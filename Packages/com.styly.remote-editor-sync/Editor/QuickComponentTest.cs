using UnityEngine;
using UnityEditor;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine.SceneManagement;

namespace RemoteEditorSync
{
    /// <summary>
    /// Quick test script for component update functionality
    /// </summary>
    public static class QuickComponentTest
    {
        [MenuItem("Tools/Remote Editor Sync/Run Quick Test (Scrollbar)")]
        public static void RunScrollbarTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[QuickComponentTest] This test must be run in Play Mode!");
                return;
            }

            // Find RemoteEditorSyncReceiver
            var receiver = Object.FindObjectOfType<RemoteEditorSyncReceiver>();
            if (receiver == null)
            {
                Debug.LogError("[QuickComponentTest] RemoteEditorSyncReceiver not found in scene!");
                return;
            }

            // Find Scrollbar GameObject
            var scene = SceneManager.GetSceneByName("SampleScene");
            if (!scene.IsValid())
            {
                Debug.LogError("[QuickComponentTest] SampleScene not found!");
                return;
            }

            GameObject scrollbarGO = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Canvas")
                {
                    var scrollbarTransform = root.transform.Find("Scrollbar");
                    if (scrollbarTransform != null)
                    {
                        scrollbarGO = scrollbarTransform.gameObject;
                        break;
                    }
                }
            }

            if (scrollbarGO == null)
            {
                Debug.LogError("[QuickComponentTest] Canvas/Scrollbar not found!");
                return;
            }

            // Get Scrollbar component
            var scrollbar = scrollbarGO.GetComponent<UnityEngine.UI.Scrollbar>();
            if (scrollbar == null)
            {
                Debug.LogError("[QuickComponentTest] Scrollbar component not found!");
                return;
            }

            Debug.Log($"[QuickComponentTest] Found Scrollbar. Current value: {scrollbar.value}");

            // Create test data with different value
            float newValue = scrollbar.value > 0.5f ? 0.1f : 0.9f;
            Debug.Log($"[QuickComponentTest] Generating test data with value: {newValue}");

            var testData = new ScrollbarTestData
            {
                m_Value = newValue,
                m_Size = scrollbar.size,
                m_NumberOfSteps = scrollbar.numberOfSteps
            };

            string serializedData = JsonUtility.ToJson(testData, true);
            Debug.Log($"[QuickComponentTest] Serialized data:\n{serializedData}");

            // Create ComponentData
            var componentData = new ComponentUpdateData
            {
                SceneName = "SampleScene",
                Path = "Canvas/Scrollbar",
                ComponentType = typeof(UnityEngine.UI.Scrollbar).AssemblyQualifiedName,
                SerializedData = serializedData
            };

            var jsonSettings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            string rpcData = JsonConvert.SerializeObject(componentData, jsonSettings);

            // Call HandleUpdateComponent via Reflection
            var method = typeof(RemoteEditorSyncReceiver).GetMethod(
                "HandleUpdateComponent",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (method == null)
            {
                Debug.LogError("[QuickComponentTest] HandleUpdateComponent method not found!");
                return;
            }

            Debug.Log("[QuickComponentTest] Calling HandleUpdateComponent...");
            method.Invoke(receiver, new object[] { new[] { rpcData } });

            // Wait a frame and check the value
            EditorApplication.delayCall += () =>
            {
                Debug.Log($"[QuickComponentTest] After update - Scrollbar value: {scrollbar.value}");

                if (Mathf.Abs(scrollbar.value - newValue) < 0.001f)
                {
                    Debug.Log("<color=green>[QuickComponentTest] ✓ TEST PASSED! Value updated successfully.</color>");
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[QuickComponentTest] ⚠ Value might not have updated. Expected: {newValue}, Got: {scrollbar.value}</color>");
                }

                Debug.Log("[QuickComponentTest] Test complete. Check the Game view to see if the Scrollbar handle moved visually.");
            };
        }

        [System.Serializable]
        private class ComponentUpdateData
        {
            public string SceneName;
            public string Path;
            public string ComponentType;
            public string SerializedData;
        }

        [System.Serializable]
        private class ScrollbarTestData
        {
            public float m_Value;
            public float m_Size;
            public int m_NumberOfSteps;
        }
    }
}
