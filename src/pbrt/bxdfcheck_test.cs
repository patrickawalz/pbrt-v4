// (not in pbrt-v4) bxdfcheck_test.cs
//
// The checks that every BxDF of the pbr-materials work has to pass before a material is built on it. They are made on the
// BxDF alone, in its local frame (the normal is +z), so that a failure is the BxDF's and not a shading normal's.
//
//   Reciprocity   f(wo, wi, Radiance) = f(wi, wo, Importance) for every pair of directions. This is the adjoint relation
//                 that the bidirectional integrators rely on; for a reflection it is the symmetry f(wo, wi) = f(wi, wo),
//                 and for a refraction it is the generalised reciprocity f(wo, wi) / n_i^2 = f(wi, wo) / n_o^2 (the
//                 radiance mode of pbrt carries the 1 / eta^2).
//   Energy        The albedo of a direction, the integral of f |cos theta_i| over the sphere, is at most 1. It is taken in
//                 the importance mode, where it is the fraction of the power arriving along the direction that leaves the
//                 surface (the radiance mode of a refraction has the 1 / eta^2 of the radiance and is not a power).
//   Sampling      The albedo by Sample_f (the mean of f |cos| / pdf) equals the albedo by directions uniform over the sphere,
//                 which uses f alone; the integral of PDF over the sphere is 1 (less than 1 only where Sample_f may fail);
//                 and the f and pdf that Sample_f returns are those of f() and PDF() for the direction it returns.
//
// A BxDF whose f is itself an estimate (the random walks of the layered and the multiple-scattering models) is evaluated
// `repeats` times and averaged, and its reciprocity is judged against the standard error of that average.

using System;
using testing;

namespace pbrt
{
    // BxDFCheckOptions Definition
    public sealed class BxDFCheckOptions
    {
        // The outgoing directions of the energy and sampling checks, by their cosine (both signs are used for a BxDF that is
        // not one sided)
        public Float[] cosines = new Float[] { 0.98f, 0.8f, 0.55f, 0.3f, 0.1f };
        // The pairs of directions of the reciprocity check
        public int pairs = 2000;
        // The samples of an albedo and of the integral of the pdf
        public int samples = 200000;
        // The evaluations of f that are averaged, for a BxDF whose f is an estimate
        public int repeats = 1;
        // Whether the directions below the surface are checked as outgoing directions too
        public bool bothSides = true;
        // Whether the BxDF may lose energy on purpose in Sample_f (the integral of the pdf is then below 1)
        public Float seed = 1;
    }

    // BxDFCheckResult Definition
    public sealed class BxDFCheckResult
    {
        // The largest |a - b| / max(|a|, |b|) over the pairs whose values are not both negligible, and the largest of the same
        // in units of the standard error for a BxDF whose f is an estimate
        public double reciprocity;
        public double reciprocitySigmas;
        // For a BxDF whose f is an estimate: the mean of the squared difference of a pair in units of its variance (1 when reciprocal)
        public double reciprocityChi2;
        // The pairs with a value that is not negligible, of the pairs tried
        public int reciprocityPairs;
        // The largest albedo (by Sample_f, importance mode) over the directions, and the smallest
        public double albedoMax;
        public double albedoMin;
        // The largest difference between the albedo by Sample_f and the albedo by uniform directions, in units of the standard
        // error of the difference, and as a fraction of the albedo
        public double albedoSigmas;
        public double albedoRelative;
        // The integral of the pdf over the sphere: its range over the directions
        public double pdfMin;
        public double pdfMax;
        // The largest relative difference between what Sample_f returns and f() and PDF() of the returned direction
        public double sampleF;
        public double samplePdf;
        // Values that are negative or not finite
        public int invalid;

        public override string ToString()
        {
            return pbrt.StringPrintf("reciprocity %.2e (%.1f sigma, chi2 %.2f, %d pairs), albedo %.4f..%.4f, sampled vs uniform %.2f sigma (%.2e), " +
                                     "pdf integral %.4f..%.4f, Sample_f vs f %.2e, vs PDF %.2e, invalid %d",
                                     reciprocity, reciprocitySigmas, reciprocityChi2, reciprocityPairs, albedoMin, albedoMax, albedoSigmas, albedoRelative,
                                     pdfMin, pdfMax, sampleF, samplePdf, invalid);
        }
    }

    public static unsafe class BxDFCheck
    {
        // Runs every check on the BxDF and prints one line with the numbers
        public static BxDFCheckResult Run(BxDF bxdf, string name, BxDFCheckOptions options)
        {
            BxDFCheckResult result = new BxDFCheckResult();
            RNG rng = new RNG(NameHash(name), (ulong)options.seed);
            bool transmissive = IsTransmissive(bxdf.Flags());
            Reciprocity(bxdf, options, transmissive, ref rng, result);
            EnergyAndSampling(bxdf, options, ref rng, result);
            Console.Error.WriteLine("MEASURE bxdf " + name + ": " + result);
            return result;
        }

        // FNV-1a, so that the directions of a BxDF are the same in every run
        static ulong NameHash(string name)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in name)
            {
                hash = (hash ^ c) * 1099511628211UL;
            }
            return hash;
        }

        // The usual expectations: reciprocal, energy conserving, and Sample_f, PDF and f in agreement
        public static void Expect(BxDFCheckResult result, string name, double reciprocityTolerance = 2e-3, double albedoLimit = 1.005,
                                  double sigmas = 5, double pdfLow = 0.99)
        {
            EXPECT_EQ(0, result.invalid, name + ": negative or non finite values");
            EXPECT_GT(result.reciprocityPairs, 100, name + ": too few pairs with a value");
            EXPECT_TRUE(result.reciprocity < reciprocityTolerance || result.reciprocitySigmas < sigmas,
                        name + ": not reciprocal, " + result.reciprocity + " (" + result.reciprocitySigmas + " sigma)");
            EXPECT_LT(result.albedoMax, albedoLimit, name + ": albedo above one");
            EXPECT_TRUE(result.albedoSigmas < sigmas || result.albedoRelative < 5e-3,
                        name + ": Sample_f and f disagree about the albedo, " + result.albedoSigmas + " sigma, " + result.albedoRelative);
            EXPECT_LT(result.pdfMax, 1.01, name + ": the pdf integrates to more than one");
            EXPECT_GT(result.pdfMin, pdfLow, name + ": the pdf integrates to less than one");
            EXPECT_LT(result.sampleF, 2e-3, name + ": Sample_f returns another f than f()");
            EXPECT_LT(result.samplePdf, 2e-3, name + ": Sample_f returns another pdf than PDF()");
        }

        static SampledSpectrum Evaluate(BxDF bxdf, Vector3f wo, Vector3f wi, TransportMode mode, int repeats, out double standardError)
        {
            standardError = 0;
            if (repeats <= 1)
            {
                return bxdf.f(wo, wi, mode);
            }
            // An estimate: its f depends on the hash of the directions only, so the directions are jittered by less than the
            // float precision of the tests to get independent estimates
            double sum = 0, sum2 = 0;
            SampledSpectrum total = new SampledSpectrum(0.0f);
            RNG jitter = new RNG(Hash(wo), Hash(wi));
            for (int i = 0; i < repeats; ++i)
            {
                Vector3f d = 1e-5f * new Vector3f(jitter.Uniform<Float>() - 0.5f, jitter.Uniform<Float>() - 0.5f, jitter.Uniform<Float>() - 0.5f);
                SampledSpectrum value = bxdf.f(Normalize(wo + d), Normalize(wi - d), mode);
                total += value;
                double average = value.Average();
                sum += average;
                sum2 += average * average;
            }
            double mean = sum / repeats;
            standardError = Math.Sqrt(Math.Max(0, sum2 / repeats - mean * mean) / repeats);
            return total / repeats;
        }

        static void Reciprocity(BxDF bxdf, BxDFCheckOptions options, bool transmissive, ref RNG rng, BxDFCheckResult result)
        {
            double chi2 = 0;
            int chi2Count = 0;
            for (int i = 0; i < options.pairs; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (!options.bothSides)
                {
                    wo.z = Math.Abs(wo.z);
                }
                if (!transmissive)
                {
                    // A reflection: both directions on the same side
                    wi.z = wo.z > 0 ? Math.Abs(wi.z) : -Math.Abs(wi.z);
                }
                // Away from grazing, where the cosines in the denominators of the models amplify the rounding
                if (Math.Abs(wo.z) < 0.02f || Math.Abs(wi.z) < 0.02f)
                {
                    continue;
                }
                SampledSpectrum a = Evaluate(bxdf, wo, wi, TransportMode.Radiance, options.repeats, out double errorA);
                SampledSpectrum b = Evaluate(bxdf, wi, wo, TransportMode.Importance, options.repeats, out double errorB);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    if (!(a[c] >= 0) || !(b[c] >= 0) || Float.IsInfinity(a[c]) || Float.IsInfinity(b[c]))
                    {
                        ++result.invalid;
                    }
                }
                double x = a.Average(), y = b.Average();
                double scale = Math.Max(Math.Abs(x), Math.Abs(y));
                if (scale < 1e-4)
                {
                    continue;
                }
                ++result.reciprocityPairs;
                double relative = Math.Abs(x - y) / scale;
                double error = Math.Sqrt(errorA * errorA + errorB * errorB);
                if (options.repeats > 1 && error > 0)
                {
                    result.reciprocitySigmas = Math.Max(result.reciprocitySigmas, Math.Abs(x - y) / error);
                    chi2 += Sqr(x - y) / Sqr(error);
                    ++chi2Count;
                }
                result.reciprocity = Math.Max(result.reciprocity, relative);
            }
            result.reciprocityChi2 = chi2Count > 0 ? chi2 / chi2Count : 0;
        }

        static void EnergyAndSampling(BxDF bxdf, BxDFCheckOptions options, ref RNG rng, BxDFCheckResult result)
        {
            result.albedoMin = Double.PositiveInfinity;
            result.pdfMin = Double.PositiveInfinity;
            bool specular = IsSpecular(bxdf.Flags());
            int sides = options.bothSides ? 2 : 1;
            for (int side = 0; side < sides; ++side)
            {
                foreach (Float cosine in options.cosines)
                {
                    Float sine = SafeSqrt(1 - cosine * cosine);
                    Float phi = 2 * Pi * rng.Uniform<Float>();
                    Vector3f wo = new Vector3f(sine * (Float)Math.Cos(phi), sine * (Float)Math.Sin(phi), side == 0 ? cosine : -cosine);

                    // The albedo by Sample_f, and what Sample_f returns against f() and PDF()
                    double sum = 0, sum2 = 0;
                    for (int i = 0; i < options.samples; ++i)
                    {
                        pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                     TransportMode.Importance, BxDFReflTransFlags.All);
                        if (!bs.has_value() || bs.value().pdf == 0)
                        {
                            continue;
                        }
                        BSDFSample s = bs.value();
                        double value = s.f.Average() * AbsCosTheta(s.wi) / s.pdf;
                        if (!(value >= 0) || Double.IsInfinity(value))
                        {
                            ++result.invalid;
                            continue;
                        }
                        sum += value;
                        sum2 += value * value;
                        if (i < 2000 && !s.IsSpecular() && !s.pdfIsProportional && options.repeats <= 1)
                        {
                            double f = bxdf.f(wo, s.wi, TransportMode.Importance).Average();
                            double pdf = bxdf.PDF(wo, s.wi, TransportMode.Importance, BxDFReflTransFlags.All);
                            double fScale = Math.Max(Math.Abs(f), Math.Abs(s.f.Average()));
                            if (fScale > 1e-4)
                            {
                                result.sampleF = Math.Max(result.sampleF, Math.Abs(f - s.f.Average()) / fScale);
                            }
                            double pdfScale = Math.Max(pdf, s.pdf);
                            if (pdfScale > 1e-4)
                            {
                                result.samplePdf = Math.Max(result.samplePdf, Math.Abs(pdf - s.pdf) / pdfScale);
                            }
                        }
                    }
                    double sampled = sum / options.samples;
                    double sampledError = Math.Sqrt(Math.Max(0, sum2 / options.samples - sampled * sampled) / options.samples);
                    result.albedoMax = Math.Max(result.albedoMax, sampled);
                    result.albedoMin = Math.Min(result.albedoMin, sampled);
                    if (specular)
                    {
                        // A specular part is in Sample_f only: nothing to compare it with
                        continue;
                    }

                    // The albedo by directions uniform over the sphere, and the integral of the pdf
                    // The directions are jittered over a grid: with independent directions the integral of the pdf of a narrow
                    // lobe is some percent wrong at this count, which is the size of what the check is to find. (The error of
                    // the albedo below is computed as for independent directions, and so is an upper limit.)
                    double uniformSum = 0, uniformSum2 = 0, pdfSum = 0;
                    int grid = Math.Max(1, (int)Math.Sqrt((double)options.samples));
                    int uniformSamples = grid * grid;
                    for (int i = 0; i < uniformSamples; ++i)
                    {
                        Vector3f wi = SampleUniformSphere(new Point2f((i % grid + rng.Uniform<Float>()) / grid, (i / grid + rng.Uniform<Float>()) / grid));
                        double f = bxdf.f(wo, wi, TransportMode.Importance).Average();
                        double value = f * AbsCosTheta(wi) * 4 * Pi;
                        if (!(value >= 0) || Double.IsInfinity(value))
                        {
                            ++result.invalid;
                            continue;
                        }
                        uniformSum += value;
                        uniformSum2 += value * value;
                        pdfSum += bxdf.PDF(wo, wi, TransportMode.Importance, BxDFReflTransFlags.All) * 4 * Pi;
                    }
                    double uniform = uniformSum / uniformSamples;
                    double uniformError = Math.Sqrt(Math.Max(0, uniformSum2 / uniformSamples - uniform * uniform) / uniformSamples);
                    double error = Math.Sqrt(sampledError * sampledError + uniformError * uniformError);
                    if (error > 0)
                    {
                        result.albedoSigmas = Math.Max(result.albedoSigmas, Math.Abs(sampled - uniform) / error);
                    }
                    if (Math.Max(sampled, uniform) > 1e-3)
                    {
                        result.albedoRelative = Math.Max(result.albedoRelative, Math.Abs(sampled - uniform) / Math.Max(sampled, uniform));
                    }
                    double pdfIntegral = pdfSum / uniformSamples;
                    result.pdfMax = Math.Max(result.pdfMax, pdfIntegral);
                    result.pdfMin = Math.Min(result.pdfMin, pdfIntegral);
                }
            }
            if (specular)
            {
                result.pdfMin = 1;
                result.pdfMax = 1;
            }
        }
    }

    // The checks on the BxDFs of stock pbrt-v4: what the checks say about models whose behaviour is known
    public static unsafe class BxDFCheckBaselineTests
    {
        [TEST("BxDFCheck", "Lambertian")]
        public static void BxDFCheck_Lambertian()
        {
            BxDFCheckResult result = BxDFCheck.Run(new DiffuseBxDF(new SampledSpectrum(0.8f)), "Lambertian", new BxDFCheckOptions());
            BxDFCheck.Expect(result, "Lambertian");
            // Its albedo is its reflectance
            EXPECT_LT(Math.Abs(result.albedoMax - 0.8), 5e-3);
            EXPECT_LT(Math.Abs(result.albedoMin - 0.8), 5e-3);
        }

        [TEST("BxDFCheck", "DiffuseTransmission")]
        public static void BxDFCheck_DiffuseTransmission()
        {
            BxDF bxdf = new DiffuseTransmissionBxDF(new SampledSpectrum(0.5f), new SampledSpectrum(0.4f));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "DiffuseTransmission", new BxDFCheckOptions());
            BxDFCheck.Expect(result, "DiffuseTransmission");
            EXPECT_LT(Math.Abs(result.albedoMax - 0.9), 5e-3);
        }

        [TEST("BxDFCheck", "RoughConductor")]
        public static void BxDFCheck_RoughConductor()
        {
            BxDF bxdf = new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), new SampledSpectrum(0.2f), new SampledSpectrum(3.9f));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "RoughConductor alpha 0.3", new BxDFCheckOptions());
            // Stock pbrt: a sampled normal whose reflection goes below the surface is dropped, so the pdf integrates to less than 1
            BxDFCheck.Expect(result, "RoughConductor", 2e-3, 1.005, 5, 0.85);
        }

        [TEST("BxDFCheck", "RoughConductorAnisotropic")]
        public static void BxDFCheck_RoughConductorAnisotropic()
        {
            BxDF bxdf = new ConductorBxDF(new TrowbridgeReitzDistribution(0.5f, 0.15f), new SampledSpectrum(0.2f), new SampledSpectrum(3.9f));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "RoughConductor alpha 0.5 0.15", new BxDFCheckOptions());
            BxDFCheck.Expect(result, "RoughConductorAnisotropic", 2e-3, 1.005, 5, 0.85);
        }

        [TEST("BxDFCheck", "RoughDielectric")]
        public static void BxDFCheck_RoughDielectric()
        {
            BxDF bxdf = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.3f, 0.3f));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "RoughDielectric eta 1.5 alpha 0.3", new BxDFCheckOptions());
            BxDFCheck.Expect(result, "RoughDielectric", 2e-3, 1.005, 5, 0.85);
        }

        // The stock BxDFs on the other distributions ("string distribution" of the materials), held to the limits of GGX
        [TEST("BxDFCheck", "RoughConductorBeckmann")]
        public static void BxDFCheck_RoughConductorBeckmann()
        {
            BxDF bxdf = new ConductorBxDF(new BeckmannDistribution(0.3f, 0.3f), new SampledSpectrum(0.2f), new SampledSpectrum(3.9f));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "RoughConductor Beckmann alpha 0.3", new BxDFCheckOptions());
            BxDFCheck.Expect(result, "RoughConductorBeckmann", 2e-3, 1.005, 5, 0.85);
        }

        [TEST("BxDFCheck", "RoughConductorStudentT")]
        public static void BxDFCheck_RoughConductorStudentT()
        {
            BxDF bxdf = new ConductorBxDF(new StudentTDistribution(0.3f, 0.3f, 4f), new SampledSpectrum(0.2f), new SampledSpectrum(3.9f));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "RoughConductor StudentT gamma 4 alpha 0.3", new BxDFCheckOptions());
            BxDFCheck.Expect(result, "RoughConductorStudentT", 2e-3, 1.005, 5, 0.85);
        }

        [TEST("BxDFCheck", "RoughDielectricBeckmann")]
        public static void BxDFCheck_RoughDielectricBeckmann()
        {
            BxDF bxdf = new DielectricBxDF(1.5f, new BeckmannDistribution(0.3f, 0.3f));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "RoughDielectric Beckmann eta 1.5 alpha 0.3", new BxDFCheckOptions());
            BxDFCheck.Expect(result, "RoughDielectricBeckmann", 2e-3, 1.005, 5, 0.85);
        }

        [TEST("BxDFCheck", "RoughDielectricStudentT")]
        public static void BxDFCheck_RoughDielectricStudentT()
        {
            BxDF bxdf = new DielectricBxDF(1.5f, new StudentTDistribution(0.3f, 0.3f, 4f));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "RoughDielectric StudentT gamma 4 eta 1.5 alpha 0.3", new BxDFCheckOptions());
            BxDFCheck.Expect(result, "RoughDielectricStudentT", 2e-3, 1.005, 5, 0.85);
        }

        // A rough thin film (ThinFilmBxDF with a distribution): the microfacet model of the two BxDFs above with the reflectance
        // of the stack of layers, held to their limits. On a conductor the film has one side.
        [TEST("BxDFCheck", "RoughThinFilmDielectric")]
        public static void BxDFCheck_RoughThinFilmDielectric()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            BxDF bxdf = new ThinFilmBxDF(lambda, new Float[] { 2.1f }, new Float[] { 300 }, 1.5f, new SampledSpectrum(1f), new SampledSpectrum(0f), false,
                                         new TrowbridgeReitzDistribution(0.3f, 0.3f));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "RoughThinFilm on a dielectric, eta 2.1 300 nm on 1.5, alpha 0.3", new BxDFCheckOptions());
            BxDFCheck.Expect(result, "RoughThinFilmDielectric", 2e-3, 1.005, 5, 0.85);
        }

        [TEST("BxDFCheck", "RoughThinFilmConductor")]
        public static void BxDFCheck_RoughThinFilmConductor()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            BxDF bxdf = new ThinFilmBxDF(lambda, new Float[] { 1.5f }, new Float[] { 400 }, 1.5f, new SampledSpectrum(0.2f), new SampledSpectrum(3.9f), true,
                                         new BeckmannDistribution(0.3f, 0.3f));
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.bothSides = false;
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "RoughThinFilm on a conductor, eta 1.5 400 nm, Beckmann alpha 0.3", options);
            BxDFCheck.Expect(result, "RoughThinFilmConductor", 2e-3, 1.005, 5, 0.85);
        }

        // A film of no thickness is the bare surface: f and PDF of the rough film are those of DielectricBxDF and ConductorBxDF
        [TEST("BxDFCheck", "RoughThinFilmOfNoThickness")]
        public static void BxDFCheck_RoughThinFilmOfNoThickness()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            MicrofacetDistribution distribution = new TrowbridgeReitzDistribution(0.4f, 0.25f);
            SampledSpectrum eta = new SampledSpectrum(0.2f), k = new SampledSpectrum(3.9f);
            BxDF glass = new DielectricBxDF(1.5f, distribution), metal = new ConductorBxDF(distribution, eta, k);
            BxDF glassFilm = new ThinFilmBxDF(lambda, new Float[] { 2.1f }, new Float[] { 0 }, 1.5f, new SampledSpectrum(1f), new SampledSpectrum(0f), false,
                                              distribution);
            BxDF metalFilm = new ThinFilmBxDF(lambda, new Float[] { 2.1f }, new Float[] { 0 }, 1.5f, eta, k, true, distribution);
            RNG rng = new RNG(11);
            double worstF = 0, worstPdf = 0;
            int compared = 0;
            for (int i = 0; i < 4000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                TransportMode mode = (i & 1) == 0 ? TransportMode.Radiance : TransportMode.Importance;
                for (int b = 0; b < 2; ++b)
                {
                    BxDF bare = b == 0 ? glass : metal, film = b == 0 ? glassFilm : metalFilm;
                    if (b == 1 && wo.z < 0)
                    {
                        // The film on a conductor has one side
                        continue;
                    }
                    double f0 = bare.f(wo, wi, mode).Average(), f1 = film.f(wo, wi, mode).Average();
                    double p0 = bare.PDF(wo, wi, mode, BxDFReflTransFlags.All), p1 = film.PDF(wo, wi, mode, BxDFReflTransFlags.All);
                    if (Math.Max(f0, f1) > 1e-3)
                    {
                        // Next to total reflection the transmittance 1 - R is the difference of two numbers near 1, good to the
                        // rounding of R (1e-7) only: the difference is allowed that much on top of the relative one
                        worstF = Math.Max(worstF, Math.Max(0, Math.Abs(f0 - f1) - 3e-6) / Math.Max(f0, f1));
                        ++compared;
                    }
                    if (Math.Max(p0, p1) > 1e-3)
                    {
                        worstPdf = Math.Max(worstPdf, Math.Max(0, Math.Abs(p0 - p1) - 3e-6) / Math.Max(p0, p1));
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE bxdf rough thin film of no thickness against the bare BxDFs: f %.1e, PDF %.1e (%d pairs)", worstF,
                                                      worstPdf, compared));
            EXPECT_GT(compared, 1500);
            EXPECT_LT(worstF, 1e-4);
            EXPECT_LT(worstPdf, 1e-4);
        }

        [TEST("BxDFCheck", "SmoothDielectric")]
        public static void BxDFCheck_SmoothDielectric()
        {
            BxDF bxdf = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.0f, 0.0f));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "SmoothDielectric eta 1.5", new BxDFCheckOptions());
            // Specular: only the energy is checked. Nothing is absorbed
            EXPECT_EQ(0, result.invalid);
            EXPECT_LT(Math.Abs(result.albedoMax - 1), 5e-3);
            EXPECT_LT(Math.Abs(result.albedoMin - 1), 5e-3);
        }

        [TEST("BxDFCheck", "ThinDielectric")]
        public static void BxDFCheck_ThinDielectric()
        {
            BxDFCheckResult result = BxDFCheck.Run(new ThinDielectricBxDF(1.5f), "ThinDielectric eta 1.5", new BxDFCheckOptions());
            EXPECT_EQ(0, result.invalid);
            EXPECT_LT(Math.Abs(result.albedoMax - 1), 5e-3);
            EXPECT_LT(Math.Abs(result.albedoMin - 1), 5e-3);
        }

        // The error function against the values of a table (Abramowitz and Stegun 7.1)
        [TEST("BxDFCheck", "Erf")]
        public static void BxDFCheck_Erf()
        {
            double[] x = new double[] { 0.1, 0.5, 1.0, 2.0, 2.4, 2.6, 3.0, 4.0 };
            double[] erf = new double[] { 0.1124629160182849, 0.5204998778130465, 0.8427007929497149, 0.9953222650189527,
                                          0.9993114861033550, 0.9997639655834707, 0.9999779095030014, 0.9999999845827421 };
            for (int i = 0; i < x.Length; ++i)
            {
                EXPECT_LT(Math.Abs(pbrt.Erf(x[i]) - erf[i]), 1e-13);
                EXPECT_LT(Math.Abs(pbrt.Erf(-x[i]) + erf[i]), 1e-13);
            }
        }

        [TEST("BxDFCheck", "EonDiffuse")]
        public static void BxDFCheck_EonDiffuse()
        {
            foreach (Float roughness in new Float[] { 0.05f, 0.3f, 0.7f, 1.0f })
            {
                foreach (Float reflectance in new Float[] { 0.3f, 0.8f, 1.0f })
                {
                    string name = pbrt.StringPrintf("EonDiffuse roughness %.2f R %.1f", roughness, reflectance);
                    BxDFCheckResult result = BxDFCheck.Run(new EonDiffuseBxDF(new SampledSpectrum(reflectance), roughness), name, new BxDFCheckOptions());
                    BxDFCheck.Expect(result, name);
                    if (reflectance == 1.0f)
                    {
                        // Energy preserving: a white surface reflects everything, at every angle
                        EXPECT_GT(result.albedoMin, 0.995, name + ": a white surface loses energy");
                    }
                }
            }
        }

        [TEST("BxDFCheck", "VmfDiffuse")]
        public static void BxDFCheck_VmfDiffuse()
        {
            foreach (Float roughness in new Float[] { 0.05f, 0.3f, 0.7f, 1.0f })
            {
                foreach (Float reflectance in new Float[] { 0.3f, 0.8f, 1.0f })
                {
                    string name = pbrt.StringPrintf("VmfDiffuse roughness %.2f R %.1f", roughness, reflectance);
                    BxDFCheckResult result = BxDFCheck.Run(new VmfDiffuseBxDF(new SampledSpectrum(reflectance), roughness), name, new BxDFCheckOptions());
                    // The published fits (they are, constant for constant, those of the authors' Shadertoy) put the albedo of a white
                    // surface up to 6% above 1; the microfacet volume they approximate has albedo 1 (a random walk in it confirms
                    // both). BxDFCheck.VmfDiffuseWhiteAlbedo prints the albedo by angle. The limit is that of the fit
                    BxDFCheck.Expect(result, name, 2e-3, 1.07);
                }
            }
        }

        // The albedo of the white VMF surface by angle: where the fits of the model leave 1
        [TEST("BxDFCheck", "VmfDiffuseWhiteAlbedo")]
        public static void BxDFCheck_VmfDiffuseWhiteAlbedo()
        {
            RNG rng = new RNG(7);
            foreach (Float roughness in new Float[] { 0.1f, 0.3f, 0.5f, 0.7f, 1.0f })
            {
                string line = pbrt.StringPrintf("MEASURE vmf white albedo, roughness %.1f:", roughness);
                BxDF bxdf = new VmfDiffuseBxDF(new SampledSpectrum(1f), roughness);
                foreach (Float cosine in new Float[] { 1f, 0.9f, 0.7f, 0.5f, 0.3f, 0.2f, 0.1f, 0.05f, 0.02f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                    double sum = 0;
                    int n = 100000;
                    for (int i = 0; i < n; ++i)
                    {
                        pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                     TransportMode.Importance, BxDFReflTransFlags.All);
                        if (bs.has_value())
                        {
                            sum += bs.value().f.Average() * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                        }
                    }
                    line += pbrt.StringPrintf(" %.2f:%.4f", cosine, sum / n);
                }
                Console.Error.WriteLine(line);
            }
        }

        // The multiple scattering conductor (the walk of Heitz et al. 2016) on each distribution. Its f is an estimate: the
        // reciprocity is judged by the squared difference of a pair against its variance, and the energy by the albedo of a
        // perfectly reflecting microsurface, which the walk returns whole.
        static BxDFCheckResult MultiScatter(MicrofacetDistribution distribution, MicrosurfaceHeight height, int maxOrder, string name, int samples)
        {
            // A reflectance of 1 at every angle, to within 1e-7
            BxDF bxdf = new MultiScatterConductorBxDF(distribution, height, new SampledSpectrum(1f), new SampledSpectrum(1e4f), maxOrder);
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.cosines = new Float[] { 0.9f, 0.5f, 0.2f };
            options.pairs = 400;
            options.repeats = 24;
            options.samples = samples;
            options.bothSides = false;
            BxDFCheckResult result = BxDFCheck.Run(bxdf, name, options);
            EXPECT_EQ(0, result.invalid, name + ": negative or non finite values");
            EXPECT_LT(result.reciprocityChi2, 1.6, name + ": not reciprocal");
            EXPECT_LT(result.albedoMax, 1.01, name + ": albedo above one");
            EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 1e-2, name + ": Sample_f and f disagree about the albedo");
            EXPECT_LT(result.pdfMax, 1.03, name + ": the pdf integrates to more than one");
            return result;
        }

        [TEST("BxDFCheck", "MultiScatterConductorGGX")]
        public static void BxDFCheck_MultiScatterConductorGGX()
        {
            BxDFCheckResult result = MultiScatter(new TrowbridgeReitzDistribution(0.5f, 0.5f), new MicrosurfaceHeightUniform(), 100,
                                                  "MultiScatterConductor GGX 0.5 uniform heights", 200000);
            EXPECT_GT(result.albedoMin, 0.99, "a perfectly reflecting microsurface loses energy");
            EXPECT_GT(result.pdfMin, 0.97);

            // The heights do not matter
            BxDFCheckResult gaussian = MultiScatter(new TrowbridgeReitzDistribution(0.5f, 0.5f), new MicrosurfaceHeightGaussian(), 100,
                                                    "MultiScatterConductor GGX 0.5 Gaussian heights", 200000);
            EXPECT_GT(gaussian.albedoMin, 0.99);

            // One reflection only: the single scattering of pbrt
            BxDFCheckResult first = MultiScatter(new TrowbridgeReitzDistribution(0.5f, 0.5f), new MicrosurfaceHeightUniform(), 1,
                                                 "MultiScatterConductor GGX 0.5 first order", 200000);
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.cosines = new Float[] { 0.9f, 0.5f, 0.2f };
            options.bothSides = false;
            BxDFCheckResult single = BxDFCheck.Run(new ConductorBxDF(new TrowbridgeReitzDistribution(0.5f, 0.5f), new SampledSpectrum(1f),
                                                                     new SampledSpectrum(1e4f)), "Conductor GGX 0.5 (pbrt, single scattering)", options);
            EXPECT_LT(Math.Abs(first.albedoMin - single.albedoMin), 0.01);
            EXPECT_LT(Math.Abs(first.albedoMax - single.albedoMax), 0.01);
        }

        [TEST("BxDFCheck", "MultiScatterConductorBeckmann")]
        public static void BxDFCheck_MultiScatterConductorBeckmann()
        {
            BxDFCheckResult result = MultiScatter(new BeckmannDistribution(0.5f, 0.5f), new MicrosurfaceHeightUniform(), 100,
                                                  "MultiScatterConductor Beckmann 0.5", 40000);
            EXPECT_GT(result.albedoMin, 0.99, "a perfectly reflecting microsurface loses energy");
        }

        [TEST("BxDFCheck", "MultiScatterConductorStudentT")]
        public static void BxDFCheck_MultiScatterConductorStudentT()
        {
            BxDFCheckResult result = MultiScatter(new StudentTDistribution(0.5f, 0.5f, 3f), new MicrosurfaceHeightUniform(), 100,
                                                  "MultiScatterConductor StudentT 0.5 gamma 3", 25000);
            EXPECT_GT(result.albedoMin, 0.99, "a perfectly reflecting microsurface loses energy");
        }

        // The multiple scattering diffuse and dielectric microsurfaces. Neither absorbs here (a white diffuse microfacet, a
        // dielectric): the albedo is 1.
        static BxDFCheckResult MultiScatterOther(BxDF bxdf, string name, int samples, bool bothSides, bool pairStatistic = true)
        {
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.cosines = new Float[] { 0.9f, 0.5f, 0.2f };
            options.pairs = 600;
            options.repeats = 24;
            options.samples = samples;
            options.bothSides = bothSides;
            BxDFCheckResult result = BxDFCheck.Run(bxdf, name, options);
            EXPECT_EQ(0, result.invalid, name + ": negative or non finite values");
            EXPECT_GT(result.reciprocityPairs, 100, name + ": too few pairs with a value");
            if (pairStatistic)
            {
                EXPECT_LT(result.reciprocityChi2, 1.6, name + ": not reciprocal");
            }
            EXPECT_LT(result.albedoMax, 1.01, name + ": albedo above one");
            EXPECT_GT(result.albedoMin, 0.99, name + ": energy is lost");
            EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 1e-2, name + ": Sample_f and f disagree about the albedo");
            EXPECT_LT(result.pdfMax, 1.04, name + ": the pdf integrates to more than one");
            EXPECT_GT(result.pdfMin, 0.96, name + ": the pdf integrates to less than one");
            return result;
        }

        [TEST("BxDFCheck", "MultiScatterDiffuse")]
        public static void BxDFCheck_MultiScatterDiffuse()
        {
            SampledSpectrum white = new SampledSpectrum(1f);
            MultiScatterOther(new MultiScatterDiffuseBxDF(new TrowbridgeReitzDistribution(0.5f, 0.5f), new MicrosurfaceHeightUniform(), white, 200),
                              "MultiScatterDiffuse GGX 0.5", 200000, false);
            MultiScatterOther(new MultiScatterDiffuseBxDF(new TrowbridgeReitzDistribution(0.5f, 0.5f), new MicrosurfaceHeightGaussian(), white, 200),
                              "MultiScatterDiffuse GGX 0.5 Gaussian heights", 200000, false);
            MultiScatterOther(new MultiScatterDiffuseBxDF(new BeckmannDistribution(0.5f, 0.5f), new MicrosurfaceHeightUniform(), white, 200),
                              "MultiScatterDiffuse Beckmann 0.5", 30000, false);
            MultiScatterOther(new MultiScatterDiffuseBxDF(new StudentTDistribution(0.5f, 0.5f, 3f), new MicrosurfaceHeightUniform(), white, 200),
                              "MultiScatterDiffuse StudentT 0.5 gamma 3", 20000, false);
        }

        // The estimate of a refraction has a heavy tail (single estimates of 10000 times the mean occur), and the variance of a
        // pair taken from a few thousand estimates misses it: the squared difference in units of that variance is not a test
        // here. The reciprocity is tested directly instead (RandomPairReciprocity): pairs of directions over both sides and
        // down to grazing, each estimated until the difference is known, at the 4 sigma limit of the other walks.
        static void Dielectric(MicrofacetDistribution distribution, MicrosurfaceHeight height, string name, int pairs, int estimates)
        {
            BxDF bxdf = new MultiScatterDielectricBxDF(distribution, height, 1.5f, 200);
            MultiScatterOther(bxdf, name, 200000, true, false);
            RandomPairReciprocity(bxdf, name, pairs, estimates);
        }

        // The difference of f(wo, wi, Radiance) and f(wi, wo, Importance) from `estimates` estimates of each, in units of its
        // standard error, and as a fraction
        static void PairDifference(BxDF bxdf, Vector3f wo, Vector3f wi, int estimates, ref RNG rng, out double z, out double relative, out double mean, out double other)
        {
            double sumA = 0, sumA2 = 0, sumB = 0, sumB2 = 0;
            for (int i = 0; i < estimates; ++i)
            {
                Vector3f d = 1e-4f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                Vector3f a = Normalize(wo + d), b = Normalize(wi - d);
                double x = bxdf.f(a, b, TransportMode.Radiance).Average();
                double y = bxdf.f(b, a, TransportMode.Importance).Average();
                sumA += x;
                sumA2 += x * x;
                sumB += y;
                sumB2 += y * y;
            }
            double meanA = sumA / estimates, meanB = sumB / estimates;
            double error = Math.Sqrt((sumA2 / estimates - meanA * meanA + sumB2 / estimates - meanB * meanB) / estimates);
            mean = Math.Max(meanA, meanB);
            other = Math.Min(meanA, meanB);
            z = error > 0 ? Math.Abs(meanA - meanB) / error : 0;
            relative = mean > 0 ? Math.Abs(meanA - meanB) / mean : 0;
        }

        // A pair whose difference stands out (above 3 sigma) is estimated again, independently and 16 times longer: a
        // difference that is real grows fourfold in sigma, and one that came from a variance taken too small (the heavy tail)
        // does not come back. The second look is what is judged.
        static void RandomPairReciprocity(BxDF bxdf, string name, int pairs, int estimates)
        {
            RNG rng = new RNG(1234);
            double worstZ = 0, worstRelative = 0;
            int used = 0, again = 0, negligible = 0;
            for (int pair = 0; pair < pairs; ++pair)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (Math.Abs(wo.z) < 0.02f || Math.Abs(wi.z) < 0.02f)
                {
                    continue;
                }
                double z, relative, mean, other;
                PairDifference(bxdf, wo, wi, estimates, ref rng, out z, out relative, out mean, out other);
                if (mean < 1e-3)
                {
                    // Where the BSDF is next to nothing (it is of the order of 0.01 to 1 elsewhere) its estimate is made of
                    // rare walks, and no number of them that a test can afford tells the two directions apart
                    ++negligible;
                    continue;
                }
                if (z > 3)
                {
                    ++again;
                    double first = z;
                    PairDifference(bxdf, wo, wi, 16 * estimates, ref rng, out z, out relative, out mean, out other);
                    Console.Error.WriteLine(pbrt.StringPrintf("MEASURE bxdf %s: pair wo.z %.3f wi.z %.3f at %.1f sigma; again with %d estimates: %.1f sigma, relative %.2e (%.3g against %.3g)",
                                                              name, wo.z, wi.z, first, 16 * estimates, z, relative, mean, other));
                }
                ++used;
                worstZ = Math.Max(worstZ, z);
                if (mean > 0.02)
                {
                    worstRelative = Math.Max(worstRelative, relative);
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE bxdf %s: reciprocity of %d random pairs (both sides, to grazing), %d estimates each (%d pairs looked at " +
                                                      "again, %d below 0.001 left out): worst %.1f sigma, worst relative difference %.2e (pairs above 0.02)",
                                                      name, used, estimates, again, negligible, worstZ, worstRelative));
            EXPECT_GT(used, pairs / 4);
            EXPECT_LT(worstZ, 4.0, name + ": not reciprocal");
        }

        [TEST("BxDFCheck", "MultiScatterDielectricGGX")]
        public static void BxDFCheck_MultiScatterDielectricGGX()
        {
            Dielectric(new TrowbridgeReitzDistribution(0.5f, 0.5f), new MicrosurfaceHeightUniform(), "MultiScatterDielectric GGX 0.5 eta 1.5", 48, 300000);
            Dielectric(new TrowbridgeReitzDistribution(0.5f, 0.5f), new MicrosurfaceHeightGaussian(), "MultiScatterDielectric GGX 0.5 eta 1.5 Gaussian heights",
                       48, 300000);
        }

        [TEST("BxDFCheck", "MultiScatterDielectricBeckmann")]
        public static void BxDFCheck_MultiScatterDielectricBeckmann()
        {
            Dielectric(new BeckmannDistribution(0.5f, 0.5f), new MicrosurfaceHeightUniform(), "MultiScatterDielectric Beckmann 0.5 eta 1.5", 24, 60000);
        }

        [TEST("BxDFCheck", "MultiScatterDielectricStudentT")]
        public static void BxDFCheck_MultiScatterDielectricStudentT()
        {
            Dielectric(new StudentTDistribution(0.5f, 0.5f, 3f), new MicrosurfaceHeightUniform(), "MultiScatterDielectric StudentT 0.5 gamma 3 eta 1.5", 16, 40000);
        }

        // A closer look at the reciprocity of the walks: a few pairs of directions, each estimated many times, so that the
        // difference of a pair is known to a fraction of a percent
        static void TightReciprocity(BxDF bxdf, string name, bool transmission, int repeats = 40000)
        {
            RNG rng = new RNG(99);
            double worstZ = 0, worstRelative = 0;
            for (int pair = 0; pair < 8; ++pair)
            {
                Vector3f wo = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.15f + 0.8f * rng.Uniform<Float>()));
                Vector3f wi = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.15f + 0.8f * rng.Uniform<Float>()));
                if (transmission && (pair % 2) == 1)
                {
                    wi = -wi;
                }
                double sumA = 0, sumA2 = 0, sumB = 0, sumB2 = 0;
                for (int i = 0; i < repeats; ++i)
                {
                    Vector3f d = 1e-4f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
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
                worstZ = Math.Max(worstZ, Math.Abs(meanA - meanB) / error);
                worstRelative = Math.Max(worstRelative, Math.Abs(meanA - meanB) / Math.Max(meanA, meanB));
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE bxdf %s: reciprocity of 8 pairs, %d estimates each: worst %.1f sigma, worst relative difference %.2e",
                                                      name, repeats, worstZ, worstRelative));
            EXPECT_LT(worstZ, 4.0, name + ": not reciprocal");
        }

        [TEST("BxDFCheck", "MultiScatterReciprocity")]
        public static void BxDFCheck_MultiScatterReciprocity()
        {
            SampledSpectrum one = new SampledSpectrum(1f);
            TightReciprocity(new MultiScatterConductorBxDF(new TrowbridgeReitzDistribution(0.5f, 0.5f), new MicrosurfaceHeightUniform(), one,
                                                           new SampledSpectrum(1e4f), 100), "MultiScatterConductor GGX 0.5", false);
            TightReciprocity(new MultiScatterDiffuseBxDF(new TrowbridgeReitzDistribution(0.5f, 0.5f), new MicrosurfaceHeightUniform(), one, 200),
                             "MultiScatterDiffuse GGX 0.5", false);
            TightReciprocity(new MultiScatterDielectricBxDF(new TrowbridgeReitzDistribution(0.5f, 0.5f), new MicrosurfaceHeightUniform(), 1.5f, 200),
                             "MultiScatterDielectric GGX 0.5 eta 1.5", true, 2000000);
            TightReciprocity(new MultiScatterDiffuseBxDF(new StudentTDistribution(0.5f, 0.5f, 3f), new MicrosurfaceHeightUniform(), one, 200),
                             "MultiScatterDiffuse StudentT 0.5 gamma 3", false);
        }

        // Thin films on the microfacets of the multiple scattering microsurfaces. With one reflection allowed the conductor
        // is the rough thin film BxDF; the films do not absorb, so the dielectric still returns all the light; and both
        // stay reciprocal.
        [TEST("BxDFCheck", "MultiScatterThinFilm")]
        public static void BxDFCheck_MultiScatterThinFilm()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            SampledSpectrum eta = new SampledSpectrum(0.2f), k = new SampledSpectrum(3.9f);
            MicrofacetDistribution distribution = new TrowbridgeReitzDistribution(0.5f, 0.5f);
            Float[] layerEta = new Float[] { 1.5f }, layerThickness = new Float[] { 400 };
            ThinFilmStack film = new ThinFilmStack(lambda, layerEta, layerThickness);
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.cosines = new Float[] { 0.9f, 0.5f, 0.2f };
            options.pairs = 600;
            options.repeats = 24;
            options.bothSides = false;
            BxDFCheckResult single = BxDFCheck.Run(new ThinFilmBxDF(lambda, layerEta, layerThickness, 1.5f, eta, k, true, distribution),
                                                   "RoughThinFilm on a conductor, GGX 0.5", options);
            BxDFCheckResult first = BxDFCheck.Run(new MultiScatterConductorBxDF(distribution, new MicrosurfaceHeightUniform(), eta, k, 1, film),
                                                  "MultiScatterConductor with a film, GGX 0.5, one reflection", options);
            EXPECT_LT(Math.Abs(first.albedoMax - single.albedoMax), 0.01);
            EXPECT_LT(Math.Abs(first.albedoMin - single.albedoMin), 0.01);
            TightReciprocity(new MultiScatterConductorBxDF(distribution, new MicrosurfaceHeightUniform(), eta, k, 100, film),
                             "MultiScatterConductor with a film, GGX 0.5", false);

            ThinFilmStack coating = new ThinFilmStack(lambda, new Float[] { 2.1f }, new Float[] { 300 });
            BxDF glass = new MultiScatterDielectricBxDF(distribution, new MicrosurfaceHeightUniform(), 1.5f, 200, coating);
            MultiScatterOther(glass, "MultiScatterDielectric with a film, GGX 0.5 eta 1.5", 200000, true, false);
            RandomPairReciprocity(glass, "MultiScatterDielectric with a film, GGX 0.5 eta 1.5", 60, 300000);
        }
    }
}
