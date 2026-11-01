// (not in pbrt-v4) compositemedium.cs
//
// Overlapping volumes, Fong et al., "Production Volume Rendering", section 3.9. The volumes that a path is inside of at
// the same time are made of particles that do not react to each other, so the combined volume has the sum of their
// absorption and scattering coefficients, one integrator runs over it, and a collision belongs to one volume: the
// probability that it is volume i is sigma_s,i / sum(sigma_s). The majorant is the sum of the majorants, and the emission
// is the sum of sigma_a * Le (the radiance of the volumes is not additive, their emission coefficients are).
//
// A CompositeMedium is a Medium like any other, so the walks, the shadow rays and bdpt need no change: the boundary stack
// hands them the composite in place of the medium on top when several of the media that it holds can overlap.

using System;
using System.Collections.Concurrent;

namespace pbrt
{

    // MixturePhaseFunction Definition
    //
    // The phase function of a collision in overlapping volumes: the phase function of volume i with probability weights[i].
    // The weights are those of the hero wavelength. For a wavelength whose weights differ (volumes of different albedo
    // colour with different phase functions) the mixture is an approximation; it is exact when the weights do not depend
    // on the wavelength or when the phase functions are the same.
    public sealed unsafe class MixturePhaseFunction : PhaseFunction
    {
        // MixturePhaseFunction Public Methods
        public MixturePhaseFunction(PhaseFunction[] phases, Float[] weights)
        {
            this.phases = phases;
            this.weights = weights;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ MixturePhaseFunction components: %d ]", phases.Length);
        }

        public override Float p(Vector3f wo, Vector3f wi)
        {
            Float sum = 0;
            for (int i = 0; i < phases.Length; ++i)
                sum += weights[i] * phases[i].p(wo, wi);
            return sum;
        }

        public override pstd.optional<PhaseFunctionSample> Sample_p(Vector3f wo, Point2f u)
        {
            // Choose the component with u[0], and use it again after the choice
            int index = phases.Length - 1;
            Float lower = 0;
            for (int i = 0; i < phases.Length - 1; ++i)
            {
                if (u.x < lower + weights[i])
                {
                    index = i;
                    break;
                }
                lower += weights[i];
            }
            Float remapped = Math.Min((u.x - lower) / weights[index], pbrt.OneMinusEpsilon);
            pstd.optional<PhaseFunctionSample> ps = phases[index].Sample_p(wo, new Point2f(remapped, u.y));
            if (!ps)
                return default;
            PhaseFunctionSample sample = ps.value();
            sample.p = p(wo, sample.wi);
            sample.pdf = PDF(wo, sample.wi);
            return sample;
        }

        public override Float PDF(Vector3f wo, Vector3f wi)
        {
            Float sum = 0;
            for (int i = 0; i < phases.Length; ++i)
                sum += weights[i] * phases[i].PDF(wo, wi);
            return sum;
        }

        // MixturePhaseFunction Private Members
        private PhaseFunction[] phases;
        private Float[] weights;
    }

    // MergedMajorantIterator Definition
    //
    // The segments of the children, cut at every breakpoint of any of them, with the sum of the majorants of the children
    // that have a segment over the piece. Stretches where no child has a majorant are skipped.
    public sealed unsafe class MergedMajorantIterator : RayMajorantIterator
    {
        // MergedMajorantIterator Public Methods
        public MergedMajorantIterator(Medium[] children, Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            iterators = new RayMajorantIterator[children.Length];
            segments = new RayMajorantSegment[children.Length];
            hasSegment = new bool[children.Length];
            for (int i = 0; i < children.Length; ++i)
            {
                iterators[i] = children[i].SampleRay(ray, tMax, lambda, buf);
                Fetch(i);
            }
            t = 0;
        }

        public override pstd.optional<RayMajorantSegment> Next()
        {
            while (true)
            {
                Float next = Float.PositiveInfinity;
                SampledSpectrum sum = new SampledSpectrum(0f);
                bool anyActive = false;
                bool anyLeft = false;
                for (int i = 0; i < iterators.Length; ++i)
                {
                    while (hasSegment[i] && segments[i].tMax <= t)
                        Fetch(i);
                    if (!hasSegment[i])
                        continue;
                    anyLeft = true;
                    if (segments[i].tMin > t)
                    {
                        next = Math.Min(next, segments[i].tMin);
                        continue;
                    }
                    anyActive = true;
                    sum += segments[i].sigma_maj;
                    next = Math.Min(next, segments[i].tMax);
                }
                if (!anyLeft)
                    return default;
                RayMajorantSegment merged = new RayMajorantSegment { tMin = t, tMax = next, sigma_maj = sum };
                t = next;
                if (anyActive && (bool)sum)
                    return merged;
                if (Float.IsPositiveInfinity(next))
                    return default;
            }
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ MergedMajorantIterator children: %d t: %f ]", iterators.Length, t);
        }

        // MergedMajorantIterator Private Methods
        private void Fetch(int index)
        {
            pstd.optional<RayMajorantSegment> seg = iterators[index].Next();
            hasSegment[index] = (bool)seg;
            if (hasSegment[index])
                segments[index] = seg.value();
        }

        // MergedMajorantIterator Private Members
        private RayMajorantIterator[] iterators;
        private RayMajorantSegment[] segments;
        private bool[] hasSegment;
        private Float t;
    }

    // CompositeMedium Definition
    public sealed unsafe class CompositeMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        // CompositeMedium Public Methods
        public CompositeMedium(Medium[] children)
        {
            this.children = children;
            for (int i = 0; i < children.Length; ++i)
                emissive |= children[i].IsEmissive();
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ CompositeMedium children: %d ]", children.Length);
        }

        public override bool IsEmissive() { return emissive; }

        public Medium[] Children() { return children; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            MediumProperties total = default;
            total.sigma_a = new SampledSpectrum(0f);
            total.sigma_s = new SampledSpectrum(0f);
            SampledSpectrum emission = new SampledSpectrum(0f);
            MediumProperties[] properties = new MediumProperties[children.Length];
            for (int i = 0; i < children.Length; ++i)
            {
                properties[i] = children[i].SamplePoint(p, lambda);
                total.sigma_a += properties[i].sigma_a;
                total.sigma_s += properties[i].sigma_s;
                emission += properties[i].sigma_a * properties[i].Le;
            }
            // The integrators gather sigma_a * Le, which is the sum of those of the children
            total.Le = new SampledSpectrum(0f);
            if (emissive)
            {
                for (int c = 0; c < pbrt.NSpectrumSamples; ++c)
                    total.Le[c] = total.sigma_a[c] > 0 ? emission[c] / total.sigma_a[c] : 0;
            }

            // The collision is with one volume, chosen by its scattering coefficient (the hero wavelength's)
            Float sigma_sHero = total.sigma_s[0];
            int scattering = 0;
            int last = 0;
            for (int i = 0; i < children.Length; ++i)
            {
                if (properties[i].sigma_s[0] > 0)
                {
                    ++scattering;
                    last = i;
                }
            }
            if (scattering <= 1 || sigma_sHero <= 0)
            {
                total.phase = properties[last].phase;
                return total;
            }
            PhaseFunction[] phases = new PhaseFunction[scattering];
            Float[] weights = new Float[scattering];
            int n = 0;
            for (int i = 0; i < children.Length; ++i)
            {
                if (properties[i].sigma_s[0] > 0)
                {
                    phases[n] = properties[i].phase;
                    weights[n] = properties[i].sigma_s[0] / sigma_sHero;
                    ++n;
                }
            }
            total.phase = new MixturePhaseFunction(phases, weights);
            return total;
        }

        public RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda)
        {
            return SampleRay(ray, tMax, lambda, new ScratchBuffer());
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(new MergedMajorantIterator(children, ray, tMax, lambda, buf));
        }

        // A lower bound that every child guarantees, so the sum is one
        public override SampledSpectrum ControlExtinction(SampledWavelengths lambda)
        {
            SampledSpectrum sum = new SampledSpectrum(0f);
            for (int i = 0; i < children.Length; ++i)
                sum += children[i].ControlExtinction(lambda);
            return sum;
        }

        // CompositeMedium Private Members
        private Medium[] children;
        private bool emissive;
    }

    // CompositeMediumCache Definition
    //
    // One CompositeMedium for every combination of media that occurs, found without allocating: the key is a hash of
    // the media of the stack, checked against the children of the entry.
    public static class CompositeMediumCache
    {
        // CompositeMediumCache Public Methods
        public static Medium Get(in BoundaryStack stack, int count)
        {
            long key = 17;
            for (int i = 0; i < count; ++i)
                key = key * 1000003 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(stack.MediumFromTop(i));
            if (cache.TryGetValue(key, out CompositeMedium[] entries))
            {
                for (int e = 0; e < entries.Length; ++e)
                {
                    if (Matches(entries[e], stack, count))
                        return entries[e];
                }
            }
            lock (cache)
            {
                // Children from the bottom of the part of the stack that overlaps up to the top, so that equal stacks
                // make equal composites
                Medium[] children = new Medium[count];
                for (int i = 0; i < count; ++i)
                    children[count - 1 - i] = stack.MediumFromTop(i);
                CompositeMedium made = new CompositeMedium(children);
                CompositeMedium[] old = cache.TryGetValue(key, out CompositeMedium[] existing) ? existing : new CompositeMedium[0];
                for (int e = 0; e < old.Length; ++e)
                {
                    if (Matches(old[e], stack, count))
                        return old[e];
                }
                CompositeMedium[] grown = new CompositeMedium[old.Length + 1];
                Array.Copy(old, grown, old.Length);
                grown[old.Length] = made;
                cache[key] = grown;
                return made;
            }
        }

        // CompositeMediumCache Private Methods
        private static bool Matches(CompositeMedium composite, in BoundaryStack stack, int count)
        {
            Medium[] children = composite.Children();
            if (children.Length != count)
                return false;
            for (int i = 0; i < count; ++i)
            {
                if (!ReferenceEquals(children[count - 1 - i], stack.MediumFromTop(i)))
                    return false;
            }
            return true;
        }

        // CompositeMediumCache Private Members
        private static readonly ConcurrentDictionary<long, CompositeMedium[]> cache = new ConcurrentDictionary<long, CompositeMedium[]>();
    }

}  // namespace pbrt
