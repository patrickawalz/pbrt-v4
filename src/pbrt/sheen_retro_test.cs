// (not in pbrt-v4) sheen_retro_test.cs
//
// SGGXSheenBxDF:
//   - nothing is lost with a white albedo: the weights of the samples (undisturbed, reflected, transmitted) have the mean 1;
//   - the undisturbed share is exp(-thickness sigma(w) / |cos w|);
//   - the samples and the evaluation agree, for the reflection and for the scattered transmission;
//   - reciprocity: the single scattering exactly, the whole in the mean (the higher orders are a random walk);
//   - the phase function: its integral over the sphere is 1 and its samples have the weight 1.
// RetroreflectiveBxDF:
//   - the blend of the lobe at the view direction and at its mirror image; rho = 1 sends the light back to where it came from;
//   - the samples and the evaluation agree, and the albedo is that of the lobe;
//   - reciprocity;
//   - a dielectric: the transmission is that of the lobe, the reflection is blended.

using System;
using testing;

namespace pbrt
{
    public static unsafe class SheenRetroTests
    {
        static Vector3f Upper(ref RNG rng, Float least)
        {
            Vector3f w;
            do
            {
                w = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                w.z = std.abs(w.z);
            } while (w.z < least);
            return w;
        }

        [TEST("Sheen", "PhaseFunction")]
        public static void PhaseFunction()
        {
            RNG rng = new RNG(21);
            double worstIntegral = 0, worstMean = 0;
            foreach (Float alpha in new Float[] { 0.15f, 0.5f, 1, 2 })
            {
                SGGXFibrePhase phase = new SGGXFibrePhase(alpha);
                for (int direction = 0; direction < 4; ++direction)
                {
                    Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    // The samples are drawn with the density p: the mean of g(wi) over them is the integral of p g. With
                    // g = 1 / (4 pi p) the integral is 1 whatever p is, as long as the samples cover the sphere
                    const int count = 400000;
                    double integral = 0, cosine = 0, cosineReference = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f wi;
                        if (!phase.Sample_p(wo, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), out wi))
                        {
                            continue;
                        }
                        integral += 1 / (4 * Math.PI * phase.p(wo, wi)) / count;
                        cosine += wi.z * wi.z / count;
                        Vector3f uniform = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        cosineReference += phase.p(wo, uniform) * uniform.z * uniform.z * 4 * Math.PI / count;
                    }
                    worstIntegral = Math.Max(worstIntegral, Math.Abs(integral - 1));
                    worstMean = Math.Max(worstMean, Math.Abs(cosine - cosineReference) / cosineReference);
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE SGGX fibre phase function: the samples cover the sphere with the density p: %.2e; a moment by " +
                                                      "the samples against uniform directions: %.2e", worstIntegral, worstMean));
            EXPECT_LT(worstIntegral, 0.02);
            EXPECT_LT(worstMean, 0.03);
        }

        [TEST("Sheen", "EnergyAndSampling")]
        public static void EnergyAndSampling()
        {
            RNG rng = new RNG(22);
            double worstEnergy = 0, worstUnscattered = 0, worstReflected = 0, worstTransmitted = 0, worstOnce = 0;
            foreach (Float alpha in new Float[] { 0.25f, 0.6f, 1 })
            {
                foreach (Float thickness in new Float[] { 0.3f, 2 })
                {
                    SGGXSheenBxDF white = new SGGXSheenBxDF(new SampledSpectrum(1f), alpha, thickness, 400);
                    SGGXSheenBxDF tinted = new SGGXSheenBxDF(new SampledSpectrum(0.7f), alpha, thickness, 400);
                    // (scattered once only: the closed form against the walk that stops after its first flake)
                    SGGXSheenBxDF once = new SGGXSheenBxDF(new SampledSpectrum(0.7f), alpha, thickness, 1);
                    for (int direction = 0; direction < 3; ++direction)
                    {
                        Vector3f wo = Upper(ref rng, 0.15f);
                        if ((direction & 1) == 1)
                        {
                            wo = -wo;
                        }
                        // (the directions of the integral of f on a grid of the sphere: the lobe is narrow at a small alpha)
                        const int grid = 548;
                        const int count = grid * grid;
                        double total = 0, unscattered = 0, sampledR = 0, sampledT = 0, evaluatedR = 0, evaluatedT = 0, sampledOnce = 0, evaluatedOnce = 0;
                        for (int i = 0; i < count; ++i)
                        {
                            pstd.optional<BSDFSample> ws = white.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                          TransportMode.Radiance, BxDFReflTransFlags.All);
                            if (ws)
                            {
                                double weight = ws.value().f[0] * AbsCosTheta(ws.value().wi) / ws.value().pdf / count;
                                total += weight;
                                if (ws.value().IsSpecular())
                                {
                                    unscattered += weight;
                                    EXPECT_LT(Length(ws.value().wi + wo), 1e-6f);
                                }
                            }
                            pstd.optional<BSDFSample> bs = tinted.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                           TransportMode.Radiance, BxDFReflTransFlags.All);
                            if (bs && !bs.value().IsSpecular())
                            {
                                double weight = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / count;
                                EXPECT_TRUE(bs.value().pdfIsProportional);
                                EXPECT_GT(tinted.PDF(wo, bs.value().wi, TransportMode.Radiance, BxDFReflTransFlags.All), 0);
                                if (SameHemisphere(wo, bs.value().wi))
                                {
                                    sampledR += weight;
                                }
                                else
                                {
                                    sampledT += weight;
                                }
                            }
                            Vector3f wi = SampleUniformSphere(new Point2f(((i % grid) + 0.5f) / grid, ((i / grid) + 0.5f) / grid));
                            double value = tinted.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI / count;
                            if (SameHemisphere(wo, wi))
                            {
                                evaluatedR += value;
                            }
                            else
                            {
                                evaluatedT += value;
                            }
                            evaluatedOnce += once.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI / count;
                            pstd.optional<BSDFSample> os = once.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                         TransportMode.Radiance, BxDFReflTransFlags.All);
                            if (os && !os.value().IsSpecular())
                            {
                                sampledOnce += os.value().f[0] * AbsCosTheta(os.value().wi) / os.value().pdf / count;
                            }
                        }
                        worstOnce = Math.Max(worstOnce, Math.Abs(evaluatedOnce - sampledOnce) / sampledOnce);
                        worstEnergy = Math.Max(worstEnergy, Math.Abs(total - 1));
                        worstUnscattered = Math.Max(worstUnscattered, Math.Abs(unscattered - white.Unscattered(wo)));
                        worstReflected = Math.Max(worstReflected, Math.Abs(evaluatedR - sampledR) / sampledR);
                        worstTransmitted = Math.Max(worstTransmitted, Math.Abs(evaluatedT - sampledT) / sampledT);
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE SGGX sheen: mean weight of the samples with a white albedo - 1: %.2e; undisturbed share against " +
                                                      "exp(-thickness sigma / cos): %.2e; integral of f against the samples: reflected %.2e, transmitted %.2e, " +
                                                      "scattered once %.2e", worstEnergy, worstUnscattered, worstReflected, worstTransmitted, worstOnce));
            EXPECT_LT(worstOnce, 0.01);
            EXPECT_LT(worstEnergy, 5e-3);
            EXPECT_LT(worstUnscattered, 5e-3);
            EXPECT_LT(worstReflected, 0.03);
            EXPECT_LT(worstTransmitted, 0.03);
        }

        [TEST("Sheen", "Reciprocity")]
        public static void Reciprocity()
        {
            RNG rng = new RNG(23);
            // Scattered once: the closed form, the same both ways
            SGGXSheenBxDF once = new SGGXSheenBxDF(new SampledSpectrum(0.8f), 0.4f, 1.2f, 1);
            double worst = 0;
            for (int i = 0; i < 20000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (std.abs(wo.z) < 0.02f || std.abs(wi.z) < 0.02f)
                {
                    continue;
                }
                double forward = once.f(wo, wi, TransportMode.Radiance)[0], backward = once.f(wi, wo, TransportMode.Radiance)[0];
                worst = Math.Max(worst, Math.Abs(forward - backward) / (forward + backward + 1e-4));
            }
            // All orders: equal in the mean over pairs of directions of two patches of the sphere
            SGGXSheenBxDF all = new SGGXSheenBxDF(new SampledSpectrum(0.95f), 0.4f, 1.2f, 200);
            double worstMean = 0;
            for (int patch = 0; patch < 4; ++patch)
            {
                Vector3f centreA = Upper(ref rng, 0.3f), centreB = Upper(ref rng, 0.3f);
                if (patch >= 2)
                {
                    centreB = -centreB;
                }
                double forward = 0, backward = 0;
                const int count = 200000;
                for (int i = 0; i < count; ++i)
                {
                    Vector3f a = Normalize(centreA + 0.2f * SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
                    Vector3f b = Normalize(centreB + 0.2f * SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
                    forward += all.f(a, b, TransportMode.Radiance)[0] / count;
                    backward += all.f(b, a, TransportMode.Radiance)[0] / count;
                }
                worstMean = Math.Max(worstMean, Math.Abs(forward - backward) / (forward + backward));
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE SGGX sheen reciprocity: single scattering %.2e; all orders, in the mean over two patches: %.2e",
                                                      worst, worstMean));
            EXPECT_LT(worst, 1e-4);
            EXPECT_LT(worstMean, 0.01);
        }

        [TEST("Retroreflection", "Conductor")]
        public static void Conductor()
        {
            RNG rng = new RNG(24);
            ConductorBxDF lobe = new ConductorBxDF(new TrowbridgeReitzDistribution(0.25f, 0.4f), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f));
            RetroreflectiveBxDF half = new RetroreflectiveBxDF(lobe, 0.6f);
            RetroreflectiveBxDF full = new RetroreflectiveBxDF(lobe, 1);
            double worstBlend = 0, worstReciprocity = 0, worstDensity = 0;
            for (int i = 0; i < 20000; ++i)
            {
                Vector3f wo = Upper(ref rng, 0.05f), wi = Upper(ref rng, 0.05f);
                Vector3f mirrored = new Vector3f(-wo.x, -wo.y, wo.z);
                double blend = 0.4 * lobe.f(wo, wi, TransportMode.Radiance)[0] + 0.6 * lobe.f(mirrored, wi, TransportMode.Radiance)[0];
                double value = half.f(wo, wi, TransportMode.Radiance)[0];
                worstBlend = Math.Max(worstBlend, Math.Abs(value - blend) / (blend + 1e-3));
                worstReciprocity = Math.Max(worstReciprocity, Math.Abs(value - half.f(wi, wo, TransportMode.Radiance)[0]) / (value + 1e-3));
                double density = 0.4 * lobe.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All) +
                                 0.6 * lobe.PDF(mirrored, wi, TransportMode.Radiance, BxDFReflTransFlags.All);
                worstDensity = Math.Max(worstDensity, Math.Abs(half.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All) - density) / (density + 1e-3));
            }
            // The light comes back: with rho = 1 the lobe is centred on the view direction, not on its mirror image
            Vector3f view = Normalize(new Vector3f(0.5f, 0.2f, 0.8f));
            Vector3f mirror = new Vector3f(-view.x, -view.y, view.z);
            EXPECT_GT(full.f(view, view, TransportMode.Radiance)[0], 20 * full.f(view, mirror, TransportMode.Radiance)[0]);
            EXPECT_GT(lobe.f(view, mirror, TransportMode.Radiance)[0], 20 * lobe.f(view, view, TransportMode.Radiance)[0]);

            double worstSampled = 0, worstAlbedo = 0;
            for (int direction = 0; direction < 4; ++direction)
            {
                Vector3f wo = Upper(ref rng, 0.2f);
                Vector3f mirrored = new Vector3f(-wo.x, -wo.y, wo.z);
                const int count = 300000;
                double sampled = 0, evaluated = 0, own = 0, turned = 0;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<BSDFSample> bs = half.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                 TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (bs)
                    {
                        EXPECT_TRUE(bs.value().pdfIsProportional);
                        // (a proportional sample: the weight of its component, and the density of the mixture from PDF())
                        sampled += bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / count;
                    }
                    Vector3f wi = Upper(ref rng, 0);
                    evaluated += half.f(wo, wi, TransportMode.Radiance)[0] * wi.z * 2 * Math.PI / count;
                    pstd.optional<BSDFSample> ls = lobe.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                 TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (ls)
                    {
                        own += ls.value().f[0] * AbsCosTheta(ls.value().wi) / ls.value().pdf / count;
                    }
                    ls = lobe.Sample_f(mirrored, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Radiance,
                                       BxDFReflTransFlags.All);
                    if (ls)
                    {
                        turned += ls.value().f[0] * AbsCosTheta(ls.value().wi) / ls.value().pdf / count;
                    }
                }
                worstSampled = Math.Max(worstSampled, Math.Abs(sampled - evaluated) / sampled);
                worstAlbedo = Math.Max(worstAlbedo, Math.Abs(sampled - (0.4 * own + 0.6 * turned)) / sampled);
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE retroreflective conductor: f against the blend %.2e, PDF against the mixture %.2e, reciprocity " +
                                                      "%.2e; integral of f against the samples %.2e; albedo against the blend of the lobe's %.2e", worstBlend,
                                                      worstDensity, worstReciprocity, worstSampled, worstAlbedo));
            EXPECT_LT(worstBlend, 1e-5);
            EXPECT_LT(worstDensity, 1e-5);
            EXPECT_LT(worstReciprocity, 1e-3);
            EXPECT_LT(worstSampled, 0.03);
            EXPECT_LT(worstAlbedo, 0.01);
        }

        [TEST("Retroreflection", "Dielectric")]
        public static void Dielectric()
        {
            RNG rng = new RNG(25);
            DielectricBxDF lobe = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.3f, 0.3f));
            RetroreflectiveBxDF retro = new RetroreflectiveBxDF(lobe, 0.5f);
            double worstTransmission = 0, worstBlend = 0;
            for (int i = 0; i < 20000; ++i)
            {
                Vector3f wo = Upper(ref rng, 0.05f);
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (std.abs(wi.z) < 0.05f)
                {
                    continue;
                }
                double value = retro.f(wo, wi, TransportMode.Radiance)[0];
                if (wi.z < 0)
                {
                    worstTransmission = Math.Max(worstTransmission, Math.Abs(value - lobe.f(wo, wi, TransportMode.Radiance)[0]));
                }
                else
                {
                    Vector3f mirrored = new Vector3f(-wo.x, -wo.y, wo.z);
                    double blend = 0.5 * lobe.f(wo, wi, TransportMode.Radiance)[0] + 0.5 * lobe.f(mirrored, wi, TransportMode.Radiance)[0];
                    worstBlend = Math.Max(worstBlend, Math.Abs(value - blend) / (blend + 1e-3));
                }
            }
            double worstReflected = 0, worstTransmitted = 0, worstDensity = 0;
            for (int direction = 0; direction < 4; ++direction)
            {
                Vector3f wo = Upper(ref rng, 0.25f);
                const int count = 400000;
                double sampledR = 0, sampledT = 0, lobeR = 0, lobeT = 0, density = 0;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<BSDFSample> bs = retro.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                  TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (bs)
                    {
                        double weight = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / count;
                        if (bs.value().IsTransmission())
                        {
                            sampledT += weight;
                        }
                        else
                        {
                            sampledR += weight;
                        }
                        // The density of the sample is the mixture's
                        if (i < 3000)
                        {
                            Float mixture = retro.PDF(wo, bs.value().wi, TransportMode.Radiance, BxDFReflTransFlags.All);
                            EXPECT_GT(mixture, 0);
                            if (bs.value().IsTransmission())
                            {
                                density = Math.Max(density, Math.Abs(mixture - bs.value().pdf) / bs.value().pdf);
                            }
                        }
                    }
                    pstd.optional<BSDFSample> ls = lobe.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                 TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (ls && ls.value().pdf > 0)
                    {
                        double weight = ls.value().f[0] * AbsCosTheta(ls.value().wi) / ls.value().pdf / count;
                        if (ls.value().IsTransmission())
                        {
                            lobeT += weight;
                        }
                        else
                        {
                            lobeR += weight;
                        }
                    }
                }
                // (the lobe is isotropic: its reflectance is the same at the mirrored direction)
                worstReflected = Math.Max(worstReflected, Math.Abs(sampledR - lobeR) / lobeR);
                worstTransmitted = Math.Max(worstTransmitted, Math.Abs(sampledT - lobeT) / lobeT);
                worstDensity = Math.Max(worstDensity, density);
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE retroreflective dielectric: transmission against the lobe %.2e, reflection against the blend " +
                                                      "%.2e; samples against those of the lobe: reflected %.2e, transmitted %.2e; density of the transmitted " +
                                                      "samples against PDF() %.2e", worstTransmission, worstBlend, worstReflected, worstTransmitted, worstDensity));
            EXPECT_LT(worstTransmission, 1e-6);
            EXPECT_LT(worstBlend, 1e-5);
            EXPECT_LT(worstReflected, 0.03);
            EXPECT_LT(worstTransmitted, 0.01);
            EXPECT_LT(worstDensity, 1e-4);
        }
    }
}
