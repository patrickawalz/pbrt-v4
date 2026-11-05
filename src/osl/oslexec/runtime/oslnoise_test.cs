// (not in OpenShadingLanguage) oslnoise_test.cs
//
// Property tests of the noise shadeops. The bit-for-bit checks against the C++ are in opnoise_test.cs.

using System;
using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static unsafe class OslNoiseTests
        {
            [TEST("OslNoise", "RangesAreRespected")]
            public static void OslNoise_RangesAreRespected()
            {
                for (int i = 0; i < 500; ++i)
                {
                    float x = i * 0.173f - 40.0f;
                    float y = i * 0.311f + 3.0f;
                    float u = OslRuntime.osl_noise_fff(x, y);
                    float s = OslRuntime.osl_snoise_fff(x, y);
                    float c = OslRuntime.osl_cellnoise_fff(x, y);
                    float h = OslRuntime.osl_hashnoise_fff(x, y);
                    EXPECT_TRUE(u >= 0.0f && u <= 1.0f);
                    EXPECT_TRUE(s >= -1.0f && s <= 1.0f);
                    EXPECT_TRUE(c >= 0.0f && c < 1.0f);
                    EXPECT_TRUE(h >= 0.0f && h < 1.0f);
                }
            }

            [TEST("OslNoise", "PerlinIsZeroOnTheLattice")]
            public static void OslNoise_PerlinIsZeroOnTheLattice()
            {
                for (int i = -5; i <= 5; ++i)
                {
                    EXPECT_NEAR(OslRuntime.osl_snoise_ff((float)i), 0.0, 1e-6);
                    EXPECT_NEAR(OslRuntime.osl_noise_ff((float)i), 0.5, 1e-6);
                }
            }

            [TEST("OslNoise", "CellnoiseIsConstantInACell")]
            public static void OslNoise_CellnoiseIsConstantInACell()
            {
                EXPECT_EQ(OslRuntime.osl_cellnoise_ff(0.1f), OslRuntime.osl_cellnoise_ff(0.9f));
                EXPECT_EQ(OslRuntime.osl_cellnoise_ff(-0.9f), OslRuntime.osl_cellnoise_ff(-0.1f));
                EXPECT_TRUE(OslRuntime.osl_cellnoise_ff(0.9f) != OslRuntime.osl_cellnoise_ff(1.1f));
            }

            [TEST("OslNoise", "PeriodicNoiseRepeats")]
            public static void OslNoise_PeriodicNoiseRepeats()
            {
                for (int i = 0; i < 50; ++i)
                {
                    float x = i * 0.29f;
                    EXPECT_NEAR(OslRuntime.osl_pnoise_fff(x, 3.0f), OslRuntime.osl_pnoise_fff(x + 3.0f, 3.0f), 1e-5);
                    EXPECT_NEAR(OslRuntime.osl_pnoise_fffff(x, 0.5f * i, 4.0f, 5.0f), OslRuntime.osl_pnoise_fffff(x + 4.0f, 0.5f * i + 5.0f, 4.0f, 5.0f), 1e-5);
                }
            }

            [TEST("OslNoise", "SimplexNoiseIsSmoothAndBounded")]
            public static void OslNoise_SimplexNoiseIsSmoothAndBounded()
            {
                for (int i = 0; i < 200; ++i)
                {
                    float x = i * 0.0731f;
                    float a = OslRuntime.osl_simplexnoise_ff(x);
                    float b = OslRuntime.osl_simplexnoise_ff(x + 1e-3f);
                    EXPECT_TRUE(a >= -1.0f && a <= 1.0f);
                    EXPECT_NEAR(a, b, 0.05);
                }
            }
        }
    }
}
