// (not in pbrt-v4) shellexpression.cs
//
// A small scalar expression language in the texture coordinates (u, v), evaluated both at a point and over a rectangle of (u, v) with
// interval arithmetic. It is the procedural displacement of the "displacedmesh" shape: the traversal needs the range of the height over
// a node of the quadtree, which for a procedural function comes from the interval evaluation, so no grid of heights is stored and the
// resolution is limited by the depth of the quadtree only (Heidrich and Seidel, "Ray-tracing procedural displacement shaders",
// Graphics Interface 1998, bound the displacement shader the same way).
//
// Grammar:  expression := term (("+" | "-") term)*,  term := unary (("*" | "/") unary)*,  unary := "-" unary | primary,
//           primary := number | "u" | "v" | "pi" | name "(" expression ("," expression)* ")" | "(" expression ")".
// Functions: sin(x), cos(x), abs(x), sqrt(x) (of max(x, 0)), exp(x), floor(x), fract(x), min(a, b), max(a, b), clamp(x, a, b),
//            mix(a, b, t), smoothstep(a, b, x), pow(x, e) (e a number), noise(x, y), fbm(x, y, octaves, omega),
//            turb(x, y, octaves, omega). noise() is the Perlin noise of pbrt on the plane z = 0.5, fbm() and turb() sum octaves of it
//            with the frequency growing by 1.99 and the weight by omega, as FBm() does.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace pbrt
{

    // ShellExpression Definition
    public sealed class ShellExpression
    {
        // ShellExpression Public Methods
        public static ShellExpression Parse(string source)
        {
            ShellExpressionParser parser = new ShellExpressionParser(source);
            return new ShellExpression(parser.ParseAll());
        }

        public double Evaluate(double u, double v)
        {
            return root.Evaluate(u, v);
        }

        // The range of the expression over [u0, u1] x [v0, v1]: every value of Evaluate() in the rectangle is within [low, high]
        public void Bounds(double u0, double u1, double v0, double v1, out double low, out double high)
        {
            root.Bounds(u0, u1, v0, v1, out low, out high);
            double widen = 1e-9 * (1 + Math.Max(Math.Abs(low), Math.Abs(high)));
            low -= widen;
            high += widen;
        }

        // ShellExpression Private Methods
        private ShellExpression(ShellExpressionNode root)
        {
            this.root = root;
        }

        // ShellExpression Private Members
        private readonly ShellExpressionNode root;
    }

    // ShellExpressionNode Definition
    internal abstract class ShellExpressionNode
    {
        public abstract double Evaluate(double u, double v);

        public abstract void Bounds(double u0, double u1, double v0, double v1, out double low, out double high);
    }

    internal sealed class ShellConstantNode : ShellExpressionNode
    {
        public ShellConstantNode(double value)
        {
            this.value = value;
        }

        public override double Evaluate(double u, double v)
        {
            return value;
        }

        public override void Bounds(double u0, double u1, double v0, double v1, out double low, out double high)
        {
            low = value;
            high = value;
        }

        public readonly double value;
    }

    internal sealed class ShellVariableNode : ShellExpressionNode
    {
        public ShellVariableNode(bool isU)
        {
            this.isU = isU;
        }

        public override double Evaluate(double u, double v)
        {
            return isU ? u : v;
        }

        public override void Bounds(double u0, double u1, double v0, double v1, out double low, out double high)
        {
            low = isU ? u0 : v0;
            high = isU ? u1 : v1;
        }

        private readonly bool isU;
    }

    internal enum ShellOperation
    {
        Negate,
        Add,
        Subtract,
        Multiply,
        Divide,
        Sine,
        Cosine,
        Absolute,
        SquareRoot,
        Exponential,
        Floor,
        Fract,
        Minimum,
        Maximum,
        Clamp,
        Mix,
        SmoothStep,
        Power,
        Noise,
        FractalBrownianMotion,
        Turbulence
    }

    // ShellOperationNode Definition: every operation of the language, with the arguments as children
    internal sealed class ShellOperationNode : ShellExpressionNode
    {
        public ShellOperationNode(ShellOperation operation, ShellExpressionNode[] arguments)
        {
            this.operation = operation;
            this.arguments = arguments;
            if (operation == ShellOperation.Power)
            {
                exponent = ((ShellConstantNode)arguments[1]).value;
            }
            if (operation == ShellOperation.FractalBrownianMotion || operation == ShellOperation.Turbulence)
            {
                octaves = (int)Math.Round(((ShellConstantNode)arguments[2]).value);
                omega = ((ShellConstantNode)arguments[3]).value;
            }
        }

        public override double Evaluate(double u, double v)
        {
            switch (operation)
            {
                case ShellOperation.Negate:
                    return -arguments[0].Evaluate(u, v);
                case ShellOperation.Add:
                    return arguments[0].Evaluate(u, v) + arguments[1].Evaluate(u, v);
                case ShellOperation.Subtract:
                    return arguments[0].Evaluate(u, v) - arguments[1].Evaluate(u, v);
                case ShellOperation.Multiply:
                    return arguments[0].Evaluate(u, v) * arguments[1].Evaluate(u, v);
                case ShellOperation.Divide:
                    return arguments[0].Evaluate(u, v) / arguments[1].Evaluate(u, v);
                case ShellOperation.Sine:
                    return Math.Sin(arguments[0].Evaluate(u, v));
                case ShellOperation.Cosine:
                    return Math.Cos(arguments[0].Evaluate(u, v));
                case ShellOperation.Absolute:
                    return Math.Abs(arguments[0].Evaluate(u, v));
                case ShellOperation.SquareRoot:
                    return Math.Sqrt(Math.Max(arguments[0].Evaluate(u, v), 0));
                case ShellOperation.Exponential:
                    return Math.Exp(arguments[0].Evaluate(u, v));
                case ShellOperation.Floor:
                    return Math.Floor(arguments[0].Evaluate(u, v));
                case ShellOperation.Fract:
                {
                    double x = arguments[0].Evaluate(u, v);
                    return x - Math.Floor(x);
                }
                case ShellOperation.Minimum:
                    return Math.Min(arguments[0].Evaluate(u, v), arguments[1].Evaluate(u, v));
                case ShellOperation.Maximum:
                    return Math.Max(arguments[0].Evaluate(u, v), arguments[1].Evaluate(u, v));
                case ShellOperation.Clamp:
                    return Math.Min(Math.Max(arguments[0].Evaluate(u, v), arguments[1].Evaluate(u, v)), arguments[2].Evaluate(u, v));
                case ShellOperation.Mix:
                {
                    double t = arguments[2].Evaluate(u, v);
                    return arguments[0].Evaluate(u, v) * (1 - t) + arguments[1].Evaluate(u, v) * t;
                }
                case ShellOperation.SmoothStep:
                {
                    double edge0 = arguments[0].Evaluate(u, v), edge1 = arguments[1].Evaluate(u, v);
                    double t = Math.Min(Math.Max((arguments[2].Evaluate(u, v) - edge0) / (edge1 - edge0), 0), 1);
                    return t * t * (3 - 2 * t);
                }
                case ShellOperation.Power:
                    return Power(arguments[0].Evaluate(u, v), exponent);
                case ShellOperation.Noise:
                    return pbrt.Noise(arguments[0].Evaluate(u, v), arguments[1].Evaluate(u, v), 0.5);
                default:
                {
                    double x = arguments[0].Evaluate(u, v), y = arguments[1].Evaluate(u, v);
                    double sum = 0, weight = 1, frequency = 1;
                    for (int octave = 0; octave < octaves; ++octave)
                    {
                        double n = pbrt.Noise(frequency * x, frequency * y, 0.5);
                        sum += weight * (operation == ShellOperation.Turbulence ? Math.Abs(n) : n);
                        frequency *= 1.99;
                        weight *= omega;
                    }
                    return sum;
                }
            }
        }

        public override void Bounds(double u0, double u1, double v0, double v1, out double low, out double high)
        {
            double a0 = 0, a1 = 0, b0 = 0, b1 = 0, c0 = 0, c1 = 0;
            arguments[0].Bounds(u0, u1, v0, v1, out a0, out a1);
            if (arguments.Length > 1 && operation != ShellOperation.Power)
            {
                arguments[1].Bounds(u0, u1, v0, v1, out b0, out b1);
            }
            if (arguments.Length > 2 && operation != ShellOperation.FractalBrownianMotion && operation != ShellOperation.Turbulence)
            {
                arguments[2].Bounds(u0, u1, v0, v1, out c0, out c1);
            }
            switch (operation)
            {
                case ShellOperation.Negate:
                    low = -a1;
                    high = -a0;
                    return;
                case ShellOperation.Add:
                    low = a0 + b0;
                    high = a1 + b1;
                    return;
                case ShellOperation.Subtract:
                    low = a0 - b1;
                    high = a1 - b0;
                    return;
                case ShellOperation.Multiply:
                    ShellInterval.Multiply(a0, a1, b0, b1, out low, out high);
                    return;
                case ShellOperation.Divide:
                    ShellInterval.Divide(a0, a1, b0, b1, out low, out high);
                    return;
                case ShellOperation.Sine:
                    ShellInterval.Sine(a0, a1, out low, out high);
                    return;
                case ShellOperation.Cosine:
                    ShellInterval.Sine(a0 + 0.5 * Math.PI, a1 + 0.5 * Math.PI, out low, out high);
                    return;
                case ShellOperation.Absolute:
                    ShellInterval.Absolute(a0, a1, out low, out high);
                    return;
                case ShellOperation.SquareRoot:
                    low = Math.Sqrt(Math.Max(a0, 0));
                    high = Math.Sqrt(Math.Max(a1, 0));
                    return;
                case ShellOperation.Exponential:
                    low = Math.Exp(a0);
                    high = Math.Exp(a1);
                    return;
                case ShellOperation.Floor:
                    low = Math.Floor(a0);
                    high = Math.Floor(a1);
                    return;
                case ShellOperation.Fract:
                    if (Math.Floor(a0) == Math.Floor(a1))
                    {
                        low = a0 - Math.Floor(a0);
                        high = a1 - Math.Floor(a1);
                    }
                    else
                    {
                        low = 0;
                        high = 1;
                    }
                    return;
                case ShellOperation.Minimum:
                    low = Math.Min(a0, b0);
                    high = Math.Min(a1, b1);
                    return;
                case ShellOperation.Maximum:
                    low = Math.Max(a0, b0);
                    high = Math.Max(a1, b1);
                    return;
                case ShellOperation.Clamp:
                {
                    // min(max(x, a), b)
                    double maximumLow = Math.Max(a0, b0), maximumHigh = Math.Max(a1, b1);
                    low = Math.Min(maximumLow, c0);
                    high = Math.Min(maximumHigh, c1);
                    return;
                }
                case ShellOperation.Mix:
                {
                    // a (1 - t) + b t
                    double oneMinusT0 = 1 - c1, oneMinusT1 = 1 - c0;
                    ShellInterval.Multiply(a0, a1, oneMinusT0, oneMinusT1, out double p0, out double p1);
                    ShellInterval.Multiply(b0, b1, c0, c1, out double q0, out double q1);
                    low = p0 + q0;
                    high = p1 + q1;
                    return;
                }
                case ShellOperation.SmoothStep:
                {
                    // t = clamp((x - edge0) / (edge1 - edge0), 0, 1), then the increasing function t^2 (3 - 2t)
                    ShellInterval.Divide(c0 - a1, c1 - a0, b0 - a1, b1 - a0, out double t0, out double t1);
                    t0 = Math.Min(Math.Max(t0, 0), 1);
                    t1 = Math.Min(Math.Max(t1, 0), 1);
                    low = t0 * t0 * (3 - 2 * t0);
                    high = t1 * t1 * (3 - 2 * t1);
                    return;
                }
                case ShellOperation.Power:
                    ShellInterval.Power(a0, a1, exponent, out low, out high);
                    return;
                case ShellOperation.Noise:
                    NoiseBounds(1, a0, a1, b0, b1, out low, out high);
                    return;
                default:
                {
                    arguments[1].Bounds(u0, u1, v0, v1, out b0, out b1);
                    low = 0;
                    high = 0;
                    double weight = 1, frequency = 1;
                    for (int octave = 0; octave < octaves; ++octave)
                    {
                        NoiseBounds(frequency, a0, a1, b0, b1, out double termLow, out double termHigh);
                        if (operation == ShellOperation.Turbulence)
                        {
                            ShellInterval.Absolute(termLow, termHigh, out termLow, out termHigh);
                        }
                        if (weight >= 0)
                        {
                            low += weight * termLow;
                            high += weight * termHigh;
                        }
                        else
                        {
                            low += weight * termHigh;
                            high += weight * termLow;
                        }
                        frequency *= 1.99;
                        weight *= omega;
                    }
                    return;
                }
            }
        }

        private static double Power(double x, double exponent)
        {
            if (exponent == Math.Round(exponent) && Math.Abs(exponent) < 64)
            {
                return Math.Pow(x, exponent);
            }
            return Math.Pow(Math.Max(x, 0), exponent);
        }

        // The bounds of Noise() over the rectangle [x0, x1] x [y0, y1] scaled by the frequency, on the plane z = 0.5. The box is widened a
        // little before it is rounded to single precision, which NoiseBounds() works in.
        private static void NoiseBounds(double frequency, double x0, double x1, double y0, double y1, out double low, out double high)
        {
            double sx0 = frequency * x0, sx1 = frequency * x1, sy0 = frequency * y0, sy1 = frequency * y1;
            double padX = 1e-6 * (1 + Math.Max(Math.Abs(sx0), Math.Abs(sx1)));
            double padY = 1e-6 * (1 + Math.Max(Math.Abs(sy0), Math.Abs(sy1)));
            Bounds3f box = new Bounds3f(new Point3f((Float)(sx0 - padX), (Float)(sy0 - padY), 0.5f),
                                        new Point3f((Float)(sx1 + padX), (Float)(sy1 + padY), 0.5f));
            pbrt.NoiseBounds(box, out Float noiseLow, out Float noiseHigh);
            low = noiseLow;
            high = noiseHigh;
        }

        private readonly ShellOperation operation;
        private readonly ShellExpressionNode[] arguments;
        private readonly double exponent, omega;
        private readonly int octaves;
    }

    // ShellInterval Definition: interval arithmetic helpers
    internal static class ShellInterval
    {
        public static void Multiply(double a0, double a1, double b0, double b1, out double low, out double high)
        {
            double p0 = Product(a0, b0), p1 = Product(a0, b1), p2 = Product(a1, b0), p3 = Product(a1, b1);
            low = Math.Min(Math.Min(p0, p1), Math.Min(p2, p3));
            high = Math.Max(Math.Max(p0, p1), Math.Max(p2, p3));
        }

        public static void Divide(double a0, double a1, double b0, double b1, out double low, out double high)
        {
            if (b0 <= 0 && b1 >= 0)
            {
                low = double.NegativeInfinity;
                high = double.PositiveInfinity;
                return;
            }
            Multiply(a0, a1, 1 / b1, 1 / b0, out low, out high);
        }

        public static void Absolute(double a0, double a1, out double low, out double high)
        {
            if (a0 >= 0)
            {
                low = a0;
                high = a1;
            }
            else if (a1 <= 0)
            {
                low = -a1;
                high = -a0;
            }
            else
            {
                low = 0;
                high = Math.Max(-a0, a1);
            }
        }

        public static void Sine(double a, double b, out double low, out double high)
        {
            if (double.IsInfinity(a) || double.IsInfinity(b) || b - a >= 2 * Math.PI)
            {
                low = -1;
                high = 1;
                return;
            }
            double sa = Math.Sin(a), sb = Math.Sin(b);
            low = Math.Min(sa, sb);
            high = Math.Max(sa, sb);
            double peak = Math.Ceiling((a - 0.5 * Math.PI) / (2 * Math.PI));
            if (0.5 * Math.PI + 2 * Math.PI * peak <= b)
            {
                high = 1;
            }
            double valley = Math.Ceiling((a + 0.5 * Math.PI) / (2 * Math.PI));
            if (-0.5 * Math.PI + 2 * Math.PI * valley <= b)
            {
                low = -1;
            }
        }

        // x^exponent over [a0, a1]: an integer exponent is exact in the sign cases, any other exponent is of max(x, 0)
        public static void Power(double a0, double a1, double exponent, out double low, out double high)
        {
            if (exponent == Math.Round(exponent) && Math.Abs(exponent) < 64)
            {
                int n = (int)exponent;
                if (n == 0)
                {
                    low = 1;
                    high = 1;
                    return;
                }
                int count = Math.Abs(n);
                double p0 = Math.Pow(a0, count), p1 = Math.Pow(a1, count);
                double l, h;
                if ((count & 1) == 1)
                {
                    l = p0;
                    h = p1;
                }
                else if (a0 >= 0)
                {
                    l = p0;
                    h = p1;
                }
                else if (a1 <= 0)
                {
                    l = p1;
                    h = p0;
                }
                else
                {
                    l = 0;
                    h = Math.Max(p0, p1);
                }
                if (n > 0)
                {
                    low = l;
                    high = h;
                }
                else
                {
                    Divide(1, 1, l, h, out low, out high);
                }
                return;
            }
            double c0 = Math.Max(a0, 0), c1 = Math.Max(a1, 0);
            if (exponent > 0)
            {
                low = Math.Pow(c0, exponent);
                high = Math.Pow(c1, exponent);
            }
            else if (c0 > 0)
            {
                low = Math.Pow(c1, exponent);
                high = Math.Pow(c0, exponent);
            }
            else
            {
                low = Math.Pow(c1, exponent);
                high = double.PositiveInfinity;
            }
        }

        // A product in which 0 * infinity is 0, as it is for an interval endpoint
        private static double Product(double a, double b)
        {
            if (a == 0 || b == 0)
            {
                return 0;
            }
            return a * b;
        }
    }

    // ShellExpressionParser Definition
    internal sealed class ShellExpressionParser
    {
        public ShellExpressionParser(string source)
        {
            this.source = source;
        }

        public ShellExpressionNode ParseAll()
        {
            ShellExpressionNode node = ParseSum();
            SkipSpace();
            if (position != source.Length)
            {
                throw new FormatException("unexpected \"" + source[position] + "\" at position " + position + " of \"" + source + "\"");
            }
            return node;
        }

        private ShellExpressionNode ParseSum()
        {
            ShellExpressionNode node = ParseProduct();
            while (true)
            {
                SkipSpace();
                if (Accept('+'))
                {
                    node = new ShellOperationNode(ShellOperation.Add, new ShellExpressionNode[] { node, ParseProduct() });
                }
                else if (Accept('-'))
                {
                    node = new ShellOperationNode(ShellOperation.Subtract, new ShellExpressionNode[] { node, ParseProduct() });
                }
                else
                {
                    return node;
                }
            }
        }

        private ShellExpressionNode ParseProduct()
        {
            ShellExpressionNode node = ParseUnary();
            while (true)
            {
                SkipSpace();
                if (Accept('*'))
                {
                    node = new ShellOperationNode(ShellOperation.Multiply, new ShellExpressionNode[] { node, ParseUnary() });
                }
                else if (Accept('/'))
                {
                    node = new ShellOperationNode(ShellOperation.Divide, new ShellExpressionNode[] { node, ParseUnary() });
                }
                else
                {
                    return node;
                }
            }
        }

        private ShellExpressionNode ParseUnary()
        {
            SkipSpace();
            if (Accept('-'))
            {
                ShellExpressionNode operand = ParseUnary();
                if (operand is ShellConstantNode constant)
                {
                    return new ShellConstantNode(-constant.value);
                }
                return new ShellOperationNode(ShellOperation.Negate, new ShellExpressionNode[] { operand });
            }
            return ParsePrimary();
        }

        private ShellExpressionNode ParsePrimary()
        {
            SkipSpace();
            if (position >= source.Length)
            {
                throw new FormatException("unexpected end of \"" + source + "\"");
            }
            char c = source[position];
            if (c == '(')
            {
                ++position;
                ShellExpressionNode inner = ParseSum();
                SkipSpace();
                if (!Accept(')'))
                {
                    throw new FormatException("missing \")\" at position " + position + " of \"" + source + "\"");
                }
                return inner;
            }
            if (char.IsDigit(c) || c == '.')
            {
                int start = position;
                while (position < source.Length && (char.IsDigit(source[position]) || source[position] == '.'))
                {
                    ++position;
                }
                if (position < source.Length && (source[position] == 'e' || source[position] == 'E'))
                {
                    int exponentStart = position;
                    ++position;
                    if (position < source.Length && (source[position] == '+' || source[position] == '-'))
                    {
                        ++position;
                    }
                    if (position < source.Length && char.IsDigit(source[position]))
                    {
                        while (position < source.Length && char.IsDigit(source[position]))
                        {
                            ++position;
                        }
                    }
                    else
                    {
                        position = exponentStart;
                    }
                }
                if (!double.TryParse(source.Substring(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture,
                                     out double value))
                {
                    throw new FormatException("bad number at position " + start + " of \"" + source + "\"");
                }
                return new ShellConstantNode(value);
            }
            if (char.IsLetter(c))
            {
                int start = position;
                while (position < source.Length && (char.IsLetterOrDigit(source[position]) || source[position] == '_'))
                {
                    ++position;
                }
                string name = source.Substring(start, position - start);
                SkipSpace();
                if (!Accept('('))
                {
                    if (name == "u")
                    {
                        return new ShellVariableNode(true);
                    }
                    if (name == "v")
                    {
                        return new ShellVariableNode(false);
                    }
                    if (name == "pi")
                    {
                        return new ShellConstantNode(Math.PI);
                    }
                    throw new FormatException("unknown name \"" + name + "\" in \"" + source + "\"");
                }
                List<ShellExpressionNode> arguments = new List<ShellExpressionNode>();
                SkipSpace();
                if (!Accept(')'))
                {
                    while (true)
                    {
                        arguments.Add(ParseSum());
                        SkipSpace();
                        if (Accept(','))
                        {
                            continue;
                        }
                        if (Accept(')'))
                        {
                            break;
                        }
                        throw new FormatException("expected \",\" or \")\" at position " + position + " of \"" + source + "\"");
                    }
                }
                return Call(name, arguments.ToArray());
            }
            throw new FormatException("unexpected \"" + c + "\" at position " + position + " of \"" + source + "\"");
        }

        private ShellExpressionNode Call(string name, ShellExpressionNode[] arguments)
        {
            ShellOperation operation;
            int count;
            switch (name)
            {
                case "sin":
                    operation = ShellOperation.Sine;
                    count = 1;
                    break;
                case "cos":
                    operation = ShellOperation.Cosine;
                    count = 1;
                    break;
                case "abs":
                    operation = ShellOperation.Absolute;
                    count = 1;
                    break;
                case "sqrt":
                    operation = ShellOperation.SquareRoot;
                    count = 1;
                    break;
                case "exp":
                    operation = ShellOperation.Exponential;
                    count = 1;
                    break;
                case "floor":
                    operation = ShellOperation.Floor;
                    count = 1;
                    break;
                case "fract":
                    operation = ShellOperation.Fract;
                    count = 1;
                    break;
                case "min":
                    operation = ShellOperation.Minimum;
                    count = 2;
                    break;
                case "max":
                    operation = ShellOperation.Maximum;
                    count = 2;
                    break;
                case "clamp":
                    operation = ShellOperation.Clamp;
                    count = 3;
                    break;
                case "mix":
                    operation = ShellOperation.Mix;
                    count = 3;
                    break;
                case "smoothstep":
                    operation = ShellOperation.SmoothStep;
                    count = 3;
                    break;
                case "pow":
                    operation = ShellOperation.Power;
                    count = 2;
                    break;
                case "noise":
                    operation = ShellOperation.Noise;
                    count = 2;
                    break;
                case "fbm":
                    operation = ShellOperation.FractalBrownianMotion;
                    count = 4;
                    break;
                case "turb":
                    operation = ShellOperation.Turbulence;
                    count = 4;
                    break;
                default:
                    throw new FormatException("unknown function \"" + name + "\" in \"" + source + "\"");
            }
            if (arguments.Length != count)
            {
                throw new FormatException("\"" + name + "\" takes " + count + " arguments in \"" + source + "\"");
            }
            if (operation == ShellOperation.Power && !(arguments[1] is ShellConstantNode))
            {
                throw new FormatException("the exponent of pow() must be a number in \"" + source + "\"");
            }
            if ((operation == ShellOperation.FractalBrownianMotion || operation == ShellOperation.Turbulence) &&
                (!(arguments[2] is ShellConstantNode) || !(arguments[3] is ShellConstantNode)))
            {
                throw new FormatException("the octaves and omega of \"" + name + "\" must be numbers in \"" + source + "\"");
            }
            return new ShellOperationNode(operation, arguments);
        }

        private bool Accept(char c)
        {
            if (position < source.Length && source[position] == c)
            {
                ++position;
                return true;
            }
            return false;
        }

        private void SkipSpace()
        {
            while (position < source.Length && char.IsWhiteSpace(source[position]))
            {
                ++position;
            }
        }

        private readonly string source;
        private int position;
    }

}  // namespace pbrt
