// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/color.h, util/color.cpp
//
// The RGB-to-spectrum coefficient tables (rgbspectrum_*.cs) are produced by pbrt's own rgb2spec_opt
// tool, as in the pbrt CMake build, and compiled in as source.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace pbrt
{

    // RGB Definition
    public struct RGB
    {
        // RGB Public Methods
        public RGB(Float r, Float g, Float b) { this.r = r; this.g = g; this.b = b; }

        public static RGB operator +(RGB a, RGB s) { return new RGB(a.r + s.r, a.g + s.g, a.b + s.b); }
        public static RGB operator -(RGB a, RGB s) { return new RGB(a.r - s.r, a.g - s.g, a.b - s.b); }
        public static RGB operator -(Float a, RGB s) { return new RGB(a - s.r, a - s.g, a - s.b); }
        public static RGB operator *(RGB a, RGB s) { return new RGB(a.r * s.r, a.g * s.g, a.b * s.b); }
        public static RGB operator *(RGB s, Float a)
        {
            DCHECK(!IsNaN(a));
            return new RGB(a * s.r, a * s.g, a * s.b);
        }
        public static RGB operator *(Float a, RGB s) { return s * a; }
        public static RGB operator /(RGB a, RGB s) { return new RGB(a.r / s.r, a.g / s.g, a.b / s.b); }
        public static RGB operator /(RGB s, Float a)
        {
            DCHECK(!IsNaN(a));
            DCHECK_NE(a, 0);
            return new RGB(s.r / a, s.g / a, s.b / a);
        }
        public static RGB operator -(RGB a) { return new RGB(-a.r, -a.g, -a.b); }

        public Float Average() { return (r + g + b) / 3; }

        public static bool operator ==(RGB a, RGB s) { return a.r == s.r && a.g == s.g && a.b == s.b; }
        public static bool operator !=(RGB a, RGB s) { return a.r != s.r || a.g != s.g || a.b != s.b; }
        public override bool Equals(object o) { return o is RGB s && this == s; }
        public override int GetHashCode() { return HashCode.Combine(r, g, b); }

        public Float this[int c]
        {
            get
            {
                DCHECK(c >= 0 && c < 3);
                if (c == 0) return r;
                else if (c == 1) return g;
                return b;
            }
            set
            {
                DCHECK(c >= 0 && c < 3);
                if (c == 0) r = value;
                else if (c == 1) g = value;
                else b = value;
            }
        }

        // Mul<RGB>(SquareMatrix<3>, RGB) via the generic SquareMatrix * T operator
        public static RGB operator *(SquareMatrix3 m, RGB v)
        {
            RGB result = new RGB();
            for (int i = 0; i < 3; ++i)
            {
                result[i] = 0;
                for (int j = 0; j < 3; ++j)
                    result[i] += m[i][j] * v[j];
            }
            return result;
        }

        public override string ToString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "[ {0:F6} {1:F6} {2:F6} ]", r, g, b);
        }

        public Float r, g, b;
    }

    // XYZ Definition
    public struct XYZ
    {
        // XYZ Public Methods
        public XYZ(Float X, Float Y, Float Z) { this.X = X; this.Y = Y; this.Z = Z; }

        public Float Average() { return (X + Y + Z) / 3; }

        public Point2f xy() { return new Point2f(X / (X + Y + Z), Y / (X + Y + Z)); }

        public static XYZ FromxyY(Point2f xy, Float Y = 1)
        {
            if (xy.y == 0)
                return new XYZ(0, 0, 0);
            return new XYZ(xy.x * Y / xy.y, Y, (1 - xy.x - xy.y) * Y / xy.y);
        }

        public static XYZ operator +(XYZ a, XYZ s) { return new XYZ(a.X + s.X, a.Y + s.Y, a.Z + s.Z); }
        public static XYZ operator -(XYZ a, XYZ s) { return new XYZ(a.X - s.X, a.Y - s.Y, a.Z - s.Z); }
        public static XYZ operator -(Float a, XYZ s) { return new XYZ(a - s.X, a - s.Y, a - s.Z); }
        public static XYZ operator *(XYZ a, XYZ s) { return new XYZ(a.X * s.X, a.Y * s.Y, a.Z * s.Z); }
        public static XYZ operator *(XYZ s, Float a)
        {
            DCHECK(!IsNaN(a));
            return new XYZ(a * s.X, a * s.Y, a * s.Z);
        }
        public static XYZ operator *(Float a, XYZ s) { return s * a; }
        public static XYZ operator /(XYZ a, XYZ s) { return new XYZ(a.X / s.X, a.Y / s.Y, a.Z / s.Z); }
        public static XYZ operator /(XYZ s, Float a)
        {
            DCHECK(!IsNaN(a));
            DCHECK_NE(a, 0);
            return new XYZ(s.X / a, s.Y / a, s.Z / a);
        }
        public static XYZ operator -(XYZ a) { return new XYZ(-a.X, -a.Y, -a.Z); }

        public static bool operator ==(XYZ a, XYZ s) { return a.X == s.X && a.Y == s.Y && a.Z == s.Z; }
        public static bool operator !=(XYZ a, XYZ s) { return a.X != s.X || a.Y != s.Y || a.Z != s.Z; }
        public override bool Equals(object o) { return o is XYZ s && this == s; }
        public override int GetHashCode() { return HashCode.Combine(X, Y, Z); }

        public Float this[int c]
        {
            get
            {
                DCHECK(c >= 0 && c < 3);
                if (c == 0) return X;
                else if (c == 1) return Y;
                return Z;
            }
            set
            {
                DCHECK(c >= 0 && c < 3);
                if (c == 0) X = value;
                else if (c == 1) Y = value;
                else Z = value;
            }
        }

        // Mul<XYZ>(SquareMatrix<3>, XYZ)
        public static XYZ operator *(SquareMatrix3 m, XYZ v)
        {
            XYZ result = new XYZ();
            for (int i = 0; i < 3; ++i)
            {
                result[i] = 0;
                for (int j = 0; j < 3; ++j)
                    result[i] += m[i][j] * v[j];
            }
            return result;
        }

        public override string ToString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "[ {0:F6} {1:F6} {2:F6} ]", X, Y, Z);
        }

        public Float X, Y, Z;
    }

    // RGBSigmoidPolynomial Definition
    public struct RGBSigmoidPolynomial
    {
        // RGBSigmoidPolynomial Public Methods
        public RGBSigmoidPolynomial(Float c0, Float c1, Float c2) { this.c0 = c0; this.c1 = c1; this.c2 = c2; }

        public override string ToString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                 "[ RGBSigmoidPolynomial c0: {0:F6} c1: {1:F6} c2: {2:F6} ]", c0, c1, c2);
        }

        public Float Apply(Float lambda)
        {
            return s(EvaluatePolynomial(lambda, c2, c1, c0));  // c0 * lambda^2 + c1 * lambda + c2
        }

        public Float MaxValue()
        {
            Float result = std.max(Apply(360), Apply(830));
            Float lambda = -c1 / (2 * c0);
            if (lambda >= 360 && lambda <= 830)
                result = std.max(result, Apply(lambda));
            return result;
        }

        // RGBSigmoidPolynomial Private Methods
        private static Float s(Float x)
        {
            if (IsInf(x))
                return x > 0 ? 1 : 0;
            return .5f + x / (2 * std.sqrt(1 + Sqr(x)));
        }

        // RGBSigmoidPolynomial Private Members
        private Float c0, c1, c2;
    }

    public static partial class pbrt
    {
        public const int RGBToSpectrumTableRes = 64;
    }

    // RGBToSpectrumTable Definition
    public sealed class RGBToSpectrumTable
    {
        // RGBToSpectrumTable Public Constants
        public const int res = RGBToSpectrumTableRes;

        // RGBToSpectrumTable Public Methods
        // coeffs is float[3][res][res][res][3] flattened in C order
        public RGBToSpectrumTable(Float[] zNodes, float[] coeffs, string name)
        {
            this.zNodes = zNodes;
            this.coeffs = coeffs;
            this.name = name;
        }

        public RGBSigmoidPolynomial Apply(RGB rgb)
        {
            DCHECK(rgb[0] >= 0f && rgb[1] >= 0f && rgb[2] >= 0f && rgb[0] <= 1f &&
                   rgb[1] <= 1f && rgb[2] <= 1f);

            // Handle uniform _rgb_ values
            if (rgb[0] == rgb[1] && rgb[1] == rgb[2])
                return new RGBSigmoidPolynomial(0, 0,
                                                (rgb[0] - .5f) / std.sqrt(rgb[0] * (1 - rgb[0])));

            // Find maximum component and compute remapped component values
            int maxc =
                (rgb[0] > rgb[1]) ? ((rgb[0] > rgb[2]) ? 0 : 2) : ((rgb[1] > rgb[2]) ? 1 : 2);
            float z = rgb[maxc];
            float x = rgb[(maxc + 1) % 3] * (res - 1) / z;
            float y = rgb[(maxc + 2) % 3] * (res - 1) / z;

            // Compute integer indices and offsets for coefficient interpolation
            int xi = std.min((int)x, res - 2), yi = std.min((int)y, res - 2),
                zi = (int)FindInterval((size_t)res, (int i) => zNodes[i] < z);
            Float dx = x - xi, dy = y - yi, dz = (z - zNodes[zi]) / (zNodes[zi + 1] - zNodes[zi]);

            // Trilinearly interpolate sigmoid polynomial coefficients _c_
            Float c0 = 0, c1 = 0, c2 = 0;
            for (int i = 0; i < 3; ++i)
            {
                Float c = Lerp(dz,
                               Lerp(dy, Lerp(dx, co(maxc, zi, yi, xi, i), co(maxc, zi, yi, xi + 1, i)),
                                    Lerp(dx, co(maxc, zi, yi + 1, xi, i), co(maxc, zi, yi + 1, xi + 1, i))),
                               Lerp(dy, Lerp(dx, co(maxc, zi + 1, yi, xi, i), co(maxc, zi + 1, yi, xi + 1, i)),
                                    Lerp(dx, co(maxc, zi + 1, yi + 1, xi, i), co(maxc, zi + 1, yi + 1, xi + 1, i))));
                if (i == 0) c0 = c; else if (i == 1) c1 = c; else c2 = c;
            }

            return new RGBSigmoidPolynomial(c0, c1, c2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Float co(int maxc, int zi, int yi, int xi, int i)
        {
            return coeffs[((((maxc * res + zi) * res + yi) * res + xi) * 3) + i];
        }

        public static void Init(Allocator alloc)
        {
            sRGB = new RGBToSpectrumTable(rgbspectrum_srgb.sRGBToSpectrumTable_Scale, rgbspectrum_srgb.sRGBToSpectrumTable_Data, "sRGB");
            DCI_P3 = new RGBToSpectrumTable(rgbspectrum_dci_p3.DCI_P3ToSpectrumTable_Scale, rgbspectrum_dci_p3.DCI_P3ToSpectrumTable_Data, "DCI_P3");
            Rec2020 = new RGBToSpectrumTable(rgbspectrum_rec2020.REC2020ToSpectrumTable_Scale, rgbspectrum_rec2020.REC2020ToSpectrumTable_Data, "Rec2020");
            ACES2065_1 = new RGBToSpectrumTable(rgbspectrum_aces.ACES2065_1ToSpectrumTable_Scale, rgbspectrum_aces.ACES2065_1ToSpectrumTable_Data, "ACES2065_1");
        }

        public override string ToString()
        {
            return $"[ RGBToSpectrumTable res: {res} ({name}) ]";
        }

        public static RGBToSpectrumTable sRGB;
        public static RGBToSpectrumTable DCI_P3;
        public static RGBToSpectrumTable Rec2020;
        public static RGBToSpectrumTable ACES2065_1;

        // RGBToSpectrumTable Private Members
        private Float[] zNodes;
        private float[] coeffs;
        private string name;
    }

    // ColorEncoding Definition (TaggedPointer<LinearColorEncoding, sRGBColorEncoding, GammaColorEncoding>)
    public abstract class ColorEncoding
    {
        // ColorEncoding Public Methods
        public abstract void ToLinear(pstd.span<byte> vin, pstd.span<Float> vout);
        public abstract void FromLinear(pstd.span<Float> vin, pstd.span<byte> vout);
        public abstract Float ToFloatLinear(Float v);
        public abstract override string ToString();

        // Single-value helpers (Magi addition) so Image::GetChannel()/SetChannel() don't
        // need to build one-element spans.
        public virtual Float ToLinearByte(byte v)
        {
            Float[] o = new Float[1];
            byte[] i = new byte[] { v };
            ToLinear(new pstd.span<byte>(i), new pstd.span<Float>(o));
            return o[0];
        }
        public virtual byte FromLinearByte(Float v)
        {
            Float[] i = new Float[] { v };
            byte[] o = new byte[1];
            FromLinear(new pstd.span<Float>(i), new pstd.span<byte>(o));
            return o[0];
        }

        public static ColorEncoding Linear;
        public static ColorEncoding sRGB;

        public static void Init(Allocator alloc)
        {
            Linear = new LinearColorEncoding();
            sRGB = new sRGBColorEncoding();
        }

        private static readonly Dictionary<float, ColorEncoding> cache = new Dictionary<float, ColorEncoding>();
        private static readonly object cacheMutex = new object();

        public static ColorEncoding Get(string name, Allocator alloc)
        {
            if (name == "linear")
                return Linear;
            else if (name == "sRGB")
                return sRGB;
            else
            {
                string[] parms = name.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parms.Length != 2 || parms[0] != "gamma")
                    ErrorExit($"{name}: expected \"gamma <value>\" for color encoding");

                Float gamma = 0;
                Float.TryParse(parms[1], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out gamma);
                if (gamma == 0)
                    ErrorExit($"{parms[1]}: unable to parse gamma value");

                lock (cacheMutex)
                {
                    ColorEncoding found;
                    if (cache.TryGetValue(gamma, out found))
                        return found;

                    ColorEncoding enc = new GammaColorEncoding(gamma);
                    cache[gamma] = enc;
                    LOG_VERBOSE($"Added ColorEncoding {name} for gamma {gamma} -> {enc}");
                    return enc;
                }
            }
        }
    }

    // LinearColorEncoding Definition
    public sealed class LinearColorEncoding : ColorEncoding
    {
        public override void ToLinear(pstd.span<byte> vin, pstd.span<Float> vout)
        {
            DCHECK_EQ(vin.size(), vout.size());
            for (size_t i = 0; i < vin.size(); ++i)
                vout[i] = vin[i] / 255f;
        }

        public override Float ToFloatLinear(Float v) { return v; }
        public override Float ToLinearByte(byte v) { return v / 255f; }
        public override byte FromLinearByte(Float v) { return (byte)Clamp(v * 255f + 0.5f, 0, 255); }

        public override void FromLinear(pstd.span<Float> vin, pstd.span<byte> vout)
        {
            DCHECK_EQ(vin.size(), vout.size());
            for (size_t i = 0; i < vin.size(); ++i)
                vout[i] = (byte)Clamp(vin[i] * 255f + 0.5f, 0, 255);
        }

        public override string ToString() { return "[ LinearColorEncoding ]"; }
    }

    // sRGBColorEncoding Definition
    public sealed class sRGBColorEncoding : ColorEncoding
    {
        public override void FromLinear(pstd.span<Float> vin, pstd.span<byte> vout)
        {
            DCHECK_EQ(vin.size(), vout.size());
            for (size_t i = 0; i < vin.size(); ++i)
                vout[i] = LinearToSRGB8(vin[i]);
        }

        public override void ToLinear(pstd.span<byte> vin, pstd.span<Float> vout)
        {
            DCHECK_EQ(vin.size(), vout.size());
            for (size_t i = 0; i < vin.size(); ++i)
                vout[i] = SRGB8ToLinear(vin[i]);
        }

        public override Float ToFloatLinear(Float v) { return SRGBToLinear(v); }
        public override Float ToLinearByte(byte v) { return SRGB8ToLinear(v); }
        public override byte FromLinearByte(Float v) { return LinearToSRGB8(v); }

        public override string ToString() { return "[ sRGBColorEncoding ]"; }
    }

    // GammaColorEncoding Definition
    public sealed class GammaColorEncoding : ColorEncoding
    {
        public GammaColorEncoding(Float gamma)
        {
            this.gamma = gamma;
            applyLUT = new Float[256];
            inverseLUT = new Float[1024];
            for (int i = 0; i < 256; ++i)
            {
                Float v = (Float)i / 255f;
                applyLUT[i] = std.pow(v, gamma);
            }

            for (int i = 0; i < inverseLUT.Length; ++i)
            {
                Float v = (Float)i / (Float)(inverseLUT.Length - 1);
                inverseLUT[i] = Clamp(255f * std.pow(v, 1f / gamma) + .5f, 0, 255);
            }
        }

        public override void ToLinear(pstd.span<byte> vin, pstd.span<Float> vout)
        {
            DCHECK_EQ(vin.size(), vout.size());
            for (size_t i = 0; i < vin.size(); ++i)
                vout[i] = applyLUT[vin[i]];
        }

        public override Float ToFloatLinear(Float v) { return std.pow(v, gamma); }
        public override Float ToLinearByte(byte v) { return applyLUT[v]; }

        public override void FromLinear(pstd.span<Float> vin, pstd.span<byte> vout)
        {
            DCHECK_EQ(vin.size(), vout.size());
            for (size_t i = 0; i < vin.size(); ++i)
                vout[i] = (byte)inverseLUT[(int)Clamp(vin[i] * (inverseLUT.Length - 1), 0, inverseLUT.Length - 1)];
        }

        public override string ToString() { return $"[ GammaColorEncoding gamma: {gamma:F6} ]"; }

        private Float gamma;
        private Float[] applyLUT;
        private Float[] inverseLUT;
    }

    public static partial class pbrt
    {
        // Color Function Declarations
        public static RGB max(RGB a, RGB b)
        {
            return new RGB(std.max(a.r, b.r), std.max(a.g, b.g), std.max(a.b, b.b));
        }

        public static RGB Lerp(Float t, RGB s1, RGB s2) { return (1 - t) * s1 + t * s2; }

        public static RGB Clamp(RGB rgb, Float min, Float max)
        {
            return new RGB(Clamp(rgb.r, min, max), Clamp(rgb.g, min, max), Clamp(rgb.b, min, max));
        }

        public static RGB ClampZero(RGB rgb)
        {
            return new RGB(std.max<Float>(0, rgb.r), std.max<Float>(0, rgb.g), std.max<Float>(0, rgb.b));
        }

        public static XYZ Clamp(XYZ xyz, Float min, Float max)
        {
            return new XYZ(Clamp(xyz.X, min, max), Clamp(xyz.Y, min, max), Clamp(xyz.Z, min, max));
        }

        public static XYZ ClampZero(XYZ xyz)
        {
            return new XYZ(std.max<Float>(0, xyz.X), std.max<Float>(0, xyz.Y), std.max<Float>(0, xyz.Z));
        }

        public static XYZ Lerp(Float t, XYZ s1, XYZ s2) { return (1 - t) * s1 + t * s2; }

        public static Float LinearToSRGB(Float value)
        {
            if (value <= 0.0031308f)
                return 12.92f * value;
            // Minimax polynomial approximation from enoki's color.h.
            float sqrtValue = SafeSqrt(value);
            float p = EvaluatePolynomial(sqrtValue, -0.0016829072605308378f, 0.03453868659826638f,
                                         0.7642611304733891f, 2.0041169284241644f,
                                         0.7551545191665577f, -0.016202083165206348f);
            float q = EvaluatePolynomial(sqrtValue, 4.178892964897981e-7f,
                                         -0.00004375359692957097f, 0.03467195408529984f,
                                         0.6085338522168684f, 1.8970238036421054f, 1f);
            return p / q * value;
        }

        public static byte LinearToSRGB8(Float value, Float dither = 0)
        {
            if (value <= 0)
                return 0;
            if (value >= 1)
                return 255;
            return (byte)Clamp(pstd.round(255f * LinearToSRGB(value) + dither), 0, 255);
        }

        public static Float SRGBToLinear(float value)
        {
            if (value <= 0.04045f)
                return value * (1 / 12.92f);
            // Minimax polynomial approximation from enoki's color.h.
            float p = EvaluatePolynomial(value, -0.0163933279112946f, -0.7386328024653209f,
                                         -11.199318357635072f, -47.46726633009393f,
                                         -36.04572663838034f);
            float q = EvaluatePolynomial(value, -0.004261480793199332f, -19.140923959601675f,
                                         -59.096406619244426f, -18.225745396846637f, 1f);
            return p / q * value;
        }

        public static readonly Float[] SRGBToLinearLUT = {
            0.0000000000f, 0.0003035270f, 0.0006070540f, 0.0009105810f, 0.0012141080f, 0.0015176350f,
            0.0018211619f, 0.0021246888f, 0.0024282159f, 0.0027317430f, 0.0030352699f, 0.0033465356f,
            0.0036765069f, 0.0040247170f, 0.0043914421f, 0.0047769533f, 0.0051815170f, 0.0056053917f,
            0.0060488326f, 0.0065120910f, 0.0069954102f, 0.0074990317f, 0.0080231922f, 0.0085681248f,
            0.0091340570f, 0.0097212177f, 0.0103298230f, 0.0109600937f, 0.0116122449f, 0.0122864870f,
            0.0129830306f, 0.0137020806f, 0.0144438436f, 0.0152085144f, 0.0159962922f, 0.0168073755f,
            0.0176419523f, 0.0185002182f, 0.0193823613f, 0.0202885624f, 0.0212190095f, 0.0221738834f,
            0.0231533647f, 0.0241576303f, 0.0251868572f, 0.0262412224f, 0.0273208916f, 0.0284260381f,
            0.0295568332f, 0.0307134409f, 0.0318960287f, 0.0331047624f, 0.0343398079f, 0.0356013142f,
            0.0368894450f, 0.0382043645f, 0.0395462364f, 0.0409151986f, 0.0423114114f, 0.0437350273f,
            0.0451862030f, 0.0466650836f, 0.0481718220f, 0.0497065634f, 0.0512694679f, 0.0528606549f,
            0.0544802807f, 0.0561284944f, 0.0578054339f, 0.0595112406f, 0.0612460710f, 0.0630100295f,
            0.0648032799f, 0.0666259527f, 0.0684781820f, 0.0703601092f, 0.0722718611f, 0.0742135793f,
            0.0761853904f, 0.0781874284f, 0.0802198276f, 0.0822827145f, 0.0843762159f, 0.0865004659f,
            0.0886556059f, 0.0908417329f, 0.0930589810f, 0.0953074843f, 0.0975873619f, 0.0998987406f,
            0.1022417471f, 0.1046164930f, 0.1070231125f, 0.1094617173f, 0.1119324341f, 0.1144353822f,
            0.1169706732f, 0.1195384338f, 0.1221387982f, 0.1247718409f, 0.1274376959f, 0.1301364899f,
            0.1328683347f, 0.1356333494f, 0.1384316236f, 0.1412633061f, 0.1441284865f, 0.1470272839f,
            0.1499598026f, 0.1529261619f, 0.1559264660f, 0.1589608639f, 0.1620294005f, 0.1651322246f,
            0.1682693958f, 0.1714410931f, 0.1746473908f, 0.1778884083f, 0.1811642349f, 0.1844749898f,
            0.1878207624f, 0.1912016720f, 0.1946178079f, 0.1980693042f, 0.2015562356f, 0.2050787061f,
            0.2086368501f, 0.2122307271f, 0.2158605307f, 0.2195262313f, 0.2232279778f, 0.2269658893f,
            0.2307400703f, 0.2345506549f, 0.2383976579f, 0.2422811985f, 0.2462013960f, 0.2501583695f,
            0.2541521788f, 0.2581829131f, 0.2622507215f, 0.2663556635f, 0.2704978585f, 0.2746773660f,
            0.2788943350f, 0.2831487954f, 0.2874408960f, 0.2917706966f, 0.2961383164f, 0.3005438447f,
            0.3049873710f, 0.3094689548f, 0.3139887452f, 0.3185468316f, 0.3231432438f, 0.3277781308f,
            0.3324515820f, 0.3371636569f, 0.3419144452f, 0.3467040956f, 0.3515326977f, 0.3564002514f,
            0.3613068759f, 0.3662526906f, 0.3712377846f, 0.3762622178f, 0.3813261092f, 0.3864295185f,
            0.3915725648f, 0.3967553079f, 0.4019778669f, 0.4072403014f, 0.4125427008f, 0.4178851545f,
            0.4232677519f, 0.4286905527f, 0.4341537058f, 0.4396572411f, 0.4452012479f, 0.4507858455f,
            0.4564110637f, 0.4620770514f, 0.4677838385f, 0.4735315442f, 0.4793202281f, 0.4851499796f,
            0.4910208881f, 0.4969330430f, 0.5028865933f, 0.5088814497f, 0.5149177909f, 0.5209956765f,
            0.5271152258f, 0.5332764983f, 0.5394796133f, 0.5457245708f, 0.5520114899f, 0.5583404899f,
            0.5647116303f, 0.5711249113f, 0.5775805116f, 0.5840784907f, 0.5906189084f, 0.5972018838f,
            0.6038274169f, 0.6104956269f, 0.6172066331f, 0.6239604354f, 0.6307572126f, 0.6375969648f,
            0.6444797516f, 0.6514056921f, 0.6583748460f, 0.6653873324f, 0.6724432111f, 0.6795425415f,
            0.6866854429f, 0.6938719153f, 0.7011020184f, 0.7083759308f, 0.7156936526f, 0.7230552435f,
            0.7304608822f, 0.7379105687f, 0.7454043627f, 0.7529423237f, 0.7605246305f, 0.7681512833f,
            0.7758223414f, 0.7835379243f, 0.7912980318f, 0.7991028428f, 0.8069523573f, 0.8148466945f,
            0.8227858543f, 0.8307699561f, 0.8387991190f, 0.8468732834f, 0.8549926877f, 0.8631572723f,
            0.8713672161f, 0.8796223402f, 0.8879231811f, 0.8962693810f, 0.9046613574f, 0.9130986929f,
            0.9215820432f, 0.9301108718f, 0.9386858940f, 0.9473065734f, 0.9559735060f, 0.9646862745f,
            0.9734454751f, 0.9822505713f, 0.9911022186f, 1.0000000000f
        };

        public static Float SRGB8ToLinear(byte value) { return SRGBToLinearLUT[value]; }

        public static readonly SquareMatrix3 LMSFromXYZ = new SquareMatrix3(0.8951f, 0.2664f, -0.1614f,
                                                                           -0.7502f, 1.7135f, 0.0367f,
                                                                           0.0389f, -0.0685f, 1.0296f);
        public static readonly SquareMatrix3 XYZFromLMS = new SquareMatrix3(0.986993f, -0.147054f, 0.159963f,
                                                                           0.432305f, 0.51836f, 0.0492912f,
                                                                           -0.00852866f, 0.0400428f, 0.968487f);

        public static SquareMatrix3 WhiteBalance(Point2f srcWhite, Point2f targetWhite)
        {
            // Find LMS coefficients for source and target white
            XYZ srcXYZ = XYZ.FromxyY(srcWhite), dstXYZ = XYZ.FromxyY(targetWhite);
            XYZ srcLMS = LMSFromXYZ * srcXYZ, dstLMS = LMSFromXYZ * dstXYZ;

            // Return white balancing matrix for source and target white
            SquareMatrix3 LMScorrect = SquareMatrix3.Diag(
                dstLMS[0] / srcLMS[0], dstLMS[1] / srcLMS[1], dstLMS[2] / srcLMS[2]);
            return XYZFromLMS * LMScorrect * LMSFromXYZ;
        }
    }

}  // namespace pbrt
