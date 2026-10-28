// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/vecmath.h, util/vecmath.cpp
//
// pbrt's Tuple2/Tuple3 CRTP templates are expanded by hand into Vector2f/2i/3f/3i,
// Point2f/2i/3f/3i and Normal3f structs (plus the Interval-valued Point3fi/Vector3fi).

using System;
using System.Runtime.CompilerServices;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        private static bool IsNaN(Interval fi) { return IsNaN((Float)fi); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f Abs(Vector2f t)
        {
            return new Vector2f(std.abs(t.x), std.abs(t.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f Ceil(Vector2f t)
        {
            return new Vector2f(pstd.ceil(t.x), pstd.ceil(t.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f Floor(Vector2f t)
        {
            return new Vector2f(pstd.floor(t.x), pstd.floor(t.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f Lerp(Float t, Vector2f t0, Vector2f t1)
        {
            return (1 - t) * t0 + t * t1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f FMA(Float a, Vector2f b, Vector2f c)
        {
            return new Vector2f(FMA(a, b.x, c.x), FMA(a, b.y, c.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f FMA(Vector2f a, Float b, Vector2f c)
        {
            return FMA(b, a, c);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f Min(Vector2f t0, Vector2f t1)
        {
            return new Vector2f(std.min(t0.x, t1.x), std.min(t0.y, t1.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MinComponentValue(Vector2f t)
        {
            return std.min(t.x, t.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentIndex(Vector2f t)
        {
            return (t.x < t.y) ? 0 : 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f Max(Vector2f t0, Vector2f t1)
        {
            return new Vector2f(std.max(t0.x, t1.x), std.max(t0.y, t1.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MaxComponentValue(Vector2f t)
        {
            return std.max(t.x, t.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentIndex(Vector2f t)
        {
            return (t.x > t.y) ? 0 : 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f Permute(Vector2f t, int p0, int p1)
        {
            return new Vector2f(t[p0], t[p1]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f Permute(Vector2f t, int[] p)
        {
            return new Vector2f(t[p[0]], t[p[1]]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float HProd(Vector2f t)
        {
            return t.x * t.y;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i Abs(Vector2i t)
        {
            return new Vector2i(std.abs(t.x), std.abs(t.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i Min(Vector2i t0, Vector2i t1)
        {
            return new Vector2i(std.min(t0.x, t1.x), std.min(t0.y, t1.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentValue(Vector2i t)
        {
            return std.min(t.x, t.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentIndex(Vector2i t)
        {
            return (t.x < t.y) ? 0 : 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i Max(Vector2i t0, Vector2i t1)
        {
            return new Vector2i(std.max(t0.x, t1.x), std.max(t0.y, t1.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentValue(Vector2i t)
        {
            return std.max(t.x, t.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentIndex(Vector2i t)
        {
            return (t.x > t.y) ? 0 : 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i Permute(Vector2i t, int p0, int p1)
        {
            return new Vector2i(t[p0], t[p1]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i Permute(Vector2i t, int[] p)
        {
            return new Vector2i(t[p[0]], t[p[1]]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HProd(Vector2i t)
        {
            return t.x * t.y;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Abs(Vector3f t)
        {
            return new Vector3f(std.abs(t.x), std.abs(t.y), std.abs(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Ceil(Vector3f t)
        {
            return new Vector3f(pstd.ceil(t.x), pstd.ceil(t.y), pstd.ceil(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Floor(Vector3f t)
        {
            return new Vector3f(pstd.floor(t.x), pstd.floor(t.y), pstd.floor(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Lerp(Float t, Vector3f t0, Vector3f t1)
        {
            return (1 - t) * t0 + t * t1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f FMA(Float a, Vector3f b, Vector3f c)
        {
            return new Vector3f(FMA(a, b.x, c.x), FMA(a, b.y, c.y), FMA(a, b.z, c.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f FMA(Vector3f a, Float b, Vector3f c)
        {
            return FMA(b, a, c);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Min(Vector3f t0, Vector3f t1)
        {
            return new Vector3f(std.min(t0.x, t1.x), std.min(t0.y, t1.y), std.min(t0.z, t1.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MinComponentValue(Vector3f t)
        {
            return std.min(t.x, std.min(t.y, t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentIndex(Vector3f t)
        {
            return (t.x < t.y) ? ((t.x < t.z) ? 0 : 2) : ((t.y < t.z) ? 1 : 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Max(Vector3f t0, Vector3f t1)
        {
            return new Vector3f(std.max(t0.x, t1.x), std.max(t0.y, t1.y), std.max(t0.z, t1.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MaxComponentValue(Vector3f t)
        {
            return std.max(t.x, std.max(t.y, t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentIndex(Vector3f t)
        {
            return (t.x > t.y) ? ((t.x > t.z) ? 0 : 2) : ((t.y > t.z) ? 1 : 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Permute(Vector3f t, int p0, int p1, int p2)
        {
            return new Vector3f(t[p0], t[p1], t[p2]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Permute(Vector3f t, int[] p)
        {
            return new Vector3f(t[p[0]], t[p[1]], t[p[2]]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float HProd(Vector3f t)
        {
            return t.x * t.y * t.z;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i Abs(Vector3i t)
        {
            return new Vector3i(std.abs(t.x), std.abs(t.y), std.abs(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i Min(Vector3i t0, Vector3i t1)
        {
            return new Vector3i(std.min(t0.x, t1.x), std.min(t0.y, t1.y), std.min(t0.z, t1.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentValue(Vector3i t)
        {
            return std.min(t.x, std.min(t.y, t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentIndex(Vector3i t)
        {
            return (t.x < t.y) ? ((t.x < t.z) ? 0 : 2) : ((t.y < t.z) ? 1 : 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i Max(Vector3i t0, Vector3i t1)
        {
            return new Vector3i(std.max(t0.x, t1.x), std.max(t0.y, t1.y), std.max(t0.z, t1.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentValue(Vector3i t)
        {
            return std.max(t.x, std.max(t.y, t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentIndex(Vector3i t)
        {
            return (t.x > t.y) ? ((t.x > t.z) ? 0 : 2) : ((t.y > t.z) ? 1 : 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i Permute(Vector3i t, int p0, int p1, int p2)
        {
            return new Vector3i(t[p0], t[p1], t[p2]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i Permute(Vector3i t, int[] p)
        {
            return new Vector3i(t[p[0]], t[p[1]], t[p[2]]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HProd(Vector3i t)
        {
            return t.x * t.y * t.z;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f Abs(Point2f t)
        {
            return new Point2f(std.abs(t.x), std.abs(t.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f Ceil(Point2f t)
        {
            return new Point2f(pstd.ceil(t.x), pstd.ceil(t.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f Floor(Point2f t)
        {
            return new Point2f(pstd.floor(t.x), pstd.floor(t.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f Lerp(Float t, Point2f t0, Point2f t1)
        {
            return (1 - t) * t0 + t * t1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f FMA(Float a, Point2f b, Point2f c)
        {
            return new Point2f(FMA(a, b.x, c.x), FMA(a, b.y, c.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f FMA(Point2f a, Float b, Point2f c)
        {
            return FMA(b, a, c);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f Min(Point2f t0, Point2f t1)
        {
            return new Point2f(std.min(t0.x, t1.x), std.min(t0.y, t1.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MinComponentValue(Point2f t)
        {
            return std.min(t.x, t.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentIndex(Point2f t)
        {
            return (t.x < t.y) ? 0 : 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f Max(Point2f t0, Point2f t1)
        {
            return new Point2f(std.max(t0.x, t1.x), std.max(t0.y, t1.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MaxComponentValue(Point2f t)
        {
            return std.max(t.x, t.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentIndex(Point2f t)
        {
            return (t.x > t.y) ? 0 : 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f Permute(Point2f t, int p0, int p1)
        {
            return new Point2f(t[p0], t[p1]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f Permute(Point2f t, int[] p)
        {
            return new Point2f(t[p[0]], t[p[1]]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float HProd(Point2f t)
        {
            return t.x * t.y;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i Abs(Point2i t)
        {
            return new Point2i(std.abs(t.x), std.abs(t.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i Min(Point2i t0, Point2i t1)
        {
            return new Point2i(std.min(t0.x, t1.x), std.min(t0.y, t1.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentValue(Point2i t)
        {
            return std.min(t.x, t.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentIndex(Point2i t)
        {
            return (t.x < t.y) ? 0 : 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i Max(Point2i t0, Point2i t1)
        {
            return new Point2i(std.max(t0.x, t1.x), std.max(t0.y, t1.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentValue(Point2i t)
        {
            return std.max(t.x, t.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentIndex(Point2i t)
        {
            return (t.x > t.y) ? 0 : 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i Permute(Point2i t, int p0, int p1)
        {
            return new Point2i(t[p0], t[p1]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i Permute(Point2i t, int[] p)
        {
            return new Point2i(t[p[0]], t[p[1]]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HProd(Point2i t)
        {
            return t.x * t.y;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f Abs(Point3f t)
        {
            return new Point3f(std.abs(t.x), std.abs(t.y), std.abs(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f Ceil(Point3f t)
        {
            return new Point3f(pstd.ceil(t.x), pstd.ceil(t.y), pstd.ceil(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f Floor(Point3f t)
        {
            return new Point3f(pstd.floor(t.x), pstd.floor(t.y), pstd.floor(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f Lerp(Float t, Point3f t0, Point3f t1)
        {
            return (1 - t) * t0 + t * t1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f FMA(Float a, Point3f b, Point3f c)
        {
            return new Point3f(FMA(a, b.x, c.x), FMA(a, b.y, c.y), FMA(a, b.z, c.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f FMA(Point3f a, Float b, Point3f c)
        {
            return FMA(b, a, c);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f Min(Point3f t0, Point3f t1)
        {
            return new Point3f(std.min(t0.x, t1.x), std.min(t0.y, t1.y), std.min(t0.z, t1.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MinComponentValue(Point3f t)
        {
            return std.min(t.x, std.min(t.y, t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentIndex(Point3f t)
        {
            return (t.x < t.y) ? ((t.x < t.z) ? 0 : 2) : ((t.y < t.z) ? 1 : 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f Max(Point3f t0, Point3f t1)
        {
            return new Point3f(std.max(t0.x, t1.x), std.max(t0.y, t1.y), std.max(t0.z, t1.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MaxComponentValue(Point3f t)
        {
            return std.max(t.x, std.max(t.y, t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentIndex(Point3f t)
        {
            return (t.x > t.y) ? ((t.x > t.z) ? 0 : 2) : ((t.y > t.z) ? 1 : 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f Permute(Point3f t, int p0, int p1, int p2)
        {
            return new Point3f(t[p0], t[p1], t[p2]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f Permute(Point3f t, int[] p)
        {
            return new Point3f(t[p[0]], t[p[1]], t[p[2]]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float HProd(Point3f t)
        {
            return t.x * t.y * t.z;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i Abs(Point3i t)
        {
            return new Point3i(std.abs(t.x), std.abs(t.y), std.abs(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i Min(Point3i t0, Point3i t1)
        {
            return new Point3i(std.min(t0.x, t1.x), std.min(t0.y, t1.y), std.min(t0.z, t1.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentValue(Point3i t)
        {
            return std.min(t.x, std.min(t.y, t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentIndex(Point3i t)
        {
            return (t.x < t.y) ? ((t.x < t.z) ? 0 : 2) : ((t.y < t.z) ? 1 : 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i Max(Point3i t0, Point3i t1)
        {
            return new Point3i(std.max(t0.x, t1.x), std.max(t0.y, t1.y), std.max(t0.z, t1.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentValue(Point3i t)
        {
            return std.max(t.x, std.max(t.y, t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentIndex(Point3i t)
        {
            return (t.x > t.y) ? ((t.x > t.z) ? 0 : 2) : ((t.y > t.z) ? 1 : 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i Permute(Point3i t, int p0, int p1, int p2)
        {
            return new Point3i(t[p0], t[p1], t[p2]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i Permute(Point3i t, int[] p)
        {
            return new Point3i(t[p[0]], t[p[1]], t[p[2]]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HProd(Point3i t)
        {
            return t.x * t.y * t.z;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f Abs(Normal3f t)
        {
            return new Normal3f(std.abs(t.x), std.abs(t.y), std.abs(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f Ceil(Normal3f t)
        {
            return new Normal3f(pstd.ceil(t.x), pstd.ceil(t.y), pstd.ceil(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f Floor(Normal3f t)
        {
            return new Normal3f(pstd.floor(t.x), pstd.floor(t.y), pstd.floor(t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f Lerp(Float t, Normal3f t0, Normal3f t1)
        {
            return (1 - t) * t0 + t * t1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f FMA(Float a, Normal3f b, Normal3f c)
        {
            return new Normal3f(FMA(a, b.x, c.x), FMA(a, b.y, c.y), FMA(a, b.z, c.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f FMA(Normal3f a, Float b, Normal3f c)
        {
            return FMA(b, a, c);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f Min(Normal3f t0, Normal3f t1)
        {
            return new Normal3f(std.min(t0.x, t1.x), std.min(t0.y, t1.y), std.min(t0.z, t1.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MinComponentValue(Normal3f t)
        {
            return std.min(t.x, std.min(t.y, t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MinComponentIndex(Normal3f t)
        {
            return (t.x < t.y) ? ((t.x < t.z) ? 0 : 2) : ((t.y < t.z) ? 1 : 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f Max(Normal3f t0, Normal3f t1)
        {
            return new Normal3f(std.max(t0.x, t1.x), std.max(t0.y, t1.y), std.max(t0.z, t1.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MaxComponentValue(Normal3f t)
        {
            return std.max(t.x, std.max(t.y, t.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxComponentIndex(Normal3f t)
        {
            return (t.x > t.y) ? ((t.x > t.z) ? 0 : 2) : ((t.y > t.z) ? 1 : 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f Permute(Normal3f t, int p0, int p1, int p2)
        {
            return new Normal3f(t[p0], t[p1], t[p2]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f Permute(Normal3f t, int[] p)
        {
            return new Normal3f(t[p[0]], t[p[1]], t[p[2]]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float HProd(Normal3f t)
        {
            return t.x * t.y * t.z;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Dot(Vector2f v1, Vector2f v2)
        {
            DCHECK(!v1.HasNaN() && !v2.HasNaN());
            return SumOfProducts(v1.x, v2.x, v1.y, v2.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AbsDot(Vector2f v1, Vector2f v2)
        {
            DCHECK(!v1.HasNaN() && !v2.HasNaN());
            return std.abs(Dot(v1, v2));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float LengthSquared(Vector2f v)
        {
            return Sqr(v.x) + Sqr(v.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Length(Vector2f v)
        {
            return std.sqrt(LengthSquared(v));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f Normalize(Vector2f v)
        {
            return v / Length(v);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Distance(Point2f p1, Point2f p2)
        {
            return Length(p1 - p2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float DistanceSquared(Point2f p1, Point2f p2)
        {
            return LengthSquared(p1 - p2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Distance(Point2i p1, Point2i p2)
        {
            return Length((Vector2f)(p1 - p2));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float DistanceSquared(Point2i p1, Point2i p2)
        {
            return LengthSquared((Vector2f)(p1 - p2));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float LengthSquared(Vector2i v)
        {
            return Sqr((Float)v.x) + Sqr((Float)v.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Length(Vector2i v)
        {
            return std.sqrt(LengthSquared(v));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Dot(Vector2i v1, Vector2i v2)
        {
            return v1.x * v2.x + v1.y * v2.y;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float LengthSquared(Vector3f v)
        {
            return Sqr(v.x) + Sqr(v.y) + Sqr(v.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Length(Vector3f v)
        {
            return std.sqrt(LengthSquared(v));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Normalize(Vector3f v)
        {
            return v / Length(v);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Dot(Vector3f v, Vector3f w)
        {
            DCHECK(!v.HasNaN() && !w.HasNaN());
            return v.x * w.x + v.y * w.y + v.z * w.z;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AngleBetween(Vector3f v1, Vector3f v2)
        {
            if (Dot(v1, v2) < 0)
                return Pi - 2 * SafeASin(Length(v1 + v2) / 2);
            else
                return 2 * SafeASin(Length(v2 - v1) / 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AbsDot(Vector3f v1, Vector3f v2)
        {
            DCHECK(!v1.HasNaN() && !v2.HasNaN());
            return std.abs(Dot(v1, v2));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AngleBetween(Normal3f a, Normal3f b)
        {
            if (Dot(a, b) < 0)
                return Pi - 2 * SafeASin(Length(a + b) / 2);
            else
                return 2 * SafeASin(Length(b - a) / 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f GramSchmidt(Vector3f v, Vector3f w)
        {
            return v - Dot(v, w) * w;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Cross(Vector3f v, Vector3f w)
        {
            DCHECK(!v.HasNaN() && !w.HasNaN());
            return new Vector3f(DifferenceOfProducts(v.y, w.z, v.z, w.y),
                                DifferenceOfProducts(v.z, w.x, v.x, w.z),
                                DifferenceOfProducts(v.x, w.y, v.y, w.x));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Cross(Vector3f v1, Normal3f v2)
        {
            DCHECK(!v1.HasNaN() && !v2.HasNaN());
            return new Vector3f(DifferenceOfProducts(v1.y, v2.z, v1.z, v2.y),
                                DifferenceOfProducts(v1.z, v2.x, v1.x, v2.z),
                                DifferenceOfProducts(v1.x, v2.y, v1.y, v2.x));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f Cross(Normal3f v1, Vector3f v2)
        {
            DCHECK(!v1.HasNaN() && !v2.HasNaN());
            return new Vector3f(DifferenceOfProducts(v1.y, v2.z, v1.z, v2.y),
                                DifferenceOfProducts(v1.z, v2.x, v1.x, v2.z),
                                DifferenceOfProducts(v1.x, v2.y, v1.y, v2.x));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CoordinateSystem(Vector3f v1, Vector3f* v2, Vector3f* v3)
        {
            Float sign = pstd.copysign((Float)1, v1.z);
            Float a = -1 / (sign + v1.z);
            Float b = v1.x * v1.y * a;
            *v2 = new Vector3f(1 + sign * Sqr(v1.x) * a, sign * b, -sign * v1.x);
            *v3 = new Vector3f(b, sign + Sqr(v1.y) * a, -v1.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CoordinateSystem(Normal3f v1, Vector3f* v2, Vector3f* v3)
        {
            Float sign = pstd.copysign((Float)1, v1.z);
            Float a = -1 / (sign + v1.z);
            Float b = v1.x * v1.y * a;
            *v2 = new Vector3f(1 + sign * Sqr(v1.x) * a, sign * b, -sign * v1.x);
            *v3 = new Vector3f(b, sign + Sqr(v1.y) * a, -v1.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Distance(Point3i p1, Point3i p2) { return Length(new Vector3f((Float)(p1.x - p2.x), (Float)(p1.y - p2.y), (Float)(p1.z - p2.z))); }
        public static Float Distance(Point3f p1, Point3f p2)
        {
            return Length(p1 - p2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float DistanceSquared(Point3f p1, Point3f p2)
        {
            return LengthSquared(p1 - p2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float LengthSquared(Normal3f n)
        {
            return Sqr(n.x) + Sqr(n.y) + Sqr(n.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Length(Normal3f n)
        {
            return std.sqrt(LengthSquared(n));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f Normalize(Normal3f n)
        {
            return n / Length(n);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Dot(Normal3f n, Vector3f v)
        {
            DCHECK(!n.HasNaN() && !v.HasNaN());
            return FMA(n.x, v.x, SumOfProducts(n.y, v.y, n.z, v.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Dot(Vector3f v, Normal3f n)
        {
            DCHECK(!v.HasNaN() && !n.HasNaN());
            return FMA(n.x, v.x, SumOfProducts(n.y, v.y, n.z, v.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Dot(Normal3f n1, Normal3f n2)
        {
            DCHECK(!n1.HasNaN() && !n2.HasNaN());
            return FMA(n1.x, n2.x, SumOfProducts(n1.y, n2.y, n1.z, n2.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AbsDot(Normal3f n, Vector3f v)
        {
            DCHECK(!n.HasNaN() && !v.HasNaN());
            return std.abs(Dot(n, v));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AbsDot(Vector3f v, Normal3f n)
        {
            DCHECK(!v.HasNaN() && !n.HasNaN());
            return std.abs(Dot(v, n));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AbsDot(Normal3f n1, Normal3f n2)
        {
            DCHECK(!n1.HasNaN() && !n2.HasNaN());
            return std.abs(Dot(n1, n2));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f FaceForward(Normal3f n, Vector3f v)
        {
            return (Dot(n, v) < 0.0f) ? -n : n;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f FaceForward(Normal3f n, Normal3f n2)
        {
            return (Dot(n, n2) < 0.0f) ? -n : n;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f FaceForward(Vector3f v, Vector3f v2)
        {
            return (Dot(v, v2) < 0.0f) ? -v : v;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f FaceForward(Vector3f v, Normal3f n2)
        {
            return (Dot(v, n2) < 0.0f) ? -v : v;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Interval LengthSquared(Vector3fi v)
        {
            return Sqr(v.x) + Sqr(v.y) + Sqr(v.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Interval Length(Vector3fi v)
        {
            return Sqrt(LengthSquared(v));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi Normalize(Vector3fi v)
        {
            return v / (Float)Length(v);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi Cross(Vector3fi v, Vector3fi w)
        {
            return new Vector3fi(DifferenceOfProducts(v.y, w.z, v.z, w.y),
                                 DifferenceOfProducts(v.z, w.x, v.x, w.z),
                                 DifferenceOfProducts(v.x, w.y, v.y, w.x));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Interval DistanceSquared(Point3fi p1, Point3fi p2)
        {
            return LengthSquared((Vector3fi)(p1 - p2));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Interval Distance(Point3fi p1, Point3fi p2)
        {
            return Length((Vector3fi)(p1 - p2));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Interval Dot(Vector3fi v, Vector3fi w)
        {
            return v.x * w.x + v.y * w.y + v.z * w.z;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Dot(Quaternion q1, Quaternion q2)
        {
            return Dot(q1.v, q2.v) + q1.w * q2.w;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Length(Quaternion q)
        {
            return std.sqrt(Dot(q, q));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Normalize(Quaternion q)
        {
            DCHECK_GT(Length(q), 0);
            return q / Length(q);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AngleBetween(Quaternion q1, Quaternion q2)
        {
            if (Dot(q1, q2) < 0)
                return Pi - 2 * SafeASin(Length(q1 + q2) / 2);
            else
                return 2 * SafeASin(Length(q2 - q1) / 2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Slerp(Float t, Quaternion q1, Quaternion q2)
        {
            Float theta = AngleBetween(q1, q2);
            Float sinThetaOverTheta = SinXOverX(theta);
            return q1 * (1 - t) * SinXOverX((1 - t) * theta) / sinThetaOverTheta +
                   q2 * t * SinXOverX(t * theta) / sinThetaOverTheta;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds2f Union(Bounds2f b, Point2f p)
        {
            Bounds2f ret = new Bounds2f();
            ret.pMin = Min(b.pMin, p);
            ret.pMax = Max(b.pMax, p);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds2f Union(Bounds2f b1, Bounds2f b2)
        {
            Bounds2f ret = new Bounds2f();
            ret.pMin = Min(b1.pMin, b2.pMin);
            ret.pMax = Max(b1.pMax, b2.pMax);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds2f Intersect(Bounds2f b1, Bounds2f b2)
        {
            Bounds2f b = new Bounds2f();
            b.pMin = Max(b1.pMin, b2.pMin);
            b.pMax = Min(b1.pMax, b2.pMax);
            return b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Overlaps(Bounds2f ba, Bounds2f bb)
        {
            bool x = (ba.pMax.x >= bb.pMin.x) && (ba.pMin.x <= bb.pMax.x);
            bool y = (ba.pMax.y >= bb.pMin.y) && (ba.pMin.y <= bb.pMax.y);
            return (x && y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Inside(Point2f pt, Bounds2f b)
        {
            return (pt.x >= b.pMin.x && pt.x <= b.pMax.x && pt.y >= b.pMin.y && pt.y <= b.pMax.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Inside(Bounds2f ba, Bounds2f bb)
        {
            return (ba.pMin.x >= bb.pMin.x && ba.pMax.x <= bb.pMax.x && ba.pMin.y >= bb.pMin.y &&
                    ba.pMax.y <= bb.pMax.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InsideExclusive(Point2f pt, Bounds2f b)
        {
            return (pt.x >= b.pMin.x && pt.x < b.pMax.x && pt.y >= b.pMin.y && pt.y < b.pMax.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds2f Expand(Bounds2f b, Float delta)
        {
            Bounds2f ret = new Bounds2f();
            ret.pMin = b.pMin - new Vector2f(delta, delta);
            ret.pMax = b.pMax + new Vector2f(delta, delta);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds2i Union(Bounds2i b, Point2i p)
        {
            Bounds2i ret = new Bounds2i();
            ret.pMin = Min(b.pMin, p);
            ret.pMax = Max(b.pMax, p);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds2i Union(Bounds2i b1, Bounds2i b2)
        {
            Bounds2i ret = new Bounds2i();
            ret.pMin = Min(b1.pMin, b2.pMin);
            ret.pMax = Max(b1.pMax, b2.pMax);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds2i Intersect(Bounds2i b1, Bounds2i b2)
        {
            Bounds2i b = new Bounds2i();
            b.pMin = Max(b1.pMin, b2.pMin);
            b.pMax = Min(b1.pMax, b2.pMax);
            return b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Overlaps(Bounds2i ba, Bounds2i bb)
        {
            bool x = (ba.pMax.x >= bb.pMin.x) && (ba.pMin.x <= bb.pMax.x);
            bool y = (ba.pMax.y >= bb.pMin.y) && (ba.pMin.y <= bb.pMax.y);
            return (x && y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Inside(Point2i pt, Bounds2i b)
        {
            return (pt.x >= b.pMin.x && pt.x <= b.pMax.x && pt.y >= b.pMin.y && pt.y <= b.pMax.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Inside(Bounds2i ba, Bounds2i bb)
        {
            return (ba.pMin.x >= bb.pMin.x && ba.pMax.x <= bb.pMax.x && ba.pMin.y >= bb.pMin.y &&
                    ba.pMax.y <= bb.pMax.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InsideExclusive(Point2i pt, Bounds2i b)
        {
            return (pt.x >= b.pMin.x && pt.x < b.pMax.x && pt.y >= b.pMin.y && pt.y < b.pMax.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds2i Expand(Bounds2i b, int delta)
        {
            Bounds2i ret = new Bounds2i();
            ret.pMin = b.pMin - new Vector2i(delta, delta);
            ret.pMax = b.pMax + new Vector2i(delta, delta);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds3f Union(Bounds3f b, Point3f p)
        {
            Bounds3f ret = new Bounds3f();
            ret.pMin = Min(b.pMin, p);
            ret.pMax = Max(b.pMax, p);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds3f Union(Bounds3f b1, Bounds3f b2)
        {
            Bounds3f ret = new Bounds3f();
            ret.pMin = Min(b1.pMin, b2.pMin);
            ret.pMax = Max(b1.pMax, b2.pMax);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds3f Intersect(Bounds3f b1, Bounds3f b2)
        {
            Bounds3f b = new Bounds3f();
            b.pMin = Max(b1.pMin, b2.pMin);
            b.pMax = Min(b1.pMax, b2.pMax);
            return b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Overlaps(Bounds3f b1, Bounds3f b2)
        {
            bool x = (b1.pMax.x >= b2.pMin.x) && (b1.pMin.x <= b2.pMax.x);
            bool y = (b1.pMax.y >= b2.pMin.y) && (b1.pMin.y <= b2.pMax.y);
            bool z = (b1.pMax.z >= b2.pMin.z) && (b1.pMin.z <= b2.pMax.z);
            return (x && y && z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Inside(Point3f p, Bounds3f b)
        {
            return (p.x >= b.pMin.x && p.x <= b.pMax.x && p.y >= b.pMin.y && p.y <= b.pMax.y &&
                    p.z >= b.pMin.z && p.z <= b.pMax.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InsideExclusive(Point3f p, Bounds3f b)
        {
            return (p.x >= b.pMin.x && p.x < b.pMax.x && p.y >= b.pMin.y && p.y < b.pMax.y &&
                    p.z >= b.pMin.z && p.z < b.pMax.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds3f Expand(Bounds3f b, Float delta)
        {
            Bounds3f ret = new Bounds3f();
            ret.pMin = b.pMin - new Vector3f(delta, delta, delta);
            ret.pMax = b.pMax + new Vector3f(delta, delta, delta);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds3i Union(Bounds3i b, Point3i p)
        {
            Bounds3i ret = new Bounds3i();
            ret.pMin = Min(b.pMin, p);
            ret.pMax = Max(b.pMax, p);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds3i Union(Bounds3i b1, Bounds3i b2)
        {
            Bounds3i ret = new Bounds3i();
            ret.pMin = Min(b1.pMin, b2.pMin);
            ret.pMax = Max(b1.pMax, b2.pMax);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds3i Intersect(Bounds3i b1, Bounds3i b2)
        {
            Bounds3i b = new Bounds3i();
            b.pMin = Max(b1.pMin, b2.pMin);
            b.pMax = Min(b1.pMax, b2.pMax);
            return b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Overlaps(Bounds3i b1, Bounds3i b2)
        {
            bool x = (b1.pMax.x >= b2.pMin.x) && (b1.pMin.x <= b2.pMax.x);
            bool y = (b1.pMax.y >= b2.pMin.y) && (b1.pMin.y <= b2.pMax.y);
            bool z = (b1.pMax.z >= b2.pMin.z) && (b1.pMin.z <= b2.pMax.z);
            return (x && y && z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Inside(Point3i p, Bounds3i b)
        {
            return (p.x >= b.pMin.x && p.x <= b.pMax.x && p.y >= b.pMin.y && p.y <= b.pMax.y &&
                    p.z >= b.pMin.z && p.z <= b.pMax.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InsideExclusive(Point3i p, Bounds3i b)
        {
            return (p.x >= b.pMin.x && p.x < b.pMax.x && p.y >= b.pMin.y && p.y < b.pMax.y &&
                    p.z >= b.pMin.z && p.z < b.pMax.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds3i Expand(Bounds3i b, int delta)
        {
            Bounds3i ret = new Bounds3i();
            ret.pMin = b.pMin - new Vector3i(delta, delta, delta);
            ret.pMax = b.pMax + new Vector3i(delta, delta, delta);
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float DistanceSquared(Point3f p, Bounds3f b)
        {
            Float dx = std.max<Float>(std.max<Float>(0, b.pMin.x - p.x), p.x - b.pMax.x);
            Float dy = std.max<Float>(std.max<Float>(0, b.pMin.y - p.y), p.y - b.pMax.y);
            Float dz = std.max<Float>(std.max<Float>(0, b.pMin.z - p.z), p.z - b.pMax.z);
            return Sqr(dx) + Sqr(dy) + Sqr(dz);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Distance(Point3f p, Bounds3f b)
        {
            Float dist2 = DistanceSquared(p, b);
            return std.sqrt(dist2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds2iIterator begin(Bounds2i b)
        {
            return new Bounds2iIterator(b, b.pMin);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds2iIterator end(Bounds2i b)
        {
            Point2i pEnd = new Point2i(b.pMin.x, b.pMax.y);
            // Normalize pEnd for empty bounds
            if (b.pMin.x >= b.pMax.x || b.pMin.y >= b.pMax.y)
                pEnd = b.pMin;
            return new Bounds2iIterator(b, pEnd);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SphericalTriangleArea(Vector3f a, Vector3f b, Vector3f c)
        {
            return std.abs(2 * std.atan2(Dot(a, Cross(b, c)), 1 + Dot(a, b) + Dot(a, c) + Dot(b, c)));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SphericalQuadArea(Vector3f a, Vector3f b, Vector3f c, Vector3f d)
        {
            Vector3f axb = Cross(a, b), bxc = Cross(b, c);
            Vector3f cxd = Cross(c, d), dxa = Cross(d, a);
            if (LengthSquared(axb) == 0 || LengthSquared(bxc) == 0 || LengthSquared(cxd) == 0 ||
                LengthSquared(dxa) == 0)
                return 0;
            axb = Normalize(axb);
            bxc = Normalize(bxc);
            cxd = Normalize(cxd);
            dxa = Normalize(dxa);
            
            Float alpha = AngleBetween(dxa, -axb);
            Float beta = AngleBetween(axb, -bxc);
            Float gamma = AngleBetween(bxc, -cxd);
            Float delta = AngleBetween(cxd, -dxa);
            
            return std.abs(alpha + beta + gamma + delta - 2 * Pi);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f SphericalDirection(Float sinTheta, Float cosTheta, Float phi)
        {
            DCHECK(sinTheta >= -1.0001 && sinTheta <= 1.0001);
            DCHECK(cosTheta >= -1.0001 && cosTheta <= 1.0001);
            return new Vector3f(Clamp(sinTheta, -1, 1) * std.cos(phi), Clamp(sinTheta, -1, 1) * std.sin(phi),
                                Clamp(cosTheta, -1, 1));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SphericalTheta(Vector3f v)
        {
            return SafeACos(v.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SphericalPhi(Vector3f v)
        {
            Float p = std.atan2(v.y, v.x);
            return (p < 0) ? (p + 2 * Pi) : p;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float CosTheta(Vector3f w)
        {
            return w.z;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Cos2Theta(Vector3f w)
        {
            return Sqr(w.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AbsCosTheta(Vector3f w)
        {
            return std.abs(w.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Sin2Theta(Vector3f w)
        {
            return std.max<Float>(0, 1 - Cos2Theta(w));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SinTheta(Vector3f w)
        {
            return std.sqrt(Sin2Theta(w));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float TanTheta(Vector3f w)
        {
            return SinTheta(w) / CosTheta(w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Tan2Theta(Vector3f w)
        {
            return Sin2Theta(w) / Cos2Theta(w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float CosPhi(Vector3f w)
        {
            Float sinTheta = SinTheta(w);
            return (sinTheta == 0) ? 1 : Clamp(w.x / sinTheta, -1, 1);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SinPhi(Vector3f w)
        {
            Float sinTheta = SinTheta(w);
            return (sinTheta == 0) ? 0 : Clamp(w.y / sinTheta, -1, 1);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float CosDPhi(Vector3f wa, Vector3f wb)
        {
            Float waxy = Sqr(wa.x) + Sqr(wa.y), wbxy = Sqr(wb.x) + Sqr(wb.y);
            if (waxy == 0 || wbxy == 0)
                return 1;
            return Clamp((wa.x * wb.x + wa.y * wb.y) / std.sqrt(waxy * wbxy), -1, 1);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SameHemisphere(Vector3f w, Vector3f wp)
        {
            return w.z * wp.z > 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SameHemisphere(Vector3f w, Normal3f wp)
        {
            return w.z * wp.z > 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f InvertBilinear(Point2f p, pstd.span<Point2f> vert)
        {
            // Find bilinear patch corners
            Point2f a = vert[0], b = vert[1], c = vert[3], d = vert[2];
            Vector2f e = b - a, f = d - a, g = (a - b) + (c - d), h = p - a;
            
            Func<Vector2f, Vector2f, Float> cross2d = (Vector2f aa, Vector2f bb) => DifferenceOfProducts(aa.x, bb.y, aa.y, bb.x);
            
            Float k2 = cross2d(g, f);
            Float k1 = cross2d(e, f) + cross2d(h, g);
            Float k0 = cross2d(h, e);
            
            // Compute $v$ from quadratic... (see pbrt-v4 vecmath.h)
            // If edges are parallel, this is a linear equation
            if (std.abs(k2) < 0.001f)
            {
                if (std.abs(e.x * k1 - g.x * k0) < 1e-5f)
                    return new Point2f((h.y * k1 + f.y * k0) / (e.y * k1 - g.y * k0), -k0 / k1);
                else
                    return new Point2f((h.x * k1 + f.x * k0) / (e.x * k1 - g.x * k0), -k0 / k1);
            }
            
            Float v0, v1;
            if (!Quadratic(k2, k1, k0, &v0, &v1))
                return new Point2f(0, 0);
            
            Float u = (h.x - f.x * v0) / (e.x + g.x * v0);
            if (u < 0 || u > 1 || v0 < 0 || v0 > 1)
                return new Point2f((h.x - f.x * v1) / (e.x + g.x * v1), v1);
            return new Point2f(u, v0);
        }
        public static bool Inside(DirectionCone d, Vector3f w)
        {
            return !d.IsEmpty() && Dot(d.w, Normalize(w)) >= d.cosTheta;
        }

        public static DirectionCone BoundSubtendedDirections(Bounds3f b, Point3f p)
        {
            // Compute bounding sphere for _b_ and check if _p_ is inside
            Float radius;
            Point3f pCenter;
            b.BoundingSphere(&pCenter, &radius);
            if (DistanceSquared(p, pCenter) < Sqr(radius))
                return DirectionCone.EntireSphere();

            // Compute and return _DirectionCone_ for bounding sphere
            Vector3f w = Normalize(pCenter - p);
            Float sin2ThetaMax = Sqr(radius) / DistanceSquared(pCenter, p);
            Float cosThetaMax = SafeSqrt(1 - sin2ThetaMax);
            return new DirectionCone(w, cosThetaMax);
        }

        public static DirectionCone Union(DirectionCone a, DirectionCone b)
        {
            // Handle the cases where one or both cones are empty
            if (a.IsEmpty())
                return b;
            if (b.IsEmpty())
                return a;

            // Handle the cases where one cone is inside the other
            Float theta_a = SafeACos(a.cosTheta), theta_b = SafeACos(b.cosTheta);
            Float theta_d = AngleBetween(a.w, b.w);
            if (std.min(theta_d + theta_b, Pi) <= theta_a)
                return a;
            if (std.min(theta_d + theta_a, Pi) <= theta_b)
                return b;

            // Compute the spread angle of the merged cone, $\theta_o$
            Float theta_o = (theta_a + theta_d + theta_b) / 2;
            if (theta_o >= Pi)
                return DirectionCone.EntireSphere();

            // Find the merged cone's axis and return cone union
            Float theta_r = theta_o - theta_a;
            Vector3f wr = Cross(a.w, b.w);
            if (LengthSquared(wr) == 0)
                return DirectionCone.EntireSphere();
            Vector3f w = Rotate(Degrees(theta_r), wr).Apply(a.w);
            return new DirectionCone(w, std.cos(theta_o));
        }
    }


    // Vector2f Definition
    public unsafe struct Vector2f : IEquatable<Vector2f>
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple2<Vector2, Float>
        // ═══════════════════════════════════════════════════════════════════
        
        public const int nDimensions = 2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2f(Float x, Float y)
        {
            this.x = x;
            this.y = y;
            DCHECK(!HasNaN());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasNaN()
        {
            return IsNaN(x) || IsNaN(y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator +(Vector2f v1, Vector2f v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector2f(v1.x + v2.x, v1.y + v2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator +(Vector2f v1, Vector2i v2)
        {
            return new Vector2f(v1.x + v2.x, v1.y + v2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector2f v)
        {
            DCHECK(!v.HasNaN());
            x += v.x;
            y += v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector2i v)
        {
            x += v.x;
            y += v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator -(Vector2f v1, Vector2f v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector2f(v1.x - v2.x, v1.y - v2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator -(Vector2f v1, Vector2i v2)
        {
            return new Vector2f(v1.x - v2.x, v1.y - v2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector2f v)
        {
            DCHECK(!v.HasNaN());
            x -= v.x;
            y -= v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector2i v)
        {
            x -= v.x;
            y -= v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector2f v1, Vector2f v2)
        {
            return v1.x == v2.x && v1.y == v2.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vector2f v1, Vector2f v2)
        {
            return v1.x != v2.x || v1.y != v2.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator *(Vector2f v, Float s)
        {
            return new Vector2f(s * v.x, s * v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator *(Vector2f v, int s)
        {
            return new Vector2f(s * v.x, s * v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Float s)
        {
            DCHECK(!IsNaN(s));
            x *= s;
            y *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator /(Vector2f v, Float d)
        {
            DCHECK(d != 0 && !IsNaN(d));
            return new Vector2f(v.x / d, v.y / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator /(Vector2f v, int d)
        {
            DCHECK_NE(d, 0);
            return new Vector2f(v.x / d, v.y / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(Float d)
        {
            DCHECK_NE(d, 0);
            DCHECK(!IsNaN(d));
            x /= d;
            y /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator -(Vector2f v)
        {
            return new Vector2f(-v.x, -v.y);
        }

        public unsafe ref Float this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 1);
                return ref Unsafe.Add(
                    ref Unsafe.As<Vector2f, Float>(ref this), i);
            }
        }

        public override string ToString()
        {
            return $"[ {x}, {y} ]";
        }

        public bool Equals(Vector2f b) { return this == b; }
        public override bool Equals(object o) { return o is Vector2f b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(x, y); }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Vector2<Float>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2f(Point2f p)
        {
            x = p.x;
            y = p.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector2f(Point2f p)
        {
            return new Vector2f(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2f(Point2i p)
        {
            x = p.x;
            y = p.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector2f(Point2i p)
        {
            return new Vector2f(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2f(Vector2i v)
        {
            x = v.x;
            y = v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector2f(Vector2i v)
        {
            return new Vector2f(v);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple2)
        // ═══════════════════════════════════════════════════════════════════

        public Float x, y;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple2)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator *(Float s, Vector2f v)
        {
            return v * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator *(int s, Vector2f v)
        {
            return v * s;
        }
    }

    // Vector2i Definition
    public unsafe struct Vector2i : IEquatable<Vector2i>
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple2<Vector2, int>
        // ═══════════════════════════════════════════════════════════════════
        
        public const int nDimensions = 2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2i(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        //public bool HasNaN();  // int types don't have HasNaN

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i operator +(Vector2i v1, Vector2i v2)
        {
            return new Vector2i(v1.x + v2.x, v1.y + v2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator +(Vector2i v1, Vector2f v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector2f(v1.x + v2.x, v1.y + v2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector2i v)
        {
            x += v.x;
            y += v.y;
        }

        //public void operator +=(Vector2f v);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i operator -(Vector2i v1, Vector2i v2)
        {
            return new Vector2i(v1.x - v2.x, v1.y - v2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator -(Vector2i v1, Vector2f v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector2f(v1.x - v2.x, v1.y - v2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector2i v)
        {
            x -= v.x;
            y -= v.y;
        }

        //public void operator -=(Vector2f v);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector2i v1, Vector2i v2)
        {
            return v1.x == v2.x && v1.y == v2.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vector2i v1, Vector2i v2)
        {
            return v1.x != v2.x || v1.y != v2.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i operator *(Vector2i v, int s)
        {
            return new Vector2i(s * v.x, s * v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator *(Vector2i v, Float s)
        {
            return new Vector2f(s * v.x, s * v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
        }

        //public void operator *=(Float s);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i operator /(Vector2i v, int d)
        {
            DCHECK_NE(d, 0);
            return new Vector2i(v.x / d, v.y / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator /(Vector2i v, Float d)
        {
            DCHECK(d != 0 && !IsNaN(d));
            return new Vector2f(v.x / d, v.y / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
        }

        //public void operator /=(Float d);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i operator -(Vector2i v)
        {
            return new Vector2i(-v.x, -v.y);
        }

        public unsafe ref int this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 1);
                return ref Unsafe.Add(
                    ref Unsafe.As<Vector2i, int>(ref this), i);
            }
        }

        public override string ToString()
        {
            return $"[ {x}, {y} ]";
        }

        public bool Equals(Vector2i b) { return this == b; }
        public override bool Equals(object o) { return o is Vector2i b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(x, y); }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Vector2<int>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2i(Point2i p)
        {
            x = p.x;
            y = p.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector2i(Point2i p)
        {
            return new Vector2i(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2i(Point2f p)
        {
            x = (int)p.x;
            y = (int)p.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector2i(Point2f p)
        {
            return new Vector2i(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2i(Vector2f v)
        {
            x = (int)v.x;
            y = (int)v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector2i(Vector2f v)
        {
            return new Vector2i(v);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple2)
        // ═══════════════════════════════════════════════════════════════════

        public int x, y;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple2)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i operator *(int s, Vector2i v)
        {
            return v * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator *(Float s, Vector2i v)
        {
            return v * s;
        }
    }

    // Vector3f Definition
    public unsafe struct Vector3f : IEquatable<Vector3f>
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple3<Vector3, Float>
        // ═══════════════════════════════════════════════════════════════════
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3f(Float x, Float y, Float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            DCHECK(!HasNaN());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasNaN()
        {
            return IsNaN(x) || IsNaN(y) || IsNaN(z);
        }

        public unsafe ref Float this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 2);
                return ref Unsafe.Add(
                    ref Unsafe.As<Vector3f, Float>(ref this), i);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator +(Vector3f v1, Vector3f v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector3f(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator +(Vector3f v1, Vector3i v2)
        {
            return new Vector3f(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator +(Vector3f v1, Vector3fi v2)
        {
            return new Vector3fi(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
        }

        public const int nDimensions = 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3f v)
        {
            DCHECK(!v.HasNaN());
            x += v.x;
            y += v.y;
            z += v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3i v)
        {
            x += v.x;
            y += v.y;
            z += v.z;
        }

        //public void operator +=(Vector3fi v);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator -(Vector3f v1, Vector3f v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector3f(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator -(Vector3f v1, Vector3i v2)
        {
            return new Vector3f(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Vector3f v1, Vector3fi v2)
        {
            return new Vector3fi(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3f v)
        {
            DCHECK(!v.HasNaN());
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3i v)
        {
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        //public void operator -=(Vector3fi v);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector3f v1, Vector3f v2)
        {
            return v1.x == v2.x && v1.y == v2.y && v1.z == v2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vector3f v1, Vector3f v2)
        {
            return v1.x != v2.x || v1.y != v2.y || v1.z != v2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator *(Vector3f v, Float s)
        {
            return new Vector3f(s * v.x, s * v.y, s * v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator *(Vector3f v, int s)
        {
            return new Vector3f(s * v.x, s * v.y, s * v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Vector3f v, Interval i)
        {
            return new Vector3fi(i * v.x, i * v.y, i * v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Float s)
        {
            DCHECK(!IsNaN(s));
            x *= s;
            y *= s;
            z *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
            z *= s;
        }

        //public void operator *=(Interval i);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator /(Vector3f v, Float d)
        {
            DCHECK_NE(d, 0);
            return new Vector3f(v.x / d, v.y / d, v.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator /(Vector3f v, int d)
        {
            DCHECK_NE(d, 0);
            return new Vector3f(v.x / d, v.y / d, v.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator /(Vector3f v, Interval i)
        {
            return new Vector3fi(v.x / i, v.y / i, v.z / i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(Float d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
            z /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
            z /= d;
        }

        //public void operator /=(Interval i);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator -(Vector3f v)
        {
            return new Vector3f(-v.x, -v.y, -v.z);
        }

        public override string ToString()
        {
            return $"[ {x}, {y}, {z} ]";
        }

        public bool Equals(Vector3f b) { return this == b; }
        public override bool Equals(object o) { return o is Vector3f b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(x, y, z); }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Vector3<Float>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3f(Vector3i v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3f(Vector3i v)
        {
            return new Vector3f(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3f(Point3f p)
        {
            x = p.x;
            y = p.y;
            z = p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3f(Point3f p)
        {
            return new Vector3f(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3f(Point3i p)
        {
            x = p.x;
            y = p.y;
            z = p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3f(Point3i p)
        {
            return new Vector3f(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3f(Normal3f n)
        {
            x = n.x;
            y = n.y;
            z = n.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3f(Normal3f n)
        {
            return new Vector3f(n);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3f(Vector3fi v)
        {
            x = (Float)v.x;
            y = (Float)v.y;
            z = (Float)v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3f(Vector3fi v)
        {
            return new Vector3f(v);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        public Float x, y, z;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator *(Float s, Vector3f v)
        {
            return v * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator *(int s, Vector3f v)
        {
            return v * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Interval i, Vector3f v)
        {
            return v * i;
        }
    }

    // Vector3i Definition
    public unsafe struct Vector3i : IEquatable<Vector3i>
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple3<Vector3, int>
        // ═══════════════════════════════════════════════════════════════════
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3i(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        //public bool HasNaN();  // int types don't have HasNaN

        public unsafe ref int this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 2);
                return ref Unsafe.Add(
                    ref Unsafe.As<Vector3i, int>(ref this), i);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i operator +(Vector3i v1, Vector3i v2)
        {
            return new Vector3i(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator +(Vector3i v1, Vector3f v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector3f(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator +(Vector3i v1, Vector3fi v2)
        {
            return new Vector3fi(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
        }

        public const int nDimensions = 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3i v)
        {
            x += v.x;
            y += v.y;
            z += v.z;
        }

        //public void operator +=(Vector3f v);  // Would change type
        //public void operator +=(Vector3fi v);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i operator -(Vector3i v1, Vector3i v2)
        {
            return new Vector3i(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator -(Vector3i v1, Vector3f v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector3f(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Vector3i v1, Vector3fi v2)
        {
            return new Vector3fi(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3i v)
        {
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        //public void operator -=(Vector3f v);  // Would change type
        //public void operator -=(Vector3fi v);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector3i v1, Vector3i v2)
        {
            return v1.x == v2.x && v1.y == v2.y && v1.z == v2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vector3i v1, Vector3i v2)
        {
            return v1.x != v2.x || v1.y != v2.y || v1.z != v2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i operator *(Vector3i v, int s)
        {
            return new Vector3i(s * v.x, s * v.y, s * v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator *(Vector3i v, Float s)
        {
            return new Vector3f(s * v.x, s * v.y, s * v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Vector3i v, Interval i)
        {
            return new Vector3fi(i * v.x, i * v.y, i * v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
            z *= s;
        }

        //public void operator *=(Float s);  // Would change type
        //public void operator *=(Interval i);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i operator /(Vector3i v, int d)
        {
            DCHECK_NE(d, 0);
            return new Vector3i(v.x / d, v.y / d, v.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator /(Vector3i v, Float d)
        {
            DCHECK_NE(d, 0);
            return new Vector3f(v.x / d, v.y / d, v.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator /(Vector3i v, Interval i)
        {
            return new Vector3fi(v.x / i, v.y / i, v.z / i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
            z /= d;
        }

        //public void operator /=(Float d);  // Would change type
        //public void operator /=(Interval i);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i operator -(Vector3i v)
        {
            return new Vector3i(-v.x, -v.y, -v.z);
        }

        public override string ToString()
        {
            return $"[ {x}, {y}, {z} ]";
        }

        public bool Equals(Vector3i b) { return this == b; }
        public override bool Equals(object o) { return o is Vector3i b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(x, y, z); }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Vector3<int>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3i(Vector3f v)
        {
            x = (int)v.x;
            y = (int)v.y;
            z = (int)v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3i(Vector3f v)
        {
            return new Vector3i(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3i(Point3i p)
        {
            x = p.x;
            y = p.y;
            z = p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3i(Point3i p)
        {
            return new Vector3i(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3i(Point3f p)
        {
            x = (int)p.x;
            y = (int)p.y;
            z = (int)p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3i(Point3f p)
        {
            return new Vector3i(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3i(Normal3f n)
        {
            x = (int)n.x;
            y = (int)n.y;
            z = (int)n.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3i(Normal3f n)
        {
            return new Vector3i(n);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        public int x, y, z;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i operator *(int s, Vector3i v)
        {
            return v * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator *(Float s, Vector3i v)
        {
            return v * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Interval i, Vector3i v)
        {
            return v * i;
        }
    }

    // Point2f Definition
    public unsafe struct Point2f : IEquatable<Point2f>
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple2<Point2, Float>
        // ═══════════════════════════════════════════════════════════════════
        
        public const int nDimensions = 2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point2f(Float x, Float y)
        {
            this.x = x;
            this.y = y;
            DCHECK(!HasNaN());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasNaN()
        {
            return IsNaN(x) || IsNaN(y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator +(Point2f p1, Point2f p2)
        {
            DCHECK(!p2.HasNaN());
            return new Point2f(p1.x + p2.x, p1.y + p2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator +(Point2f p1, Point2i p2)
        {
            return new Point2f(p1.x + p2.x, p1.y + p2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Point2f p)
        {
            DCHECK(!p.HasNaN());
            x += p.x;
            y += p.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Point2i p)
        {
            x += p.x;
            y += p.y;
        }

        //public static Point2f operator -(Point2f p1, Point2f p2);  // Hidden by Point2's version
        //public static Point2f operator -(Point2f p1, Point2i p2);

        //public void operator -=(Point2f p);  // Hidden by Point2's version
        //public void operator -=(Point2i p);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Point2f p1, Point2f p2)
        {
            return p1.x == p2.x && p1.y == p2.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Point2f p1, Point2f p2)
        {
            return p1.x != p2.x || p1.y != p2.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator *(Point2f p, Float s)
        {
            return new Point2f(s * p.x, s * p.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator *(Point2f p, int s)
        {
            return new Point2f(s * p.x, s * p.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Float s)
        {
            DCHECK(!IsNaN(s));
            x *= s;
            y *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator /(Point2f p, Float d)
        {
            DCHECK(d != 0 && !IsNaN(d));
            return new Point2f(p.x / d, p.y / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator /(Point2f p, int d)
        {
            DCHECK(d != 0);
            return new Point2f(p.x / d, p.y / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(Float d)
        {
            DCHECK_NE(d, 0);
            DCHECK(!IsNaN(d));
            x /= d;
            y /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator -(Point2f p)
        {
            return new Point2f(-p.x, -p.y);
        }

        public unsafe ref Float this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 1);
                return ref Unsafe.Add(
                    ref Unsafe.As<Point2f, Float>(ref this), i);
            }
        }

        public override string ToString()
        {
            return $"[ {x}, {y} ]";
        }

        public bool Equals(Point2f b) { return this == b; }
        public override bool Equals(object o) { return o is Point2f b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(x, y); }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Point2<Float>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point2f(Point2i p)
        {
            x = p.x;
            y = p.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point2f(Point2i p)
        {
            return new Point2f(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point2f(Vector2f v)
        {
            x = v.x;
            y = v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point2f(Vector2f v)
        {
            return new Point2f(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point2f(Vector2i v)
        {
            x = v.x;
            y = v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point2f(Vector2i v)
        {
            return new Point2f(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator +(Point2f p, Vector2f v)
        {
            DCHECK(!v.HasNaN());
            return new Point2f(p.x + v.x, p.y + v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator +(Point2f p, Vector2i v)
        {
            return new Point2f(p.x + v.x, p.y + v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector2f v)
        {
            DCHECK(!v.HasNaN());
            x += v.x;
            y += v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector2i v)
        {
            x += v.x;
            y += v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator -(Point2f p1, Point2f p2)
        {
            DCHECK(!p2.HasNaN());
            return new Vector2f(p1.x - p2.x, p1.y - p2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator -(Point2f p1, Point2i p2)
        {
            return new Vector2f(p1.x - p2.x, p1.y - p2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator -(Point2f p, Vector2f v)
        {
            DCHECK(!v.HasNaN());
            return new Point2f(p.x - v.x, p.y - v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator -(Point2f p, Vector2i v)
        {
            return new Point2f(p.x - v.x, p.y - v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector2f v)
        {
            DCHECK(!v.HasNaN());
            x -= v.x;
            y -= v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector2i v)
        {
            x -= v.x;
            y -= v.y;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple2)
        // ═══════════════════════════════════════════════════════════════════

        public Float x, y;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple2)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator *(Float s, Point2f p)
        {
            return p * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator *(int s, Point2f p)
        {
            return p * s;
        }
    }

    // Point2i Definition
    public unsafe struct Point2i : IEquatable<Point2i>
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple2<Point2, int>
        // ═══════════════════════════════════════════════════════════════════
        
        public const int nDimensions = 2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point2i(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        //public bool HasNaN();  // int types don't have HasNaN

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i operator +(Point2i p1, Point2i p2)
        {
            return new Point2i(p1.x + p2.x, p1.y + p2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator +(Point2i p1, Point2f p2)
        {
            DCHECK(!p2.HasNaN());
            return new Point2f(p1.x + p2.x, p1.y + p2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Point2i p)
        {
            x += p.x;
            y += p.y;
        }

        //public void operator +=(Point2f p);  // Would change type

        //public static Point2i operator -(Point2i p1, Point2i p2);  // Hidden by Point2's version
        //public static Point2f operator -(Point2i p1, Point2f p2);

        //public void operator -=(Point2i p);  // Hidden by Point2's version
        //public void operator -=(Point2f p);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Point2i p1, Point2i p2)
        {
            return p1.x == p2.x && p1.y == p2.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Point2i p1, Point2i p2)
        {
            return p1.x != p2.x || p1.y != p2.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i operator *(Point2i p, int s)
        {
            return new Point2i(s * p.x, s * p.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator *(Point2i p, Float s)
        {
            return new Point2f(s * p.x, s * p.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
        }

        //public void operator *=(Float s);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i operator /(Point2i p, int d)
        {
            DCHECK(d != 0);
            return new Point2i(p.x / d, p.y / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator /(Point2i p, Float d)
        {
            DCHECK(d != 0 && !IsNaN(d));
            return new Point2f(p.x / d, p.y / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
        }

        //public void operator /=(Float d);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i operator -(Point2i p)
        {
            return new Point2i(-p.x, -p.y);
        }

        public unsafe ref int this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 1);
                return ref Unsafe.Add(
                    ref Unsafe.As<Point2i, int>(ref this), i);
            }
        }

        public override string ToString()
        {
            return $"[ {x}, {y} ]";
        }

        public bool Equals(Point2i b) { return this == b; }
        public override bool Equals(object o) { return o is Point2i b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(x, y); }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Point2<int>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point2i(Point2f p)
        {
            x = (int)p.x;
            y = (int)p.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point2i(Point2f p)
        {
            return new Point2i(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point2i(Vector2i v)
        {
            x = v.x;
            y = v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point2i(Vector2i v)
        {
            return new Point2i(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point2i(Vector2f v)
        {
            x = (int)v.x;
            y = (int)v.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point2i(Vector2f v)
        {
            return new Point2i(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i operator +(Point2i p, Vector2i v)
        {
            return new Point2i(p.x + v.x, p.y + v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator +(Point2i p, Vector2f v)
        {
            DCHECK(!v.HasNaN());
            return new Point2f(p.x + v.x, p.y + v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector2i v)
        {
            x += v.x;
            y += v.y;
        }

        //public void operator +=(Vector2f v);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2i operator -(Point2i p1, Point2i p2)
        {
            return new Vector2i(p1.x - p2.x, p1.y - p2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2f operator -(Point2i p1, Point2f p2)
        {
            DCHECK(!p2.HasNaN());
            return new Vector2f(p1.x - p2.x, p1.y - p2.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i operator -(Point2i p, Vector2i v)
        {
            return new Point2i(p.x - v.x, p.y - v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator -(Point2i p, Vector2f v)
        {
            DCHECK(!v.HasNaN());
            return new Point2f(p.x - v.x, p.y - v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector2i v)
        {
            x -= v.x;
            y -= v.y;
        }

        //public void operator -=(Vector2f v);  // Would change type

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple2)
        // ═══════════════════════════════════════════════════════════════════

        public int x, y;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple2)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2i operator *(int s, Point2i p)
        {
            return p * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point2f operator *(Float s, Point2i p)
        {
            return p * s;
        }
    }

    // Point3f Definition
    public unsafe struct Point3f : IEquatable<Point3f>
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple3<Point3, Float>
        // ═══════════════════════════════════════════════════════════════════
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3f(Float x, Float y, Float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            DCHECK(!HasNaN());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasNaN()
        {
            return IsNaN(x) || IsNaN(y) || IsNaN(z);
        }

        public unsafe ref Float this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 2);
                return ref Unsafe.Add(
                    ref Unsafe.As<Point3f, Float>(ref this), i);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator +(Point3f p1, Point3f p2)
        {
            DCHECK(!p2.HasNaN());
            return new Point3f(p1.x + p2.x, p1.y + p2.y, p1.z + p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator +(Point3f p1, Point3i p2)
        {
            return new Point3f(p1.x + p2.x, p1.y + p2.y, p1.z + p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator +(Point3f p1, Point3fi p2)
        {
            return new Point3fi(p1.x + p2.x, p1.y + p2.y, p1.z + p2.z);
        }

        public const int nDimensions = 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Point3f p)
        {
            DCHECK(!p.HasNaN());
            x += p.x;
            y += p.y;
            z += p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Point3i p)
        {
            x += p.x;
            y += p.y;
            z += p.z;
        }

        //public void operator +=(Point3fi p);  // Would change type

        //public static Point3f operator -(Point3f p1, Point3f p2);  // Hidden by Point3's version
        //public static Point3f operator -(Point3f p1, Point3i p2);
        //public static Point3fi operator -(Point3f p1, Point3fi p2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Point3f p)
        {
            DCHECK(!p.HasNaN());
            x -= p.x;
            y -= p.y;
            z -= p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Point3i p)
        {
            x -= p.x;
            y -= p.y;
            z -= p.z;
        }

        //public void operator -=(Point3fi p);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Point3f p1, Point3f p2)
        {
            return p1.x == p2.x && p1.y == p2.y && p1.z == p2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Point3f p1, Point3f p2)
        {
            return p1.x != p2.x || p1.y != p2.y || p1.z != p2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator *(Point3f p, Float s)
        {
            return new Point3f(s * p.x, s * p.y, s * p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator *(Point3f p, int s)
        {
            return new Point3f(s * p.x, s * p.y, s * p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator *(Point3f p, Interval i)
        {
            return new Point3fi(i * p.x, i * p.y, i * p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Float s)
        {
            DCHECK(!IsNaN(s));
            x *= s;
            y *= s;
            z *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
            z *= s;
        }

        //public void operator *=(Interval i);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator /(Point3f p, Float d)
        {
            DCHECK_NE(d, 0);
            return new Point3f(p.x / d, p.y / d, p.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator /(Point3f p, int d)
        {
            DCHECK_NE(d, 0);
            return new Point3f(p.x / d, p.y / d, p.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator /(Point3f p, Interval i)
        {
            return new Point3fi(p.x / i, p.y / i, p.z / i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(Float d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
            z /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
            z /= d;
        }

        //public void operator /=(Interval i);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator -(Point3f p)
        {
            return new Point3f(-p.x, -p.y, -p.z);
        }

        public override string ToString()
        {
            return $"[ {x}, {y}, {z} ]";
        }

        public bool Equals(Point3f b) { return this == b; }
        public override bool Equals(object o) { return o is Point3f b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(x, y, z); }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Point3<Float>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3f(Point3i p)
        {
            x = p.x;
            y = p.y;
            z = p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3f(Point3i p)
        {
            return new Point3f(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3f(Vector3f v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3f(Vector3f v)
        {
            return new Point3f(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3f(Vector3i v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3f(Vector3i v)
        {
            return new Point3f(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator +(Point3f p, Vector3f v)
        {
            DCHECK(!v.HasNaN());
            return new Point3f(p.x + v.x, p.y + v.y, p.z + v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator +(Point3f p, Vector3i v)
        {
            return new Point3f(p.x + v.x, p.y + v.y, p.z + v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3f v)
        {
            DCHECK(!v.HasNaN());
            x += v.x;
            y += v.y;
            z += v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3i v)
        {
            x += v.x;
            y += v.y;
            z += v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator -(Point3f p, Vector3f v)
        {
            DCHECK(!v.HasNaN());
            return new Point3f(p.x - v.x, p.y - v.y, p.z - v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator -(Point3f p, Vector3i v)
        {
            return new Point3f(p.x - v.x, p.y - v.y, p.z - v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3f v)
        {
            DCHECK(!v.HasNaN());
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3i v)
        {
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator -(Point3f p1, Point3f p2)
        {
            DCHECK(!p2.HasNaN());
            return new Vector3f(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator -(Point3f p1, Point3i p2)
        {
            return new Vector3f(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Point3f p1, Point3fi p2)
        {
            return new Vector3fi(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3f(Point3fi p)
        {
            x = (Float)p.x;
            y = (Float)p.y;
            z = (Float)p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3f(Point3fi p)
        {
            return new Point3f(p);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        public Float x, y, z;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator *(Float s, Point3f p)
        {
            return p * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator *(int s, Point3f p)
        {
            return p * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator *(Interval i, Point3f p)
        {
            return p * i;
        }
    }

    // Point3i Definition
    public unsafe struct Point3i : IEquatable<Point3i>
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple3<Point3, int>
        // ═══════════════════════════════════════════════════════════════════
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3i(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        //public bool HasNaN();  // int types don't have HasNaN

        public unsafe ref int this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 2);
                return ref Unsafe.Add(
                    ref Unsafe.As<Point3i, int>(ref this), i);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i operator +(Point3i p1, Point3i p2)
        {
            return new Point3i(p1.x + p2.x, p1.y + p2.y, p1.z + p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator +(Point3i p1, Point3f p2)
        {
            DCHECK(!p2.HasNaN());
            return new Point3f(p1.x + p2.x, p1.y + p2.y, p1.z + p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator +(Point3i p1, Point3fi p2)
        {
            return new Point3fi(p1.x + p2.x, p1.y + p2.y, p1.z + p2.z);
        }

        public const int nDimensions = 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Point3i p)
        {
            x += p.x;
            y += p.y;
            z += p.z;
        }

        //public void operator +=(Point3f p);  // Would change type
        //public void operator +=(Point3fi p);  // Would change type

        //public static Point3i operator -(Point3i p1, Point3i p2);  // Hidden by Point3's version
        //public static Point3f operator -(Point3i p1, Point3f p2);
        //public static Point3fi operator -(Point3i p1, Point3fi p2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Point3i p)
        {
            x -= p.x;
            y -= p.y;
            z -= p.z;
        }

        //public void operator -=(Point3f p);  // Would change type
        //public void operator -=(Point3fi p);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Point3i p1, Point3i p2)
        {
            return p1.x == p2.x && p1.y == p2.y && p1.z == p2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Point3i p1, Point3i p2)
        {
            return p1.x != p2.x || p1.y != p2.y || p1.z != p2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i operator *(Point3i p, int s)
        {
            return new Point3i(s * p.x, s * p.y, s * p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator *(Point3i p, Float s)
        {
            return new Point3f(s * p.x, s * p.y, s * p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator *(Point3i p, Interval i)
        {
            return new Point3fi(i * p.x, i * p.y, i * p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
            z *= s;
        }

        //public void operator *=(Float s);  // Would change type
        //public void operator *=(Interval i);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i operator /(Point3i p, int d)
        {
            DCHECK_NE(d, 0);
            return new Point3i(p.x / d, p.y / d, p.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator /(Point3i p, Float d)
        {
            DCHECK_NE(d, 0);
            return new Point3f(p.x / d, p.y / d, p.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator /(Point3i p, Interval i)
        {
            return new Point3fi(p.x / i, p.y / i, p.z / i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
            z /= d;
        }

        //public void operator /=(Float d);  // Would change type
        //public void operator /=(Interval i);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i operator -(Point3i p)
        {
            return new Point3i(-p.x, -p.y, -p.z);
        }

        public override string ToString()
        {
            return $"[ {x}, {y}, {z} ]";
        }

        public bool Equals(Point3i b) { return this == b; }
        public override bool Equals(object o) { return o is Point3i b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(x, y, z); }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Point3<int>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3i(Point3f p)
        {
            x = (int)p.x;
            y = (int)p.y;
            z = (int)p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3i(Point3f p)
        {
            return new Point3i(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3i(Vector3i v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3i(Vector3i v)
        {
            return new Point3i(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3i(Vector3f v)
        {
            x = (int)v.x;
            y = (int)v.y;
            z = (int)v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3i(Vector3f v)
        {
            return new Point3i(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i operator +(Point3i p, Vector3i v)
        {
            return new Point3i(p.x + v.x, p.y + v.y, p.z + v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator +(Point3i p, Vector3f v)
        {
            DCHECK(!v.HasNaN());
            return new Point3f(p.x + v.x, p.y + v.y, p.z + v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3i v)
        {
            x += v.x;
            y += v.y;
            z += v.z;
        }

        //public void operator +=(Vector3f v);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i operator -(Point3i p, Vector3i v)
        {
            return new Point3i(p.x - v.x, p.y - v.y, p.z - v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator -(Point3i p, Vector3f v)
        {
            DCHECK(!v.HasNaN());
            return new Point3f(p.x - v.x, p.y - v.y, p.z - v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3i v)
        {
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        //public void operator -=(Vector3f v);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3i operator -(Point3i p1, Point3i p2)
        {
            return new Vector3i(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f operator -(Point3i p1, Point3f p2)
        {
            DCHECK(!p2.HasNaN());
            return new Vector3f(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Point3i p1, Point3fi p2)
        {
            return new Vector3fi(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        public int x, y, z;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3i operator *(int s, Point3i p)
        {
            return p * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f operator *(Float s, Point3i p)
        {
            return p * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator *(Interval i, Point3i p)
        {
            return p * i;
        }
    }

    // Normal3f Definition
    public unsafe struct Normal3f : IEquatable<Normal3f>
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple3<Normal3, Float>
        // ═══════════════════════════════════════════════════════════════════
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Normal3f(Float x, Float y, Float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            DCHECK(!HasNaN());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasNaN()
        {
            return IsNaN(x) || IsNaN(y) || IsNaN(z);
        }

        public unsafe ref Float this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 2);
                return ref Unsafe.Add(
                    ref Unsafe.As<Normal3f, Float>(ref this), i);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f operator +(Normal3f n1, Normal3f n2)
        {
            DCHECK(!n2.HasNaN());
            return new Normal3f(n1.x + n2.x, n1.y + n2.y, n1.z + n2.z);
        }

        public const int nDimensions = 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Normal3f n)
        {
            DCHECK(!n.HasNaN());
            x += n.x;
            y += n.y;
            z += n.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f operator -(Normal3f n1, Normal3f n2)
        {
            DCHECK(!n2.HasNaN());
            return new Normal3f(n1.x - n2.x, n1.y - n2.y, n1.z - n2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Normal3f n)
        {
            DCHECK(!n.HasNaN());
            x -= n.x;
            y -= n.y;
            z -= n.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Normal3f n1, Normal3f n2)
        {
            return n1.x == n2.x && n1.y == n2.y && n1.z == n2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Normal3f n1, Normal3f n2)
        {
            return n1.x != n2.x || n1.y != n2.y || n1.z != n2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f operator *(Normal3f n, Float s)
        {
            return new Normal3f(s * n.x, s * n.y, s * n.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f operator *(Normal3f n, int s)
        {
            return new Normal3f(s * n.x, s * n.y, s * n.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Normal3f n, Interval i)
        {
            // IMPORTANT: Normal * Interval returns Vector3fi, NOT Normal3fi!
            return new Vector3fi(i * n.x, i * n.y, i * n.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Float s)
        {
            DCHECK(!IsNaN(s));
            x *= s;
            y *= s;
            z *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
            z *= s;
        }

        //public void operator *=(Interval i);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f operator /(Normal3f n, Float d)
        {
            DCHECK_NE(d, 0);
            return new Normal3f(n.x / d, n.y / d, n.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f operator /(Normal3f n, int d)
        {
            DCHECK_NE(d, 0);
            return new Normal3f(n.x / d, n.y / d, n.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator /(Normal3f n, Interval i)
        {
            // IMPORTANT: Normal / Interval returns Vector3fi, NOT Normal3fi!
            return new Vector3fi(n.x / i, n.y / i, n.z / i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(Float d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
            z /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE(d, 0);
            x /= d;
            y /= d;
            z /= d;
        }

        //public void operator /=(Interval i);  // Would change type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f operator -(Normal3f n)
        {
            return new Normal3f(-n.x, -n.y, -n.z);
        }

        public override string ToString()
        {
            return $"[ {x}, {y}, {z} ]";
        }

        public bool Equals(Normal3f b) { return this == b; }
        public override bool Equals(object o) { return o is Normal3f b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(x, y, z); }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Normal3<Float>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Normal3f(Vector3f v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Normal3f(Vector3f v)
        {
            return new Normal3f(v);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        public Float x, y, z;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f operator *(Float s, Normal3f n)
        {
            return n * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f operator *(int s, Normal3f n)
        {
            return n * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Interval i, Normal3f n)
        {
            // IMPORTANT: Interval * Normal returns Vector3fi, NOT Normal3fi!
            return n * i;
        }
    }

    // Vector3fi Definition
    public unsafe struct Vector3fi
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple3<Vector3, Interval>
        // ═══════════════════════════════════════════════════════════════════
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3fi(Interval x, Interval y, Interval z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            DCHECK(!HasNaN());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasNaN()
        {
            return IsNaN((Float)x) || IsNaN((Float)y) || IsNaN((Float)z);
        }

        public unsafe ref Interval this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 2);
                return ref Unsafe.Add(
                    ref Unsafe.As<Vector3fi, Interval>(ref this), i);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator +(Vector3fi v1, Vector3fi v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector3fi(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator +(Vector3fi v1, Vector3f v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector3fi(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator +(Vector3fi v1, Vector3i v2)
        {
            return new Vector3fi(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
        }

        public const int nDimensions = 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3fi v)
        {
            DCHECK(!v.HasNaN());
            x += v.x;
            y += v.y;
            z += v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3f v)
        {
            DCHECK(!v.HasNaN());
            x += v.x;
            y += v.y;
            z += v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3i v)
        {
            x += v.x;
            y += v.y;
            z += v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Vector3fi v1, Vector3fi v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector3fi(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Vector3fi v1, Vector3f v2)
        {
            DCHECK(!v2.HasNaN());
            return new Vector3fi(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Vector3fi v1, Vector3i v2)
        {
            return new Vector3fi(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3fi v)
        {
            DCHECK(!v.HasNaN());
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3f v)
        {
            DCHECK(!v.HasNaN());
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3i v)
        {
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector3fi v1, Vector3fi v2)
        {
            return v1.x == v2.x && v1.y == v2.y && v1.z == v2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vector3fi v1, Vector3fi v2)
        {
            return v1.x != v2.x || v1.y != v2.y || v1.z != v2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Vector3fi v, Interval s)
        {
            return new Vector3fi(s * v.x, s * v.y, s * v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Vector3fi v, Float s)
        {
            return new Vector3fi(s * v.x, s * v.y, s * v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Vector3fi v, int s)
        {
            return new Vector3fi(s * v.x, s * v.y, s * v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Interval s)
        {
            DCHECK(!IsNaN((Float)s));
            x *= s;
            y *= s;
            z *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Float s)
        {
            DCHECK(!IsNaN((Float)s));
            x *= s;
            y *= s;
            z *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
            z *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator /(Vector3fi v, Interval d)
        {
            DCHECK_NE((Float)d, (Float)0);
            return new Vector3fi(v.x / d, v.y / d, v.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator /(Vector3fi v, Float d)
        {
            DCHECK_NE((Float)d, (Float)0);
            return new Vector3fi(v.x / d, v.y / d, v.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator /(Vector3fi v, int d)
        {
            DCHECK_NE((Float)d, (Float)0);
            return new Vector3fi(v.x / d, v.y / d, v.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(Interval d)
        {
            DCHECK_NE((Float)d, (Float)0);
            x /= d;
            y /= d;
            z /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(Float d)
        {
            DCHECK_NE((Float)d, (Float)0);
            x /= d;
            y /= d;
            z /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE((Float)d, (Float)0);
            x /= d;
            y /= d;
            z /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Vector3fi v)
        {
            return new Vector3fi(-v.x, -v.y, -v.z);
        }

        public override string ToString()
        {
            return $"[ {x} {y} {z} ]";
        }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Vector3<Interval>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3fi(Vector3f v)
        {
            x = new Interval(v.x);
            y = new Interval(v.y);
            z = new Interval(v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3fi(Vector3f v)
        {
            return new Vector3fi(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3fi(Vector3i v)
        {
            x = new Interval(v.x);
            y = new Interval(v.y);
            z = new Interval(v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3fi(Vector3i v)
        {
            return new Vector3fi(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3fi(Point3fi p)
        {
            x = p.x;
            y = p.y;
            z = p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3fi(Point3fi p)
        {
            return new Vector3fi(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3fi(Point3f p)
        {
            x = new Interval(p.x);
            y = new Interval(p.y);
            z = new Interval(p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3fi(Point3f p)
        {
            return new Vector3fi(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3fi(Point3i p)
        {
            x = new Interval(p.x);
            y = new Interval(p.y);
            z = new Interval(p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3fi(Point3i p)
        {
            return new Vector3fi(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3fi(Normal3f n)
        {
            x = new Interval(n.x);
            y = new Interval(n.y);
            z = new Interval(n.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3fi(Normal3f n)
        {
            return new Vector3fi(n);
        }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Vector3fi (specific to this type)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3fi(Float x, Float y, Float z)
        {
            this.x = new Interval(x);
            this.y = new Interval(y);
            this.z = new Interval(z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3fi(Vector3f v, Vector3f error)
        {
            x = Interval.FromValueAndError(v.x, error.x);
            y = Interval.FromValueAndError(v.y, error.y);
            z = Interval.FromValueAndError(v.z, error.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3f Error()
        {
            return new Vector3f(x.Width() / 2, y.Width() / 2, z.Width() / 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsExact()
        {
            return x.Width() == 0 && y.Width() == 0 && z.Width() == 0;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        public Interval x, y, z;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Interval s, Vector3fi v)
        {
            return v * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(Float s, Vector3fi v)
        {
            return v * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator *(int s, Vector3fi v)
        {
            return v * s;
        }
    }

    // Point3fi Definition
    public unsafe struct Point3fi
    {
        // ═══════════════════════════════════════════════════════════════════
        // FROM: Tuple3<Point3, Interval>
        // ═══════════════════════════════════════════════════════════════════
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3fi(Interval x, Interval y, Interval z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            DCHECK(!HasNaN());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasNaN()
        {
            return IsNaN((Float)x) || IsNaN((Float)y) || IsNaN((Float)z);
        }

        public unsafe ref Interval this[int i]
        {
            get
            {
                DCHECK(i >= 0 && i <= 2);
                return ref Unsafe.Add(
                    ref Unsafe.As<Point3fi, Interval>(ref this), i);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator +(Point3fi p1, Point3fi p2)
        {
            DCHECK(!p2.HasNaN());
            return new Point3fi(p1.x + p2.x, p1.y + p2.y, p1.z + p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator +(Point3fi p1, Point3f p2)
        {
            DCHECK(!p2.HasNaN());
            return new Point3fi(p1.x + p2.x, p1.y + p2.y, p1.z + p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator +(Point3fi p1, Point3i p2)
        {
            return new Point3fi(p1.x + p2.x, p1.y + p2.y, p1.z + p2.z);
        }

        public const int nDimensions = 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Point3fi p)
        {
            DCHECK(!p.HasNaN());
            x += p.x;
            y += p.y;
            z += p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Point3f p)
        {
            DCHECK(!p.HasNaN());
            x += p.x;
            y += p.y;
            z += p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Point3i p)
        {
            x += p.x;
            y += p.y;
            z += p.z;
        }

        //public static Point3fi operator -(Point3fi p1, Point3fi p2);  // Hidden by Point3's version
        //public static Point3fi operator -(Point3fi p1, Point3f p2);
        //public static Point3fi operator -(Point3fi p1, Point3i p2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Point3fi p)
        {
            DCHECK(!p.HasNaN());
            x -= p.x;
            y -= p.y;
            z -= p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Point3f p)
        {
            DCHECK(!p.HasNaN());
            x -= p.x;
            y -= p.y;
            z -= p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Point3i p)
        {
            x -= p.x;
            y -= p.y;
            z -= p.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Point3fi p1, Point3fi p2)
        {
            return p1.x == p2.x && p1.y == p2.y && p1.z == p2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Point3fi p1, Point3fi p2)
        {
            return p1.x != p2.x || p1.y != p2.y || p1.z != p2.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator *(Point3fi p, Interval s)
        {
            return new Point3fi(s * p.x, s * p.y, s * p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator *(Point3fi p, Float s)
        {
            return new Point3fi(s * p.x, s * p.y, s * p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator *(Point3fi p, int s)
        {
            return new Point3fi(s * p.x, s * p.y, s * p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Interval s)
        {
            DCHECK(!IsNaN((Float)s));
            x *= s;
            y *= s;
            z *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Float s)
        {
            DCHECK(!IsNaN((Float)s));
            x *= s;
            y *= s;
            z *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(int s)
        {
            x *= s;
            y *= s;
            z *= s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator /(Point3fi p, Interval d)
        {
            DCHECK_NE((Float)d, (Float)0);
            return new Point3fi(p.x / d, p.y / d, p.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator /(Point3fi p, Float d)
        {
            DCHECK_NE((Float)d, (Float)0);
            return new Point3fi(p.x / d, p.y / d, p.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator /(Point3fi p, int d)
        {
            DCHECK_NE((Float)d, (Float)0);
            return new Point3fi(p.x / d, p.y / d, p.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(Interval d)
        {
            DCHECK_NE((Float)d, (Float)0);
            x /= d;
            y /= d;
            z /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(Float d)
        {
            DCHECK_NE((Float)d, (Float)0);
            x /= d;
            y /= d;
            z /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator /=(int d)
        {
            DCHECK_NE((Float)d, (Float)0);
            x /= d;
            y /= d;
            z /= d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator -(Point3fi p)
        {
            return new Point3fi(-p.x, -p.y, -p.z);
        }

        public override string ToString()
        {
            return $"[ {x} {y} {z} ]";
        }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Point3<Interval>
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3fi(Point3f p)
        {
            x = new Interval(p.x);
            y = new Interval(p.y);
            z = new Interval(p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Point3fi(Point3f p)
        {
            return new Point3fi(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3fi(Point3i p)
        {
            x = new Interval(p.x);
            y = new Interval(p.y);
            z = new Interval(p.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3fi(Point3i p)
        {
            return new Point3fi(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3fi(Vector3fi v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3fi(Vector3fi v)
        {
            return new Point3fi(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3fi(Vector3f v)
        {
            x = new Interval(v.x);
            y = new Interval(v.y);
            z = new Interval(v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3fi(Vector3f v)
        {
            return new Point3fi(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3fi(Vector3i v)
        {
            x = new Interval(v.x);
            y = new Interval(v.y);
            z = new Interval(v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Point3fi(Vector3i v)
        {
            return new Point3fi(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator +(Point3fi p, Vector3fi v)
        {
            DCHECK(!v.HasNaN());
            return new Point3fi(p.x + v.x, p.y + v.y, p.z + v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator +(Point3fi p, Vector3f v)
        {
            DCHECK(!v.HasNaN());
            return new Point3fi(p.x + v.x, p.y + v.y, p.z + v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator +(Point3fi p, Vector3i v)
        {
            return new Point3fi(p.x + v.x, p.y + v.y, p.z + v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3fi v)
        {
            DCHECK(!v.HasNaN());
            x += v.x;
            y += v.y;
            z += v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3f v)
        {
            DCHECK(!v.HasNaN());
            x += v.x;
            y += v.y;
            z += v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3i v)
        {
            x += v.x;
            y += v.y;
            z += v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator -(Point3fi p, Vector3fi v)
        {
            DCHECK(!v.HasNaN());
            return new Point3fi(p.x - v.x, p.y - v.y, p.z - v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator -(Point3fi p, Vector3f v)
        {
            DCHECK(!v.HasNaN());
            return new Point3fi(p.x - v.x, p.y - v.y, p.z - v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator -(Point3fi p, Vector3i v)
        {
            return new Point3fi(p.x - v.x, p.y - v.y, p.z - v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3fi v)
        {
            DCHECK(!v.HasNaN());
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3f v)
        {
            DCHECK(!v.HasNaN());
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3i v)
        {
            x -= v.x;
            y -= v.y;
            z -= v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Point3fi p1, Point3fi p2)
        {
            DCHECK(!p2.HasNaN());
            return new Vector3fi(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Point3fi p1, Point3f p2)
        {
            DCHECK(!p2.HasNaN());
            return new Vector3fi(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3fi operator -(Point3fi p1, Point3i p2)
        {
            return new Vector3fi(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        // ═══════════════════════════════════════════════════════════════════
        // FROM: Point3fi (specific to this type)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3fi(Float x, Float y, Float z)
        {
            this.x = new Interval(x);
            this.y = new Interval(y);
            this.z = new Interval(z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3fi(Point3f p, Vector3f error)
        {
            x = Interval.FromValueAndError(p.x, error.x);
            y = Interval.FromValueAndError(p.y, error.y);
            z = Interval.FromValueAndError(p.z, error.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3f Error()
        {
            return new Vector3f(x.Width() / 2, y.Width() / 2, z.Width() / 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsExact()
        {
            return x.Width() == 0 && y.Width() == 0 && z.Width() == 0;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Data members (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        public Interval x, y, z;

        // ═══════════════════════════════════════════════════════════════════
        // Global operators (from Tuple3)
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator *(Interval s, Point3fi p)
        {
            return p * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator *(Float s, Point3fi p)
        {
            return p * s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3fi operator *(int s, Point3fi p)
        {
            return p * s;
        }
    }

    // Quaternion Definition
    public unsafe struct Quaternion
    {
        // Quaternion Public Methods
        public Quaternion(Vector3f v, Float w) { this.v = v; this.w = w; }
        public static Quaternion Identity() { return new Quaternion(new Vector3f(), 1); }

        public static Quaternion operator +(Quaternion a, Quaternion q) { return new Quaternion(a.v + q.v, a.w + q.w); }
        public static Quaternion operator -(Quaternion a) { return new Quaternion(-a.v, -a.w); }
        public static Quaternion operator -(Quaternion a, Quaternion q) { return new Quaternion(a.v - q.v, a.w - q.w); }
        public static Quaternion operator *(Quaternion a, Float f) { return new Quaternion(a.v * f, a.w * f); }
        public static Quaternion operator *(Float f, Quaternion q) { return q * f; }
        public static Quaternion operator /(Quaternion a, Float f)
        {
            DCHECK_NE(0, f);
            return new Quaternion(a.v / f, a.w / f);
        }

        public override string ToString() { return $"[ {v.x}, {v.y}, {v.z}, {w} ]"; }

        // Quaternion Public Members
        public Vector3f v;
        public Float w;
    }

    // Bounds2f Definition
    public unsafe struct Bounds2f : IEquatable<Bounds2f>
    {
        // Bounds2f Public Methods
        public Bounds2f()
        {
            Float minNum = Float.MinValue;
            Float maxNum = Float.MaxValue;
            pMin = new Point2f(maxNum, maxNum);
            pMax = new Point2f(minNum, minNum);
        }
        public Bounds2f(Point2f p) { pMin = p; pMax = p; }
        public Bounds2f(Point2f p1, Point2f p2) { pMin = Min(p1, p2); pMax = Max(p1, p2); }

        public Vector2f Diagonal() { return pMax - pMin; }

        public Float Area()
        {
            Vector2f d = pMax - pMin;
            return d.x * d.y;
        }

        public bool IsEmpty() { return pMin.x >= pMax.x || pMin.y >= pMax.y; }

        public bool IsDegenerate() { return pMin.x > pMax.x || pMin.y > pMax.y; }

        public int MaxDimension()
        {
            Vector2f diag = Diagonal();
            if (diag.x > diag.y)
                return 0;
            else
                return 1;
        }

        public Point2f this[int i]
        {
            get { DCHECK(i == 0 || i == 1); return (i == 0) ? pMin : pMax; }
            set { DCHECK(i == 0 || i == 1); if (i == 0) pMin = value; else pMax = value; }
        }

        public static bool operator ==(Bounds2f a, Bounds2f b) { return b.pMin == a.pMin && b.pMax == a.pMax; }
        public static bool operator !=(Bounds2f a, Bounds2f b) { return b.pMin != a.pMin || b.pMax != a.pMax; }
        public bool Equals(Bounds2f b) { return this == b; }
        public override bool Equals(object o) { return o is Bounds2f b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(pMin, pMax); }

        public Point2f Corner(int corner)
        {
            DCHECK(corner >= 0 && corner < 4);
            return new Point2f(this[(corner & 1)].x, this[(corner & 2) != 0 ? 1 : 0].y);
        }

        public Point2f Lerp(Point2f t)
        {
            return new Point2f(pbrt.Lerp(t.x, pMin.x, pMax.x), pbrt.Lerp(t.y, pMin.y, pMax.y));
        }

        public Vector2f Offset(Point2f p)
        {
            Vector2f o = p - pMin;
            if (pMax.x > pMin.x)
                o.x /= pMax.x - pMin.x;
            if (pMax.y > pMin.y)
                o.y /= pMax.y - pMin.y;
            return o;
        }

        public void BoundingSphere(Point2f* c, Float* rad)
        {
            *c = (Point2f)((pMin + pMax) / 2);
            *rad = Inside(*c, this) ? Distance(*c, pMax) : 0;
        }

        public override string ToString() { return $"[ {pMin} - {pMax} ]"; }

        // Bounds2f Public Members
        public Point2f pMin, pMax;
    }

    // Bounds2i Definition
    public unsafe struct Bounds2i : IEquatable<Bounds2i>
    {
        // Bounds2i Public Methods
        public Bounds2i()
        {
            int minNum = int.MinValue;
            int maxNum = int.MaxValue;
            pMin = new Point2i(maxNum, maxNum);
            pMax = new Point2i(minNum, minNum);
        }
        public Bounds2i(Point2i p) { pMin = p; pMax = p; }
        public Bounds2i(Point2i p1, Point2i p2) { pMin = Min(p1, p2); pMax = Max(p1, p2); }

        public Vector2i Diagonal() { return pMax - pMin; }

        public int Area()
        {
            Vector2i d = pMax - pMin;
            return d.x * d.y;
        }

        public bool IsEmpty() { return pMin.x >= pMax.x || pMin.y >= pMax.y; }

        public bool IsDegenerate() { return pMin.x > pMax.x || pMin.y > pMax.y; }

        public int MaxDimension()
        {
            Vector2i diag = Diagonal();
            if (diag.x > diag.y)
                return 0;
            else
                return 1;
        }

        public Point2i this[int i]
        {
            get { DCHECK(i == 0 || i == 1); return (i == 0) ? pMin : pMax; }
            set { DCHECK(i == 0 || i == 1); if (i == 0) pMin = value; else pMax = value; }
        }

        public static bool operator ==(Bounds2i a, Bounds2i b) { return b.pMin == a.pMin && b.pMax == a.pMax; }
        public static bool operator !=(Bounds2i a, Bounds2i b) { return b.pMin != a.pMin || b.pMax != a.pMax; }
        public bool Equals(Bounds2i b) { return this == b; }
        public override bool Equals(object o) { return o is Bounds2i b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(pMin, pMax); }

        public Point2i Corner(int corner)
        {
            DCHECK(corner >= 0 && corner < 4);
            return new Point2i(this[(corner & 1)].x, this[(corner & 2) != 0 ? 1 : 0].y);
        }

        public Point2i Lerp(Point2f t)
        {
            return new Point2i((int)pbrt.Lerp(t.x, pMin.x, pMax.x), (int)pbrt.Lerp(t.y, pMin.y, pMax.y));
        }

        public Vector2i Offset(Point2i p)
        {
            Vector2i o = p - pMin;
            if (pMax.x > pMin.x)
                o.x /= pMax.x - pMin.x;
            if (pMax.y > pMin.y)
                o.y /= pMax.y - pMin.y;
            return o;
        }

        public void BoundingSphere(Point2i* c, Float* rad)
        {
            *c = (Point2i)((pMin + pMax) / 2);
            *rad = Inside(*c, this) ? Distance(*c, pMax) : 0;
        }

        public override string ToString() { return $"[ {pMin} - {pMax} ]"; }

        // range-for support: foreach (Point2i p in bounds)
        public Bounds2iEnumerator GetEnumerator() { return new Bounds2iEnumerator(this); }

        // Bounds2i Public Members
        public Point2i pMin, pMax;
    }

    // Bounds3f Definition
    public unsafe struct Bounds3f : IEquatable<Bounds3f>
    {
        // Bounds3f Public Methods
        public Bounds3f()
        {
            Float minNum = Float.MinValue;
            Float maxNum = Float.MaxValue;
            pMin = new Point3f(maxNum, maxNum, maxNum);
            pMax = new Point3f(minNum, minNum, minNum);
        }

        public Bounds3f(Point3f p) { pMin = p; pMax = p; }

        public Bounds3f(Point3f p1, Point3f p2) { pMin = Min(p1, p2); pMax = Max(p1, p2); }

        public Point3f this[int i]
        {
            get { DCHECK(i == 0 || i == 1); return (i == 0) ? pMin : pMax; }
            set { DCHECK(i == 0 || i == 1); if (i == 0) pMin = value; else pMax = value; }
        }

        public Point3f Corner(int corner)
        {
            DCHECK(corner >= 0 && corner < 8);
            return new Point3f(this[(corner & 1)].x, this[(corner & 2) != 0 ? 1 : 0].y, this[(corner & 4) != 0 ? 1 : 0].z);
        }

        public Vector3f Diagonal() { return pMax - pMin; }

        public Float SurfaceArea()
        {
            Vector3f d = Diagonal();
            return 2 * (d.x * d.y + d.x * d.z + d.y * d.z);
        }

        public Float Volume()
        {
            Vector3f d = Diagonal();
            return d.x * d.y * d.z;
        }

        public int MaxDimension()
        {
            Vector3f d = Diagonal();
            if (d.x > d.y && d.x > d.z)
                return 0;
            else if (d.y > d.z)
                return 1;
            else
                return 2;
        }

        public Point3f Lerp(Point3f t)
        {
            return new Point3f(pbrt.Lerp(t.x, pMin.x, pMax.x), pbrt.Lerp(t.y, pMin.y, pMax.y),
                               pbrt.Lerp(t.z, pMin.z, pMax.z));
        }

        public Vector3f Offset(Point3f p)
        {
            Vector3f o = p - pMin;
            if (pMax.x > pMin.x)
                o.x /= pMax.x - pMin.x;
            if (pMax.y > pMin.y)
                o.y /= pMax.y - pMin.y;
            if (pMax.z > pMin.z)
                o.z /= pMax.z - pMin.z;
            return o;
        }

        public void BoundingSphere(Point3f* center, Float* radius)
        {
            *center = (Point3f)((pMin + pMax) / 2);
            *radius = Inside(*center, this) ? Distance(*center, pMax) : 0;
        }

        public bool IsEmpty() { return pMin.x >= pMax.x || pMin.y >= pMax.y || pMin.z >= pMax.z; }
        public bool IsDegenerate() { return pMin.x > pMax.x || pMin.y > pMax.y || pMin.z > pMax.z; }

        public static bool operator ==(Bounds3f a, Bounds3f b) { return b.pMin == a.pMin && b.pMax == a.pMax; }
        public static bool operator !=(Bounds3f a, Bounds3f b) { return b.pMin != a.pMin || b.pMax != a.pMax; }
        public bool Equals(Bounds3f b) { return this == b; }
        public override bool Equals(object o) { return o is Bounds3f b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(pMin, pMax); }

        public bool IntersectP(Point3f o, Vector3f d, Float tMax = Infinity, Float* hitt0 = null, Float* hitt1 = null)
        {
            Float t0 = 0, t1 = tMax;
            for (int i = 0; i < 3; ++i)
            {
                // Update interval for _i_th bounding box slab
                Float invRayDir = 1 / d[i];
                Float tNear = (pMin[i] - o[i]) * invRayDir;
                Float tFar = (pMax[i] - o[i]) * invRayDir;
                // Update parametric interval from slab intersection $t$ values
                if (tNear > tFar)
                    pstd.swap(ref tNear, ref tFar);
                // Update _tFar_ to ensure robust ray--bounds intersection
                tFar *= 1 + 2 * gamma(3);

                t0 = tNear > t0 ? tNear : t0;
                t1 = tFar < t1 ? tFar : t1;
                if (t0 > t1)
                    return false;
            }
            if (hitt0 != null)
                *hitt0 = t0;
            if (hitt1 != null)
                *hitt1 = t1;
            return true;
        }

        public bool IntersectP(Point3f o, Vector3f d, Float raytMax, Vector3f invDir, int* dirIsNeg)
        {
            Bounds3f bounds = this;
            // Check for ray intersection against $x$ and $y$ slabs
            Float tMin = (bounds[dirIsNeg[0]].x - o.x) * invDir.x;
            Float tMax = (bounds[1 - dirIsNeg[0]].x - o.x) * invDir.x;
            Float tyMin = (bounds[dirIsNeg[1]].y - o.y) * invDir.y;
            Float tyMax = (bounds[1 - dirIsNeg[1]].y - o.y) * invDir.y;
            // Update _tMax_ and _tyMax_ to ensure robust bounds intersection
            tMax *= 1 + 2 * gamma(3);
            tyMax *= 1 + 2 * gamma(3);

            if (tMin > tyMax || tyMin > tMax)
                return false;
            if (tyMin > tMin)
                tMin = tyMin;
            if (tyMax < tMax)
                tMax = tyMax;

            // Check for ray intersection against $z$ slab
            Float tzMin = (bounds[dirIsNeg[2]].z - o.z) * invDir.z;
            Float tzMax = (bounds[1 - dirIsNeg[2]].z - o.z) * invDir.z;
            // Update _tzMax_ to ensure robust bounds intersection
            tzMax *= 1 + 2 * gamma(3);

            if (tMin > tzMax || tzMin > tMax)
                return false;
            if (tzMin > tMin)
                tMin = tzMin;
            if (tzMax < tMax)
                tMax = tzMax;

            return (tMin < raytMax) && (tMax > 0);
        }

        public override string ToString() { return $"[ {pMin} - {pMax} ]"; }

        // Bounds3f Public Members
        public Point3f pMin, pMax;
    }

    // Bounds3i Definition
    public unsafe struct Bounds3i : IEquatable<Bounds3i>
    {
        // Bounds3i Public Methods
        public Bounds3i()
        {
            int minNum = int.MinValue;
            int maxNum = int.MaxValue;
            pMin = new Point3i(maxNum, maxNum, maxNum);
            pMax = new Point3i(minNum, minNum, minNum);
        }

        public Bounds3i(Point3i p) { pMin = p; pMax = p; }

        public Bounds3i(Point3i p1, Point3i p2) { pMin = Min(p1, p2); pMax = Max(p1, p2); }

        public Point3i this[int i]
        {
            get { DCHECK(i == 0 || i == 1); return (i == 0) ? pMin : pMax; }
            set { DCHECK(i == 0 || i == 1); if (i == 0) pMin = value; else pMax = value; }
        }

        public Point3i Corner(int corner)
        {
            DCHECK(corner >= 0 && corner < 8);
            return new Point3i(this[(corner & 1)].x, this[(corner & 2) != 0 ? 1 : 0].y, this[(corner & 4) != 0 ? 1 : 0].z);
        }

        public Vector3i Diagonal() { return pMax - pMin; }

        public int SurfaceArea()
        {
            Vector3i d = Diagonal();
            return 2 * (d.x * d.y + d.x * d.z + d.y * d.z);
        }

        public int Volume()
        {
            Vector3i d = Diagonal();
            return d.x * d.y * d.z;
        }

        public int MaxDimension()
        {
            Vector3i d = Diagonal();
            if (d.x > d.y && d.x > d.z)
                return 0;
            else if (d.y > d.z)
                return 1;
            else
                return 2;
        }

        public Point3i Lerp(Point3f t)
        {
            return new Point3i((int)pbrt.Lerp(t.x, pMin.x, pMax.x), (int)pbrt.Lerp(t.y, pMin.y, pMax.y),
                               (int)pbrt.Lerp(t.z, pMin.z, pMax.z));
        }

        public Vector3i Offset(Point3i p)
        {
            Vector3i o = p - pMin;
            if (pMax.x > pMin.x)
                o.x /= pMax.x - pMin.x;
            if (pMax.y > pMin.y)
                o.y /= pMax.y - pMin.y;
            if (pMax.z > pMin.z)
                o.z /= pMax.z - pMin.z;
            return o;
        }

        public void BoundingSphere(Point3i* center, Float* radius)
        {
            *center = (pMin + pMax) / 2;
            *radius = Inside(*center, this) ? Distance(*center, pMax) : 0;
        }

        public bool IsEmpty() { return pMin.x >= pMax.x || pMin.y >= pMax.y || pMin.z >= pMax.z; }
        public bool IsDegenerate() { return pMin.x > pMax.x || pMin.y > pMax.y || pMin.z > pMax.z; }

        public static bool operator ==(Bounds3i a, Bounds3i b) { return b.pMin == a.pMin && b.pMax == a.pMax; }
        public static bool operator !=(Bounds3i a, Bounds3i b) { return b.pMin != a.pMin || b.pMax != a.pMax; }
        public bool Equals(Bounds3i b) { return this == b; }
        public override bool Equals(object o) { return o is Bounds3i b && this == b; }
        public override int GetHashCode() { return HashCode.Combine(pMin, pMax); }

        public override string ToString() { return $"[ {pMin} - {pMax} ]"; }

        // Bounds3i Public Members
        public Point3i pMin, pMax;
    }

    // Bounds2iIterator Definition
    public unsafe struct Bounds2iIterator
    {
        public Bounds2iIterator(Bounds2i b, Point2i pt) { p = pt; bounds = b; }
        public void advance()
        {
            ++p.x;
            if (p.x == bounds.pMax.x)
            {
                p.x = bounds.pMin.x;
                ++p.y;
            }
        }
        public static Bounds2iIterator operator ++(Bounds2iIterator it) { it.advance(); return it; }
        public static bool operator ==(Bounds2iIterator a, Bounds2iIterator bi) { return a.p == bi.p && a.bounds == bi.bounds; }
        public static bool operator !=(Bounds2iIterator a, Bounds2iIterator bi) { return a.p != bi.p || a.bounds != bi.bounds; }
        public override bool Equals(object o) { return o is Bounds2iIterator it && this == it; }
        public override int GetHashCode() { return p.GetHashCode(); }
        public Point2i Current { get { return p; } }
        public Point2i p;
        private Bounds2i bounds;
    }

    public unsafe struct Bounds2iEnumerator
    {
        private Bounds2iIterator it, end;
        private bool started;
        public Bounds2iEnumerator(Bounds2i b) { it = pbrt.begin(b); end = pbrt.end(b); started = false; }
        public bool MoveNext()
        {
            if (started) it.advance(); else started = true;
            return it != end;
        }
        public Point2i Current { get { return it.p; } }
    }

  // pbrt

    // OctahedralVector Definition
    public unsafe struct OctahedralVector
    {
        // OctahedralVector Public Methods
        public OctahedralVector(Vector3f v)
        {
            v /= std.abs(v.x) + std.abs(v.y) + std.abs(v.z);
            if (v.z >= 0)
            {
                x = Encode(v.x);
                y = Encode(v.y);
            }
            else
            {
                // Encode octahedral vector with $z < 0$
                x = Encode((1 - std.abs(v.y)) * Sign(v.x));
                y = Encode((1 - std.abs(v.x)) * Sign(v.y));
            }
        }

        public static explicit operator Vector3f(OctahedralVector o)
        {
            Vector3f v = new Vector3f();
            v.x = -1 + 2 * (o.x / 65535.0f);
            v.y = -1 + 2 * (o.y / 65535.0f);
            v.z = 1 - (std.abs(v.x) + std.abs(v.y));
            // Reparameterize directions in the $z<0$ portion of the octahedron
            if (v.z < 0)
            {
                Float xo = v.x;
                v.x = (1 - std.abs(v.y)) * Sign(xo);
                v.y = (1 - std.abs(xo)) * Sign(v.y);
            }

            return Normalize(v);
        }

        public override string ToString() { return $"[ OctahedralVector x: {x} y: {y} ]"; }

        // OctahedralVector Private Methods
        private static Float Sign(Float v) { return pstd.copysign((Float)1, v); }

        private static uint16_t Encode(Float f)
        {
            return (uint16_t)pstd.round(Clamp((f + 1) / 2, 0, 1) * 65535.0f);
        }

        // OctahedralVector Private Members
        private uint16_t x, y;
    }

    // DirectionCone Definition
    public unsafe struct DirectionCone
    {
        // DirectionCone Public Methods
        public DirectionCone(Vector3f w, Float cosTheta) { this.w = Normalize(w); this.cosTheta = cosTheta; }
        public DirectionCone(Vector3f w) : this(w, 1) { }

        public bool IsEmpty() { return cosTheta == Infinity; }

        public static DirectionCone Empty() { DirectionCone d = new DirectionCone(); d.cosTheta = Infinity; return d; }

        public static DirectionCone EntireSphere() { return new DirectionCone(new Vector3f(0, 0, 1), -1); }

        public Vector3f ClosestVectorInCone(Vector3f wp)
        {
            DCHECK(!IsEmpty());
            wp = Normalize(wp);
            // Return provided vector if it is inside the cone
            if (Dot(wp, w) > cosTheta)
                return wp;

            // Find closest vector by rotating _wp_ until it touches the cone
            Float sinTheta = -SafeSqrt(1 - cosTheta * cosTheta);
            Vector3f a = Cross(wp, w);
            return cosTheta * w +
                   (sinTheta / Length(a)) *
                       new Vector3f(w.x * (wp.y * w.y + wp.z * w.z) - wp.x * (Sqr(w.y) + Sqr(w.z)),
                                    w.y * (wp.x * w.x + wp.z * w.z) - wp.y * (Sqr(w.x) + Sqr(w.z)),
                                    w.z * (wp.x * w.x + wp.y * w.y) - wp.z * (Sqr(w.x) + Sqr(w.y)));
        }

        public override string ToString() { return $"[ DirectionCone w: {w} cosTheta: {cosTheta} ]"; }

        // DirectionCone Public Members
        public Vector3f w;
        public Float cosTheta;
    }

    // Frame Definition
    public unsafe struct Frame
    {
        // Frame Public Methods
        public Frame()
        {
            x = new Vector3f(1, 0, 0);
            y = new Vector3f(0, 1, 0);
            z = new Vector3f(0, 0, 1);
        }

        public Frame(Vector3f x, Vector3f y, Vector3f z)
        {
            this.x = x; this.y = y; this.z = z;
            DCHECK_LT(std.abs(LengthSquared(x) - 1), (Float)1e-4);
            DCHECK_LT(std.abs(LengthSquared(y) - 1), (Float)1e-4);
            DCHECK_LT(std.abs(LengthSquared(z) - 1), (Float)1e-4);
            DCHECK_LT(std.abs(Dot(x, y)), (Float)1e-4);
            DCHECK_LT(std.abs(Dot(y, z)), (Float)1e-4);
            DCHECK_LT(std.abs(Dot(z, x)), (Float)1e-4);
        }

        public static Frame FromXZ(Vector3f x, Vector3f z) { return new Frame(x, Cross(z, x), z); }
        public static Frame FromXY(Vector3f x, Vector3f y) { return new Frame(x, y, Cross(x, y)); }

        public static Frame FromZ(Vector3f z)
        {
            Vector3f x, y;
            CoordinateSystem(z, &x, &y);
            return new Frame(x, y, z);
        }

        public static Frame FromZ(Normal3f z) { return FromZ((Vector3f)z); }

        public static Frame FromX(Vector3f x)
        {
            Vector3f y, z;
            CoordinateSystem(x, &y, &z);
            return new Frame(x, y, z);
        }

        public static Frame FromX(Normal3f x)
        {
            Vector3f y, z;
            CoordinateSystem(x, &y, &z);
            return new Frame((Vector3f)x, y, z);
        }

        public static Frame FromY(Vector3f y)
        {
            Vector3f x, z;
            CoordinateSystem(y, &z, &x);
            return new Frame(x, y, z);
        }

        public static Frame FromY(Normal3f y)
        {
            Vector3f x, z;
            CoordinateSystem(y, &z, &x);
            return new Frame(x, (Vector3f)y, z);
        }

        public Vector3f ToLocal(Vector3f v) { return new Vector3f(Dot(v, x), Dot(v, y), Dot(v, z)); }

        public Normal3f ToLocal(Normal3f n) { return new Normal3f(Dot(n, x), Dot(n, y), Dot(n, z)); }

        public Vector3f FromLocal(Vector3f v) { return v.x * x + v.y * y + v.z * z; }

        public Normal3f FromLocal(Normal3f n) { return (Normal3f)(n.x * x + n.y * y + n.z * z); }

        public override string ToString() { return $"[ Frame x: {x} y: {y} z: {z} ]"; }

        // Frame Public Members
        public Vector3f x, y, z;
    }

}
