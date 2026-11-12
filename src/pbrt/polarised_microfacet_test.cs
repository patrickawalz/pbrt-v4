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
            BxDF groove = new VCavityConductorBxDF(new TrowbridgeReitzDistribution(0.4f, 0.4f), eta, k, 1);
            BxDF glass = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.4f, 0.4f));
            BxDF glassWalk = new MultiScatterDielectricBxDF(new TrowbridgeReitzDistribution(0.4f, 0.4f), new MicrosurfaceHeightUniform(), 1.5f, 1);
            double worstWalk = 0, worstGroove = 0, worstGlass = 0;
            int compared = 0, comparedGlass = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                foreach (TransportMode mode in new TransportMode[] { TransportMode.Radiance, TransportMode.Importance })
                {
                    MuellerMatrix reference, a, b;
                    if (Matrix(single, wo, wi, mode, out reference))
                    {
                        if (Matrix(walk, wo, wi, mode, out a))
                        {
                            worstWalk = Math.Max(worstWalk, Difference(reference, a));
                            ++compared;
                        }
                        if (Matrix(groove, wo, wi, mode, out b))
                        {
                            worstGroove = Math.Max(worstGroove, Difference(reference, b));
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
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised one reflection against the single-scattering BxDFs of pbrt (worst element): walk conductor %.1e, " +
                                                      "V-cavity conductor %.1e (%d pairs), walk dielectric %.1e (%d pairs)", worstWalk, worstGroove, compared, worstGlass,
                                                      comparedGlass));
            EXPECT_GT(compared, 1000);
            EXPECT_GT(comparedGlass, 1000);
            EXPECT_LT(worstWalk, 1e-3);
            EXPECT_LT(worstGroove, 1e-3);
            EXPECT_LT(worstGlass, 1e-3);
        }

        [TEST("PolarisedMicrofacet", "MirrorKeepsWeightAndMatricesArePhysical")]
        public static void PolarisedMicrofacet_MirrorKeepsWeightAndMatricesArePhysical()
        {
            RNG rng = new RNG(3);
            BxDF[] mirrors = new BxDF[]
            {
                new MultiScatterConductorBxDF(new TrowbridgeReitzDistribution(0.6f, 0.6f), new MicrosurfaceHeightUniform(), S(1), S(1e4f), 50),
                new VCavityConductorBxDF(new TrowbridgeReitzDistribution(0.6f, 0.6f), S(1), S(1e4f), 16)
            };
            BxDF[] real = new BxDF[]
            {
                new MultiScatterConductorBxDF(new TrowbridgeReitzDistribution(0.6f, 0.6f), new MicrosurfaceHeightUniform(), S(0.2f), S(3.9f), 50),
                new MultiScatterConductorBxDF(new BeckmannDistribution(0.6f, 0.6f), new MicrosurfaceHeightUniform(), S(0.2f), S(3.9f), 50),
                new MultiScatterConductorBxDF(new StudentTDistribution(0.6f, 0.6f, 3f), new MicrosurfaceHeightUniform(), S(0.2f), S(3.9f), 50),
                new VCavityConductorBxDF(new TrowbridgeReitzDistribution(0.6f, 0.6f), S(0.2f), S(3.9f), 16),
                new VCavityConductorBxDF(new BeckmannDistribution(0.6f, 0.6f), S(0.2f), S(3.9f), 16),
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

        // The V-cavity matrix is a value: the two modes agree element by element, with paths of several reflections
        [TEST("PolarisedMicrofacet", "CameraPathIsLightPath")]
        public static void PolarisedMicrofacet_CameraPathIsLightPath()
        {
            RNG rng = new RNG(4);
            BxDF groove = new VCavityConductorBxDF(new TrowbridgeReitzDistribution(0.8f, 0.8f), S(0.2f), S(3.9f), 16);
            BxDF grooveOne = new VCavityConductorBxDF(new TrowbridgeReitzDistribution(0.8f, 0.8f), S(0.2f), S(3.9f), 1);
            double worst = 0, multiple = 0;
            int compared = 0;
            for (int i = 0; i < 3000; ++i)
            {
                Vector3f wo = Upper(ref rng), wi = Upper(ref rng);
                MuellerMatrix camera, light, one;
                if (Matrix(groove, wo, wi, TransportMode.Radiance, out camera) && Matrix(groove, wi, wo, TransportMode.Importance, out light))
                {
                    worst = Math.Max(worst, Difference(camera, light));
                    ++compared;
                    // How much of the matrix comes from paths of more than one reflection
                    if (Matrix(grooveOne, wo, wi, TransportMode.Radiance, out one))
                    {
                        multiple = Math.Max(multiple, Difference(camera, one));
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE polarised V-cavity: camera path against light path, worst element %.1e over %d pairs (the paths of several " +
                                                      "reflections change an element by up to %.2f)", worst, compared, multiple));
            EXPECT_GT(compared, 2000);
            EXPECT_LT(worst, 1e-3);
            EXPECT_GT(multiple, 0.05);
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
    }
}
