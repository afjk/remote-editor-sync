using System;
using UnityEngine;

namespace RemoteEditorSync
{
    [Serializable]
    public struct SerializableVector2
    {
        public float x;
        public float y;

        public SerializableVector2(Vector2 value)
        {
            x = value.x;
            y = value.y;
        }
    }

    [Serializable]
    public struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public SerializableVector3(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }
    }

    [Serializable]
    public struct SerializableVector4
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public SerializableVector4(Vector4 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
            w = value.w;
        }

        public Vector4 ToVector4()
        {
            return new Vector4(x, y, z, w);
        }
    }

    [Serializable]
    public struct SerializableVector2Int
    {
        public int x;
        public int y;

        public SerializableVector2Int(Vector2Int value)
        {
            x = value.x;
            y = value.y;
        }
    }

    [Serializable]
    public struct SerializableVector3Int
    {
        public int x;
        public int y;
        public int z;

        public SerializableVector3Int(Vector3Int value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }
    }

    [Serializable]
    public struct SerializableQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public SerializableQuaternion(Quaternion value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
            w = value.w;
        }
    }

    [Serializable]
    public struct SerializableColor
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public SerializableColor(Color value)
        {
            r = value.r;
            g = value.g;
            b = value.b;
            a = value.a;
        }

        public Color ToColor()
        {
            return new Color(r, g, b, a);
        }
    }

    [Serializable]
    public struct SerializableColor32
    {
        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public SerializableColor32(Color32 value)
        {
            r = value.r;
            g = value.g;
            b = value.b;
            a = value.a;
        }
    }

    [Serializable]
    public struct SerializableRect
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public SerializableRect(Rect value)
        {
            x = value.x;
            y = value.y;
            width = value.width;
            height = value.height;
        }
    }

    [Serializable]
    public struct SerializableRectInt
    {
        public int x;
        public int y;
        public int width;
        public int height;

        public SerializableRectInt(RectInt value)
        {
            x = value.x;
            y = value.y;
            width = value.width;
            height = value.height;
        }
    }

    [Serializable]
    public struct SerializableBounds
    {
        public SerializableVector3 center;
        public SerializableVector3 size;

        public SerializableBounds(Bounds value)
        {
            center = new SerializableVector3(value.center);
            size = new SerializableVector3(value.size);
        }
    }

    [Serializable]
    public struct SerializableBoundsInt
    {
        public SerializableVector3Int position;
        public SerializableVector3Int size;

        public SerializableBoundsInt(BoundsInt value)
        {
            position = new SerializableVector3Int(value.position);
            size = new SerializableVector3Int(value.size);
        }
    }

    [Serializable]
    public struct SerializableMatrix4x4
    {
        public float m00; public float m01; public float m02; public float m03;
        public float m10; public float m11; public float m12; public float m13;
        public float m20; public float m21; public float m22; public float m23;
        public float m30; public float m31; public float m32; public float m33;

        public SerializableMatrix4x4(Matrix4x4 value)
        {
            m00 = value.m00; m01 = value.m01; m02 = value.m02; m03 = value.m03;
            m10 = value.m10; m11 = value.m11; m12 = value.m12; m13 = value.m13;
            m20 = value.m20; m21 = value.m21; m22 = value.m22; m23 = value.m23;
            m30 = value.m30; m31 = value.m31; m32 = value.m32; m33 = value.m33;
        }
    }
}
