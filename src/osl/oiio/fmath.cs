// OSL port: OpenImageIO/fmath.h (the subset the OSL runtime library calls)
//
// Ported from OIIO 3.1's fmath.h with the same constants and operation order. Notes on bit-identity with the C++:
//  * madd/msub/nmadd/nmsub are written as a separate multiply and add. GCC contracts them to an FMA only when the target has one (-mfma/AVX2); a
//    default x86-64 / SSE build does not, which is what this matches. Define OSL_USE_FMA to use MathF.FusedMultiplyAdd instead.
//  * fast_rint uses round-to-nearest-even (std::rint), as an OIIO_SIMD_SSE >= 4 build does. An SSE2-only build rounds ties away from zero; the two
//    differ only for arguments that are exactly k+0.5.
//  * The non-fast (libm) functions map to MathF, which on Linux calls the C library's *f functions, but on other platforms may differ in the last bit.

using System;
using System.Runtime.CompilerServices;

namespace OIIO
{
    public static class fmath
    {
        public const double M_PI = 3.14159265358979323846;
        public const double M_PI_2 = 1.57079632679489661923;
        public const double M_PI_4 = 0.78539816339744830962;
        public const double M_1_PI = 0.31830988618379067154;
        public const double M_LN2 = 0.69314718055994530942;
        public const double M_LN10 = 2.30258509299404568402;

        // std::numeric_limits<float>::min() (smallest normal) and max()
        public const float float_min = 1.17549435e-38f;
        public const float float_max = 3.40282347e+38f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float bitcast_to_float(uint bits)
        {
            return BitConverter.UInt32BitsToSingle(bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float bitcast_to_float(int bits)
        {
            return BitConverter.Int32BitsToSingle(bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint bitcast_to_uint(float x)
        {
            return BitConverter.SingleToUInt32Bits(x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int bitcast_to_int(float x)
        {
            return BitConverter.SingleToInt32Bits(x);
        }

        // clamp a to bounds [low,high]; clamp(NaN,low,high) returns low
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float clamp(float a, float low, float high)
        {
            float val = a;
            if (!(low <= val))
            {
                val = low;
            }
            if (val > high)
            {
                val = high;
            }
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float madd(float a, float b, float c)
        {
#if OSL_USE_FMA
            return MathF.FusedMultiplyAdd(a, b, c);
#else
            return (float)(a * b) + c;
#endif
        }

        // Linearly interpolate values v0-v1 at x: v0*(1-x) + v1*x.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float lerp(float v0, float v1, float x)
        {
            return (float)(v0 * (float)(1.0f - x)) + (float)(v1 * x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float bilerp(float v0, float v1, float v2, float v3, float s, float t)
        {
            float s1 = 1.0f - s;
            return (float)((float)(1.0f - t) * (float)((float)(v0 * s1) + (float)(v1 * s))) + (float)(t * (float)((float)(v2 * s1) + (float)(v3 * s)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float trilerp(float v0, float v1, float v2, float v3, float v4, float v5, float v6, float v7, float s, float t, float r)
        {
            float s1 = 1.0f - s;
            float t1 = 1.0f - t;
            float r1 = 1.0f - r;
            return (float)(r1 * (float)((float)(t1 * (float)((float)(v0 * s1) + (float)(v1 * s))) + (float)(t * (float)((float)(v2 * s1) + (float)(v3 * s)))))
                 + (float)(r * (float)((float)(t1 * (float)((float)(v4 * s1) + (float)(v5 * s))) + (float)(t * (float)((float)(v6 * s1) + (float)(v7 * s)))));
        }

        // A float to int conversion with the x86 behaviour of cvttss2si, which the C++ gets: NaN and out of range values give INT_MIN (C# saturates)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int float_to_int(float x)
        {
            if (!(x >= -2147483648.0f && x < 2147483648.0f))
            {
                return int.MinValue;
            }
            return (int)x;
        }

        // Return floor(x) cast to an int.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ifloor(float x)
        {
            return float_to_int(MathF.Floor(x));
        }

        // Round to nearest integer, returning as an int (used by the sin/cos/tan range reduction)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int fast_rint(float x)
        {
            return float_to_int(MathF.Round(x, MidpointRounding.ToEven));
        }

        // Safe (clamping) sqrt: safe_sqrt(x<0) returns 0, not NaN.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float safe_sqrt(float x)
        {
            return x >= 0.0f ? MathF.Sqrt(x) : 0.0f;
        }

        // Safe (clamping) inverse sqrt: safe_inversesqrt(x<=0) returns 0.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float safe_inversesqrt(float x)
        {
            return x > 0.0f ? 1.0f / MathF.Sqrt(x) : 0.0f;
        }

        // Safe fmod: guard against b==0.0 (in which case, return 0.0).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float safe_fmod(float a, float b)
        {
            if (b != 0.0f)
            {
                int N = float_to_int(a / b);
                return a - (float)(N * b);
            }
            return 0.0f;
        }

        public static float fast_sin(float x)
        {
            int q = fast_rint((float)(x * (float)M_1_PI));
            float qf = (float)q;
            x = madd(qf, -0.78515625f * 4, x);
            x = madd(qf, -0.00024187564849853515625f * 4, x);
            x = madd(qf, -3.7747668102383613586e-08f * 4, x);
            x = madd(qf, -1.2816720341285448015e-12f * 4, x);
            x = (float)M_PI_2 - ((float)M_PI_2 - x); // crush denormals
            float s = x * x;
            if ((q & 1) != 0)
            {
                x = -x;
            }
            float u = 2.6083159809786593541503e-06f;
            u = madd(u, s, -0.0001981069071916863322258f);
            u = madd(u, s, +0.00833307858556509017944336f);
            u = madd(u, s, -0.166666597127914428710938f);
            u = madd(s, (float)(u * x), x);
            return clamp(u, -1.0f, 1.0f);
        }

        public static float fast_cos(float x)
        {
            int q = fast_rint((float)(x * (float)M_1_PI));
            float qf = (float)q;
            x = madd(qf, -0.78515625f * 4, x);
            x = madd(qf, -0.00024187564849853515625f * 4, x);
            x = madd(qf, -3.7747668102383613586e-08f * 4, x);
            x = madd(qf, -1.2816720341285448015e-12f * 4, x);
            x = (float)M_PI_2 - ((float)M_PI_2 - x); // crush denormals
            float s = x * x;
            float u = -2.71811842367242206819355e-07f;
            u = madd(u, s, +2.47990446951007470488548e-05f);
            u = madd(u, s, -0.00138888787478208541870117f);
            u = madd(u, s, +0.0416666641831398010253906f);
            u = madd(u, s, -0.5f);
            u = madd(u, s, +1.0f);
            if ((q & 1) != 0)
            {
                u = -u;
            }
            return clamp(u, -1.0f, 1.0f);
        }

        /// Fast approximate sin(x * pi), maximum absolute error 0.000918954611
        public static float fast_sinpi(float x)
        {
            // Strip the integral part off, so the domain is [-1,1]
            float z = x - ((x + 25165824.0f) - 25165824.0f);
            float y = z - z * MathF.Abs(z);
            const float Q = 3.10396624f;
            const float P = 3.584135056f;
            return y * (Q + P * MathF.Abs(y));
        }

        /// Fast approximate cos(x * pi)
        public static float fast_cospi(float x)
        {
            return fast_sinpi(x + 0.5f);
        }

        public static unsafe void fast_sincos(float x, float* sine, float* cosine)
        {
            int q = fast_rint((float)(x * (float)M_1_PI));
            float qf = (float)q;
            x = madd(qf, -0.78515625f * 4, x);
            x = madd(qf, -0.00024187564849853515625f * 4, x);
            x = madd(qf, -3.7747668102383613586e-08f * 4, x);
            x = madd(qf, -1.2816720341285448015e-12f * 4, x);
            x = (float)M_PI_2 - ((float)M_PI_2 - x); // crush denormals
            float s = x * x;
            if ((q & 1) != 0)
            {
                x = -x;
            }
            float su = 2.6083159809786593541503e-06f;
            su = madd(su, s, -0.0001981069071916863322258f);
            su = madd(su, s, +0.00833307858556509017944336f);
            su = madd(su, s, -0.166666597127914428710938f);
            su = madd(s, (float)(su * x), x);
            float cu = -2.71811842367242206819355e-07f;
            cu = madd(cu, s, +2.47990446951007470488548e-05f);
            cu = madd(cu, s, -0.00138888787478208541870117f);
            cu = madd(cu, s, +0.0416666641831398010253906f);
            cu = madd(cu, s, -0.5f);
            cu = madd(cu, s, +1.0f);
            if ((q & 1) != 0)
            {
                cu = -cu;
            }
            *sine = clamp(su, -1.0f, 1.0f);
            *cosine = clamp(cu, -1.0f, 1.0f);
        }

        // NOTE: this approximation is only valid on [-8192.0,+8192.0]
        public static float fast_tan(float x)
        {
            int q = fast_rint((float)(x * (float)(2 * M_1_PI)));
            float qf = (float)q;
            x = madd(qf, -0.78515625f * 2, x);
            x = madd(qf, -0.00024187564849853515625f * 2, x);
            x = madd(qf, -3.7747668102383613586e-08f * 2, x);
            x = madd(qf, -1.2816720341285448015e-12f * 2, x);
            if ((q & 1) == 0)
            {
                x = (float)M_PI_4 - ((float)M_PI_4 - x); // crush denormals (only if we aren't inverting the result later)
            }
            float s = x * x;
            float u = 0.00927245803177356719970703f;
            u = madd(u, s, 0.00331984995864331722259521f);
            u = madd(u, s, 0.0242998078465461730957031f);
            u = madd(u, s, 0.0534495301544666290283203f);
            u = madd(u, s, 0.133383005857467651367188f);
            u = madd(u, s, 0.333331853151321411132812f);
            u = madd(s, (float)(u * x), x);
            if ((q & 1) != 0)
            {
                u = -1.0f / u;
            }
            return u;
        }

        public static float fast_acos(float x)
        {
            float f = MathF.Abs(x);
            float m = (f < 1.0f) ? 1.0f - (1.0f - f) : 1.0f; // clamp and crush denormals
            float a = (float)(MathF.Sqrt(1.0f - m) * (1.5707963267f + (float)(m * (-0.213300989f + (float)(m * (0.077980478f + (float)(m * -0.02164095f)))))));
            return x < 0 ? (float)M_PI - a : a;
        }

        public static float fast_asin(float x)
        {
            float f = MathF.Abs(x);
            float m = (f < 1.0f) ? 1.0f - (1.0f - f) : 1.0f; // clamp and crush denormals
            float a = (float)M_PI_2 - (float)(MathF.Sqrt(1.0f - m) * (1.5707963267f + (float)(m * (-0.213300989f + (float)(m * (0.077980478f + (float)(m * -0.02164095f)))))));
            return MathF.CopySign(a, x);
        }

        public static float fast_atan(float x)
        {
            float a = MathF.Abs(x);
            float k = a > 1.0f ? 1 / a : a;
            float s = 1.0f - (1.0f - k); // crush denormals
            float t = s * s;
            float r = (float)(s * madd(0.430165678f, t, 1.0f)) / madd(madd(0.0579354987f, t, 0.763007998f), t, 1.0f);
            if (a > 1.0f)
            {
                r = 1.570796326794896557998982f - r;
            }
            return MathF.CopySign(r, x);
        }

        public static float fast_atan2(float y, float x)
        {
            float a = MathF.Abs(x);
            float b = MathF.Abs(y);
            bool b_is_greater_than_a = b > a;
            float k = (b == 0) ? 0.0f : ((a == b) ? 1.0f : (b > a ? a / b : b / a));
            float s = 1.0f - (1.0f - k); // crush denormals
            float t = s * s;
            float r = (float)(s * madd(0.430165678f, t, 1.0f)) / madd(madd(0.0579354987f, t, 0.763007998f), t, 1.0f);
            if (b_is_greater_than_a)
            {
                r = 1.570796326794896557998982f - r; // account for arg reduction
            }
            if ((bitcast_to_uint(x) & 0x80000000u) != 0) // test sign bit of x
            {
                r = (float)M_PI - r;
            }
            return MathF.CopySign(r, y);
        }

        public static float fast_log2(float xval)
        {
            // NOTE: clamp to avoid special cases and make result "safe" from large negative values/nans
            float x = clamp(xval, float_min, float_max);
            uint bits = bitcast_to_uint(x);
            int exponent = (int)(bits >> 23) - 127;
            float f = bitcast_to_float((bits & 0x007FFFFFu) | 0x3f800000u) - 1.0f;
            float f2 = f * f;
            float f4 = f2 * f2;
            float hi = madd(f, -0.00931049621349f, 0.05206469089414f);
            float lo = madd(f, 0.47868480909345f, -0.72116591947498f);
            hi = madd(f, hi, -0.13753123777116f);
            hi = madd(f, hi, 0.24187369696082f);
            hi = madd(f, hi, -0.34730547155299f);
            lo = madd(f, lo, 1.442689881667200f);
            return ((float)(f4 * hi) + (float)(f * lo)) + (float)exponent;
        }

        public static float fast_log(float x)
        {
            return fast_log2(x) * (float)M_LN2;
        }

        public static float fast_log10(float x)
        {
            return fast_log2(x) * (float)(M_LN2 / M_LN10);
        }

        public static float fast_logb(float x)
        {
            // don't bother with denormals
            x = MathF.Abs(x);
            if (x < float_min)
            {
                x = float_min;
            }
            if (x > float_max)
            {
                x = float_max;
            }
            uint bits = bitcast_to_uint(x);
            return (float)((int)(bits >> 23) - 127);
        }

        public static float fast_exp2(float xval)
        {
            // clamp to safe range for final addition
            float x = clamp(xval, -126.0f, 126.0f);
            // range reduction
            int m = (int)x;
            x -= m;
            x = 1.0f - (1.0f - x); // crush denormals (does not affect max ulps!)
            float r = 1.33336498402e-3f;
            r = madd(x, r, 9.810352697968e-3f);
            r = madd(x, r, 5.551834031939e-2f);
            r = madd(x, r, 0.2401793301105f);
            r = madd(x, r, 0.693144857883f);
            r = madd(x, r, 1.0f);
            // multiply by 2 ^ m by adding in the exponent
            return bitcast_to_float(bitcast_to_uint(r) + ((uint)m << 23));
        }

        public static float fast_exp(float x)
        {
            return fast_exp2(x * (float)(1 / M_LN2));
        }

        public static float fast_expm1(float x)
        {
            if (MathF.Abs(x) < 0.03f)
            {
                float y = 1.0f - (1.0f - x); // crush denormals
                return MathF.CopySign(madd(0.5f, (float)(y * y), y), x);
            }
            else
            {
                return fast_exp(x) - 1.0f;
            }
        }

        public static float fast_sinh(float x)
        {
            float a = MathF.Abs(x);
            if (a > 1.0f)
            {
                float e = fast_exp(a);
                return MathF.CopySign((float)(0.5f * e) - (float)(0.5f / e), x);
            }
            else
            {
                a = 1.0f - (1.0f - a); // crush denorms
                float a2 = a * a;
                float r = 2.03945513931e-4f;
                r = madd(r, a2, 8.32990277558e-3f);
                r = madd(r, a2, 0.1666673421859f);
                r = madd((float)(r * a), a2, a);
                return MathF.CopySign(r, x);
            }
        }

        public static float fast_cosh(float x)
        {
            float e = fast_exp(MathF.Abs(x));
            return (float)(0.5f * e) + (float)(0.5f / e);
        }

        public static float fast_tanh(float x)
        {
            float e = fast_exp(2.0f * MathF.Abs(x));
            return MathF.CopySign(1 - 2 / (1 + e), x);
        }

        public static float fast_safe_pow(float x, float y)
        {
            if (y == 0)
            {
                return 1.0f; // x^0=1
            }
            if (x == 0)
            {
                return 0.0f; // 0^y=0
            }
            // be cheap & exact for special case of squaring and identity
            if (y == 1.0f)
            {
                return x;
            }
            if (y == 2.0f)
            {
                return MathF.Min(x * x, float_max);
            }
            float sign = 1.0f;
            if (x < 0.0f)
            {
                // if x is negative, only deal with integer powers
                int ybits = bitcast_to_int(y) & 0x7fffffff;
                if (ybits >= 0x4b800000)
                {
                    // always even int, keep positive
                }
                else if (ybits >= 0x3f800000)
                {
                    // bigger than 1, check
                    int k = (ybits >> 23) - 127; // get exponent
                    int j = ybits >> (23 - k); // shift out possible fractional bits
                    if ((j << (23 - k)) == ybits) // rebuild number and check for a match
                    {
                        sign = bitcast_to_float(0x3f800000 | (j << 31)); // +1 for even, -1 for odd
                    }
                    else
                    {
                        return 0.0f; // not integer
                    }
                }
                else
                {
                    return 0.0f; // not integer
                }
            }
            return sign * fast_exp2(y * fast_log2(MathF.Abs(x)));
        }

        // Fast cube root
        public static float fast_cbrt(float x)
        {
            float x0 = MathF.Abs(x);
            // from hacker's delight
            float a = bitcast_to_float(0x2a5137a0 + bitcast_to_int(x0) / 3); // Initial guess.
            a = 0.333333333f * ((float)(2.0f * a) + x0 / (float)(a * a)); // Newton step.
            a = 0.333333333f * ((float)(2.0f * a) + x0 / (float)(a * a)); // Newton step again.
            a = (x0 == 0) ? 0 : a; // Fix 0 case
            return MathF.CopySign(a, x);
        }

        public static float fast_erf(float x)
        {
            // Abramowitz and Stegun, 7.1.28
            const float a1 = 0.0705230784f;
            const float a2 = 0.0422820123f;
            const float a3 = 0.0092705272f;
            const float a4 = 0.0001520143f;
            const float a5 = 0.0002765672f;
            const float a6 = 0.0000430638f;
            float a = MathF.Abs(x);
            float b = 1.0f - (1.0f - a); // crush denormals
            float r = madd(madd(madd(madd(madd(madd(a6, b, a5), b, a4), b, a3), b, a2), b, a1), b, 1.0f);
            float s = r * r; // ^2
            float t = s * s; // ^4
            float u = t * t; // ^8
            float v = u * u; // ^16
            return MathF.CopySign(1.0f - 1.0f / v, x);
        }

        public static float fast_ierf(float x)
        {
            // from: Approximating the erfinv function by Mike Giles; to avoid trouble at the limit, clamp input to 1-eps
            float a = MathF.Abs(x);
            if (a > 0.99999994f)
            {
                a = 0.99999994f;
            }
            float w = -fast_log((1.0f - a) * (1.0f + a));
            float p;
            if (w < 5.0f)
            {
                w = w - 2.5f;
                p = 2.81022636e-08f;
                p = madd(p, w, 3.43273939e-07f);
                p = madd(p, w, -3.5233877e-06f);
                p = madd(p, w, -4.39150654e-06f);
                p = madd(p, w, 0.00021858087f);
                p = madd(p, w, -0.00125372503f);
                p = madd(p, w, -0.00417768164f);
                p = madd(p, w, 0.246640727f);
                p = madd(p, w, 1.50140941f);
            }
            else
            {
                w = MathF.Sqrt(w) - 3.0f;
                p = -0.000200214257f;
                p = madd(p, w, 0.000100950558f);
                p = madd(p, w, 0.00134934322f);
                p = madd(p, w, -0.00367342844f);
                p = madd(p, w, 0.00573950773f);
                p = madd(p, w, -0.0076224613f);
                p = madd(p, w, 0.00943887047f);
                p = madd(p, w, 1.00167406f);
                p = madd(p, w, 2.83297682f);
            }
            return p * x;
        }

        public static float fast_erfc(float x)
        {
            return 1.0f - fast_erf(x);
        }

        // Safe (clamping) arcsine: clamp to the valid domain.
        public static float safe_asin(float x)
        {
            if (x <= -1.0f)
            {
                return (float)(-M_PI_2);
            }
            if (x >= 1.0f)
            {
                return (float)(+M_PI_2);
            }
            return MathF.Asin(x);
        }

        // Safe (clamping) arccosine: clamp to the valid domain.
        public static float safe_acos(float x)
        {
            if (x <= -1.0f)
            {
                return (float)M_PI;
            }
            if (x >= 1.0f)
            {
                return 0.0f;
            }
            return MathF.Acos(x);
        }

        public static float safe_log2(float x)
        {
            if (x < float_min)
            {
                x = float_min;
            }
            if (x > float_max)
            {
                x = float_max;
            }
            return MathF.Log2(x);
        }

        public static float safe_log(float x)
        {
            if (x < float_min)
            {
                x = float_min;
            }
            if (x > float_max)
            {
                x = float_max;
            }
            return MathF.Log(x);
        }

        public static float safe_log10(float x)
        {
            if (x < float_min)
            {
                x = float_min;
            }
            if (x > float_max)
            {
                x = float_max;
            }
            return MathF.Log10(x);
        }

        public static float safe_pow(float x, float y)
        {
            if (y == 0.0f)
            {
                return 1.0f;
            }
            if (x == 0.0f)
            {
                return 0.0f;
            }
            // if x is negative, only deal with integer powers
            if ((x < 0.0f) && (y != MathF.Floor(y)))
            {
                return 0.0f;
            }
            float r = MathF.Pow(x, y);
            // Clamp to avoid returning Inf.
            return clamp(r, -float_max, float_max);
        }
    }
}
