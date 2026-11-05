// OSL port: the Imath types OSL uses (Imath::Vec3<float>, Color3<float>, Matrix44<float>), as value types.
//
// Only what the compiler's constant folding and the runtime need; the runtime library grows this as the ops are ported.

using System;

namespace OSL
{
    public struct Vec3 : IEquatable<Vec3>
    {
        public float x, y, z;

        public Vec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vec3(float s) { x = y = z = s; }

        public float this[int i]
        {
            get { return i == 0 ? x : i == 1 ? y : z; }
            set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vec3 operator -(Vec3 a) { return new Vec3(-a.x, -a.y, -a.z); }
        public static Vec3 operator *(Vec3 a, Vec3 b) { return new Vec3(a.x * b.x, a.y * b.y, a.z * b.z); }
        public static Vec3 operator *(Vec3 a, float s) { return new Vec3(a.x * s, a.y * s, a.z * s); }
        public static Vec3 operator *(float s, Vec3 a) { return a * s; }
        public static Vec3 operator /(Vec3 a, Vec3 b) { return new Vec3(a.x / b.x, a.y / b.y, a.z / b.z); }
        public static Vec3 operator /(Vec3 a, float s) { return new Vec3(a.x / s, a.y / s, a.z / s); }
        public static bool operator ==(Vec3 a, Vec3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vec3 a, Vec3 b) { return !(a == b); }
        public bool Equals(Vec3 o) { return this == o; }
        public override bool Equals(object o) { return o is Vec3 v && this == v; }
        public override int GetHashCode() { return HashCode.Combine(x, y, z); }

        public float dot(Vec3 b) { return x * b.x + y * b.y + z * b.z; }
        public Vec3 cross(Vec3 b) { return new Vec3(y * b.z - z * b.y, z * b.x - x * b.z, x * b.y - y * b.x); }
        public float length2() { return dot(this); }
        public float length() { return MathF.Sqrt(length2()); }
        public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; }
    }

    // Color3 is a separate type in Imath; here the same layout with the colour names
    public struct Color3 : IEquatable<Color3>
    {
        public float x, y, z;

        public Color3(float r, float g, float b) { x = r; y = g; z = b; }
        public Color3(float s) { x = y = z = s; }
        public Color3(Vec3 v) { x = v.x; y = v.y; z = v.z; }

        public float r { get { return x; } set { x = value; } }
        public float g { get { return y; } set { y = value; } }
        public float b { get { return z; } set { z = value; } }

        public float this[int i]
        {
            get { return i == 0 ? x : i == 1 ? y : z; }
            set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; }
        }

        public static Color3 operator +(Color3 a, Color3 b) { return new Color3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Color3 operator -(Color3 a, Color3 b) { return new Color3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Color3 operator *(Color3 a, Color3 b) { return new Color3(a.x * b.x, a.y * b.y, a.z * b.z); }
        public static Color3 operator *(Color3 a, float s) { return new Color3(a.x * s, a.y * s, a.z * s); }
        public static Color3 operator *(float s, Color3 a) { return a * s; }
        public static Color3 operator /(Color3 a, Color3 b) { return new Color3(a.x / b.x, a.y / b.y, a.z / b.z); }
        public static Color3 operator /(Color3 a, float s) { return new Color3(a.x / s, a.y / s, a.z / s); }
        public static bool operator ==(Color3 a, Color3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Color3 a, Color3 b) { return !(a == b); }
        public bool Equals(Color3 o) { return this == o; }
        public override bool Equals(object o) { return o is Color3 v && this == v; }
        public override int GetHashCode() { return HashCode.Combine(x, y, z); }
        public static implicit operator Vec3(Color3 c) { return new Vec3(c.x, c.y, c.z); }
        public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; }
    }

    // Row-major 4x4, row vectors multiplied on the left: p' = p * M, as in Imath and OSL
    public struct Matrix44
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;

        public Matrix44(float a00, float a01, float a02, float a03, float a10, float a11, float a12, float a13,
                        float a20, float a21, float a22, float a23, float a30, float a31, float a32, float a33)
        {
            m00 = a00; m01 = a01; m02 = a02; m03 = a03;
            m10 = a10; m11 = a11; m12 = a12; m13 = a13;
            m20 = a20; m21 = a21; m22 = a22; m23 = a23;
            m30 = a30; m31 = a31; m32 = a32; m33 = a33;
        }

        public static Matrix44 identity { get { return new Matrix44(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1); } }

        public float this[int r, int c]
        {
            get { return Get(r * 4 + c); }
            set { Set(r * 4 + c, value); }
        }

        public float Get(int i)
        {
            switch (i)
            {
                case 0: return m00; case 1: return m01; case 2: return m02; case 3: return m03;
                case 4: return m10; case 5: return m11; case 6: return m12; case 7: return m13;
                case 8: return m20; case 9: return m21; case 10: return m22; case 11: return m23;
                case 12: return m30; case 13: return m31; case 14: return m32; default: return m33;
            }
        }

        public void Set(int i, float v)
        {
            switch (i)
            {
                case 0: m00 = v; break; case 1: m01 = v; break; case 2: m02 = v; break; case 3: m03 = v; break;
                case 4: m10 = v; break; case 5: m11 = v; break; case 6: m12 = v; break; case 7: m13 = v; break;
                case 8: m20 = v; break; case 9: m21 = v; break; case 10: m22 = v; break; case 11: m23 = v; break;
                case 12: m30 = v; break; case 13: m31 = v; break; case 14: m32 = v; break; default: m33 = v; break;
            }
        }

        public static Matrix44 operator *(Matrix44 a, Matrix44 b)
        {
            Matrix44 r = new Matrix44();
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    float s = 0;
                    for (int k = 0; k < 4; ++k)
                    {
                        s += a[i, k] * b[k, j];
                    }
                    r[i, j] = s;
                }
            }
            return r;
        }

        public static bool operator ==(Matrix44 a, Matrix44 b)
        {
            for (int i = 0; i < 16; ++i)
            {
                if (a.Get(i) != b.Get(i))
                {
                    return false;
                }
            }
            return true;
        }
        public static bool operator !=(Matrix44 a, Matrix44 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Matrix44 m && this == m; }
        public override int GetHashCode() { return m00.GetHashCode() ^ m33.GetHashCode(); }
    }
}
