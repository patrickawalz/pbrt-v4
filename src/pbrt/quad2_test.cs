// (not in pbrt-v4) quad2_test.cs
//
// Tests of Quad2 (shapes_quad2.cs), the quad in the form of Triangle, against Quad (shapes_quad.cs): the same hits and interactions, the same samples and
// densities for the same random numbers, the watertightness of a mesh of Quad2 shapes, and the time of the intersection of the two.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using testing;

namespace pbrt
{
    public static unsafe class Quad2Tests
    {
        // The same quad (with texture coordinates and normals, or without) as a Quad and as a Quad2
        private static void MakeBoth(Point3f[] p, int variant, bool projected, out Quad quad, out Quad2 quad2)
        {
            List<Point2f> uv = new List<Point2f>();
            List<Normal3f> normals = new List<Normal3f>();
            if ((variant & 1) != 0)
            {
                uv.AddRange(new Point2f[] { new Point2f(0.1f, 0.2f), new Point2f(0.9f, 0.3f), new Point2f(1.1f, 0.8f), new Point2f(0.0f, 1.0f) });
            }
            if ((variant & 2) != 0)
            {
                Normal3f n = (Normal3f)pbrt.Normalize(pbrt.Cross(p[2] - p[0], p[3] - p[1]));
                normals.Add(n);
                normals.Add(n + new Normal3f(0.1f, 0.05f, 0));
                normals.Add(n);
                normals.Add(n - new Normal3f(0, 0.1f, 0.05f));
            }
            List<int> indices = new List<int> { 0, 1, 2, 3 };
            pstd.vector<Shape> a = Quad.CreateShapes(new Transform(), false, indices, new List<Point3f>(p), new List<Normal3f>(normals), new List<Point2f>(uv),
                                                     new List<int>(), projected, new Allocator());
            pstd.vector<Shape> b = Quad2.CreateShapes(new Transform(), false, indices, new List<Point3f>(p), new List<Normal3f>(normals), new List<Point2f>(uv),
                                                      new List<int>(), projected, new Allocator());
            EXPECT_EQ(1, (int)a.size());
            EXPECT_EQ(1, (int)b.size());
            quad = a[0] as Quad;
            quad2 = b[0] as Quad2;
        }

        private static bool Near(Float a, Float b, double tolerance)
        {
            return Math.Abs((double)a - b) <= tolerance * (1 + Math.Max(Math.Abs((double)a), Math.Abs((double)b)));
        }

        private static bool Near(Vector3f a, Vector3f b, double tolerance)
        {
            return Near(a.x, b.x, tolerance) && Near(a.y, b.y, tolerance) && Near(a.z, b.z, tolerance);
        }

        // The rectangle with corners in order around it, and a random planar convex quadrilateral
        private static Point3f[] TestQuad(Random random, int trial)
        {
            Vector3f ez;
            Point3f center = new Point3f((Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2));
            if (trial % 5 == 0)
            {
                Vector3f a = QuadTests.RandomDirection(random);
                Vector3f helper = Math.Abs(a.x) < 0.9f ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0);
                Vector3f ex = pbrt.Normalize(pbrt.Cross(helper, a));
                Vector3f ey = pbrt.Cross(a, ex);
                Float w = (Float)(0.2 + random.NextDouble() * 2), h = (Float)(0.2 + random.NextDouble() * 2);
                return new Point3f[] { center - w * ex - h * ey, center + w * ex - h * ey, center + w * ex + h * ey, center - w * ex + h * ey };
            }
            return QuadTests.RandomQuad(random, center, (Float)(0.1 + random.NextDouble() * 3), out ez);
        }

        [TEST("Quad2", "HitsAndInteractionsMatchQuad")]
        public static void Quad2_HitsMatchQuad()
        {
            Random random = new Random(33);
            int hits = 0, misses = 0, hitMismatches = 0, valueMismatches = 0;
            for (int trial = 0; trial < 400; ++trial)
            {
                Point3f[] p = TestQuad(random, trial);
                MakeBoth(p, trial % 4, false, out Quad quad, out Quad2 quad2);
                for (int i = 0; i < 300; ++i)
                {
                    Vector3f toward = (Float)(random.NextDouble() * 3 - 1.5) * (p[1] - p[0]) + (Float)(random.NextDouble() * 3 - 1.5) * (p[3] - p[0]);
                    Point3f target = p[0] + (Vector3f)toward + (Vector3f)((p[2] - p[0]) * 0.3f);
                    Point3f origin = target + QuadTests.RandomDirection(random) * (Float)(0.3 + random.NextDouble() * 4);
                    Ray ray = new Ray(origin, pbrt.Normalize(target - origin));
                    pstd.optional<ShapeIntersection> a = quad.Intersect(ray);
                    pstd.optional<ShapeIntersection> b = quad2.Intersect(ray);
                    bool pa = quad.IntersectP(ray), pb = quad2.IntersectP(ray);
                    if ((bool)a != (bool)b || pa != pb || pa != (bool)a)
                    {
                        ++hitMismatches;
                        continue;
                    }
                    if (!a)
                    {
                        ++misses;
                        continue;
                    }
                    ++hits;
                    SurfaceInteraction x = a.value().intr, y = b.value().intr;
                    bool same = Near(a.value().tHit, b.value().tHit, 1e-6) && Near((Vector3f)x.p(), (Vector3f)y.p(), 1e-6) &&
                                Near(x.uv[0], y.uv[0], 1e-5) && Near(x.uv[1], y.uv[1], 1e-5) && Near(x.dpdu, y.dpdu, 1e-5) && Near(x.dpdv, y.dpdv, 1e-5) &&
                                Near((Vector3f)x.n, (Vector3f)y.n, 1e-5) && Near((Vector3f)x.shading.n, (Vector3f)y.shading.n, 1e-5) &&
                                Near((Vector3f)x.dndu, (Vector3f)y.dndu, 1e-4) && Near((Vector3f)x.dndv, (Vector3f)y.dndv, 1e-4) && x.faceIndex == y.faceIndex;
                    if (!same)
                    {
                        ++valueMismatches;
                    }
                }
                EXPECT_NEAR(quad.Area(), quad2.Area(), 1e-5 * quad.Area());
            }
            Console.WriteLine("[Quad2] hits " + hits + ", misses " + misses + ", hit/miss mismatches " + hitMismatches + ", value mismatches " + valueMismatches);
            EXPECT_GT(hits, 10000);
            EXPECT_EQ(0, hitMismatches);
            EXPECT_EQ(0, valueMismatches);
        }

        [TEST("Quad2", "SamplesAndDensitiesMatchQuad")]
        public static void Quad2_SamplesMatchQuad()
        {
            Random random = new Random(44);
            int samples = 0, mismatches = 0, pdfMismatches = 0, absent = 0;
            for (int trial = 0; trial < 300; ++trial)
            {
                Point3f[] p = TestQuad(random, trial);
                bool projected = trial % 3 == 2;
                MakeBoth(p, trial % 4, projected, out Quad quad, out Quad2 quad2);
                Point3f center = p[0] + 0.5f * (p[2] - p[0]);
                for (int i = 0; i < 200; ++i)
                {
                    Point2f u = new Point2f((Float)random.NextDouble(), (Float)random.NextDouble());
                    // By area
                    pstd.optional<ShapeSample> sa = quad.Sample(u);
                    pstd.optional<ShapeSample> sb = quad2.Sample(u);
                    if ((bool)sa != (bool)sb)
                    {
                        ++mismatches;
                    }
                    else if (sa && !(Near((Vector3f)sa.value().intr.p(), (Vector3f)sb.value().intr.p(), 1e-6) && Near(sa.value().pdf, sb.value().pdf, 1e-5) &&
                                     Near(sa.value().intr.uv[0], sb.value().intr.uv[0], 1e-5) && Near(sa.value().intr.uv[1], sb.value().intr.uv[1], 1e-5)))
                    {
                        ++mismatches;
                    }
                    // From a reference point, with a shading normal or without
                    Point3f reference = center + QuadTests.RandomDirection(random) * (Float)(0.2 + random.NextDouble() * 6);
                    Normal3f ns = i % 4 == 0 ? new Normal3f(0, 0, 0) : (Normal3f)QuadTests.RandomDirection(random);
                    ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(reference, new Vector3f(0, 0, 0)), ns, ns, 0);
                    pstd.optional<ShapeSample> ca = quad.Sample(ctx, u);
                    pstd.optional<ShapeSample> cb = quad2.Sample(ctx, u);
                    ++samples;
                    if ((bool)ca != (bool)cb)
                    {
                        ++mismatches;
                        continue;
                    }
                    if (!ca)
                    {
                        ++absent;
                        continue;
                    }
                    if (!(Near((Vector3f)ca.value().intr.p(), (Vector3f)cb.value().intr.p(), 1e-5) && Near(ca.value().pdf, cb.value().pdf, 1e-4) &&
                          Near(ca.value().intr.uv[0], cb.value().intr.uv[0], 1e-4) && Near(ca.value().intr.uv[1], cb.value().intr.uv[1], 1e-4) &&
                          Near((Vector3f)ca.value().intr.n, (Vector3f)cb.value().intr.n, 1e-5)))
                    {
                        ++mismatches;
                    }
                    Vector3f wi = pbrt.Normalize(ca.value().intr.p() - reference);
                    if (!Near(quad.PDF(ctx, wi), quad2.PDF(ctx, wi), 1e-4))
                    {
                        ++pdfMismatches;
                    }
                }
            }
            Console.WriteLine("[Quad2] " + samples + " samples from points (" + absent + " without a sample), sample mismatches " + mismatches + ", PDF mismatches " + pdfMismatches);
            EXPECT_GT(samples, 50000);
            EXPECT_EQ(0, mismatches);
            EXPECT_EQ(0, pdfMismatches);
        }

        // A mesh of Quad2 shapes: rays at the shared sides, diagonals and vertices of the quads must hit one of them
        [TEST("Quad2", "WatertightAtSharedEdgesAndVertices")]
        public static void Quad2_Watertight()
        {
            const int n = 6;
            List<Point3f> points = new List<Point3f>();
            Random random = new Random(5);
            for (int j = 0; j <= n; ++j)
            {
                for (int i = 0; i <= n; ++i)
                {
                    points.Add(new Point3f(i + 0.2f * (Float)random.NextDouble(), j + 0.2f * (Float)random.NextDouble(), 0));
                }
            }
            List<int> indices = new List<int>();
            for (int j = 0; j < n; ++j)
            {
                for (int i = 0; i < n; ++i)
                {
                    int a = j * (n + 1) + i;
                    indices.AddRange(new int[] { a, a + 1, a + n + 2, a + n + 1 });
                }
            }
            pstd.vector<Shape> shapes = Quad2.CreateShapes(new Transform(), false, indices, points, new List<Normal3f>(), new List<Point2f>(), new List<int>(), false,
                                                           new Allocator());
            EXPECT_EQ(n * n, (int)shapes.size());
            int rays = 0, missed = 0;
            for (int q = 0; q < n * n; ++q)
            {
                // Only the quads whose sides and vertices are all shared (the outline of the mesh is not watertight against anything)
                if (q % n == 0 || q % n == n - 1 || q / n == 0 || q / n == n - 1)
                {
                    continue;
                }
                for (int k = 0; k < 4; ++k)
                {
                    Point3f a = points[indices[4 * q + k]], b = points[indices[4 * q + ((k + 1) & 3)]];
                    Point3f c = points[indices[4 * q + ((k + 2) & 3)]];
                    // On a side, on the diagonal from this corner, and at the corner
                    for (int s = 0; s < 3; ++s)
                    {
                        for (int r = 0; r < 20; ++r)
                        {
                            Float w = (Float)random.NextDouble();
                            Point3f target = s == 0 ? a + w * (b - a) : (s == 1 ? a + w * (c - a) : a);
                            Vector3f dir = new Vector3f((Float)(random.NextDouble() - 0.5) * 0.6f, (Float)(random.NextDouble() - 0.5) * 0.6f, 1);
                            Ray ray = new Ray(target - 3 * dir, dir);
                            bool hit = false;
                            for (int m = 0; m < n * n && !hit; ++m)
                            {
                                hit = shapes[m].IntersectP(ray);
                            }
                            ++rays;
                            missed += hit ? 0 : 1;
                        }
                    }
                }
            }
            Console.WriteLine("[Quad2] " + rays + " rays at shared sides, diagonals and vertices, missed: " + missed);
            EXPECT_EQ(0, missed);
        }

        // The time of the intersection of the two (printed, not checked)
        [TEST("Quad2", "IntersectionSpeed")]
        public static void Quad2_Speed()
        {
            Random random = new Random(9);
            const int quads = 512;
            const int rays = 4096;
            List<Quad> a = new List<Quad>();
            List<Quad2> b = new List<Quad2>();
            for (int i = 0; i < quads; ++i)
            {
                Point3f[] p = TestQuad(random, i);
                MakeBoth(p, i % 4, false, out Quad quad, out Quad2 quad2);
                a.Add(quad);
                b.Add(quad2);
            }
            Ray[] r = new Ray[rays];
            for (int i = 0; i < rays; ++i)
            {
                Point3f from = new Point3f(0, 0, 0) + QuadTests.RandomDirection(random) * 4;
                Point3f to = new Point3f((Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2));
                r[i] = new Ray(from, to - from);
            }
            const int passes = 12;
            long hitsA = 0, hitsB = 0;
            double[] timeP = new double[2], timeI = new double[2];
            for (int round = 0; round < 3; ++round)
            {
                for (int which = 0; which < 2; ++which)
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    long hits = 0;
                    for (int pass = 0; pass < passes; ++pass)
                    {
                        for (int i = 0; i < rays; ++i)
                        {
                            for (int j = 0; j < quads; ++j)
                            {
                                hits += (which == 0 ? a[j].IntersectP(r[i]) : b[j].IntersectP(r[i])) ? 1 : 0;
                            }
                        }
                    }
                    watch.Stop();
                    timeP[which] = round == 0 ? watch.Elapsed.TotalMilliseconds : Math.Min(timeP[which], watch.Elapsed.TotalMilliseconds);
                    if (which == 0) { hitsA = hits; } else { hitsB = hits; }
                    watch = Stopwatch.StartNew();
                    for (int pass = 0; pass < 2; ++pass)
                    {
                        for (int i = 0; i < rays; ++i)
                        {
                            for (int j = 0; j < quads; ++j)
                            {
                                pstd.optional<ShapeIntersection> si = which == 0 ? a[j].Intersect(r[i]) : b[j].Intersect(r[i]);
                                hits += si ? 1 : 0;
                            }
                        }
                    }
                    watch.Stop();
                    timeI[which] = round == 0 ? watch.Elapsed.TotalMilliseconds : Math.Min(timeI[which], watch.Elapsed.TotalMilliseconds);
                }
            }
            double tests = (double)passes * rays * quads;
            double fullTests = 2.0 * rays * quads;
            Console.WriteLine("[Quad2] IntersectP, ns per quad test: Quad " + (timeP[0] * 1e6 / tests).ToString("F1") + ", Quad2 " + (timeP[1] * 1e6 / tests).ToString("F1") +
                              " (hits " + hitsA + " / " + hitsB + ")");
            Console.WriteLine("[Quad2] Intersect (with the interaction), ns per quad test: Quad " + (timeI[0] * 1e6 / fullTests).ToString("F1") + ", Quad2 " +
                              (timeI[1] * 1e6 / fullTests).ToString("F1"));
            EXPECT_EQ(hitsA, hitsB);
        }
    }
}
