// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/memory.h, util/memory.cpp
//
// C++ polymorphic allocators / arena allocation have no counterpart under a GC. Allocator is an
// empty placeholder that keeps pbrt's signatures intact; ScratchBuffer keeps the Alloc/Reset API but
// objects are ordinary managed allocations (C# "placement new" is just new).

using System;
using System.Diagnostics;
using System.Threading;

namespace pbrt
{

    // pstd::pmr::polymorphic_allocator<> / pbrt::Allocator placeholder
    public sealed class Allocator
    {
        public Allocator() { }
        public static readonly Allocator Default = new Allocator();
        public T[] allocate_object<T>(size_t n) { return new T[n]; }
        public T new_object<T>() where T : new() { return new T(); }
    }

    public static partial class Globals
    {
        public static size_t GetCurrentRSS()
        {
            return (size_t)Process.GetCurrentProcess().WorkingSet64;
        }
    }

    // TrackedMemoryResource: tracks allocation totals reported by callers.
    public class TrackedMemoryResource
    {
        public void Allocated(size_t size)
        {
            ulong current = (ulong)Interlocked.Add(ref allocatedBytes, (long)size);
            long prevMax = Interlocked.Read(ref maxAllocatedBytes);
            while ((ulong)prevMax < current)
            {
                long seen = Interlocked.CompareExchange(ref maxAllocatedBytes, (long)current, prevMax);
                if (seen == prevMax) break;
                prevMax = seen;
            }
        }
        public void Deallocated(size_t bytes) { Interlocked.Add(ref allocatedBytes, -(long)bytes); }
        public size_t CurrentAllocatedBytes() { return (size_t)Interlocked.Read(ref allocatedBytes); }
        public size_t MaxAllocatedBytes() { return (size_t)Interlocked.Read(ref maxAllocatedBytes); }

        private long allocatedBytes, maxAllocatedBytes;
    }

    // ScratchBuffer Definition
    public class ScratchBuffer
    {
        // ScratchBuffer Public Methods
        public ScratchBuffer(int size = 256) { allocSize = size; }

        // T Alloc<T>(args...): placement-new of a reference-type object (or boxed value);
        // callers pass the already-constructed object, e.g. scratchBuffer.Alloc(new DiffuseBxDF(r)).
        public T Alloc<T>(T obj) { return obj; }

        // T[] Alloc<T[]>(n)
        public T[] AllocArray<T>(size_t n = 1) where T : new()
        {
            T[] ret = new T[n];
            for (size_t i = 0; i < n; ++i)
                ret[i] = new T();
            return ret;
        }

        public void Reset() { offset = 0; }

        // ScratchBuffer Private Members
        private int allocSize = 0, offset = 0;
    }

}  // namespace pbrt
