// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/rng.h, util/rng.cpp
//
// RNG::Uniform<T>() template specializations are selected with typeof(T) tests, which the
// JIT folds away for value types.

using System;
using System.Runtime.CompilerServices;

namespace pbrt
{

    public static partial class pbrt
    {
        // Random Number Declarations
        public const ulong PCG32_DEFAULT_STATE = 0x853c49e6748fea9bUL;
        public const ulong PCG32_DEFAULT_STREAM = 0xda3e39cb94b95bdbUL;
        public const ulong PCG32_MULT = 0x5851f42d4c957f2dUL;
    }

    // RNG Definition
    public struct RNG
    {
        // RNG Public Methods
        public RNG()
        {
            state = PCG32_DEFAULT_STATE;
            inc = PCG32_DEFAULT_STREAM;
        }
        public RNG(ulong seqIndex, ulong offset)
        {
            state = 0;
            inc = 0;
            SetSequence(seqIndex, offset);
        }
        public RNG(ulong seqIndex)
        {
            state = 0;
            inc = 0;
            SetSequence(seqIndex);
        }

        public void SetSequence(ulong sequenceIndex, ulong seed)
        {
            state = 0u;
            inc = (sequenceIndex << 1) | 1u;
            Uniform<uint>();
            state += seed;
            Uniform<uint>();
        }
        public void SetSequence(ulong sequenceIndex)
        {
            SetSequence(sequenceIndex, MixBits(sequenceIndex));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Uniform<T>() where T : unmanaged
        {
            if (typeof(T) == typeof(uint))
            {
                ulong oldstate = state;
                state = oldstate * PCG32_MULT + inc;
                uint xorshifted = (uint)(((oldstate >> 18) ^ oldstate) >> 27);
                uint rot = (uint)(oldstate >> 59);
                uint v = (xorshifted >> (int)rot) | (xorshifted << (int)((~rot + 1u) & 31));
                return Unsafe.As<uint, T>(ref v);
            }
            else if (typeof(T) == typeof(ulong))
            {
                ulong v0 = Uniform<uint>(), v1 = Uniform<uint>();
                ulong v = (v0 << 32) | v1;
                return Unsafe.As<ulong, T>(ref v);
            }
            else if (typeof(T) == typeof(int))
            {
                uint v = Uniform<uint>();
                int r;
                if (v <= (uint)std.numeric_limits_int.max)
                    r = (int)v;
                else
                {
                    DCHECK_GE(v, 0x80000000u);
                    r = (int)(v - 0x80000000u) + std.numeric_limits_int.min;
                }
                return Unsafe.As<int, T>(ref r);
            }
            else if (typeof(T) == typeof(long))
            {
                ulong v = Uniform<ulong>();
                long r;
                if (v <= (ulong)std.numeric_limits_long.max)
                    r = (long)v;
                else
                {
                    DCHECK_GE(v, 0x8000000000000000UL);
                    r = (long)(v - 0x8000000000000000UL) + std.numeric_limits_long.min;
                }
                return Unsafe.As<long, T>(ref r);
            }
            else if (typeof(T) == typeof(float))
            {
                float v = std.min<float>(FloatOneMinusEpsilon, Uniform<uint>() * 2.3283064365386963e-10f);
                return Unsafe.As<float, T>(ref v);
            }
            else if (typeof(T) == typeof(double))
            {
                double v = std.min<double>(DoubleOneMinusEpsilon, Uniform<ulong>() * 5.42101086242752217e-20);
                return Unsafe.As<double, T>(ref v);
            }
            throw new NotSupportedException("RNG.Uniform<T>: unimplemented type");
        }

        // template <typename T> enable_if_t<is_integral_v<T>, T> Uniform(T b)
        public T Uniform<T>(T b) where T : unmanaged
        {
            if (typeof(T) == typeof(uint))
            {
                uint bb = Unsafe.As<T, uint>(ref b);
                uint threshold = (~bb + 1u) % bb;
                while (true)
                {
                    uint r = Uniform<uint>();
                    if (r >= threshold)
                    {
                        uint res = r % bb;
                        return Unsafe.As<uint, T>(ref res);
                    }
                }
            }
            else if (typeof(T) == typeof(ulong))
            {
                ulong bb = Unsafe.As<T, ulong>(ref b);
                ulong threshold = (~bb + 1u) % bb;
                while (true)
                {
                    ulong r = Uniform<ulong>();
                    if (r >= threshold)
                    {
                        ulong res = r % bb;
                        return Unsafe.As<ulong, T>(ref res);
                    }
                }
            }
            else if (typeof(T) == typeof(int))
            {
                int bb = Unsafe.As<T, int>(ref b);
                int threshold = (int)((~(uint)bb + 1u) % (uint)bb);  // C++: (~b + 1u) % b with b converted to unsigned
                while (true)
                {
                    int r = Uniform<int>();
                    if (r >= threshold)
                    {
                        int res = r % bb;
                        return Unsafe.As<int, T>(ref res);
                    }
                }
            }
            else if (typeof(T) == typeof(long))
            {
                long bb = Unsafe.As<T, long>(ref b);
                long threshold = (long)((~(ulong)bb + 1u) % (ulong)bb);
                while (true)
                {
                    long r = Uniform<long>();
                    if (r >= threshold)
                    {
                        long res = r % bb;
                        return Unsafe.As<long, T>(ref res);
                    }
                }
            }
            throw new NotSupportedException("RNG.Uniform<T>(T): integral types only");
        }

        public void Advance(long idelta)
        {
            ulong curMult = PCG32_MULT, curPlus = inc, accMult = 1u;
            ulong accPlus = 0u, delta = (ulong)idelta;
            while (delta > 0)
            {
                if ((delta & 1) != 0)
                {
                    accMult *= curMult;
                    accPlus = accPlus * curMult + curPlus;
                }
                curPlus = (curMult + 1) * curPlus;
                curMult *= curMult;
                delta /= 2;
            }
            state = accMult * state + accPlus;
        }

        public static long operator -(RNG a, RNG other)
        {
            CHECK_EQ(a.inc, other.inc);
            ulong curMult = PCG32_MULT, curPlus = a.inc, curState = other.state;
            ulong theBit = 1u, distance = 0u;
            while (a.state != curState)
            {
                // Adjust _curState_ and _distance_ for next bit
                if ((a.state & theBit) != (curState & theBit))
                {
                    curState = curState * curMult + curPlus;
                    distance |= theBit;
                }
                CHECK_EQ(a.state & theBit, curState & theBit);
                theBit <<= 1;
                curPlus = (curMult + 1UL) * curPlus;
                curMult *= curMult;
            }
            return (long)distance;
        }

        public override string ToString()
        {
            return $"[ RNG state: {state} inc: {inc} ]";
        }

        // RNG Private Members
        private ulong state, inc;
    }

}  // namespace pbrt
