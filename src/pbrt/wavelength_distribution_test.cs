// (not in pbrt-v4) Tests of the wavelength density that follows the spectra of the lights.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class WavelengthDistributionTests
    {
        private static Float SpikySpectrum(Float lambda)
        {
            return (Float)(Math.Exp(-0.5 * Math.Pow((lambda - 620) / 10, 2)) + 0.3 * Math.Exp(-0.5 * Math.Pow((lambda - 450) / 12, 2)) + 0.001);
        }

        private static Float Response(Float lambda)
        {
            return Spectra.X().Evaluate(lambda) + Spectra.Y().Evaluate(lambda) + Spectra.Z().Evaluate(lambda);
        }

        // Every one of the four stratified wavelengths estimates the integral of response times spectrum without bias, and
        // the density that follows the spectrum has a much smaller variance than the one that follows the sensor only.
        [TEST("WavelengthDistribution", "UnbiasedAndLowerVariance")]
        public static void WavelengthDistribution_UnbiasedAndLowerVariance()
        {
            Float[] bins = new Float[WavelengthDistribution.BinCount];
            for (int bin = 0; bin < bins.Length; ++bin)
                bins[bin] = SpikySpectrum(WavelengthDistribution.BinCenter(bin));
            PiecewiseConstant1D distribution = WavelengthDistribution.Create(bins, 0.25f);

            double exact = 0;
            for (double lambda = Lambda_min; lambda < Lambda_max; lambda += 0.05)
                exact += Response((Float)lambda) * SpikySpectrum((Float)lambda) * 0.05;

            int sampleCount = 400000;
            double[] sumDistribution = new double[NSpectrumSamples], sum2Distribution = new double[NSpectrumSamples];
            double[] sumVisible = new double[NSpectrumSamples], sum2Visible = new double[NSpectrumSamples];
            RNG rng = new RNG(17);
            for (int n = 0; n < sampleCount; ++n)
            {
                Float u = rng.Uniform<Float>();
                SampledWavelengths a = SampledWavelengths.Sample(u, distribution);
                SampledWavelengths b = SampledWavelengths.SampleVisible(u);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    double va = Response(a[i]) * SpikySpectrum(a[i]) / a.PDF()[i];
                    double vb = Response(b[i]) * SpikySpectrum(b[i]) / b.PDF()[i];
                    sumDistribution[i] += va;
                    sum2Distribution[i] += va * va;
                    sumVisible[i] += vb;
                    sum2Visible[i] += vb * vb;
                }
            }
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double meanDistribution = sumDistribution[i] / sampleCount;
                double varDistribution = sum2Distribution[i] / sampleCount - meanDistribution * meanDistribution;
                double meanVisible = sumVisible[i] / sampleCount;
                double varVisible = sum2Visible[i] / sampleCount - meanVisible * meanVisible;
                Console.WriteLine($"PROBE wavelengths {i}: exact {exact:F4} distribution {meanDistribution:F4} (variance {varDistribution:F2}) visible {meanVisible:F4} (variance {varVisible:F2})");
                EXPECT_LT(Math.Abs(meanDistribution - exact), 4 * Math.Sqrt(varDistribution / sampleCount) + 1e-3 * exact);
                EXPECT_LT(Math.Abs(meanVisible - exact), 4 * Math.Sqrt(varVisible / sampleCount) + 1e-3 * exact);
                EXPECT_LT(3 * varDistribution, varVisible);
            }
        }

        // The wavelengths of one sample stay stratified: they are the quantiles u + i/4 of one density
        [TEST("WavelengthDistribution", "Stratified")]
        public static void WavelengthDistribution_Stratified()
        {
            Float[] bins = new Float[WavelengthDistribution.BinCount];
            for (int bin = 0; bin < bins.Length; ++bin)
                bins[bin] = SpikySpectrum(WavelengthDistribution.BinCenter(bin));
            PiecewiseConstant1D distribution = WavelengthDistribution.Create(bins, 0.25f);
            RNG rng = new RNG(5);
            for (int n = 0; n < 1000; ++n)
            {
                Float u = rng.Uniform<Float>() * 0.2f;
                SampledWavelengths w = SampledWavelengths.Sample(u, distribution);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    EXPECT_GT(w.PDF()[i], 0);
                    Float inverse = distribution.Invert(w[i]).value();
                    EXPECT_LT(Math.Abs(inverse - (u + (Float)i / NSpectrumSamples)), 2e-3);
                }
            }
        }
    }
}
