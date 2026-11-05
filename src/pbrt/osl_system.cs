// (not in pbrt-v4) Open Shading Language integration: the shading system of the renderer.
//
// One ShadingSystem is shared by all the OSL materials and textures of a scene. This file creates it (renderer services, texture system with the
// image readers of pbrt, the closures), finds and compiles the shaders (.oso on the search path; an .osl source is compiled with the compiler of the
// port, once), and keeps the options that decide how colors become spectra.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using OIIO;
using OSL;

namespace pbrt
{
    /// How the RGB colors of a shader (weights of closures, emission, parameters) become spectra
    public enum OSLSpectralMode
    {
        /// pbrt's rule: RGBAlbedoSpectrum when the values are in [0,1] (reflectances), RGBUnboundedSpectrum above; emission is an RGBIlluminantSpectrum
        Upsample,
        /// Always RGBUnboundedSpectrum (also for reflectances; the emission is unbounded and not multiplied by the illuminant of the color space)
        Unbounded,
        /// No spectral upsampling: the luminance-weighted average of the channels is the value at all wavelengths (a gray renderer)
        Gray
    }

    /// Collects the messages of the shading system and of the compiler; errors are also written to the console, as pbrt writes its own
    public sealed class OSLErrorHandler : ErrorHandler
    {
        public static List<string> messages = new List<string>();
        public static bool quiet = false;

        public override void handle(int errcode, string msg)
        {
            lock (messages)
            {
                if (messages.Count < 1000)
                {
                    messages.Add(msg);
                }
            }
            if (!quiet && errcode >= (int)ErrorCode.EH_WARNING)
            {
                Console.Error.WriteLine("OSL: " + msg);
            }
        }
    }

    public static unsafe class OSLSystem
    {
        public static OSLSpectralMode spectralMode = OSLSpectralMode.Upsample;
        /// Emission is an RGBIlluminantSpectrum (the way pbrt's lights take an RGB color) when true, an unbounded spectrum when false
        public static bool emissionIlluminant = true;
        /// The color space the RGB values of a shader are in
        public static RGBColorSpace colorSpace = null;

        private static readonly object mutex = new object();
        private static ShadingSystem shadingSystem = null;
        private static OSLRendererServices services = null;
        private static OSLErrorHandler errorHandler = new OSLErrorHandler();
        private static List<string> searchPaths = new List<string>();
        private static Dictionary<string, string> sourceCache = new Dictionary<string, string>();
        private static HashSet<string> loaded = new HashSet<string>();
        private static string stdoslPath = null;
        private static HashSet<string> emitters = new HashSet<string>();

        /// Whether the shader (its .oso text) mentions an emission closure: only these shaders are run to find the emission of a surface
        public static bool ShaderMayEmit(string shaderName)
        {
            lock (mutex)
            {
                return emitters.Contains(shaderName);
            }
        }

        private static void NoteEmitter(string shaderName, string osoText)
        {
            if (osoText.Contains("emission") || osoText.Contains("uniform_edf"))
            {
                emitters.Add(shaderName);
            }
        }

        public static ShadingSystem ShadingSystem
        {
            get
            {
                if (shadingSystem == null)
                {
                    lock (mutex)
                    {
                        if (shadingSystem == null)
                        {
                            Initialize();
                        }
                    }
                }
                return shadingSystem;
            }
        }

        public static OSLRendererServices Services
        {
            get
            {
                ShadingSystem unused = ShadingSystem;
                return services;
            }
        }

        public static RGBColorSpace ColorSpace()
        {
            return colorSpace != null ? colorSpace : RGBColorSpace.sRGB;
        }

        private static void Initialize()
        {
            ImageReaders.external_reader = ReadImage;
            SimpleTextureSystem texturesys = new SimpleTextureSystem();
            services = new OSLRendererServices(texturesys);
            ShadingSystem ss = new ShadingSystem(services, texturesys, errorHandler);
            OSLClosures.Register(ss);
            OSLRayType.Init(ss);
            ss.attribute("lockgeom", 1);
            ss.attribute("optimize", 2);
            ss.attribute("relaxed_param_typecheck", 1);
            string envPath = Environment.GetEnvironmentVariable("OSL_SHADER_PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                foreach (string p in envPath.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!searchPaths.Contains(p))
                    {
                        searchPaths.Add(p);
                    }
                }
            }
            ss.attribute("searchpath:shader", string.Join(":", searchPaths));
            shadingSystem = ss;
        }

        /// A directory (or several, separated by ':') in which shaders are looked for
        public static void AddSearchPath(string paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return;
            }
            lock (mutex)
            {
                foreach (string p in paths.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!searchPaths.Contains(p))
                    {
                        searchPaths.Add(p);
                    }
                }
                if (shadingSystem != null)
                {
                    shadingSystem.attribute("searchpath:shader", string.Join(":", searchPaths));
                }
            }
        }

        public static List<string> SearchPaths()
        {
            lock (mutex)
            {
                return new List<string>(searchPaths);
            }
        }

        /// The color of a spectral mode name ("upsample", "unbounded", "gray")
        public static bool ParseSpectralMode(string name, out OSLSpectralMode mode)
        {
            mode = OSLSpectralMode.Upsample;
            if (name == "upsample" || name == "rgb")
            {
                return true;
            }
            if (name == "unbounded")
            {
                mode = OSLSpectralMode.Unbounded;
                return true;
            }
            if (name == "gray" || name == "grey")
            {
                mode = OSLSpectralMode.Gray;
                return true;
            }
            return false;
        }

        private static string FindStdOsl()
        {
            if (stdoslPath != null)
            {
                return stdoslPath;
            }
            List<string> candidates = new List<string>();
            string env = Environment.GetEnvironmentVariable("OSL_STDOSL_PATH");
            if (!string.IsNullOrEmpty(env))
            {
                candidates.Add(env);
            }
            foreach (string dir in searchPaths)
            {
                candidates.Add(Path.Combine(dir, "stdosl.h"));
            }
            string baseDir = AppContext.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "stdosl.h"));
            candidates.Add(Path.Combine(baseDir, "shaders", "stdosl.h"));
            string oslHome = Environment.GetEnvironmentVariable("OSL_HOME");
            if (!string.IsNullOrEmpty(oslHome))
            {
                candidates.Add(Path.Combine(oslHome, "src", "shaders", "stdosl.h"));
                candidates.Add(Path.Combine(oslHome, "shaders", "stdosl.h"));
            }
            candidates.Add("/home/claude/OpenShadingLanguage/src/shaders/stdosl.h");
            foreach (string c in candidates)
            {
                if (File.Exists(c))
                {
                    stdoslPath = c;
                    return c;
                }
            }
            stdoslPath = "";
            return stdoslPath;
        }

        /// Makes the shader available to the shading system: a compiled <name>.oso on the search path is used as it is; when there is only an
        /// <name>.osl source it is compiled (once; the result is kept) with the compiler of the port. Returns the name to use in Shader().
        public static bool PrepareShader(string name, string sceneDirectory, out string shaderName, out string error)
        {
            error = "";
            shaderName = name;
            ShadingSystem ss = ShadingSystem;
            lock (mutex)
            {
                string key = name;
                if (name.EndsWith(".oso") || name.EndsWith(".osl"))
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(name));
                    shaderName = Path.GetFileNameWithoutExtension(name);
                    if (File.Exists(name))
                    {
                        AddSearchPath(dir);
                    }
                    else if (sceneDirectory != null && File.Exists(Path.Combine(sceneDirectory, name)))
                    {
                        AddSearchPath(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(sceneDirectory, name))));
                    }
                    key = shaderName;
                }
                if (loaded.Contains(key))
                {
                    return true;
                }
                List<string> dirs = new List<string>();
                if (sceneDirectory != null)
                {
                    dirs.Add(sceneDirectory);
                }
                dirs.AddRange(searchPaths);
                dirs.Add(".");
                string oso = null, osl = null;
                foreach (string d in dirs)
                {
                    string candidate = Path.Combine(d, shaderName + ".oso");
                    if (oso == null && File.Exists(candidate))
                    {
                        oso = candidate;
                    }
                    candidate = Path.Combine(d, shaderName + ".osl");
                    if (osl == null && File.Exists(candidate))
                    {
                        osl = candidate;
                    }
                }
                if (oso != null && (osl == null || File.GetLastWriteTimeUtc(oso) >= File.GetLastWriteTimeUtc(osl)))
                {
                    AddSearchPath(Path.GetDirectoryName(Path.GetFullPath(oso)));
                    NoteEmitter(shaderName, File.ReadAllText(oso));
                    loaded.Add(key);
                    return true;
                }
                if (osl == null)
                {
                    error = "Could not find shader \"" + shaderName + "\" (looked for " + shaderName + ".oso and " + shaderName + ".osl in the scene directory and in " +
                            "the shader search path)";
                    return false;
                }
                string source;
                try
                {
                    source = File.ReadAllText(osl);
                }
                catch (Exception e)
                {
                    error = e.Message;
                    return false;
                }
                string stdosl = FindStdOsl();
                OSLCompiler compiler = new OSLCompiler(errorHandler);
                List<string> options = new List<string>();
                options.Add("-I" + Path.GetDirectoryName(Path.GetFullPath(osl)));
                if (stdosl.Length > 0)
                {
                    options.Add("-I" + Path.GetDirectoryName(stdosl));
                }
                string osobuffer;
                if (!compiler.compile_buffer(source, out osobuffer, options, stdosl, osl))
                {
                    error = "Could not compile shader \"" + osl + "\"";
                    return false;
                }
                if (!ss.LoadMemoryCompiledShader(shaderName, osobuffer))
                {
                    error = "Could not load the compiled shader \"" + shaderName + "\"";
                    return false;
                }
                sourceCache[shaderName] = osl;
                NoteEmitter(shaderName, osobuffer);
                loaded.Add(key);
                return true;
            }
        }

        // The decoder of the image formats of pbrt for the texture system of OSL: everything pbrt reads (EXR, PNG, PFM, HDR, QOI) is readable by
        // OSL textures. The values are not converted (8 bit data stays encoded, as OIIO hands it to the texture system).
        public static ImageFileData ReadImage(string filename)
        {
            if (!File.Exists(filename))
            {
                return null;
            }
            ImageAndMetadata im;
            try
            {
                im = Image.Read(filename, null, ColorEncoding.Linear);
            }
            catch (Exception)
            {
                return null;
            }
            Image image = im.image;
            if (image == null)
            {
                return null;
            }
            int w = image.Resolution().x, h = image.Resolution().y, nc = image.NChannels();
            if (w <= 0 || h <= 0 || nc <= 0)
            {
                return null;
            }
            float[] pixels = new float[w * h * nc];
            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    for (int c = 0; c < nc; ++c)
                    {
                        pixels[(y * w + x) * nc + c] = image.GetChannel(new Point2i(x, y), c);
                    }
                }
            }
            ImageSpecData spec = new ImageSpecData();
            spec.width = spec.full_width = w;
            spec.height = spec.full_height = h;
            spec.nchannels = nc;
            spec.format = TypeDesc.FLOAT;
            List<string> names = image.ChannelNames();
            for (int c = 0; c < names.Count; ++c)
            {
                if (names[c] == "A")
                {
                    spec.alpha_channel = c;
                }
            }
            ImageFileData file = new ImageFileData();
            file.format_name = "pbrt";
            ImageSubimageData sub = new ImageSubimageData();
            ImageLevelData level = new ImageLevelData();
            level.spec = spec;
            level.pixels = pixels;
            sub.levels.Add(level);
            file.subimages.Add(sub);
            return file;
        }

        // The conversions of the colors of a shader to spectra

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Float Luminance(Float r, Float g, Float b)
        {
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        /// The spectrum of a reflectance or a weight (rho) that is an RGB color
        public static SampledSpectrum ToSpectrum(Float r, Float g, Float b, SampledWavelengths lambda)
        {
            r = std.max<Float>(r, 0);
            g = std.max<Float>(g, 0);
            b = std.max<Float>(b, 0);
            if (r == g && g == b)
            {
                return new SampledSpectrum(r);
            }
            switch (spectralMode)
            {
                case OSLSpectralMode.Gray:
                    return new SampledSpectrum(Luminance(r, g, b));
                case OSLSpectralMode.Unbounded:
                    return new RGBUnboundedSpectrum(ColorSpace(), new RGB(r, g, b)).Sample(lambda);
                default:
                    if (r <= 1 && g <= 1 && b <= 1)
                    {
                        return new RGBAlbedoSpectrum(ColorSpace(), new RGB(r, g, b)).Sample(lambda);
                    }
                    return new RGBUnboundedSpectrum(ColorSpace(), new RGB(r, g, b)).Sample(lambda);
            }
        }

        /// The spectrum of emitted radiance (or of a value that scales an illuminant)
        private static RGBColorSpace photometricColorSpace = null;
        private static Float illuminantScale = 1;
        private static Float flatScale = 1;

        /// As the lights of pbrt, an emission is normalized by the photometric value of its reference spectrum (the illuminant of the color space for an
        /// RGBIlluminantSpectrum, a constant spectrum for the others), so that an emission() of RGB (r, g, b) is displayed as (r, g, b) as the L of an
        /// area light is
        private static void UpdatePhotometricScales()
        {
            RGBColorSpace cs = ColorSpace();
            if (!object.ReferenceEquals(cs, photometricColorSpace))
            {
                illuminantScale = 1 / pbrt.SpectrumToPhotometric(cs.illuminant);
                flatScale = 1 / pbrt.SpectrumToPhotometric(new ConstantSpectrum(1));
                photometricColorSpace = cs;
            }
        }

        public static SampledSpectrum ToEmissionSpectrum(Float r, Float g, Float b, SampledWavelengths lambda)
        {
            r = std.max<Float>(r, 0);
            g = std.max<Float>(g, 0);
            b = std.max<Float>(b, 0);
            UpdatePhotometricScales();
            switch (spectralMode)
            {
                case OSLSpectralMode.Gray:
                    return new SampledSpectrum(Luminance(r, g, b) * flatScale);
                case OSLSpectralMode.Unbounded:
                    return new RGBUnboundedSpectrum(ColorSpace(), new RGB(r, g, b)).Sample(lambda) * flatScale;
                default:
                    if (emissionIlluminant)
                    {
                        return new RGBIlluminantSpectrum(ColorSpace(), new RGB(r, g, b)).Sample(lambda) * illuminantScale;
                    }
                    return new RGBUnboundedSpectrum(ColorSpace(), new RGB(r, g, b)).Sample(lambda) * flatScale;
            }
        }

        /// The RGB of a spectrum texture value at the sampled wavelengths (for a texture that a shader reads as a color): the spectrum is
        /// projected on the sensor of the color space through the XYZ matching functions
        public static void ToRGB(SampledSpectrum s, SampledWavelengths lambda, out Float r, out Float g, out Float b)
        {
            XYZ xyz = s.ToXYZ(lambda);
            RGB rgb = ColorSpace().ToRGB(xyz);
            r = rgb.r;
            g = rgb.g;
            b = rgb.b;
        }
    }
}
