// (not in pbrt-v4) texture_strand.cs
//
// The color along a strand ("ribbon" and "rocap" shapes): one RGB color for each control point of the strand, interpolated down its length with the basis of the
// geometry. The texture coordinate u of the shapes runs along the whole strand ((segment + t) / segments), so the texture needs only u.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // The colors of the strands of the shapes ("rgb Cs" of "ribbon" and "rocap"): a shape registers its colors and gives the number as the face index of its hits, so that
    // the texture finds them from the evaluation context (no member of the contexts is added). 0 is the number of a strand without colors.
    public static class StrandColors
    {
        public static int Register(RGB[] colors, bool bezier)
        {
            lock (entries)
            {
                entries.Add(new Entry { colors = colors, bezier = bezier });
                for (int i = 0; i < colors.Length; ++i)
                {
                    largest = std.max(largest, std.max(colors[i].r, std.max(colors[i].g, colors[i].b)));
                }
                return entries.Count;
            }
        }

        public static bool TryColor(int number, Float x, out RGB color)
        {
            color = default;
            Entry entry;
            lock (entries)
            {
                if (number < 1 || number > entries.Count)
                {
                    return false;
                }
                entry = entries[number - 1];
            }
            color = Evaluate(entry.colors, entry.bezier, x);
            return true;
        }

        // The largest component of the colors of all the strands
        public static Float Largest
        {
            get
            {
                lock (entries)
                {
                    return largest;
                }
            }
        }

        // The color at the position x in [0, 1] along a strand: one color is constant; bezier: a chain of cubic Bezier curves (3n + 1 colors); otherwise the quadratic
        // B-spline of the ribbon (the ends clamped to the first and last color, n - 2 segments)
        public static RGB Evaluate(RGB[] colors, bool bezier, Float x)
        {
            if (colors.Length == 1)
            {
                return colors[0];
            }
            int segments = bezier ? (colors.Length - 1) / 3 : colors.Length - 2;
            x = std.max(0f, std.min(1f, x));
            int i = Math.Min((int)(x * segments), segments - 1);
            Float t = x * segments - i;
            if (bezier)
            {
                Float s = 1 - t;
                Float w0 = s * s * s, w1 = 3 * s * s * t, w2 = 3 * s * t * t, w3 = t * t * t;
                RGB c0 = colors[3 * i], c1 = colors[3 * i + 1], c2 = colors[3 * i + 2], c3 = colors[3 * i + 3];
                return new RGB(w0 * c0.r + w1 * c1.r + w2 * c2.r + w3 * c3.r, w0 * c0.g + w1 * c1.g + w2 * c2.g + w3 * c3.g,
                               w0 * c0.b + w1 * c1.b + w2 * c2.b + w3 * c3.b);
            }
            RGB p0 = colors[i], p1 = colors[i + 1], p2 = colors[i + 2];
            RGB b0 = i == 0 ? p0 : new RGB(.5f * (p0.r + p1.r), .5f * (p0.g + p1.g), .5f * (p0.b + p1.b));
            RGB b2 = i == segments - 1 ? p2 : new RGB(.5f * (p1.r + p2.r), .5f * (p1.g + p2.g), .5f * (p1.b + p2.b));
            Float u = 1 - t;
            Float v0 = u * u, v1 = 2 * u * t, v2 = t * t;
            return new RGB(v0 * b0.r + v1 * p1.r + v2 * b2.r, v0 * b0.g + v1 * p1.g + v2 * b2.g, v0 * b0.b + v1 * p1.b + v2 * b2.b);
        }

        private struct Entry
        {
            public RGB[] colors;
            public bool bezier;
        }

        private static readonly List<Entry> entries = new List<Entry>();
        private static Float largest;
    }

    public sealed unsafe class SpectrumStrandColorTexture : SpectrumTexture
    {
        // global: the colors of the texture along every strand that uses it (null: none); local: the colors of the strand of the hit ("rgb Cs" of its shape), multiplied
        // with the global ones when both are present. bezier is the basis of the global colors: the chain of cubic Bezier curves of the rocap (3n + 1 points), or
        // the quadratic B-spline of the ribbon (n points, n - 2 segments, the ends clamped).
        public SpectrumStrandColorTexture(RGB[] global, bool bezier, bool local, SpectrumType spectrumType, RGBColorSpace colorSpace)
        {
            this.global = global;
            this.bezier = bezier;
            this.local = local;
            this.spectrumType = spectrumType;
            this.colorSpace = colorSpace;
            globalLargest = 1;
            if (global != null)
            {
                globalLargest = 0;
                for (int i = 0; i < global.Length; ++i)
                {
                    globalLargest = std.max(globalLargest, std.max(global[i].r, std.max(global[i].g, global[i].b)));
                }
            }
        }

        // The color at the position x in [0, 1] along the strand of the face index (the number of the registered colors of the shape)
        public RGB Color(Float x, int faceIndex)
        {
            RGB result = global != null ? StrandColors.Evaluate(global, bezier, x) : new RGB(1, 1, 1);
            if (local && StrandColors.TryColor(faceIndex, x, out RGB own))
            {
                result = new RGB(result.r * own.r, result.g * own.g, result.b * own.b);
            }
            return result;
        }

        // The colors are blended inside their hull, so the largest component bounds the texture
        public override Float MaxValue()
        {
            Float m = globalLargest * (local ? StrandColors.Largest : 1);
            return pbrt.RGBSpectrumBound(spectrumType, colorSpace, m);
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            RGB rgb = pbrt.ClampZero(Color(ctx.uv[0], ctx.faceIndex));
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

        public override string ToString() { return pbrt.StringPrintf("[ SpectrumStrandColorTexture global: %s local: %s ]", global != null, local); }

        // "rgb Cs" (the global colors: one for each control point of the strand), "string basis" ("bspline2" for ribbon, the default, or "bezier3" for rocap),
        // "bool local" (the colors of the shape of the hit, "rgb Cs" of "ribbon" or "rocap"; the default when the texture has no "Cs"; with both, the product)
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
            bool local = parameters.GetOneBool("local", values.Count == 0);
            if (values.Count == 0 && !local)
            {
                pbrt.ErrorExit(loc, "strandcolor: needs \"rgb Cs\" or \"bool local\" true.");
            }
            if (values.Count > 1 && (bezier ? (values.Count < 4 || (values.Count - 1) % 3 != 0) : values.Count < 3))
            {
                pbrt.ErrorExit(loc, bezier ? "strandcolor: bezier3 needs 3n + 1 colors." : "strandcolor: bspline2 needs at least three colors.");
            }
            return new SpectrumStrandColorTexture(values.Count > 0 ? values.ToArray() : null, bezier, local, spectrumType, RGBColorSpace.sRGB);
        }

        private readonly RGB[] global;
        private readonly bool bezier;
        private readonly bool local;
        private readonly SpectrumType spectrumType;
        private readonly RGBColorSpace colorSpace;
        private readonly Float globalLargest;
    }
}
