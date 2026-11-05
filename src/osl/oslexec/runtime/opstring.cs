// OSL port: src/liboslexec/opstring.cpp
//
// The string shadeops that need no ShadingContext. Strings are ustring hashes (the 'h' type code); a hash that is not in the ustring table reads as the
// empty string, as ustring_from does for the empty hash in the C++.
//
// Not ported: osl_regex_impl (needs the ShadingContext's regex cache), osl_format and osl_formatfmt (printf style formatting), and the deprecated
// osl_printf / osl_error / osl_warning / osl_fprintf stubs.
//
// Differences from the C++: .NET strings are UTF-16, so strlen, getchar and substr work on the UTF-8 bytes of the text to keep the C++ byte semantics.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OIIO;

namespace OSL
{
    public static unsafe partial class OslRuntime
    {
        private static string string_from_hash(ulong h)
        {
            ustring u;
            if (ustring.from_hash(h, out u))
            {
                return u.@string();
            }
            return "";
        }

        private static ulong hash_of_string(string s)
        {
            return new ustring(s).hash();
        }

        // Only define 2-arg version of concat, sort it out upstream
        public static ulong osl_concat_sss(ulong s_, ulong t_)
        {
            string s = string_from_hash(s_);
            string t = string_from_hash(t_);
            return hash_of_string(s + t);
        }

        public static int osl_strlen_is(ulong s_)
        {
            return Encoding.UTF8.GetByteCount(string_from_hash(s_));
        }

        // (int)ustring::hash(), which is the OIIO string hash (farmhash) of the text; the empty string is 0
        public static int osl_hash_is(ulong s_)
        {
            return (int)Strutil_hash.strhash64(string_from_hash(s_));
        }

        public static int osl_getchar_isi(ulong str_, int index)
        {
            byte[] str = Encoding.UTF8.GetBytes(string_from_hash(str_));
            // the C++ reads a (signed) char; index is compared as unsigned so negative indices give 0
            return (uint)index < (uint)str.Length ? (sbyte)str[index] : 0;
        }

        public static int osl_startswith_iss(ulong s_, ulong substr_)
        {
            byte[] substr = Encoding.UTF8.GetBytes(string_from_hash(substr_));
            int substr_len = substr.Length;
            if (substr_len == 0) // empty substr always matches
            {
                return 1;
            }
            byte[] s = Encoding.UTF8.GetBytes(string_from_hash(s_));
            int s_len = s.Length;
            if (substr_len > s_len) // longer needle than haystack can't match (including empty s)
            {
                return 0;
            }
            for (int i = 0; i < substr_len; ++i)
            {
                if (s[i] != substr[i])
                {
                    return 0;
                }
            }
            return 1;
        }

        public static int osl_endswith_iss(ulong s_, ulong substr_)
        {
            byte[] substr = Encoding.UTF8.GetBytes(string_from_hash(substr_));
            int substr_len = substr.Length;
            if (substr_len == 0) // empty substr always matches
            {
                return 1;
            }
            byte[] s = Encoding.UTF8.GetBytes(string_from_hash(s_));
            int s_len = s.Length;
            if (substr_len > s_len) // longer needle than haystack can't match (including empty s)
            {
                return 0;
            }
            for (int i = 0; i < substr_len; ++i)
            {
                if (s[s_len - substr_len + i] != substr[i])
                {
                    return 0;
                }
            }
            return 1;
        }

        private static bool strutil_isspace(byte c)
        {
            return c == (byte)' ' || (c >= 9 && c <= 13);
        }

        // OIIO::Strutil::stoi(str, nullptr, 10): optional whitespace and sign, then decimal digits; overflow saturates
        private static int strutil_stoi(byte[] str)
        {
            int p = 0;
            while (p < str.Length && strutil_isspace(str[p]))
            {
                p++;
            }
            int begin = p;
            bool neg = p < str.Length && str[p] == (byte)'-';
            if (p < str.Length && (str[p] == (byte)'-' || str[p] == (byte)'+'))
            {
                p++;
            }
            long acc = 0;
            bool overflow = false;
            bool anydigits = false;
            long maxval = neg ? -(long)int.MinValue : int.MaxValue;
            for (; p < str.Length; p++)
            {
                int c = str[p];
                if (c >= (byte)'0' && c <= (byte)'9')
                {
                    c -= (byte)'0';
                }
                else
                {
                    break; // done (other bases are not used, so a letter is never a digit)
                }
                anydigits = true;
                if (!overflow)
                {
                    acc = acc * 10 + c;
                }
                if (acc > maxval)
                {
                    overflow = true;
                }
            }
            if (!anydigits)
            {
                return 0;
            }
            if (overflow)
            {
                acc = neg ? int.MinValue : int.MaxValue;
            }
            else if (neg)
            {
                acc = -acc;
            }
            return (int)acc;
        }

        public static int osl_stoi_is(ulong str_)
        {
            return strutil_stoi(Encoding.UTF8.GetBytes(string_from_hash(str_)));
        }

        private static bool is_decimal_digit(byte c)
        {
            return c >= (byte)'0' && c <= (byte)'9';
        }

        private static bool matches_ci(byte[] s, int p, string word)
        {
            if (p + word.Length > s.Length)
            {
                return false;
            }
            for (int i = 0; i < word.Length; ++i)
            {
                byte c = s[p + i];
                if (c >= (byte)'A' && c <= (byte)'Z')
                {
                    c = (byte)(c + 32);
                }
                if (c != (byte)word[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static int hex_digit_value(byte c)
        {
            if (c >= (byte)'0' && c <= (byte)'9')
            {
                return c - (byte)'0';
            }
            if (c >= (byte)'a' && c <= (byte)'f')
            {
                return c - (byte)'a' + 10;
            }
            if (c >= (byte)'A' && c <= (byte)'F')
            {
                return c - (byte)'A' + 10;
            }
            return -1;
        }

        // Strutil::stof, which is C strtof in the "C" locale: leading whitespace, a sign, then a decimal or hexadecimal number, "inf"/"infinity" or
        // "nan"; the longest valid prefix is converted (correctly rounded) and anything else gives 0.
        private static float strutil_stof(byte[] s)
        {
            int p = 0;
            while (p < s.Length && strutil_isspace(s[p]))
            {
                p++;
            }
            bool neg = false;
            if (p < s.Length && (s[p] == (byte)'-' || s[p] == (byte)'+'))
            {
                neg = s[p] == (byte)'-';
                p++;
            }
            if (matches_ci(s, p, "infinity") || matches_ci(s, p, "inf"))
            {
                return neg ? float.NegativeInfinity : float.PositiveInfinity;
            }
            if (matches_ci(s, p, "nan"))
            {
                return float.NaN;
            }
            if (p + 1 < s.Length && s[p] == (byte)'0' && (s[p + 1] == (byte)'x' || s[p + 1] == (byte)'X'))
            {
                // hexadecimal floating point: 0x h[.h][p[+-]d]
                int q = p + 2;
                double mant = 0.0;
                int exp2 = 0;
                bool any = false;
                while (q < s.Length && hex_digit_value(s[q]) >= 0)
                {
                    mant = mant * 16.0 + hex_digit_value(s[q]);
                    any = true;
                    q++;
                }
                if (q < s.Length && s[q] == (byte)'.')
                {
                    q++;
                    while (q < s.Length && hex_digit_value(s[q]) >= 0)
                    {
                        mant = mant * 16.0 + hex_digit_value(s[q]);
                        exp2 -= 4;
                        any = true;
                        q++;
                    }
                }
                if (any)
                {
                    if (q < s.Length && (s[q] == (byte)'p' || s[q] == (byte)'P'))
                    {
                        int r = q + 1;
                        bool eneg = false;
                        if (r < s.Length && (s[r] == (byte)'-' || s[r] == (byte)'+'))
                        {
                            eneg = s[r] == (byte)'-';
                            r++;
                        }
                        if (r < s.Length && is_decimal_digit(s[r]))
                        {
                            int e = 0;
                            while (r < s.Length && is_decimal_digit(s[r]))
                            {
                                if (e < 100000)
                                {
                                    e = e * 10 + (s[r] - (byte)'0');
                                }
                                r++;
                            }
                            exp2 += eneg ? -e : e;
                        }
                    }
                    float hv = (float)(mant * Math.Pow(2.0, exp2));
                    return neg ? -hv : hv;
                }
                // just the "0" before the x
                return neg ? -0.0f : 0.0f;
            }
            int start = p;
            int digits = 0;
            while (p < s.Length && is_decimal_digit(s[p]))
            {
                p++;
                digits++;
            }
            if (p < s.Length && s[p] == (byte)'.')
            {
                p++;
                while (p < s.Length && is_decimal_digit(s[p]))
                {
                    p++;
                    digits++;
                }
            }
            if (digits == 0)
            {
                return 0.0f; // invalid
            }
            if (p < s.Length && (s[p] == (byte)'e' || s[p] == (byte)'E'))
            {
                int r = p + 1;
                if (r < s.Length && (s[r] == (byte)'-' || s[r] == (byte)'+'))
                {
                    r++;
                }
                if (r < s.Length && is_decimal_digit(s[r]))
                {
                    while (r < s.Length && is_decimal_digit(s[r]))
                    {
                        r++;
                    }
                    p = r;
                }
            }
            string text = Encoding.ASCII.GetString(s, start, p - start);
            float v = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            return neg ? -v : v;
        }

        public static float osl_stof_fs(ulong str_)
        {
            return strutil_stof(Encoding.UTF8.GetBytes(string_from_hash(str_)));
        }

        public static ulong osl_substr_ssii(ulong s_, int start, int length)
        {
            byte[] s = Encoding.UTF8.GetBytes(string_from_hash(s_));
            int slen = s.Length;
            if (slen == 0)
            {
                return hash_of_string(""); // No substring of empty string
            }
            int b = start;
            if (b < 0)
            {
                b += slen;
            }
            b = Math.Clamp(b, 0, slen);
            int len = Math.Clamp(length, 0, slen);
            // ustring(s, b, len): at most len characters starting at b
            if (len > slen - b)
            {
                len = slen - b;
            }
            return hash_of_string(Encoding.UTF8.GetString(s, b, len));
        }

        // Strutil::splitsv: split on the separator (whitespace if it is empty), at most maxsplit pieces
        private static List<string> strutil_split(string str, string sep, int maxsplit)
        {
            List<string> result = new List<string>();
            byte[] s = Encoding.UTF8.GetBytes(str);
            byte[] sp = Encoding.UTF8.GetBytes(sep);
            if (s.Length == 0)
            {
                return result; // No source string --> no pieces
            }
            if (maxsplit < 0)
            {
                maxsplit = int.MaxValue;
            }
            int len = s.Length;
            if (sp.Length == 0)
            {
                // split_whitespace
                int wi = 0;
                int wj = 0;
                while (wi < len)
                {
                    while (wi < len && strutil_isspace(s[wi]))
                    {
                        wi++;
                    }
                    wj = wi;
                    while (wi < len && !strutil_isspace(s[wi]))
                    {
                        wi++;
                    }
                    if (wj < wi)
                    {
                        if (--maxsplit <= 0)
                        {
                            break;
                        }
                        result.Add(Encoding.UTF8.GetString(s, wj, wi - wj));
                        while (wi < len && strutil_isspace(s[wi]))
                        {
                            wi++;
                        }
                        wj = wi;
                    }
                }
                if (wj < len)
                {
                    result.Add(Encoding.UTF8.GetString(s, wj, len - wj));
                }
                return result;
            }
            int i = 0;
            int j = 0;
            int n = sp.Length;
            while (i + n <= len)
            {
                bool match = true;
                for (int k = 0; k < n; ++k)
                {
                    if (s[i + k] != sp[k])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    if (--maxsplit <= 0)
                    {
                        break;
                    }
                    result.Add(Encoding.UTF8.GetString(s, j, i - j));
                    i = j = i + n;
                }
                else
                {
                    i++;
                }
            }
            result.Add(Encoding.UTF8.GetString(s, j, len - j));
            return result;
        }

        public static int osl_split(ulong str_, ulong* results, ulong sep_, int maxsplit, int resultslen)
        {
            string str = string_from_hash(str_);
            string sep = string_from_hash(sep_);
            maxsplit = Math.Clamp(maxsplit, 0, resultslen);
            List<string> splits = strutil_split(str, sep, maxsplit);
            int n = Math.Min(maxsplit, splits.Count);
            for (int i = 0; i < n; ++i)
            {
                results[i] = hash_of_string(splits[i]);
            }
            return n;
        }
    }
}
