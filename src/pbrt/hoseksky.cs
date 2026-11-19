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
// The light (what it shares with the other sky models is in skydomelight.cs). An infinite light of the radiance of the
// model above the horizon, with z of "up" as the zenith; "sundir"
// points to the sun. "bool sun" (true) adds the solar disc. Below the horizon there is nothing, or with "bool ground" a
// ground of the albedo that the sky and the sun light, seen as from far above (its radiance is its albedo times the
// irradiance of a level surface, over pi). The radiance is in W m^-2 sr^-1 nm^-1 times 683, the units of the thermal
// emitters, so that the Y of the film is cd/m^2; "scale" multiplies it.
//
// Polarisation. The model has none. With "string polarisation" "wilkie2004" the light of the sky dome is given the
// polarisation of Wilkie, Ulbricht, Tobler, Zotti and Purgathofer, "An Analytical Model for Skylight Polarisation"
// (EGSR 2004), which is made to go with any sky model: the light is linearly polarised at right angles to the plane of
// the sun and the direction that is looked in, to the degree
//
//   D = Mp(T) P(gamma, theta),   Mp(T) = exp(-(T - 0.6) / 4),
//   P = 1/1.2  sin^2(gamma) / (1 + cos^2(gamma))  ( theta cos(theta) + (pi/2 - theta) I(gamma, theta) ),
//   I = (1/F(theta, gamma) - 1/F_sun)  F_90 F_sun / (F_sun - F_90)
//
// with theta from the zenith, gamma from the sun, T the turbidity, and F the luminance distribution of Perez with the
// coefficients of Preetham, Shirley and Smits for the turbidity, at the direction, at the sun (F_sun) and at the point
// of the sun's vertical 90 degrees from it (F_90). It is a heuristic, of one degree for all wavelengths and without the
// neutral points of the real sky, as its authors say; the degree is kept between 0 and 1 (below a turbidity of about 2.5
// the formula can exceed 1). The solar disc and the ground are not polarised.
//
// Other stars. "float startemperature" (K) makes the sky that of an earth-like world of another star: the "alien world"
// of the authors' implementation (Wilkie and Hosek, "Predicting Sky Dome Appearance on Earth-like Extrasolar Worlds",
// SCCG 2013). The star is a blackbody of that temperature; each waveband of the sky and of the disc is scaled by what the
// star emits there over what the sun of the model emits. The disc is given the size at which the star sends as much to the
// world as the sun to the earth, times "float starintensity" (1): a red star is large in the sky, a blue one small. The
// pattern of the sky is not changed for the size of the disc, and the darkening toward the limb is the sun's (the
// authors' caveats). A sky of two stars is two of these lights with the intensities that add up.
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
using PLight = pbrt.PolarisedLightSystem.Light;

namespace pbrt
{
    // The spectral sky-dome and solar radiance of Hosek and Wilkie for one sun elevation, turbidity and ground albedo
    public sealed class HosekSkyModel
    {
        // The angular radius of the terrestrial sun
        public const double SolarRadius = 0.51 * Math.PI / 180 / 2;

        // `solarElevation`: radians, 0 to pi/2; `turbidity`: 1 to 10; `albedo`: of the ground at the eleven wavebands.
        // `starTemperature` (K), if it is not 0, makes the sky that of an earth-like world of another star (the "alien
        // world" of the authors' implementation): the star is a blackbody of that temperature, and `starIntensity` is
        // what it sends to the world as a multiple of what the sun sends to the earth.
        public HosekSkyModel(double solarElevation, double turbidity, double[] albedo, double starTemperature = 0, double starIntensity = 1)
        {
            this.turbidity = turbidity;
            for (int band = 0; band < Bands; ++band)
            {
                configs[band] = new double[9];
                CookConfiguration(HosekSkyData.datasets[band], configs[band], turbidity, albedo[band], solarElevation);
                radiances[band] = CookRadianceConfiguration(HosekSkyData.datasetsRad[band], turbidity, albedo[band], solarElevation);
                emissionSky[band] = 1;
                emissionSun[band] = 1;
            }
            discRadius = SolarRadius;
            if (starTemperature > 0)
            {
                // Each waveband is scaled by what the star emits there over what the sun of the model emits. A star of
                // another colour is given another size, so that a star of the intensity 1 sends what the sun sends (the
                // mean of the ratios of the nine visible wavebands): the disc is scaled by its size and the sky by the
                // colour alone.
                double ratio = 0;
                for (int band = 0; band < Bands; ++band)
                {
                    double wavelength = (320.0 + 40.0 * band) * 1e-9;
                    emissionSun[band] = Blackbody(starTemperature, wavelength) * BlackbodyScale / OriginalSolarRadiance[band];
                    if (band >= 2)
                    {
                        ratio += emissionSun[band] / 9.0;
                    }
                }
                discRadius = Math.Sqrt(starIntensity) * SolarRadius / Math.Sqrt(ratio);
                for (int band = 0; band < Bands; ++band)
                {
                    emissionSky[band] = starIntensity * emissionSun[band] / ratio;
                }
            }
        }

        // The angular radius of the disc of the star: that of the sun, or of the star of another world
        public double DiscRadius
        {
            get { return discRadius; }
        }

        // Planck's formula as the authors' implementation has it (`wavelength` in metres), and the factor that makes a
        // blackbody of 5800 K match the solar spectrum of the model from 380 to 720 nm
        private static double Blackbody(double temperature, double wavelength)
        {
            const double c1 = 3.74177e-16, c2 = 0.0143878;
            return c1 / Math.Pow(wavelength, 5.0) / (Math.Exp(c2 / (wavelength * temperature)) - 1.0);
        }

        private const double BlackbodyScale = 3.19992e-10;

        // The solar spectrum above the atmosphere that the model was fitted with, at the eleven wavebands
        private static readonly double[] OriginalSolarRadiance = { 7500.0, 12500.0, 21127.5, 26760.5, 30663.7, 27825.0, 25503.8, 25134.2, 23212.1, 21526.7, 19870.8 };

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
                   radiances[band] * emissionSky[band];
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
            double sinRadius = Math.Sin(discRadius), sinGamma = Math.Sin(gamma);
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
            double weightLow = (1 - fraction) * emissionSun[low], weightHigh = fraction * emissionSun[low + 1];
            double direct = (1 - turbidityFraction) * (weightLow * SolarBand(turbidityLow, low, elevation) +
                                                        weightHigh * SolarBand(turbidityLow, low + 1, elevation)) +
                            turbidityFraction * (weightLow * SolarBand(turbidityLow + 1, low, elevation) +
                                                 weightHigh * SolarBand(turbidityLow + 1, low + 1, elevation));
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
        // (what the star emits over what the sun emits, by waveband, for the sky and for the disc)
        private readonly double[] emissionSky = new double[Bands], emissionSun = new double[Bands];
        private readonly double discRadius;
    }

    public sealed unsafe class HosekSkyLight : SkyDomeLight
    {
        // `zenith`, `sunDirection`: render space, unit length; `scale`: of the radiance of the model; `albedo`: of the
        // ground, at the eleven wavebands; `sun`: with the solar disc; `ground`: with the ground below the horizon;
        // `mixture`: the sky is sampled by the truncated Gaussian mixture and not by the table; `polarised`: the sky dome
        // has the polarisation of the heuristic; `starTemperature` (K, not 0) and `starIntensity`: the sky of an earth-like
        // world of another star
        public HosekSkyLight(Vector3f zenith, Vector3f sunDirection, double turbidity, double[] albedo, Float scale, bool sun, bool ground,
                             bool mixture, bool polarised, double starTemperature = 0, double starIntensity = 1)
            : base(zenith, sunDirection, scale, sun, ground)
        {
            this.polarised = polarised;
            this.turbidity = turbidity;
            double elevation = Math.Max(solarElevation, 0.0);
            solarZenith = 0.5 * Math.PI - elevation;
            model = new HosekSkyModel(elevation, turbidity, albedo, starTemperature, starIntensity);
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
            }
            Initialise(albedo, !mixture);
        }

        protected override int BandCount
        {
            get { return HosekSkyModel.Bands; }
        }

        protected override double BandWavelength(int band)
        {
            return 320 + 40 * band;
        }

        protected override double BandWidth
        {
            get { return 40; }
        }

        protected override double SolarRadius
        {
            get { return model.DiscRadius; }
        }

        protected override void Evaluate(Vector3f local, double theta, double gamma, double wavelength, out double sky, out double solar)
        {
            sky = model.Radiance(theta, gamma, wavelength);
            solar = gamma < model.DiscRadius ? model.SolarRadiance(0.5 * Math.PI - theta, gamma, wavelength) : 0;
        }

        // (between the wavebands as the sky is: linearly, and to nothing from the last, 720 to 760 nm)
        protected override void BandsAt(double wavelength, out int low, out int high, out double weightHigh)
        {
            low = -1;
            high = -1;
            weightHigh = 0;
            if (wavelength < 320 || wavelength >= 760)
            {
                return;
            }
            low = (int)((wavelength - 320.0) / 40.0);
            weightHigh = (wavelength - 320.0) / 40.0 - low;
            high = low + 1 < HosekSkyModel.Bands ? low + 1 : -1;
        }

        protected override bool IsPolarised
        {
            get { return polarised; }
        }

        protected override double DegreeAt(Vector3f local, double theta, double gamma, double wavelength)
        {
            return DegreeOfPolarisation(theta, gamma);
        }

        protected override double SkyDensity(Vector3f local)
        {
            return components != null ? MixtureDensity(local) : base.SkyDensity(local);
        }

        protected override Vector3f SampleSky(Point2f u)
        {
            return components != null ? SampleMixture(u) : base.SampleSky(u);
        }

        // The luminance distribution of Perez with the coefficients of Preetham et al. for the turbidity
        private double Perez(double theta, double gamma)
        {
            double a = 0.1787 * turbidity - 1.4630, b = -0.3554 * turbidity + 0.4275, c = -0.0227 * turbidity + 5.3251;
            double d = 0.1206 * turbidity - 2.5771, e = -0.0670 * turbidity + 0.3703;
            double cosTheta = Math.Cos(theta), cosGamma = Math.Cos(gamma);
            // (at the horizon the first exponential is that of minus infinity, for the turbidities above 1.2 that make b negative)
            double horizon = cosTheta > 1e-9 ? Math.Exp(b / cosTheta) : (b < 0 ? 0 : double.PositiveInfinity);
            return (1 + a * horizon) * (1 + c * Math.Exp(d * gamma) + e * cosGamma * cosGamma);
        }

        // The degree of linear polarisation of the sky at the angle theta from the zenith and gamma from the sun, by the
        // heuristic of Wilkie et al. (2004)
        public double DegreeOfPolarisation(double theta, double gamma)
        {
            double atSun = Perez(solarZenith, 0), at90 = Perez(0.5 * Math.PI - solarZenith, 0.5 * Math.PI), here = Perez(theta, gamma);
            if (!(here > 0) || !(atSun > at90) || double.IsInfinity(here))
            {
                return 0;
            }
            double intensity = (1 / here - 1 / atSun) * at90 * atSun / (atSun - at90);
            double sinGamma = Math.Sin(gamma), cosGamma = Math.Cos(gamma);
            double pattern = sinGamma * sinGamma / (1 + cosGamma * cosGamma) * (theta * Math.Cos(theta) + (0.5 * Math.PI - theta) * intensity) / 1.2;
            double degree = Math.Exp(-(turbidity - 0.6) / 4) * pattern;
            return Math.Min(Math.Max(degree, 0.0), 1.0);
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

        public override string ToString()
        {
            return pbrt.StringPrintf("[ HosekSkyLight %s sun: %s scale: %f ]", BaseToString(), sunDirection, scale);
        }

        public static HosekSkyLight Create(Transform renderFromLight, ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
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
            string polarisation = parameters.GetOneString("polarisation", "none");
            if (polarisation != "none" && polarisation != "wilkie2004")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: polarisation unknown (none, wilkie2004).", polarisation));
            }
            if (!(turbidity >= 1) || !(turbidity <= 10))
            {
                pbrt.ErrorExit(loc, "\"hosek\": \"turbidity\" must be between 1 and 10.");
            }
            double starTemperature = parameters.GetOneFloat("startemperature", 0.0f);
            double starIntensity = parameters.GetOneFloat("starintensity", 1.0f);
            if (starTemperature < 0 || !(starIntensity > 0))
            {
                pbrt.ErrorExit(loc, "\"hosek\": \"startemperature\" (K) must not be negative and \"starintensity\" must be positive.");
            }
            // (the model is for a sun above the horizon)
            Directions(renderFromLight, parameters, loc, "hosek", 0.0, out Vector3f zenith, out Vector3f toSun);
            double[] bandAlbedo = new double[HosekSkyModel.Bands];
            for (int band = 0; band < HosekSkyModel.Bands; ++band)
            {
                bandAlbedo[band] = Math.Min(Math.Max((double)albedo.Evaluate(320 + 40 * band), 0.0), 1.0);
            }
            HosekSkyLight light = new HosekSkyLight(zenith, toSun, turbidity, bandAlbedo, 683.002f * scale, sun, ground, sampling == "tgmm",
                                                    polarisation == "wilkie2004", starTemperature, starIntensity);
            if (!(light.model.DiscRadius < 0.25 * Math.PI))
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"hosek\": the star of %f K and the intensity %f would have a disc of %f degrees across.",
                                                      starTemperature, starIntensity, 2 * light.model.DiscRadius * 180 / Math.PI));
            }
            return light;
        }

        private readonly HosekSkyModel model;
        private readonly bool polarised;
        private readonly double turbidity, solarZenith;
        // (the truncated Gaussian mixture of the sky, five numbers a component)
        private readonly double[] components, cumulative;
    }
}
