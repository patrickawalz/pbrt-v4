// (not in pbrt-v4) bezierclip_test.cs
//
// Tests of the Bezier clipping intersection (bezierclip.cs) against a brute force intersection of a dense tessellation.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class BezierClipTests
    {
        private static BezierPatch4 DomePatch(Random random, double bump)
        {
            BezierPatch4 patch = default;
            for (int j = 0; j < 4; ++j)
            {
                for (int i = 0; i < 4; ++i)
                {
                    double x = i / 3.0 - 0.5, y = j / 3.0 - 0.5;
                    double z = 0.5 - (x * x + y * y) + bump * (random.NextDouble() - 0.5);
                    patch.Set(i, j, new SdfVector(x + 0.1 * bump * (random.NextDouble() - 0.5), y + 0.1 * bump * (random.NextDouble() - 0.5), z));
                }
            }
            return patch;
        }

        // Moeller-Trumbore
        private static bool Triangle(SdfVector o, SdfVector d, SdfVector a, SdfVector b, SdfVector c, out double t)
        {
            t = 0;
            SdfVector e1 = b - a, e2 = c - a;
            SdfVector p = SdfVector.Cross(d, e2);
            double det = SdfVector.Dot(e1, p);
            if (Math.Abs(det) < 1e-18)
            {
                return false;
            }
            SdfVector s = o - a;
            double u = SdfVector.Dot(s, p) / det;
            if (u < 0 || u > 1)
            {
                return false;
            }
            SdfVector q = SdfVector.Cross(s, e1);
            double v = SdfVector.Dot(d, q) / det;
            if (v < 0 || u + v > 1)
            {
                return false;
            }
            t = SdfVector.Dot(e2, q) / det;
            return t > 0;
        }

        private static bool BruteForce(in BezierPatch4 patch, SdfVector[] grid, int n, SdfVector o, SdfVector d, out double tBest)
        {
            tBest = double.MaxValue;
            for (int j = 0; j < n; ++j)
            {
                for (int i = 0; i < n; ++i)
                {
                    SdfVector a = grid[j * (n + 1) + i], b = grid[j * (n + 1) + i + 1], c = grid[(j + 1) * (n + 1) + i], e = grid[(j + 1) * (n + 1) + i + 1];
                    if (Triangle(o, d, a, b, c, out double t) && t < tBest)
                    {
                        tBest = t;
                    }
                    if (Triangle(o, d, b, e, c, out t) && t < tBest)
                    {
                        tBest = t;
                    }
                }
            }
            return tBest < double.MaxValue;
        }

        [TEST("Bezier", "ClipMatchesBruteForceOnSmoothPatches")]
        public static void ClipMatchesBruteForceOnSmoothPatches()
        {
            Random random = new Random(7);
            int n = 160;
            int agree = 0, total = 0, hits = 0;
            double worstT = 0;
            for (int trial = 0; trial < 6; ++trial)
            {
                BezierPatch4 patch = DomePatch(random, trial < 3 ? 0.05 : 0.5);
                SdfVector[] grid = new SdfVector[(n + 1) * (n + 1)];
                for (int j = 0; j <= n; ++j)
                {
                    for (int i = 0; i <= n; ++i)
                    {
                        grid[j * (n + 1) + i] = patch.Evaluate((double)i / n, (double)j / n);
                    }
                }
                BezierPatchIntersection clip = new BezierPatchIntersection(patch);
                for (int k = 0; k < 400; ++k)
                {
                    SdfVector target = patch.Evaluate(random.NextDouble() * 1.2 - 0.1, random.NextDouble() * 1.2 - 0.1);
                    SdfVector direction = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, -1).Normalized();
                    SdfVector origin = target - direction * (2 + random.NextDouble());
                    bool expected = BruteForce(patch, grid, n, origin, direction, out double tExpected);
                    bool actual = clip.Test(origin, direction, 0, 1e30, 1e-5, out BezierClipHit hit);
                    ++total;
                    if (expected == actual)
                    {
                        ++agree;
                        if (actual)
                        {
                            ++hits;
                            worstT = Math.Max(worstT, Math.Abs(hit.t - tExpected));
                            SdfVector point = patch.Evaluate(hit.u, hit.v);
                            SdfVector onRay = origin + direction * hit.t;
                            CHECK_LT((point - onRay).Length(), 2e-4);
                        }
                    }
                }
            }
            // The tessellation misses near the silhouette and borders, so a few rays differ
            CHECK_GT(agree, total * 0.99);
            CHECK_GT(hits, 600);
            CHECK_LT(worstT, 1e-3);
        }

        [TEST("Bezier", "CroppingIsReversalInvariant")]
        public static void CroppingIsReversalInvariant()
        {
            Random random = new Random(3);
            BezierPatch4 patch = DomePatch(random, 0.8);
            BezierPatch4 reversed = default;
            for (int j = 0; j < 4; ++j)
            {
                for (int i = 0; i < 4; ++i)
                {
                    reversed.Set(i, j, patch.Get(3 - i, j));
                }
            }
            for (int k = 0; k < 200; ++k)
            {
                double a = random.NextDouble() * 0.8, b = a + random.NextDouble() * (1 - a);
                BezierPatch4 cropped = patch.Crop(a, b, 0, 1);
                BezierPatch4 cropReversed = reversed.Crop(1 - b, 1 - a, 0, 1);
                for (int j = 0; j < 4; ++j)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        SdfVector x = cropped.Get(i, j), y = cropReversed.Get(3 - i, j);
                        CHECK_EQ(x.x, y.x);
                        CHECK_EQ(x.y, y.y);
                        CHECK_EQ(x.z, y.z);
                    }
                }
            }
        }

        [TEST("Bezier", "SplitAndCropAgreeWithEvaluation")]
        public static void SplitAndCropAgreeWithEvaluation()
        {
            Random random = new Random(11);
            BezierPatch4 patch = DomePatch(random, 0.6);
            patch.Split4(out BezierPatch4 p0, out BezierPatch4 p1, out BezierPatch4 p2, out BezierPatch4 p3, 0.3, 0.6);
            BezierPatch4 crop = patch.Crop(0.2, 0.7, 0.1, 0.9);
            for (int k = 0; k < 100; ++k)
            {
                double s = random.NextDouble(), t = random.NextDouble();
                CHECK_LT((p0.Evaluate(s, t) - patch.Evaluate(0.3 * s, 0.6 * t)).Length(), 1e-12);
                CHECK_LT((p1.Evaluate(s, t) - patch.Evaluate(0.3 + 0.7 * s, 0.6 * t)).Length(), 1e-12);
                CHECK_LT((p2.Evaluate(s, t) - patch.Evaluate(0.3 * s, 0.6 + 0.4 * t)).Length(), 1e-12);
                CHECK_LT((p3.Evaluate(s, t) - patch.Evaluate(0.3 + 0.7 * s, 0.6 + 0.4 * t)).Length(), 1e-12);
                CHECK_LT((crop.Evaluate(s, t) - patch.Evaluate(0.2 + 0.5 * s, 0.1 + 0.8 * t)).Length(), 1e-12);
            }
            // Derivatives against differences
            double h = 1e-6;
            SdfVector du = patch.EvaluateDu(0.4, 0.3), dv = patch.EvaluateDv(0.4, 0.3);
            CHECK_LT((du - (patch.Evaluate(0.4 + h, 0.3) - patch.Evaluate(0.4 - h, 0.3)) / (2 * h)).Length(), 1e-6);
            CHECK_LT((dv - (patch.Evaluate(0.4, 0.3 + h) - patch.Evaluate(0.4, 0.3 - h)) / (2 * h)).Length(), 1e-6);
        }
    }
}
