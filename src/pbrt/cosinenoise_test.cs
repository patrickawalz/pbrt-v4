// (not in pbrt-v4) cosinenoise_test.cs: the cosine noise, its integral along a ray and its bounds over a box against numbers
// computed without them, and the transmittance of the cosine noise medium against a quadrature of its density.

using System;
using testing;

namespace pbrt
{
    using static pbrt;
    using static testing.Gtest;

    public static unsafe class CosineNoiseTests
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

        [TEST("CosineNoise", "IntegralAlongARay")]
        public static void IntegralAlongARay()
        {
            CosineNoise noise = new CosineNoise(3f, 5, 24, 2f, 0.5f, 0.4f, false);
            RNG rng = new RNG(7);
            for (int i = 0; i < 40; ++i)
            {
                double ox = 4 * rng.Uniform<Float>() - 2, oy = 4 * rng.Uniform<Float>() - 2, oz = 4 * rng.Uniform<Float>() - 2;
                double dx = 2 * rng.Uniform<Float>() - 1, dy = 2 * rng.Uniform<Float>() - 1, dz = 2 * rng.Uniform<Float>() - 1;
                if (i % 5 == 0)
                {
                    // a ray along an axis, and a very short one
                    dy = 0;
                    dz = 0;
                }
                double t0 = rng.Uniform<Float>(), t1 = t0 + (i % 7 == 0 ? 1e-3 : 0.2 + 3 * rng.Uniform<Float>());
                double exact = noise.FieldIntegral(ox, oy, oz, dx, dy, dz, t0, t1);
                double numeric = Quadrature((double t) => noise.Field(ox + t * dx, oy + t * dy, oz + t * dz), t0, t1, 20000);
                EXPECT_NEAR(exact, numeric, 1e-7 * Math.Max(1.0, Math.Abs(numeric)));
            }
            // A ray along the crests of a wave: a direction at right angles to nothing in particular is found by making the
            // direction tiny, so that b (t1 - t0) is far below the threshold of the series of the sinc
            double tiny = noise.FieldIntegral(0.3, -0.2, 0.9, 1e-9, 0, 0, 0, 2);
            EXPECT_NEAR(tiny, 2 * noise.Field(0.3, -0.2, 0.9), 1e-6);
        }

        [TEST("CosineNoise", "BoundsOverABox")]
        public static void BoundsOverABox()
        {
            CosineNoise noise = new CosineNoise(2f, 4, 24, 2f, 0.5f, 0.2f, false);
            RNG rng = new RNG(11);
            double slack = 0, width = 0;
            for (int i = 0; i < 60; ++i)
            {
                Point3f corner = new Point3f(4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2);
                Float size = i % 3 == 0 ? 0.02f : (i % 3 == 1 ? 0.15f : 1.5f);
                Bounds3f box = new Bounds3f(corner, corner + new Vector3f(size, size * 0.7f, size * 1.3f));
                double lo, hi;
                noise.FieldBounds(box, out lo, out hi);
                double least = double.MaxValue, most = double.MinValue;
                for (int s = 0; s < 2000; ++s)
                {
                    Point3f p = box.Lerp(new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>()));
                    double v = noise.Field(p.x, p.y, p.z);
                    least = Math.Min(least, v);
                    most = Math.Max(most, v);
                }
                EXPECT_LE(lo, least + 1e-9);
                EXPECT_GE(hi, most - 1e-9);
                if (i % 3 == 0)
                {
                    slack += (hi - lo) - (most - least);
                    width += hi - lo;
                }
            }
            // Over a small box the bounds are close to the range the field has there, against the whole range of the field
            EXPECT_LT(width / 20, 0.5 * 2 * noise.Amplitude());
            Console.WriteLine("MEASURE cosine noise, boxes of 0.02: mean width of the bounds {0:F3} of the range {1:F3}; of that not reached by 2000 samples {2:F3}",
                              width / 20, 2 * noise.Amplitude(), slack / 20);
        }

        [TEST("CosineNoise", "DensityRange")]
        public static void DensityRange()
        {
            RNG rng = new RNG(3);
            foreach (Float offset in new Float[] { 12f, 3f, 1f, 0f, -1f })
            {
                CosineNoise noise = new CosineNoise(5f, 4, 24, 2f, 0.5f, offset, false);
                // (the sum of the weights is 11.3 standard deviations for four octaves at a gain of a half)
                EXPECT_EQ(noise.NeverNegative(), offset >= 12);
                int empty = 0;
                for (int s = 0; s < 20000; ++s)
                {
                    Point3f p = new Point3f(10 * rng.Uniform<Float>(), 10 * rng.Uniform<Float>(), 10 * rng.Uniform<Float>());
                    Float d = noise.Density(p);
                    EXPECT_GE(d, 0);
                    EXPECT_LE(d, noise.MaxDensity());
                    EXPECT_LE(Math.Max(0.0, -noise.Field(p.x, p.y, p.z)) / noise.Norm(), noise.ResidualBound() + 1e-12);
                    if (noise.Field(p.x, p.y, p.z) < 0)
                    {
                        ++empty;
                    }
                }
                if (offset >= 12)
                {
                    EXPECT_EQ(empty, 0);
                    EXPECT_EQ(noise.ResidualBound(), 0.0);
                }
                Console.WriteLine("MEASURE cosine noise, offset {0}: {1:F1}% of the points are empty", offset, 100.0 * empty / 20000);
            }
        }

        private static CosineNoiseMedium Medium(Float offset, bool bounded, Transform renderFromMedium, string transmittance, bool closedFormSampling)
        {
            CosineNoise noise = new CosineNoise(4f, 4, 24, 2f, 0.5f, offset, false);
            CosineNoiseMedium medium = new CosineNoiseMedium(bounded, bounded ? new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)) : new Bounds3f(),
                                                             renderFromMedium, new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(0.2f, 0.4f, 1f)),
                                                             new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(1f, 2f, 3f)), 0,
                                                             (Float)(0.25 / (offset + 0.5)), noise, closedFormSampling, 32, new Allocator());
            medium.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Transmittance(transmittance));
            return medium;
        }

        // exp(-optical depth) of the clamped density between two points, by quadrature of what SamplePoint gives
        private static void Reference(CosineNoiseMedium medium, Point3f from, Point3f to, SampledWavelengths lambda, double[] T)
        {
            int steps = 200000;
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

        [TEST("CosineNoise", "ClosedFormTransmittanceIsExact")]
        public static void ClosedFormTransmittanceIsExact()
        {
            // The field is never negative: one evaluation is the transmittance, in a box, without one, and in a medium that is
            // scaled and turned against the render space
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.52f);
            Transform[] transforms = { new Transform(), Translate(new Vector3f(0.2f, -0.1f, 0.3f)) * Rotate(35, new Vector3f(1, 2, 0.5f)) * Scale(1.4f, 0.8f, 1.1f) };
            foreach (Transform renderFromMedium in transforms)
            {
                foreach (bool bounded in new bool[] { false, true })
                {
                    CosineNoiseMedium medium = Medium(12f, bounded, renderFromMedium, "closedform", false);
                    Point3f from = new Point3f(-0.5f, -0.4f, -0.45f), to = new Point3f(0.45f, 0.5f, 0.3f);
                    double[] reference = new double[NSpectrumSamples];
                    Reference(medium, from, to, lambda, reference);
                    // (a direction that is not of length one: tMax is in its units)
                    Ray ray = new Ray(from, 0.5f * (to - from));
                    RNG rng = new RNG(5);
                    SampledSpectrum T;
                    ASSERT_TRUE(medium.ClosedFormTransmittance(ray, 2f, ref rng, lambda, out T));
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        EXPECT_NEAR(T[c], reference[c], 2e-5 * Math.Max(reference[c], 1e-3));
                        EXPECT_TRUE(reference[c] > 1e-6 && reference[c] < 0.95);
                    }
                    // and it is the same number again: nothing is random
                    SampledSpectrum again;
                    medium.ClosedFormTransmittance(ray, 2f, ref rng, lambda, out again);
                    EXPECT_EQ(T[0], again[0]);
                }
            }
            // A ray that misses the box
            CosineNoiseMedium box = Medium(12f, true, new Transform(), "closedform", false);
            RNG unused = new RNG(1);
            SampledSpectrum miss;
            ASSERT_TRUE(box.ClosedFormTransmittance(new Ray(new Point3f(3, 3, 3), new Vector3f(1, 0, 0)), 5f, ref unused, lambda, out miss));
            EXPECT_EQ(miss[0], 1f);
            // An endless segment in a medium without a box is left to the tracker
            CosineNoiseMedium open = Medium(12f, false, new Transform(), "closedform", false);
            EXPECT_FALSE(open.ClosedFormTransmittance(new Ray(new Point3f(0, 0, 0), new Vector3f(1, 0, 0)), Infinity, ref unused, lambda, out miss));
        }

        [TEST("CosineNoise", "DistanceSamplingIsExact")]
        public static void DistanceSamplingIsExact()
        {
            // The field is never negative and the distances to the collisions come from the closed form, without a majorant. The
            // first collision then lies beyond a distance with the probability that is the transmittance of the tracking
            // wavelength to it; the walk that reaches the end returns the transmittance of every wavelength; and the collision
            // is given the extinction of the medium there as its majorant, so that it is never a null one.
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.52f);
            Transform[] transforms = { new Transform(), Translate(new Vector3f(0.2f, -0.1f, 0.3f)) * Rotate(35, new Vector3f(1, 2, 0.5f)) * Scale(1.4f, 0.8f, 1.1f) };
            foreach (Transform renderFromMedium in transforms)
            {
                foreach (bool bounded in new bool[] { false, true })
                {
                    CosineNoiseMedium medium = Medium(12f, bounded, renderFromMedium, "closedform", true);
                    ASSERT_TRUE(medium.ClosedFormSampling());
                    Point3f from = new Point3f(-0.5f, -0.4f, -0.45f), to = new Point3f(0.45f, 0.5f, 0.3f);
                    Point3f middle = Lerp(0.4f, from, to);
                    double[] toMiddle = new double[NSpectrumSamples], toEnd = new double[NSpectrumSamples];
                    Reference(medium, from, middle, lambda, toMiddle);
                    Reference(medium, from, to, lambda, toEnd);
                    Ray ray = new Ray(from, 0.5f * (to - from));
                    RNG rng = new RNG(11);
                    int count = 400000, beyondMiddle = 0, beyondEnd = 0, nullCollisions = 0;
                    double[] sum = new double[NSpectrumSamples];
                    Float middleDistance = Distance(from, middle);
                    for (int i = 0; i < count; ++i)
                    {
                        bool collided = false;
                        Float collisionDistance = 0;
                        SampledSpectrum T_maj = SampleT_maj(ray, medium, 2f, rng.Uniform<Float>(), ref rng, lambda,
                            (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T) =>
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
                    Console.WriteLine("MEASURE cosine noise, closed form distances: beyond the middle {0:F5} (transmittance {1:F5}), beyond the end {2:F5} ({3:F5})",
                                      (double)beyondMiddle / count, toMiddle[0], (double)beyondEnd / count, toEnd[0]);
                }
            }
        }

        [TEST("CosineNoise", "SquaredIsExactAtEveryOffset")]
        public static void SquaredIsExactAtEveryOffset()
        {
            // The square of the sum is a sum of cosines: its integral along a ray is the closed form, it is never negative, its
            // mean is the one it is divided by, and its bounds over a box hold. The medium of it has its transmittance in one
            // evaluation and its collisions without a majorant, at an offset where the plain noise would be half empty.
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.52f);
            foreach (Float offset in new Float[] { 0f, 0.7f, -1.5f })
            {
                CosineNoise noise = new CosineNoise(4f, 2, 6, 2f, 0.5f, offset, true);
                EXPECT_TRUE(noise.NeverNegative());
                EXPECT_EQ(noise.ResidualBound(), 0.0);
                RNG rng = new RNG(7);
                for (int i = 0; i < 20; ++i)
                {
                    double ox = 4 * rng.Uniform<Float>() - 2, oy = 4 * rng.Uniform<Float>() - 2, oz = 4 * rng.Uniform<Float>() - 2;
                    double dx = 2 * rng.Uniform<Float>() - 1, dy = 2 * rng.Uniform<Float>() - 1, dz = 2 * rng.Uniform<Float>() - 1;
                    double t0 = rng.Uniform<Float>(), t1 = t0 + 0.2 + 3 * rng.Uniform<Float>();
                    double exact = noise.FieldIntegral(ox, oy, oz, dx, dy, dz, t0, t1);
                    double numeric = Quadrature((double t) => noise.Field(ox + t * dx, oy + t * dy, oz + t * dz), t0, t1, 20000);
                    EXPECT_NEAR(exact, numeric, 1e-7 * Math.Max(1.0, Math.Abs(numeric)));
                }
                double sum = 0, largest = 0;
                int count = 200000, thin = 0;
                for (int i = 0; i < count; ++i)
                {
                    Point3f p = new Point3f(40 * rng.Uniform<Float>() - 20, 40 * rng.Uniform<Float>() - 20, 40 * rng.Uniform<Float>() - 20);
                    double d = noise.Density(p);
                    EXPECT_GE(d, 0.0);
                    sum += d;
                    largest = Math.Max(largest, d);
                    if (d < 0.05)
                    {
                        ++thin;
                    }
                }
                EXPECT_NEAR(sum / count, 1.0, 0.03);
                EXPECT_LE(largest, noise.MaxDensity() * (1 + 1e-6));
                for (int i = 0; i < 30; ++i)
                {
                    Point3f corner = new Point3f(4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2);
                    Float size = i % 2 == 0 ? 0.02f : 0.3f;
                    Bounds3f box = new Bounds3f(corner, corner + new Vector3f(size, size, size));
                    double lo, hi;
                    noise.FieldBounds(box, out lo, out hi);
                    EXPECT_GE(lo, 0.0);
                    for (int k = 0; k < 500; ++k)
                    {
                        Point3f p = box.Lerp(new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>()));
                        double f = noise.Field(p.x, p.y, p.z);
                        EXPECT_GE(f, lo - 1e-9);
                        EXPECT_LE(f, hi + 1e-9);
                    }
                }

                CosineNoiseMedium medium = new CosineNoiseMedium(true, new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)), new Transform(),
                                                                 new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(0.2f, 0.4f, 1f)),
                                                                 new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(1f, 2f, 3f)), 0, 0.5f, noise, true, 32,
                                                                 new Allocator());
                Point3f from = new Point3f(-0.5f, -0.4f, -0.45f), to = new Point3f(0.45f, 0.5f, 0.3f);
                double[] reference = new double[NSpectrumSamples];
                Reference(medium, from, to, lambda, reference);
                Ray ray = new Ray(from, 0.5f * (to - from));
                SampledSpectrum T;
                ASSERT_TRUE(medium.ClosedFormTransmittance(ray, 2f, ref rng, lambda, out T));
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    EXPECT_NEAR(T[c], reference[c], 2e-5 * Math.Max(reference[c], 1e-3));
                }
                int walks = 200000, through = 0;
                for (int i = 0; i < walks; ++i)
                {
                    bool collided = false;
                    SampleT_maj(ray, medium, 2f, rng.Uniform<Float>(), ref rng, lambda,
                        (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_to) =>
                        {
                            collided = true;
                            EXPECT_NEAR(sigma_maj[0], mp.sigma_a[0] + mp.sigma_s[0], 1e-6f * sigma_maj[0]);
                            return false;
                        });
                    if (!collided)
                    {
                        ++through;
                    }
                }
                EXPECT_NEAR((double)through / walks, reference[0], 4 * Math.Sqrt(reference[0] * (1 - reference[0]) / walks));
                Console.WriteLine("MEASURE squared cosine noise, offset {0}: mean density {1:F4}, largest {2:F2} (bound {3:F2}), {4:F1}% of the points under 0.05; "
                                  + "transmittance {5:F6} (closed form {6:F6}), walks through {7:F5}", offset, sum / count, largest, noise.MaxDensity(),
                                  100.0 * thin / count, reference[0], T[0], (double)through / walks);
            }
        }

        [TEST("CosineNoise", "ClampedDensityIsUnbiased")]
        public static void ClampedDensityIsUnbiased()
        {
            // The field goes below zero ("offset" 3, 1 and 0 standard deviations): the closed form with ratio tracking of what the clamp adds has the
            // transmittance of the clamped density as its mean. Beside it, ratio tracking of the whole density through the same
            // medium, for the spread of the two.
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.52f);
            foreach (Float offset in new Float[] { 3f, 1f, 0.5f, 0f })
            {
                CosineNoiseMedium medium = Medium(offset, true, new Transform(), "closedform", false);
                Point3f from = new Point3f(-0.9f, -0.8f, -0.7f), to = new Point3f(0.8f, 0.9f, 0.6f);
                double[] reference = new double[NSpectrumSamples];
                Reference(medium, from, to, lambda, reference);
                Ray ray = new Ray(from, to - from);
                int count = 40000;
                RNG rng = new RNG(17);
                double[] mean = new double[NSpectrumSamples], square = new double[NSpectrumSamples];
                for (int i = 0; i < count; ++i)
                {
                    SampledSpectrum T;
                    medium.ClosedFormTransmittance(ray, 1f, ref rng, lambda, out T);
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        mean[c] += T[c];
                        square[c] += (double)T[c] * T[c];
                    }
                }
                // Ratio tracking of the clamped density with its majorant, the same number of times
                SampledSpectrum sigma_maj = medium.AggregateExtinction(lambda);
                double length = Distance(from, to), majorant = sigma_maj[0];
                double ratioMean = 0, ratioSquare = 0;
                for (int i = 0; i < count; ++i)
                {
                    double weight = 1, t = 0;
                    while (true)
                    {
                        t -= Math.Log(1.0 - rng.Uniform<Float>()) / majorant;
                        if (!(t < length))
                        {
                            break;
                        }
                        MediumProperties mp = medium.SamplePoint(Lerp((Float)(t / length), from, to), lambda);
                        weight *= 1.0 - ((double)mp.sigma_a[0] + mp.sigma_s[0]) / majorant;
                    }
                    ratioMean += weight;
                    ratioSquare += weight * weight;
                }
                ratioMean /= count;
                double ratioDeviation = Math.Sqrt(Math.Max(0.0, ratioSquare / count - ratioMean * ratioMean));
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    mean[c] /= count;
                    double deviation = Math.Sqrt(Math.Max(0.0, square[c] / count - mean[c] * mean[c]));
                    EXPECT_NEAR(mean[c], reference[c], 5 * deviation / Math.Sqrt(count) + 1e-6);
                    if (c == 0)
                    {
                        Console.WriteLine("MEASURE cosine noise medium, offset {0}: transmittance {1:F5}, closed form with residual {2:F5} (deviation of one estimate {3:F4}), " +
                                          "ratio tracking {4:F5} (deviation {5:F4})", offset, reference[c], mean[c], deviation, ratioMean, ratioDeviation);
                    }
                }
            }
        }

        [TEST("CosineNoise", "TextureAndItsBounds")]
        public static void TextureAndItsBounds()
        {
            CosineNoise noise = new CosineNoise(3f, 4, 24, 2f, 0.5f, 1f, false);
            CosineNoiseTexture texture = new CosineNoiseTexture(new PointTransformMapping(Scale(2f, 2f, 2f)), noise, 0.8f);
            EXPECT_EQ(texture.MinValue(), 0f);
            EXPECT_NEAR(texture.MaxValue(), 0.8 * noise.MaxDensity(), 1e-5);
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
                    EXPECT_GE(v, lo - 1e-6f);
                    EXPECT_LE(v, hi + 1e-6f);
                    // the texture is the noise at the mapped point
                    EXPECT_NEAR(v, 0.8f * noise.Density(new Point3f(2 * ctx.p.x, 2 * ctx.p.y, 2 * ctx.p.z)), 1e-5f);
                }
            }
        }
    }
}
