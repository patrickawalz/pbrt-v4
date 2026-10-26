// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/colorspace.h, util/colorspace.cpp

using System;

namespace pbrt
{

    // RGBColorSpace Definition
    public sealed class RGBColorSpace
    {
        // RGBColorSpace Public Methods
        public RGBColorSpace(Point2f r, Point2f g, Point2f b, Spectrum illuminant,
                             RGBToSpectrumTable rgbToSpec, Allocator alloc)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.illuminant = new DenselySampledSpectrum(illuminant, alloc);
            this.rgbToSpectrumTable = rgbToSpec;
            // Compute whitepoint primaries and XYZ coordinates
            XYZ W = SpectrumToXYZ(illuminant);
            w = W.xy();
            XYZ R = XYZ.FromxyY(r), G = XYZ.FromxyY(g), B = XYZ.FromxyY(b);

            // Initialize XYZ color space conversion matrices
            SquareMatrix3 rgb = new SquareMatrix3(R.X, G.X, B.X, R.Y, G.Y, B.Y, R.Z, G.Z, B.Z);
            XYZ C = InvertOrExit(rgb) * W;
            XYZFromRGB = rgb * SquareMatrix3.Diag(C[0], C[1], C[2]);
            RGBFromXYZ = InvertOrExit(XYZFromRGB);
        }

        public RGBSigmoidPolynomial ToRGBCoeffs(RGB rgb)
        {
            DCHECK(rgb.r >= 0 && rgb.g >= 0 && rgb.b >= 0);
            return rgbToSpectrumTable.Apply(ClampZero(rgb));
        }

        public static void Init(Allocator alloc)
        {
            // Rec. ITU-R BT.709.3
            sRGB = new RGBColorSpace(new Point2f(.64f, .33f), new Point2f(.3f, .6f), new Point2f(.15f, .06f),
                                     GetNamedSpectrum("stdillum-D65"), RGBToSpectrumTable.sRGB, alloc);
            // P3-D65 (display)
            DCI_P3 = new RGBColorSpace(new Point2f(.68f, .32f), new Point2f(.265f, .690f), new Point2f(.15f, .06f),
                                       GetNamedSpectrum("stdillum-D65"), RGBToSpectrumTable.DCI_P3, alloc);
            // ITU-R Rec BT.2020
            Rec2020 = new RGBColorSpace(new Point2f(.708f, .292f), new Point2f(.170f, .797f), new Point2f(.131f, .046f),
                                        GetNamedSpectrum("stdillum-D65"), RGBToSpectrumTable.Rec2020, alloc);
            ACES2065_1 = new RGBColorSpace(new Point2f(.7347f, .2653f), new Point2f(0f, 1f), new Point2f(.0001f, -.077f),
                                           GetNamedSpectrum("illum-acesD60"), RGBToSpectrumTable.ACES2065_1, alloc);
        }

        // RGBColorSpace Public Members
        public Point2f r, g, b, w;
        public DenselySampledSpectrum illuminant;
        public SquareMatrix3 XYZFromRGB, RGBFromXYZ;
        public static RGBColorSpace sRGB, DCI_P3, Rec2020, ACES2065_1;

        public static bool operator ==(RGBColorSpace a, RGBColorSpace cs)
        {
            if (ReferenceEquals(a, cs)) return true;
            if (a is null || cs is null) return false;
            return (a.r == cs.r && a.g == cs.g && a.b == cs.b && a.w == cs.w &&
                    a.rgbToSpectrumTable == cs.rgbToSpectrumTable);
        }
        public static bool operator !=(RGBColorSpace a, RGBColorSpace cs) { return !(a == cs); }
        public override bool Equals(object o) { return o is RGBColorSpace cs && this == cs; }
        public override int GetHashCode() { return HashCode.Combine(r, g, b, w); }

        public override string ToString()
        {
            return $"[ RGBColorSpace r: {r} g: {g} b: {b} w: {w} illuminant: {illuminant} " +
                   $"RGBToXYZ: {XYZFromRGB} XYZToRGB: {RGBFromXYZ} ]";
        }

        public RGB LuminanceVector()
        {
            return new RGB(XYZFromRGB[1][0], XYZFromRGB[1][1], XYZFromRGB[1][2]);
        }

        public RGB ToRGB(XYZ xyz)  // Mul<RGB>(RGBFromXYZ, xyz)
        {
            RGB result = new RGB();
            for (int i = 0; i < 3; ++i)
            {
                result[i] = 0;
                for (int j = 0; j < 3; ++j)
                    result[i] += RGBFromXYZ[i][j] * xyz[j];
            }
            return result;
        }
        public XYZ ToXYZ(RGB rgb)  // Mul<XYZ>(XYZFromRGB, rgb)
        {
            XYZ result = new XYZ();
            for (int i = 0; i < 3; ++i)
            {
                result[i] = 0;
                for (int j = 0; j < 3; ++j)
                    result[i] += XYZFromRGB[i][j] * rgb[j];
            }
            return result;
        }

        public static RGBColorSpace GetNamed(string n)
        {
            string name = n.ToLowerInvariant();
            if (name == "aces2065-1")
                return ACES2065_1;
            else if (name == "rec2020")
                return Rec2020;
            else if (name == "dci-p3")
                return DCI_P3;
            else if (name == "srgb")
                return sRGB;
            else
                return null;
        }

        public static RGBColorSpace Lookup(Point2f r, Point2f g, Point2f b, Point2f w)
        {
            // Uses 1e-3 tolerance, which is fine for the known color spaces
            Func<Point2f, Point2f, bool> closeEnough = (Point2f a, Point2f bb) =>
            {
                return ((a.x == bb.x || std.abs((a.x - bb.x) / bb.x) < 1e-3) &&
                        (a.y == bb.y || std.abs((a.y - bb.y) / bb.y) < 1e-3));
            };

            foreach (RGBColorSpace cs in new RGBColorSpace[] { ACES2065_1, DCI_P3, Rec2020, sRGB })
            {
                if (closeEnough(r, cs.r) && closeEnough(g, cs.g) && closeEnough(b, cs.b) &&
                    closeEnough(w, cs.w))
                    return cs;
            }
            return null;
        }

        // RGBColorSpace Private Members
        private RGBToSpectrumTable rgbToSpectrumTable;
    }

    public static partial class Globals
    {
        public static SquareMatrix3 ConvertRGBColorSpace(RGBColorSpace from, RGBColorSpace to)
        {
            if (from == to)
                return new SquareMatrix3();
            return to.RGBFromXYZ * from.XYZFromRGB;
        }
    }

}  // namespace pbrt
