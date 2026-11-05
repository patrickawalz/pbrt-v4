// (not in OpenShadingLanguage) osllex_test.cs

using System.Collections.Generic;
using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static class OslLexTests
        {
            private sealed class Host : IOslLexerHost
            {
                public int line = 1;
                public string file = "test.osl";
                public List<string> errors = new List<string>();
                public List<string> warnings = new List<string>();
                public bool nowarn;
                public int lineno() { return line; }
                public void lineno(int l) { line = l; }
                public void incr_lineno() { ++line; }
                public string filename() { return file; }
                public void filename(string name) { file = name; }
                public string main_filename() { return "test.osl"; }
                public string cwd() { return ""; }
                public void errorfmt(string f, int l, string message) { errors.Add(f + ":" + l + ": " + message); }
                public void warningfmt(string f, int l, string message) { warnings.Add(f + ":" + l + ": " + message); }
                public void pragma_nowarn() { nowarn = true; }
            }

            private static List<int> Tokens(string source, Host host, List<YYSTYPE> values = null)
            {
                OslLexer lexer = new OslLexer(source, host);
                List<int> tokens = new List<int>();
                YYLTYPE loc = new YYLTYPE();
                while (true)
                {
                    int t = lexer.osllex(out YYSTYPE v, ref loc);
                    if (t == 0)
                    {
                        break;
                    }
                    tokens.Add(t);
                    if (values != null)
                    {
                        values.Add(v);
                    }
                }
                return tokens;
            }

            [TEST("OslLex", "KeywordsIdentifiersAndOperators")]
            public static void OslLex_KeywordsIdentifiersAndOperators()
            {
                Host host = new Host();
                List<YYSTYPE> values = new List<YYSTYPE>();
                List<int> t = Tokens("surface s (float Kd = 1) { color c = Kd; c <<= 2; if (a >= b and not c) x++; }", host, values);
                EXPECT_EQ(t[0], 258);          // "surface" is not a keyword: shader types are identifiers
                EXPECT_EQ(t[1], 258);
                EXPECT_EQ(t[2], (int)'(');
                EXPECT_EQ(t[3], OslTokens.FLOATTYPE);
                EXPECT_TRUE(t.Contains(OslTokens.SHL_ASSIGN));
                EXPECT_TRUE(t.Contains(OslTokens.GE_OP));
                EXPECT_TRUE(t.Contains(OslTokens.AND_OP));
                EXPECT_TRUE(t.Contains(OslTokens.NOT_OP));
                EXPECT_TRUE(t.Contains(OslTokens.INCREMENT));
                EXPECT_EQ(values[1].s, "s");
                EXPECT_EQ(host.errors.Count, 0);
                // a word that only starts with a keyword is an identifier
                t = Tokens("ifx colorful", host);
                EXPECT_EQ(t.Count, 2);
                EXPECT_EQ(t[0], OslTokens.IDENTIFIER);
                EXPECT_EQ(t[1], OslTokens.IDENTIFIER);
                // reserved words are an error, and the single characters come back as their own codes
                host.errors.Clear();
                t = Tokens("class ;", host);
                EXPECT_EQ(t[0], OslTokens.RESERVED);
                EXPECT_EQ(t[1], (int)';');
                EXPECT_EQ(host.errors.Count, 1);
                EXPECT_EQ(Tokens("[[ ]]", host)[0], OslTokens.METADATA_BEGIN);
                EXPECT_EQ(Tokens("!", host)[0], OslTokens.NOT_OP);
                EXPECT_EQ(Tokens("!=", host)[0], OslTokens.NE_OP);
            }

            [TEST("OslLex", "Literals")]
            public static void OslLex_Literals()
            {
                Host host = new Host();
                List<YYSTYPE> values = new List<YYSTYPE>();
                List<int> t = Tokens("12 0x1F 1.5 .5 2. 1e3 2.5E-2 \"a\\n\\\"b\" 0xg", host, values);
                EXPECT_EQ(t[0], OslTokens.INT_LITERAL);
                EXPECT_EQ(values[0].i, 12);
                EXPECT_EQ(values[1].i, 31);
                EXPECT_EQ(t[2], OslTokens.FLOAT_LITERAL);
                EXPECT_NEAR(values[2].f, 1.5, 0);
                EXPECT_NEAR(values[3].f, 0.5, 0);
                EXPECT_NEAR(values[4].f, 2, 0);
                EXPECT_NEAR(values[5].f, 1000, 0);
                EXPECT_NEAR(values[6].f, 0.025, 1e-9);
                EXPECT_EQ(t[7], OslTokens.STRING_LITERAL);
                EXPECT_EQ(values[7].s, "a\n\"b");
                // 0xg is the integer 0 followed by the identifier xg
                EXPECT_EQ(t[8], OslTokens.INT_LITERAL);
                EXPECT_EQ(t[9], OslTokens.IDENTIFIER);
                // no leading sign: -0.5 is the operator and then the literal
                t = Tokens("a-0.5", host);
                EXPECT_EQ(t[1], (int)'-');
                EXPECT_EQ(t[2], OslTokens.FLOAT_LITERAL);
                // the largest int plus one is accepted (negation happens in the grammar), more is an error
                host.errors.Clear();
                Tokens("2147483648", host);
                EXPECT_EQ(host.errors.Count, 0);
                Tokens("2147483649", host);
                EXPECT_EQ(host.errors.Count, 1);
            }

            [TEST("OslLex", "LinesCommentsAndPreprocessorLines")]
            public static void OslLex_LinesCommentsAndPreprocessorLines()
            {
                Host host = new Host();
                Tokens("a // comment\nb\n\nc", host);
                EXPECT_EQ(host.line, 4);
                host = new Host();
                Tokens("a \\\nb", host);
                EXPECT_EQ(host.line, 2);
                // a line directive sets the line and the file
                host = new Host();
                Tokens("# 40 \"stdosl.h\"\nx\n", host);
                EXPECT_EQ(host.file, "stdosl.h");
                EXPECT_EQ(host.line, 41);
                // for the main file the line is one less (see the comment in osllex.l)
                host = new Host();
                Tokens("# 10 \"test.osl\"\nx\n", host);
                EXPECT_EQ(host.line, 10);
                // pragmas
                host = new Host();
                Tokens("#pragma osl nowarn\n#pragma warning \"careful\"\n#pragma error \"stop\"\n#pragma osl bogus\n", host);
                EXPECT_TRUE(host.nowarn);
                EXPECT_EQ(host.warnings.Count, 2);
                EXPECT_EQ(host.errors.Count, 1);
                EXPECT_TRUE(host.errors[0].EndsWith("stop"));
                // the line numbers reported in the locations
                host = new Host();
                OslLexer lexer = new OslLexer("a\n\n  b", host);
                YYLTYPE loc = new YYLTYPE();
                lexer.osllex(out YYSTYPE v, ref loc);
                EXPECT_EQ(loc.first_line, 1);
                lexer.osllex(out v, ref loc);
                EXPECT_EQ(loc.first_line, 3);
            }
        }
    }
}
