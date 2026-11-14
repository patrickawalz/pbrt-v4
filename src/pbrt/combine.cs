// (not in pbrt-v4) combine.cs
//
// The two operations that the layered material models are written with (OpenPBR's "layer" and "mix", the layers and the BSDFs
// of a Maxwell material, the layer / mix / add nodes of MaterialX), on any lobes:
//   MixBxDF      a statistical mix: the weighted sum of BxDFs, each sampled with its weight
//   StackBxDF    a layer on a base: the interface `top` over the BxDF `bottom`, with a medium between them that absorbs (by
//                wavelength) or scatters, by the position-free random walk of pbrt's LayeredBxDF; the base may be a StackBxDF
//                or a MixBxDF itself, so a stack has any number of layers
// and two wrappers:
//   AnyBxDF      a BxDF behind one type, for the layers of a StackBxDF
//   TintedBxDF   a BxDF times a colour (the "specular_color" and "transmission_color" of the artists' models: not physical)
// All of them pass on the Mueller matrices of the lobes they hold.

using System;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    // The Mueller matrix of a lobe held by another BxDF. The microfacet conductor and dielectric of pbrt have no matrix of their
    // own (the integrator and the layered BxDF build it from their type): here it is built the same way, the Fresnel matrix
    // at the normal that sends one direction to the other.
    public static unsafe class LobeMatrices
    {
        public static bool Stock(BxDF lobe) { return lobe is ConductorBxDF || lobe is DielectricBxDF; }

        public static bool Has(BxDF lobe) { return Stock(lobe) || lobe is ThinDielectricBxDF || lobe.HasMuellerMatrix(); }

        public static SampledSpectrum f(BxDF lobe, BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            if (!Stock(lobe))
            {
                return lobe.f(bsdf, wo, wi, mode, out polarised, out matrix);
            }
            polarised = false;
            matrix = default;
            SampledSpectrum value = lobe.f(wo, wi, mode);
            if ((bool)value)
            {
                matrix = StackBxDF.EventMatrix(bsdf, 1, lobe, wo, wi, mode == TransportMode.Radiance);
                polarised = true;
            }
            return value;
        }

        public static pstd.optional<BSDFSample> Sample_f(BxDF lobe, BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                         out bool polarised, out Attenuation matrix)
        {
            if (!Stock(lobe))
            {
                pstd.optional<BSDFSample> sampled = lobe.Sample_f(bsdf, wo, uc, u, mode, sampleFlags, out polarised, out matrix);
                if ((bool)sampled && !polarised && sampled.value().IsSpecular() && (bool)sampled.value().f)
                {
                    // A specular lobe whose matrix the integrator builds from its type (the thin dielectric, a smooth thin film):
                    // the same here, divided by the scalar weight of the sample, which that matrix carries
                    BSDFSample world = sampled.value();
                    SampledSpectrum weight = world.f * AbsCosTheta(world.wi) / world.pdf;
                    world.wi = bsdf.LocalToRender(world.wi);
                    SurfaceInteraction surface = new SurfaceInteraction();
                    Vector3f normal = bsdf.LocalToRender(new Vector3f(0, 0, 1));
                    surface.shading.n = new Normal3f(normal.x, normal.y, normal.z);
                    surface.n = surface.shading.n;
                    Attenuation whole;
                    if (PolarisedPathIntegrator.SpecularAttenuation(bsdf.With(lobe), surface, bsdf.LocalToRender(wo), world, mode, out whole))
                    {
                        polarised = PolarisedFacet.Normalise(whole, weight, new SampledSpectrum(1f), out matrix);
                    }
                }
                return sampled;
            }
            polarised = false;
            matrix = default;
            pstd.optional<BSDFSample> bs = lobe.Sample_f(wo, uc, u, mode, sampleFlags);
            if ((bool)bs && (bool)bs.value().f)
            {
                matrix = StackBxDF.EventMatrix(bsdf, 1, lobe, wo, bs.value().wi, mode == TransportMode.Radiance);
                polarised = true;
            }
            return bs;
        }
    }

    // AnyBxDF Definition
    public sealed unsafe class AnyBxDF : BxDF
    {
        public AnyBxDF() { inner = new DiffuseBxDF(new SampledSpectrum(0f)); }
        public AnyBxDF(BxDF inner) { this.inner = inner; }

        public override BxDFFlags Flags() { return inner.Flags(); }
        public override string ToString() { return inner.ToString(); }
        public override void Regularize() { inner.Regularize(); }
        public override bool HasMuellerMatrix() { return inner.HasMuellerMatrix(); }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode) { return inner.f(wo, wi, mode); }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return inner.Sample_f(wo, uc, u, mode, sampleFlags);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return inner.PDF(wo, wi, mode, sampleFlags);
        }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            return LobeMatrices.f(inner, bsdf, wo, wi, mode, out polarised, out matrix);
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                           out bool polarised, out Attenuation matrix)
        {
            return LobeMatrices.Sample_f(inner, bsdf, wo, uc, u, mode, sampleFlags, out polarised, out matrix);
        }

        public BxDF inner;
    }

    // TintedBxDF Definition
    // The BxDF times a colour: `reflection` on what it reflects, `transmission` on what it transmits
    public sealed unsafe class TintedBxDF : BxDF
    {
        public TintedBxDF(BxDF inner, SampledSpectrum reflection, SampledSpectrum transmission)
        {
            this.inner = inner;
            this.reflection = reflection;
            this.transmission = transmission;
        }

        public override BxDFFlags Flags() { return inner.Flags(); }
        public override string ToString() { return StringPrintf("[ TintedBxDF reflection: %s transmission: %s of: %s ]", reflection, transmission, inner); }
        public override void Regularize() { inner.Regularize(); }
        public override bool HasMuellerMatrix() { return LobeMatrices.Has(inner); }

        public BxDF Inner() { return inner; }

        private SampledSpectrum Tint(Vector3f wo, Vector3f wi) { return wo.z * wi.z > 0 ? reflection : transmission; }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode) { return inner.f(wo, wi, mode) * Tint(wo, wi); }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            pstd.optional<BSDFSample> sampled = inner.Sample_f(wo, uc, u, mode, sampleFlags);
            if (!sampled)
            {
                return default;
            }
            BSDFSample bs = sampled.value();
            bs.f *= Tint(wo, bs.wi);
            return bs;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return inner.PDF(wo, wi, mode, sampleFlags);
        }

        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            return LobeMatrices.f(inner, bsdf, wo, wi, mode, out polarised, out matrix) * Tint(wo, wi);
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
            bs.f *= Tint(wo, bs.wi);
            return bs;
        }

        private BxDF inner;
        private SampledSpectrum reflection, transmission;
    }

    // MixBxDF Definition
    // f = sum_i w_i f_i. A component is chosen with its share of the weights and sampled; the sample stands for the whole mix
    // (its weight times the sum of the weights), so components whose f is an estimate and whose density is not known (the
    // random walks) mix like the others: the density returned is that of the chosen component and is marked as proportional
    // unless the sample is specular. Weights that sum to less than one leave the rest unlit (an absorber).
    public sealed unsafe class MixBxDF : BxDF
    {
        public MixBxDF(BxDF[] parts, Float[] weights)
        {
            int count = 0;
            for (int i = 0; i < parts.Length; ++i)
            {
                count += (parts[i] != null && weights[i] > 0) ? 1 : 0;
            }
            this.parts = new BxDF[count];
            this.weights = new Float[count];
            int next = 0;
            for (int i = 0; i < parts.Length; ++i)
            {
                if (parts[i] != null && weights[i] > 0)
                {
                    this.parts[next] = parts[i];
                    this.weights[next] = weights[i];
                    total += weights[i];
                    ++next;
                }
            }
        }

        // The mix of two: `second` with the weight w, `first` with 1 - w (either alone at the ends: no mix is built)
        public static BxDF Of(BxDF first, BxDF second, Float w)
        {
            w = Clamp(w, 0, 1);
            if (w <= 0 || second == null)
            {
                return first;
            }
            if (w >= 1 || first == null)
            {
                return second;
            }
            return new MixBxDF(new BxDF[] { first, second }, new Float[] { 1 - w, w });
        }

        public int Count() { return parts.Length; }
        public BxDF Part(int i) { return parts[i]; }

        public override BxDFFlags Flags()
        {
            BxDFFlags flags = BxDFFlags.Unset;
            for (int i = 0; i < parts.Length; ++i)
            {
                flags |= parts[i].Flags();
            }
            return flags;
        }

        public override string ToString()
        {
            string text = "[ MixBxDF";
            for (int i = 0; i < parts.Length; ++i)
            {
                text += StringPrintf(" %f: %s", weights[i], parts[i]);
            }
            return text + " ]";
        }

        public override void Regularize()
        {
            for (int i = 0; i < parts.Length; ++i)
            {
                parts[i].Regularize();
            }
        }

        public override bool HasMuellerMatrix()
        {
            for (int i = 0; i < parts.Length; ++i)
            {
                if (LobeMatrices.Has(parts[i]))
                {
                    return true;
                }
            }
            return false;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            SampledSpectrum sum = new SampledSpectrum(0f);
            for (int i = 0; i < parts.Length; ++i)
            {
                sum += parts[i].f(wo, wi, mode) * weights[i];
            }
            return sum;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (!(total > 0))
            {
                return 0;
            }
            Float pdf = 0;
            for (int i = 0; i < parts.Length; ++i)
            {
                pdf += parts[i].PDF(wo, wi, mode, sampleFlags) * (weights[i] / total);
            }
            return pdf;
        }

        private int Choose(ref Float uc)
        {
            Float target = uc * total, below = 0;
            for (int i = 0; i < parts.Length; ++i)
            {
                if (target < below + weights[i] || i == parts.Length - 1)
                {
                    uc = std.min<Float>(std.max<Float>((target - below) / weights[i], 0), OneMinusEpsilon);
                    return i;
                }
                below += weights[i];
            }
            return parts.Length - 1;
        }

        private pstd.optional<BSDFSample> Finish(pstd.optional<BSDFSample> sampled)
        {
            if (!sampled || !(bool)sampled.value().f || sampled.value().pdf == 0)
            {
                return default;
            }
            BSDFSample bs = sampled.value();
            // chosen with w_i / total, worth w_i: the sample times the total
            bs.f *= total;
            if (!bs.IsSpecular())
            {
                bs.pdfIsProportional = true;
            }
            return bs;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (parts.Length == 0)
            {
                return default;
            }
            int chosen = Choose(ref uc);
            return Finish(parts[chosen].Sample_f(wo, uc, u, mode, sampleFlags));
        }

        // The matrices of the components, each with the weight of its component of f
        public override SampledSpectrum f(BSDF bsdf, Vector3f wo, Vector3f wi, TransportMode mode, out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            bool fromCamera = mode == TransportMode.Radiance;
            Vector3f woRender = bsdf.LocalToRender(wo), wiRender = bsdf.LocalToRender(wi);
            Vector3f directionExit = fromCamera ? woRender : wiRender, directionEnter = fromCamera ? -wiRender : -woRender;
            SampledSpectrum sum = new SampledSpectrum(0f);
            Attenuation total = default;
            bool hasTotal = false;
            for (int i = 0; i < parts.Length; ++i)
            {
                SampledSpectrum value = LobeMatrices.f(parts[i], bsdf, wo, wi, mode, out bool has, out Attenuation m) * weights[i];
                if (!(bool)value)
                {
                    continue;
                }
                sum += value;
                Attenuation term = has ? m * value : Attenuation.Lambert(value);
                if (!term.WithFrames(directionExit, directionEnter) || term.HasNaNs())
                {
                    continue;
                }
                if (hasTotal)
                {
                    total += term;
                }
                else
                {
                    total = term;
                    hasTotal = true;
                }
            }
            if (hasTotal)
            {
                polarised = PolarisedFacet.Normalise(total, sum, new SampledSpectrum(1f), out matrix);
            }
            return sum;
        }

        public override pstd.optional<BSDFSample> Sample_f(BSDF bsdf, Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags,
                                                           out bool polarised, out Attenuation matrix)
        {
            polarised = false;
            matrix = default;
            if (parts.Length == 0)
            {
                return default;
            }
            int chosen = Choose(ref uc);
            pstd.optional<BSDFSample> bs = Finish(LobeMatrices.Sample_f(parts[chosen], bsdf, wo, uc, u, mode, sampleFlags, out polarised, out matrix));
            if ((bool)bs && !polarised)
            {
                // a lobe without a matrix depolarises
                matrix = Attenuation.Lambert(new SampledSpectrum(1f));
                polarised = true;
            }
            return bs;
        }

        private BxDF[] parts;
        private Float[] weights;
        private Float total = 0;
    }

    // StackBxDF Definition
    public sealed unsafe class StackBxDF : LayeredBxDF<AnyBxDF, AnyBxDF>
    {
        public StackBxDF() : base(true) { }

        // top over bottom. thickness, albedo and g are those of the medium between them as pbrt has them (extinction 1 for the
        // unit of thickness); a medium that only absorbs is given by its optical depth by wavelength along the normal instead
        // (`absorptionDepth`, with an albedo of 0). twoSided: the same stack from below (an opaque base); false for a base
        // that transmits.
        public StackBxDF(BxDF top, BxDF bottom, Float thickness, SampledSpectrum albedo, Float g, int maxDepth, int nSamples, bool twoSided)
            : base(new AnyBxDF(top), new AnyBxDF(bottom), thickness, albedo, g, maxDepth, nSamples, twoSided)
        {
        }

        public static StackBxDF Absorbing(BxDF top, BxDF bottom, SampledSpectrum absorptionDepth, int maxDepth, int nSamples, bool twoSided)
        {
            StackBxDF stack = new StackBxDF(top, bottom, 1, new SampledSpectrum(0f), 0, maxDepth, nSamples, twoSided);
            stack.absorption = absorptionDepth;
            stack.hasAbsorption = true;
            return stack;
        }

        public static string Name() { return "StackBxDF"; }

        public BxDF Top() { return top.inner; }
        public BxDF Bottom() { return bottom.inner; }

        // Every stack under the BxDF given evaluated from both directions (LayeredBxDF.reciprocal)
        public static void SetReciprocal(BxDF lobe, bool on)
        {
            if (lobe is StackBxDF stack)
            {
                stack.reciprocal = on;
                SetReciprocal(stack.Top(), on);
                SetReciprocal(stack.Bottom(), on);
            }
            else if (lobe is MixBxDF mix)
            {
                for (int i = 0; i < mix.Count(); ++i)
                {
                    SetReciprocal(mix.Part(i), on);
                }
            }
            else if (lobe is TintedBxDF tinted)
            {
                SetReciprocal(tinted.Inner(), on);
            }
            else if (lobe is AnyBxDF any)
            {
                SetReciprocal(any.inner, on);
            }
        }
    }
}
