// A C# implementation of the subset of the OpenEXR (Imf/Imath) API that pbrt-v4 uses:
// single-part scanline files, HALF/FLOAT/UINT channels, the attributes pbrt reads and
// writes, and the NO_COMPRESSION, RLE_COMPRESSION, ZIPS_COMPRESSION and ZIP_COMPRESSION
// codecs. Files using PIZ, PXR24, B44(A), DWAA/DWAB or tiles are rejected with an
// exception. Writing always uses ZIP_COMPRESSION, which is the OpenEXR default.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace pbrt
{
    public static class Imath
    {
        public struct V2i
        {
            public int x, y;
            public V2i(int x, int y) { this.x = x; this.y = y; }
        }
        public struct V2f
        {
            public float x, y;
            public V2f(float x, float y) { this.x = x; this.y = y; }
        }
        public struct Box2i
        {
            public V2i min, max;
            public Box2i(V2i min, V2i max) { this.min = min; this.max = max; }
        }
    }

    public static class Imf
    {
        public enum PixelType { UINT = 0, HALF = 1, FLOAT = 2 }
        public enum Compression
        {
            NO_COMPRESSION = 0, RLE_COMPRESSION = 1, ZIPS_COMPRESSION = 2, ZIP_COMPRESSION = 3,
            PIZ_COMPRESSION = 4, PXR24_COMPRESSION = 5, B44_COMPRESSION = 6,
            B44A_COMPRESSION = 7, DWAA_COMPRESSION = 8, DWAB_COMPRESSION = 9
        }

        public static int PixelTypeSize(PixelType t) { return t == PixelType.HALF ? 2 : 4; }

        public struct Chromaticities
        {
            public Imath.V2f red, green, blue, white;
            public Chromaticities(Imath.V2f red, Imath.V2f green, Imath.V2f blue, Imath.V2f white)
            {
                this.red = red; this.green = green; this.blue = blue; this.white = white;
            }
        }

        public struct Channel
        {
            public PixelType type;
            public int xSampling, ySampling;
            public bool pLinear;
            public Channel(PixelType type) { this.type = type; xSampling = 1; ySampling = 1; pLinear = false; }
        }

        // Channels are kept sorted by name, like Imf::ChannelList (a std::map).
        public class ChannelList
        {
            public SortedDictionary<string, Channel> channels =
                new SortedDictionary<string, Channel>(StringComparer.Ordinal);
            public void insert(string name, PixelType type) { channels[name] = new Channel(type); }
            public void insert(string name, Channel c) { channels[name] = c; }
            public int Count { get { return channels.Count; } }
        }

        public abstract class Attribute
        {
            public abstract string typeName();
        }
        public class FloatAttribute : Attribute
        {
            public float v;
            public FloatAttribute(float v) { this.v = v; }
            public float value() { return v; }
            public override string typeName() { return "float"; }
        }
        public class IntAttribute : Attribute
        {
            public int v;
            public IntAttribute(int v) { this.v = v; }
            public int value() { return v; }
            public override string typeName() { return "int"; }
        }
        public class StringAttribute : Attribute
        {
            public string v;
            public StringAttribute(string v) { this.v = v; }
            public string value() { return v; }
            public override string typeName() { return "string"; }
        }
        public class StringVectorAttribute : Attribute
        {
            public List<string> v;
            public StringVectorAttribute(List<string> v) { this.v = v; }
            public List<string> value() { return v; }
            public override string typeName() { return "stringvector"; }
        }
        public class M44fAttribute : Attribute
        {
            public float[] m = new float[16];  // row major
            public M44fAttribute(float[,] mm)
            {
                for (int i = 0; i < 4; ++i)
                    for (int j = 0; j < 4; ++j)
                        m[4 * i + j] = mm[i, j];
            }
            public M44fAttribute(float[] mm) { Array.Copy(mm, m, 16); }
            public float[] getValue() { return m; }
            public override string typeName() { return "m44f"; }
        }
        public class ChromaticitiesAttribute : Attribute
        {
            public Chromaticities c;
            public ChromaticitiesAttribute(Chromaticities c) { this.c = c; }
            public Chromaticities value() { return c; }
            public override string typeName() { return "chromaticities"; }
        }
        // Attributes we do not interpret are kept verbatim.
        public class OpaqueAttribute : Attribute
        {
            public string type;
            public byte[] data;
            public OpaqueAttribute(string type, byte[] data) { this.type = type; this.data = data; }
            public override string typeName() { return type; }
        }

        public class Header
        {
            public Imath.Box2i displayWindow, dataWindow;
            public ChannelList channelList = new ChannelList();
            public Compression compression = Compression.ZIP_COMPRESSION;
            public byte lineOrder = 0;
            public float pixelAspectRatio = 1;
            public Imath.V2f screenWindowCenter = new Imath.V2f(0, 0);
            public float screenWindowWidth = 1;
            public SortedDictionary<string, Attribute> attributes =
                new SortedDictionary<string, Attribute>(StringComparer.Ordinal);

            public Header() { }
            public Header(Imath.Box2i displayWindow, Imath.Box2i dataWindow)
            {
                this.displayWindow = displayWindow;
                this.dataWindow = dataWindow;
            }

            public ChannelList channels() { return channelList; }
            public Imath.Box2i dispWindow() { return displayWindow; }
            public void insert(string name, Attribute a) { attributes[name] = a; }
            public T findTypedAttribute<T>(string name) where T : Attribute
            {
                Attribute a;
                if (attributes.TryGetValue(name, out a))
                    return a as T;
                return null;
            }
        }

        // pbrt builds a FrameBuffer of slices that address the image's memory. Here a slice
        // addresses a byte array: element (x, y) is at originOffset + x * xStride + y * yStride
        // (originOffset may be negative, since it corresponds to pixel (0,0)).
        public class Slice
        {
            public PixelType type;
            public byte[] data;
            public long originOffset;
            public long xStride, yStride;
            public Slice(PixelType type, byte[] data, long originOffset, long xStride, long yStride)
            {
                this.type = type; this.data = data; this.originOffset = originOffset;
                this.xStride = xStride; this.yStride = yStride;
            }
        }

        public class FrameBuffer
        {
            public SortedDictionary<string, Slice> slices =
                new SortedDictionary<string, Slice>(StringComparer.Ordinal);
            public void insert(string name, Slice s) { slices[name] = s; }
        }

        private static float ReadSample(byte[] b, long p, PixelType t)
        {
            switch (t)
            {
                case PixelType.HALF: return (float)Half.FromBits((ushort)(b[p] | (b[p + 1] << 8)));
                case PixelType.FLOAT: return BitConverter.ToSingle(b, (int)p);
                default: return (float)BitConverter.ToUInt32(b, (int)p);
            }
        }

        private static void WriteSample(byte[] b, long p, PixelType t, float v)
        {
            switch (t)
            {
                case PixelType.HALF:
                    {
                        ushort h = new Half(v).Bits();
                        b[p] = (byte)(h & 255); b[p + 1] = (byte)(h >> 8);
                        break;
                    }
                case PixelType.FLOAT:
                    BitConverter.TryWriteBytes(new Span<byte>(b, (int)p, 4), v);
                    break;
                default:
                    BitConverter.TryWriteBytes(new Span<byte>(b, (int)p, 4), (uint)Math.Max(0f, v));
                    break;
            }
        }

        private static int LinesPerBlock(Compression c)
        {
            switch (c)
            {
                case Compression.NO_COMPRESSION:
                case Compression.RLE_COMPRESSION:
                case Compression.ZIPS_COMPRESSION: return 1;
                case Compression.ZIP_COMPRESSION: return 16;
                default: throw new Exception("Unsupported EXR compression method " + c);
            }
        }

        // Shared by the ZIP and RLE codecs
        private static void Reorder(byte[] src, byte[] dst, int n)
        {
            int t1 = 0, t2 = (n + 1) / 2, s = 0;
            while (s < n)
            {
                dst[t1++] = src[s++];
                if (s < n) dst[t2++] = src[s++];
            }
        }
        private static void Unreorder(byte[] src, byte[] dst, int n)
        {
            int t1 = 0, t2 = (n + 1) / 2, d = 0;
            while (d < n)
            {
                dst[d++] = src[t1++];
                if (d < n) dst[d++] = src[t2++];
            }
        }

        private static byte[] Decompress(Compression c, byte[] chunk, int start, int len, int rawSize)
        {
            byte[] raw = new byte[rawSize];
            if (len == rawSize || c == Compression.NO_COMPRESSION)
            {
                Array.Copy(chunk, start, raw, 0, Math.Min(len, rawSize));
                return raw;
            }
            byte[] tmp = new byte[rawSize];
            if (c == Compression.ZIP_COMPRESSION || c == Compression.ZIPS_COMPRESSION)
            {
                using (MemoryStream ms = new MemoryStream(chunk, start, len))
                using (ZLibStream z = new ZLibStream(ms, CompressionMode.Decompress))
                {
                    int got = 0;
                    while (got < rawSize)
                    {
                        int r = z.Read(tmp, got, rawSize - got);
                        if (r <= 0) break;
                        got += r;
                    }
                    if (got != rawSize) throw new Exception("Corrupt EXR ZIP block");
                }
            }
            else if (c == Compression.RLE_COMPRESSION)
            {
                int ip = start, iend = start + len, op = 0;
                while (ip < iend && op < rawSize)
                {
                    sbyte count = unchecked((sbyte)chunk[ip++]);
                    if (count < 0)
                    {
                        int n = -count;
                        for (int i = 0; i < n; ++i) tmp[op++] = chunk[ip++];
                    }
                    else
                    {
                        int n = count + 1;
                        byte v = chunk[ip++];
                        for (int i = 0; i < n; ++i) tmp[op++] = v;
                    }
                }
                if (op != rawSize) throw new Exception("Corrupt EXR RLE block");
            }
            else
                throw new Exception("Unsupported EXR compression method " + c);
            // Predictor
            for (int i = 1; i < rawSize; ++i)
                tmp[i] = (byte)(tmp[i - 1] + tmp[i] - 128);
            Unreorder(tmp, raw, rawSize);
            return raw;
        }

        private static byte[] Compress(Compression c, byte[] raw, int rawSize)
        {
            if (c == Compression.NO_COMPRESSION) return raw;
            byte[] tmp = new byte[rawSize];
            Reorder(raw, tmp, rawSize);
            // Predictor
            int p = tmp[0];
            for (int i = 1; i < rawSize; ++i)
            {
                int d = tmp[i] - p + (128 + 256);
                p = tmp[i];
                tmp[i] = (byte)d;
            }
            byte[] outb;
            if (c == Compression.ZIP_COMPRESSION || c == Compression.ZIPS_COMPRESSION)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    using (ZLibStream z = new ZLibStream(ms, CompressionLevel.Optimal, true))
                        z.Write(tmp, 0, rawSize);
                    outb = ms.ToArray();
                }
            }
            else
                throw new Exception("Unsupported EXR compression method " + c);
            return outb.Length < rawSize ? outb : raw;
        }

        private static string ReadCString(byte[] d, ref int p)
        {
            int s = p;
            while (d[p] != 0) ++p;
            string r = Encoding.ASCII.GetString(d, s, p - s);
            ++p;
            return r;
        }
        private static int ReadI32(byte[] d, ref int p) { int v = BitConverter.ToInt32(d, p); p += 4; return v; }
        private static float ReadF32(byte[] d, ref int p) { float v = BitConverter.ToSingle(d, p); p += 4; return v; }

        public class InputFile
        {
            private Header hdr = new Header();
            private byte[] file;
            private long[] offsets;
            private FrameBuffer fb;
            private int linesPerBlock;

            public InputFile(string name)
            {
                try { file = File.ReadAllBytes(name); }
                catch (Exception e) { throw new Exception("Cannot open file \"" + name + "\": " + e.Message); }
                if (file.Length < 8 || file[0] != 0x76 || file[1] != 0x2f || file[2] != 0x31 || file[3] != 0x01)
                    throw new Exception("File is not an OpenEXR file.");
                int version = BitConverter.ToInt32(file, 4);
                if ((version & 0x200) != 0) throw new Exception("Tiled OpenEXR files are not supported.");
                if ((version & 0x1000) != 0) throw new Exception("Multi-part OpenEXR files are not supported.");
                if ((version & 0x800) != 0) throw new Exception("Deep OpenEXR files are not supported.");
                int p = 8;
                bool haveCompression = false;
                while (true)
                {
                    string attrName = ReadCString(file, ref p);
                    if (attrName.Length == 0) break;
                    string typeName = ReadCString(file, ref p);
                    int size = ReadI32(file, ref p);
                    int end = p + size;
                    switch (attrName)
                    {
                        case "channels":
                            {
                                int q = p;
                                while (file[q] != 0)
                                {
                                    string cn = ReadCString(file, ref q);
                                    Channel ch = new Channel();
                                    ch.type = (PixelType)ReadI32(file, ref q);
                                    ch.pLinear = file[q] != 0;
                                    q += 4;
                                    ch.xSampling = ReadI32(file, ref q);
                                    ch.ySampling = ReadI32(file, ref q);
                                    hdr.channelList.insert(cn, ch);
                                }
                                break;
                            }
                        case "compression":
                            hdr.compression = (Compression)file[p]; haveCompression = true; break;
                        case "dataWindow":
                            {
                                int q = p;
                                hdr.dataWindow = new Imath.Box2i(new Imath.V2i(ReadI32(file, ref q), ReadI32(file, ref q)),
                                                                 new Imath.V2i(ReadI32(file, ref q), ReadI32(file, ref q)));
                                break;
                            }
                        case "displayWindow":
                            {
                                int q = p;
                                hdr.displayWindow = new Imath.Box2i(new Imath.V2i(ReadI32(file, ref q), ReadI32(file, ref q)),
                                                                    new Imath.V2i(ReadI32(file, ref q), ReadI32(file, ref q)));
                                break;
                            }
                        case "lineOrder": hdr.lineOrder = file[p]; break;
                        case "pixelAspectRatio": { int q = p; hdr.pixelAspectRatio = ReadF32(file, ref q); break; }
                        case "screenWindowWidth": { int q = p; hdr.screenWindowWidth = ReadF32(file, ref q); break; }
                        case "screenWindowCenter":
                            { int q = p; hdr.screenWindowCenter = new Imath.V2f(ReadF32(file, ref q), ReadF32(file, ref q)); break; }
                        default:
                            {
                                int q = p;
                                if (typeName == "float" && size == 4)
                                    hdr.insert(attrName, new FloatAttribute(ReadF32(file, ref q)));
                                else if (typeName == "int" && size == 4)
                                    hdr.insert(attrName, new IntAttribute(ReadI32(file, ref q)));
                                else if (typeName == "string")
                                    hdr.insert(attrName, new StringAttribute(Encoding.UTF8.GetString(file, p, size)));
                                else if (typeName == "stringvector")
                                {
                                    List<string> sv = new List<string>();
                                    while (q < end)
                                    {
                                        int n = ReadI32(file, ref q);
                                        sv.Add(Encoding.UTF8.GetString(file, q, n));
                                        q += n;
                                    }
                                    hdr.insert(attrName, new StringVectorAttribute(sv));
                                }
                                else if (typeName == "m44f" && size == 64)
                                {
                                    float[] m = new float[16];
                                    for (int i = 0; i < 16; ++i) m[i] = ReadF32(file, ref q);
                                    hdr.insert(attrName, new M44fAttribute(m));
                                }
                                else if (typeName == "chromaticities" && size == 32)
                                {
                                    Imath.V2f r = new Imath.V2f(ReadF32(file, ref q), ReadF32(file, ref q));
                                    Imath.V2f g = new Imath.V2f(ReadF32(file, ref q), ReadF32(file, ref q));
                                    Imath.V2f b = new Imath.V2f(ReadF32(file, ref q), ReadF32(file, ref q));
                                    Imath.V2f w = new Imath.V2f(ReadF32(file, ref q), ReadF32(file, ref q));
                                    hdr.insert(attrName, new ChromaticitiesAttribute(new Chromaticities(r, g, b, w)));
                                }
                                else
                                {
                                    byte[] raw = new byte[size];
                                    Array.Copy(file, p, raw, 0, size);
                                    hdr.insert(attrName, new OpaqueAttribute(typeName, raw));
                                }
                                break;
                            }
                    }
                    p = end;
                }
                if (!haveCompression || hdr.channelList.Count == 0)
                    throw new Exception("Required header attributes are missing.");
                linesPerBlock = LinesPerBlock(hdr.compression);
                foreach (Channel ch in hdr.channelList.channels.Values)
                    if (ch.xSampling != 1 || ch.ySampling != 1)
                        throw new Exception("Subsampled OpenEXR channels are not supported.");
                int height = hdr.dataWindow.max.y - hdr.dataWindow.min.y + 1;
                int nBlocks = (height + linesPerBlock - 1) / linesPerBlock;
                offsets = new long[nBlocks];
                for (int i = 0; i < nBlocks; ++i)
                {
                    offsets[i] = BitConverter.ToInt64(file, p);
                    p += 8;
                }
            }

            public Header header() { return hdr; }
            public void setFrameBuffer(FrameBuffer f) { fb = f; }

            public void readPixels(int scanLine1, int scanLine2)
            {
                Imath.Box2i dw = hdr.dataWindow;
                int width = dw.max.x - dw.min.x + 1;
                int height = dw.max.y - dw.min.y + 1;
                int lineBytes = 0;
                foreach (Channel ch in hdr.channelList.channels.Values)
                    lineBytes += width * PixelTypeSize(ch.type);

                for (int blk = 0; blk < offsets.Length; ++blk)
                {
                    int p = (int)offsets[blk];
                    int y0 = BitConverter.ToInt32(file, p);
                    int size = BitConverter.ToInt32(file, p + 4);
                    p += 8;
                    int nLines = Math.Min(linesPerBlock, dw.max.y - y0 + 1);
                    if (y0 < dw.min.y || nLines <= 0) throw new Exception("Corrupt EXR scanline block");
                    int rawSize = lineBytes * nLines;
                    byte[] raw = Decompress(hdr.compression, file, p, size, rawSize);

                    int rp = 0;
                    for (int l = 0; l < nLines; ++l)
                    {
                        int y = y0 + l;
                        foreach (KeyValuePair<string, Channel> kv in hdr.channelList.channels)
                        {
                            int sz = PixelTypeSize(kv.Value.type);
                            Slice s;
                            if (fb != null && y >= scanLine1 && y <= scanLine2 && fb.slices.TryGetValue(kv.Key, out s))
                            {
                                for (int x = 0; x < width; ++x)
                                {
                                    float v = ReadSample(raw, rp + x * sz, kv.Value.type);
                                    WriteSample(s.data, s.originOffset + (long)(x + dw.min.x) * s.xStride + (long)y * s.yStride, s.type, v);
                                }
                            }
                            rp += width * sz;
                        }
                    }
                }
            }
        }

        public class OutputFile
        {
            private string filename;
            private Header hdr;
            private FrameBuffer fb;

            public OutputFile(string name, Header header)
            {
                filename = name;
                hdr = header;
            }

            public void setFrameBuffer(FrameBuffer f) { fb = f; }

            private static void PutI32(MemoryStream o, int v) { o.Write(BitConverter.GetBytes(v), 0, 4); }
            private static void PutF32(MemoryStream o, float v) { o.Write(BitConverter.GetBytes(v), 0, 4); }
            private static void PutStr(MemoryStream o, string s)
            {
                byte[] b = Encoding.ASCII.GetBytes(s);
                o.Write(b, 0, b.Length);
                o.WriteByte(0);
            }
            private static void PutAttr(MemoryStream o, string name, string type, byte[] payload)
            {
                PutStr(o, name); PutStr(o, type);
                PutI32(o, payload.Length);
                o.Write(payload, 0, payload.Length);
            }
            private static byte[] Floats(params float[] f)
            {
                byte[] b = new byte[4 * f.Length];
                for (int i = 0; i < f.Length; ++i) BitConverter.TryWriteBytes(new Span<byte>(b, 4 * i, 4), f[i]);
                return b;
            }
            private static byte[] Ints(params int[] f)
            {
                byte[] b = new byte[4 * f.Length];
                for (int i = 0; i < f.Length; ++i) BitConverter.TryWriteBytes(new Span<byte>(b, 4 * i, 4), f[i]);
                return b;
            }

            public void writePixels(int numScanLines)
            {
                Imath.Box2i dw = hdr.dataWindow;
                int width = dw.max.x - dw.min.x + 1;
                int height = dw.max.y - dw.min.y + 1;
                if (numScanLines != height) throw new Exception("Incorrect number of scan lines written.");
                Compression comp = hdr.compression;
                int lpb = LinesPerBlock(comp);

                using (MemoryStream o = new MemoryStream())
                {
                    o.Write(new byte[] { 0x76, 0x2f, 0x31, 0x01 }, 0, 4);
                    PutI32(o, 2);

                    // channels
                    using (MemoryStream cl = new MemoryStream())
                    {
                        foreach (KeyValuePair<string, Channel> kv in hdr.channelList.channels)
                        {
                            PutStr(cl, kv.Key);
                            PutI32(cl, (int)kv.Value.type);
                            cl.Write(new byte[] { 0, 0, 0, 0 }, 0, 4);
                            PutI32(cl, 1); PutI32(cl, 1);
                        }
                        cl.WriteByte(0);
                        PutAttr(o, "channels", "chlist", cl.ToArray());
                    }
                    PutAttr(o, "compression", "compression", new byte[] { (byte)comp });
                    PutAttr(o, "dataWindow", "box2i", Ints(dw.min.x, dw.min.y, dw.max.x, dw.max.y));
                    PutAttr(o, "displayWindow", "box2i",
                            Ints(hdr.displayWindow.min.x, hdr.displayWindow.min.y, hdr.displayWindow.max.x, hdr.displayWindow.max.y));
                    PutAttr(o, "lineOrder", "lineOrder", new byte[] { hdr.lineOrder });
                    PutAttr(o, "pixelAspectRatio", "float", Floats(hdr.pixelAspectRatio));
                    PutAttr(o, "screenWindowCenter", "v2f", Floats(hdr.screenWindowCenter.x, hdr.screenWindowCenter.y));
                    PutAttr(o, "screenWindowWidth", "float", Floats(hdr.screenWindowWidth));

                    foreach (KeyValuePair<string, Attribute> kv in hdr.attributes)
                    {
                        Attribute a = kv.Value;
                        FloatAttribute fa = a as FloatAttribute;
                        IntAttribute ia = a as IntAttribute;
                        StringAttribute sa = a as StringAttribute;
                        StringVectorAttribute sva = a as StringVectorAttribute;
                        M44fAttribute ma = a as M44fAttribute;
                        ChromaticitiesAttribute ca = a as ChromaticitiesAttribute;
                        OpaqueAttribute oa = a as OpaqueAttribute;
                        if (fa != null) PutAttr(o, kv.Key, "float", Floats(fa.v));
                        else if (ia != null) PutAttr(o, kv.Key, "int", Ints(ia.v));
                        else if (sa != null) PutAttr(o, kv.Key, "string", Encoding.UTF8.GetBytes(sa.v));
                        else if (sva != null)
                        {
                            using (MemoryStream sv = new MemoryStream())
                            {
                                foreach (string s in sva.v)
                                {
                                    byte[] b = Encoding.UTF8.GetBytes(s);
                                    PutI32(sv, b.Length);
                                    sv.Write(b, 0, b.Length);
                                }
                                PutAttr(o, kv.Key, "stringvector", sv.ToArray());
                            }
                        }
                        else if (ma != null) PutAttr(o, kv.Key, "m44f", Floats(ma.m));
                        else if (ca != null)
                            PutAttr(o, kv.Key, "chromaticities",
                                    Floats(ca.c.red.x, ca.c.red.y, ca.c.green.x, ca.c.green.y,
                                           ca.c.blue.x, ca.c.blue.y, ca.c.white.x, ca.c.white.y));
                        else if (oa != null) PutAttr(o, kv.Key, oa.type, oa.data);
                    }
                    o.WriteByte(0);  // end of header

                    int nBlocks = (height + lpb - 1) / lpb;
                    long tableStart = o.Position;
                    o.Write(new byte[8 * nBlocks], 0, 8 * nBlocks);
                    long[] offs = new long[nBlocks];

                    int lineBytes = 0;
                    foreach (Channel ch in hdr.channelList.channels.Values)
                        lineBytes += width * PixelTypeSize(ch.type);

                    for (int blk = 0; blk < nBlocks; ++blk)
                    {
                        int y0 = dw.min.y + blk * lpb;
                        int nLines = Math.Min(lpb, dw.max.y - y0 + 1);
                        int rawSize = lineBytes * nLines;
                        byte[] raw = new byte[rawSize];
                        int rp = 0;
                        for (int l = 0; l < nLines; ++l)
                        {
                            int y = y0 + l;
                            foreach (KeyValuePair<string, Channel> kv in hdr.channelList.channels)
                            {
                                Slice s;
                                if (!fb.slices.TryGetValue(kv.Key, out s))
                                    throw new Exception("Missing frame buffer slice for channel \"" + kv.Key + "\"");
                                int sz = PixelTypeSize(kv.Value.type);
                                for (int x = 0; x < width; ++x)
                                {
                                    float v = ReadSample(s.data, s.originOffset + (long)(x + dw.min.x) * s.xStride + (long)y * s.yStride, s.type);
                                    WriteSample(raw, rp + x * sz, kv.Value.type, v);
                                }
                                rp += width * sz;
                            }
                        }
                        byte[] comp2 = Compress(comp, raw, rawSize);
                        offs[blk] = o.Position;
                        PutI32(o, y0);
                        PutI32(o, comp2.Length);
                        o.Write(comp2, 0, comp2.Length);
                    }

                    byte[] all = o.ToArray();
                    for (int i = 0; i < nBlocks; ++i)
                        BitConverter.TryWriteBytes(new Span<byte>(all, (int)tableStart + 8 * i, 8), offs[i]);
                    File.WriteAllBytes(filename, all);
                }
            }
        }
    }
}
