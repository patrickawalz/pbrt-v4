// PtexUtils.h, PtexUtils.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

// Ptex utility functions.
//
// Port notes: the C++ templates over the pixel type T (uint8_t, uint16_t, PtexHalf, float) are
// C# generics over an unmanaged T plus a traits struct TR (static abstract members) supplying
// the type-specific arithmetic (the overloads of halve()/quarter() and the implicit
// conversions of PtexHalf). The functions that the C++ declares in namespace Ptex
// (MeshTypeName, ConvertToFloat, ...) are in PtexGlobals; those of namespace PtexUtils are in
// the static class PtexUtils.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ptex
{
    // Type-specific arithmetic for the four Ptex data types
    public interface IPtexPixel<T> where T : unmanaged
    {
        static abstract float ToF(T v);                     // (float)v
        static abstract T FromF(float f);                   // T(f)
        static abstract T quarter4(T a, T b, T c, T d);     // T(quarter(a + b + c + d))
        static abstract T halve2(T a, T b);                 // T(halve(a + b))
        static abstract bool IsZero(T v);                   // !v
        static abstract T blendAdd(T dst, T src, float w);  // T(dst + T(w * (float)src))
        static abstract T Zero();
    }

    public struct PixU8 : IPtexPixel<byte>
    {
        public static float ToF(byte v) { return v; }
        public static byte FromF(float f) { return (byte)f; }
        public static byte quarter4(byte a, byte b, byte c, byte d) { return (byte)((a + b + c + d) >> 2); }
        public static byte halve2(byte a, byte b) { return (byte)((a + b) >> 1); }
        public static bool IsZero(byte v) { return v == 0; }
        public static byte blendAdd(byte dst, byte src, float w) { return (byte)(dst + (byte)(w * (float)src)); }
        public static byte Zero() { return 0; }
    }

    public struct PixU16 : IPtexPixel<ushort>
    {
        public static float ToF(ushort v) { return v; }
        public static ushort FromF(float f) { return (ushort)f; }
        public static ushort quarter4(ushort a, ushort b, ushort c, ushort d) { return (ushort)((a + b + c + d) >> 2); }
        public static ushort halve2(ushort a, ushort b) { return (ushort)((a + b) >> 1); }
        public static bool IsZero(ushort v) { return v == 0; }
        public static ushort blendAdd(ushort dst, ushort src, float w) { return (ushort)(dst + (ushort)(w * (float)src)); }
        public static ushort Zero() { return 0; }
    }

    public struct PixHalf : IPtexPixel<PtexHalf>
    {
        public static float ToF(PtexHalf v) { return v; }
        public static PtexHalf FromF(float f) { return new PtexHalf(f); }
        public static PtexHalf quarter4(PtexHalf a, PtexHalf b, PtexHalf c, PtexHalf d)
        {
            return new PtexHalf(0.25f * ((float)a + (float)b + (float)c + (float)d));
        }
        public static PtexHalf halve2(PtexHalf a, PtexHalf b) { return new PtexHalf(0.5f * ((float)a + (float)b)); }
        public static bool IsZero(PtexHalf v) { return (float)v == 0f; }
        public static PtexHalf blendAdd(PtexHalf dst, PtexHalf src, float w)
        {
            PtexHalf t = new PtexHalf(w * (float)src);
            return new PtexHalf((float)dst + (float)t);
        }
        public static PtexHalf Zero() { return new PtexHalf(); }
    }

    public struct PixFloat : IPtexPixel<float>
    {
        public static float ToF(float v) { return v; }
        public static float FromF(float f) { return f; }
        public static float quarter4(float a, float b, float c, float d) { return 0.25f * (a + b + c + d); }
        public static float halve2(float a, float b) { return 0.5f * (a + b); }
        public static bool IsZero(float v) { return v == 0f; }
        public static float blendAdd(float dst, float src, float w) { return dst + w * src; }
        public static float Zero() { return 0; }
    }

    // Free functions declared in Ptexture.h and defined in PtexUtils.cpp
    public static unsafe partial class PtexGlobals
    {
        public static string MeshTypeName(MeshType mt)
        {
            string[] names = { "triangle", "quad" };
            int mtype = (int)mt;
            if (mtype < 0 || mtype >= names.Length)
                return "(invalid mesh type)";
            return names[mtype];
        }

        public static string DataTypeName(DataType dt)
        {
            string[] names = { "uint8", "uint16", "float16", "float32" };
            int dtype = (int)dt;
            if (dtype < 0 || dtype >= names.Length)
                return "(invalid data type)";
            return names[dtype];
        }

        public static string BorderModeName(BorderMode m)
        {
            string[] names = { "clamp", "black", "periodic" };
            int mode = (int)m;
            if (mode < 0 || mode >= names.Length)
                return "(invalid border mode)";
            return names[mode];
        }

        public static string EdgeFilterModeName(EdgeFilterMode m)
        {
            string[] names = { "none", "tanvec" };
            int mode = (int)m;
            if (mode < 0 || mode >= names.Length)
                return "(invalid edge filter mode)";
            return names[mode];
        }

        public static string EdgeIdName(EdgeId eid)
        {
            string[] names = { "bottom", "right", "top", "left" };
            int edgeid = (int)eid;
            if (edgeid < 0 || edgeid >= names.Length)
                return "(invalid edge id)";
            return names[edgeid];
        }

        public static string MetaDataTypeName(MetaDataType mdt)
        {
            string[] names = { "string", "int8", "int16", "int32", "float", "double" };
            int mdtype = (int)mdt;
            if (mdtype < 0 || mdtype >= names.Length)
                return "(invalid meta data type)";
            return names[mdtype];
        }

        static void ConvertArrayClamped<DST, SRC, TD>(DST* dst, SRC* src, int numChannels, float scale, float round = 0)
            where DST : unmanaged where SRC : unmanaged where TD : struct, IPtexPixel<DST>
        {
            // (SRC is always float in the C++ uses of ConvertArrayClamped)
            for (int i = 0; i < numChannels; i++)
                dst[i] = TD.FromF(PtexUtils.clamp(*(float*)(src + i), 0.0f, 1.0f) * scale + round);
        }

        static void ConvertArray<DST, SRC, TD, TS>(DST* dst, SRC* src, int numChannels, float scale, float round = 0)
            where DST : unmanaged where SRC : unmanaged where TD : struct, IPtexPixel<DST> where TS : struct, IPtexPixel<SRC>
        {
            for (int i = 0; i < numChannels; i++)
                dst[i] = TD.FromF(TS.ToF(src[i]) * scale + round);
        }

        public static void ConvertToFloat(float* dst, void* src, DataType dt, int numChannels)
        {
            switch (dt)
            {
                case DataType.dt_uint8: ConvertArray<float, byte, PixFloat, PixU8>(dst, (byte*)src, numChannels, 1f / 255f); break;
                case DataType.dt_uint16: ConvertArray<float, ushort, PixFloat, PixU16>(dst, (ushort*)src, numChannels, 1f / 65535f); break;
                case DataType.dt_half: ConvertArray<float, PtexHalf, PixFloat, PixHalf>(dst, (PtexHalf*)src, numChannels, 1f); break;
                case DataType.dt_float: Buffer.MemoryCopy(src, dst, sizeof(float) * numChannels, sizeof(float) * numChannels); break;
            }
        }

        public static void ConvertFromFloat(void* dst, float* src, DataType dt, int numChannels)
        {
            switch (dt)
            {
                case DataType.dt_uint8: ConvertArrayClamped<byte, float, PixU8>((byte*)dst, src, numChannels, (float)255.0, 0.5f); break;
                case DataType.dt_uint16: ConvertArrayClamped<ushort, float, PixU16>((ushort*)dst, src, numChannels, (float)65535.0, 0.5f); break;
                case DataType.dt_half: ConvertArray<PtexHalf, float, PixHalf, PixFloat>((PtexHalf*)dst, src, numChannels, (float)1.0); break;
                case DataType.dt_float: Buffer.MemoryCopy(src, dst, sizeof(float) * numChannels, sizeof(float) * numChannels); break;
            }
        }
    }

    public unsafe delegate void ReduceFn(void* src, int sstride, int ures, int vres, void* dst, int dstride, DataType dt, int nchannels);
    public unsafe delegate void ApplyConstFn(float weight, float* dst, void* data, int nChan);

    public static unsafe class PtexUtils
    {
        public static bool isPowerOfTwo(int x)
        {
            return (x & (x - 1)) == 0;
        }

        public static uint ones(uint x)
        {
            // count number of ones
            x = (x & 0x55555555) + ((x >> 1) & 0x55555555);  // add pairs of bits
            x = (x & 0x33333333) + ((x >> 2) & 0x33333333);  // add bit pairs
            x = (x & 0x0f0f0f0f) + ((x >> 4) & 0x0f0f0f0f);  // add nybbles
            x += (x >> 8);                                   // add bytes
            x += (x >> 16);                                  // add words
            return (x & 0xff);
        }

        public static uint floor_log2(uint x)
        {
            // floor(log2(n))
            x |= (x >> 1);
            x |= (x >> 2);
            x |= (x >> 4);
            x |= (x >> 8);
            x |= (x >> 16);
            return ones(x >> 1);
        }

        public static uint ceil_log2(uint x)
        {
            // ceil(log2(n))
            bool isPow2 = isPowerOfTwo((int)x);
            x |= (x >> 1);
            x |= (x >> 2);
            x |= (x >> 4);
            x |= (x >> 8);
            x |= (x >> 16);
            return ones(x >> 1) + (isPow2 ? 0u : 1u);
        }

        public static float reciprocalPow2(int power)
        {
            // 1.0/pow(2,power)
            int i = (127 - power) << 23;
            return BitConverter.Int32BitsToSingle(i);
        }

        public static int calcResFromWidth(float w)
        {
            // read exponent directly from float32 representation
            // equiv to ceil(log2(1.0/w)) but much faster and no error
            int wi = BitConverter.SingleToInt32Bits(w);
            int result = 127 - ((wi >> 23) & 0xff);
            return result;
        }

        public static float smoothstep(float x, float a, float b)
        {
            if (x < a) return 0;
            if (x >= b) return 1;
            x = (x - a) / (b - a);
            return x * x * (3 - 2 * x);
        }

        public static float qsmoothstep(float x, float a, float b)
        {
            // quintic smoothstep (cubic is only C1)
            if (x < a) return 0;
            if (x >= b) return 1;
            x = (x - a) / (b - a);
            return x * x * x * (10 + x * (-15 + x * 6));
        }

        public static int abs(int x) { return x > 0 ? x : -x; }
        public static float abs(float x)
        {
            int i = BitConverter.SingleToInt32Bits(x);
            i &= 0x7fffffff;
            return BitConverter.Int32BitsToSingle(i);
        }

        public static int min(int a, int b) { return a < b ? a : b; }
        public static float min(float a, float b) { return a < b ? a : b; }
        public static sbyte min(sbyte a, sbyte b) { return a < b ? a : b; }
        public static int max(int a, int b) { return a > b ? a : b; }
        public static float max(float a, float b) { return a > b ? a : b; }
        public static sbyte max(sbyte a, sbyte b) { return a > b ? a : b; }

        public static int clamp(int x, int lo, int hi) { return min(max(x, lo), hi); }
        public static float clamp(float x, float lo, float hi) { return min(max(x, lo), hi); }

        public static float halve(float val) { return 0.5f * val; }
        public static float quarter(float val) { return 0.25f * val; }

        public static float floor(float f) { return MathF.Floor(f); }
        public static float ceil(float f) { return MathF.Ceiling(f); }

        public static bool isConstant(void* data, int stride, int ures, int vres, int pixelSize)
        {
            int rowlen = pixelSize * ures;
            byte* p = (byte*)data + stride;

            // compare each row with the first
            for (int i = 1; i < vres; i++, p += stride)
                if (!new ReadOnlySpan<byte>(data, rowlen).SequenceEqual(new ReadOnlySpan<byte>(p, rowlen))) return false;

            // make sure first row is constant
            p = (byte*)data + pixelSize;
            for (int i = 1; i < ures; i++, p += pixelSize)
                if (!new ReadOnlySpan<byte>(data, pixelSize).SequenceEqual(new ReadOnlySpan<byte>(p, pixelSize))) return false;

            return true;
        }

        static void interleave<T>(T* src, int sstride, int uw, int vw, T* dst, int dstride, int nchan) where T : unmanaged
        {
            sstride /= sizeof(T);
            dstride /= sizeof(T);
            // for each channel
            for (T* dstend = dst + nchan; dst != dstend; dst++)
            {
                // for each row
                T* drow = dst;
                for (T* rowend = src + sstride * vw; src != rowend; src += sstride, drow += dstride)
                {
                    // copy each pixel across the row
                    T* dp = drow;
                    for (T* sp = src, end = sp + uw; sp != end; dp += nchan)
                        *dp = *sp++;
                }
            }
        }

        public static void interleave(void* src, int sstride, int uw, int vw, void* dst, int dstride, DataType dt, int nchan)
        {
            switch (dt)
            {
                case DataType.dt_uint8: interleave((byte*)src, sstride, uw, vw, (byte*)dst, dstride, nchan); break;
                case DataType.dt_half:
                case DataType.dt_uint16: interleave((ushort*)src, sstride, uw, vw, (ushort*)dst, dstride, nchan); break;
                case DataType.dt_float: interleave((float*)src, sstride, uw, vw, (float*)dst, dstride, nchan); break;
            }
        }

        static void deinterleave<T>(T* src, int sstride, int uw, int vw, T* dst, int dstride, int nchan) where T : unmanaged
        {
            sstride /= sizeof(T);
            dstride /= sizeof(T);
            // for each channel
            for (T* srcend = src + nchan; src != srcend; src++)
            {
                // for each row
                T* srow = src;
                for (T* rowend = srow + sstride * vw; srow != rowend; srow += sstride, dst += dstride)
                {
                    // copy each pixel across the row
                    T* sp = srow;
                    for (T* dp = dst, end = dp + uw; dp != end; sp += nchan)
                        *dp++ = *sp;
                }
            }
        }

        public static void deinterleave(void* src, int sstride, int uw, int vw, void* dst, int dstride, DataType dt, int nchan)
        {
            switch (dt)
            {
                case DataType.dt_uint8: deinterleave((byte*)src, sstride, uw, vw, (byte*)dst, dstride, nchan); break;
                case DataType.dt_half:
                case DataType.dt_uint16: deinterleave((ushort*)src, sstride, uw, vw, (ushort*)dst, dstride, nchan); break;
                case DataType.dt_float: deinterleave((float*)src, sstride, uw, vw, (float*)dst, dstride, nchan); break;
            }
        }

        static void encodeDifference(byte* data, int size)
        {
            size /= sizeof(byte);
            byte* p = data, end = p + size; byte tmp, prev = 0;
            while (p != end) { tmp = prev; prev = *p; *p = (byte)(*p - tmp); p++; }
        }
        static void encodeDifference(ushort* data, int size)
        {
            size /= sizeof(ushort);
            ushort* p = data, end = p + size; ushort tmp, prev = 0;
            while (p != end) { tmp = prev; prev = *p; *p = (ushort)(*p - tmp); p++; }
        }

        public static void encodeDifference(void* data, int size, DataType dt)
        {
            switch (dt)
            {
                case DataType.dt_uint8: encodeDifference((byte*)data, size); break;
                case DataType.dt_uint16: encodeDifference((ushort*)data, size); break;
                default: break;  // skip other types
            }
        }

        static void decodeDifference(byte* data, int size)
        {
            size /= sizeof(byte);
            byte* p = data, end = p + size; byte prev = 0;
            while (p != end) { *p = (byte)(*p + prev); prev = *p++; }
        }
        static void decodeDifference(ushort* data, int size)
        {
            size /= sizeof(ushort);
            ushort* p = data, end = p + size; ushort prev = 0;
            while (p != end) { *p = (ushort)(*p + prev); prev = *p++; }
        }

        public static void decodeDifference(void* data, int size, DataType dt)
        {
            switch (dt)
            {
                case DataType.dt_uint8: decodeDifference((byte*)data, size); break;
                case DataType.dt_uint16: decodeDifference((ushort*)data, size); break;
                default: break;  // skip other types
            }
        }

        static void reduce<T, TR>(T* src, int sstride, int uw, int vw, T* dst, int dstride, int nchan)
            where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            sstride /= sizeof(T);
            dstride /= sizeof(T);
            int rowlen = uw * nchan;
            int srowskip = 2 * sstride - rowlen;
            int drowskip = dstride - rowlen / 2;
            for (T* end = src + vw * sstride; src != end; src += srowskip, dst += drowskip)
                for (T* rowend = src + rowlen; src != rowend; src += nchan)
                    for (T* pixend = src + nchan; src != pixend; src++)
                        *dst++ = TR.quarter4(src[0], src[nchan], src[sstride], src[sstride + nchan]);
        }

        public static void reduce(void* src, int sstride, int uw, int vw, void* dst, int dstride, DataType dt, int nchan)
        {
            switch (dt)
            {
                case DataType.dt_uint8: reduce<byte, PixU8>((byte*)src, sstride, uw, vw, (byte*)dst, dstride, nchan); break;
                case DataType.dt_half: reduce<PtexHalf, PixHalf>((PtexHalf*)src, sstride, uw, vw, (PtexHalf*)dst, dstride, nchan); break;
                case DataType.dt_uint16: reduce<ushort, PixU16>((ushort*)src, sstride, uw, vw, (ushort*)dst, dstride, nchan); break;
                case DataType.dt_float: reduce<float, PixFloat>((float*)src, sstride, uw, vw, (float*)dst, dstride, nchan); break;
            }
        }

        static void reduceu<T, TR>(T* src, int sstride, int uw, int vw, T* dst, int dstride, int nchan)
            where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            sstride /= sizeof(T);
            dstride /= sizeof(T);
            int rowlen = uw * nchan;
            int srowskip = sstride - rowlen;
            int drowskip = dstride - rowlen / 2;
            for (T* end = src + vw * sstride; src != end; src += srowskip, dst += drowskip)
                for (T* rowend = src + rowlen; src != rowend; src += nchan)
                    for (T* pixend = src + nchan; src != pixend; src++)
                        *dst++ = TR.halve2(src[0], src[nchan]);
        }

        public static void reduceu(void* src, int sstride, int uw, int vw, void* dst, int dstride, DataType dt, int nchan)
        {
            switch (dt)
            {
                case DataType.dt_uint8: reduceu<byte, PixU8>((byte*)src, sstride, uw, vw, (byte*)dst, dstride, nchan); break;
                case DataType.dt_half: reduceu<PtexHalf, PixHalf>((PtexHalf*)src, sstride, uw, vw, (PtexHalf*)dst, dstride, nchan); break;
                case DataType.dt_uint16: reduceu<ushort, PixU16>((ushort*)src, sstride, uw, vw, (ushort*)dst, dstride, nchan); break;
                case DataType.dt_float: reduceu<float, PixFloat>((float*)src, sstride, uw, vw, (float*)dst, dstride, nchan); break;
            }
        }

        static void reducev<T, TR>(T* src, int sstride, int uw, int vw, T* dst, int dstride, int nchan)
            where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            sstride /= sizeof(T);
            dstride /= sizeof(T);
            int rowlen = uw * nchan;
            int srowskip = 2 * sstride - rowlen;
            int drowskip = dstride - rowlen;
            for (T* end = src + vw * sstride; src != end; src += srowskip, dst += drowskip)
                for (T* rowend = src + rowlen; src != rowend; src++)
                    *dst++ = TR.halve2(src[0], src[sstride]);
        }

        public static void reducev(void* src, int sstride, int uw, int vw, void* dst, int dstride, DataType dt, int nchan)
        {
            switch (dt)
            {
                case DataType.dt_uint8: reducev<byte, PixU8>((byte*)src, sstride, uw, vw, (byte*)dst, dstride, nchan); break;
                case DataType.dt_half: reducev<PtexHalf, PixHalf>((PtexHalf*)src, sstride, uw, vw, (PtexHalf*)dst, dstride, nchan); break;
                case DataType.dt_uint16: reducev<ushort, PixU16>((ushort*)src, sstride, uw, vw, (ushort*)dst, dstride, nchan); break;
                case DataType.dt_float: reducev<float, PixFloat>((float*)src, sstride, uw, vw, (float*)dst, dstride, nchan); break;
            }
        }

        // generate a reduction of a packed-triangle texture
        // note: this method won't work for tiled textures
        static void reduceTri<T, TR>(T* src, int sstride, int w, int vw, T* dst, int dstride, int nchan)
            where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            sstride /= sizeof(T);
            dstride /= sizeof(T);
            int rowlen = w * nchan;
            T* src2 = src + (w - 1) * sstride + rowlen - nchan;
            int srowinc2 = -2 * sstride - nchan;
            int srowskip = 2 * sstride - rowlen;
            int srowskip2 = w * sstride - 2 * nchan;
            int drowskip = dstride - rowlen / 2;
            for (T* end = src + w * sstride; src != end; src += srowskip, src2 += srowskip2, dst += drowskip)
                for (T* rowend = src + rowlen; src != rowend; src += nchan, src2 += srowinc2)
                    for (T* pixend = src + nchan; src != pixend; src++, src2++)
                        *dst++ = TR.quarter4(src[0], src[nchan], src[sstride], src2[0]);
        }

        public static void reduceTri(void* src, int sstride, int w, int vw, void* dst, int dstride, DataType dt, int nchan)
        {
            switch (dt)
            {
                case DataType.dt_uint8: reduceTri<byte, PixU8>((byte*)src, sstride, w, 0, (byte*)dst, dstride, nchan); break;
                case DataType.dt_half: reduceTri<PtexHalf, PixHalf>((PtexHalf*)src, sstride, w, 0, (PtexHalf*)dst, dstride, nchan); break;
                case DataType.dt_uint16: reduceTri<ushort, PixU16>((ushort*)src, sstride, w, 0, (ushort*)dst, dstride, nchan); break;
                case DataType.dt_float: reduceTri<float, PixFloat>((float*)src, sstride, w, 0, (float*)dst, dstride, nchan); break;
            }
        }

        public static void fill(void* src, void* dst, int dstride, int ures, int vres, int pixelsize)
        {
            // fill first row
            int rowlen = ures * pixelsize;
            byte* ptr = (byte*)dst;
            byte* end = ptr + rowlen;
            for (; ptr != end; ptr += pixelsize) Buffer.MemoryCopy(src, ptr, pixelsize, pixelsize);

            // fill remaining rows from first row
            ptr = (byte*)dst + dstride;
            end = (byte*)dst + vres * dstride;
            for (; ptr != end; ptr += dstride) Buffer.MemoryCopy(dst, ptr, rowlen, rowlen);
        }

        public static void copy(void* src, int sstride, void* dst, int dstride, int vres, int rowlen)
        {
            // regular non-tiled case
            if (sstride == rowlen && dstride == rowlen)
            {
                // packed case - copy in single block
                Buffer.MemoryCopy(src, dst, (long)vres * rowlen, (long)vres * rowlen);
            }
            else
            {
                // copy a row at a time
                byte* sptr = (byte*)src;
                byte* dptr = (byte*)dst;
                for (byte* end = sptr + vres * sstride; sptr != end;)
                {
                    Buffer.MemoryCopy(sptr, dptr, rowlen, rowlen);
                    dptr += dstride;
                    sptr += sstride;
                }
            }
        }

        static void blend<T, TR>(T* src, float weight, T* dst, int rowlen, int nchan)
            where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            for (T* end = src + rowlen * nchan; src != end; dst++)
                *dst = TR.blendAdd(*dst, *src++, weight);
        }

        static void blendflip<T, TR>(T* src, float weight, T* dst, int rowlen, int nchan)
            where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            dst += (rowlen - 1) * nchan;
            for (T* end = src + rowlen * nchan; src != end;)
            {
                for (int i = 0; i < nchan; i++, dst++)
                {
                    *dst = TR.blendAdd(*dst, *src++, weight);
                }
                dst -= nchan * 2;
            }
        }

        public static void blend(void* src, float weight, void* dst, bool flip, int rowlen, DataType dt, int nchan)
        {
            switch (((int)dt << 1) | (flip ? 1 : 0))
            {
                case ((int)DataType.dt_uint8 << 1): blend<byte, PixU8>((byte*)src, weight, (byte*)dst, rowlen, nchan); break;
                case ((int)DataType.dt_uint8 << 1 | 1): blendflip<byte, PixU8>((byte*)src, weight, (byte*)dst, rowlen, nchan); break;
                case ((int)DataType.dt_half << 1): blend<PtexHalf, PixHalf>((PtexHalf*)src, weight, (PtexHalf*)dst, rowlen, nchan); break;
                case ((int)DataType.dt_half << 1 | 1): blendflip<PtexHalf, PixHalf>((PtexHalf*)src, weight, (PtexHalf*)dst, rowlen, nchan); break;
                case ((int)DataType.dt_uint16 << 1): blend<ushort, PixU16>((ushort*)src, weight, (ushort*)dst, rowlen, nchan); break;
                case ((int)DataType.dt_uint16 << 1 | 1): blendflip<ushort, PixU16>((ushort*)src, weight, (ushort*)dst, rowlen, nchan); break;
                case ((int)DataType.dt_float << 1): blend<float, PixFloat>((float*)src, weight, (float*)dst, rowlen, nchan); break;
                case ((int)DataType.dt_float << 1 | 1): blendflip<float, PixFloat>((float*)src, weight, (float*)dst, rowlen, nchan); break;
            }
        }

        static void average<T, TR>(T* src, int sstride, int uw, int vw, T* dst, int nchan)
            where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            float* buff = stackalloc float[nchan];
            for (int i = 0; i < nchan; i++) buff[i] = 0;
            sstride /= sizeof(T);
            int rowlen = uw * nchan;
            int rowskip = sstride - rowlen;
            for (T* end = src + vw * sstride; src != end; src += rowskip)
                for (T* rowend = src + rowlen; src != rowend;)
                    for (int i = 0; i < nchan; i++) buff[i] += TR.ToF(*src++);
            float scale = 1.0f / (float)(uw * vw);
            for (int i = 0; i < nchan; i++) dst[i] = TR.FromF(buff[i] * scale);
        }

        public static void average(void* src, int sstride, int uw, int vw, void* dst, DataType dt, int nchan)
        {
            switch (dt)
            {
                case DataType.dt_uint8: average<byte, PixU8>((byte*)src, sstride, uw, vw, (byte*)dst, nchan); break;
                case DataType.dt_half: average<PtexHalf, PixHalf>((PtexHalf*)src, sstride, uw, vw, (PtexHalf*)dst, nchan); break;
                case DataType.dt_uint16: average<ushort, PixU16>((ushort*)src, sstride, uw, vw, (ushort*)dst, nchan); break;
                case DataType.dt_float: average<float, PixFloat>((float*)src, sstride, uw, vw, (float*)dst, nchan); break;
            }
        }

        // (CompareRfaceIds: descending order of the smaller log2 dimension, const faces = 1)
        sealed class CompareRfaceIds : IComparer<uint>
        {
            readonly FaceInfo* faces;
            public CompareRfaceIds(FaceInfo* facesArg) { faces = facesArg; }
            public int Compare(uint faceid1, uint faceid2)
            {
                FaceInfo f1 = faces[faceid1];
                FaceInfo f2 = faces[faceid2];
                int min1 = f1.isConstant() ? 1 : min(f1.res.ulog2, f1.res.vlog2);
                int min2 = f2.isConstant() ? 1 : min(f2.res.ulog2, f2.res.vlog2);
                // comp(a, b) = min1 > min2: "a before b"
                return min1 > min2 ? -1 : (min2 > min1 ? 1 : 0);
            }
        }

        static void multalpha<T, TR>(T* data, int npixels, int nchannels, int alphachan, float scale)
            where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            int alphaoffset;  // offset to alpha chan from data ptr
            int nchanmult;    // number of channels to alpha-multiply
            if (alphachan == 0)
            {
                // first channel is alpha chan: mult the rest of the channels
                data++;
                alphaoffset = -1;
                nchanmult = nchannels - 1;
            }
            else
            {
                // mult all channels up to alpha chan
                alphaoffset = alphachan;
                nchanmult = alphachan;
            }

            for (T* end = data + npixels * nchannels; data != end; data += nchannels)
            {
                float aval = scale * TR.ToF(data[alphaoffset]);
                for (int i = 0; i < nchanmult; i++) data[i] = TR.FromF(TR.ToF(data[i]) * aval);
            }
        }

        public static void multalpha(void* data, int npixels, DataType dt, int nchannels, int alphachan)
        {
            float scale = PtexGlobals.OneValueInv(dt);
            switch (dt)
            {
                case DataType.dt_uint8: multalpha<byte, PixU8>((byte*)data, npixels, nchannels, alphachan, scale); break;
                case DataType.dt_uint16: multalpha<ushort, PixU16>((ushort*)data, npixels, nchannels, alphachan, scale); break;
                case DataType.dt_half: multalpha<PtexHalf, PixHalf>((PtexHalf*)data, npixels, nchannels, alphachan, scale); break;
                case DataType.dt_float: multalpha<float, PixFloat>((float*)data, npixels, nchannels, alphachan, scale); break;
            }
        }

        static void divalpha<T, TR>(T* data, int npixels, int nchannels, int alphachan, float scale)
            where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            int alphaoffset;  // offset to alpha chan from data ptr
            int nchandiv;     // number of channels to alpha-divide
            if (alphachan == 0)
            {
                // first channel is alpha chan: div the rest of the channels
                data++;
                alphaoffset = -1;
                nchandiv = nchannels - 1;
            }
            else
            {
                // div all channels up to alpha chan
                alphaoffset = alphachan;
                nchandiv = alphachan;
            }

            for (T* end = data + npixels * nchannels; data != end; data += nchannels)
            {
                T alpha = data[alphaoffset];
                if (TR.IsZero(alpha)) continue;  // don't divide by zero!
                float aval = scale / TR.ToF(alpha);
                for (int i = 0; i < nchandiv; i++) data[i] = TR.FromF(TR.ToF(data[i]) * aval);
            }
        }

        public static void divalpha(void* data, int npixels, DataType dt, int nchannels, int alphachan)
        {
            float scale = PtexGlobals.OneValue(dt);
            switch (dt)
            {
                case DataType.dt_uint8: divalpha<byte, PixU8>((byte*)data, npixels, nchannels, alphachan, scale); break;
                case DataType.dt_uint16: divalpha<ushort, PixU16>((ushort*)data, npixels, nchannels, alphachan, scale); break;
                case DataType.dt_half: divalpha<PtexHalf, PixHalf>((PtexHalf*)data, npixels, nchannels, alphachan, scale); break;
                case DataType.dt_float: divalpha<float, PixFloat>((float*)data, npixels, nchannels, alphachan, scale); break;
            }
        }

        public static void genRfaceids(FaceInfo* faces, int nfaces, uint* rfaceids, uint* faceids)
        {
            // stable_sort faceids by smaller dimension (u or v) in descending order
            // treat const faces as having res of 1

            // init faceids
            for (int i = 0; i < nfaces; i++) faceids[i] = (uint)i;

            // sort faceids by rfaceid (std::stable_sort: a stable merge via LINQ's OrderBy)
            uint[] tmp = new uint[nfaces];
            for (int i = 0; i < nfaces; i++) tmp[i] = faceids[i];
            CompareRfaceIds cmp = new CompareRfaceIds(faces);
            System.Linq.IOrderedEnumerable<uint> sorted = System.Linq.Enumerable.OrderBy(tmp, (uint x) => x, Comparer<uint>.Create((uint a, uint b) => cmp.Compare(a, b)));
            int k = 0;
            foreach (uint id in sorted) faceids[k++] = id;

            // generate mapping from faceid to rfaceid
            for (int i = 0; i < nfaces; i++)
            {
                // note: i is the rfaceid
                rfaceids[faceids[i]] = (uint)i;
            }
        }

        // fixed length vector accumulator: dst[i] += val[i] * weight (n values)
        public static void VecAccum<T, TR>(float* dst, T* val, float weight, int n) where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            for (int i = 0; i < n; i++) dst[i] += TR.ToF(val[i]) * weight;
        }

        // variable length vector accumulator: dst[i] += val[i] * weight
        public static void VecAccumN<T, TR>(float* dst, T* val, int nchan, float weight) where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            for (int i = 0; i < nchan; i++) dst[i] += TR.ToF(val[i]) * weight;
        }

        // fixed length vector multiplier: dst[i] = val[i] * weight (n values)
        public static void VecMult<T, TR>(float* dst, T* val, float weight, int n) where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            for (int i = 0; i < n; i++) dst[i] = TR.ToF(val[i]) * weight;
        }

        // variable length vector multiplier: dst[i] = val[i] * weight
        public static void VecMultN<T, TR>(float* dst, T* val, int nchan, float weight) where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            for (int i = 0; i < nchan; i++) dst[i] = TR.ToF(val[i]) * weight;
        }

        // apply to 1..4 channels, unrolled in the C++
        static ApplyConstFn ApplyConst<T, TR>(int n) where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            return (float weight, float* dst, void* data, int nChan) =>
            {
                // dst[i] += data[i] * weight for i in {0..n-1}
                VecAccum<T, TR>(dst, (T*)data, weight, n);
            };
        }

        // apply to N channels (general case)
        static void ApplyConstN<T, TR>(float weight, float* dst, void* data, int nChan) where T : unmanaged where TR : struct, IPtexPixel<T>
        {
            // dst[i] += data[i] * weight for i in {0..n-1}
            VecAccumN<T, TR>(dst, (T*)data, nChan, weight);
        }

        public static readonly ApplyConstFn[] applyConstFunctions = {
            ApplyConstN<byte, PixU8>,  ApplyConstN<ushort, PixU16>,  ApplyConstN<PtexHalf, PixHalf>,  ApplyConstN<float, PixFloat>,
            ApplyConst<byte, PixU8>(1), ApplyConst<ushort, PixU16>(1), ApplyConst<PtexHalf, PixHalf>(1), ApplyConst<float, PixFloat>(1),
            ApplyConst<byte, PixU8>(2), ApplyConst<ushort, PixU16>(2), ApplyConst<PtexHalf, PixHalf>(2), ApplyConst<float, PixFloat>(2),
            ApplyConst<byte, PixU8>(3), ApplyConst<ushort, PixU16>(3), ApplyConst<PtexHalf, PixHalf>(3), ApplyConst<float, PixFloat>(3),
            ApplyConst<byte, PixU8>(4), ApplyConst<ushort, PixU16>(4), ApplyConst<PtexHalf, PixHalf>(4), ApplyConst<float, PixFloat>(4),
        };

        public static void applyConst(float weight, float* dst, void* data, DataType dt, int nChan)
        {
            // dispatch specialized apply function
            ApplyConstFn fn = applyConstFunctions[((uint)nChan <= 4 ? 1 : 0) * nChan * 4 + (int)dt];
            fn(weight, dst, data, nChan);
        }
    }
}
