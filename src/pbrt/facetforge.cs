// (not in pbrt-v4) facetforge.cs
//
// FacetForgeBxDF, FacetCoatedDiffuseBxDF and their materials: a direct port of the C++ fork (bxdfs.h, materials.h/.cpp), on
// util/facetforge.cs (the namespace pbrt::ff).

using System;
using System.Collections.Generic;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    // FacetForgeBxDF Definition
    // Multiple-scattering microsurface BSDF with general NDFs (Student-T, Beckmann, GGX, blends, full-sphere null-scattering
    // NDFs) over specular/diffuse facets, optionally biscale (see util/facetforge.cs). Evaluation and PDF are stochastic (a
    // single random-walk NEE estimate); Sample_f returns the exact walk weight with a proportional PDF.
    public sealed unsafe class FacetForgeBxDF : BxDF
    {
        public FacetForgeBxDF() { }

        // lv[0] is the outermost microsurface; with nLevels == 2, lv[1] is the microsurface seen on every facet of lv[0]
        // (biscale). Only the deepest level's facet is used. ior is the relative index of the medium below the surface
        // (dielectric facets only).
        public FacetForgeBxDF(ff.Surface s0, ff.Surface s1, Float ior, bool transmissive, ff.Surface s2 = null)
        {
            nLevels = (s2 != null && s1 != null) ? 3 : (s1 != null ? 2 : 1);
            this.ior = ior;
            this.transmissive = transmissive;
            lv[0] = s0.Clone();
            if (s1 != null)
            {
                lv[1] = s1.Clone();
            }
            if (s2 != null && s1 != null)
            {
                lv[2] = s2.Clone();
            }
            if (transmissive)
            {
                for (int i = 0; i < nLevels; ++i)
                {
                    if (lv[i].facet.kind == ff.FacetKind.Dielectric)
                    {
                        lv[i].facet.eta = new SampledSpectrum(ior);  // absolute ior for the thin-film stack
                    }
                }
            }
            // seen from inside (wo.z < 0) the strata of a layered outermost microsurface are reversed
            hasFlip = s0.ndf.layered && s0.ndf.n > 1;
            if (hasFlip)
            {
                for (int i = 0; i < nLevels; ++i)
                {
                    lvFlip[i] = lv[i];
                }
                lvFlip[0] = ff.ReverseLayers(lv[0]);
            }
        }

        private static ff.V3 Conv(Vector3f v) { return new ff.V3(v.x, v.y, v.z); }

        // (ours, not of the fork) Through the surface the estimate that starts on the side of the lower index has a tail: away from
        // the refracted lobe one evaluation in a thousand carries all of the value (measured in facetforge_test.cs), and a render
        // shows it as noise that does not go. The BSDF is reciprocal, f(wo, wi) of the radiance being f(wi, wo) of the
        // importance, so the same value is estimated by the walk that starts on the side of the higher index, which has no
        // such tail. False: the order of the fork.
        public bool denseStart = true;

        private bool StartsOnTheRareSide(Vector3f wo)
        {
            return (wo.z > 0) == (ior > 1);
        }

        public SampledSpectrum f0(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return f0(default, wo, wi, mode, false, out bool _, out Attenuation _);
        }

        // (ours) The same with the Mueller matrix of the walk (util/facetforge_polarised.cs) when `keep`
        private SampledSpectrum f0(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, bool keep, out bool polarised, out Attenuation matrix)
        {
            if (denseStart && transmissive && ior != 1 && wo.z * wi.z < 0 && StartsOnTheRareSide(wo))
            {
                return f0Given(bsdf, wi, wo, mode == TransportMode.Radiance ? TransportMode.Importance : TransportMode.Radiance, keep, out polarised, out matrix);
            }
            return f0Given(bsdf, wo, wi, mode, keep, out polarised, out matrix);
        }

        // The axes of the frame of the walk in the space of the renderer: the frame of the surface, turned over for a walk that
        // starts below it
        private static ff.Polar Polarisation(BSDF bsdf, bool flip, bool fromCamera)
        {
            Float sign = flip ? -1 : 1;
            return new ff.Polar(bsdf.LocalToRender(new Vector3f(sign, 0, 0)), bsdf.LocalToRender(new Vector3f(0, sign, 0)),
                                bsdf.LocalToRender(new Vector3f(0, 0, sign)), fromCamera);
        }

        private SampledSpectrum f0Given(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, bool keep, out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            if (wo.z == 0 || wi.z == 0)
            {
                return new SampledSpectrum(0f);
            }
            bool flip = wo.z < 0;
            ff.V3 s = Conv(flip ? -wo : wo), q = Conv(flip ? -wi : wi);
            bool trans = q.z < 0;
            if (trans && !transmissive)
            {
                return new SampledSpectrum(0f);
            }
            RNG rng = new RNG(Hash(GetOptions().seed, wo), Hash(wi));
            ff.Rnd rnd = new ff.Rnd(rng);
            double etap = flip ? 1.0 / ior : ior;
            ff.Stack st = new ff.Stack((flip && hasFlip) ? lvFlip : lv, nLevels);
            // Reflection is reciprocal, f(s,q) = f(q,s): start the walk at the more grazing of the two directions, which has far
            // lower variance for height-varying NDFs (rough layer on top of a smooth one: up to ~3 orders of magnitude,
            // measured). Transmission keeps the given order.
            SampledSpectrum fv;
            bool fromCamera = mode == TransportMode.Radiance;
            ff.Polar pol = null;
            if (!trans && q.z < s.z)
            {
                // (the walk starts from the other direction: for its matrix the camera is on the other side)
                pol = keep ? Polarisation(bsdf, flip, !fromCamera) : null;
                fv = ff.EvalLevel(st, 0, 1.0, etap, q, s, false, rnd, pol);
                if (keep && pol.hasSum)
                {
                    polarised = PolarisedFacet.Normalise(pol.sum, fv, new SampledSpectrum(1f), out matrix);
                }
                fv = fv / (Float)Math.Abs(s.z);
            }
            else
            {
                pol = keep ? Polarisation(bsdf, flip, fromCamera) : null;
                fv = ff.EvalLevel(st, 0, 1.0, etap, s, q, false, rnd, pol);
                if (keep && pol.hasSum)
                {
                    polarised = PolarisedFacet.Normalise(pol.sum, fv, new SampledSpectrum(1f), out matrix);
                }
                fv = fv / (Float)Math.Abs(q.z);
            }
            if (trans && mode == TransportMode.Radiance)
            {
                fv /= (Float)(etap * etap);
            }
            return ClampZero(fv);
        }

        public pstd.optional<BSDFSample> Sample_f0(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return Sample_f0(default, wo, uc, u, mode, sampleFlags, false, out bool _, out Attenuation _);
        }

        // (ours) The same with the Mueller matrix of the path of the walk when `keep`
        private pstd.optional<BSDFSample> Sample_f0(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                    bool keep, out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            if (wo.z == 0)
            {
                return default;
            }
            bool flip = wo.z < 0;
            ff.V3 s = Conv(flip ? -wo : wo);
            RNG rng = new RNG(Hash(GetOptions().seed, wo), Hash(uc, u));
            ff.Rnd rnd = new ff.Rnd(rng);
            double etap = flip ? 1.0 / ior : ior;
            ff.Stack st = new ff.Stack((flip && hasFlip) ? lvFlip : lv, nLevels);
            SampledSpectrum w = new SampledSpectrum(1f);
            ff.V3 q;
            ff.Polar pol = keep ? Polarisation(bsdf, flip, mode == TransportMode.Radiance) : null;
            if (!ff.SampleLevel(st, 0, 1.0, etap, s, ref w, false, rnd, out q, pol))
            {
                return default;
            }
            if (q.z == 0 || !(bool)w)
            {
                return default;
            }
            if (keep && pol.hasChain)
            {
                polarised = PolarisedFacet.Normalise(pol.chain, w, new SampledSpectrum(1f), out matrix);
            }
            bool trans = q.z < 0;
            if (trans)
            {
                if (!transmissive || (sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                {
                    return default;
                }
                if (mode == TransportMode.Radiance)
                {
                    w /= (Float)(etap * etap);
                }
            }
            else if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            {
                return default;
            }
            Vector3f wi = new Vector3f((Float)q.x, (Float)q.y, (Float)q.z);
            if (flip)
            {
                wi = -wi;
            }
            if (IsNaN(wi.x + wi.y + wi.z))
            {
                return default;
            }
            SampledSpectrum fv = w / AbsCosTheta(wi);
            return new BSDFSample(fv, wi, 1, trans ? BxDFFlags.GlossyTransmission : BxDFFlags.GlossyReflection, trans ? (Float)etap : 1f, true);
        }

        public Float PDF0(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            if (denseStart && transmissive && ior != 1 && wo.z * wi.z < 0 && StartsOnTheRareSide(wo))
            {
                // (the same density by the walk from the other side: a = f |cos wi| without the scale of the radiance, and
                // f(wo, wi) = f(wi, wo) etap^2 for that f)
                if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                {
                    return 0;
                }
                double etapGiven = wo.z < 0 ? 1.0 / ior : ior;
                Float other = PDF0Given(wi, wo, mode, BxDFReflTransFlags.Transmission);
                return std.max<Float>((Float)(other * etapGiven * etapGiven * Math.Abs(wi.z) / Math.Abs(wo.z)), 1e-6f);
            }
            return PDF0Given(wo, wi, mode, sampleFlags);
        }

        private Float PDF0Given(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            bool flip = wo.z < 0;
            ff.V3 s = Conv(flip ? -wo : wo), q = Conv(flip ? -wi : wi);
            bool trans = q.z < 0;
            if (trans ? (!transmissive || (sampleFlags & BxDFReflTransFlags.Transmission) == 0) : (sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            {
                return 0;
            }
            RNG rng = new RNG(Hash(GetOptions().seed, wi, 0x4646504446ul), Hash(wo));
            ff.Rnd rnd = new ff.Rnd(rng);
            double etap = flip ? 1.0 / ior : ior;
            ff.Stack st = new ff.Stack((flip && hasFlip) ? lvFlip : lv, nLevels);
            SampledSpectrum a;
            if (!trans && q.z < s.z)
            {
                // same reciprocal start-direction choice as f(); a = f * |cos q|
                a = ff.EvalLevel(st, 0, 1.0, etap, q, s, true, rnd) * (Float)(Math.Abs(q.z) / Math.Abs(s.z));
            }
            else
            {
                a = ff.EvalLevel(st, 0, 1.0, etap, s, q, true, rnd);
            }
            // Strictly positive: the integrator divides by this (pdfIsProportional).
            return std.max<Float>(a[0], 1e-6f);
        }

        // Minimal Retroreflective Microfacet Model blend: 0 = off. For the conductor lobe this is the plain mixture
        // f = (1-rho) f0(wo) + rho f0(wo'). For the transmissive (dielectric) lobe the retro blend modifies the BRDF only (OpenPBR
        // issue #243): f = (1-rho) f_R(wo) + rho f_R(wo') + f_T(wo), sampled by choosing the original view (probability
        // a = 1 - rho/2) or the mirrored view wo' (1 - a) and weighting the walk by
        //   original : reflection (1-rho)/a, transmission 1/a;   mirrored : reflection rho/(1-a), transmission discarded.
        // Every weight is at most 2 and the estimator is exactly unbiased; PDF() is the matching proportional density (the walk's
        // own stochastic estimate, as for the unblended lobe).
        public void SetRetro(Float r) { retro = Clamp(r, 0, 1); }
        public Float Retro() { return retro; }

        private static Vector3f RetroView(Vector3f wo) { return RetroreflectiveBxDF.RetroView(wo); }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (retro == 0)
            {
                return f0(wo, wi, mode);
            }
            if (!transmissive)
            {
                // (RetroF)
                SampledSpectrum r = new SampledSpectrum(0f);
                if (retro < 1)
                {
                    r += (1 - retro) * f0(wo, wi, mode);
                }
                if (retro > 0)
                {
                    r += retro * f0(RetroView(wo), wi, mode);
                }
                return r;
            }
            if (!SameHemisphere(wo, wi))
            {
                return f0(wo, wi, mode);
            }
            return (1 - retro) * f0(wo, wi, mode) + retro * f0(RetroView(wo), wi, mode);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (retro == 0)
            {
                return Sample_f0(wo, uc, u, mode, sampleFlags);
            }
            if (!transmissive)
            {
                // (RetroSample)
                bool mirrored = uc < retro;
                Float ucr = mirrored ? std.min<Float>(uc / retro, OneMinusEpsilon) : std.min<Float>((uc - retro) / (1 - retro), OneMinusEpsilon);
                pstd.optional<BSDFSample> br = Sample_f0(mirrored ? RetroView(wo) : wo, ucr, u, mode, sampleFlags);
                if (!br || !(bool)br.value().f || br.value().pdf == 0)
                {
                    return default;
                }
                return br;  // (pdfIsProportional already)
            }
            Float a = 1 - 0.5f * retro;
            bool orig = uc < a;
            Float uc2 = orig ? std.min<Float>(uc / a, OneMinusEpsilon) : std.min<Float>((uc - a) / (1 - a), OneMinusEpsilon);
            pstd.optional<BSDFSample> bs = Sample_f0(orig ? wo : RetroView(wo), uc2, u, mode, sampleFlags);
            if (!bs || !(bool)bs.value().f)
            {
                return default;
            }
            BSDFSample sample = bs.value();
            Float wgt;
            if (sample.IsTransmission())
            {
                wgt = orig ? 1 / a : 0;
            }
            else
            {
                wgt = orig ? (1 - retro) / a : retro / (1 - a);
            }
            if (wgt == 0)
            {
                return default;
            }
            sample.f *= wgt;
            return sample;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (retro == 0)
            {
                return PDF0(wo, wi, mode, sampleFlags);
            }
            if (!transmissive)
            {
                // (RetroPDF)
                Float p = 0;
                if (retro < 1)
                {
                    p += (1 - retro) * PDF0(wo, wi, mode, sampleFlags);
                }
                if (retro > 0)
                {
                    p += retro * PDF0(RetroView(wo), wi, mode, sampleFlags);
                }
                return p;
            }
            Float a = 1 - 0.5f * retro;
            if (!SameHemisphere(wo, wi))
            {
                return a * PDF0(wo, wi, mode, sampleFlags);
            }
            return a * PDF0(wo, wi, mode, sampleFlags) + (1 - a) * PDF0(RetroView(wo), wi, mode, sampleFlags);
        }

        // (ours) The Mueller matrices of the walk. The retroreflective blend has none: it says where the light goes, not by what
        // reflections.
        public override bool HasMuellerMatrix() { return retro == 0; }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            if (retro != 0)
            {
                polarised = false;
                matrix = default;
                return f(wo, wi, mode);
            }
            return f0(bsdf, wo, wi, mode, true, out polarised, out matrix);
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                           out bool polarised, out Attenuation matrix)
        {
            if (retro != 0)
            {
                polarised = false;
                matrix = default;
                return Sample_f(wo, uc, u, mode, sampleFlags);
            }
            return Sample_f0(bsdf, wo, uc, u, mode, sampleFlags, true, out polarised, out matrix);
        }

        public static string Name() { return "FacetForgeBxDF"; }

        public override string ToString()
        {
            return StringPrintf("[ FacetForgeBxDF levels: %d ior: %f transmissive: %s ]", nLevels, ior, transmissive);
        }

        public override void Regularize() { }

        public override BxDFFlags Flags()
        {
            return transmissive ? BxDFFlags.GlossyReflection | BxDFFlags.GlossyTransmission : BxDFFlags.GlossyReflection;
        }

        private ff.Surface[] lv = new ff.Surface[3];
        private ff.Surface[] lvFlip = new ff.Surface[3];
        private bool hasFlip = false;
        private int nLevels = 1;
        private Float ior = 1.5f;
        private bool transmissive = false;
        private Float retro = 0;
    }

    // FacetCoatedDiffuseBxDF: Facet Forge dielectric microsurface over a Lambertian base, via pbrt's stochastic LayeredBxDF
    // (unbiased up to the maxDepth truncation).
    public sealed unsafe class FacetCoatedDiffuseBxDF : LayeredBxDF<FacetForgeBxDF, DiffuseBxDF>
    {
        public FacetCoatedDiffuseBxDF() : base(true) { }

        public FacetCoatedDiffuseBxDF(FacetForgeBxDF top, DiffuseBxDF bottom, Float thickness, SampledSpectrum albedo, Float g, int maxDepth, int nSamples)
            : base(top, bottom, thickness, albedo, g, maxDepth, nSamples, true)
        {
        }

        public static string Name() { return "FacetCoatedDiffuseBxDF"; }
    }

    // FacetForgeMaterial Definition
    // "Student-T and Beyond" (d'Eon 2023) multiple-scattering microsurface material; see util/facetforge.cs and FacetForgeBxDF.
    //
    // Parameters:
    //   NDF components (up to 4; the first uses bare names, the others a numeric suffix 1..3):
    //     string ndf[N]      "studentt" (default), "beckmann", "ggx", "vmf", "nullstudentt"
    //     float roughness[N] / uroughness[N] / vroughness[N]   alpha (default 0.5), used as is unless "remaproughness";
    //                        vmf/nullstudentt: constant, isotropic
    //     float gamma[N]     Student-T shape, > 1.5 (default 3); nullstudentt needs >= 2
    //     float weight[N]    mixture weight (default 1; weights are normalised)
    //     float transform[N] [m00 m01 m10 m11]   (ours, opt-in) microsurface transformation of the component (Atanasov et al.
    //                        2022): its microsurface is mapped by this matrix of the tangent plane. Every NDF takes it, the null
    //                        ones too (an anisotropic or sheared vmf); "literalnull" is not used for a transformed component.
    //   Biscale: "inner.ndf", "inner.roughness"/"inner.uroughness"/"inner.vroughness", "inner.gamma" (and inner.ndf1...,
    //            inner.weight...); inner anisotropy follows the projected macro tangent. Triscale: "inner2." likewise.
    //   string facet         "conductor" (default; eta, k), "dielectric" (ior), "diffuse" (reflectance), "mirror" (reflectance,
    //                        default 1)
    //   string layering      "mixture" (default): the components are blended uniformly at all heights (symmetric operator).
    //                        "asymmetric": the components are strata of a layered Smith volume, listed from the outermost (top)
    //                        layer down, each occupying a fraction `weight` of the height distribution (d'Eon et al. 2023)
    //   eta1.., k1.., reflectance1..   the facets of the strata 1..3 of an asymmetric layering
    //   float filmthickness (nm) / filmeta / filmweight   exact Airy thin film on every conductor or dielectric facet (spectral,
    //                        unbiased; dielectric: T = 1 - R)
    //   float layerthickness [...] / layereta [...] / layerabbe / layercauchyb / layersellmeier / spectrum layereta /
    //   texture layerthicknessscale   (ours) the films of Magi on the facets instead (thinfilmlayers.cs): any number of layers,
    //                        from the surface inward, with their dispersion
    //   bool analyticsingle  first scattering order in closed form (specular facets, single level, default true; false =
    //                        stochastic next-event estimate for every order)
    //   int maxwalk          random walk length (default 32)
    //   string sampler       Student-T visible normals: "exact" (default) or "paper" (fitted)
    //   string nullsampler   (ours) visible normals of a vmf: "lobe" (default; normals drawn from the vMF lobe by the stable
    //                        inversion of Tokuyoshi 2025 and kept with the probability max(0, wi . m): the same density, 1 / alpha^2
    //                        times fewer rejections) or "disc" (the rejection from the uniform disc of the reference code)
    //   bool literalnull     use the reference code's literal null-collision walk for a single null NDF (otherwise the equivalent
    //                        thinned form)
    //   float retroreflectivity   the retroreflective blend of the BxDF (0)
    //   bool densestart      (ours) the transmission is estimated by the walk that starts in the denser medium (true; false: the fork)
    public sealed unsafe class FacetForgeMaterial : Material
    {
        public sealed class CompSpec
        {
            public ff.Kind kind = ff.Kind.StudentT;
            public Float weight = 1;
            public FloatTexture u, v;  // heightfield roughness (alpha units)
            public Float gamma = 3;
            public Float alpha = 1;    // null NDFs: constant roughness
            public ff.NullTable tab = null;
            public Float[] transform = null;  // m00 m01 m10 m11
        }

        public sealed class LevelSpec
        {
            public int n = 0;
            public CompSpec[] c = new CompSpec[ff.kMaxComps] { new CompSpec(), new CompSpec(), new CompSpec(), new CompSpec() };
        }

        public FacetForgeMaterial(LevelSpec outer, LevelSpec inner, LevelSpec inner2, ff.FacetKind facet, SpectrumTexture eta, SpectrumTexture k,
                                  SpectrumTexture reflectance, Spectrum ior, FloatTexture displacement, Image normalMap, bool remapRoughness,
                                  int maxWalk, bool paperSampler, bool literalNull, bool layered = false)
        {
            this.outer = outer;
            hasInner = inner != null;
            hasInner2 = inner != null && inner2 != null;
            this.facet = facet;
            this.eta = eta;
            this.k = k;
            this.reflectance = reflectance;
            this.ior = ior;
            this.displacement = displacement;
            this.normalMap = normalMap;
            this.remapRoughness = remapRoughness;
            this.maxWalk = maxWalk;
            this.paperSampler = paperSampler;
            this.literalNull = literalNull;
            this.layered = layered;
            if (inner != null)
            {
                this.inner = inner;
            }
            if (inner != null && inner2 != null)
            {
                this.inner2 = inner2;
            }
        }

        public static string Name() { return "FacetForgeMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { outer.c[0].u, outer.c[0].v }, new SpectrumTexture[] { eta, k, reflectance });
        }

        public override FloatTexture GetDisplacement() { return displacement; }
        public override Image GetNormalMap() { return normalMap; }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return StringPrintf("[ FacetForgeMaterial components: %d biscale: %d ]", outer.n, hasInner ? 1 : 0);
        }

        // Thin dielectric film on every facet (conductor / dielectric facets): filmthickness (nm), filmeta, filmweight.
        public FloatTexture filmThickness;
        public Float filmIndex = 1.33f, filmWeight = 1;
        public Float retro = 0;
        // (ours) The films of Magi instead of the one film of the fork: any number of layers, with their dispersion and a texture
        // for their thicknesses
        public ThinFilmLayers layers;
        // (ours) "bool densestart": the estimate of the transmission starts on the side of the higher index (FacetForgeBxDF.denseStart)
        public bool denseStart = true;

        // Non-uniform material (layered NDFs only): per-stratum conductor eta/k and diffuse/mirror reflectance (null entries
        // fall back to the shared ones).
        public void SetLayerMaterials(SpectrumTexture[] etaL, SpectrumTexture[] kL, SpectrumTexture[] reflL)
        {
            perLayerMat = true;
            for (int i = 0; i < ff.kMaxComps; ++i)
            {
                this.etaL[i] = etaL[i];
                this.kL[i] = kL[i];
                this.reflL[i] = reflL[i];
            }
        }

        private void BuildSurface<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, LevelSpec ls, ff.Surface s)
            where TextureEvaluator : ITextureEvaluator
        {
            s.ndf.n = ls.n;
            for (int i = 0; i < ls.n; ++i)
            {
                CompSpec cs = ls.c[i];
                ff.Comp c = s.ndf.c[i];
                c.w = cs.weight;
                if (cs.kind == ff.Kind.VMF || cs.kind == ff.Kind.NullStudentT)
                {
                    c.kind = cs.kind;
                    c.tab = cs.tab;
                }
                else
                {
                    Float ax = texEval.Apply(cs.u, ctx), ay = texEval.Apply(cs.v, ctx);
                    if (remapRoughness)
                    {
                        ax = TrowbridgeReitzDistribution.RoughnessToAlpha(ax);
                        ay = TrowbridgeReitzDistribution.RoughnessToAlpha(ay);
                    }
                    c.InitHeightfield(cs.kind, ax, ay, cs.gamma);
                }
                if (cs.transform != null)
                {
                    c.SetTransform(cs.transform[0], cs.transform[1], cs.transform[2], cs.transform[3]);
                }
                else
                {
                    c.ClearTransform();
                }
            }
            s.ndf.Normalize();
            s.ndf.paperSampler = paperSampler;
            s.ndf.literalNull = literalNull;
            s.ndf.layered = layered && ls.n > 1;
            s.analyticSingle = analyticSingle;
            s.maxWalk = maxWalk;
        }

        public FacetForgeBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            ff.Surface s0 = new ff.Surface(), s1 = new ff.Surface(), s2 = new ff.Surface();
            BuildSurface(texEval, ctx, outer, s0);
            if (hasInner)
            {
                BuildSurface(texEval, ctx, inner, s1);
            }
            if (hasInner2)
            {
                BuildSurface(texEval, ctx, inner2, s2);
            }
            ff.Facet fc = new ff.Facet();
            fc.kind = facet;
            Float iorV = 1.5f;
            switch (facet)
            {
            case ff.FacetKind.Conductor:
                fc.eta = texEval.Apply(eta, ctx, lambda);
                fc.k = texEval.Apply(k, ctx, lambda);
                break;
            case ff.FacetKind.Dielectric:
                iorV = ior.Evaluate(lambda[0]);
                if (!(ior is ConstantSpectrum))
                {
                    lambda.TerminateSecondary();
                }
                if (iorV == 0)
                {
                    iorV = 1;
                }
                fc.eta = new SampledSpectrum(iorV);  // absolute ior, used by the thin-film stack
                break;
            default:
                fc.R = Clamp(texEval.Apply(reflectance, ctx, lambda), 0, 1);
                break;
            }
            if (filmThickness != null && (facet == ff.FacetKind.Conductor || facet == ff.FacetKind.Dielectric))
            {
                ff.ThinFilm tf = new ff.ThinFilm();
                tf.thickness = std.max<Float>(texEval.Apply(filmThickness, ctx), 0);
                tf.index = filmIndex;
                tf.weight = filmWeight;
                tf.lambda = lambda;
                if (tf.Enabled())
                {
                    fc.film = tf;
                }
            }
            // (ours) The films of Magi on the facets: "layerthickness", "layereta", ... (thinfilmlayers.cs)
            if (layers != null && layers.Count() > 0 && (facet == ff.FacetKind.Conductor || facet == ff.FacetKind.Dielectric))
            {
                fc.stack = layers.Stack(texEval, ctx, lambda);
                fc.hasStack = true;
            }
            ff.Surface deep = hasInner2 ? s2 : (hasInner ? s1 : s0);
            deep.facet = fc;
            if (perLayerMat && deep.ndf.layered)
            {
                deep.perLayerFacet = true;
                for (int i = 0; i < deep.ndf.n; ++i)
                {
                    ff.Facet fl = fc.Clone();
                    if (facet == ff.FacetKind.Conductor)
                    {
                        if (etaL[i] != null)
                        {
                            fl.eta = texEval.Apply(etaL[i], ctx, lambda);
                        }
                        if (kL[i] != null)
                        {
                            fl.k = texEval.Apply(kL[i], ctx, lambda);
                        }
                    }
                    else if (facet != ff.FacetKind.Dielectric)
                    {
                        if (reflL[i] != null)
                        {
                            fl.R = Clamp(texEval.Apply(reflL[i], ctx, lambda), 0, 1);
                        }
                    }
                    deep.layerFacet[i] = fl;
                }
            }
            FacetForgeBxDF bxdf = new FacetForgeBxDF(s0, hasInner ? s1 : null, iorV, facet == ff.FacetKind.Dielectric, hasInner2 ? s2 : null);
            bxdf.SetRetro(retro);
            bxdf.denseStart = denseStart;
            return bxdf;
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            FacetForgeBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        private static void ParseFFLevel(TextureParameterDictionary parameters, string pre, LevelSpec ls, FileLoc loc, Allocator alloc)
        {
            for (int i = 0; i < ff.kMaxComps; ++i)
            {
                string sx = i == 0 ? "" : i.ToString();
                string tn = parameters.GetOneString(pre + "ndf" + sx, i == 0 ? "studentt" : "");
                if (tn.Length == 0)
                {
                    continue;
                }
                CompSpec c = ls.c[ls.n];
                if (tn == "beckmann")
                {
                    c.kind = ff.Kind.Beckmann;
                }
                else if (tn == "ggx")
                {
                    c.kind = ff.Kind.GGX;
                }
                else if (tn == "studentt")
                {
                    c.kind = ff.Kind.StudentT;
                }
                else if (tn == "vmf")
                {
                    c.kind = ff.Kind.VMF;
                }
                else if (tn == "nullstudentt")
                {
                    c.kind = ff.Kind.NullStudentT;
                }
                else
                {
                    ErrorExit(loc, StringPrintf("unknown ndf \"%s\"", tn));
                }
                c.gamma = parameters.GetOneFloat(pre + "gamma" + sx, 3f);
                if (c.kind == ff.Kind.StudentT && !(c.gamma > 1.5f))
                {
                    ErrorExit(loc, "Student-T \"gamma\" must be > 1.5");
                }
                if (c.kind == ff.Kind.NullStudentT && !(c.gamma >= 2f))
                {
                    ErrorExit(loc, "null Student-T \"gamma\" must be >= 2");
                }
                c.weight = parameters.GetOneFloat(pre + "weight" + sx, 1f);
                List<Float> transform = parameters.GetFloatArray(pre + "transform" + sx);
                if (transform.Count != 0)
                {
                    if (transform.Count != 4)
                    {
                        ErrorExit(loc, StringPrintf("\"%s\" takes the four numbers m00 m01 m10 m11", pre + "transform" + sx));
                    }
                    if (!(std.abs(transform[0] * transform[3] - transform[1] * transform[2]) > 1e-8f))
                    {
                        ErrorExit(loc, StringPrintf("\"%s\" has no inverse", pre + "transform" + sx));
                    }
                    c.transform = new Float[] { transform[0], transform[1], transform[2], transform[3] };
                }
                FloatTexture u = parameters.GetFloatTextureOrNull(pre + "uroughness" + sx, alloc);
                FloatTexture v = parameters.GetFloatTextureOrNull(pre + "vroughness" + sx, alloc);
                if (u == null)
                {
                    u = parameters.GetFloatTexture(pre + "roughness" + sx, 0.5f, alloc);
                }
                if (v == null)
                {
                    v = parameters.GetFloatTexture(pre + "roughness" + sx, 0.5f, alloc);
                }
                c.u = u;
                c.v = v;
                if (c.kind == ff.Kind.VMF || c.kind == ff.Kind.NullStudentT)
                {
                    c.alpha = parameters.GetOneFloat(pre + "roughness" + sx, 1f);
                    ff.NullTable tab = new ff.NullTable();
                    tab.Init(c.kind, c.alpha, c.gamma);
                    string nullSampler = parameters.GetOneString("nullsampler", "lobe");
                    if (nullSampler != "disc" && nullSampler != "lobe")
                    {
                        ErrorExit(loc, "\"nullsampler\" must be \"disc\" or \"lobe\"");
                    }
                    tab.lobeSampler = nullSampler == "lobe";
                    c.tab = tab;
                }
                ls.n++;
            }
            if (ls.n == 0)
            {
                ErrorExit(loc, "no NDF specified");
            }
        }

        public static FacetForgeMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc,
                                                string forceFacet = null)
        {
            LevelSpec outer = new LevelSpec(), inner = new LevelSpec(), inner2 = new LevelSpec();
            ParseFFLevel(parameters, "", outer, loc, alloc);
            bool biscale = parameters.GetOneString("inner.ndf", "").Length != 0;
            if (biscale)
            {
                ParseFFLevel(parameters, "inner.", inner, loc, alloc);
            }
            bool triscale = biscale && parameters.GetOneString("inner2.ndf", "").Length != 0;
            if (triscale)
            {
                ParseFFLevel(parameters, "inner2.", inner2, loc, alloc);
            }
            else if (!biscale && parameters.GetOneString("inner2.ndf", "").Length != 0)
            {
                ErrorExit(loc, "\"inner2.ndf\" requires \"inner.ndf\"");
            }
            string fn = forceFacet != null ? forceFacet : parameters.GetOneString("facet", "conductor");
            ff.FacetKind facet = ff.FacetKind.Conductor;
            if (fn == "conductor")
            {
                facet = ff.FacetKind.Conductor;
            }
            else if (fn == "dielectric")
            {
                facet = ff.FacetKind.Dielectric;
            }
            else if (fn == "diffuse")
            {
                facet = ff.FacetKind.Diffuse;
            }
            else if (fn == "mirror")
            {
                facet = ff.FacetKind.Mirror;
            }
            else
            {
                ErrorExit(loc, "\"facet\" must be conductor, dielectric, diffuse or mirror");
            }
            SpectrumTexture eta = null, k = null, refl = null;
            Spectrum ior = null;
            if (facet == ff.FacetKind.Conductor)
            {
                eta = parameters.GetSpectrumTextureOrNull("eta", SpectrumType.Unbounded, alloc);
                k = parameters.GetSpectrumTextureOrNull("k", SpectrumType.Unbounded, alloc);
                if (eta == null)
                {
                    eta = new SpectrumConstantTexture(GetNamedSpectrum("metal-Cu-eta"));
                }
                if (k == null)
                {
                    k = new SpectrumConstantTexture(GetNamedSpectrum("metal-Cu-k"));
                }
            }
            else if (facet == ff.FacetKind.Dielectric)
            {
                ior = ParametricSpectra.Index(parameters, "ior", alloc);
                if (ior == null)
                {
                    ior = new ConstantSpectrum(1.5f);
                }
            }
            else
            {
                refl = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo, alloc);
                if (refl == null)
                {
                    refl = new SpectrumConstantTexture(new ConstantSpectrum(facet == ff.FacetKind.Mirror ? 1f : 0.5f));
                }
            }
            string sampler = parameters.GetOneString("sampler", "exact");
            if (sampler != "exact" && sampler != "paper")
            {
                ErrorExit(loc, "\"sampler\" must be \"exact\" or \"paper\"");
            }
            string layering = parameters.GetOneString("layering", "mixture");
            if (layering != "mixture" && layering != "asymmetric")
            {
                ErrorExit(loc, "\"layering\" must be \"mixture\" or \"asymmetric\"");
            }
            bool layered = layering == "asymmetric";
            if (layered)
            {
                for (int i = 0; i < outer.n; ++i)
                {
                    if (outer.c[i].kind == ff.Kind.VMF || outer.c[i].kind == ff.Kind.NullStudentT)
                    {
                        ErrorExit(loc, "\"layering\" \"asymmetric\" supports heightfield NDFs only");
                    }
                }
            }
            // per-stratum facet properties (suffix = stratum index, 1..3; stratum 0 uses the bare names)
            SpectrumTexture[] etaL = new SpectrumTexture[ff.kMaxComps], kL = new SpectrumTexture[ff.kMaxComps], reflL = new SpectrumTexture[ff.kMaxComps];
            bool anyLayerMat = false;
            for (int i = 1; i < ff.kMaxComps; ++i)
            {
                string sx = i.ToString();
                if (facet == ff.FacetKind.Conductor)
                {
                    etaL[i] = parameters.GetSpectrumTextureOrNull("eta" + sx, SpectrumType.Unbounded, alloc);
                    kL[i] = parameters.GetSpectrumTextureOrNull("k" + sx, SpectrumType.Unbounded, alloc);
                    anyLayerMat |= (etaL[i] != null || kL[i] != null);
                }
                else if (facet != ff.FacetKind.Dielectric)
                {
                    reflL[i] = parameters.GetSpectrumTexture("reflectance" + sx, null, SpectrumType.Albedo, alloc);
                    anyLayerMat |= reflL[i] != null;
                }
            }
            etaL[0] = eta;
            kL[0] = k;
            reflL[0] = refl;
            if (anyLayerMat && !layered)
            {
                ErrorExit(loc, "per-layer facet properties (eta1, k1, reflectance1, ...) need \"layering\" \"asymmetric\"");
            }
            FloatTexture displacement = parameters.GetFloatTextureOrNull("displacement", alloc);
            FacetForgeMaterial ffm = new FacetForgeMaterial(outer, biscale ? inner : null, triscale ? inner2 : null, facet, eta, k, refl, ior, displacement,
                                                            normalMap, parameters.GetOneBool("remaproughness", false), parameters.GetOneInt("maxwalk", 32),
                                                            sampler == "paper", parameters.GetOneBool("literalnull", false), layered);
            if (anyLayerMat)
            {
                ffm.SetLayerMaterials(etaL, kL, reflL);
            }
            ffm.analyticSingle = parameters.GetOneBool("analyticsingle", true);
            ffm.filmThickness = parameters.GetFloatTextureOrNull("filmthickness", alloc);
            ffm.filmIndex = parameters.GetOneFloat("filmeta", 1.33f);
            ffm.filmWeight = Clamp(parameters.GetOneFloat("filmweight", 1f), 0, 1);
            if (ffm.filmThickness != null && facet != ff.FacetKind.Conductor && facet != ff.FacetKind.Dielectric)
            {
                ErrorExit(loc, "\"filmthickness\" needs a conductor or dielectric facet");
            }
            ffm.retro = parameters.GetOneFloat("retroreflectivity", 0f);
            ffm.layers = ThinFilmLayers.Create(parameters, loc, alloc);
            if (ffm.layers.Count() > 0)
            {
                if (ffm.filmThickness != null)
                {
                    ErrorExit(loc, "\"filmthickness\" (the film of the fork) and \"layerthickness\" (the films of Magi) are two models of the same film: give one");
                }
                if (facet != ff.FacetKind.Conductor && facet != ff.FacetKind.Dielectric)
                {
                    ErrorExit(loc, "\"layerthickness\" needs a conductor or dielectric facet");
                }
            }
            ffm.denseStart = parameters.GetOneBool("densestart", true);
            return ffm;
        }

        private SpectrumTexture[] etaL = new SpectrumTexture[ff.kMaxComps], kL = new SpectrumTexture[ff.kMaxComps], reflL = new SpectrumTexture[ff.kMaxComps];
        private bool perLayerMat = false;
        private LevelSpec outer, inner, inner2;
        private bool hasInner, hasInner2;
        private ff.FacetKind facet;
        private SpectrumTexture eta, k, reflectance;
        private Spectrum ior;
        private FloatTexture displacement;
        private Image normalMap;
        private bool remapRoughness;
        private int maxWalk;
        private bool paperSampler, literalNull, layered;

        public bool analyticSingle = true;
    }

    // FacetCoatedDiffuseMaterial: Facet Forge dielectric microsurface (any NDF/layering the facetforge material supports) as the
    // top interface of pbrt's stochastic LayeredBxDF, over a Lambertian base (paper Fig. 3d configuration). Parameters: all
    // facetforge NDF/ior parameters, plus basereflectance, thickness, albedo, g, maxdepth, nsamples.
    public sealed unsafe class FacetCoatedDiffuseMaterial : Material
    {
        public FacetCoatedDiffuseMaterial(FacetForgeMaterial ffm, SpectrumTexture baseReflectance, FloatTexture thickness, SpectrumTexture albedo,
                                          FloatTexture g, int maxDepth, int nSamples)
        {
            this.ffm = ffm;
            this.baseReflectance = baseReflectance;
            this.thickness = thickness;
            this.albedo = albedo;
            this.g = g;
            this.maxDepth = maxDepth;
            this.nSamples = nSamples;
        }

        public static string Name() { return "FacetCoatedDiffuseMaterial"; }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return ffm.CanEvaluateTextures(texEval) &&
                   texEval.CanEvaluate(new FloatTexture[] { thickness, g }, new SpectrumTexture[] { baseReflectance, albedo });
        }

        public override FloatTexture GetDisplacement() { return ffm.GetDisplacement(); }
        public override Image GetNormalMap() { return ffm.GetNormalMap(); }
        public override bool HasSubsurfaceScattering() { return false; }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return StringPrintf("[ FacetCoatedDiffuseMaterial maxDepth: %d nSamples: %d ]", maxDepth, nSamples);
        }

        public FacetCoatedDiffuseBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda)
            where TextureEvaluator : ITextureEvaluator
        {
            FacetForgeBxDF top = ffm.GetBxDF(texEval, ctx, ref lambda);
            SampledSpectrum r = Clamp(texEval.Apply(baseReflectance, ctx, lambda), 0, 1);
            SampledSpectrum a = Clamp(texEval.Apply(albedo, ctx, lambda), 0, 1);
            Float gg = Clamp(texEval.Apply(g, ctx), -1, 1);
            Float thick = texEval.Apply(thickness, ctx);
            return new FacetCoatedDiffuseBxDF(top, new DiffuseBxDF(r), thick, a, gg, maxDepth, nSamples);
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            FacetCoatedDiffuseBxDF bxdf = GetBxDF(texEval, ctx, ref lambda);
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(bxdf));
        }

        public static FacetCoatedDiffuseMaterial Create(TextureParameterDictionary parameters, Image normalMap, FileLoc loc, Allocator alloc)
        {
            FacetForgeMaterial ffm = FacetForgeMaterial.Create(parameters, normalMap, loc, alloc, "dielectric");
            SpectrumTexture baseRefl = parameters.GetSpectrumTexture("basereflectance", null, SpectrumType.Albedo, alloc);
            if (baseRefl == null)
            {
                baseRefl = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));
            }
            SpectrumTexture albedo = parameters.GetSpectrumTexture("albedo", null, SpectrumType.Albedo, alloc);
            if (albedo == null)
            {
                albedo = new SpectrumConstantTexture(new ConstantSpectrum(0f));
            }
            return new FacetCoatedDiffuseMaterial(ffm, baseRefl, parameters.GetFloatTexture("thickness", .01f, alloc), albedo,
                                                  parameters.GetFloatTexture("g", 0f, alloc), parameters.GetOneInt("maxdepth", 10),
                                                  parameters.GetOneInt("nsamples", 1));
        }

        private FacetForgeMaterial ffm;
        private SpectrumTexture baseReflectance;
        private FloatTexture thickness;
        private SpectrumTexture albedo;
        private FloatTexture g;
        private int maxDepth, nSamples;
    }
}
