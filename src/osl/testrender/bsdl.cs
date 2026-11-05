// OSL port: the core of libbsdl (BSDL/config.h, tools.h, spectrum_decl.h, bsdf_decl.h, bsdf_impl.h, microfacet_tools_decl.h, microfacet_tools_impl.h)
//
// What the MaterialX lobes of the test renderer are built from: the RGB power type (testrender configures libbsdl with three hero wavelength channels
// and the hero wavelength 0, which makes every color space conversion the identity, so only that case is ported), the hemisphere sampling tools, the
// GGX distribution, the energy compensation tables and the Turquin multiple scattering reflection.
// The thin film, absorption and bump shadowing sample filters of libbsdl are not applied by testrender (its adapter never calls them) and are not ported.

using System;
using OIIO;

namespace OSL
{
    /// The constants and free functions of libbsdl
    public static class Bsdl
    {
        public const float EPSILON = 1e-4f;
        public const float PI = 3.14159274101257324219f;
        public const float ALMOSTONE = 0.999999940395355224609375f;
        public const float FLOAT_MIN = 1.17549435e-38f;
        public const float SQRT2 = 1.4142135623730951f;
        public const float BIG = 1e12f;
        public const float PDF_MIN = 1e-6f;

        public static readonly float ONEOVERPI = 1.0f / PI;

        // std::max and std::min, with their treatment of equal and NaN arguments
        public static float stdmax(float a, float b)
        {
            return (a < b) ? b : a;
        }

        public static float stdmin(float a, float b)
        {
            return (b < a) ? b : a;
        }

        public static float MAX(float a, float b)
        {
            return stdmax(a, b);
        }

        public static float MIN(float a, float b)
        {
            return a < b ? a : b;
        }

        public static float MAX_ABS_XYZ(Vec3 v)
        {
            return stdmax(MathF.Abs(v.x), stdmax(MathF.Abs(v.y), MathF.Abs(v.z)));
        }

        public static float MAX_RGB(Color3 c)
        {
            return stdmax(c.x, stdmax(c.y, c.z));
        }

        public static float AVG_RGB(Color3 c)
        {
            return (c.x + c.y + c.z) * (1.0f / 3);
        }

        public static float SQR(float x)
        {
            return x * x;
        }

        public static float pown5(float x)
        {
            float rec = x * x;
            return rec * rec * x;
        }

        public static float CLAMP(float x, float a, float b)
        {
            return MIN(MAX(x, a), b);
        }

        public static float LERP(float f, float a, float b)
        {
            f = CLAMP(f, 0, 1);
            return (1 - f) * a + f * b;
        }

        public static Vec3 LERP(float f, Vec3 a, Vec3 b)
        {
            f = CLAMP(f, 0, 1);
            return (1 - f) * a + f * b;
        }

        public static Power LERP(float f, Power a, Power b)
        {
            f = CLAMP(f, 0, 1);
            return (1 - f) * a + f * b;
        }

        public static float fast_cos_quadrant(float x)
        {
            const float c1 = 0.99998736f;
            const float c2 = -0.30837047f;
            const float c3 = 0.01578646f;
            const float c4 = -0.00029826362f;
            float x2 = x * x;
            float cp = c3 + c4 * x2;
            cp = c2 + cp * x2;
            cp = c1 + cp * x2;
            return cp;
        }

        public static float fast_sin_quadrant(float x)
        {
            const float s1 = 0.7853975892066955566406250000000000f;
            const float s2 = -0.0807407423853874206542968750000000f;
            const float s3 = 0.0024843954015523195266723632812500f;
            const float s4 = -0.0000341485538228880614042282104492f;
            float x2 = x * x;
            float sp = s3 + s4 * x2;
            sp = s2 + sp * x2;
            sp = s1 + sp * x2;
            return sp * x;
        }

        public static Vec3 sample_cos_hemisphere(float randu, float randv)
        {
            float a = 2 * randu - 1;
            float qa = MathF.Abs(a);
            float b = 2 * randv - 1;
            float qb = MathF.Abs(b);
            float rad = qa > qb ? qa : qb;
            float phi = qa > qb ? qb / qa : ((qa == qb) ? 1.0f : 2 - qa / qb);
            float x = MathF.CopySign(rad * fast_cos_quadrant(phi), a);
            float y = MathF.CopySign(rad * fast_sin_quadrant(phi), b);
            return new Vec3(x, y, MathF.Sqrt(1 - rad * rad));
        }

        public static Vec3 sample_uniform_hemisphere(float randu, float randv)
        {
            float a = 2 * randu - 1;
            float qa = MathF.Abs(a);
            float b = 2 * randv - 1;
            float qb = MathF.Abs(b);
            float rad = qa > qb ? qa : qb;
            float phi = qa > qb ? qb / qa : ((qa == qb) ? 1.0f : 2 - qa / qb);
            float x = MathF.CopySign(rad * fast_cos_quadrant(phi), a);
            float y = MathF.CopySign(rad * fast_sin_quadrant(phi), b);
            float cos_theta = 1 - rad * rad;
            float sin_theta = MathF.Sqrt(2 - rad * rad);
            return new Vec3(sin_theta * x, sin_theta * y, cos_theta);
        }

        public static Vec2f square_to_unit_disc(Vec2f rnd)
        {
            float a = 2 * rnd.x - 1;
            float qa = MathF.Abs(a);
            float b = 2 * rnd.y - 1;
            float qb = MathF.Abs(b);
            float rad = qa > qb ? qa : qb;
            float phi = qa > qb ? qb / qa : ((qa == qb) ? 1.0f : 2 - qa / qb);
            float x = MathF.CopySign(rad * fast_cos_quadrant(phi), a);
            float y = MathF.CopySign(rad * fast_sin_quadrant(phi), b);
            return new Vec2f(x, y);
        }

        public static Vec3 reflect(Vec3 E, Vec3 N)
        {
            return N * (2 * N.dot(E)) - E;
        }

        public static Vec3 refract(Vec3 E, Vec3 N, float eta)
        {
            Vec3 R = new Vec3(0.0f);
            if (eta == 0)
            {
                return R;
            }
            Vec3 Nn;
            float cosi = E.dot(N);
            float neta;
            if (cosi > 0)
            {
                neta = 1 / eta;
                Nn = N;
            }
            else
            {
                cosi = -cosi;
                neta = eta;
                Nn = -N;
            }
            float arg = 1 - (neta * neta * (1 - (cosi * cosi)));
            if (arg >= 0)
            {
                float dnp = MathF.Sqrt(arg);
                float nK = (neta * cosi) - dnp;
                R = V3.normalized(E * (-neta) + Nn * nK);
            }
            return R;
        }

        public static Vec3 normalized(Vec3 v)
        {
            return V3.normalized(v);
        }
    }

    /// The RGB power of libbsdl (three hero wavelength channels, the hero wavelength 0)
    public struct Power
    {
        public float d0, d1, d2;

        public Power(float x, float y, float z)
        {
            d0 = x;
            d1 = y;
            d2 = z;
        }

        /// A constant in every channel, Power(f, lambda_0)
        public Power(float f, float lambda_0)
        {
            d0 = f;
            d1 = f;
            d2 = f;
        }

        /// Power(rgb, lambda_0) with the hero wavelength 0, the bypass color space
        public Power(Color3 rgb, float lambda_0)
        {
            d0 = rgb.x;
            d1 = rgb.y;
            d2 = rgb.z;
        }

        public static Power ZERO()
        {
            return new Power(0.0f, 0.0f, 0.0f);
        }

        public static Power UNIT()
        {
            return new Power(1.0f, 1.0f, 1.0f);
        }

        public float this[int i]
        {
            get { return i == 0 ? d0 : i == 1 ? d1 : d2; }
            set
            {
                if (i == 0)
                {
                    d0 = value;
                }
                else if (i == 1)
                {
                    d1 = value;
                }
                else
                {
                    d2 = value;
                }
            }
        }

        public Color3 toRGB(float lambda_0)
        {
            return new Color3(d0, d1, d2);
        }

        public float max()
        {
            float m = d0;
            m = Bsdl.stdmax(m, d1);
            m = Bsdl.stdmax(m, d2);
            return m;
        }

        public float min(float lambda_0)
        {
            float m = d0;
            m = Bsdl.stdmin(m, d1);
            m = Bsdl.stdmin(m, d2);
            return m;
        }

        public float sum()
        {
            float m = 0;
            m += d0;
            m += d1;
            m += d2;
            return m;
        }

        public float avg(float lambda_0)
        {
            return sum() / 3;
        }

        public Power clamped(float a, float b)
        {
            return new Power(Bsdl.stdmax(a, Bsdl.stdmin(b, d0)), Bsdl.stdmax(a, Bsdl.stdmin(b, d1)), Bsdl.stdmax(a, Bsdl.stdmin(b, d2)));
        }

        /// The RGB channels only, which is all of them here
        public Power cliped_rgb(float lambda_0)
        {
            return this;
        }

        public static Power sqrt(Power x)
        {
            x = x.clamped(0.0f, float.MaxValue);
            return new Power(MathF.Sqrt(x.d0), MathF.Sqrt(x.d1), MathF.Sqrt(x.d2));
        }

        public static Power operator -(Power o)
        {
            return new Power(-o.d0, -o.d1, -o.d2);
        }

        public static Power operator *(Power a, Power o)
        {
            return new Power(a.d0 * o.d0, a.d1 * o.d1, a.d2 * o.d2);
        }

        public static Power operator /(Power a, Power o)
        {
            return new Power(a.d0 / o.d0, a.d1 / o.d1, a.d2 / o.d2);
        }

        public static Power operator *(Power a, float f)
        {
            return new Power(a.d0 * f, a.d1 * f, a.d2 * f);
        }

        public static Power operator *(float f, Power o)
        {
            return o * f;
        }

        public static Power operator +(Power a, Power o)
        {
            return new Power(a.d0 + o.d0, a.d1 + o.d1, a.d2 + o.d2);
        }

        public static Power operator -(Power a, Power o)
        {
            return new Power(a.d0 - o.d0, a.d1 - o.d1, a.d2 - o.d2);
        }

        public void operator *=(Power o)
        {
            d0 *= o.d0;
            d1 *= o.d1;
            d2 *= o.d2;
        }

        public void operator *=(float f)
        {
            d0 *= f;
            d1 *= f;
            d2 *= f;
        }

        public void operator +=(Power o)
        {
            d0 += o.d0;
            d1 += o.d1;
            d2 += o.d2;
        }
    }

    /// The result of evaluating or sampling a lobe of libbsdl
    public struct BsdlSample
    {
        public Vec3 wi;
        public Power weight;
        public float pdf;
        public float roughness;

        public BsdlSample(Vec3 wi, Power weight, float pdf, float roughness)
        {
            this.wi = wi;
            this.weight = weight;
            this.pdf = pdf;
            this.roughness = roughness;
        }

        public bool isnull()
        {
            return pdf == 0;
        }

        /// Combines another estimate (weight ow, pdf opdf) that is used with probability cpdf into this sample
        public void update(Power ow, float opdf, float cpdf)
        {
            if (cpdf > Bsdl.PDF_MIN)
            {
                opdf *= cpdf;
                ow *= 1 / cpdf;
                if (opdf == pdf)
                {
                    weight = Bsdl.LERP(0.5f, weight, ow);
                }
                else if (opdf < pdf)
                {
                    weight = Bsdl.LERP(1 / (1 + opdf / pdf), ow, weight);
                }
                else
                {
                    weight = Bsdl.LERP(1 / (1 + pdf / opdf), weight, ow);
                }
                pdf += opdf;
            }
        }
    }

    /// The Data of the lobes that can be layered starts with the closure the layer was made of (BSDL::LayeredData)
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public unsafe struct LayeredData
    {
        public void* closure;
    }

    /// The shading point as the lobes see it (BSDL::BsdfGlobals)
    public struct BsdfGlobals
    {
        public Vec3 wo;
        public Vec3 Nf;
        public Vec3 Ngf;
        public bool backfacing;
        public float path_roughness;
        public float outer_ior;
        public float lambda_0;

        public BsdfGlobals(Vec3 wo, Vec3 Nf, Vec3 Ngf, bool backfacing, float path_roughness, float outer_ior, float lambda_0)
        {
            this.wo = wo;
            this.Nf = Nf;
            this.Ngf = Ngf;
            this.backfacing = backfacing;
            this.path_roughness = path_roughness;
            this.outer_ior = outer_ior;
            this.lambda_0 = lambda_0;
        }

        /// The shading normal flipped just enough to face wo
        public static Vec3 visible_normal(Vec3 wo, Vec3 Ngf, Vec3 N)
        {
            if (wo.dot(N) > 0.0f)
            {
                return N;
            }
            Vec3 V = wo.cross(N);
            if (Bsdl.MAX_ABS_XYZ(V) < 1e-4f)
            {
                V = wo.cross(Ngf);
                if (Bsdl.MAX_ABS_XYZ(V) < 1e-4f)
                {
                    float s = MathF.CopySign(1.0f, wo.z);
                    float a = -1.0f / (s + wo.z);
                    V = new Vec3(wo.x * wo.y * a, s + Bsdl.SQR(wo.y) * a, -wo.y);
                }
                else
                {
                    V = V3.normalize(V);
                }
            }
            else
            {
                V = V3.normalize(V);
            }
            return V3.normalized(V.cross(wo) + 1e-4f * wo);
        }

        public Vec3 visible_normal(Vec3 N)
        {
            return visible_normal(wo, Ngf, N);
        }

        public float regularize_roughness(float roughness)
        {
            float roughness_product = 1.0f - path_roughness;
            return 1.0f - (1.0f - roughness) * roughness_product;
        }

        public float relative_eta(float IOR)
        {
            return IOR / outer_ior;
        }

        public Power wave(Color3 c)
        {
            return new Power(c, lambda_0);
        }
    }

    /// An orthonormal frame, the local space of a lobe
    public struct Frame
    {
        public Vec3 X, Y, Z;

        private static void ortho_build(Vec3 Z, out Vec3 X, out Vec3 Y)
        {
            float s = MathF.CopySign(1.0f, Z.z);
            float a = -1.0f / (s + Z.z);
            float b = Z.x * Z.y * a;
            X = new Vec3(1.0f + s * Z.x * Z.x * a, s * b, -s * Z.x);
            Y = new Vec3(b, s + Z.y * Z.y * a, -Z.y);
        }

        public Frame(Vec3 Z)
        {
            this.Z = Z;
            ortho_build(Z, out X, out Y);
        }

        public Frame(Vec3 Z, Vec3 _X)
        {
            this.Z = Z;
            X = _X;
            Y = new Vec3();
            if (Bsdl.MAX_ABS_XYZ(X) < 1e-4f || MathF.Abs(Z.dot(V3.normalized(X))) > 0.999f)
            {
                ortho_build(Z, out X, out Y);
            }
            else
            {
                Y = V3.normalized(Z.cross(X));
                X = Y.cross(Z);
            }
        }

        public Vec3 local(Vec3 a)
        {
            return new Vec3(a.dot(X), a.dot(Y), a.dot(Z));
        }

        public Vec3 world(Vec3 a)
        {
            return new Vec3(X.x * a.x + Y.x * a.y + Z.x * a.z, X.y * a.x + Y.y * a.y + Z.y * a.z, X.z * a.x + Y.z * a.y + Z.z * a.z);
        }
    }

    /// The anisotropic GGX distribution
    public struct GGXDist
    {
        private float ax, ay;

        public GGXDist(float rough, float aniso, bool flip_aniso)
        {
            ax = Bsdl.SQR(rough);
            ay = ax;
            if (flip_aniso)
            {
                aniso = -aniso;
            }
            const float ALPHA_MIN = 1e-5f;
            ax = Bsdl.stdmax(ax * (1 + aniso), ALPHA_MIN);
            ay = Bsdl.stdmax(ay * (1 - aniso), ALPHA_MIN);
        }

        public float roughness()
        {
            return Bsdl.stdmax(ax, ay);
        }

        public float D(Vec3 Hr)
        {
            float cosPhi2st2 = Bsdl.SQR(Hr.x / ax);
            float sinPhi2st2 = Bsdl.SQR(Hr.y / ay);
            float cosThetaM2 = Bsdl.SQR(Hr.z);
            float sinThetaM2 = cosPhi2st2 + sinPhi2st2;
            return 1.0f / (Bsdl.PI * ax * ay * Bsdl.SQR(cosThetaM2 + sinThetaM2));
        }

        public float G1(Vec3 w)
        {
            w = new Vec3(w.x * ax, w.y * ay, w.z);
            return 2.0f * w.z / (w.z + w.length());
        }

        public float G2_G1(Vec3 wi, Vec3 wo)
        {
            wi = new Vec3(wi.x * ax, wi.y * ay, wi.z);
            wo = new Vec3(wo.x * ax, wo.y * ay, wo.z);
            float nl = wi.length();
            float nv = wo.length();
            return wi.z * (wo.z + nv) / (wo.z * nl + wi.z * nv);
        }

        /// Samples the visible normals
        public Vec3 sample(Vec3 wo, float randu, float randv)
        {
            Vec3 V = V3.normalized(new Vec3(ax * wo.x, ay * wo.y, wo.z));
            Vec3 T1 = V.z < 0.9999f ? V3.normalized(new Vec3(V.y, -V.x, 0)) : new Vec3(1, 0, 0);
            Vec3 T2 = T1.cross(V);
            Vec2f p = Bsdl.square_to_unit_disc(new Vec2f(randu, randv));
            float s = 0.5f * (1 + V.z);
            float p2o = s * p.y + (1 - s) * MathF.Sqrt(1 - p.x * p.x);
            float p3 = MathF.Sqrt(Bsdl.stdmax(1.0f - Bsdl.SQR(p.x) - Bsdl.SQR(p2o), 0.0f));
            Vec3 N = p.x * T1 + p2o * T2 + p3 * V;
            return V3.normalized(new Vec3(ax * N.x, ay * N.y, Bsdl.stdmax(N.z, 0.0f)));
        }

        /// Samples the normals that reflect wo (Bounded visible normal sampling)
        public Vec3 sample_for_refl(Vec3 wo, float randu, float randv)
        {
            Vec3 i_std = V3.normalized(new Vec3(wo.x * ax, wo.y * ay, wo.z));
            float phi = 2.0f * Bsdl.PI * randu;
            float a = Bsdl.CLAMP(Bsdl.stdmin(ax, ay), 0.0f, 1.0f);
            float s = 1 + MathF.Sqrt(Bsdl.SQR(wo.x) + Bsdl.SQR(wo.y));
            float a2 = Bsdl.SQR(a);
            float s2 = Bsdl.SQR(s);
            float k = (1 - a2) * s2 / (s2 + a2 * Bsdl.SQR(wo.z));
            float b = k * i_std.z;
            float z = (1 - randv) * (1 + b) - b;
            float sinTheta = MathF.Sqrt(Bsdl.CLAMP(1 - Bsdl.SQR(z), 0.0f, 1.0f));
            Vec3 o_std = new Vec3(sinTheta * fmath.fast_cos(phi), sinTheta * fmath.fast_sin(phi), z);
            Vec3 m_std = i_std + o_std;
            return V3.normalized(new Vec3(m_std.x * ax, m_std.y * ay, m_std.z));
        }

        /// The ratio of the distribution of reflected normals and D
        public float D_refl_D(Vec3 wo, Vec3 m)
        {
            Vec2f ai = new Vec2f(wo.x * ax, wo.y * ay);
            float len2 = ai.x * ai.x + ai.y * ai.y;
            float t = MathF.Sqrt(len2 + Bsdl.SQR(wo.z));
            float a = Bsdl.CLAMP(Bsdl.stdmin(ax, ay), 0.0f, 1.0f);
            float s = 1 + MathF.Sqrt(Bsdl.SQR(wo.x) + Bsdl.SQR(wo.y));
            float a2 = Bsdl.SQR(a);
            float s2 = Bsdl.SQR(s);
            float k = (1 - a2) * s2 / (s2 + a2 * Bsdl.SQR(wo.z));
            return 2 * wo.z / (k * wo.z + t);
        }
    }

    /// A table of the energy a lobe loses to multiple scattering, over the cosine of the view direction, the roughness and (for some) the fresnel index
    public sealed class EnergyTable
    {
        public readonly int Nc;
        public readonly int Nr;
        public readonly int Nf;
        public readonly bool squared_cosine;
        public readonly float[] data;

        public EnergyTable(int Nc, int Nr, int Nf, bool squared_cosine, float[] data)
        {
            this.Nc = Nc;
            this.Nr = Nr;
            this.Nf = Nf;
            this.squared_cosine = squared_cosine;
            this.data = data;
        }

        public float get_cosine(int i)
        {
            float c = (float)i * (1.0f / (Nc - 1));
            if (squared_cosine)
            {
                c = Bsdl.SQR(c);
            }
            return Bsdl.stdmax(c, 1e-6f);
        }
    }

    public static class EnergyTables
    {
        public static readonly EnergyTable MiniMicrofacetGGX = new EnergyTable(16, 16, 1, true, BsdlLuts.MiniMicrofacetGGX);
        public static readonly EnergyTable DielectricReflFront = new EnergyTable(16, 16, 32, false, BsdlLuts.DielectricReflFront);
        public static readonly EnergyTable DielectricBothFront = new EnergyTable(16, 16, 32, false, BsdlLuts.DielectricBothFront);
        public static readonly EnergyTable DielectricBothBack = new EnergyTable(16, 16, 32, false, BsdlLuts.DielectricBothBack);
    }

    /// Looks up the energy of a lobe in its table (TabulatedEnergyCurve)
    public struct TabulatedEnergyCurve
    {
        private readonly EnergyTable table;
        private readonly float roughness;
        private readonly float fresnel_index;

        public TabulatedEnergyCurve(EnergyTable table, float roughness, float fresnel_index)
        {
            this.table = table;
            this.roughness = roughness;
            this.fresnel_index = fresnel_index;
        }

        private float interpolate_emiss(int i)
        {
            float[] storedE = table.data;
            float rf = roughness * (table.Nr - 1);
            int ra = (int)rf;
            int rb = Math.Min(ra + 1, table.Nr - 1);
            rf -= ra;
            if (table.Nf == 1)
            {
                return Bsdl.LERP(rf, storedE[ra * table.Nc + i], storedE[rb * table.Nc + i]);
            }
            else
            {
                float ff = fresnel_index * (table.Nf - 1);
                int fa = (int)ff;
                int fb = Math.Min(fa + 1, table.Nf - 1);
                ff -= fa;
                return Bsdl.LERP(ff,
                                 Bsdl.LERP(rf, storedE[(fa * table.Nr + ra) * table.Nc + i], storedE[(fa * table.Nr + rb) * table.Nc + i]),
                                 Bsdl.LERP(rf, storedE[(fb * table.Nr + ra) * table.Nc + i], storedE[(fb * table.Nr + rb) * table.Nc + i]));
            }
        }

        public float Emiss_eval(float c)
        {
            float cos0 = table.get_cosine(0);
            if (c <= cos0)
            {
                return interpolate_emiss(0);
            }
            for (int i = 1; i < table.Nc; i++)
            {
                float cos1 = table.get_cosine(i);
                if (c < cos1)
                {
                    float q = (c - cos0) / (cos1 - cos0);
                    return Bsdl.LERP(q, interpolate_emiss(i - 1), interpolate_emiss(i));
                }
                cos0 = cos1;
            }
            return interpolate_emiss(table.Nc - 1);
        }
    }

    /// The fresnel term of a microfacet lobe
    public interface IBsdlFresnel
    {
        Power eval(float c);
    }

    /// The Turquin multiple scattering reflection of a GGX microfacet lobe
    public static class Turquin
    {
        public static BsdlSample eval_turquin_microms_reflection<TF>(GGXDist dist, TF fresnel, float E_ms, Vec3 wo, Vec3 wi) where TF : struct, IBsdlFresnel
        {
            float cosNO = wo.z;
            float cosNI = wi.z;
            if (cosNI <= 0 || cosNO <= 0)
            {
                return new BsdlSample();
            }
            Vec3 m = V3.normalized(wo + wi);
            float cosMO = m.dot(wo);
            float D = dist.D(m);
            float D_refl_D = dist.D_refl_D(wo, m);
            float D_refl = D_refl_D * D;
            float G1 = dist.G1(wo);
            float pdf = D_refl / (4 * cosNO);
            float output = dist.G2_G1(wi, wo) * G1 / D_refl_D;
            Power F = fresnel.eval(cosMO);
            Power F_ms = F;
            float msf = E_ms / Bsdl.stdmax(0.01f, 1 - E_ms);
            Power one = new Power(1.0f, 1.0f);
            Power O = output * F * (one + F_ms * msf);
            return new BsdlSample(wi, O, pdf, -1);
        }

        public static BsdlSample sample_turquin_microms_reflection<TF>(GGXDist dist, TF fresnel, float E_ms, Vec3 wo, Vec3 rnd) where TF : struct, IBsdlFresnel
        {
            float cosNO = wo.z;
            if (cosNO <= 0)
            {
                return new BsdlSample();
            }
            Vec3 m = dist.sample_for_refl(wo, rnd.x, rnd.y);
            Vec3 wi = Bsdl.reflect(wo, m);
            if (wi.z <= 0)
            {
                return new BsdlSample();
            }
            return eval_turquin_microms_reflection(dist, fresnel, E_ms, wo, wi);
        }
    }
}
