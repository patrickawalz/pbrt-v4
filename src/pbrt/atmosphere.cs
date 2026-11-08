// (not in pbrt-v4) A volumetric model of the Earth's atmosphere: a spherical shell of Rayleigh-scattering air,
// aerosols and ozone around a planet, rendered with the ordinary null-scattering volume integrators (no fitted data).
//
//   MakeNamedMedium "sky" "string type" "atmosphere" "float visibility" 50 ...
//   Shape "planet" "float radius" 6360000 "point3 center" [0 -6360000 0]
//
// Units: the parameters are in metres. "float scale" is the number of metres per scene unit (default 1); the geometry of the
// medium (the centre, the shells) is in scene units, the physics (profiles, cross sections) in metres.
//
// Data: libRadtran 2.0.6 (www.libradtran.org), turned into tables by tools/gen_atmosphere_tables.py (atmosphere_data.cs):
//   * Air number density: the U.S. Standard Atmosphere 1976 layers (computed here), Rayleigh cross section of Bodhaine et al. 1999
//   * Ozone: the U.S. standard atmosphere profile (AFGL-TR-86-0110) and the Serdyuchenko-Gorshelev cross sections at 11 temperatures (193 to 293 K,
//     IUP Bremen), interpolated at the temperature of the altitude (U.S. standard atmosphere)
//   * NO2 (Bremen polynomial in temperature, afglus profile) and the O2-O2 collision pair (Greenblatt) as absorbers
//   * Aerosols: the OPAC species inso, waso (50 % relative humidity) and soot with the standard mass profiles of libRadtran, as spheres; their
//     optical properties and their full polarised scattering matrices (P11, P12, P33, P34) are computed with the Mie solver of the engine (mie.cs)
//     from the OPAC refractive indices and log-normal size distributions
//   * The amount of aerosol is set by the horizontal visibility at the ground (Koschmieder), the shape of the profile follows it
//   * The phase function is the Rayleigh phase function (with the depolarisation of the King factor) mixed with the tabulated Mie phase
//     functions of the species, exact for every wavelength (the hero wavelength picks the direction, PhaseFunction.pSpectral gives the value)
//   * Not modelled: H2O and O2 line absorption, refraction, clouds, nonspherical aerosols, ground albedo parameter

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace pbrt
{

    // AtmosphereProfile Definition
    //
    // The air of the atmosphere, in double precision; altitudes in metres.
    public sealed class AtmosphereProfile
    {
        // The layers of the U.S. Standard Atmosphere 1976: geopotential base altitude (km), temperature (K), lapse rate (K/km)
        static readonly double[] layerBase = { 0, 11, 20, 32, 47, 51, 71, 84.852 };
        static readonly double[] layerTemperature = { 288.15, 216.65, 216.65, 228.65, 270.65, 270.65, 214.65, 186.946 };
        static readonly double[] layerLapse = { -6.5, 0, 1.0, 2.8, 0, -2.8, -2.0, 0 };
        const double SeaLevelPressure = 101325.0;
        const double Boltzmann = 1.380649e-23;
        // g0 M / R* in K/km
        const double GasConstantTerm = 34.1632;
        const double EarthRadiusForGeopotential = 6356766.0;

        public AtmosphereProfile(double topAltitude)
        {
            layerPressure = new double[layerBase.Length];
            layerPressure[0] = SeaLevelPressure;
            for (int i = 1; i < layerBase.Length; ++i)
            {
                layerPressure[i] = PressureInLayer(i - 1, layerBase[i]);
            }

            // Tabulate the air number density relative to sea level every 25 m
            densityStep = 25.0;
            int count = (int)Math.Ceiling(topAltitude / densityStep) + 2;
            relativeDensity = new double[count];
            seaLevelNumberDensity = NumberDensityExact(0);
            for (int i = 0; i < count; ++i)
            {
                relativeDensity[i] = NumberDensityExact(i * densityStep) / seaLevelNumberDensity;
            }
        }

        // The pressure in Pa at the geopotential altitude hKm (km) in layer i
        double PressureInLayer(int i, double hKm)
        {
            double dh = hKm - layerBase[i];
            if (layerLapse[i] == 0)
            {
                return layerPressure[i] * Math.Exp(-GasConstantTerm * dh / layerTemperature[i]);
            }
            double t = layerTemperature[i] + layerLapse[i] * dh;
            return layerPressure[i] * Math.Pow(layerTemperature[i] / t, GasConstantTerm / layerLapse[i]);
        }

        // The number density of air in 1/m^3 at the geometric altitude h (m)
        public double NumberDensityExact(double h)
        {
            double geopotentialKm = EarthRadiusForGeopotential * h / (EarthRadiusForGeopotential + h) / 1000.0;
            int layer = layerBase.Length - 1;
            for (int i = 0; i < layerBase.Length - 1; ++i)
            {
                if (geopotentialKm < layerBase[i + 1])
                {
                    layer = i;
                    break;
                }
            }
            double temperature = layerTemperature[layer] + layerLapse[layer] * (geopotentialKm - layerBase[layer]);
            return PressureInLayer(layer, geopotentialKm) / (Boltzmann * temperature);
        }

        // The air number density relative to sea level at altitude h (m), from the table
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double RelativeDensity(double h)
        {
            double x = h / densityStep;
            int i = (int)x;
            if (i < 0)
            {
                return relativeDensity[0];
            }
            if (i >= relativeDensity.Length - 1)
            {
                return relativeDensity[relativeDensity.Length - 1];
            }
            double f = x - i;
            return relativeDensity[i] * (1 - f) + relativeDensity[i + 1] * f;
        }

        public double SeaLevelNumberDensity()
        {
            return seaLevelNumberDensity;
        }

        // The King correction factor of the air at the wavelength lambdaNm: the mixture of the factors of N2, O2, Ar and 300 ppm CO2 (Bodhaine et al. 1999)
        public static double KingFactor(double lambdaNm)
        {
            double lambdaMicrons = lambdaNm * 1e-3;
            double inverseSquared = 1.0 / (lambdaMicrons * lambdaMicrons);
            double fN2 = 1.034 + 3.17e-4 * inverseSquared;
            double fO2 = 1.096 + 1.385e-3 * inverseSquared + 1.448e-4 * inverseSquared * inverseSquared;
            double co2Percent = 0.03;
            return (78.084 * fN2 + 20.946 * fO2 + 0.934 * 1.0 + co2Percent * 1.15) / (78.084 + 20.946 + 0.934 + co2Percent);
        }

        // The share of the Rayleigh phase function of the air that scatters all polarisations the same: the phase function with depolarisation
        // gamma is (1 - e) 3/(16 pi) (1 + cos^2) + e / (4 pi) with e = 3 gamma / (1 + 2 gamma); gamma follows from the King factor
        // F = (6 + 3 rho) / (6 - 7 rho) (rho the depolarisation ratio) as gamma = rho / (2 - rho)
        public static double RayleighDepolarisedFraction(double lambdaNm)
        {
            double king = KingFactor(lambdaNm);
            double rho = 6.0 * (king - 1.0) / (3.0 + 7.0 * king);
            double gamma = rho / (2.0 - rho);
            return 3.0 * gamma / (1.0 + 2.0 * gamma);
        }

        // The Rayleigh scattering cross section of an air molecule in m^2 at the wavelength lambdaNm (Bodhaine et al. 1999, 300 ppm CO2)
        public static double RayleighCrossSection(double lambdaNm)
        {
            double lambdaMicrons = lambdaNm * 1e-3;
            double inverseSquared = 1.0 / (lambdaMicrons * lambdaMicrons);
            double refractivity = (8060.51 + 2480990.0 / (132.274 - inverseSquared) + 17455.7 / (39.32957 - inverseSquared)) * 1e-8;
            double n = 1.0 + refractivity;
            double king = KingFactor(lambdaNm);
            double lambdaMetres = lambdaNm * 1e-9;
            double standardDensity = 2.546899e25;
            double n2 = n * n;
            double numerator = 24.0 * Math.PI * Math.PI * Math.PI * (n2 - 1) * (n2 - 1);
            double denominator = Math.Pow(lambdaMetres, 4) * standardDensity * standardDensity * (n2 + 2) * (n2 + 2);
            return numerator / denominator * king;
        }

        // The value at x of a profile given at ascending positions, linear between the nodes and constant outside
        public static double PiecewiseLinear(double[] positions, double[] values, double x)
        {
            if (x <= positions[0])
            {
                return values[0];
            }
            int last = positions.Length - 1;
            if (x >= positions[last])
            {
                return values[last];
            }
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (positions[mid] <= x)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            double f = (x - positions[lo]) / (positions[hi] - positions[lo]);
            return values[lo] * (1 - f) + values[hi] * f;
        }

        // The value at x of a table of equally spaced samples (start, step), constant outside
        public static double Linear(double start, double step, double[] values, double x)
        {
            double t = (x - start) / step;
            if (t <= 0)
            {
                return values[0];
            }
            int i = (int)t;
            if (i >= values.Length - 1)
            {
                return values[values.Length - 1];
            }
            double f = t - i;
            return values[i] * (1 - f) + values[i + 1] * f;
        }

        // The value at x of a profile given at ascending positions: linear in the logarithm between two positive values, linear otherwise,
        // constant outside
        public static double LogLinear(double[] positions, double[] values, double x)
        {
            if (x <= positions[0])
            {
                return values[0];
            }
            int last = positions.Length - 1;
            if (x >= positions[last])
            {
                return values[last];
            }
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (positions[mid] <= x)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            double f = (x - positions[lo]) / (positions[hi] - positions[lo]);
            if (values[lo] > 0 && values[hi] > 0)
            {
                return Math.Exp(Math.Log(values[lo]) * (1 - f) + Math.Log(values[hi]) * f);
            }
            return values[lo] * (1 - f) + values[hi] * f;
        }

        readonly double[] layerPressure;
        readonly double densityStep;
        readonly double[] relativeDensity;
        readonly double seaLevelNumberDensity;
    }

    // AtmospherePhase Definition
    //
    // The phase function of a point of the atmosphere: air (the Rayleigh phase function with the depolarisation of the King factor) and the
    // aerosol species with their Mie phase functions, mixed with the shares of each wavelength. The direction is sampled from the mixture of the
    // hero wavelength (Rayleigh or a species, then a node of the wavelength table of the species, then the angle from the tabulated cumulative
    // distribution); the density of that sampling is exactly p(). pSpectral() is the exact mixture of each wavelength.
    public sealed unsafe class AtmospherePhase : PhaseFunction
    {
        public AtmospherePhase(MieSpecies[] species, SampledWavelengths lambda, SampledSpectrum rayleighShare, SampledSpectrum depolarised,
                               SampledSpectrum[] speciesWeights)
        {
            this.species = species;
            this.rayleighShare = rayleighShare;
            this.depolarised = depolarised;
            this.speciesWeights = speciesWeights;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                species[0].Locate(lambda[i], out nodes[i], out nodeFractions[i]);
            }
        }

        public override string ToString()
        {
            return StringPrintf("[ AtmospherePhase rayleighShare: %s depolarised: %s ]", rayleighShare, depolarised);
        }

        // The Rayleigh share of the scattering and the depolarised share of the Rayleigh phase function at each wavelength
        public SampledSpectrum RayleighShare() { return rayleighShare; }

        public SampledSpectrum Depolarised() { return depolarised; }

        // The share of each aerosol species in the aerosol scattering at each wavelength
        public SampledSpectrum[] SpeciesWeights() { return speciesWeights; }

        public override bool WavelengthDependent() { return true; }

        // The cosine of the scattering angle (the angle between the direction of travel before and after the scattering) from pbrt's wo and wi
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double CosineOfScattering(Vector3f wo, Vector3f wi)
        {
            return Math.Clamp(-(double)Dot(wo, wi), -1.0, 1.0);
        }

        // The phase function (per steradian) of the wavelength i at the scattering angle theta, with its cell in the angle tables
        double Value(int i, double cosineScatter, int cell, double cellFraction)
        {
            double aerosol = 0.0;
            if (rayleighShare[i] < 1.0f)
            {
                for (int s = 0; s < species.Length; ++s)
                {
                    double weight = speciesWeights[s][i];
                    if (weight > 0.0)
                    {
                        aerosol += weight * MieSpecies.Lookup(species[s].P11, nodes[i], nodeFractions[i], cell, cellFraction);
                    }
                }
            }
            double e = depolarised[i];
            double air = (1.0 - e) * 3.0 / (16.0 * Math.PI) * (1.0 + cosineScatter * cosineScatter) + e / (4.0 * Math.PI);
            return (1.0 - rayleighShare[i]) * aerosol + rayleighShare[i] * air;
        }

        public override Float p(Vector3f wo, Vector3f wi)
        {
            double cosineScatter = CosineOfScattering(wo, wi);
            double fraction;
            int cell = species[0].FindCell(Math.Acos(cosineScatter), out fraction);
            return (Float)Value(0, cosineScatter, cell, fraction);
        }

        public override Float PDF(Vector3f wo, Vector3f wi) { return p(wo, wi); }

        public override SampledSpectrum pSpectral(Vector3f wo, Vector3f wi)
        {
            double cosineScatter = CosineOfScattering(wo, wi);
            double fraction;
            int cell = species[0].FindCell(Math.Acos(cosineScatter), out fraction);
            SampledSpectrum result = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                result[i] = (Float)Value(i, cosineScatter, cell, fraction);
            }
            return result;
        }

        public override pstd.optional<PhaseFunctionSample> Sample_p(Vector3f wo, Point2f u)
        {
            double r = rayleighShare[0];
            double cosineScatter;
            if (u.x < r)
            {
                // Air: the isotropic (depolarised) part or the Rayleigh part
                double t = u.x / r;
                double e = depolarised[0];
                if (t < e)
                {
                    cosineScatter = 1.0 - 2.0 * (t / e);
                }
                else
                {
                    double t2 = (t - e) / (1.0 - e);
                    double a = Math.Pow(Math.Sqrt(16.0 * t2 * (t2 - 1.0) + 5.0) + 4.0 * t2 - 2.0, 1.0 / 3.0);
                    cosineScatter = Math.Clamp(-(a - 1.0 / a), -1.0, 1.0);
                }
            }
            else
            {
                // An aerosol species, then a node of the wavelength table, then the angle
                double remaining = (u.x - r) / (1.0 - r);
                int chosen = species.Length - 1;
                for (int s = 0; s < species.Length; ++s)
                {
                    double weight = speciesWeights[s][0];
                    if (s == species.Length - 1 || remaining < weight)
                    {
                        chosen = s;
                        remaining = weight > 0.0 ? Math.Min(remaining / weight, 0.99999999) : 0.0;
                        break;
                    }
                    remaining -= weight;
                }
                int node = nodes[0];
                double upper = nodeFractions[0];
                if (remaining < 1.0 - upper)
                {
                    remaining = remaining / (1.0 - upper);
                }
                else
                {
                    remaining = Math.Min((remaining - (1.0 - upper)) / upper, 0.99999999);
                    node = node + 1;
                }
                double pdfIgnored;
                double theta = species[chosen].SampleAngle(node, Math.Max(remaining, 0.0), out pdfIgnored);
                cosineScatter = Math.Cos(theta);
            }

            double sinTheta = Math.Sqrt(Math.Max(1.0 - cosineScatter * cosineScatter, 0.0));
            double phi = 2.0 * Math.PI * u.y;
            // pbrt's cosine to wo is the negative of the cosine of the scattering angle
            Vector3f wi = Frame.FromZ(wo).FromLocal(new Vector3f((Float)(Math.Cos(phi) * sinTheta), (Float)(Math.Sin(phi) * sinTheta),
                                                                 (Float)(-cosineScatter)));
            double fraction;
            int cell = species[0].FindCell(Math.Acos(cosineScatter), out fraction);
            Float pdf = (Float)Value(0, cosineScatter, cell, fraction);
            PhaseFunctionSample sample = new PhaseFunctionSample();
            sample.p = pdf;
            sample.wi = wi;
            sample.pdf = pdf;
            return sample;
        }

        // The ratios S12 / S11, S33 / S11 and S34 / S11 of the Mueller matrix of the mixture at each wavelength, for a scattering angle with the
        // given cosine, in the convention of MieSolver (Q = I_parallel - I_perpendicular to the scattering plane)
        public void MuellerRatios(double cosineScatter, out SampledSpectrum s12, out SampledSpectrum s33, out SampledSpectrum s34)
        {
            double fraction;
            int cell = species[0].FindCell(Math.Acos(cosineScatter), out fraction);
            s12 = new SampledSpectrum(0f);
            s33 = new SampledSpectrum(0f);
            s34 = new SampledSpectrum(0f);
            double sin2 = 1.0 - cosineScatter * cosineScatter;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double e = depolarised[i];
                double k = (1.0 - e) * 3.0 / (16.0 * Math.PI);
                double r = rayleighShare[i];
                double t11 = r * (k * (1.0 + cosineScatter * cosineScatter) + e / (4.0 * Math.PI));
                double t12 = r * (-k * sin2);
                double t33 = r * (k * 2.0 * cosineScatter);
                double t34 = 0.0;
                if (r < 1.0f)
                {
                    for (int s = 0; s < species.Length; ++s)
                    {
                        double weight = (1.0 - r) * speciesWeights[s][i];
                        if (weight > 0.0)
                        {
                            t11 += weight * MieSpecies.Lookup(species[s].P11, nodes[i], nodeFractions[i], cell, fraction);
                            t12 += weight * MieSpecies.Lookup(species[s].P12, nodes[i], nodeFractions[i], cell, fraction);
                            t33 += weight * MieSpecies.Lookup(species[s].P33, nodes[i], nodeFractions[i], cell, fraction);
                            t34 += weight * MieSpecies.Lookup(species[s].P34, nodes[i], nodeFractions[i], cell, fraction);
                        }
                    }
                }
                if (t11 > 0.0)
                {
                    s12[i] = (Float)(t12 / t11);
                    s33[i] = (Float)(t33 / t11);
                    s34[i] = (Float)(t34 / t11);
                }
            }
        }

        readonly MieSpecies[] species;
        readonly SampledSpectrum rayleighShare, depolarised;
        readonly SampledSpectrum[] speciesWeights;
        readonly int[] nodes = new int[NSpectrumSamples];
        readonly double[] nodeFractions = new double[NSpectrumSamples];
    }

    // AtmosphereMajorantIterator Definition
    //
    // The ray cut at the concentric spherical shells of the atmosphere; each piece has the majorant of its shell. All the geometry is
    // computed in double precision relative to the planet centre.
    public sealed unsafe class AtmosphereMajorantIterator : RayMajorantIterator
    {
        // AtmosphereMajorantIterator Public Methods
        public AtmosphereMajorantIterator()
        {
            count = 0;
        }

        public AtmosphereMajorantIterator(AtmosphereMedium medium, Ray ray, Float tMax, SampledWavelengths lambda)
        {
            this.medium = medium;
            this.lambda = lambda;
            this.tMax = tMax;
            radii = medium.ShellRadii();
            shells = radii.Length - 1;
            double ox = ray.o.x - medium.CenterX(), oy = ray.o.y - medium.CenterY(), oz = ray.o.z - medium.CenterZ();
            double dx = ray.d.x, dy = ray.d.y, dz = ray.d.z;
            a = dx * dx + dy * dy + dz * dz;
            double od = ox * dx + oy * dy + oz * dz;
            double oo = ox * ox + oy * oy + oz * oz;
            tc = -od / a;
            b2 = Math.Max(0.0, oo - od * od / a);
            double rTop = radii[shells];
            if (b2 >= rTop * rTop)
            {
                count = 0;
                return;
            }
            double rPlanet = radii[0];
            if (b2 < rPlanet * rPlanet)
            {
                // The line crosses the planet: the way in down to the ground, then (after the planet) the way out
                hitsPlanet = true;
                firstClosest = -1;
                count = 2 * shells;
            }
            else
            {
                int j = 0;
                for (int k = 0; k < shells; ++k)
                {
                    if (radii[k] * radii[k] <= b2)
                    {
                        j = k;
                    }
                }
                firstClosest = j;
                count = 2 * (shells - 1 - j) + 1;
            }
            index = 0;
        }

        // The half length (in ray parameter units) of the chord of the sphere with radius r
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double Chord(double r)
        {
            return Math.Sqrt(Math.Max(0.0, r * r - b2) / a);
        }

        public override pstd.optional<RayMajorantSegment> Next()
        {
            while (index < count)
            {
                int q = index++;
                int annulus;
                double ta, tb;
                int inbound = hitsPlanet ? shells : shells - 1 - firstClosest;
                if (q < inbound)
                {
                    annulus = shells - 1 - q;
                    ta = tc - Chord(radii[annulus + 1]);
                    tb = tc - Chord(radii[annulus]);
                }
                else if (hitsPlanet)
                {
                    annulus = q - shells;
                    ta = tc + Chord(radii[annulus]);
                    tb = tc + Chord(radii[annulus + 1]);
                }
                else if (q == inbound)
                {
                    annulus = firstClosest;
                    ta = tc - Chord(radii[annulus + 1]);
                    tb = tc + Chord(radii[annulus + 1]);
                }
                else
                {
                    annulus = firstClosest + (q - inbound);
                    ta = tc + Chord(radii[annulus]);
                    tb = tc + Chord(radii[annulus + 1]);
                }
                if (ta >= tMax)
                {
                    index = count;
                    break;
                }
                if (tb <= 0)
                {
                    continue;
                }
                double lo = Math.Max(ta, 0.0);
                double hi = Math.Min(tb, (double)tMax);
                if (hi <= lo)
                {
                    continue;
                }
                RayMajorantSegment segment = new RayMajorantSegment();
                segment.tMin = (Float)lo;
                segment.tMax = (Float)hi;
                segment.sigma_maj = medium.ShellMajorant(annulus, lambda);
                return segment;
            }
            return default;
        }

        public override string ToString()
        {
            return StringPrintf("[ AtmosphereMajorantIterator index: %d count: %d ]", index, count);
        }

        AtmosphereMedium medium;
        SampledWavelengths lambda;
        double[] radii;
        Float tMax;
        double a, tc, b2;
        int shells, firstClosest, count, index;
        bool hitsPlanet;
    }

    // AtmosphereMedium Definition
    public sealed unsafe class AtmosphereMedium : Medium, IMajorantMedium<AtmosphereMajorantIterator>
    {
        const int Species = 3;

        // AtmosphereMedium Public Methods
        public AtmosphereMedium(double centerX, double centerY, double centerZ, double planetRadiusMeters, double topAltitudeMeters,
                                double metersPerUnit, double visibilityKm, string aerosolProfile, double ozoneScale, double no2Scale, double o4Scale,
                                Allocator alloc)
        {
            this.no2Scale = no2Scale;
            this.o4Scale = o4Scale;
            species = BuildSpecies();
            cx = centerX;
            cy = centerY;
            cz = centerZ;
            this.metersPerUnit = metersPerUnit;
            this.topAltitudeMeters = topAltitudeMeters;
            this.ozoneScale = ozoneScale;
            profile = new AtmosphereProfile(topAltitudeMeters);

            // The aerosol mass concentration profile (g/m^3) of the three species
            if (aerosolProfile == "continental_clean")
            {
                aerosolAltitude = AtmosphereData.ContinentalCleanAltitude;
                aerosolMass = new double[][] { AtmosphereData.ContinentalCleanInso, AtmosphereData.ContinentalCleanWaso,
                                               AtmosphereData.ContinentalCleanSoot };
            }
            else if (aerosolProfile == "continental_average")
            {
                aerosolAltitude = AtmosphereData.ContinentalAverageAltitude;
                aerosolMass = new double[][] { AtmosphereData.ContinentalAverageInso, AtmosphereData.ContinentalAverageWaso,
                                               AtmosphereData.ContinentalAverageSoot };
            }
            else if (aerosolProfile == "continental_polluted")
            {
                aerosolAltitude = AtmosphereData.ContinentalPollutedAltitude;
                aerosolMass = new double[][] { AtmosphereData.ContinentalPollutedInso, AtmosphereData.ContinentalPollutedWaso,
                                               AtmosphereData.ContinentalPollutedSoot };
            }
            else if (aerosolProfile == "visibility")
            {
                // Replaced below, when the extinction of the species is known, by a blend of the three profiles
                aerosolAltitude = AtmosphereData.ContinentalCleanAltitude;
                aerosolMass = new double[][] { AtmosphereData.ContinentalCleanInso, AtmosphereData.ContinentalCleanWaso,
                                               AtmosphereData.ContinentalCleanSoot };
            }
            else
            {
                ErrorExit(new FileLoc(), "atmosphere: unknown aerosol profile \"" + aerosolProfile +
                                         "\" (visibility, continental_clean, continental_average or continental_polluted)");
                aerosolAltitude = null;
                aerosolMass = null;
            }

            // The spectra, per scene unit where they are coefficients
            double unit = metersPerUnit;
            double seaLevelN = profile.SeaLevelNumberDensity();
            rayleigh = DenselySampledSpectrum.SampleFunction((Float lambdaNm) =>
            {
                return (Float)(seaLevelN * AtmosphereProfile.RayleighCrossSection(lambdaNm) * unit);
            }, (int)Lambda_min, (int)Lambda_max, alloc);
            depolarisedFraction = DenselySampledSpectrum.SampleFunction((Float lambdaNm) =>
            {
                return (Float)AtmosphereProfile.RayleighDepolarisedFraction(lambdaNm);
            }, (int)Lambda_min, (int)Lambda_max, alloc);
            // The ozone cross section at the 11 temperatures of the data
            ozoneCrossSection = new DenselySampledSpectrum[AtmosphereData.OzoneTemperatures.Length];
            for (int t = 0; t < ozoneCrossSection.Length; ++t)
            {
                double[] values = AtmosphereData.OzoneCrossSectionByTemperature[t];
                ozoneCrossSection[t] = DenselySampledSpectrum.SampleFunction((Float lambdaNm) =>
                {
                    return (Float)(AtmosphereProfile.Linear(AtmosphereData.OzoneLambdaStart, AtmosphereData.OzoneLambdaStep, values, lambdaNm) * unit);
                }, (int)Lambda_min, (int)Lambda_max, alloc);
            }
            no2CrossSection = new DenselySampledSpectrum[3];
            double[][] no2Tables = { AtmosphereData.No2C0, AtmosphereData.No2C1, AtmosphereData.No2C2 };
            for (int c = 0; c < 3; ++c)
            {
                double[] values = no2Tables[c];
                no2CrossSection[c] = DenselySampledSpectrum.SampleFunction((Float lambdaNm) =>
                {
                    return (Float)(AtmosphereProfile.Linear(AtmosphereData.OzoneLambdaStart, AtmosphereData.OzoneLambdaStep, values, lambdaNm) * unit);
                }, (int)Lambda_min, (int)Lambda_max, alloc);
            }
            o4CrossSection = DenselySampledSpectrum.SampleFunction((Float lambdaNm) =>
            {
                return (Float)(AtmosphereProfile.Linear(AtmosphereData.OzoneLambdaStart, AtmosphereData.OzoneLambdaStep, AtmosphereData.O4CrossSection,
                                                         lambdaNm) * unit * O4SpectrumScale);
            }, (int)Lambda_min, (int)Lambda_max, alloc);

            // The aerosol optical properties come from the Mie solver of the engine
            double[][] extinctionTable = new double[Species][];
            double[][] albedoTable = new double[Species][];
            massExtinction = new DenselySampledSpectrum[Species];
            albedo = new DenselySampledSpectrum[Species];
            for (int s = 0; s < Species; ++s)
            {
                extinctionTable[s] = species[s].MassExtinction;
                albedoTable[s] = species[s].Albedo;
                double[] extinctionValues = extinctionTable[s], albedoValues = albedoTable[s];
                massExtinction[s] = DenselySampledSpectrum.SampleFunction((Float lambdaNm) =>
                {
                    return (Float)(AtmosphereProfile.Linear(species[0].WavelengthStart, species[0].WavelengthStep, extinctionValues, lambdaNm) * unit);
                }, (int)Lambda_min, (int)Lambda_max, alloc);
                albedo[s] = DenselySampledSpectrum.SampleFunction((Float lambdaNm) =>
                {
                    return (Float)AtmosphereProfile.Linear(species[0].WavelengthStart, species[0].WavelengthStep, albedoValues, lambdaNm);
                }, (int)Lambda_min, (int)Lambda_max, alloc);
            }

            // The amount of aerosol from the visibility: the extinction of the air at the ground at 550 nm is 3.912 / V (Koschmieder); the
            // aerosol has what the air does not
            double rayleigh550 = seaLevelN * AtmosphereProfile.RayleighCrossSection(550.0);
            double target = Math.Max(3.912 / (visibilityKm * 1000.0) - rayleigh550, 0.0);
            if (aerosolProfile == "visibility" && target > 0)
            {
                aerosolMass = BlendProfiles(target, extinctionTable);
            }
            double massExtinction550 = 0;
            for (int s = 0; s < Species; ++s)
            {
                massExtinction550 += aerosolMass[s][0] * AtmosphereProfile.Linear(species[0].WavelengthStart, species[0].WavelengthStep,
                                                                                     extinctionTable[s], 550.0);
            }
            aerosolScale = massExtinction550 > 0 ? target / massExtinction550 : 0;

            // The shells (altitudes in metres): 100 m up to 2 km, 500 m up to 10 km, 1 km up to 30 km, 2.5 km above
            List<double> edges = new List<double>();
            double h = 0;
            while (h < topAltitudeMeters - 1e-6)
            {
                edges.Add(h);
                double step = h < 2000 ? 100 : h < 10000 ? 500 : h < 30000 ? 1000 : 2500;
                h += step;
            }
            edges.Add(topAltitudeMeters);
            int shells = edges.Count - 1;
            shellRadius = new double[shells + 1];
            maxRayleighDensity = new double[shells];
            maxAerosolMass = new double[shells][];
            maxOzoneDensity = new double[shells];
            maxOzoneCrossSection = new DenselySampledSpectrum[shells];
            maxNo2Density = new double[shells];
            maxNo2CrossSection = new DenselySampledSpectrum[shells];
            maxOxygenSquared = new double[shells];
            for (int i = 0; i <= shells; ++i)
            {
                shellRadius[i] = (planetRadiusMeters + edges[i]) / metersPerUnit;
            }
            for (int i = 0; i < shells; ++i)
            {
                double lo = edges[i], hi = edges[i + 1];
                maxRayleighDensity[i] = profile.RelativeDensity(lo);
                // The profiles are interpolated between the nodes of the table (and are not monotone: the soot of the files is zero in the
                // boundary layer): the maximum is at an end of the shell or at a node inside
                maxAerosolMass[i] = new double[Species];
                for (int s = 0; s < Species; ++s)
                {
                    double maxMass = Math.Max(AerosolMass(s, lo), AerosolMass(s, hi));
                    for (int n = 0; n < aerosolAltitude.Length; ++n)
                    {
                        if (aerosolAltitude[n] > lo && aerosolAltitude[n] < hi)
                        {
                            maxMass = Math.Max(maxMass, AerosolMass(s, aerosolAltitude[n]));
                        }
                    }
                    maxAerosolMass[i][s] = maxMass;
                }
                // The ozone density is interpolated between the nodes of the table: its maximum is at an end of the shell or at a node inside
                double maxOzone = Math.Max(OzoneDensity(lo), OzoneDensity(hi));
                for (int n = 0; n < AtmosphereData.OzoneProfileAltitude.Length; ++n)
                {
                    double node = AtmosphereData.OzoneProfileAltitude[n];
                    if (node > lo && node < hi)
                    {
                        maxOzone = Math.Max(maxOzone, OzoneDensity(node));
                    }
                }
                maxOzoneDensity[i] = maxOzone;

                // The temperature range of the shell (linear between the nodes of the table: the ends of the shell and the nodes inside)
                double temperatureLow = Math.Min(Temperature(lo), Temperature(hi)), temperatureHigh = Math.Max(Temperature(lo), Temperature(hi));
                double maxNo2 = Math.Max(No2Density(lo), No2Density(hi));
                double maxOxygen = Math.Max(OxygenDensity(lo), OxygenDensity(hi));
                for (int n = 0; n < AtmosphereData.AirProfileAltitude.Length; ++n)
                {
                    double node = AtmosphereData.AirProfileAltitude[n];
                    if (node > lo && node < hi)
                    {
                        temperatureLow = Math.Min(temperatureLow, AtmosphereData.AirProfileTemperature[n]);
                        temperatureHigh = Math.Max(temperatureHigh, AtmosphereData.AirProfileTemperature[n]);
                        maxNo2 = Math.Max(maxNo2, No2Density(node));
                        maxOxygen = Math.Max(maxOxygen, OxygenDensity(node));
                    }
                }
                maxNo2Density[i] = maxNo2;
                maxOxygenSquared[i] = maxOxygen * maxOxygen;

                // Ozone: the maximum over the tables whose temperatures bracket the range
                double indexLow = Math.Clamp((temperatureLow - AtmosphereData.OzoneTemperatures[0]) / 10.0, 0.0, ozoneCrossSection.Length - 1.0);
                double indexHigh = Math.Clamp((temperatureHigh - AtmosphereData.OzoneTemperatures[0]) / 10.0, 0.0, ozoneCrossSection.Length - 1.0);
                int firstTable = (int)Math.Floor(indexLow), lastTable = (int)Math.Ceiling(indexHigh);
                maxOzoneCrossSection[i] = DenselySampledSpectrum.SampleFunction((Float lambdaNm) =>
                {
                    double maximum = 0.0;
                    for (int t = firstTable; t <= lastTable; ++t)
                    {
                        maximum = Math.Max(maximum, AtmosphereProfile.Linear(AtmosphereData.OzoneLambdaStart, AtmosphereData.OzoneLambdaStep,
                                                                             AtmosphereData.OzoneCrossSectionByTemperature[t], lambdaNm));
                    }
                    return (Float)(maximum * unit);
                }, (int)Lambda_min, (int)Lambda_max, alloc);

                // NO2: the maximum over 33 temperatures of the (clamped) range of the polynomial
                double no2Low = Math.Clamp(temperatureLow, No2TemperatureMinimum, No2TemperatureMaximum);
                double no2High = Math.Clamp(temperatureHigh, No2TemperatureMinimum, No2TemperatureMaximum);
                maxNo2CrossSection[i] = DenselySampledSpectrum.SampleFunction((Float lambdaNm) =>
                {
                    double c0 = AtmosphereProfile.Linear(AtmosphereData.OzoneLambdaStart, AtmosphereData.OzoneLambdaStep, AtmosphereData.No2C0, lambdaNm);
                    double c1 = AtmosphereProfile.Linear(AtmosphereData.OzoneLambdaStart, AtmosphereData.OzoneLambdaStep, AtmosphereData.No2C1, lambdaNm);
                    double c2 = AtmosphereProfile.Linear(AtmosphereData.OzoneLambdaStart, AtmosphereData.OzoneLambdaStep, AtmosphereData.No2C2, lambdaNm);
                    double maximum = 0.0;
                    for (int k = 0; k <= 32; ++k)
                    {
                        double dT = no2Low + (no2High - no2Low) * k / 32.0 - 273.15;
                        maximum = Math.Max(maximum, c0 + c1 * dT + c2 * dT * dT);
                    }
                    return (Float)(maximum * unit);
                }, (int)Lambda_min, (int)Lambda_max, alloc);
            }
            planetRadiusUnits = planetRadiusMeters / metersPerUnit;
        }

        // The species of the aerosols, from the Mie solver: computed once (a few seconds) and shared by all media
        static MieSpecies[] sharedSpecies;
        static readonly object sharedSpeciesLock = new object();

        static MieSpecies[] BuildSpecies()
        {
            lock (sharedSpeciesLock)
            {
                if (sharedSpecies == null)
                {
                    double[][] wavelengths = { AtmosphereData.InsoRefractiveWavelength, AtmosphereData.WasoRefractiveWavelength,
                                               AtmosphereData.SootRefractiveWavelength };
                    double[][] reals = { AtmosphereData.InsoRefractiveReal, AtmosphereData.WasoRefractiveReal, AtmosphereData.SootRefractiveReal };
                    double[][] imaginaries = { AtmosphereData.InsoRefractiveImaginary, AtmosphereData.WasoRefractiveImaginary,
                                               AtmosphereData.SootRefractiveImaginary };
                    double[][] distributions = { AtmosphereData.InsoSizeDistribution, AtmosphereData.WasoSizeDistribution,
                                                 AtmosphereData.SootSizeDistribution };
                    MieSpecies[] built = new MieSpecies[Species];
                    for (int s = 0; s < Species; ++s)
                    {
                        double[] d = distributions[s];
                        built[s] = new MieSpecies(wavelengths[s], reals[s], imaginaries[s], d[0], d[1], d[2], d[3], d[4], AtmosphereData.AerosolLambdaStart,
                                                  AtmosphereData.AerosolLambdaStep, AtmosphereData.InsoMassExtinction.Length, 600);
                    }
                    sharedSpecies = built;
                }
                return sharedSpecies;
            }
        }

        public MieSpecies[] AerosolSpecies() { return species; }

        // The temperature in K and the number densities in 1/m^3 of NO2 and O2 at the altitude h (m)
        double Temperature(double h)
        {
            return AtmosphereProfile.PiecewiseLinear(AtmosphereData.AirProfileAltitude, AtmosphereData.AirProfileTemperature, h);
        }

        double No2Density(double h)
        {
            return no2Scale * AtmosphereProfile.LogLinear(AtmosphereData.AirProfileAltitude, AtmosphereData.AirProfileNO2Density, h);
        }

        double OxygenDensity(double h)
        {
            return AtmosphereProfile.LogLinear(AtmosphereData.AirProfileAltitude, AtmosphereData.AirProfileO2Density, h);
        }

        // The ozone cross section (per scene unit) at the temperature T (K): interpolated between the two tables of the data that bracket it
        SampledSpectrum OzoneCross(double temperature, SampledWavelengths lambda)
        {
            double t = Math.Clamp((temperature - AtmosphereData.OzoneTemperatures[0]) / 10.0, 0.0, ozoneCrossSection.Length - 1.0);
            int lower = Math.Min((int)t, ozoneCrossSection.Length - 2);
            Float f = (Float)(t - lower);
            return (1 - f) * ozoneCrossSection[lower].Sample(lambda) + f * ozoneCrossSection[lower + 1].Sample(lambda);
        }

        // The NO2 cross section (per scene unit) at the temperature T (K), clamped to the range of the data
        SampledSpectrum No2Cross(double temperature, SampledWavelengths lambda)
        {
            double dT = Math.Clamp(temperature, No2TemperatureMinimum, No2TemperatureMaximum) - 273.15;
            SampledSpectrum c0 = no2CrossSection[0].Sample(lambda), c1 = no2CrossSection[1].Sample(lambda), c2 = no2CrossSection[2].Sample(lambda);
            SampledSpectrum result = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                result[i] = (Float)Math.Max(c0[i] + c1[i] * dT + c2[i] * dT * dT, 0.0);
            }
            return result;
        }

        // The aerosol profile for an aerosol extinction at the ground of target (1/m) at 550 nm: the three standard profiles (clean, average and
        // polluted) have ground visibilities of about 131.8, 59.4 and 27.6 km (checked against the values of the sky model of Wilkie et al.); the
        // concentrations of the node of the table are interpolated (extrapolated outside) linearly in the logarithm of the ground extinction, so the
        // layer of haze at the ground gets thinner as the visibility grows. The result is scaled afterwards to the exact target.
        static double[][] BlendProfiles(double target, double[][] extinctionTable)
        {
            double[][][] anchors =
            {
                new double[][] { AtmosphereData.ContinentalCleanInso, AtmosphereData.ContinentalCleanWaso, AtmosphereData.ContinentalCleanSoot },
                new double[][] { AtmosphereData.ContinentalAverageInso, AtmosphereData.ContinentalAverageWaso, AtmosphereData.ContinentalAverageSoot },
                new double[][] { AtmosphereData.ContinentalPollutedInso, AtmosphereData.ContinentalPollutedWaso, AtmosphereData.ContinentalPollutedSoot }
            };
            double[] logExtinction = new double[anchors.Length];
            for (int a = 0; a < anchors.Length; ++a)
            {
                double ground = 0;
                for (int s = 0; s < Species; ++s)
                {
                    ground += anchors[a][s][0] * AtmosphereProfile.Linear(AtmosphereData.AerosolLambdaStart, AtmosphereData.AerosolLambdaStep,
                                                                          extinctionTable[s], 550.0);
                }
                logExtinction[a] = Math.Log(ground);
            }
            double x = Math.Log(target);
            int segment = x < logExtinction[1] ? 0 : 1;
            double f = (x - logExtinction[segment]) / (logExtinction[segment + 1] - logExtinction[segment]);
            double[][] blended = new double[Species][];
            for (int s = 0; s < Species; ++s)
            {
                double[] low = anchors[segment][s], high = anchors[segment + 1][s];
                blended[s] = new double[low.Length];
                for (int n = 0; n < low.Length; ++n)
                {
                    if (low[n] > 0 && high[n] > 0)
                    {
                        blended[s][n] = Math.Exp(Math.Log(low[n]) * (1 - f) + Math.Log(high[n]) * f);
                    }
                    else
                    {
                        blended[s][n] = Math.Max(low[n] * (1 - f) + high[n] * f, 0.0);
                    }
                }
            }
            return blended;
        }

        // The mass concentration in g/m^3 of the species s at the altitude h (m)
        double AerosolMass(int s, double h)
        {
            return aerosolScale * AtmosphereProfile.LogLinear(aerosolAltitude, aerosolMass[s], h);
        }

        // The ozone number density in 1/m^3 at the altitude h (m)
        double OzoneDensity(double h)
        {
            return ozoneScale * AtmosphereProfile.LogLinear(AtmosphereData.OzoneProfileAltitude, AtmosphereData.OzoneProfileDensity, h);
        }

        // The ozone column in Dobson units of the profile at the scale of the medium
        public double OzoneDobsonUnits()
        {
            double column = 0;
            double step = 100.0;
            for (double h = 0.5 * step; h < topAltitudeMeters; h += step)
            {
                column += OzoneDensity(h) * step;
            }
            // 1 Dobson unit is 2.687e20 molecules per m^2
            return column / 2.687e20;
        }

        public override string ToString()
        {
            return StringPrintf("[ AtmosphereMedium planetRadius: %f topAltitude: %f shells: %d ]", (Float)planetRadiusUnits,
                                (Float)topAltitudeMeters, shellRadius.Length - 1);
        }

        public override bool IsEmissive() { return false; }

        public double CenterX() { return cx; }
        public double CenterY() { return cy; }
        public double CenterZ() { return cz; }
        public double[] ShellRadii() { return shellRadius; }

        // The altitude above the planet in metres of a point of the scene
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double AltitudeMeters(Point3f p)
        {
            double x = p.x - cx, y = p.y - cy, z = p.z - cz;
            return (Math.Sqrt(x * x + y * y + z * z) - planetRadiusUnits) * metersPerUnit;
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            double h = AltitudeMeters(p);
            MediumProperties mp = new MediumProperties();
            mp.Le = new SampledSpectrum(0f);
            if (h < 0 || h >= topAltitudeMeters)
            {
                mp.sigma_a = new SampledSpectrum(0f);
                mp.sigma_s = new SampledSpectrum(0f);
                mp.phase = new HGPhaseFunction(0f);
                return mp;
            }
            SampledSpectrum sigmaRayleigh = (Float)profile.RelativeDensity(h) * rayleigh.Sample(lambda);
            double temperature = Temperature(h);
            double oxygen = OxygenDensity(h);
            SampledSpectrum absorption = (Float)OzoneDensity(h) * OzoneCross(temperature, lambda) +
                                         (Float)No2Density(h) * No2Cross(temperature, lambda) +
                                         (Float)(o4Scale * oxygen * oxygen / O4SpectrumScale) * o4CrossSection.Sample(lambda);
            SampledSpectrum aerosolExtinction = new SampledSpectrum(0f);
            SampledSpectrum aerosolScattering = new SampledSpectrum(0f);
            SampledSpectrum[] speciesScattering = new SampledSpectrum[Species];
            for (int s = 0; s < Species; ++s)
            {
                SampledSpectrum extinction = (Float)AerosolMass(s, h) * massExtinction[s].Sample(lambda);
                speciesScattering[s] = extinction * albedo[s].Sample(lambda);
                aerosolExtinction += extinction;
                aerosolScattering += speciesScattering[s];
            }
            mp.sigma_s = sigmaRayleigh + aerosolScattering;
            mp.sigma_a = (aerosolExtinction - aerosolScattering) + absorption;

            // The Rayleigh share of the scattering and the share of each aerosol species in the aerosol scattering, at each wavelength
            SampledSpectrum share = new SampledSpectrum(0f);
            SampledSpectrum[] weights = new SampledSpectrum[Species];
            for (int s = 0; s < Species; ++s)
            {
                weights[s] = new SampledSpectrum(0f);
            }
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                share[i] = mp.sigma_s[i] > 0 ? sigmaRayleigh[i] / mp.sigma_s[i] : 0;
                if (aerosolScattering[i] > 0)
                {
                    for (int s = 0; s < Species; ++s)
                    {
                        weights[s][i] = speciesScattering[s][i] / aerosolScattering[i];
                    }
                }
            }
            mp.phase = new AtmospherePhase(species, lambda, share, depolarisedFraction.Sample(lambda), weights);
            return mp;
        }

        // The majorant of the shell with the index annulus
        public SampledSpectrum ShellMajorant(int annulus, SampledWavelengths lambda)
        {
            SampledSpectrum majorant = (Float)maxRayleighDensity[annulus] * rayleigh.Sample(lambda) +
                                       (Float)maxOzoneDensity[annulus] * maxOzoneCrossSection[annulus].Sample(lambda) +
                                       (Float)maxNo2Density[annulus] * maxNo2CrossSection[annulus].Sample(lambda) +
                                       (Float)(o4Scale * maxOxygenSquared[annulus] / O4SpectrumScale) * o4CrossSection.Sample(lambda);
            for (int s = 0; s < Species; ++s)
            {
                majorant += (Float)maxAerosolMass[annulus][s] * massExtinction[s].Sample(lambda);
            }
            // A margin for the rounding of the single precision points
            return 1.0001f * majorant;
        }

        public AtmosphereMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            return new AtmosphereMajorantIterator(this, ray, raytMax, lambda);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(SampleRay(ray, tMax, lambda));
        }

        public static AtmosphereMedium Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            double planetRadius = parameters.GetOneFloat("planetradius", 6360000f);
            double top = parameters.GetOneFloat("atmosphereheight", 100000f);
            double scale = parameters.GetOneFloat("scale", 1f);
            Point3f center = parameters.GetOnePoint3f("center", new Point3f(0f, (Float)(-planetRadius / scale), 0f));
            double visibility = parameters.GetOneFloat("visibility", 50f);
            string aerosolProfile = parameters.GetOneString("aerosolprofile", "visibility");
            double ozoneScale = parameters.GetOneFloat("ozonescale", 1f);
            double no2Scale = parameters.GetOneFloat("no2scale", 1f);
            double o4Scale = parameters.GetOneFloat("o4scale", 1f);
            if (visibility <= 0)
            {
                ErrorExit(loc, "atmosphere: \"visibility\" must be positive");
            }
            return new AtmosphereMedium(center.x, center.y, center.z, planetRadius, top, scale, visibility, aerosolProfile, ozoneScale, no2Scale, o4Scale, alloc);
        }

        // AtmosphereMedium Private Members
        const double No2TemperatureMinimum = 220.0, No2TemperatureMaximum = 294.0;
        // The O4 cross section (m^5) is far below the range of a single precision number; the spectrum holds it times this factor
        const double O4SpectrumScale = 1e40;
        readonly double cx, cy, cz, planetRadiusUnits, topAltitudeMeters, metersPerUnit, ozoneScale, no2Scale, o4Scale, aerosolScale;
        readonly AtmosphereProfile profile;
        readonly MieSpecies[] species;
        readonly DenselySampledSpectrum rayleigh, o4CrossSection, depolarisedFraction;
        readonly DenselySampledSpectrum[] ozoneCrossSection, no2CrossSection;
        readonly DenselySampledSpectrum[] maxOzoneCrossSection, maxNo2CrossSection;
        readonly double[] maxNo2Density, maxOxygenSquared;
        readonly DenselySampledSpectrum[] massExtinction, albedo;
        readonly double[] aerosolAltitude;
        readonly double[][] aerosolMass;
        readonly double[] shellRadius, maxRayleighDensity, maxOzoneDensity;
        readonly double[][] maxAerosolMass;
    }

}  // namespace pbrt
