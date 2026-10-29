// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/texture.h

using System;

namespace pbrt
{

    // FloatTexture Definition
    // (TaggedPointer over FloatImageTexture, GPUFloatImageTexture, FloatMixTexture,
    // FloatDirectionMixTexture, FloatScaledTexture, FloatConstantTexture, FloatBilerpTexture,
    // FloatCheckerboardTexture, FloatDotsTexture, FBmTexture, FloatPtexTexture, GPUFloatPtexTexture,
    // WindyTexture, WrinkledTexture)
    public abstract unsafe partial class FloatTexture
    {
        // FloatTexture Interface
        public abstract override string ToString();
        public abstract Float Evaluate(TextureEvalContext ctx);

        // (not in pbrt-v4) Conservative bounds on the values Evaluate() can return, for any
        // context. Used to build majorants for texture-driven media.
        public abstract Float MinValue();
        public abstract Float MaxValue();

        // (not in pbrt-v4) Bounds on the value over a region of the points passed in
        // TextureEvalContext::p, assuming zero ray differentials (full octaves). Defaults to the
        // global bounds; textures that can do better override it.
        public virtual void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            lo = MinValue();
            hi = MaxValue();
        }
    }

    // SpectrumTexture Definition
    // (TaggedPointer over SpectrumImageTexture, GPUSpectrumImageTexture, SpectrumMixTexture,
    // SpectrumDirectionMixTexture, SpectrumScaledTexture, SpectrumConstantTexture,
    // SpectrumBilerpTexture, SpectrumCheckerboardTexture, MarbleTexture, SpectrumDotsTexture,
    // SpectrumPtexTexture, GPUSpectrumPtexTexture)
    public abstract unsafe partial class SpectrumTexture
    {
        // SpectrumTexture Interface
        public abstract override string ToString();
        public abstract SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda);

        // (not in pbrt-v4) Conservative upper bound on the value at any wavelength, for any
        // context. The pbrt spectrum textures are non-negative, so no lower bound is needed.
        public abstract Float MaxValue();
    }

}  // namespace pbrt
