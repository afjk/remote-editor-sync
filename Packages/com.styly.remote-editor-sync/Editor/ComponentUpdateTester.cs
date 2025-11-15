using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RemoteEditorSync
{
    /// <summary>
    /// Editor-side utility to simulate component update RPCs while staying inside the current scene.
    /// </summary>
    public class ComponentUpdateTester : EditorWindow
    {
        private GameObject _targetGameObject;
        private Component _targetComponent;
        private string _testJsonData = string.Empty;

        [MenuItem("Tools/Remote Editor Sync/Test Component Update")]
        public static void ShowWindow()
        {
            GetWindow<ComponentUpdateTester>("Component Update Tester");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Component Update Tester", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Play Mode only: sends property JSON to RemoteEditorSyncReceiver.HandleUpdateComponentProperties.",
                MessageType.Info);

            EditorGUILayout.Space();

            var newTarget = EditorGUILayout.ObjectField("Target GameObject", _targetGameObject, typeof(GameObject), true) as GameObject;
            if (newTarget != _targetGameObject)
            {
                _targetGameObject = newTarget;
                _targetComponent = null;
                _testJsonData = string.Empty;
            }

            if (_targetGameObject != null)
            {
                DrawComponentSelector();
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_targetComponent == null))
            {
                if (GUILayout.Button("Generate Test Data") && _targetComponent != null)
                {
                    GenerateTestData();
                }
            }

            EditorGUILayout.LabelField("Test JSON Data:");
            _testJsonData = EditorGUILayout.TextArea(_testJsonData, GUILayout.Height(120));

            EditorGUILayout.Space();

            bool canApply = Application.isPlaying && _targetComponent != null && !string.IsNullOrEmpty(_testJsonData);
            using (new EditorGUI.DisabledScope(!canApply))
            {
                if (GUILayout.Button("Apply Test Data (Play Mode Only)"))
                {
                    ApplyTestData();
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to send test data.", MessageType.Warning);
            }
        }

        private void DrawComponentSelector()
        {
            var components = _targetGameObject.GetComponents<Component>();
            if (components.Length == 0)
            {
                EditorGUILayout.HelpBox("No components found on the selected GameObject.", MessageType.Warning);
                return;
            }

            var names = new string[components.Length];
            int currentIndex = -1;

            for (int i = 0; i < components.Length; i++)
            {
                var comp = components[i];
                names[i] = comp != null ? comp.GetType().Name : "Missing Component";
                if (comp == _targetComponent)
                {
                    currentIndex = i;
                }
            }

            int newIndex = EditorGUILayout.Popup("Component", currentIndex, names);
            if (newIndex >= 0 && newIndex < components.Length)
            {
                _targetComponent = components[newIndex];
            }
        }

        private void GenerateTestData()
        {
            if (_targetComponent == null) return;

            var handler = ComponentSyncHandlerRegistry.GetHandler(_targetComponent);
            if (handler == null)
            {
                EditorUtility.DisplayDialog("Remote Editor Sync", $"Handler not found for {_targetComponent.GetType().Name}.", "OK");
                return;
            }

            var properties = handler.ExtractProperties(_targetComponent) ?? new Dictionary<string, object>();

            switch (_targetComponent)
            {
                case Slider slider:
                    properties["value"] = Mathf.Clamp01(slider.value + 0.25f);
                    break;
                case Toggle toggle:
                    properties["isOn"] = !toggle.isOn;
                    break;
                case InputField input:
                    properties["text"] = string.IsNullOrEmpty(input.text) ? "Test Input" : input.text + " (Test)";
                    properties["contentType"] = input.contentType;
                    break;
                case Text text:
                    properties["text"] = string.IsNullOrEmpty(text.text) ? "Test Text" : text.text + " (Test)";
                    properties["fontSize"] = text.fontSize;
                    break;
            }

            _testJsonData = JsonConvert.SerializeObject(properties, Formatting.Indented);
            Debug.Log($"[ComponentUpdateTester] Generated test data for {_targetComponent.GetType().Name}:\n{_testJsonData}");
        }

        private void ApplyTestData()
        {
            if (_targetGameObject == null || _targetComponent == null) return;

            var receiver = FindObjectOfType<RemoteEditorSyncReceiver>();
            if (receiver == null)
            {
                EditorUtility.DisplayDialog("Remote Editor Sync", "RemoteEditorSyncReceiver がシーンに存在しません。", "OK");
                return;
            }

            Dictionary<string, object> properties = null;
            try
            {
                properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(_testJsonData);
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Remote Editor Sync", $"JSONの解析に失敗しました: {e.Message}", "OK");
                return;
            }

            if (properties == null)
            {
                EditorUtility.DisplayDialog("Remote Editor Sync", "JSONデータが空です。", "OK");
                return;
            }

            var payloadData = new ComponentPropertiesPayload
            {
                SceneName = _targetGameObject.scene.name,
                Path = GetGameObjectPath(_targetGameObject),
                Signature = ComponentSignature.Create(_targetComponent),
                PropertiesJson = JsonConvert.SerializeObject(properties, Formatting.None)
            };

            var jsonSettings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            string payload = JsonConvert.SerializeObject(payloadData, jsonSettings);

            MethodInfo method = typeof(RemoteEditorSyncReceiver).GetMethod(
                "HandleUpdateComponentProperties",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (method == null)
            {
                EditorUtility.DisplayDialog("Remote Editor Sync", "HandleUpdateComponent メソッドが見つかりません。", "OK");
                return;
            }

            Debug.Log($"[ComponentUpdateTester] Applying property data to {_targetComponent.GetType().Name}");
            method.Invoke(receiver, new object[] { new[] { payload } });
            Debug.Log("[ComponentUpdateTester] Test data applied successfully");
        }

        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return string.Empty;

            var path = go.name;
            var parent = go.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
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
