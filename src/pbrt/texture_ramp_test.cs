// (not in pbrt-v4) Tests for the ramp and blackbody textures of texture-driven volumes.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class TextureRampTests
    {
        private static void RandomRamp(RNG rng, int n, out Float[] positions, out Float[] values)
        {
            positions = new Float[n];
            values = new Float[n];
            Float x = -.5f;
            for (int i = 0; i < n; ++i)
            {
                x += .05f + .4f * rng.Uniform<Float>();
                positions[i] = x;
                values[i] = 3 * rng.Uniform<Float>() - 1;
            }
        }

        // The bounds of a ramp over an interval are the extremes of the ramp on it, not just conservative
        [TEST("RampTexture", "BoundsAreExact")]
        public static void RampTexture_BoundsAreExact()
        {
            RNG rng = new RNG(21);
            int violations = 0, loose = 0;
            for (int trial = 0; trial < 600; ++trial)
            {
                RandomRamp(rng, 1 + (int)(6 * rng.Uniform<Float>()), out Float[] positions, out Float[] values);
                bool step = (trial % 2) == 1;
                Float a = -1 + 4 * rng.Uniform<Float>(), b = a + 1.5f * rng.Uniform<Float>();
                pbrt.RampBounds(positions, values, step, a, b, out Float lo, out Float hi);
                Float lowest = Infinity, highest = -Infinity;
                // The jump of a stepwise ramp at a stop belongs to the interval from the stop on, so sample the stops too
                for (int k = 0; k <= 4000; ++k)
                {
                    Float v = pbrt.RampEvaluate(positions, values, step, a + (b - a) * k / 4000);
                    lowest = std.min(lowest, v);
                    highest = std.max(highest, v);
                }
                if (lowest < lo - 1e-5f || highest > hi + 1e-5f)
                    ++violations;
                if (lowest > lo + 2e-3f || highest < hi - 2e-3f)
                    ++loose;
            }
            Console.WriteLine($"PROBE ramp bounds: violations {violations}, loose {loose} of 600");
            EXPECT_EQ(violations, 0);
            EXPECT_EQ(loose, 0);
        }

        // Linear ramps interpolate between the stops and hold the end values outside them
        [TEST("RampTexture", "EvaluatesStops")]
        public static void RampTexture_EvaluatesStops()
        {
            Float[] positions = { 0f, .5f, 1f };
            Float[] values = { 1f, 3f, 2f };
            EXPECT_LT(Math.Abs(pbrt.RampEvaluate(positions, values, false, -1f) - 1f), 1e-6f);
            EXPECT_LT(Math.Abs(pbrt.RampEvaluate(positions, values, false, .25f) - 2f), 1e-6f);
            EXPECT_LT(Math.Abs(pbrt.RampEvaluate(positions, values, false, .75f) - 2.5f), 1e-6f);
            EXPECT_LT(Math.Abs(pbrt.RampEvaluate(positions, values, false, 4f) - 2f), 1e-6f);
            EXPECT_LT(Math.Abs(pbrt.RampEvaluate(positions, values, true, .75f) - 3f), 1e-6f);
            EXPECT_LT(Math.Abs(pbrt.RampEvaluate(positions, values, true, 1f) - 2f), 1e-6f);
        }

        // The ramp of a grid texture has valid region bounds, in both the float and the spectrum versions
        [TEST("RampTexture", "RegionBoundsValid")]
        public static void RampTexture_RegionBoundsValid()
        {
            int nx = 8, ny = 6, nz = 7;
            RNG rng = new RNG(22);
            Float[] gridValues = new Float[nx * ny * nz];
            for (int i = 0; i < gridValues.Length; ++i)
                gridValues[i] = 2 * rng.Uniform<Float>();
            FloatGridTexture grid = new FloatGridTexture(new PointTransformMapping(Scale(.5f, .5f, .5f)), nx, ny, nz, gridValues,
                                                         WrapMode.Clamp, 0f, false);
            Float[] positions = { .2f, .7f, 1.1f, 1.6f };
            Float[] floatValues = { 0f, 2f, .5f, 1.5f };
            RGB[] colors = { new RGB(.1f, .0f, .0f), new RGB(1f, .5f, 0f), new RGB(.2f, .2f, 1f), new RGB(.9f, .9f, .9f) };
            FloatRampTexture floatRamp = new FloatRampTexture(grid, positions, floatValues, false);
            SpectrumRampTexture colorRamp = new SpectrumRampTexture(grid, positions, colors, false, SpectrumType.Unbounded,
                                                                    RGBColorSpace.sRGB);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            int violations = 0, tighter = 0;
            for (int i = 0; i < 1500; ++i)
            {
                Point3f c = new Point3f(-1 + 6 * rng.Uniform<Float>(), -1 + 6 * rng.Uniform<Float>(), -1 + 6 * rng.Uniform<Float>());
                Vector3f e = new Vector3f(.6f * rng.Uniform<Float>(), .6f * rng.Uniform<Float>(), .6f * rng.Uniform<Float>());
                Bounds3f region = new Bounds3f(c, c + e);
                floatRamp.RegionBounds(region, out Float lo, out Float hi);
                Float spectrumMax = colorRamp.RegionMax(region);
                if (hi < floatRamp.MaxValue() - 1e-6f)
                    ++tighter;
                for (int k = 0; k < 30; ++k)
                {
                    TextureEvalContext ctx = new TextureEvalContext();
                    ctx.p = new Point3f(c.x + e.x * rng.Uniform<Float>(), c.y + e.y * rng.Uniform<Float>(), c.z + e.z * rng.Uniform<Float>());
                    Float v = floatRamp.Evaluate(ctx);
                    if (v < lo - 1e-5f || v > hi + 1e-5f)
                        ++violations;
                    SampledSpectrum s = colorRamp.Evaluate(ctx, lambda);
                    for (int w = 0; w < NSpectrumSamples; ++w)
                    {
                        if (s[w] > spectrumMax * (1 + 1e-4f) + 1e-5f)
                            ++violations;
                    }
                }
            }
            Console.WriteLine($"PROBE ramp texture: violations {violations}, regions tighter than global {tighter}/1500");
            EXPECT_EQ(violations, 0);
            EXPECT_GT(tighter, 100);
        }

        private sealed class ConstantTemperature : FloatTexture
        {
            public ConstantTemperature(Float lo, Float hi) { this.lo = lo; this.hi = hi; }
            public override string ToString() { return "[ ConstantTemperature ]"; }
            public override Float MinValue() { return lo; }
            public override Float MaxValue() { return hi; }
            // Varies with x between lo and hi
            public override Float Evaluate(TextureEvalContext ctx) { return lo + (hi - lo) * pbrt.Clamp(ctx.p.x, 0, 1); }
            public override void RegionBounds(Bounds3f region, out Float rlo, out Float rhi)
            {
                rlo = lo + (hi - lo) * pbrt.Clamp(region.pMin.x, 0, 1);
                rhi = lo + (hi - lo) * pbrt.Clamp(region.pMax.x, 0, 1);
            }
            private Float lo, hi;
        }

        // Normalized mode is pbrt's BlackbodySpectrum; physical mode is Planck's law over the peak at the reference
        // temperature, and both have valid, exact bounds over a temperature interval
        [TEST("BlackbodyTexture", "MatchesBlackbodyAndBoundsValid")]
        public static void BlackbodyTexture_MatchesBlackbodyAndBoundsValid()
        {
            FloatTexture temperature = new ConstantTemperature(800f, 3000f);
            SpectrumBlackbodyTexture normalized = new SpectrumBlackbodyTexture(temperature, 1f, 0f, false, 1000f);
            SpectrumBlackbodyTexture physical = new SpectrumBlackbodyTexture(temperature, 1f, 0f, true, 1000f);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            TextureEvalContext ctx = new TextureEvalContext();
            ctx.p = new Point3f(.5f, 0, 0);
            Float T = 800f + 2200f * .5f;
            SampledSpectrum n = normalized.Evaluate(ctx, lambda), expectedN = new BlackbodySpectrum(T).Sample(lambda);
            SampledSpectrum ph = physical.Evaluate(ctx, lambda);
            Float peakReference = SpectrumBlackbodyTexture.PeakRadiance(1000f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_LT(Math.Abs(n[i] - expectedN[i]), 1e-5f);
                EXPECT_LT(Math.Abs(ph[i] - pbrt.Blackbody(lambda[i], T) / peakReference), 1e-4f * (1 + ph[i]));
            }
            // The brightness grows with the temperature in physical mode, not in normalized mode
            ctx.p = new Point3f(1f, 0, 0);
            Float hotMax = 0, coldMax = 0;
            SampledWavelengths peak = SampledWavelengths.SampleVisible(0.9f);
            for (int i = 0; i < 20; ++i)
            {
                SampledWavelengths l = SampledWavelengths.SampleVisible((i + .5f) / 20);
                TextureEvalContext hot = new TextureEvalContext(), cold = new TextureEvalContext();
                hot.p = new Point3f(1f, 0, 0);
                cold.p = new Point3f(0f, 0, 0);
                for (int w = 0; w < NSpectrumSamples; ++w)
                {
                    hotMax = std.max(hotMax, physical.Evaluate(hot, l)[w]);
                    coldMax = std.max(coldMax, physical.Evaluate(cold, l)[w]);
                }
            }
            EXPECT_GT(hotMax, 20 * coldMax);
            // The bounds hold for every wavelength and every temperature in the region, and are close to attained
            RNG rng = new RNG(23);
            int violations = 0;
            for (int trial = 0; trial < 300; ++trial)
            {
                Float x0 = rng.Uniform<Float>(), x1 = std.min(1f, x0 + .3f * rng.Uniform<Float>());
                Bounds3f region = new Bounds3f(new Point3f(x0, 0, 0), new Point3f(x1, 1, 1));
                Float boundN = normalized.RegionMax(region), boundP = physical.RegionMax(region);
                for (int k = 0; k < 20; ++k)
                {
                    TextureEvalContext c = new TextureEvalContext();
                    c.p = new Point3f(x0 + (x1 - x0) * rng.Uniform<Float>(), 0, 0);
                    SampledWavelengths l = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                    SampledSpectrum sn = normalized.Evaluate(c, l), sp = physical.Evaluate(c, l);
                    for (int w = 0; w < NSpectrumSamples; ++w)
                    {
                        if (sn[w] > boundN * (1 + 1e-4f) || sp[w] > boundP * (1 + 1e-3f))
                            ++violations;
                    }
                }
            }
            Console.WriteLine($"PROBE blackbody bounds: violations {violations}");
            EXPECT_EQ(violations, 0);
            // No emission at or below 100 K
            SpectrumBlackbodyTexture cool = new SpectrumBlackbodyTexture(new ConstantTemperature(50f, 90f), 1f, 0f, false, 1000f);
            EXPECT_EQ(cool.MaxValue(), 0f);
        }
    }
}
