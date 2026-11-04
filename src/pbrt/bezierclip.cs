//
// Bezier clipping ray / bicubic Bezier patch intersection.
//
// A port of the intersection code of
//   Tejima, Fujita, Matsuoka: "Direct Ray Tracing of Full-Featured Subdivision Surfaces with Bezier Clipping",
//   Journal of Computer Graphics Techniques 4(1), 2015  (bezier.h, bezierIntersect.h, bilinearIntersect.h, math.h)
// whose bezier-clipping code was contributed by Toru Matsuoka (@ototoi).
//
//   Copyright 2014 Pixar
//
//   Licensed under the Apache License, Version 2.0 (the "Apache License")
//   with the following modification; you may not use this file except in
//   compliance with the Apache License and the following modification to it:
//   Section 6. Trademarks. is deleted and replaced with:
//
//   6. Trademarks. This License does not grant permission to use the trade
//      names, trademarks, service marks, or product names of the Licensor
//      and its affiliates, except as required to comply with Section 4(c) of
//      the License and to reproduce the content of the NOTICE file.
//
//   You may obtain a copy of the Apache License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the Apache License with the above modification is
//   distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
//   KIND, either express or implied. See the Apache License for the specific
//   language governing permissions and limitations under the Apache License.
//
// Modified for Magi: C# port in double precision, fixed order four (bicubic), value-type patches without allocation.
//
using System;

namespace pbrt
{
    // A bicubic Bezier patch of 16 control points in double precision. Control point (i, j), i along u and j along v, is element 4 j + i.
    public unsafe struct BezierPatch4
    {
        public const int Order = 4;
        public const int ControlPointCount = 16;

        public SdfVector Get(int i, int j)
        {
            int k = 3 * (Order * j + i);
            return new SdfVector(c[k], c[k + 1], c[k + 2]);
        }

        public void Set(int i, int j, SdfVector v)
        {
            int k = 3 * (Order * j + i);
            c[k] = v.x;
            c[k + 1] = v.y;
            c[k + 2] = v.z;
        }

        // Evaluate
        public SdfVector Evaluate(double u, double v)
        {
            double* b = stackalloc double[12];
            fixed (double* cp = c)
            {
                for (int i = 0; i < Order; ++i)
                {
                    EvaluateCurve(u, cp + i * 12, 3, b + 3 * i);
                }
            }
            double* r = stackalloc double[3];
            EvaluateCurve(v, b, 3, r);
            return new SdfVector(r[0], r[1], r[2]);
        }

        // Derivative with respect to u
        public SdfVector EvaluateDu(double u, double v)
        {
            double* b = stackalloc double[12];
            fixed (double* cp = c)
            {
                for (int i = 0; i < Order; ++i)
                {
                    EvaluateCurveDerivative(u, cp + i * 12, 3, b + 3 * i);
                }
            }
            double* r = stackalloc double[3];
            EvaluateCurve(v, b, 3, r);
            return new SdfVector(r[0], r[1], r[2]);
        }

        // Derivative with respect to v
        public SdfVector EvaluateDv(double u, double v)
        {
            double* b = stackalloc double[12];
            fixed (double* cp = c)
            {
                for (int i = 0; i < Order; ++i)
                {
                    EvaluateCurve(u, cp + i * 12, 3, b + 3 * i);
                }
            }
            double* r = stackalloc double[3];
            EvaluateCurveDerivative(v, b, 3, r);
            return new SdfVector(r[0], r[1], r[2]);
        }

        // The bounds of the control points, grown by eps
        public void GetMinMax(out SdfVector min, out SdfVector max, double eps)
        {
            double x0 = c[0], y0 = c[1], z0 = c[2], x1 = c[0], y1 = c[1], z1 = c[2];
            for (int i = 1; i < ControlPointCount; ++i)
            {
                double x = c[3 * i], y = c[3 * i + 1], z = c[3 * i + 2];
                x0 = Math.Min(x0, x);
                y0 = Math.Min(y0, y);
                z0 = Math.Min(z0, z);
                x1 = Math.Max(x1, x);
                y1 = Math.Max(y1, y);
                z1 = Math.Max(z1, z);
            }
            min = new SdfVector(x0 - eps, y0 - eps, z0 - eps);
            max = new SdfVector(x1 + eps, y1 + eps, z1 + eps);
        }

        // Applies the affine map [m0 m1 m2 m3; m4 ...; m8 ...] (the last row is 0 0 0 1) to the control points
        public void TransformAffine(double* m)
        {
            for (int i = 0; i < ControlPointCount; ++i)
            {
                double x = c[3 * i], y = c[3 * i + 1], z = c[3 * i + 2];
                c[3 * i] = m[0] * x + m[1] * y + m[2] * z + m[3];
                c[3 * i + 1] = m[4] * x + m[5] * y + m[6] * z + m[7];
                c[3 * i + 2] = m[8] * x + m[9] * y + m[10] * z + m[11];
            }
        }

        // Applies the 3x3 matrix to the control points
        public void TransformLinear(double* m)
        {
            for (int i = 0; i < ControlPointCount; ++i)
            {
                double x = c[3 * i], y = c[3 * i + 1], z = c[3 * i + 2];
                c[3 * i] = m[0] * x + m[1] * y + m[2] * z;
                c[3 * i + 1] = m[3] * x + m[4] * y + m[5] * z;
                c[3 * i + 2] = m[6] * x + m[7] * y + m[8] * z;
            }
        }

        // Splits at u: the patches are [0, u] and [u, 1]
        public void SplitU(out BezierPatch4 a, out BezierPatch4 b, double u)
        {
            a = default;
            b = default;
            fixed (double* ap = a.c, bp = b.c, cp = c)
            {
                for (int i = 0; i < Order; ++i)
                {
                    SplitCurve(ap + i * 12, bp + i * 12, cp + i * 12, 3, u);
                }
            }
        }

        // Splits at v: the patches are [0, v] and [v, 1]
        public void SplitV(out BezierPatch4 a, out BezierPatch4 b, double v)
        {
            a = default;
            b = default;
            fixed (double* ap = a.c, bp = b.c, cp = c)
            {
                for (int i = 0; i < Order; ++i)
                {
                    SplitCurve(ap + i * 3, bp + i * 3, cp + i * 3, 12, v);
                }
            }
        }

        // Splits into four patches at (u, v): 0 is [0, u] x [0, v], 1 is [u, 1] x [0, v], 2 is [0, u] x [v, 1], 3 is [u, 1] x [v, 1]
        public void Split4(out BezierPatch4 p0, out BezierPatch4 p1, out BezierPatch4 p2, out BezierPatch4 p3, double u, double v)
        {
            BezierPatch4 left, right;
            SplitU(out left, out right, u);
            left.SplitV(out p0, out p2, v);
            right.SplitV(out p1, out p3, v);
        }

        // The patch cropped to [u0, u1] x [v0, v1], with the reversal invariant (watertight) cropping
        public BezierPatch4 Crop(double u0, double u1, double v0, double v1)
        {
            BezierPatch4 tmp = default;
            BezierPatch4 result = default;
            double* tp = tmp.c;
            double* rp = result.c;
            fixed (double* cp = c)
            {
                for (int i = 0; i < Order; ++i)
                {
                    CropCurve(tp + i * 12, cp + i * 12, 3, u0, u1);
                }
                for (int i = 0; i < Order; ++i)
                {
                    CropCurve(rp + i * 3, tp + i * 3, 12, v0, v1);
                }
            }
            return result;
        }

        public SdfVector GetLv()
        {
            return Get(0, Order - 1) - Get(0, 0) + Get(Order - 1, Order - 1) - Get(Order - 1, 0);
        }

        public SdfVector GetLu()
        {
            return Get(Order - 1, 0) - Get(0, 0) + Get(Order - 1, Order - 1) - Get(0, Order - 1);
        }

        // Cubic Bernstein basis in the order of the C++ templates (binomial * pow(1 - t, 3 - I) * pow(t, I))
        private static double Bernstein(double t, int i)
        {
            int binomial = i == 0 || i == 3 ? 1 : 3;
            return binomial * Math.Pow(1 - t, 3 - i) * Math.Pow(t, i);
        }

        // A curve of four elements that are `stride` doubles apart, each with three components
        private static void EvaluateCurve(double t, double* p, int stride, double* result)
        {
            double b0 = Bernstein(t, 0), b1 = Bernstein(t, 1), b2 = Bernstein(t, 2), b3 = Bernstein(t, 3);
            for (int k = 0; k < 3; ++k)
            {
                result[k] = p[3 * stride + k] * b3 + (p[2 * stride + k] * b2 + (p[1 * stride + k] * b1 + p[k] * b0));
            }
        }

        private static void EvaluateCurveDerivative(double t, double* p, int stride, double* result)
        {
            double t2 = t * t;
            double w0 = 3 * t2 * -1 + 2 * t * 3 + -3;
            double w1 = 3 * t2 * 3 + 2 * t * -6 + 3;
            double w2 = 3 * t2 * -3 + 2 * t * 3;
            double w3 = 3 * t2 * 1;
            for (int k = 0; k < 3; ++k)
            {
                result[k] = p[k] * w0 + p[stride + k] * w1 + p[2 * stride + k] * w2 + p[3 * stride + k] * w3;
            }
        }

        // De Casteljau split of a cubic: a gets [0, t] and b gets [t, 1]. The elements are `stride` doubles apart.
        private static void SplitCurve(double* a, double* b, double* p, int stride, double t)
        {
            double* current = stackalloc double[12];
            double* next = stackalloc double[12];
            for (int j = 0; j < Order; ++j)
            {
                for (int k = 0; k < 3; ++k)
                {
                    current[3 * j + k] = p[j * stride + k];
                }
            }
            for (int level = 0; level < Order; ++level)
            {
                int tn = Order - 1 - level;
                for (int k = 0; k < 3; ++k)
                {
                    a[level * stride + k] = current[k];
                    b[tn * stride + k] = current[3 * tn + k];
                }
                double s = 1 - t;
                t = 1 - s;
                for (int j = 0; j < tn; ++j)
                {
                    for (int k = 0; k < 3; ++k)
                    {
                        next[3 * j + k] = s * current[3 * j + k] + t * current[3 * (j + 1) + k];
                    }
                }
                for (int j = 0; j < tn; ++j)
                {
                    for (int k = 0; k < 3; ++k)
                    {
                        current[3 * j + k] = next[3 * j + k];
                    }
                }
            }
        }

        // Watertight, reversal invariant crop of a cubic to [s, t]
        private static void CropCurve(double* r, double* p, int stride, double s, double t)
        {
            // p0    p1   p2    p3
            // 0----s--------t---1
            // 1----T--------S---0
            double T = 1 - s;
            double S = 1 - t;
            s = 1 - T;
            t = 1 - S;
            for (int k = 0; k < 3; ++k)
            {
                double p0 = p[k], p1 = p[stride + k], p2 = p[2 * stride + k], p3 = p[3 * stride + k];
                r[k] = (p0 * (T * T) * T + p3 * (s * s) * s) + (p1 * (s * T) * (3 * T) + p2 * (s * s) * (3 * T));
                r[stride + k] = (p0 * (T * T) * S + p3 * (s * s) * t) + (p1 * T * (2 * (S * s) + T * t) + p2 * s * (2 * (t * T) + (s * S)));
                r[2 * stride + k] = (p3 * (t * t) * s + p0 * (S * S) * T) + (p2 * t * (2 * (s * S) + t * T) + p1 * S * (2 * (T * t) + (S * s)));
                r[3 * stride + k] = (p3 * (t * t) * t + p0 * (S * S) * S) + (p2 * (S * t) * (3 * t) + p1 * (S * S) * (3 * t));
            }
        }

        public fixed double c[48];
    }

    // The result of a Bezier clipping test
    public struct BezierClipHit
    {
        public double t;
        public double u;
        public double v;
        public int clipLevel;
    }

    // The parameters of the Bezier clipping search
    public struct BezierClipOptions
    {
        public static BezierClipOptions Default()
        {
            BezierClipOptions o = new BezierClipOptions();
            o.maxLevel = 10;
            o.uvMargin = 1;
            o.useBezierClip = true;
            o.conservative = false;
            return o;
        }

        // Clipping stops at this level (the source's DEFAULT_MAX_LEVEL)
        public int maxLevel;
        // The bilinear leaf accepts hits this far outside its parameter range (the source sets the float uvMargin to true, 1)
        public double uvMargin;
        public bool useBezierClip;
        // Test the four children at (1/2, 1/2) when a ray fails near the border of the patch (the conservative algorithm for transition patches)
        public bool conservative;
    }

    // Intersects rays with one bicubic Bezier patch with Bezier clipping
    public unsafe sealed class BezierPatchIntersection
    {
        public BezierPatchIntersection(in BezierPatch4 patch)
        {
            this.patch = patch;
            options = BezierClipOptions.Default();
            patch.GetMinMax(out boundsMin, out boundsMax, 0);
            SdfVector extent = boundsMax - boundsMin;
            double pad = Math.Max(1e-12, 1e-6 * extent.Length());
            boundsMin = boundsMin - new SdfVector(pad, pad, pad);
            boundsMax = boundsMax + new SdfVector(pad, pad, pad);
        }

        public BezierClipOptions Options
        {
            get { return options; }
            set { options = value; }
        }

        public SdfVector BoundsMin
        {
            get { return boundsMin; }
        }

        public SdfVector BoundsMax
        {
            get { return boundsMax; }
        }

        // The epsilon (the size of the final bilinear patch) from the distance to the patch and the angular size of a pixel, as the source does
        // from the ray differentials: a quarter of the smaller of the footprints
        public static double ComputeEpsilon(double distance, double pixelSpread, double fallback)
        {
            if (pixelSpread > 0 && distance > 0)
            {
                return Math.Max(1e-12, distance * pixelSpread * 0.25);
            }
            return fallback;
        }

        // Tests the ray (origin, direction of unit length) in [tmin, tmax]
        public bool Test(SdfVector origin, SdfVector direction, double tmin, double tmax, double eps, out BezierClipHit hit)
        {
            hit = default;
            if (!IntersectBounds(origin, direction, ref tmin, ref tmax))
            {
                return false;
            }
            double* m = stackalloc double[12];
            ZAlign(origin, direction, m);
            BezierPatch4 aligned = patch;
            aligned.TransformAffine(m);

            UVT uvt = default;
            uvt.tolerance = eps;
            bool result = false;
            if (TestBezierPatch(ref uvt, in aligned, tmin, tmax, eps))
            {
                hit.t = uvt.t;
                hit.u = uvt.u;
                hit.v = uvt.v;
                hit.clipLevel = uvt.level;
                tmax = uvt.t;
                result = true;
            }
            if (uvt.failFlag == 0 || !options.conservative)
            {
                return result;
            }

            // Test against the four children too (Split4 orders them (low u, low v), (high u, low v), (low u, high v), (high u, high v)), where the ray failed near the border of the patch (a crack between a transition patch and its neighbor)
            double* offsets = stackalloc double[8];
            offsets[0] = 0.0;
            offsets[1] = 0.0;
            offsets[2] = 0.5;
            offsets[3] = 0.0;
            offsets[4] = 0.0;
            offsets[5] = 0.5;
            offsets[6] = 0.5;
            offsets[7] = 0.5;
            BezierPatch4* children = stackalloc BezierPatch4[4];
            patch.Split4(out children[0], out children[1], out children[2], out children[3], 0.5, 0.5);
            for (int i = 0; i < 4; ++i)
            {
                children[i].TransformAffine(m);
            }
            int ni = -1;
            for (int i = 0; i < 4; ++i)
            {
                if (TestBezierPatch(ref uvt, in children[i], tmin, tmax, eps))
                {
                    tmax = uvt.t;
                    ni = i;
                }
            }
            if (ni >= 0)
            {
                hit.t = uvt.t;
                hit.u = uvt.u * 0.5 + offsets[2 * ni + 0];
                hit.v = uvt.v * 0.5 + offsets[2 * ni + 1];
                hit.clipLevel = uvt.level;
                result = true;
            }
            return result;
        }

        private struct UVT
        {
            public double u;
            public double v;
            public double t;
            public int level;
            public int failFlag;
            public double tolerance;
        }

        // The matrix that moves the ray to the origin with +z along the ray (the source's matrix4t(org, dir))
        private static void ZAlign(SdfVector org, SdfVector z, double* m)
        {
            int plane = 0;
            if (Math.Abs(z.y) < Math.Abs(z[plane]))
            {
                plane = 1;
            }
            if (Math.Abs(z.z) < Math.Abs(z[plane]))
            {
                plane = 2;
            }
            SdfVector x = plane == 0 ? new SdfVector(1, 0, 0) : (plane == 1 ? new SdfVector(0, 1, 0) : new SdfVector(0, 0, 1));
            SdfVector y = SdfVector.Cross(z, x).Normalized();
            x = SdfVector.Cross(y, z);
            // rot * translation(-org)
            m[0] = x.x;
            m[1] = x.y;
            m[2] = x.z;
            m[3] = x.x * -org.x + x.y * -org.y + x.z * -org.z + 0 * 1;
            m[4] = y.x;
            m[5] = y.y;
            m[6] = y.z;
            m[7] = y.x * -org.x + y.y * -org.y + y.z * -org.z + 0 * 1;
            m[8] = z.x;
            m[9] = z.y;
            m[10] = z.z;
            m[11] = z.x * -org.x + z.y * -org.y + z.z * -org.z + 0 * 1;
        }

        private bool IntersectBounds(SdfVector org, SdfVector dir, ref double tmin, ref double tmax)
        {
            for (int axis = 0; axis < 3; ++axis)
            {
                double invDir = 1 / dir[axis];
                double near = (boundsMin[axis] - org[axis]) * invDir;
                double far = (boundsMax[axis] - org[axis]) * invDir;
                if (near > far)
                {
                    double swap = near;
                    near = far;
                    far = swap;
                }
                tmin = Math.Max(tmin, near);
                tmax = Math.Min(tmax, far);
            }
            return tmin <= tmax;
        }

        private bool TestBezierPatch(ref UVT info, in BezierPatch4 aligned, double zmin, double zmax, double eps)
        {
            SdfVector min, max;
            aligned.GetMinMax(out min, out max, eps * 1e-3);
            if (0 < min.x || max.x < 0)
            {
                return false;
            }
            if (0 < min.y || max.y < 0)
            {
                return false;
            }
            if (max.z < zmin || zmax < min.z)
            {
                return false;
            }
            return TestBezierClipRangeU(ref info, in aligned, 0, 1, 0, 1, zmin, zmax, 0, options.maxLevel, eps);
        }

        // Rotates the patch about z so that the u direction of the patch is along x (to determine the clipping range in u)
        private static void RotateU(ref BezierPatch4 p)
        {
            Rotate(ref p, p.GetLv());
        }

        private static void RotateV(ref BezierPatch4 p)
        {
            Rotate(ref p, p.GetLu());
        }

        private static void Rotate(ref BezierPatch4 p, SdfVector dx)
        {
            double inverseLength = 1.0 / Math.Sqrt(dx.x * dx.x + dx.y * dx.y);
            double x0 = dx.x * inverseLength;
            double x1 = dx.y * inverseLength;
            double x2 = 0;
            double y0 = -x1;
            double y1 = x0;
            double y2 = 0;
            double* m = stackalloc double[9];
            m[0] = x0;
            m[1] = x1;
            m[2] = x2;
            m[3] = y0;
            m[4] = y1;
            m[5] = y2;
            m[6] = 0;
            m[7] = 0;
            m[8] = 1;
            p.TransformLinear(m);
        }

        private static bool IsEps(SdfVector min, SdfVector max, double eps)
        {
            double xw = max.x - min.x;
            double yw = max.y - min.y;
            return xw <= eps && yw <= eps;
        }

        // Clipping starts after four levels of splitting (two per direction) for order four
        private static bool IsClip(int level)
        {
            return 2 * 2 <= level;
        }

        private static double Lerp(double a, double b, double u)
        {
            double t = 1 - (1 - u);
            double s = 1 - t;
            return s * a + t * b;
        }

        private const double UvEps = 1.0 / 32.0;

        private static bool ScanMinMax(double* py)
        {
            int nUpper = 0, nLower = 0;
            for (int i = 0; i < BezierPatch4.Order; ++i)
            {
                if (py[i] > 0)
                {
                    nUpper++;
                }
                else
                {
                    nLower++;
                }
                if (nUpper != 0 && nLower != 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static double Slope(double ax, double ay, double bx, double by)
        {
            double d0 = bx - ax;
            double d1 = by - ay;
            return Math.Abs(d0 / d1);
        }

        private static double Dist(double p0x, double p0y, double p1x, double p1y)
        {
            double a = Math.Abs(p0y);
            double b = Math.Abs(p1y);
            double h = a + b;
            return p0x + a * (p1x - p0x) / h;
        }

        // The parameters where the convex hull of the curve (x = i / 3, y = py[i]) crosses y = 0
        private static int ScanConvex(double* ts, double* px, double* py)
        {
            if (!ScanMinMax(py))
            {
                return 0;
            }
            int n = 0;
            int last = BezierPatch4.Order - 1;
            {
                int k = -1;
                int l = -1;
                double current = double.MaxValue;
                for (int i = 1; i < BezierPatch4.Order; ++i)
                {
                    if (py[i] * py[0] < 0)
                    {
                        double s = Slope(px[i], py[i], px[0], py[0]);
                        if (s < current)
                        {
                            current = s;
                            k = i;
                        }
                    }
                }
                if (k >= 0)
                {
                    current = 0;
                    for (int i = 0; i < k; ++i)
                    {
                        if (py[i] * py[k] < 0)
                        {
                            double s = Slope(px[i], py[i], px[k], py[k]);
                            if (current < s)
                            {
                                current = s;
                                l = i;
                            }
                        }
                    }
                    if (l >= 0)
                    {
                        ts[n++] = Dist(px[l], py[l], px[k], py[k]);
                    }
                }
            }
            {
                int k = -1;
                int l = -1;
                double current = double.MaxValue;
                for (int i = 0; i < last; ++i)
                {
                    if (py[i] * py[last] < 0)
                    {
                        double s = Slope(px[i], py[i], px[last], py[last]);
                        if (s < current)
                        {
                            current = s;
                            k = i;
                        }
                    }
                }
                if (k >= 0)
                {
                    current = 0;
                    for (int i = k + 1; i < BezierPatch4.Order; ++i)
                    {
                        if (py[i] * py[k] < 0)
                        {
                            double s = Slope(px[i], py[i], px[k], py[k]);
                            if (current < s)
                            {
                                current = s;
                                l = i;
                            }
                        }
                    }
                    if (l >= 0)
                    {
                        ts[n++] = Dist(px[k], py[k], px[l], py[l]);
                    }
                }
            }
            return n;
        }

        private static bool XCheck(double* rng, double* px, double* py)
        {
            double* t = stackalloc double[4];
            t[0] = 0;
            t[1] = 0;
            t[2] = 0;
            t[3] = 0;
            int nn = ScanConvex(t, px, py);
            if (nn != 0)
            {
                double t0 = t[0];
                double t1 = t[0];
                for (int i = 1; i < nn; i++)
                {
                    t0 = Math.Min(t0, t[i]);
                    t1 = Math.Max(t1, t[i]);
                }
                rng[0] = t0;
                rng[1] = t1;
                return true;
            }
            return false;
        }

        private static bool GetRangeU(double* output, in BezierPatch4 patch)
        {
            double t0 = 1;
            double t1 = 0;
            double* px = stackalloc double[4];
            double* py = stackalloc double[4];
            double delta = 1.0 / (BezierPatch4.Order - 1);
            for (int i = 0; i < BezierPatch4.Order; ++i)
            {
                px[i] = i * delta;
            }
            double* rng = stackalloc double[2];
            for (int j = 0; j < BezierPatch4.Order; ++j)
            {
                for (int i = 0; i < BezierPatch4.Order; ++i)
                {
                    py[i] = patch.Get(i, j).y;
                }
                if (XCheck(rng, px, py))
                {
                    t0 = Math.Min(t0, rng[0]);
                    t1 = Math.Max(t1, rng[1]);
                }
                else
                {
                    return false;
                }
            }
            output[0] = t0;
            output[1] = t1;
            return true;
        }

        private static bool GetRangeV(double* output, in BezierPatch4 patch)
        {
            double t0 = 1;
            double t1 = 0;
            double* px = stackalloc double[4];
            double* py = stackalloc double[4];
            double delta = 1.0 / (BezierPatch4.Order - 1);
            for (int i = 0; i < BezierPatch4.Order; ++i)
            {
                px[i] = i * delta;
            }
            double* rng = stackalloc double[2];
            for (int i = 0; i < BezierPatch4.Order; ++i)
            {
                for (int j = 0; j < BezierPatch4.Order; ++j)
                {
                    py[j] = patch.Get(i, j).y;
                }
                if (XCheck(rng, px, py))
                {
                    t0 = Math.Min(t0, rng[0]);
                    t1 = Math.Max(t1, rng[1]);
                }
                else
                {
                    return false;
                }
            }
            output[0] = t0;
            output[1] = t1;
            return true;
        }

        private static int FailFlag(double u0, double u1, double v0, double v1)
        {
            return ((u0 == 0.0 ? 1 : 0) << 0) | ((u1 == 1.0 ? 1 : 0) << 1) | ((v0 == 0.0 ? 1 : 0) << 2) | ((v1 == 1.0 ? 1 : 0) << 3);
        }

        private bool TestBezierClipRangeU(ref UVT info, in BezierPatch4 patch, double u0, double u1, double v0, double v1, double zmin, double zmax,
                                          int level, int maxLevel, double eps)
        {
            BezierPatch4 mpatch = patch.Crop(u0, u1, v0, v1);
            BezierPatch4 tpatch = mpatch;
            RotateU(ref tpatch);

            SdfVector min, max;
            tpatch.GetMinMax(out min, out max, eps * 1e-3);
            if (0 < min.x || max.x < 0 || 0 < min.y || max.y < 0 || max.z < zmin || zmax < min.z)
            {
                info.failFlag |= FailFlag(u0, u1, v0, v1);
                return false;
            }

            bool clip = IsClip(level);
            if (clip && (IsEps(min, max, eps) || level >= maxLevel))
            {
                return TestBezierClipL(ref info, in mpatch, u0, u1, v0, v1, zmin, zmax, level);
            }
            double tw = 1;
            double tt0 = 0;
            double tt1 = 1;
            if (options.useBezierClip && clip)
            {
                double* rng = stackalloc double[2];
                if (GetRangeU(rng, in tpatch))
                {
                    tt0 = rng[0];
                    tt1 = rng[1];
                    tw = tt1 - tt0;
                }
            }
            if (tw >= 0.4)
            {
                double um = (u0 + u1) * 0.5;
                um = 1.0 - (1.0 - um);
                bool result = false;
                for (int i = 0; i < 2; ++i)
                {
                    double a = i == 0 ? u0 : um;
                    double b = i == 0 ? um : u1;
                    if (TestBezierClipRangeV(ref info, in patch, a, b, v0, v1, zmin, zmax, level + 1, maxLevel, eps))
                    {
                        zmax = info.t;
                        result = true;
                    }
                }
                return result;
            }
            tt0 = Math.Max(0.0, tt0 - UvEps);
            tt1 = Math.Min(tt1 + UvEps, 1.0);
            return TestBezierClipRangeV(ref info, in patch, Lerp(u0, u1, tt0), Lerp(u0, u1, tt1), v0, v1, zmin, zmax, level + 1, maxLevel, eps);
        }

        private bool TestBezierClipRangeV(ref UVT info, in BezierPatch4 patch, double u0, double u1, double v0, double v1, double zmin, double zmax,
                                          int level, int maxLevel, double eps)
        {
            BezierPatch4 mpatch = patch.Crop(u0, u1, v0, v1);
            BezierPatch4 tpatch = mpatch;
            RotateV(ref tpatch);

            SdfVector min, max;
            tpatch.GetMinMax(out min, out max, eps * 1e-3);
            if (0 < min.x || max.x < 0 || 0 < min.y || max.y < 0 || max.z < zmin || zmax < min.z)
            {
                info.failFlag |= FailFlag(u0, u1, v0, v1);
                return false;
            }

            bool clip = IsClip(level);
            if (clip && (IsEps(min, max, eps) || level >= maxLevel))
            {
                return TestBezierClipL(ref info, in mpatch, u0, u1, v0, v1, zmin, zmax, level);
            }
            double tw = 1;
            double tt0 = 0;
            double tt1 = 1;
            if (options.useBezierClip && clip)
            {
                double* rng = stackalloc double[2];
                if (GetRangeV(rng, in tpatch))
                {
                    tt0 = rng[0];
                    tt1 = rng[1];
                    tw = tt1 - tt0;
                }
            }
            if (tw >= 0.4)
            {
                double vm = (v0 + v1) * 0.5;
                vm = 1.0 - (1.0 - vm);
                bool result = false;
                for (int i = 0; i < 2; ++i)
                {
                    double a = i == 0 ? v0 : vm;
                    double b = i == 0 ? vm : v1;
                    if (TestBezierClipRangeU(ref info, in patch, u0, u1, a, b, zmin, zmax, level + 1, maxLevel, eps))
                    {
                        zmax = info.t;
                        result = true;
                    }
                }
                return result;
            }
            tt0 = Math.Max(0.0, tt0 - UvEps);
            tt1 = Math.Min(tt1 + UvEps, 1.0);
            return TestBezierClipRangeU(ref info, in patch, u0, u1, Lerp(v0, v1, tt0), Lerp(v0, v1, tt1), zmin, zmax, level + 1, maxLevel, eps);
        }

        // The leaf: the cropped patch is approximated by 3x3 bilinear patches
        private bool TestBezierClipL(ref UVT info, in BezierPatch4 patch, double u0, double u1, double v0, double v1, double zmin, double zmax, int level)
        {
            bool result = false;
            int nPu = BezierPatch4.Order - 1;
            int nPv = BezierPatch4.Order - 1;
            double du = 1.0 / nPu;
            double dv = 1.0 / nPv;
            double uu = 0;
            double vv = 0;
            SdfVector* p = stackalloc SdfVector[4];
            for (int j = 0; j < nPv; ++j)
            {
                for (int i = 0; i < nPu; ++i)
                {
                    p[0] = patch.Get(i, j);
                    p[1] = patch.Get(i + 1, j);
                    p[2] = patch.Get(i, j + 1);
                    p[3] = patch.Get(i + 1, j + 1);
                    double t, u, v;
                    if (TestBilinearPatch(out t, out u, out v, p, zmin, zmax, options.uvMargin))
                    {
                        u = Lerp(i * du, (i + 1) * du, u);
                        v = Lerp(j * dv, (j + 1) * dv, v);
                        // The bilinear leaf accepts hits beyond its border (the margin) and clamps them: the clamped point of the patch must still be on the ray
                        SdfVector clamped = patch.Evaluate(u, v);
                        if (clamped.x * clamped.x + clamped.y * clamped.y > info.tolerance * info.tolerance)
                        {
                            continue;
                        }
                        uu = u;
                        vv = v;
                        zmax = t;
                        u = Lerp(u0, u1, u);
                        v = Lerp(v0, v1, v);
                        info.u = u;
                        info.v = v;
                        info.t = t;
                        info.level = level;
                        result = true;
                    }
                }
            }
            if (result)
            {
                SdfVector point = patch.Evaluate(uu, vv);
                info.t = point.z;
            }
            else
            {
                info.failFlag |= FailFlag(u0, u1, v0, v1);
            }
            return result;
        }

        // Ramsey et al. 2004: the intersection of the z axis with a bilinear patch
        private static bool TestBilinearPatch(out double t, out double u, out double v, SdfVector* p, double tmin, double tmax, double uvMargin)
        {
            t = 0;
            u = 0;
            v = 0;
            SdfVector p00 = p[0];
            SdfVector p10 = p[1];
            SdfVector p01 = p[2];
            SdfVector p11 = p[3];
            SdfVector a = p11 - p10 - p01 + p00;
            SdfVector b = p10 - p00;
            SdfVector c = p01 - p00;
            SdfVector d = p00;

            double A1 = a.x, B1 = b.x, C1 = c.x, D1 = d.x;
            double A2 = a.y, B2 = b.y, C2 = c.y, D2 = d.y;
            double F1 = A2 * C1 - A1 * C2;
            double F2 = A2 * D1 - A1 * D2 + B2 * C1 - B1 * C2;
            double F3 = B2 * D1 - B1 * D2;

            double* root = stackalloc double[2];
            root[0] = 0;
            root[1] = 0;
            int count = Solve2(root, F1, F2, F3);
            if (count == 0)
            {
                return false;
            }
            bool result = false;
            for (int i = 0; i < count; ++i)
            {
                double vv = root[i];
                if (0 - uvMargin <= vv && vv <= 1 + uvMargin)
                {
                    vv = Math.Max(0.0, Math.Min(vv, 1.0));
                    double uu = ComputeU(A1, A2, B1, B2, C1, C2, D1, D2, vv);
                    if (0 - uvMargin <= uu && uu <= 1 + uvMargin)
                    {
                        uu = Math.Max(0.0, Math.Min(uu, 1.0));
                        double tt = ((uu * vv) * a.z + uu * b.z + vv * c.z + d.z) * 1.0;
                        if (tmin <= tt && tt <= tmax)
                        {
                            t = tt;
                            u = uu;
                            v = vv;
                            tmax = t;
                            result = true;
                        }
                    }
                }
            }
            return result;
        }

        private static int Solve2(double* root, double A, double B, double C)
        {
            if (Math.Abs(A) <= 2.2250738585072014e-308)
            {
                if (Math.Abs(B) <= 2.2250738585072014e-308)
                {
                    return 0;
                }
                root[0] = -C / B;
                return 1;
            }
            double D = B * B - 4 * A * C;
            if (D < 0)
            {
                return 0;
            }
            if (D == 0)
            {
                root[0] = -0.5 * B / A;
                return 1;
            }
            double x1 = (Math.Abs(B) + Math.Sqrt(D)) / (2.0 * A);
            if (B >= 0)
            {
                x1 = -x1;
            }
            double x2 = C / (A * x1);
            if (x1 > x2)
            {
                double swap = x1;
                x1 = x2;
                x2 = swap;
            }
            root[0] = x1;
            root[1] = x2;
            return 2;
        }

        private static double ComputeU(double A1, double A2, double B1, double B2, double C1, double C2, double D1, double D2, double v)
        {
            double a = v * A2 + B2;
            double b = v * (A2 - A1) + B2 - B1;
            if (Math.Abs(b) >= Math.Abs(a))
            {
                return (v * (C1 - C2) + D1 - D2) / b;
            }
            return (-v * C2 - D2) / a;
        }

        private readonly BezierPatch4 patch;
        private BezierClipOptions options;
        private SdfVector boundsMin;
        private SdfVector boundsMax;
    }
}
