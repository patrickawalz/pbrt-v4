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

        // Rays at a low angle to the strand (1 to 10 degrees from its direction, at a point of its surface), over a strand
        // that curves tightly: a ray can run along the strand, meet it more than once, and enter spheres over a long range of
        // the parameter. Against the spheres one by one.
        private static void Graze(RocapSegment segment, int rays, out int total, out int missed, out int extra, out double worstT)
        {
            Random random = new Random(23);
            segment.Bounds(out SdfVector low, out SdfVector high);
            double size = (high - low).Length();
            total = missed = extra = 0;
            worstT = 0;
            for (int k = 0; k < rays; ++k)
            {
                double u = 0.02 + 0.96 * random.NextDouble();
                SdfVector along = segment.Velocity(u).Normalized();
                SdfVector side = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5);
                side = (side - along * SdfVector.Dot(side, along)).Normalized();
                // A point of the sphere of u, a little inside or outside its surface, and a direction near that of the strand
                SdfVector target = segment.Position(u) + side * (segment.Radius(u) * (0.7 + 0.4 * random.NextDouble()));
                double tilt = (1 + 9 * random.NextDouble()) * Math.PI / 180 * (random.Next(2) == 0 ? 1 : -1);
                SdfVector turn = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5);
                turn = (turn - along * SdfVector.Dot(turn, along)).Normalized();
                SdfVector dir = (along * Math.Cos(tilt) + turn * Math.Sin(tilt)).Normalized();
                if (random.Next(2) == 0)
                {
                    dir = -dir;
                }
                SdfVector origin = target - dir * (2 * size);
                double expected = BruteForce(segment, origin, dir, 200000);
                bool hit = segment.Intersect(origin, dir, 0, 1e30, out double t, out double hitU, out SdfVector normal);
                if (expected < double.MaxValue)
                {
                    ++total;
                    if (!hit)
                    {
                        ++missed;
                        continue;
                    }
                    worstT = Math.Max(worstT, Math.Abs(t - expected) / size);
                }
                else if (hit)
                {
                    ++extra;
                }
            }
        }

        [TEST("Rocap", "GrazingRaysMatchTheSpheres")]
        public static void GrazingRaysMatchTheSpheres()
        {
            for (int roots = 0; roots < 2; ++roots)
            {
                for (int which = 0; which < 3; ++which)
                {
                    RocapSegment segment = which == 0 ? Hook(0.12, 0.12, 0.12, 0.12) : which == 1 ? Hook(0.02, 0.03, 0.02, 0.025) : Hook(0.3, 0.2, 0.06, 0.02);
                    segment.RootRefinement = roots == 1;
                    Graze(segment, 3000, out int total, out int missed, out int extra, out double worstT);
                    Console.WriteLine("rocap grazing rays (" + (roots == 1 ? "root of the slope" : "golden section") + "), " + (which == 0 ? "constant" : which == 1 ? "thin" : "tapered") +
                                      ": hits " + total + " missed " + missed + " extra " + extra + " worst t " + worstT);
                    CHECK_GT(total, 1500);
                    CHECK_EQ(missed, 0);
                    CHECK_EQ(extra, 0);
                    CHECK_LT(worstT, 1e-6);
                }
            }
            // The rays from all sides too, and the straight strand
            for (int which = 0; which < 3; ++which)
            {
                RocapSegment segment = which == 0 ? Hook(0.12, 0.12, 0.12, 0.12) : which == 1 ? Hook(0.45, 0.3, 0.08, 0.0) : Hook(0.01, 0.02, 0.01, 0.015);
                segment.RootRefinement = true;
                Compare(segment, which == 2 ? 20000 : 2500, out int total, out int missed, out int extra, out double worstT, out double worstSurface, out double worstNormal);
                Console.WriteLine("rocap from all sides (root of the slope), " + (which == 0 ? "constant" : which == 1 ? "tapered thick" : "thin") + ": hits " + total + " missed " +
                                  missed + " extra " + extra + " worst t " + worstT + " surface " + worstSurface);
                CHECK_LT(missed, which == 1 ? 5 : 3);
                CHECK_LT(extra, which == 1 ? 5 : 3);
                CHECK_LT(worstT, which == 2 ? 1e-3 : 2e-4);
            }
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

        // A straight strand (the basis "linear") is a cylinder: rays at every distance from the axis up to the silhouette, at
        // right and at slanted angles to it, hit where they hit the cylinder
        [TEST("Rocap", "StraightStrandIsACylinder")]
        public static void StraightStrandIsACylinder()
        {
            const double r = 0.1;
            SdfVector[] p = { new SdfVector(0, 0, 0), new SdfVector(4.0 / 3, 0, 0), new SdfVector(8.0 / 3, 0, 0), new SdfVector(4, 0, 0) };
            RocapSegment segment = new RocapSegment(p, new double[] { r, r, r, r }, 0, 1);
            Random random = new Random(5);
            int rays = 0, missed = 0;
            double worst = 0;
            foreach (double slant in new double[] { 0, 1, 4 })
            {
                SdfVector d = new SdfVector(slant, 0, 1).Normalized();
                for (int i = 0; i < 20000; ++i)
                {
                    // The distance from the axis: most of the rays near the silhouette, up to 1e-9 of the radius from it (a ray
                    // nearer than 1e-13 is on the silhouette to the rounding of the numbers, and may miss)
                    double fraction = 1 - (1e-9 + Math.Pow(random.NextDouble(), 3) * 0.999);
                    double y = (random.Next(2) == 0 ? -1 : 1) * r * fraction, x = 0.5 + 3 * random.NextDouble();
                    double depth = Math.Sqrt(r * r - y * y);
                    // The point of the cylinder that is hit, and the ray to it from 3 away
                    SdfVector hit = new SdfVector(x, y, -depth);
                    SdfVector o = hit - d * 3;
                    ++rays;
                    if (!segment.Intersect(o, d, 0, 1e30, out double t, out double u, out SdfVector normal))
                    {
                        ++missed;
                        continue;
                    }
                    worst = Math.Max(worst, Math.Abs(t - 3));
                }
            }
            Console.WriteLine("rocap straight strand: " + missed + " of " + rays + " rays that hit the cylinder are missed; the hits are off by up to " + worst);
            CHECK_EQ(missed, 0);
            CHECK_LT(worst, 1e-6);
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

        private static RocapShape LightShape(double r0, double r1, double r2, double r3, bool flat)
        {
            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = new Transform();
            settings.objectFromRender = new Transform();
            settings.epsilon = 1e-6;
            RocapSegment segment = Hook(r0, r1, r2, r3);
            segment.FlatCaps = flat;
            return new RocapShape(settings, segment);
        }

        // A straight strand of the radius 0.2 from (0, 0, 0) to (3, 0, 0)
        private static RocapShape StraightShape(bool flat)
        {
            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = new Transform();
            settings.objectFromRender = new Transform();
            settings.epsilon = 1e-6;
            SdfVector[] p = { new SdfVector(0, 0, 0), new SdfVector(1, 0, 0), new SdfVector(2, 0, 0), new SdfVector(3, 0, 0) };
            RocapSegment segment = new RocapSegment(p, new double[] { 0.2, 0.2, 0.2, 0.2 }, 0, 1);
            segment.FlatCaps = flat;
            return new RocapShape(settings, segment);
        }

        // The samples by area: the density of a sample is the density that the shape gives for its point, the mean of 1 / pdf
        // is the area (of the tube and the two caps: known for the straight strand), and the caps get their share
        [TEST("Rocap", "AreaDensityMatchesTheSamples")]
        public static void AreaDensityMatchesTheSamples()
        {
            for (int which = 0; which < 4; ++which)
            {
                bool flat = which % 2 == 1;
                RocapShape shape = which < 2 ? StraightShape(flat) : LightShape(0.25, 0.2, 0.15, 0.2, flat);
                Random random = new Random(21);
                const int count = 200000;
                int mismatched = 0, valid = 0, onCaps = 0;
                double sum = 0;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    if (!ss)
                    {
                        continue;
                    }
                    ++valid;
                    sum += 1.0 / ss.value().pdf;
                    Float pdf = shape.PDF(ss.value().intr);
                    if (Math.Abs(pdf - ss.value().pdf) > 1e-3 * ss.value().pdf)
                    {
                        if (++mismatched <= 6)
                        {
                            Console.WriteLine("  density " + pdf + " against " + ss.value().pdf + " at u " + ss.value().intr.uv.x + " v " + ss.value().intr.uv.y + " p " + ss.value().intr.p());
                        }
                    }
                    Float along = ss.value().intr.uv.x;
                    onCaps += along == 0 || along == 1 ? 1 : 0;
                }
                double area = sum / count;
                Console.WriteLine("rocap area (" + (which < 2 ? "straight" : "hook") + (flat ? ", flat caps" : ", round caps") + "): by samples " + area + ", Area() " +
                                  shape.Area() + "; " + valid + " of " + count + " samples valid, " + onCaps + " on the caps, " + mismatched + " densities differ");
                // (a point within rounding of the circle where a round cap meets the tube can be taken for either)
                CHECK_LE(mismatched, count / 20000);
                // (the flat caps of the hook are small: the planes of its ends cut the strand elsewhere too)
                CHECK_GT(onCaps, which == 3 ? 100 : count / 100);
                CHECK_LT(Math.Abs(area - shape.Area()) / area, 0.02);
                if (which < 2)
                {
                    double expected = 2 * Math.PI * 0.2 * 3 + (flat ? 2 * Math.PI * 0.04 : 4 * Math.PI * 0.04);
                    CHECK_LT(Math.Abs(area - expected) / expected, 0.01);
                }
            }
        }

        // From a point that looks at the end of the strand the caps are most of what is seen: the solid angle by the directions
        // that have a density and by the samples agree, with round and with flat caps, and most samples face the point
        [TEST("Rocap", "CapsAndTheNearSideAreSampled")]
        public static void CapsAndTheNearSideAreSampled()
        {
            for (int which = 0; which < 3; ++which)
            {
                RocapShape shape = StraightShape(which == 1);
                Point3f reference = which < 2 ? new Point3f(6.0f, 0.3f, 0.1f) : new Point3f(1.5f, 2.0f, 1.0f);
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(reference, new Vector3f(0, 0, 0)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
                Random random = new Random(23);
                const int count = 300000;
                Vector3f toCenter = pbrt.Normalize(new Point3f(which < 2 ? 3.0f : 1.5f, 0, 0) - reference);
                double cosineCone = Math.Cos(which < 2 ? 0.25 : 1.2);
                double coneSolidAngle = 2 * Math.PI * (1 - cosineCone);
                Vector3f helper = Math.Abs(toCenter.x) < 0.9f ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0);
                Vector3f side = pbrt.Normalize(pbrt.Cross(toCenter, helper));
                Vector3f up = pbrt.Cross(toCenter, side);
                int hits = 0, any = 0;
                for (int i = 0; i < count; ++i)
                {
                    double cosTheta = 1 - random.NextDouble() * (1 - cosineCone);
                    double sinTheta = Math.Sqrt(Math.Max(0, 1 - cosTheta * cosTheta));
                    double phi = 2 * Math.PI * random.NextDouble();
                    Vector3f wi = toCenter * (Float)cosTheta + side * (Float)(sinTheta * Math.Cos(phi)) + up * (Float)(sinTheta * Math.Sin(phi));
                    hits += shape.PDF(ctx, wi) > 0 ? 1 : 0;
                    any += shape.IntersectP(ctx.SpawnRay(wi)) ? 1 : 0;
                }
                double byDirections = coneSolidAngle * hits / count;
                double bySamples = 0;
                int seen = 0;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    if (!ss)
                    {
                        continue;
                    }
                    Vector3f wi = pbrt.Normalize(ss.value().intr.p() - reference);
                    pstd.optional<ShapeIntersection> hit = shape.Intersect(ctx.SpawnRay(wi));
                    if (hit && pbrt.Distance(hit.value().intr.p(), ss.value().intr.p()) < 1e-3)
                    {
                        bySamples += 1.0 / ss.value().pdf;
                        ++seen;
                    }
                }
                bySamples /= count;
                Console.WriteLine("rocap from " + (which < 2 ? "its end" : "its side") + (which == 1 ? ", flat caps" : ", round caps") + ": solid angle by directions " + byDirections +
                                  ", by samples " + bySamples + "; " + hits + " of the " + any + " directions that hit have a density; " + seen + " of " + count + " samples are seen");
                // Nearly every direction that hits the strand has a density (not the ones that graze it, where the hit is not
                // told from the tube well enough): the caps are no longer left to the directions of the BSDF alone
                CHECK_GE(hits, any - any / 100);
                CHECK_LT(Math.Abs(bySamples - byDirections) / byDirections, 0.03);
            }
        }

        // The Bezier form of a curve in another basis is the same curve: its points against the basis itself,
        // P(u) = [u^3 u^2 u 1] M [g0 g1 g2 g3]
        [TEST("Rocap", "BasesGiveTheSameCurve")]
        public static void BasesGiveTheSameCurve()
        {
            string[] names = { "bezier", "b-spline", "catmull-rom", "hermite", "power" };
            Random random = new Random(31);
            foreach (string name in names)
            {
                CHECK(PatchBases.TryGet(name, out double[,] basis));
                int step = PatchBases.DefaultStep(name), curves = 4;
                double[] g = new double[4 + (curves - 1) * step];
                for (int i = 0; i < g.Length; ++i)
                {
                    g[i] = random.NextDouble() * 2 - 1;
                }
                double[] values = Rocaps.ToBezierCurves(g, basis, step);
                CHECK_EQ(values.Length, 4 * curves);
                double worst = 0;
                for (int s = 0; s < curves; ++s)
                {
                    for (int k = 0; k <= 20; ++k)
                    {
                        double u = k / 20.0, v = 1 - u;
                        double expected = 0;
                        for (int column = 0; column < 4; ++column)
                        {
                            double weight = u * u * u * basis[0, column] + u * u * basis[1, column] + u * basis[2, column] + basis[3, column];
                            expected += weight * g[s * step + column];
                        }
                        double value = values[4 * s] * (v * v * v) + values[4 * s + 1] * (3 * u * v * v) + values[4 * s + 2] * (3 * u * u * v) + values[4 * s + 3] * (u * u * u);
                        worst = Math.Max(worst, Math.Abs(value - expected));
                    }
                }
                Console.WriteLine("rocap basis " + name + " (step " + step + "): the Bezier form differs from the basis by " + worst);
                CHECK_LT(worst, 1e-12);
            }
        }

        [TEST("Rocap", "SolidAnglePdfMatchesTheSampler")]
        public static void SolidAnglePdfMatchesTheSampler()
        {
            RocapShape shape = LightShape(0.25, 0.2, 0.15, 0.2, false);
            Point3f reference = new Point3f(4.0f, 3.0f, -5.0f);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(reference, new Vector3f(0, 0, 0)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
            Random random = new Random(5);
            int compared = 0, mismatched = 0;
            for (int i = 0; i < 4000; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                if (!ss)
                {
                    continue;
                }
                Vector3f wi = pbrt.Normalize(ss.value().intr.p() - reference);
                Ray ray = ctx.SpawnRay(wi);
                pstd.optional<ShapeIntersection> hit = shape.Intersect(ray);
                // The pdf is that of the first hit: skip the samples hidden by another part of the strand, and the grazing ones
                if (!hit || pbrt.Distance(hit.value().intr.p(), ss.value().intr.p()) > 1e-3 || pbrt.AbsDot(ss.value().intr.n, wi) < 0.2)
                {
                    continue;
                }
                ++compared;
                Float pdf = shape.PDF(ctx, wi);
                if (Math.Abs(pdf - ss.value().pdf) > 0.05 * ss.value().pdf)
                {
                    ++mismatched;
                    if (mismatched < 6)
                    {
                        Console.WriteLine("mismatch pdf " + pdf + " sample pdf " + ss.value().pdf);
                    }
                }
            }
            Console.WriteLine("rocap solid angle pdf: compared " + compared + " mismatched " + mismatched);
            CHECK_GT(compared, 500);
            // A few percent of the samples differ by about 5% (the hit normal of the refined hit sets the angle around the strand)
            CHECK_LT(mismatched, compared / 50);
        }

        // The solid angle of the visible tube from a point, by the directions of the sphere (the hits with a pdf) and by the samples of the shape (the visible ones, 1 / pdf)
        [TEST("Rocap", "SamplesGiveTheSolidAngle")]
        public static void SamplesGiveTheSolidAngle()
        {
            RocapShape shape = LightShape(0.25, 0.2, 0.15, 0.2, false);
            Point3f reference = new Point3f(4.0f, 3.0f, -5.0f);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(reference, new Vector3f(0, 0, 0)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
            Random random = new Random(9);
            const int count = 400000;
            // by the directions: a cone toward the strand, the solid angle of the cone times the fraction that hits the tube
            Vector3f toCenter = pbrt.Normalize(new Vector3f(-reference.x, -reference.y, -reference.z));
            double cosineCone = Math.Cos(0.5);
            double coneSolidAngle = 2 * Math.PI * (1 - cosineCone);
            Vector3f helper = Math.Abs(toCenter.x) < 0.9f ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0);
            Vector3f side = pbrt.Normalize(pbrt.Cross(toCenter, helper));
            Vector3f up = pbrt.Cross(toCenter, side);
            int tubeHits = 0;
            for (int i = 0; i < count; ++i)
            {
                double cosTheta = 1 - random.NextDouble() * (1 - cosineCone);
                double sinTheta = Math.Sqrt(Math.Max(0, 1 - cosTheta * cosTheta));
                double phi = 2 * Math.PI * random.NextDouble();
                Vector3f wi = toCenter * (Float)cosTheta + side * (Float)(sinTheta * Math.Cos(phi)) + up * (Float)(sinTheta * Math.Sin(phi));
                if (shape.PDF(ctx, wi) > 0)
                {
                    ++tubeHits;
                }
            }
            double byDirections = coneSolidAngle * tubeHits / count;
            double bySamples = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                if (!ss)
                {
                    continue;
                }
                Vector3f wi = pbrt.Normalize(ss.value().intr.p() - reference);
                pstd.optional<ShapeIntersection> hit = shape.Intersect(ctx.SpawnRay(wi));
                if (hit && pbrt.Distance(hit.value().intr.p(), ss.value().intr.p()) < 1e-3)
                {
                    bySamples += 1.0 / ss.value().pdf;
                }
            }
            bySamples /= count;
            Console.WriteLine("rocap solid angle: by directions " + byDirections + " by samples " + bySamples + " area " + shape.Area());
            CHECK_GT(byDirections, 1e-3);
            CHECK_LT(Math.Abs(bySamples - byDirections) / byDirections, 0.03);
        }

        [TEST("Rocap", "StrandColorFollowsTheBasis")]
        public static void StrandColorFollowsTheBasis()
        {
            // bezier3: the first and last colors at the ends, a linear ramp of colors stays linear along the strand
            RGB[] bezier = new RGB[7];
            for (int i = 0; i < 7; ++i)
            {
                bezier[i] = new RGB(i / 6f, 1 - i / 6f, 0.5f);
            }
            SpectrumStrandColorTexture b = new SpectrumStrandColorTexture(bezier, true, false, SpectrumType.Albedo, RGBColorSpace.sRGB);
            CHECK_LT(Math.Abs(b.Color(0, 0).r - 0), 1e-6);
            CHECK_LT(Math.Abs(b.Color(1, 0).r - 1), 1e-6);
            CHECK_LT(Math.Abs(b.Color(0.5f, 0).r - 0.5), 1e-5);
            CHECK_LT(Math.Abs(b.Color(0.25f, 0).r - 0.25), 1e-5);
            // bspline2: five points, three segments; the ends clamped, the joints at the midpoints of the points, continuous
            RGB[] spline = { new RGB(1, 0, 0), new RGB(0, 1, 0), new RGB(0, 0, 1), new RGB(1, 1, 0), new RGB(0, 1, 1) };
            SpectrumStrandColorTexture q = new SpectrumStrandColorTexture(spline, false, false, SpectrumType.Albedo, RGBColorSpace.sRGB);
            CHECK_LT(Math.Abs(q.Color(0, 0).r - 1), 1e-6);
            CHECK_LT(Math.Abs(q.Color(1, 0).b - 1), 1e-6);
            // the joint between the first and second segment (x = 1/3): the midpoint of the second and third color
            RGB joint = q.Color(1f / 3, 0);
            CHECK_LT(Math.Abs(joint.g - 0.5), 1e-5);
            CHECK_LT(Math.Abs(joint.b - 0.5), 1e-5);
            RGB before = q.Color(1f / 3 - 1e-4f, 0), after = q.Color(1f / 3 + 1e-4f, 0);
            CHECK_LT(Math.Abs(before.g - after.g), 1e-3);
            CHECK_LT(Math.Abs(before.b - after.b), 1e-3);
        }

        [TEST("Rocap", "LocalAndGlobalStrandColorsMultiply")]
        public static void LocalAndGlobalStrandColorsMultiply()
        {
            RGB[] own = { new RGB(1, 0.5f, 0.25f) };
            int number = StrandColors.Register(own, false);
            RGB[] gradient = { new RGB(1, 1, 1), new RGB(0.5f, 0.5f, 0.5f), new RGB(0, 0, 0) };
            SpectrumStrandColorTexture both = new SpectrumStrandColorTexture(gradient, false, true, SpectrumType.Albedo, RGBColorSpace.sRGB);
            SpectrumStrandColorTexture onlyLocal = new SpectrumStrandColorTexture(null, false, true, SpectrumType.Albedo, RGBColorSpace.sRGB);
            RGB product = both.Color(0, number);
            CHECK_LT(Math.Abs(product.g - 0.5), 1e-6);
            CHECK_LT(Math.Abs(product.b - 0.25), 1e-6);
            RGB ownOnly = onlyLocal.Color(0.7f, number);
            CHECK_LT(Math.Abs(ownOnly.r - 1), 1e-6);
            CHECK_LT(Math.Abs(ownOnly.b - 0.25), 1e-6);
            // a strand without colors leaves the global ones alone
            RGB plain = both.Color(0, 0);
            CHECK_LT(Math.Abs(plain.g - 1), 1e-6);
        }

        // A ray that leaves the surface to the outside must not hit the same strand again right at its origin (the dark specks of a hair render)
        [TEST("Rocap", "RaysLeavingTheSurfaceDoNotHitItAgain")]
        public static void RaysLeavingTheSurfaceDoNotHitItAgain()
        {
            RocapSegment segment = Hook(0.02, 0.018, 0.015, 0.012);
            Random random = new Random(17);
            segment.Bounds(out SdfVector low, out SdfVector high);
            SdfVector center = 0.5 * (low + high);
            double size = (high - low).Length();
            int hits = 0, again = 0, near = 0;
            for (int k = 0; k < 60000; ++k)
            {
                SdfVector dir = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
                SdfVector target = center + new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5) * size * 0.7;
                SdfVector origin = target - dir * (3 * size);
                if (!segment.Intersect(origin, dir, 0, 1e30, out double t, out double u, out SdfVector normal))
                {
                    continue;
                }
                ++hits;
                SdfVector point = origin + dir * t;
                // leave to the outside, from a point offset along the normal by the error bound of a float position
                SdfVector leave = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
                if (SdfVector.Dot(leave, normal) < 0)
                {
                    leave = -1 * leave;
                }
                SdfVector start = point + normal * 1e-6;
                if (segment.Intersect(start, leave, 1e-6 * size, 1e30, out double t2, out double u2, out SdfVector n2))
                {
                    // a legitimate hit of another part of the strand is farther than a few radii or comes from a bend; a hit at the origin is a fault
                    if (t2 < 0.01 * segment.Radius(u))
                    {
                        ++again;
                    }
                    if (t2 < 0.5 * segment.Radius(u))
                    {
                        ++near;
                    }
                }
            }
            Console.WriteLine("rocap re-hits: hits " + hits + " again at the origin " + again + " within half a radius " + near);
            CHECK_GT(hits, 500);
            CHECK_LT(again, 1);
        }
    }
}
