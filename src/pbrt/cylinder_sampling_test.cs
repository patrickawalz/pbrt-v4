// (not in pbrt-v4) cylinder_sampling_test.cs
//
// Tests of the solid angle sampling of the Cylinder shape (shapes_cylinder_sampling.cs): the distribution of the sampled directions against a grid of rays that
// are intersected with the Cylinder itself, the solid angle (the mean of one over the density) against the same grid, the density that Sample and PDF return, and
// the cases that are sampled by area instead.

using System;
using System.Globalization;
using testing;

namespace pbrt
{
    public static unsafe class CylinderSamplingTests
    {
        private static Transform[] Transforms()
        {
            Transform identity = new Transform();
            Transform rigid = Translate(new Vector3f(0.7f, -0.4f, 1.1f)) * Rotate(37, Normalize(new Vector3f(1, 2, -0.5f)));
            Transform uniform = Translate(new Vector3f(-1, 0.5f, 0.2f)) * Rotate(-71, Normalize(new Vector3f(-0.3f, 1, 0.8f))) * Scale(1.7f, 1.7f, 1.7f);
            Transform mirror = Rotate(50, Normalize(new Vector3f(1, 1, 0.2f))) * Scale(1, 1, -1);
            return new Transform[] { identity, rigid, uniform, mirror };
        }

        [TEST("CylinderSampling", "DistributionAndDensityMatchTheCylinder")]
        public static void CylinderSampling_Distribution()
        {
            CheckDistribution("solidangle", 0.0, 31, 0.01, 0.4);
        }

        // The projected solid angle path with the shading normal along the axis of the cylinder, and tilted by 2 degrees (the check still passes: the density is
        // not exactly proportional to the cosine but positive wherever it is)
        [TEST("CylinderSampling", "ProjectedSamplingAlongTheAxis")]
        public static void CylinderSampling_ProjectedAlongAxis()
        {
            CheckDistribution("projected", 0.0, 41, 0.01, 0.4);
        }

        [TEST("CylinderSampling", "ProjectedSamplingWithTiltedNormal")]
        public static void CylinderSampling_ProjectedTilted()
        {
            CheckDistribution("projected", 2.0 * Math.PI / 180, 51, 0.02, 0.7);
        }

        // A normal 20 degrees off the axis fails the check: the plain solid angle sampling is used (the density is about one over the solid angle)
        [TEST("CylinderSampling", "ProjectedSamplingFallsBackForOtherNormals")]
        public static void CylinderSampling_ProjectedFallsBack()
        {
            CheckDistribution("projected", 20.0 * Math.PI / 180, 61, 0.01, 0.7, true);
        }

        private static void CheckDistribution(string sampling, double tiltAngle, int seed, double rmsLimit, double worstLimit, bool expectPlain = false)
        {
            bool weighted = sampling == "projected" && !expectPlain;
            Random random = new Random(seed);
            Transform[] transforms = Transforms();
            double worstZ = 0, worstChi = 0, worstSolidAngle = 0, worstPdf = 0, worstDensityRatio = 0;
            int misses = 0, total = 0;
            double sumSquaredRatio = 0;
            long ratioCount = 0;
            for (int test = 0; test < 16; ++test)
            {
                Transform t = transforms[test % 4], ti = Inverse(t);
                double radius = 0.3 + random.NextDouble() * 0.8;
                double zMin = -0.2 - random.NextDouble() * 1.2, zMax = 0.2 + random.NextDouble() * 1.2;
                Cylinder cylinder = new Cylinder(t, ti, false, (Float)radius, (Float)zMin, (Float)zMax, 360, sampling);

                // The reference point in object space: outside of the infinite cylinder, from near (1.1 R) to far, above, level with or below the cylinder
                // (one in four of the points is inside of the infinite cylinder: above, below or within the tube)
                bool inside = test % 4 == 3;
                double distance = inside ? radius * (0.05 + 0.85 * random.NextDouble()) : radius * (1.1 + random.NextDouble() * (test % 3 == 0 ? 0.5 : 5.0));
                double azimuth = random.NextDouble() * 2 * Math.PI;
                double height = (random.NextDouble() * 2 - 1) * (test % 2 == 0 ? 4.0 : 1.0);
                Point3f pObjectRef = new Point3f((Float)(distance * Math.Cos(azimuth)), (Float)(distance * Math.Sin(azimuth)), (Float)height);
                Point3f p = t.Apply(pObjectRef);
                // The shading normal: along the axis of the cylinder, tilted by tiltAngle toward a perpendicular
                Vector3f axis = Normalize(t.Apply(new Vector3f(0, 0, 1)));
                Vector3f perpendicular = Normalize(Cross(axis, Math.Abs(axis.x) < 0.9 ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0)));
                Vector3f tilted = Normalize(axis * (Float)Math.Cos(tiltAngle) + perpendicular * (Float)Math.Sin(tiltAngle));
                Normal3f ns = new Normal3f(tilted.x, tilted.y, tilted.z);
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p, new Vector3f(0, 0, 0)), ns, ns, 0);

                // The grid of directions in meridian coordinates (phi about the direction to the axis, h the sine of the elevation), intersected with the Cylinder
                double phi0 = inside ? 0.0 : Math.Atan2(-pObjectRef.y, -pObjectRef.x);
                double alpha = inside ? Math.PI : Math.Asin(radius / distance);
                const int gridPhi = 240, gridH = 480, cellsPhi = 6, cellsH = 6;
                double[] expected = new double[cellsPhi * cellsH];
                double gridSolidAngle = 0, gridEstimand = 0;
                // the range of h of the directions that can hit the cylinder (the corners of its extent), so that a thin cylinder far above is resolved
                double hLow = 1, hHigh = -1;
                foreach (double rhoCorner in new double[] { Math.Abs(distance - radius), distance + radius })
                {
                    foreach (double zCorner in new double[] { zMin - height, zMax - height })
                    {
                        double hCorner = zCorner / Math.Sqrt(rhoCorner * rhoCorner + zCorner * zCorner);
                        hLow = Math.Min(hLow, hCorner);
                        hHigh = Math.Max(hHigh, hCorner);
                    }
                }
                hLow = Math.Max(-1.0, hLow - 1e-6);
                hHigh = Math.Min(1.0, hHigh + 1e-6);
                double cellPhi = 2 * alpha / gridPhi, cellH = (hHigh - hLow) / gridH;
                for (int i = 0; i < gridPhi; ++i)
                {
                    double phi = phi0 - alpha + (i + 0.5) * cellPhi;
                    for (int j = 0; j < gridH; ++j)
                    {
                        double h = hLow + (j + 0.5) * cellH;
                        double horizontal = Math.Sqrt(1 - h * h);
                        Vector3f dObject = new Vector3f((Float)(horizontal * Math.Cos(phi)), (Float)(horizontal * Math.Sin(phi)), (Float)h);
                        Ray ray = new Ray(p, Normalize(t.Apply(dObject)));
                        if (cylinder.IntersectP(ray))
                        {
                            // the density that the sampler aims at: |h| + sine of the tilt (the cosine for a normal along the axis)
                            double weight = weighted ? Math.Abs(h) + Math.Sin(tiltAngle) : 1.0;
                            gridSolidAngle += weight * cellPhi * cellH;
                            gridEstimand += (weighted ? Math.Abs(pbrt.Dot(ray.d, tilted)) : 1.0) * cellPhi * cellH;
                            int bi = Math.Min(cellsPhi - 1, i * cellsPhi / gridPhi), bj = Math.Min(cellsH - 1, j * cellsH / gridH);
                            expected[bi * cellsH + bj] += weight;
                        }
                    }
                }
                double expectedTotal = 0;
                for (int c = 0; c < expected.Length; ++c)
                {
                    expectedTotal += expected[c];
                }

                int[] counts = new int[cellsPhi * cellsH];
                const int count = 40000;
                int accepted = 0;
                double sumInverse = 0;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<ShapeSample> ss = cylinder.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    if (!ss.has_value() || ss.value().pdf == 0)
                    {
                        continue;
                    }
                    ++accepted;
                    Vector3f toSample = Normalize(ss.value().intr.p() - p);
                    double estimandWeight = weighted ? Math.Abs(pbrt.Dot(toSample, tilted)) : 1.0;
                    sumInverse += estimandWeight / ss.value().pdf;
                    Point3f q = ti.Apply(ss.value().intr.p());
                    double dx = q.x - pObjectRef.x, dy = q.y - pObjectRef.y, dz = q.z - pObjectRef.z;
                    double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double phi = Math.Atan2(dy, dx) - phi0;
                    phi -= 2 * Math.PI * Math.Floor((phi + Math.PI) / (2 * Math.PI));
                    double h = dz / length;
                    int bi = Math.Min(cellsPhi - 1, Math.Max(0, (int)((phi + alpha) / (2 * alpha) * cellsPhi)));
                    int bj = Math.Min(cellsH - 1, Math.Max(0, (int)((h - hLow) / (hHigh - hLow) * cellsH)));
                    ++counts[bi * cellsH + bj];
                    // on the cylinder
                    EXPECT_LT(Math.Abs(Math.Sqrt(q.x * q.x + q.y * q.y) - radius), 1e-4);
                    EXPECT_LT(Math.Max(zMin - q.z, q.z - zMax), 1e-4);
                    // Sample and PDF agree
                    Vector3f wi = Normalize(ss.value().intr.p() - p);
                    Float pdf = cylinder.PDF(ctx, wi);
                    double mismatch = Math.Abs(pdf - ss.value().pdf) / ss.value().pdf;
                    if (mismatch > 2e-3)
                    {
                        ++misses;
                    }
                    else
                    {
                        worstPdf = Math.Max(worstPdf, mismatch);
                    }
                    ++total;
                    // the density is about one over the solid angle (the table interpolates the integrand)
                    double sampleHeight = (q.z - pObjectRef.z) / length;
                    double sampleWeight = weighted ? Math.Abs(sampleHeight) + Math.Sin(tiltAngle) : 1.0;
                    double ratio = sampleWeight > 1e-3 ? ss.value().pdf * gridSolidAngle / sampleWeight - 1 : 0.0;
                    worstDensityRatio = Math.Max(worstDensityRatio, Math.Abs(ratio));
                    sumSquaredRatio += ratio * ratio;
                    ++ratioCount;
                }
                EXPECT_GT(accepted, count * 99 / 100);
                // the mean of one over the density is the solid angle
                double byDensity = sumInverse / accepted;
                worstSolidAngle = Math.Max(worstSolidAngle, Math.Abs(byDensity - gridEstimand) / gridEstimand);

                double chiSquare = 0;
                int degrees = 0;
                for (int c = 0; c < counts.Length; ++c)
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
            }
            Console.WriteLine("[CylinderSampling] " + sampling + (tiltAngle > 0 ? " tilt " + (tiltAngle * 180 / Math.PI).ToString("F0", CultureInfo.InvariantCulture) + " deg" : "") + ": worst |z| " + worstZ.ToString("F2", CultureInfo.InvariantCulture) + ", worst chi2/dof " +
                              worstChi.ToString("F2", CultureInfo.InvariantCulture) + ", estimand (mean weight/pdf) vs grid " +
                              worstSolidAngle.ToString("E1", CultureInfo.InvariantCulture) + ", |pdf*omega - 1|: rms " + Math.Sqrt(sumSquaredRatio / ratioCount).ToString("E1", CultureInfo.InvariantCulture) + ", worst " + worstDensityRatio.ToString("E1", CultureInfo.InvariantCulture) +
                              ", worst pdf mismatch " + worstPdf.ToString("E1", CultureInfo.InvariantCulture) + ", pdf mismatches " + misses + "/" + total);
            EXPECT_LT(worstZ, 5.0);
            EXPECT_LT(worstChi, 2.0);
            EXPECT_LT(worstSolidAngle, 5e-3);
            EXPECT_LT(Math.Sqrt(sumSquaredRatio / ratioCount), rmsLimit);
            EXPECT_LT(worstDensityRatio, worstLimit);
            EXPECT_LT(worstPdf, 2e-3);
            EXPECT_LT(misses, total / 2000);
        }

        // The irradiance at points of a floor below a horizontal cylinder (the scene of a light close to a surface): the mean of cos / pdf of the samples against a grid of rays
        // that are intersected with the Cylinder
        [TEST("CylinderSampling", "IrradianceBelowAHorizontalCylinder")]
        public static void CylinderSampling_Irradiance()
        {
            Random random = new Random(71);
            Transform t = Rotate(90, new Vector3f(0, 1, 0));
            t = Translate(new Vector3f(0, 0.3f, 0)) * t;
            Transform ti = Inverse(t);
            Cylinder cylinder = new Cylinder(t, ti, false, 0.2f, -0.8f, 0.8f, 360, "solidangle");
            Normal3f floor = new Normal3f(0, 1, 0);
            double worst = 0;
            string worstPoint = "";
            for (int ix = -3; ix <= 3; ++ix)
            {
                for (int iz = -3; iz <= 3; ++iz)
                {
                    Point3f p = new Point3f(ix * 0.5f, 0, iz * 0.5f);
                    ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p, new Vector3f(0, 0, 0)), floor, floor, 0);
                    // grid over the sphere of directions (uniform in the cosine of the polar angle about the floor normal and the azimuth), the upper hemisphere
                    double reference = 0;
                    const int gridMu = 700, gridAz = 1400;
                    for (int i = 0; i < gridMu; ++i)
                    {
                        double mu = (i + 0.5) / gridMu;
                        double sinT = Math.Sqrt(1 - mu * mu);
                        for (int j = 0; j < gridAz; ++j)
                        {
                            double az = 2 * Math.PI * (j + 0.5) / gridAz;
                            Ray ray = new Ray(p, new Vector3f((Float)(sinT * Math.Cos(az)), (Float)mu, (Float)(sinT * Math.Sin(az))));
                            if (cylinder.IntersectP(ray))
                            {
                                reference += mu * (1.0 / gridMu) * (2 * Math.PI / gridAz);
                            }
                        }
                    }
                    double sum = 0;
                    const int count = 100000;
                    for (int i = 0; i < count; ++i)
                    {
                        pstd.optional<ShapeSample> ss = cylinder.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                        if (ss.has_value() && ss.value().pdf > 0)
                        {
                            Vector3f wi = Normalize(ss.value().intr.p() - p);
                            sum += Math.Max(0.0, wi.y) / ss.value().pdf;
                        }
                    }
                    double estimate = sum / count;
                    double error = Math.Abs(estimate - reference) / reference;
                    if (error > worst)
                    {
                        worst = error;
                        worstPoint = "(" + p.x + ", " + p.z + ") estimate " + estimate + " reference " + reference;
                    }
                }
            }
            Console.WriteLine("[CylinderSampling] irradiance below a horizontal cylinder: worst relative error " + worst.ToString("E2", CultureInfo.InvariantCulture) + " at " + worstPoint);
            EXPECT_LT(worst, 0.01);
        }

        // Points close to the wall of the infinite cylinder, on both sides of it (the footprint of a vertical tube on a floor, where the pool of light has its edge)
        [TEST("CylinderSampling", "IrradianceNearTheWallOfTheInfiniteCylinder")]
        public static void CylinderSampling_NearWall()
        {
            Random random = new Random(81);
            Transform id = new Transform();
            Cylinder cylinder = new Cylinder(id, id, false, 0.25f, 0.3f, 2.1f, 360, "solidangle");
            Normal3f floor = new Normal3f(0, 0, 1);
            double worst = 0;
            string worstDescription = "";
            foreach (double relative in new double[] { 0.99, 0.998, 0.9995, 0.9999, 0.99999, 1.00001, 1.0001, 1.0005, 1.002, 1.01 })
            {
                Point3f p = new Point3f((Float)(0.25 * relative), 0, 0);
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p, new Vector3f(0, 0, 0)), floor, floor, 0);
                // grid over the upper hemisphere, uniform in the cosine and the azimuth: fine enough for the thin wedge that the wall subtends
                double reference = 0;
                const int gridMu = 1600, gridAz = 3200;
                for (int i = 0; i < gridMu; ++i)
                {
                    double mu = (i + 0.5) / gridMu;
                    double sinT = Math.Sqrt(1 - mu * mu);
                    for (int j = 0; j < gridAz; ++j)
                    {
                        double az = 2 * Math.PI * (j + 0.5) / gridAz;
                        Ray ray = new Ray(p, new Vector3f((Float)(sinT * Math.Cos(az)), (Float)(sinT * Math.Sin(az)), (Float)mu));
                        if (cylinder.IntersectP(ray))
                        {
                            reference += mu * (1.0 / gridMu) * (2 * Math.PI / gridAz);
                        }
                    }
                }
                double sum = 0, sumSquares = 0;
                int fallbackSamples = 0;
                const int count = 200000;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<ShapeSample> ss = cylinder.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    double value = 0;
                    if (ss.has_value() && ss.value().pdf > 0)
                    {
                        Vector3f wi = Normalize(ss.value().intr.p() - p);
                        value = Math.Max(0.0, wi.z) / ss.value().pdf;
                    }
                    sum += value;
                    sumSquares += value * value;
                }
                double estimate = sum / count;
                double standardError = Math.Sqrt(Math.Max(0, sumSquares / count - estimate * estimate) / count);
                double error = (estimate - reference) / reference;
                Console.WriteLine("[CylinderSampling] near the wall, |o|/R " + relative.ToString("F4", CultureInfo.InvariantCulture) + ": irradiance estimate " +
                                  estimate.ToString("F5", CultureInfo.InvariantCulture) + " reference " + reference.ToString("F5", CultureInfo.InvariantCulture) + " error " +
                                  error.ToString("P2", CultureInfo.InvariantCulture) + " (standard error " + (standardError / reference).ToString("P2", CultureInfo.InvariantCulture) + ", relative std per sample " +
                                  (standardError * Math.Sqrt(count) / estimate).ToString("F3", CultureInfo.InvariantCulture) + ")");
                // unbiased (to 4 standard errors) and as quiet as away from the wall: the points next to the wall are not sampled by area
                EXPECT_LT(Math.Abs(estimate - reference), 4 * standardError + 1e-3 * reference);
                EXPECT_LT(standardError * Math.Sqrt(count) / estimate, 0.3);
                if (Math.Abs(error) > worst)
                {
                    worst = Math.Abs(error);
                    worstDescription = relative.ToString("F4", CultureInfo.InvariantCulture);
                }
            }
            Console.WriteLine("[CylinderSampling] near the wall: worst error " + worst.ToString("P2", CultureInfo.InvariantCulture) + " at |o|/R " + worstDescription);
        }

        [TEST("CylinderSampling", "FallbacksToAreaSampling")]
        public static void CylinderSampling_Fallbacks()
        {
            Transform id = new Transform();
            Transform squash = Scale(1.5f, 0.7f, 1.1f);
            Point3f inside = new Point3f(0.2f, 0.1f, 0.3f);
            Random random = new Random(7);
            Cylinder[] cylinders = { new Cylinder(id, id, false, 1, -1, 1, 360, "area"), new Cylinder(id, id, false, 1, -1, 1, 200, "solidangle"),
                                     new Cylinder(squash, Inverse(squash), false, 1, -1, 1, 360, "solidangle") };
            Point3f outside = new Point3f(3, 0.5f, 0.4f);
            foreach (Cylinder cylinder in cylinders)
            {
                Normal3f ns = new Normal3f(0, 0, 1);
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(outside, new Vector3f(0, 0, 0)), ns, ns, 0);
                int tested = 0;
                for (int i = 0; i < 200; ++i)
                {
                    pstd.optional<ShapeSample> ss = cylinder.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                    if (ss.has_value() && ss.value().pdf > 0)
                    {
                        ++tested;
                    }
                }
                EXPECT_GT(tested, 100);
            }
            // a point inside of the cylinder is sampled by area, and still works
            Cylinder whole = new Cylinder(id, id, false, 1, -1, 1, 360);
            ShapeSampleContext insideCtx = new ShapeSampleContext(new Point3fi(inside, new Vector3f(0, 0, 0)), new Normal3f(0, 0, 1), new Normal3f(0, 0, 1), 0);
            int sampled = 0;
            for (int i = 0; i < 50; ++i)
            {
                if (whole.Sample(insideCtx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble())).has_value())
                {
                    ++sampled;
                }
            }
            EXPECT_GT(sampled, 45);
        }
    }
}
