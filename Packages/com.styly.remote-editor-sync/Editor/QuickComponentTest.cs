using System.Collections.Generic;
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

            float newValue = scrollbar.value > 0.5f ? 0.1f : 0.9f;
            Debug.Log($"[QuickComponentTest] Generating test data with value: {newValue}");

            var handler = ComponentSyncHandlerRegistry.GetHandler(scrollbar);
            if (handler == null)
            {
                Debug.LogError("[QuickComponentTest] No handler found for Scrollbar");
                return;
            }

            var properties = handler.ExtractProperties(scrollbar) ?? new Dictionary<string, object>();
            properties["value"] = newValue;

            string propertiesJson = JsonConvert.SerializeObject(properties, Formatting.None);
            Debug.Log($"[QuickComponentTest] Properties JSON:\n{propertiesJson}");

            var componentData = new ComponentPropertiesPayload
            {
                SceneName = "SampleScene",
                Path = "Canvas/Scrollbar",
                Signature = ComponentSignature.Create(scrollbar),
                PropertiesJson = propertiesJson
            };

            var jsonSettings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            string rpcData = JsonConvert.SerializeObject(componentData, jsonSettings);

            // Call HandleUpdateComponentProperties via Reflection
            var method = typeof(RemoteEditorSyncReceiver).GetMethod(
                "HandleUpdateComponentProperties",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (method == null)
            {
                Debug.LogError("[QuickComponentTest] HandleUpdateComponent method not found!");
                return;
            }

            Debug.Log("[QuickComponentTest] Calling HandleUpdateComponentProperties...");
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
        private class ComponentPropertiesPayload
        {
            public string SceneName;
            public string Path;
            public ComponentSignature Signature;
            public string PropertiesJson;
        }
    }
}
