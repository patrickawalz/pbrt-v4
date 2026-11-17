// (not in pbrt-v4) The light of a glow discharge: a gas in a tube that an electric current excites (a neon sign, a spectral
// lamp). After Sivaram, Ramamoorthi and Li, "Modeling and Rendering Glow Discharge" (SIGGRAPH 2025), rendered as a medium
// like any other and without the approximations of the paper:
//
//   MakeNamedMedium "sign" "string type" "glowdischarge" "string gas" "neon" "point3 P" [ -1 0 0  1 0 0 ]
//       "float radius" 0.006 "float radiantpower" 2
//
// What emits. The positive column of the discharge, by Schottky's theory of it (ambipolar diffusion to the wall of a
// cylinder): the density of the electrons across the tube is J0(2.405 r / R), the same all along the tube, and the gas is
// excited in proportion to it. The light is the lines of the gas: each line is a Gaussian of its thermal Doppler width,
// sigma = lambda sqrt(k T / (m c^2)) with the temperature of the gas ("gastemperature"), and the lines stand to each other
// as g_k A_ki h nu exp(-E_k / k T_exc), the upper levels populated by a Boltzmann factor of an excitation temperature
// ("excitationtemperature"). The wavelengths, transition probabilities and levels are those of the NIST Handbook of Basic
// Atomic Spectroscopic Data (Sansonetti and Martin 2005). "float lines" [ wavelength strength ... ] (nanometres in air,
// relative radiant strengths) with "float atomicmass" gives any other spectrum.
//
// "string model" "drift" takes the paper's own model of the emission instead: the densities of the electrons and ions along
// a drift velocity (DriftModel below), with the same spectrum, brightness and rendering.
//
// How bright. "radiantpower" is the radiant power of the listed lines between 360 and 830 nm per metre of tube, in watts;
// "luminousflux" gives it in lumens per metre instead. The emission coefficient is then
//   j(r, lambda) = P / (4 pi) J0(2.405 r / R) / (1.3564 R^2) phi(lambda)        W m^-3 sr^-1 nm^-1
// (1.3564 R^2 is the integral of J0 over the cross section and phi the lines, of integral one), and the radiance has the
// units of the thermal emitters: W m^-2 sr^-1 nm^-1 times 683. "metersperunit" is the size of a scene unit.
//
// How it is rendered. The gas emits and does nothing else: no scattering and, the column being optically thin in these
// lines, no absorption. The Medium interface gives the emission as sigma_a Le, so the medium has an absorption `delta`
// that is 10^-6 of its majorant and Le = j / delta: sigma_a Le is j exactly, and what the absorption takes from the light
// that crosses the tube is below 10^-5. The majorant is a control majorant, "controlsamples" collisions on the way across
// the tube, held in a grid around the centre line: the trackers gather the emission at those collisions, and every
// integrator that renders media renders this one. The emission is a light as well ("emission_nee", on here by default).
//
// The wavelengths. A line is a ten-thousandth of a nanometre wide: the wavelengths of the film would never find it. While
// a glow discharge is in the scene the film takes "wavelengthfraction" of its wavelengths from the lines themselves
// (SpectralLineSampling: the line by its share of the tristimulus sum, the wavelength by its Gaussian) and the others as
// before, with the density of the mixture. That is importance sampling of the wavelength and nothing more: unbiased for
// everything in the scene, and a scene without a glow discharge is rendered as before.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // One line of a spectrum: the wavelength in air (nm), the transition probability (10^8 s^-1), the energy of the upper
    // level (cm^-1) and its statistical weight
    public struct SpectralLine
    {
        public SpectralLine(double wavelength, double transitionProbability, double upperEnergy, int upperWeight)
        {
            this.wavelength = wavelength;
            this.transitionProbability = transitionProbability;
            this.upperEnergy = upperEnergy;
            this.upperWeight = upperWeight;
        }

        public double wavelength, transitionProbability, upperEnergy;
        public int upperWeight;
    }

    // The lines of the gases, from the tables of persistent lines of the NIST Handbook of Basic Atomic Spectroscopic Data
    // (each row checked: the wavenumber of the line is the difference of its levels). They are the strongest lines of each
    // atom, not all of its lines: see docs/glow-discharge.md for what is missing.
    public static class SpectralLineData
    {
        public static readonly SpectralLine[] Neon = new SpectralLine[]
        {
            new SpectralLine(540.05618, 0.009, 152970.7328, 1),
            new SpectralLine(585.24879, 0.682, 152970.7328, 1),
            // (these four: Bridges and Wiese, "Experimental Transition Probabilities for Neon I", JOSA 60, 1970; within 20%)
            new SpectralLine(588.18952, 0.122, 151038.4524, 3),
            new SpectralLine(594.48342, 0.107, 150858.5079, 5),
            new SpectralLine(609.61631, 0.172, 150858.5079, 5),
            new SpectralLine(633.44278, 0.146, 149824.2215, 5),
            new SpectralLine(602.99969, 0.0561, 151038.4524, 3),
            new SpectralLine(607.43377, 0.603, 150917.4307, 1),
            new SpectralLine(614.30626, 0.282, 150315.8612, 5),
            new SpectralLine(616.35939, 0.146, 151038.4524, 3),
            new SpectralLine(621.72812, 0.0637, 150121.5922, 3),
            new SpectralLine(626.64950, 0.249, 150772.1118, 3),
            new SpectralLine(638.29917, 0.321, 150121.5922, 3),
            new SpectralLine(640.22480, 0.514, 149657.0393, 7),
            new SpectralLine(650.65281, 0.3, 149824.2215, 5),
            new SpectralLine(659.89529, 0.232, 151038.4524, 3),
            new SpectralLine(692.94673, 0.174, 150315.8612, 5),
            new SpectralLine(703.24131, 0.253, 148257.7898, 3),
            new SpectralLine(717.39381, 0.0287, 149824.2215, 5),
            new SpectralLine(724.51666, 0.0935, 148257.7898, 3),
        };

        // (the two blue lines are from Martin and Wiese, "Atomic Spectroscopy", table of selected lines: four figures)
        public static readonly SpectralLine[] Argon = new SpectralLine[]
        {
            new SpectralLine(415.86, 0.0140, 117184, 5),
            new SpectralLine(425.94, 0.0398, 118871, 1),
            new SpectralLine(696.54310, 0.067, 107496.4219, 3),
            new SpectralLine(706.72180, 0.0395, 107289.7054, 5),
            new SpectralLine(750.38690, 0.472, 108722.6247, 1),
            new SpectralLine(763.51060, 0.274, 106237.5571, 5),
            new SpectralLine(794.81760, 0.196, 107131.7139, 3),
            new SpectralLine(800.61570, 0.049, 106237.5571, 5),
            new SpectralLine(801.47860, 0.0928, 105617.2753, 5),
            new SpectralLine(810.36930, 0.25, 106087.2651, 3),
            new SpectralLine(811.53110, 0.331, 105462.7649, 7),
        };

        // Wiese and Fuhr, "Accurate Atomic Transition Probabilities for Hydrogen, Helium, and Lithium" (2009), table 14: every
        // multiplet between 380 and 730 nm with a transition probability of 10^6 s^-1 or more, each as one line (the fine
        // structure of a triplet is a few hundredths of a nanometre)
        public static readonly SpectralLine[] Helium = new SpectralLine[]
        {
            new SpectralLine(381.9620, 0.064351, 195260.0710, 15),
            new SpectralLine(386.7490, 0.024466, 194936.1181, 3),
            new SpectralLine(387.1786, 0.013386, 196955.4470, 5),
            new SpectralLine(388.8640, 0.094746, 185564.6000, 9),
            new SpectralLine(392.6544, 0.019371, 196595.3723, 5),
            new SpectralLine(396.4729, 0.069507, 191492.7101, 3),
            new SpectralLine(400.9256, 0.029612, 196070.1266, 5),
            new SpectralLine(402.3980, 0.011281, 195978.8936, 1),
            new SpectralLine(402.6210, 0.116, 193917.1520, 15),
            new SpectralLine(412.0840, 0.044529, 193346.9897, 3),
            new SpectralLine(414.3759, 0.048812, 195260.7688, 5),
            new SpectralLine(416.8971, 0.018298, 195114.8672, 1),
            new SpectralLine(438.7929, 0.089889, 193918.2882, 5),
            new SpectralLine(443.7553, 0.032689, 193663.5107, 1),
            new SpectralLine(447.1500, 0.24578, 191444.4840, 15),
            new SpectralLine(471.3170, 0.095209, 190298.1115, 3),
            new SpectralLine(492.1931, 0.19863, 191446.4540, 5),
            new SpectralLine(501.5678, 0.13372, 186209.3632, 3),
            new SpectralLine(504.7738, 0.067712, 190940.2252, 1),
            new SpectralLine(587.5660, 0.70703, 186101.5540, 15),
            new SpectralLine(667.8152, 0.63705, 186104.9646, 5),
            new SpectralLine(706.5250, 0.27853, 183236.7905, 3),
            new SpectralLine(728.1350, 0.18299, 184864.8282, 1),
        };

        public static readonly SpectralLine[] Mercury = new SpectralLine[]
        {
            new SpectralLine(365.01530, 1.3, 71431.3110, 7),
            new SpectralLine(404.65630, 0.21, 62350.4560, 3),
            new SpectralLine(435.83280, 0.557, 62350.4560, 3),
            new SpectralLine(546.07350, 0.487, 62350.4560, 3),
        };

        // The Balmer lines, each as one line with the transition probability averaged over its fine structure (Wiese and
        // Fuhr 2009, table 4)
        public static readonly SpectralLine[] Hydrogen = new SpectralLine[]
        {
            new SpectralLine(434.047, 0.025304, 105291.644, 50),
            new SpectralLine(486.134, 0.084193, 102823.879, 32),
            new SpectralLine(656.283, 0.44101, 97492.283, 18),
        };

        // (the table of selected lines)
        public static readonly SpectralLine[] Sodium = new SpectralLine[]
        {
            new SpectralLine(568.26, 0.103, 34549, 4),
            new SpectralLine(589.00, 0.611, 16973, 4),
            new SpectralLine(589.59, 0.610, 16956, 2),
        };

        // The lines of a gas by its name and the mass of its atom (unified atomic mass units); false for a name that is
        // not here
        public static bool Find(string gas, out SpectralLine[] lines, out double atomicMass)
        {
            if (gas == "neon")
            {
                lines = Neon;
                atomicMass = 20.180;
                return true;
            }
            if (gas == "argon")
            {
                lines = Argon;
                atomicMass = 39.948;
                return true;
            }
            if (gas == "helium")
            {
                lines = Helium;
                atomicMass = 4.0026;
                return true;
            }
            if (gas == "mercury")
            {
                lines = Mercury;
                atomicMass = 200.59;
                return true;
            }
            if (gas == "hydrogen")
            {
                lines = Hydrogen;
                atomicMass = 1.00794;
                return true;
            }
            if (gas == "sodium")
            {
                lines = Sodium;
                atomicMass = 22.990;
                return true;
            }
            lines = null;
            atomicMass = 0;
            return false;
        }
    }

    // A spectrum of lines as it is rendered: Gaussians of given centres and widths (nm) with radiant shares that sum to one
    public sealed class LineSpectrum
    {
        // The first and last wavelength that the film samples: lines outside are not rendered
        public const double FirstWavelength = 360, LastWavelength = 830;

        // The share of each line by the Boltzmann factor of its upper level, and its Doppler width
        public LineSpectrum(SpectralLine[] lines, double atomicMass, double gasTemperature, double excitationTemperature)
        {
            List<double> centres = new List<double>();
            List<double> weights = new List<double>();
            List<double> energies = new List<double>();
            double lowest = double.MaxValue;
            foreach (SpectralLine line in lines)
            {
                if (line.wavelength > FirstWavelength && line.wavelength < LastWavelength)
                {
                    lowest = Math.Min(lowest, line.upperEnergy);
                }
            }
            foreach (SpectralLine line in lines)
            {
                if (line.wavelength <= FirstWavelength || line.wavelength >= LastWavelength)
                {
                    continue;
                }
                centres.Add(line.wavelength);
                // (g A h nu, and the energy of the upper level above the lowest one)
                weights.Add(line.upperWeight * line.transitionProbability / line.wavelength);
                energies.Add(line.upperEnergy - lowest);
            }
            weight = weights.ToArray();
            energy = energies.ToArray();
            this.excitationTemperature = excitationTemperature;
            List<double> strengths = new List<double>();
            for (int i = 0; i < weight.Length; ++i)
            {
                strengths.Add(weight[i] * Math.Exp(-energy[i] * SecondRadiation / excitationTemperature));
            }
            Initialize(centres, strengths, atomicMass, gasTemperature);
        }

        // Given strengths
        public LineSpectrum(List<double> centres, List<double> strengths, double atomicMass, double gasTemperature)
        {
            List<double> keptCentres = new List<double>();
            List<double> keptStrengths = new List<double>();
            for (int i = 0; i < centres.Count; ++i)
            {
                if (centres[i] > FirstWavelength && centres[i] < LastWavelength && strengths[i] > 0)
                {
                    keptCentres.Add(centres[i]);
                    keptStrengths.Add(strengths[i]);
                }
            }
            Initialize(keptCentres, keptStrengths, atomicMass, gasTemperature);
        }

        private void Initialize(List<double> centres, List<double> strengths, double atomicMass, double gasTemperature)
        {
            const double BoltzmannOverMassEnergy = 1.380649e-23 / (1.66053906660e-27 * 299792458.0 * 299792458.0);
            double total = 0;
            for (int i = 0; i < strengths.Count; ++i)
            {
                total += strengths[i];
            }
            centre = new double[centres.Count];
            share = new double[centres.Count];
            width = new double[centres.Count];
            for (int i = 0; i < centres.Count; ++i)
            {
                centre[i] = centres[i];
                share[i] = strengths[i] / total;
                width[i] = centres[i] * Math.Sqrt(BoltzmannOverMassEnergy * gasTemperature / atomicMass);
            }
        }

        // (h c / k in cm K)
        private const double SecondRadiation = 1.438776877;

        // Whether the shares follow an excitation temperature (a table of lines) or are given
        public bool HasLevels { get { return energy != null; } }

        // The spectrum at the wavelengths of a path with the excitation temperature scaled by a factor: the shares of the
        // lines by the Boltzmann factors of that temperature, of sum one again
        public SampledSpectrum Sample(SampledWavelengths lambda, double temperatureFactor)
        {
            if (energy == null || temperatureFactor == 1)
            {
                return Sample(lambda);
            }
            if (!(scaledOwner == this && scaledLambda == lambda))
            {
                if (scaledProfile == null || scaledProfile.Length < centre.Length * NSpectrumSamples)
                {
                    scaledProfile = new double[centre.Length * NSpectrumSamples];
                }
                for (int k = 0; k < centre.Length; ++k)
                {
                    for (int i = 0; i < NSpectrumSamples; ++i)
                    {
                        scaledProfile[k * NSpectrumSamples + i] = Gaussian(lambda[i], centre[k], width[k]);
                    }
                }
                scaledOwner = this;
                scaledLambda = lambda;
            }
            double exponent = SecondRadiation / (excitationTemperature * Math.Max(temperatureFactor, 1e-12));
            double total = 0;
            Span<double> sums = stackalloc double[NSpectrumSamples];
            sums.Clear();
            for (int k = 0; k < centre.Length; ++k)
            {
                double strength = weight[k] * Math.Exp(-energy[k] * exponent);
                total += strength;
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    sums[i] += strength * scaledProfile[k * NSpectrumSamples + i];
                }
            }
            SampledSpectrum value = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                value[i] = (Float)(sums[i] / total);
            }
            return value;
        }

        private double[] weight, energy;
        private double excitationTemperature;
        [ThreadStatic] private static LineSpectrum scaledOwner;
        [ThreadStatic] private static SampledWavelengths scaledLambda;
        [ThreadStatic] private static double[] scaledProfile;

        public int Count { get { return centre.Length; } }

        // The spectrum at a wavelength, per nanometre: its integral over the wavelengths is one
        public double Evaluate(double lambda)
        {
            double sum = 0;
            for (int i = 0; i < centre.Length; ++i)
            {
                sum += share[i] * Gaussian(lambda, centre[i], width[i]);
            }
            return sum;
        }

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

        // The spectrum at the wavelengths of a path. A path asks this at every collision with the same wavelengths: the last
        // answer of the thread is kept.
        public SampledSpectrum Sample(SampledWavelengths lambda)
        {
            if (cachedOwner == this && cachedLambda == lambda)
            {
                return cachedValue;
            }
            SampledSpectrum value = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                value[i] = (Float)Evaluate(lambda[i]);
            }
            cachedOwner = this;
            cachedLambda = lambda;
            cachedValue = value;
            return value;
        }

        // Lumens per watt of the spectrum: 683 times the sum of the shares by the luminous efficiency at each line
        public double LuminousEfficacy()
        {
            double sum = 0;
            for (int i = 0; i < centre.Length; ++i)
            {
                sum += share[i] * Spectra.Y().Evaluate((Float)centre[i]);
            }
            return 683.002 * sum;
        }

        // The tristimulus values of one watt of the spectrum (per 683 lumens: Y is the luminous efficiency), the lines taken
        // as infinitely thin
        public void Tristimulus(out double X, out double Y, out double Z)
        {
            X = 0;
            Y = 0;
            Z = 0;
            for (int i = 0; i < centre.Length; ++i)
            {
                X += share[i] * Spectra.X().Evaluate((Float)centre[i]);
                Y += share[i] * Spectra.Y().Evaluate((Float)centre[i]);
                Z += share[i] * Spectra.Z().Evaluate((Float)centre[i]);
            }
        }

        public double[] centre, share, width;

        [ThreadStatic] private static LineSpectrum cachedOwner;
        [ThreadStatic] private static SampledWavelengths cachedLambda;
        [ThreadStatic] private static SampledSpectrum cachedValue;
    }

    // The wavelengths of the film while there are line spectra in the scene: a fraction of them from the lines, the others
    // from what the film samples otherwise, each with the density of the mixture
    public static class SpectralLineSampling
    {
        public static bool Active { get { return state != null; } }

        // A line spectrum of the scene; the fraction of the wavelengths that are taken from the lines is that of the last
        // spectrum that was added
        public static void Add(LineSpectrum spectrum, double fraction)
        {
            lock (spectra)
            {
                spectra.Add(spectrum);
                Rebuild(fraction);
            }
        }

        // Forgets the spectra, for tests that build several scenes in one process
        public static void Clear()
        {
            lock (spectra)
            {
                spectra.Clear();
                state = null;
            }
        }

        private static void Rebuild(double fraction)
        {
            List<double> centres = new List<double>();
            List<double> widths = new List<double>();
            List<double> weights = new List<double>();
            double total = 0;
            foreach (LineSpectrum spectrum in spectra)
            {
                // (a line is found in proportion to what it adds to the three tristimulus values together; each spectrum has
                // the same part of the wavelengths, whatever its lines are to the eye: how bright each one is in the image is
                // not known here)
                double[] response = new double[spectrum.Count];
                double sum = 0;
                for (int i = 0; i < spectrum.Count; ++i)
                {
                    Float centre = (Float)spectrum.centre[i];
                    response[i] = spectrum.share[i] * (Spectra.X().Evaluate(centre) + Spectra.Y().Evaluate(centre) + Spectra.Z().Evaluate(centre));
                    sum += response[i];
                }
                for (int i = 0; i < spectrum.Count; ++i)
                {
                    if (!(response[i] > 0))
                    {
                        continue;
                    }
                    centres.Add(spectrum.centre[i]);
                    widths.Add(spectrum.width[i]);
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

        // The density of the lines alone at a wavelength
        private static double LineDensity(State current, double lambda)
        {
            double sum = 0;
            for (int i = 0; i < current.centre.Length; ++i)
            {
                sum += current.weight[i] * LineSpectrum.Gaussian(lambda, current.centre[i], current.width[i]);
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
                double step = (Erf(y) - x) / (1.1283791670955126 * Math.Exp(-y * y));
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
        private static double Erf(double x)
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

        private static readonly List<LineSpectrum> spectra = new List<LineSpectrum>();
        private static volatile State state;
    }

    // The paper's model of the particle densities (Sivaram et al. 2025, equations 9 and 14): the electrons, the positive and
    // the negative ions along the drift, from densities of one where the drift enters. In the coordinates of the tube (the
    // length s along the centre line and the distance r from it) the drift is either along the tube ("laminar", with the
    // speed v0 (1 - (r/R)^2)^falloff exp(-decay s)) or toward the centre line ("extruding", with the speed
    // v0 (r/R) exp(-decay s), whose divergence is -2 v0 / R: no singularity on the axis). Its integral curves are then lines
    // of constant r, or of constant s, and the paper's backward trace is exact: what remains is one system of three
    // ordinary differential equations along the curve, solved here by an adaptive Runge-Kutta method (Dormand and Prince
    // 5(4)) to a tolerance, where the paper takes a fixed number of Euler steps.
    public sealed class DriftModel
    {
        public bool laminar = true;
        public double ionization = 100, recombination = 1e-8, attachment = 0.25, ionScale = 10;
        public double speed = 1, decay = 0, falloff = 1;
        public double radius, length;
        public double tolerance = 1e-9;

        // The speed of the drift
        public double Speed(double s, double r)
        {
            double along = speed * Math.Exp(-decay * s);
            if (laminar)
            {
                double x = r / radius;
                return falloff == 0 ? along : along * Math.Pow(Math.Max(0.0, 1 - x * x), falloff);
            }
            return along * r / radius;
        }

        // The strength of the emission at a point, the speed of the drift times the density of the electrons (equation 4)
        public double Emission(double s, double r)
        {
            double integral;
            return Solve(s, r, out integral);
        }

        // The right-hand sides of equations 14 over the rate of the variable of integration: the length s along a line of
        // constant r for the laminar drift, u = ln(R / r) along a radius at the length s for the extruding one. The fourth
        // component gathers the emission over the path (laminar: per length; extruding: over the cross section).
        private void Derivative(double x, double fixedCoordinate, ReadOnlySpan<double> y, Span<double> dy)
        {
            double local, divergence, rate, measure;
            if (laminar)
            {
                local = Speed(x, fixedCoordinate);
                divergence = -decay * local;
                rate = 1 / local;
                measure = 1;
            }
            else
            {
                double along = speed * Math.Exp(-decay * fixedCoordinate);
                double r = radius * Math.Exp(-x);
                local = along * r / radius;
                divergence = -2 * along / radius;
                rate = radius / along;
                measure = 2 * Math.PI * r * r;
            }
            double e = Math.Min(y[0], 700.0), p = Math.Min(y[1], 700.0), n = Math.Min(y[2], 700.0);
            dy[0] = rate * ((ionization - attachment) * local - recombination * Math.Exp(p) - divergence);
            dy[1] = rate * (ionization / ionScale * local * Math.Exp(Math.Min(e - p, 700.0)) - recombination / ionScale * (Math.Exp(e) + Math.Exp(n)) - divergence);
            dy[2] = rate * (attachment / ionScale * local * Math.Exp(Math.Min(e - n, 700.0)) - recombination / ionScale * Math.Exp(p) - divergence);
            dy[3] = local * Math.Exp(e) * measure;
        }

        // The emission at (s, r), and the integral of the emission along the path to there (laminar: over the length from
        // the inlet; extruding: over the cross section from the wall inward)
        public double Solve(double s, double r, out double integral)
        {
            integral = 0;
            double fixedCoordinate, end;
            if (laminar)
            {
                if (!(Speed(0, r) > 0))
                {
                    return 0;
                }
                fixedCoordinate = r;
                end = s;
            }
            else
            {
                if (!(r > 0))
                {
                    return 0;
                }
                fixedCoordinate = s;
                end = Math.Log(radius / Math.Min(r, radius));
            }
            Span<double> y = stackalloc double[4];
            Span<double> trial = stackalloc double[4];
            Span<double> k1 = stackalloc double[4];
            Span<double> k2 = stackalloc double[4];
            Span<double> k3 = stackalloc double[4];
            Span<double> k4 = stackalloc double[4];
            Span<double> k5 = stackalloc double[4];
            Span<double> k6 = stackalloc double[4];
            Span<double> k7 = stackalloc double[4];
            y.Clear();
            double x = 0;
            double h = Math.Min(end, 1e-3 * Math.Max(end, 1e-12));
            bool autonomous = laminar && decay == 0;
            Derivative(x, fixedCoordinate, y, k1);
            for (int step = 0; step < 2000000 && x < end; ++step)
            {
                // (the densities no longer change: the rest of a path whose equations do not depend on the place is known)
                if (autonomous && Math.Max(Math.Abs(k1[0]), Math.Max(Math.Abs(k1[1]), Math.Abs(k1[2]))) * (end - x) < 0.1 * tolerance)
                {
                    y[3] += k1[3] * (end - x);
                    x = end;
                    break;
                }
                h = Math.Min(h, end - x);
                for (int i = 0; i < 4; ++i)
                {
                    trial[i] = y[i] + h * (1.0 / 5) * k1[i];
                }
                Derivative(x + h / 5, fixedCoordinate, trial, k2);
                for (int i = 0; i < 4; ++i)
                {
                    trial[i] = y[i] + h * (3.0 / 40 * k1[i] + 9.0 / 40 * k2[i]);
                }
                Derivative(x + 3 * h / 10, fixedCoordinate, trial, k3);
                for (int i = 0; i < 4; ++i)
                {
                    trial[i] = y[i] + h * (44.0 / 45 * k1[i] - 56.0 / 15 * k2[i] + 32.0 / 9 * k3[i]);
                }
                Derivative(x + 4 * h / 5, fixedCoordinate, trial, k4);
                for (int i = 0; i < 4; ++i)
                {
                    trial[i] = y[i] + h * (19372.0 / 6561 * k1[i] - 25360.0 / 2187 * k2[i] + 64448.0 / 6561 * k3[i] - 212.0 / 729 * k4[i]);
                }
                Derivative(x + 8 * h / 9, fixedCoordinate, trial, k5);
                for (int i = 0; i < 4; ++i)
                {
                    trial[i] = y[i] + h * (9017.0 / 3168 * k1[i] - 355.0 / 33 * k2[i] + 46732.0 / 5247 * k3[i] + 49.0 / 176 * k4[i] - 5103.0 / 18656 * k5[i]);
                }
                Derivative(x + h, fixedCoordinate, trial, k6);
                for (int i = 0; i < 4; ++i)
                {
                    trial[i] = y[i] + h * (35.0 / 384 * k1[i] + 500.0 / 1113 * k3[i] + 125.0 / 192 * k4[i] - 2187.0 / 6784 * k5[i] + 11.0 / 84 * k6[i]);
                }
                Derivative(x + h, fixedCoordinate, trial, k7);
                // (the difference of the fifth and the fourth order results: the densities are logarithms, so an error
                // in them is a relative error; the gathered emission is compared with its size)
                double error = 0;
                for (int i = 0; i < 4; ++i)
                {
                    double difference = h * (71.0 / 57600 * k1[i] - 71.0 / 16695 * k3[i] + 71.0 / 1920 * k4[i] - 17253.0 / 339200 * k5[i] + 22.0 / 525 * k6[i] - 1.0 / 40 * k7[i]);
                    double size = i < 3 ? 1 : Math.Abs(trial[i]) + 1e-300;
                    error = Math.Max(error, Math.Abs(difference) / (tolerance * size));
                }
                if (error <= 1 || h <= 1e-14 * Math.Max(end, 1.0))
                {
                    x += h;
                    for (int i = 0; i < 4; ++i)
                    {
                        y[i] = trial[i];
                        k1[i] = k7[i];
                    }
                }
                h *= Math.Min(5.0, Math.Max(0.2, 0.9 * Math.Pow(Math.Max(error, 1e-10), -0.2)));
            }
            integral = y[3];
            double here = laminar ? Speed(s, r) : Speed(s, Math.Min(r, radius));
            return here * Math.Exp(Math.Min(y[0], 700.0));
        }

        // Nodes and weights of the Gauss-Legendre rule on (0, 1)
        public static void GaussLegendre(int count, out double[] nodes, out double[] weights)
        {
            nodes = new double[count];
            weights = new double[count];
            for (int i = 0; i < count; ++i)
            {
                double x = Math.Cos(Math.PI * (i + 0.75) / (count + 0.5));
                double derivative = 1;
                for (int iteration = 0; iteration < 100; ++iteration)
                {
                    double p0 = 1, p1 = x;
                    for (int k = 2; k <= count; ++k)
                    {
                        double p2 = ((2 * k - 1) * x * p1 - (k - 1) * p0) / k;
                        p0 = p1;
                        p1 = p2;
                    }
                    derivative = count * (x * p1 - p0) / (x * x - 1);
                    double change = p1 / derivative;
                    x -= change;
                    if (Math.Abs(change) < 1e-15)
                    {
                        break;
                    }
                }
                nodes[i] = 0.5 * (1 - x);
                weights[i] = 1 / ((1 - x * x) * derivative * derivative);
            }
        }

        // The integral of the emission over the volume of a straight tube, and the largest emission that the rule meets
        public double VolumeIntegral(out double largest)
        {
            const int Count = 32;
            double[] nodes, weights;
            GaussLegendre(Count, out nodes, out weights);
            double total = 0;
            largest = 0;
            if (laminar)
            {
                for (int i = 0; i < Count; ++i)
                {
                    double r = radius * nodes[i];
                    double alongLength;
                    largest = Math.Max(largest, Solve(length, r, out alongLength));
                    total += weights[i] * radius * 2 * Math.PI * r * alongLength;
                    for (int j = 1; j < 16; ++j)
                    {
                        largest = Math.Max(largest, Emission(length * j / 16, r));
                    }
                }
                return total;
            }
            int lengthCount = decay == 0 ? 1 : Count;
            for (int j = 0; j < lengthCount; ++j)
            {
                double s = decay == 0 ? 0 : length * nodes[j];
                double section;
                Solve(s, radius * 1e-13, out section);
                total += (decay == 0 ? length : weights[j] * length) * section;
                for (int i = 0; i < Count; ++i)
                {
                    largest = Math.Max(largest, Emission(s, radius * nodes[i]));
                }
            }
            return total;
        }
    }

    // GlowDischargeMedium Definition
    public sealed unsafe class GlowDischargeMedium : Medium, IMajorantMedium<DDAMajorantIterator>
    {
        // The first zero of J0, and the integral of J0(zero r) over the unit disc
        public const double BesselZero = 2.4048255576957728;
        public const double ProfileIntegral = 1.3563977298851069;

        public override string ToString()
        {
            return StringPrintf("[ GlowDischargeMedium bounds: %s radius: %f lines: %d ]", bounds, radius, spectrum.Count);
        }

        // `points`: the centre line in the space of the medium; `power`: the radiance that a scene unit of path would gather
        // if the whole power of a unit length of tube were emitted along it (683 P / (4 pi) over the metres of a scene unit);
        // `drift`: the paper's model of the densities, or null for the positive column
        public GlowDischargeMedium(Point3f[] points, Float radius, LineSpectrum spectrum, double power, Float controlSamples,
                                   Transform renderFromMedium, Allocator alloc, DriftModel drift = null, bool speedTemperature = false)
        {
            this.points = points;
            this.radius = radius;
            this.spectrum = spectrum;
            this.renderFromMedium = renderFromMedium;
            this.drift = drift;
            this.speedTemperature = speedTemperature && drift != null && spectrum.HasLevels;
            // (the collisions that gather the emission: `controlSamples` of them on a diameter)
            majorant = controlSamples / (2 * radius);
            absorption = 1e-6f * majorant;
            phase = new HGPhaseFunction(0f);

            // The length along the centre line at the start of each segment
            start = new double[points.Length];
            for (int i = 1; i < points.Length; ++i)
            {
                start[i] = start[i - 1] + Length(points[i] - points[i - 1]);
            }
            double length = start[points.Length - 1];

            // The emission at a point is `emission` times the source there: the source over its integral over the cross
            // section, per unit length of tube
            if (drift == null)
            {
                emission = power / (ProfileIntegral * radius * radius);
                largestSource = 1;
            }
            else
            {
                drift.radius = radius;
                drift.length = length;
                double volume = drift.VolumeIntegral(out largestSource);
                if (!(volume > 0) || double.IsInfinity(volume) || double.IsNaN(volume))
                {
                    ErrorExit("\"glowdischarge\": the drift model gives no finite emission (the ionization against the recombination).");
                }
                emission = power * length / volume;
            }
            // (the constant spectrum that has the luminance of the lines: what the light samplers weigh this light by)
            equivalent = emission * spectrum.LuminousEfficacy() / 683.002 / CIE_Y_integral;

            bounds = new Bounds3f();
            foreach (Point3f p in points)
            {
                bounds = pbrt.Union(bounds, p);
            }
            bounds = pbrt.Expand(bounds, radius * 1.001f);

            // The grid of majorants: one where the tube may be, zero elsewhere, with the segments that may be nearest
            Vector3f extent = bounds.Diagonal();
            Point3i res = new Point3i(GridCells(extent.x), GridCells(extent.y), GridCells(extent.z));
            majorantGrid = new MajorantGrid(bounds, res, alloc);
            candidates = new int[res.x * res.y * res.z][];
            List<int> near = new List<int>();
            for (int z = 0; z < res.z; ++z)
            {
                for (int y = 0; y < res.y; ++y)
                {
                    for (int x = 0; x < res.x; ++x)
                    {
                        Bounds3f unit = majorantGrid.VoxelBounds(x, y, z);
                        Point3f low = bounds.Lerp(unit.pMin), high = bounds.Lerp(unit.pMax);
                        Point3f middle = low + 0.5f * (high - low);
                        Float reach = 0.5f * Length(high - low);
                        // (a segment can be the nearest one to a point of the voxel only if it is within the nearest
                        // distance at the middle plus the size of the voxel)
                        Float nearest = Infinity;
                        for (int s = 0; s + 1 < points.Length; ++s)
                        {
                            nearest = std.min(nearest, SegmentDistance(middle, s, out Float along));
                        }
                        near.Clear();
                        if (nearest - reach < radius)
                        {
                            for (int s = 0; s + 1 < points.Length; ++s)
                            {
                                if (SegmentDistance(middle, s, out Float along) <= nearest + 2 * reach)
                                {
                                    near.Add(s);
                                }
                            }
                        }
                        candidates[x + res.x * (y + res.y * z)] = near.Count > 0 ? near.ToArray() : null;
                        majorantGrid.Set(x, y, z, near.Count > 0 ? 1 : 0);
                    }
                }
            }
        }

        private int GridCells(Float extent)
        {
            return Math.Clamp((int)Math.Ceiling(extent / (2 * radius)), 1, 96);
        }

        // The distance of a point to a segment of the centre line; `along` is where the foot of the perpendicular falls,
        // 0 to 1 on the segment and outside beyond its ends
        private Float SegmentDistance(Point3f p, int segment, out Float along)
        {
            Point3f a = points[segment];
            Vector3f d = points[segment + 1] - a;
            Float lengthSquared = LengthSquared(d);
            along = lengthSquared > 0 ? Dot(p - a, d) / lengthSquared : 0;
            Float clamped = Clamp(along, 0, 1);
            return Length(p - (a + clamped * d));
        }

        // J0 by its power series, for arguments up to its first zero
        public static double BesselJ0(double x)
        {
            double quarter = 0.25 * x * x;
            double term = 1, sum = 1;
            for (int k = 1; k < 40; ++k)
            {
                term *= -quarter / ((double)k * k);
                sum += term;
                if (Math.Abs(term) < 1e-17)
                {
                    break;
                }
            }
            return sum;
        }

        // The coordinates of a point of the space of the medium in the tube: the length along the centre line and the
        // distance from it; false outside the gas (beyond the wall, or beyond the two flat ends)
        public bool TubeCoordinates(Point3f p, out double along, out double distance)
        {
            along = 0;
            distance = 0;
            Vector3f offset = bounds.Offset(p);
            if (offset.x < 0 || offset.x > 1 || offset.y < 0 || offset.y > 1 || offset.z < 0 || offset.z > 1)
            {
                return false;
            }
            Point3i res = majorantGrid.res;
            int x = std.min((int)(offset.x * res.x), res.x - 1), y = std.min((int)(offset.y * res.y), res.y - 1), z = std.min((int)(offset.z * res.z), res.z - 1);
            int[] near = candidates[x + res.x * (y + res.y * z)];
            if (near == null)
            {
                return false;
            }
            Float nearest = Infinity, nearestAlong = 0;
            int nearestSegment = -1;
            foreach (int s in near)
            {
                Float d = SegmentDistance(p, s, out Float t);
                if (d < nearest)
                {
                    nearest = d;
                    nearestAlong = t;
                    nearestSegment = s;
                }
            }
            if (!(nearest < radius))
            {
                return false;
            }
            if ((nearestSegment == 0 && nearestAlong < 0) || (nearestSegment == points.Length - 2 && nearestAlong > 1))
            {
                return false;
            }
            along = start[nearestSegment] + Clamp(nearestAlong, 0, 1) * (start[nearestSegment + 1] - start[nearestSegment]);
            distance = nearest;
            return true;
        }

        // The source of the emission at tube coordinates. The positive column: the density of the electrons, Schottky's
        // J0(2.405 r / R). The drift model: the speed of the drift times the density of the electrons.
        public double Source(double along, double distance)
        {
            if (drift == null)
            {
                return Math.Max(0.0, BesselJ0(BesselZero * distance / radius));
            }
            return drift.Emission(along, distance);
        }

        // The source at a point of the space of the medium (zero outside the gas)
        public double Profile(Point3f p)
        {
            double along, distance;
            if (!TubeCoordinates(p, out along, out distance))
            {
                return 0;
            }
            return Source(along, distance);
        }

        // Medium Interface
        public override bool IsEmissive() { return true; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            SampledSpectrum Le = new SampledSpectrum(0f);
            double along, distance;
            if (TubeCoordinates(renderFromMedium.ApplyInverse(p), out along, out distance))
            {
                double source = Source(along, distance);
                if (source > 0)
                {
                    // (sigma_a Le is the emission coefficient)
                    SampledSpectrum lines;
                    if (speedTemperature)
                    {
                        // (the paper: the temperature of the Boltzmann factors goes as the square of the speed of the drift)
                        double ratio = drift.Speed(along, distance) / drift.speed;
                        lines = spectrum.Sample(lambda, ratio * ratio);
                    }
                    else
                    {
                        lines = spectrum.Sample(lambda);
                    }
                    Le = lines * (Float)(emission * source / absorption);
                }
            }
            return new MediumProperties { sigma_a = new SampledSpectrum(absorption), sigma_s = new SampledSpectrum(0f), phase = phase, Le = Le };
        }

        public DDAMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            // Transform ray to medium's space and compute bounds overlap
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
                return new DDAMajorantIterator();
            return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, new SampledSpectrum(majorant));
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(SampleRay(ray, tMax, lambda));
        }

        // What a volume aggregate asks (Medium.AggregateRegion, Medium.AggregateExtinction)
        public override bool AggregateRegion(out Bounds3f mediumBounds, out Transform renderFromMedium, out MajorantGrid grid)
        {
            mediumBounds = bounds;
            renderFromMedium = this.renderFromMedium;
            grid = majorantGrid;
            return true;
        }

        public override SampledSpectrum AggregateExtinction(SampledWavelengths lambda)
        {
            return new SampledSpectrum(majorant);
        }

        public override SampledSpectrum EmissionForPower(Point3f p, SampledWavelengths lambda, Float time)
        {
            return new SampledSpectrum((Float)(equivalent * Profile(renderFromMedium.ApplyInverse(p))));
        }

        // What the light of the emission asks: about how much a box of the unit cube of the bounds emits, as the constant
        // spectrum of the same luminance. It is the density of the light, positive wherever the box may hold gas.
        public override Float EmissionBoundUnit(Bounds3f unitBox)
        {
            Point3f low = bounds.Lerp(unitBox.pMin), high = bounds.Lerp(unitBox.pMax);
            Point3f middle = low + 0.5f * (high - low);
            Float reach = 0.5f * Length(high - low);
            Float nearest = Infinity, nearestAlong = 0;
            int nearestSegment = 0;
            for (int s = 0; s + 1 < points.Length; ++s)
            {
                Float d = SegmentDistance(middle, s, out Float t);
                if (d < nearest)
                {
                    nearest = d;
                    nearestAlong = t;
                    nearestSegment = s;
                }
            }
            Float least = std.max(0, nearest - reach);
            if (!(least < radius))
            {
                return 0;
            }
            if (drift == null)
            {
                return (Float)(equivalent * BesselJ0(BesselZero * least / radius));
            }
            // (the drift model: the emission at the distances from the centre line that the box reaches, at the length of
            // its middle, and never less than a hundredth of the largest emission of the tube)
            double along = start[nearestSegment] + Clamp(nearestAlong, 0, 1) * (start[nearestSegment + 1] - start[nearestSegment]);
            double most = 0.01 * largestSource;
            Float greatest = std.min(nearest + reach, radius);
            for (int i = 0; i <= 4; ++i)
            {
                most = Math.Max(most, drift.Emission(along, least + (greatest - least) * i / 4.0));
            }
            return (Float)(equivalent * most);
        }

        // The radiance that the positive column adds to a ray that crosses the centre line at a right angle, per unit of
        // the line spectrum (for tests: the integral of the emission over a diameter)
        public double DiameterEmission()
        {
            double sum = 0;
            int steps = 20000;
            for (int i = 0; i < steps; ++i)
            {
                sum += BesselJ0(BesselZero * (i + 0.5) / steps);
            }
            return emission * 2 * radius * sum / steps;
        }

        // The emission coefficient per unit of the source and of the line spectrum
        public double EmissionScale() { return emission; }

        public LineSpectrum Lines() { return spectrum; }

        // Catmull-Rom curve through the points, `subdivide` segments to a span (zero: the polyline of the points)
        public static Point3f[] CentreLine(List<Point3f> given, int subdivide)
        {
            if (subdivide <= 1 || given.Count < 3)
            {
                return given.ToArray();
            }
            List<Point3f> curve = new List<Point3f>();
            for (int span = 0; span + 1 < given.Count; ++span)
            {
                Point3f p1 = given[span], p2 = given[span + 1];
                Point3f p0 = span > 0 ? given[span - 1] : p1 + (p1 - p2);
                Point3f p3 = span + 2 < given.Count ? given[span + 2] : p2 + (p2 - p1);
                for (int step = 0; step < subdivide; ++step)
                {
                    Float t = (Float)step / subdivide;
                    Float t2 = t * t, t3 = t2 * t;
                    Float w0 = 0.5f * (-t3 + 2 * t2 - t), w1 = 0.5f * (3 * t3 - 5 * t2 + 2), w2 = 0.5f * (-3 * t3 + 4 * t2 + t), w3 = 0.5f * (t3 - t2);
                    curve.Add(new Point3f(w0 * p0.x + w1 * p1.x + w2 * p2.x + w3 * p3.x, w0 * p0.y + w1 * p1.y + w2 * p2.y + w3 * p3.y,
                                          w0 * p0.z + w1 * p1.z + w2 * p2.z + w3 * p3.z));
                }
            }
            curve.Add(given[given.Count - 1]);
            return curve.ToArray();
        }

        public static GlowDischargeMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc, Allocator alloc)
        {
            List<Point3f> given = parameters.GetPoint3fArray("P");
            if (given.Count < 2)
            {
                ErrorExit(loc, "\"glowdischarge\" needs the centre line of the tube: \"point3 P\" with two points or more.");
            }
            Float radius = parameters.GetOneFloat("radius", 0.01f);
            Float metersPerUnit = parameters.GetOneFloat("metersperunit", 1f);
            Float controlSamples = parameters.GetOneFloat("controlsamples", 8f);
            Float gasTemperature = parameters.GetOneFloat("gastemperature", 300f);
            Float excitationTemperature = parameters.GetOneFloat("excitationtemperature", 11604.5f);
            Float fraction = parameters.GetOneFloat("wavelengthfraction", 0.5f);
            if (!(radius > 0) || !(metersPerUnit > 0) || !(controlSamples > 0) || !(gasTemperature > 0) || !(excitationTemperature > 0) ||
                !(fraction >= 0) || !(fraction < 1))
            {
                ErrorExit(loc, "\"glowdischarge\": \"radius\", \"metersperunit\", \"controlsamples\" and the temperatures must be positive and " +
                               "\"wavelengthfraction\" 0 to 1.");
            }

            LineSpectrum spectrum;
            List<Float> lines = parameters.GetFloatArray("lines");
            if (lines.Count > 0)
            {
                if (lines.Count % 2 != 0)
                {
                    ErrorExit(loc, "\"float lines\" is pairs of a wavelength (nm) and a strength.");
                }
                Float mass = parameters.GetOneFloat("atomicmass", 0f);
                if (!(mass > 0))
                {
                    ErrorExit(loc, "\"float lines\" needs \"float atomicmass\" (the width of the lines).");
                }
                List<double> centres = new List<double>();
                List<double> strengths = new List<double>();
                for (int i = 0; i < lines.Count; i += 2)
                {
                    centres.Add(lines[i]);
                    strengths.Add(lines[i + 1]);
                }
                spectrum = new LineSpectrum(centres, strengths, mass, gasTemperature);
            }
            else
            {
                string gas = parameters.GetOneString("gas", "neon");
                SpectralLine[] table;
                double mass;
                if (!SpectralLineData.Find(gas, out table, out mass))
                {
                    ErrorExit(loc, $"{gas}: gas unknown (neon, argon, helium, mercury, hydrogen, sodium, or \"float lines\").");
                }
                if (gas == "argon")
                {
                    Warning(loc, "\"glowdischarge\": the lines of argon here are its strongest red lines and two of the blue ones: its colour is not to be trusted (give \"float lines\").");
                }
                spectrum = new LineSpectrum(table, mass, gasTemperature, excitationTemperature);
            }
            if (spectrum.Count == 0)
            {
                ErrorExit(loc, "\"glowdischarge\": no line between 360 and 830 nm.");
            }

            // The radiant power of a metre of tube, in watts
            double power = parameters.GetOneFloat("radiantpower", 1f);
            Float flux = parameters.GetOneFloat("luminousflux", 0f);
            if (flux > 0)
            {
                double efficacy = spectrum.LuminousEfficacy();
                if (!(efficacy > 0))
                {
                    ErrorExit(loc, "\"luminousflux\": the lines of this spectrum give no lumens.");
                }
                power = flux / efficacy;
            }
            power *= parameters.GetOneFloat("scale", 1f);
            if (!(power >= 0))
            {
                ErrorExit(loc, "\"glowdischarge\": the power must not be negative.");
            }

            // The source of the emission: the positive column, or the paper's model of the densities along a drift
            DriftModel drift = null;
            string model = parameters.GetOneString("model", "column");
            if (model == "drift")
            {
                drift = new DriftModel();
                string flow = parameters.GetOneString("flow", "laminar");
                if (flow != "laminar" && flow != "extruding")
                {
                    ErrorExit(loc, $"{flow}: flow unknown (laminar, extruding).");
                }
                drift.laminar = flow == "laminar";
                drift.ionization = parameters.GetOneFloat("ionization", 100f);
                drift.recombination = parameters.GetOneFloat("recombination", 1e-8f);
                drift.attachment = parameters.GetOneFloat("attachment", 0.25f);
                drift.ionScale = parameters.GetOneFloat("ionscale", 10f);
                drift.speed = parameters.GetOneFloat("driftspeed", 1f);
                drift.decay = parameters.GetOneFloat("driftdecay", 0f);
                drift.falloff = parameters.GetOneFloat("driftfalloff", 1f);
                drift.tolerance = parameters.GetOneFloat("tolerance", 1e-9f);
                if (!(drift.ionization >= 0) || !(drift.recombination >= 0) || !(drift.attachment >= 0) || !(drift.ionScale > 0) ||
                    !(drift.speed > 0) || !(drift.decay >= 0) || !(drift.falloff >= 0) || !(drift.tolerance > 0))
                {
                    ErrorExit(loc, "\"glowdischarge\": the coefficients of the drift model must not be negative, and \"ionscale\", \"driftspeed\" and \"tolerance\" positive.");
                }
            }
            else if (model != "column")
            {
                ErrorExit(loc, $"{model}: model unknown (column, drift).");
            }

            Point3f[] points = CentreLine(given, parameters.GetOneInt("subdivide", 0));
            // (683 P / (4 pi), W sr^-1 per metre of tube as radiance in the units of the thermal emitters, over the area in
            // square metres that a square scene unit is, times the metres of a scene unit of path)
            GlowDischargeMedium medium = new GlowDischargeMedium(points, radius, spectrum, 683.002 * power / (4 * Math.PI * metersPerUnit),
                                                                 controlSamples, renderFromMedium, alloc, drift,
                                                                 parameters.GetOneBool("speedtemperature", false));
            SpectralLineSampling.Add(spectrum, fraction);
            if (parameters.GetOneBool("emission_nee", true) && power > 0)
            {
                medium.EnableEmissionSampling(parameters.GetOneInt("emission_nee_res", 32), parameters.GetOneBool("emission_fnee", false));
            }
            return medium;
        }

        // GlowDischargeMedium Private Members
        private Point3f[] points;
        private double[] start;
        private Float radius, majorant, absorption;
        private LineSpectrum spectrum;
        private DriftModel drift;
        private bool speedTemperature;
        private double emission, equivalent, largestSource;
        private Bounds3f bounds;
        private Transform renderFromMedium;
        private MajorantGrid majorantGrid;
        private int[][] candidates;
        private HGPhaseFunction phase;
    }
}
