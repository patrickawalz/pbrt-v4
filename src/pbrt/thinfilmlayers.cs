// (not in pbrt-v4) thinfilmlayers.cs
//
// The layers of the thin films of a material ("thinfilm" and the microsurfaces), read from its parameters and given to the BxDF
// at a point as a ThinFilmStack. From the surface inward:
//   float layerthickness [...]        the thicknesses in nanometres
//   float layereta [...]              the indices, the same at every wavelength, or with one of
//     float layerabbe [...]             the Abbe numbers (0: no dispersion): the index given is the one at the sodium D line
//     float layercauchyb [...]          the B of the formula of Cauchy, n = eta + B / lambda^2 (lambda in micrometres)
//   float layersellmeier [...]        instead of "layereta": six coefficients (B1 B2 B3 C1 C2 C3) for each layer
//   spectrum layereta [...]           instead of "float layereta": a spectrum for each layer (named, or any other spectrum)
//   texture layerthicknessscale       a float texture that multiplies the thicknesses at the point (default 1)
// The colours of the interference follow the index at each wavelength of the ray, so a layer that disperses costs no wavelength:
// the layers are thin, and the direction of the light that leaves does not depend on their indices.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class ThinFilmLayers
    {
        // The indices at the sodium D line (the indices, without dispersion) and the thicknesses in nanometres
        public Float[] eta = new Float[0], thickness = new Float[0];
        // The indices as spectra: null if no layer disperses
        public Spectrum[] spectra = null;
        // The texture that multiplies the thicknesses: null without one
        public FloatTexture thicknessScale = null;

        public int Count() { return eta.Length; }

        public static ThinFilmLayers Create(TextureParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            ThinFilmLayers layers = new ThinFilmLayers();
            layers.thickness = parameters.GetFloatArray("layerthickness").ToArray();
            int count = layers.thickness.Length;
            List<Float> sellmeier = parameters.GetFloatArray("layersellmeier");
            List<Float> indices = parameters.GetFloatArray("layereta");
            Spectrum[] spectra = new Spectrum[count];
            bool dispersive = false;
            if (sellmeier.Count != 0)
            {
                if (sellmeier.Count != 6 * count)
                {
                    pbrt.ErrorExit(loc, "\"layersellmeier\" needs six values [B1 B2 B3 C1 C2 C3] for each value of \"layerthickness\".");
                }
                for (int i = 0; i < count; ++i)
                {
                    spectra[i] = new SellmeierSpectrum(sellmeier[6 * i], sellmeier[6 * i + 1], sellmeier[6 * i + 2], sellmeier[6 * i + 3], sellmeier[6 * i + 4],
                                                       sellmeier[6 * i + 5]);
                }
                dispersive = true;
            }
            else if (indices.Count != 0)
            {
                if (indices.Count != count)
                {
                    pbrt.ErrorExit(loc, "\"layereta\" and \"layerthickness\" need the same number of values.");
                }
                List<Float> abbe = parameters.GetFloatArray("layerabbe");
                List<Float> cauchyB = parameters.GetFloatArray("layercauchyb");
                if (abbe.Count != 0 && abbe.Count != count)
                {
                    pbrt.ErrorExit(loc, "\"layerabbe\" and \"layerthickness\" need the same number of values.");
                }
                if (cauchyB.Count != 0 && cauchyB.Count != count)
                {
                    pbrt.ErrorExit(loc, "\"layercauchyb\" and \"layerthickness\" need the same number of values.");
                }
                for (int i = 0; i < count; ++i)
                {
                    if (abbe.Count != 0 && abbe[i] != 0)
                    {
                        spectra[i] = new AbbeDispersionSpectrum(indices[i], abbe[i]);
                        dispersive = true;
                    }
                    else if (cauchyB.Count != 0 && cauchyB[i] != 0)
                    {
                        spectra[i] = new CauchySpectrum(indices[i], cauchyB[i], 0);
                        dispersive = true;
                    }
                    else
                    {
                        spectra[i] = new ConstantSpectrum(indices[i]);
                    }
                }
            }
            else
            {
                List<Spectrum> given = parameters.GetSpectrumArray("layereta", SpectrumType.Unbounded, alloc);
                if (given.Count != count)
                {
                    pbrt.ErrorExit(loc, "\"layereta\" and \"layerthickness\" need the same number of values.");
                }
                for (int i = 0; i < count; ++i)
                {
                    spectra[i] = given[i];
                    dispersive |= !(given[i] is ConstantSpectrum);
                }
            }
            layers.eta = new Float[count];
            for (int i = 0; i < count; ++i)
            {
                layers.eta[i] = spectra[i].Evaluate(AbbeDispersionSpectrum.LambdaD);
            }
            layers.spectra = dispersive ? spectra : null;
            layers.thicknessScale = parameters.GetFloatTextureOrNull("layerthicknessscale", alloc);
            return layers;
        }

        // The layers at a point, for the wavelengths of the ray. Arrays are made only for what varies: the indices of layers
        // that disperse, the thicknesses under a texture.
        public ThinFilmStack Stack<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            Float[] thick = thickness;
            if (thicknessScale != null)
            {
                Float scale = std.max<Float>(0, texEval.Apply(thicknessScale, ctx));
                thick = new Float[thickness.Length];
                for (int i = 0; i < thick.Length; ++i)
                {
                    thick[i] = thickness[i] * scale;
                }
            }
            if (spectra == null)
            {
                return new ThinFilmStack(lambda, eta, thick);
            }
            Float[][] perWavelength = new Float[pbrt.NSpectrumSamples][];
            for (int w = 0; w < pbrt.NSpectrumSamples; ++w)
            {
                Float[] indices = new Float[spectra.Length];
                for (int i = 0; i < indices.Length; ++i)
                {
                    indices[i] = spectra[i].Evaluate(lambda[w]);
                }
                perWavelength[w] = indices;
            }
            return new ThinFilmStack(lambda, perWavelength, thick);
        }
    }
}
