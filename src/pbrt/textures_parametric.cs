// (not in pbrt-v4) textures_parametric.cs
//
// The spectra of spectra_parametric.cs as spectrum textures whose parameters are float textures, so that the centre of a
// Gaussian, the coefficients of a dispersion formula or the edge of a filter vary over a surface or in space:
//
//   Texture "name" "spectrum" "gaussian"   "mean" "width" "scale" (float or texture), "float baseline"
//   Texture "name" "spectrum" "frequency"  "frequency" "phase" "scale"
//   Texture "name" "spectrum" "cauchy"     "a" "b" "c"
//   Texture "name" "spectrum" "sellmeier"  "b1" "b2" "b3" "c1" "c2" "c3"
//   Texture "name" "spectrum" "longpass"   "edge" "softness" "low" "high"   (a short-pass filter: low above high)
//
// Each has its derivatives in closed form (the chain rule over its parameters, base/texture.cs) and a bound from the bounds
// of its parameters.

using System;

namespace pbrt
{
    // A parameter of a texture at a point: its value and its change along the two directions
    internal unsafe struct TextureParameter
    {
        public Float value, du, dv;

        public static TextureParameter Read(FloatTexture texture, TextureEvalContext ctx, TextureDirections directions, bool wantU, bool wantV)
        {
            TextureParameter p = new TextureParameter();
            Float du = 0, dv = 0;
            p.value = texture.Evaluate(ctx, directions, wantU ? &du : null, wantV ? &dv : null);
            p.du = du;
            p.dv = dv;
            return p;
        }
    }

    // SpectrumGaussianTexture Definition
    public sealed unsafe class SpectrumGaussianTexture : SpectrumTexture
    {
        public SpectrumGaussianTexture(FloatTexture mean, FloatTexture width, FloatTexture scale, Float baseline)
        {
            this.mean = mean;
            this.width = width;
            this.scale = scale;
            this.baseline = baseline;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float m = mean.Evaluate(ctx), w = std.max<Float>(width.Evaluate(ctx), 1e-6f), s = scale.Evaluate(ctx);
            SampledSpectrum value = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                value[i] = GaussianSpectrum.Value(m, w, s, baseline, lambda[i]);
            }
            return value;
        }

        public override Float MaxValue() { return baseline + std.max<Float>(scale.MaxValue(), 0); }

        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            bool wantU = dfdu != null, wantV = dfdv != null;
            TextureParameter m = TextureParameter.Read(mean, ctx, directions, wantU, wantV);
            TextureParameter w = TextureParameter.Read(width, ctx, directions, wantU, wantV);
            TextureParameter s = TextureParameter.Read(scale, ctx, directions, wantU, wantV);
            bool clamped = !(w.value > 1e-6f);
            Float wv = clamped ? 1e-6f : w.value;
            SampledSpectrum value = new SampledSpectrum(0f), du = new SampledSpectrum(0f), dv = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                Float d = (lambda[i] - m.value) / wv;
                Float e = FastExp(-0.5f * d * d);
                value[i] = baseline + s.value * e;
                // d/dmean = scale e d / width, d/dwidth = scale e d^2 / width, d/dscale = e
                Float byMean = s.value * e * d / wv, byWidth = clamped ? 0 : s.value * e * d * d / wv;
                du[i] = byMean * m.du + byWidth * w.du + e * s.du;
                dv[i] = byMean * m.dv + byWidth * w.dv + e * s.dv;
            }
            if (wantU)
            {
                *dfdu = du;
            }
            if (wantV)
            {
                *dfdv = dv;
            }
            return value;
        }

        public static SpectrumGaussianTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, SpectrumType spectrumType,
                                                     FileLoc loc, Allocator alloc)
        {
            return new SpectrumGaussianTexture(parameters.GetFloatTexture("mean", 550f, alloc), parameters.GetFloatTexture("width", 30f, alloc),
                                               parameters.GetFloatTexture("scale", 1f, alloc), parameters.GetOneFloat("baseline", 0f));
        }

        public override string ToString()
        {
            return StringPrintf("[ SpectrumGaussianTexture mean: %s width: %s scale: %s baseline: %f ]", mean, width, scale, baseline);
        }

        private FloatTexture mean, width, scale;
        private Float baseline;
    }

    // SpectrumFrequencyTexture Definition
    public sealed unsafe class SpectrumFrequencyTexture : SpectrumTexture
    {
        public SpectrumFrequencyTexture(FloatTexture frequency, FloatTexture phase, FloatTexture scale)
        {
            this.frequency = frequency;
            this.phase = phase;
            this.scale = scale;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float f = frequency.Evaluate(ctx), p = phase.Evaluate(ctx), s = scale.Evaluate(ctx);
            SampledSpectrum value = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                value[i] = FrequencySpectrum.Value(f, p, s, lambda[i]);
            }
            return value;
        }

        public override Float MaxValue() { return std.max<Float>(scale.MaxValue(), 0); }

        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            bool wantU = dfdu != null, wantV = dfdv != null;
            TextureParameter f = TextureParameter.Read(frequency, ctx, directions, wantU, wantV);
            TextureParameter p = TextureParameter.Read(phase, ctx, directions, wantU, wantV);
            TextureParameter s = TextureParameter.Read(scale, ctx, directions, wantU, wantV);
            SampledSpectrum value = new SampledSpectrum(0f), du = new SampledSpectrum(0f), dv = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                Float angle = f.value * lambda[i] + p.value;
                Float wave = 0.5f * (std.sin(angle) + 1), slope = 0.5f * s.value * std.cos(angle);
                value[i] = s.value * wave;
                du[i] = slope * (lambda[i] * f.du + p.du) + wave * s.du;
                dv[i] = slope * (lambda[i] * f.dv + p.dv) + wave * s.dv;
            }
            if (wantU)
            {
                *dfdu = du;
            }
            if (wantV)
            {
                *dfdv = dv;
            }
            return value;
        }

        public static SpectrumFrequencyTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, SpectrumType spectrumType,
                                                      FileLoc loc, Allocator alloc)
        {
            return new SpectrumFrequencyTexture(parameters.GetFloatTexture("frequency", 0.03f, alloc), parameters.GetFloatTexture("phase", 0f, alloc),
                                                parameters.GetFloatTexture("scale", 1f, alloc));
        }

        public override string ToString()
        {
            return StringPrintf("[ SpectrumFrequencyTexture frequency: %s phase: %s scale: %s ]", frequency, phase, scale);
        }

        private FloatTexture frequency, phase, scale;
    }

    // SpectrumCauchyTexture Definition
    public sealed unsafe class SpectrumCauchyTexture : SpectrumTexture
    {
        public SpectrumCauchyTexture(FloatTexture A, FloatTexture B, FloatTexture C)
        {
            this.A = A;
            this.B = B;
            this.C = C;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float a = A.Evaluate(ctx), b = B.Evaluate(ctx), c = C.Evaluate(ctx);
            SampledSpectrum value = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                value[i] = CauchySpectrum.Value(a, b, c, lambda[i]);
            }
            return value;
        }

        // Each term at the end of the range where it is largest
        public override Float MaxValue()
        {
            Float shortest = Sqr(Lambda_min * 1e-3f), longest = Sqr(Lambda_max * 1e-3f);
            Float b = B.MaxValue(), c = C.MaxValue();
            return A.MaxValue() + std.max(b / shortest, b / longest) + std.max(c / Sqr(shortest), c / Sqr(longest));
        }

        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            bool wantU = dfdu != null, wantV = dfdv != null;
            TextureParameter a = TextureParameter.Read(A, ctx, directions, wantU, wantV);
            TextureParameter b = TextureParameter.Read(B, ctx, directions, wantU, wantV);
            TextureParameter c = TextureParameter.Read(C, ctx, directions, wantU, wantV);
            SampledSpectrum value = new SampledSpectrum(0f), du = new SampledSpectrum(0f), dv = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                Float l2 = Sqr(lambda[i] * 1e-3f);
                value[i] = a.value + b.value / l2 + c.value / Sqr(l2);
                du[i] = a.du + b.du / l2 + c.du / Sqr(l2);
                dv[i] = a.dv + b.dv / l2 + c.dv / Sqr(l2);
            }
            if (wantU)
            {
                *dfdu = du;
            }
            if (wantV)
            {
                *dfdv = dv;
            }
            return value;
        }

        public static SpectrumCauchyTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, SpectrumType spectrumType,
                                                   FileLoc loc, Allocator alloc)
        {
            return new SpectrumCauchyTexture(parameters.GetFloatTexture("a", 1.5f, alloc), parameters.GetFloatTexture("b", 0.004f, alloc),
                                             parameters.GetFloatTexture("c", 0f, alloc));
        }

        public override string ToString()
        {
            return StringPrintf("[ SpectrumCauchyTexture A: %s B: %s C: %s ]", A, B, C);
        }

        private FloatTexture A, B, C;
    }

    // SpectrumSellmeierTexture Definition
    public sealed unsafe class SpectrumSellmeierTexture : SpectrumTexture
    {
        public SpectrumSellmeierTexture(FloatTexture B1, FloatTexture B2, FloatTexture B3, FloatTexture C1, FloatTexture C2, FloatTexture C3)
        {
            this.B1 = B1;
            this.B2 = B2;
            this.B3 = B3;
            this.C1 = C1;
            this.C2 = C2;
            this.C3 = C3;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float b1 = B1.Evaluate(ctx), b2 = B2.Evaluate(ctx), b3 = B3.Evaluate(ctx), c1 = C1.Evaluate(ctx), c2 = C2.Evaluate(ctx), c3 = C3.Evaluate(ctx);
            SampledSpectrum value = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                value[i] = SellmeierSpectrum.Value(b1, b2, b3, c1, c2, c3, lambda[i]);
            }
            return value;
        }

        // The largest value of one term B l^2 / (l^2 - C) over the wavelengths, for a B and a C up to their largest: infinite if
        // the resonance can be in the range
        private static Float TermBound(Float b, Float c)
        {
            Float shortest = Sqr(Lambda_min * 1e-3f), longest = Sqr(Lambda_max * 1e-3f);
            if (c >= shortest && c <= longest)
            {
                return Infinity;
            }
            // (below the range the term falls with the wavelength, above it the term is negative and rises to 0 from below)
            return std.max<Float>(0, std.max(b * shortest / (shortest - c), b * longest / (longest - c)));
        }

        public override Float MaxValue()
        {
            // (a resonance anywhere up to the largest C: the nearest to the range that the texture can give)
            Float n2 = 1 + TermBound(B1.MaxValue(), C1.MaxValue()) + TermBound(B2.MaxValue(), C2.MaxValue()) + TermBound(B3.MaxValue(), C3.MaxValue());
            Float lowest = Sqr(Lambda_min * 1e-3f);
            if (C1.MinValue() < lowest && C1.MaxValue() > lowest || C2.MinValue() < lowest && C2.MaxValue() > lowest ||
                C3.MinValue() < lowest && C3.MaxValue() > lowest)
            {
                return Infinity;
            }
            return std.sqrt(n2);
        }

        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            bool wantU = dfdu != null, wantV = dfdv != null;
            TextureParameter b1 = TextureParameter.Read(B1, ctx, directions, wantU, wantV), b2 = TextureParameter.Read(B2, ctx, directions, wantU, wantV);
            TextureParameter b3 = TextureParameter.Read(B3, ctx, directions, wantU, wantV), c1 = TextureParameter.Read(C1, ctx, directions, wantU, wantV);
            TextureParameter c2 = TextureParameter.Read(C2, ctx, directions, wantU, wantV), c3 = TextureParameter.Read(C3, ctx, directions, wantU, wantV);
            SampledSpectrum value = new SampledSpectrum(0f), du = new SampledSpectrum(0f), dv = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double l2 = Sqr((double)lambda[i] * 1e-3);
                double r1 = l2 / (l2 - c1.value), r2 = l2 / (l2 - c2.value), r3 = l2 / (l2 - c3.value);
                double n2 = 1 + b1.value * r1 + b2.value * r2 + b3.value * r3;
                if (!(n2 > 0))
                {
                    continue;
                }
                double n = Math.Sqrt(n2);
                value[i] = (Float)n;
                // d(n^2)/dB = l^2 / (l^2 - C), d(n^2)/dC = B l^2 / (l^2 - C)^2; dn = d(n^2) / (2 n)
                double k1 = b1.value * r1 / (l2 - c1.value), k2 = b2.value * r2 / (l2 - c2.value), k3 = b3.value * r3 / (l2 - c3.value);
                du[i] = (Float)((r1 * b1.du + r2 * b2.du + r3 * b3.du + k1 * c1.du + k2 * c2.du + k3 * c3.du) / (2 * n));
                dv[i] = (Float)((r1 * b1.dv + r2 * b2.dv + r3 * b3.dv + k1 * c1.dv + k2 * c2.dv + k3 * c3.dv) / (2 * n));
            }
            if (wantU)
            {
                *dfdu = du;
            }
            if (wantV)
            {
                *dfdv = dv;
            }
            return value;
        }

        // (the defaults: the borosilicate crown N-BK7)
        public static SpectrumSellmeierTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, SpectrumType spectrumType,
                                                      FileLoc loc, Allocator alloc)
        {
            return new SpectrumSellmeierTexture(parameters.GetFloatTexture("b1", 1.03961212f, alloc), parameters.GetFloatTexture("b2", 0.231792344f, alloc),
                                                parameters.GetFloatTexture("b3", 1.01046945f, alloc), parameters.GetFloatTexture("c1", 0.00600069867f, alloc),
                                                parameters.GetFloatTexture("c2", 0.0200179144f, alloc), parameters.GetFloatTexture("c3", 103.560653f, alloc));
        }

        public override string ToString()
        {
            return StringPrintf("[ SpectrumSellmeierTexture B: %s %s %s C: %s %s %s ]", B1, B2, B3, C1, C2, C3);
        }

        private FloatTexture B1, B2, B3, C1, C2, C3;
    }

    // SpectrumEdgeTexture Definition
    public sealed unsafe class SpectrumEdgeTexture : SpectrumTexture
    {
        public SpectrumEdgeTexture(FloatTexture edge, FloatTexture softness, FloatTexture low, FloatTexture high)
        {
            this.edge = edge;
            this.softness = softness;
            this.low = low;
            this.high = high;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float e = edge.Evaluate(ctx), w = std.max<Float>(softness.Evaluate(ctx), 1e-6f), lo = low.Evaluate(ctx), hi = high.Evaluate(ctx);
            SampledSpectrum value = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                value[i] = EdgeSpectrum.Value(e, w, lo, hi, lambda[i]);
            }
            return value;
        }

        public override Float MaxValue() { return std.max(low.MaxValue(), high.MaxValue()); }

        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            bool wantU = dfdu != null, wantV = dfdv != null;
            TextureParameter e = TextureParameter.Read(edge, ctx, directions, wantU, wantV);
            TextureParameter w = TextureParameter.Read(softness, ctx, directions, wantU, wantV);
            TextureParameter lo = TextureParameter.Read(low, ctx, directions, wantU, wantV);
            TextureParameter hi = TextureParameter.Read(high, ctx, directions, wantU, wantV);
            bool clamped = !(w.value > 1e-6f);
            Float wv = clamped ? 1e-6f : w.value;
            SampledSpectrum value = new SampledSpectrum(0f), du = new SampledSpectrum(0f), dv = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                Float x = 4 * (lambda[i] - e.value) / wv;
                Float t = 1 / (1 + FastExp(-x));
                value[i] = lo.value + (hi.value - lo.value) * t;
                // dt/dx = t (1 - t); dx/dedge = -4 / softness, dx/dsoftness = -x / softness
                Float byX = (hi.value - lo.value) * t * (1 - t);
                Float byEdge = -4 * byX / wv, bySoftness = clamped ? 0 : -x * byX / wv;
                du[i] = byEdge * e.du + bySoftness * w.du + (1 - t) * lo.du + t * hi.du;
                dv[i] = byEdge * e.dv + bySoftness * w.dv + (1 - t) * lo.dv + t * hi.dv;
            }
            if (wantU)
            {
                *dfdu = du;
            }
            if (wantV)
            {
                *dfdv = dv;
            }
            return value;
        }

        public static SpectrumEdgeTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, SpectrumType spectrumType, FileLoc loc,
                                                 Allocator alloc)
        {
            return new SpectrumEdgeTexture(parameters.GetFloatTexture("edge", 550f, alloc), parameters.GetFloatTexture("softness", 20f, alloc),
                                           parameters.GetFloatTexture("low", 0f, alloc), parameters.GetFloatTexture("high", 1f, alloc));
        }

        public override string ToString()
        {
            return StringPrintf("[ SpectrumEdgeTexture edge: %s softness: %s low: %s high: %s ]", edge, softness, low, high);
        }

        private FloatTexture edge, softness, low, high;
    }
}
