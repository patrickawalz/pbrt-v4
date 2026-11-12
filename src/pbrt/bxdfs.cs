// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// bxdfs.h, bxdfs.cpp
//
// The BxDF types are subclasses of the abstract class BxDF (base/bxdf.cs), which replaces the BxDF
// TaggedPointer and its Dispatch(). TopOrBottomBxDF<Top, Bottom> (a pointer to either layer) is
// simply a BxDF reference. LayeredBxDF is generic in its two layers as in C++; its "bool twoSided"
// template argument is a constructor argument (true for both instantiations pbrt uses).

using System;
using System.Collections.Generic;
using System.IO;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{

    // DiffuseBxDF Definition
    public sealed unsafe class DiffuseBxDF : BxDF
    {
        // DiffuseBxDF Public Methods
        public DiffuseBxDF() { }
        public DiffuseBxDF(SampledSpectrum R) { this.R = R; }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi))
                return new SampledSpectrum(0f);
            return R * InvPi;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                return default;
            // Sample cosine-weighted hemisphere to compute _wi_ and _pdf_
            Vector3f wi = SampleCosineHemisphere(u);
            if (wo.z < 0)
                wi.z *= -1;
            Float pdf = CosineHemispherePDF(AbsCosTheta(wi));

            return new BSDFSample(R * InvPi, wi, pdf, BxDFFlags.DiffuseReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi))
                return 0;
            return CosineHemispherePDF(AbsCosTheta(wi));
        }

        public static string Name() { return "DiffuseBxDF"; }

        public override void Regularize() { }

        public override BxDFFlags Flags()
        {
            return (bool)R ? BxDFFlags.DiffuseReflection : BxDFFlags.Unset;
        }

        public override string ToString()
        {
            return StringPrintf("[ DiffuseBxDF R: %s ]", R);
        }

        private SampledSpectrum R;
    }

    // EonDiffuseBxDF Definition
    // (not in pbrt-v4) Energy-preserving Oren-Nayar (EON): Portsmouth, Kutz and Hill, JCGT 14(1), 2025. The single scattering
    // lobe of the Fujii form of Oren-Nayar plus the multiple scattering lobe that brings the albedo of a white surface to 1.
    // Sampled by the mixture of a uniform hemisphere and a clipped linearly transformed cosine of the paper.
    public sealed unsafe class EonDiffuseBxDF : BxDF
    {
        // EonDiffuseBxDF Public Methods
        public EonDiffuseBxDF() { }
        public EonDiffuseBxDF(SampledSpectrum R, Float roughness)
        {
            this.R = R;
            this.roughness = Clamp(roughness, 0, 1);
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi))
            {
                return new SampledSpectrum(0f);
            }
            if (AbsCosTheta(wi) == 0 || AbsCosTheta(wo) == 0)
            {
                return new SampledSpectrum(0f);
            }
            bool flip = wo.z < 0;
            return Eval(flip ? -wi : wi, flip ? -wo : wo);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            {
                return default;
            }
            if (wo.z == 0)
            {
                return default;
            }
            bool flip = wo.z < 0;
            Vector3f woL = flip ? -wo : wo;
            Vector3f wiL;
            Float pdf;
            if (!Sample(woL, uc, u, out wiL, out pdf) || !(wiL.z > 0) || !(pdf > 0))
            {
                return default;
            }
            return new BSDFSample(Eval(wiL, woL), flip ? -wiL : wiL, pdf, BxDFFlags.DiffuseReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi))
            {
                return 0;
            }
            if (AbsCosTheta(wi) == 0 || AbsCosTheta(wo) == 0)
            {
                return 0;
            }
            bool flip = wo.z < 0;
            return Pdf(flip ? -wo : wo, flip ? -wi : wi);
        }

        public static string Name() { return "EonDiffuseBxDF"; }

        public override void Regularize() { }

        public override BxDFFlags Flags()
        {
            return (bool)R ? BxDFFlags.DiffuseReflection : BxDFFlags.Unset;
        }

        public override string ToString()
        {
            return StringPrintf("[ EonDiffuseBxDF R: %s roughness: %f ]", R, roughness);
        }

        // EonDiffuseBxDF Private Methods
        // Directions are in the local frame with wo.z > 0 and wi.z > 0.
        private const Float K1 = 0.5f - 2f / (3f * Pi);
        private const Float K2 = 2f / 3f - 28f / (15f * Pi);

        private Float AF() { return 1 / (1 + K1 * roughness); }
        private Float Avg() { return AF() * (1 + K2 * roughness); }
        // Directional albedo of the single-scattering (FON) lobe for rho = 1 (quartic fit, error < 0.1%).
        private Float FonAlbedo(Float mu)
        {
            Float x = 1 - mu;
            Float gOverPi = x * (0.0571085289f + x * (0.491881867f + x * (-0.332181442f + x * 0.0714429953f)));
            return (1 + roughness * gOverPi) / (1 + K1 * roughness);
        }

        private SampledSpectrum Eval(Vector3f wi, Vector3f wo)
        {
            Float mi = wi.z, mo = wo.z;
            Float s = Dot(wi, wo) - mi * mo;
            Float st = s > 0 ? s / Math.Max(mi, mo) : s;
            Float af = AF(), avg = Avg();
            SampledSpectrum fss = R * (InvPi * af * (1 + roughness * st));
            SampledSpectrum rhoMs = R * R * avg / (new SampledSpectrum(1f) - R * (1 - avg));
            const Float eps = 1e-7f;
            Float ms = Math.Max(eps, 1 - FonAlbedo(mo)) * Math.Max(eps, 1 - FonAlbedo(mi)) / Math.Max(eps, 1 - avg);
            return fss + rhoMs * (InvPi * ms);
        }

        private static Float Pu(Float mu, Float r)
        {
            return MathF.Pow(r, 0.1f) * (0.162925f + (-0.372058f + (0.538233f - 0.290822f * mu) * mu) * mu);
        }

        private static void Ltc(Float mu, Float r, out Float a, out Float b, out Float c, out Float d)
        {
            a = 1 + r * (0.303392f + (-0.518982f + 0.111709f * mu) * mu + (-0.276266f + 0.335918f * mu) * r);
            b = r * (-1.16407f + 1.15859f * mu + (0.150815f - 0.150105f * mu) * r) / (mu * mu * mu - 1.43545f);
            c = 1 + r * (0.20013f + (-0.506373f + 0.261777f * mu) * mu);
            d = r * (0.540852f + (-1.01625f + 0.475392f * mu) * mu) / (-1.0743f + (0.0725628f + mu) * mu);
        }

        // Aligned frame: wo lies in the xz plane.
        private static void Basis(Vector3f wo, out Vector3f x, out Vector3f y)
        {
            Float l2 = Sqr(wo.x) + Sqr(wo.y);
            x = l2 > 0 ? new Vector3f(wo.x, wo.y, 0) / MathF.Sqrt(l2) : new Vector3f(1, 0, 0);
            y = new Vector3f(-x.y, x.x, 0);
        }

        private Float CltcPdf(Vector3f wo, Vector3f wiLocal)
        {
            Vector3f bx, by;
            Basis(wo, out bx, out by);
            Vector3f wi = new Vector3f(Dot(bx, wiLocal), Dot(by, wiLocal), wiLocal.z);
            Float a, b, c, d;
            Ltc(wo.z, roughness, out a, out b, out c, out d);
            Float detM = c * (a - b * d);
            Vector3f wh = new Vector3f(c * (wi.x - b * wi.z), (a - b * d) * wi.y, -c * (d * wi.x - a * wi.z));
            Float lensq = LengthSquared(wh);
            Float s = 0.5f * (1 + 1 / MathF.Sqrt(d * d + 1));
            return Sqr(detM) / Sqr(lensq) * Math.Max(wh.z, 0) / (Pi * s);
        }

        private Float Pdf(Vector3f wo, Vector3f wi)
        {
            Float pu = Pu(wo.z, roughness);
            return pu * Inv2Pi + (1 - pu) * CltcPdf(wo, wi);
        }

        private bool Sample(Vector3f wo, Float uc, Point2f u, out Vector3f wi, out Float pdf)
        {
            Float pu = Pu(wo.z, roughness);
            if (uc < pu)
            {
                Float sn = SafeSqrt(1 - Sqr(u[0])), phiU = 2 * Pi * u[1];
                wi = new Vector3f(sn * MathF.Cos(phiU), sn * MathF.Sin(phiU), u[0]);
                pdf = Pdf(wo, wi);
                return true;
            }
            Float a, b, c, d;
            Ltc(wo.z, roughness, out a, out b, out c, out d);
            Float rr = MathF.Sqrt(u[0]), phi = 2 * Pi * u[1];
            Float x = rr * MathF.Cos(phi), y = rr * MathF.Sin(phi);
            Float s = 0.5f * (1 + 1 / MathF.Sqrt(d * d + 1));
            x = -((1 - s) * SafeSqrt(1 - Sqr(y)) + s * x);
            Float whz = SafeSqrt(1 - Sqr(x) - Sqr(y));
            Vector3f t = new Vector3f(a * x + b * whz, c * y, d * x + whz);
            Vector3f bx, by;
            Basis(wo, out bx, out by);
            wi = Normalize(t.x * bx + t.y * by + new Vector3f(0, 0, t.z));
            pdf = Pdf(wo, wi);
            return !IsNaN(wi.x + wi.y + wi.z) && !IsNaN(pdf);
        }

        // EonDiffuseBxDF Private Members
        private SampledSpectrum R;
        private Float roughness;
    }

    // VmfDiffuseBxDF Definition
    // (not in pbrt-v4) VMF Diffuse: d'Eon and Weidlich, EGSR 2024 (the fits of the authors' reference shader). A Lambertian
    // microsurface whose normals follow a von Mises-Fisher distribution, with its multiple scattering. Sampled by the cosine,
    // as in the paper. Evaluated in double precision (the sigma series cancels in float).
    //
    // Checked against the paper, equation by equation: the roughness (2), the cross section (4) and (6), every C coefficient of the
    // three orders, f_m (9) and the albedo mapping agree. Where the code is not the paper's text, it is the authors' Shadertoy:
    //   - between alpha 0.25 and 0.9 the cross section is the spherical harmonic expansion the paper allows, not its fit (5);
    //   - the cosine terms of the azimuth carry a factor 2 that the paper's series omits (a simulated single scattering has
    //     the moments of the factor 2);
    //   - the base of C_a is 1.0075 + 1.16942 sqrt(1 - c), the paper prints 1 + 1.169 sqrt(1 - c) (no effect at c = 1);
    //   - the albedo mapping, optional in the paper, is always applied: the reflectance given is the colour seen.
    public sealed unsafe class VmfDiffuseBxDF : BxDF
    {
        // VmfDiffuseBxDF Public Methods
        public VmfDiffuseBxDF() { }
        public VmfDiffuseBxDF(SampledSpectrum R, Float roughness)
        {
            this.R = R;
            this.roughness = Clamp(roughness, 0, 0.9999f);
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi))
            {
                return new SampledSpectrum(0f);
            }
            if (AbsCosTheta(wi) == 0 || AbsCosTheta(wo) == 0)
            {
                return new SampledSpectrum(0f);
            }
            return Eval(wo, wi);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            {
                return default;
            }
            Vector3f wi = SampleCosineHemisphere(u);
            if (wo.z < 0)
            {
                wi.z *= -1;
            }
            Float pdf = CosineHemispherePDF(AbsCosTheta(wi));
            if (!(pdf > 0) || AbsCosTheta(wo) == 0)
            {
                return default;
            }
            return new BSDFSample(Eval(wo, wi), wi, pdf, BxDFFlags.DiffuseReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi))
            {
                return 0;
            }
            return CosineHemispherePDF(AbsCosTheta(wi));
        }

        public static string Name() { return "VmfDiffuseBxDF"; }

        public override void Regularize() { }

        public override BxDFFlags Flags()
        {
            return (bool)R ? BxDFFlags.DiffuseReflection : BxDFFlags.Unset;
        }

        public override string ToString()
        {
            return StringPrintf("[ VmfDiffuseBxDF R: %s roughness: %f ]", R, roughness);
        }

        // VmfDiffuseBxDF Private Methods
        private static double VmfSigma(double u, double m)
        {
            double m2 = m * m;
            double u2 = u * u;
            if (m < 0.25)
            {
                if (m == 0)
                {
                    return (u + Math.Abs(u)) * 0.5;
                }
                if (u == 1)
                {
                    return 1 - 0.5 * m2;
                }
                return ((Math.Exp(u2 / (m2 * (-1 + u2))) * m * Math.Sqrt(1 - u2)) / Math.Sqrt(Math.PI) +
                        u * (1 + pbrt.Erf(u / (m * Math.Sqrt(1 - u2))))) * 0.5 -
                       0.25 * m2 * (u + Math.Abs(u));
            }
            double m4 = m2 * m2, m8 = m4 * m4;
            double u4 = u2 * u2, u6 = u2 * u4, u8 = u4 * u4, u10 = u6 * u4,
                   u12 = u6 * u6;
            double c = 1 / Math.Tanh(2 / m2);
            double sh = Math.Sinh(2 / m2);
            double e2 = Math.Exp(-2 / m2);
            double s = 0.25 - 0.25 * u * (m2 - 2 * c) +
                       0.0390625 * (-1 + 3 * u2) * (4 + 3 * m4 - 6 * m2 * c);
            if (m > 0.9)
            {
                return s;
            }
            s += -0.000732421875 * (3 - 30 * u2 + 35 * u4) *
                     (16 + 180 * m4 + 105 * m8 - 10 * m2 * (8 + 21 * m4) * c) +
                 0.000049591064453125 * (-5 + 105 * u2 - 315 * u4 + 231 * u6) *
                     (64 + 105 * m4 * (32 + 180 * m4 + 99 * m8) -
                      42 * m2 * (16 + 240 * m4 + 495 * m8) * c) +
                 (1.0132789611816406e-6 * (35 - 1260 * u2 + 6930 * u4 - 12012 * u6 + 6435 * u8) *
                  (1 + c) *
                  (-256 - 315 * m4 * (128 + 33 * m4 * (80 + 364 * m4 + 195 * m8)) +
                   18 * m2 * (256 + 385 * m4 * (32 + 312 * m4 + 585 * m8)) * c) * sh) * e2 -
                 (9.12696123123169e-8 *
                  (-63 + 3465 * u2 - 30030 * u4 + 90090 * u6 - 109395 * u8 + 46189 * u10) *
                  (1 + c) *
                  (-1024 - 495 * m4 * (768 + 91 * m4 * (448 + 15 * m4 * (448 + 1836 * m4 + 969 * m8))) +
                   110 * m2 * (256 + 117 * m4 * (256 + 21 * m4 * (336 + 85 * m4 * (32 + 57 * m4)))) * c) *
                  sh) * e2 +
                 (4.3655745685100555e-9 *
                  (231 - 18018 * u2 + 225225 * u4 - 1.02102e6 * u6 + 2.078505e6 * u8 -
                   1.939938e6 * u10 + 676039 * u12) *
                  (1 + c) *
                  (-4096 - 3003 * m4 * (1024 + 45 * m4 * (2560 + 51 * m4 * (1792 + 285 * m4 * (80 + 308 * m4 + 161 * m8)))) +
                   78 * m2 * (2048 + 385 * m4 * (1280 + 153 * m4 * (512 + 57 * m4 * (192 + 35 * m4 * (40 + 69 * m4))))) * c) *
                  sh) * e2;
            return s;
        }
        private static double VmfFm(double ui, double uo, double r, double c)
        {
            double C = Math.Sqrt(Math.Max(0.0, 1 - c));
            double Ck = (1 - 0.5441615108674713 * C - 0.45302863761693374 * (1 - c)) /
                        (1 + 1.4293127703064865 * C);
            double Ca = c / Math.Pow(1.0075 + 1.16942 * C,
                                     Math.Atan((0.0225272 + (-0.264641 + r) * r) * pbrt.Erf(C)));
            double v = 0.384016 * (-0.341969 + Ca) * Ca * Ck *
                       (-0.0578978 / (0.287663 + ui * uo) + Math.Abs(-0.0898863 + Math.Tanh(r)));
            return Math.Max(0.0, v);
        }
        private static double VmfAlbedoMap(double kd, double r)
        {
            if (kd <= 0)
            {
                return 0;
            }
            double s = 0.64985 + 0.631112 * r + 1.38922 * r * r;
            double sr = Math.Sqrt(r);
            return (-1 + kd + Math.Sqrt(1 - 2 * kd + kd * kd + 4 * s * s * kd * kd)) / (2 * s * kd) * sr +
                   (1 - sr) * kd;
        }
        // ui, uo > 0; cphi = cos of the azimuth difference (1 = backscatter side). Returns f (no cosine).
        private static double VmfBrdf(double ui, double uo, double cphi, double r, double c)
        {
            double m = -Math.Log(1 - Math.Sqrt(r));
            double sigmai = VmfSigma(ui, m), sigmao = VmfSigma(uo, m), sigmano = VmfSigma(-uo, m);
            double sigio = sigmai * sigmao;
            double sigdenom = uo * sigmai + ui * sigmano;
            double r2 = r * r, r25 = r2 * Math.Sqrt(r), r3 = r * r2, r4 = r2 * r2,
                   r45 = r4 * Math.Sqrt(r), r5 = r3 * r2;
            double ui2 = ui * ui, uo2 = uo * uo;
            double sq = Math.Sqrt(Math.Max(0.0, (1 - ui2) * (1 - uo2)));
            double s = ui + uo;
            double C100 = 1 + (-0.1 * r + 0.84 * r4) / (1 + 9 * r3);
            double C101 = (0.0173 * r + 20.4 * r2 - 9.47 * r3) / (1 + 7.46 * r);
            double C102 = (-0.927 * r + 2.37 * r2) / (1.24 + r2);
            double C103 = (-0.11 * r - 1.54 * r2) / (1 - 1.05 * r + 7.1 * r2);
            double f10 = ((C100 + C101 * ui * uo + C102 * ui2 * uo2 + C103 * (ui2 + uo2)) * sigio) / sigdenom;
            double C110 = (0.54 * r - 0.182 * r3) / (1 + 1.32 * r2);
            double C111 = (-0.097 * r + 0.62 * r2 - 0.375 * r3) / (1 + 0.4 * r3);
            double C112 = 0.283 + 0.862 * r - 0.681 * r2;
            double f11 = (sq * (C110 + C111 * ui * uo)) * Math.Pow(sigio, C112) / sigdenom;
            double C120 = (2.25 * r + 5.1 * r2) / (1 + 9.8 * r + 32.4 * r2);
            double C121 = (-4.32 * r + 6 * r3) / (1 + 9.7 * r + 287 * r3);
            double f12 = ((1 - ui2) * (1 - uo2) * (C120 + C121 * uo) * (C120 + C121 * ui)) / s;
            double C200 = (0.00056 * r + 0.226 * r2) / (1 + 7.07 * r2);
            double C201 = (-0.268 * r + 4.57 * r2 - 12.04 * r3) / (1 + 36.7 * r3);
            double C202 = (0.418 * r + 2.52 * r2 - 0.97 * r3) / (1 + 10 * r2);
            double C203 = (0.068 * r - 2.25 * r2 + 2.65 * r3) / (1 + 21.4 * r3);
            double C204 = (0.05 * r - 4.22 * r3) / (1 + 17.6 * r2 + 43.1 * r3);
            double f20 = (C200 + C201 * ui * uo + C203 * ui2 * uo2 + C202 * s + C204 * (ui2 + uo2)) / s;
            double C210 = (-0.049 * r - 0.027 * r3) / (1 + 3.36 * r2);
            double C211 = (2.77 * r2 - 8.332 * r25 + 6.073 * r3) / (1 + 50 * r4);
            double C212 = (-0.431 * r2 - 0.295 * r3) / (1 + 23.9 * r3);
            double f21 = (sq * (C210 + C211 * ui * uo + C212 * s)) / s;
            double C300 = (-0.083 * r3 + 0.262 * r4) / (1 - 1.9 * r2 + 38.6 * r4);
            double C301 = (-0.627 * r2 + 4.95 * r25 - 2.44 * r3) / (1 + 31.5 * r4);
            double C302 = (0.33 * r2 + 0.31 * r25 + 1.4 * r3) / (1 + 20 * r3);
            double C303 = (-0.74 * r2 + 1.77 * r25 - 4.06 * r3) / (1 + 215 * r5);
            double C304 = (-1.026 * r3) / (1 + 5.81 * r2 + 13.2 * r3);
            double f30 = (C300 + C301 * ui * uo + C303 * ui2 * uo2 + C302 * s + C304 * (ui2 + uo2)) / s;
            double C310 = (0.028 * r2 - 0.0132 * r3) / (1 + 7.46 * r2 - 3.315 * r4);
            double C311 = (-0.134 * r2 + 0.162 * r25 + 0.302 * r3) / (1 + 57.5 * r45);
            double C312 = (-0.119 * r2 + 0.5 * r25 - 0.207 * r3) / (1 + 18.7 * r3);
            double f31 = (sq * (C310 + C311 * ui * uo + C312 * s)) / s;
            double c2phi = 2 * cphi * cphi - 1;
            return (1 / Math.PI) * (c * Math.Max(0.0, f10 + 2 * f11 * cphi + 2 * f12 * c2phi) +
                               c * c * Math.Max(0.0, f20 + 2 * f21 * cphi) +
                               c * c * c * Math.Max(0.0, f30 + 2 * f31 * cphi)) +
                   VmfFm(ui, uo, r, c);
        }

        private SampledSpectrum Eval(Vector3f wo, Vector3f wi)
        {
            double ui = AbsCosTheta(wo), uo = AbsCosTheta(wi);
            double sti = Math.Sqrt(Math.Max(0.0, 1.0 - ui * ui)), sto = Math.Sqrt(Math.Max(0.0, 1.0 - uo * uo));
            double cphi = 0;
            if (sti > 0 && sto > 0)
            {
                cphi = Clamp(((double)wo.x * wi.x + (double)wo.y * wi.y) / (sti * sto), -1.0, 1.0);
            }
            SampledSpectrum result = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                double c = VmfAlbedoMap(Clamp((double)R[i], 0.0, 1.0), roughness);
                result[i] = (Float)VmfBrdf(ui, uo, cphi, roughness, c);
            }
            return result;
        }

        // VmfDiffuseBxDF Private Members
        private SampledSpectrum R;
        private Float roughness;
    }

    // PolarisedFacet Definition
    // (not in pbrt-v4) The Mueller matrices of the microfacets, for the BxDFs whose light meets several of them (BxDF.Polarised).
    // A microfacet is a mirror or a smooth interface: its matrix is that of the Fresnel equations at its normal, and its
    // Stokes frames are those of the plane of that normal (Attenuation.Scattered). The matrices are not normalised: the
    // first element of a product is the reflectance of the chain for the polarisation that the light has when it reaches
    // each microfacet, which is not the product of the scalar reflectances.
    //
    // The directions are those of a walk that starts against the first argument of BxDF.Polarised: a points from the
    // microfacet to where the walk came from, b to where it goes, and `side` is -1 when the walk was mirrored to the upper
    // side of the surface. For a path from the camera (the radiance mode) the light goes the other way, from b to a.
    public static unsafe class PolarisedFacet
    {
        public static Attenuation Conductor(BSDF bsdf, Float side, Vector3f a, Vector3f b, Vector3f m, SampledSpectrum eta, SampledSpectrum k,
                                            bool fromCamera)
        {
            Float cosine = AbsDot(a, m);
            SampledSpectrum Fs = new SampledSpectrum(0f), Fp = new SampledSpectrum(0f), Ds = new SampledSpectrum(0f), Dp = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                Float fs, fp, ds, dp;
                FrComplexPolarised(cosine, new pstd.complex(eta[i], k[i]), out fs, out fp, out ds, out dp);
                Fs[i] = fs;
                Fp[i] = fp;
                Ds[i] = ds;
                Dp[i] = dp;
            }
            return Attenuation.Fresnel(new SampledSpectrum(1f), Fs, Fp, Ds, Dp)
                .Scattered(bsdf.LocalToRender(side * a), bsdf.LocalToRender(side * b), bsdf.LocalToRender(side * m), fromCamera);
        }

        // cosine is that of a with the normal m of the side of a, and eta the index of the other side over that of the side
        // of a. `scalar` is the reflectance (or the transmittance) of unpolarised light, the factor of the scalar BxDF.
        public static Attenuation Dielectric(BSDF bsdf, Float side, Vector3f a, Vector3f b, Vector3f m, Float cosine, Float eta, bool reflect,
                                             bool fromCamera, out Float scalar)
        {
            Float fs, fp, ds, dp;
            FrDielectricPolarised(cosine, eta, out fs, out fp, out ds, out dp);
            Float reflectance = 0.5f * (fs + fp);
            scalar = reflect ? reflectance : 1 - reflectance;
            Attenuation matrix = reflect ? Attenuation.Fresnel(new SampledSpectrum(1f), fs, fp, ds, dp)
                                         : Attenuation.Fresnel(new SampledSpectrum(1f), 1 - fs, 1 - fp, 0, 0);
            return matrix.Scattered(bsdf.LocalToRender(side * a), bsdf.LocalToRender(side * b), bsdf.LocalToRender(side * m), fromCamera);
        }

        // The matrix of the walk so far with one more microfacet. From the camera the light meets the new microfacet first.
        public static Attenuation Compose(Attenuation chain, bool hasChain, Attenuation next, bool fromCamera)
        {
            if (!hasChain)
            {
                return next;
            }
            return fromCamera ? chain * next : next * chain;
        }

        // weight sum / scalar: the sum of the matrices of the paths, over the sum of their scalar reflectances. False where the
        // BxDF is next to nothing (the far tail of a distribution): the ratio of two numbers that underflow is not a matrix.
        public static bool Normalise(Attenuation sum, SampledSpectrum scalar, SampledSpectrum weight, out Attenuation A)
        {
            A = default;
            SampledSpectrum scale = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                if (!(scalar[i] > 1e-20f))
                {
                    return false;
                }
                scale[i] = weight[i] / scalar[i];
            }
            A = sum * scale;
            return !A.HasNaNs();
        }
    }

    // MultiScatterConductorBxDF Definition
    // (not in pbrt-v4) A rough conductor with the multiple scattering of its microsurface: the random walk of Heitz, Hanika,
    // d'Eon and Dachsbacher, "Multiple-Scattering Microfacet BSDFs with the Smith Model" (SIGGRAPH 2016), as in the tutorial
    // code of the paper, on any MicrofacetDistribution (the walk asks it for its single sided functions only). The walk starts
    // against wo above the microsurface; at each microfacet it meets it reflects, and what it would send to wi is added (next
    // event estimation), until it leaves or has made maxOrder reflections. f is therefore an estimate, not a value: it is
    // unbiased, and the same for the same pair of directions. Sample_f follows one walk, and its weight is the product of
    // the Fresnel reflectances; its density is not known in closed form (pdfIsProportional), and PDF estimates it, as the
    // walk of a perfect mirror microsurface.
    public sealed unsafe class MultiScatterConductorBxDF : BxDF
    {
        // MultiScatterConductorBxDF Public Methods
        public MultiScatterConductorBxDF() { }
        public MultiScatterConductorBxDF(MicrofacetDistribution mfDistrib, MicrosurfaceHeight height, SampledSpectrum eta, SampledSpectrum k,
                                         int maxOrder)
        {
            this.mfDistrib = mfDistrib;
            this.height = height;
            this.eta = eta;
            this.k = k;
            this.maxOrder = maxOrder;
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.GlossyReflection;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi) || wo.z == 0 || wi.z == 0)
            {
                return new SampledSpectrum(0f);
            }
            // The other face of the surface is the same microsurface, mirrored
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
            }
            RNG rng = new RNG(Hash(wo), Hash(wi));
            SampledSpectrum sum;
            Float density;
            Walk(wo, wi, ref rng, out sum, out density);
            return sum / wi.z;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z == 0)
            {
                return default;
            }
            bool flip = wo.z < 0;
            Vector3f start = flip ? -wo : wo;
            RNG rng = new RNG(Hash(wo), Hash(uc, u));
            Vector3f wr = -start;
            Float h = MicrosurfaceWalk.StartHeight(height);
            SampledSpectrum energy = new SampledSpectrum(1f);
            for (int order = 0; order <= maxOrder; ++order)
            {
                h = MicrosurfaceWalk.SampleHeight(mfDistrib, height, wr, h, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left)
                {
                    if (order == 0 || !(wr.z > 0))
                    {
                        return default;
                    }
                    Vector3f wi = flip ? -wr : wr;
                    return new BSDFSample(energy / AbsCosTheta(wi), wi, 1, BxDFFlags.GlossyReflection, 1, true);
                }
                if (order == maxOrder)
                {
                    break;
                }
                Vector3f incoming = -wr;
                Vector3f wm = mfDistrib.Sample_wm_SingleSided(incoming, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                energy *= FrComplex(AbsDot(incoming, wm), eta, k);
                wr = Reflect(incoming, wm);
                if (IsNaN(wr.x + wr.y + wr.z) || IsNaN(h))
                {
                    return default;
                }
            }
            return default;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi) || wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
            }
            RNG rng = new RNG(Hash(wi, 0x5044464d53UL), Hash(wo));
            SampledSpectrum sum;
            Float density;
            Walk(wo, wi, ref rng, out sum, out density);
            // Strictly positive: the integrator divides by it
            return std.max<Float>(density, 1e-6f);
        }

        // The walk of f for this pair of directions, with the Mueller matrix of every microfacet kept: what each microfacet
        // would send to wi is the matrix of the microfacets before it times that of the reflection towards wi. The matrix
        // is an estimate, as f is; its first element over the scalar estimate of the same walk scales `weight`.
        public override bool Polarised(BSDF bsdf, Vector3f wo, Vector3f wi, SampledSpectrum weight, TransportMode mode, out Attenuation A)
        {
            A = default;
            if (!SameHemisphere(wo, wi) || wo.z == 0 || wi.z == 0)
            {
                return false;
            }
            Float side = 1;
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
                side = -1;
            }
            bool fromCamera = mode == TransportMode.Radiance;
            RNG rng = new RNG(Hash(wo), Hash(wi));
            SampledSpectrum scalar = new SampledSpectrum(0f);
            Attenuation sum = default, chain = default;
            bool hasSum = false, hasChain = false;
            Vector3f wr = -wo;
            Float h = MicrosurfaceWalk.StartHeight(height);
            SampledSpectrum energy = new SampledSpectrum(1f);
            for (int order = 0; order < maxOrder; ++order)
            {
                h = MicrosurfaceWalk.SampleHeight(mfDistrib, height, wr, h, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left)
                {
                    break;
                }
                Vector3f incoming = -wr;
                Vector3f wh = Normalize(incoming + wi);
                if (wh.z > 0)
                {
                    Float cosine = Dot(incoming, wh);
                    Float term = mfDistrib.PDF_SingleSided(incoming, wh) / (4 * cosine) * MicrosurfaceWalk.G1(mfDistrib, height, wi, h);
                    if (!IsNaN(term) && !IsInf(term) && term > 0)
                    {
                        scalar += energy * FrComplex(cosine, eta, k) * term;
                        Attenuation contribution = PolarisedFacet.Compose(chain, hasChain, PolarisedFacet.Conductor(bsdf, side, incoming, wi, wh, eta, k, fromCamera),
                                                                          fromCamera) * new SampledSpectrum(term);
                        if (hasSum)
                        {
                            sum += contribution;
                        }
                        else
                        {
                            sum = contribution;
                            hasSum = true;
                        }
                    }
                }
                Vector3f wm = mfDistrib.Sample_wm_SingleSided(incoming, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                energy *= FrComplex(AbsDot(incoming, wm), eta, k);
                wr = Reflect(incoming, wm);
                if (IsNaN(wr.x + wr.y + wr.z) || IsNaN(h))
                {
                    break;
                }
                chain = PolarisedFacet.Compose(chain, hasChain, PolarisedFacet.Conductor(bsdf, side, incoming, wr, wm, eta, k, fromCamera), fromCamera);
                hasChain = true;
            }
            if (!hasSum)
            {
                return false;
            }
            return PolarisedFacet.Normalise(sum, scalar, weight, out A);
        }

        public static string Name() { return "MultiScatterConductorBxDF"; }

        public override void Regularize()
        {
            mfDistrib.Regularize();
        }

        public override string ToString()
        {
            return StringPrintf("[ MultiScatterConductorBxDF mfDistrib: %s eta: %s k: %s maxOrder: %d ]", mfDistrib, eta, k, maxOrder);
        }

        // MultiScatterConductorBxDF Private Methods
        // One walk from wo (both directions above the surface). sum estimates f(wo, wi) cos(theta_i); density estimates the
        // density of wi among the directions in which the walk of a mirror microsurface leaves.
        private void Walk(Vector3f wo, Vector3f wi, ref RNG rng, out SampledSpectrum sum, out Float density)
        {
            sum = new SampledSpectrum(0f);
            density = 0;
            Vector3f wr = -wo;
            Float h = MicrosurfaceWalk.StartHeight(height);
            SampledSpectrum energy = new SampledSpectrum(1f);
            for (int order = 0; order < maxOrder; ++order)
            {
                h = MicrosurfaceWalk.SampleHeight(mfDistrib, height, wr, h, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left)
                {
                    return;
                }
                // Next event estimation: the reflection of this microfacet towards wi, and its way out
                Vector3f incoming = -wr;
                Vector3f wh = Normalize(incoming + wi);
                if (wh.z > 0)
                {
                    Float cosine = Dot(incoming, wh);
                    Float phase = mfDistrib.PDF_SingleSided(incoming, wh) / (4 * cosine);
                    Float term = phase * MicrosurfaceWalk.G1(mfDistrib, height, wi, h);
                    if (!IsNaN(term) && !IsInf(term) && term > 0)
                    {
                        density += term;
                        sum += energy * FrComplex(cosine, eta, k) * term;
                    }
                }
                // Next direction
                Vector3f wm = mfDistrib.Sample_wm_SingleSided(incoming, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                energy *= FrComplex(AbsDot(incoming, wm), eta, k);
                wr = Reflect(incoming, wm);
                if (IsNaN(wr.x + wr.y + wr.z) || IsNaN(h))
                {
                    return;
                }
            }
        }

        // MultiScatterConductorBxDF Private Members
        private MicrofacetDistribution mfDistrib;
        private MicrosurfaceHeight height;
        private SampledSpectrum eta, k;
        private int maxOrder;
    }

    // MultiScatterDiffuseBxDF Definition
    // (not in pbrt-v4) A rough diffuse surface: a microsurface of Lambertian microfacets with its multiple scattering, by the
    // random walk of Heitz et al. 2016 (the MicrosurfaceDiffuse of the tutorial code of the paper) on any
    // MicrofacetDistribution. As for MultiScatterConductorBxDF, f is an unbiased estimate, the same for the same pair of
    // directions, and the density of Sample_f is estimated by PDF, as the walk of a white microsurface.
    public sealed unsafe class MultiScatterDiffuseBxDF : BxDF
    {
        // MultiScatterDiffuseBxDF Public Methods
        public MultiScatterDiffuseBxDF() { }
        public MultiScatterDiffuseBxDF(MicrofacetDistribution mfDistrib, MicrosurfaceHeight height, SampledSpectrum R, int maxOrder)
        {
            this.mfDistrib = mfDistrib;
            this.height = height;
            this.R = R;
            this.maxOrder = maxOrder;
        }

        public override BxDFFlags Flags()
        {
            return (bool)R ? BxDFFlags.DiffuseReflection : BxDFFlags.Unset;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi) || wo.z == 0 || wi.z == 0)
            {
                return new SampledSpectrum(0f);
            }
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
            }
            RNG rng = new RNG(Hash(wo), Hash(wi));
            SampledSpectrum sum;
            Float density;
            Walk(wo, wi, ref rng, out sum, out density);
            return sum / wi.z;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z == 0)
            {
                return default;
            }
            bool flip = wo.z < 0;
            Vector3f start = flip ? -wo : wo;
            RNG rng = new RNG(Hash(wo), Hash(uc, u));
            Vector3f wr = -start;
            Float h = MicrosurfaceWalk.StartHeight(height);
            SampledSpectrum energy = new SampledSpectrum(1f);
            for (int order = 0; order <= maxOrder; ++order)
            {
                h = MicrosurfaceWalk.SampleHeight(mfDistrib, height, wr, h, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left)
                {
                    if (order == 0 || !(wr.z > 0))
                    {
                        return default;
                    }
                    Vector3f wi = flip ? -wr : wr;
                    return new BSDFSample(energy / AbsCosTheta(wi), wi, 1, BxDFFlags.DiffuseReflection, 1, true);
                }
                if (order == maxOrder)
                {
                    break;
                }
                Vector3f wm = mfDistrib.Sample_wm_SingleSided(-wr, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                energy *= R;
                wr = Frame.FromZ(wm).FromLocal(SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
                if (IsNaN(wr.x + wr.y + wr.z) || IsNaN(h))
                {
                    return default;
                }
            }
            return default;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi) || wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
            }
            RNG rng = new RNG(Hash(wi, 0x5044464d53UL), Hash(wo));
            SampledSpectrum sum;
            Float density;
            Walk(wo, wi, ref rng, out sum, out density);
            return std.max<Float>(density, 1e-6f);
        }

        public static string Name() { return "MultiScatterDiffuseBxDF"; }

        public override void Regularize() { }

        public override string ToString()
        {
            return StringPrintf("[ MultiScatterDiffuseBxDF mfDistrib: %s R: %s maxOrder: %d ]", mfDistrib, R, maxOrder);
        }

        // MultiScatterDiffuseBxDF Private Methods
        // One walk from wo (both directions above the surface). At each microfacet the Lambertian reflection towards wi is
        // taken with the normal that continues the walk.
        private void Walk(Vector3f wo, Vector3f wi, ref RNG rng, out SampledSpectrum sum, out Float density)
        {
            sum = new SampledSpectrum(0f);
            density = 0;
            Vector3f wr = -wo;
            Float h = MicrosurfaceWalk.StartHeight(height);
            SampledSpectrum energy = new SampledSpectrum(1f);
            for (int order = 0; order < maxOrder; ++order)
            {
                h = MicrosurfaceWalk.SampleHeight(mfDistrib, height, wr, h, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left)
                {
                    return;
                }
                Vector3f wm = mfDistrib.Sample_wm_SingleSided(-wr, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                energy *= R;
                Float term = InvPi * std.max<Float>(Dot(wi, wm), 0) * MicrosurfaceWalk.G1(mfDistrib, height, wi, h);
                if (!IsNaN(term) && !IsInf(term))
                {
                    density += term;
                    sum += energy * term;
                }
                wr = Frame.FromZ(wm).FromLocal(SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
                if (IsNaN(wr.x + wr.y + wr.z) || IsNaN(h))
                {
                    return;
                }
            }
        }

        // MultiScatterDiffuseBxDF Private Members
        private MicrofacetDistribution mfDistrib;
        private MicrosurfaceHeight height;
        private SampledSpectrum R;
        private int maxOrder;
    }

    // MultiScatterDielectricBxDF Definition
    // (not in pbrt-v4) A rough dielectric interface with the multiple scattering of its microsurface, by the random walk of
    // Heitz et al. 2016 (the MicrosurfaceDielectric of the tutorial code of the paper) on any MicrofacetDistribution. The walk
    // is above or below the microsurface, and each microfacet reflects or refracts it by its Fresnel reflectance; nothing is
    // absorbed. eta is the index below the surface over the index above, as for DielectricBxDF, and a pair of directions that
    // starts below is the mirrored pair of the surface of index 1 / eta. The walk carries power: the radiance transported
    // across the interface has the factor 1 / eta^2 of pbrt on top.
    public sealed unsafe class MultiScatterDielectricBxDF : BxDF
    {
        // MultiScatterDielectricBxDF Public Methods
        public MultiScatterDielectricBxDF() { }
        public MultiScatterDielectricBxDF(MicrofacetDistribution mfDistrib, MicrosurfaceHeight height, Float eta, int maxOrder)
        {
            this.mfDistrib = mfDistrib;
            this.height = height;
            this.eta = eta;
            this.maxOrder = maxOrder;
        }

        public override BxDFFlags Flags()
        {
            return eta == 1 ? BxDFFlags.GlossyTransmission : (BxDFFlags.GlossyReflection | BxDFFlags.GlossyTransmission);
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return new SampledSpectrum(0f);
            }
            Float etaAbove = eta;
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
                etaAbove = 1 / eta;
            }
            RNG rng = new RNG(Hash(wo), Hash(wi));
            Float value = Walk(wo, wi, etaAbove, ref rng) / AbsCosTheta(wi);
            if (wi.z < 0 && mode == TransportMode.Radiance)
            {
                value /= Sqr(etaAbove);
            }
            return new SampledSpectrum(value);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0)
            {
                return default;
            }
            bool flip = wo.z < 0;
            Vector3f start = flip ? -wo : wo;
            Float etaAbove = flip ? 1 / eta : eta;
            RNG rng = new RNG(Hash(wo), Hash(uc, u));
            Vector3f wr = -start;
            Float h = MicrosurfaceWalk.StartHeight(height);
            bool outside = true;
            for (int order = 0; order <= maxOrder; ++order)
            {
                h = NextHeight(wr, h, outside, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left || h == -MicrosurfaceWalk.Left)
                {
                    if (order == 0 || (outside ? !(wr.z > 0) : !(wr.z < 0)))
                    {
                        return default;
                    }
                    BxDFReflTransFlags wanted = outside ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission;
                    if ((sampleFlags & wanted) == 0)
                    {
                        return default;
                    }
                    Vector3f wi = flip ? -wr : wr;
                    Float weight = 1;
                    if (!outside && mode == TransportMode.Radiance)
                    {
                        weight /= Sqr(etaAbove);
                    }
                    return new BSDFSample(new SampledSpectrum(weight / AbsCosTheta(wi)), wi, 1,
                                          outside ? BxDFFlags.GlossyReflection : BxDFFlags.GlossyTransmission, outside ? 1 : etaAbove, true);
                }
                if (order == maxOrder)
                {
                    break;
                }
                Vector3f wm;
                bool reflected;
                wr = SamplePhase(-wr, etaAbove, ref outside, ref rng, out wm, out reflected);
                if (IsNaN(wr.x + wr.y + wr.z) || IsNaN(h))
                {
                    return default;
                }
            }
            return default;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            BxDFReflTransFlags wanted = SameHemisphere(wo, wi) ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission;
            if ((sampleFlags & wanted) == 0)
            {
                return 0;
            }
            Float etaAbove = eta;
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
                etaAbove = 1 / eta;
            }
            RNG rng = new RNG(Hash(wi, 0x5044464d53UL), Hash(wo));
            // Nothing is absorbed: the power the walk sends to wi is the density of wi
            return std.max<Float>(Walk(wo, wi, etaAbove, ref rng), 1e-6f);
        }

        public static string Name() { return "MultiScatterDielectricBxDF"; }

        public override void Regularize()
        {
            mfDistrib.Regularize();
        }

        public override string ToString()
        {
            return StringPrintf("[ MultiScatterDielectricBxDF mfDistrib: %s eta: %f maxOrder: %d ]", mfDistrib, eta, maxOrder);
        }

        // MultiScatterDielectricBxDF Private Methods
        // Below the microsurface the walk is the mirror image of a walk above
        private Float NextHeight(Vector3f wr, Float h, bool outside, Float u)
        {
            if (outside)
            {
                return MicrosurfaceWalk.SampleHeight(mfDistrib, height, wr, h, u);
            }
            return -MicrosurfaceWalk.SampleHeight(mfDistrib, height, -wr, -h, u);
        }

        // The power per solid angle that a microfacet met from the direction wi (pointing away from it), on the side
        // `outside`, sends to wo on the side `woOutside`. wh is the normal of the microfacet that does it and cosine that of
        // wi with it (for the Fresnel equations); zero when there is none.
        private Float EvalPhase(Vector3f wi, Vector3f wo, Float etaAbove, bool outside, bool woOutside, out Vector3f wh, out Float cosine)
        {
            Float etaLocal = outside ? etaAbove : 1 / etaAbove;
            Float side = outside ? 1 : -1;
            if (outside == woOutside)
            {
                // Reflection
                wh = Normalize(wi + wo);
                cosine = Dot(wi, wh);
                return 0.25f * mfDistrib.PDF_SingleSided(side * wi, side * wh) / cosine * FrDielectric(cosine, etaLocal);
            }
            else
            {
                // Transmission
                wh = -Normalize(wi + wo * etaLocal);
                if ((wh.z < 0) == outside)
                {
                    wh = -wh;
                }
                cosine = Dot(wi, wh);
                if (cosine < 0)
                {
                    return 0;
                }
                return Sqr(etaLocal) * (1 - FrDielectric(cosine, etaLocal)) * mfDistrib.PDF_SingleSided(side * wi, side * wh) *
                       std.max<Float>(0, -Dot(wo, wh)) / Sqr(cosine + etaLocal * Dot(wo, wh));
            }
        }

        // The direction in which a microfacet met from wi sends the walk, and the side it is then on. wm is the normal of
        // the microfacet, on the side of wi, and reflected tells what it did.
        private Vector3f SamplePhase(Vector3f wi, Float etaAbove, ref bool outside, ref RNG rng, out Vector3f wm, out bool reflected)
        {
            Float etaLocal = outside ? etaAbove : 1 / etaAbove;
            Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
            wm = outside ? mfDistrib.Sample_wm_SingleSided(wi, u) : -mfDistrib.Sample_wm_SingleSided(-wi, u);
            Float cosine = Dot(wi, wm);
            Float reflectance = FrDielectric(cosine, etaLocal);
            reflected = rng.Uniform<Float>() < reflectance;
            if (reflected)
            {
                return -wi + 2 * cosine * wm;
            }
            outside = !outside;
            Float cosTransmitted = -SafeSqrt(1 - (1 - cosine * cosine) / Sqr(etaLocal));
            return Normalize(wm * (cosine / etaLocal + cosTransmitted) - wi / etaLocal);
        }

        // The walk of f for this pair of directions with the Mueller matrix of every microfacet kept. A microfacet that the
        // walk reflected from, or went through, with the probability of the scalar reflectance (or transmittance) has its
        // matrix divided by that probability.
        public override bool Polarised(BSDF bsdf, Vector3f wo, Vector3f wi, SampledSpectrum weight, TransportMode mode, out Attenuation A)
        {
            A = default;
            if (wo.z == 0 || wi.z == 0)
            {
                return false;
            }
            Float etaAbove = eta;
            Float side = 1;
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
                etaAbove = 1 / eta;
                side = -1;
            }
            bool fromCamera = mode == TransportMode.Radiance;
            RNG rng = new RNG(Hash(wo), Hash(wi));
            Float scalar = 0;
            Attenuation sum = default, chain = default;
            bool hasSum = false, hasChain = false;
            Vector3f wr = -wo;
            Float h = MicrosurfaceWalk.StartHeight(height);
            bool outside = true;
            bool wiOutside = wi.z > 0;
            for (int order = 0; order < maxOrder; ++order)
            {
                h = NextHeight(wr, h, outside, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left || h == -MicrosurfaceWalk.Left)
                {
                    break;
                }
                Vector3f incoming = -wr;
                Float etaLocal = outside ? etaAbove : 1 / etaAbove;
                Float shadowing = wiOutside ? MicrosurfaceWalk.G1(mfDistrib, height, wi, h) : MicrosurfaceWalk.G1(mfDistrib, height, -wi, -h);
                Vector3f wh;
                Float cosine;
                Float term = EvalPhase(incoming, wi, etaAbove, outside, wiOutside, out wh, out cosine) * shadowing;
                if (!IsNaN(term) && !IsInf(term) && term > 0)
                {
                    Float probability;
                    Attenuation facet = PolarisedFacet.Dielectric(bsdf, side, incoming, wi, wh, cosine, etaLocal, outside == wiOutside, fromCamera,
                                                                  out probability);
                    if (probability > 0)
                    {
                        scalar += term;
                        Attenuation contribution = PolarisedFacet.Compose(chain, hasChain, facet, fromCamera) * new SampledSpectrum(term / probability);
                        if (hasSum)
                        {
                            sum += contribution;
                        }
                        else
                        {
                            sum = contribution;
                            hasSum = true;
                        }
                    }
                }
                Vector3f wm;
                bool reflected;
                wr = SamplePhase(incoming, etaAbove, ref outside, ref rng, out wm, out reflected);
                if (IsNaN(wr.x + wr.y + wr.z) || IsNaN(h))
                {
                    break;
                }
                Float chosen;
                Attenuation step = PolarisedFacet.Dielectric(bsdf, side, incoming, wr, wm, Dot(incoming, wm), etaLocal, reflected, fromCamera, out chosen);
                if (!(chosen > 0))
                {
                    break;
                }
                chain = PolarisedFacet.Compose(chain, hasChain, step * new SampledSpectrum(1 / chosen), fromCamera);
                hasChain = true;
            }
            if (!hasSum)
            {
                return false;
            }
            return PolarisedFacet.Normalise(sum, new SampledSpectrum(scalar), weight, out A);
        }

        // One walk from wo above the surface: the estimate of the power per solid angle sent to wi (f cos(theta_i), without
        // the factor of the radiance)
        private Float Walk(Vector3f wo, Vector3f wi, Float etaAbove, ref RNG rng)
        {
            Float sum = 0;
            Vector3f wr = -wo;
            Float h = MicrosurfaceWalk.StartHeight(height);
            bool outside = true;
            bool wiOutside = wi.z > 0;
            for (int order = 0; order < maxOrder; ++order)
            {
                h = NextHeight(wr, h, outside, rng.Uniform<Float>());
                if (h == MicrosurfaceWalk.Left || h == -MicrosurfaceWalk.Left)
                {
                    break;
                }
                Float shadowing = wiOutside ? MicrosurfaceWalk.G1(mfDistrib, height, wi, h) : MicrosurfaceWalk.G1(mfDistrib, height, -wi, -h);
                Vector3f wh, wm;
                Float cosine;
                bool reflected;
                Float term = EvalPhase(-wr, wi, etaAbove, outside, wiOutside, out wh, out cosine) * shadowing;
                if (!IsNaN(term) && !IsInf(term) && term > 0)
                {
                    sum += term;
                }
                wr = SamplePhase(-wr, etaAbove, ref outside, ref rng, out wm, out reflected);
                if (IsNaN(wr.x + wr.y + wr.z) || IsNaN(h))
                {
                    break;
                }
            }
            return sum;
        }

        // MultiScatterDielectricBxDF Private Members
        private MicrofacetDistribution mfDistrib;
        private MicrosurfaceHeight height;
        private Float eta;
        private int maxOrder;
    }

    // VCavityHit Definition
    public struct VCavityHit
    {
        // contribution to p(o) (solid angle density)
        public double w;
        // tilt of the facet normals
        public double alpha;
        public int first, n, axis;
    }

    // VCavityGrooves Definition
    // (not in pbrt-v4) What the V-cavity BxDFs share: the grooves that reflect one direction to another
    public static unsafe class VCavityGrooves
    {
        public const int kMaxHits = 512;

        public static double DAt(MicrofacetDistribution distrib, double alpha, double cphi, double sphi)
        {
            double sa = Math.Sin(alpha), ca = Math.Cos(alpha);
            return distrib.D(new Vector3f((Float)(sa * cphi), (Float)(sa * sphi), (Float)ca));
        }

        // Enumerates every (axis orientation, first facet, bounce count, groove tilt) that maps i to o.
        public static int Enumerate(MicrofacetDistribution distrib, int maxN, Vector3f i, Vector3f o, VCavityHit* hits, out double ellOut, double* thI)
        {
            ellOut = 0;
            double hx = (double)i.x + o.x, hy = (double)i.y + o.y;
            double L = Math.Sqrt(hx * hx + hy * hy);
            if (!(L > 1e-9))
            {
                return 0;
            }
            double tx = hx / L, ty = hy / L;
            double a_i = i.x * tx + i.y * ty, b_i = i.z, a_o = o.x * tx + o.y * ty, b_o = o.z;
            ellOut = Math.Sqrt(a_i * a_i + b_i * b_i);
            const double PI = VCavity.kPi;
            int nh = 0;
            VCavityOutcome* outcomes = stackalloc VCavityOutcome[VCavity.kMaxOutcomes];
            for (int axis = 0; axis < 2; ++axis)
            {
                double sg = axis == 0 ? 1.0 : -1.0;
                double thi = Math.Atan2(sg * a_i, b_i), tho = Math.Atan2(sg * a_o, b_o);
                thI[axis] = thi;
                // azimuth of the left-facet normal
                double cphi = sg * tx, sphi = sg * ty;
                for (int first = 0; first < 2; ++first)
                {
                    for (int n = 1; n <= maxN; ++n)
                    {
                        bool odd = (n & 1) != 0;
                        double x0;
                        if (first == 0)
                        {
                            x0 = odd ? tho + thi : thi + PI - tho;
                        }
                        else
                        {
                            x0 = odd ? -tho - thi : tho - thi - PI;
                        }
                        double alo = 1e-9;
                        if (n >= 2)
                        {
                            alo = Math.Max(alo, PI / 2 - (PI + 2 * Math.Abs(thi)) / (2.0 * (n - 1)));
                        }
                        int m0 = (int)Math.Ceiling((2 * n * alo - x0) / (2 * PI));
                        int m1 = (int)Math.Floor((n * PI - x0) / (2 * PI));
                        for (int m = m0; m <= m1; ++m)
                        {
                            double alpha = (x0 + 2 * PI * m) / (2.0 * n);
                            if (!(alpha > 1e-9 && alpha < PI / 2 - 1e-9))
                            {
                                continue;
                            }
                            int cnt = VCavity.Trace(thi, alpha, -alpha, maxN, outcomes);
                            for (int j = 0; j < cnt; ++j)
                            {
                                if (outcomes[j].first != first || outcomes[j].n != n || Math.Abs(VCavity.WrapPi(outcomes[j].thetaO - tho)) > 1e-6)
                                {
                                    continue;
                                }
                                double sa = Math.Sin(alpha), ca = Math.Cos(alpha);
                                double q = ca * DAt(distrib, alpha, cphi, sphi);
                                double w = q * outcomes[j].prob * sa / (2.0 * n * L);
                                if (w > 0 && nh < kMaxHits)
                                {
                                    hits[nh].w = w;
                                    hits[nh].alpha = alpha;
                                    hits[nh].first = first;
                                    hits[nh].n = n;
                                    hits[nh].axis = axis;
                                    ++nh;
                                }
                            }
                        }
                    }
                }
            }
            return nh;
        }
    }

    // VCavityConductorBxDF Definition
    // (not in pbrt-v4) A rough conductor made of symmetric V grooves, with every order of the reflections inside a groove in
    // closed form: Lee, Jarabo, Jeon, Gutierrez and Kim, "Practical Multiple Scattering for Rough Surfaces" (SIGGRAPH Asia
    // 2018), on any MicrofacetDistribution (the model asks it for D and for normals with the density D cos(theta)). The
    // groove whose left facet has the normal s is taken with the density D(s) (s . n). For a pair of directions, the grooves
    // whose axis is perpendicular to the half vector of the projections, and that send one direction to the other after n
    // reflections, are enumerated (VCavity.Trace gives the part of the beam that makes each number of reflections), and f is
    // their sum: a value, not an estimate. Sample_f picks a groove and one of its outcomes; its density is the same sum
    // without the Fresnel reflectances (PDF).
    public sealed unsafe class VCavityConductorBxDF : BxDF
    {
        // VCavityConductorBxDF Public Methods
        public VCavityConductorBxDF() { }
        public VCavityConductorBxDF(MicrofacetDistribution distrib, SampledSpectrum eta, SampledSpectrum k, int maxBounces)
        {
            this.distrib = distrib;
            this.eta = eta;
            this.k = k;
            maxN = Math.Max(1, Math.Min(maxBounces, 64));
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.GlossyReflection;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (wo.z * wi.z <= 0)
            {
                return new SampledSpectrum(0f);
            }
            bool flip = wo.z < 0;
            Vector3f i = flip ? -wo : wo, o = flip ? -wi : wi;
            VCavityHit* hits = stackalloc VCavityHit[VCavityGrooves.kMaxHits];
            double ell;
            double* thI = stackalloc double[2];
            int nh = VCavityGrooves.Enumerate(distrib, maxN, i, o, hits, out ell, thI);
            SampledSpectrum sum = new SampledSpectrum(0f);
            for (int h = 0; h < nh; ++h)
            {
                sum += (Float)hits[h].w * PathFresnel(thI[hits[h].axis], hits[h].alpha, hits[h].first, hits[h].n, ell);
            }
            return ClampZero(sum / o.z);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z == 0)
            {
                return default;
            }
            bool flip = wo.z < 0;
            Vector3f i = flip ? -wo : wo;
            // groove: left-facet normal s ~ |s.n| D(s) (visible normals of n == |s.n| D(s))
            Vector3f s = distrib.Sample_wm(new Vector3f(0, 0, 1), u);
            double sinA = Math.Sqrt((double)s.x * s.x + (double)s.y * s.y);
            if (!(sinA > 1e-9))
            {
                return default;
            }
            double alpha = Math.Atan2(sinA, s.z);
            double tx = s.x / sinA, ty = s.y / sinA;
            double a_i = i.x * tx + i.y * ty, b_i = i.z, i_u = -i.x * ty + i.y * tx;
            double ell = Math.Sqrt(a_i * a_i + b_i * b_i);
            double thI = Math.Atan2(a_i, b_i);
            VCavityOutcome* outcomes = stackalloc VCavityOutcome[VCavity.kMaxOutcomes];
            int cnt = VCavity.Trace(thI, alpha, -alpha, maxN, outcomes);
            double acc = 0, target = uc;
            int pick = -1;
            for (int j = 0; j < cnt; ++j)
            {
                acc += outcomes[j].prob;
                if (target < acc)
                {
                    pick = j;
                    break;
                }
            }
            if (pick < 0)
            {
                // beyond maxBounces: energy truncated
                return default;
            }
            SampledSpectrum F = PathFresnel(thI, alpha, outcomes[pick].first, outcomes[pick].n, ell);
            double st = Math.Sin(outcomes[pick].thetaO), ct = Math.Cos(outcomes[pick].thetaO);
            Vector3f wi = new Vector3f((Float)(ell * st * tx + i_u * ty), (Float)(ell * st * ty - i_u * tx), (Float)(ell * ct));
            if (flip)
            {
                wi = -wi;
            }
            if (wi.z == 0 || IsNaN(wi.x + wi.y + wi.z))
            {
                return default;
            }
            return new BSDFSample(F / AbsCosTheta(wi), wi, 1, BxDFFlags.GlossyReflection, 1, true);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z * wi.z <= 0)
            {
                return 0;
            }
            bool flip = wo.z < 0;
            Vector3f i = flip ? -wo : wo, o = flip ? -wi : wi;
            VCavityHit* hits = stackalloc VCavityHit[VCavityGrooves.kMaxHits];
            double ell;
            double* thI = stackalloc double[2];
            int nh = VCavityGrooves.Enumerate(distrib, maxN, i, o, hits, out ell, thI);
            double sum = 0;
            for (int h = 0; h < nh; ++h)
            {
                sum += hits[h].w;
            }
            return std.max<Float>((Float)sum, 1e-6f);
        }

        // The grooves that send one direction to the other, each with the matrices of its reflections in their order: the two
        // facets of a groove are known, so the matrix of a path is exact.
        public override bool Polarised(BSDF bsdf, Vector3f wo, Vector3f wi, SampledSpectrum weight, TransportMode mode, out Attenuation A)
        {
            A = default;
            if (wo.z * wi.z <= 0)
            {
                return false;
            }
            Float side = wo.z < 0 ? -1 : 1;
            Vector3f i = side * wo, o = side * wi;
            bool fromCamera = mode == TransportMode.Radiance;
            VCavityHit* hits = stackalloc VCavityHit[VCavityGrooves.kMaxHits];
            double ell;
            double* thI = stackalloc double[2];
            int nh = VCavityGrooves.Enumerate(distrib, maxN, i, o, hits, out ell, thI);
            double hx = (double)i.x + o.x, hy = (double)i.y + o.y;
            double L = Math.Sqrt(hx * hx + hy * hy);
            if (nh == 0 || !(L > 1e-9))
            {
                return false;
            }
            SampledSpectrum scalar = new SampledSpectrum(0f);
            Attenuation sum = default;
            bool hasSum = false;
            for (int h = 0; h < nh; ++h)
            {
                // The facets of the groove: the left one is tilted towards the azimuth of the groove
                double sg = hits[h].axis == 0 ? 1.0 : -1.0;
                double sa = Math.Sin(hits[h].alpha), ca = Math.Cos(hits[h].alpha);
                Vector3f left = new Vector3f((Float)(sa * sg * hx / L), (Float)(sa * sg * hy / L), (Float)ca);
                Vector3f right = new Vector3f(-left.x, -left.y, left.z);
                Attenuation chain = default;
                bool hasChain = false;
                SampledSpectrum reflectance = new SampledSpectrum(1f);
                Vector3f from = i;
                for (int j = 1; j <= hits[h].n; ++j)
                {
                    bool leftWall = (hits[h].first == 0) ? ((j & 1) != 0) : ((j & 1) == 0);
                    Vector3f m = leftWall ? left : right;
                    Vector3f to = Reflect(from, m);
                    reflectance *= FrComplex(AbsDot(from, m), eta, k);
                    chain = PolarisedFacet.Compose(chain, hasChain, PolarisedFacet.Conductor(bsdf, side, from, to, m, eta, k, fromCamera), fromCamera);
                    hasChain = true;
                    from = -to;
                }
                // The path ends along o, or it is not the path of this groove
                if (!hasChain || Length(-from - o) > 1e-3f)
                {
                    continue;
                }
                scalar += (Float)hits[h].w * reflectance;
                Attenuation contribution = chain * new SampledSpectrum((Float)hits[h].w);
                if (hasSum)
                {
                    sum += contribution;
                }
                else
                {
                    sum = contribution;
                    hasSum = true;
                }
            }
            if (!hasSum)
            {
                return false;
            }
            return PolarisedFacet.Normalise(sum, scalar, weight, out A);
        }

        public static string Name() { return "VCavityConductorBxDF"; }

        public override void Regularize()
        {
            distrib.Regularize();
        }

        public override string ToString()
        {
            return StringPrintf("[ VCavityConductorBxDF distrib: %s eta: %s k: %s maxBounces: %d ]", distrib, eta, k, maxN);
        }

        // VCavityConductorBxDF Private Methods
        // The product of the Fresnel reflectances of the reflections of a path
        private SampledSpectrum PathFresnel(double thI, double alpha, int first, int n, double ell)
        {
            double* c2 = stackalloc double[64];
            VCavity.PathCosines(thI, alpha, -alpha, first, n, c2);
            SampledSpectrum F = new SampledSpectrum(1f);
            for (int j = 0; j < n; ++j)
            {
                F *= FrComplex((Float)Math.Min(1.0, ell * c2[j]), eta, k);
            }
            return F;
        }

        // VCavityConductorBxDF Private Members
        private MicrofacetDistribution distrib;
        private SampledSpectrum eta, k;
        private int maxN = 16;
    }

    // DiffuseTransmissionBxDF Definition
    public sealed unsafe class DiffuseTransmissionBxDF : BxDF
    {
        // DiffuseTransmissionBxDF Public Methods
        public DiffuseTransmissionBxDF() { }
        public DiffuseTransmissionBxDF(SampledSpectrum R, SampledSpectrum T) { this.R = R; this.T = T; }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return SameHemisphere(wo, wi) ? (R * InvPi) : (T * InvPi);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            // Compute reflection and transmission probabilities for diffuse BSDF
            Float pr = R.MaxComponentValue(), pt = T.MaxComponentValue();
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                pr = 0;
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                pt = 0;
            if (pr == 0 && pt == 0)
                return default;

            // Randomly sample diffuse BSDF reflection or transmission
            if (uc < pr / (pr + pt))
            {
                // Sample diffuse BSDF reflection
                Vector3f wi = SampleCosineHemisphere(u);
                if (wo.z < 0)
                    wi.z *= -1;
                Float pdf = CosineHemispherePDF(AbsCosTheta(wi)) * pr / (pr + pt);
                return new BSDFSample(f(wo, wi, mode), wi, pdf, BxDFFlags.DiffuseReflection);
            }
            else
            {
                // Sample diffuse BSDF transmission
                Vector3f wi = SampleCosineHemisphere(u);
                if (wo.z > 0)
                    wi.z *= -1;
                Float pdf = CosineHemispherePDF(AbsCosTheta(wi)) * pt / (pr + pt);
                return new BSDFSample(f(wo, wi, mode), wi, pdf, BxDFFlags.DiffuseTransmission);
            }
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            // Compute reflection and transmission probabilities for diffuse BSDF
            Float pr = R.MaxComponentValue(), pt = T.MaxComponentValue();
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                pr = 0;
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                pt = 0;
            if (pr == 0 && pt == 0)
                return 0;

            if (SameHemisphere(wo, wi))
                return pr / (pr + pt) * CosineHemispherePDF(AbsCosTheta(wi));
            else
                return pt / (pr + pt) * CosineHemispherePDF(AbsCosTheta(wi));
        }

        public static string Name() { return "DiffuseTransmissionBxDF"; }

        public override void Regularize() { }

        public override BxDFFlags Flags()
        {
            return (((bool)R ? BxDFFlags.DiffuseReflection : BxDFFlags.Unset) |
                    ((bool)T ? BxDFFlags.DiffuseTransmission : BxDFFlags.Unset));
        }

        public override string ToString()
        {
            return StringPrintf("[ DiffuseTransmissionBxDF R: %s T: %s ]", R, T);
        }

        // DiffuseTransmissionBxDF Private Members
        private SampledSpectrum R, T;
    }

    // DielectricBxDF Definition
    public sealed unsafe class DielectricBxDF : BxDF
    {
        // DielectricBxDF Public Methods
        public DielectricBxDF() { }
        public DielectricBxDF(Float eta, MicrofacetDistribution mfDistrib)
        {
            this.eta = eta;
            this.mfDistrib = mfDistrib;
        }

        // (not in pbrt-v4) For the polarised integrator: the interface that scatters specularly
        public bool IsSpecular() { return eta == 1 || mfDistrib.EffectivelySmooth(); }

        public Float Eta() { return eta; }

        public override BxDFFlags Flags()
        {
            BxDFFlags flags = (eta == 1) ? BxDFFlags.Transmission : (BxDFFlags.Reflection | BxDFFlags.Transmission);
            return flags | (mfDistrib.EffectivelySmooth() ? BxDFFlags.Specular : BxDFFlags.Glossy);
        }

        public override void Regularize() { mfDistrib.Regularize(); }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            if (eta == 1 || mfDistrib.EffectivelySmooth())
            {
                // Sample perfect specular dielectric BSDF
                Float R = FrDielectric(CosTheta(wo), eta), T = 1 - R;
                // Compute probabilities _pr_ and _pt_ for sampling reflection and transmission
                Float pr = R, pt = T;
                if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                    pr = 0;
                if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                    pt = 0;
                if (pr == 0 && pt == 0)
                    return default;

                if (uc < pr / (pr + pt))
                {
                    // Sample perfect specular dielectric BRDF
                    Vector3f wi = new Vector3f(-wo.x, -wo.y, wo.z);
                    SampledSpectrum fr = new SampledSpectrum(R / AbsCosTheta(wi));
                    return new BSDFSample(fr, wi, pr / (pr + pt), BxDFFlags.SpecularReflection);
                }
                else
                {
                    // Sample perfect specular dielectric BTDF
                    // Compute ray direction for specular transmission
                    Vector3f wi;
                    Float etap;
                    bool valid = Refract(wo, new Normal3f(0, 0, 1), eta, &etap, &wi);
                    CHECK_RARE(1e-5f, !valid);
                    if (!valid)
                        return default;

                    SampledSpectrum ft = new SampledSpectrum(T / AbsCosTheta(wi));
                    // Account for non-symmetry with transmission to different medium
                    if (mode == TransportMode.Radiance)
                        ft /= Sqr(etap);

                    return new BSDFSample(ft, wi, pt / (pr + pt), BxDFFlags.SpecularTransmission, etap);
                }
            }
            else
            {
                // Sample rough dielectric BSDF
                Vector3f wm = mfDistrib.Sample_wm(wo, u);
                Float R = FrDielectric(Dot(wo, wm), eta);
                Float T = 1 - R;
                // Compute probabilities _pr_ and _pt_ for sampling reflection and transmission
                Float pr = R, pt = T;
                if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                    pr = 0;
                if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                    pt = 0;
                if (pr == 0 && pt == 0)
                    return default;

                Float pdf;
                if (uc < pr / (pr + pt))
                {
                    // Sample reflection at rough dielectric interface
                    Vector3f wi = Reflect(wo, wm);
                    if (!SameHemisphere(wo, wi))
                        return default;
                    // Compute PDF of rough dielectric reflection
                    pdf = mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm)) * pr / (pr + pt);

                    DCHECK(!IsNaN(pdf));
                    SampledSpectrum f = new SampledSpectrum(mfDistrib.D(wm) * mfDistrib.G(wo, wi) * R /
                                                            (4 * CosTheta(wi) * CosTheta(wo)));
                    return new BSDFSample(f, wi, pdf, BxDFFlags.GlossyReflection);
                }
                else
                {
                    // Sample transmission at rough dielectric interface
                    Float etap;
                    Vector3f wi;
                    bool tir = !Refract(wo, (Normal3f)wm, eta, &etap, &wi);
                    CHECK_RARE(1e-5f, tir);
                    if (SameHemisphere(wo, wi) || wi.z == 0 || tir)
                        return default;
                    // Compute PDF of rough dielectric transmission
                    Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etap);
                    Float dwm_dwi = AbsDot(wi, wm) / denom;
                    pdf = mfDistrib.PDF(wo, wm) * dwm_dwi * pt / (pr + pt);

                    CHECK(!IsNaN(pdf));
                    // Evaluate BRDF and return _BSDFSample_ for rough transmission
                    SampledSpectrum ft = new SampledSpectrum(T * mfDistrib.D(wm) * mfDistrib.G(wo, wi) *
                                                             std.abs(Dot(wi, wm) * Dot(wo, wm) /
                                                                     (CosTheta(wi) * CosTheta(wo) * denom)));
                    // Account for non-symmetry with transmission to different medium
                    if (mode == TransportMode.Radiance)
                        ft /= Sqr(etap);

                    return new BSDFSample(ft, wi, pdf, BxDFFlags.GlossyTransmission, etap);
                }
            }
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (eta == 1 || mfDistrib.EffectivelySmooth())
                return 0;
            // Evaluate sampling PDF of rough dielectric BSDF
            // Compute generalized half vector _wm_
            Float cosTheta_o = CosTheta(wo), cosTheta_i = CosTheta(wi);
            bool reflect = cosTheta_i * cosTheta_o > 0;
            float etap = 1;
            if (!reflect)
                etap = cosTheta_o > 0 ? eta : (1 / eta);
            Vector3f wm = wi * etap + wo;
            CHECK_RARE(1e-5f, LengthSquared(wm) == 0);
            if (cosTheta_i == 0 || cosTheta_o == 0 || LengthSquared(wm) == 0)
                return 0;
            wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));

            // Discard backfacing microfacets
            if (Dot(wm, wi) * cosTheta_i < 0 || Dot(wm, wo) * cosTheta_o < 0)
                return 0;

            // Determine Fresnel reflectance of rough dielectric boundary
            Float R = FrDielectric(Dot(wo, wm), eta);
            Float T = 1 - R;

            // Compute probabilities _pr_ and _pt_ for sampling reflection and transmission
            Float pr = R, pt = T;
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                pr = 0;
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                pt = 0;
            if (pr == 0 && pt == 0)
                return 0;

            // Return PDF for rough dielectric
            Float pdf;
            if (reflect)
            {
                // Compute PDF of rough dielectric reflection
                pdf = mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm)) * pr / (pr + pt);
            }
            else
            {
                // Compute PDF of rough dielectric transmission
                Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etap);
                Float dwm_dwi = AbsDot(wi, wm) / denom;
                pdf = mfDistrib.PDF(wo, wm) * dwm_dwi * pt / (pr + pt);
            }
            return pdf;
        }

        public static string Name() { return "DielectricBxDF"; }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (eta == 1 || mfDistrib.EffectivelySmooth())
                return new SampledSpectrum(0f);
            // Evaluate rough dielectric BSDF
            // Compute generalized half vector _wm_
            Float cosTheta_o = CosTheta(wo), cosTheta_i = CosTheta(wi);
            bool reflect = cosTheta_i * cosTheta_o > 0;
            float etap = 1;
            if (!reflect)
                etap = cosTheta_o > 0 ? eta : (1 / eta);
            Vector3f wm = wi * etap + wo;
            CHECK_RARE(1e-5f, LengthSquared(wm) == 0);
            if (cosTheta_i == 0 || cosTheta_o == 0 || LengthSquared(wm) == 0)
                return default;
            wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));

            // Discard backfacing microfacets
            if (Dot(wm, wi) * cosTheta_i < 0 || Dot(wm, wo) * cosTheta_o < 0)
                return default;

            Float F = FrDielectric(Dot(wo, wm), eta);
            if (reflect)
            {
                // Compute reflection at rough dielectric interface
                return new SampledSpectrum(mfDistrib.D(wm) * mfDistrib.G(wo, wi) * F / std.abs(4 * cosTheta_i * cosTheta_o));
            }
            else
            {
                // Compute transmission at rough dielectric interface
                Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etap) * cosTheta_i * cosTheta_o;
                Float ft = mfDistrib.D(wm) * (1 - F) * mfDistrib.G(wo, wi) * std.abs(Dot(wi, wm) * Dot(wo, wm) / denom);
                // Account for non-symmetry with transmission to different medium
                if (mode == TransportMode.Radiance)
                    ft /= Sqr(etap);

                return new SampledSpectrum(ft);
            }
        }

        public override string ToString()
        {
            return StringPrintf("[ DielectricBxDF eta: %f mfDistrib: %s ]", eta, mfDistrib.ToString());
        }

        // DielectricBxDF Private Members
        private Float eta;
        private MicrofacetDistribution mfDistrib;
    }

    // ThinDielectricBxDF Definition
    public sealed unsafe class ThinDielectricBxDF : BxDF
    {
        // ThinDielectricBxDF Public Methods
        public ThinDielectricBxDF() { }
        // (not in pbrt-v4) For the polarised integrator
        public Float Eta() { return eta; }

        public ThinDielectricBxDF(Float eta) { this.eta = eta; }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return new SampledSpectrum(0);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            Float R = FrDielectric(AbsCosTheta(wo), eta), T = 1 - R;
            // Compute _R_ and _T_ accounting for scattering between interfaces
            if (R < 1)
            {
                R += Sqr(T) * R / (1 - Sqr(R));
                T = 1 - R;
            }

            // Compute probabilities _pr_ and _pt_ for sampling reflection and transmission
            Float pr = R, pt = T;
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                pr = 0;
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                pt = 0;
            if (pr == 0 && pt == 0)
                return default;

            if (uc < pr / (pr + pt))
            {
                // Sample perfect specular dielectric BRDF
                Vector3f wi = new Vector3f(-wo.x, -wo.y, wo.z);
                SampledSpectrum fr = new SampledSpectrum(R / AbsCosTheta(wi));
                return new BSDFSample(fr, wi, pr / (pr + pt), BxDFFlags.SpecularReflection);
            }
            else
            {
                // Sample perfect specular transmission at thin dielectric interface
                Vector3f wi = -wo;
                SampledSpectrum ft = new SampledSpectrum(T / AbsCosTheta(wi));
                return new BSDFSample(ft, wi, pt / (pr + pt), BxDFFlags.SpecularTransmission);
            }
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return 0;
        }

        public static string Name() { return "ThinDielectricBxDF"; }

        public override void Regularize() { /* TODO */ }

        public override BxDFFlags Flags()
        {
            return (BxDFFlags.Reflection | BxDFFlags.Transmission | BxDFFlags.Specular);
        }

        public override string ToString()
        {
            return StringPrintf("[ ThinDielectricBxDF eta: %f ]", eta);
        }

        private Float eta;
    }

    // (not in pbrt-v4) ThinCoatBxDF Definition: the reflection of a thin clear coat. The light that goes through the coat is
    // not a BSDF event (see ThinCoatMaterial), so what is left is a perfect mirror whose throughput of one is the probability of
    // reflection that was already used to choose it.
    public sealed unsafe class ThinCoatBxDF : BxDF
    {
        // ThinCoatBxDF Public Methods
        public ThinCoatBxDF() { }
        public ThinCoatBxDF(Float eta) { this.eta = eta; }

        // (not in pbrt-v4) For the polarised integrators
        public Float Eta() { return eta; }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return new SampledSpectrum(0);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                return default;
            Vector3f wi = new Vector3f(-wo.x, -wo.y, wo.z);
            return new BSDFSample(new SampledSpectrum(1 / AbsCosTheta(wi)), wi, 1, BxDFFlags.SpecularReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return 0;
        }

        public static string Name() { return "ThinCoatBxDF"; }

        public override void Regularize() { }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.SpecularReflection;
        }

        public override string ToString()
        {
            return "[ ThinCoatBxDF ]";
        }

        private Float eta = 1.5f;
    }

    // (not in pbrt-v4) ThinFilmBxDF Definition: a smooth interface coated with thin films (an anti-reflection coating of a lens, the
    // interference colours of an oxide on a metal). The reflectance is that of the stack of layers (see FrThinFilmPolarised()):
    // it depends on the wavelength, on the angle and on the polarisation. The substrate is a conductor (the film only reflects) or
    // a dielectric (the light that is not reflected goes through, as at a DielectricBxDF; the layers are lossless).
    public sealed unsafe class ThinFilmBxDF : BxDF
    {
        // ThinFilmBxDF Public Methods
        public ThinFilmBxDF() { }
        public ThinFilmBxDF(SampledWavelengths lambda, Float[] layerEta, Float[] layerThickness, Float substrateEta,
                            SampledSpectrum conductorEta, SampledSpectrum conductorK, bool conductor)
        {
            this.wavelengths = new SampledSpectrum(0.0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                wavelengths[i] = lambda[i];
            }
            this.layerEta = layerEta; this.layerThickness = layerThickness; this.substrateEta = substrateEta;
            this.conductorEta = conductorEta; this.conductorK = conductorK; this.conductor = conductor;
        }

        public bool IsSpecular() { return true; }

        public Float SubstrateEta() { return substrateEta; }

        public bool IsConductor() { return conductor; }

        // The reflectance of the stack for light that arrives with the (signed) cosine cosTheta. False when the light cannot
        // arrive from that side (the substrate of a conductor is opaque)
        public bool Fresnel(Float cosTheta, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum Ds,
                            out SampledSpectrum Dp)
        {
            Fs = new SampledSpectrum(0.0f); Fp = new SampledSpectrum(0.0f);
            Ds = new SampledSpectrum(0.0f); Dp = new SampledSpectrum(0.0f);
            bool inside = cosTheta < 0;
            if (inside && conductor)
                return false;
            Float cosine = std.abs(cosTheta);
            Float[] eta = layerEta, thickness = layerThickness;
            if (inside)
            {
                // From the substrate: the layers in the other order, the ambient medium in the place of the substrate
                eta = (Float[])layerEta.Clone(); thickness = (Float[])layerThickness.Clone();
                Array.Reverse(eta); Array.Reverse(thickness);
            }
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                pstd.complex substrate = inside ? new pstd.complex(1) :
                                         conductor ? new pstd.complex(conductorEta[i], conductorK[i]) : new pstd.complex(substrateEta);
                pbrt.FrThinFilmPolarised(cosine, inside ? substrateEta : 1, eta, thickness, wavelengths[i], substrate,
                                         out Fs[i], out Fp[i], out Ds[i], out Dp[i]);
            }
            return true;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return new SampledSpectrum(0);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0 || !Fresnel(CosTheta(wo), out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _,
                                      out SampledSpectrum _))
                return default;
            SampledSpectrum R = (Fs + Fp) * 0.5f;
            Float pr = Clamp(R[0], 0, 1), pt = 1 - pr;
            if (conductor)
            {
                pt = 0;
            }
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                pr = 0;
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                pt = 0;
            if (pr == 0 && pt == 0)
                return default;
            if (uc < pr / (pr + pt))
            {
                Vector3f wi = new Vector3f(-wo.x, -wo.y, wo.z);
                return new BSDFSample(R / AbsCosTheta(wi), wi, pr / (pr + pt), BxDFFlags.SpecularReflection);
            }

            Vector3f wt;
            Float etap;
            if (!Refract(wo, new Normal3f(0, 0, 1), substrateEta, &etap, &wt))
                return default;
            SampledSpectrum ft = (new SampledSpectrum(1.0f) - R) / AbsCosTheta(wt);
            if (mode == TransportMode.Radiance)
                ft /= Sqr(etap);
            return new BSDFSample(ft, wt, pt / (pr + pt), BxDFFlags.SpecularTransmission, etap);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return 0;
        }

        public static string Name() { return "ThinFilmBxDF"; }

        public override void Regularize() { }

        public override BxDFFlags Flags()
        {
            return conductor ? BxDFFlags.SpecularReflection : BxDFFlags.SpecularReflection | BxDFFlags.SpecularTransmission;
        }

        public override string ToString()
        {
            return StringPrintf("[ ThinFilmBxDF layers: %d substrate: %s ]", layerEta.Length, conductor ? "conductor" : "dielectric");
        }

        private SampledSpectrum wavelengths, conductorEta, conductorK;
        private Float[] layerEta, layerThickness;
        private Float substrateEta = 1.5f;
        private bool conductor;
    }

    // ConductorBxDF Definition
    public sealed unsafe class ConductorBxDF : BxDF
    {
        // ConductorBxDF Public Methods
        public ConductorBxDF() { }
        public ConductorBxDF(MicrofacetDistribution mfDistrib, SampledSpectrum eta, SampledSpectrum k)
        {
            this.mfDistrib = mfDistrib;
            this.eta = eta;
            this.k = k;
        }

        // (not in pbrt-v4) For the polarised integrator: the interface that scatters specularly
        public bool IsSpecular() { return mfDistrib.EffectivelySmooth(); }

        public SampledSpectrum Eta() { return eta; }

        public SampledSpectrum K() { return k; }

        public override BxDFFlags Flags()
        {
            return mfDistrib.EffectivelySmooth() ? BxDFFlags.SpecularReflection : BxDFFlags.GlossyReflection;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                return default;
            if (mfDistrib.EffectivelySmooth())
            {
                // Sample perfect specular conductor BRDF
                Vector3f wi = new Vector3f(-wo.x, -wo.y, wo.z);
                SampledSpectrum f = FrComplex(AbsCosTheta(wi), eta, k) / AbsCosTheta(wi);
                return new BSDFSample(f, wi, 1, BxDFFlags.SpecularReflection);
            }
            // Sample rough conductor BRDF
            // Sample microfacet normal $\wm$ and reflected direction $\wi$
            if (wo.z == 0)
                return default;
            Vector3f wm = mfDistrib.Sample_wm(wo, u);
            Vector3f wi2 = Reflect(wo, wm);
            if (!SameHemisphere(wo, wi2))
                return default;

            // Compute PDF of _wi_ for microfacet reflection
            Float pdf = mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm));

            Float cosTheta_o = AbsCosTheta(wo), cosTheta_i = AbsCosTheta(wi2);
            if (cosTheta_i == 0 || cosTheta_o == 0)
                return default;
            // Evaluate Fresnel factor _F_ for conductor BRDF
            SampledSpectrum F = FrComplex(AbsDot(wo, wm), eta, k);

            SampledSpectrum f2 = mfDistrib.D(wm) * F * mfDistrib.G(wo, wi2) / (4 * cosTheta_i * cosTheta_o);
            return new BSDFSample(f2, wi2, pdf, BxDFFlags.GlossyReflection);
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi))
                return default;
            if (mfDistrib.EffectivelySmooth())
                return default;
            // Evaluate rough conductor BRDF
            // Compute cosines and $\wm$ for conductor BRDF
            Float cosTheta_o = AbsCosTheta(wo), cosTheta_i = AbsCosTheta(wi);
            if (cosTheta_i == 0 || cosTheta_o == 0)
                return default;
            Vector3f wm = wi + wo;
            if (LengthSquared(wm) == 0)
                return default;
            wm = Normalize(wm);

            // Evaluate Fresnel factor _F_ for conductor BRDF
            SampledSpectrum F = FrComplex(AbsDot(wo, wm), eta, k);

            return mfDistrib.D(wm) * F * mfDistrib.G(wo, wi) / (4 * cosTheta_i * cosTheta_o);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                return 0;
            if (!SameHemisphere(wo, wi))
                return 0;
            if (mfDistrib.EffectivelySmooth())
                return 0;
            // Evaluate sampling PDF of rough conductor BRDF
            Vector3f wm = wo + wi;
            CHECK_RARE(1e-5f, LengthSquared(wm) == 0);
            if (LengthSquared(wm) == 0)
                return 0;
            wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));
            return mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm));
        }

        public static string Name() { return "ConductorBxDF"; }

        public override void Regularize() { mfDistrib.Regularize(); }

        public override string ToString()
        {
            return StringPrintf("[ ConductorBxDF mfDistrib: %s eta: %s k: %s ]", mfDistrib, eta, k);
        }

        // ConductorBxDF Private Members
        private MicrofacetDistribution mfDistrib;
        private SampledSpectrum eta, k;
    }

    // LayeredBxDF Definition
    // (TopOrBottomBxDF<TopBxDF, BottomBxDF> is a BxDF reference to one of the two layers)
    public abstract unsafe class LayeredBxDF<TopBxDF, BottomBxDF> : BxDF
        where TopBxDF : BxDF, new()
        where BottomBxDF : BxDF, new()
    {
        // LayeredBxDF Public Methods
        protected LayeredBxDF(bool twoSided)
        {
            this.twoSided = twoSided;
            top = new TopBxDF();
            bottom = new BottomBxDF();
        }
        protected LayeredBxDF(TopBxDF top, BottomBxDF bottom, Float thickness, SampledSpectrum albedo, Float g, int maxDepth,
                              int nSamples, bool twoSided)
        {
            this.twoSided = twoSided;
            this.top = top;
            this.bottom = bottom;
            this.thickness = std.max(thickness, FloatMinNormal);
            this.g = g;
            this.albedo = albedo;
            this.maxDepth = maxDepth;
            this.nSamples = nSamples;
        }

        // std::numeric_limits<Float>::min(): smallest positive normal value
        private static readonly Float FloatMinNormal = (Float)1.17549435e-38f;

        public override void Regularize()
        {
            top.Regularize();
            bottom.Regularize();
        }

        public override BxDFFlags Flags()
        {
            BxDFFlags topFlags = top.Flags(), bottomFlags = bottom.Flags();
            CHECK(IsTransmissive(topFlags) || IsTransmissive(bottomFlags));  // otherwise, why bother?

            BxDFFlags flags = BxDFFlags.Reflection;
            if (IsSpecular(topFlags))
                flags = flags | BxDFFlags.Specular;

            if (IsDiffuse(topFlags) || IsDiffuse(bottomFlags) || (bool)albedo)
                flags = flags | BxDFFlags.Diffuse;
            else if (IsGlossy(topFlags) || IsGlossy(bottomFlags))
                flags = flags | BxDFFlags.Glossy;

            if (IsTransmissive(topFlags) && IsTransmissive(bottomFlags))
                flags = flags | BxDFFlags.Transmission;

            return flags;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            SampledSpectrum f = new SampledSpectrum(0f);
            // Estimate _LayeredBxDF_ value _f_ using random sampling
            // Set _wo_ and _wi_ for layered BSDF evaluation
            if (twoSided && wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
            }

            // Determine entrance interface for layered BSDF
            BxDF enterInterface;
            bool enteredTop = twoSided || wo.z > 0;
            if (enteredTop)
                enterInterface = top;
            else
                enterInterface = bottom;

            // Determine exit interface and exit $z$ for layered BSDF
            BxDF exitInterface, nonExitInterface;
            if (SameHemisphere(wo, wi) ^ enteredTop)
            {
                exitInterface = bottom;
                nonExitInterface = top;
            }
            else
            {
                exitInterface = top;
                nonExitInterface = bottom;
            }
            Float exitZ = (SameHemisphere(wo, wi) ^ enteredTop) ? 0 : thickness;

            // Account for reflection at the entrance interface
            if (SameHemisphere(wo, wi))
                f = nSamples * enterInterface.f(wo, wi, mode);

            // Declare _RNG_ for layered BSDF evaluation
            RNG rng = new RNG(Hash(GetOptions().seed, wo), Hash(wi));
            Float r() { return std.min<Float>(rng.Uniform<Float>(), OneMinusEpsilon); }

            for (int s = 0; s < nSamples; ++s)
            {
                // Sample random walk through layers to estimate BSDF value
                // Sample transmission direction through entrance interface
                Float uc = r();
                Point2f u0 = default;
                u0.x = r();
                u0.y = r();
                pstd.optional<BSDFSample> wos = enterInterface.Sample_f(wo, uc, u0, mode, BxDFReflTransFlags.Transmission);
                if (!wos || !(bool)wos.value().f || wos.value().pdf == 0 || wos.value().wi.z == 0)
                    continue;

                // Sample BSDF for virtual light from _wi_
                uc = r();
                Point2f u1 = default;
                u1.x = r();
                u1.y = r();
                pstd.optional<BSDFSample> wis = exitInterface.Sample_f(wi, uc, u1, !mode, BxDFReflTransFlags.Transmission);
                if (!wis || !(bool)wis.value().f || wis.value().pdf == 0 || wis.value().wi.z == 0)
                    continue;

                // Declare state for random walk through BSDF layers
                SampledSpectrum beta = wos.value().f * AbsCosTheta(wos.value().wi) / wos.value().pdf;
                Float z = enteredTop ? thickness : 0;
                Vector3f w = wos.value().wi;
                HGPhaseFunction phase = new HGPhaseFunction(g);

                for (int depth = 0; depth < maxDepth; ++depth)
                {
                    // Sample next event for layered BSDF evaluation random walk
                    // Possibly terminate layered BSDF random walk with Russian roulette
                    if (depth > 3 && beta.MaxComponentValue() < 0.25f)
                    {
                        Float q = std.max<Float>(0, 1 - beta.MaxComponentValue());
                        if (r() < q)
                            break;
                        beta /= 1 - q;
                    }

                    // Account for media between layers and possibly scatter
                    if (!(bool)albedo)
                    {
                        // Advance to next layer boundary and update _beta_ for transmittance
                        z = (z == thickness) ? 0 : thickness;
                        beta *= Tr(thickness, w);
                    }
                    else
                    {
                        // Sample medium scattering for layered BSDF evaluation
                        Float sigma_t = 1;
                        Float dz = SampleExponential(r(), sigma_t / std.abs(w.z));
                        Float zp = w.z > 0 ? (z + dz) : (z - dz);
                        DCHECK_RARE(1e-5, z == zp);
                        if (z == zp)
                            continue;
                        if (0 < zp && zp < thickness)
                        {
                            // Handle scattering event in layered BSDF medium
                            // Account for scattering through _exitInterface_ using _wis_
                            Float wt = 1;
                            if (!IsSpecular(exitInterface.Flags()))
                                wt = PowerHeuristic(1, wis.value().pdf, 1, phase.PDF(-w, -wis.value().wi));
                            f += beta * albedo * phase.p(-w, -wis.value().wi) * wt * Tr(zp - exitZ, wis.value().wi) *
                                 wis.value().f / wis.value().pdf;

                            // Sample phase function and update layered path state
                            Point2f u = default;
                            u.x = r();
                            u.y = r();
                            pstd.optional<PhaseFunctionSample> ps = phase.Sample_p(-w, u);
                            if (!ps || ps.value().pdf == 0 || ps.value().wi.z == 0)
                                continue;
                            beta *= albedo * ps.value().p / ps.value().pdf;
                            w = ps.value().wi;
                            z = zp;

                            // Possibly account for scattering through _exitInterface_
                            if (((z < exitZ && w.z > 0) || (z > exitZ && w.z < 0)) && !IsSpecular(exitInterface.Flags()))
                            {
                                // Account for scattering through _exitInterface_
                                SampledSpectrum fExit = exitInterface.f(-w, wi, mode);
                                if ((bool)fExit)
                                {
                                    Float exitPDF = exitInterface.PDF(-w, wi, mode, BxDFReflTransFlags.Transmission);
                                    Float wt2 = PowerHeuristic(1, ps.value().pdf, 1, exitPDF);
                                    f += beta * Tr(zp - exitZ, ps.value().wi) * fExit * wt2;
                                }
                            }

                            continue;
                        }
                        z = Clamp(zp, 0, thickness);
                    }

                    // Account for scattering at appropriate interface
                    if (z == exitZ)
                    {
                        // Account for reflection at _exitInterface_
                        Float uc2 = r();
                        Point2f u2 = default;
                        u2.x = r();
                        u2.y = r();
                        pstd.optional<BSDFSample> bs = exitInterface.Sample_f(-w, uc2, u2, mode, BxDFReflTransFlags.Reflection);
                        if (!bs || !(bool)bs.value().f || bs.value().pdf == 0 || bs.value().wi.z == 0)
                            break;
                        beta *= bs.value().f * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                        w = bs.value().wi;
                    }
                    else
                    {
                        // Account for scattering at _nonExitInterface_
                        if (!IsSpecular(nonExitInterface.Flags()))
                        {
                            // Add NEE contribution along presampled _wis_ direction
                            Float wt = 1;
                            if (!IsSpecular(exitInterface.Flags()))
                                wt = PowerHeuristic(1, wis.value().pdf, 1, nonExitInterface.PDF(-w, -wis.value().wi, mode));
                            f += beta * nonExitInterface.f(-w, -wis.value().wi, mode) * AbsCosTheta(wis.value().wi) * wt *
                                 Tr(thickness, wis.value().wi) * wis.value().f / wis.value().pdf;
                        }
                        // Sample new direction using BSDF at _nonExitInterface_
                        Float uc3 = r();
                        Point2f u3 = default;
                        u3.x = r();
                        u3.y = r();
                        pstd.optional<BSDFSample> bs = nonExitInterface.Sample_f(-w, uc3, u3, mode, BxDFReflTransFlags.Reflection);
                        if (!bs || !(bool)bs.value().f || bs.value().pdf == 0 || bs.value().wi.z == 0)
                            break;
                        beta *= bs.value().f * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                        w = bs.value().wi;

                        if (!IsSpecular(exitInterface.Flags()))
                        {
                            // Add NEE contribution along direction from BSDF sample
                            SampledSpectrum fExit = exitInterface.f(-w, wi, mode);
                            if ((bool)fExit)
                            {
                                Float wt = 1;
                                if (!IsSpecular(nonExitInterface.Flags()))
                                {
                                    Float exitPDF = exitInterface.PDF(-w, wi, mode, BxDFReflTransFlags.Transmission);
                                    wt = PowerHeuristic(1, bs.value().pdf, 1, exitPDF);
                                }
                                f += beta * Tr(thickness, bs.value().wi) * fExit * wt;
                            }
                        }
                    }
                }
            }

            return f / nSamples;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            CHECK(sampleFlags == BxDFReflTransFlags.All);  // for now
            // Set _wo_ for layered BSDF sampling
            bool flipWi = false;
            if (twoSided && wo.z < 0)
            {
                wo = -wo;
                flipWi = true;
            }

            // Sample BSDF at entrance interface to get initial direction _w_
            bool enteredTop = twoSided || wo.z > 0;
            pstd.optional<BSDFSample> bs0 = enteredTop ? top.Sample_f(wo, uc, u, mode) : bottom.Sample_f(wo, uc, u, mode);
            if (!bs0 || !(bool)bs0.value().f || bs0.value().pdf == 0 || bs0.value().wi.z == 0)
                return default;
            BSDFSample bs = bs0.value();
            if (bs.IsReflection())
            {
                if (flipWi)
                    bs.wi = -bs.wi;
                bs.pdfIsProportional = true;
                return bs;
            }
            Vector3f w = bs.wi;
            bool specularPath = bs.IsSpecular();

            // Declare _RNG_ for layered BSDF sampling
            RNG rng = new RNG(Hash(GetOptions().seed, wo), Hash(uc, u));
            Float r() { return std.min<Float>(rng.Uniform<Float>(), OneMinusEpsilon); }

            // Declare common variables for layered BSDF sampling
            SampledSpectrum f = bs.f * AbsCosTheta(bs.wi);
            Float pdf = bs.pdf;
            Float z = enteredTop ? thickness : 0;
            HGPhaseFunction phase = new HGPhaseFunction(g);

            for (int depth = 0; depth < maxDepth; ++depth)
            {
                // Follow random walk through layers to sample layered BSDF
                // Possibly terminate layered BSDF sampling with Russian Roulette
                Float rrBeta = f.MaxComponentValue() / pdf;
                if (depth > 3 && rrBeta < 0.25f)
                {
                    Float q = std.max<Float>(0, 1 - rrBeta);
                    if (r() < q)
                        return default;
                    pdf *= 1 - q;
                }
                if (w.z == 0)
                    return default;

                if ((bool)albedo)
                {
                    // Sample potential scattering event in layered medium
                    Float sigma_t = 1;
                    Float dz = SampleExponential(r(), sigma_t / AbsCosTheta(w));
                    Float zp = w.z > 0 ? (z + dz) : (z - dz);
                    CHECK_RARE(1e-5, zp == z);
                    if (zp == z)
                        return default;
                    if (0 < zp && zp < thickness)
                    {
                        // Update path state for valid scattering event between interfaces
                        Point2f up = default;
                        up.x = r();
                        up.y = r();
                        pstd.optional<PhaseFunctionSample> ps = phase.Sample_p(-w, up);
                        if (!ps || ps.value().pdf == 0 || ps.value().wi.z == 0)
                            return default;
                        f *= albedo * ps.value().p;
                        pdf *= ps.value().pdf;
                        specularPath = false;
                        w = ps.value().wi;
                        z = zp;

                        continue;
                    }
                    z = Clamp(zp, 0, thickness);
                    if (z == 0)
                        DCHECK_LT(w.z, 0);
                    else
                        DCHECK_GT(w.z, 0);
                }
                else
                {
                    // Advance to the other layer interface
                    z = (z == thickness) ? 0 : thickness;
                    f *= Tr(thickness, w);
                }
                // Initialize _interface_ for current interface surface
                BxDF iface;
                if (z == 0)
                    iface = bottom;
                else
                    iface = top;

                // Sample interface BSDF to determine new path direction
                Float uc2 = r();
                Point2f u2 = default;
                u2.x = r();
                u2.y = r();
                pstd.optional<BSDFSample> bs1 = iface.Sample_f(-w, uc2, u2, mode);
                if (!bs1 || !(bool)bs1.value().f || bs1.value().pdf == 0 || bs1.value().wi.z == 0)
                    return default;
                BSDFSample bsi = bs1.value();
                f *= bsi.f;
                pdf *= bsi.pdf;
                specularPath &= bsi.IsSpecular();
                w = bsi.wi;

                // Return _BSDFSample_ if path has left the layers
                if (bsi.IsTransmission())
                {
                    BxDFFlags flags = SameHemisphere(wo, w) ? BxDFFlags.Reflection : BxDFFlags.Transmission;
                    flags |= specularPath ? BxDFFlags.Specular : BxDFFlags.Glossy;
                    if (flipWi)
                        w = -w;
                    return new BSDFSample(f, w, pdf, flags, 1f, true);
                }

                // Scale _f_ by cosine term after scattering at the interface
                f *= AbsCosTheta(bsi.wi);
            }
            return default;
        }

        // LayeredBxDF Private Methods
        private static Float Tr(Float dz, Vector3f w)
        {
            if (std.abs(dz) <= FloatMinNormal)
                return 1;
            return FastExp(-std.abs(dz / w.z));
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            CHECK(sampleFlags == BxDFReflTransFlags.All);  // for now
            // Set _wo_ and _wi_ for layered BSDF evaluation
            if (twoSided && wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
            }

            // Declare _RNG_ for layered PDF evaluation
            RNG rng = new RNG(Hash(GetOptions().seed, wi), Hash(wo));
            Float r() { return std.min<Float>(rng.Uniform<Float>(), OneMinusEpsilon); }

            // Update _pdfSum_ for reflection at the entrance layer
            bool enteredTop = twoSided || wo.z > 0;
            Float pdfSum = 0;
            if (SameHemisphere(wo, wi))
            {
                BxDFReflTransFlags reflFlag = BxDFReflTransFlags.Reflection;
                pdfSum += enteredTop ? nSamples * top.PDF(wo, wi, mode, reflFlag) : nSamples * bottom.PDF(wo, wi, mode, reflFlag);
            }

            for (int s = 0; s < nSamples; ++s)
            {
                // Evaluate layered BSDF PDF sample
                if (SameHemisphere(wo, wi))
                {
                    // Evaluate TRT term for PDF estimate
                    BxDF rInterface, tInterface;
                    if (enteredTop)
                    {
                        rInterface = bottom;
                        tInterface = top;
                    }
                    else
                    {
                        rInterface = top;
                        tInterface = bottom;
                    }
                    // Sample _tInterface_ to get direction into the layers
                    BxDFReflTransFlags trans = BxDFReflTransFlags.Transmission;
                    pstd.optional<BSDFSample> wos, wis;
                    Float a0 = r();
                    Point2f ua = default;
                    ua.x = r();
                    ua.y = r();
                    wos = tInterface.Sample_f(wo, a0, ua, mode, trans);
                    Float a1 = r();
                    Point2f ub = default;
                    ub.x = r();
                    ub.y = r();
                    wis = tInterface.Sample_f(wi, a1, ub, !mode, trans);

                    // Update _pdfSum_ accounting for TRT scattering events
                    if (wos && (bool)wos.value().f && wos.value().pdf > 0 && wis && (bool)wis.value().f && wis.value().pdf > 0)
                    {
                        if (!IsNonSpecular(tInterface.Flags()))
                            pdfSum += rInterface.PDF(-wos.value().wi, -wis.value().wi, mode);
                        else
                        {
                            // Use multiple importance sampling to estimate PDF product
                            Float a2 = r();
                            Point2f uc = default;
                            uc.x = r();
                            uc.y = r();
                            pstd.optional<BSDFSample> rs = rInterface.Sample_f(-wos.value().wi, a2, uc, mode);
                            if (rs && (bool)rs.value().f && rs.value().pdf > 0)
                            {
                                if (!IsNonSpecular(rInterface.Flags()))
                                    pdfSum += tInterface.PDF(-rs.value().wi, wi, mode);
                                else
                                {
                                    // Compute MIS-weighted estimate of Equation
                                    // (\ref{eq:pdf-triple-canceled-one})
                                    Float rPDF = rInterface.PDF(-wos.value().wi, -wis.value().wi, mode);
                                    Float wt = PowerHeuristic(1, wis.value().pdf, 1, rPDF);
                                    pdfSum += wt * rPDF;

                                    Float tPDF = tInterface.PDF(-rs.value().wi, wi, mode);
                                    wt = PowerHeuristic(1, rs.value().pdf, 1, tPDF);
                                    pdfSum += wt * tPDF;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Evaluate TT term for PDF estimate
                    BxDF toInterface, tiInterface;
                    if (enteredTop)
                    {
                        toInterface = top;
                        tiInterface = bottom;
                    }
                    else
                    {
                        toInterface = bottom;
                        tiInterface = top;
                    }

                    Float uc = r();
                    Point2f u = default;
                    u.x = r();
                    u.y = r();
                    pstd.optional<BSDFSample> wos = toInterface.Sample_f(wo, uc, u, mode);
                    if (!wos || !(bool)wos.value().f || wos.value().pdf == 0 || wos.value().wi.z == 0 || wos.value().IsReflection())
                        continue;

                    uc = r();
                    u = default;
                    u.x = r();
                    u.y = r();
                    pstd.optional<BSDFSample> wis = tiInterface.Sample_f(wi, uc, u, !mode);
                    if (!wis || !(bool)wis.value().f || wis.value().pdf == 0 || wis.value().wi.z == 0 || wis.value().IsReflection())
                        continue;

                    if (IsSpecular(toInterface.Flags()))
                        pdfSum += tiInterface.PDF(-wos.value().wi, wi, mode);
                    else if (IsSpecular(tiInterface.Flags()))
                        pdfSum += toInterface.PDF(wo, -wis.value().wi, mode);
                    else
                        pdfSum += (toInterface.PDF(wo, -wis.value().wi, mode) + tiInterface.PDF(-wos.value().wi, wi, mode)) / 2;
                }
            }
            // Return mixture of PDF estimate and constant PDF
            return Lerp(0.9f, 1 / (4 * Pi), pdfSum / nSamples);
        }

        public override string ToString()
        {
            return StringPrintf("[ LayeredBxDF top: %s bottom: %s thickness: %f albedo: %s g: %f ]", top, bottom,
                                thickness, albedo, g);
        }

        // LayeredBxDF Private Members
        private readonly bool twoSided;
        protected TopBxDF top;
        protected BottomBxDF bottom;
        private Float thickness, g;
        private SampledSpectrum albedo;
        private int maxDepth, nSamples;
    }

    // CoatedDiffuseBxDF Definition
    public sealed unsafe class CoatedDiffuseBxDF : LayeredBxDF<DielectricBxDF, DiffuseBxDF>
    {
        // CoatedDiffuseBxDF Public Methods
        public CoatedDiffuseBxDF() : base(true) { }
        public CoatedDiffuseBxDF(DielectricBxDF top, DiffuseBxDF bottom, Float thickness, SampledSpectrum albedo, Float g,
                                 int maxDepth, int nSamples)
            : base(top, bottom, thickness, albedo, g, maxDepth, nSamples, true)
        { }
        public static string Name() { return "CoatedDiffuseBxDF"; }
    }

    // CoatedConductorBxDF Definition
    public sealed unsafe class CoatedConductorBxDF : LayeredBxDF<DielectricBxDF, ConductorBxDF>
    {
        // CoatedConductorBxDF Public Methods
        public CoatedConductorBxDF() : base(true) { }
        public CoatedConductorBxDF(DielectricBxDF top, ConductorBxDF bottom, Float thickness, SampledSpectrum albedo,
                                   Float g, int maxDepth, int nSamples)
            : base(top, bottom, thickness, albedo, g, maxDepth, nSamples, true)
        { }
        public static string Name() { return "CoatedConductorBxDF"; }
    }

    // HairBxDF Definition
    public sealed unsafe class HairBxDF : BxDF
    {

        public override void Regularize() { }

        public override BxDFFlags Flags() { return BxDFFlags.GlossyReflection; }

        // HairBxDF Private Methods
        private static Float Mp(Float cosTheta_i, Float cosTheta_o, Float sinTheta_i, Float sinTheta_o, Float v)
        {
            Float a = cosTheta_i * cosTheta_o / v, b = sinTheta_i * sinTheta_o / v;
            Float mp = (v <= .1f) ? (FastExp(LogI0(a) - b - 1 / v + 0.6931f + std.log(1 / (2 * v))))
                                  : (FastExp(-b) * I0(a)) / (std.sinh(1 / v) * 2 * v);
            DCHECK(!IsInf(mp) && !IsNaN(mp));
            return mp;
        }

        private static SampledSpectrum[] Ap(Float cosTheta_o, Float eta, Float h, SampledSpectrum T)
        {
            SampledSpectrum[] ap = new SampledSpectrum[pMax + 1];
            // Compute $p=0$ attenuation at initial cylinder intersection
            Float cosGamma_o = SafeSqrt(1 - Sqr(h));
            Float cosTheta = cosTheta_o * cosGamma_o;
            Float f = FrDielectric(cosTheta, eta);
            ap[0] = new SampledSpectrum(f);

            // Compute $p=1$ attenuation term
            ap[1] = Sqr(1 - f) * T;

            // Compute attenuation terms up to $p=_pMax_$
            for (int p = 2; p < pMax; ++p)
                ap[p] = ap[p - 1] * T * f;

            // Compute attenuation term accounting for remaining orders of scattering
            if ((bool)(1 - T * f))
                ap[pMax] = ap[pMax - 1] * f * T / (1 - T * f);

            return ap;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            // TODO? flags...

            // Compute hair coordinate system terms related to _wo_
            Float sinTheta_o = wo.x;
            Float cosTheta_o = SafeSqrt(1 - Sqr(sinTheta_o));
            Float phi_o = std.atan2(wo.z, wo.y);
            Float gamma_o = SafeASin(h);

            // Compute hair coordinate system terms related to _wi_
            Float sinTheta_i = wi.x;
            Float cosTheta_i = SafeSqrt(1 - Sqr(sinTheta_i));
            Float phi_i = std.atan2(wi.z, wi.y);

            // Compute $\gammat$ for refracted ray
            Float etap = SafeSqrt(eta * eta - Sqr(sinTheta_o)) / cosTheta_o;
            Float sinGamma_t = h / etap;
            Float gamma_t = SafeASin(sinGamma_t);

            // Compute PDF for $A_p$ terms
            Float[] apPDF = ApPDF(cosTheta_o);

            // Compute PDF sum for hair scattering events
            Float phi = phi_i - phi_o;
            Float pdf = 0;
            for (int p = 0; p < pMax; ++p)
            {
                // Compute $\sin\,\thetao$ and $\cos\,\thetao$ terms accounting for scales
                Float sinThetap_o, cosThetap_o;
                if (p == 0)
                {
                    sinThetap_o = sinTheta_o * cos2kAlpha[1] - cosTheta_o * sin2kAlpha[1];
                    cosThetap_o = cosTheta_o * cos2kAlpha[1] + sinTheta_o * sin2kAlpha[1];
                }
                // Handle remainder of $p$ values for hair scale tilt
                else if (p == 1)
                {
                    sinThetap_o = sinTheta_o * cos2kAlpha[0] + cosTheta_o * sin2kAlpha[0];
                    cosThetap_o = cosTheta_o * cos2kAlpha[0] - sinTheta_o * sin2kAlpha[0];
                }
                else if (p == 2)
                {
                    sinThetap_o = sinTheta_o * cos2kAlpha[2] + cosTheta_o * sin2kAlpha[2];
                    cosThetap_o = cosTheta_o * cos2kAlpha[2] - sinTheta_o * sin2kAlpha[2];
                }
                else
                {
                    sinThetap_o = sinTheta_o;
                    cosThetap_o = cosTheta_o;
                }

                // Handle out-of-range $\cos\,\thetao$ from scale adjustment
                cosThetap_o = std.abs(cosThetap_o);

                pdf += Mp(cosTheta_i, cosThetap_o, sinTheta_i, sinThetap_o, v[p]) * apPDF[p] * Np(phi, p, s, gamma_o, gamma_t);
            }
            pdf += Mp(cosTheta_i, cosTheta_o, sinTheta_i, sinTheta_o, v[pMax]) * apPDF[pMax] * (1 / (2 * Pi));
            return pdf;
        }

        private static Float Phi(int p, Float gamma_o, Float gamma_t)
        {
            return 2 * p * gamma_t - 2 * gamma_o + p * Pi;
        }

        public static string Name() { return "HairBxDF"; }

        private static Float Np(Float phi, int p, Float s, Float gamma_o, Float gamma_t)
        {
            Float dphi = phi - Phi(p, gamma_o, gamma_t);
            // Remap _dphi_ to $[-\pi,\pi]$
            while (dphi > Pi)
                dphi -= 2 * Pi;
            while (dphi < -Pi)
                dphi += 2 * Pi;

            return TrimmedLogistic(dphi, s, -Pi, Pi);
        }
        // HairBxDF Public Methods
        public HairBxDF() { }

        public HairBxDF(Float h, Float eta, SampledSpectrum sigma_a, Float beta_m, Float beta_n, Float alpha)
        {
            this.h = h;
            this.eta = eta;
            this.sigma_a = sigma_a;
            this.beta_m = beta_m;
            this.beta_n = beta_n;
            CHECK(h >= -1 && h <= 1);
            CHECK(beta_m >= 0 && beta_m <= 1);
            CHECK(beta_n >= 0 && beta_n <= 1);
            // _HairBxDF_ constructor implementation
            v[0] = Sqr(0.726f * beta_m + 0.812f * Sqr(beta_m) + 3.7f * Pow20(beta_m));
            v[1] = .25f * v[0];
            v[2] = 4 * v[0];
            for (int p = 3; p <= pMax; ++p)
                // TODO: is there anything better here?
                v[p] = v[2];

            Float SqrtPiOver8 = 0.626657069f;
            s = SqrtPiOver8 * (0.265f * beta_n + 1.194f * Sqr(beta_n) + 5.372f * Pow22(beta_n));
            DCHECK(!IsNaN(s));

            sin2kAlpha[0] = std.sin(Radians(alpha));
            cos2kAlpha[0] = SafeSqrt(1 - Sqr(sin2kAlpha[0]));
            for (int i = 1; i < pMax; ++i)
            {
                sin2kAlpha[i] = 2 * cos2kAlpha[i - 1] * sin2kAlpha[i - 1];
                cos2kAlpha[i] = Sqr(cos2kAlpha[i - 1]) - Sqr(sin2kAlpha[i - 1]);
            }
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            // Compute hair coordinate system terms related to _wo_
            Float sinTheta_o = wo.x;
            Float cosTheta_o = SafeSqrt(1 - Sqr(sinTheta_o));
            Float phi_o = std.atan2(wo.z, wo.y);
            Float gamma_o = SafeASin(h);

            // Compute hair coordinate system terms related to _wi_
            Float sinTheta_i = wi.x;
            Float cosTheta_i = SafeSqrt(1 - Sqr(sinTheta_i));
            Float phi_i = std.atan2(wi.z, wi.y);

            // Compute $\cos\,\thetat$ for refracted ray
            Float sinTheta_t = sinTheta_o / eta;
            Float cosTheta_t = SafeSqrt(1 - Sqr(sinTheta_t));

            // Compute $\gammat$ for refracted ray
            Float etap = SafeSqrt(Sqr(eta) - Sqr(sinTheta_o)) / cosTheta_o;
            Float sinGamma_t = h / etap;
            Float cosGamma_t = SafeSqrt(1 - Sqr(sinGamma_t));
            Float gamma_t = SafeASin(sinGamma_t);

            // Compute the transmittance _T_ of a single path through the cylinder
            SampledSpectrum T = Exp(-sigma_a * (2 * cosGamma_t / cosTheta_t));

            // Evaluate hair BSDF
            Float phi = phi_i - phi_o;
            SampledSpectrum[] ap = Ap(cosTheta_o, eta, h, T);
            SampledSpectrum fsum = new SampledSpectrum(0f);

            for (int p = 0; p < pMax; ++p)
            {
                // Compute $\sin\,\thetao$ and $\cos\,\thetao$ terms accounting for scales
                Float sinThetap_o, cosThetap_o;
                if (p == 0)
                {
                    sinThetap_o = sinTheta_o * cos2kAlpha[1] - cosTheta_o * sin2kAlpha[1];
                    cosThetap_o = cosTheta_o * cos2kAlpha[1] + sinTheta_o * sin2kAlpha[1];
                }
                // Handle remainder of $p$ values for hair scale tilt
                else if (p == 1)
                {
                    sinThetap_o = sinTheta_o * cos2kAlpha[0] + cosTheta_o * sin2kAlpha[0];
                    cosThetap_o = cosTheta_o * cos2kAlpha[0] - sinTheta_o * sin2kAlpha[0];
                }
                else if (p == 2)
                {
                    sinThetap_o = sinTheta_o * cos2kAlpha[2] + cosTheta_o * sin2kAlpha[2];
                    cosThetap_o = cosTheta_o * cos2kAlpha[2] - sinTheta_o * sin2kAlpha[2];
                }
                else
                {
                    sinThetap_o = sinTheta_o;
                    cosThetap_o = cosTheta_o;
                }

                // Handle out-of-range $\cos\,\thetao$ from scale adjustment
                cosThetap_o = std.abs(cosThetap_o);

                fsum += Mp(cosTheta_i, cosThetap_o, sinTheta_i, sinThetap_o, v[p]) * ap[p] * Np(phi, p, s, gamma_o, gamma_t);
            }
            // Compute contribution of remaining terms after _pMax_
            fsum += Mp(cosTheta_i, cosTheta_o, sinTheta_i, sinTheta_o, v[pMax]) * ap[pMax] / (2 * Pi);

            if (AbsCosTheta(wi) > 0)
                fsum /= AbsCosTheta(wi);
            DCHECK(!IsInf(fsum.Average()) && !IsNaN(fsum.Average()));
            return fsum;
        }

        // HairBxDF Constants
        private const int pMax = 3;

        private Float[] ApPDF(Float cosTheta_o)
        {
            // Initialize array of $A_p$ values for _cosTheta_o_
            Float sinTheta_o = SafeSqrt(1 - Sqr(cosTheta_o));
            // Compute $\cos\,\thetat$ for refracted ray
            Float sinTheta_t = sinTheta_o / eta;
            Float cosTheta_t = SafeSqrt(1 - Sqr(sinTheta_t));

            // Compute $\gammat$ for refracted ray
            Float etap = SafeSqrt(Sqr(eta) - Sqr(sinTheta_o)) / cosTheta_o;
            Float sinGamma_t = h / etap;
            Float cosGamma_t = SafeSqrt(1 - Sqr(sinGamma_t));
            Float gamma_t = SafeASin(sinGamma_t);

            // Compute the transmittance _T_ of a single path through the cylinder
            SampledSpectrum T = Exp(-sigma_a * (2 * cosGamma_t / cosTheta_t));

            SampledSpectrum[] ap = Ap(cosTheta_o, eta, h, T);

            // Compute $A_p$ PDF from individual $A_p$ terms
            Float[] apPDF = new Float[pMax + 1];
            Float sumY = 0;
            foreach (SampledSpectrum asp in ap)
                sumY += asp.Average();
            for (int i = 0; i <= pMax; ++i)
                apPDF[i] = ap[i].Average() / sumY;

            return apPDF;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            // Compute hair coordinate system terms related to _wo_
            Float sinTheta_o = wo.x;
            Float cosTheta_o = SafeSqrt(1 - Sqr(sinTheta_o));
            Float phi_o = std.atan2(wo.z, wo.y);
            Float gamma_o = SafeASin(h);

            // Determine which term $p$ to sample for hair scattering
            Float[] apPDF = ApPDF(cosTheta_o);
            int p = SampleDiscrete(new pstd.span<Float>(apPDF), uc, null, &uc);

            // Compute $\sin\,\thetao$ and $\cos\,\thetao$ terms accounting for scales
            Float sinThetap_o, cosThetap_o;
            if (p == 0)
            {
                sinThetap_o = sinTheta_o * cos2kAlpha[1] - cosTheta_o * sin2kAlpha[1];
                cosThetap_o = cosTheta_o * cos2kAlpha[1] + sinTheta_o * sin2kAlpha[1];
            }
            // Handle remainder of $p$ values for hair scale tilt
            else if (p == 1)
            {
                sinThetap_o = sinTheta_o * cos2kAlpha[0] + cosTheta_o * sin2kAlpha[0];
                cosThetap_o = cosTheta_o * cos2kAlpha[0] - sinTheta_o * sin2kAlpha[0];
            }
            else if (p == 2)
            {
                sinThetap_o = sinTheta_o * cos2kAlpha[2] + cosTheta_o * sin2kAlpha[2];
                cosThetap_o = cosTheta_o * cos2kAlpha[2] - sinTheta_o * sin2kAlpha[2];
            }
            else
            {
                sinThetap_o = sinTheta_o;
                cosThetap_o = cosTheta_o;
            }

            // Handle out-of-range $\cos\,\thetao$ from scale adjustment
            cosThetap_o = std.abs(cosThetap_o);

            // Sample $M_p$ to compute $\thetai$
            Float cosTheta = 1 + v[p] * std.log(std.max<Float>(u[0], 1e-5f) + (1 - u[0]) * FastExp(-2 / v[p]));
            Float sinTheta = SafeSqrt(1 - Sqr(cosTheta));
            Float cosPhi = std.cos(2 * Pi * u[1]);
            Float sinTheta_i = -cosTheta * sinThetap_o + sinTheta * cosPhi * cosThetap_o;
            Float cosTheta_i = SafeSqrt(1 - Sqr(sinTheta_i));

            // Sample $N_p$ to compute $\Delta\phi$
            // Compute $\gammat$ for refracted ray
            Float etap = SafeSqrt(Sqr(eta) - Sqr(sinTheta_o)) / cosTheta_o;
            Float sinGamma_t = h / etap;
            Float cosGamma_t = SafeSqrt(1 - Sqr(sinGamma_t));
            Float gamma_t = SafeASin(sinGamma_t);

            Float dphi;
            if (p < pMax)
                dphi = Phi(p, gamma_o, gamma_t) + SampleTrimmedLogistic(uc, s, -Pi, Pi);
            else
                dphi = 2 * Pi * uc;

            // Compute _wi_ from sampled hair scattering angles
            Float phi_i = phi_o + dphi;
            Vector3f wi = new Vector3f(sinTheta_i, cosTheta_i * std.cos(phi_i), cosTheta_i * std.sin(phi_i));

            // Compute PDF for sampled hair scattering direction _wi_
            Float pdf = 0;
            for (int pp = 0; pp < pMax; ++pp)
            {
                // Compute $\sin\,\thetao$ and $\cos\,\thetao$ terms accounting for scales
                Float sinThetap_o2, cosThetap_o2;
                if (pp == 0)
                {
                    sinThetap_o2 = sinTheta_o * cos2kAlpha[1] - cosTheta_o * sin2kAlpha[1];
                    cosThetap_o2 = cosTheta_o * cos2kAlpha[1] + sinTheta_o * sin2kAlpha[1];
                }
                // Handle remainder of $p$ values for hair scale tilt
                else if (pp == 1)
                {
                    sinThetap_o2 = sinTheta_o * cos2kAlpha[0] + cosTheta_o * sin2kAlpha[0];
                    cosThetap_o2 = cosTheta_o * cos2kAlpha[0] - sinTheta_o * sin2kAlpha[0];
                }
                else if (pp == 2)
                {
                    sinThetap_o2 = sinTheta_o * cos2kAlpha[2] + cosTheta_o * sin2kAlpha[2];
                    cosThetap_o2 = cosTheta_o * cos2kAlpha[2] - sinTheta_o * sin2kAlpha[2];
                }
                else
                {
                    sinThetap_o2 = sinTheta_o;
                    cosThetap_o2 = cosTheta_o;
                }

                // Handle out-of-range $\cos\,\thetao$ from scale adjustment
                cosThetap_o2 = std.abs(cosThetap_o2);

                // Handle out-of-range $\cos\,\thetao$ from scale adjustment
                cosThetap_o2 = std.abs(cosThetap_o2);

                pdf += Mp(cosTheta_i, cosThetap_o2, sinTheta_i, sinThetap_o2, v[pp]) * apPDF[pp] * Np(dphi, pp, s, gamma_o, gamma_t);
            }
            pdf += Mp(cosTheta_i, cosTheta_o, sinTheta_i, sinTheta_o, v[pMax]) * apPDF[pMax] * (1 / (2 * Pi));

            return new BSDFSample(f(wo, wi, mode), wi, pdf, Flags());
        }

        public static RGBUnboundedSpectrum SigmaAFromConcentration(Float ce, Float cp)
        {
            RGB eumelaninSigma_a = new RGB(0.419f, 0.697f, 1.37f);
            RGB pheomelaninSigma_a = new RGB(0.187f, 0.4f, 1.05f);
            RGB sigma_a = ce * eumelaninSigma_a + cp * pheomelaninSigma_a;
            return new RGBUnboundedSpectrum(RGBColorSpace.sRGB, sigma_a);
        }

        public static SampledSpectrum SigmaAFromReflectance(SampledSpectrum c, Float beta_n, SampledWavelengths lambda)
        {
            SampledSpectrum sigma_a = default;
            for (int i = 0; i < NSpectrumSamples; ++i)
                sigma_a[i] = Sqr(std.log(c[i]) / (5.969f - 0.215f * beta_n + 2.532f * Sqr(beta_n) - 10.73f * Pow3(beta_n) +
                                                  5.574f * Pow4(beta_n) + 0.245f * Pow5(beta_n)));
            return sigma_a;
        }

        public override string ToString()
        {
            return StringPrintf("[ HairBxDF h: %f eta: %f beta_m: %f beta_n: %f v[0]: %f s: %f sigma_a: %s ]", h, eta,
                                beta_m, beta_n, v[0], s, sigma_a);
        }

        // HairBxDF Private Members
        private Float h, eta;
        private SampledSpectrum sigma_a;
        private Float beta_m, beta_n;
        private Float[] v = new Float[pMax + 1];
        private Float s;
        private Float[] sin2kAlpha = new Float[pMax], cos2kAlpha = new Float[pMax];
    }

    // Tensor file I/O
    internal unsafe class Tensor
    {
        // Data type of the tensor's fields
        public enum Type
        {
            /* Invalid/unspecified */
            Invalid = 0,

            /* Signed and unsigned integer values */
            UInt8,
            Int8,
            UInt16,
            Int16,
            UInt32,
            Int32,
            UInt64,
            Int64,

            /* Floating point values */
            Float16,
            Float32,
            Float64,
        }

        public class Field
        {
            // Data type of the tensor's fields
            public Type dtype;

            // Offset in the file
            public size_t offset;

            /// Specifies both rank and size along each dimension
            public List<size_t> shape;

            /// Pointer to the start of the tensor
            public byte[] data;
        }

        public static string TypeToString(Type value)
        {
            switch (value)
            {
                case Type.Invalid: return "invalid";
                case Type.UInt8: return "uint8_t";
                case Type.Int8: return "int8_t";
                case Type.UInt16: return "uint16_t";
                case Type.Int16: return "int16_t";
                case Type.UInt32: return "uint32_t";
                case Type.Int32: return "int8_t";
                case Type.UInt64: return "uint64_t";
                case Type.Int64: return "int64_t";
                case Type.Float16: return "float16_t";
                case Type.Float32: return "float32_t";
                case Type.Float64: return "float64_t";
                default: return "unknown";
            }
        }

        private static size_t type_size(Type value)
        {
            switch (value)
            {
                case Type.Invalid: return 0;
                case Type.UInt8: return 1;
                case Type.Int8: return 1;
                case Type.UInt16: return 2;
                case Type.Int16: return 2;
                case Type.UInt32: return 4;
                case Type.Int32: return 4;
                case Type.UInt64: return 8;
                case Type.Int64: return 8;
                case Type.Float16: return 2;
                case Type.Float32: return 4;
                case Type.Float64: return 8;
                default: return 0;
            }
        }

        /// Load a tensor file into memory
        public Tensor(string filename)
        {
            m_filename = filename;

            byte[] bytes;
            try
            {
                bytes = ReadFileBytes(filename);
            }
            catch (Exception)
            {
                ErrorExit($"{filename}: unable to open file");
                return;
            }
            m_size = (size_t)bytes.Length;
            if (m_size < 12 + 2 + 4)
                ErrorExit($"{filename}: Tensor: Invalid tensor file: too small, truncated?");

            // (the SAFE_READ/ASSERT macros: every read is range-checked)
            long pos = 0;
            Func<long, bool> can = (long n) => pos + n <= bytes.Length;

            byte[] header = new byte[12];
            if (!can(12)) ErrorExit($"{filename}: Tensor: Unable to read header.");
            Array.Copy(bytes, pos, header, 0, 12);
            pos += 12;
            byte[] version = new byte[2];
            if (!can(2)) ErrorExit($"{filename}: Tensor: Unable to read version.");
            version[0] = bytes[pos]; version[1] = bytes[pos + 1];
            pos += 2;
            if (!can(4)) ErrorExit($"{filename}: Tensor: Unable to read &n_fields.");
            uint32_t n_fields = BitConverter.ToUInt32(bytes, (int)pos);
            pos += 4;

            byte[] expected = new byte[12];
            byte[] tf = System.Text.Encoding.ASCII.GetBytes("tensor_file");
            Array.Copy(tf, expected, tf.Length);
            for (int i = 0; i < 12; ++i)
                if (header[i] != expected[i])
                    ErrorExit($"{filename}: Tensor: Invalid tensor file: invalid header.");
            if (!(version[0] == 1 && version[1] == 0))
                ErrorExit($"{filename}: Tensor: Invalid tensor file: unknown file version.");

            for (uint32_t i = 0; i < n_fields; ++i)
            {
                if (!can(2)) ErrorExit($"{filename}: Tensor: Unable to read &name_length.");
                uint16_t name_length = BitConverter.ToUInt16(bytes, (int)pos);
                pos += 2;
                if (!can(name_length)) ErrorExit($"{filename}: Tensor: Unable to read (char *)name.data().");
                string name = System.Text.Encoding.Latin1.GetString(bytes, (int)pos, name_length);
                pos += name_length;
                if (!can(2)) ErrorExit($"{filename}: Tensor: Unable to read &ndim.");
                uint16_t ndim = BitConverter.ToUInt16(bytes, (int)pos);
                pos += 2;
                if (!can(1)) ErrorExit($"{filename}: Tensor: Unable to read &dtype.");
                byte dtype = bytes[pos];
                pos += 1;
                if (!can(8)) ErrorExit($"{filename}: Tensor: Unable to read &offset.");
                uint64_t offset = BitConverter.ToUInt64(bytes, (int)pos);
                pos += 8;
                if (!(dtype != (byte)Type.Invalid && dtype <= (byte)Type.Float64))
                    ErrorExit($"{filename}: Tensor: Invalid tensor file: unknown type.");

                List<size_t> shape = new List<size_t>((int)ndim);
                size_t total_size = type_size((Type)dtype);  // no need to check here, line 43
                                                              // already removes invalid types
                for (size_t j = 0; j < (size_t)ndim; ++j)
                {
                    if (!can(8)) ErrorExit($"{filename}: Tensor: Unable to read &size_value.");
                    uint64_t size_value = BitConverter.ToUInt64(bytes, (int)pos);
                    pos += 8;
                    shape.Add((size_t)size_value);
                    total_size *= shape[(int)j];
                }

                byte[] data = new byte[total_size];

                long cur_pos = pos;
                if (offset + total_size > (size_t)bytes.Length)
                    ErrorExit($"{filename}: Tensor: Unable to read data.get().");
                Array.Copy(bytes, (long)offset, data, 0, (long)total_size);
                pos = cur_pos;

                m_fields[name] = new Field { dtype = (Type)dtype, offset = (size_t)offset, shape = shape, data = data };
            }
        }

        /// Does the file contain a field of the specified name?
        public bool has_field(string name)
        {
            return m_fields.ContainsKey(name);
        }

        /// Return a data structure with information about the specified field
        public Field field(string name)
        {
            Field f;
            bool found = m_fields.TryGetValue(name, out f);
            CHECK(found);
            return f;
        }

        /// Return a human-readable summary
        public override string ToString()
        {
            System.Text.StringBuilder oss = new System.Text.StringBuilder();
            oss.Append("Tensor[\n")
               .Append("  filename = \"" + m_filename + "\",\n")
               .Append("  size = " + size() + ",\n")
               .Append("  fields = {\n");

            size_t ctr = 0;
            foreach (KeyValuePair<string, Field> it in m_fields)
            {
                oss.Append("    \"" + it.Key + "\"")
                   .Append(" => [\n")
                   .Append("      dtype = " + TypeToString(it.Value.dtype) + ",\n")
                   .Append("      offset = " + it.Value.offset + ",\n")
                   .Append("      shape = [");
                List<size_t> shape = it.Value.shape;
                for (int j = 0; j < shape.Count; ++j)
                {
                    oss.Append(shape[j]);
                    if (j + 1 < shape.Count)
                        oss.Append(", ");
                }

                oss.Append("]\n");

                oss.Append("    ]");
                if (++ctr < (size_t)m_fields.Count)
                    oss.Append(",");
                oss.Append("\n");
            }

            oss.Append("  }\n").Append("]");

            return oss.ToString();
        }

        /// Return the total size of the tensor's data
        public size_t size() { return m_size; }

        public string filename() { return m_filename; }

        private Dictionary<string, Field> m_fields = new Dictionary<string, Field>();
        private string m_filename;
        private size_t m_size;
    }

    // MeasuredBxDFData Definition
    public unsafe class MeasuredBxDFData
    {
        // MeasuredBxDFData Public Members
        public float[] wavelengths;
        public PiecewiseLinear2D spectra;   // PiecewiseLinear2D<3>
        public PiecewiseLinear2D ndf;       // PiecewiseLinear2D<0>
        public PiecewiseLinear2D vndf;      // PiecewiseLinear2D<2>
        public PiecewiseLinear2D sigma;     // PiecewiseLinear2D<0>
        public bool isotropic;
        public PiecewiseLinear2D luminance; // PiecewiseLinear2D<2>

        public MeasuredBxDFData(Allocator alloc)
        {
            ndf = new PiecewiseLinear2D(0, alloc);
            sigma = new PiecewiseLinear2D(0, alloc);
            vndf = new PiecewiseLinear2D(2, alloc);
            luminance = new PiecewiseLinear2D(2, alloc);
            spectra = new PiecewiseLinear2D(3, alloc);
            wavelengths = new float[0];
        }

        // STAT_MEMORY_COUNTER("Memory/Measured BRDF data", measuredBRDFBytes);
        [ThreadStatic] private static long measuredBRDFBytes;
        private static StatRegisterer STATS_REGmeasuredBRDFBytes = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/Measured BRDF data", measuredBRDFBytes);
            measuredBRDFBytes = 0;
        });

        private static float[] AsFloats(byte[] data)
        {
            float[] r = new float[data.Length / 4];
            Buffer.BlockCopy(data, 0, r, 0, r.Length * 4);
            return r;
        }

        public static MeasuredBxDFData Create(string filename, Allocator alloc)
        {
            Tensor tf = new Tensor(filename);
            Tensor.Field theta_i = tf.field("theta_i");
            Tensor.Field phi_i = tf.field("phi_i");
            Tensor.Field ndf = tf.field("ndf");
            Tensor.Field sigma = tf.field("sigma");
            Tensor.Field vndf = tf.field("vndf");
            Tensor.Field spectra = tf.field("spectra");
            Tensor.Field luminance = tf.field("luminance");
            Tensor.Field wavelengths = tf.field("wavelengths");
            Tensor.Field description = tf.field("description");
            Tensor.Field jacobian = tf.field("jacobian");

            if (!(description.shape.Count == 1 && description.dtype == Tensor.Type.UInt8 &&

                  theta_i.shape.Count == 1 && theta_i.dtype == Tensor.Type.Float32 &&

                  phi_i.shape.Count == 1 && phi_i.dtype == Tensor.Type.Float32 &&

                  wavelengths.shape.Count == 1 && wavelengths.dtype == Tensor.Type.Float32 &&

                  ndf.shape.Count == 2 && ndf.dtype == Tensor.Type.Float32 &&

                  sigma.shape.Count == 2 && sigma.dtype == Tensor.Type.Float32 &&

                  vndf.shape.Count == 4 && vndf.dtype == Tensor.Type.Float32 &&
                  vndf.shape[0] == phi_i.shape[0] && vndf.shape[1] == theta_i.shape[0] &&

                  luminance.shape.Count == 4 && luminance.dtype == Tensor.Type.Float32 &&
                  luminance.shape[0] == phi_i.shape[0] &&
                  luminance.shape[1] == theta_i.shape[0] &&
                  luminance.shape[2] == luminance.shape[3] &&

                  spectra.dtype == Tensor.Type.Float32 && spectra.shape.Count == 5 &&
                  spectra.shape[0] == phi_i.shape[0] && spectra.shape[1] == theta_i.shape[0] &&
                  spectra.shape[2] == wavelengths.shape[0] &&
                  spectra.shape[3] == spectra.shape[4] &&

                  luminance.shape[2] == spectra.shape[3] &&
                  luminance.shape[3] == spectra.shape[4] &&

                  jacobian.shape.Count == 1 && jacobian.shape[0] == 1 &&
                  jacobian.dtype == Tensor.Type.UInt8))
            {
                Error($"{filename}: invalid BRDF file structure: {tf}");
                return null;
            }

            MeasuredBxDFData brdf = new MeasuredBxDFData(alloc);
            brdf.filename = filename;
            brdf.isotropic = phi_i.shape[0] <= 2;

            float[] phi_i_data = AsFloats(phi_i.data);
            float[] theta_i_data = AsFloats(theta_i.data);
            float[] wavelengths_data = AsFloats(wavelengths.data);

            if (!brdf.isotropic)
            {
                int reduction = (int)std.rint((2 * Pi) / (phi_i_data[phi_i.shape[0] - 1] - phi_i_data[0]));
                if (reduction != 1)
                    ErrorExit($"{filename}: reduction {reduction} (!= 1) not supported");
            }

            /* Construct NDF interpolant data structure */
            brdf.ndf = new PiecewiseLinear2D(0, alloc, AsFloats(ndf.data), (int)ndf.shape[1], (int)ndf.shape[0], null, null,
                                             false, false);

            /* Construct projected surface area interpolant data structure */
            brdf.sigma = new PiecewiseLinear2D(0, alloc, AsFloats(sigma.data), (int)sigma.shape[1], (int)sigma.shape[0],
                                               null, null, false, false);

            /* Construct VNDF warp data structure */
            brdf.vndf = new PiecewiseLinear2D(2, alloc, AsFloats(vndf.data), (int)vndf.shape[3], (int)vndf.shape[2],
                                              new int[] { (int)phi_i.shape[0], (int)theta_i.shape[0] },
                                              new float[][] { phi_i_data, theta_i_data });

            /* Construct Luminance warp data structure */
            brdf.luminance = new PiecewiseLinear2D(2, alloc, AsFloats(luminance.data), (int)luminance.shape[3],
                                                   (int)luminance.shape[2],
                                                   new int[] { (int)phi_i.shape[0], (int)theta_i.shape[0] },
                                                   new float[][] { phi_i_data, theta_i_data });

            /* Copy wavelength information */
            size_t size = wavelengths.shape[0];
            brdf.wavelengths = new float[size];
            for (size_t i = 0; i < size; ++i)
                brdf.wavelengths[i] = wavelengths_data[i];

            /* Construct spectral interpolant */
            brdf.spectra = new PiecewiseLinear2D(3, alloc, AsFloats(spectra.data), (int)spectra.shape[4], (int)spectra.shape[3],
                                                 new int[] { (int)phi_i.shape[0], (int)theta_i.shape[0], (int)wavelengths.shape[0] },
                                                 new float[][] { phi_i_data, theta_i_data, wavelengths_data }, false, false);

            measuredBRDFBytes += 64 + 4 * brdf.wavelengths.Length + (long)brdf.ndf.BytesUsed() + (long)brdf.sigma.BytesUsed() +
                                 (long)brdf.vndf.BytesUsed() + (long)brdf.luminance.BytesUsed() + (long)brdf.spectra.BytesUsed();

            return brdf;
        }

        public override string ToString()
        {
            return StringPrintf("[ MeasuredBxDFData filename: %s ]", filename);
        }

        public string filename;
    }

    // MeasuredBxDF Definition
    public sealed unsafe class MeasuredBxDF : BxDF
    {
        // MeasuredBxDF Public Methods
        public MeasuredBxDF() { }
        public MeasuredBxDF(MeasuredBxDFData brdf, SampledWavelengths lambda)
        {
            this.brdf = brdf;
            this.lambda = lambda;
        }

        private static readonly Dictionary<string, MeasuredBxDFData> loadedData = new Dictionary<string, MeasuredBxDFData>();

        public override void Regularize() { }

        public override BxDFFlags Flags() { return (BxDFFlags.Reflection | BxDFFlags.Glossy); }

        // MeasuredBxDF Private Methods
        private static Float theta2u(Float theta) { return std.sqrt(theta * (2 / Pi)); }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                return 0;
            if (!SameHemisphere(wo, wi))
                return 0;
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
            }

            Vector3f wm = wi + wo;
            if (LengthSquared(wm) == 0)
                return 0;
            wm = Normalize(wm);

            /* Cartesian -> spherical coordinates */
            Float theta_o = SphericalTheta(wo), phi_o = std.atan2(wo.y, wo.x);
            Float theta_m = SphericalTheta(wm), phi_m = std.atan2(wm.y, wm.x);

            /* Spherical coordinates -> unit coordinate system */
            Point2f u_wm = new Point2f(theta2u(theta_m), phi2u(brdf.isotropic ? (phi_m - phi_o) : phi_m));
            u_wm.y = u_wm.y - pstd.floor(u_wm.y);

            PLSample ui = brdf.vndf.Invert(u_wm, phi_o, theta_o);
            Point2f sample = ui.p;
            Float vndfPDF = ui.pdf;

            Float pdf = brdf.luminance.Evaluate(sample, phi_o, theta_o);
            Float sinTheta_m = std.sqrt(Sqr(wm.x) + Sqr(wm.y));
            Float jacobian = 4f * Dot(wo, wm) * std.max<Float>(2 * Sqr(Pi) * u_wm.x * sinTheta_m, 1e-6f);
            return vndfPDF * pdf / jacobian;
        }
        private static Float phi2u(Float phi) { return phi * (1 / (2 * Pi)) + .5f; }

        public static string Name() { return "MeasuredBxDF"; }

        private static Float u2theta(Float u) { return Sqr(u) * (Pi / 2f); }
        private static Float u2phi(Float u) { return (2f * u - 1f) * Pi; }

        public static MeasuredBxDFData BRDFDataFromFile(string filename, Allocator alloc)
        {
            lock (loadedData)
            {
                if (!loadedData.ContainsKey(filename))
                    loadedData[filename] = MeasuredBxDFData.Create(filename, alloc);
                return loadedData[filename];
            }
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            // Check for valid reflection configurations
            if (!SameHemisphere(wo, wi))
                return new SampledSpectrum(0);
            if (wo.z < 0)
            {
                wo = -wo;
                wi = -wi;
            }

            // Determine half-direction vector $\wm$
            Vector3f wm = wi + wo;
            if (LengthSquared(wm) == 0)
                return new SampledSpectrum(0);
            wm = Normalize(wm);

            // Map $\wo$ and $\wm$ to the unit square $[0,\,1]^2$
            Float theta_o = SphericalTheta(wo), phi_o = std.atan2(wo.y, wo.x);
            Float theta_m = SphericalTheta(wm), phi_m = std.atan2(wm.y, wm.x);
            Point2f u_wo = new Point2f(theta2u(theta_o), phi2u(phi_o));
            Point2f u_wm = new Point2f(theta2u(theta_m), phi2u(brdf.isotropic ? (phi_m - phi_o) : phi_m));
            u_wm[1] = u_wm[1] - pstd.floor(u_wm[1]);

            // Evaluate inverse parameterization $R^{-1}$
            PLSample ui = brdf.vndf.Invert(u_wm, phi_o, theta_o);

            // Evaluate spectral 5D interpolant
            SampledSpectrum fr = default;
            for (int i = 0; i < NSpectrumSamples; ++i)
                fr[i] = std.max<Float>(0, brdf.spectra.Evaluate(ui.p, phi_o, theta_o, lambda[i]));

            // Return measured BRDF value
            return fr * brdf.ndf.Evaluate(u_wm) / (4 * brdf.sigma.Evaluate(u_wo) * CosTheta(wi));
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            // Check flags and detect interactions in lower hemisphere
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                return default;
            bool flipWi = false;
            if (wo.z <= 0)
            {
                wo = -wo;
                flipWi = true;
            }

            // Initialize parameters of conditional distribution
            Float theta_o = SphericalTheta(wo), phi_o = std.atan2(wo.y, wo.x);

            // Warp sample using luminance distribution
            PLSample s = brdf.luminance.Sample(u, phi_o, theta_o);
            u = s.p;
            Float lum_pdf = s.pdf;

            // Sample visible normal distribution of measured BRDF
            s = brdf.vndf.Sample(u, phi_o, theta_o);
            Point2f u_wm = s.p;
            Float pdf = s.pdf;

            // Map from microfacet normal to incident direction
            Float phi_m = u2phi(u_wm.y), theta_m = u2theta(u_wm.x);
            if (brdf.isotropic)
                phi_m += phi_o;
            Float sinTheta_m = std.sin(theta_m), cosTheta_m = std.cos(theta_m);
            Vector3f wm = SphericalDirection(sinTheta_m, cosTheta_m, phi_m);
            Vector3f wi = Reflect(wo, wm);
            if (wi.z <= 0)
                return default;

            // Interpolate spectral BRDF
            SampledSpectrum fr = new SampledSpectrum(0);
            for (int i = 0; i < NSpectrumSamples; ++i)
                fr[i] = std.max<Float>(0, brdf.spectra.Evaluate(u, phi_o, theta_o, lambda[i]));

            Point2f u_wo = new Point2f(theta2u(theta_o), phi2u(phi_o));
            fr *= brdf.ndf.Evaluate(u_wm) / (4 * brdf.sigma.Evaluate(u_wo) * AbsCosTheta(wi));
            pdf /= 4 * Dot(wo, wm) * std.max<Float>(2 * Sqr(Pi) * u_wm.x * sinTheta_m, 1e-6f);

            // Handle interactions in lower hemisphere
            if (flipWi)
                wi = -wi;

            return new BSDFSample(fr, wi, pdf * lum_pdf, BxDFFlags.GlossyReflection);
        }

        public override string ToString()
        {
            return StringPrintf("[ MeasuredBxDF brdf: %s ]", brdf);
        }

        // MeasuredBxDF Private Members
        private MeasuredBxDFData brdf;
        private SampledWavelengths lambda;
    }

    // NormalizedFresnelBxDF Definition
    public sealed unsafe class NormalizedFresnelBxDF : BxDF
    {
        // NormalizedFresnelBxDF Public Methods
        public NormalizedFresnelBxDF() { }
        public NormalizedFresnelBxDF(Float eta) { this.eta = eta; }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags)
        {
            // (in C++ this returns a BSDFSample, so "{}" is a present sample with pdf 0)
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                return new BSDFSample();

            // Cosine-sample the hemisphere, flipping the direction if necessary
            Vector3f wi = SampleCosineHemisphere(u);
            if (wo.z < 0)
                wi.z *= -1;
            return new BSDFSample(f(wo, wi, mode), wi, PDF(wo, wi, mode, sampleFlags), BxDFFlags.DiffuseReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                return 0;
            return SameHemisphere(wo, wi) ? AbsCosTheta(wi) * InvPi : 0;
        }

        public override void Regularize() { }

        public static string Name() { return "NormalizedFresnelBxDF"; }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.Reflection | BxDFFlags.Diffuse;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi))
                return new SampledSpectrum(0f);
            // Compute $\Sw$ factor for BSSRDF value
            Float c = 1 - 2 * FresnelMoment1(1 / eta);
            SampledSpectrum f = new SampledSpectrum((1 - FrDielectric(CosTheta(wi), eta)) / (c * Pi));

            // Update BSSRDF transmission term to account for adjoint light transport
            if (mode == TransportMode.Radiance)
                f *= Sqr(eta);

            return f;
        }

        public override string ToString()
        {
            return StringPrintf("[ NormalizedFresnelBxDF eta: %f ]", eta);
        }

        private Float eta;
    }

    // BxDF Method Definitions
    public abstract unsafe partial class BxDF
    {
        public SampledSpectrum rho(Vector3f wo, pstd.span<Float> uc, pstd.span<Point2f> u2)
        {
            if (wo.z == 0)
                return default;
            SampledSpectrum r = new SampledSpectrum(0f);
            DCHECK_EQ(uc.size(), u2.size());
            for (size_t i = 0; i < uc.size(); ++i)
            {
                // Compute estimate of $\rho_\roman{hd}$
                pstd.optional<BSDFSample> bs = Sample_f(wo, uc[i], u2[i]);
                if (bs && bs.value().pdf > 0)
                    r += bs.value().f * AbsCosTheta(bs.value().wi) / bs.value().pdf;
            }
            return r / uc.size();
        }

        public SampledSpectrum rho(pstd.span<Point2f> u1, pstd.span<Float> uc, pstd.span<Point2f> u2)
        {
            DCHECK_EQ(uc.size(), u1.size());
            DCHECK_EQ(u1.size(), u2.size());
            SampledSpectrum r = new SampledSpectrum(0f);
            for (size_t i = 0; i < uc.size(); ++i)
            {
                // Compute estimate of $\rho_\roman{hh}$
                Vector3f wo = SampleUniformHemisphere(u1[i]);
                if (wo.z == 0)
                    continue;
                Float pdfo = UniformHemispherePDF();
                pstd.optional<BSDFSample> bs = Sample_f(wo, uc[i], u2[i]);
                if (bs && bs.value().pdf > 0)
                    r += bs.value().f * AbsCosTheta(bs.value().wi) * AbsCosTheta(wo) / (pdfo * bs.value().pdf);
            }
            return r / (Pi * uc.size());
        }
    }

}  // namespace pbrt
