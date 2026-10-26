// A C# implementation of the part of the stb_image.h API that pbrt-v4 uses
// (stbi_load, stbi_loadf, stbi_failure_reason, stbi_image_free). stb_image is
// public domain / MIT (Sean Barrett). Supported here: PNG (via the lodepng port),
// uncompressed and RLE TGA, uncompressed BMP, binary PGM/PPM, and Radiance HDR.
// JPEG (baseline + progressive), GIF (first frame), PSD (8 bits per channel) and PIC are ported from stb_image.h.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace pbrt
{
    public static partial class stb_image
    {
        private static string failure_reason = "";
        public static string stbi_failure_reason() { return failure_reason; }
        public static void stbi_image_free(object data) { }
        private static byte[] Fail(string why) { failure_reason = why; return null; }

        private static byte[] Convert(byte[] src, int w, int h, int n, int req)
        {
            if (req == 0 || req == n) return src;
            byte[] dst = new byte[(long)w * h * req];
            for (long i = 0; i < (long)w * h; ++i)
            {
                byte r, g, b, a = 255;
                if (n == 1 || n == 2) { r = g = b = src[i * n]; if (n == 2) a = src[i * n + 1]; }
                else { r = src[i * n]; g = src[i * n + 1]; b = src[i * n + 2]; if (n == 4) a = src[i * n + 3]; }
                byte y = (byte)((r * 77 + g * 150 + 29 * b) >> 8);
                switch (req)
                {
                    case 1: dst[i] = y; break;
                    case 2: dst[i * 2] = y; dst[i * 2 + 1] = a; break;
                    case 3: dst[i * 3] = r; dst[i * 3 + 1] = g; dst[i * 3 + 2] = b; break;
                    case 4: dst[i * 4] = r; dst[i * 4 + 1] = g; dst[i * 4 + 2] = b; dst[i * 4 + 3] = a; break;
                }
            }
            return dst;
        }

        public static byte[] stbi_load(string filename, out int x, out int y, out int comp, int req_comp)
        {
            x = y = comp = 0;
            byte[] d;
            try { d = File.ReadAllBytes(filename); }
            catch (Exception) { return Fail("can't fopen"); }
            return stbi_load_from_memory(d, out x, out y, out comp, req_comp);
        }

        public static byte[] stbi_load_from_memory(byte[] d, out int x, out int y, out int comp, int req_comp)
        {
            x = y = comp = 0;
            if (d.Length > 8 && d[0] == 137 && d[1] == 80 && d[2] == 78 && d[3] == 71)
                return LoadPNG(d, out x, out y, out comp, req_comp);
            if (d.Length > 2 && d[0] == 'B' && d[1] == 'M')
                return LoadBMP(d, out x, out y, out comp, req_comp);
            if (d.Length > 2 && d[0] == 'P' && (d[1] == '5' || d[1] == '6'))
                return LoadPNM(d, out x, out y, out comp, req_comp);
            if (d.Length > 2 && d[0] == '#' && d[1] == '?')
            {
                float[] f = LoadHDRData(d, out x, out y, out comp);
                if (f == null) return null;
                byte[] b = new byte[f.Length];
                for (int i = 0; i < f.Length; ++i)
                    b[i] = (byte)Math.Clamp((int)(Math.Pow(f[i], 1.0f / 2.2f) * 255 + 0.5f), 0, 255);
                return Convert(b, x, y, comp, req_comp);
            }
            {
                StbiContext ctx = new StbiContext(d);
                byte[] r = null;
                // (stbi__load_main tries the formats in this order: jpeg, png, bmp, gif, psd, pic, pnm, hdr, tga)
                if (stbi__jpeg_test(new StbiContext(d)))
                {
                    r = stbi__jpeg_load(ctx, out x, out y, out comp, req_comp);
                    if (r != null && req_comp != 0) comp = req_comp;
                    return r;
                }
                if (stbi__gif_test(new StbiContext(d)))
                {
                    r = stbi__gif_load(ctx, out x, out y, out comp, req_comp);
                    if (r != null && req_comp != 0) comp = req_comp;
                    return r;
                }
                if (stbi__psd_test(new StbiContext(d)))
                {
                    r = stbi__psd_load(ctx, out x, out y, out comp, req_comp);
                    if (r != null && req_comp != 0) comp = req_comp;
                    return r;
                }
                // pbrt compiles stb_image with STBI_NO_PIC ("too old school"), so PIC is never decoded.
                if (!STBI_NO_PIC && stbi__pic_test(new StbiContext(d)))
                {
                    r = stbi__pic_load(ctx, out x, out y, out comp, req_comp);
                    if (r != null && req_comp != 0) comp = req_comp;
                    return r;
                }
            }
            if (d.Length > 18)
            {
                byte[] t = LoadTGA(d, out x, out y, out comp, req_comp);
                if (t != null) return t;
            }
            return Fail("unknown image type");
        }

        private static byte[] LoadPNG(byte[] d, out int x, out int y, out int comp, int req_comp)
        {
            x = y = comp = 0;
            LodePNGState st = new LodePNGState();
            uint w, h;
            uint err = lodepng.lodepng_inspect(out w, out h, st, d, (size_t)d.Length);
            if (err != 0) return Fail(lodepng.lodepng_error_text(err));
            LodePNGColorType ct;
            switch (st.info_png.color.colortype)
            {
                case LodePNGColorType.LCT_GREY: ct = LodePNGColorType.LCT_GREY; comp = 1; break;
                case LodePNGColorType.LCT_GREY_ALPHA: ct = LodePNGColorType.LCT_GREY_ALPHA; comp = 2; break;
                case LodePNGColorType.LCT_RGBA: ct = LodePNGColorType.LCT_RGBA; comp = 4; break;
                default: ct = LodePNGColorType.LCT_RGB; comp = 3; break;
            }
            byte[] buf;
            err = lodepng.lodepng_decode_memory(out buf, out w, out h, d, (size_t)d.Length, ct, 8);
            if (err != 0) return Fail(lodepng.lodepng_error_text(err));
            x = (int)w; y = (int)h;
            byte[] r = Convert(buf, x, y, comp, req_comp);
            if (req_comp != 0) comp = req_comp;
            return r;
        }

        private static byte[] LoadBMP(byte[] d, out int x, out int y, out int comp, int req_comp)
        {
            x = y = comp = 0;
            int offset = BitConverter.ToInt32(d, 10);
            int hsz = BitConverter.ToInt32(d, 14);
            if (hsz < 40) return Fail("BMP type not supported: unsupported header");
            int w = BitConverter.ToInt32(d, 18), h = BitConverter.ToInt32(d, 22);
            int bpp = BitConverter.ToInt16(d, 28);
            int compression = BitConverter.ToInt32(d, 30);
            if (compression != 0 && compression != 3) return Fail("BMP type not supported: compressed");
            bool flip = h > 0; h = Math.Abs(h);
            if (bpp != 24 && bpp != 32 && bpp != 8) return Fail("BMP type not supported: bpp");
            int ncomp = bpp == 32 ? 4 : 3;
            byte[] pal = null;
            if (bpp == 8)
            {
                int pcount = BitConverter.ToInt32(d, 46); if (pcount == 0) pcount = 256;
                pal = new byte[pcount * 3];
                for (int i = 0; i < pcount; ++i)
                {
                    pal[3 * i] = d[14 + hsz + 4 * i + 2]; pal[3 * i + 1] = d[14 + hsz + 4 * i + 1]; pal[3 * i + 2] = d[14 + hsz + 4 * i];
                }
            }
            int stride = ((w * bpp + 31) / 32) * 4;
            byte[] outb = new byte[(long)w * h * ncomp];
            for (int j = 0; j < h; ++j)
            {
                int srcRow = offset + (flip ? (h - 1 - j) : j) * stride;
                for (int i = 0; i < w; ++i)
                {
                    long o = ((long)j * w + i) * ncomp;
                    if (bpp == 8) { int idx = d[srcRow + i]; outb[o] = pal[3 * idx]; outb[o + 1] = pal[3 * idx + 1]; outb[o + 2] = pal[3 * idx + 2]; }
                    else
                    {
                        int s = srcRow + i * (bpp / 8);
                        outb[o] = d[s + 2]; outb[o + 1] = d[s + 1]; outb[o + 2] = d[s];
                        if (ncomp == 4) outb[o + 3] = d[s + 3];
                    }
                }
            }
            x = w; y = h; comp = ncomp;
            byte[] r = Convert(outb, w, h, ncomp, req_comp);
            if (req_comp != 0) comp = req_comp;
            return r;
        }

        private static byte[] LoadPNM(byte[] d, out int x, out int y, out int comp, int req_comp)
        {
            x = y = comp = 0;
            int p = 2;
            int[] v = new int[3];
            for (int k = 0; k < 3; ++k)
            {
                while (p < d.Length && (d[p] == ' ' || d[p] == '\n' || d[p] == '\r' || d[p] == '\t' || d[p] == '#'))
                {
                    if (d[p] == '#') while (p < d.Length && d[p] != '\n') ++p;
                    else ++p;
                }
                int n = 0;
                while (p < d.Length && d[p] >= '0' && d[p] <= '9') n = n * 10 + (d[p++] - '0');
                v[k] = n;
            }
            ++p;
            if (v[2] != 255) return Fail("PNM maxval not supported");
            x = v[0]; y = v[1]; comp = d[1] == '5' ? 1 : 3;
            byte[] buf = new byte[(long)x * y * comp];
            Array.Copy(d, p, buf, 0, buf.Length);
            byte[] r = Convert(buf, x, y, comp, req_comp);
            if (req_comp != 0) comp = req_comp;
            return r;
        }

        private static byte[] LoadTGA(byte[] d, out int x, out int y, out int comp, int req_comp)
        {
            x = y = comp = 0;
            int idLen = d[0], cmapType = d[1], imgType = d[2];
            if (cmapType != 0 || !(imgType == 2 || imgType == 3 || imgType == 10 || imgType == 11)) return null;
            int w = BitConverter.ToUInt16(d, 12), h = BitConverter.ToUInt16(d, 14);
            int bpp = d[16], desc = d[17];
            if (w == 0 || h == 0 || !(bpp == 8 || bpp == 24 || bpp == 32)) return null;
            int bytesPP = bpp / 8;
            bool rle = imgType >= 9;
            byte[] px = new byte[(long)w * h * bytesPP];
            int p = 18 + idLen;
            long n = 0, total = (long)w * h;
            try
            {
                while (n < total)
                {
                    if (rle)
                    {
                        int hd = d[p++];
                        int cnt = (hd & 127) + 1;
                        if ((hd & 128) != 0)
                        {
                            for (int c = 0; c < cnt && n < total; ++c, ++n) Array.Copy(d, p, px, n * bytesPP, bytesPP);
                            p += bytesPP;
                        }
                        else
                            for (int c = 0; c < cnt && n < total; ++c, ++n, p += bytesPP) Array.Copy(d, p, px, n * bytesPP, bytesPP);
                    }
                    else { Array.Copy(d, p, px, 0, px.Length); n = total; }
                }
            }
            catch (Exception) { return null; }
            comp = bytesPP == 1 ? 1 : bytesPP;
            byte[] outb = new byte[px.Length];
            bool topFirst = (desc & 0x20) != 0;
            for (int j = 0; j < h; ++j)
            {
                int sj = topFirst ? j : h - 1 - j;
                for (int i = 0; i < w; ++i)
                {
                    long s = ((long)sj * w + i) * bytesPP, o = ((long)j * w + i) * bytesPP;
                    if (bytesPP == 1) outb[o] = px[s];
                    else
                    {
                        outb[o] = px[s + 2]; outb[o + 1] = px[s + 1]; outb[o + 2] = px[s];
                        if (bytesPP == 4) outb[o + 3] = px[s + 3];
                    }
                }
            }
            x = w; y = h;
            byte[] r = Convert(outb, w, h, comp, req_comp);
            if (req_comp != 0) comp = req_comp;
            return r;
        }

        private static string ReadLine(byte[] d, ref int p)
        {
            StringBuilder sb = new StringBuilder();
            while (p < d.Length && d[p] != '\n') sb.Append((char)d[p++]);
            ++p;
            return sb.ToString();
        }

        private static void RGBE2Float(byte[] rgbe, int o, float[] outp, long po)
        {
            if (rgbe[o + 3] != 0)
            {
                float f1 = (float)Math.ScaleB(1.0, rgbe[o + 3] - (128 + 8));
                outp[po] = rgbe[o] * f1; outp[po + 1] = rgbe[o + 1] * f1; outp[po + 2] = rgbe[o + 2] * f1;
            }
            else
                outp[po] = outp[po + 1] = outp[po + 2] = 0;
        }

        private static float[] LoadHDRData(byte[] d, out int x, out int y, out int comp)
        {
            x = y = comp = 0;
            int p = 0;
            string sig = ReadLine(d, ref p);
            if (sig != "#?RADIANCE" && sig != "#?RGBE") { failure_reason = "Corrupt HDR"; return null; }
            bool valid = false;
            while (true)
            {
                string line = ReadLine(d, ref p);
                if (p > d.Length) { failure_reason = "Corrupt HDR"; return null; }
                if (line.Length == 0) break;
                if (line == "FORMAT=32-bit_rle_rgbe") valid = true;
            }
            if (!valid) { failure_reason = "Unsupported HDR format"; return null; }
            string res = ReadLine(d, ref p);
            string[] t = res.Split(' ');
            if (t.Length != 4 || t[0] != "-Y" || t[2] != "+X") { failure_reason = "Unsupported HDR format"; return null; }
            int h = int.Parse(t[1]), w = int.Parse(t[3]);
            x = w; y = h; comp = 3;
            float[] outp = new float[(long)w * h * 3];
            byte[] scan = new byte[w * 4];
            byte[] rgbe = new byte[4];
            for (int j = 0; j < h; ++j)
            {
                bool flat = w < 8 || w >= 32768;
                if (!flat)
                {
                    if (d[p] != 2 || d[p + 1] != 2 || (d[p + 2] & 0x80) != 0) flat = true;
                    else
                    {
                        if (((d[p + 2] << 8) | d[p + 3]) != w) { failure_reason = "Corrupt HDR"; return null; }
                        p += 4;
                        for (int c = 0; c < 4; ++c)
                        {
                            int i = 0;
                            while (i < w)
                            {
                                int cnt = d[p++];
                                if (cnt > 128)
                                {
                                    cnt -= 128;
                                    byte v = d[p++];
                                    if (i + cnt > w) { failure_reason = "Corrupt HDR"; return null; }
                                    for (int k = 0; k < cnt; ++k) scan[(i++) * 4 + c] = v;
                                }
                                else
                                {
                                    if (cnt == 0 || i + cnt > w) { failure_reason = "Corrupt HDR"; return null; }
                                    for (int k = 0; k < cnt; ++k) scan[(i++) * 4 + c] = d[p++];
                                }
                            }
                        }
                        for (int i = 0; i < w; ++i) RGBE2Float(scan, i * 4, outp, ((long)j * w + i) * 3);
                    }
                }
                if (flat)
                {
                    // Old-style flat scanlines (no run-length repeat codes handled)
                    for (int i = 0; i < w; ++i)
                    {
                        Array.Copy(d, p, rgbe, 0, 4); p += 4;
                        RGBE2Float(rgbe, 0, outp, ((long)j * w + i) * 3);
                    }
                }
            }
            return outp;
        }

        public static float[] stbi_loadf(string filename, out int x, out int y, out int comp, int req_comp)
        {
            x = y = comp = 0;
            byte[] d;
            try { d = File.ReadAllBytes(filename); }
            catch (Exception) { failure_reason = "can't fopen"; return null; }
            if (d.Length > 2 && d[0] == '#' && d[1] == '?')
                return LoadHDRData(d, out x, out y, out comp);
            // LDR images are converted with gamma 2.2 like stb's default
            byte[] b = stbi_load_from_memory(d, out x, out y, out comp, req_comp);
            if (b == null) return null;
            float[] f = new float[b.Length];
            int nc = comp;
            for (int i = 0; i < b.Length; ++i)
                f[i] = ((i % nc) == 3 || (nc == 2 && (i % nc) == 1)) ? b[i] / 255.0f : (float)Math.Pow(b[i] / 255.0f, 2.2f);
            return f;
        }

        // ------------------------------------------------------------------------------------------
        // Decoder input context (stbi__context over an in-memory buffer)
        private sealed class StbiContext
        {
            public byte[] buf;
            public int pos;
            public int img_x, img_y, img_n;
            public StbiContext(byte[] b) { buf = b; pos = 0; }
            public int get8() { return pos < buf.Length ? buf[pos++] : 0; }
            public bool at_eof() { return pos >= buf.Length; }
            public void skip(int n)
            {
                if (n < 0) { pos = buf.Length; return; }
                pos += n;
            }
            public int get16be() { int z = get8(); return (z << 8) + get8(); }
            public uint get32be() { uint z = (uint)get16be(); return (z << 16) + (uint)get16be(); }
            public int get16le() { int z = get8(); return z + (get8() << 8); }
            public uint get32le() { uint z = (uint)get16le(); return z + ((uint)get16le() << 16); }
            public bool getn(byte[] dst, int off, int n)
            {
                if (pos + n > buf.Length) return false;
                Array.Copy(buf, pos, dst, off, n);
                pos += n;
                return true;
            }
        }

        private static int stbi__err(string why, string reason) { failure_reason = reason; return 0; }
        private static byte[] stbi__errpuc(string why, string reason) { failure_reason = reason; return null; }

        private static byte stbi__compute_y(int r, int g, int b)
        {
            return (byte)(((r * 77) + (g * 150) + (29 * b)) >> 8);
        }

        // ------------------------------------------------------------------------------------------
        // JPEG (baseline and progressive), port of the scalar path of stb_image.h
        private const int FAST_BITS = 9;
        private const int STBI__MARKER_none = 0xff;
        private const int STBI__SCAN_load = 0, STBI__SCAN_type = 1, STBI__SCAN_header = 2;

        private sealed class stbi__huffman
        {
            public byte[] fast = new byte[1 << FAST_BITS];
            public ushort[] code = new ushort[256];
            public byte[] values = new byte[256];
            public byte[] size = new byte[257];
            public uint[] maxcode = new uint[18];
            public int[] delta = new int[17];
        }

        private sealed class stbi__jpeg_comp
        {
            public int id, h, v, tq, hd, ha, dc_pred;
            public int x, y, w2, h2;
            public byte[] data;
            public byte[] linebuf;
            public short[] coeff;
            public int coeff_w, coeff_h;
        }

        private sealed class stbi__jpeg
        {
            public StbiContext s;
            public stbi__huffman[] huff_dc = new stbi__huffman[4];
            public stbi__huffman[] huff_ac = new stbi__huffman[4];
            public ushort[][] dequant = new ushort[4][];
            public short[][] fast_ac = new short[4][];
            public int img_h_max, img_v_max;
            public int img_mcu_x, img_mcu_y;
            public int img_mcu_w, img_mcu_h;
            public stbi__jpeg_comp[] img_comp = new stbi__jpeg_comp[4];
            public uint code_buffer;
            public int code_bits;
            public byte marker;
            public int nomore;
            public int progressive;
            public int spec_start, spec_end, succ_high, succ_low;
            public int eob_run;
            public int jfif;
            public int app14_color_transform;
            public int rgb;
            public int scan_n;
            public int[] order = new int[4];
            public int restart_interval, todo;

            public stbi__jpeg(StbiContext s)
            {
                this.s = s;
                for (int i = 0; i < 4; ++i)
                {
                    huff_dc[i] = new stbi__huffman();
                    huff_ac[i] = new stbi__huffman();
                    dequant[i] = new ushort[64];
                    fast_ac[i] = new short[1 << FAST_BITS];
                    img_comp[i] = new stbi__jpeg_comp();
                }
            }
        }

        private static int stbi__build_huffman(stbi__huffman h, int[] count)
        {
            int i, j, k = 0;
            uint code;
            // build size list for each symbol (from JPEG spec)
            for (i = 0; i < 16; ++i)
                for (j = 0; j < count[i]; ++j)
                    h.size[k++] = (byte)(i + 1);
            h.size[k] = 0;

            // compute actual symbols (from jpeg spec)
            code = 0;
            k = 0;
            for (j = 1; j <= 16; ++j)
            {
                // compute delta to add to code to compute symbol id
                h.delta[j] = (int)(k - code);
                if (h.size[k] == j)
                {
                    while (h.size[k] == j)
                        h.code[k++] = (ushort)(code++);
                    if (code - 1 >= (1u << j)) return stbi__err("bad code lengths", "Corrupt JPEG");
                }
                // compute largest code + 1 for this size, preshifted as needed later
                h.maxcode[j] = code << (16 - j);
                code <<= 1;
            }
            h.maxcode[j] = 0xffffffff;

            // build non-spec acceleration table; 255 is flag for not-accelerated
            for (i = 0; i < (1 << FAST_BITS); ++i) h.fast[i] = 255;
            for (i = 0; i < k; ++i)
            {
                int s = h.size[i];
                if (s <= FAST_BITS)
                {
                    int c = h.code[i] << (FAST_BITS - s);
                    int m = 1 << (FAST_BITS - s);
                    for (j = 0; j < m; ++j)
                        h.fast[c + j] = (byte)i;
                }
            }
            return 1;
        }

        // build a table that decodes both magnitude and value of small ACs in one go.
        private static void stbi__build_fast_ac(short[] fast_ac, stbi__huffman h)
        {
            for (int i = 0; i < (1 << FAST_BITS); ++i)
            {
                byte fast = h.fast[i];
                fast_ac[i] = 0;
                if (fast < 255)
                {
                    int rs = h.values[fast];
                    int run = (rs >> 4) & 15;
                    int magbits = rs & 15;
                    int len = h.size[fast];

                    if (magbits != 0 && len + magbits <= FAST_BITS)
                    {
                        // magnitude code followed by receive_extend code
                        int k = ((i << len) & ((1 << FAST_BITS) - 1)) >> (FAST_BITS - magbits);
                        int m = 1 << (magbits - 1);
                        if (k < m) k += (int)((~0U << magbits) + 1);
                        // if the result is small enough, we can fit it in fast_ac table
                        if (k >= -128 && k <= 127)
                            fast_ac[i] = (short)((k * 256) + (run * 16) + (len + magbits));
                    }
                }
            }
        }

        private static void stbi__grow_buffer_unsafe(stbi__jpeg j)
        {
            do
            {
                uint b = j.nomore != 0 ? 0 : (uint)j.s.get8();
                if (b == 0xff)
                {
                    int c = j.s.get8();
                    while (c == 0xff) c = j.s.get8(); // consume fill bytes
                    if (c != 0)
                    {
                        j.marker = (byte)c;
                        j.nomore = 1;
                        return;
                    }
                }
                j.code_buffer |= b << (24 - j.code_bits);
                j.code_bits += 8;
            } while (j.code_bits <= 24);
        }

        // (1 << n) - 1
        private static readonly uint[] stbi__bmask = { 0, 1, 3, 7, 15, 31, 63, 127, 255, 511, 1023, 2047, 4095, 8191, 16383, 32767, 65535 };

        private static uint stbi_lrot(uint x, int y) { return (x << y) | (x >> (32 - y)); }

        // decode a jpeg huffman value from the bitstream
        private static int stbi__jpeg_huff_decode(stbi__jpeg j, stbi__huffman h)
        {
            uint temp;
            int c, k;

            if (j.code_bits < 16) stbi__grow_buffer_unsafe(j);

            // look at the top FAST_BITS and determine what symbol ID it is, if the code is <= FAST_BITS
            c = (int)((j.code_buffer >> (32 - FAST_BITS)) & ((1 << FAST_BITS) - 1));
            k = h.fast[c];
            if (k < 255)
            {
                int s = h.size[k];
                if (s > j.code_bits)
                    return -1;
                j.code_buffer <<= s;
                j.code_bits -= s;
                return h.values[k];
            }

            temp = j.code_buffer >> 16;
            for (k = FAST_BITS + 1; ; ++k)
                if (temp < h.maxcode[k])
                    break;
            if (k == 17)
            {
                // error! code not found
                j.code_bits -= 16;
                return -1;
            }

            if (k > j.code_bits)
                return -1;

            // convert the huffman code to the symbol id
            c = (int)(((j.code_buffer >> (32 - k)) & stbi__bmask[k]) + h.delta[k]);

            // convert the id to a symbol
            j.code_bits -= k;
            j.code_buffer <<= k;
            return h.values[c];
        }

        // bias[n] = (-1<<n) + 1
        private static readonly int[] stbi__jbias = { 0, -1, -3, -7, -15, -31, -63, -127, -255, -511, -1023, -2047, -4095, -8191, -16383, -32767 };

        // combined JPEG 'receive' and JPEG 'extend', since baseline always extends everything it receives.
        private static int stbi__extend_receive(stbi__jpeg j, int n)
        {
            uint k;
            int sgn;
            if (j.code_bits < n) stbi__grow_buffer_unsafe(j);

            sgn = (int)(j.code_buffer >> 31); // sign bit always in MSB; 0 if MSB clear (positive), 1 if MSB set (negative)
            k = stbi_lrot(j.code_buffer, n);
            j.code_buffer = k & ~stbi__bmask[n];
            k &= stbi__bmask[n];
            j.code_bits -= n;
            return (int)k + (stbi__jbias[n] & (sgn - 1));
        }

        // get some unsigned bits
        private static int stbi__jpeg_get_bits(stbi__jpeg j, int n)
        {
            uint k;
            if (j.code_bits < n) stbi__grow_buffer_unsafe(j);
            k = stbi_lrot(j.code_buffer, n);
            j.code_buffer = k & ~stbi__bmask[n];
            k &= stbi__bmask[n];
            j.code_bits -= n;
            return (int)k;
        }

        private static int stbi__jpeg_get_bit(stbi__jpeg j)
        {
            uint k;
            if (j.code_bits < 1) stbi__grow_buffer_unsafe(j);
            k = j.code_buffer;
            j.code_buffer <<= 1;
            --j.code_bits;
            return (int)(k & 0x80000000);
        }

        // given a value that's at position X in the zigzag stream, where does it appear in the 8x8 matrix coded as row-major?
        private static readonly byte[] stbi__jpeg_dezigzag =
        {
            0, 1, 8, 16, 9, 2, 3, 10,
            17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63,
            // let corrupt input sample past end
            63, 63, 63, 63, 63, 63, 63, 63,
            63, 63, 63, 63, 63, 63, 63
        };

        // decode one 64-entry block (data is the 64 shorts starting at doff)
        private static int stbi__jpeg_decode_block(stbi__jpeg j, short[] data, int doff, stbi__huffman hdc, stbi__huffman hac,
                                                   short[] fac, int b, ushort[] dequant)
        {
            int diff, dc, k;
            int t;

            if (j.code_bits < 16) stbi__grow_buffer_unsafe(j);
            t = stbi__jpeg_huff_decode(j, hdc);
            if (t < 0 || t > 15) return stbi__err("bad huffman code", "Corrupt JPEG");

            // 0 all the ac values now
            Array.Clear(data, doff, 64);

            diff = t != 0 ? stbi__extend_receive(j, t) : 0;
            dc = j.img_comp[b].dc_pred + diff;
            j.img_comp[b].dc_pred = dc;
            data[doff] = (short)(dc * dequant[0]);

            // decode AC components, see JPEG spec
            k = 1;
            do
            {
                uint zig;
                int c, r, s;
                if (j.code_bits < 16) stbi__grow_buffer_unsafe(j);
                c = (int)((j.code_buffer >> (32 - FAST_BITS)) & ((1 << FAST_BITS) - 1));
                r = fac[c];
                if (r != 0)
                { // fast-AC path
                    k += (r >> 4) & 15; // run
                    s = r & 15; // combined length
                    j.code_buffer <<= s;
                    j.code_bits -= s;
                    // decode into unzigzag'd location
                    zig = stbi__jpeg_dezigzag[k++];
                    data[doff + zig] = (short)((r >> 8) * dequant[zig]);
                }
                else
                {
                    int rs = stbi__jpeg_huff_decode(j, hac);
                    if (rs < 0) return stbi__err("bad huffman code", "Corrupt JPEG");
                    s = rs & 15;
                    r = rs >> 4;
                    if (s == 0)
                    {
                        if (rs != 0xf0) break; // end block
                        k += 16;
                    }
                    else
                    {
                        k += r;
                        // decode into unzigzag'd location
                        zig = stbi__jpeg_dezigzag[k++];
                        data[doff + zig] = (short)(stbi__extend_receive(j, s) * dequant[zig]);
                    }
                }
            } while (k < 64);
            return 1;
        }

        private static int stbi__jpeg_decode_block_prog_dc(stbi__jpeg j, short[] data, int doff, stbi__huffman hdc, int b)
        {
            int diff, dc;
            int t;
            if (j.spec_end != 0) return stbi__err("can't merge dc and ac", "Corrupt JPEG");

            if (j.code_bits < 16) stbi__grow_buffer_unsafe(j);

            if (j.succ_high == 0)
            {
                // first scan for DC coefficient, must be first
                Array.Clear(data, doff, 64); // 0 all the ac values now
                t = stbi__jpeg_huff_decode(j, hdc);
                if (t < 0 || t > 15) return stbi__err("can't merge dc and ac", "Corrupt JPEG");
                diff = t != 0 ? stbi__extend_receive(j, t) : 0;

                dc = j.img_comp[b].dc_pred + diff;
                j.img_comp[b].dc_pred = dc;
                data[doff] = (short)(dc * (1 << j.succ_low));
            }
            else
            {
                // refinement scan for DC coefficient
                if (stbi__jpeg_get_bit(j) != 0)
                    data[doff] += (short)(1 << j.succ_low);
            }
            return 1;
        }

        private static int stbi__jpeg_decode_block_prog_ac(stbi__jpeg j, short[] data, int doff, stbi__huffman hac, short[] fac)
        {
            int k;
            if (j.spec_start == 0) return stbi__err("can't merge dc and ac", "Corrupt JPEG");

            if (j.succ_high == 0)
            {
                int shift = j.succ_low;

                if (j.eob_run != 0)
                {
                    --j.eob_run;
                    return 1;
                }

                k = j.spec_start;
                do
                {
                    uint zig;
                    int c, r, s;
                    if (j.code_bits < 16) stbi__grow_buffer_unsafe(j);
                    c = (int)((j.code_buffer >> (32 - FAST_BITS)) & ((1 << FAST_BITS) - 1));
                    r = fac[c];
                    if (r != 0)
                    { // fast-AC path
                        k += (r >> 4) & 15; // run
                        s = r & 15; // combined length
                        j.code_buffer <<= s;
                        j.code_bits -= s;
                        zig = stbi__jpeg_dezigzag[k++];
                        data[doff + zig] = (short)((r >> 8) * (1 << shift));
                    }
                    else
                    {
                        int rs = stbi__jpeg_huff_decode(j, hac);
                        if (rs < 0) return stbi__err("bad huffman code", "Corrupt JPEG");
                        s = rs & 15;
                        r = rs >> 4;
                        if (s == 0)
                        {
                            if (r < 15)
                            {
                                j.eob_run = (1 << r);
                                if (r != 0)
                                    j.eob_run += stbi__jpeg_get_bits(j, r);
                                --j.eob_run;
                                break;
                            }
                            k += 16;
                        }
                        else
                        {
                            k += r;
                            zig = stbi__jpeg_dezigzag[k++];
                            data[doff + zig] = (short)(stbi__extend_receive(j, s) * (1 << shift));
                        }
                    }
                } while (k <= j.spec_end);
            }
            else
            {
                // refinement scan for these AC coefficients
                short bit = (short)(1 << j.succ_low);

                if (j.eob_run != 0)
                {
                    --j.eob_run;
                    for (k = j.spec_start; k <= j.spec_end; ++k)
                    {
                        int pi = doff + stbi__jpeg_dezigzag[k];
                        if (data[pi] != 0)
                            if (stbi__jpeg_get_bit(j) != 0)
                                if ((data[pi] & bit) == 0)
                                {
                                    if (data[pi] > 0)
                                        data[pi] += bit;
                                    else
                                        data[pi] -= bit;
                                }
                    }
                }
                else
                {
                    k = j.spec_start;
                    do
                    {
                        int r, s;
                        int rs = stbi__jpeg_huff_decode(j, hac);
                        if (rs < 0) return stbi__err("bad huffman code", "Corrupt JPEG");
                        s = rs & 15;
                        r = rs >> 4;
                        if (s == 0)
                        {
                            if (r < 15)
                            {
                                j.eob_run = (1 << r) - 1;
                                if (r != 0)
                                    j.eob_run += stbi__jpeg_get_bits(j, r);
                                r = 64; // force end of block
                            }
                            else
                            {
                                // r=15 s=0 should write 16 0s, so we just do a run of 15 0s and then write s (which is 0)
                            }
                        }
                        else
                        {
                            if (s != 1) return stbi__err("bad huffman code", "Corrupt JPEG");
                            // sign bit
                            if (stbi__jpeg_get_bit(j) != 0)
                                s = bit;
                            else
                                s = -bit;
                        }

                        // advance by r
                        while (k <= j.spec_end)
                        {
                            int pi = doff + stbi__jpeg_dezigzag[k++];
                            if (data[pi] != 0)
                            {
                                if (stbi__jpeg_get_bit(j) != 0)
                                    if ((data[pi] & bit) == 0)
                                    {
                                        if (data[pi] > 0)
                                            data[pi] += bit;
                                        else
                                            data[pi] -= bit;
                                    }
                            }
                            else
                            {
                                if (r == 0)
                                {
                                    data[pi] = (short)s;
                                    break;
                                }
                                --r;
                            }
                        }
                    } while (k <= j.spec_end);
                }
            }
            return 1;
        }

        // take a -128..127 value and stbi__clamp it and convert to 0..255
        private static byte stbi__clamp(int x)
        {
            // trick to use a single test to catch both cases
            if ((uint)x > 255)
            {
                if (x < 0) return 0;
                if (x > 255) return 255;
            }
            return (byte)x;
        }

        private static int stbi__f2f(double x) { return (int)(x * 4096 + 0.5); }
        private static int stbi__fsh(int x) { return x * 4096; }

        // derived from jidctint -- DCT_ISLOW
        private static void IDCT_1D(int s0, int s1, int s2, int s3, int s4, int s5, int s6, int s7,
                                    out int x0, out int x1, out int x2, out int x3,
                                    out int t0, out int t1, out int t2, out int t3)
        {
            int p1, p2, p3, p4, p5;
            p2 = s2;
            p3 = s6;
            p1 = (p2 + p3) * stbi__f2f(0.5411961f);
            t2 = p1 + p3 * stbi__f2f(-1.847759065f);
            t3 = p1 + p2 * stbi__f2f(0.765366865f);
            p2 = s0;
            p3 = s4;
            t0 = stbi__fsh(p2 + p3);
            t1 = stbi__fsh(p2 - p3);
            x0 = t0 + t3;
            x3 = t0 - t3;
            x1 = t1 + t2;
            x2 = t1 - t2;
            t0 = s7;
            t1 = s5;
            t2 = s3;
            t3 = s1;
            p3 = t0 + t2;
            p4 = t1 + t3;
            p1 = t0 + t3;
            p2 = t1 + t2;
            p5 = (p3 + p4) * stbi__f2f(1.175875602f);
            t0 = t0 * stbi__f2f(0.298631336f);
            t1 = t1 * stbi__f2f(2.053119869f);
            t2 = t2 * stbi__f2f(3.072711026f);
            t3 = t3 * stbi__f2f(1.501321110f);
            p1 = p5 + p1 * stbi__f2f(-0.899976223f);
            p2 = p5 + p2 * stbi__f2f(-2.562915447f);
            p3 = p3 * stbi__f2f(-1.961570560f);
            p4 = p4 * stbi__f2f(-0.390180644f);
            t3 += p1 + p4;
            t2 += p2 + p3;
            t1 += p2 + p4;
            t0 += p1 + p3;
        }

        private static void stbi__idct_block(byte[] outp, int ooff, int out_stride, short[] data, int doff)
        {
            int i;
            int[] val = new int[64];
            int x0, x1, x2, x3, t0, t1, t2, t3;

            // columns
            for (i = 0; i < 8; ++i)
            {
                int d = doff + i, v = i;
                // if all zeroes, shortcut -- this avoids dequantizing 0s and IDCTing
                if (data[d + 8] == 0 && data[d + 16] == 0 && data[d + 24] == 0 && data[d + 32] == 0
                    && data[d + 40] == 0 && data[d + 48] == 0 && data[d + 56] == 0)
                {
                    int dcterm = data[d] * 4;
                    val[v] = val[v + 8] = val[v + 16] = val[v + 24] = val[v + 32] = val[v + 40] = val[v + 48] = val[v + 56] = dcterm;
                }
                else
                {
                    IDCT_1D(data[d], data[d + 8], data[d + 16], data[d + 24], data[d + 32], data[d + 40], data[d + 48], data[d + 56],
                            out x0, out x1, out x2, out x3, out t0, out t1, out t2, out t3);
                    // constants scaled things up by 1<<12; let's bring them back down, but keep 2 extra bits of precision
                    x0 += 512; x1 += 512; x2 += 512; x3 += 512;
                    val[v] = (x0 + t3) >> 10;
                    val[v + 56] = (x0 - t3) >> 10;
                    val[v + 8] = (x1 + t2) >> 10;
                    val[v + 48] = (x1 - t2) >> 10;
                    val[v + 16] = (x2 + t1) >> 10;
                    val[v + 40] = (x2 - t1) >> 10;
                    val[v + 24] = (x3 + t0) >> 10;
                    val[v + 32] = (x3 - t0) >> 10;
                }
            }

            for (i = 0; i < 8; ++i)
            {
                int v = i * 8, o = ooff + i * out_stride;
                // no fast case since the first 1D IDCT spread components out
                IDCT_1D(val[v], val[v + 1], val[v + 2], val[v + 3], val[v + 4], val[v + 5], val[v + 6], val[v + 7],
                        out x0, out x1, out x2, out x3, out t0, out t1, out t2, out t3);
                // constants scaled things up by 1<<12, plus we had 1<<2 from first loop, plus horizontal and vertical
                // each scale by sqrt(8) so together we've got an extra 1<<3, so 1<<17 total we need to remove.
                x0 += 65536 + (128 << 17);
                x1 += 65536 + (128 << 17);
                x2 += 65536 + (128 << 17);
                x3 += 65536 + (128 << 17);
                outp[o + 0] = stbi__clamp((x0 + t3) >> 17);
                outp[o + 7] = stbi__clamp((x0 - t3) >> 17);
                outp[o + 1] = stbi__clamp((x1 + t2) >> 17);
                outp[o + 6] = stbi__clamp((x1 - t2) >> 17);
                outp[o + 2] = stbi__clamp((x2 + t1) >> 17);
                outp[o + 5] = stbi__clamp((x2 - t1) >> 17);
                outp[o + 3] = stbi__clamp((x3 + t0) >> 17);
                outp[o + 4] = stbi__clamp((x3 - t0) >> 17);
            }
        }

        // if there's a pending marker from the entropy stream, return that otherwise, fetch from the stream and get a marker.
        private static byte stbi__get_marker(stbi__jpeg j)
        {
            byte x;
            if (j.marker != STBI__MARKER_none) { x = j.marker; j.marker = STBI__MARKER_none; return x; }
            x = (byte)j.s.get8();
            if (x != 0xff) return STBI__MARKER_none;
            while (x == 0xff)
                x = (byte)j.s.get8(); // consume repeated 0xff fill bytes
            return x;
        }

        private static bool STBI__RESTART(int x) { return x >= 0xd0 && x <= 0xd7; }

        // after a restart interval, stbi__jpeg_reset the entropy decoder and the dc prediction
        private static void stbi__jpeg_reset(stbi__jpeg j)
        {
            j.code_bits = 0;
            j.code_buffer = 0;
            j.nomore = 0;
            j.img_comp[0].dc_pred = j.img_comp[1].dc_pred = j.img_comp[2].dc_pred = j.img_comp[3].dc_pred = 0;
            j.marker = STBI__MARKER_none;
            j.todo = j.restart_interval != 0 ? j.restart_interval : 0x7fffffff;
            j.eob_run = 0;
        }

        private static int stbi__parse_entropy_coded_data(stbi__jpeg z)
        {
            stbi__jpeg_reset(z);
            if (z.progressive == 0)
            {
                if (z.scan_n == 1)
                {
                    int i, j;
                    short[] data = new short[64];
                    int n = z.order[0];
                    // non-interleaved data, we just need to process one block at a time, in trivial scanline order
                    int w = (z.img_comp[n].x + 7) >> 3;
                    int h = (z.img_comp[n].y + 7) >> 3;
                    for (j = 0; j < h; ++j)
                    {
                        for (i = 0; i < w; ++i)
                        {
                            int ha = z.img_comp[n].ha;
                            if (stbi__jpeg_decode_block(z, data, 0, z.huff_dc[z.img_comp[n].hd], z.huff_ac[ha], z.fast_ac[ha], n, z.dequant[z.img_comp[n].tq]) == 0) return 0;
                            stbi__idct_block(z.img_comp[n].data, z.img_comp[n].w2 * j * 8 + i * 8, z.img_comp[n].w2, data, 0);
                            // every data block is an MCU, so countdown the restart interval
                            if (--z.todo <= 0)
                            {
                                if (z.code_bits < 24) stbi__grow_buffer_unsafe(z);
                                // if it's NOT a restart, then just bail, so we get corrupt data rather than no data
                                if (!STBI__RESTART(z.marker)) return 1;
                                stbi__jpeg_reset(z);
                            }
                        }
                    }
                    return 1;
                }
                else
                { // interleaved
                    int i, j, k, x, y;
                    short[] data = new short[64];
                    for (j = 0; j < z.img_mcu_y; ++j)
                    {
                        for (i = 0; i < z.img_mcu_x; ++i)
                        {
                            // scan an interleaved mcu... process scan_n components in order
                            for (k = 0; k < z.scan_n; ++k)
                            {
                                int n = z.order[k];
                                // scan out an mcu's worth of this component; that's just determined by the basic H and V specified for the component
                                for (y = 0; y < z.img_comp[n].v; ++y)
                                {
                                    for (x = 0; x < z.img_comp[n].h; ++x)
                                    {
                                        int x2 = (i * z.img_comp[n].h + x) * 8;
                                        int y2 = (j * z.img_comp[n].v + y) * 8;
                                        int ha = z.img_comp[n].ha;
                                        if (stbi__jpeg_decode_block(z, data, 0, z.huff_dc[z.img_comp[n].hd], z.huff_ac[ha], z.fast_ac[ha], n, z.dequant[z.img_comp[n].tq]) == 0) return 0;
                                        stbi__idct_block(z.img_comp[n].data, z.img_comp[n].w2 * y2 + x2, z.img_comp[n].w2, data, 0);
                                    }
                                }
                            }
                            // after all interleaved components, that's an interleaved MCU, so now count down the restart interval
                            if (--z.todo <= 0)
                            {
                                if (z.code_bits < 24) stbi__grow_buffer_unsafe(z);
                                if (!STBI__RESTART(z.marker)) return 1;
                                stbi__jpeg_reset(z);
                            }
                        }
                    }
                    return 1;
                }
            }
            else
            {
                if (z.scan_n == 1)
                {
                    int i, j;
                    int n = z.order[0];
                    int w = (z.img_comp[n].x + 7) >> 3;
                    int h = (z.img_comp[n].y + 7) >> 3;
                    for (j = 0; j < h; ++j)
                    {
                        for (i = 0; i < w; ++i)
                        {
                            short[] data = z.img_comp[n].coeff;
                            int doff = 64 * (i + j * z.img_comp[n].coeff_w);
                            if (z.spec_start == 0)
                            {
                                if (stbi__jpeg_decode_block_prog_dc(z, data, doff, z.huff_dc[z.img_comp[n].hd], n) == 0)
                                    return 0;
                            }
                            else
                            {
                                int ha = z.img_comp[n].ha;
                                if (stbi__jpeg_decode_block_prog_ac(z, data, doff, z.huff_ac[ha], z.fast_ac[ha]) == 0)
                                    return 0;
                            }
                            // every data block is an MCU, so countdown the restart interval
                            if (--z.todo <= 0)
                            {
                                if (z.code_bits < 24) stbi__grow_buffer_unsafe(z);
                                if (!STBI__RESTART(z.marker)) return 1;
                                stbi__jpeg_reset(z);
                            }
                        }
                    }
                    return 1;
                }
                else
                { // interleaved
                    int i, j, k, x, y;
                    for (j = 0; j < z.img_mcu_y; ++j)
                    {
                        for (i = 0; i < z.img_mcu_x; ++i)
                        {
                            // scan an interleaved mcu... process scan_n components in order
                            for (k = 0; k < z.scan_n; ++k)
                            {
                                int n = z.order[k];
                                for (y = 0; y < z.img_comp[n].v; ++y)
                                {
                                    for (x = 0; x < z.img_comp[n].h; ++x)
                                    {
                                        int x2 = (i * z.img_comp[n].h + x);
                                        int y2 = (j * z.img_comp[n].v + y);
                                        short[] data = z.img_comp[n].coeff;
                                        int doff = 64 * (x2 + y2 * z.img_comp[n].coeff_w);
                                        if (stbi__jpeg_decode_block_prog_dc(z, data, doff, z.huff_dc[z.img_comp[n].hd], n) == 0)
                                            return 0;
                                    }
                                }
                            }
                            // after all interleaved components, that's an interleaved MCU, so now count down the restart interval
                            if (--z.todo <= 0)
                            {
                                if (z.code_bits < 24) stbi__grow_buffer_unsafe(z);
                                if (!STBI__RESTART(z.marker)) return 1;
                                stbi__jpeg_reset(z);
                            }
                        }
                    }
                    return 1;
                }
            }
        }

        private static void stbi__jpeg_dequantize(short[] data, int doff, ushort[] dequant)
        {
            for (int i = 0; i < 64; ++i)
                data[doff + i] *= (short)dequant[i];
        }

        private static void stbi__jpeg_finish(stbi__jpeg z)
        {
            if (z.progressive != 0)
            {
                // dequantize and idct the data
                int i, j, n;
                for (n = 0; n < z.s.img_n; ++n)
                {
                    int w = (z.img_comp[n].x + 7) >> 3;
                    int h = (z.img_comp[n].y + 7) >> 3;
                    for (j = 0; j < h; ++j)
                    {
                        for (i = 0; i < w; ++i)
                        {
                            int doff = 64 * (i + j * z.img_comp[n].coeff_w);
                            stbi__jpeg_dequantize(z.img_comp[n].coeff, doff, z.dequant[z.img_comp[n].tq]);
                            stbi__idct_block(z.img_comp[n].data, z.img_comp[n].w2 * j * 8 + i * 8, z.img_comp[n].w2,
                                             z.img_comp[n].coeff, doff);
                        }
                    }
                }
            }
        }

        private static int stbi__process_marker(stbi__jpeg z, int m)
        {
            int L;
            switch (m)
            {
            case STBI__MARKER_none: // no marker found
                return stbi__err("expected marker", "Corrupt JPEG");

            case 0xDD: // DRI - specify restart interval
                if (z.s.get16be() != 4) return stbi__err("bad DRI len", "Corrupt JPEG");
                z.restart_interval = z.s.get16be();
                return 1;

            case 0xDB: // DQT - define quantization table
                L = z.s.get16be() - 2;
                while (L > 0)
                {
                    int q = z.s.get8();
                    int p = q >> 4, sixteen = (p != 0) ? 1 : 0;
                    int t = q & 15, i;
                    if (p != 0 && p != 1) return stbi__err("bad DQT type", "Corrupt JPEG");
                    if (t > 3) return stbi__err("bad DQT table", "Corrupt JPEG");

                    for (i = 0; i < 64; ++i)
                        z.dequant[t][stbi__jpeg_dezigzag[i]] = (ushort)(sixteen != 0 ? z.s.get16be() : z.s.get8());
                    L -= (sixteen != 0 ? 129 : 65);
                }
                return L == 0 ? 1 : 0;

            case 0xC4: // DHT - define huffman table
                L = z.s.get16be() - 2;
                while (L > 0)
                {
                    byte[] v;
                    int[] sizes = new int[16];
                    int i, n = 0;
                    int q = z.s.get8();
                    int tc = q >> 4;
                    int th = q & 15;
                    if (tc > 1 || th > 3) return stbi__err("bad DHT header", "Corrupt JPEG");
                    for (i = 0; i < 16; ++i)
                    {
                        sizes[i] = z.s.get8();
                        n += sizes[i];
                    }
                    L -= 17;
                    if (tc == 0)
                    {
                        if (stbi__build_huffman(z.huff_dc[th], sizes) == 0) return 0;
                        v = z.huff_dc[th].values;
                    }
                    else
                    {
                        if (stbi__build_huffman(z.huff_ac[th], sizes) == 0) return 0;
                        v = z.huff_ac[th].values;
                    }
                    for (i = 0; i < n; ++i)
                        v[i] = (byte)z.s.get8();
                    if (tc != 0)
                        stbi__build_fast_ac(z.fast_ac[th], z.huff_ac[th]);
                    L -= n;
                }
                return L == 0 ? 1 : 0;
            }

            // check for comment block or APP blocks
            if ((m >= 0xE0 && m <= 0xEF) || m == 0xFE)
            {
                L = z.s.get16be();
                if (L < 2)
                {
                    if (m == 0xFE)
                        return stbi__err("bad COM len", "Corrupt JPEG");
                    else
                        return stbi__err("bad APP len", "Corrupt JPEG");
                }
                L -= 2;

                if (m == 0xE0 && L >= 5)
                { // JFIF APP0 segment
                    byte[] tag = { (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0 };
                    int ok = 1;
                    int i;
                    for (i = 0; i < 5; ++i)
                        if (z.s.get8() != tag[i])
                            ok = 0;
                    L -= 5;
                    if (ok != 0)
                        z.jfif = 1;
                }
                else if (m == 0xEE && L >= 12)
                { // Adobe APP14 segment
                    byte[] tag = { (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e', 0 };
                    int ok = 1;
                    int i;
                    for (i = 0; i < 6; ++i)
                        if (z.s.get8() != tag[i])
                            ok = 0;
                    L -= 6;
                    if (ok != 0)
                    {
                        z.s.get8(); // version
                        z.s.get16be(); // flags0
                        z.s.get16be(); // flags1
                        z.app14_color_transform = z.s.get8(); // color transform
                        L -= 6;
                    }
                }

                z.s.skip(L);
                return 1;
            }

            return stbi__err("unknown marker", "Corrupt JPEG");
        }

        // after we see SOS
        private static int stbi__process_scan_header(stbi__jpeg z)
        {
            int i;
            int Ls = z.s.get16be();
            z.scan_n = z.s.get8();
            if (z.scan_n < 1 || z.scan_n > 4 || z.scan_n > z.s.img_n) return stbi__err("bad SOS component count", "Corrupt JPEG");
            if (Ls != 6 + 2 * z.scan_n) return stbi__err("bad SOS len", "Corrupt JPEG");
            for (i = 0; i < z.scan_n; ++i)
            {
                int id = z.s.get8(), which;
                int q = z.s.get8();
                for (which = 0; which < z.s.img_n; ++which)
                    if (z.img_comp[which].id == id)
                        break;
                if (which == z.s.img_n) return 0; // no match
                z.img_comp[which].hd = q >> 4; if (z.img_comp[which].hd > 3) return stbi__err("bad DC huff", "Corrupt JPEG");
                z.img_comp[which].ha = q & 15; if (z.img_comp[which].ha > 3) return stbi__err("bad AC huff", "Corrupt JPEG");
                z.order[i] = which;
            }

            {
                int aa;
                z.spec_start = z.s.get8();
                z.spec_end = z.s.get8(); // should be 63, but might be 0
                aa = z.s.get8();
                z.succ_high = (aa >> 4);
                z.succ_low = (aa & 15);
                if (z.progressive != 0)
                {
                    if (z.spec_start > 63 || z.spec_end > 63 || z.spec_start > z.spec_end || z.succ_high > 13 || z.succ_low > 13)
                        return stbi__err("bad SOS", "Corrupt JPEG");
                }
                else
                {
                    if (z.spec_start != 0) return stbi__err("bad SOS", "Corrupt JPEG");
                    if (z.succ_high != 0 || z.succ_low != 0) return stbi__err("bad SOS", "Corrupt JPEG");
                    z.spec_end = 63;
                }
            }

            return 1;
        }

        private static int stbi__free_jpeg_components(stbi__jpeg z, int ncomp, int why)
        {
            for (int i = 0; i < ncomp; ++i)
            {
                z.img_comp[i].data = null;
                z.img_comp[i].coeff = null;
                z.img_comp[i].linebuf = null;
            }
            return why;
        }

        private const int STBI_MAX_DIMENSIONS = 1 << 24;

        private static int stbi__process_frame_header(stbi__jpeg z, int scan)
        {
            StbiContext s = z.s;
            int Lf, p, i, q, h_max = 1, v_max = 1, c;
            Lf = s.get16be(); if (Lf < 11) return stbi__err("bad SOF len", "Corrupt JPEG"); // JPEG
            p = s.get8(); if (p != 8) return stbi__err("only 8-bit", "JPEG format not supported: 8-bit only"); // JPEG baseline
            s.img_y = s.get16be(); if (s.img_y == 0) return stbi__err("no header height", "JPEG format not supported: delayed height");
            s.img_x = s.get16be(); if (s.img_x == 0) return stbi__err("0 width", "Corrupt JPEG"); // JPEG requires
            if (s.img_y > STBI_MAX_DIMENSIONS) return stbi__err("too large", "Very large image (corrupt?)");
            if (s.img_x > STBI_MAX_DIMENSIONS) return stbi__err("too large", "Very large image (corrupt?)");
            c = s.get8();
            if (c != 3 && c != 1 && c != 4) return stbi__err("bad component count", "Corrupt JPEG");
            s.img_n = c;
            for (i = 0; i < c; ++i)
            {
                z.img_comp[i].data = null;
                z.img_comp[i].linebuf = null;
            }

            if (Lf != 8 + 3 * s.img_n) return stbi__err("bad SOF len", "Corrupt JPEG");

            z.rgb = 0;
            byte[] rgb = { (byte)'R', (byte)'G', (byte)'B' };
            for (i = 0; i < s.img_n; ++i)
            {
                z.img_comp[i].id = s.get8();
                if (s.img_n == 3 && z.img_comp[i].id == rgb[i])
                    ++z.rgb;
                q = s.get8();
                z.img_comp[i].h = (q >> 4); if (z.img_comp[i].h == 0 || z.img_comp[i].h > 4) return stbi__err("bad H", "Corrupt JPEG");
                z.img_comp[i].v = q & 15; if (z.img_comp[i].v == 0 || z.img_comp[i].v > 4) return stbi__err("bad V", "Corrupt JPEG");
                z.img_comp[i].tq = s.get8(); if (z.img_comp[i].tq > 3) return stbi__err("bad TQ", "Corrupt JPEG");
            }

            if (scan != STBI__SCAN_load) return 1;

            if ((long)s.img_x * s.img_y * s.img_n > int.MaxValue) return stbi__err("too large", "Image too large to decode");

            for (i = 0; i < s.img_n; ++i)
            {
                if (z.img_comp[i].h > h_max) h_max = z.img_comp[i].h;
                if (z.img_comp[i].v > v_max) v_max = z.img_comp[i].v;
            }

            // check that plane subsampling factors are integer ratios; our resamplers can't deal with fractional ratios
            for (i = 0; i < s.img_n; ++i)
            {
                if (h_max % z.img_comp[i].h != 0) return stbi__err("bad H", "Corrupt JPEG");
                if (v_max % z.img_comp[i].v != 0) return stbi__err("bad V", "Corrupt JPEG");
            }

            // compute interleaved mcu info
            z.img_h_max = h_max;
            z.img_v_max = v_max;
            z.img_mcu_w = h_max * 8;
            z.img_mcu_h = v_max * 8;
            // these sizes can't be more than 17 bits
            z.img_mcu_x = (s.img_x + z.img_mcu_w - 1) / z.img_mcu_w;
            z.img_mcu_y = (s.img_y + z.img_mcu_h - 1) / z.img_mcu_h;

            for (i = 0; i < s.img_n; ++i)
            {
                // number of effective pixels (e.g. for non-interleaved MCU)
                z.img_comp[i].x = (s.img_x * z.img_comp[i].h + h_max - 1) / h_max;
                z.img_comp[i].y = (s.img_y * z.img_comp[i].v + v_max - 1) / v_max;
                // allocate enough memory to decode the bogus oversized data from using interleaved MCUs and their big blocks
                z.img_comp[i].w2 = z.img_mcu_x * z.img_comp[i].h * 8;
                z.img_comp[i].h2 = z.img_mcu_y * z.img_comp[i].v * 8;
                z.img_comp[i].coeff = null;
                z.img_comp[i].linebuf = null;
                z.img_comp[i].data = new byte[(long)z.img_comp[i].w2 * z.img_comp[i].h2];
                if (z.progressive != 0)
                {
                    // w2, h2 are multiples of 8 (see above)
                    z.img_comp[i].coeff_w = z.img_comp[i].w2 / 8;
                    z.img_comp[i].coeff_h = z.img_comp[i].h2 / 8;
                    z.img_comp[i].coeff = new short[(long)z.img_comp[i].w2 * z.img_comp[i].h2];
                }
            }

            return 1;
        }

        private static int stbi__decode_jpeg_header(stbi__jpeg z, int scan)
        {
            int m;
            z.jfif = 0;
            z.app14_color_transform = -1; // valid values are 0,1,2
            z.marker = STBI__MARKER_none; // initialize cached marker to empty
            m = stbi__get_marker(z);
            if (!(m == 0xd8)) return stbi__err("no SOI", "Corrupt JPEG");
            if (scan == STBI__SCAN_type) return 1;
            m = stbi__get_marker(z);
            while (!(m == 0xc0 || m == 0xc1 || m == 0xc2))
            {
                if (stbi__process_marker(z, m) == 0) return 0;
                m = stbi__get_marker(z);
                while (m == STBI__MARKER_none)
                {
                    // some files have extra padding after their blocks, so ok, we'll scan
                    if (z.s.at_eof()) return stbi__err("no SOF", "Corrupt JPEG");
                    m = stbi__get_marker(z);
                }
            }
            z.progressive = (m == 0xc2) ? 1 : 0;
            if (stbi__process_frame_header(z, scan) == 0) return 0;
            return 1;
        }

        // decode image to YCbCr format
        private static int stbi__decode_jpeg_image(stbi__jpeg j)
        {
            int m;
            for (m = 0; m < 4; m++)
            {
                j.img_comp[m].data = null;
                j.img_comp[m].coeff = null;
            }
            j.restart_interval = 0;
            if (stbi__decode_jpeg_header(j, STBI__SCAN_load) == 0) return 0;
            m = stbi__get_marker(j);
            while (!(m == 0xd9))
            {
                if (m == 0xda)
                {
                    if (stbi__process_scan_header(j) == 0) return 0;
                    if (stbi__parse_entropy_coded_data(j) == 0) return 0;
                    if (j.marker == STBI__MARKER_none)
                    {
                        // handle 0s at the end of image data from IP Kamera 9060
                        while (!j.s.at_eof())
                        {
                            int x = j.s.get8();
                            if (x == 255)
                            {
                                j.marker = (byte)j.s.get8();
                                break;
                            }
                        }
                    }
                }
                else if (m == 0xdc)
                {
                    int Ld = j.s.get16be();
                    uint NL = (uint)j.s.get16be();
                    if (Ld != 4) return stbi__err("bad DNL len", "Corrupt JPEG");
                    if (NL != j.s.img_y) return stbi__err("bad DNL height", "Corrupt JPEG");
                }
                else
                {
                    if (stbi__process_marker(j, m) == 0) return 0;
                }
                m = stbi__get_marker(j);
            }
            if (j.progressive != 0)
                stbi__jpeg_finish(j);
            return 1;
        }

        // static jfif-centered resampling (across block boundaries)
        // Each resampler returns the array holding the row and sets "off" to the row's start offset.
        private delegate byte[] resample_row_func(byte[] outp, byte[] in_near, int near_off, byte[] in_far, int far_off,
                                                  int w, int hs, out int off);

        private static byte stbi__div4(int x) { return (byte)(x >> 2); }
        private static byte stbi__div16(int x) { return (byte)(x >> 4); }

        private static byte[] resample_row_1(byte[] outp, byte[] in_near, int near_off, byte[] in_far, int far_off, int w, int hs, out int off)
        {
            off = near_off;
            return in_near;
        }

        private static byte[] stbi__resample_row_v_2(byte[] outp, byte[] in_near, int nOff, byte[] in_far, int fOff, int w, int hs, out int off)
        {
            // need to generate two samples vertically for every one in input
            for (int i = 0; i < w; ++i)
                outp[i] = stbi__div4(3 * in_near[nOff + i] + in_far[fOff + i] + 2);
            off = 0;
            return outp;
        }

        private static byte[] stbi__resample_row_h_2(byte[] outp, byte[] in_near, int nOff, byte[] in_far, int fOff, int w, int hs, out int off)
        {
            // need to generate two samples horizontally for every one in input
            int i;
            off = 0;
            if (w == 1)
            {
                // if only one sample, can't do any interpolation
                outp[0] = outp[1] = in_near[nOff];
                return outp;
            }

            outp[0] = in_near[nOff];
            outp[1] = stbi__div4(in_near[nOff] * 3 + in_near[nOff + 1] + 2);
            for (i = 1; i < w - 1; ++i)
            {
                int n = 3 * in_near[nOff + i] + 2;
                outp[i * 2 + 0] = stbi__div4(n + in_near[nOff + i - 1]);
                outp[i * 2 + 1] = stbi__div4(n + in_near[nOff + i + 1]);
            }
            outp[i * 2 + 0] = stbi__div4(in_near[nOff + w - 2] * 3 + in_near[nOff + w - 1] + 2);
            outp[i * 2 + 1] = in_near[nOff + w - 1];
            return outp;
        }

        private static byte[] stbi__resample_row_hv_2(byte[] outp, byte[] in_near, int nOff, byte[] in_far, int fOff, int w, int hs, out int off)
        {
            // need to generate 2x2 samples for every one in input
            int i, t0, t1;
            off = 0;
            if (w == 1)
            {
                outp[0] = outp[1] = stbi__div4(3 * in_near[nOff] + in_far[fOff] + 2);
                return outp;
            }

            t1 = 3 * in_near[nOff] + in_far[fOff];
            outp[0] = stbi__div4(t1 + 2);
            for (i = 1; i < w; ++i)
            {
                t0 = t1;
                t1 = 3 * in_near[nOff + i] + in_far[fOff + i];
                outp[i * 2 - 1] = stbi__div16(3 * t0 + t1 + 8);
                outp[i * 2] = stbi__div16(3 * t1 + t0 + 8);
            }
            outp[w * 2 - 1] = stbi__div4(t1 + 2);
            return outp;
        }

        private static byte[] stbi__resample_row_generic(byte[] outp, byte[] in_near, int nOff, byte[] in_far, int fOff, int w, int hs, out int off)
        {
            // resample with nearest-neighbor
            off = 0;
            for (int i = 0; i < w; ++i)
                for (int j = 0; j < hs; ++j)
                    outp[i * hs + j] = in_near[nOff + i];
            return outp;
        }

        // this is a reduced-precision calculation of YCbCr-to-RGB introduced to make sure the code produces the same
        // results in both SIMD and scalar
        private static int stbi__float2fixed(float x) { return ((int)(x * 4096.0f + 0.5f)) << 8; }

        private static void stbi__YCbCr_to_RGB_row(byte[] outp, int oOff, byte[] y, int yOff, byte[] pcb, int cbOff,
                                                   byte[] pcr, int crOff, int count, int step)
        {
            for (int i = 0; i < count; ++i)
            {
                int y_fixed = (y[yOff + i] << 20) + (1 << 19); // rounding
                int r, g, b;
                int cr = pcr[crOff + i] - 128;
                int cb = pcb[cbOff + i] - 128;
                r = y_fixed + cr * stbi__float2fixed(1.40200f);
                g = y_fixed + (cr * -stbi__float2fixed(0.71414f)) + ((cb * -stbi__float2fixed(0.34414f)) & unchecked((int)0xffff0000));
                b = y_fixed + cb * stbi__float2fixed(1.77200f);
                r >>= 20;
                g >>= 20;
                b >>= 20;
                if ((uint)r > 255) { if (r < 0) r = 0; else r = 255; }
                if ((uint)g > 255) { if (g < 0) g = 0; else g = 255; }
                if ((uint)b > 255) { if (b < 0) b = 0; else b = 255; }
                outp[oOff + 0] = (byte)r;
                outp[oOff + 1] = (byte)g;
                outp[oOff + 2] = (byte)b;
                outp[oOff + 3] = 255;
                oOff += step;
            }
        }

        private sealed class stbi__resample
        {
            public resample_row_func resample;
            public byte[] line0, line1;
            public int line0Off, line1Off;
            public int hs, vs;   // expansion factor in each axis
            public int w_lores;  // horizontal pixels pre-expansion
            public int ystep;    // how far through vertical expansion we are
            public int ypos;     // which pre-expansion row we're on
        }

        // fast 0..255 * 0..255 => 0..255 rounded multiplication
        private static byte stbi__blinn_8x8(byte x, byte y)
        {
            uint t = (uint)(x * y + 128);
            return (byte)((t + (t >> 8)) >> 8);
        }

        private static byte[] load_jpeg_image(stbi__jpeg z, out int out_x, out int out_y, out int comp, int req_comp)
        {
            int n, decode_n, is_rgb;
            out_x = out_y = comp = 0;
            z.s.img_n = 0; // make stbi__cleanup_jpeg safe

            // validate req_comp
            if (req_comp < 0 || req_comp > 4) return stbi__errpuc("bad req_comp", "Internal error");

            // load a jpeg image from whichever source, but leave in YCbCr format
            if (stbi__decode_jpeg_image(z) == 0) return null;

            // determine actual number of components to generate
            n = req_comp != 0 ? req_comp : z.s.img_n >= 3 ? 3 : 1;

            is_rgb = (z.s.img_n == 3 && (z.rgb == 3 || (z.app14_color_transform == 0 && z.jfif == 0))) ? 1 : 0;

            if (z.s.img_n == 3 && n < 3 && is_rgb == 0)
                decode_n = 1;
            else
                decode_n = z.s.img_n;

            // nothing to do if no components requested
            if (decode_n <= 0) return null;

            // resample and color-convert
            {
                int k;
                int i, j;
                byte[] output;
                byte[][] coutput = new byte[4][];
                int[] cOff = new int[4];

                stbi__resample[] res_comp = new stbi__resample[4];

                for (k = 0; k < decode_n; ++k)
                {
                    stbi__resample r = res_comp[k] = new stbi__resample();

                    // allocate line buffer big enough for upsampling off the edges with upsample factor of 4
                    z.img_comp[k].linebuf = new byte[z.s.img_x + 3];

                    r.hs = z.img_h_max / z.img_comp[k].h;
                    r.vs = z.img_v_max / z.img_comp[k].v;
                    r.ystep = r.vs >> 1;
                    r.w_lores = (z.s.img_x + r.hs - 1) / r.hs;
                    r.ypos = 0;
                    r.line0 = r.line1 = z.img_comp[k].data;
                    r.line0Off = r.line1Off = 0;

                    if (r.hs == 1 && r.vs == 1) r.resample = resample_row_1;
                    else if (r.hs == 1 && r.vs == 2) r.resample = stbi__resample_row_v_2;
                    else if (r.hs == 2 && r.vs == 1) r.resample = stbi__resample_row_h_2;
                    else if (r.hs == 2 && r.vs == 2) r.resample = stbi__resample_row_hv_2;
                    else r.resample = stbi__resample_row_generic;
                }

                output = new byte[(long)n * z.s.img_x * z.s.img_y + 1];

                // now go ahead and resample
                for (j = 0; j < z.s.img_y; ++j)
                {
                    int outOff = n * z.s.img_x * j;
                    for (k = 0; k < decode_n; ++k)
                    {
                        stbi__resample r = res_comp[k];
                        bool y_bot = r.ystep >= (r.vs >> 1);
                        int so;
                        coutput[k] = r.resample(z.img_comp[k].linebuf,
                                                y_bot ? r.line1 : r.line0, y_bot ? r.line1Off : r.line0Off,
                                                y_bot ? r.line0 : r.line1, y_bot ? r.line0Off : r.line1Off,
                                                r.w_lores, r.hs, out so);
                        cOff[k] = so;
                        if (++r.ystep >= r.vs)
                        {
                            r.ystep = 0;
                            r.line0 = r.line1;
                            r.line0Off = r.line1Off;
                            if (++r.ypos < z.img_comp[k].y)
                                r.line1Off += z.img_comp[k].w2;
                        }
                    }
                    if (n >= 3)
                    {
                        byte[] y = coutput[0];
                        int yo = cOff[0];
                        if (z.s.img_n == 3)
                        {
                            if (is_rgb != 0)
                            {
                                for (i = 0; i < z.s.img_x; ++i)
                                {
                                    output[outOff + 0] = y[yo + i];
                                    output[outOff + 1] = coutput[1][cOff[1] + i];
                                    output[outOff + 2] = coutput[2][cOff[2] + i];
                                    output[outOff + 3] = 255;
                                    outOff += n;
                                }
                            }
                            else
                            {
                                stbi__YCbCr_to_RGB_row(output, outOff, y, yo, coutput[1], cOff[1], coutput[2], cOff[2], z.s.img_x, n);
                            }
                        }
                        else if (z.s.img_n == 4)
                        {
                            if (z.app14_color_transform == 0)
                            { // CMYK
                                for (i = 0; i < z.s.img_x; ++i)
                                {
                                    byte m = coutput[3][cOff[3] + i];
                                    output[outOff + 0] = stbi__blinn_8x8(coutput[0][cOff[0] + i], m);
                                    output[outOff + 1] = stbi__blinn_8x8(coutput[1][cOff[1] + i], m);
                                    output[outOff + 2] = stbi__blinn_8x8(coutput[2][cOff[2] + i], m);
                                    output[outOff + 3] = 255;
                                    outOff += n;
                                }
                            }
                            else if (z.app14_color_transform == 2)
                            { // YCCK
                                stbi__YCbCr_to_RGB_row(output, outOff, y, yo, coutput[1], cOff[1], coutput[2], cOff[2], z.s.img_x, n);
                                for (i = 0; i < z.s.img_x; ++i)
                                {
                                    byte m = coutput[3][cOff[3] + i];
                                    output[outOff + 0] = stbi__blinn_8x8((byte)(255 - output[outOff + 0]), m);
                                    output[outOff + 1] = stbi__blinn_8x8((byte)(255 - output[outOff + 1]), m);
                                    output[outOff + 2] = stbi__blinn_8x8((byte)(255 - output[outOff + 2]), m);
                                    outOff += n;
                                }
                            }
                            else
                            { // YCbCr + alpha?  Ignore the fourth channel for now
                                stbi__YCbCr_to_RGB_row(output, outOff, y, yo, coutput[1], cOff[1], coutput[2], cOff[2], z.s.img_x, n);
                            }
                        }
                        else
                            for (i = 0; i < z.s.img_x; ++i)
                            {
                                output[outOff + 0] = output[outOff + 1] = output[outOff + 2] = y[yo + i];
                                output[outOff + 3] = 255; // not used if n==3
                                outOff += n;
                            }
                    }
                    else
                    {
                        if (is_rgb != 0)
                        {
                            if (n == 1)
                                for (i = 0; i < z.s.img_x; ++i)
                                    output[outOff++] = stbi__compute_y(coutput[0][cOff[0] + i], coutput[1][cOff[1] + i], coutput[2][cOff[2] + i]);
                            else
                            {
                                for (i = 0; i < z.s.img_x; ++i, outOff += 2)
                                {
                                    output[outOff + 0] = stbi__compute_y(coutput[0][cOff[0] + i], coutput[1][cOff[1] + i], coutput[2][cOff[2] + i]);
                                    output[outOff + 1] = 255;
                                }
                            }
                        }
                        else if (z.s.img_n == 4 && z.app14_color_transform == 0)
                        {
                            for (i = 0; i < z.s.img_x; ++i)
                            {
                                byte m = coutput[3][cOff[3] + i];
                                byte r = stbi__blinn_8x8(coutput[0][cOff[0] + i], m);
                                byte g = stbi__blinn_8x8(coutput[1][cOff[1] + i], m);
                                byte b = stbi__blinn_8x8(coutput[2][cOff[2] + i], m);
                                output[outOff + 0] = stbi__compute_y(r, g, b);
                                output[outOff + 1] = 255;
                                outOff += n;
                            }
                        }
                        else if (z.s.img_n == 4 && z.app14_color_transform == 2)
                        {
                            for (i = 0; i < z.s.img_x; ++i)
                            {
                                output[outOff + 0] = stbi__blinn_8x8((byte)(255 - coutput[0][cOff[0] + i]), coutput[3][cOff[3] + i]);
                                output[outOff + 1] = 255;
                                outOff += n;
                            }
                        }
                        else
                        {
                            byte[] y = coutput[0];
                            int yo = cOff[0];
                            if (n == 1)
                                for (i = 0; i < z.s.img_x; ++i) output[outOff + i] = y[yo + i];
                            else
                                for (i = 0; i < z.s.img_x; ++i) { output[outOff++] = y[yo + i]; output[outOff++] = 255; }
                        }
                    }
                }
                out_x = z.s.img_x;
                out_y = z.s.img_y;
                comp = z.s.img_n >= 3 ? 3 : 1; // report original components, not output
                // (the buffer carries one spare byte, as in stb)
                byte[] exact = new byte[(long)n * out_x * out_y];
                Array.Copy(output, exact, exact.Length);
                return exact;
            }
        }

        private static byte[] stbi__jpeg_load(StbiContext s, out int x, out int y, out int comp, int req_comp)
        {
            stbi__jpeg j = new stbi__jpeg(s);
            return load_jpeg_image(j, out x, out y, out comp, req_comp);
        }

        private static bool stbi__jpeg_test(StbiContext s)
        {
            stbi__jpeg j = new stbi__jpeg(s);
            int r = stbi__decode_jpeg_header(j, STBI__SCAN_type);
            s.pos = 0;
            return r != 0;
        }

        // ------------------------------------------------------------------------------------------
        // PSD (8 bits per channel output), port of stbi__psd_load
        private static bool stbi__psd_test(StbiContext s)
        {
            bool r = s.get32be() == 0x38425053;
            s.pos = 0;
            return r;
        }

        private static int stbi__psd_decode_rle(StbiContext s, byte[] p, int pOff, int pixelCount)
        {
            int count, nleft, len;

            count = 0;
            while ((nleft = pixelCount - count) > 0)
            {
                len = s.get8();
                if (len == 128)
                {
                    // No-op.
                }
                else if (len < 128)
                {
                    // Copy next len+1 bytes literally.
                    len++;
                    if (len > nleft) return 0; // corrupt data
                    count += len;
                    while (len != 0)
                    {
                        p[pOff] = (byte)s.get8();
                        pOff += 4;
                        len--;
                    }
                }
                else if (len > 128)
                {
                    byte val;
                    // Next -len+1 bytes in the dest are replicated from next source byte.
                    len = 257 - len;
                    if (len > nleft) return 0; // corrupt data
                    val = (byte)s.get8();
                    count += len;
                    while (len != 0)
                    {
                        p[pOff] = val;
                        pOff += 4;
                        len--;
                    }
                }
            }

            return 1;
        }

        private static byte[] stbi__psd_load(StbiContext s, out int x, out int y, out int comp, int req_comp)
        {
            int pixelCount;
            int channelCount, compression;
            int channel, i;
            int bitdepth;
            int w, h;
            byte[] outp;
            x = y = comp = 0;

            // Check identifier
            if (s.get32be() != 0x38425053)   // "8BPS"
                return stbi__errpuc("not PSD", "Corrupt PSD image");

            // Check file type version.
            if (s.get16be() != 1)
                return stbi__errpuc("wrong version", "Unsupported version of PSD image");

            // Skip 6 reserved bytes.
            s.skip(6);

            // Read the number of channels (R, G, B, A, etc).
            channelCount = s.get16be();
            if (channelCount < 0 || channelCount > 16)
                return stbi__errpuc("wrong channel count", "Unsupported number of channels in PSD image");

            // Read the rows and columns of the image.
            h = (int)s.get32be();
            w = (int)s.get32be();

            if (h > STBI_MAX_DIMENSIONS) return stbi__errpuc("too large", "Very large image (corrupt?)");
            if (w > STBI_MAX_DIMENSIONS) return stbi__errpuc("too large", "Very large image (corrupt?)");

            // Make sure the depth is 8 bits.
            bitdepth = s.get16be();
            if (bitdepth != 8 && bitdepth != 16)
                return stbi__errpuc("unsupported bit depth", "PSD bit depth is not 8 or 16 bit");

            // Make sure the color mode is RGB.
            if (s.get16be() != 3)
                return stbi__errpuc("wrong color format", "PSD is not in RGB color format");

            // Skip the Mode Data.  (It's the palette for indexed color; other info for other modes.)
            s.skip((int)s.get32be());

            // Skip the image resources.  (resolution, pen tool paths, etc)
            s.skip((int)s.get32be());

            // Skip the reserved data.
            s.skip((int)s.get32be());

            // Find out if the data is compressed.
            compression = s.get16be();
            if (compression > 1)
                return stbi__errpuc("bad compression", "PSD has an unknown compression format");

            // Check size
            if ((long)4 * w * h > int.MaxValue)
                return stbi__errpuc("too large", "Corrupt PSD");

            // Create the destination image.
            outp = new byte[4 * w * h];
            pixelCount = w * h;

            // Finally, the image data.
            if (compression != 0)
            {
                // The RLE-compressed data is preceded by a 2-byte data count for each row in the data, which we're going to just skip.
                s.skip(h * channelCount * 2);

                // Read the RLE data by channel.
                for (channel = 0; channel < 4; channel++)
                {
                    int p = channel;
                    if (channel >= channelCount)
                    {
                        // Fill this channel with default data.
                        for (i = 0; i < pixelCount; i++, p += 4)
                            outp[p] = (byte)(channel == 3 ? 255 : 0);
                    }
                    else
                    {
                        // Read the RLE data.
                        if (stbi__psd_decode_rle(s, outp, p, pixelCount) == 0)
                            return stbi__errpuc("corrupt", "bad RLE data");
                    }
                }
            }
            else
            {
                // We're at the raw image data.  It's each channel in order (Red, Green, Blue, Alpha, ...)
                for (channel = 0; channel < 4; channel++)
                {
                    if (channel >= channelCount)
                    {
                        // Fill this channel with default data.
                        int p = channel;
                        byte val = (byte)(channel == 3 ? 255 : 0);
                        for (i = 0; i < pixelCount; i++, p += 4)
                            outp[p] = val;
                    }
                    else
                    {
                        int p = channel;
                        if (bitdepth == 16)
                        {  // input bpc
                            for (i = 0; i < pixelCount; i++, p += 4)
                                outp[p] = (byte)(s.get16be() >> 8);
                        }
                        else
                        {
                            for (i = 0; i < pixelCount; i++, p += 4)
                                outp[p] = (byte)s.get8();
                        }
                    }
                }
            }

            // remove weird white matte from PSD
            if (channelCount >= 4)
            {
                for (i = 0; i < w * h; ++i)
                {
                    int pixel = 4 * i;
                    if (outp[pixel + 3] != 0 && outp[pixel + 3] != 255)
                    {
                        float a = outp[pixel + 3] / 255.0f;
                        float ra = 1.0f / a;
                        float inv_a = 255.0f * (1 - ra);
                        outp[pixel + 0] = (byte)(outp[pixel + 0] * ra + inv_a);
                        outp[pixel + 1] = (byte)(outp[pixel + 1] * ra + inv_a);
                        outp[pixel + 2] = (byte)(outp[pixel + 2] * ra + inv_a);
                    }
                }
            }

            // convert to desired output format
            if (req_comp != 0 && req_comp != 4)
                outp = stbi__convert_format(outp, 4, req_comp, w, h);

            comp = 4;
            y = h;
            x = w;
            return outp;
        }

        // convert source image with img_n components to one with req_comp components (stbi__convert_format)
        private static byte[] stbi__convert_format(byte[] data, int img_n, int req_comp, int x, int y)
        {
            if (req_comp == img_n) return data;
            byte[] good = new byte[(long)req_comp * x * y];
            int src = 0, dest = 0;
            for (int j = 0; j < y; ++j)
            {
                for (int i = x - 1; i >= 0; --i, src += img_n, dest += req_comp)
                {
                    switch (img_n * 8 + req_comp)
                    {
                    case 1 * 8 + 2: good[dest] = data[src]; good[dest + 1] = 255; break;
                    case 1 * 8 + 3: good[dest] = good[dest + 1] = good[dest + 2] = data[src]; break;
                    case 1 * 8 + 4: good[dest] = good[dest + 1] = good[dest + 2] = data[src]; good[dest + 3] = 255; break;
                    case 2 * 8 + 1: good[dest] = data[src]; break;
                    case 2 * 8 + 3: good[dest] = good[dest + 1] = good[dest + 2] = data[src]; break;
                    case 2 * 8 + 4: good[dest] = good[dest + 1] = good[dest + 2] = data[src]; good[dest + 3] = data[src + 1]; break;
                    case 3 * 8 + 4: good[dest] = data[src]; good[dest + 1] = data[src + 1]; good[dest + 2] = data[src + 2]; good[dest + 3] = 255; break;
                    case 3 * 8 + 1: good[dest] = stbi__compute_y(data[src], data[src + 1], data[src + 2]); break;
                    case 3 * 8 + 2: good[dest] = stbi__compute_y(data[src], data[src + 1], data[src + 2]); good[dest + 1] = 255; break;
                    case 4 * 8 + 1: good[dest] = stbi__compute_y(data[src], data[src + 1], data[src + 2]); break;
                    case 4 * 8 + 2: good[dest] = stbi__compute_y(data[src], data[src + 1], data[src + 2]); good[dest + 1] = data[src + 3]; break;
                    case 4 * 8 + 3: good[dest] = data[src]; good[dest + 1] = data[src + 1]; good[dest + 2] = data[src + 2]; break;
                    default: return stbi__errpuc("unsupported", "Unsupported format conversion");
                    }
                }
            }
            return good;
        }

        // ------------------------------------------------------------------------------------------
        // Softimage PIC loader by Tom Seddon
        private static bool stbi__pic_is4(StbiContext s, string str)
        {
            for (int i = 0; i < 4; ++i)
                if (s.get8() != (byte)str[i])
                    return false;
            return true;
        }

        private const bool STBI_NO_PIC = true;
        private static bool stbi__pic_test_core(StbiContext s)
        {
            if (!stbi__pic_is4(s, "\x53\x80\xF6\x34"))
                return false;

            for (int i = 0; i < 84; ++i)
                s.get8();

            if (!stbi__pic_is4(s, "PICT"))
                return false;

            return true;
        }

        private static bool stbi__pic_test(StbiContext s)
        {
            bool r = stbi__pic_test_core(s);
            s.pos = 0;
            return r;
        }

        private struct stbi__pic_packet { public byte size, type, channel; }

        private static bool stbi__readval(StbiContext s, int channel, byte[] dest, int dOff)
        {
            int mask = 0x80;
            for (int i = 0; i < 4; ++i, mask >>= 1)
            {
                if ((channel & mask) != 0)
                {
                    if (s.at_eof()) { stbi__errpuc("bad file", "PIC file too short"); return false; }
                    dest[dOff + i] = (byte)s.get8();
                }
            }
            return true;
        }

        private static void stbi__copyval(int channel, byte[] dest, int dOff, byte[] src, int sOff)
        {
            int mask = 0x80;
            for (int i = 0; i < 4; ++i, mask >>= 1)
                if ((channel & mask) != 0)
                    dest[dOff + i] = src[sOff + i];
        }

        private static bool stbi__pic_load_core(StbiContext s, int width, int height, ref int comp, byte[] result)
        {
            int act_comp = 0, num_packets = 0, y, chained;
            stbi__pic_packet[] packets = new stbi__pic_packet[10];

            // this will (should...) cater for even some bizarre stuff like having data for the same channel in multiple packets.
            do
            {
                if (num_packets == packets.Length)
                { stbi__errpuc("bad format", "too many packets"); return false; }

                chained = s.get8();
                stbi__pic_packet packet = new stbi__pic_packet();
                packet.size = (byte)s.get8();
                packet.type = (byte)s.get8();
                packet.channel = (byte)s.get8();
                packets[num_packets++] = packet;

                act_comp |= packet.channel;

                if (s.at_eof()) { stbi__errpuc("bad file", "file too short (reading packets)"); return false; }
                if (packet.size != 8) { stbi__errpuc("bad format", "packet isn't 8bpp"); return false; }
            } while (chained != 0);

            comp = (act_comp & 0x10) != 0 ? 4 : 3; // has alpha channel?

            for (y = 0; y < height; ++y)
            {
                int packet_idx;

                for (packet_idx = 0; packet_idx < num_packets; ++packet_idx)
                {
                    stbi__pic_packet packet = packets[packet_idx];
                    int dest = y * width * 4;

                    switch (packet.type)
                    {
                    default:
                        stbi__errpuc("bad format", "packet has bad compression type");
                        return false;

                    case 0:
                    { //uncompressed
                        for (int x = 0; x < width; ++x, dest += 4)
                            if (!stbi__readval(s, packet.channel, result, dest))
                                return false;
                        break;
                    }

                    case 1: //Pure RLE
                    {
                        int left = width, i;

                        while (left > 0)
                        {
                            int count;
                            byte[] value = new byte[4];

                            count = s.get8();
                            if (s.at_eof()) { stbi__errpuc("bad file", "file too short (pure read count)"); return false; }

                            if (count > left)
                                count = left;

                            if (!stbi__readval(s, packet.channel, value, 0)) return false;

                            for (i = 0; i < count; ++i, dest += 4)
                                stbi__copyval(packet.channel, result, dest, value, 0);
                            left -= count;
                        }
                    }
                    break;

                    case 2:
                    { //Mixed RLE
                        int left = width;
                        while (left > 0)
                        {
                            int count = s.get8(), i;
                            if (s.at_eof()) { stbi__errpuc("bad file", "file too short (mixed read count)"); return false; }

                            if (count >= 128)
                            { // Repeated
                                byte[] value = new byte[4];

                                if (count == 128)
                                    count = s.get16be();
                                else
                                    count -= 127;
                                if (count > left)
                                { stbi__errpuc("bad file", "scanline overrun"); return false; }

                                if (!stbi__readval(s, packet.channel, value, 0))
                                    return false;

                                for (i = 0; i < count; ++i, dest += 4)
                                    stbi__copyval(packet.channel, result, dest, value, 0);
                            }
                            else
                            { // Raw
                                ++count;
                                if (count > left) { stbi__errpuc("bad file", "scanline overrun"); return false; }

                                for (i = 0; i < count; ++i, dest += 4)
                                    if (!stbi__readval(s, packet.channel, result, dest))
                                        return false;
                            }
                            left -= count;
                        }
                        break;
                    }
                    }
                }
            }

            return true;
        }

        private static byte[] stbi__pic_load(StbiContext s, out int px, out int py, out int comp, int req_comp)
        {
            byte[] result;
            int i, x, y;
            px = py = comp = 0;

            for (i = 0; i < 92; ++i)
                s.get8();

            x = s.get16be();
            y = s.get16be();

            if (y > STBI_MAX_DIMENSIONS) return stbi__errpuc("too large", "Very large image (corrupt?)");
            if (x > STBI_MAX_DIMENSIONS) return stbi__errpuc("too large", "Very large image (corrupt?)");

            if (s.at_eof()) return stbi__errpuc("bad file", "file too short (pic header)");
            if ((long)x * y * 4 > int.MaxValue) return stbi__errpuc("too large", "PIC image too large to decode");

            s.get32be(); //skip `ratio'
            s.get16be(); //skip `fields'
            s.get16be(); //skip `pad'

            // intermediate buffer is RGBA
            result = new byte[x * y * 4];
            for (i = 0; i < result.Length; ++i) result[i] = 0xff;

            int c = 0;
            if (!stbi__pic_load_core(s, x, y, ref c, result))
                return null;
            comp = c;
            px = x;
            py = y;
            if (req_comp == 0) req_comp = comp;
            result = stbi__convert_format(result, 4, req_comp, x, y);
            return result;
        }

        // ------------------------------------------------------------------------------------------
        // GIF loader -- public domain by Jean-Marc Lienher -- simplified/shrunk by stb (first frame only is returned)
        private sealed class stbi__gif
        {
            public int w, h;
            public byte[] outp;           // output buffer (always 4 components)
            public byte[] background;     // The current "background" as far as a gif is concerned
            public byte[] history;
            public int flags, bgindex, ratio, transparent, eflags;
            public byte[] pal = new byte[256 * 4];
            public byte[] lpal = new byte[256 * 4];
            public short[] code_prefix = new short[8192];
            public byte[] code_first = new byte[8192];
            public byte[] code_suffix = new byte[8192];
            public byte[] color_table;
            public int parse, step;
            public int lflags;
            public int start_x, start_y;
            public int max_x, max_y;
            public int cur_x, cur_y;
            public int line_size;
            public int delay;
        }

        private static bool stbi__gif_test(StbiContext s)
        {
            bool r = true;
            if (s.get8() != 'G' || s.get8() != 'I' || s.get8() != 'F' || s.get8() != '8') r = false;
            else
            {
                int sz = s.get8();
                if (sz != '9' && sz != '7') r = false;
                else if (s.get8() != 'a') r = false;
            }
            s.pos = 0;
            return r;
        }

        private static void stbi__gif_parse_colortable(StbiContext s, byte[] pal, int num_entries, int transp)
        {
            for (int i = 0; i < num_entries; ++i)
            {
                pal[i * 4 + 2] = (byte)s.get8();
                pal[i * 4 + 1] = (byte)s.get8();
                pal[i * 4 + 0] = (byte)s.get8();
                pal[i * 4 + 3] = (byte)(transp == i ? 0 : 255);
            }
        }

        private static int stbi__gif_header(StbiContext s, stbi__gif g, ref int comp, int is_info)
        {
            int version;
            if (s.get8() != 'G' || s.get8() != 'I' || s.get8() != 'F' || s.get8() != '8')
                return stbi__err("not GIF", "Corrupt GIF");

            version = s.get8();
            if (version != '7' && version != '9') return stbi__err("not GIF", "Corrupt GIF");
            if (s.get8() != 'a') return stbi__err("not GIF", "Corrupt GIF");

            failure_reason = "";
            g.w = s.get16le();
            g.h = s.get16le();
            g.flags = s.get8();
            g.bgindex = s.get8();
            g.ratio = s.get8();
            g.transparent = -1;

            if (g.w > STBI_MAX_DIMENSIONS) return stbi__err("too large", "Very large image (corrupt?)");
            if (g.h > STBI_MAX_DIMENSIONS) return stbi__err("too large", "Very large image (corrupt?)");

            comp = 4;  // can't actually tell whether it's 3 or 4 until we parse the comments

            if (is_info != 0) return 1;

            if ((g.flags & 0x80) != 0)
                stbi__gif_parse_colortable(s, g.pal, 2 << (g.flags & 7), -1);

            return 1;
        }

        private static void stbi__out_gif_code(stbi__gif g, ushort code)
        {
            int idx;

            // recurse to decode the prefixes, since the linked-list is backwards,
            // and working backwards through an interleaved image would be nasty
            if (g.code_prefix[code] >= 0)
                stbi__out_gif_code(g, (ushort)g.code_prefix[code]);

            if (g.cur_y >= g.max_y) return;

            idx = g.cur_x + g.cur_y;
            g.history[idx / 4] = 1;

            int c = g.code_suffix[code] * 4;
            if (g.color_table[c + 3] > 128)
            { // don't render transparent pixels;
                g.outp[idx + 0] = g.color_table[c + 2];
                g.outp[idx + 1] = g.color_table[c + 1];
                g.outp[idx + 2] = g.color_table[c + 0];
                g.outp[idx + 3] = g.color_table[c + 3];
            }
            g.cur_x += 4;

            if (g.cur_x >= g.max_x)
            {
                g.cur_x = g.start_x;
                g.cur_y += g.step;

                while (g.cur_y >= g.max_y && g.parse > 0)
                {
                    g.step = (1 << g.parse) * g.line_size;
                    g.cur_y = g.start_y + (g.step >> 1);
                    --g.parse;
                }
            }
        }

        private static byte[] stbi__process_gif_raster(StbiContext s, stbi__gif g)
        {
            int lzw_cs;
            int len, init_code;
            uint first;
            int codesize, codemask, avail, oldcode, bits, valid_bits, clear;

            lzw_cs = s.get8();
            if (lzw_cs > 12) return null;
            clear = 1 << lzw_cs;
            first = 1;
            codesize = lzw_cs + 1;
            codemask = (1 << codesize) - 1;
            bits = 0;
            valid_bits = 0;
            for (init_code = 0; init_code < clear; init_code++)
            {
                g.code_prefix[init_code] = -1;
                g.code_first[init_code] = (byte)init_code;
                g.code_suffix[init_code] = (byte)init_code;
            }

            // support no starting clear code
            avail = clear + 2;
            oldcode = -1;

            len = 0;
            for (; ; )
            {
                if (valid_bits < codesize)
                {
                    if (len == 0)
                    {
                        len = s.get8(); // start new block
                        if (len == 0)
                            return g.outp;
                    }
                    --len;
                    bits |= (int)s.get8() << valid_bits;
                    valid_bits += 8;
                }
                else
                {
                    int code = bits & codemask;
                    bits >>= codesize;
                    valid_bits -= codesize;
                    if (code == clear)
                    {  // clear code
                        codesize = lzw_cs + 1;
                        codemask = (1 << codesize) - 1;
                        avail = clear + 2;
                        oldcode = -1;
                        first = 0;
                    }
                    else if (code == clear + 1)
                    { // end of stream code
                        s.skip(len);
                        while ((len = s.get8()) > 0)
                            s.skip(len);
                        return g.outp;
                    }
                    else if (code <= avail)
                    {
                        if (first != 0)
                        {
                            return stbi__errpuc("no clear code", "Corrupt GIF");
                        }

                        if (oldcode >= 0)
                        {
                            int p = avail++;
                            if (avail > 8192)
                            {
                                return stbi__errpuc("too many codes", "Corrupt GIF");
                            }

                            g.code_prefix[p] = (short)oldcode;
                            g.code_first[p] = g.code_first[oldcode];
                            g.code_suffix[p] = (code == avail) ? g.code_first[p] : g.code_first[code];
                        }
                        else if (code == avail)
                            return stbi__errpuc("illegal code in raster", "Corrupt GIF");

                        stbi__out_gif_code(g, (ushort)code);

                        if ((avail & codemask) == 0 && avail <= 0x0FFF)
                        {
                            codesize++;
                            codemask = (1 << codesize) - 1;
                        }

                        oldcode = code;
                    }
                    else
                    {
                        return stbi__errpuc("illegal code in raster", "Corrupt GIF");
                    }
                }
            }
        }

        private static readonly byte[] stbi__gif_end_marker = new byte[1];

        // this function is designed to support animated gifs, although stb_image doesn't support it
        private static byte[] stbi__gif_load_next(StbiContext s, stbi__gif g, ref int comp, int req_comp, byte[] two_back)
        {
            int dispose;
            bool first_frame;
            int pi;
            int pcount;

            // on first frame, any non-written pixels get the background colour (non-transparent)
            first_frame = false;
            if (g.outp == null)
            {
                if (stbi__gif_header(s, g, ref comp, 0) == 0) return null;
                if ((long)4 * g.w * g.h > int.MaxValue)
                    return stbi__errpuc("too large", "GIF image is too large");
                pcount = g.w * g.h;
                g.outp = new byte[4 * pcount];
                g.background = new byte[4 * pcount];
                g.history = new byte[pcount];
                first_frame = true;
            }
            else
            {
                // second frame - how do we dispose of the previous one?
                dispose = (g.eflags & 0x1C) >> 2;
                pcount = g.w * g.h;

                if ((dispose == 3) && (two_back == null))
                {
                    dispose = 2; // if I don't have an image to revert back to, default to the old background
                }

                if (dispose == 3)
                { // use previous graphic
                    for (pi = 0; pi < pcount; ++pi)
                        if (g.history[pi] != 0)
                            Array.Copy(two_back, pi * 4, g.outp, pi * 4, 4);
                }
                else if (dispose == 2)
                {
                    // restore what was changed last frame to background before that frame;
                    for (pi = 0; pi < pcount; ++pi)
                        if (g.history[pi] != 0)
                            Array.Copy(g.background, pi * 4, g.outp, pi * 4, 4);
                }

                // background is what out is after the undoing of the previou frame;
                Array.Copy(g.outp, g.background, 4 * g.w * g.h);
            }

            // clear my history;
            Array.Clear(g.history, 0, g.w * g.h);

            for (; ; )
            {
                int tag = s.get8();
                switch (tag)
                {
                case 0x2C: /* Image Descriptor */
                {
                    int x, y, w, h;
                    byte[] o;

                    x = s.get16le();
                    y = s.get16le();
                    w = s.get16le();
                    h = s.get16le();
                    if (((x + w) > (g.w)) || ((y + h) > (g.h)))
                        return stbi__errpuc("bad Image Descriptor", "Corrupt GIF");

                    g.line_size = g.w * 4;
                    g.start_x = x * 4;
                    g.start_y = y * g.line_size;
                    g.max_x = g.start_x + w * 4;
                    g.max_y = g.start_y + h * g.line_size;
                    g.cur_x = g.start_x;
                    g.cur_y = g.start_y;

                    // if the width of the specified rectangle is 0, that means we may not see *any* pixels or the image is malformed
                    if (w == 0)
                        g.cur_y = g.max_y;

                    g.lflags = s.get8();

                    if ((g.lflags & 0x40) != 0)
                    {
                        g.step = 8 * g.line_size; // first interlaced spacing
                        g.parse = 3;
                    }
                    else
                    {
                        g.step = g.line_size;
                        g.parse = 0;
                    }

                    if ((g.lflags & 0x80) != 0)
                    {
                        stbi__gif_parse_colortable(s, g.lpal, 2 << (g.lflags & 7), (g.eflags & 0x01) != 0 ? g.transparent : -1);
                        g.color_table = g.lpal;
                    }
                    else if ((g.flags & 0x80) != 0)
                    {
                        g.color_table = g.pal;
                    }
                    else
                        return stbi__errpuc("missing color table", "Corrupt GIF");

                    o = stbi__process_gif_raster(s, g);
                    if (o == null) return null;

                    // if this was the first frame,
                    pcount = g.w * g.h;
                    if (first_frame && (g.bgindex > 0))
                    {
                        // if first frame, any pixel not drawn to gets the background color
                        for (pi = 0; pi < pcount; ++pi)
                        {
                            if (g.history[pi] == 0)
                            {
                                g.pal[g.bgindex * 4 + 3] = 255; // just in case it was made transparent, undo that
                                Array.Copy(g.pal, g.bgindex * 4, g.outp, pi * 4, 4);
                            }
                        }
                    }

                    return o;
                }

                case 0x21: // Comment Extension.
                {
                    int len;
                    int ext = s.get8();
                    if (ext == 0xF9)
                    { // Graphic Control Extension.
                        len = s.get8();
                        if (len == 4)
                        {
                            g.eflags = s.get8();
                            g.delay = 10 * s.get16le(); // delay - 1/100th of a second, saving as 1/1000ths.

                            // unset old transparent
                            if (g.transparent >= 0)
                            {
                                g.pal[g.transparent * 4 + 3] = 255;
                            }
                            if ((g.eflags & 0x01) != 0)
                            {
                                g.transparent = s.get8();
                                if (g.transparent >= 0)
                                {
                                    g.pal[g.transparent * 4 + 3] = 0;
                                }
                            }
                            else
                            {
                                // don't need transparent
                                s.skip(1);
                                g.transparent = -1;
                            }
                        }
                        else
                        {
                            s.skip(len);
                            break;
                        }
                    }
                    while ((len = s.get8()) != 0)
                    {
                        s.skip(len);
                    }
                    break;
                }

                case 0x3B: // gif stream termination code
                    return stbi__gif_end_marker;

                default:
                    return stbi__errpuc("unknown code", "Corrupt GIF");
                }
            }
        }

        private static byte[] stbi__gif_load(StbiContext s, out int x, out int y, out int comp, int req_comp)
        {
            stbi__gif g = new stbi__gif();
            x = y = 0;
            comp = 0;
            byte[] u = stbi__gif_load_next(s, g, ref comp, req_comp, null);
            if (ReferenceEquals(u, stbi__gif_end_marker)) u = null;  // end of animated gif marker
            if (u != null)
            {
                x = g.w;
                y = g.h;

                // moved conversion to after successful load so that the same can be done for multiple frames.
                if (req_comp != 0 && req_comp != 4)
                    u = stbi__convert_format(u, 4, req_comp, g.w, g.h);
            }
            return u;
        }

    }
}
