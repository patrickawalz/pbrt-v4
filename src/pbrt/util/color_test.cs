// color_test.cpp

using System;
using testing;

namespace pbrt
{
    public static unsafe class ColorTests
    {
        // RGB(a + b * rng.Uniform<Float>(), ...): gcc evaluates the constructor arguments right to left.
        private static RGB RandomRGB(ref RNG rng, Float offset, Float scale)
        {
            Float b = offset + scale * rng.Uniform<Float>();
            Float g = offset + scale * rng.Uniform<Float>();
            Float r = offset + scale * rng.Uniform<Float>();
            return new RGB(r, g, b);
        }

        [TEST("RGBColorSpace", "RGBXYZ")]
        public static void RGBColorSpace_RGBXYZ()
        {
            foreach (RGBColorSpace cs in new RGBColorSpace[] { RGBColorSpace.ACES2065_1, RGBColorSpace.Rec2020, RGBColorSpace.sRGB })
            {
                XYZ xyz = cs.ToXYZ(new RGB(1, 1, 1));
                RGB rgb = cs.ToRGB(xyz);
                EXPECT_LT(std.abs(1 - rgb[0]), 1e-4);
                EXPECT_LT(std.abs(1 - rgb[1]), 1e-4);
                EXPECT_LT(std.abs(1 - rgb[2]), 1e-4);
            }
        }

        [TEST("RGBColorSpace", "sRGB")]
        public static void RGBColorSpace_sRGB()
        {
            RGBColorSpace sRGB = RGBColorSpace.sRGB;

            // Make sure the matrix values are sensible by throwing the x, y, and z
            // basis vectors at it to pull out columns.
            RGB rgb = sRGB.ToRGB(new XYZ(1, 0, 0));
            EXPECT_LT(std.abs(3.2406 - rgb[0]), 1e-3);
            EXPECT_LT(std.abs(-.9689 - rgb[1]), 1e-3);
            EXPECT_LT(std.abs(.0557 - rgb[2]), 1e-3);

            rgb = sRGB.ToRGB(new XYZ(0, 1, 0));
            EXPECT_LT(std.abs(-1.5372 - rgb[0]), 1e-3);
            EXPECT_LT(std.abs(1.8758 - rgb[1]), 1e-3);
            EXPECT_LT(std.abs(-.2040 - rgb[2]), 1e-3);

            rgb = sRGB.ToRGB(new XYZ(0, 0, 1));
            EXPECT_LT(std.abs(-.4986 - rgb[0]), 1e-3);
            EXPECT_LT(std.abs(.0415 - rgb[1]), 1e-3);
            EXPECT_LT(std.abs(1.0570 - rgb[2]), 1e-3);
        }

        private static void CheckWhite(RGBColorSpace cs)
        {
            XYZ xyz = SpectrumToXYZ(cs.illuminant);
            RGB rgb = cs.ToRGB(xyz);
            EXPECT_GE(rgb.r, .99);
            EXPECT_LE(rgb.r, 1.01);
            EXPECT_GE(rgb.g, .99);
            EXPECT_LE(rgb.g, 1.01);
            EXPECT_GE(rgb.b, .99);
            EXPECT_LE(rgb.b, 1.01);
        }

        [TEST("RGBColorSpace", "StdIllumWhitesRGB")]
        public static void RGBColorSpace_StdIllumWhitesRGB() { CheckWhite(RGBColorSpace.sRGB); }

        [TEST("RGBColorSpace", "StdIllumWhiteRec2020")]
        public static void RGBColorSpace_StdIllumWhiteRec2020() { CheckWhite(RGBColorSpace.Rec2020); }

        [TEST("RGBColorSpace", "StdIllumWhiteACES2065_1")]
        public static void RGBColorSpace_StdIllumWhiteACES2065_1() { CheckWhite(RGBColorSpace.ACES2065_1); }

        [TEST("RGBUnboundedSpectrum", "MaxValue")]
        public static void RGBUnboundedSpectrum_MaxValue()
        {
            RNG rng = new RNG();
            foreach (RGBColorSpace cs in new RGBColorSpace[] { RGBColorSpace.sRGB, RGBColorSpace.Rec2020, RGBColorSpace.ACES2065_1 })
            {
                for (int i = 0; i < 100; ++i)
                {
                    RGB rgb = RandomRGB(ref rng, 0, 1);
                    rgb *= 10f;
                    RGBUnboundedSpectrum rs = new RGBUnboundedSpectrum(cs, rgb);

                    Float m = rs.MaxValue();
                    Float sm = 0;
                    for (Float lambda = 360; lambda <= 830; lambda += 1f / 16f)
                        sm = std.max(sm, rs.Apply(lambda));
                    EXPECT_LT(std.abs((sm - m) / sm), 1e-4,
                              "sampled " + sm + " MaxValue " + m + " for " + rs);
                }
            }
        }

        [TEST("RGBAlbedoSpectrum", "MaxValue")]
        public static void RGBAlbedoSpectrum_MaxValue()
        {
            RNG rng = new RNG();
            foreach (RGBColorSpace cs in new RGBColorSpace[] { RGBColorSpace.sRGB, RGBColorSpace.Rec2020, RGBColorSpace.ACES2065_1 })
            {
                for (int i = 0; i < 100; ++i)
                {
                    RGB rgb = RandomRGB(ref rng, 0, 1);
                    RGBAlbedoSpectrum rs = new RGBAlbedoSpectrum(cs, rgb);

                    Float m = rs.MaxValue();
                    Float sm = 0;
                    for (Float lambda = 360; lambda <= 830; lambda += 1f / 16f)
                        sm = std.max(sm, rs.Apply(lambda));
                    EXPECT_LT(std.abs((sm - m) / sm), 1e-4,
                              "sampled " + sm + " MaxValue " + m + " for " + rs);
                }
            }
        }

        private static void RoundTripAlbedo(RGBColorSpace cs, Float offset, Float scale)
        {
            RNG rng = new RNG();
            for (int i = 0; i < 100; ++i)
            {
                RGB rgb = RandomRGB(ref rng, offset, scale);
                RGBAlbedoSpectrum rs = new RGBAlbedoSpectrum(cs, rgb);

                DenselySampledSpectrum rsIllum = DenselySampledSpectrum.SampleFunction(
                    (Float lambda) => rs.Apply(lambda) * cs.illuminant.Apply(lambda));
                XYZ xyz = SpectrumToXYZ(rsIllum);
                RGB rgb2 = cs.ToRGB(xyz);

                // Some error comes from the fact that piecewise linear (at 5nm)
                // CIE curves were used for the optimization while we use piecewise
                // linear at 1nm spacing converted to 1nm constant / densely
                // sampled.
                Float eps = .01f;
                EXPECT_LT(std.abs(rgb.r - rgb2.r), eps, rgb + " vs " + rgb2 + " xyz " + xyz);
                EXPECT_LT(std.abs(rgb.g - rgb2.g), eps, rgb + " vs " + rgb2 + " xyz " + xyz);
                EXPECT_LT(std.abs(rgb.b - rgb2.b), eps, rgb + " vs " + rgb2 + " xyz " + xyz);
            }
        }

        [TEST("RGBAlbedoSpectrum", "RoundTripsRGB")]
        public static void RGBAlbedoSpectrum_RoundTripsRGB() { RoundTripAlbedo(RGBColorSpace.sRGB, 0, 1); }

        [TEST("RGBAlbedoSpectrum", "RoundTripRec2020")]
        public static void RGBAlbedoSpectrum_RoundTripRec2020() { RoundTripAlbedo(RGBColorSpace.Rec2020, .1f, .7f); }

        [TEST("RGBAlbedoSpectrum", "RoundTripACES")]
        public static void RGBAlbedoSpectrum_RoundTripACES() { RoundTripAlbedo(RGBColorSpace.ACES2065_1, .3f, .4f); }

        private static void RoundTripIlluminant(RGBColorSpace cs, Float offset, Float scale)
        {
            RNG rng = new RNG();
            for (int i = 0; i < 100; ++i)
            {
                RGB rgb = RandomRGB(ref rng, offset, scale);
                RGBIlluminantSpectrum rs = new RGBIlluminantSpectrum(cs, rgb);

                DenselySampledSpectrum rsIllum = DenselySampledSpectrum.SampleFunction(
                    (Float lambda) => rs.Apply(lambda));
                XYZ xyz = SpectrumToXYZ(rsIllum);
                RGB rgb2 = cs.ToRGB(xyz);

                Float eps = .01f;
                EXPECT_LT(std.abs(rgb.r - rgb2.r), eps, rgb + " vs " + rgb2 + " xyz " + xyz);
                EXPECT_LT(std.abs(rgb.g - rgb2.g), eps, rgb + " vs " + rgb2 + " xyz " + xyz);
                EXPECT_LT(std.abs(rgb.b - rgb2.b), eps, rgb + " vs " + rgb2 + " xyz " + xyz);
            }
        }

        [TEST("RGBIlluminantSpectrum", "RoundTripsRGB")]
        public static void RGBIlluminantSpectrum_RoundTripsRGB() { RoundTripIlluminant(RGBColorSpace.sRGB, 0, 1); }

        [TEST("RGBIlluminantSpectrum", "RoundTripRec2020")]
        public static void RGBIlluminantSpectrum_RoundTripRec2020() { RoundTripIlluminant(RGBColorSpace.Rec2020, .1f, .7f); }

        [TEST("RGBIlluminantSpectrum", "RoundTripACES")]
        public static void RGBIlluminantSpectrum_RoundTripACES() { RoundTripIlluminant(RGBColorSpace.ACES2065_1, .3f, .4f); }

        [TEST("sRGB", "Conversion")]
        public static void sRGB_Conversion()
        {
            // Check the basic 8 bit values
            for (int i = 0; i < 256; ++i)
            {
                Float x = SRGBToLinear(i * (1f / 255f));
                Float y = SRGB8ToLinear((byte)i);
                EXPECT_LT(std.abs(x - y), 1e-5);
            }

            // Round trip to linear and back
            for (int i = 0; i < 256; ++i)
            {
                Float x = SRGBToLinear(i * (1f / 255f));
                Float y = LinearToSRGB(x) * 255f;
                EXPECT_LT(std.abs(i - y), 1e-4);
            }

            // Round trip the other way
            for (int i = 0; i < 256; ++i)
            {
                Float x = LinearToSRGB(i * (1f / 255f));
                Float y = SRGBToLinear(x) * 255f;
                EXPECT_LT(std.abs(i - y), 3e-4, StringPrintf("i = %d -> linear %f -> srgb %f", i, x, y));
            }
        }
    }
}
