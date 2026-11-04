// (not in pbrt-v4) rocap_test.cs
//
// Tests of the roving capsules (rocap.cs): the hits against the union of many spheres along the strand.

using System;
using testing;

namespace pbrt
{
    public static unsafe class RocapTests
    {
        private static double BruteForce(RocapSegment segment, SdfVector o, SdfVector d, int count)
        {
            double best = double.MaxValue;
            for (int i = 0; i <= count; ++i)
            {
                double u = (double)i / count;
                double r = segment.Radius(u);
                if (r <= 0)
                {
                    continue;
                }
                SdfVector c = segment.Position(u) - o;
                double b = SdfVector.Dot(c, d);
                double h = b * b - SdfVector.Dot(c, c) + r * r;
                if (h >= 0)
                {
                    double t = b - Math.Sqrt(h);
                    if (t > 0 && t < best)
                    {
                        best = t;
                    }
                }
            }
            return best;
        }

        private static void Compare(RocapSegment segment, int rays, out int total, out int missed, out int extra, out double worstT, out double worstSurface,
                                    out double worstNormal)
        {
            Random random = new Random(11);
            segment.Bounds(out SdfVector low, out SdfVector high);
            SdfVector center = 0.5 * (low + high);
            double size = (high - low).Length();
            total = missed = extra = 0;
            worstT = worstSurface = worstNormal = 0;
            for (int k = 0; k < rays; ++k)
            {
                SdfVector dir = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
                SdfVector target = center + new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5) * size * 0.7;
                SdfVector origin = target - dir * (3 * size);
                double expected = BruteForce(segment, origin, dir, 40000);
                bool hit = segment.Intersect(origin, dir, 0, 1e30, out double t, out double u, out SdfVector normal);
                if (expected < double.MaxValue)
                {
                    ++total;
                    if (!hit)
                    {
                        ++missed;
                        continue;
                    }
                    worstT = Math.Max(worstT, Math.Abs(t - expected) / size);
                    // The hit is on the surface of the sphere at u, with the normal outward
                    SdfVector point = origin + dir * t;
                    double surface = Math.Abs((point - segment.Position(u)).Length() - segment.Radius(u));
                    worstSurface = Math.Max(worstSurface, surface / size);
                    SdfVector radial = (point - segment.Position(u)).Normalized();
                    worstNormal = Math.Max(worstNormal, (radial - normal).Length());
                }
                else if (hit)
                {
                    ++extra;
                }
            }
        }

        private static RocapSegment Hook(double r0, double r1, double r2, double r3)
        {
            SdfVector[] p =
            {
                new SdfVector(-1, 0, 0), new SdfVector(-0.6, 2.2, 0.4), new SdfVector(0.8, -1.8, -0.5), new SdfVector(1.1, 0.3, 0.2)
            };
            return new RocapSegment(p, new double[] { r0, r1, r2, r3 }, 0, 1);
        }

        [TEST("Rocap", "ConstantRadiusMatchesTheSpheres")]
        public static void ConstantRadiusMatchesTheSpheres()
        {
            RocapSegment segment = Hook(0.12, 0.12, 0.12, 0.12);
            Compare(segment, 2500, out int total, out int missed, out int extra, out double worstT, out double worstSurface, out double worstNormal);
            Console.WriteLine("rocap constant: hits " + total + " missed " + missed + " extra " + extra + " worst t " + worstT + " surface " + worstSurface +
                              " normal " + worstNormal);
            CHECK_GT(total, 200);
            CHECK_LT(missed, 3);
            CHECK_LT(extra, 3);
            CHECK_LT(worstT, 2e-4);
            CHECK_LT(worstSurface, 1e-6);
        }

        [TEST("Rocap", "TaperedThickStrandMatchesTheSpheres")]
        public static void TaperedThickStrandMatchesTheSpheres()
        {
            RocapSegment segment = Hook(0.45, 0.3, 0.08, 0.0);
            Compare(segment, 2500, out int total, out int missed, out int extra, out double worstT, out double worstSurface, out double worstNormal);
            Console.WriteLine("rocap tapered: hits " + total + " missed " + missed + " extra " + extra + " worst t " + worstT + " surface " + worstSurface +
                              " normal " + worstNormal);
            CHECK_GT(total, 400);
            CHECK_LT(missed, 5);
            CHECK_LT(extra, 5);
            CHECK_LT(worstT, 2e-4);
            CHECK_LT(worstSurface, 1e-6);
        }

        [TEST("Rocap", "ThinStrandMatchesTheSpheres")]
        public static void ThinStrandMatchesTheSpheres()
        {
            RocapSegment segment = Hook(0.01, 0.02, 0.01, 0.015);
            Compare(segment, 20000, out int total, out int missed, out int extra, out double worstT, out double worstSurface, out double worstNormal);
            Console.WriteLine("rocap thin: hits " + total + " missed " + missed + " extra " + extra + " worst t " + worstT + " surface " + worstSurface +
                              " normal " + worstNormal);
            CHECK_GT(total, 20);
            CHECK_LT(missed, 3);
            CHECK_LT(extra, 3);
            CHECK_LT(worstT, 1e-3);
        }
    }
}
