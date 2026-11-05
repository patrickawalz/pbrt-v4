// OSL port: src/liboslexec/opcolor.cpp, src/liboslexec/opcolor_impl.h, src/liboslexec/opcolor.h
//
// The colour shadeops (osl_blackbody_vf, osl_wavelength_color_vf, osl_luminance_*, osl_prepend_color_from, osl_transformc) and the ColorSystem class they
// use. Colours are Color3 (3 floats); Dual2<Color3> is Dual2v3 (9 floats). OSL_FAST_MATH is assumed (OIIO::fast_expm1 and fast_cbrt).
//
// The C++ entry points take an OpaqueExecContextPtr only to find the ShadingStateUniform's ColorSystem. Here the first parameter is kept (void* oec) and the
// ColorSystem is resolved through OslRuntime.get_colorsystem_hook, which defaults to one shared Rec709 ColorSystem.
//
// Colour spaces other than the built-in ones go to OpenColorIO in the C++ (ShadingContext::ocio_transform). There is no OCIO here: OslRuntime.ocio_transform_hook
// can supply one; without it (or when it declines) OslRuntime.unknown_colorspace_hook is called, as the C++ calls errorfmt, and the input is returned unchanged.

using System;
using System.Runtime.CompilerServices;
using OIIO;
using OSL.pvt;

namespace OSL
{
    public static partial class Hashes
    {
        public static readonly ulong Rec709 = new ustring("Rec709").hash();
        public static readonly ulong sRGB = new ustring("sRGB").hash();
        public static readonly ulong NTSC = new ustring("NTSC").hash();
        public static readonly ulong EBU = new ustring("EBU").hash();
        public static readonly ulong PAL = new ustring("PAL").hash();
        public static readonly ulong SECAM = new ustring("SECAM").hash();
        public static readonly ulong SMPTE = new ustring("SMPTE").hash();
        public static readonly ulong HDTV = new ustring("HDTV").hash();
        public static readonly ulong CIE = new ustring("CIE").hash();
        public static readonly ulong AdobeRGB = new ustring("AdobeRGB").hash();
        public static readonly ulong XYZ = new ustring("XYZ").hash();
        public static readonly ulong ACES2065_1 = new ustring("ACES2065-1").hash();
        public static readonly ulong ACEScg = new ustring("ACEScg").hash();
        public static readonly ulong RGB = new ustring("RGB").hash();
        public static readonly ulong rgb = new ustring("rgb").hash();
        public static readonly ulong hsv = new ustring("hsv").hash();
        public static readonly ulong hsl = new ustring("hsl").hash();
        public static readonly ulong YIQ = new ustring("YIQ").hash();
        public static readonly ulong xyY = new ustring("xyY").hash();
    }

    // Transform a colour through a colour space the ColorSystem does not know (OpenColorIO in the C++). Returns false when the transform is unknown.
    public delegate bool OcioTransformDelegate(ulong fromspace, ulong tospace, Color3 c, out Color3 result);

    public unsafe delegate ColorSystem ColorSystemProvider(void* oec);
}

namespace OSL.pvt
{
    // The colour operations of opcolor_impl.h, for Color3 and for Dual2<Color3> (Dual2v3)
    public static class ColorOps
    {
        private const float BB_DRAPER = 800.0f;
        public const float BB_MAX_TABLE_RANGE = 12000.0f;
        private const float BB_TABLE_YPOWER = 5.0f;
        private const float BB_TABLE_SPACING = 2.0f;

        // CIE colour matching functions xBar, yBar, and zBar for wavelengths from 380 through 780 nanometers, every 5 nanometers
        private static readonly float[] cie_colour_match = new float[81 * 3]
        {
            0.0014f, 0.0000f, 0.0065f, 0.0022f, 0.0001f, 0.0105f, 0.0042f, 0.0001f, 0.0201f,
            0.0076f, 0.0002f, 0.0362f, 0.0143f, 0.0004f, 0.0679f, 0.0232f, 0.0006f, 0.1102f,
            0.0435f, 0.0012f, 0.2074f, 0.0776f, 0.0022f, 0.3713f, 0.1344f, 0.0040f, 0.6456f,
            0.2148f, 0.0073f, 1.0391f, 0.2839f, 0.0116f, 1.3856f, 0.3285f, 0.0168f, 1.6230f,
            0.3483f, 0.0230f, 1.7471f, 0.3481f, 0.0298f, 1.7826f, 0.3362f, 0.0380f, 1.7721f,
            0.3187f, 0.0480f, 1.7441f, 0.2908f, 0.0600f, 1.6692f, 0.2511f, 0.0739f, 1.5281f,
            0.1954f, 0.0910f, 1.2876f, 0.1421f, 0.1126f, 1.0419f, 0.0956f, 0.1390f, 0.8130f,
            0.0580f, 0.1693f, 0.6162f, 0.0320f, 0.2080f, 0.4652f, 0.0147f, 0.2586f, 0.3533f,
            0.0049f, 0.3230f, 0.2720f, 0.0024f, 0.4073f, 0.2123f, 0.0093f, 0.5030f, 0.1582f,
            0.0291f, 0.6082f, 0.1117f, 0.0633f, 0.7100f, 0.0782f, 0.1096f, 0.7932f, 0.0573f,
            0.1655f, 0.8620f, 0.0422f, 0.2257f, 0.9149f, 0.0298f, 0.2904f, 0.9540f, 0.0203f,
            0.3597f, 0.9803f, 0.0134f, 0.4334f, 0.9950f, 0.0087f, 0.5121f, 1.0000f, 0.0057f,
            0.5945f, 0.9950f, 0.0039f, 0.6784f, 0.9786f, 0.0027f, 0.7621f, 0.9520f, 0.0021f,
            0.8425f, 0.9154f, 0.0018f, 0.9163f, 0.8700f, 0.0017f, 0.9786f, 0.8163f, 0.0014f,
            1.0263f, 0.7570f, 0.0011f, 1.0567f, 0.6949f, 0.0010f, 1.0622f, 0.6310f, 0.0008f,
            1.0456f, 0.5668f, 0.0006f, 1.0026f, 0.5030f, 0.0003f, 0.9384f, 0.4412f, 0.0002f,
            0.8544f, 0.3810f, 0.0002f, 0.7514f, 0.3210f, 0.0001f, 0.6424f, 0.2650f, 0.0000f,
            0.5419f, 0.2170f, 0.0000f, 0.4479f, 0.1750f, 0.0000f, 0.3608f, 0.1382f, 0.0000f,
            0.2835f, 0.1070f, 0.0000f, 0.2187f, 0.0816f, 0.0000f, 0.1649f, 0.0610f, 0.0000f,
            0.1212f, 0.0446f, 0.0000f, 0.0874f, 0.0320f, 0.0000f, 0.0636f, 0.0232f, 0.0000f,
            0.0468f, 0.0170f, 0.0000f, 0.0329f, 0.0119f, 0.0000f, 0.0227f, 0.0082f, 0.0000f,
            0.0158f, 0.0057f, 0.0000f, 0.0114f, 0.0041f, 0.0000f, 0.0081f, 0.0029f, 0.0000f,
            0.0058f, 0.0021f, 0.0000f, 0.0041f, 0.0015f, 0.0000f, 0.0029f, 0.0010f, 0.0000f,
            0.0020f, 0.0007f, 0.0000f, 0.0014f, 0.0005f, 0.0000f, 0.0010f, 0.0004f, 0.0000f,
            0.0007f, 0.0002f, 0.0000f, 0.0005f, 0.0002f, 0.0000f, 0.0003f, 0.0001f, 0.0000f,
            0.0002f, 0.0001f, 0.0000f, 0.0002f, 0.0001f, 0.0000f, 0.0001f, 0.0000f, 0.0000f,
            0.0001f, 0.0000f, 0.0000f, 0.0001f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f
        };

        public static void clamp_zero(ref Color3 c)
        {
            if (c.x < 0.0f)
            {
                c.x = 0.0f;
            }
            if (c.y < 0.0f)
            {
                c.y = 0.0f;
            }
            if (c.z < 0.0f)
            {
                c.z = 0.0f;
            }
        }

        // sfm::min_val and sfm::max_val
        private static float min_val(float left, float right)
        {
            return (right > left) ? left : right;
        }

        private static float max_val(float left, float right)
        {
            return (right > left) ? right : left;
        }

        private static Dual2f min_val(Dual2f left, Dual2f right)
        {
            return (right.x > left.x) ? left : right;
        }

        private static Dual2f max_val(Dual2f left, Dual2f right)
        {
            return (right.x > left.x) ? right : left;
        }

        // For a given wavelength lambda (in nm), return the XYZ triple giving the XYZ color corresponding to that single wavelength
        public static Color3 wavelength_color_XYZ(float lambda_nm)
        {
            float ii = (lambda_nm - 380.0f) / 5.0f; // scaled 0..80
            int i = fmath.float_to_int(ii);
            if ((i < 0) | (i >= 80))
            {
                return new Color3(0.0f, 0.0f, 0.0f);
            }
            float remainder = ii - i;
            int si = 3 * i;
            Color3 a = new Color3(cie_colour_match[si + 0], cie_colour_match[si + 1], cie_colour_match[si + 2]);
            Color3 b = new Color3(cie_colour_match[si + 3], cie_colour_match[si + 4], cie_colour_match[si + 5]);
            return new Color3(fmath.lerp(a.x, b.x, remainder), fmath.lerp(a.y, b.y, remainder), fmath.lerp(a.z, b.z, remainder));
        }

        public static float BB_TABLE_MAP(float i)
        {
            float is_ = MathF.Sqrt(i);
            float ip = is_ * is_ * is_; // ^3/2
            return ip * BB_TABLE_SPACING + BB_DRAPER;
        }

        public static float BB_TABLE_UNMAP(float T)
        {
            float t = (T - BB_DRAPER) / BB_TABLE_SPACING;
            float ic = fmath.fast_cbrt(t);
            return ic * ic; // ^2/3
        }

        // Planck's radiation law: the black body emittance at a temperature (in Kelvin) and wavelength (in nm)
        public struct bb_spectrum
        {
            private float m_temp;

            public bb_spectrum(float temperature)
            {
                m_temp = temperature;
            }

            public float evaluate(float wavelength_nm)
            {
                float wlm = wavelength_nm * 1e-9f; // Wavelength in meters
                const float c1 = 3.74183e-16f; // 2*pi*h*c^2, W*m^2
                const float c2 = 1.4388e-2f; // h*c/k, m*K
                float wlm2 = wlm * wlm;
                float wlm4 = wlm2 * wlm2;
                float wlm5 = wlm4 * wlm;
                float inverse_of_wlm5 = 1.0f / wlm5;
                return (float)((c1 * inverse_of_wlm5) / fmath.fast_expm1(c2 / (wlm * m_temp)));
            }
        }

        // Integrate the CIE color matching values, weighted by the spectrum, returning the aggregate XYZ color
        public static Color3 spectrum_to_XYZ(bb_spectrum spec_intens)
        {
            float X = 0.0f;
            float Y = 0.0f;
            float Z = 0.0f;
            float dlambda = (float)(5.0f * 1e-9); // in meters (the product is a double in the C++)
            for (int i = 0; i < 81; ++i)
            {
                float lambda = 380.0f + 5.0f * i;
                float Me = spec_intens.evaluate(lambda) * dlambda;
                int si = 3 * i;
                X += Me * cie_colour_match[si + 0];
                Y += Me * cie_colour_match[si + 1];
                Z += Me * cie_colour_match[si + 2];
            }
            return new Color3(X, Y, Z);
        }

        public static Color3 colpow(Color3 c, float p)
        {
            return new Color3(MathF.Pow(c.x, p), MathF.Pow(c.y, p), MathF.Pow(c.z, p));
        }

        // Imath: Vec3 * Matrix33
        public static Color3 mul(Color3 v, Matrix33f m)
        {
            return new Color3(v.x * m.m00 + v.y * m.m10 + v.z * m.m20, v.x * m.m01 + v.y * m.m11 + v.z * m.m21, v.x * m.m02 + v.y * m.m12 + v.z * m.m22);
        }

        // dual_vec.h: Dual<Color3> * Matrix33
        public static Dual2v3 mul(Dual2v3 src, Matrix33f m)
        {
            Dual2f src0 = DualMath.comp_x(src);
            Dual2f src1 = DualMath.comp_y(src);
            Dual2f src2 = DualMath.comp_z(src);
            Dual2f a = src0 * m.m00 + src1 * m.m10 + src2 * m.m20;
            Dual2f b = src0 * m.m01 + src1 * m.m11 + src2 * m.m21;
            Dual2f c = src0 * m.m02 + src1 * m.m12 + src2 * m.m22;
            return DualMath.make_Vec3(a, b, c);
        }

        private static Color3 make_Color3(float x, float y, float z)
        {
            return new Color3(x, y, z);
        }

        private static Dual2v3 make_Color3(Dual2f x, Dual2f y, Dual2f z)
        {
            return DualMath.make_Vec3(x, y, z);
        }

        private static Dual2f dual_float(int i)
        {
            return new Dual2f((float)i);
        }

        public static Color3 hsv_to_rgb(Color3 hsv)
        {
            float h = hsv.x;
            float s = hsv.y;
            float v = hsv.z;
            if (s < 0.0001f)
            {
                return make_Color3(v, v, v);
            }
            h = 6.0f * (h - MathF.Floor(h)); // expand to [0..6)
            int hi = fmath.ifloor(h);
            float f = h - (float)hi;
            float p = v * (1.0f - s);
            float q = v * (1.0f - s * f);
            float t = v * (1.0f - s * (1.0f - f));
            switch (hi)
            {
                case 0:
                    return make_Color3(v, t, p);
                case 1:
                    return make_Color3(q, v, p);
                case 2:
                    return make_Color3(p, v, t);
                case 3:
                    return make_Color3(p, q, v);
                case 4:
                    return make_Color3(t, p, v);
                default:
                    return make_Color3(v, p, q);
            }
        }

        public static Dual2v3 hsv_to_rgb(Dual2v3 hsv)
        {
            Dual2f h = DualMath.comp_x(hsv);
            Dual2f s = DualMath.comp_y(hsv);
            Dual2f v = DualMath.comp_z(hsv);
            if (s.x < 0.0001f)
            {
                return make_Color3(v, v, v);
            }
            h = 6.0f * (h - MathF.Floor(h.x)); // expand to [0..6)
            int hi = fmath.ifloor(h.x);
            Dual2f f = h - dual_float(hi);
            Dual2f p = v * (1.0f - s);
            Dual2f q = v * (1.0f - s * f);
            Dual2f t = v * (1.0f - s * (1.0f - f));
            switch (hi)
            {
                case 0:
                    return make_Color3(v, t, p);
                case 1:
                    return make_Color3(q, v, p);
                case 2:
                    return make_Color3(p, v, t);
                case 3:
                    return make_Color3(p, q, v);
                case 4:
                    return make_Color3(t, p, v);
                default:
                    return make_Color3(v, p, q);
            }
        }

        public static Color3 rgb_to_hsv(Color3 rgb)
        {
            float r = rgb.x;
            float g = rgb.y;
            float b = rgb.z;
            float mincomp = min_val(r, min_val(g, b));
            float maxcomp = max_val(r, max_val(g, b));
            float delta = maxcomp - mincomp; // chroma
            float v = maxcomp;
            float s = 0.0f;
            if (maxcomp > 0.0f)
            {
                s = delta / maxcomp;
            }
            float h = 0.0f;
            if (s > 0.0f)
            {
                float k;
                float x;
                float y;
                if (r >= maxcomp)
                {
                    k = 0.0f / 6.0f;
                    x = g;
                    y = b;
                }
                else if (g >= maxcomp)
                {
                    k = 2.0f / 6.0f;
                    x = b;
                    y = r;
                }
                else
                {
                    k = 4.0f / 6.0f;
                    x = r;
                    y = g;
                }
                h = k + (x - y) / (6.0f * delta);
                if (h < 0.0f)
                {
                    h += 1.0f;
                }
            }
            return make_Color3(h, s, v);
        }

        public static Dual2v3 rgb_to_hsv(Dual2v3 rgb)
        {
            Dual2f r = DualMath.comp_x(rgb);
            Dual2f g = DualMath.comp_y(rgb);
            Dual2f b = DualMath.comp_z(rgb);
            Dual2f mincomp = min_val(r, min_val(g, b));
            Dual2f maxcomp = max_val(r, max_val(g, b));
            Dual2f delta = maxcomp - mincomp; // chroma
            Dual2f v = maxcomp;
            Dual2f s = new Dual2f(0.0f);
            if (maxcomp.x > 0.0f)
            {
                s = delta / maxcomp;
            }
            Dual2f h = new Dual2f(0.0f);
            if (s.x > 0.0f)
            {
                float k;
                Dual2f x;
                Dual2f y;
                if (r.x >= maxcomp.x)
                {
                    k = 0.0f / 6.0f;
                    x = g;
                    y = b;
                }
                else if (g.x >= maxcomp.x)
                {
                    k = 2.0f / 6.0f;
                    x = b;
                    y = r;
                }
                else
                {
                    k = 4.0f / 6.0f;
                    x = r;
                    y = g;
                }
                h = k + (x - y) / (6.0f * delta);
                if (h.x < 0.0f)
                {
                    h += 1.0f;
                }
            }
            return make_Color3(h, s, v);
        }

        public static Color3 hsl_to_rgb(Color3 hsl)
        {
            float h = hsl.x;
            float s = hsl.y;
            float l = hsl.z;
            // Easiest to convert hsl -> hsv, then hsv -> RGB (per Foley & van Dam)
            float v = (l <= 0.5f) ? (l * (1.0f + s)) : (l * (1.0f - s) + s);
            if (v <= 0.0f)
            {
                return make_Color3(0.0f, 0.0f, 0.0f);
            }
            float min = 2.0f * l - v;
            s = (v - min) / v;
            return hsv_to_rgb(make_Color3(h, s, v));
        }

        public static Dual2v3 hsl_to_rgb(Dual2v3 hsl)
        {
            Dual2f h = DualMath.comp_x(hsl);
            Dual2f s = DualMath.comp_y(hsl);
            Dual2f l = DualMath.comp_z(hsl);
            Dual2f v = (l.x <= 0.5f) ? (l * (1.0f + s)) : (l * (1.0f - s) + s);
            if (v.x <= 0.0f)
            {
                return make_Color3(new Dual2f(0.0f), new Dual2f(0.0f), new Dual2f(0.0f));
            }
            Dual2f min = 2.0f * l - v;
            s = (v - min) / v;
            return hsv_to_rgb(make_Color3(h, s, v));
        }

        public static Color3 rgb_to_hsl(Color3 rgb)
        {
            float minval = min_val(rgb.x, min_val(rgb.y, rgb.z));
            Color3 hsv = rgb_to_hsv(rgb);
            float maxval = hsv.z; // v == maxval
            float h = hsv.x;
            float s;
            float l = (minval + maxval) / 2.0f;
            if (minval == maxval)
            {
                s = 0.0f; // special 'achromatic' case, hue is 0
            }
            else
            {
                float min2max = (maxval - minval);
                float divisor;
                if (l <= 0.5f)
                {
                    divisor = (maxval + minval);
                }
                else
                {
                    divisor = (2.0f - maxval - minval);
                }
                s = min2max / divisor;
            }
            return make_Color3(h, s, l);
        }

        public static Dual2v3 rgb_to_hsl(Dual2v3 rgb)
        {
            Dual2f minval = min_val(DualMath.comp_x(rgb), min_val(DualMath.comp_y(rgb), DualMath.comp_z(rgb)));
            Dual2v3 hsv = rgb_to_hsv(rgb);
            Dual2f maxval = DualMath.comp_z(hsv); // v == maxval
            Dual2f h = DualMath.comp_x(hsv);
            Dual2f s;
            Dual2f l = (minval + maxval) / 2.0f;
            if (minval.x == maxval.x)
            {
                s = new Dual2f(0.0f); // special 'achromatic' case, hue is 0
            }
            else
            {
                Dual2f min2max = (maxval - minval);
                Dual2f divisor;
                if (l.x <= 0.5f)
                {
                    divisor = (maxval + minval);
                }
                else
                {
                    divisor = (2.0f - maxval - minval);
                }
                s = min2max / divisor;
            }
            return make_Color3(h, s, l);
        }

        private static readonly Matrix33f YIQ_to_rgb_matrix = new Matrix33f(1.0000f, 1.0000f, 1.0000f, 0.9557f, -0.2716f, -1.1082f, 0.6199f, -0.6469f, 1.7051f);
        private static readonly Matrix33f rgb_to_YIQ_matrix = new Matrix33f(0.299f, 0.596f, 0.212f, 0.587f, -0.275f, -0.523f, 0.114f, -0.321f, 0.311f);

        public static Color3 YIQ_to_rgb(Color3 YIQ)
        {
            return mul(YIQ, YIQ_to_rgb_matrix);
        }

        public static Dual2v3 YIQ_to_rgb(Dual2v3 YIQ)
        {
            return mul(YIQ, YIQ_to_rgb_matrix);
        }

        public static Color3 rgb_to_YIQ(Color3 rgb)
        {
            return mul(rgb, rgb_to_YIQ_matrix);
        }

        public static Dual2v3 rgb_to_YIQ(Dual2v3 rgb)
        {
            return mul(rgb, rgb_to_YIQ_matrix);
        }

        public static Color3 XYZ_to_xyY(Color3 XYZ)
        {
            float n = (XYZ.x + XYZ.y + XYZ.z);
            float n_inv = (n >= 1.0e-6f ? 1.0f / n : 0.0f);
            return make_Color3(XYZ.x * n_inv, XYZ.y * n_inv, XYZ.y);
        }

        public static Dual2v3 XYZ_to_xyY(Dual2v3 XYZ)
        {
            Dual2f X = DualMath.comp_x(XYZ);
            Dual2f Y = DualMath.comp_y(XYZ);
            Dual2f Z = DualMath.comp_z(XYZ);
            Dual2f n = (X + Y + Z);
            Dual2f n_inv = (n.x >= 1.0e-6f ? 1.0f / n : new Dual2f(0.0f));
            return make_Color3(X * n_inv, Y * n_inv, Y);
        }

        public static Color3 xyY_to_XYZ(Color3 xyY)
        {
            float Y = xyY.z;
            float Y_y = (xyY.y > 1.0e-6f ? Y / xyY.y : 0.0f);
            float X = Y_y * xyY.x;
            float Z = Y_y * (1.0f - xyY.x - xyY.y);
            return make_Color3(X, Y, Z);
        }

        public static Dual2v3 xyY_to_XYZ(Dual2v3 xyY)
        {
            Dual2f x = DualMath.comp_x(xyY);
            Dual2f y = DualMath.comp_y(xyY);
            Dual2f Y = DualMath.comp_z(xyY);
            Dual2f Y_y = (y.x > 1.0e-6f ? Y / y : new Dual2f(0.0f));
            Dual2f X = Y_y * x;
            Dual2f Z = Y_y * (1.0f - x - y);
            return make_Color3(X, Y, Z);
        }

        private static float sRGB_to_linear_channel(float x)
        {
            return (x <= 0.04045f) ? (x * (1.0f / 12.92f)) : fmath.safe_pow((x + 0.055f) * (1.0f / 1.055f), 2.4f);
        }

        private static Dual2f sRGB_to_linear_channel(Dual2f x)
        {
            return (x.x <= 0.04045f) ? (x * (1.0f / 12.92f)) : DualMath.safe_pow((x + 0.055f) * (1.0f / 1.055f), new Dual2f(2.4f));
        }

        public static Color3 sRGB_to_linear(Color3 srgb)
        {
            return make_Color3(sRGB_to_linear_channel(srgb.x), sRGB_to_linear_channel(srgb.y), sRGB_to_linear_channel(srgb.z));
        }

        public static Dual2v3 sRGB_to_linear(Dual2v3 srgb)
        {
            Dual2f r = DualMath.comp_x(srgb);
            Dual2f g = DualMath.comp_y(srgb);
            Dual2f b = DualMath.comp_z(srgb);
            return make_Color3(sRGB_to_linear_channel(r), sRGB_to_linear_channel(g), sRGB_to_linear_channel(b));
        }

        private static float linear_to_sRGB_channel(float x)
        {
            return (x <= 0.0031308f) ? (12.92f * x) : (1.055f * fmath.safe_pow(x, 1.0f / 2.4f) - 0.055f);
        }

        private static Dual2f linear_to_sRGB_channel(Dual2f x)
        {
            return (x.x <= 0.0031308f) ? (12.92f * x) : (1.055f * DualMath.safe_pow(x, new Dual2f(1.0f / 2.4f)) - 0.055f);
        }

        public static Color3 linear_to_sRGB(Color3 rgb)
        {
            return make_Color3(linear_to_sRGB_channel(rgb.x), linear_to_sRGB_channel(rgb.y), linear_to_sRGB_channel(rgb.z));
        }

        public static Dual2v3 linear_to_sRGB(Dual2v3 rgb)
        {
            Dual2f r = DualMath.comp_x(rgb);
            Dual2f g = DualMath.comp_y(rgb);
            Dual2f b = DualMath.comp_z(rgb);
            return make_Color3(linear_to_sRGB_channel(r), linear_to_sRGB_channel(g), linear_to_sRGB_channel(b));
        }
    }

    // A colour system is defined by the CIE x and y coordinates of its three primary illuminants and its white point
    public sealed class ColorSystem
    {
        public struct Chroma
        {
            public float xRed, yRed, xGreen, yGreen, xBlue, yBlue, xWhite, yWhite;

            public Chroma(float xRed, float yRed, float xGreen, float yGreen, float xBlue, float yBlue, float xWhite, float yWhite)
            {
                this.xRed = xRed;
                this.yRed = yRed;
                this.xGreen = xGreen;
                this.yGreen = yGreen;
                this.xBlue = xBlue;
                this.yBlue = yBlue;
                this.xWhite = xWhite;
                this.yWhite = yWhite;
            }
        }

        // White point chromaticities
        private const float IlluminantC_x = 0.3101f; // For NTSC television
        private const float IlluminantC_y = 0.3162f;
        private const float IlluminantD65_x = 0.3127f; // For EBU and SMPTE
        private const float IlluminantD65_y = 0.3291f;
        private const float IlluminantE_x = 0.33333333f; // CIE equal-energy illuminant
        private const float IlluminantE_y = 0.33333333f;
        private const float IlluminantACES_x = 0.32168f; // For ACES, approximate D60
        private const float IlluminantACES_y = 0.33767f;

        private static readonly Chroma[] k_color_systems = new Chroma[13]
        {
            /* 0  Rec709     */ new Chroma(0.64f, 0.33f, 0.30f, 0.60f, 0.15f, 0.06f, IlluminantD65_x, IlluminantD65_y),
            /* 1  sRGB       */ new Chroma(0.64f, 0.33f, 0.30f, 0.60f, 0.15f, 0.06f, IlluminantD65_x, IlluminantD65_y),
            /* 2  NTSC       */ new Chroma(0.67f, 0.33f, 0.21f, 0.71f, 0.14f, 0.08f, IlluminantC_x, IlluminantC_y),
            /* 3  EBU        */ new Chroma(0.64f, 0.33f, 0.29f, 0.60f, 0.15f, 0.06f, IlluminantD65_x, IlluminantD65_y),
            /* 4  PAL        */ new Chroma(0.64f, 0.33f, 0.29f, 0.60f, 0.15f, 0.06f, IlluminantD65_x, IlluminantD65_y),
            /* 5  SECAM      */ new Chroma(0.64f, 0.33f, 0.29f, 0.60f, 0.15f, 0.06f, IlluminantD65_x, IlluminantD65_y),
            /* 6  SMPTE      */ new Chroma(0.630f, 0.340f, 0.310f, 0.595f, 0.155f, 0.070f, IlluminantD65_x, IlluminantD65_y),
            /* 7  HDTV       */ new Chroma(0.670f, 0.330f, 0.210f, 0.710f, 0.150f, 0.060f, IlluminantD65_x, IlluminantD65_y),
            /* 8  CIE        */ new Chroma(0.7355f, 0.2645f, 0.2658f, 0.7243f, 0.1669f, 0.0085f, IlluminantE_x, IlluminantE_y),
            /* 9  AdobeRGB   */ new Chroma(0.64f, 0.33f, 0.21f, 0.71f, 0.15f, 0.06f, IlluminantD65_x, IlluminantD65_y),
            /* 10 XYZ        */ new Chroma(1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, IlluminantE_x, IlluminantE_y),
            /* 11 ACES2065-1 */ new Chroma(0.7347f, 0.2653f, 0.0f, 1.0f, 0.0001f, -0.077f, IlluminantACES_x, IlluminantACES_y),
            /* 12 ACEScg     */ new Chroma(0.713f, 0.293f, 0.165f, 0.83f, 0.128f, 0.044f, IlluminantACES_x, IlluminantACES_y),
        };

        private Color3 m_Red, m_Green, m_Blue; // Color primaries (xyY)
        private Color3 m_White; // White point (xyY)
        private Matrix33f m_XYZ2RGB; // XYZ to RGB conversion matrix
        private Matrix33f m_RGB2XYZ; // RGB to XYZ conversion matrix
        private Color3 m_luminance_scale; // Scaling for RGB->luma
        private readonly Color3[] m_blackbody_table = new Color3[317]; // Precomputed blackbody table
        private ulong m_colorspace; // What RGB colors mean

        // Starts in Rec709, the default of the shading system's "colorspace" attribute
        public ColorSystem()
        {
            set_colorspace(Hashes.Rec709);
        }

        public ColorSystem(ulong colorspace)
        {
            set_colorspace(colorspace);
        }

        public static bool fromString(ulong colorspace, out Chroma chroma)
        {
            int index = -1;
            if (colorspace == Hashes.Rec709)
            {
                index = 0;
            }
            else if (colorspace == Hashes.sRGB)
            {
                index = 1;
            }
            else if (colorspace == Hashes.NTSC)
            {
                index = 2;
            }
            else if (colorspace == Hashes.EBU)
            {
                index = 3;
            }
            else if (colorspace == Hashes.PAL)
            {
                index = 4;
            }
            else if (colorspace == Hashes.SECAM)
            {
                index = 5;
            }
            else if (colorspace == Hashes.SMPTE)
            {
                index = 6;
            }
            else if (colorspace == Hashes.HDTV)
            {
                index = 7;
            }
            else if (colorspace == Hashes.CIE)
            {
                index = 8;
            }
            else if (colorspace == Hashes.AdobeRGB)
            {
                index = 9;
            }
            else if (colorspace == Hashes.XYZ)
            {
                index = 10;
            }
            else if (colorspace == Hashes.ACES2065_1)
            {
                index = 11;
            }
            else if (colorspace == Hashes.ACEScg)
            {
                index = 12;
            }
            if (index < 0)
            {
                chroma = default;
                return false;
            }
            chroma = k_color_systems[index];
            return true;
        }

        public ulong colorspace()
        {
            return m_colorspace;
        }

        public Color3 luminance_scale()
        {
            return m_luminance_scale;
        }

        // Convert an XYZ color to RGB in our preferred color space
        public Color3 XYZ_to_RGB(Color3 XYZ)
        {
            return ColorOps.mul(XYZ, m_XYZ2RGB);
        }

        public Dual2v3 XYZ_to_RGB(Dual2v3 XYZ)
        {
            return ColorOps.mul(XYZ, m_XYZ2RGB);
        }

        // Convert an RGB color in our preferred color space to XYZ
        public Color3 RGB_to_XYZ(Color3 RGB)
        {
            return ColorOps.mul(RGB, m_RGB2XYZ);
        }

        public Dual2v3 RGB_to_XYZ(Dual2v3 RGB)
        {
            return ColorOps.mul(RGB, m_RGB2XYZ);
        }

        // Return the luminance of an RGB color in the current color space
        public float luminance(Color3 RGB)
        {
            return RGB.x * m_luminance_scale.x + RGB.y * m_luminance_scale.y + RGB.z * m_luminance_scale.z;
        }

        public bool can_lookup_blackbody(float T)
        {
            return (T < ColorOps.BB_MAX_TABLE_RANGE);
        }

        public Color3 lookup_blackbody_rgb(float T)
        {
            if (T < 800.0f)
            {
                return new Color3(1.0e-6f, 0.0f, 0.0f); // very very dim red
            }
            float t = ColorOps.BB_TABLE_UNMAP(T);
            int ti = fmath.float_to_int(t);
            float remainder = t - ti;
            Color3 a = m_blackbody_table[ti];
            Color3 b = m_blackbody_table[ti + 1];
            Color3 rgb = new Color3(fmath.lerp(a.x, b.x, remainder), fmath.lerp(a.y, b.y, remainder), fmath.lerp(a.z, b.z, remainder));
            Color3 rgb2 = rgb * rgb;
            Color3 rgb4 = rgb2 * rgb2;
            return rgb4 * rgb; // ^5
        }

        // Expensive real computation without lookup table
        public Color3 compute_blackbody_rgb(float T)
        {
            ColorOps.bb_spectrum spec = new ColorOps.bb_spectrum(T);
            Color3 rgb = XYZ_to_RGB(ColorOps.spectrum_to_XYZ(spec));
            ColorOps.clamp_zero(ref rgb);
            return rgb;
        }

        // The RGB in the current color space for blackbody radiation at temperature T (in Kelvin)
        public Color3 blackbody_rgb(float T)
        {
            if (can_lookup_blackbody(T))
            {
                return lookup_blackbody_rgb(T);
            }
            return compute_blackbody_rgb(T);
        }

        // Set the current color space
        public bool set_colorspace(ulong colorspace)
        {
            if (colorspace == m_colorspace)
            {
                return true;
            }
            Chroma chroma;
            if (!fromString(colorspace, out chroma))
            {
                return false;
            }
            m_colorspace = colorspace;

            m_Red = new Color3(chroma.xRed, chroma.yRed, 0.0f);
            m_Green = new Color3(chroma.xGreen, chroma.yGreen, 0.0f);
            m_Blue = new Color3(chroma.xBlue, chroma.yBlue, 0.0f);
            m_White = new Color3(chroma.xWhite, chroma.yWhite, 0.0f);
            // set z values to normalize
            m_Red.z = 1.0f - (m_Red.x + m_Red.y);
            m_Green.z = 1.0f - (m_Green.x + m_Green.y);
            m_Blue.z = 1.0f - (m_Blue.x + m_Blue.y);
            m_White.z = 1.0f - (m_White.x + m_White.y);

            Color3 R = m_Red;
            Color3 G = m_Green;
            Color3 B = m_Blue;
            Color3 W = m_White;
            // xyz -> rgb matrix, before scaling to white.
            Color3 r = new Color3(G.y * B.z - B.y * G.z, B.x * G.z - G.x * B.z, G.x * B.y - B.x * G.y);
            Color3 g = new Color3(B.y * R.z - R.y * B.z, R.x * B.z - B.x * R.z, B.x * R.y - R.x * B.y);
            Color3 b = new Color3(R.y * G.z - G.y * R.z, G.x * R.z - R.x * G.z, R.x * G.y - G.x * R.y);
            // White scaling factor
            Color3 w = new Color3(r.x * W.x + r.y * W.y + r.z * W.z, g.x * W.x + g.y * W.y + g.z * W.z, b.x * W.x + b.y * W.y + b.z * W.z);
            if (W.y != 0.0f) // divide by W.y to scale luminance to 1.0
            {
                w = w * (1.0f / W.y);
            }
            // xyz -> rgb matrix, correctly scaled to white.
            r = r / w.x;
            g = g / w.y;
            b = b / w.z;
            m_XYZ2RGB = new Matrix33f(r.x, g.x, b.x, r.y, g.y, b.y, r.z, g.z, b.z);
            m_RGB2XYZ = m_XYZ2RGB.inverse();
            m_luminance_scale = new Color3(m_RGB2XYZ.m01, m_RGB2XYZ.m11, m_RGB2XYZ.m21);

            // Mathematical imprecision can lead to the luminance scale not quite summing to 1.0.  If it's very close, adjust to make it exact.
            float lum2 = (1.0f - m_luminance_scale.x - m_luminance_scale.y);
            if (MathF.Abs(lum2 - m_luminance_scale.z) < 0.001f)
            {
                m_luminance_scale.z = lum2;
            }

            // Precompute a table of blackbody values
            float lastT = 0.0f;
            for (int i = 0; lastT <= ColorOps.BB_MAX_TABLE_RANGE; ++i)
            {
                float T = ColorOps.BB_TABLE_MAP((float)i);
                lastT = T;
                ColorOps.bb_spectrum spec = new ColorOps.bb_spectrum(T);
                Color3 rgb = XYZ_to_RGB(ColorOps.spectrum_to_XYZ(spec));
                ColorOps.clamp_zero(ref rgb);
                rgb = ColorOps.colpow(rgb, 1.0f / 5.0f);
                m_blackbody_table[i] = rgb;
            }
            return true;
        }

        // ColorSystem::ocio_transform: through the OpenColorIO hook if there is one, otherwise report the unknown transform and pass the colour through
        public Color3 ocio_transform(ulong fromspace, ulong tospace, Color3 C)
        {
            Color3 Cout;
            if (OslRuntime.ocio_transform_hook != null && OslRuntime.ocio_transform_hook(fromspace, tospace, C, out Cout))
            {
                return Cout;
            }
            if (OslRuntime.unknown_colorspace_hook != null)
            {
                OslRuntime.unknown_colorspace_hook(fromspace, tospace);
            }
            return C;
        }

        public Dual2v3 ocio_transform(ulong fromspace, ulong tospace, Dual2v3 C)
        {
            if (OslRuntime.ocio_transform_hook != null)
            {
                // Use finite differencing to approximate the derivative (ShadingContext::ocio_transform)
                const float eps = 0.001f;
                Color3 val = new Color3(C.x.x, C.x.y, C.x.z);
                Color3 dx = new Color3(C.dx.x, C.dx.y, C.dx.z);
                Color3 dy = new Color3(C.dy.x, C.dy.y, C.dy.z);
                Color3 CC0;
                Color3 CC1;
                Color3 CC2;
                bool ok0 = OslRuntime.ocio_transform_hook(fromspace, tospace, val, out CC0);
                bool ok1 = OslRuntime.ocio_transform_hook(fromspace, tospace, val + eps * dx, out CC1);
                bool ok2 = OslRuntime.ocio_transform_hook(fromspace, tospace, val + eps * dy, out CC2);
                if (ok0 && ok1 && ok2)
                {
                    Color3 ddx = (CC1 - CC0) * (1.0f / eps);
                    Color3 ddy = (CC2 - CC0) * (1.0f / eps);
                    return new Dual2v3(new Vec3(CC0.x, CC0.y, CC0.z), new Vec3(ddx.x, ddx.y, ddx.z), new Vec3(ddy.x, ddy.y, ddy.z));
                }
            }
            if (OslRuntime.unknown_colorspace_hook != null)
            {
                OslRuntime.unknown_colorspace_hook(fromspace, tospace);
            }
            return C;
        }

        public Color3 to_rgb(ulong fromspace, Color3 C)
        {
            if (fromspace == Hashes.RGB || fromspace == Hashes.rgb || fromspace == m_colorspace)
            {
                return C;
            }
            if (fromspace == Hashes.hsv)
            {
                return ColorOps.hsv_to_rgb(C);
            }
            if (fromspace == Hashes.hsl)
            {
                return ColorOps.hsl_to_rgb(C);
            }
            if (fromspace == Hashes.YIQ)
            {
                return ColorOps.YIQ_to_rgb(C);
            }
            if (fromspace == Hashes.XYZ)
            {
                return XYZ_to_RGB(C);
            }
            if (fromspace == Hashes.xyY)
            {
                return XYZ_to_RGB(ColorOps.xyY_to_XYZ(C));
            }
            return ocio_transform(fromspace, Hashes.RGB, C);
        }

        public Color3 from_rgb(ulong tospace, Color3 C)
        {
            if (tospace == Hashes.RGB || tospace == Hashes.rgb || tospace == m_colorspace)
            {
                return C;
            }
            if (tospace == Hashes.hsv)
            {
                return ColorOps.rgb_to_hsv(C);
            }
            if (tospace == Hashes.hsl)
            {
                return ColorOps.rgb_to_hsl(C);
            }
            if (tospace == Hashes.YIQ)
            {
                return ColorOps.rgb_to_YIQ(C);
            }
            if (tospace == Hashes.XYZ)
            {
                return RGB_to_XYZ(C);
            }
            if (tospace == Hashes.xyY)
            {
                return ColorOps.XYZ_to_xyY(RGB_to_XYZ(C));
            }
            return ocio_transform(Hashes.RGB, tospace, C);
        }

        public Color3 transformc(ulong fromspace, ulong tospace, Color3 C)
        {
            bool use_colorconfig = false;
            Color3 Crgb = default;
            if (fromspace == Hashes.RGB || fromspace == Hashes.rgb || fromspace == Hashes.linear || fromspace == m_colorspace)
            {
                Crgb = C;
            }
            else if (fromspace == Hashes.hsv)
            {
                Crgb = ColorOps.hsv_to_rgb(C);
            }
            else if (fromspace == Hashes.hsl)
            {
                Crgb = ColorOps.hsl_to_rgb(C);
            }
            else if (fromspace == Hashes.YIQ)
            {
                Crgb = ColorOps.YIQ_to_rgb(C);
            }
            else if (fromspace == Hashes.XYZ)
            {
                Crgb = XYZ_to_RGB(C);
            }
            else if (fromspace == Hashes.xyY)
            {
                Crgb = XYZ_to_RGB(ColorOps.xyY_to_XYZ(C));
            }
            else if (fromspace == Hashes.sRGB)
            {
                Crgb = ColorOps.sRGB_to_linear(C);
            }
            else
            {
                use_colorconfig = true;
            }

            Color3 Cto = default;
            if (use_colorconfig)
            {
                // do things the ColorConfig way, so skip all these other clauses...
            }
            else if (tospace == Hashes.RGB || tospace == Hashes.rgb || tospace == Hashes.linear || tospace == m_colorspace)
            {
                Cto = Crgb;
            }
            else if (tospace == Hashes.hsv)
            {
                Cto = ColorOps.rgb_to_hsv(Crgb);
            }
            else if (tospace == Hashes.hsl)
            {
                Cto = ColorOps.rgb_to_hsl(Crgb);
            }
            else if (tospace == Hashes.YIQ)
            {
                Cto = ColorOps.rgb_to_YIQ(Crgb);
            }
            else if (tospace == Hashes.XYZ)
            {
                Cto = RGB_to_XYZ(Crgb);
            }
            else if (tospace == Hashes.xyY)
            {
                Cto = ColorOps.XYZ_to_xyY(RGB_to_XYZ(Crgb));
            }
            else if (tospace == Hashes.sRGB)
            {
                Cto = ColorOps.linear_to_sRGB(Crgb);
            }
            else
            {
                use_colorconfig = true;
            }

            if (use_colorconfig)
            {
                Cto = ocio_transform(fromspace, tospace, C);
            }
            return Cto;
        }

        public Dual2v3 transformc(ulong fromspace, ulong tospace, Dual2v3 C)
        {
            bool use_colorconfig = false;
            Dual2v3 Crgb = default;
            if (fromspace == Hashes.RGB || fromspace == Hashes.rgb || fromspace == Hashes.linear || fromspace == m_colorspace)
            {
                Crgb = C;
            }
            else if (fromspace == Hashes.hsv)
            {
                Crgb = ColorOps.hsv_to_rgb(C);
            }
            else if (fromspace == Hashes.hsl)
            {
                Crgb = ColorOps.hsl_to_rgb(C);
            }
            else if (fromspace == Hashes.YIQ)
            {
                Crgb = ColorOps.YIQ_to_rgb(C);
            }
            else if (fromspace == Hashes.XYZ)
            {
                Crgb = XYZ_to_RGB(C);
            }
            else if (fromspace == Hashes.xyY)
            {
                Crgb = XYZ_to_RGB(ColorOps.xyY_to_XYZ(C));
            }
            else if (fromspace == Hashes.sRGB)
            {
                Crgb = ColorOps.sRGB_to_linear(C);
            }
            else
            {
                use_colorconfig = true;
            }

            Dual2v3 Cto = default;
            if (use_colorconfig)
            {
                // do things the ColorConfig way, so skip all these other clauses...
            }
            else if (tospace == Hashes.RGB || tospace == Hashes.rgb || tospace == Hashes.linear || tospace == m_colorspace)
            {
                Cto = Crgb;
            }
            else if (tospace == Hashes.hsv)
            {
                Cto = ColorOps.rgb_to_hsv(Crgb);
            }
            else if (tospace == Hashes.hsl)
            {
                Cto = ColorOps.rgb_to_hsl(Crgb);
            }
            else if (tospace == Hashes.YIQ)
            {
                Cto = ColorOps.rgb_to_YIQ(Crgb);
            }
            else if (tospace == Hashes.XYZ)
            {
                Cto = RGB_to_XYZ(Crgb);
            }
            else if (tospace == Hashes.xyY)
            {
                Cto = ColorOps.XYZ_to_xyY(RGB_to_XYZ(Crgb));
            }
            else if (tospace == Hashes.sRGB)
            {
                Cto = ColorOps.linear_to_sRGB(Crgb);
            }
            else
            {
                use_colorconfig = true;
            }

            if (use_colorconfig)
            {
                Cto = ocio_transform(fromspace, tospace, C);
            }
            return Cto;
        }
    }
}

namespace OSL
{
    public static unsafe partial class OslRuntime
    {
        // The ColorSystem the shadeops use. Stands in for ShadingStateUniform::m_colorsystem, found through the OpaqueExecContextPtr in the C++.
        public static ColorSystemProvider get_colorsystem_hook;

        // Optional OpenColorIO stand-in for colour spaces the ColorSystem does not know
        public static OcioTransformDelegate ocio_transform_hook;

        // Called with (fromspace, tospace) where the C++ reports "Unknown color space transformation"
        public static Action<ulong, ulong> unknown_colorspace_hook;

        private static readonly ColorSystem default_colorsystem = new ColorSystem();

        private static ColorSystem get_colorsystem(void* oec)
        {
            if (get_colorsystem_hook != null)
            {
                return get_colorsystem_hook(oec);
            }
            return default_colorsystem;
        }

        public static void osl_blackbody_vf(void* oec, void* @out, float temp)
        {
            ColorSystem cs = get_colorsystem(oec);
            *(Color3*)@out = cs.blackbody_rgb(temp);
        }

        public static void osl_wavelength_color_vf(void* oec, void* @out, float lambda)
        {
            ColorSystem cs = get_colorsystem(oec);
            Color3 rgb = cs.XYZ_to_RGB(ColorOps.wavelength_color_XYZ(lambda));
            rgb = rgb * (float)(1.0 / 2.52); // Empirical scale from lg to make all comps <= 1
            ColorOps.clamp_zero(ref rgb);
            *(Color3*)@out = rgb;
        }

        public static void osl_luminance_fv(void* oec, void* @out, void* c)
        {
            ColorSystem cs = get_colorsystem(oec);
            ((float*)@out)[0] = cs.luminance(((Color3*)c)[0]);
        }

        public static void osl_luminance_dfdv(void* oec, void* @out, void* c)
        {
            ColorSystem cs = get_colorsystem(oec);
            ((float*)@out)[0] = cs.luminance(((Color3*)c)[0]);
            ((float*)@out)[1] = cs.luminance(((Color3*)c)[1]);
            ((float*)@out)[2] = cs.luminance(((Color3*)c)[2]);
        }

        public static void osl_prepend_color_from(void* oec, void* c_, ulong from)
        {
            ColorSystem cs = get_colorsystem(oec);
            *(Color3*)c_ = cs.to_rgb(from, *(Color3*)c_);
        }

        public static int osl_transformc(void* oec, void* Cin, int Cin_derivs, void* Cout, int Cout_derivs, ulong from, ulong to)
        {
            ColorSystem cs = get_colorsystem(oec);
            if (Cout_derivs != 0)
            {
                if (Cin_derivs != 0)
                {
                    *(Dual2v3*)Cout = cs.transformc(from, to, *(Dual2v3*)Cin);
                    return 1;
                }
                // We had output derivs, but not input. Zero the output derivs and fall through to the non-deriv case.
                ((Color3*)Cout)[1] = new Color3(0.0f, 0.0f, 0.0f);
                ((Color3*)Cout)[2] = new Color3(0.0f, 0.0f, 0.0f);
            }
            // No-derivs case
            *(Color3*)Cout = cs.transformc(from, to, *(Color3*)Cin);
            return 1;
        }
    }
}
