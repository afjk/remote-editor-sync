using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Styly.NetSync;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RemoteEditorSync
{
    /// <summary>
    /// Unity Editorでの変更を検知してSTYLY NetSyncのRPC経由でクライアントに送信
    /// </summary>
    [InitializeOnLoad]
    public class RemoteEditorSync
    {
        private static Dictionary<int, ObjectState> _trackedObjects = new Dictionary<int, ObjectState>();
        private static readonly PendingChangeBuffer _pendingChanges = new PendingChangeBuffer();
        private static bool _isEnabled = false;

        private static readonly Dictionary<string, Action<string[]>> _rpcHandlers =
            new Dictionary<string, Action<string[]>>();
        private static NetSyncManager _rpcListenerSource;
        private static RemoteEditorSyncSettings Settings => RemoteEditorSyncSettings.Instance;

        // 自動同期のOn/Off設定（EditorPrefsで永続化）
        private const string AutoSyncEnabledKey = "RemoteEditorSync.AutoSyncEnabled";
        private static bool _autoSyncEnabled = true;

        // 同期対象のフィルタリング設定
        private static bool _syncOnlyEditorChanges = true; // エディタ操作のみ同期
        private static string _syncTagFilter = ""; // 特定タグのみ同期（空なら全て）

        public static bool AutoSyncEnabled
        {
            get => _autoSyncEnabled;
            set
            {
                _autoSyncEnabled = value;
                EditorPrefs.SetBool(AutoSyncEnabledKey, value);
                Debug.Log($"[RemoteEditorSync] Auto Sync: {(value ? "Enabled" : "Disabled")}");
            }
        }

        // JsonSerializerSettings to avoid circular reference errors
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.None
        };

        static RemoteEditorSync()
        {
            // EditorPrefsから設定を読み込み
            _autoSyncEnabled = EditorPrefs.GetBool(AutoSyncEnabledKey, true);

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RegisterRpcHandlers();
        }

        [MenuItem("Tools/Remote Editor Sync/Settings/Toggle Sync Only Editor Changes")]
        public static void ToggleSyncOnlyEditorChanges()
        {
            _syncOnlyEditorChanges = !_syncOnlyEditorChanges;
            Debug.Log($"[RemoteEditorSync] Sync Only Editor Changes: {_syncOnlyEditorChanges}");
        }

        [MenuItem("Tools/Remote Editor Sync/Settings/Set Tag Filter (EditorSyncOnly)")]
        public static void SetTagFilterEditorSyncOnly()
        {
            _syncTagFilter = "EditorSyncOnly";
            Debug.Log($"[RemoteEditorSync] Tag Filter set to: 'EditorSyncOnly'");
            Debug.Log("[RemoteEditorSync] Only GameObjects with 'EditorSyncOnly' tag will be synced.");
        }

        [MenuItem("Tools/Remote Editor Sync/Settings/Clear Tag Filter")]
        public static void ClearTagFilter()
        {
            _syncTagFilter = "";
            Debug.Log("[RemoteEditorSync] Tag Filter cleared. All GameObjects will be synced.");
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // 自動同期が無効の場合は何もしない
            if (!_autoSyncEnabled)
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Enable();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Disable();
            }
        }

        private static void Enable()
        {
            if (_isEnabled)
            {
                return;
            }

            _isEnabled = true;
            _trackedObjects.Clear();
            _pendingChanges.Clear();

            // Play mode開始時にChangeLogをクリア
            PlayModeChangeLog.Instance.Clear();

            // Undo/Redo（Inspector変更含む）のコールバック
            // これはエディタでの手動操作のみをキャプチャします
            Undo.postprocessModifications += OnPropertyModification;

            // ObjectChangeEventsでGameObject作成/削除/親変更を検知
            // これもエディタ操作のみを検知します
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;

            MaterialTracker.Clear();
            EnsureAllRenderersHaveAnchors();
            RegisterAllSceneMaterials();
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            EnsureRpcListener();

            Debug.Log("[RemoteEditorSync] Enabled (Editor changes only)");
        }

        private static void Disable()
        {
            if (!_isEnabled)
            {
                return;
            }

            _isEnabled = false;

            Undo.postprocessModifications -= OnPropertyModification;
            ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
            EditorApplication.update -= OnEditorUpdate;
            DetachRpcListener();

            _trackedObjects.Clear();
            MaterialTracker.Clear();
            _pendingChanges.Clear();

            Debug.Log("[RemoteEditorSync] Disabled");

            // Play mode終了後に変更一覧ウィンドウを表示
            // Edit modeに完全に戻った後に開く
            if (PlayModeChangeLog.Instance.Changes.Count > 0)
            {
                EditorApplication.delayCall += () =>
                {
                    PlayModeChangesWindow.ShowWindow();
                };
            }
        }

        private static void RegisterRpcHandlers()
        {
            _rpcHandlers["RegisterMaterialResult"] = HandleRegisterMaterialResult;
        }

        private static void EnsureRpcListener()
        {
            var manager = NetSyncManager.Instance;
            if (manager == null)
            {
                DetachRpcListener();
                return;
            }

            if (_rpcListenerSource == manager)
            {
                return;
            }

            DetachRpcListener();
            manager.OnRPCReceived.AddListener(OnNetSyncRpcReceived);
            _rpcListenerSource = manager;
        }

        private static void DetachRpcListener()
        {
            if (_rpcListenerSource != null)
            {
                _rpcListenerSource.OnRPCReceived.RemoveListener(OnNetSyncRpcReceived);
                _rpcListenerSource = null;
            }
        }

        private static void OnNetSyncRpcReceived(int senderClientNo, string functionName, string[] args)
        {
            if (!_isEnabled)
            {
                return;
            }

            var manager = NetSyncManager.Instance;
            if (manager != null && senderClientNo == manager.ClientNo)
            {
                return;
            }

            if (_rpcHandlers.TryGetValue(functionName, out var handler))
            {
                handler?.Invoke(args);
            }
        }

        private static void OnEditorUpdate()
        {
            if (!_isEnabled || !Application.isPlaying)
            {
                return;
            }

            EnsureRpcListener();
            FlushPendingChanges();

            var manager = NetSyncManager.Instance;
            if (manager == null || manager.ClientNo < 0)
            {
                return;
            }

            MaterialTracker.CheckForChanges();
            MaterialTracker.RetryPendingRegistrations();

            if (Time.frameCount % 300 == 0)
            {
                MaterialTracker.CleanupDeletedMaterials();
            }
        }

        private static void HandleRegisterMaterialResult(string[] args)
        {
            if (args == null || args.Length < 1)
            {
                return;
            }

            var data = JsonConvert.DeserializeObject<RegisterMaterialResultData>(args[0], _jsonSettings);
            if (data == null)
            {
                return;
            }

            MaterialTracker.HandleRegisterMaterialResult(data);
        }

        private static void FlushPendingChanges()
        {
            _pendingChanges.Flush(
                EditorApplication.timeSinceStartup,
                Settings.TransformFlushInterval,
                DispatchRenameChange,
                DispatchSetActive,
                DispatchTransform,
                DispatchGameObjectPatch,
                DispatchSerializedUpdate,
                DispatchComponentAdd,
                DispatchComponentUpdate,
                DispatchComponentRemoval);
        }

        private static void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!_isEnabled || !Application.isPlaying) return;

            for (int i = 0; i < stream.length; i++)
            {
                var eventType = stream.GetEventType(i);

                switch (eventType)
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        stream.GetCreateGameObjectHierarchyEvent(i, out var createEvent);
                        var createdGo = EditorUtility.InstanceIDToObject(createEvent.instanceId) as GameObject;

                        Debug.Log($"[RemoteEditorSync] CreateGameObjectHierarchy detected: {createdGo?.name ?? "null"} (instanceId: {createEvent.instanceId})");

                        if (createdGo != null)
                        {
                            if (ShouldSync(createdGo))
                            {
                                Debug.Log($"[RemoteEditorSync] Creating and syncing: {createdGo.name}");
                                CreateObjectState(createdGo);
                                SendCreateGameObject(createdGo);
                                EnsureAnchorsForHierarchy(createdGo);
                                RegisterMaterialsRecursive(createdGo);
                            }
                            else
                            {
                                Debug.Log($"[RemoteEditorSync] Filtered out (tag mismatch): {createdGo.name}");
                            }
                        }
                        break;

                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        stream.GetDestroyGameObjectHierarchyEvent(i, out var destroyEvent);
                        Debug.Log($"[RemoteEditorSync] DestroyGameObjectHierarchy detected (instanceId: {destroyEvent.instanceId})");

                        // 削除時はInstanceIDからオブジェクトを取得できないので、
                        // _trackedObjectsから探す
                        var trackedEntry = _trackedObjects.FirstOrDefault(kvp => kvp.Key == destroyEvent.instanceId);
                        if (trackedEntry.Value != null)
                        {
                            Debug.Log($"[RemoteEditorSync] Deleting tracked object: {trackedEntry.Value.Path}");
                            SendRPC("DeleteGameObject", new[] { trackedEntry.Value.SceneName, trackedEntry.Value.Path });
                            PlayModeChangeLog.Instance.RecordDeleteGameObject(trackedEntry.Value.SceneName, trackedEntry.Value.Path);
                            _trackedObjects.Remove(trackedEntry.Key);
                            _pendingChanges.Clear(trackedEntry.Key);
                        }
                        MaterialTracker.UnregisterGameObject(destroyEvent.instanceId);
                        break;

                    case ObjectChangeKind.ChangeGameObjectParent:
                        stream.GetChangeGameObjectParentEvent(i, out var parentEvent);
                        var parentChangedGo = EditorUtility.InstanceIDToObject(parentEvent.instanceId) as GameObject;
                        Debug.Log($"[RemoteEditorSync] ChangeGameObjectParent detected: {parentChangedGo?.name ?? "null"}");

                        if (parentChangedGo != null && ShouldSync(parentChangedGo))
                        {
                            SendObjectUpdate(parentChangedGo);
                        }
                        break;

                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var propEvent);
                        var changedObj = EditorUtility.InstanceIDToObject(propEvent.instanceId);

                        if (changedObj is GameObject go && ShouldSync(go))
                        {
                            Debug.Log($"[RemoteEditorSync] GameObject properties changed: {go.name}");
                            DetectAndSyncComponentChanges(go);
                            SendObjectUpdate(go);
                        }
                        else if (changedObj is Component comp && ShouldSync(comp.gameObject))
                        {
                            Debug.Log($"[RemoteEditorSync] Component properties changed: {comp.GetType().Name} on {comp.gameObject.name}");
                            SyncComponentIfChanged(comp);
                        }
                        break;

                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out var structureEvent);
                        var structureGo = EditorUtility.InstanceIDToObject(structureEvent.instanceId) as GameObject;
                        Debug.Log($"[RemoteEditorSync] ChangeGameObjectStructure detected: {structureGo?.name ?? "null"}");

                        if (structureGo != null)
                        {
                            EnsureAnchorsForHierarchy(structureGo);
                        }

                        if (structureGo != null && ShouldSync(structureGo))
                        {
                            DetectAndSyncComponentChanges(structureGo);
                            SendObjectUpdate(structureGo);
                        }
                        break;
                }
            }
        }

        private static bool ShouldSync(GameObject go)
        {
            if (go == null) return false;

            // タグフィルター
            if (!string.IsNullOrEmpty(_syncTagFilter) && !go.CompareTag(_syncTagFilter))
            {
                return false;
            }

            return true;
        }

        private static UndoPropertyModification[] OnPropertyModification(UndoPropertyModification[] modifications)
        {
            if (!_isEnabled || !Application.isPlaying) return modifications;

            // 変更されたオブジェクトごとにグループ化
            var modifiedObjects = new HashSet<GameObject>();
            var modifiedComponents = new HashSet<Component>();

            foreach (var mod in modifications)
            {
                if (mod.currentValue?.target is Component component)
                {
                    modifiedComponents.Add(component);
                    modifiedObjects.Add(component.gameObject);
                }
                else if (mod.currentValue?.target is GameObject go)
                {
                    modifiedObjects.Add(go);
                }
            }

            foreach (var component in modifiedComponents)
            {
                if (component != null && ShouldSync(component.gameObject))
                {
                    SyncComponentIfChanged(component);
                }
            }

            // 各オブジェクトの変更を送信
            foreach (var go in modifiedObjects)
            {
                if (go != null && ShouldSync(go))
                {
                    DetectAndSyncComponentChanges(go);
                    SendObjectUpdate(go);
                }
            }

            return modifications;
        }

        private static void DetectAndSyncComponentChanges(GameObject go)
        {
            if (go == null || !ShouldSync(go))
            {
                return;
            }

            int instanceId = go.GetInstanceID();
            if (!_trackedObjects.TryGetValue(instanceId, out var state))
            {
                _trackedObjects[instanceId] = new ObjectState(go);
                return;
            }

            state.DetectChanges(go, out var changes);
            foreach (var change in changes)
            {
                switch (change.Type)
                {
                    case ComponentChangeType.Added:
                        SendAddComponent(change.Component);
                        if (change.NewSnapshot != null)
                        {
                            state.ComponentSnapshots[change.Signature] = change.NewSnapshot;
                        }
                        break;
                    case ComponentChangeType.Modified:
                        SendUpdateComponentProperties(change.Component, change.PreviousSnapshot);
                        if (change.NewSnapshot != null)
                        {
                            state.ComponentSnapshots[change.Signature] = change.NewSnapshot;
                        }
                        break;
                    case ComponentChangeType.Removed:
                        SendRemoveComponent(go, change.Signature);
                        state.ComponentSnapshots.Remove(change.Signature);
                        break;
                }
            }

            UpdateMaterialRegistration(go);
        }

        private static void SyncComponentIfChanged(Component component)
        {
            if (component == null)
            {
                return;
            }

            var go = component.gameObject;
            if (!ShouldSync(go))
            {
                return;
            }

            var instanceId = go.GetInstanceID();
            if (!_trackedObjects.TryGetValue(instanceId, out var state))
            {
                _trackedObjects[instanceId] = new ObjectState(go);
                return;
            }

            var handler = ComponentSyncHandlerRegistry.GetHandler(component);
            if (handler == null)
            {
                return;
            }

            var signature = ComponentSignature.Create(component);

            if (!state.ComponentSnapshots.TryGetValue(signature, out var snapshot))
            {
                state.ComponentSnapshots[signature] = new ComponentSnapshot(component, handler);
                SendAddComponent(component);
                return;
            }

            if (snapshot.HasChanged(component, handler))
            {
                SendUpdateComponentProperties(component, snapshot);
                state.ComponentSnapshots[signature] = new ComponentSnapshot(component, handler);
            }
        }

        private static void EnsureAllRenderersHaveAnchors()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    EnsureAnchorsForHierarchy(root);
                }
            }
        }

        private static void EnsureAnchorsForHierarchy(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                MaterialAnchor.GetOrCreateForRenderer(renderer);
            }
        }

        private static void RegisterAllSceneMaterials()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    RegisterMaterialsRecursive(root);
                }
            }
        }

        private static void RegisterMaterialsRecursive(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!ShouldSync(renderer.gameObject))
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null)
                    {
                        MaterialTracker.RegisterMaterial(materials[i], renderer, i, renderer.gameObject);
                    }
                }
            }
        }

        private static void UpdateMaterialRegistration(GameObject go)
        {
            if (go == null || !ShouldSync(go))
            {
                return;
            }

            MaterialTracker.UnregisterAllMaterials(go);
            RegisterMaterialsRecursive(go);
        }

        private static void CreateObjectState(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var state = new ObjectState(go);
            _trackedObjects[state.InstanceId] = state;
        }

        private static string SerializeGameObject(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            try
            {
                return JsonUtility.ToJson(go, false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RemoteEditorSync] Failed to serialize GameObject '{go.name}': {e.Message}");
                return null;
            }
        }

        private static void SendCreateGameObject(GameObject go)
        {
            if (!ShouldSync(go)) return;

            var parentPath = go.transform.parent != null ? GetGameObjectPath(go.transform.parent.gameObject) : "";
            var primitiveType = DetectPrimitiveType(go);

            var serializedData = SerializeGameObject(go);

            var data = new CreateGameObjectData
            {
                SceneName = go.scene.name,
                Path = GetGameObjectPath(go),
                Name = go.name,
                ParentPath = parentPath,
                Position = go.transform.localPosition,
                Rotation = go.transform.localRotation.eulerAngles,
                Scale = go.transform.localScale,
                ActiveSelf = go.activeSelf,
                PrimitiveType = primitiveType,
                SerializedData = serializedData
            };

            SendRPC("CreateGameObject", new[] { JsonConvert.SerializeObject(data, _jsonSettings) });

            // Play中の変更を記録
            PlayModeChangeLog.Instance.RecordCreateGameObject(
                data.SceneName, data.Path, data.Name, data.ParentPath,
                data.Position, data.Rotation, data.Scale, data.ActiveSelf,
                data.PrimitiveType, data.SerializedData);
        }

        private static void SendUpdateComponentProperties(Component component, ComponentSnapshot previousSnapshot = null)
        {
            if (component == null)
            {
                return;
            }

            var go = component.gameObject;
            if (!ShouldSync(go))
            {
                return;
            }

            var handler = ComponentSyncHandlerRegistry.GetHandler(component);
            if (handler == null)
            {
                return;
            }

            var properties = handler.ExtractProperties(component) ?? new Dictionary<string, object>();
            if (previousSnapshot != null)
            {
                properties = previousSnapshot.ExtractDelta(properties);
            }

            if (properties == null || properties.Count == 0)
            {
                return;
            }

            var signature = previousSnapshot?.Signature ?? ComponentSignature.Create(component);
            var bufferedUpdate = new BufferedComponentUpdate
            {
                Signature = signature,
                Properties = properties
            };

            _pendingChanges.EnqueueComponentUpdate(
                go.GetInstanceID(),
                go.scene.name,
                GetGameObjectPath(go),
                bufferedUpdate);
        }

        private static void SendAddComponent(Component component)
        {
            if (component == null)
            {
                return;
            }

            var go = component.gameObject;
            if (!ShouldSync(go))
            {
                return;
            }

            var handler = ComponentSyncHandlerRegistry.GetHandler(component);
            if (handler == null)
            {
                return;
            }

            var properties = handler.ExtractProperties(component) ?? new Dictionary<string, object>();
            var propertiesJson = properties.Count > 0 ? JsonConvert.SerializeObject(properties, _jsonSettings) : null;

            var bufferedAdd = new BufferedComponentAdd
            {
                Signature = ComponentSignature.Create(component),
                PropertiesJson = propertiesJson
            };

            _pendingChanges.EnqueueComponentAdd(
                go.GetInstanceID(),
                go.scene.name,
                GetGameObjectPath(go),
                bufferedAdd);
        }

        private static void SendRemoveComponent(GameObject go, ComponentSignature signature)
        {
            if (go == null || !ShouldSync(go))
            {
                return;
            }

            var removal = new BufferedComponentRemoval
            {
                Signature = signature
            };

            _pendingChanges.EnqueueComponentRemoval(
                go.GetInstanceID(),
                go.scene.name,
                GetGameObjectPath(go),
                removal);
        }

        private static void SendObjectUpdate(GameObject go)
        {
            if (!ShouldSync(go)) return;

            int id = go.GetInstanceID();
            if (!_trackedObjects.ContainsKey(id))
            {
                CreateObjectState(go);
                return;
            }

            var oldState = _trackedObjects[id];
            var newPath = GetGameObjectPath(go);

            var sceneName = go.scene.name;

            if (oldState.Name != go.name || oldState.Path != newPath)
            {
                _pendingChanges.EnqueueRename(id, sceneName, oldState.Path, newPath, go.name);
                oldState.Name = go.name;
                oldState.Path = newPath;
            }

            if (oldState.ActiveSelf != go.activeSelf)
            {
                _pendingChanges.EnqueueSetActive(id, sceneName, newPath, go.activeSelf);
                oldState.ActiveSelf = go.activeSelf;
            }

            var t = go.transform;
            if (!VectorEquals(oldState.Position, t.localPosition, Settings.TransformPositionEpsilon) ||
                !VectorEquals(oldState.Rotation, t.localRotation.eulerAngles, Settings.TransformRotationEpsilon) ||
                !VectorEquals(oldState.Scale, t.localScale, Settings.TransformScaleEpsilon))
            {
                var transformChange = new BufferedTransformChange
                {
                    Position = t.localPosition,
                    Rotation = t.localRotation.eulerAngles,
                    Scale = t.localScale
                };

                _pendingChanges.EnqueueTransform(id, sceneName, newPath, transformChange);

                oldState.Position = t.localPosition;
                oldState.Rotation = t.localRotation.eulerAngles;
                oldState.Scale = t.localScale;
            }

            var patchProperties = new Dictionary<string, object>();
            if (oldState.Tag != go.tag)
            {
                patchProperties["tag"] = go.tag;
                oldState.Tag = go.tag;
            }

            if (oldState.Layer != go.layer)
            {
                patchProperties["layer"] = go.layer;
                oldState.Layer = go.layer;
            }

            if (oldState.IsStatic != go.isStatic)
            {
                patchProperties["isStatic"] = go.isStatic;
                oldState.IsStatic = go.isStatic;
            }

            if (patchProperties.Count > 0)
            {
                _pendingChanges.EnqueueGameObjectPatch(id, sceneName, newPath, new BufferedGameObjectPatch
                {
                    Properties = patchProperties
                });
            }

            var newSerializedData = SerializeGameObject(go);

            if (newSerializedData != null && newSerializedData != oldState.SerializedData)
            {
                _pendingChanges.EnqueueSerializedUpdate(id, sceneName, newPath, new BufferedSerializedUpdate
                {
                    SerializedData = newSerializedData
                });

                oldState.SerializedData = newSerializedData;
            }
        }

        private static void DispatchRenameChange(RenameChange change)
        {
            if (string.IsNullOrEmpty(change.SceneName) || string.IsNullOrEmpty(change.FromPath))
            {
                return;
            }

            SendRPC("RenameGameObject", new[] { change.SceneName, change.FromPath, change.NewName });
            PlayModeChangeLog.Instance.RecordRenameGameObject(change.SceneName, change.FromPath, change.NewName);
        }

        private static void DispatchSetActive(SetActiveChange change)
        {
            SendRPC("SetActive", new[] { change.SceneName, change.Path, change.Active.ToString() });
            PlayModeChangeLog.Instance.RecordSetActive(change.SceneName, change.Path, change.Active);
        }

        private static void DispatchTransform(BufferedTransformChange change)
        {
            var data = new TransformData
            {
                SceneName = change.SceneName,
                Path = change.Path,
                Position = change.Position,
                Rotation = change.Rotation,
                Scale = change.Scale
            };

            SendRPC("UpdateTransform", new[] { JsonConvert.SerializeObject(data, _jsonSettings) });
            PlayModeChangeLog.Instance.RecordUpdateTransform(data.SceneName, data.Path, data.Position, data.Rotation, data.Scale);
        }

        private static void DispatchGameObjectPatch(BufferedGameObjectPatch patch)
        {
            if (patch.Properties == null || patch.Properties.Count == 0)
            {
                return;
            }

            var data = new GameObjectPatchData
            {
                SceneName = patch.SceneName,
                Path = patch.Path,
                Properties = patch.Properties
            };

            var payload = JsonConvert.SerializeObject(data, _jsonSettings);
            SendRPC("ApplyGameObjectPatch", new[] { payload });
            PlayModeChangeLog.Instance.RecordUpdateGameObject(data.SceneName, data.Path, payload);
        }

        private static void DispatchSerializedUpdate(BufferedSerializedUpdate update)
        {
            if (string.IsNullOrEmpty(update.SerializedData))
            {
                return;
            }

            var data = new GameObjectData
            {
                SceneName = update.SceneName,
                Path = update.Path,
                SerializedData = update.SerializedData
            };

            SendRPC("UpdateGameObject", new[] { JsonConvert.SerializeObject(data, _jsonSettings) });
            PlayModeChangeLog.Instance.RecordUpdateGameObject(data.SceneName, data.Path, data.SerializedData);
        }

        private static void DispatchComponentAdd(BufferedComponentAdd add)
        {
            var data = new AddComponentData
            {
                SceneName = add.SceneName,
                Path = add.Path,
                Signature = add.Signature,
                PropertiesJson = add.PropertiesJson
            };

            SendRPC("AddComponent", new[] { JsonConvert.SerializeObject(data, _jsonSettings) });
            PlayModeChangeLog.Instance.RecordAddComponent(data.SceneName, data.Path, data.Signature, data.PropertiesJson);
        }

        private static void DispatchComponentUpdate(BufferedComponentUpdate update)
        {
            if (update.Properties == null || update.Properties.Count == 0)
            {
                return;
            }

            var propertiesJson = JsonConvert.SerializeObject(update.Properties, _jsonSettings);
            var data = new UpdateComponentPropertiesData
            {
                SceneName = update.SceneName,
                Path = update.Path,
                Signature = update.Signature,
                PropertiesJson = propertiesJson
            };

            SendRPC("UpdateComponentProperties", new[] { JsonConvert.SerializeObject(data, _jsonSettings) });
            PlayModeChangeLog.Instance.RecordUpdateComponentProperties(data.SceneName, data.Path, data.Signature, data.PropertiesJson);
        }

        private static void DispatchComponentRemoval(BufferedComponentRemoval removal)
        {
            var data = new RemoveComponentData
            {
                SceneName = removal.SceneName,
                Path = removal.Path,
                Signature = removal.Signature
            };

            SendRPC("RemoveComponent", new[] { JsonConvert.SerializeObject(data, _jsonSettings) });
            PlayModeChangeLog.Instance.RecordRemoveComponent(data.SceneName, data.Path, data.Signature);
        }

        private static void SendRPC(string functionName, string[] args)
        {
            var payloadBytes = CalculatePayloadBytes(functionName, args);
            RemoteEditorSyncMetrics.Record(functionName, payloadBytes);

            if (Settings.DryRun)
            {
                Debug.Log($"[RemoteEditorSync] Dry run RPC '{functionName}' ({payloadBytes} bytes)");
                return;
            }

            var manager = NetSyncManager.Instance;
            if (manager != null)
            {
                Debug.Log($"[RemoteEditorSync] Sending RPC: {functionName} ({payloadBytes} bytes)");
                manager.Rpc(functionName, args);
            }
            else
            {
                Debug.LogWarning("[RemoteEditorSync] Cannot send RPC: NetSyncManager.Instance is null");
            }
        }

        private static int CalculatePayloadBytes(string functionName, string[] args)
        {
            int total = Encoding.UTF8.GetByteCount(functionName ?? string.Empty);
            if (args != null)
            {
                foreach (var arg in args)
                {
                    total += Encoding.UTF8.GetByteCount(arg ?? string.Empty);
                }
            }

            return total;
        }

        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return "";

            var path = go.name;
            var parent = go.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        

        private static bool VectorEquals(Vector3 a, Vector3 b, float epsilon = 0.0001f)
        {
            return Mathf.Abs(a.x - b.x) < epsilon &&
                   Mathf.Abs(a.y - b.y) < epsilon &&
                   Mathf.Abs(a.z - b.z) < epsilon;
        }

        private static string DetectPrimitiveType(GameObject go)
        {
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var meshName = meshFilter.sharedMesh.name;
                // Unity's primitive meshes have these names
                if (meshName.Contains("Sphere")) return "Sphere";
                if (meshName.Contains("Cube")) return "Cube";
                if (meshName.Contains("Capsule")) return "Capsule";
                if (meshName.Contains("Cylinder")) return "Cylinder";
                if (meshName.Contains("Plane")) return "Plane";
                if (meshName.Contains("Quad")) return "Quad";
            }
            return null;
        }

        [System.Serializable]
        private class ObjectState
        {
            public int InstanceId;
            public string SceneName;
            public string Path;
            public string Name;
            public bool ActiveSelf;
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
            public string Tag;
            public int Layer;
            public bool IsStatic;
            public string SerializedData; // EditorJsonUtility serialized GameObject data
            public Dictionary<ComponentSignature, ComponentSnapshot> ComponentSnapshots;

            public ObjectState(GameObject go)
            {
                InstanceId = go.GetInstanceID();
                SceneName = go.scene.name;
                Path = GetGameObjectPath(go);
                Name = go.name;
                ActiveSelf = go.activeSelf;
                Position = go.transform.localPosition;
                Rotation = go.transform.localRotation.eulerAngles;
                Scale = go.transform.localScale;
                Tag = go.tag;
                Layer = go.layer;
                IsStatic = go.isStatic;
                SerializedData = SerializeGameObject(go);
                ComponentSnapshots = new Dictionary<ComponentSignature, ComponentSnapshot>();
                CaptureComponentSnapshots(go);
            }

            public void CaptureComponentSnapshots(GameObject go)
            {
                ComponentSnapshots.Clear();
                var components = go.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    var handler = ComponentSyncHandlerRegistry.GetHandler(component);
                    if (handler == null)
                    {
                        continue;
                    }

                    var snapshot = new ComponentSnapshot(component, handler);
                    ComponentSnapshots[snapshot.Signature] = snapshot;
                }
            }

            public void DetectChanges(GameObject go, out List<ComponentChange> changes)
            {
                changes = new List<ComponentChange>();
                var currentComponents = go.GetComponents<Component>();
                var currentSignatures = new HashSet<ComponentSignature>();

                foreach (var component in currentComponents)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    var handler = ComponentSyncHandlerRegistry.GetHandler(component);
                    if (handler == null)
                    {
                        continue;
                    }

                    var signature = ComponentSignature.Create(component);
                    currentSignatures.Add(signature);

                    if (!ComponentSnapshots.TryGetValue(signature, out var snapshot))
                    {
                        changes.Add(new ComponentChange
                        {
                            Type = ComponentChangeType.Added,
                            Component = component,
                            Signature = signature,
                            NewSnapshot = new ComponentSnapshot(component, handler)
                        });
                    }
                    else if (snapshot.HasChanged(component, handler))
                    {
                        changes.Add(new ComponentChange
                        {
                            Type = ComponentChangeType.Modified,
                            Component = component,
                            Signature = signature,
                            PreviousSnapshot = snapshot,
                            NewSnapshot = new ComponentSnapshot(component, handler)
                        });
                    }
                }

                var removedSignatures = new List<ComponentSignature>();
                foreach (var kvp in ComponentSnapshots)
                {
                    if (!currentSignatures.Contains(kvp.Key))
                    {
                        removedSignatures.Add(kvp.Key);
                        changes.Add(new ComponentChange
                        {
                            Type = ComponentChangeType.Removed,
                            Signature = kvp.Key
                        });
                    }
                }

                foreach (var signature in removedSignatures)
                {
                    ComponentSnapshots.Remove(signature);
                }
            }
        }

        private enum ComponentChangeType
        {
            Added,
            Modified,
            Removed
        }

        private class ComponentChange
        {
            public ComponentChangeType Type;
            public Component Component;
            public ComponentSignature Signature;
            public ComponentSnapshot PreviousSnapshot;
            public ComponentSnapshot NewSnapshot;
        }

        [System.Serializable]
        private class CreateGameObjectData
        {
            public string SceneName;
            public string Path;
            public string Name;
            public string ParentPath;
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
            public bool ActiveSelf;
            public string PrimitiveType; // "Sphere", "Cube", "Capsule", "Cylinder", "Plane", "Quad", or null
            public string SerializedData; // EditorJsonUtility serialized GameObject data
        }

        [System.Serializable]
        private class TransformData
        {
            public string SceneName;
            public string Path;
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
        }

        [System.Serializable]
        private class GameObjectData
        {
            public string SceneName;
            public string Path;
            public string SerializedData; // EditorJsonUtility serialized GameObject data
        }

        [System.Serializable]
        private class GameObjectPatchData
        {
            public string SceneName;
            public string Path;
            public Dictionary<string, object> Properties;
        }

        [System.Serializable]
        private class UpdateComponentPropertiesData
        {
            public string SceneName;
            public string Path;
            public ComponentSignature Signature;
            public string PropertiesJson;
        }

        [System.Serializable]
        private class AddComponentData
        {
            public string SceneName;
            public string Path;
            public ComponentSignature Signature;
            public string PropertiesJson;
        }

        [System.Serializable]
        private class RemoveComponentData
        {
            public string SceneName;
            public string Path;
            public ComponentSignature Signature;
        }
    }
}
