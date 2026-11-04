// (not in pbrt-v4) cubicsolver_test.cs
//
// Tests of the cubic solver of Reshetov's ribbons (cubicsolver.cs).

using System;
using testing;

namespace pbrt
{
    public static unsafe class CubicSolverTests
    {
        private static int Missed(double a, double[] expected, double tolerance)
        {
            double b = -a * (expected[0] + expected[1] + expected[2]);
            double c = a * (expected[0] * expected[1] + expected[0] * expected[2] + expected[1] * expected[2]);
            double d = -a * expected[0] * expected[1] * expected[2];
            double* roots = stackalloc double[3];
            int count = RobustCubic.Solve(a, b, c, d, roots);
            int missed = 0;
            foreach (double root in expected)
            {
                bool found = false;
                for (int i = 0; i < count; ++i)
                {
                    if (Math.Abs(roots[i] - root) <= tolerance)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    ++missed;
                }
            }
            return missed;
        }

        [TEST("Cubic", "SeparatedRootsAreFound")]
        public static void SeparatedRootsAreFound()
        {
            Random random = new Random(1);
            int total = 0, missed = 0;
            for (int k = 0; k < 20000; ++k)
            {
                double[] expected = { random.NextDouble() * 5 - 2, random.NextDouble() * 5 - 2, random.NextDouble() * 5 - 2 };
                Array.Sort(expected);
                // roots at least 0.01 apart
                if (expected[1] - expected[0] < 0.01 || expected[2] - expected[1] < 0.01)
                {
                    continue;
                }
                double a = (random.NextDouble() + 0.05) * Math.Pow(10, random.Next(-3, 4)) * (random.Next(2) == 0 ? 1 : -1);
                ++total;
                missed += Missed(a, expected, 1e-7) > 0 ? 1 : 0;
            }
            Console.WriteLine("separated roots: cubics " + total + " with a missed root " + missed);
            CHECK_GT(total, 10000);
            CHECK_EQ(missed, 0);
        }

        [TEST("Cubic", "RootsOfVeryDifferentSizeAreFound")]
        public static void RootsOfVeryDifferentSizeAreFound()
        {
            // The sum of the roots is -b / a: a root of a big amplitude hurts the small ones in the plain solvers
            Random random = new Random(2);
            int total = 0, missed = 0;
            for (int k = 0; k < 20000; ++k)
            {
                double big = Math.Pow(10, random.NextDouble() * 4 + 1) * (random.Next(2) == 0 ? 1 : -1);
                double[] expected = { random.NextDouble(), random.NextDouble() * 0.5 + 1, big };
                if (Math.Abs(expected[0] - expected[1]) < 0.01)
                {
                    continue;
                }
                ++total;
                missed += Missed(1, expected, 1e-6) > 0 ? 1 : 0;
            }
            Console.WriteLine("roots of different size: cubics " + total + " with a missed root " + missed);
            CHECK_LT(missed, total / 100);
        }

        [TEST("Cubic", "DegenerateCubicsFallBack")]
        public static void DegenerateCubicsFallBack()
        {
            double* roots = stackalloc double[3];
            // linear: 2u - 1 = 0
            CHECK_EQ(RobustCubic.Solve(0, 0, 2, -1, roots), 1);
            CHECK_LT(Math.Abs(roots[0] - 0.5), 1e-12);
            // quadratic: (u - 0.25)(u - 0.75)
            CHECK_EQ(RobustCubic.Solve(0, 1, -1, 0.1875, roots), 2);
            CHECK_LT(Math.Abs(roots[0] - 0.25), 1e-12);
            CHECK_LT(Math.Abs(roots[1] - 0.75), 1e-12);
            // a root at zero: u (u - 0.5) (u - 2)
            int count = RobustCubic.Solve(1, -2.5, 1, 0, roots);
            CHECK_EQ(count, 3);
            CHECK_LT(Math.Abs(roots[0]), 1e-12);
            // one real root: u^3 + u + 1
            CHECK_EQ(RobustCubic.Solve(1, 0, 1, 1, roots), 1);
            CHECK_LT(Math.Abs(roots[0] + 0.6823278038280193), 1e-12);
            // nothing
            CHECK_EQ(RobustCubic.Solve(0, 0, 0, 1, roots), 0);
        }
    }
}
