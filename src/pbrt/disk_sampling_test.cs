// (not in pbrt-v4) disk_sampling_test.cs
//
// Tests of the solid angle sampling of the Disk shape (shapes_disk_sampling.cs, after Guillen et al. 2017): the elliptic integrals against numerical integration,
// the solid angle of the disk (from the elliptic integrals) against two independent quadratures, the semi-axes of the spherical ellipse, the distribution of the
// samples of the polar, the concentric and the cylindrical mapping against the integral of the cosine over the squared distance, the density that Sample and PDF
// return, the iterations of the inversion, and the cases that are sampled by area instead.

using System;
using System.Globalization;
using testing;

namespace pbrt
{
    public static unsafe class DiskSamplingTests
    {
        private static void GaussLegendre(int count, out double[] nodes, out double[] weights)
        {
            nodes = new double[count];
            weights = new double[count];
            for (int i = 0; i < count; ++i)
            {
                double x = Math.Cos(Math.PI * (i + 0.75) / (count + 0.5));
                double derivative = 1;
                for (int iteration = 0; iteration < 100; ++iteration)
                {
                    double p0 = 1, p1 = x;
                    for (int j = 2; j <= count; ++j)
                    {
                        double p2 = ((2 * j - 1) * x * p1 - (j - 1) * p0) / j;
                        p0 = p1;
                        p1 = p2;
                    }
                    derivative = count * (x * p1 - p0) / (x * x - 1);
                    double step = p1 / derivative;
                    x -= step;
                    if (Math.Abs(step) < 1e-15)
                    {
                        break;
                    }
                }
                nodes[i] = x;
                weights[i] = 2 / ((1 - x * x) * derivative * derivative);
            }
        }

        // The solid angle of the ellipse from its semi-axes, without elliptic integrals: 4 a b integral_0^{pi/2} dpsi / (1 + h(psi)), h the height of the
        // boundary point at the parametric angle psi
        private static double EllipseSolidAngleByQuadrature(double a, double b)
        {
            double[] nodes, weights;
            GaussLegendre(16, out nodes, out weights);
            // panels: the integrand has a near singularity at psi = 0 when a is close to 1 (the disk reaches the horizon)
            const int panels = 128;
            double sum = 0;
            for (int panel = 0; panel < panels; ++panel)
            {
                double lo = 0.5 * Math.PI * panel / panels, hi = 0.5 * Math.PI * (panel + 1) / panels;
                for (int i = 0; i < nodes.Length; ++i)
                {
                    double psi = 0.5 * (lo + hi) + 0.5 * (hi - lo) * nodes[i];
                    double h = Math.Sqrt(Math.Max(0, 1 - a * a * Math.Cos(psi) * Math.Cos(psi) - b * b * Math.Sin(psi) * Math.Sin(psi)));
                    sum += weights[i] * 0.5 * (hi - lo) / (1 + h);
                }
            }
            return 4 * a * b * sum;
        }

        // The solid angle of the disk of radius radius, whose center is at the distance depth from the point below the plane of the point and with the offset
        // lateral in the plane, as the integral of d / dist^3 over the disk (polar around the center)
        private static double DiskSolidAngleByQuadrature(double radius, double depth, double lateral)
        {
            double[] nodes, weights;
            GaussLegendre(96, out nodes, out weights);
            const int azimuths = 512;
            double sum = 0;
            for (int i = 0; i < nodes.Length; ++i)
            {
                double rho = 0.5 * radius * (1 + nodes[i]);
                double ring = 0;
                for (int j = 0; j < azimuths; ++j)
                {
                    double phi = 2 * Math.PI * (j + 0.5) / azimuths;
                    double x = rho * Math.Cos(phi) - lateral, y = rho * Math.Sin(phi);
                    double dist2 = x * x + y * y + depth * depth;
                    ring += depth / (dist2 * Math.Sqrt(dist2));
                }
                sum += weights[i] * 0.5 * radius * rho * ring * 2 * Math.PI / azimuths;
            }
            return sum;
        }

        private static Vector3f RandomUnit(Random random)
        {
            while (true)
            {
                double x = random.NextDouble() * 2 - 1, y = random.NextDouble() * 2 - 1, z = random.NextDouble() * 2 - 1;
                double length2 = x * x + y * y + z * z;
                if (length2 > 0.01 && length2 < 1)
                {
                    double invLength = 1 / Math.Sqrt(length2);
                    return new Vector3f((Float)(x * invLength), (Float)(y * invLength), (Float)(z * invLength));
                }
            }
        }

        [TEST("DiskSampling", "EllipticIntegralsMatchNumericalIntegration")]
        public static void DiskSampling_EllipticIntegrals()
        {
            Random random = new Random(11);
            double worstF = 0, worstPi = 0;
            for (int test = 0; test < 400; ++test)
            {
                double k = random.NextDouble() * 0.999;
                double phi = (random.NextDouble() * 2 - 1) * 0.5 * Math.PI;
                double sinMax2 = Math.Sin(phi) * Math.Sin(phi);
                // n below 1 / sin^2(phi), negative too (the cylindrical mapping has n < 0)
                double n = test % 2 == 0 ? -random.NextDouble() * 30 : random.NextDouble() * 0.9 / Math.Max(sinMax2, 1e-9);
                int steps = 20000;
                double h = phi / steps;
                double sumF = 0, sumPi = 0;
                for (int i = 0; i <= steps; ++i)
                {
                    double theta = i * h;
                    double s2 = Math.Sin(theta) * Math.Sin(theta);
                    double rootTerm = 1 / Math.Sqrt(1 - k * k * s2);
                    double weight = (i == 0 || i == steps) ? 1 : (i % 2 == 1 ? 4 : 2);
                    sumF += weight * rootTerm;
                    sumPi += weight * rootTerm / (1 - n * s2);
                }
                double referenceF = sumF * h / 3, referencePi = sumPi * h / 3;
                worstF = Math.Max(worstF, Math.Abs(CarlsonElliptic.LegendreF(phi, k) - referenceF) / Math.Max(Math.Abs(referenceF), 1e-3));
                worstPi = Math.Max(worstPi, Math.Abs(CarlsonElliptic.LegendrePi(phi, n, k) - referencePi) / Math.Max(Math.Abs(referencePi), 1e-3));
            }
            Console.WriteLine("[DiskSampling] elliptic integrals: worst relative error F " + worstF.ToString("E1", CultureInfo.InvariantCulture) + ", Pi " +
                              worstPi.ToString("E1", CultureInfo.InvariantCulture));
            EXPECT_LT(worstF, 1e-10);
            EXPECT_LT(worstPi, 1e-9);
        }

        // Random configurations (the radius is 1): the point at the distance depth from the plane of the disk and the lateral offset from the axis, including
        // grazing and nearly in-plane points, and points on the axis
        private static void RandomConfiguration(Random random, int index, out double depth, out double lateral)
        {
            double scale = Math.Pow(10, random.NextDouble() * 3 - 1.2);
            depth = scale * (0.02 + random.NextDouble());
            lateral = scale * random.NextDouble() * 3;
            if (index % 7 == 0)
            {
                lateral = 0;
            }
            if (index % 5 == 0)
            {
                depth *= 0.01;
            }
        }

        [TEST("DiskSampling", "SolidAngleMatchesQuadratures")]
        public static void DiskSampling_SolidAngleMatchesQuadratures()
        {
            Random random = new Random(21);
            DiskSolidAngleMapping[] mappings = { DiskSolidAngleMapping.Polar, DiskSolidAngleMapping.Concentric, DiskSolidAngleMapping.Cylindrical };
            double worstEllipse = 0, worstPlanar = 0, worstAxes = 0, worstBooth = 0;
            int ordered = 0, prepared = 0;
            for (int test = 0; test < 3000; ++test)
            {
                double depth, lateral;
                RandomConfiguration(random, test, out depth, out lateral);
                // the point at (lateral, 0, depth) over a disk at the origin of radius 1 with the normal +z
                DiskSolidAngleMapping mapping = mappings[test % 3];
                DiskSolidAngleSampler sampler = default(DiskSolidAngleSampler);
                bool ok = sampler.Prepare(mapping, lateral, 0, depth, 0, 0, 0, 0, 0, 1, 1);
                double reference = DiskSolidAngleByQuadrature(1, depth, lateral);
                if (!ok)
                {
                    // only a vanishing disk may be refused: in the data a solid angle below 1e-5
                    EXPECT_LT(reference, 2e-5);
                    continue;
                }
                ++prepared;
                if (sampler.MajorAxis >= sampler.MinorAxis)
                {
                    ++ordered;
                }
                double byEllipse = EllipseSolidAngleByQuadrature(sampler.MajorAxis, sampler.MinorAxis);
                double ellipseError = Math.Abs(sampler.SolidAngle - byEllipse) / byEllipse;
                if (sampler.SolidAngle > 0.05)
                {
                    // Booth's closed form agrees where it is well conditioned
                    double byBooth = 4 * sampler.QuadrantSolidAngleBooth(0.5 * Math.PI);
                    if (mapping != DiskSolidAngleMapping.Cylindrical)
                    {
                        worstBooth = Math.Max(worstBooth, Math.Abs(byBooth - sampler.SolidAngle) / sampler.SolidAngle);
                    }
                }
                worstEllipse = Math.Max(worstEllipse, ellipseError);
                if (reference > 1e-3)
                {
                    // the planar quadrature loses accuracy for a disk that is very close to the point, which it resolves poorly
                    if (depth > 0.05 || lateral > 1.5)
                    {
                        worstPlanar = Math.Max(worstPlanar, Math.Abs(sampler.SolidAngle - reference) / reference);
                    }
                }
                // the axes against the closed form for the point on the axis: a = b = sin(half angle) = 1 / sqrt(1 + depth^2)
                if (lateral == 0)
                {
                    worstAxes = Math.Max(worstAxes, Math.Abs(sampler.MajorAxis - 1 / Math.Sqrt(1 + depth * depth)));
                    worstAxes = Math.Max(worstAxes, Math.Abs(sampler.MinorAxis - sampler.MajorAxis));
                }
            }
            Console.WriteLine("[DiskSampling] solid angle: prepared " + prepared + ", a >= b in " + ordered + ", worst relative error against the ellipse quadrature " +
                              worstEllipse.ToString("E1", CultureInfo.InvariantCulture) + ", against the planar quadrature " +
                              worstPlanar.ToString("E1", CultureInfo.InvariantCulture) + ", Booth closed form (where well conditioned) " + worstBooth.ToString("E1", CultureInfo.InvariantCulture) + ", axes on the axis " + worstAxes.ToString("E1", CultureInfo.InvariantCulture));
            EXPECT_EQ(ordered, prepared);
            EXPECT_GT(prepared, 2900);
            EXPECT_LT(worstEllipse, 1e-8);
            EXPECT_LT(worstPlanar, 2e-6);
            EXPECT_LT(worstAxes, 1e-12);
            EXPECT_LT(worstBooth, 1e-9);
        }

        private static Transform[] Transforms()
        {
            Transform rigid = Translate(new Vector3f(0.7f, -0.4f, 1.1f)) * Rotate(37, Normalize(new Vector3f(1, 2, -0.5f)));
            Transform uniform = Translate(new Vector3f(-1, 0.5f, 0.2f)) * Rotate(-71, Normalize(new Vector3f(-0.3f, 1, 0.8f))) * Scale(1.7f, 1.7f, 1.7f);
            Transform mirror = Rotate(50, Normalize(new Vector3f(1, 1, 0.2f))) * Scale(1, 1, -1);
            return new Transform[] { rigid, uniform, mirror };
        }

        private static void CheckDistribution(string sampling, int seed)
        {
            Random random = new Random(seed);
            Transform[] transforms = Transforms();
            double worstChi = 0, worstZ = 0, worstSolidAngle = 0, worstPdf = 0;
            int maxIterations = 0, misses = 0, total = 0, slow = 0, iterationSamples = 0;
            long iterationSum = 0;
            for (int test = 0; test < 9; ++test)
            {
                Transform t = transforms[test % 3], ti = Inverse(t);
                double radius = 0.5 + random.NextDouble();
                Float height = 0.25f;
                Disk disk = new Disk(t, ti, false, height, (Float)radius, 0, 360, sampling);
                double depth, lateral;
                RandomConfiguration(random, test * 3 + 1, out depth, out lateral);
                depth = Math.Max(depth, 0.1);
                // the reference point in object space, on either side of the disk, then in render space
                double azimuth = random.NextDouble() * 2 * Math.PI;
                double side = test % 2 == 0 ? 1 : -1;
                Point3f pObjectRef = new Point3f((Float)(lateral * Math.Cos(azimuth)), (Float)(lateral * Math.Sin(azimuth)), (Float)(height + side * depth));
                Point3f p = t.Apply(pObjectRef);
                Vector3f nrm = RandomUnit(random);
                Normal3f ns = new Normal3f(nrm.x, nrm.y, nrm.z);
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p, new Vector3f(0, 0, 0)), ns, ns, 0);

                DiskSolidAngleSampler sampler;
                EXPECT_TRUE(disk.PrepareSolidAngleSampler(p, out sampler));
                double omega = DiskSolidAngleByQuadrature(radius, depth, lateral);

                const int cells = 6;
                double[] expected = new double[cells * cells];
                const int sub = 40;
                for (int cu = 0; cu < cells; ++cu)
                {
                    for (int cv = 0; cv < cells; ++cv)
                    {
                        double sum = 0;
                        for (int i = 0; i < sub; ++i)
                        {
                            for (int j = 0; j < sub; ++j)
                            {
                                double x = -radius + 2 * radius * (cu + (i + 0.5) / sub) / cells;
                                double y = -radius + 2 * radius * (cv + (j + 0.5) / sub) / cells;
                                if (x * x + y * y > radius * radius)
                                {
                                    continue;
                                }
                                double dx = x - lateral * Math.Cos(azimuth), dy = y - lateral * Math.Sin(azimuth);
                                double dist2 = dx * dx + dy * dy + depth * depth;
                                sum += depth / (dist2 * Math.Sqrt(dist2));
                            }
                        }
                        expected[cu * cells + cv] = sum;
                    }
                }
                double expectedTotal = 0;
                for (int c = 0; c < expected.Length; ++c)
                {
                    expectedTotal += expected[c];
                }

                int[] counts = new int[cells * cells];
                const int count = 40000;
                int accepted = 0;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<ShapeSample> ss = disk.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    if (!ss.has_value() || ss.value().pdf == 0)
                    {
                        continue;
                    }
                    ++accepted;
                    Point3f q = ti.Apply(ss.value().intr.p());
                    int cu = Math.Min(cells - 1, Math.Max(0, (int)((q.x + radius) / (2 * radius) * cells)));
                    int cv = Math.Min(cells - 1, Math.Max(0, (int)((q.y + radius) / (2 * radius) * cells)));
                    ++counts[cu * cells + cv];
                    // inside the disk and on its plane
                    EXPECT_LT(Math.Abs(q.z - height), 1e-4);
                    EXPECT_LT(q.x * q.x + q.y * q.y, radius * radius * (1 + 1e-4));
                    // the density is one over the solid angle, and PDF agrees
                    worstSolidAngle = Math.Max(worstSolidAngle, Math.Abs(1 / (ss.value().pdf * omega) - 1));
                    Vector3f wi = Normalize(ss.value().intr.p() - p);
                    Float pdf = disk.PDF(ctx, wi);
                    double mismatch = Math.Abs(pdf - ss.value().pdf) / ss.value().pdf;
                    if (mismatch > 1e-3)
                    {
                        ++misses;
                    }
                    else
                    {
                        worstPdf = Math.Max(worstPdf, mismatch);
                    }
                    ++total;
                }
                EXPECT_GT(accepted, count * 99 / 100);

                double chiSquare = 0;
                int degrees = 0;
                for (int c = 0; c < cells * cells; ++c)
                {
                    double probability = expected[c] / expectedTotal;
                    double mean = accepted * probability;
                    double sigma = Math.Sqrt(Math.Max(accepted * probability * (1 - probability), 1.0));
                    double z = (counts[c] - mean) / sigma;
                    worstZ = Math.Max(worstZ, Math.Abs(z));
                    if (mean >= 5)
                    {
                        chiSquare += z * z;
                        ++degrees;
                    }
                }
                worstChi = Math.Max(worstChi, chiSquare / Math.Max(degrees, 1));

                // the iterations of the inversion
                for (int i = 0; i < 2000; ++i)
                {
                    double x, y, z;
                    int iterations;
                    sampler.Sample(random.NextDouble(), random.NextDouble(), out x, out y, out z, out iterations);
                    maxIterations = Math.Max(maxIterations, iterations);
                    iterationSum += iterations;
                    ++iterationSamples;
                    if (iterations > 12)
                    {
                        ++slow;
                    }
                }
            }
            Console.WriteLine("[DiskSampling] " + sampling + ": worst |z| " + worstZ.ToString("F2", CultureInfo.InvariantCulture) + ", worst chi2/dof " +
                              worstChi.ToString("F2", CultureInfo.InvariantCulture) + ", worst |1/(pdf*omega) - 1| " +
                              worstSolidAngle.ToString("E1", CultureInfo.InvariantCulture) + ", worst pdf mismatch " + worstPdf.ToString("E1", CultureInfo.InvariantCulture) +
                              ", pdf mismatches " + misses + "/" + total + ", iterations: mean " + ((double)iterationSum / iterationSamples).ToString("F2", CultureInfo.InvariantCulture) + ", most " + maxIterations + ", above 12: " + slow + "/" + iterationSamples);
            EXPECT_LT(worstZ, 5.0);
            EXPECT_LT(worstChi, 2.0);
            EXPECT_LT(worstSolidAngle, 2e-3);
            EXPECT_LT(worstPdf, 1e-3);
            EXPECT_LT(misses, total / 2000);
            EXPECT_LT((double)iterationSum / iterationSamples, 8.0);
            EXPECT_LT(slow, iterationSamples / 200);
        }

        [TEST("DiskSampling", "PolarDistribution")]
        public static void DiskSampling_PolarDistribution()
        {
            CheckDistribution("polar", 1);
        }

        [TEST("DiskSampling", "ConcentricDistribution")]
        public static void DiskSampling_ConcentricDistribution()
        {
            CheckDistribution("concentric", 2);
        }

        [TEST("DiskSampling", "CylindricalDistribution")]
        public static void DiskSampling_CylindricalDistribution()
        {
            CheckDistribution("cylindrical", 3);
        }

        [TEST("DiskSampling", "FallbacksToAreaSampling")]
        public static void DiskSampling_Fallbacks()
        {
            Transform id = new Transform();
            Point3f above = new Point3f(0.3f, 0.2f, 1.1f);
            Point3f inPlane = new Point3f(2.0f, 0.1f, 0.0f);
            DiskSolidAngleSampler sampler;
            EXPECT_TRUE(new Disk(id, id, false, 0, 1, 0, 360, "polar").PrepareSolidAngleSampler(above, out sampler));
            EXPECT_FALSE(new Disk(id, id, false, 0, 1, 0, 360, "area").PrepareSolidAngleSampler(above, out sampler));
            EXPECT_FALSE(new Disk(id, id, false, 0, 1, 0.3f, 360, "polar").PrepareSolidAngleSampler(above, out sampler));
            EXPECT_FALSE(new Disk(id, id, false, 0, 1, 0, 200, "polar").PrepareSolidAngleSampler(above, out sampler));
            EXPECT_FALSE(new Disk(id, id, false, 0, 1, 0, 360, "polar").PrepareSolidAngleSampler(inPlane, out sampler));
            Transform squash = Scale(1.5f, 0.7f, 1.1f);
            EXPECT_FALSE(new Disk(squash, Inverse(squash), false, 0, 1, 0, 360, "polar").PrepareSolidAngleSampler(above, out sampler));

            // the fallbacks still sample, and PDF agrees with Sample
            Random random = new Random(5);
            Disk[] disks = { new Disk(id, id, false, 0, 1, 0.3f, 360, "polar"), new Disk(id, id, false, 0, 1, 0, 360, "area"),
                             new Disk(squash, Inverse(squash), false, 0, 1, 0, 360, "polar") };
            foreach (Disk disk in disks)
            {
                Normal3f ns = new Normal3f(0, 0, 1);
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(above, new Vector3f(0, 0, 0)), ns, ns, 0);
                int agreeing = 0, tested = 0;
                for (int i = 0; i < 200; ++i)
                {
                    pstd.optional<ShapeSample> ss = disk.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    if (!ss.has_value())
                    {
                        continue;
                    }
                    ++tested;
                    Float pdf = disk.PDF(ctx, Normalize(ss.value().intr.p() - above));
                    if (Math.Abs(pdf - ss.value().pdf) <= 1e-3 * ss.value().pdf)
                    {
                        ++agreeing;
                    }
                }
                EXPECT_GT(tested, 190);
                EXPECT_GT(agreeing, tested - 3);
            }
        }
    }
}
