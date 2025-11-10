using UnityEngine;
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

            // 親オブジェクトを検索
            Transform parent = null;
            if (!string.IsNullOrEmpty(data.ParentPath))
            {
                var parentGo = FindGameObjectByPath(data.ParentPath);
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

            // キャッシュに追加
            _pathToGameObject[data.Path] = go;

            Debug.Log($"[RemoteEditorSyncReceiver] Created: {data.Path}");
        }

        private void HandleDeleteGameObject(string[] args)
        {
            if (args.Length < 1) return;

            string path = args[0];
            var go = FindGameObjectByPath(path);

            if (go != null)
            {
                _pathToGameObject.Remove(path);
                Destroy(go);
                Debug.Log($"[RemoteEditorSyncReceiver] Deleted: {path}");
            }
        }

        private void HandleRenameGameObject(string[] args)
        {
            if (args.Length < 2) return;

            string oldPath = args[0];
            string newName = args[1];

            var go = FindGameObjectByPath(oldPath);
            if (go != null)
            {
                _pathToGameObject.Remove(oldPath);
                go.name = newName;

                // 新しいパスで再登録
                string newPath = GetGameObjectPath(go);
                _pathToGameObject[newPath] = go;

                Debug.Log($"[RemoteEditorSyncReceiver] Renamed: {oldPath} -> {newPath}");
            }
        }

        private void HandleSetActive(string[] args)
        {
            if (args.Length < 2) return;

            string path = args[0];
            bool active = bool.Parse(args[1]);

            var go = FindGameObjectByPath(path);
            if (go != null)
            {
                go.SetActive(active);
                Debug.Log($"[RemoteEditorSyncReceiver] SetActive: {path} = {active}");
            }
        }

        private void HandleUpdateTransform(string[] args)
        {
            if (args.Length < 1) return;

            var data = JsonConvert.DeserializeObject<TransformData>(args[0], _jsonSettings);
            if (data == null) return;

            var go = FindGameObjectByPath(data.Path);
            if (go != null)
            {
                go.transform.localPosition = data.Position;
                go.transform.localRotation = Quaternion.Euler(data.Rotation);
                go.transform.localScale = data.Scale;
            }
        }

        private GameObject FindGameObjectByPath(string path)
        {
            // まずキャッシュを確認
            if (_pathToGameObject.TryGetValue(path, out var cached) && cached != null)
            {
                return cached;
            }

            // キャッシュになければ検索
            var parts = path.Split('/');
            Transform current = null;

            foreach (var part in parts)
            {
                if (current == null)
                {
                    // ルートレベルのオブジェクトを検索
                    var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
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
                _pathToGameObject[path] = result;
            }

            return result;
        }

        private void RefreshGameObjectCache()
        {
            _pathToGameObject.Clear();

            // シーン内のすべてのGameObjectをキャッシュ
            var allObjects = FindObjectsOfType<GameObject>(true);
            foreach (var go in allObjects)
            {
                string path = GetGameObjectPath(go);
                _pathToGameObject[path] = go;
            }

            Debug.Log($"[RemoteEditorSyncReceiver] Cached {_pathToGameObject.Count} GameObjects");
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
