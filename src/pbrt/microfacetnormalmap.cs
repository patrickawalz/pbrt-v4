// (not in pbrt-v4) microfacetnormalmap.cs
//
// Microfacet-based normal mapping, a transliteration of the Mitsuba plugins released with Schuessler, Heitz, Hanika,
// Dachsbacher, "Microfacet-based Normal Mapping for Robust Monte Carlo Path Tracing", SIGGRAPH Asia 2017
// (normalmap_microfacet_default.cpp, normalmap_microfacet.cpp, normalmap_microfacet_nonseparable.cpp).
//
// The surface under a mapped normal p is a microsurface of two facets, the one of normal p and a "tangent" facet at a
// right angle to the unperturbed normal g, whose mean normal is g: no light arrives from below the unperturbed surface
// and none leaves below it, so the black fringes and the lost energy of a plain mapped normal do not occur.
//
// Naming follows the plugins: "wi" is the direction toward the viewer (pbrt's wo) and "wo" is the direction toward the
// light (pbrt's wi). Mitsuba's eval() includes the cosine of wo against the unperturbed normal g; pbrt's f() is divided by
// the cosine against the perturbed normal p (which the integrator multiplies back in).
//
// (the C++ code hooks the model into BSDF; here it is a BxDF around the BxDF of the material, as any other lobe)
//
// Not in the C++ code:
//   - a BxDF that also transmits keeps its transmission, as in the extension of the plugin by G. Murat, D. Kang and H. Kim
//     (KAIST CS580, 2019, "Extending Microfacet-Based Normal Mapping", known here from their slides only): the BxDF of the
//     mapped facet is sampled with its transmission, and where it transmits, at whichever bounce of the walk, the walk stops
//     and the refracted ray leaves below the surface; the evaluation no longer stops at a direction below the surface, and
//     adds the transmission of each vertex of the walk towards it. The tangent facet is a mirror for such a BxDF (the
//     saturation is not used). Seen from below the surface the BxDF is used as it is;
//   - the analytic model has a Mueller matrix: the one of the BxDF at each of its three paths, with the mirror reflection by
//     the tangent facet where the path has one. The random walks are taken as depolarising.

using System;

namespace pbrt
{
    public enum NormalMapModel : int
    {
        Classic = 0,
        Default = 1,       // analytic second order, specular tangent facet (recommended)
        Walk = 2,          // random walk, separable masking-shadowing (saturation, max order)
        NonSeparable = 3,  // random walk, height-correlated masking-shadowing
    }

    public sealed class NormalMapParams
    {
        public NormalMapModel model = NormalMapModel.Classic;
        public Float maxOrder = 2;     // Walk / NonSeparable: maximum scattering order (fractional allowed)
        public Float saturation = 0;   // Walk / NonSeparable: 0 = specular tangent facet, 1 = same material
        // (not in the C++ code) The normal that the two facets average to: the shading normal from before the map (an
        // interpolated normal: what the C++ code takes), or the normal of the true surface
        public bool geometric = false;

        // "string normalmapmodel" (classic, microfacetdefault, microfacet, microfacetnonseparable), "float normalmapmaxorder",
        // "float normalmapsaturation"; null for the classic model
        public static NormalMapParams Create(ParameterDictionary parameters)
        {
            string name = parameters.GetOneString("normalmapmodel", "classic");
            string reference = parameters.GetOneString("normalmapreference", "shading");
            if (reference != "shading" && reference != "geometric")
            {
                pbrt.ErrorExit(pbrt.StringPrintf("%s: normalmapreference must be shading or geometric", reference));
            }
            Float maxOrder = parameters.GetOneFloat("normalmapmaxorder", 2f);
            Float saturation = parameters.GetOneFloat("normalmapsaturation", 0f);
            if (name == "classic")
            {
                return null;
            }
            NormalMapParams p = new NormalMapParams();
            if (name == "microfacetdefault" || name == "default")
            {
                p.model = NormalMapModel.Default;
            }
            else if (name == "microfacet")
            {
                p.model = NormalMapModel.Walk;
            }
            else if (name == "microfacetnonseparable")
            {
                p.model = NormalMapModel.NonSeparable;
            }
            else
            {
                pbrt.ErrorExit(pbrt.StringPrintf("%s: unknown normalmapmodel", name));
            }
            p.maxOrder = maxOrder;
            p.saturation = saturation;
            p.geometric = reference == "geometric";
            return p;
        }
    }

    // (the C++ code keeps a registry from the material's address to the parameters; here the material holds them)
    public abstract unsafe partial class Material
    {
        public NormalMapParams normalMapModel = null;
    }

    // MicrofacetNormalMapBxDF Definition
    // All vectors live in the BSDF's local frame (perturbed normal p = +z).
    public sealed unsafe class MicrofacetNormalMapBxDF : BxDF
    {
        public MicrofacetNormalMapBxDF(NormalMapParams parameters, Vector3f gIn, BxDF bxdf)
        {
            this.parameters = parameters;
            g = Normalize(gIn);
            this.bxdf = bxdf;
            c = g.z;
            s = std.sqrt(std.max<Float>(0, 1 - c * c));
            valid = c > 1e-3f && s > 1e-4f && IsNonSpecular(bxdf.Flags()) && IsReflective(bxdf.Flags());
            transmissive = IsTransmissive(bxdf.Flags());
            if (!valid)
            {
                return;
            }
            // wt(wp) = normalize(-wp.x, -wp.y, 0) in the geometric frame, expressed here in the perturbed frame:
            // t = -(p - c g) / |p - c g|.
            t = -Normalize(new Vector3f(-c * g.x, -c * g.y, 1 - c * g.z));
            Vector3f k = Cross(new Vector3f(0, 0, 1), t);
            Float kl = Length(k);
            if (kl < 1e-6f)
            {
                tx = new Vector3f(1, 0, 0);
                ty = new Vector3f(0, 1, 0);
            }
            else
            {
                k /= kl;
                Float cp = t.z;
                tx = Rotate(new Vector3f(1, 0, 0), k, kl, cp);
                ty = Rotate(new Vector3f(0, 1, 0), k, kl, cp);
            }
            order = (int)std.floor(parameters.maxOrder);
            frac = parameters.maxOrder - order;
            hasFrac = frac > 1e-8f;
        }

        private static Vector3f Rotate(Vector3f v, Vector3f k, Float kl, Float cp)
        {
            return v * cp + Cross(k, v) * kl + k * (Dot(k, v) * (1 - cp));
        }

        public bool Valid() { return valid; }

        public override BxDFFlags Flags()
        {
            return transmissive ? (bxdf.Flags() | BxDFFlags.GlossyReflection) : BxDFFlags.GlossyReflection;
        }

        // The transmission of a viewer above the surface to a light below it: through the mapped facet, met at once (with
        // the share LambdaP of the projected area) or after the mirror of the tangent facet. `first` and `second` are the
        // two values of f, with their weights
        private bool Crossing(Vector3f wo, Vector3f wi, TransportMode mode, out SampledSpectrum first, out SampledSpectrum second, out Vector3f woR)
        {
            first = new SampledSpectrum(0f);
            second = new SampledSpectrum(0f);
            woR = PlaneReflect(wo);
            if (!transmissive || Dot(wo, g) <= 0 || Dot(wi, g) > 0 || wi.z >= 0)
            {
                return false;
            }
            Float lam = LambdaP(wo);
            if (lam > 0 && wo.z > 0)
            {
                first = lam * bxdf.f(wo, wi, mode);
            }
            if (lam < 1 && woR.z > 0)
            {
                second = (1 - lam) * bxdf.f(woR, wi, mode);
            }
            return true;
        }

        private Float CrossingPDF(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (wi.z >= 0)
            {
                return 0;
            }
            Float lam = LambdaP(wo);
            Float pdf = 0;
            if (lam > 0 && wo.z > 0)
            {
                pdf += lam * bxdf.PDF(wo, wi, mode, BxDFReflTransFlags.All);
            }
            Vector3f woR = PlaneReflect(wo);
            if (lam < 1 && woR.z > 0)
            {
                pdf += (1 - lam) * bxdf.PDF(woR, wi, mode, BxDFReflTransFlags.All);
            }
            return pdf;
        }

        public override void Regularize() { bxdf.Regularize(); }

        public override string ToString()
        {
            return StringPrintf("[ MicrofacetNormalMapBxDF model: %s maxOrder: %f saturation: %f g: %s bxdf: %s ]", parameters.model.ToString(),
                                parameters.maxOrder, parameters.saturation, g, bxdf);
        }

        // pbrt API: wo = viewer direction, wi = light direction.
        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            if (transmissive && Dot(wo, g) <= 0)
            {
                return bxdf.f(wo, wi, mode);
            }
            if (Crossing(wo, wi, mode, out SampledSpectrum first, out SampledSpectrum second, out Vector3f _))
            {
                if (parameters.model == NormalMapModel.Default)
                {
                    return first + second;
                }
                // The transmission of every vertex of the walk
                RNG rng = new RNG(Hash(GetOptions().seed, wo), Hash(wi));
                SampledSpectrum crossed = parameters.model == NormalMapModel.NonSeparable ? EvalCrossingNS(wo, wi, mode, ref rng)
                                                                                           : EvalCrossing(wo, wi, mode, ref rng);
                return crossed / std.max<Float>(AbsCosTheta(wi), 1e-6f);
            }
            SampledSpectrum lo = EvalCos(wo, wi, mode);
            return lo / std.max<Float>(AbsCosTheta(wi), 1e-6f);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (transmissive && Dot(wo, g) <= 0)
            {
                return bxdf.PDF(wo, wi, mode, sampleFlags);
            }
            bool both = transmissive && (sampleFlags & BxDFReflTransFlags.Transmission) != 0;
            if (transmissive && Dot(wi, g) <= 0)
            {
                if (!both || wi.z >= 0)
                {
                    return 0;
                }
                // The density of the first vertex of the walk. A later vertex may transmit where the first cannot: as for
                // the reflection of the walks (PdfMirror()), a part that is nowhere 0 below the surface
                Float crossing = CrossingPDF(wo, wi, mode);
                return parameters.model == NormalMapModel.Default ? crossing : crossing + .1f * std.max<Float>(0, -Dot(wi, g)) * InvPi;
            }
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            {
                return 0;
            }
            if (Dot(wo, g) <= 0 || Dot(wi, g) <= 0)
            {
                return 0;
            }
            // (the densities of the BxDF inside are those of all that it samples: its reflection has the share that it has there)
            Float saturation = Saturation();
            switch (parameters.model)
            {
                case NormalMapModel.Default:
                    return PdfDefault(wo, wi, mode);
                default:
                    return (1 - saturation) * PdfMirror(wo, wi, mode) + saturation * PdfSame(wo, wi, mode);
            }
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (transmissive && Dot(wo, g) <= 0)
            {
                pstd.optional<BSDFSample> inside = bxdf.Sample_f(wo, uc, u, mode, sampleFlags);
                if (inside && (!IsFinite(inside.value().f[0]) || !IsFinite(inside.value().pdf)))
                {
                    return default;
                }
                return inside;
            }
            if (Dot(wo, g) <= 0 || (!transmissive && (sampleFlags & BxDFReflTransFlags.Reflection) == 0))
            {
                return default;
            }
            RNG rng = new RNG(Hash(GetOptions().seed, wo, uc), Hash(u));
            Float saturation = Saturation();
            Vector3f wi;
            SampledSpectrum e;
            Float crossedEta;
            bool ok;
            switch (parameters.model)
            {
                case NormalMapModel.Default:
                    ok = SampleDefault(wo, uc, u, mode, ref rng, out wi, out e, out crossedEta);
                    break;
                default:
                    if (parameters.model == NormalMapModel.NonSeparable)
                    {
                        if (saturation > rng.Uniform<Float>())
                        {
                            ok = SampleSameNS(wo, uc, u, mode, ref rng, out wi, out e, out crossedEta);
                        }
                        else
                        {
                            ok = SampleMirrorNS(wo, uc, u, mode, ref rng, out wi, out e, out crossedEta);
                        }
                    }
                    else if (saturation > rng.Uniform<Float>())
                    {
                        ok = SampleSame(wo, uc, u, mode, ref rng, out wi, out e, out crossedEta);
                    }
                    else
                    {
                        ok = SampleMirror(wo, uc, u, mode, ref rng, out wi, out e, out crossedEta);
                    }
                    break;
            }
            if (!ok || !(bool)e || wi.z == 0)
            {
                return default;
            }
            if (crossedEta != 0)
            {
                // The walk ended with a transmission: the refracted ray, below the surface
                if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0 || Dot(wi, g) >= 0)
                {
                    return default;
                }
                return new BSDFSample(e / std.max<Float>(AbsCosTheta(wi), 1e-6f), wi, 1, BxDFFlags.GlossyTransmission, crossedEta, true);
            }
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || Dot(wi, g) <= 0)
            {
                return default;
            }
            return new BSDFSample(e / std.max<Float>(AbsCosTheta(wi), 1e-6f), wi, 1, BxDFFlags.GlossyReflection, 1f, true);
        }

        // The tangent facet is a mirror for a BxDF that transmits
        private Float Saturation()
        {
            return transmissive ? 0 : parameters.saturation;
        }

        // ---- polarisation (the analytic model) ----
        public override bool HasMuellerMatrix() { return parameters.model == NormalMapModel.Default || transmissive; }

        // The matrix of the BxDF inside for a pair of directions, with a first element of 1 and its frames
        private PolarisedLightSystem.Attenuation NestedMatrix(BSDF bsdf, Vector3f a, Vector3f b, TransportMode mode)
        {
            PolarisedLightSystem.Attenuation matrix;
            if (PolarisedPathIntegrator.RoughAttenuation(bsdf.WithBxDF(bxdf), null, bsdf.LocalToRender(a), bsdf.LocalToRender(b),
                                                         new SampledSpectrum(1f), mode, out matrix))
            {
                return matrix;
            }
            return PolarisedLightSystem.Attenuation.Lambert(new SampledSpectrum(1f));
        }

        // The mirror reflection by the tangent facet from a to b (both leave it): a conductor without loss
        private PolarisedLightSystem.Attenuation MirrorMatrix(BSDF bsdf, Vector3f a, Vector3f b, bool fromCamera)
        {
            Vector3f m = Dot(a, t) >= 0 ? t : -t;
            return PolarisedFacet.Conductor(bsdf, 1, a, b, m, new SampledSpectrum(1e-4f), new SampledSpectrum(1e4f), fromCamera);
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
            if (transmissive && Dot(wo, g) <= 0)
            {
                // The BxDF as it is: its own matrix
                polarised = PolarisedPathIntegrator.RoughAttenuation(bsdf.WithBxDF(bxdf), null, bsdf.LocalToRender(wo), bsdf.LocalToRender(wi),
                                                                     new SampledSpectrum(1f), mode, out matrix);
                return value;
            }
            if (Crossing(wo, wi, mode, out SampledSpectrum crossFirst, out SampledSpectrum crossSecond, out Vector3f woR))
            {
                // Through the mapped facet, and by the mirror of the tangent facet first
                bool camera = mode == TransportMode.Radiance;
                Vector3f woRender = bsdf.LocalToRender(wo), wiRender = bsdf.LocalToRender(wi);
                Vector3f exit = camera ? woRender : wiRender;
                Vector3f enter = camera ? -wiRender : -woRender;
                PolarisedLightSystem.Attenuation crossing = default;
                bool hasCrossing = false;
                if ((bool)crossFirst)
                {
                    Accumulate(ref crossing, ref hasCrossing, NestedMatrix(bsdf, wo, wi, mode), crossFirst, exit, enter);
                }
                if ((bool)crossSecond)
                {
                    PolarisedLightSystem.Attenuation chain = MirrorMatrix(bsdf, wo, -woR, camera);
                    chain = PolarisedFacet.Compose(chain, true, NestedMatrix(bsdf, woR, wi, mode), camera);
                    Accumulate(ref crossing, ref hasCrossing, chain, crossSecond, exit, enter);
                }
                if (hasCrossing)
                {
                    polarised = PolarisedFacet.Normalise(crossing, crossFirst + crossSecond, new SampledSpectrum(1f), out matrix);
                }
                return value;
            }
            if (parameters.model != NormalMapModel.Default || Dot(wo, g) <= 0 || Dot(wi, g) <= 0)
            {
                return value;
            }
            // The three paths of EvalDefault(), in the names of the plugins (wi the viewer, wo the light)
            Vector3f viewer = wo, light = wi;
            bool fromCamera = mode == TransportMode.Radiance;
            Vector3f viewerRender = bsdf.LocalToRender(viewer), lightRender = bsdf.LocalToRender(light);
            Vector3f directionExit = fromCamera ? viewerRender : lightRender;
            Vector3f directionEnter = fromCamera ? -lightRender : -viewerRender;
            Float shadowing = G1(light);
            Float lam = LambdaP(viewer);
            PolarisedLightSystem.Attenuation sum = default;
            bool hasSum = false;
            SampledSpectrum total = new SampledSpectrum(0f);
            // i -> p -> o
            SampledSpectrum direct = NEval(0, viewer, light, mode) * (lam * shadowing);
            if ((bool)direct)
            {
                Accumulate(ref sum, ref hasSum, NestedMatrix(bsdf, viewer, light, mode), direct, directionExit, directionEnter);
                total += direct;
            }
            // i -> p -> t -> o: the facet scatters to the mirror image of the light, the tangent facet reflects it
            if (Dot(light, t) > 0)
            {
                Vector3f lightR = PlaneReflect(light);
                SampledSpectrum second = NEval(0, viewer, lightR, mode) * (lam * (1 - G1(lightR)) * shadowing);
                if ((bool)second)
                {
                    PolarisedLightSystem.Attenuation chain = NestedMatrix(bsdf, viewer, lightR, mode);
                    chain = PolarisedFacet.Compose(chain, true, MirrorMatrix(bsdf, -lightR, light, fromCamera), fromCamera);
                    Accumulate(ref sum, ref hasSum, chain, second, directionExit, directionEnter);
                    total += second;
                }
            }
            // i -> t -> p -> o: the tangent facet reflects the viewer's ray onto the facet
            if (Dot(viewer, t) > 0)
            {
                Vector3f viewerR = PlaneReflect(viewer);
                SampledSpectrum second = NEval(0, viewerR, light, mode) * ((1 - lam) * shadowing);
                if ((bool)second)
                {
                    PolarisedLightSystem.Attenuation chain = MirrorMatrix(bsdf, viewer, -viewerR, fromCamera);
                    chain = PolarisedFacet.Compose(chain, true, NestedMatrix(bsdf, viewerR, light, mode), fromCamera);
                    Accumulate(ref sum, ref hasSum, chain, second, directionExit, directionEnter);
                    total += second;
                }
            }
            if (hasSum)
            {
                polarised = PolarisedFacet.Normalise(sum, total, new SampledSpectrum(1f), out matrix);
            }
            return value;
        }

        // The sample of the analytic model with the matrix of its pair of directions (f is not a random walk: the matrix of
        // the pair is the one of the sample)
        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode,
                                                           BxDFReflTransFlags sampleFlags, out bool polarised,
                                                           out PolarisedLightSystem.Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            pstd.optional<BSDFSample> bs = Sample_f(wo, uc, u, mode, sampleFlags);
            if (!bs)
            {
                return bs;
            }
            if (transmissive && (Dot(wo, g) <= 0 || bs.value().IsTransmission()))
            {
                // (what crosses the surface is not a random walk either)
                f(bsdf, wo, bs.value().wi, mode, out polarised, out matrix);
            }
            else if (parameters.model == NormalMapModel.Default)
            {
                f(bsdf, wo, bs.value().wi, mode, out polarised, out matrix);
            }
            return bs;
        }

        // ---- geometry helpers (p-frame) ----
        private static Float PDot(Vector3f a, Vector3f b) { return std.max<Float>(0, Dot(a, b)); }

        private Float G1(Vector3f w)
        {
            Float d = PDot(w, new Vector3f(0, 0, 1)) + PDot(w, t) * s;
            if (d <= 0)
            {
                return 0;
            }
            return std.min<Float>(1, std.max<Float>(0, Dot(w, g)) * std.max<Float>(0, c) / d);
        }

        private Float LambdaP(Vector3f wi)
        {
            Float ip = PDot(new Vector3f(0, 0, 1), wi);
            Float d = ip + PDot(t, wi) * s;
            return d > 0 ? ip / d : 0;
        }

        // Reflection of a direction across the plane of the tangent facet.
        private Vector3f PlaneReflect(Vector3f v) { return v - 2 * Dot(v, t) * t; }

        private Vector3f ToFacet(int m, Vector3f v)
        {
            return m == 0 ? v : new Vector3f(Dot(v, tx), Dot(v, ty), Dot(v, t));
        }

        private Vector3f FromFacet(int m, Vector3f v)
        {
            return m == 0 ? v : v.x * tx + v.y * ty + v.z * t;
        }

        // ---- nested BxDF queries (Mitsuba conventions: eval includes cosine of wo) ----
        private SampledSpectrum NEval(int m, Vector3f a, Vector3f b, TransportMode mode)
        {
            Vector3f al = ToFacet(m, a), bl = ToFacet(m, b);
            if (al.z <= 0 || bl.z <= 0)
            {
                return new SampledSpectrum(0f);
            }
            return bxdf.f(al, bl, mode) * bl.z;
        }

        // What is asked of the BxDF inside: on the mapped facet all that it does, on the tangent facet its reflection
        private BxDFReflTransFlags NFlags(int m)
        {
            return (m == 0 && transmissive) ? BxDFReflTransFlags.All : BxDFReflTransFlags.Reflection;
        }

        private Float NPdf(int m, Vector3f a, Vector3f b, TransportMode mode)
        {
            Vector3f al = ToFacet(m, a), bl = ToFacet(m, b);
            if (al.z <= 0 || bl.z <= 0)
            {
                return 0;
            }
            return bxdf.PDF(al, bl, mode, NFlags(m));
        }

        // crossedEta is not 0 when the sample is a transmission (the relative index of the sample): b is then the refracted
        // direction, below the mapped facet
        private bool NSample(int m, Vector3f a, TransportMode mode, Float uc, Point2f u, out Vector3f b, out SampledSpectrum weight, out Float crossedEta)
        {
            b = default;
            weight = default;
            crossedEta = 0;
            Vector3f al = ToFacet(m, a);
            if (al.z <= 0)
            {
                return false;
            }
            pstd.optional<BSDFSample> bs = bxdf.Sample_f(al, uc, u, mode, NFlags(m));
            if (!bs || !(bool)bs.value().f || bs.value().pdf == 0)
            {
                return false;
            }
            if (bs.value().IsTransmission())
            {
                if (bs.value().wi.z >= 0)
                {
                    return false;
                }
                crossedEta = bs.value().eta != 0 ? bs.value().eta : 1;
            }
            else if (bs.value().wi.z <= 0)
            {
                return false;
            }
            b = FromFacet(m, bs.value().wi);
            weight = bs.value().f * AbsCosTheta(bs.value().wi) / bs.value().pdf;
            // (a BxDF asked along its own surface may answer with an infinite value over an infinite density)
            return IsFinite(weight[0]);
        }

        // The transmission of the BxDF of the mapped facet from a (above it) to b (below it), with the cosine of b
        private SampledSpectrum NEvalCrossing(Vector3f a, Vector3f b, TransportMode mode)
        {
            if (a.z <= 0 || b.z >= 0)
            {
                return new SampledSpectrum(0f);
            }
            return bxdf.f(a, b, mode) * AbsCosTheta(b);
        }

        private Float PdfOverCos(int m, Vector3f a, Vector3f b, TransportMode mode)
        {
            Float bz = ToFacet(m, b).z;
            return bz > 0 ? NPdf(m, a, b, mode) / bz : 0;
        }

        // ================= default model (analytic second order) =================
        private SampledSpectrum EvalDefault(Vector3f wi, Vector3f wo, TransportMode mode)
        {
            Float shadowing = G1(wo);
            Float lam = LambdaP(wi);
            SampledSpectrum value = new SampledSpectrum(0f);
            // i -> p -> o
            value += NEval(0, wi, wo, mode) * (lam * shadowing);
            // i -> p -> t -> o
            if (Dot(wo, t) > 0)
            {
                Vector3f woR = PlaneReflect(wo);
                value += NEval(0, wi, woR, mode) * (lam * (1 - G1(woR)) * shadowing);
            }
            // i -> t -> p -> o
            if (Dot(wi, t) > 0)
            {
                Vector3f wiR = PlaneReflect(wi);
                value += NEval(0, wiR, wo, mode) * ((1 - lam) * shadowing);
            }
            return value;
        }

        private Float PdfDefault(Vector3f wi, Vector3f wo, TransportMode mode)
        {
            Float lam = LambdaP(wi);
            Float pdf = 0;
            if (lam > 0)
            {
                pdf += lam * NPdf(0, wi, wo, mode) * G1(wo);
                if (Dot(wo, t) > 1e-6f)
                {
                    Vector3f woR = PlaneReflect(wo);
                    pdf += lam * NPdf(0, wi, woR, mode) * (1 - G1(woR));
                }
            }
            if (lam < 1 && Dot(wi, t) > 1e-6f)
            {
                Vector3f wiR = PlaneReflect(wi);
                pdf += (1 - lam) * NPdf(0, wiR, wo, mode);
            }
            return pdf;
        }

        private bool SampleDefault(Vector3f wi, Float uc, Point2f u, TransportMode mode, ref RNG rng, out Vector3f wo, out SampledSpectrum energy,
                                   out Float crossedEta)
        {
            wo = default;
            energy = default;
            crossedEta = 0;
            Vector3f wr = -wi;
            SampledSpectrum e = new SampledSpectrum(1f);
            Vector3f b;
            SampledSpectrum w;
            if (rng.Uniform<Float>() < LambdaP(wi))
            {
                if (!NSample(0, -wr, mode, uc, u, out b, out w, out crossedEta))
                {
                    return false;
                }
                e *= w;
                wr = b;
                if (crossedEta != 0)
                {
                    wo = wr;
                    energy = e;
                    return Dot(wr, g) < 0;
                }
                if (rng.Uniform<Float>() > G1(wr))
                {
                    wr = wr - 2 * Dot(wr, t) * t;
                    e *= G1(wr);
                }
            }
            else
            {
                wr = wr - 2 * Dot(wr, t) * t;
                if (!NSample(0, -wr, mode, uc, u, out b, out w, out crossedEta))
                {
                    return false;
                }
                e *= w;
                wr = b;
                if (crossedEta != 0)
                {
                    wo = wr;
                    energy = e;
                    return Dot(wr, g) < 0;
                }
                e *= G1(wr);
            }
            wo = wr;
            energy = e;
            return Dot(wr, g) > 0;
        }

        // ================= general random-walk model =================
        private SampledSpectrum EvalCos(Vector3f wi, Vector3f wo, TransportMode mode)
        {
            if (Dot(wi, g) <= 0 || Dot(wo, g) <= 0)
            {
                return new SampledSpectrum(0f);
            }
            if (parameters.model == NormalMapModel.Default)
            {
                return EvalDefault(wi, wo, mode);
            }
            RNG rng = new RNG(Hash(GetOptions().seed, wi), Hash(wo));
            Float weight = 2;
            if (rng.Uniform<Float>() < 0.5f)
            {
                Vector3f swap = wi;
                wi = wo;
                wo = swap;
                weight *= Dot(wi, g) / Dot(wo, g);
            }
            bool nonsep = parameters.model == NormalMapModel.NonSeparable;
            SampledSpectrum v;
            if (Saturation() > rng.Uniform<Float>())
            {
                v = nonsep ? EvalSameNS(wi, wo, mode, ref rng) : EvalSame(wi, wo, mode, ref rng);
            }
            else
            {
                v = nonsep ? EvalMirrorNS(wi, wo, mode, ref rng) : EvalMirror(wi, wo, mode, ref rng);
            }
            return v * weight;
        }

        // Adds `contribution` honouring the (fractional) maximum order. Returns true to stop.
        private bool Accumulate(SampledSpectrum contribution, int i, ref SampledSpectrum value)
        {
            if (IsFinite(contribution[0]))
            {
                if (i == order)
                {
                    value += contribution * frac;
                    return true;
                }
                value += contribution;
            }
            return false;
        }

        private bool Continue(int i) { return i < order || (i == order && hasFrac); }

        private SampledSpectrum EvalSame(Vector3f wi, Vector3f wo, TransportMode mode, ref RNG rng)
        {
            Vector3f wr = -wi;
            SampledSpectrum energy = new SampledSpectrum(1f), value = new SampledSpectrum(0f);
            Float pdfWi = 0;
            int cur = rng.Uniform<Float>() > LambdaP(wi) ? 1 : 0;
            Float shadowing = G1(wo);
            Float G1_;
            int i = 0;
            do
            {
                Point2f smp = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                Float ucs = rng.Uniform<Float>();
                Vector3f a = -wr;
                SampledSpectrum contribution = NEval(cur, a, wo, mode);
                if (i > 0)
                {
                    Float pdfWo = PdfOverCos(cur, a, wo, mode);
                    contribution *= pdfWi / (pdfWi + pdfWo);
                }
                else
                {
                    contribution *= .5f;
                }
                contribution *= shadowing * energy;
                if (Accumulate(contribution, i, ref value))
                {
                    break;
                }
                Vector3f b;
                SampledSpectrum w;
                if (!NSample(cur, a, mode, ucs, smp, out b, out w, out Float crossed) || crossed != 0)
                {
                    break;
                }
                if (i == 0)
                {
                    pdfWi = PdfOverCos(cur, a, b, mode);
                }
                energy *= w;
                wr = b;
                G1_ = G1(wr);
                cur = 1 - cur;
                i++;
            } while (Continue(i) && rng.Uniform<Float>() > G1_);
            return value;
        }

        private SampledSpectrum EvalMirror(Vector3f wi, Vector3f wo, TransportMode mode, ref RNG rng)
        {
            Vector3f woR = PlaneReflect(wo);
            Float notShadowedWpMirror = 1 - G1(woR);
            Float shadowing = G1(wo);
            int i = 0;
            Float G1_ = 0;
            SampledSpectrum energy = new SampledSpectrum(1f), value = new SampledSpectrum(0f);
            Float pdfWi = 0;
            Vector3f wr = -wi;
            if (rng.Uniform<Float>() > LambdaP(wi))
            {
                wr = wr - 2 * Dot(wr, t) * t;
                i++;
            }
            bool singleScattering = true;
            do
            {
                Point2f smp = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                Float ucs = rng.Uniform<Float>();
                Vector3f a = -wr;
                SampledSpectrum contribution = NEval(0, a, wo, mode);
                if (!singleScattering)
                {
                    Float pdfWo = PdfOverCos(0, a, wo, mode);
                    contribution *= pdfWi / (pdfWi + pdfWo);
                }
                else
                {
                    contribution *= .5f;
                }
                contribution *= shadowing * energy;
                if (Accumulate(contribution, i, ref value))
                {
                    break;
                }

                i++;
                if (!Continue(i))
                {
                    break;
                }

                contribution = NEval(0, a, woR, mode);
                if (!singleScattering)
                {
                    Float pdfWo = PdfOverCos(0, a, woR, mode);
                    contribution *= pdfWi / (pdfWi + pdfWo);
                }
                else
                {
                    contribution *= .5f;
                }
                contribution *= shadowing * energy * notShadowedWpMirror;
                if (Accumulate(contribution, i, ref value))
                {
                    break;
                }

                Vector3f b;
                SampledSpectrum w;
                if (!NSample(0, a, mode, ucs, smp, out b, out w, out Float crossed) || crossed != 0)
                {
                    break;
                }
                if (singleScattering)
                {
                    pdfWi = PdfOverCos(0, a, b, mode);
                    singleScattering = false;
                }
                energy *= w;
                wr = b;
                G1_ = G1(wr);
                if (i >= order || rng.Uniform<Float>() < G1_)
                {
                    break;
                }

                wr = wr - 2 * Dot(wr, t) * t;
                G1_ = G1(wr);
                i++;
            } while (Continue(i) && rng.Uniform<Float>() > G1_);
            return value;
        }

        // The transmission from the viewer wi, above the surface, to wo below it: the walk of SampleMirror(), and at each of
        // its vertices on the mapped facet what the BxDF transmits towards wo (nothing shadows a direction below the surface)
        private SampledSpectrum EvalCrossing(Vector3f wi, Vector3f wo, TransportMode mode, ref RNG rng)
        {
            int i = 0;
            Float G1_;
            Vector3f wr = -wi;
            SampledSpectrum energy = new SampledSpectrum(1f), value = new SampledSpectrum(0f);
            if (rng.Uniform<Float>() > LambdaP(wi))
            {
                wr = wr - 2 * Dot(wr, t) * t;
                i++;
            }
            do
            {
                if (i >= order)
                {
                    if (i == order && hasFrac)
                    {
                        energy *= frac;
                    }
                    else
                    {
                        break;
                    }
                }
                Point2f smp = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                Float ucs = rng.Uniform<Float>();
                Vector3f a = -wr;
                SampledSpectrum contribution = NEvalCrossing(a, wo, mode) * energy;
                if (IsFinite(contribution[0]))
                {
                    value += contribution;
                }
                Vector3f b;
                SampledSpectrum w;
                if (!NSample(0, a, mode, ucs, smp, out b, out w, out Float crossed) || crossed != 0)
                {
                    break;
                }
                energy *= w;
                wr = b;
                G1_ = G1(wr);
                i++;
                if (rng.Uniform<Float>() < G1_)
                {
                    break;
                }
                if (i >= order)
                {
                    if (i == order && hasFrac)
                    {
                        energy *= frac;
                    }
                    else
                    {
                        break;
                    }
                }
                wr = wr - 2 * Dot(wr, t) * t;
                G1_ = G1(wr);
                i++;
            } while (rng.Uniform<Float>() > G1_);
            return value;
        }

        // The same with the walk of SampleMirrorNS(), which follows the height of the ray
        private SampledSpectrum EvalCrossingNS(Vector3f wi, Vector3f wo, TransportMode mode, ref RNG rng)
        {
            Float height = 1 - rng.Uniform<Float>() * G1(wi);
            Float facetWidth = c / s;
            int i = 0;
            Vector3f wr = -wi;
            SampledSpectrum energy = new SampledSpectrum(1f), value = new SampledSpectrum(0f);
            Vector3f pn = new Vector3f(0, 0, 1);
            if (rng.Uniform<Float>() > LambdaP(wi))
            {
                wr = wr - 2 * Dot(wr, t) * t;
                Float d = -Dot(g * height, pn) / Dot(wr, pn);
                height += d * Dot(wr, g);
                i++;
            }
            do
            {
                if (i >= order)
                {
                    break;
                }
                Point2f smp = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                Float ucs = rng.Uniform<Float>();
                Vector3f a = -wr;
                SampledSpectrum contribution = NEvalCrossing(a, wo, mode) * energy;
                if (IsFinite(contribution[0]))
                {
                    value += contribution;
                }
                Vector3f b;
                SampledSpectrum w;
                if (!NSample(0, a, mode, ucs, smp, out b, out w, out Float crossed) || crossed != 0)
                {
                    break;
                }
                energy *= w;
                wr = b;
                Vector3f pos = g * height + t * (height * facetWidth);
                Float d = -Dot(pos, t) / Dot(wr, t);
                if (d < 0)
                {
                    break;
                }
                height += d * Dot(wr, g);
                i++;
                if (height > 1)
                {
                    break;
                }
                if (i >= order)
                {
                    break;
                }
                wr = wr - 2 * Dot(wr, t) * t;
                d = -Dot(g * height, pn) / Dot(wr, pn);
                if (d < 0)
                {
                    break;
                }
                height += d * Dot(wr, g);
                i++;
            } while (height <= 1);
            return value;
        }

        private Float PdfSame(Vector3f wi, Vector3f wo, TransportMode mode)
        {
            Float lam = LambdaP(wi);
            Float pdf = 0;
            if (lam > 0)
            {
                pdf += lam * NPdf(0, wi, wo, mode);
            }
            if (lam < 1)
            {
                pdf += (1 - lam) * NPdf(1, wi, wo, mode);
            }
            pdf *= G1(wo);
            return pdf + .1f * std.max<Float>(0, Dot(wo, g)) * InvPi;
        }

        private Float PdfMirror(Vector3f wi, Vector3f wo, TransportMode mode)
        {
            Float lam = LambdaP(wi);
            Float pdf = 0;
            if (lam > 0)
            {
                pdf += lam * NPdf(0, wi, wo, mode);
                if (Dot(wo, t) > 1e-6f)
                {
                    Vector3f woR = PlaneReflect(wo);
                    pdf += lam * NPdf(0, wi, woR, mode) * (1 - G1(woR));
                }
            }
            if (lam < 1 && Dot(wi, t) > 1e-6f)
            {
                Vector3f wiR = PlaneReflect(wi);
                pdf += (1 - lam) * NPdf(0, wiR, wo, mode);
            }
            pdf *= G1(wo);
            return pdf + .1f * std.max<Float>(0, Dot(wo, g)) * InvPi;
        }

        private bool SampleSame(Vector3f wi, Float uc, Point2f u, TransportMode mode, ref RNG rng, out Vector3f wo, out SampledSpectrum energyOut,
                                out Float crossedEta)
        {
            wo = default;
            energyOut = default;
            crossedEta = 0;
            int cur = rng.Uniform<Float>() > LambdaP(wi) ? 1 : 0;
            int i = 0;
            Vector3f wr = -wi;
            SampledSpectrum energy = new SampledSpectrum(1f);
            Float G1_;
            bool first = true;
            do
            {
                if (i >= order)
                {
                    if (i == order && hasFrac)
                    {
                        energy *= frac;
                    }
                    else
                    {
                        return false;
                    }
                }
                Vector3f b;
                SampledSpectrum w;
                Float ucs = first ? uc : rng.Uniform<Float>();
                Point2f smp = first ? u : new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                first = false;
                if (!NSample(cur, -wr, mode, ucs, smp, out b, out w, out crossedEta))
                {
                    return false;
                }
                energy *= w;
                wr = b;
                if (crossedEta != 0)
                {
                    wo = wr;
                    energyOut = energy;
                    return Dot(wr, g) < 0;
                }
                G1_ = G1(wr);
                cur = 1 - cur;
                i++;
            } while (rng.Uniform<Float>() > G1_);
            wo = wr;
            energyOut = energy;
            return Dot(wr, g) > 0;
        }

        private bool SampleMirror(Vector3f wi, Float uc, Point2f u, TransportMode mode, ref RNG rng, out Vector3f wo, out SampledSpectrum energyOut,
                                out Float crossedEta)
        {
            wo = default;
            energyOut = default;
            crossedEta = 0;
            int i = 0;
            Float G1_;
            Vector3f wr = -wi;
            SampledSpectrum energy = new SampledSpectrum(1f);
            if (rng.Uniform<Float>() > LambdaP(wi))
            {
                wr = wr - 2 * Dot(wr, t) * t;
                i++;
            }
            bool first = true;
            do
            {
                if (i >= order)
                {
                    if (i == order && hasFrac)
                    {
                        energy *= frac;
                    }
                    else
                    {
                        return false;
                    }
                }
                Vector3f b;
                SampledSpectrum w;
                Float ucs = first ? uc : rng.Uniform<Float>();
                Point2f smp = first ? u : new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                first = false;
                if (!NSample(0, -wr, mode, ucs, smp, out b, out w, out crossedEta))
                {
                    return false;
                }
                energy *= w;
                wr = b;
                if (crossedEta != 0)
                {
                    wo = wr;
                    energyOut = energy;
                    return Dot(wr, g) < 0;
                }
                G1_ = G1(wr);
                i++;
                if (rng.Uniform<Float>() < G1_)
                {
                    break;
                }
                if (i >= order)
                {
                    if (i == order && hasFrac)
                    {
                        energy *= frac;
                    }
                    else
                    {
                        return false;
                    }
                }
                wr = wr - 2 * Dot(wr, t) * t;
                G1_ = G1(wr);
                i++;
            } while (rng.Uniform<Float>() > G1_);
            wo = wr;
            energyOut = energy;
            return Dot(wr, g) > 0;
        }

        // ================= height-correlated (non-separable) variants =================
        // Intersects the height interval [hmin, hmax] with the plane through the origin offset by the facet geometry and
        // advances it along direction wr (see the nonseparable plugin).
        private bool AdvanceHeights(Vector3f wr, Vector3f otherNormal, Float facetWidth, bool offset, ref Float hmin, ref Float hmax)
        {
            Vector3f pmin = g * hmin, pmax = g * hmax;
            if (offset)
            {
                pmin += otherNormal * (hmin * facetWidth);
                pmax += otherNormal * (hmax * facetWidth);
            }
            Float dn = Dot(wr, otherNormal);
            Float dmin = -Dot(pmin, otherNormal) / dn;
            Float dmax = -Dot(pmax, otherNormal) / dn;
            if (dmin < 0 || dmax < 0)
            {
                return false;
            }
            Float wz = Dot(wr, g);
            hmin += dmin * wz;
            hmax += dmax * wz;
            return true;
        }

        private SampledSpectrum EvalSameNS(Vector3f wi, Vector3f wo, TransportMode mode, ref RNG rng)
        {
            Vector3f wr = -wi;
            SampledSpectrum energy = new SampledSpectrum(1f), value = new SampledSpectrum(0f);
            Float pdfWi = 0;
            int cur = rng.Uniform<Float>() > LambdaP(wi) ? 1 : 0;
            Float maxH = 1, minH = 1 - G1(wi);
            Float facetWidth = c / s;
            int i = 0;
            Float shadowing = G1(wo);
            Float G1_;
            do
            {
                Point2f smp = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                Float ucs = rng.Uniform<Float>();
                Vector3f a = -wr;
                Float weight;
                if (i > 0)
                {
                    weight = pdfWi / (pdfWi + PdfOverCos(cur, a, wo, mode));
                }
                else
                {
                    weight = .5f;
                }
                if (maxH > 1 - shadowing)
                {
                    Float ms = std.max<Float>(0, maxH - std.max(minH, 1 - shadowing));
                    SampledSpectrum contribution = NEval(cur, a, wo, mode) * energy * (ms / (maxH - minH) * weight);
                    if (IsFinite(contribution[0]))
                    {
                        value += contribution;
                    }
                }
                Vector3f b;
                SampledSpectrum w;
                if (!NSample(cur, a, mode, ucs, smp, out b, out w, out Float crossed) || crossed != 0)
                {
                    break;
                }
                if (i == 0)
                {
                    pdfWi = PdfOverCos(cur, a, b, mode);
                }
                energy *= w;
                wr = b;
                bool isP = cur == 0;
                Vector3f other = isP ? t : new Vector3f(0, 0, 1);
                if (!AdvanceHeights(wr, other, facetWidth, isP, ref minH, ref maxH))
                {
                    break;
                }
                cur = 1 - cur;
                i++;
                G1_ = (maxH - std.max<Float>(minH, 1)) / (maxH - minH);
                maxH = std.min<Float>(1, maxH);
            } while (i < order && rng.Uniform<Float>() > G1_);
            return value;
        }

        private SampledSpectrum EvalMirrorNS(Vector3f wi, Vector3f wo, TransportMode mode, ref RNG rng)
        {
            Vector3f woR = PlaneReflect(wo);
            Float shadowing = G1(wo);
            Float rMin = 1 - shadowing, rMax = 1;
            {
                Vector3f wrl = -woR;
                Vector3f pmin = g * rMin, pmax = g * rMax;
                Vector3f n = new Vector3f(0, 0, 1);
                Float dn = Dot(wrl, n);
                Float dmin = -Dot(pmin, n) / dn, dmax = -Dot(pmax, n) / dn;
                Float wz = Dot(wrl, g);
                rMin += dmin * wz;
                rMax += dmax * wz;
            }
            Float maxH = 1, minH = 1 - G1(wi);
            Float facetWidth = c / s;
            int i = 0;
            Float G1_ = 0;
            SampledSpectrum energy = new SampledSpectrum(1f), value = new SampledSpectrum(0f);
            Float pdfWi = 0;
            Vector3f wr = -wi;
            if (rng.Uniform<Float>() > LambdaP(wi))
            {
                wr = wr - 2 * Dot(wr, t) * t;
                AdvanceHeights(wr, new Vector3f(0, 0, 1), facetWidth, false, ref minH, ref maxH);
                i++;
            }
            bool singleScattering = true;
            do
            {
                Point2f smp = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                Float ucs = rng.Uniform<Float>();
                Vector3f a = -wr;
                Float weight;
                if (!singleScattering)
                {
                    weight = pdfWi / (pdfWi + PdfOverCos(0, a, wo, mode));
                }
                else
                {
                    weight = .5f;
                }
                if (maxH > 1 - shadowing)
                {
                    Float ms = std.max<Float>(0, maxH - std.max(minH, 1 - shadowing));
                    SampledSpectrum contribution = NEval(0, a, wo, mode) * energy * (ms / (maxH - minH) * weight);
                    if (IsFinite(contribution[0]))
                    {
                        value += contribution;
                    }
                }
                i++;
                if (i >= order)
                {
                    break;
                }
                if (!singleScattering)
                {
                    weight = pdfWi / (pdfWi + PdfOverCos(0, a, woR, mode));
                }
                else
                {
                    weight = .5f;
                }
                if (minH < rMax && rMin < maxH)
                {
                    Float ms = std.max<Float>(0, std.min(maxH, rMax) - std.max(minH, rMin));
                    SampledSpectrum contribution = NEval(0, a, woR, mode) * energy * (ms / (maxH - minH) * weight);
                    if (IsFinite(contribution[0]))
                    {
                        value += contribution;
                    }
                }
                Vector3f b;
                SampledSpectrum w;
                if (!NSample(0, a, mode, ucs, smp, out b, out w, out Float crossed) || crossed != 0)
                {
                    break;
                }
                if (singleScattering)
                {
                    pdfWi = PdfOverCos(0, a, b, mode);
                    singleScattering = false;
                }
                energy *= w;
                wr = b;
                if (!AdvanceHeights(wr, t, facetWidth, true, ref minH, ref maxH))
                {
                    break;
                }
                G1_ = (maxH - std.max<Float>(minH, 1)) / (maxH - minH);
                maxH = std.min<Float>(1, maxH);
                if (i >= order || rng.Uniform<Float>() < G1_)
                {
                    break;
                }
                wr = wr - 2 * Dot(wr, t) * t;
                if (!AdvanceHeights(wr, new Vector3f(0, 0, 1), facetWidth, false, ref minH, ref maxH))
                {
                    break;
                }
                G1_ = (maxH - std.max<Float>(minH, 1)) / (maxH - minH);
                maxH = std.min<Float>(1, maxH);
                i++;
            } while (i < order && rng.Uniform<Float>() > G1_);
            return value;
        }

        private bool SampleSameNS(Vector3f wi, Float uc, Point2f u, TransportMode mode, ref RNG rng, out Vector3f wo, out SampledSpectrum energyOut,
                                out Float crossedEta)
        {
            wo = default;
            energyOut = default;
            crossedEta = 0;
            int cur = rng.Uniform<Float>() > LambdaP(wi) ? 1 : 0;
            Float height = 1 - rng.Uniform<Float>() * G1(wi);
            Float facetWidth = c / s;
            int i = 0;
            Vector3f wr = -wi;
            SampledSpectrum energy = new SampledSpectrum(1f);
            bool first = true;
            do
            {
                if (i >= order)
                {
                    return false;
                }
                Vector3f b;
                SampledSpectrum w;
                Float ucs = first ? uc : rng.Uniform<Float>();
                Point2f smp = first ? u : new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                first = false;
                if (!NSample(cur, -wr, mode, ucs, smp, out b, out w, out crossedEta))
                {
                    return false;
                }
                energy *= w;
                wr = b;
                if (crossedEta != 0)
                {
                    wo = wr;
                    energyOut = energy;
                    return Dot(wr, g) < 0;
                }
                bool isP = cur == 0;
                Vector3f other = isP ? t : new Vector3f(0, 0, 1);
                Vector3f pos = g * height;
                if (isP)
                {
                    pos += other * (height * facetWidth);
                }
                Float d = -Dot(pos, other) / Dot(wr, other);
                if (d < 0)
                {
                    break;
                }
                height += d * Dot(wr, g);
                cur = 1 - cur;
                i++;
            } while (height <= 1);
            wo = wr;
            energyOut = energy;
            return Dot(wr, g) > 0;
        }

        private bool SampleMirrorNS(Vector3f wi, Float uc, Point2f u, TransportMode mode, ref RNG rng, out Vector3f wo, out SampledSpectrum energyOut,
                                out Float crossedEta)
        {
            wo = default;
            energyOut = default;
            crossedEta = 0;
            Float height = 1 - rng.Uniform<Float>() * G1(wi);
            Float facetWidth = c / s;
            int i = 0;
            Vector3f wr = -wi;
            SampledSpectrum energy = new SampledSpectrum(1f);
            Vector3f pn = new Vector3f(0, 0, 1);
            if (rng.Uniform<Float>() > LambdaP(wi))
            {
                wr = wr - 2 * Dot(wr, t) * t;
                Float d = -Dot(g * height, pn) / Dot(wr, pn);
                height += d * Dot(wr, g);
                i++;
            }
            bool first = true;
            do
            {
                if (i >= order)
                {
                    return false;
                }
                Vector3f b;
                SampledSpectrum w;
                Float ucs = first ? uc : rng.Uniform<Float>();
                Point2f smp = first ? u : new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                first = false;
                if (!NSample(0, -wr, mode, ucs, smp, out b, out w, out crossedEta))
                {
                    return false;
                }
                energy *= w;
                wr = b;
                if (crossedEta != 0)
                {
                    wo = wr;
                    energyOut = energy;
                    return Dot(wr, g) < 0;
                }
                Vector3f pos = g * height + t * (height * facetWidth);
                Float d = -Dot(pos, t) / Dot(wr, t);
                if (d < 0)
                {
                    break;
                }
                height += d * Dot(wr, g);
                i++;
                if (height > 1)
                {
                    break;
                }
                if (i >= order)
                {
                    return false;
                }
                wr = wr - 2 * Dot(wr, t) * t;
                d = -Dot(g * height, pn) / Dot(wr, pn);
                if (d < 0)
                {
                    break;
                }
                height += d * Dot(wr, g);
                i++;
            } while (height <= 1);
            wo = wr;
            energyOut = energy;
            return Dot(wr, g) > 0;
        }

        // MicrofacetNormalMapBxDF Private Members
        private NormalMapParams parameters;
        private Vector3f g = new Vector3f(0, 0, 1);
        private Vector3f t = new Vector3f(1, 0, 0), tx = new Vector3f(1, 0, 0), ty = new Vector3f(0, 1, 0);
        private Float c = 1, s = 0;
        private int order = 2;
        private Float frac = 0;
        private bool hasFrac = false;
        private BxDF bxdf;
        private bool valid = false;
        private bool transmissive = false;
    }
}
