// (not in pbrt-v4) simdbvh_test.cs
//
// Tests of SimdBVHAggregate (simdbvh.cs) against BVHAggregate: the same hits for the same rays, and the time of a ray.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using testing;

namespace pbrt
{
    public static unsafe class SimdBVHTests
    {
        // Triangles: `clusters` clouds of small ones, of different sizes and densities, and a floor of two large ones
        private static List<Primitive> Triangles(int count, int clusters, ulong seed)
        {
            RNG rng = new RNG(seed);
            pstd.vector<int> indices = new pstd.vector<int>();
            pstd.vector<Point3f> points = new pstd.vector<Point3f>();
            Point3f[] centers = new Point3f[clusters];
            Float[] radii = new Float[clusters];
            for (int c = 0; c < clusters; ++c)
            {
                centers[c] = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                radii[c] = 0.02f + 0.2f * rng.Uniform<Float>() * rng.Uniform<Float>();
            }
            for (int t = 0; t < count; ++t)
            {
                int c = (int)(rng.Uniform<Float>() * clusters) % clusters;
                Float size = radii[c] * (0.01f + 0.05f * rng.Uniform<Float>());
                Point3f at = centers[c] + radii[c] * new Vector3f(2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1);
                for (int k = 0; k < 3; ++k)
                {
                    indices.push_back((int)points.size());
                    points.push_back(at + size * new Vector3f(2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1));
                }
            }
            int floor = (int)points.size();
            points.push_back(new Point3f(-1, -0.1f, -1));
            points.push_back(new Point3f(2, -0.1f, -1));
            points.push_back(new Point3f(2, -0.1f, 2));
            points.push_back(new Point3f(-1, -0.1f, 2));
            foreach (int corner in new int[] { 0, 1, 2, 0, 2, 3 })
            {
                indices.push_back(floor + corner);
            }
            TriangleMesh mesh = new TriangleMesh(new Transform(), false, indices, points, new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(),
                                                 new pstd.vector<Point2f>(), new pstd.vector<int>(), null);
            pstd.vector<Shape> shapes = Triangle.CreateTriangles(mesh, null);
            List<Primitive> primitives = new List<Primitive>();
            for (ulong s = 0; s < shapes.size(); ++s)
            {
                primitives.Add(new SimplePrimitive(shapes[s], null, 0));
            }
            return primitives;
        }

        private static Ray[] Rays(int count, ulong seed)
        {
            RNG rng = new RNG(seed);
            Ray[] rays = new Ray[count];
            for (int i = 0; i < count; ++i)
            {
                Point3f from = new Point3f(0.5f, 0.5f, 0.5f) + 2.5f * SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Point3f to = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                // (some from inside, and some along an axis)
                if (i % 5 == 0)
                {
                    from = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                }
                Vector3f d = to - from;
                if (i % 97 == 0)
                {
                    d = new Vector3f(0, i % 2 == 0 ? -1 : 1, 0);
                }
                rays[i] = new Ray(from, d);
            }
            return rays;
        }

        // The hits of the two aggregates are the same, for the nearest hit and for any hit, with and without a limit of the
        // distance; for one primitive, a few, and many
        [TEST("SimdBVH", "SameHitsAsBVH")]
        public static void SameHitsAsBVH()
        {
            foreach (int count in new int[] { 0, 3, 40, 2000, 60000 })
            {
                foreach (int maxPrims in new int[] { 1, 4, 16 })
                {
                    List<Primitive> primitives = Triangles(count, 12, (ulong)(count + maxPrims));
                    BVHAggregate reference = new BVHAggregate(primitives, 4);
                    SimdBVHAggregate simd = new SimdBVHAggregate(primitives, maxPrims);
                    Bounds3f a = reference.Bounds(), b = simd.Bounds();
                    CHECK_EQ(a.pMin.x, b.pMin.x);
                    CHECK_EQ(a.pMax.z, b.pMax.z);
                    Ray[] rays = Rays(20000, 3);
                    int hits = 0, differences = 0;
                    RNG rng = new RNG(8);
                    for (int i = 0; i < rays.Length; ++i)
                    {
                        Float tMax = i % 3 == 0 ? 0.2f + 1.2f * rng.Uniform<Float>() : Infinity;
                        pstd.optional<ShapeIntersection> expected = reference.Intersect(rays[i], tMax), found = simd.Intersect(rays[i], tMax);
                        hits += expected ? 1 : 0;
                        if ((bool)expected != (bool)found || (expected && expected.value().tHit != found.value().tHit))
                        {
                            ++differences;
                        }
                        if (reference.IntersectP(rays[i], tMax) != simd.IntersectP(rays[i], tMax))
                        {
                            ++differences;
                        }
                    }
                    CHECK_EQ(differences, 0);
                    if (count >= 2000)
                    {
                        CHECK_GT(hits, rays.Length / 4);
                    }
                    simd.Dispose();
                }
            }
        }

        // The time to build and the time of a ray, of the two aggregates, on a million triangles
        [TEST("SimdBVH", "Speed")]
        public static void Speed()
        {
            List<Primitive> primitives = Triangles(1000000, 40, 17);
            Stopwatch watch = Stopwatch.StartNew();
            BVHAggregate reference = new BVHAggregate(primitives, 4);
            double referenceBuild = watch.Elapsed.TotalSeconds;
            watch.Restart();
            SimdBVHAggregate simd = new SimdBVHAggregate(primitives, 4);
            double simdBuild = watch.Elapsed.TotalSeconds;
            Ray[] rays = Rays(400000, 5);
            double[] nearest = { double.MaxValue, double.MaxValue }, any = { double.MaxValue, double.MaxValue };
            int[] hitCounts = new int[4];
            for (int pass = 0; pass < 5; ++pass)
            {
                for (int which = 0; which < 2; ++which)
                {
                    Primitive aggregate = which == 0 ? reference : simd;
                    watch.Restart();
                    int hits = 0;
                    for (int i = 0; i < rays.Length; ++i)
                    {
                        hits += aggregate.Intersect(rays[i]) ? 1 : 0;
                    }
                    nearest[which] = Math.Min(nearest[which], watch.Elapsed.TotalMilliseconds * 1e6 / rays.Length);
                    hitCounts[which] = hits;
                    watch.Restart();
                    hits = 0;
                    for (int i = 0; i < rays.Length; ++i)
                    {
                        hits += aggregate.IntersectP(rays[i]) ? 1 : 0;
                    }
                    any[which] = Math.Min(any[which], watch.Elapsed.TotalMilliseconds * 1e6 / rays.Length);
                    hitCounts[2 + which] = hits;
                }
            }
            Console.WriteLine("simd bvh, 1,000,002 triangles: built in " + simdBuild.ToString("f2") + " s (" + simd.NodeCount + " nodes) against " +
                              referenceBuild.ToString("f2") + " s; nearest hit " + nearest[1].ToString("f0") + " ns a ray against " + nearest[0].ToString("f0") +
                              " ns (" + (nearest[0] / nearest[1]).ToString("f2") + "x); any hit " + any[1].ToString("f0") + " ns against " + any[0].ToString("f0") +
                              " ns (" + (any[0] / any[1]).ToString("f2") + "x); " + hitCounts[1] + " of " + rays.Length + " rays hit");
            CHECK_EQ(hitCounts[0], hitCounts[1]);
            CHECK_EQ(hitCounts[2], hitCounts[3]);
            simd.Dispose();
        }
    }
}
