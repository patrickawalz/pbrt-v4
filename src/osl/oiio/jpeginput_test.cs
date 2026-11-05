// (not in OpenShadingLanguage) jpeginput_test.cs
//
// The JPEG reader against the pixels libjpeg-turbo decodes (golden/jpeg/NAME.jpg written by Pillow, NAME.raw the decoded 8 bit samples): gray, 4:4:4, 4:2:2
// and 4:2:0 chroma, progressive scans, restart intervals, and an image smaller than one block.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using testing;
using static testing.Gtest;

namespace OIIO
{
    public static class JpegInputTests
    {
        private static string fixture_dir([CallerFilePath] string here = "")
        {
            return Path.Combine(Path.GetDirectoryName(here), "golden", "jpeg");
        }

        [TEST("JpegInput", "MatchesLibjpeg")]
        public static void JpegInput_MatchesLibjpeg()
        {
            string[] files = Directory.GetFiles(fixture_dir(), "*.jpg");
            EXPECT_TRUE(files.Length >= 9);
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
                byte[] expected = File.ReadAllBytes(Path.ChangeExtension(file, ".raw"));
                EXPECT_EQ(expected.Length, level.spec.width * level.spec.height * level.spec.nchannels);
                int wrong = 0;
                for (int i = 0; i < expected.Length && i < level.pixels.Length; ++i)
                {
                    if (level.pixels[i] != expected[i] * ImageReaders.u8scale)
                    {
                        ++wrong;
                    }
                }
                if (wrong != 0)
                {
                    Console.Error.WriteLine(Path.GetFileName(file) + ": " + wrong + " wrong samples of " + expected.Length);
                }
                EXPECT_EQ(wrong, 0);
            }
        }
    }
}
