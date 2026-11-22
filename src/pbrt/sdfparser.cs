// (not in pbrt-v4) sdfparser.cs
//
// The expression language of Shape "sdf" and Texture "sdf". Functions are called by name with numbers, strings and other
// expressions as arguments:
//
//   sphere(r)  box(hx, hy, hz [, rounding])  torus(R, r)  cylinder(r, halfHeight)  capsule(ax,ay,az, bx,by,bz, r)
//   cone(r, height)  plane(nx, ny, nz, offset)  ellipsoid(rx, ry, rz)  octahedron(s)  mandelbulb([power, iterations, bailout, safety])
//   menger([iterations])  gyroid(period, thickness)  mesh("file.ply" [, resolution, padding])
//   boxframe(hx, hy, hz, e)  cappedtorus(halfAngle, R, r)  link(halfLength, R, r)  hexprism(apothem, halfHeight)
//   triprism(size, halfHeight)  roundedcylinder(r, rounding, halfHeight)  cappedcone(halfHeight, r1, r2)  roundcone(r1, r2, h)
//   solidangle(halfAngle, r)  cutsphere(r, h)  cuthollowsphere(r, h, t)  deathstar(ra, rb, d)  rhombus(la, lb, halfHeight, rounding)
//   pyramid(h)  triangle(ax,ay,az, bx,by,bz, cx,cy,cz, t)  quad(a, b, c, d as 12 numbers, t)  infinitecylinder(r)
//   infinitecone(halfAngle)  torusnorm(R, r, ringPower, tubePower)  ballnorm(r, power)   (sdfprimitives.cs)
//   outlines and the solids over them (sdfcontour.cs):  path("M x y L x y A x y bulge Q cx cy x y Z")  circle(r)
//   rect(hx, hy [, rounding])  polygon(x0, y0, x1, y1, ...)  ngon(sides, apothem)  octagon(apothem)  cross(arm, halfWidth)
//   pie(halfAngle, r)  arc(halfAngle, r, halfWidth)  horseshoe(halfAngle, r, length, halfWidth)  vesica(r, d)
//   translate2(o, x, y)  rotate2(o, degrees)  offset2(o, d)  extrude(o, halfHeight)  revolve(o [, offset])  lathe(profile)
//   multiphase fields, one distance for each material (sdfphases.cs):  phase(a, m)  under(A, B)  over(A, B)
//   contact(a, b, ma, mb [, alpha])  contactsmooth(a, b, ma, mb, r [, alpha])  layer(A, m, t)  change(A, s, ma, mb)
//   remove(A, m)  shrink(A, r)  checker(ma, mb [, size])  merge(A [, m, ...]); intersect and subtract cut all the phases of A
//   proxy(a)   a behind its box: the distance to the box away from it, a only inside (not below an offset, a shell or a blend)
//   union(a, b)  intersect(a, b)  subtract(a, b)  xor(a, b)  smoothunion(a, b, k [, "poly"|"cubic"|"exp"])  smoothintersect(...)
//   smoothsubtract(...)  mix(a, b, t)  offset(a, d)  shell(a, thickness)
//   translate(a, x, y, z)  rotate(a, rx, ry, rz)  rotateaxis(a, ax, ay, az, degrees)  scale(a, s | sx, sy, sz)
//   twist(a, k)  bend(a, k)  repeat(a, px, py, pz [, count [, jitter [, seed]]])  mirror(a, "x"|"y"|"z")  elongate(a, hx, hy, hz)
//   displace(a, amplitude, frequency [, octaves, roughness])
//   inset  ink  clip  julia  metaballs   texdisplace  label  transfer  morph  translate_t  rotate_t  displace_t  mandelbulbt
//   (see sdfops.cs)
//
// and the operators a | b (union), a ^ b (exclusive or), a & b (intersection) and a - b (subtraction), with & binding tighter
// than - , and - tighter than | and ^ .
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
        public Func<string, string, SdfNode> vdbLoader;
        // No warnings: for a parse whose result is only asked for its extent
        public bool quiet = false;
        // The multiphase functions make the nodes of sdfphases.cs (one node for all the phases of an operator) instead of the
        // bundles of ordinary fields of sdfphasetree.cs
        public bool legacyPhases = false;
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

        // Warns that an operator reads the field on a side where it is only a bound of the distance
        private void Inexact(string operation, string side)
        {
            if (context.quiet)
            {
                return;
            }
            pbrt.Warning("sdf expression \"" + source + "\": " + operation + " reads the distance " + side +
                         ", where this field is a bound and not the exact distance (a union is not exact inside; an " +
                         "intersection, a subtraction and a blend are not exact outside). The bound is smaller than the distance, so " +
                         "where it is loose the wall of a shell is too thick and an inward offset or an inset takes too much.");
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
                else if ("(),|&-+^".IndexOf(c) >= 0)
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
            while (IsSymbol("|") || IsSymbol("+") || IsSymbol("^"))
            {
                bool exclusive = IsSymbol("^");
                ++position;
                node = Build(exclusive ? "xor" : "union", new List<object> { node, ParseSubtraction() });
            }
            return node;
        }

        private SdfNode ParseSubtraction()
        {
            SdfNode node = ParseIntersection();
            while (IsSymbol("-"))
            {
                ++position;
                node = Build("subtract", new List<object> { node, ParseIntersection() });
            }
            return node;
        }

        private SdfNode ParseIntersection()
        {
            SdfNode node = ParsePrimary();
            while (IsSymbol("&"))
            {
                ++position;
                node = Build("intersect", new List<object> { node, ParsePrimary() });
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

        // A multiphase field (sdfphases.cs) cut by an ordinary one keeps its phases
        private static SdfNode Intersect(SdfNode a, SdfNode b)
        {
            if (a is SdfPhaseNode && !(b is SdfPhaseNode))
            {
                return new SdfPhaseCut((SdfPhaseNode)a, b, false);
            }
            return new SdfIntersection(a, b);
        }

        private static SdfNode Subtract(SdfNode a, SdfNode b)
        {
            if (a is SdfPhaseNode && !(b is SdfPhaseNode))
            {
                return new SdfPhaseCut((SdfPhaseNode)a, b, true);
            }
            return new SdfSubtraction(a, b);
        }

        // A multiphase argument
        private SdfPhaseNode Tree(List<object> arguments, int index, string function)
        {
            if (index >= arguments.Count || !(arguments[index] is SdfPhaseNode))
            {
                Fail(function + ": argument " + (index + 1) + " must be a multiphase field (phase, under, over, contact, layer, ...)");
            }
            return (SdfPhaseNode)arguments[index];
        }

        // The number of a phase
        private int PhaseIndex(List<object> arguments, int index, string function)
        {
            double value = Number(arguments, index, function, this);
            if (value != Math.Floor(value) || value < 0 || value >= SdfPhaseNode.MaxPhases)
            {
                Fail(function + ": a phase is a whole number from 0 to " + (SdfPhaseNode.MaxPhases - 1));
            }
            return (int)value;
        }

        // An outline argument (sdfcontour.cs), closed unless a profile is asked for
        private SdfContour Outline(List<object> arguments, int index, string function, bool closed)
        {
            if (index >= arguments.Count || !(arguments[index] is SdfOutlineNode))
            {
                Fail(function + ": argument " + (index + 1) + " must be an outline (path, circle, rect, polygon, ...)");
            }
            SdfContour contour = ((SdfOutlineNode)arguments[index]).Contour();
            if (closed && !contour.IsClosed())
            {
                Fail(function + ": the outline has a path that is not closed (end it with Z)");
            }
            return contour;
        }

        private SdfNode ClosedOutline(SdfContour contour, string function)
        {
            if (!contour.IsClosed())
            {
                Fail(function + ": the outline is not closed");
            }
            return new SdfOutlineNode(contour);
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

        // Build for SdfPhaseOps: an ordinary function applied to a part of a multiphase field, without warnings if asked
        private SdfNode BuildPart(string name, List<object> a, bool quiet)
        {
            bool before = context.quiet;
            context.quiet = before || quiet;
            try
            {
                return Build(name, a);
            }
            finally
            {
                context.quiet = before;
            }
        }

        private SdfNode Build(string name, List<object> a)
        {
            if (!context.legacyPhases)
            {
                SdfNode phased = SdfPhaseOps.Call(name, a, BuildPart, Fail);
                if (phased != null)
                {
                    return phased;
                }
            }
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
                case "boxframe":
                    Count(a, name, 4, 4);
                    return new SdfBoxFrame(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this), Number(a, 3, name, this));
                case "cappedtorus":
                    Count(a, name, 3, 3);
                    return new SdfCappedTorus(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this));
                case "link":
                    Count(a, name, 3, 3);
                    return new SdfLink(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this));
                case "hexprism":
                    Count(a, name, 2, 2);
                    return new SdfHexPrism(Number(a, 0, name, this), Number(a, 1, name, this));
                case "triprism":
                    Count(a, name, 2, 2);
                    return new SdfTriPrism(Number(a, 0, name, this), Number(a, 1, name, this));
                case "roundedcylinder":
                    Count(a, name, 3, 3);
                    return new SdfRoundedCylinder(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this));
                case "cappedcone":
                    Count(a, name, 3, 3);
                    return new SdfCappedCone(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this));
                case "roundcone":
                    Count(a, name, 3, 3);
                    return new SdfRoundCone(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this));
                case "solidangle":
                    Count(a, name, 2, 2);
                    return new SdfSolidAngle(Number(a, 0, name, this), Number(a, 1, name, this));
                case "cutsphere":
                    Count(a, name, 2, 2);
                    return new SdfCutSphere(Number(a, 0, name, this), Number(a, 1, name, this));
                case "cuthollowsphere":
                    Count(a, name, 3, 3);
                    return new SdfCutHollowSphere(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this));
                case "deathstar":
                    Count(a, name, 3, 3);
                    return new SdfDeathStar(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this));
                case "rhombus":
                    Count(a, name, 4, 4);
                    return new SdfRhombus(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this), Number(a, 3, name, this));
                case "pyramid":
                    Count(a, name, 1, 1);
                    return new SdfPyramid(Number(a, 0, name, this));
                case "triangle":
                    Count(a, name, 10, 10);
                    return new SdfTriangle(new SdfVector(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this)),
                                           new SdfVector(Number(a, 3, name, this), Number(a, 4, name, this), Number(a, 5, name, this)),
                                           new SdfVector(Number(a, 6, name, this), Number(a, 7, name, this), Number(a, 8, name, this)), Number(a, 9, name, this));
                case "quad":
                    Count(a, name, 13, 13);
                    return new SdfQuad(new SdfVector(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this)),
                                       new SdfVector(Number(a, 3, name, this), Number(a, 4, name, this), Number(a, 5, name, this)),
                                       new SdfVector(Number(a, 6, name, this), Number(a, 7, name, this), Number(a, 8, name, this)),
                                       new SdfVector(Number(a, 9, name, this), Number(a, 10, name, this), Number(a, 11, name, this)), Number(a, 12, name, this));
                case "infinitecylinder":
                    Count(a, name, 1, 1);
                    return new SdfInfiniteCylinder(Number(a, 0, name, this));
                case "infinitecone":
                    Count(a, name, 1, 1);
                    return new SdfInfiniteCone(Number(a, 0, name, this));
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
                case "vdb":
                    Count(a, name, 1, 2);
                    if (context.vdbLoader == null)
                    {
                        Fail("vdb: no grid loader available here");
                    }
                    return context.vdbLoader(Text(a, 0, name), a.Count > 1 ? Text(a, 1, name) : "");
                case "path":
                    {
                        Count(a, name, 1, 1);
                        SdfContour contour = SdfShapes2D.Path(Text(a, 0, name), out string error);
                        if (contour == null)
                        {
                            Fail("path: " + error);
                        }
                        // A path may be left open: a profile for lathe
                        return new SdfOutlineNode(contour);
                    }
                case "circle":
                    Count(a, name, 1, 1);
                    return new SdfOutlineNode(SdfShapes2D.Circle(Number(a, 0, name, this)));
                case "rect":
                    Count(a, name, 2, 3);
                    return new SdfOutlineNode(SdfShapes2D.Rectangle(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, 0, name)));
                case "polygon":
                    {
                        if (a.Count < 6 || a.Count % 2 != 0)
                        {
                            Fail("polygon takes the x and y of three or more points");
                        }
                        double[] coordinates = new double[a.Count];
                        for (int i = 0; i < a.Count; ++i)
                        {
                            coordinates[i] = Number(a, i, name, this);
                        }
                        return ClosedOutline(SdfShapes2D.Polygon(coordinates), name);
                    }
                case "ngon":
                    Count(a, name, 2, 2);
                    if ((int)Number(a, 0, name, this) < 3)
                    {
                        Fail("ngon needs three or more sides");
                    }
                    return new SdfOutlineNode(SdfShapes2D.RegularPolygon((int)Number(a, 0, name, this), Number(a, 1, name, this)));
                case "octagon":
                    Count(a, name, 1, 1);
                    return new SdfOutlineNode(SdfShapes2D.RegularPolygon(8, Number(a, 0, name, this)));
                case "cross":
                    Count(a, name, 2, 2);
                    return new SdfOutlineNode(SdfShapes2D.Cross(Number(a, 0, name, this), Number(a, 1, name, this)));
                case "pie":
                    Count(a, name, 2, 2);
                    return new SdfOutlineNode(SdfShapes2D.Pie(Number(a, 0, name, this) * Math.PI / 180, Number(a, 1, name, this)));
                case "arc":
                    Count(a, name, 3, 3);
                    return new SdfOutlineNode(SdfShapes2D.Arc(Number(a, 0, name, this) * Math.PI / 180, Number(a, 1, name, this),
                                                              Number(a, 2, name, this)));
                case "horseshoe":
                    Count(a, name, 4, 4);
                    return new SdfOutlineNode(SdfShapes2D.Horseshoe(Number(a, 0, name, this) * Math.PI / 180, Number(a, 1, name, this),
                                                                    Number(a, 2, name, this), Number(a, 3, name, this)));
                case "vesica":
                    Count(a, name, 2, 2);
                    if (Number(a, 1, name, this) >= Number(a, 0, name, this))
                    {
                        Fail("vesica: the distance d has to be below the radius");
                    }
                    return new SdfOutlineNode(SdfShapes2D.Vesica(Number(a, 0, name, this), Number(a, 1, name, this)));
                case "translate2":
                    Count(a, name, 3, 3);
                    return new SdfOutlineNode(Outline(a, 0, name, false).Transformed(0, Number(a, 1, name, this), Number(a, 2, name, this)));
                case "rotate2":
                    Count(a, name, 2, 2);
                    return new SdfOutlineNode(Outline(a, 0, name, false).Transformed(Number(a, 1, name, this) * Math.PI / 180, 0, 0));
                case "offset2":
                    Count(a, name, 2, 2);
                    return new SdfOutlineNode(Outline(a, 0, name, false).Grown(Number(a, 1, name, this)));
                case "extrude":
                    Count(a, name, 2, 2);
                    return new SdfExtrude(Outline(a, 0, name, true), Number(a, 1, name, this));
                case "revolve":
                    Count(a, name, 1, 2);
                    return new SdfRevolve(Outline(a, 0, name, true), Number(a, 1, 0, name), false);
                case "lathe":
                    {
                        Count(a, name, 1, 1);
                        SdfContour whole = Outline(a, 0, name, false).Mirrored();
                        if (whole == null)
                        {
                            Fail("lathe takes a profile: one open path that starts and ends on the y axis (x = 0)");
                        }
                        return new SdfRevolve(whole, 0, true);
                    }
                case "torusnorm":
                    Count(a, name, 4, 4);
                    if (Number(a, 2, name, this) < 2 || Number(a, 3, name, this) < 2)
                    {
                        Fail("torusnorm: the powers have to be 2 or more");
                    }
                    return new SdfNormTorus(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this), Number(a, 3, name, this));
                case "ballnorm":
                    Count(a, name, 2, 2);
                    if (Number(a, 1, name, this) < 2)
                    {
                        Fail("ballnorm: the power has to be 2 or more");
                    }
                    return new SdfNormBall(Number(a, 0, name, this), Number(a, 1, name, this));
                case "phase":
                    Count(a, name, 2, 2);
                    return new SdfPhaseSet(Node(a, 0, name), PhaseIndex(a, 1, name));
                case "under":
                    Count(a, name, 2, 2);
                    return new SdfPhaseUnder(Tree(a, 0, name), Tree(a, 1, name));
                case "over":
                    Count(a, name, 2, 2);
                    return new SdfPhaseUnder(Tree(a, 1, name), Tree(a, 0, name));
                case "contact":
                    {
                        Count(a, name, 4, 5);
                        double alpha = Number(a, 4, 0.5, name);
                        if (alpha < 0 || alpha > 1 || PhaseIndex(a, 2, name) == PhaseIndex(a, 3, name))
                        {
                            Fail("contact: two different phases and an alpha from 0 to 1");
                        }
                        return new SdfPhaseContact(Node(a, 0, name), Node(a, 1, name), PhaseIndex(a, 2, name), PhaseIndex(a, 3, name), alpha, 0);
                    }
                case "contactsmooth":
                    {
                        Count(a, name, 5, 6);
                        double alpha = Number(a, 5, 0.5, name);
                        if (alpha < 0 || alpha > 1 || PhaseIndex(a, 2, name) == PhaseIndex(a, 3, name) || Number(a, 4, name, this) <= 0)
                        {
                            Fail("contactsmooth: two different phases, a radius above 0 and an alpha from 0 to 1");
                        }
                        return new SdfPhaseContact(Node(a, 0, name), Node(a, 1, name), PhaseIndex(a, 2, name), PhaseIndex(a, 3, name), alpha,
                                                   Number(a, 4, name, this));
                    }
                case "layer":
                    Count(a, name, 3, 3);
                    return new SdfPhaseLayer(Tree(a, 0, name), PhaseIndex(a, 1, name), Number(a, 2, name, this));
                case "change":
                    Count(a, name, 4, 4);
                    if (PhaseIndex(a, 2, name) == PhaseIndex(a, 3, name))
                    {
                        Fail("change: two different phases");
                    }
                    return new SdfPhaseChange(Tree(a, 0, name), Node(a, 1, name), PhaseIndex(a, 2, name), PhaseIndex(a, 3, name));
                case "remove":
                    Count(a, name, 2, 2);
                    return new SdfPhaseAdjust(Tree(a, 0, name), PhaseIndex(a, 1, name), 0);
                case "shrink":
                    Count(a, name, 2, 2);
                    return new SdfPhaseAdjust(Tree(a, 0, name), -1, Number(a, 1, name, this));
                case "checker":
                    Count(a, name, 2, 3);
                    if (PhaseIndex(a, 0, name) == PhaseIndex(a, 1, name) || Number(a, 2, 1, name) <= 0)
                    {
                        Fail("checker: two different phases and a size above 0");
                    }
                    return new SdfPhaseChecker(PhaseIndex(a, 0, name), PhaseIndex(a, 1, name), Number(a, 2, 1, name));
                case "merge":
                    {
                        if (a.Count < 1)
                        {
                            Fail("merge takes a multiphase field and the phases to keep");
                        }
                        bool[] chosen = new bool[SdfPhaseNode.MaxPhases];
                        for (int i = 0; i < SdfPhaseNode.MaxPhases; ++i)
                        {
                            chosen[i] = a.Count == 1;
                        }
                        for (int i = 1; i < a.Count; ++i)
                        {
                            chosen[PhaseIndex(a, i, name)] = true;
                        }
                        return new SdfPhaseMerge(Tree(a, 0, name), chosen);
                    }
                case "proxy":
                    {
                        Count(a, name, 1, 1);
                        SdfNode part = Node(a, 0, name);
                        if (!part.Bounds(out SdfVector lower, out SdfVector upper, out bool grows))
                        {
                            Fail("proxy: the field has no box of its own (it has a part without end)");
                        }
                        return new SdfProxy(part, lower, upper, 0);
                    }
                case "xor":
                    Count(a, name, 2, 2);
                    return new SdfXor(Node(a, 0, name), Node(a, 1, name));
                case "union":
                    Count(a, name, 2, 2);
                    return new SdfUnion(Node(a, 0, name), Node(a, 1, name));
                case "intersect":
                    Count(a, name, 2, 2);
                    return Intersect(Node(a, 0, name), Node(a, 1, name));
                case "subtract":
                    Count(a, name, 2, 2);
                    return Subtract(Node(a, 0, name), Node(a, 1, name));
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
                    Count(a, name, 3, 4);
                    if (a.Count > 2 && a[2] is SdfNode)
                    {
                        // The weight is a field (ramp, noise, ...); the optional fourth argument is the largest difference of the two
                        // fields in the domain, to tighten the Lipschitz bound
                        return new SdfMixField(Node(a, 0, name), Node(a, 1, name), Node(a, 2, name), context.domainRadius,
                                               Number(a, 3, 0, name));
                    }
                    Count(a, name, 3, 3);
                    return new SdfMix(Node(a, 0, name), Node(a, 1, name), Number(a, 2, name, this));
                case "ramp":
                    Count(a, name, 3, 3);
                    return new SdfRamp(Node(a, 0, name), Number(a, 1, name, this), Number(a, 2, name, this));
                case "noiseweight":
                    Count(a, name, 1, 3);
                    return new SdfNoiseWeight(Number(a, 0, name, this), (int)Number(a, 1, 3, name), Number(a, 2, 0.5, name));
                case "offset":
                    Count(a, name, 2, 2);
                    {
                        SdfNode child = Node(a, 0, name);
                        double amount = Number(a, 1, name, this);
                        if (amount < 0 && !child.IsExactInside())
                        {
                            Inexact("offset by a negative amount", "inside");
                        }
                        return new SdfOffset(child, amount);
                    }
                case "shell":
                    Count(a, name, 2, 2);
                    {
                        SdfNode child = Node(a, 0, name);
                        if (!child.IsExactInside())
                        {
                            Inexact("shell", "inside");
                        }
                        return new SdfShell(child, Number(a, 1, name, this));
                    }
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
                    Count(a, name, 4, 7);
                    return new SdfRepeat(Node(a, 0, name),
                                         new SdfVector(Number(a, 1, name, this), Number(a, 2, name, this), Number(a, 3, name, this)),
                                         (int)Number(a, 4, 0, name), Number(a, 5, 0, name), (int)Number(a, 6, 0, name));
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
                case "inset":
                    Count(a, name, 3, 4);
                    {
                        SdfNode region = Node(a, 1, name);
                        if (!region.IsExactInside())
                        {
                            Inexact("inset", "inside its second argument");
                        }
                        return new SdfInset(Node(a, 0, name), region, Number(a, 2, name, this), Number(a, 3, 0.05, name));
                    }
                case "ink":
                    Count(a, name, 3, 3);
                    return new SdfInk(Node(a, 0, name), Node(a, 1, name), Number(a, 2, name, this));
                case "clip":
                    Count(a, name, 2, 3);
                    return new SdfClip(Node(a, 0, name), Node(a, 1, name), Number(a, 2, 0.01, name));
                case "julia":
                    Count(a, name, 4, 8);
                    return new SdfJulia(Number(a, 0, name, this), Number(a, 1, name, this), Number(a, 2, name, this),
                                        Number(a, 3, name, this), (int)Number(a, 4, 12, name), Number(a, 5, 4, name),
                                        Number(a, 6, 0, name), Number(a, 7, 0.5, name));
                case "metaballs":
                    if (a.Count < 6 || (a.Count - 1) % 5 != 0)
                    {
                        Fail("metaballs takes a threshold and then groups of x, y, z, radius, strength");
                    }
                    double[] balls = new double[a.Count - 1];
                    for (int i = 1; i < a.Count; ++i)
                    {
                        balls[i - 1] = Number(a, i, name, this);
                    }
                    return new SdfMetaballs(Number(a, 0, name, this), balls);
                case "texdisplace":
                    Count(a, name, 3, 7);
                    return new SdfTextureDisplace(Node(a, 0, name),
                                                  new SdfHeight(Text(a, 1, name), a.Count > 5 ? Text(a, 5, name) : "xyz", Number(a, 6, 1, name)),
                                                  Number(a, 2, name, this), Number(a, 3, 0, name), Number(a, 4, 1, name), context.domainRadius);
                case "label":
                    Count(a, name, 2, 2);
                    return new SdfLabel(Node(a, 0, name), (int)Number(a, 1, name, this));
                case "transfer":
                    Count(a, name, 2, 2);
                    return new SdfTransfer(Node(a, 0, name), Node(a, 1, name));
                case "morph":
                    Count(a, name, 2, 2);
                    return new SdfMorph(Node(a, 0, name), Node(a, 1, name));
                case "translate_t":
                    Count(a, name, 4, 4);
                    return new SdfTimeTranslate(Node(a, 0, name),
                                                new SdfVector(Number(a, 1, name, this), Number(a, 2, name, this), Number(a, 3, name, this)));
                case "rotate_t":
                    Count(a, name, 4, 4);
                    return new SdfTimeRotate(Node(a, 0, name), Number(a, 1, name, this), Number(a, 2, name, this), Number(a, 3, name, this));
                case "displace_t":
                    Count(a, name, 4, 6);
                    return new SdfTimeDisplacement(Node(a, 0, name), Number(a, 1, name, this), Number(a, 2, name, this),
                                                   Number(a, 3, name, this), (int)Number(a, 4, 1, name), Number(a, 5, 0.5, name));
                case "mandelbulbt":
                    Count(a, name, 2, 5);
                    return new SdfTimeMandelbulb(Number(a, 0, name, this), Number(a, 1, name, this), (int)Number(a, 2, 12, name),
                                                 Number(a, 3, 4, name), Number(a, 4, 0.6, name));
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
