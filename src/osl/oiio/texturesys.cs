// OSL port: OpenImageIO's TextureSystem (libtexture: texturesys.cpp, environment.cpp, and the parts of imagecache.cpp that describe a texture file).
//
// This is the default TextureSystem that RendererServices uses. It is a port of the lookup code of OIIO 3.1: texture() with its MIP level selection,
// anisotropic ellipse sampling, closest / bilinear / bicubic (B-spline) probes, the wrap modes, the 'fill' and 'missingcolor' options, crop windows, gray to
// RGB promotion, lat-long environment() with the fade to the pole colour, UDIM resolution and get_texture_info(). The floating point operations are in the
// same order as the C++ (which does not use FMA on a default x86-64 build), so results agree with OIIO to the last bit in the cases that were compared.
//
// Differences from OIIO (all of them because the image cache is replaced by whole images in memory):
//  * There are no tiles and no cache limits: a file is decoded on first use (ImageReaders) and every MIP level stays in memory as floats. Pixel values are
//    those OIIO's cache hands to the filter (8 and 16 bit values scaled by 1/255 and 1/65535, half widened). "automip" levels of a non MIP-mapped file are
//    made as ImageCacheFile::read_unmipped does (a bilinear tap at the texel centres of the finer level, stored back in the file's pixel format).
//  * "autotile" only decides the tile size of untiled files, which matters for the fade to the pole colour (only levels that fit one tile get it).
//  * Stochastic (rnd >= 0 with "stochastic" strategies) sampling and texture3d() are not implemented; no 3D texture format is readable. Colour transforms
//    ("colorspace" option) are not supported.
//  * Formats: see imageinput.cs; a host can plug a decoder for other formats into ImageReaders.external_reader.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using OSL;

namespace OIIO
{
    /// 4 floats with the elementwise float arithmetic of OIIO's simd::vfloat4
    public struct Float4
    {
        public float x, y, z, w;

        public Float4(float a, float b, float c, float d)
        {
            x = a;
            y = b;
            z = c;
            w = d;
        }

        public Float4(float a)
        {
            x = a;
            y = a;
            z = a;
            w = a;
        }

        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return x;
                    case 1: return y;
                    case 2: return z;
                    default: return w;
                }
            }
            set
            {
                switch (i)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    default: w = value; break;
                }
            }
        }

        public static Float4 operator +(Float4 a, Float4 b)
        {
            return new Float4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        }

        public static Float4 operator -(Float4 a, Float4 b)
        {
            return new Float4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        }

        public static Float4 operator -(Float4 a)
        {
            return new Float4(-a.x, -a.y, -a.z, -a.w);
        }

        public static Float4 operator *(Float4 a, Float4 b)
        {
            return new Float4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        }

        public static Float4 operator *(Float4 a, float b)
        {
            return new Float4(a.x * b, a.y * b, a.z * b, a.w * b);
        }

        public static Float4 operator *(float a, Float4 b)
        {
            return new Float4(a * b.x, a * b.y, a * b.z, a * b.w);
        }

        /// fmath.h lerp(v0, v1, x) = v0*(1-x) + v1*x
        public static Float4 lerp(Float4 v0, Float4 v1, float x)
        {
            float omx = 1.0f - x;
            return new Float4(v0.x * omx + v1.x * x, v0.y * omx + v1.y * x, v0.z * omx + v1.z * x, v0.w * omx + v1.w * x);
        }

        /// fmath.h bilerp(v0, v1, v2, v3, s, t) = (1-t)*(v0*(1-s) + v1*s) + t*(v2*(1-s) + v3*s)
        public static Float4 bilerp(Float4 v0, Float4 v1, Float4 v2, Float4 v3, float s, float t)
        {
            float s1 = 1.0f - s;
            float t1 = 1.0f - t;
            Float4 a = new Float4(v0.x * s1 + v1.x * s, v0.y * s1 + v1.y * s, v0.z * s1 + v1.z * s, v0.w * s1 + v1.w * s);
            Float4 b = new Float4(v2.x * s1 + v3.x * s, v2.y * s1 + v3.y * s, v2.z * s1 + v3.z * s, v2.w * s1 + v3.w * s);
            return new Float4(t1 * a.x + t * b.x, t1 * a.y + t * b.y, t1 * a.z + t * b.z, t1 * a.w + t * b.w);
        }

        /// zero the channels at and above n (blend0 with channel_masks[n])
        public Float4 masked(int n)
        {
            return new Float4(n > 0 ? x : 0.0f, n > 1 ? y : 0.0f, n > 2 ? z : 0.0f, n > 3 ? w : 0.0f);
        }

        /// keep only the channels at and above n (blend0not with channel_masks[n])
        public Float4 masked_not(int n)
        {
            return new Float4(n > 0 ? 0.0f : x, n > 1 ? 0.0f : y, n > 2 ? 0.0f : z, n > 3 ? 0.0f : w);
        }
    }

    /// ImageCacheFile::ImageDims plus the per level data of ImageCacheFile::LevelInfo, with the pixels of the level
    public sealed class ImageLevelInfo
    {
        public int x, y;
        public int width, height;
        public int full_x, full_y, full_width, full_height;
        public int tile_width, tile_height;
        public int nchannels;
        public bool full_pixel_range;
        public bool onetile;
        public float[] pixels;
        public float[] polecolor;
        public bool polecolorcomputed;

        public static bool has_full_pixel_range(ImageSpecData s)
        {
            return s.x == s.full_x && s.y == s.full_y && s.width == s.full_width && s.height == s.full_height;
        }

        public static ImageLevelInfo convert(ImageSpecData s, float[] pixels)
        {
            ImageLevelInfo l = new ImageLevelInfo();
            l.x = s.x;
            l.y = s.y;
            l.width = s.width;
            l.height = s.height;
            l.full_x = s.full_x;
            l.full_y = s.full_y;
            l.full_width = s.full_width;
            l.full_height = s.full_height;
            l.tile_width = s.tile_width;
            l.tile_height = s.tile_height;
            l.nchannels = s.nchannels;
            l.pixels = pixels;
            l.full_pixel_range = has_full_pixel_range(s);
            l.onetile = s.width <= s.tile_width && s.height <= s.tile_height;
            return l;
        }
    }

    /// ImageCacheFile::SubimageInfo
    public sealed class ImageSubimageInfo
    {
        public List<ImageLevelInfo> levels = new List<ImageLevelInfo>();
        public ImageSpecData spec;
        public int datatype = TypeDesc.FLOAT;
        public bool untiled;
        public bool unmipped;
        public bool full_pixel_range = true;
        public float sscale = 1.0f, soffset = 0.0f, tscale = 1.0f, toffset = 0.0f;
        public ustring subimagename;
        public bool is_constant_image;
        public bool has_average_color;
        public List<float> average_color = new List<float>();
        public int n_mip_levels;
        public int min_mip_level;
        public int[] minwh;

        public ImageLevelInfo leveldims(int m)
        {
            return levels[m];
        }
    }

    /// A texture file as the texture system sees it (ImageCacheFile)
    public sealed class ImageTextureHandle : TextureHandle
    {
        public string resolved_filename;
        public bool broken;
        public string broken_message = "";
        public bool opened;
        public List<ImageSubimageInfo> subimages = new List<ImageSubimageInfo>();
        public int texformat = 1;
        public int swrap = TextureOpt.WrapBlack;
        public int twrap = TextureOpt.WrapBlack;
        public int rwrap = TextureOpt.WrapBlack;
        public int envlayout = ImageTextureSystem.LayoutTexture;
        public bool y_up = true;
        public bool sample_border;
        public string fileformat = "";
        public ustring fingerprint;
        public int errors_issued;
        public object open_lock = new object();
        public int udim_nutiles, udim_nvtiles;
        public ImageTextureHandle[] udim_lookup_handles;
        public string[] udim_lookup_names;

        public override bool good()
        {
            return opened && !broken;
        }

        public bool is_udim()
        {
            return udim_nutiles > 0 || udim_nvtiles > 0;
        }

        public ImageSubimageInfo subimageinfo(int s)
        {
            return subimages[s];
        }

        public int miplevels(int s)
        {
            return subimages[s].levels.Count;
        }

        public bool errors_should_issue()
        {
            return Interlocked.Increment(ref errors_issued) <= 100;
        }
    }

    /// The default TextureSystem (see the header comment)
    public unsafe class ImageTextureSystem : TextureSystem
    {
        public const int LayoutTexture = 0, LayoutLatLong = 1, LayoutCubeThreeByTwo = 2, LayoutCubeOneBySix = 3;
        public const int TexFormatUnknown = 0, TexFormatTexture = 1, TexFormatTexture3d = 2, TexFormatShadow = 3, TexFormatCubeFaceShadow = 4,
                         TexFormatVolumeShadow = 5, TexFormatLatLongEnv = 6, TexFormatCubeFaceEnv = 7, TexFormatLast = 8;

        private static readonly string[] texture_format_names = new string[]
        {
            "unknown", "Plain Texture", "Volume Texture", "Shadow", "CubeFace Shadow", "Volume Shadow", "LatLong Environment", "CubeFace Environment", ""
        };

        private static readonly string[] texture_type_names = new string[]
        {
            "unknown", "Plain Texture", "Volume Texture", "Shadow", "Shadow", "Shadow", "Environment", "Environment", ""
        };

        private readonly Dictionary<string, ImageTextureHandle> m_files = new Dictionary<string, ImageTextureHandle>(StringComparer.Ordinal);
        private readonly object m_lock = new object();
        private readonly ThreadLocal<string> m_error = new ThreadLocal<string>(() => "");
        private readonly ThreadLocal<string> m_cache_error = new ThreadLocal<string>(() => "");

        private bool m_automip;
        private int m_autotile;
        private bool m_autoscanline;
        private int m_max_mip_res = 1 << 30;
        private bool m_gray_to_rgb;
        private bool m_flip_t;
        private int m_max_tile_channels = 6;
        private bool m_latlong_y_up_default = true;
        private string m_searchpath = "";

        public ImageTextureSystem()
        {
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Attributes
        // ------------------------------------------------------------------------------------------------------------------------------------

        public bool attribute(string name, TypeDesc type, object val)
        {
            if (type == TypeDescs.TypeInt && val is int)
            {
                return attribute(name, (int)val);
            }
            if (type == TypeDescs.TypeFloat && val is float)
            {
                return attribute(name, (float)val);
            }
            if (type == TypeDescs.TypeString && val is string)
            {
                return attribute(name, (string)val);
            }
            return false;
        }

        public bool attribute(string name, int val)
        {
            lock (m_lock)
            {
                switch (name)
                {
                    case "automip": m_automip = val != 0; return true;
                    case "autotile": m_autotile = val; return true;
                    case "autoscanline": m_autoscanline = val != 0; return true;
                    case "max_mip_res": m_max_mip_res = val; return true;
                    case "gray_to_rgb": m_gray_to_rgb = val != 0; return true;
                    case "flip_t": m_flip_t = val != 0; return true;
                    case "max_tile_channels": m_max_tile_channels = val; return true;
                    case "latlong_up": return true;
                }
            }
            return false;
        }

        public bool attribute(string name, float val)
        {
            return false;
        }

        public bool attribute(string name, string val)
        {
            lock (m_lock)
            {
                if (name == "searchpath")
                {
                    m_searchpath = val;
                    return true;
                }
                if (name == "latlong_up")
                {
                    m_latlong_y_up_default = (val == "y");
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Errors
        // ------------------------------------------------------------------------------------------------------------------------------------

        private void error(string message)
        {
            if (message.Length > 0 && message[message.Length - 1] == '\n')
            {
                message = message.Substring(0, message.Length - 1);
            }
            string e = m_error.Value;
            if (e.Length > 0 && e[e.Length - 1] != '\n')
            {
                e += "\n";
            }
            m_error.Value = e + message;
        }

        public string geterror(bool clear = true)
        {
            string e = m_error.Value;
            if (clear)
            {
                m_error.Value = "";
            }
            return e;
        }

        private void cache_error(string message)
        {
            string e = m_cache_error.Value;
            if (e.Length > 0 && e[e.Length - 1] != '\n')
            {
                e += "\n";
            }
            m_cache_error.Value = e + message;
        }

        private string cache_geterror()
        {
            string e = m_cache_error.Value;
            m_cache_error.Value = "";
            return e;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Files
        // ------------------------------------------------------------------------------------------------------------------------------------

        public TexturePerthread get_perthread_info(TexturePerthread thread_info = null)
        {
            return thread_info != null ? thread_info : new TexturePerthread();
        }

        private string resolve_filename(string filename)
        {
            if (File.Exists(filename) || Path.IsPathRooted(filename) || m_searchpath.Length == 0)
            {
                return filename;
            }
            foreach (string dir in m_searchpath.Split(':'))
            {
                if (dir.Length == 0)
                {
                    continue;
                }
                string candidate = Path.Combine(dir, filename);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return filename;
        }

        public TextureHandle get_texture_handle(ustring filename, TexturePerthread thread_info, TextureOpt* options = null)
        {
            return find_texturefile(filename.@string());
        }

        private ImageTextureHandle find_texturefile(string filename)
        {
            lock (m_lock)
            {
                ImageTextureHandle h;
                if (m_files.TryGetValue(filename, out h))
                {
                    return h;
                }
                h = new ImageTextureHandle();
                h.filename = new ustring(filename);
                h.resolved_filename = resolve_filename(filename);
                udim_setup(h);
                m_files[filename] = h;
                return h;
            }
        }

        /// Add a decoded image under a name (a host or a test that has the pixels already)
        public void add_file(string filename, ImageFileData data)
        {
            lock (m_lock)
            {
                ImageTextureHandle h = new ImageTextureHandle();
                h.filename = new ustring(filename);
                h.resolved_filename = filename;
                finish_open(h, data);
                m_files[filename] = h;
            }
        }

        public bool good(TextureHandle texture_handle)
        {
            ImageTextureHandle h = texture_handle as ImageTextureHandle;
            if (h == null)
            {
                return false;
            }
            verify_file(h, true);
            return h.good();
        }

        public bool is_udim(TextureHandle texture_handle)
        {
            ImageTextureHandle h = texture_handle as ImageTextureHandle;
            return h != null && h.is_udim();
        }

        public TextureHandle resolve_udim(TextureHandle udim_handle, TexturePerthread thread_info, float s, float t)
        {
            ImageTextureHandle h = udim_handle as ImageTextureHandle;
            int utile = Math.Max(0, (int)s);
            int vtile = Math.Max(0, (int)t);
            return resolve_udim_tile(h, utile, vtile);
        }

        private ImageTextureHandle resolve_udim_tile(ImageTextureHandle udimfile, int utile, int vtile)
        {
            if (udimfile == null || !udimfile.is_udim())
            {
                return udimfile;
            }
            if (utile < 0 || utile >= udimfile.udim_nutiles || vtile < 0 || vtile >= udimfile.udim_nvtiles)
            {
                return null;
            }
            int index = utile + vtile * udimfile.udim_nutiles;
            string name = udimfile.udim_lookup_names[index];
            if (name == null || name.Length == 0)
            {
                return null;
            }
            ImageTextureHandle real = udimfile.udim_lookup_handles[index];
            if (real == null)
            {
                real = find_texturefile(name);
                udimfile.udim_lookup_handles[index] = real;
            }
            return real;
        }

        private static string udim_to_wildcard(string udimpattern)
        {
            // Filesystem::filename_to_regex: escape the characters that are special in a regex
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in udimpattern)
            {
                if (c == '.' || c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}' || c == '+' || c == '^' || c == '$' || c == '|' ||
                    c == '\\')
                {
                    sb.Append('\\');
                }
                sb.Append(c);
            }
            string result = sb.ToString();
            result = result.Replace("<UDIM>", "([0-9]{4})");
            result = result.Replace("<u>", "u([0-9]+)");
            result = result.Replace("<v>", "v([0-9]+)");
            result = result.Replace("<U>", "u([0-9]+)");
            result = result.Replace("<V>", "v([0-9]+)");
            result = result.Replace("_u##v##", "_u([0-9]+)v([0-9]+)");
            result = result.Replace("<uvtile>", "u([0-9]+)_v([0-9]+)");
            result = result.Replace("<UVTILE>", "u([0-9]+)_v([0-9]+)");
            result = result.Replace("%\\(UDIM\\)d", "([0-9]{4})");
            return result;
        }

        private void udim_setup(ImageTextureHandle h)
        {
            h.udim_nutiles = 0;
            h.udim_nvtiles = 0;
            string filename = h.resolved_filename;
            if (File.Exists(filename))
            {
                return;
            }
            bool udim1 = filename.Contains("<UDIM>") || filename.Contains("%(UDIM)d");
            bool udim2_0 = filename.Contains("<u>") || filename.Contains("<v>") || filename.Contains("<uvtile>") || filename.Contains("_u##v##");
            bool udim2_1 = filename.Contains("<U>") || filename.Contains("<V>") || filename.Contains("<UVTILE>");
            bool udim2 = udim2_0 | udim2_1;
            if (!(udim1 | udim2))
            {
                return;
            }
            string dirname = Path.GetDirectoryName(filename);
            if (string.IsNullOrEmpty(dirname))
            {
                dirname = ".";
            }
            string pat = udim_to_wildcard(Path.GetFileName(filename));
            Regex decoder = new Regex("^(?:" + pat + ")$", RegexOptions.CultureInvariant);
            List<KeyValuePair<string, int[]>> list = new List<KeyValuePair<string, int[]>>();
            int nu = 0, nv = 0;
            string[] entries;
            try
            {
                entries = Directory.Exists(dirname) ? Directory.GetFiles(dirname) : new string[0];
            }
            catch (Exception)
            {
                entries = new string[0];
            }
            Array.Sort(entries, StringComparer.Ordinal);
            foreach (string entry in entries)
            {
                string fnpart = Path.GetFileName(entry);
                Match match = decoder.Match(fnpart);
                if (!match.Success || match.Groups.Count <= 1)
                {
                    continue;
                }
                int u = int.Parse(match.Groups[1].Value);
                int v = 0;
                if (udim1)
                {
                    v = (u - 1001) / 10;
                    u = (u - 1001) % 10;
                }
                else
                {
                    if (match.Groups.Count < 3)
                    {
                        continue;
                    }
                    v = int.Parse(match.Groups[2].Value);
                    if (udim2_1)
                    {
                        u -= 1;
                        v -= 1;
                    }
                }
                if (u < 0 || v < 0)
                {
                    continue;
                }
                list.Add(new KeyValuePair<string, int[]>(dirname == "." && !filename.StartsWith("./") ? fnpart : Path.Combine(dirname, fnpart), new int[] { u, v }));
                nu = Math.Max(nu, u + 1);
                nv = Math.Max(nv, v + 1);
            }
            h.udim_nutiles = nu;
            h.udim_nvtiles = nv;
            h.udim_lookup_names = new string[nu * nv];
            h.udim_lookup_handles = new ImageTextureHandle[nu * nv];
            foreach (KeyValuePair<string, int[]> kv in list)
            {
                h.udim_lookup_names[kv.Value[1] * nu + kv.Value[0]] = kv.Key;
            }
            if (nu == 0 && nv == 0)
            {
                // a pattern with no matching file is still a (empty) UDIM set
                h.udim_nutiles = 0;
                h.udim_nvtiles = 0;
            }
        }

        public static string texture_format_name(int f)
        {
            return texture_format_names[f];
        }

        public static string texture_type_name(int f)
        {
            return texture_type_names[f];
        }

        /// ImageCacheImpl::verify_file: open the file the first time it is needed. Returns false for a broken file.
        private void verify_file(ImageTextureHandle h, bool header_only)
        {
            if (h.is_udim())
            {
                return;
            }
            if (h.opened || h.broken)
            {
                return;
            }
            lock (h.open_lock)
            {
                if (h.opened || h.broken)
                {
                    return;
                }
                string error;
                ImageFileData data = load_file(h.resolved_filename, out error);
                if (data == null)
                {
                    if (h.resolved_filename.Length > 0 && !File.Exists(h.resolved_filename) && error == "Could not open file")
                    {
                        error = "Could not open file: " + h.resolved_filename + ": No such file or directory";
                    }
                    mark_broken(h, error);
                    return;
                }
                finish_open(h, data);
            }
        }

        /// Read the file (a point where a derived class can substitute its own source of images)
        protected virtual ImageFileData load_file(string filename, out string error)
        {
            return ImageReaders.read(filename, out error);
        }

        private void mark_broken(ImageTextureHandle h, string error)
        {
            h.broken = true;
            if (error.Length == 0)
            {
                error = "unknown error";
            }
            h.broken_message = error;
            cache_error(error);
        }

        private static int round_to_multiple(int x, int m)
        {
            return ((x + m - 1) / m) * m;
        }

        private void finish_open(ImageTextureHandle h, ImageFileData data)
        {
            h.fileformat = data.format_name;
            bool first_subimage = true;
            foreach (ImageSubimageData sd in data.subimages)
            {
                ImageSubimageInfo si = new ImageSubimageInfo();
                ImageSpecData sispec = sd.levels[0].spec.clone();
                bool clamp_texture_dims = false;
                if (first_subimage)
                {
                    clamp_texture_dims = init_texture_format(h, sispec);
                    if (clamp_texture_dims)
                    {
                        clamp_dimensions(sispec);
                    }
                }
                else
                {
                    clamp_texture_dims = h.texformat == TexFormatTexture && sispec.attributes.ContainsKey("textureformat");
                }
                si.spec = sispec;
                subimage_init(si, sispec);
                int nmip = 0;
                ImageSpecData tempspec = null;
                foreach (ImageLevelData lv in sd.levels)
                {
                    tempspec = lv.spec.clone();
                    if (tempspec.tile_width == 0 || tempspec.tile_height == 0)
                    {
                        si.untiled = true;
                        int autotile = m_autotile;
                        int rps = tempspec.get_int_attribute("tiff:RowsPerStrip", 0);
                        if (rps > 1)
                        {
                            autotile = round_to_multiple(Math.Max(autotile, 64), rps);
                        }
                        if (autotile != 0)
                        {
                            if (m_autoscanline || rps > 1)
                            {
                                tempspec.tile_width = tempspec.width;
                            }
                            else
                            {
                                tempspec.tile_width = Math.Min(tempspec.width, autotile);
                            }
                            tempspec.tile_height = Math.Min(tempspec.height, autotile);
                        }
                        else
                        {
                            tempspec.tile_width = tempspec.width;
                            tempspec.tile_height = tempspec.height;
                        }
                    }
                    if (tempspec.width > m_max_mip_res || tempspec.height > m_max_mip_res)
                    {
                        si.min_mip_level = nmip + 1;
                    }
                    if (nmip > 0 && tempspec.nchannels != sispec.nchannels)
                    {
                        mark_broken(h, "Subimages don't all have the same number of channels");
                        return;
                    }
                    if (clamp_texture_dims)
                    {
                        clamp_dimensions(tempspec);
                    }
                    si.levels.Add(ImageLevelInfo.convert(tempspec, lv.pixels));
                    ++nmip;
                }
                // Special work for non-MIPmapped images, if automip is on and it doesn't have a "textureformat" attribute
                if (nmip == 1 && (tempspec.width > 1 || tempspec.height > 1))
                {
                    si.unmipped = true;
                }
                if (si.unmipped && m_automip && !tempspec.attributes.ContainsKey("textureformat"))
                {
                    int w = tempspec.full_width;
                    int hh = tempspec.full_height;
                    while (w > 1 || hh > 1)
                    {
                        w = Math.Max(1, w / 2);
                        hh = Math.Max(1, hh / 2);
                        ImageSpecData s = tempspec.clone();
                        s.width = w;
                        s.height = hh;
                        s.full_width = w;
                        s.full_height = hh;
                        if (m_autotile != 0)
                        {
                            if (m_autoscanline)
                            {
                                s.tile_width = w;
                            }
                            else
                            {
                                s.tile_width = Math.Min(m_autotile, w);
                            }
                            s.tile_height = Math.Min(m_autotile, hh);
                        }
                        else
                        {
                            s.tile_width = w;
                            s.tile_height = hh;
                        }
                        ++nmip;
                        ImageLevelInfo prevlevel = si.levels[si.levels.Count - 1];
                        si.levels.Add(make_automip_level(si, ImageLevelInfo.convert(s, null), prevlevel));
                    }
                }
                si.n_mip_levels = nmip;
                si.min_mip_level = Math.Min(si.min_mip_level, nmip - 1);
                si.minwh = new int[nmip + 1];
                for (int m = 0; m < nmip; ++m)
                {
                    si.minwh[m] = Math.Min(si.levels[m].width, si.levels[m].height);
                }
                si.minwh[nmip] = 0;
                h.subimages.Add(si);
                first_subimage = false;
            }
            init_from_spec(h);
            h.opened = true;
        }

        private static void clamp_dimensions(ImageSpecData spec)
        {
            if (spec.full_width > spec.width)
            {
                spec.full_width = spec.width;
            }
            if (spec.full_height > spec.height)
            {
                spec.full_height = spec.height;
            }
        }

        private bool init_texture_format(ImageTextureHandle h, ImageSpecData spec)
        {
            bool clamp_texture_dims = false;
            h.texformat = TexFormatTexture;
            string tf = spec.get_string_attribute("textureformat", null);
            if (tf != null)
            {
                for (int i = 0; i < TexFormatLast; ++i)
                {
                    if (string.Equals(tf, texture_format_names[i], StringComparison.OrdinalIgnoreCase))
                    {
                        h.texformat = i;
                        break;
                    }
                }
                clamp_texture_dims = (h.texformat == TexFormatTexture);
            }
            return clamp_texture_dims;
        }

        private static List<float> parse_float_list(string s)
        {
            List<float> r = new List<float>();
            int pos = 0;
            while (pos < s.Length)
            {
                int start = pos;
                while (pos < s.Length && s[pos] != ',')
                {
                    ++pos;
                }
                float val;
                if (!float.TryParse(s.Substring(start, pos - start).Trim(), System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out val))
                {
                    break;
                }
                r.Add(val);
                if (pos < s.Length && s[pos] == ',')
                {
                    ++pos;
                }
                else
                {
                    break;
                }
            }
            return r;
        }

        private void subimage_init(ImageSubimageInfo si, ImageSpecData spec)
        {
            si.full_pixel_range = ImageLevelInfo.has_full_pixel_range(spec);
            if (!si.full_pixel_range)
            {
                si.sscale = (float)spec.full_width / spec.width;
                si.soffset = (float)(spec.full_x - spec.x) / spec.width;
                si.tscale = (float)spec.full_height / spec.height;
                si.toffset = (float)(spec.full_y - spec.y) / spec.height;
            }
            si.subimagename = new ustring(spec.get_string_attribute("oiio:subimagename"));
            si.datatype = TypeDesc.FLOAT;
            if (spec.format == TypeDesc.UINT8 || spec.format == TypeDesc.UINT16 || spec.format == TypeDesc.HALF)
            {
                si.datatype = spec.format;
            }
            string software = spec.get_string_attribute("Software");
            bool from_maketx = software.StartsWith("OpenImageIO", StringComparison.OrdinalIgnoreCase) ||
                               software.StartsWith("maketx", StringComparison.OrdinalIgnoreCase);
            string constant_color = spec.get_string_attribute("oiio:ConstantColor");
            if (from_maketx && constant_color.Length > 0)
            {
                si.average_color.AddRange(parse_float_list(constant_color));
                if (si.average_color.Count == spec.nchannels)
                {
                    si.is_constant_image = true;
                    si.has_average_color = true;
                }
            }
            string avgcolor = spec.get_string_attribute("oiio:AverageColor");
            if (from_maketx && avgcolor.Length > 0)
            {
                si.average_color.AddRange(parse_float_list(avgcolor));
                if (si.average_color.Count == spec.nchannels)
                {
                    si.has_average_color = true;
                }
            }
        }

        private static int decode_wrapmode(string name)
        {
            switch (name)
            {
                case "default": return TextureOpt.WrapDefault;
                case "black": return TextureOpt.WrapBlack;
                case "clamp": return TextureOpt.WrapClamp;
                case "periodic": return TextureOpt.WrapPeriodic;
                case "mirror": return TextureOpt.WrapMirror;
                case "periodic_pow2": return TextureOpt.WrapPeriodicPow2;
                case "periodic_sharedborder": return TextureOpt.WrapPeriodicSharedBorder;
                default: return TextureOpt.WrapDefault;
            }
        }

        private void init_from_spec(ImageTextureHandle h)
        {
            ImageSubimageInfo si = h.subimages[0];
            ImageSpecData spec = si.spec;
            ImageLevelInfo dims = si.levels[0];
            string wrapmodes = spec.get_string_attribute("wrapmodes", null);
            if (wrapmodes != null)
            {
                int comma = wrapmodes.IndexOf(',');
                string sw = comma >= 0 ? wrapmodes.Substring(0, comma) : wrapmodes;
                string tw = comma >= 0 ? wrapmodes.Substring(comma + 1) : sw;
                h.swrap = decode_wrapmode(sw);
                h.twrap = decode_wrapmode(tw);
                h.rwrap = h.swrap;
            }
            h.y_up = m_latlong_y_up_default;
            h.sample_border = false;
            if (h.texformat == TexFormatLatLongEnv || h.texformat == TexFormatCubeFaceEnv || h.texformat == TexFormatCubeFaceShadow)
            {
                string up = spec.get_string_attribute("oiio:updirection");
                if (up == "y")
                {
                    h.y_up = true;
                }
                else if (up == "z")
                {
                    h.y_up = false;
                }
                if (spec.get_int_attribute("oiio:sampleborder") != 0)
                {
                    h.sample_border = true;
                }
            }
            if (h.texformat == TexFormatCubeFaceEnv || h.texformat == TexFormatCubeFaceShadow)
            {
                int w = Math.Max(dims.full_width, dims.tile_width);
                int hh = Math.Max(dims.full_height, dims.tile_height);
                if (dims.width == 3 * w && dims.height == 2 * hh)
                {
                    h.envlayout = LayoutCubeThreeByTwo;
                }
                else if (dims.width == w && dims.height == 6 * hh)
                {
                    h.envlayout = LayoutCubeOneBySix;
                }
                else
                {
                    h.envlayout = LayoutTexture;
                }
            }
            // check_texture_metadata_sanity (note that the C++ erases the misspelled "oiio::ConstantColor" / "oiio::AverageColor", which are never set)
            string software = spec.get_string_attribute("Software");
            string textureformat = spec.get_string_attribute("textureformat");
            if (textureformat == "" || spec.tile_width == 0 ||
                (!software.StartsWith("OpenImageIO", StringComparison.OrdinalIgnoreCase) && !software.StartsWith("maketx", StringComparison.OrdinalIgnoreCase)))
            {
                spec.attributes.Remove("oiio::ConstantColor");
                spec.attributes.Remove("oiio::AverageColor");
                spec.attributes.Remove("oiio:SHA-1");
            }
            string fing = spec.get_string_attribute("oiio:SHA-1");
            if (fing.Length > 0)
            {
                h.fingerprint = new ustring(fing);
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Automip (ImageCacheFile::read_unmipped)
        // ------------------------------------------------------------------------------------------------------------------------------------

        private static ImageLevelInfo make_automip_level(ImageSubimageInfo si, ImageLevelInfo dims, ImageLevelInfo prev)
        {
            int nch = dims.nchannels;
            float[] data = new float[(long)dims.width * dims.height * nch];
            float[] bilerppels = new float[4 * nch];
            float[] resultpel = new float[nch];
            for (int j = 0; j < dims.height; ++j)
            {
                float yf = (j + 0.5f) / dims.full_height;
                int ylow;
                float yfrac = floorfrac((double)yf * prev.full_height - 0.5, out ylow);
                for (int i = 0; i < dims.width; ++i)
                {
                    float xf = (i + 0.5f) / dims.full_width;
                    int xlow;
                    float xfrac = floorfrac((double)xf * prev.full_width - 0.5, out xlow);
                    for (int k = 0; k < 4; ++k)
                    {
                        int px = xlow + (k & 1);
                        int py = ylow + (k >> 1);
                        for (int c = 0; c < nch; ++c)
                        {
                            float v = 0.0f;
                            if (px >= prev.x && px < prev.x + prev.width && py >= prev.y && py < prev.y + prev.height)
                            {
                                v = prev.pixels[((long)(py - prev.y) * prev.width + (px - prev.x)) * nch + c];
                            }
                            bilerppels[k * nch + c] = v;
                        }
                    }
                    for (int c = 0; c < nch; ++c)
                    {
                        float v0 = bilerppels[c];
                        float v1 = bilerppels[nch + c];
                        float v2 = bilerppels[2 * nch + c];
                        float v3 = bilerppels[3 * nch + c];
                        float s1 = 1.0f - xfrac;
                        float t1 = 1.0f - yfrac;
                        resultpel[c] = t1 * (v0 * s1 + v1 * xfrac) + yfrac * (v2 * s1 + v3 * xfrac);
                    }
                    for (int c = 0; c < nch; ++c)
                    {
                        data[((long)j * dims.width + i) * nch + c] = ImageReaders.quantize(resultpel[c], si.datatype);
                    }
                }
            }
            dims.pixels = data;
            return dims;
        }

        /// float floorfrac(double) as used by read_unmipped: the argument is a double expression, converted to the float parameter of floorfrac(float, int*)
        private static float floorfrac(double xd, out int xint)
        {
            float x = (float)xd;
            int i = fmath.ifloor(x);
            xint = i;
            return x - i;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Texture info
        // ------------------------------------------------------------------------------------------------------------------------------------

        private bool get_average_color(ImageTextureHandle file, float[] avg, int subimage, int chbegin, int chend)
        {
            if (subimage < 0 || subimage > file.subimages.Count)
            {
                return false;
            }
            ImageSubimageInfo si = file.subimages[subimage];
            if (!si.has_average_color)
            {
                int nlevels = si.levels.Count;
                ImageLevelInfo dims = si.levels[nlevels - 1];
                if (dims.width != 1 || dims.height != 1)
                {
                    return false;
                }
                lock (si)
                {
                    if (!si.has_average_color)
                    {
                        si.average_color.Clear();
                        for (int c = 0; c < dims.nchannels; ++c)
                        {
                            si.average_color.Add(dims.pixels[c]);
                        }
                        si.has_average_color = true;
                    }
                }
            }
            if (si.has_average_color)
            {
                for (int i = 0, c = chbegin; c < chend; ++i, ++c)
                {
                    avg[i] = (c < si.spec.nchannels) ? si.average_color[c] : 0.0f;
                }
                return true;
            }
            return false;
        }

        public bool get_texture_info(TextureHandle texture_handle, TexturePerthread thread_info, int subimage, ustring dataname, TypeDesc datatype,
                                     object data)
        {
            bool ok = get_image_info(texture_handle as ImageTextureHandle, subimage, 0, dataname.@string(), datatype, data);
            if (!ok)
            {
                string err = cache_geterror();
                if (err.Length > 0)
                {
                    error(err);
                }
            }
            return ok;
        }

        private bool get_image_info(ImageTextureHandle file, int subimage, int miplevel, string dataname, TypeDesc datatype, object data)
        {
            if (file != null)
            {
                verify_file(file, true);
            }
            if (dataname == "exists" && datatype == TypeDescs.TypeInt)
            {
                int[] d = (int[])data;
                d[0] = (file != null && !file.broken) ? 1 : 0;
                if (d[0] == 0)
                {
                    cache_geterror();
                }
                return true;
            }
            if (file == null)
            {
                cache_error("Invalid image file");
                return false;
            }
            if (dataname == "broken" && datatype == TypeDescs.TypeInt)
            {
                ((int[])data)[0] = file.broken ? 1 : 0;
                return true;
            }
            if (file.broken)
            {
                if (file.errors_should_issue())
                {
                    cache_geterror();
                    cache_error("Invalid image file \"" + file.filename.@string() + "\": " + file.broken_message);
                }
                return false;
            }
            if (dataname == "UDIM" && datatype == TypeDescs.TypeInt)
            {
                ((int[])data)[0] = file.is_udim() ? 1 : 0;
                return true;
            }
            if (file.is_udim())
            {
                // retrieve the attribute if it is the same for all of the UDIM tiles
                object common = null;
                bool first = true;
                for (int i = 0; i < file.udim_lookup_names.Length; ++i)
                {
                    if (file.udim_lookup_names[i] == null)
                    {
                        continue;
                    }
                    ImageTextureHandle concrete = resolve_udim_tile(file, i % file.udim_nutiles, i / file.udim_nutiles);
                    if (concrete == null)
                    {
                        continue;
                    }
                    verify_file(concrete, true);
                    if (concrete.broken)
                    {
                        continue;
                    }
                    object single = clone_info_array(data);
                    if (!get_image_info(concrete, subimage, miplevel, dataname, datatype, single))
                    {
                        cache_geterror();
                        return false;
                    }
                    if (first)
                    {
                        common = single;
                        first = false;
                    }
                    else if (!info_arrays_equal(common, single))
                    {
                        return false;
                    }
                }
                if (common == null)
                {
                    return false;
                }
                copy_info_array(common, data);
                return true;
            }
            if (dataname == "subimages" && datatype == TypeDescs.TypeInt)
            {
                ((int[])data)[0] = file.subimages.Count;
                return true;
            }
            if (subimage < 0 || subimage >= file.subimages.Count)
            {
                if (file.errors_should_issue())
                {
                    cache_error("Unknown subimage " + subimage + " (out of " + file.subimages.Count + ")");
                }
                return false;
            }
            if (miplevel < 0 || miplevel >= file.miplevels(subimage))
            {
                if (file.errors_should_issue())
                {
                    cache_error("Unknown mip level " + miplevel + " (out of " + file.miplevels(subimage) + ")");
                }
                return false;
            }
            ImageSubimageInfo si = file.subimages[subimage];
            ImageSpecData spec = si.spec;
            ImageLevelInfo dims = si.levels[miplevel];
            if (dataname == "resolution" && datatype.basetype == TypeDesc.INT && datatype.arraylen == 2 && datatype.aggregate == TypeDesc.SCALAR)
            {
                int[] d = (int[])data;
                d[0] = dims.width;
                d[1] = dims.height;
                return true;
            }
            if (dataname == "resolution" && datatype.basetype == TypeDesc.INT && datatype.arraylen == 3 && datatype.aggregate == TypeDesc.SCALAR)
            {
                int[] d = (int[])data;
                d[0] = dims.width;
                d[1] = dims.height;
                d[2] = 1;
                return true;
            }
            if (dataname == "texturetype" && datatype == TypeDescs.TypeString)
            {
                ((ustring[])data)[0] = new ustring(texture_type_names[file.texformat]);
                return true;
            }
            if (dataname == "textureformat" && datatype == TypeDescs.TypeString)
            {
                ((ustring[])data)[0] = new ustring(texture_format_names[file.texformat]);
                return true;
            }
            if (dataname == "fileformat" && datatype == TypeDescs.TypeString)
            {
                ((ustring[])data)[0] = new ustring(file.fileformat);
                return true;
            }
            if (dataname == "channels" && datatype == TypeDescs.TypeInt)
            {
                ((int[])data)[0] = spec.nchannels;
                return true;
            }
            if (dataname == "channels" && datatype == TypeDescs.TypeFloat)
            {
                ((float[])data)[0] = spec.nchannels;
                return true;
            }
            if (dataname == "format" && datatype == TypeDescs.TypeInt)
            {
                ((int[])data)[0] = spec.format;
                return true;
            }
            if ((dataname == "cachedformat" || dataname == "cachedpixeltype") && datatype == TypeDescs.TypeInt)
            {
                ((int[])data)[0] = si.datatype;
                return true;
            }
            if (dataname == "miplevels" && datatype == TypeDescs.TypeInt)
            {
                ((int[])data)[0] = file.miplevels(subimage);
                return true;
            }
            if (dataname == "datawindow" && datatype.basetype == TypeDesc.INT && datatype.aggregate == TypeDesc.SCALAR &&
                (datatype.arraylen == 4 || datatype.arraylen == 6))
            {
                int[] d = (int[])data;
                if (datatype.arraylen == 4)
                {
                    d[0] = dims.x;
                    d[1] = dims.y;
                    d[2] = dims.x + dims.width - 1;
                    d[3] = dims.y + dims.height - 1;
                }
                else
                {
                    d[0] = dims.x;
                    d[1] = dims.y;
                    d[2] = 0;
                    d[3] = dims.x + dims.width - 1;
                    d[4] = dims.y + dims.height - 1;
                    d[5] = 0;
                }
                return true;
            }
            if (dataname == "displaywindow" && datatype.basetype == TypeDesc.INT && datatype.aggregate == TypeDesc.SCALAR &&
                (datatype.arraylen == 4 || datatype.arraylen == 6))
            {
                int[] d = (int[])data;
                if (datatype.arraylen == 4)
                {
                    d[0] = dims.full_x;
                    d[1] = dims.full_y;
                    d[2] = dims.full_x + dims.full_width - 1;
                    d[3] = dims.full_y + dims.full_height - 1;
                }
                else
                {
                    d[0] = dims.full_x;
                    d[1] = dims.full_y;
                    d[2] = 0;
                    d[3] = dims.full_x + dims.full_width - 1;
                    d[4] = dims.full_y + dims.full_height - 1;
                    d[5] = 0;
                }
                return true;
            }
            if (dataname == "averagecolor" && datatype.basetype == TypeDesc.FLOAT)
            {
                int datalen = datatype.numelements() * datatype.aggregate;
                float[] avg = new float[datalen];
                bool r = get_average_color(file, avg, subimage, 0, datalen);
                if (r)
                {
                    Array.Copy(avg, (float[])data, datalen);
                }
                return r;
            }
            if (dataname == "averagealpha" && datatype == TypeDescs.TypeFloat && spec.alpha_channel >= 0)
            {
                float[] avg = new float[1];
                bool r = get_average_color(file, avg, subimage, spec.alpha_channel, spec.alpha_channel + 1);
                if (r)
                {
                    ((float[])data)[0] = avg[0];
                }
                return r;
            }
            if (dataname == "constantcolor" && datatype.basetype == TypeDesc.FLOAT)
            {
                if (file.subimages[subimage].is_constant_image)
                {
                    int datalen = datatype.numelements() * datatype.aggregate;
                    float[] avg = new float[datalen];
                    bool r = get_average_color(file, avg, subimage, 0, datalen);
                    if (r)
                    {
                        Array.Copy(avg, (float[])data, datalen);
                    }
                    return r;
                }
                return false;
            }
            if (dataname == "constantalpha" && datatype == TypeDescs.TypeFloat && spec.alpha_channel >= 0)
            {
                if (file.subimages[subimage].is_constant_image)
                {
                    float[] avg = new float[1];
                    bool r = get_average_color(file, avg, subimage, spec.alpha_channel, spec.alpha_channel + 1);
                    if (r)
                    {
                        ((float[])data)[0] = avg[0];
                    }
                    return r;
                }
                return false;
            }
            // general case: anything else that the spec has as an attribute
            object attr;
            if (spec.attributes.TryGetValue(dataname, out attr))
            {
                if (attr is int && datatype == TypeDescs.TypeInt)
                {
                    ((int[])data)[0] = (int)attr;
                    return true;
                }
                if (attr is int && datatype == TypeDescs.TypeFloat)
                {
                    ((float[])data)[0] = (int)attr;
                    return true;
                }
                if (attr is float && datatype == TypeDescs.TypeFloat)
                {
                    ((float[])data)[0] = (float)attr;
                    return true;
                }
                if (attr is string && datatype == TypeDescs.TypeString)
                {
                    ((ustring[])data)[0] = new ustring((string)attr);
                    return true;
                }
            }
            return false;
        }

        private static object clone_info_array(object data)
        {
            if (data is int[])
            {
                return ((int[])data).Clone();
            }
            if (data is float[])
            {
                return ((float[])data).Clone();
            }
            return ((ustring[])data).Clone();
        }

        private static bool info_arrays_equal(object a, object b)
        {
            if (a is int[])
            {
                return ((int[])a).AsSpan().SequenceEqual((int[])b);
            }
            if (a is float[])
            {
                return ((float[])a).AsSpan().SequenceEqual((float[])b);
            }
            return ((ustring[])a).AsSpan().SequenceEqual((ustring[])b);
        }

        private static void copy_info_array(object src, object dst)
        {
            Array.Copy((Array)src, (Array)dst, ((Array)src).Length);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Lookup helpers
        // ------------------------------------------------------------------------------------------------------------------------------------

        private bool missing_texture(ref TextureOpt options, int nchannels, float* result, float* dresultds, float* dresultdt)
        {
            for (int c = 0; c < nchannels; ++c)
            {
                if (options.missingcolor != null)
                {
                    result[c] = options.missingcolor[c];
                }
                else
                {
                    result[c] = options.fill;
                }
                if (dresultds != null)
                {
                    dresultds[c] = 0;
                }
                if (dresultdt != null)
                {
                    dresultdt[c] = 0;
                }
            }
            if (options.missingcolor != null)
            {
                geterror();
                return true;
            }
            return false;
        }

        private void fill_gray_channels(ImageSpecData spec, int nchannels, float* result, float* dresultds, float* dresultdt)
        {
            int specchans = spec.nchannels;
            if (specchans == 1 && nchannels >= 3)
            {
                result[1] = result[0];
                result[2] = result[0];
                if (dresultds != null)
                {
                    dresultds[1] = dresultds[0];
                    dresultds[2] = dresultds[0];
                    dresultdt[1] = dresultdt[0];
                    dresultdt[2] = dresultdt[0];
                }
            }
            else if (specchans == 2 && nchannels == 4 && spec.alpha_channel == 1)
            {
                float a;
                a = result[1];
                result[1] = result[0];
                result[2] = result[0];
                result[3] = a;
                if (dresultds != null)
                {
                    a = dresultds[1];
                    dresultds[1] = dresultds[0];
                    dresultds[2] = dresultds[0];
                    dresultds[3] = a;
                    a = dresultdt[1];
                    dresultdt[1] = dresultdt[0];
                    dresultdt[2] = dresultdt[0];
                    dresultdt[3] = a;
                }
            }
        }

        private static bool ispow2(int x)
        {
            return x > 0 && (x & (x - 1)) == 0;
        }

        // The wrap functions (imageio.h wrap_black etc.): wrap 'coord' around 'width', return true if the result is a valid pixel coordinate
        private static bool wrap_coord(int mode, ref int coord, int origin, int width)
        {
            switch (mode)
            {
                case TextureOpt.WrapDefault:
                case TextureOpt.WrapBlack:
                    return coord >= origin && coord < (width + origin);
                case TextureOpt.WrapClamp:
                    if (coord < origin)
                    {
                        coord = origin;
                    }
                    else if (coord >= origin + width)
                    {
                        coord = origin + width - 1;
                    }
                    return true;
                case TextureOpt.WrapPeriodic:
                    coord -= origin;
                    coord = coord % width;
                    if (coord < 0)
                    {
                        coord += width;
                    }
                    coord += origin;
                    return true;
                case TextureOpt.WrapMirror:
                    {
                        coord -= origin;
                        if (coord < 0)
                        {
                            coord = -1 - coord;
                        }
                        int iter = coord / width;
                        coord -= iter * width;
                        if ((iter & 1) != 0)
                        {
                            coord = (width - 1) - coord;
                        }
                        coord += origin;
                        return true;
                    }
                case TextureOpt.WrapPeriodicPow2:
                    coord -= origin;
                    coord = coord & (width - 1);
                    coord += origin;
                    return true;
                case TextureOpt.WrapPeriodicSharedBorder:
                    {
                        width = Math.Max(width, 2);
                        coord -= origin;
                        coord = coord % (width - 1);
                        if (coord < 0)
                        {
                            coord += width;
                        }
                        coord += origin;
                        return true;
                    }
                default:
                    return true;
            }
        }

        private static void st_to_texel(float s, float t, ImageTextureHandle texturefile, ImageLevelInfo dims, out int i, out int j, out float ifrac,
                                        out float jfrac)
        {
            if (!texturefile.sample_border)
            {
                s = s * dims.width + dims.x - 0.5f;
                t = t * dims.height + dims.y - 0.5f;
            }
            else
            {
                s = s * (dims.width - 1) + dims.x;
                t = t * (dims.height - 1) + dims.y;
            }
            i = fmath.ifloor(s);
            ifrac = s - i;
            j = fmath.ifloor(t);
            jfrac = t - j;
        }

        private static void st_to_texel_simd(float s_, float t_, ImageTextureHandle texturefile, ImageLevelInfo dims, out int i, out int j, out float ifrac,
                                             out float jfrac)
        {
            float s, t;
            if (!texturefile.sample_border)
            {
                s = s_ * (float)dims.width + (dims.x - 0.5f);
                t = t_ * (float)dims.height + (dims.y - 0.5f);
            }
            else
            {
                s = s_ * (float)(dims.width - 1) + (float)dims.x;
                t = t_ * (float)(dims.height - 1) + (float)dims.y;
            }
            i = fmath.ifloor(s);
            ifrac = s - i;
            j = fmath.ifloor(t);
            jfrac = t - j;
        }

        /// The 4 channels of the texel (x, y) of a level starting at 'firstchannel' (channels beyond the file's are 0)
        private static Float4 load_texel(ImageLevelInfo lvl, int x, int y, int firstchannel)
        {
            long idx = ((long)(y - lvl.y) * lvl.width + (x - lvl.x)) * lvl.nchannels;
            Float4 r = new Float4(0.0f);
            int n = lvl.nchannels;
            float[] p = lvl.pixels;
            int c0 = firstchannel;
            if (c0 + 0 < n) { r.x = p[idx + c0]; }
            if (c0 + 1 < n) { r.y = p[idx + c0 + 1]; }
            if (c0 + 2 < n) { r.z = p[idx + c0 + 2]; }
            if (c0 + 3 < n) { r.w = p[idx + c0 + 3]; }
            return r;
        }

        private float[] pole_color(ImageTextureHandle texturefile, int subimage, int miplevel, int pole, out int offset)
        {
            ImageSubimageInfo si = texturefile.subimages[subimage];
            ImageLevelInfo lvl = si.levels[miplevel];
            offset = 0;
            if (!lvl.onetile)
            {
                return null;
            }
            if (!lvl.polecolorcomputed)
            {
                lock (lvl)
                {
                    if (!lvl.polecolorcomputed)
                    {
                        lvl.polecolor = new float[2 * lvl.nchannels];
                        int width = lvl.width;
                        float scale = 1.0f / width;
                        for (int pl = 0; pl <= 1; ++pl)
                        {
                            int y = pl * (lvl.height - 1);
                            int pc = pl * lvl.nchannels;
                            for (int c = 0; c < lvl.nchannels; ++c)
                            {
                                lvl.polecolor[pc + c] = 0.0f;
                            }
                            for (int i = 0; i < width; ++i)
                            {
                                for (int c = 0; c < lvl.nchannels; ++c)
                                {
                                    lvl.polecolor[pc + c] += lvl.pixels[((long)y * lvl.width + i) * lvl.nchannels + c];
                                }
                            }
                            for (int c = 0; c < lvl.nchannels; ++c)
                            {
                                lvl.polecolor[pc + c] *= scale;
                            }
                        }
                        lvl.polecolorcomputed = true;
                    }
                }
            }
            offset = pole * lvl.nchannels;
            return lvl.polecolor;
        }

        private void fade_to_pole(float t, ref Float4 accum, ref float weight, ImageTextureHandle texturefile, ref TextureOpt options, int miplevel,
                                  int nchannels)
        {
            float pole;
            float[] polecolor;
            int off;
            if (t < 1.0f)
            {
                pole = (1.0f - t);
                polecolor = pole_color(texturefile, options.subimage, miplevel, 0, out off);
            }
            else
            {
                pole = t - MathF.Floor(t);
                polecolor = pole_color(texturefile, options.subimage, miplevel, 1, out off);
            }
            pole = fmath.clamp(pole, 0.0f, 1.0f);
            pole *= pole;
            off += options.firstchannel;
            for (int c = 0; c < nchannels; ++c)
            {
                accum[c] += weight * pole * polecolor[off + c];
            }
            weight *= 1.0f - pole;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Samplers
        // ------------------------------------------------------------------------------------------------------------------------------------

        private bool sample_closest(int nsamples, float[] s_, float[] t_, int miplevel, ImageTextureHandle texturefile, ref TextureOpt options,
                                    int nchannels_result, int actualchannels, float[] weight_, out Float4 accum_, out Float4 daccumds_,
                                    out Float4 daccumdt_, bool want_derivs)
        {
            bool allok = true;
            ImageSubimageInfo si = texturefile.subimages[options.subimage];
            ImageLevelInfo lvl = si.levels[miplevel];
            ImageLevelInfo dims = lvl;
            Float4 accum = new Float4(0.0f);
            float nonfill = 0.0f;
            int firstchannel = options.firstchannel;
            for (int sample = 0; sample < nsamples; ++sample)
            {
                float s = s_[sample], t = t_[sample];
                float weight = weight_[sample];
                int stex, ttex;
                float sfrac, tfrac;
                st_to_texel(s, t, texturefile, dims, out stex, out ttex, out sfrac, out tfrac);
                if (sfrac > 0.5f)
                {
                    ++stex;
                }
                if (tfrac > 0.5f)
                {
                    ++ttex;
                }
                bool svalid = wrap_coord(options.swrap, ref stex, dims.x, dims.width);
                bool tvalid = wrap_coord(options.twrap, ref ttex, dims.y, dims.height);
                if (!lvl.full_pixel_range)
                {
                    svalid &= (stex >= dims.x && stex < (dims.x + dims.width));
                    tvalid &= (ttex >= dims.y && ttex < (dims.y + dims.height));
                }
                if (!(svalid & tvalid))
                {
                    nonfill += weight;
                    continue;
                }
                Float4 texel = load_texel(lvl, stex, ttex, firstchannel);
                accum += weight * texel;
            }
            accum = accum.masked(actualchannels);
            if (nonfill < 1.0f && nchannels_result > actualchannels && options.fill != 0.0f)
            {
                accum += new Float4((1.0f - nonfill) * options.fill).masked_not(actualchannels);
            }
            accum_ = accum;
            daccumds_ = new Float4(0.0f);
            daccumdt_ = new Float4(0.0f);
            return allok;
        }

        private bool sample_bilinear(int nsamples, float[] s_, float[] t_, int miplevel, ImageTextureHandle texturefile, ref TextureOpt options,
                                     int nchannels_result, int actualchannels, float[] weight_, out Float4 accum_, out Float4 daccumds_,
                                     out Float4 daccumdt_, bool want_derivs)
        {
            ImageSubimageInfo si = texturefile.subimages[options.subimage];
            ImageLevelInfo lvl = si.levels[miplevel];
            ImageLevelInfo dims = lvl;
            bool use_fill = (nchannels_result > actualchannels && options.fill != 0.0f);
            int firstchannel = options.firstchannel;
            bool need_pole = (options.envlayout == LayoutLatLong && lvl.onetile);
            float nonfill = 0.0f;
            Float4 accum = new Float4(0.0f);
            Float4 daccumds = new Float4(0.0f);
            Float4 daccumdt = new Float4(0.0f);
            for (int sample = 0; sample < nsamples; ++sample)
            {
                int sint, tint;
                float sfrac, tfrac;
                st_to_texel_simd(s_[sample], t_[sample], texturefile, dims, out sint, out tint, out sfrac, out tfrac);
                float weight = weight_[sample];
                int s0 = sint, s1 = sint + 1, t0 = tint, t1 = tint + 1;
                bool vs0 = wrap_coord(options.swrap, ref s0, dims.x, dims.width);
                bool vs1 = wrap_coord(options.swrap, ref s1, dims.x, dims.width);
                bool vt0 = wrap_coord(options.twrap, ref t0, dims.y, dims.height);
                bool vt1 = wrap_coord(options.twrap, ref t1, dims.y, dims.height);
                if (!lvl.full_pixel_range)
                {
                    vs0 &= s0 >= dims.x && s0 < dims.x + dims.width;
                    vs1 &= s1 >= dims.x && s1 < dims.x + dims.width;
                    vt0 &= t0 >= dims.y && t0 < dims.y + dims.height;
                    vt1 &= t1 >= dims.y && t1 < dims.y + dims.height;
                }
                bool allvalid = vs0 & vs1 & vt0 & vt1;
                if (!(vs0 | vs1 | vt0 | vt1))
                {
                    nonfill += weight;
                    continue;
                }
                Float4 t00 = new Float4(0.0f), t01 = new Float4(0.0f), t10 = new Float4(0.0f), t11 = new Float4(0.0f);
                if (vt0 && vs0)
                {
                    t00 = load_texel(lvl, s0, t0, firstchannel);
                }
                if (vt0 && vs1)
                {
                    t01 = load_texel(lvl, s1, t0, firstchannel);
                }
                if (vt1 && vs0)
                {
                    t10 = load_texel(lvl, s0, t1, firstchannel);
                }
                if (vt1 && vs1)
                {
                    t11 = load_texel(lvl, s1, t1, firstchannel);
                }
                if (need_pole)
                {
                    float height = dims.height;
                    if (texturefile.sample_border)
                    {
                        height -= 1.0f;
                    }
                    float tt = t_[sample] * height;
                    if (tt < 1.0f || tt > (height - 1.0f))
                    {
                        fade_to_pole(tt, ref accum, ref weight, texturefile, ref options, miplevel, actualchannels);
                    }
                }
                Float4 weight_simd = new Float4(weight);
                accum += weight_simd * Float4.bilerp(t00, t01, t10, t11, sfrac, tfrac);
                if (want_derivs)
                {
                    Float4 scalex = weight_simd * (float)dims.width;
                    Float4 scaley = weight_simd * (float)dims.height;
                    daccumds += scalex * Float4.lerp(t01 - t00, t11 - t10, tfrac);
                    daccumdt += scaley * Float4.lerp(t10 - t00, t11 - t01, sfrac);
                }
                if (use_fill && !allvalid)
                {
                    float f = fmath.bilerp((vs0 && vt0) ? 1.0f : 0.0f, (vs1 && vt0) ? 1.0f : 0.0f, (vs0 && vt1) ? 1.0f : 0.0f, (vs1 && vt1) ? 1.0f : 0.0f,
                                           sfrac, tfrac);
                    nonfill += (1.0f - f) * weight;
                }
            }
            accum = accum.masked(actualchannels);
            if (use_fill)
            {
                accum += new Float4((1.0f - nonfill) * options.fill).masked_not(actualchannels);
            }
            accum_ = accum;
            if (want_derivs)
            {
                daccumds_ = daccumds.masked(actualchannels);
                daccumdt_ = daccumdt.masked(actualchannels);
            }
            else
            {
                daccumds_ = new Float4(0.0f);
                daccumdt_ = new Float4(0.0f);
            }
            return true;
        }

        // B-spline weights for the value (and the derivative) at 'fraction'
        private static void eval_bspline_weights(float fraction, out Float4 w)
        {
            float one_frac = 1.0f - fraction;
            // ofof = (1-frac, frac, 1-frac, frac); C = (1-frac, 2-frac, 2-(1-frac), frac)
            // A + B * ofof * ofof * C with A = (0, 2/3, 2/3, 0), B = (1/6, -0.5, -0.5, 1/6)
            Float4 ofof = new Float4(one_frac, fraction, one_frac, fraction);
            Float4 om1m1o = new Float4(1.0f, -1.0f, -1.0f, 1.0f);
            Float4 z22z = new Float4(0.0f, 2.0f, 2.0f, 0.0f);
            Float4 C = om1m1o * ofof + z22z;
            Float4 A = new Float4(0.0f, 2.0f / 3.0f, 2.0f / 3.0f, 0.0f);
            Float4 B = new Float4(1.0f / 6.0f, -0.5f, -0.5f, 1.0f / 6.0f);
            w = A + B * ofof * ofof * C;
        }

        private static void eval_bspline_weights_and_derivs(float fraction, out Float4 w, out Float4 dw)
        {
            float one_frac = 1.0f - fraction;
            Float4 A = new Float4(0.0f, 2.0f / 3.0f, 2.0f / 3.0f, 0.0f);
            Float4 B = new Float4(1.0f / 6.0f, -0.5f, -0.5f, 1.0f / 6.0f);
            Float4 ofof = new Float4(one_frac, fraction, one_frac, fraction);
            Float4 C = new Float4(one_frac, 2.0f - fraction, 2.0f - one_frac, fraction);
            w = A + B * ofof * ofof * C;
            Float4 D = new Float4(-0.5f, 0.5f, -0.5f, 0.5f);
            Float4 E = new Float4(1.0f, 3.0f, 3.0f, 1.0f);
            Float4 F = new Float4(0.0f, 4.0f, 4.0f, 0.0f);
            dw = D * ofof * (E * ofof - F);
        }

        private bool sample_bicubic(int nsamples, float[] s_, float[] t_, int miplevel, ImageTextureHandle texturefile, ref TextureOpt options,
                                    int nchannels_result, int actualchannels, float[] weight_, out Float4 accum_, out Float4 daccumds_,
                                    out Float4 daccumdt_, bool want_derivs)
        {
            ImageSubimageInfo si = texturefile.subimages[options.subimage];
            ImageLevelInfo lvl = si.levels[miplevel];
            ImageLevelInfo dims = lvl;
            bool use_fill = (nchannels_result > actualchannels && options.fill != 0.0f);
            int firstchannel = options.firstchannel;
            float nonfill = 0.0f;
            bool need_pole = (options.envlayout == LayoutLatLong && lvl.onetile);
            Float4 accum = new Float4(0.0f);
            Float4 daccumds = new Float4(0.0f);
            Float4 daccumdt = new Float4(0.0f);
            int* stex = stackalloc int[4];
            int* ttex = stackalloc int[4];
            bool* svalid = stackalloc bool[4];
            bool* tvalid = stackalloc bool[4];
            for (int sample = 0; sample < nsamples; ++sample)
            {
                int sint, tint;
                float sfrac, tfrac;
                st_to_texel_simd(s_[sample], t_[sample], texturefile, dims, out sint, out tint, out sfrac, out tfrac);
                float weight = weight_[sample];
                bool allvalid = true;
                bool anyvalid = false;
                for (int k = 0; k < 4; ++k)
                {
                    stex[k] = sint - 1 + k;
                    ttex[k] = tint - 1 + k;
                    svalid[k] = wrap_coord(options.swrap, ref stex[k], dims.x, dims.width);
                    tvalid[k] = wrap_coord(options.twrap, ref ttex[k], dims.y, dims.height);
                }
                if (!lvl.full_pixel_range)
                {
                    bool anyv = false;
                    for (int k = 0; k < 4; ++k)
                    {
                        anyv |= svalid[k] | tvalid[k];
                    }
                    if (anyv)
                    {
                        for (int k = 0; k < 4; ++k)
                        {
                            svalid[k] &= stex[k] >= dims.x && stex[k] < dims.x + dims.width;
                            tvalid[k] &= ttex[k] >= dims.y && ttex[k] < dims.y + dims.height;
                        }
                    }
                }
                for (int k = 0; k < 4; ++k)
                {
                    allvalid &= svalid[k] & tvalid[k];
                    anyvalid |= svalid[k] | tvalid[k];
                }
                if (!anyvalid)
                {
                    nonfill += weight;
                    continue;
                }
                Float4[,] texel = new Float4[4, 4];
                for (int j = 0; j < 4; ++j)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        if (!tvalid[j] || !svalid[i])
                        {
                            texel[j, i] = new Float4(0.0f);
                        }
                        else
                        {
                            texel[j, i] = load_texel(lvl, stex[i], ttex[j], firstchannel);
                        }
                    }
                }
                if (need_pole)
                {
                    float height = dims.height;
                    if (texturefile.sample_border)
                    {
                        height -= 1.0f;
                    }
                    float tt = t_[sample] * height;
                    if (tt < 1.0f || tt > (height - 1.0f))
                    {
                        fade_to_pole(tt, ref accum, ref weight, texturefile, ref options, miplevel, actualchannels);
                    }
                }
                Float4 wx, dwx = new Float4(0.0f);
                Float4 wy, dwy = new Float4(0.0f);
                if (want_derivs)
                {
                    eval_bspline_weights_and_derivs(sfrac, out wx, out dwx);
                    eval_bspline_weights_and_derivs(tfrac, out wy, out dwy);
                }
                else
                {
                    eval_bspline_weights(sfrac, out wx);
                    eval_bspline_weights(tfrac, out wy);
                }
                // g = (wx0+wx1, wx2+wx3, wy0+wy1, wy2+wy3); h = (wx1/g0, wx3/g1, wy1/g2, wy3/g3)
                float g0x = wx[0] + wx[1];
                float g1x = wx[2] + wx[3];
                float g0y = wy[0] + wy[1];
                float g1y = wy[2] + wy[3];
                float h0x = wx[1] / g0x;
                float h1x = wx[3] / g1x;
                float h0y = wy[1] / g0y;
                float h1y = wy[3] / g1y;
                Float4[] col = new Float4[4];
                for (int j = 0; j < 4; ++j)
                {
                    Float4 lx = Float4.lerp(texel[j, 0], texel[j, 1], h0x);
                    Float4 rx = Float4.lerp(texel[j, 2], texel[j, 3], h1x);
                    col[j] = Float4.lerp(lx, rx, g1x);
                }
                Float4 ly = Float4.lerp(col[0], col[1], h0y);
                Float4 ry = Float4.lerp(col[2], col[3], h1y);
                Float4 weight_simd = new Float4(weight);
                accum += weight_simd * Float4.lerp(ly, ry, g1y);
                if (want_derivs)
                {
                    Float4 scalex = weight_simd * (float)dims.width;
                    Float4 scaley = weight_simd * (float)dims.height;
                    daccumds += scalex * (dwx[0] * (wy[0] * texel[0, 0] + wy[1] * texel[1, 0] + wy[2] * texel[2, 0] + wy[3] * texel[3, 0]) +
                                          dwx[1] * (wy[0] * texel[0, 1] + wy[1] * texel[1, 1] + wy[2] * texel[2, 1] + wy[3] * texel[3, 1]) +
                                          dwx[2] * (wy[0] * texel[0, 2] + wy[1] * texel[1, 2] + wy[2] * texel[2, 2] + wy[3] * texel[3, 2]) +
                                          dwx[3] * (wy[0] * texel[0, 3] + wy[1] * texel[1, 3] + wy[2] * texel[2, 3] + wy[3] * texel[3, 3]));
                    daccumdt += scaley * (dwy[0] * (wx[0] * texel[0, 0] + wx[1] * texel[0, 1] + wx[2] * texel[0, 2] + wx[3] * texel[0, 3]) +
                                          dwy[1] * (wx[0] * texel[1, 0] + wx[1] * texel[1, 1] + wx[2] * texel[1, 2] + wx[3] * texel[1, 3]) +
                                          dwy[2] * (wx[0] * texel[2, 0] + wx[1] * texel[2, 1] + wx[2] * texel[2, 2] + wx[3] * texel[2, 3]) +
                                          dwy[3] * (wx[0] * texel[3, 0] + wx[1] * texel[3, 1] + wx[2] * texel[3, 2] + wx[3] * texel[3, 3]));
                }
                if (!allvalid && use_fill)
                {
                    float[] colf = new float[4];
                    for (int j = 0; j < 4; ++j)
                    {
                        float tv = tvalid[j] ? 1.0f : 0.0f;
                        float lx = fmath.lerp(tv * (svalid[0] ? 1.0f : 0.0f), tv * (svalid[1] ? 1.0f : 0.0f), h0x);
                        float rx = fmath.lerp(tv * (svalid[2] ? 1.0f : 0.0f), tv * (svalid[3] ? 1.0f : 0.0f), h1x);
                        colf[j] = fmath.lerp(lx, rx, g1x);
                    }
                    float lyf = fmath.lerp(colf[0], colf[1], h0y);
                    float ryf = fmath.lerp(colf[2], colf[3], h1y);
                    nonfill += weight * (1.0f - fmath.lerp(lyf, ryf, g1y));
                }
            }
            accum = accum.masked(actualchannels);
            if (use_fill)
            {
                accum += new Float4((1.0f - nonfill) * options.fill).masked_not(actualchannels);
            }
            accum_ = accum;
            if (want_derivs)
            {
                daccumds_ = daccumds.masked(actualchannels);
                daccumdt_ = daccumdt.masked(actualchannels);
            }
            else
            {
                daccumds_ = new Float4(0.0f);
                daccumdt_ = new Float4(0.0f);
            }
            return true;
        }

        private delegate bool Sampler(int nsamples, float[] s_, float[] t_, int miplevel, ImageTextureHandle texturefile, ref TextureOpt options,
                                      int nchannels_result, int actualchannels, float[] weight_, out Float4 accum_, out Float4 daccumds_,
                                      out Float4 daccumdt_, bool want_derivs);

        private Sampler sampler_for(int interpmode)
        {
            switch (interpmode)
            {
                case TextureOpt.InterpClosest: return sample_closest;
                case TextureOpt.InterpBicubic: return sample_bicubic;
                default: return sample_bilinear;
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Filter geometry
        // ------------------------------------------------------------------------------------------------------------------------------------

        private static void adjust_width(ref float dsdx, ref float dtdx, ref float dsdy, ref float dtdy, float swidth, float twidth)
        {
            dsdx *= swidth;
            dtdx *= twidth;
            dsdy *= swidth;
            dtdy *= twidth;
            const float eps = 1.0e-8f, eps2 = eps * eps;
            float dxlen2 = dsdx * dsdx + dtdx * dtdx;
            float dylen2 = dsdy * dsdy + dtdy * dtdy;
            if (dxlen2 < eps2)
            {
                if (dylen2 < eps2)
                {
                    dsdx = eps;
                    dsdy = 0;
                    dtdx = 0;
                    dtdy = eps;
                    dxlen2 = dylen2 = eps2;
                }
                else
                {
                    float scale = eps / MathF.Sqrt(dylen2);
                    dsdx = dtdy * scale;
                    dtdx = -dsdy * scale;
                    dxlen2 = eps2;
                }
            }
            else if (dylen2 < eps2)
            {
                float scale = eps / MathF.Sqrt(dxlen2);
                dsdy = -dtdx * scale;
                dtdy = dsdx * scale;
                dylen2 = eps2;
            }
        }

        private static void adjust_blur(ref float majorlength, ref float minorlength, ref float theta, float sblur, float tblur)
        {
            if (sblur + tblur != 0.0f)
            {
                float sintheta, costheta;
                fmath.fast_sincos(theta, &sintheta, &costheta);
                sintheta = MathF.Abs(sintheta);
                costheta = MathF.Abs(costheta);
                majorlength += sblur * costheta + tblur * sintheta;
                minorlength += sblur * sintheta + tblur * costheta;
                if (minorlength > majorlength)
                {
                    float tmp = minorlength;
                    minorlength = majorlength;
                    majorlength = tmp;
                    theta += (float)fmath.M_PI_2;
                }
            }
        }

        private static float anisotropic_aspect(ref float majorlength, ref float minorlength, ref TextureOpt options, out float trueaspect)
        {
            float aspect = fmath.clamp(majorlength / minorlength, 1.0f, 1.0e6f);
            trueaspect = aspect;
            if (aspect > options.anisotropic)
            {
                aspect = options.anisotropic;
                if (options.conservative_filter != 0)
                {
                    majorlength = 0.5f * ((majorlength) + (minorlength * options.anisotropic));
                    minorlength = majorlength / options.anisotropic;
                }
                else
                {
                    majorlength = minorlength * options.anisotropic;
                }
            }
            return aspect;
        }

        private static void ellipse_axes(float dsdx, float dtdx, float dsdy, float dtdy, out float majorlength, out float minorlength, out float theta)
        {
            float dsdx2 = dsdx * dsdx;
            float dtdx2 = dtdx * dtdx;
            float dsdy2 = dsdy * dsdy;
            float dtdy2 = dtdy * dtdy;
            double A = dtdx2 + dtdy2;
            double B = -2.0 * (dsdx * dtdx + dsdy * dtdy);
            double C = dsdx2 + dsdy2;
            double root = double.Hypot(A - C, B);
            double Aprime = (A + C - root) * 0.5;
            double Cprime = (A + C + root) * 0.5;
            majorlength = Math.Min(fmath.safe_sqrt((float)Cprime), 1000.0f);
            minorlength = Math.Min(fmath.safe_sqrt((float)Aprime), 1000.0f);
            theta = fmath.fast_atan2((float)B, (float)(A - C)) * 0.5f + (float)fmath.M_PI_2;
        }

        private static void compute_miplevels(ImageTextureHandle texturefile, ref TextureOpt options, float majorlength, float minorlength, ref float aspect,
                                              int[] miplevel, float[] levelweight)
        {
            ImageSubimageInfo si = texturefile.subimages[options.subimage];
            int nmiplevels = si.n_mip_levels;
            int min_mip_level = si.min_mip_level;
            for (int m = min_mip_level; m < nmiplevels; ++m)
            {
                float filtwidth_ras = minorlength * si.minwh[m];
                if (filtwidth_ras <= 1.0f)
                {
                    miplevel[0] = m - 1;
                    miplevel[1] = m;
                    float blend = fmath.clamp(2.0f * filtwidth_ras - 1.0f, 0.0f, 1.0f);
                    levelweight[0] = 1.0f - blend;
                    levelweight[1] = blend;
                    break;
                }
            }
            if (miplevel[1] < 0)
            {
                miplevel[0] = nmiplevels - 1;
                miplevel[1] = nmiplevels - 1;
                levelweight[0] = 1.0f;
                levelweight[1] = 0.0f;
                return;
            }
            if (miplevel[0] < min_mip_level || options.mipmode == TextureOpt.MipModeNoMIP)
            {
                miplevel[0] = min_mip_level;
                miplevel[1] = min_mip_level;
                levelweight[0] = 1.0f;
                levelweight[1] = 0.0f;
                int r = Math.Max(si.spec.full_width, si.spec.full_height);
                if (minorlength * r < 0.5f)
                {
                    aspect = fmath.clamp(majorlength * r * 2.0f, 1.0f, (float)options.anisotropic);
                }
                return;
            }
            if (options.mipmode == TextureOpt.MipModeOneLevel)
            {
                miplevel[0] = miplevel[1];
                levelweight[0] = 1.0f;
                levelweight[1] = 0.0f;
                return;
            }
        }

        private static int compute_ellipse_sampling(float aspect, float theta, float majorlength, float minorlength, out float smajor, out float tmajor,
                                                    out float invsamples, float[] weights, float[] samplepos)
        {
            tmajor = MathF.Sin(theta);
            smajor = MathF.Cos(theta);
            float LL = 2.0f * (majorlength - minorlength);
            smajor *= LL;
            tmajor *= LL;
            int nsamples = Math.Max(1, (int)(2.0f * aspect - 1.0f));
            invsamples = 1.0f / nsamples;
            if (nsamples == 1)
            {
                weights[0] = 1.0f;
                samplepos[0] = 0.0f;
            }
            else if (nsamples == 2)
            {
                weights[0] = 0.5f;
                weights[1] = 0.5f;
                samplepos[0] = -0.5f;
                samplepos[1] = 0.5f;
            }
            else
            {
                float scale = majorlength / LL;
                float sumw = 0.0f;
                for (int i = 0; i < nsamples; ++i)
                {
                    float sp = 2.0f * (i + 0.5f) * invsamples - 1.0f;
                    float x = sp * scale;
                    float w = fmath.fast_exp(-2.0f * x * x);
                    weights[i] = w;
                    sumw += w;
                    samplepos[i] = sp;
                }
                for (int i = 0; i < nsamples; ++i)
                {
                    weights[i] /= sumw;
                }
            }
            return nsamples;
        }

        private static void store(Float4 v, int n, float* dst)
        {
            for (int c = 0; c < n; ++c)
            {
                dst[c] = v[c];
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // The lookup functions
        // ------------------------------------------------------------------------------------------------------------------------------------

        private bool texture_lookup_nomip(ImageTextureHandle texturefile, ref TextureOpt options, int nchannels_result, int actualchannels, float s, float t,
                                          float dsdx, float dtdx, float dsdy, float dtdy, out Float4 result, bool want_derivs, out Float4 dresultds,
                                          out Float4 dresultdt)
        {
            Sampler sampler = sampler_for(options.interpmode);
            float[] sval = new float[] { s };
            float[] tval = new float[] { t };
            float[] weight = new float[] { 1.0f };
            ImageSubimageInfo si = texturefile.subimages[options.subimage];
            int min_mip_level = si.min_mip_level;
            return sampler(1, sval, tval, min_mip_level, texturefile, ref options, nchannels_result, actualchannels, weight, out result, out dresultds,
                           out dresultdt, want_derivs);
        }

        private bool texture_lookup_trilinear_mipmap(ImageTextureHandle texturefile, ref TextureOpt options, int nchannels_result, int actualchannels,
                                                     float s, float t, float dsdx, float dtdx, float dsdy, float dtdy, out Float4 result,
                                                     bool want_derivs, out Float4 dresultds, out Float4 dresultdt)
        {
            adjust_width(ref dsdx, ref dtdx, ref dsdy, ref dtdy, options.swidth, options.twidth);
            int[] miplevel = new int[] { -1, -1 };
            float[] levelweight = new float[] { 0, 0 };
            float sfilt = Math.Max(MathF.Abs(dsdx), MathF.Abs(dsdy));
            float tfilt = Math.Max(MathF.Abs(dtdx), MathF.Abs(dtdy));
            float filtwidth = options.conservative_filter != 0 ? Math.Max(sfilt, tfilt) : Math.Min(sfilt, tfilt);
            filtwidth += Math.Max(options.sblur, options.tblur);
            float aspect = 1.0f;
            compute_miplevels(texturefile, ref options, filtwidth, filtwidth, ref aspect, miplevel, levelweight);
            Sampler sampler = sampler_for(options.interpmode);
            float[] sval = new float[] { s };
            float[] tval = new float[] { t };
            float[] weight = new float[] { 1.0f };
            bool ok = true;
            Float4 r_sum = new Float4(0.0f), drds_sum = new Float4(0.0f), drdt_sum = new Float4(0.0f);
            for (int level = 0; level < 2; ++level)
            {
                if (levelweight[level] == 0.0f)
                {
                    continue;
                }
                Float4 r, drds, drdt;
                ok &= sampler(1, sval, tval, miplevel[level], texturefile, ref options, nchannels_result, actualchannels, weight, out r, out drds,
                              out drdt, want_derivs);
                Float4 lw = new Float4(levelweight[level]);
                r_sum += lw * r;
                if (want_derivs)
                {
                    drds_sum += lw * drds;
                    drdt_sum += lw * drdt;
                }
            }
            result = r_sum;
            dresultds = drds_sum;
            dresultdt = drdt_sum;
            return ok;
        }

        private bool texture_lookup(ImageTextureHandle texturefile, ref TextureOpt options, int nchannels_result, int actualchannels, float s, float t,
                                    float dsdx, float dtdx, float dsdy, float dtdy, out Float4 result, bool want_derivs, out Float4 dresultds,
                                    out Float4 dresultdt)
        {
            float sfilt_noblur = Math.Max(Math.Max(MathF.Abs(dsdx), MathF.Abs(dsdy)), 1e-8f);
            float tfilt_noblur = Math.Max(Math.Max(MathF.Abs(dtdx), MathF.Abs(dtdy)), 1e-8f);
            int naturalsres = (int)(1.0f / sfilt_noblur);
            int naturaltres = (int)(1.0f / tfilt_noblur);
            adjust_width(ref dsdx, ref dtdx, ref dsdy, ref dtdy, options.swidth, options.twidth);
            float smajor, tmajor;
            float majorlength, minorlength;
            float theta;
            ellipse_axes(dsdx, dtdx, dsdy, dtdy, out majorlength, out minorlength, out theta);
            adjust_blur(ref majorlength, ref minorlength, ref theta, options.sblur, options.tblur);
            float aspect, trueaspect;
            aspect = anisotropic_aspect(ref majorlength, ref minorlength, ref options, out trueaspect);
            int[] miplevel = new int[] { -1, -1 };
            float[] levelweight = new float[] { 0, 0 };
            compute_miplevels(texturefile, ref options, majorlength, minorlength, ref aspect, miplevel, levelweight);
            int maxsamples = ((2 * options.anisotropic) + 3) & ~3;
            float[] lineweight = new float[Math.Max(maxsamples, 4)];
            float[] samplepos = new float[Math.Max(maxsamples, 4)];
            float[] sval = new float[Math.Max(maxsamples, 4)];
            float[] tval = new float[Math.Max(maxsamples, 4)];
            float invsamples;
            int nsamples = compute_ellipse_sampling(aspect, theta, majorlength, minorlength, out smajor, out tmajor, out invsamples, lineweight, samplepos);
            smajor *= 0.5f;
            tmajor *= 0.5f;
            bool ok = true;
            for (int sample = 0; sample < nsamples; ++sample)
            {
                float pos = samplepos[sample];
                sval[sample] = s + pos * smajor;
                tval[sample] = t + pos * tmajor;
            }
            Float4 r_sum = new Float4(0.0f), drds_sum = new Float4(0.0f), drdt_sum = new Float4(0.0f);
            ImageSubimageInfo si = texturefile.subimages[options.subimage];
            for (int level = 0; level < 2; ++level)
            {
                if (levelweight[level] == 0.0f)
                {
                    continue;
                }
                Float4 r, drds, drdt;
                int lev = miplevel[level];
                switch (options.interpmode)
                {
                    case TextureOpt.InterpClosest:
                        ok &= sample_closest(nsamples, sval, tval, lev, texturefile, ref options, nchannels_result, actualchannels, lineweight, out r,
                                             out drds, out drdt, false);
                        break;
                    case TextureOpt.InterpBilinear:
                        ok &= sample_bilinear(nsamples, sval, tval, lev, texturefile, ref options, nchannels_result, actualchannels, lineweight, out r,
                                              out drds, out drdt, want_derivs);
                        break;
                    case TextureOpt.InterpBicubic:
                        ok &= sample_bicubic(nsamples, sval, tval, lev, texturefile, ref options, nchannels_result, actualchannels, lineweight, out r,
                                             out drds, out drdt, want_derivs);
                        break;
                    default:
                        if (lev == 0 || si.leveldims(lev).width < naturalsres / 2 || si.leveldims(lev).height < naturaltres / 2)
                        {
                            ok &= sample_bicubic(nsamples, sval, tval, lev, texturefile, ref options, nchannels_result, actualchannels, lineweight, out r,
                                                 out drds, out drdt, want_derivs);
                        }
                        else
                        {
                            ok &= sample_bilinear(nsamples, sval, tval, lev, texturefile, ref options, nchannels_result, actualchannels, lineweight, out r,
                                                  out drds, out drdt, want_derivs);
                        }
                        break;
                }
                Float4 lw = new Float4(levelweight[level]);
                r_sum += lw * r;
                if (want_derivs)
                {
                    drds_sum += lw * drds;
                    drdt_sum += lw * drdt;
                }
            }
            result = r_sum;
            dresultds = drds_sum;
            dresultdt = drdt_sum;
            return ok;
        }

        private delegate bool LookupFunc(ImageTextureHandle texturefile, ref TextureOpt options, int nchannels_result, int actualchannels, float s, float t,
                                         float dsdx, float dtdx, float dsdy, float dtdy, out Float4 result, bool want_derivs, out Float4 dresultds,
                                         out Float4 dresultdt);

        private LookupFunc lookup_for(int mipmode)
        {
            switch (mipmode)
            {
                case TextureOpt.MipModeNoMIP: return texture_lookup_nomip;
                case TextureOpt.MipModeOneLevel:
                case TextureOpt.MipModeTrilinear:
                    return texture_lookup_trilinear_mipmap;
                default: return texture_lookup;
            }
        }

        private bool resolve_subimage(ImageTextureHandle texturefile, ref TextureOpt options)
        {
            if (options.subimagename.@string().Length > 0)
            {
                int s = -1;
                for (int i = 0; i < texturefile.subimages.Count; ++i)
                {
                    if (texturefile.subimages[i].subimagename == options.subimagename)
                    {
                        s = i;
                        break;
                    }
                }
                if (s < 0)
                {
                    error("Unknown subimage \"" + options.subimagename.@string() + "\" in texture \"" + texturefile.filename.@string() + "\"");
                    return false;
                }
                options.subimage = s;
                options.subimagename = new ustring("");
            }
            if (options.subimage < 0 || options.subimage >= texturefile.subimages.Count)
            {
                error("Unknown subimage \"" + options.subimagename.@string() + "\" in texture \"" + texturefile.filename.@string() + "\"");
                return false;
            }
            return true;
        }

        private ImageTextureHandle verify_texturefile(ImageTextureHandle texturefile)
        {
            if (texturefile != null && !texturefile.is_udim())
            {
                verify_file(texturefile, false);
            }
            if (texturefile == null || texturefile.broken)
            {
                string err = cache_geterror();
                if (err.Length > 0)
                {
                    error(err);
                }
            }
            return texturefile;
        }

        public bool texture(TextureHandle texture_handle, TexturePerthread thread_info, ref TextureOpt options, float s, float t, float dsdx, float dtdx,
                            float dsdy, float dtdy, int nchannels, float* result, float* dresultds, float* dresultdt)
        {
            if (nchannels > 4)
            {
                int save_firstchannel = options.firstchannel;
                while (nchannels != 0)
                {
                    int n = Math.Min(nchannels, 4);
                    bool ok2 = texture(texture_handle, thread_info, ref options, s, t, dsdx, dtdx, dsdy, dtdy, n, result, dresultds, dresultdt);
                    if (!ok2)
                    {
                        return false;
                    }
                    result += n;
                    if (dresultds != null)
                    {
                        dresultds += n;
                    }
                    if (dresultdt != null)
                    {
                        dresultdt += n;
                    }
                    options.firstchannel += n;
                    nchannels -= n;
                }
                options.firstchannel = save_firstchannel;
                return true;
            }
            LookupFunc lookup = lookup_for(options.mipmode);
            ImageTextureHandle texturefile = texture_handle as ImageTextureHandle;
            if (texturefile != null && texturefile.is_udim())
            {
                texturefile = (ImageTextureHandle)resolve_udim(texture_handle, thread_info, s, t);
                s -= MathF.Floor(s);
                t -= MathF.Floor(t);
            }
            texturefile = verify_texturefile(texturefile);
            if (texturefile == null || texturefile.broken)
            {
                return missing_texture(ref options, nchannels, result, dresultds, dresultdt);
            }
            if (!resolve_subimage(texturefile, ref options))
            {
                return missing_texture(ref options, nchannels, result, dresultds, dresultdt);
            }
            ImageSubimageInfo si = texturefile.subimages[options.subimage];
            ImageSpecData spec = si.spec;
            int actualchannels = Math.Clamp(spec.nchannels - options.firstchannel, 0, nchannels);
            if (options.swrap == TextureOpt.WrapDefault)
            {
                options.swrap = texturefile.swrap;
            }
            if (options.swrap == TextureOpt.WrapPeriodic && ispow2(spec.width))
            {
                options.swrap = TextureOpt.WrapPeriodicPow2;
            }
            if (options.twrap == TextureOpt.WrapDefault)
            {
                options.twrap = texturefile.twrap;
            }
            if (options.twrap == TextureOpt.WrapPeriodic && ispow2(spec.height))
            {
                options.twrap = TextureOpt.WrapPeriodicPow2;
            }
            if (si.is_constant_image && options.swrap != TextureOpt.WrapBlack && options.twrap != TextureOpt.WrapBlack)
            {
                for (int c = 0; c < actualchannels; ++c)
                {
                    result[c] = si.average_color[c + options.firstchannel];
                }
                for (int c = actualchannels; c < nchannels; ++c)
                {
                    result[c] = options.fill;
                }
                if (dresultds != null)
                {
                    for (int c = 0; c < nchannels; ++c)
                    {
                        dresultds[c] = 0.0f;
                        dresultdt[c] = 0.0f;
                    }
                }
                if (actualchannels < nchannels && options.firstchannel == 0 && m_gray_to_rgb)
                {
                    fill_gray_channels(spec, nchannels, result, dresultds, dresultdt);
                }
                return true;
            }
            if (m_flip_t)
            {
                t = 1.0f - t;
                dtdx *= -1.0f;
                dtdy *= -1.0f;
            }
            if (!si.full_pixel_range)
            {
                s = s * si.sscale + si.soffset;
                dsdx *= si.sscale;
                dsdy *= si.sscale;
                t = t * si.tscale + si.toffset;
                dtdx *= si.tscale;
                dtdy *= si.tscale;
            }
            bool want_derivs = dresultds != null;
            Float4 r, drds, drdt;
            bool ok = lookup(texturefile, ref options, nchannels, actualchannels, s, t, dsdx, dtdx, dsdy, dtdy, out r, want_derivs, out drds, out drdt);
            float* tmpr = stackalloc float[4];
            float* tmpds = stackalloc float[4];
            float* tmpdt = stackalloc float[4];
            store(r, 4, tmpr);
            store(drds, 4, tmpds);
            store(drdt, 4, tmpdt);
            if (actualchannels < nchannels && options.firstchannel == 0 && m_gray_to_rgb)
            {
                fill_gray_channels(spec, nchannels, tmpr, want_derivs ? tmpds : null, want_derivs ? tmpdt : null);
            }
            for (int c = 0; c < nchannels; ++c)
            {
                result[c] = tmpr[c];
            }
            if (want_derivs)
            {
                for (int c = 0; c < nchannels; ++c)
                {
                    dresultds[c] = tmpds[c];
                    dresultdt[c] = m_flip_t ? -tmpdt[c] : tmpdt[c];
                }
            }
            return ok;
        }

        public bool texture3d(TextureHandle texture_handle, TexturePerthread thread_info, ref TextureOpt options, Vec3 P, Vec3 dPdx, Vec3 dPdy,
                              Vec3 dPdz, int nchannels, float* result, float* dresultds, float* dresultdt, float* dresultdr)
        {
            ImageTextureHandle texturefile = verify_texturefile(texture_handle as ImageTextureHandle);
            if (texturefile == null || texturefile.broken)
            {
                missing_texture(ref options, nchannels, result, dresultds, dresultdt);
                if (dresultdr != null)
                {
                    for (int c = 0; c < nchannels; ++c)
                    {
                        dresultdr[c] = 0;
                    }
                }
                return options.missingcolor != null;
            }
            error("3D texture lookups are not supported by this texture system: \"" + texturefile.filename.@string() + "\" is not a volume texture");
            missing_texture(ref options, nchannels, result, dresultds, dresultdt);
            if (dresultdr != null)
            {
                for (int c = 0; c < nchannels; ++c)
                {
                    dresultdr[c] = 0;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // environment() (environment.cpp)
        // ------------------------------------------------------------------------------------------------------------------------------------

        private static void vector_to_latlong(float rx, float ry, float rz, bool y_is_up, out float s, out float t)
        {
            if (y_is_up)
            {
                s = MathF.Atan2(-rx, rz) / (2.0f * (float)Math.PI) + 0.5f;
                t = 0.5f - MathF.Atan2(ry, Hypotf(rz, -rx)) / (float)Math.PI;
            }
            else
            {
                s = MathF.Atan2(ry, rx) / (2.0f * (float)Math.PI) + 0.5f;
                t = 0.5f - MathF.Atan2(rz, Hypotf(rx, ry)) / (float)Math.PI;
            }
            if (float.IsNaN(s))
            {
                s = 0.0f;
            }
            if (float.IsNaN(t))
            {
                t = 0.0f;
            }
        }

        private static float Hypotf(float a, float b)
        {
            // glibc hypotf computes in double precision and rounds once
            return (float)Math.Sqrt((double)a * a + (double)b * b);
        }

        private static void normalize3(ref float x, ref float y, ref float z)
        {
            // Imath::V3f::normalize(): divides by length(), computed with a scaled form that avoids overflow
            float length2 = x * x + y * y + z * z;
            float length;
            if (length2 < 2 * fmath.float_min)
            {
                // Imath's lengthTiny()
                float absMax = Math.Max(MathF.Abs(x), Math.Max(MathF.Abs(y), MathF.Abs(z)));
                if (absMax == 0.0f)
                {
                    return;
                }
                float a = x / absMax, b = y / absMax, c = z / absMax;
                length = absMax * MathF.Sqrt(a * a + b * b + c * c);
            }
            else
            {
                length = MathF.Sqrt(length2);
            }
            if (length == 0.0f)
            {
                return;
            }
            x /= length;
            y /= length;
            z /= length;
        }

        public bool environment(TextureHandle texture_handle, TexturePerthread thread_info, ref TextureOpt options, Vec3 R, Vec3 dRdx, Vec3 dRdy,
                                int nchannels, float* result, float* dresultds, float* dresultdt)
        {
            if (nchannels > 4)
            {
                int save_firstchannel = options.firstchannel;
                while (nchannels != 0)
                {
                    int n = Math.Min(nchannels, 4);
                    bool ok2 = environment(texture_handle, thread_info, ref options, R, dRdx, dRdy, n, result, dresultds, dresultdt);
                    if (!ok2)
                    {
                        return false;
                    }
                    result += n;
                    if (dresultds != null)
                    {
                        dresultds += n;
                    }
                    if (dresultdt != null)
                    {
                        dresultdt += n;
                    }
                    options.firstchannel += n;
                    nchannels -= n;
                }
                options.firstchannel = save_firstchannel;
                return true;
            }
            ImageTextureHandle texturefile = verify_texturefile(texture_handle as ImageTextureHandle);
            if (texturefile == null || texturefile.broken)
            {
                return missing_texture(ref options, nchannels, result, dresultds, dresultdt);
            }
            if (!resolve_subimage(texturefile, ref options))
            {
                return missing_texture(ref options, nchannels, result, dresultds, dresultdt);
            }
            ImageSpecData spec = texturefile.subimages[options.subimage].spec;
            options.swrap = texturefile.sample_border ? TextureOpt.WrapPeriodicSharedBorder : TextureOpt.WrapPeriodic;
            options.twrap = TextureOpt.WrapClamp;
            options.envlayout = LayoutLatLong;
            int actualchannels = Math.Clamp(spec.nchannels - options.firstchannel, 0, nchannels);
            for (int c = 0; c < nchannels; ++c)
            {
                result[c] = 0;
            }
            if (dresultds != null)
            {
                for (int c = 0; c < nchannels; ++c)
                {
                    dresultds[c] = 0;
                }
                for (int c = 0; c < nchannels; ++c)
                {
                    dresultdt[c] = 0;
                }
            }
            if (!(dresultds != null && dresultdt != null))
            {
                dresultds = dresultdt = null;
            }
            float rx = R.x, ry = R.y, rz = R.z;
            normalize3(ref rx, ref ry, ref rz);
            float rxx = R.x + dRdx.x, rxy = R.y + dRdx.y, rxz = R.z + dRdx.z;
            normalize3(ref rxx, ref rxy, ref rxz);
            float ryx = R.x + dRdy.x, ryy = R.y + dRdy.y, ryz = R.z + dRdy.z;
            normalize3(ref ryx, ref ryy, ref ryz);
            float xfilt_noblur = Math.Max(fmath.safe_acos(rx * rxx + ry * rxy + rz * rxz), 1e-8f);
            float yfilt_noblur = Math.Max(fmath.safe_acos(rx * ryx + ry * ryy + rz * ryz), 1e-8f);
            int naturalres = (int)((float)Math.PI / Math.Min(xfilt_noblur, yfilt_noblur));
            float xfilt = xfilt_noblur * options.swidth + options.sblur;
            float yfilt = yfilt_noblur * options.twidth + options.tblur;
            float rmx, rmy, rmz;
            float majorlength, minorlength;
            bool x_is_majoraxis = (xfilt >= yfilt);
            if (x_is_majoraxis)
            {
                rmx = rxx;
                rmy = rxy;
                rmz = rxz;
                majorlength = xfilt;
                minorlength = yfilt;
            }
            else
            {
                rmx = ryx;
                rmy = ryy;
                rmz = ryz;
                majorlength = yfilt;
                minorlength = xfilt;
            }
            Sampler sampler = sampler_for(options.interpmode);
            int mipmode = options.mipmode;
            bool aniso = (mipmode == TextureOpt.MipModeDefault || mipmode == TextureOpt.MipModeAniso);
            float aspect, trueaspect, filtwidth;
            int nsamples;
            float invsamples;
            if (aniso)
            {
                aspect = anisotropic_aspect(ref majorlength, ref minorlength, ref options, out trueaspect);
                filtwidth = minorlength;
                nsamples = Math.Max(1, (int)MathF.Ceiling(aspect - 0.25f));
                invsamples = 1.0f / nsamples;
            }
            else
            {
                filtwidth = options.conservative_filter != 0 ? majorlength : minorlength;
                nsamples = 1;
                invsamples = 1.0f;
            }
            ImageSubimageInfo si = texturefile.subimages[options.subimage];
            int min_mip_level = si.min_mip_level;
            bool ok = true;
            float pos = -0.5f + 0.5f * invsamples;
            for (int sample = 0; sample < nsamples; ++sample, pos += invsamples)
            {
                float rsx = rx + pos * rmx;
                float rsy = ry + pos * rmy;
                float rsz = rz + pos * rmz;
                float s, t;
                vector_to_latlong(rsx, rsy, rsz, texturefile.y_up, out s, out t);
                int[] miplevel = new int[] { -1, -1 };
                float levelblend = 0;
                int nmiplevels = si.levels.Count;
                for (int m = min_mip_level; m < nmiplevels; ++m)
                {
                    ImageLevelInfo dims = si.leveldims(m);
                    float filtwidth_ras = (float)((double)(dims.full_height * filtwidth) * fmath.M_1_PI);
                    if (filtwidth_ras <= 1)
                    {
                        miplevel[0] = m - 1;
                        miplevel[1] = m;
                        levelblend = fmath.clamp(2.0f * filtwidth_ras - 1.0f, 0.0f, 1.0f);
                        break;
                    }
                }
                if (miplevel[1] < 0)
                {
                    miplevel[0] = nmiplevels - 1;
                    miplevel[1] = miplevel[0];
                    levelblend = 0;
                }
                else if (miplevel[0] < min_mip_level)
                {
                    miplevel[0] = min_mip_level;
                    miplevel[1] = min_mip_level;
                    levelblend = 0;
                }
                if (options.mipmode == TextureOpt.MipModeOneLevel)
                {
                    miplevel[1] = miplevel[0];
                    levelblend = 0;
                }
                else if (mipmode == TextureOpt.MipModeNoMIP)
                {
                    miplevel[0] = min_mip_level;
                    miplevel[1] = min_mip_level;
                    levelblend = 0;
                }
                float[] levelweight = new float[] { 1.0f - levelblend, levelblend };
                for (int level = 0; level < 2; ++level)
                {
                    if (levelweight[level] == 0.0f)
                    {
                        continue;
                    }
                    int lev = miplevel[level];
                    if (options.interpmode == TextureOpt.InterpSmartBicubic)
                    {
                        if (lev == 0 || (si.leveldims(lev).full_height < naturalres / 2))
                        {
                            sampler = sample_bicubic;
                        }
                        else
                        {
                            sampler = sample_bilinear;
                        }
                    }
                    float[] sval = new float[] { s };
                    float[] tval = new float[] { t };
                    float[] weight = new float[] { levelweight[level] * invsamples };
                    Float4 r, drds, drdt;
                    ok &= sampler(1, sval, tval, miplevel[level], texturefile, ref options, nchannels, actualchannels, weight, out r, out drds, out drdt,
                                  dresultds != null);
                    for (int c = 0; c < nchannels; ++c)
                    {
                        result[c] += r[c];
                    }
                    if (dresultds != null)
                    {
                        for (int c = 0; c < nchannels; ++c)
                        {
                            dresultds[c] += drds[c];
                            dresultdt[c] += drdt[c];
                        }
                    }
                }
            }
            if (actualchannels < nchannels && options.firstchannel == 0 && m_gray_to_rgb)
            {
                fill_gray_channels(spec, nchannels, result, dresultds, dresultdt);
            }
            return ok;
        }
    }
}
