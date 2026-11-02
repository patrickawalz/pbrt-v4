// (not in pbrt-v4) refractiveray.cs
//
// Light paths through a refractive index field (refractiveindex.cs): the non-linear ray tracing of Pediredla et al., "Path
// Tracing Estimators for Refractive Radiative Transfer", section 3.1 and 4. A light path obeys the eikonal equation in the form
// of Hamilton's equations,
//
//     dx/ds = v / n(x)        dv/ds = grad n(x)
//
// with s the arc length and v the optical velocity, whose length is n(x) along the path (the equations conserve that, and
// |v| - n is the error of the integration). SymplecticRay is the symplectic integrator of the paper (algorithm 1): a half
// kick of v, a drift of x and a half kick, which does not drift in energy over long paths the way an explicit Runge-Kutta
// step does. (The drift here is along v / |v|, the Hamiltonian |v| - n being separable, which makes the step second order and
// reversible; dividing by n(x) as in dx/ds = v / n makes it first order, and not reversible.) SymplecticRayJacobian carries the derivatives of the position and the optical velocity with respect to the
// initial direction along the path (algorithm 2), which the gradient based search for the path between two points needs.

using System;

namespace pbrt
{

    // SymplecticRay Definition
    public struct SymplecticRay
    {
        // SymplecticRay Public Methods

        // A ray at p along direction, whose optical velocity has the length of the index there
        public SymplecticRay(RefractiveIndexField field, Point3f p, Vector3f direction)
        {
            // The index and its gradient are those a little ahead along the direction: a path that starts on the boundary of a medium
            // (rounding puts it on either side) has the gradient of the medium for its first half kick
            field.ValueAndGradient(p, out Float indexAtStart, out Vector3f unused);
            field.ValueAndGradient(p + StartNudge * pbrt.Normalize(direction), out Float indexAhead, out Vector3f gradientAtStart);
            this.p = p;
            this.v = indexAtStart * pbrt.Normalize(direction);
            this.n = indexAtStart;
            this.gradient = gradientAtStart;
        }

        // The direction the ray travels in
        public Vector3f Direction()
        {
            return pbrt.Normalize(v);
        }

        // One step of arc length s. The kick that ends a step and the one that starts the next are at the same point, so the
        // field is evaluated once per step.
        public void Step(RefractiveIndexField field, Float s)
        {
            Float half = 0.5f * s;
            v += half * gradient;
            p += s / pbrt.Length(v) * v;
            field.ValueAndGradient(p, out n, out gradient);
            v += half * gradient;
        }

        // The ray after the arc length distance: whole steps of at most maxStepSize and a last step for the rest
        public void Advance(RefractiveIndexField field, Float distance, Float maxStepSize)
        {
            int steps = (int)(distance / maxStepSize);
            for (int i = 0; i < steps; ++i)
            {
                Step(field, maxStepSize);
            }
            Float rest = distance - steps * maxStepSize;
            if (rest > 0)
            {
                Step(field, rest);
            }
        }

        // How far ahead of its start the field is evaluated for the first kick of a path
        public const Float StartNudge = 1e-5f;

        // SymplecticRay Public Members
        public Point3f p;
        public Vector3f v;

        // SymplecticRay Private Members: the index and its gradient at p
        Float n;
        Vector3f gradient;
    }

    // SymplecticRayJacobian Definition: a SymplecticRay with the derivatives of p and v with respect to the three numbers w that
    // set the initial direction, which is w / |w| (so the derivatives along w are zero, and the optimiser has three free values)
    public struct SymplecticRayJacobian
    {
        // SymplecticRayJacobian Public Methods
        public SymplecticRayJacobian(RefractiveIndexField field, Point3f p, Vector3f w)
        {
            Float length = pbrt.Length(w);
            field.ValueAndGradient(p, out Float indexAtStart, out Vector3f unused);
            field.ValueGradientAndHessian(p + SymplecticRay.StartNudge / length * w, out Float indexAhead, out Vector3f gradientAtStart,
                                          out SquareMatrix3 hessianAtStart);
            this.p = p;
            this.v = indexAtStart / length * w;
            this.dpdw = SquareMatrix3.Zero();
            // v = n w / |w|: dv/dw = n (|w|^2 I - w w^T) / |w|^3
            this.dvdw = (indexAtStart / (length * length * length)) *
                        (length * length * new SquareMatrix3() + (Float)(-1) * RefractiveIndexField.OuterProduct(w, w));
            this.n = indexAtStart;
            this.gradient = gradientAtStart;
            this.hessian = hessianAtStart;
        }

        // One step of arc length s, with the exact derivative of the step: the derivative of the integrator, which equals a finite
        // difference of it
        public void Step(RefractiveIndexField field, Float s)
        {
            Float half = 0.5f * s;
            Vector3f vHalf = v + half * gradient;
            SquareMatrix3 dvdwHalf = dvdw + half * (hessian * dpdw);
            // The drift is along v / |v|: the Hamiltonian |v| - n of the paths is separable, and the step is second order and
            // reversible
            Float length = pbrt.Length(vHalf);
            Vector3f u = (1 / length) * vHalf;
            Point3f pNew = p + s * u;
            SquareMatrix3 projected = dvdwHalf + (Float)(-1) * (RefractiveIndexField.OuterProduct(u, u) * dvdwHalf);
            SquareMatrix3 dpdwNew = dpdw + (s / length) * projected;
            field.ValueGradientAndHessian(pNew, out n, out gradient, out hessian);
            v = vHalf + half * gradient;
            dvdw = dvdwHalf + half * (hessian * dpdwNew);
            p = pNew;
            dpdw = dpdwNew;
        }

        // The index and its gradient at p
        public Float Index() { return n; }
        public Vector3f IndexGradient() { return gradient; }

        // SymplecticRayJacobian Public Members
        public Point3f p;
        public Vector3f v;
        public SquareMatrix3 dpdw;
        public SquareMatrix3 dvdw;

        // SymplecticRayJacobian Private Members: the index, its gradient and its Hessian at p
        Float n;
        Vector3f gradient;
        SquareMatrix3 hessian;
    }

}  // namespace pbrt
