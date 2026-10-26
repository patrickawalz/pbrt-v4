// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Magi: C# port of pbrt-v4. File: util/buffercache.h, util/buffercache.cpp
//
// (The "const T *" buffers handed out by the cache are T[] arrays here.)

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace pbrt
{
    // BufferCache Definition
    public unsafe class BufferCache<T> where T : unmanaged
    {
        // STAT_MEMORY_COUNTER("Memory/Redundant vertex and index buffers", redundantBufferBytes);
        [ThreadStatic] private static long redundantBufferBytes;
        // STAT_PERCENT("Geometry/Buffer cache hits", nBufferCacheHits, nBufferCacheLookups);
        [ThreadStatic] private static long nBufferCacheHits, nBufferCacheLookups;
        private static StatRegisterer STATS_REGredundantBufferBytes = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/Redundant vertex and index buffers", redundantBufferBytes);
            redundantBufferBytes = 0;
        });
        private static StatRegisterer STATS_REGnBufferCacheHits = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportPercentage("Geometry/Buffer cache hits", nBufferCacheHits, nBufferCacheLookups);
            nBufferCacheHits = 0;
            nBufferCacheLookups = 0;
        });

        // BufferCache Public Methods
        public T[] LookupOrAdd(pstd.span<T> buf, Allocator alloc)
        {
            ++nBufferCacheLookups;
            // Return pointer to data if _buf_ contents are already in the cache
            Buffer lookupBuffer = new Buffer(buf.arr, buf.offset, (int)buf.size());
            int shardIndex = (int)((uint)lookupBuffer.hash >> (32 - logShards));
            DCHECK(shardIndex >= 0 && shardIndex < nShards);
            lock (mutex[shardIndex])
            {
                Buffer found;
                if (cache[shardIndex].TryGetValue(lookupBuffer, out found))
                {
                    ++nBufferCacheHits;
                    redundantBufferBytes += (long)(buf.size() * (size_t)sizeof(T));
                    return found.arr;
                }

                // Add _buf_ contents to cache and return pointer to cached copy
                T[] ptr = new T[buf.size()];
                Array.Copy(buf.arr, buf.offset, ptr, 0, (int)buf.size());
                Interlocked.Add(ref bytesUsed, (long)(buf.size() * (size_t)sizeof(T)));
                Buffer cached = new Buffer(ptr, 0, ptr.Length);
                cache[shardIndex][cached] = cached;
                return ptr;
            }
        }

        public T[] LookupOrAdd(List<T> buf, Allocator alloc)
        {
            return LookupOrAdd(new pstd.span<T>(buf.ToArray()), alloc);
        }

        public size_t BytesUsed() { return (size_t)Interlocked.Read(ref bytesUsed); }

        // BufferCache::Buffer Definition
        private sealed class Buffer : IEquatable<Buffer>
        {
            // BufferCache::Buffer Public Methods
            public Buffer(T[] arr, int offset, int size)
            {
                this.arr = arr; this.offset = offset; this.size = size;
                if (size == 0) hash = HashBuffer((T*)null, 0);
                else
                    fixed (T* p = &arr[offset])
                        hash = HashBuffer(p, (size_t)size);
            }

            public bool Equals(Buffer b)
            {
                return size == b.size && hash == b.hash &&
                       MemoryMarshal.AsBytes(new ReadOnlySpan<T>(arr, offset, size))
                           .SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(b.arr, b.offset, b.size)));
            }
            public override bool Equals(object o) { return o is Buffer b && Equals(b); }
            public override int GetHashCode() { return (int)hash; }

            public T[] arr;
            public int offset;
            public int size;
            public ulong hash;
        }

        // BufferCache Private Members
        private const int logShards = 6;
        private const int nShards = 1 << logShards;
        private readonly object[] mutex = MakeLocks();
        private readonly Dictionary<Buffer, Buffer>[] cache = MakeCaches();
        private long bytesUsed;

        private static object[] MakeLocks()
        {
            object[] l = new object[nShards];
            for (int i = 0; i < nShards; ++i) l[i] = new object();
            return l;
        }
        private static Dictionary<Buffer, Buffer>[] MakeCaches()
        {
            Dictionary<Buffer, Buffer>[] c = new Dictionary<Buffer, Buffer>[nShards];
            for (int i = 0; i < nShards; ++i) c[i] = new Dictionary<Buffer, Buffer>();
            return c;
        }
    }

    public static unsafe partial class Globals
    {
        // BufferCache Global Definitions
        public static BufferCache<int> intBufferCache;
        public static BufferCache<Point2f> point2BufferCache;
        public static BufferCache<Point3f> point3BufferCache;
        public static BufferCache<Vector3f> vector3BufferCache;
        public static BufferCache<Normal3f> normal3BufferCache;

        public static void InitBufferCaches()
        {
            CHECK(intBufferCache == null);
            intBufferCache = new BufferCache<int>();
            point2BufferCache = new BufferCache<Point2f>();
            point3BufferCache = new BufferCache<Point3f>();
            vector3BufferCache = new BufferCache<Vector3f>();
            normal3BufferCache = new BufferCache<Normal3f>();
        }
    }
}
