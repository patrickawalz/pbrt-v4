// (not in pbrt-v4) displacedmesh_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class DisplacedMeshTests
    {
        internal static DisplacedMesh MakeMesh(Point3f[] p, int[] indices, Point2f[] uv, Normal3f[] n, Func<double, double, double> height,
                                              int resolution, double minimum, double maximum, bool smooth = false)
        {
            float[] samples = new float[resolution * resolution];
            for (int j = 0; j < resolution; ++j)
            {
                for (int i = 0; i < resolution; ++i)
                {
                    samples[j * resolution + i] = (float)height((double)i / (resolution - 1), (double)j / (resolution - 1));
                }
            }
            ShellHeightField field = new ShellHeightField(resolution, resolution, samples);
            DisplacedMeshOptions options = new DisplacedMeshOptions();
            options.smoothNormals = smooth;
            return new DisplacedMesh(new Transform(), false, indices, p, n, uv, field, minimum, maximum, options);
        }

        internal static List<DisplacedTriangle> Triangles(DisplacedMesh mesh)
        {
            List<DisplacedTriangle> list = new List<DisplacedTriangle>();
            for (int i = 0; i < mesh.TriangleCount(); ++i)
            {
                list.Add(new DisplacedTriangle(mesh, i));
            }
            return list;
        }

        internal static bool Closest(List<DisplacedTriangle> triangles, Ray ray, out ShapeIntersection hit)
        {
            hit = default;
            bool found = false;
            Float tMax = Infinity;
            foreach (DisplacedTriangle triangle in triangles)
            {
                pstd.optional<ShapeIntersection> result = triangle.Intersect(ray, tMax);
                if (result)
                {
                    hit = result.value();
                    tMax = hit.tHit;
                    found = true;
                }
            }
            return found;
        }

        // A square of side 2 in the plane z = 0 with normals +z and uv in [0, 1]
        internal static void Square(out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n)
        {
            p = new Point3f[4] { new Point3f(-1, -1, 0), new Point3f(1, -1, 0), new Point3f(1, 1, 0), new Point3f(-1, 1, 0) };
            indices = new int[6] { 0, 1, 2, 0, 2, 3 };
            uv = new Point2f[4] { new Point2f(0, 0), new Point2f(1, 0), new Point2f(1, 1), new Point2f(0, 1) };
            n = new Normal3f[4] { new Normal3f(0, 0, 1), new Normal3f(0, 0, 1), new Normal3f(0, 0, 1), new Normal3f(0, 0, 1) };
        }

        [TEST("Displaced", "TiltedPlaneIsExact")]
        public static void Displaced_TiltedPlaneIsExact()
        {
            Square(out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            // z = 0.1 + 0.4 u: a plane that the micro triangles reproduce exactly
            DisplacedMesh mesh = MakeMesh(p, indices, uv, n, (double u, double v) => u, 33, 0.1, 0.5);
            List<DisplacedTriangle> triangles = Triangles(mesh);
            RNG rng = new RNG(1);
            double worstT = 0, worstNormal = 0;
            int hits = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Point3f o = new Point3f((Float)(2 * rng.Uniform<Float>() - 1), (Float)(2 * rng.Uniform<Float>() - 1), 1.5f);
                Vector3f d = pbrt.Normalize(new Vector3f((Float)(rng.Uniform<Float>() - 0.5), (Float)(rng.Uniform<Float>() - 0.5), -1));
                Ray ray = new Ray(o, d);
                if (!Closest(triangles, ray, out ShapeIntersection hit))
                {
                    continue;
                }
                ++hits;
                // The plane z = 0.1 + 0.4 (x + 1) / 2 = 0.3 + 0.2 x
                double t = ((double)0.3 + 0.2 * o.x - o.z) / (d.z - 0.2 * d.x);
                worstT = Math.Max(worstT, Math.Abs(t - hit.tHit));
                Vector3f expected = pbrt.Normalize(new Vector3f(-0.2f, 0, 1));
                Vector3f got = new Vector3f(hit.intr.n.x, hit.intr.n.y, hit.intr.n.z);
                worstNormal = Math.Max(worstNormal, pbrt.Length(expected - got));
            }
            Console.WriteLine("tilted plane: " + hits + " hits, worst t error " + worstT + ", worst normal error " + worstNormal);
            EXPECT_GT(hits, 1200);
            EXPECT_LE(worstT, 2e-6);
            EXPECT_LE(worstNormal, 1e-5);
        }

        // The reference for bumps over a flat base: every micro triangle as a triangle in the world, brute force
        private static double ReferenceHit(Func<double, double, double> height, int resolution, double minimum, double maximum,
                                           Point3f o, Vector3f d)
        {
            double best = double.MaxValue;
            double[] z = new double[resolution * resolution];
            for (int j = 0; j < resolution; ++j)
            {
                for (int i = 0; i < resolution; ++i)
                {
                    z[j * resolution + i] = minimum + (maximum - minimum) * (float)height((double)i / (resolution - 1), (double)j / (resolution - 1));
                }
            }
            for (int j = 0; j + 1 < resolution; ++j)
            {
                for (int i = 0; i + 1 < resolution; ++i)
                {
                    SdfVector c00 = Corner(i, j, resolution, z), c10 = Corner(i + 1, j, resolution, z), c01 = Corner(i, j + 1, resolution, z),
                              c11 = Corner(i + 1, j + 1, resolution, z);
                    best = Math.Min(best, TriangleHit(o, d, c00, c10, c11));
                    best = Math.Min(best, TriangleHit(o, d, c00, c11, c01));
                }
            }
            return best;
        }

        private static SdfVector Corner(int i, int j, int resolution, double[] z)
        {
            return new SdfVector(-1 + 2.0 * i / (resolution - 1), -1 + 2.0 * j / (resolution - 1), z[j * resolution + i]);
        }

        private static double TriangleHit(Point3f o, Vector3f d, SdfVector a, SdfVector b, SdfVector c)
        {
            SdfVector origin = new SdfVector(o.x, o.y, o.z), direction = new SdfVector(d.x, d.y, d.z);
            SdfVector e1 = b - a, e2 = c - a;
            SdfVector pvec = SdfVector.Cross(direction, e2);
            double det = SdfVector.Dot(e1, pvec);
            if (Math.Abs(det) < 1e-300)
            {
                return double.MaxValue;
            }
            SdfVector tvec = origin - a;
            double u = SdfVector.Dot(tvec, pvec) / det;
            SdfVector qvec = SdfVector.Cross(tvec, e1);
            double v = SdfVector.Dot(direction, qvec) / det;
            double t = SdfVector.Dot(e2, qvec) / det;
            if (u < 0 || v < 0 || u + v > 1 || t <= 0)
            {
                return double.MaxValue;
            }
            return t;
        }

        [TEST("Displaced", "BumpsMatchTheBruteForceMicroTriangles")]
        public static void Displaced_BumpsMatchTheBruteForceMicroTriangles()
        {
            Square(out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            Func<double, double, double> height = (double u, double v) =>
                0.5 + 0.45 * Math.Sin(23 * u) * Math.Cos(17 * v) * Math.Sin(5 * (u + v));
            int resolution = 48;
            DisplacedMesh mesh = MakeMesh(p, indices, uv, n, height, resolution, 0.0, 0.3);
            List<DisplacedTriangle> triangles = Triangles(mesh);
            RNG rng = new RNG(2);
            int compared = 0, missed = 0, spurious = 0, wrong = 0;
            double worst = 0;
            for (int i = 0; i < 3000; ++i)
            {
                // Rays from above at all angles, down to grazing ones
                double grazing = i % 3 == 0 ? 0.05 : (i % 3 == 1 ? 0.3 : 1.0);
                Point3f o = new Point3f((Float)(4 * rng.Uniform<Float>() - 2), (Float)(4 * rng.Uniform<Float>() - 2), 1.0f);
                Vector3f d = pbrt.Normalize(new Vector3f((Float)(2 * rng.Uniform<Float>() - 1), (Float)(2 * rng.Uniform<Float>() - 1), -(Float)grazing));
                Ray ray = new Ray(o, d);
                double expected = ReferenceHit(height, resolution, 0.0, 0.3, o, d);
                bool found = Closest(triangles, ray, out ShapeIntersection hit);
                if (expected == double.MaxValue)
                {
                    if (found)
                    {
                        ++spurious;
                    }
                    continue;
                }
                ++compared;
                if (!found)
                {
                    ++missed;
                    continue;
                }
                double error = Math.Abs(hit.tHit - expected);
                worst = Math.Max(worst, error);
                if (error > 1e-4)
                {
                    ++wrong;
                }
            }
            Console.WriteLine("bumps: " + compared + " reference hits, " + missed + " missed, " + wrong + " wrong, " + spurious +
                              " spurious, worst t error " + worst);
            EXPECT_GT(compared, 300);
            EXPECT_EQ(missed, 0);
            EXPECT_EQ(wrong, 0);
            EXPECT_EQ(spurious, 0);
        }

        // A cap of the unit sphere over [-0.7, 0.7]^2 (a grid lifted onto the sphere) with radial vertex normals and the planar uv
        internal static void Cap(int cells, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n)
        {
            int side = cells + 1;
            p = new Point3f[side * side];
            uv = new Point2f[side * side];
            n = new Normal3f[side * side];
            for (int j = 0; j < side; ++j)
            {
                for (int i = 0; i < side; ++i)
                {
                    double x = -0.7 + 1.4 * i / cells, y = -0.7 + 1.4 * j / cells;
                    double z = Math.Sqrt(1 - x * x - y * y);
                    p[j * side + i] = new Point3f((Float)x, (Float)y, (Float)z);
                    n[j * side + i] = new Normal3f((Float)x, (Float)y, (Float)z);
                    uv[j * side + i] = new Point2f((Float)(0.5 + x / 2), (Float)(0.5 + y / 2));
                }
            }
            List<int> list = new List<int>();
            for (int j = 0; j < cells; ++j)
            {
                for (int i = 0; i < cells; ++i)
                {
                    int a = j * side + i, b = a + 1, c = a + side, d = c + 1;
                    list.AddRange(new int[] { a, b, d, a, d, c });
                }
            }
            indices = list.ToArray();
        }

        // Whether a point is below the displaced surface of the mesh (inside the material), found with the inverse shell map
        private static bool Below(DisplacedMesh mesh, SdfVector q, ShellPrism prism, double[] heights, double[] alphas, double[] betas)
        {
            for (int t = 0; t < mesh.TriangleCount(); ++t)
            {
                int v0 = mesh.indices[3 * t], v1 = mesh.indices[3 * t + 1], v2 = mesh.indices[3 * t + 2];
                prism.Set(mesh.triangleBase[3 * t], mesh.triangleBase[3 * t + 1], mesh.triangleBase[3 * t + 2], mesh.triangleOffset[3 * t],
                          mesh.triangleOffset[3 * t + 1], mesh.triangleOffset[3 * t + 2]);
                int count = prism.Inverse(q, 0, 1, heights, alphas, betas);
                for (int k = 0; k < count; ++k)
                {
                    if (alphas[k] >= 0 && betas[k] >= 0 && alphas[k] + betas[k] <= 1)
                    {
                        Point2f t0 = mesh.uv[v0], t1 = mesh.uv[v1], t2 = mesh.uv[v2];
                        double u = t0.x + alphas[k] * (t1.x - t0.x) + betas[k] * (t2.x - t0.x);
                        double v = t0.y + alphas[k] * (t1.y - t0.y) + betas[k] * (t2.y - t0.y);
                        return mesh.heightBase[t] + mesh.heightScale[t] * heights[k] <= mesh.field.Value(u * mesh.field.cellsX, v * mesh.field.cellsY);
                    }
                }
            }
            return false;
        }

        [TEST("Displaced", "CurvedBaseMatchesMarchingThroughTheInverseMap")]
        public static void Displaced_CurvedBaseMatchesMarchingThroughTheInverseMap()
        {
            Cap(6, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            Func<double, double, double> height = (double u, double v) => 0.5 + 0.4 * Math.Sin(9 * u) * Math.Cos(7 * v);
            DisplacedMesh mesh = MakeMesh(p, indices, uv, n, height, 40, 0.0, 0.12);
            List<DisplacedTriangle> triangles = Triangles(mesh);
            ShellPrism prism = new ShellPrism(new SdfVector[3], new SdfVector[3]);
            double[] heights = new double[3], alphas = new double[3], betas = new double[3];
            RNG rng = new RNG(4);
            int compared = 0, missed = 0, wrong = 0, spurious = 0;
            for (int i = 0; i < 150; ++i)
            {
                Point3f target = new Point3f((Float)(1.2 * rng.Uniform<Float>() - 0.6), (Float)(1.2 * rng.Uniform<Float>() - 0.6), 0.9f);
                Vector3f direction = pbrt.Normalize(new Vector3f((Float)(0.8 * rng.Uniform<Float>() - 0.4), (Float)(0.8 * rng.Uniform<Float>() - 0.4), -1));
                Point3f o = target - direction * 0.5f;
                Ray ray = new Ray(o, direction);
                // Reference: the first sample below the surface, refined by bisection
                double reference = -1;
                bool previousBelow = false;
                double previousT = 0;
                int steps = 1400;
                for (int s = 0; s <= steps; ++s)
                {
                    double t = 2.0 * s / steps;
                    SdfVector q = new SdfVector(o.x + t * direction.x, o.y + t * direction.y, o.z + t * direction.z);
                    bool below = Below(mesh, q, prism, heights, alphas, betas);
                    if (below && !previousBelow && s > 0)
                    {
                        double lo = previousT, hi = t;
                        for (int b = 0; b < 40; ++b)
                        {
                            double mid = 0.5 * (lo + hi);
                            SdfVector qm = new SdfVector(o.x + mid * direction.x, o.y + mid * direction.y, o.z + mid * direction.z);
                            if (Below(mesh, qm, prism, heights, alphas, betas))
                            {
                                hi = mid;
                            }
                            else
                            {
                                lo = mid;
                            }
                        }
                        reference = hi;
                        break;
                    }
                    previousBelow = below;
                    previousT = t;
                }
                bool found = Closest(triangles, ray, out ShapeIntersection hit);
                if (reference < 0)
                {
                    if (found && hit.tHit < 2.0)
                    {
                        ++spurious;
                    }
                    continue;
                }
                ++compared;
                if (!found)
                {
                    ++missed;
                }
                else if (Math.Abs(hit.tHit - reference) > 2e-3)
                {
                    ++wrong;
                    Console.WriteLine("  ray " + i + ": exact " + hit.tHit + " reference " + reference);
                }
            }
            Console.WriteLine("curved cap: " + compared + " reference hits, " + missed + " missed, " + wrong + " wrong, " + spurious + " spurious");
            EXPECT_GT(compared, 60);
            EXPECT_EQ(missed, 0);
            EXPECT_EQ(wrong, 0);
            EXPECT_EQ(spurious, 0);
        }

        [TEST("Displaced", "NarrowSpikesAreNeverMissed")]
        public static void Displaced_NarrowSpikesAreNeverMissed()
        {
            Square(out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            // A few spikes about one texel wide
            double[,] centers = new double[4, 2] { { 0.31, 0.42 }, { 0.7, 0.2 }, { 0.55, 0.8 }, { 0.12, 0.9 } };
            Func<double, double, double> height = (double u, double v) =>
            {
                double h = 0;
                for (int k = 0; k < 4; ++k)
                {
                    double du = u - centers[k, 0], dv = v - centers[k, 1];
                    h = Math.Max(h, Math.Exp(-(du * du + dv * dv) / (2 * 0.012 * 0.012)));
                }
                return h;
            };
            int resolution = 64;
            DisplacedMesh mesh = MakeMesh(p, indices, uv, n, height, resolution, 0.0, 0.5);
            List<DisplacedTriangle> triangles = Triangles(mesh);
            RNG rng = new RNG(8);
            int compared = 0, missed = 0, wrong = 0;
            for (int i = 0; i < 1500; ++i)
            {
                int k = i % 4;
                Point3f target = new Point3f((Float)(2 * (centers[k, 0] + 0.02 * (rng.Uniform<Float>() - 0.5)) - 1),
                                             (Float)(2 * (centers[k, 1] + 0.02 * (rng.Uniform<Float>() - 0.5)) - 1), (Float)(0.5 * rng.Uniform<Float>()));
                Vector3f direction = pbrt.Normalize(new Vector3f((Float)(rng.Uniform<Float>() - 0.5), (Float)(rng.Uniform<Float>() - 0.5), -0.5f - rng.Uniform<Float>()));
                Point3f o = target - direction * 2f;
                Ray ray = new Ray(o, direction);
                double expected = ReferenceHit(height, resolution, 0.0, 0.5, o, direction);
                if (expected == double.MaxValue)
                {
                    continue;
                }
                ++compared;
                if (!Closest(triangles, ray, out ShapeIntersection hit))
                {
                    ++missed;
                }
                else if (Math.Abs(hit.tHit - expected) > 1e-4)
                {
                    ++wrong;
                }
            }
            Console.WriteLine("spikes: " + compared + " reference hits, " + missed + " missed, " + wrong + " wrong");
            EXPECT_GT(compared, 400);
            EXPECT_EQ(missed, 0);
            EXPECT_EQ(wrong, 0);
        }

        [TEST("Displaced", "SecondaryRaysAndShadowRaysAreConsistent")]
        public static void Displaced_SecondaryRaysAndShadowRaysAreConsistent()
        {
            Cap(6, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            Func<double, double, double> height = (double u, double v) => 0.5 + 0.4 * Math.Sin(9 * u) * Math.Cos(7 * v);
            DisplacedMesh mesh = MakeMesh(p, indices, uv, n, height, 40, 0.0, 0.12, true);
            List<DisplacedTriangle> triangles = Triangles(mesh);
            RNG rng = new RNG(6);
            int tested = 0, selfHits = 0, disagreements = 0, backFacing = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Point3f target = new Point3f((Float)(1.2 * rng.Uniform<Float>() - 0.6), (Float)(1.2 * rng.Uniform<Float>() - 0.6), 0.9f);
                Vector3f direction = pbrt.Normalize(new Vector3f((Float)(0.8 * rng.Uniform<Float>() - 0.4), (Float)(0.8 * rng.Uniform<Float>() - 0.4), -1));
                Ray ray = new Ray(target - direction * 0.5f, direction);
                if (!Closest(triangles, ray, out ShapeIntersection hit))
                {
                    continue;
                }
                ++tested;
                // Both normals face the side the ray came from, on the same side as each other
                if (pbrt.Dot(hit.intr.n, hit.intr.shading.n) <= 0)
                {
                    ++backFacing;
                }
                // A ray leaving into the hemisphere above the surface must not find the surface at once
                Vector3f up = new Vector3f(hit.intr.n.x, hit.intr.n.y, hit.intr.n.z);
                Vector3f outgoing = pbrt.Normalize(up + new Vector3f((Float)(rng.Uniform<Float>() - 0.5), (Float)(rng.Uniform<Float>() - 0.5), (Float)(rng.Uniform<Float>() - 0.5)));
                if (pbrt.Dot(outgoing, up) <= 0.05f)
                {
                    continue;
                }
                Ray spawned = hit.intr.SpawnRay(outgoing);
                if (Closest(triangles, spawned, out ShapeIntersection again) && again.tHit < 1e-5f)
                {
                    ++selfHits;
                }
                // IntersectP agrees with Intersect on the same ray
                bool any = false;
                foreach (DisplacedTriangle triangle in triangles)
                {
                    any |= triangle.IntersectP(spawned, 0.3f);
                }
                bool closest = Closest(triangles, spawned, out ShapeIntersection near) && near.tHit <= 0.3f;
                if (any != closest)
                {
                    ++disagreements;
                }
            }
            Console.WriteLine("secondary rays: " + tested + " hits, " + selfHits + " self hits, " + disagreements + " IntersectP disagreements, " + backFacing + " inverted shading normals");
            EXPECT_GT(tested, 100);
            EXPECT_EQ(selfHits, 0);
            EXPECT_EQ(disagreements, 0);
            EXPECT_EQ(backFacing, 0);
        }
    }
}
