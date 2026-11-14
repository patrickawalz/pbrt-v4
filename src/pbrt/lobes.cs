// (not in pbrt-v4) lobes.cs
//
// Two lobes that the artists' models use and that are not physical Fresnel reflection, for the materials that are defined by
// them (the "generalized_schlick_bsdf" of MaterialX, glTF and UsdPreviewSurface; the reflectance at 0 and at 90 degrees of a
// Maxwell BSDF; the cloth model of Filament):
//
//   GeneralizedSchlickBxDF   a microfacet reflection whose Fresnel term goes from the colour `color0` along the normal to
//                            `color90` at grazing: F = color0 + (color90 - color0) s(cos), with s = (1 - cos)^exponent
//                            (Schlick), or, with `curveEta`, the curve of the dielectric of that index,
//                            s = (F_eta(cos) - F_eta(1)) / (1 - F_eta(1)) (the "Nd" of a Maxwell BSDF: the index shapes the
//                            curve between the two colours). Any distribution; one reflection.
//   VelvetBxDF               the cloth lobe of Filament (R. Guy, M. Agopian, "Physically Based Rendering in Filament"): the
//                            inverted Gaussian distribution of Ashikhmin and Premoze, "Distribution-based BRDFs" (2007),
//                                D = (1 + 4 exp(-cot^2 / alpha^2) / sin^4) / (pi (1 + 4 alpha^2)),
//                            with the visibility of Neubelt and Pettineo, 1 / (4 (cos_o + cos_i - cos_o cos_i)). Reciprocal;
//                            its albedo stays below one (lobes_test.cs) but it is a fit, not a microsurface: no masking
//                            function goes with this distribution. The sheen of this branch that is a microsurface is
//                            SGGXSheenBxDF.

using System;

namespace pbrt
{
    // GeneralizedSchlickBxDF Definition
    public sealed unsafe class GeneralizedSchlickBxDF : BxDF
    {
        public GeneralizedSchlickBxDF() { }

        public GeneralizedSchlickBxDF(MicrofacetDistribution mfDistrib, SampledSpectrum color0, SampledSpectrum color90, Float exponent, Float curveEta)
        {
            this.mfDistrib = mfDistrib;
            this.color0 = color0;
            this.color90 = color90;
            this.exponent = exponent;
            this.curveEta = curveEta;
            if (curveEta > 1)
            {
                normal = FrDielectric(1, curveEta);
            }
        }

        public SampledSpectrum Fresnel(Float cosine)
        {
            cosine = Clamp(cosine, 0, 1);
            Float s;
            if (curveEta > 1)
            {
                s = Clamp((FrDielectric(cosine, curveEta) - normal) / (1 - normal), 0, 1);
            }
            else
            {
                s = std.pow(1 - cosine, exponent);
            }
            return color0 + (color90 - color0) * s;
        }

        public override BxDFFlags Flags()
        {
            return mfDistrib.EffectivelySmooth() ? BxDFFlags.SpecularReflection : BxDFFlags.GlossyReflection;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z == 0)
            {
                return default;
            }
            if (mfDistrib.EffectivelySmooth())
            {
                Vector3f mirrored = new Vector3f(-wo.x, -wo.y, wo.z);
                return new BSDFSample(Fresnel(AbsCosTheta(mirrored)) / AbsCosTheta(mirrored), mirrored, 1, BxDFFlags.SpecularReflection);
            }
            Vector3f wm = mfDistrib.Sample_wm(wo, u);
            Vector3f wi = Reflect(wo, wm);
            if (!SameHemisphere(wo, wi))
            {
                return default;
            }
            Float pdf = mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm));
            Float cosTheta_o = AbsCosTheta(wo), cosTheta_i = AbsCosTheta(wi);
            if (cosTheta_i == 0 || cosTheta_o == 0)
            {
                return default;
            }
            SampledSpectrum f = mfDistrib.DG(wo, wi, wm) * Fresnel(AbsDot(wo, wm)) / (4 * cosTheta_i * cosTheta_o);
            return new BSDFSample(f, wi, pdf, BxDFFlags.GlossyReflection);
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi) || mfDistrib.EffectivelySmooth())
            {
                return default;
            }
            Float cosTheta_o = AbsCosTheta(wo), cosTheta_i = AbsCosTheta(wi);
            if (cosTheta_i == 0 || cosTheta_o == 0)
            {
                return default;
            }
            Vector3f wm = wi + wo;
            if (LengthSquared(wm) == 0)
            {
                return default;
            }
            wm = Normalize(wm);
            return mfDistrib.DG(wo, wi, wm) * Fresnel(AbsDot(wo, wm)) / (4 * cosTheta_i * cosTheta_o);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi) || mfDistrib.EffectivelySmooth())
            {
                return 0;
            }
            Vector3f wm = wo + wi;
            if (LengthSquared(wm) == 0)
            {
                return 0;
            }
            wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));
            return mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm));
        }

        public static string Name() { return "GeneralizedSchlickBxDF"; }

        public override void Regularize() { mfDistrib.Regularize(); }

        public override string ToString()
        {
            return StringPrintf("[ GeneralizedSchlickBxDF mfDistrib: %s color0: %s color90: %s exponent: %f curveEta: %f ]", mfDistrib, color0, color90, exponent, curveEta);
        }

        private MicrofacetDistribution mfDistrib;
        private SampledSpectrum color0, color90;
        private Float exponent = 5, curveEta = 0, normal = 0;
    }

    // VelvetBxDF Definition
    public sealed unsafe class VelvetBxDF : BxDF
    {
        public VelvetBxDF() { }

        public VelvetBxDF(SampledSpectrum color, Float alpha)
        {
            this.color = color;
            this.alpha = Clamp(alpha, 0.02f, 1);
        }

        public override BxDFFlags Flags() { return BxDFFlags.DiffuseReflection; }

        public static Float D(Float cosine, Float alpha)
        {
            Float cos2 = cosine * cosine, sin2 = std.max<Float>(1 - cos2, 1e-6f);
            Float a2 = alpha * alpha;
            return (1 + 4 * std.exp(-cos2 / (sin2 * a2)) / (sin2 * sin2)) / (Pi * (1 + 4 * a2));
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (!SameHemisphere(wo, wi))
            {
                return default;
            }
            Float cosTheta_o = AbsCosTheta(wo), cosTheta_i = AbsCosTheta(wi);
            if (cosTheta_i == 0 || cosTheta_o == 0)
            {
                return default;
            }
            Vector3f wm = wi + wo;
            if (LengthSquared(wm) == 0)
            {
                return default;
            }
            wm = Normalize(wm);
            return color * (D(AbsCosTheta(wm), alpha) / (4 * (cosTheta_o + cosTheta_i - cosTheta_o * cosTheta_i)));
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || wo.z == 0)
            {
                return default;
            }
            // (the lobe is wide and peaks at grazing: the cosine and the uniform hemisphere, half each)
            Vector3f wi = uc < 0.5f ? SampleCosineHemisphere(u) : SampleUniformHemisphere(u);
            if (wo.z < 0)
            {
                wi.z = -wi.z;
            }
            Float pdf = PDF(wo, wi, mode, sampleFlags);
            SampledSpectrum value = f(wo, wi, mode);
            if (pdf == 0 || !(bool)value)
            {
                return default;
            }
            return new BSDFSample(value, wi, pdf, BxDFFlags.DiffuseReflection);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi))
            {
                return 0;
            }
            return 0.5f * AbsCosTheta(wi) * InvPi + 0.5f * Inv2Pi;
        }

        public static string Name() { return "VelvetBxDF"; }

        public override void Regularize() { }

        public override string ToString() { return StringPrintf("[ VelvetBxDF color: %s alpha: %f ]", color, alpha); }

        private SampledSpectrum color;
        private Float alpha = 0.5f;
    }
}
