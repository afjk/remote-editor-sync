using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Styly.NetSync;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RemoteEditorSync
{
    /// <summary>
    /// STYLY NetSyncのRPCを受信してエディタからの変更をクライアントに適用
    /// </summary>
    public class RemoteEditorSyncReceiver : MonoBehaviour
    {
        // キー: "sceneName:path" の形式でGameObjectをキャッシュ
        private readonly Dictionary<string, GameObject> _pathToGameObject = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Dictionary<string, MaterialPropertyValue>> _materialStateCache = new Dictionary<string, Dictionary<string, MaterialPropertyValue>>();

        // JsonSerializerSettings to avoid circular reference errors
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        private void Start()
        {
            if (NetSyncManager.Instance != null)
            {
                NetSyncManager.Instance.OnRPCReceived.AddListener(OnRPCReceived);
                Debug.Log("[RemoteEditorSyncReceiver] Listening for RPCs");

                // 既存のGameObjectをキャッシュ
                RefreshGameObjectCache();
            }
            else
            {
                Debug.LogWarning("[RemoteEditorSyncReceiver] NetSyncManager.Instance is null");
            }
        }

        private void OnDestroy()
        {
            if (NetSyncManager.Instance != null)
            {
                NetSyncManager.Instance.OnRPCReceived.RemoveListener(OnRPCReceived);
            }
        }

        private void OnRPCReceived(int senderClientNo, string functionName, string[] args)
        {
            // 自分が送信したRPCは無視（エディタ側でも受信してしまうため）
            if (NetSyncManager.Instance != null && senderClientNo == NetSyncManager.Instance.ClientNo)
            {
                Debug.Log($"[RemoteEditorSyncReceiver] Ignoring own RPC: {functionName} (from Client#{senderClientNo})");
                return;
            }

            try
            {
                Debug.Log($"[RemoteEditorSyncReceiver] Processing RPC from Client#{senderClientNo}: {functionName}");

                switch (functionName)
                {
                    case "CreateGameObject":
                        HandleCreateGameObject(args);
                        break;

                    case "DeleteGameObject":
                        HandleDeleteGameObject(args);
                        break;

                    case "RenameGameObject":
                        HandleRenameGameObject(args);
                        break;

                    case "SetActive":
                        HandleSetActive(args);
                        break;

                    case "UpdateTransform":
                        HandleUpdateTransform(args);
                        break;

                    case "UpdateGameObject":
                        HandleUpdateGameObject(args);
                        break;

                    case "ApplyGameObjectPatch":
                        HandleApplyGameObjectPatch(args);
                        break;

                    case "UpdateComponent":
                        HandleUpdateComponent(args);
                        break;

                    case "SetComponentEnabled":
                        HandleSetComponentEnabled(args);
                        break;

                    case "UpdateComponentProperties":
                        HandleUpdateComponentProperties(args);
                        break;

                    case "AddComponent":
                        HandleAddComponent(args);
                        break;

                    case "RemoveComponent":
                        HandleRemoveComponent(args);
                        break;

                    case "RegisterMaterial":
                        HandleRegisterMaterial(args);
                        break;

                    case "UnregisterMaterial":
                        HandleUnregisterMaterial(args);
                        break;

                    case "UpdateMaterialProperties":
                        HandleUpdateMaterialProperties(args);
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RemoteEditorSyncReceiver] Error handling RPC '{functionName}': {e.Message}");
            }
        }

        private void HandleCreateGameObject(string[] args)
        {
            if (args.Length < 1) return;

            var data = JsonConvert.DeserializeObject<CreateGameObjectData>(args[0], _jsonSettings);
            if (data == null) return;

            // シーンを取得
            var scene = SceneManager.GetSceneByName(data.SceneName);
            if (!scene.IsValid())
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Scene not found: {data.SceneName}");
                return;
            }

            // 親オブジェクトを検索
            Transform parent = null;
            if (!string.IsNullOrEmpty(data.ParentPath))
            {
                var parentGo = FindGameObjectByPath(data.SceneName, data.ParentPath);
                if (parentGo != null)
                {
                    parent = parentGo.transform;
                }
            }

            GameObject go = null;

            // プリミティブタイプがあればそれを使って生成
            if (!string.IsNullOrEmpty(data.PrimitiveType))
            {
                PrimitiveType primitiveType;
                if (System.Enum.TryParse(data.PrimitiveType, out primitiveType))
                {
                    go = GameObject.CreatePrimitive(primitiveType);
                    go.name = data.Name;
                    Debug.Log($"[RemoteEditorSyncReceiver] Created primitive: {data.PrimitiveType}");
                }
            }

            // プリミティブでなければ空のGameObjectを作成
            if (go == null)
            {
                go = new GameObject(data.Name);
            }

            // シーンに移動（親がnullの場合）
            if (parent == null)
            {
                SceneManager.MoveGameObjectToScene(go, scene);
            }

            // Transform設定
            go.transform.SetParent(parent, false);
            go.transform.localPosition = data.Position;
            go.transform.localRotation = Quaternion.Euler(data.Rotation);
            go.transform.localScale = data.Scale;
            go.SetActive(data.ActiveSelf);

            // シリアライズデータがあれば適用（ただしRuntimeでは制限あり）
            if (!string.IsNullOrEmpty(data.SerializedData))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(data.SerializedData, go);
                    Debug.Log($"[RemoteEditorSyncReceiver] Applied serialized data to: {data.Name}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[RemoteEditorSyncReceiver] Failed to apply serialized data: {e.Message}");
                }
            }

            // キャッシュに追加（シーン名を含むキー）
            string cacheKey = $"{data.SceneName}:{data.Path}";
            _pathToGameObject[cacheKey] = go;

            Debug.Log($"[RemoteEditorSyncReceiver] Created: {data.SceneName}/{data.Path}");
        }

        private void HandleDeleteGameObject(string[] args)
        {
            if (args.Length < 2) return;

            string sceneName = args[0];
            string path = args[1];
            var go = FindGameObjectByPath(sceneName, path);

            if (go != null)
            {
                string cacheKey = $"{sceneName}:{path}";
                _pathToGameObject.Remove(cacheKey);
                Destroy(go);
                Debug.Log($"[RemoteEditorSyncReceiver] Deleted: {sceneName}/{path}");
            }
        }

        private void HandleRenameGameObject(string[] args)
        {
            if (args.Length < 3) return;

            string sceneName = args[0];
            string oldPath = args[1];
            string newName = args[2];

            var go = FindGameObjectByPath(sceneName, oldPath);
            if (go != null)
            {
                string oldCacheKey = $"{sceneName}:{oldPath}";
                _pathToGameObject.Remove(oldCacheKey);
                go.name = newName;

                // 新しいパスで再登録
                string newPath = GetGameObjectPath(go);
                string newCacheKey = $"{sceneName}:{newPath}";
                _pathToGameObject[newCacheKey] = go;

                Debug.Log($"[RemoteEditorSyncReceiver] Renamed: {sceneName}/{oldPath} -> {sceneName}/{newPath}");
            }
        }

        private void HandleSetActive(string[] args)
        {
            if (args.Length < 3) return;

            string sceneName = args[0];
            string path = args[1];
            bool active = bool.Parse(args[2]);

            var go = FindGameObjectByPath(sceneName, path);
            if (go != null)
            {
                go.SetActive(active);
                Debug.Log($"[RemoteEditorSyncReceiver] SetActive: {sceneName}/{path} = {active}");
            }
        }

        private void HandleUpdateTransform(string[] args)
        {
            if (args.Length < 1) return;

            var data = JsonConvert.DeserializeObject<TransformData>(args[0], _jsonSettings);
            if (data == null) return;

            var go = FindGameObjectByPath(data.SceneName, data.Path);
            if (go != null)
            {
                go.transform.localPosition = data.Position;
                go.transform.localRotation = Quaternion.Euler(data.Rotation);
                go.transform.localScale = data.Scale;
            }
        }

        private void HandleUpdateGameObject(string[] args)
        {
            if (args.Length < 1) return;

            var data = JsonConvert.DeserializeObject<GameObjectData>(args[0], _jsonSettings);
            if (data == null) return;

            var go = FindGameObjectByPath(data.SceneName, data.Path);
            if (go != null)
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(data.SerializedData, go);
                    Debug.Log($"[RemoteEditorSyncReceiver] Updated GameObject: {data.SceneName}/{data.Path}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[RemoteEditorSyncReceiver] Failed to apply serialized data to '{data.Path}': {e.Message}");
                }
            }
        }

        private void HandleUpdateComponent(string[] args)
        {
            Debug.Log($"[RemoteEditorSyncReceiver] HandleUpdateComponent called, args.Length={args.Length}");
            if (args.Length < 1)
            {
                Debug.LogWarning("[RemoteEditorSyncReceiver] HandleUpdateComponent: No args");
                return;
            }

            Debug.Log($"[RemoteEditorSyncReceiver] Deserializing ComponentData from: {args[0].Substring(0, System.Math.Min(100, args[0].Length))}...");
            var data = JsonConvert.DeserializeObject<ComponentData>(args[0], _jsonSettings);
            if (data == null)
            {
                Debug.LogWarning("[RemoteEditorSyncReceiver] HandleUpdateComponent: Failed to deserialize ComponentData");
                return;
            }

            Debug.Log($"[RemoteEditorSyncReceiver] Looking for GameObject: {data.SceneName}/{data.Path}");
            var go = FindGameObjectByPath(data.SceneName, data.Path);
            if (go == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] GameObject not found: {data.SceneName}/{data.Path}");
                return;
            }

            Debug.Log($"[RemoteEditorSyncReceiver] Found GameObject: {go.name}, getting component type: {data.ComponentType}");

            // ComponentTypeからTypeを取得
            var componentType = System.Type.GetType(data.ComponentType);
            if (componentType == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Component type not found: {data.ComponentType}");
                return;
            }

            Debug.Log($"[RemoteEditorSyncReceiver] Component type resolved: {componentType.Name}");

            // Componentを取得
            var component = go.GetComponent(componentType);
            if (component == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Component not found on GameObject: {componentType.Name}");
                return;
            }

            Debug.Log($"[RemoteEditorSyncReceiver] Found component, applying data. Data length: {data.SerializedData.Length}");

            try
            {
                JsonUtility.FromJsonOverwrite(data.SerializedData, component);
                
                // ランタイムでの確実な反映のために特定のコンポーネントタイプを個別処理
                ForceComponentUpdate(component);
                
                Debug.Log($"[RemoteEditorSyncReceiver] Updated Component: {componentType.Name} on {data.SceneName}/{data.Path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RemoteEditorSyncReceiver] Failed to apply serialized data to component '{componentType.Name}': {e.Message}\nStack: {e.StackTrace}");
            }
        }

        private void HandleApplyGameObjectPatch(string[] args)
        {
            if (args == null || args.Length < 1)
            {
                return;
            }

            var data = JsonConvert.DeserializeObject<GameObjectPatchData>(args[0], _jsonSettings);
            if (data == null || data.Properties == null || data.Properties.Count == 0)
            {
                return;
            }

            var go = FindGameObjectByPath(data.SceneName, data.Path);
            if (go == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] GameObject not found for patch: {data.SceneName}/{data.Path}");
                return;
            }

            foreach (var kvp in data.Properties)
            {
                try
                {
                    switch (kvp.Key)
                    {
                        case "tag":
                            if (kvp.Value is string tagValue)
                            {
                                go.tag = tagValue;
                            }
                            break;
                        case "layer":
                            if (kvp.Value != null)
                            {
                                go.layer = System.Convert.ToInt32(kvp.Value);
                            }
                            break;
                        case "isStatic":
                            if (kvp.Value is bool boolValue)
                            {
                                go.isStatic = boolValue;
                            }
                            else if (kvp.Value != null && bool.TryParse(kvp.Value.ToString(), out var parsed))
                            {
                                go.isStatic = parsed;
                            }
                            break;
                        default:
                            Debug.Log($"[RemoteEditorSyncReceiver] Unknown patch key '{kvp.Key}'");
                            break;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[RemoteEditorSyncReceiver] Failed to apply patch '{kvp.Key}': {e.Message}");
                }
            }

            Debug.Log($"[RemoteEditorSyncReceiver] Applied GameObject patch to {data.SceneName}/{data.Path}");
        }

        private void HandleSetComponentEnabled(string[] args)
        {
            if (args.Length < 1) return;

            var data = JsonConvert.DeserializeObject<ComponentEnabledData>(args[0], _jsonSettings);
            if (data == null) return;

            var go = FindGameObjectByPath(data.SceneName, data.Path);
            if (go == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] GameObject not found for SetComponentEnabled: {data.SceneName}/{data.Path}");
                return;
            }

            var componentType = System.Type.GetType(data.ComponentType);
            if (componentType == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Component type not found: {data.ComponentType}");
                return;
            }

            var components = go.GetComponents(componentType);
            if (data.ComponentIndex < 0 || data.ComponentIndex >= components.Length)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Component index out of range: {componentType.Name}[{data.ComponentIndex}] on {data.SceneName}/{data.Path}");
                return;
            }

            var component = components[data.ComponentIndex];
            if (component == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Component missing: {componentType.Name}[{data.ComponentIndex}] on {data.SceneName}/{data.Path}");
                return;
            }

            if (!TrySetComponentEnabled(component, data.Enabled))
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Component does not support enabled property: {componentType.Name}");
                return;
            }

            ForceComponentUpdate(component);
            Debug.Log($"[RemoteEditorSyncReceiver] Set Component Enabled: {componentType.Name}[{data.ComponentIndex}] on {data.SceneName}/{data.Path} = {data.Enabled}");
        }

        private void HandleUpdateComponentProperties(string[] args)
        {
            if (args.Length < 1) return;

            var data = JsonConvert.DeserializeObject<UpdateComponentPropertiesData>(args[0], _jsonSettings);
            if (data == null) return;

            var go = FindGameObjectByPath(data.SceneName, data.Path);
            if (go == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] GameObject not found for property update: {data.SceneName}/{data.Path}");
                return;
            }

            var component = data.Signature.Resolve(go);
            if (component == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Component not found for signature: {data.Signature.TypeName}");
                return;
            }

            var handler = ComponentSyncHandlerRegistry.GetHandler(component);
            if (handler == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] No handler available for {component.GetType().Name}");
                return;
            }

            Dictionary<string, object> properties = null;
            if (!string.IsNullOrEmpty(data.PropertiesJson))
            {
                properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(data.PropertiesJson);
            }

            handler.ApplyProperties(component, properties ?? new Dictionary<string, object>());
            ForceComponentUpdate(component);

            Debug.Log($"[RemoteEditorSyncReceiver] Updated Component Properties: {component.GetType().Name} on {data.SceneName}/{data.Path}");
        }

        private void HandleAddComponent(string[] args)
        {
            if (args.Length < 1) return;

            var data = JsonConvert.DeserializeObject<AddComponentData>(args[0], _jsonSettings);
            if (data == null) return;

            var go = FindGameObjectByPath(data.SceneName, data.Path);
            if (go == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] GameObject not found for component addition: {data.SceneName}/{data.Path}");
                return;
            }

            var componentType = System.Type.GetType(data.Signature.TypeName);
            if (componentType == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Component type not found: {data.Signature.TypeName}");
                return;
            }

            var component = go.AddComponent(componentType);
            if (component == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Failed to add component of type {componentType.Name}");
                return;
            }

            if (!string.IsNullOrEmpty(data.PropertiesJson))
            {
                var properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(data.PropertiesJson);
                var handler = ComponentSyncHandlerRegistry.GetHandler(component);
                handler?.ApplyProperties(component, properties);
            }

            ForceComponentUpdate(component);
            Debug.Log($"[RemoteEditorSyncReceiver] Added component: {componentType.Name} on {data.SceneName}/{data.Path}");
        }

        private void HandleRemoveComponent(string[] args)
        {
            if (args.Length < 1) return;

            var data = JsonConvert.DeserializeObject<RemoveComponentData>(args[0], _jsonSettings);
            if (data == null) return;

            var go = FindGameObjectByPath(data.SceneName, data.Path);
            if (go == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] GameObject not found for component removal: {data.SceneName}/{data.Path}");
                return;
            }

            var component = data.Signature.Resolve(go);
            if (component == null)
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Component not found for removal: {data.Signature.TypeName}");
                return;
            }

            Destroy(component);
            Debug.Log($"[RemoteEditorSyncReceiver] Removed component: {data.Signature.TypeName} from {data.SceneName}/{data.Path}");
        }

        private void HandleRegisterMaterial(string[] args)
        {
            if (args.Length < 1)
                return;

            var data = JsonConvert.DeserializeObject<RegisterMaterialData>(args[0], _jsonSettings);
            if (data == null)
                return;

            if (MaterialAnchorRegistry.Instance == null)
            {
                Debug.LogWarning("[RemoteEditorSyncReceiver] MaterialAnchorRegistry is missing from the scene.");
                SendRegisterMaterialResult(data.Signature.RuntimeMaterialId, false, "MaterialAnchorRegistry missing");
                return;
            }

            bool success = MaterialAnchorRegistry.Instance.RegisterMaterialDynamic(data.Signature, out var errorMessage);
            if (success)
            {
                Debug.Log($"[RemoteEditorSyncReceiver] Registered Material: {data.Signature.RuntimeMaterialId}");
            }
            else
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Failed to register material {data.Signature.RuntimeMaterialId}: {errorMessage}");
            }

            SendRegisterMaterialResult(data.Signature.RuntimeMaterialId, success, errorMessage);
        }

        private void HandleUnregisterMaterial(string[] args)
        {
            if (args.Length < 1)
                return;

            var data = JsonConvert.DeserializeObject<UnregisterMaterialData>(args[0], _jsonSettings);
            if (data == null)
                return;

            if (MaterialAnchorRegistry.Instance == null)
            {
                Debug.LogWarning("[RemoteEditorSyncReceiver] MaterialAnchorRegistry is missing from the scene.");
                return;
            }

            MaterialAnchorRegistry.Instance.UnregisterMaterialDynamic(data.RuntimeMaterialId);
            _materialStateCache.Remove(data.RuntimeMaterialId);
            Debug.Log($"[RemoteEditorSyncReceiver] Unregistered Material: {data.RuntimeMaterialId}");
        }

        private void HandleUpdateMaterialProperties(string[] args)
        {
            if (args.Length < 1)
                return;

            var data = JsonConvert.DeserializeObject<UpdateMaterialPropertiesData>(args[0], _jsonSettings);
            if (data == null)
                return;

            if (MaterialAnchorRegistry.Instance == null)
            {
                Debug.LogWarning("[RemoteEditorSyncReceiver] MaterialAnchorRegistry is missing from the scene.");
                return;
            }

            var materials = MaterialAnchorRegistry.Instance.FindMaterials(data.Signature);
            if (materials == null || materials.Count == 0)
            {
                Debug.LogError($"[RemoteEditorSyncReceiver] Material not found for id {data.Signature.RuntimeMaterialId}");
                return;
            }

            string payload = data.PropertiesJson ?? string.Empty;
            if (data.IsCompressed && !CompressionUtility.TryDecompressFromBase64(payload, out payload))
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Failed to decompress material payload for {data.Signature.RuntimeMaterialId}");
                return;
            }

            var incoming = JsonConvert.DeserializeObject<Dictionary<string, MaterialPropertyValue>>(payload, _jsonSettings);
            if (incoming == null)
            {
                return;
            }

            var state = GetOrCreateMaterialState(data.Signature.RuntimeMaterialId);
            if (!data.IsDelta)
            {
                state.Clear();
            }

            foreach (var kvp in incoming)
            {
                if (kvp.Value == null)
                {
                    state.Remove(kvp.Key);
                }
                else
                {
                    state[kvp.Key] = kvp.Value;
                }
            }

            Debug.Log($"[RemoteEditorSyncReceiver] Applying material update for {data.Signature.RuntimeMaterialId} (shader {data.Signature.ShaderName})");

            foreach (var material in materials)
            {
                ApplyMaterialProperties(material, state);
            }

            Debug.Log($"[RemoteEditorSyncReceiver] Updated Material Properties: {data.Signature.RuntimeMaterialId} ({materials.Count} material(s))");
        }

        private void SendRegisterMaterialResult(string runtimeMaterialId, bool success, string errorMessage)
        {
            var data = new RegisterMaterialResultData
            {
                RuntimeMaterialId = runtimeMaterialId,
                Success = success,
                ErrorMessage = errorMessage
            };

            var json = JsonConvert.SerializeObject(data, _jsonSettings);
            SendRPC("RegisterMaterialResult", new[] { json });
        }

        private Dictionary<string, MaterialPropertyValue> GetOrCreateMaterialState(string runtimeMaterialId)
        {
            if (!_materialStateCache.TryGetValue(runtimeMaterialId, out var state))
            {
                state = new Dictionary<string, MaterialPropertyValue>();
                _materialStateCache[runtimeMaterialId] = state;
            }

            return state;
        }

        private void ApplyMaterialProperties(Material material, Dictionary<string, MaterialPropertyValue> properties)
        {
            foreach (var kvp in properties)
            {
                if (!material.HasProperty(kvp.Key))
                {
                    continue;
                }

                if (kvp.Value == null)
                {
                    continue;
                }

                try
                {
                    switch (kvp.Value.Type)
                    {
                        case MaterialPropertyType.Color:
                            material.SetColor(kvp.Key, kvp.Value.ColorValue.ToColor());
                            break;
                        case MaterialPropertyType.Float:
                            material.SetFloat(kvp.Key, kvp.Value.FloatValue);
                            break;
                        case MaterialPropertyType.Vector:
                            material.SetVector(kvp.Key, kvp.Value.VectorValue.ToVector4());
                            break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RemoteEditorSyncReceiver] Failed to set material property {kvp.Key} on {material.name}: {e.Message}");
                }
            }
        }

        private void ForceComponentUpdate(Component component)
        {
            // 特定のコンポーネントタイプに対して、ランタイムでの確実な更新を行う
            switch (component)
            {
                case Slider slider:
                    // Reflectionでprotectedメソッド UpdateVisuals() を呼び出し
                    InvokeUpdateVisuals(slider);
                    break;

                case Scrollbar scrollbar:
                    // Reflectionでprotectedメソッド UpdateVisuals() を呼び出し
                    InvokeUpdateVisuals(scrollbar);
                    break;

                case Toggle toggle:
                    toggle.SetIsOnWithoutNotify(toggle.isOn);
                    toggle.Rebuild(CanvasUpdate.Layout);
                    toggle.Rebuild(CanvasUpdate.PreRender);
                    break;

                case InputField inputField:
                    inputField.SetTextWithoutNotify(inputField.text);
                    inputField.ForceLabelUpdate();
                    break;

                case Dropdown dropdown:
                    dropdown.SetValueWithoutNotify(dropdown.value);
                    dropdown.RefreshShownValue();
                    break;

                case Graphic graphic:
                    graphic.SetAllDirty();
                    break;

                case Renderer renderer:
                    // Rendererの場合、強制的に更新をトリガー
                    renderer.enabled = renderer.enabled;
                    break;
                    
                case Collider collider:
                    // Colliderの場合、物理演算に反映させる
                    collider.enabled = collider.enabled;
                    break;
                    
                case MonoBehaviour monoBehaviour:
                    // MonoBehaviourの場合、enable/disableで更新をトリガー
                    bool wasEnabled = monoBehaviour.enabled;
                    monoBehaviour.enabled = false;
                    monoBehaviour.enabled = wasEnabled;
                    break;
                    
                default:
                    // その他のコンポーネントは特別な処理なし
                    break;
            }
        }

        private bool TrySetComponentEnabled(Component component, bool enabled)
        {
            switch (component)
            {
                case Behaviour behaviour:
                    behaviour.enabled = enabled;
                    return true;
                case Renderer renderer:
                    renderer.enabled = enabled;
                    return true;
                case Collider collider:
                    collider.enabled = enabled;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Reflectionを使ってprotectedメソッド UpdateVisuals() を呼び出す
        /// </summary>
        private void InvokeUpdateVisuals(Selectable selectable)
        {
            if (selectable == null) return;

            // Slider/Scrollbarの必須参照フィールドがnullでないか確認
            if (!ValidateSelectableReferences(selectable))
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Skipping UpdateVisuals() on {selectable.GetType().Name}: Required references are null (likely due to cross-client instanceID serialization)");
                return;
            }

            try
            {
                // UpdateVisualsメソッドを取得（protectedメソッド）
                MethodInfo updateVisualsMethod = selectable.GetType().GetMethod(
                    "UpdateVisuals",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (updateVisualsMethod != null)
                {
                    updateVisualsMethod.Invoke(selectable, null);
                    Debug.Log($"[RemoteEditorSyncReceiver] Called UpdateVisuals() on {selectable.GetType().Name}");
                }
                else
                {
                    Debug.LogWarning($"[RemoteEditorSyncReceiver] UpdateVisuals method not found on {selectable.GetType().Name}");
                }
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                // InnerExceptionに実際のエラーが含まれている
                Debug.LogError($"[RemoteEditorSyncReceiver] Failed to invoke UpdateVisuals on {selectable.GetType().Name}: {tie.InnerException?.Message ?? tie.Message}\n{tie.InnerException?.StackTrace ?? tie.StackTrace}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RemoteEditorSyncReceiver] Failed to invoke UpdateVisuals: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Selectable(Slider/Scrollbar等)の必須参照フィールドが有効かチェックし、nullなら再構築を試みる
        /// </summary>
        private bool ValidateSelectableReferences(Selectable selectable)
        {
            // Scrollbar固有のチェックと再構築
            if (selectable is Scrollbar scrollbar)
            {
                if (scrollbar.handleRect == null)
                {
                    Debug.LogWarning($"[RemoteEditorSyncReceiver] Scrollbar.handleRect is null on {scrollbar.gameObject.name}, attempting to reconstruct...");
                    if (!ReconstructScrollbarReferences(scrollbar))
                    {
                        return false;
                    }
                }
            }

            // Slider固有のチェックと再構築
            if (selectable is Slider slider)
            {
                if (slider.handleRect == null)
                {
                    Debug.LogWarning($"[RemoteEditorSyncReceiver] Slider.handleRect is null on {slider.gameObject.name}, attempting to reconstruct...");
                    if (!ReconstructSliderReferences(slider))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Scrollbarの参照（handleRect等）を子オブジェクトから再構築
        /// </summary>
        private bool ReconstructScrollbarReferences(Scrollbar scrollbar)
        {
            try
            {
                // 標準的な階層: Scrollbar > Sliding Area > Handle
                Transform slidingArea = scrollbar.transform.Find("Sliding Area");
                if (slidingArea != null)
                {
                    Transform handle = slidingArea.Find("Handle");
                    if (handle != null)
                    {
                        RectTransform handleRect = handle.GetComponent<RectTransform>();
                        if (handleRect != null)
                        {
                            // Reflectionでprivateフィールド m_HandleRect を設定
                            FieldInfo handleRectField = typeof(Scrollbar).GetField("m_HandleRect", BindingFlags.Instance | BindingFlags.NonPublic);
                            if (handleRectField != null)
                            {
                                handleRectField.SetValue(scrollbar, handleRect);
                                Debug.Log($"[RemoteEditorSyncReceiver] Successfully reconstructed Scrollbar.handleRect on {scrollbar.gameObject.name}");

                                // targetGraphicも設定（Handleの画像）
                                Graphic targetGraphic = handle.GetComponent<Graphic>();
                                if (targetGraphic != null)
                                {
                                    FieldInfo targetGraphicField = typeof(Selectable).GetField("m_TargetGraphic", BindingFlags.Instance | BindingFlags.NonPublic);
                                    targetGraphicField?.SetValue(scrollbar, targetGraphic);
                                }

                                return true;
                            }
                        }
                    }
                }

                Debug.LogWarning($"[RemoteEditorSyncReceiver] Failed to find Handle in standard hierarchy for {scrollbar.gameObject.name}");
                return false;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RemoteEditorSyncReceiver] Error reconstructing Scrollbar references: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sliderの参照（handleRect等）を子オブジェクトから再構築
        /// </summary>
        private bool ReconstructSliderReferences(Slider slider)
        {
            try
            {
                // 標準的な階層を検索: Slider > Handle Slide Area > Handle
                Transform handleSlideArea = slider.transform.Find("Handle Slide Area");
                if (handleSlideArea != null)
                {
                    Transform handle = handleSlideArea.Find("Handle");
                    if (handle != null)
                    {
                        RectTransform handleRect = handle.GetComponent<RectTransform>();
                        if (handleRect != null)
                        {
                            // Reflectionでprivateフィールド m_HandleRect を設定
                            FieldInfo handleRectField = typeof(Slider).GetField("m_HandleRect", BindingFlags.Instance | BindingFlags.NonPublic);
                            if (handleRectField != null)
                            {
                                handleRectField.SetValue(slider, handleRect);
                                Debug.Log($"[RemoteEditorSyncReceiver] Successfully reconstructed Slider.handleRect on {slider.gameObject.name}");

                                // targetGraphicも設定
                                Graphic targetGraphic = handle.GetComponent<Graphic>();
                                if (targetGraphic != null)
                                {
                                    FieldInfo targetGraphicField = typeof(Selectable).GetField("m_TargetGraphic", BindingFlags.Instance | BindingFlags.NonPublic);
                                    targetGraphicField?.SetValue(slider, targetGraphic);
                                }

                                return true;
                            }
                        }
                    }
                }

                Debug.LogWarning($"[RemoteEditorSyncReceiver] Failed to find Handle in standard hierarchy for {slider.gameObject.name}");
                return false;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RemoteEditorSyncReceiver] Error reconstructing Slider references: {e.Message}");
                return false;
            }
        }

        private GameObject FindGameObjectByPath(string sceneName, string path)
        {
            string cacheKey = $"{sceneName}:{path}";

            // まずキャッシュを確認
            if (_pathToGameObject.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            // シーンを取得
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid())
            {
                Debug.LogWarning($"[RemoteEditorSyncReceiver] Scene not found: {sceneName}");
                return null;
            }

            // キャッシュになければ検索
            var parts = path.Split('/');
            Transform current = null;

            foreach (var part in parts)
            {
                if (current == null)
                {
                    // ルートレベルのオブジェクトを検索（指定されたシーン内）
                    var rootObjects = scene.GetRootGameObjects();
                    current = System.Array.Find(rootObjects, go => go.name == part)?.transform;
                }
                else
                {
                    // 子オブジェクトを検索
                    current = current.Find(part);
                }

                if (current == null) break;
            }

            var result = current?.gameObject;
            if (result != null)
            {
                _pathToGameObject[cacheKey] = result;
            }

            return result;
        }

        private void RefreshGameObjectCache()
        {
            _pathToGameObject.Clear();

            // すべてのロード済みシーンをイテレート
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                // シーン内のすべてのGameObjectをキャッシュ
                var rootObjects = scene.GetRootGameObjects();
                foreach (var rootGo in rootObjects)
                {
                    CacheGameObjectRecursive(scene.name, rootGo);
                }
            }

            Debug.Log($"[RemoteEditorSyncReceiver] Cached {_pathToGameObject.Count} GameObjects from {SceneManager.sceneCount} scenes");
        }

        private void CacheGameObjectRecursive(string sceneName, GameObject go)
        {
            string path = GetGameObjectPath(go);
            string cacheKey = $"{sceneName}:{path}";
            _pathToGameObject[cacheKey] = go;

            // 子オブジェクトも再帰的にキャッシュ
            foreach (Transform child in go.transform)
            {
                CacheGameObjectRecursive(sceneName, child.gameObject);
            }
        }

        private void SendRPC(string functionName, string[] args)
        {
            if (NetSyncManager.Instance == null)
            {
                return;
            }

            NetSyncManager.Instance.Rpc(functionName, args);
        }

        private string GetGameObjectPath(GameObject go)
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
            public string SerializedData;
        }

        [System.Serializable]
        private class GameObjectPatchData
        {
            public string SceneName;
            public string Path;
            public Dictionary<string, object> Properties;
        }

        [System.Serializable]
        private class ComponentData
        {
            public string SceneName;
            public string Path;
            public string ComponentType;
            public string SerializedData;
        }

        [System.Serializable]
        private class ComponentEnabledData
        {
            public string SceneName;
            public string Path;
            public string ComponentType;
            public int ComponentIndex;
            public bool Enabled;
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
