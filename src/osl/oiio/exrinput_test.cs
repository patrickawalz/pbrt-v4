// (not in OpenShadingLanguage) exrinput_test.cs
//
// The OpenEXR reader against files written by the OpenEXR library (golden/exr/*.exr: 37x13 pixels, channels R G B A, the value of channel c at (x, y) is
// (3x + 5y + 7c) / 64, which half and float represent exactly), one file per compression, pixel type and layout.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using testing;
using static testing.Gtest;

namespace OIIO
{
    public static class ExrInputTests
    {
        private static string fixture_dir([CallerFilePath] string here = "")
        {
            return Path.Combine(Path.GetDirectoryName(here), "golden", "exr");
        }

        [TEST("ExrInput", "AllCompressions")]
        public static void ExrInput_AllCompressions()
        {
            string[] files = Directory.GetFiles(fixture_dir(), "*.exr");
            EXPECT_TRUE(files.Length >= 10);
            foreach (string file in files)
            {
                string error;
                ImageFileData data = ImageReaders.read(file, out error);
                EXPECT_TRUE(data != null);
                if (data == null)
                {
                    Console.Error.WriteLine(Path.GetFileName(file) + ": " + error);
                    continue;
                }
                ImageLevelData level = data.subimages[0].levels[0];
                EXPECT_EQ(level.spec.width, 37);
                EXPECT_EQ(level.spec.height, 13);
                EXPECT_EQ(level.spec.nchannels, 4);
                EXPECT_EQ(level.spec.alpha_channel, 3);
                bool tiled = file.EndsWith("_tile.exr");
                EXPECT_EQ(level.spec.tile_width, tiled ? 16 : 0);
                int wrong = 0;
                for (int y = 0; y < 13; ++y)
                {
                    for (int x = 0; x < 37; ++x)
                    {
                        for (int c = 0; c < 4; ++c)
                        {
                            float expected = (3 * x + 5 * y + 7 * c) / 64.0f;
                            if (level.pixels[(y * 37 + x) * 4 + c] != expected)
                            {
                                ++wrong;
                            }
                        }
                    }
                }
                if (wrong != 0)
                {
                    Console.Error.WriteLine(Path.GetFileName(file) + ": " + wrong + " wrong samples");
                }
                EXPECT_EQ(wrong, 0);
            }
        }
    }
}
