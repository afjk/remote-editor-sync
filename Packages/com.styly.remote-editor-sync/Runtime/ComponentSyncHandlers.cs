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
    /// Default reflection-based handler that extracts supported public properties.
    /// </summary>
    public class ReflectionComponentHandler : IComponentSyncHandler
    {
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
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
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
                    result[prop.Name] = value;
                }
                catch
                {
                    // Ignore individual property failures to keep sync resilient.
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
                    if (prop == null || !prop.CanWrite)
                    {
                        continue;
                    }

                    var convertedValue = ConvertValue(kvp.Value, prop.PropertyType);
                    prop.SetValue(component, convertedValue);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ComponentHandler] Failed to set property {kvp.Key} on {component.GetType().Name}: {e.Message}");
                }
            }
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
                case "hideFlags":
                    return true;
                default:
                    return false;
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
            if (componentType == null)
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
