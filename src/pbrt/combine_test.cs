// (not in pbrt-v4) combine_test.cs
//
// The operations of combine.cs:
//   - a mix is the weighted sum of its lobes, reciprocal, and its samples and its density agree with it;
//   - a stack of a dielectric on a diffuse base is the coated diffuse of pbrt;
//   - an absorbing medium under a clear interface follows Beer's law by wavelength: albedo R exp(-d / mu) 2 E3(d);
//   - a scattering medium limited to one scattering returns the single scattering of a slab (Chandrasekhar), and more with
//     every scattering;
//   - the Mueller matrices of a mix and of a stack: sampled paths against evaluated directions.

using System;
using testing;

namespace pbrt
{
    public static unsafe class CombineTests
    {
        static SampledSpectrum S(Float v) { return new SampledSpectrum(v); }

        internal static void Albedo(BxDF bxdf, Vector3f wo, int count, ref RNG rng, out double albedo, out double error)
        {
            Albedo(bxdf, wo, count, ref rng, out albedo, out error, TransportMode.Radiance);
        }

        internal static void Albedo(BxDF bxdf, Vector3f wo, int count, ref RNG rng, out double albedo, out double error, TransportMode mode)
        {
            double sum = 0, squares = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), mode,
                                                             BxDFReflTransFlags.All);
                if (!bs || bs.value().pdf == 0)
                {
                    continue;
                }
                double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                sum += w;
                squares += w * w;
            }
            albedo = sum / count;
            error = Math.Sqrt(Math.Max(0.0, squares / count - albedo * albedo) / count);
        }

        internal static Vector3f Direction(Float cosine, Float phi)
        {
            Float sine = SafeSqrt(1 - cosine * cosine);
            return new Vector3f(sine * (Float)Math.Cos(phi), sine * (Float)Math.Sin(phi), cosine);
        }

        [TEST("Combine", "Mix")]
        public static void Mix()
        {
            BxDF diffuse = new DiffuseBxDF(S(0.8f));
            BxDF metal = new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), S(0.2f), S(3.5f));
            BxDF mix = new MixBxDF(new BxDF[] { diffuse, metal }, new Float[] { 0.3f, 0.7f });
            RNG rng = new RNG(1);
            double worst = 0;
            for (int i = 0; i < 1000; ++i)
            {
                Vector3f wo = FacetForgePolarisedTests.Upper(ref rng), wi = FacetForgePolarisedTests.Upper(ref rng);
                double expected = 0.3 * diffuse.f(wo, wi, TransportMode.Radiance)[0] + 0.7 * metal.f(wo, wi, TransportMode.Radiance)[0];
                worst = Math.Max(worst, Math.Abs(mix.f(wo, wi, TransportMode.Radiance)[0] - expected));
            }
            EXPECT_LT(worst, 1e-5);
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.bothSides = false;
            BxDFCheckResult result = BxDFCheck.Run(mix, "Mix of a diffuse and a rough conductor", options);
            BxDFCheck.Expect(result, "Mix of a diffuse and a rough conductor", 2e-3, 1.005, 5, 0.9);
            // a mix that holds a random walk, and one that holds a specular lobe
            BxDF walked = MixBxDF.Of(new CoatedDiffuseBxDF(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.2f, 0.2f)), new DiffuseBxDF(S(0.7f)), 0.01f, S(0f),
                                                           0, 16, 1), metal, 0.5f);
            options.repeats = 16;
            options.pairs = 300;
            options.samples = 60000;
            BxDFCheckResult second = BxDFCheck.Run(walked, "Mix of a coated diffuse and a rough conductor", options);
            EXPECT_EQ(0, second.invalid);
            EXPECT_LT(second.albedoMax, 1.0);
            EXPECT_TRUE(second.albedoSigmas < 5 || second.albedoRelative < 1e-2, "Sample_f and f disagree about the albedo");
            BxDF mirror = new ConductorBxDF(new TrowbridgeReitzDistribution(0f, 0f), S(0.2f), S(3.5f));
            BxDF withMirror = MixBxDF.Of(diffuse, mirror, 0.4f);
            foreach (Float cosine in new Float[] { 0.9f, 0.4f })
            {
                Vector3f wo = Direction(cosine, 0.3f);
                double a, e, aDiffuse, eDiffuse, aMirror, eMirror;
                Albedo(withMirror, wo, 200000, ref rng, out a, out e);
                Albedo(diffuse, wo, 1000, ref rng, out aDiffuse, out eDiffuse);
                Albedo(mirror, wo, 1000, ref rng, out aMirror, out eMirror);
                Console.WriteLine("MEASURE mix of a diffuse and a mirror, cosine {0}: albedo {1:F4} +- {2:F4}, expected {3:F4}", cosine, a, e, 0.6 * aDiffuse + 0.4 * aMirror);
                EXPECT_LT(Math.Abs(a - (0.6 * aDiffuse + 0.4 * aMirror)), 4 * e + 1e-4);
            }
        }

        [TEST("Combine", "StackIsTheCoatedDiffuse")]
        public static void StackIsTheCoatedDiffuse()
        {
            RNG rng = new RNG(2);
            BxDF stack = new StackBxDF(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.2f, 0.2f)), new DiffuseBxDF(S(0.7f)), 0.01f, S(0f), 0, 16, 1, true);
            BxDF coated = new CoatedDiffuseBxDF(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.2f, 0.2f)), new DiffuseBxDF(S(0.7f)), 0.01f, S(0f), 0, 16, 1);
            foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
            {
                Vector3f wo = Direction(cosine, 1.1f);
                double a, e, b, eb;
                Albedo(stack, wo, 200000, ref rng, out a, out e);
                Albedo(coated, wo, 200000, ref rng, out b, out eb);
                Console.WriteLine("MEASURE stack against the coated diffuse of pbrt, cosine {0}: {1:F4} / {2:F4} (+- {3:F4})", cosine, a, b, Math.Sqrt(e * e + eb * eb));
                EXPECT_LT(Math.Abs(a - b), 4 * Math.Sqrt(e * e + eb * eb));
            }
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.repeats = 16;
            options.pairs = 300;
            options.samples = 60000;
            BxDFCheckResult result = BxDFCheck.Run(stack, "Stack of a rough dielectric on a diffuse base", options);
            EXPECT_EQ(0, result.invalid);
            EXPECT_LT(result.albedoMax, 1.0);
            EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 1e-2, "Sample_f and f disagree about the albedo");
        }

        [TEST("Combine", "AbsorbingMedium")]
        public static void AbsorbingMedium()
        {
            RNG rng = new RNG(3);
            SampledSpectrum depth = new SampledSpectrum(0f);
            depth[0] = 0.4f;
            for (int c = 1; c < NSpectrumSamples; ++c)
            {
                depth[c] = 1.5f;
            }
            BxDF stack = StackBxDF.Absorbing(new OSLTransparentBxDF(), new DiffuseBxDF(S(0.8f)), depth, 16, 1, true);
            // 2 E3(d) = 2 int_0^1 exp(-d / mu) mu dmu
            double e3 = 0;
            int steps = 200000;
            for (int i = 0; i < steps; ++i)
            {
                double mu = (i + 0.5) / steps;
                e3 += 2 * Math.Exp(-0.4 / mu) * mu / steps;
            }
            foreach (Float cosine in new Float[] { 0.95f, 0.5f, 0.2f })
            {
                Vector3f wo = Direction(cosine, 2.1f);
                double a, e;
                Albedo(stack, wo, 300000, ref rng, out a, out e);
                double expected = 0.8 * Math.Exp(-0.4 / cosine) * e3;
                Console.WriteLine("MEASURE absorbing medium under a clear interface, cosine {0}: albedo {1:F5} +- {2:F5}, Beer's law {3:F5}", cosine, a, e, expected);
                EXPECT_LT(Math.Abs(a - expected), 4 * e + 2e-4);
            }
            // by evaluation too
            Vector3f o = Direction(0.7f, 0.2f), i2 = Direction(0.6f, 2.5f);
            double sum = 0;
            int count = 4000;
            for (int s = 0; s < count; ++s)
            {
                Vector3f d = 1e-5f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                sum += stack.f(Normalize(o + d), Normalize(i2 - d), TransportMode.Radiance)[0];
            }
            double exact = 0.8 / Math.PI * Math.Exp(-0.4 / 0.7 - 0.4 / 0.6);
            Console.WriteLine("MEASURE absorbing medium, f: {0:F5} against {1:F5}", sum / count, exact);
            EXPECT_LT(Math.Abs(sum / count - exact), 0.01 * exact);
        }

        [TEST("Combine", "OneScatteringInTheMedium")]
        public static void OneScatteringInTheMedium()
        {
            RNG rng = new RNG(4);
            foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.25f })
            {
                StackBxDF once = new StackBxDF(new OSLTransparentBxDF(), new DiffuseBxDF(S(0f)), 1f, S(0.9f), 0, 64, 1, true);
                once.mediumOrder = 1;
                StackBxDF every = new StackBxDF(new OSLTransparentBxDF(), new DiffuseBxDF(S(0f)), 1f, S(0.9f), 0, 64, 1, true);
                Vector3f wo = Direction(cosine, 0.9f);
                double a, e, b, eb;
                Albedo(once, wo, 300000, ref rng, out a, out e);
                Albedo(every, wo, 300000, ref rng, out b, out eb);
                // (albedo / 2) int_0^1 mu / (mu0 + mu) (1 - exp(-tau (1 / mu0 + 1 / mu))) dmu
                double expected = 0;
                int steps = 200000;
                for (int i = 0; i < steps; ++i)
                {
                    double mu = (i + 0.5) / steps;
                    expected += 0.45 * mu / (cosine + mu) * (1 - Math.Exp(-(1 / cosine + 1 / mu))) / steps;
                }
                Console.WriteLine("MEASURE slab of optical thickness 1 and albedo 0.9, cosine {0}: one scattering {1:F5} +- {2:F5} (Chandrasekhar {3:F5}); every " +
                                  "scattering {4:F5}", cosine, a, e, expected, b);
                EXPECT_LT(Math.Abs(a - expected), 4 * e + 2e-4);
                EXPECT_GT(b, a + 0.02);
            }
        }

        // The estimate of pbrt's layered walk from the two ends of a pair of directions
        [TEST("Combine", "ReciprocityOfTheStack")]
        public static void ReciprocityOfTheStack()
        {
            RNG rng = new RNG(31);
            ConductorBxDF metal = new ConductorBxDF(new TrowbridgeReitzDistribution(0.09f, 0.09f), S(0.6f), S(2.5f));
            string[] names = new string[] { "smooth coat on a rough conductor", "rough coat on a rough conductor", "rough coat on a diffuse base" };
            foreach (bool both in new bool[] { false, true })
            {
                StackBxDF[] stacks = new StackBxDF[] {
                    new StackBxDF(new DielectricBxDF(1.6f, new TrowbridgeReitzDistribution(0f, 0f)), metal, 0.01f, S(0f), 0, 32, 1, true),
                    new StackBxDF(new DielectricBxDF(1.6f, new TrowbridgeReitzDistribution(0.2f, 0.2f)), metal, 0.01f, S(0f), 0, 32, 1, true),
                    new StackBxDF(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.2f, 0.2f)), new DiffuseBxDF(S(0.8f)), 0.01f, S(0f), 0, 32, 1, true) };
                for (int c = 0; c < stacks.Length; ++c)
                {
                    stacks[c].reciprocal = both;
                    double worst = 0, mean = 0, worstSigma = 0;
                    Vector3f[] a = new Vector3f[] { Direction(0.95f, 0.2f), Direction(0.8f, 1.0f), Direction(0.9f, 4.0f), Direction(0.3f, 0.5f) };
                    Vector3f[] b = new Vector3f[] { Direction(0.25f, 3.0f), Direction(0.5f, 4.2f), Direction(0.15f, 0.9f), Direction(0.35f, 3.5f) };
                    for (int p = 0; p < a.Length; ++p)
                    {
                        double x = 0, y = 0, x2 = 0, y2 = 0;
                        int count = 40000;
                        for (int i = 0; i < count; ++i)
                        {
                            Vector3f d = 1e-5f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                            Vector3f o = Normalize(a[p] + d), i2 = Normalize(b[p] - d);
                            double u = stacks[c].f(o, i2, TransportMode.Radiance)[0], v = stacks[c].f(i2, o, TransportMode.Importance)[0];
                            x += u; x2 += u * u; y += v; y2 += v * v;
                        }
                        x /= count; y /= count;
                        double error = Math.Sqrt((Math.Max(0, x2 / count - x * x) + Math.Max(0, y2 / count - y * y)) / count);
                        double relative = Math.Abs(x - y) / Math.Max(x, y);
                        worst = Math.Max(worst, relative);
                        worstSigma = Math.Max(worstSigma, Math.Abs(x - y) / error);
                        mean += relative / a.Length;
                    }
                    Console.WriteLine("MEASURE stack, {0}{1}: f(wo, wi) against f(wi, wo), worst {2:P2} of f ({3:F1} sigma), mean {4:P2}", names[c],
                                      both ? ", from both directions" : "", worst, worstSigma, mean);
                    if (both)
                    {
                        EXPECT_LT(worstSigma, 4.5, names[c]);
                    }
                }
            }
        }

        [TEST("Combine", "Polarised")]
        public static void Polarised()
        {
            BxDF metal = new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), S(0.2f), S(3.5f));
            FacetForgePolarisedTests.SampledAgainstEvaluated(MixBxDF.Of(new DiffuseBxDF(S(0.8f)), metal, 0.6f), "mix of a diffuse and a rough conductor", 200000);
            FacetForgePolarisedTests.SampledAgainstEvaluated(new StackBxDF(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.2f, 0.2f)), metal, 0.01f, S(0f), 0, 16,
                                                                           1, true), "stack of a rough dielectric on a rough conductor", 200000);
            FacetForgePolarisedTests.SampledAgainstEvaluated(new TintedBxDF(metal, S(0.5f), S(1f)), "tinted rough conductor", 100000);
            // The specular lobes that have no matrix of their own, held by a mix: against the same lobe alone, as the integrator
            // builds its matrix
            RNG rng = new RNG(41);
            BxDF sheet = new ThinDielectricBxDF(1.5f);
            BxDF held = new MixBxDF(new BxDF[] { sheet }, new Float[] { 1f });
            BSDF alone = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), sheet), inMix = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), held);
            SurfaceInteraction surface = new SurfaceInteraction();
            surface.shading.n = new Normal3f(0, 0, 1);
            surface.n = surface.shading.n;
            double worst = 0;
            int compared = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f wo = FacetForgePolarisedTests.Upper(ref rng);
                Float uc = rng.Uniform<Float>();
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<BSDFSample> bs = alone.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                PolarisedLightSystem.Attenuation reference, A;
                bool polarised;
                pstd.optional<BSDFSample> bm = inMix.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All, out polarised, out A);
                if (!bs || !bm || !polarised || !PolarisedPathIntegrator.SpecularAttenuation(alone, surface, wo, bs.value(), TransportMode.Radiance, out reference))
                {
                    continue;
                }
                A = A * (bm.value().f * AbsCosTheta(bm.value().wi) / bm.value().pdf);
                worst = Math.Max(worst, FacetForgePolarisedTests.Difference(reference.GetMuellerMatrix(), A.GetMuellerMatrix()));
                ++compared;
            }
            Console.WriteLine("MEASURE polarised thin dielectric held by a mix against the lobe alone: worst element {0:E1} ({1} samples)", worst, compared);
            EXPECT_GT(compared, 1500);
            EXPECT_LT(worst, 1e-4);
        }
    }
}
