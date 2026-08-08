using System.Collections.Generic;
using UnityEngine;

namespace RemoteEditorSync
{
    /// <summary>
    /// Maps prefab asset GUIDs to prefab references so a built client can instantiate
    /// the same prefab the editor did.
    ///
    /// The editor identifies a prefab by its asset GUID, but <c>AssetDatabase</c> does not
    /// exist at runtime. Holding the prefabs in a serialized asset solves both halves of
    /// the problem: it gives the client a GUID lookup, and — because the asset references
    /// the prefabs directly — it pulls them into the player build.
    ///
    /// Consequence worth knowing: a prefab must be registered *before* the client is built.
    /// Registering it afterwards updates the editor's copy only, and the client will report
    /// the GUID as unknown.
    /// </summary>
    [CreateAssetMenu(fileName = DefaultAssetName, menuName = "Remote Editor Sync/Prefab Registry")]
    public class PrefabRegistry : ScriptableObject
    {
        /// <summary>
        /// Name the asset must have inside a Resources folder to be discovered at runtime.
        /// </summary>
        public const string DefaultAssetName = "RemoteEditorSyncPrefabRegistry";

        [System.Serializable]
        public class Entry
        {
            public string Guid;
            public GameObject Prefab;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        private Dictionary<string, GameObject> _lookup;
        private static PrefabRegistry _runtimeInstance;
        private static bool _runtimeInstanceSearched;

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>
        /// Finds the registry shipped with the build. Cached, including the negative
        /// result, so a project that does not use prefab sync does not pay for a
        /// Resources lookup on every incoming RPC.
        /// </summary>
        public static PrefabRegistry GetRuntimeInstance()
        {
            if (_runtimeInstanceSearched)
            {
                return _runtimeInstance;
            }

            _runtimeInstanceSearched = true;
            _runtimeInstance = Resources.Load<PrefabRegistry>(DefaultAssetName);
            return _runtimeInstance;
        }

        /// <summary>
        /// Clears the cached runtime lookup. Called by the editor tooling after the
        /// asset is edited, so play mode does not keep serving a stale table.
        /// </summary>
        public static void ResetRuntimeInstanceCache()
        {
            _runtimeInstance = null;
            _runtimeInstanceSearched = false;
        }

        public bool TryGetPrefab(string guid, out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }

            EnsureLookup();
            return _lookup.TryGetValue(guid, out prefab) && prefab != null;
        }

        public bool Contains(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }

            EnsureLookup();
            return _lookup.ContainsKey(guid);
        }

        /// <summary>
        /// Adds or replaces an entry. Editor-side tooling calls this; it is a no-op
        /// for a null prefab so a broken reference cannot poison the table.
        /// </summary>
        public bool Register(string guid, GameObject prefab)
        {
            if (string.IsNullOrEmpty(guid) || prefab == null)
            {
                return false;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i] != null && _entries[i].Guid == guid)
                {
                    if (_entries[i].Prefab == prefab)
                    {
                        return false;
                    }

                    _entries[i].Prefab = prefab;
                    _lookup = null;
                    return true;
                }
            }

            _entries.Add(new Entry { Guid = guid, Prefab = prefab });
            _lookup = null;
            return true;
        }

        /// <summary>
        /// Drops entries whose prefab reference was lost (asset deleted) and any duplicates.
        /// </summary>
        public int RemoveInvalidEntries()
        {
            var seen = new HashSet<string>();
            int removed = 0;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Guid) || entry.Prefab == null || !seen.Add(entry.Guid))
                {
                    _entries.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                _lookup = null;
            }

            return removed;
        }

        private void EnsureLookup()
        {
            if (_lookup != null)
            {
                return;
            }

            _lookup = new Dictionary<string, GameObject>();
            foreach (var entry in _entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Guid) || entry.Prefab == null)
                {
                    continue;
                }

                _lookup[entry.Guid] = entry.Prefab;
            }
        }

        private void OnEnable()
        {
            // Serialized data may have changed underneath us (asset reimport, undo).
            _lookup = null;
        }
    }
}
