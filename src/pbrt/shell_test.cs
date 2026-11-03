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
    }
}
