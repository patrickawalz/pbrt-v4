// (not in OpenShadingLanguage) opctx_test.cs
//
// Context dependent shadeops, run against a real ShadingSystemImpl / ShadingContext and a mock RendererServices. Expected values are what the C++
// extern "C" osl_* functions return for the same inputs (regex expectations follow std::regex ECMAScript semantics, testsuite/regex-reg).

using System;
using OIIO;
using testing;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static unsafe class OslRuntimeCtxTests
        {
            private sealed class MockRenderer : RendererServices
            {
                public int transform_calls;

                public override bool transform_points(ShaderGlobals* sg, ulong from, ulong to, float time, Vec3* Pin, Vec3* Pout, int npoints,
                                                      VECSEMANTICS vectype)
                {
                    ++transform_calls;
                    if (from != new ustring("shader").hash() || to != new ustring("warped").hash())
                    {
                        return false;
                    }
                    for (int i = 0; i < npoints; ++i)
                    {
                        Pout[i] = new Vec3(Pin[i].x * 2.0f + time, Pin[i].y, Pin[i].z);
                    }
                    return true;
                }
            }

            private sealed class Fixture : IDisposable
            {
                public BufferedErrorHandler errors = new BufferedErrorHandler();
                public MockRenderer renderer = new MockRenderer();
                public ShadingSystemImpl ss;
                public ShadingContext ctx;
                public ShaderGlobals* sg;

                public Fixture()
                {
                    ss = new ShadingSystemImpl(renderer, null, errors);
                    ctx = ss.get_context(ss.create_thread_info());
                    sg = (ShaderGlobals*)System.Runtime.InteropServices.NativeMemory.AllocZeroed((nuint)sizeof(ShaderGlobals));
                    sg->context = ShaderGlobalsAccess.make_handle(ctx);
                    sg->renderer = ShaderGlobalsAccess.make_handle(renderer);
                }

                public void Dispose()
                {
                    ShaderGlobalsAccess.free_handle(sg->context);
                    ShaderGlobalsAccess.free_handle(sg->renderer);
                    System.Runtime.InteropServices.NativeMemory.Free(sg);
                }
            }

            private static ulong H(string s)
            {
                return new ustring(s).hash();
            }

            private static string S(ulong h)
            {
                ustring u;
                ustring.from_hash(h, out u);
                return u.@string();
            }

            private static int Regex(Fixture f, string subject, string pattern, bool full, int[] results)
            {
                int[] buf = results == null ? new int[1] : results;
                fixed (int* p = buf)
                {
                    return ILGlue.osl_regex_impl(f.sg, H(subject), results == null ? null : p, results == null ? 0 : results.Length, H(pattern), full ? 1 : 0);
                }
            }

            [TEST("OslRuntimeCtx", "RegexMatchSearch")]
            public static void OslRuntimeCtx_RegexMatchSearch()
            {
                using (Fixture f = new Fixture())
                {
                    string subj = "electric blue";
                    EXPECT_EQ(Regex(f, subj, "ham", true, null), 0);
                    EXPECT_EQ(Regex(f, subj, "ue", true, null), 0);
                    EXPECT_EQ(Regex(f, subj, "ue", false, null), 1);
                    EXPECT_EQ(Regex(f, subj, ".*tric", true, null), 0);
                    EXPECT_EQ(Regex(f, subj, ".*tric", false, null), 1);
                    EXPECT_EQ(Regex(f, subj, "el[a-b]", false, null), 0);
                    EXPECT_EQ(Regex(f, subj, "electric.*", true, null), 1);
                    EXPECT_EQ(Regex(f, subj, "$el", false, null), 0);

                    int[] r = new int[6];
                    EXPECT_EQ(Regex(f, subj, "(b)(l)ue", false, r), 1);
                    EXPECT_EQ(r[0], 9);
                    EXPECT_EQ(r[1], 13);
                    EXPECT_EQ(r[2], 9);
                    EXPECT_EQ(r[3], 10);
                    EXPECT_EQ(r[4], 10);
                    EXPECT_EQ(r[5], 11);

                    // More result slots than groups report the pattern length, as in the C++
                    int[] r2 = new int[6];
                    EXPECT_EQ(Regex(f, subj, "blue", false, r2), 1);
                    EXPECT_EQ(r2[0], 9);
                    EXPECT_EQ(r2[1], 13);
                    EXPECT_EQ(r2[2], 4);
                    EXPECT_EQ(r2[5], 4);

                    // A failed match reports the pattern length everywhere
                    int[] r3 = new int[2];
                    EXPECT_EQ(Regex(f, subj, "zzz", false, r3), 0);
                    EXPECT_EQ(r3[0], 3);
                    EXPECT_EQ(r3[1], 3);
                }
            }

            [TEST("OslRuntimeCtx", "RegexEcmaScriptDialect")]
            public static void OslRuntimeCtx_RegexEcmaScriptDialect()
            {
                using (Fixture f = new Fixture())
                {
                    // \d, \w, \s are ASCII only
                    EXPECT_EQ(Regex(f, "٣", "\\d", false, null), 0);
                    EXPECT_EQ(Regex(f, "7", "\\d", false, null), 1);
                    EXPECT_EQ(Regex(f, "é", "\\w", false, null), 0);
                    EXPECT_EQ(Regex(f, "a_9", "\\w+", true, null), 1);
                    EXPECT_EQ(Regex(f, " ", "\\s", false, null), 0);
                    EXPECT_EQ(Regex(f, "a b", "a\\sb", true, null), 1);
                    EXPECT_EQ(Regex(f, "a-", "\\D\\W?", true, null), 1);
                    EXPECT_EQ(Regex(f, "x1", "[\\d]", false, null), 1);
                    EXPECT_EQ(Regex(f, "xy", "[\\d]", false, null), 0);
                    // '.' does not match \r or \n
                    EXPECT_EQ(Regex(f, "a\rb", "a.b", true, null), 0);
                    EXPECT_EQ(Regex(f, "a\nb", "a.b", true, null), 0);
                    EXPECT_EQ(Regex(f, "axb", "a.b", true, null), 1);
                    // '$' matches only at the very end
                    EXPECT_EQ(Regex(f, "abc\n", "abc$", false, null), 0);
                    EXPECT_EQ(Regex(f, "abc", "abc$", false, null), 1);
                    EXPECT_EQ(Regex(f, "abc", "^abc$", true, null), 1);
                    // POSIX character classes
                    EXPECT_EQ(Regex(f, "abc", "[[:alpha:]]+", true, null), 1);
                    EXPECT_EQ(Regex(f, "ab1", "[[:alpha:]]+", true, null), 0);
                    EXPECT_EQ(Regex(f, "a1 ", "[[:alnum:][:space:]]+", true, null), 1);
                    EXPECT_EQ(Regex(f, "ff09", "[[:xdigit:]]+", true, null), 1);
                    // Escaped metacharacters and a literal '$'
                    EXPECT_EQ(Regex(f, "a.b", "a\\.b", true, null), 1);
                    EXPECT_EQ(Regex(f, "axb", "a\\.b", true, null), 0);
                    EXPECT_EQ(Regex(f, "a$", "a\\$", true, null), 1);
                    EXPECT_EQ(Regex(f, "a]", "[]]", false, null), 1);
                    // Backtracking alternatives still make a full match
                    EXPECT_EQ(Regex(f, "abc", "a|abc", true, null), 1);
                    EXPECT_EQ(Regex(f, "abc", "a|abc", false, null), 1);
                }
            }

            [TEST("OslRuntimeCtx", "EcmaRegexTranslate")]
            public static void OslRuntimeCtx_EcmaRegexTranslate()
            {
                EXPECT_TRUE(EcmaRegex.translate("a.b$") == "a[^\\n\\r]b\\z");
                EXPECT_TRUE(EcmaRegex.translate("\\d+") == "[0-9]+");
                EXPECT_TRUE(EcmaRegex.translate("[\\d_]") == "[0-9_]");
                EXPECT_TRUE(EcmaRegex.translate("[[:digit:]]") == "[0-9]");
                EXPECT_TRUE(EcmaRegex.translate("\\A(?:x)\\z") == "\\A(?:x)\\z");
                EXPECT_TRUE(EcmaRegex.translate("[.$]") == "[.$]");
            }

            [TEST("OslRuntimeCtx", "RangeCheck")]
            public static void OslRuntimeCtx_RangeCheck()
            {
                using (Fixture f = new Fixture())
                {
                    EXPECT_EQ(ILGlue.osl_range_check(3, 10, H("arr"), f.sg, H("a.osl"), 7, H("grp"), 0, H("lay"), H("sh")), 3);
                    EXPECT_EQ(f.errors.errors.Count, 0);
                    EXPECT_EQ(ILGlue.osl_range_check(12, 10, H("arr"), f.sg, H("a.osl"), 7, H("grp"), 0, H("lay"), H("sh")), 9);
                    EXPECT_EQ(ILGlue.osl_range_check(-4, 10, H("arr"), f.sg, H("a.osl"), 7, H("grp"), 0, H("lay"), H("sh")), 0);
                    f.ctx.process_errors();
                    EXPECT_EQ(f.errors.errors.Count, 2);
                    EXPECT_TRUE(f.errors.errors[0].Contains("Index [12] out of range arr[0..9]: a.osl:7 (group grp, layer 0 lay, shader sh)"));
                    EXPECT_TRUE(f.errors.errors[1].Contains("Index [-4] out of range arr[0..9]"));
                }
            }

            [TEST("OslRuntimeCtx", "Raytype")]
            public static void OslRuntimeCtx_Raytype()
            {
                using (Fixture f = new Fixture())
                {
                    f.sg->raytype = 0x5;
                    EXPECT_EQ(ILGlue.osl_raytype_bit(f.sg, 1), 1);
                    EXPECT_EQ(ILGlue.osl_raytype_bit(f.sg, 2), 0);
                    EXPECT_EQ(ILGlue.osl_raytype_bit(f.sg, 4), 1);
                    EXPECT_EQ(ILGlue.osl_raytype_bit(f.sg, 6), 1);
                    f.sg->raytype = 0;
                    EXPECT_EQ(ILGlue.osl_raytype_bit(f.sg, 7), 0);
                }
            }

            [TEST("OslRuntimeCtx", "TransformTripleNonlinear")]
            public static void OslRuntimeCtx_TransformTripleNonlinear()
            {
                using (Fixture f = new Fixture())
                {
                    f.sg->time = 0.5f;
                    Vec3* pin = stackalloc Vec3[3];
                    Vec3* pout = stackalloc Vec3[3];
                    pin[0] = new Vec3(1.0f, 2.0f, 3.0f);
                    pin[1] = new Vec3(0.25f, 0.0f, 0.0f);
                    pin[2] = new Vec3(0.0f, 4.0f, 0.0f);
                    // The renderer handles shader -> warped directly, with and without input derivatives
                    int ok = OslCtxOps.osl_transform_triple_nonlinear(f.sg, pin, 1, pout, 1, H("shader"), H("warped"), (int)VECSEMANTICS.POINT);
                    EXPECT_EQ(ok, 1);
                    EXPECT_FLOAT_EQ(pout[0].x, 2.5f);
                    EXPECT_FLOAT_EQ(pout[0].y, 2.0f);
                    EXPECT_FLOAT_EQ(pout[1].x, 1.0f);
                    EXPECT_FLOAT_EQ(pout[2].y, 4.0f);
                    EXPECT_EQ(f.renderer.transform_calls, 2);
                    ok = OslCtxOps.osl_transform_triple_nonlinear(f.sg, pin, 0, pout, 1, H("shader"), H("warped"), (int)VECSEMANTICS.POINT);
                    EXPECT_EQ(ok, 1);
                    EXPECT_FLOAT_EQ(pout[1].x, 0.0f);
                    EXPECT_FLOAT_EQ(pout[2].z, 0.0f);
                }
            }

            [TEST("OslRuntimeCtx", "StringStubs")]
            public static void OslRuntimeCtx_StringStubs()
            {
                EXPECT_EQ(OslCtxOps.osl_gen_ustring(H("hello")), H("hello"));
                EXPECT_TRUE(S(OslCtxOps.osl_format(H("100%% sure"))) == "100% sure");
                EXPECT_TRUE(S(OslCtxOps.osl_format(H("plain"))) == "plain");
                OslCtxOps.osl_printf(null, null, H("ignored"));
                OslCtxOps.osl_error(null, null, H("ignored"));
                OslCtxOps.osl_warning(null, null, H("ignored"));
                OslCtxOps.osl_fprintf(null, null, H("f"), H("ignored"));
            }
        }
    }
}
