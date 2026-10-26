// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: MPL-2.0
// nanovdb/util/IO.h
//
// Port of the read side of nanovdb::io (readGrid, readGrids, readGridMetaData, hasGrid).
// Both NANOVDB_USE_ZIP and NANOVDB_USE_BLOSC are undefined in pbrt's build, so (as in the
// C++) only uncompressed (Codec::NONE) files can be read. NOT ported: writeGrid/writeGrids.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace nanovdb
{
    // std::runtime_error
    public class RuntimeError : Exception
    {
        public RuntimeError(string msg) : base(msg) { }
    }

    namespace io
    {
        // We fix a specific size for counting bytes in files so that they
        // are saved the same regardless of machine precision.
        // using fileSize_t = uint64_t;

        // Optional compression codecs
        public enum Codec : ushort { NONE = 0, ZIP = 1, BLOSC = 2, END = 3 }

        public static unsafe class IO
        {
            static readonly string[] codecLUT = { "NONE", "ZIP", "BLOSC", "END" };
            public static string toStr(Codec codec) { return codecLUT[(int)codec]; }

            public const ulong MAX_SIZE = 1UL << 30;  // size is 1 GB

            // Equivalent of #define NANOVDB_USE_ZIP: enabled by nanovdb2pbrt only (pbrt itself
            // is built without it)
            public static bool USE_ZIP = false;

            // Standard hash function to use on strings
            public static ulong stringHash(string str)
            {
                ulong hash = 0;
                if (str == null) return hash;
                byte[] bytes = Encoding.UTF8.GetBytes(str);
                for (int i = 0; i < bytes.Length && bytes[i] != 0; ++i)
                {
                    ulong overflow = hash >> (64 - 8);
                    hash *= 67;  // Next-ish prime after 26 + 26 + 10
                    hash += (ulong)bytes[i] + overflow;
                }
                return hash;
            }

            // Return a uint64_t with its bytes reversed so we can check for endianness
            public static ulong reverseEndianness(ulong val)
            {
                return System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(val);
            }

            // Internal::read: read the buffer of a handle using the optional compression
            public static void Internal_read<BufferT>(Stream @is, GridHandle<BufferT> handle, Codec codec)
                where BufferT : class, IGridBuffer<BufferT>, new()
            {
                byte* data = handle.buffer().data();
                ulong residual = handle.buffer().size();

                switch (codec)
                {
                    case Codec.ZIP:
                    {
                        if (!USE_ZIP)
                            throw new RuntimeError("ZIP compression codec was disabled during build");
                        // (zlib's uncompress() == a ZLibStream)
                        byte[] sz = new byte[8];
                        ulong size = 0;
                        if (ReadFully(@is, sz, 8)) size = BitConverter.ToUInt64(sz, 0);
                        byte[] tmp = new byte[size];
                        ReadFully(@is, tmp, (int)size);
                        ulong numBytes = 0;
                        try
                        {
                            using (System.IO.Compression.ZLibStream zs = new System.IO.Compression.ZLibStream(new MemoryStream(tmp), System.IO.Compression.CompressionMode.Decompress))
                            {
                                while (numBytes < residual)
                                {
                                    int n = zs.Read(new Span<byte>(data + numBytes, (int)Math.Min(residual - numBytes, 1UL << 30)));
                                    if (n <= 0) break;
                                    numBytes += (ulong)n;
                                }
                            }
                        }
                        catch (InvalidDataException) { }  // (the C++ ignores a failed status here)
                        if (numBytes != residual)
                            throw new RuntimeError("UNZIP failed on byte size");
                        break;
                    }
                    case Codec.BLOSC:
                        throw new RuntimeError("BLOSC compression codec was disabled during build");
                    default:
                    {
                        bool ok = true;
                        while (residual > 0)
                        {
                            int chunk = (int)Math.Min(residual, 1UL << 30);
                            int n = @is.Read(new Span<byte>(data, chunk));
                            if (n <= 0) { ok = false; break; }
                            data += n;
                            residual -= (ulong)n;
                        }
                        if (!ok) throw new RuntimeError("Failed to read Tree from file");
                        break;
                    }
                }
            }

            // Data encoded at the head of each segment of a file or stream.
            public struct Header
            {
                public ulong magic;       // 8 bytes
                public Version version;   // 4 bytes version numbers
                public ushort gridCount;  // 2 bytes
                public Codec codec;       // 2 bytes
                public const int SIZEOF = 16;
            }

            // Data encoded for each of the grids associated with a segment (MetaData, 176 bytes)
            // plus the grid name (io::GridMetaData).
            public class GridMetaData
            {
                public ulong gridSize, fileSize, nameKey, voxelCount;
                public GridType gridType;
                public GridClass gridClass;
                public BBox<double> worldBBox;
                public CoordBBox indexBBox;
                public Vec3<double> voxelSize;
                public uint nameSize;
                public uint[] nodeCount = new uint[4];
                public uint[] tileCount = new uint[3];
                public Codec codec;
                public ushort padding;
                public Version version;
                public string gridName;
                public const int SIZEOF = 176;

                public ulong memUsage() { return (ulong)SIZEOF + nameSize; }

                public void read(Stream @is)
                {
                    byte[] buf = new byte[SIZEOF];
                    bool ok = ReadFully(@is, buf, SIZEOF);
                    fixed (byte* p = buf)
                    {
                        gridSize = *(ulong*)(p + 0);
                        fileSize = *(ulong*)(p + 8);
                        nameKey = *(ulong*)(p + 16);
                        voxelCount = *(ulong*)(p + 24);
                        gridType = *(GridType*)(p + 32);
                        gridClass = *(GridClass*)(p + 36);
                        worldBBox = *(BBox<double>*)(p + 40);
                        indexBBox = *(CoordBBox*)(p + 88);
                        voxelSize = *(Vec3<double>*)(p + 112);
                        nameSize = *(uint*)(p + 136);
                        for (int i = 0; i < 4; ++i) nodeCount[i] = *(uint*)(p + 140 + 4 * i);
                        for (int i = 0; i < 3; ++i) tileCount[i] = *(uint*)(p + 156 + 4 * i);
                        codec = (Codec)(*(ushort*)(p + 168));
                        padding = *(ushort*)(p + 170);
                        version = *(Version*)(p + 172);
                    }
                    // std::unique_ptr<char[]> tmp(new char[nameSize]);
                    byte[] tmp = new byte[ok ? nameSize : 0];
                    if (ok) ok = ReadFully(@is, tmp, (int)nameSize);
                    int len = 0;
                    while (len < tmp.Length && tmp[len] != 0) ++len;
                    gridName = Encoding.UTF8.GetString(tmp, 0, len);
                    if (!ok) throw new RuntimeError("Failed reading GridMetaData");
                }
            }

            // reads exactly n bytes; false on a short read
            internal static bool ReadFully(Stream s, byte[] buf, int n)
            {
                int got = 0;
                while (got < n)
                {
                    int r = s.Read(buf, got, n - got);
                    if (r <= 0) return false;
                    got += r;
                }
                return true;
            }

            // A file or stream is composed of one or more segments that each contain
            // one or more grids.
            public class Segment
            {
                public Header header;
                public List<GridMetaData> meta = new List<GridMetaData>();

                public Segment() { header.magic = Defs.NANOVDB_MAGIC_NUMBER; header.version = Version.Default(); header.gridCount = 0; header.codec = Codec.NONE; }

                public ulong memUsage()
                {
                    ulong sum = Header.SIZEOF;
                    foreach (GridMetaData m in meta) sum += m.memUsage();
                    return sum;
                }

                public bool read(Stream @is)
                {
                    byte[] buf = new byte[Header.SIZEOF];
                    int got = 0;
                    bool eof = false;
                    while (got < Header.SIZEOF)
                    {
                        int r = @is.Read(buf, got, Header.SIZEOF - got);
                        if (r <= 0) { eof = true; break; }
                        got += r;
                    }
                    fixed (byte* p = buf)
                    {
                        header.magic = *(ulong*)p;
                        header.version = *(Version*)(p + 8);
                        header.gridCount = *(ushort*)(p + 12);
                        header.codec = (Codec)(*(ushort*)(p + 14));
                    }
                    if (eof)
                        return false;
                    if (header.magic != Defs.NANOVDB_MAGIC_NUMBER)
                    {
                        // first check for byte-swapped header magic.
                        if (header.magic == reverseEndianness(Defs.NANOVDB_MAGIC_NUMBER))
                            throw new RuntimeError("This nvdb file has reversed endianness");
                        throw new RuntimeError("Magic number error: This is not a valid nvdb file");
                    }
                    else if (header.version.getMajor() != Defs.NANOVDB_MAJOR_VERSION_NUMBER)
                    {
                        StringBuilder ss = new StringBuilder();
                        if (header.version.getMajor() < Defs.NANOVDB_MAJOR_VERSION_NUMBER)
                        {
                            ss.Append("The file contains an older version of NanoVDB: " + header.version.c_str() + "!\n\t" +
                                      "Recommendation: Re-generate this NanoVDB file with this version: " +
                                      Defs.NANOVDB_MAJOR_VERSION_NUMBER + ".X of NanoVDB");
                        }
                        else
                        {
                            ss.Append("This tool was compiled against an older version of NanoVDB: " + Defs.NANOVDB_MAJOR_VERSION_NUMBER + ".X!\n\t" +
                                      "Recommendation: Re-compile this tool against the newer version: " + header.version.getMajor() + ".X of NanoVDB");
                        }
                        throw new RuntimeError("An unrecoverable error in nanovdb::Segment::read:\n\tIncompatible file format: " + ss.ToString());
                    }
                    meta = new List<GridMetaData>();
                    for (int i = 0; i < header.gridCount; ++i)
                        meta.Add(new GridMetaData());
                    foreach (GridMetaData m in meta)
                    {
                        m.read(@is);
                        m.version = header.version;
                    }
                    return true;
                }
            }

            static Stream OpenInput(string fileName)
            {
                try
                {
                    return new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                }
                catch (Exception)
                {
                    throw new RuntimeError("Unable to open file named \"" + fileName + "\" for input");
                }
            }

            // Read the n'th grid
            public static GridHandle<BufferT> readGrid<BufferT>(string fileName, ulong n, int verbose, BufferT buffer)
                where BufferT : class, IGridBuffer<BufferT>, new()
            {
                using (Stream @is = OpenInput(fileName))
                {
                    GridHandle<BufferT> handle = readGrid<BufferT>(@is, n, buffer);
                    if (verbose != 0)
                        Console.Out.WriteLine("Read NanoGrid # " + n + " from the file named \"" + fileName + "\"");
                    return handle;
                }
            }

            public static GridHandle<BufferT> readGrid<BufferT>(Stream @is, ulong n, BufferT buffer)
                where BufferT : class, IGridBuffer<BufferT>, new()
            {
                Segment s = new Segment();
                ulong counter = 0;
                while (s.read(@is))
                {
                    long seek = 0;
                    foreach (GridMetaData m in s.meta)
                    {
                        if (counter == n)
                        {
                            GridHandle<BufferT> handle = new GridHandle<BufferT>(BufferT.create(m.gridSize, buffer));
                            @is.Seek(seek, SeekOrigin.Current);  // skip forward from the current position
                            Internal_read(@is, handle, s.header.codec);
                            return handle;
                        }
                        else
                        {
                            seek += (long)m.fileSize;
                        }
                        ++counter;
                    }
                    @is.Seek(seek, SeekOrigin.Current);  // skip forward from the current position
                }
                throw new RuntimeError("Grid index exceeds grid count in file");
            }

            // Read the first grid with a specific name
            public static GridHandle<BufferT> readGrid<BufferT>(string fileName, string gridName, int verbose, BufferT buffer)
                where BufferT : class, IGridBuffer<BufferT>, new()
            {
                using (Stream @is = OpenInput(fileName))
                {
                    GridHandle<BufferT> handle = readGrid<BufferT>(@is, gridName, buffer);
                    if (verbose != 0)
                    {
                        if (handle)
                            Console.Out.WriteLine("Read NanoGrid named \"" + gridName + "\" from the file named \"" + fileName + "\"");
                        else
                            Console.Out.WriteLine("File named \"" + fileName + "\" does not contain a grid named \"" + gridName + "\"");
                    }
                    return handle;
                }
            }

            public static GridHandle<BufferT> readGrid<BufferT>(Stream @is, string gridName, BufferT buffer)
                where BufferT : class, IGridBuffer<BufferT>, new()
            {
                ulong key = stringHash(gridName);
                Segment s = new Segment();
                while (s.read(@is))
                {
                    long seek = 0;
                    foreach (GridMetaData m in s.meta)
                    {
                        if (m.nameKey == key && m.gridName == gridName)  // check for hask key collision
                        {
                            GridHandle<BufferT> handle = new GridHandle<BufferT>(BufferT.create(m.gridSize, buffer));
                            @is.Seek(seek, SeekOrigin.Current);  // rewind
                            Internal_read(@is, handle, s.header.codec);
                            return handle;
                        }
                        else
                        {
                            seek += (long)m.fileSize;
                        }
                    }
                    @is.Seek(seek, SeekOrigin.Current);  // skip forward from the current position
                }
                return new GridHandle<BufferT>();  // empty handle
            }

            // Read all the grids
            public static List<GridHandle<BufferT>> readGrids<BufferT>(string fileName, int verbose, BufferT buffer)
                where BufferT : class, IGridBuffer<BufferT>, new()
            {
                using (Stream @is = OpenInput(fileName))
                {
                    List<GridHandle<BufferT>> handles = readGrids<BufferT>(@is, buffer);
                    if (verbose != 0)
                        Console.Out.WriteLine("Read " + handles.Count + " NanoGrid(s) from the file named \"" + fileName + "\"");
                    return handles;
                }
            }

            public static List<GridHandle<BufferT>> readGrids<BufferT>(Stream @is, BufferT buffer)
                where BufferT : class, IGridBuffer<BufferT>, new()
            {
                List<GridHandle<BufferT>> handles = new List<GridHandle<BufferT>>();
                Segment seg = new Segment();
                while (seg.read(@is))
                {
                    foreach (GridMetaData m in seg.meta)
                    {
                        GridHandle<BufferT> handle = new GridHandle<BufferT>(BufferT.create(m.gridSize, buffer));
                        Internal_read(@is, handle, seg.header.codec);
                        handles.Add(handle);
                    }
                }
                return handles;
            }

            public static List<GridMetaData> readGridMetaData(string fileName)
            {
                using (Stream @is = OpenInput(fileName))
                    return readGridMetaData(@is);
            }

            public static List<GridMetaData> readGridMetaData(Stream @is)
            {
                List<GridMetaData> meta = new List<GridMetaData>();
                Segment seg = new Segment();
                while (seg.read(@is))
                {
                    long seek = 0;
                    foreach (GridMetaData m in seg.meta)
                    {
                        meta.Add(m);
                        seek += (long)m.fileSize;
                    }
                    @is.Seek(seek, SeekOrigin.Current);
                }
                return meta;
            }

            public static bool hasGrid(string fileName, string gridName)
            {
                using (Stream @is = OpenInput(fileName))
                    return hasGrid(@is, gridName);
            }

            public static bool hasGrid(Stream @is, string gridName)
            {
                ulong key = stringHash(gridName);
                Segment s = new Segment();
                while (s.read(@is))
                {
                    long seek = 0;
                    foreach (GridMetaData m in s.meta)
                    {
                        if (m.nameKey == key && m.gridName == gridName)
                            return true;  // check for hash key collision
                        seek += (long)m.fileSize;
                    }
                    @is.Seek(seek, SeekOrigin.Current);
                }
                return false;
            }
        }
    }
}
