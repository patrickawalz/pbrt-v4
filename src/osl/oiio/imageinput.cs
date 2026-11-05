// OSL port: the image file input that the texture system needs (OpenImageIO's ImageInput plugins for the formats that textures come in).
//
// The C++ TextureSystem reads files through ImageInput plugins (tiffinput.cpp, hdrinput.cpp, pnginput.cpp, ppminput.cpp, pfminput.cpp, ...) and
// ImageCache. This file holds the managed equivalents: ImageSpecData is the part of ImageSpec the texture code looks at, and the readers decode a whole
// file into float pixels. The pixels of 8 and 16 bit files are converted exactly as OIIO's texture system converts the cached tiles (a UINT8 value c
// becomes (float)c * (1.0f / 255.0f), a UINT16 value s becomes (float)s * (1.0f / 65535.0f), a half is widened).
//
// Formats: TIFF (strips and tiles; none, LZW, Deflate and PackBits; 8/16 bit integer, 16/32 bit float; horizontal and floating point predictors; MIP levels
// stored as IFDs of a "textureformat" file as written by maketx), Radiance HDR, PNG (non interlaced), PPM/PGM (P5, P6) and PFM, BMP (uncompressed 24 and 32 bit).
// OpenEXR is read by exrinput.cs and JPEG by jpeginput.cs. Other formats go through ImageReaders.external_reader, which a host sets to its own decoder; without one such files are reported as
// invalid, as the C++ does for a file that no plugin can open.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OIIO
{
    /// The part of OIIO::ImageSpec that the texture system uses
    public sealed class ImageSpecData
    {
        public int x, y;
        public int width, height;
        public int full_x, full_y;
        public int full_width, full_height;
        public int tile_width, tile_height;
        public int nchannels;
        public int alpha_channel = -1;
        public int format = TypeDesc.UINT8;
        public Dictionary<string, object> attributes = new Dictionary<string, object>(StringComparer.Ordinal);

        public ImageSpecData clone()
        {
            ImageSpecData c = (ImageSpecData)MemberwiseClone();
            c.attributes = new Dictionary<string, object>(attributes, StringComparer.Ordinal);
            return c;
        }

        public string get_string_attribute(string name, string defaultval = "")
        {
            object v;
            if (attributes.TryGetValue(name, out v) && v is string)
            {
                return (string)v;
            }
            return defaultval;
        }

        public int get_int_attribute(string name, int defaultval = 0)
        {
            object v;
            if (attributes.TryGetValue(name, out v) && v is int)
            {
                return (int)v;
            }
            return defaultval;
        }
    }

    /// One MIP level of one subimage: the spec of that level and its pixels as floats (top row first, nchannels interleaved)
    public sealed class ImageLevelData
    {
        public ImageSpecData spec;
        public float[] pixels;
    }

    public sealed class ImageSubimageData
    {
        public List<ImageLevelData> levels = new List<ImageLevelData>();
    }

    public sealed class ImageFileData
    {
        public string format_name = "";
        public List<ImageSubimageData> subimages = new List<ImageSubimageData>();
    }

    public static class ImageReaders
    {
        /// A host decoder for formats that are not built in; returns null if it can't read the file
        public static Func<string, ImageFileData> external_reader;

        public const float u8scale = 1.0f / 255.0f;
        public const float u16scale = 1.0f / 65535.0f;

        /// Read 'filename'; on failure returns null and sets 'error' (the text OIIO's open() would report)
        public static ImageFileData read(string filename, out string error)
        {
            error = "";
            byte[] bytes;
            try
            {
                if (!File.Exists(filename))
                {
                    error = "Could not open file";
                    if (external_reader != null)
                    {
                        ImageFileData ext = external_reader(filename);
                        if (ext != null)
                        {
                            error = "";
                            return ext;
                        }
                    }
                    return null;
                }
                bytes = File.ReadAllBytes(filename);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
            try
            {
                ImageFileData r = null;
                if (bytes.Length >= 4 && ((bytes[0] == 'I' && bytes[1] == 'I' && bytes[2] == 42 && bytes[3] == 0) ||
                                          (bytes[0] == 'M' && bytes[1] == 'M' && bytes[2] == 0 && bytes[3] == 42)))
                {
                    r = TiffReader.read(bytes, out error);
                }
                else if (bytes.Length >= 2 && bytes[0] == '#' && bytes[1] == '?')
                {
                    r = HdrReader.read(bytes, out error);
                }
                else if (ExrReader.is_exr(bytes))
                {
                    r = ExrReader.read(bytes, out error);
                }
                else if (JpegReader.is_jpeg(bytes))
                {
                    r = JpegReader.read(bytes, out error);
                }
                else if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
                {
                    r = PngReader.read(bytes, out error);
                }
                else if (bytes.Length >= 2 && bytes[0] == 'P' && (bytes[1] == '5' || bytes[1] == '6' || bytes[1] == 'f' || bytes[1] == 'F'))
                {
                    r = PnmReader.read(bytes, out error);
                }
                else if (bytes.Length >= 2 && bytes[0] == 'B' && bytes[1] == 'M')
                {
                    r = BmpReader.read(bytes, out error);
                }
                else if (external_reader != null)
                {
                    r = external_reader(filename);
                    if (r == null)
                    {
                        error = "Could not open file: unsupported image format";
                    }
                }
                else
                {
                    error = "Could not open file: unsupported image format";
                }
                if (r == null && external_reader != null)
                {
                    // A format that is built in but that the built in decoder doesn't handle (interlaced PNG, other TIFF variants, ...) is left to the host
                    ImageFileData ext = external_reader(filename);
                    if (ext != null)
                    {
                        error = "";
                        r = ext;
                    }
                }
                return r;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        internal static ImageSpecData make_spec(int w, int h, int nch, int format)
        {
            ImageSpecData s = new ImageSpecData();
            s.x = 0;
            s.y = 0;
            s.width = w;
            s.height = h;
            s.full_x = 0;
            s.full_y = 0;
            s.full_width = w;
            s.full_height = h;
            s.nchannels = nch;
            s.format = format;
            s.tile_width = 0;
            s.tile_height = 0;
            return s;
        }

        internal static ImageFileData single(string format_name, ImageSpecData spec, float[] pixels)
        {
            ImageFileData f = new ImageFileData();
            f.format_name = format_name;
            ImageSubimageData si = new ImageSubimageData();
            ImageLevelData lv = new ImageLevelData();
            lv.spec = spec;
            lv.pixels = pixels;
            si.levels.Add(lv);
            f.subimages.Add(si);
            return f;
        }

        /// Quantize the way OIIO's convert_type<float, T> does for the integer formats (round half away from zero, clamp), then widen again the way the
        /// texture lookup reads the cached tile: this is what the ImageCache does to the automipped levels of an 8 or 16 bit file
        public static float quantize(float v, int format)
        {
            if (format == TypeDesc.UINT8)
            {
                float s = v * 255.0f;
                s += (s < 0.0f ? -0.5f : 0.5f);
                if (s < 0.0f)
                {
                    s = 0.0f;
                }
                if (s > 255.0f)
                {
                    s = 255.0f;
                }
                return (float)(byte)s * u8scale;
            }
            if (format == TypeDesc.UINT16)
            {
                float s = v * 65535.0f;
                s += (s < 0.0f ? -0.5f : 0.5f);
                if (s < 0.0f)
                {
                    s = 0.0f;
                }
                if (s > 65535.0f)
                {
                    s = 65535.0f;
                }
                return (float)(ushort)s * u16scale;
            }
            if (format == TypeDesc.HALF)
            {
                return (float)(Half)v;
            }
            return v;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------------
    // TIFF (tiffinput.cpp)
    // ----------------------------------------------------------------------------------------------------------------------------------------------

    internal sealed class TiffReader
    {
        private byte[] m_data;
        private bool m_little;

        private sealed class Entry
        {
            public int tag;
            public int type;
            public long count;
            public long valueoffset;
            public long fieldpos;
        }

        private sealed class Ifd
        {
            public Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        }

        private ushort u16(long pos)
        {
            if (m_little)
            {
                return (ushort)(m_data[pos] | (m_data[pos + 1] << 8));
            }
            return (ushort)((m_data[pos] << 8) | m_data[pos + 1]);
        }

        private uint u32(long pos)
        {
            if (m_little)
            {
                return (uint)(m_data[pos] | (m_data[pos + 1] << 8) | (m_data[pos + 2] << 16) | (m_data[pos + 3] << 24));
            }
            return (uint)((m_data[pos] << 24) | (m_data[pos + 1] << 16) | (m_data[pos + 2] << 8) | m_data[pos + 3]);
        }

        private ulong u64(long pos)
        {
            ulong a = u32(pos);
            ulong b = u32(pos + 4);
            return m_little ? (a | (b << 32)) : ((a << 32) | b);
        }

        private static int type_size(int type)
        {
            switch (type)
            {
                case 1:
                case 2:
                case 6:
                case 7:
                    return 1;
                case 3:
                case 8:
                    return 2;
                case 4:
                case 9:
                case 11:
                    return 4;
                case 5:
                case 10:
                case 12:
                case 16:
                case 17:
                case 18:
                    return 8;
                default:
                    return 1;
            }
        }

        private long value_position(Entry e)
        {
            long total = e.count * type_size(e.type);
            if (total <= 4)
            {
                return e.fieldpos;
            }
            return e.valueoffset;
        }

        private long get_int(Ifd ifd, int tag, long defaultval, int index = 0)
        {
            Entry e;
            if (!ifd.entries.TryGetValue(tag, out e) || index >= e.count)
            {
                return defaultval;
            }
            long p = value_position(e) + (long)index * type_size(e.type);
            switch (e.type)
            {
                case 1:
                case 7:
                    return m_data[p];
                case 6:
                    return (sbyte)m_data[p];
                case 3:
                    return u16(p);
                case 8:
                    return (short)u16(p);
                case 4:
                    return u32(p);
                case 9:
                    return (int)u32(p);
                case 16:
                    return (long)u64(p);
                default:
                    return defaultval;
            }
        }

        private string get_string(Ifd ifd, int tag)
        {
            Entry e;
            if (!ifd.entries.TryGetValue(tag, out e) || e.type != 2)
            {
                return null;
            }
            long p = value_position(e);
            int n = (int)e.count;
            while (n > 0 && m_data[p + n - 1] == 0)
            {
                --n;
            }
            return Encoding.UTF8.GetString(m_data, (int)p, n);
        }

        private Ifd read_ifd(long offset, out long next)
        {
            Ifd ifd = new Ifd();
            int n = u16(offset);
            for (int i = 0; i < n; ++i)
            {
                long p = offset + 2 + 12L * i;
                Entry e = new Entry();
                e.tag = u16(p);
                e.type = u16(p + 2);
                e.count = u32(p + 4);
                e.fieldpos = p + 8;
                e.valueoffset = u32(p + 8);
                ifd.entries[e.tag] = e;
            }
            next = u32(offset + 2 + 12L * n);
            return ifd;
        }

        public static ImageFileData read(byte[] data, out string error)
        {
            TiffReader r = new TiffReader();
            r.m_data = data;
            r.m_little = data[0] == 'I';
            return r.read_all(out error);
        }

        private ImageFileData read_all(out string error)
        {
            error = "";
            List<Ifd> ifds = new List<Ifd>();
            long off = u32(4);
            int guard = 0;
            while (off != 0 && guard++ < 4096)
            {
                long next;
                ifds.Add(read_ifd(off, out next));
                off = next;
            }
            if (ifds.Count == 0)
            {
                error = "Could not open file: no image directory";
                return null;
            }
            ImageFileData file = new ImageFileData();
            file.format_name = "tiff";
            bool emulate_mipmap = ifds[0].entries.ContainsKey(33302);
            ImageSubimageData current = null;
            for (int i = 0; i < ifds.Count; ++i)
            {
                ImageLevelData level = read_level(ifds[i], out error);
                if (level == null)
                {
                    return null;
                }
                if (emulate_mipmap)
                {
                    if (current == null)
                    {
                        current = new ImageSubimageData();
                        file.subimages.Add(current);
                    }
                    current.levels.Add(level);
                }
                else
                {
                    ImageSubimageData si = new ImageSubimageData();
                    si.levels.Add(level);
                    file.subimages.Add(si);
                }
            }
            return file;
        }

        private ImageLevelData read_level(Ifd ifd, out string error)
        {
            error = "";
            int width = (int)get_int(ifd, 256, 0);
            int height = (int)get_int(ifd, 257, 0);
            int spp = (int)get_int(ifd, 277, 1);
            int bps = (int)get_int(ifd, 258, 1);
            int compression = (int)get_int(ifd, 259, 1);
            int photometric = (int)get_int(ifd, 262, 1);
            int planar = (int)get_int(ifd, 284, 1);
            int predictor = (int)get_int(ifd, 317, 1);
            int sampleformat = (int)get_int(ifd, 339, 1);
            int extrasamples = (int)get_int(ifd, 338, 0);
            bool tiled = ifd.entries.ContainsKey(322);
            if (width <= 0 || height <= 0)
            {
                error = "Could not open file: bad image size";
                return null;
            }
            if (planar != 1)
            {
                error = "Could not open file: separate planar configuration is not supported";
                return null;
            }
            if (photometric == 3)
            {
                error = "Could not open file: palette images are not supported";
                return null;
            }
            int format;
            if (bps == 8 && sampleformat != 3)
            {
                format = TypeDesc.UINT8;
            }
            else if (bps == 16 && sampleformat == 3)
            {
                format = TypeDesc.HALF;
            }
            else if (bps == 16)
            {
                format = TypeDesc.UINT16;
            }
            else if (bps == 32 && sampleformat == 3)
            {
                format = TypeDesc.FLOAT;
            }
            else
            {
                error = "Could not open file: unsupported bit depth " + bps;
                return null;
            }
            int bytesPerSample = bps / 8;
            ImageSpecData spec = ImageReaders.make_spec(width, height, spp, format);
            bool assoc_alpha = false;
            if (extrasamples != 0 || ifd.entries.ContainsKey(338))
            {
                int es = (int)get_int(ifd, 338, 0);
                if (es == 1 || es == 2)
                {
                    spec.alpha_channel = spp - 1;
                    assoc_alpha = (es == 1);
                }
            }
            else if (spp == 2 && photometric == 1)
            {
                spec.alpha_channel = 1;
            }
            string desc = get_string(ifd, 270);
            string software = get_string(ifd, 305);
            string textureformat = get_string(ifd, 33302);
            string wrapmodes = get_string(ifd, 33303);
            if (software != null)
            {
                spec.attributes["Software"] = software;
            }
            if (textureformat != null)
            {
                spec.attributes["textureformat"] = textureformat;
            }
            if (wrapmodes != null)
            {
                spec.attributes["wrapmodes"] = wrapmodes;
            }
            if (desc != null)
            {
                string ac = excise_string_after_head(ref desc, "oiio:AverageColor=");
                if (ac.Length > 0)
                {
                    spec.attributes["oiio:AverageColor"] = ac;
                }
                string sha = excise_string_after_head(ref desc, "oiio:SHA-1=");
                if (sha.Length == 0)
                {
                    sha = excise_string_after_head(ref desc, "SHA-1=");
                }
                if (sha.Length > 0)
                {
                    spec.attributes["oiio:SHA-1"] = sha;
                }
                string cc = excise_string_after_head(ref desc, "oiio:ConstantColor=");
                if (cc.Length > 0)
                {
                    spec.attributes["oiio:ConstantColor"] = cc;
                }
                desc = desc.Trim();
                if (desc.Length > 0)
                {
                    spec.attributes["ImageDescription"] = desc;
                }
            }
            spec.attributes["Orientation"] = (int)get_int(ifd, 274, 1);
            spec.attributes["tiff:Compression"] = compression;
            spec.attributes["tiff:PhotometricInterpretation"] = photometric;
            float[] pixels = new float[(long)width * height * spp];
            int pixelBytes = spp * bytesPerSample;
            if (tiled)
            {
                int tw = (int)get_int(ifd, 322, 0);
                int th = (int)get_int(ifd, 323, 0);
                spec.tile_width = tw;
                spec.tile_height = th;
                int tilesX = (width + tw - 1) / tw;
                int tilesY = (height + th - 1) / th;
                for (int ty = 0; ty < tilesY; ++ty)
                {
                    for (int tx = 0; tx < tilesX; ++tx)
                    {
                        int idx = ty * tilesX + tx;
                        long offs = get_int(ifd, 324, 0, idx);
                        long cnt = get_int(ifd, 325, 0, idx);
                        byte[] raw = decode_block(offs, cnt, compression, predictor, tw, th, spp, bytesPerSample, tw * pixelBytes * th, out error);
                        if (raw == null)
                        {
                            return null;
                        }
                        for (int row = 0; row < th; ++row)
                        {
                            int y = ty * th + row;
                            if (y >= height)
                            {
                                break;
                            }
                            for (int col = 0; col < tw; ++col)
                            {
                                int x = tx * tw + col;
                                if (x >= width)
                                {
                                    break;
                                }
                                convert_pixel(raw, (row * tw + col) * pixelBytes, pixels, ((long)y * width + x) * spp, spp, format, photometric);
                            }
                        }
                    }
                }
            }
            else
            {
                int rps = (int)get_int(ifd, 278, height);
                if (rps <= 0 || rps > height)
                {
                    rps = height;
                }
                spec.attributes["tiff:RowsPerStrip"] = rps;
                int nstrips = (height + rps - 1) / rps;
                for (int s = 0; s < nstrips; ++s)
                {
                    long offs = get_int(ifd, 273, 0, s);
                    long cnt = get_int(ifd, 279, 0, s);
                    int rows = Math.Min(rps, height - s * rps);
                    byte[] raw = decode_block(offs, cnt, compression, predictor, width, rows, spp, bytesPerSample, width * pixelBytes * rows, out error);
                    if (raw == null)
                    {
                        return null;
                    }
                    for (int row = 0; row < rows; ++row)
                    {
                        int y = s * rps + row;
                        for (int col = 0; col < width; ++col)
                        {
                            convert_pixel(raw, (row * width + col) * pixelBytes, pixels, ((long)y * width + col) * spp, spp, format, photometric);
                        }
                    }
                }
            }
            // Unassociated alpha is converted to associated (premultiplied) on read unless asked not to, as TIFFInput does
            if (spec.alpha_channel >= 0 && !assoc_alpha && (ifd.entries.ContainsKey(338)))
            {
                int a = spec.alpha_channel;
                for (long p = 0; p < (long)width * height; ++p)
                {
                    float alpha = pixels[p * spp + a];
                    for (int c = 0; c < spp; ++c)
                    {
                        if (c != a)
                        {
                            pixels[p * spp + c] *= alpha;
                        }
                    }
                }
            }
            ImageLevelData level = new ImageLevelData();
            level.spec = spec;
            level.pixels = pixels;
            return level;
        }

        private static string excise_string_after_head(ref string str, string head)
        {
            int pos = str.IndexOf(head, StringComparison.Ordinal);
            if (pos < 0)
            {
                return "";
            }
            int start = pos + head.Length;
            int end = start;
            while (end < str.Length && !char.IsWhiteSpace(str[end]))
            {
                ++end;
            }
            string result = str.Substring(start, end - start);
            str = str.Remove(pos, end - pos);
            return result;
        }

        private void convert_pixel(byte[] raw, int pos, float[] dst, long dstpos, int spp, int format, int photometric)
        {
            for (int c = 0; c < spp; ++c)
            {
                float v;
                if (format == TypeDesc.UINT8)
                {
                    byte b = raw[pos + c];
                    if (photometric == 0)
                    {
                        b = (byte)(255 - b);
                    }
                    v = (float)b * ImageReaders.u8scale;
                }
                else if (format == TypeDesc.UINT16)
                {
                    int o = pos + c * 2;
                    ushort s = m_little ? (ushort)(raw[o] | (raw[o + 1] << 8)) : (ushort)((raw[o] << 8) | raw[o + 1]);
                    if (photometric == 0)
                    {
                        s = (ushort)(65535 - s);
                    }
                    v = (float)s * ImageReaders.u16scale;
                }
                else if (format == TypeDesc.HALF)
                {
                    int o = pos + c * 2;
                    ushort s = m_little ? (ushort)(raw[o] | (raw[o + 1] << 8)) : (ushort)((raw[o] << 8) | raw[o + 1]);
                    v = (float)BitConverter.UInt16BitsToHalf(s);
                }
                else
                {
                    int o = pos + c * 4;
                    uint u = m_little ? (uint)(raw[o] | (raw[o + 1] << 8) | (raw[o + 2] << 16) | (raw[o + 3] << 24))
                                      : (uint)((raw[o] << 24) | (raw[o + 1] << 16) | (raw[o + 2] << 8) | raw[o + 3]);
                    v = BitConverter.UInt32BitsToSingle(u);
                }
                dst[dstpos + c] = v;
            }
        }

        private byte[] decode_block(long offset, long count, int compression, int predictor, int w, int h, int spp, int bytesPerSample, int expected,
                                    out string error)
        {
            error = "";
            if (offset <= 0 || offset + count > m_data.Length)
            {
                error = "Could not open file: bad strip or tile offset";
                return null;
            }
            byte[] outbuf = new byte[expected];
            switch (compression)
            {
                case 1:
                    Array.Copy(m_data, offset, outbuf, 0, Math.Min(count, expected));
                    break;
                case 8:
                case 32946:
                    {
                        using (MemoryStream ms = new MemoryStream(m_data, (int)offset, (int)count))
                        using (ZLibStream zs = new ZLibStream(ms, CompressionMode.Decompress))
                        {
                            int total = 0;
                            while (total < expected)
                            {
                                int n = zs.Read(outbuf, total, expected - total);
                                if (n <= 0)
                                {
                                    break;
                                }
                                total += n;
                            }
                        }
                    }
                    break;
                case 5:
                    lzw_decode(m_data, (int)offset, (int)count, outbuf);
                    break;
                case 32773:
                    packbits_decode(m_data, (int)offset, (int)count, outbuf);
                    break;
                default:
                    error = "Could not open file: unsupported TIFF compression " + compression;
                    return null;
            }
            int rowBytes = w * spp * bytesPerSample;
            if (predictor == 2)
            {
                for (int row = 0; row < h; ++row)
                {
                    int rb = row * rowBytes;
                    if (bytesPerSample == 1)
                    {
                        for (int i = spp; i < w * spp; ++i)
                        {
                            outbuf[rb + i] = (byte)(outbuf[rb + i] + outbuf[rb + i - spp]);
                        }
                    }
                    else if (bytesPerSample == 2)
                    {
                        for (int i = spp; i < w * spp; ++i)
                        {
                            int p = rb + i * 2;
                            int q = rb + (i - spp) * 2;
                            int cur = m_little ? (outbuf[p] | (outbuf[p + 1] << 8)) : ((outbuf[p] << 8) | outbuf[p + 1]);
                            int prev = m_little ? (outbuf[q] | (outbuf[q + 1] << 8)) : ((outbuf[q] << 8) | outbuf[q + 1]);
                            int sum = (cur + prev) & 0xffff;
                            if (m_little)
                            {
                                outbuf[p] = (byte)sum;
                                outbuf[p + 1] = (byte)(sum >> 8);
                            }
                            else
                            {
                                outbuf[p] = (byte)(sum >> 8);
                                outbuf[p + 1] = (byte)sum;
                            }
                        }
                    }
                }
            }
            else if (predictor == 3)
            {
                byte[] tmp = new byte[rowBytes];
                for (int row = 0; row < h; ++row)
                {
                    int rb = row * rowBytes;
                    for (int i = spp; i < rowBytes; ++i)
                    {
                        outbuf[rb + i] = (byte)(outbuf[rb + i] + outbuf[rb + i - spp]);
                    }
                    int count2 = w * spp;
                    for (int i = 0; i < count2; ++i)
                    {
                        for (int b = 0; b < bytesPerSample; ++b)
                        {
                            // the planes are stored most significant byte first
                            byte value = outbuf[rb + b * count2 + i];
                            int dstByte = m_little ? (bytesPerSample - 1 - b) : b;
                            tmp[i * bytesPerSample + dstByte] = value;
                        }
                    }
                    Array.Copy(tmp, 0, outbuf, rb, rowBytes);
                }
            }
            else if (predictor != 1)
            {
                error = "Could not open file: unsupported TIFF predictor " + predictor;
                return null;
            }
            return outbuf;
        }

        private static void packbits_decode(byte[] src, int pos, int count, byte[] dst)
        {
            int end = pos + count;
            int o = 0;
            while (pos < end && o < dst.Length)
            {
                sbyte n = (sbyte)src[pos++];
                if (n >= 0)
                {
                    int len = n + 1;
                    for (int i = 0; i < len && o < dst.Length; ++i)
                    {
                        dst[o++] = src[pos++];
                    }
                }
                else if (n != -128)
                {
                    int len = 1 - n;
                    byte v = src[pos++];
                    for (int i = 0; i < len && o < dst.Length; ++i)
                    {
                        dst[o++] = v;
                    }
                }
            }
        }

        private static void lzw_decode(byte[] src, int pos, int count, byte[] dst)
        {
            int end = pos + count;
            int[] prefix = new int[4096];
            byte[] suffix = new byte[4096];
            int[] length = new int[4096];
            for (int i = 0; i < 256; ++i)
            {
                prefix[i] = -1;
                suffix[i] = (byte)i;
                length[i] = 1;
            }
            int nextCode = 258;
            int codeBits = 9;
            int bitBuf = 0;
            int bitCnt = 0;
            int o = 0;
            int prev = -1;
            byte[] scratch = new byte[4097];
            while (o < dst.Length)
            {
                while (bitCnt < codeBits && pos < end)
                {
                    bitBuf = (bitBuf << 8) | src[pos++];
                    bitCnt += 8;
                }
                if (bitCnt < codeBits)
                {
                    break;
                }
                int code = (bitBuf >> (bitCnt - codeBits)) & ((1 << codeBits) - 1);
                bitCnt -= codeBits;
                bitBuf &= (1 << bitCnt) - 1;
                if (code == 257)
                {
                    break;
                }
                if (code == 256)
                {
                    nextCode = 258;
                    codeBits = 9;
                    prev = -1;
                    continue;
                }
                if (prev == -1)
                {
                    write_string(code, prefix, suffix, length, dst, ref o, scratch);
                    prev = code;
                    continue;
                }
                if (code < nextCode)
                {
                    write_string(code, prefix, suffix, length, dst, ref o, scratch);
                    int first = first_byte(code, prefix, suffix);
                    if (nextCode < 4096)
                    {
                        prefix[nextCode] = prev;
                        suffix[nextCode] = (byte)first;
                        length[nextCode] = length[prev] + 1;
                        ++nextCode;
                    }
                }
                else
                {
                    int first = first_byte(prev, prefix, suffix);
                    if (nextCode < 4096)
                    {
                        prefix[nextCode] = prev;
                        suffix[nextCode] = (byte)first;
                        length[nextCode] = length[prev] + 1;
                        ++nextCode;
                    }
                    write_string(code, prefix, suffix, length, dst, ref o, scratch);
                }
                prev = code;
                if (nextCode == 511)
                {
                    codeBits = 10;
                }
                else if (nextCode == 1023)
                {
                    codeBits = 11;
                }
                else if (nextCode == 2047)
                {
                    codeBits = 12;
                }
            }
        }

        private static int first_byte(int code, int[] prefix, byte[] suffix)
        {
            while (prefix[code] != -1)
            {
                code = prefix[code];
            }
            return suffix[code];
        }

        private static void write_string(int code, int[] prefix, byte[] suffix, int[] length, byte[] dst, ref int o, byte[] scratch)
        {
            int len = length[code];
            int c = code;
            for (int i = len - 1; i >= 0; --i)
            {
                scratch[i] = suffix[c];
                c = prefix[c];
            }
            for (int i = 0; i < len && o < dst.Length; ++i)
            {
                dst[o++] = scratch[i];
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------------
    // Radiance HDR (hdrinput.cpp)
    // ----------------------------------------------------------------------------------------------------------------------------------------------

    internal static class HdrReader
    {
        private static readonly float[] exponent_table = make_exponent_table();

        private static float[] make_exponent_table()
        {
            float[] t = new float[256];
            for (int i = 0; i < 256; ++i)
            {
                t[i] = (float)Math.ScaleB(1.0, i - (128 + 8));
            }
            return t;
        }

        private static string read_line(byte[] d, ref int pos)
        {
            int start = pos;
            while (pos < d.Length && d[pos] != '\n')
            {
                ++pos;
            }
            if (pos >= d.Length)
            {
                return null;
            }
            ++pos;
            return Encoding.ASCII.GetString(d, start, pos - start);
        }

        public static ImageFileData read(byte[] d, out string error)
        {
            error = "";
            int pos = 0;
            string line = read_line(d, ref pos);
            if (line == null)
            {
                error = "Could not open file";
                return null;
            }
            line = read_line(d, ref pos);
            if (line == null)
            {
                error = "Could not open file";
                return null;
            }
            bool found_format = false;
            for (int nlines = 0; nlines < 100; ++nlines)
            {
                if (line.Length == 0 || line[0] == '\n')
                {
                    break;
                }
                if (line == "FORMAT=32-bit_rle_rgbe\n")
                {
                    found_format = true;
                }
                line = read_line(d, ref pos);
                if (line == null)
                {
                    error = "Could not open file";
                    return null;
                }
            }
            if (!found_format)
            {
                error = "no FORMAT specifier found";
                return null;
            }
            if (line != "\n")
            {
                error = "missing blank line after FORMAT specifier";
                return null;
            }
            line = read_line(d, ref pos);
            if (line == null)
            {
                error = "Could not open file";
                return null;
            }
            string[] tok = line.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int width, height;
            int orientation;
            if (tok.Length != 4)
            {
                error = "missing image size specifier";
                return null;
            }
            int a = int.Parse(tok[1], CultureInfo.InvariantCulture);
            int b = int.Parse(tok[3], CultureInfo.InvariantCulture);
            height = a;
            width = b;
            string key = tok[0] + tok[2];
            switch (key)
            {
                case "-Y+X": orientation = 1; break;
                case "-Y-X": orientation = 2; break;
                case "+Y-X": orientation = 3; break;
                case "+Y+X": orientation = 4; break;
                case "+X-Y": orientation = 5; break;
                case "+X+Y": orientation = 6; break;
                case "-X+Y": orientation = 7; break;
                case "-X-Y": orientation = 8; break;
                default:
                    error = "missing image size specifier";
                    return null;
            }
            ImageSpecData spec = ImageReaders.make_spec(width, height, 3, TypeDesc.FLOAT);
            spec.attributes["Orientation"] = orientation;
            float[] pixels = new float[(long)width * height * 3];
            byte[] rgbe = new byte[4];
            byte[] scan = new byte[4 * Math.Max(width, 1)];
            for (int y = 0; y < height; ++y)
            {
                float[] dst = pixels;
                long o = (long)y * width * 3;
                if (width < 8 || width > 0x7fff)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        rgbe2float(d, ref pos, pixels, ref o);
                    }
                    continue;
                }
                if (pos + 4 > d.Length)
                {
                    error = "Read error on scanline " + y;
                    return null;
                }
                if (d[pos] != 2 || d[pos + 1] != 2 || (d[pos + 2] & 0x80) != 0)
                {
                    // not run length encoded
                    for (int x = 0; x < width; ++x)
                    {
                        rgbe2float(d, ref pos, pixels, ref o);
                    }
                    continue;
                }
                if ((((uint)d[pos + 2]) << 8 | d[pos + 3]) != (uint)width)
                {
                    error = "wrong scanline width for scanline " + y;
                    return null;
                }
                pos += 4;
                int ptr = 0;
                for (int i = 0; i < 4; ++i)
                {
                    int ptr_end = (i + 1) * width;
                    while (ptr < ptr_end)
                    {
                        if (pos + 2 > d.Length)
                        {
                            error = "Read error on scanline " + y;
                            return null;
                        }
                        byte b0 = d[pos];
                        byte b1 = d[pos + 1];
                        pos += 2;
                        if (b0 > 128)
                        {
                            int count = b0 - 128;
                            if (count == 0 || count > ptr_end - ptr)
                            {
                                error = "bad scanline " + y + " data";
                                return null;
                            }
                            while (count-- > 0)
                            {
                                scan[ptr++] = b1;
                            }
                        }
                        else
                        {
                            int count = b0;
                            if (count == 0 || count > ptr_end - ptr)
                            {
                                error = "bad scanline " + y + " data";
                                return null;
                            }
                            scan[ptr++] = b1;
                            if (--count > 0)
                            {
                                if (pos + count > d.Length)
                                {
                                    error = "Read error on scanline " + y;
                                    return null;
                                }
                                Array.Copy(d, pos, scan, ptr, count);
                                pos += count;
                                ptr += count;
                            }
                        }
                    }
                }
                for (int x = 0; x < width; ++x)
                {
                    rgbe[0] = scan[x];
                    rgbe[1] = scan[x + width];
                    rgbe[2] = scan[x + 2 * width];
                    rgbe[3] = scan[x + 3 * width];
                    if (rgbe[3] != 0)
                    {
                        float f = exponent_table[rgbe[3]];
                        pixels[o] = rgbe[0] * f;
                        pixels[o + 1] = rgbe[1] * f;
                        pixels[o + 2] = rgbe[2] * f;
                    }
                    else
                    {
                        pixels[o] = 0.0f;
                        pixels[o + 1] = 0.0f;
                        pixels[o + 2] = 0.0f;
                    }
                    o += 3;
                }
            }
            return ImageReaders.single("hdr", spec, pixels);
        }

        private static void rgbe2float(byte[] d, ref int pos, float[] pixels, ref long o)
        {
            if (pos + 4 > d.Length)
            {
                return;
            }
            if (d[pos + 3] != 0)
            {
                float f = exponent_table[d[pos + 3]];
                pixels[o] = d[pos] * f;
                pixels[o + 1] = d[pos + 1] * f;
                pixels[o + 2] = d[pos + 2] * f;
            }
            else
            {
                pixels[o] = 0.0f;
                pixels[o + 1] = 0.0f;
                pixels[o + 2] = 0.0f;
            }
            pos += 4;
            o += 3;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------------
    // PNG (pnginput.cpp), non interlaced
    // ----------------------------------------------------------------------------------------------------------------------------------------------

    internal static class PngReader
    {
        private static uint be32(byte[] d, int p)
        {
            return (uint)((d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]);
        }

        public static ImageFileData read(byte[] d, out string error)
        {
            error = "";
            int pos = 8;
            int width = 0, height = 0, bitdepth = 0, colortype = 0, interlace = 0;
            MemoryStream idat = new MemoryStream();
            byte[] palette = null;
            while (pos + 8 <= d.Length)
            {
                int len = (int)be32(d, pos);
                string type = Encoding.ASCII.GetString(d, pos + 4, 4);
                int dp = pos + 8;
                if (type == "IHDR")
                {
                    width = (int)be32(d, dp);
                    height = (int)be32(d, dp + 4);
                    bitdepth = d[dp + 8];
                    colortype = d[dp + 9];
                    interlace = d[dp + 12];
                }
                else if (type == "PLTE")
                {
                    palette = new byte[len];
                    Array.Copy(d, dp, palette, 0, len);
                }
                else if (type == "IDAT")
                {
                    idat.Write(d, dp, len);
                }
                else if (type == "IEND")
                {
                    break;
                }
                pos = dp + len + 4;
            }
            if (interlace != 0)
            {
                error = "Could not open file: interlaced PNG is not supported";
                return null;
            }
            if (bitdepth != 8 && bitdepth != 16)
            {
                error = "Could not open file: unsupported PNG bit depth " + bitdepth;
                return null;
            }
            int samples;
            switch (colortype)
            {
                case 0: samples = 1; break;
                case 2: samples = 3; break;
                case 3: samples = 1; break;
                case 4: samples = 2; break;
                case 6: samples = 4; break;
                default:
                    error = "Could not open file: bad PNG color type";
                    return null;
            }
            if (colortype == 3 && (palette == null || bitdepth != 8))
            {
                error = "Could not open file: unsupported palette PNG";
                return null;
            }
            int bps = bitdepth / 8;
            int bpp = samples * bps;
            int rowBytes = width * bpp;
            byte[] raw = new byte[(rowBytes + 1) * height];
            idat.Position = 0;
            using (ZLibStream zs = new ZLibStream(idat, CompressionMode.Decompress))
            {
                int total = 0;
                while (total < raw.Length)
                {
                    int n = zs.Read(raw, total, raw.Length - total);
                    if (n <= 0)
                    {
                        break;
                    }
                    total += n;
                }
            }
            byte[] img = new byte[rowBytes * height];
            for (int y = 0; y < height; ++y)
            {
                int filter = raw[y * (rowBytes + 1)];
                int src = y * (rowBytes + 1) + 1;
                int dst = y * rowBytes;
                for (int i = 0; i < rowBytes; ++i)
                {
                    int a = i >= bpp ? img[dst + i - bpp] : 0;
                    int b = y > 0 ? img[dst - rowBytes + i] : 0;
                    int c = (i >= bpp && y > 0) ? img[dst - rowBytes + i - bpp] : 0;
                    int x = raw[src + i];
                    int v;
                    switch (filter)
                    {
                        case 0: v = x; break;
                        case 1: v = x + a; break;
                        case 2: v = x + b; break;
                        case 3: v = x + ((a + b) >> 1); break;
                        case 4:
                            {
                                int p = a + b - c;
                                int pa = Math.Abs(p - a);
                                int pb = Math.Abs(p - b);
                                int pc = Math.Abs(p - c);
                                int pr = (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
                                v = x + pr;
                            }
                            break;
                        default:
                            error = "Could not open file: bad PNG filter";
                            return null;
                    }
                    img[dst + i] = (byte)v;
                }
            }
            int nch = colortype == 3 ? 3 : samples;
            int format = bitdepth == 16 ? TypeDesc.UINT16 : TypeDesc.UINT8;
            ImageSpecData spec = ImageReaders.make_spec(width, height, nch, format);
            if (nch == 2)
            {
                spec.alpha_channel = 1;
            }
            else if (nch == 4)
            {
                spec.alpha_channel = 3;
            }
            float[] pixels = new float[(long)width * height * nch];
            for (long p = 0; p < (long)width * height; ++p)
            {
                for (int c = 0; c < nch; ++c)
                {
                    float v;
                    if (colortype == 3)
                    {
                        v = (float)palette[img[p] * 3 + c] * ImageReaders.u8scale;
                    }
                    else if (bitdepth == 8)
                    {
                        v = (float)img[p * samples + c] * ImageReaders.u8scale;
                    }
                    else
                    {
                        long o = (p * samples + c) * 2;
                        v = (float)((img[o] << 8) | img[o + 1]) * ImageReaders.u16scale;
                    }
                    pixels[p * nch + c] = v;
                }
            }
            if (spec.alpha_channel >= 0)
            {
                // PNG alpha is unassociated: associate it on read, as PNGInput does by default
                int a = spec.alpha_channel;
                for (long p = 0; p < (long)width * height; ++p)
                {
                    float alpha = pixels[p * nch + a];
                    for (int c = 0; c < nch; ++c)
                    {
                        if (c != a)
                        {
                            pixels[p * nch + c] = ImageReaders.quantize(pixels[p * nch + c] * alpha, format);
                        }
                    }
                }
            }
            return ImageReaders.single("png", spec, pixels);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------------
    // PPM / PGM (ppminput.cpp) and PFM (pfminput.cpp)
    // ----------------------------------------------------------------------------------------------------------------------------------------------

    internal static class PnmReader
    {
        private static string read_token(byte[] bytes, ref int pos)
        {
            while (pos < bytes.Length)
            {
                if (bytes[pos] == '#')
                {
                    while (pos < bytes.Length && bytes[pos] != '\n')
                    {
                        ++pos;
                    }
                }
                else if (bytes[pos] == ' ' || bytes[pos] == '\n' || bytes[pos] == '\r' || bytes[pos] == '\t')
                {
                    ++pos;
                }
                else
                {
                    break;
                }
            }
            int start = pos;
            while (pos < bytes.Length && !(bytes[pos] == ' ' || bytes[pos] == '\n' || bytes[pos] == '\r' || bytes[pos] == '\t'))
            {
                ++pos;
            }
            return Encoding.ASCII.GetString(bytes, start, pos - start);
        }

        public static ImageFileData read(byte[] bytes, out string error)
        {
            error = "";
            int pos = 0;
            string magic = read_token(bytes, ref pos);
            if (magic == "P6" || magic == "P5")
            {
                int w = int.Parse(read_token(bytes, ref pos), CultureInfo.InvariantCulture);
                int h = int.Parse(read_token(bytes, ref pos), CultureInfo.InvariantCulture);
                int maxval = int.Parse(read_token(bytes, ref pos), CultureInfo.InvariantCulture);
                ++pos;
                int nch = magic == "P6" ? 3 : 1;
                bool wide = maxval > 255;
                ImageSpecData spec = ImageReaders.make_spec(w, h, nch, wide ? TypeDesc.UINT16 : TypeDesc.UINT8);
                float[] pixels = new float[(long)w * h * nch];
                for (long i = 0; i < (long)w * h * nch; ++i)
                {
                    if (wide)
                    {
                        long o = pos + i * 2;
                        pixels[i] = (float)((bytes[o] << 8) | bytes[o + 1]) * ImageReaders.u16scale;
                    }
                    else
                    {
                        pixels[i] = (float)bytes[pos + i] * ImageReaders.u8scale;
                    }
                }
                return ImageReaders.single("pnm", spec, pixels);
            }
            else
            {
                int nch = magic == "PF" ? 3 : 1;
                int w = int.Parse(read_token(bytes, ref pos), CultureInfo.InvariantCulture);
                int h = int.Parse(read_token(bytes, ref pos), CultureInfo.InvariantCulture);
                float scale = float.Parse(read_token(bytes, ref pos), CultureInfo.InvariantCulture);
                ++pos;
                bool little = scale < 0;
                ImageSpecData spec = ImageReaders.make_spec(w, h, nch, TypeDesc.FLOAT);
                float[] pixels = new float[(long)w * h * nch];
                byte[] b = new byte[4];
                for (int y = 0; y < h; ++y)
                {
                    int row = h - 1 - y;
                    for (int i = 0; i < w * nch; ++i)
                    {
                        Array.Copy(bytes, pos + ((long)y * w * nch + i) * 4, b, 0, 4);
                        if (little != BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(b);
                        }
                        pixels[(long)row * w * nch + i] = BitConverter.ToSingle(b, 0);
                    }
                }
                return ImageReaders.single("pfm", spec, pixels);
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------------
    // BMP (bmpinput.cpp), uncompressed 24 and 32 bit
    // ----------------------------------------------------------------------------------------------------------------------------------------------

    internal static class BmpReader
    {
        public static ImageFileData read(byte[] d, out string error)
        {
            error = "";
            int dataoffset = BitConverter.ToInt32(d, 10);
            int headersize = BitConverter.ToInt32(d, 14);
            if (headersize < 40)
            {
                error = "Could not open file: unsupported BMP header";
                return null;
            }
            int w = BitConverter.ToInt32(d, 18);
            int h = BitConverter.ToInt32(d, 22);
            int bits = BitConverter.ToInt16(d, 28);
            int compression = BitConverter.ToInt32(d, 30);
            if (compression != 0 || (bits != 24 && bits != 32))
            {
                error = "Could not open file: unsupported BMP encoding";
                return null;
            }
            bool bottomup = h > 0;
            h = Math.Abs(h);
            int bpp = bits / 8;
            int stride = ((w * bits + 31) / 32) * 4;
            int nch = bits == 32 ? 4 : 3;
            ImageSpecData spec = ImageReaders.make_spec(w, h, nch, TypeDesc.UINT8);
            if (nch == 4)
            {
                spec.alpha_channel = 3;
            }
            float[] pixels = new float[(long)w * h * nch];
            for (int y = 0; y < h; ++y)
            {
                int srcrow = bottomup ? h - 1 - y : y;
                for (int x = 0; x < w; ++x)
                {
                    int o = dataoffset + srcrow * stride + x * bpp;
                    long p = ((long)y * w + x) * nch;
                    pixels[p] = (float)d[o + 2] * ImageReaders.u8scale;
                    pixels[p + 1] = (float)d[o + 1] * ImageReaders.u8scale;
                    pixels[p + 2] = (float)d[o] * ImageReaders.u8scale;
                    if (nch == 4)
                    {
                        pixels[p + 3] = (float)d[o + 3] * ImageReaders.u8scale;
                    }
                }
            }
            return ImageReaders.single("bmp", spec, pixels);
        }
    }
}
