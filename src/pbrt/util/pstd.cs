// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/pstd.h, util/pstd.cpp
//
// pbrt's "pstd" namespace re-implements parts of the C++ standard library so they
// work on the GPU. Here it provides optional, span, vector, complex, and the few
// math wrappers pbrt-v4 calls as pstd::floor(), pstd::copysign(), etc.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public static unsafe class pstd
{

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void swap<T>(ref T a, ref T b) { T t = a; a = b; b = t; }

    // bit_cast
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static uint bit_cast_uint(float f) { return BitConverter.SingleToUInt32Bits(f); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float bit_cast_float(uint u) { return BitConverter.UInt32BitsToSingle(u); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static ulong bit_cast_ulong(double f) { return BitConverter.DoubleToUInt64Bits(f); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double bit_cast_double(ulong u) { return BitConverter.UInt64BitsToDouble(u); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float sqrt(float f) { return MathF.Sqrt(f); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double sqrt(double f) { return Math.Sqrt(f); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float abs(float f) { return MathF.Abs(f); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double abs(double f) { return Math.Abs(f); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float copysign(float mag, float sign) { return MathF.CopySign(mag, sign); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double copysign(double mag, double sign) { return Math.CopySign(mag, sign); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float floor(float arg) { return MathF.Floor(arg); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double floor(double arg) { return Math.Floor(arg); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float ceil(float arg) { return MathF.Ceiling(arg); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double ceil(double arg) { return Math.Ceiling(arg); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float round(float arg) { return MathF.Round(arg, MidpointRounding.AwayFromZero); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double round(double arg) { return Math.Round(arg, MidpointRounding.AwayFromZero); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float fmod(float x, float y) { return x % y; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double fmod(double x, double y) { return x % y; }

    // pstd::optional
    public unsafe struct optional<T>
    {
        private T v;
        private bool set;

        public optional(T v) { this.v = v; set = true; }

        public static implicit operator optional<T>(T v) { return new optional<T>(v); }
        public static implicit operator bool(optional<T> o) { return o.set; }

        public T value_or(T alt) { return set ? v : alt; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T value()
        {
            if (!set) global::pbrt.pbrt.CHECK(set);
            return v;
        }

        // Ref access for in-place modification (C++: opt->member = ..., *opt = ...)
        [System.Diagnostics.CodeAnalysis.UnscopedRef]
        public ref T Ref()
        {
            if (!set) global::pbrt.pbrt.CHECK(set);
            return ref v;
        }

        public void reset() { set = false; v = default; }

        public bool has_value() { return set; }

        public void Set(T value) { v = value; set = true; }

        public override string ToString()
        {
            if (set)
                return "[ pstd::optional<" + typeof(T).Name + "> set: true value: " + global::pbrt.pbrt.PrintObject(v) + " ]";
            else
                return "[ pstd::optional<" + typeof(T).Name + "> set: false value: n/a ]";
        }
    }

    // pstd::span: a non-owning view onto contiguous memory (managed array storage or a raw pointer).
    public readonly unsafe ref struct span<T>
    {
        public span(void* ptr, size_t n)
        {
            _reference = ref Unsafe.AsRef<T>(ptr);
            _length = n;
        }

        public ref T this[size_t i]
        {
            get
            {
                return ref Unsafe.Add(ref _reference, (nuint)i);
            }
        }

        public size_t size()
        {
            return _length;
        }

        public bool empty()
        {
            return size() == 0;
        }

        private readonly ref T _reference;
        private readonly size_t _length;

        public span(void* pointer, int length)
        {
            _reference = ref Unsafe.AsRef<T>(pointer);
            _length = (size_t)length;
        }

        public span(ref T reference, int length)
        {
            _reference = ref reference;
            _length = (size_t)length;
        }

        public ref T this[int index]
        {
            get
            {
                return ref Unsafe.Add(
                    ref _reference, (nint)(uint)index /* force zero-extension */);
            }
        }

        public span(void* pointer)
        {
            _reference = ref Unsafe.AsRef<T>(pointer);
            _length = 1;
        }

        // Views onto managed arrays (the C++ code passes pointers/containers).
        public span(T[] a)
        {
            if (a == null)
            {
                _reference = ref Unsafe.NullRef<T>();
                _length = 0;
            }
            else
            {
                _reference = ref MemoryMarshal.GetArrayDataReference(a);
                _length = (size_t)a.Length;
            }
        }

        public span(T[] a, int offset, int length)
        {
            _reference = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(a), offset);
            _length = (size_t)length;
        }

        public static implicit operator span<T>(T[] a)
        {
            return new span<T>(a);
        }

        public static implicit operator span<T>(vector<T> v)
        {
            return new span<T>(v.data_, 0, v.count);
        }

        public size_t size_bytes()
        {
            return _length * (size_t)Unsafe.SizeOf<T>();
        }

        public ref T this[long i]
        {
            get
            {
                return ref Unsafe.Add(ref _reference, (nint)i);
            }
        }

        public ref T front()
        {
            return ref _reference;
        }

        public ref T back()
        {
            return ref Unsafe.Add(ref _reference, (nint)_length - 1);
        }

        public span<T> subspan(size_t pos, size_t count)
        {
            return new span<T>(ref Unsafe.Add(ref _reference, (nint)pos), (int)count);
        }

        public span<T> subspan(size_t pos)
        {
            return new span<T>(ref Unsafe.Add(ref _reference, (nint)pos), (int)(_length - pos));
        }

        public span<T> first(size_t count)
        {
            return new span<T>(ref _reference, (int)count);
        }

        public span<T> last(size_t count)
        {
            return new span<T>(ref Unsafe.Add(ref _reference, (nint)(_length - count)), (int)count);
        }

        public Span<T> AsSpan()
        {
            return MemoryMarshal.CreateSpan(ref _reference, (int)_length);
        }

        public T[] ToArray()
        {
            return AsSpan().ToArray();
        }

        public override string ToString()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder("[ ");
            for (int i = 0; i < (int)_length; ++i)
            {
                sb.Append(global::pbrt.pbrt.PrintObject(this[i]));
                if (i < (int)_length - 1) sb.Append(", ");
            }
            return sb.Append(" ]").ToString();
        }
    }

    public static span<T> MakeSpan<T>(T[] a) { return new span<T>(a); }
    public static span<T> MakeSpan<T>(T[] a, ulong size) { return new span<T>(a, 0, (int)size); }
    public static span<T> MakeSpan<T>(vector<T> v) { return new span<T>(v.data_, 0, v.count); }
    public static span<T> MakeConstSpan<T>(T[] a) { return new span<T>(a); }
    public static span<T> MakeConstSpan<T>(T[] a, ulong size) { return new span<T>(a, 0, (int)size); }
    public static span<T> MakeConstSpan<T>(vector<T> v) { return new span<T>(v.data_, 0, v.count); }

    // pstd::vector
    public unsafe class vector<T>
    {
        internal T[] data_;
        internal int count;

        public override string ToString()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder("[ ");
            for (int i = 0; i < count; ++i)
            {
                sb.Append(global::pbrt.pbrt.PrintObject(data_[i]));
                if (i < count - 1) sb.Append(", ");
            }
            return sb.Append(" ]").ToString();
        }

        public vector() { data_ = new T[4]; count = 0; }
        public vector(ulong n) { data_ = new T[Math.Max(4, (int)n)]; count = (int)n; }
        public vector(ulong n, T v) : this(n) { for (int i = 0; i < count; ++i) data_[i] = v; }
        public vector(IEnumerable<T> src) { data_ = new List<T>(src).ToArray(); count = data_.Length; if (data_.Length == 0) data_ = new T[4]; }
        public vector(T[] src) { data_ = (T[])src.Clone(); count = src.Length; if (data_.Length == 0) data_ = new T[4]; }

        public ulong size() { return (ulong)count; }
        public bool empty() { return count == 0; }
        public ulong capacity() { return (ulong)data_.Length; }

        public ref T this[int i] { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return ref data_[i]; } }
        public ref T this[long i] { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return ref data_[(int)i]; } }
        public ref T this[ulong i] { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { return ref data_[(int)i]; } }

        public void reserve(ulong n)
        {
            if ((int)n > data_.Length) Array.Resize(ref data_, (int)n);
        }

        public ref T front() { return ref data_[0]; }
        public ref T back() { return ref data_[count - 1]; }
        public T[] data() { return data_; }
        public void clear() { Array.Clear(data_, 0, count); count = 0; }
        public void emplace_back(T v) { push_back(v); }
        public void push_back(T v)
        {
            if (count == data_.Length) Array.Resize(ref data_, data_.Length * 2);
            data_[count++] = v;
        }
        public void pop_back() { --count; data_[count] = default; }
        public void resize(ulong n)
        {
            int nn = (int)n;
            if (nn > data_.Length) Array.Resize(ref data_, Math.Max(nn, data_.Length * 2));
            if (nn < count) Array.Clear(data_, nn, count - nn);
            count = nn;
        }
        public void resize(ulong n, T v)
        {
            int old = count;
            resize(n);
            for (int i = old; i < count; ++i) data_[i] = v;
        }
        public void insert_back(IEnumerable<T> items) { foreach (T t in items) push_back(t); }
        public T[] ToArray() { T[] r = new T[count]; Array.Copy(data_, r, count); return r; }

    }

    // pstd::array<T, N> for the sizes pbrt uses
    public unsafe struct array2<T>
    {
        public T v0, v1;
        public array2(T a, T b) { v0 = a; v1 = b; }
        public T this[int i]
        {
            get { return i == 0 ? v0 : v1; }
            set { if (i == 0) v0 = value; else v1 = value; }
        }
        public int size() { return 2; }
    }

    public unsafe struct array3<T>
    {
        public T v0, v1, v2;
        public array3(T a, T b, T c) { v0 = a; v1 = b; v2 = c; }
        public T this[int i]
        {
            get { return i == 0 ? v0 : (i == 1 ? v1 : v2); }
            set { if (i == 0) v0 = value; else if (i == 1) v1 = value; else v2 = value; }
        }
        public int size() { return 3; }
    }

    public unsafe struct array4<T>
    {
        public T v0, v1, v2, v3;
        public array4(T a, T b, T c, T d) { v0 = a; v1 = b; v2 = c; v3 = d; }
        public T this[int i]
        {
            get { return i == 0 ? v0 : (i == 1 ? v1 : (i == 2 ? v2 : v3)); }
            set { if (i == 0) v0 = value; else if (i == 1) v1 = value; else if (i == 2) v2 = value; else v3 = value; }
        }
        public int size() { return 4; }
    }

    public unsafe struct array7<T>
    {
        public T v0, v1, v2, v3, v4, v5, v6;
        public array7(T a, T b, T c, T d, T e, T f, T g) { v0 = a; v1 = b; v2 = c; v3 = d; v4 = e; v5 = f; v6 = g; }
        public T this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return v0;
                    case 1: return v1;
                    case 2: return v2;
                    case 3: return v3;
                    case 4: return v4;
                    case 5: return v5;
                    default: return v6;
                }
            }
            set
            {
                switch (i)
                {
                    case 0: v0 = value; break;
                    case 1: v1 = value; break;
                    case 2: v2 = value; break;
                    case 3: v3 = value; break;
                    case 4: v4 = value; break;
                    case 5: v5 = value; break;
                    default: v6 = value; break;
                }
            }
        }
        public int size() { return 7; }
    }

    // pstd::complex
    public unsafe struct complex
    {
        public complex(Float re) { this.re = re; im = 0; }
        public complex(Float re, Float im) { this.re = re; this.im = im; }

        public static implicit operator complex(Float re) { return new complex(re); }

        public static complex operator -(complex z) { return new complex(-z.re, -z.im); }
        public static complex operator +(complex a, complex z) { return new complex(a.re + z.re, a.im + z.im); }
        public static complex operator -(complex a, complex z) { return new complex(a.re - z.re, a.im - z.im); }
        public static complex operator *(complex a, complex z)
        {
            return new complex(a.re * z.re - a.im * z.im, a.re * z.im + a.im * z.re);
        }
        public static complex operator /(complex a, complex z)
        {
            Float scale = 1 / (z.re * z.re + z.im * z.im);
            return new complex(scale * (a.re * z.re + a.im * z.im), scale * (a.im * z.re - a.re * z.im));
        }
        public static complex operator +(Float value, complex z) { return new complex(value) + z; }
        public static complex operator -(Float value, complex z) { return new complex(value) - z; }
        public static complex operator *(Float value, complex z) { return new complex(value) * z; }
        public static complex operator /(Float value, complex z) { return new complex(value) / z; }

        public Float re, im;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Float real(complex z) { return z.re; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Float imag(complex z) { return z.im; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Float norm(complex z) { return z.re * z.re + z.im * z.im; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Float abs(complex z) { return sqrt(norm(z)); }

    public static complex sqrt(complex z)
    {
        Float n = abs(z), t1 = sqrt((Float)0.5 * (n + abs(z.re))), t2 = (Float)0.5 * z.im / t1;

        if (n == 0)
            return 0;

        if (z.re >= 0)
            return new complex(t1, t2);
        else
            return new complex(abs(t2), copysign(t1, z.im));
    }
}
