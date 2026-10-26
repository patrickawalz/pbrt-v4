// spectrum_test.cpp

using System;
using testing;

namespace pbrt
{
    public static unsafe class SpectrumTests
    {
        [TEST("Spectrum", "Blackbody")]
        public static void Spectrum_Blackbody()
        {
            // Relative error.
            Func<Float, Float, Float> err = (Float val, Float refv) => std.abs(val - refv) / refv;

            // Planck's law.
            // A few values via
            // http://www.spectralcalc.com/blackbody_calculator/blackbody.php
            // lambda, T, expected radiance
            Float[][] v = {
                new Float[] { 483, 6000, 3.1849e13f },
                new Float[] { 600, 6000, 2.86772e13f },
                new Float[] { 500, 3700, 1.59845e12f },
                new Float[] { 600, 4500, 7.46497e12f },
            };
            int n = v.Length;
            for (int i = 0; i < n; ++i)
            {
                Float lambda = v[i][0], T = v[i][1], LeExpected = v[i][2];
                EXPECT_LT(err(Blackbody(lambda, T), LeExpected), .001);
            }

            // Use Wien's displacement law to compute maximum wavelength for a few
            // temperatures, then confirm that the value returned by Blackbody() is
            // consistent with this.
            foreach (Float T in new Float[] { 2700, 3000, 4500, 5600, 6000 })
            {
                Float lambdaMax = (Float)(2.8977721e-3 / T * 1e9);
                Float[] lambda = { (Float)(.99 * lambdaMax), lambdaMax, (Float)(1.01 * lambdaMax) };
                EXPECT_LT(Blackbody(lambda[0], T), Blackbody(lambda[1], T));
                EXPECT_GT(Blackbody(lambda[1], T), Blackbody(lambda[2], T));
            }
        }

        [TEST("Spectrum", "XYZ")]
        public static void Spectrum_XYZ()
        {
            {
                // Make sure the integral of all matching function sample values is
                // basically one in x, y, and z.
                Float xx = 0, yy = 0, zz = 0;
                for (Float lambda = 360; lambda < 831; ++lambda)
                {
                    xx += Spectra.X().Apply(lambda);
                    yy += Spectra.Y().Apply(lambda);
                    zz += Spectra.Z().Apply(lambda);
                }
                const Float CIE_Y_integral = 106.856895f;
                xx /= CIE_Y_integral;
                yy /= CIE_Y_integral;
                zz /= CIE_Y_integral;
                EXPECT_LT(std.abs(1 - xx), .005, xx.ToString());
                EXPECT_LT(std.abs(1 - yy), .005, yy.ToString());
                EXPECT_LT(std.abs(1 - zz), .005, zz.ToString());
            }
            {
                // Make sure the xyz of a constant spectrum are basically one.
                Float[] xyzSum = new Float[3];
                int n = 100;
                foreach (Float u in new Stratified1D(n))
                {
                    SampledWavelengths lambda = SampledWavelengths.SampleUniform(u, 360, 830);
                    XYZ xyz = new SampledSpectrum(1f).ToXYZ(lambda);
                    for (int c = 0; c < 3; ++c)
                        xyzSum[c] += xyz[c];
                }
                for (int c = 0; c < 3; ++c)
                    xyzSum[c] /= n;

                EXPECT_LT(std.abs(1 - xyzSum[0]), .035, xyzSum[0].ToString());
                EXPECT_LT(std.abs(1 - xyzSum[1]), .035, xyzSum[1].ToString());
                EXPECT_LT(std.abs(1 - xyzSum[2]), .035, xyzSum[2].ToString());
            }
        }

        [TEST("Spectrum", "MaxValue")]
        public static void Spectrum_MaxValue()
        {
            EXPECT_EQ(2.5, new ConstantSpectrum(2.5f).MaxValue());

            EXPECT_EQ((Float)10.1,
                      new PiecewiseLinearSpectrum(
                          new Float[] { 300, 380, 510, 662, 700 },
                          new Float[] { 1.5f, 2.6f, 10.1f, 5.3f, 7.7f })
                          .MaxValue());

            EXPECT_GT(new BlackbodySpectrum(5000).MaxValue(), .9999);
            EXPECT_LT(new BlackbodySpectrum(5000).MaxValue(), 1.0001);

            BlackbodySpectrum bb = new BlackbodySpectrum(5000);
            EXPECT_GT(new DenselySampledSpectrum(bb).MaxValue(), .9999);
            EXPECT_LT(new DenselySampledSpectrum(bb).MaxValue(), 1.0001);

            RNG rng = new RNG();
            for (int i = 0; i < 20; ++i)
            {
                Float b = rng.Uniform<Float>(), g = rng.Uniform<Float>(), r = rng.Uniform<Float>();
                RGB rgb = new RGB(r, g, b);
                RGBAlbedoSpectrum sr = new RGBAlbedoSpectrum(RGBColorSpace.sRGB, rgb);
                Float m = sr.MaxValue() * 1.00001f;
                for (Float lambda = 360; lambda < 830; lambda += .92f)
                    EXPECT_LE(sr.Apply(lambda), m);

                RGBUnboundedSpectrum su = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, 10f * rgb);
                m = su.MaxValue() * 1.00001f;
                for (Float lambda = 360; lambda < 830; lambda += .92f)
                    EXPECT_LE(su.Apply(lambda), m);

                RGBIlluminantSpectrum si = new RGBIlluminantSpectrum(RGBColorSpace.sRGB, rgb);
                m = si.MaxValue() * 1.00001f;
                for (Float lambda = 360; lambda < 830; lambda += .92f)
                    EXPECT_LE(si.Apply(lambda), m);
            }
        }

        [TEST("Spectrum", "SamplingPdfY")]
        public static void Spectrum_SamplingPdfY()
        {
            // Make sure we can integrate the y matching curve correctly
            Float ysum = 0;
            int n = 1000;
            foreach (Float u in new Stratified1D(n))
            {
                Float lambda = SampleVisibleWavelengths(u);
                Float pdf = VisibleWavelengthsPDF(lambda);
                if (pdf > 0)
                    ysum += Spectra.Y().Apply(lambda) / pdf;
            }
            Float yint = ysum / n;

            EXPECT_LT(std.abs((yint - CIE_Y_integral) / CIE_Y_integral), 1e-3,
                      yint + " vs. " + CIE_Y_integral);
        }

        [TEST("Spectrum", "SamplingPdfXYZ")]
        public static void Spectrum_SamplingPdfXYZ()
        {
            // Make sure we can integrate the sum of the x+y+z matching curves correctly
            Float impSum = 0, unifSum = 0;
            int n = 10000;
            foreach (Float u in new Stratified1D(n))
            {
                {
                    // Uniform
                    Float lambdaU = Lerp(u, Lambda_min, Lambda_max);
                    Float pdfU = 1f / (Lambda_max - Lambda_min);
                    unifSum +=
                        (Spectra.X().Apply(lambdaU) + Spectra.Y().Apply(lambdaU) + Spectra.Z().Apply(lambdaU)) /
                        pdfU;
                }

                Float lambda = SampleVisibleWavelengths(u);
                Float pdf = VisibleWavelengthsPDF(lambda);
                if (pdf > 0)
                    impSum +=
                        (Spectra.X().Apply(lambda) + Spectra.Y().Apply(lambda) + Spectra.Z().Apply(lambda)) /
                        pdf;
            }
            Float impInt = impSum / n, unifInt = unifSum / n;

            EXPECT_LT(std.abs((impInt - unifInt) / unifInt), 1e-3,
                      impInt + " vs. " + unifInt);
        }
    }
}
