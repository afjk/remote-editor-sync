using System;
using System.Collections.Generic;
using UnityEngine;

namespace RemoteEditorSync
{
    /// <summary>
    /// Identifies a component on a GameObject by type and index.
    /// </summary>
    [Serializable]
    public struct ComponentSignature : IEquatable<ComponentSignature>
    {
        public string TypeName;
        public int Index;

        public static ComponentSignature Create(Component component)
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            var type = component.GetType();
            var go = component.gameObject;
            var components = go.GetComponents(type);
            int index = Array.IndexOf(components, component);

            return new ComponentSignature
            {
                TypeName = type.AssemblyQualifiedName,
                Index = index
            };
        }

        public Component Resolve(GameObject go)
        {
            if (go == null || string.IsNullOrEmpty(TypeName))
            {
                return null;
            }

            var type = Type.GetType(TypeName);
            if (type == null)
            {
                return null;
            }

            var components = go.GetComponents(type);
            if (Index < 0 || Index >= components.Length)
            {
                return null;
            }

            return components[Index];
        }

        public bool Equals(ComponentSignature other)
        {
            return string.Equals(TypeName, other.TypeName, StringComparison.Ordinal) && Index == other.Index;
        }

        public override bool Equals(object obj)
        {
            return obj is ComponentSignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((TypeName != null ? TypeName.GetHashCode() : 0) * 397) ^ Index;
            }
        }

        public static bool operator ==(ComponentSignature left, ComponentSignature right) => left.Equals(right);
        public static bool operator !=(ComponentSignature left, ComponentSignature right) => !left.Equals(right);

        public override string ToString()
        {
            return string.IsNullOrEmpty(TypeName) ? "<null>" : $"{TypeName}[{Index}]";
        }
    }

    /// <summary>
    /// Snapshot of component properties captured for change detection.
    /// </summary>
    [Serializable]
    public class ComponentSnapshot
    {
        public ComponentSignature Signature;
        public Dictionary<string, object> PropertyValues;

        public ComponentSnapshot(Component component, IComponentSyncHandler handler)
        {
            Signature = ComponentSignature.Create(component);
            PropertyValues = handler?.ExtractProperties(component) ?? new Dictionary<string, object>();
        }

        public bool HasChanged(Component component, IComponentSyncHandler handler)
        {
            if (component == null || handler == null)
            {
                return false;
            }

            var currentValues = handler.ExtractProperties(component);
            return !PropertyValuesEqual(PropertyValues, currentValues);
        }

        public Dictionary<string, object> ExtractDelta(Dictionary<string, object> currentValues)
        {
            var delta = new Dictionary<string, object>();
            if (currentValues == null)
            {
                return delta;
            }

            foreach (var kvp in currentValues)
            {
                if (!PropertyValues.TryGetValue(kvp.Key, out var previousValue) || !ValuesEqual(previousValue, kvp.Value))
                {
                    delta[kvp.Key] = kvp.Value;
                }
            }

            foreach (var kvp in PropertyValues)
            {
                if (!currentValues.ContainsKey(kvp.Key))
                {
                    delta[kvp.Key] = null;
                }
            }

            return delta;
        }

        private static bool PropertyValuesEqual(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var otherValue))
                {
                    return false;
                }

                if (!ValuesEqual(kvp.Value, otherValue))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            if (a is Vector3 v3a && b is Vector3 v3b)
            {
                return Vector3.Distance(v3a, v3b) < 0.0001f;
            }

            if (a is Vector2 v2a && b is Vector2 v2b)
            {
                return Vector2.Distance(v2a, v2b) < 0.0001f;
            }

            if (a is Vector4 v4a && b is Vector4 v4b)
            {
                return Vector4.Distance(v4a, v4b) < 0.0001f;
            }

            if (a is Quaternion qa && b is Quaternion qb)
            {
                return Quaternion.Angle(qa, qb) < 0.01f;
            }

            if (a is Color ca && b is Color cb)
            {
                return ca.Equals(cb);
            }

            return a.Equals(b);
        }
    }

    /// <summary>
    /// Utility for filtering which property values are supported for sync.
    /// </summary>
    public static class TypeFilter
    {
        public static bool IsSupportedValueType(Type type)
        {
            if (type == null) return false;

            if (type.IsPrimitive || type == typeof(string)) return true;
            if (type.IsEnum) return true;
            if (IsUnityPrimitiveType(type)) return true;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return IsSupportedValueType(Nullable.GetUnderlyingType(type));
            }

            return false;
        }

        public static bool IsUnityPrimitiveType(Type type)
        {
            if (type == typeof(Vector2) || type == typeof(Vector2Int)) return true;
            if (type == typeof(Vector3) || type == typeof(Vector3Int)) return true;
            if (type == typeof(Vector4)) return true;
            if (type == typeof(Quaternion)) return true;
            if (type == typeof(Color) || type == typeof(Color32)) return true;
            if (type == typeof(Rect) || type == typeof(RectInt)) return true;
            if (type == typeof(Bounds) || type == typeof(BoundsInt)) return true;
            if (type == typeof(Matrix4x4)) return true;
            if (type == typeof(LayerMask)) return true;
            return false;
        }

        public static bool IsUnityObjectReference(Type type)
        {
            return typeof(UnityEngine.Object).IsAssignableFrom(type);
        }

        public static bool IsCollectionType(Type type)
        {
            if (type == null) return false;
            if (type.IsArray) return true;
            if (type.IsGenericType)
            {
                var genericType = type.GetGenericTypeDefinition();
                if (genericType == typeof(List<>)) return true;
            }

            return false;
        }
    }
}
