// (not in pbrt-v4) pearlescent_test.cs
//
// The pearlescent BxDF (PearlescentBxDF, util/pearlescent.cs):
//   - the reflectance of a platelet, the mean over the thickness of its substrate in closed form, against the
//     characteristic matrix of the same three films (FrThinFilmPolarised) averaged over the thickness by quadrature,
//     for both polarisations and the phase between them;
//   - energy: with a white base and platelets that do not absorb, all the light comes back;
//   - f against Sample_f: the albedo of the slab by both, with every event and with single scattering only (the closed form);
//   - reciprocity;
//   - the Mueller matrices: a weight of one, physical, the matrix of a sampled single reflection is that of f.

using System;
using testing;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public static unsafe class PearlescentTests
    {
        static SampledSpectrum S(Float v)
        {
            return new SampledSpectrum(v);
        }

        static SampledSpectrum Wavelengths()
        {
            SampledSpectrum wavelengths = new SampledSpectrum(0f);
            wavelengths[0] = 450;
            wavelengths[1] = 520;
            wavelengths[2] = 590;
            wavelengths[3] = 650;
            return wavelengths;
        }

        static PearlescentStack Stack(double containerEta, double coatingThickness, double substrateEta, double substrateAbbe, double substrateThickness,
                                      double substrateDeviation)
        {
            PearlescentStack stack = new PearlescentStack();
            stack.containerEta = containerEta;
            stack.coatingEta = 2.5;
            stack.coatingAbbe = 9;
            stack.coatingThickness = coatingThickness;
            stack.substrateEta = substrateEta;
            stack.substrateAbbe = substrateAbbe;
            stack.substrateThickness = substrateThickness;
            stack.substrateDeviation = substrateDeviation;
            return stack;
        }

        // The same platelet by the characteristic matrix, with the mean over the thickness by quadrature
        static void Reference(double cosContainer, double wavelength, PearlescentStack stack, out double Fs, out double Fp, out double C, out double S)
        {
            Float coating = (Float)Pearlescent.Cauchy(stack.coatingEta, stack.coatingAbbe, wavelength);
            Float substrate = (Float)Pearlescent.Cauchy(stack.substrateEta, stack.substrateAbbe, wavelength);
            Float[] layerEta = new Float[] { coating, substrate, coating };
            Fs = 0;
            Fp = 0;
            C = 0;
            S = 0;
            int steps = stack.substrateDeviation > 0 ? 4001 : 1;
            double total = 0;
            for (int i = 0; i < steps; ++i)
            {
                double x = steps > 1 ? -6 + 12.0 * i / (steps - 1) : 0;
                double weight = Math.Exp(-0.5 * x * x);
                Float[] layerThickness = new Float[] { (Float)stack.coatingThickness, (Float)(stack.substrateThickness + x * stack.substrateDeviation),
                                                       (Float)stack.coatingThickness };
                pbrt.FrThinFilmPolarised((Float)cosContainer, (Float)stack.containerEta, layerEta, layerThickness, (Float)wavelength,
                                         new pstd.complex((Float)stack.containerEta), out Float fs, out Float fp, out Float ds, out Float dp);
                double root = Math.Sqrt((double)fs * fp);
                Fs += weight * fs;
                Fp += weight * fp;
                C += weight * root * Math.Cos(ds - dp);
                S += weight * root * Math.Sin(ds - dp);
                total += weight;
            }
            Fs /= total;
            Fp /= total;
            C /= total;
            S /= total;
        }

        [TEST("Pearlescent", "PlateletAgainstCharacteristicMatrix")]
        public static void Pearlescent_PlateletAgainstCharacteristicMatrix()
        {
            string[] names = new string[] { "mica 500 +- 60 nm in 1.57", "mica 560 +- 179 nm in 1.57", "mica 500 nm, one thickness", "silica 80 nm in 1.555 (tunnels)",
                                            "silica 300 +- 30 nm in 1.555" };
            PearlescentStack[] stacks = new PearlescentStack[] { Stack(1.57, 60, 1.58, 63, 500, 60), Stack(1.57, 90, 1.58, 63, 560, 179),
                                                                 Stack(1.57, 60, 1.58, 63, 500, 0), Stack(1.555, 120, 1.4585, 67.8, 80, 0),
                                                                 Stack(1.555, 120, 1.4585, 67.8, 300, 30) };
            for (int index = 0; index < stacks.Length; ++index)
            {
                double worst = 0, worstPhase = 0, worstUnpolarised = 0, leastCoherence = 1;
                for (int ic = 0; ic <= 60; ++ic)
                {
                    double cosine = 0.02 + 0.98 * ic / 60;
                    for (double wavelength = 380; wavelength <= 780; wavelength += 20)
                    {
                        double critical = stacks[index].containerEta * Math.Sqrt(1 - cosine * cosine) / Pearlescent.Cauchy(stacks[index].substrateEta, stacks[index].substrateAbbe, wavelength);
                        if (stacks[index].substrateDeviation > 0 && critical > 0.98)
                        {
                            // Beyond the critical angle the closed form takes the mean thickness: compared in the stack of one thickness
                            continue;
                        }
                        Pearlescent.Reflectance(cosine, wavelength, stacks[index], out double Fs, out double Fp, out double C, out double S);
                        Reference(cosine, wavelength, stacks[index], out double refFs, out double refFp, out double refC, out double refS);
                        worst = Math.Max(worst, Math.Max(Math.Abs(Fs - refFs), Math.Abs(Fp - refFp)));
                        worstPhase = Math.Max(worstPhase, Math.Max(Math.Abs(C - refC), Math.Abs(S - refS)));
                        worstUnpolarised = Math.Max(worstUnpolarised, Math.Abs(Pearlescent.Reflectance(cosine, wavelength, stacks[index]) - 0.5 * (refFs + refFp)));
                        if (Fs * Fp > 1e-4)
                        {
                            leastCoherence = Math.Min(leastCoherence, Math.Sqrt((C * C + S * S) / (Fs * Fp)));
                        }
                    }
                }
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent platelet %s: worst error of Fs, Fp %.2e, of the phase terms %.2e, unpolarised %.2e; " +
                                                          "least |<rs rp*>| / sqrt(Fs Fp) %.3f", names[index], worst, worstPhase, worstUnpolarised, leastCoherence));
                EXPECT_LT(worst, 2e-4, names[index]);
                EXPECT_LT(worstPhase, 2e-4, names[index]);
                EXPECT_LT(worstUnpolarised, 2e-4, names[index]);
            }
        }

        static PearlescentBxDF Slab(PearlescentStack stack, Float kappa, Float alpha, Float albedo, int maxEvents, int quadrature = 16)
        {
            return new PearlescentBxDF(stack, Wavelengths(), kappa, alpha, alpha, S(albedo), quadrature, maxEvents);
        }

        // The albedo by Sample_f: the mirror part and the slab, and the standard error of the slab
        static void SampledAlbedo(BxDF bxdf, Vector3f wo, int samples, ref RNG rng, out double mirror, out double slab, out double error)
        {
            double sumMirror = 0, sum = 0, sum2 = 0;
            for (int i = 0; i < samples; ++i)
            {
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                             TransportMode.Radiance, BxDFReflTransFlags.All);
                if (!bs)
                {
                    continue;
                }
                double value = bs.value().f.Average() * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                if (bs.value().IsSpecular())
                {
                    sumMirror += value;
                }
                else
                {
                    sum += value;
                    sum2 += value * value;
                }
            }
            mirror = sumMirror / samples;
            slab = sum / samples;
            error = Math.Sqrt(Math.Max(0, sum2 / samples - slab * slab) / samples);
        }

        // The albedo of f (the slab) over uniform directions
        static void EvaluatedAlbedo(BxDF bxdf, Vector3f wo, int samples, ref RNG rng, out double slab, out double error)
        {
            double sum = 0, sum2 = 0;
            for (int i = 0; i < samples; ++i)
            {
                Vector3f wi = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                double value = bxdf.f(wo, wi, TransportMode.Radiance).Average() * AbsCosTheta(wi) * 2 * Pi;
                sum += value;
                sum2 += value * value;
            }
            slab = sum / samples;
            error = Math.Sqrt(Math.Max(0, sum2 / samples - slab * slab) / samples);
        }

        [TEST("Pearlescent", "Energy")]
        public static void Pearlescent_Energy()
        {
            RNG rng = new RNG(5);
            PearlescentStack stack = Stack(1.57, 60, 1.58, 63, 500, 60);
            foreach (Float kappa in new Float[] { 0.5f, 3f, 20f })
            {
                foreach (Float cosine in new Float[] { 0.95f, 0.5f, 0.15f })
                {
                    Vector3f wo = new Vector3f(std.sqrt(1 - cosine * cosine), 0, cosine);
                    SampledAlbedo(Slab(stack, kappa, 0.2f, 1f, 1000000), wo, 100000, ref rng, out double mirror, out double slab, out double error);
                    SampledAlbedo(Slab(stack, kappa, 0.2f, 1f, 64), wo, 100000, ref rng, out double mirror64, out double slab64, out double error64);
                    Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent white base, kappa %.1f, cos %.2f: mirror %.4f (Fresnel %.4f), slab %.4f +- %.4f, " +
                                                              "total %.4f; with 64 events %.4f", kappa, cosine, mirror, FrDielectric(cosine, 1.57f), slab, error,
                                                              mirror + slab, mirror64 + slab64));
                    EXPECT_LT(Math.Abs(mirror + slab - 1), Math.Max(5 * error, 5e-3), "energy is not conserved");
                    EXPECT_LT(mirror64 + slab64, 1 + Math.Max(5 * error64, 5e-3), "energy is made");
                }
            }
        }

        [TEST("Pearlescent", "SampledAgainstEvaluated")]
        public static void Pearlescent_SampledAgainstEvaluated()
        {
            RNG rng = new RNG(6);
            PearlescentStack stack = Stack(1.57, 60, 1.58, 63, 500, 60);
            foreach (int maxEvents in new int[] { 64, 1 })
            {
                foreach (Float alpha in new Float[] { 0.3f, 0.1f })
                {
                    foreach (Float kappa in new Float[] { 0.5f, 3f })
                    {
                        foreach (Float cosine in new Float[] { 0.9f, 0.4f })
                        {
                            Vector3f wo = new Vector3f(std.sqrt(1 - cosine * cosine), 0, cosine);
                            BxDF bxdf = Slab(stack, kappa, alpha, 0.5f, maxEvents);
                            SampledAlbedo(bxdf, wo, 200000, ref rng, out double mirror, out double sampled, out double sampledError);
                            EvaluatedAlbedo(bxdf, wo, 200000, ref rng, out double evaluated, out double evaluatedError);
                            if (maxEvents == 1)
                            {
                                // What a coarser quadrature of the effective extinction does to the closed form (a measurement)
                                EvaluatedAlbedo(Slab(stack, kappa, alpha, 0.5f, maxEvents, 4), wo, 200000, ref rng, out double coarse, out double coarseError);
                                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent   with 4 quadrature samples: by f %.4f +- %.4f", coarse, coarseError));
                            }
                            if (maxEvents == 64 && alpha == 0.3f && kappa == 3f)
                            {
                                // The same with a finer quadrature: what is left of the difference is the quadrature's
                                EvaluatedAlbedo(Slab(stack, kappa, alpha, 0.5f, maxEvents, 64), wo, 200000, ref rng, out double fine, out double fineError);
                                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent   with 64 quadrature samples: by f %.4f +- %.4f", fine, fineError));
                            }
                            double error = Math.Sqrt(sampledError * sampledError + evaluatedError * evaluatedError);
                            double sigmas = Math.Abs(sampled - evaluated) / error, relative = Math.Abs(sampled - evaluated) / sampled;
                            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent events %d alpha %.1f kappa %.1f cos %.1f: albedo of the slab by Sample_f %.4f, " +
                                                                      "by f %.4f (%.1f sigma, %.2e)", maxEvents, alpha, kappa, cosine, sampled, evaluated, sigmas, relative));
                            EXPECT_TRUE(sigmas < 5 || relative < 1e-2, "Sample_f and f disagree about the albedo");
                        }
                    }
                }
            }
        }

        [TEST("Pearlescent", "Reciprocity")]
        public static void Pearlescent_Reciprocity()
        {
            RNG rng = new RNG(99);
            PearlescentStack stack = Stack(1.57, 60, 1.58, 63, 500, 60);
            foreach (int maxEvents in new int[] { 1, 64 })
            {
                BxDF bxdf = Slab(stack, 3f, 0.2f, 0.5f, maxEvents);
                int repeats = maxEvents == 1 ? 1 : 20000;
                double worstZ = 0, worstRelative = 0;
                for (int pair = 0; pair < 12; ++pair)
                {
                    Vector3f wo = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.15f + 0.8f * rng.Uniform<Float>()));
                    Vector3f wi = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.15f + 0.8f * rng.Uniform<Float>()));
                    double sumA = 0, sumA2 = 0, sumB = 0, sumB2 = 0;
                    for (int i = 0; i < repeats; ++i)
                    {
                        Vector3f d = repeats > 1 ? 1e-4f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f)
                                                 : new Vector3f(0, 0, 0);
                        Vector3f a = Normalize(wo + d), b = Normalize(wi - d);
                        double x = bxdf.f(a, b, TransportMode.Radiance).Average();
                        double y = bxdf.f(b, a, TransportMode.Importance).Average();
                        sumA += x;
                        sumA2 += x * x;
                        sumB += y;
                        sumB2 += y * y;
                    }
                    double meanA = sumA / repeats, meanB = sumB / repeats;
                    double error = Math.Sqrt((sumA2 / repeats - meanA * meanA + sumB2 / repeats - meanB * meanB) / repeats);
                    if (repeats > 1)
                    {
                        worstZ = Math.Max(worstZ, Math.Abs(meanA - meanB) / error);
                    }
                    worstRelative = Math.Max(worstRelative, Math.Abs(meanA - meanB) / Math.Max(meanA, meanB));
                }
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent events %d: reciprocity of 12 pairs, %d estimates each: worst %.1f sigma, worst relative " +
                                                          "difference %.2e", maxEvents, repeats, worstZ, worstRelative));
                if (maxEvents == 1)
                {
                    EXPECT_LT(worstRelative, 1e-4, "the closed form is not reciprocal");
                }
                else
                {
                    EXPECT_LT(worstZ, 4, "not reciprocal");
                }
            }
        }

        // The normals of the platelets from a MicrofacetDistribution:
        //   - Trowbridge-Reitz through the interface is the Trowbridge-Reitz that the BxDF writes out: the closed form for
        //     the same directions, and the albedo with every event;
        //   - every distribution conserves energy over a white base, has an f that agrees with Sample_f, and is reciprocal.
        [TEST("Pearlescent", "Distributions")]
        public static void Pearlescent_Distributions()
        {
            RNG rng = new RNG(31);
            PearlescentStack stack = Stack(1.57, 60, 1.58, 63, 500, 60);
            // Against the written-out distribution
            foreach (Float alpha in new Float[] { 0.2f, 0.05f })
            {
                // (with 1024 points: the two quadratures are not the same points, and that of the interface converges slower)
                BxDF written = Slab(stack, 3f, alpha, 0.5f, 1, 1024);
                BxDF general = new PearlescentBxDF(stack, Wavelengths(), 3f, new TrowbridgeReitzDistribution(alpha, alpha), S(0.5f), 1024, 1);
                double worst = 0;
                int compared = 0;
                for (int i = 0; i < 300; ++i)
                {
                    Vector3f wo = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.1f + 0.9f * rng.Uniform<Float>()));
                    Vector3f wi = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.1f + 0.9f * rng.Uniform<Float>()));
                    if (i % 2 == 1)
                    {
                        // Near the mirror direction, where the platelets reflect
                        wi = Normalize(new Vector3f(-wo.x, -wo.y, wo.z) + 2 * alpha * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0));
                    }
                    SampledSpectrum a = written.f(wo, wi, TransportMode.Radiance), b = general.f(wo, wi, TransportMode.Radiance);
                    for (int j = 0; j < NSpectrumSamples; ++j)
                    {
                        worst = Math.Max(worst, Math.Abs(a[j] - b[j]) / Math.Max(a[j], 1e-3f));
                    }
                    ++compared;
                }
                // A slab so thin that nothing is hidden: f is kappa D R / (4 cos cos), with no quadrature in it
                BxDF thinWritten = Slab(stack, 1e-3f, alpha, 0f, 1);
                BxDF thinGeneral = new PearlescentBxDF(stack, Wavelengths(), 1e-3f, new TrowbridgeReitzDistribution(alpha, alpha), S(0f), 16, 1);
                double worstThin = 0;
                for (int i = 0; i < 2000; ++i)
                {
                    Vector3f wo = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.1f + 0.9f * rng.Uniform<Float>()));
                    Vector3f wi = Normalize(new Vector3f(-wo.x, -wo.y, wo.z) + 2 * alpha * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0));
                    SampledSpectrum a = thinWritten.f(wo, wi, TransportMode.Radiance), b = thinGeneral.f(wo, wi, TransportMode.Radiance);
                    for (int j = 0; j < NSpectrumSamples; ++j)
                    {
                        if (a[j] > 1e-8f)
                        {
                            worstThin = Math.Max(worstThin, Math.Abs(a[j] - b[j]) / a[j]);
                        }
                    }
                }
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent Trowbridge-Reitz %.2f, thin slab (no quadrature): worst relative difference %.2e", alpha, worstThin));
                EXPECT_LT(worstThin, 1e-3, "D differs");
                Vector3f view = new Vector3f(0.6f, 0, 0.8f);
                SampledAlbedo(Slab(stack, 3f, alpha, 0.5f, 64), view, 200000, ref rng, out double mirrorA, out double slabA, out double errorA);
                SampledAlbedo(new PearlescentBxDF(stack, Wavelengths(), 3f, new TrowbridgeReitzDistribution(alpha, alpha), S(0.5f), 16, 64), view, 200000,
                              ref rng, out double mirrorB, out double slabB, out double errorB);
                double sigmas = Math.Abs(slabA - slabB) / Math.Sqrt(errorA * errorA + errorB * errorB);
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent Trowbridge-Reitz %.2f through MicrofacetDistribution against written out: closed form, " +
                                                          "%d pairs, worst relative difference %.2e; albedo of the slab with 64 events %.4f against %.4f (%.1f sigma)",
                                                          alpha, compared, worst, slabB, slabA, sigmas));
                EXPECT_LT(worst, 2e-2, "the closed form differs");
                EXPECT_LT(sigmas, 4, "the walks differ");
            }

            string[] names = new string[] { "TrowbridgeReitz 0.2", "Beckmann 0.2", "StudentT 0.2 gamma 3", "Beckmann 0.05", "StudentT 0.05 gamma 2.5", "Charlie 0.5" };
            MicrofacetDistribution[] distributions = new MicrofacetDistribution[] { new TrowbridgeReitzDistribution(0.2f, 0.2f), new BeckmannDistribution(0.2f, 0.2f),
                                                                                    new StudentTDistribution(0.2f, 0.2f, 3f), new BeckmannDistribution(0.05f, 0.05f),
                                                                                    new StudentTDistribution(0.05f, 0.05f, 2.5f), new CharlieDistribution(0.5f) };
            // The points of the effective extinction: the sheen distribution has normals all over the hemisphere, and the
            // reflectance of a platelet goes through its whole range over them
            int[] quadratures = new int[] { 16, 16, 16, 16, 16, 256 };
            for (int index = 0; index < distributions.Length; ++index)
            {
                MicrofacetDistribution distribution = distributions[index];
                int quadrature = quadratures[index];
                foreach (Float cosine in new Float[] { 0.9f, 0.4f })
                {
                    Vector3f wo = new Vector3f(std.sqrt(1 - cosine * cosine), 0, cosine);
                    SampledAlbedo(new PearlescentBxDF(stack, Wavelengths(), 3f, distribution, S(1f), quadrature, 1000000), wo, 50000, ref rng, out double mirror,
                                  out double white, out double whiteError);
                    BxDF bxdf = new PearlescentBxDF(stack, Wavelengths(), 3f, distribution, S(0.5f), quadrature, 64);
                    SampledAlbedo(bxdf, wo, 100000, ref rng, out double mirrorGrey, out double sampled, out double sampledError);
                    EvaluatedAlbedo(bxdf, wo, quadrature > 16 ? 40000 : 150000, ref rng, out double evaluated, out double evaluatedError);
                    double error = Math.Sqrt(sampledError * sampledError + evaluatedError * evaluatedError);
                    double sigmas = Math.Abs(sampled - evaluated) / error, relative = Math.Abs(sampled - evaluated) / sampled;
                    Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent %s cos %.1f: white base total %.4f; albedo of the slab by Sample_f %.4f, by f %.4f " +
                                                              "(%.1f sigma, %.2e)", names[index], cosine, mirror + white, sampled, evaluated, sigmas, relative));
                    EXPECT_LT(Math.Abs(mirror + white - 1), Math.Max(5 * whiteError, 5e-3), names[index] + ": energy is not conserved");
                    EXPECT_TRUE(sigmas < 5 || relative < 1e-2, names[index] + ": Sample_f and f disagree about the albedo");
                }
                BxDF pair = new PearlescentBxDF(stack, Wavelengths(), 3f, distribution, S(0.5f), quadrature, 64);
                double worstZ = 0, worstRelative = 0;
                for (int k = 0; k < 6; ++k)
                {
                    Vector3f wo = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.15f + 0.8f * rng.Uniform<Float>()));
                    Vector3f wi = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.15f + 0.8f * rng.Uniform<Float>()));
                    double sumA = 0, sumA2 = 0, sumB = 0, sumB2 = 0;
                    int repeats = quadrature > 16 ? 2000 : 8000;
                    for (int i = 0; i < repeats; ++i)
                    {
                        Vector3f d = 1e-4f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                        Vector3f a = Normalize(wo + d), b = Normalize(wi - d);
                        double x = pair.f(a, b, TransportMode.Radiance).Average();
                        double y = pair.f(b, a, TransportMode.Importance).Average();
                        sumA += x;
                        sumA2 += x * x;
                        sumB += y;
                        sumB2 += y * y;
                    }
                    double meanA = sumA / repeats, meanB = sumB / repeats;
                    double error = Math.Sqrt((sumA2 / repeats - meanA * meanA + sumB2 / repeats - meanB * meanB) / repeats);
                    worstZ = Math.Max(worstZ, Math.Abs(meanA - meanB) / error);
                    worstRelative = Math.Max(worstRelative, Math.Abs(meanA - meanB) / Math.Max(meanA, meanB));
                }
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent %s: reciprocity of 6 pairs: worst %.1f sigma, worst relative difference %.2e",
                                                          names[index], worstZ, worstRelative));
                EXPECT_LT(worstZ, 4, names[index] + ": not reciprocal");
            }
        }

        // What an evaluation costs (no limits: a measurement)
        [TEST("Pearlescent", "Cost")]
        public static void Pearlescent_Cost()
        {
            RNG rng = new RNG(3);
            PearlescentStack[] stacks = new PearlescentStack[] { Stack(1.57, 60, 1.58, 63, 500, 60), Stack(1.555, 120, 1.4585, 67.8, 80, 0) };
            foreach (PearlescentStack stack in stacks)
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                double sum = 0;
                int count = 2000000;
                for (int i = 0; i < count; ++i)
                {
                    sum += Pearlescent.Reflectance(0.05 + 0.95 * rng.Uniform<Float>(), 400 + 300 * rng.Uniform<Float>(), stack);
                }
                double reflectance = watch.Elapsed.TotalMilliseconds * 1e6 / count;
                foreach (Float alpha in new Float[] { 0.2f, 0.05f })
                {
                    BxDF bxdf = Slab(stack, 3f, alpha, 0.5f, 64);
                    watch.Restart();
                    count = 20000;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f wo = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.15f + 0.8f * rng.Uniform<Float>()));
                        Vector3f wi = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.15f + 0.8f * rng.Uniform<Float>()));
                        sum += bxdf.f(wo, wi, TransportMode.Radiance)[0];
                    }
                    double evaluate = watch.Elapsed.TotalMilliseconds * 1e3 / count;
                    watch.Restart();
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f wo = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.15f + 0.8f * rng.Uniform<Float>()));
                        pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                     TransportMode.Radiance, BxDFReflTransFlags.All);
                        sum += (bool)bs ? 1 : 0;
                    }
                    double sample = watch.Elapsed.TotalMilliseconds * 1e3 / count;
                    Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent cost, substrate %.0f +- %.0f nm, alpha %.2f: Reflectance %.0f ns, f %.1f us, Sample_f %.1f us",
                                                              stack.substrateThickness, stack.substrateDeviation, alpha, reflectance, evaluate, sample));
                }
                EXPECT_GT(sum, 0);
            }
        }

        static double Difference(MuellerMatrix a, MuellerMatrix b)
        {
            double worst = 0;
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    for (int j = 0; j < NSpectrumSamples; ++j)
                    {
                        worst = Math.Max(worst, Math.Abs(a.Get(row, column)[j] - b.Get(row, column)[j]));
                    }
                }
            }
            return worst;
        }

        // The worst excess of the degree of polarisation over 1, for the six pure states
        static double Unphysical(MuellerMatrix m)
        {
            double worst = 0;
            for (int state = 0; state < 6; ++state)
            {
                Float sign = (state & 1) == 0 ? 1 : -1;
                StokesVector input = new StokesVector(S(1), S(state / 2 == 0 ? sign : 0), S(state / 2 == 1 ? sign : 0), S(state / 2 == 2 ? sign : 0));
                StokesVector output = m * input;
                for (int j = 0; j < NSpectrumSamples; ++j)
                {
                    double i = output.s0[j], q = output.s1[j], u = output.s2[j], v = output.s3[j];
                    if (i < -1e-6)
                    {
                        return 1e9;
                    }
                    worst = Math.Max(worst, Math.Sqrt(q * q + u * u + v * v) - i);
                }
            }
            return worst;
        }

        [TEST("Pearlescent", "MuellerMatrices")]
        public static void Pearlescent_MuellerMatrices()
        {
            RNG rng = new RNG(21);
            PearlescentStack stack = Stack(1.57, 60, 1.58, 63, 500, 60);
            // One reflection by a platelet over a black base: the matrix that comes with a sample is the matrix of f for the
            // sampled direction, in both modes; the mirror reflection is the Fresnel matrix of the interface
            BSDF single = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), Slab(stack, 3f, 0.2f, 0f, 1));
            BSDF full = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), Slab(stack, 3f, 0.2f, 0.5f, 64));
            BSDF baseOnly = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), Slab(stack, 0f, 0.2f, 0.5f, 64));
            double worstSingle = 0, worstMirror = 0, worstWeight = 0, worstPhysical = 0, worstBase = 0, mostPolarised = 0;
            int compared = 0, mirrors = 0, evaluated = 0;
            for (int i = 0; i < 4000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                wo.z = 0.1f + 0.85f * Math.Abs(wo.z);
                wo = Normalize(wo);
                TransportMode mode = (i & 1) == 0 ? TransportMode.Radiance : TransportMode.Importance;
                Float uc = rng.Uniform<Float>();
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<BSDFSample> bs = single.Sample_f(wo, uc, u, mode, BxDFReflTransFlags.All, out bool polarised, out Attenuation sampled);
                if ((bool)bs && polarised)
                {
                    if (bs.value().IsSpecular())
                    {
                        // The Fresnel matrix of the interface: the reflectances and the phase between the two polarisations
                        pbrt.FrDielectricPolarised(wo.z, 1.57f, out Float fs, out Float fp, out Float ds, out Float dp);
                        MuellerMatrix m = sampled.GetMuellerMatrix();
                        double mean = 0.5 * (fs + fp), root = Math.Sqrt(fs * fp);
                        worstMirror = Math.Max(worstMirror, Math.Abs(m.Get(0, 0)[0] - 1) + Math.Abs(m.Get(0, 1)[0] - 0.5 * (fs - fp) / mean) +
                                                            Math.Abs(m.Get(2, 2)[0] - root * Math.Cos(ds - dp) / mean) +
                                                            Math.Abs(m.Get(2, 3)[0] - root * Math.Sin(ds - dp) / mean));
                        ++mirrors;
                    }
                    else
                    {
                        SampledSpectrum value = single.f(wo, bs.value().wi, mode, out bool evaluatedPolarised, out Attenuation matrix);
                        if (evaluatedPolarised && value.MinComponentValue() > 1e-4f)
                        {
                            worstSingle = Math.Max(worstSingle, Difference(sampled.GetMuellerMatrix(), matrix.GetMuellerMatrix()));
                            worstPhysical = Math.Max(worstPhysical, Unphysical(sampled.GetMuellerMatrix()));
                            ++compared;
                        }
                    }
                }

                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                wi.z = 0.1f + 0.85f * Math.Abs(wi.z);
                wi = Normalize(wi);
                SampledSpectrum fullValue = full.f(wo, wi, mode, out bool fullPolarised, out Attenuation fullMatrix);
                if (fullPolarised)
                {
                    MuellerMatrix m = fullMatrix.GetMuellerMatrix();
                    worstPhysical = Math.Max(worstPhysical, Unphysical(m));
                    for (int j = 0; j < NSpectrumSamples; ++j)
                    {
                        worstWeight = Math.Max(worstWeight, Math.Abs(m.Get(0, 0)[j] - 1));
                        EXPECT_GT(m.Get(0, 0)[j], 0);
                        mostPolarised = Math.Max(mostPolarised, Math.Sqrt(m.Get(1, 0)[j] * m.Get(1, 0)[j] + m.Get(2, 0)[j] * m.Get(2, 0)[j] +
                                                                          m.Get(3, 0)[j] * m.Get(3, 0)[j]));
                    }
                    ++evaluated;
                }
                SampledSpectrum baseValue = baseOnly.f(wo, wi, mode, out bool basePolarised, out Attenuation baseMatrix);
                if (basePolarised)
                {
                    StokesVector output = baseMatrix.GetMuellerMatrix() * new StokesVector(S(1), S(1), S(0), S(0));
                    worstBase = Math.Max(worstBase, Math.Abs(output.s1[0]) + Math.Abs(output.s2[0]) + Math.Abs(output.s3[0]) + Math.Abs(output.s0[0] - 1));
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent matrices: sampled single reflection against f, %d samples: worst difference %.2e; mirror " +
                                                      "against the Fresnel matrix, %d samples: %.2e; %d evaluated: first element within %.2e of 1, degree of " +
                                                      "polarisation above 1 by at most %.2e, most polarising %.3f; base alone polarises by at most %.2e",
                                                      compared, worstSingle, mirrors, worstMirror, evaluated, worstWeight, worstPhysical, mostPolarised, worstBase));
            EXPECT_GT(compared, 500);
            EXPECT_GT(mirrors, 50);
            EXPECT_GT(evaluated, 3000);
            EXPECT_LT(worstSingle, 2e-3);
            EXPECT_LT(worstMirror, 1e-4);
            EXPECT_LT(worstPhysical, 1e-3);
            EXPECT_LT(worstBase, 1e-5);
        }
    }
}
