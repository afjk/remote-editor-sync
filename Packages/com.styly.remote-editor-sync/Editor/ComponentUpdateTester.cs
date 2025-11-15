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
                "Play Mode only: sends serialized component data straight to RemoteEditorSyncReceiver.HandleUpdateComponent.",
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

            var type = _targetComponent.GetType();

            if (type == typeof(Slider))
            {
                var slider = (Slider)_targetComponent;
                _testJsonData = JsonUtility.ToJson(new SliderTestData
                {
                    m_Value = Mathf.Clamp01(slider.value + 0.25f),
                    m_MinValue = slider.minValue,
                    m_MaxValue = slider.maxValue
                }, true);
            }
            else if (type == typeof(Toggle))
            {
                var toggle = (Toggle)_targetComponent;
                _testJsonData = JsonUtility.ToJson(new ToggleTestData
                {
                    m_IsOn = !toggle.isOn
                }, true);
            }
            else if (type == typeof(InputField))
            {
                var input = (InputField)_targetComponent;
                _testJsonData = JsonUtility.ToJson(new InputFieldTestData
                {
                    m_Text = string.IsNullOrEmpty(input.text) ? "Test Input" : input.text + " (Test)",
                    m_ContentType = (int)input.contentType
                }, true);
            }
            else if (type == typeof(Text))
            {
                var text = (Text)_targetComponent;
                _testJsonData = JsonUtility.ToJson(new TextTestData
                {
                    m_Text = string.IsNullOrEmpty(text.text) ? "Test Text" : text.text + " (Test)",
                    m_FontSize = text.fontSize
                }, true);
            }
            else
            {
                _testJsonData = JsonUtility.ToJson(_targetComponent, true);
            }

            Debug.Log($"[ComponentUpdateTester] Generated test data for {type.Name}:\n{_testJsonData}");
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

            var data = new ComponentUpdateData
            {
                SceneName = _targetGameObject.scene.name,
                Path = GetGameObjectPath(_targetGameObject),
                ComponentType = _targetComponent.GetType().AssemblyQualifiedName,
                SerializedData = _testJsonData
            };

            var jsonSettings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            string payload = JsonConvert.SerializeObject(data, jsonSettings);

            MethodInfo method = typeof(RemoteEditorSyncReceiver).GetMethod(
                "HandleUpdateComponent",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (method == null)
            {
                EditorUtility.DisplayDialog("Remote Editor Sync", "HandleUpdateComponent メソッドが見つかりません。", "OK");
                return;
            }

            Debug.Log($"[ComponentUpdateTester] Applying test data to {_targetComponent.GetType().Name}");
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
        private class ComponentUpdateData
        {
            public string SceneName;
            public string Path;
            public string ComponentType;
            public string SerializedData;
        }

        [System.Serializable]
        private class SliderTestData
        {
            public float m_Value;
            public float m_MinValue;
            public float m_MaxValue;
        }

        [System.Serializable]
        private class ToggleTestData
        {
            public bool m_IsOn;
        }

        [System.Serializable]
        private class InputFieldTestData
        {
            public string m_Text;
            public int m_ContentType;
        }

        [System.Serializable]
        private class TextTestData
        {
            public string m_Text;
            public int m_FontSize;
        }
    }
}
