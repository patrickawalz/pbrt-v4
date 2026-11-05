// (not in pbrt-v4) Open Shading Language integration: the BxDFs of the closures that pbrt has no equivalent of, and the BxDF that sums the
// closures of a shader (the closure tree of OSL is a weighted sum of lobes).
//
// Every lobe is a BxDF in its own local frame (z is the normal of the closure, facing the viewer, wo.z > 0), as the closures of OSL are
// defined; OSLClosureBxDF puts the lobes in the frame of the BSDF and flips a lobe to the side of the viewer. The formulas of the closures
// follow testrender (src/testrender/shading.cpp), turned from its (weight, pdf) pairs (weight = f cos / pdf) to f and pdf as pbrt wants them.
// Where pbrt has an equivalent (diffuse, diffuse transmission, a smooth or GGX conductor, a dielectric) its BxDF is the lobe.
//
// Spectra: the colors of a closure are RGB, converted to pbrt spectra when the lobe is made (OSLColor); a lobe works on SampledSpectrum.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace pbrt
{
    /// A microfacet distribution: the Trowbridge-Reitz (GGX) one of pbrt, or Beckmann
    public abstract unsafe class OSLMicrofacetDistribution
    {
        public abstract Float D(Vector3f wm);
        public abstract Float Lambda(Vector3f w);
        public abstract Vector3f Sample_wm(Vector3f w, Point2f u);
        public abstract Float PDF(Vector3f w, Vector3f wm);
        public abstract bool EffectivelySmooth();
        public abstract void Regularize();

        public Float G1(Vector3f w)
        {
            return 1 / (1 + Lambda(w));
        }

        public Float G(Vector3f wo, Vector3f wi)
        {
            return 1 / (1 + Lambda(wo) + Lambda(wi));
        }
    }

    public sealed unsafe class OSLGGXDistribution : OSLMicrofacetDistribution
    {
        private TrowbridgeReitzDistribution distrib;

        public OSLGGXDistribution(Float alphaX, Float alphaY)
        {
            distrib = new TrowbridgeReitzDistribution(alphaX, alphaY);
        }

        public TrowbridgeReitzDistribution Distribution()
        {
            return distrib;
        }

        public override Float D(Vector3f wm)
        {
            return distrib.D(wm);
        }

        public override Float Lambda(Vector3f w)
        {
            return distrib.Lambda(w);
        }

        public override Vector3f Sample_wm(Vector3f w, Point2f u)
        {
            return distrib.Sample_wm(w, u);
        }

        public override Float PDF(Vector3f w, Vector3f wm)
        {
            return distrib.PDF(w, wm);
        }

        public override bool EffectivelySmooth()
        {
            return distrib.EffectivelySmooth();
        }

        public override void Regularize()
        {
            distrib.Regularize();
        }
    }

    /// The Beckmann distribution. The micro normals are sampled by the distribution of normals (the density is D cos(theta_m)), not by the
    /// distribution of visible normals: the weights stay correct and only the variance at grazing angles is higher.
    public sealed unsafe class OSLBeckmannDistribution : OSLMicrofacetDistribution
    {
        private Float alpha_x, alpha_y;

        public OSLBeckmannDistribution(Float alphaX, Float alphaY)
        {
            alpha_x = alphaX;
            alpha_y = alphaY;
            if (!EffectivelySmooth())
            {
                alpha_x = std.max<Float>(alpha_x, 1e-4f);
                alpha_y = std.max<Float>(alpha_y, 1e-4f);
            }
        }

        public override Float D(Vector3f wm)
        {
            Float tan2Theta = Tan2Theta(wm);
            if (IsInf(tan2Theta))
            {
                return 0;
            }
            Float cos4Theta = Sqr(Cos2Theta(wm));
            if (cos4Theta < 1e-16f)
            {
                return 0;
            }
            Float e = tan2Theta * (Sqr(CosPhi(wm) / alpha_x) + Sqr(SinPhi(wm) / alpha_y));
            return std.exp(-e) / (Pi * alpha_x * alpha_y * cos4Theta);
        }

        public override Float Lambda(Vector3f w)
        {
            Float tan2Theta = Tan2Theta(w);
            if (IsInf(tan2Theta))
            {
                return 0;
            }
            Float alpha2 = Sqr(CosPhi(w) * alpha_x) + Sqr(SinPhi(w) * alpha_y);
            Float a2 = 1 / (alpha2 * tan2Theta);
            if (a2 <= 0 || IsInf(a2))
            {
                return 0;
            }
            Float a = std.sqrt(a2);
            if (a >= 1.6f)
            {
                return 0;
            }
            return (1 - 1.259f * a + 0.396f * a2) / (3.535f * a + 2.181f * a2);
        }

        public override Vector3f Sample_wm(Vector3f w, Point2f u)
        {
            Float phi = std.atan2(alpha_y * std.sin(2 * Pi * u.y), alpha_x * std.cos(2 * Pi * u.y));
            Float cosPhi = std.cos(phi);
            Float sinPhi = std.sin(phi);
            Float denominator = Sqr(cosPhi / alpha_x) + Sqr(sinPhi / alpha_y);
            Float tan2Theta = -std.log(std.max<Float>(1 - u.x, 1e-20f)) / denominator;
            Float cosTheta = 1 / std.sqrt(1 + tan2Theta);
            Float sinTheta = std.sqrt(std.max<Float>(0, 1 - cosTheta * cosTheta));
            return new Vector3f(sinTheta * cosPhi, sinTheta * sinPhi, cosTheta);
        }

        public override Float PDF(Vector3f w, Vector3f wm)
        {
            return D(wm) * AbsCosTheta(wm);
        }

        public override bool EffectivelySmooth()
        {
            return std.max(alpha_x, alpha_y) < 1e-3f;
        }

        public override void Regularize()
        {
            if (alpha_x < 0.3f)
            {
                alpha_x = Clamp(2 * alpha_x, 0.1f, 0.3f);
            }
            if (alpha_y < 0.3f)
            {
                alpha_y = Clamp(2 * alpha_y, 0.1f, 0.3f);
            }
        }
    }

    /// The Fresnel term of a microfacet or a smooth surface, as a spectrum
    public abstract unsafe class OSLFresnel
    {
        /// The reflectance for the cosine of the angle between the direction and the (micro) normal
        public abstract SampledSpectrum Eval(Float cosTheta);

        /// Whether the part that is not reflected is transmitted (a dielectric) and not absorbed (a conductor)
        public abstract bool Transmits();
    }

    /// Dielectric Fresnel of a relative index of refraction; eta == 0 is the "no Fresnel" of OSL (everything is reflected)
    public sealed unsafe class OSLDielectricFresnel : OSLFresnel
    {
        public Float eta;

        public OSLDielectricFresnel(Float eta)
        {
            this.eta = eta;
        }

        public override SampledSpectrum Eval(Float cosTheta)
        {
            if (eta == 0)
            {
                return new SampledSpectrum(1f);
            }
            return new SampledSpectrum(FrDielectric(cosTheta, eta));
        }

        public override bool Transmits()
        {
            return eta != 0;
        }
    }

    public sealed unsafe class OSLConductorFresnel : OSLFresnel
    {
        private SampledSpectrum eta, k;

        public OSLConductorFresnel(SampledSpectrum eta, SampledSpectrum k)
        {
            this.eta = eta;
            this.k = k;
        }

        public override SampledSpectrum Eval(Float cosTheta)
        {
            return FrComplex(std.abs(cosTheta), eta, k);
        }

        public override bool Transmits()
        {
            return false;
        }
    }

    /// The generalized Schlick Fresnel of MaterialX: lerp(F0, F90, (1 - cos)^exponent)
    public sealed unsafe class OSLSchlickFresnel : OSLFresnel
    {
        private SampledSpectrum f0, f90;
        private Float exponent;

        public OSLSchlickFresnel(SampledSpectrum f0, SampledSpectrum f90, Float exponent)
        {
            this.f0 = f0;
            this.f90 = f90;
            this.exponent = exponent;
        }

        public override SampledSpectrum Eval(Float cosTheta)
        {
            Float x = Clamp(1 - std.abs(cosTheta), 0f, 1f);
            Float m = std.pow(x, exponent);
            return (1 - m) * f0 + m * f90;
        }

        public override bool Transmits()
        {
            return true;
        }
    }

    /// Specular reflection (reflection (N, eta) of OSL; a mirror whose reflectance is the Fresnel term)
    public sealed unsafe class OSLSpecularReflectionBxDF : BxDF
    {
        private OSLFresnel fresnel;

        public OSLSpecularReflectionBxDF(OSLFresnel fresnel)
        {
            this.fresnel = fresnel;
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.SpecularReflection;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return new SampledSpectrum(0f);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z == 0)
            {
                return default;
            }
            Vector3f wi = new Vector3f(-wo.x, -wo.y, wo.z);
            SampledSpectrum f = fresnel.Eval(CosTheta(wo)) / AbsCosTheta(wi);
            return new BSDFSample(f, wi, 1, BxDFFlags.SpecularReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return 0;
        }

        public override void Regularize()
        {
        }

        public override string ToString()
        {
            return "[ OSLSpecularReflectionBxDF ]";
        }
    }

    /// Specular refraction (refraction (N, eta) of OSL; the transmitted part of the Fresnel term, with no scaling of the radiance)
    public sealed unsafe class OSLSpecularRefractionBxDF : BxDF
    {
        private Float eta;

        public OSLSpecularRefractionBxDF(Float eta)
        {
            this.eta = eta;
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.SpecularTransmission;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return new SampledSpectrum(0f);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0 || wo.z == 0 || eta <= 0)
            {
                return default;
            }
            Vector3f wi;
            Float etap;
            if (!Refract(wo, new Normal3f(0, 0, 1), eta, &etap, &wi))
            {
                return default;
            }
            Float transmitted = 1 - FrDielectric(CosTheta(wo), eta);
            if (transmitted <= 0 || wi.z == 0)
            {
                return default;
            }
            SampledSpectrum ft = new SampledSpectrum(transmitted / AbsCosTheta(wi));
            if (mode == TransportMode.Radiance)
            {
                ft /= Sqr(etap);
            }
            return new BSDFSample(ft, wi, 1, BxDFFlags.SpecularTransmission, etap);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return 0;
        }

        public override void Regularize()
        {
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLSpecularRefractionBxDF eta: %f ]", eta);
        }
    }

    /// transparent (): the ray goes on
    public sealed unsafe class OSLTransparentBxDF : BxDF
    {
        public override BxDFFlags Flags()
        {
            return BxDFFlags.SpecularTransmission;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return new SampledSpectrum(0f);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0 || wo.z == 0)
            {
                return default;
            }
            Vector3f wi = -wo;
            return new BSDFSample(new SampledSpectrum(1 / AbsCosTheta(wi)), wi, 1, BxDFFlags.SpecularTransmission);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return 0;
        }

        public override void Regularize()
        {
        }

        public override string ToString()
        {
            return "[ OSLTransparentBxDF ]";
        }
    }

    /// oren_nayar (N, sigma), the qualitative model, and oren_nayar_diffuse_bsdf of MaterialX (sigma = roughness; the albedo is the weight)
    public sealed unsafe class OSLOrenNayarBxDF : BxDF
    {
        private Float A, B;

        public OSLOrenNayarBxDF(Float sigma)
        {
            Float sigma2 = sigma * sigma;
            A = 1 - sigma2 / (2 * (sigma2 + 0.33f));
            B = 0.45f * sigma2 / (sigma2 + 0.09f);
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.DiffuseReflection;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi))
            {
                return new SampledSpectrum(0f);
            }
            Float sinThetaI = SinTheta(wi);
            Float sinThetaO = SinTheta(wo);
            Float maxCos = 0;
            if (sinThetaI > 1e-4f && sinThetaO > 1e-4f)
            {
                Float cosDeltaPhi = (wi.x * wo.x + wi.y * wo.y) / (sinThetaI * sinThetaO);
                maxCos = std.max<Float>(0, cosDeltaPhi);
            }
            Float sinAlpha, tanBeta;
            if (AbsCosTheta(wi) > AbsCosTheta(wo))
            {
                sinAlpha = sinThetaO;
                tanBeta = sinThetaI / AbsCosTheta(wi);
            }
            else
            {
                sinAlpha = sinThetaI;
                tanBeta = sinThetaO / AbsCosTheta(wo);
            }
            return new SampledSpectrum(InvPi * (A + B * maxCos * sinAlpha * tanBeta));
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
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
            return new BSDFSample(f(wo, wi, mode), wi, pdf, BxDFFlags.DiffuseReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi))
            {
                return 0;
            }
            return CosineHemispherePDF(AbsCosTheta(wi));
        }

        public override void Regularize()
        {
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLOrenNayarBxDF A: %f B: %f ]", A, B);
        }
    }

    /// burley_diffuse_bsdf of MaterialX: the diffuse of the Disney principled model (retro-reflection depending on the roughness)
    public sealed unsafe class OSLBurleyDiffuseBxDF : BxDF
    {
        private Float roughness;

        public OSLBurleyDiffuseBxDF(Float roughness)
        {
            this.roughness = Clamp(roughness, 0f, 1f);
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.DiffuseReflection;
        }

        private static Float SchlickWeight(Float cosTheta)
        {
            Float m = Clamp(1 - cosTheta, 0f, 1f);
            Float m2 = m * m;
            return m2 * m2 * m;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi))
            {
                return new SampledSpectrum(0f);
            }
            Vector3f wh = wi + wo;
            if (LengthSquared(wh) == 0)
            {
                return new SampledSpectrum(0f);
            }
            wh = Normalize(wh);
            Float cosThetaD = AbsDot(wi, wh);
            Float fd90 = 0.5f + 2 * roughness * cosThetaD * cosThetaD;
            Float fo = SchlickWeight(AbsCosTheta(wo));
            Float fi = SchlickWeight(AbsCosTheta(wi));
            return new SampledSpectrum(InvPi * (1 + (fd90 - 1) * fo) * (1 + (fd90 - 1) * fi));
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
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
            return new BSDFSample(f(wo, wi, mode), wi, pdf, BxDFFlags.DiffuseReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi))
            {
                return 0;
            }
            return CosineHemispherePDF(AbsCosTheta(wi));
        }

        public override void Regularize()
        {
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLBurleyDiffuseBxDF roughness: %f ]", roughness);
        }
    }

    /// phong (N, exponent) of OSL: f = (exponent + 2) / (2 pi) cos(R, wi)^exponent (the modified Phong, without the cosine of the incidence)
    public sealed unsafe class OSLPhongBxDF : BxDF
    {
        private Float exponent;

        public OSLPhongBxDF(Float exponent)
        {
            this.exponent = std.max<Float>(exponent, 0);
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.GlossyReflection;
        }

        private static Vector3f Mirror(Vector3f wo)
        {
            return new Vector3f(-wo.x, -wo.y, wo.z);
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (wo.z <= 0 || wi.z <= 0)
            {
                return new SampledSpectrum(0f);
            }
            Float cosRI = Dot(Mirror(wo), wi);
            if (cosRI <= 0)
            {
                return new SampledSpectrum(0f);
            }
            return new SampledSpectrum((exponent + 2) * InvPi * 0.5f * std.pow(cosRI, exponent));
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z <= 0)
            {
                return default;
            }
            Vector3f r = Mirror(wo);
            Float phi = 2 * Pi * u.x;
            Float cosTheta = std.pow(u.y, 1 / (exponent + 1));
            Float sinTheta = std.sqrt(std.max<Float>(0, 1 - cosTheta * cosTheta));
            Frame frame = Frame.FromZ(r);
            Vector3f wi = frame.FromLocal(new Vector3f(std.cos(phi) * sinTheta, std.sin(phi) * sinTheta, cosTheta));
            if (wi.z <= 0)
            {
                return default;
            }
            Float pdf = (exponent + 1) * InvPi * 0.5f * std.pow(cosTheta, exponent);
            if (pdf <= 0)
            {
                return default;
            }
            return new BSDFSample(f(wo, wi, mode), wi, pdf, BxDFFlags.GlossyReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z <= 0 || wi.z <= 0)
            {
                return 0;
            }
            Float cosRI = Dot(Mirror(wo), wi);
            if (cosRI <= 0)
            {
                return 0;
            }
            return (exponent + 1) * InvPi * 0.5f * std.pow(cosRI, exponent);
        }

        public override void Regularize()
        {
            exponent = std.min<Float>(exponent, 500);
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLPhongBxDF exponent: %f ]", exponent);
        }
    }

    /// ward (N, T, ax, ay) of OSL, the anisotropic Ward model (the tangent T is the x axis of the local frame)
    public sealed unsafe class OSLWardBxDF : BxDF
    {
        private Float ax, ay;

        public OSLWardBxDF(Float ax, Float ay)
        {
            this.ax = std.max<Float>(ax, 1e-3f);
            this.ay = std.max<Float>(ay, 1e-3f);
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.GlossyReflection;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (wo.z <= 0 || wi.z <= 0)
            {
                return new SampledSpectrum(0f);
            }
            Vector3f h = Normalize(wi + wo);
            Float dotx = h.x / ax;
            Float doty = h.y / ay;
            Float dotn = h.z;
            Float e = std.exp(-(dotx * dotx + doty * doty) / (dotn * dotn));
            return new SampledSpectrum(e / (4 * Pi * ax * ay * std.sqrt(wi.z * wo.z)));
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z <= 0)
            {
                return default;
            }
            Float phi = 2 * Pi * u.x;
            Float cosPhi = ax * std.cos(phi);
            Float sinPhi = ay * std.sin(phi);
            Float k = 1 / std.sqrt(cosPhi * cosPhi + sinPhi * sinPhi);
            cosPhi *= k;
            sinPhi *= k;
            Float thetaDenom = (cosPhi * cosPhi) / (ax * ax) + (sinPhi * sinPhi) / (ay * ay);
            Float tanTheta2 = -std.log(std.max<Float>(1 - u.y, 1e-20f)) / thetaDenom;
            Float cosTheta = 1 / std.sqrt(1 + tanTheta2);
            Float sinTheta = cosTheta * std.sqrt(tanTheta2);
            Vector3f h = new Vector3f(sinTheta * cosPhi, sinTheta * sinPhi, cosTheta);
            Float oh = Dot(h, wo);
            if (oh <= 0)
            {
                return default;
            }
            Vector3f wi = 2 * oh * h - wo;
            if (wi.z <= 0)
            {
                return default;
            }
            Float pdf = PDF(wo, wi, mode, sampleFlags);
            if (pdf <= 0)
            {
                return default;
            }
            return new BSDFSample(f(wo, wi, mode), wi, pdf, BxDFFlags.GlossyReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z <= 0 || wi.z <= 0)
            {
                return 0;
            }
            Vector3f h = Normalize(wi + wo);
            Float dotx = h.x / ax;
            Float doty = h.y / ay;
            Float dotn = h.z;
            Float oh = Dot(h, wo);
            if (oh <= 0 || dotn <= 0)
            {
                return 0;
            }
            Float e = std.exp(-(dotx * dotx + doty * doty) / (dotn * dotn));
            Float c = 4 * Pi * ax * ay;
            Float k = oh * dotn * dotn * dotn;
            return e / (c * k);
        }

        public override void Regularize()
        {
            ax = std.max<Float>(ax, 0.05f);
            ay = std.max<Float>(ay, 0.05f);
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLWardBxDF ax: %f ay: %f ]", ax, ay);
        }
    }

    /// sheen_bsdf of MaterialX (the "Conty-Kulla" mode): the Charlie distribution with the Neubelt visibility, sampled by the cosine
    public sealed unsafe class OSLSheenBxDF : BxDF
    {
        private Float alpha;

        public OSLSheenBxDF(Float roughness)
        {
            alpha = Clamp(roughness, 0.06f, 1f);
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.DiffuseReflection;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi))
            {
                return new SampledSpectrum(0f);
            }
            Float cosO = AbsCosTheta(wo);
            Float cosI = AbsCosTheta(wi);
            if (cosO == 0 || cosI == 0)
            {
                return new SampledSpectrum(0f);
            }
            Vector3f wh = wi + wo;
            if (LengthSquared(wh) == 0)
            {
                return new SampledSpectrum(0f);
            }
            wh = Normalize(wh);
            Float invAlpha = 1 / alpha;
            Float sinTheta = std.sqrt(std.max<Float>(0, 1 - wh.z * wh.z));
            Float D = (2 + invAlpha) * std.pow(sinTheta, invAlpha) * InvPi * 0.5f;
            Float V = 1 / (4 * (cosI + cosO - cosI * cosO));
            return new SampledSpectrum(D * V);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
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
            return new BSDFSample(f(wo, wi, mode), wi, pdf, BxDFFlags.DiffuseReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi))
            {
                return 0;
            }
            return CosineHemispherePDF(AbsCosTheta(wi));
        }

        public override void Regularize()
        {
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLSheenBxDF alpha: %f ]", alpha);
        }
    }

    /// A rough interface with any distribution and Fresnel term, reflecting and/or refracting: the microfacet closure of OSL (testrender's
    /// Microfacet<Distribution, Refract>), the Schlick dielectric of MaterialX, and Beckmann or anisotropic versions of both. The
    /// structure is that of pbrt's DielectricBxDF; the radiance is not scaled by 1/eta^2 on transmission, as in OSL.
    public sealed unsafe class OSLMicrofacetBxDF : BxDF
    {
        private OSLMicrofacetDistribution distrib;
        private OSLFresnel fresnel;
        private Float eta;
        private bool doReflection, doTransmission;
        private SampledSpectrum reflectionTint, transmissionTint;

        public OSLMicrofacetBxDF(OSLMicrofacetDistribution distrib, OSLFresnel fresnel, Float eta, bool doReflection, bool doTransmission,
                                 SampledSpectrum reflectionTint, SampledSpectrum transmissionTint)
        {
            this.distrib = distrib;
            this.fresnel = fresnel;
            this.eta = eta;
            this.doReflection = doReflection;
            this.doTransmission = doTransmission && fresnel.Transmits() && eta > 0 && eta != 1;
            this.reflectionTint = reflectionTint;
            this.transmissionTint = transmissionTint;
        }

        public override BxDFFlags Flags()
        {
            BxDFFlags flags = BxDFFlags.Unset;
            if (doReflection)
            {
                flags |= BxDFFlags.Reflection;
            }
            if (doTransmission)
            {
                flags |= BxDFFlags.Transmission;
            }
            return flags | BxDFFlags.Glossy;
        }

        public override void Regularize()
        {
            distrib.Regularize();
        }

        private void Weights(Float cosThetaOm, out SampledSpectrum R, out SampledSpectrum T)
        {
            SampledSpectrum F = fresnel.Eval(cosThetaOm);
            R = doReflection ? reflectionTint * F : new SampledSpectrum(0f);
            T = doTransmission ? transmissionTint * (1 - F) : new SampledSpectrum(0f);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0)
            {
                return default;
            }
            Vector3f wm = distrib.Sample_wm(wo, u);
            if (Dot(wo, wm) <= 0)
            {
                return default;
            }
            SampledSpectrum R, T;
            Weights(Dot(wo, wm), out R, out T);
            Float pr = R.Average(), pt = T.Average();
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            {
                pr = 0;
            }
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
            {
                pt = 0;
            }
            if (pr <= 0 && pt <= 0)
            {
                return default;
            }
            if (uc < pr / (pr + pt))
            {
                Vector3f wi = Reflect(wo, wm);
                if (!SameHemisphere(wo, wi))
                {
                    return default;
                }
                Float pdf = distrib.PDF(wo, wm) / (4 * AbsDot(wo, wm)) * pr / (pr + pt);
                SampledSpectrum fr = R * (distrib.D(wm) * distrib.G(wo, wi) / (4 * CosTheta(wi) * CosTheta(wo)));
                return new BSDFSample(fr, wi, pdf, BxDFFlags.GlossyReflection);
            }
            else
            {
                Float etap;
                Vector3f wi;
                bool tir = !Refract(wo, (Normal3f)wm, eta, &etap, &wi);
                if (tir || SameHemisphere(wo, wi) || wi.z == 0)
                {
                    return default;
                }
                Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etap);
                Float dwm_dwi = AbsDot(wi, wm) / denom;
                Float pdf = distrib.PDF(wo, wm) * dwm_dwi * pt / (pr + pt);
                SampledSpectrum ft = T * (distrib.D(wm) * distrib.G(wo, wi) * std.abs(Dot(wi, wm) * Dot(wo, wm) / (CosTheta(wi) * CosTheta(wo) * denom)));
                if (mode == TransportMode.Radiance)
                {
                    ft /= Sqr(etap);
                }
                return new BSDFSample(ft, wi, pdf, BxDFFlags.GlossyTransmission, etap);
            }
        }

        private bool HalfVector(Vector3f wo, Vector3f wi, out Vector3f wm, out Float etap, out bool reflect)
        {
            Float cosTheta_o = CosTheta(wo), cosTheta_i = CosTheta(wi);
            reflect = cosTheta_i * cosTheta_o > 0;
            etap = 1;
            wm = default;
            if (!reflect)
            {
                etap = cosTheta_o > 0 ? eta : (1 / eta);
            }
            wm = wi * etap + wo;
            if (cosTheta_i == 0 || cosTheta_o == 0 || LengthSquared(wm) == 0)
            {
                return false;
            }
            wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));
            if (Dot(wm, wi) * cosTheta_i < 0 || Dot(wm, wo) * cosTheta_o < 0)
            {
                return false;
            }
            return true;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            Vector3f wm;
            Float etap;
            bool reflect;
            if (!HalfVector(wo, wi, out wm, out etap, out reflect))
            {
                return new SampledSpectrum(0f);
            }
            SampledSpectrum R, T;
            Weights(Dot(wo, wm), out R, out T);
            if (reflect)
            {
                if (!doReflection)
                {
                    return new SampledSpectrum(0f);
                }
                return R * (distrib.D(wm) * distrib.G(wo, wi) / std.abs(4 * CosTheta(wi) * CosTheta(wo)));
            }
            if (!doTransmission)
            {
                return new SampledSpectrum(0f);
            }
            Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etap) * CosTheta(wi) * CosTheta(wo);
            SampledSpectrum ftv = T * (distrib.D(wm) * distrib.G(wo, wi) * std.abs(Dot(wi, wm) * Dot(wo, wm) / denom));
            if (mode == TransportMode.Radiance)
            {
                ftv /= Sqr(etap);
            }
            return ftv;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Vector3f wm;
            Float etap;
            bool reflect;
            if (!HalfVector(wo, wi, out wm, out etap, out reflect))
            {
                return 0;
            }
            SampledSpectrum R, T;
            Weights(Dot(wo, wm), out R, out T);
            Float pr = R.Average(), pt = T.Average();
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            {
                pr = 0;
            }
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
            {
                pt = 0;
            }
            if (pr <= 0 && pt <= 0)
            {
                return 0;
            }
            if (reflect)
            {
                return distrib.PDF(wo, wm) / (4 * AbsDot(wo, wm)) * pr / (pr + pt);
            }
            Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etap);
            Float dwm_dwi = AbsDot(wi, wm) / denom;
            return distrib.PDF(wo, wm) * dwm_dwi * pt / (pr + pt);
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLMicrofacetBxDF eta: %f reflection: %s transmission: %s ]", eta, doReflection, doTransmission);
        }
    }

    /// A BxDF whose reflection and transmission are scaled by different colors (the reflection_tint and transmission_tint of dielectric_bsdf)
    public sealed unsafe class OSLTintedBxDF : BxDF
    {
        private BxDF inner;
        private SampledSpectrum reflectionTint, transmissionTint;

        public OSLTintedBxDF(BxDF inner, SampledSpectrum reflectionTint, SampledSpectrum transmissionTint)
        {
            this.inner = inner;
            this.reflectionTint = reflectionTint;
            this.transmissionTint = transmissionTint;
        }

        public override BxDFFlags Flags()
        {
            BxDFFlags flags = inner.Flags();
            if (!(bool)reflectionTint)
            {
                flags = new BxDFFlags(flags.value & ~BxDFFlags.Reflection.value);
            }
            if (!(bool)transmissionTint)
            {
                flags = new BxDFFlags(flags.value & ~BxDFFlags.Transmission.value);
            }
            if ((flags & BxDFFlags.Reflection) == 0 && (flags & BxDFFlags.Transmission) == 0)
            {
                return BxDFFlags.Unset;
            }
            return flags;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            SampledSpectrum value = inner.f(wo, wi, mode);
            return SameHemisphere(wo, wi) ? value * reflectionTint : value * transmissionTint;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            pstd.optional<BSDFSample> sample = inner.Sample_f(wo, uc, u, mode, sampleFlags);
            if (!sample)
            {
                return default;
            }
            BSDFSample s = sample.value();
            s.f = s.IsReflection() ? s.f * reflectionTint : s.f * transmissionTint;
            if (!(bool)s.f)
            {
                return default;
            }
            return s;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return inner.PDF(wo, wi, mode, sampleFlags);
        }

        public override void Regularize()
        {
            inner.Regularize();
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLTintedBxDF inner: %s ]", inner);
        }
    }

    /// The sum of the lobes of a closure tree: the BxDF of an OSL shader. Each lobe has a weight (a spectrum) and works in the frame of its own
    /// normal. The lobe for a sample is chosen by the albedo of the lobes, as in testrender's CompositeBSDF; f and the pdf of a sample sum
    /// over the lobes, unless the sampled lobe is specular.
    public sealed unsafe class OSLClosureBxDF : BxDF
    {
        private struct Component
        {
            public BxDF bxdf;
            public SampledSpectrum weight;
            public Float selection;
            public bool framed;
            public Frame frame;
            public BxDFFlags flags;
        }

        private Component[] components = new Component[8];
        private int count;
        private Float selectionSum;
        private BxDFFlags allFlags = BxDFFlags.Unset;

        public int Count
        {
            get { return count; }
        }

        /// Adds a lobe. selection is proportional to the chance to choose it (about the average of its weight times its albedo); normal is the
        /// normal of the lobe in the local frame of the BSDF and tangent its x axis (zero for any); a lobe on the z axis needs no frame.
        public void Add(BxDF bxdf, SampledSpectrum weight, Float selection, Vector3f normal, Vector3f tangent)
        {
            if (count == components.Length)
            {
                Array.Resize(ref components, count * 2);
            }
            Component c = new Component();
            c.bxdf = bxdf;
            c.weight = weight;
            c.selection = std.max<Float>(selection, 1e-6f);
            c.flags = bxdf.Flags();
            c.framed = false;
            c.frame = new Frame();
            Float nl = Length(normal);
            if (nl > 0)
            {
                Vector3f n = normal / nl;
                bool tilted = std.abs(n.z) < 0.99999f;
                if (tilted || n.z < 0 || LengthSquared(tangent) > 0)
                {
                    Vector3f x = tangent;
                    if (LengthSquared(x) > 0)
                    {
                        x = x - Dot(x, n) * n;
                    }
                    if (LengthSquared(x) < 1e-12f)
                    {
                        x = std.abs(n.x) > 0.9f ? new Vector3f(0, 1, 0) : new Vector3f(1, 0, 0);
                        x = x - Dot(x, n) * n;
                    }
                    x = Normalize(x);
                    c.frame = Frame.FromXZ(x, n);
                    c.framed = tilted || Dot(n, new Vector3f(0, 0, 1)) < 0 || Math.Abs(Dot(x, new Vector3f(1, 0, 0)) - 1) > 1e-4f;
                    if (n.z < 0)
                    {
                        // the lobe is on the other side of the BSDF: its z axis points away
                        c.framed = true;
                    }
                }
            }
            components[count++] = c;
            selectionSum += c.selection;
            allFlags |= c.flags;
        }

        public override BxDFFlags Flags()
        {
            return allFlags;
        }

        public override void Regularize()
        {
            for (int i = 0; i < count; ++i)
            {
                components[i].bxdf.Regularize();
            }
        }

        // The directions in the local frame of a lobe: the frame of its normal, mirrored if wo is below it
        private static void ToLobe(ref Component c, Vector3f wo, Vector3f wi, out Vector3f woLobe, out Vector3f wiLobe, out bool flipped)
        {
            woLobe = wo;
            wiLobe = wi;
            if (c.framed)
            {
                woLobe = c.frame.ToLocal(wo);
                wiLobe = c.frame.ToLocal(wi);
            }
            flipped = woLobe.z < 0;
            if (flipped)
            {
                woLobe.z = -woLobe.z;
                wiLobe.z = -wiLobe.z;
            }
        }

        private static Vector3f FromLobe(ref Component c, Vector3f wiLobe, bool flipped)
        {
            if (flipped)
            {
                wiLobe.z = -wiLobe.z;
            }
            return c.framed ? c.frame.FromLocal(wiLobe) : wiLobe;
        }

        private Float SelectionSum(BxDFReflTransFlags sampleFlags)
        {
            if (sampleFlags == BxDFReflTransFlags.All)
            {
                return selectionSum;
            }
            Float sum = 0;
            for (int i = 0; i < count; ++i)
            {
                if ((components[i].flags & sampleFlags) != 0)
                {
                    sum += components[i].selection;
                }
            }
            return sum;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            SampledSpectrum sum = new SampledSpectrum(0f);
            for (int i = 0; i < count; ++i)
            {
                Vector3f woLobe, wiLobe;
                bool flipped;
                ToLobe(ref components[i], wo, wi, out woLobe, out wiLobe, out flipped);
                sum += components[i].weight * components[i].bxdf.f(woLobe, wiLobe, mode);
            }
            return sum;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Float total = SelectionSum(sampleFlags);
            if (total <= 0)
            {
                return 0;
            }
            Float pdf = 0;
            for (int i = 0; i < count; ++i)
            {
                if ((components[i].flags & sampleFlags) == 0)
                {
                    continue;
                }
                Vector3f woLobe, wiLobe;
                bool flipped;
                ToLobe(ref components[i], wo, wi, out woLobe, out wiLobe, out flipped);
                pdf += components[i].selection / total * components[i].bxdf.PDF(woLobe, wiLobe, mode, sampleFlags);
            }
            return pdf;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Float total = SelectionSum(sampleFlags);
            if (total <= 0)
            {
                return default;
            }
            // choose the lobe
            Float target = uc * total;
            int chosen = -1;
            Float accum = 0;
            for (int i = 0; i < count; ++i)
            {
                if ((components[i].flags & sampleFlags) == 0)
                {
                    continue;
                }
                chosen = i;
                accum += components[i].selection;
                if (target < accum)
                {
                    break;
                }
            }
            if (chosen < 0)
            {
                return default;
            }
            Float probability = components[chosen].selection / total;
            Float lobeStart = accum - components[chosen].selection;
            Float ucLobe = Clamp((target - lobeStart) / components[chosen].selection, 0f, 0.99999994f);

            Vector3f woLobe, unusedWi;
            bool flipped;
            ToLobe(ref components[chosen], wo, wo, out woLobe, out unusedWi, out flipped);
            pstd.optional<BSDFSample> sample = components[chosen].bxdf.Sample_f(woLobe, ucLobe, u, mode, sampleFlags);
            if (!sample)
            {
                return default;
            }
            BSDFSample s = sample.value();
            Vector3f wi = FromLobe(ref components[chosen], s.wi, flipped);
            if (s.IsSpecular())
            {
                s.f = components[chosen].weight * s.f;
                s.pdf *= probability;
                s.wi = wi;
                if (s.pdf <= 0 || !(bool)s.f)
                {
                    return default;
                }
                return s;
            }
            // a lobe that is not specular: the value and the density are those of the whole sum in the direction
            SampledSpectrum fSum = new SampledSpectrum(0f);
            Float pdfSum = 0;
            for (int i = 0; i < count; ++i)
            {
                if ((components[i].flags & sampleFlags) == 0)
                {
                    continue;
                }
                Vector3f woI, wiI;
                bool flippedI;
                ToLobe(ref components[i], wo, wi, out woI, out wiI, out flippedI);
                fSum += components[i].weight * components[i].bxdf.f(woI, wiI, mode);
                pdfSum += components[i].selection / total * components[i].bxdf.PDF(woI, wiI, mode, sampleFlags);
            }
            if (pdfSum <= 0 || !(bool)fSum)
            {
                return default;
            }
            s.f = fSum;
            s.pdf = pdfSum;
            s.wi = wi;
            return s;
        }

        public override string ToString()
        {
            return StringPrintf("[ OSLClosureBxDF %d lobes ]", count);
        }
    }
}
