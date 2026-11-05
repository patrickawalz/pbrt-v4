// (not in OpenShadingLanguage) fmt_golden_test.cs
//
// Golden tests of the text formatting that OSL's printf, error, warning, fprintf and format take through:
//   * Fmt.format_arg against the {fmt} library that OIIO bundles (the replacement fields "{:+08.3f}" that il_gen_printf builds from the printf conversions
//     and that decode_message hands to {fmt}), including the exception messages for invalid specifications (golden/fmt_golden.txt.gz);
//   * Strutil.sprintf against OIIO::Strutil::sprintf, i.e. fmt::sprintf, which the format() constant folder uses (golden/sprintf_golden.txt.gz);
//   * a few printf conversions against the C library's printf (the C expectations were produced with gcc, see golden/c_printf.c).
// The data files were produced by golden/fmt_golden_gen.cpp and golden/sprintf_golden_gen.cpp.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using testing;
using static testing.Gtest;

namespace OIIO
{
    public static class FmtGoldenTests
    {
        private static string golden_dir([CallerFilePath] string here = "")
        {
            return Path.Combine(Path.GetDirectoryName(here), "golden");
        }

        private static IEnumerable<string> read_gz_lines(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gz))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    yield return line;
                }
            }
        }

        private static string unescape(string s)
        {
            return s.Replace("\\n", "\n").Replace("\\p", "|");
        }

        [TEST("Fmt", "GoldenAgainstFmtLibrary")]
        public static void Fmt_GoldenAgainstFmtLibrary()
        {
            int rows = 0;
            int bad = 0;
            foreach (string line in read_gz_lines(Path.Combine(golden_dir(), "fmt_golden.txt.gz")))
            {
                string[] p = line.Split('|');
                if (p.Length < 4)
                {
                    continue;
                }
                string kind = p[0];
                string spec = p[1];
                if (spec == "{" || spec == "}" || spec == "s}" || spec == "2$")
                {
                    continue;  // replacement-field syntax errors, which Fmt.format reports differently
                }
                if (spec.EndsWith("c") && (kind == "I" || kind == "U"))
                {
                    continue;  // an int printed as a char is a raw byte in {fmt}, not representable in a .NET string
                }
                object value;
                if (kind == "F")
                {
                    value = BitConverter.UInt32BitsToSingle(Convert.ToUInt32(p[2], 16));
                }
                else if (kind == "D")
                {
                    value = BitConverter.UInt64BitsToDouble(Convert.ToUInt64(p[2], 16));
                }
                else if (kind == "I")
                {
                    value = int.Parse(p[2]);
                }
                else if (kind == "U")
                {
                    value = uint.Parse(p[2]);
                }
                else
                {
                    value = unescape(p[2]);
                }
                string expected = unescape(p[3]);
                string got;
                try
                {
                    got = Fmt.format("{:" + spec + "}", value);
                }
                catch (FormatException e)
                {
                    got = "EXC:" + e.Message;
                }
                ++rows;
                if (got != expected)
                {
                    if (bad < 10)
                    {
                        Console.Error.WriteLine("Fmt golden mismatch: " + line + "  got [" + got + "]");
                    }
                    ++bad;
                }
            }
            EXPECT_TRUE(rows > 30000);
            EXPECT_EQ(bad, 0);
        }

        [TEST("Fmt", "SprintfGoldenAgainstFmtSprintf")]
        public static void Fmt_SprintfGoldenAgainstFmtSprintf()
        {
            int rows = 0;
            int bad = 0;
            foreach (string line in read_gz_lines(Path.Combine(golden_dir(), "sprintf_golden.txt.gz")))
            {
                string[] p = line.Split('|');
                int idx = 1;
                string format = p[0];
                while (idx < p.Length && p[idx] != "I" && p[idx] != "F" && p[idx] != "S")
                {
                    format += "|" + p[idx];
                    ++idx;
                }
                string kind = p[idx];
                string valuetext = p[idx + 1];
                string expected = unescape(string.Join("|", p, idx + 2, p.Length - idx - 2));
                object value;
                if (kind == "F")
                {
                    value = BitConverter.UInt32BitsToSingle(Convert.ToUInt32(valuetext, 16));
                }
                else if (kind == "I")
                {
                    value = int.Parse(valuetext);
                }
                else
                {
                    value = unescape(valuetext);
                }
                string got = format.Contains("*") ? Strutil.sprintf(format, 8, value) : Strutil.sprintf(format, value);
                ++rows;
                if (got != expected)
                {
                    if (bad < 10)
                    {
                        Console.Error.WriteLine("sprintf golden mismatch: " + line + "  got [" + got + "]");
                    }
                    ++bad;
                }
            }
            EXPECT_TRUE(rows > 30000);
            EXPECT_EQ(bad, 0);
        }

        // Expected strings produced by the C library (gcc, printf) for the OSL printf conversions on the same numbers
        [TEST("Fmt", "SprintfMatchesCLibrary")]
        public static void Fmt_SprintfMatchesCLibrary()
        {
            EXPECT_EQ(Strutil.sprintf("%g", 100000.0f), "100000");
            EXPECT_EQ(Strutil.sprintf("%g", 1000000.0f), "1e+06");
            EXPECT_EQ(Strutil.sprintf("%g", 0.0001f), "0.0001");
            EXPECT_EQ(Strutil.sprintf("%g", 0.00001f), "1e-05");
            EXPECT_EQ(Strutil.sprintf("%e", 12345.678f), "1.234568e+04");
            EXPECT_EQ(Strutil.sprintf("%5.2f|", 3.14159f), " 3.14|");
            EXPECT_EQ(Strutil.sprintf("%-8s|", "ab"), "ab      |");
            EXPECT_EQ(Strutil.sprintf("%+d", 5), "+5");
            EXPECT_EQ(Strutil.sprintf("%08.3f", -3.14159f), "-003.142");
            EXPECT_EQ(Strutil.sprintf("%%"), "%");
            EXPECT_EQ(Strutil.sprintf("%x", 255), "ff");
            EXPECT_EQ(Strutil.sprintf("%#X", 255), "0XFF");
            EXPECT_EQ(Strutil.sprintf("%o", 8), "10");
            EXPECT_EQ(Strutil.sprintf("%5d|%-5d|", 42, 42), "   42|42   |");
            EXPECT_EQ(Strutil.sprintf("%.3s", "abcdef"), "abc");
            EXPECT_EQ(Strutil.sprintf("%10.4e", 0.000123456f), "1.2346e-04");
        }
    }
}
