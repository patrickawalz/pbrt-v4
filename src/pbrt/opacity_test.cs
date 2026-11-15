// (not in pbrt-v4) Tests of the presence of a surface (the alpha of a shape, the opacity of its material): whether a point of
// the surface is there is one fact for the ray that meets it, for the light that the point emits and for the light sampler.

using System;
using testing;

namespace pbrt
{
    public static unsafe class OpacityTests
    {
        private static readonly ConstantSpectrum emission = new ConstantSpectrum(2.0f);

        private static DiffuseAreaLight Light(Shape shape, FloatTexture alpha)
        {
            DiffuseAreaLight light = new DiffuseAreaLight(new Transform(), new MediumInterface(), emission, 1, shape, null, new Image(), null, true);
            light.SetPresence(alpha);
            return light;
        }

        // A ray that meets the surface sees its light: the two tests are one. (With pbrt-v4's two hashes, of the ray and of the
        // point, half of the rays that meet a surface of presence 1/2 found it dark.)
        [TEST("Opacity", "ARayThatMeetsTheSurfaceSeesItsLight")]
        public static void ARayThatMeetsTheSurfaceSeesItsLight()
        {
            foreach (Float presence in new Float[] { 0.25f, 0.5f, 0.8f })
            {
                Shape shape = AnimatedLightTests.MakeShape("quad", new Transform());
                FloatTexture alpha = new FloatConstantTexture(presence);
                DiffuseAreaLight light = Light(shape, alpha);
                GeometricPrimitive primitive = new GeometricPrimitive(shape, null, light, new MediumInterface(), alpha);
                RNG rng = new RNG(31);
                SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
                int count = 200000, met = 0, dark = 0;
                for (int i = 0; i < count; ++i)
                {
                    Point3f from = new Point3f(-2 + 4 * rng.Uniform<Float>(), -2 + 4 * rng.Uniform<Float>(), 3);
                    Point3f to = new Point3f(-0.98f + 1.96f * rng.Uniform<Float>(), -0.98f + 1.96f * rng.Uniform<Float>(), 0);
                    Ray ray = new Ray(from, Normalize(to - from));
                    pstd.optional<ShapeIntersection> si = primitive.Intersect(ray);
                    if (!si)
                    {
                        continue;
                    }
                    ++met;
                    SurfaceInteraction intr = si.value().intr;
                    if (!(bool)light.L(intr.p(), intr.n, intr.uv, -ray.d, lambda, 0))
                    {
                        ++dark;
                    }
                }
                double fraction = (double)met / count, sigma = Math.Sqrt(presence * (1 - presence) / count);
                Console.WriteLine("MEASURE presence {0:F2}: {1:F4} of the rays meet the surface (+- {2:F4}); {3} of them find it dark", presence, fraction, sigma, dark);
                EXPECT_EQ(0, dark);
                EXPECT_LT(Math.Abs(fraction - presence), 4 * sigma);
            }
        }

        // The light sampler finds the light with the same probability, and the power is that of the part that is there
        [TEST("Opacity", "TheLightIsSampledAndMeasuredByItsPresence")]
        public static void TheLightIsSampledAndMeasuredByItsPresence()
        {
            foreach (Float presence in new Float[] { 0.25f, 0.5f, 0.8f })
            {
                Shape shape = AnimatedLightTests.MakeShape("quad", new Transform());
                DiffuseAreaLight light = Light(shape, new FloatConstantTexture(presence)), whole = Light(shape, null);
                RNG rng = new RNG(37);
                SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
                LightSampleContext ctx = new LightSampleContext(new Point3fi(new Point3f(0.2f, -0.1f, 2)), new Normal3f(0, 0, -1), new Normal3f(0, 0, -1));
                int count = 200000, found = 0;
                for (int i = 0; i < count; ++i)
                {
                    SampledWavelengths l = lambda;
                    if ((bool)light.SampleLi(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), l, false))
                    {
                        ++found;
                    }
                }
                double fraction = (double)found / count, sigma = Math.Sqrt(presence * (1 - presence) / count);
                double power = light.Phi(lambda)[0] / whole.Phi(lambda)[0];
                Console.WriteLine("MEASURE presence {0:F2}: {1:F4} of the light samples find the light (+- {2:F4}); measured presence {3:F4}, power {4:F4} of the whole",
                                  presence, fraction, sigma, light.Presence(), power);
                EXPECT_LT(Math.Abs(fraction - presence), 4 * sigma);
                EXPECT_LT(Math.Abs(light.Presence() - presence), 1e-5);
                EXPECT_LT(Math.Abs(power - presence), 1e-5);
            }
        }

        // A light that is given its power keeps it when part of it is not there
        [TEST("Opacity", "AGivenPowerIsKept")]
        public static void AGivenPowerIsKept()
        {
            Shape shape = AnimatedLightTests.MakeShape("quad", new Transform());
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            DiffuseAreaLight whole = Light(shape, null), part = Light(shape, null);
            part.fromPower = true;
            part.SetPresence(new FloatConstantTexture(0.3f));
            double ratio = part.Phi(lambda)[0] / whole.Phi(lambda)[0];
            Console.WriteLine("MEASURE a light of given power with presence 0.3: power {0:F5} of the whole", ratio);
            EXPECT_LT(Math.Abs(ratio - 1), 1e-5);
            // and again when the presence is set a second time (the alpha of the shape, then the opacity of the material)
            part.SetPresence(new FloatConstantTexture(0.6f));
            EXPECT_LT(Math.Abs(part.Phi(lambda)[0] / whole.Phi(lambda)[0] - 1), 1e-5);
        }
    }
}
