// (not in pbrt-v4) refractivemedium.cs
//
// A medium with a spatially varying refractive index (docs/refractive-media.md, stage 4). It wraps an ordinary medium, which
// gives the coefficients, a RefractiveIndexField, which bends the paths, and a box that bounds the region where the index
// varies. Collisions are sampled along the curved path in the arc length s, with a majorant that is a function of the
// position only (a constant over the medium), so the forward and the reverse walk of a curve have the same majorant
// transmittance. The coefficients may vary along the curve: the callback of SampleT_majCurved is called at every tentative
// collision with the medium properties there, as in SampleT_maj, and the integrator does delta or ratio tracking with them.

using System;

namespace pbrt
{

    // RefractiveMedium Definition
    public sealed unsafe class RefractiveMedium : Medium
    {
        // RefractiveMedium Public Methods

        // majorant gives sigma_maj for the wavelengths: it has to bound sigma_a + sigma_s of the inner medium everywhere in
        // the bounds. stepSize is the arc length of one integrator step. The path is discretised on a grid of steps from its
        // start, independently of where the collisions fall.
        public RefractiveMedium(Medium inner, RefractiveIndexField field, Bounds3f bounds,
                                Func<SampledWavelengths, SampledSpectrum> majorant, Float stepSize)
        {
            this.inner = inner;
            this.field = field;
            this.bounds = bounds;
            this.majorant = majorant;
            this.stepSize = stepSize;
            volumeIntegrator = inner.volumeIntegrator;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ RefractiveMedium stepSize: %f inner: %s ]", stepSize, inner);
        }

        public override bool IsEmissive() { return inner.IsEmissive(); }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            return inner.SamplePoint(p, lambda);
        }

        // For code that treats the medium as a straight-ray medium: one segment with the constant majorant
        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(new HomogeneousMajorantIterator(0, tMax, majorant(lambda)));
        }

        public override SampledSpectrum ControlExtinction(SampledWavelengths lambda)
        {
            return inner.ControlExtinction(lambda);
        }

        public RefractiveIndexField Field() { return field; }
        public Bounds3f Bounds() { return bounds; }
        public Float StepSize() { return stepSize; }

        public Float Index(Point3f p)
        {
            field.ValueAndGradient(p, out Float value, out Vector3f gradient);
            return value;
        }

        public bool Contains(Point3f p)
        {
            return p.x >= bounds.pMin.x && p.x <= bounds.pMax.x && p.y >= bounds.pMin.y && p.y <= bounds.pMax.y &&
                   p.z >= bounds.pMin.z && p.z <= bounds.pMax.z;
        }

        // The factor of the throughput of a path from a point of index nStart to one of index nEnd: the basic radiance L / n^2
        // is what the path integral carries (Pediredla et al., table 1), so the radiance carried to the end is scaled by
        // (nEnd / nStart)^2 with respect to the radiance at the start
        public static Float RadianceScale(Float nStart, Float nEnd)
        {
            return (nEnd * nEnd) / (nStart * nStart);
        }

        // The point and the direction of the path at the arc length s from a state of the grid, for s no larger than the step
        static SymplecticRay Partial(RefractiveIndexField field, SymplecticRay ray, Float s)
        {
            ray.Step(field, s);
            return ray;
        }

        // Samples collisions along the path that leaves p along direction, in the arc length, up to maxLength or the point
        // where the path leaves the bounds. The callback gets the point, the direction of the path there (the direction the
        // phase function sees), the properties of the medium, sigma_maj and the majorant transmittance since the last
        // callback; returning false ends the walk (the result is then 1). Otherwise the result is the majorant transmittance
        // from the last collision to the end, and the end is reported: its point, its direction and its arc length.
        public SampledSpectrum SampleT_majCurved(Point3f p, Vector3f direction, Float maxLength, Float u, ref RNG rng,
                                                 SampledWavelengths lambda,
                                                 Func<Point3f, Vector3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback,
                                                 out Point3f endPoint, out Vector3f endDirection, out Float endLength)
        {
            SampledSpectrum sigma_maj = majorant(lambda);
            SymplecticRay ray = new SymplecticRay(field, p, direction);
            endPoint = p;
            endDirection = ray.Direction();
            endLength = 0;
            if (!Contains(p) || maxLength <= 0)
            {
                return new SampledSpectrum(1f);
            }
            Float sigma0 = sigma_maj[0];
            // The arc length of the next tentative collision, and of the last one
            Float nextCollision = sigma0 > 0 ? pbrt.SampleExponential(u, sigma0) : Float.PositiveInfinity;
            Float lastCollision = 0;
            // The arc length of the state of the grid
            Float s = 0;
            while (true)
            {
                SymplecticRay next = ray;
                next.Step(field, stepSize);
                Float gridEnd = s + stepSize;
                Float segmentEnd = Math.Min(maxLength, gridEnd);
                bool ended = segmentEnd == maxLength;
                if (!Contains(next.p))
                {
                    // The path leaves the bounds in this step: bisection on the partial step
                    Float lo = 0, hi = stepSize;
                    for (int i = 0; i < 40; ++i)
                    {
                        Float mid = 0.5f * (lo + hi);
                        if (Contains(Partial(field, ray, mid).p)) { lo = mid; } else { hi = mid; }
                    }
                    if (s + lo <= segmentEnd)
                    {
                        segmentEnd = s + lo;
                        ended = true;
                    }
                }
                if (nextCollision < segmentEnd)
                {
                    SymplecticRay atCollision = Partial(field, ray, nextCollision - s);
                    SampledSpectrum T_maj = pbrt.FastExp(-(nextCollision - lastCollision) * sigma_maj);
                    MediumProperties mp = inner.SamplePoint(atCollision.p, lambda);
                    if (!callback(atCollision.p, atCollision.Direction(), mp, sigma_maj, T_maj))
                    {
                        return new SampledSpectrum(1f);
                    }
                    lastCollision = nextCollision;
                    nextCollision = lastCollision + (sigma0 > 0 ? pbrt.SampleExponential(rng.Uniform<Float>(), sigma0) : Float.PositiveInfinity);
                    continue;
                }
                if (ended)
                {
                    SymplecticRay atEnd = Partial(field, ray, segmentEnd - s);
                    endPoint = atEnd.p;
                    endDirection = atEnd.Direction();
                    endLength = segmentEnd;
                    return pbrt.FastExp(-(segmentEnd - lastCollision) * sigma_maj);
                }
                ray = next;
                s = gridEnd;
            }
        }

        // A connection from p to target through the medium (stage 3), the target being inside the medium or on its boundary
        public bool Connect(Point3f start, Point3f target, Vector3f initialDirection, RefractiveConnectionOptions options,
                            out RefractiveConnectionPath path)
        {
            Float margin = 2 * options.tolerance;
            Bounds3f expanded = new Bounds3f(bounds.pMin - new Vector3f(margin, margin, margin),
                                             bounds.pMax + new Vector3f(margin, margin, margin));
            RefractiveConnectionOptions local = options.Clone();
            local.inside = (Point3f q) => q.x >= expanded.pMin.x && q.x <= expanded.pMax.x && q.y >= expanded.pMin.y &&
                                          q.y <= expanded.pMax.y && q.z >= expanded.pMin.z && q.z <= expanded.pMax.z;
            return RefractiveConnection.Solve(field, start, target, initialDirection, local, out path);
        }

        // RefractiveMedium Private Members
        private Medium inner;
        private RefractiveIndexField field;
        private Bounds3f bounds;
        private Func<SampledWavelengths, SampledSpectrum> majorant;
        private Float stepSize;
    }

}  // namespace pbrt
