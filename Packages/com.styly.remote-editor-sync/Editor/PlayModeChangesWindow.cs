using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;

namespace RemoteEditorSync
{
    /// <summary>
    /// Play中の変更一覧を表示し、Edit modeに適用するEditorWindow
    /// </summary>
    public class PlayModeChangesWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private bool _selectAll = true;

        public static void ShowWindow()
        {
            var window = GetWindow<PlayModeChangesWindow>("Play Mode Changes");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private void OnGUI()
        {
            var changes = PlayModeChangeLog.Instance.Changes;

            if (changes.Count == 0)
            {
                EditorGUILayout.HelpBox("No changes recorded during Play mode.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Play中の変更: {changes.Count}件", EditorStyles.boldLabel);

            // シーンのサマリー表示
            var sceneNames = changes.Select(c => c.SceneName).Distinct().ToList();
            var sceneInfo = string.Join(", ", sceneNames);
            EditorGUILayout.LabelField($"対象シーン: {sceneInfo}", EditorStyles.miniLabel);

            // 開いていないシーンがある場合、警告表示
            var unopenedScenes = sceneNames.Where(sceneName =>
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByName(sceneName);
                return !scene.IsValid() || !scene.isLoaded;
            }).ToList();

            if (unopenedScenes.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"警告: {unopenedScenes.Count}個のシーンが現在開いていません（{string.Join(", ", unopenedScenes)}）\n" +
                    "適用時に自動的に開きます。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();

            // 全選択/全解除ボタン
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全て選択"))
            {
                foreach (var change in changes)
                {
                    change.Selected = true;
                }
            }
            if (GUILayout.Button("全て解除"))
            {
                foreach (var change in changes)
                {
                    change.Selected = false;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // 変更一覧（スクロール可能）
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            for (int i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                change.Selected = EditorGUILayout.Toggle(change.Selected, GUILayout.Width(20));

                // タイプ別のアイコンとラベル
                string icon = GetIconForChangeType(change.Type);
                EditorGUILayout.LabelField(icon, GUILayout.Width(30));
                EditorGUILayout.LabelField(change.Description);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            // 適用ボタン
            var selectedCount = changes.Count(c => c.Selected);
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = selectedCount > 0;
            if (GUILayout.Button($"選択した変更を適用 ({selectedCount}件)", GUILayout.Height(30)))
            {
                ApplySelectedChanges();
            }
            GUI.enabled = true;

            if (GUILayout.Button("キャンセル", GUILayout.Height(30), GUILayout.Width(100)))
            {
                PlayModeChangeLog.Instance.Clear();
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }

        private string GetIconForChangeType(PlayModeChangeLog.ChangeType type)
        {
            switch (type)
            {
                case PlayModeChangeLog.ChangeType.CreateGameObject:
                    return "➕";
                case PlayModeChangeLog.ChangeType.DeleteGameObject:
                    return "➖";
                case PlayModeChangeLog.ChangeType.RenameGameObject:
                    return "✏️";
                case PlayModeChangeLog.ChangeType.ReparentGameObject:
                    return "🔀";
                case PlayModeChangeLog.ChangeType.SetActive:
                    return "👁";
                case PlayModeChangeLog.ChangeType.UpdateTransform:
                    return "📐";
                case PlayModeChangeLog.ChangeType.UpdateGameObject:
                    return "🔄";
                case PlayModeChangeLog.ChangeType.UpdateComponentProperties:
                    return "⚙️";
                case PlayModeChangeLog.ChangeType.AddComponent:
                    return "🧩";
                case PlayModeChangeLog.ChangeType.RemoveComponent:
                    return "❌";
                default:
                    return "•";
            }
        }

        private void ApplySelectedChanges()
        {
            var changes = PlayModeChangeLog.Instance.Changes.Where(c => c.Selected).ToList();

            if (changes.Count == 0)
            {
                EditorUtility.DisplayDialog("変更なし", "適用する変更が選択されていません。", "OK");
                return;
            }

            // 必要なシーンをチェック
            var requiredScenes = changes.Select(c => c.SceneName).Distinct().ToList();
            var scenesToLoad = new List<string>();

            foreach (var sceneName in requiredScenes)
            {
                var scene = EditorSceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    scenesToLoad.Add(sceneName);
                }
            }

            // 開いていないシーンがある場合、警告とシーン開く確認
            if (scenesToLoad.Count > 0)
            {
                var sceneList = string.Join("\n  - ", scenesToLoad);
                if (!EditorUtility.DisplayDialog(
                    "シーンを開く必要があります",
                    $"以下のシーンが現在開いていません：\n  - {sceneList}\n\n" +
                    $"これらのシーンを追加で開いて変更を適用しますか？\n" +
                    $"（適用後もシーンは開いたままになります）",
                    "シーンを開いて適用",
                    "キャンセル"))
                {
                    return;
                }

                // シーンを開く
                foreach (var sceneName in scenesToLoad)
                {
                    // シーンファイルを検索
                    var sceneGuids = UnityEditor.AssetDatabase.FindAssets($"t:Scene {sceneName}");
                    if (sceneGuids.Length == 0)
                    {
                        EditorUtility.DisplayDialog(
                            "エラー",
                            $"シーン '{sceneName}' が見つかりませんでした。\n\n" +
                            $"Assets内に該当するシーンファイルが存在するか確認してください。",
                            "OK");
                        return;
                    }

                    var scenePath = UnityEditor.AssetDatabase.GUIDToAssetPath(sceneGuids[0]);
                    var loadedScene = EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);

                    if (!loadedScene.IsValid())
                    {
                        EditorUtility.DisplayDialog(
                            "エラー",
                            $"シーン '{sceneName}' を開けませんでした。",
                            "OK");
                        return;
                    }

                    Debug.Log($"[PlayModeChangesWindow] Opened scene: {sceneName} ({scenePath})");
                }
            }

            if (!EditorUtility.DisplayDialog(
                "変更を適用",
                $"{changes.Count}件の変更をEdit modeシーンに適用します。\n\nこの操作は取り消せません。続行しますか？",
                "適用",
                "キャンセル"))
            {
                return;
            }

            int successCount = 0;
            int errorCount = 0;

            Undo.SetCurrentGroupName("Apply Play Mode Changes");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var change in changes)
            {
                try
                {
                    ApplyChange(change);
                    successCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[PlayModeChangesWindow] Failed to apply change: {change.Description}\n{e.Message}");
                    errorCount++;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            // シーンをダーティにする
            EditorSceneManager.MarkAllScenesDirty();

            PlayModeChangeLog.Instance.Clear();

            EditorUtility.DisplayDialog(
                "適用完了",
                $"成功: {successCount}件\nエラー: {errorCount}件",
                "OK");

            Close();
        }

        private void ApplyChange(PlayModeChangeLog.ChangeEntry change)
        {
            var scene = EditorSceneManager.GetSceneByName(change.SceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                // この時点でシーンが開いていない場合は予期しないエラー
                Debug.LogError($"[PlayModeChangesWindow] Scene not loaded (this should not happen): {change.SceneName}");
                throw new System.Exception($"Scene '{change.SceneName}' is not loaded. This is an internal error.");
            }

            switch (change.Type)
            {
                case PlayModeChangeLog.ChangeType.CreateGameObject:
                    ApplyCreateGameObject(change.CreateData);
                    break;

                case PlayModeChangeLog.ChangeType.DeleteGameObject:
                    ApplyDeleteGameObject(change.SceneName, change.Path);
                    break;

                case PlayModeChangeLog.ChangeType.RenameGameObject:
                    ApplyRenameGameObject(change.SceneName, change.Path, change.NewName);
                    break;

                case PlayModeChangeLog.ChangeType.ReparentGameObject:
                    ApplyReparentGameObject(change.SceneName, change.Path, change.NewParentPath, change.NewName, change.SiblingIndex);
                    break;

                case PlayModeChangeLog.ChangeType.SetActive:
                    ApplySetActive(change.SceneName, change.Path, change.NewActive);
                    break;

                case PlayModeChangeLog.ChangeType.UpdateTransform:
                    ApplyUpdateTransform(change.TransformData);
                    break;

                case PlayModeChangeLog.ChangeType.UpdateGameObject:
                    ApplyUpdateGameObject(change.GameObjectData);
                    break;

                case PlayModeChangeLog.ChangeType.UpdateComponentProperties:
                    ApplyUpdateComponentProperties(change.ComponentPropertiesData);
                    break;

                case PlayModeChangeLog.ChangeType.AddComponent:
                    ApplyAddComponent(change.ComponentAddData);
                    break;

                case PlayModeChangeLog.ChangeType.RemoveComponent:
                    ApplyRemoveComponent(change.ComponentRemoveData);
                    break;
            }
        }

        private void ApplyCreateGameObject(PlayModeChangeLog.CreateGameObjectData data)
        {
            var scene = EditorSceneManager.GetSceneByName(data.SceneName);

            // 親オブジェクトを検索
            Transform parent = null;
            if (!string.IsNullOrEmpty(data.ParentPath))
            {
                var parentGo = FindGameObjectByPath(scene, data.ParentPath);
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
                }
            }

            // プリミティブでなければ空のGameObjectを作成
            // Transform派生型（RectTransform等）は後からAddComponentできないため、
            // 生成時に指定する必要がある
            if (go == null)
            {
                var transformType = ResolveTransformType(data.Components);
                go = transformType != null
                    ? new GameObject(data.Name, transformType)
                    : new GameObject(data.Name);
            }

            // シーンに移動
            if (parent == null)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            }

            // Transform設定
            go.transform.SetParent(parent, false);
            go.transform.localPosition = data.Position;
            go.transform.localRotation = Quaternion.Euler(data.Rotation);
            go.transform.localScale = data.Scale;
            go.SetActive(data.ActiveSelf);

            // 作成時点のコンポーネント一覧を再現
            if (data.Components != null)
            {
                foreach (var componentData in data.Components)
                {
                    ApplyComponentInit(go, componentData);
                }
            }

            Undo.RegisterCreatedObjectUndo(go, "Create GameObject");

            Debug.Log($"[PlayModeChangesWindow] Created: {data.SceneName}/{data.Path}");
        }

        /// <summary>
        /// 同梱されたコンポーネント一覧からTransform派生型（RectTransform等）を探す。
        /// GameObject生成時にしか指定できないため、生成前に解決する必要がある。
        /// 素のTransformは既定で付くのでnullを返す。
        /// </summary>
        private static System.Type ResolveTransformType(List<ComponentInitData> components)
        {
            if (components == null) return null;

            foreach (var componentData in components)
            {
                if (componentData == null || string.IsNullOrEmpty(componentData.Signature.TypeName))
                {
                    continue;
                }

                var type = ComponentSignature.ResolveType(componentData.Signature.TypeName);
                if (type != null && type != typeof(Transform) && typeof(Transform).IsAssignableFrom(type))
                {
                    return type;
                }
            }

            return null;
        }

        private void ApplyComponentInit(GameObject go, ComponentInitData data)
        {
            if (data == null || string.IsNullOrEmpty(data.Signature.TypeName)) return;

            var componentType = ComponentSignature.ResolveType(data.Signature.TypeName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                Debug.LogWarning($"[PlayModeChangesWindow] Component type not found: {data.Signature.TypeName}");
                return;
            }

            Component component;
            if (typeof(Transform).IsAssignableFrom(componentType))
            {
                // GameObjectは必ず1つだけTransform（派生型含む）を持ち、後から追加も交換もできない。
                // 生成時に正しい型で作られているはずなので、それを使う。
                component = componentType.IsInstanceOfType(go.transform) ? go.transform : null;
                if (component == null)
                {
                    Debug.LogWarning($"[PlayModeChangesWindow] Cannot apply {componentType.Name} to '{go.name}': it already has a {go.transform.GetType().Name}");
                    return;
                }
            }
            else
            {
                var existing = go.GetComponents(componentType);
                if (data.Signature.Index >= 0 && data.Signature.Index < existing.Length)
                {
                    component = existing[data.Signature.Index];
                }
                else
                {
                    component = go.AddComponent(componentType);
                }
            }

            if (component == null)
            {
                Debug.LogWarning($"[PlayModeChangesWindow] Failed to add component: {componentType.Name}");
                return;
            }

            if (!string.IsNullOrEmpty(data.PropertiesJson))
            {
                var properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(data.PropertiesJson);
                var handler = ComponentSyncHandlerRegistry.GetHandler(component);
                handler?.ApplyProperties(component, properties);
            }
        }

        private void ApplyReparentGameObject(string sceneName, string fromPath, string newParentPath, string newName, int siblingIndex)
        {
            var scene = EditorSceneManager.GetSceneByName(sceneName);
            var go = FindGameObjectByPath(scene, fromPath);

            if (go == null)
            {
                Debug.LogWarning($"[PlayModeChangesWindow] GameObject not found for reparent: {sceneName}/{fromPath}");
                return;
            }

            Transform newParent = null;
            if (!string.IsNullOrEmpty(newParentPath))
            {
                var parentGo = FindGameObjectByPath(scene, newParentPath);
                if (parentGo == null)
                {
                    Debug.LogWarning($"[PlayModeChangesWindow] New parent not found for reparent: {sceneName}/{newParentPath}");
                    return;
                }

                newParent = parentGo.transform;
            }

            Undo.SetTransformParent(go.transform, newParent, "Reparent GameObject");

            if (newParent == null && go.scene != scene && scene.IsValid())
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            }

            if (!string.IsNullOrEmpty(newName) && go.name != newName)
            {
                Undo.RecordObject(go, "Rename GameObject");
                go.name = newName;
            }

            if (siblingIndex >= 0)
            {
                // SetTransformParentは並び順の変更まではUndoに含めないため、明示的に記録する
                Undo.RecordObject(go.transform, "Reorder GameObject");
                go.transform.SetSiblingIndex(siblingIndex);
            }

            Debug.Log($"[PlayModeChangesWindow] Reparented: {sceneName}/{fromPath} → {(string.IsNullOrEmpty(newParentPath) ? "<root>" : newParentPath)}");
        }

        private void ApplyDeleteGameObject(string sceneName, string path)
        {
            var scene = EditorSceneManager.GetSceneByName(sceneName);
            var go = FindGameObjectByPath(scene, path);

            if (go != null)
            {
                Undo.DestroyObjectImmediate(go);
                Debug.Log($"[PlayModeChangesWindow] Deleted: {sceneName}/{path}");
            }
        }

        private void ApplyRenameGameObject(string sceneName, string oldPath, string newName)
        {
            var scene = EditorSceneManager.GetSceneByName(sceneName);
            var go = FindGameObjectByPath(scene, oldPath);

            if (go != null)
            {
                Undo.RecordObject(go, "Rename GameObject");
                go.name = newName;
                Debug.Log($"[PlayModeChangesWindow] Renamed: {sceneName}/{oldPath} → {newName}");
            }
        }

        private void ApplySetActive(string sceneName, string path, bool active)
        {
            var scene = EditorSceneManager.GetSceneByName(sceneName);
            var go = FindGameObjectByPath(scene, path);

            if (go != null)
            {
                Undo.RecordObject(go, "Set Active");
                go.SetActive(active);
                Debug.Log($"[PlayModeChangesWindow] SetActive: {sceneName}/{path} = {active}");
            }
        }

        private void ApplyUpdateTransform(PlayModeChangeLog.TransformData data)
        {
            var scene = EditorSceneManager.GetSceneByName(data.SceneName);
            var go = FindGameObjectByPath(scene, data.Path);

            if (go != null)
            {
                Undo.RecordObject(go.transform, "Update Transform");
                go.transform.localPosition = data.Position;
                go.transform.localRotation = Quaternion.Euler(data.Rotation);
                go.transform.localScale = data.Scale;
                Debug.Log($"[PlayModeChangesWindow] Updated Transform: {data.SceneName}/{data.Path}");
            }
        }

        private void ApplyUpdateGameObject(PlayModeChangeLog.GameObjectData data)
        {
            var scene = EditorSceneManager.GetSceneByName(data.SceneName);
            var go = FindGameObjectByPath(scene, data.Path);

            if (go != null)
            {
                Undo.RecordObject(go, "Update GameObject");

                try
                {
                    // JsonUtilityを使用（Runtime互換のシリアライズデータ）
                    JsonUtility.FromJsonOverwrite(data.SerializedData, go);
                    Debug.Log($"[PlayModeChangesWindow] Updated GameObject: {data.SceneName}/{data.Path}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[PlayModeChangesWindow] Failed to apply serialized data to '{data.Path}': {e.Message}");
                }
            }
        }

        private void ApplyUpdateComponentProperties(PlayModeChangeLog.ComponentPropertiesData data)
        {
            if (data == null) return;

            var scene = EditorSceneManager.GetSceneByName(data.SceneName);
            var go = FindGameObjectByPath(scene, data.Path);

            if (go == null)
            {
                Debug.LogWarning($"[PlayModeChangesWindow] GameObject not found for component update: {data.SceneName}/{data.Path}");
                return;
            }

            var component = data.Signature.Resolve(go);
            if (component == null)
            {
                Debug.LogWarning($"[PlayModeChangesWindow] Target component missing during update: {data.Signature.TypeName} on {data.SceneName}/{data.Path}");
                return;
            }

            var handler = ComponentSyncHandlerRegistry.GetHandler(component);
            if (handler == null)
            {
                Debug.LogWarning($"[PlayModeChangesWindow] No handler for component {component.GetType().Name}");
                return;
            }

            var properties = string.IsNullOrEmpty(data.PropertiesJson)
                ? new Dictionary<string, object>()
                : JsonConvert.DeserializeObject<Dictionary<string, object>>(data.PropertiesJson);

            Undo.RecordObject(component, "Update Component Properties");
            handler.ApplyProperties(component, properties ?? new Dictionary<string, object>());

            Debug.Log($"[PlayModeChangesWindow] Updated Component Properties: {component.GetType().Name} on {data.SceneName}/{data.Path}");
        }

        private void ApplyAddComponent(PlayModeChangeLog.ComponentAddData data)
        {
            if (data == null) return;

            var scene = EditorSceneManager.GetSceneByName(data.SceneName);
            var go = FindGameObjectByPath(scene, data.Path);

            if (go == null)
            {
                Debug.LogWarning($"[PlayModeChangesWindow] GameObject not found for component addition: {data.SceneName}/{data.Path}");
                return;
            }

            var componentType = ComponentSignature.ResolveType(data.Signature.TypeName);
            if (componentType == null)
            {
                Debug.LogError($"[PlayModeChangesWindow] Component type not found: {data.Signature.TypeName}");
                return;
            }

            if (typeof(Transform).IsAssignableFrom(componentType))
            {
                // Transform派生型は追加できない（GameObjectは常に1つだけ持ち、生成時に決まる）
                Debug.LogWarning($"[PlayModeChangesWindow] Skipping add of transform-derived component {componentType.Name} on {data.SceneName}/{data.Path}");
                return;
            }

            // 既に同じ位置に存在する場合は再利用する（[RequireComponent]の自動追加対策）
            Component component;
            var existing = go.GetComponents(componentType);
            if (data.Signature.Index >= 0 && data.Signature.Index < existing.Length)
            {
                component = existing[data.Signature.Index];
                Undo.RecordObject(component, "Update Component");
            }
            else
            {
                Undo.RecordObject(go, "Add Component");
                component = Undo.AddComponent(go, componentType);
            }

            if (component == null)
            {
                Debug.LogError($"[PlayModeChangesWindow] Failed to add component: {componentType.Name}");
                return;
            }

            if (!string.IsNullOrEmpty(data.PropertiesJson))
            {
                var properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(data.PropertiesJson);
                var handler = ComponentSyncHandlerRegistry.GetHandler(component);
                handler?.ApplyProperties(component, properties);
            }

            Debug.Log($"[PlayModeChangesWindow] Added Component: {componentType.Name} on {data.SceneName}/{data.Path}");
        }

        private void ApplyRemoveComponent(PlayModeChangeLog.ComponentRemoveData data)
        {
            if (data == null) return;

            var scene = EditorSceneManager.GetSceneByName(data.SceneName);
            var go = FindGameObjectByPath(scene, data.Path);

            if (go == null)
            {
                Debug.LogWarning($"[PlayModeChangesWindow] GameObject not found for component removal: {data.SceneName}/{data.Path}");
                return;
            }

            var component = data.Signature.Resolve(go);
            if (component == null)
            {
                Debug.LogWarning($"[PlayModeChangesWindow] Target component missing during removal: {data.Signature.TypeName} on {data.SceneName}/{data.Path}");
                return;
            }

            if (component is Transform)
            {
                // Transformは破棄できない
                Debug.LogWarning($"[PlayModeChangesWindow] Skipping removal of transform component on {data.SceneName}/{data.Path}");
                return;
            }

            Undo.DestroyObjectImmediate(component);
            Debug.Log($"[PlayModeChangesWindow] Removed Component: {component.GetType().Name} from {data.SceneName}/{data.Path}");
        }

        private GameObject FindGameObjectByPath(UnityEngine.SceneManagement.Scene scene, string path)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            var parts = path.Split('/');
            Transform current = null;

            foreach (var part in parts)
            {
                if (current == null)
                {
                    // ルートレベルのオブジェクトを検索
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

            return current?.gameObject;
        }
    }
}
