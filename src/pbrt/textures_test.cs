// (not in pbrt-v4) Tests for the FloatTexture MinValue()/MaxValue() bounds.

using System;
using testing;

namespace pbrt
{
    public static unsafe class TextureBoundTests
    {
        [TEST("TextureBounds", "NoiseBound")]
        public static void TextureBounds_NoiseBound()
        {
            RNG rng = new RNG(7);
            Float maxAbs = 0;
            for (int i = 0; i < 4000000; ++i)
            {
                Float x = 200 * rng.Uniform<Float>() - 100;
                Float y = 200 * rng.Uniform<Float>() - 100;
                Float z = 200 * rng.Uniform<Float>() - 100;
                maxAbs = std.max(maxAbs, std.abs(pbrt.Noise(x, y, z)));
            }
            EXPECT_LE(maxAbs, pbrt.NoiseBound);
            EXPECT_GT(maxAbs, (Float)0.5);  // the bound isn't absurdly loose
        }

        [TEST("TextureBounds", "NoiseTextures")]
        public static void TextureBounds_NoiseTextures()
        {
            TextureMapping3D map = new PointTransformMapping(new Transform());
            FloatTexture[] textures = new FloatTexture[] { new FBmTexture(map, 8, .5f), new FBmTexture(map, 3, .9f),
                                                           new WrinkledTexture(map, 8, .5f), new WindyTexture(map) };
            RNG rng = new RNG(3);
            foreach (FloatTexture tex in textures)
            {
                Float lo = tex.MinValue(), hi = tex.MaxValue();
                for (int i = 0; i < 200000; ++i)
                {
                    TextureEvalContext ctx = new TextureEvalContext();
                    ctx.p = new Point3f(50 * rng.Uniform<Float>(), 50 * rng.Uniform<Float>(), 50 * rng.Uniform<Float>());
                    Float eps = (Float)Math.Pow(10, -4 * rng.Uniform<Float>());
                    ctx.dpdx = new Vector3f(eps, 0, 0);
                    ctx.dpdy = new Vector3f(0, eps, 0);
                    Float v = tex.Evaluate(ctx);
                    EXPECT_GE(v, lo);
                    EXPECT_LE(v, hi);
                }
            }
        }

        [TEST("TextureBounds", "Combinators")]
        public static void TextureBounds_Combinators()
        {
            FloatTexture a = new FloatConstantTexture(-2), b = new FloatConstantTexture(3), s = new FloatConstantTexture(-1.5f);
            FloatScaledTexture scaled = new FloatScaledTexture(a, s);
            EXPECT_EQ((Float)3, scaled.MinValue());
            EXPECT_EQ((Float)3, scaled.MaxValue());
            FloatBilerpTexture bilerp = new FloatBilerpTexture(new UVMapping(), 1, -4, 2, 0);
            EXPECT_EQ((Float)(-4), bilerp.MinValue());
            EXPECT_EQ((Float)2, bilerp.MaxValue());
            FloatScaledTexture scaledBilerp = new FloatScaledTexture(bilerp, s);
            EXPECT_EQ((Float)(-3), scaledBilerp.MinValue());
            EXPECT_EQ((Float)6, scaledBilerp.MaxValue());
            FloatMixTexture mix = new FloatMixTexture(a, b, new FloatConstantTexture(0.25f));
            EXPECT_LE(mix.MinValue(), (Float)(-2 * .75 + 3 * .25));
            EXPECT_GE(mix.MaxValue(), (Float)(-2 * .75 + 3 * .25));
            FloatMixTexture mix01 = new FloatMixTexture(a, b, new FloatBilerpTexture(new UVMapping(), 0, 1, 0, 1));
            EXPECT_EQ((Float)(-2), mix01.MinValue());
            EXPECT_EQ((Float)3, mix01.MaxValue());
        }

        [TEST("TextureBounds", "SpectrumTextures")]
        public static void TextureBounds_SpectrumTextures()
        {
            SpectrumTexture c2 = new SpectrumConstantTexture(new ConstantSpectrum(2));
            SpectrumTexture c3 = new SpectrumConstantTexture(new ConstantSpectrum(3));
            EXPECT_EQ((Float)2, c2.MaxValue());
            EXPECT_EQ((Float)3, new SpectrumCheckerboardTexture(new UVMapping(), null, c2, c3).MaxValue());
            EXPECT_EQ((Float)3, new SpectrumDotsTexture(new UVMapping(), c2, c3).MaxValue());
            EXPECT_EQ((Float)3, new SpectrumDirectionMixTexture(c2, c3, new Vector3f(0, 0, 1)).MaxValue());
            // mix with an amount in [0, 1] can't exceed the larger input
            FloatTexture amt = new FloatBilerpTexture(new UVMapping(), 0, 1, 0, 1);
            EXPECT_EQ((Float)3, new SpectrumMixTexture(c2, c3, amt).MaxValue());
            // scaled by a negative constant: the value is <= 0, so the bound clamps to 0
            EXPECT_EQ((Float)0, new SpectrumScaledTexture(c2, new FloatConstantTexture(-1)).MaxValue());
            EXPECT_EQ((Float)5, new SpectrumScaledTexture(c2, new FloatConstantTexture(2.5f)).MaxValue());
            EXPECT_EQ((Float)1, new MarbleTexture(new PointTransformMapping(new Transform()), 8, .5f, 1, 1).MaxValue());
        }

        [TEST("TextureBounds", "NoiseGradientBound")]
        public static void TextureBounds_NoiseGradientBound()
        {
            RNG rng = new RNG(5);
            Float maxGrad = 0, h = 1e-3f;
            for (int i = 0; i < 2000000; ++i)
            {
                Float x = 20 * rng.Uniform<Float>(), y = 20 * rng.Uniform<Float>(), z = 20 * rng.Uniform<Float>();
                Float gx = (pbrt.Noise(x + h, y, z) - pbrt.Noise(x - h, y, z)) / (2 * h);
                Float gy = (pbrt.Noise(x, y + h, z) - pbrt.Noise(x, y - h, z)) / (2 * h);
                Float gz = (pbrt.Noise(x, y, z + h) - pbrt.Noise(x, y, z - h)) / (2 * h);
                maxGrad = std.max(maxGrad, (Float)Math.Sqrt(gx * gx + gy * gy + gz * gz));
            }
            EXPECT_LE(maxGrad, pbrt.NoiseGradientBound);
            EXPECT_GT(maxGrad, (Float)2);  // and it isn't absurdly loose
        }

        [TEST("TextureBounds", "FBmRegionBounds")]
        public static void TextureBounds_FBmRegionBounds()
        {
            TextureMapping3D map = new PointTransformMapping(pbrt.Scale(1.6f, 1.6f, 1.6f));
            FloatTexture tex = new FBmTexture(map, 4, .5f);
            RNG rng = new RNG(11);
            for (int i = 0; i < 2000; ++i)
            {
                Float size = 0.02f + 0.5f * rng.Uniform<Float>();
                Point3f p0 = new Point3f(4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2);
                Bounds3f region = new Bounds3f(p0, p0 + new Vector3f(size, size, size));
                tex.RegionBounds(region, out Float lo, out Float hi);
                for (int j = 0; j < 200; ++j)
                {
                    TextureEvalContext ctx = new TextureEvalContext();
                    ctx.p = new Point3f(p0.x + size * rng.Uniform<Float>(), p0.y + size * rng.Uniform<Float>(),
                                        p0.z + size * rng.Uniform<Float>());
                    Float v = tex.Evaluate(ctx);
                    EXPECT_GE(v, lo);
                    EXPECT_LE(v, hi);
                }
            }
        }
    }
}
