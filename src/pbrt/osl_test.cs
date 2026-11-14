// (not in pbrt-v4) Tests of the integration of Open Shading Language (osl_*.cs): the scene file parameters of "osl" materials and textures, the
// ShaderGlobals that a shader sees, the BxDFs of the closures (chi^2 test of the sampling, white furnace, reciprocity), and renders of small scenes
// with the command line of pbrt, compared with pbrt's own materials. The renders are written as PNG with labels to /tmp/osl_renders.
//
//   dotnet pbrt_test.dll --gtest_filter='OslPbrt*'

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using OIIO;
using OSL;
using testing;

namespace pbrt
{
    // ---------------------------------------------------------------------------------------------------------------------------------
    // Shaders of the tests (written to files, so that the scenes find them as shaders next to the scene file)
    // ---------------------------------------------------------------------------------------------------------------------------------
    public static class OslTestShaders
    {
        public static readonly Dictionary<string, string> sources = new Dictionary<string, string>
        {
            { "matte", "surface matte(float Kd = 1, color Cs = 1) { Ci = Kd * Cs * diffuse(N); }" },
            { "metal", "surface metal(float Ks = 1, float eta = 10, color Cs = 1) { Ci = Ks * Cs * reflection(N, eta); }" },
            { "glow", "surface glow(color Ce = color(1, 0.6, 0.2), float power = 5) { Ci = power * Ce * emission(); }" },
            { "mx_conductor", "surface mx_conductor(color ior = color(0.2, 0.92, 1.1), color k = color(3.9, 2.45, 2.14), float roughness = 0.3, " +
                              "string distribution = \"ggx\") { Ci = conductor_bsdf(N, vector(0, 0, 0), roughness, roughness, ior, k, distribution); }" },
            { "mx_glass", "surface mx_glass(float ior = 1.5, float roughness = 0.2, string distribution = \"ggx\") " +
                          "{ Ci = dielectric_bsdf(N, vector(0, 0, 0), color(1), color(1), roughness, roughness, ior, distribution); }" },
            { "glossy", "surface glossy(color Cs = 1, float alpha = 0.3, float eta = 1.5, string dist = \"ggx\") { Ci = Cs * microfacet(dist, N, alpha, eta, 0); }" },
            { "layered", "surface layered(color Cs = color(0.8, 0.2, 0.2), float roughness = 0.15) { Ci = layer(dielectric_bsdf(N, vector(0, 0, 0), " +
                         "color(1), color(0), roughness, roughness, 1.5, \"ggx\"), oren_nayar_diffuse_bsdf(N, Cs, 0.0)); }" },
            { "mixed_glass", "surface mixed_glass(color Cs = color(0.8, 0.2, 0.2), float mixw = 0.5) { float e = backfacing() ? 1.0 / 1.5 : 1.5; " +
                             "Ci = (1 - mixw) * Cs * diffuse(N) + mixw * (microfacet(\"ggx\", N, 0.2, e, 0) + microfacet(\"ggx\", N, 0.2, e, 1)); }" },
            { "mixed", "surface mixed(color Cs = color(0.8, 0.2, 0.2), float mixw = 0.5) { Ci = (1 - mixw) * Cs * diffuse(N) + " +
                       "mixw * microfacet(\"ggx\", N, 0.2, 1.5, 0); }" },
            { "checkerboard",
              "shader checkerboard(float s = u, float t = v, float scale_s = 4, float scale_t = 4, color Ca = color(1, 1, 1), color Cb = color(0, 0, 0), " +
              "output color Cout = 0) { float cs = fmod(s * scale_s, 2); float ct = fmod(t * scale_t, 2); " +
              "if ((int(cs) ^ int(ct)) == 0) Cout = Ca; else Cout = Cb; }" },
            { "marble",
              "shader marble(float scale = 2.5, float turbulence = 5.0, color base = color(0.92, 0.9, 0.86), color vein = color(0.15, 0.2, 0.35), " +
              "output color Cout = 0) { point Po = transform(\"object\", P) * scale; float t = 0; float f = 1; " +
              "for (int i = 0; i < 5; ++i) { t += abs(snoise(Po * f)) / f; f *= 2; } " +
              "float m = 0.5 + 0.5 * sin(Po[0] * 3.0 + turbulence * t); m = smoothstep(0.35, 0.65, m); Cout = mix(vein, base, m); }" },
            { "fnoise", "shader fnoise(float scale = 6, float lo = 0.1, float hi = 0.6, output float Fout = 0) " +
                        "{ point Po = transform(\"object\", P) * scale; Fout = lo + (hi - lo) * (0.5 + 0.5 * noise(Po)); }" },
            { "constcolor", "shader constcolor(color value = color(0.5, 0.5, 0.5), output color Cout = 0) { Cout = value; }" },
            { "dwave", "shader dwave(float freq = 3, output float Fout = 0) " +
                       "{ Fout = 0.5 + 0.3 * sin(freq * u) * cos(freq * v) + 0.1 * sin(2 * P[0] + P[1] * P[2]); }" },
            { "dcolor", "shader dcolor(float freq = 2, output color Cout = 0) { Cout = color(0.7 + 0.2 * sin(freq * u + P[0]), " +
                        "0.3 + 0.15 * cos(3 * v), 0.25 + 0.15 * sin(u * v + P[2])); }" },
            { "dwarp", "shader dwarp(float amount = 0.4, output point Out = 0) { Out = P + amount * (vector)snoise(P * 1.7); }" },
            { "dstripes", "shader dstripes(point Pin = P, float Fin = 0, output float Fout = 0) { Fout = 0.5 + 0.4 * sin(5 * Pin[0] + 2 * Pin[1]) * " +
                          "cos(3 * Pin[2]) + 0.5 * Fin; }" },
            { "dint", "shader dint(output int Fout = 0) { Fout = int(4 * u); }" },
            { "tint", "shader tint(color Cin = color(1, 1, 1), color gain = color(1, 1, 1), output color Cout = 0) { Cout = Cin * gain; }" },
            { "probe",
              "shader probe(int mode = 0, output color Cout = 0, output float Fout = 0) { " +
              "if (mode == 0) Cout = P; else if (mode == 1) Cout = N; else if (mode == 2) Cout = Ng; else if (mode == 3) Cout = I; " +
              "else if (mode == 4) Cout = color(u, v, 0); else if (mode == 5) Cout = dPdu; else if (mode == 6) Cout = dPdv; " +
              "else if (mode == 7) Cout = transform(\"object\", P); else if (mode == 8) Cout = transform(\"shader\", P); " +
              "else if (mode == 9) Cout = transform(\"camera\", P); else if (mode == 10) Cout = transform(\"mycs\", P); " +
              "else if (mode == 11) Cout = backfacing() ? 1 : 0; else if (mode == 12) Cout = time; else if (mode == 13) Cout = color(Dx(u), Dy(u), Dx(v)); " +
              "else if (mode == 14) Cout = Dx(P); else if (mode == 15) Cout = Dy(P); else if (mode == 16) Cout = raytype(\"camera\") ? 1 : 0; " +
              "else if (mode == 17) Cout = raytype(\"diffuse\") ? 1 : 0; else if (mode == 18) Cout = raytype(\"glossy\") ? 1 : 0; " +
              "else if (mode == 19) { int res[2]; getattribute(\"camera:resolution\", res); Cout = color(res[0], res[1], 0); } " +
              "else if (mode == 20) { float fov; getattribute(\"camera:fov\", fov); Cout = fov; } " +
              "else if (mode == 21) { string proj; getattribute(\"camera:projection\", proj); Cout = (proj == \"perspective\") ? 1 : 0; } " +
              "else if (mode == 22) { float sh[2]; getattribute(\"camera:shutter\", sh); Cout = color(sh[0], sh[1], 0); } " +
              "else if (mode == 23) { string nm; getattribute(\"object:name\", nm); Cout = (nm == \"ball\") ? 1 : 0; } " +
              "else if (mode == 24) { float a; getattribute(\"object:surfacearea\", a); Cout = a; } " +
              "else if (mode == 25) { float sh[2]; getattribute(\"shutter\", sh); Cout = color(sh[0], sh[1], 0); } " +
              "else if (mode == 26) { int ok = trace(P + N * 0.001, -N); float d = 0; getmessage(\"trace\", \"hitdist\", d); Cout = color(ok, d, 0); } " +
              "else if (mode == 27) Cout = transform(\"screen\", P); else if (mode == 28) Cout = transform(\"NDC\", P); " +
              "else if (mode == 29) Cout = surfacearea(); else if (mode == 30) { float fa = 0; getattribute(\"camera:pixelaspect\", fa); Cout = fa; } " +
              "Fout = Cout[0]; }" }
        };

        private static readonly object mutex = new object();

        public static string Directory()
        {
            string dir = "/tmp/osl_renders/scenes";
            lock (mutex)
            {
                System.IO.Directory.CreateDirectory(dir);
                foreach (KeyValuePair<string, string> kv in sources)
                {
                    string path = Path.Combine(dir, kv.Key + ".osl");
                    if (!File.Exists(path) || File.ReadAllText(path) != kv.Value)
                    {
                        File.WriteAllText(path, kv.Value);
                    }
                }
            }
            return dir;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // Rendering with the command line of pbrt, statistics, labelled PNGs
    // ---------------------------------------------------------------------------------------------------------------------------------
    public sealed class OslRender
    {
        public int width, height;
        public float[] rgb;

        public double[] ChannelMeans()
        {
            double[] m = new double[3];
            for (int i = 0; i < width * height; ++i)
            {
                for (int c = 0; c < 3; ++c)
                {
                    m[c] += rgb[3 * i + c];
                }
            }
            for (int c = 0; c < 3; ++c)
            {
                m[c] /= width * height;
            }
            return m;
        }

        public static double MeanAbsDiff(OslRender a, OslRender b)
        {
            double sum = 0;
            for (int i = 0; i < a.rgb.Length; ++i)
            {
                sum += Math.Abs(a.rgb[i] - b.rgb[i]);
            }
            return sum / a.rgb.Length;
        }
    }

    public static unsafe class OslRenderTools
    {
        private static readonly object processMutex = new object();

        public const string Dir = "/tmp/osl_renders";

        // Common camera, lights and ground for the comparisons; only the material of the object differs
        public static string Scene(string objectMaterial, string objectShape, string extra, int xres, int yres)
        {
            return "LookAt 0 2 -6  0 0.5 0  0 1 0\n" +
                   "Camera \"perspective\" \"float fov\" 35\n" +
                   "Film \"rgb\" \"integer xresolution\" " + xres + " \"integer yresolution\" " + yres + " \"string filename\" \"out.exr\"\n" +
                   "Sampler \"independent\" \"integer pixelsamples\" 16\n" +
                   "Integrator \"path\" \"integer maxdepth\" 5\n" +
                   "WorldBegin\n" +
                   "LightSource \"infinite\" \"rgb L\" [0.6 0.6 0.6]\n" +
                   "LightSource \"distant\" \"point3 from\" [3 5 -4] \"point3 to\" [0 0 0] \"rgb L\" [3 3 3]\n" +
                   extra +
                   "AttributeBegin\n" + objectMaterial + "\n" + objectShape + "\nAttributeEnd\n" +
                   "AttributeBegin\n" + "Material \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]\n" + "Translate 0 -1 0\n" +
                   "Shape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-5 0 -5  5 0 -5  5 0 5  -5 0 5]\nAttributeEnd\n";
        }

        public const string Sphere = "Shape \"sphere\" \"float radius\" 1";

        private static string PbrtDll()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "pbrt.dll");
            if (!File.Exists(path))
            {
                path = "/home/claude/pbrt-v4/src/pbrt/cmd/bin/Release/net10.0/pbrt.dll";
            }
            return path;
        }

        /// Renders a scene file with the command line of pbrt; returns the image (linear RGB). The scene is written next to the shaders of the tests.
        public static OslRender Render(string name, string sceneText, int spp, int seed, string extraArgs = "", string sceneDir = null)
        {
            string dir = sceneDir ?? OslTestShaders.Directory();
            string scenePath = Path.Combine(dir, name + "_" + spp + "_" + seed + ".pbrt");
            string exrPath = Path.Combine(dir, name + "_" + spp + "_" + seed + ".exr");
            File.WriteAllText(scenePath, sceneText);
            if (File.Exists(exrPath))
            {
                File.Delete(exrPath);
            }
            ProcessStartInfo psi = new ProcessStartInfo("dotnet");
            psi.ArgumentList.Add(PbrtDll());
            psi.ArgumentList.Add(scenePath);
            psi.ArgumentList.Add("--spp");
            psi.ArgumentList.Add(spp.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--seed");
            psi.ArgumentList.Add(seed.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--quiet");
            psi.ArgumentList.Add("--outfile");
            psi.ArgumentList.Add(exrPath);
            if (extraArgs.Length > 0)
            {
                foreach (string a in extraArgs.Split(' '))
                {
                    psi.ArgumentList.Add(a);
                }
            }
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            using (Process p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 || !File.Exists(exrPath))
                {
                    ADD_FAILURE("pbrt failed for " + name + ": " + err + outp);
                    return null;
                }
            }
            ImageAndMetadata im = Image.Read(exrPath);
            Image image = im.image;
            OslRender r = new OslRender();
            r.width = image.Resolution().x;
            r.height = image.Resolution().y;
            r.rgb = new float[r.width * r.height * 3];
            ImageChannelDesc desc = image.GetChannelDesc(new string[] { "R", "G", "B" });
            for (int y = 0; y < r.height; ++y)
            {
                for (int x = 0; x < r.width; ++x)
                {
                    ImageChannelValues v = image.GetChannels(new Point2i(x, y), desc);
                    for (int c = 0; c < 3; ++c)
                    {
                        r.rgb[3 * (y * r.width + x) + c] = v[c];
                    }
                }
            }
            return r;
        }

        // A 5x7 font (rows are 5 bit numbers) for the labels
        private static readonly string glyphChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789:_-./=()+, ";
        private static readonly byte[][] glyphs = new byte[][]
        {
            new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 }, new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E },
            new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E }, new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E },
            new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F }, new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 },
            new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F }, new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
            new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E }, new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C },
            new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 }, new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
            new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 }, new byte[] { 0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11 },
            new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E }, new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 },
            new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D }, new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
            new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E }, new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
            new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E }, new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 },
            new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A }, new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 },
            new byte[] { 0x11, 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04 }, new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F },
            new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E }, new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
            new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F }, new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E },
            new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 }, new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
            new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E }, new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
            new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E }, new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
            new byte[] { 0x00, 0x04, 0x00, 0x00, 0x00, 0x04, 0x00 }, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F },
            new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 }, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C },
            new byte[] { 0x01, 0x01, 0x02, 0x04, 0x08, 0x10, 0x10 }, new byte[] { 0x00, 0x00, 0x1F, 0x00, 0x1F, 0x00, 0x00 },
            new byte[] { 0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02 }, new byte[] { 0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08 },
            new byte[] { 0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00 }, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x0C, 0x04, 0x08 },
            new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
        };

        private static void DrawText(byte[] pixels, int w, int h, int x0, int y0, string text, int scale)
        {
            text = text.ToUpperInvariant();
            int x = x0;
            foreach (char ch in text)
            {
                int index = glyphChars.IndexOf(ch);
                if (index < 0)
                {
                    index = glyphChars.Length - 1;
                }
                byte[] g = glyphs[index];
                for (int row = 0; row < 7; ++row)
                {
                    for (int col = 0; col < 5; ++col)
                    {
                        if (((g[row] >> (4 - col)) & 1) == 0)
                        {
                            continue;
                        }
                        for (int sy = 0; sy < scale; ++sy)
                        {
                            for (int sx = 0; sx < scale; ++sx)
                            {
                                int px = x + col * scale + sx, py = y0 + row * scale + sy;
                                if (px >= 0 && py >= 0 && px < w && py < h)
                                {
                                    int o = 3 * (py * w + px);
                                    pixels[o] = pixels[o + 1] = pixels[o + 2] = 255;
                                }
                            }
                        }
                    }
                }
                x += 6 * scale;
            }
        }

        private static byte ToSRGB8(float v)
        {
            if (float.IsNaN(v) || v < 0)
            {
                v = 0;
            }
            double s = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(s * 255)));
        }

        private static uint[] crcTable = null;

        private static uint Crc(byte[] data, int offset, int length, uint crc = 0xFFFFFFFF)
        {
            if (crcTable == null)
            {
                uint[] t = new uint[256];
                for (uint n = 0; n < 256; ++n)
                {
                    uint c = n;
                    for (int k = 0; k < 8; ++k)
                    {
                        c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    }
                    t[n] = c;
                }
                crcTable = t;
            }
            for (int i = 0; i < length; ++i)
            {
                crc = crcTable[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            }
            return crc;
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length };
            s.Write(len, 0, 4);
            byte[] body = new byte[4 + data.Length];
            for (int i = 0; i < 4; ++i)
            {
                body[i] = (byte)type[i];
            }
            Array.Copy(data, 0, body, 4, data.Length);
            s.Write(body, 0, body.Length);
            uint crc = ~Crc(body, 0, body.Length);
            byte[] c = new byte[4] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc };
            s.Write(c, 0, 4);
        }

        public static void WritePng(string path, byte[] rgb8, int w, int h)
        {
            using (FileStream fs = File.Create(path))
            {
                fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                byte[] ihdr = new byte[13];
                ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
                ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
                ihdr[8] = 8;
                ihdr[9] = 2;
                WriteChunk(fs, "IHDR", ihdr);
                byte[] raw = new byte[h * (1 + 3 * w)];
                for (int y = 0; y < h; ++y)
                {
                    raw[y * (1 + 3 * w)] = 0;
                    Array.Copy(rgb8, y * 3 * w, raw, y * (1 + 3 * w) + 1, 3 * w);
                }
                MemoryStream ms = new MemoryStream();
                using (ZLibStream z = new ZLibStream(ms, CompressionLevel.Optimal, true))
                {
                    z.Write(raw, 0, raw.Length);
                }
                WriteChunk(fs, "IDAT", ms.ToArray());
                WriteChunk(fs, "IEND", new byte[0]);
            }
        }

        /// Writes /tmp/osl_renders/<name>_<spp>spp_seed<seed>.png: the image scaled up 2x with the labels (the mode, the spp and the seed) burned in
        public static string SaveLabelled(OslRender r, string name, string mode, int spp, int seed, string extraLine = "", float scale = 1f, string dir = null)
        {
            string outDir = dir ?? Dir;
            Directory.CreateDirectory(outDir);
            const int up = 2;
            int w = r.width * up, h = r.height * up;
            byte[] px = new byte[3 * w * h];
            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    int s = 3 * ((y / up) * r.width + x / up);
                    for (int c = 0; c < 3; ++c)
                    {
                        px[3 * (y * w + x) + c] = ToSRGB8(r.rgb[s + c] * scale);
                    }
                }
            }
            int lines = extraLine.Length > 0 ? 2 : 1;
            for (int y = 0; y < 10 * lines + 6 && y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    int o = 3 * (y * w + x);
                    px[o] = (byte)(px[o] / 4);
                    px[o + 1] = (byte)(px[o + 1] / 4);
                    px[o + 2] = (byte)(px[o + 2] / 4);
                }
            }
            DrawText(px, w, h, 4, 3, mode + "  " + spp + " SPP  SEED " + seed, 1);
            if (extraLine.Length > 0)
            {
                DrawText(px, w, h, 4, 13, extraLine, 1);
            }
            string path = Path.Combine(outDir, name + "_" + spp + "spp_seed" + seed + ".png");
            WritePng(path, px, w, h);
            return path;
        }

        public static OslRender Difference(OslRender a, OslRender b, float gain)
        {
            OslRender d = new OslRender();
            d.width = a.width;
            d.height = a.height;
            d.rgb = new float[a.rgb.Length];
            for (int i = 0; i < d.rgb.Length; ++i)
            {
                d.rgb[i] = Math.Abs(a.rgb[i] - b.rgb[i]) * gain;
            }
            return d;
        }

        public static string F(double[] v)
        {
            return "(" + v[0].ToString("F4", CultureInfo.InvariantCulture) + ", " + v[1].ToString("F4", CultureInfo.InvariantCulture) + ", " +
                   v[2].ToString("F4", CultureInfo.InvariantCulture) + ")";
        }

        /// Renders the OSL scene and the reference scene (same everything but the material), saves labelled PNGs of both and the difference, and
        /// returns the mean absolute difference; the means per channel are written to the console
        public static double Compare(string name, string oslMaterial, string refMaterial, string shape, string extra, int spp, int seed, string label,
                                     double expectedMad, double expectedMeanRel)
        {
            const int xres = 192, yres = 144;
            OslRender osl = Render(name + "_osl", Scene(oslMaterial, shape, extra, xres, yres), spp, seed);
            OslRender reference = Render(name + "_ref", Scene(refMaterial, shape, extra, xres, yres), spp, seed);
            if (osl == null || reference == null)
            {
                return double.NaN;
            }
            double mad = OslRender.MeanAbsDiff(osl, reference);
            double[] mo = osl.ChannelMeans(), mr = reference.ChannelMeans();
            string p1 = SaveLabelled(osl, name + "_osl", "OSL " + label, spp, seed);
            string p2 = SaveLabelled(reference, name + "_pbrt", "PBRT REFERENCE " + label, spp, seed);
            string p3 = SaveLabelled(Difference(osl, reference, 8), name + "_diffx8", "ABS DIFF X8 " + label, spp, seed,
                                     "MAD " + mad.ToString("F4", CultureInfo.InvariantCulture));
            Console.WriteLine("[OslPbrt] " + name + ": mean abs diff " + mad.ToString("F5", CultureInfo.InvariantCulture) + ", OSL means " + F(mo) +
                              ", pbrt means " + F(mr) + "\n    images: " + p1 + "\n            " + p2 + "\n            " + p3);
            EXPECT_LT(mad, expectedMad);
            for (int c = 0; c < 3; ++c)
            {
                EXPECT_LT(Math.Abs(mo[c] - mr[c]) / Math.Max(1e-3, mr[c]), expectedMeanRel);
            }
            return mad;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // The tests
    // ---------------------------------------------------------------------------------------------------------------------------------
    public static unsafe class OslPbrtTests
    {
        private static void ResetScene()
        {
            OSLScene.Reset();
            OSLSystem.spectralMode = OSLSpectralMode.Upsample;
            OSLSystem.emissionIlluminant = true;
            OSLSystem.AddSearchPath(OslTestShaders.Directory());
        }

        private static ParsedParameter P(string type, string name, params float[] values)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = type;
            p.name = name;
            foreach (float v in values)
            {
                p.AddFloat(v);
            }
            return p;
        }

        private static ParsedParameter PI(string name, params int[] values)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "integer";
            p.name = name;
            foreach (int v in values)
            {
                p.AddInt(v);
            }
            return p;
        }

        private static ParsedParameter PS(string type, string name, params string[] values)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = type;
            p.name = name;
            foreach (string v in values)
            {
                p.AddString(v);
            }
            return p;
        }

        private static TextureParameterDictionary Dict(NamedTextures textures, params ParsedParameter[] ps)
        {
            return new TextureParameterDictionary(new ParameterDictionary(new List<ParsedParameter>(ps), RGBColorSpace.sRGB), textures);
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Parser and parameters
        // ------------------------------------------------------------------------------------------------------------------------------

        [TEST("OslPbrt", "ParameterConversion")]
        public static void ParameterConversion()
        {
            OSLParam r;
            EXPECT_TRUE(OSLGroupBuilder.ConvertParameter(P("float", "a", 2.5f), out r));
            EXPECT_TRUE(r.type == TypeDescs.TypeFloat);
            EXPECT_EQ(((float[])r.value)[0], 2.5f);
            EXPECT_TRUE(OSLGroupBuilder.ConvertParameter(PI("i", 7), out r));
            EXPECT_TRUE(r.type == TypeDescs.TypeInt);
            EXPECT_EQ(((int[])r.value)[0], 7);
            EXPECT_TRUE(OSLGroupBuilder.ConvertParameter(PS("string", "s", "hello"), out r));
            EXPECT_TRUE(r.type == TypeDescs.TypeString);
            EXPECT_TRUE(OSLGroupBuilder.ConvertParameter(P("rgb", "c", 0.1f, 0.2f, 0.3f), out r));
            EXPECT_TRUE(r.type == TypeDescs.TypeColor);
            EXPECT_TRUE(OSLGroupBuilder.ConvertParameter(P("point3", "p", 1, 2, 3), out r));
            EXPECT_TRUE(r.type == TypeDescs.TypePoint);
            EXPECT_TRUE(OSLGroupBuilder.ConvertParameter(P("vector3", "v", 1, 2, 3), out r));
            EXPECT_TRUE(r.type == TypeDescs.TypeVector);
            EXPECT_TRUE(OSLGroupBuilder.ConvertParameter(P("normal", "n", 0, 0, 1), out r));
            EXPECT_TRUE(r.type == TypeDescs.TypeNormal);
            EXPECT_TRUE(OSLGroupBuilder.ConvertParameter(P("point2", "uv", 1, 2), out r));
            EXPECT_EQ(r.type.arraylen, 2);
            EXPECT_FALSE(OSLGroupBuilder.ConvertParameter(P("rgb", "bad", 1, 2), out r));
            ParsedParameter b = new ParsedParameter(new FileLoc());
            b.type = "bool";
            b.name = "flag";
            b.AddBool(true);
            EXPECT_TRUE(OSLGroupBuilder.ConvertParameter(b, out r));
            EXPECT_TRUE(r.type == TypeDescs.TypeInt);
            EXPECT_EQ(((int[])r.value)[0], 1);
        }

        [TEST("OslPbrt", "ParseMaterialAndLayers")]
        public static void ParseMaterialAndLayers()
        {
            ResetScene();
            OslTestShaders.Directory();
            string dir = "/tmp/osl_renders/scenes";
            string text =
                "Option \"oslsearchpath\" \"" + dir + "\"\n" +
                "LookAt 0 0 -5 0 0 0 0 1 0\nCamera \"perspective\"\nFilm \"rgb\" \"integer xresolution\" 32 \"integer yresolution\" 24\n" +
                "Sampler \"independent\"\nWorldBegin\n" +
                "Texture \"chk\" \"spectrum\" \"osl\" \"string shader\" \"checkerboard\" \"float scale_s\" 4 \"rgb Ca\" [1 0 0]\n" +
                "Texture \"gray\" \"float\" \"osl\" \"string shader\" \"fnoise\"\n" +
                "Material \"osl\" \"string shader\" \"matte\" \"rgb Cs\" [0.4 0.5 0.6] \"float Kd\" 0.5\n" +
                "MakeNamedMaterial \"layered\" \"string type\" \"osl\" \"string layers\" [ \"tex:checkerboard\" \"surf:matte\" ] " +
                "\"string connect\" [ \"tex.Cout surf.Cs\" ] \"float tex.scale_s\" 8 \"float Kd\" 0.9\n" +
                "MakeNamedMaterial \"textured\" \"string type\" \"osl\" \"string shader\" \"matte\" \"texture Cs\" \"chk\"\n" +
                "Shape \"sphere\"\n";
            BasicScene scene = new BasicScene();
            BasicSceneBuilder builder = new BasicSceneBuilder(scene);
            pbrt.ParseString(builder, text);
            NamedTextures textures = scene.CreateTextures();
            EXPECT_TRUE(textures.unboundedSpectrumTextures["chk"] is OSLSpectrumTexture);
            EXPECT_TRUE(textures.albedoSpectrumTextures["chk"] is OSLSpectrumTexture);
            EXPECT_TRUE(textures.floatTextures["gray"] is OSLFloatTexture);
            SortedDictionary<string, Material> named = new SortedDictionary<string, Material>(StringComparer.Ordinal);
            List<Material> materials = new List<Material>();
            scene.CreateMaterials(textures, named, materials);
            EXPECT_GE(materials.Count, 1);
            bool anyOsl = false;
            for (int i = 0; i < materials.Count; ++i)
            {
                anyOsl |= materials[i] is OSLMaterial;
            }
            EXPECT_TRUE(anyOsl);
            EXPECT_TRUE(named["layered"] is OSLMaterial);
            EXPECT_TRUE(named["textured"] is OSLMaterial);
        }

        [TEST("OslPbrt", "UnknownShaderIsAnError")]
        public static void UnknownShaderIsAnError()
        {
            ResetScene();
            string resolved, error;
            EXPECT_FALSE(OSLSystem.PrepareShader("no_such_shader_anywhere", OslTestShaders.Directory(), out resolved, out error));
            EXPECT_TRUE(error.Contains("no_such_shader_anywhere"));
        }

        [TEST("OslPbrt", "OslSourceIsCompiledOnTheFly")]
        public static void OslSourceIsCompiledOnTheFly()
        {
            ResetScene();
            string resolved, error;
            EXPECT_TRUE(OSLSystem.PrepareShader("constcolor", OslTestShaders.Directory(), out resolved, out error));
            EXPECT_EQ(resolved, "constcolor");
            // the second request is served from the cache
            EXPECT_TRUE(OSLSystem.PrepareShader("constcolor", OslTestShaders.Directory(), out resolved, out error));
        }

        [TEST("OslPbrt", "SpectralModes")]
        public static void SpectralModes()
        {
            ResetScene();
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            SampledSpectrum gray = OSLSystem.ToSpectrum(0.5f, 0.5f, 0.5f, lambda);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_FLOAT_EQ(gray[i], 0.5f);
            }
            SampledSpectrum up = OSLSystem.ToSpectrum(0.8f, 0.3f, 0.2f, lambda);
            SampledSpectrum expected = new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(0.8f, 0.3f, 0.2f)).Sample(lambda);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_FLOAT_EQ(up[i], expected[i]);
            }
            SampledSpectrum bright = OSLSystem.ToSpectrum(3f, 0.3f, 0.2f, lambda);
            SampledSpectrum expectedBright = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(3f, 0.3f, 0.2f)).Sample(lambda);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_FLOAT_EQ(bright[i], expectedBright[i]);
            }
            OSLSystem.spectralMode = OSLSpectralMode.Gray;
            SampledSpectrum g = OSLSystem.ToSpectrum(0.8f, 0.3f, 0.2f, lambda);
            for (int i = 1; i < NSpectrumSamples; ++i)
            {
                EXPECT_FLOAT_EQ(g[i], g[0]);
            }
            OSLSystem.spectralMode = OSLSpectralMode.Unbounded;
            SampledSpectrum ub = OSLSystem.ToSpectrum(0.8f, 0.3f, 0.2f, lambda);
            SampledSpectrum expectedUb = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(0.8f, 0.3f, 0.2f)).Sample(lambda);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_FLOAT_EQ(ub[i], expectedUb[i]);
            }
            // emission is an RGBIlluminantSpectrum by default
            OSLSystem.spectralMode = OSLSpectralMode.Upsample;
            SampledSpectrum em = OSLSystem.ToEmissionSpectrum(2f, 1f, 0.5f, lambda);
            SampledSpectrum expectedEm = new RGBIlluminantSpectrum(RGBColorSpace.sRGB, new RGB(2f, 1f, 0.5f)).Sample(lambda) *
                                          (1 / pbrt.SpectrumToPhotometric(RGBColorSpace.sRGB.illuminant));
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_FLOAT_EQ(em[i], expectedEm[i]);
            }
            ResetScene();
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // ShaderGlobals
        // ------------------------------------------------------------------------------------------------------------------------------

        private static OSLShaderGroup Probe(int mode, Transform worldFromShader)
        {
            string resolved, error;
            ASSERT_TRUE(OSLSystem.PrepareShader("probe", OslTestShaders.Directory(), out resolved, out error));
            OSLGroupSpec spec = new OSLGroupSpec();
            OSLLayerSpec layer = new OSLLayerSpec();
            layer.shader = "probe";
            layer.layer = "probe";
            OSLParam mp = new OSLParam();
            mp.name = "mode";
            mp.type = TypeDescs.TypeInt;
            mp.value = new int[] { mode };
            layer.parameters.Add(mp);
            spec.layers.Add(layer);
            spec.outputLayer = "probe";
            spec.outputParam = "Cout";
            OSLShaderGroup g = OSLGroupBuilder.Build(spec, "probe", worldFromShader, new string[] { "probe.Cout", "probe.Fout" });
            g.outputName = "Cout";
            return g;
        }

        private static SurfaceInteraction MakeSurface(Point3f p, Vector3f wo, Vector3f dpdu, Vector3f dpdv, int objectId)
        {
            SurfaceInteraction si = new SurfaceInteraction(new Point3fi(p), new Point2f(0.25f, 0.75f), wo, dpdu, dpdv, new Normal3f(0, 0, 0),
                                                           new Normal3f(0, 0, 0), 0.5f, false);
            si.objectId = objectId;
            si.dpdx = new Vector3f(0.1f, 0, 0);
            si.dpdy = new Vector3f(0, 0.2f, 0);
            si.dudx = 0.05f;
            si.dudy = 0.01f;
            si.dvdx = 0.02f;
            return si;
        }

        private static float[] Eval(int mode, SurfaceInteraction si, Transform worldFromShader, int rayType = 0)
        {
            OSLShaderGroup g = Probe(mode, worldFromShader);
            float* v = stackalloc float[3];
            si.rayType = rayType;
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            bool ok = OSLShading.EvalOutput(g, si, default(TextureEvalContext), ref lambda, v, 3);
            ASSERT_TRUE(ok);
            return new float[] { v[0], v[1], v[2] };
        }

        private static void ExpectVec(float[] v, float x, float y, float z, double tol = 1e-4)
        {
            EXPECT_NEAR(v[0], x, tol);
            EXPECT_NEAR(v[1], y, tol);
            EXPECT_NEAR(v[2], z, tol);
        }

        [TEST("OslPbrt", "GlobalsBasic")]
        public static void GlobalsBasic()
        {
            ResetScene();
            Transform id = new Transform();
            SurfaceInteraction si = MakeSurface(new Point3f(1, 2, 3), new Vector3f(0, 0, 1), new Vector3f(2, 0, 0), new Vector3f(0, 3, 0), 0);
            ExpectVec(Eval(0, si, id), 1, 2, 3);
            ExpectVec(Eval(1, si, id), 0, 0, 1);
            ExpectVec(Eval(2, si, id), 0, 0, 1);
            ExpectVec(Eval(3, si, id), 0, 0, -1);
            ExpectVec(Eval(4, si, id), 0.25f, 0.75f, 0);
            ExpectVec(Eval(5, si, id), 2, 0, 0);
            ExpectVec(Eval(6, si, id), 0, 3, 0);
            ExpectVec(Eval(11, si, id), 0, 0, 0);
            ExpectVec(Eval(12, si, id), 0.5f, 0.5f, 0.5f);
            ExpectVec(Eval(13, si, id), 0.05f, 0.01f, 0.02f);
            // the differentials of P: Dx(P) and Dy(P) are dPdx and dPdy
            ExpectVec(Eval(14, si, id), 0.1f, 0, 0);
            ExpectVec(Eval(15, si, id), 0, 0.2f, 0);
        }

        [TEST("OslPbrt", "GlobalsBackfacing")]
        public static void GlobalsBackfacing()
        {
            ResetScene();
            Transform id = new Transform();
            SurfaceInteraction si = MakeSurface(new Point3f(1, 2, 3), new Vector3f(0, 0, -1), new Vector3f(2, 0, 0), new Vector3f(0, 3, 0), 0);
            ExpectVec(Eval(11, si, id), 1, 1, 1);
            // N and Ng face the viewer
            ExpectVec(Eval(1, si, id), 0, 0, -1);
            ExpectVec(Eval(2, si, id), 0, 0, -1);
            ExpectVec(Eval(3, si, id), 0, 0, 1);
        }

        [TEST("OslPbrt", "GlobalsRenderSpaceIsConvertedToWorld")]
        public static void GlobalsRenderSpaceIsConvertedToWorld()
        {
            ResetScene();
            // render space = world translated by (10, 0, 0) and rotated 90 degrees about z
            Transform renderFromWorld = Translate(new Vector3f(10, 0, 0)) * RotateZ(90);
            OSLScene.SetRenderSpace(renderFromWorld);
            Point3f worldP = new Point3f(1, 2, 3);
            Point3f renderP = renderFromWorld.Apply(worldP);
            Vector3f worldN = new Vector3f(0, 1, 0);
            Vector3f renderN = renderFromWorld.Apply(worldN);
            Vector3f dpdu = renderFromWorld.Apply(new Vector3f(1, 0, 0));
            Vector3f dpdv = renderFromWorld.Apply(new Vector3f(0, 0, 1));
            // dpdu x dpdv = (1,0,0) x (0,0,1) = (0,-1,0): the normal is -y in world space
            SurfaceInteraction si = MakeSurface(renderP, -renderN * -1, dpdu, dpdv, 0);
            si.wo = -renderFromWorld.Apply(new Vector3f(0, -1, 0)) * -1;
            Transform id = new Transform();
            ExpectVec(Eval(0, si, id), 1, 2, 3);
            ExpectVec(Eval(1, si, id), 0, -1, 0);
            ExpectVec(Eval(5, si, id), 1, 0, 0);
            ExpectVec(Eval(6, si, id), 0, 0, 1);
            ResetScene();
        }

        [TEST("OslPbrt", "GlobalsObjectAndShaderSpace")]
        public static void GlobalsObjectAndShaderSpace()
        {
            ResetScene();
            Transform objectFromRender = new Transform();
            Transform renderFromObject = Translate(new Vector3f(1, 2, 3)) * Scale(2, 2, 2);
            OSLScene.AddObject(77, "ball", renderFromObject, null, 12.5f);
            Transform worldFromShader = Translate(new Vector3f(0, 5, 0));
            SurfaceInteraction si = MakeSurface(new Point3f(3, 4, 5), new Vector3f(0, 0, 1), new Vector3f(2, 0, 0), new Vector3f(0, 3, 0), 77);
            // object space: (3,4,5) = (1,2,3) + 2 * (1, 1, 1)
            ExpectVec(Eval(7, si, new Transform()), 1, 1, 1);
            // shader space: (3,4,5) - (0,5,0)
            ExpectVec(Eval(8, si, worldFromShader), 3, -1, 5);
            // the name and the surface area of the object
            ExpectVec(Eval(23, si, new Transform()), 1, 1, 1);
            ExpectVec(Eval(24, si, new Transform()), 12.5f, 12.5f, 12.5f);
            ExpectVec(Eval(29, si, new Transform()), 12.5f, 12.5f, 12.5f);
            ResetScene();
        }

        [TEST("OslPbrt", "GlobalsNamedCoordinateSystem")]
        public static void GlobalsNamedCoordinateSystem()
        {
            ResetScene();
            Transform worldFromCS = Translate(new Vector3f(1, 0, 0)) * RotateZ(90);
            OSLScene.AddCoordinateSystem("mycs", worldFromCS, worldFromCS, 0, 1);
            SurfaceInteraction si = MakeSurface(new Point3f(1, 2, 0), new Vector3f(0, 0, 1), new Vector3f(2, 0, 0), new Vector3f(0, 3, 0), 0);
            // csP = Inverse(worldFromCS)(1,2,0) = RotateZ(-90)((0,2,0)) = (2, 0, 0)
            ExpectVec(Eval(10, si, new Transform()), 2, 0, 0);
            ResetScene();
        }

        [TEST("OslPbrt", "GlobalsCameraSpaces")]
        public static void GlobalsCameraSpaces()
        {
            ResetScene();
            // camera at (0,0,-5) looking at +z: worldFromCamera = Translate(0,0,-5)
            OSLScene.worldFromCamera = new AnimatedTransform(Translate(new Vector3f(0, 0, -5)));
            OSLScene.resolution = new Point2i(200, 100);
            OSLScene.cameraFov = 90;
            OSLScene.clipNear = 1;
            OSLScene.clipFar = 101;
            OSLScene.screenWindow = new float[] { -2, -1, 2, 1 };
            SurfaceInteraction si = MakeSurface(new Point3f(1, 0.5f, 0), new Vector3f(0, 0, 1), new Vector3f(2, 0, 0), new Vector3f(0, 3, 0), 0);
            ExpectVec(Eval(9, si, new Transform()), 1, 0.5f, 5);
            // screen: perspective with fov 90: x/z, y/z; depth (z * far / range - far * near / range)
            float[] screen = Eval(27, si, new Transform());
            EXPECT_NEAR(screen[0], 1.0 / 5, 1e-4);
            EXPECT_NEAR(screen[1], 0.5 / 5, 1e-4);
            // NDC: x: (sx + 2) / 4, y: 1 - (sy + 1) / 2 (the y axis points down)
            float[] ndc = Eval(28, si, new Transform());
            EXPECT_NEAR(ndc[0], (0.2 + 2) / 4, 1e-4);
            EXPECT_NEAR(ndc[1], 1 - (0.1 + 1) / 2, 1e-4);
            ResetScene();
        }

        [TEST("OslPbrt", "GlobalsAttributes")]
        public static void GlobalsAttributes()
        {
            ResetScene();
            OSLScene.resolution = new Point2i(640, 480);
            OSLScene.cameraFov = 42;
            OSLScene.cameraName = "perspective";
            OSLScene.shutterOpen = 0.25f;
            OSLScene.shutterClose = 0.75f;
            OSLScene.pixelAspect = 1;
            SurfaceInteraction si = MakeSurface(new Point3f(0, 0, 0), new Vector3f(0, 0, 1), new Vector3f(2, 0, 0), new Vector3f(0, 3, 0), 0);
            ExpectVec(Eval(19, si, new Transform()), 640, 480, 0);
            ExpectVec(Eval(20, si, new Transform()), 42, 42, 42);
            ExpectVec(Eval(21, si, new Transform()), 1, 1, 1);
            ExpectVec(Eval(22, si, new Transform()), 0.25f, 0.75f, 0);
            ExpectVec(Eval(25, si, new Transform()), 0.25f, 0.75f, 0);
            ExpectVec(Eval(30, si, new Transform()), 1, 1, 1);
            ResetScene();
        }

        [TEST("OslPbrt", "GlobalsRayTypes")]
        public static void GlobalsRayTypes()
        {
            ResetScene();
            SurfaceInteraction si = MakeSurface(new Point3f(0, 0, 0), new Vector3f(0, 0, 1), new Vector3f(2, 0, 0), new Vector3f(0, 3, 0), 0);
            Transform id = new Transform();
            ExpectVec(Eval(16, si, id, 0), 1, 1, 1);
            ExpectVec(Eval(17, si, id, 0), 0, 0, 0);
            int diffuse = OSLRayType.FromFlags(BxDFFlags.DiffuseReflection);
            ExpectVec(Eval(16, si, id, diffuse), 0, 0, 0);
            ExpectVec(Eval(17, si, id, diffuse), 1, 1, 1);
            ExpectVec(Eval(18, si, id, diffuse), 0, 0, 0);
            int glossy = OSLRayType.FromFlags(BxDFFlags.GlossyReflection);
            ExpectVec(Eval(18, si, id, glossy), 1, 1, 1);
            EXPECT_TRUE((OSLRayType.FromFlags(BxDFFlags.SpecularTransmission) & OSLRayType.Refraction) != 0);
            EXPECT_TRUE((OSLRayType.FromFlags(BxDFFlags.SpecularReflection) & OSLRayType.Reflection) != 0);
        }

        [TEST("OslPbrt", "FlipHandedness")]
        public static void FlipHandedness()
        {
            ResetScene();
            OSLScene.AddObject(5, "mirrored", Scale(-1, 1, 1), null, 1);
            SurfaceInteraction si = MakeSurface(new Point3f(0, 0, 0), new Vector3f(0, 0, 1), new Vector3f(2, 0, 0), new Vector3f(0, 3, 0), 5);
            OSLShaderGroup g = Probe(0, new Transform());
            OSLThreadState ts = OSLThreadState.Current();
            ShaderGlobals sg = new ShaderGlobals();
            OSLShading.FillGlobals(ref sg, ts, g, si, default(TextureEvalContext), si.wo, 1);
            EXPECT_EQ(sg.flipHandedness, 1);
            OSLScene.AddObject(6, "plain", Scale(1, 1, 1), null, 1);
            si.objectId = 6;
            OSLShading.FillGlobals(ref sg, ts, g, si, default(TextureEvalContext), si.wo, 1);
            EXPECT_EQ(sg.flipHandedness, 0);
            EXPECT_EQ(sg.raytype, 1);
            ResetScene();
        }

        [TEST("OslPbrt", "TraceUsesTheAggregate")]
        public static void TraceUsesTheAggregate()
        {
            ResetScene();
            // a big triangle at z = 0.5 above the shaded point at z = 0, facing -z: trace(P + N * 0.001, N) hits it at 0.499
            Transform id = new Transform();
            int[] indices = new int[] { 0, 1, 2 };
            Point3f[] pts = new Point3f[] { new Point3f(-10, -10, 2.0f), new Point3f(10, -10, 2.0f), new Point3f(0, 10, 2.0f) };
            TriangleMesh mesh = new TriangleMesh(id, false, new pstd.vector<int>(indices), new pstd.vector<Point3f>(pts), new pstd.vector<Vector3f>(),
                                                 new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(), new pstd.vector<int>(), new Allocator());
            pstd.vector<Shape> tris = Triangle.CreateTriangles(mesh, new Allocator());
            List<Primitive> prims = new List<Primitive>();
            for (int ti = 0; ti < (int)tris.size(); ++ti)
            {
                prims.Add(new SimplePrimitive(tris[ti], null, 0));
            }
            OSLScene.aggregate = new BVHAggregate(prims);
            // mode 26 traces along -N, so flip: use a surface whose normal is +z, trace(P + N * 0.001, -N) goes to -z and misses
            SurfaceInteraction miss = MakeSurface(new Point3f(0, 0, 0), new Vector3f(0, 0, 1), new Vector3f(2, 0, 0), new Vector3f(0, 3, 0), 0);
            ExpectVec(Eval(26, miss, id), 0, 0, 0);
            // a surface whose normal is -z (dpdu x dpdv): -N = +z goes to the triangle at distance 2 - 0.001
            SurfaceInteraction hit = MakeSurface(new Point3f(0, 0, 0), new Vector3f(0, 0, -1), new Vector3f(0, 3, 0), new Vector3f(2, 0, 0), 0);
            float[] r = Eval(26, hit, id);
            EXPECT_NEAR(r[0], 1, 1e-6);
            EXPECT_NEAR(r[1], 1.999, 2e-3);
            ResetScene();
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Textures
        // ------------------------------------------------------------------------------------------------------------------------------

        private static SurfaceInteraction TextureSurface(Point2f uv)
        {
            SurfaceInteraction si = new SurfaceInteraction(new Point3fi(new Point3f(0.3f, 0.2f, 0.1f)), uv, new Vector3f(0, 0, 1), new Vector3f(1, 0, 0),
                                                           new Vector3f(0, 1, 0), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0, false);
            return si;
        }

        [TEST("OslPbrt", "SpectrumTextureCheckerboard")]
        public static void SpectrumTextureCheckerboard()
        {
            ResetScene();
            OslTestShaders.Directory();
            NamedTextures nt = new NamedTextures();
            TextureParameterDictionary d = Dict(nt, PS("string", "shader", "checkerboard"), P("float", "scale_s", 2), P("float", "scale_t", 2),
                                                P("rgb", "Ca", 0.8f, 0.8f, 0.8f), P("rgb", "Cb", 0.2f, 0.2f, 0.2f));
            ParameterDictionary pd = d;
            OSLSpectrumTexture tex = OSLSpectrumTexture.Create(new Transform(), d, SpectrumType.Albedo, new FileLoc(), new Allocator());
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            // u = 0.25, v = 0.25 -> cs = 0.5, ct = 0.5 -> even: Ca
            SampledSpectrum a = tex.Evaluate(new TextureEvalContext(TextureSurface(new Point2f(0.25f, 0.25f))), lambda);
            SampledSpectrum b = tex.Evaluate(new TextureEvalContext(TextureSurface(new Point2f(0.75f, 0.25f))), lambda);
            EXPECT_NEAR(a.Average(), 0.8, 1e-3);
            EXPECT_NEAR(b.Average(), 0.2, 1e-3);
        }

        [TEST("OslPbrt", "FloatTextureNoise")]
        public static void FloatTextureNoise()
        {
            ResetScene();
            OslTestShaders.Directory();
            NamedTextures nt = new NamedTextures();
            TextureParameterDictionary d = Dict(nt, PS("string", "shader", "fnoise"), P("float", "lo", 0.2f), P("float", "hi", 0.4f));
            OSLFloatTexture tex = OSLFloatTexture.Create(new Transform(), d, new FileLoc(), new Allocator());
            float lo = 1, hi = 0;
            for (int i = 0; i < 50; ++i)
            {
                SurfaceInteraction si = TextureSurface(new Point2f(0.1f, 0.1f));
                si.pi = new Point3fi(new Point3f(0.1f * i, 0.07f * i, 0.13f * i));
                float v = tex.Evaluate(new TextureEvalContext(si));
                lo = Math.Min(lo, v);
                hi = Math.Max(hi, v);
            }
            EXPECT_GE(lo, 0.2 - 1e-4);
            EXPECT_LE(hi, 0.4 + 1e-4);
            EXPECT_GT(hi - lo, 0.02);
        }

        [TEST("OslPbrt", "TextureConnectedToMaterialParameter")]
        public static void TextureConnectedToMaterialParameter()
        {
            ResetScene();
            OslTestShaders.Directory();
            NamedTextures nt = new NamedTextures();
            TextureParameterDictionary d = Dict(nt, PS("string", "shader", "constcolor"), P("rgb", "value", 0.25f, 0.25f, 0.25f));
            OSLSpectrumTexture tex = OSLSpectrumTexture.Create(new Transform(), d, SpectrumType.Unbounded, new FileLoc(), new Allocator());
            nt.unboundedSpectrumTextures["c"] = tex;
            nt.albedoSpectrumTextures["c"] = tex;
            ParsedParameter tp = PS("texture", "Cs", "c");
            OSLGroupSpec spec = OSLGroupBuilder.FromParameters(Dict(nt, PS("string", "shader", "matte"), tp, P("float", "Kd", 0.5f)), new FileLoc(), false,
                                                               "", new Allocator());
            // the layers of the texture were merged in front of the matte layer, and connected to its Cs
            EXPECT_EQ(spec.layers.Count, 2);
            EXPECT_EQ(spec.layers[1].shader, "matte");
            EXPECT_EQ(spec.connections.Count, 1);
            EXPECT_EQ(spec.connections[0].dstParam, "Cs");
            EXPECT_EQ(spec.connections[0].dstLayer, "matte");
            EXPECT_EQ(spec.connections[0].srcParam, "Cout");
            // and the surface is Kd * Cs: 0.5 * 0.25 / pi
            SurfaceInteraction si = TextureSurface(new Point2f(0.5f, 0.5f));
            si.wo = new Vector3f(0, 0, 1);
            si.shading.n = new Normal3f(0, 0, 1);
            si.shading.dpdu = new Vector3f(1, 0, 0);
            OSLShaderGroup g = OSLGroupBuilder.Build(spec, "material", new Transform(), null);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            OSLShadeResult r = OSLShading.ShadeSurface(g, si, si.shading.n, si.shading.dpdu, ref lambda);
            ASSERT_TRUE(r.bxdf != null);
            SampledSpectrum f = r.bxdf.f(new Vector3f(0, 0, 1), Normalize(new Vector3f(0.3f, 0.2f, 0.8f)), TransportMode.Radiance);
            EXPECT_NEAR(f.Average(), 0.5 * 0.25 / Math.PI, 2e-3);
        }

        [TEST("OslPbrt", "PbrtTextureBoundToParameter")]
        public static void PbrtTextureBoundToParameter()
        {
            ResetScene();
            OslTestShaders.Directory();
            NamedTextures nt = new NamedTextures();
            nt.floatTextures["half"] = new FloatConstantTexture(0.5f);
            OSLGroupSpec spec = OSLGroupBuilder.FromParameters(Dict(nt, PS("string", "shader", "matte"), PS("texture", "Kd", "half")), new FileLoc(), false,
                                                               "", new Allocator());
            EXPECT_EQ(spec.bindings.Count, 1);
            SurfaceInteraction si = TextureSurface(new Point2f(0.5f, 0.5f));
            si.wo = new Vector3f(0, 0, 1);
            si.shading.n = new Normal3f(0, 0, 1);
            si.shading.dpdu = new Vector3f(1, 0, 0);
            OSLShaderGroup g = OSLGroupBuilder.Build(spec, "material", new Transform(), null);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            OSLShadeResult r = OSLShading.ShadeSurface(g, si, si.shading.n, si.shading.dpdu, ref lambda);
            ASSERT_TRUE(r.bxdf != null);
            SampledSpectrum f = r.bxdf.f(new Vector3f(0, 0, 1), Normalize(new Vector3f(0.3f, 0.2f, 0.8f)), TransportMode.Radiance);
            EXPECT_NEAR(f.Average(), 0.5 / Math.PI, 2e-3);
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Closures to BxDFs
        // ------------------------------------------------------------------------------------------------------------------------------

        private static Vector3f shadeWo = Normalize(new Vector3f(0.2f, 0.1f, 1));

        private static OSLShadeResult ShadeMaterial(string shader, params ParsedParameter[] ps)
        {
            ResetScene();
            OslTestShaders.Directory();
            List<ParsedParameter> list = new List<ParsedParameter>();
            list.Add(PS("string", "shader", shader));
            list.AddRange(ps);
            NamedTextures nt = new NamedTextures();
            OSLGroupSpec spec = OSLGroupBuilder.FromParameters(new TextureParameterDictionary(new ParameterDictionary(list, RGBColorSpace.sRGB), nt),
                                                               new FileLoc(), false, "", new Allocator());
            OSLShaderGroup g = OSLGroupBuilder.Build(spec, "material", new Transform(), null);
            SurfaceInteraction si = TextureSurface(new Point2f(0.5f, 0.5f));
            si.wo = shadeWo;
            si.shading.n = new Normal3f(0, 0, 1);
            si.shading.dpdu = new Vector3f(1, 0, 0);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            return OSLShading.ShadeSurface(g, si, si.shading.n, si.shading.dpdu, ref lambda);
        }

        [TEST("OslPbrt", "ClosureDiffuseMatchesPbrtDiffuse")]
        public static void ClosureDiffuseMatchesPbrtDiffuse()
        {
            OSLShadeResult r = ShadeMaterial("matte", P("rgb", "Cs", 0.5f, 0.5f, 0.5f));
            ASSERT_TRUE(r.bxdf != null);
            Vector3f wo = Normalize(new Vector3f(0.2f, 0.1f, 1)), wi = Normalize(new Vector3f(-0.4f, 0.3f, 0.7f));
            SampledSpectrum f = r.bxdf.f(wo, wi, TransportMode.Radiance);
            SampledSpectrum expected = new DiffuseBxDF(new SampledSpectrum(0.5f)).f(wo, wi, TransportMode.Radiance);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_NEAR(f[i], expected[i], 1e-5);
            }
            EXPECT_NEAR(r.bxdf.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All), new DiffuseBxDF(new SampledSpectrum(1)).PDF(wo, wi,
                        TransportMode.Radiance, BxDFReflTransFlags.All), 1e-5);
            EXPECT_FALSE(r.hasEmission);
        }

        [TEST("OslPbrt", "ClosureConductorMatchesPbrtConductor")]
        public static void ClosureConductorMatchesPbrtConductor()
        {
            OSLShadeResult r = ShadeMaterial("mx_conductor", P("float", "roughness", 0.3f));
            ASSERT_TRUE(r.bxdf != null);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            SampledSpectrum eta = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(0.2f, 0.92f, 1.1f)).Sample(lambda);
            SampledSpectrum k = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(3.9f, 2.45f, 2.14f)).Sample(lambda);
            ConductorBxDF reference = new ConductorBxDF(new TrowbridgeReitzDistribution(0.09f, 0.09f), eta, k);
            Vector3f wo = Normalize(new Vector3f(0.2f, 0.1f, 1)), wi = Normalize(new Vector3f(-0.1f, 0.15f, 1));
            SampledSpectrum a = r.bxdf.f(wo, wi, TransportMode.Radiance), b = reference.f(wo, wi, TransportMode.Radiance);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_NEAR(a[i] / b[i], 1, 1e-3);
            }
        }

        [TEST("OslPbrt", "ClosureEmission")]
        public static void ClosureEmission()
        {
            OSLShadeResult r = ShadeMaterial("glow", P("rgb", "Ce", 0.5f, 0.5f, 0.5f), P("float", "power", 4));
            EXPECT_TRUE(r.hasEmission);
            EXPECT_TRUE(r.bxdf == null);
            // 2 * the gray illuminant: the spectrum of RGBIlluminantSpectrum(2, 2, 2)
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            SampledSpectrum expected = new RGBIlluminantSpectrum(RGBColorSpace.sRGB, new RGB(2, 2, 2)).Sample(lambda) *
                                          (1 / pbrt.SpectrumToPhotometric(RGBColorSpace.sRGB.illuminant));
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_NEAR(r.emission[i] / expected[i], 1, 1e-4);
            }
        }

        [TEST("OslPbrt", "ClosureMixWeights")]
        public static void ClosureMixWeights()
        {
            // 0.5 * diffuse(0.5) + 0.5 * microfacet: the diffuse lobe alone has f = 0.25 * 0.5/pi for gray
            OSLShadeResult r = ShadeMaterial("mixed", P("rgb", "Cs", 0.5f, 0.5f, 0.5f), P("float", "mixw", 0.5f));
            ASSERT_TRUE(r.bxdf != null);
            EXPECT_EQ(r.bxdf.Count, 2);
            EXPECT_TRUE(IsGlossy(r.bxdf.Flags()));
            EXPECT_TRUE(IsDiffuse(r.bxdf.Flags()));
            OSLShadeResult onlyDiffuse = ShadeMaterial("mixed", P("rgb", "Cs", 0.5f, 0.5f, 0.5f), P("float", "mixw", 0f));
            EXPECT_EQ(onlyDiffuse.bxdf.Count, 1);
        }

        [TEST("OslPbrt", "ClosureLayerScalesTheBase")]
        public static void ClosureLayerScalesTheBase()
        {
            OSLShadeResult r = ShadeMaterial("layered", P("rgb", "Cs", 0.5f, 0.5f, 0.5f), P("float", "roughness", 0.2f));
            ASSERT_TRUE(r.bxdf != null);
            EXPECT_EQ(r.bxdf.Count, 2);
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // BxDFs: sampling consistency, chi^2, energy, reciprocity
        // ------------------------------------------------------------------------------------------------------------------------------

        private static BSDF Wrap(SurfaceInteraction si, BxDF bxdf)
        {
            return new BSDF(si.shading.n, si.shading.dpdu, bxdf);
        }

        private static BxDF MakeLobe(string name)
        {
            SampledSpectrum one = new SampledSpectrum(1f);
            switch (name)
            {
                case "OrenNayar":
                    return new OSLOrenNayarBxDF(0.5f);
                case "Burley":
                    return new OSLBurleyDiffuseBxDF(0.6f);
                case "Phong":
                    return new OSLPhongBxDF(20);
                case "Ward":
                    return new OSLWardBxDF(0.3f, 0.15f);
                case "Sheen":
                    return new OSLSheenBxDF(0.5f);
                case "GGXRefl":
                    return new OSLMicrofacetBxDF(new OSLGGXDistribution(0.3f, 0.3f), new OSLDielectricFresnel(1.5f), 1.5f, true, false, one, one);
                case "GGXReflAniso":
                    return new OSLMicrofacetBxDF(new OSLGGXDistribution(0.4f, 0.15f), new OSLDielectricFresnel(1.5f), 1.5f, true, false, one, one);
                case "BeckmannRefl":
                    return new OSLMicrofacetBxDF(new OSLBeckmannDistribution(0.3f, 0.3f), new OSLDielectricFresnel(1.5f), 1.5f, true, false, one, one);
                case "GGXGlass":
                    return new OSLMicrofacetBxDF(new OSLGGXDistribution(0.3f, 0.3f), new OSLDielectricFresnel(1.5f), 1.5f, true, true, one, one);
                case "GGXGlassInv":
                    return new OSLMicrofacetBxDF(new OSLGGXDistribution(0.3f, 0.3f), new OSLDielectricFresnel(1 / 1.5f), 1 / 1.5f, true, true, one, one);
                case "BeckmannGlass":
                    return new OSLMicrofacetBxDF(new OSLBeckmannDistribution(0.35f, 0.2f), new OSLDielectricFresnel(1.5f), 1.5f, true, true, one, one);
                case "BeckmannConductor":
                    return new OSLMicrofacetBxDF(new OSLBeckmannDistribution(0.3f, 0.3f), new OSLConductorFresnel(new SampledSpectrum(0.2f),
                                                 new SampledSpectrum(3.9f)), 1, true, false, one, one);
                case "SchlickRefl":
                    return new OSLMicrofacetBxDF(new OSLGGXDistribution(0.3f, 0.3f), new OSLSchlickFresnel(new SampledSpectrum(0.04f), one, 5), 1.5f, true, false,
                                                 one, one);
                case "TintedGlass":
                    return new OSLTintedBxDF(new OSLMicrofacetBxDF(new OSLGGXDistribution(0.3f, 0.3f), new OSLDielectricFresnel(1.5f), 1.5f, true, true,
                                                                   one, one), new SampledSpectrum(1f), new SampledSpectrum(0.5f));
                default:
                    return new DiffuseBxDF(one);
            }
        }

        private static void ChiSquare(string name)
        {
            BSDFsTests.TestBSDF((SurfaceInteraction si, Allocator alloc) => Wrap(si, MakeLobe(name)), "Osl" + name);
        }

        [TEST("OslPbrt", "Chi2OrenNayar")] public static void Chi2OrenNayar() { ChiSquare("OrenNayar"); }
        [TEST("OslPbrt", "Chi2Burley")] public static void Chi2Burley() { ChiSquare("Burley"); }
        [TEST("OslPbrt", "Chi2Phong")] public static void Chi2Phong() { ChiSquare("Phong"); }
        [TEST("OslPbrt", "Chi2Ward")] public static void Chi2Ward() { ChiSquare("Ward"); }
        [TEST("OslPbrt", "Chi2Sheen")] public static void Chi2Sheen() { ChiSquare("Sheen"); }
        [TEST("OslPbrt", "Chi2GGXRefl")] public static void Chi2GGXRefl() { ChiSquare("GGXRefl"); }
        [TEST("OslPbrt", "Chi2GGXReflAniso")] public static void Chi2GGXReflAniso() { ChiSquare("GGXReflAniso"); }
        [TEST("OslPbrt", "Chi2BeckmannRefl")] public static void Chi2BeckmannRefl() { ChiSquare("BeckmannRefl"); }
        [TEST("OslPbrt", "Chi2GGXGlass")] public static void Chi2GGXGlass() { ChiSquare("GGXGlass"); }
        [TEST("OslPbrt", "Chi2GGXGlassInv")] public static void Chi2GGXGlassInv() { ChiSquare("GGXGlassInv"); }
        [TEST("OslPbrt", "Chi2BeckmannGlass")] public static void Chi2BeckmannGlass() { ChiSquare("BeckmannGlass"); }
        [TEST("OslPbrt", "Chi2BeckmannConductor")] public static void Chi2BeckmannConductor() { ChiSquare("BeckmannConductor"); }
        [TEST("OslPbrt", "Chi2SchlickRefl")] public static void Chi2SchlickRefl() { ChiSquare("SchlickRefl"); }

        [TEST("OslPbrt", "Chi2CompositeWithTiltedLobes")]
        public static void Chi2CompositeWithTiltedLobes()
        {
            // a diffuse lobe, a GGX lobe with its own normal (tilted away from the shading normal), and a translucent one
            BSDFsTests.TestBSDF((SurfaceInteraction si, Allocator alloc) =>
            {
                OSLClosureBxDF c = new OSLClosureBxDF();
                c.Add(new DiffuseBxDF(new SampledSpectrum(1f)), new SampledSpectrum(0.4f), 0.4f, new Vector3f(0, 0, 1), new Vector3f(0, 0, 0));
                c.Add(MakeLobe("GGXReflAniso"), new SampledSpectrum(0.5f), 0.5f, Normalize(new Vector3f(0.3f, 0.1f, 1)), Normalize(new Vector3f(1, 0.2f, 0)));
                c.Add(new DiffuseTransmissionBxDF(new SampledSpectrum(0f), new SampledSpectrum(1f)), new SampledSpectrum(0.2f), 0.2f,
                      new Vector3f(0, 0, 1), new Vector3f(0, 0, 0));
                return Wrap(si, c);
            }, "OslComposite");
        }

        private static void ExpectSampleConsistency(BxDF bxdf, string name, bool checkEnergy)
        {
            RNG rng = new RNG();
            int bad = 0;
            double[] albedoSums = new double[4];
            Vector3f[] wos = new Vector3f[] { Normalize(new Vector3f(0.3f, 0.2f, 1)), Normalize(new Vector3f(-0.8f, 0.4f, 0.5f)),
                                              Normalize(new Vector3f(0.1f, -0.1f, 0.15f)), Normalize(new Vector3f(0.4f, 0.1f, -0.7f)) };
            for (int k = 0; k < wos.Length; ++k)
            {
                Vector3f wo = wos[k];
                double sum = 0;
                const int n = 20000;
                for (int i = 0; i < n; ++i)
                {
                    Float uc = rng.Uniform<Float>();
                    Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    pstd.optional<BSDFSample> s = bxdf.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (!s.has_value())
                    {
                        continue;
                    }
                    BSDFSample bs = s.value();
                    if (!(bs.pdf > 0) || float.IsNaN(bs.f.Average()))
                    {
                        ++bad;
                        continue;
                    }
                    // the sampled f and pdf are those of f() and PDF() in the direction
                    SampledSpectrum f = bxdf.f(wo, bs.wi, TransportMode.Radiance);
                    Float pdf = bxdf.PDF(wo, bs.wi, TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (Math.Abs(f.Average() - bs.f.Average()) > 1e-3 * Math.Max(1, bs.f.Average()) || Math.Abs(pdf - bs.pdf) > 1e-3 * Math.Max(1, bs.pdf))
                    {
                        ++bad;
                    }
                    sum += bs.f.Average() * AbsCosTheta(bs.wi) / bs.pdf;
                }
                albedoSums[k] = sum / n;
            }
            if (bad > 2)
            {
                ADD_FAILURE(name + ": " + bad + " samples whose f/pdf differ from f()/PDF()");
            }
            if (checkEnergy)
            {
                for (int k = 0; k < wos.Length; ++k)
                {
                    EXPECT_LT(albedoSums[k], 1.03);
                }
            }
        }

        [TEST("OslPbrt", "SampleMatchesEvalAndEnergy")]
        public static void SampleMatchesEvalAndEnergy()
        {
            foreach (string name in new string[] { "OrenNayar", "Burley", "Phong", "Sheen", "GGXRefl", "GGXReflAniso", "BeckmannRefl", "GGXGlass",
                                                   "BeckmannGlass", "BeckmannConductor", "SchlickRefl", "TintedGlass" })
            {
                ExpectSampleConsistency(MakeLobe(name), name, true);
            }
            // The Ward model is not energy conserving at grazing angles (as in the paper), only the consistency is tested
            ExpectSampleConsistency(MakeLobe("Ward"), "Ward", false);
        }

        [TEST("OslPbrt", "WhiteFurnaceDiffuseAlbedo")]
        public static void WhiteFurnaceDiffuseAlbedo()
        {
            // The albedo of the (sigma = 0) Oren-Nayar and of Burley (roughness 0: Lambert) is 1 for any wo
            RNG rng = new RNG();
            foreach (BxDF b in new BxDF[] { new OSLOrenNayarBxDF(0) })
            {
                Vector3f wo = Normalize(new Vector3f(0.3f, 0.5f, 0.4f));
                double sum = 0;
                const int n = 100000;
                for (int i = 0; i < n; ++i)
                {
                    pstd.optional<BSDFSample> s = b.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                             TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (s.has_value())
                    {
                        sum += s.value().f.Average() * AbsCosTheta(s.value().wi) / s.value().pdf;
                    }
                }
                EXPECT_NEAR(sum / n, 1.0, 0.01);
            }
            // Phong at normal incidence has albedo 1 (the normalization of the modified Phong)
            OSLPhongBxDF phong = new OSLPhongBxDF(30);
            double psum = 0;
            for (int i = 0; i < 100000; ++i)
            {
                pstd.optional<BSDFSample> s = phong.Sample_f(new Vector3f(0, 0, 1), rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                            TransportMode.Radiance, BxDFReflTransFlags.All);
                if (s.has_value())
                {
                    psum += s.value().f.Average() * AbsCosTheta(s.value().wi) / s.value().pdf;
                }
            }
            EXPECT_NEAR(psum / 100000, 1.0, 0.01);
        }

        [TEST("OslPbrt", "SmoothGlassConservesEnergy")]
        public static void SmoothGlassConservesEnergy()
        {
            // reflection (Fresnel) + refraction (1 - Fresnel) of the classic closures sum to 1 (in importance mode, with no radiance scaling)
            OSLSpecularReflectionBxDF refl = new OSLSpecularReflectionBxDF(new OSLDielectricFresnel(1.5f));
            OSLSpecularRefractionBxDF refr = new OSLSpecularRefractionBxDF(1.5f);
            foreach (Vector3f wo in new Vector3f[] { new Vector3f(0, 0, 1), Normalize(new Vector3f(0.5f, 0, 0.5f)), Normalize(new Vector3f(0.5f, 0.1f, -0.8f)) })
            {
                pstd.optional<BSDFSample> a = refl.Sample_f(wo, 0.5f, new Point2f(0.5f, 0.5f), TransportMode.Importance, BxDFReflTransFlags.All);
                pstd.optional<BSDFSample> b = refr.Sample_f(wo, 0.5f, new Point2f(0.5f, 0.5f), TransportMode.Importance, BxDFReflTransFlags.All);
                float total = 0;
                if (a.has_value())
                {
                    total += a.value().f.Average() * AbsCosTheta(a.value().wi);
                }
                if (b.has_value())
                {
                    total += b.value().f.Average() * AbsCosTheta(b.value().wi);
                }
                if (Math.Abs(total - 1.0) > 1e-4)
                {
                    ADD_FAILURE("wo " + wo.x + "," + wo.y + "," + wo.z + ": reflection " + (a.has_value() ? a.value().f.Average() * AbsCosTheta(a.value().wi) : -1) +
                                ", refraction " + (b.has_value() ? b.value().f.Average() * AbsCosTheta(b.value().wi) : -1));
                }
            }
        }

        [TEST("OslPbrt", "Reciprocity")]
        public static void Reciprocity()
        {
            RNG rng = new RNG();
            foreach (string name in new string[] { "OrenNayar", "Burley", "Ward", "Sheen", "GGXRefl", "GGXReflAniso", "BeckmannRefl", "BeckmannConductor", "SchlickRefl" })
            {
                BxDF b = MakeLobe(name);
                for (int i = 0; i < 200; ++i)
                {
                    Vector3f wo = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f wi = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (wo.z < 0.05f || wi.z < 0.05f)
                    {
                        continue;
                    }
                    float a = b.f(wo, wi, TransportMode.Radiance).Average(), c = b.f(wi, wo, TransportMode.Radiance).Average();
                    if (Math.Abs(a - c) > 1e-3 * Math.Max(1, Math.Max(a, c)))
                    {
                        ADD_FAILURE(name + ": f(wo, wi) = " + a + " but f(wi, wo) = " + c);
                        break;
                    }
                }
            }
            // The dielectric: f(wo, wi) radiance-mode = f(wi, wo) importance-mode * eta^2 relation of pbrt's DielectricBxDF
            OSLMicrofacetBxDF glass = (OSLMicrofacetBxDF)MakeLobe("GGXGlass");
            Vector3f woT = Normalize(new Vector3f(0.2f, 0.1f, 1));
            pstd.optional<BSDFSample> s = glass.Sample_f(woT, 0.99f, new Point2f(0.3f, 0.6f), TransportMode.Importance, BxDFReflTransFlags.All);
            if (s.has_value() && s.value().IsTransmission())
            {
                // radiance transport of the reversed path scales by 1/eta^2 compared to importance transport
                float fi = glass.f(woT, s.value().wi, TransportMode.Importance).Average();
                float fr = glass.f(woT, s.value().wi, TransportMode.Radiance).Average();
                float etap = 1.5f;
                EXPECT_NEAR(fi / Math.Max(fr, 1e-9), etap * etap, 0.02 * etap * etap);
            }
        }

        [TEST("OslPbrt", "MicrofacetMatchesPbrtDielectric")]
        public static void MicrofacetMatchesPbrtDielectric()
        {
            // The rough dielectric (GGX, eta 1.5) is pbrt's DielectricBxDF: same f and PDF
            OSLMicrofacetBxDF mine = (OSLMicrofacetBxDF)MakeLobe("GGXGlass");
            DielectricBxDF theirs = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.3f, 0.3f));
            RNG rng = new RNG();
            for (int i = 0; i < 400; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (Math.Abs(wo.z) < 0.05f || Math.Abs(wi.z) < 0.05f)
                {
                    continue;
                }
                foreach (TransportMode mode in new TransportMode[] { TransportMode.Radiance, TransportMode.Importance })
                {
                    float a = mine.f(wo, wi, mode).Average(), b = theirs.f(wo, wi, mode).Average();
                    EXPECT_NEAR(a, b, 1e-4 * Math.Max(1, b));
                    float pa = mine.PDF(wo, wi, mode, BxDFReflTransFlags.All), pb = theirs.PDF(wo, wi, mode, BxDFReflTransFlags.All);
                    EXPECT_NEAR(pa, pb, 1e-3 * Math.Max(1, pb));
                }
            }
        }

        [TEST("OslPbrt", "ClosureBxDFSpecularSamplesCarryTheirProbability")]
        public static void ClosureBxDFSpecularSamplesCarryTheirProbability()
        {
            // mirror (weight 0.5) + diffuse (weight 0.5): the specular sample has pdf = selection probability and f = weight * f_lobe
            OSLClosureBxDF c = new OSLClosureBxDF();
            c.Add(new OSLSpecularReflectionBxDF(new OSLDielectricFresnel(0)), new SampledSpectrum(0.5f), 0.5f, new Vector3f(0, 0, 1), new Vector3f(0, 0, 0));
            c.Add(new DiffuseBxDF(new SampledSpectrum(1f)), new SampledSpectrum(0.5f), 0.5f, new Vector3f(0, 0, 1), new Vector3f(0, 0, 0));
            Vector3f wo = Normalize(new Vector3f(0.3f, 0.1f, 1));
            pstd.optional<BSDFSample> s = c.Sample_f(wo, 0.2f, new Point2f(0.5f, 0.5f), TransportMode.Radiance, BxDFReflTransFlags.All);
            ASSERT_TRUE(s.has_value());
            EXPECT_TRUE(s.value().IsSpecular());
            EXPECT_NEAR(s.value().pdf, 0.5, 1e-6);
            EXPECT_NEAR(s.value().f.Average() * AbsCosTheta(s.value().wi), 0.5, 1e-5);
            // restricted to transmission there is nothing to sample
            EXPECT_FALSE(c.Sample_f(wo, 0.2f, new Point2f(0.5f, 0.5f), TransportMode.Radiance, BxDFReflTransFlags.Transmission).has_value());
        }

        [TEST("OslPbrt", "LobeBelowTheSurfaceIsMirrored")]
        public static void LobeBelowTheSurfaceIsMirrored()
        {
            // a lobe whose normal is flipped (a back face shaded with the front normal) behaves as on the other side
            OSLClosureBxDF c = new OSLClosureBxDF();
            c.Add(new DiffuseBxDF(new SampledSpectrum(1f)), new SampledSpectrum(1f), 1f, new Vector3f(0, 0, -1), new Vector3f(0, 0, 0));
            Vector3f wo = Normalize(new Vector3f(0.3f, 0.1f, -1)), wi = Normalize(new Vector3f(-0.2f, 0.4f, -0.8f));
            EXPECT_NEAR(c.f(wo, wi, TransportMode.Radiance).Average(), InvPi, 1e-5);
            EXPECT_NEAR(c.f(wo, -wi, TransportMode.Radiance).Average(), 0, 1e-6);
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Renders: the OSL material against the pbrt material of the same look
        // ------------------------------------------------------------------------------------------------------------------------------

        [TEST("OslPbrt", "RenderMatteVsDiffuse")]
        public static void RenderMatteVsDiffuse()
        {
            ResetScene();
            OslRenderTools.Compare("matte_vs_diffuse", "Material \"osl\" \"string shader\" \"matte\" \"rgb Cs\" [0.8 0.3 0.2]",
                                   "Material \"diffuse\" \"rgb reflectance\" [0.8 0.3 0.2]", OslRenderTools.Sphere, "", 64, 7, "MATTE VS DIFFUSE", 0.01, 0.02);
        }

        [TEST("OslPbrt", "RenderConductorVsPbrtConductor")]
        public static void RenderConductorVsPbrtConductor()
        {
            ResetScene();
            OslRenderTools.Compare("mx_conductor_vs_conductor", "Material \"osl\" \"string shader\" \"mx_conductor\" \"float roughness\" 0.3",
                                   "Material \"conductor\" \"rgb eta\" [0.2 0.92 1.1] \"rgb k\" [3.9 2.45 2.14] \"float roughness\" 0.09 " +
                                   "\"bool remaproughness\" false", OslRenderTools.Sphere, "", 64, 7, "MX CONDUCTOR R0.3 VS PBRT CONDUCTOR", 0.01, 0.02);
        }

        [TEST("OslPbrt", "RenderRoughGlassVsDielectric")]
        public static void RenderRoughGlassVsDielectric()
        {
            ResetScene();
            OslRenderTools.Compare("mx_glass_vs_dielectric", "Material \"osl\" \"string shader\" \"mx_glass\" \"float roughness\" 0.2",
                                   "Material \"dielectric\" \"float eta\" 1.5 \"float roughness\" 0.04 \"bool remaproughness\" false", OslRenderTools.Sphere, "",
                                   64, 7, "MX ROUGH GLASS VS PBRT DIELECTRIC", 0.01, 0.02);
        }

        [TEST("OslPbrt", "RenderSmoothGlassVsDielectric")]
        public static void RenderSmoothGlassVsDielectric()
        {
            ResetScene();
            OslRenderTools.Compare("mx_smooth_glass_vs_dielectric", "Material \"osl\" \"string shader\" \"mx_glass\" \"float roughness\" 0",
                                   "Material \"dielectric\" \"float eta\" 1.5", OslRenderTools.Sphere, "", 64, 7, "MX SMOOTH GLASS VS PBRT DIELECTRIC", 0.01, 0.02);
        }

        [TEST("OslPbrt", "RenderClassicGlassVsDielectric")]
        public static void RenderClassicGlassVsDielectric()
        {
            ResetScene();
            // reflection(N, eta) + refraction(N, eta) of the OSL glass shader of testrender is the smooth dielectric
            string glass = "surface classicglass(float eta = 1.5) { if (backfacing()) { Ci = refraction(N, 1.0 / eta) + reflection(N, 1.0 / eta); } " +
                           "else { Ci = reflection(N, eta) + refraction(N, eta); } }";
            File.WriteAllText(Path.Combine(OslTestShaders.Directory(), "classicglass.osl"), glass);
            OslRenderTools.Compare("classic_glass_vs_dielectric", "Material \"osl\" \"string shader\" \"classicglass\"",
                                   "Material \"dielectric\" \"float eta\" 1.5", OslRenderTools.Sphere, "", 64, 7, "CLASSIC GLASS VS PBRT DIELECTRIC", 0.03, 0.03);
        }

        [TEST("OslPbrt", "RenderMirrorVsConductor")]
        public static void RenderMirrorVsConductor()
        {
            ResetScene();
            // reflection(N, 0) is a perfect mirror: the conductor with k = 0 and an index of refraction of 0 ... pbrt's smooth conductor with a huge k
            OslRenderTools.Compare("mirror_vs_conductor", "Material \"osl\" \"string shader\" \"metal\" \"float eta\" 0 \"rgb Cs\" [0.95 0.95 0.95]",
                                   "Material \"conductor\" \"rgb eta\" [0.0001 0.0001 0.0001] \"rgb k\" [100000 100000 100000] \"float roughness\" 0", OslRenderTools.Sphere,
                                   "", 64, 7, "MIRROR CS0.95 VS PBRT SMOOTH CONDUCTOR", 0.02, 0.05);
        }

        [TEST("OslPbrt", "RenderProceduralTextures")]
        public static void RenderProceduralTextures()
        {
            ResetScene();
            const int spp = 32, seed = 3, xres = 192, yres = 144;
            string extra = "Texture \"mar\" \"spectrum\" \"osl\" \"string shader\" \"marble\"\n" +
                           "Texture \"chk\" \"spectrum\" \"osl\" \"string shader\" \"checkerboard\" \"float scale_s\" 8 \"float scale_t\" 8 " +
                           "\"rgb Ca\" [0.9 0.9 0.9] \"rgb Cb\" [0.1 0.2 0.5]\n";
            string scene = "LookAt 0 2 -6  0 0.5 0  0 1 0\nCamera \"perspective\" \"float fov\" 35\n" +
                           "Film \"rgb\" \"integer xresolution\" " + xres + " \"integer yresolution\" " + yres + " \"string filename\" \"out.exr\"\n" +
                           "Sampler \"independent\" \"integer pixelsamples\" 16\nIntegrator \"path\" \"integer maxdepth\" 4\nWorldBegin\n" +
                           "LightSource \"infinite\" \"rgb L\" [0.6 0.6 0.6]\n" +
                           "LightSource \"distant\" \"point3 from\" [3 5 -4] \"point3 to\" [0 0 0] \"rgb L\" [3 3 3]\n" + extra +
                           "AttributeBegin\nMaterial \"osl\" \"string shader\" \"matte\" \"texture Cs\" \"mar\"\nShape \"sphere\" \"float radius\" 1\nAttributeEnd\n" +
                           "AttributeBegin\nMaterial \"osl\" \"string shader\" \"matte\" \"texture Cs\" \"chk\"\nTranslate 0 -1 0\n" +
                           "Shape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-5 0 -5  5 0 -5  5 0 5  -5 0 5] " +
                           "\"point2 uv\" [0 0 1 0 1 1 0 1]\nAttributeEnd\n";
            OslRender r = OslRenderTools.Render("procedural_textures", scene, spp, seed);
            ASSERT_TRUE(r != null);
            string path = OslRenderTools.SaveLabelled(r, "procedural_textures_osl", "OSL MARBLE SPHERE + CHECKER PLANE", spp, seed);
            Console.WriteLine("[OslPbrt] procedural textures: means " + OslRenderTools.F(r.ChannelMeans()) + "\n    image: " + path);
            // the marble has two colors (veins and base): the sphere region has a large variance in the blue/red ratio; the plane is a checkerboard
            double minRatio = 1e9, maxRatio = 0;
            for (int y = yres / 4; y < yres * 3 / 4; ++y)
            {
                for (int x = xres / 3; x < xres * 2 / 3; ++x)
                {
                    int o = 3 * (y * xres + x);
                    double ratio = r.rgb[o + 2] / Math.Max(1e-3, r.rgb[o]);
                    minRatio = Math.Min(minRatio, ratio);
                    maxRatio = Math.Max(maxRatio, ratio);
                }
            }
            EXPECT_GT(maxRatio - minRatio, 0.3);
            // bottom rows are the checkerboard plane: red/blue of the squares differ
            double bluish = 0, white = 0;
            for (int x = 0; x < xres; ++x)
            {
                int o = 3 * ((yres - 4) * xres + x);
                if (r.rgb[o + 2] > r.rgb[o] * 1.5) ++bluish; else ++white;
            }
            EXPECT_GT(bluish, 10);
            EXPECT_GT(white, 10);
        }

        [TEST("OslPbrt", "RenderOslTextureVsPbrtTexture")]
        public static void RenderOslTextureVsPbrtTexture()
        {
            ResetScene();
            // the OSL checkerboard (s,t from u,v) on a sphere vs pbrt's checkerboard: same pattern up to the filtering of pbrt's texture
            string oslTex = "Texture \"t\" \"spectrum\" \"osl\" \"string shader\" \"checkerboard\" \"float scale_s\" 8 \"float scale_t\" 4 " +
                            "\"rgb Ca\" [0.9 0.9 0.9] \"rgb Cb\" [0.1 0.1 0.1]\n";
            string pbrtTex = "Texture \"t\" \"spectrum\" \"checkerboard\" \"float uscale\" 8 \"float vscale\" 4 \"rgb tex1\" [0.9 0.9 0.9] \"rgb tex2\" [0.1 0.1 0.1]\n";
            const int xres = 192, yres = 144, spp = 64, seed = 5;
            OslRender a = OslRenderTools.Render("osl_checker_sphere", OslRenderTools.Scene("Material \"osl\" \"string shader\" \"matte\" \"texture Cs\" \"t\"",
                                                OslRenderTools.Sphere, oslTex, xres, yres), spp, seed);
            OslRender b = OslRenderTools.Render("pbrt_checker_sphere", OslRenderTools.Scene("Material \"diffuse\" \"texture reflectance\" \"t\"",
                                                OslRenderTools.Sphere, pbrtTex, xres, yres), spp, seed);
            ASSERT_TRUE(a != null && b != null);
            double mad = OslRender.MeanAbsDiff(a, b);
            string p1 = OslRenderTools.SaveLabelled(a, "osl_checker_sphere", "OSL CHECKERBOARD", spp, seed);
            string p2 = OslRenderTools.SaveLabelled(b, "pbrt_checker_sphere", "PBRT CHECKERBOARD", spp, seed);
            Console.WriteLine("[OslPbrt] OSL vs pbrt checkerboard: mean abs diff " + mad.ToString("F5", CultureInfo.InvariantCulture) + ", means " +
                              OslRenderTools.F(a.ChannelMeans()) + " vs " + OslRenderTools.F(b.ChannelMeans()) + "\n    " + p1 + "\n    " + p2);
            EXPECT_LT(mad, 0.05);
            double[] ma = a.ChannelMeans(), mb = b.ChannelMeans();
            EXPECT_LT(Math.Abs(ma[0] - mb[0]) / mb[0], 0.05);
        }

        [TEST("OslPbrt", "RenderLayeredAndMixedClosures")]
        public static void RenderLayeredAndMixedClosures()
        {
            ResetScene();
            const int spp = 64, seed = 11, xres = 192, yres = 144;
            // layered: a clear coat (dielectric_bsdf) over oren_nayar; reference: pbrt's coated diffuse with the same coat and base (no interlayer scattering)
            OslRender layered = OslRenderTools.Render("layered_osl", OslRenderTools.Scene("Material \"osl\" \"string shader\" \"layered\" \"rgb Cs\" [0.8 0.2 0.2] " +
                                                      "\"float roughness\" 0.15", OslRenderTools.Sphere, "", xres, yres), spp, seed);
            OslRender mixed = OslRenderTools.Render("mixed_osl", OslRenderTools.Scene("Material \"osl\" \"string shader\" \"mixed\" \"rgb Cs\" [0.8 0.2 0.2] " +
                                                     "\"float mixw\" 0.5", OslRenderTools.Sphere, "", xres, yres), spp, seed);
            OslRender plain = OslRenderTools.Render("matte_osl_for_layers", OslRenderTools.Scene("Material \"osl\" \"string shader\" \"matte\" " +
                                                    "\"rgb Cs\" [0.8 0.2 0.2]", OslRenderTools.Sphere, "", xres, yres), spp, seed);
            ASSERT_TRUE(layered != null && mixed != null && plain != null);
            string p1 = OslRenderTools.SaveLabelled(layered, "layered_osl", "OSL LAYER(DIELECTRIC, OREN NAYAR)", spp, seed);
            string p2 = OslRenderTools.SaveLabelled(mixed, "mixed_osl", "OSL 0.5 DIFFUSE + 0.5 GGX", spp, seed);
            string p3 = OslRenderTools.SaveLabelled(plain, "matte_osl_layer_base", "OSL MATTE (BASE ONLY)", spp, seed);
            double[] ml = layered.ChannelMeans(), mm = mixed.ChannelMeans(), mp = plain.ChannelMeans();
            Console.WriteLine("[OslPbrt] layered means " + OslRenderTools.F(ml) + ", mixed " + OslRenderTools.F(mm) + ", matte " + OslRenderTools.F(mp) +
                              "\n    " + p1 + "\n    " + p2 + "\n    " + p3);
            // the clear coat adds specular reflection on top of the (attenuated) base: more energy than the base, less than base + full coat
            EXPECT_GT(OslRender.MeanAbsDiff(layered, plain), 0.003);
            EXPECT_GT(OslRender.MeanAbsDiff(mixed, plain), 0.003);
            // the mixed material loses half of the diffuse (0.5 weight) and gains a glossy lobe (0.5 * F)
            EXPECT_LT(Math.Abs(ml[0] - mp[0]) / mp[0], 0.25);
            EXPECT_LT(Math.Abs(mm[0] - mp[0]) / mp[0], 0.35);
        }

        [TEST("OslPbrt", "RenderEmission")]
        public static void RenderEmission()
        {
            ResetScene();
            const int spp = 128, seed = 13, xres = 192, yres = 144;
            // An emissive sphere to the side of the object. pbrt's reference is the same sphere as a diffuse area light of the same radiance
            string objectShape = "Shape \"sphere\" \"float radius\" 1";
            string oslEmitter = "AttributeBegin\nMaterial \"osl\" \"string shader\" \"glow\" \"rgb Ce\" [1 0.6 0.2] \"float power\" 4\nTranslate 2.4 0 0\n" +
                                "Shape \"sphere\" \"float radius\" 0.5\nAttributeEnd\n";
            string pbrtEmitter = "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [4 2.4 0.8]\nMaterial \"diffuse\" \"rgb reflectance\" [0 0 0]\n" +
                                 "Translate 2.4 0 0\nShape \"sphere\" \"float radius\" 0.5\nAttributeEnd\n";
            OslRender a = OslRenderTools.Render("emission_osl", OslRenderTools.Scene("Material \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]", objectShape,
                                                oslEmitter, xres, yres), spp, seed);
            OslRender b = OslRenderTools.Render("emission_pbrt", OslRenderTools.Scene("Material \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]", objectShape,
                                                pbrtEmitter, xres, yres), spp, seed);
            ASSERT_TRUE(a != null && b != null);
            string p1 = OslRenderTools.SaveLabelled(a, "emission_osl", "OSL EMISSION() SPHERE", spp, seed);
            string p2 = OslRenderTools.SaveLabelled(b, "emission_pbrt", "PBRT AREA LIGHT L=4,2.4,0.8", spp, seed);
            // the directly visible emitter: its center pixels; the emitter is at world x = 2.4, in the picture to the right of the center
            double[] ma = a.ChannelMeans(), mb = b.ChannelMeans();
            // emitter pixels: very bright ones
            int countA = 0;
            double sumA = 0, sumB = 0;
            for (int i = 0; i < a.width * a.height; ++i)
            {
                if (a.rgb[3 * i] > 2 && b.rgb[3 * i] > 2)
                {
                    ++countA;
                    sumA += a.rgb[3 * i];
                    sumB += b.rgb[3 * i];
                }
            }
            Console.WriteLine("[OslPbrt] emission: OSL means " + OslRenderTools.F(ma) + ", pbrt " + OslRenderTools.F(mb) + ", emitter pixels " + countA +
                              ", mean red there " + (sumA / Math.Max(1, countA)).ToString("F3", CultureInfo.InvariantCulture) + " vs " +
                              (sumB / Math.Max(1, countA)).ToString("F3", CultureInfo.InvariantCulture) + "\n    " + p1 + "\n    " + p2);
            EXPECT_GT(countA, 100);
            EXPECT_NEAR(sumA / countA, 4.0, 0.2);
            EXPECT_NEAR(sumB / countA, 4.0, 0.2);
            // the lit surroundings agree: the OSL emitter is found by the bsdf sampling only (unbiased, noisier), the area light also by light sampling
            for (int c = 0; c < 3; ++c)
            {
                EXPECT_LT(Math.Abs(ma[c] - mb[c]) / mb[c], 0.1);
            }
        }

        private static double Rmse(OslRender a, OslRender b)
        {
            double sum = 0;
            for (int i = 0; i < a.rgb.Length; ++i)
            {
                double d = a.rgb[i] - b.rgb[i];
                sum += d * d;
            }
            return Math.Sqrt(sum / a.rgb.Length);
        }

        [TEST("OslPbrt", "RenderEmissionStrategies")]
        public static void RenderEmissionStrategies()
        {
            ResetScene();
            const int xres = 128, yres = 96, spp = 32, seed = 21, refSpp = 1024;
            string objectShape = "Shape \"sphere\" \"float radius\" 1";
            string body = "Material \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]";
            // a small emitter: where light sampling matters
            string oslEmitter = "AttributeBegin\nMaterial \"osl\" \"string shader\" \"glow\" \"rgb Ce\" [1 0.6 0.2] \"float power\" 40\nTranslate 2.0 1.2 -1\n" +
                                "Shape \"sphere\" \"float radius\" 0.12\nAttributeEnd\n";
            string pbrtEmitter = "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [40 24 8]\nMaterial \"diffuse\" \"rgb reflectance\" [0 0 0]\n" +
                                 "Translate 2.0 1.2 -1\nShape \"sphere\" \"float radius\" 0.12\nAttributeEnd\n";
            // no other lights: the emitter lights the scene
            Func<string, string, string> sceneOf = (string emitter, string integrator) =>
                "LookAt 0 2 -6  0 0.5 0  0 1 0\nCamera \"perspective\" \"float fov\" 35\n" +
                "Film \"rgb\" \"integer xresolution\" " + xres + " \"integer yresolution\" " + yres + " \"string filename\" \"out.exr\"\n" +
                "Sampler \"independent\" \"integer pixelsamples\" 16\n" + integrator + "\nWorldBegin\n" +
                "AttributeBegin\n" + body + "\n" + objectShape + "\nAttributeEnd\n" + emitter +
                "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]\nTranslate 0 -1 0\n" +
                "Shape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-5 0 -5  5 0 -5  5 0 5  -5 0 5]\nAttributeEnd\n";
            string[] names = new string[] { "light_only", "bsdf_only", "mis" };
            string[] labels = new string[] { "LIGHT SAMPLING ONLY", "BSDF SAMPLING ONLY", "MIS (PATH)" };
            string[] integrators = new string[]
            {
                "Integrator \"simplepath\" \"integer maxdepth\" 1 \"bool samplelights\" true \"bool samplebsdf\" false",
                "Integrator \"simplepath\" \"integer maxdepth\" 1 \"bool samplelights\" false \"bool samplebsdf\" true",
                "Integrator \"path\" \"integer maxdepth\" 1"
            };
            OslRender reference = OslRenderTools.Render("emit_ref_pbrt", sceneOf(pbrtEmitter, integrators[2]), refSpp, 99);
            ASSERT_TRUE(reference != null);
            string refPath = OslRenderTools.SaveLabelled(reference, "emit_reference_pbrt_arealight", "PBRT AREA LIGHT REFERENCE MIS", refSpp, 99);
            Console.WriteLine("[OslPbrt] emitter reference (pbrt area light, path, " + refSpp + " spp): means " + OslRenderTools.F(reference.ChannelMeans()) + "\n    " + refPath);
            for (int k = 0; k < 3; ++k)
            {
                OslRender o = OslRenderTools.Render("emit_osl_" + names[k], sceneOf(oslEmitter, integrators[k]), spp, seed);
                OslRender p = OslRenderTools.Render("emit_pbrt_" + names[k], sceneOf(pbrtEmitter, integrators[k]), spp, seed);
                ASSERT_TRUE(o != null && p != null);
                string p1 = OslRenderTools.SaveLabelled(o, "emit_osl_" + names[k], "OSL EMITTER " + labels[k], spp, seed);
                string p2 = OslRenderTools.SaveLabelled(p, "emit_pbrt_" + names[k], "PBRT AREA LIGHT " + labels[k], spp, seed);
                double[] mo = o.ChannelMeans(), mp = p.ChannelMeans(), mr = reference.ChannelMeans();
                double rmseO = Rmse(o, reference), rmseP = Rmse(p, reference);
                double relMean = Math.Abs(mo[0] - mr[0]) / mr[0];
                Console.WriteLine("[OslPbrt] emitter " + labels[k] + " " + spp + " spp: OSL means " + OslRenderTools.F(mo) + ", pbrt " + OslRenderTools.F(mp) +
                                  ", reference " + OslRenderTools.F(mr) + "; mean abs diff OSL-pbrt " +
                                  OslRender.MeanAbsDiff(o, p).ToString("F5", CultureInfo.InvariantCulture) + "; RMSE vs reference: OSL " +
                                  rmseO.ToString("F4", CultureInfo.InvariantCulture) + ", pbrt " + rmseP.ToString("F4", CultureInfo.InvariantCulture) +
                                  "\n    " + p1 + "\n    " + p2);
                // the mean of each strategy is the mean of the reference (the strategies are unbiased); the noise of the OSL emitter is that of the area light
                EXPECT_LT(relMean, 0.08);
                EXPECT_LT(Math.Abs(mp[0] - mr[0]) / mr[0], 0.08);
                EXPECT_LT(rmseO, rmseP * 1.5 + 0.01);
                EXPECT_LT(rmseP, rmseO * 1.5 + 0.01);
            }
        }

        // The integrators of pbrt on the same scene with an OSL material and with the pbrt material of the same look
        [TEST("OslPbrt", "RenderAllIntegrators")]
        public static void RenderAllIntegrators()
        {
            ResetScene();
            const int xres = 96, yres = 72, spp = 32, seed = 5;
            string classicGlass = "surface classicglass(float eta = 1.5) { if (backfacing()) { Ci = refraction(N, 1.0 / eta) + reflection(N, 1.0 / eta); } " +
                                  "else { Ci = reflection(N, eta) + refraction(N, eta); } }";
            File.WriteAllText(Path.Combine(OslTestShaders.Directory(), "classicglass.osl"), classicGlass);
            string[] integrators = new string[]
            {
                "Integrator \"path\" \"integer maxdepth\" 4",
                "Integrator \"volpath\" \"integer maxdepth\" 4",
                "Integrator \"simplepath\" \"integer maxdepth\" 4",
                "Integrator \"randomwalk\" \"integer maxdepth\" 4",
                "Integrator \"bdpt\" \"integer maxdepth\" 4",
                "Integrator \"mlt\" \"integer maxdepth\" 4 \"integer mutationsperpixel\" 32",
                "Integrator \"sppm\" \"integer maxdepth\" 4 \"integer photonsperiteration\" 20000",
                "Integrator \"lightpath\" \"integer maxdepth\" 4"
            };
            string[] materialNames = new string[] { "matte", "conductor", "glass", "emitter" };
            string[] oslMaterials = new string[]
            {
                "Material \"osl\" \"string shader\" \"matte\" \"rgb Cs\" [0.8 0.3 0.2]",
                "Material \"osl\" \"string shader\" \"mx_conductor\" \"float roughness\" 0.3",
                "Material \"osl\" \"string shader\" \"classicglass\"",
                "Material \"osl\" \"string shader\" \"glow\" \"rgb Ce\" [1 0.6 0.2] \"float power\" 2"
            };
            string[] pbrtMaterials = new string[]
            {
                "Material \"diffuse\" \"rgb reflectance\" [0.8 0.3 0.2]",
                "Material \"conductor\" \"rgb eta\" [0.2 0.92 1.1] \"rgb k\" [3.9 2.45 2.14] \"float roughness\" 0.09 \"bool remaproughness\" false",
                "Material \"dielectric\" \"float eta\" 1.5",
                "Material \"diffuse\" \"rgb reflectance\" [0 0 0]\nAreaLightSource \"diffuse\" \"rgb L\" [2 1.2 0.4]"
            };
            for (int m = 0; m < materialNames.Length; ++m)
            {
                for (int k = 0; k < integrators.Length; ++k)
                {
                    string integratorName = integrators[k].Split('"')[1];
                    string only = Environment.GetEnvironmentVariable("OSL_TEST_INTEGRATORS");
                    if (only != null && Array.IndexOf(only.Split(','), integratorName) < 0)
                    {
                        continue;
                    }
                    string scene = OslRenderTools.Scene(oslMaterials[m], OslRenderTools.Sphere, "", xres, yres).Replace("Integrator \"path\" \"integer maxdepth\" 5", integrators[k]);
                    string reference = OslRenderTools.Scene(pbrtMaterials[m], OslRenderTools.Sphere, "", xres, yres).Replace("Integrator \"path\" \"integer maxdepth\" 5", integrators[k]);
                    string name = materialNames[m] + "_" + integratorName;
                    OslRender o = OslRenderTools.Render(name + "_osl", scene, spp, seed);
                    OslRender p = OslRenderTools.Render(name + "_pbrt", reference, spp, seed);
                    if (o == null || p == null)
                    {
                        continue;
                    }
                    double mad = OslRender.MeanAbsDiff(o, p);
                    double[] mo = o.ChannelMeans(), mp = p.ChannelMeans();
                    string p1 = OslRenderTools.SaveLabelled(o, name + "_osl", "OSL " + materialNames[m].ToUpperInvariant() + " " + integratorName.ToUpperInvariant(), spp, seed);
                    string p2 = OslRenderTools.SaveLabelled(p, name + "_pbrt", "PBRT " + materialNames[m].ToUpperInvariant() + " " + integratorName.ToUpperInvariant(), spp, seed);
                    Console.WriteLine("[OslPbrt] " + name + ": mean abs diff " + mad.ToString("F5", CultureInfo.InvariantCulture) + ", OSL means " + OslRenderTools.F(mo) +
                                      ", pbrt means " + OslRenderTools.F(mp) + "\n    " + p1 + "\n    " + p2);
                    for (int c = 0; c < 3; ++c)
                    {
                        if (Math.Abs(mo[c] - mp[c]) / Math.Max(1e-3, mp[c]) > 0.05)
                        {
                            ADD_FAILURE(name + ": channel " + c + " mean " + mo[c] + " (OSL) vs " + mp[c] + " (pbrt)");
                            break;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Layered and mixed closures against independent references built with pbrt's own BxDFs
        // ------------------------------------------------------------------------------------------------------------------------------

        private static double DirectionalAlbedo(BxDF b, Vector3f wo, BxDFReflTransFlags flags, int n)
        {
            return DirectionalAlbedo(b, wo, flags, n, TransportMode.Radiance);
        }

        private static double DirectionalAlbedo(BxDF b, Vector3f wo, BxDFReflTransFlags flags, int n, TransportMode mode)
        {
            RNG rng = new RNG();
            double sum = 0;
            for (int i = 0; i < n; ++i)
            {
                pstd.optional<BSDFSample> s = b.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), mode,
                                                         flags);
                if (s.has_value() && s.value().pdf > 0)
                {
                    sum += s.value().f.Average() * AbsCosTheta(s.value().wi) / s.value().pdf;
                }
            }
            return sum / n;
        }

        private static readonly Vector3f[] testWos = new Vector3f[]
        {
            new Vector3f(0, 0, 1), new Vector3f(0.2f, 0.1f, 1), new Vector3f(0.7f, 0.2f, 0.7f), new Vector3f(-0.9f, 0.3f, 0.4f)
        };

        [TEST("OslPbrt", "LayeredIsTopPlusAttenuatedBase")]
        public static void LayeredIsTopPlusAttenuatedBase()
        {
            // layer(dielectric_bsdf(roughness 0.15 -> alpha 0.0225), oren_nayar_diffuse_bsdf(Cs, 0)) is, for MaterialX, f_top + (1 - albedo_top(wo)) * f_base
            // with pbrt's DielectricBxDF (its reflection part) and DiffuseBxDF as the references; the albedo is integrated by Monte Carlo
            const float cs = 0.5f;
            DielectricBxDF top = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.0225f, 0.0225f));
            DiffuseBxDF baseBxdf = new DiffuseBxDF(new SampledSpectrum(cs));
            RNG rng = new RNG();
            double worst = 0;
            foreach (Vector3f wo0 in testWos)
            {
                Vector3f wo = Normalize(wo0);
                shadeWo = wo;
                OSLShadeResult r = ShadeMaterial("layered", P("rgb", "Cs", cs, cs, cs), P("float", "roughness", 0.15f));
                shadeWo = Normalize(new Vector3f(0.2f, 0.1f, 1));
                ASSERT_TRUE(r.bxdf != null);
                // the albedo of the reflection part of the top at wo
                double albedo = DirectionalAlbedo(top, wo, BxDFReflTransFlags.Reflection, 200000);
                for (int i = 0; i < 40; ++i)
                {
                    Vector3f wi = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (wo.z < 0)
                    {
                        wi.z = -wi.z;
                    }
                    if (Math.Abs(wi.z) < 0.1f)
                    {
                        continue;
                    }
                    double expected = top.f(wo, wi, TransportMode.Radiance).Average() + (1 - albedo) * baseBxdf.f(wo, wi, TransportMode.Radiance).Average();
                    double actual = r.bxdf.f(wo, wi, TransportMode.Radiance).Average();
                    worst = Math.Max(worst, Math.Abs(actual - expected) / Math.Max(0.05, expected));
                }
            }
            Console.WriteLine("[OslPbrt] layered f vs f_top + (1 - E_top) f_base (pbrt DielectricBxDF, DiffuseBxDF): worst relative error " +
                              worst.ToString("F4", CultureInfo.InvariantCulture));
            EXPECT_LT(worst, 0.03);
        }

        [TEST("OslPbrt", "LayeredFurnaceAndCoatedDiffuse")]
        public static void LayeredFurnaceAndCoatedDiffuse()
        {
            // White furnace: with a white base the layered closure reflects everything (the top reflects E, the base the rest: albedo 1);
            // with a gray base of 0.5 the albedo is E + (1 - E) * 0.5. pbrt's CoatedDiffuseBxDF (which refracts into the coat and has interreflections)
            // is printed for comparison: it is a different model (MaterialX layer does not refract), it is not an equality
            foreach (float cs in new float[] { 1f, 0.5f })
            {
                foreach (Vector3f wo0 in testWos)
                {
                    Vector3f wo = Normalize(wo0);
                    shadeWo = wo;
                    OSLShadeResult r = ShadeMaterial("layered", P("rgb", "Cs", cs, cs, cs), P("float", "roughness", 0.15f));
                    shadeWo = Normalize(new Vector3f(0.2f, 0.1f, 1));
                    double albedo = DirectionalAlbedo(r.bxdf, wo, BxDFReflTransFlags.All, 200000);
                    DielectricBxDF topRef = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.0225f, 0.0225f));
                    double e = DirectionalAlbedo(topRef, wo, BxDFReflTransFlags.Reflection, 100000);
                    double expected = e + (1 - e) * cs;
                    CoatedDiffuseBxDF coated = new CoatedDiffuseBxDF(topRef, new DiffuseBxDF(new SampledSpectrum(cs)), 0.01f, new SampledSpectrum(0f), 0f, 10, 1);
                    double coatedAlbedo = DirectionalAlbedo(coated, wo, BxDFReflTransFlags.All, 40000);
                    Console.WriteLine("[OslPbrt] layered furnace Cs=" + cs + " wo.z=" + wo.z.ToString("F2", CultureInfo.InvariantCulture) + ": albedo " +
                                      albedo.ToString("F4", CultureInfo.InvariantCulture) + ", expected E + (1-E) Cs = " + expected.ToString("F4", CultureInfo.InvariantCulture) +
                                      " (E = " + e.ToString("F4", CultureInfo.InvariantCulture) + "), pbrt CoatedDiffuse " + coatedAlbedo.ToString("F4", CultureInfo.InvariantCulture));
                    EXPECT_NEAR(albedo, expected, 0.02);
                    EXPECT_LT(albedo, 1.02);
                }
            }
        }

        [TEST("OslPbrt", "MixedIsTheWeightedSumOfPbrtBxDFs")]
        public static void MixedIsTheWeightedSumOfPbrtBxDFs()
        {
            // 0.5 * Cs * diffuse + 0.5 * microfacet("ggx", alpha 0.2, eta 1.5, reflection only) is 0.5 * DiffuseBxDF + 0.5 * the reflection part of pbrt's DielectricBxDF
            const float cs = 0.6f;
            DielectricBxDF glass = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.2f, 0.2f));
            DiffuseBxDF diffuse = new DiffuseBxDF(new SampledSpectrum(cs));
            OSLShadeResult r = ShadeMaterial("mixed", P("rgb", "Cs", cs, cs, cs), P("float", "mixw", 0.5f));
            ASSERT_TRUE(r.bxdf != null);
            RNG rng = new RNG();
            double worst = 0;
            Vector3f wo = shadeWo;
            for (int i = 0; i < 60; ++i)
            {
                Vector3f wi = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (wi.z < 0.1f)
                {
                    continue;
                }
                double expected = 0.5 * diffuse.f(wo, wi, TransportMode.Radiance).Average() + 0.5 * glass.f(wo, wi, TransportMode.Radiance).Average();
                double actual = r.bxdf.f(wo, wi, TransportMode.Radiance).Average();
                worst = Math.Max(worst, Math.Abs(actual - expected) / Math.Max(0.05, expected));
            }
            Console.WriteLine("[OslPbrt] mixed f vs 0.5 DiffuseBxDF + 0.5 DielectricBxDF: worst relative error " + worst.ToString("F5", CultureInfo.InvariantCulture));
            EXPECT_LT(worst, 1e-3);
            // furnace: the diffuse (Cs = 1) and the whole dielectric (microfacet reflection + refraction): equal to 0.5 + 0.5 * the albedo of pbrt's DielectricBxDF
            // (importance mode: no radiance scaling), which is below 1 only by the energy that single scattering microfacet models lose
            OSLShadeResult white = ShadeMaterial("mixed_glass", P("rgb", "Cs", 1f, 1f, 1f), P("float", "mixw", 0.5f));
            foreach (Vector3f wo0 in testWos)
            {
                Vector3f w = Normalize(wo0);
                double albedo = DirectionalAlbedo(white.bxdf, w, BxDFReflTransFlags.All, 200000, TransportMode.Importance);
                double expected = 0.5 + 0.5 * DirectionalAlbedo(glass, w, BxDFReflTransFlags.All, 200000, TransportMode.Importance);
                Console.WriteLine("[OslPbrt] mixed furnace wo.z=" + w.z.ToString("F2", CultureInfo.InvariantCulture) + ": albedo " + albedo.ToString("F4", CultureInfo.InvariantCulture) +
                                  ", 0.5 + 0.5 * pbrt DielectricBxDF " + expected.ToString("F4", CultureInfo.InvariantCulture));
                EXPECT_NEAR(albedo, expected, 0.01);
                EXPECT_LT(albedo, 1.01);
            }
        }

        [TEST("OslPbrt", "RenderMixedVsMixMaterial")]
        public static void RenderMixedVsMixMaterial()
        {
            ResetScene();
            // 0.5 diffuse + 0.5 rough glass as one OSL closure sum, and pbrt's MixMaterial of a diffuse and a dielectric material (stochastic selection: the same mean)
            string extra = "MakeNamedMaterial \"mixa\" \"string type\" \"diffuse\" \"rgb reflectance\" [0.8 0.2 0.2]\n" +
                           "MakeNamedMaterial \"mixb\" \"string type\" \"dielectric\" \"float eta\" 1.5 \"float roughness\" 0.2 \"bool remaproughness\" false\n";
            OslRenderTools.Compare("mixed_vs_mixmaterial", "Material \"osl\" \"string shader\" \"mixed_glass\" \"rgb Cs\" [0.8 0.2 0.2] \"float mixw\" 0.5",
                                   "Material \"mix\" \"string materials\" [\"mixa\" \"mixb\"] \"float amount\" 0.5", OslRenderTools.Sphere, extra, 256, 17,
                                   "OSL 0.5 DIFFUSE + 0.5 GLASS VS PBRT MIXMATERIAL", 0.03, 0.03);
        }

        [TEST("OslPbrt", "RenderSpectralModes")]
        public static void RenderSpectralModes()
        {
            ResetScene();
            const int spp = 32, seed = 2, xres = 96, yres = 72;
            string scene = OslRenderTools.Scene("Material \"osl\" \"string shader\" \"matte\" \"rgb Cs\" [0.8 0.3 0.2]", OslRenderTools.Sphere, "", xres, yres);
            OslRender up = OslRenderTools.Render("spectral_upsample", scene, spp, seed);
            OslRender gray = OslRenderTools.Render("spectral_gray", "Option \"oslspectral\" \"gray\"\n" + scene, spp, seed);
            // an Option has to come before WorldBegin, so put it first in the file: LookAt after Option is fine
            ASSERT_TRUE(up != null && gray != null);
            // gray mode: the sphere is a neutral gray (equal channels); upsample mode: a red sphere
            int o = 3 * ((yres / 2) * xres + xres / 2);
            EXPECT_GT(up.rgb[o], up.rgb[o + 2] * 1.5);
            double r = gray.rgb[o], g = gray.rgb[o + 1], b = gray.rgb[o + 2];
            EXPECT_LT(Math.Abs(r - b) / Math.Max(r, 1e-3), 0.2);
            EXPECT_LT(Math.Abs(r - g) / Math.Max(r, 1e-3), 0.2);
            OSLSystem.spectralMode = OSLSpectralMode.Upsample;
        }
    }
}
