// (not in pbrt-v4) refractivemedium_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class RefractiveMediumTests
    {
        // An absorbing medium whose coefficient varies with the position
        private class VaryingMedium : Medium
        {
            public static Float Sigma(Point3f p)
            {
                return 0.6f + 0.4f * (Float)Math.Sin(3 * p.x + 2 * p.z);
            }

            public override string ToString() { return "[ VaryingMedium ]"; }
            public override bool IsEmissive() { return false; }

            public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
            {
                return new MediumProperties { sigma_a = new SampledSpectrum(Sigma(p)), sigma_s = new SampledSpectrum(0f),
                                              phase = new HGPhaseFunction(0f), Le = new SampledSpectrum(0f) };
            }

            public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
            {
                return buf.Alloc(new HomogeneousMajorantIterator(0, tMax, new SampledSpectrum(1.0f)));
            }
        }

        static RefractiveMedium MakeMedium(RefractiveIndexField field, Float step)
        {
            Bounds3f box = new Bounds3f(new Point3f(-1.6f, -1.6f, -1.6f), new Point3f(1.6f, 1.6f, 1.6f));
            return new RefractiveMedium(new VaryingMedium(), field, box, (SampledWavelengths l) => new SampledSpectrum(1.0f), step);
        }

        // Ratio tracking along the curved path estimates exp(-integral of sigma along the arc length), also when sigma varies
        // along the curve, and the walk ends where the integrator of the plain ray ends
        [TEST("RefractiveMedium", "RatioTrackingMatchesQuadratureAlongTheCurve")]
        public static void RefractiveMedium_RatioTrackingMatchesQuadratureAlongTheCurve()
        {
            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), 1.5f, 2.0f);
            RefractiveMedium medium = MakeMedium(lens, 0.05f);
            Point3f start = new Point3f(-0.9f, 0.1f, -0.6f);
            Vector3f direction = pbrt.Normalize(new Vector3f(1.0f, 0.2f, 0.9f));
            Float length = 2.03f;

            // The reference: the integral along a finely traced curve (trapezoid rule)
            SymplecticRay reference = new SymplecticRay(lens, start, direction);
            Float fineStep = 0.002f;
            int steps = (int)(length / fineStep + 0.5f);
            double integral = 0;
            Float previousSigma = VaryingMedium.Sigma(reference.p);
            for (int i = 0; i < steps; ++i)
            {
                reference.Step(lens, fineStep);
                Float sigma = VaryingMedium.Sigma(reference.p);
                integral += 0.5 * (sigma + previousSigma) * fineStep;
                previousSigma = sigma;
            }
            double expected = Math.Exp(-integral);

            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(11);
            int count = 40000;
            double sum = 0;
            for (int i = 0; i < count; ++i)
            {
                Float weight = 1;
                medium.SampleT_majCurved(start, direction, length, rng.Uniform<Float>(), ref rng, lambda,
                                         (Point3f p, Vector3f tangent, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                                         {
                                             weight *= 1 - (mp.sigma_a[0] + mp.sigma_s[0]) / sigma_maj[0];
                                             return true;
                                         }, out Point3f endPoint, out Vector3f endDirection, out Float endLength);
                sum += weight;
                if (i == 0)
                {
                    EXPECT_NEAR(length, endLength, 1e-5f);
                    SymplecticRay plain = new SymplecticRay(lens, start, direction);
                    plain.Advance(lens, length, 0.05f);
                    // Measured: 6.6e-7
                    EXPECT_NEAR(plain.p.x, endPoint.x, 5e-6f);
                    EXPECT_NEAR(plain.p.y, endPoint.y, 5e-6f);
                    EXPECT_NEAR(plain.p.z, endPoint.z, 5e-6f);
                    Vector3f plainDirection = plain.Direction();
                    EXPECT_NEAR(plainDirection.x, endDirection.x, 5e-6f);
                    EXPECT_NEAR(plainDirection.y, endDirection.y, 5e-6f);
                    EXPECT_NEAR(plainDirection.z, endDirection.z, 5e-6f);
                }
            }
            // The standard deviation of the mean is about 0.001 with these samples (0.29240 against 0.29257 with twenty times as many)
            EXPECT_NEAR(expected, sum / count, 5e-3);
        }

        // The tentative collisions are a Poisson process in the arc length with the rate sigma_maj, and the walk ends at the
        // boundary of the box
        [TEST("RefractiveMedium", "CollisionsArePoissonInArcLengthAndStopAtTheBounds")]
        public static void RefractiveMedium_CollisionsArePoissonInArcLengthAndStopAtTheBounds()
        {
            LinearRefractiveIndexField field = new LinearRefractiveIndexField(1.3f, new Vector3f(0, 0, 0));
            RefractiveMedium medium = MakeMedium(field, 0.07f);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(5);
            // From the origin along x the path leaves the box [-1.6, 1.6]^3 after 1.6
            int count = 8000;
            double collisions = 0;
            for (int i = 0; i < count; ++i)
            {
                int n = 0;
                // The optical length of the majorant (sigma_maj = 1) is the arc length: the sum of the transmittances between
                // the collisions and the one to the end is the length, and the collision points are at the running sum
                Float optical = 0;
                bool positionsMatch = true;
                SampledSpectrum T = medium.SampleT_majCurved(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), 10.0f, rng.Uniform<Float>(), ref rng, lambda,
                                                             (Point3f p, Vector3f tangent, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                                                             {
                                                                 ++n;
                                                                 optical += -(Float)Math.Log(T_maj[0]);
                                                                 positionsMatch &= Math.Abs(p.x - optical) < 2e-3f && Math.Abs(tangent.x - 1) < 1e-6f;
                                                                 return true;
                                                             }, out Point3f endPoint, out Vector3f endDirection, out Float endLength);
                collisions += n;
                optical += -(Float)Math.Log(T[0]);
                // FastExp is approximate: the sums differ by 4e-4 at most
                EXPECT_NEAR(endLength, optical, 1e-3f);
                EXPECT_TRUE(positionsMatch);
                // Without a collision the result is the majorant transmittance of the whole path
                if (n == 0)
                {
                    EXPECT_NEAR(Math.Exp(-1.6), T[0], 1e-5);
                }
                if (i == 0)
                {
                    // Measured: 1.2e-7
                    EXPECT_NEAR(1.6f, endLength, 1e-5f);
                    EXPECT_NEAR(1.6f, endPoint.x, 1e-5f);
                    EXPECT_NEAR(1.0f, endDirection.x, 1e-5f);
                }
            }
            // The mean is sigma_maj * 1.6 = 1.6, the standard deviation of the mean 0.014
            EXPECT_NEAR(1.6, collisions / count, 7e-2);
        }

        [TEST("RefractiveMedium", "RadianceScaleIsTheSquareOfTheIndexRatio")]
        public static void RefractiveMedium_RadianceScaleIsTheSquareOfTheIndexRatio()
        {
            EXPECT_NEAR(2.25f, RefractiveMedium.RadianceScale(1.0f, 1.5f), 1e-6f);
            EXPECT_NEAR(1.0f / 2.25f, RefractiveMedium.RadianceScale(1.5f, 1.0f), 1e-6f);
            EXPECT_NEAR(1.0f, RefractiveMedium.RadianceScale(1.3f, 1.3f), 1e-6f);
        }

        // A connection to a point on the boundary of the box (a type-1 connection) reaches it, and a retraced path ends there
        [TEST("RefractiveMedium", "ConnectsToAPointOnTheBoundary")]
        public static void RefractiveMedium_ConnectsToAPointOnTheBoundary()
        {
            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), 1.0f, 2.0f);
            RefractiveMedium medium = MakeMedium(lens, 0.05f);
            Point3f start = new Point3f(-0.4f, 0.2f, 0.1f);
            Point3f target = new Point3f(1.6f, 0.5f, 0.3f);
            RefractiveConnectionOptions options = new RefractiveConnectionOptions();
            options.stepSize = 0.03f;
            options.maxLength = 20.0f;
            EXPECT_TRUE(medium.Connect(start, target, target - start, options, out RefractiveConnectionPath path));
            SymplecticRay ray = new SymplecticRay(lens, start, path.startDirection);
            ray.Advance(lens, path.length, 0.03f);
            // The solver guarantees the tolerance of 1e-3 (measured 2e-6)
            EXPECT_NEAR(target.x, ray.p.x, 1.5e-3f);
            EXPECT_NEAR(target.y, ray.p.y, 1.5e-3f);
            EXPECT_NEAR(target.z, ray.p.z, 1.5e-3f);
            // The lens bends the path
            EXPECT_GT(path.length, pbrt.Length(target - start));
        }

        // The two ways to estimate the same integral over the directions at a point inside the medium agree: sampling the
        // direction and following the curve to a patch that emits (A), and sampling the point of the patch and finding the curve
        // to it with the connection solver, with the count of algorithm 3 and the Jacobian of the end (B).
        [TEST("RefractiveMedium", "ConnectionEstimateMatchesDirectionSampling")]
        public static void RefractiveMedium_ConnectionEstimateMatchesDirectionSampling()
        {
            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), 1.5f, 2.0f);
            RefractiveMedium medium = MakeMedium(lens, 0.05f);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            Point3f x = new Point3f(-0.3f, 0.1f, 0.2f);
            // The patch is tilted, so the cosine at its end varies over the paths
            Point3f center = new Point3f(3.0f, 0, 0);
            Vector3f normal = new Vector3f(-0.5f, 0, 0.8660254f);
            Vector3f axisU = new Vector3f(0, 1, 0);
            Vector3f axisV = pbrt.Cross(normal, axisU);
            Float half = 0.6f;
            Float area = 4 * half * half;
            RNG rng = new RNG(21);

            // A: directions
            int countA = 500000;
            double sumA = 0, sumA2 = 0;
            for (int i = 0; i < countA; ++i)
            {
                Vector3f direction = pbrt.SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Float weight = 1;
                medium.SampleT_majCurved(x, direction, 100.0f, rng.Uniform<Float>(), ref rng, lambda,
                                         (Point3f p, Vector3f tangent, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                                         {
                                             weight *= 1 - (mp.sigma_a[0] + mp.sigma_s[0]) / sigma_maj[0];
                                             return true;
                                         }, out Point3f endPoint, out Vector3f endDirection, out Float endLength);
                Float value = 0;
                Float facing = pbrt.Dot(endDirection, normal);
                if (facing != 0)
                {
                    Float t = pbrt.Dot(center - endPoint, normal) / facing;
                    if (t > 0)
                    {
                        Vector3f offset = endPoint + t * endDirection - center;
                        if (Math.Abs(pbrt.Dot(offset, axisU)) < half && Math.Abs(pbrt.Dot(offset, axisV)) < half)
                        {
                            value = weight;
                        }
                    }
                }
                sumA += value;
                sumA2 += value * value;
            }
            double meanA = sumA / countA;
            double seA = Math.Sqrt((sumA2 / countA - meanA * meanA) / countA);

            // B: points of the patch
            RefractiveConnectionOptions options = new RefractiveConnectionOptions();
            options.stepSize = 0.05f;
            options.maxLength = 12.0f;
            options.tolerance = 1e-3f;
            int countB = 40000;
            double sumB = 0, sumB2 = 0;
            int found = 0;
            for (int i = 0; i < countB; ++i)
            {
                Point3f y = center + half * (2 * rng.Uniform<Float>() - 1) * axisU + half * (2 * rng.Uniform<Float>() - 1) * axisV;
                double value = 0;
                if (medium.ConnectToPoint(x, y, options, ref rng, out RefractiveConnectionPath path, out int count))
                {
                    ++found;
                    Float weight = 1;
                    medium.SampleT_majCurved(x, path.startDirection, path.length, rng.Uniform<Float>(), ref rng, lambda,
                                             (Point3f p, Vector3f tangent, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                                             {
                                                 weight *= 1 - (mp.sigma_a[0] + mp.sigma_s[0]) / sigma_maj[0];
                                                 return true;
                                             }, out Point3f endPoint, out Vector3f endDirection, out Float endLength);
                    Float cosine = Math.Abs(pbrt.Dot(path.endDirection, normal));
                    // f = 1 / (4 pi), the density of the direction is area-density * J / cos
                    value = count * weight * cosine * area / path.areaJacobian / (4 * Math.PI);
                }
                sumB += value;
                sumB2 += value * value;
            }
            double meanB = sumB / countB;
            double seB = Math.Sqrt((sumB2 / countB - meanB * meanB) / countB);
            Console.Error.WriteLine("MEASURE A=" + meanA + "+-" + seA + " B=" + meanB + "+-" + seB + " found=" + found + "/" + countB);
            EXPECT_LT(Math.Abs(meanA - meanB), 4 * Math.Sqrt(seA * seA + seB * seB));
            EXPECT_GT(meanA, 0.001);
        }

        // GridModulatedMedium interpolates the grids trilinearly (a linear function exactly), is zero outside the bounds, and has the
        // largest density as the factor of its majorant
        [TEST("RefractiveMedium", "GridModulatedMediumInterpolatesTheGrids")]
        public static void RefractiveMedium_GridModulatedMediumInterpolatesTheGrids()
        {
            Medium inner = new HomogeneousMedium(new ConstantSpectrum(0.5f), new ConstantSpectrum(0.25f), 1.0f, new ConstantSpectrum(2f), 1.0f, 0.0f, null);
            Bounds3f bounds = new Bounds3f(new Point3f(-1, -2, -3), new Point3f(1, 2, 3));
            int nx = 3, ny = 4, nz = 5;
            Float[] density = new Float[nx * ny * nz];
            Float[] emission = new Float[nx * ny * nz];
            for (int z = 0; z < nz; ++z)
            {
                for (int y = 0; y < ny; ++y)
                {
                    for (int x = 0; x < nx; ++x)
                    {
                        Float px = -1 + 2.0f * x / (nx - 1), py = -2 + 4.0f * y / (ny - 1), pz = -3 + 6.0f * z / (nz - 1);
                        density[(z * ny + y) * nx + x] = 1.5f + 0.5f * px - 0.25f * py + 0.1f * pz;
                        emission[(z * ny + y) * nx + x] = 3.0f - 0.5f * px + 0.2f * py - 0.3f * pz;
                    }
                }
            }
            GridModulatedMedium medium = new GridModulatedMedium(inner, bounds, nx, ny, nz, density, emission);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            Point3f[] points = new Point3f[] { new Point3f(0.3f, -0.7f, 1.1f), new Point3f(-0.9f, 1.9f, -2.7f), new Point3f(0.0f, 0.0f, 0.0f),
                                               new Point3f(1.0f, 2.0f, 3.0f) };
            for (int i = 0; i < points.Length; ++i)
            {
                Point3f p = points[i];
                MediumProperties mp = medium.SamplePoint(p, lambda);
                Float expectedDensity = 1.5f + 0.5f * p.x - 0.25f * p.y + 0.1f * p.z;
                Float expectedEmission = 3.0f - 0.5f * p.x + 0.2f * p.y - 0.3f * p.z;
                EXPECT_NEAR(0.5f * expectedDensity, mp.sigma_a[0], 1e-5f);
                EXPECT_NEAR(0.25f * expectedDensity, mp.sigma_s[0], 1e-5f);
                EXPECT_NEAR(2.0f * expectedEmission, mp.Le[0], 1e-5f);
            }
            MediumProperties outside = medium.SamplePoint(new Point3f(1.01f, 0, 0), lambda);
            EXPECT_EQ(0.0f, outside.sigma_a[0]);
            EXPECT_EQ(0.0f, outside.Le[0]);
            // The largest value of the density grid is at the corner (1, -2, 3)
            EXPECT_NEAR(1.5f + 0.5f + 0.5f + 0.3f, medium.MaxDensity(), 1e-5f);
        }
    }
}
