// (not in pbrt-v4) Wavelength importance sampling that accounts for the spectra of the light sources.
//
// pbrt-v4 samples wavelengths with a density that follows the response of the sensor and nothing else. For a light with a
// narrow or strongly peaked spectrum (a blackbody at a low temperature, a fluorescent tube) most of those wavelengths carry
// little energy, which shows up as color noise. West et al. 2020 (Continuous Multiple Importance Sampling, section 5) sample
// them with a density proportional to the product of the response of the observer and a mixture of the spectra of the lights.
//
// The wavelengths are still stratified in the pbrt way: the first one is sampled with u, the others with u + i / n, all
// through the inverse CDF of the same density, so every wavelength has that density as its marginal and the estimator
// divides by it, as with any other density. The estimate stays unbiased as long as the density is positive wherever the
// product of the response and the spectrum is, which the mix with the sensor-only density guarantees.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static unsafe class WavelengthDistribution
    {
        // One bin per nanometer between the limits of the visible wavelengths
        public const int BinCount = 470;

        public static Float BinCenter(int bin)
        {
            return Lambda_min + (Float)bin + 0.5f;
        }

        // The density is visibleMix times the sensor-only density plus (1 - visibleMix) times the density that is
        // proportional to the product of the response of the sensor and the spectrum, with the spectrum given per bin.
        public static PiecewiseConstant1D Create(Float[] spectrumPerBin, Float visibleMix)
        {
            if (spectrumPerBin.Length != BinCount)
                ErrorExit("A wavelength distribution takes one spectrum value per nanometer of the visible range.");
            Float[] visible = new Float[BinCount];
            Float[] weighted = new Float[BinCount];
            Float visibleSum = 0, weightedSum = 0;
            for (int bin = 0; bin < BinCount; ++bin)
            {
                Float lambda = BinCenter(bin);
                visible[bin] = VisibleWavelengthsPDF(lambda);
                Float response = Spectra.X().Evaluate(lambda) + Spectra.Y().Evaluate(lambda) + Spectra.Z().Evaluate(lambda);
                weighted[bin] = std.max<Float>(0, response * spectrumPerBin[bin]);
                visibleSum += visible[bin];
                weightedSum += weighted[bin];
            }
            if (!(weightedSum > 0) || IsInf(weightedSum) || !(visibleSum > 0))
                return null;
            Float[] density = new Float[BinCount];
            for (int bin = 0; bin < BinCount; ++bin)
                density[bin] = visibleMix * visible[bin] / visibleSum + (1 - visibleMix) * weighted[bin] / weightedSum;
            return new PiecewiseConstant1D(density, Lambda_min, Lambda_max);
        }

        // The spectra of the lights are their powers (Phi) over the wavelengths, so a bright light weighs more. The infinite
        // lights are left out: their spectra are those of an image or of a color space illuminant, and Phi of an image
        // light runs over all of its pixels. The mix with the sensor-only density covers what they add.
        public static PiecewiseConstant1D FromLights(List<Light> lights, Float visibleMix = 0.5f)
        {
            Float[] power = new Float[BinCount];
            bool any = false;
            for (int first = 0; first < BinCount; first += NSpectrumSamples)
            {
                SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
                for (int k = 0; k < NSpectrumSamples; ++k)
                    lambda[k] = BinCenter(std.min(first + k, BinCount - 1));
                foreach (Light light in lights)
                {
                    if (light.Type() == LightType.Infinite)
                        continue;
                    SampledSpectrum phi = light.Phi(lambda);
                    for (int k = 0; k < NSpectrumSamples && first + k < BinCount; ++k)
                    {
                        if (phi[k] > 0 && !IsInf(phi[k]))
                        {
                            power[first + k] += phi[k];
                            any = true;
                        }
                    }
                }
            }
            if (!any)
                return null;
            return Create(power, visibleMix);
        }
    }
}
