// (not in pbrt-v4) facetforge_polarised_test.cs
//
// The Mueller matrices of the Facet Forge walk (util/facetforge_polarised.cs) and of the layered BxDFs on a walked interface:
//   - the scalar value that comes with a matrix is that of the scalar walk, to the bit;
//   - with one scattering the matrix is that of the microfacet lobes of pbrt (conductor; dielectric on either side and through),
//     and with a film on the facets its first element is the scalar Airy sum of the fork;
//   - with every reflection the mean Mueller matrix is that of the walk of Heitz (MultiScatterConductorBxDF);
//   - a path from the camera and the same path from the light have the same matrix;
//   - the light that leaves under an even unpolarised surround is the same by sampled paths and by evaluated directions, for the
//     conductor, the dielectric, two scales, strata, and the two coated materials on a walked coat.

using System;
using System.Collections.Generic;
using testing;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public static unsafe class FacetForgePolarisedTests
    {
        static SampledSpectrum S(Float v) { return new SampledSpectrum(v); }

        static ff.Surface Heightfield(ff.Kind kind, double alpha, double gamma, ff.FacetKind facet)
        {
            ff.Surface s = new ff.Surface();
            s.ndf.n = 1;
            s.ndf.c[0].InitHeightfield(kind, alpha, alpha, gamma);
            s.facet.kind = facet;
            s.facet.eta = S(0.2f);
            s.facet.k = S(3.9f);
            s.facet.R = S(1f);
            return s;
        }

        internal static bool Matrix(BxDF bxdf, Vector3f wo, Vector3f wi, TransportMode mode, out MuellerMatrix matrix)
        {
            BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), bxdf);
            Attenuation A;
            bool ok = PolarisedPathIntegrator.RoughAttenuation(bsdf, default, wo, wi, S(1), mode, out A);
            matrix = ok ? A.GetMuellerMatrix() : default;
            return ok;
        }

        internal static double Difference(MuellerMatrix a, MuellerMatrix b)
        {
            double worst = 0;
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    worst = Math.Max(worst, Math.Abs(a.Get(row, column)[0] - b.Get(row, column)[0]));
                }
            }
            return worst;
        }

        internal static Vector3f Upper(ref RNG rng)
        {
            Vector3f w = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
            w.z = 0.15f + 0.8f * Math.Abs(w.z);
            return Normalize(w);
        }

        [TEST("FacetForgePolarised", "OneScatteringIsTheLobeOfPbrt")]
        public static void OneScatteringIsTheLobeOfPbrt()
        {
            RNG rng = new RNG(2);
            ff.Surface metal = Heightfield(ff.Kind.GGX, 0.4, 2, ff.FacetKind.Conductor), glassSurface = Heightfield(ff.Kind.GGX, 0.4, 2, ff.FacetKind.Dielectric);
            metal.maxWalk = 1;
            glassSurface.maxWalk = 1;
            BxDF single = new ConductorBxDF(new TrowbridgeReitzDistribution(0.4f, 0.4f), S(0.2f), S(3.9f));
            FacetForgeBxDF walk = new FacetForgeBxDF(metal, null, 1.5f, false);
            BxDF glass = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.4f, 0.4f));
            FacetForgeBxDF glassWalk = new FacetForgeBxDF(glassSurface, null, 1.5f, true);
            double worstWalk = 0, worstGlass = 0, worstScalar = 0;
            int compared = 0, comparedGlass = 0;
            BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), glassWalk);
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                foreach (TransportMode mode in new TransportMode[] { TransportMode.Radiance, TransportMode.Importance })
                {
                    MuellerMatrix reference, a;
                    if (Matrix(single, wo, wi, mode, out reference) && Matrix(walk, wo, wi, mode, out a))
                    {
                        worstWalk = Math.Max(worstWalk, Difference(reference, a));
                        ++compared;
                    }
                    for (int c = 0; c < 4; ++c)
                    {
                        Vector3f go = (c & 1) != 0 ? -wo : wo, gi = (c & 2) != 0 ? -wi : wi;
                        // (pairs with a value: next to the critical angle the single precision Fresnel term of the lobe of pbrt has a
                        // transmittance some percent off, where the BSDF is next to nothing)
                        if (glass.f(go, gi, mode)[0] > 1e-3f && Matrix(glass, go, gi, mode, out reference) && Matrix(glassWalk, go, gi, mode, out a))
                        {
                            worstGlass = Math.Max(worstGlass, Difference(reference, a));
                            ++comparedGlass;
                        }
                        // The scalar value that comes with the matrix is that of the scalar walk
                        SampledSpectrum with = glassWalk.f(frame, go, gi, mode, out bool _, out Attenuation _), without = glassWalk.f(go, gi, mode);
                        worstScalar = Math.Max(worstScalar, Math.Abs(with[0] - without[0]));
                    }
                }
            }
            Console.WriteLine("MEASURE facet forge polarised, one scattering against the lobes of pbrt (worst element): conductor {0:E1} ({1} pairs), dielectric {2:E1} " +
                              "({3} pairs); scalar with and without the matrix {4:E1}", worstWalk, compared, worstGlass, comparedGlass, worstScalar);
            EXPECT_GT(compared, 1000);
            EXPECT_GT(comparedGlass, 1000);
            EXPECT_LT(worstWalk, 1e-3);
            EXPECT_LT(worstGlass, 1e-3);
            EXPECT_EQ(0.0, worstScalar);
        }

        // With every reflection the scalar is still that of the scalar walk, and a film on the facets has the first element of
        // its matrix equal to the scalar reflectance of the fork (two codes for the Airy sum)
        [TEST("FacetForgePolarised", "ScalarAndFilm")]
        public static void ScalarAndFilm()
        {
            RNG rng = new RNG(4);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            double worstScalar = 0, worstFilm = 0;
            int films = 0;
            foreach (ff.FacetKind kind in new ff.FacetKind[] { ff.FacetKind.Conductor, ff.FacetKind.Dielectric })
            {
                ff.Surface full = Heightfield(ff.Kind.StudentT, 0.4, 3, kind);
                ff.Surface filmed = Heightfield(ff.Kind.GGX, 0.4, 2, kind);
                filmed.maxWalk = 1;
                filmed.facet.film.thickness = 380;
                filmed.facet.film.index = 1.8f;
                filmed.facet.film.weight = 1;
                filmed.facet.film.lambda = lambda;
                FacetForgeBxDF a = new FacetForgeBxDF(full, null, 1.5f, kind == ff.FacetKind.Dielectric);
                FacetForgeBxDF b = new FacetForgeBxDF(filmed, null, 1.5f, kind == ff.FacetKind.Dielectric);
                BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), a);
                for (int i = 0; i < 1500; ++i)
                {
                    Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                    if (kind == ff.FacetKind.Dielectric)
                    {
                        if ((i & 1) != 0) wo = -wo;
                        if ((i & 2) != 0) wi = -wi;
                    }
                    SampledSpectrum with = a.f(frame, wo, wi, TransportMode.Radiance, out bool _, out Attenuation _), without = a.f(wo, wi, TransportMode.Radiance);
                    worstScalar = Math.Max(worstScalar, Math.Abs(with[0] - without[0]));
                    SampledSpectrum value = b.f(frame, wo, wi, TransportMode.Radiance, out bool polarised, out Attenuation A);
                    if (polarised && value[0] > 1e-3f)
                    {
                        MuellerMatrix m = A.GetMuellerMatrix();
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            worstFilm = Math.Max(worstFilm, Math.Abs(m.Get(0, 0)[c] - 1));
                        }
                        ++films;
                    }
                }
            }
            Console.WriteLine("MEASURE facet forge polarised: scalar with and without the matrix, every reflection {0:E1}; first element of the matrix of a filmed facet " +
                              "against the scalar film of the fork {1:E1} ({2} pairs)", worstScalar, worstFilm, films);
            EXPECT_EQ(0.0, worstScalar);
            EXPECT_GT(films, 1000);
            EXPECT_LT(worstFilm, 2e-3);
        }

        // The film of the fork (one layer, the Airy sum of FrThinFilm) and the films of Magi (ThinFilmStack) with that one layer are
        // the same surface: the same f with one scattering, on either side and through, and with every reflection a dielectric
        // under either film returns all the light
        [TEST("FacetForgePolarised", "TheTwoFilms")]
        public static void TheTwoFilms()
        {
            RNG rng = new RNG(44);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            foreach (ff.FacetKind kind in new ff.FacetKind[] { ff.FacetKind.Conductor, ff.FacetKind.Dielectric })
            {
                bool glass = kind == ff.FacetKind.Dielectric;
                ff.Surface fork = Heightfield(ff.Kind.GGX, 0.4, 2, kind);
                fork.facet.film.thickness = 380;
                fork.facet.film.index = 1.8f;
                fork.facet.film.weight = 1;
                fork.facet.film.lambda = lambda;
                ff.Surface magi = Heightfield(ff.Kind.GGX, 0.4, 2, kind);
                magi.facet.stack = new ThinFilmStack(lambda, new Float[] { 1.8f }, new Float[] { 380 });
                magi.facet.hasStack = true;
                ff.Surface forkOnce = fork.Clone(), magiOnce = magi.Clone();
                forkOnce.maxWalk = 1;
                magiOnce.maxWalk = 1;
                FacetForgeBxDF a = new FacetForgeBxDF(forkOnce, null, 1.5f, glass), b = new FacetForgeBxDF(magiOnce, null, 1.5f, glass);
                double worst = 0;
                int count = 0;
                for (int i = 0; i < 3000; ++i)
                {
                    Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                    if (glass)
                    {
                        if ((i & 1) != 0) wo = -wo;
                        if ((i & 2) != 0) wi = -wi;
                    }
                    SampledSpectrum x = a.f(wo, wi, TransportMode.Radiance), y = b.f(wo, wi, TransportMode.Radiance);
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        if (Math.Max(x[c], y[c]) > 1e-3f)
                        {
                            worst = Math.Max(worst, Math.Abs(x[c] - y[c]) / Math.Max(x[c], y[c]));
                            ++count;
                        }
                    }
                }
                FacetForgeBxDF fullFork = new FacetForgeBxDF(fork, null, 1.5f, glass), fullMagi = new FacetForgeBxDF(magi, null, 1.5f, glass);
                double albedoFork = 0, albedoMagi = 0;
                Vector3f from = Normalize(new Vector3f(0.6f, 0, 0.8f));
                int samples = 100000;
                for (int i = 0; i < samples; ++i)
                {
                    Float uc = rng.Uniform<Float>();
                    Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    pstd.optional<BSDFSample> s1 = fullFork.Sample_f(from, uc, u, TransportMode.Importance, BxDFReflTransFlags.All);
                    pstd.optional<BSDFSample> s2 = fullMagi.Sample_f(from, uc, u, TransportMode.Importance, BxDFReflTransFlags.All);
                    if (s1) albedoFork += s1.value().f.Average() * AbsCosTheta(s1.value().wi) / s1.value().pdf / samples;
                    if (s2) albedoMagi += s2.value().f.Average() * AbsCosTheta(s2.value().wi) / s2.value().pdf / samples;
                }
                Console.WriteLine("MEASURE facet forge, the film of the fork against the same layer as a film of Magi, {0}: f with one scattering {1:E1} ({2} values); " +
                                  "albedo with every reflection {3:F5} against {4:F5}", kind, worst, count, albedoFork, albedoMagi);
                EXPECT_GT(count, 1000);
                EXPECT_LT(worst, 2e-3);
                EXPECT_LT(Math.Abs(albedoFork - albedoMagi), 2e-3);
                if (glass)
                {
                    EXPECT_GT(albedoFork, 0.995);
                    EXPECT_GT(albedoMagi, 0.995);
                    EXPECT_LT(albedoFork, 1.002);
                    EXPECT_LT(albedoMagi, 1.002);
                }
            }
        }

        // The mean Mueller matrix (times the BSDF) of two BxDFs for a pair of directions, element by element, in units of the
        // error of the difference
        static double MeanDifference(BxDF first, BxDF second, Vector3f wo, Vector3f wi, TransportMode modeFirst, TransportMode modeSecond, bool swapSecond,
                                     int estimates, ref RNG rng, out double largest)
        {
            double[] a = new double[16], b = new double[16], square = new double[16];
            BSDF bsdfFirst = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), first), bsdfSecond = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), second);
            for (int e = 0; e < estimates; ++e)
            {
                Vector3f d = 1e-4f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                Vector3f o = Normalize(wo + d), i = Normalize(wi - d);
                SampledSpectrum f1 = bsdfFirst.f(o, i, modeFirst, out bool p1, out Attenuation A1);
                SampledSpectrum f2 = swapSecond ? bsdfSecond.f(i, o, modeSecond, out bool p2, out Attenuation A2) : bsdfSecond.f(o, i, modeSecond, out p2, out A2);
                MuellerMatrix m1 = p1 ? A1.GetMuellerMatrix() : default, m2 = p2 ? A2.GetMuellerMatrix() : default;
                for (int c = 0; c < 16; ++c)
                {
                    double x = p1 ? m1.Get(c / 4, c % 4)[0] * f1[0] : 0, y = p2 ? m2.Get(c / 4, c % 4)[0] * f2[0] : 0;
                    a[c] += x;
                    b[c] += y;
                    square[c] += (x - y) * (x - y);
                }
            }
            double worst = 0;
            largest = 0;
            for (int c = 0; c < 16; ++c)
            {
                double difference = (a[c] - b[c]) / estimates;
                double error = Math.Sqrt(square[c]) / estimates + 1e-5;
                worst = Math.Max(worst, Math.Abs(difference) / error);
                largest = Math.Max(largest, Math.Abs(a[c] / estimates));
            }
            return worst;
        }

        [TEST("FacetForgePolarised", "EveryReflectionAgainstTheWalkOfHeitz")]
        public static void EveryReflectionAgainstTheWalkOfHeitz()
        {
            RNG rng = new RNG(6);
            FacetForgeBxDF mine = new FacetForgeBxDF(Heightfield(ff.Kind.GGX, 0.6, 2, ff.FacetKind.Conductor), null, 1.5f, false);
            MultiScatterConductorBxDF theirs = new MultiScatterConductorBxDF(new TrowbridgeReitzDistribution(0.6f, 0.6f), new MicrosurfaceHeightUniform(), S(0.2f), S(3.9f),
                                                                             100);
            double worst = 0;
            for (int pair = 0; pair < 6; ++pair)
            {
                Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                double largest;
                double sigmas = MeanDifference(mine, theirs, wo, wi, TransportMode.Radiance, TransportMode.Radiance, false, 20000, ref rng, out largest);
                Console.WriteLine("MEASURE facet forge polarised, every reflection against the walk of Heitz, pair {0}: worst element {1:F1} sigma (largest element {2:F4})",
                                  pair, sigmas, largest);
                worst = Math.Max(worst, sigmas);
            }
            EXPECT_LT(worst, 4.5);
        }

        [TEST("FacetForgePolarised", "CameraPathIsLightPath")]
        public static void CameraPathIsLightPath()
        {
            RNG rng = new RNG(8);
            FacetForgeBxDF metal = new FacetForgeBxDF(Heightfield(ff.Kind.StudentT, 0.5, 3, ff.FacetKind.Conductor), null, 1.5f, false);
            FacetForgeBxDF glass = new FacetForgeBxDF(Heightfield(ff.Kind.GGX, 0.5, 2, ff.FacetKind.Dielectric), null, 1.5f, true);
            double worst = 0;
            for (int pair = 0; pair < 4; ++pair)
            {
                Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                double largest;
                worst = Math.Max(worst, MeanDifference(metal, metal, wo, wi, TransportMode.Radiance, TransportMode.Importance, true, 10000, ref rng, out largest));
                worst = Math.Max(worst, MeanDifference(glass, glass, wo, wi, TransportMode.Radiance, TransportMode.Importance, true, 10000, ref rng, out largest));
                worst = Math.Max(worst, MeanDifference(glass, glass, -wo, -wi, TransportMode.Radiance, TransportMode.Importance, true, 10000, ref rng, out largest));
                // (through the surface, near the refracted direction)
                Vector3f through = Normalize(new Vector3f(-wo.x / 1.5f + 0.1f, -wo.y / 1.5f, -0.8f));
                double sigmas = MeanDifference(glass, glass, wo, through, TransportMode.Radiance, TransportMode.Importance, true, 10000, ref rng, out largest);
                Console.WriteLine("MEASURE facet forge polarised, camera path against light path through the surface, pair {0}: {1:F1} sigma (largest element {2:F4})", pair,
                                  sigmas, largest);
                worst = Math.Max(worst, sigmas);
            }
            EXPECT_LT(worst, 4.5);
        }

        internal static void SampledAgainstEvaluated(BxDF bxdf, string name, int samples)
        {
            RNG rng = new RNG(13);
            BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), bxdf);
            Vector3f wo = Normalize(new Vector3f(0.75f, 0.1f, 0.5f)), up = new Vector3f(0, 0, 1);
            double[] sumSampled = new double[4], sumEvaluated = new double[4];
            double squareSampled = 0, squareEvaluated = 0;
            int polarisedSamples = 0;
            for (int i = 0; i < samples; ++i)
            {
                bool polarised;
                Attenuation A;
                pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                             TransportMode.Radiance, BxDFReflTransFlags.All, out polarised, out A);
                if ((bool)bs && polarised)
                {
                    ++polarisedSamples;
                    SampledSpectrum weight = bs.value().f * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                    StokesVector light = ((A * weight) * new PolarisedLightSystem.Light(S(1))).RepresentInDirection(wo, up).ToStokesVector();
                    sumSampled[0] += light.s0[0]; sumSampled[1] += light.s1[0]; sumSampled[2] += light.s2[0]; sumSampled[3] += light.s3[0];
                    squareSampled += Sqr((double)light.s1[0]);
                }
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                SampledSpectrum f = bsdf.f(wo, wi, TransportMode.Radiance, out polarised, out A);
                if (polarised && (bool)f)
                {
                    SampledSpectrum weight = f * (AbsCosTheta(wi) * 4 * Pi);
                    StokesVector light = ((A * weight) * new PolarisedLightSystem.Light(S(1))).RepresentInDirection(wo, up).ToStokesVector();
                    sumEvaluated[0] += light.s0[0]; sumEvaluated[1] += light.s1[0]; sumEvaluated[2] += light.s2[0]; sumEvaluated[3] += light.s3[0];
                    squareEvaluated += Sqr((double)light.s1[0]);
                }
            }
            double sampledIntensity = sumSampled[0] / samples, evaluatedIntensity = sumEvaluated[0] / samples;
            double sampledS1 = sumSampled[1] / samples, evaluatedS1 = sumEvaluated[1] / samples;
            double error = Math.Sqrt((squareSampled / samples - sampledS1 * sampledS1 + squareEvaluated / samples - evaluatedS1 * evaluatedS1) / samples);
            double sigmas = Math.Abs(sampledS1 - evaluatedS1) / error;
            Console.WriteLine("MEASURE polarised {0}: light under an even unpolarised surround, sampled against evaluated: intensity {1:F4} / {2:F4}, s1 {3:F5} / {4:F5} " +
                              "({5:F1} sigma, error {6:F5}), s2 {7:F5} / {8:F5}; {9:P1} of the samples have a matrix", name, sampledIntensity, evaluatedIntensity, sampledS1,
                              evaluatedS1, sigmas, error, sumSampled[2] / samples, sumEvaluated[2] / samples, (double)polarisedSamples / samples);
            EXPECT_LT(sigmas, 4.0, name);
            EXPECT_LT(Math.Abs(sampledIntensity - evaluatedIntensity), 0.02 * evaluatedIntensity, name);
            EXPECT_GT(Math.Abs(evaluatedS1), 3 * error, name + ": no polarisation to compare");
        }

        [TEST("FacetForgePolarised", "SampledAgainstEvaluated")]
        public static void SampledAgainstEvaluatedTest()
        {
            SampledAgainstEvaluated(new FacetForgeBxDF(Heightfield(ff.Kind.StudentT, 0.5, 3, ff.FacetKind.Conductor), null, 1.5f, false), "facet forge conductor", 300000);
            SampledAgainstEvaluated(new FacetForgeBxDF(Heightfield(ff.Kind.GGX, 0.5, 2, ff.FacetKind.Dielectric), null, 1.5f, true), "facet forge dielectric", 300000);
            ff.Surface strata = Heightfield(ff.Kind.Beckmann, 0.5, 2, ff.FacetKind.Conductor);
            strata.ndf.n = 2;
            strata.ndf.c[0].w = 0.5;
            strata.ndf.c[1].InitHeightfield(ff.Kind.GGX, 0.1, 0.1, 2);
            strata.ndf.c[1].w = 0.5;
            strata.ndf.layered = true;
            SampledAgainstEvaluated(new FacetForgeBxDF(strata, null, 1.5f, false), "facet forge strata, conductor", 300000);
            SampledAgainstEvaluated(new FacetForgeBxDF(Heightfield(ff.Kind.GGX, 0.4, 2, ff.FacetKind.Conductor), Heightfield(ff.Kind.Beckmann, 0.2, 2, ff.FacetKind.Conductor),
                                                       1.5f, false), "facet forge two scales, conductor", 150000);
        }

        // The coated materials on a coat that is a walk: the layered BxDF takes the matrix of every event at the coat from the
        // walk that gave it. Against the coated diffuse of pbrt for a coat that is nearly smooth, where the two are the same
        // surface.
        [TEST("FacetForgePolarised", "CoatedOnAWalkedCoat")]
        public static void CoatedOnAWalkedCoat()
        {
            FacetForgeBxDF forge = new FacetForgeBxDF(Heightfield(ff.Kind.GGX, 0.3, 2, ff.FacetKind.Dielectric), null, 1.5f, true);
            FacetCoatedDiffuseBxDF facetCoated = new FacetCoatedDiffuseBxDF(forge, new DiffuseBxDF(S(0.7f)), 0.01f, S(0), 0, 10, 1);
            EXPECT_TRUE(facetCoated.HasMuellerMatrix());
            SampledAgainstEvaluated(facetCoated, "facet coated diffuse, GGX 0.3", 200000);
            MultiScatterCoatedDiffuseBxDF walked = new MultiScatterCoatedDiffuseBxDF(
                new MultiScatterDielectricBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), new MicrosurfaceHeightUniform(), 1.5f, 16), new DiffuseBxDF(S(0.7f)), 0.01f, S(0),
                0, 10, 1);
            EXPECT_TRUE(walked.HasMuellerMatrix());
            SampledAgainstEvaluated(walked, "coated diffuse, walked coat GGX 0.3", 200000);

            // The same light as the coated diffuse of pbrt, for a coat smooth enough that one scattering is all there is
            RNG rng = new RNG(31);
            Vector3f wo = Normalize(new Vector3f(0.75f, 0.1f, 0.5f)), up = new Vector3f(0, 0, 1);
            BxDF[] three = new BxDF[] {
                new CoatedDiffuseBxDF(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.05f, 0.05f)), new DiffuseBxDF(S(0.7f)), 0.01f, S(0), 0, 10, 1),
                new FacetCoatedDiffuseBxDF(new FacetForgeBxDF(Heightfield(ff.Kind.GGX, 0.05, 2, ff.FacetKind.Dielectric), null, 1.5f, true), new DiffuseBxDF(S(0.7f)), 0.01f,
                                           S(0), 0, 10, 1),
                new MultiScatterCoatedDiffuseBxDF(new MultiScatterDielectricBxDF(new TrowbridgeReitzDistribution(0.05f, 0.05f), new MicrosurfaceHeightUniform(), 1.5f, 16),
                                                  new DiffuseBxDF(S(0.7f)), 0.01f, S(0), 0, 10, 1) };
            double[] intensity = new double[3], s1 = new double[3], error = new double[3];
            int samples = 300000;
            for (int k = 0; k < 3; ++k)
            {
                BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), three[k]);
                double square = 0;
                for (int i = 0; i < samples; ++i)
                {
                    pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Radiance,
                                                                 BxDFReflTransFlags.All, out bool polarised, out Attenuation A);
                    if ((bool)bs && polarised)
                    {
                        SampledSpectrum weight = bs.value().f * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                        StokesVector light = ((A * weight) * new PolarisedLightSystem.Light(S(1))).RepresentInDirection(wo, up).ToStokesVector();
                        intensity[k] += light.s0[0];
                        s1[k] += light.s1[0];
                        square += Sqr((double)light.s1[0]);
                    }
                }
                intensity[k] /= samples;
                s1[k] /= samples;
                error[k] = Math.Sqrt(Math.Max(0, square / samples - s1[k] * s1[k]) / samples);
            }
            Console.WriteLine("MEASURE polarised coated diffuse with a coat of roughness 0.05, light under an even unpolarised surround: pbrt intensity {0:F4} s1 {1:F5}; " +
                              "facet forge coat {2:F4} {3:F5}; walked coat {4:F4} {5:F5} (error of s1 {6:F5})", intensity[0], s1[0], intensity[1], s1[1], intensity[2],
                              s1[2], error[0]);
            // Their intensities differ by a percent, and that is the coats: with a white base under a layer too thin to absorb the
            // walked coats return all the light, and the lobe of pbrt does not (it drops what one scattering sends into the surface)
            BxDF[] white = new BxDF[] {
                new CoatedDiffuseBxDF(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.05f, 0.05f)), new DiffuseBxDF(S(1f)), 1e-6f, S(0), 0, 200, 1),
                new FacetCoatedDiffuseBxDF(new FacetForgeBxDF(Heightfield(ff.Kind.GGX, 0.05, 2, ff.FacetKind.Dielectric), null, 1.5f, true), new DiffuseBxDF(S(1f)), 1e-6f,
                                           S(0), 0, 200, 1),
                new MultiScatterCoatedDiffuseBxDF(new MultiScatterDielectricBxDF(new TrowbridgeReitzDistribution(0.05f, 0.05f), new MicrosurfaceHeightUniform(), 1.5f, 16),
                                                  new DiffuseBxDF(S(1f)), 1e-6f, S(0), 0, 200, 1) };
            double[] albedo = new double[3];
            for (int k = 0; k < 3; ++k)
            {
                for (int i = 0; i < 60000; ++i)
                {
                    pstd.optional<BSDFSample> bs = white[k].Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                     TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (bs)
                    {
                        albedo[k] += bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / 60000;
                    }
                }
            }
            Console.WriteLine("MEASURE the same over a white base, no absorption: albedo pbrt {0:F4}, facet forge coat {1:F4}, walked coat {2:F4}", albedo[0], albedo[1],
                              albedo[2]);
            EXPECT_GT(albedo[1], 0.995);
            EXPECT_GT(albedo[2], 0.995);
            EXPECT_GT(Math.Abs(s1[0]), 5 * error[0]);
            for (int k = 1; k < 3; ++k)
            {
                EXPECT_LT(Math.Abs(s1[k] - s1[0]), 4.5 * Math.Sqrt(error[0] * error[0] + error[k] * error[k]) + 0.02 * Math.Abs(s1[0]));
                // (more light than with the coat of pbrt, by no more than what that coat loses)
                EXPECT_GT(intensity[k], intensity[0]);
                EXPECT_LT((intensity[k] - intensity[0]) / intensity[0], (albedo[k] - albedo[0]) / albedo[0] + 0.005);
            }
        }
    }
}
