// (not in pbrt-v4) lobes_test.cs
//
// The lobes of lobes.cs and the materials built on combine.cs beside OpenPBR:
//   - GeneralizedSchlickBxDF: with white colours it is a perfect mirror microsurface, with the curve of an index and the
//     reflectance of that index it is the reflection of the dielectric of pbrt; reciprocal, energy conserving;
//   - VelvetBxDF: reciprocal, and an albedo below one for every roughness;
//   - MaxwellMaterial: its BSDFs and the three ways its layers lie on each other, and the medium between stacked layers;
//   - the MaterialX reader: the surface shaders as the OpenPBR material with the same inputs, and a graph of BSDF nodes as
//     the lobes it stands for.

using System;
using System.Collections.Generic;
using System.IO;
using testing;

namespace pbrt
{
    public static unsafe class LobesTests
    {
        static SampledSpectrum S(Float v) { return new SampledSpectrum(v); }

        [TEST("Lobes", "GeneralizedSchlick")]
        public static void GeneralizedSchlick()
        {
            RNG rng = new RNG(1);
            MicrofacetDistribution d = new TrowbridgeReitzDistribution(0.3f, 0.3f);
            BxDF white = new GeneralizedSchlickBxDF(d, S(1f), S(1f), 5, 0);
            BxDF mirror = new ConductorBxDF(d, S(1f), S(1e4f));
            BxDF curve = new GeneralizedSchlickBxDF(d, S(FrDielectric(1, 1.5f)), S(1f), 5, 1.5f);
            BxDF glass = new DielectricBxDF(1.5f, d);
            double worstWhite = 0, worstCurve = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f wo = FacetForgePolarisedTests.Upper(ref rng), wi = FacetForgePolarisedTests.Upper(ref rng);
                worstWhite = Math.Max(worstWhite, Math.Abs(white.f(wo, wi, TransportMode.Radiance)[0] - mirror.f(wo, wi, TransportMode.Radiance)[0]) /
                                                  Math.Max(1e-3, mirror.f(wo, wi, TransportMode.Radiance)[0]));
                worstCurve = Math.Max(worstCurve, Math.Abs(curve.f(wo, wi, TransportMode.Radiance)[0] - glass.f(wo, wi, TransportMode.Radiance)[0]) /
                                                  Math.Max(1e-3, glass.f(wo, wi, TransportMode.Radiance)[0]));
            }
            Console.WriteLine("MEASURE generalized Schlick: white against a perfect conductor {0:E1}, the curve of an index against the dielectric of pbrt {1:E1} (relative)",
                              worstWhite, worstCurve);
            EXPECT_LT(worstWhite, 1e-3);
            EXPECT_LT(worstCurve, 1e-3);
            SampledSpectrum red = S(0.04f), edge = S(0.9f);
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.bothSides = false;
            BxDFCheck.Expect(BxDFCheck.Run(new GeneralizedSchlickBxDF(d, red, edge, 5, 0), "Generalized Schlick", options), "Generalized Schlick", 2e-3, 1.005, 5, 0.9);
            BxDFCheck.Expect(BxDFCheck.Run(new GeneralizedSchlickBxDF(new TrowbridgeReitzDistribution(0.5f, 0.2f), S(0.5f), S(1f), 5, 1.8f),
                                           "Generalized Schlick, anisotropic, the curve of an index", options), "Generalized Schlick, anisotropic", 2e-3, 1.005, 5, 0.85);
        }

        [TEST("Lobes", "Velvet")]
        public static void Velvet()
        {
            RNG rng = new RNG(2);
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.bothSides = false;
            BxDFCheck.Expect(BxDFCheck.Run(new VelvetBxDF(S(1f), 0.4f), "Velvet", options), "Velvet", 2e-3, 1.005, 5, 0.99);
            double largest = 0;
            foreach (Float alpha in new Float[] { 0.05f, 0.1f, 0.2f, 0.4f, 0.7f, 1f })
            {
                foreach (Float cosine in new Float[] { 0.99f, 0.8f, 0.5f, 0.2f, 0.05f })
                {
                    double a, e;
                    CombineTests.Albedo(new VelvetBxDF(S(1f), alpha), CombineTests.Direction(cosine, 0.3f), 40000, ref rng, out a, out e);
                    largest = Math.Max(largest, a);
                }
            }
            Console.WriteLine("MEASURE velvet: largest albedo over roughnesses 0.05 to 1 and cosines 0.05 to 0.99: {0:F4}", largest);
            EXPECT_LT(largest, 1.0);
        }

        static BxDF Maxwell(params ParsedParameter[] list)
        {
            TextureParameterDictionary d = new TextureParameterDictionary(new ParameterDictionary(new List<ParsedParameter>(list), RGBColorSpace.sRGB), new NamedTextures());
            MaxwellMaterial material = MaxwellMaterial.Create(d, null, new FileLoc(), new Allocator());
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            return material.GetBxDF(new UniversalTextureEvaluator(), new MaterialEvalContext(), ref lambda);
        }

        [TEST("Lobes", "MaxwellLayers")]
        public static void MaxwellLayers()
        {
            RNG rng = new RNG(3);
            Vector3f wo = CombineTests.Direction(0.6f, 0.4f);
            // a Lambertian
            BxDF lambert = Maxwell(OpenPBRTests.N("layer0.bsdf0.roughness", 100), OpenPBRTests.C("layer0.bsdf0.reflectance0", 0.6f, 0.6f, 0.6f));
            double a, e;
            CombineTests.Albedo(lambert, wo, 20000, ref rng, out a, out e);
            EXPECT_LT(Math.Abs(a - 0.6), 1e-3);
            // a conductor by its index
            BxDF metal = Maxwell(OpenPBRTests.N("layer0.bsdf0.roughness", 40), OpenPBRTests.C("layer0.bsdf0.eta", 0.2f, 0.2f, 0.2f), OpenPBRTests.C("layer0.bsdf0.k", 3.5f, 3.5f, 3.5f));
            BxDF reference = new ConductorBxDF(new TrowbridgeReitzDistribution(0.16f, 0.16f), S(0.2f), S(3.5f));
            Vector3f wi = CombineTests.Direction(0.7f, 2.9f);
            EXPECT_LT(Math.Abs(metal.f(wo, wi, TransportMode.Radiance)[0] - reference.f(wo, wi, TransportMode.Radiance)[0]), 1e-3 * reference.f(wo, wi, TransportMode.Radiance)[0]);
            // a layer that covers 30 % of the one below
            BxDF two = Maxwell(OpenPBRTests.N("layer0.bsdf0.roughness", 100), OpenPBRTests.C("layer0.bsdf0.reflectance0", 0.6f, 0.6f, 0.6f),
                               OpenPBRTests.N("layer1.bsdf0.roughness", 40), OpenPBRTests.C("layer1.bsdf0.eta", 0.2f, 0.2f, 0.2f), OpenPBRTests.C("layer1.bsdf0.k", 3.5f, 3.5f, 3.5f),
                               OpenPBRTests.N("layer1.opacity", 0.3f));
            double expected = 0.7 * lambert.f(wo, wi, TransportMode.Radiance)[0] + 0.3 * reference.f(wo, wi, TransportMode.Radiance)[0];
            EXPECT_LT(Math.Abs(two.f(wo, wi, TransportMode.Radiance)[0] - expected), 1e-5);
            // the same, added
            BxDF added = Maxwell(OpenPBRTests.N("layer0.bsdf0.roughness", 100), OpenPBRTests.C("layer0.bsdf0.reflectance0", 0.6f, 0.6f, 0.6f),
                                 OpenPBRTests.N("layer1.bsdf0.roughness", 40), OpenPBRTests.C("layer1.bsdf0.eta", 0.2f, 0.2f, 0.2f), OpenPBRTests.C("layer1.bsdf0.k", 3.5f, 3.5f, 3.5f),
                                 OpenPBRTests.N("layer1.opacity", 0.3f), OpenPBRTests.T("layer1.blend", "additive"));
            expected = lambert.f(wo, wi, TransportMode.Radiance)[0] + 0.3 * reference.f(wo, wi, TransportMode.Radiance)[0];
            EXPECT_LT(Math.Abs(added.f(wo, wi, TransportMode.Radiance)[0] - expected), 1e-5);
            // a clear layer stacked on a white Lambertian with an absorbing medium between: Beer's law
            BxDF stacked = Maxwell(OpenPBRTests.N("layer0.bsdf0.roughness", 100), OpenPBRTests.C("layer0.bsdf0.reflectance0", 1, 1, 1),
                                   OpenPBRTests.T("layer1.bsdf0.type", "dielectric"), OpenPBRTests.N("layer1.bsdf0.nd", 1), OpenPBRTests.T("layer1.blend", "stack"),
                                   OpenPBRTests.C("layer1.medium.absorption", 0.5f, 0.5f, 0.5f));
            double depth = -Math.Log(0.5), e3 = 0;
            for (int i = 0; i < 200000; ++i)
            {
                double mu = (i + 0.5) / 200000;
                e3 += 2 * Math.Exp(-depth / mu) * mu / 200000;
            }
            CombineTests.Albedo(stacked, wo, 200000, ref rng, out a, out e);
            Console.WriteLine("MEASURE maxwell, absorbing medium under a clear stacked layer: albedo {0:F4} +- {1:F4}, Beer's law {2:F4}", a, e, Math.Exp(-depth / 0.6) * e3);
            EXPECT_LT(Math.Abs(a - Math.Exp(-depth / 0.6) * e3), 4 * e + 2e-3);
            // a scattering medium between a clear layer and a black base, with one scattering and with all
            ParsedParameter[] slab = new ParsedParameter[] {
                OpenPBRTests.N("layer0.bsdf0.roughness", 100), OpenPBRTests.C("layer0.bsdf0.reflectance0", 0, 0, 0), OpenPBRTests.T("layer1.bsdf0.type", "dielectric"),
                OpenPBRTests.N("layer1.bsdf0.nd", 1), OpenPBRTests.T("layer1.blend", "stack"), OpenPBRTests.N("layer1.medium.thickness", 1),
                OpenPBRTests.C("layer1.medium.albedo", 0.9f, 0.9f, 0.9f), OpenPBRTests.I("maxdepth", 64), null };
            slab[slab.Length - 1] = OpenPBRTests.I("layer1.medium.scatterings", 1);
            BxDF once = Maxwell(slab);
            slab[slab.Length - 1] = OpenPBRTests.I("layer1.medium.scatterings", 0);
            BxDF every = Maxwell(slab);
            double b, eb;
            CombineTests.Albedo(once, wo, 200000, ref rng, out a, out e);
            CombineTests.Albedo(every, wo, 200000, ref rng, out b, out eb);
            double single = 0;
            for (int i = 0; i < 200000; ++i)
            {
                double mu = (i + 0.5) / 200000;
                single += 0.45 * mu / (0.6 + mu) * (1 - Math.Exp(-(1 / 0.6 + 1 / mu))) / 200000;
            }
            Console.WriteLine("MEASURE maxwell, scattering medium between layers: one scattering {0:F4} +- {1:F4} (Chandrasekhar {2:F4}), every scattering {3:F4}", a, e,
                              single, b);
            // (the albedo 0.9 as a colour is 0.9 to the precision of the RGB to spectrum conversion)
            EXPECT_LT(Math.Abs(a - single), 4 * e + 3e-3);
            EXPECT_GT(b, a + 0.02);
            // three layers of several BSDFs: valid, and samples and evaluation agree
            BxDF full = Maxwell(OpenPBRTests.N("layer0.bsdf0.roughness", 100), OpenPBRTests.C("layer0.bsdf0.reflectance0", 0.7f, 0.2f, 0.1f),
                                OpenPBRTests.N("layer0.bsdf1.roughness", 30), OpenPBRTests.C("layer0.bsdf1.reflectance0", 0.05f, 0.05f, 0.05f), OpenPBRTests.N("layer0.bsdf1.weight", 0.5f),
                                OpenPBRTests.T("layer1.bsdf0.type", "dielectric"), OpenPBRTests.N("layer1.bsdf0.roughness", 20), OpenPBRTests.T("layer1.blend", "stack"),
                                OpenPBRTests.C("layer1.medium.absorption", 0.9f, 0.8f, 0.5f), OpenPBRTests.T("layer2.bsdf0.type", "fuzz"),
                                OpenPBRTests.N("layer2.bsdf0.roughness", 50), OpenPBRTests.T("layer2.blend", "stack"), OpenPBRTests.N("layer2.opacity", 0.5f));
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.cosines = new Float[] { 0.9f, 0.5f, 0.2f };
            options.samples = 30000;
            options.pairs = 200;
            options.repeats = 8;
            options.bothSides = false;
            BxDFCheckResult result = BxDFCheck.Run(full, "Maxwell, three layers", options);
            EXPECT_EQ(0, result.invalid);
            EXPECT_LT(result.albedoMax, 1.005);
            EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 1.5e-2, "Sample_f and f disagree about the albedo");
        }

        static Material MaterialX(string document, string material, out string path)
        {
            path = Path.Combine(Path.GetTempPath(), "magi_materialx_" + Guid.NewGuid().ToString("N") + ".mtlx");
            File.WriteAllText(path, document);
            List<ParsedParameter> list = new List<ParsedParameter>(new ParsedParameter[] { OpenPBRTests.T("filename", path) });
            if (material != "")
            {
                list.Add(OpenPBRTests.T("material", material));
            }
            TextureParameterDictionary d = new TextureParameterDictionary(new ParameterDictionary(list, RGBColorSpace.sRGB), new NamedTextures());
            return MaterialXMaterial.Create(d, null, new FileLoc(), new Allocator());
        }

        static BxDF Lobe(Material material)
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            if (material is OpenPBRMaterial open)
            {
                return open.GetBxDF(new UniversalTextureEvaluator(), new MaterialEvalContext(), ref lambda);
            }
            return ((MaterialXMaterial)material).GetBxDF(new UniversalTextureEvaluator(), new MaterialEvalContext(), ref lambda);
        }

        [TEST("Lobes", "MaterialX")]
        public static void MaterialXDocuments()
        {
            RNG rng = new RNG(4);
            Vector3f wo = CombineTests.Direction(0.6f, 0.4f), wi = CombineTests.Direction(0.7f, 2.9f);
            string path;
            // a standard surface, its colour from a graph: a metal with a coat
            Material standard = MaterialX(@"<?xml version=""1.0""?>
<materialx version=""1.38"">
  <nodegraph name=""NG"">
    <constant name=""tint"" type=""color3""><input name=""value"" type=""color3"" value=""0.9, 0.6, 0.3""/></constant>
    <output name=""out"" type=""color3"" nodename=""tint""/>
  </nodegraph>
  <standard_surface name=""SR_other"" type=""surfaceshader""><input name=""base_color"" type=""color3"" value=""0, 0, 1""/></standard_surface>
  <standard_surface name=""SR_metal"" type=""surfaceshader"">
    <input name=""base"" type=""float"" value=""0.9""/>
    <input name=""base_color"" type=""color3"" nodegraph=""NG"" output=""out""/>
    <input name=""metalness"" type=""float"" value=""1""/>
    <input name=""specular_roughness"" type=""float"" value=""0.4""/>
    <input name=""coat"" type=""float"" value=""0.5""/>
    <input name=""coat_color"" type=""color3"" value=""0.8, 0.9, 1""/>
    <input name=""thin_film_thickness"" type=""float"" value=""300""/>
  </standard_surface>
  <surfacematerial name=""Other"" type=""material""><input name=""surfaceshader"" type=""surfaceshader"" nodename=""SR_other""/></surfacematerial>
  <surfacematerial name=""Metal"" type=""material""><input name=""surfaceshader"" type=""surfaceshader"" nodename=""SR_metal""/></surfacematerial>
</materialx>", "Metal", out path);
            File.Delete(path);
            EXPECT_TRUE(standard is OpenPBRMaterial);
            BxDF fromDocument = Lobe(standard);
            BxDF direct = OpenPBRTests.Build(OpenPBRTests.N("base_weight", 0.9f), OpenPBRTests.C("base_color", 0.9f, 0.6f, 0.3f), OpenPBRTests.N("base_metalness", 1),
                                             OpenPBRTests.N("specular_roughness", 0.4f), OpenPBRTests.N("coat_weight", 0.5f), OpenPBRTests.C("coat_color", 0.8f, 0.9f, 1),
                                             OpenPBRTests.N("thin_film_thickness", 0.3f), OpenPBRTests.N("thin_film_weight", 1));
            double a, e, b, eb;
            CombineTests.Albedo(fromDocument, wo, 100000, ref rng, out a, out e);
            CombineTests.Albedo(direct, wo, 100000, ref rng, out b, out eb);
            Console.WriteLine("MEASURE materialx standard_surface against the same OpenPBR material: albedo {0:F4} / {1:F4} (+- {2:F4})", a, b, Math.Sqrt(e * e + eb * eb));
            EXPECT_LT(Math.Abs(a - b), 4 * Math.Sqrt(e * e + eb * eb));
            // glTF and USD preview surfaces
            Material gltf = MaterialX(@"<materialx version=""1.39""><gltf_pbr name=""G"" type=""surfaceshader"">
  <input name=""base_color"" type=""color3"" value=""0.2, 0.5, 0.8""/><input name=""roughness"" type=""float"" value=""0.5""/>
  <input name=""clearcoat"" type=""float"" value=""1""/></gltf_pbr></materialx>", "", out path);
            File.Delete(path);
            CombineTests.Albedo(Lobe(gltf), wo, 100000, ref rng, out a, out e);
            CombineTests.Albedo(OpenPBRTests.Build(OpenPBRTests.C("base_color", 0.2f, 0.5f, 0.8f), OpenPBRTests.N("specular_roughness", 0.5f), OpenPBRTests.N("coat_weight", 1),
                                                   OpenPBRTests.N("coat_ior", 1.5f)), wo, 100000, ref rng, out b, out eb);
            Console.WriteLine("MEASURE materialx gltf_pbr against the same OpenPBR material: albedo {0:F4} / {1:F4} (+- {2:F4})", a, b, Math.Sqrt(e * e + eb * eb));
            EXPECT_LT(Math.Abs(a - b), 4 * Math.Sqrt(e * e + eb * eb));
            // a graph of BSDF nodes: half a conductor, half a dielectric layered on a diffuse base
            Material graph = MaterialX(@"<materialx version=""1.39"">
  <oren_nayar_diffuse_bsdf name=""diffuse"" type=""BSDF""><input name=""color"" type=""color3"" value=""0.7, 0.7, 0.7""/></oren_nayar_diffuse_bsdf>
  <dielectric_bsdf name=""gloss"" type=""BSDF""><input name=""ior"" type=""float"" value=""1.5""/><input name=""roughness"" type=""vector2"" value=""0.2, 0.2""/></dielectric_bsdf>
  <layer name=""plastic"" type=""BSDF""><input name=""top"" type=""BSDF"" nodename=""gloss""/><input name=""base"" type=""BSDF"" nodename=""diffuse""/></layer>
  <conductor_bsdf name=""metal"" type=""BSDF""><input name=""ior"" type=""color3"" value=""0.2, 0.2, 0.2""/><input name=""extinction"" type=""color3"" value=""3.5, 3.5, 3.5""/>
    <input name=""roughness"" type=""vector2"" value=""0.3, 0.3""/></conductor_bsdf>
  <mix name=""both"" type=""BSDF""><input name=""fg"" type=""BSDF"" nodename=""metal""/><input name=""bg"" type=""BSDF"" nodename=""plastic""/><input name=""mix"" type=""float"" value=""0.5""/></mix>
  <surface name=""S"" type=""surfaceshader""><input name=""bsdf"" type=""BSDF"" nodename=""both""/></surface>
  <surfacematerial name=""M"" type=""material""><input name=""surfaceshader"" type=""surfaceshader"" nodename=""S""/></surfacematerial>
</materialx>", "", out path);
            File.Delete(path);
            EXPECT_TRUE(graph is MaterialXMaterial);
            BxDF nodes = Lobe(graph);
            // (0.7 and the indices as colours go through the RGB to spectrum conversion: the reference takes them from the lobes)
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            SampledSpectrum grey = new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(0.7f, 0.7f, 0.7f)).Sample(lambda);
            SampledSpectrum eta = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(0.2f, 0.2f, 0.2f)).Sample(lambda);
            SampledSpectrum k = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(3.5f, 3.5f, 3.5f)).Sample(lambda);
            BxDF plastic = StackBxDF.Absorbing(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.2f, 0.2f)), new EonDiffuseBxDF(grey, 0), S(0f), 32, 1, true);
            BxDF reference = MixBxDF.Of(plastic, new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), eta, k), 0.5f);
            CombineTests.Albedo(nodes, wo, 200000, ref rng, out a, out e);
            CombineTests.Albedo(reference, wo, 200000, ref rng, out b, out eb);
            Console.WriteLine("MEASURE materialx graph (mix of a conductor and a dielectric layered on a diffuse) against the lobes: albedo {0:F4} / {1:F4} (+- {2:F4})", a, b,
                              Math.Sqrt(e * e + eb * eb));
            EXPECT_LT(Math.Abs(a - b), 4 * Math.Sqrt(e * e + eb * eb));
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.cosines = new Float[] { 0.9f, 0.5f, 0.2f };
            options.samples = 30000;
            options.pairs = 200;
            options.repeats = 8;
            options.bothSides = false;
            BxDFCheckResult result = BxDFCheck.Run(nodes, "MaterialX graph", options);
            EXPECT_EQ(0, result.invalid);
            EXPECT_LT(result.albedoMax, 1.005);
            EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 1.5e-2, "Sample_f and f disagree about the albedo");
        }

        static TextureEvalContext At(Float u, Float v)
        {
            return new TextureEvalContext(new Point3f(u, v, 0), default, default, new Normal3f(0, 0, 1), new Point2f(u, v), 0, 0, 0, 0, 0, 0);
        }

        // The pattern nodes of the reader: an image, arithmetic that folds to a constant, a node defined by the document, the
        // colour space of a value, channels, and a procedural that makes a texture
        [TEST("Lobes", "MaterialXPatterns")]
        public static void MaterialXPatterns()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            string directory = Path.Combine(Path.GetTempPath(), "magi_materialx_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            // a 2 x 2 image: red, green on the top row; blue, white on the bottom row
            Image image = new Image(PixelFormat.Float, new Point2i(2, 2), new string[] { "R", "G", "B" });
            Float[][] pixels = new Float[][] { new Float[] { 0.8f, 0.1f, 0.1f }, new Float[] { 0.1f, 0.8f, 0.1f }, new Float[] { 0.1f, 0.1f, 0.8f }, new Float[] { 0.9f, 0.9f, 0.9f } };
            for (int i = 0; i < 4; ++i)
            {
                for (int c = 0; c < 3; ++c)
                {
                    image.SetChannel(new Point2i(i % 2, i / 2), c, pixels[i][c]);
                }
            }
            EXPECT_TRUE(image.Write(Path.Combine(directory, "four.exr")));
            string document = @"<materialx version=""1.39"" colorspace=""lin_rec709"">
  <nodedef name=""ND_halved"" node=""halved""><input name=""in"" type=""color3"" value=""1, 1, 1""/><input name=""amount"" type=""float"" value=""0.5""/>
    <output name=""out"" type=""color3""/></nodedef>
  <nodegraph name=""NG_halved"" nodedef=""ND_halved"">
    <multiply name=""m"" type=""color3""><input name=""in1"" type=""color3"" interfacename=""in""/><input name=""in2"" type=""float"" interfacename=""amount""/></multiply>
    <output name=""out"" type=""color3"" nodename=""m""/>
  </nodegraph>
  <nodegraph name=""NG"">
    <tiledimage name=""img"" type=""color3""><input name=""file"" type=""filename"" value=""four.exr""/></tiledimage>
    <halved name=""dim"" type=""color3""><input name=""in"" type=""color3"" nodename=""img""/></halved>
    <output name=""colour"" type=""color3"" nodename=""dim""/>
    <constant name=""a"" type=""color3""><input name=""value"" type=""color3"" value=""0.5, 0.5, 0.5"" colorspace=""srgb_texture""/></constant>
    <separate3 name=""parts"" type=""multioutput""><input name=""in"" type=""color3"" nodename=""a""/></separate3>
    <add name=""sum"" type=""float""><input name=""in1"" type=""float"" nodename=""parts"" output=""outr""/><input name=""in2"" type=""float"" value=""0.1""/></add>
    <output name=""rough"" type=""float"" nodename=""sum""/>
    <noise2d name=""n"" type=""float""><input name=""amplitude"" type=""float"" value=""0.5""/><input name=""pivot"" type=""float"" value=""0.5""/></noise2d>
    <output name=""metal"" type=""float"" nodename=""n""/>
  </nodegraph>
  <open_pbr_surface name=""S"" type=""surfaceshader"">
    <input name=""base_color"" type=""color3"" nodegraph=""NG"" output=""colour""/>
    <input name=""specular_roughness"" type=""float"" nodegraph=""NG"" output=""rough""/>
    <input name=""base_metalness"" type=""float"" nodegraph=""NG"" output=""metal""/>
  </open_pbr_surface>
</materialx>";
            string path = Path.Combine(directory, "patterns.mtlx");
            File.WriteAllText(path, document);
            TextureParameterDictionary d = new TextureParameterDictionary(new ParameterDictionary(new List<ParsedParameter>(new ParsedParameter[] { OpenPBRTests.T("filename", path) }),
                                                                                                    RGBColorSpace.sRGB), new NamedTextures());
            OpenPBRMaterial material = (OpenPBRMaterial)MaterialXMaterial.Create(d, null, new FileLoc(), new Allocator());
            Directory.Delete(directory, true);
            // the image through the node of the document: half of each pixel (v = 0 is the bottom row of the image)
            Float[][] where = new Float[][] { new Float[] { 0.25f, 0.75f }, new Float[] { 0.75f, 0.75f }, new Float[] { 0.25f, 0.25f }, new Float[] { 0.75f, 0.25f } };
            double worst = 0;
            for (int i = 0; i < 4; ++i)
            {
                SampledSpectrum got = material.baseColor.Evaluate(At(where[i][0], where[i][1]), lambda);
                SampledSpectrum expected = new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(0.5f * pixels[i][0], 0.5f * pixels[i][1], 0.5f * pixels[i][2])).Sample(lambda);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    worst = Math.Max(worst, Math.Abs(got[c] - expected[c]));
                }
            }
            // sRGB 0.5 is 0.2140 linear; plus 0.1: a constant, folded when the document was read
            Float rough = material.specularRoughness.Evaluate(At(0.3f, 0.3f));
            // the noise is a texture: it changes over the surface, around 0.5
            Float n1 = material.baseMetalness.Evaluate(At(0.13f, 0.37f)), n2 = material.baseMetalness.Evaluate(At(2.71f, 5.19f));
            Console.WriteLine("MEASURE materialx patterns: image through a node of the document, worst {0:E1}; sRGB 0.5 + 0.1 = {1:F4}; noise at two points {2:F3}, {3:F3}", worst,
                              rough, n1, n2);
            EXPECT_LT(worst, 1e-4);
            EXPECT_LT(Math.Abs(rough - 0.31404), 1e-4);
            EXPECT_TRUE(material.specularRoughness is FloatConstantTexture, "a constant graph is folded");
            EXPECT_TRUE(material.baseMetalness is MtlxFloatTexture);
            EXPECT_NE(n1, n2);
            EXPECT_LT(Math.Abs(n1 - 0.5f), 0.5f);
        }
    }
}
