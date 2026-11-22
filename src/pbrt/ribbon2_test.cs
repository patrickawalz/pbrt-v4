// (not in pbrt-v4) ribbon2_test.cs
//
// Tests of the "ribbon2" shape (ribbon2shape.cs): the bases of its centerline and its sampling as a light.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class Ribbon2Tests
    {
        private static SdfVector[] Spiral(int count, double radius, double rise)
        {
            SdfVector[] points = new SdfVector[count];
            for (int i = 0; i < count; ++i)
            {
                double angle = i * 0.7;
                points[i] = new SdfVector(radius * Math.Cos(angle), radius * Math.Sin(angle), rise * i);
            }
            return points;
        }

        private static double[] Widths(int count, double width)
        {
            double[] widths = new double[count];
            for (int i = 0; i < count; ++i)
            {
                widths[i] = width * (1 + 0.5 * Math.Sin(i));
            }
            return widths;
        }

        private static List<RibbonSegment> Segments(Ribbons2.Run run)
        {
            List<Ribbons2.Run> runs = new List<Ribbons2.Run>();
            runs.Add(run);
            return Ribbons2.Build(runs);
        }

        // The largest distance between the surfaces of two lists of segments at the same parameters
        private static double Difference(List<RibbonSegment> a, List<RibbonSegment> b)
        {
            double worst = 0;
            for (int i = 0; i < a.Count; ++i)
            {
                for (int j = 0; j <= 8; ++j)
                {
                    for (int k = 0; k <= 4; ++k)
                    {
                        worst = Math.Max(worst, (a[i].Position(j / 8.0, k / 2.0 - 1) - b[i].Position(j / 8.0, k / 2.0 - 1)).Length());
                    }
                }
            }
            return worst;
        }

        // The quadratic B-spline (the default) gives the segments of "ribbon", without and with normals
        [TEST("Ribbon2", "SplineIsTheRibbon")]
        public static void SplineIsTheRibbon()
        {
            SdfVector[] points = Spiral(9, 1.0, 0.35);
            double[] widths = Widths(points.Length, 0.18);
            SdfVector[] normals = new SdfVector[points.Length];
            for (int i = 0; i < normals.Length; ++i)
            {
                normals[i] = new SdfVector(points[i].x, points[i].y, 0.6).Normalized();
            }
            for (int which = 0; which < 2; ++which)
            {
                SdfVector[] given = which == 0 ? null : normals;
                List<RibbonSegment> old = RibbonBuilder.Build(points, given, widths);
                List<RibbonSegment> now = Segments(Ribbons2.FromSpline(points, widths, given));
                CHECK_EQ(now.Count, old.Count);
                double worst = Difference(old, now);
                Console.WriteLine("ribbon2 b-spline " + (which == 0 ? "without" : "with") + " normals: " + now.Count + " segments, differs from ribbon by " + worst);
                CHECK_LT(worst, 1e-12);
            }
        }

        // The quadratic Bezier chain: the centerline is the Bezier curve of each three points, the half width that of the widths
        [TEST("Ribbon2", "QuadraticBezierIsExact")]
        public static void QuadraticBezierIsExact()
        {
            SdfVector[] points = Spiral(9, 1.0, 0.35);
            double[] widths = Widths(points.Length, 0.18);
            Ribbons2.Run run = new Ribbons2.Run();
            run.points = points;
            run.widths = widths;
            List<RibbonSegment> segments = Segments(run);
            CHECK_EQ(segments.Count, 4);
            double worstCenter = 0, worstWidth = 0;
            for (int i = 0; i < segments.Count; ++i)
            {
                for (int j = 0; j <= 16; ++j)
                {
                    double u = j / 16.0, s = 1 - u;
                    SdfVector center = points[2 * i] * (s * s) + points[2 * i + 1] * (2 * u * s) + points[2 * i + 2] * (u * u);
                    double width = widths[2 * i] * (s * s) + widths[2 * i + 1] * (2 * u * s) + widths[2 * i + 2] * (u * u);
                    worstCenter = Math.Max(worstCenter, (segments[i].Position(u, 0) - center).Length());
                    worstWidth = Math.Max(worstWidth, Math.Abs(0.5 * (segments[i].Position(u, 1) - segments[i].Position(u, -1)).Length() - width));
                }
            }
            Console.WriteLine("ribbon2 quadratic bezier: centerline differs by " + worstCenter + ", half width by " + worstWidth);
            CHECK_LT(worstCenter, 1e-14);
            CHECK_LT(worstWidth, 1e-12);
        }

        // The cubic bases: the distance of the centerline of the ribbon from the cubic curve, for the number of quadratic
        // pieces to a cubic curve. It falls by about 8 for twice the pieces, and the pieces meet with one tangent.
        [TEST("Ribbon2", "CubicBasesAreApproximated")]
        public static void CubicBasesAreApproximated()
        {
            string[] names = { "bezier", "b-spline", "catmull-rom" };
            foreach (string name in names)
            {
                PatchBases.TryGet(name, out double[,] basis);
                int step = PatchBases.DefaultStep(name);
                int count = 4 + 3 * step;
                SdfVector[] points = new SdfVector[count];
                double[] widths = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    double angle = i * 0.8;
                    points[i] = new SdfVector(Math.Cos(angle) * (1 + 0.1 * i), Math.Sin(angle) * (1 + 0.1 * i), 0.3 * i);
                    widths[i] = 0.1;
                }
                if (name == "bezier")
                {
                    // One tangent where the curves meet
                    for (int i = 3; i + 1 < count; i += 3)
                    {
                        points[i] = (points[i - 1] + points[i + 1]) * 0.5;
                    }
                }
                double[] x = new double[count], y = new double[count], z = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    x[i] = points[i].x;
                    y[i] = points[i].y;
                    z[i] = points[i].z;
                }
                x = Rocaps.ToBezierCurves(x, basis, step);
                y = Rocaps.ToBezierCurves(y, basis, step);
                z = Rocaps.ToBezierCurves(z, basis, step);
                int curves = x.Length / 4;
                double size = 0;
                for (int i = 0; i < count; ++i)
                {
                    size = Math.Max(size, (points[i] - points[0]).Length());
                }
                double before = 0;
                for (int pieces = 1; pieces <= 8; pieces *= 2)
                {
                    RGB[] colors = null;
                    List<Ribbons2.Run> runs = Ribbons2.FromCubics(points, widths, null, basis, step, pieces, ref colors);
                    CHECK_EQ(runs.Count, 1);
                    List<RibbonSegment> segments = Ribbons2.Build(runs);
                    CHECK_EQ(segments.Count, curves * pieces);
                    double worst = 0, worstJoint = 0;
                    for (int i = 0; i < segments.Count; ++i)
                    {
                        int curve = i / pieces;
                        for (int j = 0; j <= 16; ++j)
                        {
                            double t = (i % pieces + j / 16.0) / pieces, s = 1 - t;
                            double b0 = s * s * s, b1 = 3 * t * s * s, b2 = 3 * t * t * s, b3 = t * t * t;
                            SdfVector cubic = new SdfVector(x[4 * curve] * b0 + x[4 * curve + 1] * b1 + x[4 * curve + 2] * b2 + x[4 * curve + 3] * b3,
                                                            y[4 * curve] * b0 + y[4 * curve + 1] * b1 + y[4 * curve + 2] * b2 + y[4 * curve + 3] * b3,
                                                            z[4 * curve] * b0 + z[4 * curve + 1] * b1 + z[4 * curve + 2] * b2 + z[4 * curve + 3] * b3);
                            worst = Math.Max(worst, (segments[i].Position(j / 16.0, 0) - cubic).Length());
                        }
                        if (i > 0)
                        {
                            worstJoint = Math.Max(worstJoint, (segments[i].Velocity(0) - segments[i - 1].Velocity(1)).Length());
                        }
                    }
                    Console.WriteLine("ribbon2 cubic " + name + ", " + pieces + " pieces to a curve: centerline off by " + worst + " (" + (100 * worst / size).ToString("0.0000") +
                                      "% of the size)" + (before > 0 ? ", " + (before / worst).ToString("0.0") + " times less" : "") + "; tangents at the joints differ by " + worstJoint);
                    CHECK_LT(worstJoint, 1e-12);
                    if (before > 0)
                    {
                        CHECK_GT(before / worst, 5);
                    }
                    before = worst;
                }
                CHECK_LT(before / size, 1e-3);
            }
        }

        // Cubic curves that do not meet, or meet at an angle (hermite, power; a bezier with a corner), give separate runs
        [TEST("Ribbon2", "CubicCurvesThatDoNotMeetAreCut")]
        public static void CubicCurvesThatDoNotMeetAreCut()
        {
            PatchBases.TryGet("bezier", out double[,] basis);
            SdfVector[] points = { new SdfVector(0, 0, 0), new SdfVector(1, 0, 0), new SdfVector(2, 1, 0), new SdfVector(3, 1, 0), new SdfVector(3, 2, 0),
                                   new SdfVector(3, 3, 1), new SdfVector(4, 3, 1) };
            double[] widths = { 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1 };
            RGB[] colors = null;
            List<Ribbons2.Run> runs = Ribbons2.FromCubics(points, widths, null, basis, 3, 4, ref colors);
            CHECK_EQ(runs.Count, 2);
            List<RibbonSegment> segments = Ribbons2.Build(runs);
            CHECK_EQ(segments.Count, 8);
            CHECK_EQ(segments[7].Index, 7);
            CHECK_EQ(segments[7].Count, 8);
            // Each run starts and ends at the ends of its cubic curve
            CHECK_LT((segments[3].Position(1, 0) - points[3]).Length(), 1e-14);
            CHECK_LT((segments[4].Position(0, 0) - points[3]).Length(), 1e-14);
            CHECK_LT((segments[7].Position(1, 0) - points[6]).Length(), 1e-14);
        }

        private static Ribbon2Shape LightShape(int which, bool solidAngle)
        {
            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = new Transform();
            settings.objectFromRender = new Transform();
            settings.epsilon = 1e-7;
            Ribbons2.Run run = new Ribbons2.Run();
            if (which == 0)
            {
                // A long flat strip along x, in the plane z = 0
                run.points = new SdfVector[] { new SdfVector(0, 0, 0), new SdfVector(2, 0, 0), new SdfVector(4, 0, 0) };
                run.widths = new double[] { 0.2, 0.2, 0.2 };
                run.normals = new SdfVector[] { new SdfVector(0, 0, 1), new SdfVector(0, 0, 1) };
            }
            else
            {
                // A bent and twisted strip of changing width
                run.points = new SdfVector[] { new SdfVector(0, 0, 0), new SdfVector(1.5, 1.5, 0.3), new SdfVector(3, 0, 0.5) };
                run.widths = new double[] { 0.15, 0.3, 0.2 };
                run.normals = new SdfVector[] { new SdfVector(0, 0, 1), new SdfVector(0, 0.6, 0.8) };
            }
            return new Ribbon2Shape(settings, Segments(run)[0], 0, solidAngle);
        }

        // The samples by area: the density of a sample is the density that the shape gives for its point, and the mean of
        // 1 / pdf is the area (known for the flat strip)
        [TEST("Ribbon2", "AreaDensityMatchesTheSamples")]
        public static void AreaDensityMatchesTheSamples()
        {
            for (int which = 0; which < 2; ++which)
            {
                Ribbon2Shape shape = LightShape(which, true);
                Random random = new Random(21);
                const int count = 200000;
                int mismatched = 0;
                double sum = 0, least = double.MaxValue, most = 0;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    if (!ss)
                    {
                        continue;
                    }
                    sum += 1.0 / ss.value().pdf;
                    Float pdf = shape.PDF(ss.value().intr);
                    least = Math.Min(least, pdf);
                    most = Math.Max(most, pdf);
                    mismatched += Math.Abs(pdf - ss.value().pdf) > 1e-3 * ss.value().pdf ? 1 : 0;
                }
                double area = sum / count;
                Console.WriteLine("ribbon2 area (" + (which == 0 ? "flat strip" : "bent strip") + "): by samples " + area + ", Area() " + shape.Area() + "; density from " + least +
                                  " to " + most + " (1 / area " + 1 / area + "), " + mismatched + " densities differ");
                CHECK_LE(mismatched, count / 20000);
                CHECK_LT(Math.Abs(area - shape.Area()) / area, 0.01);
                if (which == 0)
                {
                    CHECK_LT(Math.Abs(area - 1.6) / 1.6, 0.005);
                }
            }
        }

        // From a point: the solid angle of the ribbon by the directions that hit it and by the samples (the mean of 1 / pdf
        // of the ones that are seen) are the same, the density of a direction is that of its sample, and the samples for the
        // point vary less than those by area
        [TEST("Ribbon2", "SamplesGiveTheSolidAngle")]
        public static void SamplesGiveTheSolidAngle()
        {
            Point3f[] references = { new Point3f(0.3f, 0.1f, 0.4f), new Point3f(-1.5f, 0.05f, 0.15f), new Point3f(2.0f, 3.0f, 2.5f), new Point3f(1.0f, 0.2f, 0.25f) };
            for (int test = 0; test < 6; ++test)
            {
                int which = test < 4 ? 0 : 1;
                Point3f reference = references[test < 4 ? test : test - 2];
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(reference, new Vector3f(0, 0, 0)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
                Ribbon2Shape shape = LightShape(which, true), plain = LightShape(which, false);
                Random random = new Random(31);
                const int count = 400000;
                int hits = 0;
                for (int i = 0; i < count; ++i)
                {
                    double z = 1 - 2 * random.NextDouble(), radius = Math.Sqrt(Math.Max(0, 1 - z * z)), phi = 2 * Math.PI * random.NextDouble();
                    Vector3f wi = new Vector3f((Float)(radius * Math.Cos(phi)), (Float)(radius * Math.Sin(phi)), (Float)z);
                    hits += shape.IntersectP(ctx.SpawnRay(wi)) ? 1 : 0;
                }
                double byDirections = 4 * Math.PI * hits / count;
                double[] mean = new double[2], square = new double[2];
                int compared = 0, mismatched = 0;
                for (int kind = 0; kind < 2; ++kind)
                {
                    Ribbon2Shape sampled = kind == 0 ? shape : plain;
                    for (int i = 0; i < count; ++i)
                    {
                        pstd.optional<ShapeSample> ss = sampled.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                        if (!ss || !(ss.value().pdf > 0))
                        {
                            continue;
                        }
                        Vector3f wi = pbrt.Normalize(ss.value().intr.p() - reference);
                        pstd.optional<ShapeIntersection> hit = sampled.Intersect(ctx.SpawnRay(wi));
                        if (!hit || pbrt.Distance(hit.value().intr.p(), ss.value().intr.p()) > 1e-3)
                        {
                            continue;
                        }
                        double value = 1.0 / ss.value().pdf;
                        mean[kind] += value / count;
                        square[kind] += value * value / count;
                        if (kind == 0 && i % 50 == 0 && pbrt.AbsDot(ss.value().intr.n, wi) > 0.05)
                        {
                            ++compared;
                            Float pdf = sampled.PDF(ctx, wi);
                            mismatched += Math.Abs(pdf - ss.value().pdf) > 0.02 * ss.value().pdf ? 1 : 0;
                        }
                    }
                }
                double deviation = Math.Sqrt(Math.Max(0, square[0] - mean[0] * mean[0])), deviationPlain = Math.Sqrt(Math.Max(0, square[1] - mean[1] * mean[1]));
                Console.WriteLine("ribbon2 " + (which == 0 ? "flat" : "bent") + " strip from " + reference + ": solid angle by directions " + byDirections.ToString("0.00000") +
                                  ", by samples " + mean[0].ToString("0.00000") + " (deviation " + deviation.ToString("0.0000") + "), by area samples " + mean[1].ToString("0.00000") +
                                  " (deviation " + deviationPlain.ToString("0.0000") + "); " + mismatched + " of " + compared + " densities differ");
                double tolerance = 3 * Math.Sqrt(byDirections * (4 * Math.PI - byDirections) / count) + 4 * deviation / Math.Sqrt(count) + 0.005 * byDirections;
                CHECK_LT(Math.Abs(mean[0] - byDirections), tolerance);
                CHECK_GT(compared, 500);
                CHECK_LE(mismatched, compared / 100);
                CHECK_LT(deviation, deviationPlain);
            }
        }
    }
}
