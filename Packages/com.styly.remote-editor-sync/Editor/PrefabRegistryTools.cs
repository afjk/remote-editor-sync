using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RemoteEditorSync
{
    /// <summary>
    /// Editor-side helpers for maintaining the <see cref="PrefabRegistry"/> asset that
    /// lets a built client resolve a prefab GUID back to a prefab.
    /// </summary>
    public static class PrefabRegistryTools
    {
        private const string ResourcesFolder = "Assets/Resources";
        private const string AssetPath = ResourcesFolder + "/" + PrefabRegistry.DefaultAssetName + ".asset";

        private const string MenuRoot = "Tools/Remote Editor Sync/Prefab Registry/";

        /// <summary>
        /// Returns the registry the runtime will load, creating it on demand.
        /// </summary>
        public static PrefabRegistry GetOrCreateRegistry()
        {
            var registry = FindRegistry();
            if (registry != null)
            {
                return registry;
            }

            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            registry = ScriptableObject.CreateInstance<PrefabRegistry>();
            AssetDatabase.CreateAsset(registry, AssetPath);
            AssetDatabase.SaveAssets();
            PrefabRegistry.ResetRuntimeInstanceCache();

            Debug.Log($"[PrefabRegistryTools] Created prefab registry at {AssetPath}");
            return registry;
        }

        /// <summary>
        /// Finds an existing registry anywhere in the project, preferring the one the
        /// runtime can actually load (a Resources folder).
        /// </summary>
        public static PrefabRegistry FindRegistry()
        {
            var direct = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(AssetPath);
            if (direct != null)
            {
                return direct;
            }

            var guids = AssetDatabase.FindAssets($"t:{nameof(PrefabRegistry)}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(path);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        [MenuItem(MenuRoot + "Create or Select Registry")]
        public static void CreateOrSelectRegistry()
        {
            var registry = GetOrCreateRegistry();
            Selection.activeObject = registry;
            EditorGUIUtility.PingObject(registry);
        }

        [MenuItem(MenuRoot + "Register Selected Prefabs")]
        public static void RegisterSelectedPrefabs()
        {
            var prefabs = GetSelectedPrefabs();
            if (prefabs.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Prefabが選択されていません",
                    "ProjectウィンドウでPrefabアセットを選択してから実行してください。",
                    "OK");
                return;
            }

            var registry = GetOrCreateRegistry();
            int added = RegisterPrefabs(registry, prefabs);

            EditorUtility.DisplayDialog(
                "Prefabを登録しました",
                $"選択: {prefabs.Count}件\n新規または更新: {added}件\n\n" +
                "クライアント側で使うにはビルドし直す必要があります。",
                "OK");
        }

        [MenuItem(MenuRoot + "Register Selected Prefabs", true)]
        private static bool RegisterSelectedPrefabsValidate()
        {
            return GetSelectedPrefabs().Count > 0;
        }

        [MenuItem(MenuRoot + "Register Prefabs In Folder...")]
        public static void RegisterPrefabsInFolder()
        {
            var absolute = EditorUtility.OpenFolderPanel("登録するPrefabを含むフォルダ", "Assets", "");
            if (string.IsNullOrEmpty(absolute))
            {
                return;
            }

            var dataPath = Application.dataPath;
            if (!absolute.StartsWith(dataPath))
            {
                EditorUtility.DisplayDialog(
                    "対象外のフォルダ",
                    "Assets以下のフォルダを選択してください。",
                    "OK");
                return;
            }

            var relative = "Assets" + absolute.Substring(dataPath.Length);
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { relative });

            var prefabs = new List<GameObject>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            if (prefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("Prefabが見つかりません", $"{relative} 以下にPrefabがありませんでした。", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                "Prefabを登録",
                $"{relative} 以下の {prefabs.Count} 件のPrefabを登録します。\n\n" +
                "登録したPrefabはビルドに含まれるようになります。数が多いとビルドサイズが増えるため、\n" +
                "同期に使うものだけを対象にしてください。",
                "登録",
                "キャンセル"))
            {
                return;
            }

            var registry = GetOrCreateRegistry();
            int added = RegisterPrefabs(registry, prefabs);

            EditorUtility.DisplayDialog(
                "Prefabを登録しました",
                $"対象: {prefabs.Count}件\n新規または更新: {added}件\n\n" +
                "クライアント側で使うにはビルドし直す必要があります。",
                "OK");
        }

        [MenuItem(MenuRoot + "Remove Invalid Entries")]
        public static void RemoveInvalidEntries()
        {
            var registry = FindRegistry();
            if (registry == null)
            {
                EditorUtility.DisplayDialog("レジストリがありません", "Prefab Registryがまだ作成されていません。", "OK");
                return;
            }

            int removed = registry.RemoveInvalidEntries();
            if (removed > 0)
            {
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
                PrefabRegistry.ResetRuntimeInstanceCache();
            }

            EditorUtility.DisplayDialog("整理しました", $"無効なエントリを {removed} 件削除しました。", "OK");
        }

        private static int RegisterPrefabs(PrefabRegistry registry, IReadOnlyList<GameObject> prefabs)
        {
            int changed = 0;
            foreach (var prefab in prefabs)
            {
                var path = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var guid = AssetDatabase.AssetPathToGUID(path);
                if (registry.Register(guid, prefab))
                {
                    changed++;
                }
            }

            if (changed > 0)
            {
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
                PrefabRegistry.ResetRuntimeInstanceCache();
            }

            return changed;
        }

        private static List<GameObject> GetSelectedPrefabs()
        {
            var result = new List<GameObject>();
            foreach (var obj in Selection.GetFiltered<GameObject>(SelectionMode.Assets))
            {
                if (PrefabUtility.IsPartOfPrefabAsset(obj))
                {
                    result.Add(obj);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Warns once per prefab when it is instantiated during play but is missing from the
    /// registry, so the problem surfaces in the editor rather than only as an error on
    /// the client.
    /// </summary>
    internal static class PrefabRegistryWarning
    {
        private static readonly HashSet<string> _warnedGuids = new HashSet<string>();

        public static void Reset()
        {
            _warnedGuids.Clear();
        }

        public static void WarnIfUnregistered(string guid, string assetPath)
        {
            if (string.IsNullOrEmpty(guid) || !_warnedGuids.Add(guid))
            {
                return;
            }

            var registry = PrefabRegistryTools.FindRegistry();
            if (registry != null && registry.Contains(guid))
            {
                return;
            }

            Debug.LogWarning(
                $"[RemoteEditorSync] Prefab '{assetPath}' is not registered in the Prefab Registry. " +
                "Clients cannot instantiate it. Register it via Tools > Remote Editor Sync > Prefab Registry > " +
                "Register Selected Prefabs, then rebuild the client.");
        }
    }
}
