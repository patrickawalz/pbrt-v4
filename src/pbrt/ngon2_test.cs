// (not in pbrt-v4) ngon2_test.cs
//
// Tests of Ngon2 and Rectangle2 (shapes_ngon2.cs, shapes_rectangle2.cs), the polygon shapes in the form of Triangle, against Ngon and Rectangle (shapes_ngon.cs,
// shapes_rectangle.cs): the same hits and interactions, the same samples and densities for the same random numbers, and the time of the intersection.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using testing;

namespace pbrt
{
    public static unsafe class Ngon2Tests
    {
        private static bool Near(Float a, Float b, double tolerance)
        {
            return Math.Abs((double)a - b) <= tolerance * (1 + Math.Max(Math.Abs((double)a), Math.Abs((double)b)));
        }

        private static bool Near(Vector3f a, Vector3f b, double tolerance)
        {
            return Near(a.x, b.x, tolerance) && Near(a.y, b.y, tolerance) && Near(a.z, b.z, tolerance);
        }

        // The corners of a random convex polygon (n corners) or rectangle (n = 4 and rectangle) in a random plane
        private static Point3f[] RandomPolygon(Random random, int n, bool rectangle)
        {
            NgonTests_Plane(random, out Vector3f ex, out Vector3f ey);
            Point3f center = new Point3f((Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2));
            Point3f[] p = new Point3f[n];
            if (rectangle)
            {
                Float w = (Float)(0.2 + random.NextDouble() * 2), h = (Float)(0.2 + random.NextDouble() * 2);
                p[0] = center - w * ex - h * ey;
                p[1] = center + w * ex - h * ey;
                p[2] = center + w * ex + h * ey;
                p[3] = center - w * ex + h * ey;
                return p;
            }
            double[][] q = NgonTests.ConvexPolygon2D(random, n, 0.3 + random.NextDouble() * 2);
            for (int i = 0; i < n; ++i)
            {
                p[i] = NgonTests.Embed(q[i], center, ex, ey);
            }
            return p;
        }

        private static void NgonTests_Plane(Random random, out Vector3f ex, out Vector3f ey)
        {
            NgonTests.RandomPlane(random, out ex, out ey, out Vector3f ez);
        }

        private static void Lists(Point3f[] p, int variant, out List<int> indices, out List<Normal3f> normals, out List<Point2f> uv)
        {
            indices = new List<int>();
            normals = new List<Normal3f>();
            uv = new List<Point2f>();
            Normal3f n = (Normal3f)pbrt.Normalize(pbrt.Cross(p[1] - p[0], p[2] - p[1]));
            for (int i = 0; i < p.Length; ++i)
            {
                indices.Add(i);
                if ((variant & 1) != 0)
                {
                    uv.Add(new Point2f(0.1f + 0.2f * i, 0.9f - 0.1f * i * i));
                }
                if ((variant & 2) != 0)
                {
                    normals.Add(n + new Normal3f(0.05f * i, -0.03f * i, 0.02f * i));
                }
            }
        }

        private static void MakeNgons(Point3f[] p, int variant, bool projected, out Ngon ngon, out Ngon2 ngon2)
        {
            Lists(p, variant, out List<int> indices, out List<Normal3f> normals, out List<Point2f> uv);
            List<int> counts = new List<int> { p.Length };
            pstd.vector<Shape> a = Ngon.CreateShapes(new Transform(), false, counts, indices, new List<Point3f>(p), normals, uv, new List<int>(), projected, new Allocator());
            pstd.vector<Shape> b = Ngon2.CreateShapes(new Transform(), false, counts, indices, new List<Point3f>(p), normals, uv, new List<int>(), projected, new Allocator());
            EXPECT_EQ(1, (int)a.size());
            EXPECT_EQ(1, (int)b.size());
            ngon = a[0] as Ngon;
            ngon2 = b[0] as Ngon2;
        }

        private static void MakeRectangles(Point3f[] p, int variant, bool projected, out Shape rectangle, out Shape rectangle2)
        {
            Lists(p, variant, out List<int> indices, out List<Normal3f> normals, out List<Point2f> uv);
            pstd.vector<Shape> a = Ngon.CreateShapes(new Transform(), false, new List<int> { 4 }, indices, new List<Point3f>(p), normals, uv, new List<int>(), projected,
                                                     new Allocator());
            EXPECT_EQ(1, (int)a.size());
            rectangle = new Rectangle(a[0] as Ngon, projected);
            pstd.vector<Shape> b = Rectangle2.CreateShapes(new Transform(), false, indices, new List<Point3f>(p), normals, uv, new List<int>(), projected, new Allocator());
            EXPECT_EQ(1, (int)b.size());
            rectangle2 = b[0];
            EXPECT_TRUE(rectangle2 is Rectangle2);
        }

        private static bool SameHit(pstd.optional<ShapeIntersection> a, pstd.optional<ShapeIntersection> b)
        {
            SurfaceInteraction x = a.value().intr, y = b.value().intr;
            return Near(a.value().tHit, b.value().tHit, 1e-6) && Near((Vector3f)x.p(), (Vector3f)y.p(), 1e-6) && Near(x.uv[0], y.uv[0], 1e-5) &&
                   Near(x.uv[1], y.uv[1], 1e-5) && Near(x.dpdu, y.dpdu, 1e-5) && Near(x.dpdv, y.dpdv, 1e-5) && Near((Vector3f)x.n, (Vector3f)y.n, 1e-5) &&
                   Near((Vector3f)x.shading.n, (Vector3f)y.shading.n, 1e-5) && Near((Vector3f)x.dndu, (Vector3f)y.dndu, 1e-4) &&
                   Near((Vector3f)x.dndv, (Vector3f)y.dndv, 1e-4) && x.faceIndex == y.faceIndex;
        }

        // Rays toward the plane around the polygon: the hits of the two shapes must be the same
        private static void CompareHits(Random random, Point3f[] p, Shape a, Shape b, ref int hits, ref int misses, ref int hitMismatches, ref int valueMismatches)
        {
            Point3f center = p[0];
            for (int k = 1; k < p.Length; ++k)
            {
                center = center + (Vector3f)(p[k] - p[0]) / p.Length;
            }
            Float size = pbrt.Length(p[2] - p[0]);
            for (int i = 0; i < 300; ++i)
            {
                Point3f target = center + (Vector3f)(p[1] - p[0]) * (Float)(random.NextDouble() * 1.6 - 0.8) + (Vector3f)(p[2] - p[1]) * (Float)(random.NextDouble() * 1.6 - 0.8);
                Point3f origin = target + QuadTests.RandomDirection(random) * (Float)(0.3 + random.NextDouble() * 4) * (1 + size);
                Ray ray = new Ray(origin, pbrt.Normalize(target - origin));
                pstd.optional<ShapeIntersection> x = a.Intersect(ray);
                pstd.optional<ShapeIntersection> y = b.Intersect(ray);
                bool px = a.IntersectP(ray), py = b.IntersectP(ray);
                if ((bool)x != (bool)y || px != py || px != (bool)x)
                {
                    ++hitMismatches;
                    continue;
                }
                if (!x)
                {
                    ++misses;
                    continue;
                }
                ++hits;
                if (!SameHit(x, y))
                {
                    ++valueMismatches;
                }
            }
        }

        private static void CompareSamples(Random random, Point3f[] p, Shape a, Shape b, ref int samples, ref int absent, ref int mismatches, ref int pdfMismatches)
        {
            Point3f center = p[0] + 0.5f * (p[2] - p[0]);
            for (int i = 0; i < 200; ++i)
            {
                Point2f u = new Point2f((Float)random.NextDouble(), (Float)random.NextDouble());
                pstd.optional<ShapeSample> sa = a.Sample(u);
                pstd.optional<ShapeSample> sb = b.Sample(u);
                if ((bool)sa != (bool)sb || (sa && !(Near((Vector3f)sa.value().intr.p(), (Vector3f)sb.value().intr.p(), 1e-6) && Near(sa.value().pdf, sb.value().pdf, 1e-5))))
                {
                    ++mismatches;
                }
                Point3f reference = center + QuadTests.RandomDirection(random) * (Float)(0.2 + random.NextDouble() * 6);
                Normal3f ns = i % 4 == 0 ? new Normal3f(0, 0, 0) : (Normal3f)QuadTests.RandomDirection(random);
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(reference, new Vector3f(0, 0, 0)), ns, ns, 0);
                pstd.optional<ShapeSample> ca = a.Sample(ctx, u);
                pstd.optional<ShapeSample> cb = b.Sample(ctx, u);
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
                // At a grazing angle the density of area sampling is ill-conditioned (the point of the hit differs by rounding, the cosine is tiny): not compared
                if (pbrt.AbsDot(ca.value().intr.n, wi) > 1e-3f && !Near(a.PDF(ctx, wi), b.PDF(ctx, wi), 1e-4))
                {
                    ++pdfMismatches;
                }
            }
        }

        [TEST("Ngon2", "HitsAndInteractionsMatchNgon")]
        public static void Ngon2_HitsMatchNgon()
        {
            Random random = new Random(61);
            int hits = 0, misses = 0, hitMismatches = 0, valueMismatches = 0;
            for (int trial = 0; trial < 300; ++trial)
            {
                int n = 3 + trial % 7;
                Point3f[] p = RandomPolygon(random, n, false);
                MakeNgons(p, trial % 4, false, out Ngon ngon, out Ngon2 ngon2);
                CompareHits(random, p, ngon, ngon2, ref hits, ref misses, ref hitMismatches, ref valueMismatches);
                EXPECT_NEAR(ngon.Area(), ngon2.Area(), 1e-6 * ngon.Area());
            }
            Console.WriteLine("[Ngon2] hits " + hits + ", misses " + misses + ", hit/miss mismatches " + hitMismatches + ", value mismatches " + valueMismatches);
            EXPECT_GT(hits, 5000);
            EXPECT_EQ(0, hitMismatches);
            EXPECT_EQ(0, valueMismatches);
        }

        [TEST("Ngon2", "SamplesAndDensitiesMatchNgon")]
        public static void Ngon2_SamplesMatchNgon()
        {
            Random random = new Random(62);
            int samples = 0, absent = 0, mismatches = 0, pdfMismatches = 0;
            for (int trial = 0; trial < 240; ++trial)
            {
                int n = 3 + trial % 7;
                Point3f[] p = RandomPolygon(random, n, false);
                MakeNgons(p, trial % 4, trial % 3 != 2, out Ngon ngon, out Ngon2 ngon2);
                CompareSamples(random, p, ngon, ngon2, ref samples, ref absent, ref mismatches, ref pdfMismatches);
            }
            Console.WriteLine("[Ngon2] " + samples + " samples from points (" + absent + " without a sample), sample mismatches " + mismatches + ", PDF mismatches " + pdfMismatches);
            EXPECT_GT(samples, 40000);
            EXPECT_EQ(0, mismatches);
            EXPECT_EQ(0, pdfMismatches);
        }

        [TEST("Rectangle2", "HitsAndInteractionsMatchRectangle")]
        public static void Rectangle2_HitsMatchRectangle()
        {
            Random random = new Random(63);
            int hits = 0, misses = 0, hitMismatches = 0, valueMismatches = 0;
            for (int trial = 0; trial < 300; ++trial)
            {
                Point3f[] p = RandomPolygon(random, 4, true);
                MakeRectangles(p, trial % 4, false, out Shape rectangle, out Shape rectangle2);
                CompareHits(random, p, rectangle, rectangle2, ref hits, ref misses, ref hitMismatches, ref valueMismatches);
                EXPECT_NEAR(rectangle.Area(), rectangle2.Area(), 1e-6 * rectangle.Area());
            }
            Console.WriteLine("[Rectangle2] hits " + hits + ", misses " + misses + ", hit/miss mismatches " + hitMismatches + ", value mismatches " + valueMismatches);
            EXPECT_GT(hits, 5000);
            EXPECT_EQ(0, hitMismatches);
            EXPECT_EQ(0, valueMismatches);
        }

        [TEST("Rectangle2", "SamplesAndDensitiesMatchRectangle")]
        public static void Rectangle2_SamplesMatchRectangle()
        {
            Random random = new Random(64);
            int samples = 0, absent = 0, mismatches = 0, pdfMismatches = 0;
            for (int trial = 0; trial < 240; ++trial)
            {
                Point3f[] p = RandomPolygon(random, 4, true);
                MakeRectangles(p, trial % 4, trial % 2 == 1, out Shape rectangle, out Shape rectangle2);
                CompareSamples(random, p, rectangle, rectangle2, ref samples, ref absent, ref mismatches, ref pdfMismatches);
            }
            Console.WriteLine("[Rectangle2] " + samples + " samples from points (" + absent + " without a sample), sample mismatches " + mismatches + ", PDF mismatches " + pdfMismatches);
            EXPECT_GT(samples, 40000);
            EXPECT_EQ(0, mismatches);
            EXPECT_EQ(0, pdfMismatches);
        }

        // The time of the intersection of the old and new polygon shapes (printed, not checked)
        [TEST("Ngon2", "IntersectionSpeed")]
        public static void Ngon2_Speed()
        {
            Random random = new Random(11);
            const int rays = 2048;
            const int count = 256;
            for (int which = 0; which < 2; ++which)
            {
                int corners = which == 0 ? 4 : 8;
                List<Shape> a = new List<Shape>(), b = new List<Shape>();
                for (int i = 0; i < count; ++i)
                {
                    Point3f[] p = RandomPolygon(random, corners, which == 0);
                    if (which == 0)
                    {
                        MakeRectangles(p, i % 4, false, out Shape rectangle, out Shape rectangle2);
                        a.Add(rectangle);
                        b.Add(rectangle2);
                    }
                    else
                    {
                        MakeNgons(p, i % 4, false, out Ngon ngon, out Ngon2 ngon2);
                        a.Add(ngon);
                        b.Add(ngon2);
                    }
                }
                Ray[] r = new Ray[rays];
                for (int i = 0; i < rays; ++i)
                {
                    Point3f from = new Point3f(0, 0, 0) + QuadTests.RandomDirection(random) * 4;
                    Point3f to = new Point3f((Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2));
                    r[i] = new Ray(from, to - from);
                }
                double[] timeP = new double[2], timeI = new double[2];
                long[] hitCount = new long[2];
                const int passes = 12;
                for (int round = 0; round < 3; ++round)
                {
                    for (int version = 0; version < 2; ++version)
                    {
                        List<Shape> shapes = version == 0 ? a : b;
                        Stopwatch watch = Stopwatch.StartNew();
                        long hits = 0;
                        for (int pass = 0; pass < passes; ++pass)
                        {
                            for (int i = 0; i < rays; ++i)
                            {
                                for (int j = 0; j < count; ++j)
                                {
                                    hits += shapes[j].IntersectP(r[i]) ? 1 : 0;
                                }
                            }
                        }
                        watch.Stop();
                        hitCount[version] = hits;
                        timeP[version] = round == 0 ? watch.Elapsed.TotalMilliseconds : Math.Min(timeP[version], watch.Elapsed.TotalMilliseconds);
                        watch = Stopwatch.StartNew();
                        for (int pass = 0; pass < 2; ++pass)
                        {
                            for (int i = 0; i < rays; ++i)
                            {
                                for (int j = 0; j < count; ++j)
                                {
                                    pstd.optional<ShapeIntersection> si = shapes[j].Intersect(r[i]);
                                    hits += si ? 1 : 0;
                                }
                            }
                        }
                        watch.Stop();
                        timeI[version] = round == 0 ? watch.Elapsed.TotalMilliseconds : Math.Min(timeI[version], watch.Elapsed.TotalMilliseconds);
                    }
                }
                double tests = (double)passes * rays * count, fullTests = 2.0 * rays * count;
                string name = which == 0 ? "Rectangle" : "Ngon (8 corners)";
                Console.WriteLine("[Ngon2] " + name + " IntersectP, ns per test: old " + (timeP[0] * 1e6 / tests).ToString("F1") + ", new " + (timeP[1] * 1e6 / tests).ToString("F1") +
                                  "; Intersect: old " + (timeI[0] * 1e6 / fullTests).ToString("F1") + ", new " + (timeI[1] * 1e6 / fullTests).ToString("F1"));
                EXPECT_EQ(hitCount[0], hitCount[1]);
            }
        }
    }
}
