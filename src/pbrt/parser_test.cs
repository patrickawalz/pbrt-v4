// parser_test.cpp

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class ParserTests
    {
        private static string inTestDir(string path) { return path; }

        private static List<string> extract(Tokenizer t)
        {
            List<string> tokens = new List<string>();
            while (true)
            {
                Token tok = t.Next();
                if (tok == null)
                    return tokens;
                tokens.Add(tok.token);
            }
        }

        private static void checkTokens(Tokenizer t, string[] expected)
        {
            List<string> tokens = extract(t);
            int iter = 0;
            foreach (string s in tokens)
            {
                EXPECT_TRUE(iter != expected.Length);
                if (iter < expected.Length)
                    EXPECT_EQ(expected[iter], s);
                ++iter;
            }
            EXPECT_TRUE(iter == expected.Length);
        }

        [TEST("Parser", "TokenizerBasics")]
        public static void Parser_TokenizerBasics()
        {
            List<string> errors = new List<string>();
            Action<string, FileLoc> err = (string e, FileLoc loc) => { errors.Add(e); };

            {
                Tokenizer t = Tokenizer.CreateFromString("Shape \"sphere\" \"float radius\" [1]", err);
                ASSERT_TRUE(t != null);
                checkTokens(t, new string[] { "Shape", "\"sphere\"", "\"float radius\"", "[", "1", "]" });
            }

            {
                Tokenizer t = Tokenizer.CreateFromString("Shape \"sphere\"\n\"float radius\" [1]", err);
                ASSERT_TRUE(t != null);
                checkTokens(t, new string[] { "Shape", "\"sphere\"", "\"float radius\"", "[", "1", "]" });
            }

            {
                Tokenizer t = Tokenizer.CreateFromString("\nShape\"sphere\" # foo bar [\n\"float radius\\\"\" 1", err);
                ASSERT_TRUE(t != null);
                checkTokens(t, new string[] { "Shape", "\"sphere\"", "# foo bar [", "\"float radius\"\"", "1" });
            }
        }

        private static void checkError(string input, string expectedError)
        {
            bool gotError = false;
            Action<string, FileLoc> err = (string e, FileLoc loc) => { gotError = (e == expectedError); };
            Tokenizer t = Tokenizer.CreateFromString(input, err);
            ASSERT_TRUE(t != null);
            extract(t);
            EXPECT_TRUE(gotError);
        }

        [TEST("Parser", "TokenizerErrors")]
        public static void Parser_TokenizerErrors()
        {
            checkError("Shape\"sphere\"\t\t # foo bar\n\"float radius", "premature EOF");
            checkError("Shape\"sphere\"\t\t # foo bar\n\"float radius", "premature EOF");
            checkError("Shape\"sphere\"\t\t # foo bar\n\"float radius\\", "premature EOF");
            checkError("Shape\"sphere\"\t\t # foo bar\n\"float radius\n\" 5", "unterminated string");
        }

        [TEST("Parser", "TokenizeFile")]
        public static void Parser_TokenizeFile()
        {
            string filename = inTestDir("test.tok");
            System.IO.File.WriteAllText(filename,
                "\nWorldBegin # hello\nIntegrator \"deep\" \"float density\" [ 2 2.66612 -5e-51]\n");

            Action<string, FileLoc> err = (string e, FileLoc loc) => { EXPECT_TRUE(false, "Unexpected error: " + e); };
            {
                Tokenizer t = Tokenizer.CreateFromFile(filename, err);
                ASSERT_TRUE(t != null);
                checkTokens(t, new string[] { "WorldBegin", "# hello", "Integrator", "\"deep\"",
                                              "\"float density\"", "[", "2", "2.66612", "-5e-51", "]" });
            }

            System.IO.File.Delete(filename);
            EXPECT_FALSE(System.IO.File.Exists(filename));
        }
    }
}
