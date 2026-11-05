// (not in OpenShadingLanguage) il_test.cs: tests of the IL backend -- shaders are compiled with the managed oslc, built into a group, executed through
// the generated IL and their outputs checked, the way testshade does.

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
        /// Builds and runs shader groups for the tests
        public sealed unsafe class IlRun : IDisposable
        {
            public ShadingSystemImpl ss;
            public BufferedErrorHandler errors;
            public ShaderGroup group;
            public ShadingContext ctx;
            public PerThreadInfo thread;
            public ShaderGlobals sg;
            public const string StdOsl = "/home/claude/OpenShadingLanguage/src/shaders/stdosl.h";
            private bool m_disposed;

            public IlRun(RendererServices rs = null)
            {
                errors = new BufferedErrorHandler();
                ss = new ShadingSystemImpl(rs != null ? rs : new RendererServices(), null, errors);
                ss.attribute("lockgeom", 1);
                sg = new ShaderGlobals();
                sg.P = new Vec3(1.0f, 2.0f, 3.0f);
                sg.N = new Vec3(0.0f, 0.0f, 1.0f);
                sg.Ng = new Vec3(0.0f, 0.0f, 1.0f);
                sg.I = new Vec3(0.0f, 0.0f, -1.0f);
                sg.u = 0.25f;
                sg.v = 0.75f;
                sg.dPdu = new Vec3(1.0f, 0.0f, 0.0f);
                sg.dPdv = new Vec3(0.0f, 1.0f, 0.0f);
                sg.dudx = 0.01f;
                sg.dudy = 0.0f;
                sg.dvdx = 0.0f;
                sg.dvdy = 0.01f;
                sg.dPdx = new Vec3(0.01f, 0.0f, 0.0f);
                sg.dPdy = new Vec3(0.0f, 0.01f, 0.0f);
                sg.surfacearea = 1.0f;
                sg.raytype = 1;
            }

            public bool Load(string name, string source)
            {
                OSLCompiler compiler = new OSLCompiler(errors);
                string oso;
                bool ok = compiler.compile_buffer(source, out oso, new List<string>(), StdOsl, name + ".osl");
                if (!ok)
                {
                    Console.WriteLine("compile of " + name + " failed: " + string.Join("\n", errors.errors));
                    return false;
                }
                return ss.LoadMemoryCompiledShader(name, oso);
            }

            /// Makes a one-layer group from the source; the outputs are what the test reads
            public bool Single(string source, params string[] outputs)
            {
                if (!Load("t", source))
                {
                    return false;
                }
                group = ss.ShaderGroupBegin("g");
                ss.Shader(group, "surface", "t", "t");
                ss.ShaderGroupEnd(group);
                if (outputs.Length != 0)
                {
                    ss.attribute(group, "renderer_outputs", new TypeDesc(TypeDesc.STRING, outputs.Length), outputs);
                }
                return Execute();
            }

            /// Several layers (shader names are layer names); 'connections' are "src.out:dst.in"
            public bool Multi(string[] names, string[] sources, string[] connections, params string[] outputs)
            {
                if (!Prepare(names, sources, connections, outputs))
                {
                    return false;
                }
                return Execute();
            }

            /// Builds the group without executing it
            public bool Prepare(string[] names, string[] sources, string[] connections, params string[] outputs)
            {
                for (int i = 0; i < names.Length; ++i)
                {
                    if (!Load(names[i], sources[i]))
                    {
                        return false;
                    }
                }
                group = ss.ShaderGroupBegin("g");
                for (int i = 0; i < names.Length; ++i)
                {
                    ss.Shader(group, "surface", names[i], names[i]);
                }
                foreach (string c in connections)
                {
                    string[] sd = c.Split(':');
                    string[] s1 = sd[0].Split('.');
                    string[] d1 = sd[1].Split('.');
                    ss.ConnectShaders(group, s1[0], s1[1], d1[0], d1[1]);
                }
                ss.ShaderGroupEnd(group);
                if (outputs.Length != 0)
                {
                    ss.attribute(group, "renderer_outputs", new TypeDesc(TypeDesc.STRING, outputs.Length), outputs);
                }
                return true;
            }

            public bool Execute()
            {
                thread = ss.create_thread_info();
                ctx = ss.get_context(thread);
                bool ok = ss.execute(ctx, group, 0, 0, ref sg, null, null);
                return ok;
            }

            private void* data(string layer, string name)
            {
                TypeDesc t;
                Symbol sym = ss.get_symbol(ctx, new ustring(layer), new ustring(name), out t);
                if (sym == null)
                {
                    return null;
                }
                object managed;
                return ctx.symbol_data(sym, out managed);
            }

            public float F(string name, int index = 0, string layer = "t")
            {
                float* p = (float*)data(layer, name);
                return p != null ? p[index] : float.NaN;
            }

            public int I(string name, int index = 0, string layer = "t")
            {
                int* p = (int*)data(layer, name);
                return p != null ? p[index] : int.MinValue;
            }

            public Vec3 V(string name, int index = 0, string layer = "t")
            {
                float* p = (float*)data(layer, name);
                return p != null ? new Vec3(p[index * 3], p[index * 3 + 1], p[index * 3 + 2]) : new Vec3(float.NaN, float.NaN, float.NaN);
            }

            public string S(string name, int index = 0, string layer = "t")
            {
                ulong* p = (ulong*)data(layer, name);
                return p != null ? OslStrings.from_hash(p[index]).@string() : null;
            }

            public string Errors()
            {
                if (ctx != null)
                {
                    ctx.process_errors();
                }
                return string.Join("\n", errors.errors);
            }

            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    if (ctx != null)
                    {
                        ss.release_context(ctx);
                        ctx = null;
                    }
                }
            }
        }

        public static unsafe class OslIlTests
        {

            private static void Check(string name, string src, string[] outs, Action<IlRun> verify)
            {
                using (IlRun r = new IlRun())
                {
                    bool ok = r.Single(src, outs);
                    EXPECT_TRUE(ok);
                    string err = r.Errors();
                    if (err.Length != 0)
                    {
                        Console.WriteLine(name + " errors: " + err);
                    }
                    verify(r);
                }
            }

            [TEST("OslIL", "Arithmetic")]
            public static void OslIL_Arithmetic()
            {
                Check("arith", @"shader t(output float a = 0, output float b = 0, output int c = 0, output int d = 0, output int e = 0, output float g = 0,
                        output int h = 0, output int i = 0, output int j = 0) {
                    float x = 3.5, y = 2.0;
                    a = x * y - x / y + x;      // 7 - 1.75 + 3.5 = 8.75
                    b = -x + fmod(x, y);        // -3.5 + 1.5 = -2
                    int p = 17, q = 5;
                    c = p % q;                  // 2
                    d = p / q;                  // 3
                    e = (p << 2) | (q & 3) ^ 1; // 68 | (1 ^ 1)=0 -> 68  (precedence: ^ binds tighter than |)
                    g = floor(x) + ceil(x) + round(x) + abs(-y); // 3 + 4 + 4 + 2 = 13
                    h = (p > q) && (x < y);     // 0
                    i = (p > q) || (x < y);     // 1
                    j = !(p == q) + (p >= 17) + (q <= 4) + (p != q); // 1+1+0+1 = 3
                }", new string[] { "a", "b", "c", "d", "e", "g", "h", "i", "j" }, (IlRun r) =>
                {
                    EXPECT_FLOAT_EQ(r.F("a"), 8.75f);
                    EXPECT_FLOAT_EQ(r.F("b"), -2.0f);
                    EXPECT_EQ(r.I("c"), 2);
                    EXPECT_EQ(r.I("d"), 3);
                    EXPECT_EQ(r.I("e"), 68);
                    EXPECT_FLOAT_EQ(r.F("g"), 13.0f);
                    EXPECT_EQ(r.I("h"), 0);
                    EXPECT_EQ(r.I("i"), 1);
                    EXPECT_EQ(r.I("j"), 3);
                });
            }

            [TEST("OslIL", "Vectors")]
            public static void OslIL_Vectors()
            {
                Check("vec", @"shader t(output color c = 0, output vector vn = 0, output float d = 0, output float l = 0, output vector cr = 0,
                        output float cx = 0, output point p = 0, output normal n = 0) {
                    color a = color(1, 2, 3), b = color(0.5, 0.25, 2);
                    c = a * b + 1;                       // (1.5, 1.5, 7)
                    vector ux = vector(1, 0, 0), wy = vector(0, 1, 0);
                    cr = cross(ux, wy);                    // (0,0,1)
                    d = dot(vector(1, 2, 3), vector(4, 5, 6));  // 32
                    l = length(vector(3, 4, 12));        // 13
                    vn = normalize(vector(0, 3, 4));      // (0,.6,.8)
                    cx = c[2];                            // 7
                    c[0] = 9;                             // (9,1.5,7)
                    p = point(1, 2, 3) * 2;
                    n = normal(0, 0, 1) + normal(1, 0, 0);
                }", new string[] { "c", "vn", "d", "l", "cr", "cx", "p", "n" }, (IlRun r) =>
                {
                    Vec3 c = r.V("c");
                    EXPECT_FLOAT_EQ(c.x, 9.0f);
                    EXPECT_FLOAT_EQ(c.y, 1.5f);
                    EXPECT_FLOAT_EQ(c.z, 7.0f);
                    Vec3 cr = r.V("cr");
                    EXPECT_FLOAT_EQ(cr.z, 1.0f);
                    EXPECT_FLOAT_EQ(r.F("d"), 32.0f);
                    EXPECT_FLOAT_EQ(r.F("l"), 13.0f);
                    Vec3 v = r.V("vn");
                    EXPECT_NEAR(v.y, 0.6, 1e-6);
                    EXPECT_NEAR(v.z, 0.8, 1e-6);
                    EXPECT_FLOAT_EQ(r.F("cx"), 7.0f);
                    Vec3 p = r.V("p");
                    EXPECT_FLOAT_EQ(p.z, 6.0f);
                    Vec3 n = r.V("n");
                    EXPECT_FLOAT_EQ(n.x, 1.0f);
                    EXPECT_FLOAT_EQ(n.z, 1.0f);
                });
            }

            [TEST("OslIL", "ControlFlow")]
            public static void OslIL_ControlFlow()
            {
                Check("flow", @"shader t(output int a = 0, output int b = 0, output int c = 0, output int d = 0, output int e = 0) {
                    int x = 5;
                    if (x > 3) { a = 1; } else { a = 2; }
                    if (x < 3) { b = 1; } else if (x == 5) { b = 2; } else { b = 3; }
                    for (int i = 0; i < 10; ++i) { c += i; }                 // 45
                    int k = 0;
                    while (k < 100) { k += 7; if (k > 50) break; }           // 56
                    d = k;
                    int n = 0;
                    do { n++; if (n == 3) continue; e += n; } while (n < 6); // 1+2+4+5+6 = 18
                }", new string[] { "a", "b", "c", "d", "e" }, (IlRun r) =>
                {
                    EXPECT_EQ(r.I("a"), 1);
                    EXPECT_EQ(r.I("b"), 2);
                    EXPECT_EQ(r.I("c"), 45);
                    EXPECT_EQ(r.I("d"), 56);
                    EXPECT_EQ(r.I("e"), 18);
                });
            }

            [TEST("OslIL", "NestedLoops")]
            public static void OslIL_NestedLoops()
            {
                Check("nested", @"shader t(output int s = 0, output int t2 = 0) {
                    for (int i = 0; i < 5; ++i) {
                        for (int j = 0; j < 5; ++j) {
                            if (j == 3) break;
                            if (i == 2) continue;
                            s += i * 10 + j;
                        }
                    }
                    int cnt = 0;
                    for (int i = 0; i < 4; ++i) for (int j = i; j < 4; ++j) cnt++;
                    t2 = cnt;     // 10
                }", new string[] { "s", "t2" }, (IlRun r) =>
                {
                    // i in {0,1,3,4}, j in {0,1,2}: sum(i*10*3 + 3) over i=0,1,3,4 = 30*8+12 = 252
                    EXPECT_EQ(r.I("s"), 252);
                    EXPECT_EQ(r.I("t2"), 10);
                });
            }

            [TEST("OslIL", "Arrays")]
            public static void OslIL_Arrays()
            {
                Check("arr", @"shader t(output float s = 0, output int n = 0, output float m = 0, output color cc = 0) {
                    float a[5] = {1, 2, 3, 4, 5};
                    for (int i = 0; i < 5; ++i) s += a[i] * a[i];   // 55
                    n = arraylength(a);
                    float b[5];
                    for (int i = 0; i < 5; ++i) b[i] = a[4 - i];
                    m = b[0];                                        // 5
                    color ca[2] = {color(1, 0, 0), color(0, 1, 1)};
                    cc = ca[0] + ca[1];                              // (1,1,1)
                    a = b;
                    s += a[1];                                       // 59
                }", new string[] { "s", "n", "m", "cc" }, (IlRun r) =>
                {
                    EXPECT_FLOAT_EQ(r.F("s"), 59.0f);
                    EXPECT_EQ(r.I("n"), 5);
                    EXPECT_FLOAT_EQ(r.F("m"), 5.0f);
                    Vec3 cc = r.V("cc");
                    EXPECT_FLOAT_EQ(cc.x, 1.0f);
                    EXPECT_FLOAT_EQ(cc.y, 1.0f);
                    EXPECT_FLOAT_EQ(cc.z, 1.0f);
                });
            }

            [TEST("OslIL", "Structs")]
            public static void OslIL_Structs()
            {
                Check("struct", @"struct S { float a; int b; color c; };
                    shader t(output float fa = 0, output int ib = 0, output float cz = 0) {
                    S s;
                    s.a = 2.5; s.b = 4; s.c = color(1, 2, 3);
                    S w = s;
                    w.a *= 2;
                    fa = s.a + w.a;     // 7.5
                    ib = w.b * 2;       // 8
                    cz = w.c[2];        // 3
                }", new string[] { "fa", "ib", "cz" }, (IlRun r) =>
                {
                    EXPECT_FLOAT_EQ(r.F("fa"), 7.5f);
                    EXPECT_EQ(r.I("ib"), 8);
                    EXPECT_FLOAT_EQ(r.F("cz"), 3.0f);
                });
            }

            [TEST("OslIL", "Functions")]
            public static void OslIL_Functions()
            {
                Check("func", @"float sq(float x) { return x * x; }
                    int tri(int n) { int s = 0; for (int i = 1; i <= n; ++i) { if (i > 100) return -1; s += i; } return s; }
                    void both(float x, output float a, output float b) { a = x + 1; b = x - 1; }
                    float early(float x) { if (x > 0) return 1; return -1; }
                    shader t(output float r1 = 0, output int r2 = 0, output float a = 0, output float b = 0, output float e1 = 0, output float e2 = 0) {
                    r1 = sq(3) + sq(4);
                    r2 = tri(10);
                    both(10, a, b);
                    e1 = early(3);
                    e2 = early(-3);
                }", new string[] { "r1", "r2", "a", "b", "e1", "e2" }, (IlRun r) =>
                {
                    EXPECT_FLOAT_EQ(r.F("r1"), 25.0f);
                    EXPECT_EQ(r.I("r2"), 55);
                    EXPECT_FLOAT_EQ(r.F("a"), 11.0f);
                    EXPECT_FLOAT_EQ(r.F("b"), 9.0f);
                    EXPECT_FLOAT_EQ(r.F("e1"), 1.0f);
                    EXPECT_FLOAT_EQ(r.F("e2"), -1.0f);
                });
            }

            [TEST("OslIL", "Matrices")]
            public static void OslIL_Matrices()
            {
                Check("mat", @"shader t(output float m00 = 0, output float m33 = 0, output float det = 0, output point p = 0, output vector vv = 0,
                        output float t30 = 0) {
                    matrix m = matrix(2, 0, 0, 0,  0, 3, 0, 0,  0, 0, 4, 0,  1, 2, 3, 1);
                    m00 = m[0][0];
                    m33 = m[3][3];
                    det = determinant(m);                 // 24
                    p = transform(m, point(1, 1, 1));     // (3,5,7)
                    vv = transform(m, vector(1, 1, 1));    // (2,3,4)
                    matrix tt = transpose(m);
                    t30 = tt[0][3];                       // 1
                    m[1][1] = 7;
                    m00 += m[1][1];                       // 9
                }", new string[] { "m00", "m33", "det", "p", "vv", "t30" }, (IlRun r) =>
                {
                    EXPECT_FLOAT_EQ(r.F("m00"), 9.0f);
                    EXPECT_FLOAT_EQ(r.F("m33"), 1.0f);
                    EXPECT_FLOAT_EQ(r.F("det"), 24.0f);
                    Vec3 p = r.V("p");
                    EXPECT_FLOAT_EQ(p.x, 3.0f);
                    EXPECT_FLOAT_EQ(p.y, 5.0f);
                    EXPECT_FLOAT_EQ(p.z, 7.0f);
                    EXPECT_FLOAT_EQ(r.V("vv").y, 3.0f);
                    EXPECT_FLOAT_EQ(r.F("t30"), 1.0f);
                });
            }

            [TEST("OslIL", "Strings")]
            public static void OslIL_Strings()
            {
                Check("str", @"shader t(output string s = """", output int n = 0, output int sw = 0, output string sub = """", output int m = 0) {
                    s = concat(""foo"", ""bar"");
                    n = strlen(s);
                    sw = startswith(s, ""foo"") + 2 * endswith(s, ""bar"");
                    sub = substr(s, 1, 3);
                    m = regex_search(s, ""o+b"");
                }", new string[] { "s", "n", "sw", "sub", "m" }, (IlRun r) =>
                {
                    EXPECT_EQ(r.S("s"), "foobar");
                    EXPECT_EQ(r.I("n"), 6);
                    EXPECT_EQ(r.I("sw"), 3);
                    EXPECT_EQ(r.S("sub"), "oob");
                    EXPECT_EQ(r.I("m"), 1);
                });
            }

            [TEST("OslIL", "MathFunctions")]
            public static void OslIL_MathFunctions()
            {
                Check("math", @"shader t(output float a = 0, output float b = 0, output float c = 0, output float d = 0, output float e = 0, output float f = 0,
                        output float g = 0, output vector h = 0) {
                    a = sin(0.5) * sin(0.5) + cos(0.5) * cos(0.5);   // 1
                    b = sqrt(16) + pow(2, 10) + exp(0) + log(1);      // 4 + 1024 + 1 + 0
                    c = clamp(5, 0, 3) + min(1, 2) + max(1, 2);       // 3+1+2
                    d = mix(10, 20, 0.25);                            // 12.5
                    e = smoothstep(0, 1, 0.5);                        // 0.5
                    f = atan2(1, 1) * 4;                              // pi
                    g = step(0.5, 0.7) + sign(-2) + trunc(2.7);       // 1 - 1 + 2
                    h = pow(vector(1, 2, 3), 2);                      // (1,4,9)
                }", new string[] { "a", "b", "c", "d", "e", "f", "g", "h" }, (IlRun r) =>
                {
                    EXPECT_NEAR(r.F("a"), 1.0, 1e-6);
                    EXPECT_FLOAT_EQ(r.F("b"), 1029.0f);
                    EXPECT_FLOAT_EQ(r.F("c"), 6.0f);
                    EXPECT_FLOAT_EQ(r.F("d"), 12.5f);
                    EXPECT_FLOAT_EQ(r.F("e"), 0.5f);
                    EXPECT_NEAR(r.F("f"), 3.14159265, 1e-6);
                    EXPECT_FLOAT_EQ(r.F("g"), 2.0f);
                    EXPECT_FLOAT_EQ(r.V("h").z, 9.0f);
                });
            }

            [TEST("OslIL", "ShaderGlobals")]
            public static void OslIL_ShaderGlobals()
            {
                Check("sg", @"shader t(output point p = 0, output normal n = 0, output float uu = 0, output float vv = 0, output vector i = 0,
                        output int bf = 0, output float sa = 0, output int rt = 0) {
                    p = P; n = N; uu = u; vv = v; i = I; bf = backfacing(); sa = surfacearea(); rt = raytype(""camera"");
                }", new string[] { "p", "n", "uu", "vv", "i", "bf", "sa", "rt" }, (IlRun r) =>
                {
                    Vec3 p = r.V("p");
                    EXPECT_FLOAT_EQ(p.x, 1.0f);
                    EXPECT_FLOAT_EQ(p.y, 2.0f);
                    EXPECT_FLOAT_EQ(p.z, 3.0f);
                    EXPECT_FLOAT_EQ(r.V("n").z, 1.0f);
                    EXPECT_FLOAT_EQ(r.F("uu"), 0.25f);
                    EXPECT_FLOAT_EQ(r.F("vv"), 0.75f);
                    EXPECT_FLOAT_EQ(r.V("i").z, -1.0f);
                    EXPECT_EQ(r.I("bf"), 0);
                    EXPECT_FLOAT_EQ(r.F("sa"), 1.0f);
                });
            }

            [TEST("OslIL", "Derivatives")]
            public static void OslIL_Derivatives()
            {
                Check("deriv", @"shader t(output float dx = 0, output float dy = 0, output float w = 0, output vector dp = 0, output float prod = 0) {
                    float f = u * u;        // d/dx = 2 u dudx = 2 * .25 * .01 = .005
                    dx = Dx(f);
                    float g = v * 3 + u;    // d/dy = 3 * dvdy = .03
                    dy = Dy(g);
                    w = filterwidth(u);     // sqrt(dudx^2 + dudy^2) = .01
                    dp = Dx(P * 2);         // 2 * dPdx = (.02,0,0)
                    prod = Dx(u * v);       // v dudx + u dvdx = .75*.01
                }", new string[] { "dx", "dy", "w", "dp", "prod" }, (IlRun r) =>
                {
                    EXPECT_NEAR(r.F("dx"), 0.005, 1e-7);
                    EXPECT_NEAR(r.F("dy"), 0.03, 1e-7);
                    EXPECT_NEAR(r.F("w"), 0.01, 1e-7);
                    EXPECT_NEAR(r.V("dp").x, 0.02, 1e-7);
                    EXPECT_NEAR(r.F("prod"), 0.0075, 1e-7);
                });
            }

            [TEST("OslIL", "LayerConnections")]
            public static void OslIL_LayerConnections()
            {
                using (IlRun r = new IlRun())
                {
                    bool ok = r.Multi(new string[] { "a", "b" },
                                      new string[]
                                      {
                                          "shader a(float x = 2, output float y = 0, output color c = 0) { y = x * 10; c = color(x, 2 * x, 3 * x); }",
                                          "shader b(float y = 0, color c = 0, output float z = 0, output color cc = 0) { z = y + 1; cc = c * 2; }"
                                      },
                                      new string[] { "a.y:b.y", "a.c:b.c" }, "z", "cc");
                    EXPECT_TRUE(ok);
                    EXPECT_EQ(r.Errors(), "");
                    EXPECT_FLOAT_EQ(r.F("z", 0, "b"), 21.0f);
                    EXPECT_FLOAT_EQ(r.V("cc", 0, "b").z, 12.0f);
                }
            }

            [TEST("OslIL", "Closures")]
            public static void OslIL_Closures()
            {
                using (IlRun r = new IlRun())
                {
                    r.ss.register_closure("diffuse", 1, new ClosureParam[] { ClosureParam.CLOSURE_VECTOR_PARAM(0), ClosureParam.CLOSURE_FINISH_PARAM(12, 4) }, null,
                                          null);
                    r.ss.register_closure("emission", 2, new ClosureParam[] { ClosureParam.CLOSURE_FINISH_PARAM(0, 1) }, null, null);
                    bool ok = r.Single(@"shader t(output closure color Cout = 0, output int dummy = 0) {
                        Ci = 0.5 * diffuse(N) + emission();
                        Cout = Ci;
                        dummy = 1;
                    }", "Cout", "dummy");
                    EXPECT_TRUE(ok);
                    EXPECT_EQ(r.I("dummy"), 1);
                    EXPECT_TRUE(r.sg.Ci != null);
                    if (r.sg.Ci != null)
                    {
                        string text = ClosurePrinter.print_closure(r.sg.Ci, r.ss);
                        EXPECT_TRUE(text.Contains("(0.5, 0.5, 0.5) * diffuse ((0, 0, 1))"));
                        EXPECT_TRUE(text.Contains("(1, 1, 1) * emission ()"));
                    }
                }
            }

            [TEST("OslIL", "Noise")]
            public static void OslIL_Noise()
            {
                Check("noise", @"shader t(output float n1 = 0, output float n2 = 0, output float n3 = 0, output color cn = 0, output float cell = 0,
                        output float sn = 0, output float pn = 0, output float un = 0) {
                    n1 = noise(0.5);
                    n2 = noise(P);
                    n3 = noise(P, 2.0);
                    cn = noise(P);
                    cell = cellnoise(P);
                    sn = snoise(0.3, 0.4);
                    pn = pnoise(0.3, 4.0);
                    un = noise(""uperlin"", 0.5);
                }", new string[] { "n1", "n2", "n3", "cn", "cell", "sn", "pn", "un" }, (IlRun r) =>
                {
                    // Golden values: the shadeops called directly
                    float* x = stackalloc float[1];
                    x[0] = 0.5f;
                    float g1 = OslRuntime.osl_noise_ff(0.5f);
                    EXPECT_FLOAT_EQ(r.F("n1"), g1);
                    Vec3 P = new Vec3(1.0f, 2.0f, 3.0f);
                    float g2 = OslRuntime.osl_noise_fv(&P);
                    EXPECT_FLOAT_EQ(r.F("n2"), g2);
                    EXPECT_FLOAT_EQ(r.F("n3"), OslRuntime.osl_noise_fvf(&P, 2.0f));
                    EXPECT_FLOAT_EQ(r.F("cell"), OslRuntime.osl_cellnoise_fv(&P));
                    EXPECT_FLOAT_EQ(r.F("sn"), OslRuntime.osl_snoise_fff(0.3f, 0.4f));
                    EXPECT_FLOAT_EQ(r.F("un"), g1);
                });
            }

            /// Saves the compiled group as an assembly, loads it into a second shading system and compares the results with the in-memory compile
            [TEST("OslIL", "PersistedAssembly")]
            public static void OslIL_PersistedAssembly()
            {
                string[] names = new string[] { "a", "t" };
                string[] sources = new string[]
                {
                    "shader a(output float x = 0, output string s = \"\") { x = 2.5; s = \"hello\"; }",
                    "shader t(float x = 0, string s = \"\", output float f = 0, output int n = 0, output float nz = 0, output float arr = 0) "
                    + "{ float a[3] = {1, 2, 3}; for (int i = 0; i < 3; ++i) { arr += a[i] * x; } f = x * 2.0 + arr; n = (s == \"hello\") ? 42 : -1; "
                    + "nz = noise(point(1, 2, 3)); }"
                };
                string[] conns = new string[] { "a.x:t.x", "a.s:t.s" };
                string file = Path.Combine(Path.GetTempPath(), "osl_il_persisted_" + Environment.ProcessId + ".dll");
                float f0;
                float nz0;
                using (IlRun r1 = new IlRun())
                {
                    // The ops of a group are dropped once it is compiled in memory (group_post_jit_cleanup), so the assembly is generated first
                    EXPECT_TRUE(r1.Prepare(names, sources, conns, "f", "n", "nz"));
                    r1.thread = r1.ss.create_thread_info();
                    r1.ctx = r1.ss.get_context(r1.thread);
                    EXPECT_TRUE(BackendIL.save_group_assembly(r1.group, r1.ctx, file));
                    EXPECT_EQ(r1.Errors(), "");
                    EXPECT_TRUE(r1.Execute());
                    EXPECT_EQ(r1.Errors(), "");
                    f0 = r1.F("f");
                    nz0 = r1.F("nz");
                    EXPECT_FLOAT_EQ(f0, 20.0f);
                    EXPECT_EQ(r1.I("n"), 42);
                }
                EXPECT_TRUE(File.Exists(file));
                using (IlRun r2 = new IlRun())
                {
                    EXPECT_TRUE(r2.Prepare(names, sources, conns, "f", "n", "nz"));
                    r2.thread = r2.ss.create_thread_info();
                    r2.ctx = r2.ss.get_context(r2.thread);
                    EXPECT_TRUE(BackendIL.load_group_assembly(r2.group, r2.ctx, file));
                    EXPECT_TRUE(r2.group.jitted() != 0);
                    EXPECT_TRUE(r2.ss.execute(r2.ctx, r2.group, 0, 0, ref r2.sg, null, null));
                    EXPECT_EQ(r2.Errors(), "");
                    EXPECT_FLOAT_EQ(r2.F("f"), f0);
                    EXPECT_EQ(r2.I("n"), 42);
                    EXPECT_FLOAT_EQ(r2.F("nz"), nz0);
                }
                File.Delete(file);
            }

            [TEST("OslIL", "Smoke")]
            public static void OslIL_Smoke()
            {
                using (IlRun r = new IlRun())
                {
                    bool ok = r.Single("shader t(output float f = 0, output int i = 0) { f = 1.5 + 2.0 * 3.0; i = 7 / 2; }", "f", "i");
                    EXPECT_TRUE(ok);
                    EXPECT_EQ(r.Errors(), "");
                    EXPECT_FLOAT_EQ(r.F("f"), 7.5f);
                    EXPECT_EQ(r.I("i"), 3);
                }
            }
        }
    }
}
