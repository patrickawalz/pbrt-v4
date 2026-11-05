// (not in pbrt-v4) quadrics_test.cs
//
// Tests of the quadrics (shapes.cs: sphere, disk, cylinder; shapes_quadrics.cs: cone, paraboloid, hyperboloid, torus) against
// independent references: brute-force ray marching of the implicit surface, the RenderMan parameterization written out again here,
// Crofton's formula (the mean number of crossings of random lines) for the area and for integrals over the surface, and uniformly
// random directions for the solid angle of a shape.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class QuadricTests
    {
        private delegate Shape ShapeMaker(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation);

        // A shape together with its definition written out again: the implicit function, the clipping and the RenderMan (u, v) map
        private sealed class Reference
        {
            public string name;
            public ShapeMaker make;
            public Func<double, double, double, double> implicitFunction;
            public Func<double, double, double, double, bool> inRange;   // x, y, z, tolerance
            public Func<double, double, Point3f> parameterization;       // (u, v) in [0, 1]^2 -> object-space point
        }

        private static double PhiOf(double x, double y)
        {
            double phi = Math.Atan2(y, x);
            return phi < 0 ? phi + 2 * Math.PI : phi;
        }

        private static double Sweep(double degrees)
        {
            return degrees >= 360 ? 2 * Math.PI : degrees * Math.PI / 180;
        }

        private static Reference MakeSphere(string name, double r, double zmin, double zmax, double phimaxDegrees)
        {
            double lo = Math.Max(-r, Math.Min(zmin, zmax)), hi = Math.Min(r, Math.Max(zmin, zmax)), phiMax = Sweep(phimaxDegrees);
            return new Reference
            {
                name = name,
                make = (Transform t, Transform ti, bool rev) => new Sphere(t, ti, rev, (Float)r, (Float)zmin, (Float)zmax, (Float)phimaxDegrees),
                implicitFunction = (double x, double y, double z) => x * x + y * y + z * z - r * r,
                inRange = (double x, double y, double z, double e) => z >= lo - e && z <= hi + e && (PhiOf(x, y) <= phiMax + e),
                parameterization = (double u, double v) =>
                {
                    double thetaMin = Math.Acos(lo / r), thetaMax = Math.Acos(hi / r);
                    double phi = u * phiMax, theta = thetaMin + v * (thetaMax - thetaMin);
                    return new Point3f((Float)(r * Math.Sin(theta) * Math.Cos(phi)), (Float)(r * Math.Sin(theta) * Math.Sin(phi)),
                                       (Float)(r * Math.Cos(theta)));
                }
            };
        }

        private static Reference MakeDisk(string name, double h, double r, double inner, double phimaxDegrees)
        {
            double phiMax = Sweep(phimaxDegrees);
            return new Reference
            {
                name = name,
                make = (Transform t, Transform ti, bool rev) => new Disk(t, ti, rev, (Float)h, (Float)r, (Float)inner, (Float)phimaxDegrees),
                implicitFunction = (double x, double y, double z) => z - h,
                inRange = (double x, double y, double z, double e) =>
                {
                    double rho = Math.Sqrt(x * x + y * y);
                    return rho >= inner - e && rho <= r + e && PhiOf(x, y) <= phiMax + e;
                },
                parameterization = (double u, double v) =>
                {
                    double rho = r - v * (r - inner), phi = u * phiMax;
                    return new Point3f((Float)(rho * Math.Cos(phi)), (Float)(rho * Math.Sin(phi)), (Float)h);
                }
            };
        }

        private static Reference MakeCylinder(string name, double r, double zmin, double zmax, double phimaxDegrees)
        {
            double phiMax = Sweep(phimaxDegrees);
            return new Reference
            {
                name = name,
                make = (Transform t, Transform ti, bool rev) => new Cylinder(t, ti, rev, (Float)r, (Float)zmin, (Float)zmax, (Float)phimaxDegrees),
                implicitFunction = (double x, double y, double z) => x * x + y * y - r * r,
                inRange = (double x, double y, double z, double e) => z >= zmin - e && z <= zmax + e && PhiOf(x, y) <= phiMax + e,
                parameterization = (double u, double v) =>
                {
                    double phi = u * phiMax;
                    return new Point3f((Float)(r * Math.Cos(phi)), (Float)(r * Math.Sin(phi)), (Float)(zmin + v * (zmax - zmin)));
                }
            };
        }

        private static Reference MakeCone(string name, double height, double r, double zmin, double zmax, double phimaxDegrees)
        {
            double phiMax = Sweep(phimaxDegrees), k = r / height;
            return new Reference
            {
                name = name,
                make = (Transform t, Transform ti, bool rev) => new Cone(t, ti, rev, (Float)height, (Float)r, (Float)zmin, (Float)zmax, (Float)phimaxDegrees),
                implicitFunction = (double x, double y, double z) => x * x + y * y - k * k * (height - z) * (height - z),
                inRange = (double x, double y, double z, double e) => z >= zmin - e && z <= zmax + e && PhiOf(x, y) <= phiMax + e,
                parameterization = (double u, double v) =>
                {
                    double z = zmin + v * (zmax - zmin), rho = k * (height - z), phi = u * phiMax;
                    return new Point3f((Float)(rho * Math.Cos(phi)), (Float)(rho * Math.Sin(phi)), (Float)z);
                }
            };
        }

        private static Reference MakeParaboloid(string name, double r, double zmin, double zmax, double phimaxDegrees)
        {
            double phiMax = Sweep(phimaxDegrees), c2 = r * r / zmax;
            return new Reference
            {
                name = name,
                make = (Transform t, Transform ti, bool rev) => new Paraboloid(t, ti, rev, (Float)r, (Float)zmin, (Float)zmax, (Float)phimaxDegrees),
                implicitFunction = (double x, double y, double z) => x * x + y * y - c2 * z,
                inRange = (double x, double y, double z, double e) => z >= zmin - e && z <= zmax + e && PhiOf(x, y) <= phiMax + e,
                parameterization = (double u, double v) =>
                {
                    double z = zmin + v * (zmax - zmin), rho = Math.Sqrt(c2 * z), phi = u * phiMax;
                    return new Point3f((Float)(rho * Math.Cos(phi)), (Float)(rho * Math.Sin(phi)), (Float)z);
                }
            };
        }

        private static Reference MakeHyperboloid(string name, Point3f p1, Point3f p2, double phimaxDegrees)
        {
            double phiMax = Sweep(phimaxDegrees);
            double zLow = Math.Min(p1.z, p2.z), zHigh = Math.Max(p1.z, p2.z);
            Func<double, double[]> segment = (double z) =>
            {
                double s = (z - p1.z) / ((double)p2.z - p1.z);
                return new double[] { p1.x + s * ((double)p2.x - p1.x), p1.y + s * ((double)p2.y - p1.y) };
            };
            return new Reference
            {
                name = name,
                make = (Transform t, Transform ti, bool rev) => new Hyperboloid(t, ti, rev, p1, p2, (Float)phimaxDegrees),
                implicitFunction = (double x, double y, double z) =>
                {
                    double[] q = segment(z);
                    return x * x + y * y - (q[0] * q[0] + q[1] * q[1]);
                },
                inRange = (double x, double y, double z, double e) =>
                {
                    if (z < zLow - e || z > zHigh + e)
                    {
                        return false;
                    }
                    double[] q = segment(z);
                    double phi = Math.Atan2(q[0] * y - q[1] * x, q[0] * x + q[1] * y);
                    if (phi < 0)
                    {
                        phi += 2 * Math.PI;
                    }
                    return phi <= phiMax + e;
                },
                parameterization = (double u, double v) =>
                {
                    double x = p1.x + v * ((double)p2.x - p1.x), y = p1.y + v * ((double)p2.y - p1.y), z = p1.z + v * ((double)p2.z - p1.z);
                    double phi = u * phiMax;
                    return new Point3f((Float)(Math.Cos(phi) * x - Math.Sin(phi) * y), (Float)(Math.Sin(phi) * x + Math.Cos(phi) * y), (Float)z);
                }
            };
        }

        private static Reference MakeTorus(string name, double major, double minor, double phiMinDegrees, double phiMaxDegrees,
                                           double thetaMaxDegrees)
        {
            double phiMin = phiMinDegrees * Math.PI / 180, phiSweep = Sweep(phiMaxDegrees - phiMinDegrees), thetaMax = Sweep(thetaMaxDegrees);
            return new Reference
            {
                name = name,
                make = (Transform t, Transform ti, bool rev) => new Torus(t, ti, rev, (Float)major, (Float)minor, (Float)phiMinDegrees, (Float)phiMaxDegrees, (Float)thetaMaxDegrees),
                implicitFunction = (double x, double y, double z) =>
                {
                    double rho = Math.Sqrt(x * x + y * y) - major;
                    return rho * rho + z * z - minor * minor;
                },
                inRange = (double x, double y, double z, double e) =>
                {
                    double rho = Math.Sqrt(x * x + y * y) - major;
                    double phi = Math.Atan2(z, rho) - phiMin;
                    phi -= 2 * Math.PI * Math.Floor(phi / (2 * Math.PI));
                    return PhiOf(x, y) <= thetaMax + e && (phiSweep >= 2 * Math.PI || phi <= phiSweep + e);
                },
                parameterization = (double u, double v) =>
                {
                    double theta = u * thetaMax, phi = phiMin + v * phiSweep;
                    double rho = major + minor * Math.Cos(phi);
                    return new Point3f((Float)(rho * Math.Cos(theta)), (Float)(rho * Math.Sin(theta)), (Float)(minor * Math.Sin(phi)));
                }
            };
        }

        private static List<Reference> References()
        {
            List<Reference> list = new List<Reference>();
            list.Add(MakeSphere("sphere", 1.3, -1.3, 1.3, 360));
            list.Add(MakeSphere("partial sphere", 1.3, -0.5, 0.9, 250));
            list.Add(MakeSphere("sphere zmin only", 1.3, 0.2, 1.3, 360));
            list.Add(MakeDisk("disk", 0.2, 1.5, 0, 360));
            list.Add(MakeDisk("annulus", -0.3, 1.5, 0.5, 200));
            list.Add(MakeCylinder("cylinder", 1, -0.8, 1.2, 360));
            list.Add(MakeCylinder("partial cylinder", 1, -0.5, 1, 270));
            list.Add(MakeCone("cone", 2, 1.2, 0, 2, 360));
            list.Add(MakeCone("partial cone", 2, 1.2, 0.3, 1.7, 300));
            list.Add(MakeParaboloid("paraboloid", 1.1, 0, 1.5, 360));
            list.Add(MakeParaboloid("partial paraboloid", 1.1, 0.4, 1.2, 250));
            list.Add(MakeHyperboloid("hyperboloid", new Point3f(1, 0, -1), new Point3f(0.5f, 0, 1), 360));
            list.Add(MakeHyperboloid("skew hyperboloid", new Point3f(1, 0, -1), new Point3f(0.3f, 0.8f, 1.2f), 360));
            list.Add(MakeHyperboloid("partial skew hyperboloid", new Point3f(1, 0, -1), new Point3f(0.3f, 0.8f, 1.2f), 220));
            list.Add(MakeHyperboloid("reversed hyperboloid", new Point3f(0.4f, 0.2f, 1), new Point3f(1, 0, -1), 300));
            list.Add(MakeTorus("torus", 1.2, 0.4, 0, 360, 360));
            list.Add(MakeTorus("partial torus", 1.2, 0.4, 30, 250, 280));
            list.Add(MakeTorus("torus crossing zero", 1.2, 0.4, -120, 100, 360));
            return list;
        }

        // The object-to-render transforms the tests run through
        private static Transform[] Transforms()
        {
            Transform rigid = Translate(new Vector3f(0.7f, -0.4f, 1.1f)) * Rotate(37, Normalize(new Vector3f(1, 2, -0.5f)));
            Transform uniform = Translate(new Vector3f(-1, 0.5f, 0.2f)) * Rotate(-71, Normalize(new Vector3f(-0.3f, 1, 0.8f))) * Scale(1.7f, 1.7f, 1.7f);
            Transform nonUniform = Translate(new Vector3f(0.3f, 0.2f, -0.6f)) * Rotate(23, Normalize(new Vector3f(0.5f, 0.1f, 1))) * Scale(1.5f, 0.7f, 1.1f);
            Transform mirror = Rotate(50, Normalize(new Vector3f(1, 1, 0.2f))) * Scale(1, 1, -1);
            return new Transform[] { new Transform(), rigid, uniform, nonUniform, mirror };
        }

        private static string[] TransformNames = { "identity", "rigid", "uniform", "non-uniform", "mirror" };

        private static Point3f Center(Bounds3f b)
        {
            return new Point3f((b.pMin.x + b.pMax.x) / 2, (b.pMin.y + b.pMax.y) / 2, (b.pMin.z + b.pMax.z) / 2);
        }

        private static Float Radius(Bounds3f b)
        {
            return Length(b.pMax - b.pMin) / 2;
        }

        private static Vector3f RandomDirection(Random rng)
        {
            return SampleUniformSphere(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
        }

        // The first crossing of the implicit surface by the object-space ray o + t d that lies in the clipped range, by marching
        private static bool March(Reference reference, double[] o, double[] d, double tFar, out double tHit)
        {
            const int steps = 3000;
            tHit = 0;
            double previousT = 0, previousF = reference.implicitFunction(o[0], o[1], o[2]);
            for (int i = 1; i <= steps; ++i)
            {
                double t = tFar * i / steps;
                double f = reference.implicitFunction(o[0] + t * d[0], o[1] + t * d[1], o[2] + t * d[2]);
                if ((previousF < 0) != (f < 0))
                {
                    double lo = previousT, hi = t, flo = previousF;
                    for (int k = 0; k < 70; ++k)
                    {
                        double mid = 0.5 * (lo + hi);
                        double fm = reference.implicitFunction(o[0] + mid * d[0], o[1] + mid * d[1], o[2] + mid * d[2]);
                        if ((fm < 0) == (flo < 0))
                        {
                            lo = mid;
                        }
                        else
                        {
                            hi = mid;
                        }
                    }
                    double root = 0.5 * (lo + hi);
                    if (root > 0 && reference.inRange(o[0] + root * d[0], o[1] + root * d[1], o[2] + root * d[2], 1e-7))
                    {
                        tHit = root;
                        return true;
                    }
                }
                previousT = t;
                previousF = f;
            }
            return false;
        }

        [TEST("Quadric", "HitsMatchRayMarching")]
        public static void HitsMatchRayMarching()
        {
            Transform[] transforms = Transforms();
            foreach (Reference reference in References())
            {
                Bounds3f objectBounds = reference.make(new Transform(), new Transform(), false).Bounds();
                Point3f center = Center(objectBounds);
                Float radius = Radius(objectBounds);
                foreach (int which in new int[] { 0, 3 })
                {
                    Transform t = transforms[which], ti = Inverse(t);
                    Shape shape = reference.make(t, ti, false);
                    Random rng = new Random(1234 + which);
                    int mismatches = 0, total = 0, pMismatches = 0, outsideBounds = 0;
                    Bounds3f bounds = shape.Bounds();
                    Float pad = 1e-4f * radius;
                    for (int i = 0; i < 1500; ++i)
                    {
                        // Half of the rays start outside and aim at the bounds, the others start inside
                        Vector3f dirFromCenter = RandomDirection(rng);
                        Point3f o;
                        if (i % 3 == 0)
                        {
                            o = center + (Float)(rng.NextDouble() * 0.6) * radius * RandomDirection(rng);
                        }
                        else
                        {
                            o = center + (Float)(1.5 + 2 * rng.NextDouble()) * radius * dirFromCenter;
                        }
                        Point3f target = new Point3f(Lerp((Float)rng.NextDouble(), objectBounds.pMin.x, objectBounds.pMax.x),
                                                     Lerp((Float)rng.NextDouble(), objectBounds.pMin.y, objectBounds.pMax.y),
                                                     Lerp((Float)rng.NextDouble(), objectBounds.pMin.z, objectBounds.pMax.z));
                        Vector3f d = target - o;
                        if (LengthSquared(d) < 1e-6f)
                        {
                            continue;
                        }
                        if (i % 2 == 0)
                        {
                            d = Normalize(d);
                        }
                        Ray ray = new Ray(t.Apply(o), t.Apply(d));
                        // The object-space ray, as the shape sees it
                        Point3f oo = ti.Apply(ray.o);
                        Vector3f dd = ti.Apply(ray.d);
                        double[] od = { oo.x, oo.y, oo.z }, dv = { dd.x, dd.y, dd.z };
                        double tFar = (Length(oo - center) + 1.2 * radius) / Length(dd);
                        double tRef;
                        bool refHit = March(reference, od, dv, tFar, out tRef);
                        pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                        bool hit = si.has_value();
                        ++total;
                        if (shape.IntersectP(ray) != hit)
                        {
                            ++pMismatches;
                        }
                        if (hit != refHit)
                        {
                            ++mismatches;
                        }
                        else if (hit)
                        {
                            EXPECT_NEAR(tRef, si.value().tHit, 2e-3 * Math.Max(1.0, tRef), reference.name + " t");
                            Point3f p = si.value().intr.p();
                            if (p.x < bounds.pMin.x - pad || p.y < bounds.pMin.y - pad || p.z < bounds.pMin.z - pad ||
                                p.x > bounds.pMax.x + pad || p.y > bounds.pMax.y + pad || p.z > bounds.pMax.z + pad)
                            {
                                ++outsideBounds;
                            }
                        }
                    }
                    EXPECT_EQ(0, pMismatches, reference.name + " IntersectP vs Intersect " + TransformNames[which]);
                    EXPECT_EQ(0, outsideBounds, reference.name + " hits outside Bounds() " + TransformNames[which]);
                    EXPECT_LT(mismatches, 1 + total / 250, reference.name + " hit/miss mismatches " + TransformNames[which] + " of " + total);
                }
            }
        }

        [TEST("Quadric", "NoSelfIntersection")]
        public static void NoSelfIntersection()
        {
            Transform[] transforms = Transforms();
            foreach (Reference reference in References())
            {
                Bounds3f objectBounds = reference.make(new Transform(), new Transform(), false).Bounds();
                Point3f center = Center(objectBounds);
                Float radius = Radius(objectBounds);
                for (int which = 0; which < transforms.Length; ++which)
                {
                    Transform t = transforms[which], ti = Inverse(t);
                    Shape shape = reference.make(t, ti, which == 1);
                    Random rng = new Random(77 + which);
                    int failures = 0, hits = 0;
                    for (int i = 0; i < 1500; ++i)
                    {
                        Point3f o = center + (Float)(1.5 + 2 * rng.NextDouble()) * radius * RandomDirection(rng);
                        Point3f target = new Point3f(Lerp((Float)rng.NextDouble(), objectBounds.pMin.x, objectBounds.pMax.x),
                                                     Lerp((Float)rng.NextDouble(), objectBounds.pMin.y, objectBounds.pMax.y),
                                                     Lerp((Float)rng.NextDouble(), objectBounds.pMin.z, objectBounds.pMax.z));
                        Ray ray = new Ray(t.Apply(o), Normalize(t.Apply(target - o)));
                        pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                        if (!si.has_value())
                        {
                            continue;
                        }
                        ++hits;
                        SurfaceInteraction intr = si.value().intr;
                        // Continue through the surface, and come back out of it: neither ray may hit the point it starts at
                        foreach (Vector3f w in new Vector3f[] { ray.d, -ray.d })
                        {
                            Ray next = new Ray(intr.OffsetRayOrigin(w), w);
                            pstd.optional<ShapeIntersection> again = shape.Intersect(next);
                            if (again.has_value() && again.value().tHit < 1e-5f * radius)
                            {
                                ++failures;
                            }
                        }
                    }
                    EXPECT_GT(hits, 100, reference.name);
                    EXPECT_EQ(0, failures, reference.name + " self intersections " + TransformNames[which]);
                }
            }
        }

        // The integral of g over the surface by Crofton's formula: for lines in uniformly random directions through a disk of
        // radius R, the sum of g over the crossings has the mean (1 / 2) (integral of g dA) / (pi R^2)
        private static double CroftonIntegral(Shape shape, Func<Point3f, double> g, int lines, int seed)
        {
            Bounds3f bounds = shape.Bounds();
            Point3f center = Center(bounds);
            Float radius = Radius(bounds);
            Random rng = new Random(seed);
            double sum = 0;
            for (int i = 0; i < lines; ++i)
            {
                Vector3f w = RandomDirection(rng);
                Vector3f a, b;
                CoordinateSystem(w, &a, &b);
                Point2f disk = SampleUniformDiskConcentric(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                Point3f o = center - 1.5f * radius * w + radius * (disk.x * a + disk.y * b);
                Ray ray = new Ray(o, w);
                for (int crossing = 0; crossing < 32; ++crossing)
                {
                    pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                    if (!si.has_value())
                    {
                        break;
                    }
                    sum += g(si.value().intr.p());
                    ray = new Ray(si.value().intr.OffsetRayOrigin(w), w);
                }
            }
            return 2 * Math.PI * radius * radius * sum / lines;
        }

        [TEST("Quadric", "AreaAndSamplingMatchCrofton")]
        public static void AreaAndSamplingMatchCrofton()
        {
            Transform[] transforms = Transforms();
            foreach (Reference reference in References())
            {
                foreach (int which in new int[] { 0, 2, 3, 4 })
                {
                    Transform t = transforms[which], ti = Inverse(t);
                    Shape shape = reference.make(t, ti, false);
                    Bounds3f bounds = shape.Bounds();
                    Point3f center = Center(bounds);
                    Float radius = Radius(bounds);
                    Func<Point3f, double> g = (Point3f p) => 1.0 + 0.5 * (p.x - center.x) / radius + 0.3 * (p.z - center.z) / radius;

                    double area = CroftonIntegral(shape, (Point3f p) => 1.0, 100000, 5 + 7 * which + reference.name.Length);
                    double weighted = CroftonIntegral(shape, g, 100000, 105 + 7 * which + reference.name.Length);
                    if (System.Environment.GetEnvironmentVariable("QUADRIC_VERBOSE") != null)
                    {
                        Console.WriteLine(reference.name + " " + TransformNames[which] + ": Area() " + shape.Area() + " Crofton " + area);
                    }
                    EXPECT_NEAR(1.0, shape.Area() / area, 0.05, reference.name + " Area() vs Crofton " + TransformNames[which]);

                    // The importance-sampled estimates of the same integrals
                    Random rng = new Random(9);
                    double sampledArea = 0, sampledWeighted = 0;
                    const int n = 40000;
                    for (int i = 0; i < n; ++i)
                    {
                        pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                        ASSERT_TRUE(ss.has_value());
                        sampledArea += 1 / ss.value().pdf;
                        sampledWeighted += g(ss.value().intr.p()) / ss.value().pdf;
                        // the area density of PDF(Interaction) is that of the sampler
                        if (i < 50)
                        {
                            EXPECT_NEAR(1.0, shape.PDF(ss.value().intr) / ss.value().pdf, 2e-3, reference.name + " PDF(Interaction) " + TransformNames[which]);
                        }
                    }
                    sampledArea /= n;
                    sampledWeighted /= n;
                    EXPECT_NEAR(1.0, sampledArea / shape.Area(), 0.05, reference.name + " mean 1/pdf vs Area() " + TransformNames[which]);
                    EXPECT_NEAR(1.0, sampledWeighted / weighted, 0.05, reference.name + " sampled integral vs Crofton " + TransformNames[which]);
                }
            }
        }

        [TEST("Quadric", "SamplesAreOnTheSurface")]
        public static void SamplesAreOnTheSurface()
        {
            Transform[] transforms = Transforms();
            foreach (Reference reference in References())
            {
                for (int which = 0; which < transforms.Length; ++which)
                {
                    foreach (bool reverse in new bool[] { false, true })
                    {
                        Transform t = transforms[which], ti = Inverse(t);
                        Shape shape = reference.make(t, ti, reverse);
                        Bounds3f bounds = shape.Bounds();
                        Float radius = Radius(bounds);
                        DirectionCone cone = shape.NormalBounds();
                        Random rng = new Random(31 + which);
                        int outside = 0, normalOutside = 0, off = 0;
                        for (int i = 0; i < 2000; ++i)
                        {
                            pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                            ASSERT_TRUE(ss.has_value());
                            EXPECT_GT(ss.value().pdf, 0, reference.name);
                            Point3f p = ss.value().intr.p();
                            Float pad = 1e-4f * radius;
                            if (p.x < bounds.pMin.x - pad || p.y < bounds.pMin.y - pad || p.z < bounds.pMin.z - pad ||
                                p.x > bounds.pMax.x + pad || p.y > bounds.pMax.y + pad || p.z > bounds.pMax.z + pad)
                            {
                                ++outside;
                            }
                            Normal3f n = ss.value().intr.n;
                            if (Dot((Vector3f)n, cone.w) < cone.cosTheta - 1e-3f)
                            {
                                ++normalOutside;
                            }
                            Point3f po = ti.Apply(p);
                            double f = reference.implicitFunction(po.x, po.y, po.z);
                            if (!reference.inRange(po.x, po.y, po.z, 1e-3) || Math.Abs(f) > 1e-3 * (1 + Math.Abs(po.x) + Math.Abs(po.y) + Math.Abs(po.z)))
                            {
                                ++off;
                            }
                        }
                        EXPECT_EQ(0, outside, reference.name + " samples outside Bounds() " + TransformNames[which]);
                        EXPECT_EQ(0, normalOutside, reference.name + " sampled normals outside NormalBounds() " + TransformNames[which] + (reverse ? " reversed" : ""));
                        EXPECT_EQ(0, off, reference.name + " samples off the surface or outside the clipping " + TransformNames[which]);
                    }
                }
            }
        }

        [TEST("Quadric", "HitNormalsInNormalBounds")]
        public static void HitNormalsInNormalBounds()
        {
            Transform[] transforms = Transforms();
            foreach (Reference reference in References())
            {
                Bounds3f objectBounds = reference.make(new Transform(), new Transform(), false).Bounds();
                Point3f center = Center(objectBounds);
                Float radius = Radius(objectBounds);
                for (int which = 0; which < transforms.Length; ++which)
                {
                    foreach (bool reverse in new bool[] { false, true })
                    {
                        Transform t = transforms[which], ti = Inverse(t);
                        Shape shape = reference.make(t, ti, reverse);
                        DirectionCone cone = shape.NormalBounds();
                        Random rng = new Random(5 + which);
                        int outside = 0;
                        for (int i = 0; i < 800; ++i)
                        {
                            Point3f o = center + (Float)(1.5 + 2 * rng.NextDouble()) * radius * RandomDirection(rng);
                            Point3f target = new Point3f(Lerp((Float)rng.NextDouble(), objectBounds.pMin.x, objectBounds.pMax.x),
                                                         Lerp((Float)rng.NextDouble(), objectBounds.pMin.y, objectBounds.pMax.y),
                                                         Lerp((Float)rng.NextDouble(), objectBounds.pMin.z, objectBounds.pMax.z));
                            pstd.optional<ShapeIntersection> si = shape.Intersect(new Ray(t.Apply(o), t.Apply(target - o)));
                            if (si.has_value() && Dot((Vector3f)si.value().intr.n, cone.w) < cone.cosTheta - 1e-3f)
                            {
                                ++outside;
                            }
                        }
                        EXPECT_EQ(0, outside, reference.name + " hit normals outside NormalBounds() " + TransformNames[which] + (reverse ? " reversed" : ""));
                    }
                }
            }
        }

        [TEST("Quadric", "SampleAgreesWithIntersection")]
        public static void SampleAgreesWithIntersection()
        {
            Transform[] transforms = Transforms();
            foreach (Reference reference in References())
            {
                for (int which = 0; which < transforms.Length; ++which)
                {
                    foreach (bool reverse in new bool[] { false, true })
                    {
                        Transform t = transforms[which], ti = Inverse(t);
                        Shape shape = reference.make(t, ti, reverse);
                        Float radius = Radius(shape.Bounds());
                        Random rng = new Random(11 + which);
                        int missed = 0, total = 0, badNormal = 0, badUV = 0, badPoint = 0;
                        for (int i = 0; i < 800; ++i)
                        {
                            pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                            Point3f p = ss.value().intr.p();
                            Normal3f n = ss.value().intr.n;
                            Float eps = 2e-3f * radius;
                            Ray ray = new Ray(p + eps * (Vector3f)n, -(Vector3f)n);
                            ++total;
                            pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                            if (!si.has_value())
                            {
                                ++missed;
                                continue;
                            }
                            SurfaceInteraction intr = si.value().intr;
                            if (Distance(intr.p(), p) > 2e-4f * radius)
                            {
                                ++badPoint;
                                continue;
                            }
                            if (Dot((Vector3f)intr.n, (Vector3f)n) < 0.999f)
                            {
                                ++badNormal;
                            }
                            Point2f uvs = ss.value().intr.uv, uvh = intr.uv;
                            Float du = std.abs(uvs.x - uvh.x), dv = std.abs(uvs.y - uvh.y);
                            bool seamU = du > 0.99f || uvs.x < 0.01f || uvs.x > 0.99f, seamV = dv > 0.99f || uvs.y < 0.01f || uvs.y > 0.99f;
                            if ((!seamU && du > 2e-3f) || (!seamV && dv > 2e-3f))
                            {
                                ++badUV;
                            }
                        }
                        string label = reference.name + " " + TransformNames[which] + (reverse ? " reversed" : "");
                        EXPECT_LT(missed, 1 + total / 100, label + " sample points not hit from outside");
                        EXPECT_EQ(0, badPoint, label + " hit elsewhere");
                        EXPECT_EQ(0, badNormal, label + " sample normal differs from the hit normal");
                        EXPECT_EQ(0, badUV, label + " sample uv differs from the hit uv");
                    }
                }
            }
        }

        [TEST("Quadric", "ParameterizationMatchesRenderMan")]
        public static void ParameterizationMatchesRenderMan()
        {
            foreach (Reference reference in References())
            {
                Shape shape = reference.make(new Transform(), new Transform(), false);
                Float radius = Radius(shape.Bounds());
                double h = 1e-3;
                foreach (double u in new double[] { 0.1, 0.35, 0.6, 0.85 })
                {
                    foreach (double v in new double[] { 0.12, 0.4, 0.66, 0.9 })
                    {
                        Point3f p = reference.parameterization(u, v);
                        Point3f pu0 = reference.parameterization(u - h, v), pu1 = reference.parameterization(u + h, v);
                        Point3f pv0 = reference.parameterization(u, v - h), pv1 = reference.parameterization(u, v + h);
                        Vector3f dpdu = (pu1 - pu0) / (float)(2 * h), dpdv = (pv1 - pv0) / (float)(2 * h);
                        Vector3f n = Normalize(Cross(dpdu, dpdv));
                        Ray ray = new Ray(p + 0.02f * radius * n, -n);
                        pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                        string label = reference.name + " (u, v) = (" + u + ", " + v + ")";
                        ASSERT_TRUE(si.has_value(), label);
                        SurfaceInteraction intr = si.value().intr;
                        EXPECT_LT(Distance(intr.p(), p), 2e-4f * radius, label + " point");
                        EXPECT_NEAR(u, intr.uv.x, 2e-3, label + " u");
                        EXPECT_NEAR(v, intr.uv.y, 2e-3, label + " v");
                        EXPECT_GT(Dot((Vector3f)intr.n, n), 0.999f, label + " normal orientation");
                        // dpdu and dpdv of the hit point the same way as the parameterization (up to the length of the parameter step)
                        EXPECT_GT(Dot(Normalize(intr.dpdu), Normalize(dpdu)), 0.999f, label + " dpdu");
                        EXPECT_GT(Dot(Normalize(intr.dpdv), Normalize(dpdv)), 0.999f, label + " dpdv");
                        EXPECT_NEAR(1.0, Length(intr.dpdu) / Length(dpdu), 3e-3, label + " |dpdu|");
                        EXPECT_NEAR(1.0, Length(intr.dpdv) / Length(dpdv), 3e-3, label + " |dpdv|");
                    }
                }
            }
        }

        // The reference point for the solid angle tests: outside the shape, and near its middle
        private static Point3f[] ReferencePoints(Shape shape, Random rng)
        {
            Bounds3f bounds = shape.Bounds();
            Point3f center = Center(bounds);
            Float radius = Radius(bounds);
            return new Point3f[] { center + 2.2f * radius * RandomDirection(rng), center + 0.35f * radius * RandomDirection(rng),
                                   center + 1.3f * radius * RandomDirection(rng) };
        }

        [TEST("Quadric", "SolidAngleSampling")]
        public static void SolidAngleSampling()
        {
            Transform[] transforms = Transforms();
            foreach (Reference reference in References())
            {
                foreach (int which in new int[] { 0, 1, 3, 4 })
                {
                    Transform t = transforms[which], ti = Inverse(t);
                    Shape shape = reference.make(t, ti, which == 1);
                    Random rng = new Random(2024 + which);
                    int pointIndex = 0;
                    foreach (Point3f p in ReferencePoints(shape, rng))
                    {
                        ++pointIndex;
                        string label = reference.name + " " + TransformNames[which] + " point " + pointIndex;
                        ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);

                        // The solid angle by uniformly random directions, and the integral of PDF(ctx, wi) over them
                        const int nDirections = 150000;
                        int hits = 0;
                        double pdfSum = 0;
                        for (int i = 0; i < nDirections; ++i)
                        {
                            Vector3f w = RandomDirection(rng);
                            if (shape.IntersectP(new Ray(p, w)))
                            {
                                ++hits;
                            }
                            pdfSum += shape.PDF(ctx, w);
                        }
                        double solidAngle = 4 * Math.PI * hits / nDirections;
                        double pdfIntegral = 4 * Math.PI * pdfSum / nDirections;

                        // The solid angle by the samples that see the shape (the first hit on the way to them is the sample itself)
                        const int nSamples = 40000;
                        double sampledSolidAngle = 0;
                        int visible = 0, inconsistent = 0, failed = 0;
                        for (int i = 0; i < nSamples; ++i)
                        {
                            pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                            if (!ss.has_value() || ss.value().pdf <= 0)
                            {
                                ++failed;
                                continue;
                            }
                            Point3f q = ss.value().intr.p();
                            Vector3f wi = q - p;
                            Float distance = Length(wi);
                            wi /= distance;
                            pstd.optional<ShapeIntersection> first = shape.Intersect(new Ray(p, wi));
                            if (!first.has_value() || std.abs(first.value().tHit - distance) > 2e-5f * distance)
                            {
                                continue;
                            }
                            ++visible;
                            sampledSolidAngle += 1 / ss.value().pdf;
                            Float pdf = shape.PDF(ctx, wi);
                            if (std.abs(pdf - ss.value().pdf) > 2e-3f * ss.value().pdf)
                            {
                                ++inconsistent;
                            }
                        }
                        sampledSolidAngle /= nSamples;
                        if (solidAngle > 0.05 && System.Environment.GetEnvironmentVariable("QUADRIC_VERBOSE") != null)
                        {
                            Console.WriteLine(label + ": solid angle " + solidAngle + " sampled " + sampledSolidAngle + " pdf integral " + pdfIntegral + " visible " + visible / (double)nSamples);
                        }
                        if (solidAngle > 0.05 && solidAngle < 4.0)
                        {
                            EXPECT_NEAR(1.0, sampledSolidAngle / solidAngle, 0.06, label + " solid angle from samples vs directions: " + solidAngle);
                            // The density of the nearest hits integrates to the share of the samples that are visible
                            EXPECT_NEAR(visible / (double)(nSamples - failed), pdfIntegral, 0.05 + 0.15 * pdfIntegral, label + " integral of the pdf vs the visible share of the samples");
                        }
                        EXPECT_LE(inconsistent, 1 + visible / 100, label + " Sample pdf differs from PDF(ctx, wi) for visible samples");
                        EXPECT_LT(failed, nSamples / 50, label + " failed samples");
                    }
                }
            }
        }

        [TEST("Quadric", "SphereConeSamplingMatchesAreaSampling")]
        public static void SphereConeSamplingMatchesAreaSampling()
        {
            Transform t = Translate(new Vector3f(0.5f, -1, 2)) * Scale(1.4f, 1.4f, 1.4f);
            Sphere sphere = new Sphere(t, Inverse(t), false, 1, -1, 1, 360);
            Point3f p = new Point3f(4, 3, -1);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
            Point3f c = t.Apply(new Point3f(0, 0, 0));
            Float sinThetaMax = 1.4f / Distance(p, c);
            Float expectedPdf = 1 / (2 * Pi * (1 - std.sqrt(1 - sinThetaMax * sinThetaMax)));
            Random rng = new Random(4);
            for (int i = 0; i < 500; ++i)
            {
                pstd.optional<ShapeSample> ss = sphere.Sample(ctx, new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                ASSERT_TRUE(ss.has_value());
                EXPECT_NEAR(1.0, ss.value().pdf / expectedPdf, 1e-4);
                // on the sphere of radius 1.4 about c, on the side facing p
                EXPECT_NEAR(1.4, Distance(ss.value().intr.p(), c), 1e-3);
                EXPECT_GT(Dot(Normalize(p - c), (Vector3f)ss.value().intr.n), 0.0f);
                EXPECT_NEAR(1.0, sphere.PDF(ctx, Normalize(ss.value().intr.p() - p)) / expectedPdf, 1e-4);
            }
            // a direction away from the sphere has zero density
            EXPECT_EQ(0.0f, sphere.PDF(ctx, Normalize(p - c)));
        }

        [TEST("Quadric", "ClosedFormAreas")]
        public static void ClosedFormAreas()
        {
            Transform id = new Transform();
            double twoPi = 2 * Math.PI;
            EXPECT_NEAR(4 * Math.PI * 1.69, new Sphere(id, id, false, 1.3f, -1.3f, 1.3f, 360).Area(), 1e-3);
            EXPECT_NEAR(twoPi * 1.3 * 1.4 * (250.0 / 360), new Sphere(id, id, false, 1.3f, -0.5f, 0.9f, 250).Area(), 1e-3);
            EXPECT_NEAR(Math.PI * (2.25 - 0.25), new Disk(id, id, false, 0, 1.5f, 0.5f, 360).Area(), 1e-3);
            EXPECT_NEAR(twoPi * 1 * 2, new Cylinder(id, id, false, 1, -1, 1, 360).Area(), 1e-3);
            // the lateral area of a cone: pi r sqrt(r^2 + h^2); and of a frustum between the heights
            EXPECT_NEAR(Math.PI * 1.2 * Math.Sqrt(1.44 + 4), new Cone(id, id, false, 2, 1.2f, 0, 2, 360).Area(), 1e-3);
            double slant = Math.Sqrt(1.44 + 4);
            double rLow = 1.2 * (1 - 0.3 / 2), rHigh = 1.2 * (1 - 1.7 / 2);
            EXPECT_NEAR(Math.PI * (rLow + rHigh) * (slant * 1.4 / 2) * (300.0 / 360), new Cone(id, id, false, 2, 1.2f, 0.3f, 1.7f, 300).Area(), 2e-3);
            // a hyperboloid whose segment is that of the cone is the cone
            Hyperboloid asCone = new Hyperboloid(id, id, false, new Point3f(1.2f, 0, 0), new Point3f(0, 0, 2), 360);
            EXPECT_NEAR(Math.PI * 1.2 * Math.Sqrt(1.44 + 4), asCone.Area(), 1e-3);
            // the paraboloid of revolution: (pi / 6) (r / h^2) ((r^2 + 4 h^2)^(3/2) - r^3) for z = h (r^2 = ... ): use the integral form
            double r = 1.1, zmax = 1.5, c2 = r * r / zmax, m = c2 / 4;
            EXPECT_NEAR(twoPi * (2 * Math.Sqrt(c2) / 3) * (Math.Pow(zmax + m, 1.5) - Math.Pow(m, 1.5)),
                        new Paraboloid(id, id, false, 1.1f, 0, 1.5f, 360).Area(), 2e-3);
            // the torus: 4 pi^2 R r, and the part of it
            EXPECT_NEAR(4 * Math.PI * Math.PI * 1.2 * 0.4, new Torus(id, id, false, 1.2f, 0.4f, 0, 360, 360).Area(), 1e-3);
            EXPECT_NEAR(2 * Math.PI * 0.4 * 1.2 * Math.PI, new Torus(id, id, false, 1.2f, 0.4f, 0, 180, 360).Area(), 1e-3);
        }

        [TEST("Quadric", "HyperboloidOfTheConeIntersectsLikeTheCone")]
        public static void HyperboloidOfTheConeIntersectsLikeTheCone()
        {
            Transform t = Translate(new Vector3f(0.2f, 0.1f, -0.3f)) * Rotate(20, new Vector3f(0, 1, 0));
            Transform ti = Inverse(t);
            Cone cone = new Cone(t, ti, false, 2, 1.2f, 0, 2, 300);
            Hyperboloid hyperboloid = new Hyperboloid(t, ti, false, new Point3f(1.2f, 0, 0), new Point3f(0, 0, 2), 300);
            Random rng = new Random(8);
            int hits = 0;
            for (int i = 0; i < 3000; ++i)
            {
                Point3f o = new Point3f((Float)(rng.NextDouble() * 6 - 3), (Float)(rng.NextDouble() * 6 - 3), (Float)(rng.NextDouble() * 6 - 3));
                Point3f target = new Point3f((Float)(rng.NextDouble() * 3 - 1.5), (Float)(rng.NextDouble() * 3 - 1.5), (Float)(rng.NextDouble() * 2));
                Ray ray = new Ray(t.Apply(o), t.Apply(target - o));
                pstd.optional<ShapeIntersection> a = cone.Intersect(ray), b = hyperboloid.Intersect(ray);
                EXPECT_EQ(a.has_value(), b.has_value());
                if (a.has_value() && b.has_value())
                {
                    ++hits;
                    EXPECT_NEAR(a.value().tHit, b.value().tHit, 1e-4 * Math.Max(1.0, a.value().tHit));
                    EXPECT_NEAR(a.value().intr.uv.x, b.value().intr.uv.x, 1e-3);
                    EXPECT_NEAR(a.value().intr.uv.y, b.value().intr.uv.y, 1e-3);
                }
            }
            EXPECT_GT(hits, 300);
        }

        // Rays that are tangent, parallel to a generator or to the axis, or start on the surface
        [TEST("Quadric", "DegenerateRays")]
        public static void DegenerateRays()
        {
            Transform id = new Transform();
            Cylinder cylinder = new Cylinder(id, id, false, 1, -1, 1, 360);
            EXPECT_FALSE(cylinder.IntersectP(new Ray(new Point3f(0.2f, 0.1f, -3), new Vector3f(0, 0, 1))));
            EXPECT_TRUE(cylinder.IntersectP(new Ray(new Point3f(3, 0.1f, 0), new Vector3f(-1, 0, 0))));
            EXPECT_FALSE(cylinder.IntersectP(new Ray(new Point3f(3, 0.1f, 5), new Vector3f(-1, 0, 0))));

            // down the axis of a cone onto its apex region, and parallel to a generator of the cone
            Cone cone = new Cone(id, id, false, 2, 1, 0, 2, 360);
            pstd.optional<ShapeIntersection> side = cone.Intersect(new Ray(new Point3f(3, 0, 0.5f), new Vector3f(-1, 0, 0)));
            ASSERT_TRUE(side.has_value());
            EXPECT_NEAR(3 - 0.75, side.value().tHit, 1e-4);
            Vector3f generator = Normalize(new Vector3f(-1, 0, 2));   // the generator in the xz plane runs from (1, 0, 0) to (0, 0, 2)
            pstd.optional<ShapeIntersection> along = cone.Intersect(new Ray(new Point3f(1, 0, 0) + 0.4f * Normalize(new Vector3f(2, 0, 1)), generator));
            if (along.has_value())
            {
                Point3f q = along.value().intr.p();
                EXPECT_FALSE(IsNaN(along.value().tHit));
                EXPECT_NEAR(Math.Sqrt((double)q.x * q.x + (double)q.y * q.y), 0.5 * (2 - q.z), 1e-4);
            }

            // a ray down into the bowl of a paraboloid close to its axis hits it near the apex
            Paraboloid paraboloid = new Paraboloid(id, id, false, 1, 0, 1, 360);
            pstd.optional<ShapeIntersection> inside = paraboloid.Intersect(new Ray(new Point3f(0, 0.01f, 2), new Vector3f(0, 0, -1)));
            ASSERT_TRUE(inside.has_value());
            EXPECT_NEAR(1e-4, inside.value().intr.p().z, 1e-4);
        }

        [TEST("Quadric", "TorusFarAndTiny")]
        public static void TorusFarAndTiny()
        {
            Transform id = new Transform();
            Random rng = new Random(99);
            int failures = 0, total = 0, offSurface = 0;
            foreach (double scale in new double[] { 1e-3, 1.0, 1e3 })
            {
                for (int k = 0; k < 600; ++k)
                {
                    double major = scale * (0.5 + rng.NextDouble());
                    double minor = major * (0.05 + 0.9 * rng.NextDouble());
                    Torus torus = new Torus(id, id, false, (Float)major, (Float)minor, 0, 360, 360);
                    double theta = rng.NextDouble() * 2 * Math.PI, phi = rng.NextDouble() * 2 * Math.PI;
                    double rho = major + minor * Math.Cos(phi);
                    Point3f aim = new Point3f((Float)(rho * Math.Cos(theta)), (Float)(rho * Math.Sin(theta)), (Float)(minor * Math.Sin(phi)));
                    Vector3f dir = RandomDirection(rng);
                    Point3f o = aim - (Float)(major * (3 + 40 * rng.NextDouble())) * dir;
                    pstd.optional<ShapeIntersection> si = torus.Intersect(new Ray(o, dir));
                    ++total;
                    if (!si.has_value())
                    {
                        ++failures;
                        continue;
                    }
                    Point3f p = si.value().intr.p();
                    double radial = Math.Sqrt((double)p.x * p.x + (double)p.y * p.y) - major;
                    double residual = Math.Abs(radial * radial + (double)p.z * p.z - minor * minor) / (minor * minor);
                    if (residual > 1e-4 || si.value().tHit > Distance(o, aim) + 1e-3 * major)
                    {
                        ++offSurface;
                    }
                }
            }
            EXPECT_LT(failures, 1 + total / 500, "rays aimed at the torus that miss it: " + failures + " of " + total);
            EXPECT_EQ(0, offSurface);
        }

        // The quartic solver
        private static int MissedRoots(double[] expected, double a, double tolerance, out int count)
        {
            // a (x - r0)(x - r1)(x - r2)(x - r3)
            double e1 = expected[0] + expected[1] + expected[2] + expected[3];
            double e2 = expected[0] * expected[1] + expected[0] * expected[2] + expected[0] * expected[3] + expected[1] * expected[2] +
                        expected[1] * expected[3] + expected[2] * expected[3];
            double e3 = expected[0] * expected[1] * expected[2] + expected[0] * expected[1] * expected[3] +
                        expected[0] * expected[2] * expected[3] + expected[1] * expected[2] * expected[3];
            double e4 = expected[0] * expected[1] * expected[2] * expected[3];
            double* roots = stackalloc double[4];
            count = RobustQuartic.Solve(a, -a * e1, a * e2, -a * e3, a * e4, roots);
            if (System.Environment.GetEnvironmentVariable("QUADRIC_VERBOSE") != null)
            {
                Console.WriteLine("quartic roots " + count + ": " + (count > 0 ? roots[0] : 0) + " " + (count > 1 ? roots[1] : 0) + " " + (count > 2 ? roots[2] : 0) + " " + (count > 3 ? roots[3] : 0));
            }
            int missed = 0;
            foreach (double root in expected)
            {
                bool found = false;
                for (int i = 0; i < count; ++i)
                {
                    if (Math.Abs(roots[i] - root) <= tolerance * (1 + Math.Abs(root)))
                    {
                        found = true;
                    }
                }
                if (!found)
                {
                    ++missed;
                }
            }
            for (int i = 1; i < count; ++i)
            {
                if (roots[i] < roots[i - 1])
                {
                    ++missed;
                }
            }
            return missed;
        }

        [TEST("Quadric", "QuarticFourRealRoots")]
        public static void QuarticFourRealRoots()
        {
            Random random = new Random(3);
            int total = 0, bad = 0;
            for (int k = 0; k < 30000; ++k)
            {
                double[] expected = { random.NextDouble() * 6 - 3, random.NextDouble() * 6 - 3, random.NextDouble() * 6 - 3, random.NextDouble() * 6 - 3 };
                Array.Sort(expected);
                if (expected[1] - expected[0] < 0.02 || expected[2] - expected[1] < 0.02 || expected[3] - expected[2] < 0.02)
                {
                    continue;
                }
                double a = (random.NextDouble() + 0.1) * Math.Pow(10, random.Next(-3, 4)) * (random.Next(2) == 0 ? 1 : -1);
                int count;
                ++total;
                if (MissedRoots(expected, a, 1e-7, out count) != 0 || count != 4)
                {
                    ++bad;
                }
            }
            EXPECT_GT(total, 15000);
            EXPECT_EQ(0, bad, "quartics with four separated roots that were not solved: " + bad + " of " + total);
        }

        [TEST("Quadric", "QuarticTwoRealRootsAndAComplexPair")]
        public static void QuarticTwoRealRootsAndAComplexPair()
        {
            Random random = new Random(4);
            int total = 0, bad = 0;
            double* roots = stackalloc double[4];
            for (int k = 0; k < 30000; ++k)
            {
                double r0 = random.NextDouble() * 6 - 3, r1 = random.NextDouble() * 6 - 3;
                if (Math.Abs(r0 - r1) < 0.02)
                {
                    continue;
                }
                double b = random.NextDouble() * 4 - 2, c = b * b / 4 + 0.05 + random.NextDouble() * 3;   // x^2 + b x + c without real roots
                // (x - r0)(x - r1)(x^2 + b x + c)
                double s = r0 + r1, p = r0 * r1;
                double c4 = 1, c3 = b - s, c2 = c - s * b + p, c1 = -s * c + p * b, c0 = p * c;
                int count = RobustQuartic.Solve(c4, c3, c2, c1, c0, roots);
                ++total;
                bool found0 = false, found1 = false;
                for (int i = 0; i < count; ++i)
                {
                    found0 |= Math.Abs(roots[i] - r0) < 1e-7 * (1 + Math.Abs(r0));
                    found1 |= Math.Abs(roots[i] - r1) < 1e-7 * (1 + Math.Abs(r1));
                }
                if (count != 2 || !found0 || !found1)
                {
                    ++bad;
                }
            }
            EXPECT_EQ(0, bad, "quartics with two real roots that were not solved: " + bad + " of " + total);
        }

        [TEST("Quadric", "QuarticNoRealRootsAndDoubleRoots")]
        public static void QuarticNoRealRootsAndDoubleRoots()
        {
            Random random = new Random(5);
            double* roots = stackalloc double[4];
            int wrong = 0;
            for (int k = 0; k < 5000; ++k)
            {
                // (x^2 + b1 x + c1)(x^2 + b2 x + c2) with both factors without real roots
                double b1 = random.NextDouble() * 4 - 2, c1 = b1 * b1 / 4 + 0.05 + random.NextDouble();
                double b2 = random.NextDouble() * 4 - 2, c2 = b2 * b2 / 4 + 0.05 + random.NextDouble();
                int count = RobustQuartic.Solve(1, b1 + b2, c1 + c2 + b1 * b2, b1 * c2 + b2 * c1, c1 * c2, roots);
                if (count != 0)
                {
                    ++wrong;
                }
            }
            EXPECT_EQ(0, wrong, "quartics without real roots that returned some");

            // (x - a)^2 (x - b)(x - c): the double root is found (to the accuracy of a double root), as are the others
            int missed = 0;
            for (int k = 0; k < 5000; ++k)
            {
                double a = random.NextDouble() * 4 - 2, b = a + 0.3 + random.NextDouble(), c = b + 0.3 + random.NextDouble();
                double[] expected = { a, a, b, c };
                int count;
                missed += MissedRoots(expected, 1, 1e-6, out count) != 0 ? 1 : 0;
            }
            EXPECT_EQ(0, missed, "quartics with a double root that were not solved");
        }

        [TEST("Quadric", "QuarticDegreeDropsAndScales")]
        public static void QuarticDegreeDropsAndScales()
        {
            double* roots = stackalloc double[4];
            // a cubic: x^3 - 6 x^2 + 11 x - 6 = (x - 1)(x - 2)(x - 3)
            int count = RobustQuartic.Solve(0, 1, -6, 11, -6, roots);
            EXPECT_EQ(3, count);
            EXPECT_NEAR(1, roots[0], 1e-9);
            EXPECT_NEAR(2, roots[1], 1e-9);
            EXPECT_NEAR(3, roots[2], 1e-9);

            // roots at 1e-4 and 1e4 apart (the torus quartic is solved in units of the major radius, but the solver must cope)
            double[] expected = { -1e4, -2, 3e-4, 5e3 };
            int ignored;
            EXPECT_EQ(0, MissedRoots(expected, 1, 1e-8, out ignored));
            double[] small = { -1e-4, -2e-5, 3e-6, 5e-5 };
            Array.Sort(small);
            EXPECT_EQ(0, MissedRoots(small, 1e12, 1e-8, out ignored));
        }

        [TEST("Quadric", "ShapeCreateParsesTheNewQuadrics")]
        public static void ShapeCreateParsesTheNewQuadrics()
        {
            Transform id = new Transform();
            Func<string, string, Float[], ParsedParameter> param = (string type, string name, Float[] values) =>
            {
                ParsedParameter p = new ParsedParameter(null);
                p.type = type;
                p.name = name;
                foreach (Float v in values)
                {
                    p.floats.push_back(v);
                }
                return p;
            };
            Func<string, List<ParsedParameter>, Shape> create = (string name, List<ParsedParameter> list) =>
            {
                ParameterDictionary dict = new ParameterDictionary(list, RGBColorSpace.sRGB);
                pstd.vector<Shape> shapes = Shape.Create(name, id, id, false, dict, new Dictionary<string, FloatTexture>(), null, new Allocator());
                EXPECT_EQ(1, shapes.size());
                return shapes[0];
            };

            // cone: height 2, radius 1, half a turn given as RenderMan's thetamax
            Shape cone = create("cone", new List<ParsedParameter> { param("float", "height", new Float[] { 2 }), param("float", "radius", new Float[] { 1 }),
                                                                   param("float", "thetamax", new Float[] { 180 }) });
            EXPECT_TRUE(cone is Cone);
            EXPECT_NEAR(0.5 * Math.PI * Math.Sqrt(5), cone.Area(), 1e-3);
            // the same with pbrt's phimax
            Shape cone2 = create("cone", new List<ParsedParameter> { param("float", "height", new Float[] { 2 }), param("float", "radius", new Float[] { 1 }),
                                                                    param("float", "phimax", new Float[] { 180 }) });
            EXPECT_NEAR(cone.Area(), cone2.Area(), 1e-6);

            Shape paraboloid = create("paraboloid", new List<ParsedParameter> { param("float", "radius", new Float[] { 1 }), param("float", "zmin", new Float[] { 0.25f }),
                                                                              param("float", "zmax", new Float[] { 1 }), param("float", "thetamax", new Float[] { 90 }) });
            EXPECT_TRUE(paraboloid is Paraboloid);
            double c2 = 1.0, m = c2 / 4;
            EXPECT_NEAR(0.5 * Math.PI * (2.0 / 3) * (Math.Pow(1 + m, 1.5) - Math.Pow(0.25 + m, 1.5)), paraboloid.Area(), 1e-3);

            Shape hyperboloid = create("hyperboloid", new List<ParsedParameter> { param("point3", "p1", new Float[] { 1.2f, 0, 0 }), param("point3", "p2", new Float[] { 0, 0, 2 }) });
            EXPECT_TRUE(hyperboloid is Hyperboloid);
            EXPECT_NEAR(Math.PI * 1.2 * Math.Sqrt(1.44 + 4), hyperboloid.Area(), 1e-3);

            Shape torus = create("torus", new List<ParsedParameter> { param("float", "majorradius", new Float[] { 2 }), param("float", "minorradius", new Float[] { 0.5f }),
                                                                    param("float", "phimin", new Float[] { 0 }), param("float", "phimax", new Float[] { 360 }),
                                                                    param("float", "thetamax", new Float[] { 90 }) });
            EXPECT_TRUE(torus is Torus);
            EXPECT_NEAR(0.25 * 4 * Math.PI * Math.PI * 2 * 0.5, torus.Area(), 1e-3);
            Shape torus2 = create("torus", new List<ParsedParameter> { param("float", "majorrad", new Float[] { 2 }), param("float", "minorrad", new Float[] { 0.5f }),
                                                                     param("float", "thetamax", new Float[] { 90 }) });
            EXPECT_NEAR(torus.Area(), torus2.Area(), 1e-5);

            // RenderMan's names on the old quadrics
            Shape sphere = create("sphere", new List<ParsedParameter> { param("float", "radius", new Float[] { 1 }), param("float", "thetamax", new Float[] { 90 }) });
            EXPECT_NEAR(Math.PI, sphere.Area(), 1e-4);
            Shape disk = create("disk", new List<ParsedParameter> { param("float", "radius", new Float[] { 1 }), param("float", "thetamax", new Float[] { 90 }) });
            EXPECT_NEAR(Math.PI / 4, disk.Area(), 1e-4);
            Shape cylinder = create("cylinder", new List<ParsedParameter> { param("float", "radius", new Float[] { 1 }), param("float", "thetamax", new Float[] { 90 }) });
            EXPECT_NEAR(Math.PI, cylinder.Area(), 1e-4);
        }
    }
}  // namespace pbrt
