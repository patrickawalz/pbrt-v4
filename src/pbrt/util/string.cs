// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // std::stoi/stoll/stof/stod parse a numeric prefix, skipping leading whitespace, and
        // throw if there is none; these helpers do the same.
        private static readonly Regex intPrefix = new Regex(@"^\s*[+-]?\d+", RegexOptions.Compiled);
        private static readonly Regex floatPrefix = new Regex(
            @"^\s*[+-]?(?:(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?|inf(?:inity)?|nan)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool Atoi(string str, int* ptr)
        {
            Match m = intPrefix.Match(str);
            if (!m.Success) return false;
            long v;
            if (!long.TryParse(m.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v)) return false;
            if (v < int.MinValue || v > int.MaxValue) return false;
            *ptr = (int)v;
            return true;
        }

        public static bool Atoi(string str, int64_t* ptr)
        {
            Match m = intPrefix.Match(str);
            if (!m.Success) return false;
            long v;
            if (!long.TryParse(m.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v)) return false;
            *ptr = v;
            return true;
        }

        private static bool ParseDoublePrefix(string str, out double v)
        {
            v = 0;
            Match m = floatPrefix.Match(str);
            if (!m.Success) return false;
            string t = m.Value.Trim();
            string lower = t.ToLowerInvariant();
            bool neg = lower.StartsWith("-");
            string u = lower.TrimStart('+', '-');
            if (u.StartsWith("inf")) { v = neg ? double.NegativeInfinity : double.PositiveInfinity; return true; }
            if (u.StartsWith("nan")) { v = double.NaN; return true; }
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        public static bool Atof(string str, float* ptr)
        {
            double d;
            if (!ParseDoublePrefix(str, out d)) return false;
            // Parse the decimal text directly as a float so the rounding matches std::stof
            Match m = floatPrefix.Match(str);
            float f;
            if (float.TryParse(m.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                *ptr = f;
            else
                *ptr = (float)d;
            return true;
        }

        public static bool Atof(string str, double* ptr)
        {
            double d;
            if (!ParseDoublePrefix(str, out d)) return false;
            *ptr = d;
            return true;
        }

        public static List<string> SplitStringsFromWhitespace(string str)
        {
            List<string> ret = new List<string>();

            int start = 0;
            do
            {
                // skip leading ws
                while (start != str.Length && char.IsWhiteSpace(str[start]))
                    ++start;

                // |start| is at the start of the current word
                int end = start;
                while (end != str.Length && !char.IsWhiteSpace(str[end]))
                    ++end;

                ret.Add(str.Substring(start, end - start));
                start = end;
            } while (start != str.Length);

            return ret;
        }

        public static List<string> SplitString(string str, char ch)
        {
            List<string> strings = new List<string>();

            if (str.Length == 0)
                return strings;

            int begin = 0;
            while (true)
            {
                int end = begin;
                while (end != str.Length && str[end] != ch)
                    ++end;

                strings.Add(str.Substring(begin, end - begin));

                if (end == str.Length)
                    break;

                begin = end + 1;
            }

            return strings;
        }

        public static List<int> SplitStringToInts(string str, char ch)
        {
            List<string> strs = SplitString(str, ch);
            List<int> ints = new List<int>(new int[strs.Count]);

            for (int i = 0; i < strs.Count; ++i)
            {
                int v;
                if (!Atoi(strs[i], &v))
                    return new List<int>();
                ints[i] = v;
            }
            return ints;
        }

        public static List<int64_t> SplitStringToInt64s(string str, char ch)
        {
            List<string> strs = SplitString(str, ch);
            List<int64_t> ints = new List<int64_t>(new int64_t[strs.Count]);

            for (int i = 0; i < strs.Count; ++i)
            {
                int64_t v;
                if (!Atoi(strs[i], &v))
                    return new List<int64_t>();
                ints[i] = v;
            }
            return ints;
        }

        public static List<Float> SplitStringToFloats(string str, char ch)
        {
            List<string> strs = SplitString(str, ch);
            List<Float> floats = new List<Float>(new Float[strs.Count]);

            for (int i = 0; i < strs.Count; ++i)
            {
                Float v;
                if (!Atof(strs[i], &v))
                    return new List<Float>();
                floats[i] = v;
            }
            return floats;
        }

        public static List<double> SplitStringToDoubles(string str, char ch)
        {
            List<string> strs = SplitString(str, ch);
            List<double> doubles = new List<double>(new double[strs.Count]);

            for (int i = 0; i < strs.Count; ++i)
            {
                double v;
                if (!Atof(strs[i], &v))
                    return new List<double>();
                doubles[i] = v;
            }
            return doubles;
        }

        // C# strings are already UTF-16, so the std::u16string <-> UTF-8 conversions operate
        // on the string itself (UTF-8 bytes are held in a Latin-1-style string only where C++
        // used std::string for raw bytes).
        public static string UTF8FromUTF16(string str)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            return Encoding.Latin1.GetString(bytes);
        }

        public static string UTF16FromUTF8(string str)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(str);
            return Encoding.UTF8.GetString(bytes);
        }

        public static string NormalizeUTF8(string str)
        {
            // (strings hold UTF-8 bytes, one per char, like std::string)
            int options = utf8proc.Utf8Proc.UTF8PROC_COMPOSE;

            byte[] data = Encoding.Latin1.GetBytes(str);
            byte* result;
            long length;
            fixed (byte* p = data)
                length = utf8proc.Utf8Proc.utf8proc_map(p, data.Length, &result, options);
            if (length < 0)
                ErrorExit(StringPrintf("Unicode normalization error: %s: \"%s\"", utf8proc.Utf8Proc.utf8proc_errmsg(length), str));

            str = Encoding.Latin1.GetString(result, (int)length);
            utf8proc.Utf8Proc.utf8proc_free(result);
            return str;
        }
    }

    // InternedString Definition
    public unsafe struct InternedString : IEquatable<InternedString>
    {
        // InternedString Public Methods
        public InternedString(string str) { this.str = str; }
        public static implicit operator string(InternedString s) { return s.str; }

        public static bool operator ==(InternedString a, string s) { return a.str == s; }
        public static bool operator !=(InternedString a, string s) { return a.str != s; }
        public static bool operator ==(InternedString a, InternedString b) { return a.str == b.str; }
        public static bool operator !=(InternedString a, InternedString b) { return a.str != b.str; }
        public static bool operator <(InternedString a, string s) { return string.CompareOrdinal(a.str, s) < 0; }
        public static bool operator >(InternedString a, string s) { return string.CompareOrdinal(a.str, s) > 0; }
        public bool Equals(InternedString o) { return str == o.str; }
        public override bool Equals(object o) { return o is InternedString i && i.str == str; }
        public override int GetHashCode() { return str == null ? 0 : str.GetHashCode(); }

        public override string ToString() { return str; }

        private string str;
    }
}
