// containers_test.cpp
//
// NOT PORTED (compile-time TypePack machinery, replaced by class hierarchies in this port):
//   TypePack.Index, TypePack.HasType, TypePack.TakeRemove, TypePack.Map

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class ContainersTests
    {
        [TEST("Array2D", "Basics")]
        public static void Array2D_Basics()
        {
            const int nx = 5, ny = 9;
            Array2D<Float> a = new Array2D<Float>(nx, ny);

            EXPECT_EQ(nx, a.XSize());
            EXPECT_EQ(ny, a.YSize());
            EXPECT_EQ(nx * ny, a.size());

            for (int y = 0; y < ny; ++y)
                for (int x = 0; x < nx; ++x)
                    a[x, y] = 1000 * x + y;

            for (int y = 0; y < ny; ++y)
                for (int x = 0; x < nx; ++x)
                    EXPECT_EQ(1000 * x + y, a[x, y]);
        }

        [TEST("Array2D", "Bounds")]
        public static void Array2D_Bounds()
        {
            Bounds2i b = new Bounds2i(new Point2i(-4, 3), new Point2i(10, 7));
            Array2D<Point2f> a = new Array2D<Point2f>(b);

            EXPECT_EQ(b.pMax.x - b.pMin.x, a.XSize());
            EXPECT_EQ(b.pMax.y - b.pMin.y, a.YSize());

            foreach (Point2i p in b)
                a[p] = new Point2f(p.y, p.x);

            foreach (Point2i p in b)
                EXPECT_EQ(new Point2f(p.y, p.x), a[p]);
        }

        [TEST("HashMap", "Basics")]
        public static void HashMap_Basics()
        {
            Allocator alloc = new Allocator();
            HashMap<int, string> map = new HashMap<int, string>(alloc);

            map.Insert(1, "yolo");
            map.Insert(10, "hello");
            map.Insert(42, "test");

            EXPECT_EQ(3, map.size());
            EXPECT_GE(map.capacity(), 3);
            EXPECT_TRUE(map.HasKey(1));
            EXPECT_TRUE(map.HasKey(10));
            EXPECT_TRUE(map.HasKey(42));
            EXPECT_FALSE(map.HasKey(0));
            EXPECT_FALSE(map.HasKey(1240));
            EXPECT_EQ("yolo", map[1]);
            EXPECT_EQ("hello", map[10]);
            EXPECT_EQ("test", map[42]);

            map.Insert(10, "hai");
            EXPECT_EQ(3, map.size());
            EXPECT_GE(map.capacity(), 3);
            EXPECT_EQ("hai", map[10]);
        }

        [TEST("HashMap", "Randoms")]
        public static void HashMap_Randoms()
        {
            Allocator alloc = new Allocator();
            HashMap<int, int> map = new HashMap<int, int>(alloc);
            HashSet<int> values = new HashSet<int>();
            RNG rng = new RNG(1234);

            for (int i = 0; i < 10000; ++i)
            {
                int v = rng.Uniform<int>();
                values.Add(v);
                map.Insert(v, -v);
            }

            // Could have a collision so thus less...
            EXPECT_EQ(map.size(), values.Count);

            // Reset
            rng.SetSequence(1234);
            for (int i = 0; i < 10000; ++i)
            {
                int v = rng.Uniform<int>();
                ASSERT_TRUE(map.HasKey(v));
                EXPECT_EQ(-v, map[v]);
            }

            int nVisited = 0;
            foreach (KeyValuePair<int, int> iter in map)
            {
                ++nVisited;

                EXPECT_EQ(iter.Key, -iter.Value);

                int v = iter.Key;
                ASSERT_TRUE(values.Contains(v));
                values.Remove(v);
            }

            // (the C++ test expects 10000 visits, which also assumes no random collisions)
            EXPECT_EQ(nVisited, 10000);
            EXPECT_EQ(0, values.Count);
        }

        [TEST("InternCache", "BasicString")]
        public static void InternCache_BasicString()
        {
            InternCache<string> cache = new InternCache<string>();

            string one = cache.Lookup("one");
            EXPECT_EQ(1, cache.size());
            EXPECT_EQ("one", one);

            string two = cache.Lookup("two");
            EXPECT_FALSE(ReferenceEquals(one, two));
            EXPECT_EQ("two", two);

            string anotherOne = cache.Lookup(new string("one".ToCharArray()));
            EXPECT_EQ(2, cache.size());
            EXPECT_EQ("one", anotherOne);
            EXPECT_TRUE(ReferenceEquals(one, anotherOne));
        }
    }
}
