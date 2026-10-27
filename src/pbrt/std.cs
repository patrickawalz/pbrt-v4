// std.cs -- C++ <cmath>/<algorithm> emulation for the Magi port.
// pbrt:   std::sqrt(x)      Magi: std.sqrt(x)
// Every function has float and double overloads (plus int/long where C++ has them),
// so call sites are line-for-line identical to pbrt-v4 regardless of the Float alias.

using System;
using System.Runtime.CompilerServices;

public static partial class std
{
    private const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;

    // ---- abs / min / max ------------------------------------------------------
    [MethodImpl(AI)] public static float  abs(float x)  => MathF.Abs(x);
    [MethodImpl(AI)] public static double abs(double x) => Math.Abs(x);
    [MethodImpl(AI)] public static int    abs(int x)    => Math.Abs(x);
    [MethodImpl(AI)] public static long   abs(long x)   => Math.Abs(x);

    // C++ std::min/max return the first argument when equal/unordered: min(a,b) = (b < a) ? b : a
    [MethodImpl(AI)] public static float  min(float a, float b)   => (b < a) ? b : a;
    [MethodImpl(AI)] public static double min(double a, double b) => (b < a) ? b : a;
    [MethodImpl(AI)] public static int    min(int a, int b)       => (b < a) ? b : a;
    [MethodImpl(AI)] public static uint   min(uint a, uint b)     => (b < a) ? b : a;
    [MethodImpl(AI)] public static long   min(long a, long b)     => (b < a) ? b : a;
    [MethodImpl(AI)] public static ulong  min(ulong a, ulong b)   => (b < a) ? b : a;
    [MethodImpl(AI)] public static float  max(float a, float b)   => (a < b) ? b : a;
    [MethodImpl(AI)] public static double max(double a, double b) => (a < b) ? b : a;
    [MethodImpl(AI)] public static int    max(int a, int b)       => (a < b) ? b : a;
    [MethodImpl(AI)] public static uint   max(uint a, uint b)     => (a < b) ? b : a;
    [MethodImpl(AI)] public static long   max(long a, long b)     => (a < b) ? b : a;
    [MethodImpl(AI)] public static ulong  max(ulong a, ulong b)   => (a < b) ? b : a;

    [MethodImpl(AI)] public static T min<T>(T a, T b) where T : IComparable<T> => (b.CompareTo(a) < 0) ? b : a;
    [MethodImpl(AI)] public static T max<T>(T a, T b) where T : IComparable<T> => (a.CompareTo(b) < 0) ? b : a;

    // fmin/fmax: NaN-ignoring
    [MethodImpl(AI)] public static float  fmin(float a, float b)   => float.IsNaN(a) ? b : float.IsNaN(b) ? a : (b < a ? b : a);
    [MethodImpl(AI)] public static double fmin(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : (b < a ? b : a);
    [MethodImpl(AI)] public static float  fmax(float a, float b)   => float.IsNaN(a) ? b : float.IsNaN(b) ? a : (a < b ? b : a);
    [MethodImpl(AI)] public static double fmax(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : (a < b ? b : a);

    [MethodImpl(AI)] public static float  clamp(float v, float lo, float hi)    => (v < lo) ? lo : (hi < v) ? hi : v;
    [MethodImpl(AI)] public static double clamp(double v, double lo, double hi) => (v < lo) ? lo : (hi < v) ? hi : v;
    [MethodImpl(AI)] public static int    clamp(int v, int lo, int hi)          => (v < lo) ? lo : (hi < v) ? hi : v;

    [MethodImpl(AI)] public static void swap(ref float a, ref float b)   { float t = a; a = b; b = t; }
    [MethodImpl(AI)] public static void swap(ref double a, ref double b) { double t = a; a = b; b = t; }
    [MethodImpl(AI)] public static void swap(ref int a, ref int b)       { int t = a; a = b; b = t; }
    [MethodImpl(AI)] public static void swap(ref uint a, ref uint b)     { uint t = a; a = b; b = t; }
    [MethodImpl(AI)] public static void swap<T>(ref T a, ref T b)        { T t = a; a = b; b = t; }

    // ---- roots / powers -------------------------------------------------------
    [MethodImpl(AI)] public static float  sqrt(float x)  => MathF.Sqrt(x);
    [MethodImpl(AI)] public static double sqrt(double x) => Math.Sqrt(x);
    [MethodImpl(AI)] public static float  cbrt(float x)  => MathF.Cbrt(x);
    [MethodImpl(AI)] public static double cbrt(double x) => Math.Cbrt(x);
    [MethodImpl(AI)] public static float  pow(float x, float y)   => MathF.Pow(x, y);
    [MethodImpl(AI)] public static double pow(double x, double y) => Math.Pow(x, y);

    public static float hypot(float x, float y)
    {
        x = MathF.Abs(x); y = MathF.Abs(y);
        if (float.IsInfinity(x) || float.IsInfinity(y)) return float.PositiveInfinity;
        if (x < y) { float t = x; x = y; y = t; }
        if (x == 0) return 0;
        float r = y / x;
        return x * MathF.Sqrt(1 + r * r);
    }
    public static double hypot(double x, double y)
    {
        x = Math.Abs(x); y = Math.Abs(y);
        if (double.IsInfinity(x) || double.IsInfinity(y)) return double.PositiveInfinity;
        if (x < y) { double t = x; x = y; y = t; }
        if (x == 0) return 0;
        double r = y / x;
        return x * Math.Sqrt(1 + r * r);
    }

    [MethodImpl(AI)] public static float  fma(float a, float b, float c)    => MathF.FusedMultiplyAdd(a, b, c);
    [MethodImpl(AI)] public static double fma(double a, double b, double c) => Math.FusedMultiplyAdd(a, b, c);

    // ---- exp / log ------------------------------------------------------------
    [MethodImpl(AI)] public static float  exp(float x)    => MathF.Exp(x);
    [MethodImpl(AI)] public static double exp(double x)   => Math.Exp(x);
    [MethodImpl(AI)] public static float  exp2(float x)   => MathF.Pow(2f, x);
    [MethodImpl(AI)] public static double exp2(double x)  => Math.Pow(2.0, x);
    [MethodImpl(AI)] public static float  expm1(float x)  => (MathF.Abs(x) < 1e-4f) ? x + 0.5f * x * x + x * x * x * (1f / 6f) : MathF.Exp(x) - 1f;
    [MethodImpl(AI)] public static double expm1(double x) => (Math.Abs(x) < 1e-5) ? x + 0.5 * x * x + x * x * x * (1.0 / 6.0) : Math.Exp(x) - 1.0;
    [MethodImpl(AI)] public static float  log(float x)    => MathF.Log(x);
    [MethodImpl(AI)] public static double log(double x)   => Math.Log(x);
    [MethodImpl(AI)] public static float  log2(float x)   => MathF.Log2(x);
    [MethodImpl(AI)] public static double log2(double x)  => Math.Log2(x);
    [MethodImpl(AI)] public static float  log10(float x)  => MathF.Log10(x);
    [MethodImpl(AI)] public static double log10(double x) => Math.Log10(x);
    [MethodImpl(AI)] public static float  log1p(float x)  => (MathF.Abs(x) < 1e-4f) ? x - 0.5f * x * x + x * x * x * (1f / 3f) : MathF.Log(1f + x);
    [MethodImpl(AI)] public static double log1p(double x) => (Math.Abs(x) < 1e-5) ? x - 0.5 * x * x + x * x * x * (1.0 / 3.0) : Math.Log(1.0 + x);
    [MethodImpl(AI)] public static int    ilogb(float x)  => MathF.ILogB(x);
    [MethodImpl(AI)] public static int    ilogb(double x) => Math.ILogB(x);
    [MethodImpl(AI)] public static float  ldexp(float x, int e)   => MathF.ScaleB(x, e);
    [MethodImpl(AI)] public static double ldexp(double x, int e)  => Math.ScaleB(x, e);
    [MethodImpl(AI)] public static float  scalbn(float x, int e)  => MathF.ScaleB(x, e);
    [MethodImpl(AI)] public static double scalbn(double x, int e) => Math.ScaleB(x, e);

    // ---- trig -----------------------------------------------------------------
    [MethodImpl(AI)] public static float  sin(float x)    => MathF.Sin(x);
    [MethodImpl(AI)] public static double sin(double x)   => Math.Sin(x);
    [MethodImpl(AI)] public static float  cos(float x)    => MathF.Cos(x);
    [MethodImpl(AI)] public static double cos(double x)   => Math.Cos(x);
    [MethodImpl(AI)] public static float  tan(float x)    => MathF.Tan(x);
    [MethodImpl(AI)] public static double tan(double x)   => Math.Tan(x);
    [MethodImpl(AI)] public static float  asin(float x)   => MathF.Asin(x);
    [MethodImpl(AI)] public static double asin(double x)  => Math.Asin(x);
    [MethodImpl(AI)] public static float  acos(float x)   => MathF.Acos(x);
    [MethodImpl(AI)] public static double acos(double x)  => Math.Acos(x);
    [MethodImpl(AI)] public static float  atan(float x)   => MathF.Atan(x);
    [MethodImpl(AI)] public static double atan(double x)  => Math.Atan(x);
    [MethodImpl(AI)] public static float  atan2(float y, float x)    => MathF.Atan2(y, x);
    [MethodImpl(AI)] public static double atan2(double y, double x)  => Math.Atan2(y, x);
    [MethodImpl(AI)] public static float  sinh(float x)   => MathF.Sinh(x);
    [MethodImpl(AI)] public static double sinh(double x)  => Math.Sinh(x);
    [MethodImpl(AI)] public static float  cosh(float x)   => MathF.Cosh(x);
    [MethodImpl(AI)] public static double cosh(double x)  => Math.Cosh(x);
    [MethodImpl(AI)] public static float  tanh(float x)   => MathF.Tanh(x);
    [MethodImpl(AI)] public static double tanh(double x)  => Math.Tanh(x);
    [MethodImpl(AI)] public static float  asinh(float x)  => MathF.Asinh(x);
    [MethodImpl(AI)] public static double asinh(double x) => Math.Asinh(x);
    [MethodImpl(AI)] public static float  acosh(float x)  => MathF.Acosh(x);
    [MethodImpl(AI)] public static double acosh(double x) => Math.Acosh(x);
    [MethodImpl(AI)] public static float  atanh(float x)  => MathF.Atanh(x);
    [MethodImpl(AI)] public static double atanh(double x) => Math.Atanh(x);

    // ---- rounding -------------------------------------------------------------
    [MethodImpl(AI)] public static float  floor(float x)   => MathF.Floor(x);
    [MethodImpl(AI)] public static double floor(double x)  => Math.Floor(x);
    [MethodImpl(AI)] public static float  ceil(float x)    => MathF.Ceiling(x);
    [MethodImpl(AI)] public static double ceil(double x)   => Math.Ceiling(x);
    [MethodImpl(AI)] public static float  trunc(float x)   => MathF.Truncate(x);
    [MethodImpl(AI)] public static double trunc(double x)  => Math.Truncate(x);
    // C++ round(): half away from zero
    [MethodImpl(AI)] public static float  round(float x)   => MathF.Round(x, MidpointRounding.AwayFromZero);
    [MethodImpl(AI)] public static double round(double x)  => Math.Round(x, MidpointRounding.AwayFromZero);
    [MethodImpl(AI)] public static float  rint(float x)    => MathF.Round(x, MidpointRounding.ToEven);
    [MethodImpl(AI)] public static double rint(double x)   => Math.Round(x, MidpointRounding.ToEven);
    [MethodImpl(AI)] public static float  nearbyint(float x)  => MathF.Round(x, MidpointRounding.ToEven);
    [MethodImpl(AI)] public static double nearbyint(double x) => Math.Round(x, MidpointRounding.ToEven);
    [MethodImpl(AI)] public static long   lround(float x)  => (long)MathF.Round(x, MidpointRounding.AwayFromZero);
    [MethodImpl(AI)] public static long   lround(double x) => (long)Math.Round(x, MidpointRounding.AwayFromZero);
    [MethodImpl(AI)] public static long   lrint(float x)   => (long)MathF.Round(x, MidpointRounding.ToEven);
    [MethodImpl(AI)] public static long   lrint(double x)  => (long)Math.Round(x, MidpointRounding.ToEven);

    // fmod: result has the sign of x (same as C# %)
    [MethodImpl(AI)] public static float  fmod(float x, float y)    => x % y;
    [MethodImpl(AI)] public static double fmod(double x, double y)  => x % y;
    [MethodImpl(AI)] public static float  remainder(float x, float y)   => MathF.IEEERemainder(x, y);
    [MethodImpl(AI)] public static double remainder(double x, double y) => Math.IEEERemainder(x, y);

    public static float modf(float x, out float ip)
    {
        ip = MathF.Truncate(x);
        return float.IsInfinity(x) ? MathF.CopySign(0f, x) : x - ip;
    }
    public static double modf(double x, out double ip)
    {
        ip = Math.Truncate(x);
        return double.IsInfinity(x) ? Math.CopySign(0.0, x) : x - ip;
    }

    // ---- sign / classification -----------------------------------------------
    [MethodImpl(AI)] public static float  copysign(float x, float y)    => MathF.CopySign(x, y);
    [MethodImpl(AI)] public static double copysign(double x, double y)  => Math.CopySign(x, y);
    [MethodImpl(AI)] public static bool signbit(float x)   => float.IsNegative(x);
    [MethodImpl(AI)] public static bool signbit(double x)  => double.IsNegative(x);
    [MethodImpl(AI)] public static bool isnan(float x)     => float.IsNaN(x);
    [MethodImpl(AI)] public static bool isnan(double x)    => double.IsNaN(x);
    [MethodImpl(AI)] public static bool isinf(float x)     => float.IsInfinity(x);
    [MethodImpl(AI)] public static bool isinf(double x)    => double.IsInfinity(x);
    [MethodImpl(AI)] public static bool isfinite(float x)  => float.IsFinite(x);
    [MethodImpl(AI)] public static bool isfinite(double x) => double.IsFinite(x);

    // ---- nextafter ------------------------------------------------------------
    [MethodImpl(AI)] public static float nextafter(float x, float y)
    {
        if (float.IsNaN(x) || float.IsNaN(y)) return float.NaN;
        if (x == y) return y;
        return (y > x) ? MathF.BitIncrement(x) : MathF.BitDecrement(x);
    }
    [MethodImpl(AI)] public static double nextafter(double x, double y)
    {
        if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
        if (x == y) return y;
        return (y > x) ? Math.BitIncrement(x) : Math.BitDecrement(x);
    }

    // ---- lgamma: Lanczos approximation (g = 607/128, 15 terms; ~1e-15 relative accuracy) ----
    private static readonly double[] lgammaCoef = {
        0.99999999999999709182, 57.156235665862923517, -59.597960355475491248,
        14.136097974741747174, -0.49191381609762019978, .33994649984811888699e-4,
        .46523628927048575665e-4, -.98374475304879564677e-4, .15808870322491248884e-3,
        -.21026444172410488319e-3, .21743961811521264320e-3, -.16431810653676389022e-3,
        .84418223983852743293e-4, -.26190838401581408670e-4, .36899182659531622704e-5 };
    public static double lgamma(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x <= 0 && Math.Floor(x) == x) return double.PositiveInfinity;
        if (x < 0.5)  // reflection: Gamma(x) Gamma(1-x) = pi / sin(pi x)
            return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - lgamma(1.0 - x);
        double y = x, tmp = x + 5.24218750000000000;
        tmp = (x + 0.5) * Math.Log(tmp) - tmp;
        double ser = 0.999999999999997092;
        for (int j = 1; j < 15; ++j) ser += lgammaCoef[j] / ++y;
        return tmp + Math.Log(2.5066282746310005 * ser / x);
    }
    public static float lgamma(float x) { return (float)lgamma((double)x); }

    // ---- erf / erfc / gamma-free special functions ----------------------------
    // erf via Taylor series for |x| < 3 and a continued fraction for erfc beyond.
    public static double erf(double x)
    {
        if (double.IsNaN(x)) return x;
        double ax = Math.Abs(x);
        if (ax > 6.0) return x > 0 ? 1.0 : -1.0;
        if (ax < 3.0)
        {
            double sum = ax, term = ax, x2 = ax * ax;
            for (int n = 1; n < 100; ++n)
            {
                term *= -x2 / n;
                double add = term / (2 * n + 1);
                sum += add;
                if (Math.Abs(add) < 1e-17 * Math.Abs(sum)) break;
            }
            double r = 1.1283791670955126 * sum;
            return x < 0 ? -r : r;
        }
        double e = erfc_cf(ax);
        return x > 0 ? 1.0 - e : e - 1.0;
    }
    public static float erf(float x) { return (float)erf((double)x); }
    public static double erfc(double x) { return x >= 3.0 ? erfc_cf(x) : 1.0 - erf(x); }
    public static float erfc(float x) { return (float)erfc((double)x); }
    private static double erfc_cf(double x)
    {
        // erfc(x) = exp(-x^2)/sqrt(pi) / (x + (1/2)/(x + (2/2)/(x + (3/2)/(x + ...)))), x >= 3
        double t = x;
        for (int n = 240; n >= 1; --n)
            t = x + (n * 0.5) / t;
        return Math.Exp(-x * x) / (Math.Sqrt(Math.PI) * t);
    }

    // ---- numeric_limits-style constants --------------------------------------
    public static unsafe class numeric_limits_float
    {
        public const float max       = float.MaxValue;
        public const float lowest    = float.MinValue;
        public const float min       = 1.17549435e-38f;   // smallest normal
        public const float denorm_min= float.Epsilon;
        public const float epsilon   = 1.1920929e-07f;    // 2^-23
        public const float infinity  = float.PositiveInfinity;
        public const float quiet_NaN = float.NaN;
    }
    public static unsafe class numeric_limits_double
    {
        public const double max       = double.MaxValue;
        public const double lowest    = double.MinValue;
        public const double min       = 2.2250738585072014e-308;
        public const double denorm_min= double.Epsilon;
        public const double epsilon   = 2.220446049250313e-16;
        public const double infinity  = double.PositiveInfinity;
        public const double quiet_NaN = double.NaN;
    }

}
