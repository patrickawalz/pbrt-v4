// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/check.h

using System;
using System.Numerics;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // CHECK Macro Definitions
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CHECK(bool x, [CallerArgumentExpression("x")] string expr = "",
                                 [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (!x) LogFatal(LogLevel.Fatal, file, line, "Check failed: " + expr);
        }

        public static void CHECK_EQ(int a, int b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { if (!(a == b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} == {b}"); }
        public static void CHECK_NE(int a, int b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { if (!(a != b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} != {b}"); }
        public static void CHECK_EQ(long a, long b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { if (!(a == b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} == {b}"); }
        public static void CHECK_NE(long a, long b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { if (!(a != b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} != {b}"); }
        public static void CHECK_EQ(ulong a, ulong b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { if (!(a == b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} == {b}"); }
        public static void CHECK_EQ(float a, float b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { if (!(a == b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} == {b}"); }
        public static void CHECK_NE(float a, float b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { if (!(a != b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} != {b}"); }
        public static void CHECK_NE(Vector3f a, Vector3f b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { if (!(a != b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} != {b}"); }
        public static void CHECK_EQ(Point2i a, Point2i b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { if (!(a == b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} == {b}"); }
        public static void CHECK_NE(Normal3f a, Normal3f b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { if (!(a != b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} != {b}"); }
        // Catch-all for any type exposing ==/!= through IEqualityOperators.
        public static void CHECK_EQ<T>(T a, T b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            where T : IEqualityOperators<T, T, bool>
        { if (!(a == b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} == {b}"); }
        public static void CHECK_NE<T>(T a, T b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            where T : IEqualityOperators<T, T, bool>
        { if (!(a != b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} != {b}"); }
        public static void CHECK_GT<T>(T a, T b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            where T : IComparable<T>
        { if (!(a.CompareTo(b) > 0)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} > {b}"); }
        public static void CHECK_GE<T>(T a, T b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            where T : IComparable<T>
        { if (!(a.CompareTo(b) >= 0)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} >= {b}"); }
        public static void CHECK_LT<T>(T a, T b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            where T : IComparable<T>
        { if (!(a.CompareTo(b) < 0)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} < {b}"); }
        public static void CHECK_LE<T>(T a, T b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            where T : IComparable<T>
        { if (!(a.CompareTo(b) <= 0)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} <= {b}"); }

        // DCHECK variants compile away unless PBRT_DEBUG_BUILD is defined.
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK(bool x, [CallerArgumentExpression("x")] string expr = "",
                                  [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        { CHECK(x, expr, file, line); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_EQ(int a, int b) { CHECK_EQ(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_NE(int a, int b) { CHECK_NE(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_EQ(ulong a, ulong b) { CHECK_EQ(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_NE(float a, float b) { CHECK_NE(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_NE(Vector3f a, Vector3f b) { CHECK_NE(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_EQ<T>(T a, T b) where T : IEqualityOperators<T, T, bool> { CHECK_EQ(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_NE<T>(T a, T b) where T : IEqualityOperators<T, T, bool> { CHECK_NE(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_GT<T>(T a, T b) where T : IComparable<T> { CHECK_GT(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_GE<T>(T a, T b) where T : IComparable<T> { CHECK_GE(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_LT<T>(T a, T b) where T : IComparable<T> { CHECK_LT(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_LE<T>(T a, T b) where T : IComparable<T> { CHECK_LE(a, b); }

        // CHECK_RARE / DCHECK_RARE: statistics-based; the check itself is a no-op here.
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void CHECK_RARE(double freq, bool condition) { }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_RARE(double freq, bool condition) { }

        public static void PrintStackTrace()
        {
            Console.Error.WriteLine(Environment.StackTrace);
        }
    }
}
