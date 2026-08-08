using System;
using System.Collections.Generic;

namespace RemoteEditorSync
{
    internal sealed class PendingChangeBuffer
    {
        private readonly Dictionary<int, BufferedObjectChanges> _pending = new Dictionary<int, BufferedObjectChanges>();

        public void Clear()
        {
            _pending.Clear();
        }

        public void Clear(int instanceId)
        {
            _pending.Remove(instanceId);
        }

        public void EnqueueRename(int instanceId, string sceneName, string originalPath, string newPath, string newName)
        {
            var changes = GetOrCreate(instanceId, sceneName, newPath);

            // 未送信の再親付けがある場合、名前をそちらへ畳み込んで1RPCにまとめる
            // （別RPCにすると受信側が知らないパスを参照してしまう）
            if (changes.HasReparent)
            {
                var reparent = changes.Reparent;
                reparent.NewName = newName;
                changes.Reparent = reparent;
                return;
            }

            // 同一フラッシュ内で複数回リネームされた場合、受信側が知っている
            // 最初のパスを維持しないと対象を見つけられなくなる
            var fromPath = changes.HasRename ? changes.Rename.FromPath : originalPath;
            changes.Rename = new RenameChange
            {
                SceneName = sceneName,
                FromPath = fromPath,
                NewName = newName
            };
            changes.HasRename = true;
        }

        public void EnqueueReparent(int instanceId, string sceneName, string originalPath, string newPath, string newParentPath, string newName, int siblingIndex)
        {
            var changes = GetOrCreate(instanceId, sceneName, newPath);

            // 受信側がまだ知っている最古のパスをFromPathに使う
            string fromPath;
            if (changes.HasReparent)
            {
                fromPath = changes.Reparent.FromPath;
            }
            else if (changes.HasRename)
            {
                // 未送信のリネームは再親付けRPCのNewNameに畳み込む
                fromPath = changes.Rename.FromPath;
                changes.HasRename = false;
            }
            else
            {
                fromPath = originalPath;
            }

            changes.Reparent = new ReparentChange
            {
                SceneName = sceneName,
                FromPath = fromPath,
                NewParentPath = newParentPath,
                NewName = newName,
                SiblingIndex = siblingIndex
            };
            changes.HasReparent = true;
        }

        public void EnqueueSetActive(int instanceId, string sceneName, string path, bool active)
        {
            var changes = GetOrCreate(instanceId, sceneName, path);
            changes.Active = new SetActiveChange
            {
                SceneName = sceneName,
                Path = path,
                Active = active
            };
            changes.HasActive = true;
        }

        public void EnqueueTransform(int instanceId, string sceneName, string path, BufferedTransformChange transform)
        {
            var changes = GetOrCreate(instanceId, sceneName, path);
            transform.SceneName = sceneName;
            transform.Path = path;
            changes.Transform = transform;
            changes.TransformDirty = true;
        }

        public void EnqueueGameObjectPatch(int instanceId, string sceneName, string path, BufferedGameObjectPatch patch)
        {
            var changes = GetOrCreate(instanceId, sceneName, path);
            patch.SceneName = sceneName;
            patch.Path = path;
            if (changes.HasPatch && changes.Patch.Properties != null)
            {
                if (patch.Properties != null)
                {
                    foreach (var kvp in patch.Properties)
                    {
                        changes.Patch.Properties[kvp.Key] = kvp.Value;
                    }
                }
            }
            else
            {
                changes.Patch = patch;
            }

            changes.HasPatch = changes.Patch.Properties != null && changes.Patch.Properties.Count > 0;
        }

        public void EnqueueSerializedUpdate(int instanceId, string sceneName, string path, BufferedSerializedUpdate update)
        {
            var changes = GetOrCreate(instanceId, sceneName, path);
            update.SceneName = sceneName;
            update.Path = path;
            changes.SerializedUpdate = update;
            changes.HasSerializedUpdate = !string.IsNullOrEmpty(update.SerializedData);
            if (changes.HasSerializedUpdate)
            {
                changes.HasPatch = false;
            }
        }

        public void EnqueueComponentAdd(int instanceId, string sceneName, string path, BufferedComponentAdd add)
        {
            var changes = GetOrCreate(instanceId, sceneName, path);
            add.SceneName = sceneName;
            add.Path = path;
            changes.ComponentAdds[add.Signature] = add;
            changes.ComponentRemovals.Remove(add.Signature);
            changes.ComponentUpdates.Remove(add.Signature);
        }

        public void EnqueueComponentUpdate(int instanceId, string sceneName, string path, BufferedComponentUpdate update)
        {
            if (update.Properties == null || update.Properties.Count == 0)
            {
                return;
            }

            var changes = GetOrCreate(instanceId, sceneName, path);
            update.SceneName = sceneName;
            update.Path = path;
            changes.ComponentUpdates[update.Signature] = update;
        }

        public void EnqueueComponentRemoval(int instanceId, string sceneName, string path, BufferedComponentRemoval removal)
        {
            var changes = GetOrCreate(instanceId, sceneName, path);
            removal.SceneName = sceneName;
            removal.Path = path;
            changes.ComponentRemovals.Add(removal.Signature);
            changes.ComponentAdds.Remove(removal.Signature);
            changes.ComponentUpdates.Remove(removal.Signature);
        }

        public void Flush(
            double now,
            float transformInterval,
            Action<ReparentChange> reparentAction,
            Action<RenameChange> renameAction,
            Action<SetActiveChange> activeAction,
            Action<BufferedTransformChange> transformAction,
            Action<BufferedGameObjectPatch> patchAction,
            Action<BufferedSerializedUpdate> serializedAction,
            Action<BufferedComponentAdd> addAction,
            Action<BufferedComponentUpdate> updateAction,
            Action<BufferedComponentRemoval> removeAction)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            var toClear = new List<int>();

            foreach (var pair in _pending)
            {
                var changes = pair.Value;

                if (changes.HasReparent)
                {
                    reparentAction?.Invoke(changes.Reparent);
                    changes.HasReparent = false;
                }

                if (changes.HasRename)
                {
                    renameAction?.Invoke(changes.Rename);
                    changes.HasRename = false;
                }

                if (changes.HasActive)
                {
                    activeAction?.Invoke(changes.Active);
                    changes.HasActive = false;
                }

                if (changes.TransformDirty)
                {
                    if (now >= changes.NextTransformSendTime)
                    {
                        transformAction?.Invoke(changes.Transform);
                        changes.TransformDirty = false;
                        changes.NextTransformSendTime = now + transformInterval;
                    }
                }

                if (changes.HasPatch)
                {
                    patchAction?.Invoke(changes.Patch);
                    changes.HasPatch = false;
                }

                if (changes.HasSerializedUpdate)
                {
                    serializedAction?.Invoke(changes.SerializedUpdate);
                    changes.HasSerializedUpdate = false;
                }

                foreach (var add in changes.ComponentAdds.Values)
                {
                    addAction?.Invoke(add);
                }
                changes.ComponentAdds.Clear();

                foreach (var signature in changes.ComponentRemovals)
                {
                    removeAction?.Invoke(new BufferedComponentRemoval
                    {
                        SceneName = changes.SceneName,
                        Path = changes.Path,
                        Signature = signature
                    });
                }
                changes.ComponentRemovals.Clear();

                foreach (var update in changes.ComponentUpdates.Values)
                {
                    updateAction?.Invoke(update);
                }
                changes.ComponentUpdates.Clear();

                if (!changes.HasReparent &&
                    !changes.HasRename &&
                    !changes.HasActive &&
                    !changes.TransformDirty &&
                    !changes.HasPatch &&
                    !changes.HasSerializedUpdate &&
                    changes.ComponentAdds.Count == 0 &&
                    changes.ComponentUpdates.Count == 0 &&
                    changes.ComponentRemovals.Count == 0)
                {
                    toClear.Add(pair.Key);
                }
            }

            foreach (var id in toClear)
            {
                _pending.Remove(id);
            }
        }

        private BufferedObjectChanges GetOrCreate(int instanceId, string sceneName, string path)
        {
            if (!_pending.TryGetValue(instanceId, out var changes))
            {
                changes = new BufferedObjectChanges
                {
                    SceneName = sceneName,
                    Path = path
                };
                _pending[instanceId] = changes;
            }
            else
            {
                changes.SceneName = sceneName;
                changes.Path = path;
            }

            return changes;
        }

        private sealed class BufferedObjectChanges
        {
            public string SceneName;
            public string Path;
            public ReparentChange Reparent;
            public bool HasReparent;
            public RenameChange Rename;
            public bool HasRename;
            public SetActiveChange Active;
            public bool HasActive;
            public BufferedTransformChange Transform;
            public bool TransformDirty;
            public double NextTransformSendTime;
            public BufferedGameObjectPatch Patch;
            public bool HasPatch;
            public BufferedSerializedUpdate SerializedUpdate;
            public bool HasSerializedUpdate;
            public readonly Dictionary<ComponentSignature, BufferedComponentAdd> ComponentAdds = new Dictionary<ComponentSignature, BufferedComponentAdd>();
            public readonly Dictionary<ComponentSignature, BufferedComponentUpdate> ComponentUpdates = new Dictionary<ComponentSignature, BufferedComponentUpdate>();
            public readonly HashSet<ComponentSignature> ComponentRemovals = new HashSet<ComponentSignature>();
        }
    }

    internal struct RenameChange
    {
        public string SceneName;
        public string FromPath;
        public string NewName;
    }

    internal struct ReparentChange
    {
        public string SceneName;
        public string FromPath;
        public string NewParentPath;
        public string NewName;
        public int SiblingIndex;
    }

    internal struct SetActiveChange
    {
        public string SceneName;
        public string Path;
        public bool Active;
    }

    internal struct BufferedTransformChange
    {
        public string SceneName;
        public string Path;
        public UnityEngine.Vector3 Position;
        public UnityEngine.Vector3 Rotation;
        public UnityEngine.Vector3 Scale;
    }

    internal struct BufferedGameObjectPatch
    {
        public string SceneName;
        public string Path;
        public Dictionary<string, object> Properties;
    }

    internal struct BufferedSerializedUpdate
    {
        public string SceneName;
        public string Path;
        public string SerializedData;
    }

    internal struct BufferedComponentAdd
    {
        public string SceneName;
        public string Path;
        public ComponentSignature Signature;
        public string PropertiesJson;
    }

    internal struct BufferedComponentUpdate
    {
        public string SceneName;
        public string Path;
        public ComponentSignature Signature;
        public Dictionary<string, object> Properties;
    }

    internal struct BufferedComponentRemoval
    {
        public string SceneName;
        public string Path;
        public ComponentSignature Signature;
    }
}
