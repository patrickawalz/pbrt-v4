// OSL port: the parts of OpenImageIO strutil.h, filesystem.h, timer.h, sysutil.h and the fmt library that the shading system uses
//
//  * string_view: a window on a string, as OIIO::string_view, for the Strutil::parse_* functions that consume their input.
//  * Strutil: parse_char / parse_prefix / parse_until / parse_int / parse_float / parse_word / parse_identifier / parse_string ...
//  * Fmt: the replacement-field formatter of {fmt} ("{}", "{:.9g}", "{:>6}", "{:08.3f}" ...), which OSL's messages and decode_message use.
//  * Timer, Filesystem (search paths, text files), Sysutil.

using System;
using OSL;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace OIIO
{
    /// A window on a string (the part from pos, len characters), that the parse functions consume from the front.
    public struct string_view
    {
        private string m_s;
        private int m_pos;
        private int m_len;

        public string_view(string s)
        {
            m_s = s == null ? "" : s;
            m_pos = 0;
            m_len = m_s.Length;
        }

        public string_view(string s, int pos, int len)
        {
            m_s = s == null ? "" : s;
            m_pos = pos;
            m_len = len;
        }

        public static implicit operator string_view(string s) { return new string_view(s); }
        public static implicit operator string(string_view v) { return v.ToString(); }

        public int size() { return m_len; }
        public bool empty() { return m_len == 0; }
        public char this[int i] { get { return m_s[m_pos + i]; } }
        public char front() { return m_s[m_pos]; }
        public char back() { return m_s[m_pos + m_len - 1]; }

        /// Offset of the first character of the window within the string it looks at
        public int data_offset() { return m_pos; }

        public void remove_prefix(int n)
        {
            m_pos += n;
            m_len -= n;
        }

        public void remove_suffix(int n)
        {
            m_len -= n;
        }

        public const int npos = -1;

        public string_view substr(int pos, int n = int.MaxValue)
        {
            if (pos < 0 || pos > m_len)
            {
                pos = m_len;
            }
            int len = Math.Min(n, m_len - pos);
            return new string_view(m_s, m_pos + pos, len);
        }

        public int find(char c, int from = 0)
        {
            for (int i = from; i < m_len; ++i)
            {
                if (m_s[m_pos + i] == c)
                {
                    return i;
                }
            }
            return npos;
        }

        public int find_last_of(char c, int from = int.MaxValue)
        {
            for (int i = Math.Min(from, m_len - 1); i >= 0; --i)
            {
                if (m_s[m_pos + i] == c)
                {
                    return i;
                }
            }
            return npos;
        }

        public int find_first_of(char c, int from = 0)
        {
            return find(c, from);
        }

        public override string ToString() { return m_s.Substring(m_pos, m_len); }
    }

    public static partial class Strutil
    {
        public enum QuoteBehavior { DeleteQuotes, KeepQuotes }

        public static bool starts_with(string s, string prefix) { return s.StartsWith(prefix, StringComparison.Ordinal); }
        public static bool ends_with(string s, string suffix) { return s.EndsWith(suffix, StringComparison.Ordinal); }
        public static bool contains(string s, string sub) { return s.IndexOf(sub, StringComparison.Ordinal) >= 0; }

        public static string replace(string str, string pattern, string replacement, bool global = false)
        {
            if (pattern.Length == 0)
            {
                return str;
            }
            if (global)
            {
                return str.Replace(pattern, replacement);
            }
            int i = str.IndexOf(pattern, StringComparison.Ordinal);
            if (i < 0)
            {
                return str;
            }
            return str.Substring(0, i) + replacement + str.Substring(i + pattern.Length);
        }

        public static string join(IEnumerable<string> seq, string sep)
        {
            return string.Join(sep, seq);
        }

        private static bool is_space(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';
        }

        private static bool is_alpha(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool is_digit(char c)
        {
            return c >= '0' && c <= '9';
        }

        /// Skip leading whitespace
        public static bool skip_whitespace(ref string_view str)
        {
            while (str.size() > 0 && is_space(str.front()))
            {
                str.remove_prefix(1);
            }
            return str.size() > 0;
        }

        /// If the first (non-whitespace) character is c, eat it and return true
        public static bool parse_char(ref string_view str, char c, bool skip_white = true, bool eat = true)
        {
            if (skip_white)
            {
                skip_whitespace(ref str);
            }
            if (str.size() > 0 && str.front() == c)
            {
                if (eat)
                {
                    str.remove_prefix(1);
                }
                return true;
            }
            return false;
        }

        /// If the string begins (after whitespace) with prefix, eat it and return true
        public static bool parse_prefix(ref string_view str, string prefix, bool eat = true)
        {
            string_view p = str;
            skip_whitespace(ref p);
            if (p.size() < prefix.Length)
            {
                return false;
            }
            for (int i = 0; i < prefix.Length; ++i)
            {
                if (p[i] != prefix[i])
                {
                    return false;
                }
            }
            if (eat)
            {
                p.remove_prefix(prefix.Length);
                str = p;
            }
            return true;
        }

        /// Parse characters up to (not including) the first of the separator characters; the result is eaten from str
        public static string_view parse_until(ref string_view str, string sep = " \t\r\n", bool eat = true)
        {
            int n = 0;
            while (n < str.size() && sep.IndexOf(str[n]) < 0)
            {
                ++n;
            }
            string_view result = str.substr(0, n);
            if (eat)
            {
                str.remove_prefix(n);
            }
            return result;
        }

        public static string_view parse_until_char(ref string_view str, char c, bool eat = true)
        {
            return parse_until(ref str, c.ToString(), eat);
        }

        /// Parse an integer after optional whitespace
        public static bool parse_int(ref string_view str, out int val, bool eat = true)
        {
            val = 0;
            string_view p = str;
            skip_whitespace(ref p);
            int n = 0;
            if (n < p.size() && (p[n] == '-' || p[n] == '+'))
            {
                ++n;
            }
            int firstdigit = n;
            while (n < p.size() && is_digit(p[n]))
            {
                ++n;
            }
            if (n == firstdigit)
            {
                return false;
            }
            long v;
            if (!long.TryParse(p.substr(0, n).ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v))
            {
                return false;
            }
            val = (int)v;
            if (eat)
            {
                p.remove_prefix(n);
                str = p;
            }
            return true;
        }

        /// Parse a float after optional whitespace
        public static bool parse_float(ref string_view str, out float val, bool eat = true)
        {
            val = 0.0f;
            string_view p = str;
            skip_whitespace(ref p);
            int n = 0;
            if (n < p.size() && (p[n] == '-' || p[n] == '+'))
            {
                ++n;
            }
            // inf / nan spellings
            string rest = p.substr(n, 3).ToString().ToLowerInvariant();
            if (rest == "inf" || rest == "nan")
            {
                n += 3;
                if (rest == "inf" && p.substr(n, 5).ToString().ToLowerInvariant() == "inity")
                {
                    n += 5;
                }
                string tok = p.substr(0, n).ToString().ToLowerInvariant();
                val = tok.StartsWith("-") ? (rest == "inf" ? float.NegativeInfinity : float.NaN) : (rest == "inf" ? float.PositiveInfinity : float.NaN);
                if (eat)
                {
                    p.remove_prefix(n);
                    str = p;
                }
                return true;
            }
            int digits = 0;
            while (n < p.size() && is_digit(p[n]))
            {
                ++n;
                ++digits;
            }
            if (n < p.size() && p[n] == '.')
            {
                ++n;
                while (n < p.size() && is_digit(p[n]))
                {
                    ++n;
                    ++digits;
                }
            }
            if (digits == 0)
            {
                return false;
            }
            if (n < p.size() && (p[n] == 'e' || p[n] == 'E'))
            {
                int m = n + 1;
                if (m < p.size() && (p[m] == '-' || p[m] == '+'))
                {
                    ++m;
                }
                int expdigits = 0;
                while (m < p.size() && is_digit(p[m]))
                {
                    ++m;
                    ++expdigits;
                }
                if (expdigits > 0)
                {
                    n = m;
                }
            }
            float f;
            if (!float.TryParse(p.substr(0, n).ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
            {
                return false;
            }
            val = f;
            if (eat)
            {
                p.remove_prefix(n);
                str = p;
            }
            return true;
        }

        /// The first word (a maximal run of letters) after optional whitespace
        public static string_view parse_word(ref string_view str, bool eat = true)
        {
            string_view p = str;
            skip_whitespace(ref p);
            int n = 0;
            while (n < p.size() && is_alpha(p[n]))
            {
                ++n;
            }
            string_view result = p.substr(0, n);
            if (eat)
            {
                p.remove_prefix(n);
                str = p;
            }
            return result;
        }

        /// An identifier: a letter or underscore (or one of 'allowed') followed by letters, digits, underscores (or 'allowed')
        public static string_view parse_identifier(ref string_view str, string allowed = "", bool eat = true)
        {
            string_view p = str;
            skip_whitespace(ref p);
            int n = 0;
            if (p.size() > 0 && (is_alpha(p[0]) || p[0] == '_' || allowed.IndexOf(p[0]) >= 0))
            {
                ++n;
                while (n < p.size() && (is_alpha(p[n]) || is_digit(p[n]) || p[n] == '_' || allowed.IndexOf(p[n]) >= 0))
                {
                    ++n;
                }
            }
            string_view result = p.substr(0, n);
            if (eat)
            {
                p.remove_prefix(n);
                str = p;
            }
            return result;
        }

        /// A quoted string (single or double quotes, backslash escapes skipped over) after optional whitespace
        public static bool parse_string(ref string_view str, out string_view val, bool eat = true, QuoteBehavior keep_quotes = QuoteBehavior.DeleteQuotes)
        {
            val = new string_view();
            string_view p = str;
            skip_whitespace(ref p);
            if (p.size() < 1 || (p.front() != '"' && p.front() != '\''))
            {
                return false;
            }
            char quote = p.front();
            int n = 1;
            bool escaped = false;
            while (n < p.size())
            {
                char c = p[n];
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == quote)
                {
                    break;
                }
                ++n;
            }
            if (n >= p.size())
            {
                return false;  // no closing quote
            }
            val = keep_quotes == QuoteBehavior.KeepQuotes ? p.substr(0, n + 1) : p.substr(1, n - 1);
            if (eat)
            {
                p.remove_prefix(n + 1);
                str = p;
            }
            return true;
        }

        public static int stoi(string s)
        {
            int v;
            string_view p = new string_view(s);
            return parse_int(ref p, out v) ? v : 0;
        }

        /// Text of a duration the way OIIO prints it: "1.5s", "2m 3.0s", "1h 5m 0.2s"
        public static string timeintervalformat(double secs, int digits = 1)
        {
            const double mins = 60.0;
            const double hours = mins * 60.0;
            const double days = hours * 24.0;
            StringBuilder o = new StringBuilder();
            int d = (int)(secs / days);
            secs -= d * days;
            int h = (int)(secs / hours);
            secs -= h * hours;
            int m = (int)(secs / mins);
            secs -= m * mins;
            if (d > 0)
            {
                o.Append(d + "d " + h + "h ");
            }
            else if (h > 0)
            {
                o.Append(h + "h ");
            }
            if (m > 0 || h > 0 || d > 0)
            {
                o.Append(m + "m " + secs.ToString("F" + digits, CultureInfo.InvariantCulture) + "s");
            }
            else
            {
                o.Append(secs.ToString("F" + digits, CultureInfo.InvariantCulture) + "s");
            }
            return o.ToString();
        }

        /// Text of a byte count: "512 B", "1.5 KB", "2.0 MB"
        public static string memformat(long bytes, int digits = 1)
        {
            const long KB = 1L << 10;
            const long MB = 1L << 20;
            const long GB = 1L << 30;
            string units = "B";
            double d = bytes;
            if (bytes >= GB)
            {
                units = "GB";
                d = (double)bytes / GB;
            }
            else if (bytes >= MB)
            {
                units = "MB";
                d = (double)bytes / MB;
            }
            else if (bytes >= KB)
            {
                units = "KB";
                d = (double)bytes / KB;
            }
            else
            {
                return bytes + " B";
            }
            return d.ToString("F" + digits, CultureInfo.InvariantCulture) + " " + units;
        }

        /// Break text into lines of at most 'columns' characters, indenting the continuation lines
        public static string wordwrap(string src, int columns = 80, int prefix = 0)
        {
            if (columns < prefix + 20)
            {
                return src;
            }
            StringBuilder o = new StringBuilder();
            columns -= prefix;
            string[] words = src.Split(' ');
            int linelen = 0;
            bool first = true;
            foreach (string w in words)
            {
                if (!first && linelen + 1 + w.Length > columns)
                {
                    o.Append('\n');
                    o.Append(' ', prefix);
                    linelen = 0;
                    first = true;
                }
                if (!first)
                {
                    o.Append(' ');
                    ++linelen;
                }
                o.Append(w);
                linelen += w.Length;
                first = false;
            }
            return o.ToString();
        }
    }

    /// The replacement-field formatter of {fmt}
    public static class Fmt
    {
        /// Format with "{}" / "{:spec}" fields taking the arguments in order ("{{" and "}}" are the braces)
        public static string format(string fmt, params object[] args)
        {
            StringBuilder o = new StringBuilder();
            int arg = 0;
            for (int i = 0; i < fmt.Length; ++i)
            {
                char c = fmt[i];
                if (c == '{')
                {
                    if (i + 1 < fmt.Length && fmt[i + 1] == '{')
                    {
                        o.Append('{');
                        ++i;
                        continue;
                    }
                    int close = fmt.IndexOf('}', i);
                    if (close < 0)
                    {
                        o.Append(fmt, i, fmt.Length - i);
                        break;
                    }
                    string field = fmt.Substring(i + 1, close - i - 1);
                    string spec = "";
                    int colon = field.IndexOf(':');
                    int index = arg;
                    if (colon >= 0)
                    {
                        spec = field.Substring(colon + 1);
                        field = field.Substring(0, colon);
                    }
                    if (field.Length > 0)
                    {
                        int.TryParse(field, out index);
                    }
                    else
                    {
                        ++arg;
                    }
                    if (index < args.Length)
                    {
                        o.Append(format_arg(spec, args[index]));
                    }
                    i = close;
                }
                else if (c == '}' && i + 1 < fmt.Length && fmt[i + 1] == '}')
                {
                    o.Append('}');
                    ++i;
                }
                else
                {
                    o.Append(c);
                }
            }
            return o.ToString();
        }

        // Digits and decimal exponent of the shortest representation that round-trips: value = 0.d1d2d3... * 10^exp10
        private static void shortest_digits(double v, bool isfloat, out string digits, out int exp10)
        {
            string r = isfloat ? ((float)v).ToString("R", CultureInfo.InvariantCulture) : v.ToString("R", CultureInfo.InvariantCulture);
            r = r.TrimStart('-');
            int e = 0;
            int epos = r.IndexOfAny(new char[] { 'E', 'e' });
            if (epos >= 0)
            {
                e = int.Parse(r.Substring(epos + 1), CultureInfo.InvariantCulture);
                r = r.Substring(0, epos);
            }
            int dot = r.IndexOf('.');
            string intpart = dot >= 0 ? r.Substring(0, dot) : r;
            string frac = dot >= 0 ? r.Substring(dot + 1) : "";
            string all = intpart + frac;
            int pointpos = intpart.Length + e;
            int lead = 0;
            while (lead < all.Length - 1 && all[lead] == '0')
            {
                ++lead;
            }
            all = all.Substring(lead);
            pointpos -= lead;
            all = all.TrimEnd('0');
            if (all.Length == 0)
            {
                all = "0";
                pointpos = 1;
            }
            digits = all;
            exp10 = pointpos;
        }

        private static string two_digit_exp(int e, bool upper)
        {
            string sign = e < 0 ? "-" : "+";
            int a = Math.Abs(e);
            return (upper ? "E" : "e") + sign + (a < 10 ? "0" : "") + a;
        }

        /// printf %e
        public static string format_e(double v, int prec, bool upper)
        {
            string s = v.ToString((upper ? "E" : "e") + prec, CultureInfo.InvariantCulture);
            // .NET writes e+006: reduce the exponent to at least two digits
            int epos = s.IndexOfAny(new char[] { 'e', 'E' });
            string mant = s.Substring(0, epos);
            int e = int.Parse(s.Substring(epos + 1), CultureInfo.InvariantCulture);
            return mant + two_digit_exp(e, upper);
        }

        /// printf %g
        public static string format_g(double v, int prec, bool upper, bool alt)
        {
            if (prec == 0)
            {
                prec = 1;
            }
            if (v == 0.0)
            {
                string z = alt ? "0." + new string('0', prec - 1) : "0";
                return (1.0 / v < 0 ? "-" : "") + z;
            }
            string es = v.ToString("e" + (prec - 1), CultureInfo.InvariantCulture);
            int epos = es.IndexOf('e');
            int x = int.Parse(es.Substring(epos + 1), CultureInfo.InvariantCulture);
            string result;
            if (x < -4 || x >= prec)
            {
                string mant = es.Substring(0, epos);
                if (!alt && mant.Contains("."))
                {
                    mant = mant.TrimEnd('0').TrimEnd('.');
                }
                result = mant + two_digit_exp(x, upper);
            }
            else
            {
                result = v.ToString("F" + Math.Max(0, prec - 1 - x), CultureInfo.InvariantCulture);
                if (!alt && result.Contains("."))
                {
                    result = result.TrimEnd('0').TrimEnd('.');
                }
            }
            return result;
        }

        private static string format_default_float(double v, bool isfloat)
        {
            if (double.IsNaN(v))
            {
                return "nan";
            }
            if (double.IsInfinity(v))
            {
                return v < 0 ? "-inf" : "inf";
            }
            string digits;
            int exp10;
            shortest_digits(v, isfloat, out digits, out exp10);
            string sign = (v < 0 || (v == 0 && 1.0 / v < 0)) ? "-" : "";
            int x = exp10 - 1;  // exponent of the first digit
            string body;
            if (x < -4 || x >= 16)
            {
                body = digits.Substring(0, 1) + (digits.Length > 1 ? "." + digits.Substring(1) : "") + two_digit_exp(x, false);
            }
            else if (exp10 <= 0)
            {
                body = "0." + new string('0', -exp10) + digits;
            }
            else if (digits.Length <= exp10)
            {
                body = digits + new string('0', exp10 - digits.Length);
            }
            else
            {
                body = digits.Substring(0, exp10) + "." + digits.Substring(exp10);
            }
            return sign + body;
        }

        /// Format one value with the part of a replacement field after the colon
        public static string format_arg(string spec, object arg)
        {
            char fill = ' ';
            char align = '\0';
            char sign = '-';
            bool alt = false;
            bool zero = false;
            int width = 0;
            int prec = -1;
            char type = '\0';
            int p = 0;
            if (spec.Length >= 2 && (spec[1] == '<' || spec[1] == '>' || spec[1] == '^'))
            {
                fill = spec[0];
                align = spec[1];
                p = 2;
            }
            else if (spec.Length >= 1 && (spec[0] == '<' || spec[0] == '>' || spec[0] == '^'))
            {
                align = spec[0];
                p = 1;
            }
            if (p < spec.Length && (spec[p] == '+' || spec[p] == '-' || spec[p] == ' '))
            {
                sign = spec[p++];
            }
            if (p < spec.Length && spec[p] == '#')
            {
                alt = true;
                ++p;
            }
            if (p < spec.Length && spec[p] == '0')
            {
                zero = true;
                ++p;
            }
            while (p < spec.Length && char.IsDigit(spec[p]))
            {
                width = width * 10 + (spec[p++] - '0');
            }
            if (p < spec.Length && spec[p] == '.')
            {
                ++p;
                prec = 0;
                while (p < spec.Length && char.IsDigit(spec[p]))
                {
                    prec = prec * 10 + (spec[p++] - '0');
                }
            }
            if (p < spec.Length)
            {
                type = spec[p];
            }

            string text;
            bool numeric = false;
            bool negative = false;
            if (arg is ustring)
            {
                arg = ((ustring)arg).@string();
            }
            if (arg is string)
            {
                text = (string)arg;
                if (prec >= 0 && prec < text.Length)
                {
                    text = text.Substring(0, prec);
                }
            }
            else if (arg is bool)
            {
                text = (bool)arg ? "true" : "false";
            }
            else if (arg is char)
            {
                text = ((char)arg).ToString();
            }
            else if (arg is float || arg is double)
            {
                numeric = true;
                bool isfloat = arg is float;
                double v = isfloat ? (double)(float)arg : (double)arg;
                negative = v < 0 || (v == 0 && 1.0 / v < 0);
                double a = Math.Abs(v);
                if (double.IsNaN(v))
                {
                    text = "nan";
                    negative = false;
                    zero = false;
                }
                else if (double.IsInfinity(v))
                {
                    text = "inf";
                    zero = false;
                }
                else if (type == 'f' || type == 'F')
                {
                    text = a.ToString("F" + (prec < 0 ? 6 : prec), CultureInfo.InvariantCulture);
                }
                else if (type == 'e' || type == 'E')
                {
                    text = format_e(a, prec < 0 ? 6 : prec, type == 'E');
                }
                else if (type == 'g' || type == 'G')
                {
                    text = format_g(a, prec < 0 ? 6 : prec, type == 'G', alt);
                }
                else if (prec >= 0)
                {
                    text = format_g(a, prec, false, alt);
                }
                else
                {
                    text = format_default_float(a, isfloat);
                }
            }
            else if (arg is int || arg is long || arg is uint || arg is ulong || arg is short || arg is ushort || arg is byte || arg is sbyte)
            {
                numeric = true;
                if (type == 'c')
                {
                    text = ((char)Convert.ToInt32(arg)).ToString();
                    numeric = false;
                }
                else
                {
                    bool isunsigned = arg is uint || arg is ulong || arg is ushort || arg is byte;
                    ulong mag;
                    if (isunsigned)
                    {
                        mag = Convert.ToUInt64(arg);
                    }
                    else
                    {
                        long sv = Convert.ToInt64(arg);
                        negative = sv < 0;
                        mag = negative ? (ulong)(-(sv + 1)) + 1UL : (ulong)sv;
                    }
                    switch (type)
                    {
                        case 'x': text = mag.ToString("x", CultureInfo.InvariantCulture); if (alt) { text = "0x" + text; } break;
                        case 'X': text = mag.ToString("X", CultureInfo.InvariantCulture); if (alt) { text = "0X" + text; } break;
                        case 'o': text = Convert.ToString((long)mag, 8); if (alt) { text = "0" + text; } break;
                        case 'b':
                        case 'B': text = Convert.ToString((long)mag, 2); if (alt) { text = "0" + type + text; } break;
                        default: text = mag.ToString(CultureInfo.InvariantCulture); break;
                    }
                }
            }
            else if (arg is IntPtr)
            {
                text = "0x" + ((IntPtr)arg).ToInt64().ToString("x");
            }
            else
            {
                text = arg == null ? "" : arg.ToString();
            }

            string signtext = "";
            if (numeric)
            {
                if (negative)
                {
                    signtext = "-";
                }
                else if (sign == '+')
                {
                    signtext = "+";
                }
                else if (sign == ' ')
                {
                    signtext = " ";
                }
            }
            if (width > signtext.Length + text.Length)
            {
                int pad = width - signtext.Length - text.Length;
                if (zero && numeric && align == '\0')
                {
                    return signtext + new string('0', pad) + text;
                }
                char a2 = align != '\0' ? align : (numeric ? '>' : '<');
                if (a2 == '<')
                {
                    return signtext + text + new string(fill, pad);
                }
                if (a2 == '>')
                {
                    return new string(fill, pad) + signtext + text;
                }
                int left = pad / 2;
                return new string(fill, left) + signtext + text + new string(fill, pad - left);
            }
            return signtext + text;
        }
    }

    /// A stopwatch
    public class Timer
    {
        public enum StartNowVal { DontStartNow = 0, StartNow = 1 }

        private long m_starttime;
        private long m_elapsed_ticks;
        private bool m_ticking;

        public Timer(StartNowVal startnow = StartNowVal.StartNow)
        {
            m_ticking = false;
            m_elapsed_ticks = 0;
            if (startnow == StartNowVal.StartNow)
            {
                start();
            }
        }

        public void start()
        {
            if (!m_ticking)
            {
                m_starttime = Stopwatch.GetTimestamp();
                m_ticking = true;
            }
        }

        public double stop()
        {
            if (m_ticking)
            {
                m_elapsed_ticks += Stopwatch.GetTimestamp() - m_starttime;
                m_ticking = false;
            }
            return seconds(m_elapsed_ticks);
        }

        public void reset()
        {
            m_elapsed_ticks = 0;
            m_ticking = false;
        }

        /// Ticks elapsed so far
        public long ticks()
        {
            long t = m_elapsed_ticks;
            if (m_ticking)
            {
                t += Stopwatch.GetTimestamp() - m_starttime;
            }
            return t;
        }

        /// Seconds elapsed so far (the C++ operator())
        public double elapsed()
        {
            return seconds(ticks());
        }

        public static double seconds(long ticks)
        {
            return (double)ticks / Stopwatch.Frequency;
        }
    }

    public static class Filesystem
    {
        /// Split a search path on ':' (';' on Windows) into its directories
        public static void searchpath_split(string searchpath, List<string> dirs, bool validonly = false)
        {
            dirs.Clear();
            char sep = Path.PathSeparator;
            foreach (string d in searchpath.Split(sep))
            {
                if (d.Length == 0)
                {
                    continue;
                }
                if (validonly && !Directory.Exists(d))
                {
                    continue;
                }
                dirs.Add(d);
            }
        }

        /// Find the file in the directories (and optionally the current directory first), or return an empty string
        public static string searchpath_find(string filename, List<string> dirs, bool testcwd = true, bool recursive = false)
        {
            if (testcwd && File.Exists(filename))
            {
                return filename;
            }
            if (Path.IsPathRooted(filename))
            {
                return File.Exists(filename) ? filename : "";
            }
            foreach (string d in dirs)
            {
                string f = Path.Combine(d, filename);
                if (File.Exists(f))
                {
                    return f;
                }
                if (recursive && Directory.Exists(d))
                {
                    foreach (string sub in Directory.GetDirectories(d))
                    {
                        List<string> subs = new List<string>();
                        subs.Add(sub);
                        string r = searchpath_find(filename, subs, false, true);
                        if (r.Length > 0)
                        {
                            return r;
                        }
                    }
                }
            }
            return "";
        }

        public static bool exists(string path) { return File.Exists(path) || Directory.Exists(path); }
        public static bool is_directory(string path) { return Directory.Exists(path); }
        public static string filename(string path) { return Path.GetFileName(path); }

        public static bool create_directory(string path, out string err)
        {
            err = "";
            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch (Exception e)
            {
                err = e.Message;
                return false;
            }
        }

        public static bool write_text_file(string filename, string str)
        {
            try
            {
                File.WriteAllText(filename, str);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool read_text_file(string filename, out string str)
        {
            try
            {
                str = File.ReadAllText(filename);
                return true;
            }
            catch (Exception)
            {
                str = "";
                return false;
            }
        }
    }

    public static class Sysutil
    {
        public static int terminal_columns()
        {
            try
            {
                return Console.IsOutputRedirected ? 80 : Math.Max(20, Console.WindowWidth);
            }
            catch (Exception)
            {
                return 80;
            }
        }

        public static int hardware_concurrency() { return Environment.ProcessorCount; }

        public static long physical_memory() { return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; }

        public static string getenv(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return v == null ? "" : v;
        }
    }

    /// OIIO::TypeDesc(string): the type named by text such as "int", "float", "color", "string[3]" (UNKNOWN if the text names no type)
    public static class TypeDescParse
    {
        public static TypeDesc fromstring(string typestring)
        {
            string name = typestring.Trim();
            int arraylen = 0;
            int br = name.IndexOf('[');
            if (br >= 0)
            {
                int close = name.IndexOf(']', br);
                string inside = close > br ? name.Substring(br + 1, close - br - 1).Trim() : "";
                arraylen = inside.Length == 0 ? -1 : int.Parse(inside, CultureInfo.InvariantCulture);
                name = name.Substring(0, br).Trim();
            }
            TypeDesc t;
            switch (name)
            {
                case "int": case "int32": t = TypeDescs.TypeInt; break;
                case "float": t = TypeDescs.TypeFloat; break;
                case "color": t = TypeDescs.TypeColor; break;
                case "point": t = TypeDescs.TypePoint; break;
                case "vector": t = TypeDescs.TypeVector; break;
                case "normal": t = TypeDescs.TypeNormal; break;
                case "matrix": case "matrix44": t = TypeDescs.TypeMatrix; break;
                case "string": t = TypeDescs.TypeString; break;
                case "int64": t = new TypeDesc(TypeDesc.INT64); break;
                case "uint": case "uint32": t = new TypeDesc(TypeDesc.UINT); break;
                case "uint64": t = new TypeDesc(TypeDesc.UINT64); break;
                case "uint8": t = new TypeDesc(TypeDesc.UINT8); break;
                case "int8": t = new TypeDesc(TypeDesc.INT8); break;
                case "double": t = new TypeDesc(TypeDesc.DOUBLE); break;
                case "ustringhash": t = new TypeDesc(TypeDesc.USTRINGHASH); break;
                case "pointer": t = new TypeDesc(TypeDesc.PTR); break;
                default: return new TypeDesc();
            }
            if (arraylen != 0)
            {
                t.arraylen = arraylen;
            }
            return t;
        }
    }

    /// One named, typed value (OIIO::ParamValue): the data are held as a managed array int[], float[] or ustring[]
    public class ParamValue
    {
        public ustring name_;
        public TypeDesc type_;
        public int nvalues_;
        public object data_;

        public ParamValue(string name, TypeDesc type, int nvalues, object val)
        {
            name_ = new ustring(name);
            type_ = type;
            nvalues_ = nvalues;
            data_ = copy_values(type, nvalues, val);
        }

        public ustring name() { return name_; }
        public TypeDesc type() { return type_; }
        public int nvalues() { return nvalues_; }
        public object data() { return data_; }

        /// Copy the values of a Parameter() argument into a freshly allocated array of the base type of 'type'. The argument may be a boxed
        /// scalar (int, float, double, string, ustring), or an array (int[], float[], string[], ustring[], Vec3[], Color3[]).
        public static object copy_values(TypeDesc type, int nvalues, object val)
        {
            int count = Math.Max(1, type.basevalues()) * nvalues;
            if (type.basetype == TypeDesc.INT)
            {
                int[] r = new int[count];
                if (val is int)
                {
                    r[0] = (int)val;
                }
                else if (val is int[])
                {
                    Array.Copy((int[])val, r, Math.Min(count, ((int[])val).Length));
                }
                return r;
            }
            if (type.basetype == TypeDesc.FLOAT)
            {
                float[] r = new float[count];
                if (val is float)
                {
                    r[0] = (float)val;
                }
                else if (val is double)
                {
                    r[0] = (float)(double)val;
                }
                else if (val is float[])
                {
                    Array.Copy((float[])val, r, Math.Min(count, ((float[])val).Length));
                }
                else if (val is Vec3)
                {
                    Vec3 v = (Vec3)val;
                    r[0] = v.x;
                    r[1] = v.y;
                    r[2] = v.z;
                }
                else if (val is Color3)
                {
                    Color3 v = (Color3)val;
                    r[0] = v.x;
                    r[1] = v.y;
                    r[2] = v.z;
                }
                else if (val is Matrix44)
                {
                    Matrix44 m = (Matrix44)val;
                    for (int i = 0; i < 16; ++i)
                    {
                        r[i] = m.Get(i);
                    }
                }
                return r;
            }
            if (type.basetype == TypeDesc.STRING)
            {
                ustring[] r = new ustring[count];
                for (int i = 0; i < count; ++i)
                {
                    r[i] = new ustring("");
                }
                if (val is string)
                {
                    r[0] = new ustring((string)val);
                }
                else if (val is ustring)
                {
                    r[0] = (ustring)val;
                }
                else if (val is string[])
                {
                    string[] s = (string[])val;
                    for (int i = 0; i < Math.Min(count, s.Length); ++i)
                    {
                        r[i] = new ustring(s[i]);
                    }
                }
                else if (val is ustring[])
                {
                    ustring[] s = (ustring[])val;
                    for (int i = 0; i < Math.Min(count, s.Length); ++i)
                    {
                        r[i] = s[i];
                    }
                }
                return r;
            }
            return val;
        }
    }

    public class ParamValueList : List<ParamValue>
    {
    }
}
