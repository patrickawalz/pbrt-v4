// (not in pbrt-v4) The derivatives of the textures in closed form (see FloatTexture.Evaluate() with directions in
// base/texture.cs). A texture on a mapping differentiates its value by the mapped coordinates and asks the mapping how
// those change along the two directions; a texture made of others asks each of them (which answers in closed form or
// by finite differences, as it can) and applies the rule of its own operation.
//
// Still on the finite difference of the base class: the image and Ptex textures, the OSL textures, and the textures
// of the volume and SDF work (cloud, triplanar, shell map, SDF, label); of the spectrum textures those that go through
// an RGB colour (image, marble, ramp, grid, variation, strand colour, blackbody).

using System;

namespace pbrt
{
    // A value that goes with a pointer that may be null: the place for what a child texture returns
    internal unsafe struct Slope
    {
        public Float u, v;
    }

    public sealed unsafe partial class FloatConstantTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Derivatives(ctx, directions, dfdu, dfdv);
            return value;
        }

        protected override void Derivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            if (dfdu != null)
            {
                *dfdu = 0;
            }
            if (dfdv != null)
            {
                *dfdv = 0;
            }
        }
    }

    public sealed unsafe partial class SpectrumConstantTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            Derivatives(ctx, lambda, directions, dfdu, dfdv);
            return Evaluate(ctx, lambda);
        }

        protected override void Derivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions, SampledSpectrum* dfdu,
                                            SampledSpectrum* dfdv)
        {
            if (dfdu != null)
            {
                *dfdu = new SampledSpectrum(0f);
            }
            if (dfdv != null)
            {
                *dfdv = new SampledSpectrum(0f);
            }
        }
    }

    public sealed unsafe partial class FloatBilerpTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            TexCoord2D c = mapping.Map(ctx);
            Float s = c.st[0], t = c.st[1];
            Float dfds = (1 - t) * (v10 - v00) + t * (v11 - v01);
            Float dfdt = (1 - s) * (v01 - v00) + s * (v11 - v10);
            Vector2f dstdu = default, dstdv = default;
            mapping.MapDerivatives(ctx, directions, dfdu != null ? &dstdu : null, dfdv != null ? &dstdv : null);
            if (dfdu != null)
            {
                *dfdu = dfds * dstdu.x + dfdt * dstdu.y;
            }
            if (dfdv != null)
            {
                *dfdv = dfds * dstdv.x + dfdt * dstdv.y;
            }
            return (1 - s) * (1 - t) * v00 + s * (1 - t) * v10 + (1 - s) * t * v01 + s * t * v11;
        }
    }

    public sealed unsafe partial class SpectrumBilerpTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            TexCoord2D c = mapping.Map(ctx);
            Float s = c.st[0], t = c.st[1];
            SampledSpectrum s00 = v00.Sample(lambda), s01 = v01.Sample(lambda), s10 = v10.Sample(lambda), s11 = v11.Sample(lambda);
            SampledSpectrum dfds = (1 - t) * (s10 - s00) + t * (s11 - s01);
            SampledSpectrum dfdt = (1 - s) * (s01 - s00) + s * (s11 - s10);
            Vector2f dstdu = default, dstdv = default;
            mapping.MapDerivatives(ctx, directions, dfdu != null ? &dstdu : null, dfdv != null ? &dstdv : null);
            if (dfdu != null)
            {
                *dfdu = dfds * dstdu.x + dfdt * dstdu.y;
            }
            if (dfdv != null)
            {
                *dfdv = dfds * dstdv.x + dfdt * dstdv.y;
            }
            return (1 - s) * (1 - t) * s00 + s * (1 - t) * s10 + (1 - s) * t * s01 + s * t * s11;
        }
    }

    // The weight of the checkerboard (pbrt.Checkerboard()) with its derivatives: the product of the filtered square waves
    // of the axes, each a piecewise quadratic of its coordinate
    internal static unsafe class CheckerboardDerivative
    {
        static Float D(Float x)
        {
            Float y = x / 2 - std.floor(x / 2) - 0.5f;
            return x / 2 + y * (1 - 2 * std.abs(y));
        }

        static Float DPrime(Float x)
        {
            Float y = x / 2 - std.floor(x / 2) - 0.5f;
            return 1 - 2 * std.abs(y);
        }

        static Float Wave(Float x, Float r)
        {
            if (std.floor(x - r) == std.floor(x + r))
            {
                return 1 - 2 * ((int)std.floor(x) & 1);
            }
            return (D(x + r) - 2 * D(x) + D(x - r)) / pbrt.Sqr(r);
        }

        static Float WavePrime(Float x, Float r)
        {
            if (std.floor(x - r) == std.floor(x + r))
            {
                return 0;
            }
            return (DPrime(x + r) - 2 * DPrime(x) + DPrime(x - r)) / pbrt.Sqr(r);
        }

        public static Float Weight(TextureEvalContext ctx, TextureMapping2D map2D, TextureMapping3D map3D, TextureDirections directions, Float* dwdu,
                                   Float* dwdv)
        {
            if (map2D != null)
            {
                TexCoord2D c = map2D.Map(ctx);
                Float ds = 1.5f * std.max(std.abs(c.dsdx), std.abs(c.dsdy));
                Float dt = 1.5f * std.max(std.abs(c.dtdx), std.abs(c.dtdy));
                Float ws = Wave(c.st[0], ds), wt = Wave(c.st[1], dt);
                Float dwds = -0.5f * WavePrime(c.st[0], ds) * wt, dwdt = -0.5f * ws * WavePrime(c.st[1], dt);
                Vector2f dstdu = default, dstdv = default;
                map2D.MapDerivatives(ctx, directions, dwdu != null ? &dstdu : null, dwdv != null ? &dstdv : null);
                if (dwdu != null)
                {
                    *dwdu = dwds * dstdu.x + dwdt * dstdu.y;
                }
                if (dwdv != null)
                {
                    *dwdv = dwds * dstdv.x + dwdt * dstdv.y;
                }
                return 0.5f - ws * wt / 2;
            }
            else
            {
                TexCoord3D c = map3D.Map(ctx);
                Float dx = 1.5f * std.max(std.abs(c.dpdx.x), std.abs(c.dpdy.x));
                Float dy = 1.5f * std.max(std.abs(c.dpdx.y), std.abs(c.dpdy.y));
                Float dz = 1.5f * std.max(std.abs(c.dpdx.z), std.abs(c.dpdy.z));
                Float wx = Wave(c.p.x, dx), wy = Wave(c.p.y, dy), wz = Wave(c.p.z, dz);
                Vector3f gradient = -0.5f * new Vector3f(WavePrime(c.p.x, dx) * wy * wz, wx * WavePrime(c.p.y, dy) * wz, wx * wy * WavePrime(c.p.z, dz));
                Vector3f dpdu = default, dpdv = default;
                map3D.MapDerivatives(ctx, directions, dwdu != null ? &dpdu : null, dwdv != null ? &dpdv : null);
                if (dwdu != null)
                {
                    *dwdu = pbrt.Dot(gradient, dpdu);
                }
                if (dwdv != null)
                {
                    *dwdv = pbrt.Dot(gradient, dpdv);
                }
                return 0.5f - 0.5f * wx * wy * wz;
            }
        }
    }

    public sealed unsafe partial class FloatCheckerboardTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope dw = default, d0 = default, d1 = default;
            Float w = CheckerboardDerivative.Weight(ctx, map2D, map3D, directions, dfdu != null ? &dw.u : null, dfdv != null ? &dw.v : null);
            bool edge = dw.u != 0 || dw.v != 0;
            Float t0 = 0, t1 = 0;
            if (w != 1 || edge)
            {
                t0 = tex[0].Evaluate(ctx, directions, dfdu != null ? &d0.u : null, dfdv != null ? &d0.v : null);
            }
            if (w != 0 || edge)
            {
                t1 = tex[1].Evaluate(ctx, directions, dfdu != null ? &d1.u : null, dfdv != null ? &d1.v : null);
            }
            if (dfdu != null)
            {
                *dfdu = dw.u * (t1 - t0) + (1 - w) * d0.u + w * d1.u;
            }
            if (dfdv != null)
            {
                *dfdv = dw.v * (t1 - t0) + (1 - w) * d0.v + w * d1.v;
            }
            return (1 - w) * t0 + w * t1;
        }
    }

    public sealed unsafe partial class SpectrumCheckerboardTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            Slope dw = default;
            SampledSpectrum d0u = new SampledSpectrum(0f), d0v = new SampledSpectrum(0f), d1u = new SampledSpectrum(0f), d1v = new SampledSpectrum(0f);
            Float w = CheckerboardDerivative.Weight(ctx, map2D, map3D, directions, dfdu != null ? &dw.u : null, dfdv != null ? &dw.v : null);
            bool edge = dw.u != 0 || dw.v != 0;
            SampledSpectrum t0 = new SampledSpectrum(0f), t1 = new SampledSpectrum(0f);
            if (w != 1 || edge)
            {
                t0 = tex[0].Evaluate(ctx, lambda, directions, dfdu != null ? &d0u : null, dfdv != null ? &d0v : null);
            }
            if (w != 0 || edge)
            {
                t1 = tex[1].Evaluate(ctx, lambda, directions, dfdu != null ? &d1u : null, dfdv != null ? &d1v : null);
            }
            if (dfdu != null)
            {
                *dfdu = dw.u * (t1 - t0) + (1 - w) * d0u + w * d1u;
            }
            if (dfdv != null)
            {
                *dfdv = dw.v * (t1 - t0) + (1 - w) * d0v + w * d1v;
            }
            return (1 - w) * t0 + w * t1;
        }
    }

    // The dots: the texture of the side of the edge that the point is on (the edge itself is a step: no slope)
    public sealed unsafe partial class FloatDotsTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            TexCoord2D c = mapping.Map(ctx);
            return pbrt.InsidePolkaDot(c.st) ? insideDot.Evaluate(ctx, directions, dfdu, dfdv) : outsideDot.Evaluate(ctx, directions, dfdu, dfdv);
        }

        protected override void Derivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            TexCoord2D c = mapping.Map(ctx);
            if (pbrt.InsidePolkaDot(c.st))
            {
                insideDot.EvaluateDerivatives(ctx, directions, dfdu, dfdv);
            }
            else
            {
                outsideDot.EvaluateDerivatives(ctx, directions, dfdu, dfdv);
            }
        }
    }

    public sealed unsafe partial class SpectrumDotsTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            TexCoord2D c = mapping.Map(ctx);
            return pbrt.InsidePolkaDot(c.st) ? insideDot.Evaluate(ctx, lambda, directions, dfdu, dfdv)
                                             : outsideDot.Evaluate(ctx, lambda, directions, dfdu, dfdv);
        }
    }

    public sealed unsafe partial class FBmTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            TexCoord3D c = mapping.Map(ctx);
            Vector3f gradient, dpdu = default, dpdv = default;
            Float value = pbrt.FBm(c.p, c.dpdx, c.dpdy, omega, octaves, &gradient);
            mapping.MapDerivatives(ctx, directions, dfdu != null ? &dpdu : null, dfdv != null ? &dpdv : null);
            if (dfdu != null)
            {
                *dfdu = pbrt.Dot(gradient, dpdu);
            }
            if (dfdv != null)
            {
                *dfdv = pbrt.Dot(gradient, dpdv);
            }
            return value;
        }
    }

    public sealed unsafe partial class WrinkledTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            TexCoord3D c = mapping.Map(ctx);
            Vector3f gradient, dpdu = default, dpdv = default;
            Float value = pbrt.Turbulence(c.p, c.dpdx, c.dpdy, omega, octaves, &gradient);
            mapping.MapDerivatives(ctx, directions, dfdu != null ? &dpdu : null, dfdv != null ? &dpdv : null);
            if (dfdu != null)
            {
                *dfdu = pbrt.Dot(gradient, dpdu);
            }
            if (dfdv != null)
            {
                *dfdv = pbrt.Dot(gradient, dpdv);
            }
            return value;
        }
    }

    // |strength| height: strength = FBm(0.1 p) and height = FBm(p)
    public sealed unsafe partial class WindyTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            TexCoord3D c = mapping.Map(ctx);
            Vector3f strengthGradient, heightGradient, dpdu = default, dpdv = default;
            Float windStrength = pbrt.FBm(.1f * c.p, .1f * c.dpdx, .1f * c.dpdy, .5f, 3, &strengthGradient);
            Float waveHeight = pbrt.FBm(c.p, c.dpdx, c.dpdy, .5f, 6, &heightGradient);
            Vector3f gradient = (windStrength < 0 ? -.1f * waveHeight : .1f * waveHeight) * strengthGradient + std.abs(windStrength) * heightGradient;
            mapping.MapDerivatives(ctx, directions, dfdu != null ? &dpdu : null, dfdv != null ? &dpdv : null);
            if (dfdu != null)
            {
                *dfdu = pbrt.Dot(gradient, dpdu);
            }
            if (dfdv != null)
            {
                *dfdv = pbrt.Dot(gradient, dpdv);
            }
            return std.abs(windStrength) * waveHeight;
        }
    }

    // (1 - amount) tex1 + amount tex2
    public sealed unsafe partial class FloatMixTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope da = default, d1 = default, d2 = default;
            Float amt = amount.Evaluate(ctx, directions, dfdu != null ? &da.u : null, dfdv != null ? &da.v : null);
            bool moving = da.u != 0 || da.v != 0;
            Float t1 = 0, t2 = 0;
            if (amt != 1 || moving)
            {
                t1 = tex1.Evaluate(ctx, directions, dfdu != null ? &d1.u : null, dfdv != null ? &d1.v : null);
            }
            if (amt != 0 || moving)
            {
                t2 = tex2.Evaluate(ctx, directions, dfdu != null ? &d2.u : null, dfdv != null ? &d2.v : null);
            }
            if (dfdu != null)
            {
                *dfdu = da.u * (t2 - t1) + (1 - amt) * d1.u + amt * d2.u;
            }
            if (dfdv != null)
            {
                *dfdv = da.v * (t2 - t1) + (1 - amt) * d1.v + amt * d2.v;
            }
            return (1 - amt) * t1 + amt * t2;
        }
    }

    public sealed unsafe partial class SpectrumMixTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            Slope da = default;
            SampledSpectrum d1u = new SampledSpectrum(0f), d1v = new SampledSpectrum(0f), d2u = new SampledSpectrum(0f), d2v = new SampledSpectrum(0f);
            Float amt = amount.Evaluate(ctx, directions, dfdu != null ? &da.u : null, dfdv != null ? &da.v : null);
            bool moving = da.u != 0 || da.v != 0;
            SampledSpectrum t1 = new SampledSpectrum(0f), t2 = new SampledSpectrum(0f);
            if (amt != 1 || moving)
            {
                t1 = tex1.Evaluate(ctx, lambda, directions, dfdu != null ? &d1u : null, dfdv != null ? &d1v : null);
            }
            if (amt != 0 || moving)
            {
                t2 = tex2.Evaluate(ctx, lambda, directions, dfdu != null ? &d2u : null, dfdv != null ? &d2v : null);
            }
            if (dfdu != null)
            {
                *dfdu = da.u * (t2 - t1) + (1 - amt) * d1u + amt * d2u;
            }
            if (dfdv != null)
            {
                *dfdv = da.v * (t2 - t1) + (1 - amt) * d1v + amt * d2v;
            }
            return (1 - amt) * t1 + amt * t2;
        }
    }

    // amount tex1 + (1 - amount) tex2, the amount from the normal of the context, which does not move with the point
    public sealed unsafe partial class FloatDirectionMixTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope d1 = default, d2 = default;
            Float amt = pbrt.AbsDot(ctx.n, dir);
            Float t1 = 0, t2 = 0;
            if (amt != 0)
            {
                t1 = tex1.Evaluate(ctx, directions, dfdu != null ? &d1.u : null, dfdv != null ? &d1.v : null);
            }
            if (amt != 1)
            {
                t2 = tex2.Evaluate(ctx, directions, dfdu != null ? &d2.u : null, dfdv != null ? &d2.v : null);
            }
            if (dfdu != null)
            {
                *dfdu = amt * d1.u + (1 - amt) * d2.u;
            }
            if (dfdv != null)
            {
                *dfdv = amt * d1.v + (1 - amt) * d2.v;
            }
            return amt * t1 + (1 - amt) * t2;
        }
    }

    public sealed unsafe partial class SpectrumDirectionMixTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            SampledSpectrum d1u = new SampledSpectrum(0f), d1v = new SampledSpectrum(0f), d2u = new SampledSpectrum(0f), d2v = new SampledSpectrum(0f);
            Float amt = pbrt.AbsDot(ctx.n, dir);
            SampledSpectrum t1 = new SampledSpectrum(0f), t2 = new SampledSpectrum(0f);
            if (amt != 0)
            {
                t1 = tex1.Evaluate(ctx, lambda, directions, dfdu != null ? &d1u : null, dfdv != null ? &d1v : null);
            }
            if (amt != 1)
            {
                t2 = tex2.Evaluate(ctx, lambda, directions, dfdu != null ? &d2u : null, dfdv != null ? &d2v : null);
            }
            if (dfdu != null)
            {
                *dfdu = amt * d1u + (1 - amt) * d2u;
            }
            if (dfdv != null)
            {
                *dfdv = amt * d1v + (1 - amt) * d2v;
            }
            return amt * t1 + (1 - amt) * t2;
        }
    }

    // tex scale
    public sealed unsafe partial class FloatScaledTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope ds = default, dt = default;
            Float sc = scale.Evaluate(ctx, directions, dfdu != null ? &ds.u : null, dfdv != null ? &ds.v : null);
            Float t = 0;
            if (sc != 0 || ds.u != 0 || ds.v != 0)
            {
                t = tex.Evaluate(ctx, directions, dfdu != null ? &dt.u : null, dfdv != null ? &dt.v : null);
            }
            if (dfdu != null)
            {
                *dfdu = dt.u * sc + t * ds.u;
            }
            if (dfdv != null)
            {
                *dfdv = dt.v * sc + t * ds.v;
            }
            return t * sc;
        }
    }

    public sealed unsafe partial class SpectrumScaledTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            Slope ds = default;
            SampledSpectrum dtu = new SampledSpectrum(0f), dtv = new SampledSpectrum(0f);
            Float sc = scale.Evaluate(ctx, directions, dfdu != null ? &ds.u : null, dfdv != null ? &ds.v : null);
            SampledSpectrum t = new SampledSpectrum(0f);
            if (sc != 0 || ds.u != 0 || ds.v != 0)
            {
                t = tex.Evaluate(ctx, lambda, directions, dfdu != null ? &dtu : null, dfdv != null ? &dtv : null);
            }
            if (dfdu != null)
            {
                *dfdu = dtu * sc + t * ds.u;
            }
            if (dfdv != null)
            {
                *dfdv = dtv * sc + t * ds.v;
            }
            return t * sc;
        }
    }

    public sealed unsafe partial class FloatAddTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope d1 = default, d2 = default;
            Float a = tex1.Evaluate(ctx, directions, dfdu != null ? &d1.u : null, dfdv != null ? &d1.v : null);
            Float b = tex2.Evaluate(ctx, directions, dfdu != null ? &d2.u : null, dfdv != null ? &d2.v : null);
            if (dfdu != null)
            {
                *dfdu = d1.u + d2.u;
            }
            if (dfdv != null)
            {
                *dfdv = d1.v + d2.v;
            }
            return a + b;
        }

        protected override void Derivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope d1 = default, d2 = default;
            tex1.EvaluateDerivatives(ctx, directions, dfdu != null ? &d1.u : null, dfdv != null ? &d1.v : null);
            tex2.EvaluateDerivatives(ctx, directions, dfdu != null ? &d2.u : null, dfdv != null ? &d2.v : null);
            if (dfdu != null)
            {
                *dfdu = d1.u + d2.u;
            }
            if (dfdv != null)
            {
                *dfdv = d1.v + d2.v;
            }
        }
    }

    public sealed unsafe partial class FloatSubtractTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope d1 = default, d2 = default;
            Float a = tex1.Evaluate(ctx, directions, dfdu != null ? &d1.u : null, dfdv != null ? &d1.v : null);
            Float b = tex2.Evaluate(ctx, directions, dfdu != null ? &d2.u : null, dfdv != null ? &d2.v : null);
            if (dfdu != null)
            {
                *dfdu = d1.u - d2.u;
            }
            if (dfdv != null)
            {
                *dfdv = d1.v - d2.v;
            }
            return a - b;
        }

        protected override void Derivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope d1 = default, d2 = default;
            tex1.EvaluateDerivatives(ctx, directions, dfdu != null ? &d1.u : null, dfdv != null ? &d1.v : null);
            tex2.EvaluateDerivatives(ctx, directions, dfdu != null ? &d2.u : null, dfdv != null ? &d2.v : null);
            if (dfdu != null)
            {
                *dfdu = d1.u - d2.u;
            }
            if (dfdv != null)
            {
                *dfdv = d1.v - d2.v;
            }
        }
    }

    public sealed unsafe partial class FloatAbsTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Float t = tex.Evaluate(ctx, directions, dfdu, dfdv);
            if (t < 0)
            {
                if (dfdu != null)
                {
                    *dfdu = -*dfdu;
                }
                if (dfdv != null)
                {
                    *dfdv = -*dfdv;
                }
            }
            return std.abs(t);
        }
    }

    public sealed unsafe partial class FloatClampTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Float t = tex.Evaluate(ctx, directions, dfdu, dfdv);
            if (!(t > minValue && t < maxValue))
            {
                // Held at a limit: flat
                if (dfdu != null)
                {
                    *dfdu = 0;
                }
                if (dfdv != null)
                {
                    *dfdv = 0;
                }
            }
            return pbrt.Clamp(t, minValue, maxValue);
        }
    }

    // tex1 / tex2, and 0 where tex2 is 0
    public sealed unsafe partial class FloatDivideTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope d1 = default, d2 = default;
            Float b = tex2.Evaluate(ctx, directions, dfdu != null ? &d2.u : null, dfdv != null ? &d2.v : null);
            if (b == 0)
            {
                if (dfdu != null)
                {
                    *dfdu = 0;
                }
                if (dfdv != null)
                {
                    *dfdv = 0;
                }
                return 0;
            }
            Float a = tex1.Evaluate(ctx, directions, dfdu != null ? &d1.u : null, dfdv != null ? &d1.v : null);
            if (dfdu != null)
            {
                *dfdu = (d1.u * b - a * d2.u) / (b * b);
            }
            if (dfdv != null)
            {
                *dfdv = (d1.v * b - a * d2.v) / (b * b);
            }
            return a / b;
        }
    }

    // base^exponent, and 0 for a negative base with an exponent that is not whole
    public sealed unsafe partial class FloatPowerTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope db = default, de = default;
            Float b = baseTexture.Evaluate(ctx, directions, dfdu != null ? &db.u : null, dfdv != null ? &db.v : null);
            Float e = exponent.Evaluate(ctx, directions, dfdu != null ? &de.u : null, dfdv != null ? &de.v : null);
            Float value = SafePow(b, e);
            Float byBase = 0, byExponent = 0;
            if (!(b < 0 && e != (int)e))
            {
                byBase = b != 0 ? e * value / b : (e == 1 ? 1 : 0);
                byExponent = b > 0 ? value * std.log(b) : 0;
            }
            if (dfdu != null)
            {
                *dfdu = byBase * db.u + byExponent * de.u;
            }
            if (dfdv != null)
            {
                *dfdv = byBase * db.v + byExponent * de.v;
            }
            return value;
        }
    }

    // The value held between the source limits, then mapped linearly from the source range to the target range
    public sealed unsafe partial class FloatRemapTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope dv = default, dsMin = default, dsMax = default, dtMin = default, dtMax = default;
            Float* pu = dfdu, pv = dfdv;
            Float v = value.Evaluate(ctx, directions, pu != null ? &dv.u : null, pv != null ? &dv.v : null);
            Float sMin = sourceMin.Evaluate(ctx, directions, pu != null ? &dsMin.u : null, pv != null ? &dsMin.v : null);
            Float sMax = sourceMax.Evaluate(ctx, directions, pu != null ? &dsMax.u : null, pv != null ? &dsMax.v : null);
            Float tMin = targetMin.Evaluate(ctx, directions, pu != null ? &dtMin.u : null, pv != null ? &dtMin.v : null);
            Float tMax = targetMax.Evaluate(ctx, directions, pu != null ? &dtMax.u : null, pv != null ? &dtMax.v : null);
            Float result = ClampedRemap(v, sMin, sMax, tMin, tMax);
            if (sMax == sMin)
            {
                if (dfdu != null)
                {
                    *dfdu = dtMin.u;
                }
                if (dfdv != null)
                {
                    *dfdv = dtMin.v;
                }
                return result;
            }
            // The value where it is held: it moves as the limit that holds it
            Float lo = std.min(sMin, sMax), hi = std.max(sMin, sMax);
            Slope held = dv;
            Float clamped = v;
            if (v <= lo)
            {
                clamped = lo;
                held = sMin <= sMax ? dsMin : dsMax;
            }
            else if (v >= hi)
            {
                clamped = hi;
                held = sMin <= sMax ? dsMax : dsMin;
            }
            Float range = sMax - sMin, q = (clamped - sMin) / range;
            if (dfdu != null)
            {
                Float dq = ((held.u - dsMin.u) * range - (clamped - sMin) * (dsMax.u - dsMin.u)) / (range * range);
                *dfdu = dtMin.u + dq * (tMax - tMin) + q * (dtMax.u - dtMin.u);
            }
            if (dfdv != null)
            {
                Float dq = ((held.v - dsMin.v) * range - (clamped - sMin) * (dsMax.v - dsMin.v)) / (range * range);
                *dfdv = dtMin.v + dq * (tMax - tMin) + q * (dtMax.v - dtMin.v);
            }
            return result;
        }
    }

    // The nearest multiple of the increment: a whole number of increments, which moves only as the increment does
    public sealed unsafe partial class FloatRoundingTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope dt = default, di = default;
            Float t = tex.Evaluate(ctx, directions, dfdu != null ? &dt.u : null, dfdv != null ? &dt.v : null);
            Float inc = increment.Evaluate(ctx, directions, dfdu != null ? &di.u : null, dfdv != null ? &di.v : null);
            Float result = Round(t, inc);
            if (t == inc || inc == 0)
            {
                if (dfdu != null)
                {
                    *dfdu = dt.u;
                }
                if (dfdv != null)
                {
                    *dfdv = dt.v;
                }
                return result;
            }
            Float count = std.round(result / inc);
            if (dfdu != null)
            {
                *dfdu = count * di.u;
            }
            if (dfdv != null)
            {
                *dfdv = count * di.v;
            }
            return result;
        }
    }

    // tex - trunc(tex / modulo) modulo, and 0 where modulo is 0
    public sealed unsafe partial class FloatModuloTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Slope dt = default, dm = default;
            Float modulus = modulo.Evaluate(ctx, directions, dfdu != null ? &dm.u : null, dfdv != null ? &dm.v : null);
            if (modulus == 0)
            {
                if (dfdu != null)
                {
                    *dfdu = 0;
                }
                if (dfdv != null)
                {
                    *dfdv = 0;
                }
                return 0;
            }
            Float t = tex.Evaluate(ctx, directions, dfdu != null ? &dt.u : null, dfdv != null ? &dt.v : null);
            Float whole = (Float)Math.Truncate(t / modulus);
            if (dfdu != null)
            {
                *dfdu = dt.u - whole * dm.u;
            }
            if (dfdv != null)
            {
                *dfdv = dt.v - whole * dm.v;
            }
            return (Float)(t % modulus);
        }
    }

    // 0 or 1: flat on both sides of where the two textures cross
    public sealed unsafe partial class FloatCompareTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Derivatives(ctx, directions, dfdu, dfdv);
            return Evaluate(ctx);
        }

        protected override void Derivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            if (dfdu != null)
            {
                *dfdu = 0;
            }
            if (dfdv != null)
            {
                *dfdv = 0;
            }
        }
    }

    // The ramp of the value of a texture: the slope of the segment that the value is in (none for a stepwise ramp, and
    // none beyond the ends)
    public sealed unsafe partial class FloatRampTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            Float x = tex.Evaluate(ctx, directions, dfdu, dfdv);
            Float slope = 0;
            int n = positions.Length;
            if (!step && x > positions[0] && x < positions[n - 1])
            {
                int i = 0;
                while (i + 1 < n && positions[i + 1] <= x)
                {
                    ++i;
                }
                slope = (values[i + 1] - values[i]) / (positions[i + 1] - positions[i]);
            }
            if (dfdu != null)
            {
                *dfdu *= slope;
            }
            if (dfdv != null)
            {
                *dfdv *= slope;
            }
            return pbrt.RampEvaluate(positions, values, step, x);
        }
    }
}
