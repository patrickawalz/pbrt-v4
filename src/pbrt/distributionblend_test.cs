// (not in pbrt-v4) distributionblend_test.cs
//
// The blends of distributions (distributionblend.cs, MixtureDistribution, LayeredDistribution), against the direct port of Facet
// Forge, which has them in its own code:
//   - a mixture of three distributions is a distribution, and the microfacet lobes of pbrt on it are the first reflection of
//     the Facet Forge blend; walked (MultiScatterConductorBxDF), it has the albedo of the Facet Forge blend with every reflection;
//   - the lobes of pbrt on a LayeredDistribution are the first reflection and the first refraction of the asymmetric layering of
//     Facet Forge, from either side;
//   - those lobes are reciprocal, lose no energy that the first reflection does not lose, and sample what they evaluate;
//   - the scene syntax builds the same distributions.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class DistributionBlendTests
    {
        static ff.Surface Three(bool layered)
        {
            ff.Surface s = new ff.Surface();
            s.ndf.n = 3;
            s.ndf.c[0].InitHeightfield(ff.Kind.Beckmann, 0.5, 0.5, 2);
            s.ndf.c[0].w = 0.3;
            s.ndf.c[1].InitHeightfield(ff.Kind.GGX, 0.12, 0.2, 2);
            s.ndf.c[1].w = 0.5;
            s.ndf.c[2].InitHeightfield(ff.Kind.StudentT, 0.3, 0.3, 3);
            s.ndf.c[2].w = 0.2;
            s.ndf.layered = layered;
            return s;
        }

        static MicrofacetDistribution[] Parts()
        {
            return new MicrofacetDistribution[] { new BeckmannDistribution(0.5f, 0.5f), new TrowbridgeReitzDistribution(0.12f, 0.2f), new StudentTDistribution(0.3f, 0.3f, 3) };
        }

        static Float[] Shares() { return new Float[] { 0.3f, 0.5f, 0.2f }; }

        static double Worst(BxDF a, BxDF b, bool through, ulong seed, out int count)
        {
            RNG rng = new RNG(seed);
            double worst = 0;
            count = 0;
            for (int i = 0; i < 6000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (Math.Abs(wo.z) < 0.05f || Math.Abs(wi.z) < 0.05f || (wo.z * wi.z < 0) != through)
                {
                    continue;
                }
                double x = a.f(wo, wi, TransportMode.Radiance)[0], y = b.f(wo, wi, TransportMode.Radiance)[0];
                if (Math.Max(x, y) > 1e-3)
                {
                    worst = Math.Max(worst, Math.Abs(x - y) / Math.Max(x, y));
                    ++count;
                }
            }
            return worst;
        }

        static double Albedo(BxDF bxdf, Vector3f wo, int count, ref RNG rng, out double error)
        {
            double sum = 0, squares = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Importance,
                                                             BxDFReflTransFlags.All);
                if (bs)
                {
                    double weight = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                    sum += weight;
                    squares += weight * weight;
                }
            }
            double mean = sum / count;
            error = Math.Sqrt(Math.Max(0.0, squares / count - mean * mean) / count);
            return mean;
        }

        [TEST("DistributionBlend", "MixtureOfThree")]
        public static void MixtureOfThree()
        {
            MixtureDistribution mixture = new MixtureDistribution(Parts(), Shares());
            // A distribution: the projected area of its normals along the normal is 1
            double area = 0;
            int n = 1500;
            for (int a = 0; a < n; ++a)
            {
                for (int b = 0; b < n; ++b)
                {
                    double px = Math.Tan(Math.PI * ((a + 0.5) / n - 0.5)), py = Math.Tan(Math.PI * ((b + 0.5) / n - 0.5));
                    double jac = (1 + px * px) * (1 + py * py) * (Math.PI / n) * (Math.PI / n);
                    Vector3f m = Normalize(new Vector3f((Float)(-px), (Float)(-py), 1));
                    area += mixture.D(m) * Math.Pow(m.z, 4) * jac;
                }
            }
            EXPECT_LT(Math.Abs(area - 1), 5e-3);

            // The first reflection of the Facet Forge blend
            ff.Surface surface = Three(false);
            surface.facet.kind = ff.FacetKind.Conductor;
            surface.facet.eta = new SampledSpectrum(0.2f);
            surface.facet.k = new SampledSpectrum(3.5f);
            ff.Surface once = surface.Clone();
            once.maxWalk = 1;
            int count;
            double worst = Worst(new ConductorBxDF(mixture, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)), new FacetForgeBxDF(once, null, 1.5f, false), false, 3,
                                 out count);
            EXPECT_GT(count, 500);
            EXPECT_LT(worst, 2e-3);

            // Every reflection
            RNG rng = new RNG(9);
            MultiScatterConductorBxDF walked = new MultiScatterConductorBxDF(mixture, new MicrosurfaceHeightUniform(), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f),
                                                                             100);
            FacetForgeBxDF forge = new FacetForgeBxDF(surface, null, 1.5f, false);
            foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
            {
                Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                double e0, e1;
                double mine = Albedo(walked, wo, 100000, ref rng, out e0), theirs = Albedo(forge, wo, 100000, ref rng, out e1);
                Console.WriteLine("MEASURE mixture of three, cosine {0}: first reflection against facet forge {1:E2}; albedo with every reflection {2:F4} against " +
                                  "{3:F4} (+- {4:F4})", cosine, worst, mine, theirs, Math.Sqrt(e0 * e0 + e1 * e1));
                EXPECT_LT(Math.Abs(mine - theirs), 5 * Math.Sqrt(e0 * e0 + e1 * e1) + 2e-3);
            }
            BxDFCheck.Expect(BxDFCheck.Run(new ConductorBxDF(mixture, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)), "Conductor on a mixture of three",
                                           new BxDFCheckOptions()), "Conductor on a mixture of three", 2e-3, 1.005, 5, 0.9);
        }

        [TEST("DistributionBlend", "StrataAgainstFacetForge")]
        public static void StrataAgainstFacetForge()
        {
            LayeredDistribution strata = new LayeredDistribution(Parts(), Shares());
            ff.Surface metal = Three(true);
            metal.facet.kind = ff.FacetKind.Conductor;
            metal.facet.eta = new SampledSpectrum(0.2f);
            metal.facet.k = new SampledSpectrum(3.5f);
            metal.maxWalk = 1;
            ff.Surface glass = Three(true);
            glass.facet.kind = ff.FacetKind.Dielectric;
            glass.maxWalk = 1;
            int c0, c1, c2;
            double conductor = Worst(new ConductorBxDF(strata, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)), new FacetForgeBxDF(metal, null, 1.5f, false), false, 5,
                                     out c0);
            DielectricBxDF mine = new DielectricBxDF(1.5f, strata);
            FacetForgeBxDF theirs = new FacetForgeBxDF(glass, null, 1.5f, true);
            double reflection = Worst(mine, theirs, false, 7, out c1), refraction = Worst(mine, theirs, true, 11, out c2);
            Console.WriteLine("MEASURE strata of three against facet forge (one scattering): conductor {0:E2} ({1} pairs), dielectric reflection {2:E2} ({3}), " +
                              "refraction {4:E2} ({5})", conductor, c0, reflection, c1, refraction, c2);
            EXPECT_GT(c0, 300);
            EXPECT_GT(c1, 300);
            EXPECT_GT(c2, 300);
            EXPECT_LT(conductor, 2e-3);
            EXPECT_LT(reflection, 2e-3);
            EXPECT_LT(refraction, 2e-3);
        }

        [TEST("DistributionBlend", "ChecksOfTheStrata")]
        public static void ChecksOfTheStrata()
        {
            LayeredDistribution strata = new LayeredDistribution(Parts(), Shares());
            BxDFCheckOptions options = new BxDFCheckOptions();
            BxDFCheckResult conductor = BxDFCheck.Run(new ConductorBxDF(strata, new SampledSpectrum(1f), new SampledSpectrum(1e4f)), "Conductor on strata of three", options);
            // (a lobe of one reflection drops the normals that reflect below the surface: its pdf integrates to less than one on a
            // rough surface, here as for one distribution)
            BxDFCheck.Expect(conductor, "Conductor on strata of three", 2e-3, 1.005, 5, 0.9);
            BxDFCheckResult dielectric = BxDFCheck.Run(new DielectricBxDF(1.5f, strata), "Dielectric on strata of three", options);
            BxDFCheck.Expect(dielectric, "Dielectric on strata of three", 2e-3, 1.005, 5, 0.9);
            // Strata with one distribution are that distribution: the reflection of pbrt
            LayeredDistribution same = new LayeredDistribution(new MicrofacetDistribution[] { new TrowbridgeReitzDistribution(0.3f, 0.3f),
                                                                                              new TrowbridgeReitzDistribution(0.3f, 0.3f) }, new Float[] { 0.4f, 0.6f });
            int count;
            double worst = Worst(new ConductorBxDF(same, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)),
                                 new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)), false, 13, out count);
            Console.WriteLine("MEASURE strata of one distribution against that distribution: {0:E2}", worst);
            EXPECT_LT(worst, 1e-3);
        }

        static ParsedParameter Number(string name, Float value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "float";
            p.name = name;
            p.AddFloat(value);
            return p;
        }

        static ParsedParameter Text(string name, string value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "string";
            p.name = name;
            p.AddString(value);
            return p;
        }

        static ParsedParameter Flag(string name, bool value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "bool";
            p.name = name;
            p.AddBool(value);
            return p;
        }

        [TEST("DistributionBlend", "SceneSyntax")]
        public static void SceneSyntax()
        {
            foreach (string layering in new string[] { "mixture", "asymmetric" })
            {
                List<ParsedParameter> list = new List<ParsedParameter>(new ParsedParameter[] {
                    Flag("remaproughness", false), Text("distribution", "beckmann"), Number("roughness", 0.5f), Number("weight", 0.3f),
                    Text("distribution1", "ggx"), Number("uroughness1", 0.12f), Number("vroughness1", 0.2f), Number("weight1", 0.5f),
                    Text("distribution2", "studentt"), Number("roughness2", 0.3f), Number("gamma2", 3), Number("weight2", 0.2f), Text("layering", layering) });
                TextureParameterDictionary d = new TextureParameterDictionary(new ParameterDictionary(list, RGBColorSpace.sRGB), new NamedTextures());
                ConductorMaterial material = ConductorMaterial.Create(d, null, new FileLoc(), new Allocator());
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
                ConductorBxDF built = material.GetBxDF(new UniversalTextureEvaluator(), new MaterialEvalContext(), ref lambda);
                SampledSpectrum eta = GetNamedSpectrum("metal-Cu-eta").Sample(lambda), k = GetNamedSpectrum("metal-Cu-k").Sample(lambda);
                MicrofacetDistribution direct = layering == "mixture" ? new MixtureDistribution(Parts(), Shares()) : new LayeredDistribution(Parts(), Shares());
                int count;
                double worst = Worst(built, new ConductorBxDF(direct, eta, k), false, 17, out count);
                Console.WriteLine("MEASURE scene syntax, layering {0}: the conductor of the parameters against the distribution built directly {1:E2}", layering, worst);
                EXPECT_GT(count, 300);
                EXPECT_LT(worst, 1e-4);
            }
        }
    }
}
