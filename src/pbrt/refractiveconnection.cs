// (not in pbrt-v4) refractiveconnection.cs
//
// The path between two points in a refractive index field: the boundary value problem of Pediredla et al., "Path Tracing
// Estimators for Refractive Radiative Transfer", section 5 (direct connections). A path leaves the start in the direction
// w / |w| and is traced until its closest approach to the target y, the arc length s* where (x(s) - y) . v(s) = 0. The error
// x(s*) - y is zero for a path that reaches y, and BFGS on w drives it to zero (the optimiser the paper found best). A
// connection may have several solutions, and a search from a random direction finds one of them; algorithm 3 turns the number
// of searches until the same solution is found again into an unbiased estimate of one over the density of that solution.

using System;

namespace pbrt
{

    // RefractiveConnectionOptions Definition
    public class RefractiveConnectionOptions
    {
        // Arc length of an integrator step (the paper's default is 1 mm)
        public Float stepSize = 0.05f;
        // A path longer than this has no closest approach
        public Float maxLength = 100.0f;
        // The distance of the closest approach at which a path counts as reaching the target
        public Float tolerance = 1e-3f;
        public int maxIterations = 20;
        // The most searches of algorithm 3 before the estimate is cut off
        public int maxTrials = 1000;
        // If set, a path that leaves the region where this is true is a failure (the medium bounds)
        public Func<Point3f, bool> inside = null;

        public RefractiveConnectionOptions Clone()
        {
            return (RefractiveConnectionOptions)MemberwiseClone();
        }
    }

    // RefractiveConnectionPath Definition
    public struct RefractiveConnectionPath
    {
        public Vector3f startDirection;
        public Vector3f endDirection;
        // Arc length of the path
        public Float length;
        // Distance of the end of the path from the target
        public Float distance;
        // The area on the plane perpendicular to the end direction that a unit of solid angle of start directions covers at the
        // end: dA / d(omega) (s^2 for a straight path). It converts a density on the target into one on the start direction.
        public Float areaJacobian;
        // The derivative of the end position with respect to w at the solution, and |w| there: how a change of the start direction
        // moves the end (SolveMultiple compares solutions with it)
        public SquareMatrix3 derivative;
        public Float directionScale;
        // Set by SolveMultiple when the number of searches reached maxTrials (the count is then a lower bound)
        public bool truncated;
    }

    // RefractiveConnection Definition
    public static class RefractiveConnection
    {
        // Traces from start along w until the closest approach to target, the first place where (x - y) . v goes from negative to
        // positive, refined by a bracketed secant search on a partial step. Fails if the path leaves the region, is longer than
        // the maximum, or never approaches the target.
        public static bool TraceToClosestApproach(RefractiveIndexField field, Point3f start, Vector3f w, Point3f target,
                                                  RefractiveConnectionOptions options,
                                                  out SymplecticRayJacobian atClosest, out Float length)
        {
            SymplecticRayJacobian state = new SymplecticRayJacobian(field, start, w);
            atClosest = state;
            length = 0;
            Float h = options.stepSize;
            Float f = pbrt.Dot(state.p - target, state.v);
            bool armed = f < 0;
            Float travelled = 0;
            while (travelled < options.maxLength)
            {
                SymplecticRayJacobian previous = state;
                Float fPrevious = f;
                state.Step(field, h);
                travelled += h;
                f = pbrt.Dot(state.p - target, state.v);
                // A step that ends at the closest approach may overshoot a target on the boundary: it is the refined point
                // that has to be inside then
                bool closing = armed && f >= 0;
                if (!closing && options.inside != null && !options.inside(state.p))
                {
                    return false;
                }
                if (float.IsNaN((float)f))
                {
                    return false;
                }
                if (armed && f >= 0)
                {
                    // Bracket [0, h] of a partial step from previous
                    Float lo = 0, hi = h, fLo = fPrevious, fHi = f;
                    SymplecticRayJacobian best = state;
                    Float bestF = f, bestT = h;
                    for (int i = 0; i < 20 && Math.Abs(bestF) > 1e-6f && hi - lo > 1e-6f * h; ++i)
                    {
                        Float t = lo + (hi - lo) * (-fLo) / (fHi - fLo);
                        Float margin = 0.1f * (hi - lo);
                        t = Math.Min(Math.Max(t, lo + margin), hi - margin);
                        SymplecticRayJacobian trial = previous;
                        trial.Step(field, t);
                        Float fTrial = pbrt.Dot(trial.p - target, trial.v);
                        if (fTrial < 0) { lo = t; fLo = fTrial; } else { hi = t; fHi = fTrial; }
                        if (Math.Abs(fTrial) < Math.Abs(bestF)) { best = trial; bestF = fTrial; bestT = t; }
                    }
                    atClosest = best;
                    length = travelled - h + bestT;
                    return options.inside == null || options.inside(best.p);
                }
                if (!armed && f < 0)
                {
                    armed = true;
                }
            }
            return false;
        }

        // The error x(s*) - y of a path at its closest approach and the derivative of that error with respect to w, including
        // the change of s* with w: ds*/dw = -(Jp^T v + Jv^T (x - y)) / (v.v / n + (x - y) . grad n), from (x - y) . v = 0
        public static void ErrorAndDerivative(SymplecticRayJacobian atClosest, Point3f target, out Vector3f error,
                                              out SquareMatrix3 derivative)
        {
            error = atClosest.p - target;
            Float n = atClosest.Index();
            Vector3f numerator = pbrt.Transpose(atClosest.dpdw) * atClosest.v +
                                 pbrt.Transpose(atClosest.dvdw) * error;
            Float denominator = pbrt.Dot(atClosest.v, atClosest.v) / n + pbrt.Dot(error, atClosest.IndexGradient());
            Vector3f dsdw = (-1 / denominator) * numerator;
            derivative = atClosest.dpdw + RefractiveIndexField.OuterProduct((1 / n) * atClosest.v, dsdw);
        }

        // Evaluates the loss 1/2 |e|^2 and its gradient at w; false if the trace fails
        static bool Evaluate(RefractiveIndexField field, Point3f start, Point3f target, Vector3f w,
                             RefractiveConnectionOptions options, out Float loss, out Vector3f gradient, out Vector3f error,
                             out SymplecticRayJacobian end, out Float length, out SquareMatrix3 derivative)
        {
            derivative = SquareMatrix3.Zero();
            loss = Float.PositiveInfinity;
            gradient = new Vector3f(0, 0, 0);
            error = new Vector3f(0, 0, 0);
            if (!TraceToClosestApproach(field, start, w, target, options, out end, out length))
            {
                return false;
            }
            ErrorAndDerivative(end, target, out error, out derivative);
            loss = 0.5f * pbrt.Dot(error, error);
            gradient = pbrt.Transpose(derivative) * error;
            return !float.IsNaN((float)loss);
        }

        // BFGS on w (the paper's choice of optimiser) from the initial direction; true if a path reaches the target within the
        // tolerance
        public static bool Solve(RefractiveIndexField field, Point3f start, Point3f target, Vector3f initialDirection,
                                 RefractiveConnectionOptions options, out RefractiveConnectionPath path)
        {
            path = new RefractiveConnectionPath();
            Vector3f w = pbrt.Normalize(initialDirection);
            if (!Evaluate(field, start, target, w, options, out Float loss, out Vector3f gradient, out Vector3f error,
                          out SymplecticRayJacobian end, out Float length, out SquareMatrix3 derivative))
            {
                return false;
            }
            SquareMatrix3 inverseHessian = new SquareMatrix3();
            bool first = true;
            for (int iteration = 0; ; ++iteration)
            {
                if (pbrt.Length(error) <= options.tolerance)
                {
                    path.startDirection = pbrt.Normalize(w);
                    path.endDirection = pbrt.Normalize(end.v);
                    path.length = length;
                    path.distance = pbrt.Length(error);
                    path.areaJacobian = AreaJacobian(derivative, w, path.endDirection);
                    path.derivative = derivative;
                    path.directionScale = pbrt.Length(w);
                    return true;
                }
                if (iteration >= options.maxIterations)
                {
                    return false;
                }
                Vector3f direction = (-1) * (inverseHessian * gradient);
                Float slope = pbrt.Dot(gradient, direction);
                if (!(slope < 0))
                {
                    return false;
                }
                Float alpha = first ? Math.Min(1.0f, 0.1f / pbrt.Length(gradient)) : 1.0f;
                bool accepted = false;
                Vector3f wNew = w, gradientNew = gradient, errorNew = error;
                Float lossNew = loss;
                SymplecticRayJacobian endNew = end;
                Float lengthNew = length;
                SquareMatrix3 derivativeNew = derivative;
                for (int halvings = 0; halvings < 25; ++halvings, alpha *= 0.5f)
                {
                    Vector3f candidate = w + alpha * direction;
                    if (pbrt.Length(candidate) < 1e-6f)
                    {
                        continue;
                    }
                    // A failed trace is an infinite loss
                    if (Evaluate(field, start, target, candidate, options, out lossNew, out gradientNew, out errorNew,
                                 out endNew, out lengthNew, out derivativeNew) && lossNew <= loss + 1e-4f * alpha * slope)
                    {
                        wNew = candidate;
                        accepted = true;
                        break;
                    }
                }
                if (!accepted)
                {
                    return false;
                }
                Vector3f s = wNew - w;
                Vector3f y = gradientNew - gradient;
                Float ys = pbrt.Dot(y, s);
                if (ys > 1e-12f * pbrt.Length(s) * pbrt.Length(y) && ys > 0)
                {
                    if (first)
                    {
                        inverseHessian = (ys / pbrt.Dot(y, y)) * new SquareMatrix3();
                    }
                    Float rho = 1 / ys;
                    SquareMatrix3 a = new SquareMatrix3() + (-rho) * RefractiveIndexField.OuterProduct(s, y);
                    inverseHessian = a * inverseHessian * pbrt.Transpose(a) + rho * RefractiveIndexField.OuterProduct(s, s);
                }
                first = false;
                w = wNew;
                gradient = gradientNew;
                error = errorNew;
                loss = lossNew;
                end = endNew;
                length = lengthNew;
                derivative = derivativeNew;
            }
        }

        // dA / d(omega) from the derivative of the end position with respect to w: the two directions perpendicular to w, as
        // angles (|w| times the derivative), span a parallelogram at the end whose area on the plane perpendicular to the end
        // direction is the Jacobian
        public static Float AreaJacobian(SquareMatrix3 derivative, Vector3f w, Vector3f endDirection)
        {
            Float r = pbrt.Length(w);
            Vector3f u = (1 / r) * w;
            Vector3f axis = Math.Abs(u.x) < 0.6f ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0);
            Vector3f e1 = pbrt.Normalize(pbrt.Cross(u, axis));
            Vector3f e2 = pbrt.Cross(u, e1);
            Vector3f a = r * (derivative * e1);
            Vector3f b = r * (derivative * e2);
            return Math.Abs(pbrt.Dot(pbrt.Cross(a, b), endDirection));
        }

        // (stage 6) The curve that a walk follows from start along direction for the arc length length, with the area Jacobian dA / d(omega)
        // of the edge it makes: the end position's two directions perpendicular to the end direction against the solid angle of the
        // start direction. The field is the one of the whole edge (the index is 1 and its gradient 0 where the edge runs outside the
        // medium, so that one trace covers straight and curved pieces), and the steps are those of the integrator.
        public static void TraceEdge(RefractiveIndexField field, Point3f start, Vector3f direction, Float length, Float stepSize,
                                     out Point3f end, out Vector3f endDirection, out Float areaJacobian, out Float startIndex,
                                     out Float endIndex)
        {
            Vector3f w = pbrt.Normalize(direction);
            field.ValueAndGradient(start, out startIndex, out Vector3f startGradient);
            SymplecticRayJacobian ray = new SymplecticRayJacobian(field, start, w);
            int steps = (int)(length / stepSize);
            for (int i = 0; i < steps; ++i)
            {
                ray.Step(field, stepSize);
            }
            Float rest = length - steps * stepSize;
            if (rest > 0)
            {
                ray.Step(field, rest);
            }
            end = ray.p;
            endDirection = pbrt.Normalize(ray.v);
            endIndex = ray.Index();
            areaJacobian = AreaJacobian(ray.dpdw, w, endDirection);
        }

        // Algorithm 3 of the paper: searches from uniformly random directions. The first search that fails is a null path
        // (false, count 0). Otherwise the searches go on until one finds the same path again (same start direction within what
        // the tolerance allows); the number of searches, failures included, is the count, whose expected value over the first
        // search succeeding is one, so that count / (the number of paths) is an unbiased estimate of one over the density.
        public static bool SolveMultiple(RefractiveIndexField field, Point3f start, Point3f target,
                                         RefractiveConnectionOptions options, ref RNG rng, out RefractiveConnectionPath path,
                                         out int count)
        {
            path = new RefractiveConnectionPath();
            count = 0;
            for (int trial = 0; trial < options.maxTrials; ++trial)
            {
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                bool ok = Solve(field, start, target, pbrt.SampleUniformSphere(u), options, out RefractiveConnectionPath found);
                if (trial == 0)
                {
                    if (!ok)
                    {
                        return false;
                    }
                    path = found;
                    count = 1;
                    continue;
                }
                // The same path: the change of the start direction moves the end by no more than the two solutions can differ
                // by (each is within the tolerance of the target), to first order
                if (ok && pbrt.Length(path.derivative * (path.directionScale * (found.startDirection - path.startDirection))) <
                              4 * options.tolerance)
                {
                    return true;
                }
                ++count;
            }
            path.truncated = true;
            return true;
        }
    }

}  // namespace pbrt
