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
        private readonly float _precision;

        public MaterialSnapshot(Material material, float precision)
        {
            _precision = Mathf.Max(0.0001f, precision);
            Properties = ExtractProperties(material, _precision);
        }

        public bool HasChanged(Material material)
        {
            if (material == null)
            {
                return false;
            }

            var current = ExtractProperties(material, _precision);
            return !PropertiesEqual(Properties, current);
        }

        public Dictionary<string, MaterialPropertyValue> CalculateDelta(Material material)
        {
            var current = ExtractProperties(material, _precision);
            return CalculateDelta(Properties, current);
        }

        public static Dictionary<string, MaterialPropertyValue> CalculateDelta(
            Dictionary<string, MaterialPropertyValue> previous,
            Dictionary<string, MaterialPropertyValue> current)
        {
            var delta = new Dictionary<string, MaterialPropertyValue>();
            if (previous == null)
            {
                return delta;
            }

            foreach (var kvp in current)
            {
                if (!previous.TryGetValue(kvp.Key, out var oldValue) || !Equals(oldValue, kvp.Value))
                {
                    delta[kvp.Key] = kvp.Value;
                }
            }

            foreach (var kvp in previous)
            {
                if (!current.ContainsKey(kvp.Key))
                {
                    delta[kvp.Key] = null;
                }
            }

            return delta;
        }

        private static Dictionary<string, MaterialPropertyValue> ExtractProperties(Material material, float precision)
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
                            var color = material.GetColor(propertyName);
                            result[propertyName] = new MaterialPropertyValue
                            {
                                Type = MaterialPropertyType.Color,
                                ColorValue = new SerializableColor
                                {
                                    r = Quantize(color.r, precision),
                                    g = Quantize(color.g, precision),
                                    b = Quantize(color.b, precision),
                                    a = Quantize(color.a, precision)
                                }
                            };
                            break;

                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range:
                            result[propertyName] = new MaterialPropertyValue
                            {
                                Type = MaterialPropertyType.Float,
                                FloatValue = Quantize(material.GetFloat(propertyName), precision)
                            };
                            break;

                        case ShaderUtil.ShaderPropertyType.Vector:
                            result[propertyName] = new MaterialPropertyValue
                            {
                                Type = MaterialPropertyType.Vector,
                                VectorValue = new SerializableVector4(QuantizeVector(material.GetVector(propertyName), precision))
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

        private static float Quantize(float value, float precision)
        {
            if (precision <= 0f)
            {
                return value;
            }

            return Mathf.Round(value / precision) * precision;
        }

        private static Vector4 QuantizeVector(Vector4 value, float precision)
        {
            return new Vector4(
                Quantize(value.x, precision),
                Quantize(value.y, precision),
                Quantize(value.z, precision),
                Quantize(value.w, precision));
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
