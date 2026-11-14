// (not in pbrt-v4) microfacetnormalmap_test.cs
//
// MicrofacetNormalMapBxDF (microfacetnormalmap.cs), on a diffuse and on a rough conductor BxDF:
//   - nothing arrives from below the unperturbed normal g and nothing leaves below it;
//   - the samples and the evaluation agree: the mean weight of the samples is the integral of f |cos| over the sphere;
//   - energy: with a white diffuse BxDF and a mirror tangent facet, the random walk reflects everything (to the order
//     of the walk), and the analytic second order model no more than that;
//   - reciprocity with respect to g: f(wo, wi) |cos_p wi| / cos_g wi is symmetric (the analytic model);
//   - the random walk stopped at the second order is the analytic model;
//   - no perturbation: the model is not valid and the BxDF of the material is used as it is.

using System;
using testing;

namespace pbrt
{
    public static unsafe class MicrofacetNormalMapTests
    {
        static NormalMapParams Model(NormalMapModel model, Float maxOrder, Float saturation)
        {
            NormalMapParams p = new NormalMapParams();
            p.model = model;
            p.maxOrder = maxOrder;
            p.saturation = saturation;
            return p;
        }

        // The integral of f |cos| over the sphere (the evaluation of a walk is one estimate per pair of directions: the
        // directions are random, so the mean is the integral) and the mean weight of the samples, per wo
        static void Albedo(MicrofacetNormalMapBxDF bxdf, Vector3f wo, Vector3f g, ref RNG rng, int count, out double evaluated, out double sampled,
                           out int below)
        {
            evaluated = 0;
            sampled = 0;
            below = 0;
            for (int i = 0; i < count; ++i)
            {
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                SampledSpectrum f = bxdf.f(wo, wi, TransportMode.Radiance);
                if (Dot(wi, g) <= 0 && f[0] != 0)
                {
                    ++below;
                }
                evaluated += f[0] * AbsCosTheta(wi) * 4 * Math.PI / count;
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                             TransportMode.Radiance, BxDFReflTransFlags.All);
                if (bs)
                {
                    if (Dot(bs.value().wi, g) <= 0)
                    {
                        ++below;
                    }
                    EXPECT_TRUE(bs.value().pdfIsProportional);
                    sampled += bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / count;
                }
            }
        }

        [TEST("MicrofacetNormalMap", "EnergyAndSampling")]
        public static void EnergyAndSampling()
        {
            RNG rng = new RNG(11);
            BxDF white = new DiffuseBxDF(new SampledSpectrum(1f));
            BxDF metal = new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f));
            // The unperturbed normal in the frame of the mapped one: the map tilts the normal by 20 and by 50 degrees
            foreach (Float tilt in new Float[] { 20, 50 })
            {
                Vector3f g = Normalize(new Vector3f(std.sin(Radians(tilt)) * 0.8f, std.sin(Radians(tilt)) * 0.6f, std.cos(Radians(tilt))));
                foreach (BxDF nested in new BxDF[] { white, metal })
                {
                    string name = (nested == white ? "white diffuse" : "rough conductor") + pbrt.StringPrintf(", tilt %.0f", tilt);
                    MicrofacetNormalMapBxDF analytic = new MicrofacetNormalMapBxDF(Model(NormalMapModel.Default, 2, 0), g, nested);
                    MicrofacetNormalMapBxDF second = new MicrofacetNormalMapBxDF(Model(NormalMapModel.Walk, 2, 0), g, nested);
                    MicrofacetNormalMapBxDF walk = new MicrofacetNormalMapBxDF(Model(NormalMapModel.Walk, 40, 0), g, nested);
                    MicrofacetNormalMapBxDF same = new MicrofacetNormalMapBxDF(Model(NormalMapModel.Walk, 40, 1), g, nested);
                    MicrofacetNormalMapBxDF heights = new MicrofacetNormalMapBxDF(Model(NormalMapModel.NonSeparable, 40, 0), g, nested);
                    EXPECT_TRUE(analytic.Valid());
                    double worstAnalytic = 0, worstSecond = 0, worstWalk = 0, worstSame = 0, worstHeights = 0, worstSecondAnalytic = 0, lowest = 1, highest = 0;
                    for (int direction = 0; direction < 5; ++direction)
                    {
                        Vector3f wo;
                        do
                        {
                            wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        } while (Dot(wo, g) < 0.2f || wo.z < 0.1f);
                        const int count = 300000;
                        Albedo(analytic, wo, g, ref rng, count, out double eA, out double sA, out int belowA);
                        Albedo(second, wo, g, ref rng, count, out double eS, out double sS, out int belowS);
                        Albedo(walk, wo, g, ref rng, count, out double eW, out double sW, out int belowW);
                        Albedo(same, wo, g, ref rng, count, out double eM, out double sM, out int belowM);
                        Albedo(heights, wo, g, ref rng, count, out double eH, out double sH, out int belowH);
                        EXPECT_EQ(0, belowA + belowS + belowW + belowM + belowH, name);
                        worstAnalytic = Math.Max(worstAnalytic, Math.Abs(eA - sA) / sA);
                        worstSecond = Math.Max(worstSecond, Math.Abs(eS - sS) / sS);
                        worstWalk = Math.Max(worstWalk, Math.Abs(eW - sW) / sW);
                        worstSame = Math.Max(worstSame, Math.Abs(eM - sM) / sM);
                        worstHeights = Math.Max(worstHeights, Math.Abs(eH - sH) / sH);
                        worstSecondAnalytic = Math.Max(worstSecondAnalytic, Math.Abs(sS - sA) / sA);
                        if (nested == white)
                        {
                            lowest = Math.Min(lowest, Math.Min(sW, Math.Min(sM, sH)));
                            highest = Math.Max(highest, Math.Max(Math.Max(sA, sW), Math.Max(sM, sH)));
                            EXPECT_LT(sA, sW + 0.01);
                        }
                    }
                    Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet normal map, %s: integral of f against the samples: analytic %.2e, walk to " +
                                                              "order 2 %.2e, walk %.2e, same material on the tangent facet %.2e, heights %.2e; walk to order 2 " +
                                                              "against analytic %.2e", name, worstAnalytic, worstSecond, worstWalk, worstSame, worstHeights,
                                                              worstSecondAnalytic));
                    EXPECT_LT(worstAnalytic, 0.02, name);
                    EXPECT_LT(worstSecond, 0.03, name);
                    EXPECT_LT(worstWalk, 0.03, name);
                    EXPECT_LT(worstSame, 0.03, name);
                    EXPECT_LT(worstHeights, 0.03, name);
                    EXPECT_LT(worstSecondAnalytic, 0.02, name);
                    if (nested == white)
                    {
                        Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet normal map, %s: albedo of the walks between %.4f and %.4f", name, lowest,
                                                                  highest));
                        EXPECT_GT(lowest, 0.99, name);
                        EXPECT_LT(highest, 1.01, name);
                    }
                }
            }
        }

        [TEST("MicrofacetNormalMap", "ReciprocityAndValidity")]
        public static void ReciprocityAndValidity()
        {
            RNG rng = new RNG(12);
            BxDF metal = new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f));
            Vector3f g = Normalize(new Vector3f(0.4f, -0.3f, 0.8f));
            MicrofacetNormalMapBxDF analytic = new MicrofacetNormalMapBxDF(Model(NormalMapModel.Default, 2, 0), g, metal);
            double worst = 0;
            int compared = 0;
            for (int i = 0; i < 20000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (Dot(wo, g) < 0.05f || Dot(wi, g) < 0.05f || std.abs(wo.z) < 0.05f || std.abs(wi.z) < 0.05f)
                {
                    continue;
                }
                double forward = analytic.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) / Dot(wi, g);
                double backward = analytic.f(wi, wo, TransportMode.Radiance)[0] * AbsCosTheta(wo) / Dot(wo, g);
                if (forward + backward > 1e-3)
                {
                    worst = Math.Max(worst, Math.Abs(forward - backward) / (forward + backward));
                    ++compared;
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet normal map: reciprocity of the analytic model with respect to g: %.2e over %d pairs",
                                                      worst, compared));
            EXPECT_GT(compared, 3000);
            EXPECT_LT(worst, 1e-3);

            // No perturbation, and a BxDF that is specular: not valid
            EXPECT_FALSE(new MicrofacetNormalMapBxDF(Model(NormalMapModel.Default, 2, 0), new Vector3f(0, 0, 1), metal).Valid());
            BxDF mirror = new ConductorBxDF(new TrowbridgeReitzDistribution(0, 0), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f));
            EXPECT_FALSE(new MicrofacetNormalMapBxDF(Model(NormalMapModel.Default, 2, 0), g, mirror).Valid());
        }
        // A BxDF that transmits (rough glass): the walk ends with the refracted ray wherever the BxDF of the mapped facet
        // transmits, and the evaluation adds the transmission of every vertex of the walk
        //   - the samples and the evaluation agree, for what is reflected and for what is transmitted (the transmitted lobe is
        //     narrow: its integral is taken with directions drawn from the glass itself, whose density is PDF());
        //   - the analytic model transmits what the glass does from the mapped facet, met at once or by the mirror of the
        //     tangent facet; a longer walk transmits more (light that is first reflected between the facets);
        //   - nothing is created: in the mode without the 1 / eta^2 of radiance, what is reflected and transmitted is at most 1;
        //   - from below the surface the glass is used as it is.
        [TEST("MicrofacetNormalMap", "Transmission")]
        public static void Transmission()
        {
            RNG rng = new RNG(13);
            BxDF glass = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.3f, 0.3f));
            Vector3f g = Normalize(new Vector3f(0.35f, -0.2f, 0.85f));
            Vector3f tangent = -Normalize(new Vector3f(-g.z * g.x, -g.z * g.y, 1 - g.z * g.z));
            Float sine = std.sqrt(1 - g.z * g.z);
            NormalMapParams[] models = new NormalMapParams[] { Model(NormalMapModel.Default, 2, 0), Model(NormalMapModel.Walk, 2, 0),
                                                               Model(NormalMapModel.Walk, 40, 0), Model(NormalMapModel.NonSeparable, 40, 0) };
            string[] names = new string[] { "analytic", "walk to order 2", "walk", "heights" };
            Vector3f[] viewers = new Vector3f[5];
            for (int direction = 0; direction < 5; ++direction)
            {
                Vector3f wo;
                do
                {
                    wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                } while (Dot(wo, g) < 0.3f || (direction < 3 ? wo.z < 0.3f : wo.z > 0.1f));
                viewers[direction] = wo;
            }
            double[] transmittedByAnalytic = new double[5];
            for (int m = 0; m < models.Length; ++m)
            {
                MicrofacetNormalMapBxDF mapped = new MicrofacetNormalMapBxDF(models[m], g, glass);
                EXPECT_TRUE(mapped.Valid());
                EXPECT_TRUE(IsTransmissive(mapped.Flags()));
                double worstReflected = 0, worstTransmitted = 0, worstGlass = 0, worstGlassSamples = 0, most = 0, leastGain = 1e9;
                for (int direction = 0; direction < 5; ++direction)
                {
                    Vector3f wo = viewers[direction];
                    Float lam = std.max<Float>(0, wo.z) / (std.max<Float>(0, wo.z) + std.max<Float>(0, Dot(wo, tangent)) * sine);
                    Vector3f mirrored = wo - 2 * Dot(wo, tangent) * tangent;
                    const int count = 600000;
                    double evaluatedR = 0, evaluatedT = 0, sampledR = 0, sampledT = 0, glassSampledT = 0, power = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        if (Dot(wi, g) > 0)
                        {
                            evaluatedR += mapped.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI / count;
                        }
                        else if (m == 0 && wi.z < 0 && i < 20000)
                        {
                            // The analytic model: the glass from the facet and by the mirror
                            double both = (wo.z > 0 ? lam * glass.f(wo, wi, TransportMode.Radiance)[0] : 0) +
                                          (mirrored.z > 0 ? (1 - lam) * glass.f(mirrored, wi, TransportMode.Radiance)[0] : 0);
                            worstGlass = Math.Max(worstGlass, Math.Abs(both - mapped.f(wo, wi, TransportMode.Radiance)[0]) / (both + 1e-3));
                        }
                        // A direction below the surface drawn from the glass at the mapped facet, met at once or after the mirror
                        // of the tangent facet: the density of PDF() there
                        Vector3f seen = rng.Uniform<Float>() < lam ? wo : mirrored;
                        pstd.optional<BSDFSample> gs = seen.z > 0 ? glass.Sample_f(seen, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                                   TransportMode.Radiance, BxDFReflTransFlags.All) : default;
                        if (gs && gs.value().pdf > 0 && gs.value().IsTransmission() && Dot(gs.value().wi, g) < 0)
                        {
                            glassSampledT += gs.value().f[0] * AbsCosTheta(gs.value().wi) / gs.value().pdf / count;
                            // (the density of these directions: PDF() without the part that the walks add everywhere)
                            Float density = mapped.PDF(wo, gs.value().wi, TransportMode.Radiance, BxDFReflTransFlags.All);
                            if (m > 0)
                            {
                                density -= .1f * std.max<Float>(0, -Dot(gs.value().wi, g)) * InvPi;
                            }
                            if (density > 0)
                            {
                                evaluatedT += mapped.f(wo, gs.value().wi, TransportMode.Radiance)[0] * AbsCosTheta(gs.value().wi) / density / count;
                            }
                        }
                        pstd.optional<BSDFSample> bs = mapped.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                       TransportMode.Radiance, BxDFReflTransFlags.All);
                        if (bs)
                        {
                            double weight = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / count;
                            if (bs.value().IsTransmission())
                            {
                                EXPECT_LT(Dot(bs.value().wi, g), 0);
                                EXPECT_EQ(1.5f, bs.value().eta);
                                sampledT += weight;
                            }
                            else
                            {
                                EXPECT_GT(Dot(bs.value().wi, g), 0);
                                sampledR += weight;
                            }
                        }
                        pstd.optional<BSDFSample> ps = mapped.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                       TransportMode.Importance, BxDFReflTransFlags.All);
                        if (ps)
                        {
                            power += ps.value().f[0] * AbsCosTheta(ps.value().wi) / ps.value().pdf / count;
                        }
                    }
                    worstReflected = Math.Max(worstReflected, Math.Abs(evaluatedR - sampledR) / sampledR);
                    worstTransmitted = Math.Max(worstTransmitted, Math.Abs(evaluatedT - sampledT) / sampledT);
                    most = Math.Max(most, power);
                    if (m == 0)
                    {
                        worstGlassSamples = Math.Max(worstGlassSamples, Math.Abs(glassSampledT - sampledT) / sampledT);
                        transmittedByAnalytic[direction] = sampledT;
                    }
                    else
                    {
                        leastGain = Math.Min(leastGain, sampledT / transmittedByAnalytic[direction]);
                    }
                }
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet normal map on rough glass, %s: integral of f against the samples: reflected " +
                                                          "%.2e, transmitted %.2e; largest reflected + transmitted power %.4f", names[m], worstReflected,
                                                          worstTransmitted, most));
                EXPECT_LT(worstReflected, 0.05, names[m]);
                EXPECT_LT(worstTransmitted, 0.02, names[m]);
                EXPECT_LT(most, 1.005, names[m]);
                if (m == 0)
                {
                    Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet normal map on rough glass, analytic: transmitted f against the glass at " +
                                                              "the facet and by the mirror %.2e, transmitted samples against those of the glass %.2e",
                                                              worstGlass, worstGlassSamples));
                    EXPECT_LT(worstGlass, 1e-5);
                    EXPECT_LT(worstGlassSamples, 0.01);
                }
                else
                {
                    Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet normal map on rough glass, %s: transmitted over what the analytic model " +
                                                              "transmits: at least %.4f", names[m], leastGain));
                    EXPECT_GT(leastGain, 0.99, names[m]);
                }

                // Nothing but numbers, whatever the directions (along the facets and the surface too)
                int notNumbers = 0;
                for (int i = 0; i < 400000; ++i)
                {
                    Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if ((i & 7) == 0)
                    {
                        wo = Normalize(new Vector3f(wo.x, wo.y, 1e-4f * wo.z));
                    }
                    TransportMode mode = (i & 1) == 0 ? TransportMode.Radiance : TransportMode.Importance;
                    SampledSpectrum value = mapped.f(wo, wi, mode);
                    Float density = mapped.PDF(wo, wi, mode, BxDFReflTransFlags.All);
                    pstd.optional<BSDFSample> bs = mapped.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), mode,
                                                                   BxDFReflTransFlags.All);
                    bool bad = !IsFinite(value[0]) || value[0] < 0 || !IsFinite(density) || density < 0;
                    if (bs)
                    {
                        bad = bad || !IsFinite(bs.value().f[0]) || bs.value().f[0] < 0 || !IsFinite(bs.value().pdf) || !IsFinite(bs.value().wi.x) ||
                              !IsFinite(bs.value().wi.z);
                        if (!bad)
                        {
                            // (a sample that is "proportional" has its density from PDF(): an integrator divides by it)
                            Float again = mapped.PDF(wo, bs.value().wi, mode, BxDFReflTransFlags.All);
                            SampledSpectrum back = mapped.f(wo, bs.value().wi, mode);
                            bad = !IsFinite(again) || !IsFinite(back[0]) || (bs.value().pdfIsProportional && !(again > 0));
                        }
                    }
                    if (bad)
                    {
                        if (notNumbers < 3)
                        {
                            Console.Error.WriteLine(pbrt.StringPrintf("NOT A NUMBER %s: wo %s wi %s f %f pdf %f sample %s", names[m], wo, wi, value[0], density,
                                                                      bs ? bs.value().ToString() : "none"));
                        }
                        ++notNumbers;
                    }
                }
                EXPECT_EQ(0, notNumbers, names[m]);

                // From below the surface: the glass as it is
                for (int i = 0; i < 2000; ++i)
                {
                    Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (Dot(wo, g) < 0)
                    {
                        EXPECT_EQ(glass.f(wo, wi, TransportMode.Radiance)[0], mapped.f(wo, wi, TransportMode.Radiance)[0]);
                        EXPECT_EQ(glass.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All),
                                  mapped.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All));
                    }
                }
            }
        }

        // The analytic model gives a Mueller matrix wherever it reflects: from the BxDF inside (a conductor, whose matrix
        // the polarised integrator builds) and the mirror of the tangent facet
        [TEST("MicrofacetNormalMap", "Polarised")]
        public static void Polarised()
        {
            RNG rng = new RNG(14);
            BxDF metal = new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f));
            Vector3f g = Normalize(new Vector3f(0.4f, -0.3f, 0.8f));
            MicrofacetNormalMapBxDF analytic = new MicrofacetNormalMapBxDF(Model(NormalMapModel.Default, 2, 0), g, metal);
            EXPECT_TRUE(analytic.HasMuellerMatrix());
            BSDF bsdf = new BSDF(new Normal3f(0.1f, 0.2f, 1), new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), analytic);
            int reflecting = 0, withMatrix = 0, sampled = 0, sampledWithMatrix = 0;
            for (int i = 0; i < 20000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                bool polarised;
                PolarisedLightSystem.Attenuation matrix;
                SampledSpectrum value = analytic.f(bsdf, wo, wi, TransportMode.Radiance, out polarised, out matrix);
                EXPECT_EQ(value[0], analytic.f(wo, wi, TransportMode.Radiance)[0]);
                if (value[0] > 1e-3f)
                {
                    ++reflecting;
                    if (polarised)
                    {
                        ++withMatrix;
                    }
                }
                pstd.optional<BSDFSample> bs = analytic.Sample_f(bsdf, wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                 TransportMode.Radiance, BxDFReflTransFlags.All, out polarised, out matrix);
                if (bs && bs.value().f[0] > 1e-3f)
                {
                    ++sampled;
                    if (polarised)
                    {
                        ++sampledWithMatrix;
                    }
                }
            }
            // Where neither direction sees the tangent facet, the only path is the BxDF itself: unpolarised light comes out
            // with the Stokes vector that the BxDF alone gives it. Where a path goes by the mirror, it does not.
            BSDF inside = bsdf.WithBxDF(metal);
            double worstDirect = 0, largestPolarisation = 0, largestChange = 0;
            int direct = 0, mirrored = 0;
            for (int i = 0; i < 40000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (wo.z < 0.05f || wi.z < 0.05f || Dot(wo, g) < 0.05f || Dot(wi, g) < 0.05f)
                {
                    continue;
                }
                bool polarised;
                PolarisedLightSystem.Attenuation matrix, alone;
                SampledSpectrum value = analytic.f(bsdf, wo, wi, TransportMode.Radiance, out polarised, out matrix);
                if (!(value[0] > 1e-3f) || !polarised ||
                    !PolarisedPathIntegrator.RoughAttenuation(inside, null, bsdf.LocalToRender(wo), bsdf.LocalToRender(wi), new SampledSpectrum(1f),
                                                              TransportMode.Radiance, out alone))
                {
                    continue;
                }
                StokesVector mine = (matrix * new PolarisedLightSystem.Light(new SampledSpectrum(1f))).ToStokesVector();
                StokesVector reference = (alone * new PolarisedLightSystem.Light(new SampledSpectrum(1f))).ToStokesVector();
                double difference = Math.Abs(mine.s0[0] - reference.s0[0]) + Math.Abs(mine.s1[0] - reference.s1[0]) + Math.Abs(mine.s2[0] - reference.s2[0]) +
                                    Math.Abs(mine.s3[0] - reference.s3[0]);
                // (the tangent facet of the test: t = -(p - c g) / |p - c g| in the frame of p)
                Vector3f tangent = -Normalize(new Vector3f(-g.z * g.x, -g.z * g.y, 1 - g.z * g.z));
                if (Dot(wo, tangent) <= 0 && Dot(wi, tangent) <= 0)
                {
                    ++direct;
                    worstDirect = Math.Max(worstDirect, difference);
                    largestPolarisation = Math.Max(largestPolarisation, Math.Sqrt(reference.s1[0] * reference.s1[0] + reference.s2[0] * reference.s2[0]));
                    EXPECT_LT(Math.Abs(mine.s0[0] - 1), 1e-4);
                }
                else
                {
                    ++mirrored;
                    largestChange = Math.Max(largestChange, difference);
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet normal map, polarised: unpolarised light through the model against the BxDF " +
                                                      "alone, %d pairs without the tangent facet: %.2e (degree of polarisation up to %.3f); %d pairs with " +
                                                      "it differ by up to %.3f", direct, worstDirect, largestPolarisation, mirrored, largestChange));
            EXPECT_GT(direct, 500);
            EXPECT_LT(worstDirect, 1e-4);
            EXPECT_GT(largestPolarisation, 0.05);
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet normal map, polarised: %d of %d evaluations and %d of %d samples have a matrix",
                                                      withMatrix, reflecting, sampledWithMatrix, sampled));
            EXPECT_GT(reflecting, 1000);
            EXPECT_EQ(reflecting, withMatrix);
            EXPECT_EQ(sampled, sampledWithMatrix);
        }
    }
}
