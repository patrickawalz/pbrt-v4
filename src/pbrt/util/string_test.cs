// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// string_test.cpp

using System;
using testing;

namespace pbrt
{
    public static unsafe class StringTests
    {
        [TEST("Unicode", "BasicNormalization")]
        public static void Unicode_BasicNormalization()
        {
            // "Amélie" two ways, via https://en.wikipedia.org/wiki/Unicode_equivalence
            string nfc16 = "Amélie";
            string nfd16 = "Amélie";
            EXPECT_NE(nfc16, nfd16);

            string nfc8 = UTF8FromUTF16(nfc16);
            string nfd8 = UTF8FromUTF16(nfd16);
            EXPECT_NE(nfc8, nfd8);

            EXPECT_EQ(nfc8, NormalizeUTF8(nfc8));  // nfc is already normalized
            EXPECT_EQ(nfc8, NormalizeUTF8(nfd8));  // normalizing nfd should make it equal nfc
        }
    }
}
