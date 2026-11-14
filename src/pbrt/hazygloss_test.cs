// (not in pbrt-v4) hazygloss_test.cs
//
// The hazy gloss model (hazygloss.cs):
//   - the mapping of the paper against the shaders of its supplemental material (hazyGlossConductor.brdf and
//     hazyGlossDielectric.brdf, transcribed here), and its two constraints: the core stays positive and the energy is conserved;
//   - what the mapping is for: the peak of the BRDF at normal incidence stays that of the core, rc D_n(n) / 4, whatever the haze;
//   - the composite BxDF of each kind is reciprocal, conserves energy, and its sampling agrees with its f and its pdf;
//   - the mixture of two distributions is a distribution (normalised, its visible normals sampled with their density), and the
//     intertwined masking differs from the separate one only away from the normal;
//   - the base under the interface of the plastic kind loses no energy.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class HazyGlossTests
    {
        // hazyGlossDielectric.brdf, HazeMapping(): the intensity of the haze
        static double ShaderHazeIntensity(double rc, double alphaH, double betaH, bool smoothVar, double w)
        {
            double p = 1.0 / ((1 + alphaH) * (1 + alphaH));
            if (!smoothVar)
            {
                return rc > p ? betaH * (1 - rc) / (1 - p) : betaH * rc / p;
            }
            double b = 2 * (rc * (1 - w) + w * p);
            double u;
            if (Math.Abs(2 * (b - 1)) < 0.000001)
            {
                u = (2 * w * p - 1 - (4 * w * w * p * p - 4 * w * w * p + 1) * (rc - (0.5 - w * p) / (1 - w))) / (2 * (w - 1));
            }
            else
            {
                double D = b * b - 4 * (b - 1) * rc;
                u = (b - Math.Sqrt(D)) / (2 * (b - 1));
            }
            return (2 * (1 - u) * u * w * betaH) / ((1 - u) * (1 - u) + 2 * (1 - u) * u * w + u * u);
        }

        [TEST("HazyGloss", "MappingAgainstTheShaders")]
        public static void MappingAgainstTheShaders()
        {
            RNG rng = new RNG(17);
            double worstLinear = 0, worstBezier = 0, worstPeak = 0, worstColor = 0;
            int nearSingular = 0;
            for (int n = 0; n < 20000; ++n)
            {
                Float rc = 0.001f + 0.998f * rng.Uniform<Float>(), alphaN = 0.002f + 0.2f * rng.Uniform<Float>(), alphaH = 0.01f + 10 * rng.Uniform<Float>();
                Float betaH = 0.99f * rng.Uniform<Float>(), w = 1 + 9 * rng.Uniform<Float>();
                // The wide roughness and the peak ratio of the shader: alpha_w = alpha_n (1 + alpha_h), p = 1 / (1 + alpha_h)^2
                Float awx, awy;
                HazeMapping.WideRoughness(alphaN, alphaN, alphaH, alphaH, out awx, out awy);
                EXPECT_LT(Math.Abs(awx - alphaN * (1 + alphaH)) / awx, 1e-5);
                double pShader = 1.0 / ((1 + (double)alphaH) * (1 + alphaH));
                Float pGGX = HazeMapping.PeakRatio(new TrowbridgeReitzDistribution(alphaN, alphaN), new TrowbridgeReitzDistribution(awx, awy));
                Float pBeckmann = HazeMapping.PeakRatio(new BeckmannDistribution(alphaN, alphaN), new BeckmannDistribution(awx, awy));
                worstPeak = Math.Max(worstPeak, Math.Max(Math.Abs(pGGX - pShader), Math.Abs(pBeckmann - pShader)) / pShader);

                Float p = (Float)pShader;
                double linear = HazeMapping.HazeIntensity(rc, p, betaH);
                worstLinear = Math.Max(worstLinear, Math.Abs(linear - ShaderHazeIntensity(rc, alphaH, betaH, false, w)));
                double bezier = HazeMapping.HazeIntensityBezier(rc, p, betaH, w);
                // (the shader divides by 2 (b - 1): where that is small its square root loses its digits, and the port has the
                // stable roots for that reason)
                double b = 2 * (rc * (1 - (double)w) + w * pShader);
                if (Math.Abs(b - 1) < 1e-3)
                {
                    ++nearSingular;
                }
                else
                {
                    worstBezier = Math.Max(worstBezier, Math.Abs(bezier - ShaderHazeIntensity(rc, alphaH, betaH, true, w)));
                }
                foreach (double kh in new double[] { linear, bezier })
                {
                    // The core stays positive (rc - p kh >= 0) and the reflectivity below one
                    EXPECT_GE(kh, 0);
                    EXPECT_GE(rc - pShader * kh, -1e-5);
                    EXPECT_LE(rc + (1 - pShader) * kh, 1 + 1e-5);
                }
                // The smooth curve stays under the corner of the linear one
                EXPECT_LE(bezier, linear + 1e-5);

                // ColorMapping(): Gulbrandsen, n = mix(nmax, nmin, g), and the reflectance at normal incidence is r again
                Float g = rng.Uniform<Float>(), eta, k;
                HazeMapping.GulbrandsenEtaK(rc, g, out eta, out k);
                double sr = Math.Sqrt(rc);
                double nShader = (1 + sr) / (1 - sr) * (1 - g) + (1 - (double)rc) / (1 + rc) * g;
                double kShader = Math.Sqrt((rc * (nShader + 1) * (nShader + 1) - (nShader - 1) * (nShader - 1)) / (1 - rc));
                worstColor = Math.Max(worstColor, Math.Max(Math.Abs(eta - nShader) / nShader, Math.Abs(k - kShader) / (1 + kShader)));
                EXPECT_LT(Math.Abs(FrComplex(1, new pstd.complex(eta, k)) - rc), 2e-4);
            }
            HazeMapping.GulbrandsenEtaK(0.3f, 0, out Float etaNoTint, out Float kNoTint);
            EXPECT_LT(Math.Abs(kNoTint), 1e-3);
            Console.WriteLine("MEASURE hazy gloss mapping: against the shaders, haze intensity {0:E2} (Equation 7) and {1:E2} (Bezier, {2} cases by its singular " +
                              "point left out), peak ratio {3:E2}, colour mapping {4:E2}", worstLinear, worstBezier, nearSingular, worstPeak, worstColor);
            EXPECT_LT(worstLinear, 1e-5);
            EXPECT_LT(worstBezier, 2e-4);
            EXPECT_LT(worstPeak, 1e-4);
            EXPECT_LT(worstColor, 1e-4);
        }

        static ParsedParameter Number(string name, params Float[] values)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "float";
            p.name = name;
            foreach (Float v in values)
            {
                p.AddFloat(v);
            }
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

        static ParsedParameter Colour(string name, Float r, Float g, Float b)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "rgb";
            p.name = name;
            p.AddFloat(r);
            p.AddFloat(g);
            p.AddFloat(b);
            return p;
        }

        static ParsedParameter Grey(string name, Float value)
        {
            return Colour(name, value, value, value);
        }

        static HazyGlossMaterial Material(params ParsedParameter[] parameters)
        {
            TextureParameterDictionary d = new TextureParameterDictionary(new ParameterDictionary(new List<ParsedParameter>(parameters), RGBColorSpace.sRGB),
                                                                          new NamedTextures());
            return HazyGlossMaterial.Create(d, null, new FileLoc(), new Allocator());
        }

        static HazyGlossBxDF BxDFOf(HazyGlossMaterial material)
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            return material.GetBxDF(new UniversalTextureEvaluator(), new MaterialEvalContext(), ref lambda);
        }

        [TEST("HazyGloss", "ThePeakIsTheCore")]
        public static void ThePeakIsTheCore()
        {
            Vector3f n = new Vector3f(0, 0, 1);
            double worst = 0;
            foreach (string distribution in new string[] { "ggx", "beckmann" })
            {
                foreach (string kind in new string[] { "conductor", "dielectric", "plastic" })
                {
                    foreach (Float rc in new Float[] { 0.04f, 0.3f, 0.8f })
                    {
                        foreach (Float haziness in new Float[] { 0, 0.3f, 0.9f })
                        {
                            foreach (Float extent in new Float[] { 1, 5 })
                            {
                                foreach (Float smoothing in new Float[] { 0, 4 })
                                {
                                    Float alpha = 0.05f;
                                    HazyGlossMaterial material = Material(Text("kind", kind), Text("distribution", distribution), Grey("reflectance", rc),
                                                                          Number("roughness", alpha), Number("haziness", haziness), Number("hazeextent", extent),
                                                                          Number("smoothing", smoothing), Grey("edgetint", 0.4f));
                                    SampledSpectrum f = BxDFOf(material).f(n, n, TransportMode.Radiance);
                                    // The core alone: rc D_n(n) / 4, D_n(n) = 1 / (pi alpha^2) for both distributions
                                    double core = rc / (4 * Math.PI * alpha * alpha);
                                    worst = Math.Max(worst, Math.Abs(f[0] - core) / core);
                                    if (haziness > 0)
                                    {
                                        EXPECT_GT(BxDFOf(material).Beta(), 0);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Console.WriteLine("MEASURE hazy gloss: the peak at normal incidence is off the peak of the core by {0:E2} over 216 materials", worst);
            EXPECT_LT(worst, 2e-3);

            // A coloured conductor: the haze has the colour of the core, each wavelength keeps its peak
            HazyGlossMaterial gold = Material(Colour("reflectance", 0.3f, 0.2f, 0.1f), Number("roughness", 0.05f), Number("haziness", 0.7f));
            HazyGlossMaterial plain = Material(Colour("reflectance", 0.3f, 0.2f, 0.1f), Number("roughness", 0.05f), Number("haziness", 0));
            SampledSpectrum hazy = BxDFOf(gold).f(n, n, TransportMode.Radiance), clear = BxDFOf(plain).f(n, n, TransportMode.Radiance);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_LT(Math.Abs(hazy[i] - clear[i]) / clear[i], 2e-3);
            }
            // Away from the peak the haze is there: a half vector 22 degrees off the normal, eight times the narrow roughness
            Vector3f wo = n, wi = new Vector3f(0.69f, 0, 0.724f);
            EXPECT_GT(BxDFOf(gold).f(wo, wi, TransportMode.Radiance)[0], 5 * BxDFOf(plain).f(wo, wi, TransportMode.Radiance)[0]);
        }

        [TEST("HazyGloss", "CompositeBxDFs")]
        public static void CompositeBxDFs()
        {
            BxDFCheckOptions options = new BxDFCheckOptions();
            ParsedParameter[] haze = { Number("roughness", 0.12f), Number("haziness", 0.6f), Number("hazeextent", 3) };
            BxDFCheckResult result = BxDFCheck.Run(BxDFOf(Material(haze[0], haze[1], haze[2], Colour("reflectance", 0.9f, 0.6f, 0.2f))), "HazyGloss conductor", options);
            // (stock pbrt lobes: a sampled normal whose reflection goes below the surface is dropped)
            BxDFCheck.Expect(result, "HazyGloss conductor", 2e-3, 1.005, 5, 0.85);
            result = BxDFCheck.Run(BxDFOf(Material(haze[0], haze[1], haze[2], Text("kind", "conductor"), Text("masking", "intertwined"), Grey("reflectance", 0.7f))),
                                   "HazyGloss conductor intertwined", options);
            BxDFCheck.Expect(result, "HazyGloss conductor intertwined", 2e-3, 1.005, 5, 0.85);
            result = BxDFCheck.Run(BxDFOf(Material(haze[0], haze[1], haze[2], Text("kind", "conductor"), Text("distribution", "beckmann"), Grey("reflectance", 0.7f))),
                                   "HazyGloss conductor Beckmann", options);
            BxDFCheck.Expect(result, "HazyGloss conductor Beckmann", 2e-3, 1.005, 5, 0.85);
            result = BxDFCheck.Run(BxDFOf(Material(haze[0], haze[1], haze[2], Text("kind", "plastic"), Grey("reflectance", 0.05f),
                                                   Grey("base.reflectance", 0.8f), Flag("base.coupled", true))),
                                   "HazyGloss plastic coupled base", options);
            BxDFCheck.Expect(result, "HazyGloss plastic coupled base", 2e-3, 1.005, 5, 0.85);
            result = BxDFCheck.Run(BxDFOf(Material(haze[0], haze[1], haze[2], Text("mode", "physical"), Number("beta", 0.4f), Number("wideroughness", 0.4f))),
                                   "HazyGloss physical copper", options);
            BxDFCheck.Expect(result, "HazyGloss physical copper", 2e-3, 1.005, 5, 0.85);
        }

        static ParsedParameter Flag(string name, bool value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "bool";
            p.name = name;
            p.AddBool(value);
            return p;
        }

        [TEST("HazyGloss", "Dielectric")]
        public static void Dielectric()
        {
            // Reflection and refraction: energy conserved, sampling in agreement (the reciprocity of a refraction has the
            // factor of the indices, and is that of the lobes of pbrt)
            HazyGlossBxDF bxdf = BxDFOf(Material(Text("kind", "dielectric"), Grey("reflectance", 0.05f), Number("roughness", 0.12f), Number("haziness", 0.6f),
                                                 Number("hazeextent", 3)));
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "HazyGloss dielectric", new BxDFCheckOptions());
            EXPECT_EQ(0, result.invalid);
            EXPECT_LT(result.albedoMax, 1.005);
            EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 5e-3);
            EXPECT_LT(result.pdfMax, 1.01);
            EXPECT_LT(result.sampleF, 2e-3);
            EXPECT_LT(result.samplePdf, 2e-3);
            EXPECT_TRUE(IsTransmissive(bxdf.Flags()));

            // Equation 1 itself: the composite is (1 - beta) fn + beta fw of the two lobes that the mapping gives
            HazyGlossMaterial material = Material(Text("kind", "dielectric"), Grey("reflectance", 0.05f), Number("roughness", 0.12f), Number("haziness", 0.6f),
                                                  Number("hazeextent", 3));
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            HazyGlossParameters q = material.Parameters(new UniversalTextureEvaluator(), new MaterialEvalContext(), ref lambda);
            EXPECT_LT(Math.Abs(q.awx - 0.12f * 4), 1e-5);
            DielectricBxDF narrow = new DielectricBxDF(q.dielectricEta, new TrowbridgeReitzDistribution(q.anx, q.any));
            DielectricBxDF wide = new DielectricBxDF(q.dielectricEta, new TrowbridgeReitzDistribution(q.awx, q.awy));
            RNG rng = new RNG(9);
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Float expected = (1 - q.beta) * narrow.f(wo, wi, TransportMode.Radiance)[0] + q.beta * wide.f(wo, wi, TransportMode.Radiance)[0];
                EXPECT_LT(Math.Abs(bxdf.f(wo, wi, TransportMode.Radiance)[0] - expected), 1e-5 * (1 + expected));
            }
        }

        [TEST("HazyGloss", "MixtureDistribution")]
        public static void Mixture()
        {
            MicrofacetDistribution narrow = new TrowbridgeReitzDistribution(0.08f, 0.12f), wide = new TrowbridgeReitzDistribution(0.4f, 0.5f);
            MixtureDistribution mixture = new MixtureDistribution(narrow, wide, 0.35f);
            // The projected area of the normals is 1, and of those turned to a direction cos (1 + Lambda)
            Vector3f[] directions = { Normalize(new Vector3f(0.2f, 0.1f, 1)), Normalize(new Vector3f(1, -0.4f, 0.5f)), Normalize(new Vector3f(-0.9f, 0.3f, 0.12f)) };
            int steps = 1400;
            double area = 0;
            double[] visible = new double[3], mean = new double[3], density = new double[3];
            for (int i = 0; i < steps; ++i)
            {
                // (the normals by their slope, which spreads the points where the distributions are)
                double radius = (i + 0.5) / steps;
                double tan = Math.Tan(radius * Math.PI / 2);
                double cos = 1 / Math.Sqrt(1 + tan * tan), sin = tan * cos;
                double dTheta = Math.PI / 2 / steps;
                for (int j = 0; j < 360; ++j)
                {
                    double phi = (j + 0.5) / 360 * 2 * Math.PI;
                    Vector3f wm = new Vector3f((Float)(sin * Math.Cos(phi)), (Float)(sin * Math.Sin(phi)), (Float)cos);
                    double weight = sin * dTheta * (2 * Math.PI / 360);
                    double D = mixture.D(wm);
                    area += D * cos * weight;
                    for (int d = 0; d < 3; ++d)
                    {
                        double towards = Math.Max(0.0, Dot(directions[d], wm));
                        visible[d] += D * towards * weight;
                        double pdf = mixture.PDF_SingleSided(directions[d], wm);
                        density[d] += pdf * weight;
                        mean[d] += pdf * wm.z * weight;
                    }
                }
            }
            EXPECT_LT(Math.Abs(area - 1), 3e-3);
            RNG rng = new RNG(23);
            for (int d = 0; d < 3; ++d)
            {
                double sigma = mixture.Sigma(directions[d]);
                EXPECT_LT(Math.Abs(visible[d] - sigma) / sigma, 4e-3);
                EXPECT_LT(Math.Abs(sigma - directions[d].z * (1 + mixture.Lambda(directions[d]))) / sigma, 1e-4);
                EXPECT_LT(Math.Abs(density[d] - 1), 4e-3);
                // The sampled normals have that density: their mean height against the integral
                double sum = 0;
                int count = 400000;
                for (int i = 0; i < count; ++i)
                {
                    sum += mixture.Sample_wm_SingleSided(directions[d], new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())).z;
                }
                Console.WriteLine("MEASURE mixture distribution: direction {0}, projected area {1:F5} against {2:F5}, mean height of the sampled normals {3:F5} " +
                                  "against {4:F5}", d, visible[d], sigma, sum / count, mean[d]);
                EXPECT_LT(Math.Abs(sum / count - mean[d]), 3e-3);
            }

            // The intertwined mixture is the separate one at the normal (no masking there) and not at grazing angles
            HazyGlossBxDF separate = BxDFOf(Material(Grey("reflectance", 0.7f), Number("roughness", 0.1f), Number("haziness", 0.6f)));
            HazyGlossBxDF intertwined = BxDFOf(Material(Grey("reflectance", 0.7f), Number("roughness", 0.1f), Number("haziness", 0.6f), Text("masking", "intertwined")));
            Vector3f n = new Vector3f(0, 0, 1);
            EXPECT_LT(Math.Abs(separate.f(n, n, TransportMode.Radiance)[0] - intertwined.f(n, n, TransportMode.Radiance)[0]) / separate.f(n, n, TransportMode.Radiance)[0],
                      1e-4);
            Vector3f wo = Normalize(new Vector3f(1, 0, 0.15f)), wi = Normalize(new Vector3f(-1, 0.05f, 0.2f));
            Float a = separate.f(wo, wi, TransportMode.Radiance)[0], b = intertwined.f(wo, wi, TransportMode.Radiance)[0];
            EXPECT_GT(Math.Abs(a - b) / a, 0.02);
        }

        [TEST("HazyGloss", "CoupledBase")]
        public static void CoupledBase()
        {
            // A white base under a nearly smooth interface: what is not reflected by the interface comes back out of the base
            HazyGlossBxDF white = BxDFOf(Material(Text("kind", "plastic"), Grey("reflectance", 0.04f), Number("roughness", 0.01f), Grey("base.reflectance", 1),
                                                  Flag("base.coupled", true)));
            RNG rng = new RNG(5);
            foreach (Float cosine in new Float[] { 0.95f, 0.6f, 0.25f })
            {
                Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                double albedo = 0;
                int count = 300000;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<BSDFSample> bs = white.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                  TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (bs)
                    {
                        albedo += bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                    }
                }
                albedo /= count;
                Console.WriteLine("MEASURE hazy gloss coupled base: white base, cosine {0}, albedo {1:F4}", cosine, albedo);
                EXPECT_LT(albedo, 1.005);
                EXPECT_GT(albedo, 0.985);
            }
            // The internal diffuse reflectance of an interface of index 1.5 (0.5963 by the closed form of the literature)
            EXPECT_LT(Math.Abs(HazyGlossBxDF.InternalDiffuseReflectance(1.5f) - 0.5963), 2e-3);
        }
        static HazyDielectricBxDF InterfaceOf(HazyGlossMaterial material)
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            UniversalTextureEvaluator texEval = new UniversalTextureEvaluator();
            MaterialEvalContext ctx = new MaterialEvalContext();
            return material.InterfaceBxDF(texEval, ctx, ref lambda, material.Parameters(texEval, ctx, ref lambda));
        }

        // The albedo by Sample_f and its standard error
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

        // The cosine, on the outer side, of the facet that takes wo to wi (as HazyDielectricBxDF finds it)
        static Float OuterCosine(Vector3f wo, Vector3f wi, Float eta)
        {
            bool reflect = wo.z * wi.z > 0;
            Float etap = reflect ? 1 : (wo.z > 0 ? eta : 1 / eta);
            Vector3f wm = Normalize(wi * etap + wo);
            Vector3f outer = wo.z > 0 ? wo : wi;
            if (reflect && wo.z < 0)
            {
                // (both inside: the refracted ray of either)
                Float sin2 = Sqr(eta) * std.max<Float>(0, 1 - Sqr(AbsDot(wo, wm)));
                return sin2 >= 1 ? 0 : SafeSqrt(1 - sin2);
            }
            return AbsDot(outer, wm);
        }

        // The dielectric whose index of refraction the haze leaves alone
        [TEST("HazyGloss", "DielectricWithItsOwnIndex")]
        public static void DielectricWithItsOwnIndex()
        {
            RNG rng = new RNG(41);
            // With one index it is the composite of the paper
            HazyDielectricBxDF own = new HazyDielectricBxDF(1.5f, 1.5f, new TrowbridgeReitzDistribution(0.1f, 0.1f), new TrowbridgeReitzDistribution(0.3f, 0.3f), 0.4f);
            HazyGlossBxDF composite = new HazyGlossBxDF(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.1f, 0.1f)),
                                                        new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.3f, 0.3f)), 0.4f);
            double worst = 0, worstGrazing = 0;
            int compared = 0;
            for (int i = 0; i < 20000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                foreach (TransportMode mode in new TransportMode[] { TransportMode.Radiance, TransportMode.Importance })
                {
                    Float expected = composite.f(wo, wi, mode)[0], value = own.f(wo, wi, mode)[0];
                    double difference = Math.Abs(value - expected) / (1e-3 + expected);
                    // (where the facet is met at a grazing angle on its outer side, the reflectance is steep in the angle, and
                    // the two ways to the same number, from inside or from outside, differ by the rounding of the directions)
                    if (OuterCosine(wo, wi, 1.5f) > 0.25f)
                    {
                        worst = Math.Max(worst, difference);
                    }
                    else
                    {
                        worstGrazing = Math.Max(worstGrazing, difference);
                    }
                    if (expected > 0)
                    {
                        ++compared;
                    }
                }
            }
            Console.WriteLine("MEASURE hazy dielectric: with one index, {0:E2} from the composite of the paper ({1:E2} at grazing facets) over {2} values", worst,
                              worstGrazing, compared);
            EXPECT_LT(worst, 2e-5);
            EXPECT_LT(worstGrazing, 5e-3);
            EXPECT_GT(compared, 10000);

            // The index of the refraction does not move with the haze, as it does in the paper's mapping
            foreach (Float haziness in new Float[] { 0, 0.1f, 0.3f })
            {
                HazyGlossMaterial fixedIndex = Material(Text("kind", "dielectric"), Grey("reflectance", 0.04f), Number("haziness", haziness), Number("ior", 1.5f));
                EXPECT_TRUE(fixedIndex.SeparateTransmission());
                HazyGlossMaterial mapped = Material(Text("kind", "dielectric"), Grey("reflectance", 0.04f), Number("haziness", haziness));
                EXPECT_FALSE(mapped.SeparateTransmission());
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
                Float eta = mapped.Parameters(new UniversalTextureEvaluator(), new MaterialEvalContext(), ref lambda).dielectricEta;
                HazyDielectricBxDF surface = InterfaceOf(fixedIndex);
                Console.WriteLine("MEASURE hazy dielectric: haziness {0}: index of the paper's mapping {1:F3}; here, of the refraction {2:F3}, of the Fresnel term {3:F3}",
                                  haziness, eta, surface.EtaGeometry(), surface.EtaFresnel());
                EXPECT_LT(Math.Abs(surface.EtaGeometry() - 1.5), 1e-5);
                EXPECT_LT(Math.Abs(surface.EtaFresnel() - eta), 1e-5);
                if (haziness > 0)
                {
                    EXPECT_GT(eta, 1.55f);
                }
            }

            // Two indices: the checks of a BxDF
            HazyDielectricBxDF bxdf = InterfaceOf(Material(Text("kind", "dielectric"), Grey("reflectance", 0.04f), Number("roughness", 0.1f), Number("haziness", 0.3f),
                                                           Number("hazeextent", 2), Number("ior", 1.5f)));
            EXPECT_GT(bxdf.EtaFresnel(), 1.8f);
            BxDFCheckResult result = BxDFCheck.Run(bxdf, "HazyDielectric two indices", new BxDFCheckOptions());
            EXPECT_EQ(0, result.invalid);
            EXPECT_LT(result.albedoMax, 1.005);
            EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 5e-3);
            EXPECT_LT(result.pdfMax, 1.01);
            EXPECT_LT(result.sampleF, 2e-3);
            EXPECT_LT(result.samplePdf, 2e-3);

            // A path and its reverse: the refraction of pbrt has f(wo, wi) = etap^2 f(wi, wo) (importance mode), and the
            // reflection is symmetric; so here, with two indices
            double worstReverse = 0, worstReverseGrazing = 0;
            int pairs = 0;
            for (int i = 0; i < 20000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Float forward = bxdf.f(wo, wi, TransportMode.Importance)[0], back = bxdf.f(wi, wo, TransportMode.Importance)[0];
                if (!SameHemisphere(wo, wi))
                {
                    Float etap = wo.z > 0 ? bxdf.EtaGeometry() : 1 / bxdf.EtaGeometry();
                    back *= Sqr(etap);
                }
                if (forward > 1e-4f || back > 1e-4f)
                {
                    ++pairs;
                    double difference = Math.Abs(forward - back) / Math.Max(forward, back);
                    if (OuterCosine(wo, wi, bxdf.EtaGeometry()) > 0.25f)
                    {
                        worstReverse = Math.Max(worstReverse, difference);
                    }
                    else
                    {
                        worstReverseGrazing = Math.Max(worstReverseGrazing, difference);
                    }
                }
            }
            Console.WriteLine("MEASURE hazy dielectric: a path and its reverse differ by {0:E2} ({1:E2} at grazing facets) over {2} pairs", worstReverse,
                              worstReverseGrazing, pairs);
            EXPECT_LT(worstReverse, 1e-4);
            EXPECT_LT(worstReverseGrazing, 2e-2);
            EXPECT_GT(pairs, 3000);

            // The energy, with the two indices far apart and from both sides
            double largest = 0;
            string where = "";
            foreach (Float beta in new Float[] { 0, 0.5f, 1 })
            {
                foreach (Float geometry in new Float[] { 1.2f, 1.5f, 2.4f })
                {
                    foreach (Float fresnel in new Float[] { 1.2f, 3.5f })
                    {
                        HazyDielectricBxDF extreme = new HazyDielectricBxDF(geometry, fresnel, new TrowbridgeReitzDistribution(0.05f, 0.05f),
                                                                            new TrowbridgeReitzDistribution(0.6f, 0.6f), beta);
                        foreach (Float cosine in new Float[] { 0.99f, 0.7f, 0.4f, 0.15f, -0.99f, -0.8f, -0.5f })
                        {
                            Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                            double error;
                            double albedo = Albedo(extreme, wo, 40000, ref rng, out error);
                            EXPECT_LT(albedo - 4 * error, 1.0);
                            if (albedo > largest)
                            {
                                largest = albedo;
                                where = "beta " + beta + " index of refraction " + geometry + " of Fresnel " + fresnel + " cosine " + cosine;
                            }
                        }
                    }
                }
            }
            Console.WriteLine("MEASURE hazy dielectric: largest albedo over the extremes {0:F4} ({1})", largest, where);
        }

        // The haze of the transmission: the layer under the interface
        [TEST("HazyGloss", "TransmissionHaze")]
        public static void TransmissionHaze()
        {
            EXPECT_LT(Math.Abs(Math.Exp(-HazyTransmissionBxDF.OpticalThickness(0.3f)) - 0.7), 1e-5);
            EXPECT_EQ(0f, HazyTransmissionBxDF.OpticalThickness(0));
            RNG rng = new RNG(43);
            foreach (Float haziness in new Float[] { 0.3f, 0.8f })
            {
                HazyDielectricBxDF surface = new HazyDielectricBxDF(1.5f, 1.5f, new TrowbridgeReitzDistribution(0.1f, 0.1f), new TrowbridgeReitzDistribution(0.3f, 0.3f),
                                                                    0.2f);
                HazyTransmissionBxDF bxdf = new HazyTransmissionBxDF(surface, HazyTransmissionBxDF.OpticalThickness(haziness), 0.9f, 20, 1);
                // f is an estimate: averaged
                BxDFCheckOptions options = new BxDFCheckOptions();
                options.repeats = 16;
                options.samples = 100000;
                BxDFCheckResult result = BxDFCheck.Run(bxdf, "HazyTransmission " + haziness, options);
                EXPECT_EQ(0, result.invalid);
                // (what Sample_f gives is what f integrates to: the light that crosses without a scattering is in f)
                EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 1e-2, "sampled and integrated albedo differ: " + result.albedoSigmas + " sigma");
                // Not above one, from either side. (It is a little under the albedo of the interface alone: the light that the
                // layer turns back meets the interface again, and a rough interface of pbrt loses a little at each meeting.)
                foreach (Float cosine in new Float[] { 0.9f, 0.5f, -0.9f, -0.5f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                    double error, errorSurface;
                    double albedo = Albedo(bxdf, wo, 150000, ref rng, out error);
                    double alone = Albedo(surface, wo, 150000, ref rng, out errorSurface);
                    Console.WriteLine("MEASURE hazy transmission: haziness {0}, cosine {1}: albedo {2:F4} +- {3:F4}, of the interface alone {4:F4}", haziness, cosine,
                                      albedo, error, alone);
                    EXPECT_LT(albedo - 4 * error, 1.0);
                    EXPECT_LT(albedo, alone + 4 * (error + errorSurface));
                }
                // The layer absorbs nothing: under an interface that loses nothing (nearly smooth) the albedo is one
                HazyDielectricBxDF smooth = new HazyDielectricBxDF(1.5f, 1.5f, new TrowbridgeReitzDistribution(0.004f, 0.004f),
                                                                   new TrowbridgeReitzDistribution(0.004f, 0.004f), 0);
                HazyTransmissionBxDF lossless = new HazyTransmissionBxDF(smooth, HazyTransmissionBxDF.OpticalThickness(haziness), 0.9f, 100, 1);
                foreach (Float cosine in new Float[] { 0.9f, 0.5f, -0.9f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                    double error;
                    double albedo = Albedo(lossless, wo, 150000, ref rng, out error);
                    Console.WriteLine("MEASURE hazy transmission: haziness {0}, cosine {1}, smooth interface: albedo {2:F4} +- {3:F4}", haziness, cosine, albedo, error);
                    EXPECT_LT(Math.Abs(albedo - 1), 4 * error + 5e-3);
                }
            }
        }
        // The material as it is given, a narrow interface and its own length of the walk in the layer: no energy is lost
        // from either side, at any angle (a walk of 10 events loses a third of it from inside at grazing angles)
        [TEST("HazyGloss", "TransmissionHazeOfTheMaterial")]
        public static void TransmissionHazeOfTheMaterial()
        {
            HazyGlossMaterial material = Material(Text("kind", "dielectric"), Grey("reflectance", 0.04f), Number("roughness", 0.01f), Number("transmissionhaziness", 0.8f));
            HazyDielectricBxDF surface = InterfaceOf(material);
            EXPECT_LT(Math.Abs(surface.EtaGeometry() - 1.5), 1e-3);
            Float tau = material.TransmissionLayer(new UniversalTextureEvaluator(), new MaterialEvalContext());
            EXPECT_LT(Math.Abs(Math.Exp(-tau) - 0.2), 1e-4);
            HazyTransmissionBxDF bxdf = new HazyTransmissionBxDF(surface, tau, material.transmissionHazeG, material.layerDepth, material.layerSamples);
            RNG rng = new RNG(77);
            double least = 2, most = 0;
            foreach (Float cosine in new Float[] { 0.95f, 0.6f, 0.2f, -0.95f, -0.8f, -0.7f, -0.5f, -0.2f })
            {
                Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                double error;
                double albedo = Albedo(bxdf, wo, 80000, ref rng, out error);
                least = Math.Min(least, albedo);
                most = Math.Max(most, albedo);
                EXPECT_LT(Math.Abs(albedo - 1), 4 * error + 6e-3, "cosine " + cosine);
            }
            Console.WriteLine("MEASURE hazy transmission: the material with its defaults, albedo {0:F4} to {1:F4} over the angles from both sides", least, most);
        }
        // "scatterdepth" above one: the two lobes are the multiple-scattering microsurfaces (their f is an estimate)
        [TEST("HazyGloss", "MultipleScattering")]
        public static void MultipleScattering()
        {
            RNG rng = new RNG(61);
            foreach (string kind in new string[] { "conductor", "dielectric" })
            {
                ParsedParameter depth = new ParsedParameter(new FileLoc());
                depth.type = "integer";
                depth.name = "scatterdepth";
                depth.AddInt(8);
                Float reflectance = kind == "conductor" ? 1f : 0.04f;
                HazyGlossBxDF single = BxDFOf(Material(Text("kind", kind), Grey("reflectance", reflectance), Number("roughness", 0.25f), Number("haziness", 0.5f),
                                                       Number("hazeextent", 2)));
                HazyGlossBxDF multiple = BxDFOf(Material(Text("kind", kind), Grey("reflectance", reflectance), Number("roughness", 0.25f), Number("haziness", 0.5f),
                                                         Number("hazeextent", 2), depth));
                BxDFCheckOptions options = new BxDFCheckOptions();
                options.repeats = 16;
                options.samples = 60000;
                BxDFCheckResult result = BxDFCheck.Run(multiple, "HazyGloss " + kind + " scatterdepth 8", options);
                EXPECT_EQ(0, result.invalid);
                EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 1e-2, kind + ": sampled and integrated albedo differ, " + result.albedoSigmas + " sigma");
                // The energy that single scattering loses on a rough surface comes back, and no more than was sent
                foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                    double error, errorSingle;
                    double albedo = Albedo(multiple, wo, 100000, ref rng, out error);
                    double alone = Albedo(single, wo, 100000, ref rng, out errorSingle);
                    Console.WriteLine("MEASURE hazy gloss multiple scattering: {0}, cosine {1}: albedo {2:F4} +- {3:F4}, with single scattering {4:F4}", kind, cosine,
                                      albedo, error, alone);
                    // (the walk returns weights of exactly one for a surface that absorbs nothing: their mean is one to the
                    // rounding of a float, with no spread)
                    EXPECT_LT(albedo - 4 * error, 1.0 + 1e-6);
                    EXPECT_GT(albedo + 4 * (error + errorSingle), alone);
                }
            }
        }
    }
}
