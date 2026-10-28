// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/math.h, util/math.cpp

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        public const Float ShadowEpsilon = 0.0001f;

        public const Float Pi = (Float)3.14159265358979323846;
        public const Float InvPi = (Float)0.31830988618379067154;
        public const Float Inv2Pi = (Float)0.15915494309189533577;
        public const Float Inv4Pi = (Float)0.07957747154594766788;
        public const Float PiOver2 = (Float)1.57079632679489661923;
        public const Float PiOver4 = (Float)0.78539816339744830961;
        public const Float Sqrt2 = (Float)1.41421356237309504880;

        // Math Function Declarations
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint32_t ReverseBits32(uint32_t n)
        {
            n = (n << 16) | (n >> 16);
            n = ((n & 0x00ff00ff) << 8) | ((n & 0xff00ff00) >> 8);
            n = ((n & 0x0f0f0f0f) << 4) | ((n & 0xf0f0f0f0) >> 4);
            n = ((n & 0x33333333) << 2) | ((n & 0xcccccccc) >> 2);
            n = ((n & 0x55555555) << 1) | ((n & 0xaaaaaaaa) >> 1);
            return n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint64_t ReverseBits64(uint64_t n)
        {
            uint64_t n0 = ReverseBits32((uint32_t)n);
            uint64_t n1 = ReverseBits32((uint32_t)(n >> 32));
            return (n0 << 32) | n1;
        }

        // Bit Operation Function Definitions
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint64_t LeftShift2(uint64_t x)
        {
            x &= 0xffffffff;
            x = (x ^ (x << 16)) & 0x0000ffff0000ffff;
            x = (x ^ (x << 8)) & 0x00ff00ff00ff00ff;
            x = (x ^ (x << 4)) & 0x0f0f0f0f0f0f0f0f;
            x = (x ^ (x << 2)) & 0x3333333333333333;
            x = (x ^ (x << 1)) & 0x5555555555555555;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint64_t EncodeMorton2(uint32_t x, uint32_t y)
        {
            return (LeftShift2(y) << 1) | LeftShift2(x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint32_t LeftShift3(uint32_t x)
        {
            DCHECK_LE(x, (1u << 10));
            if (x == (1 << 10))
                --x;
            x = (x | (x << 16)) & 0b00000011000000000000000011111111;
            // x = ---- --98 ---- ---- ---- ---- 7654 3210
            x = (x | (x << 8)) & 0b00000011000000001111000000001111;
            // x = ---- --98 ---- ---- 7654 ---- ---- 3210
            x = (x | (x << 4)) & 0b00000011000011000011000011000011;
            // x = ---- --98 ---- 76-- --54 ---- 32-- --10
            x = (x | (x << 2)) & 0b00001001001001001001001001001001;
            // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint32_t EncodeMorton3(float x, float y, float z)
        {
            DCHECK_GE(x, 0);
            DCHECK_GE(y, 0);
            DCHECK_GE(z, 0);
            return (LeftShift3((uint32_t)z) << 2) | (LeftShift3((uint32_t)y) << 1) | LeftShift3((uint32_t)x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint32_t Compact1By1(uint64_t x)
        {
            // TODO: as of Haswell, the PEXT instruction could do all this in a
            // single instruction.
            // x = -f-e -d-c -b-a -9-8 -7-6 -5-4 -3-2 -1-0
            x &= 0x5555555555555555;
            // x = --fe --dc --ba --98 --76 --54 --32 --10
            x = (x ^ (x >> 1)) & 0x3333333333333333;
            // x = ---- fedc ---- ba98 ---- 7654 ---- 3210
            x = (x ^ (x >> 2)) & 0x0f0f0f0f0f0f0f0f;
            // x = ---- ---- fedc ba98 ---- ---- 7654 3210
            x = (x ^ (x >> 4)) & 0x00ff00ff00ff00ff;
            // x = ---- ---- ---- ---- fedc ba98 7654 3210
            x = (x ^ (x >> 8)) & 0x0000ffff0000ffff;
            x = (x ^ (x >> 16)) & 0xffffffff;
            return (uint32_t)x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecodeMorton2(uint64_t v, uint32_t* x, uint32_t* y)
        {
            *x = Compact1By1(v);
            *y = Compact1By1(v >> 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint32_t Compact1By2(uint32_t x)
        {
            x &= 0x09249249;                   // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
            x = (x ^ (x >> 2)) & 0x030c30c3;   // x = ---- --98 ---- 76-- --54 ---- 32-- --10
            x = (x ^ (x >> 4)) & 0x0300f00f;   // x = ---- --98 ---- ---- 7654 ---- ---- 3210
            x = (x ^ (x >> 8)) & 0xff0000ff;   // x = ---- --98 ---- ---- ---- ---- 7654 3210
            x = (x ^ (x >> 16)) & 0x000003ff;  // x = ---- ---- ---- ---- ---- --98 7654 3210
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Lerp(Float x, Float a, Float b) { return (1 - x) * a + x * b; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FMA(int a, int b, int c) { return a * b + c; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long FMA(long a, long b, long c) { return a * b + c; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint FMA(uint a, uint b, uint c) { return a * b + c; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong FMA(ulong a, ulong b, ulong c) { return a * b + c; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float WindowedSinc(Float x, Float radius, Float tau)
        {
            if (std.abs(x) > radius)
                return 0;
            return Sinc(x) * Sinc(x / tau);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Sinc(Float x) { return SinXOverX(Pi * x); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float val, float low, float high)
        { if (val < low) return low; else if (val > high) return high; else return val; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double val, double low, double high)
        { if (val < low) return low; else if (val > high) return high; else return val; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int val, int low, int high)
        { if (val < low) return low; else if (val > high) return high; else return val; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Clamp(uint val, uint low, uint high)
        { if (val < low) return low; else if (val > high) return high; else return val; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Clamp(long val, long low, long high)
        { if (val < low) return low; else if (val > high) return high; else return val; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Clamp(ulong val, ulong low, ulong high)
        { if (val < low) return low; else if (val > high) return high; else return val; }
        // mixed-type forms used by pbrt (T val, U low, V high) -> T
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int val, int low, uint high)
        { if (val < low) return low; else if (val > high) return (int)high; else return val; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int val, int low, ulong high)
        { if (val < low) return low; else if (val > (long)high) return (int)high; else return val; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Clamp(long val, long low, ulong high)
        { if (val < low) return low; else if (val > (long)high) return (long)high; else return val; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Clamp(Float val, int low, Float high)
        { if (val < low) return low; else if (val > high) return high; else return val; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Mod(int a, int b) { int result = a - (a / b) * b; return (int)((result < 0) ? result + b : result); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Mod(uint a, uint b) { uint result = a - (a / b) * b; return result; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Mod(long a, long b) { long result = a - (a / b) * b; return (long)((result < 0) ? result + b : result); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Mod(ulong a, ulong b) { ulong result = a - (a / b) * b; return result; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Mod(Float a, Float b) { return std.fmod(a, b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Radians(Float deg) { return (Pi / 180) * deg; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Degrees(Float rad) { return (180 / Pi) * rad; }

        public static Float SmoothStep(Float x, Float a, Float b)
        {
            if (a == b)
                return (x < a) ? 0 : 1;
            DCHECK_LT(a, b);
            Float t = Clamp((x - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SafeSqrt(float x)
        {
            DCHECK_GE(x, -1e-3f);  // not too negative
            return std.sqrt(std.max(0.0f, x));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SafeSqrt(double x)
        {
            DCHECK_GE(x, -1e-3);  // not too negative
            return std.sqrt(std.max(0.0, x));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Sqr(float v) { return v * v; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double Sqr(double v) { return v * v; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Sqr(int v) { return v * v; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static uint Sqr(uint v) { return v * v; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static pstd.complex Sqr(pstd.complex v) { return v * v; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long Sqr(long v) { return v * v; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static ulong Sqr(ulong v) { return v * v; }

        // Pow<n>(v): n is a (positive) compile-time constant in pbrt; here a runtime/inlined argument.
        public static float Pow(int n, float v)
        {
            if (n < 0)
                return 1 / Pow(-n, v);
            if (n == 0) return 1;
            if (n == 1) return v;
            float n2 = Pow(n / 2, v);
            return n2 * n2 * Pow(n & 1, v);
        }

        public static double Pow(int n, double v)
        {
            if (n < 0)
                return 1 / Pow(-n, v);
            if (n == 0) return 1;
            if (n == 1) return v;
            double n2 = Pow(n / 2, v);
            return n2 * n2 * Pow(n & 1, v);
        }

        // EvaluatePolynomial overloads (variadic template in pbrt)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0) { return c0; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1) { return FMA(t, EvaluatePolynomial(t, c1), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2) { return FMA(t, EvaluatePolynomial(t, c1, c2), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9, Float c10) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9, Float c10, Float c11) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9, Float c10, Float c11, Float c12) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9, Float c10, Float c11, Float c12, Float c13) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12, c13), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9, Float c10, Float c11, Float c12, Float c13, Float c14) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12, c13, c14), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9, Float c10, Float c11, Float c12, Float c13, Float c14, Float c15) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12, c13, c14, c15), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9, Float c10, Float c11, Float c12, Float c13, Float c14, Float c15, Float c16) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12, c13, c14, c15, c16), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9, Float c10, Float c11, Float c12, Float c13, Float c14, Float c15, Float c16, Float c17) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12, c13, c14, c15, c16, c17), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9, Float c10, Float c11, Float c12, Float c13, Float c14, Float c15, Float c16, Float c17, Float c18) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12, c13, c14, c15, c16, c17, c18), c0); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float EvaluatePolynomial(Float t, Float c0, Float c1, Float c2, Float c3, Float c4, Float c5, Float c6, Float c7, Float c8, Float c9, Float c10, Float c11, Float c12, Float c13, Float c14, Float c15, Float c16, Float c17, Float c18, Float c19) { return FMA(t, EvaluatePolynomial(t, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12, c13, c14, c15, c16, c17, c18, c19), c0); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float SinXOverX(Float x)
        {
            if (1 - x * x == 1)
                return 1;
            return std.sin(x) / x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SafeASin(float x)
        {
            DCHECK(x >= -1.0001 && x <= 1.0001);
            return std.asin(Clamp(x, -1, 1));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SafeACos(float x)
        {
            DCHECK(x >= -1.0001 && x <= 1.0001);
            return std.acos(Clamp(x, -1, 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SafeASin(double x)
        {
            DCHECK(x >= -1.0001 && x <= 1.0001);
            return std.asin(Clamp(x, -1, 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SafeACos(double x)
        {
            DCHECK(x >= -1.0001 && x <= 1.0001);
            return std.acos(Clamp(x, -1, 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Log2(Float x)
        {
            const Float invLog2 = (Float)1.442695040888963387004650940071;
            return std.log(x) * invLog2;
        }

        public static int Log2Int(float v)
        {
            DCHECK_GT(v, 0);
            if (v < 1)
                return -Log2Int(1 / v);
            // https://graphics.stanford.edu/~seander/bithacks.html#IntegerLog
            // (With an additional check of the significant to get round-to-nearest
            // rather than round down.)
            // midsignif = Significand(std::pow(2., 1.5))
            // i.e. grab the significand of a value halfway between two exponents,
            // in log space.
            const uint32_t midsignif = 0b00000000001101010000010011110011;
            return Exponent(v) + ((Significand(v) >= midsignif) ? 1 : 0);
        }

        public static int Log2Int(double v)
        {
            DCHECK_GT(v, 0);
            if (v < 1)
                return -Log2Int(1 / v);
            // midsignif = Significand(std::pow(2., 1.5))
            const uint64_t midsignif = 0b110101000001001111001100110011111110011101111001101;
            return Exponent(v) + ((Significand(v) >= midsignif) ? 1 : 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2Int(uint32_t v) { return 31 - BitOperations.LeadingZeroCount(v); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2Int(int32_t v) { return Log2Int((uint32_t)v); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2Int(uint64_t v) { return 63 - BitOperations.LeadingZeroCount(v); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2Int(int64_t v) { return Log2Int((uint64_t)v); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Log4Int(uint32_t v) { return Log2Int(v) / 2; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Log4Int(int32_t v) { return Log2Int(v) / 2; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Log4Int(uint64_t v) { return Log2Int(v) / 2; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Log4Int(int64_t v) { return Log2Int(v) / 2; }

        public static float FastExp(float x)
        {
            // Compute $x'$ such that $\roman{e}^x = 2^{x'}$
            float xp = x * 1.442695041f;

            // Find integer and fractional components of $x'$
            float fxp = pstd.floor(xp), f = xp - fxp;
            int i = (int)fxp;

            // Evaluate polynomial approximation of $2^f$
            float twoToF = EvaluatePolynomial(f, 1.0f, 0.695556856f, 0.226173572f, 0.0781455737f);

            // Scale $2^f$ by $2^i$ and return final result
            int exponent = Exponent(twoToF) + i;
            if (exponent < -126)
                return 0;
            if (exponent > 127)
                return Infinity;
            uint32_t bits = FloatToBits(twoToF);
            bits &= 0b10000000011111111111111111111111u;
            bits |= (uint32_t)((exponent + 127) << 23);
            return BitsToFloat(bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float Gaussian(Float x, Float mu = 0, Float sigma = 1)
        {
            return 1 / std.sqrt(2 * Pi * sigma * sigma) * FastExp(-Sqr(x - mu) / (2 * sigma * sigma));
        }

        public static Float GaussianIntegral(Float x0, Float x1, Float mu = 0, Float sigma = 1)
        {
            DCHECK_GT(sigma, 0);
            Float sigmaRoot2 = sigma * (Float)1.414213562373095;
            return 0.5f * (std.erf((mu - x0) / sigmaRoot2) - std.erf((mu - x1) / sigmaRoot2));
        }

        public static Float Logistic(Float x, Float s)
        {
            x = std.abs(x);
            return std.exp(-x / s) / (s * Sqr(1 + std.exp(-x / s)));
        }

        public static Float LogisticCDF(Float x, Float s) { return 1 / (1 + std.exp(-x / s)); }

        public static Float TrimmedLogistic(Float x, Float s, Float a, Float b)
        {
            DCHECK_LT(a, b);
            return Logistic(x, s) / (LogisticCDF(b, s) - LogisticCDF(a, s));
        }

        // FindInterval: the predicate takes an index in [0, sz)
        public static size_t FindInterval(size_t sz, Func<int, bool> pred)
        {
            long size = (long)sz - 2, first = 1;
            while (size > 0)
            {
                // Evaluate predicate at midpoint and update _first_ and _size_
                size_t half = (size_t)size >> 1, middle = (size_t)first + half;
                bool predResult = pred((int)middle);
                first = predResult ? (long)middle + 1 : first;
                size = predResult ? size - (long)(half + 1) : (long)half;
            }
            return (size_t)Clamp((long)first - 1, 0L, (long)sz - 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOf2(int v) { return v != 0 && (v & (v - 1)) == 0; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOf2(uint v) { return v != 0 && (v & (v - 1)) == 0; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOf2(long v) { return v != 0 && (v & (v - 1)) == 0; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOf2(ulong v) { return v != 0 && (v & (v - 1)) == 0; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOf4(int v) { return v == 1 << (2 * Log4Int(v)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOf4(uint v) { return v == 1u << (2 * Log4Int(v)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOf4(long v) { return v == 1L << (2 * Log4Int(v)); }

        public static int32_t RoundUpPow2(int32_t v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return v + 1;
        }

        public static int64_t RoundUpPow2(int64_t v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v |= v >> 32;
            return v + 1;
        }

        public static int RoundUpPow4(int v) { return IsPowerOf4(v) ? v : (1 << (2 * (1 + Log4Int(v)))); }
        public static long RoundUpPow4(long v) { return IsPowerOf4(v) ? v : (1L << (2 * (1 + Log4Int(v)))); }

        // CompensatedFloat helpers
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompensatedFloat TwoProd(Float a, Float b)
        {
            Float ab = a * b;
            return new CompensatedFloat(ab, FMA(a, b, -ab));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompensatedFloat TwoSum(Float a, Float b)
        {
            Float s = a + b, delta = s - a;
            return new CompensatedFloat(s, (a - (s - delta)) + (b - delta));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DifferenceOfProducts(float a, float b, float c, float d)
        {
            float cd = c * d;
            float differenceOfProducts = FMA(a, b, -cd);
            float error = FMA(-c, d, cd);
            return differenceOfProducts + error;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DifferenceOfProducts(double a, double b, double c, double d)
        {
            double cd = c * d;
            double differenceOfProducts = FMA(a, b, -cd);
            double error = FMA(-c, d, cd);
            return differenceOfProducts + error;
        }

        // (the C++ DifferenceOfProducts template instantiated for vector types: componentwise)
        public static Vector3f DifferenceOfProducts(Float a, Vector3f b, Float c, Vector3f d)
        {
            return new Vector3f(DifferenceOfProducts(a, b.x, c, d.x), DifferenceOfProducts(a, b.y, c, d.y),
                                DifferenceOfProducts(a, b.z, c, d.z));
        }
        public static Normal3f DifferenceOfProducts(Float a, Normal3f b, Float c, Normal3f d)
        {
            return new Normal3f(DifferenceOfProducts(a, b.x, c, d.x), DifferenceOfProducts(a, b.y, c, d.y),
                                DifferenceOfProducts(a, b.z, c, d.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SumOfProducts(float a, float b, float c, float d)
        {
            float cd = c * d;
            float sumOfProducts = FMA(a, b, cd);
            float error = FMA(c, d, -cd);
            return sumOfProducts + error;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SumOfProducts(double a, double b, double c, double d)
        {
            double cd = c * d;
            double sumOfProducts = FMA(a, b, cd);
            double error = FMA(c, d, -cd);
            return sumOfProducts + error;
        }

        // InnerProduct overloads (variadic template in pbrt)
        public static Float InnerProduct(Float a0, Float b0) { return (Float)(CompensatedFloat_InnerProduct(a0, b0)); }
        internal static CompensatedFloat CompensatedFloat_InnerProduct(Float a, Float b) { return TwoProd(a, b); }
        internal static CompensatedFloat CompensatedFloat_InnerProduct(Float a0, Float b0, Float a1, Float b1)
        {
            CompensatedFloat ab = TwoProd(a0, b0);
            CompensatedFloat tp = CompensatedFloat_InnerProduct(a1, b1);
            CompensatedFloat sum = TwoSum(ab.v, tp.v);
            return new CompensatedFloat(sum.v, ab.err + (tp.err + sum.err));
        }
        public static Float InnerProduct(Float a0, Float b0, Float a1, Float b1) { return (Float)CompensatedFloat_InnerProduct(a0, b0, a1, b1); }
        internal static CompensatedFloat CompensatedFloat_InnerProduct(Float a0, Float b0, Float a1, Float b1, Float a2, Float b2)
        {
            CompensatedFloat ab = TwoProd(a0, b0);
            CompensatedFloat tp = CompensatedFloat_InnerProduct(a1, b1, a2, b2);
            CompensatedFloat sum = TwoSum(ab.v, tp.v);
            return new CompensatedFloat(sum.v, ab.err + (tp.err + sum.err));
        }
        public static Float InnerProduct(Float a0, Float b0, Float a1, Float b1, Float a2, Float b2) { return (Float)CompensatedFloat_InnerProduct(a0, b0, a1, b1, a2, b2); }
        internal static CompensatedFloat CompensatedFloat_InnerProduct(Float a0, Float b0, Float a1, Float b1, Float a2, Float b2, Float a3, Float b3)
        {
            CompensatedFloat ab = TwoProd(a0, b0);
            CompensatedFloat tp = CompensatedFloat_InnerProduct(a1, b1, a2, b2, a3, b3);
            CompensatedFloat sum = TwoSum(ab.v, tp.v);
            return new CompensatedFloat(sum.v, ab.err + (tp.err + sum.err));
        }
        public static Float InnerProduct(Float a0, Float b0, Float a1, Float b1, Float a2, Float b2, Float a3, Float b3) { return (Float)CompensatedFloat_InnerProduct(a0, b0, a1, b1, a2, b2, a3, b3); }
        internal static CompensatedFloat CompensatedFloat_InnerProduct(Float a0, Float b0, Float a1, Float b1, Float a2, Float b2, Float a3, Float b3, Float a4, Float b4)
        {
            CompensatedFloat ab = TwoProd(a0, b0);
            CompensatedFloat tp = CompensatedFloat_InnerProduct(a1, b1, a2, b2, a3, b3, a4, b4);
            CompensatedFloat sum = TwoSum(ab.v, tp.v);
            return new CompensatedFloat(sum.v, ab.err + (tp.err + sum.err));
        }
        public static Float InnerProduct(Float a0, Float b0, Float a1, Float b1, Float a2, Float b2, Float a3, Float b3, Float a4, Float b4) { return (Float)CompensatedFloat_InnerProduct(a0, b0, a1, b1, a2, b2, a3, b3, a4, b4); }
        internal static CompensatedFloat CompensatedFloat_InnerProduct(Float a0, Float b0, Float a1, Float b1, Float a2, Float b2, Float a3, Float b3, Float a4, Float b4, Float a5, Float b5)
        {
            CompensatedFloat ab = TwoProd(a0, b0);
            CompensatedFloat tp = CompensatedFloat_InnerProduct(a1, b1, a2, b2, a3, b3, a4, b4, a5, b5);
            CompensatedFloat sum = TwoSum(ab.v, tp.v);
            return new CompensatedFloat(sum.v, ab.err + (tp.err + sum.err));
        }
        public static Float InnerProduct(Float a0, Float b0, Float a1, Float b1, Float a2, Float b2, Float a3, Float b3, Float a4, Float b4, Float a5, Float b5) { return (Float)CompensatedFloat_InnerProduct(a0, b0, a1, b1, a2, b2, a3, b3, a4, b4, a5, b5); }
        internal static CompensatedFloat CompensatedFloat_InnerProduct(Float a0, Float b0, Float a1, Float b1, Float a2, Float b2, Float a3, Float b3, Float a4, Float b4, Float a5, Float b5, Float a6, Float b6)
        {
            CompensatedFloat ab = TwoProd(a0, b0);
            CompensatedFloat tp = CompensatedFloat_InnerProduct(a1, b1, a2, b2, a3, b3, a4, b4, a5, b5, a6, b6);
            CompensatedFloat sum = TwoSum(ab.v, tp.v);
            return new CompensatedFloat(sum.v, ab.err + (tp.err + sum.err));
        }
        public static Float InnerProduct(Float a0, Float b0, Float a1, Float b1, Float a2, Float b2, Float a3, Float b3, Float a4, Float b4, Float a5, Float b5, Float a6, Float b6) { return (Float)CompensatedFloat_InnerProduct(a0, b0, a1, b1, a2, b2, a3, b3, a4, b4, a5, b5, a6, b6); }

        public static bool Quadratic(float a, float b, float c, float* t0, float* t1)
        {
            // Handle case of $a=0$ for quadratic solution
            if (a == 0)
            {
                if (b == 0)
                    return false;
                *t0 = *t1 = -c / b;
                return true;
            }

            // Find quadratic discriminant
            float discrim = DifferenceOfProducts(b, b, 4 * a, c);
            if (discrim < 0)
                return false;
            float rootDiscrim = std.sqrt(discrim);

            // Compute quadratic _t_ values
            float q = -0.5f * (b + pstd.copysign(rootDiscrim, b));
            *t0 = q / a;
            *t1 = c / q;
            if (*t0 > *t1)
                pstd.swap(ref *t0, ref *t1);

            return true;
        }

        public static bool Quadratic(double a, double b, double c, double* t0, double* t1)
        {
            // Find quadratic discriminant
            double discrim = DifferenceOfProducts(b, b, 4 * a, c);
            if (discrim < 0)
                return false;
            double rootDiscrim = std.sqrt(discrim);

            if (a == 0)
            {
                *t0 = *t1 = -c / b;
                return true;
            }

            // Compute quadratic _t_ values
            double q = -0.5 * (b + pstd.copysign(rootDiscrim, b));
            *t0 = q / a;
            *t1 = c / q;
            if (*t0 > *t1)
                pstd.swap(ref *t0, ref *t1);
            return true;
        }

        public static Float NewtonBisection(Float x0, Float x1, Func<Float, (Float first, Float second)> f,
                                            Float xEps = 1e-6f, Float fEps = 1e-6f)
        {
            // Check function endpoints for roots
            DCHECK_LT(x0, x1);
            Float fx0 = f(x0).first, fx1 = f(x1).first;
            if (std.abs(fx0) < fEps)
                return x0;
            if (std.abs(fx1) < fEps)
                return x1;
            bool startIsNegative = fx0 < 0;

            // Set initial midpoint using linear approximation of _f_
            Float xMid = x0 + (x1 - x0) * -fx0 / (fx1 - fx0);

            while (true)
            {
                // Fall back to bisection if _xMid_ is out of bounds
                if (!(x0 < xMid && xMid < x1))
                    xMid = (x0 + x1) / 2;

                // Evaluate function and narrow bracket range _[x0, x1]_
                (Float first, Float second) fxMid = f(xMid);
                DCHECK(!IsNaN(fxMid.first));
                if (startIsNegative == (fxMid.first < 0))
                    x0 = xMid;
                else
                    x1 = xMid;

                // Stop the iteration if bracket is sufficiently small or _f_ is close
                // to zero
                if ((x1 - x0) < xEps || std.abs(fxMid.first) < fEps)
                    return xMid;

                // Perform a Newton step
                xMid -= fxMid.first / fxMid.second;
            }
        }

        // LinearLeastSquares<3>
        public static pstd.optional<SquareMatrix3> LinearLeastSquares3(Float[,] A, Float[,] B, int rows)
        {
            const int N = 3;
            SquareMatrix3 AtA = SquareMatrix3.Zero();
            SquareMatrix3 AtB = SquareMatrix3.Zero();

            for (int i = 0; i < N; ++i)
                for (int j = 0; j < N; ++j)
                    for (int r = 0; r < rows; ++r)
                    {
                        AtA[i][j] += A[r, i] * A[r, j];
                        AtB[i][j] += A[r, i] * B[r, j];
                    }

            pstd.optional<SquareMatrix3> AtAi = Inverse(AtA);
            if (!AtAi.has_value())
                return default;
            return Transpose(AtAi.value() * AtB);
        }

        public static int NextPrime(int x)
        {
            // Return the next prime after _x_, or _x_ itself if it's prime
            if (x == 2)
                return 3;
            if ((x & 1) == 0)
                ++x;  // make it odd

            System.Collections.Generic.List<int> smallPrimes = new System.Collections.Generic.List<int> { 2 };
            Func<int, bool> isPrime = (int n) =>
            {
                foreach (int p in smallPrimes)
                    if (n != p && (n % p) == 0)
                        return false;
                return true;
            };

            // Gap between consecutive primes is at most 320 up to 2^32
            const int maxPrimeGap = 320;
            for (int n = 3; n < (int)(std.sqrt((double)(x + maxPrimeGap))) + 1; n += 2)
                if (isPrime(n))
                    smallPrimes.Add(n);

            while (!isPrime(x))
                x += 2;

            return x;
        }

        public static int PermutationElement(uint32_t i, uint32_t l, uint32_t p)
        {
            uint32_t w = l - 1;
            w |= w >> 1;
            w |= w >> 2;
            w |= w >> 4;
            w |= w >> 8;
            w |= w >> 16;
            do
            {
                i ^= p;
                i *= 0xe170893d;
                i ^= p >> 16;
                i ^= (i & w) >> 4;
                i ^= p >> 8;
                i *= 0x0929eb3f;
                i ^= p >> 23;
                i ^= (i & w) >> 1;
                i *= 1 | p >> 27;
                i *= 0x6935fa69;
                i ^= (i & w) >> 11;
                i *= 0x74dcb303;
                i ^= (i & w) >> 2;
                i *= 0x9e501cc3;
                i ^= (i & w) >> 2;
                i *= 0xc860a3df;
                i &= w;
                i ^= i >> 5;
            } while (i >= l);
            return (int)((i + p) % l);
        }

        public static Float ErfInv(Float a)
        {
            // https://stackoverflow.com/a/49743348
            float p;
            float t = std.log(std.max(FMA(a, -a, 1), 1.17549435e-38f));
            CHECK(!IsNaN(t) && !IsInf(t));
            if (std.abs(t) > 6.125f)
            {                                      // maximum ulp error = 2.35793
                p = 3.03697567e-10f;               //  0x1.4deb44p-32
                p = FMA(p, t, 2.93243101e-8f);     //  0x1.f7c9aep-26
                p = FMA(p, t, 1.22150334e-6f);     //  0x1.47e512p-20
                p = FMA(p, t, 2.84108955e-5f);     //  0x1.dca7dep-16
                p = FMA(p, t, 3.93552968e-4f);     //  0x1.9cab92p-12
                p = FMA(p, t, 3.02698812e-3f);     //  0x1.8cc0dep-9
                p = FMA(p, t, 4.83185798e-3f);     //  0x1.3ca920p-8
                p = FMA(p, t, -2.64646143e-1f);    // -0x1.0eff66p-2
                p = FMA(p, t, 8.40016484e-1f);     //  0x1.ae16a4p-1
            }
            else
            {                                      // maximum ulp error = 2.35456
                p = 5.43877832e-9f;                //  0x1.75c000p-28
                p = FMA(p, t, 1.43286059e-7f);     //  0x1.33b458p-23
                p = FMA(p, t, 1.22775396e-6f);     //  0x1.49929cp-20
                p = FMA(p, t, 1.12962631e-7f);     //  0x1.e52bbap-24
                p = FMA(p, t, -5.61531961e-5f);    // -0x1.d70c12p-15
                p = FMA(p, t, -1.47697705e-4f);    // -0x1.35be9ap-13
                p = FMA(p, t, 2.31468701e-3f);     //  0x1.2f6402p-9
                p = FMA(p, t, 1.15392562e-2f);     //  0x1.7a1e4cp-7
                p = FMA(p, t, -2.32015476e-1f);    // -0x1.db2aeep-3
                p = FMA(p, t, 8.86226892e-1f);     //  0x1.c5bf88p-1
            }
            return a * p;
        }

        public static Float I0(Float x)
        {
            Float val = 0;
            Float x2i = 1;
            int64_t ifact = 1;
            int i4 = 1;
            // I0(x) \approx Sum_i x^(2i) / (4^i (i!)^2)
            for (int i = 0; i < 10; ++i)
            {
                if (i > 1)
                    ifact *= i;
                val += x2i / (i4 * Sqr((Float)ifact));
                x2i *= x * x;
                i4 *= 4;
            }
            return val;
        }

        public static Float LogI0(Float x)
        {
            if (x > 12)
                // Truncated asymptotic expansion; the error is O(1/x^2).
                return x + 0.5f * (-std.log(2 * Pi) + std.log(1 / x) + 1 / (8 * x));
            else
                return std.log(I0(x));
        }

        // Interval Function Declarations
        public static bool InRange(Float v, Interval i) { return v >= i.LowerBound() && v <= i.UpperBound(); }
        public static bool InRange(Interval a, Interval b) { return a.LowerBound() <= b.UpperBound() && a.UpperBound() >= b.LowerBound(); }

        public static Interval Sqr(Interval i)
        {
            Float alow = std.abs(i.LowerBound()), ahigh = std.abs(i.UpperBound());
            if (alow > ahigh)
                pstd.swap(ref alow, ref ahigh);
            if (InRange(0, i))
                return new Interval(0, MulRoundUp(ahigh, ahigh));
            return new Interval(MulRoundDown(alow, alow), MulRoundUp(ahigh, ahigh));
        }

        public static Float Floor(Interval i) { return pstd.floor(i.LowerBound()); }
        public static Float Ceil(Interval i) { return pstd.ceil(i.UpperBound()); }
        public static Float floor(Interval i) { return Floor(i); }
        public static Float ceil(Interval i) { return Ceil(i); }
        public static Float Min(Interval a, Interval b) { return std.min(a.LowerBound(), b.LowerBound()); }
        public static Float Max(Interval a, Interval b) { return std.max(a.UpperBound(), b.UpperBound()); }
        public static Float min(Interval a, Interval b) { return Min(a, b); }
        public static Float max(Interval a, Interval b) { return Max(a, b); }

        public static Interval Sqrt(Interval i) { return new Interval(SqrtRoundDown(i.LowerBound()), SqrtRoundUp(i.UpperBound())); }
        public static Interval sqrt(Interval i) { return Sqrt(i); }

        public static Interval FMA(Interval a, Interval b, Interval c)
        {
            Float low = std.min(
                std.min(FMARoundDown(a.LowerBound(), b.LowerBound(), c.LowerBound()),
                        FMARoundDown(a.UpperBound(), b.LowerBound(), c.LowerBound())),
                std.min(FMARoundDown(a.LowerBound(), b.UpperBound(), c.LowerBound()),
                        FMARoundDown(a.UpperBound(), b.UpperBound(), c.LowerBound())));
            Float high = std.max(
                std.max(FMARoundUp(a.LowerBound(), b.LowerBound(), c.UpperBound()),
                        FMARoundUp(a.UpperBound(), b.LowerBound(), c.UpperBound())),
                std.max(FMARoundUp(a.LowerBound(), b.UpperBound(), c.UpperBound()),
                        FMARoundUp(a.UpperBound(), b.UpperBound(), c.UpperBound())));
            return new Interval(low, high);
        }

        public static Interval DifferenceOfProducts(Interval a, Interval b, Interval c, Interval d)
        {
            Float* ab = stackalloc Float[4] { a.LowerBound() * b.LowerBound(), a.UpperBound() * b.LowerBound(),
                                              a.LowerBound() * b.UpperBound(), a.UpperBound() * b.UpperBound() };
            Float abLow = std.min(std.min(ab[0], ab[1]), std.min(ab[2], ab[3]));
            Float abHigh = std.max(std.max(ab[0], ab[1]), std.max(ab[2], ab[3]));
            int abLowIndex = abLow == ab[0] ? 0 : (abLow == ab[1] ? 1 : (abLow == ab[2] ? 2 : 3));
            int abHighIndex = abHigh == ab[0] ? 0 : (abHigh == ab[1] ? 1 : (abHigh == ab[2] ? 2 : 3));

            Float* cd = stackalloc Float[4] { c.LowerBound() * d.LowerBound(), c.UpperBound() * d.LowerBound(),
                                              c.LowerBound() * d.UpperBound(), c.UpperBound() * d.UpperBound() };
            Float cdLow = std.min(std.min(cd[0], cd[1]), std.min(cd[2], cd[3]));
            Float cdHigh = std.max(std.max(cd[0], cd[1]), std.max(cd[2], cd[3]));
            int cdLowIndex = cdLow == cd[0] ? 0 : (cdLow == cd[1] ? 1 : (cdLow == cd[2] ? 2 : 3));
            int cdHighIndex = cdHigh == cd[0] ? 0 : (cdHigh == cd[1] ? 1 : (cdHigh == cd[2] ? 2 : 3));

            // Invert few terms here; we want this to be computed in the
            // order that corresponds to the difference of products.
            Float low = DifferenceOfProducts(a[abLowIndex & 1], b[abLowIndex >> 1], c[cdHighIndex & 1], d[cdHighIndex >> 1]);
            Float high = DifferenceOfProducts(a[abHighIndex & 1], b[abHighIndex >> 1], c[cdLowIndex & 1], d[cdLowIndex >> 1]);
            DCHECK_LE(low, high);

            return new Interval(NextFloatDown(NextFloatDown(low)), NextFloatUp(NextFloatUp(high)));
        }

        public static Interval SumOfProducts(Interval a, Interval b, Interval c, Interval d)
        {
            return DifferenceOfProducts(a, b, -c, d);
        }

        public static Interval MulPow2(Float s, Interval i) { return MulPow2(i, s); }

        public static Interval MulPow2(Interval i, Float s)
        {
            Float as_ = std.abs(s);
            // Mul. by a power of 2 is exact
            return new Interval(std.min(i.LowerBound() * s, i.UpperBound() * s),
                                std.max(i.LowerBound() * s, i.UpperBound() * s));
        }

        public static Interval Abs(Interval i)
        {
            if (i.LowerBound() >= 0)
                // The entire interval is greater than zero, so we're all set.
                return i;
            else if (i.UpperBound() <= 0)
                // The entire interval is less than zero.
                return new Interval(-i.UpperBound(), -i.LowerBound());
            else
                // The interval straddles zero.
                return new Interval(0, std.max(-i.LowerBound(), i.UpperBound()));
        }

        public static Interval abs(Interval i) { return Abs(i); }

        public static Interval ACos(Interval i)
        {
            Float low = std.acos(std.min<Float>(1, i.UpperBound()));
            Float high = std.acos(std.max<Float>(-1, i.LowerBound()));

            return new Interval(std.max<Float>(0, NextFloatDown(low)), NextFloatUp(high));
        }

        public static Interval Sin(Interval i)
        {
            CHECK_GE(i.LowerBound(), (Float)(-1e-16));
            CHECK_LE(i.UpperBound(), (Float)(2.0001 * Pi));
            Float low = std.sin(std.max<Float>(0, i.LowerBound()));
            Float high = std.sin(i.UpperBound());
            if (low > high)
                pstd.swap(ref low, ref high);
            low = std.max<Float>(-1, NextFloatDown(low));
            high = std.min<Float>(1, NextFloatUp(high));
            if (InRange(Pi / 2, i))
                high = 1;
            if (InRange((3.0f / 2.0f) * Pi, i))
                low = -1;

            return new Interval(low, high);
        }

        public static Interval Cos(Interval i)
        {
            CHECK_GE(i.LowerBound(), (Float)(-1e-16));
            CHECK_LE(i.UpperBound(), (Float)(2.0001 * Pi));
            Float low = std.cos(std.max<Float>(0, i.LowerBound()));
            Float high = std.cos(i.UpperBound());
            if (low > high)
                pstd.swap(ref low, ref high);
            low = std.max<Float>(-1, NextFloatDown(low));
            high = std.min<Float>(1, NextFloatUp(high));
            if (InRange(Pi, i))
                low = -1;

            return new Interval(low, high);
        }

        public static bool Quadratic(Interval a, Interval b, Interval c, Interval* t0, Interval* t1)
        {
            // Find quadratic discriminant
            Interval discrim = DifferenceOfProducts(b, b, MulPow2(4, a), c);
            if (discrim.LowerBound() < 0)
                return false;
            Interval floatRootDiscrim = Sqrt(discrim);

            // Compute quadratic _t_ values
            Interval q;
            if ((Float)b < 0)
                q = MulPow2(-0.5f, b - floatRootDiscrim);
            else
                q = MulPow2(-0.5f, b + floatRootDiscrim);
            *t0 = q / a;
            *t1 = c / q;
            if (t0->LowerBound() > t1->LowerBound())
                pstd.swap(ref *t0, ref *t1);
            return true;
        }

        public static Interval SumSquares(Interval i) { return Sqr(i); }
        public static Interval SumSquares(Interval i, Interval a) { return SumSquares2(i, SumSquares(a)); }
        public static Interval SumSquares(Interval i, Interval a, Interval b) { return SumSquares2(i, SumSquares(a, b)); }
        public static Interval SumSquares(Interval i, Interval a, Interval b, Interval c) { return SumSquares2(i, SumSquares(a, b, c)); }
        private static Interval SumSquares2(Interval i, Interval rest)
        {
            Interval ss = FMA(i, i, rest);
            return new Interval(std.max<Float>(0, ss.LowerBound()), ss.UpperBound());
        }

        public static Interval operator_mul(Float f, Interval i) { return i * f; }

        // Catmull-Rom
        public static bool CatmullRomWeights(pstd.span<Float> nodes, Float x, int* offset, pstd.span<Float> weights)
        {
            CHECK_GE((int)weights.size(), 4);
            // Return _false_ if _x_ is out of bounds
            if (!(x >= nodes.front() && x <= nodes.back()))
                return false;

            // Search for the interval _idx_ containing _x_
            int idx = (int)FindInterval(nodes.size(), (int i) => nodes[i] <= x);
            *offset = idx - 1;
            Float x0 = nodes[idx], x1 = nodes[idx + 1];

            // Compute the $t$ parameter and powers
            Float t = (x - x0) / (x1 - x0), t2 = t * t, t3 = t2 * t;

            // Compute initial node weights $w_1$ and $w_2$
            weights[1] = 2 * t3 - 3 * t2 + 1;
            weights[2] = -2 * t3 + 3 * t2;

            // Compute first node weight $w_0$
            if (idx > 0)
            {
                Float w0 = (t3 - 2 * t2 + t) * (x1 - x0) / (x1 - nodes[idx - 1]);
                weights[0] = -w0;
                weights[2] += w0;
            }
            else
            {
                Float w0 = t3 - 2 * t2 + t;
                weights[0] = 0;
                weights[1] -= w0;
                weights[2] += w0;
            }

            // Compute last node weight $w_3$
            if (idx + 2 < (int)nodes.size())
            {
                Float w3 = (t3 - t2) * (x1 - x0) / (nodes[idx + 2] - x0);
                weights[1] -= w3;
                weights[3] = w3;
            }
            else
            {
                Float w3 = t3 - t2;
                weights[1] -= w3;
                weights[2] += w3;
                weights[3] = 0;
            }

            return true;
        }

        public static Float CatmullRom(pstd.span<Float> nodes, pstd.span<Float> f, Float x)
        {
            CHECK_EQ((int)nodes.size(), (int)f.size());
            if (!(x >= nodes.front() && x <= nodes.back()))
                return 0;
            int idx = (int)FindInterval(nodes.size(), (int i) => nodes[i] <= x);
            Float x0 = nodes[idx], x1 = nodes[idx + 1];
            Float f0 = f[idx], f1 = f[idx + 1];
            Float width = x1 - x0;
            Float d0, d1;
            if (idx > 0)
                d0 = width * (f1 - f[idx - 1]) / (x1 - nodes[idx - 1]);
            else
                d0 = f1 - f0;

            if (idx + 2 < (int)nodes.size())
                d1 = width * (f[idx + 2] - f0) / (nodes[idx + 2] - x0);
            else
                d1 = f1 - f0;

            Float t = (x - x0) / (x1 - x0), t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * f0 + (-2 * t3 + 3 * t2) * f1 + (t3 - 2 * t2 + t) * d0 +
                   (t3 - t2) * d1;
        }

        public static Float InvertCatmullRom(pstd.span<Float> nodes, pstd.span<Float> f, Float u)
        {
            // Stop when _u_ is out of bounds
            if (!(u > f.front()))
                return nodes.front();
            else if (!(u < f.back()))
                return nodes.back();

            // Map _u_ to a spline interval by inverting _values_
            int i = (int)FindInterval(f.size(), (int j) => f[j] <= u);

            // Look up $x_i$ and function values of spline segment _i_
            Float x0 = nodes[i], x1 = nodes[i + 1];
            Float f0 = f[i], f1 = f[i + 1];
            Float width = x1 - x0;

            // Approximate derivatives using finite differences
            Float d0 = (i > 0) ? width * (f1 - f[i - 1]) / (x1 - nodes[i - 1]) : (f1 - f0);
            Float d1 = (i + 2 < (int)nodes.size()) ? width * (f[i + 2] - f0) / (nodes[i + 2] - x0) : (f1 - f0);

            // Invert the spline interpolant using Newton-Bisection
            Func<Float, (Float, Float)> eval = (Float tt) =>
            {
                // Compute powers of _t_
                Float t2 = tt * tt, t3 = t2 * tt;

                // Set _Fhat_ using Equation (8.27)
                Float Fhat = (2 * t3 - 3 * t2 + 1) * f0 + (-2 * t3 + 3 * t2) * f1 +
                             (t3 - 2 * t2 + tt) * d0 + (t3 - t2) * d1;

                // Set _fhat_ using Equation (not present)
                Float fhat = (6 * t2 - 6 * tt) * f0 + (-6 * t2 + 6 * tt) * f1 +
                             (3 * t2 - 4 * tt + 1) * d0 + (3 * t2 - 2 * tt) * d1;

                return (Fhat - u, fhat);
            };
            Float t = NewtonBisection(0, 1, eval);
            return x0 + t * width;
        }

        public static Float IntegrateCatmullRom(pstd.span<Float> nodes, pstd.span<Float> f, pstd.span<Float> cdf)
        {
            CHECK_EQ((int)nodes.size(), (int)f.size());
            Float sum = 0;
            cdf[0] = 0;
            for (int i = 0; i < (int)nodes.size() - 1; ++i)
            {
                // Look up $x_i$ and function values of spline segment _i_
                Float x0 = nodes[i], x1 = nodes[i + 1];
                Float f0 = f[i], f1 = f[i + 1];
                Float width = x1 - x0;

                // Approximate derivatives using finite differences
                Float d0 = (i > 0) ? width * (f1 - f[i - 1]) / (x1 - nodes[i - 1]) : (f1 - f0);
                Float d1 = (i + 2 < (int)nodes.size()) ? width * (f[i + 2] - f0) / (nodes[i + 2] - x0) : (f1 - f0);

                // Keep a running sum and build a cumulative distribution function
                sum += width * ((f0 + f1) / 2 + (d0 - d1) / 12);
                cdf[i + 1] = sum;
            }
            return sum;
        }

        public static Vector3f EqualAreaSquareToSphere(Point2f p)
        {
            CHECK(p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1);
            // Transform _p_ to $[-1,1]^2$ and compute absolute values
            Float u = 2 * p.x - 1, v = 2 * p.y - 1;
            Float up = std.abs(u), vp = std.abs(v);

            // Compute radius _r_ as signed distance from diagonal
            Float signedDistance = 1 - (up + vp);
            Float d = std.abs(signedDistance);
            Float r = 1 - d;

            // Compute angle $\phi$ for square to sphere mapping
            Float phi = (r == 0 ? 1 : (vp - up) / r + 1) * Pi / 4;

            // Find $z$ coordinate for spherical direction
            Float z = pstd.copysign(1 - Sqr(r), signedDistance);

            // Compute $\cos\phi$ and $\sin\phi$ for original quadrant and return vector
            Float cosPhi = pstd.copysign(std.cos(phi), u);
            Float sinPhi = pstd.copysign(std.sin(phi), v);
            return new Vector3f(cosPhi * r * SafeSqrt(2 - Sqr(r)), sinPhi * r * SafeSqrt(2 - Sqr(r)), z);
        }

        // Via source code from Clarberg: Fast Equal-Area Mapping of the (Hemi)Sphere using SIMD
        public static Point2f EqualAreaSphereToSquare(Vector3f d)
        {
            DCHECK(LengthSquared(d) > 0.999 && LengthSquared(d) < 1.001);
            Float x = std.abs(d.x), y = std.abs(d.y), z = std.abs(d.z);

            // Compute the radius r
            Float r = SafeSqrt(1 - z);  // r = sqrt(1-|z|)

            // Compute the argument to atan (detect a=0 to avoid div-by-zero)
            Float a = std.max(x, y), b = std.min(x, y);
            b = a == 0 ? 0 : b / a;

            // Polynomial approximation of atan(x)*2/pi, x=b
            // Coefficients for 6th degree minimax approximation of atan(x)*2/pi,
            // x=[0,1].
            const Float t1 = (Float)0.406758566246788489601959989e-5;
            const Float t2 = (Float)0.636226545274016134946890922156;
            const Float t3 = (Float)0.61572017898280213493197203466e-2;
            const Float t4 = (Float)(-0.247333733281268944196501420480);
            const Float t5 = (Float)0.881770664775316294736387951347e-1;
            const Float t6 = (Float)0.419038818029165735901852432784e-1;
            const Float t7 = (Float)(-0.251390972343483509333252996350e-1);
            Float phi = EvaluatePolynomial(b, t1, t2, t3, t4, t5, t6, t7);

            // Extend phi if the input is in the range 45-90 degrees (u<v)
            if (x < y)
                phi = 1 - phi;

            // Find (u,v) based on (r,phi)
            Float v = phi * r;
            Float u = r - v;

            if (d.z < 0)
            {
                // southern hemisphere -> mirror u,v
                pstd.swap(ref u, ref v);
                u = 1 - u;
                v = 1 - v;
            }

            // Move (u,v) to the correct quadrant based on the signs of (x,y)
            u = pstd.copysign(u, d.x);
            v = pstd.copysign(v, d.y);

            // Transform (u,v) from [-1,1] to [0,1]
            return new Point2f(0.5f * (u + 1), 0.5f * (v + 1));
        }

        public static Point2f WrapEqualAreaSquare(Point2f uv)
        {
            if (uv[0] < 0)
            {
                uv[0] = -uv[0];     // mirror across u = 0
                uv[1] = 1 - uv[1];  // mirror across v = 0.5
            }
            else if (uv[0] > 1)
            {
                uv[0] = 2 - uv[0];  // mirror across u = 1
                uv[1] = 1 - uv[1];  // mirror across v = 0.5
            }
            if (uv[1] < 0)
            {
                uv[0] = 1 - uv[0];  // mirror across u = 0.5
                uv[1] = -uv[1];     // mirror across v = 0;
            }
            else if (uv[1] > 1)
            {
                uv[0] = 1 - uv[0];  // mirror across u = 0.5
                uv[1] = 2 - uv[1];  // mirror across v = 1
            }
            return uv;
        }

        // SquareMatrix free functions
        public static Float Determinant(SquareMatrix2 m)
        {
            return DifferenceOfProducts(m[0][0], m[1][1], m[0][1], m[1][0]);
        }
        public static SquareMatrix2 Transpose(SquareMatrix2 m)
        {
            SquareMatrix2 r = new SquareMatrix2();
            for (int i = 0; i < 2; ++i)
                for (int j = 0; j < 2; ++j)
                    r[i][j] = m[j][i];
            return r;
        }
        // General-case Inverse() from math.cpp, instantiated for N = 2
        public static pstd.optional<SquareMatrix2> Inverse(SquareMatrix2 m)
        {
            const int N = 2;
            int* indxc = stackalloc int[N];
            int* indxr = stackalloc int[N];
            int* ipiv = stackalloc int[N];
            Float* minv = stackalloc Float[N * N];
            for (int i = 0; i < N; ++i)
                ipiv[i] = 0;
            for (int i = 0; i < N; ++i)
                for (int j = 0; j < N; ++j)
                    minv[i * N + j] = m[i][j];
            for (int i = 0; i < N; i++)
            {
                int irow = 0, icol = 0;
                Float big = 0.0f;
                // Choose pivot
                for (int j = 0; j < N; j++)
                {
                    if (ipiv[j] != 1)
                    {
                        for (int k = 0; k < N; k++)
                        {
                            if (ipiv[k] == 0)
                            {
                                if (std.abs(minv[j * N + k]) >= big)
                                {
                                    big = std.abs(minv[j * N + k]);
                                    irow = j;
                                    icol = k;
                                }
                            }
                            else if (ipiv[k] > 1)
                                return default;  // singular
                        }
                    }
                }
                ++ipiv[icol];
                // Swap rows _irow_ and _icol_ for pivot
                if (irow != icol)
                {
                    for (int k = 0; k < N; ++k)
                        pstd.swap(ref minv[irow * N + k], ref minv[icol * N + k]);
                }
                indxr[i] = irow;
                indxc[i] = icol;
                if (minv[icol * N + icol] == 0.0f)
                    return default;  // singular

                // Set $m[icol][icol]$ to one by scaling row _icol_ appropriately
                Float pivinv = (Float)(1.0 / minv[icol * N + icol]);
                minv[icol * N + icol] = 1;
                for (int j = 0; j < N; j++)
                    minv[icol * N + j] *= pivinv;

                // Subtract this row from others to zero out their columns
                for (int j = 0; j < N; j++)
                {
                    if (j != icol)
                    {
                        Float save = minv[j * N + icol];
                        minv[j * N + icol] = 0;
                        for (int k = 0; k < N; k++)
                            minv[j * N + k] = FMA(-minv[icol * N + k], save, minv[j * N + k]);
                    }
                }
            }
            // Swap columns to reflect permutation
            for (int j = N - 1; j >= 0; j--)
            {
                if (indxr[j] != indxc[j])
                {
                    for (int k = 0; k < N; k++)
                        pstd.swap(ref minv[k * N + indxr[j]], ref minv[k * N + indxc[j]]);
                }
            }
            return new SquareMatrix2(minv);
        }

        public static Float Determinant(SquareMatrix3 m)
        {
            Float minor12 = DifferenceOfProducts(m[1][1], m[2][2], m[1][2], m[2][1]);
            Float minor02 = DifferenceOfProducts(m[1][0], m[2][2], m[1][2], m[2][0]);
            Float minor01 = DifferenceOfProducts(m[1][0], m[2][1], m[1][1], m[2][0]);
            return FMA(m[0][2], minor01, DifferenceOfProducts(m[0][0], minor12, m[0][1], minor02));
        }

        public static Float Determinant(SquareMatrix4 m)
        {
            Float s0 = DifferenceOfProducts(m[0][0], m[1][1], m[1][0], m[0][1]);
            Float s1 = DifferenceOfProducts(m[0][0], m[1][2], m[1][0], m[0][2]);
            Float s2 = DifferenceOfProducts(m[0][0], m[1][3], m[1][0], m[0][3]);

            Float s3 = DifferenceOfProducts(m[0][1], m[1][2], m[1][1], m[0][2]);
            Float s4 = DifferenceOfProducts(m[0][1], m[1][3], m[1][1], m[0][3]);
            Float s5 = DifferenceOfProducts(m[0][2], m[1][3], m[1][2], m[0][3]);

            Float c0 = DifferenceOfProducts(m[2][0], m[3][1], m[3][0], m[2][1]);
            Float c1 = DifferenceOfProducts(m[2][0], m[3][2], m[3][0], m[2][2]);
            Float c2 = DifferenceOfProducts(m[2][0], m[3][3], m[3][0], m[2][3]);

            Float c3 = DifferenceOfProducts(m[2][1], m[3][2], m[3][1], m[2][2]);
            Float c4 = DifferenceOfProducts(m[2][1], m[3][3], m[3][1], m[2][3]);
            Float c5 = DifferenceOfProducts(m[2][2], m[3][3], m[3][2], m[2][3]);

            return (DifferenceOfProducts(s0, c5, s1, c4) + DifferenceOfProducts(s2, c3, -s3, c2) +
                    DifferenceOfProducts(s5, c0, s4, c1));
        }

        public static SquareMatrix3 Transpose(SquareMatrix3 m)
        {
            SquareMatrix3 r = new SquareMatrix3();
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    r[i][j] = m[j][i];
            return r;
        }

        public static SquareMatrix4 Transpose(SquareMatrix4 m)
        {
            SquareMatrix4 r = new SquareMatrix4();
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                    r[i][j] = m[j][i];
            return r;
        }

        public static SquareMatrix3 InvertOrExit(SquareMatrix3 m)
        {
            pstd.optional<SquareMatrix3> inv = Inverse(m);
            CHECK(inv.has_value());
            return inv.value();
        }

        public static SquareMatrix4 InvertOrExit(SquareMatrix4 m)
        {
            pstd.optional<SquareMatrix4> inv = Inverse(m);
            CHECK(inv.has_value());
            return inv.value();
        }

        public static pstd.optional<SquareMatrix3> Inverse(SquareMatrix3 m)
        {
            Float det = Determinant(m);
            if (det == 0)
                return default;
            Float invDet = 1 / det;

            SquareMatrix3 r = new SquareMatrix3();

            // Compute adjugate matrix
            r[0][0] = invDet * DifferenceOfProducts(m[1][1], m[2][2], m[1][2], m[2][1]);
            r[1][0] = invDet * DifferenceOfProducts(m[1][2], m[2][0], m[1][0], m[2][2]);
            r[2][0] = invDet * DifferenceOfProducts(m[1][0], m[2][1], m[1][1], m[2][0]);
            r[0][1] = invDet * DifferenceOfProducts(m[0][2], m[2][1], m[0][1], m[2][2]);
            r[1][1] = invDet * DifferenceOfProducts(m[0][0], m[2][2], m[0][2], m[2][0]);
            r[2][1] = invDet * DifferenceOfProducts(m[0][1], m[2][0], m[0][0], m[2][1]);
            r[0][2] = invDet * DifferenceOfProducts(m[0][1], m[1][2], m[0][2], m[1][1]);
            r[1][2] = invDet * DifferenceOfProducts(m[0][2], m[1][0], m[0][0], m[1][2]);
            r[2][2] = invDet * DifferenceOfProducts(m[0][0], m[1][1], m[0][1], m[1][0]);

            return r;
        }

        public static pstd.optional<SquareMatrix4> Inverse(SquareMatrix4 m)
        {
            // Via: https://github.com/google/ion/blob/master/ion/math/matrixutils.cc,
            // (c) Google, Apache license.

            // For 4x4 do not compute the adjugate as the determinant of
            // subdeterminants. Instead, compute the 2x2 subdeterminants and use
            // them to obtain the 3x3 subdeterminants.
            Float s0 = DifferenceOfProducts(m[0][0], m[1][1], m[1][0], m[0][1]);
            Float s1 = DifferenceOfProducts(m[0][0], m[1][2], m[1][0], m[0][2]);
            Float s2 = DifferenceOfProducts(m[0][0], m[1][3], m[1][0], m[0][3]);

            Float s3 = DifferenceOfProducts(m[0][1], m[1][2], m[1][1], m[0][2]);
            Float s4 = DifferenceOfProducts(m[0][1], m[1][3], m[1][1], m[0][3]);
            Float s5 = DifferenceOfProducts(m[0][2], m[1][3], m[1][2], m[0][3]);

            Float c0 = DifferenceOfProducts(m[2][0], m[3][1], m[3][0], m[2][1]);
            Float c1 = DifferenceOfProducts(m[2][0], m[3][2], m[3][0], m[2][2]);
            Float c2 = DifferenceOfProducts(m[2][0], m[3][3], m[3][0], m[2][3]);

            Float c3 = DifferenceOfProducts(m[2][1], m[3][2], m[3][1], m[2][2]);
            Float c4 = DifferenceOfProducts(m[2][1], m[3][3], m[3][1], m[2][3]);
            Float c5 = DifferenceOfProducts(m[2][2], m[3][3], m[3][2], m[2][3]);

            Float determinant = InnerProduct(s0, c5, -s1, c4, s2, c3, s3, c2, s5, c0, -s4, c1);
            if (determinant == 0)
                return default;
            Float s = 1 / determinant;

            Float* inv = stackalloc Float[16];
            inv[0] = s * InnerProduct(m[1][1], c5, m[1][3], c3, -m[1][2], c4);
            inv[1] = s * InnerProduct(-m[0][1], c5, m[0][2], c4, -m[0][3], c3);
            inv[2] = s * InnerProduct(m[3][1], s5, m[3][3], s3, -m[3][2], s4);
            inv[3] = s * InnerProduct(-m[2][1], s5, m[2][2], s4, -m[2][3], s3);

            inv[4] = s * InnerProduct(-m[1][0], c5, m[1][2], c2, -m[1][3], c1);
            inv[5] = s * InnerProduct(m[0][0], c5, m[0][3], c1, -m[0][2], c2);
            inv[6] = s * InnerProduct(-m[3][0], s5, m[3][2], s2, -m[3][3], s1);
            inv[7] = s * InnerProduct(m[2][0], s5, m[2][3], s1, -m[2][2], s2);

            inv[8] = s * InnerProduct(m[1][0], c4, m[1][3], c0, -m[1][1], c2);
            inv[9] = s * InnerProduct(-m[0][0], c4, m[0][1], c2, -m[0][3], c0);
            inv[10] = s * InnerProduct(m[3][0], s4, m[3][3], s0, -m[3][1], s2);
            inv[11] = s * InnerProduct(-m[2][0], s4, m[2][1], s2, -m[2][3], s0);

            inv[12] = s * InnerProduct(-m[1][0], c3, m[1][1], c1, -m[1][2], c0);
            inv[13] = s * InnerProduct(m[0][0], c3, m[0][2], c0, -m[0][1], c1);
            inv[14] = s * InnerProduct(-m[3][0], s3, m[3][1], s1, -m[3][2], s0);
            inv[15] = s * InnerProduct(m[2][0], s3, m[2][2], s0, -m[2][1], s1);

            return new SquareMatrix4(inv);
        }

        public static SquareMatrix3 operator_mul(SquareMatrix3 m1, SquareMatrix3 m2) { return m1 * m2; }
    }

    // CompensatedSum Definition (template <typename Float> class CompensatedSum)
    public unsafe struct CompensatedSum<T> where T : unmanaged, System.Numerics.IFloatingPointIeee754<T>
    {
        // CompensatedSum Public Methods
        public CompensatedSum(T v) { sum = v; c = T.Zero; }

        public void Assign(T v) { sum = v; c = T.Zero; }

        public static CompensatedSum<T> operator +(CompensatedSum<T> s, T v)
        {
            // Algorithm 3: Kahan Summation (compensated)
            T delta = v - s.c;
            T newSum = s.sum + delta;
            s.c = (newSum - s.sum) - delta;
            s.sum = newSum;
            return s;
        }

        public static explicit operator T(CompensatedSum<T> s) { return s.sum; }

        public override string ToString() { return StringPrintf("[ CompensatedSum sum: %s c: %s ]", sum, c); }

        // CompensatedSum Private Members
        private T sum, c;
    }

    // CompensatedFloat Definition
    public unsafe struct CompensatedFloat
    {
        // CompensatedFloat Public Methods
        public CompensatedFloat(Float v, Float err = 0) { this.v = v; this.err = err; }
        public static explicit operator float(CompensatedFloat c) { return (float)(c.v + c.err); }
        public static explicit operator double(CompensatedFloat c) { return (double)c.v + (double)c.err; }
        public override string ToString() { return $"[ CompensatedFloat v: {v} err: {err} ]"; }

        public Float v, err;
    }

    // Interval Definition
    public unsafe struct Interval
    {
        // Interval Public Methods
        public Interval(Float v) { low = v; high = v; }
        public Interval(Float low, Float high)
        {
            this.low = std.min(low, high);
            this.high = std.max(low, high);
        }

        public static Interval FromValueAndError(Float v, Float err)
        {
            Interval i = new Interval();
            if (err == 0)
                i.low = i.high = v;
            else
            {
                // Compute conservative bounds by rounding the endpoints away
                // from the middle. Note that this will be (slightly) suboptimal
                // for the case where floating-point rounding would have
                // actually caused the interval to be exact.
                i.low = SubRoundDown(v, err);
                i.high = AddRoundUp(v, err);
            }
            return i;
        }

        public void Assign(Float v) { low = high = v; }

        public Float UpperBound() { return high; }
        public Float LowerBound() { return low; }
        public Float Midpoint() { return (low + high) / 2; }
        public Float Width() { return high - low; }

        public Float this[int i]
        {
            get
            {
                DCHECK(i == 0 || i == 1);
                return (i == 0) ? low : high;
            }
        }

        public static explicit operator Float(Interval i) { return i.Midpoint(); }

        public bool Exactly(Float v) { return low == v && high == v; }

        public static bool operator ==(Interval a, Float v) { return a.Exactly(v); }
        public static bool operator !=(Interval a, Float f) { return f < a.low || f > a.high; }

        public static Interval operator -(Interval i) { return new Interval(-i.high, -i.low); }

        public static Interval operator +(Interval a, Interval i)
        {
            return new Interval(AddRoundDown(a.low, i.low), AddRoundUp(a.high, i.high));
        }

        public static Interval operator -(Interval a, Interval i)
        {
            return new Interval(SubRoundDown(a.low, i.high), SubRoundUp(a.high, i.low));
        }

        public static Interval operator *(Interval a, Interval i)
        {
            Float* lp = stackalloc Float[4] { MulRoundDown(a.low, i.low), MulRoundDown(a.high, i.low),
                                              MulRoundDown(a.low, i.high), MulRoundDown(a.high, i.high) };
            Float* hp = stackalloc Float[4] { MulRoundUp(a.low, i.low), MulRoundUp(a.high, i.low),
                                              MulRoundUp(a.low, i.high), MulRoundUp(a.high, i.high) };
            return new Interval(std.min(std.min(lp[0], lp[1]), std.min(lp[2], lp[3])),
                                std.max(std.max(hp[0], hp[1]), std.max(hp[2], hp[3])));
        }

        public static Interval operator /(Interval a, Interval i)
        {
            if (InRange(0, i))
                // The interval we're dividing by straddles zero, so just
                // return an interval of everything.
                return new Interval(-Infinity, Infinity);

            Float* lowQuot = stackalloc Float[4] { DivRoundDown(a.low, i.low), DivRoundDown(a.high, i.low),
                                                   DivRoundDown(a.low, i.high), DivRoundDown(a.high, i.high) };
            Float* highQuot = stackalloc Float[4] { DivRoundUp(a.low, i.low), DivRoundUp(a.high, i.low),
                                                    DivRoundUp(a.low, i.high), DivRoundUp(a.high, i.high) };
            return new Interval(std.min(std.min(lowQuot[0], lowQuot[1]), std.min(lowQuot[2], lowQuot[3])),
                                std.max(std.max(highQuot[0], highQuot[1]), std.max(highQuot[2], highQuot[3])));
        }

        public static bool operator ==(Interval a, Interval i) { return a.low == i.low && a.high == i.high; }
        public static bool operator !=(Interval a, Interval i) { return !(a == i); }
        public override bool Equals(object o) { return o is Interval i && this == i; }
        public override int GetHashCode() { return HashCode.Combine(low, high); }

        public override string ToString() { return $"[ Interval [{low}, {high}] ]"; }

        public static Interval operator +(Float f, Interval i) { return new Interval(f) + i; }
        public static Interval operator -(Float f, Interval i) { return new Interval(f) - i; }
        public static Interval operator *(Float f, Interval i)
        {
            if (f > 0)
                return new Interval(MulRoundDown(f, i.LowerBound()), MulRoundUp(f, i.UpperBound()));
            else
                return new Interval(MulRoundDown(f, i.UpperBound()), MulRoundUp(f, i.LowerBound()));
        }
        public static Interval operator /(Float f, Interval i)
        {
            if (InRange(0, i))
                // The interval we're dividing by straddles zero, so just
                // return an interval of everything.
                return new Interval(-Infinity, Infinity);

            if (f > 0)
                return new Interval(DivRoundDown(f, i.UpperBound()), DivRoundUp(f, i.LowerBound()));
            else
                return new Interval(DivRoundDown(f, i.LowerBound()), DivRoundUp(f, i.UpperBound()));
        }
        public static Interval operator +(Interval i, Float f) { return i + new Interval(f); }
        public static Interval operator -(Interval i, Float f) { return i - new Interval(f); }
        public static Interval operator *(Interval i, Float f)
        {
            if (f > 0)
                return new Interval(MulRoundDown(f, i.LowerBound()), MulRoundUp(f, i.UpperBound()));
            else
                return new Interval(MulRoundDown(f, i.UpperBound()), MulRoundUp(f, i.LowerBound()));
        }
        public static Interval operator /(Interval i, Float f)
        {
            if (f == 0)
                return new Interval(-Infinity, Infinity);

            if (f > 0)
                return new Interval(DivRoundDown(i.LowerBound(), f), DivRoundUp(i.UpperBound(), f));
            else
                return new Interval(DivRoundDown(i.UpperBound(), f), DivRoundUp(i.LowerBound(), f));
        }

        // Interval Public Members
#if PBRT_FLOAT_AS_DOUBLE
        public static readonly Interval Pi = new Interval(3.1415926535897931, 3.1415926535897936);
#else
        public static readonly Interval Pi = new Interval(3.1415925f, 3.14159274f);
#endif

        // Interval Private Members
        private Float low, high;
    }

    // SquareMatrix: C# has no integer template parameters, so SquareMatrix<2>, SquareMatrix<3> and
    // SquareMatrix<4> are expanded by hand as SquareMatrix2 / SquareMatrix3 / SquareMatrix4.
    public unsafe ref struct MatrixRow
    {
        private ref Float r;
        public MatrixRow(ref Float r) { this.r = ref r; }
        public ref Float this[int j]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return ref Unsafe.Add(ref r, j); }
        }
    }

    public unsafe struct SquareMatrix2
    {
        // SquareMatrix Public Methods
        public static SquareMatrix2 Zero()
        {
            return default;
        }

        public SquareMatrix2()
        {
            m00 = m11 = 1;
        }

        public SquareMatrix2(Float* mat)
        {
            m00 = mat[0];
            m01 = mat[1];
            m10 = mat[2];
            m11 = mat[3];
        }

        public SquareMatrix2(pstd.span<Float> t)
        {
            CHECK_EQ(4, (int)t.size());
            m00 = t[0];
            m01 = t[1];
            m10 = t[2];
            m11 = t[3];
        }

        public SquareMatrix2(Float m00, Float m01,
                             Float m10, Float m11)
        {
            this.m00 = m00;
            this.m01 = m01;
            this.m10 = m10;
            this.m11 = m11;
        }

        public static SquareMatrix2 Diag(Float v1, Float v2)
        {
            SquareMatrix2 ret = default;
            ret.m00 = v1;
            ret.m11 = v2;
            return ret;
        }

        public static SquareMatrix2 operator +(SquareMatrix2 m1, SquareMatrix2 m2)
        {
            SquareMatrix2 r = m1;
            r.m00 += m2.m00;
            r.m01 += m2.m01;
            r.m10 += m2.m10;
            r.m11 += m2.m11;
            return r;
        }

        public static SquareMatrix2 operator *(SquareMatrix2 m, Float s)
        {
            SquareMatrix2 r = m;
            r.m00 *= s;
            r.m01 *= s;
            r.m10 *= s;
            r.m11 *= s;
            return r;
        }

        public static SquareMatrix2 operator /(SquareMatrix2 m, Float s)
        {
            DCHECK_NE(s, (Float)0);
            SquareMatrix2 r = m;
            r.m00 /= s;
            r.m01 /= s;
            r.m10 /= s;
            r.m11 /= s;
            return r;
        }

        public static bool operator ==(SquareMatrix2 m1, SquareMatrix2 m2)
        {
            return (m1.m00 == m2.m00 && m1.m11 == m2.m11 && // Check diagonal element first for early out.
                    m1.m01 == m2.m01 && m1.m10 == m2.m10);
        }

        public static bool operator !=(SquareMatrix2 m1, SquareMatrix2 m2)
        {
            return (m1.m00 != m2.m00 || m1.m01 != m2.m01 ||
                    m1.m10 != m2.m10 || m1.m11 != m2.m11);
        }

        public static bool operator <(SquareMatrix2 m1, SquareMatrix2 m2)
        {
            for (int i = 0; i < 2; ++i)
                for (int j = 0; j < 2; ++j) {
                    if (m1[i][j] < m2[i][j])
                        return true;
                    if (m1[i][j] > m2[i][j])
                        return false;
                }
            return false;
        }

        public bool IsIdentity()
        {
            return m00 == 1f && m11 == 1f && // Check diagonal element first for early out.
                   m01 == 0f &&
                   m10 == 0f;
        }

        public override string ToString()
        {
            string s = "[ [";
            for (int i = 0; i < 2; ++i)
            {
                for (int j = 0; j < 2; ++j)
                {
                    s += StringPrintf(" %f", this[i][j]);
                    if (j < 2 - 1) s += ','; else s += " ]";
                }
                if (i < 2 - 1) s += ", [";
            }
            s += " ]";
            return s;
        }

        [System.Diagnostics.CodeAnalysis.UnscopedRef]
        public MatrixRow this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return new MatrixRow(ref Unsafe.Add(ref m00, i * 2)); }
        }

        // SquareMatrix Private Members
        internal Float m00;
        internal Float m01;
        internal Float m10;
        internal Float m11;

        public static bool operator >(SquareMatrix2 m1, SquareMatrix2 m2)
        {
            for (int i = 0; i < 2; ++i)
                for (int j = 0; j < 2; ++j) {
                    if (m1[i][j] > m2[i][j])
                        return true;
                    if (m1[i][j] < m2[i][j])
                        return false;
                }
            return false;
        }

        public static SquareMatrix2 operator *(Float s, SquareMatrix2 m)
        {
            return m * s;
        }

        // Matrix * vector (Mul<Tresult>)
        public static Vector2f operator *(SquareMatrix2 mm, Vector2f v)
        {
            Vector2f result = new Vector2f();
            for (int i = 0; i < 2; ++i)
            {
                result[i] = 0;
                for (int j = 0; j < 2; ++j)
                    result[i] += mm[i][j] * v[j];
            }
            return result;
        }
        public static pstd.array2<Float> operator *(SquareMatrix2 mm, pstd.array2<Float> v)
        {
            pstd.array2<Float> result = new pstd.array2<Float>();
            for (int i = 0; i < 2; ++i)
            {
                result[i] = 0;
                for (int j = 0; j < 2; ++j)
                    result[i] += mm[i][j] * v[j];
            }
            return result;
        }

        public static SquareMatrix2 operator *(SquareMatrix2 m1, SquareMatrix2 m2)
        {
            SquareMatrix2 r;
            r.m00 = FMA(m1.m01, m2.m10, FMA(m1.m00, m2.m00, 0f));
            r.m01 = FMA(m1.m01, m2.m11, FMA(m1.m00, m2.m01, 0f));
            r.m10 = FMA(m1.m11, m2.m10, FMA(m1.m10, m2.m00, 0f));
            r.m11 = FMA(m1.m11, m2.m11, FMA(m1.m10, m2.m01, 0f));
            return r;
        }
    }

    public unsafe struct SquareMatrix3
    {
        // SquareMatrix Public Methods
        public static SquareMatrix3 Zero()
        {
            return default;
        }

        public SquareMatrix3()
        {
            m00 = m11 = m22 = 1;
        }

        public SquareMatrix3(Float* mat)
        {
            m00 = mat[0];
            m01 = mat[1];
            m02 = mat[2];
            m10 = mat[3];
            m11 = mat[4];
            m12 = mat[5];
            m20 = mat[6];
            m21 = mat[7];
            m22 = mat[8];
        }

        public SquareMatrix3(pstd.span<Float> t)
        {
            CHECK_EQ(9, (int)t.size());
            m00 = t[0];
            m01 = t[1];
            m02 = t[2];
            m10 = t[3];
            m11 = t[4];
            m12 = t[5];
            m20 = t[6];
            m21 = t[7];
            m22 = t[8];
        }

        public SquareMatrix3(Float m00, Float m01, Float m02,
                             Float m10, Float m11, Float m12,
                             Float m20, Float m21, Float m22)
        {
            this.m00 = m00;
            this.m01 = m01;
            this.m02 = m02;
            this.m10 = m10;
            this.m11 = m11;
            this.m12 = m12;
            this.m20 = m20;
            this.m21 = m21;
            this.m22 = m22;
        }

        public static SquareMatrix3 Diag(Float v1, Float v2, Float v3)
        {
            SquareMatrix3 ret = default;
            ret.m00 = v1;
            ret.m11 = v2;
            ret.m22 = v3;
            return ret;
        }

        public static SquareMatrix3 operator +(SquareMatrix3 m1, SquareMatrix3 m2)
        {
            SquareMatrix3 r = m1;
            r.m00 += m2.m00;
            r.m01 += m2.m01;
            r.m02 += m2.m02;
            r.m10 += m2.m10;
            r.m11 += m2.m11;
            r.m12 += m2.m12;
            r.m20 += m2.m20;
            r.m21 += m2.m21;
            r.m22 += m2.m22;
            return r;
        }

        public static SquareMatrix3 operator *(SquareMatrix3 m, Float s)
        {
            SquareMatrix3 r = m;
            r.m00 *= s;
            r.m01 *= s;
            r.m02 *= s;
            r.m10 *= s;
            r.m11 *= s;
            r.m12 *= s;
            r.m20 *= s;
            r.m21 *= s;
            r.m22 *= s;
            return r;
        }

        public static SquareMatrix3 operator /(SquareMatrix3 m, Float s)
        {
            DCHECK_NE(s, (Float)0);
            SquareMatrix3 r = m;
            r.m00 /= s;
            r.m01 /= s;
            r.m02 /= s;
            r.m10 /= s;
            r.m11 /= s;
            r.m12 /= s;
            r.m20 /= s;
            r.m21 /= s;
            r.m22 /= s;
            return r;
        }

        public static bool operator ==(SquareMatrix3 m1, SquareMatrix3 m2)
        {
            return (m1.m00 == m2.m00 && m1.m11 == m2.m11 && m1.m22 == m2.m22 && // Check diagonal element first for early out.
                    m1.m01 == m2.m01 && m1.m02 == m2.m02 && m1.m10 == m2.m10 &&
                    m1.m12 == m2.m12 && m1.m20 == m2.m20 && m1.m21 == m2.m21);
        }

        public static bool operator !=(SquareMatrix3 m1, SquareMatrix3 m2)
        {
            return (m1.m00 != m2.m00 || m1.m01 != m2.m01 || m1.m02 != m2.m02 ||
                    m1.m10 != m2.m10 || m1.m11 != m2.m11 || m1.m12 != m2.m12 ||
                    m1.m20 != m2.m20 || m1.m21 != m2.m21 || m1.m22 != m2.m22);
        }

        public static bool operator <(SquareMatrix3 m1, SquareMatrix3 m2)
        {
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j) {
                    if (m1[i][j] < m2[i][j])
                        return true;
                    if (m1[i][j] > m2[i][j])
                        return false;
                }
            return false;
        }

        public bool IsIdentity()
        {
            return m00 == 1f && m11 == 1f && m22 == 1f && // Check diagonal element first for early out.
                   m01 == 0f && m02 == 0f &&
                   m10 == 0f && m12 == 0f &&
                   m20 == 0f && m21 == 0f;
        }

        public override string ToString()
        {
            string s = "[ [";
            for (int i = 0; i < 3; ++i)
            {
                for (int j = 0; j < 3; ++j)
                {
                    s += $" {this[i][j]}";
                    if (j < 3 - 1) s += ','; else s += " ]";
                }
                if (i < 3 - 1) s += ", [";
            }
            s += " ]";
            return s;
        }

        [System.Diagnostics.CodeAnalysis.UnscopedRef]
        public MatrixRow this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return new MatrixRow(ref Unsafe.Add(ref m00, i * 3)); }
        }

        // SquareMatrix Private Members
        internal Float m00;
        internal Float m01;
        internal Float m02;
        internal Float m10;
        internal Float m11;
        internal Float m12;
        internal Float m20;
        internal Float m21;
        internal Float m22;

        public static bool operator >(SquareMatrix3 m1, SquareMatrix3 m2)
        {
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j) {
                    if (m1[i][j] > m2[i][j])
                        return true;
                    if (m1[i][j] < m2[i][j])
                        return false;
                }
            return false;
        }

        public static SquareMatrix3 operator *(Float s, SquareMatrix3 m)
        {
            return m * s;
        }

        // Matrix * vector (Mul<Tresult>)
        public static Vector3f operator *(SquareMatrix3 mm, Vector3f v)
        {
            Vector3f result = new Vector3f();
            for (int i = 0; i < 3; ++i)
            {
                result[i] = 0;
                for (int j = 0; j < 3; ++j)
                    result[i] += mm[i][j] * v[j];
            }
            return result;
        }

        public static pstd.array3<Float> operator *(SquareMatrix3 mm, pstd.array3<Float> v)
        {
            pstd.array3<Float> result = new pstd.array3<Float>();
            for (int i = 0; i < 3; ++i)
            {
                result[i] = 0;
                for (int j = 0; j < 3; ++j)
                    result[i] += mm[i][j] * v[j];
            }
            return result;
        }

        public static SquareMatrix3 operator *(SquareMatrix3 m1, SquareMatrix3 m2)
        {
            SquareMatrix3 r;
            r.m00 = InnerProduct(m1.m00, m2.m00, m1.m01, m2.m10, m1.m02, m2.m20);
            r.m01 = InnerProduct(m1.m00, m2.m01, m1.m01, m2.m11, m1.m02, m2.m21);
            r.m02 = InnerProduct(m1.m00, m2.m02, m1.m01, m2.m12, m1.m02, m2.m22);
            r.m10 = InnerProduct(m1.m10, m2.m00, m1.m11, m2.m10, m1.m12, m2.m20);
            r.m11 = InnerProduct(m1.m10, m2.m01, m1.m11, m2.m11, m1.m12, m2.m21);
            r.m12 = InnerProduct(m1.m10, m2.m02, m1.m11, m2.m12, m1.m12, m2.m22);
            r.m20 = InnerProduct(m1.m20, m2.m00, m1.m21, m2.m10, m1.m22, m2.m20);
            r.m21 = InnerProduct(m1.m20, m2.m01, m1.m21, m2.m11, m1.m22, m2.m21);
            r.m22 = InnerProduct(m1.m20, m2.m02, m1.m21, m2.m12, m1.m22, m2.m22);
            return r;
        }
    }

    public unsafe struct SquareMatrix4
    {
        // SquareMatrix Public Methods
        public static SquareMatrix4 Zero()
        {
            return default;
        }

        public SquareMatrix4()
        {
            m00 = m11 = m22 = m33 = 1;
        }

        public SquareMatrix4(Float* mat)
        {
            m00 = mat[0];
            m01 = mat[1];
            m02 = mat[2];
            m03 = mat[3];
            m10 = mat[4];
            m11 = mat[5];
            m12 = mat[6];
            m13 = mat[7];
            m20 = mat[8];
            m21 = mat[9];
            m22 = mat[10];
            m23 = mat[11];
            m30 = mat[12];
            m31 = mat[13];
            m32 = mat[14];
            m33 = mat[15];
        }

        public SquareMatrix4(pstd.span<Float> t)
        {
            CHECK_EQ(16, (int)t.size());
            m00 = t[0];
            m01 = t[1];
            m02 = t[2];
            m03 = t[3];
            m10 = t[4];
            m11 = t[5];
            m12 = t[6];
            m13 = t[7];
            m20 = t[8];
            m21 = t[9];
            m22 = t[10];
            m23 = t[11];
            m30 = t[12];
            m31 = t[13];
            m32 = t[14];
            m33 = t[15];
        }

        public SquareMatrix4(Float m00, Float m01, Float m02, Float m03,
                             Float m10, Float m11, Float m12, Float m13,
                             Float m20, Float m21, Float m22, Float m23,
                             Float m30, Float m31, Float m32, Float m33)
        {
            this.m00 = m00;
            this.m01 = m01;
            this.m02 = m02;
            this.m03 = m03;
            this.m10 = m10;
            this.m11 = m11;
            this.m12 = m12;
            this.m13 = m13;
            this.m20 = m20;
            this.m21 = m21;
            this.m22 = m22;
            this.m23 = m23;
            this.m30 = m30;
            this.m31 = m31;
            this.m32 = m32;
            this.m33 = m33;
        }

        public static SquareMatrix4 Diag(Float v1, Float v2, Float v3, Float v4)
        {
            SquareMatrix4 ret = default;
            ret.m00 = v1;
            ret.m11 = v2;
            ret.m22 = v3;
            ret.m33 = v4;
            return ret;
        }

        public static SquareMatrix4 operator +(SquareMatrix4 m1, SquareMatrix4 m2)
        {
            SquareMatrix4 r = m1;
            r.m00 += m2.m00;
            r.m01 += m2.m01;
            r.m02 += m2.m02;
            r.m03 += m2.m03;
            r.m10 += m2.m10;
            r.m11 += m2.m11;
            r.m12 += m2.m12;
            r.m13 += m2.m13;
            r.m20 += m2.m20;
            r.m21 += m2.m21;
            r.m22 += m2.m22;
            r.m23 += m2.m23;
            r.m30 += m2.m30;
            r.m31 += m2.m31;
            r.m32 += m2.m32;
            r.m33 += m2.m33;
            return r;
        }

        public static SquareMatrix4 operator *(SquareMatrix4 m, Float s)
        {
            SquareMatrix4 r = m;
            r.m00 *= s;
            r.m01 *= s;
            r.m02 *= s;
            r.m03 *= s;
            r.m10 *= s;
            r.m11 *= s;
            r.m12 *= s;
            r.m13 *= s;
            r.m20 *= s;
            r.m21 *= s;
            r.m22 *= s;
            r.m23 *= s;
            r.m30 *= s;
            r.m31 *= s;
            r.m32 *= s;
            r.m33 *= s;
            return r;
        }

        public static SquareMatrix4 operator /(SquareMatrix4 m, Float s)
        {
            DCHECK_NE(s, (Float)0);
            SquareMatrix4 r = m;
            r.m00 /= s;
            r.m01 /= s;
            r.m02 /= s;
            r.m03 /= s;
            r.m10 /= s;
            r.m11 /= s;
            r.m12 /= s;
            r.m13 /= s;
            r.m20 /= s;
            r.m21 /= s;
            r.m22 /= s;
            r.m23 /= s;
            r.m30 /= s;
            r.m31 /= s;
            r.m32 /= s;
            r.m33 /= s;
            return r;
        }

        public static bool operator ==(SquareMatrix4 m1, SquareMatrix4 m2)
        {
            return (m1.m00 == m2.m00 && m1.m11 == m2.m11 && m1.m22 == m2.m22 && m1.m33 == m2.m33 && // Check diagonal element first for early out.
                    m1.m01 == m2.m01 && m1.m02 == m2.m02 && m1.m03 == m2.m03 && m1.m10 == m2.m10 &&
                    m1.m12 == m2.m12 && m1.m13 == m2.m13 && m1.m20 == m2.m20 && m1.m21 == m2.m21 &&
                    m1.m23 == m2.m23 && m1.m30 == m2.m30 && m1.m31 == m2.m31 && m1.m32 == m2.m32);
        }

        public static bool operator !=(SquareMatrix4 m1, SquareMatrix4 m2)
        {
            return (m1.m00 != m2.m00 || m1.m01 != m2.m01 || m1.m02 != m2.m02 || m1.m03 != m2.m03 ||
                    m1.m10 != m2.m10 || m1.m11 != m2.m11 || m1.m12 != m2.m12 || m1.m13 != m2.m13 ||
                    m1.m20 != m2.m20 || m1.m21 != m2.m21 || m1.m22 != m2.m22 || m1.m23 != m2.m23 ||
                    m1.m30 != m2.m30 || m1.m31 != m2.m31 || m1.m32 != m2.m32 || m1.m33 != m2.m33);
        }

        public static bool operator <(SquareMatrix4 m1, SquareMatrix4 m2)
        {
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j) {
                    if (m1[i][j] < m2[i][j])
                        return true;
                    if (m1[i][j] > m2[i][j])
                        return false;
                }
            return false;
        }

        public bool IsIdentity()
        {
            return m00 == 1f && m11 == 1f && m22 == 1f && m33 == 1f && // Check diagonal element first for early out.
                   m01 == 0f && m02 == 0f && m03 == 0f &&
                   m10 == 0f && m12 == 0f && m13 == 0f &&
                   m20 == 0f && m21 == 0f && m23 == 0f &&
                   m30 == 0f && m31 == 0f && m32 == 0f;
        }

        public override string ToString()
        {
            string s = "[ [";
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    s += $" {this[i][j]}";
                    if (j < 4 - 1) s += ','; else s += " ]";
                }
                if (i < 4 - 1) s += ", [";
            }
            s += " ]";
            return s;
        }

        [System.Diagnostics.CodeAnalysis.UnscopedRef]
        public MatrixRow this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return new MatrixRow(ref Unsafe.Add(ref m00, i * 4)); }
        }

        // SquareMatrix Private Members
        internal Float m00;
        internal Float m01;
        internal Float m02;
        internal Float m03;
        internal Float m10;
        internal Float m11;
        internal Float m12;
        internal Float m13;
        internal Float m20;
        internal Float m21;
        internal Float m22;
        internal Float m23;
        internal Float m30;
        internal Float m31;
        internal Float m32;
        internal Float m33;

        public static bool operator >(SquareMatrix4 m1, SquareMatrix4 m2)
        {
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j) {
                    if (m1[i][j] > m2[i][j])
                        return true;
                    if (m1[i][j] < m2[i][j])
                        return false;
                }
            return false;
        }

        public static SquareMatrix4 operator *(Float s, SquareMatrix4 m)
        {
            return m * s;
        }

        public static SquareMatrix4 operator *(SquareMatrix4 m1, SquareMatrix4 m2)
        {
            SquareMatrix4 r;
            r.m00 = InnerProduct(m1.m00, m2.m00, m1.m01, m2.m10, m1.m02, m2.m20, m1.m03, m2.m30);
            r.m01 = InnerProduct(m1.m00, m2.m01, m1.m01, m2.m11, m1.m02, m2.m21, m1.m03, m2.m31);
            r.m02 = InnerProduct(m1.m00, m2.m02, m1.m01, m2.m12, m1.m02, m2.m22, m1.m03, m2.m32);
            r.m03 = InnerProduct(m1.m00, m2.m03, m1.m01, m2.m13, m1.m02, m2.m23, m1.m03, m2.m33);
            r.m10 = InnerProduct(m1.m10, m2.m00, m1.m11, m2.m10, m1.m12, m2.m20, m1.m13, m2.m30);
            r.m11 = InnerProduct(m1.m10, m2.m01, m1.m11, m2.m11, m1.m12, m2.m21, m1.m13, m2.m31);
            r.m12 = InnerProduct(m1.m10, m2.m02, m1.m11, m2.m12, m1.m12, m2.m22, m1.m13, m2.m32);
            r.m13 = InnerProduct(m1.m10, m2.m03, m1.m11, m2.m13, m1.m12, m2.m23, m1.m13, m2.m33);
            r.m20 = InnerProduct(m1.m20, m2.m00, m1.m21, m2.m10, m1.m22, m2.m20, m1.m23, m2.m30);
            r.m21 = InnerProduct(m1.m20, m2.m01, m1.m21, m2.m11, m1.m22, m2.m21, m1.m23, m2.m31);
            r.m22 = InnerProduct(m1.m20, m2.m02, m1.m21, m2.m12, m1.m22, m2.m22, m1.m23, m2.m32);
            r.m23 = InnerProduct(m1.m20, m2.m03, m1.m21, m2.m13, m1.m22, m2.m23, m1.m23, m2.m33);
            r.m30 = InnerProduct(m1.m30, m2.m00, m1.m31, m2.m10, m1.m32, m2.m20, m1.m33, m2.m30);
            r.m31 = InnerProduct(m1.m30, m2.m01, m1.m31, m2.m11, m1.m32, m2.m21, m1.m33, m2.m31);
            r.m32 = InnerProduct(m1.m30, m2.m02, m1.m31, m2.m12, m1.m32, m2.m22, m1.m33, m2.m32);
            r.m33 = InnerProduct(m1.m30, m2.m03, m1.m31, m2.m13, m1.m32, m2.m23, m1.m33, m2.m33);
            return r;
        }
    }
}
