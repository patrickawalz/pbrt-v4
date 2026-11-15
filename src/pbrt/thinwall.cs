// (not in pbrt-v4) thinwall.cs
//
// The lobes of the thin-walled mode of OpenPBR (Portsmouth, Kutz, Hill, "OpenPBR: Novel Features and Implementation Details",
// section 7): a sheet with no interior that looks the same from either side.
//   ThinSheetBxDF        a thin sheet of dielectric: the ladder of reflections between its two faces with the absorption of
//                        the sheet inside it, smooth or rough
//   TranslucentEonBxDF   the thin sheet of dense scattering material: diffuse reflection and diffuse transmission, shared out
//                        by the anisotropy of the scattering (the paper's Eq. 94), each an EON lobe
//   LeavingBxDF          the base of a thin wall under dielectric layers: what goes through it leaves into the air
//   OrientedBxDF         any BxDF seen from below as it is from above (the paper: "the surface is automatically oriented so
//                        that the incident ray is always entering the surface from the top")

using System;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    // A thin sheet of dielectric of the index eta. One face reflects R (Fresnel) and lets 1 - R through; a crossing of the sheet
    // keeps a = colour^(1 / cos theta_t) of the light (colour: the transmittance along the normal, the path in the sheet is
    // longer at an angle). The sum of the ladder:
    //   reflected    R + (1 - R)^2 R a^2 / (1 - R^2 a^2)
    //   transmitted  (1 - R)^2 a / (1 - R^2 a^2)
    // (pbrt's ThinDielectricBxDF is this with a = 1.) Nothing is refracted: the transmitted light goes straight on.
    //
    // Rough: there is no worked-out model (the paper says so). Here, as Kulla and Conty do, the two lobes are microfacet lobes:
    // the reflected one that of the face, the transmitted one a reflection lobe mirrored below the sheet. Its roughness is that of
    // the face times (eta - 1) / sqrt(2): for small slopes a face tilted by d sends the ray on, after both faces, turned by
    // (eta - 1) (d - d'), d' the tilt of the other face, where the reflection turns it by 2 d. The ladder is evaluated at the
    // angle to the microfacet.
    public sealed unsafe class ThinSheetBxDF : BxDF
    {
        public ThinSheetBxDF() { }

        public ThinSheetBxDF(Float eta, Float alphaX, Float alphaY, SampledSpectrum colour)
        {
            this.eta = eta;
            this.colour = colour;
            Float through = std.min<Float>((eta - 1) / std.sqrt(2f), 1);
            reflected = new TrowbridgeReitzDistribution(alphaX, alphaY);
            transmitted = new TrowbridgeReitzDistribution(alphaX * through, alphaY * through);
        }

        public static string Name() { return "ThinSheetBxDF"; }

        public override string ToString() { return StringPrintf("[ ThinSheetBxDF eta: %f colour: %s ]", eta, colour); }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.Reflection | BxDFFlags.Transmission | (reflected.EffectivelySmooth() ? BxDFFlags.Specular : BxDFFlags.Glossy);
        }

        public override void Regularize()
        {
            reflected.Regularize();
            transmitted.Regularize();
        }

        // The ladder for each of the two polarisations (s: perpendicular to the plane of incidence, p: in it), which keep to
        // themselves from one face to the other: what the sheet reflects (Rs, Rp) and transmits (Ts, Tp) of each, and the terms
        // that keep the phase between them (Cr, Ct: the third and fourth diagonal elements of the Mueller matrices). With r the
        // signed amplitude of the reflection of a face from outside (-r from inside; the two crossings of a face multiply to
        // 1 - r^2) and a the transmittance of one crossing of the sheet:
        //   Cr = r_s r_p + (1 - R_s)(1 - R_p) r_s r_p a^2 / (1 - R_s R_p a^2),   Ct = (1 - R_s)(1 - R_p) a / (1 - R_s R_p a^2)
        // The orders add without interference, so the sheet depolarises a little. (With a = 1 these are the terms that the
        // polarised integrator has for pbrt's ThinDielectricBxDF.)
        private void Ladders(Float cosine, out SampledSpectrum Rs, out SampledSpectrum Rp, out SampledSpectrum Ts, out SampledSpectrum Tp, out SampledSpectrum Cr,
                             out SampledSpectrum Ct)
        {
            cosine = Clamp(cosine, 0, 1);
            Float Fs, Fp, Ds, Dp;
            FrDielectricPolarised(cosine, eta, out Fs, out Fp, out Ds, out Dp);
            Float rs = std.sqrt(Fs) * std.cos(Ds), rp = std.sqrt(Fp) * std.cos(Dp);
            Float cosInside = SafeSqrt(1 - (1 - cosine * cosine) / (eta * eta));
            Rs = new SampledSpectrum(0f);
            Rp = new SampledSpectrum(0f);
            Ts = new SampledSpectrum(0f);
            Tp = new SampledSpectrum(0f);
            Cr = new SampledSpectrum(0f);
            Ct = new SampledSpectrum(0f);
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                Float a = colour[c] >= 1 ? 1 : (colour[c] <= 0 || cosInside <= 0 ? 0 : std.pow(colour[c], 1 / cosInside));
                Float betweenS = 1 - Fs * Fs * a * a, betweenP = 1 - Fp * Fp * a * a, between = 1 - Fs * Fp * a * a;
                Rs[c] = betweenS > 0 ? Fs + (1 - Fs) * (1 - Fs) * Fs * a * a / betweenS : 1;
                Rp[c] = betweenP > 0 ? Fp + (1 - Fp) * (1 - Fp) * Fp * a * a / betweenP : 1;
                Ts[c] = betweenS > 0 ? (1 - Fs) * (1 - Fs) * a / betweenS : 0;
                Tp[c] = betweenP > 0 ? (1 - Fp) * (1 - Fp) * a / betweenP : 0;
                Cr[c] = rs * rp + (between > 0 ? (1 - Fs) * (1 - Fp) * rs * rp * a * a / between : 0);
                Ct[c] = between > 0 ? (1 - Fs) * (1 - Fp) * a / between : 0;
            }
        }

        // What the sheet reflects and transmits of light that is not polarised, at the cosine given to the normal of its faces:
        // the mean of the two polarisations (pbrt's ThinDielectricBxDF takes the ladder of the mean reflectance instead, which
        // is not the same)
        public void Sheet(Float cosine, out SampledSpectrum R, out SampledSpectrum T)
        {
            SampledSpectrum Rs, Rp, Ts, Tp, Cr, Ct;
            Ladders(cosine, out Rs, out Rp, out Ts, out Tp, out Cr, out Ct);
            R = (Rs + Rp) * 0.5f;
            T = (Ts + Tp) * 0.5f;
        }

        // The Mueller matrix of the scattering from wo to wi, with a first element of 1 (the value of f carries the rest). The
        // plane of incidence is that of wo and the microfacet; for the light that goes through, the frame at wi is the one of
        // the reflection mirrored below the sheet with the direction.
        private bool Matrix(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out Attenuation matrix)
        {
            matrix = default;
            bool reflect = wo.z * wi.z > 0;
            Vector3f o = Above(wo), i = Above(wi);
            Vector3f wm = o + i;
            wm = LengthSquared(wm) == 0 ? new Vector3f(0, 0, 1) : Normalize(wm);
            SampledSpectrum Rs, Rp, Ts, Tp, Cr, Ct;
            Ladders(AbsDot(o, wm), out Rs, out Rp, out Ts, out Tp, out Cr, out Ct);
            SampledSpectrum Is = reflect ? Rs : Ts, Ip = reflect ? Rp : Tp, C = reflect ? Cr : Ct;
            SampledSpectrum one = new SampledSpectrum(1f), zero = new SampledSpectrum(0f), b = new SampledSpectrum(0f), c = new SampledSpectrum(0f);
            for (int k = 0; k < NSpectrumSamples; ++k)
            {
                Float mean = 0.5f * (Is[k] + Ip[k]);
                if (!(mean > 0))
                {
                    return false;
                }
                b[k] = 0.5f * (Is[k] - Ip[k]) / mean;
                c[k] = C[k] / mean;
            }
            Float side = wo.z > 0 ? 1 : -1;
            Vector3f normalAtWo = new Vector3f(wm.x, wm.y, side * wm.z);
            Vector3f normalAtWi = reflect ? normalAtWo : new Vector3f(wm.x, wm.y, -side * wm.z);
            matrix = new Attenuation(new MuellerMatrix(
                one, b, zero, zero,
                b, one, zero, zero,
                zero, zero, c, zero,
                zero, zero, zero, c));
            matrix = matrix.Scattered(bsdf.LocalToRender(wo), bsdf.LocalToRender(wi), bsdf.LocalToRender(normalAtWo), bsdf.LocalToRender(normalAtWi),
                                      mode == TransportMode.Radiance);
            return !matrix.HasNaNs();
        }

        public override bool HasMuellerMatrix() { return true; }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            SampledSpectrum value = f(wo, wi, mode);
            if ((bool)value)
            {
                polarised = Matrix(bsdf, wo, wi, mode, out matrix);
            }
            return value;
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                           out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            pstd.optional<BSDFSample> sampled = Sample_f(wo, uc, u, mode, sampleFlags);
            if ((bool)sampled && (bool)sampled.value().f)
            {
                polarised = Matrix(bsdf, wo, sampled.value().wi, mode, out matrix);
            }
            return sampled;
        }

        private void Choice(Vector3f wo, BxDFReflTransFlags sampleFlags, out Float pr, out Float pt)
        {
            SampledSpectrum R, T;
            Sheet(AbsCosTheta(wo), out R, out T);
            pr = (sampleFlags & BxDFReflTransFlags.Reflection) != 0 ? R.Average() : 0;
            pt = (sampleFlags & BxDFReflTransFlags.Transmission) != 0 ? T.Average() : 0;
        }

        private static Vector3f Above(Vector3f w) { return new Vector3f(w.x, w.y, std.abs(w.z)); }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (reflected.EffectivelySmooth() || wo.z == 0 || wi.z == 0)
            {
                return new SampledSpectrum(0f);
            }
            bool reflect = wo.z * wi.z > 0;
            Vector3f o = Above(wo), i = Above(wi);
            Vector3f wm = o + i;
            if (LengthSquared(wm) == 0)
            {
                return new SampledSpectrum(0f);
            }
            wm = Normalize(wm);
            SampledSpectrum R, T;
            Sheet(AbsDot(o, wm), out R, out T);
            TrowbridgeReitzDistribution distribution = reflect ? reflected : transmitted;
            if (distribution.EffectivelySmooth())
            {
                return new SampledSpectrum(0f);
            }
            return (reflect ? R : T) * (distribution.D(wm) * distribution.G(o, i) / (4 * o.z * i.z));
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0)
            {
                return default;
            }
            Float pr, pt;
            Choice(wo, sampleFlags, out pr, out pt);
            if (pr == 0 && pt == 0)
            {
                return default;
            }
            bool reflect = uc < pr / (pr + pt);
            Float chosen = (reflect ? pr : pt) / (pr + pt);
            Vector3f o = Above(wo);
            TrowbridgeReitzDistribution distribution = reflect ? reflected : transmitted;
            Vector3f i;
            SampledSpectrum value;
            Float pdf;
            bool specular = distribution.EffectivelySmooth();
            if (specular)
            {
                SampledSpectrum R, T;
                Sheet(o.z, out R, out T);
                i = new Vector3f(-o.x, -o.y, o.z);
                value = (reflect ? R : T) / o.z;
                pdf = chosen;
            }
            else
            {
                Vector3f wm = distribution.Sample_wm(o, u);
                i = Reflect(o, wm);
                if (!(i.z > 0))
                {
                    return default;
                }
                SampledSpectrum R, T;
                Sheet(AbsDot(o, wm), out R, out T);
                value = (reflect ? R : T) * (distribution.D(wm) * distribution.G(o, i) / (4 * o.z * i.z));
                pdf = chosen * distribution.PDF(o, wm) / (4 * AbsDot(o, wm));
            }
            // the direction: on the side of wo if reflected, on the other if transmitted
            Float side = (wo.z > 0) == reflect ? 1 : -1;
            Vector3f wi = new Vector3f(i.x, i.y, side * i.z);
            BxDFFlags flags = reflect ? (specular ? BxDFFlags.SpecularReflection : BxDFFlags.GlossyReflection)
                                      : (specular ? BxDFFlags.SpecularTransmission : BxDFFlags.GlossyTransmission);
            return new BSDFSample(value, wi, pdf, flags);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            bool reflect = wo.z * wi.z > 0;
            TrowbridgeReitzDistribution distribution = reflect ? reflected : transmitted;
            if (distribution.EffectivelySmooth())
            {
                return 0;
            }
            Float pr, pt;
            Choice(wo, sampleFlags, out pr, out pt);
            if (pr == 0 && pt == 0)
            {
                return 0;
            }
            Vector3f o = Above(wo), i = Above(wi);
            Vector3f wm = o + i;
            if (LengthSquared(wm) == 0)
            {
                return 0;
            }
            wm = Normalize(wm);
            return (reflect ? pr : pt) / (pr + pt) * distribution.PDF(o, wm) / (4 * AbsDot(o, wm));
        }

        private Float eta = 1.5f;
        private SampledSpectrum colour = new SampledSpectrum(1f);
        private TrowbridgeReitzDistribution reflected, transmitted;
    }

    // The thin sheet of dense scattering material: of the light it scatters (the fraction S, by wavelength) the half (1 - g) is
    // reflected and the half (1 + g) transmitted, g the anisotropy of the scattering, each as an EON lobe of the roughness
    // given, the transmitted one below the sheet.
    public sealed unsafe class TranslucentEonBxDF : BxDF
    {
        public TranslucentEonBxDF() { }

        public TranslucentEonBxDF(SampledSpectrum S, Float g, Float roughness)
        {
            back = 0.5f * (1 - Clamp(g, -1, 1));
            lobe = new EonDiffuseBxDF(S, roughness);
        }

        public static string Name() { return "TranslucentEonBxDF"; }

        public override string ToString() { return StringPrintf("[ TranslucentEonBxDF reflected: %f of: %s ]", back, lobe); }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.Diffuse | (back > 0 ? BxDFFlags.Reflection : BxDFFlags.Unset) | (back < 1 ? BxDFFlags.Transmission : BxDFFlags.Unset);
        }

        public override void Regularize()
        {
        }

        private static Vector3f Above(Vector3f w) { return new Vector3f(w.x, w.y, std.abs(w.z)); }

        private Float Share(bool reflect, BxDFReflTransFlags sampleFlags)
        {
            Float pr = (sampleFlags & BxDFReflTransFlags.Reflection) != 0 ? back : 0;
            Float pt = (sampleFlags & BxDFReflTransFlags.Transmission) != 0 ? 1 - back : 0;
            return pr + pt > 0 ? (reflect ? pr : pt) / (pr + pt) : 0;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return new SampledSpectrum(0f);
            }
            return lobe.f(Above(wo), Above(wi), mode) * (wo.z * wi.z > 0 ? back : 1 - back);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0)
            {
                return default;
            }
            Float pr = Share(true, sampleFlags), pt = Share(false, sampleFlags);
            if (pr == 0 && pt == 0)
            {
                return default;
            }
            bool reflect = uc < pr;
            Float chosen = reflect ? pr : pt;
            Float remapped = reflect ? uc / pr : (uc - pr) / pt;
            pstd.optional<BSDFSample> sampled = lobe.Sample_f(Above(wo), std.min(remapped, OneMinusEpsilon), u, mode, BxDFReflTransFlags.Reflection);
            if (!sampled)
            {
                return default;
            }
            BSDFSample bs = sampled.value();
            Float side = (wo.z > 0) == reflect ? 1 : -1;
            Vector3f wi = new Vector3f(bs.wi.x, bs.wi.y, side * std.abs(bs.wi.z));
            return new BSDFSample(bs.f * (reflect ? back : 1 - back), wi, bs.pdf * chosen, reflect ? BxDFFlags.DiffuseReflection : BxDFFlags.DiffuseTransmission);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            return Share(wo.z * wi.z > 0, sampleFlags) * lobe.PDF(Above(wo), Above(wi), mode, BxDFReflTransFlags.Reflection);
        }

        private Float back = 0.5f;
        private EonDiffuseBxDF lobe;
    }

    // The base of a thin wall under layers of dielectric (the gloss, the coat): the light that goes through it leaves into the
    // air below without crossing those layers again. pbrt's dielectric interfaces divide the radiance that enters them by the
    // square of the index (the radiance in a medium is larger by it, and a path of radiance carries the inverse), and the
    // interface on the way out undoes it; here there is none, so the transmission of the base undoes it. `eta` is the product
    // of the relative indices of the interfaces above. (wo above: the path of radiance goes down through the base.)
    public sealed unsafe class LeavingBxDF : BxDF
    {
        public LeavingBxDF() { }

        public LeavingBxDF(BxDF inner, Float eta)
        {
            this.inner = inner;
            this.eta = eta;
        }

        public static string Name() { return "LeavingBxDF"; }

        public override string ToString() { return StringPrintf("[ LeavingBxDF eta: %f of: %s ]", eta, inner); }
        public override BxDFFlags Flags() { return inner.Flags(); }
        public override void Regularize() { inner.Regularize(); }

        private Float Scale(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (wo.z * wi.z > 0 || mode != TransportMode.Radiance)
            {
                return 1;
            }
            // (down through the base: the factor of the layers above is undone; up through it, towards them: it is given
            // beforehand, for the interface that the light then leaves by and that multiplies by the square of the index)
            return wo.z > 0 ? eta * eta : 1 / (eta * eta);
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode) { return inner.f(wo, wi, mode) * Scale(wo, wi, mode); }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            pstd.optional<BSDFSample> sampled = inner.Sample_f(wo, uc, u, mode, sampleFlags);
            if (!sampled)
            {
                return default;
            }
            BSDFSample bs = sampled.value();
            bs.f *= Scale(wo, bs.wi, mode);
            return bs;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return inner.PDF(wo, wi, mode, sampleFlags);
        }

        public override bool HasMuellerMatrix() { return LobeMatrices.Has(inner); }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            return LobeMatrices.f(inner, bsdf, wo, wi, mode, out polarised, out matrix) * Scale(wo, wi, mode);
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                           out bool polarised, out Attenuation matrix)
        {
            pstd.optional<BSDFSample> sampled = LobeMatrices.Sample_f(inner, bsdf, wo, uc, u, mode, sampleFlags, out polarised, out matrix);
            if (!sampled)
            {
                return default;
            }
            BSDFSample bs = sampled.value();
            bs.f *= Scale(wo, bs.wi, mode);
            return bs;
        }

        private BxDF inner;
        private Float eta = 1;
    }

    // A BxDF seen from below as it is from above: the directions are mirrored in the surface when wo is below it. The layers of
    // a thin wall (coat, fuzz, gloss) are then on the side of wo, whichever it is, and the light that goes through leaves from
    // the base without meeting them again: the approximation that the paper proposes. Reflection is exactly the same on both
    // sides. Transmission is not reciprocal where the layers are not the same from both sides of the base (the light meets the
    // layers on the side of wo only).
    public sealed unsafe class OrientedBxDF : BxDF
    {
        public OrientedBxDF() { }
        public OrientedBxDF(BxDF inner) { this.inner = inner; }

        public static string Name() { return "OrientedBxDF"; }

        public override string ToString() { return StringPrintf("[ OrientedBxDF of: %s ]", inner); }
        public override BxDFFlags Flags() { return inner.Flags(); }
        public override void Regularize() { inner.Regularize(); }

        public BxDF Inner() { return inner; }

        private static Vector3f Mirror(Vector3f w) { return new Vector3f(w.x, w.y, -w.z); }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return wo.z < 0 ? inner.f(Mirror(wo), Mirror(wi), mode) : inner.f(wo, wi, mode);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (!(wo.z < 0))
            {
                return inner.Sample_f(wo, uc, u, mode, sampleFlags);
            }
            pstd.optional<BSDFSample> sampled = inner.Sample_f(Mirror(wo), uc, u, mode, sampleFlags);
            if (!sampled)
            {
                return default;
            }
            BSDFSample bs = sampled.value();
            bs.wi = Mirror(bs.wi);
            return bs;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return wo.z < 0 ? inner.PDF(Mirror(wo), Mirror(wi), mode, sampleFlags) : inner.PDF(wo, wi, mode, sampleFlags);
        }

        // The matrices of the lobe: with the directions mirrored it is given the frame of the surface mirrored too, so that the
        // frames of its matrices are those of the directions as they are
        public override bool HasMuellerMatrix() { return LobeMatrices.Has(inner); }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            return wo.z < 0 ? LobeMatrices.f(inner, bsdf.Mirrored(), Mirror(wo), Mirror(wi), mode, out polarised, out matrix)
                            : LobeMatrices.f(inner, bsdf, wo, wi, mode, out polarised, out matrix);
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                           out bool polarised, out Attenuation matrix)
        {
            if (!(wo.z < 0))
            {
                return LobeMatrices.Sample_f(inner, bsdf, wo, uc, u, mode, sampleFlags, out polarised, out matrix);
            }
            pstd.optional<BSDFSample> sampled = LobeMatrices.Sample_f(inner, bsdf.Mirrored(), Mirror(wo), uc, u, mode, sampleFlags, out polarised, out matrix);
            if (!sampled)
            {
                return default;
            }
            BSDFSample bs = sampled.value();
            bs.wi = Mirror(bs.wi);
            return bs;
        }

        private BxDF inner;
    }
}
