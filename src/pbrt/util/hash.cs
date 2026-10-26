// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/hash.h
//
// Hash(Args...) is a variadic template in C++; it is expanded here into fixed-arity
// generic overloads (1..16 arguments) constrained to unmanaged types.

using System;
using System.Runtime.CompilerServices;

namespace pbrt
{

    public static unsafe partial class Globals
    {
        // Hashing Inline Functions
        // http://zimbry.blogspot.com/2011/09/better-bit-mixing-improving-on.html
        public static ulong MurmurHash64A(byte* key, size_t len, ulong seed)
        {
            const ulong m = 0xc6a4a7935bd1e995UL;
            const int r = 47;

            ulong h = seed ^ (len * m);

            byte* end = key + 8 * (len / 8);
            while (key != end)
            {
                ulong k;
                k = Unsafe.ReadUnaligned<ulong>(key);
                key += 8;

                k *= m;
                k ^= k >> r;
                k *= m;

                h ^= k;
                h *= m;
            }

            // C++ switch with fallthrough: case 7 ... case 1
            int rem = (int)(len & 7);
            if (rem > 0)
            {
                for (int i = rem; i >= 1; --i)
                    h ^= (ulong)key[i - 1] << (8 * (i - 1));
                h *= m;
            }

            h ^= h >> r;
            h *= m;
            h ^= h >> r;
            return h;
        }

        // https://gist.github.com/badboy/6267743
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong MixBits(ulong v)
        {
            v ^= (v >> 31);
            v *= 0x7fb5d329728ea185UL;
            v ^= (v >> 27);
            v *= 0x81dadef4bc2dd44dUL;
            v ^= (v >> 33);
            return v;
        }

        public static ulong HashBuffer<T>(T* ptr, size_t nElements, ulong seed = 0) where T : unmanaged
        {
            return MurmurHash64A((byte*)ptr, nElements * (size_t)sizeof(T), seed);
        }

        public static ulong Hash<T0>(T0 a0) where T0 : unmanaged
        {
            int sz = sizeof(T0);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0>(T0 a0) where T0 : unmanaged
        {
            return (uint)Hash(a0) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1>(T0 a0, T1 a1) where T0 : unmanaged where T1 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1>(T0 a0, T1 a1) where T0 : unmanaged where T1 : unmanaged
        {
            return (uint)Hash(a0, a1) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2>(T0 a0, T1 a1, T2 a2) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2>(T0 a0, T1 a1, T2 a2) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged
        {
            return (uint)Hash(a0, a1, a2) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3>(T0 a0, T1 a1, T2 a2, T3 a3) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3>(T0 a0, T1 a1, T2 a2, T3 a3) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5, T6>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5) + sizeof(T6);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            Unsafe.WriteUnaligned(p, a6); p += sizeof(T6);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5, T6>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5, a6) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5, T6, T7>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5) + sizeof(T6) + sizeof(T7);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            Unsafe.WriteUnaligned(p, a6); p += sizeof(T6);
            Unsafe.WriteUnaligned(p, a7); p += sizeof(T7);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5, T6, T7>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5, a6, a7) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5, T6, T7, T8>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5) + sizeof(T6) + sizeof(T7) + sizeof(T8);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            Unsafe.WriteUnaligned(p, a6); p += sizeof(T6);
            Unsafe.WriteUnaligned(p, a7); p += sizeof(T7);
            Unsafe.WriteUnaligned(p, a8); p += sizeof(T8);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5, T6, T7, T8>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5, a6, a7, a8) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5) + sizeof(T6) + sizeof(T7) + sizeof(T8) + sizeof(T9);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            Unsafe.WriteUnaligned(p, a6); p += sizeof(T6);
            Unsafe.WriteUnaligned(p, a7); p += sizeof(T7);
            Unsafe.WriteUnaligned(p, a8); p += sizeof(T8);
            Unsafe.WriteUnaligned(p, a9); p += sizeof(T9);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5, a6, a7, a8, a9) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5) + sizeof(T6) + sizeof(T7) + sizeof(T8) + sizeof(T9) + sizeof(T10);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            Unsafe.WriteUnaligned(p, a6); p += sizeof(T6);
            Unsafe.WriteUnaligned(p, a7); p += sizeof(T7);
            Unsafe.WriteUnaligned(p, a8); p += sizeof(T8);
            Unsafe.WriteUnaligned(p, a9); p += sizeof(T9);
            Unsafe.WriteUnaligned(p, a10); p += sizeof(T10);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5) + sizeof(T6) + sizeof(T7) + sizeof(T8) + sizeof(T9) + sizeof(T10) + sizeof(T11);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            Unsafe.WriteUnaligned(p, a6); p += sizeof(T6);
            Unsafe.WriteUnaligned(p, a7); p += sizeof(T7);
            Unsafe.WriteUnaligned(p, a8); p += sizeof(T8);
            Unsafe.WriteUnaligned(p, a9); p += sizeof(T9);
            Unsafe.WriteUnaligned(p, a10); p += sizeof(T10);
            Unsafe.WriteUnaligned(p, a11); p += sizeof(T11);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged where T12 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5) + sizeof(T6) + sizeof(T7) + sizeof(T8) + sizeof(T9) + sizeof(T10) + sizeof(T11) + sizeof(T12);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            Unsafe.WriteUnaligned(p, a6); p += sizeof(T6);
            Unsafe.WriteUnaligned(p, a7); p += sizeof(T7);
            Unsafe.WriteUnaligned(p, a8); p += sizeof(T8);
            Unsafe.WriteUnaligned(p, a9); p += sizeof(T9);
            Unsafe.WriteUnaligned(p, a10); p += sizeof(T10);
            Unsafe.WriteUnaligned(p, a11); p += sizeof(T11);
            Unsafe.WriteUnaligned(p, a12); p += sizeof(T12);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged where T12 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged where T12 : unmanaged where T13 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5) + sizeof(T6) + sizeof(T7) + sizeof(T8) + sizeof(T9) + sizeof(T10) + sizeof(T11) + sizeof(T12) + sizeof(T13);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            Unsafe.WriteUnaligned(p, a6); p += sizeof(T6);
            Unsafe.WriteUnaligned(p, a7); p += sizeof(T7);
            Unsafe.WriteUnaligned(p, a8); p += sizeof(T8);
            Unsafe.WriteUnaligned(p, a9); p += sizeof(T9);
            Unsafe.WriteUnaligned(p, a10); p += sizeof(T10);
            Unsafe.WriteUnaligned(p, a11); p += sizeof(T11);
            Unsafe.WriteUnaligned(p, a12); p += sizeof(T12);
            Unsafe.WriteUnaligned(p, a13); p += sizeof(T13);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged where T12 : unmanaged where T13 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13, T14 a14) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged where T12 : unmanaged where T13 : unmanaged where T14 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5) + sizeof(T6) + sizeof(T7) + sizeof(T8) + sizeof(T9) + sizeof(T10) + sizeof(T11) + sizeof(T12) + sizeof(T13) + sizeof(T14);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            Unsafe.WriteUnaligned(p, a6); p += sizeof(T6);
            Unsafe.WriteUnaligned(p, a7); p += sizeof(T7);
            Unsafe.WriteUnaligned(p, a8); p += sizeof(T8);
            Unsafe.WriteUnaligned(p, a9); p += sizeof(T9);
            Unsafe.WriteUnaligned(p, a10); p += sizeof(T10);
            Unsafe.WriteUnaligned(p, a11); p += sizeof(T11);
            Unsafe.WriteUnaligned(p, a12); p += sizeof(T12);
            Unsafe.WriteUnaligned(p, a13); p += sizeof(T13);
            Unsafe.WriteUnaligned(p, a14); p += sizeof(T14);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13, T14 a14) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged where T12 : unmanaged where T13 : unmanaged where T14 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14) * 2.3283064365386963e-10f;
        }

        public static ulong Hash<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13, T14 a14, T15 a15) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged where T12 : unmanaged where T13 : unmanaged where T14 : unmanaged where T15 : unmanaged
        {
            int sz = sizeof(T0) + sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5) + sizeof(T6) + sizeof(T7) + sizeof(T8) + sizeof(T9) + sizeof(T10) + sizeof(T11) + sizeof(T12) + sizeof(T13) + sizeof(T14) + sizeof(T15);
            int n = (sz + 7) / 8;
            ulong* buf = stackalloc ulong[n];
            byte* p = (byte*)buf;
            Unsafe.WriteUnaligned(p, a0); p += sizeof(T0);
            Unsafe.WriteUnaligned(p, a1); p += sizeof(T1);
            Unsafe.WriteUnaligned(p, a2); p += sizeof(T2);
            Unsafe.WriteUnaligned(p, a3); p += sizeof(T3);
            Unsafe.WriteUnaligned(p, a4); p += sizeof(T4);
            Unsafe.WriteUnaligned(p, a5); p += sizeof(T5);
            Unsafe.WriteUnaligned(p, a6); p += sizeof(T6);
            Unsafe.WriteUnaligned(p, a7); p += sizeof(T7);
            Unsafe.WriteUnaligned(p, a8); p += sizeof(T8);
            Unsafe.WriteUnaligned(p, a9); p += sizeof(T9);
            Unsafe.WriteUnaligned(p, a10); p += sizeof(T10);
            Unsafe.WriteUnaligned(p, a11); p += sizeof(T11);
            Unsafe.WriteUnaligned(p, a12); p += sizeof(T12);
            Unsafe.WriteUnaligned(p, a13); p += sizeof(T13);
            Unsafe.WriteUnaligned(p, a14); p += sizeof(T14);
            Unsafe.WriteUnaligned(p, a15); p += sizeof(T15);
            return MurmurHash64A((byte*)buf, (size_t)sz, 0);
        }

        public static Float HashFloat<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(T0 a0, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, T9 a9, T10 a10, T11 a11, T12 a12, T13 a13, T14 a14, T15 a15) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged where T12 : unmanaged where T13 : unmanaged where T14 : unmanaged where T15 : unmanaged
        {
            return (uint)Hash(a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15) * 2.3283064365386963e-10f;
        }
    }

}  // namespace pbrt
