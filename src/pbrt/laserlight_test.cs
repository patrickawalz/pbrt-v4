// (not in pbrt-v4) Tests of the laser light and of the wavelengths of its line (laserlight.cs, spectrallines.cs).

using System;
using testing;
using PLight = pbrt.PolarisedLightSystem.Light;

namespace pbrt
{
    public static unsafe class LaserLightTests
    {
        private const double Waist = 0.0005, Divergence = 0.0012, Power = 3.0, Centre = 532.0, Width = 0.4;

        private static LaserLight Laser(double waistDistance, double stokes1 = 0, double stokes3 = 0)
        {
            return new LaserLight(new Transform(), new MediumInterface(), Waist, Divergence, waistDistance, Power, Centre, Width,
                                  stokes1, 0, stokes3, new Vector3f(1, 0, 0));
        }

        private static double BeamRadius(double z, double waistDistance)
        {
            return Math.Sqrt(Waist * Waist + (z - waistDistance) * (z - waistDistance) * Divergence * Divergence);
        }

        // The wavelengths of the tests: one at the centre of the line
        private static SampledWavelengths AtLine(out double line)
        {
            Span<Float> lambda = stackalloc Float[NSpectrumSamples];
            Span<Float> pdf = stackalloc Float[NSpectrumSamples];
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                lambda[i] = (Float)Centre;
                pdf[i] = 1;
            }
            line = SpectralLineSampling.Gaussian((Float)Centre, Centre, Width);
            return SampledWavelengths.FromSamples(lambda, pdf);
        }

        // The rays of the light, followed to planes across the beam: every ray carries the power, and the part of them
        // within a radius is that of the Gaussian beam of the wave theory at that distance, near the waist and far from it,
        // for a beam that widens from the light and for one that is focused in front of it
        [TEST("LaserLight", "RaysMakeTheGaussianBeam")]
        public static void RaysMakeTheGaussianBeam()
        {
            foreach (double waistDistance in new double[] { 0.0, 1.0 })
            {
                LaserLight laser = Laser(waistDistance);
                SampledWavelengths lambda = AtLine(out double line);
                double[] planes = new double[] { 0.05, 0.4, 1.0, 2.0, 15.0 };
                double[] fractions = new double[] { 0.5, 1.0, 1.5 };
                double[,] inside = new double[planes.Length, fractions.Length];
                RNG rng = new RNG(77);
                int count = 400000;
                for (int i = 0; i < count; ++i)
                {
                    Point2f u1 = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    Point2f u2 = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    pstd.optional<LightLeSample> sample = laser.SampleLe(u1, u2, ref lambda, 0);
                    ASSERT_TRUE((bool)sample);
                    LightLeSample les = sample.value();
                    double weight = les.L[0] / line / ((double)les.pdfPos * les.pdfDir);
                    EXPECT_NEAR(Power, weight, 2e-5 * Power);
                    EXPECT_NEAR(0.0, les.ray.o.z, 1e-9);

                    // (the densities of the ray, asked again)
                    Float pdfPos, pdfDir;
                    laser.PDF_Le(les.ray, &pdfPos, &pdfDir);
                    EXPECT_NEAR(1.0, pdfPos / les.pdfPos, 2e-3);
                    EXPECT_NEAR(1.0, pdfDir / les.pdfDir, 2e-3);

                    for (int k = 0; k < planes.Length; ++k)
                    {
                        double t = planes[k] / les.ray.d.z;
                        double x = les.ray.o.x + t * les.ray.d.x, y = les.ray.o.y + t * les.ray.d.y;
                        double radius = Math.Sqrt(x * x + y * y) / BeamRadius(planes[k], waistDistance);
                        for (int f = 0; f < fractions.Length; ++f)
                        {
                            if (radius < fractions[f])
                            {
                                inside[k, f] += 1.0 / count;
                            }
                        }
                    }
                }
                for (int k = 0; k < planes.Length; ++k)
                {
                    for (int f = 0; f < fractions.Length; ++f)
                    {
                        double expected = 1 - Math.Exp(-2 * fractions[f] * fractions[f]);
                        double error = Math.Sqrt(expected * (1 - expected) / count);
                        Console.WriteLine("waist at " + waistDistance + ", plane " + planes[k] + ", within " + fractions[f] + " w: " +
                                          inside[k, f] + " expected " + expected + " +- " + error);
                        EXPECT_LT(Math.Abs(inside[k, f] - expected), 4 * error + 2e-4);
                    }
                }
            }
        }

        // The light toward a point: the radiance of the sampled ray is that of the rays of the light (as their densities give
        // it), the estimate of the irradiance is exact with every sample, and the radiance integrated over the directions at
        // the point, by quadrature, is that irradiance
        [TEST("LaserLight", "LightAtAPoint")]
        public static void LightAtAPoint()
        {
            foreach (double waistDistance in new double[] { 0.0, 1.0 })
            {
                LaserLight laser = Laser(waistDistance);
                SampledWavelengths lambda = AtLine(out double line);
                RNG rng = new RNG(9);
                foreach (double z in new double[] { 0.02, 0.5, 1.0, 3.0, 40.0 })
                {
                    double beam = BeamRadius(z, waistDistance);
                    foreach (double offset in new double[] { 0.0, 0.7, 1.6, 3.0 })
                    {
                        Point3f p = new Point3f((Float)(0.6 * offset * beam), (Float)(-0.8 * offset * beam), (Float)z);
                        double rho2 = (double)p.x * p.x + (double)p.y * p.y;
                        double expected = 2 * Power / (Math.PI * beam * beam) * Math.Exp(-2 * rho2 / (beam * beam));
                        EXPECT_NEAR(1.0, laser.Irradiance(p) / expected, 1e-4);

                        LightSampleContext ctx = new LightSampleContext(new Point3fi(p), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
                        double meanSlopeX = 0, meanSlopeY = 0, spread = 0;
                        int count = 2000;
                        for (int i = 0; i < count; ++i)
                        {
                            pstd.optional<LightLiSample> sample = laser.SampleLi(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), lambda);
                            ASSERT_TRUE((bool)sample);
                            LightLiSample ls = sample.value();
                            Vector3f toPoint = -ls.wi;
                            double cosTheta = toPoint.z;
                            // (every sample gives the irradiance)
                            EXPECT_NEAR(1.0, ls.L[0] / line * cosTheta / ls.pdf / expected, 1e-3);
                            // (the radiance of that ray by the densities of the light: P pdfPos pdfDir is L cos)
                            Float pdfPos, pdfDir;
                            laser.PDF_Le(new Ray(ls.pLight.p(), toPoint), &pdfPos, &pdfDir);
                            double radiance = Power * pdfPos * pdfDir / cosTheta;
                            EXPECT_NEAR(1.0, ls.L[0] / line / radiance, 0.03, "z " + z + " offset " + offset);
                            EXPECT_NEAR(0.0, ls.pLight.p().z, 1e-12);
                            meanSlopeX += toPoint.x / toPoint.z / count;
                            meanSlopeY += toPoint.y / toPoint.z / count;
                        }

                        // Quadrature over the slopes about the mean of the samples: the integral of L cos^4 ds
                        double fromWaist = z - waistDistance;
                        spread = Waist * Divergence / (2 * beam);
                        int side = 160;
                        double sum = 0, reach = 7 * spread, cell = 2 * reach / side;
                        double centreX = p.x * fromWaist * Divergence * Divergence / (beam * beam);
                        double centreY = p.y * fromWaist * Divergence * Divergence / (beam * beam);
                        EXPECT_NEAR(centreX, meanSlopeX, 0.15 * spread + 1e-7);
                        EXPECT_NEAR(centreY, meanSlopeY, 0.15 * spread + 1e-7);
                        for (int a = 0; a < side; ++a)
                        {
                            for (int b = 0; b < side; ++b)
                            {
                                double slopeX = centreX - reach + (a + 0.5) * cell, slopeY = centreY - reach + (b + 0.5) * cell;
                                // (double precision: the densities of the ray, as LaserLight.Densities has them)
                                double originX = p.x - z * slopeX, originY = p.y - z * slopeY;
                                double waistX = originX + waistDistance * slopeX, waistY = originY + waistDistance * slopeY;
                                double radianceCos4 = 4 * Power / (Math.PI * Math.PI * Waist * Waist * Divergence * Divergence) *
                                                      Math.Exp(-2 * (waistX * waistX + waistY * waistY) / (Waist * Waist)) *
                                                      Math.Exp(-2 * (slopeX * slopeX + slopeY * slopeY) / (Divergence * Divergence));
                                sum += radianceCos4 * cell * cell;
                            }
                        }
                        Console.WriteLine("waist at " + waistDistance + ", z " + z + ", " + offset + " w off the axis: quadrature " + sum +
                                          " irradiance " + expected);
                        EXPECT_NEAR(1.0, sum / expected, 1e-6);
                    }
                }
                // (nothing behind the light)
                LightSampleContext behind = new LightSampleContext(new Point3fi(new Point3f(0, 0, -0.1f)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
                EXPECT_FALSE((bool)laser.SampleLi(behind, new Point2f(0.3f, 0.6f), lambda));
            }
        }

        // The wavelengths of the film with the line of a laser in the scene: the estimate of the line is one, and that of a
        // smooth spectrum is what it was, for a wide line and for a narrow one
        [TEST("LaserLight", "WavelengthsAreUnbiased")]
        public static void WavelengthsAreUnbiased()
        {
            foreach (double width in new double[] { 0.4, 0.0005 })
            {
                foreach (bool visible in new bool[] { true, false })
                {
                    SpectralLineSampling.Clear();
                    SpectralLineSampling.Add(new double[] { 632.8 }, new double[] { width }, new double[] { 1.0 }, 0.5);
                    EXPECT_TRUE(SpectralLineSampling.Active);
                    RNG rng = new RNG(11);
                    int count = 400000;
                    double[] mean = new double[2], squares = new double[2];
                    for (int i = 0; i < count; ++i)
                    {
                        SampledWavelengths lambda = visible ? SpectralLineSampling.SampleVisible(rng.Uniform<Float>())
                                                            : SpectralLineSampling.SampleUniform(rng.Uniform<Float>(), 360, 830);
                        double[] v = new double[2];
                        for (int k = 0; k < NSpectrumSamples; ++k)
                        {
                            double pdf = lambda.PDF()[k];
                            EXPECT_GT(pdf, 0);
                            v[0] += SpectralLineSampling.Gaussian(lambda[k], 632.8, width) / pdf / NSpectrumSamples;
                            v[1] += Spectra.Y().Evaluate(lambda[k]) / pdf / NSpectrumSamples;
                        }
                        for (int c = 0; c < 2; ++c)
                        {
                            mean[c] += v[c];
                            squares[c] += v[c] * v[c];
                        }
                    }
                    double[] expected = new double[] { 1.0, CIE_Y_integral };
                    for (int c = 0; c < 2; ++c)
                    {
                        mean[c] /= count;
                        double error = Math.Sqrt(Math.Max(0.0, squares[c] / count - mean[c] * mean[c]) / count);
                        Console.WriteLine("width " + width + (visible ? " visible" : " uniform") + " component " + c + ": " + mean[c] +
                                          " +- " + error + " expected " + expected[c]);
                        EXPECT_LT(Math.Abs(mean[c] - expected[c]), 4 * error + 1e-6 * expected[c]);
                        EXPECT_LT(error, 0.01 * expected[c]);
                    }
                }
            }
            SpectralLineSampling.Clear();
            EXPECT_FALSE(SpectralLineSampling.Active);
        }

        // The polarisation of the beam, as the integrators ask for it: the electric field of a linear beam lies along the
        // axis that was given, and a beam without polarisation is plain light
        [TEST("LaserLight", "Polarisation")]
        public static void Polarisation()
        {
            Vector3f w = new Vector3f(0, 0, 1);
            SampledSpectrum scalar = new SampledSpectrum(2.5f);
            SampledWavelengths lambda = AtLine(out double line);
            Interaction point = new Interaction(new Point3f(0, 0, 0), new MediumInterface());

            PLight linear = PolarisedEmission.AtPoint(Laser(0, 1, 0), point, w, lambda, scalar);
            StokesVector along = linear.RepresentInDirection(w, new Vector3f(1, 0, 0)).ToStokesVector();
            EXPECT_NEAR(2.5, along.s0[0], 1e-5);
            EXPECT_NEAR(2.5, along.s1[0], 1e-5);
            EXPECT_NEAR(0.0, along.s2[0], 1e-5);
            EXPECT_NEAR(0.0, along.s3[0], 1e-5);
            StokesVector across = linear.RepresentInDirection(w, new Vector3f(0, 1, 0)).ToStokesVector();
            EXPECT_NEAR(-2.5, across.s1[0], 1e-5);
            EXPECT_NEAR(0.0, across.s2[0], 1e-5);
            StokesVector diagonal = linear.RepresentInDirection(w, Normalize(new Vector3f(1, 1, 0))).ToStokesVector();
            EXPECT_NEAR(0.0, diagonal.s1[0], 1e-5);
            EXPECT_NEAR(2.5, Math.Abs(diagonal.s2[0]), 1e-5);

            PLight circular = PolarisedEmission.AtPoint(Laser(0, 0, 0.5), point, w, lambda, scalar);
            StokesVector turned = circular.RepresentInDirection(w, new Vector3f(0, 1, 0)).ToStokesVector();
            EXPECT_NEAR(2.5, turned.s0[0], 1e-5);
            EXPECT_NEAR(0.0, turned.s1[0], 1e-5);
            EXPECT_NEAR(1.25, turned.s3[0], 1e-5);

            PLight plain = PolarisedEmission.AtPoint(Laser(0), point, w, lambda, scalar);
            EXPECT_FALSE(plain.IsPolarised());
            EXPECT_NEAR(2.5, plain.GetIntensity()[0], 1e-5);
        }

        // What the light samplers are told: a laser is no point light for the samplers of distances, and the sampler that goes
        // by the bounds of the lights leaves no point that the light reaches without a probability, beside another light
        [TEST("LaserLight", "SamplersAreToldTheTruth")]
        public static void SamplersAreToldTheTruth()
        {
            EXPECT_FALSE(GaussianBeamLight.IsPointLight(Laser(0)));
            EXPECT_TRUE(IsDeltaLight(Laser(0).Type()));
            foreach (double waistDistance in new double[] { 0.0, 1.0 })
            {
                Transform placed = Translate(new Vector3f(0.3f, -1.1f, 2.0f)) * Rotate(37, new Vector3f(0.4f, 1, -0.2f));
                LaserLight laser = new LaserLight(placed, new MediumInterface(), Waist, Divergence, waistDistance, Power, Centre,
                                                  Width, 0, 0, 0, new Vector3f(1, 0, 0));
                Light other = new PointLight(Translate(new Vector3f(1, 1, 1)), new MediumInterface(), new ConstantSpectrum(1), 1);
                BVHLightSampler sampler = new BVHLightSampler(new pstd.span<Light>(new Light[] { laser, other }), new Allocator());
                SampledWavelengths lambda = AtLine(out double line);
                RNG rng = new RNG(3);
                int reached = 0;
                for (int i = 0; i < 200000; ++i)
                {
                    // (the rays of the light, the outermost among them: the points along them are those that it reaches)
                    Float far = 1 - (Float)Math.Pow(10, -7 * rng.Uniform<Float>());
                    Point2f u1 = new Point2f(i % 2 == 0 ? far : rng.Uniform<Float>(), rng.Uniform<Float>());
                    Point2f u2 = new Point2f(i % 3 == 0 ? far : rng.Uniform<Float>(), rng.Uniform<Float>());
                    pstd.optional<LightLeSample> sample = laser.SampleLe(u1, u2, ref lambda, 0);
                    ASSERT_TRUE((bool)sample);
                    Ray ray = sample.value().ray;
                    Point3f p = ray.Apply((Float)Math.Pow(10, Lerp(rng.Uniform<Float>(), -4f, 2f)));
                    LightSampleContext ctx = new LightSampleContext(new Point3fi(p), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
                    EXPECT_GT(sampler.PMF(ctx, laser), 0);
                    // (and the light does come from there)
                    if ((bool)laser.SampleLi(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), lambda))
                    {
                        ++reached;
                    }
                }
                EXPECT_GT(reached, 190000);
                // (far off the beam the light is not asked for)
                LightSampleContext aside = new LightSampleContext(new Point3fi(placed.Apply(new Point3f(1, 0, 1))), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
                EXPECT_EQ(0, sampler.PMF(aside, laser));
                EXPECT_FALSE((bool)laser.SampleLi(aside, new Point2f(0.3f, 0.6f), lambda));
                EXPECT_GT(laser.Phi(lambda)[0], 0);
            }
        }

        // The distance along a ray where it crosses the beam: the density that is given is that of the samples (the mean of
        // one over it is the length of the segment, and of the part of it near the beam the length of that part), for rays
        // across the beam, along it, past it and away from it
        [TEST("LaserLight", "DistanceAcrossTheBeam")]
        public static void DistanceAcrossTheBeam()
        {
            LaserLight laser = Laser(1.0);
            Point3f[] origins = new Point3f[] { new Point3f(-0.3f, 0.02f, 0.5f), new Point3f(0.0002f, 0, 0.1f), new Point3f(-0.3f, 0.5f, 0.5f),
                                                new Point3f(0.2f, 0.1f, 2f), new Point3f(-0.05f, 0.0001f, -0.2f) };
            Vector3f[] directions = new Vector3f[] { new Vector3f(1, -0.07f, 0.2f), new Vector3f(0, 0, 1), new Vector3f(1, 0, 0),
                                                     new Vector3f(1, 0.3f, 0.1f), new Vector3f(0.1f, 0, 1) };
            Float[] lengths = new Float[] { 1.5f, 3f, 2f, 1f, 4f };
            RNG rng = new RNG(13);
            for (int k = 0; k < origins.Length; ++k)
            {
                Vector3f direction = Normalize(directions[k]);
                Float tMax = lengths[k];
                // (the part of the segment within a millimetre of the nearest point to the axis, if it is on the segment)
                double across = (double)direction.x * direction.x + (double)direction.y * direction.y;
                double nearest = across > 0 ? -((double)origins[k].x * direction.x + (double)origins[k].y * direction.y) / across : -1;
                double low = Math.Max(nearest - 0.001, 0.0), high = Math.Min(nearest + 0.001, (double)tMax);
                int count = 400000;
                double mean = 0, squares = 0, near = 0, nearSquares = 0;
                for (int i = 0; i < count; ++i)
                {
                    ASSERT_TRUE(laser.SampleDistance(origins[k], direction, tMax, rng.Uniform<Float>(), out Float t, out Float pdf));
                    EXPECT_TRUE(t >= 0 && t <= tMax);
                    Float again = laser.PdfDistance(origins[k], direction, tMax, t);
                    EXPECT_NEAR(1.0, again / pdf, 1e-5);
                    double v = 1.0 / pdf;
                    mean += v;
                    squares += v * v;
                    if (t >= low && t <= high)
                    {
                        near += v;
                        nearSquares += v * v;
                    }
                }
                mean /= count;
                near /= count;
                double error = Math.Sqrt(Math.Max(0.0, squares / count - mean * mean) / count);
                double nearError = Math.Sqrt(Math.Max(0.0, nearSquares / count - near * near) / count);
                Console.WriteLine("ray " + k + ": length " + mean + " +- " + error + " of " + tMax + "; near the beam " + near + " +- " + nearError +
                                  " of " + Math.Max(high - low, 0.0));
                EXPECT_LT(Math.Abs(mean - tMax), 4 * error + 1e-4 * tMax);
                EXPECT_LT(Math.Abs(near - Math.Max(high - low, 0.0)), 4 * nearError + 2e-6);
            }
            // (a ray without an end: all of it by the Gaussian)
            Vector3f crossing = Normalize(new Vector3f(1, -0.07f, 0.2f));
            ASSERT_TRUE(laser.SampleDistance(origins[0], crossing, Float.PositiveInfinity, 0.4f, out Float open, out Float openPdf));
            EXPECT_NEAR(1.0, laser.PdfDistance(origins[0], crossing, Float.PositiveInfinity, open) / openPdf, 1e-5);
            EXPECT_GT(openPdf, 100);
        }
    }
}
