// (not in pbrt-v4) hoseksky.cs
//
// The sky of Hosek and Wilkie: "An Analytic Model for Full Spectral Sky-Dome Radiance" (SIGGRAPH 2012) and "Adding a
// Solar Radiance Function to the Hosek Skylight Model" (IEEE CG&A 2013), as a light.
//
//   LightSource "hosek" "vector3 sundir" [ 0.3 0.2 0.5 ] "float turbidity" 3 "spectrum albedo" [ 300 0.3 900 0.3 ]
//
// The model. A function of nine coefficients gives the radiance of the sky dome at the angle theta from the zenith and
// the angle gamma from the sun, for one waveband; the coefficients are fitted to a path tracer's skies and depend on the
// turbidity (1 to 10), the albedo of the ground (0 to 1) and the elevation of the sun (0 to 90 degrees), between which
// the model interpolates (a quintic Bezier curve in the elevation). There are eleven wavebands, 320 to 720 nm; between
// them the radiance is interpolated, and it falls to nothing from 720 to 760 nm, as in the authors' implementation. The
// solar radiance is what of the sun's own light reaches the ground, by turbidity and elevation, times the darkening of
// the solar disc toward its limb; the disc has the diameter of 0.51 degrees. HosekSkyModel is the port of the spectral
// part of their sample implementation (version 1.4a); the coefficients are in hosekskydata.cs.
//
// The light. An infinite light of the radiance of the model above the horizon, with z of "up" as the zenith; "sundir"
// points to the sun. "bool sun" (true) adds the solar disc. Below the horizon there is nothing, or with "bool ground" a
// ground of the albedo that the sky and the sun light, seen as from far above (its radiance is its albedo times the
// irradiance of a level surface, over pi). The radiance is in W m^-2 sr^-1 nm^-1 times 683, the units of the thermal
// emitters, so that the Y of the film is cd/m^2; "scale" multiplies it. The light is not polarised: the model has no
// polarisation.
//
// Sampling. A direction is drawn from the solar disc (uniformly) or from the sky, the two as a mixture by their shares of
// the light; the density of a direction is that of the mixture, so the light is found by its own samples and by the
// samples of the surfaces alike. The sky is sampled in one of two ways, "string sampling":
//   "table" (the default): a table of the luminance of the sky over the sphere, 128 by 128 (the equal-area square of
//       pbrt's image lights, with a floor so that no direction is left out).
//   "tgmm": the truncated Gaussian mixtures of Vitsas, Vardis and Papaioannou, "Sampling Clear Sky Models using Truncated
//       Gaussian Mixtures" (EGSR 2021): five Gaussians of the azimuth and of the elevation, each a product of two that are
//       sampled in closed form (by the error function), fitted by the authors to the luminance of this model for the
//       turbidities 2 to 10 and every third degree of the sun (hoseksamplingdata.cs); the nearest fit is taken. The
//       mixture covers the sky; a twentieth of the samples are uniform over the sky, so that the weight of a sample is
//       bounded where the Gaussians have all but ended (not in the paper); a ground, if there is one, is sampled
//       uniformly, by its share of the light.

using System;

namespace pbrt
{
    // The spectral sky-dome and solar radiance of Hosek and Wilkie for one sun elevation, turbidity and ground albedo
    public sealed class HosekSkyModel
    {
        public const double SolarRadius = 0.51 * Math.PI / 180 / 2;

        // `solarElevation`: radians, 0 to pi/2; `turbidity`: 1 to 10; `albedo`: of the ground at the eleven wavebands
        public HosekSkyModel(double solarElevation, double turbidity, double[] albedo)
        {
            this.turbidity = turbidity;
            for (int band = 0; band < Bands; ++band)
            {
                configs[band] = new double[9];
                CookConfiguration(HosekSkyData.datasets[band], configs[band], turbidity, albedo[band], solarElevation);
                radiances[band] = CookRadianceConfiguration(HosekSkyData.datasetsRad[band], turbidity, albedo[band], solarElevation);
            }
        }

        // The quintic Bezier curve of six control values at an index of the data, in the (cube root of the) elevation
        private static double Bezier(double[] data, int offset, int stride, double x)
        {
            double y = 1 - x;
            return y * y * y * y * y * data[offset] + 5 * y * y * y * y * x * data[offset + stride] +
                   10 * y * y * y * x * x * data[offset + 2 * stride] + 10 * y * y * x * x * x * data[offset + 3 * stride] +
                   5 * y * x * x * x * x * data[offset + 4 * stride] + x * x * x * x * x * data[offset + 5 * stride];
        }

        private static void CookConfiguration(double[] dataset, double[] config, double turbidity, double albedo, double solarElevation)
        {
            int intTurbidity = (int)turbidity;
            double turbidityRem = turbidity - intTurbidity;
            double x = Math.Pow(solarElevation / (Math.PI / 2), 1.0 / 3.0);
            for (int i = 0; i < 9; ++i)
            {
                // (albedo 0 and 1, at the turbidity below)
                config[i] = (1 - albedo) * (1 - turbidityRem) * Bezier(dataset, 9 * 6 * (intTurbidity - 1) + i, 9, x) +
                            albedo * (1 - turbidityRem) * Bezier(dataset, 9 * 6 * 10 + 9 * 6 * (intTurbidity - 1) + i, 9, x);
                if (intTurbidity == 10)
                {
                    continue;
                }
                // (and at the turbidity above)
                config[i] += (1 - albedo) * turbidityRem * Bezier(dataset, 9 * 6 * intTurbidity + i, 9, x) +
                             albedo * turbidityRem * Bezier(dataset, 9 * 6 * 10 + 9 * 6 * intTurbidity + i, 9, x);
            }
        }

        private static double CookRadianceConfiguration(double[] dataset, double turbidity, double albedo, double solarElevation)
        {
            int intTurbidity = (int)turbidity;
            double turbidityRem = turbidity - intTurbidity;
            double x = Math.Pow(solarElevation / (Math.PI / 2), 1.0 / 3.0);
            double result = (1 - albedo) * (1 - turbidityRem) * Bezier(dataset, 6 * (intTurbidity - 1), 1, x) +
                            albedo * (1 - turbidityRem) * Bezier(dataset, 6 * 10 + 6 * (intTurbidity - 1), 1, x);
            if (intTurbidity == 10)
            {
                return result;
            }
            return result + (1 - albedo) * turbidityRem * Bezier(dataset, 6 * intTurbidity, 1, x) +
                   albedo * turbidityRem * Bezier(dataset, 6 * 10 + 6 * intTurbidity, 1, x);
        }

        // The radiance of one waveband of the sky dome, at the angle theta from the zenith and gamma from the sun
        public double BandRadiance(int band, double theta, double gamma)
        {
            double[] c = configs[band];
            double cosGamma = Math.Cos(gamma), cosTheta = Math.Cos(theta);
            double expM = Math.Exp(c[4] * gamma);
            double rayM = cosGamma * cosGamma;
            double mieM = (1 + cosGamma * cosGamma) / Math.Pow(1 + c[8] * c[8] - 2 * c[8] * cosGamma, 1.5);
            double zenith = Math.Sqrt(cosTheta);
            return (1 + c[0] * Math.Exp(c[1] / (cosTheta + 0.01))) * (c[2] + c[3] * expM + c[5] * rayM + c[6] * mieM + c[7] * zenith) *
                   radiances[band];
        }

        // The radiance of the sky dome at a wavelength (nm): W m^-2 sr^-1 nm^-1
        public double Radiance(double theta, double gamma, double wavelength)
        {
            int low = (int)((wavelength - 320.0) / 40.0);
            if (wavelength < 320.0 || low >= Bands)
            {
                return 0;
            }
            double interp = (wavelength - 320.0) / 40.0 % 1.0;
            double valueLow = BandRadiance(low, theta, gamma);
            if (interp < 1e-6)
            {
                return valueLow;
            }
            double result = (1 - interp) * valueLow;
            if (low + 1 < Bands)
            {
                result += interp * BandRadiance(low + 1, theta, gamma);
            }
            return result;
        }

        // The radiance of the sun itself through the atmosphere, for a turbidity of the data and a waveband, at an elevation:
        // a piecewise cubic of 45 pieces in the cube root of the elevation
        private static double SolarBand(int turbidityIndex, int band, double elevation)
        {
            const int pieces = 45, order = 4;
            int pos = (int)(Math.Pow(2.0 * elevation / Math.PI, 1.0 / 3.0) * pieces);
            if (pos > 44)
            {
                pos = 44;
            }
            double breakX = Math.Pow((double)pos / pieces, 3.0) * (Math.PI * 0.5);
            double[] data = HosekSkyData.solarDatasets[band];
            int index = order * pieces * turbidityIndex + order * (pos + 1) - 1;
            double result = 0, x = elevation - breakX, power = 1;
            for (int i = 0; i < order; ++i)
            {
                result += power * data[index - i];
                power *= x;
            }
            return result;
        }

        // The radiance of the solar disc alone (without the sky in front of it) at a wavelength of 320 to 720 nm, for a point
        // of the disc at the elevation `elevation` and at the angle gamma from its centre: W m^-2 sr^-1 nm^-1
        public double SolarRadiance(double elevation, double gamma, double wavelength)
        {
            if (wavelength < 320.0 || wavelength > 720.0 || !(elevation >= 0))
            {
                return 0;
            }
            double sinRadius = Math.Sin(SolarRadius), sinGamma = Math.Sin(gamma);
            double sc2 = 1.0 - sinGamma * sinGamma / (sinRadius * sinRadius);
            if (!(sc2 > 0))
            {
                return 0;
            }
            double sampleCosine = Math.Sqrt(sc2);

            int turbidityLow = (int)turbidity - 1;
            double turbidityFraction = turbidity - (turbidityLow + 1);
            if (turbidityLow == 9)
            {
                turbidityLow = 8;
                turbidityFraction = 1.0;
            }
            int low = (int)((wavelength - 320.0) / 40.0);
            double fraction = wavelength % 40.0 / 40.0;
            if (low == 10)
            {
                low = 9;
                fraction = 1.0;
            }
            double direct = (1 - turbidityFraction) * ((1 - fraction) * SolarBand(turbidityLow, low, elevation) +
                                                        fraction * SolarBand(turbidityLow, low + 1, elevation)) +
                            turbidityFraction * ((1 - fraction) * SolarBand(turbidityLow + 1, low, elevation) +
                                                 fraction * SolarBand(turbidityLow + 1, low + 1, elevation));
            // (the darkening toward the limb: a polynomial of the fifth order in the cosine at the surface of the sun)
            double[] limbLow = HosekSkyData.limbDarkeningDatasets[low], limbHigh = HosekSkyData.limbDarkeningDatasets[low + 1];
            double darkening = 0, power = 1;
            for (int i = 0; i < 6; ++i)
            {
                darkening += ((1 - fraction) * limbLow[i] + fraction * limbHigh[i]) * power;
                power *= sampleCosine;
            }
            return direct * darkening;
        }

        public const int Bands = 11;
        private readonly double turbidity;
        private readonly double[][] configs = new double[Bands][];
        private readonly double[] radiances = new double[Bands];
    }

    public sealed unsafe class HosekSkyLight : LightBase
    {
        // `zenith`, `sunDirection`: render space, unit length; `scale`: of the radiance of the model; `albedo`: of the
        // ground, at the eleven wavebands; `sun`: with the solar disc; `ground`: with the ground below the horizon
        // `mixture`: the sky is sampled by the truncated Gaussian mixture and not by the table
        public HosekSkyLight(Vector3f zenith, Vector3f sunDirection, double turbidity, double[] albedo, Float scale, bool sun, bool ground,
                             bool mixture)
            : base(LightType.Infinite, new Transform(), new MediumInterface())
        {
            this.zenith = zenith;
            this.sunDirection = sunDirection;
            this.scale = scale;
            this.sun = sun;
            double elevation = Math.Asin(Math.Min(Math.Max((double)pbrt.Dot(zenith, sunDirection), 0.0), 1.0));
            model = new HosekSkyModel(elevation, turbidity, albedo);
            // (the frame of the table: z to the zenith, x toward the sun)
            frame = pbrt.AbsDot(zenith, sunDirection) < 0.9999f ? Frame.FromXZ(pbrt.Normalize(sunDirection - pbrt.Dot(zenith, sunDirection) * zenith), zenith)
                                                                : Frame.FromZ(zenith);
            cosSolarRadius = Math.Cos(HosekSkyModel.SolarRadius);
            sunSolidAngle = 2 * Math.PI * (1 - cosSolarRadius);

            // The luminance of the sky over the sphere, for the table of the directions; and with it the luminous irradiance
            // of a level surface, by wavebands, for the ground
            const int resolution = 128, sub = 2;
            Float[] luminance = new Float[resolution * resolution];
            double[] irradiance = new double[HosekSkyModel.Bands];
            double cell = 4 * Math.PI / (resolution * resolution * sub * sub);
            for (int pass = 0; pass < 2; ++pass)
            {
                if (pass == 1)
                {
                    // (the sun on the level surface, by a few rings of its disc)
                    for (int band = 0; band < HosekSkyModel.Bands && sun; ++band)
                    {
                        irradiance[band] += SunIrradiance(elevation, 320 + 40 * band);
                    }
                    if (ground)
                    {
                        groundRadiance = new double[HosekSkyModel.Bands];
                        for (int band = 0; band < HosekSkyModel.Bands; ++band)
                        {
                            groundRadiance[band] = albedo[band] * irradiance[band] / Math.PI;
                        }
                    }
                }
                for (int v = 0; v < resolution; ++v)
                {
                    for (int u = 0; u < resolution; ++u)
                    {
                        double sum = 0;
                        for (int s = 0; s < sub * sub; ++s)
                        {
                            Point2f uv = new Point2f((u + (s % sub + 0.5f) / sub) / resolution, (v + (s / sub + 0.5f) / sub) / resolution);
                            Vector3f w = frame.FromLocal(pbrt.EqualAreaSquareToSphere(uv));
                            double cosTheta = pbrt.Dot(w, zenith);
                            for (int band = 0; band < HosekSkyModel.Bands; ++band)
                            {
                                double value = BandRadiance(band, w);
                                sum += value * BandLuminance[band];
                                if (pass == 0 && cosTheta > 0)
                                {
                                    irradiance[band] += value * cosTheta * cell;
                                }
                            }
                        }
                        luminance[v * resolution + u] = (Float)(sum / (sub * sub));
                    }
                }
            }
            double total = 0;
            foreach (Float value in luminance)
            {
                total += value;
            }
            double mean = total / luminance.Length;
            // (a floor, so that no direction of the sphere is left without a density)
            for (int i = 0; i < luminance.Length; ++i)
            {
                luminance[i] = (Float)(luminance[i] + 0.02 * mean);
            }
            if (mixture)
            {
                // The nearest of the fitted skies, and the part of each Gaussian that is within its range
                int turbidityIndex = Math.Min(Math.Max((int)Math.Round(turbidity) - HosekSamplingData.FirstTurbidity, 0), HosekSamplingData.Turbidities - 1);
                int elevationIndex = Math.Min(Math.Max((int)Math.Round((elevation * 180 / Math.PI - HosekSamplingData.FirstElevation) /
                                                                        HosekSamplingData.ElevationStep), 0), HosekSamplingData.Elevations - 1);
                components = new double[HosekSamplingData.Components * 5];
                Array.Copy(HosekSamplingData.mixtures, (turbidityIndex * HosekSamplingData.Elevations + elevationIndex) * components.Length,
                           components, 0, components.Length);
                // (the distribution functions of the Gaussians at the ends of their ranges: of the azimuth at 0 and 2 pi, of the
                // elevation at 0 and pi/2)
                cumulative = new double[HosekSamplingData.Components * 4];
                for (int k = 0; k < HosekSamplingData.Components; ++k)
                {
                    cumulative[4 * k] = Cumulative(0, components[5 * k], components[5 * k + 2]);
                    cumulative[4 * k + 1] = Cumulative(2 * Math.PI, components[5 * k], components[5 * k + 2]);
                    cumulative[4 * k + 2] = Cumulative(0, components[5 * k + 1], components[5 * k + 3]);
                    cumulative[4 * k + 3] = Cumulative(0.5 * Math.PI, components[5 * k + 1], components[5 * k + 3]);
                }
                // (the share of the ground in the light of the sphere without the disc: the lower half of the table's cells)
                double below = 0;
                for (int v = 0; v < resolution; ++v)
                {
                    for (int u = 0; u < resolution; ++u)
                    {
                        Vector3f w = pbrt.EqualAreaSquareToSphere(new Point2f((u + 0.5f) / resolution, (v + 0.5f) / resolution));
                        if (w.z < 0)
                        {
                            below += luminance[v * resolution + u] - 0.02 * mean;
                        }
                    }
                }
                groundShare = groundRadiance != null && total > 0 ? Math.Min(Math.Max(below / total, 0.02), 0.9) : 0;
            }
            else
            {
                distribution = new PiecewiseConstant2D(new pstd.span<Float>(luminance), resolution, resolution,
                                                       new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));
            }

            // The shares of the sky and of the disc in the light, by their luminance over the sphere
            skyLuminance = mean * 4 * Math.PI;
            double sunLuminance = 0;
            for (int band = 0; band < HosekSkyModel.Bands && sun; ++band)
            {
                sunLuminance += SunIrradiance(Math.PI / 2, 320 + 40 * band, elevation) * BandLuminance[band];
            }
            sunShare = sun && sunLuminance > 0 ? Math.Min(Math.Max(sunLuminance / (sunLuminance + skyLuminance), 0.05), 0.95) : 0;
            totalLuminance = skyLuminance + sunLuminance;
        }

        // The weights of the wavebands in the luminance: the luminous efficiency at the waveband times its width (nm)
        private static readonly double[] BandLuminance = Luminances();

        private static double[] Luminances()
        {
            double[] weights = new double[HosekSkyModel.Bands];
            for (int band = 0; band < HosekSkyModel.Bands; ++band)
            {
                weights[band] = 40 * Spectra.Y().Evaluate(320 + 40 * band);
            }
            return weights;
        }

        // The integral of the radiance of the disc times the cosine at a surface whose normal has the elevation
        // `normalElevation` of the sun (pi/2: a surface that faces the sun; the elevation of the sun: a level surface is
        // meant by the two-argument form), for a disc whose centre is at `discElevation`
        private double SunIrradiance(double normalElevation, double wavelength, double discElevation)
        {
            const int rings = 16;
            double sum = 0;
            for (int ring = 0; ring < rings; ++ring)
            {
                // (rings of equal solid angle; the elevation of the disc is taken at its centre)
                double cosGamma = 1 - (ring + 0.5) / rings * (1 - cosSolarRadius);
                sum += model.SolarRadiance(discElevation, Math.Acos(cosGamma), wavelength) * sunSolidAngle / rings;
            }
            return sum * Math.Sin(normalElevation);
        }

        private double SunIrradiance(double elevation, double wavelength)
        {
            return SunIrradiance(elevation, wavelength, elevation);
        }

        // The radiance of one waveband of the sky, or of the ground, in a direction (without the solar disc)
        private double BandRadiance(int band, Vector3f w)
        {
            double cosTheta = pbrt.Dot(w, zenith);
            if (!(cosTheta > 0))
            {
                return groundRadiance != null ? groundRadiance[band] : 0;
            }
            return Math.Max(model.BandRadiance(band, Math.Acos(Math.Min(cosTheta, 1.0)), Gamma(w)), 0.0);
        }

        // The angle of a direction from the sun, accurate where it is small
        private double Gamma(Vector3f w)
        {
            Vector3f difference = w - sunDirection;
            return 2 * Math.Asin(Math.Min(0.5 * Math.Sqrt((double)pbrt.LengthSquared(difference)), 1.0));
        }

        // The radiance in a direction (render space, unit length)
        private SampledSpectrum Radiance(Vector3f w, SampledWavelengths lambda)
        {
            SampledSpectrum L = new SampledSpectrum(0.0f);
            double cosTheta = pbrt.Dot(w, zenith);
            double gamma = Gamma(w);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double wavelength = lambda[i];
                double value;
                if (!(cosTheta > 0))
                {
                    value = 0;
                    if (groundRadiance != null && wavelength >= 320 && wavelength < 760)
                    {
                        // (between the wavebands as the sky is)
                        int low = (int)((wavelength - 320.0) / 40.0);
                        double interp = (wavelength - 320.0) / 40.0 - low;
                        value = (1 - interp) * groundRadiance[low] + (low + 1 < HosekSkyModel.Bands ? interp * groundRadiance[low + 1] : 0);
                    }
                }
                else
                {
                    double theta = Math.Acos(Math.Min(cosTheta, 1.0));
                    value = Math.Max(model.Radiance(theta, gamma, wavelength), 0.0);
                    if (sun && gamma < HosekSkyModel.SolarRadius)
                    {
                        value += Math.Max(model.SolarRadiance(Math.PI / 2 - theta, gamma, wavelength), 0.0);
                    }
                }
                L[i] = (Float)(scale * value);
            }
            return L;
        }

        public override SampledSpectrum Le(Ray ray, SampledWavelengths lambda)
        {
            return Radiance(pbrt.Normalize(ray.d), lambda);
        }

        // The part of the samples of the sky that are uniform over it, beside the mixture: the fits are close where the sky is
        // bright, and far from the Gaussians a density that is next to nothing would give a sample there a weight without
        // bound. (Not in the paper.)
        private const double UniformShare = 0.05;

        // The distribution function of a Gaussian
        private static double Cumulative(double x, double mean, double deviation)
        {
            return 0.5 * (1 + std.erf((x - mean) / (deviation * 1.4142135623730951)));
        }

        // A sample of a Gaussian truncated to the range from 0 to `end`, whose distribution function is `low` at 0 and `high`
        // at `end`
        private static double SampleTruncated(double u, double mean, double deviation, double end, double low, double high)
        {
            double probability = Math.Min(Math.Max(low + u * (high - low), 1e-300), 1 - 1e-16);
            // (the inverse of the error function: the single precision one, then Newton's steps to double precision)
            double target = 2 * probability - 1;
            double y = pbrt.ErfInv((Float)Math.Min(Math.Max(target, -0.99999), 0.99999));
            for (int i = 0; i < 8; ++i)
            {
                double step = (std.erf(y) - target) / (1.1283791670955126 * Math.Exp(-y * y));
                y -= step;
                if (Math.Abs(step) < 1e-12)
                {
                    break;
                }
            }
            return Math.Min(Math.Max(mean + deviation * 1.4142135623730951 * y, 0.0), end);
        }

        // The density of a direction of the frame of the sky (z to the zenith, x toward the sun) by the mixture of the sky and
        // the uniform lower half, per solid angle. The azimuth of the fits has the sun at pi/2.
        private double MixtureDensity(Vector3f local)
        {
            if (local.z < 0)
            {
                return groundShare / (2 * Math.PI);
            }
            // (the elevation by its cosine, which near the zenith is held far better than by the height of the direction)
            double cosElevation = Math.Sqrt((double)local.x * local.x + (double)local.y * local.y);
            double elevation = Math.Atan2(local.z, cosElevation);
            double azimuth = Math.Atan2(local.y, local.x) + 0.5 * Math.PI;
            if (azimuth < 0)
            {
                azimuth += 2 * Math.PI;
            }
            double density = 0;
            for (int k = 0; k < HosekSamplingData.Components; ++k)
            {
                // (the two Gaussians over the parts of them within their ranges)
                density += components[5 * k + 4] * SpectralLineSampling.Gaussian(azimuth, components[5 * k], components[5 * k + 2]) *
                           SpectralLineSampling.Gaussian(elevation, components[5 * k + 1], components[5 * k + 3]) /
                           ((cumulative[4 * k + 1] - cumulative[4 * k]) * (cumulative[4 * k + 3] - cumulative[4 * k + 2]));
            }
            // (per solid angle: over the cosine of the elevation, which is kept from zero at the zenith; and the uniform part)
            return (1 - groundShare) * ((1 - UniformShare) * density / Math.Max(cosElevation, 1e-7) + UniformShare / (2 * Math.PI));
        }

        private Vector3f SampleMixture(Point2f u)
        {
            if (u[0] < groundShare)
            {
                Vector3f down = pbrt.SampleUniformHemisphere(new Point2f((Float)Math.Min(u[0] / groundShare, (double)FloatOneMinusEpsilon), u[1]));
                return new Vector3f(down.x, down.y, -down.z);
            }
            double rest = (u[0] - groundShare) / (1 - groundShare);
            if (rest < UniformShare)
            {
                return pbrt.SampleUniformHemisphere(new Point2f((Float)Math.Min(rest / UniformShare, (double)FloatOneMinusEpsilon), u[1]));
            }
            // (the component by its weight, with what is left of the first number for the azimuth)
            rest = (rest - UniformShare) / (1 - UniformShare);
            int chosen = HosekSamplingData.Components - 1;
            for (int k = 0; k < HosekSamplingData.Components; ++k)
            {
                double weight = components[5 * k + 4];
                if (rest < weight || k == HosekSamplingData.Components - 1)
                {
                    chosen = k;
                    rest = Math.Min(rest / weight, 1.0);
                    break;
                }
                rest -= weight;
            }
            double azimuth = SampleTruncated(rest, components[5 * chosen], components[5 * chosen + 2], 2 * Math.PI, cumulative[4 * chosen],
                                             cumulative[4 * chosen + 1]) - 0.5 * Math.PI;
            double elevation = SampleTruncated(u[1], components[5 * chosen + 1], components[5 * chosen + 3], 0.5 * Math.PI,
                                               cumulative[4 * chosen + 2], cumulative[4 * chosen + 3]);
            double cosElevation = Math.Cos(elevation);
            return new Vector3f((Float)(cosElevation * Math.Cos(azimuth)), (Float)(cosElevation * Math.Sin(azimuth)), (Float)Math.Sin(elevation));
        }

        // The density of a direction, per solid angle: the mixture of the disc and of the sky
        private Float Density(Vector3f w)
        {
            double density = (1 - sunShare) * (components != null ? MixtureDensity(frame.ToLocal(w))
                                                                  : distribution.PDF(pbrt.EqualAreaSphereToSquare(frame.ToLocal(w))) / (4 * Math.PI));
            if (sunShare > 0 && Gamma(w) < HosekSkyModel.SolarRadius)
            {
                density += sunShare / sunSolidAngle;
            }
            return (Float)density;
        }

        private Vector3f SampleDirection(Point2f u)
        {
            if (u[0] < sunShare)
            {
                Point2f remapped = new Point2f((Float)Math.Min(u[0] / sunShare, (double)FloatOneMinusEpsilon), u[1]);
                return Frame.FromZ(sunDirection).FromLocal(pbrt.SampleUniformCone(remapped, (Float)cosSolarRadius));
            }
            Point2f rest = new Point2f((Float)Math.Min((u[0] - sunShare) / (1 - sunShare), (double)FloatOneMinusEpsilon), u[1]);
            return frame.FromLocal(components != null ? SampleMixture(rest) : pbrt.EqualAreaSquareToSphere(distribution.Sample(rest)));
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u, SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            Vector3f wi = pbrt.Normalize(SampleDirection(u));
            Float pdf = Density(wi);
            if (!(pdf > 0))
            {
                return default;
            }
            SampledSpectrum L = Radiance(wi, lambda);
            if (!L)
            {
                return default;
            }
            return new LightLiSample(L, wi, pdf, new Interaction(ctx.p() + wi * (2 * sceneRadius), (MediumInterface)null));
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f w, bool allowIncompletePDF = false)
        {
            return Density(pbrt.Normalize(w));
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
        {
            Vector3f toSky = pbrt.Normalize(SampleDirection(u1));
            Float pdfDir = Density(toSky);
            if (!(pdfDir > 0))
            {
                return default;
            }
            Frame wFrame = Frame.FromZ(toSky);
            Point2f cd = pbrt.SampleUniformDiskConcentric(u2);
            Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));
            Ray ray = new Ray(pDisk + sceneRadius * toSky, -toSky, time);
            return new LightLeSample(Radiance(toSky, lambda), ray, 1 / (Pi * pbrt.Sqr(sceneRadius)), pdfDir);
        }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfPos = 1 / (Pi * pbrt.Sqr(sceneRadius));
            *pdfDir = Density(pbrt.Normalize(-ray.d));
        }

        // The constant spectrum of the luminance of the light over the sphere, on the disc of the scene
        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            return new SampledSpectrum((Float)(scale * totalLuminance / CIE_Y_integral * Math.PI * sceneRadius * sceneRadius));
        }

        public override pstd.optional<LightBounds> Bounds()
        {
            return default;
        }

        public override void Preprocess(Bounds3f sceneBounds)
        {
            Point3f c;
            Float r;
            sceneBounds.BoundingSphere(&c, &r);
            sceneCenter = c;
            sceneRadius = r;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ HosekSkyLight %s sun: %s scale: %f ]", BaseToString(), sunDirection, scale);
        }

        public static HosekSkyLight Create(Transform renderFromLight, ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Vector3f up = parameters.GetOneVector3f("up", new Vector3f(0, 0, 1));
            Vector3f sunDirection = parameters.GetOneVector3f("sundir", new Vector3f(0, 0, 1));
            double turbidity = parameters.GetOneFloat("turbidity", 3.0f);
            Spectrum albedo = parameters.GetOneSpectrum("albedo", new ConstantSpectrum(0.3f), SpectrumType.Albedo, alloc);
            Float scale = parameters.GetOneFloat("scale", 1.0f);
            bool sun = parameters.GetOneBool("sun", true);
            bool ground = parameters.GetOneBool("ground", false);
            string sampling = parameters.GetOneString("sampling", "table");
            if (sampling != "table" && sampling != "tgmm")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: sampling unknown (table, tgmm).", sampling));
            }
            if (!(turbidity >= 1) || !(turbidity <= 10))
            {
                pbrt.ErrorExit(loc, "\"hosek\": \"turbidity\" must be between 1 and 10.");
            }
            if (pbrt.LengthSquared(up) == 0 || pbrt.LengthSquared(sunDirection) == 0)
            {
                pbrt.ErrorExit(loc, "\"hosek\": \"up\" and \"sundir\" must not be zero.");
            }
            Vector3f zenith = pbrt.Normalize(renderFromLight.Apply(up));
            Vector3f toSun = pbrt.Normalize(renderFromLight.Apply(sunDirection));
            if (pbrt.Dot(zenith, toSun) < 0)
            {
                pbrt.ErrorExit(loc, "\"hosek\": the sun is below the horizon; the model is for a sun above it.");
            }
            double[] bandAlbedo = new double[HosekSkyModel.Bands];
            for (int band = 0; band < HosekSkyModel.Bands; ++band)
            {
                bandAlbedo[band] = Math.Min(Math.Max((double)albedo.Evaluate(320 + 40 * band), 0.0), 1.0);
            }
            return new HosekSkyLight(zenith, toSun, turbidity, bandAlbedo, 683.002f * scale, sun, ground, sampling == "tgmm");
        }

        private readonly HosekSkyModel model;
        private readonly Vector3f zenith, sunDirection;
        private readonly Frame frame;
        private readonly Float scale;
        private readonly bool sun;
        private readonly double[] groundRadiance;
        private readonly PiecewiseConstant2D distribution;
        // (the truncated Gaussian mixture of the sky, five numbers a component, and the share of the uniform lower half)
        private readonly double[] components, cumulative;
        private readonly double groundShare;
        private readonly double cosSolarRadius, sunSolidAngle, sunShare, skyLuminance, totalLuminance;
        private Point3f sceneCenter;
        private Float sceneRadius;
    }
}
