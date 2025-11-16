using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RemoteEditorSync
{
    [Serializable]
    public struct MaterialSignature : IEquatable<MaterialSignature>
    {
        public string AssetGuid;
        public string AssetPath;
        public string AnchorId;
        public int MaterialIndex;
        public string ShaderName;
        public string RuntimeMaterialId;

        public static MaterialSignature Create(Material material, Renderer renderer, int materialIndex)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            var anchor = MaterialAnchor.GetOrCreateForRenderer(renderer);
            string anchorId = anchor.AnchorId.ToString();
            string runtimeMaterialId;
            string assetGuid = string.Empty;
            string assetPath = string.Empty;

#if UNITY_EDITOR
            assetPath = AssetDatabase.GetAssetPath(material);
            if (!string.IsNullOrEmpty(assetPath))
            {
                assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            }

            runtimeMaterialId = !string.IsNullOrEmpty(assetGuid)
                ? assetGuid
                : $"{anchorId}:{materialIndex}";
#else
            runtimeMaterialId = $"{anchorId}:{materialIndex}";
#endif

            return new MaterialSignature
            {
                AssetGuid = assetGuid,
                AssetPath = assetPath,
                AnchorId = anchorId,
                MaterialIndex = materialIndex,
                ShaderName = material.shader != null ? material.shader.name : string.Empty,
                RuntimeMaterialId = runtimeMaterialId
            };
        }

        public bool Equals(MaterialSignature other)
        {
            return string.Equals(RuntimeMaterialId, other.RuntimeMaterialId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialSignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            return RuntimeMaterialId?.GetHashCode() ?? 0;
        }

        public override string ToString()
        {
            return $"MaterialSignature({RuntimeMaterialId})";
        }

        public static bool operator ==(MaterialSignature left, MaterialSignature right) => left.Equals(right);
        public static bool operator !=(MaterialSignature left, MaterialSignature right) => !left.Equals(right);
    }

    [Serializable]
    public class MaterialPropertyValue : IEquatable<MaterialPropertyValue>
    {
        public MaterialPropertyType Type;
        public SerializableColor ColorValue;
        public float FloatValue;
        public SerializableVector4 VectorValue;

        public bool Equals(MaterialPropertyValue other)
        {
            if (other == null || other.Type != Type)
            {
                return false;
            }

            switch (Type)
            {
                case MaterialPropertyType.Color:
                    return ColorValue.r.Equals(other.ColorValue.r) &&
                           ColorValue.g.Equals(other.ColorValue.g) &&
                           ColorValue.b.Equals(other.ColorValue.b) &&
                           ColorValue.a.Equals(other.ColorValue.a);
                case MaterialPropertyType.Float:
                    return Mathf.Approximately(FloatValue, other.FloatValue);
                case MaterialPropertyType.Vector:
                    return VectorValue.x.Equals(other.VectorValue.x) &&
                           VectorValue.y.Equals(other.VectorValue.y) &&
                           VectorValue.z.Equals(other.VectorValue.z) &&
                           VectorValue.w.Equals(other.VectorValue.w);
                default:
                    return false;
            }
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialPropertyValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Type;
                switch (Type)
                {
                    case MaterialPropertyType.Color:
                        hash = hash * 397 ^ ColorValue.r.GetHashCode();
                        hash = hash * 397 ^ ColorValue.g.GetHashCode();
                        hash = hash * 397 ^ ColorValue.b.GetHashCode();
                        hash = hash * 397 ^ ColorValue.a.GetHashCode();
                        break;
                    case MaterialPropertyType.Float:
                        hash = hash * 397 ^ FloatValue.GetHashCode();
                        break;
                    case MaterialPropertyType.Vector:
                        hash = hash * 397 ^ VectorValue.x.GetHashCode();
                        hash = hash * 397 ^ VectorValue.y.GetHashCode();
                        hash = hash * 397 ^ VectorValue.z.GetHashCode();
                        hash = hash * 397 ^ VectorValue.w.GetHashCode();
                        break;
                }

                return hash;
            }
        }
    }

    public enum MaterialPropertyType
    {
        Color,
        Float,
        Vector
    }

    [Serializable]
    public class RegisterMaterialData
    {
        public MaterialSignature Signature;
    }

    [Serializable]
    public class UnregisterMaterialData
    {
        public string RuntimeMaterialId;
    }

    [Serializable]
    public class UpdateMaterialPropertiesData
    {
        public MaterialSignature Signature;
        public string PropertiesJson;
    }

    [Serializable]
    public class RegisterMaterialResultData
    {
        public string RuntimeMaterialId;
        public bool Success;
        public string ErrorMessage;
    }
}
