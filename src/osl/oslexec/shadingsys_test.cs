// (not in OpenShadingLanguage) shadingsys_test.cs: tests of the execution library core (masters, groups, attributes, error messages)

using System;
using System.Collections.Generic;
using System.IO;
using OIIO;
using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static class OslExecTests
        {
            private const string OsoDir = "/tmp/oslc/out2";
            private const string TestsuiteDir = "/home/claude/OpenShadingLanguage/testsuite";

            /// Counts the symbols and instructions an .oso file declares
            private sealed class CountingReader : OSOReader
            {
                public int symbols, instructions, params_;

                public CountingReader(ErrorHandler e) : base(e) { }
                public override void symbol(SymType symtype, TypeSpec typespec, string name)
                {
                    ++symbols;
                    if (symtype == SymType.SymTypeParam || symtype == SymType.SymTypeOutputParam)
                    {
                        ++params_;
                    }
                }
                public override void instruction(int label, string opcode) { ++instructions; }
            }

            private static ShadingSystemImpl MakeSystem(out BufferedErrorHandler errors)
            {
                errors = new BufferedErrorHandler();
                return new ShadingSystemImpl(new RendererServices(), null, errors);
            }

            private static string CompileShader(string source, BufferedErrorHandler errors)
            {
                OSLCompiler compiler = new OSLCompiler(errors);
                string oso;
                bool ok = compiler.compile_buffer(source, out oso, new List<string>(), "", "test.osl");
                EXPECT_TRUE(ok);
                return oso;
            }

            [TEST("OslExec", "LoadsAllCompiledShaders")]
            public static void OslExec_LoadsAllCompiledShaders()
            {
                if (!Directory.Exists(OsoDir))
                {
                    return;
                }
                BufferedErrorHandler errors;
                ShadingSystemImpl ss = MakeSystem(out errors);
                ss.attribute("searchpath:shader", OsoDir);
                string[] files = Directory.GetFiles(OsoDir, "*.oso");
                int loaded = 0;
                int mismatches = 0;
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    ShaderMaster master = ss.loadshader(name);
                    if (master == null)
                    {
                        continue;
                    }
                    ++loaded;
                    CountingReader counter = new CountingReader(new BufferedErrorHandler());
                    EXPECT_TRUE(counter.parse_file(file));
                    if (master.m_symbols.Count != counter.symbols || master.m_ops.Count != counter.instructions
                        || master.num_params() != counter.params_)
                    {
                        ++mismatches;
                        if (mismatches < 5)
                        {
                            Console.Error.WriteLine("mismatch in " + name + ": symbols " + master.m_symbols.Count + " vs " + counter.symbols + ", ops "
                                                    + master.m_ops.Count + " vs " + counter.instructions);
                        }
                    }
                }
                EXPECT_EQ(loaded, files.Length);
                EXPECT_EQ(mismatches, 0);
                int count;
                EXPECT_TRUE(ss.getattribute("stat:masters", out count));
                EXPECT_EQ(count, loaded);
            }

            private const string SrcA = "shader a(float f_in = 1, output float f_out = 0, output color c_out = 0) { f_out = f_in * 2; c_out = color(f_out); }\n";
            private const string SrcB = "shader b(float f_in = 0 [[ int allowconnect = 0 ]], color c_in = 0, output float res = 0) { res = f_in + c_in[0]; }\n";
            private const string SrcC = "shader c(float f_in = 0, color c_in = 0, float arr[3] = {1,2,3}, output float res = 0) { res = f_in + c_in[0] + arr[1]; }\n";

            [TEST("OslExec", "BuildsGroupsAndConnections")]
            public static void OslExec_BuildsGroupsAndConnections()
            {
                BufferedErrorHandler errors;
                ShadingSystemImpl ss = MakeSystem(out errors);
                EXPECT_TRUE(ss.LoadMemoryCompiledShader("a", CompileShader(SrcA, errors)));
                EXPECT_TRUE(ss.LoadMemoryCompiledShader("c", CompileShader(SrcC, errors)));

                ShaderGroup g = ss.ShaderGroupBegin("grp");
                EXPECT_TRUE(ss.Parameter(g, "f_in", TypeDescs.TypeFloat, new float[] { 3.0f }));
                EXPECT_TRUE(ss.Shader(g, "surface", "a", "alayer"));
                EXPECT_TRUE(ss.Parameter(g, "arr", new TypeDesc(TypeDesc.FLOAT, 3), new float[] { 4, 5, 6 }));
                EXPECT_TRUE(ss.Shader(g, "surface", "c", "clayer"));
                EXPECT_TRUE(ss.ConnectShaders(g, "alayer", "f_out", "clayer", "f_in"));
                EXPECT_TRUE(ss.ConnectShaders(g, "alayer", "c_out", "clayer", "c_in"));
                EXPECT_TRUE(ss.ConnectShaders(g, "alayer", "c_out[1]", "clayer", "arr[2]"));
                EXPECT_TRUE(ss.ShaderGroupEnd(g));
                EXPECT_EQ(g.nlayers(), 2);
                EXPECT_EQ(g[0].layername().@string(), "alayer");
                EXPECT_EQ(g[1].nconnections(), 3);
                EXPECT_TRUE(g[1].connection(0).srclayer == 0);
                EXPECT_EQ(errors.errors.Count, 0);

                object v;
                EXPECT_TRUE(ss.getattribute(g, "num_layers", TypeDescs.TypeInt, out v));
                EXPECT_EQ((int)v, 2);
                EXPECT_TRUE(ss.getattribute(g, "groupname", TypeDescs.TypeString, out v));
                EXPECT_EQ((string)v, "grp");
                EXPECT_TRUE(ss.getattribute(g, "layer_names", new TypeDesc(TypeDesc.STRING, 2), out v));
                EXPECT_EQ(((string[])v)[1], "clayer");
                EXPECT_TRUE(ss.getattribute(g, "group_init_name", TypeDescs.TypeString, out v));
                EXPECT_EQ((string)v, "osl_init_group_grp");
                EXPECT_TRUE(ss.getattribute(g, "group_entry_name", TypeDescs.TypeString, out v));
                EXPECT_EQ((string)v, "osl_layer_group_grp_name_clayer");
                EXPECT_TRUE(ss.getattribute("stat:groups", out int groups));
                EXPECT_EQ(groups, 1);
                EXPECT_TRUE(ss.getattribute("stat:instances", out int insts));
                EXPECT_EQ(insts, 2);

                // The textual group description makes the same network
                ShaderGroup g2 = ss.ShaderGroupBegin("grp2", "surface",
                                                     "shader a alayer; float f_in 3; shader c clayer; connect alayer.f_out clayer.f_in; "
                                                     + "connect alayer.c_out clayer.c_in; ");
                EXPECT_TRUE(g2 != null);
                EXPECT_TRUE(ss.ShaderGroupEnd(g2));
                EXPECT_EQ(g2.nlayers(), 2);
                EXPECT_EQ(g2[1].nconnections(), 2);
                EXPECT_EQ(errors.errors.Count, 0);
            }

            [TEST("OslExec", "AttributeRoundTrips")]
            public static void OslExec_AttributeRoundTrips()
            {
                BufferedErrorHandler errors;
                ShadingSystemImpl ss = MakeSystem(out errors);
                int i;
                EXPECT_TRUE(ss.getattribute("optimize", out i));
                EXPECT_EQ(i, 2);
                EXPECT_TRUE(ss.attribute("optimize", 1));
                EXPECT_TRUE(ss.getattribute("optimize", out i));
                EXPECT_EQ(i, 1);
                EXPECT_TRUE(ss.getattribute("lazylayers", out i));
                EXPECT_EQ(i, 1);
                EXPECT_TRUE(ss.attribute("lazylayers", 0));
                EXPECT_TRUE(ss.getattribute("lazylayers", out i));
                EXPECT_EQ(i, 0);
                EXPECT_TRUE(ss.attribute("lockgeom", 0));
                EXPECT_FALSE(ss.lockgeom_default());
                // wrong type is refused, unknown names are refused
                EXPECT_FALSE(ss.attribute("optimize", 1.5f));
                EXPECT_FALSE(ss.attribute("no_such_attribute", 1));
                string s;
                EXPECT_TRUE(ss.attribute("searchpath:shader", "/a:/b"));
                EXPECT_TRUE(ss.getattribute("searchpath:shader", out s));
                EXPECT_EQ(s, "/a:/b");
                EXPECT_EQ(ss.m_searchpath_dirs.Count, 2);
                EXPECT_TRUE(ss.attribute("debug_groupname", "xyz"));
                EXPECT_TRUE(ss.getattribute("debug_groupname", out s));
                EXPECT_EQ(s, "xyz");
                EXPECT_TRUE(ss.attribute("commonspace", "common"));
                EXPECT_TRUE(ss.getattribute("commonspace", out s));
                EXPECT_EQ(s, "common");
                EXPECT_TRUE(ss.getattribute("colorspace", out s));
                EXPECT_EQ(s, "Rec709");
                EXPECT_TRUE(ss.attribute("colorspace", "sRGB") || errors.errors.Count > 0);
                // options string
                EXPECT_TRUE(ss.attribute("options", "opt_passes=7,vector_width=8,debug_layername=lay"));
                EXPECT_TRUE(ss.getattribute("opt_passes", out i));
                EXPECT_EQ(i, 7);
                EXPECT_TRUE(ss.getattribute("vector_width", out i));
                EXPECT_EQ(i, 8);
                EXPECT_TRUE(ss.getattribute("debug_layername", out s));
                EXPECT_EQ(s, "lay");
                // raytypes
                EXPECT_EQ(ss.raytype_bit(new ustring("camera")), 1);
                EXPECT_EQ(ss.raytype_bit(new ustring("diffuse")), 16);
                EXPECT_EQ(ss.raytype_bit(new ustring("nonesuch")), 0);
                ss.attribute("raytypes", new TypeDesc(TypeDesc.STRING, 2), new string[] { "foo", "bar" });
                EXPECT_EQ(ss.raytype_bit(new ustring("bar")), 2);
                // statistics are readable
                object v;
                EXPECT_TRUE(ss.getattribute("stat:memory_peak", new TypeDesc(TypeDesc.LONGLONG), out v));
                EXPECT_TRUE(ss.getattribute("stat:master_load_time", TypeDescs.TypeFloat, out v));
                // error_repeats
                EXPECT_TRUE(ss.attribute("error_repeats", 0));
                ss.error("same");
                ss.error("same");
                EXPECT_EQ(errors.errors.Count, 1);
                ss.attribute("error_repeats", 1);
                ss.error("same");
                ss.error("same");
                EXPECT_EQ(errors.errors.Count, 3);
                EXPECT_EQ(ShadingSystemImpl.globals_name(SGBits.dPdu), "dPdu");
                EXPECT_TRUE(ShadingSystemImpl.globals_bit("Ng") == SGBits.Ng);
            }

            // Reads the expected "ERROR: ..." lines of a testsuite reference output (the error handler of the port does not add the prefix)
            private static List<string> RefErrors(string testname)
            {
                string path = Path.Combine(TestsuiteDir, testname, "ref", "out.txt");
                List<string> result = new List<string>();
                if (!File.Exists(path))
                {
                    return result;
                }
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; ++i)
                {
                    if (lines[i].StartsWith("ERROR: "))
                    {
                        string msg = lines[i].Substring(7);
                        while (i + 1 < lines.Length && lines[i + 1].StartsWith("        "))
                        {
                            msg += "\n" + lines[++i];
                        }
                        result.Add(msg);
                    }
                }
                return result;
            }

            [TEST("OslExec", "MissingShaderMessagesMatchTestsuite")]
            public static void OslExec_MissingShaderMessagesMatchTestsuite()
            {
                List<string> expected = RefErrors("missing-shader");
                if (expected.Count == 0)
                {
                    return;
                }
                BufferedErrorHandler errors;
                ShadingSystemImpl ss = MakeSystem(out errors);
                ShaderGroup g = ss.ShaderGroupBegin("unnamed_group_1");
                EXPECT_FALSE(ss.Shader(g, "surface", "foo", "lay1"));
                EXPECT_FALSE(ss.Shader(g, "surface", "bar", "lay2"));
                EXPECT_FALSE(ss.ConnectShaders(g, "lay1", "x", "lay2", "y"));
                EXPECT_EQ(errors.errors.Count, expected.Count);
                for (int i = 0; i < expected.Count && i < errors.errors.Count; ++i)
                {
                    EXPECT_EQ(errors.errors[i].TrimEnd('\n'), expected[i]);
                }
            }

            [TEST("OslExec", "AllowConnectMessageMatchesTestsuite")]
            public static void OslExec_AllowConnectMessageMatchesTestsuite()
            {
                List<string> expected = RefErrors("allowconnect-err");
                BufferedErrorHandler errors;
                ShadingSystemImpl ss = MakeSystem(out errors);
                EXPECT_TRUE(ss.LoadMemoryCompiledShader("a", CompileShader(SrcA, errors)));
                EXPECT_TRUE(ss.LoadMemoryCompiledShader("b", CompileShader(SrcB, errors)));
                ShaderGroup g = ss.ShaderGroupBegin("unnamed_group_1");
                EXPECT_TRUE(ss.Shader(g, "surface", "a", "alayer"));
                EXPECT_TRUE(ss.Shader(g, "surface", "b", "blayer"));
                EXPECT_FALSE(ss.ConnectShaders(g, "alayer", "f_out", "blayer", "f_in"));
                EXPECT_TRUE(ss.ConnectShaders(g, "alayer", "c_out", "blayer", "c_in"));
                EXPECT_EQ(errors.errors.Count, 1);
                string want = "ConnectShaders: cannot connect to blayer.f_in because it has metadata allowconnect=0\n        group: unnamed_group_1";
                EXPECT_EQ(errors.errors[0].TrimEnd('\n'), want);
                if (expected.Count > 0)
                {
                    EXPECT_EQ(expected[0], want);
                }
            }

            [TEST("OslExec", "BadConnectionMessages")]
            public static void OslExec_BadConnectionMessages()
            {
                BufferedErrorHandler errors;
                ShadingSystemImpl ss = MakeSystem(out errors);
                EXPECT_TRUE(ss.LoadMemoryCompiledShader("a", CompileShader(SrcA, errors)));
                EXPECT_TRUE(ss.LoadMemoryCompiledShader("c", CompileShader(SrcC, errors)));
                ShaderGroup g = ss.ShaderGroupBegin("G");
                EXPECT_TRUE(ss.Shader(g, "surface", "a", "alayer"));
                EXPECT_TRUE(ss.Shader(g, "surface", "c", "clayer"));
                EXPECT_FALSE(ss.ConnectShaders(g, "clayer", "res", "alayer", "f_in"));
                EXPECT_FALSE(ss.ConnectShaders(g, "alayer", "nope", "clayer", "f_in"));
                EXPECT_FALSE(ss.ConnectShaders(g, "alayer", "f_out", "clayer", "arr"));
                EXPECT_FALSE(ss.ConnectShaders(g, "", "f_out", "clayer", "f_in"));
                EXPECT_EQ(errors.errors[0],
                          "ConnectShaders: destination layer must follow source layer (tried to connect clayer.res -> alayer.f_in)\n        group: G");
                EXPECT_EQ(errors.errors[1],
                          "ConnectShaders: \"nope\" is not a parameter or global of layer \"alayer\" (shader \"a\")");
                EXPECT_TRUE(errors.errors[2].StartsWith("ConnectShaders: cannot connect a "));
                EXPECT_TRUE(errors.errors[2].Contains("(nope)") && errors.errors[2].Contains("invalid connection\n        group: G"));
                EXPECT_TRUE(errors.errors[3].StartsWith("ConnectShaders: cannot connect a float (f_out) to a float[3] (arr)\n        group: G"));
                EXPECT_EQ(errors.errors[4], "ConnectShaders: badly formed source layer/parameter\n        group: G");

                // a bad group description reports what is wrong and where
                ShaderGroup bad = ss.ShaderGroupBegin("H", "surface", "connect a.b c.d;");
                EXPECT_TRUE(bad == null);
                string last = errors.errors[errors.errors.Count - 1];
                EXPECT_TRUE(last.StartsWith("ShaderGroupBegin: error parsing group description: "));
                EXPECT_TRUE(last.Contains("problem might be here: connect a.b c.d;"));
            }

            [TEST("OslExec", "ReadsGroupAttributesAfterOptimizing")]
            public static void OslExec_ReadsGroupAttributesAfterOptimizing()
            {
                BufferedErrorHandler errors;
                ShadingSystemImpl ss = MakeSystem(out errors);
                EXPECT_TRUE(ss.LoadMemoryCompiledShader("a", CompileShader(SrcA, errors)));
                ShaderGroup g = ss.ShaderGroupBegin("G");
                EXPECT_TRUE(ss.Shader(g, "surface", "a", "alayer"));
                EXPECT_TRUE(ss.ShaderGroupEnd(g));
                object v;
                EXPECT_TRUE(ss.getattribute(g, "num_globals_needed", TypeDescs.TypeInt, out v));
                EXPECT_EQ(g.optimized(), 1);
                EXPECT_TRUE(ss.getattribute(g, "group_id", TypeDescs.TypeInt, out v));
                EXPECT_FALSE(ss.getattribute(g, "no_such_group_attribute", TypeDescs.TypeInt, out v));
            }
        }
    }
}
