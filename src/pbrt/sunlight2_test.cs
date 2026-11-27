// (not in pbrt-v4) Tests of "sun2" (sunlight2.cs): the sun light with the solar spectrum, the atmosphere and the limb
// darkening of Neckel and Labs.

using System;
using testing;

namespace pbrt
{
    public static unsafe class SunLight2Tests
    {
        private static Transform Toward(Vector3f toSun)
        {
            Vector3f w = Normalize(toSun);
            Vector3f v1, v2;
            CoordinateSystem(w, &v1, &v2);
            return new Transform(new SquareMatrix4(v1.x, v2.x, w.x, 0, v1.y, v2.y, w.y, 0, v1.z, v2.z, w.z, 0, 0, 0, 0, 1));
        }

        private static LightSampleContext Origin()
        {
            return new LightSampleContext(new Point3fi(new Point3f(0, 0, 0)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
        }

        // Without its additions "sun2" is "sun": the same light toward a point and along a ray
        [TEST("SunLight2", "SameAsSun")]
        public static void SameAsSun()
        {
            Spectrum L = GetNamedSpectrum("stdillum-D65");
            Transform toward = Toward(new Vector3f(-0.4f, 0.3f, 0.7f));
            foreach (bool darkening in new bool[] { true, false })
            {
                SunLight sun = new SunLight(toward, L, 2.5f, 0.5334f, darkening);
                SunLight2 sun2 = new SunLight2(toward, L, 2.5f, 0.5334f, darkening ? 1 : 0, false, 0, 300, new Vector3f(0, 0, 1));
                Bounds3f bounds = new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1));
                sun.Preprocess(bounds);
                sun2.Preprocess(bounds);
                RNG rng = new RNG(5);
                for (int i = 0; i < 2000; ++i)
                {
                    SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                    Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    LightLiSample a = sun.SampleLi(Origin(), u, lambda).value();
                    LightLiSample b = sun2.SampleLi(Origin(), u, lambda).value();
                    EXPECT_EQ(a.pdf, b.pdf);
                    EXPECT_EQ(a.wi.x, b.wi.x);
                    EXPECT_EQ(a.wi.z, b.wi.z);
                    Ray ray = new Ray(new Point3f(0, 0, 0), a.wi);
                    SampledSpectrum along = sun.Le(ray, lambda), along2 = sun2.Le(ray, lambda);
                    for (int k = 0; k < NSpectrumSamples; ++k)
                    {
                        EXPECT_EQ(a.L[k], b.L[k]);
                        EXPECT_EQ(along[k], along2[k]);
                    }
                }
            }
        }

        // The limb darkening of Neckel and Labs: 1 at the centre of the disc at every wavelength, darker toward the limb and
        // more so in the blue, its mean over the disc as stated, and the light keeps its irradiance with it
        [TEST("SunLight2", "NeckelLimbDarkening")]
        public static void NeckelLimbDarkening()
        {
            double worstCentre = 0, worstMean = 0;
            for (double lambda = 300; lambda <= 1100; lambda += 7)
            {
                double centre = SunLight2.NeckelLimb(lambda, 1, out double mean);
                worstCentre = Math.Max(worstCentre, Math.Abs(centre - 1));
                // (the mean over the disc: the radius squared q uniform, mu = sqrt(1 - q))
                double sum = 0;
                int n = 20000;
                double last = 2;
                for (int i = 0; i < n; ++i)
                {
                    double q = (i + 0.5) / n;
                    double value = SunLight2.NeckelLimb(lambda, Math.Sqrt(1 - q), out double _);
                    sum += value;
                    CHECK_LE(value, last + 1e-12);
                    last = value;
                }
                worstMean = Math.Max(worstMean, Math.Abs(sum / n - mean));
            }
            double blue = SunLight2.NeckelLimb(450, 0.2, out double _), green = SunLight2.NeckelLimb(550, 0.2, out double _);
            double red = SunLight2.NeckelLimb(700, 0.2, out double _);
            Console.WriteLine("Neckel and Labs: centre off 1 by " + worstCentre + ", mean off by " + worstMean + "; at mu 0.2: 450 nm " + blue.ToString("F4") +
                              ", 550 nm " + green.ToString("F4") + ", 700 nm " + red.ToString("F4"));
            CHECK_LT(worstCentre, 2e-5);
            CHECK_LT(worstMean, 1e-4);
            CHECK(blue < green && green < red);
            // (measured at 550 nm near mu 0.2: about 0.45 of the centre)
            CHECK(green > 0.40 && green < 0.50);

            Spectrum L = new ConstantSpectrum(1);
            SunLight2 flat = new SunLight2(Toward(new Vector3f(0, 0, 1)), L, 1, 0.5334f, 0, false, 0, 300, new Vector3f(0, 0, 1));
            SunLight2 dark = new SunLight2(Toward(new Vector3f(0, 0, 1)), L, 1, 0.5334f, 2, false, 0, 300, new Vector3f(0, 0, 1));
            flat.Preprocess(new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)));
            dark.Preprocess(new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)));
            SampledWavelengths wavelengths = SampledWavelengths.SampleUniform(0.37f);
            double[] sums = new double[NSpectrumSamples];
            double expected = 0;
            int samples = 200000;
            RNG rng = new RNG(9);
            for (int i = 0; i < samples; ++i)
            {
                Point2f u = new Point2f((i + rng.Uniform<Float>()) / samples, rng.Uniform<Float>());
                LightLiSample a = flat.SampleLi(Origin(), u, wavelengths).value();
                LightLiSample b = dark.SampleLi(Origin(), u, wavelengths).value();
                expected += a.L[0] / a.pdf;
                for (int k = 0; k < NSpectrumSamples; ++k)
                {
                    sums[k] += b.L[k] / b.pdf;
                }
            }
            for (int k = 0; k < NSpectrumSamples; ++k)
            {
                EXPECT_NEAR(1.0, sums[k] / expected, 2e-4);
            }
        }

        // "spectrum" "solar": the table of the sun above the atmosphere as an irradiance, and the light gives it
        [TEST("SunLight2", "SolarSpectrum")]
        public static void SolarSpectrum()
        {
            double total = 0, y = 0;
            for (int lambda = 280; lambda <= 2500; ++lambda)
            {
                total += SunLight2.SolarIrradiance(lambda);
            }
            for (int lambda = 360; lambda <= 830; ++lambda)
            {
                y += Spectra.Y().Evaluate(lambda) * SunLight2.SolarIrradiance(lambda);
            }
            Console.WriteLine("solar: " + total.ToString("F1") + " W/m^2 from 280 to 2500 nm, " + (683.002 * y).ToString("F0") + " lux, " +
                              SunLight2.SolarIrradiance(500).ToString("F4") + " W/m^2/nm at 500 nm");
            // (the solar constant is 1361 W/m^2, of which about 97 percent is in this range; 127 to 134 thousand lux)
            CHECK(total > 1250 && total < 1340);
            CHECK(683.002 * y > 125000 && 683.002 * y < 136000);

            foreach (Float angle in new Float[] { 0.5334f, 3 })
            {
                SunLight2 sun = new SunLight2(Toward(new Vector3f(0.2f, 0.1f, 0.9f)), null, 683.002f, angle, 2, true, 0, 300, new Vector3f(0, 0, 1));
                sun.Preprocess(new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)));
                SampledWavelengths wavelengths = SampledWavelengths.SampleUniform(0.61f);
                double[] sums = new double[NSpectrumSamples];
                int samples = 100000;
                RNG rng = new RNG(4);
                for (int i = 0; i < samples; ++i)
                {
                    LightLiSample s = sun.SampleLi(Origin(), new Point2f((i + rng.Uniform<Float>()) / samples, rng.Uniform<Float>()), wavelengths).value();
                    for (int k = 0; k < NSpectrumSamples; ++k)
                    {
                        sums[k] += s.L[k] / s.pdf / samples;
                    }
                }
                for (int k = 0; k < NSpectrumSamples; ++k)
                {
                    EXPECT_NEAR(1.0, sums[k] / (683.002 * SunLight2.SolarIrradiance(wavelengths[k])), 3e-4);
                }
            }
        }

        // The atmosphere: the optical depths of the formulas, the direct light of a clear day, the reddening toward the
        // horizon, and the lower edge of the disc darker than the upper one
        [TEST("SunLight2", "Atmosphere")]
        public static void Atmosphere()
        {
            // (pure air, no ozone, the sun at the zenith: the Rayleigh optical depth; Hansen and Travis's formula for it,
            // 0.008569 l^-4 (1 + 0.0113 l^-2 + 0.00013 l^-4), gives 0.0973 at 550 nm and 0.3643 at 400 nm)
            double rayleigh550 = -Math.Log(SunLight2.AtmosphericTransmittance(550, 1, 1, 0)), rayleigh400 = -Math.Log(SunLight2.AtmosphericTransmittance(400, 1, 1, 0));
            EXPECT_NEAR(0.0973, rayleigh550, 2e-3);
            EXPECT_NEAR(0.3643, rayleigh400, 6e-3);
            // (ozone: the Chappuis band, about 0.04 at its peak near 600 nm for 300 Dobson units; in proportion to the amount)
            double peak = 0, peakAt = 0;
            for (double lambda = 400; lambda <= 800; lambda += 1)
            {
                double depth = SunLight2.OzoneOpticalDepth(lambda, 300);
                if (depth > peak)
                {
                    peak = depth;
                    peakAt = lambda;
                }
            }
            Console.WriteLine("Rayleigh optical depth " + rayleigh550.ToString("F4") + " at 550 nm, " + rayleigh400.ToString("F4") + " at 400 nm; ozone, 300 DU: " +
                              peak.ToString("F4") + " at its peak, " + peakAt + " nm; " + SunLight2.OzoneOpticalDepth(500, 300).ToString("F4") + " at 500 nm, " +
                              SunLight2.OzoneOpticalDepth(700, 300).ToString("F4") + " at 700 nm");
            CHECK(peak > 0.035 && peak < 0.047 && peakAt > 560 && peakAt < 615);
            EXPECT_NEAR(SunLight2.OzoneOpticalDepth(592, 300), Math.Log(SunLight2.AtmosphericTransmittance(592, 1, 1, 0) / SunLight2.AtmosphericTransmittance(592, 1, 1, 300)), 1e-6);
            EXPECT_NEAR(1.5 * SunLight2.OzoneOpticalDepth(592, 300), SunLight2.OzoneOpticalDepth(592, 450), 1e-12);
            // (the air mass at the horizon: 38 of Kasten and Young)
            EXPECT_NEAR(37.92, Math.Log(SunLight2.AtmosphericTransmittance(700, 0, 1, 0)) / Math.Log(SunLight2.AtmosphericTransmittance(700, 1, 1, 0)), 0.05);

            foreach (double turbidity in new double[] { 1, 2, 3, 6 })
            {
                string line = "turbidity " + turbidity + ":";
                double lastLux = double.MaxValue, lastRatio = double.MaxValue;
                foreach (double elevation in new double[] { 90, 60, 30, 10, 5, 2, 0 })
                {
                    double cosZenith = Math.Sin(elevation * Math.PI / 180);
                    double lux = 0;
                    for (int lambda = 360; lambda <= 830; ++lambda)
                    {
                        lux += 683.002 * Spectra.Y().Evaluate(lambda) * SunLight2.SolarIrradiance(lambda) *
                               SunLight2.AtmosphericTransmittance(lambda, cosZenith, turbidity, 300);
                    }
                    double ratio = SunLight2.AtmosphericTransmittance(450, cosZenith, turbidity, 300) / SunLight2.AtmosphericTransmittance(650, cosZenith, turbidity, 300);
                    line += " " + elevation + " deg " + lux.ToString("F0") + " lux (blue/red " + ratio.ToString("F3") + ")";
                    CHECK_LT(lux, lastLux);
                    CHECK_LT(ratio, lastRatio);
                    lastLux = lux;
                    lastRatio = ratio;
                    if (turbidity == 2 && elevation == 90)
                    {
                        // (the direct light of the sun overhead on a clear day: about a hundred thousand lux)
                        CHECK(lux > 95000 && lux < 112000);
                    }
                }
                Console.WriteLine(line);
            }

            // The disc of a sun whose centre is 1 degree over the horizon: its lower edge is darker than its upper edge
            double elevationRadians = Math.PI / 180;
            Vector3f toSun = new Vector3f((Float)Math.Cos(elevationRadians), 0, (Float)Math.Sin(elevationRadians));
            SunLight2 low = new SunLight2(Toward(toSun), null, 683.002f, 0.5334f, 0, true, 3, 300, new Vector3f(0, 0, 1));
            double edge = 0.24 * Math.PI / 180;
            Vector3f upper = new Vector3f((Float)Math.Cos(elevationRadians + edge), 0, (Float)Math.Sin(elevationRadians + edge));
            Vector3f lower = new Vector3f((Float)Math.Cos(elevationRadians - edge), 0, (Float)Math.Sin(elevationRadians - edge));
            SampledWavelengths wavelengths = SampledWavelengths.SampleUniform(0.5f);
            SampledSpectrum top = low.Le(new Ray(new Point3f(0, 0, 0), upper), wavelengths), bottom = low.Le(new Ray(new Point3f(0, 0, 0), lower), wavelengths);
            Console.WriteLine("sun at 1 degree, turbidity 3, " + wavelengths[0].ToString("F0") + " nm: lower edge over upper edge " + (bottom[0] / top[0]).ToString("F3"));
            CHECK(top[0] > 0 && bottom[0] > 0 && bottom[0] < 0.8 * top[0]);
        }
    }
}
