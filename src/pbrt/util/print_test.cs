// print_test.cpp

using System;
using System.Collections.Generic;
using System.Globalization;
using testing;

namespace pbrt
{
    public static unsafe class PrintTests
    {
        [TEST("StringPrintf", "Basics")]
        public static void StringPrintf_Basics()
        {
            EXPECT_EQ(StringPrintf("Hello, world"), "Hello, world");
            EXPECT_EQ(StringPrintf("x = %d", 5), "x = 5");
            EXPECT_EQ(StringPrintf("%f, %f, %f", 1.0, 1.5, -8.125), "1, 1.5, -8.125");
            EXPECT_DEATH(() => StringPrintf("not enough %s"), "Not enough optional values");
            EXPECT_DEATH(() => StringPrintf("not enough %f yolo"), "Not enough optional values");
            EXPECT_DEATH(() => StringPrintf("too many %f yolo", 1, 2), "Excess values passed");
            EXPECT_DEATH(() => StringPrintf("too many", 1), "Excess values passed");
        }

        [TEST("StringPrintf", "FancyPctS")]
        public static void StringPrintf_FancyPctS()
        {
            EXPECT_EQ(StringPrintf("%s", false), "false");
            EXPECT_EQ(StringPrintf("%s", true), "true");
#if PBRT_FLOAT_AS_DOUBLE
            EXPECT_EQ(StringPrintf("%s", new Vector3f(Pi, -2, 3.1)),
                      "[ 3.141592653589793, -2, 3.1 ]");
#else
            EXPECT_EQ(StringPrintf("%s", new Vector3f(Pi, -2, 3.1f)), "[ 3.1415927, -2, 3.1 ]");
#endif

            string s = "string";
            EXPECT_EQ(StringPrintf("%d %s", 1, s), "1 string");

            List<int> v = new List<int> { 1, 2, 3, 4 };
            EXPECT_EQ(StringPrintf("%s", v), "[ 1, 2, 3, 4 ]");

            string[] a = { "foo", "bar", "bat" };
            EXPECT_EQ(StringPrintf("%s", (object)a), "[ foo, bar, bat ]");

            pstd.span<string> span = new pstd.span<string>(a);
            EXPECT_EQ(StringPrintf("%s", span), "[ foo, bar, bat ]");

            pstd.optional<float> pi = 3.125f;
            EXPECT_EQ(StringPrintf("[ pstd::optional<%s> set: true value: 3.125 ]",
                                   typeof(float).Name),
                      StringPrintf("%s", pi));

            pstd.optional<string> oe = new pstd.optional<string>();
            EXPECT_EQ(StringPrintf("[ pstd::optional<%s> set: false value: n/a ]",
                                   typeof(string).Name),
                      StringPrintf("%s", oe));
        }

        [TEST("StringPrintf", "optional")]
        public static void StringPrintf_optional()
        {
            pstd.optional<float> pi = 3.125f;
            StringPrintf("%s", pi);
        }

        [TEST("StringPrintf", "FancyPctD")]
        public static void StringPrintf_FancyPctD()
        {
            EXPECT_EQ("1", StringPrintf("%d", 1u));
            EXPECT_EQ("4294967295", StringPrintf("%d", 0xffffffffu));

            // > 4G
            EXPECT_EQ("100000000000", StringPrintf("%d", (size_t)100000000000));

            uint64_t ubig = ~0ul;
            EXPECT_EQ("18446744073709551615", StringPrintf("%d", ubig));

            int64_t ibig = (int64_t)(ubig >> 1);
            EXPECT_EQ("9223372036854775807", StringPrintf("%d", ibig));
        }

        [TEST("StringPrintf", "Precision")]
        public static void StringPrintf_Precision()
        {
            // (double-conversion's StringToFloat/StringToDouble are float.Parse/double.Parse here)
            string pi = StringPrintf("%f", (float)Globals.Pi);
            float val = float.Parse(pi, CultureInfo.InvariantCulture);
            EXPECT_EQ(val, Globals.Pi);

            string e = StringPrintf("%f", Math.Exp(1.0));
            double dval = double.Parse(e, CultureInfo.InvariantCulture);
            EXPECT_EQ(dval, Math.Exp(1.0));
        }

        [TEST("OperatorLeftShiftPrint", "Basics")]
        public static void OperatorLeftShiftPrint_Basics()
        {
            EXPECT_EQ("[ 105.5, -12.75 ]", new Point2f(105.5f, -12.75f).ToString());
            EXPECT_EQ("[ -9, 5 ]", new Point2i(-9, 5).ToString());
            EXPECT_EQ("[ 0, 1.25, -9.25 ]", new Point3f(0f, 1.25f, -9.25f).ToString());
            EXPECT_EQ("[ 7, -10, 4 ]", new Point3i(7, -10, 4).ToString());
            EXPECT_EQ("[ 105.5, -12.75 ]", new Vector2f(105.5f, -12.75f).ToString());
            EXPECT_EQ("[ -9, 5 ]", new Vector2i(-9, 5).ToString());
            EXPECT_EQ("[ 0, 1.25, -9.25 ]", new Vector3f(0f, 1.25f, -9.25f).ToString());
            EXPECT_EQ("[ 7, -10, 4 ]", new Vector3i(7, -10, 4).ToString());
            EXPECT_EQ("[ 0, 1.25, -9.25 ]", new Normal3f(0f, 1.25f, -9.25f).ToString());
            {
                Quaternion q = new Quaternion();
                q.v = new Vector3f(1.25f, -8.25f, 14.75f);
                q.w = -0.5f;
                EXPECT_EQ("[ 1.25, -8.25, 14.75, -0.5 ]", q.ToString());
            }
            {
                Ray r = new Ray(new Point3f(-5.5f, 2.75f, 0f), new Vector3f(1.0f, -8.75f, 2.25f), 0.25f);
                EXPECT_EQ("[ o: [ -5.5, 2.75, 0 ] d: [ 1, -8.75, 2.25 ] time: 0.25, " +
                          "medium: (nullptr) ]",
                          r.ToString());
            }
            {
                Bounds2f b = new Bounds2f(new Point2f(2, -5), new Point2f(-8, 3));
                EXPECT_EQ("[ [ -8, -5 ] - [ 2, 3 ] ]", b.ToString());
            }
            {
                Bounds3f b = new Bounds3f(new Point3f(2, -5, .125f), new Point3f(-8, 3, -128.5f));
                EXPECT_EQ("[ [ -8, -5, -128.5 ] - [ 2, 3, 0.125 ] ]", b.ToString());
            }
            {
                SquareMatrix4 m = new SquareMatrix4(0f, -1f, 2f, -3.5f, 4.5f, 5.5f, -6.5f, -7.5f, 8f, 9.25f, 10.75f, -11f,
                                                    12f, 13.25f, 14.5f, -15.875f);
                EXPECT_EQ("[ [ 0, -1, 2, -3.5 ], " +
                          "[ 4.5, 5.5, -6.5, -7.5 ], " +
                          "[ 8, 9.25, 10.75, -11 ], " +
                          "[ 12, 13.25, 14.5, -15.875 ] " +
                          "]",
                          m.ToString());
            }
            {
                Transform t = Translate(new Vector3f(-1.25f, 3.5f, 7.875f)) * Scale(2f, -8f, 1f);
                EXPECT_EQ("[ m: [ [ 2, 0, 0, -1.25 ], [ 0, -8, 0, 3.5 ], [ 0, 0, 1, " +
                          "7.875 ], [ 0, 0, 0, 1 ] ] " +
                          "mInv: [ [ 0.5, 0, 0, 0.625 ], [ 0, -0.125, 0, 0.4375 ], [ " +
                          "0, 0, 1, -7.875 ], [ 0, 0, 0, 1 ] ] ]",
                          t.ToString());
            }
        }
    }
}
