// (not in pbrt-v4) polarised_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class PolarisedTests
    {
        static StokesLight Horizontal(Float I)
        {
            return new StokesLight(new SampledSpectrum(I), new SampledSpectrum(I), new SampledSpectrum(0.0f), new SampledSpectrum(0.0f));
        }

        [TEST("Polarised", "ScalarAndIdentity")]
        public static void Polarised_ScalarAndIdentity()
        {
            StokesLight s = new StokesLight(new SampledSpectrum(2.0f), new SampledSpectrum(0.5f), new SampledSpectrum(-0.25f),
                                            new SampledSpectrum(0.1f));
            StokesLight id = MuellerAttenuation.Identity() * s;
            StokesLight sc = MuellerAttenuation.Scalar(new SampledSpectrum(0.5f)) * s;
            for (int c = 0; c < 4; ++c)
            {
                EXPECT_EQ(s[c][0], id[c][0]);
                EXPECT_EQ(0.5f * s[c][0], sc[c][0]);
            }
            // Unpolarised light stays unpolarised under a scalar
            StokesLight u = MuellerAttenuation.Scalar(new SampledSpectrum(0.25f)) * StokesLight.Unpolarised(new SampledSpectrum(4.0f));
            EXPECT_EQ(1.0f, u.Radiance()[0]);
            EXPECT_EQ(0.0f, u.DegreeOfPolarisation(0));
        }

        [TEST("Polarised", "Malus")]
        public static void Polarised_Malus()
        {
            // Horizontally polarised light of radiance 1 through a polariser at an angle: I cos^2(angle)
            for (int i = 0; i <= 8; ++i)
            {
                Float angle = i * Pi / 16;
                StokesLight o = MuellerAttenuation.LinearPolariser(angle) * Horizontal(1);
                EXPECT_LT(std.abs(o.Radiance()[0] - std.cos(angle) * std.cos(angle)), (Float)1e-5);
            }
            // Unpolarised light loses half
            StokesLight h = MuellerAttenuation.LinearPolariser(0.3f) * StokesLight.Unpolarised(new SampledSpectrum(1.0f));
            EXPECT_LT(std.abs(h.Radiance()[0] - 0.5f), (Float)1e-6);
            EXPECT_LT(std.abs(h.DegreeOfPolarisation(0) - 1), (Float)1e-5);
        }

        [TEST("Polarised", "Rotator")]
        public static void Polarised_Rotator()
        {
            // A rotation keeps the radiance and the degree of polarisation, and two compose by adding the angles
            StokesLight s = new StokesLight(new SampledSpectrum(1.0f), new SampledSpectrum(0.3f), new SampledSpectrum(0.4f),
                                            new SampledSpectrum(0.2f));
            MuellerAttenuation r1 = MuellerAttenuation.Rotator(0.4f), r2 = MuellerAttenuation.Rotator(0.7f),
                               r12 = MuellerAttenuation.Rotator(1.1f);
            StokesLight a = r2 * (r1 * s), b = r12 * s;
            for (int c = 0; c < 4; ++c)
                EXPECT_LT(std.abs(a[c][0] - b[c][0]), (Float)1e-5);
            EXPECT_LT(std.abs(a.DegreeOfPolarisation(0) - s.DegreeOfPolarisation(0)), (Float)1e-5);
            // A product of matrices is applied right to left
            MuellerAttenuation prod = r2 * r1;
            StokesLight c2 = prod * s;
            for (int c = 0; c < 4; ++c)
                EXPECT_LT(std.abs(c2[c][0] - b[c][0]), (Float)1e-5);
        }

        [TEST("Polarised", "Rayleigh")]
        public static void Polarised_Rayleigh()
        {
            // The first element is the Rayleigh phase function: it integrates to one over the sphere
            int n = 20000;
            double sum = 0;
            for (int i = 0; i < n; ++i)
            {
                Float cosTheta = -1 + 2 * (i + 0.5f) / n;
                sum += MuellerAttenuation.Rayleigh(cosTheta).UnpolarisedGain()[0] * 2 * Math.PI * (2.0 / n);
            }
            EXPECT_LT(Math.Abs(sum - 1), 1e-3);
            // Unpolarised light scattered at 90 degrees becomes fully polarised
            StokesLight o = MuellerAttenuation.Rayleigh(0) * StokesLight.Unpolarised(new SampledSpectrum(1.0f));
            EXPECT_LT(std.abs(o.DegreeOfPolarisation(0) - 1), (Float)1e-5);
            // and forward scattering of unpolarised light stays unpolarised
            StokesLight f = MuellerAttenuation.Rayleigh(1) * StokesLight.Unpolarised(new SampledSpectrum(1.0f));
            EXPECT_LT(f.DegreeOfPolarisation(0), (Float)1e-5);
        }
    }
}
