// A C# implementation of the QOI ("Quite OK Image") format, following the specification at
// https://qoiformat.org (reference implementation Copyright (c) 2021 Dominic Szablewski, MIT).

using System;

namespace pbrt
{
    public static class qoi
    {
        public const int QOI_SRGB = 0;
        public const int QOI_LINEAR = 1;

        public class qoi_desc
        {
            public uint width;
            public uint height;
            public int channels;
            public int colorspace;
        }

        private const byte QOI_OP_INDEX = 0x00;
        private const byte QOI_OP_DIFF = 0x40;
        private const byte QOI_OP_LUMA = 0x80;
        private const byte QOI_OP_RUN = 0xc0;
        private const byte QOI_OP_RGB = 0xfe;
        private const byte QOI_OP_RGBA = 0xff;
        private const byte QOI_MASK_2 = 0xc0;
        private const int QOI_HEADER_SIZE = 14;
        private static readonly byte[] qoi_padding = { 0, 0, 0, 0, 0, 0, 0, 1 };
        private const uint QOI_PIXELS_MAX = 400000000;

        private static int Hash(int r, int g, int b, int a) { return (r * 3 + g * 5 + b * 7 + a * 11) % 64; }

        private static void Write32(byte[] b, ref int p, uint v)
        {
            b[p++] = (byte)(v >> 24); b[p++] = (byte)(v >> 16); b[p++] = (byte)(v >> 8); b[p++] = (byte)v;
        }

        private static uint Read32(byte[] b, ref int p)
        {
            uint a = b[p++], bb = b[p++], c = b[p++], d = b[p++];
            return a << 24 | bb << 16 | c << 8 | d;
        }

        public static byte[] qoi_encode(byte[] data, qoi_desc desc)
        {
            if (data == null || desc.width == 0 || desc.height == 0 || desc.channels < 3 || desc.channels > 4 ||
                desc.colorspace > 1 || desc.height >= QOI_PIXELS_MAX / desc.width)
                return null;

            int max_size = (int)(desc.width * desc.height * (uint)(desc.channels + 1) + QOI_HEADER_SIZE + qoi_padding.Length);
            byte[] bytes = new byte[max_size];
            int p = 0;
            bytes[p++] = (byte)'q'; bytes[p++] = (byte)'o'; bytes[p++] = (byte)'i'; bytes[p++] = (byte)'f';
            Write32(bytes, ref p, desc.width);
            Write32(bytes, ref p, desc.height);
            bytes[p++] = (byte)desc.channels;
            bytes[p++] = (byte)desc.colorspace;

            int[] index = new int[64 * 4];
            int run = 0;
            int pr = 0, pg = 0, pb = 0, pa = 255;
            int r = 0, g = 0, b = 0, a = 255;
            int channels = desc.channels;
            int px_len = (int)(desc.width * desc.height) * channels;
            int px_end = px_len - channels;
            for (int px_pos = 0; px_pos < px_len; px_pos += channels)
            {
                r = data[px_pos]; g = data[px_pos + 1]; b = data[px_pos + 2];
                if (channels == 4) a = data[px_pos + 3];

                if (r == pr && g == pg && b == pb && a == pa)
                {
                    run++;
                    if (run == 62 || px_pos == px_end)
                    {
                        bytes[p++] = (byte)(QOI_OP_RUN | (run - 1));
                        run = 0;
                    }
                }
                else
                {
                    if (run > 0)
                    {
                        bytes[p++] = (byte)(QOI_OP_RUN | (run - 1));
                        run = 0;
                    }

                    int index_pos = Hash(r, g, b, a);
                    if (index[index_pos * 4] == r && index[index_pos * 4 + 1] == g &&
                        index[index_pos * 4 + 2] == b && index[index_pos * 4 + 3] == a)
                    {
                        bytes[p++] = (byte)(QOI_OP_INDEX | index_pos);
                    }
                    else
                    {
                        index[index_pos * 4] = r; index[index_pos * 4 + 1] = g;
                        index[index_pos * 4 + 2] = b; index[index_pos * 4 + 3] = a;

                        if (a == pa)
                        {
                            sbyte vr = unchecked((sbyte)(r - pr));
                            sbyte vg = unchecked((sbyte)(g - pg));
                            sbyte vb = unchecked((sbyte)(b - pb));
                            sbyte vg_r = unchecked((sbyte)(vr - vg));
                            sbyte vg_b = unchecked((sbyte)(vb - vg));

                            if (vr > -3 && vr < 2 && vg > -3 && vg < 2 && vb > -3 && vb < 2)
                            {
                                bytes[p++] = (byte)(QOI_OP_DIFF | (vr + 2) << 4 | (vg + 2) << 2 | (vb + 2));
                            }
                            else if (vg_r > -9 && vg_r < 8 && vg > -33 && vg < 32 && vg_b > -9 && vg_b < 8)
                            {
                                bytes[p++] = (byte)(QOI_OP_LUMA | (vg + 32));
                                bytes[p++] = (byte)((vg_r + 8) << 4 | (vg_b + 8));
                            }
                            else
                            {
                                bytes[p++] = QOI_OP_RGB;
                                bytes[p++] = (byte)r; bytes[p++] = (byte)g; bytes[p++] = (byte)b;
                            }
                        }
                        else
                        {
                            bytes[p++] = QOI_OP_RGBA;
                            bytes[p++] = (byte)r; bytes[p++] = (byte)g; bytes[p++] = (byte)b; bytes[p++] = (byte)a;
                        }
                    }
                }
                pr = r; pg = g; pb = b; pa = a;
            }

            for (int i = 0; i < qoi_padding.Length; ++i)
                bytes[p++] = qoi_padding[i];

            byte[] result = new byte[p];
            Array.Copy(bytes, result, p);
            return result;
        }

        // channels == 0: use the channel count stored in the file
        public static byte[] qoi_decode(byte[] bytes, int size, qoi_desc desc, int channels)
        {
            if (bytes == null || (channels != 0 && channels != 3 && channels != 4) ||
                size < QOI_HEADER_SIZE + qoi_padding.Length)
                return null;

            int p = 0;
            uint header_magic = Read32(bytes, ref p);
            desc.width = Read32(bytes, ref p);
            desc.height = Read32(bytes, ref p);
            desc.channels = bytes[p++];
            desc.colorspace = bytes[p++];

            if (desc.width == 0 || desc.height == 0 || desc.channels < 3 || desc.channels > 4 ||
                desc.colorspace > 1 || header_magic != 0x716f6966 || desc.height >= QOI_PIXELS_MAX / desc.width)
                return null;

            if (channels == 0)
                channels = desc.channels;

            int px_len = (int)(desc.width * desc.height) * channels;
            byte[] pixels = new byte[px_len];

            int[] index = new int[64 * 4];
            int r = 0, g = 0, b = 0, a = 255;
            int run = 0;
            int chunks_len = size - qoi_padding.Length;

            for (int px_pos = 0; px_pos < px_len; px_pos += channels)
            {
                if (run > 0)
                    run--;
                else if (p < chunks_len)
                {
                    int b1 = bytes[p++];

                    if (b1 == QOI_OP_RGB)
                    {
                        r = bytes[p++]; g = bytes[p++]; b = bytes[p++];
                    }
                    else if (b1 == QOI_OP_RGBA)
                    {
                        r = bytes[p++]; g = bytes[p++]; b = bytes[p++]; a = bytes[p++];
                    }
                    else if ((b1 & QOI_MASK_2) == QOI_OP_INDEX)
                    {
                        r = index[b1 * 4]; g = index[b1 * 4 + 1]; b = index[b1 * 4 + 2]; a = index[b1 * 4 + 3];
                    }
                    else if ((b1 & QOI_MASK_2) == QOI_OP_DIFF)
                    {
                        r = (r + ((b1 >> 4) & 0x03) - 2) & 255;
                        g = (g + ((b1 >> 2) & 0x03) - 2) & 255;
                        b = (b + (b1 & 0x03) - 2) & 255;
                    }
                    else if ((b1 & QOI_MASK_2) == QOI_OP_LUMA)
                    {
                        int b2 = bytes[p++];
                        int vg = (b1 & 0x3f) - 32;
                        r = (r + vg - 8 + ((b2 >> 4) & 0x0f)) & 255;
                        g = (g + vg) & 255;
                        b = (b + vg - 8 + (b2 & 0x0f)) & 255;
                    }
                    else if ((b1 & QOI_MASK_2) == QOI_OP_RUN)
                    {
                        run = (b1 & 0x3f);
                    }

                    int ip = Hash(r, g, b, a);
                    index[ip * 4] = r; index[ip * 4 + 1] = g; index[ip * 4 + 2] = b; index[ip * 4 + 3] = a;
                }

                pixels[px_pos] = (byte)r; pixels[px_pos + 1] = (byte)g; pixels[px_pos + 2] = (byte)b;
                if (channels == 4)
                    pixels[px_pos + 3] = (byte)a;
            }

            return pixels;
        }
    }
}
