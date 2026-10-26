// pstd_test.cpp
//
// NOT PORTED (C++ memory-model specific; the port uses the garbage collector instead of
// pmr allocators and explicit destructors):
//   Optional.RunDestructors            (checks that ~T() runs when an optional is reset)
//   MonotonicBufferResource.NoOverlap  (pstd::pmr::monotonic_buffer_resource is not ported)

using System;
using testing;

namespace pbrt
{
    public static unsafe class PstdTests
    {
        [TEST("Optional", "Basics")]
        public static void Optional_Basics()
        {
            pstd.optional<int> opt = new pstd.optional<int>();

            EXPECT_FALSE(opt.has_value());

            opt = 1;
            EXPECT_TRUE(opt.has_value());
            EXPECT_TRUE((bool)opt);
            EXPECT_EQ(1, opt.value());
            EXPECT_EQ(1, opt.Ref());

            opt.reset();
            EXPECT_FALSE(opt.has_value());
            EXPECT_FALSE((bool)opt);
            EXPECT_EQ(2, opt.value_or(2));
            opt = 1;
            EXPECT_EQ(1, opt.value_or(2));

            int x = 3;
            opt.reset();
            opt = x;
            EXPECT_TRUE(opt.has_value());
            EXPECT_EQ(3, opt.value());
        }
    }
}
