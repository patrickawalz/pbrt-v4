// PtexReader.h, PtexReader.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

// Implementation of PtexTexture API for reading Ptex files.
//
// Port notes: the C++ objects with destructors (PtexReader, the face data blocks, Level,
// MetaData) implement IDisposable here; `delete x` is x.Dispose() and the buffers handed out as
// raw pointers (face data, constant data, large meta data) are native allocations freed by
// Dispose, exactly like the C++ new[]/delete[]. zlib's streaming inflate is replaced by
// System.IO.Compression.ZLibStream (the compressed block is gathered first, in the same
// BlockSize file reads as the C++).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Ptex
{
    // Native memory helpers standing in for new[]/delete[]
    public static unsafe class PtexMem
    {
        public static byte* NewBytes(int size)
        {
            // (zero-initialized so that uninitialized reads are deterministic)
            return (byte*)NativeMemory.AllocZeroed((nuint)Math.Max(size, 1));
        }
        public static void Delete(byte* p)
        {
            if (p != null) NativeMemory.Free(p);
        }
    }

    public abstract unsafe partial class PtexTexture
    {
        internal sealed class TempErrorHandler : PtexErrorHandler
        {
            string _error = "";
            public override void reportError(string error) { _error += error; }
            public string getErrorString() { return _error; }
        }

        // Open a ptex file for reading. If an error occurs, an error message will be stored in
        // the error string param and null will be returned.
        public static PtexTexture open(string path, ref string error, bool premultiply = false)
        {
            PtexReader reader = new PtexReader(premultiply, null, null);
            bool ok = reader.open(path, ref error);
            if (!ok)
            {
                reader.release();
                return null;
            }
            return reader;
        }
    }

    public unsafe class PtexReader : PtexTexture, IDisposable
    {
        public PtexReader(bool premultiply, PtexInputHandler io, PtexErrorHandler err)
        {
            _defaultIo = new DefaultInputHandler();
            _io = io != null ? io : _defaultIo;
            _err = err;
            _premultiply = premultiply;
            _ok = true;
            _needToOpen = true;
            _pendingPurge = false;
            _fp = IntPtr.Zero;
            _pos = 0;
            _pixelsize = 0;
            _constdata = null;
            _metadata = null;
            _hasEdits = false;
            _baseMemUsed = (ulong)SizeOfThis;
            _memUsed = _baseMemUsed;
            _opens = 0;
            _blockReads = 0;
        }

        // (sizeof(*this) in the C++ is used only to account memory in the cache)
        protected const int SizeOfThis = 1024;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            closeFP();
            if (_constdata != null) { PtexMem.Delete(_constdata); _constdata = null; }
            if (_metadata != null) { _metadata.Dispose(); _metadata = null; }

            if (_levels != null)
                foreach (Level l in _levels)
                    if (l != null) l.Dispose();
            _levels = null;
            _reductions.clear();
        }

        ~PtexReader() { Dispose(false); }

        public override void release() { Dispose(); }
        public bool needToOpen() { return _needToOpen; }
        public void setPendingPurge() { _pendingPurge = true; }
        public bool pendingPurge() { return _pendingPurge; }
        public bool ok() { return _ok; }
        public bool isOpen() { return _fp != IntPtr.Zero; }
        public void invalidate()
        {
            _ok = false;
            _needToOpen = false;
        }

        public void increaseMemUsed(ulong amount) { if (amount != 0) PtexPlatform.AtomicAdd(ref _memUsed, amount); }
        public void logOpen() { PtexPlatform.AtomicIncrement(ref _opens); }
        public void logBlockRead() { PtexPlatform.AtomicIncrement(ref _blockReads); }

        public override string path() { return _path; }

        public override Info getInfo()
        {
            Info info = new Info();
            info.meshType = (MeshType)_header.meshtype;
            info.dataType = (DataType)_header.datatype;
            info.uBorderMode = (BorderMode)_extheader.ubordermode;
            info.vBorderMode = (BorderMode)_extheader.vbordermode;
            info.edgeFilterMode = (EdgeFilterMode)_extheader.edgefiltermode;
            info.alphaChannel = _header.alphachan;
            info.numChannels = _header.nchannels;
            info.numFaces = (int)_header.nfaces;
            return info;
        }

        public override MeshType meshType() { return (MeshType)_header.meshtype; }
        public override DataType dataType() { return (DataType)_header.datatype; }
        public override BorderMode uBorderMode() { return (BorderMode)_extheader.ubordermode; }
        public override BorderMode vBorderMode() { return (BorderMode)_extheader.vbordermode; }
        public override EdgeFilterMode edgeFilterMode() { return (EdgeFilterMode)_extheader.edgefiltermode; }
        public override int alphaChannel() { return _header.alphachan; }
        public override int numChannels() { return _header.nchannels; }
        public override int numFaces() { return (int)_header.nfaces; }
        public override bool hasEdits() { return _hasEdits; }
        public override bool hasMipMaps() { return _header.nlevels > 1; }

        public DataType datatype() { return (DataType)_header.datatype; }
        public int nchannels() { return _header.nchannels; }
        public int pixelsize() { return _pixelsize; }
        public ref readonly Header header() { return ref _header; }
        public ref readonly ExtHeader extheader() { return ref _extheader; }
        public ref readonly LevelInfo levelinfo(int level) { return ref _levelinfo[level]; }

        // Meta data accessor
        public class MetaData : PtexMetaData, IDisposable
        {
            public MetaData(PtexReader reader) { _reader = reader; }
            public void Dispose()
            {
                foreach (Entry e in _entries) e.clear();
            }

            public override void release() { }

            public override int numKeys() { return _entries.Count; }
            public override void getKey(int index, out string key, out MetaDataType type)
            {
                key = null;
                type = 0;
                if (index < 0 || index >= _entries.Count)
                {
                    return;
                }
                Entry e = _entries[index];
                key = e.key;
                type = e.type;
            }

            public override bool findKey(string key, out int index, out MetaDataType type)
            {
                Entry e;
                if (!_map.TryGetValue(key, out e))
                {
                    index = -1;
                    type = 0;
                    return false;
                }
                index = (int)e.index;
                type = e.type;
                return true;
            }

            public override void getValue(string key, out string value)
            {
                int index = -1;
                MetaDataType type;
                if (!findKey(key, out index, out type))
                {
                    value = null;
                    return;
                }
                Entry e = getEntry(index);
                if (e != null && e.type == MetaDataType.mdt_string) value = Marshal.PtrToStringUTF8((IntPtr)e.data);
                else value = null;
            }

            public override void getValue(int index, out string value)
            {
                if (index < 0 || index >= _entries.Count) { value = null; return; }
                Entry e = getEntry(index);
                if (e != null && e.type == MetaDataType.mdt_string) value = Marshal.PtrToStringUTF8((IntPtr)e.data);
                else value = null;
            }

            void getValueT<T>(int index, MetaDataType requestedType, out T* value, out int count) where T : unmanaged
            {
                if (index < 0 || index >= _entries.Count)
                {
                    value = null;
                    count = 0;
                    return;
                }
                Entry e = getEntry(index);
                if (e != null && e.type == requestedType)
                {
                    value = (T*)e.data;
                    count = (int)(e.datasize / (uint)sizeof(T));
                }
                else { value = null; count = 0; }
            }

            void getValueT<T>(string key, MetaDataType requestedType, out T* value, out int count) where T : unmanaged
            {
                int index = -1;
                MetaDataType type;
                findKey(key, out index, out type);
                getValueT<T>(index, requestedType, out value, out count);
            }

            public override void getValue(string key, out sbyte* value, out int count) { getValueT<sbyte>(key, MetaDataType.mdt_int8, out value, out count); }
            public override void getValue(int index, out sbyte* value, out int count) { getValueT<sbyte>(index, MetaDataType.mdt_int8, out value, out count); }
            public override void getValue(string key, out short* value, out int count) { getValueT<short>(key, MetaDataType.mdt_int16, out value, out count); }
            public override void getValue(int index, out short* value, out int count) { getValueT<short>(index, MetaDataType.mdt_int16, out value, out count); }
            public override void getValue(string key, out int* value, out int count) { getValueT<int>(key, MetaDataType.mdt_int32, out value, out count); }
            public override void getValue(int index, out int* value, out int count) { getValueT<int>(index, MetaDataType.mdt_int32, out value, out count); }
            public override void getValue(string key, out float* value, out int count) { getValueT<float>(key, MetaDataType.mdt_float, out value, out count); }
            public override void getValue(int index, out float* value, out int count) { getValueT<float>(index, MetaDataType.mdt_float, out value, out count); }
            public override void getValue(string key, out double* value, out int count) { getValueT<double>(key, MetaDataType.mdt_double, out value, out count); }
            public override void getValue(int index, out double* value, out int count) { getValueT<double>(index, MetaDataType.mdt_double, out value, out count); }

            public void addEntry(byte keysize, byte* key, byte datatype, uint datasize, void* data, ref ulong metaDataMemUsed)
            {
                Entry e = newEntry(keysize, key, datatype, datasize, ref metaDataMemUsed);
                e.data = PtexMem.NewBytes((int)datasize);
                Buffer.MemoryCopy(data, e.data, datasize, datasize);
                metaDataMemUsed += datasize;
            }

            public void addLmdEntry(byte keysize, byte* key, byte datatype, uint datasize, long filepos, uint zipsize,
                                    ref ulong metaDataMemUsed)
            {
                Entry e = newEntry(keysize, key, datatype, datasize, ref metaDataMemUsed);
                e.isLmd = true;
                e.lmdData = null;
                e.lmdPos = filepos;
                e.lmdZipSize = zipsize;
            }

            public ulong selfDataSize()
            {
                return (ulong)(64 + 8 * _entries.Capacity);
            }

            protected sealed class LargeMetaData : IDisposable
            {
                public LargeMetaData(int size) { _data = PtexMem.NewBytes(size); }
                public void Dispose() { PtexMem.Delete(_data); _data = null; }
                public void* data() { return _data; }
                byte* _data;
            }

            protected sealed class Entry
            {
                public string key;           // map key string
                public MetaDataType type;    // meta data type
                public uint datasize;        // size of data in bytes
                public byte* data;           // if lmd, data only valid when lmd is loaded and ref'ed
                public bool isLmd;           // true if data is a large meta data block
                public LargeMetaData lmdData;  // large meta data (lazy-loaded)
                public long lmdPos;          // large meta data file position
                public uint lmdZipSize;      // large meta data size on disk
                public uint index;           // index in vector

                public void clear()
                {
                    if (isLmd)
                    {
                        isLmd = false;
                        if (lmdData != null) { lmdData.Dispose(); lmdData = null; }
                        lmdPos = 0;
                        lmdZipSize = 0;
                    }
                    else
                    {
                        if (data != null) { PtexMem.Delete(data); }
                    }
                    data = null;
                }
            }

            Entry newEntry(byte keysize, byte* key, byte datatype, uint datasize, ref ulong metaDataMemUsed)
            {
                string keystr = System.Text.Encoding.UTF8.GetString(key, keysize);
                Entry e;
                bool newentry = !_map.TryGetValue(keystr, out e);
                uint index = 0;
                if (newentry)
                {
                    e = new Entry();
                    _map[keystr] = e;
                    index = (uint)_entries.Count;
                    _entries.Add(e);
                }
                else
                {
                    index = e.index;
                    e.clear();
                }
                e.key = keystr;
                e.type = (MetaDataType)datatype;
                e.datasize = datasize;
                e.index = index;
                metaDataMemUsed += (ulong)(24 + keysize + 1 + 64);
                return e;
            }

            Entry getEntry(int index)
            {
                if (index < 0 || index >= _entries.Count)
                {
                    return null;
                }

                Entry e = _entries[index];
                if (!e.isLmd)
                {
                    // normal (small) meta data - just return directly
                    return e;
                }

                // large meta data - may not be read in yet
                if (e.lmdData != null)
                {
                    // already in memory
                    return e;
                }
                else
                {
                    // not present, must read from file

                    // get read lock and make sure we still need to read
                    using (new AutoMutex(_reader.readlock))
                    {
                        if (e.lmdData != null)
                        {
                            return e;
                        }
                        // go ahead and read, keep local until finished
                        LargeMetaData lmdData = new LargeMetaData((int)e.datasize);
                        e.data = (byte*)lmdData.data();
                        _reader.increaseMemUsed(64 + e.datasize);
                        _reader.seek(e.lmdPos);
                        _reader.readZipBlock(e.data, (int)e.lmdZipSize, (int)e.datasize);
                        // update entry
                        e.lmdData = lmdData;
                        return e;
                    }
                }
            }

            protected PtexReader _reader;
            protected SortedDictionary<string, Entry> _map = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
            protected List<Entry> _entries = new List<Entry>();
        }

        public sealed class ConstDataPtr : PtexFaceData
        {
            public ConstDataPtr(void* data, int pixelsize) { _data = data; _pixelsize = pixelsize; }
            public override void release() { }
            public override Res res() { return new Res((ushort)0); }
            public override bool isConstant() { return true; }
            public override void getPixel(int u, int v, void* result) { Buffer.MemoryCopy(_data, result, _pixelsize, _pixelsize); }
            public override void* getData() { return _data; }
            public override bool isTiled() { return false; }
            public override Res tileRes() { return new Res((ushort)0); }
            public override PtexFaceData getTile(int i) { return null; }

            void* _data;
            int _pixelsize;
        }

        public abstract class FaceData : PtexFaceData, IDisposable
        {
            public FaceData(Res resArg) { _res = resArg; }
            public virtual void Dispose() { }
            public override void release() { }
            public override Res res() { return _res; }
            public abstract FaceData reduce(PtexReader r, Res newres, ReduceFn reducefn, ref ulong newMemUsed);
            protected Res _res;
        }

        public class PackedFace : FaceData
        {
            public PackedFace(Res resArg, int pixelsize, int size) : base(resArg)
            {
                _pixelsize = pixelsize;
                _data = PtexMem.NewBytes(size);
            }
            public void* data() { return _data; }
            public override bool isConstant() { return false; }
            public override void getPixel(int u, int v, void* result)
            {
                Buffer.MemoryCopy(_data + (v * _res.u() + u) * _pixelsize, result, _pixelsize, _pixelsize);
            }
            public override void* getData() { return _data; }
            public override bool isTiled() { return false; }
            public override Res tileRes() { return _res; }
            public override PtexFaceData getTile(int i) { return null; }

            public override FaceData reduce(PtexReader r, Res newres, ReduceFn reducefn, ref ulong newMemUsed)
            {
                // allocate a new face and reduce image
                DataType dt = r.datatype();
                int nchan = r.nchannels();
                int memsize = _pixelsize * newres.size();
                PackedFace pf = new PackedFace(newres, _pixelsize, memsize);
                newMemUsed = (ulong)(48 + memsize);
                // reduce and copy into new face
                reducefn(_data, _pixelsize * _res.u(), _res.u(), _res.v(), pf._data, _pixelsize * newres.u(), dt, nchan);
                return pf;
            }

            public override void Dispose()
            {
                if (_data != null) { PtexMem.Delete(_data); _data = null; }
            }

            protected int _pixelsize;
            protected byte* _data;
        }

        public class ConstantFace : PackedFace
        {
            public ConstantFace(int pixelsize) : base(new Res((ushort)0), pixelsize, pixelsize) { }
            public override bool isConstant() { return true; }
            public override void getPixel(int u, int v, void* result) { Buffer.MemoryCopy(_data, result, _pixelsize, _pixelsize); }

            public override FaceData reduce(PtexReader r, Res newres, ReduceFn reducefn, ref ulong newMemUsed)
            {
                // must make a new constant face (even though it's identical to this one)
                // because it will be owned by a different reduction level
                // and will therefore have a different parent
                ConstantFace pf = new ConstantFace(_pixelsize);
                newMemUsed = (ulong)(48 + _pixelsize);
                Buffer.MemoryCopy(_data, pf._data, _pixelsize, _pixelsize);
                return pf;
            }
        }

        public class ErrorFace : ConstantFace
        {
            bool _deleteOnRelease;
            public ErrorFace(void* errorPixel, int pixelsize, bool deleteOnRelease) : base(pixelsize)
            {
                _deleteOnRelease = deleteOnRelease;
                Buffer.MemoryCopy(errorPixel, _data, pixelsize, pixelsize);
            }
            public override void release() { if (_deleteOnRelease) Dispose(); }
        }

        public abstract class TiledFaceBase : FaceData
        {
            public TiledFaceBase(PtexReader reader, Res resArg, Res tileresArg) : base(resArg)
            {
                _reader = reader;
                _tileres = tileresArg;
                _dt = reader.datatype();
                _nchan = reader.nchannels();
                _pixelsize = PtexGlobals.DataSize(_dt) * _nchan;
                _ntilesu = _res.ntilesu(tileresArg);
                _ntilesv = _res.ntilesv(tileresArg);
                _ntiles = _ntilesu * _ntilesv;
                _tiles = new FaceData[_ntiles];
            }

            public override void release() { }
            public override bool isConstant() { return false; }
            public override void* getData() { return null; }
            public override bool isTiled() { return true; }
            public override Res tileRes() { return _tileres; }
            public Res tileres() { return _tileres; }
            public int ntilesu() { return _ntilesu; }
            public int ntilesv() { return _ntilesv; }
            public int ntiles() { return _ntiles; }

            public override void getPixel(int ui, int vi, void* result)
            {
                int tileu = ui >> _tileres.ulog2;
                int tilev = vi >> _tileres.vlog2;
                using (PtexPtr<PtexFaceData> tile = new PtexPtr<PtexFaceData>(getTile(tilev * _ntilesu + tileu)))
                {
                    tile.get().getPixel(ui - (tileu << _tileres.ulog2), vi - (tilev << _tileres.vlog2), result);
                }
            }

            public override FaceData reduce(PtexReader r, Res newres, ReduceFn reducefn, ref ulong newMemUsed)
            {
                /* Tiled reductions should generally only be anisotropic (just u
                   or v, not both) since isotropic reductions are precomputed and
                   stored on disk.  (This function should still work for isotropic
                   reductions though.)

                   In the anisotropic case, the number of tiles should be kept the
                   same along the direction not being reduced in order to preserve
                   the laziness of the file access.  In contrast, if reductions
                   were not tiled, then any reduction would read all the tiles and
                   defeat the purpose of tiling.
                */

                // keep new face local until fully initialized
                FaceData newface = null;

                // don't tile triangle reductions (too complicated)
                Res newtileres;
                bool isTriangle = r._header.meshtype == (uint)MeshType.mt_triangle;
                if (isTriangle)
                {
                    newtileres = newres;
                }
                else
                {
                    // propagate the tile res to the reduction
                    newtileres = _tileres;
                    // but make sure tile isn't larger than the new face!
                    if (newtileres.ulog2 > newres.ulog2) newtileres.ulog2 = newres.ulog2;
                    if (newtileres.vlog2 > newres.vlog2) newtileres.vlog2 = newres.vlog2;
                }

                // determine how many tiles we will have on the reduction
                int newntiles = newres.ntiles(newtileres);

                if (newntiles == 1)
                {
                    // no need to keep tiling, reduce tiles into a single face
                    // first, get all tiles and check if they are constant (with the same value)
                    PtexFaceData[] tiles = new PtexFaceData[_ntiles];
                    bool allConstant = true;
                    for (int i = 0; i < _ntiles; i++)
                    {
                        PtexFaceData tile = tiles[i] = getTile(i);
                        allConstant = (allConstant && tile.isConstant() &&
                                       (i == 0 || new ReadOnlySpan<byte>(tiles[0].getData(), _pixelsize).SequenceEqual(new ReadOnlySpan<byte>(tile.getData(), _pixelsize))));
                    }
                    if (allConstant)
                    {
                        // allocate a new constant face
                        newface = new ConstantFace(_pixelsize);
                        Buffer.MemoryCopy(tiles[0].getData(), newface.getData(), _pixelsize, _pixelsize);
                        newMemUsed = (ulong)(48 + _pixelsize);
                    }
                    else if (isTriangle)
                    {
                        // reassemble all tiles into temporary contiguous image
                        // (triangle reduction doesn't work on tiles)
                        int tileures = _tileres.u();
                        int tilevres = _tileres.v();
                        int sstride = _pixelsize * tileures;
                        int dstride = sstride * _ntilesu;
                        int dstepv = dstride * tilevres - sstride * (_ntilesu - 1);

                        byte* tmp = PtexMem.NewBytes(_ntiles * _tileres.size() * _pixelsize);
                        byte* tmpptr = tmp;
                        for (int i = 0; i < _ntiles;)
                        {
                            PtexFaceData tile = tiles[i];
                            if (tile.isConstant())
                                PtexUtils.fill(tile.getData(), tmpptr, dstride, tileures, tilevres, _pixelsize);
                            else
                                PtexUtils.copy(tile.getData(), sstride, tmpptr, dstride, tilevres, sstride);
                            i++;
                            tmpptr += (i % _ntilesu) != 0 ? sstride : dstepv;
                        }

                        // allocate a new packed face
                        int memsize = _pixelsize * newres.size();
                        newface = new PackedFace(newres, _pixelsize, memsize);
                        newMemUsed = (ulong)(48 + memsize);
                        // reduce and copy into new face
                        reducefn(tmp, _pixelsize * _res.u(), _res.u(), _res.v(), newface.getData(), _pixelsize * newres.u(), _dt, _nchan);

                        PtexMem.Delete(tmp);
                    }
                    else
                    {
                        // allocate a new packed face
                        int memsize = _pixelsize * newres.size();
                        newface = new PackedFace(newres, _pixelsize, memsize);
                        newMemUsed = (ulong)(48 + memsize);

                        int tileures = _tileres.u();
                        int tilevres = _tileres.v();
                        int sstride = _pixelsize * tileures;
                        int dstride = _pixelsize * newres.u();
                        int dstepu = dstride / _ntilesu;
                        int dstepv = dstride * newres.v() / _ntilesv - dstepu * (_ntilesu - 1);

                        byte* dst = (byte*)newface.getData();
                        for (int i = 0; i < _ntiles;)
                        {
                            PtexFaceData tile = tiles[i];
                            if (tile.isConstant())
                                PtexUtils.fill(tile.getData(), dst, dstride, newres.u() / _ntilesu, newres.v() / _ntilesv, _pixelsize);
                            else
                                reducefn(tile.getData(), sstride, tileures, tilevres, dst, dstride, _dt, _nchan);
                            i++;
                            dst += (i % _ntilesu) != 0 ? dstepu : dstepv;
                        }
                    }
                    // release the tiles
                    for (int i = 0; i < _ntiles; i++) tiles[i].release();
                }
                else
                {
                    // otherwise, tile the reduced face
                    TiledReducedFace tf = new TiledReducedFace(_reader, newres, newtileres, this, reducefn);
                    newface = tf;
                    newMemUsed = tf.memUsed();
                }
                return newface;
            }

            protected ulong baseExtraMemUsed() { return (ulong)_tiles.Length * 8; }

            public override void Dispose()
            {
                foreach (FaceData t in _tiles)
                    if (t != null) t.Dispose();
            }

            protected PtexReader _reader;
            protected Res _tileres;
            protected DataType _dt;
            protected int _nchan;
            protected int _ntilesu;
            protected int _ntilesv;
            internal int _ntiles;
            protected int _pixelsize;
            protected FaceData[] _tiles;
        }

        public class TiledFace : TiledFaceBase
        {
            public TiledFace(PtexReader reader, Res resArg, Res tileresArg, int levelid) : base(reader, resArg, tileresArg)
            {
                _levelid = levelid;
                _fdh = new FaceDataHeader[_ntiles];
                _offsets = new long[_ntiles];
            }
            public override PtexFaceData getTile(int tile)
            {
                ref FaceData f = ref _tiles[tile];
                if (f == null) readTile(tile, ref f);
                return f;
            }
            public void readTile(int tile, ref FaceData data)
            {
                _reader.readFaceData(_offsets[tile], _fdh[tile], _tileres, _levelid, ref data);
            }
            public ulong memUsed()
            {
                return 96 + baseExtraMemUsed() + (ulong)_fdh.Length * (4 + 8);
            }

            internal int _levelid;
            internal FaceDataHeader[] _fdh;
            internal long[] _offsets;
        }

        public class TiledReducedFace : TiledFaceBase
        {
            public TiledReducedFace(PtexReader reader, Res resArg, Res tileresArg, TiledFaceBase parentface, ReduceFn reducefn)
                : base(reader, resArg, tileresArg)
            {
                _parentface = parentface;
                _reducefn = reducefn;
            }

            public override PtexFaceData getTile(int tile)
            {
                FaceData face = _tiles[tile];
                if (face != null)
                {
                    return face;
                }

                // first, get all parent tiles for this tile
                // and check if they are constant (with the same value)
                int pntilesu = _parentface.ntilesu();
                int pntilesv = _parentface.ntilesv();
                int nu = pntilesu / _ntilesu;  // num parent tiles for this tile in u dir
                int nv = pntilesv / _ntilesv;  // num parent tiles for this tile in v dir

                int ntilesval = nu * nv;  // num parent tiles for this tile
                PtexFaceData[] tiles = new PtexFaceData[ntilesval];
                bool allConstant = true;
                int ptile = (tile / _ntilesu) * nv * pntilesu + (tile % _ntilesu) * nu;
                for (int i = 0; i < ntilesval;)
                {
                    PtexFaceData tileval = tiles[i] = _parentface.getTile(ptile);
                    allConstant = (allConstant && tileval.isConstant() &&
                                   (i == 0 || new ReadOnlySpan<byte>(tiles[0].getData(), _pixelsize).SequenceEqual(new ReadOnlySpan<byte>(tileval.getData(), _pixelsize))));
                    i++;
                    ptile += (i % nu) != 0 ? 1 : pntilesu - nu + 1;
                }

                FaceData newface = null;
                ulong newMemUsed = 0;
                if (allConstant)
                {
                    // allocate a new constant face
                    newface = new ConstantFace(_pixelsize);
                    newMemUsed = (ulong)(48 + _pixelsize);
                    Buffer.MemoryCopy(tiles[0].getData(), newface.getData(), _pixelsize, _pixelsize);
                }
                else
                {
                    // allocate a new packed face for the tile
                    int memsize = _pixelsize * _tileres.size();
                    newface = new PackedFace(_tileres, _pixelsize, memsize);
                    newMemUsed = (ulong)(48 + memsize);

                    // generate reduction from parent tiles
                    int ptileures = _parentface.tileres().u();
                    int ptilevres = _parentface.tileres().v();
                    int sstride = ptileures * _pixelsize;
                    int dstride = _tileres.u() * _pixelsize;
                    int dstepu = dstride / nu;
                    int dstepv = dstride * _tileres.v() / nv - dstepu * (nu - 1);

                    byte* dst = (byte*)newface.getData();
                    for (int i = 0; i < ntilesval;)
                    {
                        PtexFaceData tileval = tiles[i];
                        if (tileval.isConstant())
                            PtexUtils.fill(tileval.getData(), dst, dstride, _tileres.u() / nu, _tileres.v() / nv, _pixelsize);
                        else
                            _reducefn(tileval.getData(), sstride, ptileures, ptilevres, dst, dstride, _dt, _nchan);
                        i++;
                        dst += (i % nu) != 0 ? dstepu : dstepv;
                    }
                }

                if (!PtexPlatform.AtomicCompareAndSwap(ref _tiles[tile], null, newface))
                {
                    newface.Dispose();
                }
                else
                {
                    _reader.increaseMemUsed(newMemUsed);
                }

                return _tiles[tile];
            }

            public ulong memUsed() { return 96 + baseExtraMemUsed(); }

            protected TiledFaceBase _parentface;
            protected ReduceFn _reducefn;
        }

        public class Level : IDisposable
        {
            public FaceDataHeader[] fdh;
            public long[] offsets;
            public FaceData[] faces;

            public Level(int nfaces)
            {
                fdh = new FaceDataHeader[nfaces];
                offsets = new long[nfaces];
                faces = new FaceData[nfaces];
            }

            public void Dispose()
            {
                foreach (FaceData f in faces)
                    if (f != null) f.Dispose();
            }

            public ulong memUsed()
            {
                return 64 + (ulong)fdh.Length * (4 + 8 + 8);
            }
        }

        public void prune()
        {
            if (_metadata != null) { _metadata.Dispose(); _metadata = null; }
            if (_levels != null)
                for (int i = 0; i < _levels.Length; ++i)
                    if (_levels[i] != null) { _levels[i].Dispose(); _levels[i] = null; }
            _reductions.clear();
            _memUsed = _baseMemUsed;
        }

        public void purge()
        {
            // free all dynamic data
            prune();
            if (_constdata != null) { PtexMem.Delete(_constdata); _constdata = null; }
            _faceinfo = null;
            _rfaceids = null;
            _levelinfo = null;
            _levelpos = null;
            _levels = null;
            _metaedits = new List<MetaEdit>();
            _faceedits = new List<FaceEdit>();
            closeFP();

            // reset initial state
            _ok = true;
            _needToOpen = true;
            _pendingPurge = false;
            _baseMemUsed = (ulong)SizeOfThis;
            _memUsed = _baseMemUsed;
        }

        public bool open(string pathArg, ref string error)
        {
            using (new AutoMutex(readlock))
            {
                if (!needToOpen()) return false;

                if (!PtexIO.LittleEndian())
                {
                    error = "Ptex library doesn't currently support big-endian cpu's";
                    return false;
                }
                _path = pathArg;
                _fp = _io.open(pathArg);
                if (_fp == IntPtr.Zero)
                {
                    string errstr = "Can't open ptex file: ";
                    errstr += pathArg; errstr += "\n"; errstr += _io.lastError();
                    error = errstr;
                    _ok = false;
                    return false;
                }
                _header = new Header();
                fixed (Header* hp = &_header) readBlock(hp, PtexIO.HeaderSize);
                if (_header.magic != PtexIO.Magic)
                {
                    string errstr = "Not a ptex file: "; errstr += pathArg;
                    error = errstr;
                    _ok = false;
                    closeFP();
                    return false;
                }
                if (_header.version != 1)
                {
                    error = "Unsupported ptex file version (" + _header.version + "): " + pathArg;
                    _ok = false;
                    closeFP();
                    return false;
                }
                if (!(_header.meshtype == (uint)MeshType.mt_triangle || _header.meshtype == (uint)MeshType.mt_quad))
                {
                    error = "Invalid mesh type (" + _header.meshtype + "): " + pathArg;
                    _ok = false;
                    closeFP();
                    return false;
                }
                _pixelsize = _header.pixelSize();
                _errorPixel = new byte[_pixelsize];

                // install temp error handler to capture error (to return in error param)
                TempErrorHandler tempErr = new TempErrorHandler();
                PtexErrorHandler prevErr = _err;
                _err = tempErr;

                // read extended header
                _extheader = new ExtHeader();
                fixed (ExtHeader* ep = &_extheader) readBlock(ep, (int)PtexUtils.min((int)PtexIO.ExtHeaderSize, (int)_header.extheadersize));

                // compute offsets of various blocks
                long pos = PtexIO.HeaderSize + _header.extheadersize;
                _faceinfopos = pos; pos += _header.faceinfosize;
                _constdatapos = pos; pos += _header.constdatasize;
                _levelinfopos = pos; pos += _header.levelinfosize;
                _leveldatapos = pos; pos += (long)_header.leveldatasize;
                _metadatapos = pos; pos += _header.metadatazipsize;
                pos += sizeof(ulong);  // compatibility barrier
                _lmdheaderpos = pos; pos += _extheader.lmdheaderzipsize;
                _lmddatapos = pos; pos += (long)_extheader.lmddatasize;

                // edit data may not start immediately if additional sections have been added
                // use value from extheader if present (and > pos)
                _editdatapos = Math.Max((long)_extheader.editdatapos, pos);

                // read basic file info
                readFaceInfo();
                readConstData();
                readLevelInfo();
                readEditData();
                _baseMemUsed = _memUsed;

                // restore error handler
                _err = prevErr;

                // an error occurred while reading the file
                if (!_ok)
                {
                    error = tempErr.getErrorString();
                    closeFP();
                    return false;
                }
                PtexPlatform.AtomicStore(ref _needToOpen, false);
                return true;
            }
        }

        public bool tryClose()
        {
            if (_fp != IntPtr.Zero)
            {
                if (!readlock.trylock()) return false;
                closeFP();
                readlock.unlock();
            }
            return true;
        }

        protected void closeFP()
        {
            if (_fp != IntPtr.Zero)
            {
                _io.close(_fp);
                _fp = IntPtr.Zero;
            }
        }

        protected bool reopenFP()
        {
            if (_fp != IntPtr.Zero) return true;

            // we assume this is called lazily in a scope where readlock is already held
            _fp = _io.open(_path);
            if (_fp == IntPtr.Zero)
            {
                setError("Can't reopen");
                return false;
            }
            _pos = 0;
            Header headerval = new Header();
            ExtHeader extheaderval = new ExtHeader();
            readBlock(&headerval, PtexIO.HeaderSize);
            readBlock(&extheaderval, (int)PtexUtils.min((int)PtexIO.ExtHeaderSize, (int)headerval.extheadersize));
            Header hcopy = _header;
            ExtHeader ecopy = _extheader;
            if (!new ReadOnlySpan<byte>(&headerval, sizeof(Header)).SequenceEqual(new ReadOnlySpan<byte>(&hcopy, sizeof(Header))) ||
                !new ReadOnlySpan<byte>(&extheaderval, sizeof(ExtHeader)).SequenceEqual(new ReadOnlySpan<byte>(&ecopy, sizeof(ExtHeader))))
            {
                setError("Header mismatch on reopen of");
                return false;
            }
            logOpen();
            return true;
        }

        static FaceInfo s_dummyFaceInfo = FaceInfo.Default();

        public override ref readonly FaceInfo getFaceInfo(int faceid)
        {
            if (faceid >= 0 && (uint)faceid < (uint)(_faceinfo != null ? _faceinfo.Length : 0))
                return ref _faceinfo[faceid];

            return ref s_dummyFaceInfo;
        }

        protected void readFaceInfo()
        {
            if (_faceinfo == null || _faceinfo.Length == 0)
            {
                // read compressed face info block
                seek(_faceinfopos);
                int nfaces = (int)_header.nfaces;
                _faceinfo = new FaceInfo[nfaces];
                for (int i = 0; i < nfaces; ++i) _faceinfo[i] = FaceInfo.Default();
                fixed (FaceInfo* fp = _faceinfo)
                    readZipBlock(fp, (int)_header.faceinfosize, sizeof(FaceInfo) * nfaces);

                // generate rfaceids
                _rfaceids = new uint[nfaces];
                uint[] faceids_r = new uint[nfaces];
                fixed (FaceInfo* fp = _faceinfo)
                fixed (uint* rp = _rfaceids)
                fixed (uint* fr = faceids_r)
                    PtexUtils.genRfaceids(fp, nfaces, rp, fr);
                increaseMemUsed((ulong)nfaces * (ulong)(sizeof(FaceInfo) + sizeof(uint)));
            }
        }

        protected void readLevelInfo()
        {
            if (_levelinfo == null || _levelinfo.Length == 0)
            {
                // read level info block
                seek(_levelinfopos);
                _levelinfo = new LevelInfo[_header.nlevels];
                fixed (LevelInfo* lp = _levelinfo)
                    readBlock(lp, PtexIO.LevelInfoSize * _header.nlevels);

                // initialize related data
                _levels = new Level[_header.nlevels];
                _levelpos = new long[_header.nlevels];
                long pos = _leveldatapos;
                for (int i = 0; i < _header.nlevels; i++)
                {
                    _levelpos[i] = pos;
                    pos += (long)_levelinfo[i].leveldatasize;
                }
                increaseMemUsed((ulong)(_header.nlevels * sizeof(LevelInfo) + 8 + 8));
            }
        }

        protected void readConstData()
        {
            if (_constdata == null)
            {
                // read compressed constant data block
                seek(_constdatapos);
                int size = _pixelsize * (int)_header.nfaces;
                _constdata = PtexMem.NewBytes(size);
                readZipBlock(_constdata, (int)_header.constdatasize, size);
                if (_premultiply && _header.hasAlpha())
                    PtexUtils.multalpha(_constdata, (int)_header.nfaces, datatype(), _header.nchannels, _header.alphachan);
                increaseMemUsed((ulong)size);
            }
        }

        public override PtexMetaData getMetaData()
        {
            if (_metadata == null) readMetaData();
            return _metadata;
        }

        protected void readMetaData()
        {
            // get read lock and make sure we still need to read
            using (new AutoMutex(readlock))
            {
                if (_metadata != null)
                {
                    return;
                }

                // allocate new meta data (keep local until fully initialized)
                MetaData newmeta = new MetaData(this);
                ulong metaDataMemUsed = 48;

                // read primary meta data blocks
                if (_header.metadatamemsize != 0)
                    readMetaDataBlock(newmeta, _metadatapos, (int)_header.metadatazipsize, (int)_header.metadatamemsize, ref metaDataMemUsed);

                // read large meta data headers
                if (_extheader.lmdheadermemsize != 0)
                    readLargeMetaDataHeaders(newmeta, _lmdheaderpos, (int)_extheader.lmdheaderzipsize, (int)_extheader.lmdheadermemsize, ref metaDataMemUsed);

                // read meta data edits
                for (int i = 0, size = _metaedits.Count; i < size; i++)
                    readMetaDataBlock(newmeta, _metaedits[i].pos, _metaedits[i].zipsize, _metaedits[i].memsize, ref metaDataMemUsed);

                // store meta data
                PtexPlatform.AtomicStore(ref _metadata, newmeta);
                increaseMemUsed(newmeta.selfDataSize() + metaDataMemUsed);
            }
        }

        protected void readMetaDataBlock(MetaData metadata, long pos, int zipsize, int memsize, ref ulong metaDataMemUsed)
        {
            seek(pos);
            // read from file
            bool useNew = memsize > PtexIO.AllocaMax;
            byte* buff = PtexMem.NewBytes(memsize);

            if (readZipBlock(buff, zipsize, memsize))
            {
                // unpack data entries
                byte* ptr = buff;
                byte* end = ptr + memsize;
                while (ptr < end)
                {
                    byte keysize = *ptr++;
                    byte* key = ptr; ptr += keysize;
                    key[keysize - 1] = 0;
                    byte datatypeval = *ptr++;
                    uint datasize; Buffer.MemoryCopy(ptr, &datasize, sizeof(uint), sizeof(uint));
                    ptr += sizeof(uint);
                    byte* data = ptr; ptr += datasize;
                    metadata.addEntry((byte)(keysize - 1), key, datatypeval, datasize, data, ref metaDataMemUsed);
                }
            }
            PtexMem.Delete(buff);
        }

        protected void readLargeMetaDataHeaders(MetaData metadata, long pos, int zipsize, int memsize, ref ulong metaDataMemUsed)
        {
            seek(pos);
            // read from file
            bool useNew = memsize > PtexIO.AllocaMax;
            byte* buff = PtexMem.NewBytes(memsize);

            if (readZipBlock(buff, zipsize, memsize))
            {
                pos += zipsize;

                // unpack data entries
                byte* ptr = buff;
                byte* end = ptr + memsize;
                while (ptr < end)
                {
                    byte keysize = *ptr++;
                    byte* key = ptr; ptr += keysize;
                    byte datatypeval = *ptr++;
                    uint datasize; Buffer.MemoryCopy(ptr, &datasize, sizeof(uint), sizeof(uint));
                    ptr += sizeof(uint);
                    uint zipsizeval; Buffer.MemoryCopy(ptr, &zipsizeval, sizeof(uint), sizeof(uint));
                    ptr += sizeof(uint);
                    metadata.addLmdEntry((byte)(keysize - 1), key, datatypeval, datasize, pos, zipsizeval, ref metaDataMemUsed);
                    pos += zipsizeval;
                }
            }
            PtexMem.Delete(buff);
        }

        protected void readEditData()
        {
            // determine file range to scan for edits
            long pos = _editdatapos, endpos;
            if (_extheader.editdatapos > 0)
            {
                // newer files record the edit data position and size in the extheader
                // note: position will be non-zero even if size is zero
                endpos = (long)(pos + (long)_extheader.editdatasize);
            }
            else
            {
                // must have an older file, just read until EOF
                endpos = unchecked((long)(ulong)-1);
            }

            while (pos < endpos)
            {
                seek(pos);
                // read the edit data header
                byte edittype = (byte)EditType.et_editmetadata;
                uint editsize;
                if (!readBlock(&edittype, sizeof(byte), /*reporterror*/ false)) break;
                if (!readBlock(&editsize, sizeof(uint), /*reporterror*/ false)) break;
                if (editsize == 0) break;
                _hasEdits = true;
                pos = tell() + editsize;
                switch ((EditType)edittype)
                {
                    case EditType.et_editfacedata: readEditFaceData(); break;
                    case EditType.et_editmetadata: readEditMetaData(); break;
                }
            }
            increaseMemUsed((ulong)(_faceedits.Capacity * 40 + _metaedits.Capacity * 16));
        }

        protected void readEditFaceData()
        {
            // read header
            EditFaceDataHeader efdh;
            if (!readBlock(&efdh, PtexIO.EditFaceDataHeaderSize)) return;

            // update face info
            int faceid = (int)efdh.faceid;
            if (faceid < 0 || (uint)faceid >= _header.nfaces) return;
            _faceinfo[faceid] = efdh.faceinfo;
            _faceinfo[faceid].flags |= FaceInfo.flag_hasedits;

            // read const value now
            byte* constdata = _constdata + _pixelsize * faceid;
            if (!readBlock(constdata, _pixelsize)) return;
            if (_premultiply && _header.hasAlpha())
                PtexUtils.multalpha(constdata, 1, datatype(), _header.nchannels, _header.alphachan);

            // update header info for remaining data
            if (!_faceinfo[faceid].isConstant())
            {
                FaceEdit e = new FaceEdit();
                e.pos = tell();
                e.faceid = faceid;
                e.fdh = efdh.fdh;
                _faceedits.Add(e);
            }
        }

        protected void readEditMetaData()
        {
            // read header
            EditMetaDataHeader emdh;
            if (!readBlock(&emdh, PtexIO.EditMetaDataHeaderSize)) return;

            // record header info for later
            MetaEdit e = new MetaEdit();
            e.pos = tell();
            e.zipsize = (int)emdh.metadatazipsize;
            e.memsize = (int)emdh.metadatamemsize;
            _metaedits.Add(e);
        }

        protected void setError(string error)
        {
            string msg = error;
            msg += " PtexFile: ";
            msg += _path;
            msg += "\n";
            if (_err != null) _err.reportError(msg);
            else Console.Error.Write(msg);
            _ok = false;
        }

        protected long tell() { return _pos; }
        protected void seek(long pos)
        {
            if (_fp == IntPtr.Zero && !reopenFP()) return;
            logBlockRead();
            if (pos != _pos)
            {
                _io.seek(_fp, pos);
                _pos = pos;
            }
        }

        protected bool readBlock(void* data, int size, bool reporterror = true)
        {
            System.Diagnostics.Debug.Assert(_fp != IntPtr.Zero && size >= 0);
            if (_fp == IntPtr.Zero || size < 0) return false;
            int result = (int)_io.read(data, (ulong)size, _fp);
            if (result == size)
            {
                _pos += size;
                return true;
            }
            if (reporterror)
                setError("PtexReader error: read failed (EOF)");
            return false;
        }

        protected bool readZipBlock(void* data, int zipsize, int unzipsize)
        {
            if (zipsize < 0 || unzipsize < 0) return false;

            // gather the compressed block, one BlockSize file read at a time like the C++
            byte[] zipped = new byte[zipsize];
            byte* buff = stackalloc byte[PtexIO.BlockSize];
            int zpos = 0;
            int remaining = zipsize;
            while (true)
            {
                int size = (remaining < PtexIO.BlockSize) ? remaining : PtexIO.BlockSize;
                remaining -= size;
                if (!readBlock(buff, size)) return false;
                Marshal.Copy((IntPtr)buff, zipped, zpos, size);
                zpos += size;
                if (remaining == 0) break;
            }

            // inflate: the stream must produce exactly unzipsize bytes
            long total = 0;
            try
            {
                using (ZLibStream zs = new ZLibStream(new MemoryStream(zipped, 0, zpos), CompressionMode.Decompress))
                {
                    byte* dst = (byte*)data;
                    while (total < unzipsize)
                    {
                        int n = zs.Read(new Span<byte>(dst + total, unzipsize - (int)total));
                        if (n <= 0) break;
                        total += n;
                    }
                    if (total == unzipsize && zs.ReadByte() >= 0)
                        throw new InvalidDataException();  // output buffer too small (Z_BUF_ERROR)
                }
            }
            catch (InvalidDataException)
            {
                setError("PtexReader error: unzip failed, file corrupt");
                return false;
            }

            return total == unzipsize;
        }

        protected Level getLevel(int levelid)
        {
            ref Level level = ref _levels[levelid];
            if (level == null) readLevel(levelid, ref level);
            return level;
        }

        protected byte* getConstData() { return _constdata; }
        protected FaceData getFace(int levelid, Level level, int faceid, Res res)
        {
            ref FaceData face = ref level.faces[faceid];
            if (face == null) readFace(levelid, level, faceid, res);
            return face;
        }

        protected void readLevel(int levelid, ref Level level)
        {
            // get read lock and make sure we still need to read
            using (new AutoMutex(readlock))
            {
                if (level != null)
                {
                    return;
                }

                // go ahead and read the level
                LevelInfo l = _levelinfo[levelid];

                // keep new level local until finished
                Level newlevel = new Level((int)l.nfaces);
                seek(_levelpos[levelid]);
                fixed (FaceDataHeader* fp = newlevel.fdh)
                    readZipBlock(fp, (int)l.levelheadersize, PtexIO.FaceDataHeaderSize * (int)l.nfaces);
                computeOffsets(tell(), (int)l.nfaces, newlevel.fdh, newlevel.offsets);

                // apply edits (if any) to level 0
                if (levelid == 0)
                {
                    for (int i = 0, size = _faceedits.Count; i < size; i++)
                    {
                        FaceEdit e = _faceedits[i];
                        newlevel.fdh[e.faceid] = e.fdh;
                        newlevel.offsets[e.faceid] = e.pos;
                    }
                }

                // don't assign to result until level data is fully initialized
                PtexPlatform.AtomicStore(ref level, newlevel);
                increaseMemUsed(level.memUsed());
            }
        }

        protected void readFace(int levelid, Level level, int faceid, Res res)
        {
            FaceDataHeader fdh = level.fdh[faceid];
            readFaceData(level.offsets[faceid], fdh, res, levelid, ref level.faces[faceid]);
        }

        internal void readFaceData(long pos, FaceDataHeader fdh, Res res, int levelid, ref FaceData face)
        {
            using (new AutoMutex(readlock))
            {
                if (face != null)
                {
                    return;
                }

                // keep new face local until fully initialized
                FaceData newface = null;
                ulong newMemUsed = 0;

                seek(pos);
                switch (fdh.encoding())
                {
                    case Encoding.enc_constant:
                    {
                        ConstantFace cf = new ConstantFace(_pixelsize);
                        newface = cf;
                        newMemUsed = (ulong)(48 + _pixelsize);
                        readBlock(cf.data(), _pixelsize);
                        if (levelid == 0 && _premultiply && _header.hasAlpha())
                            PtexUtils.multalpha(cf.data(), 1, datatype(), _header.nchannels, _header.alphachan);
                    }
                    break;
                    case Encoding.enc_tiled:
                    {
                        Res tileres;
                        readBlock(&tileres, sizeof(Res));
                        uint tileheadersize;
                        readBlock(&tileheadersize, sizeof(uint));
                        TiledFace tf = new TiledFace(this, res, tileres, levelid);
                        newface = tf;
                        newMemUsed = tf.memUsed();
                        fixed (FaceDataHeader* fp = tf._fdh)
                            readZipBlock(fp, (int)tileheadersize, PtexIO.FaceDataHeaderSize * tf._ntiles);
                        computeOffsets(tell(), tf._ntiles, tf._fdh, tf._offsets);
                    }
                    break;
                    case Encoding.enc_zipped:
                    case Encoding.enc_diffzipped:
                    {
                        int uw = res.u(), vw = res.v();
                        int npixels = uw * vw;
                        int unpackedSize = _pixelsize * npixels;
                        PackedFace pf = new PackedFace(res, _pixelsize, unpackedSize);
                        newface = pf;
                        newMemUsed = (ulong)(48 + unpackedSize);
                        bool useNew = unpackedSize > PtexIO.AllocaMax;
                        byte* tmp = PtexMem.NewBytes(unpackedSize);
                        readZipBlock(tmp, (int)fdh.blocksize(), unpackedSize);
                        if (fdh.encoding() == Encoding.enc_diffzipped)
                            PtexUtils.decodeDifference(tmp, unpackedSize, datatype());
                        PtexUtils.interleave(tmp, uw * PtexGlobals.DataSize(datatype()), uw, vw, pf.data(), uw * _pixelsize,
                                             datatype(), _header.nchannels);
                        if (levelid == 0 && _premultiply && _header.hasAlpha())
                            PtexUtils.multalpha(pf.data(), npixels, datatype(), _header.nchannels, _header.alphachan);
                        PtexMem.Delete(tmp);
                    }
                    break;
                }

                if (newface == null) newface = errorData();

                PtexPlatform.AtomicStore(ref face, newface);
                increaseMemUsed(newMemUsed);
            }
        }

        public override void getData(int faceid, void* buffer, int stride)
        {
            FaceInfo f = getFaceInfo(faceid);
            getData(faceid, buffer, stride, f.res);
        }

        public override void getData(int faceid, void* buffer, int stride, Res res)
        {
            if (!_ok || faceid < 0 || (uint)faceid >= _header.nfaces)
            {
                fixed (byte* ep = _errorPixel)
                    PtexUtils.fill(ep, buffer, stride, res.u(), res.v(), _pixelsize);
                return;
            }

            // note - all locking is handled in called getData methods
            int resu = res.u(), resv = res.v();
            int rowlen = _pixelsize * resu;
            if (stride == 0) stride = rowlen;

            using (PtexPtr<PtexFaceData> dp = new PtexPtr<PtexFaceData>(getData(faceid, res)))
            {
                PtexFaceData d = dp.get();
                if (d.isConstant())
                {
                    // fill dest buffer with pixel value
                    PtexUtils.fill(d.getData(), buffer, stride, resu, resv, _pixelsize);
                }
                else if (d.isTiled())
                {
                    // loop over tiles
                    Res tileres = d.tileRes();
                    int ntilesu = res.ntilesu(tileres);
                    int ntilesv = res.ntilesv(tileres);
                    int tileures = tileres.u();
                    int tilevres = tileres.v();
                    int tilerowlen = _pixelsize * tileures;
                    int tile = 0;
                    byte* dsttilerow = (byte*)buffer;
                    for (int i = 0; i < ntilesv; i++)
                    {
                        byte* dsttile = dsttilerow;
                        for (int j = 0; j < ntilesu; j++)
                        {
                            using (PtexPtr<PtexFaceData> tp = new PtexPtr<PtexFaceData>(d.getTile(tile++)))
                            {
                                PtexFaceData t = tp.get();
                                if (t.isConstant())
                                    PtexUtils.fill(t.getData(), dsttile, stride, tileures, tilevres, _pixelsize);
                                else
                                    PtexUtils.copy(t.getData(), tilerowlen, dsttile, stride, tilevres, tilerowlen);
                            }
                            dsttile += tilerowlen;
                        }
                        dsttilerow += stride * tilevres;
                    }
                }
                else
                {
                    PtexUtils.copy(d.getData(), rowlen, buffer, stride, resv, rowlen);
                }
            }
        }

        public override PtexFaceData getData(int faceid)
        {
            if (!_ok || faceid < 0 || (uint)faceid >= _header.nfaces)
            {
                return errorData(/*deleteOnRelease*/ true);
            }

            ref FaceInfo fi = ref _faceinfo[faceid];
            if (fi.isConstant() || fi.res == new Res((ushort)0))
            {
                return new ConstDataPtr(getConstData() + faceid * _pixelsize, _pixelsize);
            }

            // get level zero (full) res face
            Level level = getLevel(0);
            FaceData face = getFace(0, level, faceid, fi.res);
            return face;
        }

        public override PtexFaceData getData(int faceid, Res res)
        {
            if (!_ok || faceid < 0 || (uint)faceid >= _header.nfaces)
            {
                return errorData(/*deleteOnRelease*/ true);
            }

            ref FaceInfo fi = ref _faceinfo[faceid];
            if (fi.isConstant() || res == new Res((ushort)0))
            {
                return new ConstDataPtr(getConstData() + faceid * _pixelsize, _pixelsize);
            }

            // determine how many reduction levels are needed
            int redu = fi.res.ulog2 - res.ulog2, redv = fi.res.vlog2 - res.vlog2;

            if (redu == 0 && redv == 0)
            {
                // no reduction - get level zero (full) res face
                Level level = getLevel(0);
                FaceData face0 = getFace(0, level, faceid, res);
                return face0;
            }

            if (redu == redv && !fi.hasEdits())
            {
                // reduction is symmetric and non-negative
                // and face has no edits => access data from reduction level (if present)
                int levelid = redu;
                if ((uint)levelid < (uint)_levels.Length)
                {
                    Level level = getLevel(levelid);

                    // get reduction face id
                    int rfaceid = (int)_rfaceids[faceid];

                    // get the face data (if present)
                    FaceData rface = null;
                    if ((uint)rfaceid < (uint)level.faces.Length)
                    {
                        rface = getFace(levelid, level, rfaceid, res);
                    }
                    if (rface != null)
                    {
                        return rface;
                    }
                }
            }

            // dynamic reduction required - look in dynamic reduction cache
            ReductionKey key = new ReductionKey((uint)faceid, res);
            FaceData face = _reductions.get(key);
            if (face != null)
            {
                return face;
            }

            // not found,  generate new reduction
            FaceData newface = null;
            ulong newMemUsed = 0;

            if (res.ulog2 < 0 || res.vlog2 < 0)
            {
                Console.Error.WriteLine("PtexReader::getData - reductions below 1 pixel not supported");
                newface = errorData();
            }
            else if (redu < 0 || redv < 0)
            {
                Console.Error.WriteLine("PtexReader::getData - enlargements not supported");
                newface = errorData();
            }
            else if (_header.meshtype == (uint)MeshType.mt_triangle)
            {
                if (redu != redv)
                {
                    Console.Error.WriteLine("PtexReader::getData - anisotropic reductions not supported for triangle mesh");
                    newface = errorData();
                }
                else
                {
                    using (PtexPtr<PtexFaceData> psrc = new PtexPtr<PtexFaceData>(getData(faceid, new Res((sbyte)(res.ulog2 + 1), (sbyte)(res.vlog2 + 1)))))
                    {
                        FaceData src = (FaceData)psrc.get();
                        newface = src.reduce(this, res, PtexUtils.reduceTri, ref newMemUsed);
                    }
                }
            }
            else
            {
                // determine which direction to blend
                bool blendu;
                if (redu == redv)
                {
                    // for symmetric face blends, alternate u and v blending
                    blendu = (res.ulog2 & 1) != 0;
                }
                else blendu = redu > redv;

                if (blendu)
                {
                    // get next-higher u-res and reduce in u
                    using (PtexPtr<PtexFaceData> psrc = new PtexPtr<PtexFaceData>(getData(faceid, new Res((sbyte)(res.ulog2 + 1), (sbyte)res.vlog2))))
                    {
                        FaceData src = (FaceData)psrc.get();
                        newface = src.reduce(this, res, PtexUtils.reduceu, ref newMemUsed);
                    }
                }
                else
                {
                    // get next-higher v-res and reduce in v
                    using (PtexPtr<PtexFaceData> psrc = new PtexPtr<PtexFaceData>(getData(faceid, new Res((sbyte)res.ulog2, (sbyte)(res.vlog2 + 1)))))
                    {
                        FaceData src = (FaceData)psrc.get();
                        newface = src.reduce(this, res, PtexUtils.reducev, ref newMemUsed);
                    }
                }
            }

            ulong tableNewMemUsed = 0;
            face = _reductions.tryInsert(key, newface, ref tableNewMemUsed);
            if (face != newface)
            {
                newface.Dispose();
            }
            else
            {
                increaseMemUsed(newMemUsed + tableNewMemUsed);
            }
            return face;
        }

        public override void getPixel(int faceid, int u, int v, float* result, int firstchan, int nchannelsArg)
        {
            for (int i = 0; i < nchannelsArg; ++i) result[i] = 0;  // memset(result, 0, sizeof(*result)*nchannelsArg)

            // clip nchannels against actual number available
            nchannelsArg = PtexUtils.min(nchannelsArg, _header.nchannels - firstchan);
            if (nchannelsArg <= 0) return;

            // get raw pixel data
            using (PtexPtr<PtexFaceData> data = new PtexPtr<PtexFaceData>(getData(faceid)))
            {
                byte* pixel = stackalloc byte[_pixelsize];
                data.get().getPixel(u, v, pixel);

                // adjust for firstchan offset
                int datasize = PtexGlobals.DataSize(datatype());
                if (firstchan != 0)
                    pixel = pixel + datasize * firstchan;

                // convert/copy to result as needed
                if (datatype() == DataType.dt_float)
                    Buffer.MemoryCopy(pixel, result, datasize * nchannelsArg, datasize * nchannelsArg);
                else
                    PtexGlobals.ConvertToFloat(result, pixel, datatype(), nchannelsArg);
            }
        }

        public override void getPixel(int faceid, int u, int v, float* result, int firstchan, int nchannelsArg, Res res)
        {
            // (the C++ memsets nchannelsArg *bytes* here, i.e. only the first part of result)
            for (int i = 0; i < nchannelsArg && i < nchannelsArg; ++i) ((byte*)result)[i] = 0;

            // clip nchannels against actual number available
            nchannelsArg = PtexUtils.min(nchannelsArg, _header.nchannels - firstchan);
            if (nchannelsArg <= 0) return;

            // get raw pixel data
            using (PtexPtr<PtexFaceData> data = new PtexPtr<PtexFaceData>(getData(faceid, res)))
            {
                byte* pixel = stackalloc byte[_pixelsize];
                data.get().getPixel(u, v, pixel);

                // adjust for firstchan offset
                int datasize = PtexGlobals.DataSize(datatype());
                if (firstchan != 0)
                    pixel = pixel + datasize * firstchan;

                // convert/copy to result as needed
                if (datatype() == DataType.dt_float)
                    Buffer.MemoryCopy(pixel, result, datasize * nchannelsArg, datasize * nchannelsArg);
                else
                    PtexGlobals.ConvertToFloat(result, pixel, datatype(), nchannelsArg);
            }
        }

        protected FaceData errorData(bool deleteOnRelease = false)
        {
            fixed (byte* ep = _errorPixel)
                return new ErrorFace(ep, _pixelsize, deleteOnRelease);
        }

        protected void computeOffsets(long pos, int noffsets, FaceDataHeader[] fdh, long[] offsets)
        {
            for (int i = 0; i < noffsets; ++i) { offsets[i] = pos; pos += fdh[i].blocksize(); }
        }

        // The default file input handler (FILE* based in the C++)
        protected sealed class DefaultInputHandler : PtexInputHandler
        {
            string _lastError = "";
            public override IntPtr open(string path)
            {
                try
                {
                    FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, PtexIO.IBuffSize);
                    return GCHandle.ToIntPtr(GCHandle.Alloc(fs));
                }
                catch (Exception e)
                {
                    _lastError = e is FileNotFoundException || e is DirectoryNotFoundException ? "No such file or directory" : e.Message;
                    return IntPtr.Zero;
                }
            }
            public override void seek(IntPtr handle, long pos)
            {
                ((FileStream)GCHandle.FromIntPtr(handle).Target).Seek(pos, SeekOrigin.Begin);
            }
            public override ulong read(void* bufferArg, ulong size, IntPtr handle)
            {
                FileStream fs = (FileStream)GCHandle.FromIntPtr(handle).Target;
                ulong total = 0;
                byte* dst = (byte*)bufferArg;
                while (total < size)
                {
                    int n = fs.Read(new Span<byte>(dst + total, (int)Math.Min(size - total, 1UL << 30)));
                    if (n <= 0) return 0;  // fread(...) == 1 ? size : 0
                    total += (ulong)n;
                }
                return size;
            }
            public override bool close(IntPtr handle)
            {
                if (handle == IntPtr.Zero) return false;
                GCHandle h = GCHandle.FromIntPtr(handle);
                ((FileStream)h.Target).Dispose();
                h.Free();
                return true;
            }
            public override string lastError() { return _lastError; }
        }

        protected Mutex readlock = new Mutex();
        protected DefaultInputHandler _defaultIo;  // Default IO handler
        protected PtexInputHandler _io;            // IO handler
        protected PtexErrorHandler _err;           // Error handler
        protected bool _premultiply;               // true if reader should premultiply the alpha chan
        protected bool _ok;                        // flag set to false if open or read error occurred
        protected bool _needToOpen;                // true if file needs to be opened (or reopened after a purge)
        protected bool _pendingPurge;              // true if a purge attempt was made but file was busy
        protected IntPtr _fp;                      // file pointer
        protected long _pos;                       // current seek position
        protected string _path;                    // current file path
        protected Header _header;                  // the header
        protected ExtHeader _extheader;            // extended header
        protected long _faceinfopos;               // file positions of data sections
        protected long _constdatapos;              // ...
        protected long _levelinfopos;
        protected long _leveldatapos;
        protected long _metadatapos;
        protected long _lmdheaderpos;
        protected long _lmddatapos;
        protected long _editdatapos;
        protected int _pixelsize;                  // size of a pixel in bytes
        protected byte* _constdata;                // constant pixel value per face
        protected MetaData _metadata;              // meta data (read on demand)
        protected bool _hasEdits;                  // has edit blocks

        protected FaceInfo[] _faceinfo;            // per-face header info
        protected uint[] _rfaceids;                // faceids sorted in reduction order
        protected LevelInfo[] _levelinfo;          // per-level header info
        protected long[] _levelpos;                // file position of each level's data
        protected Level[] _levels;                 // level data (read on demand)

        protected struct MetaEdit
        {
            public long pos;
            public int zipsize;
            public int memsize;
        }
        protected List<MetaEdit> _metaedits = new List<MetaEdit>();

        protected struct FaceEdit
        {
            public long pos;
            public int faceid;
            public FaceDataHeader fdh;
        }
        protected List<FaceEdit> _faceedits = new List<FaceEdit>();

        protected sealed class ReductionKey : IPtexKey<ReductionKey>
        {
            long _val;

            public ReductionKey() { _val = -1; }
            public ReductionKey(uint faceid, Res res)
            {
                unchecked
                {
                    _val = (long)faceid << 32 | (uint)(16777619 * ((res.val() << 16) ^ (int)faceid));
                }
            }

            public void copy(ReductionKey key) { _val = key._val; }
            public void move(ReductionKey key) { _val = key._val; }
            public bool matches(ReductionKey key) { return _val == key._val; }
            public bool isEmpty() { return _val == -1; }
            public uint hash() { return (uint)_val; }
        }
        protected PtexHashMap<ReductionKey, FaceData> _reductions = new PtexHashMap<ReductionKey, FaceData>();
        protected byte[] _errorPixel = new byte[0];  // referenced by errorData()

        protected ulong _baseMemUsed;
        protected ulong _memUsed;
        protected ulong _opens;
        protected ulong _blockReads;
    }
}
