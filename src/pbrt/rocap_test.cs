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

        // The reference for the strand with flat caps: the union of the spheres cut by the planes of the ends; the entry of the ray into it
        private static double FlatBruteForce(RocapSegment segment, SdfVector o, SdfVector d, int count)
        {
            SdfVector p0 = segment.Position(0), p1 = segment.Position(1);
            SdfVector t0 = segment.Velocity(0).Normalized(), t1 = segment.Velocity(1).Normalized();
            // The interval of the ray inside the slab: (p - p0) . t0 >= 0 and (p - p1) . t1 <= 0
            double low = 0, high = double.MaxValue;
            double[] numerators = { SdfVector.Dot(p0 - o, t0), SdfVector.Dot(p1 - o, t1) };
            double[] slopes = { SdfVector.Dot(d, t0), SdfVector.Dot(d, t1) };
            // first plane: t * slope0 >= numerator0; second: t * slope1 <= numerator1
            if (slopes[0] > 0)
            {
                low = Math.Max(low, numerators[0] / slopes[0]);
            }
            else if (slopes[0] < 0)
            {
                high = Math.Min(high, numerators[0] / slopes[0]);
            }
            else if (numerators[0] > 0)
            {
                return double.MaxValue;
            }
            if (slopes[1] > 0)
            {
                high = Math.Min(high, numerators[1] / slopes[1]);
            }
            else if (slopes[1] < 0)
            {
                low = Math.Max(low, numerators[1] / slopes[1]);
            }
            else if (numerators[1] < 0)
            {
                return double.MaxValue;
            }
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
                if (h < 0)
                {
                    continue;
                }
                double a = b - Math.Sqrt(h), e = b + Math.Sqrt(h);
                double start = Math.Max(a, low), finish = Math.Min(e, high);
                if (start <= finish && start > 0 && start < best)
                {
                    best = start;
                }
            }
            return best;
        }

        [TEST("Rocap", "FlatCapsMatchTheCutSpheres")]
        public static void FlatCapsMatchTheCutSpheres()
        {
            RocapSegment segment = Hook(0.25, 0.2, 0.15, 0.2);
            segment.FlatCaps = true;
            Random random = new Random(21);
            segment.Bounds(out SdfVector low, out SdfVector high);
            SdfVector center = 0.5 * (low + high);
            double size = (high - low).Length();
            int total = 0, missed = 0, extra = 0, caps = 0;
            double worst = 0;
            for (int k = 0; k < 3000; ++k)
            {
                SdfVector dir = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
                SdfVector target = center + new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5) * size * 0.7;
                SdfVector origin = target - dir * (3 * size);
                double expected = FlatBruteForce(segment, origin, dir, 40000);
                bool hit = segment.Intersect(origin, dir, 0, 1e30, out double t, out double u, out SdfVector normal);
                if (expected < double.MaxValue)
                {
                    ++total;
                    if (!hit)
                    {
                        ++missed;
                        continue;
                    }
                    if (Math.Abs(SdfVector.Dot(normal, segment.Velocity(0).Normalized())) > 0.999999 || Math.Abs(SdfVector.Dot(normal, segment.Velocity(1).Normalized())) > 0.999999)
                    {
                        ++caps;
                    }
                    worst = Math.Max(worst, Math.Abs(t - expected) / size);
                }
                else if (hit)
                {
                    ++extra;
                }
            }
            Console.WriteLine("rocap flat caps: hits " + total + " (on a cap " + caps + ") missed " + missed + " extra " + extra + " worst t " + worst);
            CHECK_GT(total, 200);
            CHECK_GT(caps, 3);
            CHECK_LT(missed, 5);
            CHECK_LT(extra, 5);
            CHECK_LT(worst, 5e-4);
        }
    }
}
