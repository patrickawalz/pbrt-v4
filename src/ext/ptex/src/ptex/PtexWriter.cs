// PtexWriter.h, PtexWriter.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

/* Ptex writer classes:

   PtexIncrWriter implements "incremental" mode and simply appends "edit" blocks to the end of
   the file.

   PtexMainWriter implements both writing from scratch and updating an existing file, either to
   add data or to "roll up" previous incremental edits.

   Because the various headers (faceinfo, levelinfo, etc.) are variable-length and precede the
   data, and because the data size is not known until it is compressed and written, all data are
   written to a temp file and then copied at the end to the final location. This happens during
   the "finish" phase.

   Each time a texture is written to the file, a reduction of the texture is also generated and
   stored. These reductions are stored in a temporary form and recalled later as the resolution
   levels are generated.

   The final reduction for each face is averaged and stored in the const data block.

   Port notes: FILE* is System.IO.FileStream; zlib's streaming deflate is replaced by
   System.IO.Compression.ZLibStream (the pending input of a not-yet-finished zip block is
   gathered and compressed when the block is finished). The compressed bytes are therefore not
   necessarily identical to those zlib produces, but are valid zlib streams the reader decodes.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Ptex
{
    public abstract unsafe class PtexWriterBase : PtexWriter
    {
        // ---- namespace-level helpers of PtexWriter.cpp ----
        static readonly object tmpLock = new object();
        static string tmpdir = null;
        static int tmpCounter = 0;

        protected static FileStream OpenTempFile(out string tmppath)
        {
            lock (tmpLock)
            {
                // choose temp dir
                if (tmpdir == null)
                {
                    // try $TEMP or $TMP, use /tmp as last resort
                    string t = Environment.GetEnvironmentVariable("TEMP");
                    if (t == null) t = Environment.GetEnvironmentVariable("TMP");
                    if (t == null) t = "/tmp";
                    tmpdir = t;
                }

                // build temp path (unique file, created exclusively like mkstemp)
                const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                Random rnd = new Random(Environment.ProcessId * 7919 + (++tmpCounter));
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    char[] suffix = new char[6];
                    for (int i = 0; i < 6; i++) suffix[i] = chars[rnd.Next(chars.Length)];
                    tmppath = tmpdir + "/PtexTmp" + new string(suffix);
                    try
                    {
                        return new FileStream(tmppath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096);
                    }
                    catch (IOException) { if (!File.Exists(tmppath)) break; }
                    catch (UnauthorizedAccessException) { break; }
                }
                tmppath = tmpdir + "/PtexTmpXXXXXX";
                return null;
            }
        }

        protected static string strerror(Exception e)
        {
            if (e is FileNotFoundException || e is DirectoryNotFoundException) return "No such file or directory";
            if (e is UnauthorizedAccessException) return "Permission denied";
            return e.Message;
        }

        protected static string fileError(string message, string path, Exception e = null)
        {
            return message + path + "\n" + (e != null ? strerror(e) : "Success");
        }

        static bool checkFormat(MeshType mt, DataType dt, int nchannels, int alphachan, ref string error)
        {
            // check to see if given file attributes are valid
            if (!PtexIO.LittleEndian())
            {
                error = "PtexWriter doesn't currently support big-endian cpu's";
                return false;
            }

            if (mt < MeshType.mt_triangle || mt > MeshType.mt_quad)
            {
                error = "PtexWriter error: Invalid mesh type";
                return false;
            }

            if (dt < DataType.dt_uint8 || dt > DataType.dt_float)
            {
                error = "PtexWriter error: Invalid data type";
                return false;
            }

            if (nchannels <= 0)
            {
                error = "PtexWriter error: Invalid number of channels";
                return false;
            }

            if (alphachan != -1 && (alphachan < 0 || alphachan >= nchannels))
            {
                error = "PtexWriter error: Invalid alpha channel";
                return false;
            }
            return true;
        }

        internal static bool CheckFormat(MeshType mt, DataType dt, int nchannels, int alphachan, ref string error)
        {
            return checkFormat(mt, dt, nchannels, alphachan, ref error);
        }

        // ---- PtexWriterBase ----
        public override void setBorderModes(BorderMode uBorderMode, BorderMode vBorderMode)
        {
            _extheader.ubordermode = (ushort)uBorderMode;
            _extheader.vbordermode = (ushort)vBorderMode;
        }

        public override void setEdgeFilterMode(EdgeFilterMode edgeFilterMode)
        {
            _extheader.edgefiltermode = (ushort)edgeFilterMode;
        }

        public bool ok(ref string error)
        {
            if (!_ok) getError(ref error);
            return _ok;
        }

        public void getError(ref string error)
        {
            error = _error + "\nPtex file: " + _path;
        }

        protected DataType datatype() { return (DataType)_header.datatype; }

        protected sealed class MetaEntry
        {
            public string key;
            public MetaDataType datatype;
            public byte[] data;
            public MetaEntry() { key = ""; datatype = (MetaDataType)0; data = new byte[0]; }
        }

        protected abstract void finish();

        protected PtexWriterBase(string path,
                                 MeshType mt, DataType dt,
                                 int nchannels, int alphachan, int nfaces,
                                 bool compress)
        {
            _ok = true;
            _path = path;
            _tilefp = null;
            _header = new Header();
            _header.magic = PtexIO.Magic;
            _header.version = PtexVersion.PtexFileMajorVersion;
            _header.minorversion = PtexVersion.PtexFileMinorVersion;
            _header.meshtype = (uint)mt;
            _header.datatype = (uint)dt;
            _header.alphachan = alphachan;
            _header.nchannels = (ushort)nchannels;
            _header.nfaces = (uint)nfaces;
            _header.nlevels = 0;
            _header.extheadersize = (uint)sizeof(ExtHeader);
            _pixelSize = _header.pixelSize();
            _extheader = new ExtHeader();

            if (mt == MeshType.mt_triangle)
                _reduceFn = PtexUtils.reduceTri;
            else
                _reduceFn = PtexUtils.reduce;

            _zlevel = compress ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
            _compress = compress;

            // create temp file for writing tiles
            // (must compress each tile before assembling a tiled face)
            _tilefp = OpenTempFile(out _tilepath);
            if (_tilefp == null)
            {
                setError(fileError("Error creating temp file: ", _tilepath));
            }
        }

        public override void release()
        {
            string error = "";
            // close writer if app didn't, and report error if any
            if (_tilefp != null && !close(ref error))
                Console.Error.WriteLine(error);
            destroy();
        }

        // ~PtexWriterBase (deflateEnd in the C++)
        protected virtual void destroy() { }

        public override bool close(ref string error)
        {
            if (_ok) finish();
            if (!_ok) getError(ref error);
            if (_tilefp != null)
            {
                _tilefp.Dispose();
                try { File.Delete(_tilepath); } catch (Exception) { }
                _tilefp = null;
            }
            return _ok;
        }

        protected bool storeFaceInfo(int faceid, ref FaceInfo f, in FaceInfo src, int flags = 0)
        {
            if (faceid < 0 || (ulong)faceid >= _header.nfaces)
            {
                setError("PtexWriter error: faceid out of range");
                return false;
            }
            if (_header.meshtype == (uint)MeshType.mt_triangle && (f.res.ulog2 != f.res.vlog2))
            {
                setError("PtexWriter error: asymmetric face res not supported for triangle textures");
                return false;
            }

            // copy all values
            f = src;

            // and clear extraneous ones
            if (_header.meshtype == (uint)MeshType.mt_triangle)
            {
                f.flags = 0;  // no user-settable flags on triangles
                f.adjfaces[3] = -1;
                f.adjedges &= 0x3f;  // clear all but bottom six bits
            }
            else
            {
                // clear non-user-settable flags
                f.flags &= FaceInfo.flag_subface;
            }

            // set new flags
            f.flags |= (byte)flags;
            return true;
        }

        public override void writeMeta(string key, string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            byte[] withNul = new byte[bytes.Length + 1];
            Array.Copy(bytes, withNul, bytes.Length);
            fixed (byte* p = withNul) addMetaData(key, MetaDataType.mdt_string, p, withNul.Length);
        }

        public override void writeMeta(string key, sbyte* value, int count)
        {
            addMetaData(key, MetaDataType.mdt_int8, value, count);
        }

        public override void writeMeta(string key, short* value, int count)
        {
            addMetaData(key, MetaDataType.mdt_int16, value, count * sizeof(short));
        }

        public override void writeMeta(string key, int* value, int count)
        {
            addMetaData(key, MetaDataType.mdt_int32, value, count * sizeof(int));
        }

        public override void writeMeta(string key, float* value, int count)
        {
            addMetaData(key, MetaDataType.mdt_float, value, count * sizeof(float));
        }

        public override void writeMeta(string key, double* value, int count)
        {
            addMetaData(key, MetaDataType.mdt_double, value, count * sizeof(double));
        }

        public override void writeMeta(PtexMetaData data)
        {
            int nkeys = data.numKeys();
            for (int i = 0; i < nkeys; i++)
            {
                string key = null;
                MetaDataType type;
                data.getKey(i, out key, out type);

                int count;
                switch (type)
                {
                    case MetaDataType.mdt_string:
                    {
                        string val = null;
                        data.getValue(key, out val);
                        writeMeta(key, val);
                    }
                    break;

                    case MetaDataType.mdt_int8:
                    {
                        sbyte* val = null;
                        data.getValue(key, out val, out count);
                        writeMeta(key, val, count);
                    }
                    break;

                    case MetaDataType.mdt_int16:
                    {
                        short* val = null;
                        data.getValue(key, out val, out count);
                        writeMeta(key, val, count);
                    }
                    break;

                    case MetaDataType.mdt_int32:
                    {
                        int* val = null;
                        data.getValue(key, out val, out count);
                        writeMeta(key, val, count);
                    }
                    break;

                    case MetaDataType.mdt_float:
                    {
                        float* val = null;
                        data.getValue(key, out val, out count);
                        writeMeta(key, val, count);
                    }
                    break;

                    case MetaDataType.mdt_double:
                    {
                        double* val = null;
                        data.getValue(key, out val, out count);
                        writeMeta(key, val, count);
                    }
                    break;
                }
            }
        }

        protected virtual void addMetaData(string key, MetaDataType t, void* value, int size)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(key) > 255)
            {
                setError("PtexWriter error: meta data key too long (max=255) \"" + key + "\"");
                return;
            }

            if (size <= 0)
            {
                setError("PtexWriter error: meta data size <= 0 for \"" + key + "\"");
            }

            int index;
            if (_metamap.TryGetValue(key, out index))
            {
                // see if we already have this entry - if so, overwrite it
            }
            else
            {
                // allocate a new entry
                index = _metadata.Count;
                _metadata.Add(new MetaEntry());
                _metamap[key] = index;
            }

            MetaEntry m = _metadata[index];
            m.key = key;
            m.datatype = t;
            m.data = new byte[Math.Max(size, 0)];
            if (size > 0) System.Runtime.InteropServices.Marshal.Copy((IntPtr)value, m.data, 0, size);
        }

        // ---- stdio equivalents ----
        static bool fwrite(FileStream fp, void* data, int size)
        {
            if (size <= 0) return false;  // fwrite(data, size, 1, fp) returns 0 for size 0
            try { fp.Write(new ReadOnlySpan<byte>(data, size)); return true; }
            catch (IOException) { return false; }
        }

        static bool fread(FileStream fp, void* data, int size)
        {
            if (size <= 0) return false;
            int total = 0;
            byte* dst = (byte*)data;
            while (total < size)
            {
                int n = fp.Read(new Span<byte>(dst + total, size - total));
                if (n <= 0) return false;
                total += n;
            }
            return true;
        }

        protected static long ftello(FileStream fp) { return fp.Position; }
        protected static void fseeko(FileStream fp, long pos, SeekOrigin origin) { fp.Seek(pos, origin); }
        protected static void rewind(FileStream fp) { fp.Seek(0, SeekOrigin.Begin); }

        protected int writeBlank(FileStream fp, int size)
        {
            if (!_ok) return 0;

            byte* zeros = stackalloc byte[PtexIO.BlockSize];
            for (int i = 0; i < PtexIO.BlockSize; i++) zeros[i] = 0;
            int remain = size;
            while (remain > 0)
            {
                remain -= writeBlock(fp, zeros, remain < PtexIO.BlockSize ? remain : PtexIO.BlockSize);
            }
            return size;
        }

        protected int writeBlock(FileStream fp, void* data, int size)
        {
            if (!_ok) return 0;
            if (!fwrite(fp, data, size))
            {
                setError("PtexWriter error: file write failed");
                return 0;
            }
            return size;
        }

        // Add data to the current zip block; if finish is true the block is compressed, streamed to
        // the file and its compressed size is returned.
        protected int writeZipBlock(FileStream fp, void* data, int size, bool finishArg = true)
        {
            if (!_ok) return 0;

            if (size > 0) _zin.Write(new ReadOnlySpan<byte>(data, size));
            if (!finishArg) return 0;

            byte[] input = _zin.ToArray();
            _zin.SetLength(0);
            byte[] output;
            if (input.Length == 0)
            {
                // (an empty zlib stream; ZLibStream emits nothing for empty input)
                output = _compress
                    ? new byte[] { 0x78, 0x9c, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 }
                    : new byte[] { 0x78, 0x01, 0x01, 0x00, 0x00, 0xff, 0xff, 0x00, 0x00, 0x00, 0x01 };
            }
            else
            {
                MemoryStream ms = new MemoryStream();
                using (ZLibStream zs = new ZLibStream(ms, _zlevel, true))
                    zs.Write(input, 0, input.Length);
                output = ms.ToArray();
            }

            fixed (byte* op = output)
            {
                int pos = 0;
                while (pos < output.Length)
                {
                    int sizeval = Math.Min(PtexIO.BlockSize, output.Length - pos);
                    writeBlock(fp, op + pos, sizeval);
                    pos += sizeval;
                }
            }
            return output.Length;
        }

        protected int readBlock(FileStream fp, void* data, int size)
        {
            if (!fread(fp, data, size))
            {
                setError("PtexWriter error: temp file read failed");
                return 0;
            }
            return size;
        }

        protected int copyBlock(FileStream dst, FileStream src, long pos, int size)
        {
            if (size <= 0) return 0;
            fseeko(src, pos, SeekOrigin.Begin);
            int remain = size;
            byte* buff = stackalloc byte[PtexIO.BlockSize];
            while (remain != 0)
            {
                int nbytes = remain < PtexIO.BlockSize ? remain : PtexIO.BlockSize;
                if (!fread(src, buff, nbytes))
                {
                    setError("PtexWriter error: temp file read failed");
                    return 0;
                }
                if (writeBlock(dst, buff, nbytes) == 0) break;
                remain -= nbytes;
            }
            return size;
        }

        protected Res calcTileRes(Res faceres)
        {
            // desired number of tiles = floor(log2(facesize / tilesize))
            int facesize = faceres.size() * _pixelSize;
            int ntileslog2 = (int)PtexUtils.floor_log2((uint)(facesize / PtexIO.TileSize));
            if (ntileslog2 == 0) return faceres;

            // number of tiles is defined as:
            //   ntileslog2 = ureslog2 + vreslog2 - (tile_ureslog2 + tile_vreslog2)
            // rearranging to solve for the tile res:
            //   tile_ureslog2 + tile_vreslog2 = ureslog2 + vreslog2 - ntileslog2
            int n = faceres.ulog2 + faceres.vlog2 - ntileslog2;

            // choose u and v sizes for roughly square result (u ~= v ~= n/2)
            // and make sure tile isn't larger than face
            Res tileres = new Res((ushort)0);
            tileres.ulog2 = (sbyte)PtexUtils.min((n + 1) / 2, (int)faceres.ulog2);
            tileres.vlog2 = (sbyte)PtexUtils.min(n - tileres.ulog2, (int)faceres.vlog2);
            return tileres;
        }

        protected void writeConstFaceBlock(FileStream fp, void* data, ref FaceDataHeader fdh)
        {
            // write a single const face data block
            // record level data for face and output the one pixel value
            fdh.set((uint)_pixelSize, Encoding_enc_constant);
            writeBlock(fp, data, _pixelSize);
        }

        const Ptex.Encoding Encoding_enc_constant = Ptex.Encoding.enc_constant;

        protected void writeFaceBlock(FileStream fp, void* data, int stride, Res res, ref FaceDataHeader fdh)
        {
            // write a single face data block
            // copy to temp buffer, and deinterleave
            int ures = res.u(), vres = res.v();
            int blockSize = ures * vres * _pixelSize;
            byte* buff = PtexMem.NewBytes(blockSize);
            PtexUtils.deinterleave(data, stride, ures, vres, buff,
                                   ures * PtexGlobals.DataSize(datatype()),
                                   datatype(), _header.nchannels);

            // difference if needed
            bool diff = (datatype() == DataType.dt_uint8 ||
                         datatype() == DataType.dt_uint16);
            if (diff) PtexUtils.encodeDifference(buff, blockSize, datatype());

            // compress and stream data to file, and record size in header
            int zippedsize = writeZipBlock(fp, buff, blockSize);

            // record compressed size and encoding in data header
            fdh.set((uint)zippedsize, diff ? Ptex.Encoding.enc_diffzipped : Ptex.Encoding.enc_zipped);
            PtexMem.Delete(buff);
        }

        protected void writeFaceData(FileStream fp, void* data, int stride, Res res, ref FaceDataHeader fdh)
        {
            // determine whether to break into tiles
            Res tileres = calcTileRes(res);
            int ntilesu = res.ntilesu(tileres);
            int ntilesv = res.ntilesv(tileres);
            int ntiles = ntilesu * ntilesv;
            if (ntiles == 1)
            {
                // write single block
                writeFaceBlock(fp, data, stride, res, ref fdh);
            }
            else
            {
                // write tiles to tilefp temp file
                rewind(_tilefp);

                // alloc tile header
                FaceDataHeader[] tileHeader = new FaceDataHeader[ntiles];
                int tileures = tileres.u();
                int tilevres = tileres.v();
                int tileustride = tileures * _pixelSize;
                int tilevstride = tilevres * stride;

                // output tiles
                int tdh = 0;
                int datasize = 0;
                byte* rowp = (byte*)data;
                byte* rowpend = rowp + ntilesv * tilevstride;
                for (; rowp != rowpend; rowp += tilevstride)
                {
                    byte* p = rowp;
                    byte* pend = p + ntilesu * tileustride;
                    for (; p != pend; tdh++, p += tileustride)
                    {
                        // determine if tile is constant
                        if (PtexUtils.isConstant(p, stride, tileures, tilevres, _pixelSize))
                            writeConstFaceBlock(_tilefp, p, ref tileHeader[tdh]);
                        else
                            writeFaceBlock(_tilefp, p, stride, tileres, ref tileHeader[tdh]);
                        datasize += (int)tileHeader[tdh].blocksize();
                    }
                }

                // output compressed tile header
                uint tileheadersize;
                fixed (FaceDataHeader* thp = tileHeader)
                    tileheadersize = (uint)writeZipBlock(_tilefp, thp, sizeof(FaceDataHeader) * tileHeader.Length);

                // output tile data pre-header
                int totalsize = 0;
                totalsize += writeBlock(fp, &tileres, sizeof(Res));
                totalsize += writeBlock(fp, &tileheadersize, sizeof(uint));

                // copy compressed tile header from temp file
                totalsize += copyBlock(fp, _tilefp, datasize, (int)tileheadersize);

                // copy tile data from temp file
                totalsize += copyBlock(fp, _tilefp, 0, datasize);

                fdh.set((uint)totalsize, Ptex.Encoding.enc_tiled);
            }
        }

        protected void writeReduction(FileStream fp, void* data, int stride, Res res)
        {
            // reduce and write to file
            Res newres = new Res((sbyte)(res.ulog2 - 1), (sbyte)(res.vlog2 - 1));
            int buffsize = newres.size() * _pixelSize;
            byte* buff = PtexMem.NewBytes(buffsize);

            int dstride = newres.u() * _pixelSize;
            _reduceFn(data, stride, res.u(), res.v(), buff, dstride, datatype(), _header.nchannels);
            writeBlock(fp, buff, buffsize);
            PtexMem.Delete(buff);
        }

        protected int writeMetaDataBlock(FileStream fp, MetaEntry val)
        {
            byte[] keybytes = System.Text.Encoding.UTF8.GetBytes(val.key);
            byte keysize = (byte)(keybytes.Length + 1);
            byte[] keyz = new byte[keysize];
            Array.Copy(keybytes, keyz, keybytes.Length);
            byte datatypeval = (byte)val.datatype;
            uint datasize = (uint)val.data.Length;
            writeZipBlock(fp, &keysize, sizeof(byte), false);
            fixed (byte* kp = keyz) writeZipBlock(fp, kp, keysize, false);
            writeZipBlock(fp, &datatypeval, sizeof(byte), false);
            writeZipBlock(fp, &datasize, sizeof(uint), false);
            fixed (byte* dp = val.data) writeZipBlock(fp, dp, (int)datasize, false);
            int memsize = (int)(sizeof(byte) + (uint)keysize + sizeof(byte)
                                + sizeof(uint) + datasize);
            return memsize;
        }

        protected void setError(string error) { _error = error; _ok = false; }

        protected bool _ok;                        // true if no error has occurred
        protected string _error = "";              // the error text (if any)
        protected string _path;                    // file path
        protected string _tilepath;                // temp tile file path ("<path>.tiles.tmp")
        protected FileStream _tilefp;              // temp tile file handle
        protected Header _header;                  // the file header
        protected ExtHeader _extheader;            // extended header
        protected int _pixelSize;                  // size of a pixel in bytes
        protected List<MetaEntry> _metadata = new List<MetaEntry>();          // meta data waiting to be written
        protected Dictionary<string, int> _metamap = new Dictionary<string, int>();  // for preventing duplicate keys
        protected MemoryStream _zin = new MemoryStream();   // pending input of the current zip block
        protected CompressionLevel _zlevel;
        protected bool _compress;
        protected ReduceFn _reduceFn;
    }

    public unsafe class PtexMainWriter : PtexWriterBase
    {
        public PtexMainWriter(string path, PtexTexture tex,
                              MeshType mt, DataType dt,
                              int nchannels, int alphachan, int nfaces, bool genmipmaps)
            : base(path, mt, dt, nchannels, alphachan, nfaces,
                   /* compress */ true)
        {
            _hasNewData = false;
            _genmipmaps = genmipmaps;
            _reader = null;

            _tmpfp = OpenTempFile(out _tmppath);
            if (_tmpfp == null)
            {
                setError(fileError("Error creating temp file: ", _tmppath));
                return;
            }

            // data will be written to a ".new" path and then renamed to final location
            _newpath = path; _newpath += ".new";

            _levels.Add(new LevelRec());

            // init faceinfo and set flags to -1 to mark as uninitialized
            _faceinfo = new FaceInfo[nfaces];
            for (int i = 0; i < nfaces; i++) { _faceinfo[i] = FaceInfo.Default(); _faceinfo[i].flags = 0xff; }
            _levels[0].pos = new long[nfaces];
            _levels[0].fdh = new FaceDataHeader[nfaces];
            _rpos = new long[nfaces];
            _constdata = new byte[nfaces * _pixelSize];

            if (tex != null)
            {
                // access reader implementation
                // Note: we can assume we have a PtexReader because we opened the tex from the cache
                _reader = (PtexReader)tex;

                // copy border modes
                setBorderModes(tex.uBorderMode(), tex.vBorderMode());

                // copy edge filter mode
                setEdgeFilterMode(tex.edgeFilterMode());

                // copy meta data from existing file
                using (PtexPtr<PtexMetaData> meta = new PtexPtr<PtexMetaData>(_reader.getMetaData()))
                    writeMeta(meta.get());

                // see if we have any edits
                _hasNewData = _reader.hasEdits();
            }
        }

        protected override void destroy()
        {
            if (_reader != null) _reader.release();
        }

        public override bool close(ref string error)
        {
            // closing base writer will write all pending data via finish() method
            // and will close _fp (which in this case is on the temp disk)
            bool result = base.close(ref error);

            if (_reader != null)
            {
                _reader.release();
                _reader = null;
            }

            if (_tmpfp != null)
            {
                _tmpfp.Dispose();
                try { File.Delete(_tmppath); } catch (Exception) { }
                _tmpfp = null;
            }

            if (result && _hasNewData)
            {
                // rename temppath into final location
                try { File.Delete(_path); } catch (Exception) { }
                try
                {
                    File.Move(_newpath, _path);
                }
                catch (Exception e)
                {
                    error = fileError("Can't write to ptex file: ", _path, e);
                    try { File.Delete(_newpath); } catch (Exception) { }
                    result = false;
                }
            }

            return result;
        }

        public override bool writeFace(int faceid, in FaceInfo f, void* data, int stride)
        {
            if (!_ok) return false;

            // auto-compute stride
            if (stride == 0) stride = f.res.u() * _pixelSize;

            // handle constant case
            if (PtexUtils.isConstant(data, stride, f.res.u(), f.res.v(), _pixelSize))
                return writeConstantFace(faceid, f, data);

            // non-constant case, ...

            // check and store face info
            if (!storeFaceInfo(faceid, ref _faceinfo[faceid], f)) return false;

            // record position of current face
            _levels[0].pos[faceid] = ftello(_tmpfp);

            // write face data
            writeFaceData(_tmpfp, data, stride, f.res, ref _levels[0].fdh[faceid]);
            if (!_ok) return false;

            // premultiply (if needed) before making reductions; use temp copy of data
            byte* temp = null;
            if (_header.hasAlpha())
            {
                // first copy to temp buffer
                int rowlen = f.res.u() * _pixelSize, nrows = f.res.v();
                temp = PtexMem.NewBytes(rowlen * nrows);
                PtexUtils.copy(data, stride, temp, rowlen, nrows, rowlen);

                // multiply alpha
                PtexUtils.multalpha(temp, f.res.size(), datatype(), _header.nchannels,
                                    _header.alphachan);

                // override source buffer
                data = temp;
                stride = rowlen;
            }

            // generate first reduction (if needed)
            if (_genmipmaps &&
                (f.res.ulog2 > MinReductionLog2 && f.res.vlog2 > MinReductionLog2))
            {
                _rpos[faceid] = ftello(_tmpfp);
                writeReduction(_tmpfp, data, stride, f.res);
            }
            else
            {
                storeConstValue(faceid, data, stride, f.res);
            }

            if (temp != null) PtexMem.Delete(temp);
            _hasNewData = true;
            return true;
        }

        public override bool writeConstantFace(int faceid, in FaceInfo f, void* data)
        {
            if (!_ok) return false;

            // check and store face info
            if (!storeFaceInfo(faceid, ref _faceinfo[faceid], f, FaceInfo.flag_constant)) return false;

            // store face value in constant block
            for (int i = 0; i < _pixelSize; i++) _constdata[faceid * _pixelSize + i] = ((byte*)data)[i];
            _hasNewData = true;
            return true;
        }

        protected override void addMetaData(string key, MetaDataType t, void* value, int size)
        {
            base.addMetaData(key, t, value, size);
            _hasNewData = true;
        }

        void storeConstValue(int faceid, void* data, int stride, Res res)
        {
            // compute average value and store in _constdata block
            fixed (byte* cd = &_constdata[faceid * _pixelSize])
            {
                byte* constdata = cd;
                PtexUtils.average(data, stride, res.u(), res.v(), constdata,
                                  datatype(), _header.nchannels);
                if (_header.hasAlpha())
                    PtexUtils.divalpha(constdata, 1, datatype(), _header.nchannels, _header.alphachan);
            }
        }

        protected override void finish()
        {
            // do nothing if there's no new data to write
            if (!_hasNewData) return;

            // copy missing faces from _reader
            if (_reader != null)
            {
                for (int i = 0, nfaces = (int)_header.nfaces; i < nfaces; i++)
                {
                    if (_faceinfo[i].flags == 0xff)
                    {
                        // copy face data
                        FaceInfo info = _reader.getFaceInfo(i);
                        int size = _pixelSize * info.res.size();
                        if (info.isConstant())
                        {
                            using (PtexPtr<PtexFaceData> data = new PtexPtr<PtexFaceData>(_reader.getData(i)))
                            {
                                if (data.get() != null)
                                {
                                    writeConstantFace(i, info, data.get().getData());
                                }
                            }
                        }
                        else
                        {
                            byte* data = PtexMem.NewBytes(size);
                            _reader.getData(i, data, 0);
                            writeFace(i, info, data, 0);
                            PtexMem.Delete(data);
                        }
                    }
                }
            }
            else
            {
                // just flag missing faces as constant (black)
                for (int i = 0, nfaces = (int)_header.nfaces; i < nfaces; i++)
                {
                    if (_faceinfo[i].flags == 0xff)
                        _faceinfo[i].flags = FaceInfo.flag_constant;
                }
            }

            // write reductions to tmp file
            if (_genmipmaps)
                generateReductions();

            // flag faces w/ constant neighborhoods
            flagConstantNeighorhoods();

            // update header
            _header.nlevels = (ushort)_levels.Count;
            _header.nfaces = (uint)_faceinfo.Length;

            // create new file
            FileStream newfp;
            try
            {
                newfp = new FileStream(_newpath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096);
            }
            catch (Exception e)
            {
                setError(fileError("Can't write to ptex file: ", _newpath, e));
                return;
            }

            // write blank header (to fill in later)
            writeBlank(newfp, PtexIO.HeaderSize);
            writeBlank(newfp, PtexIO.ExtHeaderSize);

            // write compressed face info block
            fixed (FaceInfo* fip = _faceinfo)
                _header.faceinfosize = (uint)writeZipBlock(newfp, fip, sizeof(FaceInfo) * (int)_header.nfaces);

            // write compressed const data block
            fixed (byte* cdp = _constdata)
                _header.constdatasize = (uint)writeZipBlock(newfp, cdp, _constdata.Length);

            // write blank level info block (to fill in later)
            long levelInfoPos = ftello(newfp);
            writeBlank(newfp, PtexIO.LevelInfoSize * _header.nlevels);

            // write level data blocks (and record level info)
            LevelInfo[] levelinfo = new LevelInfo[_header.nlevels];
            for (int li = 0; li < _header.nlevels; li++)
            {
                LevelRec level = _levels[li];
                int nfaces = level.fdh.Length;
                levelinfo[li].nfaces = (uint)nfaces;

                // output compressed level data header
                fixed (FaceDataHeader* fdp = level.fdh)
                    levelinfo[li].levelheadersize = (uint)writeZipBlock(newfp, fdp, sizeof(FaceDataHeader) * nfaces);
                levelinfo[li].leveldatasize = levelinfo[li].levelheadersize;

                // copy level data from tmp file
                for (int fi = 0; fi < nfaces; fi++)
                    levelinfo[li].leveldatasize += (ulong)copyBlock(newfp, _tmpfp, level.pos[fi],
                                                                    (int)level.fdh[fi].blocksize());
                _header.leveldatasize += levelinfo[li].leveldatasize;
            }
            rewind(_tmpfp);

            // write meta data (if any)
            if (_metadata.Count != 0)
                writeMetaData(newfp);

            // update extheader for edit data position
            _extheader.editdatapos = (ulong)ftello(newfp);

            // rewrite level info block
            fseeko(newfp, levelInfoPos, SeekOrigin.Begin);
            fixed (LevelInfo* lip = levelinfo)
                _header.levelinfosize = (uint)writeBlock(newfp, lip, PtexIO.LevelInfoSize * _header.nlevels);

            // rewrite header
            fseeko(newfp, 0, SeekOrigin.Begin);
            fixed (Header* hp = &_header) writeBlock(newfp, hp, PtexIO.HeaderSize);
            fixed (ExtHeader* ep = &_extheader) writeBlock(newfp, ep, PtexIO.ExtHeaderSize);
            newfp.Dispose();
        }

        void flagConstantNeighorhoods()
        {
            // for each constant face
            for (int faceid = 0, n = _faceinfo.Length; faceid < n; faceid++)
            {
                if (!_faceinfo[faceid].isConstant()) continue;
                int constoff = faceid * _pixelSize;

                // check to see if neighborhood is constant
                bool isConst = true;
                bool isTriangle = _header.meshtype == (uint)MeshType.mt_triangle;
                int nedges = isTriangle ? 3 : 4;
                for (int eid = 0; isConst && (eid < nedges); eid++)
                {
                    bool prevWasSubface = _faceinfo[faceid].isSubface();
                    int prevFid = faceid;

                    // traverse around vertex in CW direction
                    int afid = _faceinfo[faceid].adjface(eid);
                    int aeid = (int)_faceinfo[faceid].adjedge(eid);
                    int count = 0;
                    const int maxcount = 10;  // max valence (as safety valve)
                    while (afid != faceid && afid >= 0 && ++count < maxcount)
                    {
                        // check if neighbor is constant, and has the same value as face
                        FaceInfo af = _faceinfo[afid];
                        if (!af.isConstant() || !constEqual(constoff, afid * _pixelSize))
                        { isConst = false; break; }

                        // if vertex is a T vertex between subface and main face, we can stop
                        bool isSubface = af.isSubface();
                        bool isT = !isTriangle && prevWasSubface && !isSubface && af.adjface(aeid) == prevFid;
                        if (isT) break;
                        prevWasSubface = isSubface;

                        // traverse around vertex in CW direction
                        prevFid = afid;
                        aeid = (aeid + 1) % nedges;
                        afid = af.adjface(aeid);
                        aeid = (int)af.adjedge(aeid);
                    }

                    if (afid < 0)
                    {
                        // hit boundary edge, check boundary mode
                        if (_extheader.ubordermode != (ushort)BorderMode.m_clamp || _extheader.vbordermode != (ushort)BorderMode.m_clamp)
                        {
                            isConst = false;
                        }

                        // and traverse CCW neighbors too
                        if (isConst)
                        {
                            aeid = (aeid - 1 + nedges) % nedges;
                            afid = _faceinfo[faceid].adjface(aeid);
                            aeid = (int)_faceinfo[faceid].adjedge(aeid);
                            count = 0;
                            while (afid != faceid && afid >= 0 && ++count < maxcount)
                            {
                                // check if neighbor is constant, and has the same value as face
                                FaceInfo af = _faceinfo[afid];
                                if (!af.isConstant() || !constEqual(constoff, afid * _pixelSize))
                                { isConst = false; break; }

                                // traverse around vertex in CCW direction
                                prevFid = afid;
                                aeid = (aeid - 1 + nedges) % nedges;
                                afid = af.adjface(aeid);
                                aeid = (int)af.adjedge(aeid);

                                // if traversing to a subface, switch to secondary subface (afid points to primary/CW subface)
                                bool isSubface = af.isSubface();
                                if (isSubface && !prevWasSubface)
                                {
                                    aeid = (aeid + 3) % 4;
                                    afid = af.adjface(aeid);
                                    aeid = ((int)af.adjedge(aeid) + 3) % 4;
                                }
                                prevWasSubface = isSubface;
                            }
                        }
                    }
                }
                if (isConst) _faceinfo[faceid].flags |= FaceInfo.flag_nbconstant;
            }
        }

        bool constEqual(int a, int b)
        {
            for (int i = 0; i < _pixelSize; i++) if (_constdata[a + i] != _constdata[b + i]) return false;
            return true;
        }

        void generateReductions()
        {
            // first generate "rfaceids", reduction faceids,
            // which are faceids reordered by decreasing smaller dimension
            int nfaces = (int)_header.nfaces;
            _rfaceids = new uint[nfaces];
            _faceids_r = new uint[nfaces];
            fixed (FaceInfo* fip = _faceinfo)
            fixed (uint* rp = _rfaceids)
            fixed (uint* fr = _faceids_r)
                PtexUtils.genRfaceids(fip, nfaces, rp, fr);

            // determine how many faces in each level, and resize _levels
            // traverse in reverse rfaceid order to find number of faces
            // larger than cutoff size of each level
            for (int rfaceid = nfaces - 1, cutoffres = MinReductionLog2; rfaceid >= 0; rfaceid--)
            {
                int faceid = (int)_faceids_r[rfaceid];
                FaceInfo face = _faceinfo[faceid];
                Res res = face.res;
                int minres = face.isConstant() ? 1 : PtexUtils.min((int)res.ulog2, (int)res.vlog2);
                while (minres > cutoffres)
                {
                    // i == last face for current level
                    int size = rfaceid + 1;
                    LevelRec level = new LevelRec();
                    _levels.Add(level);
                    level.pos = new long[size];
                    level.fdh = new FaceDataHeader[size];
                    cutoffres++;
                }
            }

            // generate and cache reductions (including const data)

            // first, find largest face and allocate tmp buffer
            int buffsize = 0;
            for (int i = 0; i < nfaces; i++)
                buffsize = PtexUtils.max(buffsize, _faceinfo[i].res.size());
            buffsize *= _pixelSize;
            byte* buff = PtexMem.NewBytes(buffsize);

            int nlevels = _levels.Count;
            for (int i = 1; i < nlevels; i++)
            {
                LevelRec level = _levels[i];
                int nextsize = (i + 1 < nlevels) ? _levels[i + 1].fdh.Length : 0;
                for (int rfaceid = 0, size = level.fdh.Length; rfaceid < size; rfaceid++)
                {
                    // output current reduction for face (previously generated)
                    int faceid = (int)_faceids_r[rfaceid];
                    Res res = _faceinfo[faceid].res;
                    res.ulog2 = (sbyte)(res.ulog2 - i);
                    res.vlog2 = (sbyte)(res.vlog2 - i);
                    int stride = res.u() * _pixelSize;
                    int blocksize = res.size() * _pixelSize;
                    fseeko(_tmpfp, _rpos[faceid], SeekOrigin.Begin);
                    readBlock(_tmpfp, buff, blocksize);
                    fseeko(_tmpfp, 0, SeekOrigin.End);
                    level.pos[rfaceid] = ftello(_tmpfp);
                    writeFaceData(_tmpfp, buff, stride, res, ref level.fdh[rfaceid]);
                    if (!_ok) { PtexMem.Delete(buff); return; }

                    // write a new reduction if needed for next level
                    if (rfaceid < nextsize)
                    {
                        fseeko(_tmpfp, _rpos[faceid], SeekOrigin.Begin);
                        writeReduction(_tmpfp, buff, stride, res);
                    }
                    else
                    {
                        // the last reduction for each face is its constant value
                        storeConstValue(faceid, buff, stride, res);
                    }
                }
            }
            fseeko(_tmpfp, 0, SeekOrigin.End);
            PtexMem.Delete(buff);
        }

        void writeMetaData(FileStream fp)
        {
            List<MetaEntry> lmdEntries = new List<MetaEntry>();  // large meta data items

            // write small meta data items in a single zip block
            for (int i = 0, n = _metadata.Count; i < n; i++)
            {
                MetaEntry e = _metadata[i];
                if (e.data.Length > PtexIO.MetaDataThreshold)
                {
                    // skip large items, but record for later
                    lmdEntries.Add(e);
                }
                else
                {
                    // add small item to zip block
                    _header.metadatamemsize += (uint)writeMetaDataBlock(fp, e);
                }
            }

            if (_header.metadatamemsize != 0)
            {
                // finish zip block
                _header.metadatazipsize = (uint)writeZipBlock(fp, null, 0, /*finish*/ true);
            }

            // write compatibility barrier
            writeBlank(fp, sizeof(ulong));

            // write large items as separate blocks
            int nLmd = lmdEntries.Count;
            if (nLmd > 0)
            {
                // write data records to tmp file and accumulate zip sizes for lmd header
                long[] lmdoffset = new long[nLmd];
                uint[] lmdzipsize = new uint[nLmd];
                for (int i = 0; i < nLmd; i++)
                {
                    MetaEntry e = lmdEntries[i];
                    lmdoffset[i] = ftello(_tmpfp);
                    fixed (byte* dp = e.data)
                        lmdzipsize[i] = (uint)writeZipBlock(_tmpfp, dp, e.data.Length);
                }

                // write lmd header records as single zip block
                for (int i = 0; i < nLmd; i++)
                {
                    MetaEntry e = lmdEntries[i];
                    byte[] keybytes = System.Text.Encoding.UTF8.GetBytes(e.key);
                    byte keysize = (byte)(keybytes.Length + 1);
                    byte[] keyz = new byte[keysize];
                    Array.Copy(keybytes, keyz, keybytes.Length);
                    byte datatypeval = (byte)e.datatype;
                    uint datasize = (uint)e.data.Length;
                    uint zipsize = lmdzipsize[i];
                    writeZipBlock(fp, &keysize, sizeof(byte), false);
                    fixed (byte* kp = keyz) writeZipBlock(fp, kp, keysize, false);
                    writeZipBlock(fp, &datatypeval, sizeof(byte), false);
                    writeZipBlock(fp, &datasize, sizeof(uint), false);
                    writeZipBlock(fp, &zipsize, sizeof(uint), false);
                    _extheader.lmdheadermemsize +=
                        (uint)(sizeof(byte) + (uint)keysize + sizeof(byte) +
                               sizeof(uint) + sizeof(uint));
                }
                _extheader.lmdheaderzipsize = (uint)writeZipBlock(fp, null, 0, /*finish*/ true);

                // copy data records
                for (int i = 0; i < nLmd; i++)
                {
                    _extheader.lmddatasize +=
                        (ulong)copyBlock(fp, _tmpfp, lmdoffset[i], (int)lmdzipsize[i]);
                }
            }
        }

        string _newpath;                  // path to ".new" file
        string _tmppath;                  // temp file path ("<path>.tmp")
        FileStream _tmpfp;                // temp file handle
        bool _hasNewData;                 // true if data has been written
        bool _genmipmaps;                 // true if mipmaps should be generated
        FaceInfo[] _faceinfo;             // info about each face
        byte[] _constdata;                // constant data for each face
        uint[] _rfaceids;                 // faceid reordering for reduction levels
        uint[] _faceids_r;                // faceid indexed by rfaceid
        const int MinReductionLog2 = 2;   // log2(minimum reduction size) - can tune

        sealed class LevelRec
        {
            // note: level 0 is ordered by faceid
            //       levels 1+ are reduction levels (half res in both u and v) and
            //       are ordered by rfaceid[faceid].   Also, faces with a minimum
            //       dimension (the smaller of u or v) smaller than MinReductionLog2
            //       are omitted from subsequent levels.
            public long[] pos = new long[0];                // position of data blocks within _tmp file
            public FaceDataHeader[] fdh = new FaceDataHeader[0];  // face data headers
        }
        List<LevelRec> _levels = new List<LevelRec>();      // info about each level
        long[] _rpos;                     // reduction file positions
        PtexReader _reader;               // reader for accessing existing data in file
    }

    public unsafe class PtexIncrWriter : PtexWriterBase
    {
        public PtexIncrWriter(string path, FileStream fp,
                              MeshType mt, DataType dt,
                              int nchannels, int alphachan, int nfaces)
            : base(path, mt, dt, nchannels, alphachan, nfaces,
                   /* compress */ false)
        {
            _fp = fp;

            // note: incremental saves are not compressed (see compress flag above)
            // to improve save time in the case where in incremental save is followed by
            // a full save (which ultimately it always should be).  With a compressed
            // incremental save, the data would be compressed twice and decompressed once
            // on every save vs. just compressing once.

            // make sure existing header matches
            Header hdr = new Header();
            bool readOk = ReadHeader(fp, &hdr, PtexIO.HeaderSize);
            _header = hdr;
            if (!readOk || _header.magic != PtexIO.Magic)
            {
                setError("Not a ptex file: " + path);
                return;
            }

            bool headerMatch = ((uint)mt == _header.meshtype &&
                                dt == datatype() &&
                                nchannels == _header.nchannels &&
                                alphachan == _header.alphachan &&
                                nfaces == (int)_header.nfaces);
            if (!headerMatch)
            {
                setError("PtexWriter::edit error: header doesn't match existing file, " +
                         "conversions not currently supported");
                return;
            }

            // read extended header
            _extheader = new ExtHeader();
            ExtHeader eh = new ExtHeader();
            if (!ReadHeader(fp, &eh, PtexUtils.min((int)PtexIO.ExtHeaderSize, (int)_header.extheadersize)))
            {
                setError("Error reading extended header: " + path);
                return;
            }
            _extheader = eh;

            // seek to end of file to append
            fseeko(_fp, 0, SeekOrigin.End);
        }

        static bool ReadHeader(FileStream fp, void* dst, int size)
        {
            if (size <= 0) return false;
            int total = 0;
            byte* d = (byte*)dst;
            while (total < size)
            {
                int n = fp.Read(new Span<byte>(d + total, size - total));
                if (n <= 0) return false;
                total += n;
            }
            return true;
        }

        public override bool writeFace(int faceid, in FaceInfo f, void* data, int stride)
        {
            if (stride == 0) stride = f.res.u() * _pixelSize;

            // handle constant case
            if (PtexUtils.isConstant(data, stride, f.res.u(), f.res.v(), _pixelSize))
                return writeConstantFace(faceid, f, data);

            // init headers
            byte edittype = (byte)EditType.et_editfacedata;
            uint editsize;
            EditFaceDataHeader efdh = new EditFaceDataHeader();
            efdh.faceid = (uint)faceid;

            // check and store face info
            if (!storeFaceInfo(faceid, ref efdh.faceinfo, f))
                return false;

            // record position and skip headers
            long pos = ftello(_fp);
            writeBlank(_fp, sizeof(byte) + sizeof(uint) + sizeof(EditFaceDataHeader));

            // must compute constant (average) val first
            byte* constval = PtexMem.NewBytes(_pixelSize);
            if (_header.hasAlpha())
            {
                // must premult alpha before averaging

                // first copy to temp buffer
                int rowlen = f.res.u() * _pixelSize, nrows = f.res.v();
                byte* temp = PtexMem.NewBytes(rowlen * nrows);
                PtexUtils.copy(data, stride, temp, rowlen, nrows, rowlen);

                // multiply alpha
                PtexUtils.multalpha(temp, f.res.size(), datatype(), _header.nchannels,
                                    _header.alphachan);

                // average
                PtexUtils.average(temp, rowlen, f.res.u(), f.res.v(), constval,
                                  datatype(), _header.nchannels);

                // unmult alpha
                PtexUtils.divalpha(constval, 1, datatype(), _header.nchannels,
                                   _header.alphachan);
                PtexMem.Delete(temp);
            }
            else
            {
                // average
                PtexUtils.average(data, stride, f.res.u(), f.res.v(), constval,
                                  datatype(), _header.nchannels);
            }

            // write const val
            writeBlock(_fp, constval, _pixelSize);
            PtexMem.Delete(constval);

            // write face data
            writeFaceData(_fp, data, stride, f.res, ref efdh.fdh);

            // update editsize in header
            editsize = (uint)(sizeof(EditFaceDataHeader) + (uint)_pixelSize + efdh.fdh.blocksize());

            // rewind and write headers
            fseeko(_fp, pos, SeekOrigin.Begin);
            writeBlock(_fp, &edittype, sizeof(byte));
            writeBlock(_fp, &editsize, sizeof(uint));
            writeBlock(_fp, &efdh, sizeof(EditFaceDataHeader));
            fseeko(_fp, 0, SeekOrigin.End);
            return true;
        }

        public override bool writeConstantFace(int faceid, in FaceInfo f, void* data)
        {
            // init headers
            byte edittype = (byte)EditType.et_editfacedata;
            uint editsize;
            EditFaceDataHeader efdh = new EditFaceDataHeader();
            efdh.faceid = (uint)faceid;
            efdh.fdh.set(0, Ptex.Encoding.enc_constant);
            editsize = (uint)sizeof(EditFaceDataHeader) + (uint)_pixelSize;

            // check and store face info
            if (!storeFaceInfo(faceid, ref efdh.faceinfo, f, FaceInfo.flag_constant))
                return false;

            // write headers
            writeBlock(_fp, &edittype, sizeof(byte));
            writeBlock(_fp, &editsize, sizeof(uint));
            writeBlock(_fp, &efdh, sizeof(EditFaceDataHeader));

            // write data
            writeBlock(_fp, data, _pixelSize);
            return true;
        }

        void writeMetaDataEdit()
        {
            // init headers
            byte edittype = (byte)EditType.et_editmetadata;
            uint editsize;
            EditMetaDataHeader emdh = new EditMetaDataHeader();
            emdh.metadatazipsize = 0;
            emdh.metadatamemsize = 0;

            // record position and skip headers
            long pos = ftello(_fp);
            writeBlank(_fp, sizeof(byte) + sizeof(uint) + sizeof(EditMetaDataHeader));

            // write meta data
            for (int i = 0, n = _metadata.Count; i < n; i++)
            {
                MetaEntry e = _metadata[i];
                emdh.metadatamemsize += (uint)writeMetaDataBlock(_fp, e);
            }
            // finish zip block
            emdh.metadatazipsize = (uint)writeZipBlock(_fp, null, 0, /*finish*/ true);

            // update headers
            editsize = (uint)(sizeof(EditMetaDataHeader) + emdh.metadatazipsize);

            // rewind and write headers
            fseeko(_fp, pos, SeekOrigin.Begin);
            writeBlock(_fp, &edittype, sizeof(byte));
            writeBlock(_fp, &editsize, sizeof(uint));
            writeBlock(_fp, &emdh, sizeof(EditMetaDataHeader));
            fseeko(_fp, 0, SeekOrigin.End);
        }

        public override bool close(ref string error)
        {
            // closing base writer will write all pending data via finish() method
            bool result = base.close(ref error);
            if (_fp != null)
            {
                _fp.Dispose();
                _fp = null;
            }
            return result;
        }

        protected override void finish()
        {
            // write meta data edit block (if any)
            if (_metadata.Count != 0) writeMetaDataEdit();

            // rewrite extheader for updated editdatasize
            if (_extheader.editdatapos != 0)
            {
                _extheader.editdatasize = (ulong)ftello(_fp) - _extheader.editdatapos;
                fseeko(_fp, PtexIO.HeaderSize, SeekOrigin.Begin);
                int n = PtexUtils.min((int)PtexIO.ExtHeaderSize, (int)_header.extheadersize);
                fixed (ExtHeader* ep = &_extheader)
                    _fp.Write(new ReadOnlySpan<byte>(ep, n));
            }
        }

        FileStream _fp;  // the file being edited
    }

    public abstract unsafe partial class PtexWriter
    {
        // Open a new texture file for writing. Returns null (with error set) on failure.
        public static PtexWriter open(string path,
                                      MeshType mt, DataType dt,
                                      int nchannels, int alphachan, int nfaces,
                                      ref string error, bool genmipmaps = true)
        {
            if (!PtexWriterBase.CheckFormat(mt, dt, nchannels, alphachan, ref error))
                return null;

            PtexMainWriter w = new PtexMainWriter(path, null,
                                                  mt, dt, nchannels, alphachan, nfaces,
                                                  genmipmaps);
            if (!w.ok(ref error))
            {
                w.release();
                return null;
            }
            return w;
        }

        // Open an existing texture file for writing (or create it if it doesn't exist).
        public static PtexWriter edit(string path, bool incremental,
                                      MeshType mt, DataType dt,
                                      int nchannels, int alphachan, int nfaces,
                                      ref string error, bool genmipmaps = true)
        {
            if (!PtexWriterBase.CheckFormat(mt, dt, nchannels, alphachan, ref error))
                return null;

            // try to open existing file (it might not exist)
            FileStream fp = null;
            try
            {
                fp = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 4096);
            }
            catch (Exception e)
            {
                if (!(e is FileNotFoundException || e is DirectoryNotFoundException))
                    error = "Can't open ptex file for update: " + path + "\n" + (e is UnauthorizedAccessException ? "Permission denied" : e.Message);
            }

            PtexWriterBase w = null;

            // use incremental writer iff incremental mode requested and file exists
            if (incremental && fp != null)
            {
                w = new PtexIncrWriter(path, fp, mt, dt, nchannels, alphachan, nfaces);
            }

            // otherwise use main writer
            else
            {
                PtexTexture tex = null;
                if (fp != null)
                {
                    // got an existing file, close and reopen with PtexReader
                    fp.Dispose();

                    // open reader for existing file
                    tex = PtexTexture.open(path, ref error);
                    if (tex == null) return null;

                    // make sure header matches
                    bool headerMatch = (mt == tex.meshType() &&
                                        dt == tex.dataType() &&
                                        nchannels == tex.numChannels() &&
                                        alphachan == tex.alphaChannel() &&
                                        nfaces == tex.numFaces());
                    if (!headerMatch)
                    {
                        error = "PtexWriter::edit error: header doesn't match existing file, " +
                                "conversions not currently supported";
                        return null;
                    }
                }
                w = new PtexMainWriter(path, tex, mt, dt, nchannels, alphachan,
                                       nfaces, genmipmaps);
            }

            if (!w.ok(ref error))
            {
                w.release();
                return null;
            }
            return w;
        }

        // Apply previously made incremental edits to the file, rewriting it in place.
        public static bool applyEdits(string path, ref string error)
        {
            // open reader for existing file
            PtexTexture tex = PtexTexture.open(path, ref error);
            if (tex == null) return false;

            // see if we have any edits to apply
            if (tex.hasEdits())
            {
                // create non-incremental writer
                PtexWriter w = new PtexMainWriter(path, tex, tex.meshType(), tex.dataType(),
                                                  tex.numChannels(), tex.alphaChannel(), tex.numFaces(),
                                                  tex.hasMipMaps());
                try
                {
                    // close to rebuild file
                    if (!w.close(ref error)) return false;
                }
                finally
                {
                    w.release();
                }
            }
            return true;
        }
    }
}
