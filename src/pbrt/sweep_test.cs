// (not in pbrt-v4) sweep_test.cs
//
// Tests of the "sweep" shape (sweep.cs): the rays against the triangles of the surface and against a cylinder, the least
// degree of a cross section, and the sampling.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using testing;

namespace pbrt
{
    public static unsafe class SweepTests
    {
        private static BezierSurfaceSettings Settings()
        {
            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = new Transform();
            settings.objectFromRender = new Transform();
            return settings;
        }

        // A blade that bends out of a plane and twists, with a cross section of the degree that narrows to the end
        // kind 0: degree 1; 1: degree 2; 2: degree 2 with weights; 3: degree 3
        private static SweepSurface Blade(int kind)
        {
            SdfVector[] points = { new SdfVector(0, 0, 0), new SdfVector(1, 1.2, 0.3), new SdfVector(2, -0.8, 0.6), new SdfVector(3, 0.4, -0.2) };
            SdfVector[] normals = { new SdfVector(0, 0, 1), new SdfVector(0, 0.3, 1), new SdfVector(0, 0.7, 0.8), new SdfVector(0, 1, 0.5) };
            double[] scales = { 1, 1, 1, 1 };
            double[] x, y, weights;
            int degree;
            if (kind == 0)
            {
                degree = 1;
                x = new double[] { -0.3, 0.3, -0.1, 0.12 };
                y = new double[] { 0, 0.05, 0, 0 };
                weights = new double[] { 1, 1 };
            }
            else if (kind == 1 || kind == 2)
            {
                degree = 2;
                x = new double[] { -0.3, 0.02, 0.3, -0.1, 0, 0.12 };
                y = new double[] { 0.1, -0.25, 0.12, 0.03, -0.08, 0.02 };
                weights = kind == 1 ? new double[] { 1, 1, 1 } : new double[] { 1, 0.6, 1.3 };
            }
            else
            {
                degree = 3;
                x = new double[] { -0.3, -0.1, 0.12, 0.3, -0.1, -0.03, 0.05, 0.12 };
                y = new double[] { 0.1, -0.3, 0.25, -0.05, 0.03, -0.1, 0.08, 0 };
                weights = new double[] { 1, 1, 1, 1 };
            }
            return Sweeps.Surfaces(points, normals, x, y, weights, scales, degree, 1, false)[0];
        }

        // The distance to the triangle along the ray (Moller and Trumbore), or -1
        private static double Triangle(SdfVector o, SdfVector d, SdfVector a, SdfVector b, SdfVector c)
        {
            SdfVector e1 = b - a, e2 = c - a, p = SdfVector.Cross(d, e2);
            double determinant = SdfVector.Dot(e1, p);
            if (Math.Abs(determinant) < 1e-300)
            {
                return -1;
            }
            SdfVector s = o - a;
            double u = SdfVector.Dot(s, p) / determinant;
            SdfVector q = SdfVector.Cross(s, e1);
            double v = SdfVector.Dot(d, q) / determinant;
            if (u < 0 || v < 0 || u + v > 1)
            {
                return -1;
            }
            double t = SdfVector.Dot(e2, q) / determinant;
            return t > 0 ? t : -1;
        }

        // Whether the ray meets the surface itself near its point at the distance t (found on the triangles of the grid): from
        // the nearest point of the grid, Newton's steps in (u, v) onto the ray
        private static bool OnSurface(SweepSurface surface, SdfVector o, SdfVector d, double t, int alongCount, int acrossCount, SdfVector[] grid)
        {
            SdfVector at = o + d * t;
            int bestI = 0, bestJ = 0;
            double least = double.MaxValue;
            for (int i = 0; i <= alongCount; ++i)
            {
                for (int j = 0; j <= acrossCount; ++j)
                {
                    double distance = (grid[i * (acrossCount + 1) + j] - at).LengthSquared();
                    if (distance < least)
                    {
                        least = distance;
                        bestI = i;
                        bestJ = j;
                    }
                }
            }
            SdfVector e1 = RibbonBuilder.OrthogonalTo(d), e2 = SdfVector.Cross(d, e1);
            double u = (double)bestI / alongCount, v = (double)bestJ / acrossCount;
            for (int step = 0; step < 20; ++step)
            {
                surface.Evaluate(u, v, out SdfVector p, out SdfVector du, out SdfVector dv);
                double fx = SdfVector.Dot(e1, p - o), fy = SdfVector.Dot(e2, p - o);
                if (fx * fx + fy * fy < 1e-24)
                {
                    // (well inside the rim, and not where the ray only touches)
                    SdfVector normal = SdfVector.Cross(du, dv).Normalized();
                    return u > 1e-3 && u < 1 - 1e-3 && v > 1e-3 && v < 1 - 1e-3 && Math.Abs(SdfVector.Dot(normal, d)) > 1e-2 && Math.Abs(SdfVector.Dot(p - o, d) - t) < 5e-3;
                }
                double xu = SdfVector.Dot(e1, du), xv = SdfVector.Dot(e1, dv), yu = SdfVector.Dot(e2, du), yv = SdfVector.Dot(e2, dv);
                double determinant = xu * yv - xv * yu;
                if (determinant == 0)
                {
                    return false;
                }
                u -= (fx * yv - fy * xv) / determinant;
                v -= (fy * xu - fx * yu) / determinant;
            }
            return false;
        }

        private static SdfVector Direction(Random random)
        {
            double z = 1 - 2 * random.NextDouble(), radius = Math.Sqrt(Math.Max(0, 1 - z * z)), phi = 2 * Math.PI * random.NextDouble();
            return new SdfVector(radius * Math.Cos(phi), radius * Math.Sin(phi), z);
        }

        // The rays hit the shape where they hit the surface in fine triangles: rays from all sides at points of the surface and
        // beside it, for each degree of the cross section
        [TEST("Sweep", "RaysHitTheSurface")]
        public static void RaysHitTheSurface()
        {
            const int alongCount = 256, acrossCount = 96, rayCount = 4000;
            for (int kind = 0; kind < 4; ++kind)
            {
                SweepSurface surface = Blade(kind);
                SweepShape shape = new SweepShape(Settings(), surface, 0, 1);
                SdfVector[] grid = new SdfVector[(alongCount + 1) * (acrossCount + 1)];
                for (int i = 0; i <= alongCount; ++i)
                {
                    for (int j = 0; j <= acrossCount; ++j)
                    {
                        surface.Evaluate((double)i / alongCount, (double)j / acrossCount, out grid[i * (acrossCount + 1) + j], out SdfVector du, out SdfVector dv);
                    }
                }
                Random random = new Random(7 + kind);
                SdfVector[] origins = new SdfVector[rayCount], directions = new SdfVector[rayCount];
                for (int r = 0; r < rayCount; ++r)
                {
                    surface.Evaluate(random.NextDouble(), random.NextDouble(), out SdfVector target, out SdfVector du, out SdfVector dv);
                    if (r % 4 == 3)
                    {
                        target = target + Direction(random) * 0.3;
                    }
                    directions[r] = Direction(random);
                    origins[r] = target - directions[r] * (2 + 2 * random.NextDouble());
                }
                int agreed = 0, bothMiss = 0, onlyShape = 0, onlyTriangles = 0, far = 0, offRay = 0, missed = 0;
                double worst = 0;
                double[] found = new double[rayCount];
                // The time of a ray, once the code is compiled in full
                {
                    double best = double.MaxValue;
                    int hitCount = 0;
                    Stopwatch warm = Stopwatch.StartNew();
                    for (int pass = 0; pass < 4 || warm.ElapsedMilliseconds < 600; ++pass)
                    {
                        Stopwatch timer = Stopwatch.StartNew();
                        hitCount = 0;
                        for (int r = 0; r < rayCount; ++r)
                        {
                            Ray timed = new Ray(new Point3f((Float)origins[r].x, (Float)origins[r].y, (Float)origins[r].z),
                                                new Vector3f((Float)directions[r].x, (Float)directions[r].y, (Float)directions[r].z));
                            hitCount += shape.IntersectP(timed) ? 1 : 0;
                        }
                        timer.Stop();
                        best = Math.Min(best, timer.Elapsed.TotalMilliseconds * 1e6 / rayCount);
                    }
                    Console.WriteLine("  sweep of degree " + surface.Degree + ": " + hitCount + " hits, " + best.ToString("f0") + " ns a ray");
                }
                if (kind == 0)
                {
                    // (the flat blade as "ribbon3" with the polynomial of its crossing function, for the time: the same
                    // centerline, of about the same width)
                    SdfVector[] center = { new SdfVector(0, 0, 0), new SdfVector(1, 1.2, 0.3), new SdfVector(2, -0.8, 0.6), new SdfVector(3, 0.4, -0.2) };
                    double[] widths = { 0.3, 0.25, 0.18, 0.11 };
                    Ribbon3Curve curve = new Ribbon3Curve(center, widths, new SdfVector(0, 0, 1), new SdfVector(0, 1, 0.5), Ribbon3Normals.Linear, null, null, 0, 1);
                    Ribbon3Shape ribbon = new Ribbon3Shape(Settings(), curve, 0, 1, Ribbons3.Leaves(curve, true), 0, true, true, false, false, false, null);
                    ribbon.Polynomial = true;
                    double best = double.MaxValue;
                    int hitCount = 0;
                    Stopwatch warm = Stopwatch.StartNew();
                    for (int pass = 0; pass < 4 || warm.ElapsedMilliseconds < 600; ++pass)
                    {
                        Stopwatch timer = Stopwatch.StartNew();
                        hitCount = 0;
                        for (int r = 0; r < rayCount; ++r)
                        {
                            Ray timed = new Ray(new Point3f((Float)origins[r].x, (Float)origins[r].y, (Float)origins[r].z),
                                                new Vector3f((Float)directions[r].x, (Float)directions[r].y, (Float)directions[r].z));
                            hitCount += ribbon.IntersectP(timed) ? 1 : 0;
                        }
                        timer.Stop();
                        best = Math.Min(best, timer.Elapsed.TotalMilliseconds * 1e6 / rayCount);
                    }
                    Console.WriteLine("  \"ribbon3\" of the same centerline: " + hitCount + " hits, " + best.ToString("f0") + " ns a ray");
                }
                for (int r = 0; r < rayCount; ++r)
                {
                    Ray ray = new Ray(new Point3f((Float)origins[r].x, (Float)origins[r].y, (Float)origins[r].z),
                                      new Vector3f((Float)directions[r].x, (Float)directions[r].y, (Float)directions[r].z));
                    pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                    found[r] = -1;
                    if (si)
                    {
                        found[r] = si.value().tHit;
                        Point3f p = si.value().intr.p(), along = ray.o + ray.d * si.value().tHit;
                        offRay += pbrt.Distance(p, along) > 2e-5f ? 1 : 0;
                    }
                }
                for (int r = 0; r < rayCount; ++r)
                {
                    // (the direction as the shape had it, in single precision)
                    SdfVector o = new SdfVector((Float)origins[r].x, (Float)origins[r].y, (Float)origins[r].z);
                    SdfVector d = new SdfVector((Float)directions[r].x, (Float)directions[r].y, (Float)directions[r].z);
                    double nearest = -1;
                    for (int i = 0; i < alongCount; ++i)
                    {
                        for (int j = 0; j < acrossCount; ++j)
                        {
                            SdfVector a = grid[i * (acrossCount + 1) + j], b = grid[(i + 1) * (acrossCount + 1) + j];
                            SdfVector c = grid[(i + 1) * (acrossCount + 1) + j + 1], e = grid[i * (acrossCount + 1) + j + 1];
                            double t = Triangle(o, d, a, b, c);
                            nearest = t > 0 && (nearest < 0 || t < nearest) ? t : nearest;
                            t = Triangle(o, d, a, c, e);
                            nearest = t > 0 && (nearest < 0 || t < nearest) ? t : nearest;
                        }
                    }
                    if (nearest > 0 && (found[r] < 0 || found[r] > nearest + 5e-3))
                    {
                        // (is the hit of the triangles one of the surface? Newton's steps from the triangle onto the ray)
                        missed += OnSurface(surface, o, d, nearest, alongCount, acrossCount, grid) ? 1 : 0;
                    }
                    if (nearest < 0 && found[r] < 0)
                    {
                        ++bothMiss;
                    }
                    else if (nearest < 0)
                    {
                        ++onlyShape;
                    }
                    else if (found[r] < 0)
                    {
                        ++onlyTriangles;
                    }
                    else if (Math.Abs(nearest - found[r]) > 5e-3)
                    {
                        ++far;
                    }
                    else
                    {
                        ++agreed;
                        worst = Math.Max(worst, Math.Abs(nearest - found[r]));
                    }
                }
                Console.WriteLine("sweep kind " + kind + " (degree " + surface.Degree + ", polynomial of degree " + 6 * surface.Degree + "): " + agreed +
                                  " hits agree (worst " + worst.ToString("e2") + "), " + bothMiss + " miss both, " + onlyShape + " only the shape, " + onlyTriangles +
                                  " only the triangles, " + far + " at another distance, " + missed + " hits of the surface missed, " + offRay + " off the ray");
                CHECK_EQ(offRay, 0);
                CHECK_EQ(missed, 0);
                // (the triangles are not the surface at its rim and where a ray touches it)
                CHECK_LE(onlyShape + onlyTriangles + far, rayCount / 200);
            }
        }

        // A straight curve and a quadratic with the weight of an arc is a part of a cylinder exactly: the hits are those of
        // the cylinder
        [TEST("Sweep", "ArcIsTheCylinder")]
        public static void ArcIsTheCylinder()
        {
            const double radius = 0.4, half = 1.1;
            SdfVector[] points = { new SdfVector(0, 0, 0), new SdfVector(0, 0, 1), new SdfVector(0, 0, 2), new SdfVector(0, 0, 3) };
            SdfVector[] normals = { new SdfVector(0, 1, 0), new SdfVector(0, 1, 0), new SdfVector(0, 1, 0), new SdfVector(0, 1, 0) };
            double[] x = { -radius * Math.Sin(half), 0, radius * Math.Sin(half) };
            double[] y = { radius * Math.Cos(half), radius / Math.Cos(half), radius * Math.Cos(half) };
            double[] weights = { 1, Math.Cos(half), 1 };
            double[] scales = { 1, 1, 1, 1 };
            SweepSurface surface = Sweeps.Surfaces(points, normals, x, y, weights, scales, 2, 1, true)[0];
            CHECK_EQ(surface.Degree, 2);
            SweepShape shape = new SweepShape(Settings(), surface, 0, 1);
            Random random = new Random(3);
            int hits = 0, misses = 0, wrong = 0;
            double worst = 0;
            for (int r = 0; r < 20000; ++r)
            {
                surface.Evaluate(random.NextDouble(), random.NextDouble(), out SdfVector target, out SdfVector du, out SdfVector dv);
                // (on the cylinder)
                worst = Math.Max(worst, Math.Abs(Math.Sqrt(target.x * target.x + target.y * target.y) - radius));
                if (r % 3 == 2)
                {
                    target = target + Direction(random) * 0.2;
                }
                SdfVector direction = Direction(random);
                SdfVector start = target - direction * 2;
                Ray ray = new Ray(new Point3f((Float)start.x, (Float)start.y, (Float)start.z), new Vector3f((Float)direction.x, (Float)direction.y, (Float)direction.z));
                // The cylinder x^2 + y^2 = radius^2, within the arc and the length
                double ox = ray.o.x, oy = ray.o.y, oz = ray.o.z, dx = ray.d.x, dy = ray.d.y, dz = ray.d.z;
                double a = dx * dx + dy * dy, b = 2 * (ox * dx + oy * dy), c = ox * ox + oy * oy - radius * radius;
                double discriminant = b * b - 4 * a * c;
                double expected = -1;
                bool rim = false;
                if (discriminant > 0 && a > 0)
                {
                    for (int side = 0; side < 2 && expected < 0; ++side)
                    {
                        double t = (-b + (side == 0 ? -1 : 1) * Math.Sqrt(discriminant)) / (2 * a);
                        double px = ox + t * dx, py = oy + t * dy, pz = oz + t * dz;
                        double angle = Math.Atan2(px, py);
                        rim |= Math.Abs(Math.Abs(angle) - half) < 1e-5 || Math.Abs(pz) < 1e-5 || Math.Abs(pz - 3) < 1e-5 || discriminant < 1e-8;
                        if (t > 0 && Math.Abs(angle) <= half && pz >= 0 && pz <= 3)
                        {
                            expected = t;
                        }
                    }
                }
                pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                if (rim)
                {
                    continue;
                }
                if (expected < 0)
                {
                    ++misses;
                    wrong += si ? 1 : 0;
                }
                else
                {
                    ++hits;
                    wrong += !si || Math.Abs(si.value().tHit - expected) > 2e-5 ? 1 : 0;
                }
            }
            Console.WriteLine("sweep arc: " + hits + " hits and " + misses + " misses of the cylinder, " + wrong + " wrong; the surface is off the cylinder by " +
                              worst.ToString("e2") + " at most");
            CHECK_LT(worst, 1e-14);
            CHECK_EQ(wrong, 0);
        }

        // A cubic cross section that is a quadratic with its degree raised is brought down to the quadratic, and a quadratic
        // that is a line to the line, with the same surface
        [TEST("Sweep", "LeastDegree")]
        public static void LeastDegree()
        {
            SdfVector[] points = { new SdfVector(0, 0, 0), new SdfVector(1, 1.2, 0.3), new SdfVector(2, -0.8, 0.6), new SdfVector(3, 0.4, -0.2) };
            double[] scales = { 1, 0.8, 0.5, 0.2 };
            double[] ones = { 1, 1, 1, 1 };
            // (the quadratic (-0.3, 0.1), (0, -0.2), (0.3, 0.1) as a cubic)
            double[] x = { -0.3, -0.1, 0.1, 0.3 };
            double[] y = { 0.1, -0.1, -0.1, 0.1 };
            SweepSurface raised = Sweeps.Surfaces(points, null, x, y, ones, scales, 3, 1, false)[0];
            SweepSurface lowered = Sweeps.Surfaces(points, null, x, y, ones, scales, 3, 1, true)[0];
            CHECK_EQ(raised.Degree, 3);
            CHECK_EQ(lowered.Degree, 2);
            double[] lineX = { -0.3, 0, 0.3 };
            double[] lineY = { 0.1, 0.1, 0.1 };
            SweepSurface line = Sweeps.Surfaces(points, null, lineX, lineY, ones, scales, 2, 1, true)[0];
            CHECK_EQ(line.Degree, 1);
            double worst = 0;
            for (int i = 0; i <= 20; ++i)
            {
                for (int j = 0; j <= 20; ++j)
                {
                    raised.Evaluate(i / 20.0, j / 20.0, out SdfVector p, out SdfVector du, out SdfVector dv);
                    lowered.Evaluate(i / 20.0, j / 20.0, out SdfVector q, out SdfVector qu, out SdfVector qv);
                    worst = Math.Max(worst, Math.Max((p - q).Length(), Math.Max((du - qu).Length(), (dv - qv).Length())));
                }
            }
            CHECK_LT(worst, 1e-12);
        }

        // The samples: the mean of 1 / pdf is the area, which is that of the surface in fine triangles, and the density of a
        // point is that of its sample
        [TEST("Sweep", "AreaDensityMatchesTheSamples")]
        public static void AreaDensityMatchesTheSamples()
        {
            for (int kind = 0; kind < 4; ++kind)
            {
                SweepSurface surface = Blade(kind);
                SweepShape shape = new SweepShape(Settings(), surface, 0, 1);
                const int fine = 1500;
                double triangles = 0;
                SdfVector[] previous = new SdfVector[fine + 1], current = new SdfVector[fine + 1];
                for (int i = 0; i <= fine; ++i)
                {
                    for (int j = 0; j <= fine; ++j)
                    {
                        surface.Evaluate((double)i / fine, (double)j / fine, out current[j], out SdfVector du, out SdfVector dv);
                    }
                    for (int j = 0; i > 0 && j < fine; ++j)
                    {
                        triangles += 0.5 * SdfVector.Cross(current[j] - previous[j], current[j + 1] - previous[j]).Length();
                        triangles += 0.5 * SdfVector.Cross(current[j + 1] - previous[j], previous[j + 1] - previous[j]).Length();
                    }
                    SdfVector[] swap = previous;
                    previous = current;
                    current = swap;
                }
                Random random = new Random(21);
                const int count = 200000;
                int mismatched = 0;
                double sum = 0;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    if (!ss)
                    {
                        continue;
                    }
                    sum += 1.0 / ss.value().pdf;
                    Float pdf = shape.PDF(ss.value().intr);
                    mismatched += Math.Abs(pdf - ss.value().pdf) > 1e-3 * ss.value().pdf ? 1 : 0;
                }
                double area = sum / count;
                Console.WriteLine("sweep kind " + kind + " area: triangles " + triangles + ", Area() " + shape.Area() + ", samples " + area + "; " + mismatched +
                                  " densities differ");
                CHECK_LE(mismatched, count / 20000);
                CHECK_LT(Math.Abs(area - triangles) / triangles, 0.01);
                CHECK_LT(Math.Abs(shape.Area() - triangles) / triangles, 1e-3);
            }
        }

        // From a point: the solid angle of the surface by the directions that hit it and by the samples (the mean of 1 / pdf of
        // the ones that are seen) are the same, and the density of a direction is that of its sample
        [TEST("Sweep", "SamplesGiveTheSolidAngle")]
        public static void SamplesGiveTheSolidAngle()
        {
            Point3f[] references = { new Point3f(1.5f, 0.3f, 1.5f), new Point3f(-1.5f, 0.05f, 0.15f), new Point3f(2.0f, 3.0f, 2.5f) };
            for (int kind = 1; kind < 4; ++kind)
            {
                SweepShape shape = new SweepShape(Settings(), Blade(kind), 0, 1);
                foreach (Point3f reference in references)
                {
                    ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(reference, new Vector3f(0, 0, 0)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
                    Random random = new Random(31);
                    const int count = 300000;
                    int hits = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        SdfVector d = Direction(random);
                        hits += shape.IntersectP(ctx.SpawnRay(new Vector3f((Float)d.x, (Float)d.y, (Float)d.z))) ? 1 : 0;
                    }
                    double byDirections = 4 * Math.PI * hits / count;
                    double mean = 0;
                    int compared = 0, mismatched = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                        if (!ss || !(ss.value().pdf > 0))
                        {
                            continue;
                        }
                        Vector3f wi = pbrt.Normalize(ss.value().intr.p() - reference);
                        Float distance = pbrt.Distance(ss.value().intr.p(), reference);
                        // (seen: nothing of the surface in front of the sample)
                        pstd.optional<ShapeIntersection> si = shape.Intersect(ctx.SpawnRay(wi));
                        if (!si || si.value().tHit < distance * (1 - 1e-3f))
                        {
                            continue;
                        }
                        mean += 1.0 / ss.value().pdf;
                        if (i % 16 == 0)
                        {
                            Float pdf = shape.PDF(ctx, wi);
                            ++compared;
                            mismatched += Math.Abs(pdf - ss.value().pdf) > 2e-2 * ss.value().pdf ? 1 : 0;
                        }
                    }
                    mean /= count;
                    Console.WriteLine("sweep kind " + kind + " from " + reference + ": solid angle by directions " + byDirections + ", by samples " + mean + "; " + mismatched +
                                      " of " + compared + " densities differ");
                    CHECK_LT(Math.Abs(mean - byDirections), 0.03 * byDirections + 0.002);
                    CHECK_LE(mismatched, compared / 100 + 2);
                }
            }
        }
    }
}
