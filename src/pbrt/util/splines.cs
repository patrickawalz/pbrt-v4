// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Magi: C# port of pbrt-v4. File: util/splines.h

using System;

namespace pbrt
{
    public static unsafe partial class Globals
    {
        // Bezier Inline Functions
        // (the C++ templates over P are expanded for Float and Point3f)
        public static Point3f BlossomCubicBezier(pstd.span<Point3f> p, Float u0, Float u1, Float u2)
        {
            Point3f* a = stackalloc Point3f[3];
            a[0] = Lerp(u0, p[0], p[1]); a[1] = Lerp(u0, p[1], p[2]); a[2] = Lerp(u0, p[2], p[3]);
            Point3f* b = stackalloc Point3f[2];
            b[0] = Lerp(u1, a[0], a[1]); b[1] = Lerp(u1, a[1], a[2]);
            return Lerp(u2, b[0], b[1]);
        }

        public static Float BlossomCubicBezier(pstd.span<Float> p, Float u0, Float u1, Float u2)
        {
            Float* a = stackalloc Float[3];
            a[0] = Lerp(u0, p[0], p[1]); a[1] = Lerp(u0, p[1], p[2]); a[2] = Lerp(u0, p[2], p[3]);
            Float* b = stackalloc Float[2];
            b[0] = Lerp(u1, a[0], a[1]); b[1] = Lerp(u1, a[1], a[2]);
            return Lerp(u2, b[0], b[1]);
        }

        public static Point3f EvaluateCubicBezier(pstd.span<Point3f> cp, Float u)
        {
            return BlossomCubicBezier(cp, u, u, u);
        }

        public static Float EvaluateCubicBezier(pstd.span<Float> cp, Float u)
        {
            return BlossomCubicBezier(cp, u, u, u);
        }

        public static Point3f EvaluateCubicBezier(pstd.span<Point3f> cp, Float u, Vector3f* deriv)
        {
            Point3f* cp1 = stackalloc Point3f[3];
            cp1[0] = Lerp(u, cp[0], cp[1]); cp1[1] = Lerp(u, cp[1], cp[2]); cp1[2] = Lerp(u, cp[2], cp[3]);
            Point3f* cp2 = stackalloc Point3f[2];
            cp2[0] = Lerp(u, cp1[0], cp1[1]); cp2[1] = Lerp(u, cp1[1], cp1[2]);
            if (deriv != null)
            {
                // Compute B\'ezier curve derivative at $u$
                if (LengthSquared(cp2[1] - cp2[0]) > 0)
                    *deriv = 3 * (cp2[1] - cp2[0]);
                else
                    *deriv = cp[3] - cp[0];
            }
            return Lerp(u, cp2[0], cp2[1]);
        }

        public static pstd.array7<Point3f> SubdivideCubicBezier(pstd.span<Point3f> cp)
        {
            return new pstd.array7<Point3f>(cp[0],
                                            (cp[0] + cp[1]) / 2,
                                            (cp[0] + 2 * cp[1] + cp[2]) / 4,
                                            (cp[0] + 3 * cp[1] + 3 * cp[2] + cp[3]) / 8,
                                            (cp[1] + 2 * cp[2] + cp[3]) / 4,
                                            (cp[2] + cp[3]) / 2,
                                            cp[3]);
        }

        public static pstd.array4<Point3f> CubicBezierControlPoints(pstd.span<Point3f> cp, Float uMin, Float uMax)
        {
            return new pstd.array4<Point3f>(BlossomCubicBezier(cp, uMin, uMin, uMin),
                                            BlossomCubicBezier(cp, uMin, uMin, uMax),
                                            BlossomCubicBezier(cp, uMin, uMax, uMax),
                                            BlossomCubicBezier(cp, uMax, uMax, uMax));
        }

        public static Bounds3f BoundCubicBezier(pstd.span<Point3f> cp)
        {
            return Union(new Bounds3f(cp[0], cp[1]), new Bounds3f(cp[2], cp[3]));
        }

        public static Bounds3f BoundCubicBezier(pstd.span<Point3f> cp, Float uMin, Float uMax)
        {
            if (uMin == 0 && uMax == 1)
                return BoundCubicBezier(cp);
            pstd.array4<Point3f> cpSeg = CubicBezierControlPoints(cp, uMin, uMax);
            return BoundCubicBezier(new pstd.span<Point3f>(new Point3f[] { cpSeg[0], cpSeg[1], cpSeg[2], cpSeg[3] }));
        }

        public static pstd.array4<Point3f> ElevateQuadraticBezierToCubic(pstd.span<Point3f> cp)
        {
            return new pstd.array4<Point3f>(cp[0], Lerp(2.0f / 3.0f, cp[0], cp[1]), Lerp(1.0f / 3.0f, cp[1], cp[2]), cp[2]);
        }

        public static pstd.array3<Point3f> QuadraticBSplineToBezier(pstd.span<Point3f> cp)
        {
            // We can compute equivalent Bezier control points via some blossoming.
            // We have three control points and a uniform knot vector; we will label
            // the points p01, p12, and p23.  We want the Bezier control points of
            // the equivalent curve, which are p11, p12, and p22.  We already have
            // p12.
            Point3f p11 = Lerp((Float)0.5, cp[0], cp[1]);
            Point3f p22 = Lerp((Float)0.5, cp[1], cp[2]);
            return new pstd.array3<Point3f>(p11, cp[1], p22);
        }

        public static pstd.array4<Point3f> CubicBSplineToBezier(pstd.span<Point3f> cp)
        {
            // Blossom from p012, p123, p234, and p345 to the Bezier control points
            // p222, p223, p233, and p333.
            // https://people.eecs.berkeley.edu/~sequin/CS284/IMGS/cubicbsplinepoints.gif
            Point3f p012 = cp[0], p123 = cp[1], p234 = cp[2], p345 = cp[3];

            Point3f p122 = Lerp(2.0f / 3.0f, p012, p123);
            Point3f p223 = Lerp(1.0f / 3.0f, p123, p234);
            Point3f p233 = Lerp(2.0f / 3.0f, p123, p234);
            Point3f p334 = Lerp(1.0f / 3.0f, p234, p345);

            Point3f p222 = Lerp(0.5f, p122, p223);
            Point3f p333 = Lerp(0.5f, p233, p334);

            return new pstd.array4<Point3f>(p222, p223, p233, p333);
        }
    }
}
