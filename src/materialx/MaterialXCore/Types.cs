// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Types.h and Types.cpp.

using System;

namespace MaterialX
{
    // Type Strings Definition
    public static partial class Mx
    {
        public const string DEFAULT_TYPE_STRING = "color3";
        public const string FILENAME_TYPE_STRING = "filename";
        public const string GEOMNAME_TYPE_STRING = "geomname";
        public const string STRING_TYPE_STRING = "string";
        public const string BSDF_TYPE_STRING = "BSDF";
        public const string EDF_TYPE_STRING = "EDF";
        public const string VDF_TYPE_STRING = "VDF";
        public const string SURFACE_SHADER_TYPE_STRING = "surfaceshader";
        public const string DISPLACEMENT_SHADER_TYPE_STRING = "displacementshader";
        public const string VOLUME_SHADER_TYPE_STRING = "volumeshader";
        public const string LIGHT_SHADER_TYPE_STRING = "lightshader";
        public const string MATERIAL_TYPE_STRING = "material";
        public const string SURFACE_MATERIAL_NODE_STRING = "surfacematerial";
        public const string VOLUME_MATERIAL_NODE_STRING = "volumematerial";
        public const string MULTI_OUTPUT_TYPE_STRING = "multioutput";
        public const string NONE_TYPE_STRING = "none";
        public const string VALUE_STRING_TRUE = "true";
        public const string VALUE_STRING_FALSE = "false";
        public const string NAME_PREFIX_SEPARATOR = ":";
        public const string NAME_PATH_SEPARATOR = "/";
        public const string ARRAY_VALID_SEPARATORS = ", ";
        public const string ARRAY_PREFERRED_SEPARATOR = ", ";
    }

    // Vector2 Definition
    public struct Vector2 : IEquatable<Vector2>, IComparable<Vector2>
    {
        // Vector2 Public Members
        public float x;
        public float y;

        // Vector2 Public Methods
        public Vector2(float s)
        {
            x = s;
            y = s;
        }

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public Vector2(float[] values)
        {
            x = values[0];
            y = values[1];
        }

        public const int numElementsConstant = 2;

        public static int numElements()
        {
            return 2;
        }

        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0:
                        return x;
                    case 1:
                        return y;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
            set
            {
                switch (i)
                {
                    case 0:
                        x = value;
                        break;
                    case 1:
                        y = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
        }

        public static bool operator ==(Vector2 a, Vector2 b)
        {
            return a.x == b.x && a.y == b.y;
        }

        public static bool operator !=(Vector2 a, Vector2 b)
        {
            return !(a == b);
        }

        public static bool operator <(Vector2 a, Vector2 b)
        {
            return a.CompareTo(b) < 0;
        }

        public static bool operator >(Vector2 a, Vector2 b)
        {
            return a.CompareTo(b) > 0;
        }

        public bool Equals(Vector2 other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Vector2 other && this == other;
        }

        public int CompareTo(Vector2 other)
        {
            if (x < other.x)
            {
                return -1;
            }
            if (other.x < x)
            {
                return 1;
            }
            if (y < other.y)
            {
                return -1;
            }
            if (other.y < y)
            {
                return 1;
            }
            return 0;
        }

        public override int GetHashCode()
        {
            ulong h = 0;
            h = Mx.hashCombine(h, x);
            h = Mx.hashCombine(h, y);
            return (int)(h ^ (h >> 32));
        }

        public static Vector2 operator +(Vector2 a, Vector2 b)
        {
            return new Vector2(a.x + b.x, a.y + b.y);
        }

        public void operator +=(Vector2 rhs)
        {
            x += rhs.x;
            y += rhs.y;
        }

        public static Vector2 operator -(Vector2 a, Vector2 b)
        {
            return new Vector2(a.x - b.x, a.y - b.y);
        }

        public void operator -=(Vector2 rhs)
        {
            x -= rhs.x;
            y -= rhs.y;
        }

        public static Vector2 operator *(Vector2 a, Vector2 b)
        {
            return new Vector2(a.x * b.x, a.y * b.y);
        }

        public void operator *=(Vector2 rhs)
        {
            x *= rhs.x;
            y *= rhs.y;
        }

        public static Vector2 operator /(Vector2 a, Vector2 b)
        {
            return new Vector2(a.x / b.x, a.y / b.y);
        }

        public void operator /=(Vector2 rhs)
        {
            x /= rhs.x;
            y /= rhs.y;
        }

        public static Vector2 operator *(Vector2 a, float s)
        {
            return new Vector2(a.x * s, a.y * s);
        }

        public void operator *=(float s)
        {
            x *= s;
            y *= s;
        }

        public static Vector2 operator /(Vector2 a, float s)
        {
            return new Vector2(a.x / s, a.y / s);
        }

        public void operator /=(float s)
        {
            x /= s;
            y /= s;
        }

        public static Vector2 operator -(Vector2 a)
        {
            return new Vector2(-a.x, -a.y);
        }

        public static Vector2 operator +(Vector2 a)
        {
            return a;
        }

        public float getMagnitude()
        {
            float res = 0.0f;
            res += x * x;
            res += y * y;
            return MathF.Sqrt(res);
        }

        public Vector2 getNormalized()
        {
            return this / getMagnitude();
        }

        public float dot(Vector2 rhs)
        {
            float res = 0.0f;
            res += x * rhs.x;
            res += y * rhs.y;
            return res;
        }

        public float[] data()
        {
            return new float[] { x, y };
        }

        public float cross(Vector2 rhs)
        {
            return x * rhs.y - y * rhs.x;
        }
    }

    // Vector3 Definition
    public struct Vector3 : IEquatable<Vector3>, IComparable<Vector3>
    {
        // Vector3 Public Members
        public float x;
        public float y;
        public float z;

        // Vector3 Public Methods
        public Vector3(float s)
        {
            x = s;
            y = s;
            z = s;
        }

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3(float[] values)
        {
            x = values[0];
            y = values[1];
            z = values[2];
        }

        public const int numElementsConstant = 3;

        public static int numElements()
        {
            return 3;
        }

        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0:
                        return x;
                    case 1:
                        return y;
                    case 2:
                        return z;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
            set
            {
                switch (i)
                {
                    case 0:
                        x = value;
                        break;
                    case 1:
                        y = value;
                        break;
                    case 2:
                        z = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
        }

        public static bool operator ==(Vector3 a, Vector3 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z;
        }

        public static bool operator !=(Vector3 a, Vector3 b)
        {
            return !(a == b);
        }

        public static bool operator <(Vector3 a, Vector3 b)
        {
            return a.CompareTo(b) < 0;
        }

        public static bool operator >(Vector3 a, Vector3 b)
        {
            return a.CompareTo(b) > 0;
        }

        public bool Equals(Vector3 other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Vector3 other && this == other;
        }

        public int CompareTo(Vector3 other)
        {
            if (x < other.x)
            {
                return -1;
            }
            if (other.x < x)
            {
                return 1;
            }
            if (y < other.y)
            {
                return -1;
            }
            if (other.y < y)
            {
                return 1;
            }
            if (z < other.z)
            {
                return -1;
            }
            if (other.z < z)
            {
                return 1;
            }
            return 0;
        }

        public override int GetHashCode()
        {
            ulong h = 0;
            h = Mx.hashCombine(h, x);
            h = Mx.hashCombine(h, y);
            h = Mx.hashCombine(h, z);
            return (int)(h ^ (h >> 32));
        }

        public static Vector3 operator +(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public void operator +=(Vector3 rhs)
        {
            x += rhs.x;
            y += rhs.y;
            z += rhs.z;
        }

        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public void operator -=(Vector3 rhs)
        {
            x -= rhs.x;
            y -= rhs.y;
            z -= rhs.z;
        }

        public static Vector3 operator *(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        public void operator *=(Vector3 rhs)
        {
            x *= rhs.x;
            y *= rhs.y;
            z *= rhs.z;
        }

        public static Vector3 operator /(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
        }

        public void operator /=(Vector3 rhs)
        {
            x /= rhs.x;
            y /= rhs.y;
            z /= rhs.z;
        }

        public static Vector3 operator *(Vector3 a, float s)
        {
            return new Vector3(a.x * s, a.y * s, a.z * s);
        }

        public void operator *=(float s)
        {
            x *= s;
            y *= s;
            z *= s;
        }

        public static Vector3 operator /(Vector3 a, float s)
        {
            return new Vector3(a.x / s, a.y / s, a.z / s);
        }

        public void operator /=(float s)
        {
            x /= s;
            y /= s;
            z /= s;
        }

        public static Vector3 operator -(Vector3 a)
        {
            return new Vector3(-a.x, -a.y, -a.z);
        }

        public static Vector3 operator +(Vector3 a)
        {
            return a;
        }

        public float getMagnitude()
        {
            float res = 0.0f;
            res += x * x;
            res += y * y;
            res += z * z;
            return MathF.Sqrt(res);
        }

        public Vector3 getNormalized()
        {
            return this / getMagnitude();
        }

        public float dot(Vector3 rhs)
        {
            float res = 0.0f;
            res += x * rhs.x;
            res += y * rhs.y;
            res += z * rhs.z;
            return res;
        }

        public float[] data()
        {
            return new float[] { x, y, z };
        }

        public Vector3 cross(Vector3 rhs)
        {
            return new Vector3(y * rhs.z - z * rhs.y,
                               z * rhs.x - x * rhs.z,
                               x * rhs.y - y * rhs.x);
        }
    }

    // Vector4 Definition
    public struct Vector4 : IEquatable<Vector4>, IComparable<Vector4>
    {
        // Vector4 Public Members
        public float x;
        public float y;
        public float z;
        public float w;

        // Vector4 Public Methods
        public Vector4(float s)
        {
            x = s;
            y = s;
            z = s;
            w = s;
        }

        public Vector4(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public Vector4(float[] values)
        {
            x = values[0];
            y = values[1];
            z = values[2];
            w = values[3];
        }

        public const int numElementsConstant = 4;

        public static int numElements()
        {
            return 4;
        }

        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0:
                        return x;
                    case 1:
                        return y;
                    case 2:
                        return z;
                    case 3:
                        return w;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
            set
            {
                switch (i)
                {
                    case 0:
                        x = value;
                        break;
                    case 1:
                        y = value;
                        break;
                    case 2:
                        z = value;
                        break;
                    case 3:
                        w = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
        }

        public static bool operator ==(Vector4 a, Vector4 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        }

        public static bool operator !=(Vector4 a, Vector4 b)
        {
            return !(a == b);
        }

        public static bool operator <(Vector4 a, Vector4 b)
        {
            return a.CompareTo(b) < 0;
        }

        public static bool operator >(Vector4 a, Vector4 b)
        {
            return a.CompareTo(b) > 0;
        }

        public bool Equals(Vector4 other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Vector4 other && this == other;
        }

        public int CompareTo(Vector4 other)
        {
            if (x < other.x)
            {
                return -1;
            }
            if (other.x < x)
            {
                return 1;
            }
            if (y < other.y)
            {
                return -1;
            }
            if (other.y < y)
            {
                return 1;
            }
            if (z < other.z)
            {
                return -1;
            }
            if (other.z < z)
            {
                return 1;
            }
            if (w < other.w)
            {
                return -1;
            }
            if (other.w < w)
            {
                return 1;
            }
            return 0;
        }

        public override int GetHashCode()
        {
            ulong h = 0;
            h = Mx.hashCombine(h, x);
            h = Mx.hashCombine(h, y);
            h = Mx.hashCombine(h, z);
            h = Mx.hashCombine(h, w);
            return (int)(h ^ (h >> 32));
        }

        public static Vector4 operator +(Vector4 a, Vector4 b)
        {
            return new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        }

        public void operator +=(Vector4 rhs)
        {
            x += rhs.x;
            y += rhs.y;
            z += rhs.z;
            w += rhs.w;
        }

        public static Vector4 operator -(Vector4 a, Vector4 b)
        {
            return new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        }

        public void operator -=(Vector4 rhs)
        {
            x -= rhs.x;
            y -= rhs.y;
            z -= rhs.z;
            w -= rhs.w;
        }

        public static Vector4 operator *(Vector4 a, Vector4 b)
        {
            return new Vector4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        }

        public void operator *=(Vector4 rhs)
        {
            x *= rhs.x;
            y *= rhs.y;
            z *= rhs.z;
            w *= rhs.w;
        }

        public static Vector4 operator /(Vector4 a, Vector4 b)
        {
            return new Vector4(a.x / b.x, a.y / b.y, a.z / b.z, a.w / b.w);
        }

        public void operator /=(Vector4 rhs)
        {
            x /= rhs.x;
            y /= rhs.y;
            z /= rhs.z;
            w /= rhs.w;
        }

        public static Vector4 operator *(Vector4 a, float s)
        {
            return new Vector4(a.x * s, a.y * s, a.z * s, a.w * s);
        }

        public void operator *=(float s)
        {
            x *= s;
            y *= s;
            z *= s;
            w *= s;
        }

        public static Vector4 operator /(Vector4 a, float s)
        {
            return new Vector4(a.x / s, a.y / s, a.z / s, a.w / s);
        }

        public void operator /=(float s)
        {
            x /= s;
            y /= s;
            z /= s;
            w /= s;
        }

        public static Vector4 operator -(Vector4 a)
        {
            return new Vector4(-a.x, -a.y, -a.z, -a.w);
        }

        public static Vector4 operator +(Vector4 a)
        {
            return a;
        }

        public float getMagnitude()
        {
            float res = 0.0f;
            res += x * x;
            res += y * y;
            res += z * z;
            res += w * w;
            return MathF.Sqrt(res);
        }

        public Vector4 getNormalized()
        {
            return this / getMagnitude();
        }

        public float dot(Vector4 rhs)
        {
            float res = 0.0f;
            res += x * rhs.x;
            res += y * rhs.y;
            res += z * rhs.z;
            res += w * rhs.w;
            return res;
        }

        public float[] data()
        {
            return new float[] { x, y, z, w };
        }
    }

    // Color3 Definition
    public struct Color3 : IEquatable<Color3>, IComparable<Color3>
    {
        // Color3 Public Members
        public float r;
        public float g;
        public float b;

        // Color3 Public Methods
        public Color3(float s)
        {
            r = s;
            g = s;
            b = s;
        }

        public Color3(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }

        public Color3(float[] values)
        {
            r = values[0];
            g = values[1];
            b = values[2];
        }

        public const int numElementsConstant = 3;

        public static int numElements()
        {
            return 3;
        }

        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0:
                        return r;
                    case 1:
                        return g;
                    case 2:
                        return b;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
            set
            {
                switch (i)
                {
                    case 0:
                        r = value;
                        break;
                    case 1:
                        g = value;
                        break;
                    case 2:
                        b = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
        }

        public static bool operator ==(Color3 a, Color3 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b;
        }

        public static bool operator !=(Color3 a, Color3 b)
        {
            return !(a == b);
        }

        public static bool operator <(Color3 a, Color3 b)
        {
            return a.CompareTo(b) < 0;
        }

        public static bool operator >(Color3 a, Color3 b)
        {
            return a.CompareTo(b) > 0;
        }

        public bool Equals(Color3 other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Color3 other && this == other;
        }

        public int CompareTo(Color3 other)
        {
            if (r < other.r)
            {
                return -1;
            }
            if (other.r < r)
            {
                return 1;
            }
            if (g < other.g)
            {
                return -1;
            }
            if (other.g < g)
            {
                return 1;
            }
            if (b < other.b)
            {
                return -1;
            }
            if (other.b < b)
            {
                return 1;
            }
            return 0;
        }

        public override int GetHashCode()
        {
            ulong h = 0;
            h = Mx.hashCombine(h, r);
            h = Mx.hashCombine(h, g);
            h = Mx.hashCombine(h, b);
            return (int)(h ^ (h >> 32));
        }

        public static Color3 operator +(Color3 a, Color3 b)
        {
            return new Color3(a.r + b.r, a.g + b.g, a.b + b.b);
        }

        public void operator +=(Color3 rhs)
        {
            r += rhs.r;
            g += rhs.g;
            b += rhs.b;
        }

        public static Color3 operator -(Color3 a, Color3 b)
        {
            return new Color3(a.r - b.r, a.g - b.g, a.b - b.b);
        }

        public void operator -=(Color3 rhs)
        {
            r -= rhs.r;
            g -= rhs.g;
            b -= rhs.b;
        }

        public static Color3 operator *(Color3 a, Color3 b)
        {
            return new Color3(a.r * b.r, a.g * b.g, a.b * b.b);
        }

        public void operator *=(Color3 rhs)
        {
            r *= rhs.r;
            g *= rhs.g;
            b *= rhs.b;
        }

        public static Color3 operator /(Color3 a, Color3 b)
        {
            return new Color3(a.r / b.r, a.g / b.g, a.b / b.b);
        }

        public void operator /=(Color3 rhs)
        {
            r /= rhs.r;
            g /= rhs.g;
            b /= rhs.b;
        }

        public static Color3 operator *(Color3 a, float s)
        {
            return new Color3(a.r * s, a.g * s, a.b * s);
        }

        public void operator *=(float s)
        {
            r *= s;
            g *= s;
            b *= s;
        }

        public static Color3 operator /(Color3 a, float s)
        {
            return new Color3(a.r / s, a.g / s, a.b / s);
        }

        public void operator /=(float s)
        {
            r /= s;
            g /= s;
            b /= s;
        }

        public static Color3 operator -(Color3 a)
        {
            return new Color3(-a.r, -a.g, -a.b);
        }

        public static Color3 operator +(Color3 a)
        {
            return a;
        }

        public float getMagnitude()
        {
            float res = 0.0f;
            res += r * r;
            res += g * g;
            res += b * b;
            return MathF.Sqrt(res);
        }

        public Color3 getNormalized()
        {
            return this / getMagnitude();
        }

        public float dot(Color3 rhs)
        {
            float res = 0.0f;
            res += r * rhs.r;
            res += g * rhs.g;
            res += b * rhs.b;
            return res;
        }

        public float[] data()
        {
            return new float[] { r, g, b };
        }

        public Color3 linearToSrgb()
        {
            Color3 res = new Color3();
            for (int i = 0; i < 3; i++)
            {
                if (this[i] <= 0.0031308f)
                {
                    res[i] = this[i] * 12.92f;
                }
                else
                {
                    res[i] = 1.055f * MathF.Pow(this[i], 1.0f / 2.4f) - 0.055f;
                }
            }
            return res;
        }

        public Color3 srgbToLinear()
        {
            Color3 res = new Color3();
            for (int i = 0; i < 3; i++)
            {
                if (this[i] <= 0.04045f)
                {
                    res[i] = this[i] / 12.92f;
                }
                else
                {
                    res[i] = MathF.Pow((this[i] + 0.055f) / 1.055f, 2.4f);
                }
            }
            return res;
        }
    }

    // Color4 Definition
    public struct Color4 : IEquatable<Color4>, IComparable<Color4>
    {
        // Color4 Public Members
        public float r;
        public float g;
        public float b;
        public float a;

        // Color4 Public Methods
        public Color4(float s)
        {
            r = s;
            g = s;
            b = s;
            a = s;
        }

        public Color4(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Color4(float[] values)
        {
            r = values[0];
            g = values[1];
            b = values[2];
            a = values[3];
        }

        public const int numElementsConstant = 4;

        public static int numElements()
        {
            return 4;
        }

        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0:
                        return r;
                    case 1:
                        return g;
                    case 2:
                        return b;
                    case 3:
                        return a;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
            set
            {
                switch (i)
                {
                    case 0:
                        r = value;
                        break;
                    case 1:
                        g = value;
                        break;
                    case 2:
                        b = value;
                        break;
                    case 3:
                        a = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
        }

        public static bool operator ==(Color4 a, Color4 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        public static bool operator !=(Color4 a, Color4 b)
        {
            return !(a == b);
        }

        public static bool operator <(Color4 a, Color4 b)
        {
            return a.CompareTo(b) < 0;
        }

        public static bool operator >(Color4 a, Color4 b)
        {
            return a.CompareTo(b) > 0;
        }

        public bool Equals(Color4 other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Color4 other && this == other;
        }

        public int CompareTo(Color4 other)
        {
            if (r < other.r)
            {
                return -1;
            }
            if (other.r < r)
            {
                return 1;
            }
            if (g < other.g)
            {
                return -1;
            }
            if (other.g < g)
            {
                return 1;
            }
            if (b < other.b)
            {
                return -1;
            }
            if (other.b < b)
            {
                return 1;
            }
            if (a < other.a)
            {
                return -1;
            }
            if (other.a < a)
            {
                return 1;
            }
            return 0;
        }

        public override int GetHashCode()
        {
            ulong h = 0;
            h = Mx.hashCombine(h, r);
            h = Mx.hashCombine(h, g);
            h = Mx.hashCombine(h, b);
            h = Mx.hashCombine(h, a);
            return (int)(h ^ (h >> 32));
        }

        public static Color4 operator +(Color4 a, Color4 b)
        {
            return new Color4(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);
        }

        public void operator +=(Color4 rhs)
        {
            r += rhs.r;
            g += rhs.g;
            b += rhs.b;
            a += rhs.a;
        }

        public static Color4 operator -(Color4 a, Color4 b)
        {
            return new Color4(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);
        }

        public void operator -=(Color4 rhs)
        {
            r -= rhs.r;
            g -= rhs.g;
            b -= rhs.b;
            a -= rhs.a;
        }

        public static Color4 operator *(Color4 a, Color4 b)
        {
            return new Color4(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        }

        public void operator *=(Color4 rhs)
        {
            r *= rhs.r;
            g *= rhs.g;
            b *= rhs.b;
            a *= rhs.a;
        }

        public static Color4 operator /(Color4 a, Color4 b)
        {
            return new Color4(a.r / b.r, a.g / b.g, a.b / b.b, a.a / b.a);
        }

        public void operator /=(Color4 rhs)
        {
            r /= rhs.r;
            g /= rhs.g;
            b /= rhs.b;
            a /= rhs.a;
        }

        public static Color4 operator *(Color4 a, float s)
        {
            return new Color4(a.r * s, a.g * s, a.b * s, a.a * s);
        }

        public void operator *=(float s)
        {
            r *= s;
            g *= s;
            b *= s;
            a *= s;
        }

        public static Color4 operator /(Color4 a, float s)
        {
            return new Color4(a.r / s, a.g / s, a.b / s, a.a / s);
        }

        public void operator /=(float s)
        {
            r /= s;
            g /= s;
            b /= s;
            a /= s;
        }

        public static Color4 operator -(Color4 a)
        {
            return new Color4(-a.r, -a.g, -a.b, -a.a);
        }

        public static Color4 operator +(Color4 a)
        {
            return a;
        }

        public float getMagnitude()
        {
            float res = 0.0f;
            res += r * r;
            res += g * g;
            res += b * b;
            res += a * a;
            return MathF.Sqrt(res);
        }

        public Color4 getNormalized()
        {
            return this / getMagnitude();
        }

        public float dot(Color4 rhs)
        {
            float res = 0.0f;
            res += r * rhs.r;
            res += g * rhs.g;
            res += b * rhs.b;
            res += a * rhs.a;
            return res;
        }

        public float[] data()
        {
            return new float[] { r, g, b, a };
        }
    }

    // Matrix33 Definition
    public struct Matrix33 : IEquatable<Matrix33>
    {
        // Matrix33 Public Members
        public float m00, m01, m02;
        public float m10, m11, m12;
        public float m20, m21, m22;

        public static readonly Matrix33 IDENTITY = new Matrix33(1, 0, 0, 0, 1, 0, 0, 0, 1);

        // Matrix33 Public Methods
        public Matrix33(float s)
        {
            m00 = s;
            m01 = s;
            m02 = s;
            m10 = s;
            m11 = s;
            m12 = s;
            m20 = s;
            m21 = s;
            m22 = s;
        }

        public Matrix33(float m00, float m01, float m02,
            float m10, float m11, float m12,
            float m20, float m21, float m22)
        {
            this.m00 = m00;
            this.m01 = m01;
            this.m02 = m02;
            this.m10 = m10;
            this.m11 = m11;
            this.m12 = m12;
            this.m20 = m20;
            this.m21 = m21;
            this.m22 = m22;
        }

        public Matrix33(float[] values)
        {
            m00 = values[0];
            m01 = values[1];
            m02 = values[2];
            m10 = values[3];
            m11 = values[4];
            m12 = values[5];
            m20 = values[6];
            m21 = values[7];
            m22 = values[8];
        }

        public static int numRows()
        {
            return 3;
        }

        public static int numColumns()
        {
            return 3;
        }

        public float this[int i, int j]
        {
            get
            {
                switch (i * 3 + j)
                {
                    case 0:
                        return m00;
                    case 1:
                        return m01;
                    case 2:
                        return m02;
                    case 3:
                        return m10;
                    case 4:
                        return m11;
                    case 5:
                        return m12;
                    case 6:
                        return m20;
                    case 7:
                        return m21;
                    case 8:
                        return m22;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            set
            {
                switch (i * 3 + j)
                {
                    case 0:
                        m00 = value;
                        break;
                    case 1:
                        m01 = value;
                        break;
                    case 2:
                        m02 = value;
                        break;
                    case 3:
                        m10 = value;
                        break;
                    case 4:
                        m11 = value;
                        break;
                    case 5:
                        m12 = value;
                        break;
                    case 6:
                        m20 = value;
                        break;
                    case 7:
                        m21 = value;
                        break;
                    case 8:
                        m22 = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public static bool operator ==(Matrix33 a, Matrix33 b)
        {
            return a.m00 == b.m00 && a.m01 == b.m01 && a.m02 == b.m02 &&
                   a.m10 == b.m10 && a.m11 == b.m11 && a.m12 == b.m12 &&
                   a.m20 == b.m20 && a.m21 == b.m21 && a.m22 == b.m22;
        }

        public static bool operator !=(Matrix33 a, Matrix33 b)
        {
            return !(a == b);
        }

        public bool Equals(Matrix33 other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Matrix33 other && this == other;
        }

        public override int GetHashCode()
        {
            ulong h = 0;
            h = Mx.hashCombine(h, m00);
            h = Mx.hashCombine(h, m01);
            h = Mx.hashCombine(h, m02);
            h = Mx.hashCombine(h, m10);
            h = Mx.hashCombine(h, m11);
            h = Mx.hashCombine(h, m12);
            h = Mx.hashCombine(h, m20);
            h = Mx.hashCombine(h, m21);
            h = Mx.hashCombine(h, m22);
            return (int)(h ^ (h >> 32));
        }

        public bool isEquivalent(Matrix33 rhs, float tolerance)
        {
            if (MathF.Abs(m00 - rhs.m00) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m01 - rhs.m01) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m02 - rhs.m02) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m10 - rhs.m10) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m11 - rhs.m11) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m12 - rhs.m12) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m20 - rhs.m20) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m21 - rhs.m21) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m22 - rhs.m22) > tolerance)
            {
                return false;
            }
            return true;
        }

        public static Matrix33 operator +(Matrix33 a, Matrix33 b)
        {
            return new Matrix33(a.m00 + b.m00, a.m01 + b.m01, a.m02 + b.m02,
                a.m10 + b.m10, a.m11 + b.m11, a.m12 + b.m12,
                a.m20 + b.m20, a.m21 + b.m21, a.m22 + b.m22);
        }

        public void operator +=(Matrix33 rhs)
        {
            this = this + rhs;
        }

        public static Matrix33 operator -(Matrix33 a, Matrix33 b)
        {
            return new Matrix33(a.m00 - b.m00, a.m01 - b.m01, a.m02 - b.m02,
                a.m10 - b.m10, a.m11 - b.m11, a.m12 - b.m12,
                a.m20 - b.m20, a.m21 - b.m21, a.m22 - b.m22);
        }

        public void operator -=(Matrix33 rhs)
        {
            this = this - rhs;
        }

        public static Matrix33 operator *(Matrix33 a, float s)
        {
            return new Matrix33(a.m00 * s, a.m01 * s, a.m02 * s,
                a.m10 * s, a.m11 * s, a.m12 * s,
                a.m20 * s, a.m21 * s, a.m22 * s);
        }

        public void operator *=(float s)
        {
            this = this * s;
        }

        public static Matrix33 operator /(Matrix33 a, float s)
        {
            return new Matrix33(a.m00 / s, a.m01 / s, a.m02 / s,
                a.m10 / s, a.m11 / s, a.m12 / s,
                a.m20 / s, a.m21 / s, a.m22 / s);
        }

        public void operator /=(float s)
        {
            this = this / s;
        }

        public static Matrix33 operator *(Matrix33 a, Matrix33 b)
        {
            Matrix33 res = new Matrix33();
            res.m00 += a.m00 * b.m00;
            res.m00 += a.m01 * b.m10;
            res.m00 += a.m02 * b.m20;
            res.m01 += a.m00 * b.m01;
            res.m01 += a.m01 * b.m11;
            res.m01 += a.m02 * b.m21;
            res.m02 += a.m00 * b.m02;
            res.m02 += a.m01 * b.m12;
            res.m02 += a.m02 * b.m22;
            res.m10 += a.m10 * b.m00;
            res.m10 += a.m11 * b.m10;
            res.m10 += a.m12 * b.m20;
            res.m11 += a.m10 * b.m01;
            res.m11 += a.m11 * b.m11;
            res.m11 += a.m12 * b.m21;
            res.m12 += a.m10 * b.m02;
            res.m12 += a.m11 * b.m12;
            res.m12 += a.m12 * b.m22;
            res.m20 += a.m20 * b.m00;
            res.m20 += a.m21 * b.m10;
            res.m20 += a.m22 * b.m20;
            res.m21 += a.m20 * b.m01;
            res.m21 += a.m21 * b.m11;
            res.m21 += a.m22 * b.m21;
            res.m22 += a.m20 * b.m02;
            res.m22 += a.m21 * b.m12;
            res.m22 += a.m22 * b.m22;
            return res;
        }

        public void operator *=(Matrix33 rhs)
        {
            this = this * rhs;
        }

        public static Matrix33 operator /(Matrix33 a, Matrix33 b)
        {
            return a * b.getInverse();
        }

        public void operator /=(Matrix33 rhs)
        {
            this = this * rhs.getInverse();
        }

        public static Matrix33 operator -(Matrix33 a)
        {
            return new Matrix33(-a.m00, -a.m01, -a.m02,
                -a.m10, -a.m11, -a.m12,
                -a.m20, -a.m21, -a.m22);
        }

        public static Matrix33 operator +(Matrix33 a)
        {
            return a;
        }

        public float[] data()
        {
            return new float[] { m00, m01, m02, m10, m11, m12, m20, m21, m22 };
        }

        public Matrix33 getTranspose()
        {
            return new Matrix33(m00, m10, m20,
                            m01, m11, m21,
                            m02, m12, m22);
        }

        public float getDeterminant()
        {
            return m00 * (m11*m22 - m21*m12) +
                   m01 * (m12*m20 - m22*m10) +
                   m02 * (m10*m21 - m20*m11);
        }

        public Matrix33 getAdjugate()
        {
            return new Matrix33(
                m11*m22 - m21*m12,
                m21*m02 - m01*m22,
                m01*m12 - m11*m02,
                m12*m20 - m22*m10,
                m22*m00 - m02*m20,
                m02*m10 - m12*m00,
                m10*m21 - m20*m11,
                m20*m01 - m00*m21,
                m00*m11 - m10*m01);
        }

        public Matrix33 getInverse()
        {
            return getAdjugate() / getDeterminant();
        }

        public Vector3 multiply(Vector3 v)
        {
            return new Vector3(
              v.x*m00 + v.y*m10 + v.z*m20,
              v.x*m01 + v.y*m11 + v.z*m21,
              v.x*m02 + v.y*m12 + v.z*m22);
        }

        public Vector2 transformPoint(Vector2 v)
        {
            Vector3 res = multiply(new Vector3(v.x, v.y, 1.0f));
            return new Vector2(res.x, res.y);
        }

        public Vector2 transformVector(Vector2 v)
        {
            Vector3 res = multiply(new Vector3(v.x, v.y, 0.0f));
            return new Vector2(res.x, res.y);
        }

        public Vector3 transformNormal(Vector3 v)
        {
            return getInverse().getTranspose().multiply(v);
        }

        public static Matrix33 createTranslation(Vector2 v)
        {
            return new Matrix33(1.0f, 0.0f, 0.0f,
                            0.0f, 1.0f, 0.0f,
                            v.x, v.y, 1.0f);
        }

        public static Matrix33 createScale(Vector2 v)
        {
            return new Matrix33(v.x, 0.0f, 0.0f,
                            0.0f, v.y, 0.0f,
                            0.0f, 0.0f, 1.0f);
        }

        public static Matrix33 createRotation(float angle)
        {
            float sin = MathF.Sin(angle);
            float cos = MathF.Cos(angle);

            return new Matrix33( cos,  sin, 0.0f,
                            -sin,  cos, 0.0f,
                            0.0f, 0.0f, 1.0f);
        }
    }

    // Matrix44 Definition
    public struct Matrix44 : IEquatable<Matrix44>
    {
        // Matrix44 Public Members
        public float m00, m01, m02, m03;
        public float m10, m11, m12, m13;
        public float m20, m21, m22, m23;
        public float m30, m31, m32, m33;

        public static readonly Matrix44 IDENTITY = new Matrix44(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

        // Matrix44 Public Methods
        public Matrix44(float s)
        {
            m00 = s;
            m01 = s;
            m02 = s;
            m03 = s;
            m10 = s;
            m11 = s;
            m12 = s;
            m13 = s;
            m20 = s;
            m21 = s;
            m22 = s;
            m23 = s;
            m30 = s;
            m31 = s;
            m32 = s;
            m33 = s;
        }

        public Matrix44(float m00, float m01, float m02, float m03,
            float m10, float m11, float m12, float m13,
            float m20, float m21, float m22, float m23,
            float m30, float m31, float m32, float m33)
        {
            this.m00 = m00;
            this.m01 = m01;
            this.m02 = m02;
            this.m03 = m03;
            this.m10 = m10;
            this.m11 = m11;
            this.m12 = m12;
            this.m13 = m13;
            this.m20 = m20;
            this.m21 = m21;
            this.m22 = m22;
            this.m23 = m23;
            this.m30 = m30;
            this.m31 = m31;
            this.m32 = m32;
            this.m33 = m33;
        }

        public Matrix44(float[] values)
        {
            m00 = values[0];
            m01 = values[1];
            m02 = values[2];
            m03 = values[3];
            m10 = values[4];
            m11 = values[5];
            m12 = values[6];
            m13 = values[7];
            m20 = values[8];
            m21 = values[9];
            m22 = values[10];
            m23 = values[11];
            m30 = values[12];
            m31 = values[13];
            m32 = values[14];
            m33 = values[15];
        }

        public static int numRows()
        {
            return 4;
        }

        public static int numColumns()
        {
            return 4;
        }

        public float this[int i, int j]
        {
            get
            {
                switch (i * 4 + j)
                {
                    case 0:
                        return m00;
                    case 1:
                        return m01;
                    case 2:
                        return m02;
                    case 3:
                        return m03;
                    case 4:
                        return m10;
                    case 5:
                        return m11;
                    case 6:
                        return m12;
                    case 7:
                        return m13;
                    case 8:
                        return m20;
                    case 9:
                        return m21;
                    case 10:
                        return m22;
                    case 11:
                        return m23;
                    case 12:
                        return m30;
                    case 13:
                        return m31;
                    case 14:
                        return m32;
                    case 15:
                        return m33;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            set
            {
                switch (i * 4 + j)
                {
                    case 0:
                        m00 = value;
                        break;
                    case 1:
                        m01 = value;
                        break;
                    case 2:
                        m02 = value;
                        break;
                    case 3:
                        m03 = value;
                        break;
                    case 4:
                        m10 = value;
                        break;
                    case 5:
                        m11 = value;
                        break;
                    case 6:
                        m12 = value;
                        break;
                    case 7:
                        m13 = value;
                        break;
                    case 8:
                        m20 = value;
                        break;
                    case 9:
                        m21 = value;
                        break;
                    case 10:
                        m22 = value;
                        break;
                    case 11:
                        m23 = value;
                        break;
                    case 12:
                        m30 = value;
                        break;
                    case 13:
                        m31 = value;
                        break;
                    case 14:
                        m32 = value;
                        break;
                    case 15:
                        m33 = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public static bool operator ==(Matrix44 a, Matrix44 b)
        {
            return a.m00 == b.m00 && a.m01 == b.m01 && a.m02 == b.m02 && a.m03 == b.m03 &&
                   a.m10 == b.m10 && a.m11 == b.m11 && a.m12 == b.m12 && a.m13 == b.m13 &&
                   a.m20 == b.m20 && a.m21 == b.m21 && a.m22 == b.m22 && a.m23 == b.m23 &&
                   a.m30 == b.m30 && a.m31 == b.m31 && a.m32 == b.m32 && a.m33 == b.m33;
        }

        public static bool operator !=(Matrix44 a, Matrix44 b)
        {
            return !(a == b);
        }

        public bool Equals(Matrix44 other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Matrix44 other && this == other;
        }

        public override int GetHashCode()
        {
            ulong h = 0;
            h = Mx.hashCombine(h, m00);
            h = Mx.hashCombine(h, m01);
            h = Mx.hashCombine(h, m02);
            h = Mx.hashCombine(h, m03);
            h = Mx.hashCombine(h, m10);
            h = Mx.hashCombine(h, m11);
            h = Mx.hashCombine(h, m12);
            h = Mx.hashCombine(h, m13);
            h = Mx.hashCombine(h, m20);
            h = Mx.hashCombine(h, m21);
            h = Mx.hashCombine(h, m22);
            h = Mx.hashCombine(h, m23);
            h = Mx.hashCombine(h, m30);
            h = Mx.hashCombine(h, m31);
            h = Mx.hashCombine(h, m32);
            h = Mx.hashCombine(h, m33);
            return (int)(h ^ (h >> 32));
        }

        public bool isEquivalent(Matrix44 rhs, float tolerance)
        {
            if (MathF.Abs(m00 - rhs.m00) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m01 - rhs.m01) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m02 - rhs.m02) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m03 - rhs.m03) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m10 - rhs.m10) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m11 - rhs.m11) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m12 - rhs.m12) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m13 - rhs.m13) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m20 - rhs.m20) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m21 - rhs.m21) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m22 - rhs.m22) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m23 - rhs.m23) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m30 - rhs.m30) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m31 - rhs.m31) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m32 - rhs.m32) > tolerance)
            {
                return false;
            }
            if (MathF.Abs(m33 - rhs.m33) > tolerance)
            {
                return false;
            }
            return true;
        }

        public static Matrix44 operator +(Matrix44 a, Matrix44 b)
        {
            return new Matrix44(a.m00 + b.m00, a.m01 + b.m01, a.m02 + b.m02, a.m03 + b.m03,
                a.m10 + b.m10, a.m11 + b.m11, a.m12 + b.m12, a.m13 + b.m13,
                a.m20 + b.m20, a.m21 + b.m21, a.m22 + b.m22, a.m23 + b.m23,
                a.m30 + b.m30, a.m31 + b.m31, a.m32 + b.m32, a.m33 + b.m33);
        }

        public void operator +=(Matrix44 rhs)
        {
            this = this + rhs;
        }

        public static Matrix44 operator -(Matrix44 a, Matrix44 b)
        {
            return new Matrix44(a.m00 - b.m00, a.m01 - b.m01, a.m02 - b.m02, a.m03 - b.m03,
                a.m10 - b.m10, a.m11 - b.m11, a.m12 - b.m12, a.m13 - b.m13,
                a.m20 - b.m20, a.m21 - b.m21, a.m22 - b.m22, a.m23 - b.m23,
                a.m30 - b.m30, a.m31 - b.m31, a.m32 - b.m32, a.m33 - b.m33);
        }

        public void operator -=(Matrix44 rhs)
        {
            this = this - rhs;
        }

        public static Matrix44 operator *(Matrix44 a, float s)
        {
            return new Matrix44(a.m00 * s, a.m01 * s, a.m02 * s, a.m03 * s,
                a.m10 * s, a.m11 * s, a.m12 * s, a.m13 * s,
                a.m20 * s, a.m21 * s, a.m22 * s, a.m23 * s,
                a.m30 * s, a.m31 * s, a.m32 * s, a.m33 * s);
        }

        public void operator *=(float s)
        {
            this = this * s;
        }

        public static Matrix44 operator /(Matrix44 a, float s)
        {
            return new Matrix44(a.m00 / s, a.m01 / s, a.m02 / s, a.m03 / s,
                a.m10 / s, a.m11 / s, a.m12 / s, a.m13 / s,
                a.m20 / s, a.m21 / s, a.m22 / s, a.m23 / s,
                a.m30 / s, a.m31 / s, a.m32 / s, a.m33 / s);
        }

        public void operator /=(float s)
        {
            this = this / s;
        }

        public static Matrix44 operator *(Matrix44 a, Matrix44 b)
        {
            Matrix44 res = new Matrix44();
            res.m00 += a.m00 * b.m00;
            res.m00 += a.m01 * b.m10;
            res.m00 += a.m02 * b.m20;
            res.m00 += a.m03 * b.m30;
            res.m01 += a.m00 * b.m01;
            res.m01 += a.m01 * b.m11;
            res.m01 += a.m02 * b.m21;
            res.m01 += a.m03 * b.m31;
            res.m02 += a.m00 * b.m02;
            res.m02 += a.m01 * b.m12;
            res.m02 += a.m02 * b.m22;
            res.m02 += a.m03 * b.m32;
            res.m03 += a.m00 * b.m03;
            res.m03 += a.m01 * b.m13;
            res.m03 += a.m02 * b.m23;
            res.m03 += a.m03 * b.m33;
            res.m10 += a.m10 * b.m00;
            res.m10 += a.m11 * b.m10;
            res.m10 += a.m12 * b.m20;
            res.m10 += a.m13 * b.m30;
            res.m11 += a.m10 * b.m01;
            res.m11 += a.m11 * b.m11;
            res.m11 += a.m12 * b.m21;
            res.m11 += a.m13 * b.m31;
            res.m12 += a.m10 * b.m02;
            res.m12 += a.m11 * b.m12;
            res.m12 += a.m12 * b.m22;
            res.m12 += a.m13 * b.m32;
            res.m13 += a.m10 * b.m03;
            res.m13 += a.m11 * b.m13;
            res.m13 += a.m12 * b.m23;
            res.m13 += a.m13 * b.m33;
            res.m20 += a.m20 * b.m00;
            res.m20 += a.m21 * b.m10;
            res.m20 += a.m22 * b.m20;
            res.m20 += a.m23 * b.m30;
            res.m21 += a.m20 * b.m01;
            res.m21 += a.m21 * b.m11;
            res.m21 += a.m22 * b.m21;
            res.m21 += a.m23 * b.m31;
            res.m22 += a.m20 * b.m02;
            res.m22 += a.m21 * b.m12;
            res.m22 += a.m22 * b.m22;
            res.m22 += a.m23 * b.m32;
            res.m23 += a.m20 * b.m03;
            res.m23 += a.m21 * b.m13;
            res.m23 += a.m22 * b.m23;
            res.m23 += a.m23 * b.m33;
            res.m30 += a.m30 * b.m00;
            res.m30 += a.m31 * b.m10;
            res.m30 += a.m32 * b.m20;
            res.m30 += a.m33 * b.m30;
            res.m31 += a.m30 * b.m01;
            res.m31 += a.m31 * b.m11;
            res.m31 += a.m32 * b.m21;
            res.m31 += a.m33 * b.m31;
            res.m32 += a.m30 * b.m02;
            res.m32 += a.m31 * b.m12;
            res.m32 += a.m32 * b.m22;
            res.m32 += a.m33 * b.m32;
            res.m33 += a.m30 * b.m03;
            res.m33 += a.m31 * b.m13;
            res.m33 += a.m32 * b.m23;
            res.m33 += a.m33 * b.m33;
            return res;
        }

        public void operator *=(Matrix44 rhs)
        {
            this = this * rhs;
        }

        public static Matrix44 operator /(Matrix44 a, Matrix44 b)
        {
            return a * b.getInverse();
        }

        public void operator /=(Matrix44 rhs)
        {
            this = this * rhs.getInverse();
        }

        public static Matrix44 operator -(Matrix44 a)
        {
            return new Matrix44(-a.m00, -a.m01, -a.m02, -a.m03,
                -a.m10, -a.m11, -a.m12, -a.m13,
                -a.m20, -a.m21, -a.m22, -a.m23,
                -a.m30, -a.m31, -a.m32, -a.m33);
        }

        public static Matrix44 operator +(Matrix44 a)
        {
            return a;
        }

        public float[] data()
        {
            return new float[] { m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33 };
        }

        public Matrix44 getTranspose()
        {
            return new Matrix44(m00, m10, m20, m30,
                            m01, m11, m21, m31,
                            m02, m12, m22, m32,
                            m03, m13, m23, m33);
        }

        public float getDeterminant()
        {
            return m00 * (m11*m22*m33 + m31*m12*m23 + m21*m32*m13 -
                                 m11*m32*m23 - m21*m12*m33 - m31*m22*m13) +
                   m01 * (m12*m33*m20 + m22*m13*m30 + m32*m23*m10 -
                                 m12*m23*m30 - m32*m13*m20 - m22*m33*m10) +
                   m02 * (m13*m20*m31 + m33*m10*m21 + m23*m30*m11 -
                                 m13*m30*m21 - m23*m10*m31 - m33*m20*m11) +
                   m03 * (m10*m31*m22 + m20*m11*m32 + m30*m21*m12 -
                                 m10*m21*m32 - m30*m11*m22 - m20*m31*m12);
        }

        public Matrix44 getAdjugate()
        {
            return new Matrix44(
                m11*m22*m33 + m31*m12*m23 + m21*m32*m13 -
                m11*m32*m23 - m21*m12*m33 - m31*m22*m13,

                m01*m32*m23 + m21*m02*m33 + m31*m22*m03 -
                m31*m02*m23 - m21*m32*m03 - m01*m22*m33,

                m01*m12*m33 + m31*m02*m13 + m11*m32*m03 -
                m01*m32*m13 - m11*m02*m33 - m31*m12*m03,

                m01*m22*m13 + m11*m02*m23 + m21*m12*m03 -
                m01*m12*m23 - m21*m02*m13 - m11*m22*m03,

                m12*m33*m20 + m22*m13*m30 + m32*m23*m10 -
                m12*m23*m30 - m32*m13*m20 - m22*m33*m10,

                m02*m23*m30 + m32*m03*m20 + m22*m33*m00 -
                m02*m33*m20 - m22*m03*m30 - m32*m23*m00,

                m02*m33*m10 + m12*m03*m30 + m32*m13*m00 -
                m02*m13*m30 - m32*m03*m10 - m12*m33*m00,

                m02*m13*m20 + m22*m03*m10 + m12*m23*m00 -
                m02*m23*m10 - m12*m03*m20 - m22*m13*m00,

                m13*m20*m31 + m33*m10*m21 + m23*m30*m11 -
                m13*m30*m21 - m23*m10*m31 - m33*m20*m11,

                m03*m30*m21 + m23*m00*m31 + m33*m20*m01 -
                m03*m20*m31 - m33*m00*m21 - m23*m30*m01,

                m03*m10*m31 + m33*m00*m11 + m13*m30*m01 -
                m03*m30*m11 - m13*m00*m31 - m33*m10*m01,

                m03*m20*m11 + m13*m00*m21 + m23*m10*m01 -
                m03*m10*m21 - m23*m00*m11 - m13*m20*m01,

                m10*m31*m22 + m20*m11*m32 + m30*m21*m12 -
                m10*m21*m32 - m30*m11*m22 - m20*m31*m12,

                m00*m21*m32 + m30*m01*m22 + m20*m31*m02 -
                m00*m31*m22 - m20*m01*m32 - m30*m21*m02,

                m00*m31*m12 + m10*m01*m32 + m30*m11*m02 -
                m00*m11*m32 - m30*m01*m12 - m10*m31*m02,

                m00*m11*m22 + m20*m01*m12 + m10*m21*m02 -
                m00*m21*m12 - m10*m01*m22 - m20*m11*m02);
        }

        public Matrix44 getInverse()
        {
            return getAdjugate() / getDeterminant();
        }

        public Vector4 multiply(Vector4 v)
        {
            return new Vector4(
              v.x*m00 + v.y*m10 + v.z*m20 + v.w*m30,
              v.x*m01 + v.y*m11 + v.z*m21 + v.w*m31,
              v.x*m02 + v.y*m12 + v.z*m22 + v.w*m32,
              v.x*m03 + v.y*m13 + v.z*m23 + v.w*m33);
        }

        public Vector3 transformPoint(Vector3 v)
        {
            Vector4 res = multiply(new Vector4(v.x, v.y, v.z, 1.0f));
            return new Vector3(res.x, res.y, res.z);
        }

        public Vector3 transformVector(Vector3 v)
        {
            Vector4 res = multiply(new Vector4(v.x, v.y, v.z, 0.0f));
            return new Vector3(res.x, res.y, res.z);
        }

        public Vector3 transformNormal(Vector3 v)
        {
            return getInverse().getTranspose().transformVector(v);
        }

        public static Matrix44 createTranslation(Vector3 v)
        {
            return new Matrix44(1.0f, 0.0f, 0.0f, 0.0f,
                            0.0f, 1.0f, 0.0f, 0.0f,
                            0.0f, 0.0f, 1.0f, 0.0f,
                            v.x, v.y, v.z, 1.0f);
        }

        public static Matrix44 createScale(Vector3 v)
        {
            return new Matrix44(v.x, 0.0f, 0.0f, 0.0f,
                            0.0f, v.y, 0.0f, 0.0f,
                            0.0f, 0.0f, v.z, 0.0f,
                            0.0f, 0.0f, 0.0f, 1.0f);
        }

        public static Matrix44 createRotationX(float angle)
        {
            float sin = MathF.Sin(angle);
            float cos = MathF.Cos(angle);

            return new Matrix44(1.0f, 0.0f, 0.0f, 0.0f,
                            0.0f,  cos,  sin, 0.0f,
                            0.0f, -sin,  cos, 0.0f,
                            0.0f, 0.0f, 0.0f, 1.0f);
        }

        public static Matrix44 createRotationY(float angle)
        {
            float sin = MathF.Sin(angle);
            float cos = MathF.Cos(angle);

            return new Matrix44( cos, 0.0f, -sin, 0.0f,
                            0.0f, 1.0f, 0.0f, 0.0f,
                             sin, 0.0f,  cos, 0.0f,
                            0.0f, 0.0f, 0.0f, 1.0f);
        }

        public static Matrix44 createRotationZ(float angle)
        {
            float sin = MathF.Sin(angle);
            float cos = MathF.Cos(angle);

            return new Matrix44( cos,  sin, 0.0f, 0.0f,
                            -sin,  cos, 0.0f, 0.0f,
                            0.0f, 0.0f, 1.0f, 0.0f,
                            0.0f, 0.0f, 0.0f, 1.0f);
        }
    }
}
