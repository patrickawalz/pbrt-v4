// (not in pbrt-v4) RGB textures, as LuxRender had colour textures beside its float textures.
//
// A texture that computes a colour (an image, a ramp of colours, a spline of colours) is an RGBTexture: it returns the
// RGB colour and its derivatives, exactly. A spectrum comes from it through one adapter, SpectrumRGBTexture, which
// upsamples the colour (RGBAlbedoSpectrum, RGBUnboundedSpectrum or RGBIlluminantSpectrum, by the spectrum type) and
// carries the derivatives through the upsampling in closed form (RGBUpsampling.Derivative): the sigmoid of a polynomial
// whose coefficients are a trilinear interpolation of a table.
//
// The spectrum textures that are a colour underneath derive from the adapter and hand it their RGBTexture: the
// classes, their constructors and the scene syntax are what they were.

using System;

namespace pbrt
{
    // RGBTexture Definition
    public abstract unsafe class RGBTexture
    {
        public abstract RGB Evaluate(TextureEvalContext ctx);
        public abstract RGBColorSpace ColorSpace();
        public abstract override string ToString();

        // The derivatives along two directions, as FloatTexture has them (base/texture.cs)
        public bool finiteDifferences = false;

        public RGB Evaluate(TextureEvalContext ctx, TextureDirections directions, RGB* dfdu, RGB* dfdv)
        {
            if (dfdu == null && dfdv == null)
            {
                return Evaluate(ctx);
            }
            if (finiteDifferences || pbrt.GetOptions().finiteTextureDerivatives)
            {
                RGB value = Evaluate(ctx);
                FiniteDifferences(ctx, value, directions, dfdu, dfdv);
                return value;
            }
            return ValueAndDerivatives(ctx, directions, dfdu, dfdv);
        }

        public virtual bool HasAnalyticDerivatives()
        {
            return false;
        }

        protected virtual RGB ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, RGB* dfdu, RGB* dfdv)
        {
            RGB value = Evaluate(ctx);
            FiniteDifferences(ctx, value, directions, dfdu, dfdv);
            return value;
        }

        protected void FiniteDifferences(TextureEvalContext ctx, RGB value, TextureDirections directions, RGB* dfdu, RGB* dfdv)
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

    // RGBUpsampling Definition
    // A colour to the spectrum of a texture, as the spectrum textures of pbrt do it, and the change of that spectrum
    // when the colour moves
    public static unsafe class RGBUpsampling
    {
        public static SampledSpectrum Convert(RGB rgb, RGBColorSpace colorSpace, SpectrumType spectrumType, SampledWavelengths lambda)
        {
            rgb = pbrt.ClampZero(rgb);
            if (colorSpace == null)
            {
                // The one channel of an image without colours: the same at every wavelength
                return new SampledSpectrum(rgb[0]);
            }
            if (spectrumType == SpectrumType.Unbounded)
            {
                return new RGBUnboundedSpectrum(colorSpace, rgb).Sample(lambda);
            }
            if (spectrumType == SpectrumType.Albedo)
            {
                return new RGBAlbedoSpectrum(colorSpace, pbrt.Clamp(rgb, 0, 1)).Sample(lambda);
            }
            return new RGBIlluminantSpectrum(colorSpace, rgb).Sample(lambda);
        }

        // The change of Convert(rgb) per unit of a step that changes the colour by drgb
        public static SampledSpectrum Derivative(RGB rgb, RGB drgb, RGBColorSpace colorSpace, SpectrumType spectrumType, SampledWavelengths lambda)
        {
            if (colorSpace == null)
            {
                return new SampledSpectrum(rgb[0] > 0 ? drgb[0] : 0);
            }
            // A channel held at a limit does not move
            Float upper = spectrumType == SpectrumType.Albedo ? 1 : Infinity;
            for (int c = 0; c < 3; ++c)
            {
                if (!(rgb[c] > 0 && rgb[c] < upper))
                {
                    drgb[c] = 0;
                }
            }
            rgb = pbrt.ClampZero(rgb);
            SampledSpectrum result = new SampledSpectrum(0f);
            double dc0, dc1, dc2;
            if (spectrumType == SpectrumType.Albedo)
            {
                rgb = pbrt.Clamp(rgb, 0, 1);
                RGBSigmoidPolynomial polynomial = colorSpace.ToRGBCoeffs(rgb);
                colorSpace.ToRGBCoeffsDerivative(rgb, drgb, &dc0, &dc1, &dc2);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    result[i] = polynomial.EvaluateDerivative(lambda[i], dc0, dc1, dc2);
                }
                return result;
            }
            // scale s(rgb / scale), scale twice the largest channel (times the illuminant for an illuminant)
            int largest = (rgb[0] > rgb[1]) ? ((rgb[0] > rgb[2]) ? 0 : 2) : ((rgb[1] > rgb[2]) ? 1 : 2);
            Float scale = 2 * rgb[largest];
            if (!(scale > 0))
            {
                return result;
            }
            Float dscale = 2 * drgb[largest];
            RGB scaled = rgb / scale;
            RGB dscaled = (drgb * scale - rgb * dscale) / (scale * scale);
            RGBSigmoidPolynomial rsp = colorSpace.ToRGBCoeffs(scaled);
            colorSpace.ToRGBCoeffsDerivative(scaled, dscaled, &dc0, &dc1, &dc2);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                result[i] = dscale * rsp.Evaluate(lambda[i]) + scale * rsp.EvaluateDerivative(lambda[i], dc0, dc1, dc2);
            }
            if (spectrumType != SpectrumType.Unbounded)
            {
                result *= colorSpace.illuminant.Sample(lambda);
            }
            return result;
        }
    }

    // SpectrumRGBTexture Definition
    // The spectrum of an RGB texture
    public abstract unsafe class SpectrumRGBTexture : SpectrumTexture
    {
        protected SpectrumRGBTexture(SpectrumType spectrumType)
        {
            conversionType = spectrumType;
        }

        // The colour underneath; a class sets it in its constructor
        protected RGBTexture rgbTexture;

        public RGBTexture RGB()
        {
            return rgbTexture;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            return RGBUpsampling.Convert(rgbTexture.Evaluate(ctx), rgbTexture.ColorSpace(), conversionType, lambda);
        }

        public override bool HasAnalyticDerivatives()
        {
            return rgbTexture.HasAnalyticDerivatives();
        }

        // The derivatives of the colour, exact where the RGB texture has them so, through the upsampling
        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            RGB du = default, dv = default;
            RGB rgb = rgbTexture.Evaluate(ctx, directions, dfdu != null ? &du : null, dfdv != null ? &dv : null);
            RGBColorSpace colorSpace = rgbTexture.ColorSpace();
            if (dfdu != null)
            {
                *dfdu = RGBUpsampling.Derivative(rgb, du, colorSpace, conversionType, lambda);
            }
            if (dfdv != null)
            {
                *dfdv = RGBUpsampling.Derivative(rgb, dv, colorSpace, conversionType, lambda);
            }
            return RGBUpsampling.Convert(rgb, colorSpace, conversionType, lambda);
        }

        protected SpectrumType conversionType;
    }
}

namespace pbrt
{
    public sealed unsafe partial class MarbleTexture
    {
        // The colour of the marble: a spline of colours along sin(y + variation FBm)
        private sealed class MarbleRGB : RGBTexture
        {
            public MarbleRGB(MarbleTexture owner)
            {
                this.owner = owner;
            }

            public override RGBColorSpace ColorSpace() { return RGBColorSpace.sRGB; }
            public override bool HasAnalyticDerivatives() { return true; }
            public override string ToString() { return "[ MarbleRGB ]"; }

            public override RGB Evaluate(TextureEvalContext ctx)
            {
                return ValueAndDerivatives(ctx, default, null, null);
            }

            protected override RGB ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, RGB* dfdu, RGB* dfdv)
            {
                TexCoord3D c = owner.mapping.Map(ctx);
                c.p *= owner.scale;
                Vector3f gradient;
                Float marble = c.p.y + owner.variation * pbrt.FBm(c.p, owner.scale * c.dpdx, owner.scale * c.dpdy, owner.omega, owner.octaves, &gradient);
                Float t = .5f + .5f * std.sin(marble);
                // Evaluate marble spline at $t$ to compute color _rgb_
                int nSeg = colors.Length - 3;
                int first = std.min<int>((int)std.floor(t * nSeg), nSeg - 1);
                Float segment = t * nSeg - first;
                Float[] ch = new Float[4];
                RGB rgb = new RGB(), slope = new RGB();
                for (int k = 0; k < 3; ++k)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        ch[i] = colors[first + i][k];
                    }
                    rgb[k] = 1.5f * pbrt.EvaluateCubicBezier(new pstd.span<Float>(ch), segment);
                    // The derivative of the cubic Bezier curve by its parameter
                    Float one = 1 - segment;
                    slope[k] = 1.5f * 3 * (one * one * (ch[1] - ch[0]) + 2 * one * segment * (ch[2] - ch[1]) + segment * segment * (ch[3] - ch[2]));
                }
                if (dfdu != null || dfdv != null)
                {
                    // colour by t, t by the marble function, the marble function by the mapped point
                    RGB byMarble = slope * (nSeg * .5f * std.cos(marble));
                    Vector3f byPoint = owner.scale * (new Vector3f(0, 1, 0) + owner.variation * gradient);
                    Vector3f dpdu = default, dpdv = default;
                    owner.mapping.MapDerivatives(ctx, directions, dfdu != null ? &dpdu : null, dfdv != null ? &dpdv : null);
                    if (dfdu != null)
                    {
                        *dfdu = byMarble * pbrt.Dot(byPoint, dpdu);
                    }
                    if (dfdv != null)
                    {
                        *dfdv = byMarble * pbrt.Dot(byPoint, dpdv);
                    }
                }
                return rgb;
            }

            private MarbleTexture owner;
        }
    }

    public sealed unsafe partial class SpectrumRampTexture
    {
        // The colour of the ramp: three ramps of the value of a float texture
        private sealed class RampRGB : RGBTexture
        {
            public RampRGB(SpectrumRampTexture owner)
            {
                this.owner = owner;
            }

            public override RGBColorSpace ColorSpace() { return owner.colorSpace; }
            public override bool HasAnalyticDerivatives() { return true; }
            public override string ToString() { return "[ RampRGB ]"; }

            public override RGB Evaluate(TextureEvalContext ctx)
            {
                Float x = owner.tex.Evaluate(ctx);
                return new RGB(pbrt.RampEvaluate(owner.positions, owner.channels[0], owner.step, x),
                               pbrt.RampEvaluate(owner.positions, owner.channels[1], owner.step, x),
                               pbrt.RampEvaluate(owner.positions, owner.channels[2], owner.step, x));
            }

            protected override RGB ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, RGB* dfdu, RGB* dfdv)
            {
                Float dxdu = 0, dxdv = 0;
                Float x = owner.tex.Evaluate(ctx, directions, dfdu != null ? &dxdu : null, dfdv != null ? &dxdv : null);
                // The slope of the segment of the ramp that the value is in
                RGB slope = new RGB(0, 0, 0);
                Float[] positions = owner.positions;
                int n = positions.Length;
                if (!owner.step && x > positions[0] && x < positions[n - 1])
                {
                    int i = 0;
                    while (i + 1 < n && positions[i + 1] <= x)
                    {
                        ++i;
                    }
                    Float width = positions[i + 1] - positions[i];
                    slope = new RGB((owner.channels[0][i + 1] - owner.channels[0][i]) / width, (owner.channels[1][i + 1] - owner.channels[1][i]) / width,
                                    (owner.channels[2][i + 1] - owner.channels[2][i]) / width);
                }
                if (dfdu != null)
                {
                    *dfdu = slope * dxdu;
                }
                if (dfdv != null)
                {
                    *dfdv = slope * dxdv;
                }
                return new RGB(pbrt.RampEvaluate(positions, owner.channels[0], owner.step, x), pbrt.RampEvaluate(positions, owner.channels[1], owner.step, x),
                               pbrt.RampEvaluate(positions, owner.channels[2], owner.step, x));
            }

            private SpectrumRampTexture owner;
        }
    }
}

namespace pbrt
{
    // The colour of a spectrum texture that computes one in TryEvaluateRGB(): differentiated by finite differences in
    // RGB (the upsampling after it is still exact)
    internal sealed unsafe class SourceRGB : RGBTexture
    {
        public SourceRGB(SpectrumTexture owner, RGBColorSpace colorSpace)
        {
            this.owner = owner;
            this.colorSpace = colorSpace;
        }

        public override RGBColorSpace ColorSpace() { return colorSpace; }
        public override string ToString() { return "[ SourceRGB ]"; }

        public override RGB Evaluate(TextureEvalContext ctx)
        {
            owner.TryEvaluateRGB(ctx, out RGB rgb);
            return rgb;
        }

        private SpectrumTexture owner;
        private RGBColorSpace colorSpace;
    }

    public sealed unsafe partial class SpectrumImageTexture
    {
        // The colour of the image: the filtered texels, with the slope of the filter in closed form (MIPMap.FilterSlope)
        private sealed class ImageRGB : RGBTexture
        {
            public ImageRGB(SpectrumImageTexture owner)
            {
                this.owner = owner;
            }

            public override RGBColorSpace ColorSpace() { return owner.mipmap.GetRGBColorSpace(); }
            public override bool HasAnalyticDerivatives() { return owner.mipmap.FilterSlope(new Point2f(0, 0), default, default, (RGB*)null, null); }
            public override string ToString() { return "[ ImageRGB ]"; }

            public override RGB Evaluate(TextureEvalContext ctx)
            {
                TexCoord2D c = owner.mapping.Map(ctx);
                c.st.y = 1 - c.st.y;
                RGB rgb = owner.scale * owner.mipmap.Filter<RGB>(c.st, new Vector2f(c.dsdx, c.dtdx), new Vector2f(c.dsdy, c.dtdy));
                return pbrt.ClampZero(owner.invert ? (new RGB(1, 1, 1) - rgb) : rgb);
            }

            protected override RGB ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, RGB* dfdu, RGB* dfdv)
            {
                TexCoord2D c = owner.mapping.Map(ctx);
                c.st.y = 1 - c.st.y;
                RGB bys, byt;
                if (!owner.mipmap.FilterSlope(c.st, new Vector2f(c.dsdx, c.dtdx), new Vector2f(c.dsdy, c.dtdy), &bys, &byt))
                {
                    return base.ValueAndDerivatives(ctx, directions, dfdu, dfdv);
                }
                RGB rgb = owner.scale * owner.mipmap.Filter<RGB>(c.st, new Vector2f(c.dsdx, c.dtdx), new Vector2f(c.dsdy, c.dtdy));
                // The image has t downwards; inverted, the colour is one minus the texel
                Float sign = owner.invert ? -owner.scale : owner.scale;
                bys = bys * sign;
                byt = byt * -sign;
                rgb = owner.invert ? (new RGB(1, 1, 1) - rgb) : rgb;
                for (int k = 0; k < 3; ++k)
                {
                    if (!(rgb[k] > 0))
                    {
                        bys[k] = 0;
                        byt[k] = 0;
                    }
                }
                Vector2f dstdu = default, dstdv = default;
                owner.mapping.MapDerivatives(ctx, directions, dfdu != null ? &dstdu : null, dfdv != null ? &dstdv : null);
                if (dfdu != null)
                {
                    *dfdu = bys * dstdu.x + byt * dstdu.y;
                }
                if (dfdv != null)
                {
                    *dfdv = bys * dstdv.x + byt * dstdv.y;
                }
                return pbrt.ClampZero(rgb);
            }

            private SpectrumImageTexture owner;
        }
    }

    public sealed unsafe partial class FloatImageTexture
    {
        public override bool HasAnalyticDerivatives() { return mipmap.FilterSlope(new Point2f(0, 0), default, default, (Float*)null, null); }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            TexCoord2D c = mapping.Map(ctx);
            c.st.y = 1 - c.st.y;
            Float bys, byt;
            if (!mipmap.FilterSlope(c.st, new Vector2f(c.dsdx, c.dtdx), new Vector2f(c.dsdy, c.dtdy), &bys, &byt))
            {
                return base.ValueAndDerivatives(ctx, directions, dfdu, dfdv);
            }
            Float v = scale * mipmap.Filter<Float>(c.st, new Vector2f(c.dsdx, c.dtdx), new Vector2f(c.dsdy, c.dtdy));
            // The image has t downwards; inverted, the value is one minus the texel, held at zero
            Float sign = invert ? (1 - v > 0 ? -scale : 0) : scale;
            bys *= sign;
            byt *= -sign;
            Vector2f dstdu = default, dstdv = default;
            mapping.MapDerivatives(ctx, directions, dfdu != null ? &dstdu : null, dfdv != null ? &dstdv : null);
            if (dfdu != null)
            {
                *dfdu = bys * dstdu.x + byt * dstdu.y;
            }
            if (dfdv != null)
            {
                *dfdv = bys * dstdv.x + byt * dstdv.y;
            }
            return invert ? std.max<Float>(0, 1 - v) : v;
        }
    }

    public sealed unsafe partial class FloatGridTexture
    {
        // Voxel values: the trilinear interpolation, a polynomial of the point (grids of nodes take the difference)
        public override bool HasAnalyticDerivatives() { return !nodes; }

        protected override Float ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, Float* dfdu, Float* dfdv)
        {
            if (nodes)
            {
                return base.ValueAndDerivatives(ctx, directions, dfdu, dfdv);
            }
            Point3f p = mapping.Map(ctx).p;
            Float fx = p.x * nx - .5f, fy = p.y * ny - .5f, fz = p.z * nz - .5f;
            int ix = (int)std.floor(fx), iy = (int)std.floor(fy), iz = (int)std.floor(fz);
            Float dx = fx - ix, dy = fy - iy, dz = fz - iz;
            Float v000 = Lookup(ix, iy, iz), v100 = Lookup(ix + 1, iy, iz), v010 = Lookup(ix, iy + 1, iz), v110 = Lookup(ix + 1, iy + 1, iz);
            Float v001 = Lookup(ix, iy, iz + 1), v101 = Lookup(ix + 1, iy, iz + 1), v011 = Lookup(ix, iy + 1, iz + 1), v111 = Lookup(ix + 1, iy + 1, iz + 1);
            Float d00 = Lerp(dx, v000, v100), d10 = Lerp(dx, v010, v110), d01 = Lerp(dx, v001, v101), d11 = Lerp(dx, v011, v111);
            Vector3f gradient = new Vector3f(nx * Lerp(dz, Lerp(dy, v100 - v000, v110 - v010), Lerp(dy, v101 - v001, v111 - v011)),
                                             ny * Lerp(dz, d10 - d00, d11 - d01), nz * (Lerp(dy, d01, d11) - Lerp(dy, d00, d10)));
            Vector3f dpdu = default, dpdv = default;
            mapping.MapDerivatives(ctx, directions, dfdu != null ? &dpdu : null, dfdv != null ? &dpdv : null);
            if (dfdu != null)
            {
                *dfdu = pbrt.Dot(gradient, dpdu);
            }
            if (dfdv != null)
            {
                *dfdv = pbrt.Dot(gradient, dpdv);
            }
            return Lerp(dz, Lerp(dy, d00, d10), Lerp(dy, d01, d11));
        }
    }

    public sealed unsafe partial class SpectrumGridTexture
    {
        // The colour of the grid: a float grid for each channel
        private sealed class GridRGB : RGBTexture
        {
            public GridRGB(SpectrumGridTexture owner)
            {
                this.owner = owner;
            }

            public override RGBColorSpace ColorSpace() { return owner.colorSpace; }
            public override string ToString() { return "[ GridRGB ]"; }

            public override bool HasAnalyticDerivatives()
            {
                return owner.channels[0].HasAnalyticDerivatives() && owner.channels[1].HasAnalyticDerivatives() &&
                       owner.channels[2].HasAnalyticDerivatives();
            }

            public override RGB Evaluate(TextureEvalContext ctx)
            {
                return new RGB(owner.channels[0].Evaluate(ctx), owner.channels[1].Evaluate(ctx), owner.channels[2].Evaluate(ctx));
            }

            protected override RGB ValueAndDerivatives(TextureEvalContext ctx, TextureDirections directions, RGB* dfdu, RGB* dfdv)
            {
                RGB rgb = new RGB(), du = new RGB(), dv = new RGB();
                for (int k = 0; k < 3; ++k)
                {
                    Float channelU = 0, channelV = 0;
                    rgb[k] = owner.channels[k].Evaluate(ctx, directions, dfdu != null ? &channelU : null, dfdv != null ? &channelV : null);
                    du[k] = channelU;
                    dv[k] = channelV;
                }
                if (dfdu != null)
                {
                    *dfdu = du;
                }
                if (dfdv != null)
                {
                    *dfdv = dv;
                }
                return rgb;
            }

            private SpectrumGridTexture owner;
        }
    }

    // Planck's law at the temperature that a float texture gives: its change with the temperature in closed form
    public sealed unsafe partial class SpectrumBlackbodyTexture
    {
        public override bool HasAnalyticDerivatives() { return true; }

        protected override SampledSpectrum ValueAndDerivatives(TextureEvalContext ctx, SampledWavelengths lambda, TextureDirections directions,
                                                               SampledSpectrum* dfdu, SampledSpectrum* dfdv)
        {
            Float dvdu = 0, dvdv = 0;
            Float T = Temperature(tex.Evaluate(ctx, directions, dfdu != null ? &dvdu : null, dfdv != null ? &dvdv : null));
            SampledSpectrum value = new SampledSpectrum(0f), byTemperature = new SampledSpectrum(0f);
            if (T > minimumTemperature)
            {
                value = physical ? new SampledSpectrum(0f) : new BlackbodySpectrum(T).Sample(lambda);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    if (physical)
                    {
                        value[i] = pbrt.Blackbody(lambda[i], T) * referenceNormalization;
                    }
                    // B = a / (lambda^5 (e^x - 1)), x = h c / (lambda k T): dB/dT = B x e^x / (T (e^x - 1)). Divided by its
                    // peak, which grows as T^5 (Wien), the normalised one loses 5 B / T of it.
                    double x = 6.62606957e-34 * 299792458.0 / (lambda[i] * 1e-9 * 1.3806488e-23 * T);
                    double grow = x / (1 - Math.Exp(-x));
                    byTemperature[i] = (Float)(value[i] * (physical ? grow : grow - 5) / T);
                }
            }
            if (dfdu != null)
            {
                *dfdu = byTemperature * (temperatureScale * dvdu);
            }
            if (dfdv != null)
            {
                *dfdv = byTemperature * (temperatureScale * dvdv);
            }
            return value;
        }
    }
}
