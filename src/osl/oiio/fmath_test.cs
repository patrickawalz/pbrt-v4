// (not in OpenShadingLanguage) fmath_test.cs
//
// OIIO fmath / hash checks. The expected bit patterns come from OIIO's own fmath.h and strutil.h compiled with -msse4.2 -ffp-contract=off.

using System;
using testing;
using static testing.Gtest;

namespace OIIO
{
    public static class FmathTests
    {
        private static uint Bits(float f)
        {
            return BitConverter.SingleToUInt32Bits(f);
        }

        [TEST("Fmath", "FastFunctionsMatchCxxBitForBit")]
        public static void Fmath_FastFunctionsMatchCxxBitForBit()
        {
            EXPECT_EQ(Bits(fmath.fast_sin(1.0f)), 0x3f576aa4u);
            EXPECT_EQ(Bits(fmath.fast_cos(2.5f)), 0xbf4d17c0u);
            EXPECT_EQ(Bits(fmath.fast_tan(0.7f)), 0x3f57a036u);
            EXPECT_EQ(Bits(fmath.fast_acos(0.3f)), 0x3fa20e7fu);
            EXPECT_EQ(Bits(fmath.fast_asin(0.3f)), 0x3e9c0570u);
            EXPECT_EQ(Bits(fmath.fast_atan(1.7f)), 0x3f850070u);
            EXPECT_EQ(Bits(fmath.fast_atan2(1.0f, 2.0f)), 0x3eed637fu);
            EXPECT_EQ(Bits(fmath.fast_log(5.0f)), 0x3fce020fu);
            EXPECT_EQ(Bits(fmath.fast_log2(5.0f)), 0x40149a78u);
            EXPECT_EQ(Bits(fmath.fast_log10(5.0f)), 0x3f32efb3u);
            EXPECT_EQ(Bits(fmath.fast_exp(1.5f)), 0x408f69f2u);
            EXPECT_EQ(Bits(fmath.fast_exp2(1.5f)), 0x403504e5u);
            EXPECT_EQ(Bits(fmath.fast_expm1(0.2f)), 0x3e62b6a0u);
            EXPECT_EQ(Bits(fmath.fast_sinh(0.8f)), 0x3f635aeau);
            EXPECT_EQ(Bits(fmath.fast_cosh(0.8f)), 0x3fab3108u);
            EXPECT_EQ(Bits(fmath.fast_tanh(0.8f)), 0x3f29fe44u);
            EXPECT_EQ(Bits(fmath.fast_cbrt(27.0f)), 0x40400003u);
            EXPECT_EQ(Bits(fmath.fast_erf(0.5f)), 0x3f053f7au);
            EXPECT_EQ(Bits(fmath.fast_erfc(0.5f)), 0x3ef5810cu);
            EXPECT_EQ(Bits(fmath.fast_safe_pow(2.0f, 3.5f)), 0x413504e5u);
        }

        [TEST("Fmath", "SafeFunctionsMatchCxxBitForBit")]
        public static void Fmath_SafeFunctionsMatchCxxBitForBit()
        {
            EXPECT_EQ(Bits(fmath.safe_pow(2.0f, 3.5f)), 0x413504f3u);
            EXPECT_EQ(Bits(fmath.safe_fmod(7.5f, -2.0f)), 0x3fc00000u);
            EXPECT_EQ(Bits(fmath.safe_log(0.0f)), 0xc2aeac50u);
            EXPECT_EQ(Bits(fmath.safe_acos(1.5f)), 0x00000000u);
        }

        [TEST("Fmath", "FloatToIntFollowsCvttss2si")]
        public static void Fmath_FloatToIntFollowsCvttss2si()
        {
            EXPECT_EQ(fmath.float_to_int(2.9f), 2);
            EXPECT_EQ(fmath.float_to_int(-2.9f), -2);
            EXPECT_EQ(fmath.float_to_int(float.PositiveInfinity), int.MinValue);
            EXPECT_EQ(fmath.float_to_int(float.NaN), int.MinValue);
            EXPECT_EQ(fmath.float_to_int(3e9f), int.MinValue);
            EXPECT_EQ(fmath.ifloor(-0.5f), -1);
            EXPECT_EQ(fmath.fast_rint(2.5f), 2);
            EXPECT_EQ(fmath.fast_rint(3.5f), 4);
        }

        [TEST("Fmath", "FastFunctionsAreCloseToLibm")]
        public static void Fmath_FastFunctionsAreCloseToLibm()
        {
            for (int i = -20; i <= 20; ++i)
            {
                float x = i * 0.15f;
                EXPECT_NEAR(fmath.fast_sin(x), MathF.Sin(x), 1e-5);
                EXPECT_NEAR(fmath.fast_cos(x), MathF.Cos(x), 1e-5);
                EXPECT_NEAR(fmath.fast_exp(x), MathF.Exp(x), 1e-4 * MathF.Exp(x));
                EXPECT_NEAR(fmath.fast_tanh(x), MathF.Tanh(x), 1e-5);
            }
        }

        [TEST("Hash", "Strhash64MatchesOiio")]
        public static void Hash_Strhash64MatchesOiio()
        {
            EXPECT_EQ(Strutil_hash.strhash64(""), 0x0000000000000000ul);
            EXPECT_EQ(Strutil_hash.strhash64("a"), 0xb3454265b6df75e3ul);
            EXPECT_EQ(Strutil_hash.strhash64("hello"), 0xb48be5a931380ce8ul);
            EXPECT_EQ(Strutil_hash.strhash64("noise"), 0xdee5b31929501eddul);
            EXPECT_EQ(Strutil_hash.strhash64("The quick brown fox jumps over the lazy dog"), 0x073893414011cae3ul);
        }
    }
}
