// (not in pbrt-v4) Tests of the atmosphere medium and the planet shape.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    // The coefficients of the model computed straight from the tables of AtmosphereData, with an interpolation that is written again here (a
    // search over the nodes) so that it does not share code with the medium
    public static class AtmosphereReference
    {
        public static double Table(double[] xs, double[] ys, double x, bool logarithmic)
        {
            if (x <= xs[0])
            {
                return ys[0];
            }
            if (x >= xs[xs.Length - 1])
            {
                return ys[ys.Length - 1];
            }
            int i = 0;
            while (xs[i + 1] <= x)
            {
                ++i;
            }
            double f = (x - xs[i]) / (xs[i + 1] - xs[i]);
            if (logarithmic && ys[i] > 0 && ys[i + 1] > 0)
            {
                return Math.Exp((1 - f) * Math.Log(ys[i]) + f * Math.Log(ys[i + 1]));
            }
            return (1 - f) * ys[i] + f * ys[i + 1];
        }

        public static double[] Lambdas(double start, double step, int count)
        {
            double[] xs = new double[count];
            for (int i = 0; i < count; ++i)
            {
                xs[i] = start + i * step;
            }
            return xs;
        }

        // The coefficients in 1/m at the wavelength lambda (nm) and the altitude h (m) of the continental_clean atmosphere that has the given
        // visibility (km) and ozone scale: Rayleigh scattering, the extinction and the scattering of the aerosols (and the scattering of each
        // species), and the absorption of the gases (ozone at the temperature of the altitude, NO2 and the O2 collision pair)
        public static void Coefficients(AtmosphereProfile profile, MieSpecies[] species, double visibilityKm, double ozoneScale, double lambda, double h,
                                        out double rayleigh, out double aerosolExtinction, out double aerosolScattering,
                                        out double[] speciesScattering, out double gasAbsorption)
        {
            double[] aerosolLambdas = Lambdas(AtmosphereData.AerosolLambdaStart, AtmosphereData.AerosolLambdaStep, species[0].MassExtinction.Length);
            double[][] extinction = { species[0].MassExtinction, species[1].MassExtinction, species[2].MassExtinction };
            double[][] albedo = { species[0].Albedo, species[1].Albedo, species[2].Albedo };
            double[][] mass = { AtmosphereData.ContinentalCleanInso, AtmosphereData.ContinentalCleanWaso, AtmosphereData.ContinentalCleanSoot };

            double rayleigh0 = profile.SeaLevelNumberDensity() * AtmosphereProfile.RayleighCrossSection(550.0);
            double aerosolAt550 = 0;
            for (int s = 0; s < 3; ++s)
            {
                aerosolAt550 += mass[s][0] * Table(aerosolLambdas, extinction[s], 550.0, false);
            }
            double scale = Math.Max(3.912 / (visibilityKm * 1000.0) - rayleigh0, 0.0) / aerosolAt550;

            rayleigh = profile.SeaLevelNumberDensity() * AtmosphereProfile.RayleighCrossSection(lambda) * profile.RelativeDensity(h);
            aerosolExtinction = 0;
            aerosolScattering = 0;
            speciesScattering = new double[3];
            for (int s = 0; s < 3; ++s)
            {
                double c = scale * Table(AtmosphereData.ContinentalCleanAltitude, mass[s], h, true);
                double e = c * Table(aerosolLambdas, extinction[s], lambda, false);
                speciesScattering[s] = e * Table(aerosolLambdas, albedo[s], lambda, false);
                aerosolExtinction += e;
                aerosolScattering += speciesScattering[s];
            }

            // Gases
            double temperature = Table(AtmosphereData.AirProfileAltitude, AtmosphereData.AirProfileTemperature, h, false);
            double[] gasLambdas = Lambdas(AtmosphereData.OzoneLambdaStart, AtmosphereData.OzoneLambdaStep, AtmosphereData.OzoneCrossSection.Length);
            double tableIndex = Math.Clamp((temperature - AtmosphereData.OzoneTemperatures[0]) / 10.0, 0.0, AtmosphereData.OzoneTemperatures.Length - 1.0);
            int lower = Math.Min((int)tableIndex, AtmosphereData.OzoneTemperatures.Length - 2);
            double f = tableIndex - lower;
            double ozoneCross = (1 - f) * Table(gasLambdas, AtmosphereData.OzoneCrossSectionByTemperature[lower], lambda, false) +
                                f * Table(gasLambdas, AtmosphereData.OzoneCrossSectionByTemperature[lower + 1], lambda, false);
            double ozone = ozoneScale * Table(AtmosphereData.OzoneProfileAltitude, AtmosphereData.OzoneProfileDensity, h, true) * ozoneCross;
            double dT = Math.Clamp(temperature, 220.0, 294.0) - 273.15;
            double no2Cross = Math.Max(Table(gasLambdas, AtmosphereData.No2C0, lambda, false) + Table(gasLambdas, AtmosphereData.No2C1, lambda, false) * dT +
                                       Table(gasLambdas, AtmosphereData.No2C2, lambda, false) * dT * dT, 0.0);
            double no2 = Table(AtmosphereData.AirProfileAltitude, AtmosphereData.AirProfileNO2Density, h, true) * no2Cross;
            double oxygen = Table(AtmosphereData.AirProfileAltitude, AtmosphereData.AirProfileO2Density, h, true);
            double o4 = Table(gasLambdas, AtmosphereData.O4CrossSection, lambda, false) * oxygen * oxygen;
            gasAbsorption = ozone + no2 + o4;
        }

        // The phase function (per steradian) of the mixture at the wavelength lambda for a scattering angle with the cosine given: the air with its
        // depolarised share and the species with the P11 tables interpolated bilinearly in the wavelength node and the angle (searched linearly)
        public static double Phase(MieSpecies[] species, double[] speciesScattering, double rayleighScattering, double lambda, double cosineScatter)
        {
            double theta = Math.Acos(Math.Clamp(cosineScatter, -1.0, 1.0));
            double nodePosition = (lambda - species[0].WavelengthStart) / species[0].WavelengthStep;
            int node = Math.Clamp((int)Math.Floor(nodePosition), 0, species[0].WavelengthCount - 2);
            double nodeFraction = Math.Clamp(nodePosition - node, 0.0, 1.0);
            int cell = 0;
            while (cell < species[0].Angles.Length - 2 && species[0].Angles[cell + 1] <= theta)
            {
                ++cell;
            }
            double cellFraction = (theta - species[0].Angles[cell]) / (species[0].Angles[cell + 1] - species[0].Angles[cell]);
            double scatteringSum = rayleighScattering;
            double aerosol = 0;
            for (int s = 0; s < 3; ++s)
            {
                scatteringSum += speciesScattering[s];
                double[] low = species[s].P11[node], high = species[s].P11[node + 1];
                double atLow = low[cell] * (1 - cellFraction) + low[cell + 1] * cellFraction;
                double atHigh = high[cell] * (1 - cellFraction) + high[cell + 1] * cellFraction;
                aerosol += speciesScattering[s] * (atLow * (1 - nodeFraction) + atHigh * nodeFraction);
            }
            double e = AtmosphereProfile.RayleighDepolarisedFraction(lambda);
            double air = (1 - e) * 3.0 / (16.0 * Math.PI) * (1.0 + cosineScatter * cosineScatter) + e / (4.0 * Math.PI);
            return (aerosol + rayleighScattering * air) / scatteringSum;
        }
    }

    public static unsafe class AtmosphereTests
    {
        static ParsedParameter FloatParameter(string name, params float[] values)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "float";
            p.name = name;
            foreach (float v in values)
            {
                p.AddFloat(v);
            }
            return p;
        }

        // The scales of the gases that are switched off in the tests that look at the aerosols or at the air alone
        static ParsedParameter[] NoGases()
        {
            return new ParsedParameter[] { FloatParameter("ozonescale", 0f), FloatParameter("no2scale", 0f), FloatParameter("o4scale", 0f) };
        }

        static ParsedParameter StringParameter(string name, string value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "string";
            p.name = name;
            p.AddString(value);
            return p;
        }

        // The medium with the profile of the tables that the reference below reads
        static AtmosphereMedium MakeCleanAtmosphere(params ParsedParameter[] ps)
        {
            List<ParsedParameter> list = new List<ParsedParameter>(ps);
            list.Add(StringParameter("aerosolprofile", "continental_clean"));
            return MakeAtmosphere(list.ToArray());
        }

        static AtmosphereMedium MakeAtmosphere(params ParsedParameter[] ps)
        {
            List<ParsedParameter> list = new List<ParsedParameter>(ps);
            Medium m = Medium.Create("atmosphere", new ParameterDictionary(list, RGBColorSpace.sRGB), new Transform(), new FileLoc(),
                                     new Allocator());
            return (AtmosphereMedium)m;
        }

        // Wavelengths that are all (nearly) the same, to read the spectra at one wavelength
        static SampledWavelengths NearlyMonochromatic(Float lambdaNm)
        {
            return SampledWavelengths.SampleUniform(0.25f, lambdaNm - 0.4f, lambdaNm + 0.4f);
        }

        [TEST("Atmosphere", "StandardAtmosphereDensity")]
        public static void Atmosphere_StandardAtmosphereDensity()
        {
            AtmosphereProfile profile = new AtmosphereProfile(100000.0);
            // 1976 standard atmosphere: 2.547e25 molecules per m^3 at sea level; the density ratio at 8 km is 0.4292 and at 11 km 0.2972
            EXPECT_NEAR(profile.NumberDensityExact(0) / 2.547e25, 1.0, 1e-3);
            EXPECT_NEAR(profile.RelativeDensity(8000.0), 0.4292, 0.004);
            EXPECT_NEAR(profile.RelativeDensity(11019.0), 0.2972, 0.004);
            // Monotone decreasing
            double previous = 2;
            for (double h = 0; h <= 100000.0; h += 500.0)
            {
                double d = profile.RelativeDensity(h);
                EXPECT_LE(d, previous);
                previous = d;
            }
        }

        [TEST("Atmosphere", "RayleighCoefficient")]
        public static void Atmosphere_RayleighCoefficient()
        {
            double n0 = 2.546899e25;
            double beta550 = n0 * AtmosphereProfile.RayleighCrossSection(550.0);
            // About 1.15e-5 per metre at 550 nm at sea level (Bodhaine's vertical optical depth of 0.097 over the 8.4 km effective height)
            EXPECT_GT(beta550, 1.13e-5);
            EXPECT_LT(beta550, 1.17e-5);
            // About lambda^-4.08
            // The depolarisation ratio of air at 550 nm is about 0.028 (rho from the King factor F = (6 + 3 rho) / (6 - 7 rho))
            double king = AtmosphereProfile.KingFactor(550.0);
            EXPECT_NEAR(6.0 * (king - 1.0) / (3.0 + 7.0 * king), 0.028, 0.002);
            double ratio = AtmosphereProfile.RayleighCrossSection(450.0) / AtmosphereProfile.RayleighCrossSection(650.0);
            EXPECT_NEAR(ratio, Math.Pow(650.0 / 450.0, 4.08), 0.25);
        }

        [TEST("Atmosphere", "VerticalRayleighOpticalDepth")]
        public static void Atmosphere_VerticalRayleighOpticalDepth()
        {
            // No aerosols and no ozone: the vertical Rayleigh optical depth at 550 nm is about 0.097
            AtmosphereMedium m = MakeCleanAtmosphere(FloatParameter("visibility", 1e7f), NoGases()[0], NoGases()[1], NoGases()[2]);
            SampledWavelengths lambda = NearlyMonochromatic(550f);
            double tau = 0;
            double step = 25.0;
            for (double h = 0.5 * step; h < 100000.0; h += step)
            {
                MediumProperties mp = m.SamplePoint(new Point3f(0f, (Float)h, 0f), lambda);
                tau += (mp.sigma_a[0] + mp.sigma_s[0]) * step;
            }
            EXPECT_NEAR(tau, 0.097, 0.004);
        }

        [TEST("Atmosphere", "MajorantBoundsAndCoversTheRay")]
        public static void Atmosphere_MajorantBoundsAndCoversTheRay()
        {
            AtmosphereMedium m = MakeCleanAtmosphere();
            RNG rng = new RNG();
            Float R = 6360000f;
            int checkedSegments = 0;
            for (int trial = 0; trial < 200; ++trial)
            {
                // Origins on the ground, in the air and in space; random directions
                Float height = trial % 3 == 0 ? 1.7f : trial % 3 == 1 ? 30000f * rng.Uniform<Float>() : 250000f;
                Point3f o = new Point3f(1000f * (rng.Uniform<Float>() - 0.5f), height, 1000f * (rng.Uniform<Float>() - 0.5f));
                Vector3f d = Normalize(SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
                if (trial % 3 == 0 && d.y < 0)
                {
                    d.y = -d.y;
                }
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                Ray ray = new Ray(o, d);
                AtmosphereMajorantIterator it = m.SampleRay(ray, Infinity, lambda);
                Float previousEnd = 0;
                for (pstd.optional<RayMajorantSegment> seg = it.Next(); seg.has_value(); seg = it.Next())
                {
                    RayMajorantSegment s = seg.value();
                    EXPECT_GE(s.tMin, previousEnd - 1e-3f * Math.Max(1f, previousEnd));
                    EXPECT_GT(s.tMax, s.tMin);
                    previousEnd = s.tMax;
                    // The majorant bounds sigma_t at points of the segment
                    for (int k = 0; k < 5; ++k)
                    {
                        Float t = s.tMin + (s.tMax - s.tMin) * (k + 0.5f) / 5;
                        MediumProperties mp = m.SamplePoint(ray.o + t * ray.d, lambda);
                        for (int w = 0; w < NSpectrumSamples; ++w)
                        {
                            EXPECT_LE(mp.sigma_a[w] + mp.sigma_s[w], s.sigma_maj[w] * 1.0005f + 1e-12f);
                        }
                    }
                    ++checkedSegments;
                }
                // Points of the ray inside the atmosphere are inside a segment (sampled at 200 points up to 400 km)
                AtmosphereMajorantIterator again = m.SampleRay(ray, Infinity, lambda);
                List<RayMajorantSegment> segs = new List<RayMajorantSegment>();
                for (pstd.optional<RayMajorantSegment> seg = again.Next(); seg.has_value(); seg = again.Next())
                {
                    segs.Add(seg.value());
                }
                for (int k = 0; k < 200; ++k)
                {
                    Float t = 2000f * k;
                    Point3f p = ray.o + t * ray.d;
                    double altitude = m.AltitudeMeters(p);
                    if (altitude < 50.0 || altitude > 99950.0)
                    {
                        continue;
                    }
                    // A point that is behind the ground along the ray is not covered (the ray ends at the planet)
                    bool covered = false;
                    foreach (RayMajorantSegment s in segs)
                    {
                        if (t >= s.tMin - 1f && t <= s.tMax + 1f)
                        {
                            covered = true;
                        }
                    }
                    EXPECT_TRUE(covered, "uncovered point at t " + t + " altitude " + altitude);
                }
            }
            EXPECT_GT(checkedSegments, 1000);
        }

        // The single scattered radiance of a camera ray from the ground, with the sun at a given direction, by a quadrature of the
        // integral over the ray that uses only the profile functions (nothing of the majorant iterator or of the null collisions), at 550 nm
        static double SingleScatteringQuadrature(AtmosphereProfile profile, MieSpecies[] species, double visibilityKm, double lambda,
                                                 Vector3f rayDirection, Vector3f toSun)
        {
            double radius = 6360000.0, top = 100000.0;

            Func<double, double> beta = (double h) =>
            {
                double r, ae, asc, o;
                double[] bySpecies;
                AtmosphereReference.Coefficients(profile, species, visibilityKm, 1.0, lambda, h, out r, out ae, out asc, out bySpecies, out o);
                return r + ae + o;
            };
            Func<double, double, double, double> altitude = (double x, double y, double z) =>
            {
                return Math.Sqrt(x * x + (y + radius) * (y + radius) + z * z) - radius;
            };

            // The distance from a point along a direction to the top of the atmosphere
            Func<double, double, double, Vector3f, double> distanceToTop = (double x, double y, double z, Vector3f d) =>
            {
                double ox = x, oy = y + radius, oz = z;
                double b = ox * d.x + oy * d.y + oz * d.z;
                double c = ox * ox + oy * oy + oz * oz - (radius + top) * (radius + top);
                return -b + Math.Sqrt(b * b - c);
            };

            double total = 0;
            double tauCamera = 0;
            double dt = 50.0;
            double tEnd = distanceToTop(0, 1.7, 0, rayDirection);
            // Cos of the angle between the directions of the ray (towards the sun's side) as the phase function of pbrt takes it
            Vector3f wo = -rayDirection;
            for (double t = 0.5 * dt; t < tEnd; t += dt)
            {
                double x = t * rayDirection.x, y = 1.7 + t * rayDirection.y, z = t * rayDirection.z;
                double h = altitude(x, y, z);
                double b = beta(h);
                tauCamera += b * dt * 0.5;
                double transmittanceCamera = Math.Exp(-tauCamera);
                tauCamera += b * dt * 0.5;

                // The optical depth from the point to the sun
                double tSun = distanceToTop(x, y, z, toSun);
                double tauSun = 0;
                double ds = 100.0;
                for (double s = 0.5 * ds; s < tSun; s += ds)
                {
                    tauSun += beta(altitude(x + s * toSun.x, y + s * toSun.y, z + s * toSun.z)) * ds;
                }

                double rayleighScattering, aerosolExtinction, aerosolScattering, gas;
                double[] speciesScattering;
                AtmosphereReference.Coefficients(profile, species, visibilityKm, 1.0, lambda, h, out rayleighScattering, out aerosolExtinction,
                                                 out aerosolScattering, out speciesScattering, out gas);
                double scattering = rayleighScattering + aerosolScattering;
                // pbrt's cosine of the phase function is that of wo and wi: the negative of the cosine of the scattering angle
                double phaseValue = AtmosphereReference.Phase(species, speciesScattering, rayleighScattering, lambda, -Dot(wo, toSun));
                total += transmittanceCamera * scattering * phaseValue * Math.Exp(-tauSun) * dt;
            }
            return total;
        }

        [TEST("Atmosphere", "SingleScatteringMatchesQuadrature")]
        public static void Atmosphere_SingleScatteringMatchesQuadrature()
        {
            AtmosphereMedium m = MakeCleanAtmosphere();
            AtmosphereProfile profile = new AtmosphereProfile(100000.0);
            SampledWavelengths lambda = NearlyMonochromatic(550f);
            Vector3f toSun = Normalize(new Vector3f(0f, 0.5f, 0.866025f));
            // Three camera rays: high and away from the sun, near the sun, low towards the horizon
            Vector3f[] directions = { Normalize(new Vector3f(1f, 1.2f, -0.3f)), Normalize(new Vector3f(0.1f, 0.7f, 1f)),
                                      Normalize(new Vector3f(-1f, 0.15f, 0.2f)) };
            RNG rng = new RNG(987654321);
            foreach (Vector3f d in directions)
            {
                double expected = SingleScatteringQuadrature(profile, m.AerosolSpecies(), 50.0, lambda[0], d, toSun);
                Ray cameraRay = new Ray(new Point3f(0f, 1.7f, 0f), d);
                int samples = 200000;
                double sum = 0, sumSquared = 0;
                for (int i = 0; i < samples; ++i)
                {
                    double contribution = 0;
                    SampleT_maj<AtmosphereMedium, AtmosphereMajorantIterator>(cameraRay, m, Infinity, rng.Uniform<Float>(), ref rng, lambda,
                        (Point3f p, MediumProperties mp, SampledSpectrum sigmaMaj, SampledSpectrum tMaj) =>
                        {
                            Float pAbsorb = mp.sigma_a[0] / sigmaMaj[0];
                            Float pScatter = mp.sigma_s[0] / sigmaMaj[0];
                            Float uMode = rng.Uniform<Float>();
                            if (uMode < pAbsorb)
                            {
                                return false;
                            }
                            if (uMode < pAbsorb + pScatter)
                            {
                                // Next event estimation to the sun with ratio tracking
                                Float transmittance = 1;
                                Ray shadow = new Ray(p, toSun);
                                SampleT_maj<AtmosphereMedium, AtmosphereMajorantIterator>(shadow, m, Infinity, rng.Uniform<Float>(), ref rng,
                                    lambda, (Point3f ps, MediumProperties mps, SampledSpectrum sigmaMajS, SampledSpectrum tMajS) =>
                                    {
                                        Float sigmaNull = Math.Max(0f, sigmaMajS[0] - mps.sigma_a[0] - mps.sigma_s[0]);
                                        transmittance *= sigmaNull / sigmaMajS[0];
                                        return transmittance > 0;
                                    });
                                contribution = transmittance * mp.phase.p(-cameraRay.d, toSun);
                                return false;
                            }
                            return true;
                        });
                    sum += contribution;
                    sumSquared += contribution * contribution;
                }
                double mean = sum / samples;
                double standardError = Math.Sqrt(Math.Max(sumSquared / samples - mean * mean, 0) / samples);
                double relative = (mean - expected) / expected;
                Console.WriteLine("single scattering, direction " + d + ": quadrature " + expected + ", estimate " + mean + " +- " + standardError +
                                  ", relative difference " + relative);
                EXPECT_LT(Math.Abs(mean - expected), 4 * standardError + 0.004 * expected,
                          "direction " + d + " quadrature " + expected + " estimate " + mean + " +- " + standardError +
                          " relative difference " + relative);
            }
        }

        // The phase function of a point is the mixture of the Rayleigh phase function of the air and the Henyey-Greenstein one of the aerosols with
        // the shares of each wavelength, sampled with the shares of the hero wavelength. The weight f / pdf of every wavelength must integrate to one
        // and give the first moment of the exact mixture of that wavelength.
        [TEST("Atmosphere", "SpectralPhaseIsUnbiased")]
        public static void Atmosphere_SpectralPhaseIsUnbiased()
        {
            AtmosphereMedium m = MakeCleanAtmosphere();
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0f, 440f, 660f);
            MediumProperties mp = m.SamplePoint(new Point3f(0f, 100f, 0f), lambda);
            EXPECT_TRUE(mp.phase.WavelengthDependent());
            BSDF bsdf = new BSDF(mp.phase);
            Vector3f wo = Normalize(new Vector3f(0.3f, 0.5f, -0.8f));

            // The first moment of the exact mixture of each wavelength, by a quadrature over the angle to wo
            // The first moment of cos(angle between wo and wi) of the exact mixture of each wavelength: minus the mean cosine of the scattering
            // angle, which is zero for the air and the asymmetry parameter of the Mie series (computed by the solver, not from the tables of the
            // phase function) for each species
            AtmospherePhase atmospherePhase = (AtmospherePhase)mp.phase;
            SampledSpectrum[] weights = atmospherePhase.SpeciesWeights();
            MieSpecies[] species = m.AerosolSpecies();
            double[] expectedMoment = new double[NSpectrumSamples];
            for (int w = 0; w < NSpectrumSamples; ++w)
            {
                double aerosolMoment = 0;
                for (int s = 0; s < 3; ++s)
                {
                    aerosolMoment += weights[s][w] * AtmosphereProfile.Linear(species[0].WavelengthStart, species[0].WavelengthStep,
                                                                              species[s].Asymmetry, lambda[w]);
                }
                expectedMoment[w] = -(1.0 - atmospherePhase.RayleighShare()[w]) * aerosolMoment;
            }

            RNG rng = new RNG(4242);
            int samples = 400000;
            double[] mean = new double[NSpectrumSamples];
            double[] moment = new double[NSpectrumSamples];
            for (int i = 0; i < samples; ++i)
            {
                pstd.optional<BSDFSample> ps = bsdf.Sample_f(wo, 0f, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                EXPECT_TRUE(ps.has_value());
                BSDFSample sample = ps.value();
                Float cosTheta = Dot(wo, sample.wi);
                for (int w = 0; w < NSpectrumSamples; ++w)
                {
                    double weight = sample.f[w] / sample.pdf;
                    mean[w] += weight;
                    moment[w] += weight * cosTheta;
                }
            }
            for (int w = 0; w < NSpectrumSamples; ++w)
            {
                Console.WriteLine("spectral phase, " + lambda[w] + " nm: integral " + mean[w] / samples + ", first moment " + moment[w] / samples +
                                  " expected " + expectedMoment[w]);
                EXPECT_NEAR(mean[w] / samples, 1.0, 0.01);
                EXPECT_NEAR(moment[w] / samples, expectedMoment[w], 0.01);
            }
        }

        [TEST("Atmosphere", "OzoneColumnAndCoefficients")]
        public static void Atmosphere_OzoneColumnAndCoefficients()
        {
            AtmosphereMedium m = MakeCleanAtmosphere();
            // The U.S. standard atmosphere has a column of about 340 Dobson units
            double column = m.OzoneDobsonUnits();
            Console.WriteLine("ozone column: " + column + " DU");
            EXPECT_GT(column, 300.0);
            EXPECT_LT(column, 380.0);
            AtmosphereProfile profile = new AtmosphereProfile(100000.0);
            // The coefficients of the medium equal the ones computed from the tables, at several altitudes and wavelengths (the spectra of the medium are
            // sampled every nanometre and the wavelengths of the test are not integers: the Rayleigh coefficient changes by 0.5 % per 0.5 nm at 400 nm)
            foreach (double h in new double[] { 5.0, 700.0, 1500.0, 2500.0, 9000.0, 21000.0, 40000.0 })
            {
                foreach (float lambdaNm in new float[] { 400f, 475f, 550f, 620f, 690f })
                {
                    SampledWavelengths lambda = NearlyMonochromatic(lambdaNm);
                    MediumProperties mp = m.SamplePoint(new Point3f(0f, (Float)h, 0f), lambda);
                    double rayleigh, ae, asc, gas;
                    double[] bySpecies;
                    AtmosphereReference.Coefficients(profile, m.AerosolSpecies(), 50.0, 1.0, lambda[0], h, out rayleigh, out ae, out asc, out bySpecies,
                                                     out gas);
                    double sigmaT = mp.sigma_a[0] + mp.sigma_s[0];
                    EXPECT_NEAR(sigmaT / (rayleigh + ae + gas), 1.0, 8e-3, "extinction at " + h + " m, " + lambdaNm + " nm");
                    EXPECT_NEAR(mp.sigma_s[0] / (rayleigh + asc), 1.0, 8e-3, "scattering at " + h + " m, " + lambdaNm + " nm");
                }
            }
            // The extinction of the air at the ground at 550 nm is 3.912 / visibility (aerosol plus Rayleigh)
            SampledWavelengths l550 = NearlyMonochromatic(550f);
            MediumProperties ground = m.SamplePoint(new Point3f(0f, 0.01f, 0f), l550);
            double rayleighGround, aerosolExtinctionGround, aerosolScatteringGround, gasGround;
            double[] bySpeciesGround;
            AtmosphereReference.Coefficients(profile, m.AerosolSpecies(), 50.0, 1.0, l550[0], 0.01, out rayleighGround, out aerosolExtinctionGround,
                                             out aerosolScatteringGround, out bySpeciesGround, out gasGround);
            EXPECT_NEAR((ground.sigma_a[0] + ground.sigma_s[0] - gasGround) * 50000.0, 3.912, 0.02);
        }

        // The profile follows the visibility: at the visibilities of the three standard profiles (131.8, 59.4 and 27.6 km) the ratio of the aerosol
        // extinction at 3 km to the one at the ground is the one of the clean, average and polluted tables, and the ground extinction is exact
        [TEST("Atmosphere", "VisibilityBlendsProfiles")]
        public static void Atmosphere_VisibilityBlendsProfiles()
        {
            SampledWavelengths l550 = NearlyMonochromatic(550f);
            double[][][] tables =
            {
                new double[][] { AtmosphereData.ContinentalCleanInso, AtmosphereData.ContinentalCleanWaso, AtmosphereData.ContinentalCleanSoot },
                new double[][] { AtmosphereData.ContinentalAverageInso, AtmosphereData.ContinentalAverageWaso, AtmosphereData.ContinentalAverageSoot },
                new double[][] { AtmosphereData.ContinentalPollutedInso, AtmosphereData.ContinentalPollutedWaso, AtmosphereData.ContinentalPollutedSoot }
            };
            double[][] extinction = { AtmosphereData.InsoMassExtinction, AtmosphereData.WasoMassExtinction, AtmosphereData.SootMassExtinction };
            double[] k550 = new double[3];
            double[] aerosolLambdas = AtmosphereReference.Lambdas(AtmosphereData.AerosolLambdaStart, AtmosphereData.AerosolLambdaStep,
                                                                  AtmosphereData.InsoMassExtinction.Length);
            for (int s = 0; s < 3; ++s)
            {
                k550[s] = AtmosphereReference.Table(aerosolLambdas, extinction[s], 550.0, false);
            }
            double[] visibilities = { 131.8, 59.4, 27.6 };
            for (int i = 0; i < 3; ++i)
            {
                AtmosphereMedium m = MakeAtmosphere(FloatParameter("visibility", (float)visibilities[i]), NoGases()[0], NoGases()[1], NoGases()[2]);
                MediumProperties ground = m.SamplePoint(new Point3f(0f, 0.01f, 0f), l550);
                MediumProperties high = m.SamplePoint(new Point3f(0f, 3000f, 0f), l550);
                double rayleighGround = 2.5469e25 * AtmosphereProfile.RayleighCrossSection(l550[0]);
                double aerosolGround = ground.sigma_a[0] + ground.sigma_s[0] - rayleighGround;
                double aerosolHigh = high.sigma_a[0] + high.sigma_s[0] - rayleighGround * new AtmosphereProfile(100000.0).RelativeDensity(3000.0);
                // The ratio of the aerosol extinction at 3 km and at the ground of the table of the standard profile
                double expectedHigh = 0, expectedGround = 0;
                for (int s = 0; s < 3; ++s)
                {
                    expectedHigh += AtmosphereReference.Table(AtmosphereData.ContinentalCleanAltitude, tables[i][s], 3000.0, true) * k550[s];
                    expectedGround += tables[i][s][0] * k550[s];
                }
                double expected = expectedHigh / expectedGround;
                Console.WriteLine("visibility " + visibilities[i] + " km: aerosol extinction 3 km / ground " + aerosolHigh / aerosolGround +
                                  ", table " + expected);
                EXPECT_NEAR(aerosolHigh / aerosolGround / expected, 1.0, 0.05);
                EXPECT_NEAR((ground.sigma_a[0] + ground.sigma_s[0]) * visibilities[i] * 1000.0, 3.912, 0.06);
            }
        }

        // A sphere much smaller than the wavelength scatters like a Rayleigh scatterer: Qsca = 8/3 x^4 |(m^2 - 1) / (m^2 + 2)|^2 and the Mueller
        // matrix ratios S12/S11 = -sin^2/(1+cos^2) and S33/S11 = 2 cos/(1+cos^2) (Q = I_parallel - I_perpendicular to the scattering plane)
        [TEST("Atmosphere", "MieRayleighLimit")]
        public static void Atmosphere_MieRayleighLimit()
        {
            MieSolver solver = new MieSolver();
            double x = 0.01, m = 1.5;
            double[] thetas = { 0.3, 1.0, Math.PI / 2, 2.2 };
            double[] cosines = new double[thetas.Length];
            for (int i = 0; i < thetas.Length; ++i)
            {
                cosines[i] = Math.Cos(thetas[i]);
            }
            double[] s1r = new double[4], s1i = new double[4], s2r = new double[4], s2i = new double[4];
            double qext, qsca, g;
            solver.Sphere(x, m, 0.0, cosines, s1r, s1i, s2r, s2i, out qext, out qsca, out g);
            double polarisability = (m * m - 1.0) / (m * m + 2.0);
            EXPECT_NEAR(qsca / (8.0 / 3.0 * Math.Pow(x, 4) * polarisability * polarisability), 1.0, 1e-3);
            EXPECT_NEAR(qext / qsca, 1.0, 1e-6);
            EXPECT_NEAR(g, 0.0, 1e-4);
            for (int i = 0; i < thetas.Length; ++i)
            {
                double s1 = s1r[i] * s1r[i] + s1i[i] * s1i[i], s2 = s2r[i] * s2r[i] + s2i[i] * s2i[i];
                double s11 = 0.5 * (s1 + s2), s12 = 0.5 * (s2 - s1), s33 = s2r[i] * s1r[i] + s2i[i] * s1i[i];
                double c = cosines[i];
                EXPECT_NEAR(s12 / s11, -(1.0 - c * c) / (1.0 + c * c), 1e-3);
                EXPECT_NEAR(s33 / s11, 2.0 * c / (1.0 + c * c), 1e-3);
            }
        }

        // The efficiencies of a non absorbing sphere: Qext = Qsca, and the angular integrals of the amplitude functions give the same Qsca and the
        // same asymmetry parameter as the series (independent routes to the same numbers)
        [TEST("Atmosphere", "MieAngularIntegralsMatchEfficiencies")]
        public static void Atmosphere_MieAngularIntegralsMatchEfficiencies()
        {
            MieSolver solver = new MieSolver();
            foreach (double x in new double[] { 0.5, 3.0, 12.0 })
            {
                int count = 14401;
                double[] cosines = new double[count];
                double[] angles = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    angles[i] = Math.PI * i / (count - 1);
                    cosines[i] = Math.Cos(angles[i]);
                }
                double[] s1r = new double[count], s1i = new double[count], s2r = new double[count], s2i = new double[count];
                double qext, qsca, g;
                solver.Sphere(x, 1.5, 0.0, cosines, s1r, s1i, s2r, s2i, out qext, out qsca, out g);
                EXPECT_NEAR(qext / qsca, 1.0, 1e-9);
                double integralScattering = 0, integralAsymmetry = 0;
                double step = Math.PI / (count - 1);
                for (int i = 0; i < count; ++i)
                {
                    double weight = (i == 0 || i == count - 1) ? 0.5 : 1.0;
                    double s11 = 0.5 * (s1r[i] * s1r[i] + s1i[i] * s1i[i] + s2r[i] * s2r[i] + s2i[i] * s2i[i]);
                    integralScattering += weight * s11 * Math.Sin(angles[i]) * step;
                    integralAsymmetry += weight * s11 * Math.Sin(angles[i]) * cosines[i] * step;
                }
                // Qsca = (2 / x^2) integral of S11 sin(theta) dtheta
                EXPECT_NEAR(2.0 / (x * x) * integralScattering / qsca, 1.0, 2e-4);
                EXPECT_NEAR(integralAsymmetry / integralScattering, g, 2e-4);
            }
        }

        // The species computed by the solver of the engine against the miepython values that the generator wrote into AtmosphereData (600 radii
        // in both): mass extinction, single scattering albedo and asymmetry at every wavelength of the table
        [TEST("Atmosphere", "MieSpeciesMatchTables")]
        public static void Atmosphere_MieSpeciesMatchTables()
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            MieSpecies[] species = MakeCleanAtmosphere().AerosolSpecies();
            Console.WriteLine("Mie species of the engine computed in " + stopwatch.ElapsedMilliseconds + " ms");
            double[][] extinction = { AtmosphereData.InsoMassExtinction, AtmosphereData.WasoMassExtinction, AtmosphereData.SootMassExtinction };
            double[][] albedo = { AtmosphereData.InsoAlbedo, AtmosphereData.WasoAlbedo, AtmosphereData.SootAlbedo };
            double[][] asymmetry = { AtmosphereData.InsoAsymmetry, AtmosphereData.WasoAsymmetry, AtmosphereData.SootAsymmetry };
            string[] names = { "inso", "waso", "soot" };
            for (int s = 0; s < 3; ++s)
            {
                double worstExtinction = 0, worstAlbedo = 0, worstAsymmetry = 0;
                for (int w = 0; w < extinction[s].Length; ++w)
                {
                    worstExtinction = Math.Max(worstExtinction, Math.Abs(species[s].MassExtinction[w] / extinction[s][w] - 1.0));
                    worstAlbedo = Math.Max(worstAlbedo, Math.Abs(species[s].Albedo[w] - albedo[s][w]));
                    worstAsymmetry = Math.Max(worstAsymmetry, Math.Abs(species[s].Asymmetry[w] - asymmetry[s][w]));
                }
                Console.WriteLine(names[s] + ": worst relative difference of the extinction " + worstExtinction + ", of the albedo " + worstAlbedo +
                                  " (absolute), of the asymmetry " + worstAsymmetry + " (absolute)");
                EXPECT_LT(worstExtinction, 0.005);
                EXPECT_LT(worstAlbedo, 0.002);
                EXPECT_LT(worstAsymmetry, 0.002);
            }
        }

        // The tabulated phase function and its sampling: P11 integrates to one, its mean cosine is the asymmetry parameter of the series, and the
        // angles that SampleAngle returns have that mean and a density equal to the table
        [TEST("Atmosphere", "MieTablesAndSampling")]
        public static void Atmosphere_MieTablesAndSampling()
        {
            MieSpecies[] species = MakeCleanAtmosphere().AerosolSpecies();
            for (int s = 0; s < 3; ++s)
            {
                foreach (int node in new int[] { 4, 20, 40 })
                {
                    double[] p = species[s].P11[node];
                    double integral = 0, mean = 0;
                    for (int j = 0; j + 1 < species[s].Angles.Length; ++j)
                    {
                        // Fine midpoint quadrature of the linear interpolation in each cell
                        int pieces = 64;
                        double width = species[s].Angles[j + 1] - species[s].Angles[j];
                        for (int k = 0; k < pieces; ++k)
                        {
                            double f = (k + 0.5) / pieces;
                            double theta = species[s].Angles[j] + f * width;
                            double value = p[j] * (1 - f) + p[j + 1] * f;
                            integral += 2.0 * Math.PI * value * Math.Sin(theta) * width / pieces;
                            mean += 2.0 * Math.PI * value * Math.Sin(theta) * Math.Cos(theta) * width / pieces;
                        }
                    }
                    double expectedAsymmetry = species[s].Asymmetry[node];
                    EXPECT_NEAR(integral, 1.0, 1e-3);
                    EXPECT_NEAR(mean, expectedAsymmetry, 0.004);

                    RNG rng = new RNG((ulong)(31337 + s * 100 + node));
                    int samples = 200000;
                    double sampleMean = 0;
                    for (int i = 0; i < samples; ++i)
                    {
                        double pdf;
                        double theta = species[s].SampleAngle(node, rng.Uniform<Float>(), out pdf);
                        sampleMean += Math.Cos(theta);
                    }
                    sampleMean /= samples;
                    Console.WriteLine("species " + s + " node " + node + ": table mean cosine " + mean + ", series " + expectedAsymmetry + ", samples " +
                                      sampleMean);
                    EXPECT_NEAR(sampleMean, mean, 0.006);
                }
            }
        }

        static Light MakeLight(string name, double elevationDegrees, float angle)
        {
            double e = elevationDegrees * Math.PI / 180.0;
            List<ParsedParameter> list = new List<ParsedParameter>();
            ParsedParameter from = new ParsedParameter(new FileLoc());
            from.type = "point3"; from.name = "from";
            from.AddFloat(0f); from.AddFloat((float)Math.Sin(e)); from.AddFloat((float)Math.Cos(e));
            list.Add(from);
            ParsedParameter to = new ParsedParameter(new FileLoc());
            to.type = "point3"; to.name = "to";
            to.AddFloat(0f); to.AddFloat(0f); to.AddFloat(0f);
            list.Add(to);
            list.Add(FloatParameter("illuminance", 3f));
            if (name == "sun") list.Add(FloatParameter("angle", angle));
            return Light.Create(name, new ParameterDictionary(list, RGBColorSpace.sRGB), new Transform(), null, null, new FileLoc(), new Allocator());
        }

        [TEST("Atmosphere", "SunDiskIrradianceAndSampling")]
        public static void Atmosphere_SunDiskIrradianceAndSampling()
        {
            Light sun = MakeLight("sun", 3.0, 0.5334f), distant = MakeLight("distant", 3.0, 0f);
            Bounds3f bounds = new Bounds3f(new Point3f(-1000f, -1000f, -1000f), new Point3f(1000f, 1000f, 1000f));
            sun.Preprocess(bounds); distant.Preprocess(bounds);
            LightSampleContext ctx = new LightSampleContext(new Point3fi(new Point3f(0f, 0f, 0f)), new Normal3f(0f, 0f, 0f), new Normal3f(0f, 0f, 0f));
            double alpha = 0.5 * 0.5334 * Math.PI / 180.0;
            double omega = 2.0 * Math.PI * (1.0 - Math.Cos(alpha));
            foreach (Float lambdaNm in new Float[] { 450f, 550f, 650f })
            {
                SampledWavelengths lambda = NearlyMonochromatic(lambdaNm);
                SampledSpectrum reference = distant.SampleLi(ctx, new Point2f(0.5f, 0.5f), lambda).value().L;
                RNG rng = new RNG(5);
                int n = 40000;
                double sum = 0, sumSquares = 0;
                for (int i = 0; i < n; ++i)
                {
                    Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    LightLiSample ls = sun.SampleLi(ctx, u, lambda).value();
                    // The radiance divided by the density is the irradiance on a surface facing the sun
                    double x = ls.L[0] / ls.pdf;
                    sum += x; sumSquares += x * x;
                    // The radiance seen by a ray and the density agree with the sample
                    EXPECT_NEAR(sun.PDF_Li(ctx, ls.wi) * omega, 1.0, 1e-4);
                    if (i < 200)
                    {
                        SampledSpectrum le = sun.Le(new Ray(new Point3f(0f, 0f, 0f), ls.wi), lambda);
                        EXPECT_NEAR(le[0] / ls.L[0], 1.0, 2e-3);
                    }
                }
                double mean = sum / n;
                double error = Math.Sqrt((sumSquares / n - mean * mean) / n);
                Console.WriteLine("sun irradiance at " + lambdaNm + " nm: " + mean + " +- " + error + ", distant light " + reference[0]);
                EXPECT_NEAR(mean / reference[0], 1.0, 4 * error / reference[0] + 2e-3);
            }
            // Outside the disk there is no radiance and no density; limb darkening makes the edge darker, more so in the blue
            double e0 = 3.0 * Math.PI / 180.0;
            Vector3f toSun = new Vector3f(0f, (Float)Math.Sin(e0), (Float)Math.Cos(e0));
            Vector3f side = new Vector3f(1f, 0f, 0f);
            Func<double, Vector3f> direction = (double angle) =>
                pbrt.Normalize(toSun * (Float)Math.Cos(angle) + side * (Float)Math.Sin(angle));
            SampledWavelengths blue = NearlyMonochromatic(450f), red = NearlyMonochromatic(700f);
            Ray outside = new Ray(new Point3f(0f, 0f, 0f), direction(1.1 * alpha));
            EXPECT_EQ(sun.Le(outside, blue)[0], 0f);
            EXPECT_EQ(sun.PDF_Li(ctx, outside.d), 0f);
            Ray centre = new Ray(new Point3f(0f, 0f, 0f), toSun), edge = new Ray(new Point3f(0f, 0f, 0f), direction(0.995 * alpha));
            double ratioBlue = sun.Le(edge, blue)[0] / sun.Le(centre, blue)[0];
            double ratioRed = sun.Le(edge, red)[0] / sun.Le(centre, red)[0];
            Console.WriteLine("edge/centre radiance: blue " + ratioBlue + ", red " + ratioRed);
            double mu = Math.Sqrt(1 - 0.995 * 0.995);
            EXPECT_NEAR(ratioBlue, 1 - 0.80 * (1 - mu), 0.02);
            EXPECT_NEAR(ratioRed, 1 - 0.53 * (1 - mu), 0.02);
            EXPECT_TRUE(ratioBlue < ratioRed);
        }

        [TEST("Atmosphere", "PlanetIntersection")]
        public static void Atmosphere_PlanetIntersection()
        {
            PlanetShape planet = new PlanetShape(0.0, -6360000.0, 0.0, 6360000.0, false);
            // Straight down from 1.7 m: the ground is 1.7 m away
            pstd.optional<ShapeIntersection> down = planet.Intersect(new Ray(new Point3f(0f, 1.7f, 0f), new Vector3f(0f, -1f, 0f)));
            EXPECT_TRUE(down.has_value());
            EXPECT_NEAR(down.value().tHit, 1.7f, 1e-4f);
            EXPECT_NEAR(down.value().intr.n.y, 1f, 1e-5f);
            // Straight up: no hit
            EXPECT_FALSE(planet.Intersect(new Ray(new Point3f(0f, 1.7f, 0f), new Vector3f(0f, 1f, 0f))).has_value());
            // A ray that dips by twice the horizon angle: the distance to the sphere from the quadratic, in double precision
            double h = 1.7, radius = 6360000.0;
            double dip = 2.0 * Math.Sqrt(2.0 * h / radius);
            double rh = radius + h;
            double expected = rh * Math.Sin(dip) - Math.Sqrt(rh * rh * Math.Sin(dip) * Math.Sin(dip) - (rh * rh - radius * radius));
            pstd.optional<ShapeIntersection> slanted =
                planet.Intersect(new Ray(new Point3f(0f, 1.7f, 0f), new Vector3f((Float)Math.Cos(dip), (Float)(-Math.Sin(dip)), 0f)));
            EXPECT_TRUE(slanted.has_value());
            if (slanted.has_value())
            {
                EXPECT_NEAR(slanted.value().tHit / expected, 1.0, 2e-4);
            }
            // A horizontal ray above the surface never reaches the curved ground
            EXPECT_FALSE(planet.Intersect(new Ray(new Point3f(0f, 1.7f, 0f), new Vector3f(1f, 0f, 0f))).has_value());
            // A line from space that passes 1 km above the surface misses; one that passes 1 km below hits
            EXPECT_FALSE(planet.Intersect(new Ray(new Point3f(-3e6f, 1000f, 0f), new Vector3f(1f, 0f, 0f))).has_value());
            EXPECT_TRUE(planet.Intersect(new Ray(new Point3f(-3e6f, -1000f, 0f), new Vector3f(1f, 0f, 0f))).has_value());
            EXPECT_TRUE(planet.IntersectP(new Ray(new Point3f(0f, 1.7f, 0f), new Vector3f(0f, -1f, 0f)), 2f));
            EXPECT_FALSE(planet.IntersectP(new Ray(new Point3f(0f, 1.7f, 0f), new Vector3f(0f, -1f, 0f)), 1f));
        }
    }
}
