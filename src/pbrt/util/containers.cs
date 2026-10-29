// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/containers.h
//
// TypePack and friends belong to TaggedPointer's template machinery and are replaced by
// abstract-class hierarchies. InlinedVector is not ported (List<T> is used where needed).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace pbrt
{

    // Array2D Definition
    public class Array2D<T> : IEnumerable<T>
    {
        // Array2D Public Methods
        public Array2D() : this(new Bounds2i(new Point2i(0, 0), new Point2i(0, 0))) { }
        public Array2D(Allocator allocator) : this() { }

        public Array2D(Bounds2i extent, Allocator allocator = null)
        {
            this.extent = extent;
            int n = extent.Area();
            values = new T[n];
        }

        public Array2D(Bounds2i extent, T def, Allocator allocator = null)
            : this(extent, allocator)
        {
            Array.Fill(values, def);
        }

        public Array2D(IEnumerable<T> src, int nx, int ny, Allocator allocator = null)
            : this(new Bounds2i(new Point2i(0, 0), new Point2i(nx, ny)), allocator)
        {
            int i = 0;
            foreach (T v in src)
                values[i++] = v;
        }

        public Array2D(int nx, int ny, Allocator allocator = null)
            : this(new Bounds2i(new Point2i(0, 0), new Point2i(nx, ny)), allocator) { }

        public Array2D(int nx, int ny, T def, Allocator allocator = null)
            : this(new Bounds2i(new Point2i(0, 0), new Point2i(nx, ny)), def, allocator) { }

        // Array2D(const Array2D &a)
        public Array2D(Array2D<T> a, Allocator allocator = null)
            : this(a.values, a.XSize(), a.YSize(), allocator) { }

        // T &operator[](Point2i p)
        public ref T this[Point2i p]
        {
            get
            {
                DCHECK(InsideExclusive(p, extent));
                p.x -= extent.pMin.x;
                p.y -= extent.pMin.y;
                return ref values[p.x + (extent.pMax.x - extent.pMin.x) * p.y];
            }
        }

        // T &operator()(int x, int y)
        public ref T this[int x, int y]
        {
            get { return ref this[new Point2i(x, y)]; }
        }

        public int size() { return extent.Area(); }
        public int XSize() { return extent.pMax.x - extent.pMin.x; }
        public int YSize() { return extent.pMax.y - extent.pMin.y; }

        public T[] data() { return values; }

        public IEnumerator<T> GetEnumerator()
        {
            int n = size();
            for (int i = 0; i < n; ++i)
                yield return values[i];
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public static implicit operator pstd.span<T>(Array2D<T> a)
        {
            return new pstd.span<T>(a.values, 0, a.size());
        }

        public Bounds2i Extent() { return extent; }

        public override string ToString()
        {
            System.Text.StringBuilder s = new System.Text.StringBuilder();
            s.Append($"[ Array2D extent: {extent} values: [");
            for (int y = extent.pMin.y; y < extent.pMax.y; ++y)
            {
                s.Append(" [ ");
                for (int x = extent.pMin.x; x < extent.pMax.x; ++x)
                {
                    T value = this[x, y];
                    s.Append($"{value}, ");
                }
                s.Append("], ");
            }
            s.Append(" ] ]");
            return s.ToString();
        }

        // Array2D Private Members
        private Bounds2i extent;
        private T[] values;
    }

    // HashMap Definition (open-addressing table in C++; a Dictionary here)
    public class HashMap<Key, Value>
    {

        public size_t size() { return (size_t)dict.Count; }
        public size_t capacity() { return (size_t)Math.Max(8, dict.EnsureCapacity(0)); }
        public void Clear() { dict.Clear(); }
        public HashMap() { }
        public HashMap(Allocator alloc) { }

        public void Insert(Key key, Value value)
        {
            dict[key] = value;
        }

        public bool HasKey(Key key) { return dict.ContainsKey(key); }

        public Value this[Key key]
        {
            get
            {
                Value v;
                if (!dict.TryGetValue(key, out v))
                    LOG_FATAL("HashMap: key not found");
                return v;
            }
        }

        public IEnumerator<KeyValuePair<Key, Value>> GetEnumerator() { return dict.GetEnumerator(); }

        private readonly Dictionary<Key, Value> dict = new Dictionary<Key, Value>();
    }

    // SampledGrid Definition
    public class SampledGrid<T>
    {
        // SampledGrid Public Methods
        public SampledGrid() { values = new List<T>(); }
        public SampledGrid(Allocator alloc) { values = new List<T>(); }
        public SampledGrid(pstd.span<T> v, int nx, int ny, int nz, Allocator alloc = null)
        {
            values = new List<T>((int)v.size());
            for (int i = 0; i < (int)v.size(); ++i)
                values.Add(v[i]);
            this.nx = nx;
            this.ny = ny;
            this.nz = nz;
            CHECK_EQ(nx * ny * nz, values.Count);
        }
        public SampledGrid(T[] v, int nx, int ny, int nz, Allocator alloc = null)
            : this(new pstd.span<T>(v), nx, ny, nz, alloc) { }

        public size_t BytesAllocated() { return (size_t)values.Count * (size_t)System.Runtime.CompilerServices.Unsafe.SizeOf<T>(); }
        public int XSize() { return nx; }
        public int YSize() { return ny; }
        public int ZSize() { return nz; }

        public IEnumerator<T> GetEnumerator() { return values.GetEnumerator(); }

        // template <typename F> auto Lookup(Point3f p, F convert) const
        public Float Lookup(Point3f p, Func<T, Float> convert)
        {
            // Compute voxel coordinates and offsets for _p_
            Point3f pSamples = new Point3f(p.x * nx - .5f, p.y * ny - .5f, p.z * nz - .5f);
            Point3i pi = (Point3i)Floor(pSamples);
            Vector3f d = pSamples - (Point3f)pi;

            // Return trilinearly interpolated voxel values
            Float d00 = Lerp(d.x, Lookup(pi, convert), Lookup(pi + new Vector3i(1, 0, 0), convert));
            Float d10 = Lerp(d.x, Lookup(pi + new Vector3i(0, 1, 0), convert),
                         Lookup(pi + new Vector3i(1, 1, 0), convert));
            Float d01 = Lerp(d.x, Lookup(pi + new Vector3i(0, 0, 1), convert),
                         Lookup(pi + new Vector3i(1, 0, 1), convert));
            Float d11 = Lerp(d.x, Lookup(pi + new Vector3i(0, 1, 1), convert),
                         Lookup(pi + new Vector3i(1, 1, 1), convert));
            return Lerp(d.z, Lerp(d.y, d00, d10), Lerp(d.y, d01, d11));
        }

        public SampledSpectrum Lookup(Point3f p, Func<T, SampledSpectrum> convert)
        {
            // Compute voxel coordinates and offsets for _p_
            Point3f pSamples = new Point3f(p.x * nx - .5f, p.y * ny - .5f, p.z * nz - .5f);
            Point3i pi = (Point3i)Floor(pSamples);
            Vector3f d = pSamples - (Point3f)pi;

            // Return trilinearly interpolated voxel values
            SampledSpectrum d00 = Lerp(d.x, Lookup(pi, convert), Lookup(pi + new Vector3i(1, 0, 0), convert));
            SampledSpectrum d10 = Lerp(d.x, Lookup(pi + new Vector3i(0, 1, 0), convert),
                         Lookup(pi + new Vector3i(1, 1, 0), convert));
            SampledSpectrum d01 = Lerp(d.x, Lookup(pi + new Vector3i(0, 0, 1), convert),
                         Lookup(pi + new Vector3i(1, 0, 1), convert));
            SampledSpectrum d11 = Lerp(d.x, Lookup(pi + new Vector3i(0, 1, 1), convert),
                         Lookup(pi + new Vector3i(1, 1, 1), convert));
            return Lerp(d.z, Lerp(d.y, d00, d10), Lerp(d.y, d01, d11));
        }

        public R Lookup<R>(Point3i p, Func<T, R> convert)
        {
            Bounds3i sampleBounds = new Bounds3i(new Point3i(0, 0, 0), new Point3i(nx, ny, nz));
            if (!InsideExclusive(p, sampleBounds))
                return convert(outsideValue);
            return convert(values[(p.z * ny + p.y) * nx + p.x]);
        }

        public T Lookup(Point3i p)
        {
            Bounds3i sampleBounds = new Bounds3i(new Point3i(0, 0, 0), new Point3i(nx, ny, nz));
            if (!InsideExclusive(p, sampleBounds))
                return outsideValue;
            return values[(p.z * ny + p.y) * nx + p.x];
        }

        public Float MaxValue(Bounds3f bounds, Func<T, Float> convert)
        {
            Point3f[] ps = {new Point3f(bounds.pMin.x * nx - .5f, bounds.pMin.y * ny - .5f,
                                        bounds.pMin.z * nz - .5f),
                            new Point3f(bounds.pMax.x * nx - .5f, bounds.pMax.y * ny - .5f,
                                        bounds.pMax.z * nz - .5f)};
            Point3i[] pi = {Max((Point3i)Floor(ps[0]), new Point3i(0, 0, 0)),
                            Min((Point3i)Floor(ps[1]) + new Vector3i(1, 1, 1),
                                new Point3i(nx - 1, ny - 1, nz - 1))};

            Float maxValue = Lookup(pi[0], convert);
            for (int z = pi[0].z; z <= pi[1].z; ++z)
            {
                for (int y = pi[0].y; y <= pi[1].y; ++y)
                {
                    for (int x = pi[0].x; x <= pi[1].x; ++x)
                    {
                        Float v = Lookup(new Point3i(x, y, z), convert);
                        if (v > maxValue)
                        {
                            maxValue = v;  // std::max(maxValue, v)
                        }
                    }
                }
            }
            return maxValue;
        }

        public override string ToString()
        {
            return $"[ SampledGrid nx: {nx} ny: {ny} nz: {nz} values: {values.Count} ]";
        }

        // SampledGrid Private Members
        // (C++ returns T{} for lookups outside the grid; for reference types, such as the spectrum
        // classes, that is a default-constructed instance)
        private static readonly T outsideValue =
            typeof(T).IsValueType ? default(T) : (T)Activator.CreateInstance(typeof(T));
        private List<T> values;
        private int nx, ny, nz;
    }

    // Overloads of SampledGrid<T> that need arithmetic on T itself (C++ templates instantiate them lazily)
    public static class SampledGridExtensions
    {
        public static Float Lookup(this SampledGrid<Float> g, Point3f p)
        {
            return g.Lookup(p, (Float v) => v);
        }

        public static Float MaxValue(this SampledGrid<Float> g, Bounds3f bounds)
        {
            return g.MaxValue(bounds, (Float v) => v);
        }
    }

    // InternCache Definition: returns a canonical reference for each distinct item.
    public class InternCache<T> where T : class, IEquatable<T>
    {
        public InternCache(Allocator alloc = null) { }

        public T Lookup(T item)
        {
            return Lookup(item, (Allocator a, T i) => i);
        }

        public T Lookup(T item, Func<Allocator, T, T> create)
        {
            lock (sync)
            {
                T existing;
                if (table.TryGetValue(item, out existing))
                    return existing;
                T newItem = create(null, item);
                table[newItem] = newItem;
                return newItem;
            }
        }

        public size_t size() { lock (sync) { return (size_t)table.Count; } }
        public size_t capacity() { lock (sync) { return (size_t)Math.Max(256, table.Count * 4); } }

        private readonly object sync = new object();
        private readonly Dictionary<T, T> table = new Dictionary<T, T>();
    }

}  // namespace pbrt
