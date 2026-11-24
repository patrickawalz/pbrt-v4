// (not in pbrt-v4) cyhair_test.cs
//
// Tests of the HAIR file (cyhair.cs): what is written is what is read, with every array and with none but the points.

using System;
using System.IO;
using testing;

namespace pbrt
{
    public static class CyHairTests
    {
        private static HairStrandSet Strands(bool even, bool arrays)
        {
            Random random = new Random(5);
            int strands = 37;
            HairStrandSet set = new HairStrandSet();
            set.first = new int[strands + 1];
            for (int i = 0; i < strands; ++i)
            {
                set.first[i + 1] = set.first[i] + (even ? 9 : 2 + random.Next(20));
            }
            int points = set.first[strands];
            set.points = new float[3 * points];
            for (int i = 0; i < 3 * points; ++i)
            {
                set.points[i] = (float)(random.NextDouble() * 10 - 5);
            }
            if (arrays)
            {
                set.thickness = new float[points];
                set.transparency = new float[points];
                set.colors = new float[3 * points];
                for (int i = 0; i < points; ++i)
                {
                    set.thickness[i] = (float)random.NextDouble();
                    set.transparency[i] = (float)random.NextDouble();
                    set.colors[3 * i] = (float)random.NextDouble();
                    set.colors[3 * i + 1] = (float)random.NextDouble();
                    set.colors[3 * i + 2] = (float)random.NextDouble();
                }
            }
            set.defaultThickness = 0.25f;
            set.defaultTransparency = 0.125f;
            set.defaultRed = 0.5f;
            set.defaultGreen = 0.75f;
            set.defaultBlue = 0.0625f;
            set.note = "a test of the HAIR file";
            return set;
        }

        private static void Same(float[] a, float[] b)
        {
            CHECK_EQ(a == null, b == null);
            if (a == null)
            {
                return;
            }
            CHECK_EQ(a.Length, b.Length);
            for (int i = 0; i < a.Length; ++i)
            {
                CHECK_EQ(a[i], b[i]);
            }
        }

        [TEST("CyHair", "WhatIsWrittenIsRead")]
        public static void WhatIsWrittenIsRead()
        {
            for (int which = 0; which < 4; ++which)
            {
                bool even = which % 2 == 0, arrays = which >= 2;
                HairStrandSet set = Strands(even, arrays);
                string filename = Path.Combine(Path.GetTempPath(), "magi_cyhair_test_" + which + ".hair");
                CyHair.Write(filename, set);
                // The size is that of the header and of the arrays that the file has
                long size = 128 + (even ? 0 : 2 * set.StrandCount) + 12 * set.PointCount + (arrays ? 20 * set.PointCount : 0);
                CHECK_EQ(new FileInfo(filename).Length, size);
                HairStrandSet read = CyHair.Read(filename);
                File.Delete(filename);
                CHECK_EQ(read.StrandCount, set.StrandCount);
                CHECK_EQ(read.PointCount, set.PointCount);
                for (int i = 0; i <= set.StrandCount; ++i)
                {
                    CHECK_EQ(read.first[i], set.first[i]);
                }
                Same(read.points, set.points);
                Same(read.thickness, set.thickness);
                Same(read.transparency, set.transparency);
                Same(read.colors, set.colors);
                CHECK_EQ(read.defaultThickness, set.defaultThickness);
                CHECK_EQ(read.defaultTransparency, set.defaultTransparency);
                CHECK_EQ(read.defaultRed, set.defaultRed);
                CHECK_EQ(read.defaultGreen, set.defaultGreen);
                CHECK_EQ(read.defaultBlue, set.defaultBlue);
                CHECK(read.note == set.note);
            }
        }
    }
}
