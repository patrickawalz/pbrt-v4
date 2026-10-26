// Ptexture.h
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

// Public API classes for reading, writing, caching, and filtering Ptex files.
//
// Port notes: the C++ API objects (PtexTexture, PtexFaceData, PtexMetaData, PtexCache,
// PtexFilter, PtexWriter) are abstract classes released through release(); the pointers into
// internal storage returned by the C++ API are raw (native) pointers here too. Ptex::String is
// std::string, i.e. System.String. The static factory functions declared in this header
// (PtexTexture::open, PtexCache::create, PtexFilter::getFilter, PtexWriter::open/edit/
// applyEdits) are defined in the partial class parts found in PtexReader.cs, PtexCache.cs,
// PtexFilters.cs and PtexWriter.cs.

using System;
using System.Runtime.InteropServices;

namespace Ptex
{
    // Type of base mesh for which the textures are defined.  A mesh can be triangle-based (with
    // triangular textures) or quad-based (with rectangular textures).
    public enum MeshType
    {
        mt_triangle,  // Mesh is triangle-based.
        mt_quad       // Mesh is quad-based.
    }

    // Type of data stored in texture file.
    public enum DataType
    {
        dt_uint8,   // Unsigned, 8-bit integer.
        dt_uint16,  // Unsigned, 16-bit integer.
        dt_half,    // Half-precision (16-bit) floating point.
        dt_float    // Single-precision (32-bit) floating point.
    }

    // How to handle mesh edges when filtering.
    public enum EdgeFilterMode
    {
        efm_none,   // Don't do anything with the values.
        efm_tanvec  // Values are vectors in tangent space; rotate values.
    }

    // How to handle texture access beyond face boundaries.
    public enum BorderMode
    {
        m_clamp,    // texel access is clamped to border
        m_black,    // texel beyond border are assumed to be black
        m_periodic  // texel access wraps to other side of face
    }

    // Edge IDs used in adjacency data in the Ptex::FaceInfo struct.
    // Edge ID usage for triangle meshes is TBD.
    public enum EdgeId
    {
        e_bottom,  // Bottom edge, from UV (0,0) to (1,0)
        e_right,   // Right edge, from UV (1,0) to (1,1)
        e_top,     // Top edge, from UV (1,1) to (0,1)
        e_left     // Left edge, from UV (0,1) to (0,0)
    }

    // Type of meta data entry.
    public enum MetaDataType
    {
        mdt_string,  // Null-terminated string.
        mdt_int8,    // Signed 8-bit integer.
        mdt_int16,   // Signed 16-bit integer.
        mdt_int32,   // Signed 32-bit integer.
        mdt_float,   // Single-precision (32-bit) floating point.
        mdt_double   // Double-precision (32-bit) floating point.
    }

    // Free functions declared in Ptexture.h (the non-inline ones are defined in PtexUtils.cs)
    public static unsafe partial class PtexGlobals
    {
        // Look up size of given data type (in bytes).
        public static int DataSize(DataType dt)
        {
            int[] sizes = { 1, 2, 2, 4 };
            return sizes[(int)dt];
        }

        // Look up value of given data type that corresponds to the normalized value of 1.0.
        public static float OneValue(DataType dt)
        {
            float[] one = { 255f, 65535f, 1f, 1f };
            return one[(int)dt];
        }

        // Lookup up inverse value of given data type that corresponds to the normalized value of 1.0.
        public static float OneValueInv(DataType dt)
        {
            float[] one = { 1f / 255f, 1f / 65535f, 1f, 1f };
            return one[(int)dt];
        }
    }

    // Pixel resolution of a given texture.  The resolution is stored in log form:
    // ulog2 = log2(ures), vlog2 = log2(vres)).
    // Note: negative ulog2 or vlog2 values are reserved for internal use.
    public struct Res
    {
        public sbyte ulog2;  // log base 2 of u resolution, in texels
        public sbyte vlog2;  // log base 2 of v resolution, in texels

        public Res(sbyte ulog2_, sbyte vlog2_) { ulog2 = ulog2_; vlog2 = vlog2_; }
        public Res(ushort value) { ulog2 = (sbyte)(byte)(value & 0xff); vlog2 = (sbyte)(byte)((value >> 8) & 0xff); }

        // U resolution in texels.
        public readonly int u() { return 1 << (int)(uint)ulog2; }
        // V resolution in texels.
        public readonly int v() { return 1 << (int)(uint)vlog2; }
        // Resolution as a single 16-bit integer value.
        public readonly ushort val() { return (ushort)(ulog2 | (vlog2 << 8)); }
        // Total size of specified texture in texels (u * v).
        public readonly int size() { return u() * v(); }

        public static bool operator ==(Res a, Res r) { return r.ulog2 == a.ulog2 && r.vlog2 == a.vlog2; }
        public static bool operator !=(Res a, Res r) { return !(r == a); }
        public static bool operator >=(Res a, Res r) { return a.ulog2 >= r.ulog2 && a.vlog2 >= r.vlog2; }
        // (not in the C++ but required by C# when >= is defined)
        public static bool operator <=(Res a, Res r) { return r >= a; }
        public override bool Equals(object obj) { return obj is Res r && r == this; }
        public override int GetHashCode() { return val(); }

        // Swap the u and v resolution values in place.
        public readonly Res swappeduv() { return new Res(vlog2, ulog2); }
        public void swapuv() { this = swappeduv(); }

        // Clamp the resolution value against the given value.
        public void clamp(Res r)
        {
            if (ulog2 > r.ulog2) ulog2 = r.ulog2;
            if (vlog2 > r.vlog2) vlog2 = r.vlog2;
        }

        // Determine the number of tiles in the u direction for the given tile resolution.
        public readonly int ntilesu(Res tileres) { return 1 << (ulog2 - tileres.ulog2); }
        // Determine the number of tiles in the v direction for the given tile resolution.
        public readonly int ntilesv(Res tileres) { return 1 << (vlog2 - tileres.vlog2); }
        // Determine the total number of tiles for the given tile resolution.
        public readonly int ntiles(Res tileres) { return ntilesu(tileres) * ntilesv(tileres); }
    }

    // Information about a face, as stored in the Ptex file header.  The FaceInfo data contains
    // the face resolution and neighboring face adjacency information as well as a set of flags
    // describing the face.
    // The adjfaces data member contains the face ids of the four neighboring faces. The
    // neighbors are accessed in EdgeId order, CCW, starting with the bottom edge.
    // The adjedges data member contains the corresponding edge id for each neighboring face.
    // If a face has no neighbor for a given edge, the adjface id should be -1, and the adjedge
    // id doesn't matter (but is typically zero).
    // If an adjacent face is a pair of subfaces, the id of the first subface as encountered in
    // a CCW traversal should be stored as the adjface id.
    public unsafe struct FaceInfo
    {
        public Res res;           // Resolution of face.
        public byte adjedges;     // Adjacent edges, 2 bits per edge.
        public byte flags;        // Flags.
        public fixed int adjfaces[4];  // Adjacent faces (-1 == no adjacent face).

        public FaceInfo(Res res_)
        {
            res = res_;
            adjedges = 0;
            flags = 0;
            adjfaces[0] = adjfaces[1] = adjfaces[2] = adjfaces[3] = -1;
        }

        public FaceInfo(Res res_, int[] adjfaces_, int[] adjedges_, bool isSubface_ = false)
        {
            res = res_;
            adjedges = 0;
            flags = (byte)(isSubface_ ? flag_subface : 0);
            adjfaces[0] = adjfaces[1] = adjfaces[2] = adjfaces[3] = -1;
            setadjfaces(adjfaces_[0], adjfaces_[1], adjfaces_[2], adjfaces_[3]);
            setadjedges(adjedges_[0], adjedges_[1], adjedges_[2], adjedges_[3]);
        }

        // Default state of a C++ FaceInfo(): res (0,0), no flags, no adjacent faces
        public static FaceInfo Default()
        {
            FaceInfo f = new FaceInfo(new Res());
            return f;
        }

        // Access an adjacent edge id.  The eid value must be 0..3.
        public readonly EdgeId adjedge(int eid) { return (EdgeId)((adjedges >> (2 * eid)) & 3); }
        // Access an adjacent face id.  The eid value must be 0..3.
        public readonly int adjface(int eid) { fixed (int* p = adjfaces) return p[eid]; }
        // Determine if face is constant (by checking a flag).
        public readonly bool isConstant() { return (flags & flag_constant) != 0; }
        // Determine if neighborhood of face is constant (by checking a flag).
        public readonly bool isNeighborhoodConstant() { return (flags & flag_nbconstant) != 0; }
        // Determine if face has edits in the file (by checking a flag).
        public readonly bool hasEdits() { return (flags & flag_hasedits) != 0; }
        // Determine if face is a subface (by checking a flag).
        public readonly bool isSubface() { return (flags & flag_subface) != 0; }

        // Set the adjfaces data (and optionally the adjedges data).
        public void setadjfaces(int f0, int f1, int f2, int f3)
        {
            adjfaces[0] = f0; adjfaces[1] = f1; adjfaces[2] = f2; adjfaces[3] = f3;
        }
        public void setadjedges(int e0, int e1, int e2, int e3)
        {
            adjedges = (byte)((e0 & 3) | ((e1 & 3) << 2) | ((e2 & 3) << 4) | ((e3 & 3) << 6));
        }

        public const int flag_constant = 1, flag_hasedits = 2, flag_nbconstant = 4, flag_subface = 8;
    }

    // Meta data accessor. Meta data is acquired from PtexTexture and accessed through this
    // interface. (The `value` pointers returned point into storage owned by the meta data
    // object, as in the C++.)
    public abstract unsafe class PtexMetaData
    {
        // Release resources held by this pointer (pointer becomes invalid).
        public abstract void release();

        // Query number of meta data entries.
        public abstract int numKeys();

        // Query the meta data entry at a given index.
        public abstract void getKey(int index, out string key, out MetaDataType type);

        // Query the meta data entry by name. Returns true if found, with its index and type.
        public abstract bool findKey(string key, out int index, out MetaDataType type);

        // Query the value of a given meta data entry. If the key doesn't exist or the type
        // doesn't match, value is set to null
        public abstract void getValue(string key, out string value);
        public abstract void getValue(int index, out string value);
        public abstract void getValue(string key, out sbyte* value, out int count);
        public abstract void getValue(int index, out sbyte* value, out int count);
        public abstract void getValue(string key, out short* value, out int count);
        public abstract void getValue(int index, out short* value, out int count);
        public abstract void getValue(string key, out int* value, out int count);
        public abstract void getValue(int index, out int* value, out int count);
        public abstract void getValue(string key, out float* value, out int count);
        public abstract void getValue(int index, out float* value, out int count);
        public abstract void getValue(string key, out double* value, out int count);
        public abstract void getValue(int index, out double* value, out int count);
    }

    // Per-face texture data accessor. Per-face texture data is acquired from PtexTexture and
    // accessed through this interface. This interface provides low-level access to the data as
    // stored on disk for maximum efficiency.
    public abstract unsafe class PtexFaceData
    {
        // Release resources held by this pointer (pointer becomes invalid).
        public abstract void release();

        // True if this data block is constant.
        public abstract bool isConstant();

        // Resolution of the texture held by this data block.  Note: the indicated texture res
        // may be larger than 1x1 even if the texture data is constant.
        public abstract Res res();

        // Read a single texel from the data block.
        public abstract void getPixel(int u, int v, void* result);

        // Access the data from this data block.
        public abstract void* getData();

        // True if this data block is tiled.
        public abstract bool isTiled();

        // Resolution of each tile in this data block.
        public abstract Res tileRes();

        // Access a tile from the data block.
        public abstract PtexFaceData getTile(int tile);
    }

    // Interface for reading data from a ptex file. PtexTexture instances can be acquired via
    // the static open() method, or via the PtexCache interface. Data access through this
    // interface is returned in v-major order with all data channels interleaved per texel.
    public abstract unsafe partial class PtexTexture
    {
        // (static PtexTexture* open(const char* path, Ptex::String& error, bool premultiply=0)
        // is defined in PtexReader.cs)

        // Release resources held by this pointer (pointer becomes invalid).
        public abstract void release();

        // Path that file was opened with.
        public abstract string path();

        // Various per-file information
        public struct Info
        {
            public MeshType meshType;
            public DataType dataType;
            public BorderMode uBorderMode;
            public BorderMode vBorderMode;
            public EdgeFilterMode edgeFilterMode;
            public int alphaChannel;
            public int numChannels;
            public int numFaces;
        }

        // Get most commonly used info in a single call for efficiency.
        public abstract Info getInfo();

        // Type of mesh for which texture data is defined.
        public abstract MeshType meshType();
        // Type of data stored in file.
        public abstract DataType dataType();
        // Mode for filtering texture access beyond mesh border.
        public abstract BorderMode uBorderMode();
        public abstract BorderMode vBorderMode();
        // Mode for filtering textures across edges.
        public abstract EdgeFilterMode edgeFilterMode();
        // Index of alpha channel (if any).  One channel in the file can be flagged to be the
        // alpha channel.  If no channel is acting as the alpha channel, -1 is returned.
        public abstract int alphaChannel();
        // Number of channels stored in file.
        public abstract int numChannels();
        // Number of faces stored in file.
        public abstract int numFaces();
        // True if the file has edit blocks.
        public abstract bool hasEdits();
        // True if the file has mipmaps.
        public abstract bool hasMipMaps();

        // Access meta data.
        public abstract PtexMetaData getMetaData();

        // Access resolution and adjacency information about a face.
        public abstract ref readonly FaceInfo getFaceInfo(int faceid);

        // Access texture data for a face at highest-resolution.  The texture data is copied
        // into the user-supplied buffer. The buffer must be at least this size (in bytes):
        // DataSize(dataType()) * numChannels() * getFaceInfo(faceid).res.size().
        // If a stride is given, then (stride-row_length) bytes will be skipped after each row.
        // If stride is zero, then no bytes will be skipped.  Note: the image can be flipped
        // vertically by using an appropriate negative stride value.
        public abstract void getData(int faceid, void* buffer, int stride);

        // Access texture data for a face at a specific resolution (see the previous getData).
        public abstract void getData(int faceid, void* buffer, int stride, Res res);

        // Access texture data for a face at highest-resolution as a PtexFaceData.
        public abstract PtexFaceData getData(int faceid);

        // Access texture data for a face at a specific resolution as a PtexFaceData.
        public abstract PtexFaceData getData(int faceid, Res res);

        // Access a single texel from the highest-resolution texture. The texel data is
        // converted to floating point (integer types are normalized 0.0 to 1.0).
        public abstract void getPixel(int faceid, int u, int v, float* result, int firstchan, int nchannels);

        // Access a single texel for a face at a specific resolution.
        public abstract void getPixel(int faceid, int u, int v, float* result, int firstchan, int nchannels, Res res);
    }

    // Custom handler interface for intercepting and redirecting Ptex input stream calls. A
    // custom instance of this class can be defined and supplied to the PtexCache class.
    public abstract unsafe class PtexInputHandler
    {
        // Open a file in read mode. Returns null (IntPtr.Zero) if there was an error.
        public abstract IntPtr open(string path);
        // Seek to an absolute byte position in the input stream.
        public abstract void seek(IntPtr handle, long pos);
        // Read a number of bytes from the file. Returns the number of bytes successfully read.
        public abstract ulong read(void* buffer, ulong size, IntPtr handle);
        // Close a file. Returns false if an error occurs while closing the file.
        public abstract bool close(IntPtr handle);
        // Return the last error message encountered.
        public abstract string lastError();
    }

    // Custom handler interface redirecting Ptex error messages.
    public abstract class PtexErrorHandler
    {
        public abstract void reportError(string error);
    }

    // File-handle and memory cache for reading ptex files.
    public abstract partial class PtexCache
    {
        // (static PtexCache* create(...) is defined in PtexCache.cs)

        // Release resources held by this pointer (pointer becomes invalid).
        public abstract void release();

        // Set a search path for finding textures.
        public abstract void setSearchPath(string path);
        // Query the search path.
        public abstract string getSearchPath();

        // Open a texture, or access one from the cache.
        public abstract PtexTexture get(string path, ref string error);

        // Remove a texture from the cache.
        public abstract void purge(PtexTexture texture);
        public abstract void purge(string path);
        // Remove all textures from the cache.
        public abstract void purgeAll();

        // Cache statistics
        public struct Stats
        {
            public ulong memUsed;
            public ulong peakMemUsed;
            public ulong filesOpen;
            public ulong peakFilesOpen;
            public ulong filesAccessed;
            public ulong fileReopens;
            public ulong blockReads;
        }

        // Query a snapshot of the internal cache stats.
        public abstract void getStats(out Stats stats);
    }

    // Interface for writing data to a ptex file. (The static open/edit/applyEdits are defined
    // in PtexWriter.cs)
    public abstract unsafe partial class PtexWriter
    {
        // Close the file. This must be called before release().
        public abstract void release();

        // Set border modes
        public abstract void setBorderModes(BorderMode uBorderMode, BorderMode vBorderMode);
        // Set edge filter mode
        public abstract void setEdgeFilterMode(EdgeFilterMode edgeFilterMode);

        // Write a string as meta data
        public abstract void writeMeta(string key, string @string);
        public abstract void writeMeta(string key, sbyte* value, int count);
        public abstract void writeMeta(string key, short* value, int count);
        public abstract void writeMeta(string key, int* value, int count);
        public abstract void writeMeta(string key, float* value, int count);
        public abstract void writeMeta(string key, double* value, int count);
        // Copy all metadata from a PtexMetaData instance
        public abstract void writeMeta(PtexMetaData data);

        // Write texture data for a face.
        public abstract bool writeFace(int faceid, in FaceInfo info, void* data, int stride = 0);

        // Write constant texture data for a face.
        public abstract bool writeConstantFace(int faceid, in FaceInfo info, void* data);

        // Close the file.  An error string will be returned if the close fails.
        public abstract bool close(ref string error);
    }

    // Interface for filtered sampling of ptex data files.
    public abstract unsafe partial class PtexFilter
    {
        // (static PtexFilter* getFilter(PtexTexture* tx, const Options& opts) is defined in
        // PtexFilters.cs)

        public enum FilterType
        {
            f_point,       // Point-sampled (no filtering)
            f_bilinear,    // Bi-linear interpolation
            f_box,         // Box filter
            f_gaussian,    // Gaussian filter
            f_bicubic,     // General bi-cubic filter (uses sharpness option)
            f_bspline,     // BSpline (equivalent to bi-cubic w/ sharpness=0)
            f_catmullrom,  // Catmull-Rom (equivalent to bi-cubic w/ sharpness=1)
            f_mitchell     // Mitchell (equivalent to bi-cubic w/ sharpness=2/3)
        }

        // Filter options
        public struct Options
        {
            public int __structSize;  // (for internal use only)
            public FilterType filter; // Filter type.
            public bool lerp;         // Interpolate between mipmap levels.
            public float sharpness;   // Filter sharpness, 0..1 (for general bi-cubic filter only).
            public bool noedgeblend;  // Disable cross-face filtering.  Useful for debugging or rendering on polys.

            public Options()
            {
                __structSize = 20;  // sizeof(Options)
                filter = FilterType.f_box;
                lerp = false;
                sharpness = 0;
                noedgeblend = false;
            }

            public Options(FilterType filter_, bool lerp_ = false, float sharpness_ = 0, bool noedgeblend_ = false)
            {
                __structSize = 20;  // sizeof(Options)
                filter = filter_;
                lerp = lerp_;
                sharpness = sharpness_;
                noedgeblend = noedgeblend_;
            }
        }

        // Release resources held by this pointer (pointer becomes invalid).
        public abstract void release();

        // Apply filter to a ptex data file. The filter region is a parallelogram centered at
        // the given (u,v) coordinate with sides defined by two vectors [uw1, vw1] and
        // [uw2, vw2].  For an axis-aligned rectangle, the vectors are [uw, 0] and [0, vw].
        public abstract void eval(float* result, int firstchan, int nchannels, int faceid, float u, float v, float uw1,
                                  float vw1, float uw2, float vw2, float width = 1, float blur = 0);
    }

    // Smart-pointer for acquiring and releasing API objects. All public API objects must be
    // released back to the Ptex library via the release() method.  This smart-pointer class can
    // wrap any of the Ptex API objects and will automatically release the object when it is
    // disposed (use with `using`).
    public interface IPtexReleasable
    {
        void release();
    }

    public sealed class PtexPtr<T> : IDisposable where T : class
    {
        T _ptr;

        public PtexPtr(T ptr = null) { _ptr = ptr; }
        public static implicit operator T(PtexPtr<T> p) { return p._ptr; }
        public T get() { return _ptr; }
        public static implicit operator bool(PtexPtr<T> p) { return p._ptr != null; }
        public void swap(PtexPtr<T> p)
        {
            T tmp = p._ptr;
            p._ptr = _ptr;
            _ptr = tmp;
        }
        public void reset(T ptr = null)
        {
            Release();
            _ptr = ptr;
        }
        public void Dispose() { Release(); _ptr = null; }

        void Release()
        {
            if (_ptr == null) return;
            if (_ptr is PtexMetaData md) md.release();
            else if (_ptr is PtexFaceData fd) fd.release();
            else if (_ptr is PtexTexture tx) tx.release();
            else if (_ptr is PtexCache c) c.release();
            else if (_ptr is PtexWriter w) w.release();
            else if (_ptr is PtexFilter f) f.release();
        }
    }
}
