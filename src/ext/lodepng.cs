// A C# implementation of the subset of the lodepng API that pbrt-v4 uses (PNG decoding
// and 8-bit encoding). lodepng itself is Copyright (c) 2005-2018 Lode Vandevenne (zlib
// license); this is an independent implementation of the same interface on top of
// System.IO.Compression.ZLibStream.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace pbrt
{
    public enum LodePNGColorType
    {
        LCT_GREY = 0,
        LCT_RGB = 2,
        LCT_PALETTE = 3,
        LCT_GREY_ALPHA = 4,
        LCT_RGBA = 6
    }

    public class LodePNGColorMode
    {
        public LodePNGColorType colortype = LodePNGColorType.LCT_RGBA;
        public uint bitdepth = 8;
        public byte[] palette = new byte[0];  // RGBA entries
    }

    public class LodePNGInfo
    {
        public uint compression_method;
        public uint filter_method;
        public uint interlace_method;
        public LodePNGColorMode color = new LodePNGColorMode();
    }

    public class LodePNGState
    {
        public LodePNGInfo info_png = new LodePNGInfo();
    }

    public static class lodepng
    {
        public static void lodepng_state_init(LodePNGState state)
        {
            state.info_png = new LodePNGInfo();
        }

        public static string lodepng_error_text(uint code)
        {
            switch (code)
            {
                case 0: return "no error, everything went ok";
                case 27: return "PNG file is smaller than a PNG header";
                case 28: return "incorrect PNG signature, it's no PNG or corrupted";
                case 29: return "first chunk is not the header chunk";
                case 30: return "chunk length too large, chunk broken off at end of file";
                case 31: return "illegal PNG color type or bpp";
                case 32: return "illegal PNG compression method";
                case 33: return "illegal PNG filter method";
                case 34: return "illegal PNG interlace method";
                case 35: return "chunk length of a chunk is too large or the chunk too small";
                case 36: return "illegal PNG filter type encountered";
                case 37: return "illegal bit depth for this color type given";
                case 57: return "invalid CRC encountered (checking CRC can be disabled)";
                case 58: return "invalid ADLER32 encountered (checking ADLER32 can be disabled)";
                case 59: return "requested color conversion not supported";
                case 62: return "conversion from color to grayscale not supported";
                case 63: return "png chunk too long";
                case 64: return "png chunk missing IDAT / invalid zlib data";
                case 91: return "invalid image size";
                default: return "unknown error code " + code;
            }
        }

        // CRC32
        private static readonly uint[] crcTable = MakeCrcTable();
        private static uint[] MakeCrcTable()
        {
            uint[] t = new uint[256];
            for (uint n = 0; n < 256; ++n)
            {
                uint c = n;
                for (int k = 0; k < 8; ++k)
                    c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }
        private static uint Crc32(byte[] buf, int start, int len)
        {
            uint c = 0xffffffffu;
            for (int i = 0; i < len; ++i)
                c = crcTable[(c ^ buf[start + i]) & 0xff] ^ (c >> 8);
            return c ^ 0xffffffffu;
        }

        private static uint BE32(byte[] d, int p)
        {
            return ((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3];
        }

        private static int SamplesPerPixel(LodePNGColorType ct)
        {
            switch (ct)
            {
                case LodePNGColorType.LCT_GREY: return 1;
                case LodePNGColorType.LCT_RGB: return 3;
                case LodePNGColorType.LCT_PALETTE: return 1;
                case LodePNGColorType.LCT_GREY_ALPHA: return 2;
                case LodePNGColorType.LCT_RGBA: return 4;
            }
            return 0;
        }

        private static bool ValidColorBitdepth(LodePNGColorType ct, uint bd)
        {
            switch (ct)
            {
                case LodePNGColorType.LCT_GREY: return bd == 1 || bd == 2 || bd == 4 || bd == 8 || bd == 16;
                case LodePNGColorType.LCT_PALETTE: return bd == 1 || bd == 2 || bd == 4 || bd == 8;
                case LodePNGColorType.LCT_RGB:
                case LodePNGColorType.LCT_GREY_ALPHA:
                case LodePNGColorType.LCT_RGBA: return bd == 8 || bd == 16;
            }
            return false;
        }

        public static uint lodepng_inspect(out uint w, out uint h, LodePNGState state, byte[] data, size_t size)
        {
            w = h = 0;
            if (size < 33) return 27;
            byte[] sig = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int i = 0; i < 8; ++i)
                if (data[i] != sig[i]) return 28;
            if (BE32(data, 8) != 13 || data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R')
                return 29;
            w = BE32(data, 16);
            h = BE32(data, 20);
            if (w == 0 || h == 0) return 93;
            state.info_png.color.bitdepth = data[24];
            state.info_png.color.colortype = (LodePNGColorType)data[25];
            state.info_png.compression_method = data[26];
            state.info_png.filter_method = data[27];
            state.info_png.interlace_method = data[28];
            if (!ValidColorBitdepth(state.info_png.color.colortype, state.info_png.color.bitdepth)) return 37;
            if (state.info_png.compression_method != 0) return 32;
            if (state.info_png.filter_method != 0) return 33;
            if (state.info_png.interlace_method > 1) return 34;
            return 0;
        }

        private static byte[] ZlibDecompress(byte[] data)
        {
            using (MemoryStream input = new MemoryStream(data))
            using (ZLibStream z = new ZLibStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                z.CopyTo(output);
                return output.ToArray();
            }
        }

        private static readonly int[] adamXStart = { 0, 4, 0, 2, 0, 1, 0 };
        private static readonly int[] adamYStart = { 0, 0, 4, 0, 2, 0, 1 };
        private static readonly int[] adamXStep = { 8, 8, 4, 4, 2, 2, 1 };
        private static readonly int[] adamYStep = { 8, 8, 8, 4, 4, 2, 2 };

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }

        // Decodes the PNG to the requested color type and bit depth (8 or 16). 16-bit samples
        // are produced big endian, like lodepng.
        public static uint lodepng_decode_memory(out byte[] outBuf, out uint w, out uint h, byte[] data, size_t size,
                                                 LodePNGColorType colortype, uint bitdepth)
        {
            outBuf = null;
            LodePNGState state = new LodePNGState();
            uint err = lodepng_inspect(out w, out h, state, data, size);
            if (err != 0) return err;
            LodePNGColorType srcType = state.info_png.color.colortype;
            int srcDepth = (int)state.info_png.color.bitdepth;
            int spp = SamplesPerPixel(srcType);
            int bppBits = spp * srcDepth;
            int fbpp = Math.Max(1, bppBits / 8);

            // Walk chunks
            MemoryStream idat = new MemoryStream();
            byte[] palette = new byte[0];
            int pos = 8;
            bool sawEnd = false;
            while (pos + 12 <= (int)size)
            {
                uint len = BE32(data, pos);
                if (len > 0x7fffffff) return 63;
                if (pos + 12 + (int)len > (int)size) return 30;
                string type = Encoding.ASCII.GetString(data, pos + 4, 4);
                uint crc = BE32(data, pos + 8 + (int)len);
                if (Crc32(data, pos + 4, 4 + (int)len) != crc) return 57;
                if (type == "IDAT")
                    idat.Write(data, pos + 8, (int)len);
                else if (type == "PLTE")
                {
                    int n = (int)len / 3;
                    palette = new byte[n * 4];
                    for (int i = 0; i < n; ++i)
                    {
                        palette[4 * i] = data[pos + 8 + 3 * i];
                        palette[4 * i + 1] = data[pos + 8 + 3 * i + 1];
                        palette[4 * i + 2] = data[pos + 8 + 3 * i + 2];
                        palette[4 * i + 3] = 255;
                    }
                }
                else if (type == "IEND") { sawEnd = true; break; }
                pos += 12 + (int)len;
            }
            if (idat.Length == 0) return 64;

            byte[] raw;
            try { raw = ZlibDecompress(idat.ToArray()); }
            catch (Exception) { return 64; }

            // Reduced images (one pass, or Adam7's seven)
            int width = (int)w, height = (int)h;
            ushort[] samples = new ushort[(long)width * height * spp];
            int passes = state.info_png.interlace_method == 1 ? 7 : 1;
            int rawPos = 0;
            for (int pass = 0; pass < passes; ++pass)
            {
                int xs = passes == 1 ? 0 : adamXStart[pass], ys = passes == 1 ? 0 : adamYStart[pass];
                int dx = passes == 1 ? 1 : adamXStep[pass], dy = passes == 1 ? 1 : adamYStep[pass];
                int pw = (width - xs + dx - 1) / dx, ph = (height - ys + dy - 1) / dy;
                if (width <= xs || height <= ys || pw <= 0 || ph <= 0) continue;
                int stride = (pw * bppBits + 7) / 8;
                if (rawPos + (long)ph * (stride + 1) > raw.Length) return 64;
                byte[] prev = new byte[stride];
                byte[] cur = new byte[stride];
                for (int y = 0; y < ph; ++y)
                {
                    int ft = raw[rawPos++];
                    Array.Copy(raw, rawPos, cur, 0, stride);
                    rawPos += stride;
                    switch (ft)
                    {
                        case 0: break;
                        case 1:
                            for (int i = fbpp; i < stride; ++i) cur[i] = (byte)(cur[i] + cur[i - fbpp]);
                            break;
                        case 2:
                            for (int i = 0; i < stride; ++i) cur[i] = (byte)(cur[i] + prev[i]);
                            break;
                        case 3:
                            for (int i = 0; i < stride; ++i)
                            {
                                int left = i >= fbpp ? cur[i - fbpp] : 0;
                                cur[i] = (byte)(cur[i] + ((left + prev[i]) >> 1));
                            }
                            break;
                        case 4:
                            for (int i = 0; i < stride; ++i)
                            {
                                int left = i >= fbpp ? cur[i - fbpp] : 0;
                                int up = prev[i];
                                int ul = i >= fbpp ? prev[i - fbpp] : 0;
                                cur[i] = (byte)(cur[i] + Paeth(left, up, ul));
                            }
                            break;
                        default: return 36;
                    }
                    // unpack samples
                    for (int x = 0; x < pw; ++x)
                    {
                        long outIdx = ((long)(ys + y * dy) * width + (xs + x * dx)) * spp;
                        for (int c = 0; c < spp; ++c)
                        {
                            int sampleIndex = x * spp + c;
                            ushort v;
                            if (srcDepth == 8) v = cur[sampleIndex];
                            else if (srcDepth == 16) v = (ushort)((cur[2 * sampleIndex] << 8) | cur[2 * sampleIndex + 1]);
                            else
                            {
                                int bitPos = sampleIndex * srcDepth;
                                int b = cur[bitPos >> 3];
                                int shift = 8 - srcDepth - (bitPos & 7);
                                v = (ushort)((b >> shift) & ((1 << srcDepth) - 1));
                            }
                            samples[outIdx + c] = v;
                        }
                    }
                    byte[] tmp = prev; prev = cur; cur = tmp;
                }
            }

            // Convert to the requested format
            int dstSpp = SamplesPerPixel(colortype);
            if (dstSpp == 0 || colortype == LodePNGColorType.LCT_PALETTE) return 59;
            if (bitdepth != 8 && bitdepth != 16) return 37;
            if (colortype == LodePNGColorType.LCT_GREY &&
                !(srcType == LodePNGColorType.LCT_GREY || srcType == LodePNGColorType.LCT_GREY_ALPHA))
                return 62;
            int dstBytes = (int)(bitdepth / 8);
            outBuf = new byte[(long)width * height * dstSpp * dstBytes];
            int hiMax = (1 << Math.Min(srcDepth, 16)) - 1;
            long op = 0;
            for (long i = 0; i < (long)width * height; ++i)
            {
                int r, g, b, a;
                // Produce RGBA16 for the source pixel
                switch (srcType)
                {
                    case LodePNGColorType.LCT_GREY:
                        r = g = b = ScaleTo16(samples[i * spp], srcDepth, hiMax); a = 65535; break;
                    case LodePNGColorType.LCT_GREY_ALPHA:
                        r = g = b = ScaleTo16(samples[i * spp], srcDepth, hiMax); a = ScaleTo16(samples[i * spp + 1], srcDepth, hiMax); break;
                    case LodePNGColorType.LCT_RGB:
                        r = ScaleTo16(samples[i * spp], srcDepth, hiMax); g = ScaleTo16(samples[i * spp + 1], srcDepth, hiMax);
                        b = ScaleTo16(samples[i * spp + 2], srcDepth, hiMax); a = 65535; break;
                    case LodePNGColorType.LCT_RGBA:
                        r = ScaleTo16(samples[i * spp], srcDepth, hiMax); g = ScaleTo16(samples[i * spp + 1], srcDepth, hiMax);
                        b = ScaleTo16(samples[i * spp + 2], srcDepth, hiMax); a = ScaleTo16(samples[i * spp + 3], srcDepth, hiMax); break;
                    default:  // palette
                        {
                            int idx = samples[i];
                            if (idx * 4 + 3 >= palette.Length) return 46;
                            r = palette[idx * 4] * 257; g = palette[idx * 4 + 1] * 257;
                            b = palette[idx * 4 + 2] * 257; a = palette[idx * 4 + 3] * 257;
                            break;
                        }
                }
                int[] px = null;
                switch (colortype)
                {
                    case LodePNGColorType.LCT_GREY: px = new int[] { r }; break;
                    case LodePNGColorType.LCT_GREY_ALPHA: px = new int[] { r, a }; break;
                    case LodePNGColorType.LCT_RGB: px = new int[] { r, g, b }; break;
                    case LodePNGColorType.LCT_RGBA: px = new int[] { r, g, b, a }; break;
                }
                for (int c = 0; c < dstSpp; ++c)
                {
                    if (bitdepth == 8) outBuf[op++] = (byte)(px[c] >> 8);
                    else { outBuf[op++] = (byte)(px[c] >> 8); outBuf[op++] = (byte)(px[c] & 255); }
                }
            }
            return 0;
        }

        private static int ScaleTo16(int v, int srcDepth, int hiMax)
        {
            if (srcDepth == 16) return v;
            if (srcDepth == 8) return v * 257;
            return v * 65535 / hiMax;
        }

        // lodepng::decode() wrapper with the C++ calling pattern used by pbrt.
        public static uint decode(List<byte> outList, uint w, uint h, byte[] data, size_t size,
                                  LodePNGColorType colortype, uint bitdepth)
        {
            byte[] buf;
            uint ww, hh;
            uint err = lodepng_decode_memory(out buf, out ww, out hh, data, size, colortype, bitdepth);
            if (err == 0)
            {
                outList.Clear();
                outList.AddRange(buf);
            }
            return err;
        }

        private static void WriteChunk(MemoryStream o, string type, byte[] payload)
        {
            byte[] lenb = { (byte)(payload.Length >> 24), (byte)(payload.Length >> 16), (byte)(payload.Length >> 8), (byte)payload.Length };
            o.Write(lenb, 0, 4);
            byte[] tb = Encoding.ASCII.GetBytes(type);
            byte[] body = new byte[4 + payload.Length];
            Array.Copy(tb, 0, body, 0, 4);
            Array.Copy(payload, 0, body, 4, payload.Length);
            o.Write(body, 0, body.Length);
            uint crc = Crc32(body, 0, body.Length);
            byte[] crcb = { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc };
            o.Write(crcb, 0, 4);
        }

        // Encodes 8-bit GREY/GREY_ALPHA/RGB/RGBA pixels.
        public static uint lodepng_encode_memory(out byte[] png, out size_t pngsize, byte[] image, uint w, uint h,
                                                 LodePNGColorType colortype, uint bitdepth)
        {
            png = null; pngsize = 0;
            if (w == 0 || h == 0) return 93;
            if (bitdepth != 8 || colortype == LodePNGColorType.LCT_PALETTE) return 37;
            int spp = SamplesPerPixel(colortype);
            int stride = (int)w * spp;

            // Filter each scanline, choosing the filter type that minimizes the sum of absolute values
            byte[] filtered = new byte[(long)(stride + 1) * h];
            byte[][] cand = new byte[5][];
            for (int i = 0; i < 5; ++i) cand[i] = new byte[stride];
            for (int y = 0; y < (int)h; ++y)
            {
                int rowOff = y * stride;
                int bestType = 0; long bestSum = long.MaxValue;
                for (int ft = 0; ft < 5; ++ft)
                {
                    long sum = 0;
                    for (int i = 0; i < stride; ++i)
                    {
                        int cur = image[rowOff + i];
                        int left = i >= spp ? image[rowOff + i - spp] : 0;
                        int up = y > 0 ? image[rowOff - stride + i] : 0;
                        int ul = (y > 0 && i >= spp) ? image[rowOff - stride + i - spp] : 0;
                        int pred;
                        switch (ft)
                        {
                            case 0: pred = 0; break;
                            case 1: pred = left; break;
                            case 2: pred = up; break;
                            case 3: pred = (left + up) >> 1; break;
                            default: pred = Paeth(left, up, ul); break;
                        }
                        byte v = (byte)(cur - pred);
                        cand[ft][i] = v;
                        sum += v < 128 ? v : 256 - v;
                    }
                    if (sum < bestSum) { bestSum = sum; bestType = ft; }
                }
                filtered[(long)y * (stride + 1)] = (byte)bestType;
                Array.Copy(cand[bestType], 0, filtered, (long)y * (stride + 1) + 1, stride);
            }

            byte[] compressed;
            using (MemoryStream cs = new MemoryStream())
            {
                using (ZLibStream z = new ZLibStream(cs, CompressionLevel.Optimal, true))
                    z.Write(filtered, 0, filtered.Length);
                compressed = cs.ToArray();
            }

            using (MemoryStream o = new MemoryStream())
            {
                byte[] sig = { 137, 80, 78, 71, 13, 10, 26, 10 };
                o.Write(sig, 0, 8);
                byte[] ihdr = new byte[13];
                ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
                ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
                ihdr[8] = (byte)bitdepth; ihdr[9] = (byte)colortype; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
                WriteChunk(o, "IHDR", ihdr);
                WriteChunk(o, "IDAT", compressed);
                WriteChunk(o, "IEND", new byte[0]);
                png = o.ToArray();
                pngsize = (size_t)png.Length;
            }
            return 0;
        }
    }
}
