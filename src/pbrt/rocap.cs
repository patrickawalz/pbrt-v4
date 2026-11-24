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
    // The end of a segment of swept disks ("rocylinder")
    public enum RocylinderEnd
    {
        // The strand goes on with the next segment in one direction: nothing
        Open,
        // The disk of the end (the end of a strand: the cylinder is cut off square)
        Flat,
        // Half a sphere beyond the disk of the end (a round end of a strand)
        Round,
        // The whole sphere of the end (where the strand goes on in another direction: the sphere fills the corner between
        // the two segments)
        Join
    }

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
            // Where the radius begins or stops changing faster than the centerline moves (|r'| = |q'|, the roots of a polynomial
            // of degree 4): between two of them each sphere is inside the next, there is no envelope, and the surface is the
            // sphere at the end of that part
            SdfVector c1 = (point[1] - point[0]) * 3, c2 = (point[0] - 2 * point[1] + point[2]) * 3, c3 = point[3] - 3 * point[2] + 3 * point[1] - point[0];
            double r1 = 3 * (radius[1] - radius[0]), r2 = 3 * (radius[0] - 2 * radius[1] + radius[2]), r3 = radius[3] - 3 * radius[2] + 3 * radius[1] - radius[0];
            double* excess = stackalloc double[5];
            excess[0] = SdfVector.Dot(c1, c1) - r1 * r1;
            excess[1] = 4 * (SdfVector.Dot(c1, c2) - r1 * r2);
            excess[2] = 6 * (SdfVector.Dot(c1, c3) - r1 * r3) + 4 * (SdfVector.Dot(c2, c2) - r2 * r2);
            excess[3] = 12 * (SdfVector.Dot(c2, c3) - r2 * r3);
            excess[4] = 9 * (SdfVector.Dot(c3, c3) - r3 * r3);
            double* turns = stackalloc double[4];
            int turnCount = PolynomialRoots.Roots(excess, 4, 0, 1, 1e-12, turns);
            envelopeEnds = turnCount == 0 ? Array.Empty<double>() : new double[turnCount];
            for (int i = 0; i < turnCount; ++i)
            {
                envelopeEnds[i] = turns[i];
            }
        }

        private readonly double[] envelopeEnds;

        // Flat caps: the strand ends in disks perpendicular to the curve instead of spheres (the solid is cut by the planes of the ends)
        public bool FlatCaps;
        // The nearest entry into the spheres of a capsule is found from where the entry stops changing along the strand (the
        // root of its derivative, EntrySlope: the default) and not by the golden section search (the first method, as exact
        // and a little slower)
        public bool RootRefinement = true;

        // Swept disks in place of swept spheres ("rocylinder"): at every u the disk of the radius across the centerline. The
        // surface is the circles of those disks; with a radius that does not change it is the surface of the spheres, and
        // with one that does it has the radius given at every u (the spheres give a little less: their envelope leans by
        // r' / |q'|). A point of the ray on the sphere of u is on its circle where it is in the plane of the disk,
        // (p - q) . q' = 0; on the envelope of the spheres it is where (p - q) . q' = -r r'. The ends are disks, spheres or
        // nothing (RocylinderEnd).
        public bool Disks;
        // The hits by the roots of a polynomial (polynomialroots.cs) in place of the search between samples. A point of the ray
        // is on the circle of the disk of u, or on the circle where the sphere of u touches the envelope of the spheres, where
        // G(u) = D^2 (S - r^2) + (S' / 2 - k r r')^2 = 0, with S(u) the square of the distance of the centerline from the line of
        // the ray, D(u) = d . q'(u), and k = 0 for the disks, 1 for the spheres: of degree 10. Every root that crosses zero is
        // found. (Not with FlatCaps on the spheres.)
        public bool Polynomial;
        // (of the swept spheres with Polynomial too: Flat and Round are the sphere of an end of the strand)
        public RocylinderEnd Start = RocylinderEnd.Flat, End = RocylinderEnd.Flat;

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

        public SdfVector Acceleration(double u)
        {
            return (point[2] - 2 * point[1] + point[0]) * (6 * (1 - u)) + (point[3] - 2 * point[2] + point[1]) * (6 * u);
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
            // The ray is taken from its point nearest the middle of the strand: the squares of the distances from the origin of a
            // ray that starts far away would take the digits that tell a thin strand from nothing (b^2 - |c|^2 + r^2 with b and
            // |c| a million times r), as for the sphere of pbrt-v4
            double shift = SdfVector.Dot(Position(0.5) - o, d);
            bool found = IntersectNear(o + d * shift, d, tMin - shift, tMax - shift, out t, out u, out normal);
            t += shift;
            return found;
        }

        private bool IntersectNear(SdfVector o, SdfVector d, double tMin, double tMax, out double t, out double u, out SdfVector normal)
        {
            t = tMax;
            u = 0;
            normal = new SdfVector(0, 0, 1);
            bool found = false;
            double slabLow = double.MinValue, slabHigh = double.MaxValue;
            SdfVector slabNormal = default;
            int slabPlane = -1;
            if (FlatCaps && !Disks)
            {
                Slab(o, d, out slabLow, out slabHigh, out slabPlane, out slabNormal, out bool empty);
                if (empty || slabLow > slabHigh)
                {
                    return false;
                }
            }

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

            double* roots = stackalloc double[3];
            if (Polynomial && (Disks || !FlatCaps))
            {
                // (the first u where the strand is not all to one side of the ray)
                double first = Math.Max(Advance(gPlus, 0, roots), Advance(gMinus, 0, roots));
                bool hit = first < 1 && PolynomialSweep(o, d, Math.Max(0, first - 1e-9), tMin, ref t, ref u, ref normal);
                hit |= Ends(o, d, tMin, ref t, ref u, ref normal);
                return hit;
            }
            double step = CapsuleStep();
            double us = 0;
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
                double sagitta = maxAcceleration * (ue - us) * (ue - us) / 8;
                double ra = Math.Max(0, Radius(us)) + sagitta, rb = Math.Max(0, Radius(ue)) + sagitta;
                SdfVector a = Position(us) - o, b = Position(ue) - o;
                if (Capsule(a, b, ra, rb, d, tMin, t, out double capsuleT, out double fraction))
                {
                    // Move the hit onto the swept surface
                    double hitU = us + fraction * (ue - us);
                    double refinedT = capsuleT;
                    SdfVector refinedNormal = default;
                    bool ok = Scan(o, d, us, ue, tMin, slabLow, slabHigh, slabNormal, ref hitU, ref refinedT, out refinedNormal);
                    if (!ok)
                    {
                        // the swept spheres of the interval are missed: the capsule was larger than the surface
                        if (ue >= 1)
                        {
                            break;
                        }
                        us = ue;
                        continue;
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
            if (Disks)
            {
                found |= Ends(o, d, tMin, ref t, ref u, ref normal);
            }
            return found;
        }

        // The ends, nearer than t. Of the swept disks: the disk of an end, half a sphere beyond it, or the whole sphere. Of the
        // swept spheres: the spheres of the two ends, and those where the envelope ends within the segment.
        private bool Ends(SdfVector o, SdfVector d, double tMin, ref double t, ref double u, ref SdfVector normal)
        {
            bool found = false;
            if (!Disks)
            {
                // The sphere of an end of the strand: the part of it beyond the circle where the envelope leaves it (the rest is
                // inside the strand). Where the strand goes on with another segment in one direction all of the sphere is inside
                // the two, and nothing of it is surface: a ray inside the strand would meet it as if from outside (Open).
                for (int end = 0; end <= 1; ++end)
                {
                    RocylinderEnd kind = end == 0 ? Start : End;
                    if (kind == RocylinderEnd.Open)
                    {
                        continue;
                    }
                    SdfVector along = Velocity(end);
                    if (kind == RocylinderEnd.Join || !(along.LengthSquared() > 0))
                    {
                        found |= Sphere(o, d, end, default, 0, tMin, ref t, ref u, ref normal);
                        continue;
                    }
                    double lean = Radius(end) * RadiusDerivative(end) / along.Length();
                    found |= Sphere(o, d, end, end == 0 ? -1 * along.Normalized() : along.Normalized(), end == 0 ? lean : -lean, tMin, ref t, ref u, ref normal);
                }
                for (int i = 0; i < envelopeEnds.Length; ++i)
                {
                    found |= Sphere(o, d, envelopeEnds[i], default, 0, tMin, ref t, ref u, ref normal);
                }
                return found;
            }
            for (int end = 0; end <= 1; ++end)
            {
                RocylinderEnd kind = end == 0 ? Start : End;
                if (kind == RocylinderEnd.Open)
                {
                    continue;
                }
                if (kind == RocylinderEnd.Join)
                {
                    found |= Sphere(o, d, end, default, 0, tMin, ref t, ref u, ref normal);
                    continue;
                }
                SdfVector axis = Velocity(end);
                double r = Radius(end);
                if (!(r > 0) || !(axis.LengthSquared() > 0))
                {
                    continue;
                }
                // (out of the strand)
                SdfVector outward = end == 0 ? -1 * axis.Normalized() : axis.Normalized();
                if (kind == RocylinderEnd.Round)
                {
                    found |= Sphere(o, d, end, outward, 0, tMin, ref t, ref u, ref normal);
                    continue;
                }
                SdfVector c = Position(end) - o;
                double slope = SdfVector.Dot(d, outward);
                // (into the strand only)
                if (!(slope < 0))
                {
                    continue;
                }
                double hit = SdfVector.Dot(c, outward) / slope;
                if (!(hit > tMin) || !(hit < t) || (d * hit - c).LengthSquared() > r * r)
                {
                    continue;
                }
                t = hit;
                u = end;
                normal = outward;
                found = true;
            }
            return found;
        }

        // The sphere of the parameter `at`, or the part of it farther than `beyond` along `outward` from its center (the zero
        // vector: all of it), where the ray goes into it, nearer than t
        private bool Sphere(SdfVector o, SdfVector d, double at, SdfVector outward, double beyond, double tMin, ref double t, ref double u, ref SdfVector normal)
        {
            SdfVector c = Position(at) - o;
            double r = Radius(at);
            if (!(r > 0))
            {
                return false;
            }
            double along = SdfVector.Dot(c, d);
            double h = along * along - SdfVector.Dot(c, c) + r * r;
            if (h < 0)
            {
                return false;
            }
            // (where the ray goes into the sphere)
            double hit = along - Math.Sqrt(h);
            if (!(hit > tMin) || !(hit < t))
            {
                return false;
            }
            SdfVector radial = d * hit - c;
            if (outward.LengthSquared() > 0 && SdfVector.Dot(radial, outward) < beyond)
            {
                return false;
            }
            t = hit;
            u = at;
            normal = radial / r;
            return true;
        }

        // The interval of the ray inside the slab between the planes of the ends (flat caps), and the plane (0: start, 1: end) that bounds it from below
        private void Slab(SdfVector o, SdfVector d, out double low, out double high, out int lowPlane, out SdfVector lowNormal, out bool empty)
        {
            low = double.MinValue;
            high = double.MaxValue;
            lowPlane = -1;
            lowNormal = default;
            empty = false;
            for (int end = 0; end <= 1; ++end)
            {
                SdfVector axis = Velocity(end);
                if (!(axis.LengthSquared() > 0))
                {
                    continue;
                }
                axis = axis.Normalized();
                // inside the slab: (p - q0) . axis >= 0 at the start, (p - q1) . axis <= 0 at the end; outward = -axis at the start and +axis at the end
                SdfVector outward = end == 0 ? -axis : axis;
                double slope = SdfVector.Dot(d, outward);
                double offset = SdfVector.Dot(Position(end) - o, outward);
                // inside: t * slope <= offset
                if (slope > 0)
                {
                    high = Math.Min(high, offset / slope);
                }
                else if (slope < 0)
                {
                    double entry = offset / slope;
                    if (entry > low)
                    {
                        low = entry;
                        lowPlane = end;
                        lowNormal = outward;
                    }
                }
                else if (offset < 0)
                {
                    empty = true;
                }
            }
        }

        // The size of the capsules in the parameter (the function capsule_step of the paper, without the footprint of the pixel)
        private double CapsuleStep()
        {
            const double minimum = 1.0 / 2000;
            double r = Math.Max(averageRadius, 1e-300);
            // The capsule replaces the arc of the curve, whose sagitta is a fraction of the radius
            double step = maxAcceleration > 0 ? Math.Sqrt(0.2 * r / maxAcceleration) : 1;
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

        // The clamped entry of the ray into the sphere (q(u), r(u)): the entry, moved to the start of the slab (flat caps) when the ray is already inside the sphere there,
        // or double.MaxValue when the ray leaves the sphere before the slab or misses it
        private double ClampedEntry(SdfVector o, SdfVector d, double u, double tMin, double slabLow, double slabHigh, out bool onPlane, out SdfVector v)
        {
            onPlane = false;
            v = default;
            SdfVector c = Position(u) - o;
            double r = Radius(u);
            double b = SdfVector.Dot(c, d);
            double h = b * b - SdfVector.Dot(c, c) + r * r;
            if (h < 0)
            {
                return double.MaxValue;
            }
            double root = Math.Sqrt(h);
            double entry = b - root, exit = b + root;
            double start = Math.Max(entry, slabLow), finish = Math.Min(exit, slabHigh);
            if (!(start <= finish) || !(start > tMin))
            {
                return double.MaxValue;
            }
            onPlane = start > entry;
            v = d * start - c;
            return start;
        }

        // The derivative along u of the distance at which the ray enters the sphere of u, b - sqrt(h) with b = (C - o) . d and
        // h = b^2 - |C - o|^2 + r^2: b' - h' / (2 sqrt(h)). At an end of the spheres that the ray hits h is 0, and the entry
        // falls (at the first of them) or rises (at the last) without bound: -infinity or +infinity by the sign of h', and
        // `outside` (the value for an end of the range) where the ray misses the sphere.
        private double EntrySlope(SdfVector o, SdfVector d, double u, double outside)
        {
            SdfVector c = Position(u) - o, velocity = Velocity(u);
            double r = Radius(u), along = SdfVector.Dot(c, d);
            double h = along * along - SdfVector.Dot(c, c) + r * r;
            if (h < 0)
            {
                return outside;
            }
            double alongSlope = SdfVector.Dot(velocity, d);
            double hSlope = 2 * along * alongSlope - 2 * SdfVector.Dot(c, velocity) + 2 * r * RadiusDerivative(u);
            if (h == 0)
            {
                return hSlope > 0 ? double.NegativeInfinity : hSlope < 0 ? double.PositiveInfinity : alongSlope;
            }
            return alongSlope - hSlope / (2 * Math.Sqrt(h));
        }

        // The parameter in [a, b] of the sphere that the ray enters first, [a, b] being around the nearest of the samples: where
        // the slope of the entry changes from falling to rising. An end when the entry rises from a or falls to b. In between
        // the root of the slope by the secant of the two ends of the part that holds it, with a step to the middle whenever
        // the secant did not halve the part (the slope is without bound at the ends of the spheres that are hit, where the
        // secant alone would creep).
        private double SlopeRoot(SdfVector o, SdfVector d, double a, double b)
        {
            double sa = EntrySlope(o, d, a, double.NegativeInfinity), sb = EntrySlope(o, d, b, double.PositiveInfinity);
            if (!(sa < 0))
            {
                return a;
            }
            if (!(sb > 0))
            {
                return b;
            }
            double width = b - a;
            bool halve = false;
            for (int i = 0; i < 60 && b - a > 1e-9 * width; ++i)
            {
                double before = b - a;
                double next = 0.5 * (a + b);
                if (!halve && !double.IsInfinity(sa) && !double.IsInfinity(sb))
                {
                    double secant = a - sa * (b - a) / (sb - sa);
                    if (secant > a && secant < b)
                    {
                        next = secant;
                    }
                }
                double slope = EntrySlope(o, d, next, 0);
                if (slope == 0)
                {
                    return next;
                }
                if (slope < 0)
                {
                    a = next;
                    sa = slope;
                }
                else
                {
                    b = next;
                    sb = slope;
                }
                halve = b - a > 0.5 * before;
            }
            return 0.5 * (a + b);
        }

        // The swept disks. The point of the ray that enters (side -1) or leaves (side +1) the sphere of u is on the circle of the
        // disk of u where it is in the plane of the disk: F(u) = (p - q) . q' = t (d . q') - (q - o) . q' = 0. F and the
        // distance t; false where the ray misses the sphere.
        private bool DiskCondition(SdfVector o, SdfVector d, double u, int side, out double f, out double t)
        {
            SdfVector c = Position(u) - o, velocity = Velocity(u);
            double r = Radius(u), along = SdfVector.Dot(c, d);
            double h = along * along - SdfVector.Dot(c, c) + r * r;
            if (h < 0)
            {
                // (within the rounding of an end of the spheres that are hit)
                if (h < -1e-9 * r * r)
                {
                    f = 0;
                    t = 0;
                    return false;
                }
                h = 0;
            }
            t = along + side * Math.Sqrt(h);
            f = t * SdfVector.Dot(d, velocity) - SdfVector.Dot(c, velocity);
            if (!Disks)
            {
                // (the circle where the sphere touches the envelope: (p - q) . q' = -r r')
                f += r * RadiusDerivative(u);
            }
            return true;
        }

        // The nearest point of the ray on the circles of the disks of the parameters in [lo, hi], over which the ray hits the
        // spheres: the roots of F on the side of the spheres that the ray enters and on the side that it leaves (the two meet
        // at the ends of the spheres that are hit), found between samples of F with different signs. The normal is that of the
        // surface of the circles: (p - q) (|q'|^2 - (p - q) . q'') - r r' q'.
        private bool DiskRoots(SdfVector o, SdfVector d, double lo, double hi, double tMin, ref double u, ref double t, out SdfVector normal)
        {
            normal = default;
            const int samples = 8;
            // The two sides at each sample from one sphere
            double* entry = stackalloc double[samples + 1];
            double* leave = stackalloc double[samples + 1];
            bool* valid = stackalloc bool[samples + 1];
            for (int i = 0; i <= samples; ++i)
            {
                double parameter = lo + (hi - lo) * i / samples;
                SdfVector c = Position(parameter) - o, tangent = Velocity(parameter);
                double r = Radius(parameter), along = SdfVector.Dot(c, d);
                double h = along * along - SdfVector.Dot(c, c) + r * r;
                valid[i] = h >= -1e-9 * r * r;
                double root = Math.Sqrt(Math.Max(0, h)), slope = SdfVector.Dot(d, tangent), offset = SdfVector.Dot(c, tangent);
                entry[i] = (along - root) * slope - offset;
                leave[i] = (along + root) * slope - offset;
            }
            double bestT = double.MaxValue, bestU = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                double* values = side < 0 ? entry : leave;
                for (int i = 1; i <= samples; ++i)
                {
                    if (!valid[i - 1] || !valid[i] || (values[i - 1] > 0) == (values[i] > 0))
                    {
                        continue;
                    }
                    // The root between, by false position: the secant of the two ends of the part that holds the root, and where an
                    // end stays for a second step, half its value (so that the other end moves too)
                    double a = lo + (hi - lo) * (i - 1) / samples, b = lo + (hi - lo) * i / samples, sa = values[i - 1], sb = values[i];
                    double width = b - a, rootU = 0.5 * (a + b);
                    int kept = 0;
                    for (int k = 0; k < 60 && b - a > 1e-9 * width; ++k)
                    {
                        double next = a - sa * (b - a) / (sb - sa);
                        if (!(next > a && next < b))
                        {
                            next = 0.5 * (a + b);
                        }
                        double step = Math.Abs(next - rootU);
                        rootU = next;
                        // (the steps of false position become short at the root before its two ends close on it)
                        if (!DiskCondition(o, d, next, side, out double f, out double tn) || f == 0 || (k > 0 && step <= 1e-9 * width))
                        {
                            break;
                        }
                        if ((f > 0) == (sa > 0))
                        {
                            a = next;
                            sa = f;
                            if (kept == 1)
                            {
                                sb *= 0.5;
                            }
                            kept = 1;
                        }
                        else
                        {
                            b = next;
                            sb = f;
                            if (kept == -1)
                            {
                                sa *= 0.5;
                            }
                            kept = -1;
                        }
                    }
                    if (DiskCondition(o, d, rootU, side, out double fr, out double rootT) && rootT > tMin && rootT < bestT && Enters(o, d, rootT, rootU))
                    {
                        bestT = rootT;
                        bestU = rootU;
                    }
                }
            }
            if (bestT == double.MaxValue)
            {
                return false;
            }
            u = bestU;
            t = bestT;
            normal = DiskNormal(o + d * bestT, bestU);
            return true;
        }

        // The root of the condition of the circle on one side of the spheres (DiskCondition) nearest u, by steps of the secant:
        // u and the distance there; true where the steps came to a stop at a root (u and t are left as they were otherwise)
        private bool Refine(SdfVector o, SdfVector d, int side, ref double u, ref double t)
        {
            if (!DiskCondition(o, d, u, side, out double condition, out double distance))
            {
                return false;
            }
            double start = u, rootU = u, otherU = Math.Min(1, Math.Max(0, u + (u < 0.5 ? 1e-6 : -1e-6)));
            if (!DiskCondition(o, d, otherU, side, out double otherCondition, out double otherT))
            {
                return false;
            }
            bool settled = condition == 0;
            for (int k = 0; k < 6 && !settled && condition != otherCondition; ++k)
            {
                double next = rootU - condition * (rootU - otherU) / (condition - otherCondition);
                if (!(Math.Abs(next - start) <= 1e-3) || next < 0 || next > 1 || !DiskCondition(o, d, next, side, out double nextCondition, out double nextT))
                {
                    return false;
                }
                otherU = rootU;
                otherCondition = condition;
                rootU = next;
                condition = nextCondition;
                distance = nextT;
                settled = condition == 0 || Math.Abs(rootU - otherU) <= 1e-12;
            }
            if (!settled)
            {
                return false;
            }
            u = rootU;
            t = distance;
            return true;
        }

        // Whether the ray goes into the strand at its point of the distance t on the circle of u (and not out of it). A strand
        // is met from outside only, as it always was by the search of the swept spheres: a ray that starts in it (the light
        // that a fiber sends on through itself starts just under its surface) does not meet its far side, where a fiber
        // would scatter it a second time.
        private bool Enters(SdfVector o, SdfVector d, double t, double u)
        {
            SdfVector p = o + d * t;
            return SdfVector.Dot(d, Disks ? DiskNormal(p, u) : p - Position(u)) < 0;
        }

        // The normal of the surface of the circles at the point p of the circle of u: (p - q) (|q'|^2 - (p - q) . q'') - r r' q'
        private SdfVector DiskNormal(SdfVector p, double u)
        {
            SdfVector radial = p - Position(u), velocity = Velocity(u);
            SdfVector outward = radial * (velocity.LengthSquared() - SdfVector.Dot(radial, Acceleration(u))) - velocity * (Radius(u) * RadiusDerivative(u));
            return outward.LengthSquared() > 0 ? outward.Normalized() : Unit(radial, Radius(u));
        }

        // The nearest point of the ray on the circles of the parameters in [from, 1], by the roots of the polynomial G, of the
        // centerline across the ray only (e = q - (q . d) d, whose square S does not have the distance along the ray in it).
        // At a root the distance is that of the side of the sphere of u which meets the condition of the circle.
        private bool PolynomialSweep(SdfVector o, SdfVector d, double from, double tMin, ref double t, ref double u, ref SdfVector normal)
        {
            // The centerline in powers of u, and across the ray
            SdfVector c0 = point[0] - o, c1 = (point[1] - point[0]) * 3, c2 = (point[0] - 2 * point[1] + point[2]) * 3;
            SdfVector c3 = point[3] - 3 * point[2] + 3 * point[1] - point[0];
            SdfVector e0 = c0 - d * SdfVector.Dot(c0, d), e1 = c1 - d * SdfVector.Dot(c1, d), e2 = c2 - d * SdfVector.Dot(c2, d), e3 = c3 - d * SdfVector.Dot(c3, d);
            double r0 = radius[0], r1 = 3 * (radius[1] - radius[0]), r2 = 3 * (radius[0] - 2 * radius[1] + radius[2]);
            double r3 = radius[3] - 3 * radius[2] + 3 * radius[1] - radius[0];
            // S = |e|^2, and W = S - r^2
            double* s = stackalloc double[7];
            s[0] = SdfVector.Dot(e0, e0);
            s[1] = 2 * SdfVector.Dot(e0, e1);
            s[2] = 2 * SdfVector.Dot(e0, e2) + SdfVector.Dot(e1, e1);
            s[3] = 2 * (SdfVector.Dot(e0, e3) + SdfVector.Dot(e1, e2));
            s[4] = 2 * SdfVector.Dot(e1, e3) + SdfVector.Dot(e2, e2);
            s[5] = 2 * SdfVector.Dot(e2, e3);
            s[6] = SdfVector.Dot(e3, e3);
            double* w = stackalloc double[7];
            w[0] = s[0] - r0 * r0;
            w[1] = s[1] - 2 * r0 * r1;
            w[2] = s[2] - (2 * r0 * r2 + r1 * r1);
            w[3] = s[3] - 2 * (r0 * r3 + r1 * r2);
            w[4] = s[4] - (2 * r1 * r3 + r2 * r2);
            w[5] = s[5] - 2 * r2 * r3;
            w[6] = s[6] - r3 * r3;
            // G is not below zero where W is not (where the line of the ray is outside the sphere of u): the roots of G are in
            // the parts of [from, 1] where W < 0, between the roots of W (of degree 6), and most rays that miss have none
            double* inside = stackalloc double[8];
            int insideCount = PolynomialRoots.Roots(w, 6, from, 1, 1e-7, inside + 1);
            inside[0] = from;
            inside[insideCount + 1] = 1;
            bool below = PolynomialRoots.Evaluate(w, 6, from) < 0;
            if (insideCount == 0 && !below)
            {
                return false;
            }
            // M = S' / 2 and D^2, D = d . q'
            double* m = stackalloc double[6];
            for (int i = 0; i < 6; ++i)
            {
                m[i] = 0.5 * (i + 1) * s[i + 1];
            }
            if (!Disks)
            {
                // r r' = (r^2)' / 2
                m[0] -= r0 * r1;
                m[1] -= 2 * r0 * r2 + r1 * r1;
                m[2] -= 3 * (r0 * r3 + r1 * r2);
                m[3] -= 2 * (2 * r1 * r3 + r2 * r2);
                m[4] -= 5 * r2 * r3;
                m[5] -= 3 * r3 * r3;
            }
            double d0 = SdfVector.Dot(d, c1), d1 = 2 * SdfVector.Dot(d, c2), d2 = 3 * SdfVector.Dot(d, c3);
            double* squared = stackalloc double[5];
            squared[0] = d0 * d0;
            squared[1] = 2 * d0 * d1;
            squared[2] = 2 * d0 * d2 + d1 * d1;
            squared[3] = 2 * d1 * d2;
            squared[4] = d2 * d2;
            double* g = stackalloc double[11];
            for (int i = 0; i <= 10; ++i)
            {
                g[i] = 0;
            }
            for (int i = 0; i <= 4; ++i)
            {
                for (int j = 0; j <= 6; ++j)
                {
                    g[i + j] += squared[i] * w[j];
                }
            }
            for (int i = 0; i <= 5; ++i)
            {
                for (int j = 0; j <= 5; ++j)
                {
                    g[i + j] += m[i] * m[j];
                }
            }
            double* found = stackalloc double[10];
            double* some = stackalloc double[10];
            int count = 0;
            for (int part = 0; part <= insideCount && count < 10; ++part)
            {
                if (below)
                {
                    // (a little beyond the ends of the part: G is zero at an end where the ray touches the sphere there)
                    int more = PolynomialRoots.Roots(g, 10, Math.Max(from, inside[part] - 1e-6), Math.Min(1, inside[part + 1] + 1e-6), 1e-9, some);
                    for (int i = 0; i < more && count < 10; ++i)
                    {
                        found[count++] = some[i];
                    }
                }
                below = !below;
            }
            double bestT = double.MaxValue, bestU = 0;
            for (int i = 0; i < count; ++i)
            {
                bool entered = DiskCondition(o, d, found[i], -1, out double entryCondition, out double entryT);
                bool left = DiskCondition(o, d, found[i], 1, out double leaveCondition, out double leaveT);
                if (!entered || !left)
                {
                    continue;
                }
                // The root of the polynomial is as good as the rounding of its terms lets it be (they cancel): from it, the root of
                // the condition of that side itself
                int side = Math.Abs(entryCondition) <= Math.Abs(leaveCondition) ? -1 : 1;
                double rootU = found[i], rootT = side < 0 ? entryT : leaveT;
                Refine(o, d, side, ref rootU, ref rootT);
                if (rootT > tMin && rootT < bestT && Enters(o, d, rootT, rootU))
                {
                    bestT = rootT;
                    bestU = rootU;
                }
            }
            // A ray that lies in the plane of the circle of some u enters and leaves through that one circle: G has a root of
            // multiplicity two there (it touches zero and does not cross it), and for a ray that is nearly in the plane two roots
            // too close together to be told apart. The circle is where D = 0 (the ray across the strand), a quadratic: from
            // there, the roots of the conditions of the two sides themselves, which are apart.
            double* across = stackalloc double[2];
            double* direction = stackalloc double[3];
            direction[0] = d0;
            direction[1] = d1;
            direction[2] = d2;
            int acrossCount = PolynomialRoots.Roots(direction, 2, from, 1, 0, across);
            for (int i = 0; i < acrossCount; ++i)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    double rootU = across[i], rootT = 0;
                    if (Refine(o, d, side, ref rootU, ref rootT) && rootT > tMin && rootT < bestT && Enters(o, d, rootT, rootU))
                    {
                        bestT = rootT;
                        bestU = rootU;
                    }
                }
            }
            if (bestT == double.MaxValue || !(bestT < t))
            {
                return false;
            }
            u = bestU;
            t = bestT;
            normal = Disks ? DiskNormal(o + d * bestT, bestU) : Unit(d * bestT - (Position(bestU) - o), Radius(bestU));
            return true;
        }

        // How far the ray goes into the sphere of the parameter u: the square of half the chord (negative: the ray misses it)
        private double Depth(SdfVector o, SdfVector d, double u)
        {
            SdfVector c = Position(u) - o;
            double r = Radius(u), along = SdfVector.Dot(c, d);
            return along * along - SdfVector.Dot(c, c) + r * r;
        }

        // The end of the parameters whose spheres the ray hits, between `outside` (missed) and `inside` (hit)
        private double EndOfHits(SdfVector o, SdfVector d, double outside, double inside)
        {
            for (int i = 0; i < 24; ++i)
            {
                double middle = 0.5 * (outside + inside);
                if (Depth(o, d, middle) >= 0)
                {
                    inside = middle;
                }
                else
                {
                    outside = middle;
                }
            }
            return inside;
        }

        // The nearest entry into the spheres of the parameters in [lo, hi] (cut by the slab of flat caps). The entry of the sphere that is the nearest is on the
        // swept surface (or on the plane of an end). Near the silhouette the ray hits the spheres of a short range of the parameters only, which samples of
        // the entry over [lo, hi] step over (the strand then shows as a string of beads, the more the longer the capsule: a straight strand most of all).
        // So first the sphere that the ray goes into the deepest is found (the depth is defined for every sphere, hit or not: a scan and the golden section
        // search), then the range of the spheres that are hit around it, and then the nearest entry within that range (a scan and the golden section search).
        private bool Scan(SdfVector o, SdfVector d, double lo, double hi, double tMin, double slabLow, double slabHigh, SdfVector slabNormal, ref double u, ref double t,
                          out SdfVector normal)
        {
            normal = default;
            const int samples = 12;
            const double phi = 0.6180339887498949;
            double deepest = double.MinValue, deepestU = lo;
            for (int i = 0; i <= samples; ++i)
            {
                double w = lo + (hi - lo) * i / samples;
                double depth = Depth(o, d, w);
                if (depth > deepest)
                {
                    deepest = depth;
                    deepestU = w;
                }
            }
            double from = Math.Max(lo, deepestU - (hi - lo) / samples), to = Math.Min(hi, deepestU + (hi - lo) / samples);
            // (the search is only needed when no sample is in a sphere that the ray hits)
            for (int i = 0; i < 28 && deepest < 0; ++i)
            {
                double c1 = to - phi * (to - from), c2 = from + phi * (to - from);
                if (Depth(o, d, c1) > Depth(o, d, c2))
                {
                    to = c2;
                }
                else
                {
                    from = c1;
                }
            }
            double middleU = 0.5 * (from + to);
            if (deepest < 0 && Depth(o, d, middleU) > deepest)
            {
                deepestU = middleU;
                deepest = Depth(o, d, middleU);
            }
            if (deepest < 0)
            {
                return false;
            }
            lo = Depth(o, d, lo) >= 0 ? lo : EndOfHits(o, d, lo, deepestU);
            hi = Depth(o, d, hi) >= 0 ? hi : EndOfHits(o, d, hi, deepestU);
            if (Disks)
            {
                return DiskRoots(o, d, lo, hi, tMin, ref u, ref t, out normal);
            }
            double bestT = double.MaxValue, bestU = 0;
            for (int i = 0; i <= samples; ++i)
            {
                double w = lo + (hi - lo) * i / samples;
                double ts = ClampedEntry(o, d, w, tMin, slabLow, slabHigh, out bool planeSample, out SdfVector vs);
                if (ts < bestT)
                {
                    bestT = ts;
                    bestU = w;
                }
            }
            if (bestT == double.MaxValue)
            {
                return false;
            }
            double a = Math.Max(lo, bestU - (hi - lo) / samples), b = Math.Min(hi, bestU + (hi - lo) / samples);
            double best;
            if (RootRefinement)
            {
                best = SlopeRoot(o, d, a, b);
            }
            else
            {
                for (int i = 0; i < 34; ++i)
                {
                    double c1 = b - phi * (b - a), c2 = a + phi * (b - a);
                    double f1 = ClampedEntry(o, d, c1, tMin, slabLow, slabHigh, out bool p1, out SdfVector v1);
                    double f2 = ClampedEntry(o, d, c2, tMin, slabLow, slabHigh, out bool p2, out SdfVector v2);
                    if (f1 < f2)
                    {
                        b = c2;
                    }
                    else
                    {
                        a = c1;
                    }
                }
                best = 0.5 * (a + b);
            }
            double tb = ClampedEntry(o, d, best, tMin, slabLow, slabHigh, out bool onPlane, out SdfVector vb);
            if (!(tb <= bestT))
            {
                best = bestU;
                tb = ClampedEntry(o, d, best, tMin, slabLow, slabHigh, out onPlane, out vb);
            }
            u = best;
            t = tb;
            normal = onPlane ? slabNormal : Unit(vb, Radius(best));
            return true;
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
