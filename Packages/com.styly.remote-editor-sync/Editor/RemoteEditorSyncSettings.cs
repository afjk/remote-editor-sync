using UnityEditor;
using UnityEngine;

namespace RemoteEditorSync
{
    /// <summary>
    /// Central configuration exposed via Project Settings so teams can tune aggressiveness.
    /// </summary>
    [FilePath("ProjectSettings/RemoteEditorSyncSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class RemoteEditorSyncSettings : ScriptableSingleton<RemoteEditorSyncSettings>
    {
        [SerializeField] private float _transformFlushInterval = 0.075f;
        [SerializeField] private float _transformPositionEpsilon = 0.0005f;
        [SerializeField] private float _transformRotationEpsilon = 0.1f;
        [SerializeField] private float _transformScaleEpsilon = 0.0005f;
        [SerializeField] private float _materialQuantizationPrecision = 0.001f;
        [SerializeField] private int _materialCompressionThreshold = 512;
        [SerializeField] private bool _enableMetrics = true;
        [SerializeField] private bool _dryRun;

        public static RemoteEditorSyncSettings Instance => instance;

        public float TransformFlushInterval => Mathf.Clamp(_transformFlushInterval, 0.02f, 0.5f);
        public float TransformPositionEpsilon => Mathf.Max(1e-5f, _transformPositionEpsilon);
        public float TransformRotationEpsilon => Mathf.Max(0.01f, _transformRotationEpsilon);
        public float TransformScaleEpsilon => Mathf.Max(1e-5f, _transformScaleEpsilon);
        public float MaterialQuantizationPrecision => Mathf.Clamp(_materialQuantizationPrecision, 0.0001f, 0.05f);
        public int MaterialCompressionThreshold => Mathf.Max(128, _materialCompressionThreshold);
        public bool EnableMetrics => _enableMetrics;
        public bool DryRun => _dryRun;

        public void SaveSettings()
        {
            Save(true);
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/Remote Editor Sync", SettingsScope.Project)
            {
                guiHandler = _ =>
                {
                    var settings = Instance;
                    EditorGUILayout.LabelField("Remote Editor Sync", EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        settings._transformFlushInterval = EditorGUILayout.Slider("Transform Flush Interval (s)", settings._transformFlushInterval, 0.02f, 0.5f);
                        settings._transformPositionEpsilon = EditorGUILayout.FloatField("Transform Position Epsilon", settings._transformPositionEpsilon);
                        settings._transformRotationEpsilon = EditorGUILayout.FloatField("Transform Rotation Epsilon", settings._transformRotationEpsilon);
                        settings._transformScaleEpsilon = EditorGUILayout.FloatField("Transform Scale Epsilon", settings._transformScaleEpsilon);
                        settings._materialQuantizationPrecision = EditorGUILayout.FloatField("Material Quantization Precision", settings._materialQuantizationPrecision);
                        settings._materialCompressionThreshold = EditorGUILayout.IntField("Material Compression Threshold (bytes)", settings._materialCompressionThreshold);
                        settings._enableMetrics = EditorGUILayout.Toggle("Enable Metrics", settings._enableMetrics);
                        settings._dryRun = EditorGUILayout.Toggle("Dry Run (Don't Send RPCs)", settings._dryRun);
                    }

                    if (GUI.changed)
                    {
                        settings.SaveSettings();
                    }
                }
            };
        }
    }
}
