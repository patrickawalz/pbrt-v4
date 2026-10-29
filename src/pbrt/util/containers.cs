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

    // SampledGrid Definition: values live in aligned native memory, so T must be unmanaged. The T-specific
    // interpolation (C++ templates instantiated lazily) is in SampledGridExtensions below.
    public unsafe class SampledGrid<T> : IDisposable
        where T : unmanaged
    {
        // SampledGrid Public Methods
        public SampledGrid()
        {
        }

        public SampledGrid(Allocator alloc)
        {
        }

        public SampledGrid(pstd.span<T> v, int nx, int ny, int nz, Allocator alloc = null)
        {
            this.nx = nx;
            this.ny = ny;
            this.nz = nz;
            count = (int)v.size();
            CHECK_EQ(nx * ny * nz, count);
            if (count > 0)
            {
                values = AllocAligned<T>((size_t)count);
                for (int i = 0; i < count; ++i)
                    values[i] = v[i];
            }
        }

        public SampledGrid(T[] v, int nx, int ny, int nz, Allocator alloc = null)
            : this(new pstd.span<T>(v), nx, ny, nz, alloc)
        {
        }

        ~SampledGrid()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            // values is unmanaged memory, so it is freed whether called from Dispose() or the finalizer;
            // there are no managed members to release when disposing is true
            if (values != null)
            {
                FreeAligned(values);
                values = null;
            }
            disposed = true;
        }

        public size_t BytesAllocated()
        {
            return (size_t)count * (size_t)sizeof(T);
        }

        public int XSize()
        {
            return nx;
        }

        public int YSize()
        {
            return ny;
        }

        public int ZSize()
        {
            return nz;
        }

        public T Lookup(Point3i p)
        {
            Bounds3i sampleBounds = new Bounds3i(new Point3i(0, 0, 0), new Point3i(nx, ny, nz));
            if (!InsideExclusive(p, sampleBounds))
                return default;
            return values[(p.z * ny + p.y) * nx + p.x];
        }

        public override string ToString()
        {
            return $"[ SampledGrid nx: {nx} ny: {ny} nz: {nz} values: {count} ]";
        }

        // SampledGrid Private Members
        private T* values;
        private int count;
        private int nx, ny, nz;
        private bool disposed;
    }

    // RGBUnboundedVoxel Definition: the value stored per voxel of a SampledGrid<RGBUnboundedVoxel>. In C++ the grid
    // holds RGBUnboundedSpectrum values directly; here the spectrum types are classes, so this unmanaged struct
    // holds the same data (scale and RGBSigmoidPolynomial) inline.
    public struct RGBUnboundedVoxel
    {
        public static RGBUnboundedVoxel FromRGB(RGBColorSpace cs, RGB rgb)
        {
            RGBUnboundedVoxel v;
            Float m = std.max(rgb.r, std.max(rgb.g, rgb.b));
            v.scale = 2 * m;
            v.rsp = cs.ToRGBCoeffs(v.scale != 0 ? rgb / v.scale : new RGB(0, 0, 0));
            return v;
        }

        public SampledSpectrum Sample(SampledWavelengths lambda)
        {
            SampledSpectrum s = new SampledSpectrum();
            for (int i = 0; i < NSpectrumSamples; ++i)
                s[i] = scale * rsp.Evaluate(lambda[i]);
            return s;
        }

        public Float MaxValue()
        {
            return scale * rsp.MaxValue();
        }

        public Float scale;
        public RGBSigmoidPolynomial rsp;
    }

    // RGBIlluminantVoxel Definition: the value stored per voxel of a SampledGrid<RGBIlluminantVoxel>. The illuminant
    // is shared by every voxel of a grid, so it is passed to the lookup instead of being stored per voxel.
    public struct RGBIlluminantVoxel
    {
        public static RGBIlluminantVoxel FromRGB(RGBColorSpace cs, RGB rgb)
        {
            RGBIlluminantVoxel v;
            Float m = std.max(rgb.r, std.max(rgb.g, rgb.b));
            v.scale = 2 * m;
            v.rsp = cs.ToRGBCoeffs(v.scale != 0 ? rgb / v.scale : new RGB(0, 0, 0));
            return v;
        }

        public SampledSpectrum Sample(SampledWavelengths lambda, SampledSpectrum illuminant)
        {
            SampledSpectrum s = new SampledSpectrum();
            for (int i = 0; i < NSpectrumSamples; ++i)
                s[i] = scale * rsp.Evaluate(lambda[i]);
            return s * illuminant;
        }

        public Float scale;
        public RGBSigmoidPolynomial rsp;
    }

    // SampledGrid<T> operations that depend on T (the C++ Lookup/MaxValue templates, instantiated for the three
    // element types that are used)
    public static class SampledGridExtensions
    {
        // SampledGrid<Float>
        public static Float Lookup(this SampledGrid<Float> g, Point3f p)
        {
            // Compute voxel coordinates and offsets for _p_
            Point3f pSamples = new Point3f(p.x * g.XSize() - .5f, p.y * g.YSize() - .5f, p.z * g.ZSize() - .5f);
            Point3i pi = (Point3i)Floor(pSamples);
            Vector3f d = pSamples - (Point3f)pi;

            // Return trilinearly interpolated voxel values
            Float d00 = Lerp(d.x, g.Lookup(pi), g.Lookup(pi + new Vector3i(1, 0, 0)));
            Float d10 = Lerp(d.x, g.Lookup(pi + new Vector3i(0, 1, 0)), g.Lookup(pi + new Vector3i(1, 1, 0)));
            Float d01 = Lerp(d.x, g.Lookup(pi + new Vector3i(0, 0, 1)), g.Lookup(pi + new Vector3i(1, 0, 1)));
            Float d11 = Lerp(d.x, g.Lookup(pi + new Vector3i(0, 1, 1)), g.Lookup(pi + new Vector3i(1, 1, 1)));
            return Lerp(d.z, Lerp(d.y, d00, d10), Lerp(d.y, d01, d11));
        }

        public static Float MaxValue(this SampledGrid<Float> g, Bounds3f bounds)
        {
            int nx = g.XSize();
            int ny = g.YSize();
            int nz = g.ZSize();
            Point3f[] ps = {new Point3f(bounds.pMin.x * nx - .5f, bounds.pMin.y * ny - .5f,
                                        bounds.pMin.z * nz - .5f),
                            new Point3f(bounds.pMax.x * nx - .5f, bounds.pMax.y * ny - .5f,
                                        bounds.pMax.z * nz - .5f)};
            Point3i[] pi = {Max((Point3i)Floor(ps[0]), new Point3i(0, 0, 0)),
                            Min((Point3i)Floor(ps[1]) + new Vector3i(1, 1, 1),
                                new Point3i(nx - 1, ny - 1, nz - 1))};

            Float maxValue = g.Lookup(pi[0]);
            for (int z = pi[0].z; z <= pi[1].z; ++z)
            {
                for (int y = pi[0].y; y <= pi[1].y; ++y)
                {
                    for (int x = pi[0].x; x <= pi[1].x; ++x)
                    {
                        Float v = g.Lookup(new Point3i(x, y, z));
                        if (v > maxValue)
                        {
                            maxValue = v;  // std::max(maxValue, v)
                        }
                    }
                }
            }
            return maxValue;
        }

        // SampledGrid<RGBUnboundedVoxel>
        public static SampledSpectrum Lookup(this SampledGrid<RGBUnboundedVoxel> g, Point3f p, SampledWavelengths lambda)
        {
            // Compute voxel coordinates and offsets for _p_
            Point3f pSamples = new Point3f(p.x * g.XSize() - .5f, p.y * g.YSize() - .5f, p.z * g.ZSize() - .5f);
            Point3i pi = (Point3i)Floor(pSamples);
            Vector3f d = pSamples - (Point3f)pi;

            // Return trilinearly interpolated voxel values
            SampledSpectrum d00 = Lerp(d.x, g.Lookup(pi).Sample(lambda),
                                       g.Lookup(pi + new Vector3i(1, 0, 0)).Sample(lambda));
            SampledSpectrum d10 = Lerp(d.x, g.Lookup(pi + new Vector3i(0, 1, 0)).Sample(lambda),
                                       g.Lookup(pi + new Vector3i(1, 1, 0)).Sample(lambda));
            SampledSpectrum d01 = Lerp(d.x, g.Lookup(pi + new Vector3i(0, 0, 1)).Sample(lambda),
                                       g.Lookup(pi + new Vector3i(1, 0, 1)).Sample(lambda));
            SampledSpectrum d11 = Lerp(d.x, g.Lookup(pi + new Vector3i(0, 1, 1)).Sample(lambda),
                                       g.Lookup(pi + new Vector3i(1, 1, 1)).Sample(lambda));
            return Lerp(d.z, Lerp(d.y, d00, d10), Lerp(d.y, d01, d11));
        }

        public static Float MaxValue(this SampledGrid<RGBUnboundedVoxel> g, Bounds3f bounds)
        {
            int nx = g.XSize();
            int ny = g.YSize();
            int nz = g.ZSize();
            Point3f[] ps = {new Point3f(bounds.pMin.x * nx - .5f, bounds.pMin.y * ny - .5f,
                                        bounds.pMin.z * nz - .5f),
                            new Point3f(bounds.pMax.x * nx - .5f, bounds.pMax.y * ny - .5f,
                                        bounds.pMax.z * nz - .5f)};
            Point3i[] pi = {Max((Point3i)Floor(ps[0]), new Point3i(0, 0, 0)),
                            Min((Point3i)Floor(ps[1]) + new Vector3i(1, 1, 1),
                                new Point3i(nx - 1, ny - 1, nz - 1))};

            Float maxValue = g.Lookup(pi[0]).MaxValue();
            for (int z = pi[0].z; z <= pi[1].z; ++z)
            {
                for (int y = pi[0].y; y <= pi[1].y; ++y)
                {
                    for (int x = pi[0].x; x <= pi[1].x; ++x)
                    {
                        Float v = g.Lookup(new Point3i(x, y, z)).MaxValue();
                        if (v > maxValue)
                        {
                            maxValue = v;  // std::max(maxValue, v)
                        }
                    }
                }
            }
            return maxValue;
        }

        // SampledGrid<RGBIlluminantVoxel>: the grid's shared illuminant is sampled once and applied to each corner
        public static SampledSpectrum Lookup(this SampledGrid<RGBIlluminantVoxel> g, Point3f p, SampledWavelengths lambda,
                                             DenselySampledSpectrum illuminant)
        {
            SampledSpectrum illum = illuminant != null ? illuminant.Sample(lambda) : new SampledSpectrum(0f);

            // Compute voxel coordinates and offsets for _p_
            Point3f pSamples = new Point3f(p.x * g.XSize() - .5f, p.y * g.YSize() - .5f, p.z * g.ZSize() - .5f);
            Point3i pi = (Point3i)Floor(pSamples);
            Vector3f d = pSamples - (Point3f)pi;

            // Return trilinearly interpolated voxel values
            SampledSpectrum d00 = Lerp(d.x, g.Lookup(pi).Sample(lambda, illum),
                                       g.Lookup(pi + new Vector3i(1, 0, 0)).Sample(lambda, illum));
            SampledSpectrum d10 = Lerp(d.x, g.Lookup(pi + new Vector3i(0, 1, 0)).Sample(lambda, illum),
                                       g.Lookup(pi + new Vector3i(1, 1, 0)).Sample(lambda, illum));
            SampledSpectrum d01 = Lerp(d.x, g.Lookup(pi + new Vector3i(0, 0, 1)).Sample(lambda, illum),
                                       g.Lookup(pi + new Vector3i(1, 0, 1)).Sample(lambda, illum));
            SampledSpectrum d11 = Lerp(d.x, g.Lookup(pi + new Vector3i(0, 1, 1)).Sample(lambda, illum),
                                       g.Lookup(pi + new Vector3i(1, 1, 1)).Sample(lambda, illum));
            return Lerp(d.z, Lerp(d.y, d00, d10), Lerp(d.y, d01, d11));
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
