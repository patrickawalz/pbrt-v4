// (not in pbrt-v4) Anomalous dispersion: the index of refraction of a coloured transparent material, from its absorption.
// After Weidlich and Wilkie, "Anomalous Dispersion in Predictive Rendering" (EGSR 2009).
//
// The index and the absorption of a material are not independent: each absorption band bends the index around it (it falls
// toward the band from the long wavelengths, rises steeply through it and falls again), and the Kramers-Kronig relation gives
// the one from the other. For a coloured material the band is in the visible range, and the order of the colours behind a
// prism is partly reversed. As in the paper (its equation 12, in the wavelength):
//
//   n(l) = n_b(l) + (2 l^2 / pi) P integral k(l') / (l' (l^2 - l'^2)) dl'
//
// with the extinction coefficient k = l alpha / (4 pi) of the absorption coefficient alpha (its equation 3), the background
// index n_b (what the bands outside the range of the integral leave: a constant, or a Cauchy or Sellmeier formula) and the
// principal value of the integral taken by Maclaurin's formula (its equations 15 and 16, and its appendix A): at the i-th of
// m evenly spaced wavelengths the sum runs over every other point, the ones of the other parity, so that it never meets the
// pole:
//
//   n(l_i) = n_b(l_i) + (2 l_i^2 / pi) 2 h sum_j k_j / (l_j (l_i^2 - l_j^2)),   j of the parity that i is not
//
// Between those wavelengths the correction is interpolated linearly.
//
// With the index of a material (ParametricSpectra.Index), next to "eta", "cauchy" or "sellmeier", which are then the
// background index:
//
//   "spectrum extinction"    the extinction coefficient k (the imaginary part of the index)
//   "spectrum absorption"    or the absorption coefficient, per scene unit: the same spectrum as the sigma_a of the medium
//                            inside the object, so that the colour and the index are the same material
//   "float metersperunit"    the size of a scene unit, for "absorption" (1)
//   "float absorptionscale"  multiplies either one: the concentration (1)
//   "float dispersionrange"  [first last]: the wavelengths of the integral, nm (300 900; the paper: 300 720)
//   "integer dispersionsamples"  their number (121: every 5 nm, the step of the paper)
//
// The paper splits a ray into 45 rays of one wavelength each at the first dispersive surface. Here nothing is split: a path
// carries its wavelengths until it meets a surface whose index differs between them and goes on with the first one
// (ParametricSpectra.SampleIndex), as for every other dispersive material.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // AnomalousDispersionSpectrum Definition
    public sealed class AnomalousDispersionSpectrum : Spectrum
    {
        // `extinction`: k at `count` wavelengths from `first` to `last` (nm), evenly spaced
        public AnomalousDispersionSpectrum(Spectrum background, double[] extinction, Float first, Float last)
        {
            this.background = background;
            this.first = first;
            this.last = last;
            this.extinction = extinction;
            correction = Maclaurin(extinction, first, last);
            maxValue = 0;
            for (Float l = pbrt.Lambda_min; l <= pbrt.Lambda_max; ++l)
            {
                maxValue = std.max(maxValue, Evaluate(l));
            }
        }

        // What the absorption adds to the index at each of the wavelengths: the Kramers-Kronig integral by Maclaurin's formula
        public static double[] Maclaurin(double[] extinction, double first, double last)
        {
            int count = extinction.Length;
            double h = (last - first) / (count - 1);
            double[] result = new double[count];
            for (int i = 0; i < count; ++i)
            {
                double lambda = first + i * h;
                double sum = 0;
                // (the points of the other parity: none of them is the pole)
                for (int j = (i + 1) % 2; j < count; j += 2)
                {
                    double other = first + j * h;
                    sum += extinction[j] / (other * (lambda * lambda - other * other));
                }
                result[i] = 2 * lambda * lambda / Math.PI * 2 * h * sum;
            }
            return result;
        }

        // The value between the wavelengths of the integral, linearly; outside them, the value at the end
        private double Interpolate(double[] values, Float lambda)
        {
            double position = (lambda - first) / (last - first) * (values.Length - 1);
            if (position <= 0)
            {
                return values[0];
            }
            if (position >= values.Length - 1)
            {
                return values[values.Length - 1];
            }
            int index = (int)position;
            double t = position - index;
            return (1 - t) * values[index] + t * values[index + 1];
        }

        // What the absorption adds to the background index
        public Float Correction(Float lambda)
        {
            return (Float)Interpolate(correction, lambda);
        }

        // The extinction coefficient: the imaginary part of the index
        public Float Extinction(Float lambda)
        {
            return (Float)Interpolate(extinction, lambda);
        }

        public override Float Evaluate(Float lambda)
        {
            return background.Evaluate(lambda) + Correction(lambda);
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
            return pbrt.StringPrintf("[ AnomalousDispersionSpectrum background: %s first: %f last: %f samples: %d ]", background, first, last,
                                     extinction.Length);
        }

        // The index of a material with "extinction" or "absorption" among its parameters: `background` with the correction of
        // the absorption; `background` itself if there is neither
        public static Spectrum Create(TextureParameterDictionary parameters, string prefix, Spectrum background, Allocator alloc)
        {
            Spectrum extinction = parameters.GetOneSpectrum(prefix + "extinction", null, SpectrumType.Unbounded, alloc);
            Spectrum absorption = parameters.GetOneSpectrum(prefix + "absorption", null, SpectrumType.Unbounded, alloc);
            if (extinction == null && absorption == null)
            {
                return background;
            }
            if (extinction != null && absorption != null)
            {
                pbrt.ErrorExit(pbrt.StringPrintf("\"%sextinction\" and \"%sabsorption\" are the same quantity: give one of them.", prefix, prefix));
            }
            if (background == null)
            {
                background = new ConstantSpectrum(1.5f);
            }
            List<Float> range = parameters.GetFloatArray(prefix + "dispersionrange");
            if (range.Count != 0 && range.Count != 2)
            {
                pbrt.ErrorExit(pbrt.StringPrintf("\"%sdispersionrange\" needs 2 values [first last]", prefix));
            }
            Float first = range.Count == 2 ? range[0] : 300f;
            Float last = range.Count == 2 ? range[1] : 900f;
            int count = parameters.GetOneInt(prefix + "dispersionsamples", 121);
            Float scale = parameters.GetOneFloat(prefix + "absorptionscale", 1f);
            Float metersPerUnit = parameters.GetOneFloat(prefix + "metersperunit", 1f);
            if (!(last > first) || !(first > 0) || count < 4 || !(metersPerUnit > 0) || !(scale >= 0))
            {
                pbrt.ErrorExit("anomalous dispersion: \"dispersionrange\" must be two rising positive wavelengths, \"dispersionsamples\" 4 or more, " +
                               "\"metersperunit\" positive and \"absorptionscale\" not negative.");
            }
            double[] k = new double[count];
            for (int i = 0; i < count; ++i)
            {
                Float lambda = first + (last - first) * i / (count - 1);
                if (extinction != null)
                {
                    k[i] = scale * extinction.Evaluate(lambda);
                }
                else
                {
                    // (k = lambda alpha / (4 pi), the wavelength in metres and the absorption per metre)
                    k[i] = scale * absorption.Evaluate(lambda) / metersPerUnit * lambda * 1e-9 / (4 * Math.PI);
                }
            }
            return new AnomalousDispersionSpectrum(background, k, first, last);
        }

        // AnomalousDispersionSpectrum Private Members
        private Spectrum background;
        private Float first, last, maxValue;
        private double[] extinction, correction;
    }
}
