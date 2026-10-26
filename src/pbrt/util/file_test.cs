// file_test.cpp

using System;
using System.Collections.Generic;
using System.IO;
using testing;

namespace pbrt
{
    public static unsafe class FileTests
    {
        private static string inTestDir(string path)
        {
            return path;
        }

        [TEST("File", "HasExtension")]
        public static void File_HasExtension()
        {
            EXPECT_TRUE(HasExtension("foo.exr", "exr"));
            EXPECT_TRUE(HasExtension("foo.Exr", "exr"));
            EXPECT_TRUE(HasExtension("foo.Exr", "exR"));
            EXPECT_TRUE(HasExtension("foo.EXR", "exr"));
            EXPECT_FALSE(HasExtension("foo.xr", "exr"));
            EXPECT_FALSE(HasExtension("/foo/png", "ppm"));
        }

        [TEST("File", "RemoveExtension")]
        public static void File_RemoveExtension()
        {
            EXPECT_EQ(RemoveExtension("foo.exr"), "foo");
            EXPECT_EQ(RemoveExtension("fooexr"), "fooexr");
            EXPECT_EQ(RemoveExtension("foo.exr.png"), "foo.exr");
        }

        [TEST("File", "ReadWriteFile")]
        public static void File_ReadWriteFile()
        {
            string fn = inTestDir("readwrite.txt");
            string str = "this is a test.";
            EXPECT_TRUE(WriteFileContents(fn, str));
            string contents = ReadFileContents(fn);
            EXPECT_FALSE(contents.Length == 0);
            EXPECT_EQ(str, contents);
            File.Delete(fn);
            EXPECT_FALSE(File.Exists(fn));
        }

        [TEST("File", "Success")]
        public static void File_Success()
        {
            string fn = inTestDir("floatfile_good.txt");
            EXPECT_TRUE(WriteFileContents(fn, @"1
# comment 6632
-2.5
#6502
3e2       -4.75E-1       5.25




6
"));

            List<Float> floats = ReadFloatFile(fn);
            EXPECT_EQ(6, floats.Count);
            Float[] expected = { 1f, -2.5f, 300f, -.475f, 5.25f, 6f };
            for (int i = 0; i < expected.Length; ++i)
                EXPECT_EQ(expected[i], floats[i], expected[i] + " " + floats[i]);

            File.Delete(fn);
            EXPECT_FALSE(File.Exists(fn));
        }

        [TEST("File", "Failures")]
        public static void File_Failures()
        {
            List<Float> floats = ReadFloatFile("NO_SUCH_FILE_64622");
            EXPECT_EQ(0, floats.Count);

            string fn = inTestDir("malformed.txt");
            EXPECT_TRUE(WriteFileContents(fn, @"1
2 3 4
l5l
6


"));
            floats = ReadFloatFile(fn);
            EXPECT_TRUE(floats.Count == 0);

            File.Delete(fn);
        }
    }
}
