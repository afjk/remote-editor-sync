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

        public void RecordUpdateComponent(string sceneName, string path, string componentType, string serializedData)
        {
            // 既存の同じComponent変更を上書き（同じコンポーネントなら最新の値だけ保持）
            var existingIndex = _changes.FindLastIndex(c =>
                c.Type == ChangeType.UpdateComponent &&
                c.SceneName == sceneName &&
                c.Path == path &&
                c.ComponentData?.ComponentType == componentType);

            // ComponentTypeから表示名を抽出
            var typeName = componentType.Split(',')[0].Split('.').Last();

            var entry = new ChangeEntry
            {
                Type = ChangeType.UpdateComponent,
                SceneName = sceneName,
                Path = path,
                Description = $"Update Component: {typeName} on {sceneName}/{path}",
                ComponentData = new ComponentData
                {
                    SceneName = sceneName,
                    Path = path,
                    ComponentType = componentType,
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

        public void RecordSetComponentEnabled(string sceneName, string path, string componentType, int componentIndex, bool enabled)
        {
            var typeName = componentType.Split(',')[0].Split('.').Last();
            _changes.Add(new ChangeEntry
            {
                Type = ChangeType.SetComponentEnabled,
                SceneName = sceneName,
                Path = path,
                Description = $"Component Enabled: {typeName}[{componentIndex}] = {enabled}",
                ComponentEnabledData = new ComponentEnabledData
                {
                    SceneName = sceneName,
                    Path = path,
                    ComponentType = componentType,
                    ComponentIndex = componentIndex,
                    Enabled = enabled
                }
            });
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
            public ComponentData ComponentData;
            public ComponentEnabledData ComponentEnabledData;
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
            UpdateComponent,
            SetComponentEnabled
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
        public class ComponentData
        {
            public string SceneName;
            public string Path;
            public string ComponentType;
            public string SerializedData;
        }

        [System.Serializable]
        public class ComponentEnabledData
        {
            public string SceneName;
            public string Path;
            public string ComponentType;
            public int ComponentIndex;
            public bool Enabled;
        }
    }
}
