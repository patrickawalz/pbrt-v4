// (not in pbrt-v4) refractivemedium.cs
//
// A medium with a spatially varying refractive index (docs/refractive-media.md, stage 4). It wraps an ordinary medium, which
// gives the coefficients, a RefractiveIndexField, which bends the paths, and a box that bounds the region where the index
// varies. Collisions are sampled along the curved path in the arc length s, with a majorant that is a function of the
// position only (a constant over the medium), so the forward and the reverse walk of a curve have the same majorant
// transmittance. The coefficients may vary along the curve: the callback of SampleT_majCurved is called at every tentative
// collision with the medium properties there, as in SampleT_maj, and the integrator does delta or ratio tracking with them.

using System;
using System.Collections.Generic;

namespace pbrt
{


    // (not in pbrt-v4) GridModulatedMedium Definition: a medium whose extinction coefficients are those of the inner medium times
    // the density grid, and whose emission is the inner medium's times the emission grid. The grids (x fastest, then y, then z;
    // values at the corners of the cells over the bounds) are interpolated trilinearly, and are zero outside the bounds.
    public sealed unsafe class GridModulatedMedium : Medium
    {
        // Either grid can be null, which is a factor of one
        public GridModulatedMedium(Medium inner, Bounds3f bounds, int resolutionX, int resolutionY, int resolutionZ,
                                   Float[] density, Float[] emission)
        {
            if (resolutionX < 2 || resolutionY < 2 || resolutionZ < 2)
            {
                pbrt.ErrorExit("A grid of a medium needs at least two values along each axis.");
            }
            if ((density != null && density.Length != resolutionX * resolutionY * resolutionZ) ||
                (emission != null && emission.Length != resolutionX * resolutionY * resolutionZ))
            {
                pbrt.ErrorExit("A grid of a medium needs nx * ny * nz values.");
            }
            this.inner = inner;
            this.bounds = bounds;
            this.resolutionX = resolutionX;
            this.resolutionY = resolutionY;
            this.resolutionZ = resolutionZ;
            this.density = density;
            this.emission = emission;
            maxDensity = 1;
            if (density != null)
            {
                maxDensity = 0;
                for (int i = 0; i < density.Length; ++i)
                {
                    maxDensity = Math.Max(maxDensity, density[i]);
                }
            }
            volumeIntegrator = inner.volumeIntegrator;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ GridModulatedMedium inner: %s ]", inner);
        }

        public override bool IsEmissive() { return inner.IsEmissive(); }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            MediumProperties mp = inner.SamplePoint(p, lambda);
            Float d = density == null ? 1 : Interpolate(density, p);
            Float e = emission == null ? 1 : Interpolate(emission, p);
            mp.sigma_a = mp.sigma_a * d;
            mp.sigma_s = mp.sigma_s * d;
            mp.Le = mp.Le * e;
            return mp;
        }

        // The largest factor of the extinction coefficients: a majorant of the medium is the inner one times this
        public Float MaxDensity() { return maxDensity; }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            MediumProperties mp = inner.SamplePoint(ray.o, lambda);
            return buf.Alloc(new HomogeneousMajorantIterator(0, tMax, (mp.sigma_a + mp.sigma_s) * maxDensity));
        }

        Float Interpolate(Float[] values, Point3f p)
        {
            Vector3f o = bounds.Offset(p);
            if (o.x < 0 || o.x > 1 || o.y < 0 || o.y > 1 || o.z < 0 || o.z > 1)
            {
                return 0;
            }
            Float fx = o.x * (resolutionX - 1), fy = o.y * (resolutionY - 1), fz = o.z * (resolutionZ - 1);
            int ix = Math.Min((int)fx, resolutionX - 2), iy = Math.Min((int)fy, resolutionY - 2), iz = Math.Min((int)fz, resolutionZ - 2);
            Float tx = fx - ix, ty = fy - iy, tz = fz - iz;
            Float Value(int x, int y, int z) { return values[(z * resolutionY + y) * resolutionX + x]; }
            Float x00 = pbrt.Lerp(tx, Value(ix, iy, iz), Value(ix + 1, iy, iz));
            Float x10 = pbrt.Lerp(tx, Value(ix, iy + 1, iz), Value(ix + 1, iy + 1, iz));
            Float x01 = pbrt.Lerp(tx, Value(ix, iy, iz + 1), Value(ix + 1, iy, iz + 1));
            Float x11 = pbrt.Lerp(tx, Value(ix, iy + 1, iz + 1), Value(ix + 1, iy + 1, iz + 1));
            return pbrt.Lerp(tz, pbrt.Lerp(ty, x00, x10), pbrt.Lerp(ty, x01, x11));
        }

        private Medium inner;
        private Bounds3f bounds;
        private int resolutionX, resolutionY, resolutionZ;
        private Float[] density, emission;
        private Float maxDensity;
    }


    // (not in pbrt-v4) RefractiveEdge Definition (stage 6): an edge of a subpath, from a vertex along startDirection to the vertex it
    // reaches (arriving along endDirection) with the area Jacobian dA_perp(end) / dOmega(start) of the curve, and the one of the
    // reverse edge, which follows from the conservation of n^2 dA dOmega
    public struct RefractiveEdge
    {
        public Vector3f startDirection, endDirection;
        public Float length, distance;
        public Float areaJacobian, areaJacobianReverse;
    }


    // (not in pbrt-v4) EdgeGeometry Definition (stage 6): how an edge is traversed from one vertex to the next when it is curved: the
    // direction it leaves along, the direction it arrives along and the area Jacobian dA_perp(arrival) / dOmega(leaving). An edge
    // that is not curved (curved false) is the straight line between the two vertices.
    public struct EdgeGeometry
    {
        public bool curved;
        public Vector3f leaving, arriving;
        public Float jacobian;

        // The edge in the direction it was walked in
        public static EdgeGeometry Forward(RefractiveEdge edge)
        {
            EdgeGeometry g = new EdgeGeometry();
            g.curved = true;
            g.leaving = edge.startDirection;
            g.arriving = edge.endDirection;
            g.jacobian = edge.areaJacobian;
            return g;
        }

        // The edge traversed the other way
        public static EdgeGeometry Backward(RefractiveEdge edge)
        {
            EdgeGeometry g = new EdgeGeometry();
            g.curved = true;
            g.leaving = (-1) * edge.endDirection;
            g.arriving = (-1) * edge.startDirection;
            g.jacobian = edge.areaJacobianReverse;
            return g;
        }
    }


    // (not in pbrt-v4) The transverse vector that the polarisation of light follows along a curved path (Rytov's law): in an
    // isotropic medium whose index varies smoothly the electric field is carried along the ray without turning about it, so the
    // vector is rotated, at each step, by the rotation that takes the old tangent to the new one about their common normal. The
    // Stokes frames at the two ends of an edge differ by the angle between the frame there and this vector.
    public sealed class PolarisationTransport
    {
        // Whether an edge is being followed, its direction and vector at the vertex it left, and the direction and vector now
        public bool active;
        public Vector3f startDirection, startVector;
        public Vector3f tangent, vector;
        // The state at the last step of the grid (the collisions are partway through a step)
        Vector3f baseTangent, baseVector;
        // For tests: the tangent and the vector after each full step, when these are not null
        public System.Collections.Generic.List<Vector3f> tangentHistory, vectorHistory;

        // The edge leaves a vertex along direction: any vector perpendicular to it is the reference
        public void Begin(Vector3f direction)
        {
            PolarisationFrame frame = default;
            frame.SetFromZ(pbrt.Normalize(direction));
            active = true;
            startDirection = frame.z;
            startVector = frame.x;
            tangent = baseTangent = frame.z;
            vector = baseVector = frame.x;
        }

        // The same with the given vector (made perpendicular to the direction)
        public void Begin(Vector3f direction, Vector3f reference)
        {
            Vector3f d = pbrt.Normalize(direction);
            Vector3f x = pbrt.Normalize(reference - d * pbrt.Dot(reference, d));
            active = true;
            startDirection = d;
            startVector = x;
            tangent = baseTangent = d;
            vector = baseVector = x;
        }

        public void End()
        {
            active = false;
        }

        // A full step of the path ended with this tangent
        public void Advance(Vector3f newTangent)
        {
            baseVector = Rotate(baseVector, baseTangent, newTangent);
            baseTangent = newTangent;
            tangent = newTangent;
            vector = baseVector;
            if (tangentHistory != null)
            {
                tangentHistory.Add(newTangent);
                vectorHistory.Add(baseVector);
            }
        }

        // The path is partway through the step, with this tangent
        public void Report(Vector3f newTangent)
        {
            vector = Rotate(baseVector, baseTangent, newTangent);
            tangent = newTangent;
        }

        // The rotation about the normal of the two tangents that takes the old to the new, applied to e
        public static Vector3f Rotate(Vector3f e, Vector3f oldTangent, Vector3f newTangent)
        {
            Vector3f axis = pbrt.Cross(oldTangent, newTangent);
            Float sin = pbrt.Length(axis);
            Float cos = pbrt.Dot(oldTangent, newTangent);
            if (sin < 1e-12f)
                return e;
            axis = axis / sin;
            Vector3f rotated = e * cos + pbrt.Cross(axis, e) * sin + axis * (pbrt.Dot(axis, e) * (1 - cos));
            return pbrt.Normalize(rotated);
        }
    }

    // RefractiveMedium Definition
    public sealed unsafe class RefractiveMedium : Medium
    {
        // RefractiveMedium Public Methods

        // majorant gives sigma_maj for the wavelengths: it has to bound sigma_a + sigma_s of the inner medium everywhere in
        // the bounds. stepSize is the arc length of one integrator step. The path is discretised on a grid of steps from its
        // start, independently of where the collisions fall.
        public RefractiveMedium(Medium inner, RefractiveIndexField field, Bounds3f bounds,
                                Func<SampledWavelengths, SampledSpectrum> majorant, Float stepSize)
            : this(inner, field, bounds, majorant, stepSize, null)
        {
        }

        // contains tells whether a point is in the region of the medium, which has to be what the boundary shape of the scene
        // encloses (bounds is its bounding box); without it the region is the box
        public RefractiveMedium(Medium inner, RefractiveIndexField field, Bounds3f bounds,
                                Func<SampledWavelengths, SampledSpectrum> majorant, Float stepSize,
                                Func<Point3f, bool> contains)
        {
            this.inner = inner;
            this.field = field;
            this.bounds = bounds;
            this.majorant = majorant;
            this.stepSize = stepSize;
            this.contains = contains ?? InBox;
            MediumTracking.hasRefractive = true;
            this.boundedField = new BoundedRefractiveIndexField(field, this.contains, 1.0f);
            volumeIntegrator = inner.volumeIntegrator;
            WarnIfIndexIsNotOneOnBoundary();
        }

        // The medium does not model a refraction at its boundary (no Snell's law, no Fresnel), so the index of the field has to be 1 there:
        // a step in n makes the curved paths of the walks and of the connections disagree about the geometry across the step. Looks for
        // points of the boundary from random points inside the region.
        void WarnIfIndexIsNotOneOnBoundary()
        {
            RNG rng = new RNG(1, 2);
            Vector3f extent = bounds.pMax - bounds.pMin;
            Float diagonal = pbrt.Length(extent);
            Float worst = 0;
            int found = 0;
            for (int attempt = 0; attempt < 4000 && found < 200; ++attempt)
            {
                Point3f p = new Point3f(bounds.pMin.x + extent.x * rng.Uniform<Float>(), bounds.pMin.y + extent.y * rng.Uniform<Float>(),
                                        bounds.pMin.z + extent.z * rng.Uniform<Float>());
                if (!contains(p))
                {
                    continue;
                }
                Vector3f direction = pbrt.SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Point3f inside = p;
                for (int step = 1; step <= 64; ++step)
                {
                    Point3f candidate = p + (step * diagonal / 64) * direction;
                    if (contains(candidate))
                    {
                        inside = candidate;
                        continue;
                    }
                    Point3f outside = candidate;
                    for (int bisection = 0; bisection < 24; ++bisection)
                    {
                        Point3f middle = new Point3f(0.5f * (inside.x + outside.x), 0.5f * (inside.y + outside.y), 0.5f * (inside.z + outside.z));
                        if (contains(middle))
                        {
                            inside = middle;
                        }
                        else
                        {
                            outside = middle;
                        }
                    }
                    field.ValueAndGradient(inside, out Float value, out Vector3f gradient);
                    worst = Math.Max(worst, Math.Abs(value - 1));
                    ++found;
                    break;
                }
            }
            if (worst > 0.005f)
            {
                pbrt.Warning(pbrt.StringPrintf("RefractiveMedium: the index is %f away from 1 on the boundary of the medium, which does not model a refraction " +
                                               "there (Snell's law, Fresnel); make the field 1 on the boundary.", worst));
            }
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

        // The field with the index 1 outside the box: the one a connection to a point outside the medium is solved in
        public RefractiveIndexField BoundedField() { return boundedField; }
        public Bounds3f Bounds() { return bounds; }
        public Float StepSize() { return stepSize; }

        public Float Index(Point3f p)
        {
            field.ValueAndGradient(p, out Float value, out Vector3f gradient);
            return value;
        }

        public bool Contains(Point3f p)
        {
            return contains(p);
        }

        bool InBox(Point3f p)
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
                                                 out Point3f endPoint, out Vector3f endDirection, out Float endLength,
                                                 PolarisationTransport transport = null)
        {
            SampledSpectrum sigma_maj = majorant(lambda);
            SymplecticRay ray = new SymplecticRay(field, p, direction);
            endPoint = p;
            endDirection = ray.Direction();
            endLength = 0;
            // A path that enters at an edge of the region can start a rounding error outside it: a point a little ahead decides
            Float nudge = 1e-4f * pbrt.Length(bounds.pMax - bounds.pMin);
            if (!(Contains(p) || Contains(p + nudge * ray.Direction())) || maxLength <= 0)
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
                    if (transport != null)
                        transport.Report(atCollision.Direction());
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
                    if (transport != null)
                        transport.Report(atEnd.Direction());
                    endPoint = atEnd.p;
                    endDirection = atEnd.Direction();
                    endLength = segmentEnd;
                    return pbrt.FastExp(-(segmentEnd - lastCollision) * sigma_maj);
                }
                ray = next;
                if (transport != null)
                    transport.Advance(next.Direction());
                s = gridEnd;
            }
        }

        // The edge of a walk that left start along direction and reached end (the curve is traced again from start, on the field that
        // is 1 outside the region, until its closest approach to end: its distance from end is the error of the two traces)
        public bool TraceEdge(Point3f start, Vector3f direction, Point3f end, out RefractiveEdge edge)
        {
            edge = new RefractiveEdge();
            RefractiveConnectionOptions options = ConnectionOptions(pbrt.Length(end - start));
            options.inside = null;
            Vector3f w = pbrt.Normalize(direction);
            if (!RefractiveConnection.TraceToClosestApproach(boundedField, start, w, end, options,
                                                             out SymplecticRayJacobian atClosest, out Float length))
            {
                return false;
            }
            RefractiveConnection.ErrorAndDerivative(atClosest, end, out Vector3f error, out SquareMatrix3 derivative);
            edge.startDirection = w;
            edge.endDirection = pbrt.Normalize(atClosest.v);
            edge.length = length;
            edge.distance = pbrt.Length(error);
            edge.areaJacobian = RefractiveConnection.AreaJacobian(derivative, w, edge.endDirection);
            boundedField.ValueAndGradient(start, out Float startIndex, out Vector3f startGradient);
            boundedField.ValueAndGradient(end, out Float endIndex, out Vector3f endGradient);
            edge.areaJacobianReverse = (endIndex / startIndex) * (endIndex / startIndex) * edge.areaJacobian;
            return true;
        }

        // The edge between a camera side vertex and a light side vertex as a function of the two vertices alone: the curve that the
        // solver finds from the camera side vertex, starting from a guess of the direction, the Jacobian dA_perp(light side) /
        // dOmega(camera side) and, by the conservation of n^2 dA dOmega, the one of the reverse. Every strategy of bdpt that has the
        // edge in its path uses these values, whichever subpath walked it, which the weights of the strategies need.
        public bool SolveEdge(Point3f cameraSide, Point3f lightSide, Vector3f directionGuess, out RefractiveEdge edge)
        {
            edge = new RefractiveEdge();
            RefractiveConnectionOptions options = ConnectionOptions(pbrt.Length(lightSide - cameraSide));
            options.inside = null;
            if (!RefractiveConnection.Solve(boundedField, cameraSide, lightSide, directionGuess, options, out RefractiveConnectionPath path))
            {
                return false;
            }
            edge.startDirection = path.startDirection;
            edge.endDirection = path.endDirection;
            edge.length = path.length;
            edge.distance = path.distance;
            edge.areaJacobian = path.areaJacobian;
            boundedField.ValueAndGradient(cameraSide, out Float cameraIndex, out Vector3f cameraGradient);
            boundedField.ValueAndGradient(lightSide, out Float lightIndex, out Vector3f lightGradient);
            edge.areaJacobianReverse = (lightIndex / cameraIndex) * (lightIndex / cameraIndex) * path.areaJacobian;
            return true;
        }

        // The options of a connection over the given distance: the tolerance and the longest path scale with the medium
        public RefractiveConnectionOptions ConnectionOptions(Float distance)
        {
            Float size = pbrt.Length(bounds.pMax - bounds.pMin);
            RefractiveConnectionOptions options = new RefractiveConnectionOptions();
            options.stepSize = stepSize;
            options.tolerance = 5e-4f * size;
            options.maxLength = 4 * (size + distance);
            options.maxTrials = 200;
            return options;
        }

        // A path from p to a target anywhere (inside the medium, or outside it where the index is 1), by one search of algorithm 3
        // from a random direction: the count is that of the algorithm, whose expectation over finding a path is one per path.
        public bool ConnectToPoint(Point3f start, Point3f target, RefractiveConnectionOptions options, ref RNG rng,
                                   out RefractiveConnectionPath path, out int count)
        {
            RefractiveConnectionOptions local = options.Clone();
            local.inside = null;
            return RefractiveConnection.SolveMultiple(boundedField, start, target, local, ref rng, out path, out count);
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

        // "refractive": the coefficients of a homogeneous medium ("sigma_a", "sigma_s", "scale", "g", "preset"), a "string field"
        // that is "luneburg" ("point3 center", "float radius", "float exponent") or "grid" ("integer nx", "ny", "nz", "float
        // index", "point3 p0", "p1"), "float stepsize", and optionally grids "float density" and "float emission" (nx * ny * nz
        // values over the bounds, scaling the extinction and the "Le"). The region of the medium is the sphere or the box, and has to be what
        // the boundary shape of the scene encloses. The index must be 1 on the boundary of the region. renderFromMedium may
        // translate and scale uniformly (a rotation is not supported).
        public static RefractiveMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc,
                                              Allocator alloc)
        {
            Medium homogeneous = HomogeneousMedium.Create(parameters, loc, alloc);
            Medium inner = homogeneous;
            Float maxDensity = 1;
            Point3f origin = renderFromMedium.Apply(new Point3f(0, 0, 0));
            Vector3f ex = renderFromMedium.Apply(new Point3f(1, 0, 0)) - origin;
            Vector3f ey = renderFromMedium.Apply(new Point3f(0, 1, 0)) - origin;
            Vector3f ez = renderFromMedium.Apply(new Point3f(0, 0, 1)) - origin;
            Float scale = pbrt.Length(ex);
            if (Math.Abs(pbrt.Length(ey) - scale) > 1e-4f * scale || Math.Abs(pbrt.Length(ez) - scale) > 1e-4f * scale ||
                Math.Abs(pbrt.Dot(ex, ey)) > 1e-4f * scale * scale || Math.Abs(pbrt.Dot(ex, ez)) > 1e-4f * scale * scale ||
                Math.Abs(ex.y) > 1e-4f * scale || Math.Abs(ex.z) > 1e-4f * scale || Math.Abs(ey.x) > 1e-4f * scale ||
                Math.Abs(ey.z) > 1e-4f * scale || Math.Abs(ez.x) > 1e-4f * scale || Math.Abs(ez.y) > 1e-4f * scale)
            {
                pbrt.ErrorExit(loc, "A refractive medium supports a translation and a uniform scale as its transformation.");
            }
            Float stepSize = parameters.GetOneFloat("stepsize", 0.05f) * scale;
            string fieldName = parameters.GetOneString("field", "luneburg");
            RefractiveIndexField field;
            Bounds3f bounds;
            Func<Point3f, bool> contains = null;
            if (fieldName == "luneburg")
            {
                Point3f center = renderFromMedium.Apply(parameters.GetOnePoint3f("center", new Point3f(0, 0, 0)));
                Float radius = parameters.GetOneFloat("radius", 1.0f) * scale;
                field = new LuneburgRefractiveIndexField(center, radius, parameters.GetOneFloat("exponent", 2.0f));
                bounds = new Bounds3f(center - new Vector3f(radius, radius, radius), center + new Vector3f(radius, radius, radius));
                Float radius2 = radius * radius;
                contains = (Point3f q) => pbrt.LengthSquared(q - center) <= radius2;
            }
            else if (fieldName == "grid")
            {
                int nx = parameters.GetOneInt("nx", 1), ny = parameters.GetOneInt("ny", 1), nz = parameters.GetOneInt("nz", 1);
                List<Float> index = parameters.GetFloatArray("index");
                if (index.Count != nx * ny * nz)
                    pbrt.ErrorExit(loc, "A refractive medium needs nx * ny * nz values of \"index\".");
                Point3f p0 = renderFromMedium.Apply(parameters.GetOnePoint3f("p0", new Point3f(0, 0, 0)));
                Point3f p1 = renderFromMedium.Apply(parameters.GetOnePoint3f("p1", new Point3f(1, 1, 1)));
                bounds = new Bounds3f(p0, p1);
                field = new GridRefractiveIndexField(bounds, nx, ny, nz, index.ToArray());
            }
            else
            {
                pbrt.ErrorExit(loc, "The refractive index field \"" + fieldName + "\" is unknown.");
                return null;
            }
            // Optional grids over the bounds ("integer nx", "ny", "nz" values, x fastest) scale the extinction ("float density")
            // and the emission ("float emission", the "Le" of the homogeneous medium is its scale)
            List<Float> densityValues = parameters.GetFloatArray("density");
            List<Float> emissionValues = parameters.GetFloatArray("emission");
            if (densityValues.Count > 0 || emissionValues.Count > 0)
            {
                GridModulatedMedium modulated = new GridModulatedMedium(homogeneous, bounds, parameters.GetOneInt("nx", 1),
                                                                        parameters.GetOneInt("ny", 1), parameters.GetOneInt("nz", 1),
                                                                        densityValues.Count > 0 ? densityValues.ToArray() : null,
                                                                        emissionValues.Count > 0 ? emissionValues.ToArray() : null);
                inner = modulated;
                maxDensity = modulated.MaxDensity();
            }
            // The majorant is the extinction of the homogeneous medium for the wavelengths asked for, times the largest density
            return new RefractiveMedium(inner, field, bounds,
                                        (SampledWavelengths l) =>
                                        {
                                            MediumProperties mp = homogeneous.SamplePoint(origin, l);
                                            return (mp.sigma_a + mp.sigma_s) * maxDensity;
                                        }, stepSize, contains);
        }

        // RefractiveMedium Private Members
        private Medium inner;
        private RefractiveIndexField field;
        private Bounds3f bounds;
        private Func<SampledWavelengths, SampledSpectrum> majorant;
        private Float stepSize;
        private Func<Point3f, bool> contains;
        private RefractiveIndexField boundedField;
    }

}  // namespace pbrt
