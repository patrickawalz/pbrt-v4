// (not in OpenShadingLanguage) opstring_test.cs
//
// String shadeops. The expected values for the number parsers and substr are what the C++ osl_* functions return on the same inputs.

using System;
using OIIO;
using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static unsafe class OpStringTests
        {
            private static ulong H(string s)
            {
                return new ustring(s).hash();
            }

            private static string S(ulong h)
            {
                ustring u;
                ustring.from_hash(h, out u);
                return u.@string();
            }

            [TEST("OpString", "ConcatLengthSubstr")]
            public static void OpString_ConcatLengthSubstr()
            {
                EXPECT_TRUE(S(OslRuntime.osl_concat_sss(H("foo"), H("bar"))) == "foobar");
                EXPECT_TRUE(S(OslRuntime.osl_concat_sss(H(""), H("bar"))) == "bar");
                EXPECT_EQ(OslRuntime.osl_strlen_is(H("hello")), 5);
                EXPECT_EQ(OslRuntime.osl_strlen_is(H("")), 0);
                EXPECT_TRUE(S(OslRuntime.osl_substr_ssii(H("hello world"), 6, 5)) == "world");
                EXPECT_TRUE(S(OslRuntime.osl_substr_ssii(H("hello world"), -5, 3)) == "wor");
                EXPECT_TRUE(S(OslRuntime.osl_substr_ssii(H("hello"), 3, 50)) == "lo");
                EXPECT_TRUE(S(OslRuntime.osl_substr_ssii(H("hello"), 100, 2)) == "");
                EXPECT_TRUE(S(OslRuntime.osl_substr_ssii(H("hello"), 1, -1)) == "");
            }

            [TEST("OpString", "GetcharStartsEndsWith")]
            public static void OpString_GetcharStartsEndsWith()
            {
                EXPECT_EQ(OslRuntime.osl_getchar_isi(H("abc"), 1), (int)'b');
                EXPECT_EQ(OslRuntime.osl_getchar_isi(H("abc"), 3), 0);
                EXPECT_EQ(OslRuntime.osl_getchar_isi(H("abc"), -1), 0);
                EXPECT_EQ(OslRuntime.osl_startswith_iss(H("hello"), H("he")), 1);
                EXPECT_EQ(OslRuntime.osl_startswith_iss(H("hello"), H("lo")), 0);
                EXPECT_EQ(OslRuntime.osl_endswith_iss(H("hello"), H("lo")), 1);
                EXPECT_EQ(OslRuntime.osl_endswith_iss(H("hello"), H("he")), 0);
                EXPECT_EQ(OslRuntime.osl_startswith_iss(H("hello"), H("")), 1);
            }

            [TEST("OpString", "StoiAndStofFollowStrtol")]
            public static void OpString_StoiAndStofFollowStrtol()
            {
                EXPECT_EQ(OslRuntime.osl_stoi_is(H("12")), 12);
                EXPECT_EQ(OslRuntime.osl_stoi_is(H("-7")), -7);
                EXPECT_EQ(OslRuntime.osl_stoi_is(H(" 42")), 42);
                EXPECT_EQ(OslRuntime.osl_stoi_is(H("abc")), 0);
                EXPECT_EQ(OslRuntime.osl_stoi_is(H("")), 0);
                EXPECT_EQ(OslRuntime.osl_stof_fs(H("3.5")), 3.5f);
                EXPECT_EQ(OslRuntime.osl_stof_fs(H("-0.5e-3")), -0.5e-3f);
                EXPECT_EQ(OslRuntime.osl_stof_fs(H("1e5")), 100000.0f);
                EXPECT_EQ(OslRuntime.osl_stof_fs(H("x")), 0.0f);
                EXPECT_TRUE(float.IsPositiveInfinity(OslRuntime.osl_stof_fs(H("inf"))));
                EXPECT_EQ(BitConverter.SingleToUInt32Bits(OslRuntime.osl_stof_fs(H("nan"))), 0x7fc00000u);
            }

            [TEST("OpString", "SplitMatchesOsl")]
            public static void OpString_SplitMatchesOsl()
            {
                ulong* parts = stackalloc ulong[8];
                int n = OslRuntime.osl_split(H("a b  c"), parts, H(""), 8, 8);
                EXPECT_EQ(n, 3);
                EXPECT_TRUE(S(parts[0]) == "a" && S(parts[1]) == "b" && S(parts[2]) == "c");
                n = OslRuntime.osl_split(H("a,b,,c"), parts, H(","), 8, 8);
                EXPECT_EQ(n, 4);
                EXPECT_TRUE(S(parts[2]) == "" && S(parts[3]) == "c");
                n = OslRuntime.osl_split(H("a,b,c"), parts, H(","), 2, 8);
                EXPECT_EQ(n, 2);
                EXPECT_TRUE(S(parts[0]) == "a" && S(parts[1]) == "b,c");
            }

            [TEST("OpString", "HashIsStrhash")]
            public static void OpString_HashIsStrhash()
            {
                EXPECT_EQ(OslRuntime.osl_hash_is(H("hello")), unchecked((int)Strutil_hash.strhash64("hello")));
            }
        }
    }
}
