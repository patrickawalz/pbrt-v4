// (not in pbrt-v4) spectra_parametric.cs
//
// Spectra given by a formula, as parameters of their own type next to "rgb", "blackbody" and "spectrum":
//
//   "cauchy name"      [A B (C)]               n = A + B / l^2 + C / l^4, l in micrometres            (C++ fork: CauchySpectrum)
//   "sellmeier name"   [B1 B2 B3 C1 C2 C3]     n^2 = 1 + sum B_i l^2 / (l^2 - C_i), l in um, C in um^2 (C++ fork: SellmeierSpectrum)
//   "gaussian name"    [mean width (scale)]    scale exp(-(l - mean)^2 / (2 width^2)), nm              (LuxRender, Octane)
//   "bandstop name"    [mean width (depth)]    1 - depth exp(-(l - mean)^2 / (2 width^2))              (a filter: the Gaussian taken out)
//   "frequency name"   [frequency phase (scale)]  scale (sin(frequency l + phase) + 1) / 2, l in nm    (LuxRender)
//   "equalenergy name" [value]                 the same at every wavelength                             (LuxRender)
//   "regulardata name" [first last v0 v1 ...]  values at even steps from `first` to `last` nm, linear   (LuxRender)
//   "longpass name"    [edge softness (low high)]   low below the edge and high above it, over `softness` nm (a filter)
//   "shortpass name"   [edge softness (low high)]   high below the edge and low above it
//
// LuxRender's "irregulardata" is pbrt's "spectrum" with (wavelength, value) pairs.
// The same formulas as textures, with float textures for their parameters, are in textures_parametric.cs.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // CauchySpectrum Definition
    public sealed class CauchySpectrum : Spectrum
    {
        public CauchySpectrum(Float A, Float B, Float C)
        {
            this.A = A;
            this.B = B;
            this.C = C;
            maxValue = 0;
            for (Float l = pbrt.Lambda_min; l <= pbrt.Lambda_max; ++l)
            {
                maxValue = std.max(maxValue, Evaluate(l));
            }
        }

        public static Float Value(Float A, Float B, Float C, Float lambda)
        {
            Float l2 = pbrt.Sqr(lambda * 1e-3f);
            return A + B / l2 + C / pbrt.Sqr(l2);
        }

        public override Float Evaluate(Float lambda)
        {
            return Value(A, B, C, lambda);
        }

        public override SampledSpectrum Sample(SampledWavelengths lambda)
        {
            SampledSpectrum s = new SampledSpectrum(0f);
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                s[i] = Evaluate(lambda[i]);
            }
            return s;
        }

        public override Float MaxValue() { return maxValue; }

        public override string ToString() { return pbrt.StringPrintf("[ CauchySpectrum A: %f B: %f C: %f ]", A, B, C); }

        private Float A, B, C, maxValue;
    }

    // SellmeierSpectrum Definition
    public sealed class SellmeierSpectrum : Spectrum
    {
        public SellmeierSpectrum(Float B1, Float B2, Float B3, Float C1, Float C2, Float C3)
        {
            this.B1 = B1;
            this.B2 = B2;
            this.B3 = B3;
            this.C1 = C1;
            this.C2 = C2;
            this.C3 = C3;
            maxValue = 0;
            for (Float l = pbrt.Lambda_min; l <= pbrt.Lambda_max; ++l)
            {
                maxValue = std.max(maxValue, Evaluate(l));
            }
        }

        public static Float Value(Float B1, Float B2, Float B3, Float C1, Float C2, Float C3, Float lambda)
        {
            double l2 = pbrt.Sqr((double)lambda * 1e-3);
            double n2 = 1 + (double)B1 * l2 / (l2 - (double)C1) + (double)B2 * l2 / (l2 - (double)C2) + (double)B3 * l2 / (l2 - (double)C3);
            return n2 > 0 ? (Float)Math.Sqrt(n2) : 0;
        }

        public override Float Evaluate(Float lambda)
        {
            return Value(B1, B2, B3, C1, C2, C3, lambda);
        }

        public override SampledSpectrum Sample(SampledWavelengths lambda)
        {
            SampledSpectrum s = new SampledSpectrum(0f);
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                s[i] = Evaluate(lambda[i]);
            }
            return s;
        }

        public override Float MaxValue() { return maxValue; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SellmeierSpectrum B: %f %f %f C: %f %f %f ]", B1, B2, B3, C1, C2, C3);
        }

        private Float B1, B2, B3, C1, C2, C3, maxValue;
    }

    // GaussianSpectrum Definition
    // baseline + scale exp(-(lambda - mean)^2 / (2 width^2)): a line or a band of emission or of colour (baseline 0), or a band
    // taken out of a filter (baseline 1, scale negative)
    public sealed class GaussianSpectrum : Spectrum
    {
        public GaussianSpectrum(Float mean, Float width, Float scale, Float baseline)
        {
            this.mean = mean;
            this.width = std.max<Float>(width, 1e-6f);
            this.scale = scale;
            this.baseline = baseline;
        }

        public static Float Value(Float mean, Float width, Float scale, Float baseline, Float lambda)
        {
            Float d = (lambda - mean) / width;
            return baseline + scale * pbrt.FastExp(-0.5f * d * d);
        }

        public override Float Evaluate(Float lambda)
        {
            return Value(mean, width, scale, baseline, lambda);
        }

        public override SampledSpectrum Sample(SampledWavelengths lambda)
        {
            SampledSpectrum s = new SampledSpectrum(0f);
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                s[i] = Evaluate(lambda[i]);
            }
            return s;
        }

        // The Gaussian is at most 1: the largest value is at its peak if it adds, far from it if it takes away
        public override Float MaxValue() { return baseline + std.max<Float>(scale, 0); }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ GaussianSpectrum mean: %f width: %f scale: %f baseline: %f ]", mean, width, scale, baseline);
        }

        private Float mean, width, scale, baseline;
    }

    // FrequencySpectrum Definition
    // scale (sin(frequency lambda + phase) + 1) / 2, lambda in nm: the interference colours of LuxRender's "frequency" spectrum
    public sealed class FrequencySpectrum : Spectrum
    {
        public FrequencySpectrum(Float frequency, Float phase, Float scale)
        {
            this.frequency = frequency;
            this.phase = phase;
            this.scale = scale;
        }

        public static Float Value(Float frequency, Float phase, Float scale, Float lambda)
        {
            return scale * 0.5f * (std.sin(frequency * lambda + phase) + 1);
        }

        public override Float Evaluate(Float lambda)
        {
            return Value(frequency, phase, scale, lambda);
        }

        public override SampledSpectrum Sample(SampledWavelengths lambda)
        {
            SampledSpectrum s = new SampledSpectrum(0f);
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                s[i] = Evaluate(lambda[i]);
            }
            return s;
        }

        public override Float MaxValue() { return std.max<Float>(scale, 0); }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FrequencySpectrum frequency: %f phase: %f scale: %f ]", frequency, phase, scale);
        }

        private Float frequency, phase, scale;
    }

    // EdgeSpectrum Definition
    // A filter with one edge: `low` on the short side of `edge` and `high` on the long side, the step a logistic curve that goes
    // from 12% to 88% of the way over `softness` nm
    public sealed class EdgeSpectrum : Spectrum
    {
        public EdgeSpectrum(Float edge, Float softness, Float low, Float high)
        {
            this.edge = edge;
            this.softness = std.max<Float>(softness, 1e-6f);
            this.low = low;
            this.high = high;
        }

        public static Float Value(Float edge, Float softness, Float low, Float high, Float lambda)
        {
            Float t = 1 / (1 + pbrt.FastExp(-4 * (lambda - edge) / softness));
            return low + (high - low) * t;
        }

        public override Float Evaluate(Float lambda)
        {
            return Value(edge, softness, low, high, lambda);
        }

        public override SampledSpectrum Sample(SampledWavelengths lambda)
        {
            SampledSpectrum s = new SampledSpectrum(0f);
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                s[i] = Evaluate(lambda[i]);
            }
            return s;
        }

        public override Float MaxValue() { return std.max(low, high); }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ EdgeSpectrum edge: %f softness: %f low: %f high: %f ]", edge, softness, low, high);
        }

        private Float edge, softness, low, high;
    }

    // The parameter types of the spectra above
    public static class ParametricSpectra
    {
        public static readonly string[] Types = new string[] { "cauchy", "sellmeier", "gaussian", "bandstop", "frequency", "equalenergy", "regulardata",
                                                               "longpass", "shortpass" };

        public static bool IsType(string type)
        {
            foreach (string t in Types)
            {
                if (t == type)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Expect(string type, string name, pstd.vector<Float> v, int least, int most, string values, FileLoc loc)
        {
            if ((int)v.size() < least || (int)v.size() > most)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"%s %s\" needs %s, not %d values.", type, name, values, (int)v.size()));
            }
        }

        // The spectrum of a parameter of one of the types (the whole array of its values is one spectrum)
        public static Spectrum Create(string type, string name, pstd.vector<Float> v, FileLoc loc, Allocator alloc)
        {
            if (type == "cauchy")
            {
                Expect(type, name, v, 2, 3, "[A B (C)]", loc);
                return new CauchySpectrum(v[0], v[1], v.size() > 2 ? v[2] : 0);
            }
            if (type == "sellmeier")
            {
                Expect(type, name, v, 6, 6, "[B1 B2 B3 C1 C2 C3]", loc);
                return new SellmeierSpectrum(v[0], v[1], v[2], v[3], v[4], v[5]);
            }
            if (type == "gaussian")
            {
                Expect(type, name, v, 2, 3, "[mean width (scale)]", loc);
                return new GaussianSpectrum(v[0], v[1], v.size() > 2 ? v[2] : 1, 0);
            }
            if (type == "bandstop")
            {
                Expect(type, name, v, 2, 3, "[mean width (depth)]", loc);
                return new GaussianSpectrum(v[0], v[1], -(v.size() > 2 ? v[2] : 1), 1);
            }
            if (type == "frequency")
            {
                Expect(type, name, v, 2, 3, "[frequency phase (scale)]", loc);
                return new FrequencySpectrum(v[0], v[1], v.size() > 2 ? v[2] : 1);
            }
            if (type == "equalenergy")
            {
                Expect(type, name, v, 1, 1, "[value]", loc);
                return new ConstantSpectrum(v[0]);
            }
            if (type == "regulardata")
            {
                Expect(type, name, v, 4, int.MaxValue, "[first last v0 v1 ...] (two values or more)", loc);
                int n = (int)v.size() - 2;
                if (!(v[1] > v[0]))
                {
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("\"regulardata %s\": the last wavelength %f is not above the first %f.", name, v[1], v[0]));
                }
                Float[] lambda = new Float[n], value = new Float[n];
                for (int i = 0; i < n; ++i)
                {
                    lambda[i] = pbrt.Lerp((Float)i / (n - 1), v[0], v[1]);
                    value[i] = v[2 + i];
                }
                return new PiecewiseLinearSpectrum(lambda, value, alloc);
            }
            bool longpass = type == "longpass";
            Expect(type, name, v, 2, 4, "[edge softness (low high)]", loc);
            Float low = v.size() > 2 ? v[2] : 0, high = v.size() > 3 ? v[3] : 1;
            return longpass ? new EdgeSpectrum(v[0], v[1], low, high) : new EdgeSpectrum(v[0], v[1], high, low);
        }

        // Index of refraction parameter of a material, as the C++ fork reads it. In order of precedence for `<prefix>eta`:
        //   "<prefix>sellmeier" [B1 B2 B3 C1 C2 C3]  Sellmeier dispersion, wavelength in um, C in um^2
        //   "<prefix>cauchy"    [A B C]               Cauchy dispersion n = A + B/l^2 + C/l^4 (um)
        //   "<prefix>cauchyb"   B                     LuxRender style: A is the plain eta value
        //   "float <name>"                            constant index
        //   "spectrum <name>" (or any of the types above)   tabulated / named / parametric spectrum
        // Returns null if none is given.
        public static Spectrum Index(TextureParameterDictionary parameters, string name, Allocator alloc)
        {
            string prefix = name.Substring(0, name.Length - 3);  // strip "eta"
            List<Float> sm = parameters.GetFloatArray(prefix + "sellmeier");
            if (sm.Count != 0)
            {
                if (sm.Count != 6)
                {
                    pbrt.ErrorExit(pbrt.StringPrintf("\"%ssellmeier\" needs 6 values [B1 B2 B3 C1 C2 C3]", prefix));
                }
                return new SellmeierSpectrum(sm[0], sm[1], sm[2], sm[3], sm[4], sm[5]);
            }
            List<Float> ca = parameters.GetFloatArray(prefix + "cauchy");
            if (ca.Count != 0)
            {
                if (ca.Count < 2 || ca.Count > 3)
                {
                    pbrt.ErrorExit(pbrt.StringPrintf("\"%scauchy\" needs 2 or 3 values [A B (C)]", prefix));
                }
                return new CauchySpectrum(ca[0], ca[1], ca.Count > 2 ? ca[2] : 0);
            }
            List<Float> cb = parameters.GetFloatArray(prefix + "cauchyb");
            if (cb.Count != 0)
            {
                List<Float> a = parameters.GetFloatArray(name);
                return new CauchySpectrum(a.Count == 0 ? 1.5f : a[0], cb[0], 0);
            }
            if (parameters.GetFloatArray(name).Count != 0)
            {
                return new ConstantSpectrum(parameters.GetFloatArray(name)[0]);
            }
            return parameters.GetOneSpectrum(name, null, SpectrumType.Unbounded, alloc);
        }

        // "texture <name>": an index that varies over the surface or in space (a spectrum texture), where Index() found no
        // spectrum. Null if there is none.
        public static SpectrumTexture IndexTexture(TextureParameterDictionary parameters, string name, Spectrum index, Allocator alloc)
        {
            return index == null ? parameters.GetSpectrumTextureOrNull(name, SpectrumType.Unbounded, alloc) : null;
        }

        // The index at the first wavelength of the ray, from the texture if there is one and from the spectrum otherwise. The
        // other wavelengths are terminated if the index is not the same for them: the light disperses.
        public static Float SampleIndex<TextureEvaluator>(Spectrum eta, SpectrumTexture etaTexture, TextureEvaluator texEval, MaterialEvalContext ctx,
                                                          ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            Float sampledEta;
            if (etaTexture != null)
            {
                SampledSpectrum etas = texEval.Apply(etaTexture, ctx, lambda);
                sampledEta = etas[0];
                for (int i = 1; i < pbrt.NSpectrumSamples; ++i)
                {
                    if (etas[i] != etas[0])
                    {
                        lambda.TerminateSecondary();
                        break;
                    }
                }
            }
            else
            {
                sampledEta = eta.Evaluate(lambda[0]);
                if (!(eta is ConstantSpectrum))
                {
                    lambda.TerminateSecondary();
                }
            }
            // Handle edge case in case lambda[0] is beyond the wavelengths stored by the Spectrum.
            if (sampledEta == 0)
            {
                sampledEta = 1;
            }
            return sampledEta;
        }
    }
}
