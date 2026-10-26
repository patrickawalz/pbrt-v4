// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: MPL-2.0
// nanovdb/NanoVDB.h
//
// Port of the NanoVDB data structure (the *read* side: everything that pbrt-v4 and
// nanovdb2pbrt use). C++ templates over the value type ("BuildT") become C# generics over
// unmanaged numeric types (float, double, int, uint, long); the memory layout is the
// on-disk/in-memory NanoVDB layout, accessed through pointers like the C++ (the C++ classes
// can never be constructed; they are reinterpret_cast views onto the raw buffer, and so are
// the structs here).
//
// NOT ported from NanoVDB.h: Fp4/Fp8/Fp16/FpN/Half/bool/ValueMask/Rgba8/Vec3/Vec4 leaf types,
// PointAccessor, the write-side setters (those are used only by GridBuilder, which is not
// ported), and the partially-cached ReadAccessor variants (<-1,-1,-1>, <0,-1,-1>, ...).

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace nanovdb
{
    public static class Defs
    {
        public const ulong NANOVDB_MAGIC_NUMBER = 0x304244566f6e614eUL;  // "NanoVDB0" in hex - little endian (uint64_t)
        public const int NANOVDB_MAJOR_VERSION_NUMBER = 32;
        public const int NANOVDB_MINOR_VERSION_NUMBER = 3;
        public const int NANOVDB_PATCH_VERSION_NUMBER = 3;
        public const int NANOVDB_DATA_ALIGNMENT = 32;
    }

    // GridType Enumeration
    public enum GridType : uint
    {
        Unknown = 0,
        Float = 1,
        Double = 2,
        Int16 = 3,
        Int32 = 4,
        Int64 = 5,
        Vec3f = 6,
        Vec3d = 7,
        Mask = 8,
        Half = 9,
        UInt32 = 10,
        Boolean = 11,
        RGBA8 = 12,
        Fp4 = 13,
        Fp8 = 14,
        Fp16 = 15,
        FpN = 16,
        Vec4f = 17,
        Vec4d = 18,
        End = 19
    }

    // GridClass Enumeration
    public enum GridClass : uint
    {
        Unknown = 0,
        LevelSet = 1,
        FogVolume = 2,
        Staggered = 3,
        PointIndex = 4,
        PointData = 5,
        Topology = 6,
        VoxelVolume = 7,
        End = 8
    }

    // GridFlags Enumeration
    public enum GridFlags : uint
    {
        HasLongGridName = 1 << 0,
        HasBBox = 1 << 1,
        HasMinMax = 1 << 2,
        HasAverage = 1 << 3,
        HasStdDeviation = 1 << 4,
        IsBreadthFirst = 1 << 5,
        End = 1 << 6,
    }

    public enum GridBlindDataClass : uint
    {
        Unknown = 0,
        IndexArray = 1,
        AttributeArray = 2,
        GridName = 3,
        End = 4
    }

    public enum GridBlindDataSemantic : uint
    {
        Unknown = 0,
        PointPosition = 1,
        PointColor = 2,
        PointNormal = 3,
        PointRadius = 4,
        PointVelocity = 5,
        PointId = 6,
        End = 7
    }

    public static unsafe partial class NanoVDB
    {
        static readonly string[] gridTypeLUT = { "?", "float", "double", "int16", "int32",
                                                 "int64", "Vec3f", "Vec3d", "Mask", "Half",
                                                 "uint32", "bool", "RGBA8", "Float4", "Float8",
                                                 "Float16", "FloatN", "Vec4f", "Vec4d", "End" };
        public static string toStr(GridType gridType) { return gridTypeLUT[(int)gridType]; }

        static readonly string[] gridClassLUT = { "?", "SDF", "FOG", "MAC", "PNTIDX",
                                                  "PNTDAT", "TOPO", "VOX", "END" };
        public static string toStr(GridClass gridClass) { return gridClassLUT[(int)gridClass]; }

        static readonly string[] gridFlagsLUT = { "has long grid name", "has bbox", "has min/max",
                                                  "has average", "has standard deviation",
                                                  "is breadth-first", "end" };
        public static string toStr(GridFlags gridFlags)
        {
            // (the C++ indexes the LUT with the enum value itself)
            return gridFlagsLUT[(int)gridFlags];
        }

        // mapToGridType<BuildT>()
        public static GridType mapToGridType<BuildT>()
        {
            if (typeof(BuildT) == typeof(float))
                return GridType.Float;
            else if (typeof(BuildT) == typeof(double))
                return GridType.Double;
            else if (typeof(BuildT) == typeof(short))
                return GridType.Int16;
            else if (typeof(BuildT) == typeof(int))
                return GridType.Int32;
            else if (typeof(BuildT) == typeof(long))
                return GridType.Int64;
            else if (typeof(BuildT) == typeof(uint))
                return GridType.UInt32;
            return GridType.Unknown;
        }

        // AlignUp<wordSize>(byteCount)
        public static ulong AlignUp(ulong wordSize, ulong byteCount)
        {
            ulong r = byteCount % wordSize;
            return r != 0 ? byteCount - r + wordSize : byteCount;
        }

        // Floor/Ceil/Pow/Abs helpers used by the samplers
        public static int Floor(float x) { return (int)MathF.Floor(x); }
        public static int Floor(double x) { return (int)Math.Floor(x); }
        public static int Ceil(float x) { return (int)MathF.Ceiling(x); }
        public static int Ceil(double x) { return (int)Math.Ceiling(x); }

        // Round<Coord>(Vec3<float>): rintf
        public static Coord Round(Vec3<float> xyz)
        {
            return new Coord((int)MathF.Round(xyz[0], MidpointRounding.ToEven), (int)MathF.Round(xyz[1], MidpointRounding.ToEven),
                             (int)MathF.Round(xyz[2], MidpointRounding.ToEven));
        }
        // Round<Coord>(Vec3<double>)
        public static Coord Round(Vec3<double> xyz)
        {
            return new Coord((int)Math.Floor(xyz[0] + 0.5), (int)Math.Floor(xyz[1] + 0.5), (int)Math.Floor(xyz[2] + 0.5));
        }

        // matMult: float matrices (fmaf)
        public static Vec3<float> matMult(float* mat, Vec3<float> xyz)
        {
            return new Vec3<float>(MathF.FusedMultiplyAdd(xyz[0], mat[0], MathF.FusedMultiplyAdd(xyz[1], mat[1], xyz[2] * mat[2])),
                                   MathF.FusedMultiplyAdd(xyz[0], mat[3], MathF.FusedMultiplyAdd(xyz[1], mat[4], xyz[2] * mat[5])),
                                   MathF.FusedMultiplyAdd(xyz[0], mat[6], MathF.FusedMultiplyAdd(xyz[1], mat[7], xyz[2] * mat[8])));
        }
        public static Vec3<double> matMult(float* mat, Vec3<double> xyz)
        {
            // Vec3T(fmaf(xyz[0], mat[0], ...)) with xyz[i] converted to float
            float x = (float)xyz[0], y = (float)xyz[1], z = (float)xyz[2];
            return new Vec3<double>(MathF.FusedMultiplyAdd(x, mat[0], MathF.FusedMultiplyAdd(y, mat[1], z * mat[2])),
                                    MathF.FusedMultiplyAdd(x, mat[3], MathF.FusedMultiplyAdd(y, mat[4], z * mat[5])),
                                    MathF.FusedMultiplyAdd(x, mat[6], MathF.FusedMultiplyAdd(y, mat[7], z * mat[8])));
        }
        public static Vec3<float> matMult(double* mat, Vec3<float> xyz)
        {
            return new Vec3<float>((float)Math.FusedMultiplyAdd((double)xyz[0], mat[0], Math.FusedMultiplyAdd((double)xyz[1], mat[1], (double)xyz[2] * mat[2])),
                                   (float)Math.FusedMultiplyAdd((double)xyz[0], mat[3], Math.FusedMultiplyAdd((double)xyz[1], mat[4], (double)xyz[2] * mat[5])),
                                   (float)Math.FusedMultiplyAdd((double)xyz[0], mat[6], Math.FusedMultiplyAdd((double)xyz[1], mat[7], (double)xyz[2] * mat[8])));
        }
        public static Vec3<double> matMult(double* mat, Vec3<double> xyz)
        {
            return new Vec3<double>(Math.FusedMultiplyAdd(xyz[0], mat[0], Math.FusedMultiplyAdd(xyz[1], mat[1], xyz[2] * mat[2])),
                                    Math.FusedMultiplyAdd(xyz[0], mat[3], Math.FusedMultiplyAdd(xyz[1], mat[4], xyz[2] * mat[5])),
                                    Math.FusedMultiplyAdd(xyz[0], mat[6], Math.FusedMultiplyAdd(xyz[1], mat[7], xyz[2] * mat[8])));
        }
        public static Vec3<float> matMult(float* mat, float* vec, Vec3<float> xyz)
        {
            return new Vec3<float>(MathF.FusedMultiplyAdd(xyz[0], mat[0], MathF.FusedMultiplyAdd(xyz[1], mat[1], MathF.FusedMultiplyAdd(xyz[2], mat[2], vec[0]))),
                                   MathF.FusedMultiplyAdd(xyz[0], mat[3], MathF.FusedMultiplyAdd(xyz[1], mat[4], MathF.FusedMultiplyAdd(xyz[2], mat[5], vec[1]))),
                                   MathF.FusedMultiplyAdd(xyz[0], mat[6], MathF.FusedMultiplyAdd(xyz[1], mat[7], MathF.FusedMultiplyAdd(xyz[2], mat[8], vec[2]))));
        }
        public static Vec3<double> matMult(float* mat, float* vec, Vec3<double> xyz)
        {
            float x = (float)xyz[0], y = (float)xyz[1], z = (float)xyz[2];
            return new Vec3<double>(MathF.FusedMultiplyAdd(x, mat[0], MathF.FusedMultiplyAdd(y, mat[1], MathF.FusedMultiplyAdd(z, mat[2], vec[0]))),
                                    MathF.FusedMultiplyAdd(x, mat[3], MathF.FusedMultiplyAdd(y, mat[4], MathF.FusedMultiplyAdd(z, mat[5], vec[1]))),
                                    MathF.FusedMultiplyAdd(x, mat[6], MathF.FusedMultiplyAdd(y, mat[7], MathF.FusedMultiplyAdd(z, mat[8], vec[2]))));
        }
        public static Vec3<float> matMult(double* mat, double* vec, Vec3<float> xyz)
        {
            return new Vec3<float>((float)Math.FusedMultiplyAdd((double)xyz[0], mat[0], Math.FusedMultiplyAdd((double)xyz[1], mat[1], Math.FusedMultiplyAdd((double)xyz[2], mat[2], vec[0]))),
                                   (float)Math.FusedMultiplyAdd((double)xyz[0], mat[3], Math.FusedMultiplyAdd((double)xyz[1], mat[4], Math.FusedMultiplyAdd((double)xyz[2], mat[5], vec[1]))),
                                   (float)Math.FusedMultiplyAdd((double)xyz[0], mat[6], Math.FusedMultiplyAdd((double)xyz[1], mat[7], Math.FusedMultiplyAdd((double)xyz[2], mat[8], vec[2]))));
        }
        public static Vec3<double> matMult(double* mat, double* vec, Vec3<double> xyz)
        {
            return new Vec3<double>(Math.FusedMultiplyAdd(xyz[0], mat[0], Math.FusedMultiplyAdd(xyz[1], mat[1], Math.FusedMultiplyAdd(xyz[2], mat[2], vec[0]))),
                                    Math.FusedMultiplyAdd(xyz[0], mat[3], Math.FusedMultiplyAdd(xyz[1], mat[4], Math.FusedMultiplyAdd(xyz[2], mat[5], vec[1]))),
                                    Math.FusedMultiplyAdd(xyz[0], mat[6], Math.FusedMultiplyAdd(xyz[1], mat[7], Math.FusedMultiplyAdd(xyz[2], mat[8], vec[2]))));
        }
        // matMultT
        public static Vec3<float> matMultT(float* mat, Vec3<float> xyz)
        {
            return new Vec3<float>(MathF.FusedMultiplyAdd(xyz[0], mat[0], MathF.FusedMultiplyAdd(xyz[1], mat[3], xyz[2] * mat[6])),
                                   MathF.FusedMultiplyAdd(xyz[0], mat[1], MathF.FusedMultiplyAdd(xyz[1], mat[4], xyz[2] * mat[7])),
                                   MathF.FusedMultiplyAdd(xyz[0], mat[2], MathF.FusedMultiplyAdd(xyz[1], mat[5], xyz[2] * mat[8])));
        }
        public static Vec3<double> matMultT(double* mat, Vec3<double> xyz)
        {
            return new Vec3<double>(Math.FusedMultiplyAdd(xyz[0], mat[0], Math.FusedMultiplyAdd(xyz[1], mat[3], xyz[2] * mat[6])),
                                    Math.FusedMultiplyAdd(xyz[0], mat[1], Math.FusedMultiplyAdd(xyz[1], mat[4], xyz[2] * mat[7])),
                                    Math.FusedMultiplyAdd(xyz[0], mat[2], Math.FusedMultiplyAdd(xyz[1], mat[5], xyz[2] * mat[8])));
        }
    }

    // Version Class
    public struct Version
    {
        uint mData;  // 11 + 11 + 10 bit packing of major + minor + patch

        public static Version Default()
        {
            Version v = new Version();
            v.mData = (uint)Defs.NANOVDB_MAJOR_VERSION_NUMBER << 21 |
                      (uint)Defs.NANOVDB_MINOR_VERSION_NUMBER << 10 |
                      (uint)Defs.NANOVDB_PATCH_VERSION_NUMBER;
            return v;
        }
        public Version(uint major, uint minor, uint patch)
        {
            mData = major << 21 | minor << 10 | patch;
        }
        public static Version FromId(uint id) { Version v = new Version(); v.mData = id; return v; }
        public static bool operator ==(Version a, Version b) { return a.mData == b.mData; }
        public static bool operator !=(Version a, Version b) { return a.mData != b.mData; }
        public static bool operator <(Version a, Version b) { return a.mData < b.mData; }
        public static bool operator <=(Version a, Version b) { return a.mData <= b.mData; }
        public static bool operator >(Version a, Version b) { return a.mData > b.mData; }
        public static bool operator >=(Version a, Version b) { return a.mData >= b.mData; }
        public override bool Equals(object obj) { return obj is Version v && v.mData == mData; }
        public override int GetHashCode() { return (int)mData; }
        public uint id() { return mData; }
        public uint getMajor() { return (mData >> 21) & ((1u << 11) - 1); }
        public uint getMinor() { return (mData >> 10) & ((1u << 11) - 1); }
        public uint getPatch() { return mData & ((1u << 10) - 1); }
        public string c_str() { return getMajor() + "." + getMinor() + "." + getPatch(); }
    }

    // Coord Class
    // Signed (i, j, k) 32-bit integer coordinate class, similar to openvdb::math::Coord
    [StructLayout(LayoutKind.Sequential)]
    public struct Coord
    {
        int x0, x1, x2;  // mVec[3]

        public Coord(int n) { x0 = n; x1 = n; x2 = n; }
        public Coord(int i, int j, int k) { x0 = i; x1 = j; x2 = k; }

        public int x() { return x0; }
        public int y() { return x1; }
        public int z() { return x2; }

        public static Coord max() { return new Coord((int)((1u << 31) - 1)); }
        public static Coord min() { return new Coord(-(int)((1u << 31) - 1) - 1); }
        public static ulong memUsage() { return 12; }

        public int this[int i]
        {
            get { return i == 0 ? x0 : i == 1 ? x1 : x2; }
            set { if (i == 0) x0 = value; else if (i == 1) x1 = value; else x2 = value; }
        }

        public static Coord operator &(Coord c, uint n) { return new Coord(c.x0 & (int)n, c.x1 & (int)n, c.x2 & (int)n); }
        public static Coord operator <<(Coord c, int n) { return new Coord(c.x0 << n, c.x1 << n, c.x2 << n); }
        public static Coord operator >>(Coord c, int n) { return new Coord(c.x0 >> n, c.x1 >> n, c.x2 >> n); }
        public static bool operator <(Coord a, Coord rhs)
        {
            return a.x0 < rhs.x0 ? true : a.x0 > rhs.x0 ? false : a.x1 < rhs.x1 ? true : a.x1 > rhs.x1 ? false : a.x2 < rhs.x2 ? true : false;
        }
        public static bool operator >(Coord a, Coord rhs) { return rhs < a; }
        public static bool operator ==(Coord a, Coord rhs) { return a.x0 == rhs.x0 && a.x1 == rhs.x1 && a.x2 == rhs.x2; }
        public static bool operator !=(Coord a, Coord rhs) { return a.x0 != rhs.x0 || a.x1 != rhs.x1 || a.x2 != rhs.x2; }
        public override bool Equals(object obj) { return obj is Coord c && c == this; }
        public override int GetHashCode() { return x0 * 73856093 ^ x1 * 19349663 ^ x2 * 83492791; }
        public static Coord operator +(Coord a, Coord rhs) { return new Coord(a.x0 + rhs.x0, a.x1 + rhs.x1, a.x2 + rhs.x2); }
        public static Coord operator -(Coord a, Coord rhs) { return new Coord(a.x0 - rhs.x0, a.x1 - rhs.x1, a.x2 - rhs.x2); }

        public Coord minComponent(Coord other)
        {
            if (other.x0 < x0) x0 = other.x0;
            if (other.x1 < x1) x1 = other.x1;
            if (other.x2 < x2) x2 = other.x2;
            return this;
        }
        public Coord maxComponent(Coord other)
        {
            if (other.x0 > x0) x0 = other.x0;
            if (other.x1 > x1) x1 = other.x1;
            if (other.x2 > x2) x2 = other.x2;
            return this;
        }
        public Coord offsetBy(int dx, int dy, int dz) { return new Coord(x0 + dx, x1 + dy, x2 + dz); }
        public Coord offsetBy(int n) { return offsetBy(n, n, n); }
        public static bool lessThan(Coord a, Coord b) { return (a.x0 < b.x0 || a.x1 < b.x1 || a.x2 < b.x2); }

        public Vec3<float> asVec3s() { return new Vec3<float>((float)x0, (float)x1, (float)x2); }
        public Vec3<double> asVec3d() { return new Vec3<double>((double)x0, (double)x1, (double)x2); }
    }

    // Vec3 Class
    [StructLayout(LayoutKind.Sequential)]
    public struct Vec3<T> where T : unmanaged, INumber<T>
    {
        T v0, v1, v2;  // mVec[3]

        public Vec3(T x) { v0 = x; v1 = x; v2 = x; }
        public Vec3(T x, T y, T z) { v0 = x; v1 = y; v2 = z; }
        public Vec3(Coord ijk) { v0 = T.CreateTruncating(ijk[0]); v1 = T.CreateTruncating(ijk[1]); v2 = T.CreateTruncating(ijk[2]); }
        public static Vec3<T> From<T2>(Vec3<T2> v) where T2 : unmanaged, INumber<T2>
        {
            return new Vec3<T>(T.CreateTruncating(v[0]), T.CreateTruncating(v[1]), T.CreateTruncating(v[2]));
        }

        public T this[int i]
        {
            get { return i == 0 ? v0 : i == 1 ? v1 : v2; }
            set { if (i == 0) v0 = value; else if (i == 1) v1 = value; else v2 = value; }
        }

        public static bool operator ==(Vec3<T> a, Vec3<T> rhs) { return a.v0 == rhs.v0 && a.v1 == rhs.v1 && a.v2 == rhs.v2; }
        public static bool operator !=(Vec3<T> a, Vec3<T> rhs) { return a.v0 != rhs.v0 || a.v1 != rhs.v1 || a.v2 != rhs.v2; }
        public override bool Equals(object obj) { return obj is Vec3<T> v && v == this; }
        public override int GetHashCode() { return HashCode.Combine(v0, v1, v2); }

        public T dot(Vec3<T> v) { return v0 * v[0] + v1 * v[1] + v2 * v[2]; }
        public Vec3<T> cross(Vec3<T> v)
        {
            return new Vec3<T>(v1 * v[2] - v2 * v[1], v2 * v[0] - v0 * v[2], v0 * v[1] - v1 * v[0]);
        }
        public T lengthSqr() { return v0 * v0 + v1 * v1 + v2 * v2; }
        public T length()
        {
            return T.CreateTruncating(Math.Sqrt(double.CreateTruncating(lengthSqr())));
        }
        public static Vec3<T> operator -(Vec3<T> a) { return new Vec3<T>(-a.v0, -a.v1, -a.v2); }
        public static Vec3<T> operator *(Vec3<T> a, Vec3<T> v) { return new Vec3<T>(a.v0 * v[0], a.v1 * v[1], a.v2 * v[2]); }
        public static Vec3<T> operator /(Vec3<T> a, Vec3<T> v) { return new Vec3<T>(a.v0 / v[0], a.v1 / v[1], a.v2 / v[2]); }
        public static Vec3<T> operator +(Vec3<T> a, Vec3<T> v) { return new Vec3<T>(a.v0 + v[0], a.v1 + v[1], a.v2 + v[2]); }
        public static Vec3<T> operator -(Vec3<T> a, Vec3<T> v) { return new Vec3<T>(a.v0 - v[0], a.v1 - v[1], a.v2 - v[2]); }
        public static Vec3<T> operator *(Vec3<T> a, T s) { return new Vec3<T>(s * a.v0, s * a.v1, s * a.v2); }
        public static Vec3<T> operator *(T s, Vec3<T> a) { return new Vec3<T>(s * a.v0, s * a.v1, s * a.v2); }
        public static Vec3<T> operator /(Vec3<T> a, T s) { return (T.One / s) * a; }

        public Vec3<T> minComponent(Vec3<T> other)
        {
            if (other[0] < v0) v0 = other[0];
            if (other[1] < v1) v1 = other[1];
            if (other[2] < v2) v2 = other[2];
            return this;
        }
        public Vec3<T> maxComponent(Vec3<T> other)
        {
            if (other[0] > v0) v0 = other[0];
            if (other[1] > v1) v1 = other[1];
            if (other[2] > v2) v2 = other[2];
            return this;
        }
        public T min() { return v0 < v1 ? (v0 < v2 ? v0 : v2) : (v1 < v2 ? v1 : v2); }
        public T max() { return v0 > v1 ? (v0 > v2 ? v0 : v2) : (v1 > v2 ? v1 : v2); }
        public Coord floor() { return new Coord(NanoVDB.Floor(double.CreateTruncating(v0)), NanoVDB.Floor(double.CreateTruncating(v1)), NanoVDB.Floor(double.CreateTruncating(v2))); }
        public Coord ceil() { return new Coord(NanoVDB.Ceil(double.CreateTruncating(v0)), NanoVDB.Ceil(double.CreateTruncating(v1)), NanoVDB.Ceil(double.CreateTruncating(v2))); }
        public Coord round()
        {
            return new Coord(NanoVDB.Floor(double.CreateTruncating(v0) + 0.5), NanoVDB.Floor(double.CreateTruncating(v1) + 0.5),
                             NanoVDB.Floor(double.CreateTruncating(v2) + 0.5));
        }
    }

    // BBox<Vec3T> for floating-point Vec3T (BBox<Vec3T, true> in the C++)
    [StructLayout(LayoutKind.Sequential)]
    public struct BBox<T> where T : unmanaged, INumber<T>
    {
        Vec3<T> mCoord0, mCoord1;  // mCoord[2]

        public BBox(Vec3<T> min, Vec3<T> max) { mCoord0 = min; mCoord1 = max; }
        public BBox(Coord min, Coord max)
        {
            mCoord0 = new Vec3<T>(T.CreateTruncating(min[0]), T.CreateTruncating(min[1]), T.CreateTruncating(min[2]));
            mCoord1 = new Vec3<T>(T.CreateTruncating(max[0] + 1), T.CreateTruncating(max[1] + 1), T.CreateTruncating(max[2] + 1));
        }
        public static BBox<T> Default()
        {
            // Maximum<ValueType>::value() = numeric_limits<T>::max()
            T m = T.CreateTruncating(typeof(T) == typeof(float) ? (double)float.MaxValue : double.MaxValue);
            return new BBox<T>(new Vec3<T>(m), new Vec3<T>(-m));
        }
        public Vec3<T> this[int i]
        {
            get { return i == 0 ? mCoord0 : mCoord1; }
            set { if (i == 0) mCoord0 = value; else mCoord1 = value; }
        }
        public Vec3<T> min() { return mCoord0; }
        public Vec3<T> max() { return mCoord1; }
        public bool empty()
        {
            return mCoord0[0] >= mCoord1[0] || mCoord0[1] >= mCoord1[1] || mCoord0[2] >= mCoord1[2];
        }
        public Vec3<T> dim() { return empty() ? new Vec3<T>(T.Zero) : max() - min(); }
        public bool isInside(Vec3<T> p)
        {
            return p[0] > mCoord0[0] && p[1] > mCoord0[1] && p[2] > mCoord0[2] &&
                   p[0] < mCoord1[0] && p[1] < mCoord1[1] && p[2] < mCoord1[2];
        }
    }

    // CoordBBox = BBox<Coord> (BBox<CoordT, false> in the C++)
    [StructLayout(LayoutKind.Sequential)]
    public struct CoordBBox
    {
        Coord mCoord0, mCoord1;  // mCoord[2]

        public CoordBBox(Coord min, Coord max) { mCoord0 = min; mCoord1 = max; }
        public static CoordBBox Default() { return new CoordBBox(Coord.max(), Coord.min()); }
        public Coord this[int i]
        {
            get { return i == 0 ? mCoord0 : mCoord1; }
            set { if (i == 0) mCoord0 = value; else mCoord1 = value; }
        }
        public Coord min() { return mCoord0; }
        public Coord max() { return mCoord1; }
        public bool is_divisible()
        {
            return mCoord0[0] < mCoord1[0] && mCoord0[1] < mCoord1[1] && mCoord0[2] < mCoord1[2];
        }
        public bool empty()
        {
            return mCoord0[0] > mCoord1[0] || mCoord0[1] > mCoord1[1] || mCoord0[2] > mCoord1[2];
        }
        public Coord dim() { return empty() ? new Coord(0) : max() - min() + new Coord(1); }
        public ulong volume() { Coord d = dim(); return (ulong)d[0] * (ulong)d[1] * (ulong)d[2]; }
        public bool isInside(Coord p) { return !(Coord.lessThan(p, min()) || Coord.lessThan(max(), p)); }
        public bool isInside(CoordBBox b) { return !(Coord.lessThan(b.min(), min()) || Coord.lessThan(max(), b.max())); }
        public BBox<RealT> asReal<RealT>() where RealT : unmanaged, INumber<RealT>
        {
            return new BBox<RealT>(new Vec3<RealT>(RealT.CreateTruncating(mCoord0[0]), RealT.CreateTruncating(mCoord0[1]), RealT.CreateTruncating(mCoord0[2])),
                                   new Vec3<RealT>(RealT.CreateTruncating(mCoord1[0] + 1), RealT.CreateTruncating(mCoord1[1] + 1), RealT.CreateTruncating(mCoord1[2] + 1)));
        }
        public CoordBBox expandBy(int padding) { return new CoordBBox(mCoord0.offsetBy(-padding), mCoord1.offsetBy(padding)); }
    }

    // Mask Class: a view onto the bit-mask words of a node (SIZE = 1 << (3 * LOG2DIM) bits)
    public unsafe struct Mask
    {
        public ulong* mWords;
        public uint LOG2DIM;
        public Mask(void* p, uint log2dim) { mWords = (ulong*)p; LOG2DIM = log2dim; }
        public uint SIZE { get { return 1u << (int)(3 * LOG2DIM); } }
        public uint WORD_COUNT { get { return SIZE >> 6; } }
        public bool isOn(uint n) { return 0 != (mWords[n >> 6] & (1UL << (int)(n & 63))); }
        public bool isOff()
        {
            for (uint i = 0; i < WORD_COUNT; ++i)
                if (mWords[i] != 0) return false;
            return true;
        }
        public bool isOn()
        {
            for (uint i = 0; i < WORD_COUNT; ++i)
                if (mWords[i] != ulong.MaxValue) return false;
            return true;
        }
        public uint countOn()
        {
            uint sum = 0;
            for (uint i = 0; i < WORD_COUNT; ++i)
                sum += (uint)BitOperations.PopCount(mWords[i]);
            return sum;
        }
    }

    // Map Struct (affine index <-> world transform)
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Map
    {
        public fixed float mMatF[9];
        public fixed float mInvMatF[9];
        public fixed float mVecF[3];
        public float mTaperF;
        public fixed double mMatD[9];
        public fixed double mInvMatD[9];
        public fixed double mVecD[3];
        public double mTaperD;

        public Vec3<float> applyMapF(Vec3<float> xyz) { fixed (Map* t = &this) return NanoVDB.matMult(t->mMatF, t->mVecF, xyz); }
        public Vec3<double> applyMapF(Vec3<double> xyz) { fixed (Map* t = &this) return NanoVDB.matMult(t->mMatF, t->mVecF, xyz); }
        public Vec3<float> applyMap(Vec3<float> xyz) { fixed (Map* t = &this) return NanoVDB.matMult(t->mMatD, t->mVecD, xyz); }
        public Vec3<double> applyMap(Vec3<double> xyz) { fixed (Map* t = &this) return NanoVDB.matMult(t->mMatD, t->mVecD, xyz); }

        public Vec3<float> applyJacobianF(Vec3<float> xyz) { fixed (Map* t = &this) return NanoVDB.matMult(t->mMatF, xyz); }
        public Vec3<float> applyJacobian(Vec3<float> xyz) { fixed (Map* t = &this) return NanoVDB.matMult(t->mMatD, xyz); }
        public Vec3<double> applyJacobian(Vec3<double> xyz) { fixed (Map* t = &this) return NanoVDB.matMult(t->mMatD, xyz); }

        public Vec3<float> applyInverseMapF(Vec3<float> xyz)
        {
            fixed (Map* t = &this)
                return NanoVDB.matMult(t->mInvMatF, new Vec3<float>(xyz[0] - t->mVecF[0], xyz[1] - t->mVecF[1], xyz[2] - t->mVecF[2]));
        }
        public Vec3<double> applyInverseMapF(Vec3<double> xyz)
        {
            fixed (Map* t = &this)
                return NanoVDB.matMult(t->mInvMatF, new Vec3<double>(xyz[0] - t->mVecF[0], xyz[1] - t->mVecF[1], xyz[2] - t->mVecF[2]));
        }
        public Vec3<float> applyInverseMap(Vec3<float> xyz)
        {
            fixed (Map* t = &this)
                return NanoVDB.matMult(t->mInvMatD, new Vec3<float>((float)(xyz[0] - t->mVecD[0]), (float)(xyz[1] - t->mVecD[1]), (float)(xyz[2] - t->mVecD[2])));
        }
        public Vec3<double> applyInverseMap(Vec3<double> xyz)
        {
            fixed (Map* t = &this)
                return NanoVDB.matMult(t->mInvMatD, new Vec3<double>(xyz[0] - t->mVecD[0], xyz[1] - t->mVecD[1], xyz[2] - t->mVecD[2]));
        }

        public Vec3<float> applyInverseJacobianF(Vec3<float> xyz) { fixed (Map* t = &this) return NanoVDB.matMult(t->mInvMatF, xyz); }
        public Vec3<float> applyInverseJacobian(Vec3<float> xyz) { fixed (Map* t = &this) return NanoVDB.matMult(t->mInvMatD, xyz); }
        public Vec3<double> applyInverseJacobian(Vec3<double> xyz) { fixed (Map* t = &this) return NanoVDB.matMult(t->mInvMatD, xyz); }
        public Vec3<float> applyIJTF(Vec3<float> xyz) { fixed (Map* t = &this) return NanoVDB.matMultT(t->mInvMatF, xyz); }
        public Vec3<double> applyIJT(Vec3<double> xyz) { fixed (Map* t = &this) return NanoVDB.matMultT(t->mInvMatD, xyz); }
    }

    // GridBlindMetaData Struct
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GridBlindMetaData
    {
        public const int MaxNameSize = 256;
        public long mByteOffset;
        public ulong mElementCount;
        public uint mFlags;
        public GridBlindDataSemantic mSemantic;
        public GridBlindDataClass mDataClass;
        public GridType mDataType;
        public fixed byte mName[MaxNameSize];
    }

    // GridData: layout of the header of a grid (sizeof == 672)
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GridData
    {
        public const int MaxNameSize = 256;
        public ulong mMagic;
        public ulong mChecksum;
        public Version mVersion;
        public uint mFlags;
        public uint mGridIndex;
        public uint mGridCount;
        public ulong mGridSize;
        public fixed byte mGridName[MaxNameSize];
        public Map mMap;
        public BBox<double> mWorldBBox;
        public Vec3<double> mVoxelSize;
        public GridClass mGridClass;
        public GridType mGridType;
        public long mBlindMetadataOffset;
        public uint mBlindMetadataCount;
        // (padded to 672 bytes)
        public const int SIZEOF = 672;

        public GridBlindMetaData* blindMetaData(uint n)
        {
            fixed (GridData* t = &this)
                return (GridBlindMetaData*)((byte*)t + mBlindMetadataOffset) + n;
        }
    }

    // TreeData (sizeof == 64)
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TreeData
    {
        public fixed ulong mNodeOffset[4];
        public fixed uint mNodeCount[3];
        public fixed uint mTileCount[3];
        public ulong mVoxelCount;
        public const int SIZEOF = 64;
    }

    // Layout<BuildT>: byte offsets of the members of the node data structures, which depend on
    // the size of the value type (the C++ derives them from the struct definitions).
    public static unsafe class Layout<BuildT> where BuildT : unmanaged, INumber<BuildT>
    {
        public static readonly int V = sizeof(BuildT);     // sizeof(ValueType)
        public static readonly int S = V == 8 ? 8 : 4;     // sizeof(FloatType): FloatTraits
        static int Al(int off, int a) { return (off + a - 1) / a * a; }

        // Root
        public static readonly int rootBackground = Al(28, V);
        public static readonly int rootMinimum = rootBackground + V;
        public static readonly int rootMaximum = rootMinimum + V;
        public static readonly int rootAverage = Al(rootMaximum + V, S);
        public static readonly int rootStdDevi = rootAverage + S;
        public static readonly int rootSize = Al(rootStdDevi + S, 32);
        // Root tile: KeyT key; int64 child; uint32 state; ValueT value
        public static readonly int tileState = 16;
        public static readonly int tileValue = Al(20, V);
        public static readonly int tileSize = Al(tileValue + V, 32);

        // Internal node with table of 8-byte entries
        public static int internalMaskBytes(int log2dim) { return (1 << (3 * log2dim)) / 8; }
        public static int internalMinimum(int log2dim) { return 32 + 2 * internalMaskBytes(log2dim); }
        public static int internalMaximum(int log2dim) { return internalMinimum(log2dim) + V; }
        public static int internalAverage(int log2dim) { return Al(internalMaximum(log2dim) + V, S); }
        public static int internalStdDevi(int log2dim) { return internalAverage(log2dim) + S; }
        public static int internalTable(int log2dim) { return Al(internalStdDevi(log2dim) + S, 32); }
        public static int internalSize(int log2dim) { return Al(internalTable(log2dim) + (1 << (3 * log2dim)) * 8, 32); }

        // Leaf node (LOG2DIM = 3)
        public const int leafMask = 16;
        public static readonly int leafMinimum = leafMask + 64;
        public static readonly int leafMaximum = leafMinimum + V;
        public static readonly int leafAverage = Al(leafMaximum + V, S);
        public static readonly int leafStdDevi = leafAverage + S;
        public static readonly int leafValues = Al(leafStdDevi + S, 32);
        public static readonly int leafSize = Al(leafValues + 512 * V, 32);
    }

    // Interface implemented by things that can supply voxel values (a tree or a ReadAccessor)
    public interface IVoxelAccess<BuildT> where BuildT : unmanaged, INumber<BuildT>
    {
        BuildT getValue(Coord ijk);
    }

    // LeafNode<BuildT> (NanoLeaf<BuildT>): Coord, Mask, LOG2DIM = 3
    public unsafe struct NanoLeaf<BuildT> where BuildT : unmanaged, INumber<BuildT>
    {
        public byte* p;
        public NanoLeaf(byte* p) { this.p = p; }
        public bool IsNull { get { return p == null; } }

        public const uint LOG2DIM = 3;
        public const uint TOTAL = LOG2DIM;
        public const uint DIM = 1u << (int)TOTAL;
        public const uint SIZE = 1u << (int)(3 * LOG2DIM);
        public const uint MASK = (1u << (int)LOG2DIM) - 1u;
        public const uint LEVEL = 0;

        public static uint dim() { return 1u << (int)LOG2DIM; }

        public Mask valueMask() { return new Mask(p + Layout<BuildT>.leafMask, LOG2DIM); }
        public BuildT minimum() { return *(BuildT*)(p + Layout<BuildT>.leafMinimum); }
        public BuildT maximum() { return *(BuildT*)(p + Layout<BuildT>.leafMaximum); }
        public double average() { return Layout<BuildT>.S == 8 ? *(double*)(p + Layout<BuildT>.leafAverage) : *(float*)(p + Layout<BuildT>.leafAverage); }
        public double stdDeviation() { return Layout<BuildT>.S == 8 ? *(double*)(p + Layout<BuildT>.leafStdDevi) : *(float*)(p + Layout<BuildT>.leafStdDevi); }
        public double variance() { double d = stdDeviation(); return d * d; }
        public byte flags() { return p[15]; }

        public Coord origin()
        {
            Coord c = *(Coord*)p;
            return c & ~MASK;
        }

        public CoordBBox bbox()
        {
            Coord bmin = *(Coord*)p;
            CoordBBox bbox = new CoordBBox(bmin, bmin);
            if (isActive())
            {
                Coord mx = bbox.max();
                mx[0] += p[12];
                mx[1] += p[13];
                mx[2] += p[14];
                bbox[1] = mx;
            }
            else
            {
                bbox = CoordBBox.Default();  // invalid
            }
            return bbox;
        }

        public static uint voxelCount() { return 1u << (int)(3 * LOG2DIM); }

        public BuildT getValue(uint offset) { return ((BuildT*)(p + Layout<BuildT>.leafValues))[offset]; }
        public BuildT getValue(Coord ijk) { return getValue(CoordToOffset(ijk)); }
        public bool isActive(Coord ijk) { return valueMask().isOn(CoordToOffset(ijk)); }
        public bool isActive(uint n) { return valueMask().isOn(n); }
        public bool isActive() { return (flags() & 2) != 0; }
        public bool probeValue(Coord ijk, out BuildT v)
        {
            uint n = CoordToOffset(ijk);
            v = getValue(n);
            return valueMask().isOn(n);
        }
        public NanoLeaf<BuildT> probeLeaf(Coord ijk) { return this; }

        public static uint CoordToOffset(Coord ijk)
        {
            return (((uint)ijk[0] & MASK) << (int)(2 * LOG2DIM)) | (((uint)ijk[1] & MASK) << (int)LOG2DIM) | ((uint)ijk[2] & MASK);
        }
        public static Coord OffsetToLocalCoord(uint n)
        {
            uint m = n & (uint)((1 << (int)(2 * LOG2DIM)) - 1);
            return new Coord((int)(n >> (int)(2 * LOG2DIM)), (int)(m >> (int)LOG2DIM), (int)(m & MASK));
        }
        public Coord offsetToGlobalCoord(uint n) { return OffsetToLocalCoord(n) + origin(); }

        // Private read access method used by the ReadAccessor
        internal BuildT getValueAndCache(Coord ijk, ReadAccessor<BuildT> acc) { return getValue(ijk); }
        internal bool isActiveAndCache(Coord ijk, ReadAccessor<BuildT> acc) { return isActive(ijk); }
        internal bool probeValueAndCache(Coord ijk, out BuildT v, ReadAccessor<BuildT> acc) { return probeValue(ijk, out v); }
        internal NanoLeaf<BuildT> probeLeafAndCache(Coord ijk, ReadAccessor<BuildT> acc) { return this; }
    }

    // InternalNode<ChildT, Log2Dim>: NanoLower (Log2Dim 4, children are leaves) and
    // NanoUpper (Log2Dim 5, children are lower nodes). The child is accessed through the
    // generic LEVEL; level 1 children are NanoLeaf, level 2 children are NanoLower.
    public unsafe struct NanoLower<BuildT> where BuildT : unmanaged, INumber<BuildT>
    {
        public byte* p;
        public NanoLower(byte* p) { this.p = p; }
        public bool IsNull { get { return p == null; } }

        public const uint LOG2DIM = 4;
        public const uint TOTAL = LOG2DIM + NanoLeaf<BuildT>.TOTAL;
        public const uint DIM = 1u << (int)TOTAL;
        public const uint SIZE = 1u << (int)(3 * LOG2DIM);
        public const uint MASK = (1u << (int)TOTAL) - 1u;
        public const uint LEVEL = 1;

        static int T { get { return Layout<BuildT>.internalTable((int)LOG2DIM); } }

        public static uint dim() { return 1u << (int)TOTAL; }
        public Mask valueMask() { return new Mask(p + 32, LOG2DIM); }
        public Mask childMask() { return new Mask(p + 32 + Layout<BuildT>.internalMaskBytes((int)LOG2DIM), LOG2DIM); }
        public CoordBBox bbox() { return *(CoordBBox*)p; }
        public Coord origin() { return bbox().min() & ~MASK; }
        public ulong flags() { return *(ulong*)(p + 24); }
        public BuildT minimum() { return *(BuildT*)(p + Layout<BuildT>.internalMinimum((int)LOG2DIM)); }
        public BuildT maximum() { return *(BuildT*)(p + Layout<BuildT>.internalMaximum((int)LOG2DIM)); }
        public double average() { int o = Layout<BuildT>.internalAverage((int)LOG2DIM); return Layout<BuildT>.S == 8 ? *(double*)(p + o) : *(float*)(p + o); }
        public double stdDeviation() { int o = Layout<BuildT>.internalStdDevi((int)LOG2DIM); return Layout<BuildT>.S == 8 ? *(double*)(p + o) : *(float*)(p + o); }
        public double variance() { double d = stdDeviation(); return d * d; }
        public bool isActive() { return (flags() & 2) != 0; }

        BuildT tableValue(uint n) { return *(BuildT*)(p + T + n * 8); }
        long tableChild(uint n) { return *(long*)(p + T + n * 8); }
        public NanoLeaf<BuildT> getChild(uint n) { return new NanoLeaf<BuildT>(p + tableChild(n)); }

        public static uint CoordToOffset(Coord ijk)
        {
            return ((((uint)ijk[0] & MASK) >> (int)NanoLeaf<BuildT>.TOTAL) << (int)(2 * LOG2DIM)) |
                   ((((uint)ijk[1] & MASK) >> (int)NanoLeaf<BuildT>.TOTAL) << (int)LOG2DIM) |
                   (((uint)ijk[2] & MASK) >> (int)NanoLeaf<BuildT>.TOTAL);
        }
        public static Coord OffsetToLocalCoord(uint n)
        {
            uint m = n & (uint)((1 << (int)(2 * LOG2DIM)) - 1);
            return new Coord((int)(n >> (int)(2 * LOG2DIM)), (int)(m >> (int)LOG2DIM), (int)(m & (uint)((1 << (int)LOG2DIM) - 1)));
        }
        public void localToGlobalCoord(ref Coord ijk)
        {
            ijk = ijk << (int)NanoLeaf<BuildT>.TOTAL;
            ijk = ijk + origin();
        }
        public Coord offsetToGlobalCoord(uint n)
        {
            Coord ijk = OffsetToLocalCoord(n);
            localToGlobalCoord(ref ijk);
            return ijk;
        }

        public BuildT getValue(Coord ijk)
        {
            uint n = CoordToOffset(ijk);
            return childMask().isOn(n) ? getChild(n).getValue(ijk) : tableValue(n);
        }
        public bool isActive(Coord ijk)
        {
            uint n = CoordToOffset(ijk);
            return childMask().isOn(n) ? getChild(n).isActive(ijk) : valueMask().isOn(n);
        }
        public bool probeValue(Coord ijk, out BuildT v)
        {
            uint n = CoordToOffset(ijk);
            if (childMask().isOn(n))
                return getChild(n).probeValue(ijk, out v);
            v = tableValue(n);
            return valueMask().isOn(n);
        }
        public NanoLeaf<BuildT> probeLeaf(Coord ijk)
        {
            uint n = CoordToOffset(ijk);
            if (childMask().isOn(n))
                return getChild(n).probeLeaf(ijk);
            return new NanoLeaf<BuildT>(null);
        }

        internal BuildT getValueAndCache(Coord ijk, ReadAccessor<BuildT> acc)
        {
            uint n = CoordToOffset(ijk);
            if (!childMask().isOn(n))
                return tableValue(n);
            NanoLeaf<BuildT> child = getChild(n);
            acc.insert(ijk, child);
            return child.getValueAndCache(ijk, acc);
        }
        internal bool isActiveAndCache(Coord ijk, ReadAccessor<BuildT> acc)
        {
            uint n = CoordToOffset(ijk);
            if (!childMask().isOn(n))
                return valueMask().isOn(n);
            NanoLeaf<BuildT> child = getChild(n);
            acc.insert(ijk, child);
            return child.isActiveAndCache(ijk, acc);
        }
        internal bool probeValueAndCache(Coord ijk, out BuildT v, ReadAccessor<BuildT> acc)
        {
            uint n = CoordToOffset(ijk);
            if (!childMask().isOn(n))
            {
                v = tableValue(n);
                return valueMask().isOn(n);
            }
            NanoLeaf<BuildT> child = getChild(n);
            acc.insert(ijk, child);
            return child.probeValueAndCache(ijk, out v, acc);
        }
        internal NanoLeaf<BuildT> probeLeafAndCache(Coord ijk, ReadAccessor<BuildT> acc)
        {
            uint n = CoordToOffset(ijk);
            if (!childMask().isOn(n))
                return new NanoLeaf<BuildT>(null);
            NanoLeaf<BuildT> child = getChild(n);
            acc.insert(ijk, child);
            return child.probeLeafAndCache(ijk, acc);
        }
    }

    public unsafe struct NanoUpper<BuildT> where BuildT : unmanaged, INumber<BuildT>
    {
        public byte* p;
        public NanoUpper(byte* p) { this.p = p; }
        public bool IsNull { get { return p == null; } }

        public const uint LOG2DIM = 5;
        public const uint TOTAL = LOG2DIM + NanoLower<BuildT>.TOTAL;
        public const uint DIM = 1u << (int)TOTAL;
        public const uint SIZE = 1u << (int)(3 * LOG2DIM);
        public const uint MASK = (1u << (int)TOTAL) - 1u;
        public const uint LEVEL = 2;

        static int T { get { return Layout<BuildT>.internalTable((int)LOG2DIM); } }

        public static uint dim() { return 1u << (int)TOTAL; }
        public Mask valueMask() { return new Mask(p + 32, LOG2DIM); }
        public Mask childMask() { return new Mask(p + 32 + Layout<BuildT>.internalMaskBytes((int)LOG2DIM), LOG2DIM); }
        public CoordBBox bbox() { return *(CoordBBox*)p; }
        public Coord origin() { return bbox().min() & ~MASK; }
        public ulong flags() { return *(ulong*)(p + 24); }
        public BuildT minimum() { return *(BuildT*)(p + Layout<BuildT>.internalMinimum((int)LOG2DIM)); }
        public BuildT maximum() { return *(BuildT*)(p + Layout<BuildT>.internalMaximum((int)LOG2DIM)); }
        public double average() { int o = Layout<BuildT>.internalAverage((int)LOG2DIM); return Layout<BuildT>.S == 8 ? *(double*)(p + o) : *(float*)(p + o); }
        public double stdDeviation() { int o = Layout<BuildT>.internalStdDevi((int)LOG2DIM); return Layout<BuildT>.S == 8 ? *(double*)(p + o) : *(float*)(p + o); }
        public double variance() { double d = stdDeviation(); return d * d; }
        public bool isActive() { return (flags() & 2) != 0; }

        BuildT tableValue(uint n) { return *(BuildT*)(p + T + n * 8); }
        long tableChild(uint n) { return *(long*)(p + T + n * 8); }
        public NanoLower<BuildT> getChild(uint n) { return new NanoLower<BuildT>(p + tableChild(n)); }

        public static uint CoordToOffset(Coord ijk)
        {
            return ((((uint)ijk[0] & MASK) >> (int)NanoLower<BuildT>.TOTAL) << (int)(2 * LOG2DIM)) |
                   ((((uint)ijk[1] & MASK) >> (int)NanoLower<BuildT>.TOTAL) << (int)LOG2DIM) |
                   (((uint)ijk[2] & MASK) >> (int)NanoLower<BuildT>.TOTAL);
        }
        public static Coord OffsetToLocalCoord(uint n)
        {
            uint m = n & (uint)((1 << (int)(2 * LOG2DIM)) - 1);
            return new Coord((int)(n >> (int)(2 * LOG2DIM)), (int)(m >> (int)LOG2DIM), (int)(m & (uint)((1 << (int)LOG2DIM) - 1)));
        }
        public void localToGlobalCoord(ref Coord ijk)
        {
            ijk = ijk << (int)NanoLower<BuildT>.TOTAL;
            ijk = ijk + origin();
        }
        public Coord offsetToGlobalCoord(uint n)
        {
            Coord ijk = OffsetToLocalCoord(n);
            localToGlobalCoord(ref ijk);
            return ijk;
        }

        public BuildT getValue(Coord ijk)
        {
            uint n = CoordToOffset(ijk);
            return childMask().isOn(n) ? getChild(n).getValue(ijk) : tableValue(n);
        }
        public bool isActive(Coord ijk)
        {
            uint n = CoordToOffset(ijk);
            return childMask().isOn(n) ? getChild(n).isActive(ijk) : valueMask().isOn(n);
        }
        public bool probeValue(Coord ijk, out BuildT v)
        {
            uint n = CoordToOffset(ijk);
            if (childMask().isOn(n))
                return getChild(n).probeValue(ijk, out v);
            v = tableValue(n);
            return valueMask().isOn(n);
        }
        public NanoLeaf<BuildT> probeLeaf(Coord ijk)
        {
            uint n = CoordToOffset(ijk);
            if (childMask().isOn(n))
                return getChild(n).probeLeaf(ijk);
            return new NanoLeaf<BuildT>(null);
        }

        internal BuildT getValueAndCache(Coord ijk, ReadAccessor<BuildT> acc)
        {
            uint n = CoordToOffset(ijk);
            if (!childMask().isOn(n))
                return tableValue(n);
            NanoLower<BuildT> child = getChild(n);
            acc.insert(ijk, child);
            return child.getValueAndCache(ijk, acc);
        }
        internal bool isActiveAndCache(Coord ijk, ReadAccessor<BuildT> acc)
        {
            uint n = CoordToOffset(ijk);
            if (!childMask().isOn(n))
                return valueMask().isOn(n);
            NanoLower<BuildT> child = getChild(n);
            acc.insert(ijk, child);
            return child.isActiveAndCache(ijk, acc);
        }
        internal bool probeValueAndCache(Coord ijk, out BuildT v, ReadAccessor<BuildT> acc)
        {
            uint n = CoordToOffset(ijk);
            if (!childMask().isOn(n))
            {
                v = tableValue(n);
                return valueMask().isOn(n);
            }
            NanoLower<BuildT> child = getChild(n);
            acc.insert(ijk, child);
            return child.probeValueAndCache(ijk, out v, acc);
        }
        internal NanoLeaf<BuildT> probeLeafAndCache(Coord ijk, ReadAccessor<BuildT> acc)
        {
            uint n = CoordToOffset(ijk);
            if (!childMask().isOn(n))
                return new NanoLeaf<BuildT>(null);
            NanoLower<BuildT> child = getChild(n);
            acc.insert(ijk, child);
            return child.probeLeafAndCache(ijk, acc);
        }
    }

    // RootNode<ChildT> (NanoRoot<BuildT>)
    public unsafe struct NanoRoot<BuildT> where BuildT : unmanaged, INumber<BuildT>
    {
        public byte* p;
        public NanoRoot(byte* p) { this.p = p; }
        public bool IsNull { get { return p == null; } }

        public const uint LEVEL = 3;

        // USE_SINGLE_ROOT_KEY
        public static ulong CoordToKey(Coord ijk)
        {
            return ((ulong)((uint)ijk[2] >> (int)NanoUpper<BuildT>.TOTAL)) |
                   ((ulong)((uint)ijk[1] >> (int)NanoUpper<BuildT>.TOTAL) << 21) |
                   ((ulong)((uint)ijk[0] >> (int)NanoUpper<BuildT>.TOTAL) << 42);
        }
        public static Coord KeyToCoord(ulong key)
        {
            const ulong MASK = (1u << 21) - 1;
            return new Coord((int)(((key >> 42) & MASK) << (int)NanoUpper<BuildT>.TOTAL),
                             (int)(((key >> 21) & MASK) << (int)NanoUpper<BuildT>.TOTAL),
                             (int)((key & MASK) << (int)NanoUpper<BuildT>.TOTAL));
        }

        public CoordBBox bbox() { return *(CoordBBox*)p; }
        public uint tileCount() { return *(uint*)(p + 24); }
        public BuildT background() { return *(BuildT*)(p + Layout<BuildT>.rootBackground); }
        public BuildT minimum() { return *(BuildT*)(p + Layout<BuildT>.rootMinimum); }
        public BuildT maximum() { return *(BuildT*)(p + Layout<BuildT>.rootMaximum); }
        public double average() { int o = Layout<BuildT>.rootAverage; return Layout<BuildT>.S == 8 ? *(double*)(p + o) : *(float*)(p + o); }
        public double stdDeviation() { int o = Layout<BuildT>.rootStdDevi; return Layout<BuildT>.S == 8 ? *(double*)(p + o) : *(float*)(p + o); }
        public double variance() { double d = stdDeviation(); return d * d; }
        public bool isEmpty() { return tileCount() == 0; }

        // Tile accessors
        byte* tile(uint n) { return p + Layout<BuildT>.rootSize + (long)n * Layout<BuildT>.tileSize; }
        static ulong tileKey(byte* t) { return *(ulong*)t; }
        static long tileChild(byte* t) { return *(long*)(t + 8); }
        static bool tileIsChild(byte* t) { return tileChild(t) != 0; }
        static uint tileState(byte* t) { return *(uint*)(t + Layout<BuildT>.tileState); }
        static BuildT tileValue(byte* t) { return *(BuildT*)(t + Layout<BuildT>.tileValue); }
        public Coord tileOrigin(uint n) { return KeyToCoord(tileKey(tile(n))); }
        NanoUpper<BuildT> getChild(byte* t) { return new NanoUpper<BuildT>(p + tileChild(t)); }

        // Private method to find a Tile of this root node (linear search)
        byte* findTile(Coord ijk)
        {
            ulong key = CoordToKey(ijk);
            uint n = tileCount();
            for (uint i = 0; i < n; ++i)
            {
                byte* t = tile(i);
                if (tileKey(t) == key) return t;
            }
            return null;
        }

        public BuildT getValue(Coord ijk)
        {
            byte* t = findTile(ijk);
            if (t != null)
                return tileIsChild(t) ? getChild(t).getValue(ijk) : tileValue(t);
            return background();
        }
        public bool isActive(Coord ijk)
        {
            byte* t = findTile(ijk);
            if (t != null)
                return tileIsChild(t) ? getChild(t).isActive(ijk) : tileState(t) != 0;
            return false;
        }
        public bool probeValue(Coord ijk, out BuildT v)
        {
            byte* t = findTile(ijk);
            if (t != null)
            {
                if (tileIsChild(t))
                    return getChild(t).probeValue(ijk, out v);
                v = tileValue(t);
                return tileState(t) != 0;
            }
            v = background();
            return false;
        }
        public NanoLeaf<BuildT> probeLeaf(Coord ijk)
        {
            byte* t = findTile(ijk);
            if (t != null && tileIsChild(t))
                return getChild(t).probeLeaf(ijk);
            return new NanoLeaf<BuildT>(null);
        }

        internal BuildT getValueAndCache(Coord ijk, ReadAccessor<BuildT> acc)
        {
            byte* t = findTile(ijk);
            if (t != null)
            {
                if (tileIsChild(t))
                {
                    NanoUpper<BuildT> child = getChild(t);
                    acc.insert(ijk, child);
                    return child.getValueAndCache(ijk, acc);
                }
                return tileValue(t);
            }
            return background();
        }
        internal bool isActiveAndCache(Coord ijk, ReadAccessor<BuildT> acc)
        {
            byte* t = findTile(ijk);
            if (t != null && tileIsChild(t))
            {
                NanoUpper<BuildT> child = getChild(t);
                acc.insert(ijk, child);
                return child.isActiveAndCache(ijk, acc);
            }
            return false;
        }
        internal bool probeValueAndCache(Coord ijk, out BuildT v, ReadAccessor<BuildT> acc)
        {
            byte* t = findTile(ijk);
            if (t != null)
            {
                if (tileIsChild(t))
                {
                    NanoUpper<BuildT> child = getChild(t);
                    acc.insert(ijk, child);
                    return child.probeValueAndCache(ijk, out v, acc);
                }
                v = tileValue(t);
                return tileState(t) != 0;
            }
            v = background();
            return false;
        }
        internal NanoLeaf<BuildT> probeLeafAndCache(Coord ijk, ReadAccessor<BuildT> acc)
        {
            byte* t = findTile(ijk);
            if (t != null && tileIsChild(t))
            {
                NanoUpper<BuildT> child = getChild(t);
                acc.insert(ijk, child);
                return child.probeLeafAndCache(ijk, acc);
            }
            return new NanoLeaf<BuildT>(null);
        }
    }

    // Tree<RootT> (NanoTree<BuildT>)
    public unsafe struct NanoTree<BuildT> : IVoxelAccess<BuildT> where BuildT : unmanaged, INumber<BuildT>
    {
        public byte* p;
        public NanoTree(byte* p) { this.p = p; }
        public bool IsNull { get { return p == null; } }

        TreeData* data() { return (TreeData*)p; }

        public NanoRoot<BuildT> root() { return new NanoRoot<BuildT>(p + data()->mNodeOffset[3]); }
        public ReadAccessor<BuildT> getAccessor() { return new ReadAccessor<BuildT>(root()); }
        public BuildT getValue(Coord ijk) { return root().getValue(ijk); }
        public bool isActive(Coord ijk) { return root().isActive(ijk); }
        public bool isEmpty() { return root().isEmpty(); }
        public bool probeValue(Coord ijk, out BuildT v) { return root().probeValue(ijk, out v); }
        public BuildT background() { return root().background(); }
        public void extrema(out BuildT min, out BuildT max)
        {
            min = root().minimum();
            max = root().maximum();
        }
        public CoordBBox bbox() { return root().bbox(); }
        public ulong activeVoxelCount() { return data()->mVoxelCount; }
        public uint activeTileCount(uint n) { return data()->mTileCount[n]; }
        public uint nodeCount(int level) { return data()->mNodeCount[level]; }
    }

    // Grid<TreeT> (NanoGrid<BuildT>)
    public unsafe struct NanoGrid<BuildT> where BuildT : unmanaged, INumber<BuildT>
    {
        public byte* p;
        public NanoGrid(byte* p) { this.p = p; }
        public bool IsNull { get { return p == null; } }
        public static implicit operator bool(NanoGrid<BuildT> g) { return g.p != null; }

        public GridData* data() { return (GridData*)p; }

        public Version version() { return data()->mVersion; }
        public static ulong memUsage() { return GridData.SIZEOF; }
        public ulong gridSize() { return data()->mGridSize; }
        public uint gridIndex() { return data()->mGridIndex; }
        public uint gridCount() { return data()->mGridCount; }

        public NanoTree<BuildT> tree() { return new NanoTree<BuildT>(p + GridData.SIZEOF); }
        public ReadAccessor<BuildT> getAccessor() { return new ReadAccessor<BuildT>(tree().root()); }

        public Vec3<double> voxelSize() { return data()->mVoxelSize; }
        public Map* map() { return &data()->mMap; }

        // world to index space transformation
        public Vec3<float> worldToIndex(Vec3<float> xyz) { return data()->mMap.applyInverseMap(xyz); }
        public Vec3<double> worldToIndex(Vec3<double> xyz) { return data()->mMap.applyInverseMap(xyz); }
        public Vec3<float> indexToWorld(Vec3<float> xyz) { return data()->mMap.applyMap(xyz); }
        public Vec3<double> indexToWorld(Vec3<double> xyz) { return data()->mMap.applyMap(xyz); }
        public Vec3<float> indexToWorldDir(Vec3<float> dir) { return data()->mMap.applyJacobian(dir); }
        public Vec3<float> worldToIndexDir(Vec3<float> dir) { return data()->mMap.applyInverseJacobian(dir); }
        public Vec3<double> indexToWorldGrad(Vec3<double> grad) { return data()->mMap.applyIJT(grad); }
        public Vec3<float> worldToIndexF(Vec3<float> xyz) { return data()->mMap.applyInverseMapF(xyz); }
        public Vec3<double> worldToIndexF(Vec3<double> xyz) { return data()->mMap.applyInverseMapF(xyz); }
        public Vec3<float> indexToWorldF(Vec3<float> xyz) { return data()->mMap.applyMapF(xyz); }
        public Vec3<double> indexToWorldF(Vec3<double> xyz) { return data()->mMap.applyMapF(xyz); }
        public Vec3<float> indexToWorldDirF(Vec3<float> dir) { return data()->mMap.applyJacobianF(dir); }
        public Vec3<float> worldToIndexDirF(Vec3<float> dir) { return data()->mMap.applyInverseJacobianF(dir); }
        public Vec3<float> indexToWorldGradF(Vec3<float> grad) { return data()->mMap.applyIJTF(grad); }

        public BBox<double> worldBBox() { return data()->mWorldBBox; }
        public CoordBBox indexBBox() { return tree().bbox(); }
        public ulong activeVoxelCount() { return tree().activeVoxelCount(); }

        public bool isValid() { return data()->mMagic == Defs.NANOVDB_MAGIC_NUMBER; }
        public GridType gridType() { return data()->mGridType; }
        public GridClass gridClass() { return data()->mGridClass; }
        public bool isLevelSet() { return data()->mGridClass == GridClass.LevelSet; }
        public bool isFogVolume() { return data()->mGridClass == GridClass.FogVolume; }
        public bool isStaggered() { return data()->mGridClass == GridClass.Staggered; }
        public bool isPointIndex() { return data()->mGridClass == GridClass.PointIndex; }
        public bool isPointData() { return data()->mGridClass == GridClass.PointData; }
        public bool isMask() { return data()->mGridClass == GridClass.Topology; }
        public bool isUnknown() { return data()->mGridClass == GridClass.Unknown; }
        public bool hasMinMax() { return (data()->mFlags & (uint)GridFlags.HasMinMax) != 0; }
        public bool hasBBox() { return (data()->mFlags & (uint)GridFlags.HasBBox) != 0; }
        public bool hasLongGridName() { return (data()->mFlags & (uint)GridFlags.HasLongGridName) != 0; }
        public bool hasAverage() { return (data()->mFlags & (uint)GridFlags.HasAverage) != 0; }
        public bool hasStdDeviation() { return (data()->mFlags & (uint)GridFlags.HasStdDeviation) != 0; }
        public bool isBreadthFirst() { return (data()->mFlags & (uint)GridFlags.IsBreadthFirst) != 0; }

        // Return the name of this grid
        public string gridName()
        {
            if (hasLongGridName())
            {
                GridBlindMetaData* metaData = data()->blindMetaData(data()->mBlindMetadataCount - 1);  // always the last
                return Marshal.PtrToStringUTF8((IntPtr)((byte*)data() + data()->mBlindMetadataOffset + (long)(data()->mBlindMetadataCount - 1) * sizeof(GridBlindMetaData) + metaData->mByteOffset));
            }
            return shortGridName();
        }
        public string shortGridName() { return Marshal.PtrToStringUTF8((IntPtr)data()->mGridName); }
        public ulong checksum() { return data()->mChecksum; }
        public bool isEmpty() { return tree().isEmpty(); }
        public int blindDataCount() { return (int)data()->mBlindMetadataCount; }
    }

    // ReadAccessor<BuildT, 0, 1, 2> (DefaultReadAccessor<BuildT>): caches the leaf, lower and
    // upper internal node visited by the last access.
    public unsafe sealed class ReadAccessor<BuildT> : IVoxelAccess<BuildT> where BuildT : unmanaged, INumber<BuildT>
    {
        // All member data are mutable to allow for access methods to be const
        Coord[] mKeys = new Coord[3];
        NanoRoot<BuildT> mRoot;
        byte*[] mNode = new byte*[3];

        public const int CacheLevels = 3;

        public ReadAccessor(NanoRoot<BuildT> root)
        {
            mKeys[0] = Coord.max();
            mKeys[1] = Coord.max();
            mKeys[2] = Coord.max();
            mRoot = root;
            mNode[0] = null;
            mNode[1] = null;
            mNode[2] = null;
        }

        public NanoRoot<BuildT> root() { return mRoot; }

        bool isCached(uint MASK, int LEVEL, Coord ijk)
        {
            return ((ijk[0] & (int)~MASK) == mKeys[LEVEL][0]) && ((ijk[1] & (int)~MASK) == mKeys[LEVEL][1]) &&
                   ((ijk[2] & (int)~MASK) == mKeys[LEVEL][2]);
        }

        public BuildT getValue(Coord ijk)
        {
            if (isCached(NanoLeaf<BuildT>.MASK, 0, ijk))
                return new NanoLeaf<BuildT>(mNode[0]).getValue(ijk);
            else if (isCached(NanoLower<BuildT>.MASK, 1, ijk))
                return new NanoLower<BuildT>(mNode[1]).getValueAndCache(ijk, this);
            else if (isCached(NanoUpper<BuildT>.MASK, 2, ijk))
                return new NanoUpper<BuildT>(mNode[2]).getValueAndCache(ijk, this);
            return mRoot.getValueAndCache(ijk, this);
        }

        public bool isActive(Coord ijk)
        {
            if (isCached(NanoLeaf<BuildT>.MASK, 0, ijk))
                return new NanoLeaf<BuildT>(mNode[0]).isActive(ijk);
            else if (isCached(NanoLower<BuildT>.MASK, 1, ijk))
                return new NanoLower<BuildT>(mNode[1]).isActiveAndCache(ijk, this);
            else if (isCached(NanoUpper<BuildT>.MASK, 2, ijk))
                return new NanoUpper<BuildT>(mNode[2]).isActiveAndCache(ijk, this);
            return mRoot.isActiveAndCache(ijk, this);
        }

        public bool probeValue(Coord ijk, out BuildT v)
        {
            if (isCached(NanoLeaf<BuildT>.MASK, 0, ijk))
                return new NanoLeaf<BuildT>(mNode[0]).probeValue(ijk, out v);
            else if (isCached(NanoLower<BuildT>.MASK, 1, ijk))
                return new NanoLower<BuildT>(mNode[1]).probeValueAndCache(ijk, out v, this);
            else if (isCached(NanoUpper<BuildT>.MASK, 2, ijk))
                return new NanoUpper<BuildT>(mNode[2]).probeValueAndCache(ijk, out v, this);
            return mRoot.probeValueAndCache(ijk, out v, this);
        }

        public NanoLeaf<BuildT> probeLeaf(Coord ijk)
        {
            if (isCached(NanoLeaf<BuildT>.MASK, 0, ijk))
                return new NanoLeaf<BuildT>(mNode[0]);
            else if (isCached(NanoLower<BuildT>.MASK, 1, ijk))
                return new NanoLower<BuildT>(mNode[1]).probeLeafAndCache(ijk, this);
            else if (isCached(NanoUpper<BuildT>.MASK, 2, ijk))
                return new NanoUpper<BuildT>(mNode[2]).probeLeafAndCache(ijk, this);
            return mRoot.probeLeafAndCache(ijk, this);
        }

        // Allow nodes to insert themselves into the cache.
        internal void insert(Coord ijk, NanoLeaf<BuildT> node) { mKeys[0] = ijk & ~NanoLeaf<BuildT>.MASK; mNode[0] = node.p; }
        internal void insert(Coord ijk, NanoLower<BuildT> node) { mKeys[1] = ijk & ~NanoLower<BuildT>.MASK; mNode[1] = node.p; }
        internal void insert(Coord ijk, NanoUpper<BuildT> node) { mKeys[2] = ijk & ~NanoUpper<BuildT>.MASK; mNode[2] = node.p; }
    }

    // GridMetaData Class: reinterpret_cast of a grid buffer, valid for all methods that are
    // invariant to the value type (the C++ uses NanoGrid<int>).
    public unsafe struct GridMetaData
    {
        NanoGrid<int> grid;
        public GridMetaData(byte* p) { grid = new NanoGrid<int>(p); }
        public bool IsNull { get { return grid.p == null; } }

        public bool isValid() { return grid.isValid(); }
        public ulong gridSize() { return grid.gridSize(); }
        public uint gridIndex() { return grid.gridIndex(); }
        public uint gridCount() { return grid.gridCount(); }
        public string shortGridName() { return grid.shortGridName(); }
        public GridType gridType() { return grid.gridType(); }
        public GridClass gridClass() { return grid.gridClass(); }
        public bool isLevelSet() { return grid.isLevelSet(); }
        public bool isFogVolume() { return grid.isFogVolume(); }
        public bool isPointIndex() { return grid.isPointIndex(); }
        public bool isPointData() { return grid.isPointData(); }
        public bool isMask() { return grid.isMask(); }
        public bool isStaggered() { return grid.isStaggered(); }
        public bool isUnknown() { return grid.isUnknown(); }
        public Map* map() { return grid.map(); }
        public BBox<double> worldBBox() { return grid.worldBBox(); }
        public CoordBBox indexBBox() { return grid.indexBBox(); }
        public Vec3<double> voxelSize() { return grid.voxelSize(); }
        public int blindDataCount() { return grid.blindDataCount(); }
        public ulong activeVoxelCount() { return grid.activeVoxelCount(); }
        public uint activeTileCount(uint n) { return grid.tree().activeTileCount(n); }
        public uint nodeCount(uint level) { return grid.tree().nodeCount((int)level); }
        public ulong checksum() { return grid.checksum(); }
        public bool isEmpty() { return grid.isEmpty(); }
        public Version version() { return grid.version(); }
    }
}
