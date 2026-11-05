// (not in OpenShadingLanguage) gabornoise_test.cs
//
// Gabor noise checks. The bit patterns come from the C++ OSL::pvt::gabor functions (liboslnoise/gabornoise.cpp) built from the OSL sources.

using System;
using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static unsafe class GaborNoiseTests
        {
            private static uint Bits(float f)
            {
                return BitConverter.SingleToUInt32Bits(f);
            }

            private static NoiseParams Filtered()
            {
                NoiseParams opt = NoiseParams.create_default();
                opt.anisotropic = 1;
                opt.do_filter = 1;
                opt.direction = new Vec3(0.3f, 0.5f, 0.8f);
                opt.bandwidth = 1.0f;
                opt.impulses = 8.0f;
                return opt;
            }

            [TEST("GaborNoise", "MatchesCxx")]
            public static void GaborNoise_MatchesCxx()
            {
                NoiseParams opt = Filtered();
                Dual2f x = new Dual2f(0.3f, 0.5f, 0.0f);
                Dual2f y = new Dual2f(1.7f, 0.0f, 0.4f);
                Dual2f z = new Dual2f(2.2f, 0.1f, 0.1f);
                Dual2v3 P = DualMath.make_Vec3(x, y, z);

                Dual2f g = GaborNoise.gabor(P, &opt);
                EXPECT_EQ(Bits(g.x), 0x3ce251ecu);
                EXPECT_EQ(Bits(g.dx), 0xbd4dcb51u);
                EXPECT_EQ(Bits(g.dy), 0xbd8fa336u);

                Dual2v3 v = GaborNoise.gabor3(P, &opt);
                float[] got = new float[] { v.x.x, v.x.y, v.x.z, v.dx.x, v.dx.y, v.dx.z, v.dy.x, v.dy.y, v.dy.z };
                uint[] expected = new uint[] { 0x3ce251ecu, 0x3da68c72u, 0x3cd1b68cu, 0xbd4dcb51u, 0x3dadc6f3u, 0x3dbeedb6u, 0xbd8fa336u, 0x3e8f8a5bu, 0x3d8f9720u };
                for (int i = 0; i < 9; ++i)
                {
                    EXPECT_EQ(Bits(got[i]), expected[i]);
                }

                Dual2f pg = GaborNoise.pgabor(P, new Vec3(2.0f, 3.0f, 4.0f), &opt);
                EXPECT_EQ(Bits(pg.x), 0x3c5f58d1u);
                EXPECT_EQ(Bits(pg.dx), 0x3c9508cbu);
                EXPECT_EQ(Bits(pg.dy), 0x3d2a4960u);

                NoiseParams plain = opt;
                plain.do_filter = 0;
                plain.anisotropic = 0;
                Dual2f g2 = GaborNoise.gabor(x, y, &plain);
                EXPECT_EQ(Bits(g2.x), 0x3ed73a55u);
                EXPECT_EQ(Bits(g2.dx), 0xbd6f4d4au);
                EXPECT_EQ(Bits(g2.dy), 0x3f9019abu);
            }

            [TEST("GaborNoise", "IsBounded")]
            public static void GaborNoise_IsBounded()
            {
                NoiseParams opt = Filtered();
                opt.do_filter = 0;
                for (int i = 0; i < 20; ++i)
                {
                    Dual2f x = new Dual2f(0.37f * i, 0.0f, 0.0f);
                    Dual2f a = GaborNoise.pgabor(x, 4.0f, &opt);
                    Dual2f b = GaborNoise.gabor(x, &opt);
                    EXPECT_TRUE(MathF.Abs(a.x) < 4.0f);
                    EXPECT_TRUE(MathF.Abs(b.x) < 4.0f);
                }
            }

            [TEST("Matrix33f", "InverseUndoesTheMatrix")]
            public static void Matrix33f_InverseUndoesTheMatrix()
            {
                Matrix33f m = new Matrix33f(2.0f, 0.5f, 0.1f, 0.2f, 3.0f, 0.3f, 0.4f, 0.1f, 4.0f);
                Matrix33f p = m * m.inverse();
                EXPECT_NEAR(p.m00, 1.0, 1e-5);
                EXPECT_NEAR(p.m11, 1.0, 1e-5);
                EXPECT_NEAR(p.m22, 1.0, 1e-5);
                EXPECT_NEAR(p.m01, 0.0, 1e-5);
                EXPECT_NEAR(p.m12, 0.0, 1e-5);
                EXPECT_NEAR(p.m20, 0.0, 1e-5);
            }
        }
    }
}
