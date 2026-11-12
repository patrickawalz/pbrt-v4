// (not in pbrt-v4) polarised_microfacet_test.cs
//
// The Mueller matrices of the BxDFs whose light meets several microfacets (BxDF.Polarised), through the entry that the
// polarised integrator uses (PolarisedPathIntegrator.RoughAttenuation):
//   - with one reflection allowed they are the matrix of the single-scattering BxDF of pbrt for the same directions;
//   - a perfectly reflecting microsurface keeps the weight: no energy is made or lost by the matrices;
//   - the matrix is physical: fully polarised light of any state comes out with a degree of polarisation of at most 1;
//   - the matrix for a path from the camera is that of the same light followed from its source (the order of the
//     microfacets is reversed between the two, so this is what checks that order).

using System;
using testing;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public static unsafe class PolarisedMicrofacetTests
    {
        static SampledSpectrum S(Float v)
        {
            return new SampledSpectrum(v);
        }

        static bool Matrix(BxDF bxdf, Vector3f wo, Vector3f wi, TransportMode mode, out MuellerMatrix matrix)
        {
            BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), bxdf);
            Attenuation A;
            bool ok = PolarisedPathIntegrator.RoughAttenuation(bsdf, default, wo, wi, S(1), mode, out A);
            matrix = ok ? A.GetMuellerMatrix() : default;
            return ok;
        }

        static double Difference(MuellerMatrix a, MuellerMatrix b)
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

        static Vector3f Upper(ref RNG rng)
        {
            Vector3f w = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
            w.z = 0.15f + 0.8f * Math.Abs(w.z);
            return Normalize(w);
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
                double i = output.s0[0], q = output.s1[0], u = output.s2[0], v = output.s3[0];
                if (i < -1e-6)
                {
                    return 1e9;
                }
                worst = Math.Max(worst, Math.Sqrt(q * q + u * u + v * v) - i);
            }
            return worst;
        }

        [TEST("PolarisedMicrofacet", "OneReflectionIsSingleScattering")]
        public static void PolarisedMicrofacet_OneReflectionIsSingleScattering()
        {
            RNG rng = new RNG(2);
            SampledSpectrum eta = S(0.2f), k = S(3.9f);
            BxDF single = new ConductorBxDF(new TrowbridgeReitzDistribution(0.4f, 0.4f), eta, k);
            BxDF walk = new MultiScatterConductorBxDF(new TrowbridgeReitzDistribution(0.4f, 0.4f), new MicrosurfaceHeightUniform(), eta, k, 1);
            BxDF glass = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.4f, 0.4f));
            BxDF glassWalk = new MultiScatterDielectricBxDF(new TrowbridgeReitzDistribution(0.4f, 0.4f), new MicrosurfaceHeightUniform(), 1.5f, 1);
            double worstWalk = 0, worstGlass = 0;
            int compared = 0, comparedGlass = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                foreach (TransportMode mode in new TransportMode[] { TransportMode.Radiance, TransportMode.Importance })
                {
                    MuellerMatrix reference, a;
                    if (Matrix(single, wo, wi, mode, out reference))
                    {
                        if (Matrix(walk, wo, wi, mode, out a))
                        {
                            worstWalk = Math.Max(worstWalk, Difference(reference, a));
                            ++compared;
                        }
                    }
                    // The dielectric: reflection on either side, and transmission both ways
                    for (int c = 0; c < 4; ++c)
                    {
                        Vector3f go = (c & 1) != 0 ? -wo : wo, gi = (c & 2) != 0 ? -wi : wi;
                        if (Matrix(glass, go, gi, mode, out reference) && Matrix(glassWalk, go, gi, mode, out a))
                        {
                            worstGlass = Math.Max(worstGlass, Difference(reference, a));
                            ++comparedGlass;
                        }
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised one reflection against the single-scattering BxDFs of pbrt (worst element): walk conductor %.1e " +
                                                      "(%d pairs), walk dielectric %.1e (%d pairs)", worstWalk, compared, worstGlass, comparedGlass));
            EXPECT_GT(compared, 1000);
            EXPECT_GT(comparedGlass, 1000);
            EXPECT_LT(worstWalk, 1e-3);
            EXPECT_LT(worstGlass, 1e-3);
        }

        [TEST("PolarisedMicrofacet", "MirrorKeepsWeightAndMatricesArePhysical")]
        public static void PolarisedMicrofacet_MirrorKeepsWeightAndMatricesArePhysical()
        {
            RNG rng = new RNG(3);
            BxDF[] mirrors = new BxDF[]
            {
                new MultiScatterConductorBxDF(new TrowbridgeReitzDistribution(0.6f, 0.6f), new MicrosurfaceHeightUniform(), S(1), S(1e4f), 50)
            };
            BxDF[] real = new BxDF[]
            {
                new MultiScatterConductorBxDF(new TrowbridgeReitzDistribution(0.6f, 0.6f), new MicrosurfaceHeightUniform(), S(0.2f), S(3.9f), 50),
                new MultiScatterConductorBxDF(new BeckmannDistribution(0.6f, 0.6f), new MicrosurfaceHeightUniform(), S(0.2f), S(3.9f), 50),
                new MultiScatterConductorBxDF(new StudentTDistribution(0.6f, 0.6f, 3f), new MicrosurfaceHeightUniform(), S(0.2f), S(3.9f), 50),
                new MultiScatterDielectricBxDF(new TrowbridgeReitzDistribution(0.6f, 0.6f), new MicrosurfaceHeightUniform(), 1.5f, 50)
            };
            double worstWeight = 0, worstUnphysical = 0;
            int answered = 0, asked = 0;
            for (int i = 0; i < 1500; ++i)
            {
                Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                TransportMode mode = (i & 1) == 0 ? TransportMode.Radiance : TransportMode.Importance;
                foreach (BxDF bxdf in mirrors)
                {
                    MuellerMatrix m;
                    if (Matrix(bxdf, wo, wi, mode, out m))
                    {
                        worstWeight = Math.Max(worstWeight, Math.Abs(m.Get(0, 0)[0] - 1));
                        worstUnphysical = Math.Max(worstUnphysical, Unphysical(m));
                    }
                }
                foreach (BxDF bxdf in real)
                {
                    Vector3f gi = (bxdf is MultiScatterDielectricBxDF && (i & 2) != 0) ? -wi : wi;
                    MuellerMatrix m;
                    ++asked;
                    if (Matrix(bxdf, wo, gi, mode, out m))
                    {
                        ++answered;
                        double value = Unphysical(m);
                        worstUnphysical = Math.Max(worstUnphysical, value);
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised: mirror microsurfaces keep the weight within %.1e; degree of polarisation above 1 by at most %.1e " +
                                                      "(%d of %d pairs have a matrix)", worstWeight, worstUnphysical, answered, asked));
            EXPECT_GT(answered, asked * 8 / 10);
            EXPECT_LT(worstWeight, 2e-3);
            EXPECT_LT(worstUnphysical, 2e-3);
        }

        // The walks are estimates. With the weight of a pair set to the scalar estimate of the same walk (f cos), the matrix is
        // the estimate of the Mueller BSDF times the cosine, with no ratio in it: its mean for a path from the camera, over the
        // cosine of wi, is its mean for the same light followed from its source, over the cosine of wo. A pair that stands
        // out is estimated again, 16 times longer (the estimates of the dielectric have a heavy tail).
        static double WalkDifference(BxDF walk, Vector3f wo, Vector3f wi, int estimates, ref RNG rng)
        {
            double[] camera = new double[16], light = new double[16], square = new double[16];
            int used = 0;
            for (int e = 0; e < estimates; ++e)
            {
                Vector3f d = 1e-4f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                Vector3f a = Normalize(wo + d), b = Normalize(wi - d);
                BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), walk);
                SampledSpectrum w1 = walk.f(a, b, TransportMode.Radiance) * AbsCosTheta(b), w2 = walk.f(b, a, TransportMode.Importance) * AbsCosTheta(a);
                Attenuation A1 = default, A2 = default;
                bool ok1 = w1[0] > 0 && PolarisedPathIntegrator.RoughAttenuation(bsdf, default, a, b, w1, TransportMode.Radiance, out A1);
                bool ok2 = w2[0] > 0 && PolarisedPathIntegrator.RoughAttenuation(bsdf, default, b, a, w2, TransportMode.Importance, out A2);
                ++used;
                MuellerMatrix m1 = ok1 ? A1.GetMuellerMatrix() : default, m2 = ok2 ? A2.GetMuellerMatrix() : default;
                for (int c = 0; c < 16; ++c)
                {
                    double x = ok1 ? m1.Get(c / 4, c % 4)[0] / AbsCosTheta(b) : 0, y = ok2 ? m2.Get(c / 4, c % 4)[0] / AbsCosTheta(a) : 0;
                    camera[c] += x;
                    light[c] += y;
                    square[c] += (x - y) * (x - y);
                }
            }
            double worst = 0;
            for (int c = 0; c < 16; ++c)
            {
                double difference = (camera[c] - light[c]) / used;
                double error = Math.Sqrt(square[c]) / used + 1e-5;
                worst = Math.Max(worst, Math.Abs(difference) / error);
            }
            return worst;
        }

        [TEST("PolarisedMicrofacet", "WalkCameraPathIsLightPath")]
        public static void PolarisedMicrofacet_WalkCameraPathIsLightPath()
        {
            RNG rng = new RNG(5);
            BxDF[] walks = new BxDF[]
            {
                new MultiScatterConductorBxDF(new TrowbridgeReitzDistribution(0.8f, 0.8f), new MicrosurfaceHeightUniform(), S(0.2f), S(3.9f), 50),
                new MultiScatterDielectricBxDF(new TrowbridgeReitzDistribution(0.8f, 0.8f), new MicrosurfaceHeightUniform(), 1.5f, 50)
            };
            foreach (BxDF walk in walks)
            {
                double worst = 0;
                int again = 0;
                for (int pair = 0; pair < 8; ++pair)
                {
                    Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                    // The dielectric: every other pair is a transmission
                    if (walk is MultiScatterDielectricBxDF && (pair & 1) != 0)
                    {
                        wi = -wi;
                    }
                    double z = WalkDifference(walk, wo, wi, 20000, ref rng);
                    if (z > 3)
                    {
                        ++again;
                        z = WalkDifference(walk, wo, wi, 320000, ref rng);
                    }
                    worst = Math.Max(worst, z);
                }
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised walk %s: camera path against light path, worst element %.1f sigma over 8 pairs of 20000 estimates " +
                                                          "(%d pairs estimated again)", walk is MultiScatterConductorBxDF ? "conductor" : "dielectric", worst, again));
                EXPECT_LT(worst, 4.5);
            }
        }

        // The thin dielectric slab: the closed form of its matrix against the orders of reflection inside it, added one by one
        // from the matrices of its two interfaces
        [TEST("PolarisedMicrofacet", "ThinDielectricIsSumOfOrders")]
        public static void PolarisedMicrofacet_ThinDielectricIsSumOfOrders()
        {
            Float eta = 1.5f;
            BxDF thin = new ThinDielectricBxDF(eta);
            BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), thin);
            SurfaceInteraction isect = new SurfaceInteraction();
            isect.shading.n = new Normal3f(0, 0, 1);
            double worst = 0, worstEnergy = 0, worstUnphysical = 0, depolarised = 0;
            foreach (Float cosine in new Float[] { 0.98f, 0.8f, 0.55f, 0.3f, 0.1f })
            {
                Float sine = SafeSqrt(1 - cosine * cosine);
                Vector3f wo = new Vector3f(sine, 0, cosine);
                // The interfaces: from outside, and from inside at the refracted angle
                Float cosInside = SafeSqrt(1 - sine * sine / (eta * eta));
                pbrt.FrDielectricPolarised(cosine, eta, out Float Fs, out Float Fp, out Float Ds, out Float Dp);
                pbrt.FrDielectricPolarised(cosInside, 1 / eta, out Float Gs, out Float Gp, out Float Es, out Float Ep);
                MuellerMatrix outer = Attenuation.Fresnel(S(1), Fs, Fp, Ds, Dp).GetMuellerMatrix();
                MuellerMatrix inner = Attenuation.Fresnel(S(1), Gs, Gp, Es, Ep).GetMuellerMatrix();
                MuellerMatrix enter = Attenuation.Fresnel(S(1), 1 - Fs, 1 - Fp, 0, 0).GetMuellerMatrix();
                MuellerMatrix leave = Attenuation.Fresnel(S(1), 1 - Gs, 1 - Gp, 0, 0).GetMuellerMatrix();
                MuellerMatrix reflected = outer, transmitted = leave * enter, inside = enter;
                for (int order = 1; order < 200; ++order)
                {
                    inside = inner * inside;
                    if ((order & 1) != 0)
                    {
                        reflected = reflected + leave * inside;
                    }
                    else
                    {
                        transmitted = transmitted + leave * inside;
                    }
                }
                // The scalar reflectance of pbrt, by which the matrix of the BxDF is divided
                Float R = 0.5f * (Fs + Fp), T = 1 - R;
                R += Sqr(T) * R / (1 - Sqr(R));
                T = 1 - R;
                for (int transmission = 0; transmission < 2; ++transmission)
                {
                    Vector3f wi = transmission == 0 ? new Vector3f(-wo.x, -wo.y, wo.z) : -wo;
                    BSDFSample bs = new BSDFSample(S(1), wi, 1, transmission == 0 ? BxDFFlags.SpecularReflection : BxDFFlags.SpecularTransmission);
                    Attenuation A;
                    EXPECT_TRUE(PolarisedPathIntegrator.SpecularAttenuation(bsdf, isect, wo, bs, TransportMode.Radiance, out A));
                    MuellerMatrix m = A.GetMuellerMatrix(), expected = transmission == 0 ? reflected : transmitted;
                    Float scalar = transmission == 0 ? R : T;
                    for (int row = 0; row < 4; ++row)
                    {
                        for (int column = 0; column < 4; ++column)
                        {
                            worst = Math.Max(worst, Math.Abs(m.Get(row, column)[0] * scalar - expected.Get(row, column)[0]));
                        }
                    }
                    worstUnphysical = Math.Max(worstUnphysical, Unphysical(m));
                    // How far the slab is from an interface that keeps the polarisation
                    double pure = Math.Sqrt(Math.Max(0.0, Sqr(m.Get(0, 0)[0]) - Sqr(m.Get(0, 1)[0])));
                    depolarised = Math.Max(depolarised, (pure - Math.Abs(m.Get(2, 2)[0])) / m.Get(0, 0)[0]);
                }
                worstEnergy = Math.Max(worstEnergy, Math.Abs(reflected.Get(0, 0)[0] + transmitted.Get(0, 0)[0] - 1));
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised thin dielectric: closed form against 200 orders added one by one, worst element %.1e; the orders " +
                                                      "reflect and transmit 1 within %.1e; degree of polarisation above 1 by at most %.1e; depolarisation up to %.3f",
                                                      worst, worstEnergy, worstUnphysical, depolarised));
            EXPECT_LT(worst, 1e-5);
            EXPECT_LT(worstEnergy, 1e-5);
            EXPECT_LT(worstUnphysical, 1e-5);
        }

        // The rough thin film (ThinFilmBxDF.Polarised). With no thickness its matrix is that of the bare rough surface; with
        // one, the matrix for a path from the camera is that of the same light followed from its source (a transmission
        // meets the layers in the other order from the other side, with the same phases), and it is physical.
        [TEST("PolarisedMicrofacet", "RoughThinFilm")]
        public static void PolarisedMicrofacet_RoughThinFilm()
        {
            RNG rng = new RNG(6);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            MicrofacetDistribution distribution = new TrowbridgeReitzDistribution(0.4f, 0.4f);
            SampledSpectrum eta = S(0.2f), k = S(3.9f);
            BxDF glass = new DielectricBxDF(1.5f, distribution), metal = new ConductorBxDF(distribution, eta, k);
            BxDF glassNone = new ThinFilmBxDF(lambda, new Float[] { 2.1f }, new Float[] { 0 }, 1.5f, S(1), S(0), false, distribution);
            BxDF metalNone = new ThinFilmBxDF(lambda, new Float[] { 2.1f }, new Float[] { 0 }, 1.5f, eta, k, true, distribution);
            BxDF glassFilm = new ThinFilmBxDF(lambda, new Float[] { 1.38f, 2.1f }, new Float[] { 95, 300 }, 1.5f, S(1), S(0), false, distribution);
            BxDF metalFilm = new ThinFilmBxDF(lambda, new Float[] { 1.5f }, new Float[] { 400 }, 1.5f, eta, k, true, distribution);
            double worstBare = 0, worstPath = 0, worstUnphysical = 0, effect = 0;
            int comparedBare = 0, comparedPath = 0;
            for (int i = 0; i < 3000; ++i)
            {
                Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                TransportMode mode = (i & 1) == 0 ? TransportMode.Radiance : TransportMode.Importance;
                MuellerMatrix a, b;
                if (Matrix(metal, wo, wi, mode, out a) && Matrix(metalNone, wo, wi, mode, out b))
                {
                    worstBare = Math.Max(worstBare, Difference(a, b));
                    ++comparedBare;
                }
                // The dielectric: reflection on either side, and transmission both ways
                Vector3f go = (i & 2) != 0 ? -wo : wo, gi = (i & 4) != 0 ? -wi : wi;
                if (Matrix(glass, go, gi, mode, out a) && Matrix(glassNone, go, gi, mode, out b))
                {
                    worstBare = Math.Max(worstBare, Difference(a, b));
                    ++comparedBare;
                }
                foreach (BxDF film in new BxDF[] { glassFilm, metalFilm })
                {
                    Vector3f fo = film == glassFilm ? go : wo, fi = film == glassFilm ? gi : wi;
                    MuellerMatrix camera, light;
                    if (Matrix(film, fo, fi, TransportMode.Radiance, out camera) && Matrix(film, fi, fo, TransportMode.Importance, out light))
                    {
                        worstPath = Math.Max(worstPath, Difference(camera, light));
                        worstUnphysical = Math.Max(worstUnphysical, Unphysical(camera));
                        ++comparedPath;
                        if (film == glassFilm && Matrix(glass, fo, fi, TransportMode.Radiance, out a))
                        {
                            effect = Math.Max(effect, Difference(camera, a));
                        }
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised rough thin film: no thickness against the bare surface %.1e (%d pairs); camera path " +
                                                      "against light path %.1e (%d pairs), degree of polarisation above 1 by at most %.1e; the layers change " +
                                                      "an element by up to %.2f", worstBare, comparedBare, worstPath, comparedPath, worstUnphysical, effect));
            EXPECT_GT(comparedBare, 3000);
            EXPECT_GT(comparedPath, 3000);
            EXPECT_LT(worstBare, 1e-3);
            EXPECT_LT(worstPath, 1e-3);
            EXPECT_LT(worstUnphysical, 2e-3);
            EXPECT_GT(effect, 0.05);
        }

        // The layered BxDFs (LayeredBxDF.Polarised), against cases with a known answer:
        //   - under a coat of index 1 there is no coat: the matrix is that of the conductor below, rough or smooth;
        //   - over a black base only the coat reflects: the matrix is that of the rough dielectric;
        //   - over a white diffuse base the light that went through the coat comes back unpolarised: the first element stays
        //     the weight, and on average the surface polarises much less than the coat alone. (Not for every pair: the
        //     light of the base is polarised again as it leaves through the coat, at grazing angles more than the coat
        //     polarises what it reflects near its normal.)
        [TEST("PolarisedMicrofacet", "Layered")]
        public static void PolarisedMicrofacet_Layered()
        {
            RNG rng = new RNG(8);
            SampledSpectrum eta = S(0.2f), k = S(3.9f);
            MicrofacetDistribution rough = new TrowbridgeReitzDistribution(0.3f, 0.3f), smooth = new TrowbridgeReitzDistribution(0, 0);
            BxDF metal = new ConductorBxDF(rough, eta, k);
            BxDF noCoat = new CoatedConductorBxDF(new DielectricBxDF(1f, smooth), new ConductorBxDF(rough, eta, k), 0.01f, S(0), 0, 10, 1);
            BxDF coat = new DielectricBxDF(1.5f, rough);
            BxDF blackBase = new CoatedDiffuseBxDF(new DielectricBxDF(1.5f, rough), new DiffuseBxDF(S(0)), 0.01f, S(0), 0, 10, 1);
            BxDF whiteBase = new CoatedDiffuseBxDF(new DielectricBxDF(1.5f, rough), new DiffuseBxDF(S(0.9f)), 0.01f, S(0), 0, 10, 4);
            BxDF coatedMetal = new CoatedConductorBxDF(new DielectricBxDF(1.5f, rough), new ConductorBxDF(rough, eta, k), 0.01f, S(0), 0, 10, 4);
            BxDF cloudy = new CoatedConductorBxDF(new DielectricBxDF(1.5f, rough), new ConductorBxDF(rough, eta, k), 0.3f, S(0.8f), 0.2f, 10, 4);
            double worstNoCoat = 0, worstBlack = 0, worstWeight = 0, worstUnphysical = 0, diluted = 0, alone = 0;
            int comparedNoCoat = 0, comparedBlack = 0, comparedWhite = 0, answered = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                // The layers are two sided: from below they are what they are from above (the bare dielectric is not: it is
                // asked from above)
                Vector3f lo = (i & 2) != 0 ? -wo : wo, li = (i & 2) != 0 ? -wi : wi;
                TransportMode mode = (i & 1) == 0 ? TransportMode.Radiance : TransportMode.Importance;
                MuellerMatrix a, b;
                if (Matrix(metal, wo, wi, mode, out a) && Matrix(noCoat, lo, li, mode, out b))
                {
                    worstNoCoat = Math.Max(worstNoCoat, Difference(a, b));
                    ++comparedNoCoat;
                }
                if (Matrix(coat, wo, wi, mode, out a))
                {
                    if (Matrix(blackBase, lo, li, mode, out b))
                    {
                        worstBlack = Math.Max(worstBlack, Difference(a, b));
                        ++comparedBlack;
                    }
                    if (Matrix(whiteBase, lo, li, mode, out b))
                    {
                        worstWeight = Math.Max(worstWeight, Math.Abs(b.m00[0] - 1));
                        worstUnphysical = Math.Max(worstUnphysical, Unphysical(b));
                        // The polarisation that the surface gives to unpolarised light
                        double white = Math.Sqrt(Sqr((double)b.m10[0]) + Sqr((double)b.m20[0]) + Sqr((double)b.m30[0]));
                        double bare = Math.Sqrt(Sqr((double)a.m10[0]) + Sqr((double)a.m20[0]) + Sqr((double)a.m30[0]));
                        diluted += white;
                        alone += bare;
                        ++comparedWhite;
                    }
                }
                foreach (BxDF bxdf in new BxDF[] { coatedMetal, cloudy })
                {
                    if (Matrix(bxdf, lo, li, mode, out b))
                    {
                        worstUnphysical = Math.Max(worstUnphysical, Unphysical(b));
                        ++answered;
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised layered: a coat of index 1 against the bare conductor %.1e (%d pairs); a black base " +
                                                      "against the coat alone %.1e (%d pairs); a white diffuse base keeps the weight within %.1e, and polarises " +
                                                      "unpolarised light %.3f on average where the coat alone does %.3f (%d pairs); degree of polarisation above 1 " +
                                                      "by at most %.1e (%d matrices of coated conductors)", worstNoCoat, comparedNoCoat, worstBlack, comparedBlack,
                                                      worstWeight, diluted / comparedWhite, alone / comparedWhite, comparedWhite, worstUnphysical, answered));
            EXPECT_GT(comparedNoCoat, 1500);
            EXPECT_GT(comparedBlack, 1500);
            EXPECT_GT(comparedWhite, 1500);
            EXPECT_GT(answered, 3000);
            EXPECT_LT(worstNoCoat, 1e-3);
            EXPECT_LT(worstBlack, 1e-3);
            EXPECT_LT(worstWeight, 1e-3);
            EXPECT_LT(worstUnphysical, 2e-3);
            EXPECT_LT(diluted, 0.8 * alone);
        }

        // The matrix of a sampled path (the Sample_f that returns one), in the frames of the matrix of the pair of directions
        static bool SampledMatrix(BxDF bxdf, Vector3f wo, ref RNG rng, TransportMode mode, out Vector3f wi, out MuellerMatrix matrix)
        {
            BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), bxdf);
            bool polarised;
            Attenuation A;
            pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), mode,
                                                         BxDFReflTransFlags.All, out polarised, out A);
            wi = default;
            matrix = default;
            if (!bs || !polarised)
            {
                return false;
            }
            wi = bs.value().wi;
            matrix = A.GetMuellerMatrix();
            return true;
        }

        // The matrix that comes with a sampled direction is that of the path that was sampled:
        //   - a walk allowed one reflection is one microfacet: the matrix of the single-scattering BxDF for that direction;
        //   - under a smooth coat of index 1 a smooth conductor is itself: its Fresnel matrix at the mirror direction;
        //   - over a black base a sample is a reflection of the coat: the matrix of the dielectric;
        //   - with every reflection allowed, and through real coats, the matrices are physical.
        [TEST("PolarisedMicrofacet", "SampledPath")]
        public static void PolarisedMicrofacet_SampledPath()
        {
            RNG rng = new RNG(12);
            SampledSpectrum eta = S(0.2f), k = S(3.9f);
            MicrofacetDistribution rough = new TrowbridgeReitzDistribution(0.4f, 0.4f), smooth = new TrowbridgeReitzDistribution(0, 0);
            BxDF metal = new ConductorBxDF(rough, eta, k), glass = new DielectricBxDF(1.5f, rough);
            // The mirror direction through a conductor that is barely rough: its half vector is the normal
            BxDF mirror = new ConductorBxDF(new TrowbridgeReitzDistribution(0.01f, 0.01f), eta, k);
            BxDF metalOne = new MultiScatterConductorBxDF(rough, new MicrosurfaceHeightUniform(), eta, k, 1);
            BxDF glassOne = new MultiScatterDielectricBxDF(rough, new MicrosurfaceHeightUniform(), 1.5f, 1);
            BxDF noCoat = new CoatedConductorBxDF(new DielectricBxDF(1f, smooth), new ConductorBxDF(smooth, eta, k), 0.01f, S(0), 0, 10, 1);
            BxDF blackBase = new CoatedDiffuseBxDF(new DielectricBxDF(1.5f, rough), new DiffuseBxDF(S(0)), 0.01f, S(0), 0, 10, 1);
            BxDF[] others = new BxDF[]
            {
                new MultiScatterConductorBxDF(rough, new MicrosurfaceHeightUniform(), eta, k, 50),
                new MultiScatterDielectricBxDF(rough, new MicrosurfaceHeightUniform(), 1.5f, 50),
                new CoatedConductorBxDF(new DielectricBxDF(1.5f, smooth), new ConductorBxDF(smooth, eta, k), 0.01f, S(0), 0, 10, 1),
                new CoatedConductorBxDF(new DielectricBxDF(1.5f, rough), new ConductorBxDF(rough, eta, k), 0.3f, S(0.8f), 0.2f, 10, 1),
                new CoatedDiffuseBxDF(new DielectricBxDF(1.5f, rough), new DiffuseBxDF(S(0.9f)), 0.01f, S(0), 0, 10, 1)
            };
            double worstMetal = 0, worstGlass = 0, worstNoCoat = 0, worstBlack = 0, worstUnphysical = 0;
            int comparedMetal = 0, comparedGlass = 0, comparedNoCoat = 0, comparedBlack = 0, sampled = 0;
            for (int i = 0; i < 3000; ++i)
            {
                Vector3f wo = Upper(ref rng), wi;
                TransportMode mode = (i & 1) == 0 ? TransportMode.Radiance : TransportMode.Importance;
                MuellerMatrix a, b;
                if (SampledMatrix(metalOne, wo, ref rng, mode, out wi, out a) && Matrix(metal, wo, wi, mode, out b))
                {
                    worstMetal = Math.Max(worstMetal, Difference(a, b));
                    ++comparedMetal;
                }
                Vector3f go = (i & 2) != 0 ? -wo : wo;
                if (SampledMatrix(glassOne, go, ref rng, mode, out wi, out a) && Matrix(glass, go, wi, mode, out b))
                {
                    worstGlass = Math.Max(worstGlass, Difference(a, b));
                    ++comparedGlass;
                }
                if (SampledMatrix(noCoat, wo, ref rng, mode, out wi, out a) && Matrix(mirror, wo, wi, mode, out b))
                {
                    worstNoCoat = Math.Max(worstNoCoat, Difference(a, b));
                    ++comparedNoCoat;
                }
                if (SampledMatrix(blackBase, wo, ref rng, mode, out wi, out a) && wi.z > 0 && Matrix(glass, wo, wi, mode, out b))
                {
                    worstBlack = Math.Max(worstBlack, Difference(a, b));
                    ++comparedBlack;
                }
                foreach (BxDF bxdf in others)
                {
                    if (SampledMatrix(bxdf, bxdf is MultiScatterDielectricBxDF ? go : wo, ref rng, mode, out wi, out a))
                    {
                        worstUnphysical = Math.Max(worstUnphysical, Unphysical(a));
                        ++sampled;
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised sampled paths: one reflection against single scattering, conductor %.1e (%d), " +
                                                      "dielectric %.1e (%d); a smooth conductor under a coat of index 1 %.1e (%d); the coat over a black " +
                                                      "base %.1e (%d); degree of polarisation above 1 by at most %.1e (%d other samples)", worstMetal,
                                                      comparedMetal, worstGlass, comparedGlass, worstNoCoat, comparedNoCoat, worstBlack, comparedBlack,
                                                      worstUnphysical, sampled));
            EXPECT_GT(comparedMetal, 1500);
            EXPECT_GT(comparedGlass, 1500);
            EXPECT_GT(comparedNoCoat, 2500);
            EXPECT_GT(comparedBlack, 100);
            EXPECT_GT(sampled, 8000);
            EXPECT_LT(worstMetal, 1e-3);
            EXPECT_LT(worstGlass, 1e-3);
            EXPECT_LT(worstNoCoat, 1e-3);
            EXPECT_LT(worstBlack, 1e-3);
            EXPECT_LT(worstUnphysical, 2e-3);
        }

        // The two ways to the same light. Unpolarised light arrives evenly from every direction; the light that leaves along
        // wo is found by sampling directions with their path matrices (Sample_f), and by evaluating the matrix of directions
        // taken evenly (f). Both estimate one Stokes vector: its degree of polarisation agrees within their noise. (The
        // matrix of a sampled path has no ratio of two estimates in it; a matrix asked for the pair of directions of a
        // sample, scaled by the weight of the sample, does, and gave a polarisation some percent off.)
        static void SampledAgainstEvaluated(BxDF bxdf, string name, int samples, bool sameIntensity)
        {
            RNG rng = new RNG(13);
            BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), bxdf);
            Vector3f wo = Normalize(new Vector3f(0.75f, 0.1f, 0.5f)), up = new Vector3f(0, 0, 1);
            double[] sumSampled = new double[4], sumEvaluated = new double[4];
            double squareSampled = 0, squareEvaluated = 0;
            for (int i = 0; i < samples; ++i)
            {
                bool polarised;
                Attenuation A;
                pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                             TransportMode.Radiance, BxDFReflTransFlags.All, out polarised, out A);
                if ((bool)bs && polarised)
                {
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
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised %s: light from an even unpolarised surround, sampled against evaluated: intensity %.4f " +
                                                      "/ %.4f, s1 %+.5f / %+.5f (%.1f sigma, error %.5f), s2 %+.5f / %+.5f, s3 %+.5f / %+.5f", name,
                                                      sampledIntensity, evaluatedIntensity, sampledS1, evaluatedS1, sigmas, error, sumSampled[2] / samples,
                                                      sumEvaluated[2] / samples, sumSampled[3] / samples, sumEvaluated[3] / samples));
            EXPECT_LT(sigmas, 4.0, name);
            if (sameIntensity)
            {
                EXPECT_LT(Math.Abs(sampledIntensity - evaluatedIntensity), 0.02 * evaluatedIntensity, name);
            }
        }

        [TEST("PolarisedMicrofacet", "SampledAgainstEvaluated")]
        public static void PolarisedMicrofacet_SampledAgainstEvaluated()
        {
            SampledSpectrum eta = S(0.2f), k = S(3.9f);
            MicrofacetDistribution rough = new TrowbridgeReitzDistribution(0.5f, 0.5f), coat = new TrowbridgeReitzDistribution(0.1f, 0.1f);
            SampledAgainstEvaluated(new MultiScatterConductorBxDF(rough, new MicrosurfaceHeightUniform(), eta, k, 50), "walk conductor GGX 0.5", 400000, true);
            SampledAgainstEvaluated(new MultiScatterDielectricBxDF(rough, new MicrosurfaceHeightUniform(), 1.5f, 50), "walk dielectric GGX 0.5", 400000, true);
            // The layered BxDFs: the scalar Sample_f and f of the layers already disagree by some 4% about the light
            // they return (BxDFCheck measures the same on the scalar BxDF, at any depth, before any of this), so the
            // intensities are printed and only the polarised part is held to its noise
            SampledAgainstEvaluated(new CoatedDiffuseBxDF(new DielectricBxDF(1.5f, coat), new DiffuseBxDF(S(0.7f)), 0.01f, S(0), 0, 10, 1),
                                    "coated diffuse", 400000, false);
            SampledAgainstEvaluated(new CoatedConductorBxDF(new DielectricBxDF(1.5f, coat), new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), eta, k),
                                                            0.01f, S(0), 0, 10, 1), "coated conductor", 400000, false);
        }

        // Thin films on the microfacets of the multiple scattering microsurfaces: with one reflection allowed the matrix is
        // that of the rough thin film BxDF for the same directions (conductor; dielectric on both sides and through), and
        // with all of them it is physical.
        [TEST("PolarisedMicrofacet", "MultiScatterThinFilm")]
        public static void PolarisedMicrofacet_MultiScatterThinFilm()
        {
            RNG rng = new RNG(10);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            SampledSpectrum eta = S(0.2f), k = S(3.9f);
            MicrofacetDistribution distribution = new TrowbridgeReitzDistribution(0.4f, 0.4f);
            Float[] metalEta = new Float[] { 1.5f }, metalThickness = new Float[] { 400 };
            Float[] glassEta = new Float[] { 1.38f, 2.1f }, glassThickness = new Float[] { 95, 300 };
            ThinFilmStack metalFilm = new ThinFilmStack(lambda, metalEta, metalThickness), glassFilm = new ThinFilmStack(lambda, glassEta, glassThickness);
            BxDF metal = new ThinFilmBxDF(lambda, metalEta, metalThickness, 1.5f, eta, k, true, distribution);
            BxDF glass = new ThinFilmBxDF(lambda, glassEta, glassThickness, 1.5f, S(1), S(0), false, distribution);
            BxDF metalOne = new MultiScatterConductorBxDF(distribution, new MicrosurfaceHeightUniform(), eta, k, 1, metalFilm);
            BxDF glassOne = new MultiScatterDielectricBxDF(distribution, new MicrosurfaceHeightUniform(), 1.5f, 1, glassFilm);
            BxDF metalAll = new MultiScatterConductorBxDF(distribution, new MicrosurfaceHeightUniform(), eta, k, 50, metalFilm);
            BxDF glassAll = new MultiScatterDielectricBxDF(distribution, new MicrosurfaceHeightUniform(), 1.5f, 50, glassFilm);
            double worstMetal = 0, worstGlass = 0, worstUnphysical = 0;
            int comparedMetal = 0, comparedGlass = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                TransportMode mode = (i & 1) == 0 ? TransportMode.Radiance : TransportMode.Importance;
                MuellerMatrix a, b;
                if (Matrix(metal, wo, wi, mode, out a) && Matrix(metalOne, wo, wi, mode, out b))
                {
                    worstMetal = Math.Max(worstMetal, Difference(a, b));
                    ++comparedMetal;
                }
                Vector3f go = (i & 2) != 0 ? -wo : wo, gi = (i & 4) != 0 ? -wi : wi;
                if (Matrix(glass, go, gi, mode, out a) && Matrix(glassOne, go, gi, mode, out b))
                {
                    worstGlass = Math.Max(worstGlass, Difference(a, b));
                    ++comparedGlass;
                }
                if (Matrix(metalAll, wo, wi, mode, out a))
                {
                    worstUnphysical = Math.Max(worstUnphysical, Unphysical(a));
                }
                if (Matrix(glassAll, go, gi, mode, out a))
                {
                    worstUnphysical = Math.Max(worstUnphysical, Unphysical(a));
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised walks with thin films: one reflection against the rough thin film BxDF, conductor %.1e " +
                                                      "(%d pairs), dielectric %.1e (%d pairs); all reflections: degree of polarisation above 1 by at most %.1e",
                                                      worstMetal, comparedMetal, worstGlass, comparedGlass, worstUnphysical));
            EXPECT_GT(comparedMetal, 1000);
            EXPECT_GT(comparedGlass, 1000);
            EXPECT_LT(worstMetal, 1e-3);
            EXPECT_LT(worstGlass, 1e-3);
            EXPECT_LT(worstUnphysical, 2e-3);
        }
    }
}
