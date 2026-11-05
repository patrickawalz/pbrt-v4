// (not in pbrt-v4) Tests of the OSL volume shaders and the "osl" medium (osl_volume.cs): the coefficients of the volume closures, the equality of a
// constant shader with pbrt's "homogeneous" medium, the phase function, a heterogeneous shader against pbrt's GridMedium sampled from the same function
// (transmittance with z-scores), the soundness of the majorants and the violation counter, emission, a white furnace, nested media and a camera in a
// medium, the spectral modes, threads, and renders with the command line of pbrt that are compared with pbrt's own media.
//
//   dotnet pbrt_test.dll --gtest_filter='OslVolume*'
//
// The renders are written with their mode, spp and seed to /tmp/osl_volume_renders.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OSL;
using testing;

namespace pbrt
{
    // ---------------------------------------------------------------------------------------------------------------------------------
    // Shaders of the tests
    // ---------------------------------------------------------------------------------------------------------------------------------
    public static class OslVolumeShaders
    {
        public const string OutputDir = "/tmp/osl_volume_renders";

        private const string Header = "#include \"pbrt_volume.h\"\n";

        public static readonly Dictionary<string, string> sources = new Dictionary<string, string>
        {
            // a Gaussian blob in the unit cube of the medium space
            { "vblob", Header +
              "volume vblob(float amp = 6, float width = 0.18, color sigma_a = color(0.3, 0.15, 0.05), color sigma_s = color(0.5, 0.7, 0.9), float g = 0.4) {\n" +
              "  point Pm = transform(\"shader\", P); vector q = Pm - point(0.5, 0.5, 0.5);\n" +
              "  float d = amp * exp(-dot(q, q) / (2 * width * width));\n" +
              "  Ci = absorption() * (d * sigma_a) + henyey_greenstein(g) * (d * sigma_s); }\n" },
            // a linear ramp along x
            { "vramp", Header +
              "volume vramp(float amp = 3, color sigma_a = color(0.2), color sigma_s = color(0.6), float g = 0.0) {\n" +
              "  point Pm = transform(\"shader\", P); float d = amp * Pm[0];\n" +
              "  Ci = absorption() * (d * sigma_a) + henyey_greenstein(g) * (d * sigma_s); }\n" },
            // a smooth function with the known bound amp * (0.3 + 1) of the extinction (gray colors)
            { "vtrig", Header +
              "volume vtrig(float amp = 3, float fx = 5, float fy = 7, float fz = 3, float px = 0, float py = 1, float pz = 2) {\n" +
              "  point Pm = transform(\"shader\", P);\n" +
              "  float d = amp * (0.5 + 0.5 * sin(fx * Pm[0] + px) * cos(fy * Pm[1] + py) * sin(fz * Pm[2] + pz));\n" +
              "  Ci = absorption() * (d * 0.3) + isotropic() * d; }\n" },
            // constant emission with absorption
            { "vemit", Header +
              "volume vemit(color sigma_a = color(0.8), color Le = color(1, 0.6, 0.2)) { Ci = absorption() * sigma_a + emission() * Le; }\n" },
            // emission that varies in space: absorption and emission of a blob, no scattering
            { "vglow", Header +
              "volume vglow(float amp = 5, float width = 0.2, color Le = color(1, 0.5, 0.2), float emit = 3) {\n" +
              "  point Pm = transform(\"shader\", P); vector q = Pm - point(0.5, 0.5, 0.5);\n" +
              "  float d = amp * exp(-dot(q, q) / (2 * width * width));\n" +
              "  Ci = absorption() * d + emission() * (Le * emit * (0.5 + Pm[0])); }\n" },
            // a medium whose coefficients do not depend on the position but are computed by a longer program
            { "vfolded", Header +
              "volume vfolded(float a = 0.3, float b = 0.5) { float s = 0; for (int i = 0; i < 4; ++i) s += a * (i + 1) * 0.1; color c = color(s, 2 * s, 3 * s);\n" +
              "  Ci = absorption() * (0.1 * c) + isotropic() * (b * c); }\n" },
            // a shader that reads the position only in a branch it never takes (the optimizer removes it)
            { "vdead", Header +
              "volume vdead(float k = 0.0) { float d = 1.0; if (k > 1.0) d = P[0]; Ci = absorption() * (0.3 * d) + isotropic() * (0.5 * d); }\n" },
            // one closure of each kind, selected by mode
            { "vclosures", Header +
              "volume vclosures(int mode = 0) {\n" +
              "  if (mode == 0) Ci = anisotropic_vdf(color(0.5, 0.8, 1.0), color(2, 3, 4), 0.3);\n" +
              "  else if (mode == 1) Ci = medium_vdf(color(0.9), 2.0, color(0.5, 0.25, 1.0), -0.2, 1.33, 1);\n" +
              "  else if (mode == 2) Ci = 2.5 * medium_vdf(color(0.9), 2.0, color(0.5, 0.25, 1.0), -0.2, 1.33, 1);\n" +
              "  else if (mode == 3) Ci = absorption() * color(0.1, 0.2, 0.3) + henyey_greenstein(0.5) * color(0.4) + isotropic() * color(0.6);\n" +
              "  else if (mode == 4) Ci = transparent() + medium_vdf(color(0.9), 2.0, color(0.5, 0.25, 1.0), -0.2, 1.33, 1);\n" +
              "  else if (mode == 5) Ci = emission() * color(1, 2, 3) + absorption() * color(0.5);\n" +
              "  else if (mode == 6) Ci = medium_vdf(color(0), 1.0, color(0), 0.0, 1.5, 2);\n" +
              "  else if (mode == 7) Ci = henyey_greenstein(0.5) * color(0.2, 0.4, 0.6) + henyey_greenstein(-0.5) * color(0.6, 0.4, 0.2);\n" +
              "  else Ci = henyey_greenstein(0.7) * color(1, 1, 1) + henyey_greenstein(-0.3) * color(2, 2, 2) + isotropic() * color(1, 1, 1); }\n" },
            // a shader that reads I (the direction of the ray) and the time
            { "vprobe", Header +
              "volume vprobe(int mode = 0) { color c = 0; if (mode == 0) c = P; else if (mode == 1) c = I; else if (mode == 2) c = time; " +
              "else if (mode == 3) c = -N; else if (mode == 4) c = transform(\"shader\", P); else if (mode == 5) c = transform(\"common\", \"shader\", point(0));\n" +
              "  Ci = isotropic() * c; }\n" },
            // a shader with noise functions
            { "vnoise", Header +
              "volume vnoise(int kind = 0) { point Pm = transform(\"shader\", P); float n = 0;\n" +
              "  if (kind == 0) n = noise(Pm * 6); else if (kind == 1) n = 0.5 + 0.5 * snoise(Pm * 6); else if (kind == 2) n = 0.5 + 0.5 * pnoise(Pm * 6, point(6)); " +
              "else if (kind == 3) n = cellnoise(Pm * 6); else if (kind == 4) { float gs = noise(\"gabor\", Pm * 6); n = max(0.0, 0.5 + 0.5 * gs); }\n" +
              "  Ci = absorption() * (0.5 * n) + isotropic() * (2.0 * n); }\n" }
        };

        private static readonly object mutex = new object();

        public static string RepoShaders()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; ++i)
            {
                string candidate = Path.Combine(dir, "scenes", "osl_volume");
                if (File.Exists(Path.Combine(candidate, "pbrt_volume.h")))
                {
                    return candidate;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return "/home/claude/pbrt-v4/scenes/osl_volume";
        }

        public static string Directory()
        {
            string dir = Path.Combine(OutputDir, "scenes");
            lock (mutex)
            {
                System.IO.Directory.CreateDirectory(dir);
                foreach (string file in System.IO.Directory.GetFiles(RepoShaders()))
                {
                    string target = Path.Combine(dir, Path.GetFileName(file));
                    if (!File.Exists(target) || File.ReadAllText(target) != File.ReadAllText(file))
                    {
                        File.Copy(file, target, true);
                    }
                }
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

    // The estimate of a transmittance and its statistics
    public sealed class OslTransmittanceStats
    {
        public double[] mean = new double[NSpectrumSamples];
        public double[] standardError = new double[NSpectrumSamples];
    }

    public static unsafe class OslVolumeTests
    {
        private static void ResetScene()
        {
            OSLScene.Reset();
            OSLSystem.spectralMode = OSLSpectralMode.Upsample;
            OSLSystem.emissionIlluminant = true;
            OSLSystem.AddSearchPath(OslVolumeShaders.Directory());
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

        private static ParsedParameter PS(string name, string value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "string";
            p.name = name;
            p.AddString(value);
            return p;
        }

        private static ParsedParameter PB(string name, bool value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "bool";
            p.name = name;
            p.AddBool(value);
            return p;
        }

        private static ParameterDictionary Dict(params ParsedParameter[] ps)
        {
            return new ParameterDictionary(new List<ParsedParameter>(ps), RGBColorSpace.sRGB);
        }

        private static OSLMedium MakeOsl(string shader, Transform renderFromMedium, params ParsedParameter[] ps)
        {
            List<ParsedParameter> list = new List<ParsedParameter>();
            list.Add(PS("shader", shader));
            list.AddRange(ps);
            Medium m = Medium.Create("osl", new ParameterDictionary(list, RGBColorSpace.sRGB), renderFromMedium, new FileLoc(), new Allocator());
            return (OSLMedium)m;
        }

        private static Medium MakePbrt(string type, Transform renderFromMedium, params ParsedParameter[] ps)
        {
            List<ParsedParameter> list = new List<ParsedParameter>(ps);
            return Medium.Create(type, new ParameterDictionary(list, RGBColorSpace.sRGB), renderFromMedium, new FileLoc(), new Allocator());
        }

        private static void ExpectSpectrumNear(SampledSpectrum a, SampledSpectrum b, double relTol, string what)
        {
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double scale = Math.Max(1e-6, Math.Max(Math.Abs(a[i]), Math.Abs(b[i])));
                if (Math.Abs(a[i] - b[i]) > relTol * scale)
                {
                    ADD_FAILURE(what + ": wavelength " + i + ": " + a[i].ToString("G9", CultureInfo.InvariantCulture) + " vs " +
                                b[i].ToString("G9", CultureInfo.InvariantCulture));
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Estimates of the transmittance along a ray (ratio tracking with the hero wavelength's collisions, as the shadow rays of volpath)
        // ------------------------------------------------------------------------------------------------------------------------------

        public static OslTransmittanceStats EstimateTransmittance(Medium medium, Ray ray, Float tMax, SampledWavelengths lambda, int n, ulong seed)
        {
            double[] sum = new double[NSpectrumSamples], sum2 = new double[NSpectrumSamples];
            for (int i = 0; i < n; ++i)
            {
                RNG rng = new RNG(seed, (ulong)i * 7919UL);
                SampledSpectrum T_ray = new SampledSpectrum(1f);
                Float u = rng.Uniform<Float>();
                SampledSpectrum T_maj = SampleT_maj(ray, medium, tMax, u, ref rng, lambda,
                                                    (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                                                    {
                                                        SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                                                        Float pdf = T_maj_[0] * sigma_maj[0];
                                                        T_ray *= T_maj_ * sigma_n / pdf;
                                                        return (bool)T_ray;
                                                    });
                T_ray *= T_maj / T_maj[0];
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    sum[c] += T_ray[c];
                    sum2[c] += (double)T_ray[c] * T_ray[c];
                }
            }
            OslTransmittanceStats s = new OslTransmittanceStats();
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                s.mean[c] = sum[c] / n;
                double var = Math.Max(0, sum2[c] / n - s.mean[c] * s.mean[c]) * n / (n - 1);
                s.standardError[c] = Math.Sqrt(var / n);
            }
            return s;
        }

        // The optical depth of a ray by quadrature of the extinction that SamplePoint reports
        public static double[] OpticalDepth(Medium medium, Ray ray, Float tMax, SampledWavelengths lambda, int steps)
        {
            double[] tau = new double[NSpectrumSamples];
            double h = tMax / steps;
            for (int i = 0; i < steps; ++i)
            {
                MediumProperties mp = medium.SamplePoint(ray.Apply((Float)((i + 0.5) * h)), lambda);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    tau[c] += (mp.sigma_a[c] + mp.sigma_s[c]) * h;
                }
            }
            return tau;
        }

        // A random ray that passes through the box [0.2, 0.8]^3 coming from a sphere around it
        private static Ray RandomRay(ref RNG rng, out Float tMax)
        {
            Point3f target = new Point3f(0.2f + 0.6f * rng.Uniform<Float>(), 0.2f + 0.6f * rng.Uniform<Float>(), 0.2f + 0.6f * rng.Uniform<Float>());
            Vector3f dir = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
            Point3f origin = new Point3f(0.5f, 0.5f, 0.5f) + 1.6f * dir;
            Vector3f d = Normalize(target - origin);
            tMax = 3.2f;
            return new Ray(origin, d);
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Closures
        // ------------------------------------------------------------------------------------------------------------------------------

        private static OSLVolumeValues EvaluateClosures(int mode)
        {
            OSLMedium m = MakeOsl("vclosures", new Transform(), PI("mode", mode));
            OSLVolumeValues v;
            m.Evaluate(new Point3f(0.3f, 0.4f, 0.5f), new Vector3f(0, 0, 1), 0, out v);
            return v;
        }

        private static void ExpectColor(Color3 c, double x, double y, double z, string what)
        {
            EXPECT_NEAR(c.x, x, 1e-5 * Math.Max(1, Math.Abs(x)), what + ".x");
            EXPECT_NEAR(c.y, y, 1e-5 * Math.Max(1, Math.Abs(y)), what + ".y");
            EXPECT_NEAR(c.z, z, 1e-5 * Math.Max(1, Math.Abs(z)), what + ".z");
        }

        [TEST("OslVolume", "ClosureCoefficients")]
        public static void ClosureCoefficients()
        {
            ResetScene();
            // anisotropic_vdf: sigma_t = extinction, sigma_s = albedo * sigma_t
            OSLVolumeValues v = EvaluateClosures(0);
            ExpectColor(v.sigma_s, 1, 2.4, 4, "anisotropic sigma_s");
            ExpectColor(v.sigma_a, 1, 0.6, 0, "anisotropic sigma_a");
            EXPECT_EQ(v.nLobes, 1);
            EXPECT_NEAR(v.lobeG[0], 0.3, 1e-6);
            EXPECT_NEAR(v.lobeWeight[0], (1 + 2.4 + 4) / 3.0, 1e-5);

            // medium_vdf: sigma_t = -ln(transmission_color) / transmission_depth (testrender), sigma_s = albedo * sigma_t
            v = EvaluateClosures(1);
            double t0 = -Math.Log(0.5) / 2, t1 = -Math.Log(0.25) / 2;
            ExpectColor(v.sigma_s, 0.9 * t0, 0.9 * t1, 0, "medium_vdf sigma_s");
            ExpectColor(v.sigma_a, 0.1 * t0, 0.1 * t1, 0, "medium_vdf sigma_a");
            EXPECT_NEAR(v.lobeG[0], -0.2, 1e-6);

            // the weight of the closure scales the coefficients
            v = EvaluateClosures(2);
            ExpectColor(v.sigma_s, 2.5 * 0.9 * t0, 2.5 * 0.9 * t1, 0, "weighted medium_vdf sigma_s");
            ExpectColor(v.sigma_a, 2.5 * 0.1 * t0, 2.5 * 0.1 * t1, 0, "weighted medium_vdf sigma_a");

            // henyey_greenstein and isotropic: the weight is the scattering coefficient; absorption: the absorption coefficient; two lobes
            v = EvaluateClosures(3);
            ExpectColor(v.sigma_a, 0.1, 0.2, 0.3, "absorption");
            ExpectColor(v.sigma_s, 1, 1, 1, "scattering");
            EXPECT_EQ(v.nLobes, 2);
            EXPECT_NEAR(v.lobeG[0], 0.5, 1e-6);
            EXPECT_NEAR(v.lobeWeight[0], 0.4, 1e-6);
            EXPECT_NEAR(v.lobeG[1], 0.0, 1e-6);
            EXPECT_NEAR(v.lobeWeight[1], 0.6, 1e-6);

            // transparent() next to the vdf (the testsuite's medium shader) changes nothing
            OSLVolumeValues same = EvaluateClosures(4);
            EXPECT_EQ(same.sigma_s.x, EvaluateClosures(1).sigma_s.x);
            EXPECT_EQ(same.sigma_a.y, EvaluateClosures(1).sigma_a.y);

            // emission
            v = EvaluateClosures(5);
            EXPECT_TRUE(v.hasEmission);
            ExpectColor(v.emission, 1, 2, 3, "emission");
            ExpectColor(v.sigma_a, 0.5, 0.5, 0.5, "emitter absorption");
            EXPECT_EQ(v.nLobes, 0);

            // a vacuum (black albedo and transmission color) has no coefficients
            v = EvaluateClosures(6);
            ExpectColor(v.sigma_a, 0, 0, 0, "vacuum sigma_a");
            ExpectColor(v.sigma_s, 0, 0, 0, "vacuum sigma_s");

            // two lobes of different color, three lobes with their weights
            v = EvaluateClosures(7);
            EXPECT_EQ(v.nLobes, 2);
            EXPECT_NEAR(v.lobeWeight[0], 0.4, 1e-6);
            EXPECT_NEAR(v.lobeWeight[1], 0.4, 1e-6);
            ExpectColor(v.sigma_s, 0.8, 0.8, 0.8, "two lobes sigma_s");
            v = EvaluateClosures(8);
            EXPECT_EQ(v.nLobes, 3);
            ExpectColor(v.sigma_s, 4, 4, 4, "three lobes sigma_s");
        }

        [TEST("OslVolume", "ShaderGlobalsOfAVolumePoint")]
        public static void ShaderGlobalsOfAVolumePoint()
        {
            ResetScene();
            // render space is world space translated: render = world - (1, 2, 3); the medium space is the CTM at MakeNamedMedium (a scale by 2)
            OSLScene.SetRenderSpace(Translate(new Vector3f(-1, -2, -3)));
            Transform renderFromMedium = Translate(new Vector3f(-1, -2, -3)) * Scale(2, 2, 2);
            Func<int, Color3> probe = (int mode) =>
            {
                OSLMedium m = MakeOsl("vprobe", renderFromMedium, PI("mode", mode));
                OSLVolumeValues v;
                m.Evaluate(new Point3f(0.5f, 0.25f, 1f), new Vector3f(0, 0, 2), 0.375f, out v);
                return new Color3(v.sigma_s.x * 1f, v.sigma_s.y, v.sigma_s.z);
            };
            // the shader sees world space: P = render + (1, 2, 3); the closure is scaled by the color so sigma_s is the color
            Color3 p = probe(0);
            ExpectColor(p, 1.5, 2.25, 4, "P in world space");
            // I is the direction of the ray in world space
            ExpectColor(probe(1), 0, 0, 1, "I");
            ExpectColor(probe(2), 0.375, 0.375, 0.375, "time");
            // there is no surface: the normal faces the viewer (-N is read, the weight of a closure is not negative)
            ExpectColor(probe(3), 0, 0, 1, "-N");
            // the medium space is the CTM of the medium: render (0.5, 0.25, 1) -> medium ((0.5+1)/2, (0.25+2)/2, (1+3)/2)
            ExpectColor(probe(4), 0.75, 1.125, 2, "shader space");
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Homogeneous
        // ------------------------------------------------------------------------------------------------------------------------------

        [TEST("OslVolume", "DetectsHomogeneousShaders")]
        public static void DetectsHomogeneousShaders()
        {
            ResetScene();
            EXPECT_TRUE(MakeOsl("fog", new Transform()).IsHomogeneous());
            EXPECT_TRUE(MakeOsl("vfolded", new Transform()).IsHomogeneous());
            // the read of P is behind a branch that the optimizer removes
            EXPECT_TRUE(MakeOsl("vdead", new Transform()).IsHomogeneous());
            EXPECT_FALSE(MakeOsl("vdead", new Transform(), P("float", "k", 2)).IsHomogeneous());
            EXPECT_FALSE(MakeOsl("vblob", new Transform()).IsHomogeneous());
            EXPECT_FALSE(MakeOsl("vramp", new Transform()).IsHomogeneous());
            EXPECT_FALSE(MakeOsl("vprobe", new Transform(), PI("mode", 1)).IsHomogeneous());
            EXPECT_FALSE(MakeOsl("vprobe", new Transform(), PI("mode", 2)).IsHomogeneous());
            EXPECT_FALSE(MakeOsl("vnoise", new Transform()).IsHomogeneous());
            // the analysis can be turned off: the grid path is used for a constant shader
            OSLMedium forced = MakeOsl("fog", new Transform(), PB("detecthomogeneous", false));
            EXPECT_FALSE(forced.IsHomogeneous());
        }

        [TEST("OslVolume", "ConstantShaderEqualsHomogeneousMedium")]
        public static void ConstantShaderEqualsHomogeneousMedium()
        {
            ResetScene();
            for (int variant = 0; variant < 2; ++variant)
            {
                ParsedParameter sa = variant == 0 ? P("rgb", "sigma_a", 0.2f, 0.1f, 0.05f) : P("rgb", "sigma_a", 0.15f, 0.15f, 0.15f);
                ParsedParameter ss = variant == 0 ? P("rgb", "sigma_s", 0.3f, 0.6f, 0.9f) : P("rgb", "sigma_s", 0.5f, 0.5f, 0.5f);
                OSLMedium osl = MakeOsl("fog", new Transform(), sa, ss, P("float", "g", 0.3f), P("float", "scale", 2f));
                Medium reference = MakePbrt("homogeneous", new Transform(), sa, ss, P("float", "g", 0.3f), P("float", "scale", 2f));
                EXPECT_TRUE(osl.IsHomogeneous());
                EXPECT_FALSE(osl.IsEmissive());
                RNG rng = new RNG(3, 5);
                for (int i = 0; i < 20; ++i)
                {
                    SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                    Point3f p = new Point3f(rng.Uniform<Float>() * 4 - 2, rng.Uniform<Float>() * 4 - 2, rng.Uniform<Float>() * 4 - 2);
                    MediumProperties a = osl.SamplePoint(p, lambda), b = reference.SamplePoint(p, lambda);
                    ExpectSpectrumNear(a.sigma_a, b.sigma_a, 1e-6, "sigma_a");
                    ExpectSpectrumNear(a.sigma_s, b.sigma_s, 1e-6, "sigma_s");
                    ExpectSpectrumNear(a.Le, b.Le, 1e-6, "Le");
                    // the segments of the majorant iterator of a ray
                    Ray ray = new Ray(new Point3f(0, 0, 0), Normalize(new Vector3f(1, 0.3f, -0.2f)));
                    RayMajorantIterator ia = osl.SampleRay(ray, 5f, lambda, new ScratchBuffer()), ib = reference.SampleRay(ray, 5f, lambda, new ScratchBuffer());
                    pstd.optional<RayMajorantSegment> sa0 = ia.Next(), sb0 = ib.Next();
                    EXPECT_TRUE(sa0.has_value() && sb0.has_value());
                    EXPECT_FLOAT_EQ(sa0.value().tMin, sb0.value().tMin);
                    EXPECT_FLOAT_EQ(sa0.value().tMax, sb0.value().tMax);
                    ExpectSpectrumNear(sa0.value().sigma_maj, sb0.value().sigma_maj, 1e-6, "sigma_maj");
                    EXPECT_FALSE(ia.Next().has_value());
                    // the phase function
                    for (int k = 0; k < 5; ++k)
                    {
                        Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        EXPECT_FLOAT_EQ(a.phase.p(wo, wi), b.phase.p(wo, wi));
                    }
                    // the transmittance in closed form (the analytic estimator) and by Beer's law
                    SampledSpectrum Ta, Tb;
                    Ray along = new Ray(new Point3f(0, 0, 0), new Vector3f(0, 0, 1));
                    AnalyticTransmittanceEstimator analytic = new AnalyticTransmittanceEstimator();
                    EXPECT_TRUE(analytic.TryClosedForm(along, osl, 1.7f, lambda, out Ta));
                    EXPECT_TRUE(analytic.TryClosedForm(along, reference, 1.7f, lambda, out Tb));
                    ExpectSpectrumNear(Ta, Tb, 1e-6, "closed form");
                    SampledSpectrum sigma_t = b.sigma_a + b.sigma_s;
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        EXPECT_NEAR(Ta[c], Math.Exp(-1.7 * sigma_t[c]), 1e-4);
                    }
                }

                // ratio tracking against Beer's law: z-scores of the estimate of each wavelength
                SampledWavelengths lam = SampledWavelengths.SampleVisible(0.31f);
                Ray ray2 = new Ray(new Point3f(0, 0, 0), new Vector3f(0, 0, 1));
                MediumProperties mp = reference.SamplePoint(new Point3f(0, 0, 0), lam);
                OslTransmittanceStats s = EstimateTransmittance(osl, ray2, 1.5f, lam, 40000, 77);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    double truth = Math.Exp(-1.5 * (mp.sigma_a[c] + mp.sigma_s[c]));
                    double z = (s.mean[c] - truth) / s.standardError[c];
                    Console.WriteLine("[OslVolume] constant variant " + variant + " wavelength " + c + ": T " + s.mean[c].ToString("F5", CultureInfo.InvariantCulture) +
                                      " +- " + s.standardError[c].ToString("F5", CultureInfo.InvariantCulture) + ", exp(-sigma_t d) " +
                                      truth.ToString("F5", CultureInfo.InvariantCulture) + ", z " + z.ToString("F2", CultureInfo.InvariantCulture));
                    EXPECT_LT(Math.Abs(z), 4.5);
                }
            }
        }

        [TEST("OslVolume", "BoundedConstantShader")]
        public static void BoundedConstantShader()
        {
            ResetScene();
            // a constant shader with bounds fills the box only
            Transform renderFromMedium = Translate(new Vector3f(1, 0, 0)) * Scale(2, 2, 2);
            OSLMedium osl = MakeOsl("fog", renderFromMedium, P("rgb", "sigma_a", 0.3f, 0.3f, 0.3f), P("rgb", "sigma_s", 0.2f, 0.2f, 0.2f),
                                    P("point3", "p0", 0f, 0f, 0f), P("point3", "p1", 1f, 1f, 1f));
            EXPECT_TRUE(osl.IsHomogeneous());
            EXPECT_TRUE(osl.HasBounds());
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.6f);
            EXPECT_TRUE((bool)osl.SamplePoint(new Point3f(2, 1, 1), lambda).sigma_a);
            EXPECT_FALSE((bool)osl.SamplePoint(new Point3f(0.5f, 1, 1), lambda).sigma_a);
            EXPECT_FALSE((bool)osl.SamplePoint(new Point3f(2, 3, 1), lambda).sigma_a);
            // the transmittance through the box (2 long along z) by ratio tracking against Beer's law
            Ray ray = new Ray(new Point3f(2, 1, -3), new Vector3f(0, 0, 1));
            OslTransmittanceStats s = EstimateTransmittance(osl, ray, 10f, lambda, 20000, 5);
            double truth = Math.Exp(-0.5 * 2);
            double z = (s.mean[0] - truth) / s.standardError[0];
            EXPECT_LT(Math.Abs(z), 4.5);
            // a ray that misses the box
            Ray miss = new Ray(new Point3f(0, 1, -3), new Vector3f(0, 0, 1));
            OslTransmittanceStats m = EstimateTransmittance(osl, miss, 10f, lambda, 100, 5);
            EXPECT_EQ(m.mean[0], 1.0);
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // The phase function
        // ------------------------------------------------------------------------------------------------------------------------------

        private static double ChiSquareCritical(int dof, double z)
        {
            double a = 2.0 / (9.0 * dof);
            return dof * Math.Pow(1 - a + z * Math.Sqrt(a), 3);
        }

        // The cosine of the angle to wo is binned; the expected count of each bin comes from the density of the function itself (integrated numerically)
        private static double PhaseChiSquare(PhaseFunction phase, Vector3f wo, int n, int bins, out double azimuthChi2)
        {
            RNG rng = new RNG(11, 13);
            int[] count = new int[bins];
            int[] azimuth = new int[16];
            Frame frame = Frame.FromZ(wo);
            for (int i = 0; i < n; ++i)
            {
                pstd.optional<PhaseFunctionSample> ps = phase.Sample_p(wo, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                ASSERT_TRUE(ps.has_value());
                Vector3f wi = ps.value().wi;
                double c = Dot(wo, wi);
                count[Math.Min(bins - 1, Math.Max(0, (int)((c + 1) / 2 * bins)))]++;
                Vector3f local = frame.ToLocal(wi);
                double phi = Math.Atan2(local.y, local.x) + Math.PI;
                azimuth[Math.Min(15, (int)(phi / (2 * Math.PI) * 16))]++;
            }
            double chi2 = 0;
            int steps = 400;
            double total = 0;
            double[] expected = new double[bins];
            for (int b = 0; b < bins; ++b)
            {
                double lo = -1 + 2.0 * b / bins, hi = -1 + 2.0 * (b + 1) / bins;
                double integral = 0;
                for (int k = 0; k < steps; ++k)
                {
                    double c = lo + (hi - lo) * (k + 0.5) / steps;
                    double s = Math.Sqrt(Math.Max(0, 1 - c * c));
                    Vector3f perp = Normalize(Cross(wo, Math.Abs(wo.x) < 0.9 ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0)));
                    Vector3f wi = (Float)c * wo + (Float)s * perp;
                    integral += phase.p(wo, wi) * 2 * Math.PI * (hi - lo) / steps;
                }
                expected[b] = integral;
                total += integral;
            }
            // the phase function integrates to one
            EXPECT_NEAR(total, 1.0, 2e-3);
            for (int b = 0; b < bins; ++b)
            {
                double e = expected[b] * n;
                chi2 += (count[b] - e) * (count[b] - e) / e;
            }
            azimuthChi2 = 0;
            for (int a = 0; a < 16; ++a)
            {
                double e = n / 16.0;
                azimuthChi2 += (azimuth[a] - e) * (azimuth[a] - e) / e;
            }
            return chi2;
        }

        [TEST("OslVolume", "HenyeyGreensteinAgreesWithPbrt")]
        public static void HenyeyGreensteinAgreesWithPbrt()
        {
            ResetScene();
            float[] gs = new float[] { -0.9f, -0.5f, -0.2f, 0f, 0.3f, 0.7f, 0.95f };
            Vector3f wo = Normalize(new Vector3f(0.3f, -0.5f, 0.8f));
            foreach (float g in gs)
            {
                // the closures that carry g: henyey_greenstein (through fog), anisotropic_vdf and medium_vdf
                OSLMedium fog = MakeOsl("fog", new Transform(), P("float", "g", g));
                OSLMedium aniso = MakeOsl("anisotropic", new Transform(), P("float", "anisotropy", g), P("rgb", "extinction", 1f, 1f, 1f));
                OSLMedium mvdf = MakeOsl("medium", new Transform(), P("float", "anisotropy", g), P("float", "transmission_depth", 1f),
                                         P("rgb", "transmission_color", 0.5f, 0.5f, 0.5f));
                HGPhaseFunction reference = new HGPhaseFunction(g);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.4f);
                PhaseFunction[] phases = new PhaseFunction[]
                {
                    fog.SamplePoint(new Point3f(0, 0, 0), lambda).phase, aniso.SamplePoint(new Point3f(0, 0, 0), lambda).phase,
                    mvdf.SamplePoint(new Point3f(0, 0, 0), lambda).phase
                };
                RNG rng = new RNG(7, 9);
                foreach (PhaseFunction ph in phases)
                {
                    for (int i = 0; i < 100; ++i)
                    {
                        Vector3f a = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        Vector3f b = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        EXPECT_FLOAT_EQ(ph.p(a, b), reference.p(a, b));
                        EXPECT_FLOAT_EQ(ph.PDF(a, b), reference.PDF(a, b));
                        Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                        pstd.optional<PhaseFunctionSample> s1 = ph.Sample_p(a, u), s2 = reference.Sample_p(a, u);
                        EXPECT_TRUE(s1.has_value() && s2.has_value());
                        EXPECT_FLOAT_EQ(s1.value().wi.x, s2.value().wi.x);
                        EXPECT_FLOAT_EQ(s1.value().wi.y, s2.value().wi.y);
                        EXPECT_FLOAT_EQ(s1.value().wi.z, s2.value().wi.z);
                        EXPECT_FLOAT_EQ(s1.value().p, s2.value().p);
                        EXPECT_FLOAT_EQ(s1.value().pdf, s2.value().pdf);
                        EXPECT_FLOAT_EQ(s1.value().p, s1.value().pdf);
                    }
                }
                // chi-square test of the directions that the OSL phase function samples against its own density
                double azimuthChi2;
                double chi2 = PhaseChiSquare(phases[0], wo, 400000, 40, out azimuthChi2);
                double critical = ChiSquareCritical(39, 3.09), criticalAz = ChiSquareCritical(15, 3.09);
                Console.WriteLine("[OslVolume] HG g=" + g.ToString(CultureInfo.InvariantCulture) + ": chi2 " + chi2.ToString("F1", CultureInfo.InvariantCulture) +
                                  " (39 dof, p=0.001 at " + critical.ToString("F1", CultureInfo.InvariantCulture) + "), azimuth chi2 " +
                                  azimuthChi2.ToString("F1", CultureInfo.InvariantCulture) + " (15 dof, " + criticalAz.ToString("F1", CultureInfo.InvariantCulture) + ")");
                EXPECT_LT(chi2, critical);
                EXPECT_LT(azimuthChi2, criticalAz);
            }
        }

        [TEST("OslVolume", "MixtureOfLobes")]
        public static void MixtureOfLobes()
        {
            ResetScene();
            // henyey_greenstein(0.7) * 1 + henyey_greenstein(-0.3) * 2 + isotropic() * 1: the mixture 1/4, 1/2, 1/4
            OSLMedium m = MakeOsl("vclosures", new Transform(), PI("mode", 8));
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            MediumProperties mp = m.SamplePoint(new Point3f(0, 0, 0), lambda);
            HGPhaseFunction a = new HGPhaseFunction(0.7f), b = new HGPhaseFunction(-0.3f), c = new HGPhaseFunction(0f);
            RNG rng = new RNG(1, 2);
            for (int i = 0; i < 200; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                double expected = 0.25 * a.p(wo, wi) + 0.5 * b.p(wo, wi) + 0.25 * c.p(wo, wi);
                EXPECT_NEAR(mp.phase.p(wo, wi), expected, 1e-5 * Math.Max(1, expected));
                EXPECT_NEAR(mp.phase.PDF(wo, wi), expected, 1e-5 * Math.Max(1, expected));
                pstd.optional<PhaseFunctionSample> s = mp.phase.Sample_p(wo, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                EXPECT_TRUE(s.has_value());
                EXPECT_NEAR(s.value().pdf, 0.25 * a.p(wo, s.value().wi) + 0.5 * b.p(wo, s.value().wi) + 0.25 * c.p(wo, s.value().wi), 1e-4);
            }
            Vector3f woFixed = Normalize(new Vector3f(-0.2f, 0.6f, 0.5f));
            double azimuthChi2;
            double chi2 = PhaseChiSquare(mp.phase, woFixed, 400000, 40, out azimuthChi2);
            Console.WriteLine("[OslVolume] mixture of three lobes: chi2 " + chi2.ToString("F1", CultureInfo.InvariantCulture) + ", azimuth " +
                              azimuthChi2.ToString("F1", CultureInfo.InvariantCulture));
            EXPECT_LT(chi2, ChiSquareCritical(39, 3.09));
            EXPECT_LT(azimuthChi2, ChiSquareCritical(15, 3.09));
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Heterogeneous media
        // ------------------------------------------------------------------------------------------------------------------------------

        private static Medium MakeBlobGrid(int n, float amp, float width, Transform renderFromMedium, params ParsedParameter[] extra)
        {
            float[] density = new float[n * n * n];
            for (int z = 0; z < n; ++z)
            {
                for (int y = 0; y < n; ++y)
                {
                    for (int x = 0; x < n; ++x)
                    {
                        double qx = (x + 0.5) / n - 0.5, qy = (y + 0.5) / n - 0.5, qz = (z + 0.5) / n - 0.5;
                        density[x + n * (y + n * z)] = (float)(amp * Math.Exp(-(qx * qx + qy * qy + qz * qz) / (2.0 * width * width)));
                    }
                }
            }
            List<ParsedParameter> ps = new List<ParsedParameter>();
            ps.Add(P("float", "density", density));
            ps.Add(PI("nx", n));
            ps.Add(PI("ny", n));
            ps.Add(PI("nz", n));
            ps.AddRange(extra);
            return MakePbrt("uniformgrid", renderFromMedium, ps.ToArray());
        }

        [TEST("OslVolume", "HeterogeneousBlobMatchesGridMedium")]
        public static void HeterogeneousBlobMatchesGridMedium()
        {
            ResetScene();
            const int gridRes = 80, rays = 40, perRay = 3000;
            ParsedParameter sa = P("rgb", "sigma_a", 0.3f, 0.15f, 0.05f), ss = P("rgb", "sigma_s", 0.5f, 0.7f, 0.9f);
            OSLMedium osl = MakeOsl("vblob", new Transform());
            Medium grid = MakeBlobGrid(gridRes, 6f, 0.18f, new Transform(), sa, ss, P("float", "g", 0.4f));
            EXPECT_FALSE(osl.IsHomogeneous());
            RNG rng = new RNG(31, 41);
            double chi2 = 0, maxZ = 0;
            int count = 0;
            double worstTauError = 0;
            for (int r = 0; r < rays; ++r)
            {
                Float tMax;
                Ray ray = RandomRay(ref rng, out tMax);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                OslTransmittanceStats a = EstimateTransmittance(osl, ray, tMax, lambda, perRay, 1000 + (ulong)r);
                OslTransmittanceStats b = EstimateTransmittance(grid, ray, tMax, lambda, perRay, 5000 + (ulong)r);
                // the optical depth of the OSL medium against the integral of the function itself
                double[] tauOsl = OpticalDepth(osl, ray, tMax, lambda, 3000), tauGrid = OpticalDepth(grid, ray, tMax, lambda, 3000);
                worstTauError = Math.Max(worstTauError, Math.Abs(tauOsl[0] - tauGrid[0]) / Math.Max(1e-3, tauGrid[0]));
                for (int c = 0; c < 2; ++c)
                {
                    int ch = c == 0 ? 0 : NSpectrumSamples - 1;
                    double se = Math.Sqrt(a.standardError[ch] * a.standardError[ch] + b.standardError[ch] * b.standardError[ch]);
                    if (se < 1e-12)
                    {
                        continue;
                    }
                    double z = (a.mean[ch] - b.mean[ch]) / se;
                    chi2 += z * z;
                    maxZ = Math.Max(maxZ, Math.Abs(z));
                    ++count;
                }
            }
            Console.WriteLine("[OslVolume] blob vs GridMedium (" + gridRes + "^3): " + count + " z-scores, sum z^2 " + chi2.ToString("F1", CultureInfo.InvariantCulture) +
                              " (expected " + count + " +- " + Math.Sqrt(2.0 * count).ToString("F1", CultureInfo.InvariantCulture) + "), max |z| " +
                              maxZ.ToString("F2", CultureInfo.InvariantCulture) + ", worst relative optical depth difference " +
                              worstTauError.ToString("E2", CultureInfo.InvariantCulture) + ", majorant violations " + osl.Violations());
            // the z-scores of independent estimates of equal expectations: sum of squares is chi-square with `count` dof (4.5 sigma), the largest |z| of 80 below 4.2
            EXPECT_LT(chi2, count + 4.5 * Math.Sqrt(2.0 * count));
            EXPECT_GT(chi2, count - 4.5 * Math.Sqrt(2.0 * count));
            EXPECT_LT(maxZ, 4.2);
            EXPECT_EQ(osl.Violations(), 0L);
        }

        [TEST("OslVolume", "MajorantsAreSoundForSmoothShaders")]
        public static void MajorantsAreSoundForSmoothShaders()
        {
            ResetScene();
            // random smooth functions with the proven bound amp * 1.3 of the extinction (a gray medium); the estimator is compared with exp(-tau),
            // where tau is the quadrature of the extinction itself. Both the empirical grid and the exact user bound must give an unbiased estimator.
            RNG rng = new RNG(17, 19);
            double worstZ = 0;
            int tests = 0;
            for (int shaderIndex = 0; shaderIndex < 6; ++shaderIndex)
            {
                float amp = 1.5f + 3.5f * rng.Uniform<Float>();
                float fx = 2 + 8 * rng.Uniform<Float>(), fy = 2 + 8 * rng.Uniform<Float>(), fz = 2 + 8 * rng.Uniform<Float>();
                float px = 6 * rng.Uniform<Float>(), py = 6 * rng.Uniform<Float>(), pz = 6 * rng.Uniform<Float>();
                ParsedParameter[] sp = new ParsedParameter[] { P("float", "amp", amp), P("float", "fx", fx), P("float", "fy", fy), P("float", "fz", fz),
                                                              P("float", "px", px), P("float", "py", py), P("float", "pz", pz) };
                List<ParsedParameter> empirical = new List<ParsedParameter>(sp), exact = new List<ParsedParameter>(sp);
                exact.Add(P("float", "majorant", amp * 1.3f));
                OSLMedium mEmpirical = MakeOsl("vtrig", new Transform(), empirical.ToArray());
                OSLMedium mExact = MakeOsl("vtrig", new Transform(), exact.ToArray());
                for (int r = 0; r < 4; ++r)
                {
                    Float tMax;
                    Ray ray = RandomRay(ref rng, out tMax);
                    SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                    double[] tau = OpticalDepth(mExact, ray, tMax, lambda, 4000);
                    double truth = Math.Exp(-tau[0]);
                    foreach (OSLMedium m in new OSLMedium[] { mEmpirical, mExact })
                    {
                        OslTransmittanceStats s = EstimateTransmittance(m, ray, tMax, lambda, 6000, 300 + (ulong)(shaderIndex * 10 + r));
                        double z = (s.mean[0] - truth) / s.standardError[0];
                        worstZ = Math.Max(worstZ, Math.Abs(z));
                        ++tests;
                        if (Math.Abs(z) > 4.5)
                        {
                            ADD_FAILURE("shader " + shaderIndex + " ray " + r + ": T " + s.mean[0] + " vs exp(-tau) " + truth + ", z " + z);
                        }
                    }
                }
                EXPECT_EQ(mEmpirical.Violations(), 0L);
                EXPECT_EQ(mExact.Violations(), 0L);
            }
            Console.WriteLine("[OslVolume] smooth random shaders: " + tests + " estimates of exp(-tau), worst |z| " + worstZ.ToString("F2", CultureInfo.InvariantCulture));
        }

        [TEST("OslVolume", "ViolationCounterFiresForTooSmallOverride")]
        public static void ViolationCounterFiresForTooSmallOverride()
        {
            ResetScene();
            // the extinction of this shader reaches 3.9; a majorant of 1.5 is wrong and the counter says so
            OSLMedium bad = MakeOsl("vtrig", new Transform(), P("float", "majorant", 1.5f));
            OSLMedium good = MakeOsl("vtrig", new Transform(), P("float", "majorant", 3.9f * 1.0001f));
            OSLMedium unchecked_ = MakeOsl("vtrig", new Transform(), P("float", "majorant", 1.5f), PB("checkmajorant", false));
            RNG rng = new RNG(5, 5);
            Float tMax;
            Ray ray = RandomRay(ref rng, out tMax);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
            double[] tau = OpticalDepth(good, ray, tMax, lambda, 4000);
            double truth = Math.Exp(-tau[0]);
            OslTransmittanceStats sGood = EstimateTransmittance(good, ray, tMax, lambda, 8000, 1);
            long before = OSLMedium.TotalViolations;
            OslTransmittanceStats sBad = EstimateTransmittance(bad, ray, tMax, lambda, 8000, 1);
            EstimateTransmittance(unchecked_, ray, tMax, lambda, 100, 1);
            Console.WriteLine("[OslVolume] exact bound: T " + sGood.mean[0].ToString("F4", CultureInfo.InvariantCulture) + " vs truth " + truth.ToString("F4", CultureInfo.InvariantCulture) +
                              ", violations " + good.Violations() + "; too small bound: T " + sBad.mean[0].ToString("F4", CultureInfo.InvariantCulture) + ", violations " + bad.Violations() +
                              " (worst ratio " + bad.WorstViolationRatio().ToString("F2", CultureInfo.InvariantCulture) + ")");
            EXPECT_EQ(good.Violations(), 0L);
            EXPECT_GT(bad.Violations(), 0L);
            EXPECT_GT(bad.WorstViolationRatio(), 1.0f);
            EXPECT_EQ(unchecked_.Violations(), 0L);
            EXPECT_GE(OSLMedium.TotalViolations - before, bad.Violations());
            // the estimate with the right bound is unbiased; with the wrong bound the transmittance is too high (the extinction is clipped)
            EXPECT_LT(Math.Abs(sGood.mean[0] - truth) / sGood.standardError[0], 4.5);
            EXPECT_GT((sBad.mean[0] - truth) / sBad.standardError[0], 4.5);
        }

        [TEST("OslVolume", "UserMajorantReplacesTheGrid")]
        public static void UserMajorantReplacesTheGrid()
        {
            ResetScene();
            OSLMedium m = MakeOsl("vblob", new Transform(), P("float", "sigma_t_max", 20f));
            MajorantGrid g = m.Majorants();
            EXPECT_EQ(g.res.x, 1);
            EXPECT_FLOAT_EQ(g.Lookup(0, 0, 0), 20f);
            OSLMedium e = MakeOsl("vblob", new Transform(), PI("majorantres", 8), PI("majorantsamples", 2), P("float", "majorantmargin", 1.5f));
            MajorantGrid ge = e.Majorants();
            EXPECT_EQ(ge.res.x, 8);
            // the centre cell holds the density of the blob at its largest (6 * (0.3+0.5) .. 6 * (0.05+0.9)), times the margin
            Float centre = ge.Lookup(3, 3, 3);
            EXPECT_GT(centre, 6f * 0.8f);
            EXPECT_LT(centre, 6f * 2.0f * 1.5f);
            // the corners of the cube are almost empty
            EXPECT_LT(ge.Lookup(0, 0, 0), 0.05f);
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Emission
        // ------------------------------------------------------------------------------------------------------------------------------

        [TEST("OslVolume", "ConstantEmissionEqualsHomogeneousMedium")]
        public static void ConstantEmissionEqualsHomogeneousMedium()
        {
            ResetScene();
            OSLMedium osl = MakeOsl("vemit", new Transform(), P("rgb", "sigma_a", 0.8f, 0.8f, 0.8f), P("rgb", "Le", 1f, 0.6f, 0.2f), P("float", "scale", 2f),
                                    P("float", "Lescale", 3f));
            Medium reference = MakePbrt("homogeneous", new Transform(), P("rgb", "sigma_a", 0.8f, 0.8f, 0.8f), P("rgb", "sigma_s", 0f, 0f, 0f),
                                        P("rgb", "Le", 1f, 0.6f, 0.2f), P("float", "scale", 2f), P("float", "Lescale", 3f));
            EXPECT_TRUE(osl.IsEmissive());
            EXPECT_TRUE(reference.IsEmissive());
            RNG rng = new RNG(8, 8);
            for (int i = 0; i < 10; ++i)
            {
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                MediumProperties a = osl.SamplePoint(new Point3f(0, 0, 0), lambda), b = reference.SamplePoint(new Point3f(0, 0, 0), lambda);
                ExpectSpectrumNear(a.sigma_a, b.sigma_a, 1e-6, "sigma_a");
                ExpectSpectrumNear(a.sigma_s, b.sigma_s, 1e-6, "sigma_s");
                ExpectSpectrumNear(a.Le, b.Le, 1e-6, "Le");
                EXPECT_TRUE((bool)a.Le);
            }
        }

        [TEST("OslVolume", "HeterogeneousEmissionAgainstQuadrature")]
        public static void HeterogeneousEmissionAgainstQuadrature()
        {
            ResetScene();
            // an absorbing and emitting blob: the radiance along a ray is the integral of sigma_a * Le * T. Delta tracking of the emission (as volpath
            // gathers it at the collisions of the majorant) against the quadrature of the same integrand.
            OSLMedium m = MakeOsl("vglow", new Transform());
            EXPECT_TRUE(m.IsEmissive());
            RNG rng = new RNG(23, 29);
            double worstZ = 0;
            for (int r = 0; r < 8; ++r)
            {
                Float tMax;
                Ray ray = RandomRay(ref rng, out tMax);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                // quadrature
                int steps = 6000;
                double h = tMax / steps, tau = 0, reference = 0;
                for (int i = 0; i < steps; ++i)
                {
                    MediumProperties mp = m.SamplePoint(ray.Apply((Float)((i + 0.5) * h)), lambda);
                    tau += (mp.sigma_a[0] + mp.sigma_s[0]) * 0.5 * h;
                    reference += mp.sigma_a[0] * mp.Le[0] * Math.Exp(-tau) * h;
                    tau += (mp.sigma_a[0] + mp.sigma_s[0]) * 0.5 * h;
                }
                int n = 20000;
                double sum = 0, sum2 = 0;
                for (int i = 0; i < n; ++i)
                {
                    RNG rg = new RNG(900 + (ulong)r, (ulong)i * 31UL);
                    double L = 0, beta = 1;
                    Float u = rg.Uniform<Float>();
                    SampleT_maj(ray, m, tMax, u, ref rg, lambda, (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                    {
                        L += beta * mp.sigma_a[0] * mp.Le[0] / sigma_maj[0];
                        beta *= Math.Max(0, sigma_maj[0] - mp.sigma_a[0] - mp.sigma_s[0]) / sigma_maj[0];
                        return beta > 0;
                    });
                    sum += L;
                    sum2 += L * L;
                }
                double mean = sum / n, se = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean) / (n - 1));
                double z = (mean - reference) / Math.Max(se, 1e-12);
                worstZ = Math.Max(worstZ, Math.Abs(z));
                Console.WriteLine("[OslVolume] emission ray " + r + ": delta tracking " + mean.ToString("F5", CultureInfo.InvariantCulture) + " +- " +
                                  se.ToString("F5", CultureInfo.InvariantCulture) + ", quadrature " + reference.ToString("F5", CultureInfo.InvariantCulture));
                if (reference > 1e-4)
                {
                    EXPECT_LT(Math.Abs(z), 4.5);
                }
            }
            EXPECT_EQ(m.Violations(), 0L);
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Overlap, spectral modes, threads
        // ------------------------------------------------------------------------------------------------------------------------------

        [TEST("OslVolume", "OverlappingWithPbrtMedia")]
        public static void OverlappingWithPbrtMedia()
        {
            ResetScene();
            OSLMedium osl = MakeOsl("fog", new Transform(), P("rgb", "sigma_a", 0.1f, 0.2f, 0.3f), P("rgb", "sigma_s", 0.4f, 0.4f, 0.4f), P("float", "g", 0.2f));
            Medium homogeneous = MakePbrt("homogeneous", new Transform(), P("rgb", "sigma_a", 0.3f, 0.2f, 0.1f), P("rgb", "sigma_s", 0.1f, 0.2f, 0.3f), P("float", "g", -0.5f));
            OSLMedium blob = MakeOsl("vblob", new Transform());
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.45f);
            Point3f p = new Point3f(0.5f, 0.55f, 0.5f);
            CompositeMedium composite = new CompositeMedium(new Medium[] { osl, homogeneous, blob }, new int[] { 0, 0, 0 });
            MediumProperties total = composite.SamplePoint(p, lambda);
            MediumProperties a = osl.SamplePoint(p, lambda), b = homogeneous.SamplePoint(p, lambda), c = blob.SamplePoint(p, lambda);
            ExpectSpectrumNear(total.sigma_a, a.sigma_a + b.sigma_a + c.sigma_a, 1e-6, "sigma_a of the overlap");
            ExpectSpectrumNear(total.sigma_s, a.sigma_s + b.sigma_s + c.sigma_s, 1e-6, "sigma_s of the overlap");
            // the majorants add up: a ray through the three
            Ray ray = new Ray(new Point3f(0.5f, 0.5f, -1f), new Vector3f(0, 0, 1));
            OslTransmittanceStats s = EstimateTransmittance(composite, ray, 3f, lambda, 20000, 4);
            double[] tauA = OpticalDepth(osl, ray, 3f, lambda, 2000), tauB = OpticalDepth(homogeneous, ray, 3f, lambda, 2000), tauC = OpticalDepth(blob, ray, 3f, lambda, 2000);
            double truth = Math.Exp(-(tauA[0] + tauB[0] + tauC[0]));
            double z = (s.mean[0] - truth) / s.standardError[0];
            Console.WriteLine("[OslVolume] overlap of OSL fog, homogeneous and OSL blob: T " + s.mean[0].ToString("F5", CultureInfo.InvariantCulture) + " +- " +
                              s.standardError[0].ToString("F5", CultureInfo.InvariantCulture) + " vs exp(-sum tau) " + truth.ToString("F5", CultureInfo.InvariantCulture));
            EXPECT_LT(Math.Abs(z), 4.5);
        }

        [TEST("OslVolume", "SpectralModes")]
        public static void SpectralModes()
        {
            ResetScene();
            ParsedParameter sa = P("rgb", "sigma_a", 0.6f, 0.2f, 0.1f), ss = P("rgb", "sigma_s", 0.3f, 0.5f, 0.9f);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.62f);
            OSLSystem.spectralMode = OSLSpectralMode.Upsample;
            MediumProperties up = MakeOsl("fog", new Transform(), sa, ss).SamplePoint(new Point3f(0, 0, 0), lambda);
            OSLSystem.spectralMode = OSLSpectralMode.Unbounded;
            MediumProperties unb = MakeOsl("fog", new Transform(), sa, ss).SamplePoint(new Point3f(0, 0, 0), lambda);
            // the coefficients are unbounded spectra in both (not albedo spectra)
            ExpectSpectrumNear(up.sigma_a, unb.sigma_a, 1e-6, "upsample vs unbounded sigma_a");
            ExpectSpectrumNear(up.sigma_s, unb.sigma_s, 1e-6, "upsample vs unbounded sigma_s");
            MediumProperties pb = MakePbrt("homogeneous", new Transform(), sa, ss).SamplePoint(new Point3f(0, 0, 0), lambda);
            ExpectSpectrumNear(up.sigma_a, pb.sigma_a, 1e-6, "sigma_a is that of pbrt's homogeneous medium (RGBUnboundedSpectrum)");
            ExpectSpectrumNear(up.sigma_s, pb.sigma_s, 1e-6, "sigma_s is that of pbrt's homogeneous medium (RGBUnboundedSpectrum)");
            // the gray mode: the luminance at all wavelengths
            OSLSystem.spectralMode = OSLSpectralMode.Gray;
            MediumProperties gray = MakeOsl("fog", new Transform(), sa, ss).SamplePoint(new Point3f(0, 0, 0), lambda);
            double lumA = 0.2126 * 0.6 + 0.7152 * 0.2 + 0.0722 * 0.1;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_NEAR(gray.sigma_a[i], lumA, 1e-6);
            }
            // a gray medium is the same in all three modes
            ParsedParameter ga = P("rgb", "sigma_a", 0.25f, 0.25f, 0.25f), gs = P("rgb", "sigma_s", 0.5f, 0.5f, 0.5f);
            OSLSpectralMode[] modes = new OSLSpectralMode[] { OSLSpectralMode.Upsample, OSLSpectralMode.Unbounded, OSLSpectralMode.Gray };
            MediumProperties first = default;
            for (int k = 0; k < 3; ++k)
            {
                OSLSystem.spectralMode = modes[k];
                MediumProperties mp = MakeOsl("fog", new Transform(), ga, gs).SamplePoint(new Point3f(0, 0, 0), lambda);
                if (k == 0)
                {
                    first = mp;
                }
                ExpectSpectrumNear(mp.sigma_a, first.sigma_a, 1e-5, "gray medium in mode " + k);
                ExpectSpectrumNear(mp.sigma_s, first.sigma_s, 1e-5, "gray medium in mode " + k);
            }
            // a heterogeneous shader: the same in the modes for a gray color (the extinction of the blob with gray coefficients)
            ParsedParameter ba = P("rgb", "sigma_a", 0.25f, 0.25f, 0.25f), bs = P("rgb", "sigma_s", 0.5f, 0.5f, 0.5f);
            MediumProperties[] blobs = new MediumProperties[3];
            for (int k = 0; k < 3; ++k)
            {
                OSLSystem.spectralMode = modes[k];
                blobs[k] = MakeOsl("vblob", new Transform(), ba, bs).SamplePoint(new Point3f(0.55f, 0.5f, 0.5f), lambda);
            }
            ExpectSpectrumNear(blobs[0].sigma_a, blobs[1].sigma_a, 1e-5, "blob upsample vs unbounded");
            ExpectSpectrumNear(blobs[0].sigma_a, blobs[2].sigma_a, 1e-5, "blob upsample vs gray");
            OSLSystem.spectralMode = OSLSpectralMode.Upsample;
            // the chromatic blob: extinction per wavelength is d(x) times the extinction spectrum of the colors
            OSLMedium chroma = MakeOsl("vblob", new Transform());
            MediumProperties mpc = chroma.SamplePoint(new Point3f(0.5f, 0.5f, 0.5f), lambda);
            Spectrum ea = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(6 * 0.3f, 6 * 0.15f, 6 * 0.05f));
            SampledSpectrum expected = ea.Sample(lambda);
            ExpectSpectrumNear(mpc.sigma_a, expected, 1e-4, "chromatic blob sigma_a at the centre");
        }

        [TEST("OslVolume", "ThreadSafety")]
        public static void ThreadSafety()
        {
            ResetScene();
            // several threads evaluate one medium, the first use (Prepare) included
            OSLMedium m = MakeOsl("vnoise", new Transform(), PI("kind", 1));
            const int threads = 8, points = 400;
            Point3f[] pts = new Point3f[points];
            RNG rng = new RNG(2, 3);
            for (int i = 0; i < points; ++i)
            {
                pts[i] = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
            }
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.2f);
            float[][] results = new float[threads][];
            System.Threading.Barrier start = new System.Threading.Barrier(threads);
            Thread[] ts = new Thread[threads];
            for (int t = 0; t < threads; ++t)
            {
                int ti = t;
                ts[t] = new Thread(() =>
                {
                    start.SignalAndWait();
                    float[] mine = new float[points * 2];
                    for (int rep = 0; rep < 3; ++rep)
                    {
                        for (int i = 0; i < points; ++i)
                        {
                            int j = (i + ti * 37) % points;
                            MediumProperties mp = m.SamplePoint(pts[j], lambda);
                            mine[2 * j] = mp.sigma_a[0];
                            mine[2 * j + 1] = mp.sigma_s[2];
                        }
                    }
                    results[ti] = mine;
                });
                ts[t].Start();
            }
            foreach (Thread t in ts)
            {
                t.Join();
            }
            int mismatches = 0;
            for (int t = 1; t < threads; ++t)
            {
                for (int i = 0; i < points * 2; ++i)
                {
                    if (results[t][i] != results[0][i])
                    {
                        ++mismatches;
                    }
                }
            }
            EXPECT_EQ(mismatches, 0);
            // the single thread reproduces the same values
            for (int i = 0; i < points; ++i)
            {
                MediumProperties mp = m.SamplePoint(pts[i], lambda);
                EXPECT_EQ(mp.sigma_a[0], results[0][2 * i]);
            }

            // transmittance estimates in parallel are those of the sequential run (same seeds)
            OSLMedium blob = MakeOsl("vblob", new Transform());
            Float tMax;
            Ray ray = RandomRay(ref rng, out tMax);
            OslTransmittanceStats serial = EstimateTransmittance(blob, ray, tMax, lambda, 2000, 55);
            OslTransmittanceStats[] par = new OslTransmittanceStats[threads];
            Thread[] ts2 = new Thread[threads];
            System.Threading.Barrier start2 = new System.Threading.Barrier(threads);
            for (int t = 0; t < threads; ++t)
            {
                int ti = t;
                ts2[t] = new Thread(() =>
                {
                    start2.SignalAndWait();
                    par[ti] = EstimateTransmittance(blob, ray, tMax, lambda, 2000, 55);
                });
                ts2[t].Start();
            }
            foreach (Thread t in ts2)
            {
                t.Join();
            }
            for (int t = 0; t < threads; ++t)
            {
                EXPECT_EQ(par[t].mean[0], serial.mean[0]);
            }
            EXPECT_EQ(blob.Violations(), 0L);
        }

        [TEST("OslVolume", "NoiseShadersRunInAVolume")]
        public static void NoiseShadersRunInAVolume()
        {
            ResetScene();
            for (int kind = 0; kind < 5; ++kind)
            {
                OSLMedium m = MakeOsl("vnoise", new Transform(), PI("kind", kind), PB("checkmajorant", false));
                EXPECT_FALSE(m.IsHomogeneous());
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.7f);
                RNG rng = new RNG(4, (ulong)kind);
                float lo = float.MaxValue, hi = 0;
                double mean = 0;
                const int n = 3000;
                for (int i = 0; i < n; ++i)
                {
                    MediumProperties mp = m.SamplePoint(new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>()), lambda);
                    float st = mp.sigma_a[0] + mp.sigma_s[0];
                    EXPECT_TRUE(!float.IsNaN(st) && !float.IsInfinity(st) && st >= 0);
                    lo = Math.Min(lo, st);
                    hi = Math.Max(hi, st);
                    mean += st / n;
                }
                Console.WriteLine("[OslVolume] noise kind " + kind + ": extinction min " + lo.ToString("F3", CultureInfo.InvariantCulture) + " max " +
                                  hi.ToString("F3", CultureInfo.InvariantCulture) + " mean " + mean.ToString("F3", CultureInfo.InvariantCulture));
                EXPECT_GT(hi - lo, 0.2f);
            }
        }

        [TEST("OslVolume", "CloudShaderIsSound")]
        public static void CloudShaderIsSound()
        {
            ResetScene();
            OSLMedium cloud = MakeOsl("cloud", new Transform());
            EXPECT_FALSE(cloud.IsHomogeneous());
            RNG rng = new RNG(61, 67);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.35f);
            int nonEmpty = 0;
            double worstZ = 0;
            for (int r = 0; r < 12; ++r)
            {
                Float tMax;
                Ray ray = RandomRay(ref rng, out tMax);
                double[] tau = OpticalDepth(cloud, ray, tMax, lambda, 4000);
                if (tau[0] > 0.05)
                {
                    ++nonEmpty;
                }
                double truth = Math.Exp(-tau[0]);
                OslTransmittanceStats s = EstimateTransmittance(cloud, ray, tMax, lambda, 4000, 70 + (ulong)r);
                double z = (s.mean[0] - truth) / Math.Max(s.standardError[0], 1e-9);
                worstZ = Math.Max(worstZ, Math.Abs(z));
                if (Math.Abs(z) > 4.5 && s.standardError[0] > 0)
                {
                    ADD_FAILURE("cloud ray " + r + ": T " + s.mean[0] + " vs exp(-tau) " + truth + " z " + z);
                }
            }
            Console.WriteLine("[OslVolume] cloud: " + nonEmpty + " of 12 rays hit cloud, worst |z| " + worstZ.ToString("F2", CultureInfo.InvariantCulture) +
                              ", majorant violations " + cloud.Violations());
            EXPECT_GT(nonEmpty, 4);
            EXPECT_EQ(cloud.Violations(), 0L);
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Performance: the cost of a query of the medium
        // ------------------------------------------------------------------------------------------------------------------------------

        private static double NanosecondsPerQuery(Medium m, Point3f[] pts, SampledWavelengths lambda, int reps)
        {
            Ray r = new Ray(new Point3f(0.5f, 0.5f, -2f), new Vector3f(0, 0, 1));
            // SampleRay sets the direction that the OSL medium hands to the shader
            m.SampleRay(r, 10f, lambda, new ScratchBuffer());
            float sink = 0;
            for (int i = 0; i < pts.Length; ++i)
            {
                sink += m.SamplePoint(pts[i], lambda).sigma_a[0];
            }
            Stopwatch sw = Stopwatch.StartNew();
            for (int rep = 0; rep < reps; ++rep)
            {
                for (int i = 0; i < pts.Length; ++i)
                {
                    sink += m.SamplePoint(pts[i], lambda).sigma_a[0];
                }
            }
            sw.Stop();
            if (float.IsNaN(sink))
            {
                Console.WriteLine("nan");
            }
            return sw.Elapsed.TotalMilliseconds * 1e6 / ((double)reps * pts.Length);
        }

        private static double MicrosecondsPerTransmittanceRay(Medium m, SampledWavelengths lambda, int rays)
        {
            RNG rng = new RNG(99, 100);
            Ray[] rs = new Ray[64];
            Float[] tm = new Float[64];
            for (int i = 0; i < rs.Length; ++i)
            {
                rs[i] = RandomRay(ref rng, out tm[i]);
            }
            EstimateTransmittance(m, rs[0], tm[0], lambda, 50, 1);
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < rays; ++i)
            {
                EstimateTransmittance(m, rs[i % rs.Length], tm[i % rs.Length], lambda, 1, (ulong)i);
            }
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds * 1e3 / rays;
        }

        [TEST("OslVolume", "QueryCostAgainstGridMedium")]
        public static void QueryCostAgainstGridMedium()
        {
            ResetScene();
            Medium grid = MakeBlobGrid(80, 6f, 0.18f, new Transform(), P("rgb", "sigma_a", 0.3f, 0.15f, 0.05f), P("rgb", "sigma_s", 0.5f, 0.7f, 0.9f));
            Medium homogeneous = MakePbrt("homogeneous", new Transform(), P("rgb", "sigma_a", 0.3f, 0.15f, 0.05f), P("rgb", "sigma_s", 0.5f, 0.7f, 0.9f));
            OSLMedium blob = MakeOsl("vblob", new Transform());
            OSLMedium noise = MakeOsl("cloud", new Transform());
            OSLMedium constant = MakeOsl("fog", new Transform());
            RNG rng = new RNG(1, 1);
            Point3f[] pts = new Point3f[4096];
            for (int i = 0; i < pts.Length; ++i)
            {
                pts[i] = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
            }
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
            double nsGrid = NanosecondsPerQuery(grid, pts, lambda, 100), nsHom = NanosecondsPerQuery(homogeneous, pts, lambda, 100);
            double nsConst = NanosecondsPerQuery(constant, pts, lambda, 100), nsBlob = NanosecondsPerQuery(blob, pts, lambda, 20);
            double nsCloud = NanosecondsPerQuery(noise, pts, lambda, 10);
            double usGrid = MicrosecondsPerTransmittanceRay(grid, lambda, 400), usBlob = MicrosecondsPerTransmittanceRay(blob, lambda, 400);
            double usCloud = MicrosecondsPerTransmittanceRay(noise, lambda, 400);
            Func<double, string> f = (double v) => v.ToString("F0", CultureInfo.InvariantCulture);
            string table = "[OslVolume] cost of SamplePoint (ns/query, one thread): HomogeneousMedium " + f(nsHom) + ", GridMedium(80^3) " + f(nsGrid) +
                           ", OSL constant (precomputed) " + f(nsConst) + ", OSL gaussian blob " + f(nsBlob) + " (" + (nsBlob / nsGrid).ToString("F1", CultureInfo.InvariantCulture) +
                           "x GridMedium), OSL fbm cloud (5 octaves) " + f(nsCloud) + " (" + (nsCloud / nsGrid).ToString("F1", CultureInfo.InvariantCulture) + "x)\n" +
                           "[OslVolume] ratio-tracking transmittance of one ray (us): GridMedium " + f(usGrid) + ", OSL blob " + f(usBlob) + ", OSL cloud " + f(usCloud);
            Console.WriteLine(table);
            File.WriteAllText(Path.Combine(OslVolumeShaders.OutputDir, "performance.txt"), table + "\n");
            EXPECT_TRUE(nsBlob > 0 && nsGrid > 0);
            // the constant shader is a table look-up and not a shader run
            EXPECT_LT(nsConst, nsBlob);
        }

        // ------------------------------------------------------------------------------------------------------------------------------
        // Renders
        // ------------------------------------------------------------------------------------------------------------------------------

        private const int RX = 160, RY = 120;

        // The common scene: the camera, the lights, the ground and two diffuse spheres; the medium sits in a sphere with an interface material
        private static string VolScene(string integrator, string namedMedia, string cameraMedium, string shapes, bool deltaLights = true, bool areaLight = false)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("LookAt 0 0.9 -5.6  0 0 0  0 1 0\n");
            if (cameraMedium.Length > 0)
            {
                // the media of the camera are made before the camera (their transform does not matter: they are unbounded)
                sb.Append(namedMedia);
                sb.Append("MediumInterface \"\" \"" + cameraMedium + "\"\n");
            }
            sb.Append("Camera \"perspective\" \"float fov\" 32\n");
            sb.Append("Film \"rgb\" \"integer xresolution\" " + RX + " \"integer yresolution\" " + RY + " \"string filename\" \"out.exr\"\n");
            sb.Append("Sampler \"independent\" \"integer pixelsamples\" 16\n");
            sb.Append("Integrator " + integrator + "\n");
            sb.Append("WorldBegin\n");
            sb.Append("LightSource \"infinite\" \"rgb L\" [0.35 0.4 0.5]\n");
            if (deltaLights)
            {
                sb.Append("LightSource \"point\" \"rgb I\" [45 45 42] \"point3 from\" [2.2 3 -2.4]\n");
            }
            if (areaLight)
            {
                sb.Append("AttributeBegin\n  AreaLightSource \"diffuse\" \"rgb L\" [12 12 11]\n  Material \"interface\"\n  Translate 2 3.2 -1\n  Shape \"sphere\" \"float radius\" 0.5\nAttributeEnd\n");
            }
            if (cameraMedium.Length == 0)
            {
                sb.Append(namedMedia);
            }
            sb.Append(shapes);
            return sb.ToString();
        }

        private const string Backdrop =
            "AttributeBegin\n  Material \"diffuse\" \"rgb reflectance\" [0.55 0.55 0.55]\n  Translate 0 -1.1 0\n" +
            "  Shape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-6 0 -6  6 0 -6  6 0 6  -6 0 6]\nAttributeEnd\n" +
            "AttributeBegin\n  Material \"diffuse\" \"rgb reflectance\" [0.75 0.2 0.15]\n  Translate -1.6 -0.5 1.6\n  Shape \"sphere\" \"float radius\" 0.6\nAttributeEnd\n" +
            "AttributeBegin\n  Material \"diffuse\" \"rgb reflectance\" [0.2 0.35 0.75]\n  Translate 1.7 -0.6 1.3\n  Shape \"sphere\" \"float radius\" 0.5\nAttributeEnd\n";

        private static string MediumSphere(string medium, float radius = 1.5f, string at = "")
        {
            return "AttributeBegin\n  MediumInterface \"" + medium + "\" \"\"\n  Material \"interface\"\n  " + at + "Shape \"sphere\" \"float radius\" " + radius.ToString(CultureInfo.InvariantCulture) + "\nAttributeEnd\n";
        }

        private static string Num(params float[] v)
        {
            return string.Join(" ", Array.ConvertAll(v, x => x.ToString("R", CultureInfo.InvariantCulture)));
        }

        // pbrt's homogeneous medium and the OSL medium of fog.osl with the same parameters
        private static string HomogeneousDecl(string name, float[] sa, float[] ss, float g, string extra = "")
        {
            return "MakeNamedMedium \"" + name + "\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [" + Num(sa) + "] \"rgb sigma_s\" [" + Num(ss) + "] \"float g\" " +
                   g.ToString(CultureInfo.InvariantCulture) + " " + extra + "\n";
        }

        private static string OslFogDecl(string name, float[] sa, float[] ss, float g, string extra = "")
        {
            return "MakeNamedMedium \"" + name + "\" \"string type\" \"osl\" \"string shader\" \"fog.osl\" \"rgb sigma_a\" [" + Num(sa) + "] \"rgb sigma_s\" [" + Num(ss) +
                   "] \"float g\" " + g.ToString(CultureInfo.InvariantCulture) + " " + extra + "\n";
        }

        private static string IntegratorText(string name, int maxdepth = 40)
        {
            return "\"" + name + "\" \"integer maxdepth\" " + maxdepth;
        }

        private static void WriteRow(string id, string title, string oslPng, string refPng, string diffPng, string note)
        {
            Directory.CreateDirectory(OslVolumeShaders.OutputDir);
            File.WriteAllText(Path.Combine(OslVolumeShaders.OutputDir, id + ".row"), title + "\t" + oslPng + "\t" + refPng + "\t" + diffPng + "\t" + note + "\n");
        }

        private static double Pixelwise(OslRender a, OslRender b)
        {
            return OslRender.MeanAbsDiff(a, b);
        }

        // Renders the OSL scene, the pbrt scene with the same seed, and the pbrt scene with another seed (the noise floor); writes the images with their
        // labels and the row of the contact sheet. Returns { MAD(osl, ref same seed), MAD(osl, ref other seed), MAD(ref, ref other seed), relative mean difference }
        private static double[] RenderPair(string id, string title, string integratorName, string oslScene, string refScene, int spp, int seed, string note = "")
        {
            string dir = OslVolumeShaders.Directory();
            OslRender osl = OslRenderTools.Render(id + "_osl", oslScene, spp, seed, "", dir);
            OslRender reference = OslRenderTools.Render(id + "_pbrt", refScene, spp, seed, "", dir);
            OslRender reference2 = OslRenderTools.Render(id + "_pbrt_b", refScene, spp, seed + 1, "", dir);
            ASSERT_TRUE(osl != null && reference != null && reference2 != null);
            double same = Pixelwise(osl, reference), other = Pixelwise(osl, reference2), floor = Pixelwise(reference, reference2);
            double[] mo = osl.ChannelMeans(), mr = reference.ChannelMeans();
            double rel = 0;
            for (int c = 0; c < 3; ++c)
            {
                rel = Math.Max(rel, Math.Abs(mo[c] - mr[c]) / Math.Max(1e-3, mr[c]));
            }
            string mad = "MAD " + same.ToString("F5", CultureInfo.InvariantCulture) + "  FLOOR " + floor.ToString("F5", CultureInfo.InvariantCulture);
            string label = integratorName;
            string p1 = OslRenderTools.SaveLabelled(osl, id + "_osl", "OSL " + label, spp, seed, title, 1f, OslVolumeShaders.OutputDir);
            string p2 = OslRenderTools.SaveLabelled(reference, id + "_pbrt", "PBRT " + label, spp, seed, title, 1f, OslVolumeShaders.OutputDir);
            string p3 = OslRenderTools.SaveLabelled(OslRenderTools.Difference(osl, reference, 8), id + "_diffx8", "DIFF X8 " + label, spp, seed, mad, 1f, OslVolumeShaders.OutputDir);
            WriteRow(id, title + " [" + integratorName + "]", p1, p2, p3, note + "MAD " + same.ToString("F5", CultureInfo.InvariantCulture) + ", noise floor " +
                     floor.ToString("F5", CultureInfo.InvariantCulture) + ", means " + OslRenderTools.F(mo) + " vs " + OslRenderTools.F(mr));
            Console.WriteLine("[OslVolume] render " + id + " (" + integratorName + ", " + spp + " spp): MAD(osl, ref same seed) " + same.ToString("F5", CultureInfo.InvariantCulture) +
                              ", MAD(osl, ref seed+1) " + other.ToString("F5", CultureInfo.InvariantCulture) + ", noise floor MAD(ref, ref seed+1) " +
                              floor.ToString("F5", CultureInfo.InvariantCulture) + ", means " + OslRenderTools.F(mo) + " vs " + OslRenderTools.F(mr) + "\n    " + p1 + "\n    " + p2);
            return new double[] { same, other, floor, rel };
        }

        // The OSL render and the reference are equal within the noise: against an independent render of the reference
        private static void ExpectWithinNoise(double[] r, double ratio, double meanTol)
        {
            EXPECT_LT(r[1], ratio * r[2] + 1e-4);
            EXPECT_LT(r[3], meanTol);
        }

        [TEST("OslVolume", "RenderHomogeneousAbsorbing")]
        public static void RenderHomogeneousAbsorbing()
        {
            ResetScene();
            float[] sa = new float[] { 0.7f, 0.7f, 0.7f }, ss = new float[] { 0, 0, 0 };
            string integ = IntegratorText("volpath");
            double[] r = RenderPair("a1_absorbing", "ABSORBING FOG", "volpath", VolScene(integ, OslFogDecl("m", sa, ss, 0), "", Backdrop + MediumSphere("m")),
                                    VolScene(integ, HomogeneousDecl("m", sa, ss, 0), "", Backdrop + MediumSphere("m")), 64, 3);
            // the same estimators and the same random numbers: the images agree to the rounding of the spectra
            EXPECT_LT(r[0], 2e-4);
            ExpectWithinNoise(r, 1.5, 0.01);
        }

        [TEST("OslVolume", "RenderHomogeneousScattering")]
        public static void RenderHomogeneousScattering()
        {
            ResetScene();
            float[] sa = new float[] { 0.02f, 0.02f, 0.02f }, ss = new float[] { 1.1f, 1.1f, 1.1f };
            string integ = IntegratorText("volpath");
            double[] r = RenderPair("a2_scattering", "SCATTERING FOG G=0.4", "volpath", VolScene(integ, OslFogDecl("m", sa, ss, 0.4f), "", Backdrop + MediumSphere("m")),
                                    VolScene(integ, HomogeneousDecl("m", sa, ss, 0.4f), "", Backdrop + MediumSphere("m")), 64, 5);
            EXPECT_LT(r[0], 2e-4);
            ExpectWithinNoise(r, 1.5, 0.01);
        }

        [TEST("OslVolume", "RenderHomogeneousChromatic")]
        public static void RenderHomogeneousChromatic()
        {
            ResetScene();
            float[] sa = new float[] { 0.1f, 0.5f, 1.2f }, ss = new float[] { 1.2f, 0.7f, 0.1f };
            string integ = IntegratorText("volpath");
            double[] r = RenderPair("a3_chromatic", "CHROMATIC FOG", "volpath", VolScene(integ, OslFogDecl("m", sa, ss, 0.2f), "", Backdrop + MediumSphere("m")),
                                    VolScene(integ, HomogeneousDecl("m", sa, ss, 0.2f), "", Backdrop + MediumSphere("m")), 64, 7);
            EXPECT_LT(r[0], 2e-4);
            ExpectWithinNoise(r, 1.5, 0.01);
        }

        [TEST("OslVolume", "RenderEmissiveHomogeneous")]
        public static void RenderEmissiveHomogeneous()
        {
            ResetScene();
            string integ = IntegratorText("volpath");
            string osl = "MakeNamedMedium \"m\" \"string type\" \"osl\" \"string shader\" \"vemit.osl\" \"rgb sigma_a\" [0.9 0.9 0.9] \"rgb Le\" [1 0.5 0.15] \"float Lescale\" 2\n";
            string pbrtMedium = "MakeNamedMedium \"m\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0.9 0.9 0.9] \"rgb sigma_s\" [0 0 0] \"rgb Le\" [1 0.5 0.15] \"float Lescale\" 2\n";
            double[] r = RenderPair("a4_emissive", "EMISSIVE FOG", "volpath", VolScene(integ, osl, "", Backdrop + MediumSphere("m")),
                                    VolScene(integ, pbrtMedium, "", Backdrop + MediumSphere("m")), 64, 9);
            EXPECT_LT(r[0], 2e-4);
            ExpectWithinNoise(r, 1.5, 0.01);
        }

        [TEST("OslVolume", "RenderCameraInFog")]
        public static void RenderCameraInFog()
        {
            ResetScene();
            float[] sa = new float[] { 0.04f, 0.04f, 0.04f }, ss = new float[] { 0.22f, 0.26f, 0.32f };
            string integ = IntegratorText("volpath");
            // the fog fills the space: the camera is in it and so is everything else (the medium is the one that rays see until a surface with an interface)
            string body = Backdrop;
            double[] r = RenderPair("a5_camera_in_fog", "CAMERA IN FOG", "volpath", VolScene(integ, OslFogDecl("m", sa, ss, 0.3f), "m", body),
                                    VolScene(integ, HomogeneousDecl("m", sa, ss, 0.3f), "m", body), 64, 11);
            EXPECT_LT(r[0], 2e-4);
            ExpectWithinNoise(r, 1.5, 0.01);
        }

        [TEST("OslVolume", "RenderNestedMedia")]
        public static void RenderNestedMedia()
        {
            ResetScene();
            float[] saA = new float[] { 0.03f, 0.03f, 0.03f }, ssA = new float[] { 0.25f, 0.25f, 0.25f };
            float[] saB = new float[] { 0.5f, 0.2f, 0.05f }, ssB = new float[] { 0.2f, 0.9f, 1.0f };
            string integ = IntegratorText("volpath");
            string shapes = Backdrop + MediumSphere("A", 2.2f) +
                            "AttributeBegin\n  MediumInterface \"B\" \"A\"\n  Material \"interface\"\n  Translate 0.2 0 0\n  Shape \"sphere\" \"float radius\" 0.9\nAttributeEnd\n";
            double[] r = RenderPair("a6_nested", "NESTED MEDIA, CAMERA IN OUTER", "volpath",
                                    VolScene(integ, OslFogDecl("A", saA, ssA, 0.1f) + OslFogDecl("B", saB, ssB, -0.2f), "A", shapes),
                                    VolScene(integ, HomogeneousDecl("A", saA, ssA, 0.1f) + HomogeneousDecl("B", saB, ssB, -0.2f), "A", shapes), 64, 13);
            EXPECT_LT(r[0], 2e-4);
            ExpectWithinNoise(r, 1.5, 0.01);
        }

        [TEST("OslVolume", "RenderMixedOslAndPbrtMedia")]
        public static void RenderMixedOslAndPbrtMedia()
        {
            ResetScene();
            // OSL fog inside a pbrt homogeneous medium against the same with both in pbrt
            float[] saA = new float[] { 0.03f, 0.03f, 0.03f }, ssA = new float[] { 0.25f, 0.25f, 0.25f };
            float[] saB = new float[] { 0.5f, 0.2f, 0.05f }, ssB = new float[] { 0.2f, 0.9f, 1.0f };
            string integ = IntegratorText("volpath");
            string shapes = Backdrop + MediumSphere("A", 2.2f) +
                            "AttributeBegin\n  MediumInterface \"B\" \"A\"\n  Material \"interface\"\n  Translate 0.2 0 0\n  Shape \"sphere\" \"float radius\" 0.9\nAttributeEnd\n";
            double[] r = RenderPair("a7_mixed", "PBRT OUTER, OSL INNER", "volpath",
                                    VolScene(integ, HomogeneousDecl("A", saA, ssA, 0.1f) + OslFogDecl("B", saB, ssB, -0.2f), "A", shapes),
                                    VolScene(integ, HomogeneousDecl("A", saA, ssA, 0.1f) + HomogeneousDecl("B", saB, ssB, -0.2f), "A", shapes), 64, 15);
            EXPECT_LT(r[0], 2e-4);
            ExpectWithinNoise(r, 1.5, 0.01);
        }

        [TEST("OslVolume", "RenderGlassSphereWithOslMedium")]
        public static void RenderGlassSphereWithOslMedium()
        {
            ResetScene();
            float[] sa = new float[] { 0.5f, 0.15f, 0.04f }, ss = new float[] { 0.1f, 0.5f, 0.9f };
            string integ = IntegratorText("volpath", 60);
            string glass = "AttributeBegin\n  MediumInterface \"m\" \"\"\n  Material \"dielectric\" \"float eta\" 1.5\n  Shape \"sphere\" \"float radius\" 1.3\nAttributeEnd\n";
            double[] r = RenderPair("a8_glass", "OSL MEDIUM IN A GLASS SPHERE", "volpath", VolScene(integ, OslFogDecl("m", sa, ss, 0.5f), "", Backdrop + glass),
                                    VolScene(integ, HomogeneousDecl("m", sa, ss, 0.5f), "", Backdrop + glass), 96, 17);
            EXPECT_LT(r[0], 2e-4);
            ExpectWithinNoise(r, 1.5, 0.015);
        }

        // The density of the cloud shader on a grid: the pbrt reference of the heterogeneous renders
        private static string CloudGridDecl(string name, OSLMedium cloud, Transform renderFromMedium, int n, string preamble)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(preamble);
            sb.Append("MakeNamedMedium \"" + name + "\" \"string type\" \"uniformgrid\" \"rgb sigma_a\" [0.04 0.03 0 ] \"rgb sigma_s\" [0.96 0.97 1.0] \"float g\" 0.35 " +
                      "\"integer nx\" " + n + " \"integer ny\" " + n + " \"integer nz\" " + n + " \"float density\" [");
            for (int z = 0; z < n; ++z)
            {
                for (int y = 0; y < n; ++y)
                {
                    for (int x = 0; x < n; ++x)
                    {
                        Point3f pm = new Point3f((x + 0.5f) / n, (y + 0.5f) / n, (z + 0.5f) / n);
                        OSLVolumeValues v;
                        cloud.Evaluate(renderFromMedium.Apply(pm), new Vector3f(0, 0, 1), 0, out v);
                        sb.Append((v.sigma_a.x + v.sigma_s.x).ToString("R", CultureInfo.InvariantCulture));
                        sb.Append(' ');
                    }
                }
            }
            sb.Append("]\n");
            return sb.ToString();
        }

        private static string CloudShapes(float scaleToWorld)
        {
            return Backdrop + "AttributeBegin\n  MediumInterface \"c\" \"\"\n  Material \"interface\"\n  Shape \"sphere\" \"float radius\" 1.8\nAttributeEnd\n";
        }

        [TEST("OslVolume", "RenderCloud")]
        public static void RenderCloud()
        {
            ResetScene();
            string integ = IntegratorText("volpath", 60);
            // the medium space is the cube [-1, 1]^3 of the world: the cloud (centre 0.5, radius 0.5 of medium space) is the unit sphere of the world
            string pre = "AttributeBegin\n  Translate -1 -1 -1\n  Scale 2 2 2\n";
            string post = "AttributeEnd\n";
            Transform t = Translate(new Vector3f(-1, -1, -1)) * Scale(2, 2, 2);
            OSLMedium shaderForGrid = MakeOsl("cloud", t, P("float", "density", 6f));
            string oslDecl = pre + "  MakeNamedMedium \"c\" \"string type\" \"osl\" \"string shader\" \"cloud.osl\" \"float density\" 6 \"point3 p0\" [0 0 0] \"point3 p1\" [1 1 1]\n" + post;
            string gridDecl = CloudGridDecl("c", shaderForGrid, t, 64, pre).Replace("\"float g\" 0.35 ", "\"float g\" 0.35 \"point3 p0\" [0 0 0] \"point3 p1\" [1 1 1] ") + post;
            double[] r = RenderPair("b1_cloud", "NOISE CLOUD (FBM) VS GRID 64 CUBED", "volpath", VolScene(integ, oslDecl, "", CloudShapes(1)), VolScene(integ, gridDecl, "", CloudShapes(1)),
                                    96, 19, "reference: GridMedium sampled from the shader on 64^3. ");
            // the grid is a trilinear table of the function (resolution 64 of a 5 octave noise): compared in the mean and against the noise floor with room for that
            EXPECT_LT(r[3], 0.03);
            EXPECT_LT(r[1], 3.0 * r[2] + 1e-3);
        }

        [TEST("OslVolume", "RenderFlame")]
        public static void RenderFlame()
        {
            ResetScene();
            string integ = IntegratorText("volpath", 30);
            string pre = "AttributeBegin\n  Translate -1 -1.1 -1\n  Scale 2 2.4 2\n";
            string decl = pre + "  MakeNamedMedium \"c\" \"string type\" \"osl\" \"string shader\" \"flame.osl\" \"float density\" 12 \"float emission_scale\" 30 " +
                          "\"point3 p0\" [0 0 0] \"point3 p1\" [1 1 1]\nAttributeEnd\n";
            string shapes = Backdrop + "AttributeBegin\n  MediumInterface \"c\" \"\"\n  Material \"interface\"\n  Translate 0 0.1 0\n  Shape \"sphere\" \"float radius\" 1.9\nAttributeEnd\n";
            string scene = VolScene(integ, decl, "", shapes);
            OslRender img = OslRenderTools.Render("b2_flame", scene, 128, 21, "", OslVolumeShaders.Directory());
            ASSERT_TRUE(img != null);
            string p = OslRenderTools.SaveLabelled(img, "b2_flame_osl", "OSL volpath", 128, 21, "EMISSIVE FLAME (NOISE)", 1f, OslVolumeShaders.OutputDir);
            WriteRow("b2_flame", "EMISSIVE FLAME (OSL EMISSION + NOISE) [volpath]", p, "", "", "no pbrt reference: pbrt media have one emission color. ");
            double[] m = img.ChannelMeans();
            Console.WriteLine("[OslVolume] flame means " + OslRenderTools.F(m) + "\n    " + p);
            // the flame is warm: red above blue in the mean, and there is light in the image
            EXPECT_GT(m[0], m[2]);
            EXPECT_GT(m[0], 0.02);
        }

        [TEST("OslVolume", "RenderFurnace")]
        public static void RenderFurnace()
        {
            ResetScene();
            // a white furnace: an albedo of 1 in a uniform environment with no other lights and no other surfaces: every pixel is the radiance of the environment
            string integ = IntegratorText("volpath", 400);
            Func<string, string, string> furnace = (string media, string shape) =>
                "LookAt 0 0 -5  0 0 0  0 1 0\nCamera \"perspective\" \"float fov\" 30\nFilm \"rgb\" \"integer xresolution\" 48 \"integer yresolution\" 48 \"string filename\" \"out.exr\"\n" +
                "Sampler \"independent\" \"integer pixelsamples\" 16\nIntegrator " + integ + "\nWorldBegin\nLightSource \"infinite\" \"rgb L\" [1 1 1]\n" + media + shape;
            string sphere = MediumSphere("m", 1.8f);
            string pre = "AttributeBegin\n  Translate -1 -1 -1\n  Scale 2 2 2\n";
            string[] decls = new string[]
            {
                OslFogDecl("m", new float[] { 0, 0, 0 }, new float[] { 1.3f, 1.3f, 1.3f }, 0.6f),
                pre + "  MakeNamedMedium \"m\" \"string type\" \"osl\" \"string shader\" \"cloud.osl\" \"float density\" 5 \"rgb albedo\" [1 1 1] \"point3 p0\" [0 0 0] \"point3 p1\" [1 1 1]\nAttributeEnd\n",
                pre + "  MakeNamedMedium \"m\" \"string type\" \"osl\" \"string shader\" \"vblob.osl\" \"rgb sigma_a\" [0 0 0] \"rgb sigma_s\" [1 1 1] \"float amp\" 5 \"point3 p0\" [0 0 0] \"point3 p1\" [1 1 1]\nAttributeEnd\n"
            };
            string[] names = new string[] { "constant fog g=0.6", "cloud (albedo 1)", "gaussian blob (gray, albedo 1)" };
            for (int i = 0; i < decls.Length; ++i)
            {
                OslRender img = OslRenderTools.Render("f" + i + "_furnace", furnace(decls[i], sphere), 128, 3, "", OslVolumeShaders.Directory());
                ASSERT_TRUE(img != null);
                double[] m = img.ChannelMeans();
                double lo = double.MaxValue, hi = 0;
                for (int k = 0; k < img.rgb.Length; ++k)
                {
                    lo = Math.Min(lo, img.rgb[k]);
                    hi = Math.Max(hi, img.rgb[k]);
                }
                Console.WriteLine("[OslVolume] furnace " + names[i] + ": mean " + OslRenderTools.F(m) + ", pixel range " + lo.ToString("F3", CultureInfo.InvariantCulture) + " .. " +
                                  hi.ToString("F3", CultureInfo.InvariantCulture));
                for (int c = 0; c < 3; ++c)
                {
                    EXPECT_NEAR(m[c], 1.0, 0.01);
                }
                string p = OslRenderTools.SaveLabelled(img, "c" + i + "_furnace", "OSL volpath FURNACE", 128, 3, names[i].ToUpperInvariant() + " MEAN " + m[1].ToString("F4", CultureInfo.InvariantCulture), 1f,
                                                       OslVolumeShaders.OutputDir);
                if (i == 0)
                {
                    // the control: with absorption the same scene is darker, so the medium is part of the render
                    OslRender dark = OslRenderTools.Render("f9_furnace_control", furnace(OslFogDecl("m", new float[] { 0.3f, 0.3f, 0.3f }, new float[] { 1.3f, 1.3f, 1.3f }, 0.6f), sphere), 64, 3, "",
                                                           OslVolumeShaders.Directory());
                    ASSERT_TRUE(dark != null);
                    EXPECT_LT(dark.ChannelMeans()[1], 0.9);
                    Console.WriteLine("[OslVolume] furnace control with sigma_a 0.3: mean " + OslRenderTools.F(dark.ChannelMeans()));
                }
                WriteRow("c" + i + "_furnace", "WHITE FURNACE: " + names[i].ToUpperInvariant() + " [volpath]", p, "", "", "mean " + OslRenderTools.F(m) + " (must be 1). ");
            }
        }

        // The same scene with each integrator that supports media: the OSL medium against pbrt's homogeneous one (same integrator, same seed)
        private static void IntegratorRow(string integrator, string integParams, bool areaOnly, int spp, int seed, string idPrefix, double ratio)
        {
            float[] sa = new float[] { 0.1f, 0.2f, 0.4f }, ss = new float[] { 0.9f, 0.7f, 0.5f };
            string integ = "\"" + integrator + "\" " + integParams;
            string shapes = (areaOnly ? "" : Backdrop) + MediumSphere("m", 1.5f);
            double[] r = RenderPair(idPrefix, "OSL FOG VS HOMOGENEOUS", integrator, VolScene(integ, OslFogDecl("m", sa, ss, 0.3f), "", shapes, !areaOnly, areaOnly),
                                    VolScene(integ, HomogeneousDecl("m", sa, ss, 0.3f), "", shapes, !areaOnly, areaOnly), spp, seed);
            ExpectWithinNoise(r, ratio, 0.03);
        }

        [TEST("OslVolume", "IntegratorVolpath")]
        public static void IntegratorVolpath()
        {
            ResetScene();
            IntegratorRow("volpath", "\"integer maxdepth\" 30", false, 64, 23, "d1_volpath", 1.5);
        }

        [TEST("OslVolume", "IntegratorSimpleVolpath")]
        public static void IntegratorSimpleVolpath()
        {
            ResetScene();
            // simplevolpath: no delta lights and interface materials only (its own restrictions); the phase function is the only sampling strategy
            IntegratorRow("simplevolpath", "\"integer maxdepth\" 30", true, 256, 25, "d2_simplevolpath", 1.5);
        }

        [TEST("OslVolume", "IntegratorBdpt")]
        public static void IntegratorBdpt()
        {
            ResetScene();
            IntegratorRow("bdpt", "\"integer maxdepth\" 8 \"bool regularize\" false", false, 32, 27, "d3_bdpt", 2.0);
        }

        [TEST("OslVolume", "IntegratorMlt")]
        public static void IntegratorMlt()
        {
            ResetScene();
            IntegratorRow("mlt", "\"integer maxdepth\" 8", false, 32, 29, "d4_mlt", 3.0);
        }

        [TEST("OslVolume", "IntegratorsAgreeInTheMean")]
        public static void IntegratorsAgreeInTheMean()
        {
            ResetScene();
            // the cloud with the integrators that support media: volpath is the reference; the others agree in the mean
            string pre = "AttributeBegin\n  Translate -1 -1 -1\n  Scale 2 2 2\n";
            string decl = pre + "  MakeNamedMedium \"c\" \"string type\" \"osl\" \"string shader\" \"cloud.osl\" \"float density\" 6 \"point3 p0\" [0 0 0] \"point3 p1\" [1 1 1]\nAttributeEnd\n";
            string[] names = new string[] { "volpath", "simplevolpath", "bdpt" };
            string[] integ = new string[] { IntegratorText("volpath", 30), IntegratorText("simplevolpath", 30), IntegratorText("bdpt", 8) + " \"bool regularize\" false" };
            int[] spp = new int[] { 64, 256, 32 };
            double[][] means = new double[3][];
            for (int i = 0; i < 3; ++i)
            {
                bool simple = i == 1;
                string scene = VolScene(integ[i], decl, "", simple ? CloudShapes(1).Replace(Backdrop, "") : CloudShapes(1), !simple, simple);
                OslRender img = OslRenderTools.Render("e" + i + "_cloud_" + names[i], scene, spp[i], 31, "", OslVolumeShaders.Directory());
                ASSERT_TRUE(img != null);
                means[i] = img.ChannelMeans();
                string p = OslRenderTools.SaveLabelled(img, "e" + i + "_cloud_" + names[i], "OSL " + names[i], spp[i], 31, "NOISE CLOUD", 1f, OslVolumeShaders.OutputDir);
                WriteRow("e" + i + "_cloud_" + names[i], "NOISE CLOUD [" + names[i] + "]", p, "", "", simple ? "no delta lights; area light and infinite light only (not comparable to the others in value). " : "");
                Console.WriteLine("[OslVolume] cloud with " + names[i] + ": mean " + OslRenderTools.F(means[i]) + "\n    " + p);
            }
            // volpath and bdpt see the same scene
            for (int c = 0; c < 3; ++c)
            {
                EXPECT_LT(Math.Abs(means[0][c] - means[2][c]) / Math.Max(1e-3, means[0][c]), 0.06);
            }
        }
    }
}
