// OSL port: the OpenEXR ImageInput (OpenImageIO exrinput.cpp over the OpenEXR library), written out as a self-contained decoder.
//
// Reads scanline and tiled single-part files with the compressions NONE, RLE, ZIPS, ZIP, PIZ and PXR24 (not B44, B44A, DWAA, DWAB; not deep or multi-part
// files), pixel types HALF, FLOAT and UINT, ONE_LEVEL and MIPMAP tiled files (a RIPMAP file gives its top level only). Like OIIO the channels come out
// with R, G, B, A first (when present) followed by the others in the file's (alphabetical) order; the data window and the display window become the
// pixel data window and the full image window of the spec.
//
// Not ported: the writer (testshade has its own uncompressed writer), the attributes beyond the ones the texture system looks at, multi-part files.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OIIO
{
    internal sealed class ExrChannel
    {
        public string name;
        public int pixel_type;  // 0 UINT, 1 HALF, 2 FLOAT
        public int x_sampling;
        public int y_sampling;

        public int bytes_per_sample()
        {
            return pixel_type == 1 ? 2 : 4;
        }
    }

    internal static class ExrReader
    {
        private const int NO_COMPRESSION = 0;
        private const int RLE_COMPRESSION = 1;
        private const int ZIPS_COMPRESSION = 2;
        private const int ZIP_COMPRESSION = 3;
        private const int PIZ_COMPRESSION = 4;
        private const int PXR24_COMPRESSION = 5;

        public static bool is_exr(byte[] d)
        {
            return d.Length >= 4 && d[0] == 0x76 && d[1] == 0x2f && d[2] == 0x31 && d[3] == 0x01;
        }

        private sealed class Cursor
        {
            public byte[] data;
            public int pos;

            public int read_int()
            {
                if (pos + 4 > data.Length)
                {
                    throw new InvalidDataException("unexpected end of file");
                }
                int v = BitConverter.ToInt32(data, pos);
                pos += 4;
                return v;
            }

            public string read_cstring()
            {
                int start = pos;
                while (pos < data.Length && data[pos] != 0)
                {
                    ++pos;
                }
                if (pos >= data.Length)
                {
                    throw new InvalidDataException("unexpected end of file");
                }
                string s = Encoding.ASCII.GetString(data, start, pos - start);
                ++pos;
                return s;
            }
        }

        private sealed class Level
        {
            public int width;
            public int height;
            public int tiles_x;
            public int tiles_y;
            public long first_offset_index;
        }

        public static ImageFileData read(byte[] file, out string error)
        {
            error = "";
            Cursor c = new Cursor();
            c.data = file;
            c.pos = 4;
            int version = c.read_int();
            bool tiled = (version & 0x200) != 0;
            bool deep = (version & 0x800) != 0;
            bool multipart = (version & 0x1000) != 0;
            if ((version & 0xff) != 2)
            {
                error = "Could not open file: unsupported OpenEXR version";
                return null;
            }
            if (deep || multipart)
            {
                error = "Could not open file: deep and multi-part OpenEXR files are not supported";
                return null;
            }

            List<ExrChannel> channels = new List<ExrChannel>();
            int compression = -1;
            int dw_x0 = 0, dw_y0 = 0, dw_x1 = -1, dw_y1 = -1;
            int disp_x0 = 0, disp_y0 = 0, disp_x1 = -1, disp_y1 = -1;
            int tile_w = 0, tile_h = 0, tile_mode = 0;
            Dictionary<string, object> extra = new Dictionary<string, object>(StringComparer.Ordinal);
            while (true)
            {
                if (c.pos >= file.Length)
                {
                    error = "Could not open file: truncated OpenEXR header";
                    return null;
                }
                if (file[c.pos] == 0)
                {
                    ++c.pos;
                    break;
                }
                string name = c.read_cstring();
                string type = c.read_cstring();
                int size = c.read_int();
                int end = c.pos + size;
                if (size < 0 || end > file.Length)
                {
                    error = "Could not open file: corrupt OpenEXR header";
                    return null;
                }
                if (name == "channels")
                {
                    int p = c.pos;
                    while (p < end && file[p] != 0)
                    {
                        ExrChannel ch = new ExrChannel();
                        int q = p;
                        while (file[q] != 0)
                        {
                            ++q;
                        }
                        ch.name = Encoding.ASCII.GetString(file, p, q - p);
                        p = q + 1;
                        ch.pixel_type = BitConverter.ToInt32(file, p);
                        p += 4;
                        p += 4;  // pLinear + reserved
                        ch.x_sampling = BitConverter.ToInt32(file, p);
                        ch.y_sampling = BitConverter.ToInt32(file, p + 4);
                        p += 8;
                        channels.Add(ch);
                    }
                }
                else if (name == "compression")
                {
                    compression = file[c.pos];
                }
                else if (name == "dataWindow")
                {
                    dw_x0 = BitConverter.ToInt32(file, c.pos);
                    dw_y0 = BitConverter.ToInt32(file, c.pos + 4);
                    dw_x1 = BitConverter.ToInt32(file, c.pos + 8);
                    dw_y1 = BitConverter.ToInt32(file, c.pos + 12);
                }
                else if (name == "displayWindow")
                {
                    disp_x0 = BitConverter.ToInt32(file, c.pos);
                    disp_y0 = BitConverter.ToInt32(file, c.pos + 4);
                    disp_x1 = BitConverter.ToInt32(file, c.pos + 8);
                    disp_y1 = BitConverter.ToInt32(file, c.pos + 12);
                }
                else if (name == "tiles")
                {
                    tile_w = BitConverter.ToInt32(file, c.pos);
                    tile_h = BitConverter.ToInt32(file, c.pos + 4);
                    tile_mode = file[c.pos + 8];
                }
                else if (type == "string")
                {
                    extra[name] = Encoding.ASCII.GetString(file, c.pos, size);
                }
                else if (type == "int" && size == 4)
                {
                    extra[name] = BitConverter.ToInt32(file, c.pos);
                }
                else if (type == "float" && size == 4)
                {
                    extra[name] = BitConverter.ToSingle(file, c.pos);
                }
                c.pos = end;
            }
            if (channels.Count == 0 || compression < 0 || dw_x1 < dw_x0 || dw_y1 < dw_y0)
            {
                error = "Could not open file: missing required OpenEXR header attributes";
                return null;
            }
            if (compression > PXR24_COMPRESSION)
            {
                error = "Could not open file: unsupported OpenEXR compression";
                return null;
            }
            foreach (ExrChannel ch in channels)
            {
                if (ch.x_sampling != 1 || ch.y_sampling != 1)
                {
                    error = "Could not open file: subsampled OpenEXR channels are not supported";
                    return null;
                }
            }

            int width = dw_x1 - dw_x0 + 1;
            int height = dw_y1 - dw_y0 + 1;

            // The channel order of the output: R, G, B, A first, then the rest as listed
            List<int> out_order = new List<int>();
            string[] rgba = new string[] { "R", "G", "B", "A" };
            foreach (string wanted in rgba)
            {
                for (int i = 0; i < channels.Count; ++i)
                {
                    if (channels[i].name == wanted)
                    {
                        out_order.Add(i);
                    }
                }
            }
            for (int i = 0; i < channels.Count; ++i)
            {
                if (!out_order.Contains(i))
                {
                    out_order.Add(i);
                }
            }
            int nch = channels.Count;
            int alpha = -1;
            for (int i = 0; i < out_order.Count; ++i)
            {
                if (channels[out_order[i]].name == "A")
                {
                    alpha = i;
                }
            }
            bool all_half = true;
            foreach (ExrChannel ch in channels)
            {
                all_half &= ch.pixel_type == 1;
            }

            // The levels and the table of chunk offsets
            List<Level> levels = new List<Level>();
            int lines_per_block = 1;
            if (!tiled)
            {
                switch (compression)
                {
                    case ZIP_COMPRESSION:
                    case PXR24_COMPRESSION:
                        lines_per_block = 16;
                        break;
                    case PIZ_COMPRESSION:
                        lines_per_block = 32;
                        break;
                    default:
                        lines_per_block = 1;
                        break;
                }
                Level lv = new Level();
                lv.width = width;
                lv.height = height;
                lv.first_offset_index = 0;
                levels.Add(lv);
            }
            else
            {
                if (tile_w <= 0 || tile_h <= 0)
                {
                    error = "Could not open file: bad OpenEXR tile description";
                    return null;
                }
                int level_mode = tile_mode & 15;
                bool round_up = ((tile_mode >> 4) & 1) != 0;
                int count = 1;
                if (level_mode != 0)
                {
                    int m = Math.Max(width, height);
                    int n = 0;
                    while (m > 1)
                    {
                        m = round_up ? (m + 1) / 2 : m / 2;
                        ++n;
                    }
                    count = n + 1;
                }
                long index = 0;
                for (int l = 0; l < count; ++l)
                {
                    Level lv = new Level();
                    lv.width = level_size(width, l, round_up);
                    lv.height = level_size(height, l, round_up);
                    lv.tiles_x = (lv.width + tile_w - 1) / tile_w;
                    lv.tiles_y = (lv.height + tile_h - 1) / tile_h;
                    lv.first_offset_index = index;
                    index += (long)lv.tiles_x * lv.tiles_y;
                    levels.Add(lv);
                    if (level_mode == 0)
                    {
                        break;
                    }
                }
                if (level_mode == 2)
                {
                    // RIPMAP: only the top level is read, and its offsets come first in the table
                    levels.RemoveRange(1, levels.Count - 1);
                }
                // The offset table of a MIPMAP file has the tiles of all the levels
            }
            long total_chunks;
            if (!tiled)
            {
                total_chunks = (height + lines_per_block - 1) / lines_per_block;
            }
            else
            {
                total_chunks = 0;
                foreach (Level lv in levels)
                {
                    total_chunks += (long)lv.tiles_x * lv.tiles_y;
                }
            }
            long table_pos = c.pos;
            if (table_pos + total_chunks * 8 > file.Length)
            {
                error = "Could not open file: truncated OpenEXR offset table";
                return null;
            }

            ImageFileData result = new ImageFileData();
            result.format_name = "openexr";
            ImageSubimageData sub = new ImageSubimageData();
            result.subimages.Add(sub);

            int bytes_per_pixel = 0;
            foreach (ExrChannel ch in channels)
            {
                bytes_per_pixel += ch.bytes_per_sample();
            }

            for (int l = 0; l < levels.Count; ++l)
            {
                Level lv = levels[l];
                float[] pixels = new float[(long)lv.width * lv.height * nch];
                int x_origin = dw_x0;
                int y_origin = dw_y0;
                if (!tiled)
                {
                    int blocks = (lv.height + lines_per_block - 1) / lines_per_block;
                    for (int b = 0; b < blocks; ++b)
                    {
                        long offset = BitConverter.ToInt64(file, (int)(table_pos + 8L * b));
                        c.pos = (int)offset;
                        int y = c.read_int();
                        int data_size = c.read_int();
                        int first = y - y_origin;
                        int lines = Math.Min(lines_per_block, lv.height - first);
                        if (first < 0 || lines <= 0 || c.pos + data_size > file.Length)
                        {
                            error = "Could not open file: corrupt OpenEXR scanline block";
                            return null;
                        }
                        int raw_size = lines * lv.width * bytes_per_pixel;
                        byte[] raw = unpack_block(file, c.pos, data_size, raw_size, compression, channels, lv.width, lines, ref error);
                        if (raw == null)
                        {
                            return null;
                        }
                        store_block(raw, channels, out_order, pixels, nch, lv.width, 0, first, lv.width, lines);
                    }
                }
                else
                {
                    for (int ty = 0; ty < lv.tiles_y; ++ty)
                    {
                        for (int tx = 0; tx < lv.tiles_x; ++tx)
                        {
                            long index = lv.first_offset_index + (long)ty * lv.tiles_x + tx;
                            long offset = BitConverter.ToInt64(file, (int)(table_pos + 8 * index));
                            c.pos = (int)offset;
                            c.read_int();
                            c.read_int();
                            c.read_int();
                            c.read_int();
                            int data_size = c.read_int();
                            if (c.pos + data_size > file.Length)
                            {
                                error = "Could not open file: corrupt OpenEXR tile";
                                return null;
                            }
                            int x0 = tx * tile_w;
                            int y0 = ty * tile_h;
                            int tw = Math.Min(tile_w, lv.width - x0);
                            int th = Math.Min(tile_h, lv.height - y0);
                            int raw_size = tw * th * bytes_per_pixel;
                            byte[] raw = unpack_block(file, c.pos, data_size, raw_size, compression, channels, tw, th, ref error);
                            if (raw == null)
                            {
                                return null;
                            }
                            store_block(raw, channels, out_order, pixels, nch, lv.width, x0, y0, tw, th);
                        }
                    }
                }

                ImageSpecData spec = new ImageSpecData();
                spec.nchannels = nch;
                spec.alpha_channel = alpha;
                spec.format = all_half ? TypeDesc.HALF : TypeDesc.FLOAT;
                spec.x = l == 0 ? dw_x0 : 0;
                spec.y = l == 0 ? dw_y0 : 0;
                spec.width = lv.width;
                spec.height = lv.height;
                spec.full_x = l == 0 ? disp_x0 : 0;
                spec.full_y = l == 0 ? disp_y0 : 0;
                spec.full_width = l == 0 ? disp_x1 - disp_x0 + 1 : lv.width;
                spec.full_height = l == 0 ? disp_y1 - disp_y0 + 1 : lv.height;
                spec.tile_width = tiled ? tile_w : 0;
                spec.tile_height = tiled ? tile_h : 0;
                foreach (KeyValuePair<string, object> kv in extra)
                {
                    spec.attributes[kv.Key] = kv.Value;
                }
                string[] compression_names = new string[] { "none", "rle", "zips", "zip", "piz", "pxr24" };
                spec.attributes["compression"] = compression_names[compression];
                ImageLevelData level = new ImageLevelData();
                level.spec = spec;
                level.pixels = pixels;
                sub.levels.Add(level);
            }
            return result;
        }

        private static int level_size(int full, int level, bool round_up)
        {
            long s = full;
            if (round_up)
            {
                s = (s + (1L << level) - 1) >> level;
            }
            else
            {
                s = s >> level;
            }
            return (int)Math.Max(1L, s);
        }

        // The block's bytes as the channels lie in an uncompressed block: for each line, for each channel (file order), the samples of that line
        private static byte[] unpack_block(byte[] file, int pos, int data_size, int raw_size, int compression, List<ExrChannel> channels, int width, int lines,
                                           ref string error)
        {
            try
            {
                if (data_size == raw_size || compression == NO_COMPRESSION)
                {
                    byte[] copy = new byte[raw_size];
                    Array.Copy(file, pos, copy, 0, Math.Min(raw_size, data_size));
                    return copy;
                }
                switch (compression)
                {
                    case ZIPS_COMPRESSION:
                    case ZIP_COMPRESSION:
                    {
                        byte[] t = inflate(file, pos, data_size, raw_size);
                        return undo_predictor_and_reorder(t);
                    }
                    case RLE_COMPRESSION:
                    {
                        byte[] t = rle_decompress(file, pos, data_size, raw_size);
                        return undo_predictor_and_reorder(t);
                    }
                    case PXR24_COMPRESSION:
                    {
                        byte[] t = inflate(file, pos, data_size, -1);
                        return undo_pxr24(t, channels, width, lines, raw_size);
                    }
                    case PIZ_COMPRESSION:
                        return PizDecoder.decode(file, pos, data_size, raw_size, channels, width, lines);
                    default:
                        error = "Could not open file: unsupported OpenEXR compression";
                        return null;
                }
            }
            catch (Exception e)
            {
                error = "Could not open file: " + e.Message;
                return null;
            }
        }

        private static byte[] inflate(byte[] file, int pos, int size, int expected)
        {
            using (MemoryStream input = new MemoryStream(file, pos, size))
            using (ZLibStream z = new ZLibStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream(expected > 0 ? expected : 4096))
            {
                z.CopyTo(output);
                byte[] r = output.ToArray();
                if (expected > 0 && r.Length != expected)
                {
                    throw new InvalidDataException("corrupt compressed OpenEXR data");
                }
                return r;
            }
        }

        private static byte[] rle_decompress(byte[] file, int pos, int size, int expected)
        {
            byte[] r = new byte[expected];
            int o = 0;
            int end = pos + size;
            while (pos < end)
            {
                sbyte count = (sbyte)file[pos++];
                if (count < 0)
                {
                    int n = -count;
                    if (pos + n > end || o + n > expected)
                    {
                        throw new InvalidDataException("corrupt RLE OpenEXR data");
                    }
                    Array.Copy(file, pos, r, o, n);
                    pos += n;
                    o += n;
                }
                else
                {
                    int n = count + 1;
                    if (pos >= end || o + n > expected)
                    {
                        throw new InvalidDataException("corrupt RLE OpenEXR data");
                    }
                    byte v = file[pos++];
                    for (int i = 0; i < n; ++i)
                    {
                        r[o++] = v;
                    }
                }
            }
            if (o != expected)
            {
                throw new InvalidDataException("corrupt RLE OpenEXR data");
            }
            return r;
        }

        // The ZIP and RLE codecs store the bytes as deltas (t[i] - t[i-1] + 128) with the even-indexed bytes in the first half and the odd ones in the second
        private static byte[] undo_predictor_and_reorder(byte[] t)
        {
            for (int i = 1; i < t.Length; ++i)
            {
                t[i] = (byte)(t[i - 1] + t[i] - 128);
            }
            byte[] r = new byte[t.Length];
            int half = (t.Length + 1) / 2;
            int s1 = 0;
            int s2 = half;
            int o = 0;
            while (o < r.Length)
            {
                r[o++] = t[s1++];
                if (o < r.Length)
                {
                    r[o++] = t[s2++];
                }
            }
            return r;
        }

        private static byte[] undo_pxr24(byte[] t, List<ExrChannel> channels, int width, int lines, int raw_size)
        {
            byte[] r = new byte[raw_size];
            int src = 0;
            int dst = 0;
            for (int y = 0; y < lines; ++y)
            {
                foreach (ExrChannel ch in channels)
                {
                    if (ch.pixel_type == 1)
                    {
                        int p0 = src;
                        int p1 = src + width;
                        src += 2 * width;
                        uint pixel = 0;
                        for (int x = 0; x < width; ++x)
                        {
                            uint diff = ((uint)t[p0++] << 8) | t[p1++];
                            pixel += diff;
                            r[dst++] = (byte)pixel;
                            r[dst++] = (byte)(pixel >> 8);
                        }
                    }
                    else if (ch.pixel_type == 2)
                    {
                        int p0 = src;
                        int p1 = src + width;
                        int p2 = src + 2 * width;
                        src += 3 * width;
                        uint pixel = 0;
                        for (int x = 0; x < width; ++x)
                        {
                            uint diff = ((uint)t[p0++] << 24) | ((uint)t[p1++] << 16) | ((uint)t[p2++] << 8);
                            pixel += diff;
                            r[dst++] = (byte)pixel;
                            r[dst++] = (byte)(pixel >> 8);
                            r[dst++] = (byte)(pixel >> 16);
                            r[dst++] = (byte)(pixel >> 24);
                        }
                    }
                    else
                    {
                        int p0 = src;
                        int p1 = src + width;
                        int p2 = src + 2 * width;
                        int p3 = src + 3 * width;
                        src += 4 * width;
                        uint pixel = 0;
                        for (int x = 0; x < width; ++x)
                        {
                            uint diff = ((uint)t[p0++] << 24) | ((uint)t[p1++] << 16) | ((uint)t[p2++] << 8) | t[p3++];
                            pixel += diff;
                            r[dst++] = (byte)pixel;
                            r[dst++] = (byte)(pixel >> 8);
                            r[dst++] = (byte)(pixel >> 16);
                            r[dst++] = (byte)(pixel >> 24);
                        }
                    }
                }
            }
            return r;
        }

        // Copy the samples of an uncompressed block into the float image
        private static void store_block(byte[] raw, List<ExrChannel> channels, List<int> out_order, float[] pixels, int nch, int image_width, int x0, int y0,
                                        int block_width, int lines)
        {
            int pos = 0;
            for (int y = 0; y < lines; ++y)
            {
                for (int ci = 0; ci < channels.Count; ++ci)
                {
                    ExrChannel ch = channels[ci];
                    int out_channel = out_order.IndexOf(ci);
                    long o = ((long)(y0 + y) * image_width + x0) * nch + out_channel;
                    if (ch.pixel_type == 1)
                    {
                        for (int x = 0; x < block_width; ++x)
                        {
                            pixels[o] = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(raw, pos));
                            pos += 2;
                            o += nch;
                        }
                    }
                    else if (ch.pixel_type == 2)
                    {
                        for (int x = 0; x < block_width; ++x)
                        {
                            pixels[o] = BitConverter.ToSingle(raw, pos);
                            pos += 4;
                            o += nch;
                        }
                    }
                    else
                    {
                        for (int x = 0; x < block_width; ++x)
                        {
                            pixels[o] = (float)BitConverter.ToUInt32(raw, pos);
                            pos += 4;
                            o += nch;
                        }
                    }
                }
            }
        }
    }

    // The PIZ codec of OpenEXR (ImfPizCompressor.cpp, ImfHuf.cpp, ImfWav.cpp): a wavelet transform of each channel, Huffman coding of the result, and a
    // remapping of the used 16 bit values to a dense range.
    internal static class PizDecoder
    {
        private const int HUF_ENCBITS = 16;
        private const int HUF_DECBITS = 14;
        private const int HUF_ENCSIZE = (1 << HUF_ENCBITS) + 1;
        private const int HUF_DECSIZE = 1 << HUF_DECBITS;
        private const int HUF_DECMASK = HUF_DECSIZE - 1;
        private const int SHORT_ZEROCODE_RUN = 59;
        private const int LONG_ZEROCODE_RUN = 63;
        private const int SHORTEST_LONG_RUN = 2 + LONG_ZEROCODE_RUN - SHORT_ZEROCODE_RUN;
        private const int BITMAP_SIZE = 8192;

        private sealed class ChannelData
        {
            public int start;  // index into the shorts of the temporary buffer
            public int end;
            public int nx;
            public int ny;
            public int size;  // in shorts per sample
        }

        public static byte[] decode(byte[] file, int pos, int data_size, int raw_size, List<ExrChannel> channels, int width, int lines)
        {
            byte[] output = new byte[raw_size];
            if (data_size == 0)
            {
                return output;
            }
            int p = pos;
            int min_non_zero = BitConverter.ToUInt16(file, p);
            p += 2;
            int max_non_zero = BitConverter.ToUInt16(file, p);
            p += 2;
            if (max_non_zero >= BITMAP_SIZE)
            {
                throw new InvalidDataException("corrupt PIZ OpenEXR data");
            }
            byte[] bitmap = new byte[BITMAP_SIZE];
            if (min_non_zero <= max_non_zero)
            {
                int n = max_non_zero - min_non_zero + 1;
                Array.Copy(file, p, bitmap, min_non_zero, n);
                p += n;
            }
            ushort[] lut = new ushort[65536];
            int max_value = reverse_lut_from_bitmap(bitmap, lut);

            int length = BitConverter.ToInt32(file, p);
            p += 4;
            int total_shorts = raw_size / 2;
            ushort[] tmp = new ushort[total_shorts];

            List<ChannelData> cds = new List<ChannelData>();
            int offset = 0;
            foreach (ExrChannel ch in channels)
            {
                ChannelData cd = new ChannelData();
                cd.start = offset;
                cd.end = offset;
                cd.nx = width;
                cd.ny = lines;
                cd.size = ch.bytes_per_sample() / 2;
                offset += cd.nx * cd.ny * cd.size;
                cds.Add(cd);
            }

            huf_uncompress(file, p, length, tmp, total_shorts);

            foreach (ChannelData cd in cds)
            {
                for (int j = 0; j < cd.size; ++j)
                {
                    wav2_decode(tmp, cd.start + j, cd.nx, cd.size, cd.ny, cd.nx * cd.size, max_value);
                }
            }
            for (int i = 0; i < tmp.Length; ++i)
            {
                tmp[i] = lut[tmp[i]];
            }

            int o = 0;
            for (int y = 0; y < lines; ++y)
            {
                foreach (ChannelData cd in cds)
                {
                    int n = cd.nx * cd.size;
                    for (int i = 0; i < n; ++i)
                    {
                        ushort v = tmp[cd.end + i];
                        output[o++] = (byte)v;
                        output[o++] = (byte)(v >> 8);
                    }
                    cd.end += n;
                }
            }
            return output;
        }

        private static int reverse_lut_from_bitmap(byte[] bitmap, ushort[] lut)
        {
            int k = 0;
            for (int i = 0; i < 65536; ++i)
            {
                if (i == 0 || (bitmap[i >> 3] & (1 << (i & 7))) != 0)
                {
                    lut[k++] = (ushort)i;
                }
            }
            int n = k - 1;
            while (k < 65536)
            {
                lut[k++] = 0;
            }
            return n;
        }

        // ---- Wavelet ----

        private static void wdec14(ushort l, ushort h, out ushort a, out ushort b)
        {
            int ls = (short)l;
            int hs = (short)h;
            int hi = hs;
            int ai = ls + (hi & 1) + (hi >> 1);
            int a_s = ai;
            int b_s = ai - hi;
            a = (ushort)a_s;
            b = (ushort)b_s;
        }

        private static void wdec16(ushort l, ushort h, out ushort a, out ushort b)
        {
            int m = l;
            int d = h;
            int bb = (m - (d >> 1)) & 0xffff;
            int aa = (d + bb - (1 << 15)) & 0xffff;
            b = (ushort)bb;
            a = (ushort)aa;
        }

        private static void wav2_decode(ushort[] data, int start, int nx, int ox, int ny, int oy, int mx)
        {
            bool w14 = mx < (1 << 14);
            int n = nx > ny ? ny : nx;
            int p = 1;
            int p2;
            while (p <= n)
            {
                p <<= 1;
            }
            p >>= 1;
            p2 = p;
            p >>= 1;
            while (p >= 1)
            {
                int py = start;
                int ey = start + oy * (ny - p2);
                int oy1 = oy * p;
                int oy2 = oy * p2;
                int ox1 = ox * p;
                int ox2 = ox * p2;
                ushort i00, i01, i10, i11;
                for (; py <= ey; py += oy2)
                {
                    int px = py;
                    int ex = py + ox * (nx - p2);
                    for (; px <= ex; px += ox2)
                    {
                        int p01 = px + ox1;
                        int p10 = px + oy1;
                        int p11 = p10 + ox1;
                        ushort a, b;
                        if (w14)
                        {
                            wdec14(data[px], data[p10], out i00, out i10);
                            wdec14(data[p01], data[p11], out i01, out i11);
                            wdec14(i00, i01, out a, out b);
                            data[px] = a;
                            data[p01] = b;
                            wdec14(i10, i11, out a, out b);
                            data[p10] = a;
                            data[p11] = b;
                        }
                        else
                        {
                            wdec16(data[px], data[p10], out i00, out i10);
                            wdec16(data[p01], data[p11], out i01, out i11);
                            wdec16(i00, i01, out a, out b);
                            data[px] = a;
                            data[p01] = b;
                            wdec16(i10, i11, out a, out b);
                            data[p10] = a;
                            data[p11] = b;
                        }
                    }
                    if ((nx & p) != 0)
                    {
                        int p10 = px + oy1;
                        ushort a, b;
                        if (w14)
                        {
                            wdec14(data[px], data[p10], out a, out b);
                        }
                        else
                        {
                            wdec16(data[px], data[p10], out a, out b);
                        }
                        data[px] = a;
                        data[p10] = b;
                    }
                }
                if ((ny & p) != 0)
                {
                    int px = py;
                    int ex = py + ox * (nx - p2);
                    for (; px <= ex; px += ox2)
                    {
                        int p01 = px + ox1;
                        ushort a, b;
                        if (w14)
                        {
                            wdec14(data[px], data[p01], out a, out b);
                        }
                        else
                        {
                            wdec16(data[px], data[p01], out a, out b);
                        }
                        data[px] = a;
                        data[p01] = b;
                    }
                }
                p2 = p;
                p >>= 1;
            }
        }

        // ---- Huffman ----

        private sealed class HufDec
        {
            public int len;
            public int lit;
            public int[] p;
        }

        private static uint read_uint(byte[] d, int pos)
        {
            return BitConverter.ToUInt32(d, pos);
        }

        private static long get_bits(int nbits, ref long c, ref int lc, byte[] d, ref int pos)
        {
            while (lc < nbits)
            {
                c = (c << 8) | d[pos++];
                lc += 8;
            }
            lc -= nbits;
            return (c >> lc) & ((1L << nbits) - 1);
        }

        private static void huf_uncompress(byte[] d, int pos, int compressed_size, ushort[] raw, int n_raw)
        {
            if (compressed_size == 0)
            {
                if (n_raw != 0)
                {
                    throw new InvalidDataException("corrupt PIZ OpenEXR data");
                }
                return;
            }
            int im = (int)read_uint(d, pos);
            int iM = (int)read_uint(d, pos + 4);
            int n_bits = (int)read_uint(d, pos + 12);
            if (im < 0 || im >= HUF_ENCSIZE || iM < 0 || iM >= HUF_ENCSIZE)
            {
                throw new InvalidDataException("corrupt PIZ OpenEXR data");
            }
            int c_pos = pos + 20;
            int end = pos + compressed_size;
            if (c_pos + (n_bits + 7) / 8 > end)
            {
                throw new InvalidDataException("corrupt PIZ OpenEXR data");
            }
            long[] freq = new long[HUF_ENCSIZE];
            unpack_enc_table(d, ref c_pos, end - c_pos, im, iM, freq);
            if (n_bits > 8 * (end - c_pos))
            {
                throw new InvalidDataException("corrupt PIZ OpenEXR data");
            }
            HufDec[] hdec = new HufDec[HUF_DECSIZE];
            for (int i = 0; i < HUF_DECSIZE; ++i)
            {
                hdec[i] = new HufDec();
            }
            build_dec_table(freq, im, iM, hdec);
            decode(freq, hdec, d, c_pos, n_bits, iM, n_raw, raw);
        }

        private static void unpack_enc_table(byte[] d, ref int pos, int ni, int im, int iM, long[] hcode)
        {
            int start = pos;
            long c = 0;
            int lc = 0;
            for (; im <= iM; ++im)
            {
                if (pos - start > ni)
                {
                    throw new InvalidDataException("corrupt PIZ OpenEXR data");
                }
                long l = hcode[im] = get_bits(6, ref c, ref lc, d, ref pos);
                if (l == LONG_ZEROCODE_RUN)
                {
                    int zerun = (int)get_bits(8, ref c, ref lc, d, ref pos) + SHORTEST_LONG_RUN;
                    if (im + zerun > iM + 1)
                    {
                        throw new InvalidDataException("corrupt PIZ OpenEXR data");
                    }
                    while (zerun-- > 0)
                    {
                        hcode[im++] = 0;
                    }
                    --im;
                }
                else if (l >= SHORT_ZEROCODE_RUN)
                {
                    int zerun = (int)l - SHORT_ZEROCODE_RUN + 2;
                    if (im + zerun > iM + 1)
                    {
                        throw new InvalidDataException("corrupt PIZ OpenEXR data");
                    }
                    while (zerun-- > 0)
                    {
                        hcode[im++] = 0;
                    }
                    --im;
                }
            }
            canonical_code_table(hcode);
        }

        private static void canonical_code_table(long[] hcode)
        {
            long[] n = new long[59];
            for (int i = 0; i < HUF_ENCSIZE; ++i)
            {
                n[hcode[i]] += 1;
            }
            long c = 0;
            for (int i = 58; i > 0; --i)
            {
                long nc = (c + n[i]) >> 1;
                n[i] = c;
                c = nc;
            }
            for (int i = 0; i < HUF_ENCSIZE; ++i)
            {
                int l = (int)hcode[i];
                if (l > 0)
                {
                    hcode[i] = l | (n[l]++ << 6);
                }
            }
        }

        private static int huf_length(long code)
        {
            return (int)(code & 63);
        }

        private static long huf_code(long code)
        {
            return code >> 6;
        }

        private static void build_dec_table(long[] hcode, int im, int iM, HufDec[] hdecod)
        {
            for (; im <= iM; ++im)
            {
                long c = huf_code(hcode[im]);
                int l = huf_length(hcode[im]);
                if ((c >> l) != 0)
                {
                    throw new InvalidDataException("corrupt PIZ OpenEXR data");
                }
                if (l > HUF_DECBITS)
                {
                    HufDec pl = hdecod[c >> (l - HUF_DECBITS)];
                    if (pl.len != 0)
                    {
                        throw new InvalidDataException("corrupt PIZ OpenEXR data");
                    }
                    pl.lit++;
                    if (pl.p != null)
                    {
                        int[] old = pl.p;
                        pl.p = new int[pl.lit];
                        Array.Copy(old, pl.p, pl.lit - 1);
                    }
                    else
                    {
                        pl.p = new int[1];
                    }
                    pl.p[pl.lit - 1] = im;
                }
                else if (l != 0)
                {
                    long index = c << (HUF_DECBITS - l);
                    for (long i = 1L << (HUF_DECBITS - l); i > 0; --i, ++index)
                    {
                        HufDec pl = hdecod[index];
                        if (pl.len != 0 || pl.p != null)
                        {
                            throw new InvalidDataException("corrupt PIZ OpenEXR data");
                        }
                        pl.len = l;
                        pl.lit = im;
                    }
                }
            }
        }

        private static void get_code(int po, int rlc, ref long c, ref int lc, byte[] d, ref int pos, ushort[] output, ref int o, int oe)
        {
            if (po == rlc)
            {
                if (lc < 8)
                {
                    c = (c << 8) | d[pos++];
                    lc += 8;
                }
                lc -= 8;
                int cs = (int)((c >> lc) & 0xff);
                if (o + cs > oe || o == 0)
                {
                    throw new InvalidDataException("corrupt PIZ OpenEXR data");
                }
                ushort s = output[o - 1];
                while (cs-- > 0)
                {
                    output[o++] = s;
                }
            }
            else if (o < oe)
            {
                output[o++] = (ushort)po;
            }
            else
            {
                throw new InvalidDataException("corrupt PIZ OpenEXR data");
            }
        }

        private static void decode(long[] hcode, HufDec[] hdecod, byte[] d, int pos, int ni, int rlc, int no, ushort[] output)
        {
            long c = 0;
            int lc = 0;
            int o = 0;
            int oe = no;
            int ie = pos + (ni + 7) / 8;
            while (pos < ie)
            {
                c = (c << 8) | d[pos++];
                lc += 8;
                while (lc >= HUF_DECBITS)
                {
                    HufDec pl = hdecod[(c >> (lc - HUF_DECBITS)) & HUF_DECMASK];
                    if (pl.len != 0)
                    {
                        lc -= pl.len;
                        get_code(pl.lit, rlc, ref c, ref lc, d, ref pos, output, ref o, oe);
                    }
                    else
                    {
                        if (pl.p == null)
                        {
                            throw new InvalidDataException("corrupt PIZ OpenEXR data");
                        }
                        int j;
                        for (j = 0; j < pl.lit; ++j)
                        {
                            int l = huf_length(hcode[pl.p[j]]);
                            while (lc < l && pos < ie)
                            {
                                c = (c << 8) | d[pos++];
                                lc += 8;
                            }
                            if (lc >= l)
                            {
                                if (huf_code(hcode[pl.p[j]]) == ((c >> (lc - l)) & ((1L << l) - 1)))
                                {
                                    lc -= l;
                                    get_code(pl.p[j], rlc, ref c, ref lc, d, ref pos, output, ref o, oe);
                                    break;
                                }
                            }
                        }
                        if (j == pl.lit)
                        {
                            throw new InvalidDataException("corrupt PIZ OpenEXR data");
                        }
                    }
                }
            }
            int i = (8 - ni) & 7;
            c >>= i;
            lc -= i;
            while (lc > 0)
            {
                HufDec pl = hdecod[(c << (HUF_DECBITS - lc)) & HUF_DECMASK];
                if (pl.len != 0)
                {
                    lc -= pl.len;
                    get_code(pl.lit, rlc, ref c, ref lc, d, ref pos, output, ref o, oe);
                }
                else
                {
                    throw new InvalidDataException("corrupt PIZ OpenEXR data");
                }
            }
            if (o != no)
            {
                throw new InvalidDataException("corrupt PIZ OpenEXR data");
            }
        }
    }
}
