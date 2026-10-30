// (not in pbrt-v4) Tests for the textures that drive volumes: the 3D grid texture, the triplanar texture and the
// textured absorption, scattering and asymmetry of the texture medium.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class TextureVolumeTests
    {
        private static Float[] RandomGrid(int nx, int ny, int nz, int seed)
        {
            RNG rng = new RNG((ulong)seed);
            Float[] values = new Float[nx * ny * nz];
            for (int i = 0; i < values.Length; ++i)
                values[i] = (rng.Uniform<Float>() < .3f) ? 0 : 3 * rng.Uniform<Float>();
            return values;
        }

        // The grid texture reproduces SampledGrid's trilinear lookup, which is what a "uniformgrid" medium reads
        [TEST("FloatGridTexture", "MatchesSampledGrid")]
        public static void FloatGridTexture_MatchesSampledGrid()
        {
            int nx = 7, ny = 5, nz = 6;
            Float[] values = RandomGrid(nx, ny, nz, 1);
            SampledGrid<Float> grid = new SampledGrid<Float>(values, nx, ny, nz, null);
            FloatGridTexture texture = new FloatGridTexture(new PointTransformMapping(new Transform()), nx, ny, nz, values,
                                                            WrapMode.Black);
            RNG rng = new RNG(2);
            Float maxDifference = 0;
            for (int i = 0; i < 5000; ++i)
            {
                Point3f p = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                TextureEvalContext ctx = new TextureEvalContext();
                ctx.p = p;
                maxDifference = std.max(maxDifference, std.abs(texture.Evaluate(ctx) - grid.Lookup(p)));
            }
            Console.WriteLine($"PROBE densitygrid vs SampledGrid: max difference {maxDifference}");
            EXPECT_LT(maxDifference, 1e-5f);
        }

        // Region bounds hold for every wrap mode, including regions straddling or outside the unit cube, and are
        // tighter than the global bounds for small regions
        [TEST("FloatGridTexture", "RegionBoundsValid")]
        public static void FloatGridTexture_RegionBoundsValid()
        {
            int nx = 9, ny = 6, nz = 7;
            Float[] values = RandomGrid(nx, ny, nz, 3);
            // The fourth case is the white border (a black-style border that reads 1); the last three sample at the nodes
            foreach ((WrapMode wrap, Float border, bool nodes) in new (WrapMode, Float, bool)[] { (WrapMode.Black, 0f, false), (WrapMode.Clamp, 0f, false), (WrapMode.Repeat, 0f, false), (WrapMode.Black, 1f, false), (WrapMode.Black, 0f, true), (WrapMode.Clamp, 0f, true), (WrapMode.Repeat, 0f, true), (WrapMode.Black, 1f, true) })
            {
                Transform renderFromTexture = Translate(new Vector3f(.1f, -.2f, .3f)) * Scale(1.5f, 1.2f, 2f);
                FloatGridTexture texture = new FloatGridTexture(new PointTransformMapping(Inverse(renderFromTexture)), nx, ny, nz,
                                                                values, wrap, border, nodes);
                RNG rng = new RNG(4);
                int violations = 0, tighter = 0;
                for (int i = 0; i < 2000; ++i)
                {
                    Point3f c = new Point3f(-1 + 4 * rng.Uniform<Float>(), -1 + 4 * rng.Uniform<Float>(), -1 + 4 * rng.Uniform<Float>());
                    Vector3f e = new Vector3f(.4f * rng.Uniform<Float>(), .4f * rng.Uniform<Float>(), .4f * rng.Uniform<Float>());
                    Bounds3f region = new Bounds3f(c, c + e);
                    texture.RegionBounds(region, out Float lo, out Float hi);
                    if (hi < texture.MaxValue() - 1e-6f || lo > texture.MinValue() + 1e-6f)
                        ++tighter;
                    for (int k = 0; k < 40; ++k)
                    {
                        TextureEvalContext ctx = new TextureEvalContext();
                        ctx.p = new Point3f(c.x + e.x * rng.Uniform<Float>(), c.y + e.y * rng.Uniform<Float>(), c.z + e.z * rng.Uniform<Float>());
                        Float v = texture.Evaluate(ctx);
                        if (v < lo - 1e-5f || v > hi + 1e-5f)
                            ++violations;
                    }
                }
                Console.WriteLine($"PROBE densitygrid {wrap} border {border} nodes {nodes}: violations {violations}, tighter-than-global regions {tighter}/2000");
                EXPECT_EQ(violations, 0);
                EXPECT_GT(tighter, 200);
            }
        }

        // Node sampling puts the samples at i / n, as LuxCore's density grid does
        [TEST("FloatGridTexture", "NodeSamplesAtNodes")]
        public static void FloatGridTexture_NodeSamplesAtNodes()
        {
            int nx = 6, ny = 5, nz = 4;
            Float[] values = RandomGrid(nx, ny, nz, 11);
            FloatGridTexture texture = new FloatGridTexture(new PointTransformMapping(new Transform()), nx, ny, nz, values,
                                                            WrapMode.Clamp, 0, true);
            Float maxDifference = 0;
            for (int z = 0; z < nz; ++z)
            {
                for (int y = 0; y < ny; ++y)
                {
                    for (int x = 0; x < nx; ++x)
                    {
                        TextureEvalContext ctx = new TextureEvalContext();
                        ctx.p = new Point3f((x + 1e-4f) / nx, (y + 1e-4f) / ny, (z + 1e-4f) / nz);
                        maxDifference = std.max(maxDifference, std.abs(texture.Evaluate(ctx) - values[x + nx * (y + ny * z)]));
                    }
                }
            }
            Console.WriteLine($"PROBE node grid: max difference at the nodes {maxDifference}");
            EXPECT_LT(maxDifference, 2e-3f);
        }

        private static FloatGridTexture MakeGrid(int seed, Float scale, Float offset)
        {
            int nx = 6, ny = 5, nz = 7;
            Float[] values = RandomGrid(nx, ny, nz, seed);
            for (int i = 0; i < values.Length; ++i)
                values[i] = values[i] * scale + offset;
            Transform renderFromTexture = Translate(new Vector3f(.1f * seed, -.2f, .3f)) * Scale(1.4f, 1.2f, 1.7f);
            return new FloatGridTexture(new PointTransformMapping(Inverse(renderFromTexture)), nx, ny, nz, values, WrapMode.Repeat);
        }

        // Each math texture's global and region bounds contain its values, with grids (which have tight region bounds)
        // and constants as inputs
        [TEST("FloatMathTextures", "BoundsValid")]
        public static void FloatMathTextures_BoundsValid()
        {
            FloatTexture a = MakeGrid(1, 1f, -1f), b = MakeGrid(2, 2f, .5f), c = MakeGrid(3, .5f, 1f);
            FloatTexture two = new FloatConstantTexture(2f), half = new FloatConstantTexture(.5f);
            (string name, FloatTexture texture)[] cases =
            {
                ("add", new FloatAddTexture(a, b)),
                ("subtract", new FloatSubtractTexture(a, b)),
                ("abs", new FloatAbsTexture(a)),
                ("clamp", new FloatClampTexture(a, -.3f, .8f)),
                ("divide by positive", new FloatDivideTexture(a, c)),
                ("power", new FloatPowerTexture(c, half)),
                ("power of signed base", new FloatPowerTexture(a, two)),
                ("remap", new FloatRemapTexture(a, new FloatConstantTexture(-1f), new FloatConstantTexture(1f), half, two)),
                ("rounding", new FloatRoundingTexture(b, half)),
                ("modulo", new FloatModuloTexture(a, half)),
                ("greaterthan", new FloatCompareTexture(a, b, true)),
                ("lessthan", new FloatCompareTexture(a, b, false)),
                ("nested", new FloatAddTexture(new FloatAbsTexture(new FloatSubtractTexture(a, b)), new FloatClampTexture(c, 0f, 1.2f))),
            };
            foreach ((string name, FloatTexture texture) in cases)
            {
                RNG rng = new RNG(13);
                int violations = 0, globalViolations = 0, tighter = 0;
                Float globalLo = texture.MinValue(), globalHi = texture.MaxValue();
                for (int i = 0; i < 1500; ++i)
                {
                    Point3f center = new Point3f(-1 + 3 * rng.Uniform<Float>(), -1 + 3 * rng.Uniform<Float>(), -1 + 3 * rng.Uniform<Float>());
                    Vector3f extent = new Vector3f(.3f * rng.Uniform<Float>(), .3f * rng.Uniform<Float>(), .3f * rng.Uniform<Float>());
                    texture.RegionBounds(new Bounds3f(center, center + extent), out Float lo, out Float hi);
                    if (hi < globalHi - 1e-4f || lo > globalLo + 1e-4f)
                        ++tighter;
                    for (int k = 0; k < 30; ++k)
                    {
                        TextureEvalContext ctx = new TextureEvalContext();
                        ctx.p = new Point3f(center.x + extent.x * rng.Uniform<Float>(), center.y + extent.y * rng.Uniform<Float>(),
                                            center.z + extent.z * rng.Uniform<Float>());
                        Float v = texture.Evaluate(ctx);
                        if (v < lo - 1e-4f || v > hi + 1e-4f)
                            ++violations;
                        if (v < globalLo - 1e-4f || v > globalHi + 1e-4f)
                            ++globalViolations;
                    }
                }
                Console.WriteLine($"PROBE math texture {name}: region violations {violations}, global violations {globalViolations}, tighter regions {tighter}/1500");
                EXPECT_EQ(violations, 0);
                EXPECT_EQ(globalViolations, 0);
            }
        }

        // Values of LuxCore's math textures at fixed inputs
        [TEST("FloatMathTextures", "LuxCoreValues")]
        public static void FloatMathTextures_LuxCoreValues()
        {
            TextureEvalContext ctx = new TextureEvalContext();
            FloatTexture Const(Float v) { return new FloatConstantTexture(v); }
            EXPECT_EQ(new FloatDivideTexture(Const(3f), Const(0f)).Evaluate(ctx), 0f);
            EXPECT_EQ(new FloatDivideTexture(Const(3f), Const(2f)).Evaluate(ctx), 1.5f);
            EXPECT_EQ(new FloatPowerTexture(Const(-2f), Const(.5f)).Evaluate(ctx), 0f);
            EXPECT_EQ(new FloatPowerTexture(Const(-2f), Const(3f)).Evaluate(ctx), -8f);
            EXPECT_EQ(new FloatModuloTexture(Const(5.5f), Const(2f)).Evaluate(ctx), 1.5f);
            EXPECT_EQ(new FloatModuloTexture(Const(5.5f), Const(0f)).Evaluate(ctx), 0f);
            EXPECT_EQ(new FloatRoundingTexture(Const(1.3f), Const(.5f)).Evaluate(ctx), 1.5f);
            EXPECT_EQ(new FloatRoundingTexture(Const(1.1f), Const(.5f)).Evaluate(ctx), 1f);
            EXPECT_EQ(new FloatRemapTexture(Const(.5f), Const(0f), Const(1f), Const(10f), Const(20f)).Evaluate(ctx), 15f);
            EXPECT_EQ(new FloatRemapTexture(Const(3f), Const(0f), Const(1f), Const(10f), Const(20f)).Evaluate(ctx), 20f);
            EXPECT_EQ(new FloatCompareTexture(Const(2f), Const(1f), true).Evaluate(ctx), 1f);
            EXPECT_EQ(new FloatCompareTexture(Const(2f), Const(1f), false).Evaluate(ctx), 0f);
            EXPECT_EQ(new FloatClampTexture(Const(5f), 0f, 1f).Evaluate(ctx), 1f);
            EXPECT_EQ(new FloatAbsTexture(Const(-4f)).Evaluate(ctx), 4f);
        }

        // The cloud texture stays in [0,1], is a cloud (zero far from the center, positive near it), and its spheres work
        [TEST("FloatCloudTexture", "RangeAndShape")]
        public static void FloatCloudTexture_RangeAndShape()
        {
            foreach (int spheres in new int[] { 0, 12 })
            {
                FloatCloudTexture cloud = new FloatCloudTexture(new PointTransformMapping(new Transform()), .5f, .5f, .01f, 6f, .9f, .8f,
                                                                2, .5f, .3f, spheres, .15f);
                RNG rng = new RNG(14);
                int outOfRange = 0, positive = 0;
                for (int i = 0; i < 20000; ++i)
                {
                    TextureEvalContext ctx = new TextureEvalContext();
                    ctx.p = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                    Float v = cloud.Evaluate(ctx);
                    if (!(v >= 0 && v <= 1))
                        ++outOfRange;
                    if (v > 0)
                        ++positive;
                }
                TextureEvalContext far = new TextureEvalContext();
                far.p = new Point3f(5, 5, 5);
                Console.WriteLine($"PROBE cloud spheres={spheres}: out of range {outOfRange}, positive {positive}/20000, far value {cloud.Evaluate(far)}");
                EXPECT_EQ(outOfRange, 0);
                EXPECT_GT(positive, 200);
                EXPECT_EQ(cloud.Evaluate(far), 0f);
            }
        }

        // Three different textures along the three axes, bounds valid
        [TEST("FloatTriplanarTexture", "ThreeTexturesBoundsValid")]
        public static void FloatTriplanarTexture_ThreeTexturesBoundsValid()
        {
            FloatImageTexture t1 = MakeProjectedImage("magi_triplanar_a.exr", WrapMode.Clamp, 1f);
            FloatImageTexture t2 = MakeProjectedImage("magi_triplanar_b.exr", WrapMode.Repeat, 3f);
            FloatTexture t3 = new FloatConstantTexture(.25f);
            Transform renderFromTexture = Translate(new Vector3f(.3f, .1f, -.2f)) * Scale(1.3f, 1.3f, 1.3f);
            FloatTriplanarTexture texture = new FloatTriplanarTexture(new PointTransformMapping(Inverse(renderFromTexture)), t1, t2,
                                                                      t3, 1f, 1f, 2f);
            RNG rng = new RNG(15);
            int violations = 0;
            for (int i = 0; i < 1500; ++i)
            {
                Point3f c = new Point3f(-1 + 3 * rng.Uniform<Float>(), -1 + 3 * rng.Uniform<Float>(), -1 + 3 * rng.Uniform<Float>());
                Vector3f e = new Vector3f(.3f * rng.Uniform<Float>(), .3f * rng.Uniform<Float>(), .3f * rng.Uniform<Float>());
                texture.RegionBounds(new Bounds3f(c, c + e), out Float lo, out Float hi);
                for (int k = 0; k < 30; ++k)
                {
                    TextureEvalContext ctx = new TextureEvalContext();
                    ctx.p = new Point3f(c.x + e.x * rng.Uniform<Float>(), c.y + e.y * rng.Uniform<Float>(), c.z + e.z * rng.Uniform<Float>());
                    Float v = texture.Evaluate(ctx);
                    if (v < lo - 1e-5f || v > hi + 1e-5f || v < texture.MinValue() - 1e-5f || v > texture.MaxValue() + 1e-5f)
                        ++violations;
                }
            }
            Console.WriteLine($"PROBE triplanar with three textures: violations {violations}");
            EXPECT_EQ(violations, 0);
        }

        // A color grid is three float grids; its spectrum bound holds at every wavelength
        [TEST("SpectrumGridTexture", "BoundsValid")]
        public static void SpectrumGridTexture_BoundsValid()
        {
            FloatGridTexture r = MakeGrid(4, 1f, 0f), g = MakeGrid(5, 2f, 0f), b = MakeGrid(6, .5f, 0f);
            SpectrumGridTexture texture = new SpectrumGridTexture(r, g, b, SpectrumType.Unbounded, RGBColorSpace.sRGB);
            RNG rng = new RNG(16);
            int violations = 0;
            for (int i = 0; i < 600; ++i)
            {
                Point3f c = new Point3f(-1 + 3 * rng.Uniform<Float>(), -1 + 3 * rng.Uniform<Float>(), -1 + 3 * rng.Uniform<Float>());
                Vector3f e = new Vector3f(.3f * rng.Uniform<Float>(), .3f * rng.Uniform<Float>(), .3f * rng.Uniform<Float>());
                Float bound = texture.RegionMax(new Bounds3f(c, c + e));
                for (int k = 0; k < 10; ++k)
                {
                    TextureEvalContext ctx = new TextureEvalContext();
                    ctx.p = new Point3f(c.x + e.x * rng.Uniform<Float>(), c.y + e.y * rng.Uniform<Float>(), c.z + e.z * rng.Uniform<Float>());
                    SampledSpectrum value = texture.Evaluate(ctx, SampledWavelengths.SampleVisible(rng.Uniform<Float>()));
                    for (int w = 0; w < 4; ++w)
                        if (value[w] > bound + 1e-4f || value[w] > texture.MaxValue() + 1e-4f)
                            ++violations;
                }
            }
            Console.WriteLine($"PROBE spectrum grid: violations {violations}");
            EXPECT_EQ(violations, 0);
        }

        private static FloatImageTexture MakeProjectedImage(string name, WrapMode wrap, Float scale)
        {
            Point2i res = new Point2i(32, 16);
            Image image = new Image(PixelFormat.Float, res, new string[] { "R", "G", "B" });
            RNG rng = new RNG(5);
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    Float u = (x + .5f) / res.x - .5f, v = (y + .5f) / res.y - .5f;
                    Float value = std.max<Float>(0, (Float)Math.Exp(-(u * u + v * v) * 12) - .1f + .1f * (rng.Uniform<Float>() - .5f));
                    for (int c = 0; c < 3; ++c)
                        image.SetChannel(new Point2i(x, y), c, value);
                }
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
            EXPECT_TRUE(image.Write(file));
            MIPMapFilterOptions options = new MIPMapFilterOptions();
            options.filter = FilterFunction.Bilinear;
            TextureMapping2D mapping = new PlanarMapping(new Transform(), new Vector3f(1, 0, 0), new Vector3f(0, 1, 0), 0, 0);
            return new FloatImageTexture(mapping, file, options, wrap, scale, false, ColorEncoding.Linear, null);
        }

        // The triplanar texture is the weighted sum of three projections of the image; its region bounds hold
        [TEST("FloatTriplanarTexture", "RegionBoundsValid")]
        public static void FloatTriplanarTexture_RegionBoundsValid()
        {
            foreach (WrapMode wrap in new WrapMode[] { WrapMode.Clamp, WrapMode.Repeat, WrapMode.Black })
            {
                FloatImageTexture image = MakeProjectedImage("magi_triplanar_test_" + wrap + ".exr", wrap, 2f);
                Transform renderFromTexture = Translate(new Vector3f(.3f, .1f, -.2f)) * Scale(1.3f, 1.3f, 1.3f);
                FloatTriplanarTexture texture = new FloatTriplanarTexture(new PointTransformMapping(Inverse(renderFromTexture)),
                                                                          image, 1f, 2f, .5f);
                RNG rng = new RNG(6);
                int violations = 0, tighter = 0;
                for (int i = 0; i < 1500; ++i)
                {
                    Point3f c = new Point3f(-1 + 3 * rng.Uniform<Float>(), -1 + 3 * rng.Uniform<Float>(), -1 + 3 * rng.Uniform<Float>());
                    Vector3f e = new Vector3f(.3f * rng.Uniform<Float>(), .3f * rng.Uniform<Float>(), .3f * rng.Uniform<Float>());
                    texture.RegionBounds(new Bounds3f(c, c + e), out Float lo, out Float hi);
                    if (hi < texture.MaxValue() - 1e-6f)
                        ++tighter;
                    for (int k = 0; k < 40; ++k)
                    {
                        TextureEvalContext ctx = new TextureEvalContext();
                        ctx.p = new Point3f(c.x + e.x * rng.Uniform<Float>(), c.y + e.y * rng.Uniform<Float>(), c.z + e.z * rng.Uniform<Float>());
                        Float v = texture.Evaluate(ctx);
                        if (v < lo - 1e-5f || v > hi + 1e-5f)
                            ++violations;
                    }
                }
                Console.WriteLine($"PROBE triplanar {wrap}: violations {violations}, tighter {tighter}/1500");
                EXPECT_EQ(violations, 0);
                EXPECT_GT(tighter, 100);
            }
        }

        // A spectrum texture that varies in space, for the textured-coefficient test
        private sealed class WaveSpectrumTexture : SpectrumTexture
        {
            public override string ToString() { return "[ WaveSpectrumTexture ]"; }
            public override Float MaxValue() { return 1.5f; }
            public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
            {
                Float v = .75f + .75f * (Float)Math.Sin(7 * ctx.p.x + 3 * ctx.p.y - 5 * ctx.p.z);
                return new SampledSpectrum(v);
            }
        }

        private sealed class WaveFloatTexture : FloatTexture
        {
            public override string ToString() { return "[ WaveFloatTexture ]"; }
            public override Float MinValue() { return -.5f; }
            public override Float MaxValue() { return .5f; }
            public override Float Evaluate(TextureEvalContext ctx) { return .5f * (Float)Math.Sin(4 * ctx.p.x - 2 * ctx.p.z); }
        }

        // Ratio tracking through a medium whose sigma_a, sigma_s (and g) are textures matches quadrature of the
        // coefficients, the majorants hold, and the g texture reaches the phase function
        [TEST("TextureMedium", "TexturedCoefficientsUnbiased")]
        public static void TextureMedium_TexturedCoefficientsUnbiased()
        {
            NamedTextures textures = new NamedTextures();
            textures.floatTextures["density"] = new FBmTexture(new PointTransformMapping(Scale(1.6f, 1.6f, 1.6f)), 4, .5f);
            textures.unboundedSpectrumTextures["wave"] = new WaveSpectrumTexture();
            textures.floatTextures["asym"] = new WaveFloatTexture();
            TextureMedium medium = new TextureMedium(new Bounds3f(new Point3f(-.8f, .2f, -.8f), new Point3f(.8f, 1.8f, .8f)),
                                                     new Transform(), new ConstantSpectrum(0.05f), new ConstantSpectrum(2f), 0.3f,
                                                     null, 0.05f, 8f, 8, false, 0.01f, 0, false, false, false, false, false, false, 4f,
                                                     .5f, 0f, false, 4, 2, .7f, false, 4, 0f, true, "density", "wave", "wave", "asym", "", 1f, false, 0f, true,
                                                     null);
            medium.ResolveTextures(textures, new FileLoc());
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);

            // g is read per point
            MediumProperties at = medium.SamplePoint(new Point3f(.4f, 1f, -.3f), lambda);
            Float expectedG = .5f * (Float)Math.Sin(4 * .4f - 2 * -.3f);
            Float cosTheta = .6f;
            Vector3f wo = new Vector3f(0, 0, 1), wi = new Vector3f(std.sqrt(1 - cosTheta * cosTheta), 0, cosTheta);
            EXPECT_LT(Math.Abs(at.phase.p(wo, wi) - HenyeyGreenstein(Dot(wo, wi), expectedG)), 1e-5f);

            RNG rng = new RNG(8);
            int violations = 0;
            for (int i = 0; i < 300; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .5f * (rng.Uniform<Float>() - .5f), .5f * (rng.Uniform<Float>() - .5f)));
                RayMajorantIterator it = medium.SampleRay(new Ray(o, d, 0, medium), 2.2f, lambda);
                for (pstd.optional<RayMajorantSegment> seg = it.Next(); seg; seg = it.Next())
                {
                    RayMajorantSegment sg = seg.value();
                    for (int k = 0; k < 6; ++k)
                    {
                        Float t = sg.tMin + (sg.tMax - sg.tMin) * rng.Uniform<Float>();
                        MediumProperties mp = medium.SamplePoint(new Point3f(o.x + t * d.x, o.y + t * d.y, o.z + t * d.z), lambda);
                        if (mp.sigma_a[0] + mp.sigma_s[0] > sg.sigma_maj[0] * (1 + 1e-3f) + 1e-4f)
                            ++violations;
                    }
                }
            }
            Console.WriteLine($"PROBE textured coefficients: majorant violations {violations}");
            EXPECT_EQ(violations, 0);

            for (int i = 0; i < 3; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                Float refT = ReferenceTransmittance(medium, new Ray(o, d, 0, medium), 2.2f, lambda);
                int n = 100000;
                double sum = 0, sum2 = 0;
                for (int s = 0; s < n; ++s)
                {
                    Float q = RatioTracking(medium, new Ray(o, d, 0, medium), 2.2f, lambda, ref rng);
                    sum += q;
                    sum2 += q * q;
                }
                double mean = sum / n, sigma = Math.Sqrt(Math.Max(sum2 / n - mean * mean, 0) / n);
                Console.WriteLine($"PROBE textured coefficients: mean {mean} reference {refT} stderr {sigma}");
                EXPECT_LT(Math.Abs(mean - refT), 4 * sigma + 1e-3);
            }
        }

        // Progressive tracking of a medium whose coefficients are scaled by textures learns majorants that cover the
        // textured extinction, not just the density
        [TEST("TextureMedium", "ProgressiveTexturedMajorantsCover")]
        public static void TextureMedium_ProgressiveTexturedMajorantsCover()
        {
            TextureMedium.ClearProgressive();
            NamedTextures textures = new NamedTextures();
            textures.floatTextures["density"] = new FBmTexture(new PointTransformMapping(Scale(1.6f, 1.6f, 1.6f)), 4, .5f);
            textures.unboundedSpectrumTextures["wave"] = new WaveSpectrumTexture();
            Bounds3f box = new Bounds3f(new Point3f(-.8f, .2f, -.8f), new Point3f(.8f, 1.8f, .8f));
            TextureMedium reference = new TextureMedium(box, new Transform(), new ConstantSpectrum(0.05f), new ConstantSpectrum(2f), 0.3f,
                                                        null, 0.05f, 8f, 8, false, 0.01f, 0, false, false, false, false, false, false, 4f,
                                                        .5f, 0f, false, 4, 2, .7f, false, 4, 0f, true, "density", "wave", "wave", "", "", 1f,
                                                        false, 0f, true, null);
            reference.ResolveTextures(textures, new FileLoc());
            // Not unclamped on purpose: textured coefficients switch progressive tracking to signed null coefficients
            TextureMedium progressive = new TextureMedium(box, new Transform(), new ConstantSpectrum(0.05f), new ConstantSpectrum(2f), 0.3f,
                                                          null, 0.05f, 8f, 8, true, 0.2f, 0, false, false, false, false, false, false, 4f,
                                                          .5f, 0f, false, 4, 2, .7f, false, 4, 0f, true, "density", "wave", "wave", "", "", 1f,
                                                          false, 0f, true, null);
            progressive.ResolveTextures(textures, new FileLoc());
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(10);
            int res = 8;
            Float[] trueMax = new Float[res * res * res];
            for (int i = 0; i < 400000; ++i)
            {
                Point3f p = new Point3f(-.8f + 1.6f * rng.Uniform<Float>(), .2f + 1.6f * rng.Uniform<Float>(),
                                        -.8f + 1.6f * rng.Uniform<Float>());
                int vx = std.min((int)((p.x + .8f) / 1.6f * res), res - 1), vy = std.min((int)((p.y - .2f) / 1.6f * res), res - 1);
                int vz = std.min((int)((p.z + .8f) / 1.6f * res), res - 1);
                int v = vx + res * (vy + res * vz);
                MediumProperties mp = reference.SamplePoint(p, lambda);
                for (int k = 0; k < NSpectrumSamples; ++k)
                    trueMax[v] = std.max(trueMax[v], (mp.sigma_a[k] + mp.sigma_s[k]) / 2.05f);
            }
            for (int pass = 1; pass <= 64; ++pass)
            {
                for (int s = 0; s < 40000; ++s)
                {
                    Point3f o = new Point3f(-1.2f + 2.4f * rng.Uniform<Float>(), -.2f + 2.4f * rng.Uniform<Float>(),
                                            -1.2f + 2.4f * rng.Uniform<Float>());
                    Vector3f d = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    RatioTracking(progressive, new Ray(o, d, 0, progressive), 3f, lambda, ref rng);
                }
                TextureMedium.EndProgressivePass();
            }
            Float[] learned = progressive.DebugMajorants();
            int considered = 0, covered = 0;
            for (int v = 0; v < learned.Length; ++v)
            {
                if (trueMax[v] < 0.05f)
                    continue;
                ++considered;
                if (learned[v] >= 0.9f * trueMax[v])
                    ++covered;
            }
            Console.WriteLine($"PROBE progressive textured majorants: {covered} of {considered} voxels covered");
            TextureMedium.ClearProgressive();
            EXPECT_GT(considered, 100);
            EXPECT_GE(covered, (int)(0.95f * considered));
        }

        private sealed class GlowSpectrumTexture : SpectrumTexture
        {
            public override string ToString() { return "[ GlowSpectrumTexture ]"; }
            public override Float MaxValue() { return 2f; }
            public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
            {
                return new SampledSpectrum(1f + (Float)Math.Sin(5 * ctx.p.y));
            }
        }

        // The emission texture sets the medium's Le, and a medium without one isn't emissive
        [TEST("TextureMedium", "TexturedEmission")]
        public static void TextureMedium_TexturedEmission()
        {
            NamedTextures textures = new NamedTextures();
            textures.floatTextures["density"] = new FBmTexture(new PointTransformMapping(Scale(1.6f, 1.6f, 1.6f)), 4, .5f);
            textures.illuminantSpectrumTextures["glow"] = new GlowSpectrumTexture();
            TextureMedium glowing = new TextureMedium(new Bounds3f(new Point3f(-.8f, .2f, -.8f), new Point3f(.8f, 1.8f, .8f)),
                                                      new Transform(), new ConstantSpectrum(0.05f), new ConstantSpectrum(2f), 0.3f,
                                                      null, 0.05f, 8f, 8, false, 0.01f, 0, false, false, false, false, false, false, 4f,
                                                      .5f, 0f, false, 4, 2, .7f, false, 4, 0f, true, "density", "", "", "", "glow", 3f, false, 0f, true,
                                                      null);
            glowing.ResolveTextures(textures, new FileLoc());
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            Point3f p = new Point3f(.1f, 1.1f, .2f);
            MediumProperties mp = glowing.SamplePoint(p, lambda);
            EXPECT_TRUE(glowing.IsEmissive());
            EXPECT_LT(Math.Abs(mp.Le[0] - 3f * (1f + (Float)Math.Sin(5 * 1.1f))), 1e-4f);
            TextureMedium plain = new TextureMedium(new Bounds3f(new Point3f(-.8f, .2f, -.8f), new Point3f(.8f, 1.8f, .8f)),
                                                    new Transform(), new ConstantSpectrum(0.05f), new ConstantSpectrum(2f), 0.3f,
                                                    null, 0.05f, 8f, 8, false, 0.01f, 0, false, false, false, false, false, false, 4f,
                                                    .5f, 0f, false, 4, 2, .7f, false, 4, 0f, true, "density", "", "", "", "", 1f, false, 0f, true,
                                                    null);
            plain.ResolveTextures(textures, new FileLoc());
            EXPECT_FALSE(plain.IsEmissive());
            EXPECT_EQ(plain.SamplePoint(p, lambda).Le[0], 0f);
        }

        // With a constant density of 1 and unit base spectra, the coefficient textures are the coefficients, as in
        // LuxCore's heterogeneous volume and pbrt-v4's RGBGridMedium
        [TEST("TextureMedium", "CoefficientTexturesAreCoefficients")]
        public static void TextureMedium_CoefficientTexturesAreCoefficients()
        {
            NamedTextures textures = new NamedTextures();
            textures.floatTextures["one"] = new FloatConstantTexture(1f);
            textures.unboundedSpectrumTextures["wave"] = new WaveSpectrumTexture();
            TextureMedium medium = new TextureMedium(new Bounds3f(new Point3f(-.8f, .2f, -.8f), new Point3f(.8f, 1.8f, .8f)),
                                                     new Transform(), new ConstantSpectrum(1f), new ConstantSpectrum(1f), 0f,
                                                     null, 0f, 1f, 4, false, 0.01f, 0, false, false, false, false, false, false, 4f,
                                                     .5f, 0f, false, 4, 2, .7f, false, 4, 0f, true, "one", "wave", "wave", "", "", 1f, false, 0f, true,
                                                     null);
            medium.ResolveTextures(textures, new FileLoc());
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(9);
            Float maxDifference = 0;
            for (int i = 0; i < 1000; ++i)
            {
                Point3f p = new Point3f(-.8f + 1.6f * rng.Uniform<Float>(), .2f + 1.6f * rng.Uniform<Float>(), -.8f + 1.6f * rng.Uniform<Float>());
                Float expected = .75f + .75f * (Float)Math.Sin(7 * p.x + 3 * p.y - 5 * p.z);
                MediumProperties mp = medium.SamplePoint(p, lambda);
                maxDifference = std.max(maxDifference, std.max(std.abs(mp.sigma_a[0] - expected), std.abs(mp.sigma_s[0] - expected)));
            }
            Console.WriteLine($"PROBE coefficient textures: max |sigma - texture| {maxDifference}");
            EXPECT_LT(maxDifference, 1e-5f);
        }

        // Per-length emission is gathered at every free-path sample and matches the quadrature of T(t) E(t), including
        // where the density (and so the extinction) is zero
        [TEST("TextureMedium", "PerLengthEmissionUnbiased")]
        public static void TextureMedium_PerLengthEmissionUnbiased()
        {
            NamedTextures textures = new NamedTextures();
            textures.floatTextures["density"] = new FBmTexture(new PointTransformMapping(Scale(1.6f, 1.6f, 1.6f)), 4, .5f);
            textures.illuminantSpectrumTextures["glow"] = new GlowSpectrumTexture();
            // The offset makes the density zero over part of the volume, where only the emission majorant is positive
            TextureMedium medium = new TextureMedium(new Bounds3f(new Point3f(-.8f, .2f, -.8f), new Point3f(.8f, 1.8f, .8f)),
                                                     new Transform(), new ConstantSpectrum(0.05f), new ConstantSpectrum(2f), 0.3f,
                                                     null, -.15f, 8f, 8, false, 0.01f, 0, false, false, false, false, false, false, 4f,
                                                     .5f, 0f, false, 4, 2, .7f, false, 4, 0f, true, "density", "", "", "", "glow", 1f,
                                                     true, 1f, true, null);
            medium.ResolveTextures(textures, new FileLoc());
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(12);
            int zeroDensityPoints = 0;
            for (int i = 0; i < 4; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                Ray ray = new Ray(o, d, 0, medium);
                // Quadrature of T(t) E(t)
                int n = 20000;
                double tau = 0, reference = 0;
                for (int k = 0; k < n; ++k)
                {
                    Float t = (k + .5f) / n * 2.2f;
                    Point3f p = ray.Apply(t);
                    if (p.x < -.8f || p.x > .8f || p.y < .2f || p.y > 1.8f || p.z < -.8f || p.z > .8f)
                        continue;
                    MediumProperties mp = medium.SamplePoint(p, lambda);
                    double sigma = mp.sigma_a[0] + mp.sigma_s[0];
                    if (sigma == 0 && mp.Le[0] > 0)
                        ++zeroDensityPoints;
                    reference += Math.Exp(-tau) * mp.Le[0] * 2.2 / n;
                    tau += sigma * 2.2 / n;
                }
                int samples = 200000;
                double sum = 0, sum2 = 0;
                for (int s = 0; s < samples; ++s)
                {
                    Float beta = 1, estimate = 0;
                    pbrt.SampleT_maj(ray, 2.2f, rng.Uniform<Float>(), ref rng, lambda,
                        (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                        {
                            estimate += beta * mp.Le[0] / sigma_maj[0];
                            beta *= std.max<Float>(0, sigma_maj[0] - mp.sigma_a[0] - mp.sigma_s[0]) / sigma_maj[0];
                            return beta > 0;
                        });
                    sum += estimate;
                    sum2 += estimate * estimate;
                }
                double mean = sum / samples, stderr = Math.Sqrt(Math.Max(sum2 / samples - mean * mean, 0) / samples);
                Console.WriteLine($"PROBE per-length emission: mean {mean} reference {reference} stderr {stderr}");
                EXPECT_LT(Math.Abs(mean - reference), 4 * stderr + 1e-3);
            }
            Console.WriteLine($"PROBE per-length emission: quadrature points with emission but no extinction {zeroDensityPoints}");
            EXPECT_GT(zeroDensityPoints, 100);
        }

        private static Float ReferenceTransmittance(TextureMedium medium, Ray ray, Float tMax, SampledWavelengths lambda)
        {
            int n = 20000;
            Float sum = 0;
            for (int i = 0; i < n; ++i)
            {
                Float t = (i + .5f) / n * tMax;
                Point3f p = ray.Apply(t);
                if (p.x < -.8f || p.x > .8f || p.y < .2f || p.y > 1.8f || p.z < -.8f || p.z > .8f)
                    continue;
                MediumProperties mp = medium.SamplePoint(p, lambda);
                sum += mp.sigma_a[0] + mp.sigma_s[0];
            }
            return (Float)Math.Exp(-sum * tMax / n);
        }

        private static Float RatioTracking(TextureMedium medium, Ray ray, Float tMax, SampledWavelengths lambda, ref RNG rng)
        {
            Float T_ray = 1;
            SampledSpectrum T_maj = pbrt.SampleT_maj(ray, tMax, rng.Uniform<Float>(), ref rng, lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    Float sigma_n = std.max<Float>(0, sigma_maj[0] - mp.sigma_a[0] - mp.sigma_s[0]);
                    T_ray *= sigma_n / sigma_maj[0];
                    return T_ray > 0;
                });
            return T_ray;
        }
    }
}
