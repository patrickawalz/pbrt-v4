// OSL port: the part of OpenImageIO texture.h that OSL uses (TextureOpt, TextureSystem, TextureSystem::TextureHandle / Perthread), as an interface
//
// OSL asks the texture system for lookups through RendererServices::texture / texture3d / environment / get_texture_info. The default implementation is
// ImageTextureSystem (texturesys.cs, a port of OIIO's TextureSystem that reads image files itself, see imageinput.cs); a host can supply its own. The
// SimpleTextureSystem below is that default plus the registration of in-memory images (add_image) and a loader callback (image_loader).

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

        public virtual bool good() { return image != null; }
    }

    /// Options of a texture lookup
    public struct TextureOpt
    {
        public const int WrapDefault = 0, WrapBlack = 1, WrapClamp = 2, WrapPeriodic = 3, WrapMirror = 4, WrapPeriodicPow2 = 5,
                         WrapPeriodicSharedBorder = 6, WrapLast = 7;
        public const int MipModeDefault = 0, MipModeNoMIP = 1, MipModeOneLevel = 2, MipModeTrilinear = 3, MipModeAniso = 4, MipModeStochasticTrilinear = 5,
                         MipModeStochasticAniso = 6;
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

    /// The default TextureSystem with in-memory images that a test or a host registers by name (add_image) and an optional loader callback
    public unsafe class SimpleTextureSystem : ImageTextureSystem
    {
        /// Called for a file that was not registered with add_image; return null if the file can't be loaded
        public Func<string, SimpleImage> image_loader;

        public void add_image(string filename, SimpleImage image)
        {
            add_file(filename, to_file_data(image));
        }

        private static ImageFileData to_file_data(SimpleImage image)
        {
            ImageSpecData spec = ImageReaders.make_spec(image.width, image.height, image.nchannels, TypeDesc.FLOAT);
            if (image.nchannels == 2)
            {
                spec.alpha_channel = 1;
            }
            else if (image.nchannels == 4)
            {
                spec.alpha_channel = 3;
            }
            if (image.texturetype != "Plain Texture")
            {
                spec.attributes["textureformat"] = image.texturetype == "Environment" ? "LatLong Environment" : image.texturetype;
            }
            return ImageReaders.single("memory", spec, (float[])image.pixels.Clone());
        }

        protected override ImageFileData load_file(string filename, out string error)
        {
            error = "";
            if (image_loader != null)
            {
                SimpleImage img = image_loader(filename);
                if (img != null)
                {
                    return to_file_data(img);
                }
            }
            return base.load_file(filename, out error);
        }
    }
}
