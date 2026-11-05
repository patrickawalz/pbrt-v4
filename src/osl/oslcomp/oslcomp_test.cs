// (not in OpenShadingLanguage) oslcomp_test.cs

using System.Collections.Generic;
using OIIO;
using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static class OslCompTests
        {
            private static bool Compile(string source, out string oso, out BufferedErrorHandler errors, List<string> options = null)
            {
                errors = new BufferedErrorHandler();
                OSLCompiler compiler = new OSLCompiler(errors);
                return compiler.compile_buffer(source, out oso, options != null ? options : new List<string>(), "", "test.osl");
            }

            [TEST("OslComp", "CompilesASurface")]
            public static void OslComp_CompilesASurface()
            {
                string source = "shader t1(float Kd = 0.5, color Cs = color(1, 0.5, 0.25), output color Out = 0)\n"
                                + "{\n    float x = Kd * 2 + u;\n    Out = Cs * x;\n    if (x > 1)\n        Out = Out + 1;\n}\n";
                string oso;
                BufferedErrorHandler errors;
                EXPECT_TRUE(Compile(source, out oso, out errors));
                EXPECT_TRUE(oso.StartsWith("OpenShadingLanguage 1.00\n"));
                EXPECT_TRUE(oso.Contains("shader t1\n"));
                EXPECT_TRUE(oso.Contains("param\tfloat\tKd\t0.5\t\t%read{0,0} %write{2147483647,-1}\n"));
                EXPECT_TRUE(oso.Contains("param\tcolor\tCs\t1 0.5 0.25\t\t"));
                EXPECT_TRUE(oso.Contains("oparam\tcolor\tOut\t0 0 0\t\t"));
                EXPECT_TRUE(oso.Contains("\tmul\t\t$tmp1 Kd $const2 \t%filename{\"test.osl\"} %line{3} %argrw{\"wrr\"}\n"));
                EXPECT_TRUE(oso.Contains("\tif\t\t$tmp2 6 6 \t%argrw{\"r\"}\n"));
                EXPECT_TRUE(oso.EndsWith("\tend\n"));
            }

            [TEST("OslComp", "PreprocessorMacros")]
            public static void OslComp_PreprocessorMacros()
            {
                string source = "#define TWICE(x) ((x) * 2)\n#define NAME(a) a ## _v\n#ifdef NOPE\n#error not defined\n#endif\n"
                                + "shader t2(float NAME(k) = TWICE(3), string s = \"a\" \"b\")\n{\n}\n";
                string oso;
                BufferedErrorHandler errors;
                EXPECT_TRUE(Compile(source, out oso, out errors));
                EXPECT_TRUE(oso.Contains("param\tfloat\tk_v\t"));
                EXPECT_TRUE(oso.Contains("param\tstring\ts\t\"ab\"\t"));
            }

            [TEST("OslComp", "ReportsErrorsWithLines")]
            public static void OslComp_ReportsErrorsWithLines()
            {
                string source = "shader t3(float a = 1)\n{\n  a = 2;\n  int i = 1.5 [;\n}\n";
                string oso;
                BufferedErrorHandler errors;
                EXPECT_TRUE(!Compile(source, out oso, out errors));
                EXPECT_TRUE(errors.errors.Count > 0);
                EXPECT_TRUE(errors.errors[0].Contains("test.osl:4: error: Syntax error: syntax error"));
            }

            [TEST("OslComp", "WriteToNonOutputIsAnError")]
            public static void OslComp_WriteToNonOutputIsAnError()
            {
                string source = "shader t4(float a = 1)\n{\n  a = 2;\n}\n";
                string oso;
                BufferedErrorHandler errors;
                // The ast.cpp warning is followed by check_write_legality's error, as in the C++ (see testsuite oslc-err-write-nonoutput)
                EXPECT_TRUE(!Compile(source, out oso, out errors));
                EXPECT_TRUE(errors.errors.Count == 1 && errors.errors[0].Contains("test.osl:3: error: cannot write to non-output parameter \"a\""));
            }

            [TEST("OslComp", "StructsAndFunctions")]
            public static void OslComp_StructsAndFunctions()
            {
                string source = "struct P2 { float a, b; };\nfloat sum(P2 p) { return p.a + p.b; }\n"
                                + "shader t5(output float r = 0)\n{\n  P2 q = { 1, 2 };\n  r = sum(q);\n}\n";
                string oso;
                BufferedErrorHandler errors;
                EXPECT_TRUE(Compile(source, out oso, out errors));
                EXPECT_TRUE(oso.Contains("%mystruct{q} %mystructfield{0}"));
                EXPECT_TRUE(oso.Contains("%mystruct{q} %mystructfield{1}"));
                EXPECT_TRUE(oso.Contains("\tfunctioncall"));
            }
        }
    }
}
