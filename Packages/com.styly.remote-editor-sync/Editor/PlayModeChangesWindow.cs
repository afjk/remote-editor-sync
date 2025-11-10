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
                case PlayModeChangeLog.ChangeType.SetActive:
                    return "👁";
                case PlayModeChangeLog.ChangeType.UpdateTransform:
                    return "📐";
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
                Debug.LogWarning($"[PlayModeChangesWindow] Scene not found or not loaded: {change.SceneName}");
                return;
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

                case PlayModeChangeLog.ChangeType.SetActive:
                    ApplySetActive(change.SceneName, change.Path, change.NewActive);
                    break;

                case PlayModeChangeLog.ChangeType.UpdateTransform:
                    ApplyUpdateTransform(change.TransformData);
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
            if (go == null)
            {
                go = new GameObject(data.Name);
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

            Undo.RegisterCreatedObjectUndo(go, "Create GameObject");

            Debug.Log($"[PlayModeChangesWindow] Created: {data.SceneName}/{data.Path}");
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
