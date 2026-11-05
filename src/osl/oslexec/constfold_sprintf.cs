// OSL port: OIIO::Strutil::sprintf, the printf-style formatting (a typesafe tinyformat) that the format() folder of constfold.cpp uses to build the
// constant string at optimization time.
//
// The conventions are those of tinyformat, which formats through a C++ ostream rather than through the C library:
//   * the conversion character selects the stream flags (d i u: decimal, o: octal, x X: hex, e E: scientific, f F: fixed, g G: general, c: character,
//     s: the value as the stream prints it), the argument type selects how the value is printed;
//   * a float given to d i u o x X c is converted to int first, an int given to e f g is printed as an int (the stream ignores the float flags and the
//     precision for integers), a float given to s is printed in the stream's default (%g) format;
//   * a precision truncates a string given to %s;
//   * the flags are '-', '+', ' ', '#' and '0'; width and precision may be '*' (taken from an int argument); length modifiers are skipped.
// Arguments are int, float and string.

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

                // flags
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

                // width
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

                // precision
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

                // length modifiers are ignored
                while (i < fmt.Length && (fmt[i] == 'h' || fmt[i] == 'l' || fmt[i] == 'L' || fmt[i] == 'j' || fmt[i] == 'z' || fmt[i] == 't' || fmt[i] == 'q'))
                {
                    ++i;
                }

                if (i >= fmt.Length)
                {
                    break;  // a format specification that ends the string: nothing to convert
                }
                char conv = fmt[i];
                ++i;
                if (argnum >= args.Length)
                {
                    break;  // not enough arguments
                }
                object arg = args[argnum++];
                o.Append(format_one(arg, conv, left, plus, space, alt, zero, width, prec));
            }
            return o.ToString();
        }

        private static string format_one(object arg, char conv, bool left, bool plus, bool space, bool alt, bool zero, int width, int prec)
        {
            string body;  // the text of the value without the sign
            string sign = "";
            bool numeric = true;
            bool upper = char.IsUpper(conv) && conv != 'c';
            if (arg is string)
            {
                numeric = false;
                body = (string)arg;
                if (conv == 's' && prec >= 0 && body.Length > prec)
                {
                    body = body.Substring(0, prec);
                }
            }
            else
            {
                bool isint = arg is int;
                // Integer conversions take a float as an int
                if (!isint && (conv == 'd' || conv == 'i' || conv == 'u' || conv == 'o' || conv == 'x' || conv == 'X' || conv == 'c'))
                {
                    float fv = (float)arg;
                    arg = (fv >= -2147483648.0f && fv < 2147483648.0f) ? (int)fv : int.MinValue;
                    isint = true;
                }
                if (isint)
                {
                    int v = (int)arg;
                    if (conv == 'c')
                    {
                        numeric = false;
                        body = ((char)(byte)v).ToString();
                    }
                    else if (conv == 'x' || conv == 'X')
                    {
                        body = ((uint)v).ToString(conv == 'X' ? "X" : "x", CultureInfo.InvariantCulture);
                        if (alt && v != 0)
                        {
                            body = (conv == 'X' ? "0X" : "0x") + body;
                        }
                        plus = false;
                        space = false;
                    }
                    else if (conv == 'o')
                    {
                        body = Convert.ToString(v, 8);
                        if (alt && v != 0)
                        {
                            body = "0" + body;
                        }
                        plus = false;
                        space = false;
                    }
                    else
                    {
                        // d i u s and the float conversions of an int: the stream prints the signed decimal value
                        if (v < 0)
                        {
                            sign = "-";
                            body = ((long)(-(long)v)).ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            body = v.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                }
                else
                {
                    float fv = (float)arg;
                    double v = fv;
                    bool neg = fv < 0 || (fv == 0 && float.IsNegative(fv));
                    double a = Math.Abs(v);
                    if (float.IsNaN(fv))
                    {
                        body = upper ? "NAN" : "nan";
                        neg = false;
                        zero = false;
                    }
                    else if (float.IsInfinity(fv))
                    {
                        body = upper ? "INF" : "inf";
                        zero = false;
                    }
                    else if (conv == 'e' || conv == 'E')
                    {
                        body = Fmt.format_e(a, prec >= 0 ? prec : 6, upper);
                        if (alt && prec == 0)
                        {
                            body = body.Insert(body.IndexOfAny(new char[] { 'e', 'E' }), ".");
                        }
                    }
                    else if (conv == 'f' || conv == 'F')
                    {
                        body = a.ToString("F" + (prec >= 0 ? prec : 6), CultureInfo.InvariantCulture);
                        if (alt && prec == 0)
                        {
                            body += ".";
                        }
                    }
                    else
                    {
                        // g G and s: the stream's default float format, which is %g with the precision (6 if none is given)
                        body = Fmt.format_g(a, prec >= 0 ? prec : 6, upper, alt && (conv == 'g' || conv == 'G'));
                    }
                    if (neg)
                    {
                        sign = "-";
                    }
                }
                if (sign.Length == 0 && numeric)
                {
                    if (plus)
                    {
                        sign = "+";
                    }
                    else if (space)
                    {
                        sign = " ";
                    }
                }
            }

            int len = sign.Length + body.Length;
            if (len >= width)
            {
                return sign + body;
            }
            int pad = width - len;
            if (left)
            {
                return sign + body + new string(' ', pad);
            }
            if (zero && numeric)
            {
                // internal padding: zeros after the sign (and after the 0x of a hex number)
                int prefixlen = 0;
                if (body.StartsWith("0x", StringComparison.Ordinal) || body.StartsWith("0X", StringComparison.Ordinal))
                {
                    prefixlen = 2;
                }
                return sign + body.Substring(0, prefixlen) + new string('0', pad) + body.Substring(prefixlen);
            }
            return new string(' ', pad) + sign + body;
        }
    }
}
