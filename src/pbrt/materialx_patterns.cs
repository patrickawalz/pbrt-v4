// (not in pbrt-v4) materialx_patterns.cs
//
// The pattern nodes of MaterialX that the reader of materialx.cs evaluates: an input of a shader that is connected to nodes
// becomes an expression (MtlxExpr), evaluated at every shaded point as a float texture or a colour texture of pbrt
// (MtlxFloatTexture, MtlxSpectrumTexture; a colour is RGB in the graph and becomes a spectrum at the end). An expression
// that depends on no position is folded to a constant when the document is read.
//
// The nodes:
//   values       constant, image, tiledimage (bilinear, no mip maps; "uvtiling", "uvoffset", "default"), texcoord, position,
//                normal
//   arithmetic   add, subtract, multiply, divide, modulo, power, min, max, clamp, mix, invert, absval, sign, sqrt, exp, ln,
//                sin, cos, tan, floor, ceil, round, fract, dotproduct, magnitude, normalize, remap, smoothstep
//   channels     extract, separate2 / 3 / 4, combine2 / 3 / 4, swizzle, convert
//   colour       luminance, saturate, contrast, rgbtohsv, hsvtorgb
//   procedural   noise2d, noise3d, fractal3d (Perlin noise of pbrt), checkerboard, ramplr, ramptb, place2d
//   conditional  ifgreater, ifgreatereq, ifequal
//   dot, normalmap (the value passed on: normal maps are pbrt's own)
// and any node that the document defines itself: a <nodedef> with a <nodegraph nodedef="..."> that implements it, whose
// "interfacename" inputs take the values given to the instance or the defaults of the definition.
//
// Colour spaces of colour values and images ("colorspace" on the element or on an ancestor): lin_rec709 and none as they are,
// srgb_texture decoded from sRGB, g22_rec709 from a gamma of 2.2. Any other is read as linear Rec. 709, with a warning.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class MtlxExpr
    {
        public string op;
        public Dictionary<string, MtlxExpr> args = new Dictionary<string, MtlxExpr>();
        public Float[] constant;
        public Image image;
        public ImageChannelDesc rgb;   // the red, green and blue channels of the image, where it names them
        public int size = 1;       // the number of channels of the value
        public int index = 0;      // the output of a separate node
        public string text = "";   // the channels of a swizzle

        public static MtlxExpr Constant(params Float[] v)
        {
            MtlxExpr e = new MtlxExpr();
            e.op = "constant";
            e.constant = v;
            e.size = v.Length;
            return e;
        }

        public bool Varying()
        {
            if (op == "image" || op == "tiledimage" || op == "texcoord" || op == "position" || op == "normal")
            {
                return true;
            }
            foreach (MtlxExpr a in args.Values)
            {
                if (a != null && a.Varying())
                {
                    return true;
                }
            }
            // (these take the texture coordinates or the position when none is connected)
            return op == "noise2d" || op == "noise3d" || op == "fractal3d" || op == "checkerboard" || op == "ramplr" || op == "ramptb";
        }

        private Float[] A(string name, TextureEvalContext ctx, params Float[] fallback)
        {
            MtlxExpr e;
            if (args.TryGetValue(name, out e) && e != null)
            {
                Float[] v = e.Evaluate(ctx);
                if (v != null && v.Length > 0)
                {
                    return v;
                }
            }
            return fallback;
        }

        private static Float At(Float[] v, int i) { return v[Math.Min(i, v.Length - 1)]; }

        private Float[] Map2(string a, string b, TextureEvalContext ctx, Float fa, Float fb, Func<Float, Float, Float> f)
        {
            Float[] x = A(a, ctx, fa), y = A(b, ctx, fb);
            Float[] r = new Float[Math.Max(Math.Max(x.Length, y.Length), 1)];
            for (int i = 0; i < r.Length; ++i)
            {
                r[i] = f(At(x, i), At(y, i));
            }
            return r;
        }

        private Float[] Map1(TextureEvalContext ctx, Func<Float, Float> f)
        {
            Float[] x = A("in", ctx, 0);
            Float[] r = new Float[x.Length];
            for (int i = 0; i < r.Length; ++i)
            {
                r[i] = f(x[i]);
            }
            return r;
        }

        private Float[] Coordinates(TextureEvalContext ctx)
        {
            return A("texcoord", ctx, ctx.uv[0], ctx.uv[1]);
        }

        private static void ToHsv(Float r, Float g, Float b, out Float h, out Float s, out Float v)
        {
            Float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            v = max;
            Float d = max - min;
            s = max > 0 ? d / max : 0;
            if (d == 0)
            {
                h = 0;
            }
            else if (max == r)
            {
                h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
            }
            else if (max == g)
            {
                h = ((b - r) / d + 2) / 6;
            }
            else
            {
                h = ((r - g) / d + 4) / 6;
            }
        }

        private static void FromHsv(Float h, Float s, Float v, out Float r, out Float g, out Float b)
        {
            h = (h - (Float)Math.Floor(h)) * 6;
            int sector = (int)Math.Floor(h);
            Float f = h - sector, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            switch (sector % 6)
            {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
            }
        }

        public Float[] Evaluate(TextureEvalContext ctx)
        {
            switch (op)
            {
            case "constant":
                return constant;
            case "texcoord":
                return new Float[] { ctx.uv[0], ctx.uv[1] };
            case "position":
                return new Float[] { ctx.p.x, ctx.p.y, ctx.p.z };
            case "normal":
                return new Float[] { ctx.n.x, ctx.n.y, ctx.n.z };
            case "image":
            case "tiledimage":
                {
                    Float[] uv = Coordinates(ctx);
                    Float[] tiling = A("uvtiling", ctx, 1, 1), offset = A("uvoffset", ctx, 0, 0);
                    // (MaterialX: v = 0 at the bottom of the image; pbrt images have their first row at the top)
                    Point2f st = new Point2f(At(uv, 0) * At(tiling, 0) - At(offset, 0), 1 - (At(uv, 1) * At(tiling, 1) - At(offset, 1)));
                    if (image == null)
                    {
                        return A("default", ctx, new Float[size]);
                    }
                    Float[] r = new Float[size];
                    WrapMode2D wrap = new WrapMode2D(WrapMode.Repeat);
                    if (rgb != null && (bool)rgb)
                    {
                        // (an OpenEXR file stores its channels by name, in alphabetical order)
                        ImageChannelValues named = image.Bilerp(st, rgb, wrap);
                        for (int c = 0; c < size; ++c)
                        {
                            r[c] = c < 3 ? named[c] : 1;
                        }
                        return r;
                    }
                    for (int c = 0; c < size; ++c)
                    {
                        r[c] = c < image.NChannels() ? image.BilerpChannel(st, c, wrap) : (image.NChannels() == 1 ? image.BilerpChannel(st, 0, wrap) : 1);
                    }
                    return r;
                }
            case "add": return Map2("in1", "in2", ctx, 0, 0, (Float x, Float y) => x + y);
            case "subtract": return Map2("in1", "in2", ctx, 0, 0, (Float x, Float y) => x - y);
            case "multiply": return Map2("in1", "in2", ctx, 0, 1, (Float x, Float y) => x * y);
            case "divide": return Map2("in1", "in2", ctx, 0, 1, (Float x, Float y) => y != 0 ? x / y : 0);
            case "modulo": return Map2("in1", "in2", ctx, 0, 1, (Float x, Float y) => y != 0 ? x - y * (Float)Math.Floor(x / y) : 0);
            case "power": return Map2("in1", "in2", ctx, 0, 1, (Float x, Float y) => (Float)Math.Pow(Math.Max(x, 0), y));
            case "min": return Map2("in1", "in2", ctx, 0, 0, (Float x, Float y) => Math.Min(x, y));
            case "max": return Map2("in1", "in2", ctx, 0, 0, (Float x, Float y) => Math.Max(x, y));
            case "absval": return Map1(ctx, (Float x) => Math.Abs(x));
            case "sign": return Map1(ctx, (Float x) => Math.Sign(x));
            case "sqrt": return Map1(ctx, (Float x) => (Float)Math.Sqrt(Math.Max(x, 0)));
            case "exp": return Map1(ctx, (Float x) => (Float)Math.Exp(x));
            case "ln": return Map1(ctx, (Float x) => (Float)Math.Log(Math.Max(x, 1e-30f)));
            case "sin": return Map1(ctx, (Float x) => (Float)Math.Sin(x));
            case "cos": return Map1(ctx, (Float x) => (Float)Math.Cos(x));
            case "tan": return Map1(ctx, (Float x) => (Float)Math.Tan(x));
            case "floor": return Map1(ctx, (Float x) => (Float)Math.Floor(x));
            case "ceil": return Map1(ctx, (Float x) => (Float)Math.Ceiling(x));
            case "round": return Map1(ctx, (Float x) => (Float)Math.Round(x));
            case "fract": return Map1(ctx, (Float x) => x - (Float)Math.Floor(x));
            case "dot":
            case "normalmap":
            case "convert":
                {
                    Float[] x = A("in", ctx, 0);
                    if (op != "convert" || x.Length == size)
                    {
                        return x;
                    }
                    Float[] r = new Float[size];
                    for (int i = 0; i < size; ++i)
                    {
                        // a float becomes every channel; a colour gains an alpha of one
                        r[i] = x.Length == 1 ? x[0] : (i < x.Length ? x[i] : 1);
                    }
                    return r;
                }
            case "invert":
                {
                    Float[] x = A("in", ctx, 0), amount = A("amount", ctx, 1);
                    Float[] r = new Float[x.Length];
                    for (int i = 0; i < r.Length; ++i)
                    {
                        r[i] = At(amount, i) - x[i];
                    }
                    return r;
                }
            case "clamp":
                {
                    Float[] x = A("in", ctx, 0), low = A("low", ctx, 0), high = A("high", ctx, 1);
                    Float[] r = new Float[x.Length];
                    for (int i = 0; i < r.Length; ++i)
                    {
                        r[i] = Math.Min(Math.Max(x[i], At(low, i)), At(high, i));
                    }
                    return r;
                }
            case "mix":
                {
                    Float[] fg = A("fg", ctx, 0), bg = A("bg", ctx, 0), mix = A("mix", ctx, 0);
                    Float[] r = new Float[Math.Max(fg.Length, bg.Length)];
                    for (int i = 0; i < r.Length; ++i)
                    {
                        Float m = At(mix, i);
                        r[i] = At(bg, i) * (1 - m) + At(fg, i) * m;
                    }
                    return r;
                }
            case "remap":
                {
                    Float[] x = A("in", ctx, 0), a = A("inlow", ctx, 0), b = A("inhigh", ctx, 1), c = A("outlow", ctx, 0), d = A("outhigh", ctx, 1);
                    Float[] r = new Float[x.Length];
                    for (int i = 0; i < r.Length; ++i)
                    {
                        Float span = At(b, i) - At(a, i);
                        r[i] = At(c, i) + (span != 0 ? (x[i] - At(a, i)) / span : 0) * (At(d, i) - At(c, i));
                    }
                    return r;
                }
            case "smoothstep":
                {
                    Float[] x = A("in", ctx, 0), low = A("low", ctx, 0), high = A("high", ctx, 1);
                    Float[] r = new Float[x.Length];
                    for (int i = 0; i < r.Length; ++i)
                    {
                        Float span = At(high, i) - At(low, i);
                        Float t = span != 0 ? Math.Min(Math.Max((x[i] - At(low, i)) / span, 0), 1) : (x[i] < At(low, i) ? 0 : 1);
                        r[i] = t * t * (3 - 2 * t);
                    }
                    return r;
                }
            case "dotproduct":
                {
                    Float[] x = A("in1", ctx, 0), y = A("in2", ctx, 0);
                    Float sum = 0;
                    for (int i = 0; i < Math.Max(x.Length, y.Length); ++i)
                    {
                        sum += At(x, i) * At(y, i);
                    }
                    return new Float[] { sum };
                }
            case "magnitude":
            case "normalize":
                {
                    Float[] x = A("in", ctx, 0);
                    Float sum = 0;
                    for (int i = 0; i < x.Length; ++i)
                    {
                        sum += x[i] * x[i];
                    }
                    Float length = (Float)Math.Sqrt(sum);
                    if (op == "magnitude")
                    {
                        return new Float[] { length };
                    }
                    Float[] r = new Float[x.Length];
                    for (int i = 0; i < r.Length; ++i)
                    {
                        r[i] = length > 0 ? x[i] / length : 0;
                    }
                    return r;
                }
            case "extract":
                {
                    Float[] x = A("in", ctx, 0), which = A("index", ctx, 0);
                    return new Float[] { At(x, Math.Max(0, (int)which[0])) };
                }
            case "separate2":
            case "separate3":
            case "separate4":
                return new Float[] { At(A("in", ctx, 0), index) };
            case "combine2":
                return new Float[] { A("in1", ctx, 0)[0], A("in2", ctx, 0)[0] };
            case "combine3":
                return new Float[] { A("in1", ctx, 0)[0], A("in2", ctx, 0)[0], A("in3", ctx, 0)[0] };
            case "combine4":
                return new Float[] { A("in1", ctx, 0)[0], A("in2", ctx, 0)[0], A("in3", ctx, 0)[0], A("in4", ctx, 0)[0] };
            case "swizzle":
                {
                    Float[] x = A("in", ctx, 0);
                    Float[] r = new Float[Math.Max(text.Length, 1)];
                    for (int i = 0; i < text.Length; ++i)
                    {
                        int c = "rgba".IndexOf(text[i]);
                        if (c < 0)
                        {
                            c = "xyzw".IndexOf(text[i]);
                        }
                        r[i] = text[i] == '0' ? 0 : text[i] == '1' ? 1 : At(x, Math.Max(c, 0));
                    }
                    return r;
                }
            case "luminance":
                {
                    Float[] x = A("in", ctx, 0);
                    Float y = 0.2126f * At(x, 0) + 0.7152f * At(x, 1) + 0.0722f * At(x, 2);
                    return new Float[] { y, y, y };
                }
            case "saturate":
                {
                    Float[] x = A("in", ctx, 0);
                    Float amount = A("amount", ctx, 1)[0];
                    Float y = 0.2126f * At(x, 0) + 0.7152f * At(x, 1) + 0.0722f * At(x, 2);
                    return new Float[] { y + (At(x, 0) - y) * amount, y + (At(x, 1) - y) * amount, y + (At(x, 2) - y) * amount };
                }
            case "contrast":
                {
                    Float[] x = A("in", ctx, 0), amount = A("amount", ctx, 1), pivot = A("pivot", ctx, 0.5f);
                    Float[] r = new Float[x.Length];
                    for (int i = 0; i < r.Length; ++i)
                    {
                        r[i] = (x[i] - At(pivot, i)) * At(amount, i) + At(pivot, i);
                    }
                    return r;
                }
            case "rgbtohsv":
                {
                    Float[] x = A("in", ctx, 0);
                    Float h, s, v;
                    ToHsv(At(x, 0), At(x, 1), At(x, 2), out h, out s, out v);
                    return new Float[] { h, s, v };
                }
            case "hsvtorgb":
                {
                    Float[] x = A("in", ctx, 0);
                    Float r, g, b;
                    FromHsv(At(x, 0), At(x, 1), At(x, 2), out r, out g, out b);
                    return new Float[] { r, g, b };
                }
            case "noise2d":
            case "noise3d":
            case "fractal3d":
                {
                    Float[] amplitude = A("amplitude", ctx, 1);
                    Float value;
                    if (op == "noise2d")
                    {
                        Float[] uv = Coordinates(ctx);
                        value = pbrt.Noise(At(uv, 0), At(uv, 1), 0.5f);
                    }
                    else
                    {
                        Float[] p = A("position", ctx, ctx.p.x, ctx.p.y, ctx.p.z);
                        if (op == "noise3d")
                        {
                            value = pbrt.Noise(At(p, 0), At(p, 1), At(p, 2));
                        }
                        else
                        {
                            int octaves = Math.Max(1, (int)A("octaves", ctx, 3)[0]);
                            Float lacunarity = A("lacunarity", ctx, 2)[0], diminish = A("diminish", ctx, 0.5f)[0];
                            Float scale = 1, weight = 1;
                            value = 0;
                            for (int o = 0; o < octaves; ++o)
                            {
                                value += weight * pbrt.Noise(At(p, 0) * scale, At(p, 1) * scale, At(p, 2) * scale);
                                scale *= lacunarity;
                                weight *= diminish;
                            }
                        }
                    }
                    Float pivot = A("pivot", ctx, 0)[0];
                    Float[] r = new Float[Math.Max(size, 1)];
                    for (int i = 0; i < r.Length; ++i)
                    {
                        r[i] = value * At(amplitude, i) + pivot;
                    }
                    return r;
                }
            case "checkerboard":
                {
                    Float[] uv = Coordinates(ctx), tiling = A("uvtiling", ctx, 8, 8), offset = A("uvoffset", ctx, 0, 0);
                    Float[] c1 = A("color1", ctx, 1, 1, 1), c2 = A("color2", ctx, 0, 0, 0);
                    int cell = (int)Math.Floor(At(uv, 0) * At(tiling, 0) - At(offset, 0)) + (int)Math.Floor(At(uv, 1) * At(tiling, 1) - At(offset, 1));
                    return (cell & 1) == 0 ? c1 : c2;
                }
            case "ramplr":
            case "ramptb":
                {
                    Float[] uv = Coordinates(ctx);
                    Float[] a = A(op == "ramplr" ? "valuel" : "valuet", ctx, 0), b = A(op == "ramplr" ? "valuer" : "valueb", ctx, 1);
                    Float t = Math.Min(Math.Max(op == "ramplr" ? At(uv, 0) : 1 - At(uv, 1), 0), 1);
                    Float[] r = new Float[Math.Max(a.Length, b.Length)];
                    for (int i = 0; i < r.Length; ++i)
                    {
                        r[i] = At(a, i) * (1 - t) + At(b, i) * t;
                    }
                    return r;
                }
            case "place2d":
                {
                    Float[] uv = Coordinates(ctx), pivot = A("pivot", ctx, 0, 0), scale = A("scale", ctx, 1, 1), offset = A("offset", ctx, 0, 0);
                    Float rotate = A("rotate", ctx, 0)[0] * (Float)Math.PI / 180;
                    Float x = At(uv, 0) - At(pivot, 0), y = At(uv, 1) - At(pivot, 1);
                    x = At(scale, 0) != 0 ? x / At(scale, 0) : x;
                    y = At(scale, 1) != 0 ? y / At(scale, 1) : y;
                    Float c = (Float)Math.Cos(rotate), s = (Float)Math.Sin(rotate);
                    return new Float[] { c * x + s * y - At(offset, 0) + At(pivot, 0), -s * x + c * y - At(offset, 1) + At(pivot, 1) };
                }
            case "ifgreater":
            case "ifgreatereq":
            case "ifequal":
                {
                    Float a = A("value1", ctx, 1)[0], b = A("value2", ctx, 0)[0];
                    bool yes = op == "ifgreater" ? a > b : op == "ifgreatereq" ? a >= b : a == b;
                    return yes ? A("in1", ctx, 0) : A("in2", ctx, 0);
                }
            }
            return new Float[Math.Max(size, 1)];
        }

        public static bool Known(string category)
        {
            switch (category)
            {
            case "constant": case "texcoord": case "position": case "normal": case "image": case "tiledimage": case "add": case "subtract": case "multiply":
            case "divide": case "modulo": case "power": case "min": case "max": case "absval": case "sign": case "sqrt": case "exp": case "ln": case "sin":
            case "cos": case "tan": case "floor": case "ceil": case "round": case "fract": case "dot": case "normalmap": case "convert": case "invert":
            case "clamp": case "mix": case "remap": case "smoothstep": case "dotproduct": case "magnitude": case "normalize": case "extract":
            case "separate2": case "separate3": case "separate4": case "combine2": case "combine3": case "combine4": case "swizzle": case "luminance":
            case "saturate": case "contrast": case "rgbtohsv": case "hsvtorgb": case "noise2d": case "noise3d": case "fractal3d": case "checkerboard":
            case "ramplr": case "ramptb": case "place2d": case "ifgreater": case "ifgreatereq": case "ifequal":
                return true;
            }
            return false;
        }
    }

    // A float input of a shader that is a graph of pattern nodes
    public sealed unsafe class MtlxFloatTexture : FloatTexture
    {
        public MtlxFloatTexture(MtlxExpr expr) { this.expr = expr; }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Float[] v = expr.Evaluate(ctx);
            return v.Length > 0 ? v[0] : 0;
        }

        public override Float MinValue() { return Float.NegativeInfinity; }
        public override Float MaxValue() { return Float.PositiveInfinity; }
        public override string ToString() { return "[ MtlxFloatTexture " + expr.op + " ]"; }

        private MtlxExpr expr;
    }

    // A colour input: RGB (linear Rec. 709) in the graph, a reflectance spectrum (or, with `unbounded`, any positive one) here
    public sealed unsafe class MtlxSpectrumTexture : SpectrumTexture
    {
        public MtlxSpectrumTexture(MtlxExpr expr, bool unbounded)
        {
            this.expr = expr;
            this.unbounded = unbounded;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float[] v = expr.Evaluate(ctx);
            Float r = v.Length > 0 ? v[0] : 0, g = v.Length > 1 ? v[1] : r, b = v.Length > 2 ? v[2] : r;
            if (unbounded)
            {
                return new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(Math.Max(r, 0), Math.Max(g, 0), Math.Max(b, 0))).Sample(lambda);
            }
            return new RGBAlbedoSpectrum(RGBColorSpace.sRGB, new RGB(Math.Min(Math.Max(r, 0), 1), Math.Min(Math.Max(g, 0), 1), Math.Min(Math.Max(b, 0), 1))).Sample(lambda);
        }

        public override Float MaxValue() { return unbounded ? Float.PositiveInfinity : 1; }
        public override string ToString() { return "[ MtlxSpectrumTexture " + expr.op + " ]"; }

        private MtlxExpr expr;
        private bool unbounded;
    }
}
