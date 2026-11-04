// (not in pbrt-v4) texture_strand.cs
//
// The color along a strand ("ribbon" and "rocap" shapes): one RGB color for each control point of the strand, interpolated down its length with the basis of the
// geometry. The texture coordinate u of the shapes runs along the whole strand ((segment + t) / segments), so the texture needs only u.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class SpectrumStrandColorTexture : SpectrumTexture
    {
        // bspline2: the quadratic B-spline of the ribbon (n points, n - 2 segments, the ends clamped to the first and last point);
        // bezier3: the chain of cubic Bezier curves of the rocap (3n + 1 points, n segments)
        public SpectrumStrandColorTexture(RGB[] colors, bool bezier, SpectrumType spectrumType, RGBColorSpace colorSpace)
        {
            this.colors = colors;
            this.bezier = bezier;
            this.spectrumType = spectrumType;
            this.colorSpace = colorSpace;
            segments = bezier ? (colors.Length - 1) / 3 : colors.Length - 2;
            Float largest = 0;
            for (int i = 0; i < colors.Length; ++i)
            {
                largest = std.max(largest, std.max(colors[i].r, std.max(colors[i].g, colors[i].b)));
            }
            maximum = largest;
        }

        // The color at the position x in [0, 1] along the strand
        public RGB Color(Float x)
        {
            x = std.max(0f, std.min(1f, x));
            int i = Math.Min((int)(x * segments), segments - 1);
            Float t = x * segments - i;
            RGB result;
            if (bezier)
            {
                Float s = 1 - t;
                Float w0 = s * s * s, w1 = 3 * s * s * t, w2 = 3 * s * t * t, w3 = t * t * t;
                RGB c0 = colors[3 * i], c1 = colors[3 * i + 1], c2 = colors[3 * i + 2], c3 = colors[3 * i + 3];
                result = new RGB(w0 * c0.r + w1 * c1.r + w2 * c2.r + w3 * c3.r, w0 * c0.g + w1 * c1.g + w2 * c2.g + w3 * c3.g,
                                 w0 * c0.b + w1 * c1.b + w2 * c2.b + w3 * c3.b);
            }
            else
            {
                // The quadratic Bezier curve of the control points of the segment: the midpoints between the points inside, the first and last point at the ends
                RGB p0 = colors[i], p1 = colors[i + 1], p2 = colors[i + 2];
                RGB b0 = i == 0 ? p0 : new RGB(.5f * (p0.r + p1.r), .5f * (p0.g + p1.g), .5f * (p0.b + p1.b));
                RGB b2 = i == segments - 1 ? p2 : new RGB(.5f * (p1.r + p2.r), .5f * (p1.g + p2.g), .5f * (p1.b + p2.b));
                Float s = 1 - t;
                Float w0 = s * s, w1 = 2 * s * t, w2 = t * t;
                result = new RGB(w0 * b0.r + w1 * p1.r + w2 * b2.r, w0 * b0.g + w1 * p1.g + w2 * b2.g, w0 * b0.b + w1 * p1.b + w2 * b2.b);
            }
            return result;
        }

        // The colors are blended inside their hull, so the largest component of the points bounds the texture
        public override Float MaxValue() { return pbrt.RGBSpectrumBound(spectrumType, colorSpace, maximum); }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            RGB rgb = pbrt.ClampZero(Color(ctx.uv[0]));
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

        public override string ToString() { return pbrt.StringPrintf("[ SpectrumStrandColorTexture points: %d bezier: %s ]", colors.Length, bezier); }

        // "rgb Cs" (the color at each control point of the strand), "string basis" ("bspline2" for ribbon, the default, or "bezier3" for rocap)
        public static SpectrumStrandColorTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, SpectrumType spectrumType, FileLoc loc,
                                                        Allocator alloc)
        {
            List<RGB> values = parameters.GetRGBArray("Cs");
            string basis = parameters.GetOneString("basis", "bspline2");
            if (basis != "bspline2" && basis != "bezier3")
            {
                pbrt.ErrorExit(loc, "strandcolor: \"basis\" must be \"bspline2\" (ribbon) or \"bezier3\" (rocap).");
            }
            bool bezier = basis == "bezier3";
            if (bezier ? (values.Count < 4 || (values.Count - 1) % 3 != 0) : values.Count < 3)
            {
                pbrt.ErrorExit(loc, bezier ? "strandcolor: bezier3 needs 3n + 1 colors." : "strandcolor: bspline2 needs at least three colors.");
            }
            return new SpectrumStrandColorTexture(values.ToArray(), bezier, spectrumType, RGBColorSpace.sRGB);
        }

        private readonly RGB[] colors;
        private readonly bool bezier;
        private readonly SpectrumType spectrumType;
        private readonly RGBColorSpace colorSpace;
        private readonly int segments;
        private readonly Float maximum;
    }
}
