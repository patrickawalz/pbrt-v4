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

        public override string ToString()
        {
            return StringPrintf("[ DiffuseBxDF R: %s ]", R);
        }

        public override void Regularize() { }

        public override BxDFFlags Flags()
        {
            return (bool)R ? BxDFFlags.DiffuseReflection : BxDFFlags.Unset;
        }

        private SampledSpectrum R;
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

        public override string ToString()
        {
            return StringPrintf("[ DiffuseTransmissionBxDF R: %s T: %s ]", R, T);
        }

        public override void Regularize() { }

        public override BxDFFlags Flags()
        {
            return (((bool)R ? BxDFFlags.DiffuseReflection : BxDFFlags.Unset) |
                    ((bool)T ? BxDFFlags.DiffuseTransmission : BxDFFlags.Unset));
        }

        // DiffuseTransmissionBxDF Private Members
        private SampledSpectrum R, T;
    }

    // DielectricBxDF Definition
    public sealed unsafe class DielectricBxDF : BxDF
    {
        // DielectricBxDF Public Methods
        public DielectricBxDF() { }
        public DielectricBxDF(Float eta, TrowbridgeReitzDistribution mfDistrib)
        {
            this.eta = eta;
            this.mfDistrib = mfDistrib;
        }

        public override BxDFFlags Flags()
        {
            BxDFFlags flags = (eta == 1) ? BxDFFlags.Transmission : (BxDFFlags.Reflection | BxDFFlags.Transmission);
            return flags | (mfDistrib.EffectivelySmooth() ? BxDFFlags.Specular : BxDFFlags.Glossy);
        }

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

        public override string ToString()
        {
            return StringPrintf("[ DielectricBxDF eta: %f mfDistrib: %s ]", eta, mfDistrib.ToString());
        }

        public override void Regularize() { mfDistrib.Regularize(); }

        // DielectricBxDF Private Members
        private Float eta;
        private TrowbridgeReitzDistribution mfDistrib;
    }

    // ThinDielectricBxDF Definition
    public sealed unsafe class ThinDielectricBxDF : BxDF
    {
        // ThinDielectricBxDF Public Methods
        public ThinDielectricBxDF() { }
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

        public override string ToString()
        {
            return StringPrintf("[ ThinDielectricBxDF eta: %f ]", eta);
        }

        public override void Regularize() { /* TODO */ }

        public override BxDFFlags Flags()
        {
            return (BxDFFlags.Reflection | BxDFFlags.Transmission | BxDFFlags.Specular);
        }

        private Float eta;
    }

    // ConductorBxDF Definition
    public sealed unsafe class ConductorBxDF : BxDF
    {
        // ConductorBxDF Public Methods
        public ConductorBxDF() { }
        public ConductorBxDF(TrowbridgeReitzDistribution mfDistrib, SampledSpectrum eta, SampledSpectrum k)
        {
            this.mfDistrib = mfDistrib;
            this.eta = eta;
            this.k = k;
        }

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

        public override string ToString()
        {
            return StringPrintf("[ ConductorBxDF mfDistrib: %s eta: %s k: %s ]", mfDistrib, eta, k);
        }

        public override void Regularize() { mfDistrib.Regularize(); }

        // ConductorBxDF Private Members
        private TrowbridgeReitzDistribution mfDistrib;
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

        public override string ToString()
        {
            return StringPrintf("[ LayeredBxDF top: %s bottom: %s thickness: %f albedo: %s g: %f ]", top, bottom,
                                thickness, albedo, g);
        }

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

        // LayeredBxDF Private Methods
        private static Float Tr(Float dz, Vector3f w)
        {
            if (std.abs(dz) <= FloatMinNormal)
                return 1;
            return FastExp(-std.abs(dz / w.z));
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
            v[0] = Sqr(0.726f * beta_m + 0.812f * Sqr(beta_m) + 3.7f * Pow(20, beta_m));
            v[1] = .25f * v[0];
            v[2] = 4 * v[0];
            for (int p = 3; p <= pMax; ++p)
                // TODO: is there anything better here?
                v[p] = v[2];

            Float SqrtPiOver8 = 0.626657069f;
            s = SqrtPiOver8 * (0.265f * beta_n + 1.194f * Sqr(beta_n) + 5.372f * Pow(22, beta_n));
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

        public override void Regularize() { }

        public static string Name() { return "HairBxDF"; }

        public override string ToString()
        {
            return StringPrintf("[ HairBxDF h: %f eta: %f beta_m: %f beta_n: %f v[0]: %f s: %f sigma_a: %s ]", h, eta,
                                beta_m, beta_n, v[0], s, sigma_a);
        }

        public override BxDFFlags Flags() { return BxDFFlags.GlossyReflection; }

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
                sigma_a[i] = Sqr(std.log(c[i]) / (5.969f - 0.215f * beta_n + 2.532f * Sqr(beta_n) - 10.73f * Pow(3, beta_n) +
                                                  5.574f * Pow(4, beta_n) + 0.245f * Pow(5, beta_n)));
            return sigma_a;
        }

        // HairBxDF Constants
        private const int pMax = 3;

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

        private static Float Phi(int p, Float gamma_o, Float gamma_t)
        {
            return 2 * p * gamma_t - 2 * gamma_o + p * Pi;
        }

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

        public override void Regularize() { }

        public static string Name() { return "MeasuredBxDF"; }

        public override string ToString()
        {
            return StringPrintf("[ MeasuredBxDF brdf: %s ]", brdf);
        }

        public override BxDFFlags Flags() { return (BxDFFlags.Reflection | BxDFFlags.Glossy); }

        // MeasuredBxDF Private Methods
        private static Float theta2u(Float theta) { return std.sqrt(theta * (2 / Pi)); }
        private static Float phi2u(Float phi) { return phi * (1 / (2 * Pi)) + .5f; }

        private static Float u2theta(Float u) { return Sqr(u) * (Pi / 2f); }
        private static Float u2phi(Float u) { return (2f * u - 1f) * Pi; }

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

        public override string ToString()
        {
            return StringPrintf("[ NormalizedFresnelBxDF eta: %f ]", eta);
        }

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
