// (not in pbrt-v4) texture_bounds_test.cs
//
// The bounds of the textures over a region (texture_bounds.cs): for boxes of several sizes, every value at points of
// the box is inside the bounds, and how much of the bounds the values fill (1: the bounds are the range of the values).

using System;
using testing;

namespace pbrt
{
    public static unsafe class TextureBoundsTests
    {
        static Bounds3f Box(ref RNG rng, Float size)
        {
            Point3f corner = new Point3f(4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2);
            return new Bounds3f(corner, corner + size * new Vector3f(0.3f + rng.Uniform<Float>(), 0.3f + rng.Uniform<Float>(), 0.3f + rng.Uniform<Float>()));
        }

        static TextureEvalContext Inside(ref RNG rng, Bounds3f box)
        {
            TextureEvalContext ctx = new TextureEvalContext();
            ctx.p = box.Lerp(new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>()));
            ctx.n = (Normal3f)Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f));
            return ctx;
        }

        static void Check(string name, FloatTexture texture)
        {
            RNG rng = new RNG(11);
            foreach (Float size in new Float[] { 0.05f, 0.3f, 1.5f })
            {
                int outside = 0;
                double filled = 0, saved = 0;
                const int boxes = 300;
                Float globalRange = texture.MaxValue() - texture.MinValue();
                for (int i = 0; i < boxes; ++i)
                {
                    Bounds3f box = Box(ref rng, size);
                    texture.RegionBounds(box, out Float lo, out Float hi);
                    Float seenLo = Infinity, seenHi = -Infinity;
                    for (int k = 0; k < 400; ++k)
                    {
                        Float v = texture.Evaluate(Inside(ref rng, box));
                        seenLo = std.min(seenLo, v);
                        seenHi = std.max(seenHi, v);
                    }
                    if (seenLo < lo - 1e-5f || seenHi > hi + 1e-5f)
                    {
                        ++outside;
                    }
                    filled += hi > lo ? (seenHi - seenLo) / (hi - lo) : 1;
                    saved += globalRange > 0 ? 1 - (hi - lo) / globalRange : 0;
                }
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE bounds %s, boxes of size %.2f: %d of %d with a value outside; the values fill %.0f%% of the " +
                                                          "bounds; the bounds are %.0f%% narrower than those over everything", name, size, outside, boxes,
                                                          100 * filled / boxes, 100 * saved / boxes));
                EXPECT_EQ(0, outside, name);
            }
        }

        static void Check(string name, SpectrumTexture texture)
        {
            RNG rng = new RNG(12);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.6f);
            foreach (Float size in new Float[] { 0.05f, 0.3f, 1.5f })
            {
                int outside = 0;
                double filled = 0, saved = 0;
                const int boxes = 300;
                Float everything = texture.MaxValue();
                for (int i = 0; i < boxes; ++i)
                {
                    Bounds3f box = Box(ref rng, size);
                    texture.RegionBounds(box, out Float lo, out Float hi);
                    EXPECT_EQ(hi, texture.RegionMax(box));
                    Float seenLo = Infinity, seenHi = -Infinity;
                    for (int k = 0; k < 400; ++k)
                    {
                        SampledSpectrum v = texture.Evaluate(Inside(ref rng, box), lambda);
                        seenLo = std.min(seenLo, v.MinComponentValue());
                        seenHi = std.max(seenHi, v.MaxComponentValue());
                    }
                    if (seenLo < lo - 1e-5f || seenHi > hi + 1e-5f)
                    {
                        ++outside;
                    }
                    filled += hi > 0 ? seenHi / hi : 1;
                    saved += everything > 0 ? 1 - hi / everything : 0;
                }
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE bounds %s, boxes of size %.2f: %d of %d with a value outside; the largest value is %.0f%% of " +
                                                          "the bound; the bound is %.0f%% lower than that over everything", name, size, outside, boxes,
                                                          100 * filled / boxes, 100 * saved / boxes));
                EXPECT_EQ(0, outside, name);
            }
        }

        [TEST("TextureBounds", "Regions")]
        public static void TextureBounds_Regions()
        {
            Transform t = pbrt.Translate(new Vector3f(0.3f, -0.2f, 0.1f)) * pbrt.Rotate(40, new Vector3f(0.2f, 1, 0.4f)) * pbrt.Scale(1.3f, 0.8f, 1.1f);
            TextureMapping2D planar = new PlanarMapping(t, new Vector3f(1, 0.2f, 0), new Vector3f(0, 1, 0.3f), 0.1f, 0.2f);
            // A plane whose (s, t) stay in the unit square over the boxes
            TextureMapping2D unit = new PlanarMapping(pbrt.Scale(0.1f, 0.1f, 0.1f), new Vector3f(1, 0, 0), new Vector3f(0, 1, 0), 0.5f, 0.5f);
            TextureMapping3D solid = new PointTransformMapping(t);
            FloatTexture bilerp = new FloatBilerpTexture(planar, 0.1f, 0.9f, 0.4f, -0.3f), fbm = new FBmTexture(solid, 4, 0.5f);
            Check("bilerp", bilerp);
            Check("checkerboard", new FloatCheckerboardTexture(planar, null, fbm, new FloatConstantTexture(3)));
            Check("checkerboard 3D", new FloatCheckerboardTexture(null, solid, fbm, new FloatConstantTexture(3)));
            Check("dots", new FloatDotsTexture(planar, fbm, new FloatConstantTexture(3)));
            Check("direction mix", new FloatDirectionMixTexture(fbm, bilerp, Normalize(new Vector3f(0.2f, 0.3f, 1))));
            Check("wrinkled", new WrinkledTexture(solid, 4, 0.5f));
            Check("wrinkled, negative weights", new WrinkledTexture(solid, 3, -0.6f));
            Check("windy", new WindyTexture(solid));

            Spectrum a = new ConstantSpectrum(0.2f), b = new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(0.7f, 0.3f, 0.1f));
            Spectrum c = new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(0.1f, 0.5f, 0.8f)), d = new ConstantSpectrum(0.9f);
            SpectrumTexture blend = new SpectrumBilerpTexture(unit, a, b, c, d), flat = new SpectrumConstantTexture(new ConstantSpectrum(0.05f));
            Check("spectrum bilerp", blend);
            Check("spectrum checkerboard 3D", new SpectrumCheckerboardTexture(null, solid, blend, flat));
            Check("spectrum dots", new SpectrumDotsTexture(planar, blend, flat));
            Check("spectrum direction mix", new SpectrumDirectionMixTexture(blend, flat, Normalize(new Vector3f(0.2f, 0.3f, 1))));
            FloatTexture amount = new FloatClampTexture(new FloatAddTexture(new FloatConstantTexture(0.5f), fbm), 0, 1);
            Check("spectrum mix", new SpectrumMixTexture(blend, flat, amount));
            Check("spectrum scale", new SpectrumScaledTexture(blend, new FloatAbsTexture(fbm)));

            // The smallest value of a constant spectrum
            SpectrumConstantTexture colour = new SpectrumConstantTexture(b);
            Float least = Infinity;
            for (Float lambda = 360; lambda <= 830; lambda += 0.5f)
            {
                least = std.min(least, b.Evaluate(lambda));
            }
            EXPECT_LE(colour.MinValue(), least);
            EXPECT_GT(colour.MinValue(), 0.9f * least);
        }
    }
}
