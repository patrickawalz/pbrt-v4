// (not in pbrt-v4) shell_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class ShellTests
    {
        // A prism over a random triangle whose offsets lean away from the normal (as the vertex normals of a curved mesh do)
        private static ShellPrism RandomPrism(ref RNG rng, double lean)
        {
            SdfVector[] p = new SdfVector[3];
            SdfVector[] n = new SdfVector[3];
            p[0] = new SdfVector(0, 0, 0);
            p[1] = new SdfVector(1 + 0.3 * rng.Uniform<double>(), 0.2 * rng.Uniform<double>(), 0.1 * rng.Uniform<double>());
            p[2] = new SdfVector(0.2 * rng.Uniform<double>(), 1 + 0.3 * rng.Uniform<double>(), 0.1 * rng.Uniform<double>());
            for (int i = 0; i < 3; ++i)
            {
                n[i] = new SdfVector(lean * (rng.Uniform<double>() - 0.5), lean * (rng.Uniform<double>() - 0.5), 0.4 + 0.2 * rng.Uniform<double>());
            }
            return new ShellPrism(p, n);
        }

        [TEST("Shell", "CubicRootsFindEveryRootInTheInterval")]
        public static void Shell_CubicRootsFindEveryRootInTheInterval()
        {
            RNG rng = new RNG(3);
            double[] roots = new double[3];
            for (int i = 0; i < 2000; ++i)
            {
                double r0 = rng.Uniform<double>(), r1 = rng.Uniform<double>(), r2 = rng.Uniform<double>();
                double scale = 0.1 + rng.Uniform<double>();
                // scale (x - r0)(x - r1)(x - r2) expanded
                double c3 = scale, c2 = -scale * (r0 + r1 + r2), c1 = scale * (r0 * r1 + r0 * r2 + r1 * r2), c0 = -scale * r0 * r1 * r2;
                int count = ShellPolynomial.CubicRoots(c0, c1, c2, c3, -0.001, 1.001, roots);
                EXPECT_EQ(count, 3);
                double[] expected = new double[3] { r0, r1, r2 };
                Array.Sort(expected);
                for (int k = 0; k < count; ++k)
                {
                    EXPECT_LE(Math.Abs(roots[k] - expected[k]), 1e-9);
                }
            }
        }

        [TEST("Shell", "InverseMapRecoversTheCanonicalCoordinates")]
        public static void Shell_InverseMapRecoversTheCanonicalCoordinates()
        {
            RNG rng = new RNG(5);
            double[] heights = new double[3], alphas = new double[3], betas = new double[3];
            int failures = 0;
            for (int i = 0; i < 3000; ++i)
            {
                ShellPrism prism = RandomPrism(ref rng, 0.8);
                double alpha = rng.Uniform<double>(), beta = (1 - alpha) * rng.Uniform<double>(), h = rng.Uniform<double>();
                SdfVector q = prism.Point(alpha, beta, h);
                int count = prism.Inverse(q, -1e-9, 1 + 1e-9, heights, alphas, betas);
                bool found = false;
                for (int k = 0; k < count; ++k)
                {
                    if (Math.Abs(heights[k] - h) < 1e-8 && Math.Abs(alphas[k] - alpha) < 1e-8 && Math.Abs(betas[k] - beta) < 1e-8)
                    {
                        found = true;
                    }
                }
                if (!found)
                {
                    ++failures;
                }
            }
            EXPECT_EQ(failures, 0);
        }

        [TEST("Shell", "RationalRayMatchesTheInverseMap")]
        public static void Shell_RationalRayMatchesTheInverseMap()
        {
            RNG rng = new RNG(7);
            double[] heights = new double[3], alphas = new double[3], betas = new double[3];
            double worst = 0;
            int tested = 0;
            for (int i = 0; i < 400; ++i)
            {
                ShellPrism prism = RandomPrism(ref rng, 0.8);
                SdfVector target = prism.Point(rng.Uniform<double>() * 0.3, rng.Uniform<double>() * 0.3, rng.Uniform<double>());
                SdfVector w = new SdfVector(rng.Uniform<double>() - 0.5, rng.Uniform<double>() - 0.5, -1).Normalized();
                SdfVector o = target - w * 2;
                ShellRay ray = new ShellRay();
                prism.RayCoefficients(o, w, ray);
                for (int k = 0; k < 20; ++k)
                {
                    double t = 2 + (rng.Uniform<double>() - 0.5) * 0.4;
                    SdfVector q = o + w * t;
                    int count = prism.Inverse(q, -0.5, 1.5, heights, alphas, betas);
                    for (int r = 0; r < count; ++r)
                    {
                        double d = ShellRay.Polynomial(ray.denominator, heights[r]);
                        double alpha = ShellRay.Polynomial(ray.alpha, heights[r]) / d;
                        double beta = ShellRay.Polynomial(ray.beta, heights[r]) / d;
                        worst = Math.Max(worst, Math.Max(Math.Abs(alpha - alphas[r]), Math.Abs(beta - betas[r])));
                        ++tested;
                    }
                }
            }
            Console.WriteLine("rational ray: " + tested + " points, worst alpha/beta error " + worst);
            EXPECT_GT(tested, 1000);
            EXPECT_LE(worst, 1e-7);
        }

        [TEST("Shell", "FaceCrossingsBoundTheRayInsideThePrism")]
        public static void Shell_FaceCrossingsBoundTheRayInsideThePrism()
        {
            RNG rng = new RNG(11);
            double[] heights = new double[3], alphas = new double[3], betas = new double[3];
            double[] distances = new double[10], crossingHeights = new double[10];
            int tested = 0, wrong = 0;
            for (int i = 0; i < 600; ++i)
            {
                ShellPrism prism = RandomPrism(ref rng, 0.8);
                SdfVector target = prism.Point(rng.Uniform<double>() * 0.4, rng.Uniform<double>() * 0.4, rng.Uniform<double>());
                SdfVector w = new SdfVector(rng.Uniform<double>() - 0.5, rng.Uniform<double>() - 0.5, rng.Uniform<double>() - 0.7).Normalized();
                SdfVector o = target - w * 3;
                int count = prism.FaceCrossings(o, w, 1e9, distances, crossingHeights, new double[2]);
                double tIn = double.MaxValue, tOut = 0;
                for (int k = 0; k < count; ++k)
                {
                    tIn = Math.Min(tIn, distances[k]);
                    tOut = Math.Max(tOut, distances[k]);
                }
                if (count < 2)
                {
                    ++wrong;
                    continue;
                }
                ++tested;
                // Dense sampling: the ray is inside exactly between the first and the last crossing
                int steps = 6000;
                double firstInside = double.MaxValue, lastInside = -1;
                for (int s = 0; s <= steps; ++s)
                {
                    double t = 1 + 4.0 * s / steps;
                    SdfVector q = o + w * t;
                    int found = prism.Inverse(q, 0, 1, heights, alphas, betas);
                    for (int k = 0; k < found; ++k)
                    {
                        if (alphas[k] >= 0 && betas[k] >= 0 && alphas[k] + betas[k] <= 1)
                        {
                            firstInside = Math.Min(firstInside, t);
                            lastInside = Math.Max(lastInside, t);
                        }
                    }
                }
                if (firstInside < double.MaxValue && (Math.Abs(firstInside - tIn) > 0.002 || Math.Abs(lastInside - tOut) > 0.002))
                {
                    ++wrong;
                }
            }
            Console.WriteLine("face crossings: " + tested + " rays, " + wrong + " wrong");
            EXPECT_EQ(wrong, 0);
            EXPECT_GT(tested, 300);
        }

        // A texture of the texture space position: returns one coordinate (0 = u, 1 = v, 2 = w), with exact region bounds
        private sealed class CoordinateTexture : FloatTexture
        {
            public CoordinateTexture(int axis)
            {
                this.axis = axis;
            }

            public override Float Evaluate(TextureEvalContext ctx)
            {
                return ctx.p[axis];
            }

            public override Float MinValue()
            {
                return -1000;
            }

            public override Float MaxValue()
            {
                return 1000;
            }

            public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
            {
                lo = region.pMin[axis];
                hi = region.pMax[axis];
            }

            public override string ToString()
            {
                return "[ CoordinateTexture ]";
            }

            private readonly int axis;
        }

        private static ShellMapTexture MakeShellMap(int axis, bool tightBounds, double outside)
        {
            DisplacedMeshTests.Cap(6, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            return new ShellMapTexture(new Transform(), new CoordinateTexture(axis), indices, p, n, uv, 0.15, 1, 1, outside, tightBounds);
        }

        [TEST("Shell", "ShellMapFindsEveryPointOfTheShell")]
        public static void Shell_ShellMapFindsEveryPointOfTheShell()
        {
            DisplacedMeshTests.Cap(6, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            ShellMapTexture texture = MakeShellMap(2, false, -5);
            SdfVector[] corner = new SdfVector[3];
            SdfVector[] offset = new SdfVector[3];
            RNG rng = new RNG(13);
            int missing = 0, wrongHeight = 0, wrongUv = 0;
            int total = 6000;
            for (int i = 0; i < total; ++i)
            {
                int t = (int)(rng.Uniform<Float>() * (indices.Length / 3 - 1));
                for (int k = 0; k < 3; ++k)
                {
                    Point3f point = p[indices[3 * t + k]];
                    Normal3f normal = pbrt.Normalize(n[indices[3 * t + k]]);
                    corner[k] = new SdfVector(point.x, point.y, point.z);
                    offset[k] = new SdfVector(normal.x, normal.y, normal.z) * 0.15;
                }
                ShellPrism prism = new ShellPrism(corner, offset);
                double alpha = 0.02 + 0.96 * rng.Uniform<double>(), beta = (0.98 - alpha) * rng.Uniform<double>(), h = 0.02 + 0.96 * rng.Uniform<double>();
                SdfVector q = prism.Point(alpha, beta, h);
                TextureEvalContext ctx = default;
                ctx.p = new Point3f((Float)q.x, (Float)q.y, (Float)q.z);
                double w = texture.Evaluate(ctx);
                if (w == -5)
                {
                    ++missing;
                    continue;
                }
                // The height of the point in whichever prism contains it (the prisms of a smooth cap overlap a little at the sides)
                if (Math.Abs(w - h) > 0.05)
                {
                    ++wrongHeight;
                }
                ShellMapTexture uTexture = MakeShellMap(0, false, -5);
                double u = uTexture.Evaluate(ctx);
                Point2f t0 = uv[indices[3 * t]], t1 = uv[indices[3 * t + 1]], t2 = uv[indices[3 * t + 2]];
                double expectedU = t0.x + alpha * (t1.x - t0.x) + beta * (t2.x - t0.x);
                if (Math.Abs(u - expectedU) > 0.02)
                {
                    ++wrongUv;
                }
            }
            Console.WriteLine("shellmap lookup: " + total + " points, " + missing + " missing, " + wrongHeight + " wrong heights, " + wrongUv + " wrong u");
            EXPECT_EQ(missing, 0);
            EXPECT_EQ(wrongHeight, 0);
            EXPECT_EQ(wrongUv, 0);
            // Far from the shell the value is the outside value
            TextureEvalContext far = default;
            far.p = new Point3f(0, 0, 5);
            EXPECT_EQ(texture.Evaluate(far), -5f);
        }

        [TEST("Shell", "ShellMapRegionBoundsContainTheValues")]
        public static void Shell_ShellMapRegionBoundsContainTheValues()
        {
            ShellMapTexture texture = MakeShellMap(2, true, 0);
            RNG rng = new RNG(17);
            int violations = 0, boxes = 0;
            double averageWidth = 0;
            for (int i = 0; i < 300; ++i)
            {
                Point3f center = new Point3f((Float)(1.2 * rng.Uniform<Float>() - 0.6), (Float)(1.2 * rng.Uniform<Float>() - 0.6), 0.75f + 0.3f * rng.Uniform<Float>());
                Float half = 0.02f + 0.05f * rng.Uniform<Float>();
                Bounds3f box = new Bounds3f(center - new Vector3f(half, half, half), center + new Vector3f(half, half, half));
                texture.RegionBounds(box, out Float lo, out Float hi);
                bool overlaps = false;
                for (int k = 0; k < 400; ++k)
                {
                    TextureEvalContext ctx = default;
                    ctx.p = new Point3f(box.pMin.x + (box.pMax.x - box.pMin.x) * rng.Uniform<Float>(),
                                        box.pMin.y + (box.pMax.y - box.pMin.y) * rng.Uniform<Float>(),
                                        box.pMin.z + (box.pMax.z - box.pMin.z) * rng.Uniform<Float>());
                    Float value = texture.Evaluate(ctx);
                    if (value < lo - 1e-5f || value > hi + 1e-5f)
                    {
                        if (violations < 6)
                        {
                            Console.WriteLine("  value " + value + " bounds [" + lo + ", " + hi + "] at " + ctx.p + " box " + box);
                        }
                        ++violations;
                    }
                    if (value != 0 || true)
                    {
                        overlaps = true;
                    }
                }
                if (overlaps)
                {
                    ++boxes;
                    averageWidth += hi - lo;
                }
            }
            Console.WriteLine("shellmap bounds: " + boxes + " boxes, " + violations + " values outside the bounds, mean width " + averageWidth / boxes);
            EXPECT_EQ(violations, 0);
            // The global range of w is [0, 1]; the bounds over small boxes are narrower
            EXPECT_LE(averageWidth / boxes, 0.8);
        }

        [TEST("Shell", "ShellMapIsEmptyOutsideTheShell")]
        public static void Shell_ShellMapIsEmptyOutsideTheShell()
        {
            ShellMapTexture texture = MakeShellMap(2, false, 0);
            RNG rng = new RNG(19);
            int nonzeroOutside = 0, nonzero = 0;
            for (int i = 0; i < 200000; ++i)
            {
                Point3f q = new Point3f((Float)(2 * rng.Uniform<Float>() - 1), (Float)(2 * rng.Uniform<Float>() - 1), (Float)(0.5 + 0.9 * rng.Uniform<Float>()));
                TextureEvalContext ctx = default;
                ctx.p = q;
                if (texture.Evaluate(ctx) != 0)
                {
                    ++nonzero;
                    double r = Math.Sqrt((double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z);
                    if (r < 0.9 || r > 1.23 || Math.Abs(q.x) > 0.95 || Math.Abs(q.y) > 0.95)
                    {
                        if (nonzeroOutside < 8)
                        {
                            Console.WriteLine("  point " + q + " r " + r);
                        }
                        ++nonzeroOutside;
                    }
                }
            }
            Console.WriteLine("outside the shell: " + nonzero + " nonzero values, " + nonzeroOutside + " outside the radius or footprint");
            EXPECT_GT(nonzero, 1000);
            EXPECT_EQ(nonzeroOutside, 0);
        }
    }
}
