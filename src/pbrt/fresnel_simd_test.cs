// (not in pbrt-v4) Tests of fresnel_simd.cs: the Fresnel equations at the four wavelengths at once against the functions
// for one wavelength.

using System;
using testing;

namespace pbrt
{
    public static unsafe class FresnelSimdTests
    {
        // FrComplex() and FrComplexPolarised() at once: each value is that of the function for one wavelength, to the last bit
        [TEST("FresnelSimd", "ConductorSameAsOneWavelengthAtATime")]
        public static void ConductorSameAsOneWavelengthAtATime()
        {
            RNG rng = new RNG(17);
            int compared = 0;
            for (int trial = 0; trial < 200000; ++trial)
            {
                SampledSpectrum eta = new SampledSpectrum(0), k = new SampledSpectrum(0);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    eta[i] = 0.05f + 4 * rng.Uniform<Float>();
                    k[i] = trial % 7 == 0 ? 0 : 8 * rng.Uniform<Float>() * (trial % 5 == 0 ? -1 : 1);
                }
                if (trial % 11 == 0)
                {
                    // (dielectrics, also from the denser side: total internal reflection)
                    k = new SampledSpectrum(0);
                    eta = new SampledSpectrum(trial % 22 == 0 ? 1.5f : 1 / 1.5f);
                }
                Float cosine = trial % 13 == 0 ? (trial % 26 == 0 ? 0 : 1) : rng.Uniform<Float>();
                SampledSpectrum F = FrComplex(cosine, eta, k);
                FrComplexPolarised(cosine, eta, k, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum Ds, out SampledSpectrum Dp);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    EXPECT_EQ(FrComplex(cosine, new pstd.complex(eta[i], k[i])), F[i]);
                    FrComplexPolarised(cosine, new pstd.complex(eta[i], k[i]), out Float fs, out Float fp, out Float ds, out Float dp);
                    EXPECT_EQ(fs, Fs[i]);
                    EXPECT_EQ(fp, Fp[i]);
                    EXPECT_EQ(ds, Ds[i]);
                    EXPECT_EQ(dp, Dp[i]);
                    compared += 5;
                }
            }
            Console.WriteLine("conductor Fresnel at once: " + compared + " values the same");
        }

        // FrThinFilmPolarised() at once against the function for one wavelength: the reflectances and the phases (as points
        // on the circle, since a phase next to pi may come out next to -pi) agree to rounding
        [TEST("FresnelSimd", "ThinFilmAgreesWithOneWavelengthAtATime")]
        public static void ThinFilmAgreesWithOneWavelengthAtATime()
        {
            RNG rng = new RNG(23);
            double worstReflectance = 0, worstPhase = 0;
            int compared = 0;
            for (int trial = 0; trial < 60000; ++trial)
            {
                int count = 1 + trial % 4;
                Float[][] layerEta = new Float[NSpectrumSamples][];
                Float[] thickness = new Float[count];
                for (int w = 0; w < NSpectrumSamples; ++w)
                {
                    layerEta[w] = new Float[count];
                }
                for (int layer = 0; layer < count; ++layer)
                {
                    Float n = 1.2f + 1.4f * rng.Uniform<Float>();
                    thickness[layer] = 20 + 480 * rng.Uniform<Float>();
                    for (int w = 0; w < NSpectrumSamples; ++w)
                    {
                        layerEta[w][layer] = trial % 2 == 0 ? n : n + 0.05f * w;
                    }
                }
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                SampledSpectrum wavelengths = new SampledSpectrum(0), eta = new SampledSpectrum(0), k = new SampledSpectrum(0);
                bool conductor = trial % 3 == 0;
                for (int w = 0; w < NSpectrumSamples; ++w)
                {
                    wavelengths[w] = lambda[w];
                    eta[w] = conductor ? 0.2f + 2 * rng.Uniform<Float>() : 1.5f;
                    k[w] = conductor ? 1 + 5 * rng.Uniform<Float>() : 0;
                }
                // (from the denser side for some of the dielectrics: total internal reflection in the stack)
                bool reversed = !conductor && trial % 5 == 0;
                Float ambient = reversed ? 1.5f : 1;
                if (reversed)
                {
                    eta = new SampledSpectrum(1);
                }
                Float cosine = trial % 17 == 0 ? 1 : 0.02f + 0.98f * rng.Uniform<Float>();
                FrThinFilmPolarised(cosine, ambient, layerEta[0], layerEta[1], layerEta[2], layerEta[3], thickness, reversed, wavelengths, eta, k,
                                    out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum Ds, out SampledSpectrum Dp, out SampledSpectrum DsT,
                                    out SampledSpectrum DpT);
                for (int w = 0; w < NSpectrumSamples; ++w)
                {
                    FrThinFilmPolarised(cosine, ambient, layerEta[w], thickness, reversed, wavelengths[w], new pstd.complex(eta[w], k[w]), out Float fs,
                                        out Float fp, out Float ds, out Float dp, out Float dsT, out Float dpT);
                    worstReflectance = Math.Max(worstReflectance, Math.Max(Math.Abs(fs - Fs[w]), Math.Abs(fp - Fp[w])));
                    // (the phase of a reflection that is next to nothing has no meaning)
                    if (fs > 1e-6f)
                    {
                        worstPhase = Math.Max(worstPhase, OnCircle(ds, Ds[w]));
                    }
                    if (fp > 1e-6f)
                    {
                        worstPhase = Math.Max(worstPhase, OnCircle(dp, Dp[w]));
                    }
                    if (fs < 1 - 1e-6f)
                    {
                        worstPhase = Math.Max(worstPhase, OnCircle(dsT, DsT[w]));
                    }
                    if (fp < 1 - 1e-6f)
                    {
                        worstPhase = Math.Max(worstPhase, OnCircle(dpT, DpT[w]));
                    }
                    compared += 6;
                }
            }
            Console.WriteLine("thin film at once: " + compared + " values; reflectances differ by " + worstReflectance + " at most, phases by " + worstPhase);
            CHECK_LT(worstReflectance, 2e-6);
            CHECK_LT(worstPhase, 2e-4);
        }

        // How long the functions take for the four wavelengths, at once and one at a time (printed, nothing asserted)
        [TEST("FresnelSimd", "Speed")]
        public static void Speed()
        {
            SampledSpectrum eta = new SampledSpectrum(0), k = new SampledSpectrum(0), wavelengths = new SampledSpectrum(0);
            Float[] thickness = { 60, 90, 60 };
            Float[] layers = { 2.3f, 1.46f, 2.3f };
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                eta[i] = 0.2f + 0.3f * i;
                k[i] = 3 + 0.5f * i;
                wavelengths[i] = 420 + 90 * i;
            }
            int n = 2000000;
            double[] best = { double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue };
            Float sum = 0;
            for (int repeat = 0; repeat < 3; ++repeat)
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                for (int j = 0; j < n; ++j)
                {
                    Float cosine = (j % 1000 + 0.5f) / 1000;
                    for (int i = 0; i < NSpectrumSamples; ++i)
                    {
                        sum += FrComplex(cosine, new pstd.complex(eta[i], k[i]));
                    }
                }
                best[0] = Math.Min(best[0], watch.Elapsed.TotalSeconds * 1e9 / n);
                watch.Restart();
                for (int j = 0; j < n; ++j)
                {
                    sum += FrComplex((j % 1000 + 0.5f) / 1000, eta, k)[0];
                }
                best[1] = Math.Min(best[1], watch.Elapsed.TotalSeconds * 1e9 / n);
                watch.Restart();
                for (int j = 0; j < n; ++j)
                {
                    Float cosine = (j % 1000 + 0.5f) / 1000;
                    for (int i = 0; i < NSpectrumSamples; ++i)
                    {
                        FrComplexPolarised(cosine, new pstd.complex(eta[i], k[i]), out Float fs, out Float fp, out Float ds, out Float dp);
                        sum += fs + dp;
                    }
                }
                best[2] = Math.Min(best[2], watch.Elapsed.TotalSeconds * 1e9 / n);
                watch.Restart();
                for (int j = 0; j < n; ++j)
                {
                    FrComplexPolarised((j % 1000 + 0.5f) / 1000, eta, k, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum Ds, out SampledSpectrum Dp);
                    sum += Fs[0] + Dp[0];
                }
                best[3] = Math.Min(best[3], watch.Elapsed.TotalSeconds * 1e9 / n);
                watch.Restart();
                for (int j = 0; j < n / 4; ++j)
                {
                    Float cosine = (j % 1000 + 0.5f) / 1000;
                    for (int i = 0; i < NSpectrumSamples; ++i)
                    {
                        FrThinFilmPolarised(cosine, 1, layers, thickness, false, wavelengths[i], new pstd.complex(eta[i], k[i]), out Float fs, out Float fp,
                                            out Float ds, out Float dp, out Float dsT, out Float dpT);
                        sum += fs + dp;
                    }
                }
                best[4] = Math.Min(best[4], watch.Elapsed.TotalSeconds * 1e9 / (n / 4));
                watch.Restart();
                for (int j = 0; j < n / 4; ++j)
                {
                    FrThinFilmPolarised((j % 1000 + 0.5f) / 1000, 1, layers, layers, layers, layers, thickness, false, wavelengths, eta, k, out SampledSpectrum Fs,
                                        out SampledSpectrum Fp, out SampledSpectrum Ds, out SampledSpectrum Dp, out SampledSpectrum DsT, out SampledSpectrum DpT);
                    sum += Fs[0] + Dp[0];
                }
                best[5] = Math.Min(best[5], watch.Elapsed.TotalSeconds * 1e9 / (n / 4));
            }
            Console.WriteLine("four wavelengths, ns: FrComplex one at a time " + best[0].ToString("F1") + ", at once " + best[1].ToString("F1") +
                              "; FrComplexPolarised " + best[2].ToString("F1") + ", at once " + best[3].ToString("F1") + "; FrThinFilmPolarised (3 layers) " +
                              best[4].ToString("F1") + ", at once " + best[5].ToString("F1") + " (" + sum + ")");
        }

        private static double OnCircle(double a, double b)
        {
            return Math.Sqrt((Math.Cos(a) - Math.Cos(b)) * (Math.Cos(a) - Math.Cos(b)) + (Math.Sin(a) - Math.Sin(b)) * (Math.Sin(a) - Math.Sin(b)));
        }
    }
}
