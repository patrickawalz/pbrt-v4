// (not in pbrt-v4) Per-volume integrators, as in Fong et al. "Production Volume Rendering" (SIGGRAPH 2017 course):
// every volume chooses how its collisions are sampled and how its transmittance is estimated, independently of the
// path-tracing estimator that calls it. The pieces are separate modules so that they can be shared between volumes:
//
//   VolumeTracker            samples collisions along a ray ("tracker")
//   TransmittanceEstimator   estimates the transmittance of shadow rays ("integration method")
//   VolumeIntegrator         the pair a medium carries, built from the medium's parameters
//
// The defaults (MajorantTracker, RatioTrackingEstimator) are pbrt-v4's own algorithms.

using System;

namespace pbrt
{
    using static pbrt;

    // VolumeTracker Definition: calls back at every collision of the majorant along the ray and returns the
    // majorant transmittance from the last collision to the end of the ray (or 1 if the callback stopped the walk)
    public abstract unsafe class VolumeTracker
    {
        public abstract string Name();

        public abstract SampledSpectrum SampleT_maj(Ray ray, Medium medium, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
                                                    Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback);
    }

    // pbrt-v4's majorant iteration: follows the medium's own majorant segments (grid DDA, homogeneous, ...)
    public sealed unsafe class MajorantTracker : VolumeTracker
    {
        public override string Name() { return "majorant"; }

        public override SampledSpectrum SampleT_maj(Ray ray, Medium medium, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
                                                    Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback)
        {
            return pbrt.SampleT_maj(ray, medium, tMax, u, ref rng, lambda, callback);
        }
    }

    // One majorant for the whole ray: the largest of the medium's segment majorants, over the whole extent of the ray.
    // Needs no per-segment bookkeeping, at the price of more null collisions in a medium whose density varies.
    public sealed unsafe class GlobalMajorantTracker : VolumeTracker
    {
        public override string Name() { return "global"; }

        public override SampledSpectrum SampleT_maj(Ray ray, Medium medium, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
                                                    Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback)
        {
            // Normalize ray direction and update tMax accordingly
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);

            // Collect the largest majorant of the segments of the ray
            RayMajorantIterator iter = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            SampledSpectrum sigma_maj = new SampledSpectrum(0f);
            Float tEnd = 0;
            while (true)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                    break;
                RayMajorantSegment sg = seg.value();
                if (sg.tMax <= sg.tMin)
                    continue;
                for (int c = 0; c < NSpectrumSamples; ++c)
                    sigma_maj[c] = std.max(sigma_maj[c], sg.sigma_maj[c]);
                tEnd = std.max(tEnd, sg.tMax);
            }
            Float length = IsInf(tEnd) ? Float.MaxValue : tEnd;
            if (sigma_maj[0] == 0)
                return FastExp(-length * sigma_maj);

            SampledSpectrum T_maj = new SampledSpectrum(1f);
            Float tMin = 0;
            while (true)
            {
                Float t = tMin + SampleExponential(u, sigma_maj[0]);
                u = rng.Uniform<Float>();
                if (t < tEnd)
                {
                    T_maj *= FastExp(-(t - tMin) * sigma_maj);
                    MediumProperties mp = medium.SamplePoint(ray.Apply(t), lambda);
                    if (!callback(ray.Apply(t), mp, sigma_maj, T_maj))
                        return new SampledSpectrum(1f);
                    T_maj = new SampledSpectrum(1f);
                    tMin = t;
                }
                else
                {
                    T_maj *= FastExp(-(length - tMin) * sigma_maj);
                    return T_maj;
                }
            }
        }
    }

    // TransmittanceEstimator Definition: what a shadow ray does at each null collision of the tracker, and at the end of
    // a segment. The Standalone overloads serve a transmittance that is used alone (Tr, inv_w); the Combined overloads
    // one that is weighed against other strategies through the rescaled path probabilities (T_ray, r_l, r_u).
    public abstract unsafe class TransmittanceEstimator
    {
        public abstract string Name();

        // A closed form for the transmittance of the segment, if the medium has one. Then the tracker is not run, and the
        // estimate is exact for every wavelength, so no path probability changes. tMax is in units of the length of ray.d.
        public virtual bool TryClosedForm(Ray ray, Medium medium, Float tMax, SampledWavelengths lambda, out SampledSpectrum T)
        {
            T = new SampledSpectrum(1f);
            return false;
        }

        public abstract void Step(ref SampledSpectrum Tr, ref SampledSpectrum inv_w, SampledSpectrum T_maj,
                                  SampledSpectrum sigma_maj, SampledSpectrum sigma_n);
        public abstract void Step(ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u,
                                  SampledSpectrum T_maj, SampledSpectrum sigma_maj, SampledSpectrum sigma_n);
        public abstract void Finish(ref SampledSpectrum Tr, ref SampledSpectrum inv_w, SampledSpectrum T_maj);
        public abstract void Finish(ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u, SampledSpectrum T_maj);
    }

    // Ratio tracking (Novak et al. 2014), as pbrt-v4 does
    public unsafe class RatioTrackingEstimator : TransmittanceEstimator
    {
        public override string Name() { return "ratio"; }

        public override void Step(ref SampledSpectrum Tr, ref SampledSpectrum inv_w, SampledSpectrum T_maj,
                                  SampledSpectrum sigma_maj, SampledSpectrum sigma_n)
        {
            MediumEvents.RatioTrackingStep(ref Tr, ref inv_w, T_maj, sigma_maj, sigma_n);
        }

        public override void Step(ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u,
                                  SampledSpectrum T_maj, SampledSpectrum sigma_maj, SampledSpectrum sigma_n)
        {
            MediumEvents.RatioTrackingStep(ref T_ray, ref r_l, ref r_u, T_maj, sigma_maj, sigma_n);
        }

        public override void Finish(ref SampledSpectrum Tr, ref SampledSpectrum inv_w, SampledSpectrum T_maj)
        {
            MediumEvents.FinishSegment(ref Tr, ref inv_w, T_maj);
        }

        public override void Finish(ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u, SampledSpectrum T_maj)
        {
            MediumEvents.FinishSegment(ref T_ray, ref r_l, ref r_u, T_maj);
        }
    }

    // Beer-Lambert in closed form for a homogeneous medium; every other medium falls back to ratio tracking
    public sealed unsafe class AnalyticTransmittanceEstimator : RatioTrackingEstimator
    {
        public override string Name() { return "analytic"; }

        public override bool TryClosedForm(Ray ray, Medium medium, Float tMax, SampledWavelengths lambda, out SampledSpectrum T)
        {
            if (!(medium is HomogeneousMedium))
            {
                T = new SampledSpectrum(1f);
                return false;
            }
            MediumProperties mp = medium.SamplePoint(ray.o, lambda);
            Float length = tMax * Length(ray.d);
            if (IsInf(length))
                length = Float.MaxValue;
            T = FastExp(-length * (mp.sigma_a + mp.sigma_s));
            return true;
        }
    }

    // VolumeIntegrator Definition: the tracker and transmittance estimator of one medium
    public sealed unsafe class VolumeIntegrator
    {
        public readonly VolumeTracker tracker;
        public readonly TransmittanceEstimator transmittance;

        public VolumeIntegrator(VolumeTracker tracker, TransmittanceEstimator transmittance)
        {
            this.tracker = tracker;
            this.transmittance = transmittance;
        }

        // The modules are stateless, so volumes share them
        private static readonly MajorantTracker majorantTracker = new MajorantTracker();
        private static readonly GlobalMajorantTracker globalTracker = new GlobalMajorantTracker();
        private static readonly RatioTrackingEstimator ratioEstimator = new RatioTrackingEstimator();
        private static readonly AnalyticTransmittanceEstimator analyticEstimator = new AnalyticTransmittanceEstimator();

        public static readonly VolumeIntegrator Default = new VolumeIntegrator(majorantTracker, ratioEstimator);

        public override string ToString()
        {
            return "[ VolumeIntegrator tracker: " + tracker.Name() + " transmittance: " + transmittance.Name() + " ]";
        }

        // "string tracker" ("majorant" | "global"), "string transmittance" ("ratio" | "analytic")
        public static VolumeIntegrator Create(ParameterDictionary parameters, FileLoc loc)
        {
            string trackerName = parameters.GetOneString("tracker", "majorant");
            string transmittanceName = parameters.GetOneString("transmittance", "ratio");
            VolumeTracker tracker = majorantTracker;
            if (trackerName == "global")
                tracker = globalTracker;
            else if (trackerName != "majorant")
                ErrorExit(loc, $"{trackerName}: volume tracker unknown.");
            TransmittanceEstimator estimator = ratioEstimator;
            if (transmittanceName == "analytic")
                estimator = analyticEstimator;
            else if (transmittanceName != "ratio")
                ErrorExit(loc, $"{transmittanceName}: transmittance estimator unknown.");
            if (tracker == majorantTracker && estimator == ratioEstimator)
                return Default;
            return new VolumeIntegrator(tracker, estimator);
        }
    }

}  // namespace pbrt
