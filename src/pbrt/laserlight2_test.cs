// (not in pbrt-v4) Tests of the beam of any spectrum and of the slide across it (laserlight2.cs).

using System;
using testing;

namespace pbrt
{
    public static unsafe class LaserLight2Tests
    {
        private const double Waist = 0.002, Divergence = 0.003;

        // A spectrum of the luminous power that was asked: every ray of the light carries it, and the light toward a point
        // is the irradiance of the Gaussian beam times the spectrum
        [TEST("LaserLight2", "ColourCarriesThePower")]
        public static void ColourCarriesThePower()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float power = 5;
            Float scale = power / SpectrumToPhotometric(L);
            LaserLight2 light = new LaserLight2(new Transform(), new MediumInterface(), Waist, Divergence, 0.5, L, scale, null, null, null, 0,
                                                new Vector3f(0, 1, 0), 0, 0, 0, new Vector3f(1, 0, 0));
            RNG rng = new RNG(21);
            for (int i = 0; i < 2000; ++i)
            {
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                pstd.optional<LightLeSample> sample = light.SampleLe(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                     new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), ref lambda, 0);
                ASSERT_TRUE((bool)sample);
                LightLeSample les = sample.value();
                for (int k = 0; k < NSpectrumSamples; ++k)
                {
                    EXPECT_NEAR(1.0, les.L[k] / ((double)les.pdfPos * les.pdfDir) / scale, 2e-5);
                }

                double z = Math.Pow(10, Lerp(rng.Uniform<Float>(), -2f, 1f));
                double beam = Math.Sqrt(Waist * Waist + (z - 0.5) * (z - 0.5) * Divergence * Divergence);
                Point3f p = new Point3f((Float)(0.8 * beam * rng.Uniform<Float>()), (Float)(-0.9 * beam * rng.Uniform<Float>()), (Float)z);
                double expected = 2 / (Math.PI * beam * beam) * Math.Exp(-2 * ((double)p.x * p.x + (double)p.y * p.y) / (beam * beam)) * scale;
                LightSampleContext ctx = new LightSampleContext(new Point3fi(p), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
                pstd.optional<LightLiSample> toward = light.SampleLi(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), lambda);
                ASSERT_TRUE((bool)toward);
                LightLiSample ls = toward.value();
                for (int k = 0; k < NSpectrumSamples; ++k)
                {
                    EXPECT_NEAR(1.0, ls.L[k] * -ls.wi.z / ls.pdf / expected, 1e-3);
                }
            }
            // (the luminance that the light samplers are told: the luminous power as a constant spectrum)
            EXPECT_NEAR(power / CIE_Y_integral, light.Phi(SampledWavelengths.SampleUniform(0.5f))[0], 1e-4);
        }

        // A slide that is white on one half and black on the other: the rays that leave the white half carry the light and
        // the others none, half of the power gets through, a point close in front of the white half is lit and one in front
        // of the black half is not, and far away, where the rays of both halves have mixed, every point is lit
        [TEST("LaserLight2", "SlideAcrossTheBeam")]
        public static void SlideAcrossTheBeam()
        {
            int res = 64;
            Image image = new Image(PixelFormat.Float, new Point2i(res, res), new string[] { "R", "G", "B" });
            for (int y = 0; y < res; ++y)
            {
                for (int x = 0; x < res; ++x)
                {
                    for (int c = 0; c < 3; ++c)
                    {
                        image.SetChannel(new Point2i(x, y), c, x < res / 2 ? 1 : 0);
                    }
                }
            }
            RGBColorSpace colorSpace = RGBColorSpace.sRGB;
            Float power = 3;
            Float scale = power / SpectrumToPhotometric(colorSpace.illuminant);
            double imageWidth = 8 * Waist;
            LaserLight2 light = new LaserLight2(new Transform(), new MediumInterface(), Waist, Divergence, 0, null, scale, image, colorSpace,
                                                null, imageWidth, new Vector3f(0, 1, 0), 0, 0, 0, new Vector3f(1, 0, 0));
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            SampledSpectrum white = scale * new RGBIlluminantSpectrum(colorSpace, new RGB(1, 1, 1)).Sample(lambda);
            RNG rng = new RNG(22);
            int count = 200000;
            double through = 0;
            double pixel = imageWidth / res;
            for (int i = 0; i < count; ++i)
            {
                Point2f u1 = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), u2 = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<LightLeSample> sample = light.SampleLe(u1, u2, ref lambda, 0);
                double weight = (bool)sample ? sample.value().L[0] / ((double)sample.value().pdfPos * sample.value().pdfDir) / white[0] : 0;
                through += weight / count;
                if ((bool)sample && sample.value().ray.o.x < -pixel)
                {
                    EXPECT_NEAR(1.0, weight, 1e-4);
                }
                if ((bool)sample)
                {
                    EXPECT_LT(sample.value().ray.o.x, pixel);
                }
            }
            Console.WriteLine("part of the power through the slide: " + through);
            EXPECT_NEAR(0.5, through, 4 * Math.Sqrt(0.25 / count) + 0.01);
            EXPECT_NEAR(0.5 * power / CIE_Y_integral, light.Phi(lambda)[0], 0.02 * power / CIE_Y_integral);

            // (close to the light the slide is sharp)
            Float near = 0.001f;
            LightSampleContext lit = new LightSampleContext(new Point3fi(new Point3f(-0.4f * (Float)Waist, 0, near)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
            LightSampleContext dark = new LightSampleContext(new Point3fi(new Point3f(0.4f * (Float)Waist, 0, near)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
            LightSampleContext far = new LightSampleContext(new Point3fi(new Point3f(0.4f * (Float)Waist, 0, 50)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
            int farLit = 0;
            for (int i = 0; i < 1000; ++i)
            {
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                EXPECT_TRUE((bool)light.SampleLi(lit, u, lambda));
                EXPECT_FALSE((bool)light.SampleLi(dark, u, lambda));
                if ((bool)light.SampleLi(far, u, lambda))
                {
                    ++farLit;
                }
            }
            // (far away half of the rays through a point come from the white half)
            EXPECT_NEAR(500, farLit, 80);
        }
    }
}
