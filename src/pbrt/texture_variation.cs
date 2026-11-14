// (not in pbrt-v4) texture_variation.cs
//
// The variation of a color by object: every object of the scene (a "Shape" of the scene file, and each instance of an object instance) gets its own small random shift
// of the hue, the saturation and the value of a base color, so that a patch of grass, the leaves of a tree or the hairs of a head are not all the same color. The
// shift is a fixed function of the number of the object (SurfaceInteraction.objectId) and a seed, so it is the same for every ray and every run.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static class ObjectIds
    {
        // The number of the object of a shape of the scene file: from its place in the file (deterministic, the same in every run)
        public static int Hash(FileLoc loc)
        {
            unchecked
            {
                uint h = 2166136261;
                if (loc != null)
                {
                    if (loc.filename != null)
                    {
                        for (int i = 0; i < loc.filename.Length; ++i)
                        {
                            h = (h ^ loc.filename[i]) * 16777619;
                        }
                    }
                    h = (h ^ (uint)loc.line) * 16777619;
                    h = (h ^ (uint)loc.column) * 16777619;
                }
                return (int)h;
            }
        }

        public static int Mix(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b;
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return (int)h;
            }
        }

        // A number in [-1, 1) from the number of the object, the seed and the channel (hue, saturation, value)
        public static Float Signed(int objectId, int seed, int channel)
        {
            uint h = (uint)Mix(Mix(objectId, seed), channel + 1);
            return (Float)((h >> 8) * (1.0 / 8388608.0) - 1.0);
        }
    }

    public abstract unsafe partial class SpectrumTexture
    {
        // Whether the texture gives an RGB color (for textures that work on the color: the variation of hue, saturation and value)
        public virtual bool SupportsRGB { get { return false; } }

        public virtual bool TryEvaluateRGB(TextureEvalContext ctx, out RGB rgb)
        {
            rgb = default;
            return false;
        }
    }

    public sealed unsafe class SpectrumVariationTexture : SpectrumRGBTexture
    {
        public SpectrumVariationTexture(SpectrumTexture tex, RGB baseColor, Float hue, Float saturation, Float value, int seed, SpectrumType spectrumType,
                                        RGBColorSpace colorSpace) : base(spectrumType)
        {
            rgbTexture = new SourceRGB(this, colorSpace);
            this.tex = tex;
            this.baseColor = baseColor;
            this.hue = hue;
            this.saturation = saturation;
            this.value = value;
            this.seed = seed;
            this.spectrumType = spectrumType;
            this.colorSpace = colorSpace;
        }

        public override bool SupportsRGB { get { return true; } }

        // The shift of the base color for the object: hue in degrees, saturation and value by fractions, each by a random amount in the range
        public RGB Vary(RGB rgb, int objectId)
        {
            Float h = hue * ObjectIds.Signed(objectId, seed, 0);
            Float s = 1 + saturation * ObjectIds.Signed(objectId, seed, 1);
            Float v = 1 + value * ObjectIds.Signed(objectId, seed, 2);
            return pbrt.HSVToRGB(pbrt.RGBToHSVShift(rgb, h, s, v));
        }

        public override bool TryEvaluateRGB(TextureEvalContext ctx, out RGB rgb)
        {
            RGB source = baseColor;
            if (tex != null && !tex.TryEvaluateRGB(ctx, out source))
            {
                rgb = default;
                return false;
            }
            rgb = Vary(source, ctx.objectId);
            return true;
        }

        public override Float MaxValue()
        {
            Float upper = 1 + value;
            if (tex != null)
            {
                return tex.MaxValue() * upper;
            }
            Float m = std.max(baseColor.r, std.max(baseColor.g, baseColor.b)) * upper;
            return pbrt.RGBSpectrumBound(spectrumType, colorSpace, m);
        }

        public override string ToString() { return pbrt.StringPrintf("[ SpectrumVariationTexture hue: %f saturation: %f value: %f seed: %d ]", hue, saturation, value, seed); }

        // "texture tex" (the base color: an "imagemap", "strandcolor" or another "variation"; absent: "rgb base", white by default), "float hue" (the range of the shift in
        // degrees, +-), "float saturation" and "float value" (the ranges, as fractions of the base: 0.2 is +-20%), "integer seed"
        public static SpectrumVariationTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, SpectrumType spectrumType, FileLoc loc,
                                                      Allocator alloc)
        {
            SpectrumTexture tex = parameters.GetSpectrumTextureOrNull("tex", spectrumType, alloc);
            if (tex != null && !tex.SupportsRGB)
            {
                pbrt.ErrorExit(loc, "variation: \"tex\" must be a texture that gives a color (\"imagemap\", \"strandcolor\" or \"variation\"); use \"rgb base\" for a constant color.");
            }
            List<RGB> baseColors = parameters.GetRGBArray("base");
            RGB baseColor = baseColors.Count > 0 ? baseColors[0] : new RGB(1, 1, 1);
            return new SpectrumVariationTexture(tex, baseColor, parameters.GetOneFloat("hue", 0f), parameters.GetOneFloat("saturation", 0f), parameters.GetOneFloat("value", 0f),
                                                parameters.GetOneInt("seed", 0), spectrumType, RGBColorSpace.sRGB);
        }

        private readonly SpectrumTexture tex;
        private readonly RGB baseColor;
        private readonly Float hue, saturation, value;
        private readonly int seed;
        private readonly SpectrumType spectrumType;
        private readonly RGBColorSpace colorSpace;
    }

    public static unsafe partial class pbrt
    {
        // The color with the hue shifted by hueDegrees and the saturation and value scaled, as (h in [0, 6), s, v); the inverse is HSVToRGB
        public static Vector3f RGBToHSVShift(RGB rgb, Float hueDegrees, Float saturationScale, Float valueScale)
        {
            Float r = rgb.r, g = rgb.g, b = rgb.b;
            Float max = std.max(r, std.max(g, b)), min = std.min(r, std.min(g, b));
            Float delta = max - min;
            Float h = 0;
            if (delta > 0)
            {
                if (max == r)
                {
                    h = (g - b) / delta;
                }
                else if (max == g)
                {
                    h = 2 + (b - r) / delta;
                }
                else
                {
                    h = 4 + (r - g) / delta;
                }
                if (h < 0)
                {
                    h += 6;
                }
            }
            Float s = max > 0 ? delta / max : 0;
            h += hueDegrees / 60;
            h -= 6 * (Float)Math.Floor(h / 6);
            s = std.max(0f, std.min(1f, s * saturationScale));
            return new Vector3f(h, s, max * valueScale);
        }

        public static RGB HSVToRGB(Vector3f hsv)
        {
            Float h = hsv.x, s = hsv.y, v = hsv.z;
            int sector = (int)Math.Floor(h);
            Float f = h - sector;
            Float p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            switch (((sector % 6) + 6) % 6)
            {
                case 0:
                    return new RGB(v, t, p);
                case 1:
                    return new RGB(q, v, p);
                case 2:
                    return new RGB(p, v, t);
                case 3:
                    return new RGB(p, q, v);
                case 4:
                    return new RGB(t, p, v);
                default:
                    return new RGB(v, p, q);
            }
        }
    }
}
