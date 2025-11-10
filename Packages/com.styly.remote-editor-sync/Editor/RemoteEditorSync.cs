using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RemoteEditorSync
{
    /// <summary>
    /// Unity Editorでの変更を検知してSTYLY NetSyncのRPC経由でクライアントに送信
    /// </summary>
    [InitializeOnLoad]
    public class RemoteEditorSync
    {
        private static Dictionary<int, ObjectState> _trackedObjects = new Dictionary<int, ObjectState>();
        private static bool _isEnabled = false;

        // 同期対象のフィルタリング設定
        private static bool _syncOnlyEditorChanges = true; // エディタ操作のみ同期
        private static string _syncTagFilter = ""; // 特定タグのみ同期（空なら全て）

        // JsonSerializerSettings to avoid circular reference errors
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.None
        };

        static RemoteEditorSync()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
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
            _isEnabled = true;
            _trackedObjects.Clear();

            // Undo/Redo（Inspector変更含む）のコールバック
            // これはエディタでの手動操作のみをキャプチャします
            Undo.postprocessModifications += OnPropertyModification;

            // ObjectChangeEventsでGameObject作成/削除/親変更を検知
            // これもエディタ操作のみを検知します
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;

            Debug.Log("[RemoteEditorSync] Enabled (Editor changes only)");
        }

        private static void Disable()
        {
            _isEnabled = false;

            Undo.postprocessModifications -= OnPropertyModification;
            ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;

            _trackedObjects.Clear();

            Debug.Log("[RemoteEditorSync] Disabled");
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
                            SendRPC("DeleteGameObject", new[] { trackedEntry.Value.Path });
                            _trackedObjects.Remove(trackedEntry.Key);
                        }
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

            foreach (var mod in modifications)
            {
                if (mod.currentValue?.target is Component component)
                {
                    modifiedObjects.Add(component.gameObject);
                }
                else if (mod.currentValue?.target is GameObject go)
                {
                    modifiedObjects.Add(go);
                }
            }

            // 各オブジェクトの変更を送信
            foreach (var go in modifiedObjects)
            {
                if (go != null && ShouldSync(go))
                {
                    SendObjectUpdate(go);
                }
            }

            return modifications;
        }

        private static void CreateObjectState(GameObject go)
        {
            var state = new ObjectState
            {
                InstanceId = go.GetInstanceID(),
                Path = GetGameObjectPath(go),
                Name = go.name,
                ActiveSelf = go.activeSelf,
                Position = go.transform.localPosition,
                Rotation = go.transform.localRotation.eulerAngles,
                Scale = go.transform.localScale
            };
            _trackedObjects[state.InstanceId] = state;
        }

        private static void SendCreateGameObject(GameObject go)
        {
            if (!ShouldSync(go)) return;

            var parentPath = go.transform.parent != null ? GetGameObjectPath(go.transform.parent.gameObject) : "";
            var primitiveType = DetectPrimitiveType(go);

            // Try to serialize the GameObject
            string serializedData = null;
            try
            {
                serializedData = EditorJsonUtility.ToJson(go, false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RemoteEditorSync] Failed to serialize GameObject '{go.name}': {e.Message}");
            }

            var data = new CreateGameObjectData
            {
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

            // 変更をチェックして送信
            if (oldState.Name != go.name || oldState.Path != newPath)
            {
                SendRPC("RenameGameObject", new[] { oldState.Path, go.name });
                oldState.Name = go.name;
                oldState.Path = newPath;
            }

            if (oldState.ActiveSelf != go.activeSelf)
            {
                SendRPC("SetActive", new[] { newPath, go.activeSelf.ToString() });
                oldState.ActiveSelf = go.activeSelf;
            }

            // Transform変更
            var t = go.transform;
            if (!VectorEquals(oldState.Position, t.localPosition) ||
                !VectorEquals(oldState.Rotation, t.localRotation.eulerAngles) ||
                !VectorEquals(oldState.Scale, t.localScale))
            {
                var data = new TransformData
                {
                    Path = newPath,
                    Position = t.localPosition,
                    Rotation = t.localRotation.eulerAngles,
                    Scale = t.localScale
                };

                SendRPC("UpdateTransform", new[] { JsonConvert.SerializeObject(data, _jsonSettings) });

                oldState.Position = t.localPosition;
                oldState.Rotation = t.localRotation.eulerAngles;
                oldState.Scale = t.localScale;
            }
        }

        private static void SendRPC(string functionName, string[] args)
        {
            if (Styly.NetSync.NetSyncManager.Instance != null)
            {
                Debug.Log($"[RemoteEditorSync] Sending RPC: {functionName}");
                Styly.NetSync.NetSyncManager.Instance.Rpc(functionName, args);
            }
            else
            {
                Debug.LogWarning("[RemoteEditorSync] Cannot send RPC: NetSyncManager.Instance is null");
            }
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
            public string Path;
            public string Name;
            public bool ActiveSelf;
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
        }

        [System.Serializable]
        private class CreateGameObjectData
        {
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
            public string Path;
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
        }
    }
}
