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
    }
}
