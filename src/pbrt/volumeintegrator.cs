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

        // The tracker for the walks that estimate a transmittance (a tracker that makes collisions without a lookup would
        // give them nothing or everything there)
        public virtual VolumeTracker ForTransmittance() { return this; }
    }

    // Decomposition tracking (Kutz, Habel, Li and Novak, "Spectral and Decomposition Tracking for Rendering Heterogeneous
    // Volumes", SIGGRAPH 2017, section 4). The medium is the sum of a control component, the same along a majorant segment
    // (the smallest extinction there: RayMajorantSegment.controlFraction), and a residual. The collisions of the two are
    // independent, so those of the majorant are sampled as before, and each is the control component's with the probability
    // mu_c / mu_bar: a real collision, made from the coefficients of the medium at the control density, without a lookup. The
    // others are collisions of the residual with the majorant mu_bar - mu_c, which is what the paper's algorithm 2 does with
    // two distances and their minimum.
    //
    // The callback is given the component as if it were the medium: the control component with itself as the majorant (no
    // null collision), the residual (the medium less the control) with the residual majorant. The weights of the callers,
    // T_maj sigma / (T_maj[0] sigma[0]) for the event taken, are then those of the component, and the probabilities of the
    // wavelength that tracks multiply out to the same densities as in delta tracking. Media that cannot say what their
    // coefficients are without a lookup (Medium.ControlProperties) are tracked as by "majorant".
    public sealed unsafe class DecompositionTracker : VolumeTracker
    {
        public override string Name() { return "decomposition"; }

        public override VolumeTracker ForTransmittance() { return plain; }

        public override SampledSpectrum SampleT_maj(Ray ray, Medium medium, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
                                                    Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback)
        {
            MediumProperties unit;
            if (!medium.ControlProperties(lambda, out unit))
            {
                return pbrt.SampleT_maj(ray, medium, tMax, u, ref rng, lambda, callback);
            }
            // Normalize ray direction and update tMax accordingly
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            SampledSpectrum unit_t = unit.sigma_a + unit.sigma_s;
            RayMajorantIterator iter = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            SampledSpectrum T_maj = new SampledSpectrum(1f);
            while (true)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    return T_maj;
                }
                RayMajorantSegment sg = seg.value();
                if (sg.sigma_maj[0] == 0)
                {
                    Float dt = sg.tMax - sg.tMin;
                    if (IsInf(dt))
                    {
                        dt = Float.MaxValue;
                    }
                    T_maj *= FastExp(-dt * sg.sigma_maj);
                    continue;
                }
                // The control component of the segment: the coefficients at its smallest density
                Float controlDensity = unit_t[0] > 0 ? sg.controlFraction * sg.sigma_maj[0] / unit_t[0] : 0;
                MediumProperties control = unit;
                control.sigma_a = controlDensity * unit.sigma_a;
                control.sigma_s = controlDensity * unit.sigma_s;
                SampledSpectrum sigma_c = control.sigma_a + control.sigma_s;
                SampledSpectrum sigma_r = ClampZero(sg.sigma_maj - sigma_c);
                Float pControl = sigma_c[0] / sg.sigma_maj[0];
                Float tMin = sg.tMin;
                while (true)
                {
                    Float t = tMin + SampleExponential(u, sg.sigma_maj[0]);
                    u = rng.Uniform<Float>();
                    if (t < sg.tMax)
                    {
                        T_maj *= FastExp(-(t - tMin) * sg.sigma_maj);
                        Point3f p = ray.Apply(t);
                        bool goOn;
                        if (pControl > 0 && rng.Uniform<Float>() < pControl)
                        {
                            // A collision of the control component: no lookup
                            goOn = callback(p, control, sigma_c, T_maj);
                        }
                        else
                        {
                            MediumProperties mp = medium.SamplePoint(p, lambda, ray.time);
                            mp.sigma_a = ClampZero(mp.sigma_a - control.sigma_a);
                            mp.sigma_s = ClampZero(mp.sigma_s - control.sigma_s);
                            goOn = callback(p, mp, sigma_r, T_maj);
                        }
                        if (!goOn)
                        {
                            return new SampledSpectrum(1f);
                        }
                        T_maj = new SampledSpectrum(1f);
                        tMin = t;
                    }
                    else
                    {
                        Float dt = sg.tMax - tMin;
                        if (IsInf(dt))
                        {
                            dt = Float.MaxValue;
                        }
                        T_maj *= FastExp(-dt * sg.sigma_maj);
                        break;
                    }
                }
            }
        }

        private static readonly MajorantTracker plain = new MajorantTracker();
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
                    MediumProperties mp = medium.SamplePoint(ray.Apply(t), lambda, ray.time);
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
    // a segment. The transmittance is weighed against the other strategies through the rescaled path probabilities
    // (T_ray, r_l, r_u).
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

        // An estimator that runs the whole segment itself (it needs a different majorant than the tracker's): updates T_ray,
        // r_l and r_u for the segment and returns true.
        public virtual bool TryEstimate(Ray ray, Medium medium, Float tMax, ref RNG rng, SampledWavelengths lambda,
                                        ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            return false;
        }

        public abstract void Step(ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u,
                                  SampledSpectrum T_maj, SampledSpectrum sigma_maj, SampledSpectrum sigma_n, ref RNG rng);
        public abstract void Finish(ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u, SampledSpectrum T_maj);
    }

    // Ratio tracking (Novak et al. 2014), as pbrt-v4 does
    public unsafe class RatioTrackingEstimator : TransmittanceEstimator
    {
        public override string Name() { return "ratio"; }

        public override void Step(ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u,
                                  SampledSpectrum T_maj, SampledSpectrum sigma_maj, SampledSpectrum sigma_n, ref RNG rng)
        {
            MediumEvents.RatioTrackingStep(ref T_ray, ref r_l, ref r_u, T_maj, sigma_maj, sigma_n);
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
            if (!(medium is HomogeneousMedium) && !(medium is OSLMedium oslMedium && oslMedium.IsHomogeneous() && !oslMedium.HasBounds()))
            {
                T = new SampledSpectrum(1f);
                return false;
            }
            MediumProperties mp = medium.SamplePoint(ray.o, lambda, ray.time);
            Float length = tMax * Length(ray.d);
            if (IsInf(length))
                length = Float.MaxValue;
            T = FastExp(-length * (mp.sigma_a + mp.sigma_s));
            return true;
        }
    }

    // The integral that the medium has in closed form (Medium.ClosedFormTransmittance: the cosine noise and the kernel noise media); every other
    // medium, and a segment the medium cannot do, falls back to ratio tracking
    public sealed unsafe class ClosedFormEstimator : RatioTrackingEstimator
    {
        public override string Name() { return "closedform"; }

        public override bool TryEstimate(Ray ray, Medium medium, Float tMax, ref RNG rng, SampledWavelengths lambda,
                                         ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            SampledSpectrum T;
            if (!medium.ClosedFormTransmittance(ray, tMax, ref rng, lambda, out T))
            {
                return false;
            }
            T_ray *= T;
            return true;
        }
    }

    // Track-length estimation (delta tracking): the estimate is 1 if no real collision happens along the segment and 0
    // otherwise. At a collision the hero wavelength continues through a null collision with probability sigma_n / sigma_maj.
    // Cheap per sample and binary, so it suits thin or bright-to-dark media better than dense ones.
    public sealed unsafe class TrackLengthEstimator : RatioTrackingEstimator
    {
        public override string Name() { return "tracklength"; }

        public override void Step(ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u,
                                  SampledSpectrum T_maj, SampledSpectrum sigma_maj, SampledSpectrum sigma_n, ref RNG rng)
        {
            Float pNull = sigma_n[0] / sigma_maj[0];
            if (!(rng.Uniform<Float>() < pNull))
            {
                // A real collision: the ray is blocked
                T_ray = new SampledSpectrum(0f);
                return;
            }
            // The same weights as pbrt-v4 gives a null collision of a path, with the probability of choosing it included
            Float pdf = T_maj[0] * sigma_n[0];
            T_ray *= T_maj * sigma_n / pdf;
            r_l *= T_maj * sigma_maj / pdf;
            r_u *= T_maj * sigma_n / pdf;
        }
    }

    // Residual ratio tracking (Novak et al. 2014): a control extinction that the medium guarantees everywhere (its minimum
    // density) is integrated in closed form, and ratio tracking handles the residual with the smaller majorant
    // sigma_maj - sigma_c. A medium without a control, or an empty one, is ratio tracked as usual.
    public sealed unsafe class ResidualRatioEstimator : RatioTrackingEstimator
    {
        public override string Name() { return "residual"; }

        public override bool TryEstimate(Ray ray, Medium medium, Float tMax, ref RNG rng, SampledWavelengths lambda,
                                         ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            SampledSpectrum sigma_c = medium.ControlExtinction(lambda);
            if (!sigma_c)
            {
                return false;
            }
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            RayMajorantIterator iter = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            Float u = rng.Uniform<Float>();
            SampledSpectrum T_maj = new SampledSpectrum(1f);
            while (true)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    break;
                }
                RayMajorantSegment sg = seg.value();
                // The control cannot exceed the majorant of the segment
                SampledSpectrum control = new SampledSpectrum(0f);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    control[c] = std.min(sigma_c[c], sg.sigma_maj[c]);
                }
                SampledSpectrum residualMaj = sg.sigma_maj - control;
                Float dtSegment = sg.tMax - sg.tMin;
                if (IsInf(dtSegment))
                {
                    dtSegment = Float.MaxValue;
                }
                T_ray *= FastExp(-dtSegment * control);
                if (!T_ray)
                {
                    return true;
                }
                if (residualMaj[0] == 0)
                {
                    T_maj *= FastExp(-dtSegment * residualMaj);
                    continue;
                }
                Float tMin = sg.tMin;
                while (true)
                {
                    Float t = tMin + SampleExponential(u, residualMaj[0]);
                    u = rng.Uniform<Float>();
                    if (t < sg.tMax)
                    {
                        T_maj *= FastExp(-(t - tMin) * residualMaj);
                        MediumProperties mp = medium.SamplePoint(ray.Apply(t), lambda, ray.time);
                        SampledSpectrum sigma_n = residualMaj - (mp.sigma_a + mp.sigma_s) + control;
                        MediumEvents.RatioTrackingStep(ref T_ray, ref r_l, ref r_u, T_maj, residualMaj, sigma_n);
                        if (!T_ray)
                        {
                            return true;
                        }
                        T_maj = new SampledSpectrum(1f);
                        tMin = t;
                    }
                    else
                    {
                        T_maj *= FastExp(-(sg.tMax - tMin) * residualMaj);
                        break;
                    }
                }
            }
            MediumEvents.FinishSegment(ref T_ray, ref r_l, ref r_u, T_maj);
            return true;
        }
    }

    // VolumeIntegrator Definition: the tracker and transmittance estimator of one medium
    // The unbiased ray-marching estimator of Kettunen, d'Eon, Pantaleoni and Novak, "An unbiased ray-marching transmittance
    // estimator" (SIGGRAPH 2021), algorithm 5. The transmittance is exp(-tau), tau the optical depth of the segment.
    //   - An estimate X of -tau is the mean of M lookups of the extinction at equidistant points of the segment, shifted
    //     together by one random number (a comb, section 4.3), with the affine control variate that matches the two ends
    //     of the segment (section 4.4; not below 6 lookups, as the paper has it).
    //   - exp(X) alone is jittered ray marching, and biased. The bias is corrected by the terms of the Taylor series of the
    //     exponential about X, estimated from N further such estimates; N comes from a Russian roulette that stops at the
    //     zeroth term 9 times in 10 (algorithm 2: Bhanot and Kennedy's roulette with K = c = 2 behind the probability 0.1).
    //   - Each of the N + 1 estimates is the pivot in its turn, and the others enter through their elementary symmetric
    //     means (algorithm 1), so that every lookup improves every term (equations 31 and 32).
    //   - M follows the optical depth of the majorant along the ray (algorithm 4).
    // Every wavelength is estimated from the same lookups, each by its own series: no wavelength samples for the others,
    // and the path probabilities are left as they are (as with a closed form). An estimate is not bound to [0, 1]: when the
    // correction terms are taken it may lie a little outside, on either side.
    public sealed unsafe class RayMarchingEstimator : RatioTrackingEstimator
    {
        public override string Name() { return "raymarching"; }

        // Algorithm 4: the number of lookups of one estimate, for the optical depth of the majorant
        public static int TupleSize(double tauMajorant)
        {
            double cmf = Math.Ceiling(Math.Cbrt((0.015 + tauMajorant) * (0.65 + tauMajorant) * (60.3 + tauMajorant)));
            // Algorithm 3 for c = K = 2: (1 - pZ) (K + (exp(c) - sum_{k <= K} c^k / k!) / (c^K / K!))
            double expectedOrder = (1 - StopAtZero) * (2 + (Math.Exp(2.0) - 5) / 2);
            double size = Math.Floor(cmf / (expectedOrder + 1) + 0.5);
            return (int)Math.Max(1, Math.Min(size, MaxTupleSize));
        }

        // Algorithm 2: the highest order evaluated, and for each order the inverse of the probability of reaching it
        private static int Roulette(Float u, double* weights)
        {
            weights[0] = 1;
            double P = 1 - StopAtZero;
            if (P <= u)
            {
                return 0;
            }
            weights[1] = 1 / P;
            weights[2] = 1 / P;
            for (int i = 3; i <= MaxOrder; ++i)
            {
                P *= Math.Min(2.0 / i, 1);
                if (P <= u)
                {
                    return i - 1;
                }
                weights[i] = 1 / P;
            }
            return MaxOrder;
        }

        public override bool TryEstimate(Ray ray, Medium medium, Float tMax, ref RNG rng, SampledWavelengths lambda,
                                         ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            // The part of the ray where the medium may be, and the optical depth of its majorant
            RayMajorantIterator iter = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            Float tStart = Infinity, tEnd = 0;
            double tauMajorant = 0;
            while (true)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    break;
                }
                RayMajorantSegment sg = seg.value();
                Float most = sg.sigma_maj.MaxComponentValue();
                if (!(most > 0) || !(sg.tMax > sg.tMin))
                {
                    continue;
                }
                if (IsInf(sg.tMax))
                {
                    // without end: nothing gets through where the majorant is not zero (it is the extinction itself
                    // for the media that have such a segment)
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        if (sg.sigma_maj[c] > 0)
                        {
                            T_ray[c] = 0;
                        }
                    }
                    if (!T_ray)
                    {
                        return true;
                    }
                    continue;
                }
                tStart = std.min(tStart, sg.tMin);
                tEnd = std.max(tEnd, sg.tMax);
                tauMajorant += (double)most * (sg.tMax - sg.tMin);
            }
            if (!(tEnd > tStart))
            {
                return true;
            }
            double length = tEnd - tStart;
            int M = TupleSize(tauMajorant);
            double* weights = stackalloc double[MaxOrder + 1];
            int N = Roulette(rng.Uniform<Float>(), weights);

            // The two ends, for the control variate
            bool matching = M >= MatchingFrom;
            SampledSpectrum ends = new SampledSpectrum(0f);
            if (matching)
            {
                MediumProperties first = medium.SamplePoint(ray.Apply(tStart), lambda, ray.time);
                MediumProperties last = medium.SamplePoint(ray.Apply(tEnd), lambda, ray.time);
                ends = last.sigma_a + last.sigma_s - first.sigma_a - first.sigma_s;
            }

            // The N + 1 combed estimates of -tau, by wavelength
            double* X = stackalloc double[(MaxOrder + 1) * NSpectrumSamples];
            for (int i = 0; i <= N; ++i)
            {
                double u = rng.Uniform<Float>();
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    X[i * NSpectrumSamples + c] = 0;
                }
                for (int j = 0; j < M; ++j)
                {
                    MediumProperties mp = medium.SamplePoint(ray.Apply((Float)(tStart + length * (u + j) / M)), lambda, ray.time);
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        X[i * NSpectrumSamples + c] -= (double)mp.sigma_a[c] + mp.sigma_s[c];
                    }
                }
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    X[i * NSpectrumSamples + c] = length / M * (X[i * NSpectrumSamples + c] - (0.5 - u) * ends[c]);
                }
            }

            // Each estimate as the pivot, the others through their elementary symmetric means
            double* m = stackalloc double[MaxOrder + 1];
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                double T = 0;
                for (int pivot = 0; pivot <= N; ++pivot)
                {
                    double p = X[pivot * NSpectrumSamples + c];
                    m[0] = 1;
                    for (int k = 1; k <= N; ++k)
                    {
                        m[k] = 0;
                    }
                    int n = 0;
                    for (int i = 0; i <= N; ++i)
                    {
                        if (i == pivot)
                        {
                            continue;
                        }
                        ++n;
                        double x = X[i * NSpectrumSamples + c] - p;
                        for (int k = n; k >= 1; --k)
                        {
                            m[k] += (double)k / n * (m[k - 1] * x - m[k]);
                        }
                    }
                    double series = 0, factorial = 1;
                    for (int k = 0; k <= N; ++k)
                    {
                        if (k > 0)
                        {
                            factorial *= k;
                        }
                        series += m[k] * weights[k] / factorial;
                    }
                    T += Math.Exp(p) * series / (N + 1);
                }
                T_ray[c] *= (Float)T;
            }
            return true;
        }

        // The probability of stopping at the zeroth order (the paper's p_Z)
        private const double StopAtZero = 0.9;
        // Below this many lookups the two for the ends are not worth their cost (section 4.7.1)
        private const int MatchingFrom = 6;
        private const int MaxOrder = 24;
        private const int MaxTupleSize = 1 << 16;
    }

    // The next-flight estimators of Cramer 1978, as in Novak, Georgiev, Hanika and Jarosz, "Monte Carlo Methods for Volumetric
    // Light Transport Simulation" (Eurographics 2018 state of the art report), section 5.3.3. The transmittance through the
    // real medium is that through the majorant, T_bar(0, t), plus, for every tentative collision t_j of a walk, the
    // uncollided flight through the majorant from there to the end, weighted by the null collisions up to and at t_j:
    //     next-flight ratio tracking (equation 52):   T_bar(0, t) + sum_j prod_{k <= j} (mu_n(t_k) / mu_bar) T_bar(t_j, t),
    //                                                 the walk going on to the end;
    //     next-flight delta tracking (equation 51):   the same sum up to the first collision that is taken as real, the walk
    //                                                 going on from a collision with the probability of a null collision.
    // Every lookup scores, where ratio tracking scores once at the end and the track-length estimator scores one or nothing.
    //
    // The majorant is the largest over the wavelengths, one for all of them: T_bar is then the same for every wavelength, the
    // weights mu_n / mu_bar are at most one for each, and no wavelength samples for the others (the path probabilities are
    // left as they are, as with a closed form). With it the walk of the delta-tracking variant goes on with the largest
    // mu_n / mu_bar over the wavelengths as its probability, and the weights are divided by it: equation 51 where the medium
    // is grey. An estimate is not negative where the majorant bounds the medium; it is a sum of flights and may exceed one.
    public enum NextFlightKind
    {
        // equation 51: the walk goes on from a collision with the probability of a null collision
        Delta,
        // equation 52: the walk goes on to the end
        Ratio,
        // Georgiev et al. 2019, "Unidir-MIS": ratio tracking and the next flights of one walk, weighed against one another
        Unidirectional
    }

    public sealed unsafe class NextFlightEstimator : RatioTrackingEstimator
    {
        public NextFlightEstimator(NextFlightKind kind)
        {
            this.kind = kind;
        }

        public override string Name()
        {
            return kind == NextFlightKind.Ratio ? "nextflightratio" : (kind == NextFlightKind.Delta ? "nextflight" : "unidirectional");
        }

        public override bool TryEstimate(Ray ray, Medium medium, Float tMax, ref RNG rng, SampledWavelengths lambda,
                                         ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            // The optical depth of the majorant over the whole ray
            double total = 0;
            RayMajorantIterator iter = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            while (true)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    break;
                }
                RayMajorantSegment sg = seg.value();
                Float majorant = sg.sigma_maj.MaxComponentValue();
                if (!(majorant > 0) || !(sg.tMax > sg.tMin))
                {
                    continue;
                }
                if (IsInf(sg.tMax))
                {
                    // without end: nothing gets through where the majorant is not zero (it is the extinction itself for
                    // the media that have such a segment)
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        if (sg.sigma_maj[c] > 0)
                        {
                            T_ray[c] = 0;
                        }
                    }
                    if (!T_ray)
                    {
                        return true;
                    }
                    continue;
                }
                total += (double)majorant * (sg.tMax - sg.tMin);
            }
            if (!(total > 0))
            {
                return true;
            }
            // The walk: the flight through the majorant from the start, then from every tentative collision. Weighed against
            // ratio tracking (Georgiev et al. 2019, section 5.4; the reference code's unidirectional estimator): from a vertex
            // whose flight to the end is E, the flight scores E with the weight 1 / (1 + E) (it is always there), and the
            // walk, if its next step passes the end (which it does with the probability E), scores 1 with the weight E / (1 + E).
            bool weighed = kind == NextFlightKind.Unidirectional;
            Float flight = (Float)Math.Exp(-total);
            SampledSpectrum sum = new SampledSpectrum(weighed ? flight / (1 + flight) : flight), weight = new SampledSpectrum(1f);
            double before = 0;
            bool walking = true;
            iter = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            while (walking)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    break;
                }
                RayMajorantSegment sg = seg.value();
                Float majorant = sg.sigma_maj.MaxComponentValue();
                if (!(majorant > 0) || !(sg.tMax > sg.tMin) || IsInf(sg.tMax))
                {
                    continue;
                }
                Float t = sg.tMin;
                while (true)
                {
                    t += -std.log(1 - rng.Uniform<Float>()) / majorant;
                    if (!(t < sg.tMax))
                    {
                        break;
                    }
                    MediumProperties mp = medium.SamplePoint(ray.Apply(t), lambda, ray.time);
                    SampledSpectrum null_ = (new SampledSpectrum(majorant) - mp.sigma_a - mp.sigma_s) / majorant;
                    weight *= null_;
                    if (!weight)
                    {
                        walking = false;
                        break;
                    }
                    // (the depth of the majorant from the collision to the end of the ray)
                    double ahead = total - before - (double)majorant * (t - sg.tMin);
                    flight = (Float)Math.Exp(-ahead);
                    sum += weight * (weighed ? flight / (1 + flight) : flight);
                    if (kind == NextFlightKind.Delta)
                    {
                        Float goOn = std.min<Float>(1, null_.MaxComponentValue());
                        if (!(rng.Uniform<Float>() < goOn))
                        {
                            walking = false;
                            break;
                        }
                        weight /= goOn;
                    }
                }
                before += (double)majorant * (sg.tMax - sg.tMin);
            }
            if (weighed && walking)
            {
                // the walk passed the end from its last vertex
                sum += weight * (flight / (1 + flight));
            }
            T_ray *= sum;
            return true;
        }

        private readonly NextFlightKind kind;
    }

    // Residual tracking with a control for every voxel (Novak, Selle and Jarosz, "Residual Ratio Tracking for Estimating
    // Attenuation in Participating Media", SIGGRAPH Asia 2014, sections 3.2, 3.3 and 5). Along each segment of the majorant
    // the extinction is a control, constant there, plus a residual: the control is integrated in closed form and only the
    // residual is tracked, with the majorant of the residual.
    //     residual ratio tracking ("residuallocal"): the control is the density that the voxel's densities are about
    //         (RayMajorantSegment.averageFraction), as the paper finds best; the residual has either sign, its majorant is
    //         the largest distance of the density from the control, and a collision multiplies by 1 - mu_r / mu_bar_r.
    //     residual tracking ("residualtracking"): the control is the smallest density of the voxel
    //         (RayMajorantSegment.controlFraction), so that the residual is not negative, and a collision ends the walk with
    //         the probability mu_r / mu_bar_r: the estimate is the control's transmittance or nothing.
    // The existing "residual" has one control for the whole medium (Medium.ControlExtinction). Where a segment has no control
    // (a medium without the grids) these are ratio tracking and the track-length estimator there. One majorant for all the
    // wavelengths, the largest; the weights are by wavelength and the path probabilities are left as they are. For the binary
    // one the walk goes on with the largest 1 - mu_r / mu_bar_r over the wavelengths, and the weights are divided by it.
    public sealed unsafe class LocalResidualEstimator : RatioTrackingEstimator
    {
        public LocalResidualEstimator(bool binary)
        {
            this.binary = binary;
        }

        public override string Name() { return binary ? "residualtracking" : "residuallocal"; }

        public override bool TryEstimate(Ray ray, Medium medium, Float tMax, ref RNG rng, SampledWavelengths lambda,
                                         ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            RayMajorantIterator iter = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            while (true)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    return true;
                }
                RayMajorantSegment sg = seg.value();
                Float largest = sg.sigma_maj.MaxComponentValue();
                if (!(largest > 0) || !(sg.tMax > sg.tMin))
                {
                    continue;
                }
                if (IsInf(sg.tMax))
                {
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        if (sg.sigma_maj[c] > 0)
                        {
                            T_ray[c] = 0;
                        }
                    }
                    if (!T_ray)
                    {
                        return true;
                    }
                    continue;
                }
                // The control of the segment and the majorant of the residual
                Float fraction = binary ? sg.controlFraction : sg.averageFraction;
                Float bound = binary ? 1 - fraction : std.max(1 - fraction, fraction - sg.controlFraction);
                SampledSpectrum control = fraction * sg.sigma_maj;
                // (the exponential itself, not its fast approximation: this factor is often the whole estimate)
                T_ray *= Exp(-(sg.tMax - sg.tMin) * control);
                Float majorant = largest * bound;
                if (!(majorant > 0))
                {
                    continue;
                }
                Float t = sg.tMin;
                while (true)
                {
                    t += -std.log(1 - rng.Uniform<Float>()) / majorant;
                    if (!(t < sg.tMax))
                    {
                        break;
                    }
                    MediumProperties mp = medium.SamplePoint(ray.Apply(t), lambda, ray.time);
                    SampledSpectrum null_ = new SampledSpectrum(1f) - (mp.sigma_a + mp.sigma_s - control) / majorant;
                    if (binary)
                    {
                        Float goOn = std.min<Float>(1, null_.MaxComponentValue());
                        if (!(rng.Uniform<Float>() < goOn))
                        {
                            T_ray = new SampledSpectrum(0f);
                            return true;
                        }
                        null_ /= goOn;
                    }
                    T_ray *= null_;
                    if (!T_ray)
                    {
                        return true;
                    }
                }
            }
        }

        private readonly bool binary;
    }

    // MajorantProfile Definition: the majorant along a ray as one function, for the estimators that place their lookups by
    // the optical depth of the majorant and not by a walk. The majorant is the largest over the wavelengths. In the depth of
    // the majorant the ray is a homogeneous medium of extinction 1 and of length `total`.
    public sealed unsafe class MajorantProfile
    {
        public double total;

        // False where nothing gets through (T_ray is then zero) or where there is no medium along the ray
        public static bool Build(Ray ray, Medium medium, Float tMax, SampledWavelengths lambda, ref SampledSpectrum T_ray, out MajorantProfile profile)
        {
            profile = new MajorantProfile();
            profile.ray = ray;
            profile.medium = medium;
            profile.lambda = lambda;
            RayMajorantIterator iter = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            while (true)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    break;
                }
                RayMajorantSegment sg = seg.value();
                Float majorant = sg.sigma_maj.MaxComponentValue();
                if (!(majorant > 0) || !(sg.tMax > sg.tMin))
                {
                    continue;
                }
                if (IsInf(sg.tMax))
                {
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        if (sg.sigma_maj[c] > 0)
                        {
                            T_ray[c] = 0;
                        }
                    }
                    if (!T_ray)
                    {
                        return false;
                    }
                    continue;
                }
                if (profile.count == profile.start.Length)
                {
                    Array.Resize(ref profile.start, 2 * profile.count);
                    Array.Resize(ref profile.majorant, 2 * profile.count);
                    Array.Resize(ref profile.before, 2 * profile.count);
                }
                profile.start[profile.count] = sg.tMin;
                profile.majorant[profile.count] = majorant;
                profile.before[profile.count] = profile.total;
                ++profile.count;
                profile.total += (double)majorant * (sg.tMax - sg.tMin);
            }
            return profile.total > 0;
        }

        // The null collision coefficient over the majorant, by wavelength, at the point of the ray where the depth of the
        // majorant from the start is `depth`: one lookup of the medium
        public SampledSpectrum Null(double depth)
        {
            int lo = 0, hi = count - 1;
            while (lo < hi)
            {
                int middle = (lo + hi + 1) / 2;
                if (before[middle] <= depth)
                {
                    lo = middle;
                }
                else
                {
                    hi = middle - 1;
                }
            }
            Float t = (Float)(start[lo] + (depth - before[lo]) / majorant[lo]);
            MediumProperties mp = medium.SamplePoint(ray.Apply(t), lambda, ray.time);
            return (new SampledSpectrum(majorant[lo]) - mp.sigma_a - mp.sigma_s) / majorant[lo];
        }

        private Ray ray;
        private Medium medium;
        private SampledWavelengths lambda;
        private int count;
        private Float[] start = new Float[16], majorant = new Float[16];
        private double[] before = new double[16];
    }

    // The power-series estimators of Georgiev, Misso, Hachisuka, Nowrouzezahrai, Krivanek and Jarosz, "Integral formulations of
    // volumetric transmittance" (SIGGRAPH Asia 2019), section 5.2, as in the authors' reference code (pseries_cumulative,
    // pseries_cdf). With the null thickness tau_n = the integral of mu_bar - mu,
    //     T = exp(-tau_bar) exp(tau_n) = exp(-tau_bar) sum_k tau_n^k / k!,
    // and the sum is estimated by its prefix sums: the term k is the term k - 1 times an estimate of tau_n over k, each
    // estimate one lookup at a point drawn in proportion to the majorant (so that it is tau_bar mu_n / mu_bar there). The two
    // differ in when the sum stops, both with one random number for the whole roulette:
    //     "pseriescumulative": a term is taken with the probability of its size over that of the one before, at most 1;
    //     "pseriescmf": every term is taken until the Poisson distribution of mean tau_bar has given 99% of its mass, then
    //         a term k with the probability tau_bar / k.
    // The lookups are independent of one another, not the steps of a walk. Every wavelength has its own sum from the same
    // lookups; the roulette of the first goes by the largest term over the wavelengths. A majorant that is exceeded makes
    // terms negative, which the sums carry. Beyond a majorant depth of 600 the first factor is nothing a number can hold, and
    // the ray is left to ratio tracking.
    public sealed unsafe class PowerSeriesEstimator : RatioTrackingEstimator
    {
        public PowerSeriesEstimator(bool cmf)
        {
            this.cmf = cmf;
        }

        public override string Name() { return cmf ? "pseriescmf" : "pseriescumulative"; }

        public override bool TryEstimate(Ray ray, Medium medium, Float tMax, ref RNG rng, SampledWavelengths lambda,
                                         ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            MajorantProfile profile;
            if (!MajorantProfile.Build(ray, medium, tMax, lambda, ref T_ray, out profile))
            {
                return true;
            }
            double tau = profile.total;
            if (tau > MaxDepth)
            {
                return false;
            }
            double* sum = stackalloc double[NSpectrumSamples];
            double* W = stackalloc double[NSpectrumSamples];
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                sum[c] = 0;
                W[c] = 1;
            }
            double i = 1, rr;
            do
            {
                rr = rng.Uniform<Float>();
            } while (rr == 0);
            if (cmf)
            {
                // Every term until the terms that are left are a hundredth of the mass
                double runningCDF = 0, lastPDF = Math.Exp(-tau);
                while (runningCDF < MassTaken)
                {
                    SampledSpectrum null_ = profile.Null(rng.Uniform<Float>() * tau);
                    runningCDF += lastPDF;
                    lastPDF *= tau / i;
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        sum[c] += W[c];
                        W[c] *= tau / i * null_[c];
                    }
                    i += 1;
                }
                while (true)
                {
                    double accept = tau / i;
                    if (accept <= rr || i > MaxTerms)
                    {
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            sum[c] += W[c];
                        }
                        break;
                    }
                    rr /= accept;
                    SampledSpectrum null_ = profile.Null(rng.Uniform<Float>() * tau);
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        sum[c] += W[c];
                        W[c] *= tau / i * null_[c] / accept;
                    }
                    i += 1;
                }
            }
            else
            {
                while (true)
                {
                    SampledSpectrum null_ = profile.Null(rng.Uniform<Float>() * tau);
                    double accept = 0;
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        accept = Math.Max(accept, Math.Abs(W[c] * tau / i * null_[c]));
                    }
                    accept = Math.Min(accept, 1);
                    if (accept <= rr || i > MaxTerms)
                    {
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            sum[c] += W[c];
                        }
                        break;
                    }
                    rr /= accept;
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        sum[c] += W[c];
                        W[c] *= tau / i * null_[c] / accept;
                    }
                    i += 1;
                }
            }
            double through = Math.Exp(-tau);
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                T_ray[c] *= (Float)(sum[c] * through);
            }
            return true;
        }

        private readonly bool cmf;
        private const double MassTaken = 0.99;
        private const double MaxDepth = 600;
        private const double MaxTerms = 1e6;
    }

    // The bidirectional estimator of Georgiev et al. 2019 (section 5.4, "Bidir-MIS"), as in the authors' reference code. Two
    // walks of tentative collisions, one from each end of the ray. A set of null collisions between the ends can be made by
    // the first i collisions of the walk from the start, the first j of the walk from the end and the flight through the
    // majorant between the two, for every i and j whose points are in order; or by a walk alone that passes the far end
    // (ratio tracking, from either side). All of them are taken, each with the weight of the balance heuristic over every
    // way the same set of points could have been made: with p(gap) = 1 / T_bar(gap), the density of the split at a gap is
    // p(gap) and that of each whole walk is 1, so a connection scores
    //     prod(mu_n / mu_bar) / (p(connection) + sum p(gaps on the two sides) + 2)
    // and a whole walk prod(mu_n / mu_bar) / (2 + sum p(all its gaps)). In the depth of the majorant (MajorantProfile), where
    // the majorant is 1 whatever it is along the ray. Twice the lookups of ratio tracking, and the pairs of the two walks.
    public sealed unsafe class BidirectionalEstimator : RatioTrackingEstimator
    {
        public override string Name() { return "bidirectional"; }

        public override bool TryEstimate(Ray ray, Medium medium, Float tMax, ref RNG rng, SampledWavelengths lambda,
                                         ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            MajorantProfile profile;
            if (!MajorantProfile.Build(ray, medium, tMax, lambda, ref T_ray, out profile))
            {
                return true;
            }
            double tau = profile.total;
            if (tau > MaxDepth)
            {
                return false;
            }
            // The two walks: the depths of their collisions from their own end, then what is there
            int m = 0, n = 0;
            double[] fromStart = new double[16], fromEnd = new double[16];
            double u = -Math.Log(1 - rng.Uniform<Float>());
            while (u < tau)
            {
                if (m == fromStart.Length)
                {
                    Array.Resize(ref fromStart, 2 * m);
                }
                fromStart[m++] = u;
                u += -Math.Log(1 - rng.Uniform<Float>());
            }
            u = -Math.Log(1 - rng.Uniform<Float>());
            while (u < tau)
            {
                if (n == fromEnd.Length)
                {
                    Array.Resize(ref fromEnd, 2 * n);
                }
                fromEnd[n++] = u;
                u += -Math.Log(1 - rng.Uniform<Float>());
            }
            // The products of the null collisions of the first i points of each walk, and the sums of p(gap) over the gaps
            // before the i-th point (gapsA[m + 1], gapsB[n + 1]: with the last gap, to the far end)
            SampledSpectrum[] productA = new SampledSpectrum[m + 1], productB = new SampledSpectrum[n + 1];
            double[] gapsA = new double[m + 2], gapsB = new double[n + 2];
            productA[0] = new SampledSpectrum(1f);
            for (int i = 1; i <= m; ++i)
            {
                productA[i] = productA[i - 1] * profile.Null(fromStart[i - 1]);
                gapsA[i] = gapsA[i - 1] + Math.Exp(fromStart[i - 1] - (i > 1 ? fromStart[i - 2] : 0));
            }
            gapsA[m + 1] = gapsA[m] + Math.Exp(tau - (m > 0 ? fromStart[m - 1] : 0));
            productB[0] = new SampledSpectrum(1f);
            for (int j = 1; j <= n; ++j)
            {
                productB[j] = productB[j - 1] * profile.Null(tau - fromEnd[j - 1]);
                gapsB[j] = gapsB[j - 1] + Math.Exp(fromEnd[j - 1] - (j > 1 ? fromEnd[j - 2] : 0));
            }
            gapsB[n + 1] = gapsB[n] + Math.Exp(tau - (n > 0 ? fromEnd[n - 1] : 0));
            // Every connection, and the two walks alone
            SampledSpectrum sum = productA[m] * (Float)(1 / (2 + gapsA[m + 1])) + productB[n] * (Float)(1 / (2 + gapsB[n + 1]));
            for (int i = 0; i <= m; ++i)
            {
                double a = i > 0 ? fromStart[i - 1] : 0;
                for (int j = 0; j <= n; ++j)
                {
                    double b = j > 0 ? tau - fromEnd[j - 1] : tau;
                    if (b < a)
                    {
                        break;
                    }
                    sum += productA[i] * productB[j] * (Float)(1 / (Math.Exp(b - a) + gapsA[i] + gapsB[j] + 2));
                }
            }
            T_ray *= sum;
            return true;
        }

        private const double MaxDepth = 600;
    }

    public sealed unsafe class VolumeIntegrator
    {
        public readonly VolumeTracker tracker;
        public readonly TransmittanceEstimator transmittance;
        // Which of the integrators of Fong et al. the volume is given by the "production" path tracer (productionvolume.cs)
        public VolumeScattering scattering = VolumeScattering.Multiple;
        // The step of the integrator that marches (SingleScatterPDFVolume), in the units of the scene
        public Float stepSize = 0.1f;
        // How a single scattering volume chooses the point of the scattering along the ray (the course, section 4.2):
        // with the density of the transmittance, with the equiangular density about a point of a light, or with both
        public VolumeDistanceSampling distanceSampling = VolumeDistanceSampling.Density;
        // Whether the light at the point of a single scattering is sampled from the lights alone, or from the lights and
        // from the phase function with the heuristic (the course, listings 10 and 11)
        public bool phaseSampling = false;
        // The number of points lit along one interval of a single scattering volume (the course, section 4.1)
        public int lightSamples = 1;
        // The contrast control of section 3.7: the scattering coefficient of the shadow rays of a multiple scattering volume
        // is multiplied by this to the power of the depth of the path
        public Float contrast = 1;
        // Whether a single scattering volume gathers the light scattered twice too, and how (Volume.DoubleScattering)
        public VolumeDoubleScattering doubleScattering = VolumeDoubleScattering.None;
        // Spectral tracking (Kutz et al. 2017, section 5) in the multiple scattering volume of a heterogeneous medium, in the
        // place of the wavelength that samples for the others
        public bool spectralTracking = false;
        // Its probabilities: from the largest coefficient over the wavelengths (section 5.1.1) in the place of the mean of the
        // coefficients times the throughput of the path (section 5.1.2)
        public bool spectralMaximum = false;
        // Whether an absorption is an event that ends the walk, in the place of the reduced termination of section 5.1.3
        public bool spectralAbsorption = false;
        // Whether the emission between two points (Volume.Emission) is tracked about the mean extinction of each cell of the
        // medium, in the place of ratio tracking (Volume.TrackEmissionResidual)
        public bool emissionResidual = false;
        // Whether a multiple scattering volume puts one more scattering between each of its collisions and a light at a
        // position (Volume.OnceMoreShare, Volume.OnceMoreShare2)
        public VolumeOnceMore onceMore = VolumeOnceMore.None;

        public VolumeIntegrator(VolumeTracker tracker, TransmittanceEstimator transmittance)
        {
            this.tracker = tracker;
            this.transmittance = transmittance;
        }

        // The modules are stateless, so volumes share them
        private static readonly MajorantTracker majorantTracker = new MajorantTracker();
        private static readonly GlobalMajorantTracker globalTracker = new GlobalMajorantTracker();
        private static readonly DecompositionTracker decompositionTracker = new DecompositionTracker();

        // The tracker of Kutz et al. 2017, for a medium made in code
        public static VolumeTracker Decomposition() { return decompositionTracker; }
        private static readonly RatioTrackingEstimator ratioEstimator = new RatioTrackingEstimator();
        private static readonly AnalyticTransmittanceEstimator analyticEstimator = new AnalyticTransmittanceEstimator();
        private static readonly ClosedFormEstimator closedFormEstimator = new ClosedFormEstimator();
        private static readonly TrackLengthEstimator trackLengthEstimator = new TrackLengthEstimator();
        private static readonly ResidualRatioEstimator residualEstimator = new ResidualRatioEstimator();
        private static readonly RayMarchingEstimator rayMarchingEstimator = new RayMarchingEstimator();
        private static readonly NextFlightEstimator nextFlightEstimator = new NextFlightEstimator(NextFlightKind.Delta);
        private static readonly NextFlightEstimator nextFlightRatioEstimator = new NextFlightEstimator(NextFlightKind.Ratio);
        private static readonly NextFlightEstimator unidirectionalEstimator = new NextFlightEstimator(NextFlightKind.Unidirectional);
        private static readonly BidirectionalEstimator bidirectionalEstimator = new BidirectionalEstimator();
        private static readonly LocalResidualEstimator residualLocalEstimator = new LocalResidualEstimator(false);
        private static readonly LocalResidualEstimator residualTrackingEstimator = new LocalResidualEstimator(true);
        private static readonly PowerSeriesEstimator powerSeriesCumulativeEstimator = new PowerSeriesEstimator(false);
        private static readonly PowerSeriesEstimator powerSeriesCmfEstimator = new PowerSeriesEstimator(true);

        // The estimators that a medium made in code may name: "ratio", "analytic", "closedform", "tracklength", "residual", "raymarching",
        // "nextflight", "nextflightratio", "unidirectional", "bidirectional", "residuallocal", "residualtracking",
        // "pseriescumulative", "pseriescmf"; null for any other name
        public static TransmittanceEstimator Transmittance(string name)
        {
            switch (name)
            {
            case "ratio":
                return ratioEstimator;
            case "analytic":
                return analyticEstimator;
            case "closedform":
                return closedFormEstimator;
            case "tracklength":
                return trackLengthEstimator;
            case "residual":
                return residualEstimator;
            case "raymarching":
                return rayMarchingEstimator;
            case "nextflight":
                return nextFlightEstimator;
            case "nextflightratio":
                return nextFlightRatioEstimator;
            case "unidirectional":
                return unidirectionalEstimator;
            case "bidirectional":
                return bidirectionalEstimator;
            case "residuallocal":
                return residualLocalEstimator;
            case "residualtracking":
                return residualTrackingEstimator;
            case "pseriescumulative":
                return powerSeriesCumulativeEstimator;
            case "pseriescmf":
                return powerSeriesCmfEstimator;
            default:
                return null;
            }
        }

        // The estimator of Kettunen et al. 2021, for a medium made in code
        public static TransmittanceEstimator RayMarching() { return rayMarchingEstimator; }

        public static readonly VolumeIntegrator Default = new VolumeIntegrator(majorantTracker, ratioEstimator);

        public override string ToString()
        {
            return "[ VolumeIntegrator tracker: " + tracker.Name() + " transmittance: " + transmittance.Name() + " ]";
        }

        // "string tracker" ("majorant" | "global" | "decomposition"), "string spectral" ("hero" | "tracking"),
        // "string spectralprobabilities" ("average" | "maximum"), "bool spectralabsorption"
        //, "string transmittance" ("ratio" | "analytic" | "tracklength" | "residual" | "raymarching" | "nextflight" | "nextflightratio" | "unidirectional" | "bidirectional" |
        // "residuallocal" | "residualtracking" | "pseriescumulative" | "pseriescmf"),
        // "string scattering" ("multiple" | "single" | "singlepdf" | "absorption" | "none"), "float stepsize",
        // "string distancesampling" ("density" | "equiangular" | "mis"), "string lightsampling" ("light" | "mis"),
        // "integer lightsamples", "float contrast", "string doublescattering" ("none" | "plain" | "joint" | "mis"),
        // "string oncemore" ("none" | "omnee" | "omnee2")
        public static VolumeIntegrator Create(ParameterDictionary parameters, FileLoc loc, string defaultTransmittance)
        {
            string trackerName = parameters.GetOneString("tracker", "majorant");
            string transmittanceName = parameters.GetOneString("transmittance", defaultTransmittance);
            VolumeTracker tracker = majorantTracker;
            if (trackerName == "global")
                tracker = globalTracker;
            else if (trackerName == "decomposition")
                tracker = decompositionTracker;
            else if (trackerName != "majorant")
                ErrorExit(loc, $"{trackerName}: volume tracker unknown.");
            TransmittanceEstimator estimator = Transmittance(transmittanceName);
            if (estimator == null)
                ErrorExit(loc, $"{transmittanceName}: transmittance estimator unknown.");
            string scatteringName = parameters.GetOneString("scattering", "multiple");
            VolumeScattering scattering = VolumeScattering.Multiple;
            if (scatteringName == "single")
                scattering = VolumeScattering.Single;
            else if (scatteringName == "singlepdf")
                scattering = VolumeScattering.SinglePDF;
            else if (scatteringName == "none")
                scattering = VolumeScattering.None;
            else if (scatteringName == "absorption")
                scattering = VolumeScattering.Absorption;
            else if (scatteringName != "multiple")
                ErrorExit(loc, $"{scatteringName}: \"scattering\" must be \"multiple\", \"single\", \"singlepdf\", \"absorption\" or \"none\".");
            Float stepSize = parameters.GetOneFloat("stepsize", 0.1f);
            if (!(stepSize > 0))
                ErrorExit(loc, "\"stepsize\" must be positive.");
            string distanceName = parameters.GetOneString("distancesampling", "density");
            VolumeDistanceSampling distanceSampling = VolumeDistanceSampling.Density;
            if (distanceName == "equiangular")
                distanceSampling = VolumeDistanceSampling.Equiangular;
            else if (distanceName == "mis")
                distanceSampling = VolumeDistanceSampling.MIS;
            else if (distanceName != "density")
                ErrorExit(loc, $"{distanceName}: \"distancesampling\" must be \"density\", \"equiangular\" or \"mis\".");
            string lightName = parameters.GetOneString("lightsampling", "light");
            if (lightName != "light" && lightName != "mis")
                ErrorExit(loc, $"{lightName}: \"lightsampling\" must be \"light\" or \"mis\".");
            int lightSamples = parameters.GetOneInt("lightsamples", 1);
            if (lightSamples < 1)
                ErrorExit(loc, "\"lightsamples\" must be at least 1.");
            Float contrast = parameters.GetOneFloat("contrast", 1);
            if (!(contrast > 0) || contrast > 1)
                ErrorExit(loc, "\"contrast\" must be in (0, 1].");
            string spectralName = parameters.GetOneString("spectral", "hero");
            if (spectralName != "hero" && spectralName != "tracking")
                ErrorExit(loc, $"{spectralName}: \"spectral\" must be \"hero\" or \"tracking\".");
            string emissionName = parameters.GetOneString("emission", "ratio");
            if (emissionName != "ratio" && emissionName != "residual")
                ErrorExit(loc, $"{emissionName}: \"emission\" must be \"ratio\" or \"residual\".");
            if (tracker == majorantTracker && estimator == ratioEstimator && scattering == VolumeScattering.Multiple && contrast == 1 && spectralName == "hero" &&
                distanceSampling == VolumeDistanceSampling.Density && emissionName == "ratio" && parameters.GetOneString("doublescattering", "none") == "none" &&
                parameters.GetOneString("oncemore", "none") == "none")
                return Default;
            VolumeIntegrator made = new VolumeIntegrator(tracker, estimator);
            made.emissionResidual = emissionName == "residual";
            made.scattering = scattering;
            made.stepSize = stepSize;
            made.distanceSampling = distanceSampling;
            made.phaseSampling = lightName == "mis";
            made.lightSamples = lightSamples;
            made.contrast = contrast;
            string doubleName = parameters.GetOneString("doublescattering", "none");
            if (doubleName == "plain")
                made.doubleScattering = VolumeDoubleScattering.Plain;
            else if (doubleName == "joint")
                made.doubleScattering = VolumeDoubleScattering.Joint;
            else if (doubleName == "mis")
                made.doubleScattering = VolumeDoubleScattering.MIS;
            else if (doubleName != "none")
                ErrorExit(loc, $"{doubleName}: \"doublescattering\" must be \"none\", \"plain\", \"joint\" or \"mis\".");
            string onceMoreName = parameters.GetOneString("oncemore", "none");
            if (onceMoreName == "omnee")
                made.onceMore = VolumeOnceMore.OnceMore;
            else if (onceMoreName == "omnee2")
                made.onceMore = VolumeOnceMore.OnceMore2;
            else if (onceMoreName != "none")
                ErrorExit(loc, $"{onceMoreName}: \"oncemore\" must be \"none\", \"omnee\" or \"omnee2\".");
            made.spectralTracking = spectralName == "tracking";
            string probabilityName = parameters.GetOneString("spectralprobabilities", "average");
            if (probabilityName != "average" && probabilityName != "maximum")
                ErrorExit(loc, $"{probabilityName}: \"spectralprobabilities\" must be \"average\" or \"maximum\".");
            made.spectralMaximum = probabilityName == "maximum";
            made.spectralAbsorption = parameters.GetOneBool("spectralabsorption", false);
            return made;
        }
    }

}  // namespace pbrt
