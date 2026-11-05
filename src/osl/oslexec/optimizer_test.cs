// (not in OpenShadingLanguage) optimizer_test.cs: tests of the runtime optimizer (runtimeoptimize*.cs, constfold*.cs).
//
// Three kinds of checks:
//   * folds and eliminations on known cases (what the instances look like after optimizing, at optimization levels 0 to 4);
//   * equivalence: a group built at optimization level 0 and the same group optimized at levels 1 to 4 compute the same outputs, run by the reference
//     interpreter of optinterp_test.cs over the ops of the instances;
//   * the options (lockgeom parameters, interpolated parameters, lazy layers, renderer attributes) that change what the optimizer may do.

using System;
using System.Collections.Generic;
using OIIO;
using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static unsafe class OslOptimizerTests
        {
            /// A renderer that answers one object attribute, so that getattribute() can be folded
            private sealed unsafe class AttrRenderer : RendererServices
            {
                public override bool get_attribute(ShaderGlobals* sg, bool derivatives, ulong obj, TypeDesc type, ulong name, void* val)
                {
                    if (obj == new ustring("myobj").hash() && name == new ustring("answer").hash() && type == TypeDescs.TypeInt)
                    {
                        *(int*)val = 42;
                        return true;
                    }
                    return false;
                }
            }

            private delegate void LayerBuilder(ShadingSystemImpl ss, ShaderGroup g);

            private sealed class Scenario
            {
                public string[] shaders;           // the shader source of each shader
                public string[] shadernames;       // the names the shaders are registered under
                public LayerBuilder build;         // adds the layers, parameters and connections
                public string[] renderer_outputs;  // the outputs of the last layer that are renderer outputs
                public bool lazyunconnected;
                public bool interpretable = true;  // whether the unoptimized ops are in the interpreter's repertoire
                public bool attr_renderer;
            }

            private sealed class Built
            {
                public ShadingSystemImpl ss;
                public ShaderGroup group;
                public BufferedErrorHandler errors;
            }

            private static Built Build(Scenario sc, int optlevel)
            {
                Built b = new Built();
                b.errors = new BufferedErrorHandler();
                RendererServices rs = sc.attr_renderer ? (RendererServices)new AttrRenderer() : new RendererServices();
                b.ss = new ShadingSystemImpl(rs, null, b.errors);
                ShadingSystemImpl ss = b.ss;
                EXPECT_TRUE(ss.attribute("optimize", optlevel));
                if (sc.lazyunconnected)
                {
                    EXPECT_TRUE(ss.attribute("lazyunconnected", 1));
                }
                if (sc.renderer_outputs != null)
                {
                    EXPECT_TRUE(ss.attribute("renderer_outputs", new TypeDesc(TypeDesc.STRING, sc.renderer_outputs.Length), sc.renderer_outputs));
                }
                for (int i = 0; i < sc.shaders.Length; ++i)
                {
                    OSLCompiler compiler = new OSLCompiler(b.errors);
                    string oso;
                    bool ok = compiler.compile_buffer("#include \"stdosl.h\"\n" + sc.shaders[i], out oso, new List<string> { "-I/home/claude/OpenShadingLanguage/src/shaders" }, "/home/claude/OpenShadingLanguage/src/shaders/stdosl.h", sc.shadernames[i] + ".osl");
                    if (!ok)
                    {
                        Console.WriteLine("compile of " + sc.shadernames[i] + " failed: " + string.Join(" | ", b.errors.errors));
                    }
                    ASSERT_TRUE(ok);
                    EXPECT_TRUE(ss.LoadMemoryCompiledShader(sc.shadernames[i], oso));
                }
                b.group = ss.ShaderGroupBegin("grp");
                sc.build(ss, b.group);
                EXPECT_TRUE(ss.ShaderGroupEnd(b.group));
                ss.optimize_group(b.group, null, false);
                EXPECT_EQ(b.group.optimized(), 1);
                return b;
            }

            private static readonly HashSet<string> s_trivial = new HashSet<string> { "nop", "end", "useparam" };

            /// The number of ops of an instance that do something
            private static int CountOps(ShaderInstance inst)
            {
                int n = 0;
                foreach (Opcode op in inst.ops())
                {
                    if (!s_trivial.Contains(op.opname().@string()))
                    {
                        ++n;
                    }
                }
                return n;
            }

            private static string Dump(ShaderInstance inst)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (Opcode op in inst.ops())
                {
                    sb.Append(op.opname().@string());
                    for (int a = 0; a < op.nargs(); ++a)
                    {
                        Symbol s = inst.argsymbol(op.firstarg() + a);
                        sb.Append(' ').Append(s.name().@string());
                    }
                    sb.Append("; ");
                }
                return sb.ToString();
            }

            private static int CountOp(ShaderInstance inst, string name)
            {
                int n = 0;
                foreach (Opcode op in inst.ops())
                {
                    if (op.opname().@string() == name)
                    {
                        ++n;
                    }
                }
                return n;
            }

            /// Every op that does something is an assignment of a constant
            private static bool OnlyConstantAssigns(ShaderInstance inst)
            {
                foreach (Opcode op in inst.ops())
                {
                    string name = op.opname().@string();
                    if (s_trivial.Contains(name))
                    {
                        continue;
                    }
                    if (name != "assign" || !inst.argsymbol(op.firstarg() + 1).is_constant())
                    {
                        return false;
                    }
                }
                return true;
            }

            private static float[] Outputs(Built b, string name)
            {
                OptInterp interp = new OptInterp(b.group);
                interp.Run();
                return interp.Output(b.group.nlayers() - 1, name);
            }

            private static void ExpectOutput(Built b, string name, params float[] expected)
            {
                float[] got = Outputs(b, name);
                ASSERT_TRUE(got != null);
                ASSERT_EQ(got.Length, expected.Length);
                for (int i = 0; i < expected.Length; ++i)
                {
                    EXPECT_NEAR(got[i], expected[i], 1e-5 + 1e-5 * Math.Abs(expected[i]));
                }
            }

            private static void SingleLayer(ShadingSystemImpl ss, ShaderGroup g, string shader)
            {
                EXPECT_TRUE(ss.Shader(g, "surface", shader, "lay"));
            }

            private static Scenario Single(string source, string name, params string[] outputs)
            {
                Scenario sc = new Scenario();
                sc.shaders = new string[] { source };
                sc.shadernames = new string[] { name };
                sc.renderer_outputs = outputs;
                sc.build = delegate (ShadingSystemImpl ss, ShaderGroup g) { SingleLayer(ss, g, name); };
                return sc;
            }

            private const string SrcFold = "shader fold1(float pa = 2, float pb = 3, int pm = 7, output float r = 0, output int i = 0, output color c = 0)\n"
                                           + "{ float a = pa * pb; r = a + 1; i = pm % 4; c = color(1, 2, 3) * pa; }\n";

            private const string SrcIf = "shader cif(output float r = 0, output float q = 0)\n"
                                         + "{ float x = 1; if (x > 0) r = 10; else r = 20; if (x < 0) { q = 1; } else { q = 2; } }\n";

            private const string SrcLoops = "float sumto(int n) { float s = 0; for (int i = 1; i <= n; i += 1) { s += i; } return s; }\n"
                                            + "shader loops(float scale = 2, output float r = 0, output int cnt = 0, output color c = 0)\n"
                                            + "{\n"
                                            + "  float acc = 0;\n"
                                            + "  for (int i = 0; i < 5; i += 1) { if (i == 3) continue; acc += i * scale; }\n"
                                            + "  int k = 0; while (k < 4) { k += 1; if (k == 3) break; }\n"
                                            + "  int j = 0; do { j += 2; } while (j < 7);\n"
                                            + "  r = acc + sumto(4);\n"
                                            + "  cnt = k * 100 + j;\n"
                                            + "  float arr[4] = {1, 2, 3, 4};\n"
                                            + "  arr[2] = acc;\n"
                                            + "  c = color(arr[0], arr[2], arr[3]);\n"
                                            + "}\n";

            private const string SrcMisc = "shader misc(float a = 1.5, float b = 0.25, output float r1 = 0, output float r2 = 0, output color c = 0,\n"
                                           + "            output int i = 0)\n"
                                           + "{\n"
                                           + "  float x = a * 2 + b;\n"
                                           + "  float y = x - b;\n"
                                           + "  r1 = mix(a, b, 0.5) + clamp(y, 0, 1) + min(a, b) + max(a, b);\n"
                                           + "  r2 = (x > 2) ? 7 : 9;\n"
                                           + "  color cc = color(a, b, 0.5);\n"
                                           + "  c = cc * 2 + color(1);\n"
                                           + "  i = int(a * 10) % 4;\n"
                                           + "}\n";

            private const string SrcArr = "shader arrs(output float r = 0, output color c = 0)\n"
                                           + "{\n"
                                           + "  float a[3] = {1, 2, 3};\n"
                                           + "  a[1] = 10;\n"
                                           + "  r = a[0] + a[1] + a[2];\n"
                                           + "  color k = color(0); k[0] = 1; k[1] = 2; k[2] = 3; c = k;\n"
                                           + "}\n";

            private const string SrcStr = "shader strs(string base = \"abc\", output string s = \"\", output int n = 0, output int h = 0)\n"
                                          + "{\n"
                                          + "  s = concat(base, \"def\");\n"
                                          + "  n = strlen(s) + startswith(s, \"ab\") + endswith(s, \"ef\");\n"
                                          + "  string t = substr(s, 1, 3);\n"
                                          + "  h = (t == \"bcd\") ? 1 : 0;\n"
                                          + "  s = format(\"%s-%d-%.2f\", t, n, 1.5);\n"
                                          + "}\n";

            private const string SrcMath = "shader mth(output float r = 0, output color c = 0)\n"
                                           + "{\n"
                                           + "  r = sqrt(16) + floor(2.7) + ceil(2.1) + abs(-3) + pow(2, 10) + radians(180) + degrees(3.14159265);\n"
                                           + "  c = normalize(vector(0, 3, 4)) + color(dot(vector(1, 2, 3), vector(4, 5, 6)));\n"
                                           + "}\n";

            private const string SrcUp = "shader up(float k = 5, output float o = 0, output color oc = 0) { o = k; oc = color(k, 2, 3); }\n";
            private const string SrcDown = "shader down(float in = 0, color cin = 0, output float r = 0, output color rc = 0)\n"
                                           + "{ r = in * 2; rc = cin + 1; }\n";
            private const string SrcSide = "shader side(float k = 1, output float q = 0) { q = k * 3; }\n";

            private const string SrcLock = "shader lg(float f_in = 1, output float r = 0) { r = f_in * 2 + 1; }\n";

            private const string SrcAttr = "shader ga(output int r = 0, output int ok = 0, output string sn = \"\")\n"
                                           + "{ int w = 0; ok = getattribute(\"myobj\", \"answer\", w); r = w; string s; getattribute(\"shader:shadername\", s); sn = s; }\n";

            private const string SrcCopy = "shader cp(float f = 3, output float r = 0, output float r2 = 0)\n"
                                           + "{ float tt = f; float uu = tt; r = uu + 0; float w = r2 + 1; r2 = 4; r2 = w; }\n";

            private static Scenario Connected()
            {
                Scenario sc = new Scenario();
                sc.shaders = new string[] { SrcUp, SrcDown };
                sc.shadernames = new string[] { "up", "down" };
                sc.renderer_outputs = new string[] { "r", "rc" };
                sc.build = delegate (ShadingSystemImpl ss, ShaderGroup g)
                {
                    EXPECT_TRUE(ss.Shader(g, "surface", "up", "uplayer"));
                    EXPECT_TRUE(ss.Shader(g, "surface", "down", "downlayer"));
                    EXPECT_TRUE(ss.ConnectShaders(g, "uplayer", "o", "downlayer", "in"));
                    EXPECT_TRUE(ss.ConnectShaders(g, "uplayer", "oc", "downlayer", "cin"));
                };
                return sc;
            }

            private static Scenario WithUnusedLayer()
            {
                Scenario sc = new Scenario();
                sc.shaders = new string[] { SrcUp, SrcSide, SrcDown };
                sc.shadernames = new string[] { "up", "side", "down" };
                sc.renderer_outputs = new string[] { "r", "rc" };
                sc.lazyunconnected = true;
                sc.build = delegate (ShadingSystemImpl ss, ShaderGroup g)
                {
                    EXPECT_TRUE(ss.Shader(g, "surface", "up", "uplayer"));
                    EXPECT_TRUE(ss.Shader(g, "surface", "side", "sidelayer"));
                    EXPECT_TRUE(ss.Shader(g, "surface", "down", "downlayer"));
                    EXPECT_TRUE(ss.ConnectShaders(g, "uplayer", "o", "downlayer", "in"));
                    EXPECT_TRUE(ss.ConnectShaders(g, "uplayer", "oc", "downlayer", "cin"));
                };
                return sc;
            }

            [TEST("OslOpt", "FoldsConstantArithmetic")]
            public static void OslOpt_FoldsConstantArithmetic()
            {
                Scenario sc = Single(SrcFold, "fold1", "r", "i", "c");
                Built b0 = Build(sc, 0);
                EXPECT_GT(CountOp(b0.group[0], "mul"), 0);
                EXPECT_GT(CountOp(b0.group[0], "add"), 0);
                EXPECT_GT(CountOp(b0.group[0], "mod"), 0);
                for (int level = 2; level <= 4; ++level)
                {
                    Built b = Build(sc, level);
                    ShaderInstance inst = b.group[0];
                    EXPECT_TRUE(OnlyConstantAssigns(inst));
                    EXPECT_EQ(CountOps(inst), 3);
                    ExpectOutput(b, "r", 7.0f);
                    ExpectOutput(b, "i", 3.0f);
                    ExpectOutput(b, "c", 2.0f, 4.0f, 6.0f);
                }
            }

            [TEST("OslOpt", "EliminatesConstantConditions")]
            public static void OslOpt_EliminatesConstantConditions()
            {
                Scenario sc = Single(SrcIf, "cif", "r", "q");
                Built b0 = Build(sc, 0);
                EXPECT_EQ(CountOp(b0.group[0], "if"), 2);
                for (int level = 2; level <= 4; ++level)
                {
                    Built b = Build(sc, level);
                    EXPECT_EQ(CountOp(b.group[0], "if"), 0);
                    EXPECT_EQ(CountOp(b.group[0], "gt"), 0);
                    EXPECT_TRUE(OnlyConstantAssigns(b.group[0]));
                    ExpectOutput(b, "r", 10.0f);
                    ExpectOutput(b, "q", 2.0f);
                }
            }

            [TEST("OslOpt", "FoldsArraysAndComponents")]
            public static void OslOpt_FoldsArraysAndComponents()
            {
                Scenario sc = Single(SrcArr, "arrs", "r", "c");
                for (int level = 2; level <= 4; ++level)
                {
                    Built b = Build(sc, level);
                    // Like the C++ folders, aref of a local array that was assigned to is not folded, but the color components are
                    EXPECT_EQ(CountOp(b.group[0], "compassign"), 0);
                    EXPECT_EQ(CountOp(b.group[0], "aassign"), 1);
                    ExpectOutput(b, "r", 14.0f);
                    ExpectOutput(b, "c", 1.0f, 2.0f, 3.0f);
                }
            }

            [TEST("OslOpt", "FoldsStringOps")]
            public static void OslOpt_FoldsStringOps()
            {
                Scenario sc = Single(SrcStr, "strs", "s", "n", "h");
                sc.interpretable = false;
                for (int level = 2; level <= 4; ++level)
                {
                    Built b = Build(sc, level);
                    EXPECT_TRUE(OnlyConstantAssigns(b.group[0]));
                    OptInterp interp = new OptInterp(b.group);
                    interp.Run();
                    EXPECT_EQ(interp.OutputString(0, "s"), "bcd-8-1.50");
                    EXPECT_NEAR(interp.Output(0, "n")[0], 8, 0);
                    EXPECT_NEAR(interp.Output(0, "h")[0], 1, 0);
                }
            }

            [TEST("OslOpt", "FoldsMathFunctions")]
            public static void OslOpt_FoldsMathFunctions()
            {
                Scenario sc = Single(SrcMath, "mth", "r", "c");
                sc.interpretable = false;
                for (int level = 2; level <= 4; ++level)
                {
                    Built b = Build(sc, level);
                    EXPECT_TRUE(OnlyConstantAssigns(b.group[0]));
                    // sqrt(16) + floor(2.7) + ceil(2.1) + abs(-3) + pow(2,10) + radians(180) + degrees(pi)
                    double expect = 4 + 2 + 3 + 3 + 1024 + Math.PI + 180.0;
                    ExpectOutput(b, "r", (float)expect);
                    // normalize((0,3,4)) + color(32)
                    ExpectOutput(b, "c", 32.0f, 32.6f, 32.8f);
                }
            }

            [TEST("OslOpt", "FoldsGetAttribute")]
            public static void OslOpt_FoldsGetAttribute()
            {
                Scenario sc = Single(SrcAttr, "ga", "r", "ok", "sn");
                sc.attr_renderer = true;
                sc.interpretable = false;
                Built b0 = Build(sc, 0);
                EXPECT_GT(CountOp(b0.group[0], "getattribute"), 0);
                Built b = Build(sc, 3);
                EXPECT_EQ(CountOp(b.group[0], "getattribute"), 0);  // both the object attribute and shader:shadername are known at optimization time
                EXPECT_TRUE(OnlyConstantAssigns(b.group[0]));
                OptInterp interp = new OptInterp(b.group);
                interp.Run();
                EXPECT_NEAR(interp.Output(0, "r")[0], 42, 0);
                EXPECT_NEAR(interp.Output(0, "ok")[0], 1, 0);
            }

            [TEST("OslOpt", "ConnectionsToConstantsAreSevered")]
            public static void OslOpt_ConnectionsToConstantsAreSevered()
            {
                Scenario sc = Connected();
                Built b0 = Build(sc, 0);
                EXPECT_EQ(b0.group[1].nconnections(), 2);
                EXPECT_FALSE(b0.group[0].unused());
                for (int level = 2; level <= 4; ++level)
                {
                    Built b = Build(sc, level);
                    EXPECT_TRUE(b.group[0].unused());
                    EXPECT_EQ(b.group[1].nconnections(), 0);
                    EXPECT_TRUE(OnlyConstantAssigns(b.group[1]));
                    ExpectOutput(b, "r", 10.0f);
                    ExpectOutput(b, "rc", 6.0f, 3.0f, 4.0f);
                }
            }

            [TEST("OslOpt", "RemovesUnusedLayers")]
            public static void OslOpt_RemovesUnusedLayers()
            {
                Scenario sc = WithUnusedLayer();
                for (int level = 1; level <= 4; ++level)
                {
                    Built b = Build(sc, level);
                    EXPECT_TRUE(b.group[1].unused());  // the layer that nobody reads

                    ExpectOutput(b, "r", 10.0f);
                }
            }

            [TEST("OslOpt", "PropagatesCopies")]
            public static void OslOpt_PropagatesCopies()
            {
                Scenario sc = Single(SrcCopy, "cp", "r", "r2");
                Built b0 = Build(sc, 0);
                int ops0 = CountOps(b0.group[0]);
                for (int level = 2; level <= 4; ++level)
                {
                    Built b = Build(sc, level);
                    EXPECT_LT(CountOps(b.group[0]), ops0);
                    EXPECT_EQ(CountOp(b.group[0], "add"), 0);  // r2 is still 0 when 'r2 + 1' is computed, and uu + 0 is a copy
                    ExpectOutput(b, "r", 3.0f);
                    ExpectOutput(b, "r2", 1.0f);
                }
            }

            [TEST("OslOpt", "LockGeomParametersBecomeConstants")]
            public static void OslOpt_LockGeomParametersBecomeConstants()
            {
                Scenario sc = Single(SrcLock, "lg", "r");
                Built locked = null;
                {
                    Scenario s = new Scenario();
                    s.shaders = sc.shaders;
                    s.shadernames = sc.shadernames;
                    s.renderer_outputs = sc.renderer_outputs;
                    s.build = delegate (ShadingSystemImpl ss, ShaderGroup g)
                    {
                        EXPECT_TRUE(ss.Parameter(g, "f_in", TypeDescs.TypeFloat, new float[] { 3.0f }));
                        SingleLayer(ss, g, "lg");
                    };
                    locked = Build(s, 3);
                    EXPECT_TRUE(OnlyConstantAssigns(locked.group[0]));
                    ExpectOutput(locked, "r", 7.0f);
                }
                {
                    // An interpolated parameter can be overridden by the geometry, so the arithmetic stays
                    Scenario s = new Scenario();
                    s.shaders = sc.shaders;
                    s.shadernames = sc.shadernames;
                    s.renderer_outputs = sc.renderer_outputs;
                    s.build = delegate (ShadingSystemImpl ss, ShaderGroup g)
                    {
                        EXPECT_TRUE(ss.Parameter(g, "f_in", TypeDescs.TypeFloat, new float[] { 3.0f }, ParamHints.interpolated));
                        SingleLayer(ss, g, "lg");
                    };
                    Built interp = Build(s, 3);
                    EXPECT_GT(CountOp(interp.group[0], "mul"), 0);
                    EXPECT_FALSE(OnlyConstantAssigns(interp.group[0]));
                    EXPECT_EQ(interp.group.m_userdata_names.Count, 1);
                }
            }

            [TEST("OslOpt", "OptimizationLevelsShrinkTheCode")]
            public static void OslOpt_OptimizationLevelsShrinkTheCode()
            {
                Scenario[] scs = new Scenario[] { Single(SrcFold, "fold1", "r", "i", "c"), Single(SrcLoops, "loops", "r", "cnt", "c"),
                                                  Single(SrcMisc, "misc", "r1", "r2", "c", "i"), Single(SrcArr, "arrs", "r", "c") };
                foreach (Scenario sc in scs)
                {
                    int prev = int.MaxValue;
                    for (int level = 0; level <= 4; ++level)
                    {
                        Built b = Build(sc, level);
                        int n = CountOps(b.group[0]);
                        EXPECT_LE(n, prev);
                        prev = n;
                    }
                    Built b0 = Build(sc, 0);
                    Built b3 = Build(sc, 3);
                    EXPECT_LT(CountOps(b3.group[0]), CountOps(b0.group[0]));
                    EXPECT_LE(b3.group[0].symbols().Count, b0.group[0].symbols().Count);
                }
            }

            private static void CheckEquivalent(Scenario sc, params string[] outputs)
            {
                Built reference = Build(sc, 0);
                OptInterp rinterp = new OptInterp(reference.group);
                rinterp.Run();
                int last = reference.group.nlayers() - 1;
                for (int level = 1; level <= 4; ++level)
                {
                    Built b = Build(sc, level);
                    EXPECT_EQ(b.errors.errors.Count, 0);
                    OptInterp interp = new OptInterp(b.group);
                    interp.Run();
                    foreach (string o in outputs)
                    {
                        float[] want = rinterp.Output(last, o);
                        float[] got = interp.Output(b.group.nlayers() - 1, o);
                        ASSERT_TRUE(want != null);
                        ASSERT_TRUE(got != null);
                        ASSERT_EQ(got.Length, want.Length);
                        for (int i = 0; i < want.Length; ++i)
                        {
                            EXPECT_NEAR(got[i], want[i], 1e-4 + 1e-4 * Math.Abs(want[i]));
                        }
                    }
                }
            }

            [TEST("OslOpt", "OptimizedGroupsAreEquivalent")]
            public static void OslOpt_OptimizedGroupsAreEquivalent()
            {
                CheckEquivalent(Single(SrcFold, "fold1", "r", "i", "c"), "r", "i", "c");
                CheckEquivalent(Single(SrcIf, "cif", "r", "q"), "r", "q");
                CheckEquivalent(Single(SrcLoops, "loops", "r", "cnt", "c"), "r", "cnt", "c");
                CheckEquivalent(Single(SrcMisc, "misc", "r1", "r2", "c", "i"), "r1", "r2", "c", "i");
                CheckEquivalent(Single(SrcArr, "arrs", "r", "c"), "r", "c");
                CheckEquivalent(Single(SrcCopy, "cp", "r", "r2"), "r", "r2");
                CheckEquivalent(Connected(), "r", "rc");
                CheckEquivalent(WithUnusedLayer(), "r", "rc");
            }

            [TEST("OslOpt", "LoopsKeepTheirMeaning")]
            public static void OslOpt_LoopsKeepTheirMeaning()
            {
                // acc = (0+1+2+4)*2 = 14, sumto(4) = 10, k stops at 3, j = 8, arr = {1,2,14,4}
                Scenario sc = Single(SrcLoops, "loops", "r", "cnt", "c");
                for (int level = 0; level <= 4; ++level)
                {
                    Built b = Build(sc, level);
                    ExpectOutput(b, "r", 24.0f);
                    ExpectOutput(b, "cnt", 308.0f);
                    ExpectOutput(b, "c", 1.0f, 14.0f, 4.0f);
                }
            }

            [TEST("OslOpt", "SerializesTheOptimizedGroup")]
            public static void OslOpt_SerializesTheOptimizedGroup()
            {
                Built b = Build(Single(SrcFold, "fold1", "r", "i", "c"), 3);
                RuntimeOptimizer rop = new RuntimeOptimizer(b.ss, b.group, b.ss.get_context(b.ss.create_thread_info()));
                rop.set_inst(0);
                rop.find_basic_blocks();
                string text = rop.serialize();
                EXPECT_TRUE(text.StartsWith("Shader fold1\n"));
                EXPECT_TRUE(text.Contains("assign r"));
                EXPECT_TRUE(text.Contains("  code:\n"));
                rop.Dispose();
            }

            [TEST("OslOpt", "OptimizerOptionsTurnPassesOff")]
            public static void OslOpt_OptimizerOptionsTurnPassesOff()
            {
                Scenario sc = Single(SrcFold, "fold1", "r", "i", "c");
                Built full = Build(sc, 3);
                // with constant folding off, the arithmetic stays even at the highest level
                Scenario off = Single(SrcFold, "fold1", "r", "i", "c");
                Built b = new Built();
                b.errors = new BufferedErrorHandler();
                b.ss = new ShadingSystemImpl(new RendererServices(), null, b.errors);
                EXPECT_TRUE(b.ss.attribute("optimize", 3));
                EXPECT_TRUE(b.ss.attribute("opt_constant_fold", 0));
                EXPECT_TRUE(b.ss.attribute("renderer_outputs", new TypeDesc(TypeDesc.STRING, 3), new string[] { "r", "i", "c" }));
                OSLCompiler compiler = new OSLCompiler(b.errors);
                string oso;
                EXPECT_TRUE(compiler.compile_buffer("#include \"stdosl.h\"\n" + SrcFold, out oso, new List<string> { "-I/home/claude/OpenShadingLanguage/src/shaders" }, "/home/claude/OpenShadingLanguage/src/shaders/stdosl.h", "fold1.osl"));
                EXPECT_TRUE(b.ss.LoadMemoryCompiledShader("fold1", oso));
                b.group = b.ss.ShaderGroupBegin("grp");
                SingleLayer(b.ss, b.group, "fold1");
                EXPECT_TRUE(b.ss.ShaderGroupEnd(b.group));
                b.ss.optimize_group(b.group, null, false);
                EXPECT_GT(CountOps(b.group[0]), CountOps(full.group[0]));
                ExpectOutput(b, "r", 7.0f);
            }
        }
    }
}
