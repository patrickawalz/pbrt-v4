// rng_test.cpp

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class RNGTests
    {
        [TEST("RNG", "Reseed")]
        public static void RNG_Reseed()
        {
            RNG rng = new RNG(1234);
            List<uint32_t> values = new List<uint32_t>();
            for (int i = 0; i < 100; ++i)
                values.Add(rng.Uniform<uint32_t>());

            rng.SetSequence(1234);
            for (int i = 0; i < values.Count; ++i)
                EXPECT_EQ(values[i], rng.Uniform<uint32_t>());
        }

        [TEST("RNG", "Advance")]
        public static void RNG_Advance()
        {
            // Note: must use float not Float, since doubles consume two 32-bit
            // values from the stream, so the advance tests (as written)
            // consequently fail.
            RNG rng = new RNG();
            rng.SetSequence(1234, 6502);
            List<float> v = new List<float>();
            for (int i = 0; i < 1000; ++i)
                v.Add(rng.Uniform<float>());

            rng.SetSequence(1234, 6502);
            rng.Advance(16);
            EXPECT_EQ(rng.Uniform<float>(), v[16]);

            for (int i = v.Count - 1; i >= 0; --i)
            {
                rng.SetSequence(1234, 6502);
                rng.Advance(i);
                EXPECT_EQ(rng.Uniform<float>(), v[i]);
            }

            // Switch to another sequence
            rng.SetSequence(32);
            rng.Uniform<float>();

            // Go back and check one last time
            foreach (int i in new int[] { 5, 998, 552, 37, 16 })
            {
                rng.SetSequence(1234, 6502);
                rng.Advance(i);
                EXPECT_EQ(rng.Uniform<float>(), v[i]);
            }
        }

        [TEST("RNG", "OperatorMinus")]
        public static void RNG_OperatorMinus()
        {
            RNG ra = new RNG(1337), rb = new RNG(1337);
            RNG rng = new RNG();
            for (int i = 0; i < 10; ++i)
            {
                int step = 1 + (int)rng.Uniform<uint32_t>(1000);
                for (int j = 0; j < step; ++j)
                    ra.Uniform<uint32_t>();
                EXPECT_EQ(step, ra - rb);
                EXPECT_EQ(-step, rb - ra);

                // Reysnchronize them
                if ((rng.Uniform<uint32_t>() & 1) != 0u)
                    rb.Advance(step);
                else
                    ra.Advance(-step);
                EXPECT_EQ(0, ra - rb);
                EXPECT_EQ(0, rb - ra);
            }
        }

        [TEST("RNG", "Int")]
        public static void RNG_Int()
        {
            RNG rng = new RNG();
            int positive = 0, negative = 0, zero = 0;
            int count = 10000;
            for (int i = 0; i < count; ++i)
            {
                int v = rng.Uniform<int>();
                if (v < 0)
                    ++negative;
                else if (v == 0)
                    ++zero;
                else
                    ++positive;
            }

            EXPECT_GT(positive, .48 * count);
            EXPECT_LT(positive, .52 * count);
            EXPECT_GT(negative, .48 * count);
            EXPECT_LT(negative, .52 * count);
            EXPECT_LT(zero, .001 * count);
        }

        [TEST("RNG", "Uint64")]
        public static void RNG_Uint64()
        {
            RNG rng = new RNG();
            int[] bitCounts = new int[64];
            int count = 10000;
            for (int i = 0; i < count; ++i)
            {
                uint64_t v = rng.Uniform<uint64_t>();
                for (int b = 0; b < 64; ++b)
                    if ((v & (1ul << b)) != 0u)
                        ++bitCounts[b];
            }

            for (int b = 0; b < 64; ++b)
            {
                EXPECT_GT(bitCounts[b], .48 * count);
                EXPECT_LT(bitCounts[b], .52 * count);
            }
        }

        [TEST("RNG", "Double")]
        public static void RNG_Double()
        {
            RNG rng = new RNG();
            for (int i = 0; i < 10; ++i)
            {
                double v = rng.Uniform<double>();
                EXPECT_NE(v, (double)(float)v);
            }
        }
    }
}
