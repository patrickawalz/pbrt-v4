// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

using System;
using System.Globalization;
using System.Text;

namespace pbrt
{
    public static unsafe partial class Globals
    {
        // Printing Function Declarations
        // C-style formatting. C++ variadic templates become params object[]; the directive
        // handling follows detail::stringPrintfRecursive().
        public static string StringPrintf(string fmt, params object[] args)
        {
            StringBuilder s = new StringBuilder();
            int argIndex = 0;
            int c = 0;
            while (true)
            {
                // copyToFormatString(): copy up to the next directive
                while (c < fmt.Length)
                {
                    if (fmt[c] != '%')
                    {
                        s.Append(fmt[c]);
                        ++c;
                    }
                    else if (c + 1 < fmt.Length && fmt[c + 1] == '%')
                    {
                        s.Append('%');
                        c += 2;
                    }
                    else
                        break;
                }
                if (c >= fmt.Length)
                    break;

                // Parse the directive
                int start = c;
                ++c;  // '%'
                bool left = false, plus = false, space = false, alt = false, zero = false;
                for (; c < fmt.Length; ++c)
                {
                    char f = fmt[c];
                    if (f == '-') left = true;
                    else if (f == '+') plus = true;
                    else if (f == ' ') space = true;
                    else if (f == '#') alt = true;
                    else if (f == '0') zero = true;
                    else break;
                }
                int width = -1, precision = -1;
                if (c < fmt.Length && fmt[c] == '*')
                {
                    if (argIndex >= args.Length) LOG_FATAL("Not enough optional values passed to Printf.");
                    width = Convert.ToInt32(args[argIndex++]);
                    if (width < 0) { left = true; width = -width; }
                    ++c;
                }
                else
                {
                    int w = 0; bool any = false;
                    while (c < fmt.Length && char.IsDigit(fmt[c])) { w = w * 10 + (fmt[c] - '0'); ++c; any = true; }
                    if (any) width = w;
                }
                if (c < fmt.Length && fmt[c] == '.')
                {
                    ++c;
                    if (c < fmt.Length && fmt[c] == '*')
                    {
                        if (argIndex >= args.Length) LOG_FATAL("Not enough optional values passed to Printf.");
                        precision = Convert.ToInt32(args[argIndex++]);
                        ++c;
                    }
                    else
                    {
                        int p = 0;
                        while (c < fmt.Length && char.IsDigit(fmt[c])) { p = p * 10 + (fmt[c] - '0'); ++c; }
                        precision = p;
                    }
                }
                // length modifiers
                while (c < fmt.Length && (fmt[c] == 'l' || fmt[c] == 'h' || fmt[c] == 'z' || fmt[c] == 'j' || fmt[c] == 't' || fmt[c] == 'L' || fmt[c] == 'q'))
                    ++c;
                if (c >= fmt.Length)
                    LOG_FATAL("Truncated format directive in Printf.");
                char conv = fmt[c++];
                string directive = fmt.Substring(start, c - start);

                if (argIndex >= args.Length)
                    LOG_FATAL("Not enough optional values passed to Printf.");
                object v = args[argIndex++];

                string body;
                bool numeric = false;
                if (v is float fv && (conv == 'f' || conv == 's') && directive == "%" + conv)
                    body = FloatToString(fv);
                else if (v is double dv && (conv == 'f' || conv == 's') && directive == "%" + conv)
                    body = DoubleToString(dv);
                else if (v is bool bv && conv == 's')
                    body = bv ? "true" : "false";
                else
                {
                    switch (conv)
                    {
                        case 'd':
                        case 'i':
                            {
                                if (!IsIntegral(v)) LOG_FATAL("Non-integral type passed to %d format.");
                                if (v is ulong ulv)
                                {
                                    // (unsigned values print as unsigned, like the IntegerFormatTrait "lu")
                                    string udigits = ulv.ToString(CultureInfo.InvariantCulture);
                                    if (precision >= 0) udigits = udigits.PadLeft(precision, '0');
                                    body = SignPad(plus ? "+" : (space ? " " : ""), udigits, width, left, zero && precision < 0);
                                    numeric = true;
                                    break;
                                }
                                long n = ToInt64(v);
                                string digits = (n < 0 ? (n == long.MinValue ? "9223372036854775808" : (-n).ToString(CultureInfo.InvariantCulture)) : n.ToString(CultureInfo.InvariantCulture));
                                if (precision >= 0) digits = digits.PadLeft(precision, '0');
                                string sign = n < 0 ? "-" : (plus ? "+" : (space ? " " : ""));
                                body = SignPad(sign, digits, width, left, zero && precision < 0);
                                numeric = true;
                                break;
                            }
                        case 'u':
                        case 'x':
                        case 'X':
                        case 'o':
                            {
                                if (!IsIntegral(v)) LOG_FATAL("Non-integral type passed to integer format.");
                                ulong n = ToUInt64(v);
                                string digits;
                                string prefix = "";
                                if (conv == 'u') digits = n.ToString(CultureInfo.InvariantCulture);
                                else if (conv == 'x') { digits = n.ToString("x", CultureInfo.InvariantCulture); if (alt && n != 0) prefix = "0x"; }
                                else if (conv == 'X') { digits = n.ToString("X", CultureInfo.InvariantCulture); if (alt && n != 0) prefix = "0X"; }
                                else { digits = Convert.ToString((long)n, 8); if (alt) digits = "0" + digits; }
                                if (precision >= 0) digits = digits.PadLeft(precision, '0');
                                body = SignPad(prefix, digits, width, left, zero && precision < 0);
                                numeric = true;
                                break;
                            }
                        case 'c':
                            body = v is char ch ? ch.ToString() : ((char)Convert.ToInt32(v)).ToString();
                            break;
                        case 's':
                            {
                                string str = PrintObject(v);
                                if (precision >= 0 && precision < str.Length) str = str.Substring(0, precision);
                                body = str;
                                break;
                            }
                        case 'p':
                            body = "0x" + ToUInt64(v).ToString("x", CultureInfo.InvariantCulture);
                            break;
                        case 'f':
                        case 'F':
                        case 'e':
                        case 'E':
                        case 'g':
                        case 'G':
                        case 'a':
                        case 'A':
                            {
                                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                                string sign = (d < 0 || (d == 0 && double.IsNegative(d))) ? "-" : (plus ? "+" : (space ? " " : ""));
                                double ad = Math.Abs(d);
                                string digits;
                                if (double.IsNaN(d)) { sign = plus ? "+" : ""; digits = char.IsUpper(conv) ? "NAN" : "nan"; zero = false; }
                                else if (double.IsInfinity(d)) { digits = char.IsUpper(conv) ? "INF" : "inf"; zero = false; }
                                else digits = FormatFloatingPoint(ad, conv, precision, alt);
                                body = SignPad(sign, digits, width, left, zero);
                                numeric = true;
                                break;
                            }
                        default:
                            LOG_FATAL("Unsupported Printf directive " + directive);
                            body = "";
                            break;
                    }
                }

                if (!numeric && width > body.Length)
                    body = left ? body.PadRight(width) : body.PadLeft(width);
                s.Append(body);
            }

            // No args left; make sure there aren't any extra values
            if (argIndex < args.Length)
                LOG_FATAL("Excess values passed to Printf.");
            return s.ToString();
        }

        // What operator<< prints for a value passed to %s: containers (anything with size()/data())
        // print as "[ a, b, c ]", null pointers as "(nullptr)".
        public static string PrintObject(object v)
        {
            if (v == null) return "(nullptr)";
            if (v is string str) return str;
            if (v is bool b) return b ? "true" : "false";
            if (v is float f) return FloatToString(f);
            if (v is double d) return DoubleToString(d);
            if (v is System.Collections.IEnumerable e)
            {
                StringBuilder sb = new StringBuilder("[ ");
                bool first = true;
                foreach (object o in e)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(PrintObject(o));
                }
                return sb.Append(" ]").ToString();
            }
            return v.ToString();
        }

        public static void Printf(string fmt, params object[] args)
        {
            string s = StringPrintf(fmt, args);
            Console.Out.Write(s);
        }

        private static bool IsIntegral(object v)
        {
            return v is int || v is uint || v is long || v is ulong || v is short || v is ushort || v is byte || v is sbyte || v is bool || v is char;
        }

        private static long ToInt64(object v)
        {
            if (v is ulong ul) return unchecked((long)ul);
            if (v is bool b) return b ? 1 : 0;
            if (v is char c) return c;
            return Convert.ToInt64(v);
        }

        private static ulong ToUInt64(object v)
        {
            if (v is long l) return unchecked((ulong)l);
            if (v is int i) return unchecked((uint)i);
            if (v is short sh) return unchecked((ushort)sh);
            if (v is sbyte sb) return unchecked((byte)sb);
            if (v is bool b) return b ? 1UL : 0UL;
            if (v is char c) return c;
            return Convert.ToUInt64(v);
        }

        private static string SignPad(string sign, string digits, int width, bool left, bool zero)
        {
            int len = sign.Length + digits.Length;
            if (width <= len)
                return sign + digits;
            if (left)
                return (sign + digits).PadRight(width);
            if (zero)
                return sign + new string('0', width - len) + digits;
            return (sign + digits).PadLeft(width);
        }

        private static string FormatE(double ad, int precision, bool upper, bool alt)
        {
            if (precision < 0) precision = 6;
            string r = ad.ToString("e" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            // .NET: d.ddde+ddd -> C: d.ddde+dd (at least two exponent digits)
            int ei = r.IndexOf('e');
            string mant = r.Substring(0, ei);
            char esign = r[ei + 1];
            string exp = r.Substring(ei + 2).TrimStart('0');
            if (exp.Length < 2) exp = exp.PadLeft(2, '0');
            if (alt && precision == 0) mant += ".";
            string result = mant + "e" + esign + exp;
            return upper ? result.ToUpperInvariant() : result;
        }

        private static string FormatFloatingPoint(double ad, char conv, int precision, bool alt)
        {
            switch (conv)
            {
                case 'f':
                case 'F':
                    {
                        if (precision < 0) precision = 6;
                        string r = ad.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                        if (alt && precision == 0) r += ".";
                        return r;
                    }
                case 'e':
                case 'E':
                    return FormatE(ad, precision, conv == 'E', alt);
                case 'g':
                case 'G':
                    {
                        int P = precision < 0 ? 6 : (precision == 0 ? 1 : precision);
                        if (ad == 0)
                        {
                            string z = alt ? "0." + new string('0', P - 1) : "0";
                            return z;
                        }
                        string e = ad.ToString("e" + (P - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                        int X = int.Parse(e.Substring(e.IndexOf('e') + 1), CultureInfo.InvariantCulture);
                        string r;
                        if (P > X && X >= -4)
                            r = ad.ToString("F" + (P - 1 - X).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                        else
                            r = FormatE(ad, P - 1, conv == 'G', alt);
                        if (!alt)
                        {
                            int ei = r.IndexOfAny(new char[] { 'e', 'E' });
                            string mant = ei < 0 ? r : r.Substring(0, ei);
                            string tail = ei < 0 ? "" : r.Substring(ei);
                            if (mant.Contains('.'))
                                mant = mant.TrimEnd('0').TrimEnd('.');
                            r = mant + tail;
                        }
                        return conv == 'G' ? r.ToUpperInvariant() : r;
                    }
                default:
                    // %a: hex float
                    return ad.ToString("R", CultureInfo.InvariantCulture);
            }
        }

        // double_conversion::DoubleToStringConverter (NO_FLAGS, "Inf", "NaN", 'e',
        // decimal_in_shortest_low = -6, decimal_in_shortest_high = 9) emulation.
        private static string ShortestToString(string roundTrip, bool negative)
        {
            // roundTrip is the shortest round-trip representation of |v| from .NET
            // ("123.45", "1E-05", "1.5E+20", ...). Extract digits and decimal point.
            string t = roundTrip;
            int exp10 = 0;
            int ei = t.IndexOfAny(new char[] { 'E', 'e' });
            if (ei >= 0)
            {
                exp10 = int.Parse(t.Substring(ei + 1), CultureInfo.InvariantCulture);
                t = t.Substring(0, ei);
            }
            int dot = t.IndexOf('.');
            string digits;
            int decimalPoint;
            if (dot < 0) { digits = t; decimalPoint = t.Length; }
            else { digits = t.Substring(0, dot) + t.Substring(dot + 1); decimalPoint = dot; }
            decimalPoint += exp10;
            // strip leading zeros
            int lead = 0;
            while (lead < digits.Length - 1 && digits[lead] == '0') { ++lead; --decimalPoint; }
            digits = digits.Substring(lead);
            // strip trailing zeros
            digits = digits.TrimEnd('0');
            if (digits.Length == 0) return negative ? "-0" : "0";

            int exponent = decimalPoint - 1;
            StringBuilder sb = new StringBuilder();
            if (negative) sb.Append('-');
            if (-6 <= exponent && exponent < 9)
            {
                if (decimalPoint <= 0)
                {
                    sb.Append("0.");
                    sb.Append('0', -decimalPoint);
                    sb.Append(digits);
                }
                else if (decimalPoint >= digits.Length)
                {
                    sb.Append(digits);
                    sb.Append('0', decimalPoint - digits.Length);
                }
                else
                {
                    sb.Append(digits, 0, decimalPoint);
                    sb.Append('.');
                    sb.Append(digits, decimalPoint, digits.Length - decimalPoint);
                }
            }
            else
            {
                sb.Append(digits[0]);
                if (digits.Length > 1)
                {
                    sb.Append('.');
                    sb.Append(digits, 1, digits.Length - 1);
                }
                sb.Append('e');
                if (exponent < 0) sb.Append('-');
                sb.Append(Math.Abs(exponent).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static string FloatToString(float v)
        {
            if (float.IsNaN(v)) return "NaN";
            if (float.IsInfinity(v)) return v < 0 ? "-Inf" : "Inf";
            bool neg = float.IsNegative(v);
            return ShortestToString(Math.Abs(v).ToString("R", CultureInfo.InvariantCulture), neg);
        }

        public static string DoubleToString(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsInfinity(v)) return v < 0 ? "-Inf" : "Inf";
            bool neg = double.IsNegative(v);
            return ShortestToString(Math.Abs(v).ToString("R", CultureInfo.InvariantCulture), neg);
        }

        // https://en.wikipedia.org/wiki/ANSI_escape_code#Colors
        public static string Red(string s)
        {
            string red = "\u001b[1m\u001b[31m";  // bold red
            string reset = "\u001b[0m";
            return red + s + reset;
        }

        public static string Yellow(string s)
        {
            // https://en.wikipedia.org/wiki/ANSI_escape_code#8-bit
            string yellow = "\u001b[1m\u001b[38;5;100m";
            string reset = "\u001b[0m";
            return yellow + s + reset;
        }

        public static string Green(string s)
        {
            // https://en.wikipedia.org/wiki/ANSI_escape_code#8-bit
            string green = "\u001b[1m\u001b[38;5;22m";
            string reset = "\u001b[0m";
            return green + s + reset;
        }
    }
}
