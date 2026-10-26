// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/float.h, util/float.cpp

using System;
using System.Runtime.CompilerServices;

namespace pbrt
{
    public static unsafe partial class Globals
    {
        public const Float Infinity = Float.PositiveInfinity;

#if PBRT_FLOAT_AS_DOUBLE
        public const Float MachineEpsilon = 2.220446049250313e-16 * 0.5;
#else
        public const Float MachineEpsilon = 1.1920929e-07f * 0.5f;
#endif

        public const double DoubleOneMinusEpsilon = 0.99999999999999989;           // 0x1.fffffffffffffp-1
        public const float FloatOneMinusEpsilon = 0.99999994f;                    // 0x1.fffffep-1
#if PBRT_FLOAT_AS_DOUBLE
        public const double OneMinusEpsilon = DoubleOneMinusEpsilon;
#else
        public const float OneMinusEpsilon = FloatOneMinusEpsilon;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsNaN(float v) { return float.IsNaN(v); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsNaN(double v) { return double.IsNaN(v); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsNaN(int v) { return false; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsNaN(uint v) { return false; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsNaN(long v) { return false; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsNaN(ulong v) { return false; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsInf(float v) { return float.IsInfinity(v); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsInf(double v) { return double.IsInfinity(v); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsInf(int v) { return false; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsInf(uint v) { return false; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsInf(long v) { return false; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsInf(ulong v) { return false; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsFinite(float v) { return float.IsFinite(v); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsFinite(double v) { return double.IsFinite(v); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsFinite(int v) { return true; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsFinite(uint v) { return true; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsFinite(long v) { return true; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsFinite(ulong v) { return true; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FMA(float a, float b, float c) { return std.fma(a, b, c); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double FMA(double a, double b, double c) { return std.fma(a, b, c); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint32_t FloatToBits(float f) { return BitConverter.SingleToUInt32Bits(f); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BitsToFloat(uint32_t ui) { return BitConverter.UInt32BitsToSingle(ui); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Exponent(float v) { return (int)(FloatToBits(v) >> 23) - 127; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Significand(float v) { return (int)(FloatToBits(v) & ((1 << 23) - 1)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint32_t SignBit(float v) { return FloatToBits(v) & 0x80000000; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint64_t FloatToBits(double f) { return BitConverter.DoubleToUInt64Bits(f); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double BitsToFloat(uint64_t ui) { return BitConverter.UInt64BitsToDouble(ui); }

        public static float NextFloatUp(float v)
        {
            // Handle infinity and negative zero for _NextFloatUp()_
            if (IsInf(v) && v > 0.0f)
                return v;
            if (v == -0.0f)
                v = 0.0f;

            // Advance _v_ to next higher float
            uint32_t ui = FloatToBits(v);
            if (v >= 0)
                ++ui;
            else
                --ui;
            return BitsToFloat(ui);
        }

        public static float NextFloatDown(float v)
        {
            // Handle infinity and positive zero for _NextFloatDown()_
            if (IsInf(v) && v < 0.0)
                return v;
            if (v == 0.0f)
                v = -0.0f;
            uint32_t ui = FloatToBits(v);
            if (v > 0)
                --ui;
            else
                ++ui;
            return BitsToFloat(ui);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float gamma(int n)
        {
            return (n * MachineEpsilon) / (1 - n * MachineEpsilon);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AddRoundUp(Float a, Float b) { return NextFloatUp(a + b); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float AddRoundDown(Float a, Float b) { return NextFloatDown(a + b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SubRoundUp(Float a, Float b) { return AddRoundUp(a, -b); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SubRoundDown(Float a, Float b) { return AddRoundDown(a, -b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MulRoundUp(Float a, Float b) { return NextFloatUp(a * b); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float MulRoundDown(Float a, Float b) { return NextFloatDown(a * b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float DivRoundUp(Float a, Float b) { return NextFloatUp(a / b); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float DivRoundDown(Float a, Float b) { return NextFloatDown(a / b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SqrtRoundUp(Float a) { return NextFloatUp(std.sqrt(a)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SqrtRoundDown(Float a) { return std.max<Float>(0, NextFloatDown(std.sqrt(a))); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float FMARoundUp(Float a, Float b, Float c) { return NextFloatUp(FMA(a, b, c)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float FMARoundDown(Float a, Float b, Float c) { return NextFloatDown(FMA(a, b, c)); }

        public static double NextFloatUp(double v)
        {
            if (IsInf(v) && v > 0.0)
                return v;
            if (v == -0.0f)
                v = 0.0f;
            uint64_t ui = FloatToBits(v);
            if (v >= 0.0)
                ++ui;
            else
                --ui;
            return BitsToFloat(ui);
        }

        public static double NextFloatDown(double v)
        {
            if (IsInf(v) && v < 0.0)
                return v;
            if (v == 0.0f)
                v = -0.0f;
            uint64_t ui = FloatToBits(v);
            if (v > 0.0)
                --ui;
            else
                ++ui;
            return BitsToFloat(ui);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Exponent(double d) { return (int)(FloatToBits(d) >> 52) - 1023; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint64_t Significand(double d) { return FloatToBits(d) & ((1ul << 52) - 1); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint64_t SignBit(double v) { return FloatToBits(v) & 0x8000000000000000; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double FlipSign(double a, double b) { return BitsToFloat(FloatToBits(a) ^ SignBit(b)); }

        // Half-precision constants
        public const int HalfExponentMask = 0b0111110000000000;
        public const int HalfSignificandMask = 0b1111111111;
        public const int HalfNegativeZero = 0b1000000000000000;
        public const int HalfPositiveZero = 0;
        // Exponent all 1s, significand zero
        public const int HalfNegativeInfinity = 0b1111110000000000;
        public const int HalfPositiveInfinity = 0b0111110000000000;
    }

    // Half Definition
    public unsafe struct Half : IEquatable<Half>
    {
        // Half Public Methods
        public static Half FromBits(uint16_t v) { Half r = new Half(); r.h = v; return r; }

        public Half(float ff)
        {
            // Rounding-mode-safe float -> half conversion (Fabian Giesen / ISPC)
            uint f_u = BitConverter.SingleToUInt32Bits(ff);
            uint f32infty_u = 255u << 23;
            uint f16max_u = (127u + 16u) << 23;
            uint denorm_magic_u = (((127u - 15u) + (23u - 10u) + 1u) << 23);
            uint sign_mask = 0x80000000u;
            uint o_u = 0;

            uint sign = f_u & sign_mask;
            f_u ^= sign;

            // NOTE all the integer compares in this function can be safely
            // compiled into signed compares since all operands are below
            // 0x80000000. Important if you want fast straight SSE2 code (since
            // there's no unsigned PCMPGTD).

            if (f_u >= f16max_u)  // result is Inf or NaN (all exponent bits set)
                o_u = (f_u > f32infty_u) ? 0x7e00u : 0x7c00u;  // NaN->qNaN and Inf->Inf
            else
            {                                                   // (De)normalized number or zero
                if (f_u < (113u << 23))
                {  // resulting FP16 is subnormal or zero
                    // use a magic value to align our 10 mantissa bits at the bottom of
                    // the float. as long as FP addition is round-to-nearest-even this
                    // just works.
                    float fv = BitConverter.UInt32BitsToSingle(f_u) + BitConverter.UInt32BitsToSingle(denorm_magic_u);

                    // and one integer subtract of the bias later, we have our final float!
                    o_u = BitConverter.SingleToUInt32Bits(fv) - denorm_magic_u;
                }
                else
                {
                    uint mant_odd = (f_u >> 13) & 1;  // resulting mantissa is odd

                    // update exponent, rounding bias part 1
                    f_u += (unchecked((uint)(15 - 127)) << 23) + 0xfff;
                    // rounding bias part 2
                    f_u += mant_odd;
                    // take the bits!
                    o_u = f_u >> 13;
                }
            }

            o_u |= sign >> 16;
            h = (ushort)o_u;
        }

        public Half(double d) : this((float)d) { }

        public static explicit operator float(Half hh)
        {
            uint hu = hh.h;
            uint magic_u = 113u << 23;
            uint shifted_exp = 0x7c00u << 13;  // exponent mask after shift

            uint o_u = (hu & 0x7fffu) << 13;       // exponent/mantissa bits
            uint exp = shifted_exp & o_u;          // just the exponent
            o_u += (127u - 15u) << 23;             // exponent adjust

            // handle exponent special cases
            if (exp == shifted_exp)                // Inf/NaN?
                o_u += (128u - 16u) << 23;         // extra exp adjust
            else if (exp == 0)
            {                                      // Zero/Denormal?
                o_u += 1u << 23;                   // extra exp adjust
                // renormalize
                float of = BitConverter.UInt32BitsToSingle(o_u) - BitConverter.UInt32BitsToSingle(magic_u);
                o_u = BitConverter.SingleToUInt32Bits(of);
            }

            o_u |= (hu & 0x8000u) << 16;           // sign bit
            return BitConverter.UInt32BitsToSingle(o_u);
        }

        public static explicit operator double(Half hh) { return (float)hh; }

        public static bool operator ==(Half a, Half v)
        {
            // Handle zero (+0 == -0)
            if (a.Bits() == v.Bits())
                return true;
            return ((a.Bits() == HalfNegativeZero && v.Bits() == HalfPositiveZero) ||
                    (a.Bits() == HalfPositiveZero && v.Bits() == HalfNegativeZero));
        }
        public static bool operator !=(Half a, Half v) { return !(a == v); }
        public bool Equals(Half o) { return this == o; }
        public override bool Equals(object o) { return o is Half hh && this == hh; }
        public override int GetHashCode() { return h; }

        public static Half operator -(Half a) { return FromBits((uint16_t)(a.h ^ (1 << 15))); }

        public uint16_t Bits() { return h; }

        public int Sign() { return (h >> 15) != 0 ? -1 : 1; }

        public bool IsInf() { return h == HalfPositiveInfinity || h == HalfNegativeInfinity; }

        public bool IsNaN()
        {
            return ((h & HalfExponentMask) == HalfExponentMask &&
                    (h & HalfSignificandMask) != 0);
        }

        public Half NextUp()
        {
            if (IsInf() && Sign() == 1)
                return this;

            Half up = this;
            if (up.h == HalfNegativeZero)
                up.h = (ushort)HalfPositiveZero;
            // Advance _v_ to next higher float
            if (up.Sign() >= 0)
                ++up.h;
            else
                --up.h;
            return up;
        }

        public Half NextDown()
        {
            if (IsInf() && Sign() == -1)
                return this;

            Half down = this;
            if (down.h == HalfPositiveZero)
                down.h = (ushort)HalfNegativeZero;
            if (down.Sign() >= 0)
                --down.h;
            else
                ++down.h;
            return down;
        }

        public override string ToString() { return ((float)this).ToString("F6"); }

        // Half Private Members
        private uint16_t h;

        private static int HalfNegativeZero => Globals.HalfNegativeZero;
        private static int HalfPositiveZero => Globals.HalfPositiveZero;
        private static int HalfPositiveInfinity => Globals.HalfPositiveInfinity;
        private static int HalfNegativeInfinity => Globals.HalfNegativeInfinity;
        private static int HalfExponentMask => Globals.HalfExponentMask;
        private static int HalfSignificandMask => Globals.HalfSignificandMask;
    }
}
