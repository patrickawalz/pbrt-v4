// OSL port: the JPEG reader of OpenImageIO (jpeginput.cpp, which sits on libjpeg)
//
// A decoder that gives the same pixels as libjpeg(-turbo) with its default settings: baseline, extended sequential and progressive Huffman files;
// the accurate integer inverse DCT of jidctint.c ("islow"); the "fancy" triangle filter upsampling of jdsample.c for 2:1 chroma subsampling; and the
// fixed point YCbCr to RGB conversion of jdcolor.c. One component files are grayscale, three component files are RGB (JFIF YCbCr, or Adobe RGB when
// the Adobe marker says the transform is 0). Arithmetic coding, lossless and 12 bit files are not supported.

using System;
using System.Collections.Generic;

namespace OIIO
{
    internal static class JpegReader
    {
        private static readonly int[] zigzag =
        {
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28, 35, 42, 49, 56, 57, 50,
            43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
        };

        private sealed class HuffTable
        {
            public int[] maxcode = new int[18];
            public int[] valoffset = new int[17];
            public byte[] huffval = new byte[256];
            public bool defined;
        }

        private sealed class Component
        {
            public int id;
            public int h, v;
            public int tq;
            public int dc_tbl, ac_tbl;
            public int width_in_blocks, height_in_blocks;  // rounded up to whole MCUs
            public int downsampled_width, downsampled_height;
            public short[] coefs;  // width_in_blocks * height_in_blocks * 64, natural order
            public int dc_pred;
            public byte[] samples;  // after the IDCT: width_in_blocks * 8 by height_in_blocks * 8
        }

        private sealed class Decoder
        {
            public byte[] d;
            public int pos;
            public int width, height;
            public bool progressive;
            public List<Component> comps = new List<Component>();
            public ushort[][] qt = new ushort[4][];
            public HuffTable[] dc = new HuffTable[4];
            public HuffTable[] ac = new HuffTable[4];
            public int restart_interval;
            public int hmax = 1, vmax = 1;
            public int mcus_x, mcus_y;
            public int adobe_transform = -1;
            public bool saw_jfif;

            // bit reader
            public uint bitbuf;
            public int bitcnt;
            public bool hit_marker;
            public int eobrun;

            public int read_bit_byte()
            {
                if (pos >= d.Length)
                {
                    return 0;
                }
                if (hit_marker)
                {
                    return 0;
                }
                int b = d[pos++];
                if (b == 0xFF)
                {
                    int b2 = pos < d.Length ? d[pos] : 0xD9;
                    if (b2 == 0)
                    {
                        pos++;
                    }
                    else
                    {
                        pos--;
                        hit_marker = true;
                        return 0;
                    }
                }
                return b;
            }

            public int get_bits(int n)
            {
                if (n == 0)
                {
                    return 0;
                }
                while (bitcnt < n)
                {
                    bitbuf = (bitbuf << 8) | (uint)read_bit_byte();
                    bitcnt += 8;
                }
                int r = (int)((bitbuf >> (bitcnt - n)) & ((1u << n) - 1));
                bitcnt -= n;
                return r;
            }

            public int get_bit()
            {
                return get_bits(1);
            }

            public int decode_huff(HuffTable t)
            {
                int code = 0;
                for (int l = 1; l <= 16; l++)
                {
                    code = (code << 1) | get_bit();
                    if (t.maxcode[l] >= 0 && code <= t.maxcode[l])
                    {
                        return t.huffval[code + t.valoffset[l]];
                    }
                }
                return 0;  // corrupt data: libjpeg warns and yields zero
            }

            public static int extend(int v, int t)
            {
                return t == 0 ? 0 : (v < (1 << (t - 1)) ? v - (1 << t) + 1 : v);
            }

            public void reset_bits()
            {
                bitbuf = 0;
                bitcnt = 0;
            }
        }

        public static bool is_jpeg(byte[] d)
        {
            return d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF;
        }

        private static int be16(byte[] d, int p)
        {
            return (d[p] << 8) | d[p + 1];
        }

        private static void build_huff(HuffTable t, byte[] counts, byte[] vals, int nvals)
        {
            Array.Clear(t.maxcode, 0, t.maxcode.Length);
            int code = 0;
            int k = 0;
            for (int l = 1; l <= 16; l++)
            {
                t.valoffset[l] = k - code;
                if (counts[l - 1] != 0)
                {
                    k += counts[l - 1];
                    code += counts[l - 1];
                    t.maxcode[l] = code - 1;
                }
                else
                {
                    t.maxcode[l] = -1;
                }
                code <<= 1;
            }
            t.maxcode[17] = int.MaxValue;
            Array.Copy(vals, t.huffval, nvals);
            t.defined = true;
        }

        public static ImageFileData read(byte[] data, out string error)
        {
            error = "";
            Decoder dec = new Decoder();
            dec.d = data;
            dec.pos = 2;
            bool done = false;
            bool have_frame = false;
            while (!done && dec.pos + 4 <= data.Length)
            {
                if (data[dec.pos] != 0xFF)
                {
                    dec.pos++;
                    continue;
                }
                int marker = data[dec.pos + 1];
                if (marker == 0xFF || marker == 0x00)
                {
                    dec.pos++;
                    continue;
                }
                dec.pos += 2;
                if (marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
                {
                    continue;
                }
                if (marker == 0xD9)
                {
                    break;
                }
                int len = be16(data, dec.pos);
                int seg_end = dec.pos + len;
                int p = dec.pos + 2;
                if (seg_end > data.Length)
                {
                    error = "Premature end of JPEG file";
                    return null;
                }
                switch (marker)
                {
                    case 0xE0:
                        if (len >= 7 && data[p] == 'J' && data[p + 1] == 'F' && data[p + 2] == 'I' && data[p + 3] == 'F')
                        {
                            dec.saw_jfif = true;
                        }
                        break;
                    case 0xEE:
                        if (len >= 14 && data[p] == 'A' && data[p + 1] == 'd' && data[p + 2] == 'o' && data[p + 3] == 'b' && data[p + 4] == 'e')
                        {
                            dec.adobe_transform = data[p + 11];
                        }
                        break;
                    case 0xDB:
                        while (p < seg_end)
                        {
                            int pq = data[p] >> 4;
                            int tq = data[p] & 15;
                            p++;
                            ushort[] q = new ushort[64];
                            for (int i = 0; i < 64; i++)
                            {
                                if (pq != 0)
                                {
                                    q[zigzag[i]] = (ushort)be16(data, p);
                                    p += 2;
                                }
                                else
                                {
                                    q[zigzag[i]] = data[p++];
                                }
                            }
                            dec.qt[tq & 3] = q;
                        }
                        break;
                    case 0xC4:
                        while (p < seg_end)
                        {
                            int tc = data[p] >> 4;
                            int th = data[p] & 3;
                            p++;
                            byte[] counts = new byte[16];
                            int n = 0;
                            for (int i = 0; i < 16; i++)
                            {
                                counts[i] = data[p++];
                                n += counts[i];
                            }
                            byte[] vals = new byte[256];
                            Array.Copy(data, p, vals, 0, Math.Min(n, 256));
                            p += n;
                            HuffTable t = new HuffTable();
                            build_huff(t, counts, vals, Math.Min(n, 256));
                            if (tc == 0)
                            {
                                dec.dc[th] = t;
                            }
                            else
                            {
                                dec.ac[th] = t;
                            }
                        }
                        break;
                    case 0xC0:
                    case 0xC1:
                    case 0xC2:
                        {
                            if (data[p] != 8)
                            {
                                error = "Unsupported JPEG data precision " + data[p];
                                return null;
                            }
                            dec.progressive = marker == 0xC2;
                            dec.height = be16(data, p + 1);
                            dec.width = be16(data, p + 3);
                            int nc = data[p + 5];
                            p += 6;
                            if (nc != 1 && nc != 3)
                            {
                                error = "Unsupported JPEG color space (" + nc + " components)";
                                return null;
                            }
                            for (int i = 0; i < nc; i++)
                            {
                                Component c = new Component();
                                c.id = data[p];
                                c.h = data[p + 1] >> 4;
                                c.v = data[p + 1] & 15;
                                c.tq = data[p + 2] & 3;
                                p += 3;
                                dec.comps.Add(c);
                                dec.hmax = Math.Max(dec.hmax, c.h);
                                dec.vmax = Math.Max(dec.vmax, c.v);
                            }
                            if (nc == 1)
                            {
                                dec.comps[0].h = 1;
                                dec.comps[0].v = 1;
                                dec.hmax = 1;
                                dec.vmax = 1;
                            }
                            dec.mcus_x = (dec.width + 8 * dec.hmax - 1) / (8 * dec.hmax);
                            dec.mcus_y = (dec.height + 8 * dec.vmax - 1) / (8 * dec.vmax);
                            foreach (Component c in dec.comps)
                            {
                                c.width_in_blocks = dec.mcus_x * c.h;
                                c.height_in_blocks = dec.mcus_y * c.v;
                                c.downsampled_width = (dec.width * c.h + dec.hmax - 1) / dec.hmax;
                                c.downsampled_height = (dec.height * c.v + dec.vmax - 1) / dec.vmax;
                                c.coefs = new short[c.width_in_blocks * c.height_in_blocks * 64];
                            }
                            have_frame = true;
                        }
                        break;
                    case 0xC3:
                    case 0xC5:
                    case 0xC6:
                    case 0xC7:
                    case 0xC9:
                    case 0xCA:
                    case 0xCB:
                    case 0xCD:
                    case 0xCE:
                    case 0xCF:
                        error = "Unsupported JPEG process (arithmetic or lossless coding)";
                        return null;
                    case 0xDD:
                        dec.restart_interval = be16(data, p);
                        break;
                    case 0xDA:
                        {
                            if (!have_frame)
                            {
                                error = "Invalid JPEG file structure: missing SOF";
                                return null;
                            }
                            int ns = data[p++];
                            List<Component> scan = new List<Component>();
                            for (int i = 0; i < ns; i++)
                            {
                                int cid = data[p];
                                Component sc = null;
                                foreach (Component c in dec.comps)
                                {
                                    if (c.id == cid)
                                    {
                                        sc = c;
                                    }
                                }
                                if (sc == null)
                                {
                                    error = "Invalid JPEG file structure: bad component id in scan";
                                    return null;
                                }
                                sc.dc_tbl = data[p + 1] >> 4;
                                sc.ac_tbl = data[p + 1] & 15;
                                scan.Add(sc);
                                p += 2;
                            }
                            int ss = data[p];
                            int se = data[p + 1];
                            int ah = data[p + 2] >> 4;
                            int al = data[p + 2] & 15;
                            dec.pos = seg_end;
                            decode_scan(dec, scan, ss, se, ah, al);
                            continue;  // dec.pos is already past the entropy coded data
                        }
                }
                dec.pos = seg_end;
            }
            if (!have_frame)
            {
                error = "Not a JPEG file: starts with 0x" + data[0].ToString("x2") + " 0x" + data[1].ToString("x2");
                return null;
            }
            return finish(dec);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Entropy decoding
        // ------------------------------------------------------------------------------------------------------------------------------------

        private static void decode_scan(Decoder dec, List<Component> scan, int ss, int se, int ah, int al)
        {
            dec.reset_bits();
            dec.hit_marker = false;
            dec.eobrun = 0;
            foreach (Component c in dec.comps)
            {
                c.dc_pred = 0;
            }
            int mcu_count;
            int mcus_per_row;
            bool interleaved = scan.Count > 1;
            Component single = scan[0];
            int blocks_w = 0;
            if (interleaved)
            {
                mcus_per_row = dec.mcus_x;
                mcu_count = dec.mcus_x * dec.mcus_y;
            }
            else
            {
                // a non-interleaved scan covers exactly the blocks inside the image
                blocks_w = (single.downsampled_width + 7) / 8;
                int blocks_h = (single.downsampled_height + 7) / 8;
                mcus_per_row = blocks_w;
                mcu_count = blocks_w * blocks_h;
            }
            int restarts_left = dec.restart_interval;
            for (int m = 0; m < mcu_count; m++)
            {
                if (dec.restart_interval != 0 && restarts_left == 0)
                {
                    process_restart(dec);
                    restarts_left = dec.restart_interval;
                }
                if (interleaved)
                {
                    int mx = m % mcus_per_row;
                    int my = m / mcus_per_row;
                    foreach (Component c in scan)
                    {
                        for (int by = 0; by < c.v; by++)
                        {
                            for (int bx = 0; bx < c.h; bx++)
                            {
                                int blk = (my * c.v + by) * c.width_in_blocks + mx * c.h + bx;
                                decode_block(dec, c, blk * 64, ss, se, ah, al);
                            }
                        }
                    }
                }
                else
                {
                    int bx = m % mcus_per_row;
                    int by = m / mcus_per_row;
                    decode_block(dec, single, (by * single.width_in_blocks + bx) * 64, ss, se, ah, al);
                }
                restarts_left--;
            }
            // skip to the next marker
            dec.reset_bits();
            while (dec.pos + 1 < dec.d.Length)
            {
                if (dec.d[dec.pos] == 0xFF && dec.d[dec.pos + 1] != 0 && !(dec.d[dec.pos + 1] >= 0xD0 && dec.d[dec.pos + 1] <= 0xD7)
                    && dec.d[dec.pos + 1] != 0xFF)
                {
                    break;
                }
                dec.pos++;
            }
        }

        private static void process_restart(Decoder dec)
        {
            dec.reset_bits();
            dec.hit_marker = false;
            // find the RSTn marker
            while (dec.pos + 1 < dec.d.Length)
            {
                if (dec.d[dec.pos] == 0xFF && dec.d[dec.pos + 1] >= 0xD0 && dec.d[dec.pos + 1] <= 0xD7)
                {
                    dec.pos += 2;
                    break;
                }
                if (dec.d[dec.pos] == 0xFF && dec.d[dec.pos + 1] != 0 && dec.d[dec.pos + 1] != 0xFF)
                {
                    break;  // some other marker: the data is damaged
                }
                dec.pos++;
            }
            foreach (Component c in dec.comps)
            {
                c.dc_pred = 0;
            }
            dec.eobrun = 0;
        }

        private static void decode_block(Decoder dec, Component c, int off, int ss, int se, int ah, int al)
        {
            short[] blk = c.coefs;
            if (!dec.progressive)
            {
                HuffTable dct = dec.dc[c.dc_tbl];
                HuffTable act = dec.ac[c.ac_tbl];
                int s = dec.decode_huff(dct);
                int diff = Decoder.extend(dec.get_bits(s), s);
                c.dc_pred += diff;
                blk[off] = (short)c.dc_pred;
                for (int k = 1; k < 64; k++)
                {
                    int rs = dec.decode_huff(act);
                    int r = rs >> 4;
                    s = rs & 15;
                    if (s != 0)
                    {
                        k += r;
                        if (k > 63)
                        {
                            break;
                        }
                        blk[off + zigzag[k]] = (short)Decoder.extend(dec.get_bits(s), s);
                    }
                    else
                    {
                        if (r != 15)
                        {
                            break;
                        }
                        k += 15;
                    }
                }
                return;
            }
            if (ss == 0)
            {
                if (ah == 0)
                {
                    int s = dec.decode_huff(dec.dc[c.dc_tbl]);
                    int diff = Decoder.extend(dec.get_bits(s), s);
                    c.dc_pred += diff;
                    blk[off] = (short)(c.dc_pred << al);
                }
                else if (dec.get_bit() != 0)
                {
                    blk[off] |= (short)(1 << al);
                }
                return;
            }
            HuffTable ach = dec.ac[c.ac_tbl];
            if (ah == 0)
            {
                if (dec.eobrun > 0)
                {
                    dec.eobrun--;
                    return;
                }
                for (int k = ss; k <= se; k++)
                {
                    int rs = dec.decode_huff(ach);
                    int r = rs >> 4;
                    int s = rs & 15;
                    if (s != 0)
                    {
                        k += r;
                        if (k > 63)
                        {
                            break;
                        }
                        blk[off + zigzag[k]] = (short)(Decoder.extend(dec.get_bits(s), s) * (1 << al));
                    }
                    else
                    {
                        if (r == 15)
                        {
                            k += 15;
                        }
                        else
                        {
                            dec.eobrun = 1 << r;
                            if (r != 0)
                            {
                                dec.eobrun += dec.get_bits(r);
                            }
                            dec.eobrun--;
                            break;
                        }
                    }
                }
                return;
            }
            // AC refinement
            int p1 = 1 << al;
            int m1 = -1 << al;
            int kk = ss;
            if (dec.eobrun <= 0)
            {
                for (; kk <= se; kk++)
                {
                    int rs = dec.decode_huff(ach);
                    int r = rs >> 4;
                    int s = rs & 15;
                    int newval = 0;
                    if (s != 0)
                    {
                        newval = dec.get_bit() != 0 ? p1 : m1;
                    }
                    else if (r != 15)
                    {
                        dec.eobrun = 1 << r;
                        if (r != 0)
                        {
                            dec.eobrun += dec.get_bits(r);
                        }
                        break;
                    }
                    do
                    {
                        int idx = off + zigzag[kk];
                        if (blk[idx] != 0)
                        {
                            if (dec.get_bit() != 0)
                            {
                                if ((blk[idx] & p1) == 0)
                                {
                                    blk[idx] = (short)(blk[idx] >= 0 ? blk[idx] + p1 : blk[idx] + m1);
                                }
                            }
                        }
                        else
                        {
                            if (--r < 0)
                            {
                                break;
                            }
                        }
                        kk++;
                    }
                    while (kk <= se);
                    if (newval != 0 && kk <= se)
                    {
                        blk[off + zigzag[kk]] = (short)newval;
                    }
                }
            }
            if (dec.eobrun > 0)
            {
                for (; kk <= se; kk++)
                {
                    int idx = off + zigzag[kk];
                    if (blk[idx] != 0)
                    {
                        if (dec.get_bit() != 0)
                        {
                            if ((blk[idx] & p1) == 0)
                            {
                                blk[idx] = (short)(blk[idx] >= 0 ? blk[idx] + p1 : blk[idx] + m1);
                            }
                        }
                    }
                }
                dec.eobrun--;
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Inverse DCT (jidctint.c, jpeg_idct_islow)
        // ------------------------------------------------------------------------------------------------------------------------------------

        private const int CONST_BITS = 13;
        private const int PASS1_BITS = 2;
        private const int FIX_0_298631336 = 2446;
        private const int FIX_0_390180644 = 3196;
        private const int FIX_0_541196100 = 4433;
        private const int FIX_0_765366865 = 6270;
        private const int FIX_0_899976223 = 7373;
        private const int FIX_1_175875602 = 9633;
        private const int FIX_1_501321110 = 12299;
        private const int FIX_1_847759065 = 15137;
        private const int FIX_1_961570560 = 16069;
        private const int FIX_2_053119869 = 16819;
        private const int FIX_2_562915447 = 20995;
        private const int FIX_3_072711026 = 25172;

        private static int descale(int x, int n)
        {
            return (x + (1 << (n - 1))) >> n;
        }

        private static byte clamp_sample(int v)
        {
            v += 128;
            return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
        }

        private static void idct_block(short[] coef, int off, ushort[] q, byte[] outp, int outoff, int stride)
        {
            int[] ws = new int[64];
            for (int col = 0; col < 8; col++)
            {
                int c0 = coef[off + col] * q[col];
                int c1 = coef[off + 8 + col] * q[8 + col];
                int c2 = coef[off + 16 + col] * q[16 + col];
                int c3 = coef[off + 24 + col] * q[24 + col];
                int c4 = coef[off + 32 + col] * q[32 + col];
                int c5 = coef[off + 40 + col] * q[40 + col];
                int c6 = coef[off + 48 + col] * q[48 + col];
                int c7 = coef[off + 56 + col] * q[56 + col];
                if (c1 == 0 && c2 == 0 && c3 == 0 && c4 == 0 && c5 == 0 && c6 == 0 && c7 == 0)
                {
                    int dcval = c0 << PASS1_BITS;
                    for (int r = 0; r < 8; r++)
                    {
                        ws[r * 8 + col] = dcval;
                    }
                    continue;
                }
                int z2 = c2;
                int z3 = c6;
                int z1 = (z2 + z3) * FIX_0_541196100;
                int tmp2 = z1 + z3 * (-FIX_1_847759065);
                int tmp3 = z1 + z2 * FIX_0_765366865;
                z2 = c0;
                z3 = c4;
                int tmp0 = (z2 + z3) << CONST_BITS;
                int tmp1 = (z2 - z3) << CONST_BITS;
                int tmp10 = tmp0 + tmp3;
                int tmp13 = tmp0 - tmp3;
                int tmp11 = tmp1 + tmp2;
                int tmp12 = tmp1 - tmp2;
                tmp0 = c7;
                tmp1 = c5;
                tmp2 = c3;
                tmp3 = c1;
                z1 = tmp0 + tmp3;
                z2 = tmp1 + tmp2;
                z3 = tmp0 + tmp2;
                int z4 = tmp1 + tmp3;
                int z5 = (z3 + z4) * FIX_1_175875602;
                tmp0 *= FIX_0_298631336;
                tmp1 *= FIX_2_053119869;
                tmp2 *= FIX_3_072711026;
                tmp3 *= FIX_1_501321110;
                z1 *= -FIX_0_899976223;
                z2 *= -FIX_2_562915447;
                z3 *= -FIX_1_961570560;
                z4 *= -FIX_0_390180644;
                z3 += z5;
                z4 += z5;
                tmp0 += z1 + z3;
                tmp1 += z2 + z4;
                tmp2 += z2 + z3;
                tmp3 += z1 + z4;
                ws[0 * 8 + col] = descale(tmp10 + tmp3, CONST_BITS - PASS1_BITS);
                ws[7 * 8 + col] = descale(tmp10 - tmp3, CONST_BITS - PASS1_BITS);
                ws[1 * 8 + col] = descale(tmp11 + tmp2, CONST_BITS - PASS1_BITS);
                ws[6 * 8 + col] = descale(tmp11 - tmp2, CONST_BITS - PASS1_BITS);
                ws[2 * 8 + col] = descale(tmp12 + tmp1, CONST_BITS - PASS1_BITS);
                ws[5 * 8 + col] = descale(tmp12 - tmp1, CONST_BITS - PASS1_BITS);
                ws[3 * 8 + col] = descale(tmp13 + tmp0, CONST_BITS - PASS1_BITS);
                ws[4 * 8 + col] = descale(tmp13 - tmp0, CONST_BITS - PASS1_BITS);
            }
            for (int row = 0; row < 8; row++)
            {
                int w = row * 8;
                int o = outoff + row * stride;
                int z2 = ws[w + 2];
                int z3 = ws[w + 6];
                int z1 = (z2 + z3) * FIX_0_541196100;
                int tmp2 = z1 + z3 * (-FIX_1_847759065);
                int tmp3 = z1 + z2 * FIX_0_765366865;
                int tmp0 = (ws[w + 0] + ws[w + 4]) << CONST_BITS;
                int tmp1 = (ws[w + 0] - ws[w + 4]) << CONST_BITS;
                int tmp10 = tmp0 + tmp3;
                int tmp13 = tmp0 - tmp3;
                int tmp11 = tmp1 + tmp2;
                int tmp12 = tmp1 - tmp2;
                tmp0 = ws[w + 7];
                tmp1 = ws[w + 5];
                tmp2 = ws[w + 3];
                tmp3 = ws[w + 1];
                z1 = tmp0 + tmp3;
                z2 = tmp1 + tmp2;
                z3 = tmp0 + tmp2;
                int z4 = tmp1 + tmp3;
                int z5 = (z3 + z4) * FIX_1_175875602;
                tmp0 *= FIX_0_298631336;
                tmp1 *= FIX_2_053119869;
                tmp2 *= FIX_3_072711026;
                tmp3 *= FIX_1_501321110;
                z1 *= -FIX_0_899976223;
                z2 *= -FIX_2_562915447;
                z3 *= -FIX_1_961570560;
                z4 *= -FIX_0_390180644;
                z3 += z5;
                z4 += z5;
                tmp0 += z1 + z3;
                tmp1 += z2 + z4;
                tmp2 += z2 + z3;
                tmp3 += z1 + z4;
                int sh = CONST_BITS + PASS1_BITS + 3;
                outp[o + 0] = clamp_sample(descale(tmp10 + tmp3, sh));
                outp[o + 7] = clamp_sample(descale(tmp10 - tmp3, sh));
                outp[o + 1] = clamp_sample(descale(tmp11 + tmp2, sh));
                outp[o + 6] = clamp_sample(descale(tmp11 - tmp2, sh));
                outp[o + 2] = clamp_sample(descale(tmp12 + tmp1, sh));
                outp[o + 5] = clamp_sample(descale(tmp12 - tmp1, sh));
                outp[o + 3] = clamp_sample(descale(tmp13 + tmp0, sh));
                outp[o + 4] = clamp_sample(descale(tmp13 - tmp0, sh));
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Upsampling and color conversion
        // ------------------------------------------------------------------------------------------------------------------------------------

        /// The component's rows at full resolution, width x height bytes
        private static byte[] upsample(Decoder dec, Component c)
        {
            int stride = c.width_in_blocks * 8;
            int w = dec.width;
            int h = dec.height;
            byte[] outp = new byte[w * h];
            int hs = dec.hmax / c.h;
            int vs = dec.vmax / c.v;
            int dw = c.downsampled_width;
            int dh = c.downsampled_height;
            byte[] s = c.samples;
            if (hs == 1 && vs == 1)
            {
                for (int y = 0; y < h; y++)
                {
                    Array.Copy(s, y * stride, outp, y * w, w);
                }
            }
            else if (hs == 2 && vs == 1 && dw > 2)
            {
                for (int y = 0; y < h; y++)
                {
                    h2v1_fancy(s, y * stride, dw, outp, y * w, w);
                }
            }
            else if (hs == 2 && vs == 2 && dw > 2)
            {
                int[] colsum = new int[dw];
                for (int y = 0; y < h; y++)
                {
                    // the nearer and the farther input row for this output row (rows past the edges repeat the edge row)
                    int row0 = y >> 1;
                    int row1 = (y & 1) == 0 ? row0 - 1 : row0 + 1;
                    row1 = Math.Max(0, Math.Min(dh - 1, row1));
                    row0 = Math.Min(dh - 1, row0);
                    int b0 = row0 * stride;
                    int b1 = row1 * stride;
                    for (int i = 0; i < dw; i++)
                    {
                        colsum[i] = s[b0 + i] * 3 + s[b1 + i];
                    }
                    h2v2_row(colsum, dw, outp, y * w, w);
                }
            }
            else if (hs == 1 && vs == 2)
            {
                for (int y = 0; y < h; y++)
                {
                    int row0 = Math.Min(dh - 1, y >> 1);
                    int row1 = (y & 1) == 0 ? row0 - 1 : row0 + 1;
                    row1 = Math.Max(0, Math.Min(dh - 1, row1));
                    int bias = (y & 1) == 0 ? 1 : 2;
                    for (int x = 0; x < w; x++)
                    {
                        outp[y * w + x] = (byte)((s[row0 * stride + x] * 3 + s[row1 * stride + x] + bias) >> 2);
                    }
                }
            }
            else
            {
                for (int y = 0; y < h; y++)
                {
                    int sy = Math.Min(dh - 1, y / vs);
                    for (int x = 0; x < w; x++)
                    {
                        outp[y * w + x] = s[sy * stride + Math.Min(dw - 1, x / hs)];
                    }
                }
            }
            return outp;
        }

        private static void h2v1_fancy(byte[] s, int soff, int dw, byte[] outp, int ooff, int w)
        {
            byte[] row = new byte[dw * 2];
            row[0] = s[soff];
            row[1] = (byte)((s[soff] * 3 + s[soff + 1] + 2) >> 2);
            for (int i = 1; i < dw - 1; i++)
            {
                int v = s[soff + i] * 3;
                row[2 * i] = (byte)((v + s[soff + i - 1] + 1) >> 2);
                row[2 * i + 1] = (byte)((v + s[soff + i + 1] + 2) >> 2);
            }
            row[2 * (dw - 1)] = (byte)((s[soff + dw - 1] * 3 + s[soff + dw - 2] + 1) >> 2);
            row[2 * (dw - 1) + 1] = s[soff + dw - 1];
            Array.Copy(row, 0, outp, ooff, Math.Min(w, row.Length));
        }

        private static void h2v2_row(int[] cs, int dw, byte[] outp, int ooff, int w)
        {
            byte[] row = new byte[dw * 2];
            row[0] = (byte)((cs[0] * 4 + 8) >> 4);
            row[1] = (byte)((cs[0] * 3 + cs[1] + 7) >> 4);
            for (int i = 1; i < dw - 1; i++)
            {
                int t = cs[i] * 3;
                row[2 * i] = (byte)((t + cs[i - 1] + 8) >> 4);
                row[2 * i + 1] = (byte)((t + cs[i + 1] + 7) >> 4);
            }
            row[2 * (dw - 1)] = (byte)((cs[dw - 1] * 3 + cs[dw - 2] + 8) >> 4);
            row[2 * (dw - 1) + 1] = (byte)((cs[dw - 1] * 4 + 7) >> 4);
            Array.Copy(row, 0, outp, ooff, Math.Min(w, row.Length));
        }

        private static int fix(double x)
        {
            return (int)(x * 65536 + 0.5);
        }

        private static ImageFileData finish(Decoder dec)
        {
            foreach (Component c in dec.comps)
            {
                ushort[] q = dec.qt[c.tq];
                if (q == null)
                {
                    q = new ushort[64];
                    for (int i = 0; i < 64; i++)
                    {
                        q[i] = 1;
                    }
                }
                int stride = c.width_in_blocks * 8;
                c.samples = new byte[stride * c.height_in_blocks * 8];
                for (int by = 0; by < c.height_in_blocks; by++)
                {
                    for (int bx = 0; bx < c.width_in_blocks; bx++)
                    {
                        idct_block(c.coefs, (by * c.width_in_blocks + bx) * 64, q, c.samples, by * 8 * stride + bx * 8, stride);
                    }
                }
            }
            int w = dec.width;
            int h = dec.height;
            int nch = dec.comps.Count;
            float[] pixels = new float[w * h * nch];
            if (nch == 1)
            {
                byte[] y = upsample(dec, dec.comps[0]);
                for (int i = 0; i < w * h; i++)
                {
                    pixels[i] = y[i] * ImageReaders.u8scale;
                }
            }
            else
            {
                byte[] c0 = upsample(dec, dec.comps[0]);
                byte[] c1 = upsample(dec, dec.comps[1]);
                byte[] c2 = upsample(dec, dec.comps[2]);
                bool ycc = !(dec.adobe_transform == 0) && !(dec.comps[0].id == 'R' && dec.comps[1].id == 'G' && dec.comps[2].id == 'B' && !dec.saw_jfif);
                int[] cr_r = new int[256];
                int[] cb_b = new int[256];
                int[] cr_g = new int[256];
                int[] cb_g = new int[256];
                for (int i = 0; i < 256; i++)
                {
                    int x = i - 128;
                    cr_r[i] = (fix(1.40200) * x + 32768) >> 16;
                    cb_b[i] = (fix(1.77200) * x + 32768) >> 16;
                    cr_g[i] = -fix(0.71414) * x;
                    cb_g[i] = -fix(0.34414) * x + 32768;
                }
                for (int i = 0; i < w * h; i++)
                {
                    int r;
                    int g;
                    int b;
                    if (ycc)
                    {
                        int y = c0[i];
                        int cb = c1[i];
                        int cr = c2[i];
                        r = y + cr_r[cr];
                        g = y + ((cb_g[cb] + cr_g[cr]) >> 16);
                        b = y + cb_b[cb];
                        r = r < 0 ? 0 : (r > 255 ? 255 : r);
                        g = g < 0 ? 0 : (g > 255 ? 255 : g);
                        b = b < 0 ? 0 : (b > 255 ? 255 : b);
                    }
                    else
                    {
                        r = c0[i];
                        g = c1[i];
                        b = c2[i];
                    }
                    pixels[i * 3] = r * ImageReaders.u8scale;
                    pixels[i * 3 + 1] = g * ImageReaders.u8scale;
                    pixels[i * 3 + 2] = b * ImageReaders.u8scale;
                }
            }
            ImageSpecData spec = ImageReaders.make_spec(w, h, nch, TypeDesc.UINT8);
            return ImageReaders.single("jpeg", spec, pixels);
        }
    }
}
