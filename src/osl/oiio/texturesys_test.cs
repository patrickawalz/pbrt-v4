// (not in OpenShadingLanguage) texturesys_test.cs
//
// The default TextureSystem against OpenImageIO 3.1.9's own TextureSystem: the lookups in TextureSystemGolden.Lines were produced by a C++ program that
// calls TextureSystem::texture() and environment() on the OSL testsuite textures (grid.tx, mandrill.tif, kitchen_probe.hdr) with options drawn from a
// deterministic generator, and printed every result as raw float bits. The same generator runs here and the results must agree bit for bit.
//
// The images are read from the OSL source tree (testsuite/common/textures); the tests that need them are skipped if it is not there (set OSL_TEXTURE_DIR to
// point at another copy).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OSL;
using testing;
using static testing.Gtest;

namespace OIIO
{
    public static unsafe partial class TextureSystemTests
    {
        private static string texture_dir()
        {
            string d = Environment.GetEnvironmentVariable("OSL_TEXTURE_DIR");
            if (string.IsNullOrEmpty(d))
            {
                d = "/home/claude/OpenShadingLanguage/testsuite/common/textures";
            }
            return d;
        }

        private static bool have_textures()
        {
            return File.Exists(Path.Combine(texture_dir(), "grid.tx")) && File.Exists(Path.Combine(texture_dir(), "mandrill.tif")) &&
                   File.Exists(Path.Combine(texture_dir(), "kitchen_probe.hdr"));
        }

        private static uint g_state;

        private static float rnd()
        {
            g_state = g_state * 1664525u + 1013904223u;
            return (float)(g_state >> 8) * (1.0f / 16777216.0f);
        }

        private static float derivmag()
        {
            int k = (int)(rnd() * 12.0f);
            float bas = MathF.ScaleB(1.0f, -k);
            return bas * (0.5f + rnd());
        }

        private static float sgn()
        {
            return rnd() < 0.5f ? -1.0f : 1.0f;
        }

        private static float sdm()
        {
            float a = sgn();
            float b = derivmag();
            return a * b;
        }

        private static float pm1()
        {
            return 2.0f * rnd() - 1.0f;
        }

        private static string hex(float f)
        {
            return BitConverter.SingleToUInt32Bits(f).ToString("x8");
        }

        private static string[] golden_lines()
        {
            string[] g = TextureSystemGolden.Lines;
            if (g != null && g.Length > 0)
            {
                return g;
            }
            string f = Environment.GetEnvironmentVariable("OSL_TEX_GOLDEN");
            if (!string.IsNullOrEmpty(f) && File.Exists(f))
            {
                return File.ReadAllLines(f);
            }
            return null;
        }

        private static string results_only(string line, int nch)
        {
            string[] parts = line.Split(' ');
            return string.Join(" ", parts, 0, 5 + nch);
        }

        private static string info_line(ImageTextureSystem ts, TexturePerthread pt, string path)
        {
            TextureHandle h = ts.get_texture_handle(new ustring(path), pt);
            StringBuilder sb = new StringBuilder();
            int[] exists = new int[1];
            ts.get_texture_info(h, pt, 0, new ustring("exists"), TypeDescs.TypeInt, exists);
            sb.Append("exists=").Append(exists[0]);
            int[] res = new int[2];
            if (ts.get_texture_info(h, pt, 0, new ustring("resolution"), new TypeDesc(TypeDesc.INT, 2), res))
            {
                sb.Append(" res=").Append(res[0]).Append(',').Append(res[1]);
            }
            int[] ch = new int[1];
            if (ts.get_texture_info(h, pt, 0, new ustring("channels"), TypeDescs.TypeInt, ch))
            {
                sb.Append(" ch=").Append(ch[0]);
            }
            ustring[] tt = new ustring[1];
            if (ts.get_texture_info(h, pt, 0, new ustring("texturetype"), TypeDescs.TypeString, tt))
            {
                sb.Append(" tt=").Append(tt[0].@string());
            }
            if (ts.get_texture_info(h, pt, 0, new ustring("textureformat"), TypeDescs.TypeString, tt))
            {
                sb.Append(" tf=").Append(tt[0].@string());
            }
            float[] avg = new float[4];
            if (ts.get_texture_info(h, pt, 0, new ustring("averagecolor"), new TypeDesc(TypeDesc.FLOAT, 4), avg))
            {
                sb.Append(" avg=").Append(hex(avg[0])).Append(',').Append(hex(avg[1])).Append(',').Append(hex(avg[2])).Append(',').Append(hex(avg[3]));
            }
            int[] mp = new int[1];
            if (ts.get_texture_info(h, pt, 0, new ustring("miplevels"), TypeDescs.TypeInt, mp))
            {
                sb.Append(" mips=").Append(mp[0]);
            }
            int[] fm = new int[] { -1 };
            if (ts.get_texture_info(h, pt, 0, new ustring("format"), TypeDescs.TypeInt, fm))
            {
                sb.Append(" fmt=").Append(fm[0]);
            }
            sb.Append(" err=[").Append(ts.geterror()).Append(']');
            return sb.ToString();
        }

        [TEST("TextureSystem", "TextureInfoMatchesOpenImageIO")]
        public static void TextureSystem_TextureInfoMatchesOpenImageIO()
        {
            if (!have_textures())
            {
                return;
            }
            ImageTextureSystem ts = new ImageTextureSystem();
            ts.attribute("automip", 1);
            ts.attribute("autotile", 64);
            TexturePerthread pt = ts.get_perthread_info();
            string dir = texture_dir();
            EXPECT_EQ(info_line(ts, pt, Path.Combine(dir, "grid.tx")),
                      "exists=1 res=1024,1024 ch=4 tt=Plain Texture tf=Plain Texture avg=3f1be64f,3f1bc255,3f1bd599,3f800000 mips=11 fmt=2 err=[]");
            EXPECT_EQ(info_line(ts, pt, Path.Combine(dir, "mandrill.tif")),
                      "exists=1 res=512,512 ch=3 tt=Plain Texture tf=Plain Texture avg=3ea0a0a1,3e868687,3e70f0f2,00000000 mips=10 fmt=2 err=[]");
            EXPECT_EQ(info_line(ts, pt, Path.Combine(dir, "kitchen_probe.hdr")),
                      "exists=1 res=640,640 ch=3 tt=Plain Texture tf=Plain Texture avg=3f856231,3f3d2b1f,3f08b523,00000000 mips=10 fmt=11 err=[]");
            // a missing file: every failing query adds one "Invalid image file" line, exists is not an error, and a lookup that fails later has no new message
            string missing = Path.Combine(dir, "nonexistent.tif");
            string line = "Invalid image file \"" + missing + "\": Could not open file: " + missing + ": No such file or directory";
            StringBuilder expected = new StringBuilder("exists=0 err=[");
            for (int i = 0; i < 7; ++i)
            {
                expected.Append(i > 0 ? "\n" : "").Append(line);
            }
            expected.Append(']');
            EXPECT_EQ(info_line(ts, pt, missing), expected.ToString());
            TextureHandle hm = ts.get_texture_handle(new ustring(missing), pt);
            TextureOpt opt = TextureOpt.make_default();
            float* r = stackalloc float[4];
            EXPECT_FALSE(ts.texture(hm, pt, ref opt, 0.5f, 0.5f, 0.01f, 0, 0, 0.01f, 3, r, null, null));
            EXPECT_EQ(ts.geterror(), "");
            // with a missing color the failure is not an error
            float* mc = stackalloc float[4];
            mc[0] = 1.0f;
            mc[1] = 0.5f;
            mc[2] = 0.25f;
            opt.missingcolor = new float[] { 1.0f, 0.5f, 0.25f, 0.0f };
            EXPECT_TRUE(ts.texture(hm, pt, ref opt, 0.5f, 0.5f, 0.01f, 0, 0, 0.01f, 3, r, null, null));
            EXPECT_EQ(r[0], 1.0f);
            EXPECT_EQ(r[1], 0.5f);
            EXPECT_EQ(r[2], 0.25f);
        }

        private static readonly int[] mips = new int[] { 0, 1, 2, 3, 4 };

        [TEST("TextureSystem", "MatchesOpenImageIO")]
        public static void TextureSystem_MatchesOpenImageIO()
        {
            if (!have_textures())
            {
                return;
            }
            string[] golden = golden_lines();
            ASSERT_TRUE(golden != null);
            ImageTextureSystem ts = new ImageTextureSystem();
            ts.attribute("automip", 1);
            ts.attribute("autotile", 64);
            TexturePerthread pt = ts.get_perthread_info();
            string[] files = new string[] { "grid.tx", "mandrill.tif", "kitchen_probe.hdr" };
            g_state = 12345u;
            int line = 0;
            int mismatches = 0;
            int total = 0;
            for (int f = 0; f < 3; ++f)
            {
                TextureHandle h = ts.get_texture_handle(new ustring(Path.Combine(texture_dir(), files[f])), pt);
                for (int ci = 0; ci < 400; ++ci)
                {
                    TextureOpt opt = TextureOpt.make_default();
                    opt.interpmode = (int)(rnd() * 4.0f);
                    opt.mipmode = mips[(int)(rnd() * 5.0f)];
                    opt.swrap = (int)(rnd() * 5.0f);
                    opt.twrap = (int)(rnd() * 5.0f);
                    opt.sblur = rnd() < 0.25f ? 0.01f * rnd() : 0.0f;
                    opt.tblur = rnd() < 0.25f ? 0.01f * rnd() : 0.0f;
                    opt.swidth = rnd() < 0.3f ? 1.0f + rnd() : 1.0f;
                    opt.twidth = rnd() < 0.3f ? 1.0f + rnd() : 1.0f;
                    opt.fill = rnd() < 0.3f ? 0.5f : 0.0f;
                    opt.firstchannel = (int)(rnd() * 2.0f);
                    opt.anisotropic = rnd() < 0.3f ? 8 : 32;
                    opt.conservative_filter = rnd() < 0.8f ? 1 : 0;
                    float s = -0.25f + 1.5f * rnd();
                    float t = -0.25f + 1.5f * rnd();
                    float dsdx = sdm();
                    float dtdx = sdm();
                    float dsdy = sdm();
                    float dtdy = sdm();
                    if (rnd() < 0.3f)
                    {
                        dtdx = 0.1f * dsdx * rnd();
                        dsdy = 0.1f * dtdy * rnd();
                    }
                    int nch = 1 + (int)(rnd() * 4.0f);
                    bool derivs = rnd() < 0.5f;
                    float* res = stackalloc float[4];
                    float* drds = stackalloc float[4];
                    float* drdt = stackalloc float[4];
                    for (int i = 0; i < 4; ++i)
                    {
                        res[i] = 0;
                        drds[i] = 0;
                        drdt[i] = 0;
                    }
                    bool ok = ts.texture(h, pt, ref opt, s, t, dsdx, dtdx, dsdy, dtdy, nch, res, derivs ? drds : null, derivs ? drdt : null);
                    StringBuilder sb = new StringBuilder();
                    sb.Append("T ").Append(f).Append(' ').Append(ci).Append(' ').Append(ok ? 1 : 0).Append(' ').Append(nch);
                    for (int c = 0; c < nch; ++c)
                    {
                        sb.Append(' ').Append(hex(res[c]));
                    }
                    if (derivs)
                    {
                        for (int c = 0; c < nch; ++c)
                        {
                            sb.Append(' ').Append(hex(drds[c]));
                        }
                        for (int c = 0; c < nch; ++c)
                        {
                            sb.Append(' ').Append(hex(drdt[c]));
                        }
                    }
                    ++total;
                    string mine = sb.ToString();
                    string theirs = golden[line];
                    if (derivs && opt.interpmode == TextureOpt.InterpClosest && (opt.mipmode == TextureOpt.MipModeDefault || opt.mipmode == TextureOpt.MipModeAniso))
                    {
                        // OIIO adds the (uninitialized) derivative outputs of a closest probe in this path; compare only the values
                        mine = results_only(mine, nch);
                        theirs = results_only(theirs, nch);
                    }
                    if (mine != theirs)
                    {
                        if (++mismatches <= 12)
                        {
                            ADD_FAILURE("case " + sb.ToString() + "  expected  " + golden[line] + "   interp=" + opt.interpmode + " mip=" + opt.mipmode + " wrap=" +
                                        opt.swrap + "," + opt.twrap + " st=" + s + "," + t + " d=" + dsdx + "," + dtdx + "," + dsdy + "," + dtdy);
                        }
                    }
                    ++line;
                }
            }
            TextureHandle he = ts.get_texture_handle(new ustring(Path.Combine(texture_dir(), "kitchen_probe.hdr")), pt);
            for (int ci = 0; ci < 300; ++ci)
            {
                TextureOpt opt = TextureOpt.make_default();
                opt.interpmode = (int)(rnd() * 4.0f);
                opt.mipmode = mips[(int)(rnd() * 5.0f)];
                opt.sblur = rnd() < 0.25f ? 0.01f * rnd() : 0.0f;
                opt.tblur = rnd() < 0.25f ? 0.01f * rnd() : 0.0f;
                opt.swidth = rnd() < 0.3f ? 1.0f + rnd() : 1.0f;
                opt.twidth = rnd() < 0.3f ? 1.0f + rnd() : 1.0f;
                opt.fill = rnd() < 0.3f ? 0.5f : 0.0f;
                opt.anisotropic = rnd() < 0.3f ? 8 : 32;
                float rx = pm1();
                float ry = pm1();
                float rz = pm1();
                Vec3 R = new Vec3(rx, ry, rz);
                float ax = sdm();
                float ay = sdm();
                float az = sdm();
                Vec3 dRdx = new Vec3(ax, ay, az);
                float bx = sdm();
                float by = sdm();
                float bz = sdm();
                Vec3 dRdy = new Vec3(bx, by, bz);
                int nch = 1 + (int)(rnd() * 4.0f);
                bool derivs = rnd() < 0.5f;
                float* res = stackalloc float[4];
                float* drds = stackalloc float[4];
                float* drdt = stackalloc float[4];
                for (int i = 0; i < 4; ++i)
                {
                    res[i] = 0;
                    drds[i] = 0;
                    drdt[i] = 0;
                }
                bool ok = ts.environment(he, pt, ref opt, R, dRdx, dRdy, nch, res, derivs ? drds : null, derivs ? drdt : null);
                StringBuilder sb = new StringBuilder();
                sb.Append("E 2 ").Append(ci).Append(' ').Append(ok ? 1 : 0).Append(' ').Append(nch);
                for (int c = 0; c < nch; ++c)
                {
                    sb.Append(' ').Append(hex(res[c]));
                }
                if (derivs)
                {
                    for (int c = 0; c < nch; ++c)
                    {
                        sb.Append(' ').Append(hex(drds[c]));
                    }
                    for (int c = 0; c < nch; ++c)
                    {
                        sb.Append(' ').Append(hex(drdt[c]));
                    }
                }
                ++total;
                if (sb.ToString() != golden[line])
                {
                    if (++mismatches <= 12)
                    {
                        ADD_FAILURE("env " + sb.ToString() + "  expected  " + golden[line] + "   interp=" + opt.interpmode + " mip=" + opt.mipmode);
                    }
                }
                ++line;
            }
            Console.WriteLine("texture lookups compared: " + total + ", mismatches: " + mismatches);
            EXPECT_EQ(mismatches, 0);
        }
    }

    public static partial class TextureSystemGolden
    {
        public static string[] Lines;
    }
}
