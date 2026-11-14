// (not in pbrt-v4) spectra_parametric_test.cs
//
// The spectra of spectra_parametric.cs and their textures (textures_parametric.cs):
//   - the formulas: a glass by its Sellmeier coefficients has its catalogue index, the Cauchy and Sellmeier spectra are those of
//     the realistic camera, a Gaussian has its peak and its width, the filters have their two levels, regular data are
//     interpolated between even steps;
//   - MaxValue() is not exceeded over the wavelengths;
//   - the parameter types ("gaussian name" [...] and the others) give the spectra, as a spectrum and as a spectrum texture;
//   - the index of refraction of a material from "sellmeier", "cauchy", "cauchyb", a float or a spectrum, in that order;
//   - a texture with constant parameters is its spectrum, and a texture is never above its MaxValue().
// The derivatives of the textures are tested with the others (TextureDerivatives.ParametricSpectra).

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class ParametricSpectraTests
    {
        static ParsedParameter Parameter(string type, string name, params Float[] values)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = type;
            p.name = name;
            foreach (Float v in values)
            {
                p.AddFloat(v);
            }
            return p;
        }

        static TextureParameterDictionary Dictionary(params ParsedParameter[] parameters)
        {
            return new TextureParameterDictionary(new ParameterDictionary(new List<ParsedParameter>(parameters), RGBColorSpace.sRGB), new NamedTextures());
        }

        static void ExpectBounded(Spectrum s, string name)
        {
            Float bound = s.MaxValue();
            for (Float lambda = Lambda_min; lambda <= Lambda_max; lambda += 0.25f)
            {
                EXPECT_LE(s.Evaluate(lambda), bound * (1 + 1e-6f) + 1e-7f, name);
            }
        }

        [TEST("ParametricSpectra", "Formulas")]
        public static void Formulas()
        {
            // N-BK7: n_d = 1.5168, n_F - n_C = 0.008054 (the catalogue of the glass)
            SellmeierSpectrum bk7 = new SellmeierSpectrum(1.03961212f, 0.231792344f, 1.01046945f, 0.00600069867f, 0.0200179144f, 103.560653f);
            EXPECT_LT(Math.Abs(bk7.Evaluate(587.5618f) - 1.5168), 2e-5);
            EXPECT_LT(Math.Abs((bk7.Evaluate(486.1327f) - bk7.Evaluate(656.2725f)) - 0.008054), 2e-5);
            CauchySpectrum cauchy = new CauchySpectrum(1.5046f, 0.0042f, 0.00001f);
            double worst = 0;
            for (Float lambda = 380; lambda <= 780; lambda += 7)
            {
                double fromCamera = RealisticCamera.SellmeierIndex(new double[] { 1.03961212, 0.231792344, 1.01046945, 0.00600069867, 0.0200179144, 103.560653 },
                                                                   lambda * 1e-3);
                worst = Math.Max(worst, Math.Abs(fromCamera - bk7.Evaluate(lambda)));
                worst = Math.Max(worst, Math.Abs(RealisticCamera.CauchyIndex(new double[] { 1.5046, 0.0042, 0.00001 }, lambda * 1e-3) - cauchy.Evaluate(lambda)));
            }
            EXPECT_LT(worst, 2e-6);

            GaussianSpectrum gaussian = new GaussianSpectrum(550, 20, 3, 0);
            EXPECT_LT(Math.Abs(gaussian.Evaluate(550) - 3), 1e-5);
            EXPECT_LT(Math.Abs(gaussian.Evaluate(570) - 3 * Math.Exp(-0.5)) / 3, 2e-3);
            EXPECT_LT(Math.Abs(gaussian.Evaluate(530) - gaussian.Evaluate(570)), 1e-5);
            GaussianSpectrum stop = new GaussianSpectrum(550, 20, -0.8f, 1);
            EXPECT_LT(Math.Abs(stop.Evaluate(550) - 0.2), 1e-5);
            EXPECT_LT(Math.Abs(stop.Evaluate(750) - 1), 1e-5);

            FrequencySpectrum frequency = new FrequencySpectrum(0.05f, 0.3f, 0.9f);
            Float least = Infinity, most = 0;
            for (Float lambda = Lambda_min; lambda <= Lambda_max; lambda += 0.5f)
            {
                least = std.min(least, frequency.Evaluate(lambda));
                most = std.max(most, frequency.Evaluate(lambda));
            }
            EXPECT_LT(least, 0.01f);
            EXPECT_GT(most, 0.89f);
            EXPECT_LE(most, 0.9f + 1e-6f);
            EXPECT_LT(Math.Abs(frequency.Evaluate(500) - 0.45 * (Math.Sin(0.05 * 500 + 0.3) + 1)), 1e-4);

            EdgeSpectrum edge = new EdgeSpectrum(600, 20, 0.1f, 0.9f);
            EXPECT_LT(Math.Abs(edge.Evaluate(600) - 0.5), 1e-4);
            EXPECT_LT(Math.Abs(edge.Evaluate(400) - 0.1), 1e-4);
            EXPECT_LT(Math.Abs(edge.Evaluate(780) - 0.9), 1e-4);
            // (from 12% to 88% of the step over the softness)
            EXPECT_LT(Math.Abs(edge.Evaluate(610) - (0.1 + 0.8 * 0.8808)), 2e-3);

            Spectrum regular = ParametricSpectra.Create("regulardata", "x", Parameter("regulardata", "x", 400, 700, 1, 3, 2, 6).floats, new FileLoc(),
                                                        new Allocator());
            EXPECT_LT(Math.Abs(regular.Evaluate(400) - 1), 1e-5);
            EXPECT_LT(Math.Abs(regular.Evaluate(500) - 3), 1e-5);
            EXPECT_LT(Math.Abs(regular.Evaluate(550) - 2.5), 1e-5);
            EXPECT_LT(Math.Abs(regular.Evaluate(700) - 6), 1e-5);

            ExpectBounded(bk7, "sellmeier");
            ExpectBounded(cauchy, "cauchy");
            ExpectBounded(gaussian, "gaussian");
            ExpectBounded(stop, "band stop");
            ExpectBounded(frequency, "frequency");
            ExpectBounded(edge, "edge");
            ExpectBounded(new EdgeSpectrum(600, 20, 0.9f, 0.1f), "short pass");
            ExpectBounded(regular, "regular data");
        }

        [TEST("ParametricSpectra", "ParameterTypes")]
        public static void ParameterTypes()
        {
            Allocator alloc = new Allocator();
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.41f);
            TextureParameterDictionary d = Dictionary(Parameter("gaussian", "L", 620, 15, 2), Parameter("bandstop", "filter", 500, 25), Parameter("frequency", "f", 0.04f, 1),
                                                      Parameter("equalenergy", "white", 0.7f), Parameter("longpass", "red", 600, 10),
                                                      Parameter("shortpass", "blue", 480, 10, 0.05f, 0.95f), Parameter("cauchy", "eta", 1.5f, 0.005f),
                                                      Parameter("sellmeier", "glass", 1.03961212f, 0.231792344f, 1.01046945f, 0.00600069867f, 0.0200179144f,
                                                                103.560653f),
                                                      Parameter("regulardata", "table", 400, 700, 0.2f, 0.8f));
            Spectrum L = d.GetOneSpectrum("L", null, SpectrumType.Illuminant, alloc);
            EXPECT_TRUE(L is GaussianSpectrum);
            EXPECT_LT(Math.Abs(L.Evaluate(620) - 2), 1e-5);
            EXPECT_LT(Math.Abs(d.GetOneSpectrum("filter", null, SpectrumType.Albedo, alloc).Evaluate(500)), 1e-5);
            EXPECT_LT(Math.Abs(d.GetOneSpectrum("f", null, SpectrumType.Albedo, alloc).Evaluate(500) - 0.5 * (Math.Sin(0.04 * 500 + 1) + 1)), 1e-4);
            EXPECT_TRUE(d.GetOneSpectrum("white", null, SpectrumType.Albedo, alloc) is ConstantSpectrum);
            EXPECT_LT(d.GetOneSpectrum("red", null, SpectrumType.Albedo, alloc).Evaluate(500), 1e-3f);
            EXPECT_GT(d.GetOneSpectrum("red", null, SpectrumType.Albedo, alloc).Evaluate(700), 0.999f);
            EXPECT_GT(d.GetOneSpectrum("blue", null, SpectrumType.Albedo, alloc).Evaluate(420), 0.94f);
            EXPECT_LT(d.GetOneSpectrum("blue", null, SpectrumType.Albedo, alloc).Evaluate(600), 0.06f);
            EXPECT_LT(Math.Abs(d.GetOneSpectrum("glass", null, SpectrumType.Unbounded, alloc).Evaluate(587.5618f) - 1.5168), 2e-5);
            EXPECT_LT(Math.Abs(d.GetOneSpectrum("table", null, SpectrumType.Albedo, alloc).Evaluate(550) - 0.5), 1e-5);

            // The same parameter where a texture is asked for: the spectrum, constant over the surface
            SpectrumTexture texture = d.GetSpectrumTextureOrNull("L", SpectrumType.Illuminant, alloc);
            EXPECT_TRUE(texture is SpectrumConstantTexture);
            SampledSpectrum value = texture.Evaluate(new TextureEvalContext(), lambda);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_LT(Math.Abs(value[i] - L.Evaluate(lambda[i])), 1e-6);
            }

            // The index of a material
            Spectrum byType = ParametricSpectra.Index(d, "eta", alloc);
            EXPECT_TRUE(byType is CauchySpectrum);
            Spectrum sellmeier = ParametricSpectra.Index(Dictionary(Parameter("float", "sellmeier", 1.03961212f, 0.231792344f, 1.01046945f, 0.00600069867f,
                                                                               0.0200179144f, 103.560653f), Parameter("float", "eta", 1.7f)), "eta", alloc);
            EXPECT_LT(Math.Abs(sellmeier.Evaluate(587.5618f) - 1.5168), 2e-5);
            Spectrum cauchy = ParametricSpectra.Index(Dictionary(Parameter("float", "cauchy", 1.5f, 0.004f, 0.0001f)), "eta", alloc);
            EXPECT_LT(Math.Abs(cauchy.Evaluate(500) - (1.5 + 0.004 / 0.25 + 0.0001 / 0.0625)), 1e-5);
            Spectrum lux = ParametricSpectra.Index(Dictionary(Parameter("float", "eta", 1.6f), Parameter("float", "cauchyb", 0.01f)), "eta", alloc);
            EXPECT_LT(Math.Abs(lux.Evaluate(500) - (1.6 + 0.01 / 0.25)), 1e-5);
            EXPECT_TRUE(ParametricSpectra.Index(Dictionary(Parameter("float", "eta", 1.33f)), "eta", alloc) is ConstantSpectrum);
            EXPECT_TRUE(ParametricSpectra.Index(Dictionary(Parameter("float", "roughness", 0.1f)), "eta", alloc) == null);
            // (a coat: the prefix of its index)
            Spectrum coat = ParametricSpectra.Index(Dictionary(Parameter("float", "coat.cauchy", 1.4f, 0.003f)), "coat.eta", alloc);
            EXPECT_TRUE(coat is CauchySpectrum);
        }

        [TEST("ParametricSpectra", "Textures")]
        public static void Textures()
        {
            RNG rng = new RNG(31);
            // Constant parameters: the spectrum
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.63f);
            TextureEvalContext ctx = new TextureEvalContext();
            SpectrumTexture[] textures = new SpectrumTexture[]
            {
                new SpectrumGaussianTexture(new FloatConstantTexture(560), new FloatConstantTexture(25), new FloatConstantTexture(1.5f), 0.1f),
                new SpectrumFrequencyTexture(new FloatConstantTexture(0.04f), new FloatConstantTexture(0.7f), new FloatConstantTexture(0.8f)),
                new SpectrumCauchyTexture(new FloatConstantTexture(1.5f), new FloatConstantTexture(0.005f), new FloatConstantTexture(0.0001f)),
                new SpectrumSellmeierTexture(new FloatConstantTexture(1.03961212f), new FloatConstantTexture(0.231792344f), new FloatConstantTexture(1.01046945f),
                                             new FloatConstantTexture(0.00600069867f), new FloatConstantTexture(0.0200179144f),
                                             new FloatConstantTexture(103.560653f)),
                new SpectrumEdgeTexture(new FloatConstantTexture(580), new FloatConstantTexture(30), new FloatConstantTexture(0.1f), new FloatConstantTexture(0.9f)),
            };
            Spectrum[] spectra = new Spectrum[]
            {
                new GaussianSpectrum(560, 25, 1.5f, 0.1f), new FrequencySpectrum(0.04f, 0.7f, 0.8f), new CauchySpectrum(1.5f, 0.005f, 0.0001f),
                new SellmeierSpectrum(1.03961212f, 0.231792344f, 1.01046945f, 0.00600069867f, 0.0200179144f, 103.560653f), new EdgeSpectrum(580, 30, 0.1f, 0.9f),
            };
            for (int t = 0; t < textures.Length; ++t)
            {
                SampledSpectrum value = textures[t].Evaluate(ctx, lambda);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    EXPECT_LT(Math.Abs(value[i] - spectra[t].Evaluate(lambda[i])), 1e-5, textures[t].ToString());
                }
                EXPECT_GE(textures[t].MaxValue() * (1 + 1e-5f), spectra[t].MaxValue() * (1 - 1e-5f), textures[t].ToString());
            }

            // Parameters that vary: never above the bound
            FloatTexture noise = new FBmTexture(new PointTransformMapping(pbrt.Scale(2, 2, 2)), 3, 0.5f);
            FloatTexture mean = new FloatAddTexture(new FloatConstantTexture(550), new FloatScaledTexture(noise, new FloatConstantTexture(120)));
            FloatTexture positive = new FloatAddTexture(new FloatConstantTexture(1.2f), new FloatScaledTexture(noise, new FloatConstantTexture(0.5f)));
            FloatTexture small = new FloatScaledTexture(positive, new FloatConstantTexture(0.004f));
            SpectrumTexture[] varying = new SpectrumTexture[]
            {
                new SpectrumGaussianTexture(mean, new FloatConstantTexture(30), positive, 0.05f),
                new SpectrumFrequencyTexture(new FloatScaledTexture(positive, new FloatConstantTexture(0.03f)), noise, positive),
                new SpectrumCauchyTexture(positive, small, new FloatConstantTexture(0)),
                new SpectrumEdgeTexture(mean, new FloatConstantTexture(25), new FloatConstantTexture(0.1f), positive),
            };
            foreach (SpectrumTexture texture in varying)
            {
                Float bound = texture.MaxValue();
                EXPECT_TRUE(IsFinite(bound), texture.ToString());
                for (int i = 0; i < 5000; ++i)
                {
                    TextureEvalContext c = new TextureEvalContext();
                    c.p = new Point3f(4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2);
                    SampledWavelengths l = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                    EXPECT_LE(texture.Evaluate(c, l).MaxComponentValue(), bound * (1 + 1e-5f), texture.ToString());
                }
            }
        }
        // "texture eta" (and "texture interface.eta"): the materials with an index take one that varies over the surface
        [TEST("ParametricSpectra", "MaterialIndexTextures")]
        public static void MaterialIndexTextures()
        {
            Allocator alloc = new Allocator();
            NamedTextures textures = new NamedTextures();
            FloatTexture a = new FloatAddTexture(new FloatConstantTexture(1.5f), new FloatScaledTexture(new FBmTexture(new PointTransformMapping(pbrt.Scale(2, 2, 2)), 3, 0.5f),
                                                                                                        new FloatConstantTexture(0.2f)));
            textures.unboundedSpectrumTextures["flat"] = new SpectrumCauchyTexture(a, new FloatConstantTexture(0), new FloatConstantTexture(0));
            textures.unboundedSpectrumTextures["dispersive"] = new SpectrumCauchyTexture(a, new FloatConstantTexture(0.01f), new FloatConstantTexture(0));
            ParsedParameter named = new ParsedParameter(new FileLoc());
            named.type = "texture";
            named.name = "eta";
            named.AddString("dispersive");
            ParsedParameter namedInterface = new ParsedParameter(new FileLoc());
            namedInterface.type = "texture";
            namedInterface.name = "interface.eta";
            namedInterface.AddString("dispersive");
            TextureParameterDictionary d = new TextureParameterDictionary(new ParameterDictionary(new List<ParsedParameter> { named, namedInterface }, RGBColorSpace.sRGB),
                                                                          textures);
            EXPECT_TRUE(DielectricMaterial.Create(d, null, new FileLoc(), alloc).etaTexture != null);
            EXPECT_TRUE(ThinDielectricMaterial.Create(d, null, new FileLoc(), alloc).etaTexture != null);
            EXPECT_TRUE(CoatedDiffuseMaterial.Create(d, null, new FileLoc(), alloc).etaTexture != null);
            EXPECT_TRUE(CoatedConductorMaterial.Create(d, null, new FileLoc(), alloc).interfaceEtaTexture != null);
            EXPECT_TRUE(MicrosurfaceMaterial.Create(MicrosurfaceKind.MultiScatterDielectric, d, null, new FileLoc(), alloc).dielectricEtaTexture != null);

            // The index at a point: that of the texture at the first wavelength; the other wavelengths end only if it disperses
            RNG rng = new RNG(5);
            UniversalTextureEvaluator texEval = new UniversalTextureEvaluator();
            Float least = Infinity, most = 0;
            for (int i = 0; i < 2000; ++i)
            {
                MaterialEvalContext ctx = new MaterialEvalContext();
                ctx.p = new Point3f(4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2);
                Float u = rng.Uniform<Float>();
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(u);
                Float flat = ParametricSpectra.SampleIndex(new ConstantSpectrum(1.5f), textures.unboundedSpectrumTextures["flat"], texEval, ctx, ref lambda);
                EXPECT_FALSE(lambda.SecondaryTerminated());
                Float expected = texEval.Apply(a, ctx);
                EXPECT_LT(Math.Abs(flat - expected), 1e-6);
                least = std.min(least, flat);
                most = std.max(most, flat);
                Float first = lambda[0];
                Float dispersive = ParametricSpectra.SampleIndex(new ConstantSpectrum(1.5f), textures.unboundedSpectrumTextures["dispersive"], texEval, ctx, ref lambda);
                EXPECT_TRUE(lambda.SecondaryTerminated());
                EXPECT_LT(Math.Abs(dispersive - (expected + 0.01 / Sqr(first * 1e-3))), 1e-5);
                // (without a texture: the spectrum)
                SampledWavelengths other = SampledWavelengths.SampleVisible(u);
                EXPECT_EQ(1.5f, ParametricSpectra.SampleIndex(new ConstantSpectrum(1.5f), null, texEval, ctx, ref other));
                EXPECT_FALSE(other.SecondaryTerminated());
                ParametricSpectra.SampleIndex(new CauchySpectrum(1.5f, 0.01f, 0), null, texEval, ctx, ref other);
                EXPECT_TRUE(other.SecondaryTerminated());
            }
            // (the index does vary over the points)
            EXPECT_GT(most - least, 0.05f);
        }
    }
}
