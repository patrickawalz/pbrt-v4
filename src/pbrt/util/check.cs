// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/check.h

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // Equality via the type's operator== (as the C++ CHECK_EQ/CHECK_NE macros do);
        // types without one fall back to Equals().
        public static class detail
        {
            public static class OperatorEquals<T>
            {
                public static readonly Func<T, T, bool> Equal = Create();
                static Func<T, T, bool> Create()
                {
                    try
                    {
                        ParameterExpression pa = Expression.Parameter(typeof(T));
                        ParameterExpression pb = Expression.Parameter(typeof(T));
                        return Expression.Lambda<Func<T, T, bool>>(Expression.Equal(pa, pb), pa, pb).Compile();
                    }
                    catch (InvalidOperationException)
                    {
                        EqualityComparer<T> c = EqualityComparer<T>.Default;
                        return (a, b) => c.Equals(a, b);
                    }
                }
            }
        }

        // CHECK Macro Definitions
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CHECK(bool x, [CallerArgumentExpression("x")] string expr = "",
                                 [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (!x) LogFatal(LogLevel.Fatal, file, line, "Check failed: " + expr);
        }

        public static void CHECK_EQ<T>(T a, T b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            
        { if (!detail.OperatorEquals<T>.Equal(a, b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} == {b}"); }
        public static void CHECK_NE<T>(T a, T b, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            
        { if (detail.OperatorEquals<T>.Equal(a, b)) LogFatal(LogLevel.Fatal, file, line, $"Check failed: {a} != {b}"); }
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
        public static void DCHECK_EQ<T>(T a, T b) { CHECK_EQ(a, b); }
        [Conditional("PBRT_DEBUG_BUILD")]
        public static void DCHECK_NE<T>(T a, T b) { CHECK_NE(a, b); }
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
