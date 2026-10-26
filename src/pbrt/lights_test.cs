// lights_test.cpp

using System;
using testing;

namespace pbrt
{
    public static unsafe class LightsTests
    {
        [TEST("SpotLight", "Power")]
        public static void SpotLight_Power()
        {
            ConstantSpectrum I = new ConstantSpectrum(10.0f);
            Transform id = new Transform();
            SpotLight light = new SpotLight(id, new MediumInterface(), I, 1.0f /* scale */, 60 /* total width */,
                                            40 /* falloff start */);

            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            SampledSpectrum phi = light.Phi(lambda);

            int nSamples = 1024 * 1024;
            double phiSampled = 0;
            for (int i = 0; i < nSamples; ++i)
            {
                Vector3f w = SampleUniformSphere(new Point2f(RadicalInverse(0, (uint64_t)i), RadicalInverse(1, (uint64_t)i)));
                phiSampled += light.I(w, lambda)[0];
            }
            phiSampled /= (nSamples * UniformSpherePDF());

            EXPECT_LT(std.abs(phiSampled - phi[0]), 1e-3, " qmc: " + phiSampled + ", closed-form: " + phi[0]);
        }

        [TEST("SpotLight", "Sampling")]
        public static void SpotLight_Sampling()
        {
            ConstantSpectrum I = new ConstantSpectrum(10.0f);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);

            int[][] widthStart = { new int[] { 50, 0 }, new int[] { 40, 10 }, new int[] { 60, 5 }, new int[] { 70, 70 } };
            Transform id = new Transform();
            foreach (int[] ws in widthStart)
            {
                SpotLight light = new SpotLight(id, new MediumInterface(), I, 1.0f /* scale */,
                                                ws[0] /* total width */, ws[1] /* falloff start */);

                RNG rng = new RNG();
                for (int i = 0; i < 100; ++i)
                {
                    Float u1x = rng.Uniform<Float>(), u1y = rng.Uniform<Float>();
                    Point2f u1 = new Point2f(u1x, u1y);
                    Float u2x = rng.Uniform<Float>(), u2y = rng.Uniform<Float>();
                    Point2f u2 = new Point2f(u2x, u2y);
                    pstd.optional<LightLeSample> ls = light.SampleLe(u1, u2, ref lambda, 0 /* time */);
                    EXPECT_TRUE(ls.has_value());
                    EXPECT_TRUE(ls.value().ray.o == new Point3f(0, 0, 0));
                    EXPECT_EQ(1, ls.value().pdfPos);
                    // Importance should be perfect, so a single sample should
                    // compute power with zero variance.
                    EXPECT_LT(std.abs(light.Phi(lambda)[0] - (ls.value().L / ls.value().pdfDir)[0]), 1e-3);
                }
            }
        }

        private static Image MakeLightImage(Point2i res)
        {
            Image image = new Image(PixelFormat.Float, res, new string[] { "R", "G", "B" });
            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                {
                    Float val = 0;
                    if (((x >= 30 && x <= 200) || x > 400) && y >= 40 && y <= 220)
                    {
                        val = (Float)(.2 + std.sin(100 * x * y / (Float)(res[0] * res[1])));
                        val = std.max((Float)0, val);
                    }
                    Float[] v = { val, val, val };
                    image.SetChannels(new Point2i(x, y), new pstd.span<Float>(v, 0, 3));
                }
            return image;
        }

        [TEST("GoniometricLight", "Power")]
        public static void GoniometricLight_Power()
        {
            Image image = MakeLightImage(new Point2i(256, 256));
            image = image.SelectChannels(image.GetChannelDesc(new string[] { "R" }));

            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            ConstantSpectrum I = new ConstantSpectrum(10.0f);
            Transform id = new Transform();
            GoniometricLight light = new GoniometricLight(id, new MediumInterface(), I, 1.0f, image, new Allocator());

            SampledSpectrum phi = light.Phi(lambda);

            int sqrtSamples = 1024;
            int nSamples = sqrtSamples * sqrtSamples;
            double phiSampled = 0;
            foreach (Point2f u in new Hammersley2D(nSamples))
            {
                Vector3f w = SampleUniformSphere(u);
                phiSampled += light.I(w, lambda).Average();
            }
            phiSampled /= (nSamples * UniformSpherePDF());

            EXPECT_LT(std.abs(phiSampled - phi[0]), 3e-3, " qmc: " + phiSampled + ", closed-form: " + phi[0]);
        }

        private static void testPhiVsSampled(Light light, ref SampledWavelengths lambda)
        {
            double sum = 0;
            int count = 100000;
            for (int i = 0; i < count; ++i)
            {
                Point2f u1 = new Point2f(RadicalInverse(0, (uint64_t)i), RadicalInverse(1, (uint64_t)i));
                Point2f u2 = new Point2f(RadicalInverse(2, (uint64_t)i), RadicalInverse(3, (uint64_t)i));
                pstd.optional<LightLeSample> ls = light.SampleLe(u1, u2, ref lambda, 0 /* time */);
                if (!ls.has_value())
                    continue;

                EXPECT_TRUE(ls.value().ray.o == new Point3f(0, 0, 0));
                EXPECT_EQ(1, ls.value().pdfPos);
                sum += ls.value().L[0] / ls.value().pdfDir;
            }
            SampledSpectrum Phi = light.Phi(lambda);
            EXPECT_LT(std.abs(sum / count - Phi[0]) / Phi[0], 1e-2, Phi[0] + ", sampled " + sum / count);
        }

        [TEST("GoniometricLight", "Sampling")]
        public static void GoniometricLight_Sampling()
        {
            Image image = MakeLightImage(new Point2i(256, 256));
            image = image.SelectChannels(image.GetChannelDesc(new string[] { "R" }));

            ConstantSpectrum I = new ConstantSpectrum(10.0f);
            Transform id = new Transform();
            GoniometricLight light = new GoniometricLight(id, new MediumInterface(), I, 1.0f, image, new Allocator());
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            testPhiVsSampled(light, ref lambda);
        }

        [TEST("ProjectionLight", "Power")]
        public static void ProjectionLight_Power()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            foreach (Point2i res in new Point2i[] { new Point2i(512, 256), new Point2i(300, 900) })
            {
                Image image = MakeLightImage(res);

                ProjectionLight light = new ProjectionLight(new Transform(), new MediumInterface(), image,
                                                            RGBColorSpace.sRGB, 10 /* scale */, 30 /* fov */,
                                                            new Allocator());

                SampledSpectrum phi = light.Phi(lambda);

                int nSamples = 1024 * 1024;
                double phiSampled = 0;
                foreach (Point2f u in new Hammersley2D(nSamples))
                {
                    Vector3f w = SampleUniformSphere(u);
                    phiSampled += light.I(w, lambda)[0];
                }
                phiSampled /= (nSamples * UniformSpherePDF());

                EXPECT_LT(std.abs(phiSampled - phi[0]), 1e-3,
                          "res: " + res + " qmc: " + phiSampled + ", closed-form: " + phi[0]);
            }
        }

        [TEST("ProjectionLight", "Sampling")]
        public static void ProjectionLight_Sampling()
        {
            foreach (Point2i res in new Point2i[] { new Point2i(512, 256), new Point2i(300, 900) })
            {
                Image image = MakeLightImage(res);

                SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
                ProjectionLight light = new ProjectionLight(new Transform(), new MediumInterface(), image,
                                                            RGBColorSpace.sRGB, 10 /* scale */, 30 /* fov */,
                                                            new Allocator());

                testPhiVsSampled(light, ref lambda);
            }
        }

        [TEST("LightBounds", "Basics")]
        public static void LightBounds_Basics()
        {
            LightBounds bounds = new LightBounds(new Bounds3f(new Point3f(0, 0, 0), new Point3f(.1f, .1f, .01f)),
                                                 new Vector3f(0, 0, 1), 1.0f /* phi */,
                                                 (Float)std.cos(0.0f) /* theta_o: normal spread */,
                                                 (Float)std.cos(Pi / 2) /* theta_e: falloff given visible normal */,
                                                 false /* two-sided */);

            // Positive importance for point on the emissive side
            {
                Interaction intr = new Interaction(new Point3f(1, 1, 1), 0.0f /* time */, (MediumInterface)null);
                EXPECT_GT(bounds.Importance(intr.p(), intr.n), 0);
            }

            // Zero importance for point on the non-emissive side
            {
                Interaction intr = new Interaction(new Point3f(1, 1, -1), 0.0f /* time */, (MediumInterface)null);
                EXPECT_EQ(bounds.Importance(intr.p(), intr.n), 0);
            }

            // Low importance when close to the emitter's plane (since cos theta)
            {
                Interaction intrHigh = new Interaction(new Point3f(1, 1, 1), 0.0f /* time */, (MediumInterface)null);
                Float impHigh = bounds.Importance(intrHigh.p(), intrHigh.n);

                Interaction intrLow = new Interaction(new Point3f(1, 1, .02f), 0.0f /* time */, (MediumInterface)null);
                Float impLow = bounds.Importance(intrLow.p(), intrLow.n);

                EXPECT_LT(impLow / impHigh, .2);
            }
        }
    }
}
