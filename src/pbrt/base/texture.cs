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

        // (not in pbrt-v4) The derivative of the value with respect to the point passed in TextureEvalContext::p. A central
        // difference unless the texture overrides it with the analytic derivative, which bump and normal mapping from a
        // volume prefer.
        public virtual Vector3f Gradient(TextureEvalContext ctx)
        {
            const Float h = 1e-3f;
            Vector3f gradient = new Vector3f(0, 0, 0);
            for (int axis = 0; axis < 3; ++axis)
            {
                TextureEvalContext lower = ctx, upper = ctx;
                lower.p[axis] -= h;
                upper.p[axis] += h;
                gradient[axis] = (Evaluate(upper) - Evaluate(lower)) / (2 * h);
            }
            return gradient;
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

        // (not in pbrt-v4) Upper bound on the value at any wavelength for contexts whose point lies in a region of
        // texture space; the default is the global bound
        public virtual Float RegionMax(Bounds3f region) { return MaxValue(); }
    }

}  // namespace pbrt
