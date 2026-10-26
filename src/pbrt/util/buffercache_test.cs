// buffercache_test.cpp

using System;
using testing;

namespace pbrt
{
    public static unsafe class BufferCacheTests
    {
        [TEST("BufferCache", "Basics")]
        public static void BufferCache_Basics()
        {
            ASSERT_FALSE(intBufferCache == null);

            int[] v = { 1, 2, 3, 4, 5 };

            size_t baseMem = intBufferCache.BytesUsed();

            Allocator alloc = new Allocator();
            int[] ptr = intBufferCache.LookupOrAdd(new pstd.span<int>(v), alloc);
            for (int i = 0; i < v.Length; ++i)
                EXPECT_EQ(ptr[i], v[i]);

            EXPECT_EQ((size_t)(5 * sizeof(int)), intBufferCache.BytesUsed() - baseMem);

            EXPECT_TRUE(ReferenceEquals(ptr, intBufferCache.LookupOrAdd(new pstd.span<int>(v), alloc)));

            EXPECT_EQ((size_t)(5 * sizeof(int)), intBufferCache.BytesUsed() - baseMem);

            int[] v2 = { 1, 2, 3, 4 };
            int[] ptr2 = intBufferCache.LookupOrAdd(new pstd.span<int>(v2), alloc);
            EXPECT_FALSE(ReferenceEquals(ptr, ptr2));
            for (int i = 0; i < v2.Length; ++i)
                EXPECT_EQ(ptr2[i], v2[i]);

            EXPECT_EQ((size_t)(9 * sizeof(int)), intBufferCache.BytesUsed() - baseMem);
        }
    }
}
