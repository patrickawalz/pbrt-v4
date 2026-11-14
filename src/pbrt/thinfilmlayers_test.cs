// (not in pbrt-v4) thinfilmlayers_test.cs
//
// The layers of the thin films of a material (thinfilmlayers.cs): without dispersion and without a texture they are the
// stack as before; a layer that disperses has, at each wavelength of the ray, the reflection of a film with the index of its
// spectrum there; the texture multiplies the thicknesses; and the wavelengths of the ray are kept. The thicknesses of the
// platelets of the pearlescent material as textures.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class ThinFilmLayersTests
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

        static ParsedParameter Named(string type, string name, string value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = type;
            p.name = name;
            p.AddString(value);
            return p;
        }

        static TextureParameterDictionary Dictionary(NamedTextures textures, params ParsedParameter[] parameters)
        {
            return new TextureParameterDictionary(new ParameterDictionary(new List<ParsedParameter>(parameters), RGBColorSpace.sRGB), textures);
        }

        static double Difference(SampledSpectrum a, SampledSpectrum b)
        {
            double worst = 0;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
            }
            return worst;
        }

        [TEST("ThinFilmLayers", "DispersionAndThickness")]
        public static void DispersionAndThickness()
        {
            Allocator alloc = new Allocator();
            UniversalTextureEvaluator texEval = new UniversalTextureEvaluator();
            MaterialEvalContext ctx = new MaterialEvalContext();
            NamedTextures textures = new NamedTextures();
            textures.floatTextures["half"] = new FloatConstantTexture(0.5f);
            SampledSpectrum eta = new SampledSpectrum(0.2f), k = new SampledSpectrum(3.6f);
            RNG rng = new RNG(11);

            ThinFilmLayers plain = ThinFilmLayers.Create(Dictionary(textures, Parameter("float", "layereta", 1.38f, 2.3f), Parameter("float", "layerthickness", 95, 240)),
                                                         new FileLoc(), alloc);
            EXPECT_TRUE(plain.spectra == null);
            ThinFilmLayers abbe = ThinFilmLayers.Create(Dictionary(textures, Parameter("float", "layereta", 1.38f, 2.3f), Parameter("float", "layerabbe", 0, 12),
                                                                   Parameter("float", "layerthickness", 95, 240)), new FileLoc(), alloc);
            EXPECT_TRUE(abbe.spectra != null);
            EXPECT_TRUE(abbe.spectra[0] is ConstantSpectrum);
            EXPECT_LT(Math.Abs(abbe.eta[1] - 2.3), 1e-4);
            ThinFilmLayers cauchy = ThinFilmLayers.Create(Dictionary(textures, Parameter("float", "layereta", 1.38f, 2.1f), Parameter("float", "layercauchyb", 0, 0.03f),
                                                                     Parameter("float", "layerthickness", 95, 240)), new FileLoc(), alloc);
            EXPECT_TRUE(cauchy.spectra[1] is CauchySpectrum);
            ThinFilmLayers sellmeier = ThinFilmLayers.Create(Dictionary(textures, Parameter("float", "layersellmeier", 1.03961212f, 0.231792344f, 1.01046945f,
                                                                                            0.00600069867f, 0.0200179144f, 103.560653f),
                                                                        Parameter("float", "layerthickness", 400)), new FileLoc(), alloc);
            EXPECT_LT(Math.Abs(sellmeier.eta[0] - 1.5167), 1e-3);
            ThinFilmLayers named = ThinFilmLayers.Create(Dictionary(textures, Named("spectrum", "layereta", "glass-BK7"), Parameter("float", "layerthickness", 400)),
                                                         new FileLoc(), alloc);
            EXPECT_TRUE(named.spectra != null);
            EXPECT_LT(Math.Abs(named.eta[0] - 1.5167), 2e-3);
            ThinFilmLayers scaled = ThinFilmLayers.Create(Dictionary(textures, Parameter("float", "layereta", 1.38f, 2.3f), Parameter("float", "layerthickness", 190, 480),
                                                                     Named("texture", "layerthicknessscale", "half")), new FileLoc(), alloc);
            EXPECT_TRUE(scaled.thicknessScale != null);

            double worstPlain = 0, worstDispersive = 0, worstScaled = 0, effect = 0;
            for (int n = 0; n < 500; ++n)
            {
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                Float cosine = 0.05f + 0.95f * rng.Uniform<Float>();
                SampledSpectrum Fs, Fp, Ds, Dp, Rs, Rp, Es, Ep;
                // Without dispersion: the stack as before
                new ThinFilmStack(lambda, new Float[] { 1.38f, 2.3f }, new Float[] { 95, 240 }).Conductor(cosine, eta, k, out Rs, out Rp, out Ds, out Dp);
                plain.Stack(texEval, ctx, lambda).Conductor(cosine, eta, k, out Fs, out Fp, out Ds, out Dp);
                worstPlain = Math.Max(worstPlain, Math.Max(Difference(Fs, Rs), Difference(Fp, Rp)));
                // The texture: half of twice the thicknesses
                scaled.Stack(texEval, ctx, lambda).Conductor(cosine, eta, k, out Fs, out Fp, out Ds, out Dp);
                worstScaled = Math.Max(worstScaled, Math.Max(Difference(Fs, Rs), Difference(Fp, Rp)));
                // With dispersion: at each wavelength, a film with the index of the spectrum there
                abbe.Stack(texEval, ctx, lambda).Conductor(cosine, eta, k, out Fs, out Fp, out Ds, out Dp);
                effect = Math.Max(effect, Difference(Fs, Rs));
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    Float index = abbe.spectra[1].Evaluate(lambda[i]);
                    new ThinFilmStack(lambda, new Float[] { 1.38f, index }, new Float[] { 95, 240 }).Conductor(cosine, eta, k, out Es, out Ep, out Ds, out Dp);
                    worstDispersive = Math.Max(worstDispersive, Math.Max(Math.Abs(Es[i] - Fs[i]), Math.Abs(Ep[i] - Fp[i])));
                    EXPECT_GE(Fs[i], 0);
                    EXPECT_LE(Fs[i], 1 + 1e-5f);
                    EXPECT_LE(Fp[i], 1 + 1e-5f);
                }
                // On a dielectric, from both sides: what is reflected and what goes through are all the light
                abbe.Stack(texEval, ctx, lambda).Dielectric(rng.Uniform<Float>() < 0.5f ? cosine : -cosine, 1.52f, out Fs, out Fp, out Ds, out Dp, out SampledSpectrum _,
                                                            out SampledSpectrum _);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    EXPECT_GE(Fs[i], 0);
                    EXPECT_LE(Fs[i], 1 + 1e-5f);
                    EXPECT_LE(Fp[i], 1 + 1e-5f);
                }
            }
            Console.WriteLine("MEASURE thin film layers: without dispersion {0:E2} from the stack as before, with dispersion {1:E2} from a film of the index at each " +
                              "wavelength, texture {2:E2}; the dispersion changes the reflectance by up to {3:F3}", worstPlain, worstDispersive, worstScaled, effect);
            EXPECT_EQ(0.0, worstPlain);
            EXPECT_LT(worstDispersive, 1e-6);
            EXPECT_LT(worstScaled, 1e-6);
            EXPECT_GT(effect, 0.05);
        }

        [TEST("ThinFilmLayers", "Materials")]
        public static void Materials()
        {
            Allocator alloc = new Allocator();
            UniversalTextureEvaluator texEval = new UniversalTextureEvaluator();
            NamedTextures textures = new NamedTextures();
            textures.floatTextures["ninety"] = new FloatConstantTexture(90);
            MaterialEvalContext ctx = new MaterialEvalContext();
            ctx.n = new Normal3f(0, 0, 1);
            ctx.ns = new Normal3f(0, 0, 1);
            ctx.dpdus = new Vector3f(1, 0, 0);
            ScratchBuffer buf = new ScratchBuffer();

            // Layers that disperse keep the wavelengths of the ray, on a conductor and on a dielectric with one index; a
            // substrate that disperses does not
            ParsedParameter[] layers = new ParsedParameter[] { Parameter("float", "layereta", 2.3f), Parameter("float", "layerabbe", 12),
                                                               Parameter("float", "layerthickness", 240) };
            ThinFilmMaterial onMetal = ThinFilmMaterial.Create(Dictionary(textures, layers[0], layers[1], layers[2], Named("spectrum", "substrateeta", "metal-Cu-eta"),
                                                                          Named("spectrum", "substratek", "metal-Cu-k")), null, new FileLoc(), alloc);
            ThinFilmMaterial onGlass = ThinFilmMaterial.Create(Dictionary(textures, layers[0], layers[1], layers[2], Parameter("float", "substrateeta", 1.5f)), null,
                                                               new FileLoc(), alloc);
            ThinFilmMaterial onFlint = ThinFilmMaterial.Create(Dictionary(textures, layers[0], layers[1], layers[2], Parameter("float", "substratecauchy", 1.7f, 0.015f)),
                                                               null, new FileLoc(), alloc);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            onMetal.GetBSDF(texEval, ctx, ref lambda, buf);
            EXPECT_FALSE(lambda.SecondaryTerminated());
            onGlass.GetBSDF(texEval, ctx, ref lambda, buf);
            EXPECT_FALSE(lambda.SecondaryTerminated());
            onFlint.GetBSDF(texEval, ctx, ref lambda, buf);
            EXPECT_TRUE(lambda.SecondaryTerminated());

            // The pearlescent material: a thickness as a texture is the thickness as a number, and it changes the colour
            SampledWavelengths visible = SampledWavelengths.SampleVisible(0.37f);
            PearlescentMaterial byDefault = PearlescentMaterial.Create(Dictionary(textures), null, new FileLoc(), alloc);
            PearlescentMaterial byNumber = PearlescentMaterial.Create(Dictionary(textures, Parameter("float", "coatthickness", 90)), null, new FileLoc(), alloc);
            PearlescentMaterial byTexture = PearlescentMaterial.Create(Dictionary(textures, Named("texture", "coatthickness", "ninety")), null, new FileLoc(), alloc);
            Vector3f wo = Normalize(new Vector3f(0.3f, 0.1f, 0.9f)), wi = Normalize(new Vector3f(-0.25f, 0.05f, 0.95f));
            SampledSpectrum f60 = byDefault.GetBxDF(texEval, ctx, ref visible).f(wo, wi, TransportMode.Radiance);
            SampledSpectrum f90 = byNumber.GetBxDF(texEval, ctx, ref visible).f(wo, wi, TransportMode.Radiance);
            SampledSpectrum f90Texture = byTexture.GetBxDF(texEval, ctx, ref visible).f(wo, wi, TransportMode.Radiance);
            Console.WriteLine("MEASURE pearlescent: f with a coating of 60 nm {0}, of 90 nm {1}, of 90 nm by a texture {2}", f60[0], f90[0], f90Texture[0]);
            EXPECT_GT(f90[0], 0);
            EXPECT_EQ(0.0, Difference(f90, f90Texture));
            EXPECT_GT(Difference(f60, f90), 1e-3 * f60[0]);
        }
    }
}
