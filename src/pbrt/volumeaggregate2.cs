// (not in pbrt-v4) volumeaggregate2.cs
//
// The volume aggregate of volumeaggregate.cs given to the framework as a medium like any other, `MakeNamedMedium "name"
// "string type" "aggregate2" "string members" [ ... ]`, beside "aggregate", which stays as it was.
//
// "aggregate" integrates itself in the production path tracer (AggregateVolume): its own walk over the intervals of the
// octree, its own ratio tracking for the transmittance. What was made since for the media -- the estimators of the
// transmittance, the trackers, equiangular sampling and its weighting against next event estimation, the double scattering,
// the residual tracking of the emission -- is in the volumes of productionvolume.cs, which ask a medium only for the
// majorants along a ray (SampleRay) and for its properties at a point (SamplePoint). "aggregate2" is the same octree
// answering those two:
//
//   - SampleRay: one segment per leaf that the ray crosses (AggregateMajorantIterator). The majorant of a leaf is the sum
//     over its volumes of the largest density of each there times its extinction; its control and its mean are the same
//     sums with the smallest and the mean densities, given as the fractions of the majorant that the segments carry. The
//     volumes may differ in colour, and one fraction scales every wavelength alike: the control is the smallest of the
//     fractions of the wavelengths (it must not exceed the extinction at any), the mean is their mean (any value serves).
//   - SamplePoint: the sum of the volumes of the leaf at the point that hold it, with the mixture of their phase functions
//     by their scattering coefficients (what CompositeMedium does for media that overlap, over the few volumes of the leaf
//     and not over all the members).
//
// So the volume of an "aggregate2" is whichever "scattering" asks for, with "tracker", "transmittance", "distancesampling",
// "doublescattering", "emission" and the rest read from the aggregate medium as from any medium, and the emission light
// ("emission_nee") as for "aggregate". What "aggregate" has and this has not: the control collisions without a lookup of
// decomposition tracking (a control collision here would have to belong to one member; "tracker" "decomposition" falls back
// to the majorant tracker), and spectral tracking that chooses among the scatterings of the members.

using System;

namespace pbrt
{
    using static pbrt;

    public sealed unsafe class AggregateMajorantIterator : RayMajorantIterator
    {
        public AggregateMajorantIterator(VolumeAggregate aggregate, SampledSpectrum[] extinctions, Ray ray, Float tMax)
        {
            // (the iterator of the octree wants a direction of unit length; the segments are given in the units of the ray)
            length = Length(ray.d);
            this.extinctions = extinctions;
            interval = aggregate.GetIterator(ray.o, ray.d / length, tMax * length);
        }

        public override pstd.optional<RayMajorantSegment> Next()
        {
            if (!interval.Next())
            {
                return default;
            }
            SampledSpectrum largest = new SampledSpectrum(0f), smallest = new SampledSpectrum(0f), mean = new SampledSpectrum(0f);
            int[] volumes = interval.Volumes();
            for (int k = 0; k < volumes.Length; ++k)
            {
                SampledSpectrum extinction = extinctions[volumes[k]];
                largest += interval.MaxDensity(k) * extinction;
                smallest += interval.MinDensity(k) * extinction;
                mean += interval.MeanDensity(k) * extinction;
            }
            RayMajorantSegment seg = new RayMajorantSegment { tMin = interval.Start() / length, tMax = interval.End() / length, sigma_maj = largest };
            Float control = 1, average = 0;
            int counted = 0;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                if (largest[i] > 0)
                {
                    control = std.min(control, smallest[i] / largest[i]);
                    average += mean[i] / largest[i];
                    ++counted;
                }
            }
            if (counted > 0)
            {
                seg.controlFraction = std.max<Float>(0, control);
                seg.averageFraction = std.min<Float>(1, std.max(seg.controlFraction, average / counted));
            }
            return seg;
        }

        public override string ToString()
        {
            return StringPrintf("[ AggregateMajorantIterator start: %f end: %f ]", interval.Start(), interval.End());
        }

        private VolumeAggregate.Iterator interval;
        private SampledSpectrum[] extinctions;
        private Float length;
    }

    public sealed unsafe class AggregateMedium2 : Medium, IMajorantMedium<RayMajorantIterator>
    {
        public AggregateMedium2(Medium[] media, Float threshold, int maxDepth, FileLoc loc)
        {
            // (the octree, and what the emission light asks, are those of "aggregate")
            inner = new AggregateMedium(media, threshold, maxDepth, loc);
            aggregate = inner.Aggregate();
        }

        public static AggregateMedium2 Create(Medium[] media, ParameterDictionary parameters, FileLoc loc)
        {
            if (media.Length == 0)
            {
                ErrorExit(loc, "An \"aggregate2\" medium needs \"string members\".");
            }
            Float threshold = parameters.GetOneFloat("threshold", 2);
            int maxDepth = parameters.GetOneInt("maxdepth", 6);
            if (!(threshold > 0) || maxDepth < 0 || maxDepth > 10)
            {
                ErrorExit(loc, "\"threshold\" must be positive and \"maxdepth\" between 0 and 10.");
            }
            return new AggregateMedium2(media, threshold, maxDepth, loc);
        }

        public override string ToString()
        {
            return StringPrintf("[ AggregateMedium2 members: %d leaves: %d ]", aggregate.MemberCount(), aggregate.LeafCount());
        }

        public override bool IsEmissive() { return inner.IsEmissive(); }

        public VolumeAggregate Aggregate() { return aggregate; }

        public override bool EmissionRegion(out Bounds3f mediumBounds, out Transform renderFromMedium)
        {
            return inner.EmissionRegion(out mediumBounds, out renderFromMedium);
        }

        public override Float EmissionBoundUnit(Bounds3f unitBox)
        {
            return inner.EmissionBoundUnit(unitBox);
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            return SamplePoint(p, lambda, 0);
        }

        // The sum of the volumes of the leaf at the point that hold the point
        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda, Float time)
        {
            return SampleAt(p, new Vector3f(0, 0, 0), false, lambda, time);
        }

        // (not in pbrt-v4) With the direction of travel, for the media inside whose extinction depends on it
        public override MediumProperties SamplePoint(Point3f p, Vector3f w, SampledWavelengths lambda, Float time)
        {
            return SampleAt(p, w, true, lambda, time);
        }

        // directed: w is the direction of travel, and the media inside are asked with it
        private MediumProperties SampleAt(Point3f p, Vector3f w, bool directed, SampledWavelengths lambda, Float time)
        {
            MediumProperties total = default;
            total.sigma_a = new SampledSpectrum(0f);
            total.sigma_s = new SampledSpectrum(0f);
            total.Le = new SampledSpectrum(0f);
            int[] volumes = aggregate.VolumesAt(p);
            if (volumes == null || volumes.Length == 0)
            {
                return total;
            }
            if (volumes.Length == 1)
            {
                AggregateMember only = aggregate.Member(volumes[0]);
                return only.Inside(p) ? (directed ? only.medium.SamplePoint(p, w, lambda, time) : only.medium.SamplePoint(p, lambda, time)) : total;
            }
            MediumProperties[] properties = new MediumProperties[volumes.Length];
            SampledSpectrum emission = new SampledSpectrum(0f);
            int scattering = 0, last = -1;
            for (int k = 0; k < volumes.Length; ++k)
            {
                AggregateMember member = aggregate.Member(volumes[k]);
                if (!member.Inside(p))
                {
                    properties[k].sigma_s = new SampledSpectrum(0f);
                    continue;
                }
                properties[k] = (directed ? member.medium.SamplePoint(p, w, lambda, time) : member.medium.SamplePoint(p, lambda, time));
                total.sigma_a += properties[k].sigma_a;
                total.sigma_s += properties[k].sigma_s;
                emission += properties[k].sigma_a * properties[k].Le;
                if (properties[k].sigma_s[0] > 0)
                {
                    ++scattering;
                    last = k;
                }
                else if (last < 0 && properties[k].phase != null)
                {
                    total.phase = properties[k].phase;
                }
            }
            // (the integrators gather sigma_a Le, which is the sum of those of the volumes)
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                total.Le[c] = total.sigma_a[c] > 0 ? emission[c] / total.sigma_a[c] : 0;
            }
            if (scattering == 0)
            {
                return total;
            }
            if (scattering == 1)
            {
                total.phase = properties[last].phase;
                return total;
            }
            // The collision is with one volume, chosen by its scattering coefficient (that of the wavelength that samples)
            PhaseFunction[] phases = new PhaseFunction[scattering];
            Float[] weights = new Float[scattering];
            int n = 0;
            for (int k = 0; k < volumes.Length; ++k)
            {
                if (properties[k].sigma_s[0] > 0)
                {
                    phases[n] = properties[k].phase;
                    weights[n] = properties[k].sigma_s[0] / total.sigma_s[0];
                    ++n;
                }
            }
            total.phase = new MixturePhaseFunction(phases, weights);
            return total;
        }

        public RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda)
        {
            SampledSpectrum[] extinctions = new SampledSpectrum[aggregate.MemberCount()];
            for (int i = 0; i < extinctions.Length; ++i)
            {
                extinctions[i] = aggregate.Member(i).medium.AggregateExtinction(lambda);
            }
            return new AggregateMajorantIterator(aggregate, extinctions, ray, tMax);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return SampleRay(ray, tMax, lambda);
        }

        private AggregateMedium inner;
        private VolumeAggregate aggregate;
    }
}
