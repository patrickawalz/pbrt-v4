// (not in pbrt-v4) sheen_retro.cs
//
// Two lobes of the C++ fork as BxDFs of their own (there the first is a medium inside LayeredBxDF and the second a setting
// of DielectricBxDF and ConductorBxDF), for a material to use alone or to compose:
//
// SGGXSheenBxDF: a slab of fibre microflakes, the volumetric sheen of Zeltner, Burley, Chiang, "Practical Multiple-Scattering
// Sheen Using Linearly Transformed Cosines" (SIGGRAPH 2022 Talks) with the SGGX microflake phase function of Heitz, Dupuy,
// Crassin, Dachsbacher, "The SGGX Microflake Distribution" (ACM Trans. Graph. 34(4), 2015), fibres along the normal:
// S = diag(1, 1, alpha^2), mirror flakes.
//
// RetroreflectiveBxDF: the Minimal Retroreflective Microfacet Model of Portsmouth, Raab, Belcour, Liu (JCGT 15(1), 2026): a
// microfacet lobe evaluated at the mirrored view direction v' = reflect(v, n) (local frame: (-x, -y, z)) is retroreflective,
// and a partially retroreflective material is the blend f(v, l) = (1 - rho) f0(v, l) + rho f0(v', l).

using System;

namespace pbrt
{
    // SGGX fibre microflake phase function (Heitz & Dupuy 2015), normal-aligned fibres S = diag(1, 1, alpha^2) (Zeltner et al.
    // 2022 volumetric sheen), mirror-reflecting flakes.
    //   sigma(w)  = sqrt(S_xx wx^2 + S_yy wy^2 + S_zz wz^2)            (projected area)
    //   D(m)      = 1 / (pi sqrt(det S) (m^T S^-1 m)^2)
    //   p(wo, wi) = D(h) / (4 sigma(wo)),  h = normalize(wo + wi)       (wo, wi point away)
    // Flake normals are sampled from the exact visible-normal distribution D_wo(m) = <wo, m>_+ D(m) / sigma(wo), so pdf == p
    // and every scattering event has weight 1.
    public unsafe struct SGGXFibrePhase
    {
        public Float alpha;

        public SGGXFibrePhase(Float alpha)
        {
            this.alpha = alpha;
        }

        public Float Sigma(Vector3f w)
        {
            return std.sqrt(w.x * w.x + w.y * w.y + alpha * alpha * w.z * w.z);
        }

        public Float D(Vector3f m)
        {
            Float q = m.x * m.x + m.y * m.y + m.z * m.z / (alpha * alpha);
            return 1 / (pbrt.Pi * alpha * q * q);
        }

        public Float p(Vector3f wo, Vector3f wi)
        {
            Vector3f h = wo + wi;
            Float l2 = pbrt.LengthSquared(h);
            if (l2 == 0)
            {
                return 0;
            }
            h /= std.sqrt(l2);
            return D(h) / (4 * Sigma(wo));
        }

        // Visible flake normal sample (Heitz & Dupuy 2015, Listing 1)
        public Vector3f SampleVisibleNormal(Vector3f wi, Point2f u)
        {
            wi = pbrt.Normalize(wi);  // basis must be exactly orthonormal; drift compounds over long walks
            Float r = std.sqrt(u.x), phi = 2 * pbrt.Pi * u.y;
            Float u1 = r * std.cos(phi), u2 = r * std.sin(phi);
            // orthonormal basis (wk, wj, wi)
            Vector3f wk, wj;
            pbrt.CoordinateSystem(wi, &wk, &wj);
            // S projected into this basis; S = diag(1, 1, alpha^2)
            Float Skk = Sdot(wk, wk), Sjj = Sdot(wj, wj), Sii = Sdot(wi, wi);
            Float Skj = Sdot(wk, wj), Ski = Sdot(wk, wi), Sji = Sdot(wj, wi);
            Float sqrtDetSkji = std.sqrt(std.abs(Skk * Sjj * Sii - Skk * Sji * Sji - Sjj * Ski * Ski - Sii * Skj * Skj + 2 * Skj * Ski * Sji));
            Float invSqrtSii = 1 / std.sqrt(Sii);
            Float tmp = std.sqrt(std.max<Float>(Sjj * Sii - Sji * Sji, 1e-30f));
            Vector3f Mk = new Vector3f(sqrtDetSkji / tmp, 0, 0);
            Vector3f Mj = new Vector3f(-invSqrtSii * (Ski * Sji - Skj * Sii) / tmp, invSqrtSii * tmp, 0);
            Vector3f Mi = new Vector3f(invSqrtSii * Ski, invSqrtSii * Sji, invSqrtSii * Sii);
            Float u3 = std.sqrt(std.max<Float>(0, 1 - u1 * u1 - u2 * u2));
            Vector3f wm = pbrt.Normalize(u1 * Mk + u2 * Mj + u3 * Mi);
            return pbrt.Normalize(wm.x * wk + wm.y * wj + wm.z * wi);
        }

        private Float Sdot(Vector3f a, Vector3f b)
        {
            return a.x * b.x + a.y * b.y + alpha * alpha * a.z * b.z;
        }

        // The direction of the light after a flake: false if the sample is not one
        public bool Sample_p(Vector3f wo, Point2f u, out Vector3f wi)
        {
            wo = pbrt.Normalize(wo);
            Vector3f m = SampleVisibleNormal(wo, u);
            wi = pbrt.Normalize(2 * pbrt.Dot(wo, m) * m - wo);
            Float pdf = p(wo, wi);
            return pdf > 0 && pdf < pbrt.Infinity;
        }
    }

    // SGGXSheenBxDF Definition
    // A slab of the fibres without interfaces, of optical thickness `thickness` along the normal for the unit of the projected
    // area of the flakes: along w the light is extinguished at the rate sigma(w), so the slab passes exp(-thickness sigma(w) /
    // |cos w|) of it undisturbed (a specular transmission: the lobe is a layer for what is under it), and the flakes scatter the
    // rest with the albedo `color`, back out of the side it came in (the reflection) or through (a glossy transmission).
    //   f = the single scattering in closed form + the scattering of higher order, by a random walk with next-event
    //       estimation (an unbiased estimate, the same for the same pair of directions);
    //   Sample_f = the walk itself: nothing is lost with a white albedo.
    public sealed unsafe class SGGXSheenBxDF : BxDF
    {
        public SGGXSheenBxDF() { }
        public SGGXSheenBxDF(SampledSpectrum color, Float alpha, Float thickness, int maxDepth)
        {
            this.color = color;
            phase = new SGGXFibrePhase(std.max<Float>(alpha, 1e-3f));
            this.thickness = std.max<Float>(thickness, 0);
            this.maxDepth = maxDepth;
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.GlossyReflection | BxDFFlags.GlossyTransmission | BxDFFlags.SpecularTransmission;
        }

        public override void Regularize() { }

        public static string Name() { return "SGGXSheenBxDF"; }

        public override string ToString()
        {
            return StringPrintf("[ SGGXSheenBxDF color: %s alpha: %f thickness: %f maxDepth: %d ]", color, phase.alpha, thickness, maxDepth);
        }

        // The rate of extinction per unit of depth along w
        private Float Rate(Vector3f w)
        {
            return phase.Sigma(w) / std.max<Float>(AbsCosTheta(w), 1e-7f);
        }

        // The part of the light along w that the slab passes undisturbed
        public Float Unscattered(Vector3f w)
        {
            return FastExp(-thickness * Rate(w));
        }

        // The light scattered once, in closed form. The depth z of the flake runs from 0 (the side of wo) to the thickness:
        //   f = color D(h) / (4 |cos o| |cos i|) Integral exp(-a_o z) exp(-a_i z') dz,  z' = z (reflection) or thickness - z
        private SampledSpectrum SingleScatter(Vector3f wo, Vector3f wi)
        {
            Vector3f h = wo + wi;
            Float l2 = LengthSquared(h);
            if (l2 == 0)
            {
                return new SampledSpectrum(0f);
            }
            h /= std.sqrt(l2);
            Float ao = Rate(wo), ai = Rate(wi);
            Float integral;
            if (SameHemisphere(wo, wi))
            {
                integral = (1 - FastExp(-thickness * (ao + ai))) / (ao + ai);
            }
            else
            {
                Float difference = ao - ai;
                if (std.abs(difference) * thickness < 1e-3f)
                {
                    integral = thickness * FastExp(-thickness * 0.5f * (ao + ai));
                }
                else
                {
                    integral = (FastExp(-thickness * ai) - FastExp(-thickness * ao)) / difference;
                }
            }
            return color * (phase.D(h) * integral / (4 * AbsCosTheta(wo) * AbsCosTheta(wi)));
        }

        // The light scattered more than once: a walk from wo, and at each flake after the first the light of wi that the
        // flake sends along the walk, through what is left of the slab
        private SampledSpectrum MultipleScatter(Vector3f wo, Vector3f wi, ref RNG rng)
        {
            SampledSpectrum value = new SampledSpectrum(0f);
            // z = 0 is the side of wo: the walk goes in along -wo, seen from a frame where wo points up
            Float side = wo.z > 0 ? 1 : -1;
            Vector3f d = -side * wo;
            Vector3f toLight = side * wi;
            Float z = 0;
            SampledSpectrum beta = new SampledSpectrum(1f);
            Float rateLight = Rate(toLight);
            for (int depth = 0; depth < maxDepth; ++depth)
            {
                // The distance to the next flake along d
                Float sigma = phase.Sigma(d);
                Float distance = -std.log(std.max<Float>(1 - rng.Uniform<Float>(), 1e-30f)) / sigma;
                z -= distance * d.z;
                if (z <= 0 || z >= thickness)
                {
                    break;
                }
                beta *= color;
                if (depth > 0)
                {
                    // towards the light: out of the top (z = 0) if the light is on the side of wo, else out of the bottom
                    Float left = toLight.z > 0 ? z : thickness - z;
                    Float pValue = phase.p(-d, toLight);
                    SampledSpectrum contribution = beta * (pValue * FastExp(-left * rateLight) / std.max<Float>(AbsCosTheta(toLight), 1e-7f));
                    if (IsFinite(contribution[0]))
                    {
                        value += contribution;
                    }
                }
                Vector3f next;
                if (!phase.Sample_p(-d, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), out next))
                {
                    break;
                }
                d = next;
                if (!(bool)beta)
                {
                    break;
                }
            }
            return value;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (wo.z == 0 || wi.z == 0 || thickness == 0)
            {
                return new SampledSpectrum(0f);
            }
            SampledSpectrum value = SingleScatter(wo, wi);
            if (maxDepth > 1)
            {
                // (the same walk for a pair of directions and for the pair the other way round would not be the same estimate:
                // the two are equal in the mean)
                RNG rng = new RNG(Hash(GetOptions().seed, wo), Hash(wi));
                value += MultipleScatter(wo, wi, ref rng);
            }
            return value;
        }

        // The walk: undisturbed (the specular transmission), or scattered out of one side
        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0)
            {
                return default;
            }
            if (thickness == 0)
            {
                if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                {
                    return default;
                }
                return new BSDFSample(new SampledSpectrum(1 / AbsCosTheta(wo)), -wo, 1, BxDFFlags.SpecularTransmission);
            }
            RNG rng = new RNG(Hash(GetOptions().seed, wo, uc), Hash(u));
            Float side = wo.z > 0 ? 1 : -1;
            Vector3f d = -side * wo;
            Float z = 0;
            SampledSpectrum beta = new SampledSpectrum(1f);
            bool first = true;
            for (int depth = 0; depth <= maxDepth; ++depth)
            {
                Float sigma = phase.Sigma(d);
                Float distance = -std.log(std.max<Float>(1 - (first ? uc : rng.Uniform<Float>()), 1e-30f)) / sigma;
                z -= distance * d.z;
                if (z <= 0 || z >= thickness)
                {
                    Vector3f wi = side * d;
                    if (depth == 0)
                    {
                        // Nothing in the way
                        if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                        {
                            return default;
                        }
                        return new BSDFSample(new SampledSpectrum(1 / AbsCosTheta(wo)), -wo, 1, BxDFFlags.SpecularTransmission);
                    }
                    bool reflected = z <= 0;
                    if ((sampleFlags & (reflected ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission)) == 0 || wi.z == 0)
                    {
                        return default;
                    }
                    return new BSDFSample(beta / AbsCosTheta(wi), wi, 1, reflected ? BxDFFlags.GlossyReflection : BxDFFlags.GlossyTransmission, 1f, true);
                }
                if (depth == maxDepth)
                {
                    break;
                }
                beta *= color;
                if (!(bool)beta)
                {
                    break;
                }
                Vector3f next;
                if (!phase.Sample_p(-d, first ? u : new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), out next))
                {
                    break;
                }
                d = next;
                first = false;
            }
            return default;
        }

        // The density of the scattered light, for the light sampling of an integrator to be weighted against: what the first
        // flake does (the phase function of the direction of the walk, over the flakes of the slab), with a part that is 0
        // nowhere for the rest
        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0 || wi.z == 0 || thickness == 0)
            {
                return 0;
            }
            bool reflected = SameHemisphere(wo, wi);
            if ((sampleFlags & (reflected ? BxDFReflTransFlags.Reflection : BxDFReflTransFlags.Transmission)) == 0)
            {
                return 0;
            }
            Float scattered = 1 - Unscattered(wo);
            return scattered * (0.5f * phase.p(wo, wi) + 0.25f * AbsCosTheta(wi) * InvPi);
        }

        // The slab polarises nothing that it scatters (mirror flakes without a Fresnel term), and leaves alone what it passes
        public override bool HasMuellerMatrix() { return true; }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags, out bool polarised,
                                                           out PolarisedLightSystem.Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            pstd.optional<BSDFSample> bs = Sample_f(wo, uc, u, mode, sampleFlags);
            if (bs && bs.value().IsSpecular())
            {
                polarised = true;
                matrix = PolarisedLightSystem.Attenuation.Identity();
            }
            return bs;
        }

        // SGGXSheenBxDF Private Members
        private SampledSpectrum color;
        private SGGXFibrePhase phase;
        private Float thickness;
        private int maxDepth;
    }

    // RetroreflectiveBxDF Definition
    // f(v, l) = (1 - rho) f0(v, l) + rho f0(v', l), sampled as a one-sample mixture with the probabilities (1 - rho, rho); the
    // weight of the component, f c / pdf, is then the unbiased estimator (pdfIsProportional), and PDF() is the density of the
    // mixture. Reciprocity and energy conservation follow from the lobe for a distribution of normals that is the same turned
    // by half a turn about the normal (the paper).
    //
    // A dielectric is retroreflective in its reflection only (OpenPBR issue #243: modify the BRDF, leave the BTDF unchanged):
    // f = (1 - rho) f_R(wo) + rho f_R(wo') + f_T(wo), a mixture of three components with the probabilities p_R (1 - rho),
    // p_R rho and 1 - p_R, p_R the mean Fresnel reflectance over the visible normals.
    public sealed unsafe class RetroreflectiveBxDF : BxDF
    {
        public RetroreflectiveBxDF(ConductorBxDF lobe, Float rho)
        {
            this.lobe = lobe;
            this.rho = Clamp(rho, 0, 1);
        }

        public RetroreflectiveBxDF(DielectricBxDF lobe, Float rho)
        {
            this.lobe = lobe;
            dielectric = lobe;
            this.rho = Clamp(rho, 0, 1);
        }

        public static Vector3f RetroView(Vector3f wo) { return new Vector3f(-wo.x, -wo.y, wo.z); }

        public Float Rho() { return rho; }

        public override BxDFFlags Flags() { return lobe.Flags(); }

        public override void Regularize() { lobe.Regularize(); }

        public static string Name() { return "RetroreflectiveBxDF"; }

        public override string ToString()
        {
            return StringPrintf("[ RetroreflectiveBxDF rho: %f lobe: %s ]", rho, lobe);
        }

        // Probability of choosing the reflection components of a dielectric. Any value in (0,1) is unbiased; the best one makes
        // every component weight ~1: the share of reflection in the base sampler, i.e. the mean Fresnel reflectance over the
        // visible microfacet normals. It is a pure function of wo (deterministic 16-point Hammersley quadrature of the VNDF), so
        // PDF() stays the exact mixture density. A macro-Fresnel value alone spikes the weights past the critical angle (rough
        // surfaces still transmit there).
        private Float RetroSelectR(Vector3f wo)
        {
            Float eta = dielectric.Eta();
            if (eta == 1)
            {
                return 0;
            }
            MicrofacetDistribution distribution = dielectric.Distribution();
            if (distribution.EffectivelySmooth())
            {
                return Clamp(FrDielectric(CosTheta(wo), eta), 0.02f, 0.98f);
            }
            const int K = 16;
            Float sum = 0;
            for (int i = 0; i < K; ++i)
            {
                Point2f uq = new Point2f((i + 0.5f) / K, ReverseBits32((uint)i) * 2.3283064365386963e-10f);
                Vector3f wm = distribution.Sample_wm(wo, uq);
                sum += FrDielectric(Dot(wo, wm), eta);
            }
            return Clamp(sum / K, 0.05f, 0.95f);
        }

        // The probabilities of the reflection and of the transmission of a dielectric for the flags
        private bool Selection(Vector3f wo, BxDFReflTransFlags sampleFlags, out Float pR, out Float pT)
        {
            Float a = RetroSelectR(wo);
            pR = (sampleFlags & BxDFReflTransFlags.Reflection) != 0 ? a : 0;
            pT = (sampleFlags & BxDFReflTransFlags.Transmission) != 0 ? 1 - a : 0;
            if (dielectric.Eta() == 1)
            {
                pR = 0;
                pT = (sampleFlags & BxDFReflTransFlags.Transmission) != 0 ? 1 : 0;
            }
            Float sum = pR + pT;
            if (sum == 0)
            {
                return false;
            }
            pR /= sum;
            pT /= sum;
            return true;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (dielectric != null && !SameHemisphere(wo, wi))
            {
                return lobe.f(wo, wi, mode);  // BTDF unchanged
            }
            SampledSpectrum r = new SampledSpectrum(0f);
            if (rho < 1)
            {
                r += (1 - rho) * lobe.f(wo, wi, mode);
            }
            if (rho > 0)
            {
                r += rho * lobe.f(RetroView(wo), wi, mode);
            }
            return r;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (dielectric != null)
            {
                Float pR, pT;
                if (!Selection(wo, sampleFlags, out pR, out pT))
                {
                    return 0;
                }
                if (!SameHemisphere(wo, wi))
                {
                    return pT * lobe.PDF(wo, wi, mode, BxDFReflTransFlags.Transmission);
                }
                return pR * ((1 - rho) * lobe.PDF(wo, wi, mode, BxDFReflTransFlags.Reflection) +
                             rho * lobe.PDF(RetroView(wo), wi, mode, BxDFReflTransFlags.Reflection));
            }
            Float p = 0;
            if (rho < 1)
            {
                p += (1 - rho) * lobe.PDF(wo, wi, mode, sampleFlags);
            }
            if (rho > 0)
            {
                p += rho * lobe.PDF(RetroView(wo), wi, mode, sampleFlags);
            }
            return p;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            pstd.optional<BSDFSample> bs;
            if (dielectric != null)
            {
                Float pR, pT;
                if (!Selection(wo, sampleFlags, out pR, out pT))
                {
                    return default;
                }
                if (uc < pT)
                {
                    bs = lobe.Sample_f(wo, std.min<Float>(uc / pT, OneMinusEpsilon), u, mode, BxDFReflTransFlags.Transmission);
                    if (bs)
                    {
                        BSDFSample s = bs.value();
                        s.pdf *= pT;
                        bs = s;
                    }
                }
                else
                {
                    Float v = std.min<Float>((uc - pT) / pR, OneMinusEpsilon);  // in [0,1)
                    if (v < 1 - rho)
                    {
                        Float v2 = std.min<Float>(v / (1 - rho), OneMinusEpsilon);
                        bs = lobe.Sample_f(wo, v2, u, mode, BxDFReflTransFlags.Reflection);
                        if (bs)
                        {
                            BSDFSample s = bs.value();
                            s.f *= (1 - rho);
                            s.pdf *= pR * (1 - rho);
                            bs = s;
                        }
                    }
                    else
                    {
                        Float v2 = std.min<Float>((v - (1 - rho)) / rho, OneMinusEpsilon);
                        bs = lobe.Sample_f(RetroView(wo), v2, u, mode, BxDFReflTransFlags.Reflection);
                        if (bs)
                        {
                            BSDFSample s = bs.value();
                            s.f *= rho;
                            s.pdf *= pR * rho;
                            bs = s;
                        }
                    }
                }
            }
            else
            {
                bool retro = uc < rho;
                Float uc2 = retro ? std.min<Float>(uc / rho, OneMinusEpsilon) : std.min<Float>((uc - rho) / (1 - rho), OneMinusEpsilon);
                bs = lobe.Sample_f(retro ? RetroView(wo) : wo, uc2, u, mode, sampleFlags);
            }
            if (!bs || !(bool)bs.value().f || bs.value().pdf == 0)
            {
                return default;
            }
            if (!bs.value().IsSpecular())
            {
                BSDFSample s = bs.value();
                s.pdfIsProportional = true;
                bs = s;
            }
            return bs;
        }

        // The lobe keeps its Mueller matrix for its share of the light; the retroreflected share is taken as depolarising (the
        // model says where that light goes, not by what reflections)
        public override bool HasMuellerMatrix() { return true; }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised,
                                          out PolarisedLightSystem.Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            SampledSpectrum value = f(wo, wi, mode);
            if (!(bool)value)
            {
                return value;
            }
            bool transmitted = dielectric != null && !SameHemisphere(wo, wi);
            SampledSpectrum own = transmitted ? value : (1 - rho) * lobe.f(wo, wi, mode);
            bool fromCamera = mode == TransportMode.Radiance;
            Vector3f woRender = bsdf.LocalToRender(wo), wiRender = bsdf.LocalToRender(wi);
            Vector3f directionExit = fromCamera ? woRender : wiRender;
            Vector3f directionEnter = fromCamera ? -wiRender : -woRender;
            PolarisedLightSystem.Attenuation sum = default;
            bool hasSum = false;
            if ((bool)own)
            {
                PolarisedLightSystem.Attenuation ownMatrix;
                if (!PolarisedPathIntegrator.RoughAttenuation(bsdf.WithBxDF(lobe), null, woRender, wiRender, new SampledSpectrum(1f), mode, out ownMatrix))
                {
                    ownMatrix = PolarisedLightSystem.Attenuation.Lambert(new SampledSpectrum(1f));
                }
                Accumulate(ref sum, ref hasSum, ownMatrix, own, directionExit, directionEnter);
            }
            SampledSpectrum retro = value - own;
            if (!transmitted && (bool)retro)
            {
                Accumulate(ref sum, ref hasSum, PolarisedLightSystem.Attenuation.Lambert(new SampledSpectrum(1f)), retro, directionExit, directionEnter);
            }
            if (hasSum)
            {
                polarised = PolarisedFacet.Normalise(sum, value, new SampledSpectrum(1f), out matrix);
            }
            return value;
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags, out bool polarised,
                                                           out PolarisedLightSystem.Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            pstd.optional<BSDFSample> bs = Sample_f(wo, uc, u, mode, sampleFlags);
            if (bs && !bs.value().IsSpecular())
            {
                f(bsdf, wo, bs.value().wi, mode, out polarised, out matrix);
            }
            return bs;
        }

        private static void Accumulate(ref PolarisedLightSystem.Attenuation sum, ref bool hasSum, PolarisedLightSystem.Attenuation matrix,
                                       SampledSpectrum contribution, Vector3f directionExit, Vector3f directionEnter)
        {
            matrix *= contribution;
            if (!matrix.WithFrames(directionExit, directionEnter) || matrix.HasNaNs())
            {
                return;
            }
            if (hasSum)
            {
                sum += matrix;
            }
            else
            {
                sum = matrix;
                hasSum = true;
            }
        }

        // RetroreflectiveBxDF Private Members
        private BxDF lobe;
        // The lobe again when it is a dielectric (its reflection alone is retroreflective), else null
        private DielectricBxDF dielectric;
        private Float rho;
    }

    // SheenMaterial Definition
    // Material "sggxsheen": the slab of fibres alone. "spectrum color" (the albedo of the fibres), "float roughness" (the alpha of
    // the SGGX distribution: small for fibres that stand along the normal), "float thickness" (optical), "integer maxdepth".
    public sealed unsafe class SheenMaterial : Material
    {
        public SheenMaterial(SpectrumTexture color, FloatTexture roughness, FloatTexture thickness, int maxDepth, FloatTexture displacement, Image normalMap)
        {
            this.color = color;
            this.roughness = roughness;
            this.thickness = thickness;
            this.maxDepth = maxDepth;
            this.displacement = displacement;
            this.normalMap = normalMap;
        }

        public static string Name() { return "SheenMaterial"; }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda,
                                                           ScratchBuffer buf)
        {
            return null;
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { roughness, thickness }, new SpectrumTexture[] { color });
        }

        public SGGXSheenBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            SampledSpectrum albedo = pbrt.Clamp(texEval.Apply(color, ctx, lambda), 0, 1);
            return new SGGXSheenBxDF(albedo, texEval.Apply(roughness, ctx), texEval.Apply(thickness, ctx), maxDepth);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda,
                                                       ScratchBuffer buf)
        {
            SGGXSheenBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public static SheenMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            SpectrumTexture color = parameters.GetSpectrumTexture("color", null, SpectrumType.Albedo, alloc);
            if (color == null)
            {
                color = new SpectrumConstantTexture(new ConstantSpectrum(1f));
            }
            FloatTexture roughness = parameters.GetFloatTexture("roughness", 0.3f, alloc);
            FloatTexture thickness = parameters.GetFloatTexture("thickness", 1f, alloc);
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            return new SheenMaterial(color, roughness, thickness, parameters.GetOneInt("maxdepth", 32), displacement, normalMap);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SheenMaterial color: %s roughness: %s thickness: %s maxDepth: %d ]", color, roughness, thickness, maxDepth);
        }

        // SheenMaterial Private Members
        private SpectrumTexture color;
        private FloatTexture roughness, thickness;
        private int maxDepth;
        private FloatTexture displacement;
        private Image normalMap;
    }
}
