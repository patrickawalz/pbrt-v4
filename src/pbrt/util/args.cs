// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/args.h, util/args.cpp
//
// C++ pointer outputs (int *, float *, ...) become ref parameters; the std::vector<std::string>
// iterator becomes an index (ref int iter) into a List<string> with an end index.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace pbrt
{

    public static unsafe partial class pbrt
    {
        // Downcase the string and remove any '-' or '_' characters; thus we can be
        // a little flexible in what we match for argument names.
        public static string normalizeArg(string str)
        {
            System.Text.StringBuilder ret = new System.Text.StringBuilder();
            foreach (char c in str)
            {
                if (c != '_' && c != '-')
                    ret.Append(char.ToLowerInvariant(c));
            }
            return ret.ToString();
        }

        public static bool initArg(string str, ref int ptr)
        {
            if (str.Length == 0 || (!char.IsAsciiDigit(str[0]) && str[0] != '-'))
                return false;
            int v;
            // std::stoi accepts a numeric prefix; mimic by parsing the longest leading integer
            int n = 0;
            if (str[n] == '-') ++n;
            int d0 = n;
            while (n < str.Length && char.IsAsciiDigit(str[n])) ++n;
            if (n == d0)
                return false;
            if (!int.TryParse(str.AsSpan(0, n), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v))
                return false;
            ptr = v;
            return true;
        }

        private static int NumericPrefix(string str)
        {
            // Longest prefix that std::stod/stof would consume
            int n = 0;
            while (n < str.Length && char.IsWhiteSpace(str[n])) ++n;
            int start = n;
            if (n < str.Length && (str[n] == '+' || str[n] == '-')) ++n;
            int digits = 0;
            while (n < str.Length && char.IsAsciiDigit(str[n])) { ++n; ++digits; }
            if (n < str.Length && str[n] == '.')
            {
                ++n;
                while (n < str.Length && char.IsAsciiDigit(str[n])) { ++n; ++digits; }
            }
            if (digits == 0)
                return -1;
            if (n < str.Length && (str[n] == 'e' || str[n] == 'E'))
            {
                int m = n + 1;
                if (m < str.Length && (str[m] == '+' || str[m] == '-')) ++m;
                int ed = 0;
                while (m < str.Length && char.IsAsciiDigit(str[m])) { ++m; ++ed; }
                if (ed > 0) n = m;
            }
            return n;
        }

        public static bool initArg(string str, ref float ptr)
        {
            if (str.Length == 0)
                return false;
            int n = NumericPrefix(str);
            if (n < 0)
                return false;
            float v;
            if (!float.TryParse(str.AsSpan(0, n), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ||
                float.IsInfinity(v))
                return false;
            ptr = v;
            return true;
        }

        public static bool initArg(string str, ref double ptr)
        {
            if (str.Length == 0)
                return false;
            int n = NumericPrefix(str);
            if (n < 0)
                return false;
            double v;
            if (!double.TryParse(str.AsSpan(0, n), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ||
                double.IsInfinity(v))
                return false;
            ptr = v;
            return true;
        }

        public static bool initArg(string str, pstd.span<float> o)
        {
            List<Float> v = SplitStringToFloats(str, ',');
            if ((size_t)v.Count != o.size())
                return false;
            for (int i = 0; i < v.Count; ++i)
                o[i] = v[i];
            return true;
        }

        public static bool initArg(string str, pstd.span<double> o)
        {
            List<double> v = SplitStringToDoubles(str, ',');
            if ((size_t)v.Count != o.size())
                return false;
            for (int i = 0; i < v.Count; ++i)
                o[i] = v[i];
            return true;
        }

        public static bool initArg(string str, pstd.span<int> o)
        {
            List<int> v = SplitStringToInts(str, ',');
            if ((size_t)v.Count != o.size())
                return false;
            for (int i = 0; i < v.Count; ++i)
                o[i] = v[i];
            return true;
        }

        // (char ** overload: C# strings are managed, so it is the same as the std::string one)
        public static bool initArg(string str, ref string ptr)
        {
            if (str.Length == 0)
                return false;
            ptr = str;
            return true;
        }

        public static bool initArg(string str, ref bool ptr)
        {
            if (normalizeArg(str) == "false")
            {
                ptr = false;
                return true;
            }
            else if (normalizeArg(str) == "true")
            {
                ptr = true;
                return true;
            }
            return false;
        }

        public static bool initArg(string str, ref pstd.optional<int> ptr)
        {
            int value = default;
            if (initArg(str, ref value)) { ptr = value; return true; }
            return false;
        }
        public static bool initArg(string str, ref pstd.optional<float> ptr)
        {
            float value = default;
            if (initArg(str, ref value)) { ptr = value; return true; }
            return false;
        }
        public static bool initArg(string str, ref pstd.optional<double> ptr)
        {
            double value = default;
            if (initArg(str, ref value)) { ptr = value; return true; }
            return false;
        }
        public static bool initArg(string str, ref pstd.optional<string> ptr)
        {
            string value = default;
            if (initArg(str, ref value)) { ptr = value; return true; }
            return false;
        }
        public static bool initArg(string str, ref pstd.optional<bool> ptr)
        {
            bool value = default;
            if (initArg(str, ref value)) { ptr = value; return true; }
            return false;
        }

        public static bool matchPrefix(string str, string prefix)
        {
            if (prefix.Length > str.Length)
                return false;
            for (int i = 0; i < prefix.Length; ++i)
                if (prefix[i] != str[i])
                    return false;
            return true;
        }

        // Shared body of ParseArg. init(str) plays the role of initArg(str, out); isBool is true
        // when enable(out) would succeed (only bool pointers).
        private static bool ParseArgImpl(List<string> args, ref int iter, int end, string name,
                                         Func<string, bool> init, Action enable, Action<string> onError)
        {
            string arg = args[iter];

            // Strip either one or two leading dashes.
            if (arg[1] == '-')
                arg = arg.Substring(2);
            else
                arg = arg.Substring(1);

            if (matchPrefix(normalizeArg(arg), normalizeArg(name + '=')))
            {
                // --arg=value
                string value = arg.Substring(name.Length + 1);
                if (!init(value))
                {
                    onError(StringPrintf("invalid value \"%s\" for --%s argument", value, name));
                    return false;
                }
                return true;
            }
            else if (normalizeArg(arg) == normalizeArg(name))
            {
                // --arg <value>, except for bool arguments, which are set to true
                // without expecting another argument.
                if (enable != null)
                {
                    enable();
                    return true;
                }

                ++iter;
                if (iter == end)
                {
                    onError(StringPrintf("missing value after --%s argument", arg));
                    return false;
                }
                if (!init(args[iter]))
                {
                    onError(StringPrintf("invalid value \"%s\" for --%s argument", args[iter], name));
                    return false;
                }
                return true;
            }
            else
                return false;
        }

        // ParseArg overloads, one per output type of the C++ template
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, ref int o, Action<string> onError)
        {
            int tmp = o;
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, ref tmp), null, onError);
            o = tmp;
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, ref float o, Action<string> onError)
        {
            float tmp = o;
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, ref tmp), null, onError);
            o = tmp;
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, ref double o, Action<string> onError)
        {
            double tmp = o;
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, ref tmp), null, onError);
            o = tmp;
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, ref string o, Action<string> onError)
        {
            string tmp = o;
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, ref tmp), null, onError);
            o = tmp;
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, ref bool o, Action<string> onError)
        {
            bool tmp = o;
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, ref tmp), () => { tmp = true; }, onError);
            o = tmp;
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, ref pstd.optional<int> o, Action<string> onError)
        {
            pstd.optional<int> tmp = o;
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, ref tmp), null, onError);
            o = tmp;
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, ref pstd.optional<float> o, Action<string> onError)
        {
            pstd.optional<float> tmp = o;
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, ref tmp), null, onError);
            o = tmp;
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, ref pstd.optional<double> o, Action<string> onError)
        {
            pstd.optional<double> tmp = o;
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, ref tmp), null, onError);
            o = tmp;
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, ref pstd.optional<string> o, Action<string> onError)
        {
            pstd.optional<string> tmp = o;
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, ref tmp), null, onError);
            o = tmp;
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, ref pstd.optional<bool> o, Action<string> onError)
        {
            pstd.optional<bool> tmp = o;
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, ref tmp), null, onError);
            o = tmp;
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, pstd.span<float> o, Action<string> onError)
        {
            // (spans are ref structs and cannot be captured, so parse into a temporary and copy out)
            float[] tmp = new float[o.size()];
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, tmp), null, onError);
            if (r)
                for (int i = 0; i < tmp.Length; ++i)
                    o[i] = tmp[i];
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, pstd.span<double> o, Action<string> onError)
        {
            // (spans are ref structs and cannot be captured, so parse into a temporary and copy out)
            double[] tmp = new double[o.size()];
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, tmp), null, onError);
            if (r)
                for (int i = 0; i < tmp.Length; ++i)
                    o[i] = tmp[i];
            return r;
        }
        public static bool ParseArg(List<string> args, ref int iter, int end, string name, pstd.span<int> o, Action<string> onError)
        {
            // (spans are ref structs and cannot be captured, so parse into a temporary and copy out)
            int[] tmp = new int[o.size()];
            bool r = ParseArgImpl(args, ref iter, end, name, (string s) => initArg(s, tmp), null, onError);
            if (r)
                for (int i = 0; i < tmp.Length; ++i)
                    o[i] = tmp[i];
            return r;
        }

        // Command-line Argument Utility Functions
        // (argv excludes the executable name already in .NET's Main(string[] args))
        public static List<string> GetCommandLineArguments(string[] argv)
        {
            List<string> argStrings = new List<string>();
            foreach (string a in argv)
                argStrings.Add(a);
            return argStrings;
        }
    }

}  // namespace pbrt
