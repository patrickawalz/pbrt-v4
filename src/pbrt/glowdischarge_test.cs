// (not in pbrt-v4) Tests of the glow discharge medium and of the wavelengths of its lines (glowdischarge.cs).

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class GlowDischargeTests
    {
        private static GlowDischargeMedium Tube(LineSpectrum spectrum, Float radius)
        {
            Point3f[] points = new Point3f[] { new Point3f(-1, 0, 0), new Point3f(1, 0, 0) };
            return new GlowDischargeMedium(points, radius, spectrum, 3.0 * GlowDischargeMedium.ProfileIntegral * radius * radius, 8f, new Transform(), null);
        }

        // The profile is Schottky's: its integral over the cross section is the constant that the brightness is divided by,
        // so that a metre of tube radiates the power that was asked
        [TEST("GlowDischarge", "ProfileCarriesThePower")]
        public static void ProfileCarriesThePower()
        {
            int steps = 200000;
            double sum = 0;
            for (int i = 0; i < steps; ++i)
            {
                double r = (i + 0.5) / steps;
                sum += GlowDischargeMedium.BesselJ0(GlowDischargeMedium.BesselZero * r) * 2 * Math.PI * r / steps;
            }
            EXPECT_NEAR(GlowDischargeMedium.ProfileIntegral, sum, 1e-9);
            EXPECT_NEAR(0.0, GlowDischargeMedium.BesselJ0(GlowDischargeMedium.BesselZero), 1e-14);

            // (the medium: points of a box around a length of the tube)
            Float radius = 0.05f;
            GlowDischargeMedium tube = Tube(new LineSpectrum(SpectralLineData.Neon, 20.180, 300, 11604.5), radius);
            RNG rng = new RNG(7);
            int count = 2000000;
            double mean = 0, squares = 0;
            for (int i = 0; i < count; ++i)
            {
                Point3f p = new Point3f(Lerp(rng.Uniform<Float>(), -0.5f, 0.5f), Lerp(rng.Uniform<Float>(), -radius, radius), Lerp(rng.Uniform<Float>(), -radius, radius));
                double v = tube.Profile(p) * 4 * radius * radius;
                mean += v;
                squares += v * v;
            }
            mean /= count;
            double error = Math.Sqrt((squares / count - mean * mean) / count);
            double expected = GlowDischargeMedium.ProfileIntegral * radius * radius;
            EXPECT_LT(Math.Abs(mean - expected), 4 * error, mean + " vs. " + expected);
            EXPECT_LT(error / expected, 2e-3);

            // (no gas beyond the ends, none outside the wall)
            EXPECT_EQ(0.0, tube.Profile(new Point3f(1.01f, 0, 0)));
            EXPECT_EQ(0.0, tube.Profile(new Point3f(0, 0.0501f, 0)));
            EXPECT_NEAR(1.0, tube.Profile(new Point3f(0.3f, 0, 0)), 1e-9);
        }

        // The wavelengths of the film with lines in the scene: the estimate of the tristimulus values of the lines is the
        // sum over the lines, and that of a smooth spectrum is what it was
        [TEST("GlowDischarge", "WavelengthsAreUnbiased")]
        public static void WavelengthsAreUnbiased()
        {
            foreach (string gas in new string[] { "neon", "mercury", "helium" })
            {
                SpectralLine[] table;
                double mass;
                SpectralLineData.Find(gas, out table, out mass);
                LineSpectrum spectrum = new LineSpectrum(table, mass, 300, 11604.5);
                SpectralLineSampling.Clear();
                SpectralLineSampling.Add(spectrum, 0.5);
                double X, Y, Z;
                spectrum.Tristimulus(out X, out Y, out Z);

                // (the ends of the range of the random number give wavelengths)
                foreach (Float edge in new Float[] { 0f, 0.25f, 0.5f, 0.99999994f })
                {
                    SampledWavelengths ends = SpectralLineSampling.SampleVisible(edge);
                    for (int k = 0; k < NSpectrumSamples; ++k)
                    {
                        EXPECT_TRUE(ends[k] >= 360 && ends[k] <= 830, gas + " u " + edge + ": " + ends[k]);
                        EXPECT_GT(ends.PDF()[k], 0);
                    }
                }

                RNG rng = new RNG(11);
                int count = 400000;
                double[] mean = new double[4], squares = new double[4];
                for (int i = 0; i < count; ++i)
                {
                    SampledWavelengths lambda = SpectralLineSampling.SampleVisible(rng.Uniform<Float>());
                    SampledSpectrum lines = spectrum.Sample(lambda);
                    double[] v = new double[4];
                    for (int k = 0; k < NSpectrumSamples; ++k)
                    {
                        double pdf = lambda.PDF()[k];
                        v[0] += lines[k] * Spectra.X().Evaluate(lambda[k]) / pdf / NSpectrumSamples;
                        v[1] += lines[k] * Spectra.Y().Evaluate(lambda[k]) / pdf / NSpectrumSamples;
                        v[2] += lines[k] * Spectra.Z().Evaluate(lambda[k]) / pdf / NSpectrumSamples;
                        v[3] += Spectra.Y().Evaluate(lambda[k]) / pdf / NSpectrumSamples;
                    }
                    for (int c = 0; c < 4; ++c)
                    {
                        mean[c] += v[c];
                        squares[c] += v[c] * v[c];
                    }
                }
                double[] expected = new double[] { X, Y, Z, CIE_Y_integral };
                for (int c = 0; c < 4; ++c)
                {
                    mean[c] /= count;
                    double error = Math.Sqrt(Math.Max(0.0, squares[c] / count - mean[c] * mean[c]) / count);
                    Console.WriteLine(gas + " component " + c + ": " + mean[c] + " +- " + error + " expected " + expected[c]);
                    EXPECT_LT(Math.Abs(mean[c] - expected[c]), 4 * error + 1e-6 * expected[c], gas + " " + c + ": " + mean[c] + " vs. " + expected[c]);
                    EXPECT_LT(error, 0.01 * expected[c] + 1e-12);
                }
            }
            SpectralLineSampling.Clear();
        }

        // The inversion of the Gaussian: known quantiles of the normal distribution, far into the tails
        [TEST("GlowDischarge", "GaussianInversion")]
        public static void GaussianInversion()
        {
            double[] probability = new double[] { 0.5, 0.8413447460685429, 0.9772498680518208, 0.9986501019683699, 1e-9, 1 - 1e-9 };
            double[] quantile = new double[] { 0, 1, 2, 3, -5.997807015007686, 5.997807015007686 };
            for (int i = 0; i < probability.Length; ++i)
            {
                double z = 1.4142135623730951 * SpectralLineSampling.InverseErrorFunction(2 * probability[i] - 1);
                EXPECT_NEAR(quantile[i], z, 1e-6);
            }
        }

        // What the trackers gather along a ray across the tube is the integral of the emission along it
        [TEST("GlowDischarge", "TrackedEmissionIsTheIntegral")]
        public static void TrackedEmissionIsTheIntegral()
        {
            LineSpectrum spectrum = new LineSpectrum(SpectralLineData.Neon, 20.180, 300, 11604.5);
            GlowDischargeMedium tube = Tube(spectrum, 0.05f);
            Float centre = (Float)spectrum.centre[9];
            Float[] wavelengths = new Float[] { centre, 500, 585.24879f, 700 };
            Float[] densities = new Float[] { 1, 1, 1, 1 };
            SampledWavelengths lambda = SampledWavelengths.FromSamples(wavelengths, densities);
            SampledSpectrum lines = spectrum.Sample(lambda);
            EXPECT_GT(lines[0], 0);
            EXPECT_EQ(0, lines[1]);

            RNG rng = new RNG(3);
            int count = 400000;
            double mean = 0, squares = 0, transmitted = 0;
            Ray ray = new Ray(new Point3f(0.2f, 0.021f, 5), new Vector3f(0, 0, -1));
            for (int i = 0; i < count; ++i)
            {
                double gathered = 0;
                SampledSpectrum T = SampleT_maj(ray, tube, Infinity, rng.Uniform<Float>(), ref rng, lambda,
                    (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                    {
                        gathered += mp.sigma_a[0] * mp.Le[0] / sigma_maj[0];
                        return true;
                    });
                mean += gathered;
                squares += gathered * gathered;
                transmitted += T[0];
            }
            mean /= count;
            double error = Math.Sqrt((squares / count - mean * mean) / count);
            // (the integral of the profile along the ray, by the midpoint rule)
            double along = 0;
            int steps = 200000;
            for (int i = 0; i < steps; ++i)
            {
                along += tube.Profile(ray.Apply(4.9f + 0.2f * (i + 0.5f) / steps)) * 0.2 / steps;
            }
            double expected = tube.DiameterEmission() * lines[0] * along / (2 * 0.05 * 0.6113957158676298);
            Console.WriteLine("tracked " + mean + " +- " + error + " expected " + expected);
            EXPECT_LT(Math.Abs(mean - expected), 4 * error, mean + " vs. " + expected);
            EXPECT_LT(error / expected, 2e-3);
        }

        // The paper's equations 14 by the classical fourth order Runge-Kutta method with a fixed small step, written here
        // from the paper and not from the solver that is tested
        private static double DriftReference(DriftModel m, double s, double r)
        {
            int steps = 400000;
            double end = m.laminar ? s : Math.Log(m.radius / r);
            double h = end / steps;
            double[] y = new double[3];
            Func<double, double[], double[]> f = (double x, double[] v) =>
            {
                double local, divergence, rate;
                if (m.laminar)
                {
                    local = m.speed * Math.Exp(-m.decay * x) * Math.Pow(1 - (r / m.radius) * (r / m.radius), m.falloff);
                    divergence = -m.decay * local;
                    rate = 1 / local;
                }
                else
                {
                    double along = m.speed * Math.Exp(-m.decay * s);
                    local = along * Math.Exp(-x);
                    divergence = -2 * along / m.radius;
                    rate = m.radius / along;
                }
                return new double[]
                {
                    rate * ((m.ionization - m.attachment) * local - m.recombination * Math.Exp(v[1]) - divergence),
                    rate * (m.ionization / m.ionScale * local * Math.Exp(v[0] - v[1]) - m.recombination / m.ionScale * Math.Exp(v[0]) -
                            m.recombination / m.ionScale * Math.Exp(v[2]) - divergence),
                    rate * (m.attachment / m.ionScale * local * Math.Exp(v[0] - v[2]) - m.recombination / m.ionScale * Math.Exp(v[1]) - divergence)
                };
            };
            for (int i = 0; i < steps; ++i)
            {
                double x = i * h;
                double[] k1 = f(x, y);
                double[] k2 = f(x + h / 2, new double[] { y[0] + h / 2 * k1[0], y[1] + h / 2 * k1[1], y[2] + h / 2 * k1[2] });
                double[] k3 = f(x + h / 2, new double[] { y[0] + h / 2 * k2[0], y[1] + h / 2 * k2[1], y[2] + h / 2 * k2[2] });
                double[] k4 = f(x + h, new double[] { y[0] + h * k3[0], y[1] + h * k3[1], y[2] + h * k3[2] });
                for (int c = 0; c < 3; ++c)
                {
                    y[c] += h / 6 * (k1[c] + 2 * k2[c] + 2 * k3[c] + k4[c]);
                }
            }
            return m.Speed(s, r) * Math.Exp(y[0]);
        }

        // The drift model: its closed form without recombination, and a reference integration with it
        [TEST("GlowDischarge", "DriftModelSolvesThePaper")]
        public static void DriftModelSolvesThePaper()
        {
            // (no recombination: the electrons grow as exp((ionization - attachment) s), whatever the decay of the speed)
            DriftModel free = new DriftModel { laminar = true, ionization = 7, recombination = 0, attachment = 0.5, ionScale = 10, speed = 2, decay = 1.5, falloff = 0, radius = 0.05, length = 1 };
            foreach (double s in new double[] { 0.1, 0.4, 0.9 })
            {
                double expected = 2 * Math.Exp(6.5 * s);
                EXPECT_LT(Math.Abs(free.Emission(s, 0.02) - expected) / expected, 1e-7, "s " + s);
            }

            DriftModel laminar = new DriftModel { laminar = true, ionization = 40, recombination = 1e-3, attachment = 0.25, ionScale = 10, speed = 1, decay = 0, falloff = 1, radius = 0.05, length = 2 };
            DriftModel decaying = new DriftModel { laminar = true, ionization = 40, recombination = 1e-3, attachment = 0.25, ionScale = 10, speed = 1, decay = 0.8, falloff = 1, radius = 0.05, length = 2 };
            DriftModel extruding = new DriftModel { laminar = false, ionization = 40, recombination = 1e-3, attachment = 0.25, ionScale = 10, speed = 1, decay = 0.3, falloff = 1, radius = 0.05, length = 2 };
            foreach (DriftModel m in new DriftModel[] { laminar, decaying, extruding })
            {
                foreach (double s in new double[] { 0.05, 0.3, 1.7 })
                {
                    foreach (double r in new double[] { 0.004, 0.02, 0.045 })
                    {
                        double reference = DriftReference(m, s, r);
                        double value = m.Emission(s, r);
                        EXPECT_LT(Math.Abs(value - reference) / reference, 2e-6, (m.laminar ? "laminar" : "extruding") + " decay " + m.decay + " s " + s + " r " + r + ": " + value + " vs. " + reference);
                    }
                }
            }
        }

        // The power of the drift model: the integral over the volume that the brightness is divided by is the integral of
        // the emission of the medium, and the trackers gather the integral of the emission along a ray
        [TEST("GlowDischarge", "DriftModelCarriesThePower")]
        public static void DriftModelCarriesThePower()
        {
            foreach (bool laminar in new bool[] { true, false })
            {
                Float radius = 0.05f;
                LineSpectrum spectrum = new LineSpectrum(SpectralLineData.Neon, 20.180, 300, 11604.5);
                DriftModel drift = new DriftModel { laminar = laminar, ionization = 12, recombination = 1e-2, attachment = 0.25, ionScale = 10, speed = 1, decay = 0.4, falloff = 1 };
                Point3f[] points = new Point3f[] { new Point3f(-1, 0, 0), new Point3f(1, 0, 0) };
                GlowDischargeMedium tube = new GlowDischargeMedium(points, radius, spectrum, 5.0, 8f, new Transform(), null, drift);

                // (the emission over the volume is the power times the length: 5 times 2)
                RNG rng = new RNG(17);
                int count = 400000;
                double mean = 0, squares = 0;
                for (int i = 0; i < count; ++i)
                {
                    Point3f p = new Point3f(Lerp(rng.Uniform<Float>(), -1f, 1f), Lerp(rng.Uniform<Float>(), -radius, radius), Lerp(rng.Uniform<Float>(), -radius, radius));
                    double v = tube.EmissionScale() * tube.Profile(p) * 2 * 4 * radius * radius;
                    mean += v;
                    squares += v * v;
                }
                mean /= count;
                double error = Math.Sqrt((squares / count - mean * mean) / count);
                Console.WriteLine((laminar ? "laminar" : "extruding") + " volume " + mean + " +- " + error + " expected 10");
                EXPECT_LT(Math.Abs(mean - 10), 4 * error, mean + " vs. 10");
                EXPECT_LT(error, 0.05);

                // (a ray across the tube)
                Float centre = (Float)spectrum.centre[9];
                SampledWavelengths lambda = SampledWavelengths.FromSamples(new Float[] { centre, 500, 585.24879f, 700 }, new Float[] { 1, 1, 1, 1 });
                SampledSpectrum lines = spectrum.Sample(lambda);
                Ray ray = new Ray(new Point3f(0.6f, 0.013f, 5), new Vector3f(0, 0, -1));
                double along = 0;
                int steps = 20000;
                for (int i = 0; i < steps; ++i)
                {
                    along += tube.Profile(ray.Apply(4.9f + 0.2f * (i + 0.5f) / steps)) * 0.2 / steps;
                }
                double expected = tube.EmissionScale() * along * lines[0];
                mean = 0;
                squares = 0;
                count = 100000;
                for (int i = 0; i < count; ++i)
                {
                    double gathered = 0;
                    SampleT_maj(ray, tube, Infinity, rng.Uniform<Float>(), ref rng, lambda,
                        (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                        {
                            gathered += mp.sigma_a[0] * mp.Le[0] / sigma_maj[0];
                            return true;
                        });
                    mean += gathered;
                    squares += gathered * gathered;
                }
                mean /= count;
                error = Math.Sqrt((squares / count - mean * mean) / count);
                Console.WriteLine((laminar ? "laminar" : "extruding") + " tracked " + mean + " +- " + error + " expected " + expected);
                EXPECT_LT(Math.Abs(mean - expected), 4 * error + 2e-3 * expected, mean + " vs. " + expected);
            }
        }
    }
}
