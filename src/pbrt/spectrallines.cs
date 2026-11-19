// (not in pbrt-v4) spectrallines.cs
//
// The wavelengths of the film while there are emitters of spectral lines in the scene (a laser: laserlight.cs). A line is
// far narrower than the spacing of the wavelengths of a path: the wavelengths that the film samples would never find it.
// An emitter of lines tells its lines here when it is created, and the film then takes a fraction of its wavelengths from
// the lines themselves (the line by its share of the tristimulus sum, the wavelength by its Gaussian) and the others as
// before, each with the density of the mixture. That is importance sampling of the wavelength and nothing more: unbiased
// for everything in the scene, and a scene without lines is rendered as before.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // The wavelengths of the film while there are line spectra in the scene: a fraction of them from the lines, the others
    // from what the film samples otherwise, each with the density of the mixture
    public static class SpectralLineSampling
    {
        public static bool Active { get { return state != null; } }

        // The lines of one emitter of the scene (centres and standard deviations in nanometres, radiant shares of sum one); the
        // fraction of the wavelengths that are taken from the lines is that of the last emitter that was added
        public static void Add(double[] centre, double[] width, double[] share, double fraction)
        {
            lock (emitters)
            {
                Emitter emitter = new Emitter();
                emitter.centre = centre;
                emitter.width = width;
                emitter.share = share;
                emitters.Add(emitter);
                Rebuild(fraction);
            }
        }

        // Forgets the lines, for tests that build several scenes in one process
        public static void Clear()
        {
            lock (emitters)
            {
                emitters.Clear();
                state = null;
            }
        }

        private static void Rebuild(double fraction)
        {
            List<double> centres = new List<double>();
            List<double> widths = new List<double>();
            List<double> weights = new List<double>();
            double total = 0;
            foreach (Emitter emitter in emitters)
            {
                // (a line is found in proportion to what it adds to the three tristimulus values together; each emitter has
                // the same part of the wavelengths, whatever its lines are to the eye: how bright each one is in the image is
                // not known here)
                double[] response = new double[emitter.centre.Length];
                double sum = 0;
                for (int i = 0; i < emitter.centre.Length; ++i)
                {
                    Float centre = (Float)emitter.centre[i];
                    response[i] = emitter.share[i] * (Spectra.X().Evaluate(centre) + Spectra.Y().Evaluate(centre) + Spectra.Z().Evaluate(centre));
                    sum += response[i];
                }
                for (int i = 0; i < emitter.centre.Length; ++i)
                {
                    if (!(response[i] > 0))
                    {
                        continue;
                    }
                    centres.Add(emitter.centre[i]);
                    widths.Add(emitter.width[i]);
                    weights.Add(response[i] / sum);
                    total += response[i] / sum;
                }
            }
            if (!(total > 0) || !(fraction > 0))
            {
                state = null;
                return;
            }
            State built = new State();
            built.fraction = Math.Min(fraction, 0.95);
            built.centre = centres.ToArray();
            built.width = widths.ToArray();
            built.weight = new double[weights.Count];
            built.cumulative = new double[weights.Count + 1];
            for (int i = 0; i < weights.Count; ++i)
            {
                built.weight[i] = weights[i] / total;
                built.cumulative[i + 1] = built.cumulative[i] + built.weight[i];
            }
            state = built;
        }

        // A line of a Gaussian profile, per nanometre: its integral over the wavelengths is one
        public static double Gaussian(double lambda, double centre, double width)
        {
            double z = (lambda - centre) / width;
            // (beyond 39 widths the exponential is zero in double precision: nothing is cut off)
            if (Math.Abs(z) > 39)
            {
                return 0;
            }
            return Math.Exp(-0.5 * z * z) / (width * 2.5066282746310002);
        }

        // The density of the lines alone at a wavelength
        private static double LineDensity(State current, double lambda)
        {
            double sum = 0;
            for (int i = 0; i < current.centre.Length; ++i)
            {
                sum += current.weight[i] * Gaussian(lambda, current.centre[i], current.width[i]);
            }
            return sum;
        }

        // The wavelength of the lines for a uniform number: the line by its weight, then its Gaussian by inversion
        private static double SampleLine(State current, double u)
        {
            int line = 0;
            while (line + 1 < current.centre.Length && u >= current.cumulative[line + 1])
            {
                ++line;
            }
            double inside = (u - current.cumulative[line]) / current.weight[line];
            inside = Math.Min(Math.Max(inside, 1e-9), 1 - 1e-9);
            return current.centre[line] + current.width[line] * 1.4142135623730951 * InverseErrorFunction(2 * inside - 1);
        }

        // The inverse of the error function: Newton's iteration from the single precision form (ErfInv)
        public static double InverseErrorFunction(double x)
        {
            // (the single precision form is not finite at one, which a number next to it rounds to)
            double y = pbrt.ErfInv((Float)Math.Min(Math.Max(x, -0.99999), 0.99999));
            for (int i = 0; i < 60; ++i)
            {
                double step = (ErrorFunction(y) - x) / (1.1283791670955126 * Math.Exp(-y * y));
                y -= step;
                if (Math.Abs(step) < 1e-13)
                {
                    break;
                }
            }
            return y;
        }

        // The error function to double precision: its series for small arguments, the continued fraction of its complement
        // for large ones
        public static double ErrorFunction(double x)
        {
            double a = Math.Abs(x);
            double value;
            if (a < 2.5)
            {
                double term = a, sum = a;
                for (int n = 1; n < 200; ++n)
                {
                    term *= -a * a / n;
                    double add = term / (2 * n + 1);
                    sum += add;
                    if (Math.Abs(add) < 1e-17 * Math.Abs(sum))
                    {
                        break;
                    }
                }
                value = 1.1283791670955126 * sum;
            }
            else
            {
                double fraction = 0;
                for (int n = 60; n >= 1; --n)
                {
                    fraction = 0.5 * n / (a + fraction);
                }
                value = 1 - Math.Exp(-a * a) / ((a + fraction) * 1.7724538509055159);
            }
            return x < 0 ? -value : value;
        }

        // The wavelengths of a film that samples the visible wavelengths (SampledWavelengths.SampleVisible)
        public static SampledWavelengths SampleVisible(Float u)
        {
            State current = state;
            if (current == null)
            {
                return SampledWavelengths.SampleVisible(u);
            }
            Span<Float> lambda = stackalloc Float[NSpectrumSamples];
            Span<Float> pdf = stackalloc Float[NSpectrumSamples];
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double up = u + (double)i / NSpectrumSamples;
                if (up >= 1)
                {
                    up -= 1;
                }
                if (up < current.fraction)
                {
                    lambda[i] = (Float)SampleLine(current, up / current.fraction);
                }
                else
                {
                    lambda[i] = pbrt.SampleVisibleWavelengths((Float)Math.Min((up - current.fraction) / (1 - current.fraction), 0.99999994));
                }
                pdf[i] = (Float)(current.fraction * LineDensity(current, lambda[i]) + (1 - current.fraction) * pbrt.VisibleWavelengthsPDF(lambda[i]));
            }
            return SampledWavelengths.FromSamples(lambda, pdf);
        }

        // The wavelengths of a film that samples a range uniformly (SampledWavelengths.SampleUniform)
        public static SampledWavelengths SampleUniform(Float u, Float lambdaMin, Float lambdaMax)
        {
            State current = state;
            if (current == null)
            {
                return SampledWavelengths.SampleUniform(u, lambdaMin, lambdaMax);
            }
            Span<Float> lambda = stackalloc Float[NSpectrumSamples];
            Span<Float> pdf = stackalloc Float[NSpectrumSamples];
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double up = u + (double)i / NSpectrumSamples;
                if (up >= 1)
                {
                    up -= 1;
                }
                if (up < current.fraction)
                {
                    lambda[i] = (Float)SampleLine(current, up / current.fraction);
                }
                else
                {
                    lambda[i] = pbrt.Lerp((Float)((up - current.fraction) / (1 - current.fraction)), lambdaMin, lambdaMax);
                }
                double uniform = lambda[i] >= lambdaMin && lambda[i] <= lambdaMax ? 1.0 / (lambdaMax - lambdaMin) : 0.0;
                pdf[i] = (Float)(current.fraction * LineDensity(current, lambda[i]) + (1 - current.fraction) * uniform);
            }
            return SampledWavelengths.FromSamples(lambda, pdf);
        }

        private sealed class State
        {
            public double fraction;
            public double[] centre, width, weight, cumulative;
        }

        private sealed class Emitter
        {
            public double[] centre, width, share;
        }

        private static readonly List<Emitter> emitters = new List<Emitter>();
        private static volatile State state;
    }
}
