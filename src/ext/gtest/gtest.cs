// Minimal googletest-compatible harness for the C# port (the NuGet feed is offline, so xunit/NUnit
// are not available). Provides TEST registration, EXPECT_*/ASSERT_* checks, EXPECT_DEATH and a
// gtest-style runner with --gtest_filter / --gtest_list_tests.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace testing
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TESTAttribute : Attribute
    {
        public string Suite, Name;
        public TESTAttribute(string suite, string name) { Suite = suite; Name = name; }
    }

    // TEST_P / INSTANTIATE_TEST_CASE_P: the method takes one int parameter; Values lists its arguments.
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TEST_PAttribute : Attribute
    {
        public string Suite, Name; public int[] Values;
        public TEST_PAttribute(string suite, string name, params int[] values) { Suite = suite; Name = name; Values = values; }
    }

    public sealed class AssertFailure : Exception
    {
        public AssertFailure() : base("assertion failed") { }
    }

    public static class Gtest
    {
        [ThreadStatic] private static List<string> failures;
        [ThreadStatic] private static List<string> traces;
        internal static List<string> Failures { get { if (failures == null) failures = new List<string>(); return failures; } }

        private static void Fail(bool fatal, string file, int line, string msg)
        {
            Failures.Add(file + ":" + line + ": Failure\n" + msg);
            if (fatal) throw new AssertFailure();
        }

        private static string Str(object o)
        {
            if (o == null) return "(null)";
            if (o is float f) return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (o is double d) return d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (o is bool b) return b ? "true" : "false";
            return o.ToString();
        }

        private static bool IsNumeric(object o)
        {
            return o is sbyte || o is byte || o is short || o is ushort || o is int || o is uint || o is long || o is ulong ||
                   o is float || o is double || o is char || o is bool || o is Enum;
        }
        private static bool IsFloating(object o) { return o is float || o is double; }

        // Compare with C++ usual-arithmetic-conversion semantics for mixed numeric types.
        private static int Cmp<A, B>(A a, B b, out bool comparable)
        {
            object oa = a, ob = b; comparable = true;
            if (oa == null || ob == null) { comparable = oa == ob; return oa == ob ? 0 : 1; }
            if (IsNumeric(oa) && IsNumeric(ob))
            {
                if (oa is Enum) oa = System.Convert.ToInt64(oa);
                if (ob is Enum) ob = System.Convert.ToInt64(ob);
                if (oa is bool ba) oa = ba ? 1 : 0;
                if (ob is bool bb) ob = bb ? 1 : 0;
                if (IsFloating(oa) || IsFloating(ob))
                {
                    if (oa is float && ob is float) { float x = (float)oa, y = (float)ob; if (float.IsNaN(x) || float.IsNaN(y)) { comparable = false; return 0; } return x.CompareTo(y); }
                    double dx = System.Convert.ToDouble(oa), dy = System.Convert.ToDouble(ob);
                    if (double.IsNaN(dx) || double.IsNaN(dy)) { comparable = false; return 0; }
                    return dx.CompareTo(dy);
                }
                if (oa is ulong ua && !(ob is ulong)) { long lb = System.Convert.ToInt64(ob); return lb < 0 ? 1 : ua.CompareTo((ulong)lb); }
                if (ob is ulong ub && !(oa is ulong)) { long la = System.Convert.ToInt64(oa); return la < 0 ? -1 : ((ulong)la).CompareTo(ub); }
                if (oa is ulong x2 && ob is ulong y2) return x2.CompareTo(y2);
                return System.Convert.ToInt64(oa).CompareTo(System.Convert.ToInt64(ob));
            }
            if (oa is IComparable ca && oa.GetType() == ob.GetType()) return ca.CompareTo(ob);
            comparable = false;
            return 0;
        }

        private static bool Equal<A, B>(A a, B b)
        {
            bool comparable;
            int c = Cmp(a, b, out comparable);
            if (IsNumeric(a) && IsNumeric(b)) return comparable && c == 0;
            if (a == null || b == null) return a == null && b == null;
            if (typeof(A) == typeof(B)) return EqualityComparer<A>.Default.Equals(a, (A)(object)b);
            return a.Equals(b);
        }

        public static void EXPECT_TRUE(bool c, [CallerArgumentExpression("c")] string e = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { if (!c) Fail(false, f, l, "Value of: " + e + "\n  Actual: false\nExpected: true"); }
        public static void EXPECT_FALSE(bool c, [CallerArgumentExpression("c")] string e = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { if (c) Fail(false, f, l, "Value of: " + e + "\n  Actual: true\nExpected: false"); }
        public static void ASSERT_TRUE(bool c, [CallerArgumentExpression("c")] string e = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { if (!c) Fail(true, f, l, "Value of: " + e + "\n  Actual: false\nExpected: true"); }
        public static void ASSERT_FALSE(bool c, [CallerArgumentExpression("c")] string e = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { if (c) Fail(true, f, l, "Value of: " + e + "\n  Actual: true\nExpected: false"); }

        private static void Rel<A, B>(bool fatal, bool ok, string op, A a, B b, string ea, string eb, string f, int l)
        { if (!ok) Fail(fatal, f, l, "Expected: (" + ea + ") " + op + " (" + eb + "), actual: " + Str(a) + " vs " + Str(b)); }

        public static void EXPECT_EQ<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { if (!Equal(a, b)) Fail(false, f, l, "Expected equality of these values:\n  " + ea + "\n    Which is: " + Str(a) + "\n  " + eb + "\n    Which is: " + Str(b)); }
        public static void ASSERT_EQ<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { if (!Equal(a, b)) Fail(true, f, l, "Expected equality of these values:\n  " + ea + "\n    Which is: " + Str(a) + "\n  " + eb + "\n    Which is: " + Str(b)); }
        public static void EXPECT_NE<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { Rel(false, !Equal(a, b), "!=", a, b, ea, eb, f, l); }
        public static void ASSERT_NE<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { Rel(true, !Equal(a, b), "!=", a, b, ea, eb, f, l); }

        public static void EXPECT_LT<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { bool c; int r = Cmp(a, b, out c); Rel(false, c && r < 0, "<", a, b, ea, eb, f, l); }
        public static void EXPECT_LE<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { bool c; int r = Cmp(a, b, out c); Rel(false, c && r <= 0, "<=", a, b, ea, eb, f, l); }
        public static void EXPECT_GT<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { bool c; int r = Cmp(a, b, out c); Rel(false, c && r > 0, ">", a, b, ea, eb, f, l); }
        public static void EXPECT_GE<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { bool c; int r = Cmp(a, b, out c); Rel(false, c && r >= 0, ">=", a, b, ea, eb, f, l); }
        public static void ASSERT_LT<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { bool c; int r = Cmp(a, b, out c); Rel(true, c && r < 0, "<", a, b, ea, eb, f, l); }
        public static void ASSERT_LE<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { bool c; int r = Cmp(a, b, out c); Rel(true, c && r <= 0, "<=", a, b, ea, eb, f, l); }
        public static void ASSERT_GT<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { bool c; int r = Cmp(a, b, out c); Rel(true, c && r > 0, ">", a, b, ea, eb, f, l); }
        public static void ASSERT_GE<A, B>(A a, B b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { bool c; int r = Cmp(a, b, out c); Rel(true, c && r >= 0, ">=", a, b, ea, eb, f, l); }

        // gtest's EXPECT_FLOAT_EQ: equal within 4 ULPs.
        private static bool AlmostEqual(float a, float b)
        {
            if (float.IsNaN(a) || float.IsNaN(b)) return false;
            return UlpDiff(a, b) <= 4;
        }
        private static bool AlmostEqual(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return false;
            return UlpDiff(a, b) <= 4;
        }
        private static uint Biased(float x) { uint u = BitConverter.SingleToUInt32Bits(x); return (u & 0x80000000u) != 0 ? ~u + 1 : u | 0x80000000u; }
        private static ulong Biased(double x) { ulong u = BitConverter.DoubleToUInt64Bits(x); return (u & 0x8000000000000000ul) != 0 ? ~u + 1 : u | 0x8000000000000000ul; }
        private static uint UlpDiff(float a, float b) { uint x = Biased(a), y = Biased(b); return x >= y ? x - y : y - x; }
        private static ulong UlpDiff(double a, double b) { ulong x = Biased(a), y = Biased(b); return x >= y ? x - y : y - x; }

        public static void EXPECT_FLOAT_EQ(float a, float b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { if (!AlmostEqual(a, b)) Fail(false, f, l, "Expected equality of these values:\n  " + ea + "\n    Which is: " + Str(a) + "\n  " + eb + "\n    Which is: " + Str(b)); }
        // C++ implicitly narrows double arguments to the float parameters of EXPECT_FLOAT_EQ.
        public static void EXPECT_FLOAT_EQ(double a, double b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { EXPECT_FLOAT_EQ((float)a, (float)b, ea, eb, f, l); }
        public static void EXPECT_DOUBLE_EQ(double a, double b, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { if (!AlmostEqual(a, b)) Fail(false, f, l, "Expected equality of these values:\n  " + ea + "\n    Which is: " + Str(a) + "\n  " + eb + "\n    Which is: " + Str(b)); }
        public static void EXPECT_NEAR(double a, double b, double tol, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { if (!(Math.Abs(a - b) <= tol)) Fail(false, f, l, "The difference between " + ea + " and " + eb + " is " + Str(Math.Abs(a - b)) + ", which exceeds " + Str(tol) + ", where\n" + ea + " evaluates to " + Str(a) + ",\n" + eb + " evaluates to " + Str(b)); }
        public static void ASSERT_NEAR(double a, double b, double tol, [CallerArgumentExpression("a")] string ea = "", [CallerArgumentExpression("b")] string eb = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        { if (!(Math.Abs(a - b) <= tol)) Fail(true, f, l, "The difference between " + ea + " and " + eb + " is " + Str(Math.Abs(a - b)) + ", which exceeds " + Str(tol)); }

        public static void ADD_FAILURE(string msg = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0) { Fail(false, f, l, msg); }
        public static void FAIL(string msg = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0) { Fail(true, f, l, msg); }

        // EXPECT_DEATH(statement, regex): the statement must raise a fatal error (CHECK failure / ErrorExit)
        // whose message matches the regex. (A C# process cannot "die" per test, so the fatal exception stands in.)
        public static void EXPECT_DEATH(Action statement, string regex, [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        {
            try { statement(); }
            catch (pbrt.FatalException ex)
            {
                if (!Regex.IsMatch(ex.Message, regex, RegexOptions.Singleline))
                    Fail(false, f, l, "Death test: message did not match \"" + regex + "\"\n  Actual msg: " + ex.Message);
                return;
            }
            catch (Exception ex)
            {
                Fail(false, f, l, "Death test: unexpected exception " + ex.GetType().Name + ": " + ex.Message);
                return;
            }
            Fail(false, f, l, "Death test: statement did not die");
        }

        // ---------------------------------------------------------------------------------------
        // Runner
        private sealed class Case { public string Suite, Name, Full; public MethodInfo M; public object[] Args; }

        private static List<Case> Collect(Assembly asm)
        {
            List<Case> cases = new List<Case>();
            foreach (Type t in asm.GetTypes())
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    foreach (TESTAttribute a in m.GetCustomAttributes<TESTAttribute>())
                        cases.Add(new Case { Suite = a.Suite, Name = a.Name, Full = a.Suite + "." + a.Name, M = m, Args = new object[0] });
                    foreach (TEST_PAttribute a in m.GetCustomAttributes<TEST_PAttribute>())
                        foreach (int v in a.Values)
                            cases.Add(new Case { Suite = a.Suite, Name = a.Name + "/" + v, Full = a.Suite + "." + a.Name + "/" + v, M = m, Args = new object[] { v } });
                }
            return cases;
        }

        private static bool FilterMatch(string full, string filter)
        {
            if (filter == null) return true;
            string[] parts = filter.Split('-');
            string pos = parts[0].Length == 0 ? "*" : parts[0];
            string neg = parts.Length > 1 ? parts[1] : "";
            Func<string, bool> any = (string list) =>
            {
                foreach (string p in list.Split(':'))
                {
                    if (p.Length == 0) continue;
                    string rx = "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    if (Regex.IsMatch(full, rx)) return true;
                }
                return false;
            };
            return any(pos) && !any(neg);
        }

        public static int RunAllTests(Assembly asm, string filter, bool list)
        {
            List<Case> cases = Collect(asm);
            cases.Sort((Case x, Case y) => { int c = string.CompareOrdinal(x.Suite, y.Suite); return c; });  // stable by suite below
            List<Case> sel = new List<Case>();
            foreach (Case c in cases) if (FilterMatch(c.Full, filter)) sel.Add(c);
            if (list)
            {
                string last = null;
                foreach (Case c in sel) { if (c.Suite != last) { Console.WriteLine(c.Suite + "."); last = c.Suite; } Console.WriteLine("  " + c.Name); }
                return 0;
            }
            HashSet<string> suites = new HashSet<string>(); foreach (Case c in sel) suites.Add(c.Suite);
            Console.WriteLine("[==========] Running " + sel.Count + " tests from " + suites.Count + " test suites.");
            List<string> failed = new List<string>();
            Stopwatch total = Stopwatch.StartNew();
            foreach (Case c in sel)
            {
                Console.WriteLine("[ RUN      ] " + c.Full);
                Failures.Clear();
                Stopwatch sw = Stopwatch.StartNew();
                try { c.M.Invoke(null, c.Args); }
                catch (TargetInvocationException tie)
                {
                    if (!(tie.InnerException is AssertFailure))
                        Failures.Add("Unhandled exception: " + tie.InnerException.GetType().Name + ": " + tie.InnerException.Message + "\n" + tie.InnerException.StackTrace);
                }
                if (Failures.Count > 0)
                {
                    foreach (string s in Failures) Console.WriteLine(s);
                    Console.WriteLine("[  FAILED  ] " + c.Full + " (" + sw.ElapsedMilliseconds + " ms)");
                    failed.Add(c.Full);
                }
                else
                    Console.WriteLine("[       OK ] " + c.Full + " (" + sw.ElapsedMilliseconds + " ms)");
            }
            Console.WriteLine("[==========] " + sel.Count + " tests from " + suites.Count + " test suites ran. (" + total.ElapsedMilliseconds + " ms total)");
            Console.WriteLine("[  PASSED  ] " + (sel.Count - failed.Count) + " tests.");
            if (failed.Count > 0)
            {
                Console.WriteLine("[  FAILED  ] " + failed.Count + " tests, listed below:");
                foreach (string s in failed) Console.WriteLine("[  FAILED  ] " + s);
            }
            return failed.Count == 0 ? 0 : 1;
        }
    }
}
