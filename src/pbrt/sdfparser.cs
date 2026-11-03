// (not in pbrt-v4) sdfparser.cs
//
// The expression language of Shape "sdf" and Texture "sdf". Functions are called by name with numbers, strings and other
// expressions as arguments:
//
//   sphere(r)  box(hx, hy, hz [, rounding])  torus(R, r)  cylinder(r, halfHeight)  capsule(ax,ay,az, bx,by,bz, r)
//   cone(r, height)  plane(nx, ny, nz, offset)  ellipsoid(rx, ry, rz)  octahedron(s)  mandelbulb([power, iterations, bailout, safety])
//   menger([iterations])  gyroid(period, thickness)  mesh("file.ply" [, resolution, padding])
//   union(a, b)  intersect(a, b)  subtract(a, b)  smoothunion(a, b, k [, "poly"|"cubic"|"exp"])  smoothintersect(...)
//   smoothsubtract(...)  mix(a, b, t)  offset(a, d)  shell(a, thickness)
//   translate(a, x, y, z)  rotate(a, rx, ry, rz)  rotateaxis(a, ax, ay, az, degrees)  scale(a, s | sx, sy, sz)
//   twist(a, k)  bend(a, k)  repeat(a, px, py, pz [, count])  mirror(a, "x"|"y"|"z")  elongate(a, hx, hy, hz)
//   displace(a, amplitude, frequency [, octaves, roughness])
//
// and the operators a | b (union), a & b (intersection) and a - b (subtraction), with & binding tighter than - and | .
// The constant pi is known; angles are in degrees.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace pbrt
{

    // SdfParseContext Definition
    public sealed class SdfParseContext
    {
        // The largest distance from the origin of the object that the field is evaluated at (bounds the twist and bend)
        public double domainRadius = 2;
        // Called to make a field from a mesh file (name, resolution, padding); set by the code that knows how to read meshes
        public Func<string, int, double, string, SdfNode> meshLoader;
    }

    // SdfParser Definition
    public sealed class SdfParser
    {
        private enum TokenKind
        {
            Number,
            Identifier,
            String,
            Symbol,
            End
        }

        private struct Token
        {
            public TokenKind kind;
            public string text;
            public double number;
        }

        public static SdfNode Parse(string source, SdfParseContext context)
        {
            SdfParser parser = new SdfParser(source, context);
            SdfNode node = parser.ParseUnion();
            if (parser.tokens[parser.position].kind != TokenKind.End)
            {
                parser.Fail("unexpected \"" + parser.tokens[parser.position].text + "\"");
            }
            return node;
        }

        private SdfParser(string source, SdfParseContext context)
        {
            this.source = source;
            this.context = context;
            Tokenize();
        }

        private void Fail(string message)
        {
            pbrt.ErrorExit("sdf expression \"" + source + "\": " + message + ".");
        }

        private void Tokenize()
        {
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                if (char.IsWhiteSpace(c))
                {
                    ++i;
                }
                else if (char.IsDigit(c) || (c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
                {
                    int start = i;
                    while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.'))
                    {
                        ++i;
                    }
                    if (i < source.Length && (source[i] == 'e' || source[i] == 'E'))
                    {
                        int mark = i++;
                        if (i < source.Length && (source[i] == '+' || source[i] == '-'))
                        {
                            ++i;
                        }
                        if (i < source.Length && char.IsDigit(source[i]))
                        {
                            while (i < source.Length && char.IsDigit(source[i]))
                            {
                                ++i;
                            }
                        }
                        else
                        {
                            i = mark;
                        }
                    }
                    string text = source.Substring(start, i - start);
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    {
                        Fail("bad number \"" + text + "\"");
                    }
                    tokens.Add(new Token { kind = TokenKind.Number, text = text, number = value });
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                    {
                        ++i;
                    }
                    tokens.Add(new Token { kind = TokenKind.Identifier, text = source.Substring(start, i - start).ToLowerInvariant() });
                }
                else if (c == '"')
                {
                    int end = source.IndexOf('"', i + 1);
                    if (end < 0)
                    {
                        Fail("unterminated string");
                    }
                    tokens.Add(new Token { kind = TokenKind.String, text = source.Substring(i + 1, end - i - 1) });
                    i = end + 1;
                }
                else if ("(),|&-+".IndexOf(c) >= 0)
                {
                    tokens.Add(new Token { kind = TokenKind.Symbol, text = c.ToString() });
                    ++i;
                }
                else
                {
                    Fail("unexpected character '" + c + "'");
                }
            }
            tokens.Add(new Token { kind = TokenKind.End, text = "end of expression" });
        }

        private bool IsSymbol(string symbol)
        {
            return tokens[position].kind == TokenKind.Symbol && tokens[position].text == symbol;
        }

        private SdfNode ParseUnion()
        {
            SdfNode node = ParseSubtraction();
            while (IsSymbol("|") || IsSymbol("+"))
            {
                ++position;
                node = new SdfUnion(node, ParseSubtraction());
            }
            return node;
        }

        private SdfNode ParseSubtraction()
        {
            SdfNode node = ParseIntersection();
            while (IsSymbol("-"))
            {
                ++position;
                node = new SdfSubtraction(node, ParseIntersection());
            }
            return node;
        }

        private SdfNode ParseIntersection()
        {
            SdfNode node = ParsePrimary();
            while (IsSymbol("&"))
            {
                ++position;
                node = new SdfIntersection(node, ParsePrimary());
            }
            return node;
        }

        private SdfNode ParsePrimary()
        {
            if (IsSymbol("("))
            {
                ++position;
                SdfNode inner = ParseUnion();
                Expect(")");
                return inner;
            }
            if (tokens[position].kind != TokenKind.Identifier)
            {
                Fail("expected a function name, found \"" + tokens[position].text + "\"");
            }
            string name = tokens[position].text;
            ++position;
            List<object> arguments = new List<object>();
            if (IsSymbol("("))
            {
                ++position;
                if (!IsSymbol(")"))
                {
                    while (true)
                    {
                        arguments.Add(ParseArgument());
                        if (IsSymbol(","))
                        {
                            ++position;
                            continue;
                        }
                        break;
                    }
                }
                Expect(")");
            }
            return Build(name, arguments);
        }

        private void Expect(string symbol)
        {
            if (!IsSymbol(symbol))
            {
                Fail("expected \"" + symbol + "\", found \"" + tokens[position].text + "\"");
            }
            ++position;
        }

        // A number (with an optional sign, or the constant pi), a string, or an expression
        private object ParseArgument()
        {
            Token token = tokens[position];
            if (token.kind == TokenKind.String)
            {
                ++position;
                return token.text;
            }
            bool negative = false;
            int look = position;
            if (IsSymbol("-") || IsSymbol("+"))
            {
                negative = tokens[look].text == "-";
                ++look;
            }
            if (tokens[look].kind == TokenKind.Number || (tokens[look].kind == TokenKind.Identifier && tokens[look].text == "pi"))
            {
                double value = tokens[look].kind == TokenKind.Number ? tokens[look].number : Math.PI;
                position = look + 1;
                return negative ? -value : value;
            }
            return ParseUnion();
        }

        private static double Number(List<object> arguments, int index, string function, SdfParser parser)
        {
            if (index >= arguments.Count || !(arguments[index] is double))
            {
                parser.Fail(function + ": argument " + (index + 1) + " must be a number");
            }
            return (double)arguments[index];
        }

        private double Number(List<object> arguments, int index, double fallback, string function)
        {
            return index < arguments.Count ? Number(arguments, index, function, this) : fallback;
        }

        private SdfNode Node(List<object> arguments, int index, string function)
        {
            if (index >= arguments.Count || !(arguments[index] is SdfNode))
            {
                Fail(function + ": argument " + (index + 1) + " must be a shape");
            }
            return (SdfNode)arguments[index];
        }

        private string Text(List<object> arguments, int index, string function)
        {
            if (index >= arguments.Count || !(arguments[index] is string))
            {
                Fail(function + ": argument " + (index + 1) + " must be a string");
            }
            return (string)arguments[index];
        }

        private void Count(List<object> arguments, string function, int minimum, int maximum)
        {
            if (arguments.Count < minimum || arguments.Count > maximum)
            {
                Fail(function + " takes " + (minimum == maximum ? minimum.ToString() : minimum + " to " + maximum) + " arguments");
            }
        }

        private SdfBlendKind BlendKind(List<object> arguments, int index, string function)
        {
            if (index >= arguments.Count)
            {
                return SdfBlendKind.Polynomial;
            }
            string kind = Text(arguments, index, function);
            if (kind == "poly" || kind == "polynomial")
            {
                return SdfBlendKind.Polynomial;
            }
            if (kind == "cubic")
            {
                return SdfBlendKind.Cubic;
            }
            if (kind == "exp" || kind == "exponential")
            {
                return SdfBlendKind.Exponential;
            }
            Fail(function + ": unknown blend \"" + kind + "\"");
            return SdfBlendKind.Polynomial;
        }

        private static double[] Rotation(double rx, double ry, double rz)
        {
            // The field is rotated by rx about x, then ry about y, then rz about z; the matrix applied to the point is the inverse
            double a = rx * Math.PI / 180, b = ry * Math.PI / 180, c = rz * Math.PI / 180;
            double[] mx = { 1, 0, 0, 0, Math.Cos(a), -Math.Sin(a), 0, Math.Sin(a), Math.Cos(a) };
            double[] my = { Math.Cos(b), 0, Math.Sin(b), 0, 1, 0, -Math.Sin(b), 0, Math.Cos(b) };
            double[] mz = { Math.Cos(c), -Math.Sin(c), 0, Math.Sin(c), Math.Cos(c), 0, 0, 0, 1 };
            double[] forward = Multiply(mz, Multiply(my, mx));
            return Transpose(forward);
        }

        private static double[] Multiply(double[] a, double[] b)
        {
            double[] r = new double[9];
            for (int i = 0; i < 3; ++i)
            {
                for (int j = 0; j < 3; ++j)
                {
                    r[3 * i + j] = a[3 * i] * b[j] + a[3 * i + 1] * b[3 + j] + a[3 * i + 2] * b[6 + j];
                }
            }
            return r;
        }

        private static double[] Transpose(double[] a)
        {
            return new double[] { a[0], a[3], a[6], a[1], a[4], a[7], a[2], a[5], a[8] };
        }

        private static double[] AxisRotation(SdfVector axis, double degrees)
        {
            SdfVector k = axis.Normalized();
            double t = degrees * Math.PI / 180, c = Math.Cos(t), s = Math.Sin(t), v = 1 - c;
            double[] forward = {
                c + k.x * k.x * v, k.x * k.y * v - k.z * s, k.x * k.z * v + k.y * s,
                k.y * k.x * v + k.z * s, c + k.y * k.y * v, k.y * k.z * v - k.x * s,
                k.z * k.x * v - k.y * s, k.z * k.y * v + k.x * s, c + k.z * k.z * v };
            return Transpose(forward);
        }

        private SdfNode Build(string name, List<object> a)
        {
            double[] identity = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            switch (name)
            {
                case "sphere":
                    Count(a, name, 1, 1);
                    return new SdfSphere(Number(a, 0, name, this));
                case "box":
                    Count(a, name, 3, 4);
                    return new SdfBox(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this),
                                      Number(a, 3, 0, name));
                case "torus":
                    Count(a, name, 2, 2);
                    return new SdfTorus(Number(a, 0, name, this), Number(a, 1, name, this));
                case "cylinder":
                    Count(a, name, 2, 2);
                    return new SdfCylinder(Number(a, 0, name, this), Number(a, 1, name, this));
                case "capsule":
                    Count(a, name, 7, 7);
                    return new SdfCapsule(new SdfVector(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this)),
                                          new SdfVector(Number(a, 3, name, this), Number(a, 4, name, this), Number(a, 5, name, this)),
                                          Number(a, 6, name, this));
                case "cone":
                    Count(a, name, 2, 2);
                    return new SdfCone(Number(a, 0, name, this), Number(a, 1, name, this));
                case "plane":
                    Count(a, name, 4, 4);
                    return new SdfPlane(new SdfVector(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this)),
                                        Number(a, 3, name, this));
                case "ellipsoid":
                    Count(a, name, 3, 3);
                    return new SdfEllipsoid(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this));
                case "octahedron":
                    Count(a, name, 1, 1);
                    return new SdfOctahedron(Number(a, 0, name, this));
                case "mandelbulb":
                    Count(a, name, 0, 4);
                    return new SdfMandelbulb(Number(a, 0, 8, name), (int)Number(a, 1, 12, name), Number(a, 2, 4, name),
                                             Number(a, 3, 0.6, name));
                case "menger":
                    Count(a, name, 0, 1);
                    return new SdfMengerSponge((int)Number(a, 0, 4, name));
                case "gyroid":
                    Count(a, name, 2, 2);
                    return new SdfGyroid(Number(a, 0, name, this), Number(a, 1, name, this));
                case "mesh":
                    Count(a, name, 1, 4);
                    if (context.meshLoader == null)
                    {
                        Fail("mesh: no mesh loader available here");
                    }
                    return context.meshLoader(Text(a, 0, name), (int)Number(a, 1, 64, name), Number(a, 2, 0.05, name),
                                              a.Count > 3 ? Text(a, 3, name) : "auto");
                case "union":
                    Count(a, name, 2, 2);
                    return new SdfUnion(Node(a, 0, name), Node(a, 1, name));
                case "intersect":
                    Count(a, name, 2, 2);
                    return new SdfIntersection(Node(a, 0, name), Node(a, 1, name));
                case "subtract":
                    Count(a, name, 2, 2);
                    return new SdfSubtraction(Node(a, 0, name), Node(a, 1, name));
                case "smoothunion":
                    Count(a, name, 3, 4);
                    return new SdfSmoothCombine(Node(a, 0, name), Node(a, 1, name), Number(a, 2, name, this), BlendKind(a, 3, name), 0);
                case "smoothintersect":
                    Count(a, name, 3, 4);
                    return new SdfSmoothCombine(Node(a, 0, name), Node(a, 1, name), Number(a, 2, name, this), BlendKind(a, 3, name), 1);
                case "smoothsubtract":
                    Count(a, name, 3, 4);
                    return new SdfSmoothCombine(Node(a, 0, name), Node(a, 1, name), Number(a, 2, name, this), BlendKind(a, 3, name), 2);
                case "mix":
                    Count(a, name, 3, 3);
                    return new SdfMix(Node(a, 0, name), Node(a, 1, name), Number(a, 2, name, this));
                case "offset":
                    Count(a, name, 2, 2);
                    return new SdfOffset(Node(a, 0, name), Number(a, 1, name, this));
                case "shell":
                    Count(a, name, 2, 2);
                    return new SdfShell(Node(a, 0, name), Number(a, 1, name, this));
                case "translate":
                    Count(a, name, 4, 4);
                    // f(p - t) is f(p + (-t))
                    return new SdfAffine(Node(a, 0, name), identity,
                                         new SdfVector(-Number(a, 1, name, this), -Number(a, 2, name, this), -Number(a, 3, name, this)));
                case "rotate":
                    Count(a, name, 4, 4);
                    return new SdfAffine(Node(a, 0, name), Rotation(Number(a, 1, name, this), Number(a, 2, name, this),
                                                                     Number(a, 3, name, this)), new SdfVector(0, 0, 0));
                case "rotateaxis":
                    Count(a, name, 5, 5);
                    return new SdfAffine(Node(a, 0, name),
                                         AxisRotation(new SdfVector(Number(a, 1, name, this), Number(a, 2, name, this),
                                                                    Number(a, 3, name, this)), Number(a, 4, name, this)),
                                         new SdfVector(0, 0, 0));
                case "scale":
                    Count(a, name, 2, 4);
                    if (a.Count == 2)
                    {
                        double s = Number(a, 1, name, this);
                        return new SdfScale(Node(a, 0, name), s, s, s);
                    }
                    Count(a, name, 4, 4);
                    return new SdfScale(Node(a, 0, name), Number(a, 1, name, this), Number(a, 2, name, this), Number(a, 3, name, this));
                case "twist":
                    Count(a, name, 2, 2);
                    return new SdfTwist(Node(a, 0, name), Number(a, 1, name, this), context.domainRadius);
                case "bend":
                    Count(a, name, 2, 2);
                    return new SdfBend(Node(a, 0, name), Number(a, 1, name, this), context.domainRadius);
                case "repeat":
                    Count(a, name, 4, 5);
                    return new SdfRepeat(Node(a, 0, name),
                                         new SdfVector(Number(a, 1, name, this), Number(a, 2, name, this), Number(a, 3, name, this)),
                                         (int)Number(a, 4, 0, name));
                case "mirror":
                    Count(a, name, 2, 2);
                    string axis = Text(a, 1, name);
                    if (axis != "x" && axis != "y" && axis != "z")
                    {
                        Fail("mirror: the axis must be \"x\", \"y\" or \"z\"");
                    }
                    return new SdfMirror(Node(a, 0, name), axis == "x" ? 0 : (axis == "y" ? 1 : 2));
                case "elongate":
                    Count(a, name, 4, 4);
                    return new SdfElongate(Node(a, 0, name),
                                           new SdfVector(Number(a, 1, name, this), Number(a, 2, name, this), Number(a, 3, name, this)));
                case "displace":
                    Count(a, name, 3, 5);
                    return new SdfNoiseDisplacement(Node(a, 0, name), Number(a, 1, name, this), Number(a, 2, name, this),
                                                    (int)Number(a, 3, 1, name), Number(a, 4, 0.5, name));
                default:
                    Fail("unknown function \"" + name + "\"");
                    return null;
            }
        }

        private readonly string source;
        private readonly SdfParseContext context;
        private readonly List<Token> tokens = new List<Token>();
        private int position;
    }

}  // namespace pbrt
