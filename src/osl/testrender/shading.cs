// OSL port: testrender/shading.h, shading.cpp and optics.h (the surface part: no MaterialX closures and no volumes)
//
// The closures of the test renderer and the BSDFs behind them: diffuse, translucent, oren_nayar, phong, ward, microfacet (GGX and Beckmann, reflection,
// refraction or both), reflection, refraction and transparent. A shader's closure tree is turned into a CompositeBSDF (the weighted lobes, picked by
// their albedo) plus the emission, and the background closure into a color.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OIIO;

namespace OSL
{
    public static class ClosureIDs
    {
        public const int ADD = -2;
        public const int MUL = -1;
        public const int COMPONENT_BASE_ID = 0;
        public const int EMISSION_ID = 1;
        public const int BACKGROUND_ID = 2;
        public const int DIFFUSE_ID = 3;
        public const int OREN_NAYAR_ID = 4;
        public const int TRANSLUCENT_ID = 5;
        public const int PHONG_ID = 6;
        public const int WARD_ID = 7;
        public const int MICROFACET_ID = 8;
        public const int REFLECTION_ID = 9;
        public const int FRESNEL_REFLECTION_ID = 10;
        public const int REFRACTION_ID = 11;
        public const int TRANSPARENT_ID = 12;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DiffuseParams
    {
        public Vec3 N;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OrenNayarParams
    {
        public Vec3 N;
        public float sigma;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PhongParams
    {
        public Vec3 N;
        public float exponent;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WardParams
    {
        public Vec3 N, T;
        public float ax, ay;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ReflectionParams
    {
        public Vec3 N;
        public float eta;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RefractionParams
    {
        public Vec3 N;
        public float eta;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MicrofacetParams
    {
        public ulong dist;
        public Vec3 N, U;
        public float xalpha, yalpha, eta;
        public int refract;
    }

    public static class Optics
    {
        public static float fresnel_dielectric(float cosi, float eta)
        {
            if (eta == 0)
            {
                return 1;
            }
            if (cosi < 0.0f)
            {
                eta = 1.0f / eta;
            }
            float c = MathF.Abs(cosi);
            float g = eta * eta - 1 + c * c;
            if (g > 0)
            {
                g = MathF.Sqrt(g);
                float A = (g - c) / (g + c);
                float B = (c * (g + c) - 1) / (c * (g - c) + 1);
                return 0.5f * A * A * (1 + B * B);
            }
            return 1.0f;
        }

        public static float fresnel_refraction(Vec3 I, Vec3 N, float eta, out Vec3 T)
        {
            float cosi = -I.dot(N);
            Vec3 Nn;
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
            float arg = 1.0f - (neta * neta * (1.0f - cosi * cosi));
            if (arg >= 0)
            {
                float dnp = MathF.Sqrt(arg);
                float nK = (neta * cosi) - dnp;
                T = I * neta + Nn * nK;
                return 1 - fresnel_dielectric(cosi, eta);
            }
            T = new Vec3(0, 0, 0);
            return 0;
        }
    }

    public struct BsdfSample
    {
        public Vec3 wi;
        public Color3 weight;
        public float pdf;
        public float roughness;

        public BsdfSample(Vec3 wi, Color3 w, float pdf, float r)
        {
            this.wi = wi;
            weight = w;
            this.pdf = pdf;
            roughness = r;
        }
    }

    public abstract class BSDF
    {
        public virtual Color3 get_albedo(Vec3 wo)
        {
            return new Color3(1);
        }

        public abstract BsdfSample eval(Vec3 wo, Vec3 wi);

        public abstract BsdfSample sample(Vec3 wo, float rx, float ry, float rz);
    }

    public sealed class Diffuse : BSDF
    {
        private Vec3 N;

        public Diffuse(DiffuseParams p, bool trans)
        {
            N = p.N;
            if (trans)
            {
                N = -N;
            }
        }

        public override BsdfSample eval(Vec3 wo, Vec3 wi)
        {
            float pdf = MathF.Max(N.dot(wi), 0.0f) * (float)(1.0 / Math.PI);
            return new BsdfSample(wi, new Color3(1.0f), pdf, 1.0f);
        }

        public override BsdfSample sample(Vec3 wo, float rx, float ry, float rz)
        {
            Vec3 out_dir;
            float pdf;
            Sampling.sample_cosine_hemisphere(N, rx, ry, out out_dir, out pdf);
            return new BsdfSample(out_dir, new Color3(1.0f), pdf, 1.0f);
        }
    }

    public sealed unsafe class Phong : BSDF
    {
        private Vec3 N;
        private float exponent;

        public Phong(PhongParams p)
        {
            N = p.N;
            exponent = p.exponent;
        }

        public override BsdfSample eval(Vec3 wo, Vec3 wi)
        {
            float cosNI = N.dot(wi);
            float cosNO = N.dot(wo);
            if (cosNI > 0 && cosNO > 0)
            {
                Vec3 R = (2 * cosNO) * N - wo;
                float cosRI = R.dot(wi);
                if (cosRI > 0)
                {
                    float pdf = (exponent + 1) * (float)(1.0 / Math.PI / 2) * fmath.fast_safe_pow(cosRI, exponent);
                    return new BsdfSample(wi, new Color3(cosNI * (exponent + 2) / (exponent + 1)), pdf, 1 / (1 + exponent));
                }
            }
            return new BsdfSample();
        }

        public override BsdfSample sample(Vec3 wo, float rx, float ry, float rz)
        {
            float cosNO = N.dot(wo);
            if (cosNO > 0)
            {
                Vec3 R = (2 * cosNO) * N - wo;
                float phi = 2 * (float)Math.PI * rx;
                float sp;
                float cp;
                fmath.fast_sincos(phi, &sp, &cp);
                float cosTheta = fmath.fast_safe_pow(ry, 1 / (exponent + 1));
                float sinTheta2 = 1 - cosTheta * cosTheta;
                float sinTheta = sinTheta2 > 0 ? MathF.Sqrt(sinTheta2) : 0;
                Vec3 wi = TangentFrame.from_normal(R).get(cp * sinTheta, sp * sinTheta, cosTheta);
                return eval(wo, wi);
            }
            return new BsdfSample();
        }
    }

    public sealed unsafe class Ward : BSDF
    {
        private Vec3 N, T;
        private float ax, ay;

        public Ward(WardParams p)
        {
            N = p.N;
            T = p.T;
            ax = p.ax;
            ay = p.ay;
        }

        public override BsdfSample eval(Vec3 wo, Vec3 wi)
        {
            float cosNO = N.dot(wo);
            float cosNI = N.dot(wi);
            if (cosNI > 0 && cosNO > 0)
            {
                Vec3 H = wi + wo;
                H = V3.normalize(H);
                TangentFrame tf = TangentFrame.from_normal_and_tangent(N, T);
                float dotx = tf.getx(H) / ax;
                float doty = tf.gety(H) / ay;
                float dotn = tf.getz(H);
                float oh = H.dot(wi);
                float e = fmath.fast_exp(-(dotx * dotx + doty * doty) / (dotn * dotn));
                float c = (float)(4 * Math.PI) * ax * ay;
                float k = oh * dotn * dotn * dotn;
                float pdf = e / (c * k);
                return new BsdfSample(wi, new Color3(k * MathF.Sqrt(cosNI / cosNO)), pdf, MathF.Max(ax, ay));
            }
            return new BsdfSample();
        }

        public override BsdfSample sample(Vec3 wo, float rx, float ry, float rz)
        {
            float cosNO = N.dot(wo);
            if (cosNO > 0)
            {
                TangentFrame tf = TangentFrame.from_normal_and_tangent(N, T);
                float phi = 2 * (float)Math.PI * rx;
                float sp;
                float cp;
                fmath.fast_sincos(phi, &sp, &cp);
                float cosPhi = ax * cp;
                float sinPhi = ay * sp;
                float k = 1 / MathF.Sqrt(cosPhi * cosPhi + sinPhi * sinPhi);
                cosPhi *= k;
                sinPhi *= k;
                float thetaDenom = (cosPhi * cosPhi) / (ax * ax) + (sinPhi * sinPhi) / (ay * ay);
                float tanTheta2 = -fmath.fast_log(1 - ry) / thetaDenom;
                float cosTheta = 1 / MathF.Sqrt(1 + tanTheta2);
                float sinTheta = cosTheta * MathF.Sqrt(tanTheta2);
                Vec3 h;
                h.x = sinTheta * cosPhi;
                h.y = sinTheta * sinPhi;
                h.z = cosTheta;
                float dotx = h.x / ax;
                float doty = h.y / ay;
                float dotn = h.z;
                h = tf.get(h.x, h.y, h.z);
                float oh = h.dot(wo);
                Vec3 wi = 2 * oh * h - wo;
                float cosNI = N.dot(wi);
                if (cosNI > 0)
                {
                    float e = fmath.fast_exp(-(dotx * dotx + doty * doty) / (dotn * dotn));
                    float c = (float)(4 * Math.PI) * ax * ay;
                    float kk = oh * dotn * dotn * dotn;
                    float pdf = e / (c * kk);
                    return new BsdfSample(wi, new Color3(kk * MathF.Sqrt(cosNI / cosNO)), pdf, MathF.Max(ax, ay));
                }
            }
            return new BsdfSample();
        }
    }

    /// The microfacet BSDF of the "microfacet" closure. The anisotropic variants of GGX and Beckmann come from "Understanding the Masking-Shadowing
    /// Function in Microfacet-Based BRDFs" (Heitz, JCGT 2014, section 5.4) with the height correlated masking and shadowing function; the sampling is
    /// derived from d'Eon and Heitz (EGSR 2014) and, for Beckmann, from Jakob's improved visible normal sampling.
    public sealed class Microfacet : BSDF
    {
        private readonly bool ggx;
        private readonly int Refract;
        private Vec3 N;
        private float xalpha, yalpha, eta;
        private TangentFrame tf;

        public Microfacet(MicrofacetParams p, bool ggx, int refract)
        {
            this.ggx = ggx;
            Refract = refract;
            N = p.N;
            xalpha = p.xalpha;
            yalpha = p.yalpha;
            eta = p.eta;
            tf = TangentFrame.from_normal_and_tangent(p.N, p.U);
        }

        public override Color3 get_albedo(Vec3 wo)
        {
            if (Refract == 2)
            {
                return new Color3(1.0f);
            }
            float fr = Optics.fresnel_dielectric(N.dot(wo), eta);
            return new Color3(Refract != 0 ? 1 - fr : fr);
        }

        public override BsdfSample eval(Vec3 wo, Vec3 wi)
        {
            Vec3 wo_l = tf.tolocal(wo);
            Vec3 wi_l = tf.tolocal(wi);
            if (Refract == 0 || Refract == 2)
            {
                if (wo_l.z > 0 && wi_l.z > 0)
                {
                    Vec3 m = V3.normalize(wi_l + wo_l);
                    float D = evalD(m);
                    float Lambda_o = evalLambda(wo_l);
                    float Lambda_i = evalLambda(wi_l);
                    float G2 = evalG2(Lambda_o, Lambda_i);
                    float G1 = evalG1(Lambda_o);
                    float Fr = Optics.fresnel_dielectric(m.dot(wo_l), eta);
                    float pdf = (G1 * D * 0.25f) / wo_l.z;
                    float outv = G2 / G1;
                    if (Refract == 2)
                    {
                        pdf *= Fr;
                        return new BsdfSample(wi, new Color3(outv), pdf, MathF.Max(xalpha, yalpha));
                    }
                    else
                    {
                        return new BsdfSample(wi, new Color3(outv * Fr), pdf, MathF.Max(xalpha, yalpha));
                    }
                }
            }
            if (Refract == 1 || Refract == 2)
            {
                if (wi_l.z < 0 && wo_l.z > 0.0f)
                {
                    Vec3 ht = -(eta * wi_l + wo_l);
                    if (eta < 1.0f)
                    {
                        ht = -ht;
                    }
                    Vec3 Ht = V3.normalize(ht);
                    float cosHO = Ht.dot(wo_l);
                    float Ft = 1.0f - Optics.fresnel_dielectric(cosHO, eta);
                    if (Ft > 0)
                    {
                        float cosHI = Ht.dot(wi_l);
                        float cosThetaM = Ht.z;
                        if (cosThetaM <= 0.0f)
                        {
                            return new BsdfSample();
                        }
                        float Dt = evalD(Ht);
                        float Lambda_o = evalLambda(wo_l);
                        float Lambda_i = evalLambda(wi_l);
                        float G2 = evalG2(Lambda_o, Lambda_i);
                        float G1 = evalG1(Lambda_o);
                        float invHt2 = 1 / ht.dot(ht);
                        float pdf = (MathF.Abs(cosHI * cosHO) * (eta * eta) * (G1 * Dt) * invHt2) / wo_l.z;
                        float outv = G2 / G1;
                        if (Refract == 2)
                        {
                            pdf *= Ft;
                            return new BsdfSample(wi, new Color3(outv), pdf, MathF.Max(xalpha, yalpha));
                        }
                        else
                        {
                            return new BsdfSample(wi, new Color3(outv * Ft), pdf, MathF.Max(xalpha, yalpha));
                        }
                    }
                }
            }
            return new BsdfSample();
        }

        public override BsdfSample sample(Vec3 wo, float rx, float ry, float rz)
        {
            Vec3 wo_l = tf.tolocal(wo);
            float cosNO = wo_l.z;
            if (!(cosNO > 0))
            {
                return new BsdfSample();
            }
            Vec3 m = sampleMicronormal(wo_l, rx, ry);
            float cosMO = m.dot(wo_l);
            float F = Optics.fresnel_dielectric(cosMO, eta);
            if (Refract == 0 || (Refract == 2 && rz < F))
            {
                Vec3 wi_l = (2.0f * cosMO) * m - wo_l;
                float D = evalD(m);
                float Lambda_o = evalLambda(wo_l);
                float Lambda_i = evalLambda(wi_l);
                float G2 = evalG2(Lambda_o, Lambda_i);
                float G1 = evalG1(Lambda_o);
                Vec3 wi = tf.toworld(wi_l);
                float pdf = (G1 * D * 0.25f) / cosNO;
                float outv = G2 / G1;
                if (Refract == 2)
                {
                    pdf *= F;
                    return new BsdfSample(wi, new Color3(outv), pdf, MathF.Max(xalpha, yalpha));
                }
                return new BsdfSample(wi, new Color3(F * outv), pdf, MathF.Max(xalpha, yalpha));
            }
            else
            {
                Vec3 M = tf.toworld(m);
                Vec3 wi;
                float Ft = Optics.fresnel_refraction(-wo, M, eta, out wi);
                Vec3 wi_l = tf.tolocal(wi);
                float cosHO = m.dot(wo_l);
                float cosHI = m.dot(wi_l);
                float D = evalD(m);
                float Lambda_o = evalLambda(wo_l);
                float Lambda_i = evalLambda(wi_l);
                float G2 = evalG2(Lambda_o, Lambda_i);
                float G1 = evalG1(Lambda_o);
                Vec3 ht = -(eta * wi_l + wo_l);
                float invHt2 = 1.0f / ht.dot(ht);
                float pdf = (MathF.Abs(cosHI * cosHO) * (eta * eta) * (G1 * D) * invHt2) / MathF.Abs(wo_l.z);
                float outv = G2 / G1;
                if (Refract == 2)
                {
                    pdf *= Ft;
                    return new BsdfSample(wi, new Color3(outv), pdf, MathF.Max(xalpha, yalpha));
                }
                return new BsdfSample(wi, new Color3(Ft * outv), pdf, MathF.Max(xalpha, yalpha));
            }
        }

        private static float SQR(float x)
        {
            return x * x;
        }

        // The distribution functions (GGX or Beckmann)
        private float dist_F(float tan_m2)
        {
            if (ggx)
            {
                return 1 / ((float)Math.PI * (1 + tan_m2) * (1 + tan_m2));
            }
            return (float)(1 / Math.PI) * fmath.fast_exp(-tan_m2);
        }

        private float dist_Lambda(float a2)
        {
            if (ggx)
            {
                return 0.5f * (-1.0f + MathF.Sqrt(1.0f + 1.0f / a2));
            }
            float a = MathF.Sqrt(a2);
            return a < 1.6f ? (1.0f - 1.259f * a + 0.396f * a2) / (3.535f * a + 2.181f * a2) : 0.0f;
        }

        private Vec2f dist_sampleSlope(float cos_theta, float randu, float randv)
        {
            if (ggx)
            {
                Vec2f slope;
                float c = cos_theta < 1e-6f ? 1e-6f : cos_theta;
                float Q = (1 + c) * randu - c;
                float num = c * MathF.Sqrt((1 - c) * (1 + c)) - Q * MathF.Sqrt((1 - Q) * (1 + Q));
                float den = (Q - c) * (Q + c);
                float eps = 1.0f / 4294967296.0f;
                den = MathF.Abs(den) < eps ? MathF.CopySign(eps, den) : den;
                slope.x = num / den;
                float Ru = 1 - 2 * randv;
                float u2 = MathF.Abs(Ru);
                float z = (u2 * (u2 * (u2 * 0.27385f - 0.73369f) + 0.46341f)) / (u2 * (u2 * (u2 * 0.093073f + 0.309420f) - 1.0f) + 0.597999f);
                slope.y = MathF.CopySign(1.0f, Ru) * z * MathF.Sqrt(1.0f + slope.x * slope.x);
                return slope;
            }
            else
            {
                float SQRT_PI_INV = 1 / MathF.Sqrt((float)Math.PI);
                float ct = cos_theta < 1e-6f ? 1e-6f : cos_theta;
                float tanThetaI = MathF.Sqrt(1 - ct * ct) / ct;
                float cotThetaI = 1 / tanThetaI;
                float c = fmath.fast_erf(cotThetaI);
                float K = tanThetaI * SQRT_PI_INV;
                float yApprox = randu * (1.0f + c + K * (1 - c * c));
                float yExact = randu * (1.0f + c + K * fmath.fast_exp(-cotThetaI * cotThetaI));
                float b = K > 0 ? (0.5f - MathF.Sqrt(K * (K - yApprox + 1.0f) + 0.25f)) / K : yApprox - 1.0f;
                float invErf = fmath.fast_ierf(b);
                float value = 1.0f + b + K * fmath.fast_exp(-invErf * invErf) - yExact;
                Vec2f slope;
                if (MathF.Abs(value) > 1e-6f)
                {
                    b -= value / (1 - invErf * tanThetaI);
                    invErf = fmath.fast_ierf(b);
                    value = 1.0f + b + K * fmath.fast_exp(-invErf * invErf) - yExact;
                    b -= value / (1 - invErf * tanThetaI);
                    slope.x = fmath.fast_ierf(b);
                }
                else
                {
                    slope.x = invErf;
                }
                slope.y = fmath.fast_ierf(2.0f * randv - 1.0f);
                return slope;
            }
        }

        private float evalLambda(Vec3 w)
        {
            float cosTheta2 = SQR(w.z);
            float cosPhi2st2 = SQR(w.x * xalpha);
            float sinPhi2st2 = SQR(w.y * yalpha);
            return dist_Lambda(cosTheta2 / (cosPhi2st2 + sinPhi2st2));
        }

        private static float evalG2(float Lambda_i, float Lambda_o)
        {
            return 1 / (Lambda_i + Lambda_o + 1);
        }

        private static float evalG1(float Lambda_v)
        {
            return 1 / (Lambda_v + 1);
        }

        private float evalD(Vec3 Hr)
        {
            float cosThetaM = Hr.z;
            if (cosThetaM > 0)
            {
                float cosPhi2st2 = SQR(Hr.x / xalpha);
                float sinPhi2st2 = SQR(Hr.y / yalpha);
                float cosThetaM2 = SQR(cosThetaM);
                float cosThetaM4 = SQR(cosThetaM2);
                float tanThetaM2 = (cosPhi2st2 + sinPhi2st2) / cosThetaM2;
                return dist_F(tanThetaM2) / (xalpha * yalpha * cosThetaM4);
            }
            return 0;
        }

        private Vec3 sampleMicronormal(Vec3 wo, float randu, float randv)
        {
            Vec3 swo = wo;
            swo.x *= xalpha;
            swo.y *= yalpha;
            swo = V3.normalize(swo);
            float cos_theta = MathF.Max(swo.z, 0.0f);
            float cos_phi = 1;
            float sin_phi = 0;
            if (cos_theta < 0.99999f)
            {
                float invnorm = 1 / MathF.Sqrt(SQR(swo.x) + SQR(swo.y));
                cos_phi = swo.x * invnorm;
                sin_phi = swo.y * invnorm;
            }
            Vec2f slope = dist_sampleSlope(cos_theta, randu, randv);
            Vec2f s = new Vec2f(cos_phi * slope.x - sin_phi * slope.y, sin_phi * slope.x + cos_phi * slope.y);
            s.x *= xalpha;
            s.y *= yalpha;
            float mlen = MathF.Sqrt(s.x * s.x + s.y * s.y + 1);
            return new Vec3(MathF.Abs(s.x) < mlen ? -s.x / mlen : 1.0f, MathF.Abs(s.y) < mlen ? -s.y / mlen : 1.0f, 1.0f / mlen);
        }
    }

    public sealed class Reflection : BSDF
    {
        private Vec3 N;
        private float eta;

        public Reflection(ReflectionParams p)
        {
            N = p.N;
            eta = p.eta;
        }

        public override Color3 get_albedo(Vec3 wo)
        {
            float cosNO = N.dot(wo);
            if (cosNO > 0)
            {
                return new Color3(Optics.fresnel_dielectric(cosNO, eta));
            }
            return new Color3(1);
        }

        public override BsdfSample eval(Vec3 wo, Vec3 wi)
        {
            return new BsdfSample();
        }

        public override BsdfSample sample(Vec3 wo, float rx, float ry, float rz)
        {
            float cosNO = N.dot(wo);
            if (cosNO > 0)
            {
                Vec3 wi = (2 * cosNO) * N - wo;
                float pdf = float.PositiveInfinity;
                return new BsdfSample(wi, new Color3(Optics.fresnel_dielectric(cosNO, eta)), pdf, 0);
            }
            return new BsdfSample();
        }
    }

    public sealed class Refraction : BSDF
    {
        private Vec3 N;
        private float eta;

        public Refraction(RefractionParams p)
        {
            N = p.N;
            eta = p.eta;
        }

        public override Color3 get_albedo(Vec3 wo)
        {
            float cosNO = N.dot(wo);
            return new Color3(1 - Optics.fresnel_dielectric(cosNO, eta));
        }

        public override BsdfSample eval(Vec3 wo, Vec3 wi)
        {
            return new BsdfSample();
        }

        public override BsdfSample sample(Vec3 wo, float rx, float ry, float rz)
        {
            float pdf = float.PositiveInfinity;
            Vec3 wi;
            float Ft = Optics.fresnel_refraction(-wo, N, eta, out wi);
            return new BsdfSample(wi, new Color3(Ft), pdf, 0);
        }
    }

    public sealed class Transparent : BSDF
    {
        public override BsdfSample eval(Vec3 wo, Vec3 wi)
        {
            return new BsdfSample();
        }

        public override BsdfSample sample(Vec3 wo, float rx, float ry, float rz)
        {
            Vec3 wi = -wo;
            float pdf = float.PositiveInfinity;
            return new BsdfSample(wi, new Color3(1.0f), pdf, 0);
        }
    }

    /// The BSDL OrenNayarDiffuseLobe (MaterialX oren_nayar_diffuse_bsdf) as the "oren_nayar" closure uses it: white albedo and no energy compensation
    public sealed unsafe class OrenNayarLobe : BSDF
    {
        private Vec3 fX, fY, fZ;
        private Color3 diff_albedo;
        private float diff_roughness;

        public OrenNayarLobe(OrenNayarParams p, Vec3 wo)
        {
            Vec3 Z = visible_normal(wo, p.N, p.N);
            float s = MathF.CopySign(1.0f, Z.z);
            float a = -1.0f / (s + Z.z);
            float b = Z.x * Z.y * a;
            fX = new Vec3(1.0f + s * Z.x * Z.x * a, s * b, -s * Z.x);
            fY = new Vec3(b, s + Z.y * Z.y * a, -Z.y);
            fZ = Z;
            diff_albedo = new Color3(1, 1, 1);
            diff_roughness = Math.Clamp(p.sigma, 0.0f, 1.0f);
        }

        private static float max_abs_xyz(Vec3 v)
        {
            return MathF.Max(MathF.Abs(v.x), MathF.Max(MathF.Abs(v.y), MathF.Abs(v.z)));
        }

        /// BsdfGlobals::visible_normal
        private static Vec3 visible_normal(Vec3 wo, Vec3 Ngf, Vec3 N)
        {
            if (wo.dot(N) > 0.0f)
            {
                return N;
            }
            Vec3 V = wo.cross(N);
            if (max_abs_xyz(V) < 1e-4f)
            {
                V = wo.cross(Ngf);
                if (max_abs_xyz(V) < 1e-4f)
                {
                    float s = MathF.CopySign(1.0f, wo.z);
                    float a = -1.0f / (s + wo.z);
                    V = new Vec3(wo.x * wo.y * a, s + wo.y * wo.y * a, -wo.y);
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

        private Vec3 local(Vec3 a)
        {
            return new Vec3(a.dot(fX), a.dot(fY), a.dot(fZ));
        }

        private Vec3 world(Vec3 a)
        {
            return new Vec3(fX.x * a.x + fY.x * a.y + fZ.x * a.z, fX.y * a.x + fY.y * a.y + fZ.y * a.z, fX.z * a.x + fY.z * a.y + fZ.z * a.z);
        }

        private BsdfSample eval_local(Vec3 wo, Vec3 wi, Vec3 wi_world)
        {
            float cosNI = Math.Clamp(wi.z, 0.0f, 1.0f);
            float cosNO = Math.Clamp(wo.z, 0.0f, 1.0f);
            if (cosNI <= 0.0f || cosNO <= 0.0f)
            {
                return new BsdfSample();
            }
            float cosIO = Math.Clamp(wo.dot(wi), -1.0f, 1.0f);
            float s = cosIO - cosNI * cosNO;
            float pdf = cosNI * (float)(1.0 / Math.PI);
            float s2 = diff_roughness * diff_roughness;
            float A = 1.0f - 0.50f * s2 / (s2 + 0.33f);
            float B = 0.45f * s2 / (s2 + 0.09f);
            float stinv = s > 0.0f ? s / MathF.Max(cosNI, cosNO) : 0.0f;
            float f_ss = A + B * stinv;
            return new BsdfSample(wi_world, diff_albedo * f_ss, pdf, 1.0f);
        }

        public override Color3 get_albedo(Vec3 wo)
        {
            return diff_albedo;
        }

        public override BsdfSample eval(Vec3 wo, Vec3 wi)
        {
            return eval_local(local(wo), local(wi), wi);
        }

        public override BsdfSample sample(Vec3 wo, float rx, float ry, float rz)
        {
            Vec3 wo_l = local(wo);
            if (wo_l.z <= 0.0f)
            {
                return new BsdfSample();
            }
            Vec3 wi = sample_cos_hemisphere(rx, ry);
            BsdfSample s = eval_local(wo_l, wi, wi);
            s.wi = world(s.wi);
            return s;
        }

        private static float fast_cos_quadrant(float x)
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

        private static float fast_sin_quadrant(float x)
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

        private static Vec3 sample_cos_hemisphere(float randu, float randv)
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
    }

    public sealed class CompositeBSDF
    {
        private const int MaxEntries = 8;

        private Color3[] weights = new Color3[MaxEntries];
        private float[] pdfs = new float[MaxEntries];
        private BSDF[] bsdfs = new BSDF[MaxEntries];
        private int num_bsdfs;

        private static float dot(Color3 a, Color3 b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }

        public void prepare(Vec3 wo, Color3 path_weight, bool absorb)
        {
            float total = 0;
            for (int i = 0; i < num_bsdfs; i++)
            {
                pdfs[i] = dot(weights[i], path_weight * bsdfs[i].get_albedo(wo)) / (path_weight.x + path_weight.y + path_weight.z);
                total += pdfs[i];
            }
            if ((!absorb && total > 0) || total > 1)
            {
                for (int i = 0; i < num_bsdfs; i++)
                {
                    pdfs[i] /= total;
                }
            }
        }

        public Color3 get_albedo(Vec3 wo)
        {
            Color3 result = new Color3(0, 0, 0);
            for (int i = 0; i < num_bsdfs; i++)
            {
                result += weights[i] * bsdfs[i].get_albedo(wo);
            }
            return result;
        }

        public BsdfSample eval(Vec3 wo, Vec3 wi)
        {
            BsdfSample s = new BsdfSample();
            for (int i = 0; i < num_bsdfs; i++)
            {
                BsdfSample b = bsdfs[i].eval(wo, wi);
                b.weight *= weights[i];
                MIS.update_eval(ref s.weight, ref s.pdf, b.weight, b.pdf, pdfs[i]);
                s.roughness += b.roughness * pdfs[i];
            }
            return s;
        }

        public BsdfSample sample(Vec3 wo, float rx, float ry, float rz)
        {
            float accum = 0;
            for (int i = 0; i < num_bsdfs; i++)
            {
                if (rx < (pdfs[i] + accum))
                {
                    rx = (rx - accum) / pdfs[i];
                    rx = MathF.Min(rx, 0.99999994f);
                    BsdfSample s = bsdfs[i].sample(wo, rx, ry, rz);
                    s.weight *= weights[i] * (1 / pdfs[i]);
                    s.pdf *= pdfs[i];
                    if (s.pdf == 0.0f)
                    {
                        return new BsdfSample();
                    }
                    for (int j = 0; j < num_bsdfs; j++)
                    {
                        if (i != j)
                        {
                            BsdfSample b = bsdfs[j].eval(wo, s.wi);
                            b.weight *= weights[j];
                            MIS.update_eval(ref s.weight, ref s.pdf, b.weight, b.pdf, pdfs[j]);
                        }
                    }
                    return s;
                }
                accum += pdfs[i];
            }
            return new BsdfSample();
        }

        public bool add_bsdf(Color3 w, BSDF bsdf)
        {
            if (num_bsdfs >= MaxEntries)
            {
                return false;
            }
            weights[num_bsdfs] = w;
            bsdfs[num_bsdfs] = bsdf;
            num_bsdfs++;
            return true;
        }
    }

    public sealed class ShadingResult
    {
        public Color3 Le = new Color3(0, 0, 0);
        public CompositeBSDF bsdf = new CompositeBSDF();
    }

    public static unsafe class ShadingClosures
    {
        private static readonly ulong uh_ggx = new ustring("ggx").hash();
        private static readonly ulong uh_beckmann = new ustring("beckmann").hash();
        private static readonly ulong uh_default = new ustring("default").hash();

        /// Describes where the closure parameters live (the CLOSURE_*_PARAM macros) and registers the closures with the shading system
        public static void register_closures(ShadingSystem shadingsys)
        {
            reg(shadingsys, "emission", ClosureIDs.EMISSION_ID, new ClosureParam[] { ClosureParam.CLOSURE_FINISH_PARAM(1, 1) });
            reg(shadingsys, "background", ClosureIDs.BACKGROUND_ID, new ClosureParam[] { ClosureParam.CLOSURE_FINISH_PARAM(1, 1) });
            reg(shadingsys, "diffuse", ClosureIDs.DIFFUSE_ID, new ClosureParam[] { ClosureParam.CLOSURE_VECTOR_PARAM(0), ClosureParam.CLOSURE_FINISH_PARAM(12, 4) });
            reg(shadingsys, "oren_nayar", ClosureIDs.OREN_NAYAR_ID,
                new ClosureParam[] { ClosureParam.CLOSURE_VECTOR_PARAM(0), ClosureParam.CLOSURE_FLOAT_PARAM(12), ClosureParam.CLOSURE_FINISH_PARAM(16, 4) });
            reg(shadingsys, "translucent", ClosureIDs.TRANSLUCENT_ID,
                new ClosureParam[] { ClosureParam.CLOSURE_VECTOR_PARAM(0), ClosureParam.CLOSURE_FINISH_PARAM(12, 4) });
            reg(shadingsys, "phong", ClosureIDs.PHONG_ID,
                new ClosureParam[] { ClosureParam.CLOSURE_VECTOR_PARAM(0), ClosureParam.CLOSURE_FLOAT_PARAM(12), ClosureParam.CLOSURE_FINISH_PARAM(16, 4) });
            reg(shadingsys, "ward", ClosureIDs.WARD_ID,
                new ClosureParam[]
                {
                    ClosureParam.CLOSURE_VECTOR_PARAM(0), ClosureParam.CLOSURE_VECTOR_PARAM(12), ClosureParam.CLOSURE_FLOAT_PARAM(24),
                    ClosureParam.CLOSURE_FLOAT_PARAM(28), ClosureParam.CLOSURE_FINISH_PARAM(32, 4)
                });
            reg(shadingsys, "microfacet", ClosureIDs.MICROFACET_ID,
                new ClosureParam[]
                {
                    ClosureParam.CLOSURE_STRING_PARAM(0), ClosureParam.CLOSURE_VECTOR_PARAM(8), ClosureParam.CLOSURE_VECTOR_PARAM(20),
                    ClosureParam.CLOSURE_FLOAT_PARAM(32), ClosureParam.CLOSURE_FLOAT_PARAM(36), ClosureParam.CLOSURE_FLOAT_PARAM(40),
                    ClosureParam.CLOSURE_INT_PARAM(44), ClosureParam.CLOSURE_FINISH_PARAM(48, 8)
                });
            reg(shadingsys, "reflection", ClosureIDs.REFLECTION_ID,
                new ClosureParam[] { ClosureParam.CLOSURE_VECTOR_PARAM(0), ClosureParam.CLOSURE_FINISH_PARAM(16, 4) });
            reg(shadingsys, "reflection", ClosureIDs.FRESNEL_REFLECTION_ID,
                new ClosureParam[] { ClosureParam.CLOSURE_VECTOR_PARAM(0), ClosureParam.CLOSURE_FLOAT_PARAM(12), ClosureParam.CLOSURE_FINISH_PARAM(16, 4) });
            reg(shadingsys, "refraction", ClosureIDs.REFRACTION_ID,
                new ClosureParam[] { ClosureParam.CLOSURE_VECTOR_PARAM(0), ClosureParam.CLOSURE_FLOAT_PARAM(12), ClosureParam.CLOSURE_FINISH_PARAM(16, 4) });
            reg(shadingsys, "transparent", ClosureIDs.TRANSPARENT_ID, new ClosureParam[] { ClosureParam.CLOSURE_FINISH_PARAM(1, 1) });
        }

        private static void reg(ShadingSystem shadingsys, string name, int id, ClosureParam[] @params)
        {
            shadingsys.register_closure(name, id, @params, null, null);
        }

        private static bool is_black(Color3 c)
        {
            return c.x == 0 && c.y == 0 && c.z == 0;
        }

        /// Walks the closure tree, adding the emission to result.Le and (unless only the light is wanted) a lobe per BSDF closure
        public static void process_bsdf_closure(ShaderGlobals* sg, float path_roughness, ShadingResult result, ClosureColor* closure, Color3 w,
                                                bool light_only)
        {
            if (closure == null)
            {
                return;
            }
            const int STACK_SIZE = 16;
            int stack_idx = 0;
            ClosureColor** ptr_stack = stackalloc ClosureColor*[STACK_SIZE];
            Color3* weight_stack = stackalloc Color3[STACK_SIZE];
            Color3 weight = w;
            while (closure != null)
            {
                switch (closure->id)
                {
                    case ClosureIDs.MUL:
                        {
                            ClosureMul* mul = ClosureColor.as_mul(closure);
                            weight *= mul->weight;
                            closure = mul->closure;
                            break;
                        }
                    case ClosureIDs.ADD:
                        {
                            ClosureAdd* add = ClosureColor.as_add(closure);
                            ptr_stack[stack_idx] = add->closureB;
                            weight_stack[stack_idx++] = weight;
                            closure = add->closureA;
                            break;
                        }
                    default:
                        {
                            ClosureComponent* comp = ClosureColor.as_comp(closure);
                            Color3 cw = weight * new Color3(comp->w);
                            closure = null;
                            if (comp->id == ClosureIDs.EMISSION_ID)
                            {
                                result.Le += cw;
                            }
                            else if (!light_only)
                            {
                                void* data = ClosureComponent.data(comp);
                                bool ok = false;
                                switch (comp->id)
                                {
                                    case ClosureIDs.DIFFUSE_ID:
                                        ok = result.bsdf.add_bsdf(cw, new Diffuse(*(DiffuseParams*)data, false));
                                        break;
                                    case ClosureIDs.OREN_NAYAR_ID:
                                        ok = result.bsdf.add_bsdf(cw, new OrenNayarLobe(*(OrenNayarParams*)data, -sg->I));
                                        break;
                                    case ClosureIDs.TRANSLUCENT_ID:
                                        ok = result.bsdf.add_bsdf(cw, new Diffuse(*(DiffuseParams*)data, true));
                                        break;
                                    case ClosureIDs.PHONG_ID:
                                        ok = result.bsdf.add_bsdf(cw, new Phong(*(PhongParams*)data));
                                        break;
                                    case ClosureIDs.WARD_ID:
                                        ok = result.bsdf.add_bsdf(cw, new Ward(*(WardParams*)data));
                                        break;
                                    case ClosureIDs.MICROFACET_ID:
                                        {
                                            MicrofacetParams mp = *(MicrofacetParams*)data;
                                            if (mp.dist == uh_ggx)
                                            {
                                                ok = result.bsdf.add_bsdf(cw, new Microfacet(mp, true, mp.refract));
                                            }
                                            else if (mp.dist == uh_beckmann || mp.dist == uh_default)
                                            {
                                                ok = result.bsdf.add_bsdf(cw, new Microfacet(mp, false, mp.refract));
                                            }
                                            break;
                                        }
                                    case ClosureIDs.REFLECTION_ID:
                                    case ClosureIDs.FRESNEL_REFLECTION_ID:
                                        ok = result.bsdf.add_bsdf(cw, new Reflection(*(ReflectionParams*)data));
                                        break;
                                    case ClosureIDs.REFRACTION_ID:
                                        ok = result.bsdf.add_bsdf(cw, new Refraction(*(RefractionParams*)data));
                                        break;
                                    case ClosureIDs.TRANSPARENT_ID:
                                        ok = result.bsdf.add_bsdf(cw, new Transparent());
                                        break;
                                }
                                if (!ok)
                                {
                                    throw new InvalidOperationException("Invalid closure invoked in surface shader");
                                }
                            }
                            break;
                        }
                }
                if (closure == null && stack_idx > 0)
                {
                    closure = ptr_stack[--stack_idx];
                    weight = weight_stack[stack_idx];
                }
            }
        }

        public static void process_closure(ShaderGlobals* sg, float path_roughness, ShadingResult result, ClosureColor* Ci, bool light_only)
        {
            process_bsdf_closure(sg, path_roughness, result, Ci, new Color3(1), light_only);
        }

        public static Vec3 process_background_closure(ClosureColor* closure)
        {
            if (closure == null)
            {
                return new Vec3(0, 0, 0);
            }
            const int STACK_SIZE = 16;
            int stack_idx = 0;
            ClosureColor** ptr_stack = stackalloc ClosureColor*[STACK_SIZE];
            Color3* weight_stack = stackalloc Color3[STACK_SIZE];
            Color3 branch_weight = new Color3(1.0f);
            Color3 accumulated_weight = new Color3(0.0f);
            while (closure != null)
            {
                switch (closure->id)
                {
                    case ClosureIDs.MUL:
                        {
                            ClosureMul* mul = ClosureColor.as_mul(closure);
                            branch_weight *= mul->weight;
                            closure = mul->closure;
                            break;
                        }
                    case ClosureIDs.ADD:
                        {
                            ClosureAdd* add = ClosureColor.as_add(closure);
                            ptr_stack[stack_idx] = add->closureB;
                            weight_stack[stack_idx++] = branch_weight;
                            closure = add->closureA;
                            break;
                        }
                    case ClosureIDs.BACKGROUND_ID:
                        {
                            branch_weight *= new Color3(ClosureColor.as_comp(closure)->w);
                            closure = null;
                            break;
                        }
                }
                if (closure == null)
                {
                    accumulated_weight += branch_weight;
                    if (stack_idx > 0)
                    {
                        closure = ptr_stack[--stack_idx];
                        branch_weight = weight_stack[stack_idx];
                    }
                }
            }
            return new Vec3(accumulated_weight.x, accumulated_weight.y, accumulated_weight.z);
        }
    }
}
