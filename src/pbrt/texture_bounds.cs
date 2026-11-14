// (not in pbrt-v4) Bounds of the textures over a region of points, in closed form, for the textures that had only their
// bounds over everything (FloatTexture.RegionBounds(), SpectrumTexture.RegionMax() in base/texture.cs; the others are
// beside their textures). A region is a box of the points of the contexts, with no size of a pixel and no texture
// coordinates: what a medium asks. A 2D mapping says which rectangle of (s, t) the box maps to (RegionST()), a 3D
// mapping maps its corners.

using System;

namespace pbrt
{
    internal static unsafe class TextureRegion
    {
        // The box of the mapped corners of a region
        public static Bounds3f Mapped(TextureMapping3D mapping, Bounds3f region)
        {
            Bounds3f mapped = new Bounds3f();
            for (int i = 0; i < 8; ++i)
            {
                TextureEvalContext ctx = new TextureEvalContext();
                ctx.p = region.Corner(i);
                TexCoord3D c = mapping.Map(ctx);
                mapped = (i == 0) ? new Bounds3f(c.p) : pbrt.Union(mapped, c.p);
            }
            return mapped;
        }

        // Which of the two textures of a checkerboard the whole region is in (0 or 1), or -1 if it meets an edge
        public static int Check(TextureMapping2D map2D, TextureMapping3D map3D, Bounds3f region)
        {
            if (map2D != null)
            {
                if (!map2D.RegionST(region, out Bounds2f st))
                {
                    return -1;
                }
                Float s0 = std.floor(st.pMin.x), t0 = std.floor(st.pMin.y);
                if (s0 != std.floor(st.pMax.x) || t0 != std.floor(st.pMax.y))
                {
                    return -1;
                }
                return (((int)s0 + (int)t0) & 1) == 0 ? 0 : 1;
            }
            Bounds3f mapped = Mapped(map3D, region);
            Float x = std.floor(mapped.pMin.x), y = std.floor(mapped.pMin.y), z = std.floor(mapped.pMin.z);
            if (x != std.floor(mapped.pMax.x) || y != std.floor(mapped.pMax.y) || z != std.floor(mapped.pMax.z))
            {
                return -1;
            }
            return (((int)x + (int)y + (int)z) & 1) == 0 ? 0 : 1;
        }
    }

    // Bilinear in (s, t): over a rectangle its extremes are at the corners
    public sealed unsafe partial class FloatBilerpTexture
    {
        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            if (!mapping.RegionST(region, out Bounds2f st))
            {
                lo = MinValue();
                hi = MaxValue();
                return;
            }
            lo = Infinity;
            hi = -Infinity;
            for (int i = 0; i < 4; ++i)
            {
                Float s = (i & 1) == 0 ? st.pMin.x : st.pMax.x, t = (i & 2) == 0 ? st.pMin.y : st.pMax.y;
                Float v = (1 - s) * (1 - t) * v00 + s * (1 - t) * v10 + (1 - s) * t * v01 + s * t * v11;
                lo = std.min(lo, v);
                hi = std.max(hi, v);
            }
        }
    }

    public sealed unsafe partial class SpectrumBilerpTexture
    {
        // Inside the unit square the weights of the four spectra are not negative: the same blend of their largest values
        public override Float RegionMax(Bounds3f region)
        {
            if (!mapping.RegionST(region, out Bounds2f st) || st.pMin.x < 0 || st.pMin.y < 0 || st.pMax.x > 1 || st.pMax.y > 1)
            {
                return MaxValue();
            }
            Float m00 = v00.MaxValue(), m01 = v01.MaxValue(), m10 = v10.MaxValue(), m11 = v11.MaxValue();
            Float hi = 0;
            for (int i = 0; i < 4; ++i)
            {
                Float s = (i & 1) == 0 ? st.pMin.x : st.pMax.x, t = (i & 2) == 0 ? st.pMin.y : st.pMax.y;
                hi = std.max(hi, (1 - s) * (1 - t) * m00 + s * (1 - t) * m10 + (1 - s) * t * m01 + s * t * m11);
            }
            return hi;
        }
    }

    // One of the two textures where the region is inside a check, else a blend of both
    public sealed unsafe partial class FloatCheckerboardTexture
    {
        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            int check = TextureRegion.Check(map2D, map3D, region);
            if (check >= 0)
            {
                tex[check].RegionBounds(region, out lo, out hi);
                return;
            }
            tex[0].RegionBounds(region, out Float lo0, out Float hi0);
            tex[1].RegionBounds(region, out Float lo1, out Float hi1);
            lo = std.min(lo0, lo1);
            hi = std.max(hi0, hi1);
        }
    }

    public sealed unsafe partial class SpectrumCheckerboardTexture
    {
        public override Float RegionMax(Bounds3f region)
        {
            int check = TextureRegion.Check(map2D, map3D, region);
            if (check >= 0)
            {
                return tex[check].RegionMax(region);
            }
            return std.max(tex[0].RegionMax(region), tex[1].RegionMax(region));
        }
    }

    public sealed unsafe partial class FloatDotsTexture
    {
        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            insideDot.RegionBounds(region, out Float lo0, out Float hi0);
            outsideDot.RegionBounds(region, out Float lo1, out Float hi1);
            lo = std.min(lo0, lo1);
            hi = std.max(hi0, hi1);
        }
    }

    public sealed unsafe partial class SpectrumDotsTexture
    {
        public override Float RegionMax(Bounds3f region)
        {
            return std.max(insideDot.RegionMax(region), outsideDot.RegionMax(region));
        }
    }

    // A blend with an amount between 0 and 1: between the bounds of the two
    public sealed unsafe partial class FloatDirectionMixTexture
    {
        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            tex1.RegionBounds(region, out Float lo1, out Float hi1);
            tex2.RegionBounds(region, out Float lo2, out Float hi2);
            lo = std.min(lo1, lo2);
            hi = std.max(hi1, hi2);
        }
    }

    public sealed unsafe partial class SpectrumDirectionMixTexture
    {
        public override Float RegionMax(Bounds3f region)
        {
            return std.max(tex1.RegionMax(region), tex2.RegionMax(region));
        }
    }

    public sealed unsafe partial class SpectrumMixTexture
    {
        public override Float RegionMax(Bounds3f region)
        {
            amount.RegionBounds(region, out Float a0, out Float a1);
            return -pbrt.BlendMin(-tex1.RegionMax(region), 0, -tex2.RegionMax(region), 0, a0, a1);
        }
    }

    public sealed unsafe partial class SpectrumScaledTexture
    {
        public override Float RegionMax(Bounds3f region)
        {
            scale.RegionBounds(region, out Float s0, out Float s1);
            return tex.RegionMax(region) * std.max<Float>(0, s1);
        }
    }

    public sealed unsafe partial class WrinkledTexture
    {
        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            pbrt.TurbulenceBounds(TextureRegion.Mapped(mapping, region), omega, octaves, out lo, out hi);
            lo = std.max(lo, MinValue());
            hi = std.min(hi, MaxValue());
        }
    }

    // |FBm(0.1 p, 3 octaves)| FBm(p, 6 octaves): the product of the two intervals
    public sealed unsafe partial class WindyTexture
    {
        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            Bounds3f mapped = TextureRegion.Mapped(mapping, region);
            pbrt.FBmBounds(new Bounds3f(.1f * mapped.pMin, .1f * mapped.pMax), .5f, 3, out Float strengthLo, out Float strengthHi);
            pbrt.FBmBounds(mapped, .5f, 6, out Float heightLo, out Float heightHi);
            Float sizeLo = strengthLo > 0 ? strengthLo : (strengthHi < 0 ? -strengthHi : 0);
            Float sizeHi = std.max(std.abs(strengthLo), std.abs(strengthHi));
            lo = std.min(std.min(sizeLo * heightLo, sizeLo * heightHi), std.min(sizeHi * heightLo, sizeHi * heightHi));
            hi = std.max(std.max(sizeLo * heightLo, sizeLo * heightHi), std.max(sizeHi * heightLo, sizeHi * heightHi));
            lo = std.max(lo, MinValue());
            hi = std.min(hi, MaxValue());
        }
    }

    // The smallest value of the spectrum over the visible wavelengths
    public sealed unsafe partial class SpectrumConstantTexture
    {
        public override Float MinValue()
        {
            if (!minimumKnown)
            {
                Float m = Infinity;
                for (int lambda = (int)Lambda_min; lambda <= (int)Lambda_max; ++lambda)
                {
                    m = std.min(m, value.Evaluate(lambda));
                }
                minimum = std.max<Float>(m, 0);
                minimumKnown = true;
            }
            return minimum;
        }

        private bool minimumKnown;
        private Float minimum;
    }
}
