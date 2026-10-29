// std.cs -- C++ <cmath>/<algorithm> emulation for the Magi port.
// pbrt:   std::sqrt(x)      Magi: std.sqrt(x)
// Every function has float and double overloads (plus int/long where C++ has them),
// so call sites are line-for-line identical to pbrt-v4 regardless of the Float alias.

using System;
using System.Runtime.CompilerServices;

public static partial class std
{

    // ---- abs / min / max ------------------------------------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  abs(float x)  => MathF.Abs(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double abs(double x) => Math.Abs(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int    abs(int x)    => Math.Abs(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long   abs(long x)   => Math.Abs(x);

    // C++ std::min/max return the first argument when equal/unordered: min(a,b) = (b < a) ? b : a
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  min(float a, float b)   => (b < a) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double min(double a, double b) => (b < a) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int    min(int a, int b)       => (b < a) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static uint   min(uint a, uint b)     => (b < a) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long   min(long a, long b)     => (b < a) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static ulong  min(ulong a, ulong b)   => (b < a) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  max(float a, float b)   => (a < b) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double max(double a, double b) => (a < b) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int    max(int a, int b)       => (a < b) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static uint   max(uint a, uint b)     => (a < b) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long   max(long a, long b)     => (a < b) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static ulong  max(ulong a, ulong b)   => (a < b) ? b : a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static T min<T>(T a, T b) where T : IComparable<T> => (b.CompareTo(a) < 0) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static T max<T>(T a, T b) where T : IComparable<T> => (a.CompareTo(b) < 0) ? b : a;

    // fmin/fmax: NaN-ignoring
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  fmin(float a, float b)   => float.IsNaN(a) ? b : float.IsNaN(b) ? a : (b < a ? b : a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double fmin(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : (b < a ? b : a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  fmax(float a, float b)   => float.IsNaN(a) ? b : float.IsNaN(b) ? a : (a < b ? b : a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double fmax(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : (a < b ? b : a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  clamp(float v, float lo, float hi)    => (v < lo) ? lo : (hi < v) ? hi : v;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double clamp(double v, double lo, double hi) => (v < lo) ? lo : (hi < v) ? hi : v;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int    clamp(int v, int lo, int hi)          => (v < lo) ? lo : (hi < v) ? hi : v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void swap(ref float a, ref float b)   { float t = a; a = b; b = t; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void swap(ref double a, ref double b) { double t = a; a = b; b = t; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void swap(ref int a, ref int b)       { int t = a; a = b; b = t; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void swap(ref uint a, ref uint b)     { uint t = a; a = b; b = t; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void swap<T>(ref T a, ref T b)        { T t = a; a = b; b = t; }

    // ---- roots / powers -------------------------------------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  sqrt(float x)  => MathF.Sqrt(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double sqrt(double x) => Math.Sqrt(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  cbrt(float x)  => MathF.Cbrt(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double cbrt(double x) => Math.Cbrt(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  pow(float x, float y)   => MathF.Pow(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double pow(double x, double y) => Math.Pow(x, y);

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  fma(float a, float b, float c)    => MathF.FusedMultiplyAdd(a, b, c);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double fma(double a, double b, double c) => Math.FusedMultiplyAdd(a, b, c);

    // ---- exp / log ------------------------------------------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  exp(float x)    => MathF.Exp(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double exp(double x)   => Math.Exp(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  exp2(float x)   => MathF.Pow(2f, x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double exp2(double x)  => Math.Pow(2.0, x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  expm1(float x)  => (MathF.Abs(x) < 1e-4f) ? x + 0.5f * x * x + x * x * x * (1f / 6f) : MathF.Exp(x) - 1f;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double expm1(double x) => (Math.Abs(x) < 1e-5) ? x + 0.5 * x * x + x * x * x * (1.0 / 6.0) : Math.Exp(x) - 1.0;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  log(float x)    => MathF.Log(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double log(double x)   => Math.Log(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  log2(float x)   => MathF.Log2(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double log2(double x)  => Math.Log2(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  log10(float x)  => MathF.Log10(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double log10(double x) => Math.Log10(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  log1p(float x)  => (MathF.Abs(x) < 1e-4f) ? x - 0.5f * x * x + x * x * x * (1f / 3f) : MathF.Log(1f + x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double log1p(double x) => (Math.Abs(x) < 1e-5) ? x - 0.5 * x * x + x * x * x * (1.0 / 3.0) : Math.Log(1.0 + x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int    ilogb(float x)  => MathF.ILogB(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int    ilogb(double x) => Math.ILogB(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  ldexp(float x, int e)   => MathF.ScaleB(x, e);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double ldexp(double x, int e)  => Math.ScaleB(x, e);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  scalbn(float x, int e)  => MathF.ScaleB(x, e);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double scalbn(double x, int e) => Math.ScaleB(x, e);

    // ---- trig -----------------------------------------------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  sin(float x)    => MathF.Sin(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double sin(double x)   => Math.Sin(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  cos(float x)    => MathF.Cos(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double cos(double x)   => Math.Cos(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  tan(float x)    => MathF.Tan(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double tan(double x)   => Math.Tan(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  asin(float x)   => MathF.Asin(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double asin(double x)  => Math.Asin(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  acos(float x)   => MathF.Acos(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double acos(double x)  => Math.Acos(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  atan(float x)   => MathF.Atan(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double atan(double x)  => Math.Atan(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  atan2(float y, float x)    => MathF.Atan2(y, x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double atan2(double y, double x)  => Math.Atan2(y, x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  sinh(float x)   => MathF.Sinh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double sinh(double x)  => Math.Sinh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  cosh(float x)   => MathF.Cosh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double cosh(double x)  => Math.Cosh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  tanh(float x)   => MathF.Tanh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double tanh(double x)  => Math.Tanh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  asinh(float x)  => MathF.Asinh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double asinh(double x) => Math.Asinh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  acosh(float x)  => MathF.Acosh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double acosh(double x) => Math.Acosh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  atanh(float x)  => MathF.Atanh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double atanh(double x) => Math.Atanh(x);

    // ---- rounding -------------------------------------------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  floor(float x)   => MathF.Floor(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double floor(double x)  => Math.Floor(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  ceil(float x)    => MathF.Ceiling(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double ceil(double x)   => Math.Ceiling(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  trunc(float x)   => MathF.Truncate(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double trunc(double x)  => Math.Truncate(x);
    // C++ round(): half away from zero
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  round(float x)   => MathF.Round(x, MidpointRounding.AwayFromZero);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double round(double x)  => Math.Round(x, MidpointRounding.AwayFromZero);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  rint(float x)    => MathF.Round(x, MidpointRounding.ToEven);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double rint(double x)   => Math.Round(x, MidpointRounding.ToEven);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  nearbyint(float x)  => MathF.Round(x, MidpointRounding.ToEven);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double nearbyint(double x) => Math.Round(x, MidpointRounding.ToEven);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long   lround(float x)  => (long)MathF.Round(x, MidpointRounding.AwayFromZero);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long   lround(double x) => (long)Math.Round(x, MidpointRounding.AwayFromZero);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long   lrint(float x)   => (long)MathF.Round(x, MidpointRounding.ToEven);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long   lrint(double x)  => (long)Math.Round(x, MidpointRounding.ToEven);

    // fmod: result has the sign of x (same as C# %)
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  fmod(float x, float y)    => x % y;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double fmod(double x, double y)  => x % y;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  remainder(float x, float y)   => MathF.IEEERemainder(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double remainder(double x, double y) => Math.IEEERemainder(x, y);

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float  copysign(float x, float y)    => MathF.CopySign(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double copysign(double x, double y)  => Math.CopySign(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool signbit(float x)   => float.IsNegative(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool signbit(double x)  => double.IsNegative(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool isnan(float x)     => float.IsNaN(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool isnan(double x)    => double.IsNaN(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool isinf(float x)     => float.IsInfinity(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool isinf(double x)    => double.IsInfinity(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool isfinite(float x)  => float.IsFinite(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool isfinite(double x) => double.IsFinite(x);

    // ---- nextafter ------------------------------------------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float nextafter(float x, float y)
    {
        if (float.IsNaN(x) || float.IsNaN(y)) return float.NaN;
        if (x == y) return y;
        return (y > x) ? MathF.BitIncrement(x) : MathF.BitDecrement(x);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double nextafter(double x, double y)
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


        public static int partition<T>(Span<T> arr, Func<T, bool> pred)
        {
            int first = 0, last = arr.Length;
            while (true)
            {
                while (true)
                {
                    if (first == last) return first;
                    else if (pred(arr[first])) ++first;
                    else break;
                }
                --last;
                while (true)
                {
                    if (first == last) return first;
                    else if (!pred(arr[last])) --last;
                    else break;
                }
                swap(ref arr[first], ref arr[last]);
                ++first;
            }
        }

        public static int partition<T>(T[] arr, int first, int last, Func<T, bool> pred)
        {
            return first + partition(new Span<T>(arr, first, last - first), pred);
        }

        public static int partition<T>(pstd.span<T> s, Func<T, bool> pred)
        {
            return partition(s.AsSpan(), pred);
        }

        // std::nth_element, following libstdc++'s introselect on an array range [first, last).
        // (If the depth limit is exhausted, the range is fully sorted instead of heap-selected.)
        public static void nth_element<T>(Span<T> a, int nth, Func<T, T, bool> comp)
        {
            int first = 0, last = a.Length;
            if (first == last || nth == last)
                return;
            int depthLimit = (31 - System.Numerics.BitOperations.LeadingZeroCount((uint)(last - first))) * 2;
            while (last - first > 3)
            {
                if (depthLimit == 0)
                {
                    a.Slice(first, last - first).Sort((x, y) => comp(x, y) ? -1 : (comp(y, x) ? 1 : 0));
                    return;
                }
                --depthLimit;
                // __unguarded_partition_pivot
                int mid = first + (last - first) / 2;
                // __move_median_to_first(first, first + 1, mid, last - 1)
                int A = first + 1, B = mid, C = last - 1;
                if (comp(a[A], a[B]))
                {
                    if (comp(a[B], a[C])) swap(ref a[first], ref a[B]);
                    else if (comp(a[A], a[C])) swap(ref a[first], ref a[C]);
                    else swap(ref a[first], ref a[A]);
                }
                else if (comp(a[A], a[C])) swap(ref a[first], ref a[A]);
                else if (comp(a[B], a[C])) swap(ref a[first], ref a[C]);
                else swap(ref a[first], ref a[B]);
                // __unguarded_partition(first + 1, last, first)
                int lo = first + 1, hi = last;
                int cut;
                while (true)
                {
                    while (comp(a[lo], a[first])) ++lo;
                    --hi;
                    while (comp(a[first], a[hi])) --hi;
                    if (!(lo < hi)) { cut = lo; break; }
                    swap(ref a[lo], ref a[hi]);
                    ++lo;
                }
                if (cut <= nth) first = cut; else last = cut;
            }
            // __insertion_sort
            for (int i = first + 1; i < last; ++i)
            {
                T val = a[i];
                int j = i;
                while (j > first && comp(val, a[j - 1]))
                {
                    a[j] = a[j - 1];
                    --j;
                }
                a[j] = val;
            }
        }

        public static void nth_element<T>(T[] a, int first, int nth, int last, Func<T, T, bool> comp)
        {
            nth_element(new Span<T>(a, first, last - first), nth - first, comp);
        }

        public static void nth_element<T>(pstd.span<T> s, int nth, Func<T, T, bool> comp)
        {
            nth_element(s.AsSpan(), nth, comp);
        }
}
