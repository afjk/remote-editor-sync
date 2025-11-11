using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace RemoteEditorSync
{
    /// <summary>
    /// Remote Editor Syncのセットアップユーティリティ
    /// </summary>
    public class RemoteEditorSyncSetup
    {
        [MenuItem("Tools/Remote Editor Sync/Setup Scene")]
        public static void SetupScene()
        {
            // RemoteEditorSyncReceiverが既に存在するか確認
            var existingReceiver = Object.FindObjectOfType<RemoteEditorSyncReceiver>();
            if (existingReceiver != null)
            {
                Debug.LogWarning("[RemoteEditorSyncSetup] RemoteEditorSyncReceiver already exists in the scene.");
                Selection.activeGameObject = existingReceiver.gameObject;
                EditorGUIUtility.PingObject(existingReceiver.gameObject);
                return;
            }

            // NetSyncManagerを探す
            var netSyncManager = Object.FindObjectOfType<Styly.NetSync.NetSyncManager>();
            if (netSyncManager == null)
            {
                EditorUtility.DisplayDialog(
                    "NetSyncManager Not Found",
                    "NetSyncManager is not found in the scene.\n\nPlease add NetSyncManager to your scene first before setting up Remote Editor Sync.",
                    "OK"
                );
                Debug.LogError("[RemoteEditorSyncSetup] NetSyncManager not found in the scene. Please add NetSyncManager first.");
                return;
            }

            // RemoteEditorSyncReceiverを追加
            var receiverObj = new GameObject("RemoteEditorSyncReceiver");
            receiverObj.AddComponent<RemoteEditorSyncReceiver>();

            // NetSyncManagerの近くに配置
            receiverObj.transform.SetParent(netSyncManager.transform.parent);
            receiverObj.transform.SetSiblingIndex(netSyncManager.transform.GetSiblingIndex() + 1);

            // シーンを保存
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            // 選択
            Selection.activeGameObject = receiverObj;
            EditorGUIUtility.PingObject(receiverObj);

            Debug.Log("[RemoteEditorSyncSetup] Setup completed! RemoteEditorSyncReceiver has been added to the scene.");
        }

        [MenuItem("Tools/Remote Editor Sync/Enable Auto Sync")]
        public static void ToggleAutoSync()
        {
            RemoteEditorSync.AutoSyncEnabled = !RemoteEditorSync.AutoSyncEnabled;
        }

        [MenuItem("Tools/Remote Editor Sync/Enable Auto Sync", true)]
        public static bool ToggleAutoSyncValidate()
        {
            Menu.SetChecked("Tools/Remote Editor Sync/Enable Auto Sync", RemoteEditorSync.AutoSyncEnabled);
            return true;
        }

        [MenuItem("Tools/Remote Editor Sync/Show Play Mode Changes")]
        public static void ShowPlayModeChanges()
        {
            if (PlayModeChangeLog.Instance.Changes.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "変更なし",
                    "Play中の変更記録がありません。\n\nPlay Modeで変更を行った後にこのウィンドウを開いてください。",
                    "OK"
                );
                return;
            }

            PlayModeChangesWindow.ShowWindow();
        }

        [MenuItem("Tools/Remote Editor Sync/About")]
        public static void ShowAbout()
        {
            var autoSyncStatus = RemoteEditorSync.AutoSyncEnabled ? "有効" : "無効";
            EditorUtility.DisplayDialog(
                "STYLY Remote Editor Sync v1.2.3",
                "Remote Editor Sync - Unity Editorの変更をクライアントにリアルタイム反映\n\n" +
                $"現在の状態: 自動同期 {autoSyncStatus}\n" +
                "(Tools > Remote Editor Sync > Enable Auto Sync で切り替え)\n\n" +
                "使用方法:\n" +
                "1. Tools > Remote Editor Sync > Setup Scene でセットアップ\n" +
                "2. Play Modeに入る\n" +
                "3. クライアント端末でアプリを起動\n" +
                "4. Hierarchy/Inspectorで変更を行う\n" +
                "5. クライアント側に反映されることを確認\n\n" +
                "Features:\n" +
                "• GameObject作成/削除/名前変更/アクティブ状態\n" +
                "• Transform同期（Position, Rotation, Scale）\n" +
                "• Play Mode変更の保存と適用\n" +
                "• プリミティブタイプの自動検出\n" +
                "• エディタ操作のみ検知（スクリプト生成は除外）\n" +
                "• タグフィルタリング対応\n" +
                "• マルチシーン対応\n\n" +
                "詳細: Packages/com.styly.remote-editor-sync/README.md",
                "OK"
            );
        }

        [MenuItem("Tools/Remote Editor Sync/Open README")]
        public static void OpenReadme()
        {
            var readmePath = "Packages/com.styly.remote-editor-sync/README.md";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(readmePath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                // Open in external editor
                AssetDatabase.OpenAsset(asset);
            }
            else
            {
                Debug.LogWarning($"[RemoteEditorSyncSetup] README not found at {readmePath}");
            }
        }
    }
}
