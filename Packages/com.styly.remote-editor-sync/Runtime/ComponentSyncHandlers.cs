using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RemoteEditorSync
{
    public interface IComponentSyncHandler
    {
        bool CanHandle(Type componentType);
        Dictionary<string, object> ExtractProperties(Component component);
        void ApplyProperties(Component component, Dictionary<string, object> properties);
    }

    /// <summary>
    /// Default reflection-based handler that extracts supported writable public
    /// properties and serialized fields (public or [SerializeField]).
    /// </summary>
    public class ReflectionComponentHandler : IComponentSyncHandler
    {
        private static readonly Dictionary<Type, FieldInfo[]> _serializedFieldCache = new Dictionary<Type, FieldInfo[]>();

        public virtual bool CanHandle(Type componentType)
        {
            return componentType != null && typeof(Component).IsAssignableFrom(componentType);
        }

        public virtual Dictionary<string, object> ExtractProperties(Component component)
        {
            var result = new Dictionary<string, object>();
            if (component == null)
            {
                return result;
            }

            var type = component.GetType();
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);

            foreach (var prop in properties)
            {
                if (!prop.CanRead || !prop.CanWrite || prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (ShouldExcludeProperty(prop.Name))
                {
                    continue;
                }

                var propType = prop.PropertyType;
                if (!TypeFilter.IsSupportedValueType(propType))
                {
                    continue;
                }

                try
                {
                    var value = prop.GetValue(component);
                    value = PrepareValueForSerialization(value);
                    result[prop.Name] = value;
                }
                catch
                {
                    // Ignore individual property failures to keep sync resilient.
                }
            }

            foreach (var field in GetSerializedFields(type))
            {
                // GetSerializedFields walks derived-to-base, and a base class may declare a
                // field with the same name as a derived one. Keep the first (most derived)
                // hit so extraction matches FindSerializedField, which resolves the same way.
                if (result.ContainsKey(field.Name))
                {
                    continue;
                }

                try
                {
                    var value = field.GetValue(component);
                    value = PrepareValueForSerialization(value);
                    result[field.Name] = value;
                }
                catch
                {
                    // Ignore individual field failures to keep sync resilient.
                }
            }

            return result;
        }

        public virtual void ApplyProperties(Component component, Dictionary<string, object> properties)
        {
            if (component == null || properties == null || properties.Count == 0)
            {
                return;
            }

            var type = component.GetType();

            foreach (var kvp in properties)
            {
                try
                {
                    var prop = type.GetProperty(kvp.Key, BindingFlags.Instance | BindingFlags.Public);
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(component, ConvertValue(kvp.Value, prop.PropertyType));
                        continue;
                    }

                    var field = FindSerializedField(type, kvp.Key);
                    if (field != null)
                    {
                        field.SetValue(component, ConvertValue(kvp.Value, field.FieldType));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ComponentHandler] Failed to set property {kvp.Key} on {component.GetType().Name}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Collects fields Unity serializes (public non-[NonSerialized] fields and
        /// [SerializeField] private fields), walking the inheritance chain up to the
        /// UnityEngine base classes whose state is native-backed.
        /// </summary>
        protected static FieldInfo[] GetSerializedFields(Type componentType)
        {
            lock (_serializedFieldCache)
            {
                if (_serializedFieldCache.TryGetValue(componentType, out var cached))
                {
                    return cached;
                }
            }

            var fields = new List<FieldInfo>();
            for (var type = componentType; type != null && !IsUnityBaseType(type); type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsLiteral)
                    {
                        continue;
                    }

                    if (field.IsPublic)
                    {
                        if (field.IsDefined(typeof(NonSerializedAttribute), false))
                        {
                            continue;
                        }
                    }
                    else if (!field.IsDefined(typeof(SerializeField), false))
                    {
                        continue;
                    }

                    if (!TypeFilter.IsSupportedValueType(field.FieldType))
                    {
                        continue;
                    }

                    fields.Add(field);
                }
            }

            var result = fields.ToArray();
            lock (_serializedFieldCache)
            {
                _serializedFieldCache[componentType] = result;
            }

            return result;
        }

        protected static FieldInfo FindSerializedField(Type componentType, string fieldName)
        {
            foreach (var field in GetSerializedFields(componentType))
            {
                if (string.Equals(field.Name, fieldName, StringComparison.Ordinal))
                {
                    return field;
                }
            }

            return null;
        }

        private static bool IsUnityBaseType(Type type)
        {
            return type == typeof(MonoBehaviour) ||
                   type == typeof(Behaviour) ||
                   type == typeof(Component) ||
                   type == typeof(UnityEngine.Object);
        }

        protected virtual bool ShouldExcludeProperty(string propertyName)
        {
            switch (propertyName)
            {
                case "gameObject":
                case "transform":
                case "rigidbody":
                case "camera":
                case "light":
                case "animation":
                case "constantForce":
                case "renderer":
                case "audio":
                case "networkView":
                case "guiTexture":
                case "collider":
                case "hingeJoint":
                case "particleEmitter":
                case "name":
                case "tag":
                case "hideFlags":
                    return true;
                default:
                    return false;
            }
        }

        protected virtual object PrepareValueForSerialization(object value)
        {
            switch (value)
            {
                case Vector2 v2:
                    return new SerializableVector2(v2);
                case Vector3 v3:
                    return new SerializableVector3(v3);
                case Vector4 v4:
                    return new SerializableVector4(v4);
                case Vector2Int v2Int:
                    return new SerializableVector2Int(v2Int);
                case Vector3Int v3Int:
                    return new SerializableVector3Int(v3Int);
                case Quaternion quaternion:
                    return new SerializableQuaternion(quaternion);
                case Color color:
                    return new SerializableColor(color);
                case Color32 color32:
                    return new SerializableColor32(color32);
                case Rect rect:
                    return new SerializableRect(rect);
                case RectInt rectInt:
                    return new SerializableRectInt(rectInt);
                case Bounds bounds:
                    return new SerializableBounds(bounds);
                case BoundsInt boundsInt:
                    return new SerializableBoundsInt(boundsInt);
                case Matrix4x4 matrix:
                    return new SerializableMatrix4x4(matrix);
                case LayerMask layerMask:
                    return layerMask.value;
                default:
                    return value;
            }
        }

        protected object ConvertValue(object value, Type targetType)
        {
            if (targetType == null)
            {
                return value;
            }

            if (value == null)
            {
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }

            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            if (targetType == typeof(LayerMask))
            {
                // LayerMask is sent as its int value; rebuild the struct explicitly
                // because neither Json.NET nor Convert.ChangeType can produce one.
                var maskValue = value is JValue jMaskValue ? jMaskValue.ToObject<int>() : Convert.ToInt32(value);
                return (LayerMask)maskValue;
            }

            if (value is JObject jObject)
            {
                return jObject.ToObject(targetType);
            }

            if (value is JArray jArray)
            {
                return jArray.ToObject(targetType);
            }

            if (value is JValue jValue)
            {
                return jValue.ToObject(targetType);
            }

            if (targetType.IsEnum)
            {
                if (value is string strValue)
                {
                    return Enum.Parse(targetType, strValue);
                }

                return Enum.ToObject(targetType, value);
            }

            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                return value;
            }
        }
    }

    public class BehaviourHandler : ReflectionComponentHandler
    {
        public override bool CanHandle(Type componentType)
        {
            return componentType != null && typeof(Behaviour).IsAssignableFrom(componentType);
        }

        public override Dictionary<string, object> ExtractProperties(Component component)
        {
            var properties = base.ExtractProperties(component);

            if (component is Behaviour behaviour)
            {
                properties["enabled"] = behaviour.enabled;
            }

            return properties;
        }

        public override void ApplyProperties(Component component, Dictionary<string, object> properties)
        {
            if (component is Behaviour behaviour && properties != null && properties.ContainsKey("enabled"))
            {
                behaviour.enabled = (bool)ConvertValue(properties["enabled"], typeof(bool));
            }

            base.ApplyProperties(component, properties);
        }
    }

    public class RendererHandler : ReflectionComponentHandler
    {
        public override bool CanHandle(Type componentType)
        {
            return componentType != null && typeof(Renderer).IsAssignableFrom(componentType);
        }

        public override Dictionary<string, object> ExtractProperties(Component component)
        {
            var properties = base.ExtractProperties(component);

            if (component is Renderer renderer)
            {
                properties["enabled"] = renderer.enabled;
            }

            return properties;
        }

        public override void ApplyProperties(Component component, Dictionary<string, object> properties)
        {
            if (component is Renderer renderer && properties != null && properties.ContainsKey("enabled"))
            {
                renderer.enabled = (bool)ConvertValue(properties["enabled"], typeof(bool));
            }

            base.ApplyProperties(component, properties);
        }
    }

    public class ColliderHandler : ReflectionComponentHandler
    {
        public override bool CanHandle(Type componentType)
        {
            return componentType != null && typeof(Collider).IsAssignableFrom(componentType);
        }

        public override Dictionary<string, object> ExtractProperties(Component component)
        {
            var properties = base.ExtractProperties(component);

            if (component is Collider collider)
            {
                properties["enabled"] = collider.enabled;
            }

            return properties;
        }

        public override void ApplyProperties(Component component, Dictionary<string, object> properties)
        {
            if (component is Collider collider && properties != null && properties.ContainsKey("enabled"))
            {
                collider.enabled = (bool)ConvertValue(properties["enabled"], typeof(bool));
            }

            base.ApplyProperties(component, properties);
        }
    }

    public class TransformHandler : IComponentSyncHandler
    {
        private static readonly Dictionary<string, object> _empty = new Dictionary<string, object>();

        public bool CanHandle(Type componentType)
        {
            return componentType == typeof(Transform);
        }

        public Dictionary<string, object> ExtractProperties(Component component)
        {
            return _empty;
        }

        public void ApplyProperties(Component component, Dictionary<string, object> properties)
        {
            // Intentionally left blank. Transforms are handled elsewhere via dedicated RPCs.
        }
    }

    public static class ComponentSyncHandlerRegistry
    {
        private static readonly List<IComponentSyncHandler> _handlers = new List<IComponentSyncHandler>
        {
            new TransformHandler(),
            new BehaviourHandler(),
            new RendererHandler(),
            new ColliderHandler(),
            new ReflectionComponentHandler()
        };

        /// <summary>
        /// このパッケージ自身の基盤コンポーネント。専用のRPC経路を持ち、
        /// 各クライアントがローカルに自前の状態（アンカーGUID等）を管理するため、
        /// 汎用のコンポーネント同期に載せてはいけない。
        /// </summary>
        private static readonly HashSet<Type> _excludedTypes = new HashSet<Type>
        {
            typeof(MaterialAnchor),
            typeof(MaterialAnchorRegistry),
            typeof(MaterialAnchorRuntimeBootstrap),
            typeof(RemoteEditorSyncReceiver)
        };

        public static void RegisterHandler(IComponentSyncHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            _handlers.Insert(0, handler);
        }

        public static IComponentSyncHandler GetHandler(Component component)
        {
            return component == null ? null : GetHandler(component.GetType());
        }

        public static IComponentSyncHandler GetHandler(Type componentType)
        {
            if (componentType == null || _excludedTypes.Contains(componentType))
            {
                return null;
            }

            foreach (var handler in _handlers)
            {
                if (handler.CanHandle(componentType))
                {
                    return handler;
                }
            }

            return null;
        }
    }
}
