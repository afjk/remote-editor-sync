using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RemoteEditorSync
{
    /// <summary>
    /// Captures the editable shader properties of a material so we can diff inspector changes.
    /// </summary>
    public class MaterialSnapshot
    {
        public Dictionary<string, MaterialPropertyValue> Properties { get; }

        public MaterialSnapshot(Material material)
        {
            Properties = ExtractProperties(material);
        }

        public bool HasChanged(Material material)
        {
            if (material == null)
            {
                return false;
            }

            var current = ExtractProperties(material);
            return !PropertiesEqual(Properties, current);
        }

        private static Dictionary<string, MaterialPropertyValue> ExtractProperties(Material material)
        {
            var result = new Dictionary<string, MaterialPropertyValue>();

            if (material == null || material.shader == null)
            {
                return result;
            }

            int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            for (int i = 0; i < propertyCount; i++)
            {
                string propertyName = ShaderUtil.GetPropertyName(material.shader, i);
                var propertyType = ShaderUtil.GetPropertyType(material.shader, i);

                if (!material.HasProperty(propertyName))
                {
                    continue;
                }

                try
                {
                    switch (propertyType)
                    {
                        case ShaderUtil.ShaderPropertyType.Color:
                            result[propertyName] = new MaterialPropertyValue
                            {
                                Type = MaterialPropertyType.Color,
                                ColorValue = new SerializableColor(material.GetColor(propertyName))
                            };
                            break;

                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range:
                            result[propertyName] = new MaterialPropertyValue
                            {
                                Type = MaterialPropertyType.Float,
                                FloatValue = material.GetFloat(propertyName)
                            };
                            break;

                        case ShaderUtil.ShaderPropertyType.Vector:
                            result[propertyName] = new MaterialPropertyValue
                            {
                                Type = MaterialPropertyType.Vector,
                                VectorValue = new SerializableVector4(material.GetVector(propertyName))
                            };
                            break;
                    }
                }
                catch
                {
                    // Ignore extraction failures for individual properties to keep the sync resilient.
                }
            }

            return result;
        }

        private static bool PropertiesEqual(
            Dictionary<string, MaterialPropertyValue> a,
            Dictionary<string, MaterialPropertyValue> b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null || a.Count != b.Count)
            {
                return false;
            }

            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var otherValue))
                {
                    return false;
                }

                if (!kvp.Value.Equals(otherValue))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
