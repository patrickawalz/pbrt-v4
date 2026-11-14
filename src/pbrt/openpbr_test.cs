// (not in pbrt-v4) openpbr_test.cs
//
// OpenPBRMaterial (openpbr.cs):
//   - every part of the model, alone and together: no invalid values, an albedo of at most one, and samples, density and
//     evaluation in agreement;
//   - with its defaults it is a rough dielectric on a Lambertian base (the coated diffuse of pbrt);
//   - nothing is lost where nothing absorbs: a white base, a perfect metal, a clear coat and a white fuzz on them, with the lobes
//     that follow every reflection;
//   - coat_color is the colour of a white base seen and lit along the normal through the coat;
//   - the Mueller matrices: sampled paths against evaluated directions.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class OpenPBRTests
    {
        internal static ParsedParameter N(string name, Float value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "float";
            p.name = name;
            p.AddFloat(value);
            return p;
        }

        internal static ParsedParameter I(string name, int value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "integer";
            p.name = name;
            p.AddInt(value);
            return p;
        }

        internal static ParsedParameter T(string name, string value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "string";
            p.name = name;
            p.AddString(value);
            return p;
        }

        internal static ParsedParameter B(string name, bool value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "bool";
            p.name = name;
            p.AddBool(value);
            return p;
        }

        internal static ParsedParameter C(string name, Float r, Float g, Float b)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "rgb";
            p.name = name;
            p.AddFloat(r);
            p.AddFloat(g);
            p.AddFloat(b);
            return p;
        }

        internal static BxDF Build(params ParsedParameter[] list)
        {
            TextureParameterDictionary d = new TextureParameterDictionary(new ParameterDictionary(new List<ParsedParameter>(list), RGBColorSpace.sRGB), new NamedTextures());
            OpenPBRMaterial material = OpenPBRMaterial.Create(d, null, new FileLoc(), new Allocator());
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            return material.GetBxDF(new UniversalTextureEvaluator(), new MaterialEvalContext(), ref lambda);
        }

        static void Case(string name, bool bothSides, params ParsedParameter[] list)
        {
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.cosines = new Float[] { 0.9f, 0.5f, 0.2f };
            options.samples = 30000;
            options.pairs = 200;
            options.repeats = 8;
            options.bothSides = bothSides;
            BxDFCheckResult result = BxDFCheck.Run(Build(list), "OpenPBR " + name, options);
            EXPECT_EQ(0, result.invalid, name);
            EXPECT_LT(result.albedoMax, 1.005, name);
            EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 1.5e-2, name + ": Sample_f and f disagree about the albedo, " + result.albedoSigmas);
        }

        [TEST("OpenPBR", "TheParts")]
        public static void TheParts()
        {
            Case("defaults", false);
            Case("metal", false, N("base_metalness", 1), C("base_color", 0.9f, 0.6f, 0.3f));
            Case("metal by its index, every reflection", false, N("base_metalness", 1), C("metal_eta", 0.2f, 0.4f, 1.2f), C("metal_k", 3.5f, 2.4f, 1.8f),
                 T("metal_lobe", "multiscatter"), N("specular_roughness", 0.6f));
            Case("metal, facet forge with a second distribution", false, N("base_metalness", 1), T("metal_lobe", "facetforge"), T("metal_distribution", "beckmann"),
                 T("metal_distribution1", "ggx"), N("metal_roughness1", 0.1f), N("metal_weight", 0.5f), N("metal_weight1", 0.5f), N("specular_roughness", 0.6f));
            Case("coloured rough coat", false, N("coat_weight", 1), C("coat_color", 0.8f, 0.3f, 0.2f), N("coat_roughness", 0.2f));
            Case("half a coat on a metal", false, N("coat_weight", 0.5f), N("base_metalness", 1), C("coat_color", 0.8f, 0.3f, 0.2f));
            Case("fuzz", false, N("fuzz_weight", 1), C("fuzz_color", 0.8f, 0.8f, 0.8f), N("fuzz_roughness", 0.5f));
            Case("transmission", true, N("transmission_weight", 1), N("specular_roughness", 0.3f), C("transmission_color", 0.9f, 0.7f, 0.5f));
            Case("transmission, every reflection", true, N("transmission_weight", 1), N("specular_roughness", 0.5f), T("specular_lobe", "multiscatter"));
            Case("subsurface", false, N("subsurface_weight", 1), C("subsurface_color", 0.8f, 0.5f, 0.3f));
            Case("thin walled, subsurface and transmission", true, B("geometry_thin_walled", true), N("subsurface_weight", 1), N("transmission_weight", 0.5f));
            Case("thin film on a metal", false, N("base_metalness", 1), N("thin_film_weight", 1), N("thin_film_thickness", 0.4f));
            Case("thin film on the gloss", false, N("thin_film_weight", 0.7f), N("thin_film_thickness", 0.3f), N("thin_film_ior", 1.8f));
            Case("haze", false, N("base_metalness", 1), N("specular_roughness", 0.1f), N("specular_haze", 0.4f), N("specular_haze_spread", 0.5f));
            Case("retroreflective", false, N("base_metalness", 1), N("specular_retroreflectivity", 0.5f));
            Case("half opaque", true, N("geometry_opacity", 0.5f));
            Case("half of everything", false, N("base_metalness", 0.5f), N("coat_weight", 0.5f), N("fuzz_weight", 0.5f), N("subsurface_weight", 0.5f),
                 N("specular_weight", 0.5f), N("specular_roughness_anisotropy", 0.5f), N("thin_film_weight", 0.5f));
        }

        [TEST("OpenPBR", "DefaultsAreACoatedDiffuse")]
        public static void DefaultsAreACoatedDiffuse()
        {
            RNG rng = new RNG(5);
            BxDF model = Build();
            // base_color 0.8, specular_roughness 0.3 (alpha 0.09), specular_ior 1.5; a layer of no thickness
            BxDF coated = new CoatedDiffuseBxDF(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.09f, 0.09f)), new DiffuseBxDF(new SampledSpectrum(0.8f)), 1e-6f,
                                                new SampledSpectrum(0f), 0, 32, 1);
            foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
            {
                Vector3f wo = CombineTests.Direction(cosine, 1.1f);
                double a, e, b, eb;
                CombineTests.Albedo(model, wo, 200000, ref rng, out a, out e);
                CombineTests.Albedo(coated, wo, 200000, ref rng, out b, out eb);
                Console.WriteLine("MEASURE openpbr defaults against the coated diffuse of pbrt, cosine {0}: {1:F4} / {2:F4} (+- {3:F4})", cosine, a, b,
                                  Math.Sqrt(e * e + eb * eb));
                EXPECT_LT(Math.Abs(a - b), 4 * Math.Sqrt(e * e + eb * eb));
            }
        }

        [TEST("OpenPBR", "NothingIsLost")]
        public static void NothingIsLost()
        {
            RNG rng = new RNG(6);
            string[] names = new string[] { "white base, one reflection on the gloss", "white base, every reflection", "white base, facet forge", "perfect metal, one reflection",
                                            "perfect metal, every reflection", "clear rough coat on the white base, every reflection",
                                            "white fuzz on the white base, every reflection", "clear glass, every reflection" };
            BxDF[] cases = new BxDF[] {
                Build(C("base_color", 1, 1, 1), N("specular_roughness", 0.7f), I("maxdepth", 400)),
                Build(C("base_color", 1, 1, 1), N("specular_roughness", 0.7f), T("specular_lobe", "multiscatter"), I("maxdepth", 400), I("scatterdepth", 64)),
                Build(C("base_color", 1, 1, 1), N("specular_roughness", 0.7f), T("specular_lobe", "facetforge"), I("maxdepth", 400), I("scatterdepth", 64)),
                Build(C("base_color", 1, 1, 1), N("base_metalness", 1), N("specular_roughness", 0.7f)),
                Build(C("base_color", 1, 1, 1), N("base_metalness", 1), N("specular_roughness", 0.7f), T("metal_lobe", "multiscatter"), I("scatterdepth", 64)),
                Build(C("base_color", 1, 1, 1), N("specular_roughness", 0.7f), T("specular_lobe", "multiscatter"), N("coat_weight", 1), N("coat_roughness", 0.5f),
                      T("coat_lobe", "multiscatter"), I("maxdepth", 400), I("scatterdepth", 64)),
                Build(C("base_color", 1, 1, 1), N("specular_roughness", 0.7f), T("specular_lobe", "multiscatter"), N("fuzz_weight", 1), I("maxdepth", 400),
                      I("scatterdepth", 64)),
                Build(N("transmission_weight", 1), N("specular_roughness", 0.7f), T("specular_lobe", "multiscatter"), I("scatterdepth", 64)) };
            for (int c = 0; c < cases.Length; ++c)
            {
                foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                {
                    double a, e;
                    // (light paths: a path from the camera through an interface carries radiance, which is divided by eta^2)
                    CombineTests.Albedo(cases[c], CombineTests.Direction(cosine, 0.6f), 60000, ref rng, out a, out e, TransportMode.Importance);
                    Console.WriteLine("MEASURE openpbr white furnace, {0}, cosine {1}: {2:F4} +- {3:F4}", names[c], cosine, a, e);
                    EXPECT_LT(a, 1 + 4 * e + 1e-3, names[c]);
                    if (names[c].Contains("every reflection") || names[c].Contains("facet forge"))
                    {
                        EXPECT_GT(a, 0.985 - 4 * e, names[c]);
                    }
                }
            }
        }

        // The subsurface is a walk in a medium under the gloss: its samples and its evaluation agree, and without an interface a
        // grey subsurface_color is the albedo of the surface (the fit of van de Hulst that the specification uses)
        [TEST("OpenPBR", "Subsurface")]
        public static void Subsurface()
        {
            RNG rng = new RNG(15);
            BxDF sss = Build(N("subsurface_weight", 1), C("subsurface_color", 0.8f, 0.5f, 0.3f));
            Vector3f o = CombineTests.Direction(0.5f, 0.3f);
            double a, e;
            CombineTests.Albedo(sss, o, 60000, ref rng, out a, out e);
            double sum = 0, sq = 0;
            int n = 60000;
            for (int i = 0; i < n; ++i)
            {
                Vector3f w = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                double v = sss.f(o, w, TransportMode.Radiance)[0] * Math.PI;
                sum += v;
                sq += v * v;
            }
            double error = Math.Sqrt((sq / n - sum * sum / n / n) / n);
            Console.WriteLine("MEASURE openpbr subsurface: albedo by samples {0:F4} +- {1:F4}, by evaluation {2:F4} +- {3:F4}", a, e, sum / n, error);
            EXPECT_LT(Math.Abs(a - sum / n), 4 * Math.Sqrt(e * e + error * error));
            foreach (Float grey in new Float[] { 0.2f, 0.5f, 0.8f })
            {
                BxDF bare = Build(N("subsurface_weight", 1), C("subsurface_color", grey, grey, grey), N("specular_weight", 0), I("maxdepth", 2000));
                double mean = 0;
                foreach (Float cosine in new Float[] { 0.9f, 0.7f, 0.5f, 0.3f, 0.1f })
                {
                    double b, eb;
                    CombineTests.Albedo(bare, CombineTests.Direction(cosine, 1.3f), 20000, ref rng, out b, out eb);
                    // (the mean over the hemisphere with the cosine: the five cosines are its midpoints in mu^2)
                    mean += b * cosine * 0.2 * 2;
                }
                Console.WriteLine("MEASURE openpbr subsurface_color {0} without an interface: albedo of the surface under an even light {1:F4}", grey, mean);
                EXPECT_LT(Math.Abs(mean - grey), 0.02);
            }
        }

        // The checks that the paper proposes for a thin film (its appendix E): a perfectly reflecting metal under a film loses
        // nothing, whatever the film; and the film under a coat is not the film in air
        [TEST("OpenPBR", "ThinFilm")]
        public static void ThinFilm()
        {
            RNG rng = new RNG(8);
            foreach (Float thickness in new Float[] { 0.1f, 0.4f, 1.5f })
            {
                BxDF mirror = Build(C("metal_eta", 1.8f, 1.8f, 1.8f), C("metal_k", 1000, 1000, 1000), N("base_metalness", 1), N("specular_roughness", 0.5f),
                                    T("metal_lobe", "multiscatter"), I("scatterdepth", 64), N("thin_film_weight", 1), N("thin_film_thickness", thickness),
                                    N("thin_film_ior", 1.4f));
                foreach (Float cosine in new Float[] { 0.9f, 0.3f })
                {
                    double a, e;
                    CombineTests.Albedo(mirror, CombineTests.Direction(cosine, 0.6f), 40000, ref rng, out a, out e);
                    Console.WriteLine("MEASURE openpbr thin film of {0} micrometres on a perfect metal, cosine {1}: albedo {2:F4} +- {3:F4}", thickness, cosine, a, e);
                    EXPECT_GT(a, 0.995 - 4 * e);
                    EXPECT_LT(a, 1.001 + 4 * e);
                }
            }
            BxDF bare = Build(N("base_metalness", 1), N("thin_film_weight", 1), N("thin_film_thickness", 0.4f), C("base_color", 0.9f, 0.9f, 0.9f));
            BxDF coated = Build(N("base_metalness", 1), N("thin_film_weight", 1), N("thin_film_thickness", 0.4f), C("base_color", 0.9f, 0.9f, 0.9f), N("coat_weight", 1));
            double x, ex, y, ey;
            CombineTests.Albedo(bare, CombineTests.Direction(0.8f, 0.6f), 100000, ref rng, out x, out ex);
            CombineTests.Albedo(coated, CombineTests.Direction(0.8f, 0.6f), 100000, ref rng, out y, out ey);
            Console.WriteLine("MEASURE openpbr thin film on a metal, first wavelength: albedo {0:F4} in air, {1:F4} under a coat", x, y);
        }

        [TEST("OpenPBR", "CoatColour")]
        public static void CoatColour()
        {
            // No interfaces (indices of 1), a white Lambertian base: seen and lit along the normal, the base has the colour of
            // the coat
            RNG rng = new RNG(7);
            BxDF model = Build(C("base_color", 1, 1, 1), N("specular_weight", 0), N("coat_weight", 1), N("coat_ior", 1), C("coat_color", 0.5f, 0.5f, 0.5f));
            Vector3f normal = new Vector3f(0, 0, 1);
            double sum = 0;
            int count = 20000;
            for (int s = 0; s < count; ++s)
            {
                Vector3f d = 1e-4f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0);
                sum += model.f(Normalize(normal + d), Normalize(normal - d), TransportMode.Radiance)[0];
            }
            Console.WriteLine("MEASURE openpbr coat_color 0.5 on a white base, along the normal: pi f = {0:F4}", Math.PI * sum / count);
            EXPECT_LT(Math.Abs(Math.PI * sum / count - 0.5), 0.01);
        }

        [TEST("OpenPBR", "Polarised")]
        public static void Polarised()
        {
            FacetForgePolarisedTests.SampledAgainstEvaluated(Build(), "openpbr, defaults", 200000);
            FacetForgePolarisedTests.SampledAgainstEvaluated(Build(N("base_metalness", 1), N("coat_weight", 1), N("coat_roughness", 0.1f)), "openpbr, coated metal", 200000);
            FacetForgePolarisedTests.SampledAgainstEvaluated(Build(N("base_metalness", 0.5f), T("metal_lobe", "multiscatter"), N("specular_roughness", 0.5f),
                                                                   N("fuzz_weight", 0.5f)), "openpbr, half metal with every reflection, half fuzz", 200000);
        }
    }
}
