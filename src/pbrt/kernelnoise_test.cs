// (not in pbrt-v4) kernelnoise_test.cs: the kernel noise, its integral along a ray, the inverse of the integral and its bounds
// over a box against numbers computed without them, and the transmittance and the collisions of the kernel noise medium against
// a quadrature of its density.

using System;
using testing;

namespace pbrt
{
    using static pbrt;
    using static testing.Gtest;

    public static unsafe class KernelNoiseTests
    {
        private static double Quadrature(Func<double, double> f, double t0, double t1, int steps)
        {
            // Simpson's rule
            double h = (t1 - t0) / steps, sum = f(t0) + f(t1);
            for (int i = 1; i < steps; ++i)
            {
                sum += (i % 2 == 1 ? 4.0 : 2.0) * f(t0 + i * h);
            }
            return sum * h / 3.0;
        }

        [TEST("KernelNoise", "IntegralAlongARay")]
        public static void IntegralAlongARay()
        {
            foreach (Float coverage in new Float[] { 1f, 0.4f })
            {
                KernelNoise noise = new KernelNoise(3f, 3, 2f, 0.5f, coverage, 0, null);
                RNG rng = new RNG(7);
                for (int i = 0; i < 40; ++i)
                {
                    double ox = 4 * rng.Uniform<Float>() - 2, oy = 4 * rng.Uniform<Float>() - 2, oz = 4 * rng.Uniform<Float>() - 2;
                    double dx = 2 * rng.Uniform<Float>() - 1, dy = 2 * rng.Uniform<Float>() - 1, dz = 2 * rng.Uniform<Float>() - 1;
                    if (i % 5 == 0)
                    {
                        // a ray along an axis
                        dy = 0;
                        dz = 0;
                    }
                    if (i % 5 == 1)
                    {
                        // a ray that starts on the faces of the cells and runs backwards along two axes
                        ox = Math.Round(ox * 3) / 3;
                        oy = Math.Round(oy * 3) / 3;
                        dx = -Math.Abs(dx);
                        dy = -Math.Abs(dy);
                    }
                    // (a very short one too)
                    double t0 = rng.Uniform<Float>(), t1 = t0 + (i % 7 == 0 ? 1e-3 : 0.2 + 3 * rng.Uniform<Float>());
                    double exact = noise.FieldIntegral(ox, oy, oz, dx, dy, dz, t0, t1);
                    double numeric = Quadrature((double t) => noise.Field(ox + t * dx, oy + t * dy, oz + t * dz), t0, t1, 200000);
                    EXPECT_NEAR(exact, numeric, 1e-7 * Math.Max(1.0, Math.Abs(numeric)));
                    EXPECT_GE(exact, 0.0);
                }
            }
        }

        [TEST("KernelNoise", "InverseOfTheIntegral")]
        public static void InverseOfTheIntegral()
        {
            KernelNoise noise = new KernelNoise(4f, 3, 2f, 0.5f, 0.6f, 3, null);
            RNG rng = new RNG(17);
            for (int i = 0; i < 200; ++i)
            {
                double ox = 4 * rng.Uniform<Float>() - 2, oy = 4 * rng.Uniform<Float>() - 2, oz = 4 * rng.Uniform<Float>() - 2;
                double dx = 2 * rng.Uniform<Float>() - 1, dy = 2 * rng.Uniform<Float>() - 1, dz = 2 * rng.Uniform<Float>() - 1;
                double tStart = rng.Uniform<Float>(), tEnd = tStart + 0.5 + 3 * rng.Uniform<Float>();
                double whole = noise.FieldIntegral(ox, oy, oz, dx, dy, dz, tStart, tEnd);
                // A target short of the whole is reached at the distance that has it
                double target = whole * (0.02 + 0.96 * rng.Uniform<Float>());
                double t, toEnd;
                if (whole > 1e-6)
                {
                    ASSERT_TRUE(noise.InvertFieldIntegral(ox, oy, oz, dx, dy, dz, tStart, tEnd, target, out t, out toEnd));
                    EXPECT_TRUE(t > tStart && t < tEnd);
                    EXPECT_NEAR(noise.FieldIntegral(ox, oy, oz, dx, dy, dz, tStart, t), target, 1e-7 * target);
                }
                // One beyond it is not, and the whole comes back
                EXPECT_FALSE(noise.InvertFieldIntegral(ox, oy, oz, dx, dy, dz, tStart, tEnd, whole * 1.01 + 1e-6, out t, out toEnd));
                EXPECT_NEAR(toEnd, whole, 1e-9 * Math.Max(1.0, whole));
            }
        }

        [TEST("KernelNoise", "MeanAndBounds")]
        public static void MeanAndBounds()
        {
            // The density is the noise over its mean with every cell filled: its mean is 1 then and the coverage otherwise, it is
            // never negative, and it stays under its bound. Part of space is empty.
            foreach (Float coverage in new Float[] { 1f, 0.5f, 0.15f })
            {
                KernelNoise noise = new KernelNoise(2f, 3, 2f, 0.5f, coverage, 1, null);
                RNG rng = new RNG(31);
                double sum = 0, largest = 0;
                int count = 400000, empty = 0;
                for (int i = 0; i < count; ++i)
                {
                    Point3f p = new Point3f(40 * rng.Uniform<Float>() - 20, 40 * rng.Uniform<Float>() - 20, 40 * rng.Uniform<Float>() - 20);
                    double d = noise.Density(p);
                    EXPECT_GE(d, 0.0);
                    sum += d;
                    largest = Math.Max(largest, d);
                    if (d == 0)
                    {
                        ++empty;
                    }
                }
                EXPECT_NEAR(sum / count, coverage, 0.02);
                EXPECT_LE(largest, noise.MaxDensity());
                Console.WriteLine("MEASURE kernel noise, coverage {0}: mean density {1:F4}, largest {2:F2} (bound {3:F2}), {4:F1}% of the points are empty",
                                  coverage, sum / count, largest, noise.MaxDensity(), 100.0 * empty / count);
            }

            // Over a box
            KernelNoise boxed = new KernelNoise(2f, 3, 2f, 0.5f, 0.7f, 5, null);
            RNG rg = new RNG(11);
            double width = 0, reached = 0;
            for (int i = 0; i < 60; ++i)
            {
                Point3f corner = new Point3f(4 * rg.Uniform<Float>() - 2, 4 * rg.Uniform<Float>() - 2, 4 * rg.Uniform<Float>() - 2);
                Float size = i % 3 == 0 ? 0.02f : (i % 3 == 1 ? 0.15f : 1.5f);
                Bounds3f box = new Bounds3f(corner, corner + new Vector3f(size, 0.7f * size, 1.3f * size));
                double lo, hi;
                boxed.FieldBounds(box, out lo, out hi);
                double least = Double.PositiveInfinity, most = 0;
                for (int s = 0; s < 2000; ++s)
                {
                    Point3f p = box.Lerp(new Point3f(rg.Uniform<Float>(), rg.Uniform<Float>(), rg.Uniform<Float>()));
                    double f = boxed.Field(p.x, p.y, p.z);
                    least = Math.Min(least, f);
                    most = Math.Max(most, f);
                }
                EXPECT_LE(lo, least + 1e-9);
                EXPECT_GE(hi, most - 1e-9);
                if (i % 3 == 0)
                {
                    // a small box: the bounds are a small part of the range of the noise (each kernel is taken at its nearest and
                    // at its farthest point of the box, which are not the same point for all of them)
                    EXPECT_LE(hi - lo, 0.05 * boxed.MaxDensity() * boxed.Mean());
                    width += hi - lo;
                    reached += most - least;
                }
            }
            Console.WriteLine("MEASURE kernel noise, boxes of 0.02: mean width of the bounds {0:F3}, of which reached {1:F3}; bound of the noise {2:F2}",
                              width / 20, reached / 20, boxed.MaxDensity() * boxed.Mean());
        }

        [TEST("KernelNoise", "CloudEnvelope")]
        public static void CloudEnvelope()
        {
            // The kernels take the density of pbrt-v4's cloud as their heights: the integral along a ray is still the closed
            // form, part of the box is empty, and the mean over the box is close to that of the envelope (as in pbrt-v4, it is the
            // box of the medium that ends the cloud at the altitude 1: the formula does not)
            KernelNoiseCloudEnvelope envelope = new KernelNoiseCloudEnvelope(5f, 1f, 1f);
            KernelNoise noise = new KernelNoise(24f, 2, 2f, 0.5f, 1f, 0, envelope);
            RNG rng = new RNG(7);
            for (int i = 0; i < 30; ++i)
            {
                double ox = rng.Uniform<Float>(), oy = rng.Uniform<Float>(), oz = rng.Uniform<Float>();
                double dx = 2 * rng.Uniform<Float>() - 1, dy = 2 * rng.Uniform<Float>() - 1, dz = 2 * rng.Uniform<Float>() - 1;
                double t0 = 0, t1 = 0.1 + 0.5 * rng.Uniform<Float>();
                double exact = noise.FieldIntegral(ox, oy, oz, dx, dy, dz, t0, t1);
                double numeric = Quadrature((double t) => noise.Field(ox + t * dx, oy + t * dy, oz + t * dz), t0, t1, 200000);
                EXPECT_NEAR(exact, numeric, 1e-7 * Math.Max(1.0, Math.Abs(numeric)));
            }
            double sum = 0, envelopeSum = 0;
            int count = 200000, empty = 0;
            for (int i = 0; i < count; ++i)
            {
                Point3f p = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                double d = noise.Density(p);
                sum += d;
                envelopeSum += envelope.Evaluate(p.x, p.y, p.z);
                if (d == 0)
                {
                    ++empty;
                }
            }
            EXPECT_NEAR(sum / count, envelopeSum / count, 0.1 * envelopeSum / count);
            EXPECT_GT(empty, count / 20);
            Console.WriteLine("MEASURE kernel noise with the cloud envelope: mean density {0:F4}, mean of the envelope {1:F4}, {2:F1}% of the box is empty",
                              sum / count, envelopeSum / count, 100.0 * empty / count);
        }

        private static KernelNoiseMedium Medium(Float coverage, bool bounded, Transform renderFromMedium, bool closedFormSampling)
        {
            KernelNoise noise = new KernelNoise(4f, 3, 2f, 0.5f, coverage, 2, null);
            KernelNoiseMedium medium = new KernelNoiseMedium(bounded, bounded ? new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)) : new Bounds3f(),
                                                             renderFromMedium, new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(0.2f, 0.4f, 1f)),
                                                             new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(1f, 2f, 3f)), 0, 0.5f, noise,
                                                             closedFormSampling, 32, new Allocator());
            medium.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Transmittance("closedform"));
            return medium;
        }

        // exp(-optical depth) of the density between two points, by quadrature of what SamplePoint gives
        private static void Reference(KernelNoiseMedium medium, Point3f from, Point3f to, SampledWavelengths lambda, double[] T)
        {
            int steps = 400000;
            double d = Distance(from, to);
            double[] tau = new double[NSpectrumSamples];
            for (int i = 0; i < steps; ++i)
            {
                MediumProperties mp = medium.SamplePoint(Lerp((Float)((i + 0.5) / steps), from, to), lambda);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    tau[c] += ((double)mp.sigma_a[c] + mp.sigma_s[c]) * d / steps;
                }
            }
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                T[c] = Math.Exp(-tau[c]);
            }
        }

        [TEST("KernelNoise", "TransmittanceAndDistancesAreExact")]
        public static void TransmittanceAndDistancesAreExact()
        {
            // One evaluation is the transmittance, and it is the same number again. The first collision lies beyond a distance
            // with the probability that is the transmittance of the tracking wavelength to it; the walk that reaches the end
            // returns the transmittance of every wavelength; and a collision is given the extinction of the medium there as
            // its majorant, so that it is never a null one. In a box, without one, and in a medium that is scaled and turned
            // against the render space; with every cell filled and with most of them empty.
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.52f);
            Transform[] transforms = { new Transform(), Translate(new Vector3f(0.2f, -0.1f, 0.3f)) * Rotate(35, new Vector3f(1, 2, 0.5f)) * Scale(1.4f, 0.8f, 1.1f) };
            int which = 0;
            foreach (Transform renderFromMedium in transforms)
            {
                foreach (bool bounded in new bool[] { false, true })
                {
                    Float coverage = (which++ % 2 == 0) ? 1f : 0.35f;
                    KernelNoiseMedium medium = Medium(coverage, bounded, renderFromMedium, true);
                    Point3f from = new Point3f(-0.5f, -0.4f, -0.45f), to = new Point3f(0.45f, 0.5f, 0.3f);
                    Point3f middle = Lerp(0.4f, from, to);
                    double[] toMiddle = new double[NSpectrumSamples], toEnd = new double[NSpectrumSamples];
                    Reference(medium, from, middle, lambda, toMiddle);
                    Reference(medium, from, to, lambda, toEnd);
                    // (a direction that is not of length one: tMax is in its units)
                    Ray ray = new Ray(from, 0.5f * (to - from));
                    RNG rng = new RNG(11);

                    SampledSpectrum T, again;
                    ASSERT_TRUE(medium.ClosedFormTransmittance(ray, 2f, ref rng, lambda, out T));
                    medium.ClosedFormTransmittance(ray, 2f, ref rng, lambda, out again);
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        EXPECT_NEAR(T[c], toEnd[c], 2e-5 * Math.Max(toEnd[c], 1e-3));
                        EXPECT_TRUE(toEnd[c] > 1e-6 && toEnd[c] < 0.97);
                        EXPECT_EQ(T[c], again[c]);
                    }

                    int count = 200000, beyondMiddle = 0, beyondEnd = 0, nullCollisions = 0;
                    double[] sum = new double[NSpectrumSamples];
                    Float middleDistance = Distance(from, middle);
                    for (int i = 0; i < count; ++i)
                    {
                        bool collided = false;
                        Float collisionDistance = 0;
                        SampledSpectrum T_maj = SampleT_maj(ray, medium, 2f, rng.Uniform<Float>(), ref rng, lambda,
                            (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_to) =>
                            {
                                collided = true;
                                collisionDistance = Distance(from, p);
                                if (Math.Abs(sigma_maj[0] - (mp.sigma_a[0] + mp.sigma_s[0])) > 1e-6f * sigma_maj[0])
                                {
                                    ++nullCollisions;
                                }
                                return false;
                            });
                        if (!collided)
                        {
                            ++beyondEnd;
                            for (int c = 0; c < NSpectrumSamples; ++c)
                            {
                                sum[c] += T_maj[c] / T_maj[0];
                            }
                        }
                        if (!collided || collisionDistance > middleDistance)
                        {
                            ++beyondMiddle;
                        }
                    }
                    EXPECT_EQ(nullCollisions, 0);
                    EXPECT_NEAR((double)beyondMiddle / count, toMiddle[0], 4 * Math.Sqrt(toMiddle[0] * (1 - toMiddle[0]) / count));
                    EXPECT_NEAR((double)beyondEnd / count, toEnd[0], 4 * Math.Sqrt(toEnd[0] * (1 - toEnd[0]) / count));
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        // (the estimate of the other wavelengths: the walk that gets through, weighted by T / T[0])
                        EXPECT_NEAR(sum[c] / count, toEnd[c], 0.01 * toEnd[c] + 4 * toEnd[c] / toEnd[0] * Math.Sqrt(toEnd[0] / count));
                    }
                    Console.WriteLine("MEASURE kernel noise medium, coverage {0}: transmittance {1:F6} (closed form {2:F6}); beyond the middle {3:F5} ({4:F5}), "
                                      + "beyond the end {5:F5}", coverage, toEnd[0], T[0], (double)beyondMiddle / count, toMiddle[0], (double)beyondEnd / count);

                    // Tracking against the majorants of the same medium agrees
                    KernelNoiseMedium tracked = Medium(coverage, bounded, renderFromMedium, false);
                    int through = 0;
                    for (int i = 0; i < count / 4; ++i)
                    {
                        bool collided = false;
                        SampleT_maj(ray, tracked, 2f, rng.Uniform<Float>(), ref rng, lambda,
                            (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_to) =>
                            {
                                // (a real collision with the probability sigma_t / sigma_maj)
                                if (rng.Uniform<Float>() * sigma_maj[0] < mp.sigma_a[0] + mp.sigma_s[0])
                                {
                                    collided = true;
                                    return false;
                                }
                                return true;
                            });
                        if (!collided)
                        {
                            ++through;
                        }
                    }
                    EXPECT_NEAR((double)through / (count / 4), toEnd[0], 4 * Math.Sqrt(toEnd[0] * (1 - toEnd[0]) / (count / 4)));
                }
            }
            // A ray that misses the box
            KernelNoiseMedium box = Medium(1f, true, new Transform(), true);
            RNG unused = new RNG(1);
            SampledSpectrum miss;
            ASSERT_TRUE(box.ClosedFormTransmittance(new Ray(new Point3f(3, 3, 3), new Vector3f(1, 0, 0)), 5f, ref unused, lambda, out miss));
            EXPECT_EQ(miss[0], 1f);
        }

        [TEST("KernelNoise", "TextureAndItsBounds")]
        public static void TextureAndItsBounds()
        {
            KernelNoise noise = new KernelNoise(3f, 3, 2f, 0.5f, 0.8f, 0, null);
            KernelNoiseTexture texture = new KernelNoiseTexture(new PointTransformMapping(Scale(2f, 2f, 2f)), noise, 0.8f);
            EXPECT_EQ(texture.MinValue(), 0f);
            EXPECT_NEAR(texture.MaxValue(), 0.8 * noise.MaxDensity(), 1e-4);
            RNG rng = new RNG(23);
            for (int i = 0; i < 40; ++i)
            {
                Point3f corner = new Point3f(2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1);
                Float size = i % 2 == 0 ? 0.03f : 0.4f;
                Bounds3f region = new Bounds3f(corner, corner + new Vector3f(size, size, size));
                Float lo, hi;
                texture.RegionBounds(region, out lo, out hi);
                EXPECT_GE(lo, 0);
                EXPECT_LE(hi, texture.MaxValue());
                for (int s = 0; s < 500; ++s)
                {
                    TextureEvalContext ctx = new TextureEvalContext();
                    ctx.p = region.Lerp(new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Float v = texture.Evaluate(ctx);
                    EXPECT_GE(v, lo - 1e-5f);
                    EXPECT_LE(v, hi + 1e-5f);
                    // the texture is the noise at the mapped point
                    EXPECT_NEAR(v, 0.8f * noise.Density(new Point3f(2 * ctx.p.x, 2 * ctx.p.y, 2 * ctx.p.z)), 1e-5f);
                }
            }
        }
    }
}
