// (not in pbrt-v4) quadrics_benchmark_test.cs
//
// Throughput and accuracy of the sphere, disk and cylinder against the versions before the QuadricBase refactor (quadrics_baseline_test.cs).
// Runs only when the environment variable QUADRIC_BENCH is set.

using System;
using System.Diagnostics;
using testing;

namespace pbrt
{
    public static unsafe class QuadricBenchmarkTests
    {
        private sealed class Pair
        {
            public string name;
            public Shape current, baseline;
            public Func<Ray, double> exactT;   // the double precision t of the nearest hit, or -1
        }

        private static double Solve(double ox, double oy, double oz, double dx, double dy, double dz, double r, double zMin, double zMax,
                                    double phiMax, bool sphere)
        {
            double a, b, c;
            if (sphere)
            {
                a = dx * dx + dy * dy + dz * dz;
                b = 2 * (ox * dx + oy * dy + oz * dz);
                c = ox * ox + oy * oy + oz * oz - r * r;
            }
            else
            {
                a = dx * dx + dy * dy;
                b = 2 * (ox * dx + oy * dy);
                c = ox * ox + oy * oy - r * r;
            }
            double disc = b * b - 4 * a * c;
            if (!(a > 0) || disc < 0)
            {
                return -1;
            }
            double q = -0.5 * (b + Math.Sign(b == 0 ? 1 : b) * Math.Sqrt(disc));
            double t0 = q / a, t1 = c / q;
            if (t0 > t1)
            {
                double tmp = t0;
                t0 = t1;
                t1 = tmp;
            }
            foreach (double t in new double[] { t0, t1 })
            {
                if (t <= 0)
                {
                    continue;
                }
                double x = ox + t * dx, y = oy + t * dy, z = oz + t * dz;
                double phi = Math.Atan2(y, x);
                if (phi < 0)
                {
                    phi += 2 * Math.PI;
                }
                if (z >= zMin && z <= zMax && phi <= phiMax)
                {
                    return t;
                }
            }
            return -1;
        }

        [TEST("QuadricBench", "CurrentVersusBaseline")]
        public static void CurrentVersusBaseline()
        {
            if (Environment.GetEnvironmentVariable("QUADRIC_BENCH") == null)
            {
                return;
            }
            Transform id = new Transform();
            Transform rigid = Translate(new Vector3f(0.7f, -0.4f, 1.1f)) * Rotate(37, Normalize(new Vector3f(1, 2, -0.5f)));
            Transform rigidInverse = Inverse(rigid);
            double twoPi = 2 * Math.PI;
            Pair[] pairs =
            {
                new Pair { name = "sphere", current = new Sphere(id, id, false, 1.3f, -1.3f, 1.3f, 360), baseline = new BaselineSphere(id, id, false, 1.3f, -1.3f, 1.3f, 360),
                           exactT = (Ray r) => Solve(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, 1.3f, -1.3, 1.3, twoPi, true) },
                new Pair { name = "partial sphere", current = new Sphere(id, id, false, 1.3f, -0.5f, 0.9f, 250), baseline = new BaselineSphere(id, id, false, 1.3f, -0.5f, 0.9f, 250),
                           exactT = (Ray r) => Solve(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, 1.3f, -0.5f, 0.9f, 250 * Math.PI / 180, true) },
                new Pair { name = "cylinder", current = new Cylinder(id, id, false, 1, -0.8f, 1.2f, 360), baseline = new BaselineCylinder(id, id, false, 1, -0.8f, 1.2f, 360),
                           exactT = (Ray r) => Solve(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, 1, -0.8f, 1.2f, twoPi, false) },
                new Pair { name = "partial cylinder", current = new Cylinder(id, id, false, 1, -0.5f, 1, 270), baseline = new BaselineCylinder(id, id, false, 1, -0.5f, 1, 270),
                           exactT = (Ray r) => Solve(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, 1, -0.5f, 1, 270 * Math.PI / 180, false) },
                new Pair { name = "annulus", current = new Disk(id, id, false, -0.3f, 1.5f, 0.5f, 200), baseline = new BaselineDisk(id, id, false, -0.3f, 1.5f, 0.5f, 200),
                           exactT = null },
                new Pair { name = "rigid sphere", current = new Sphere(rigid, rigidInverse, false, 1.3f, -1.3f, 1.3f, 360), baseline = new BaselineSphere(rigid, rigidInverse, false, 1.3f, -1.3f, 1.3f, 360),
                           exactT = null },
                new Pair { name = "rigid partial cylinder", current = new Cylinder(rigid, rigidInverse, false, 1, -0.5f, 1, 270), baseline = new BaselineCylinder(rigid, rigidInverse, false, 1, -0.5f, 1, 270),
                           exactT = null },
            };

            const int nRays = 1000000;
            foreach (Pair pair in pairs)
            {
                Bounds3f bounds = pair.baseline.Bounds();
                Point3f center = new Point3f((bounds.pMin.x + bounds.pMax.x) / 2, (bounds.pMin.y + bounds.pMax.y) / 2, (bounds.pMin.z + bounds.pMax.z) / 2);
                Float radius = Length(bounds.pMax - bounds.pMin) / 2;
                Random rng = new Random(1);
                Ray[] rays = new Ray[nRays];
                for (int i = 0; i < nRays; ++i)
                {
                    Vector3f dir = SampleUniformSphere(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                    Point3f o = center + (Float)(1.5 + 2 * rng.NextDouble()) * radius * dir;
                    Point3f target = new Point3f(Lerp((Float)rng.NextDouble(), bounds.pMin.x, bounds.pMax.x), Lerp((Float)rng.NextDouble(), bounds.pMin.y, bounds.pMax.y),
                                                 Lerp((Float)rng.NextDouble(), bounds.pMin.z, bounds.pMax.z));
                    rays[i] = new Ray(o, Normalize(target - o));
                }

                double[] times = new double[2];
                int[] hits = new int[2];
                double[] maxError = new double[2], sumError = new double[2];
                int[] compared = new int[2];
                Shape[] shapes = { pair.baseline, pair.current };
                for (int version = 0; version < 2; ++version)
                {
                    Shape shape = shapes[version];
                    for (int i = 0; i < 20000; ++i)
                    {
                        shape.IntersectP(rays[i]);
                    }
                    Stopwatch watch = Stopwatch.StartNew();
                    int count = 0;
                    for (int i = 0; i < nRays; ++i)
                    {
                        if (shape.IntersectP(rays[i]))
                        {
                            ++count;
                        }
                    }
                    watch.Stop();
                    times[version] = watch.Elapsed.TotalMilliseconds;
                    hits[version] = count;
                }
                double[] fullTimes = new double[2];
                for (int version = 0; version < 2; ++version)
                {
                    Shape shape = shapes[version];
                    Stopwatch watch = Stopwatch.StartNew();
                    for (int i = 0; i < nRays / 4; ++i)
                    {
                        shape.Intersect(rays[i]);
                    }
                    watch.Stop();
                    fullTimes[version] = watch.Elapsed.TotalMilliseconds * 4;
                    if (pair.exactT != null)
                    {
                        for (int i = 0; i < nRays; i += 3)
                        {
                            double exact = pair.exactT(rays[i]);
                            pstd.optional<ShapeIntersection> si = shape.Intersect(rays[i]);
                            if (exact < 0 || !si.has_value())
                            {
                                continue;
                            }
                            double error = Math.Abs(si.value().tHit - exact) / exact;
                            maxError[version] = Math.Max(maxError[version], error);
                            sumError[version] += error;
                            ++compared[version];
                        }
                    }
                }
                // Sampling throughput
                double[] sampleTimes = new double[2], pdfTimes = new double[2];
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(center + 3 * radius * new Vector3f(0.3f, 0.5f, 0.8f)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
                for (int version = 0; version < 2; ++version)
                {
                    Shape shape = shapes[version];
                    Random sr = new Random(2);
                    Stopwatch watch = Stopwatch.StartNew();
                    double sink = 0;
                    for (int i = 0; i < nRays / 2; ++i)
                    {
                        pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)sr.NextDouble(), (Float)sr.NextDouble()));
                        if (ss.has_value())
                        {
                            sink += ss.value().pdf;
                        }
                    }
                    watch.Stop();
                    sampleTimes[version] = watch.Elapsed.TotalMilliseconds * 2;
                    watch = Stopwatch.StartNew();
                    for (int i = 0; i < nRays / 2; ++i)
                    {
                        sink += shape.PDF(ctx, rays[i].d);
                    }
                    watch.Stop();
                    pdfTimes[version] = watch.Elapsed.TotalMilliseconds * 2;
                    if (sink == -1)
                    {
                        Console.WriteLine();
                    }
                }
                Console.WriteLine(pair.name + ": per 1M rays IntersectP baseline " + times[0].ToString("F0") + " ms current " + times[1].ToString("F0") +
                                  " ms; Intersect baseline " + fullTimes[0].ToString("F0") + " current " + fullTimes[1].ToString("F0") +
                                  "; Sample(ctx) baseline " + sampleTimes[0].ToString("F0") + " current " + sampleTimes[1].ToString("F0") +
                                  "; PDF(ctx) baseline " + pdfTimes[0].ToString("F0") + " current " + pdfTimes[1].ToString("F0") +
                                  "; hits " + hits[0] + "/" + hits[1] + "; relative t error (max/mean over " + compared[0] + "/" + compared[1] + ") baseline " +
                                  maxError[0].ToString("E2") + "/" + (sumError[0] / Math.Max(1, compared[0])).ToString("E2") + " current " +
                                  maxError[1].ToString("E2") + "/" + (sumError[1] / Math.Max(1, compared[1])).ToString("E2"));
            }
        }
    }
}
