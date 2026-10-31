// (not in pbrt-v4) Ramp ("curve") and blackbody textures for texture-driven volumes.
//
// A ramp remaps a scalar texture, typically a density or a temperature, through a piecewise-linear (or stepwise)
// function given by stops, into a float or a color. The bounds over a region follow from the bounds of the input:
// a piecewise-linear function attains its extremes at the ends of the input interval or at a stop inside it, so
// the bounds are exact rather than conservative. The blackbody texture turns a temperature texture into the
// spectrum of a blackbody emitter; Planck's law increases with temperature at every wavelength, so its bounds over a
// temperature interval are exact too.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // The value of the ramp at x: the end values outside the stops, a linear blend between stops, or the value of
        // the last stop at or below x for a stepwise ramp
        public static Float RampEvaluate(Float[] positions, Float[] values, bool step, Float x)
        {
            int n = positions.Length;
            if (x <= positions[0])
                return values[0];
            if (x >= positions[n - 1])
                return values[n - 1];
            int i = 0;
            while (i + 1 < n && positions[i + 1] <= x)
                ++i;
            if (step)
                return values[i];
            Float t = (x - positions[i]) / (positions[i + 1] - positions[i]);
            return values[i] + t * (values[i + 1] - values[i]);
        }

        // The smallest and largest value of the ramp for x in [a, b]
        public static void RampBounds(Float[] positions, Float[] values, bool step, Float a, Float b, out Float lo, out Float hi)
        {
            if (a > b)
            {
                Float swap = a;
                a = b;
                b = swap;
            }
            lo = hi = RampEvaluate(positions, values, step, a);
            Float atB = RampEvaluate(positions, values, step, b);
            lo = std.min(lo, atB);
            hi = std.max(hi, atB);
            for (int i = 0; i < positions.Length; ++i)
            {
                // A linear ramp turns at every stop inside (a, b); a stepwise ramp jumps to the value of a stop in (a, b]
                bool inside = step ? (positions[i] > a && positions[i] <= b) : (positions[i] > a && positions[i] < b);
                if (inside)
                {
                    lo = std.min(lo, values[i]);
                    hi = std.max(hi, values[i]);
                }
            }
        }

        public static void CheckRampStops(Float[] positions, int nValues, string name, FileLoc loc)
        {
            if (positions.Length == 0 || positions.Length != nValues)
                ErrorExit(loc, StringPrintf("%s: \"positions\" has %d entries and the values %d; they must match and be non-empty.",
                                            name, positions.Length, nValues));
            for (int i = 1; i < positions.Length; ++i)
            {
                if (!(positions[i] > positions[i - 1]))
                    ErrorExit(loc, StringPrintf("%s: \"positions\" must increase strictly.", name));
            }
        }
    }

    // FloatRampTexture Definition
    public sealed unsafe class FloatRampTexture : FloatTexture
    {
        public FloatRampTexture(FloatTexture tex, Float[] positions, Float[] values, bool step)
        {
            this.tex = tex;
            this.positions = positions;
            this.values = values;
            this.step = step;
        }

        public override Float MinValue()
        {
            pbrt.RampBounds(positions, values, step, tex.MinValue(), tex.MaxValue(), out Float lo, out Float hi);
            return lo;
        }

        public override Float MaxValue()
        {
            pbrt.RampBounds(positions, values, step, tex.MinValue(), tex.MaxValue(), out Float lo, out Float hi);
            return hi;
        }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            tex.RegionBounds(region, out Float tlo, out Float thi);
            pbrt.RampBounds(positions, values, step, tlo, thi, out lo, out hi);
        }

        public override Float Evaluate(TextureEvalContext ctx) { return pbrt.RampEvaluate(positions, values, step, tex.Evaluate(ctx)); }

        public override string ToString() { return pbrt.StringPrintf("[ FloatRampTexture tex: %s stops: %d ]", tex, positions.Length); }

        public static FloatRampTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                              Allocator alloc)
        {
            Float[] positions = parameters.GetFloatArray("positions").ToArray();
            Float[] values = parameters.GetFloatArray("values").ToArray();
            pbrt.CheckRampStops(positions, values.Length, "ramp", loc);
            string interpolation = parameters.GetOneString("interpolation", "linear");
            if (interpolation != "linear" && interpolation != "step")
                pbrt.ErrorExit(loc, pbrt.StringPrintf("ramp: interpolation \"%s\" unknown; use \"linear\" or \"step\".", interpolation));
            return new FloatRampTexture(parameters.GetFloatTexture("tex", 0f, alloc), positions, values, interpolation == "step");
        }

        private FloatTexture tex;
        private Float[] positions, values;
        private bool step;
    }

    // SpectrumRampTexture Definition
    // The stops are RGB colors that are blended in RGB; the blend becomes a spectrum of the requested type
    public sealed unsafe class SpectrumRampTexture : SpectrumTexture
    {
        public SpectrumRampTexture(FloatTexture tex, Float[] positions, RGB[] values, bool step, SpectrumType spectrumType,
                                   RGBColorSpace colorSpace)
        {
            this.tex = tex;
            this.positions = positions;
            this.step = step;
            this.spectrumType = spectrumType;
            this.colorSpace = colorSpace;
            channels = new Float[3][] { new Float[values.Length], new Float[values.Length], new Float[values.Length] };
            for (int i = 0; i < values.Length; ++i)
            {
                channels[0][i] = values[i].r;
                channels[1][i] = values[i].g;
                channels[2][i] = values[i].b;
            }
        }

        private Float MaxComponent(Float a, Float b)
        {
            Float m = 0;
            for (int c = 0; c < 3; ++c)
            {
                pbrt.RampBounds(positions, channels[c], step, a, b, out Float lo, out Float hi);
                m = std.max(m, hi);
            }
            return pbrt.RGBSpectrumBound(spectrumType, colorSpace, m);
        }

        public override Float MaxValue() { return MaxComponent(tex.MinValue(), tex.MaxValue()); }

        public override Float RegionMax(Bounds3f region)
        {
            tex.RegionBounds(region, out Float lo, out Float hi);
            return MaxComponent(lo, hi);
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float x = tex.Evaluate(ctx);
            RGB rgb = pbrt.ClampZero(new RGB(pbrt.RampEvaluate(positions, channels[0], step, x),
                                              pbrt.RampEvaluate(positions, channels[1], step, x),
                                              pbrt.RampEvaluate(positions, channels[2], step, x)));
            if (spectrumType == SpectrumType.Unbounded)
                return new RGBUnboundedSpectrum(colorSpace, rgb).Sample(lambda);
            if (spectrumType == SpectrumType.Albedo)
                return new RGBAlbedoSpectrum(colorSpace, pbrt.Clamp(rgb, 0, 1)).Sample(lambda);
            return new RGBIlluminantSpectrum(colorSpace, rgb).Sample(lambda);
        }

        public override string ToString() { return pbrt.StringPrintf("[ SpectrumRampTexture tex: %s stops: %d ]", tex, positions.Length); }

        public static SpectrumRampTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                 SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            Float[] positions = parameters.GetFloatArray("positions").ToArray();
            List<RGB> values = parameters.GetRGBArray("values");
            pbrt.CheckRampStops(positions, values.Count, "ramp", loc);
            string interpolation = parameters.GetOneString("interpolation", "linear");
            if (interpolation != "linear" && interpolation != "step")
                pbrt.ErrorExit(loc, pbrt.StringPrintf("ramp: interpolation \"%s\" unknown; use \"linear\" or \"step\".", interpolation));
            return new SpectrumRampTexture(parameters.GetFloatTexture("tex", 0f, alloc), positions, values.ToArray(),
                                           interpolation == "step", spectrumType, RGBColorSpace.sRGB);
        }

        private FloatTexture tex;
        private Float[] positions;
        private Float[][] channels;
        private bool step;
        private SpectrumType spectrumType;
        private RGBColorSpace colorSpace;
    }

    // SpectrumBlackbodyTexture Definition
    // The emission spectrum of a blackbody whose temperature, in Kelvin, is (tex - offset) * scale. Like the temperature
    // grid of pbrt's "uniformgrid" medium, no emission is produced at or below 100 K. In "normalized" mode the spectrum
    // is scaled to a peak of 1 at every temperature, so only the color follows the temperature, as in pbrt-v4. In
    // "physical" mode it is Planck's law divided by the peak of the spectrum at "referencetemperature", so that the
    // brightness also grows with the temperature, roughly as T^4.
    public sealed unsafe class SpectrumBlackbodyTexture : SpectrumTexture
    {
        public SpectrumBlackbodyTexture(FloatTexture tex, Float temperatureScale, Float temperatureOffset, bool physical,
                                        Float referenceTemperature)
        {
            this.tex = tex;
            this.temperatureScale = temperatureScale;
            this.temperatureOffset = temperatureOffset;
            this.physical = physical;
            referenceNormalization = 1 / PeakRadiance(referenceTemperature);
        }

        // The largest value of Planck's law over wavelength at temperature T (Wien's displacement law)
        public static Float PeakRadiance(Float T)
        {
            return pbrt.Blackbody(2.8977721e-3f / T * 1e9f, T);
        }

        private Float Temperature(Float value) { return (value - temperatureOffset) * temperatureScale; }

        // The largest value any wavelength can take for a temperature of at most T
        private Float Bound(Float T)
        {
            if (T <= minimumTemperature)
                return 0;
            return physical ? PeakRadiance(T) * referenceNormalization : 1f;
        }

        public override Float MaxValue()
        {
            Float a = Temperature(tex.MinValue()), b = Temperature(tex.MaxValue());
            return Bound(std.max(a, b));
        }

        public override Float RegionMax(Bounds3f region)
        {
            tex.RegionBounds(region, out Float lo, out Float hi);
            return Bound(std.max(Temperature(lo), Temperature(hi)));
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float T = Temperature(tex.Evaluate(ctx));
            if (T <= minimumTemperature)
                return new SampledSpectrum(0f);
            if (!physical)
                return new BlackbodySpectrum(T).Sample(lambda);
            SampledSpectrum s = new SampledSpectrum();
            for (int i = 0; i < NSpectrumSamples; ++i)
                s[i] = pbrt.Blackbody(lambda[i], T) * referenceNormalization;
            return s;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SpectrumBlackbodyTexture tex: %s scale: %f offset: %f physical: %s ]", tex, temperatureScale,
                                     temperatureOffset, physical ? "true" : "false");
        }

        public static SpectrumBlackbodyTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                      SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            // Every named spectrum texture is created for each spectrum type; the blackbody spectrum is the same for all of them
            string mode = parameters.GetOneString("mode", "normalized");
            if (mode != "normalized" && mode != "physical")
                pbrt.ErrorExit(loc, pbrt.StringPrintf("blackbody: mode \"%s\" unknown; use \"normalized\" or \"physical\".", mode));
            Float reference = parameters.GetOneFloat("referencetemperature", 1000f);
            if (!(reference > minimumTemperature))
                pbrt.ErrorExit(loc, "blackbody: \"referencetemperature\" must exceed 100 K.");
            return new SpectrumBlackbodyTexture(parameters.GetFloatTexture("temperature", 1500f, alloc),
                                                parameters.GetOneFloat("temperaturescale", 1f),
                                                parameters.GetOneFloat("temperatureoffset", 0f), mode == "physical", reference);
        }

        private const Float minimumTemperature = 100f;
        private FloatTexture tex;
        private Float temperatureScale, temperatureOffset, referenceNormalization;
        private bool physical;
    }
}
