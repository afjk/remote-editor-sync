using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using Styly.NetSync;
using Newtonsoft.Json;

namespace RemoteEditorSync
{
    /// <summary>
    /// STYLY NetSyncのRPCを受信してエディタからの変更をクライアントに適用
    /// </summary>
    public class RemoteEditorSyncReceiver : MonoBehaviour
    {
        // キー: "sceneName:path" の形式でGameObjectをキャッシュ
        private readonly Dictionary<string, GameObject> _pathToGameObject = new Dictionary<string, GameObject>();

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

                    case "UpdateComponent":
                        HandleUpdateComponent(args);
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

        private void ForceComponentUpdate(Component component)
        {
            // 特定のコンポーネントタイプに対して、ランタイムでの確実な更新を行う
            switch (component)
            {
                case Slider slider:
                    // valueプロパティに再代入することでSetterを呼び、UpdateVisualsをトリガー
                    float sliderValue = slider.value;
                    slider.value = sliderValue;
                    break;

                case Scrollbar scrollbar:
                    // valueプロパティに再代入することでSetterを呼び、UpdateVisualsをトリガー
                    float scrollbarValue = scrollbar.value;
                    scrollbar.value = scrollbarValue;
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
        private class ComponentData
        {
            public string SceneName;
            public string Path;
            public string ComponentType;
            public string SerializedData;
        }
    }
}
