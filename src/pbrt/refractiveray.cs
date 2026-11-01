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
// step does. SymplecticRayJacobian carries the derivatives of the position and the optical velocity with respect to the
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
            field.ValueAndGradient(p, out Float indexAtStart, out Vector3f gradientAtStart);
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
            p += s / n * v;
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
            field.ValueGradientAndHessian(p, out Float indexAtStart, out Vector3f gradientAtStart,
                                          out SquareMatrix3 hessianAtStart);
            Float length = pbrt.Length(w);
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

        // One step of arc length s, with the exact derivative of the step. (Algorithm 2 of the paper evaluates n and the
        // gradient at the end of the drift for the derivative of the drift; they are the ones at the start of it here, which
        // makes the derivatives those of the integrator, and equal to a finite difference of it.)
        public void Step(RefractiveIndexField field, Float s)
        {
            Float half = 0.5f * s;
            Vector3f vHalf = v + half * gradient;
            SquareMatrix3 dvdwHalf = dvdw + half * (hessian * dpdw);
            Float inverseIndex = 1 / n;
            Point3f pNew = p + s * inverseIndex * vHalf;
            SquareMatrix3 dpdwNew = dpdw + s * (inverseIndex * dvdwHalf +
                                                (-inverseIndex * inverseIndex) * (RefractiveIndexField.OuterProduct(vHalf, gradient) * dpdw));
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
