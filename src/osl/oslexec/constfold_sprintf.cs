// OSL port: OIIO::Strutil::sprintf, the printf-style formatting that the format() folder of constfold.cpp uses to build the constant string at
// optimization time. In the OIIO that OSL builds against (3.x) it is fmt::sprintf, the printf emulation of the {fmt} library, so this follows C printf:
//   * flags '-', '+', ' ', '#' and '0'; width and precision may be '*' (taken from an int argument); length modifiers (h l ll j z t L q) are skipped;
//   * d i: signed decimal, u o x X: the int taken as unsigned, c: the character with that code, s: the string (a number given to %s prints as {fmt}
//     prints it), e E f F g G: a float in the style of {fmt}/C (the same engine as Fmt.format_arg), "%%" is a percent sign;
//   * a precision on an integer conversion is the minimum number of digits, on %s it truncates the string; the '0' flag is ignored when there is a
//     precision for integers or when '-' is given.
// Arguments are int, float, double and string. The output of the golden test sprintf_golden_test.cs is what the real fmt::sprintf produced.

using System;
using System.Globalization;
using System.Text;

namespace OIIO
{
    public static partial class Strutil
    {
        public static string sprintf(string fmt, params object[] args)
        {
            StringBuilder o = new StringBuilder();
            int argnum = 0;
            int i = 0;
            while (i < fmt.Length)
            {
                char ch = fmt[i];
                if (ch != '%')
                {
                    o.Append(ch);
                    ++i;
                    continue;
                }
                ++i;
                if (i < fmt.Length && fmt[i] == '%')
                {
                    o.Append('%');
                    ++i;
                    continue;
                }

                bool left = false, plus = false, space = false, alt = false, zero = false;
                bool more = true;
                while (more && i < fmt.Length)
                {
                    switch (fmt[i])
                    {
                        case '-':
                            left = true;
                            ++i;
                            break;
                        case '+':
                            plus = true;
                            ++i;
                            break;
                        case ' ':
                            space = true;
                            ++i;
                            break;
                        case '#':
                            alt = true;
                            ++i;
                            break;
                        case '0':
                            zero = true;
                            ++i;
                            break;
                        default:
                            more = false;
                            break;
                    }
                }

                int width = 0;
                if (i < fmt.Length && fmt[i] == '*')
                {
                    ++i;
                    if (argnum < args.Length && args[argnum] is int)
                    {
                        width = (int)args[argnum];
                        ++argnum;
                        if (width < 0)
                        {
                            left = true;
                            width = -width;
                        }
                    }
                }
                else
                {
                    while (i < fmt.Length && fmt[i] >= '0' && fmt[i] <= '9')
                    {
                        width = width * 10 + (fmt[i] - '0');
                        ++i;
                    }
                }

                int prec = -1;
                if (i < fmt.Length && fmt[i] == '.')
                {
                    ++i;
                    prec = 0;
                    if (i < fmt.Length && fmt[i] == '*')
                    {
                        ++i;
                        if (argnum < args.Length && args[argnum] is int)
                        {
                            prec = (int)args[argnum];
                            ++argnum;
                            if (prec < 0)
                            {
                                prec = -1;
                            }
                        }
                    }
                    else
                    {
                        while (i < fmt.Length && fmt[i] >= '0' && fmt[i] <= '9')
                        {
                            prec = prec * 10 + (fmt[i] - '0');
                            ++i;
                        }
                    }
                }

                int short_count = 0;
                while (i < fmt.Length && (fmt[i] == 'h' || fmt[i] == 'l' || fmt[i] == 'L' || fmt[i] == 'j' || fmt[i] == 'z' || fmt[i] == 't' || fmt[i] == 'q'))
                {
                    if (fmt[i] == 'h')
                    {
                        ++short_count;
                    }
                    ++i;
                }

                if (i >= fmt.Length)
                {
                    break;
                }
                char conv = fmt[i];
                ++i;
                if (argnum >= args.Length)
                {
                    break;
                }
                object arg = args[argnum++];
                if (short_count > 0 && arg is int && conv != 'c' && conv != 's')
                {
                    // 'h' and 'hh' narrow the int to a short or a char (signed for d and i, unsigned for the others)
                    int iv = (int)arg;
                    bool is_signed = conv == 'd' || conv == 'i';
                    if (short_count == 1)
                    {
                        arg = is_signed ? (int)(short)iv : (int)(ushort)iv;
                    }
                    else
                    {
                        arg = is_signed ? (int)(sbyte)iv : (int)(byte)iv;
                    }
                }
                o.Append(format_one(arg, conv, left, plus, space, alt, zero, width, prec));
            }
            return o.ToString();
        }

        private static string pad_text(string prefix, string body, int width, bool left, bool zero_fill)
        {
            int len = prefix.Length + body.Length;
            if (len >= width)
            {
                return prefix + body;
            }
            int pad = width - len;
            if (left)
            {
                return prefix + body + new string(' ', pad);
            }
            if (zero_fill)
            {
                return prefix + new string('0', pad) + body;
            }
            return new string(' ', pad) + prefix + body;
        }

        private static string format_one(object arg, char conv, bool left, bool plus, bool space, bool alt, bool zero, int width, int prec)
        {
            if (arg is string)
            {
                string body = (string)arg;
                if (conv == 's' && prec >= 0 && body.Length > prec)
                {
                    body = body.Substring(0, prec);
                }
                return pad_text("", body, width, left, false);
            }
            if (arg is int)
            {
                int v = (int)arg;
                switch (conv)
                {
                    case 'c':
                        return pad_text("", ((char)(byte)v).ToString(), width, left, false);
                    case 'd':
                    case 'i':
                    case 'u':
                    case 'o':
                    case 'x':
                    case 'X':
                    {
                        bool is_signed = conv == 'd' || conv == 'i';
                        string sign = "";
                        ulong mag;
                        if (is_signed)
                        {
                            if (v < 0)
                            {
                                sign = "-";
                                mag = (ulong)(-(long)v);
                            }
                            else
                            {
                                mag = (ulong)v;
                                sign = plus ? "+" : (space ? " " : "");
                            }
                        }
                        else
                        {
                            mag = (uint)v;
                            sign = plus ? "+" : (space ? " " : "");
                        }
                        string digits;
                        string prefix = "";
                        if (conv == 'x' || conv == 'X')
                        {
                            digits = mag.ToString(conv == 'X' ? "X" : "x", CultureInfo.InvariantCulture);
                            if (alt && mag != 0)
                            {
                                prefix = conv == 'X' ? "0X" : "0x";
                            }
                        }
                        else if (conv == 'o')
                        {
                            digits = Convert.ToString((long)mag, 8);
                        }
                        else
                        {
                            digits = mag.ToString(CultureInfo.InvariantCulture);
                        }
                        if (prec >= 0)
                        {
                            while (digits.Length < prec)
                            {
                                digits = "0" + digits;
                            }
                        }
                        if (conv == 'o' && alt && (digits.Length == 0 || digits[0] != '0'))
                        {
                            digits = "0" + digits;
                        }
                        return pad_text(sign + prefix, digits, width, left, zero && prec < 0);
                    }
                    default:
                        // e f g with an int: the value as {fmt} prints a number
                        return Fmt.format_arg(spec_for(left, plus, space, alt, zero, width, prec, ""), arg);
                }
            }
            if (conv == 's' || conv == 'e' || conv == 'E' || conv == 'f' || conv == 'F' || conv == 'g' || conv == 'G')
            {
                bool nonfinite = false;
                if (arg is float)
                {
                    nonfinite = float.IsNaN((float)arg) || float.IsInfinity((float)arg);
                }
                else if (arg is double)
                {
                    nonfinite = double.IsNaN((double)arg) || double.IsInfinity((double)arg);
                }
                string type = conv == 's' ? "" : conv.ToString();
                if (nonfinite)
                {
                    // C printf pads inf and nan with spaces (right aligned unless '-')
                    string spec = (left ? "<" : ">") + (plus ? "+" : (space ? " " : "")) + (width > 0 ? width.ToString(CultureInfo.InvariantCulture) : "") + type;
                    return Fmt.format_arg(spec, arg);
                }
                return Fmt.format_arg(spec_for(left, plus, space, alt, zero, width, prec, type), arg);
            }
            // An integer conversion given a float: the float is taken as an int
            float fv = arg is double ? (float)(double)arg : (float)arg;
            int iv = (fv >= -2147483648.0f && fv < 2147483648.0f) ? (int)fv : int.MinValue;
            return format_one(iv, conv, left, plus, space, alt, zero, width, prec);
        }

        private static string spec_for(bool left, bool plus, bool space, bool alt, bool zero, int width, int prec, string type)
        {
            StringBuilder s = new StringBuilder();
            if (left)
            {
                s.Append('<');
            }
            if (plus)
            {
                s.Append('+');
            }
            else if (space)
            {
                s.Append(' ');
            }
            if (alt)
            {
                s.Append('#');
            }
            if (zero && !left)
            {
                s.Append('0');
            }
            if (width > 0)
            {
                s.Append(width.ToString(CultureInfo.InvariantCulture));
            }
            if (prec >= 0)
            {
                s.Append('.').Append(prec.ToString(CultureInfo.InvariantCulture));
            }
            s.Append(type);
            return s.ToString();
        }
    }
}
