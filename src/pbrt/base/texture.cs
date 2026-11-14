// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// base/texture.h

using System;

namespace pbrt
{

    // TextureDirections Definition
    // (not in pbrt-v4) The two directions along which a texture is differentiated: tangent vectors of the surface, dpdu and
    // dpdv, and what a step along each changes of the first texture coordinates of the surface (duvdu, duvdv). The
    // derivative "by u" of a texture is its change along dpdu, per unit of that step.
    public struct TextureDirections
    {
        // The tangents of the first texture coordinates themselves: a step along dpdu is a step of u, as in pbrt's BumpMap()
        public TextureDirections(Vector3f dpdu, Vector3f dpdv)
        {
            this.dpdu = dpdu;
            this.dpdv = dpdv;
            duvdu = new Vector2f(1, 0);
            duvdv = new Vector2f(0, 1);
        }

        // The context moved by `step` along a direction: the point, the first texture coordinates, and how far it has
        // moved (for the mappings that read other texture coordinates from the hit)
        public static TextureEvalContext Shifted(TextureEvalContext ctx, Float step, Vector3f direction, Vector2f duv)
        {
            TextureEvalContext shifted = ctx;
            shifted.p = ctx.p + step * direction;
            shifted.uv = ctx.uv + step * duv;
            shifted.shift = ctx.shift + step * direction;
            return shifted;
        }

        public Vector3f dpdu, dpdv;
        public Vector2f duvdu, duvdv;
    }

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
            if (HasAnalyticDerivatives() && !finiteDifferences && !pbrt.GetOptions().finiteTextureDerivatives)
            {
                // The closed form along the three axes, the texture coordinates held
                TextureDirections xy = new TextureDirections(new Vector3f(1, 0, 0), new Vector3f(0, 1, 0));
                xy.duvdu = new Vector2f(0, 0);
                xy.duvdv = new Vector2f(0, 0);
                TextureDirections z = xy;
                z.dpdu = new Vector3f(0, 0, 1);
                Float dx, dy, dz;
                EvaluateDerivatives(ctx, xy, &dx, &dy);
                EvaluateDerivatives(ctx, z, &dz, null);
                return new Vector3f(dx, dy, dz);
            }
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
        // (not in pbrt-v4) Derivatives of the value along two directions of the surface (see TextureDirections): what bump
        // mapping, and anything else that needs the slope of a texture, asks for. Every texture answers. A texture with a
        // closed form overrides ValueAndDerivatives() (and Derivatives() where the derivative is cheaper without the value);
        // one without gets the finite difference of pbrt's BumpMap(), computed on the fly. A texture made of others asks
        // each of them and applies its own rule to what they return, however they got it.
        //
        // A null pointer is a derivative that nobody wants: it is not computed.
        //
        // Finite differences can be forced: for one texture with "bool analyticderivatives" false (finiteDifferences), for
        // all of them with Option "texturederivatives" "finite" (which is stock pbrt: the whole graph of a texture
        // is evaluated again at the shifted points).
        public bool finiteDifferences = false;

        public Float Evaluate(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            if (dfdu == null && dfdv == null)
            {
                return Evaluate(ctx);
            }
            if (finiteDifferences || pbrt.GetOptions().finiteTextureDerivatives)
            {
                Float value = Evaluate(ctx);
                FiniteDifferences(ctx, value, directions, dfdu, dfdv);
                return value;
            }
            return ValueAndDerivatives(ctx, directions, dfdu, dfdv);
        }

        // The derivatives alone
        public void EvaluateDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            if (dfdu == null && dfdv == null)
            {
                return;
            }
            if (finiteDifferences || pbrt.GetOptions().finiteTextureDerivatives)
            {
                FiniteDifferences(ctx, Evaluate(ctx), directions, dfdu, dfdv);
                return;
            }
            Derivatives(ctx, directions, dfdu, dfdv);
        }

        // Whether the texture has a closed form of its own (its rule; what it is made of may still take differences)
        public virtual bool HasAnalyticDerivatives()
        {
            return false;
        }

        protected virtual Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Float value = Evaluate(ctx);
            FiniteDifferences(ctx, value, directions, dfdu, dfdv);
            return value;
        }

        protected virtual void Derivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            ValueAndDerivatives(ctx, directions, dfdu, dfdv);
        }

        // The difference of pbrt's BumpMap(): the context moved by a step the size of the pixel on the surface
        protected void FiniteDifferences(TextureEvalContext ctx, Float value, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            if (dfdu != null)
            {
                Float du = .5f * (std.abs(ctx.dudx) + std.abs(ctx.dudy));
                if (du == 0)
                {
                    du = .0005f;
                }
                *dfdu = (Evaluate(TextureDirections.Shifted(ctx, du, directions.dpdu, directions.duvdu)) - value) / du;
            }
            if (dfdv != null)
            {
                Float dv = .5f * (std.abs(ctx.dvdx) + std.abs(ctx.dvdy));
                if (dv == 0)
                {
                    dv = .0005f;
                }
                *dfdv = (Evaluate(TextureDirections.Shifted(ctx, dv, directions.dpdv, directions.duvdv)) - value) / dv;
            }
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

        // (not in pbrt-v4) A lower bound on the value at any wavelength, for any context (the spectrum textures of pbrt
        // are not negative), and both bounds over a region
        public virtual Float MinValue()
        {
            return 0;
        }

        public virtual void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            lo = MinValue();
            hi = RegionMax(region);
        }

        // (not in pbrt-v4) Derivatives of the value along two directions of the surface, at every wavelength: as for
        // FloatTexture
        public bool finiteDifferences = false;

        public SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions, SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            if (dfdu == null && dfdv == null)
            {
                return Evaluate(ctx, lambda);
            }
            if (finiteDifferences || pbrt.GetOptions().finiteTextureDerivatives)
            {
                SampledSpectrum value = Evaluate(ctx, lambda);
                FiniteDifferences(ctx, lambda, value, directions, dfdu, dfdv);
                return value;
            }
            return ValueAndDerivatives(ctx, lambda, directions, dfdu, dfdv);
        }

        // The derivatives alone
        public void EvaluateDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions, SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            if (dfdu == null && dfdv == null)
            {
                return;
            }
            if (finiteDifferences || pbrt.GetOptions().finiteTextureDerivatives)
            {
                FiniteDifferences(ctx, lambda, Evaluate(ctx, lambda), directions, dfdu, dfdv);
                return;
            }
            Derivatives(ctx, lambda, directions, dfdu, dfdv);
        }

        // Whether the texture has a closed form of its own (its rule; what it is made of may still take differences)
        public virtual bool HasAnalyticDerivatives()
        {
            return false;
        }

        protected virtual SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions, SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            SampledSpectrum value = Evaluate(ctx, lambda);
            FiniteDifferences(ctx, lambda, value, directions, dfdu, dfdv);
            return value;
        }

        protected virtual void Derivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions, SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            ValueAndDerivatives(ctx, lambda, directions, dfdu, dfdv);
        }

        // The difference of pbrt's BumpMap(): the context moved by a step the size of the pixel on the surface
        protected void FiniteDifferences(TextureEvalContext ctx, SampledWavelengths lambda, SampledSpectrum value, TextureDirections directions,
                                         SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            if (dfdu != null)
            {
                Float du = .5f * (std.abs(ctx.dudx) + std.abs(ctx.dudy));
                if (du == 0)
                {
                    du = .0005f;
                }
                *dfdu = (Evaluate(TextureDirections.Shifted(ctx, du, directions.dpdu, directions.duvdu), lambda) - value) / du;
            }
            if (dfdv != null)
            {
                Float dv = .5f * (std.abs(ctx.dvdx) + std.abs(ctx.dvdy));
                if (dv == 0)
                {
                    dv = .0005f;
                }
                *dfdv = (Evaluate(TextureDirections.Shifted(ctx, dv, directions.dpdv, directions.duvdv), lambda) - value) / dv;
            }
        }
    }

}  // namespace pbrt
