// (not in OpenShadingLanguage) opcolor_test.cs
//
// Colour shadeop checks. The bit patterns come from the C++ ColorSystem (opcolor.cpp / opcolor_impl.h) built from the OSL sources.

using System;
using OIIO;
using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static unsafe class OpColorTests
        {
            private static uint Bits(float f)
            {
                return BitConverter.SingleToUInt32Bits(f);
            }

            private static ulong H(string s)
            {
                return new ustring(s).hash();
            }

            private static void ExpectColor(Color3 c, uint x, uint y, uint z)
            {
                EXPECT_EQ(Bits(c.x), x);
                EXPECT_EQ(Bits(c.y), y);
                EXPECT_EQ(Bits(c.z), z);
            }

            [TEST("OpColor", "TransformcMatchesCxx")]
            public static void OpColor_TransformcMatchesCxx()
            {
                ColorSystem cs = new ColorSystem(Hashes.Rec709);
                Color3 rgb = new Color3(0.8f, 0.4f, 0.2f);
                ExpectColor(cs.transformc(H("rgb"), H("hsv"), rgb), 0x3d638e39u, 0x3f400000u, 0x3f4ccccdu);
                ExpectColor(cs.transformc(H("rgb"), H("hsl"), rgb), 0x3d638e39u, 0x3f19999au, 0x3f000000u);
                ExpectColor(cs.transformc(H("rgb"), H("YIQ"), rgb), 0x3efe5c92u, 0x3e9aee63u, 0x3cb9239au);
                ExpectColor(cs.transformc(H("rgb"), H("XYZ"), rgb), 0x3f0243f8u, 0x3ef0ef20u, 0x3e81990eu);
                ExpectColor(cs.transformc(H("rgb"), H("xyY"), rgb), 0x3ed36061u, 0x3ec37a18u, 0x3ef0ef20u);
                ExpectColor(cs.transformc(H("rgb"), H("sRGB"), rgb), 0x3f68055bu, 0x3f2a4991u, 0x3ef81436u);

                Color3 c = new Color3(0.1f, 0.7f, 0.9f);
                ExpectColor(cs.transformc(H("hsv"), H("rgb"), c), 0x3f666666u, 0x3f25e354u, 0x3e8a3d71u);
                ExpectColor(cs.transformc(H("hsl"), H("rgb"), c), 0x3f7851ecu, 0x3f69fbe7u, 0x3f547ae0u);
                ExpectColor(cs.transformc(H("YIQ"), H("rgb"), c), 0x3fa9d7dcu, 0xbf2c1dd2u, 0x3f5bdd99u);
                ExpectColor(cs.transformc(H("XYZ"), H("rgb"), c), 0xbf99cce6u, 0x3fa06d44u, 0x3f508a7eu);
                ExpectColor(cs.transformc(H("xyY"), H("rgb"), c), 0xbf8c448cu, 0x3fc97a24u, 0x3dc37192u);
                ExpectColor(cs.transformc(H("sRGB"), H("rgb"), c), 0x3c2436c7u, 0x3ee55ebdu, 0x3f4993dbu);
            }

            [TEST("OpColor", "TransformcWithDerivativesMatchesCxx")]
            public static void OpColor_TransformcWithDerivativesMatchesCxx()
            {
                ColorSystem cs = new ColorSystem(Hashes.Rec709);
                Dual2v3 d = new Dual2v3(new Vec3(0.8f, 0.4f, 0.2f), new Vec3(0.1f, -0.2f, 0.3f), new Vec3(-0.05f, 0.15f, 0.25f));
                Dual2v3 hsv = cs.transformc(H("rgb"), H("hsv"), d);
                uint[] expectedHsv = new uint[] { 0x3d638e39u, 0x3f400000u, 0x3f4ccccdu, 0xbdf684bdu, 0xbeb00002u, 0x3dcccccdu, 0x310e38e3u, 0xbea80001u, 0xbd4ccccdu };
                float[] got = new float[] { hsv.x.x, hsv.x.y, hsv.x.z, hsv.dx.x, hsv.dx.y, hsv.dx.z, hsv.dy.x, hsv.dy.y, hsv.dy.z };
                for (int i = 0; i < 9; ++i)
                {
                    EXPECT_EQ(Bits(got[i]), expectedHsv[i]);
                }
                Dual2v3 srgb = cs.transformc(H("rgb"), H("sRGB"), d);
                uint[] expectedSrgb = new uint[] { 0x3f68055bu, 0x3f2a4992u, 0x3ef81436u, 0x3d4d1587u, 0xbe19a3b5u, 0x3eaca63cu, 0xbccd1587u, 0x3de67590u, 0x3e8fdfddu };
                got = new float[] { srgb.x.x, srgb.x.y, srgb.x.z, srgb.dx.x, srgb.dx.y, srgb.dx.z, srgb.dy.x, srgb.dy.y, srgb.dy.z };
                for (int i = 0; i < 9; ++i)
                {
                    EXPECT_EQ(Bits(got[i]), expectedSrgb[i]);
                }
            }

            [TEST("OpColor", "BlackbodyMatchesCxx")]
            public static void OpColor_BlackbodyMatchesCxx()
            {
                ColorSystem cs = new ColorSystem(Hashes.Rec709);
                ExpectColor(cs.blackbody_rgb(500.0f), 0x358637bdu, 0x00000000u, 0x00000000u);
                ExpectColor(cs.blackbody_rgb(1000.0f), 0x3d696805u, 0x39fad0bdu, 0x00000000u);
                ExpectColor(cs.blackbody_rgb(2000.0f), 0x45a817e5u, 0x44ac6bb9u, 0x422de630u);
                ExpectColor(cs.blackbody_rgb(6500.0f), 0x4b611e1du, 0x4b540c80u, 0x4b5f4047u);
                ExpectColor(cs.blackbody_rgb(12000.0f), 0x4c9b9135u, 0x4cbb0b4eu, 0x4d1362cau);
                ExpectColor(cs.blackbody_rgb(15000.0f), 0x4cf60316u, 0x4d1b6496u, 0x4d858b53u);
            }

            [TEST("OpColor", "WavelengthColorMatchesCxx")]
            public static void OpColor_WavelengthColorMatchesCxx()
            {
                float* rgb = stackalloc float[3];
                float[] lambdas = new float[] { 350.0f, 450.0f, 550.0f, 650.0f, 780.0f, 800.0f };
                uint[][] expected = new uint[][]
                {
                    new uint[] { 0x00000000u, 0x00000000u, 0x00000000u },
                    new uint[] { 0x3d7010dfu, 0x00000000u, 0x3f3f85f0u },
                    new uint[] { 0x00000000u, 0x3f12f27fu, 0x00000000u },
                    new uint[] { 0x3e995a48u, 0x00000000u, 0x00000000u },
                    new uint[] { 0x00000000u, 0x00000000u, 0x00000000u },
                    new uint[] { 0x00000000u, 0x00000000u, 0x00000000u },
                };
                for (int i = 0; i < lambdas.Length; ++i)
                {
                    OslRuntime.osl_wavelength_color_vf(null, rgb, lambdas[i]);
                    EXPECT_EQ(Bits(rgb[0]), expected[i][0]);
                    EXPECT_EQ(Bits(rgb[1]), expected[i][1]);
                    EXPECT_EQ(Bits(rgb[2]), expected[i][2]);
                }
            }

            [TEST("OpColor", "LuminanceMatchesCxx")]
            public static void OpColor_LuminanceMatchesCxx()
            {
                float* c = stackalloc float[3];
                c[0] = 0.2f;
                c[1] = 0.5f;
                c[2] = 0.8f;
                float lum;
                OslRuntime.osl_luminance_fv(null, &lum, c);
                EXPECT_EQ(Bits(lum), 0x3eea70a3u);
            }

            [TEST("OpColor", "ColorSystemsMatchCxx")]
            public static void OpColor_ColorSystemsMatchCxx()
            {
                ColorSystem acescg = new ColorSystem(Hashes.ACEScg);
                ExpectColor(acescg.XYZ_to_RGB(new Color3(0.3f, 0.5f, 0.2f)), 0x3e90b3a1u, 0x3f1ca6adu, 0x3e49c859u);
                ExpectColor(acescg.luminance_scale(), 0x3e8b6190u, 0x3f2c909eu, 0x3d5be9a0u);
            }

            [TEST("OpColor", "UnknownColorspaceIsReportedAndPassedThrough")]
            public static void OpColor_UnknownColorspaceIsReportedAndPassedThrough()
            {
                ColorSystem cs = new ColorSystem(Hashes.Rec709);
                int reported = 0;
                Action<ulong, ulong> previous = OslRuntime.unknown_colorspace_hook;
                OslRuntime.unknown_colorspace_hook = (ulong from, ulong to) => { ++reported; };
                Color3 c = new Color3(0.25f, 0.5f, 0.75f);
                Color3 result = cs.transformc(H("rgb"), H("ACEScct"), c);
                OslRuntime.unknown_colorspace_hook = previous;
                EXPECT_EQ(reported, 1);
                EXPECT_TRUE(result == c);
            }

            [TEST("OpColor", "HsvRoundTrips")]
            public static void OpColor_HsvRoundTrips()
            {
                ColorSystem cs = new ColorSystem(Hashes.Rec709);
                for (int r = 0; r <= 4; ++r)
                {
                    for (int g = 0; g <= 4; ++g)
                    {
                        for (int b = 0; b <= 4; ++b)
                        {
                            Color3 c = new Color3(r * 0.25f, g * 0.25f, b * 0.25f);
                            Color3 hsv = cs.transformc(H("rgb"), H("hsv"), c);
                            Color3 hsl = cs.transformc(H("rgb"), H("hsl"), c);
                            Color3 back = cs.transformc(H("hsv"), H("rgb"), hsv);
                            Color3 backl = cs.transformc(H("hsl"), H("rgb"), hsl);
                            EXPECT_NEAR(back.x, c.x, 1e-5);
                            EXPECT_NEAR(back.y, c.y, 1e-5);
                            EXPECT_NEAR(back.z, c.z, 1e-5);
                            EXPECT_NEAR(backl.x, c.x, 1e-5);
                            EXPECT_NEAR(backl.y, c.y, 1e-5);
                            EXPECT_NEAR(backl.z, c.z, 1e-5);
                        }
                    }
                }
            }

            [TEST("OpColor", "WhiteHasUnitLuminanceAndXyzRoundTrips")]
            public static void OpColor_WhiteHasUnitLuminanceAndXyzRoundTrips()
            {
                string[] names = new string[] { "Rec709", "sRGB", "NTSC", "EBU", "PAL", "SECAM", "SMPTE", "HDTV", "AdobeRGB", "ACEScg" };
                foreach (string name in names)
                {
                    ColorSystem cs = new ColorSystem(H(name));
                    EXPECT_NEAR(cs.luminance(new Color3(1.0f, 1.0f, 1.0f)), 1.0, 1e-5);
                    Color3 c = new Color3(0.3f, 0.6f, 0.9f);
                    Color3 back = cs.XYZ_to_RGB(cs.RGB_to_XYZ(c));
                    EXPECT_NEAR(back.x, c.x, 1e-4);
                    EXPECT_NEAR(back.y, c.y, 1e-4);
                    EXPECT_NEAR(back.z, c.z, 1e-4);
                }
            }

            [TEST("OpColor", "BlackbodyIsRedderWhenCooler")]
            public static void OpColor_BlackbodyIsRedderWhenCooler()
            {
                ColorSystem cs = new ColorSystem(Hashes.Rec709);
                Color3 warm = cs.blackbody_rgb(2000.0f);
                Color3 cool = cs.blackbody_rgb(10000.0f);
                EXPECT_TRUE(warm.x > warm.y && warm.y > warm.z);
                EXPECT_TRUE(cool.z > cool.x);
                EXPECT_TRUE(cs.blackbody_rgb(100.0f).y == 0.0f);
            }
        }
    }
}
