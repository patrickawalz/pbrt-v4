// OSL port: the part of OpenImageIO texture.h that OSL uses (TextureOpt, TextureSystem, TextureSystem::TextureHandle / Perthread), as an interface
//
// OSL asks the texture system for lookups through RendererServices::texture / texture3d / environment / get_texture_info. The production image I/O is
// an extension point: the host supplies its own TextureSystem. SimpleTextureSystem is the default, with in-memory images that a test or a host
// registers by name (add_image), an optional loader callback (image_loader) and a built-in loader for binary PPM/PGM and PFM files.

using System;
using System.Collections.Generic;
using System.IO;
using OSL;

namespace OIIO
{
    /// Per-thread data of a texture system (the opaque TextureSystem::Perthread)
    public class TexturePerthread
    {
    }

    /// An opened texture (the opaque TextureSystem::TextureHandle)
    public class TextureHandle
    {
        public ustring filename;
        public SimpleImage image;
        public bool is_udim_pattern;
        public string error;

        public bool good() { return image != null; }
    }

    /// Options of a texture lookup
    public struct TextureOpt
    {
        public const int WrapDefault = 0, WrapBlack = 1, WrapClamp = 2, WrapPeriodic = 3, WrapMirror = 4, WrapPeriodicPow2 = 5,
                         WrapPeriodicSharedBorder = 6, WrapLast = 7;
        public const int MipModeDefault = 0, MipModeNoMIP = 1, MipModeOneLevel = 2, MipModeTrilinear = 3, MipModeAniso = 4;
        public const int InterpClosest = 0, InterpBilinear = 1, InterpBicubic = 2, InterpSmartBicubic = 3;

        public int firstchannel;
        public int subimage;
        public ustring subimagename;
        public int swrap, twrap, rwrap;
        public int mipmode;
        public int interpmode;
        public int anisotropic;
        public int conservative_filter;
        public float sblur, tblur, rblur;
        public float swidth, twidth, rwidth;
        public bool do_resize;
        public float fill;
        public float[] missingcolor;
        public float time;
        public float rnd;
        public int samples;
        public int envlayout;

        public static TextureOpt make_default()
        {
            TextureOpt o = new TextureOpt();
            o.firstchannel = 0;
            o.subimage = 0;
            o.subimagename = new ustring("");
            o.swrap = WrapDefault;
            o.twrap = WrapDefault;
            o.rwrap = WrapDefault;
            o.mipmode = MipModeDefault;
            o.interpmode = InterpSmartBicubic;
            o.anisotropic = 32;
            o.conservative_filter = 1;
            o.sblur = 0.0f;
            o.tblur = 0.0f;
            o.rblur = 0.0f;
            o.swidth = 1.0f;
            o.twidth = 1.0f;
            o.rwidth = 1.0f;
            o.fill = 0.0f;
            o.missingcolor = null;
            o.time = 0.0f;
            o.rnd = -1.0f;
            o.samples = 1;
            return o;
        }
    }

    /// The part of OIIO::TextureSystem that OSL calls
    public unsafe interface TextureSystem
    {
        bool attribute(string name, TypeDesc type, object val);
        bool attribute(string name, int val);
        bool attribute(string name, float val);
        bool attribute(string name, string val);

        TexturePerthread get_perthread_info(TexturePerthread thread_info = null);

        TextureHandle get_texture_handle(ustring filename, TexturePerthread thread_info, TextureOpt* options = null);

        bool good(TextureHandle texture_handle);
        bool is_udim(TextureHandle texture_handle);
        TextureHandle resolve_udim(TextureHandle udim_handle, TexturePerthread thread_info, float s, float t);

        bool texture(TextureHandle texture_handle, TexturePerthread thread_info, ref TextureOpt options, float s, float t, float dsdx, float dtdx,
                     float dsdy, float dtdy, int nchannels, float* result, float* dresultds, float* dresultdt);

        bool texture3d(TextureHandle texture_handle, TexturePerthread thread_info, ref TextureOpt options, Vec3 P, Vec3 dPdx, Vec3 dPdy,
                       Vec3 dPdz, int nchannels, float* result, float* dresultds, float* dresultdt, float* dresultdr);

        bool environment(TextureHandle texture_handle, TexturePerthread thread_info, ref TextureOpt options, Vec3 R, Vec3 dRdx, Vec3 dRdy,
                         int nchannels, float* result, float* dresultds, float* dresultdt);

        /// Query a named piece of information. 'data' is an array that receives the value (int[], float[] or ustring[]).
        bool get_texture_info(TextureHandle texture_handle, TexturePerthread thread_info, int subimage, ustring dataname, TypeDesc datatype,
                              object data);

        /// The message of the last error (and clear it)
        string geterror(bool clear = true);
    }

    /// An image in memory: width x height pixels of nchannels floats
    public class SimpleImage
    {
        public int width;
        public int height;
        public int nchannels;
        public float[] pixels;
        public string texturetype = "Plain Texture";

        public SimpleImage(int w, int h, int nch)
        {
            width = w;
            height = h;
            nchannels = nch;
            pixels = new float[w * h * nch];
        }

        public float get(int x, int y, int c)
        {
            return pixels[(y * width + x) * nchannels + c];
        }

        public void set(int x, int y, int c, float v)
        {
            pixels[(y * width + x) * nchannels + c] = v;
        }
    }

    /// The default TextureSystem: images held in memory, closest and bilinear lookups, wrap modes, lat-long environments
    public unsafe class SimpleTextureSystem : TextureSystem
    {
        private readonly Dictionary<string, SimpleImage> m_images = new Dictionary<string, SimpleImage>(StringComparer.Ordinal);
        private readonly Dictionary<string, TextureHandle> m_handles = new Dictionary<string, TextureHandle>(StringComparer.Ordinal);
        private readonly object m_lock = new object();
        private string m_error = "";

        /// Called for a file that was not registered with add_image; return null if the file can't be loaded
        public Func<string, SimpleImage> image_loader;

        public void add_image(string filename, SimpleImage image)
        {
            lock (m_lock)
            {
                m_images[filename] = image;
                m_handles.Remove(filename);
            }
        }

        public bool attribute(string name, TypeDesc type, object val) { return false; }
        public bool attribute(string name, int val) { return false; }
        public bool attribute(string name, float val) { return false; }
        public bool attribute(string name, string val) { return false; }

        public TexturePerthread get_perthread_info(TexturePerthread thread_info = null)
        {
            return thread_info != null ? thread_info : new TexturePerthread();
        }

        public TextureHandle get_texture_handle(ustring filename, TexturePerthread thread_info, TextureOpt* options = null)
        {
            lock (m_lock)
            {
                string name = filename.@string();
                TextureHandle h;
                if (m_handles.TryGetValue(name, out h))
                {
                    return h;
                }
                h = new TextureHandle();
                h.filename = filename;
                SimpleImage img;
                if (!m_images.TryGetValue(name, out img))
                {
                    img = image_loader != null ? image_loader(name) : null;
                    if (img == null)
                    {
                        img = load_builtin(name);
                    }
                }
                h.image = img;
                if (img == null)
                {
                    h.error = "Invalid image file \"" + name + "\"";
                }
                m_handles[name] = h;
                return h;
            }
        }

        public bool good(TextureHandle texture_handle) { return texture_handle != null && texture_handle.good(); }
        public bool is_udim(TextureHandle texture_handle) { return texture_handle != null && texture_handle.is_udim_pattern; }

        public TextureHandle resolve_udim(TextureHandle udim_handle, TexturePerthread thread_info, float s, float t)
        {
            return null;
        }

        private static int wrap_coord(int x, int size, int mode)
        {
            switch (mode)
            {
                case TextureOpt.WrapBlack:
                    return (x < 0 || x >= size) ? -1 : x;
                case TextureOpt.WrapPeriodic:
                case TextureOpt.WrapPeriodicPow2:
                case TextureOpt.WrapPeriodicSharedBorder:
                    {
                        int m = x % size;
                        return m < 0 ? m + size : m;
                    }
                case TextureOpt.WrapMirror:
                    {
                        int period = 2 * size;
                        int m = x % period;
                        if (m < 0)
                        {
                            m += period;
                        }
                        return m >= size ? period - 1 - m : m;
                    }
                default:
                    return x < 0 ? 0 : (x >= size ? size - 1 : x);
            }
        }

        private float texel(SimpleImage img, int x, int y, int c, int swrap, int twrap, float fill)
        {
            int wx = wrap_coord(x, img.width, swrap);
            int wy = wrap_coord(y, img.height, twrap);
            if (wx < 0 || wy < 0)
            {
                return 0.0f;
            }
            if (c >= img.nchannels)
            {
                return fill;
            }
            return img.get(wx, wy, c);
        }

        private void lookup(SimpleImage img, ref TextureOpt opt, float s, float t, int nchannels, float* result)
        {
            // s,t in [0,1] with t=0 at the top row, as image rows are stored top to bottom
            float x = s * img.width - 0.5f;
            float y = t * img.height - 0.5f;
            for (int c = 0; c < nchannels; ++c)
            {
                int ch = opt.firstchannel + c;
                float v;
                if (opt.interpmode == TextureOpt.InterpClosest)
                {
                    v = texel(img, (int)Math.Floor(x + 0.5f), (int)Math.Floor(y + 0.5f), ch, opt.swrap, opt.twrap, opt.fill);
                }
                else
                {
                    int x0 = (int)Math.Floor(x);
                    int y0 = (int)Math.Floor(y);
                    float fx = x - x0;
                    float fy = y - y0;
                    float a = texel(img, x0, y0, ch, opt.swrap, opt.twrap, opt.fill);
                    float b = texel(img, x0 + 1, y0, ch, opt.swrap, opt.twrap, opt.fill);
                    float cc = texel(img, x0, y0 + 1, ch, opt.swrap, opt.twrap, opt.fill);
                    float d = texel(img, x0 + 1, y0 + 1, ch, opt.swrap, opt.twrap, opt.fill);
                    v = (a * (1 - fx) + b * fx) * (1 - fy) + (cc * (1 - fx) + d * fx) * fy;
                }
                result[c] = v;
            }
        }

        public bool texture(TextureHandle texture_handle, TexturePerthread thread_info, ref TextureOpt options, float s, float t, float dsdx, float dtdx,
                            float dsdy, float dtdy, int nchannels, float* result, float* dresultds, float* dresultdt)
        {
            if (!good(texture_handle))
            {
                m_error = texture_handle != null && texture_handle.error != null ? texture_handle.error : "Invalid texture handle";
                return false;
            }
            lookup(texture_handle.image, ref options, s, t, nchannels, result);
            if (dresultds != null || dresultdt != null)
            {
                SimpleImage img = texture_handle.image;
                float ds = 1.0f / img.width;
                float dt = 1.0f / img.height;
                float* tmp0 = stackalloc float[nchannels];
                float* tmp1 = stackalloc float[nchannels];
                if (dresultds != null)
                {
                    lookup(img, ref options, s + ds, t, nchannels, tmp1);
                    lookup(img, ref options, s - ds, t, nchannels, tmp0);
                    for (int c = 0; c < nchannels; ++c)
                    {
                        dresultds[c] = (tmp1[c] - tmp0[c]) / (2 * ds);
                    }
                }
                if (dresultdt != null)
                {
                    lookup(img, ref options, s, t + dt, nchannels, tmp1);
                    lookup(img, ref options, s, t - dt, nchannels, tmp0);
                    for (int c = 0; c < nchannels; ++c)
                    {
                        dresultdt[c] = (tmp1[c] - tmp0[c]) / (2 * dt);
                    }
                }
            }
            return true;
        }

        public bool texture3d(TextureHandle texture_handle, TexturePerthread thread_info, ref TextureOpt options, Vec3 P, Vec3 dPdx, Vec3 dPdy,
                              Vec3 dPdz, int nchannels, float* result, float* dresultds, float* dresultdt, float* dresultdr)
        {
            m_error = "3D textures are not supported by the simple texture system";
            return false;
        }

        public bool environment(TextureHandle texture_handle, TexturePerthread thread_info, ref TextureOpt options, Vec3 R, Vec3 dRdx, Vec3 dRdy,
                                int nchannels, float* result, float* dresultds, float* dresultdt)
        {
            if (!good(texture_handle))
            {
                m_error = texture_handle != null && texture_handle.error != null ? texture_handle.error : "Invalid texture handle";
                return false;
            }
            // lat-long layout: s from the azimuth about +y, t from the polar angle
            double len = Math.Sqrt(R.x * R.x + R.y * R.y + R.z * R.z);
            if (len == 0.0)
            {
                len = 1.0;
            }
            double theta = Math.Acos(Math.Max(-1.0, Math.Min(1.0, R.y / len)));
            double phi = Math.Atan2(R.x, -R.z);
            float s = (float)(phi / (2.0 * Math.PI) + 0.5);
            float t = (float)(theta / Math.PI);
            TextureOpt o = options;
            o.swrap = TextureOpt.WrapPeriodic;
            o.twrap = TextureOpt.WrapClamp;
            lookup(texture_handle.image, ref o, s, t, nchannels, result);
            if (dresultds != null)
            {
                for (int c = 0; c < nchannels; ++c)
                {
                    dresultds[c] = 0.0f;
                }
            }
            if (dresultdt != null)
            {
                for (int c = 0; c < nchannels; ++c)
                {
                    dresultdt[c] = 0.0f;
                }
            }
            return true;
        }

        public bool get_texture_info(TextureHandle texture_handle, TexturePerthread thread_info, int subimage, ustring dataname, TypeDesc datatype,
                                     object data)
        {
            if (texture_handle == null)
            {
                m_error = "Invalid texture handle";
                return false;
            }
            string name = dataname.@string();
            if (name == "exists")
            {
                if (data is int[] && datatype.basetype == TypeDesc.INT)
                {
                    ((int[])data)[0] = texture_handle.good() ? 1 : 0;
                    return true;
                }
                return false;
            }
            if (!texture_handle.good())
            {
                m_error = texture_handle.error != null ? texture_handle.error : "Invalid texture handle";
                return false;
            }
            SimpleImage img = texture_handle.image;
            if (name == "resolution" && data is int[] && datatype.basetype == TypeDesc.INT && datatype.basevalues() >= 2)
            {
                ((int[])data)[0] = img.width;
                ((int[])data)[1] = img.height;
                return true;
            }
            if (name == "channels" && data is int[] && datatype.basetype == TypeDesc.INT)
            {
                ((int[])data)[0] = img.nchannels;
                return true;
            }
            if (name == "texturetype" && data is ustring[] && datatype.basetype == TypeDesc.STRING)
            {
                ((ustring[])data)[0] = new ustring(img.texturetype);
                return true;
            }
            if (name == "textureformat" && data is ustring[] && datatype.basetype == TypeDesc.STRING)
            {
                ((ustring[])data)[0] = new ustring(img.texturetype);
                return true;
            }
            m_error = "Unknown texture query \"" + name + "\"";
            return false;
        }

        public string geterror(bool clear = true)
        {
            string e = m_error;
            if (clear)
            {
                m_error = "";
            }
            return e;
        }

        // Binary PPM (P6) and PGM (P5), and PFM (Pf, PF) files
        private static SimpleImage load_builtin(string filename)
        {
            try
            {
                if (!File.Exists(filename))
                {
                    return null;
                }
                byte[] bytes = File.ReadAllBytes(filename);
                int pos = 0;
                string magic = read_token(bytes, ref pos);
                if (magic == "P6" || magic == "P5")
                {
                    int w = int.Parse(read_token(bytes, ref pos));
                    int h = int.Parse(read_token(bytes, ref pos));
                    int maxval = int.Parse(read_token(bytes, ref pos));
                    ++pos;
                    int nch = magic == "P6" ? 3 : 1;
                    SimpleImage img = new SimpleImage(w, h, nch);
                    for (int i = 0; i < w * h * nch; ++i)
                    {
                        img.pixels[i] = bytes[pos + i] / (float)maxval;
                    }
                    return img;
                }
                if (magic == "PF" || magic == "Pf")
                {
                    int nch = magic == "PF" ? 3 : 1;
                    int w = int.Parse(read_token(bytes, ref pos));
                    int h = int.Parse(read_token(bytes, ref pos));
                    float scale = float.Parse(read_token(bytes, ref pos), System.Globalization.CultureInfo.InvariantCulture);
                    ++pos;
                    bool little = scale < 0;
                    SimpleImage img = new SimpleImage(w, h, nch);
                    for (int y = 0; y < h; ++y)
                    {
                        // PFM stores the bottom row first
                        int row = h - 1 - y;
                        for (int i = 0; i < w * nch; ++i)
                        {
                            byte[] b = new byte[4];
                            Array.Copy(bytes, pos + (y * w * nch + i) * 4, b, 0, 4);
                            if (little != BitConverter.IsLittleEndian)
                            {
                                Array.Reverse(b);
                            }
                            img.pixels[row * w * nch + i] = BitConverter.ToSingle(b, 0);
                        }
                    }
                    return img;
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static string read_token(byte[] bytes, ref int pos)
        {
            while (pos < bytes.Length && (bytes[pos] == ' ' || bytes[pos] == '\n' || bytes[pos] == '\r' || bytes[pos] == '\t'))
            {
                ++pos;
            }
            int start = pos;
            while (pos < bytes.Length && !(bytes[pos] == ' ' || bytes[pos] == '\n' || bytes[pos] == '\r' || bytes[pos] == '\t'))
            {
                ++pos;
            }
            return System.Text.Encoding.ASCII.GetString(bytes, start, pos - start);
        }
    }
}
