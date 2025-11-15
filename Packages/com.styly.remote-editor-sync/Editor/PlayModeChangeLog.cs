using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RemoteEditorSync
{
    /// <summary>
    /// Play中のEditor変更を記録し、Stop後に適用できるようにする
    /// </summary>
    public class PlayModeChangeLog
    {
        private static PlayModeChangeLog _instance;
        public static PlayModeChangeLog Instance => _instance ?? (_instance = new PlayModeChangeLog());

        private readonly List<ChangeEntry> _changes = new List<ChangeEntry>();

        public IReadOnlyList<ChangeEntry> Changes => _changes;

        public void Clear()
        {
            _changes.Clear();
        }

        public void RecordCreateGameObject(string sceneName, string path, string name, string parentPath,
            Vector3 position, Vector3 rotation, Vector3 scale, bool activeSelf, string primitiveType, string serializedData)
        {
            _changes.Add(new ChangeEntry
            {
                Type = ChangeType.CreateGameObject,
                SceneName = sceneName,
                Path = path,
                Description = $"Create: {sceneName}/{path}",
                CreateData = new CreateGameObjectData
                {
                    SceneName = sceneName,
                    Path = path,
                    Name = name,
                    ParentPath = parentPath,
                    Position = position,
                    Rotation = rotation,
                    Scale = scale,
                    ActiveSelf = activeSelf,
                    PrimitiveType = primitiveType,
                    SerializedData = serializedData
                }
            });
        }

        public void RecordDeleteGameObject(string sceneName, string path)
        {
            _changes.Add(new ChangeEntry
            {
                Type = ChangeType.DeleteGameObject,
                SceneName = sceneName,
                Path = path,
                Description = $"Delete: {sceneName}/{path}"
            });
        }

        public void RecordRenameGameObject(string sceneName, string oldPath, string newName)
        {
            _changes.Add(new ChangeEntry
            {
                Type = ChangeType.RenameGameObject,
                SceneName = sceneName,
                Path = oldPath,
                NewName = newName,
                Description = $"Rename: {sceneName}/{oldPath} → {newName}"
            });
        }

        public void RecordSetActive(string sceneName, string path, bool active)
        {
            _changes.Add(new ChangeEntry
            {
                Type = ChangeType.SetActive,
                SceneName = sceneName,
                Path = path,
                NewActive = active,
                Description = $"SetActive: {sceneName}/{path} = {active}"
            });
        }

        public void RecordUpdateTransform(string sceneName, string path, Vector3 position, Vector3 rotation, Vector3 scale)
        {
            // 既存のTransform変更を上書き（同じオブジェクトなら最新の値だけ保持）
            var existingIndex = _changes.FindLastIndex(c =>
                c.Type == ChangeType.UpdateTransform &&
                c.SceneName == sceneName &&
                c.Path == path);

            var entry = new ChangeEntry
            {
                Type = ChangeType.UpdateTransform,
                SceneName = sceneName,
                Path = path,
                Description = $"Transform: {sceneName}/{path}",
                TransformData = new TransformData
                {
                    SceneName = sceneName,
                    Path = path,
                    Position = position,
                    Rotation = rotation,
                    Scale = scale
                }
            };

            if (existingIndex >= 0)
            {
                _changes[existingIndex] = entry;
            }
            else
            {
                _changes.Add(entry);
            }
        }

        public void RecordUpdateGameObject(string sceneName, string path, string serializedData)
        {
            // 既存のGameObject変更を上書き（同じオブジェクトなら最新の値だけ保持）
            var existingIndex = _changes.FindLastIndex(c =>
                c.Type == ChangeType.UpdateGameObject &&
                c.SceneName == sceneName &&
                c.Path == path);

            var entry = new ChangeEntry
            {
                Type = ChangeType.UpdateGameObject,
                SceneName = sceneName,
                Path = path,
                Description = $"Update: {sceneName}/{path}",
                GameObjectData = new GameObjectData
                {
                    SceneName = sceneName,
                    Path = path,
                    SerializedData = serializedData
                }
            };

            if (existingIndex >= 0)
            {
                _changes[existingIndex] = entry;
            }
            else
            {
                _changes.Add(entry);
            }
        }

        public void RecordUpdateComponentProperties(string sceneName, string path, ComponentSignature signature, string propertiesJson)
        {
            var typeName = GetTypeDisplayName(signature.TypeName);
            var entry = new ChangeEntry
            {
                Type = ChangeType.UpdateComponentProperties,
                SceneName = sceneName,
                Path = path,
                Description = $"Update Component: {typeName} on {sceneName}/{path}",
                ComponentPropertiesData = new ComponentPropertiesData
                {
                    SceneName = sceneName,
                    Path = path,
                    Signature = signature,
                    PropertiesJson = propertiesJson
                }
            };

            var existingIndex = _changes.FindLastIndex(c =>
                c.Type == ChangeType.UpdateComponentProperties &&
                c.SceneName == sceneName &&
                c.Path == path &&
                c.ComponentPropertiesData?.Signature.Equals(signature) == true);

            if (existingIndex >= 0)
            {
                _changes[existingIndex] = entry;
            }
            else
            {
                _changes.Add(entry);
            }
        }

        public void RecordAddComponent(string sceneName, string path, ComponentSignature signature, string propertiesJson)
        {
            var typeName = GetTypeDisplayName(signature.TypeName);
            _changes.Add(new ChangeEntry
            {
                Type = ChangeType.AddComponent,
                SceneName = sceneName,
                Path = path,
                Description = $"Add Component: {typeName} on {sceneName}/{path}",
                ComponentAddData = new ComponentAddData
                {
                    SceneName = sceneName,
                    Path = path,
                    Signature = signature,
                    PropertiesJson = propertiesJson
                }
            });
        }

        public void RecordRemoveComponent(string sceneName, string path, ComponentSignature signature)
        {
            var typeName = GetTypeDisplayName(signature.TypeName);
            _changes.Add(new ChangeEntry
            {
                Type = ChangeType.RemoveComponent,
                SceneName = sceneName,
                Path = path,
                Description = $"Remove Component: {typeName} on {sceneName}/{path}",
                ComponentRemoveData = new ComponentRemoveData
                {
                    SceneName = sceneName,
                    Path = path,
                    Signature = signature
                }
            });
        }

        private string GetTypeDisplayName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
            {
                return "<Unknown>";
            }

            var shortName = assemblyQualifiedName.Split(',')[0];
            var lastDot = shortName.LastIndexOf('.');
            return lastDot >= 0 ? shortName.Substring(lastDot + 1) : shortName;
        }

        [System.Serializable]
        public class ChangeEntry
        {
            public ChangeType Type;
            public string SceneName;
            public string Path;
            public string Description;
            public bool Selected = true; // デフォルトで選択状態

            // Type別データ
            public CreateGameObjectData CreateData;
            public TransformData TransformData;
            public GameObjectData GameObjectData;
            public ComponentPropertiesData ComponentPropertiesData;
            public ComponentAddData ComponentAddData;
            public ComponentRemoveData ComponentRemoveData;
            public string NewName; // Rename用
            public bool NewActive; // SetActive用
        }

        public enum ChangeType
        {
            CreateGameObject,
            DeleteGameObject,
            RenameGameObject,
            SetActive,
            UpdateTransform,
            UpdateGameObject,
            UpdateComponentProperties,
            AddComponent,
            RemoveComponent
        }

        [System.Serializable]
        public class CreateGameObjectData
        {
            public string SceneName;
            public string Path;
            public string Name;
            public string ParentPath;
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
            public bool ActiveSelf;
            public string PrimitiveType;
            public string SerializedData;
        }

        [System.Serializable]
        public class TransformData
        {
            public string SceneName;
            public string Path;
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
        }

        [System.Serializable]
        public class GameObjectData
        {
            public string SceneName;
            public string Path;
            public string SerializedData;
        }

        [System.Serializable]
        public class ComponentPropertiesData
        {
            public string SceneName;
            public string Path;
            public ComponentSignature Signature;
            public string PropertiesJson;
        }

        [System.Serializable]
        public class ComponentAddData
        {
            public string SceneName;
            public string Path;
            public ComponentSignature Signature;
            public string PropertiesJson;
        }

        [System.Serializable]
        public class ComponentRemoveData
        {
            public string SceneName;
            public string Path;
            public ComponentSignature Signature;
        }
    }
}
