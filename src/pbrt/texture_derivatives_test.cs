// (not in pbrt-v4) texture_derivatives_test.cs
//
// The derivatives of the textures (FloatTexture.Evaluate() with directions, texture_derivatives.cs):
//   - every closed form against a central difference of the texture itself, over random contexts and directions, on
//     every mapping. Where two differences with different steps disagree (an edge of a checkerboard, a clamp, a step)
//     there is no derivative to compare with: those points are counted and left out;
//   - a texture made of one with a closed form and one without: the rule of the texture on both;
//   - finite differences forced, for one texture and for all: the difference of pbrt's BumpMap();
//   - the value that comes with the derivatives is Evaluate(), and with no pointer nothing else is computed.

using System;
using testing;

namespace pbrt
{
    public static unsafe class TextureDerivativesTests
    {
        // A texture without a closed form: the base class differentiates it
        sealed class Plain : FloatTexture
        {
            public override Float Evaluate(TextureEvalContext ctx)
            {
                return std.sin(1.3f * ctx.p.x + 0.4f * ctx.p.z) * (0.5f + 0.25f * ctx.uv[0]) + 0.2f * ctx.uv[1] * ctx.p.y;
            }
            public override Float MinValue() { return -Infinity; }
            public override Float MaxValue() { return Infinity; }
            public override string ToString() { return "[ Plain ]"; }
        }

        static TextureEvalContext Context(ref RNG rng, bool footprint)
        {
            TextureEvalContext ctx = new TextureEvalContext();
            ctx.p = new Point3f(4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2);
            ctx.uv = new Point2f(4 * rng.Uniform<Float>(), 4 * rng.Uniform<Float>());
            ctx.n = (Normal3f)Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() + 0.2f));
            if (footprint)
            {
                // The size of a pixel, no component of it next to nothing: the filtered checkerboard of pbrt divides a
                // difference of three numbers by the square of its width, which leaves no digits for a width of 1e-4
                Float size = 0.04f + 0.1f * rng.Uniform<Float>();
                ctx.dpdx = size * new Vector3f(0.3f + rng.Uniform<Float>(), 0.3f + rng.Uniform<Float>(), 0.3f + rng.Uniform<Float>());
                ctx.dpdy = size * new Vector3f(0.3f + rng.Uniform<Float>(), -0.3f - rng.Uniform<Float>(), 0.3f + rng.Uniform<Float>());
                ctx.dudx = size * (0.3f + rng.Uniform<Float>());
                ctx.dudy = -size * (0.3f + rng.Uniform<Float>());
                ctx.dvdx = size * (0.3f + rng.Uniform<Float>());
                ctx.dvdy = size * (0.3f + rng.Uniform<Float>());
            }
            return ctx;
        }

        static TextureDirections Directions(ref RNG rng)
        {
            return new TextureDirections(new Vector3f(rng.Uniform<Float>() - 0.3f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f),
                                         new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.3f, rng.Uniform<Float>() - 0.5f));
        }

        // The central difference of a function of the context along a direction, with the step h
        static double Difference(Func<TextureEvalContext, double> f, TextureEvalContext ctx, Vector3f direction, Vector2f duv, Float h)
        {
            return (f(TextureDirections.Shifted(ctx, h, direction, duv)) - f(TextureDirections.Shifted(ctx, -h, direction, duv))) / (2 * h);
        }

        delegate double Analytic(TextureEvalContext ctx, TextureDirections directions, out double dfdu, out double dfdv);

        // Compares over random contexts; returns the fraction of the points that could be compared
        static void Check(string name, Func<TextureEvalContext, double> value, Analytic analytic, bool footprint, int seed = 1)
        {
            RNG rng = new RNG((ulong)seed);
            int compared = 0, skipped = 0, failed = 0;
            double worst = 0;
            const int count = 3000;
            for (int i = 0; i < count; ++i)
            {
                TextureEvalContext ctx = Context(ref rng, footprint);
                TextureDirections directions = Directions(ref rng);
                double returned = analytic(ctx, directions, out double dfdu, out double dfdv);
                EXPECT_LT(Math.Abs(returned - value(ctx)), 1e-6 * (1 + Math.Abs(returned)), name + ": the value with the derivatives is not Evaluate()");
                for (int side = 0; side < 2; ++side)
                {
                    Vector3f direction = side == 0 ? directions.dpdu : directions.dpdv;
                    Vector2f duv = side == 0 ? directions.duvdu : directions.duvdv;
                    double coarse = Difference(value, ctx, direction, duv, 4e-3f), fine = Difference(value, ctx, direction, duv, 2e-3f);
                    double mine = side == 0 ? dfdu : dfdv;
                    double scale = Math.Max(Math.Abs(fine), Math.Abs(mine));
                    // A kink at the point itself fools both central differences the same way: the two sides must agree too
                    double here = value(ctx);
                    double forward = (value(TextureDirections.Shifted(ctx, 2e-3f, direction, duv)) - here) / 2e-3;
                    double backward = (here - value(TextureDirections.Shifted(ctx, -2e-3f, direction, duv))) / 2e-3;
                    if (Math.Abs(coarse - fine) > 0.02 * scale + 2e-3 || Math.Abs(forward - backward) > 0.03 * scale + 3e-3)
                    {
                        ++skipped;
                        continue;
                    }
                    ++compared;
                    // The difference has an error of its own: about what the two steps differ by
                    double error = Math.Max(0, Math.Abs(mine - fine) - 2 * Math.Abs(coarse - fine)) / (scale + 0.05);
                    worst = Math.Max(worst, error);
                    if (error > 0.03)
                    {
                        ++failed;
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE derivative %s: %d compared, %d at an edge, worst difference beyond the error of the reference %.2e of the slope, %d off",
                                                      name, compared, skipped, worst, failed));
            EXPECT_GT(compared, count);
            EXPECT_EQ(0, failed, name);
        }

        static void Check(string name, FloatTexture texture, bool footprint)
        {
            EXPECT_TRUE(texture.HasAnalyticDerivatives(), name);
            Check(name, (TextureEvalContext ctx) => texture.Evaluate(ctx),
                  (TextureEvalContext ctx, TextureDirections directions, out double dfdu, out double dfdv) =>
                  {
                      Float du, dv, onlyU, onlyV, alone;
                      Float value = texture.Evaluate(ctx, directions, &du, &dv);
                      // One at a time, and without the value: the same
                      texture.Evaluate(ctx, directions, &onlyU, null);
                      texture.EvaluateDerivatives(ctx, directions, null, &onlyV);
                      texture.EvaluateDerivatives(ctx, directions, &alone, null);
                      EXPECT_EQ(du, onlyU);
                      EXPECT_EQ(dv, onlyV);
                      EXPECT_EQ(du, alone);
                      dfdu = du;
                      dfdv = dv;
                      return value;
                  }, footprint);
        }

        static void Check(string name, SpectrumTexture texture, bool footprint)
        {
            EXPECT_TRUE(texture.HasAnalyticDerivatives(), name);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
            for (int index = 0; index < NSpectrumSamples; index += NSpectrumSamples - 1)
            {
                int wavelength = index;
                Check(name + " [" + wavelength + "]", (TextureEvalContext ctx) => texture.Evaluate(ctx, lambda)[wavelength],
                      (TextureEvalContext ctx, TextureDirections directions, out double dfdu, out double dfdv) =>
                      {
                          SampledSpectrum du, dv;
                          SampledSpectrum value = texture.Evaluate(ctx, lambda, directions, &du, &dv);
                          dfdu = du[wavelength];
                          dfdv = dv[wavelength];
                          return value[wavelength];
                      }, footprint);
            }
        }

        static TextureMapping2D[] Mappings2D()
        {
            Transform t = pbrt.Translate(new Vector3f(0.3f, -0.2f, 0.1f)) * pbrt.Rotate(40, new Vector3f(0.2f, 1, 0.4f)) * pbrt.Scale(1.3f, 0.8f, 1.1f);
            return new TextureMapping2D[] { new UVMapping(1.5f, 0.7f, 0.2f, -0.1f), new PlanarMapping(t, new Vector3f(1, 0.2f, 0), new Vector3f(0, 1, 0.3f), 0.1f, 0.2f),
                                            new SphericalMapping(t), new CylindricalMapping(t) };
        }

        static TextureMapping3D Mapping3D()
        {
            return new PointTransformMapping(pbrt.Translate(new Vector3f(0.3f, -0.2f, 0.1f)) * pbrt.Rotate(40, new Vector3f(0.2f, 1, 0.4f)) *
                                             pbrt.Scale(1.3f, 0.8f, 1.1f));
        }

        [TEST("TextureDerivatives", "Mapped")]
        public static void TextureDerivatives_Mapped()
        {
            string[] names = new string[] { "uv", "planar", "spherical", "cylindrical" };
            TextureMapping2D[] mappings = Mappings2D();
            Spectrum a = new ConstantSpectrum(0.2f), b = new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(0.7f, 0.3f, 0.1f));
            Spectrum c = new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(0.1f, 0.5f, 0.8f)), d = new ConstantSpectrum(0.9f);
            for (int i = 0; i < mappings.Length; ++i)
            {
                FloatTexture bilerp = new FloatBilerpTexture(mappings[i], 0.1f, 0.9f, 0.4f, -0.3f);
                Check("bilerp, " + names[i], bilerp, false);
                Check("spectrum bilerp, " + names[i], new SpectrumBilerpTexture(mappings[i], a, b, c, d), false);
                // With the size of a pixel the edges of a checkerboard are ramps: they have a slope. The width of the ramp
                // is the size of the pixel in (s, t), which the closed form holds while the point moves; on the spherical
                // and cylindrical mappings it changes with the point, so there the comparison is without a pixel size.
                Check("checkerboard, " + names[i], new FloatCheckerboardTexture(mappings[i], null, bilerp, new FloatConstantTexture(0.2f)), i < 2);
                Check("dots, " + names[i], new FloatDotsTexture(mappings[i], bilerp, new FloatScaledTexture(bilerp, new FloatConstantTexture(-2))), false);
            }
            TextureMapping3D solid = Mapping3D();
            FloatTexture plane = new FloatBilerpTexture(mappings[0], 0.1f, 0.9f, 0.4f, -0.3f);
            Check("checkerboard 3D", new FloatCheckerboardTexture(null, solid, plane, new FloatConstantTexture(0.2f)), true);
            Check("fbm", new FBmTexture(solid, 4, 0.5f), true);
            Check("fbm, no pixel size", new FBmTexture(solid, 3, 0.6f), false);
            Check("wrinkled", new WrinkledTexture(solid, 4, 0.5f), true);
            Check("windy", new WindyTexture(solid), true);
            SpectrumTexture spectrumA = new SpectrumBilerpTexture(mappings[0], a, b, c, d), spectrumB = new SpectrumBilerpTexture(mappings[1], d, c, b, a);
            Check("spectrum checkerboard", new SpectrumCheckerboardTexture(mappings[0], null, spectrumA, spectrumB), true);
            Check("spectrum checkerboard 3D", new SpectrumCheckerboardTexture(null, solid, spectrumA, spectrumB), true);
            Check("spectrum dots", new SpectrumDotsTexture(mappings[0], spectrumA, spectrumB), false);
            Check("constant", new FloatConstantTexture(0.7f), false);
            Check("spectrum constant", new SpectrumConstantTexture(b), false);
        }

        [TEST("TextureDerivatives", "Composed")]
        public static void TextureDerivatives_Composed()
        {
            TextureMapping2D[] mappings = Mappings2D();
            TextureMapping3D solid = Mapping3D();
            FloatTexture fbm = new FBmTexture(solid, 3, 0.5f);
            FloatTexture plane = new FloatBilerpTexture(mappings[0], 0.1f, 0.9f, 0.4f, 0.7f), other = new FloatBilerpTexture(mappings[1], 0.8f, 0.3f, 0.5f, 1.2f);
            // Bounded away from zero: 1.3 +- 1.05, and 0.8 +- 0.44
            FloatTexture positive = new FloatAddTexture(new FloatConstantTexture(1.3f), new FloatScaledTexture(new FBmTexture(solid, 3, 0.5f), new FloatConstantTexture(0.6f)));
            FloatTexture modulus = new FloatAddTexture(new FloatConstantTexture(0.8f),
                                                       new FloatScaledTexture(new FBmTexture(new PointTransformMapping(pbrt.Scale(0.7f, 0.9f, 0.6f)), 3, 0.5f),
                                                                              new FloatConstantTexture(0.25f)));
            Check("mix", new FloatMixTexture(fbm, plane, other), false);
            Check("direction mix", new FloatDirectionMixTexture(fbm, plane, Normalize(new Vector3f(0.2f, 0.3f, 1))), false);
            Check("scale", new FloatScaledTexture(fbm, plane), false);
            Check("add", new FloatAddTexture(fbm, plane), false);
            Check("subtract", new FloatSubtractTexture(fbm, plane), false);
            Check("abs", new FloatAbsTexture(fbm), false);
            Check("clamp", new FloatClampTexture(fbm, -0.15f, 0.2f), false);
            Check("divide", new FloatDivideTexture(fbm, positive), false);
            Check("power", new FloatPowerTexture(positive, modulus), false);
            Check("remap", new FloatRemapTexture(fbm, new FloatScaledTexture(plane, new FloatConstantTexture(-0.4f)), plane, other, positive), false);
            Check("rounding", new FloatRoundingTexture(fbm, new FloatScaledTexture(positive, new FloatConstantTexture(0.1f))), false);
            Check("modulo", new FloatModuloTexture(positive, modulus), false);
            Check("compare", new FloatCompareTexture(fbm, plane, true), false);
            Check("ramp", new FloatRampTexture(fbm, new Float[] { -0.3f, -0.1f, 0.05f, 0.3f }, new Float[] { 0, 1, 0.2f, 2 }, false), false);
            Check("ramp of steps", new FloatRampTexture(fbm, new Float[] { -0.3f, -0.1f, 0.05f, 0.3f }, new Float[] { 0, 1, 0.2f, 2 }, true), false);
            Spectrum a = new ConstantSpectrum(0.2f), b = new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(0.7f, 0.3f, 0.1f));
            Spectrum c = new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(0.1f, 0.5f, 0.8f)), d = new ConstantSpectrum(0.9f);
            SpectrumTexture spectrumA = new SpectrumBilerpTexture(mappings[0], a, b, c, d), spectrumB = new SpectrumBilerpTexture(mappings[1], d, c, b, a);
            Check("spectrum mix", new SpectrumMixTexture(spectrumA, spectrumB, plane), false);
            Check("spectrum direction mix", new SpectrumDirectionMixTexture(spectrumA, spectrumB, Normalize(new Vector3f(0.2f, 0.3f, 1))), false);
            Check("spectrum scale", new SpectrumScaledTexture(spectrumA, fbm), false);
        }

        // The change of an upsampled spectrum with its colour (RGBUpsampling.Derivative), for the three kinds of spectrum.
        // The upsampling is a table: its slope changes at the faces of the cells, and its values are differences of large
        // numbers in single precision, so a finite difference at a point is no reference. The slope is integrated along
        // a line of colours instead (the midpoint rule over 400 steps), which has to give the difference of the spectra at
        // the two ends: colours of any hue, and grey ones along the grey axis.
        [TEST("TextureDerivatives", "Upsampling")]
        public static void TextureDerivatives_Upsampling()
        {
            RNG rng = new RNG(8);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
            RGBColorSpace cs = RGBColorSpace.sRGB;
            SpectrumType[] types = new SpectrumType[] { SpectrumType.Albedo, SpectrumType.Unbounded, SpectrumType.Illuminant };
            foreach (SpectrumType type in types)
            {
                double worst = 0;
                const int lines = 400, steps = 400;
                for (int i = 0; i < lines; ++i)
                {
                    Float top = type == SpectrumType.Albedo ? 0.9f : 3f;
                    RGB a = new RGB(0.05f + top * rng.Uniform<Float>(), 0.05f + top * rng.Uniform<Float>(), 0.05f + top * rng.Uniform<Float>());
                    RGB b = new RGB(0.05f + top * rng.Uniform<Float>(), 0.05f + top * rng.Uniform<Float>(), 0.05f + top * rng.Uniform<Float>());
                    if (i % 5 == 0)
                    {
                        a = new RGB(a[0], a[0], a[0]);
                        b = new RGB(b[0], b[0], b[0]);
                    }
                    RGB step = (b - a) / steps;
                    double[] sum = new double[NSpectrumSamples], size = new double[NSpectrumSamples];
                    for (int k = 0; k < steps; ++k)
                    {
                        SampledSpectrum d = RGBUpsampling.Derivative(a + step * (k + 0.5f), step, cs, type, lambda);
                        for (int j = 0; j < NSpectrumSamples; ++j)
                        {
                            sum[j] += d[j];
                            size[j] += Math.Abs(d[j]);
                        }
                    }
                    SampledSpectrum ends = RGBUpsampling.Convert(b, cs, type, lambda) - RGBUpsampling.Convert(a, cs, type, lambda);
                    for (int j = 0; j < NSpectrumSamples; ++j)
                    {
                        worst = Math.Max(worst, Math.Abs(sum[j] - ends[j]) / (size[j] + 1e-3));
                    }
                }
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE derivative of the upsampling, %s: the slope integrated along %d lines of colours against the " +
                                                          "difference of the ends: worst %.2e of the integral of its size", type.ToString(), lines, worst));
                EXPECT_LT(worst, 0.01);
            }
            // A channel held at a limit does not move the spectrum
            SampledSpectrum held = RGBUpsampling.Derivative(new RGB(1.2f, 0.5f, -0.1f), new RGB(1, 0, 1), cs, SpectrumType.Albedo, lambda);
            EXPECT_EQ(0, held.MaxComponentValue());
            EXPECT_EQ(0, held.MinComponentValue());
        }

        // The same for a spectrum texture: its slope integrated along a line on the surface against the difference of its
        // values at the ends
        static void CheckIntegral(string name, SpectrumTexture texture)
        {
            EXPECT_TRUE(texture.HasAnalyticDerivatives(), name);
            RNG rng = new RNG(5);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
            double worst = 0;
            // (many steps: the midpoint rule loses a step's worth of slope at every kink of the texture)
            const int lines = 150, steps = 4000;
            for (int i = 0; i < lines; ++i)
            {
                TextureEvalContext ctx = Context(ref rng, false);
                TextureDirections directions = Directions(ref rng);
                Float length = 0.6f;
                double[] sum = new double[NSpectrumSamples], size = new double[NSpectrumSamples];
                for (int k = 0; k < steps; ++k)
                {
                    SampledSpectrum d;
                    texture.EvaluateDerivatives(TextureDirections.Shifted(ctx, length * (k + 0.5f) / steps, directions.dpdu, directions.duvdu), lambda, directions,
                                                &d, null);
                    for (int j = 0; j < NSpectrumSamples; ++j)
                    {
                        sum[j] += d[j] * length / steps;
                        size[j] += Math.Abs(d[j]) * length / steps;
                    }
                }
                SampledSpectrum ends = texture.Evaluate(TextureDirections.Shifted(ctx, length, directions.dpdu, directions.duvdu), lambda) - texture.Evaluate(ctx, lambda);
                for (int j = 0; j < NSpectrumSamples; ++j)
                {
                    worst = Math.Max(worst, Math.Abs(sum[j] - ends[j]) / (size[j] + 1e-3));
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE derivative %s: the slope integrated along %d lines against the difference of the ends: worst %.2e " +
                                                      "of the integral of its size", name, lines, worst));
            EXPECT_LT(worst, 0.01, name);
        }

        static void Check(string name, RGBTexture texture, bool footprint)
        {
            EXPECT_TRUE(texture.HasAnalyticDerivatives(), name);
            for (int channel = 0; channel < 3; ++channel)
            {
                int c = channel;
                Check(name + " [" + "rgb"[c] + "]", (TextureEvalContext ctx) => texture.Evaluate(ctx)[c],
                      (TextureEvalContext ctx, TextureDirections directions, out double dfdu, out double dfdv) =>
                      {
                          RGB du, dv;
                          RGB value = texture.Evaluate(ctx, directions, &du, &dv);
                          dfdu = du[c];
                          dfdv = dv[c];
                          return value[c];
                      }, footprint);
            }
        }

        // The spectrum textures that are a colour underneath: the colour against central differences, in RGB (the marble
        // spline of pbrt jumps between its segments: those points are left out as edges are); and the whole, colour and
        // upsampling, by the integral of the slope along a line, for a ramp whose colours keep red as their largest
        // channel (the table of the upsampling has a part for each largest channel, and its values do not quite meet
        // where they change over)
        [TEST("TextureDerivatives", "FromRGB")]
        public static void TextureDerivatives_FromRGB()
        {
            TextureMapping3D solid = Mapping3D();
            Check("marble colour", new MarbleTexture(solid, 3, 0.5f, 1.2f, 0.3f).RGB(), false);
            FloatTexture fbm = new FBmTexture(solid, 3, 0.5f);
            RGB[] colours = new RGB[] { new RGB(0.9f, 0.1f, 0.2f), new RGB(0.8f, 0.6f, 0.1f), new RGB(0.7f, 0.2f, 0.6f), new RGB(0.95f, 0.5f, 0.4f) };
            Float[] positions = new Float[] { -0.4f, -0.1f, 0.1f, 0.4f };
            Check("ramp colour", new SpectrumRampTexture(fbm, positions, colours, false, SpectrumType.Albedo, RGBColorSpace.sRGB).RGB(), false);
            CheckIntegral("spectrum ramp, albedo", new SpectrumRampTexture(fbm, positions, colours, false, SpectrumType.Albedo, RGBColorSpace.sRGB));
            CheckIntegral("spectrum ramp, unbounded", new SpectrumRampTexture(fbm, positions, colours, false, SpectrumType.Unbounded, RGBColorSpace.sRGB));
            CheckIntegral("spectrum ramp, illuminant", new SpectrumRampTexture(fbm, positions, colours, false, SpectrumType.Illuminant, RGBColorSpace.sRGB));
        }

        // An image of smooth values on disk: one channel, or three with red the largest
        static string WriteImage(string name, bool colour)
        {
            RNG rng = new RNG(77);
            const int size = 16;
            Image image = new Image(PixelFormat.Float, new Point2i(size, size), colour ? new string[] { "R", "G", "B" } : new string[] { "Y" });
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    Float wave = 0.5f + 0.25f * std.sin(0.9f * x + 0.3f * y) + 0.15f * std.cos(0.5f * y - 0.2f * x);
                    image.SetChannel(new Point2i(x, y), 0, colour ? 0.55f + 0.4f * wave : wave + 0.05f * rng.Uniform<Float>());
                    if (colour)
                    {
                        image.SetChannel(new Point2i(x, y), 1, 0.5f * wave + 0.05f * rng.Uniform<Float>());
                        image.SetChannel(new Point2i(x, y), 2, 0.45f - 0.4f * wave);
                    }
                }
            }
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
            EXPECT_TRUE(image.Write(path));
            return path;
        }

        // The image textures (the slope of the filter of the MIP map in closed form), the grids and the blackbody
        [TEST("TextureDerivatives", "ImagesGridsBlackbody")]
        public static void TextureDerivatives_ImagesGridsBlackbody()
        {
            string grey = WriteImage("magi_derivative_grey.exr", false), colour = WriteImage("magi_derivative_colour.exr", true);
            TextureMapping2D uv = new UVMapping(0.4f, 0.3f, 0.2f, -0.1f);
            FilterFunction[] filters = new FilterFunction[] { FilterFunction.Bilinear, FilterFunction.Trilinear, FilterFunction.Point };
            foreach (FilterFunction filter in filters)
            {
                MIPMapFilterOptions options = new MIPMapFilterOptions();
                options.filter = filter;
                foreach (bool invert in new bool[] { false, true })
                {
                    string name = "image, " + filter + (invert ? ", inverted" : "");
                    // The trilinear filter with the size of a pixel: between two levels of the pyramid
                    bool footprint = filter == FilterFunction.Trilinear;
                    Check(name, new FloatImageTexture(uv, grey, options, WrapMode.Repeat, 1.3f, invert, ColorEncoding.Linear, null), footprint);
                    SpectrumImageTexture spectrum = new SpectrumImageTexture(uv, colour, options, WrapMode.Repeat, 0.9f, invert, ColorEncoding.Linear,
                                                                             SpectrumType.Albedo, null);
                    Check(name + ", colour", spectrum.RGB(), footprint);
                }
            }
            MIPMapFilterOptions bilinear = new MIPMapFilterOptions();
            bilinear.filter = FilterFunction.Bilinear;
            CheckIntegral("spectrum image", new SpectrumImageTexture(uv, colour, bilinear, WrapMode.Repeat, 0.9f, false, ColorEncoding.Linear,
                                                                    SpectrumType.Albedo, null));
            // One channel as a spectrum: the same at every wavelength, with the slope of the float image
            CheckIntegral("spectrum image of one channel", new SpectrumImageTexture(uv, grey, bilinear, WrapMode.Repeat, 0.9f, false, ColorEncoding.Linear,
                                                                                    SpectrumType.Albedo, null));
            MIPMapFilterOptions ewa = new MIPMapFilterOptions();
            ewa.filter = FilterFunction.EWA;
            ewa.maxAnisotropy = 8;
            EXPECT_FALSE(new FloatImageTexture(uv, grey, ewa, WrapMode.Repeat, 1, false, ColorEncoding.Linear, null).HasAnalyticDerivatives());

            RNG rng = new RNG(3);
            TextureMapping3D solid = new PointTransformMapping(pbrt.Scale(0.3f, 0.25f, 0.2f) * pbrt.Translate(new Vector3f(2.5f, 2.5f, 2.5f)));
            FloatGridTexture[] channels = new FloatGridTexture[3];
            for (int k = 0; k < 3; ++k)
            {
                Float[] values = new Float[6 * 5 * 4];
                for (int i = 0; i < values.Length; ++i)
                {
                    values[i] = (k == 0 ? 0.6f : 0.05f) + 0.4f * rng.Uniform<Float>();
                }
                channels[k] = new FloatGridTexture(solid, 6, 5, 4, values, WrapMode.Repeat);
            }
            Check("grid", channels[1], false);
            SpectrumGridTexture grid = new SpectrumGridTexture(channels[0], channels[1], channels[2], SpectrumType.Albedo, RGBColorSpace.sRGB);
            Check("grid colour", grid.RGB(), false);
            CheckIntegral("spectrum grid", grid);

            FloatTexture heat = new FBmTexture(Mapping3D(), 3, 0.5f);
            Check("blackbody, physical", new SpectrumBlackbodyTexture(heat, 1500, -3, true, 6500), false);
            Check("blackbody, normalised", new SpectrumBlackbodyTexture(heat, 1500, -3, false, 6500), false);
        }

        [TEST("TextureDerivatives", "MixedAndForced")]
        public static void TextureDerivatives_MixedAndForced()
        {
            TextureMapping3D solid = Mapping3D();
            FloatTexture fbm = new FBmTexture(solid, 3, 0.5f), plain = new Plain();
            EXPECT_FALSE(plain.HasAnalyticDerivatives());
            // The product rule over a closed form and a finite difference
            FloatTexture product = new FloatScaledTexture(fbm, plain);
            Check("scale of a closed form by a texture without one", product, false);
            Check("mix by a texture without a closed form", new FloatMixTexture(fbm, new FloatConstantTexture(0.3f), plain), false);

            // Forced: what BumpMap() computes, a one-sided difference with the step of the pixel
            RNG rng = new RNG(4);
            for (int i = 0; i < 200; ++i)
            {
                TextureEvalContext ctx = Context(ref rng, (i & 1) == 0);
                TextureDirections directions = Directions(ref rng);
                Float du = .5f * (std.abs(ctx.dudx) + std.abs(ctx.dudy));
                if (du == 0)
                {
                    du = .0005f;
                }
                Float value = fbm.Evaluate(ctx);
                Float stock = (fbm.Evaluate(TextureDirections.Shifted(ctx, du, directions.dpdu, directions.duvdu)) - value) / du;
                Float analytic, forced, all;
                fbm.Evaluate(ctx, directions, &analytic, null);
                fbm.finiteDifferences = true;
                fbm.Evaluate(ctx, directions, &forced, null);
                fbm.finiteDifferences = false;
                pbrt.GetOptions().finiteTextureDerivatives = true;
                fbm.Evaluate(ctx, directions, &all, null);
                pbrt.GetOptions().finiteTextureDerivatives = false;
                EXPECT_EQ(stock, forced);
                EXPECT_EQ(stock, all);
                EXPECT_TRUE(analytic != stock || analytic == 0);
            }
        }
        static ParsedParameter OslParameter(string type, string name, string value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = type;
            p.name = name;
            p.AddString(value);
            return p;
        }

        static ParsedParameter OslParameter(string name, Float value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "float";
            p.name = name;
            p.AddFloat(value);
            return p;
        }

        static ParsedParameter OslLayers(string first, string second)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "string";
            p.name = "layers";
            p.AddString(first);
            p.AddString(second);
            return p;
        }

        static TextureParameterDictionary OslDictionary(params ParsedParameter[] parameters)
        {
            return new TextureParameterDictionary(new ParameterDictionary(new System.Collections.Generic.List<ParsedParameter>(parameters), RGBColorSpace.sRGB), new NamedTextures());
        }

        // The derivatives that OSL computes itself (the output of the shader compiled with its derivatives)
        [TEST("TextureDerivatives", "OSL")]
        public static void OSL()
        {
            OSLScene.Reset();
            OSLSystem.spectralMode = OSLSpectralMode.Upsample;
            OSLSystem.emissionIlluminant = true;
            OSLSystem.AddSearchPath(OslTestShaders.Directory());

            OSLFloatTexture wave = OSLFloatTexture.Create(new Transform(), OslDictionary(OslParameter("string", "shader", "dwave")), new FileLoc(),
                                                          new Allocator());
            Check("OSL float of u, v and P", wave, false);
            Check("OSL float of u, v and P, with the size of a pixel", wave, true);

            OSLFloatTexture noise = OSLFloatTexture.Create(new Transform(), OslDictionary(OslParameter("string", "shader", "fnoise"), OslParameter("scale", 2)),
                                                           new FileLoc(), new Allocator());
            Check("OSL noise", noise, false);

            // A color read as a float: the mean of the three
            OSLFloatTexture gray = OSLFloatTexture.Create(new Transform(), OslDictionary(OslParameter("string", "shader", "dcolor")), new FileLoc(),
                                                          new Allocator());
            Check("OSL color read as a float", gray, false);

            // Two layers: the derivatives go through the connection
            OSLFloatTexture layers = OSLFloatTexture.Create(new Transform(),
                                                            OslDictionary(OslLayers("warp:dwarp", "stripes:dstripes"),
                                                                          OslParameter("string", "connect", "warp.Out stripes.Pin")),
                                                            new FileLoc(), new Allocator());
            Check("OSL two layers", layers, false);

            foreach (SpectrumType type in new SpectrumType[] { SpectrumType.Albedo, SpectrumType.Unbounded, SpectrumType.Illuminant })
            {
                OSLSpectrumTexture color = OSLSpectrumTexture.Create(new Transform(), OslDictionary(OslParameter("string", "shader", "dcolor")), type,
                                                                     new FileLoc(), new Allocator());
                CheckIntegral("OSL color to a spectrum (" + type.ToString() + ")", color);
            }

            // An output that OSL does not differentiate: differences, as any texture without a closed form
            OSLFloatTexture steps = OSLFloatTexture.Create(new Transform(), OslDictionary(OslParameter("string", "shader", "dint")), new FileLoc(),
                                                           new Allocator());
            EXPECT_FALSE(steps.HasAnalyticDerivatives());
            TextureEvalContext flat = new TextureEvalContext();
            flat.uv = new Point2f(0.1f, 0.3f);
            Float stepped;
            EXPECT_EQ(0, steps.Evaluate(flat, new TextureDirections(new Vector3f(1, 0, 0), new Vector3f(0, 1, 0)), &stepped, null));
            EXPECT_EQ(0, stepped);

            // At an intersection the lookup follows the point of the context: a difference moves it
            SurfaceInteraction si = new SurfaceInteraction(new Point3fi(new Point3f(0.3f, 0.2f, 0.1f)), new Point2f(0.4f, 0.6f), new Vector3f(0, 0, 1),
                                                           new Vector3f(1, 0, 0), new Vector3f(0, 1, 0), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0, false);
            TextureEvalContext hit = new TextureEvalContext(si);
            TextureDirections along = new TextureDirections(new Vector3f(1, 0, 0), new Vector3f(0, 1, 0));
            Float exactU, exactV, differenceU, differenceV;
            wave.Evaluate(hit, along, &exactU, &exactV);
            wave.finiteDifferences = true;
            wave.Evaluate(hit, along, &differenceU, &differenceV);
            wave.finiteDifferences = false;
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE derivative OSL at an intersection: OSL %f %f, differences %f %f", exactU, exactV, differenceU,
                                                      differenceV));
            EXPECT_GT(std.abs(exactU), 0.05f);
            EXPECT_LT(std.abs(exactU - differenceU), 0.01f);
            EXPECT_LT(std.abs(exactV - differenceV), 0.01f);
        }
        // The spectra given by a formula, their parameters textures (textures_parametric.cs)
        [TEST("TextureDerivatives", "ParametricSpectra")]
        public static void ParametricSpectra()
        {
            TextureMapping3D solid = Mapping3D();
            FloatTexture noise = new FBmTexture(solid, 3, 0.5f);
            FloatTexture other = new FBmTexture(new PointTransformMapping(pbrt.Scale(1.3f, 0.9f, 1.6f)), 2, 0.6f);
            FloatTexture mean = new FloatAddTexture(new FloatConstantTexture(540), new FloatScaledTexture(noise, new FloatConstantTexture(60)));
            FloatTexture width = new FloatAddTexture(new FloatConstantTexture(60), new FloatScaledTexture(other, new FloatConstantTexture(15)));
            FloatTexture level = new FloatAddTexture(new FloatConstantTexture(1.2f), new FloatScaledTexture(other, new FloatConstantTexture(0.3f)));
            Check("gaussian spectrum", new SpectrumGaussianTexture(mean, width, level, 0.1f), false);
            Check("frequency spectrum", new SpectrumFrequencyTexture(new FloatAddTexture(new FloatConstantTexture(0.02f),
                                                                                         new FloatScaledTexture(noise, new FloatConstantTexture(0.004f))),
                                                                     other, level), false);
            Check("cauchy spectrum", new SpectrumCauchyTexture(level, new FloatScaledTexture(level, new FloatConstantTexture(0.004f)),
                                                               new FloatScaledTexture(noise, new FloatConstantTexture(0.0002f))), false);
            Check("sellmeier spectrum", new SpectrumSellmeierTexture(level, new FloatScaledTexture(level, new FloatConstantTexture(0.2f)),
                                                                     new FloatConstantTexture(1.01f),
                                                                     new FloatScaledTexture(level, new FloatConstantTexture(0.005f)),
                                                                     new FloatConstantTexture(0.02f), new FloatConstantTexture(103.56f)), false);
            Check("long-pass spectrum", new SpectrumEdgeTexture(mean, width, new FloatScaledTexture(level, new FloatConstantTexture(0.1f)), level), false);
        }
    }
}
