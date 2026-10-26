// hash_test.cpp

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class HashTests
    {
        [TEST("Hash", "VarArgs")]
        public static void Hash_VarArgs()
        {
            int64_t[] buf = { 1, -12511, 31415821, 37 };
            fixed (int64_t* b = buf)
                for (int i = 0; i < 4; ++i)
                    EXPECT_EQ(HashBuffer(b + i, 1), Hash(buf[i]));
        }

        [TEST("Hash", "Collisions")]
        public static void Hash_Collisions()
        {
            HashSet<uint32_t> low = new HashSet<uint32_t>(), high = new HashSet<uint32_t>();
            HashSet<uint64_t> full = new HashSet<uint64_t>();

            int lowCollisions = 0, highCollisions = 0;
            int fullCollisions = 0;
            int same = 0;
            for (int i = 0; i < 10000000; ++i)
            {
                uint64_t h = Hash(i);

                if (h == (uint64_t)i)
                    ++same;

                if (low.Contains((uint32_t)h))
                    ++lowCollisions;
                if (high.Contains((uint32_t)(h >> 32)))
                    ++highCollisions;
                if (full.Contains(h >> 32))
                    ++fullCollisions;
            }

            // It's actually potentially legit if any of these hit; it should
            // shouldn't happen a lot.
            EXPECT_EQ(0, same);
            EXPECT_EQ(0, lowCollisions);
            EXPECT_EQ(0, highCollisions);
            EXPECT_EQ(0, fullCollisions);
        }

        [TEST("Hash", "Unaligned")]
        public static void Hash_Unaligned()
        {
            uint64_t[] buf = { 0xfacebeef, 0x65028088, 0x13372048 };
            byte[] cbuf = new byte[sizeof(uint64_t) * 3 + 8];
            fixed (uint64_t* pbuf = buf)
            fixed (byte* pc = cbuf)
                for (int delta = 0; delta < 8; ++delta)
                {
                    Buffer.MemoryCopy(pbuf, pc + delta, cbuf.Length - delta, sizeof(uint64_t) * 3);
                    EXPECT_EQ(HashBuffer(pbuf, 3), HashBuffer(pc + delta, (size_t)(sizeof(uint64_t) * 3)));
                }
        }
    }
}
