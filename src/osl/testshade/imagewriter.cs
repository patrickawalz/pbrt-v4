// (not in OpenShadingLanguage) imagewriter.cs: writes the output buffers of testshade as uncompressed TIFF or OpenEXR files, standing in for OIIO::ImageBuf::write

using System;
using System.IO;
using OIIO;

namespace OSL.TestShade
{
    public static class ImageWriter
    {
        private static byte to_uint8(float v)
        {
            float s = v * 255.0f + 0.5f;
            if (!(s > 0.0f))
            {
                return 0;
            }
            if (s >= 255.0f)
            {
                return 255;
            }
            return (byte)s;
        }

        private static ushort to_uint16(float v)
        {
            float s = v * 65535.0f + 0.5f;
            if (!(s > 0.0f))
            {
                return 0;
            }
            if (s >= 65535.0f)
            {
                return 65535;
            }
            return (ushort)s;
        }

        public static void write(OutputBuffer buf, string dataformatname)
        {
            string filename = buf.filename;
            string format = dataformatname;
            if (format.Length == 0)
            {
                // The ImageBuf of an output has the base type of the output symbol (OIIO writes the buffer in its own format by default), which is
                // int32 for an int output
                format = buf.datatype.basetype == TypeDesc.INT ? "int32" : "float";
            }
            string lower = filename.ToLowerInvariant();
            try
            {
                if (lower.EndsWith(".tif") || lower.EndsWith(".tiff"))
                {
                    write_tiff(buf, format);
                }
                else if (lower.EndsWith(".exr"))
                {
                    write_exr(buf, format);
                }
                else
                {
                    Console.Error.WriteLine("Could not write " + filename + ": unsupported image format");
                }
            }
            catch (IOException e)
            {
                Console.Error.WriteLine("Could not write " + filename + ": " + e.Message);
            }
        }

        private static void write_tiff(OutputBuffer buf, string format)
        {
            int w = buf.xres;
            int h = buf.yres;
            int nch = buf.nchannels;
            int bits = format == "uint8" ? 8 : (format == "uint16" ? 16 : 32);
            bool isint32 = format == "int32";
            bool isfloat = bits == 32 && !isint32;
            int bytes = w * h * nch * (bits / 8);
            byte[] data = new byte[bytes];
            int n = w * h * nch;
            for (int i = 0; i < n; ++i)
            {
                float v = buf.pixels[i];
                if (bits == 8)
                {
                    data[i] = to_uint8(v);
                }
                else if (bits == 16)
                {
                    BitConverter.GetBytes(to_uint16(v)).CopyTo(data, i * 2);
                }
                else if (isint32)
                {
                    // float to int32 as OIIO converts: the float is a fraction of the range of the integer type
                    double scaled = Math.Round((double)v * 2147483647.0, MidpointRounding.AwayFromZero);
                    int iv = scaled >= 2147483647.0 ? int.MaxValue : (scaled <= -2147483648.0 ? int.MinValue : (int)scaled);
                    BitConverter.GetBytes(iv).CopyTo(data, i * 4);
                }
                else
                {
                    BitConverter.GetBytes(v).CopyTo(data, i * 4);
                }
            }

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                // header, then image data, then the IFD
                bw.Write((byte)'I');
                bw.Write((byte)'I');
                bw.Write((ushort)42);
                bw.Write((uint)0);  // IFD offset, patched below
                int bpsOffset = (int)ms.Position;
                for (int c = 0; c < nch; ++c)
                {
                    bw.Write((ushort)bits);
                }
                int sfOffset = (int)ms.Position;
                for (int c = 0; c < nch; ++c)
                {
                    bw.Write((ushort)(isfloat ? 3 : (isint32 ? 2 : 1)));
                }
                if ((ms.Position & 1) != 0)
                {
                    bw.Write((byte)0);
                }
                int dataOffset = (int)ms.Position;
                bw.Write(data);
                if ((ms.Position & 1) != 0)
                {
                    bw.Write((byte)0);
                }
                int ifdOffset = (int)ms.Position;

                int photometric = nch >= 3 ? 2 : 1;
                int extra = nch == 2 ? 1 : (nch > 3 ? nch - 3 : 0);
                int ntags = 10 + (extra > 0 ? 1 : 0) + (isfloat || isint32 ? 1 : 0);
                bw.Write((ushort)ntags);
                write_entry(bw, 256, 4, 1, (uint)w);
                write_entry(bw, 257, 4, 1, (uint)h);
                write_entry(bw, 258, 3, nch, nch == 1 ? (uint)bits : (uint)bpsOffset);
                write_entry(bw, 259, 3, 1, 1);
                write_entry(bw, 262, 3, 1, (uint)photometric);
                write_entry(bw, 273, 4, 1, (uint)dataOffset);
                write_entry(bw, 277, 3, 1, (uint)nch);
                write_entry(bw, 278, 4, 1, (uint)h);
                write_entry(bw, 279, 4, 1, (uint)bytes);
                write_entry(bw, 284, 3, 1, 1);
                if (extra > 0)
                {
                    write_entry(bw, 338, 3, extra, 2);
                }
                if (isfloat || isint32)
                {
                    write_entry(bw, 339, 3, nch, nch == 1 ? (isint32 ? 2u : 3u) : (uint)sfOffset);
                }
                bw.Write((uint)0);
                bw.Flush();
                byte[] file = ms.ToArray();
                BitConverter.GetBytes((uint)ifdOffset).CopyTo(file, 4);
                File.WriteAllBytes(buf.filename, file);
            }
        }

        private static void write_entry(BinaryWriter bw, int tag, int type, int count, uint value)
        {
            bw.Write((ushort)tag);
            bw.Write((ushort)type);
            bw.Write((uint)count);
            if (type == 3 && count <= 2)
            {
                bw.Write((ushort)value);
                bw.Write((ushort)0);
            }
            else
            {
                bw.Write(value);
            }
        }

        private static void write_exr(OutputBuffer buf, string format)
        {
            int w = buf.xres;
            int h = buf.yres;
            int nch = buf.nchannels;
            bool half = format != "float";
            string[] names;
            int[] order;  // index in the pixel for each channel in alphabetical order of the names
            if (nch == 1)
            {
                names = new string[] { "Y" };
                order = new int[] { 0 };
            }
            else if (nch == 3)
            {
                names = new string[] { "B", "G", "R" };
                order = new int[] { 2, 1, 0 };
            }
            else if (nch == 4)
            {
                names = new string[] { "A", "B", "G", "R" };
                order = new int[] { 3, 2, 1, 0 };
            }
            else
            {
                names = new string[nch];
                order = new int[nch];
                for (int c = 0; c < nch; ++c)
                {
                    names[c] = "channel" + (c < 10 ? "0" : "") + c.ToString();
                    order[c] = c;
                }
            }
            int bytesPerSample = half ? 2 : 4;

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(20000630);
                bw.Write(2);
                // channels
                write_attr_header(bw, "channels", "chlist", 0);
                long sizePos = ms.Position - 4;
                long start = ms.Position;
                for (int c = 0; c < nch; ++c)
                {
                    bw.Write(System.Text.Encoding.ASCII.GetBytes(names[c]));
                    bw.Write((byte)0);
                    bw.Write(half ? 1 : 2);  // pixel type HALF=1, FLOAT=2
                    bw.Write(0);             // pLinear + reserved
                    bw.Write(1);             // xSampling
                    bw.Write(1);             // ySampling
                }
                bw.Write((byte)0);
                patch_size(ms, bw, sizePos, start);
                write_attr_header(bw, "compression", "compression", 1);
                bw.Write((byte)0);
                write_attr_header(bw, "dataWindow", "box2i", 16);
                bw.Write(0);
                bw.Write(0);
                bw.Write(w - 1);
                bw.Write(h - 1);
                write_attr_header(bw, "displayWindow", "box2i", 16);
                bw.Write(0);
                bw.Write(0);
                bw.Write(w - 1);
                bw.Write(h - 1);
                write_attr_header(bw, "lineOrder", "lineOrder", 1);
                bw.Write((byte)0);
                write_attr_header(bw, "pixelAspectRatio", "float", 4);
                bw.Write(1.0f);
                write_attr_header(bw, "screenWindowCenter", "v2f", 8);
                bw.Write(0.0f);
                bw.Write(0.0f);
                write_attr_header(bw, "screenWindowWidth", "float", 4);
                bw.Write(1.0f);
                bw.Write((byte)0);  // end of header

                // offset table, then the scanlines
                long tablePos = ms.Position;
                for (int y = 0; y < h; ++y)
                {
                    bw.Write((long)0);
                }
                long[] offsets = new long[h];
                int lineBytes = w * nch * bytesPerSample;
                for (int y = 0; y < h; ++y)
                {
                    offsets[y] = ms.Position;
                    bw.Write(y);
                    bw.Write(lineBytes);
                    for (int c = 0; c < nch; ++c)
                    {
                        for (int x = 0; x < w; ++x)
                        {
                            float v = buf.pixels[(y * w + x) * nch + order[c]];
                            if (half)
                            {
                                bw.Write(BitConverter.HalfToUInt16Bits((Half)v));
                            }
                            else
                            {
                                bw.Write(v);
                            }
                        }
                    }
                }
                bw.Flush();
                byte[] file = ms.ToArray();
                for (int y = 0; y < h; ++y)
                {
                    BitConverter.GetBytes(offsets[y]).CopyTo(file, (int)tablePos + y * 8);
                }
                File.WriteAllBytes(buf.filename, file);
            }
        }

        private static void write_attr_header(BinaryWriter bw, string name, string type, int size)
        {
            bw.Write(System.Text.Encoding.ASCII.GetBytes(name));
            bw.Write((byte)0);
            bw.Write(System.Text.Encoding.ASCII.GetBytes(type));
            bw.Write((byte)0);
            bw.Write(size);
        }

        private static void patch_size(MemoryStream ms, BinaryWriter bw, long sizePos, long start)
        {
            bw.Flush();
            long end = ms.Position;
            ms.Position = sizePos;
            bw.Write((int)(end - start));
            bw.Flush();
            ms.Position = end;
        }
    }
}
