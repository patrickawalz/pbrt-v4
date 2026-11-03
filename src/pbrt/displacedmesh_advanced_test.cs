// (not in pbrt-v4) displacedmesh_advanced_test.cs
//
// Tests of the additions to the displaced mesh: per-triangle height ranges, rectangular (RMIP) bounds, compact storage and flat nodes,
// crease welding, and procedural displacement.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class DisplacedMeshAdvancedTests
    {
        // A grid of n x n squares (two triangles each) over [-1, 1]^2 in the plane z = 0, normals +z, uv equal to the position in [0, 1]^2
        private static void Grid(int n, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] normals)
        {
            p = new Point3f[(n + 1) * (n + 1)];
            uv = new Point2f[(n + 1) * (n + 1)];
            normals = new Normal3f[(n + 1) * (n + 1)];
            for (int j = 0; j <= n; ++j)
            {
                for (int i = 0; i <= n; ++i)
                {
                    p[j * (n + 1) + i] = new Point3f(-1 + 2.0f * i / n, -1 + 2.0f * j / n, 0);
                    uv[j * (n + 1) + i] = new Point2f((Float)i / n, (Float)j / n);
                    normals[j * (n + 1) + i] = new Normal3f(0, 0, 1);
                }
            }
            List<int> list = new List<int>();
            for (int j = 0; j < n; ++j)
            {
                for (int i = 0; i < n; ++i)
                {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    list.Add(a);
                    list.Add(b);
                    list.Add(d);
                    list.Add(a);
                    list.Add(d);
                    list.Add(c);
                }
            }
            indices = list.ToArray();
        }

        private static float[] Samples(int resolution, Func<double, double, double> height)
        {
            float[] samples = new float[resolution * resolution];
            for (int j = 0; j < resolution; ++j)
            {
                for (int i = 0; i < resolution; ++i)
                {
                    samples[j * resolution + i] = (float)height((double)i / (resolution - 1), (double)j / (resolution - 1));
                }
            }
            return samples;
        }

        // Rays from above at all angles, down to grazing ones
        private static Ray[] Rays(int count, int seed)
        {
            RNG rng = new RNG((ulong)seed);
            Ray[] rays = new Ray[count];
            for (int i = 0; i < count; ++i)
            {
                double grazing = i % 3 == 0 ? 0.05 : (i % 3 == 1 ? 0.3 : 1.0);
                Point3f o = new Point3f((Float)(4 * rng.Uniform<Float>() - 2), (Float)(4 * rng.Uniform<Float>() - 2), 1.0f);
                Vector3f d = pbrt.Normalize(new Vector3f((Float)(2 * rng.Uniform<Float>() - 1), (Float)(2 * rng.Uniform<Float>() - 1), -(Float)grazing));
                rays[i] = new Ray(o, d);
            }
            return rays;
        }

        // Traces the rays; hits[i] is the distance or -1. Returns the statistics of the traversal.
        private static ShellStatistics Trace(DisplacedMesh mesh, Ray[] rays, double[] hits)
        {
            List<DisplacedTriangle> triangles = DisplacedMeshTests.Triangles(mesh);
            DisplacedTriangle.ResetThreadStatistics();
            for (int i = 0; i < rays.Length; ++i)
            {
                hits[i] = DisplacedMeshTests.Closest(triangles, rays[i], out ShapeIntersection hit) ? hit.tHit : -1;
            }
            return DisplacedTriangle.ThreadStatistics();
        }

        private static int Mismatches(double[] expected, double[] actual, double tolerance)
        {
            int mismatches = 0;
            for (int i = 0; i < expected.Length; ++i)
            {
                if ((expected[i] < 0) != (actual[i] < 0) || (expected[i] >= 0 && Math.Abs(expected[i] - actual[i]) > tolerance))
                {
                    ++mismatches;
                }
            }
            return mismatches;
        }

        private static double Hills(double u, double v)
        {
            return 0.5 + 0.4 * Math.Sin(7 * u + 1) * Math.Cos(5 * v) + 0.08 * Math.Sin(61 * u) * Math.Cos(53 * v + 2 * u);
        }

        [TEST("Displaced", "RangesAndRectanglesGiveTheSameSurface")]
        public static void Displaced_RangesAndRectanglesGiveTheSameSurface()
        {
            Grid(6, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            int resolution = 513;
            ShellHeightField field = new ShellHeightField(resolution, resolution, Samples(resolution, Hills));
            Ray[] rays = Rays(4000, 11);
            double[][] hits = new double[4][];
            ShellStatistics[] statistics = new ShellStatistics[4];
            for (int configuration = 0; configuration < 4; ++configuration)
            {
                DisplacedMeshOptions options = new DisplacedMeshOptions();
                options.tightRanges = (configuration & 1) != 0;
                options.rectangleBounds = (configuration & 2) != 0;
                DisplacedMesh mesh = new DisplacedMesh(new Transform(), false, indices, p, n, uv, field, 0.0, 0.25, options);
                hits[configuration] = new double[rays.Length];
                statistics[configuration] = Trace(mesh, rays, hits[configuration]);
            }
            int found = 0;
            for (int i = 0; i < rays.Length; ++i)
            {
                if (hits[0][i] >= 0)
                {
                    ++found;
                }
            }
            string[] names = { "global ranges, plain", "tight ranges, plain", "global ranges, rectangles", "tight ranges, rectangles" };
            for (int configuration = 0; configuration < 4; ++configuration)
            {
                int mismatches = Mismatches(hits[0], hits[configuration], 1e-6);
                Console.WriteLine("ranges/rectangles: " + names[configuration] + ": " + statistics[configuration].nodes + " nodes, " +
                                  statistics[configuration].leaves + " leaves, " + mismatches + " mismatches with the plain traversal");
                EXPECT_LE(mismatches, 2);
            }
            EXPECT_GT(found, 300);
            EXPECT_LT(statistics[1].nodes, statistics[0].nodes);
            EXPECT_LT(statistics[2].nodes, statistics[0].nodes);
            EXPECT_LT(statistics[3].nodes, statistics[1].nodes);
        }

        private static bool PointInTriangle(double px, double py, double[] t)
        {
            double d0 = (t[2] - t[0]) * (py - t[1]) - (t[3] - t[1]) * (px - t[0]);
            double d1 = (t[4] - t[2]) * (py - t[3]) - (t[5] - t[3]) * (px - t[2]);
            double d2 = (t[0] - t[4]) * (py - t[5]) - (t[1] - t[5]) * (px - t[4]);
            bool negative = d0 < -1e-12 || d1 < -1e-12 || d2 < -1e-12, positive = d0 > 1e-12 || d1 > 1e-12 || d2 > 1e-12;
            return !(negative && positive);
        }

        private static bool SegmentsIntersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            if (Math.Max(Math.Min(ax, bx), Math.Min(cx, dx)) > Math.Min(Math.Max(ax, bx), Math.Max(cx, dx)) ||
                Math.Max(Math.Min(ay, by), Math.Min(cy, dy)) > Math.Min(Math.Max(ay, by), Math.Max(cy, dy)))
            {
                return false;
            }
            double d1 = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax), d2 = (bx - ax) * (dy - ay) - (by - ay) * (dx - ax);
            double d3 = (dx - cx) * (ay - cy) - (dy - cy) * (ax - cx), d4 = (dx - cx) * (by - cy) - (dy - cy) * (bx - cx);
            return d1 * d2 <= 0 && d3 * d4 <= 0;
        }

        private static bool CellTouchesTriangle(int i, int j, double[] t)
        {
            for (int k = 0; k < 3; ++k)
            {
                if (t[2 * k] >= i && t[2 * k] <= i + 1 && t[2 * k + 1] >= j && t[2 * k + 1] <= j + 1)
                {
                    return true;
                }
            }
            for (int corner = 0; corner < 4; ++corner)
            {
                if (PointInTriangle(i + (corner & 1), j + (corner >> 1), t))
                {
                    return true;
                }
            }
            double[] cx = { i, i + 1, i + 1, i }, cy = { j, j, j + 1, j + 1 };
            for (int k = 0; k < 3; ++k)
            {
                for (int e = 0; e < 4; ++e)
                {
                    if (SegmentsIntersect(t[2 * k], t[2 * k + 1], t[2 * ((k + 1) % 3)], t[2 * ((k + 1) % 3) + 1], cx[e], cy[e],
                                          cx[(e + 1) % 4], cy[(e + 1) % 4]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        [TEST("Displaced", "TriangleRangeCoversEveryCellItTouches")]
        public static void Displaced_TriangleRangeCoversEveryCellItTouches()
        {
            int resolution = 301;
            ShellHeightField field = new ShellHeightField(resolution, resolution, Samples(resolution, Hills));
            RNG rng = new RNG(5);
            int covered = 0, equal = 0, total = 0;
            for (int trial = 0; trial < 150; ++trial)
            {
                double scale = trial % 3 == 0 ? 6 : (trial % 3 == 1 ? 40 : 250);
                double ox = 300 * rng.Uniform<Float>() - 0, oy = 300 * rng.Uniform<Float>();
                double[] t = new double[6];
                for (int k = 0; k < 3; ++k)
                {
                    t[2 * k] = Math.Min(Math.Max(ox + scale * (rng.Uniform<Float>() - 0.5), 0), 300);
                    t[2 * k + 1] = Math.Min(Math.Max(oy + scale * (rng.Uniform<Float>() - 0.5), 0), 300);
                }
                double area = (t[2] - t[0]) * (t[5] - t[1]) - (t[3] - t[1]) * (t[4] - t[0]);
                if (Math.Abs(area) < 1e-3)
                {
                    continue;
                }
                bool any = field.TriangleRange(t[0], t[1], t[2], t[3], t[4], t[5], out double low, out double high);
                double expectedLow = double.MaxValue, expectedHigh = double.MinValue;
                int minI = (int)Math.Floor(Math.Min(t[0], Math.Min(t[2], t[4]))), maxI = (int)Math.Floor(Math.Max(t[0], Math.Max(t[2], t[4])));
                int minJ = (int)Math.Floor(Math.Min(t[1], Math.Min(t[3], t[5]))), maxJ = (int)Math.Floor(Math.Max(t[1], Math.Max(t[3], t[5])));
                for (int j = Math.Max(minJ, 0); j <= Math.Min(maxJ, 299); ++j)
                {
                    for (int i = Math.Max(minI, 0); i <= Math.Min(maxI, 299); ++i)
                    {
                        if (CellTouchesTriangle(i, j, t))
                        {
                            field.NodeRange(0, i, j, out double cellLow, out double cellHigh);
                            expectedLow = Math.Min(expectedLow, cellLow);
                            expectedHigh = Math.Max(expectedHigh, cellHigh);
                        }
                    }
                }
                ++total;
                EXPECT_TRUE(any);
                if (any && low <= expectedLow + 1e-12 && high >= expectedHigh - 1e-12)
                {
                    ++covered;
                }
                if (any && Math.Abs(low - expectedLow) < 1e-6 && Math.Abs(high - expectedHigh) < 1e-6)
                {
                    ++equal;
                }
            }
            Console.WriteLine("triangle range: " + total + " triangles, " + covered + " cover the cells they touch, " + equal + " are equal to it");
            EXPECT_EQ(covered, total);
            EXPECT_GE(equal, total * 9 / 10);

            // The range of the heights at points inside the triangle is within the range
            double[] triangle = { 20.5, 30.25, 180.75, 60, 90, 250.5 };
            field.TriangleRange(triangle[0], triangle[1], triangle[2], triangle[3], triangle[4], triangle[5], out double rangeLow, out double rangeHigh);
            int outside = 0;
            for (int k = 0; k < 5000; ++k)
            {
                double a = rng.Uniform<Float>(), b = rng.Uniform<Float>();
                if (a + b > 1)
                {
                    a = 1 - a;
                    b = 1 - b;
                }
                double x = triangle[0] + a * (triangle[2] - triangle[0]) + b * (triangle[4] - triangle[0]);
                double y = triangle[1] + a * (triangle[3] - triangle[1]) + b * (triangle[5] - triangle[1]);
                double value = field.Value(x, y);
                if (value < rangeLow - 1e-9 || value > rangeHigh + 1e-9)
                {
                    ++outside;
                }
            }
            EXPECT_EQ(outside, 0);
        }

        private static void CheckRectangles(ShellHeights field, string name, bool exactWhenSmall)
        {
            RNG rng = new RNG(9);
            int outside = 0, tooLoose = 0, total = 0;
            double excess = 0;
            for (int trial = 0; trial < 600; ++trial)
            {
                double size = trial % 4 == 0 ? 2 : (trial % 4 == 1 ? 9 : (trial % 4 == 2 ? 50 : 200));
                double x0 = (field.cellsX - 1) * rng.Uniform<Float>(), y0 = (field.cellsY - 1) * rng.Uniform<Float>();
                double x1 = Math.Min(x0 + size * rng.Uniform<Float>(), field.cellsX), y1 = Math.Min(y0 + size * rng.Uniform<Float>(), field.cellsY);
                bool any = field.RectangleRange(x0, y0, x1, y1, out double low, out double high);
                EXPECT_TRUE(any);
                int i0 = (int)Math.Floor(x0), i1 = Math.Min((int)Math.Floor(x1), field.cellsX - 1);
                int j0 = (int)Math.Floor(y0), j1 = Math.Min((int)Math.Floor(y1), field.cellsY - 1);
                double expectedLow = double.MaxValue, expectedHigh = double.MinValue;
                for (int j = j0; j <= j1; ++j)
                {
                    for (int i = i0; i <= i1; ++i)
                    {
                        double a = Math.Min(Math.Min(field.Sample(i, j), field.Sample(i + 1, j)), Math.Min(field.Sample(i, j + 1), field.Sample(i + 1, j + 1)));
                        double b = Math.Max(Math.Max(field.Sample(i, j), field.Sample(i + 1, j)), Math.Max(field.Sample(i, j + 1), field.Sample(i + 1, j + 1)));
                        expectedLow = Math.Min(expectedLow, a);
                        expectedHigh = Math.Max(expectedHigh, b);
                    }
                }
                ++total;
                if (low > expectedLow + 1e-6 || high < expectedHigh - 1e-6)
                {
                    ++outside;
                }
                excess += (high - low) - (expectedHigh - expectedLow);
                if (exactWhenSmall && i1 - i0 < 3 && j1 - j0 < 3 && ((high - low) - (expectedHigh - expectedLow)) > 1e-6)
                {
                    ++tooLoose;
                }
                // Points of the surface in the rectangle
                for (int k = 0; k < 8; ++k)
                {
                    double value = field.Value(x0 + (x1 - x0) * rng.Uniform<Float>(), y0 + (y1 - y0) * rng.Uniform<Float>());
                    if (value < low - 1e-6 || value > high + 1e-6)
                    {
                        ++outside;
                    }
                }
            }
            Console.WriteLine("rectangles (" + name + "): " + total + " queries, " + outside + " outside the range, mean excess over the cells touched " +
                              excess / total);
            EXPECT_EQ(outside, 0);
            EXPECT_EQ(tooLoose, 0);
        }

        [TEST("Displaced", "RectangleQueriesBoundTheRectangle")]
        public static void Displaced_RectangleQueriesBoundTheRectangle()
        {
            int width = 300, height = 257;
            float[] samples = new float[width * height];
            for (int j = 0; j < height; ++j)
            {
                for (int i = 0; i < width; ++i)
                {
                    samples[j * width + i] = (float)Hills((double)i / (width - 1), (double)j / (height - 1));
                }
            }
            CheckRectangles(new ShellHeightField(width, height, samples), "float, 128 blocks", true);
            CheckRectangles(new ShellHeightField(width, height, samples, ShellStorage.Float32, -1, 16), "float, 16 blocks", true);
            CheckRectangles(new ShellHeightField(width, height, samples, ShellStorage.Fixed16), "fixed16", true);
            CheckRectangles(new ShellHeightField(width, height, samples, ShellStorage.Packed), "packed", true);
            ShellExpression expression = ShellExpression.Parse("0.5 + 0.3 * sin(20 * u) * cos(14 * v) + 0.1 * noise(30 * u, 30 * v)");
            CheckRectangles(new ShellProceduralField(expression, 8, 1, 0), "procedural", false);
        }

        [TEST("Displaced", "CompactStoragesAreLosslessAndSmall")]
        public static void Displaced_CompactStoragesAreLosslessAndSmall()
        {
            int resolution = 513;
            float[] samples = Samples(resolution, Hills);
            ShellHeightField floats = new ShellHeightField(resolution, resolution, samples);
            ShellHeightField fixed16 = new ShellHeightField(resolution, resolution, samples, ShellStorage.Fixed16);
            ShellHeightField packed = new ShellHeightField(resolution, resolution, samples, ShellStorage.Packed);
            int packedDifferent = 0, quantizationTooLarge = 0, rangeDifferent = 0, notBounding = 0;
            for (int y = 0; y < resolution; ++y)
            {
                for (int x = 0; x < resolution; ++x)
                {
                    if (fixed16.Sample(x, y) != packed.Sample(x, y))
                    {
                        ++packedDifferent;
                    }
                    if (Math.Abs(fixed16.Sample(x, y) - samples[y * resolution + x]) > 0.5 / 65535 + 1e-9)
                    {
                        ++quantizationTooLarge;
                    }
                }
            }
            for (int level = 0; level < floats.levels; ++level)
            {
                for (int j = 0; j < fixed16.LevelHeight(level); ++j)
                {
                    for (int i = 0; i < fixed16.LevelWidth(level); ++i)
                    {
                        fixed16.NodeRange(level, i, j, out double fixedLow, out double fixedHigh);
                        packed.NodeRange(level, i, j, out double packedLow, out double packedHigh);
                        floats.NodeRange(level, i, j, out double floatLow, out double floatHigh);
                        if (fixedLow != packedLow || fixedHigh != packedHigh)
                        {
                            ++rangeDifferent;
                        }
                        if (fixedLow > floatLow + 1e-9 + 0.5 / 65535 || fixedLow < floatLow - 0.5 / 65535 - 1e-9 ||
                            fixedHigh < floatHigh - 1e-9 - 0.5 / 65535 || fixedHigh > floatHigh + 0.5 / 65535 + 1e-9)
                        {
                            ++notBounding;
                        }
                    }
                }
            }
            double texels = (double)resolution * resolution;
            Console.WriteLine("storage: float " + floats.ByteCount() / texels + " bytes per height, fixed16 " + fixed16.ByteCount() / texels +
                              ", packed " + packed.ByteCount() / texels + "; " + packedDifferent + " packed heights differ from fixed16");
            EXPECT_EQ(packedDifferent, 0);
            EXPECT_EQ(quantizationTooLarge, 0);
            EXPECT_EQ(rangeDifferent, 0);
            EXPECT_EQ(notBounding, 0);
            EXPECT_LT(fixed16.ByteCount(), floats.ByteCount() / 3);
            EXPECT_LT(packed.ByteCount(), fixed16.ByteCount());

            // Noise that needs all the bits, and a constant map, round trip too
            RNG rng = new RNG(3);
            float[] noise = new float[100 * 77];
            for (int k = 0; k < noise.Length; ++k)
            {
                noise[k] = rng.Uniform<Float>();
            }
            ShellHeightField noiseFixed = new ShellHeightField(100, 77, noise, ShellStorage.Fixed16);
            ShellHeightField noisePacked = new ShellHeightField(100, 77, noise, ShellStorage.Packed);
            int differentNoise = 0;
            for (int y = 0; y < 77; ++y)
            {
                for (int x = 0; x < 100; ++x)
                {
                    if (noiseFixed.Sample(x, y) != noisePacked.Sample(x, y))
                    {
                        ++differentNoise;
                    }
                }
            }
            EXPECT_EQ(differentNoise, 0);

            // Traced, the three storages give the same surface up to the quantization
            Grid(2, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            Ray[] rays = Rays(2000, 17);
            double[] floatHits = new double[rays.Length], packedHits = new double[rays.Length], fixedHits = new double[rays.Length];
            DisplacedMeshOptions options = new DisplacedMeshOptions();
            Trace(new DisplacedMesh(new Transform(), false, indices, p, n, uv, floats, 0.0, 0.25, options), rays, floatHits);
            Trace(new DisplacedMesh(new Transform(), false, indices, p, n, uv, packed, 0.0, 0.25, options), rays, packedHits);
            Trace(new DisplacedMesh(new Transform(), false, indices, p, n, uv, fixed16, 0.0, 0.25, options), rays, fixedHits);
            int mismatches = Mismatches(floatHits, packedHits, 1e-3), fixedMismatches = Mismatches(fixedHits, packedHits, 1e-12);
            Console.WriteLine("storage traced: " + mismatches + " of " + rays.Length + " rays differ from float by more than 1e-3, " +
                              fixedMismatches + " differ between fixed16 and packed");
            EXPECT_LE(mismatches, 10);
            EXPECT_EQ(fixedMismatches, 0);
        }

        [TEST("Displaced", "FlatNodesAreIntersectedWithoutDescending")]
        public static void Displaced_FlatNodesAreIntersectedWithoutDescending()
        {
            // Plateaus, a ramp and a few bumps: the plateaus are exactly flat, the ramp is a plane up to the rounding of floats
            int resolution = 513;
            Func<double, double, double> height = (double u, double v) =>
            {
                if (u < 0.4)
                {
                    return 0.3;
                }
                if (u < 0.7)
                {
                    return 0.3 + 0.5 * (u - 0.4) / 0.3;
                }
                return 0.8 + 0.1 * Math.Max(0, Math.Sin(30 * v) * Math.Sin(30 * u));
            };
            float[] samples = Samples(resolution, height);
            ShellHeightField off = new ShellHeightField(resolution, resolution, samples);
            ShellHeightField exact = new ShellHeightField(resolution, resolution, samples, ShellStorage.Float32, 0);
            ShellHeightField tolerant = new ShellHeightField(resolution, resolution, samples, ShellStorage.Float32, 1e-5);
            Grid(3, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            Ray[] rays = Rays(4000, 23);
            double[] offHits = new double[rays.Length], exactHits = new double[rays.Length], tolerantHits = new double[rays.Length];
            DisplacedMeshOptions options = new DisplacedMeshOptions();
            ShellStatistics offStatistics = Trace(new DisplacedMesh(new Transform(), false, indices, p, n, uv, off, 0.0, 0.2, options), rays, offHits);
            ShellStatistics exactStatistics = Trace(new DisplacedMesh(new Transform(), false, indices, p, n, uv, exact, 0.0, 0.2, options), rays, exactHits);
            ShellStatistics tolerantStatistics = Trace(new DisplacedMesh(new Transform(), false, indices, p, n, uv, tolerant, 0.0, 0.2, options), rays,
                                                       tolerantHits);
            int exactMismatches = Mismatches(offHits, exactHits, 1e-7), tolerantMismatches = Mismatches(offHits, tolerantHits, 1e-4);
            Console.WriteLine("flat nodes: off " + offStatistics.nodes + " nodes " + offStatistics.leaves + " leaves; exact planes (" + exact.FlatCount() +
                              " flat nodes) " + exactStatistics.nodes + " nodes " + exactStatistics.leaves + " leaves, " + exactMismatches +
                              " mismatches; tolerance 1e-5 (" + tolerant.FlatCount() + " flat nodes) " + tolerantStatistics.nodes + " nodes " +
                              tolerantStatistics.leaves + " leaves, " + tolerantMismatches + " mismatches");
            EXPECT_GT(exact.FlatCount(), 0);
            EXPECT_GT(tolerant.FlatCount(), exact.FlatCount());
            EXPECT_LE(exactMismatches, 2);
            EXPECT_LE(tolerantMismatches, 6);
            EXPECT_LT(exactStatistics.nodes, offStatistics.nodes);
            EXPECT_LT(tolerantStatistics.nodes, exactStatistics.nodes);
        }

        // A roof: two faces with hard normals meeting in the ridge x = 0, z = 1 (the vertices of the ridge are duplicated)
        private static void Roof(out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n)
        {
            float s = (float)Math.Sqrt(0.5);
            p = new Point3f[8]
            {
                new Point3f(-1, -1, 0), new Point3f(0, -1, 1), new Point3f(0, 1, 1), new Point3f(-1, 1, 0),
                new Point3f(0, -1, 1), new Point3f(1, -1, 0), new Point3f(1, 1, 0), new Point3f(0, 1, 1)
            };
            // v runs along the ridge for both faces, u across
            uv = new Point2f[8]
            {
                new Point2f(0, 0), new Point2f(1, 0), new Point2f(1, 1), new Point2f(0, 1),
                new Point2f(0, 0), new Point2f(1, 0), new Point2f(1, 1), new Point2f(0, 1)
            };
            n = new Normal3f[8]
            {
                new Normal3f(-s, 0, s), new Normal3f(-s, 0, s), new Normal3f(-s, 0, s), new Normal3f(-s, 0, s),
                new Normal3f(s, 0, s), new Normal3f(s, 0, s), new Normal3f(s, 0, s), new Normal3f(s, 0, s)
            };
            // The left face: normal (-1, 0, 1); the right face: (1, 0, 1). Both wound counter-clockwise seen from above.
            indices = new int[12] { 0, 2, 3, 0, 1, 2, 4, 5, 6, 4, 6, 7 };
        }

        [TEST("Displaced", "CreasesAreWatertight")]
        public static void Displaced_CreasesAreWatertight()
        {
            Roof(out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            // The height depends on v only, so that the two faces displace the ridge identically
            int resolution = 129;
            Func<double, double, double> height = (double u, double v) => 0.6 + 0.4 * Math.Sin(12 * v);
            ShellHeightField field = new ShellHeightField(resolution, resolution, Samples(resolution, height));
            int[] misses = new int[3];
            double ridgeError = 0;
            for (int mode = 0; mode < 3; ++mode)
            {
                // Mode 2: welded, with the winding of every triangle reversed (the vertex normals still say which side is up)
                int[] winding = (int[])indices.Clone();
                if (mode == 2)
                {
                    for (int t = 0; t < winding.Length; t += 3)
                    {
                        int swap = winding[t + 1];
                        winding[t + 1] = winding[t + 2];
                        winding[t + 2] = swap;
                    }
                }
                DisplacedMeshOptions options = new DisplacedMeshOptions();
                options.weldCreases = mode != 0;
                DisplacedMesh mesh = new DisplacedMesh(new Transform(), false, winding, p, n, uv, field, 0.0, 0.5, options);
                List<DisplacedTriangle> triangles = DisplacedMeshTests.Triangles(mesh);
                RNG local = new RNG(31);
                for (int i = 0; i < 3000; ++i)
                {
                    // Straight down onto the ridge; the surface there is continuous and covers all x if the prisms are watertight
                    double x = 0.5 * (2 * local.Uniform<Float>() - 1), y = 0.9 * (2 * local.Uniform<Float>() - 1);
                    Ray ray = new Ray(new Point3f((Float)x, (Float)y, 5), new Vector3f(0, 0, -1));
                    if (!DisplacedMeshTests.Closest(triangles, ray, out ShapeIntersection hit))
                    {
                        ++misses[mode];
                    }
                }
                if (mode != 0)
                {
                    // At the ridge the welded direction is straight up, scaled by the miter factor sqrt(2), so that the distance to the
                    // planes of both faces is the displacement distance d
                    for (int i = 0; i < 40; ++i)
                    {
                        double y = 0.8 * (2 * local.Uniform<Float>() - 1);
                        double v = (y + 1) / 2;
                        double d = 0.5 * (0.6 + 0.4 * Math.Sin(12 * v));
                        Ray ray = new Ray(new Point3f(0.0f, (Float)y, 5), new Vector3f(0, 0, -1));
                        if (DisplacedMeshTests.Closest(triangles, ray, out ShapeIntersection hit))
                        {
                            double z = 5 - hit.tHit;
                            ridgeError = Math.Max(ridgeError, Math.Abs(z - (1 + Math.Sqrt(2) * d)));
                        }
                    }
                }
            }
            Console.WriteLine("creases: rays through the ridge that miss: " + misses[0] + " of 3000 with creases none, " + misses[1] +
                              " with creases weld, " + misses[2] + " with weld and reversed winding; worst error of the ridge height " + ridgeError);
            EXPECT_GT(misses[0], 300);
            EXPECT_EQ(misses[1], 0);
            EXPECT_EQ(misses[2], 0);
            EXPECT_LE(ridgeError, 2e-3);
        }

        [TEST("Displaced", "ExpressionBoundsContainTheValues")]
        public static void Displaced_ExpressionBoundsContainTheValues()
        {
            string[] sources =
            {
                "u * v - 0.5",
                "sin(30 * u) * cos(25 * v)",
                "abs(u - 0.5) + abs(v - 0.3)",
                "sqrt(u) * exp(-v)",
                "pow(u - 0.5, 2) + pow(v - 0.5, 3)",
                "pow(u + 0.1, 0.7) - pow(v + 0.2, -0.5)",
                "min(u, v) * max(u, 1 - v)",
                "clamp(sin(9 * u) * 2, -0.5, 0.8)",
                "mix(u, v * v, smoothstep(0.2, 0.8, u))",
                "floor(5 * u) / 5 + fract(7 * v)",
                "noise(12 * u, 12 * v)",
                "fbm(9 * u, 9 * v, 4, 0.5)",
                "turb(6 * u, 6 * v, 3, 0.6)",
                "1 / (2 + u * v) - 1.5 * cos(pi * u)",
                "-(u + 2) * (v - 3)",
                "0.5 + 0.2 * sin(40 * u) * cos(30 * v) + 0.1 * fbm(20 * u, 20 * v, 3, 0.5)"
            };
            RNG rng = new RNG(13);
            int outside = 0, evaluated = 0;
            double tightness = 0;
            int tightCount = 0;
            foreach (string source in sources)
            {
                ShellExpression expression = ShellExpression.Parse(source);
                for (int trial = 0; trial < 400; ++trial)
                {
                    double size = trial % 3 == 0 ? 0.002 : (trial % 3 == 1 ? 0.05 : 0.6);
                    double u0 = (1 - size) * rng.Uniform<Float>(), v0 = (1 - size) * rng.Uniform<Float>();
                    expression.Bounds(u0, u0 + size, v0, v0 + size, out double low, out double high);
                    double sampledLow = double.MaxValue, sampledHigh = double.MinValue;
                    for (int k = 0; k < 20; ++k)
                    {
                        double value = expression.Evaluate(u0 + size * rng.Uniform<Float>(), v0 + size * rng.Uniform<Float>());
                        sampledLow = Math.Min(sampledLow, value);
                        sampledHigh = Math.Max(sampledHigh, value);
                        ++evaluated;
                        if (value < low || value > high)
                        {
                            ++outside;
                        }
                    }
                    if (size == 0.002 && high - low < 1e9 && sampledHigh - sampledLow >= 0)
                    {
                        tightness += (high - low) - (sampledHigh - sampledLow);
                        ++tightCount;
                    }
                }
            }
            Console.WriteLine("expressions: " + sources.Length + " expressions, " + evaluated + " values, " + outside +
                              " outside their bounds, mean excess width over tiny rectangles " + tightness / tightCount);
            EXPECT_EQ(outside, 0);
            EXPECT_LT(tightness / tightCount, 0.05);

            // The parser reports mistakes
            string[] broken = { "", "1 +", "sin(", "sin(1, 2)", "foo(1)", "u $ v", "pow(u, v)", "fbm(u, v, u, 0.5)", "w + 1", "(1 + 2" };
            int rejected = 0;
            foreach (string source in broken)
            {
                try
                {
                    ShellExpression.Parse(source);
                }
                catch (FormatException)
                {
                    ++rejected;
                }
            }
            EXPECT_EQ(rejected, broken.Length);
            EXPECT_DOUBLE_EQ(ShellExpression.Parse("2 * (3 + u) - 1 / 4").Evaluate(0.5, 0), 6.75);
            EXPECT_DOUBLE_EQ(ShellExpression.Parse("-u * -v").Evaluate(2, 3), 6.0);
        }

        [TEST("Displaced", "ProceduralFieldMatchesTheSampledGrid")]
        public static void Displaced_ProceduralFieldMatchesTheSampledGrid()
        {
            ShellExpression expression = ShellExpression.Parse("0.4 + 0.25 * sin(21 * u) * cos(17 * v) + 0.12 * fbm(14 * u, 14 * v, 3, 0.5)");
            int depth = 8;
            ShellProceduralField procedural = new ShellProceduralField(expression, depth, 1, 0);
            int resolution = (1 << depth) + 1;
            float[] samples = new float[resolution * resolution];
            for (int j = 0; j < resolution; ++j)
            {
                for (int i = 0; i < resolution; ++i)
                {
                    samples[j * resolution + i] = (float)procedural.Sample(i, j);
                }
            }
            ShellHeightField grid = new ShellHeightField(resolution, resolution, samples);
            Grid(3, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            Ray[] rays = Rays(3000, 29);
            double[] gridHits = new double[rays.Length], proceduralHits = new double[rays.Length];
            DisplacedMeshOptions options = new DisplacedMeshOptions();
            double thickness = procedural.High() - procedural.Low();
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            ShellStatistics gridStatistics = Trace(new DisplacedMesh(new Transform(), false, indices, p, n, uv, grid, procedural.Low(), procedural.High(), options), rays, gridHits);
            ShellStatistics proceduralStatistics = Trace(new DisplacedMesh(new Transform(), false, indices, p, n, uv, procedural, procedural.Low(),
                                                                         procedural.High(), options), rays, proceduralHits);
            Console.WriteLine("procedural timing (debug build): grid and procedural together " + watch.ElapsedMilliseconds + " ms");
            int mismatches = Mismatches(gridHits, proceduralHits, 1e-5);
            int found = 0;
            for (int i = 0; i < rays.Length; ++i)
            {
                if (proceduralHits[i] >= 0)
                {
                    ++found;
                }
            }
            Console.WriteLine("procedural: thickness " + thickness + ", " + found + " hits, " + mismatches + " mismatches with the grid of the same samples; " +
                              proceduralStatistics.nodes + " nodes vs " + gridStatistics.nodes + " for the grid");
            EXPECT_GT(found, 300);
            EXPECT_LE(mismatches, 3);

            // A depth no grid could store: 2^20 cells per axis, found without any memory for heights
            ShellProceduralField huge = new ShellProceduralField(expression, 20, 1, 0);
            DisplacedMesh hugeMesh = new DisplacedMesh(new Transform(), false, indices, p, n, uv, huge, huge.Low(), huge.High(), options);
            double[] hugeHits = new double[rays.Length];
            Ray[] hugeRays = new Ray[600];
            Array.Copy(rays, hugeRays, hugeRays.Length);
            watch.Restart();
            ShellStatistics hugeStatistics = Trace(hugeMesh, hugeRays, hugeHits);
            Console.WriteLine("procedural 2^20: " + hugeStatistics.nodes + " nodes in " + watch.ElapsedMilliseconds + " ms");
            int hugeFound = 0, hugeMismatches = 0;
            for (int i = 0; i < hugeRays.Length; ++i)
            {
                if (hugeHits[i] >= 0)
                {
                    ++hugeFound;
                }
                if ((hugeHits[i] >= 0) != (gridHits[i] >= 0) || (hugeHits[i] >= 0 && Math.Abs(hugeHits[i] - gridHits[i]) > 0.02))
                {
                    ++hugeMismatches;
                }
            }
            Console.WriteLine("procedural at 2^20 cells: " + hugeFound + " hits, " + hugeMismatches + " differ from the 256 cell surface by more than 0.02");
            EXPECT_GT(hugeFound, 60);
            EXPECT_LE(hugeMismatches, 8);
        }

        // Not a check: the cost of the traversals on a 1024^2 map in this build (run the Release test to read the times)
        [TEST("Displaced", "TraversalCostOnALargeMap")]
        public static void Displaced_TraversalCostOnALargeMap()
        {
            Grid(2, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            foreach (int resolution in new int[] { 1025, 4097 })
            {
            Console.WriteLine("cost: map " + resolution + "^2");
            // Bricks: flat tops, steep mortar, plus fine grain
            Func<double, double, double> bricks = (double u, double v) =>
            {
                double row = Math.Floor(v * 12), x = u * 6 + (((int)row) % 2 == 0 ? 0 : 0.5);
                double fx = x - Math.Floor(x), fy = v * 12 - row;
                double mortar = Math.Min(Math.Min(fx, 1 - fx) * 6, Math.Min(fy, 1 - fy) * 2.2);
                return (Math.Min(1, mortar) * 0.9 + 0.03 * Math.Sin(300 * u) * Math.Cos(280 * v) + 0.03) / 0.96;
            };
            ShellHeightField field = new ShellHeightField(resolution, resolution, Samples(resolution, bricks));
            Ray[] rays = Rays(20000, 41);
            double[] reference = new double[rays.Length];
            int[][] configurations = { new int[] { 0, 0, 2, 2 }, new int[] { 1, 0, 2, 2 }, new int[] { 0, 1, 2, 2 }, new int[] { 1, 1, 2, 2 },
                                       new int[] { 1, 1, 3, 1 }, new int[] { 1, 1, 4, 1 }, new int[] { 1, 1, 3, 2 }, new int[] { 1, 1, 5, 1 } };
            for (int c = 0; c < configurations.Length; ++c)
            {
                DisplacedMeshOptions options = new DisplacedMeshOptions();
                options.tightRanges = configurations[c][0] != 0;
                options.rectangleBounds = configurations[c][1] != 0;
                options.rectangleMinimumLevel = configurations[c][2];
                options.rectanglePasses = configurations[c][3];
                DisplacedMesh mesh = new DisplacedMesh(new Transform(), false, indices, p, n, uv, field, 0.0, 0.2, options);
                double[] hits = new double[rays.Length];
                Trace(mesh, rays, hits);
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                ShellStatistics statistics = Trace(mesh, rays, hits);
                long milliseconds = watch.ElapsedMilliseconds;
                if (c == 0)
                {
                    Array.Copy(hits, reference, hits.Length);
                }
                Console.WriteLine("cost: tight " + configurations[c][0] + " rectangles " + configurations[c][1] + " from level " + configurations[c][2] +
                                  " passes " + configurations[c][3] + ": " + milliseconds + " ms, " + statistics.nodes + " nodes, " + statistics.leaves +
                                  " leaves, " + Mismatches(reference, hits, 1e-6) + " mismatches");
                EXPECT_LE(Mismatches(reference, hits, 1e-6), 5);
            }
            }
        }
    }
}
