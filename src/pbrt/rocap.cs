// (not in pbrt-v4) rocap.cs
//
// Hair strands as swept spheres, ray traced with roving capsules: Reshetov, Hart, "Modeling Hair Strands with Roving Capsules" (SIGGRAPH 2024).
// A segment is a cubic Bezier curve q(u) with a cubic radius r(u). For every ray the interval of u that can hold the surface is tightened with the
// roots of g(u) = (q(u) - o) . p - r(u) (a plane through the ray: where g > 0 the curve is farther than r from the ray), a capsule of a size that depends
// on the curvature and the radius is made at the start of the interval and intersected (cone-sphere, Leipelt 1995), and the start moves by the size of the
// capsule. The hit of the capsule is then moved onto the swept surface ("delinearization": the secant method on e(u) = v . q'(u) - r(u) r'(u), Section 4.4).

using System;

namespace pbrt
{
    public sealed unsafe class RocapSegment
    {
        // point[0..3]: the control points of the curve; radius[0..3]: the control values of the radius
        public RocapSegment(SdfVector[] point, double[] radius, int index, int count)
        {
            this.point = (SdfVector[])point.Clone();
            this.radius = (double[])radius.Clone();
            this.index = index;
            this.count = count;
            double sum = 0;
            for (int i = 0; i < 4; ++i)
            {
                sum += Math.Max(0, radius[i]);
            }
            // The integral of the radius over [0, 1]
            averageRadius = Math.Max(0, (radius[0] + 3 * radius[1] + 3 * radius[2] + radius[3]) / 8);
            if (averageRadius == 0)
            {
                averageRadius = sum / 4;
            }
            SdfVector d0 = 3 * (point[1] - point[0]), d1 = 3 * (point[2] - point[1]), d2 = 3 * (point[3] - point[2]);
            maxSpeed = Math.Max(d0.Length(), Math.Max(d1.Length(), d2.Length()));
            SdfVector a0 = 6 * (point[2] - 2 * point[1] + point[0]), a1 = 6 * (point[3] - 2 * point[2] + point[1]);
            maxAcceleration = Math.Max(a0.Length(), a1.Length());
        }

        public readonly int index, count;

        public SdfVector[] ControlPoints
        {
            get { return point; }
        }

        public double[] Radii
        {
            get { return radius; }
        }

        public SdfVector Position(double u)
        {
            double v = 1 - u;
            return point[0] * (v * v * v) + point[1] * (3 * u * v * v) + point[2] * (3 * u * u * v) + point[3] * (u * u * u);
        }

        public SdfVector Velocity(double u)
        {
            double v = 1 - u;
            return (point[1] - point[0]) * (3 * v * v) + (point[2] - point[1]) * (6 * u * v) + (point[3] - point[2]) * (3 * u * u);
        }

        public double Radius(double u)
        {
            double v = 1 - u;
            return radius[0] * (v * v * v) + radius[1] * (3 * u * v * v) + radius[2] * (3 * u * u * v) + radius[3] * (u * u * u);
        }

        public double RadiusDerivative(double u)
        {
            double v = 1 - u;
            return (radius[1] - radius[0]) * (3 * v * v) + (radius[2] - radius[1]) * (6 * u * v) + (radius[3] - radius[2]) * (3 * u * u);
        }

        // The axis aligned box of the strand: the cubic extrema of q . axis +- r (Section 4.5), with the spheres at the ends
        public void Bounds(out SdfVector low, out SdfVector high)
        {
            double* lo = stackalloc double[3];
            double* hi = stackalloc double[3];
            for (int axis = 0; axis < 3; ++axis)
            {
                lo[axis] = double.MaxValue;
                hi[axis] = -double.MaxValue;
            }
            for (int axis = 0; axis < 3; ++axis)
            {
                double c0 = Component(point[0], axis), c1 = Component(point[1], axis), c2 = Component(point[2], axis), c3 = Component(point[3], axis);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    // f(u) = q . axis + sign r, as Bezier values
                    double f0 = c0 + sign * radius[0], f1 = c1 + sign * radius[1], f2 = c2 + sign * radius[2], f3 = c3 + sign * radius[3];
                    Extend(lo, hi, axis, f0);
                    Extend(lo, hi, axis, f3);
                    // f'(u) / 3 = a u^2 + b u + c
                    double d0 = f1 - f0, d1 = f2 - f1, d2 = f3 - f2;
                    double a = d0 - 2 * d1 + d2, b = 2 * (d1 - d0), c = d0;
                    double* roots = stackalloc double[2];
                    int n = Quadratic(a, b, c, roots);
                    for (int i = 0; i < n; ++i)
                    {
                        if (roots[i] > 0 && roots[i] < 1)
                        {
                            double u = roots[i], w = 1 - u;
                            Extend(lo, hi, axis, f0 * (w * w * w) + f1 * (3 * u * w * w) + f2 * (3 * u * u * w) + f3 * (u * u * u));
                        }
                    }
                }
            }
            low = new SdfVector(lo[0], lo[1], lo[2]);
            high = new SdfVector(hi[0], hi[1], hi[2]);
        }

        // The nearest hit of the ray o + t d (d of unit length) with t in (tMin, tMax): the distance, the parameter of the curve and the unit normal
        public bool Intersect(SdfVector o, SdfVector d, double tMin, double tMax, out double t, out double u, out SdfVector normal)
        {
            t = tMax;
            u = 0;
            normal = new SdfVector(0, 0, 1);
            bool found = false;

            // The ray at the origin: the control points relative to it (more accurate for rays that start far away, Section 4)
            SdfVector q0 = point[0] - o, q1 = point[1] - o, q2 = point[2] - o, q3 = point[3] - o;
            double* gPlus = stackalloc double[4];
            double* gMinus = stackalloc double[4];
            // p: perpendicular to the ray and to the tangent in the middle of the curve (the plane of the ray and that tangent separates the strand best)
            SdfVector tangent = Velocity(0.5);
            SdfVector p = SdfVector.Cross(tangent, d);
            if (p.LengthSquared() < 1e-24 * Math.Max(1, tangent.LengthSquared()))
            {
                p = Perpendicular(d);
            }
            p = p.Normalized();
            double s0 = SdfVector.Dot(q0, p), s1 = SdfVector.Dot(q1, p), s2 = SdfVector.Dot(q2, p), s3 = SdfVector.Dot(q3, p);
            gPlus[0] = s0 - radius[0];
            gPlus[1] = s1 - radius[1];
            gPlus[2] = s2 - radius[2];
            gPlus[3] = s3 - radius[3];
            gMinus[0] = -s0 - radius[0];
            gMinus[1] = -s1 - radius[1];
            gMinus[2] = -s2 - radius[2];
            gMinus[3] = -s3 - radius[3];

            double step = CapsuleStep();
            double us = 0;
            double* roots = stackalloc double[3];
            while (us < 1)
            {
                // Tighten the interval
                double un = Math.Max(Advance(gPlus, us, roots), Advance(gMinus, us, roots));
                if (un >= 1)
                {
                    break;
                }
                us = Math.Max(us, un - 1e-9);
                double ue = Math.Min(1, us + step);
                double ra = Math.Max(0, Radius(us)), rb = Math.Max(0, Radius(ue));
                SdfVector a = Position(us) - o, b = Position(ue) - o;
                if (Capsule(a, b, ra, rb, d, tMin, t, out double capsuleT, out double fraction))
                {
                    // Move the hit onto the swept surface
                    double hitU = us + fraction * (ue - us);
                    double refinedT = capsuleT;
                    SdfVector refinedNormal = default;
                    bool ok = Delinearize(o, d, ref hitU, ref refinedT, out refinedNormal);
                    if (!ok)
                    {
                        // keep the hit of the capsule
                        SdfVector onCurve = Position(hitU) - o;
                        refinedNormal = (d * capsuleT - onCurve);
                        double length = refinedNormal.Length();
                        refinedNormal = length > 0 ? refinedNormal / length : refinedNormal;
                    }
                    if (refinedT > tMin && refinedT < t)
                    {
                        t = refinedT;
                        u = hitU;
                        normal = refinedNormal;
                        found = true;
                    }
                }
                if (ue >= 1)
                {
                    break;
                }
                us = ue;
            }
            return found;
        }

        // The size of the capsules in the parameter (the function capsule_step of the paper, without the footprint of the pixel)
        private double CapsuleStep()
        {
            const double minimum = 1.0 / 50;
            double r = Math.Max(averageRadius, 1e-300);
            // The capsule replaces the arc of the curve, whose sagitta is a fraction of the radius
            double step = maxAcceleration > 0 ? Math.Sqrt(0.4 * r / maxAcceleration) : 1;
            // At most about ten diameters long
            if (maxSpeed > 0)
            {
                step = Math.Min(step, 20 * r / maxSpeed);
            }
            // The radius must not change much along one capsule
            double dr = Math.Max(Math.Abs(radius[1] - radius[0]), Math.Max(Math.Abs(radius[2] - radius[1]), Math.Abs(radius[3] - radius[2]))) * 3;
            if (dr > 0)
            {
                step = Math.Min(step, 0.5 * r / dr);
            }
            return Math.Max(minimum, Math.Min(1, step));
        }

        // The first u >= us where g(u) <= 0 (1 + something when there is none) for the cubic of the Bezier values g[0..3]
        private static double Advance(double* g, double us, double* roots)
        {
            double value = Bernstein(g, us);
            if (value <= 0)
            {
                return us;
            }
            double a = -g[0] + 3 * g[1] - 3 * g[2] + g[3];
            double b = 3 * g[0] - 6 * g[1] + 3 * g[2];
            double c = -3 * g[0] + 3 * g[1];
            double d = g[0];
            int n = RobustCubic.Solve(a, b, c, d, roots);
            for (int i = 0; i < n; ++i)
            {
                if (roots[i] > us)
                {
                    // g is positive at us, so it is positive on [us, root) up to the first root
                    return roots[i];
                }
            }
            return 2;
        }

        private static double Bernstein(double* g, double u)
        {
            double v = 1 - u;
            return g[0] * (v * v * v) + g[1] * (3 * u * v * v) + g[2] * (3 * u * u * v) + g[3] * (u * u * u);
        }

        // The nearest entry of the ray (the origin at zero, d of unit length) into the union of the spheres (A, ra), (B, rb) and the cone that is tangent to
        // both (Inigo Quilez's solution of the capsule of two radii): the distance and the position along the axis in [0, 1]
        private static bool Capsule(SdfVector pa, SdfVector pb, double ra, double rb, SdfVector rd, double tMin, double tMax, out double t, out double fraction)
        {
            t = 0;
            fraction = 0;
            SdfVector ro = new SdfVector(0, 0, 0);
            SdfVector ba = pb - pa;
            SdfVector oa = ro - pa;
            SdfVector ob = ro - pb;
            double rr = ra - rb;
            double m0 = SdfVector.Dot(ba, ba);
            double m1 = SdfVector.Dot(ba, oa);
            double m2 = SdfVector.Dot(ba, rd);
            double m3 = SdfVector.Dot(rd, oa);
            double m5 = SdfVector.Dot(oa, oa);
            double m6 = SdfVector.Dot(ob, rd);
            double m7 = SdfVector.Dot(ob, ob);
            double d2 = m0 - rr * rr;

            double best = double.MaxValue;
            double bestFraction = 0;
            if (d2 > 0)
            {
                double k2 = d2 - m2 * m2;
                double k1 = d2 * m3 - m1 * m2 + m2 * rr * ra;
                double k0 = d2 * m5 - m1 * m1 + m1 * rr * ra * 2 - m0 * ra * ra;
                double h = k1 * k1 - k0 * k2;
                if (h >= 0 && k2 != 0)
                {
                    double root = Math.Sqrt(h);
                    double tc = (-root - k1) / k2;
                    double y = m1 - ra * rr + tc * m2;
                    if (y > 0 && y < d2 && tc > tMin && tc < tMax)
                    {
                        best = tc;
                        bestFraction = y / d2;
                    }
                }
            }
            double h1 = m3 * m3 - m5 + ra * ra;
            if (h1 > 0)
            {
                double ts = -m3 - Math.Sqrt(h1);
                if (ts > tMin && ts < best && ts < tMax)
                {
                    best = ts;
                    bestFraction = 0;
                }
            }
            double h2 = m6 * m6 - m7 + rb * rb;
            if (h2 > 0)
            {
                double ts = -m6 - Math.Sqrt(h2);
                if (ts > tMin && ts < best && ts < tMax)
                {
                    best = ts;
                    bestFraction = 1;
                }
            }
            if (best == double.MaxValue)
            {
                return false;
            }
            t = best;
            fraction = Math.Max(0, Math.Min(1, bestFraction));
            return true;
        }

        // The first hit of the ray with the sphere (q(u), r(u)) and the mapping error e(u) = v . q'(u) - r(u) r'(u) of Section 4.4 (v: from the curve to the hit)
        private bool SphereError(SdfVector o, SdfVector d, double u, out double t, out double error, out SdfVector v)
        {
            SdfVector c = Position(u) - o;
            double r = Radius(u);
            double b = SdfVector.Dot(c, d);
            double h = b * b - SdfVector.Dot(c, c) + r * r;
            if (h < 0)
            {
                t = 0;
                error = 0;
                v = default;
                return false;
            }
            t = b - Math.Sqrt(h);
            v = d * t - c;
            error = SdfVector.Dot(v, Velocity(u)) - r * RadiusDerivative(u);
            return true;
        }

        // Improves the parameter of the hit of a capsule with the secant method on e(u)
        private bool Delinearize(SdfVector o, SdfVector d, ref double u, ref double t, out SdfVector normal)
        {
            normal = default;
            double u0 = u;
            if (!SphereError(o, d, u0, out double t0, out double e0, out SdfVector v0))
            {
                return false;
            }
            if (Math.Abs(e0) < 1e-14 * Math.Max(1, Math.Abs(r0Scale(u0))))
            {
                u = u0;
                t = t0;
                normal = Unit(v0, Radius(u0));
                return true;
            }
            double u1 = u0 + (u0 < 0.5 ? 1 : -1) * 1e-3;
            if (!SphereError(o, d, u1, out double t1, out double e1, out SdfVector v1))
            {
                u = u0;
                t = t0;
                normal = Unit(v0, Radius(u0));
                return true;
            }
            for (int i = 0; i < 12; ++i)
            {
                double denominator = e1 - e0;
                if (denominator == 0)
                {
                    break;
                }
                double next = u1 - e1 * (u1 - u0) / denominator;
                next = Math.Max(0, Math.Min(1, next));
                u0 = u1;
                t0 = t1;
                e0 = e1;
                v0 = v1;
                if (!SphereError(o, d, next, out t1, out e1, out v1))
                {
                    // the iteration left the shadow of the sphere: keep the best point
                    break;
                }
                u1 = next;
                if (Math.Abs(u1 - u0) < 1e-12)
                {
                    break;
                }
            }
            u = u1;
            t = t1;
            normal = Unit(v1, Radius(u1));
            return true;
        }

        private double r0Scale(double u)
        {
            return Velocity(u).Length() * Math.Max(Radius(u), 1e-300);
        }

        private static SdfVector Unit(SdfVector v, double r)
        {
            double length = v.Length();
            return length > 0 ? v / length : v;
        }

        private static SdfVector Perpendicular(SdfVector d)
        {
            double ax = Math.Abs(d.x), ay = Math.Abs(d.y), az = Math.Abs(d.z);
            SdfVector axis = ax <= ay && ax <= az ? new SdfVector(1, 0, 0) : ay <= az ? new SdfVector(0, 1, 0) : new SdfVector(0, 0, 1);
            return SdfVector.Cross(d, axis);
        }

        private static double Component(SdfVector p, int axis)
        {
            return axis == 0 ? p.x : axis == 1 ? p.y : p.z;
        }

        private static void Extend(double* lo, double* hi, int axis, double value)
        {
            if (value < lo[axis])
            {
                lo[axis] = value;
            }
            if (value > hi[axis])
            {
                hi[axis] = value;
            }
        }

        private static int Quadratic(double a, double b, double c, double* roots)
        {
            if (Math.Abs(a) < 1e-300)
            {
                if (b == 0)
                {
                    return 0;
                }
                roots[0] = -c / b;
                return 1;
            }
            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
            {
                return 0;
            }
            double root = Math.Sqrt(discriminant);
            double q = -0.5 * (b + (b >= 0 ? root : -root));
            roots[0] = q / a;
            roots[1] = q != 0 ? c / q : roots[0];
            return 2;
        }

        private readonly SdfVector[] point;
        private readonly double[] radius;
        private readonly double averageRadius, maxSpeed, maxAcceleration;
    }
}
