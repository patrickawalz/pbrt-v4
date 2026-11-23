// (not in pbrt-v4) ribbon3shape.cs
//
// The "ribbon3" shape: the ribbon of the curves of pbrt-v4 (a cubic centerline, a normal that goes round from one end of a curve to
// the other) as a surface of its own, that rays hit where it is and that can be sampled as a light.
//
//   Shape "ribbon3" "point3 P" [...] ("string basis" "b-spline") ("integer degree" 2) ("integer step" ...) ("string wrap" "nonperiodic")
//         ("float tension" 0.5) ("float knots" [...])
//         ("normal N" [...]) ("float width" [...]) ("rgb Cs" [...]) ("bool solidanglesampling" true) ("bool flipturns" false)
//         ("string intersector" "polynomial") ("string look" "surface") ("string normals" "slerp")
//
//   normals: how "N" goes from one end of a curve to the other (Ribbon3Normals). "slerp": round at one rate, as the ribbons of
//   pbrt-v4. "linear": in a line and then of unit length, a "varying" normal of RenderMan. "basis": the normals of the control
//   points through the basis of the strand and then of unit length, a "vertex" normal of RenderMan. In all three the bitangent
//   is across the centerline and the normal, T x n. (RenderMan gives no rule for its curves by name; these are the rules of
//   its primitive variables.)
//
//   look: "surface" (the surface below), or "curves": the look of the "curves" of pbrt-v4 in their ribbon style, a strip that
//   faces each ray and is as wide as the normal makes it for that ray (Ribbon3Curve.FacingCrossing), on this shape's bases, with
//   its widths and colors along the strand. It is not a surface: it cannot be a light. With "flipturns" the strip also narrows
//   to nothing where "ribbon" turns its sides.
//   look "facing": the strip that faces each ray at its whole width, whatever the normals: the flat style of the "curves" of
//   pbrt-v4, and the curves of RenderMan without normals as they were (a ribbon that turns to face whoever looks at it). Not a
//   surface either.
//   look "cylinder": the facing strip with the normal of a round strand across it: the cylinder style of the "curves" of
//   pbrt-v4 (a picture of a tube; "rocap" is the tube).
//
// The surface of one curve is
//
//   S(u, v) = C(u) + v w(u) b(u),   b(u) = T(u) x n(u) / |T(u) x n(u)|,   u in [0, 1], v in [-1, 1]
//
// with C the cubic Bezier centerline, T its derivative, w the cubic half width and n the normal: from the normal at the start of
// the curve to that at its end at one rate (the spherical interpolation that pbrt-v4 uses for its ribbons). The ribbon is across
// the normal everywhere, however much the strand turns or twists within a curve.
//
//   The three ribbons:
//     "curves" in the ribbon style (pbrt-v4): the same centerline and normals, but no surface: a strip that faces each ray, as wide
//       as the ribbon would look from there. It has no points to sample.
//     "ribbon", "ribbon2" (Reshetov): a surface with a quadratic centerline and a bitangent that goes in a line along a segment, so
//       that the ray is found from a cubic equation. Cubic bases are approximated by quadratic pieces, and a strand that turns
//       is cut into more segments.
//     "ribbon3": the surface above. Every basis of strandbasis.cs is exact (they all are cubic Bezier curves; the default is the
//       quadratic B-spline of "ribbon", so that the three take the same points). There is no equation for the ray to solve: the
//       ray is found by Newton's method (below). Sampled as "ribbon2" is.
//
//   N: one for each point, following the basis; the normals at the ends of each curve are used. Without "N" the normal is that of
//   the plane of the start of the strand, carried along the strand without twist (so a strand in a plane lies flat in it).
//   width, Cs: one for each point (or one for all), following the basis; "width" is the half width.
//   flipturns: the behavior of "ribbon" where a curve turns by more than a right angle, instead of lying across the normals
//   as given: the ribbon turns its sides there and shows its other face from there on. The bitangents are those of "ribbon2"
//   with "flipturns" for the same strand (its segments, "integer flippieces" of them to a cubic curve as there; "integer
//   pieces" 4 when that is not given), on the centerline of this shape: with the default basis that is the surface of
//   "ribbon" itself, with the others that of "ribbon2" but for its centerline of quadratic pieces.
//   flipstretch: "curve" or "bend", as for "ribbon2" (cubic bases with normals: the rule of flipturns over stretches that go
//   round the bends of the strand, as the segments of "ribbon" do).
//
// The ray. "intersector" "polynomial" (the default): where the crossing function below is a polynomial in u (one normal all
// along, "normals" "linear" or "basis", and every look that faces the ray), all its roots by polynomialroots.cs
// (Ribbon3Curve.CrossingPolynomial); elsewhere (a normal that turns at one rate, "flipturns") it is "roots":
//
// "intersector" "roots". The surface is the line across the ribbon, C(u) + s b(u), moved along u. A ray
// meets that line where the ray, b(u) and the step from the origin of the ray to C(u) are in one plane:
//
//   f(u) = (C(u) - o) . (d x b(u)) = 0
//
// one equation in u (for the quadratic centerline and the bitangent in a line of "ribbon" it is the cubic equation of Reshetov;
// here it is solved by numbers). Each curve is cut into leaves over which the centerline and the bitangent each turn by no more
// than 45 degrees; f and f' are kept ready at the ends of the leaves. A leaf whose f changes its sign has a crossing between
// (Newton's method kept between the two ends, which cannot leave them); a leaf whose f has one sign but turns back toward 0
// within it (a ray at a low angle) is searched for the turn, and has two crossings when f is beyond 0 there. At a crossing the
// position across the width and the distance follow at once; it is a hit within the width. Nothing is assumed flat, and every
// crossing is found. Rendered faster than "newton" below on strands that turn sharply, and never slower by more than the noise
// of the timing (the same images but for single samples).
//
// The ray, "intersector" "newton" (the first method, kept for comparison). Each curve is cut into leaves, parts of its parameter over which the centerline and the bitangent each turn by no more
// than 25 degrees, so that a leaf is close to a flat strip. A ray that passes within the leaf's reach (the half width, and the
// height of the arc of the centerline over its chord) is taken to the leaf: from the point of the leaf's chord that the ray is
// nearest to, Newton's method in (u, v) on the two components of S(u, v) - o across the ray. On a strip that is nearly flat that
// has one solution and finds it in a few steps. The hit counts when |v| <= 1 and u is in the leaf (a solution beside the leaf is
// left to the leaf beside it, which finds the same point). The hit is on the surface itself to the rounding of the numbers; a
// ray along the surface (within a millionth of its plane) is not found. A ray at a low angle to a leaf can cross it twice, and
// is searched for from both ends of the leaf too. A shape is one curve.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // How the normal of a ribbon goes from one end of a curve to the other
    public enum Ribbon3Normals
    {
        // Round from one to the other at one rate (the spherical interpolation of the ribbons of pbrt-v4)
        Slerp,
        // In a line from one to the other, then made of unit length (a "varying" normal of RenderMan): the same normals as
        // Slerp at the ends and at the middle, turning faster at the middle than at the ends
        Linear,
        // The cubic curve of the normals of the control points in the basis of the strand, then made of unit length (a
        // "vertex" normal of RenderMan)
        Basis
    }

    // Ribbon3Curve Definition
    // One cubic curve of a ribbon
    public sealed class Ribbon3Curve
    {
        // Ribbon3Curve Public Methods
        // points, widths: the four Bezier values; n0, n1: the normals at the ends; index, count: the curve among those of the strand.
        // like: the segments of "ribbon2" over this curve, at equal parts of its parameter, whose bitangents the curve takes
        // in place of the one across the normal ("flipturns"), or null.
        // interpolation: how the normal goes from n0 to n1; values: the four Bezier values of the normal (for Basis; without
        // them Basis is Slerp).
        public Ribbon3Curve(SdfVector[] points, double[] widths, SdfVector n0, SdfVector n1, Ribbon3Normals interpolation, SdfVector[] values, RibbonSegment[] like,
                            int index, int count)
        {
            this.like = like;
            this.interpolation = interpolation == Ribbon3Normals.Basis && values == null ? Ribbon3Normals.Slerp : interpolation;
            if (values != null)
            {
                v0 = values[0];
                v1 = values[1];
                v2 = values[2];
                v3 = values[3];
            }
            p0 = points[0];
            p1 = points[1];
            p2 = points[2];
            p3 = points[3];
            w0 = widths[0];
            w1 = widths[1];
            w2 = widths[2];
            w3 = widths[3];
            this.n0 = n0.Normalized();
            this.n1 = n1.Normalized();
            this.index = index;
            this.count = count;
            // The turn that takes n0 to n1: about the direction across both, by the angle between them
            double cosine = Math.Max(-1, Math.Min(1, SdfVector.Dot(this.n0, this.n1)));
            SdfVector across = SdfVector.Cross(this.n0, this.n1);
            double sine = across.Length();
            angle = sine > 1e-9 || cosine < 0 ? Math.Atan2(sine, cosine) : 0;
            axis = sine > 1e-9 ? across / sine : RibbonBuilder.OrthogonalTo(this.n0);
        }

        public int Index
        {
            get { return index; }
        }

        public int Count
        {
            get { return count; }
        }

        public SdfVector Center(double u)
        {
            double s = 1 - u;
            return p0 * (s * s * s) + p1 * (3 * u * s * s) + p2 * (3 * u * u * s) + p3 * (u * u * u);
        }

        public SdfVector Velocity(double u)
        {
            double s = 1 - u;
            return (p1 - p0) * (3 * s * s) + (p2 - p1) * (6 * u * s) + (p3 - p2) * (3 * u * u);
        }

        public SdfVector Acceleration(double u)
        {
            return (p2 - 2 * p1 + p0) * (6 * (1 - u)) + (p3 - 2 * p2 + p1) * (6 * u);
        }

        public double Width(double u)
        {
            double s = 1 - u;
            return w0 * (s * s * s) + w1 * (3 * u * s * s) + w2 * (3 * u * u * s) + w3 * (u * u * u);
        }

        public double WidthDerivative(double u)
        {
            double s = 1 - u;
            return (w1 - w0) * (3 * s * s) + (w2 - w1) * (6 * u * s) + (w3 - w2) * (3 * u * u);
        }

        // The largest length of the second derivative of the centerline, and the largest size of that of the width (both are
        // lines in u, the largest at an end)
        public double LargestAcceleration()
        {
            return Math.Max(Acceleration(0).Length(), Acceleration(1).Length());
        }

        public double LargestWidthAcceleration()
        {
            return 6 * Math.Max(Math.Abs(w2 - 2 * w1 + w0), Math.Abs(w3 - 2 * w2 + w1));
        }

        // The normal at u, round from that of the start to that of the end at one rate (the normal of the start turned about
        // the axis by u times the angle), and its derivative
        public SdfVector Normal(double u, out SdfVector derivative)
        {
            if (interpolation != Ribbon3Normals.Slerp)
            {
                // A vector that goes in a line, or along the cubic curve of the values, made of unit length
                double s = 1 - u;
                SdfVector vector, vectorDerivative;
                if (interpolation == Ribbon3Normals.Linear)
                {
                    vector = n0 * s + n1 * u;
                    vectorDerivative = n1 - n0;
                }
                else
                {
                    vector = v0 * (s * s * s) + v1 * (3 * u * s * s) + v2 * (3 * u * u * s) + v3 * (u * u * u);
                    vectorDerivative = (v1 - v0) * (3 * s * s) + (v2 - v1) * (6 * u * s) + (v3 - v2) * (3 * u * u);
                }
                double length = vector.Length();
                if (length > 1e-12)
                {
                    SdfVector unit = vector / length;
                    derivative = (vectorDerivative - unit * SdfVector.Dot(unit, vectorDerivative)) / length;
                    return unit;
                }
                // (the vector passes through 0: opposite normals; the normal of the start)
                derivative = new SdfVector(0, 0, 0);
                return n0;
            }
            if (angle == 0)
            {
                derivative = new SdfVector(0, 0, 0);
                return n0;
            }
            double turn = u * angle, cosine = Math.Cos(turn), sine = Math.Sin(turn);
            SdfVector normal = n0 * cosine + SdfVector.Cross(axis, n0) * sine + axis * (SdfVector.Dot(axis, n0) * (1 - cosine));
            derivative = SdfVector.Cross(axis, normal) * angle;
            return normal;
        }

        // The centerline, the half width and the bitangent (of unit length, across the centerline and the normal) at u, with the
        // derivatives of the three
        public void Frame(double u, out SdfVector center, out SdfVector velocity, out double width, out double widthDerivative, out SdfVector bitangent,
                          out SdfVector bitangentDerivative)
        {
            center = Center(u);
            velocity = Velocity(u);
            width = Width(u);
            widthDerivative = WidthDerivative(u);
            if (like != null)
            {
                int part = Math.Max(0, Math.Min(like.Length - 1, (int)(u * like.Length)));
                double local = u * like.Length - part;
                SdfVector generator = like[part].Generator(local);
                double generatorLength = generator.Length();
                if (generatorLength > 0)
                {
                    SdfVector generatorDerivative = like[part].GeneratorDerivative(local) * like.Length;
                    bitangent = generator / generatorLength;
                    bitangentDerivative = (generatorDerivative - bitangent * SdfVector.Dot(bitangent, generatorDerivative)) / generatorLength;
                    return;
                }
            }
            SdfVector normal = Normal(u, out SdfVector normalDerivative);
            SdfVector cross = SdfVector.Cross(velocity, normal);
            double length = cross.Length();
            if (!(length > 0))
            {
                // (the centerline along the normal: no direction across both)
                bitangent = RibbonBuilder.OrthogonalTo(velocity);
                bitangentDerivative = new SdfVector(0, 0, 0);
                return;
            }
            bitangent = cross / length;
            SdfVector crossDerivative = SdfVector.Cross(Acceleration(u), normal) + SdfVector.Cross(velocity, normalDerivative);
            bitangentDerivative = (crossDerivative - bitangent * SdfVector.Dot(bitangent, crossDerivative)) / length;
        }

        public SdfVector Bitangent(double u)
        {
            Frame(u, out SdfVector center, out SdfVector velocity, out double width, out double widthDerivative, out SdfVector bitangent, out SdfVector bitangentDerivative);
            return bitangent;
        }

        // The point of the ribbon at (u, v), v in [-1, 1] across the width
        public SdfVector Position(double u, double v)
        {
            Frame(u, out SdfVector center, out SdfVector velocity, out double width, out double widthDerivative, out SdfVector bitangent, out SdfVector bitangentDerivative);
            return center + bitangent * (v * width);
        }

        // The derivatives of the position with respect to u and v
        public void Derivatives(double u, double v, out SdfVector dpdu, out SdfVector dpdv)
        {
            Frame(u, out SdfVector center, out SdfVector velocity, out double width, out double widthDerivative, out SdfVector bitangent, out SdfVector bitangentDerivative);
            dpdu = velocity + (bitangent * widthDerivative + bitangentDerivative * width) * v;
            dpdv = bitangent * width;
        }

        // The hit of the ray (origin o, direction d of unit length, e1 and e2 of unit length across d and each other) with the
        // part of the ribbon whose u is in [a, b], nearer than t and farther than tMin: Newton's method from the point `along`
        // (0 at a, 1 at b) of the part. The part must be close to a flat strip (a leaf).
        public bool IntersectLeaf(SdfVector o, SdfVector d, SdfVector e1, SdfVector e2, double a, double b, double along, double tMin, ref double t, ref double hitU,
                                  ref double hitV)
        {
            // From the point of the chord that is the nearest to the ray. A ray at a low angle to the leaf can cross it twice
            // (the leaf turns by up to 25 degrees), and can be far from where the leaf is flat: when the solution is at less than
            // Low to the surface, or there is none, also from near the two ends of the leaf.
            int result = Newton(o, d, e1, e2, a, b, along, tMin, ref t, ref hitU, ref hitV, out double sine);
            bool found = result == 2;
            if (result == 0 || sine < Low)
            {
                found |= Newton(o, d, e1, e2, a, b, 0.05, tMin, ref t, ref hitU, ref hitV, out sine) == 2;
                found |= Newton(o, d, e1, e2, a, b, 0.95, tMin, ref t, ref hitU, ref hitV, out sine) == 2;
            }
            return found;
        }

        // The sine of the angle between a ray and the surface under which the leaf is also searched from its ends (30 degrees)
        private const double Low = 0.5;

        // Newton's method from `along`: 2 with a hit, 1 with a solution that is no hit (outside the leaf or the width, or not
        // between tMin and t), 0 without a solution; sine: of the angle between the ray and the surface at the solution
        private int Newton(SdfVector o, SdfVector d, SdfVector e1, SdfVector e2, double a, double b, double along, double tMin, ref double t, ref double hitU,
                           ref double hitV, out double sine)
        {
            sine = 0;
            double extent = b - a;
            double u = a + Math.Max(0, Math.Min(1, along)) * extent, v = 0;
            for (int iteration = 0; iteration < 16; ++iteration)
            {
                Frame(u, out SdfVector center, out SdfVector velocity, out double width, out double widthDerivative, out SdfVector bitangent,
                      out SdfVector bitangentDerivative);
                SdfVector q = center - o;
                double qx = SdfVector.Dot(e1, q), qy = SdfVector.Dot(e2, q);
                double bx = width * SdfVector.Dot(e1, bitangent), by = width * SdfVector.Dot(e2, bitangent);
                if (iteration == 0)
                {
                    // The v of the point of the generator of u that is the nearest to the ray
                    double square = bx * bx + by * by;
                    v = square > 0 ? Math.Max(-1.5, Math.Min(1.5, -(qx * bx + qy * by) / square)) : 0;
                }
                double gx = qx + v * bx, gy = qy + v * by;
                double distance = Math.Sqrt(q.LengthSquared());
                if (gx * gx + gy * gy <= Square(1e-10 * Math.Abs(width) + 1e-14 * distance))
                {
                    SdfVector normal = SdfVector.Cross(velocity + (bitangent * widthDerivative + bitangentDerivative * width) * v, bitangent);
                    double normalLength = normal.Length();
                    sine = normalLength > 0 ? Math.Abs(SdfVector.Dot(normal, d)) / normalLength : 0;
                    if (u < a - 1e-9 * extent || u > b + 1e-9 * extent || Math.Abs(v) > 1)
                    {
                        return 1;
                    }
                    double hit = SdfVector.Dot(q + bitangent * (v * width), d);
                    if (!(hit > tMin) || !(hit < t))
                    {
                        return 1;
                    }
                    t = hit;
                    hitU = Math.Max(0, Math.Min(1, u));
                    hitV = v;
                    return 2;
                }
                SdfVector du = velocity + (bitangent * widthDerivative + bitangentDerivative * width) * v;
                double ux = SdfVector.Dot(e1, du), uy = SdfVector.Dot(e2, du);
                double determinant = ux * by - bx * uy;
                // (the ray along the surface: the two derivatives are one line across it)
                if (Math.Abs(determinant) <= 1e-6 * Math.Sqrt((ux * ux + uy * uy) * (bx * bx + by * by)))
                {
                    return 0;
                }
                double stepU = (-gx * by + bx * gy) / determinant, stepV = (-ux * gy + uy * gx) / determinant;
                // (no step longer than the leaf, or than the ribbon is wide)
                stepU = Math.Max(-extent, Math.Min(extent, stepU));
                stepV = Math.Max(-2, Math.Min(2, stepV));
                u += stepU;
                v += stepV;
                if (u < a - extent || u > b + extent || Math.Abs(v) > 4)
                {
                    return 0;
                }
            }
            return 0;
        }

        // The ray (origin o, direction d of unit length) crosses the line of the ribbon at u (the line through the centerline along
        // the bitangent) where the ray, the bitangent and the step from the origin to the centerline are in one plane: where
        // f(u) = (C(u) - o) . (d x b(u)) is 0. f and its derivative at u.
        public void Crossing(SdfVector o, SdfVector d, double u, out double f, out double derivative)
        {
            Frame(u, out SdfVector center, out SdfVector velocity, out double width, out double widthDerivative, out SdfVector bitangent, out SdfVector bitangentDerivative);
            SdfVector q = center - o;
            SdfVector across = SdfVector.Cross(d, bitangent);
            f = SdfVector.Dot(q, across);
            derivative = SdfVector.Dot(velocity, across) + SdfVector.Dot(q, SdfVector.Cross(d, bitangentDerivative));
        }

        // The u between l and r where f is 0, for values fl and fr of f at l and r of opposite signs: Newton's method, with a
        // step to the middle whenever it would leave the part where f changes its sign
        // facing: of the crossing function of the strip that faces the ray (FacingCrossing)
        public double Root(SdfVector o, SdfVector d, double l, double fl, double r, double fr, bool facing)
        {
            double u = l - fl * (r - l) / (fr - fl);
            if (!(u > l && u < r))
            {
                u = 0.5 * (l + r);
            }
            for (int iteration = 0; iteration < 48; ++iteration)
            {
                double f, derivative;
                if (facing)
                {
                    FacingCrossing(o, d, u, out f, out derivative);
                }
                else
                {
                    Crossing(o, d, u, out f, out derivative);
                }
                if (f == 0)
                {
                    return u;
                }
                if ((f > 0) == (fl > 0))
                {
                    l = u;
                    fl = f;
                }
                else
                {
                    r = u;
                    fr = f;
                }
                double next = derivative != 0 ? u - f / derivative : 0.5 * (l + r);
                if (!(next > l && next < r))
                {
                    next = 0.5 * (l + r);
                }
                if (Math.Abs(next - u) <= 1e-13 || r - l <= 1e-14)
                {
                    return next;
                }
                u = next;
            }
            return u;
        }

        // The crossing function as a polynomial (polynomialroots.cs). It has the sign of f(u) = (C - o) . (d x b) wherever the
        // bitangent b is a polynomial in u before it is made of unit length (that length is positive, and does not move a
        // root): b ~ C' x N with the normal N the same all along (degree 2), in a line from n0 to n1 ("normals" "linear",
        // degree 3) or along the cubic curve of its values ("basis", degree 5), which with C - o of degree 3 gives a polynomial
        // of degree 5, 6 or 8. The normal that turns at one rate ("slerp") and the bitangents of "flipturns" are not
        // polynomials: -1. facing: of the strip that faces the ray, f(u) = (C - o) . (C' - d (d . C')), of degree 5 for any
        // normals. The ray is taken from its point nearest the middle of the curve (the squares of a far origin would take the
        // digits). coef: room for 9 values; the degree.
        public unsafe int CrossingPolynomial(SdfVector o, SdfVector d, bool facing, double* coef)
        {
            if (!facing && (like != null || (interpolation == Ribbon3Normals.Slerp && angle != 0)))
            {
                return -1;
            }
            SdfVector c1 = (p1 - p0) * 3, c2 = (p0 - 2 * p1 + p2) * 3, c3 = p3 - 3 * p2 + 3 * p1 - p0;
            SdfVector c0 = p0 - (o + d * SdfVector.Dot(Center(0.5) - o, d));
            // The derivative of the centerline
            SdfVector t0 = c1, t1 = c2 * 2, t2 = c3 * 3;
            if (facing)
            {
                // (the centerline across the ray)
                SdfVector e0 = c0 - d * SdfVector.Dot(c0, d), e1 = c1 - d * SdfVector.Dot(c1, d), e2 = c2 - d * SdfVector.Dot(c2, d), e3 = c3 - d * SdfVector.Dot(c3, d);
                coef[0] = SdfVector.Dot(e0, t0);
                coef[1] = SdfVector.Dot(e0, t1) + SdfVector.Dot(e1, t0);
                coef[2] = SdfVector.Dot(e0, t2) + SdfVector.Dot(e1, t1) + SdfVector.Dot(e2, t0);
                coef[3] = SdfVector.Dot(e1, t2) + SdfVector.Dot(e2, t1) + SdfVector.Dot(e3, t0);
                coef[4] = SdfVector.Dot(e2, t2) + SdfVector.Dot(e3, t1);
                coef[5] = SdfVector.Dot(e3, t2);
                return 5;
            }
            // (C - o) x d
            SdfVector q0 = SdfVector.Cross(c0, d), q1 = SdfVector.Cross(c1, d), q2 = SdfVector.Cross(c2, d), q3 = SdfVector.Cross(c3, d);
            // The normal before it is made of unit length, in powers of u
            SdfVector m0 = n0, m1 = default, m2 = default, m3 = default;
            int normalDegree = 0;
            if (interpolation == Ribbon3Normals.Linear)
            {
                m1 = n1 - n0;
                normalDegree = 1;
            }
            else if (interpolation == Ribbon3Normals.Basis)
            {
                m0 = v0;
                m1 = (v1 - v0) * 3;
                m2 = (v0 - 2 * v1 + v2) * 3;
                m3 = v3 - 3 * v2 + 3 * v1 - v0;
                normalDegree = 3;
            }
            // b ~ C' x N
            SdfVector b0 = SdfVector.Cross(t0, m0), b1 = SdfVector.Cross(t1, m0), b2 = SdfVector.Cross(t2, m0), b3 = default, b4 = default, b5 = default;
            if (normalDegree >= 1)
            {
                b1 = b1 + SdfVector.Cross(t0, m1);
                b2 = b2 + SdfVector.Cross(t1, m1);
                b3 = SdfVector.Cross(t2, m1);
            }
            if (normalDegree == 3)
            {
                b2 = b2 + SdfVector.Cross(t0, m2);
                b3 = b3 + SdfVector.Cross(t1, m2) + SdfVector.Cross(t0, m3);
                b4 = SdfVector.Cross(t2, m2) + SdfVector.Cross(t1, m3);
                b5 = SdfVector.Cross(t2, m3);
            }
            coef[0] = SdfVector.Dot(b0, q0);
            coef[1] = SdfVector.Dot(b0, q1) + SdfVector.Dot(b1, q0);
            coef[2] = SdfVector.Dot(b0, q2) + SdfVector.Dot(b1, q1) + SdfVector.Dot(b2, q0);
            coef[3] = SdfVector.Dot(b0, q3) + SdfVector.Dot(b1, q2) + SdfVector.Dot(b2, q1) + SdfVector.Dot(b3, q0);
            coef[4] = SdfVector.Dot(b1, q3) + SdfVector.Dot(b2, q2) + SdfVector.Dot(b3, q1) + SdfVector.Dot(b4, q0);
            coef[5] = SdfVector.Dot(b2, q3) + SdfVector.Dot(b3, q2) + SdfVector.Dot(b4, q1) + SdfVector.Dot(b5, q0);
            if (normalDegree == 0)
            {
                return 5;
            }
            coef[6] = SdfVector.Dot(b3, q3) + SdfVector.Dot(b4, q2) + SdfVector.Dot(b5, q1);
            if (normalDegree == 1)
            {
                return 6;
            }
            coef[7] = SdfVector.Dot(b4, q3) + SdfVector.Dot(b5, q2);
            coef[8] = SdfVector.Dot(b5, q3);
            return 8;
        }

        // The hit of the ray with the line of the ribbon at u, where the ray crosses it: within the width, farther than tMin and
        // nearer than t
        public bool HitAt(SdfVector o, SdfVector d, SdfVector e1, SdfVector e2, double u, double tMin, ref double t, ref double hitU, ref double hitV)
        {
            Frame(u, out SdfVector center, out SdfVector velocity, out double width, out double widthDerivative, out SdfVector bitangent, out SdfVector bitangentDerivative);
            SdfVector q = center - o;
            double qx = SdfVector.Dot(e1, q), qy = SdfVector.Dot(e2, q);
            double bx = width * SdfVector.Dot(e1, bitangent), by = width * SdfVector.Dot(e2, bitangent);
            double square = bx * bx + by * by;
            // (the ray along the line: within a millionth of its direction)
            if (!(square > 1e-12 * width * width))
            {
                return false;
            }
            double v = -(qx * bx + qy * by) / square;
            if (Math.Abs(v) > 1)
            {
                return false;
            }
            double hit = SdfVector.Dot(q + bitangent * (v * width), d);
            if (!(hit > tMin) || !(hit < t))
            {
                return false;
            }
            t = hit;
            hitU = u;
            hitV = v;
            return true;
        }

        // The look of the "curves" of pbrt-v4 in their ribbon style: no surface, but at every u a strip that faces the ray (across
        // the centerline and the ray), as wide as the ribbon would look to the ray were it only turned about the centerline:
        // the width times |n . d|. (That is the width a ray sees that is tilted across the ribbon; a ray tilted along the ribbon
        // sees the whole width of a flat one, and the strip is too narrow for it: the ribbon of pbrt-v4 thins and swells with the
        // direction it is seen from, which reads as a strip that turns.) The ray meets the strip of u where the centerline, seen
        // along the ray, is the nearest to it: f(u) = (C(u) - o) . (T(u) - d (d . T(u))) = 0. f and its derivative at u.
        public void FacingCrossing(SdfVector o, SdfVector d, double u, out double f, out double derivative)
        {
            SdfVector q = Center(u) - o, velocity = Velocity(u), acceleration = Acceleration(u);
            SdfVector across = velocity - d * SdfVector.Dot(d, velocity);
            f = SdfVector.Dot(q, across);
            derivative = SdfVector.Dot(velocity, across) + SdfVector.Dot(q, acceleration - d * SdfVector.Dot(d, acceleration));
        }

        // The direction that the strip that faces the ray takes for the normal of the ribbon at u: the normal; with the
        // bitangents of "flipturns" the direction across the centerline and that bitangent, shorter where the two are not at a
        // right angle (so that the strip narrows to nothing where "ribbon" does)
        public SdfVector FacingNormal(double u)
        {
            if (like == null)
            {
                return Normal(u, out SdfVector normalDerivative);
            }
            Frame(u, out SdfVector center, out SdfVector velocity, out double width, out double widthDerivative, out SdfVector bitangent, out SdfVector bitangentDerivative);
            double speed = velocity.Length();
            return speed > 0 ? SdfVector.Cross(velocity / speed, bitangent) : new SdfVector(0, 0, 0);
        }

        // The hit of the ray with the strip that faces it at u, where the centerline is the nearest to the ray: within the width
        // that the ray sees, farther than tMin and nearer than t. hitV: across that width, in [-1, 1].
        // narrowed: the width that the normal leaves for the ray (the ribbon style of pbrt-v4); otherwise the whole width (its
        // flat style, and the curves of RenderMan without normals as they were: a ribbon that turns to face whoever looks)
        public bool FacingHitAt(SdfVector o, SdfVector d, double u, bool narrowed, double tMin, ref double t, ref double hitU, ref double hitV)
        {
            SdfVector q = Center(u) - o;
            SdfVector facing = SdfVector.Cross(Velocity(u), d);
            double length = facing.Length();
            if (!(length > 0))
            {
                return false;
            }
            double offset = SdfVector.Dot(q, facing) / length;
            double seen = narrowed ? Width(u) * Math.Abs(SdfVector.Dot(FacingNormal(u), d)) : Width(u);
            if (!(seen > 0) || Math.Abs(offset) > seen)
            {
                return false;
            }
            double hit = SdfVector.Dot(q, d);
            if (!(hit > tMin) || !(hit < t))
            {
                return false;
            }
            t = hit;
            hitU = u;
            // (the side of the strip as the curves of pbrt-v4 count it)
            hitV = offset / seen;
            return true;
        }

        // Ribbon3Curve Private Methods
        private static double Square(double x)
        {
            return x * x;
        }

        // Ribbon3Curve Private Members
        private readonly SdfVector p0, p1, p2, p3;
        private readonly double w0, w1, w2, w3;
        private readonly SdfVector n0, n1;
        private readonly SdfVector axis;
        private readonly double angle;
        private readonly Ribbon3Normals interpolation;
        private readonly SdfVector v0, v1, v2, v3;
        private readonly RibbonSegment[] like;
        private readonly int index, count;
    }

    // Ribbon3Shape Definition
    public sealed unsafe class Ribbon3Shape : Shape
    {
        // Ribbon3Shape Public Methods
        // from, to: the part of the parameter of the curve that the shape is; leaves: the leaves that the part is cut into
        // roots: the ray is found from the roots of the crossing function along u (Ribbon3Curve.Crossing), not by Newton's method
        // in (u, v). facing: the look of the "curves" of pbrt-v4 (Ribbon3Curve.FacingCrossing): a strip that faces each ray;
        // narrowed: by the normal (its ribbon style), or of the whole width (its flat style); rounded: of the whole width with
        // the normal of a round strand (its cylinder style).
        // matrix: the first three rows of the matrix from object to render space (one for all the shapes of a strand), or null.
        public Ribbon3Shape(BezierSurfaceSettings settings, Ribbon3Curve curve, double from, double to, int leaves, int strandColors, bool solidAngleSampling,
                            bool roots, bool facing, bool narrowed, bool rounded, double[] matrix)
        {
            this.rounded = rounded;
            this.roots = roots || facing;
            this.facing = facing;
            this.narrowed = narrowed;
            this.settings = settings;
            this.curve = curve;
            this.from = from;
            this.to = to;
            this.leaves = leaves;
            this.strandColors = strandColors;
            this.solidAngleSampling = solidAngleSampling;
            // What is kept at the ends of the leaves and for each leaf, in one array (a shape is one of very many when the
            // strands are hair): the centerline and its derivative, and the second derivative (the strip that faces the ray)
            // or the bitangent and its derivative (the surface); the reach of the leaf, and the largest speed of the
            // centerline and rate of the bitangent over it
            stride = facing ? 9 : 12;
            data = new double[(leaves + 1) * stride + 3 * leaves];
            for (int i = 0; i <= leaves; ++i)
            {
                double u = from + (to - from) * i / leaves;
                curve.Frame(u, out SdfVector center, out SdfVector velocity, out double width, out double widthDerivative, out SdfVector bitangent,
                            out SdfVector bitangentDerivative);
                Keep(i, 0, center);
                Keep(i, 1, velocity);
                if (facing)
                {
                    Keep(i, 2, curve.Acceleration(u));
                }
                else
                {
                    Keep(i, 2, bitangent);
                    Keep(i, 3, bitangentDerivative);
                }
            }
            Bounds3f result = new Bounds3f();
            bool first = true;
            for (int i = 0; i < leaves; ++i)
            {
                double a = from + (to - from) * i / leaves, b = from + (to - from) * (i + 1) / leaves;
                // The centerline is within its second derivative times the square of the step over 8 of the chord, and the
                // half width within as much of the larger of those of the ends
                double step = b - a;
                double reach = Math.Max(Math.Abs(curve.Width(a)), Math.Abs(curve.Width(b))) + (curve.LargestWidthAcceleration() + curve.LargestAcceleration()) * step * step / 8;
                // The largest speed of the centerline and rate of the bitangent over the leaf (at 9 places, and half as much again)
                double speed = 0, rate = 0;
                for (int k = 0; k <= 8; ++k)
                {
                    curve.Frame(from + (to - from) * (i + k / 8.0) / leaves, out SdfVector center, out SdfVector velocity, out double width, out double widthDerivative,
                                out SdfVector bitangent, out SdfVector bitangentDerivative);
                    speed = Math.Max(speed, 1.5 * velocity.Length());
                    rate = Math.Max(rate, 1.5 * bitangentDerivative.Length());
                }
                int at = (leaves + 1) * stride + 3 * i;
                data[at] = reach;
                data[at + 1] = speed;
                data[at + 2] = rate;
                for (int corner = 0; corner < 16; ++corner)
                {
                    SdfVector center = (corner & 8) != 0 ? Node(i + 1, 0) : Node(i, 0);
                    Point3f point = settings.renderFromObject.Apply(new Point3f((Float)(center.x + ((corner & 1) != 0 ? reach : -reach)),
                                                                                (Float)(center.y + ((corner & 2) != 0 ? reach : -reach)),
                                                                                (Float)(center.z + ((corner & 4) != 0 ? reach : -reach))));
                    result = first ? new Bounds3f(point) : pbrt.Union(result, point);
                    first = false;
                }
            }
            Vector3f pad = new Vector3f(1e-4f, 1e-4f, 1e-4f) * pbrt.Length(result.pMax - result.pMin);
            bounds = new Bounds3f(result.pMin - pad, result.pMax + pad);
            this.matrix = matrix ?? Matrix(settings);
        }

        // The first three rows of the matrix from object to render space
        public static double[] Matrix(BezierSurfaceSettings settings)
        {
            SquareMatrix4 m = settings.renderFromObject.GetMatrix();
            double[] matrix = new double[12];
            for (int row = 0; row < 3; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    matrix[4 * row + column] = m[row][column];
                }
            }
            return matrix;
        }

        public override Bounds3f Bounds()
        {
            return bounds;
        }

        public override DirectionCone NormalBounds()
        {
            return DirectionCone.EntireSphere();
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            if (!Trace(ray, tMax, out double u, out double v, out double t, out double length))
            {
                return default;
            }
            SurfaceInteraction intr = facing ? FacingInteractionAt(u, v, t, ray) : InteractionAt(u, v, ray);
            return new ShapeIntersection(intr, (Float)(t / length));
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Trace(ray, tMax, out double u, out double v, out double t, out double length);
        }

        public override Float Area()
        {
            return (Float)Sampler().Area;
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            if (facing)
            {
                pbrt.LOG_FATAL("ribbon3: the looks \"curves\" and \"facing\" are strips that face each ray: they have no surface to sample, and cannot be lights.");
            }
            PatchAreaSampler areaSampler = Sampler();
            areaSampler.Sample(u, out double pu, out double pv, out double density);
            double jacobian = areaSampler.Jacobian(pu, pv);
            if (!(jacobian > 0))
            {
                return default;
            }
            return new ShapeSample(PointAt(from + pu * (to - from), 2 * pv - 1).intr, (Float)(density / jacobian));
        }

        // The density of Sample(u) at a point of the ribbon, by area: from the parameters of the point
        public override Float PDF(Interaction intr)
        {
            double u = (double)intr.uv.x * curve.Count - curve.Index;
            double along = Math.Max(0, Math.Min(1, (u - from) / (to - from)));
            double v = Math.Max(0, Math.Min(1, (double)intr.uv.y));
            double pdf = Sampler().AreaDensity(along, v);
            return double.IsNaN(pdf) || double.IsInfinity(pdf) ? 0 : (Float)pdf;
        }

        // From a lit point the position along the ribbon is chosen by the light that the ribbon sends to the point from there
        // (the weights at the nodes, joined by lines), across it uniformly; a tenth of the samples are those of the area
        // (as "ribbon2", ribbon2shape.cs)
        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            if (facing)
            {
                pbrt.LOG_FATAL("ribbon3: the looks \"curves\" and \"facing\" are strips that face each ray: they have no surface to sample, and cannot be lights.");
            }
            PatchAreaSampler areaSampler = Sampler();
            Span<double> weights = stackalloc double[Nodes + 1];
            double total = solidAngleSampling ? Weights(ctx.p(), weights) : 0;
            if (!(total > 0))
            {
                return PatchSampling.ToSolidAngle(Sample(u), ctx);
            }
            double pu, pv;
            if (u.x < Defence)
            {
                areaSampler.Sample(new Point2f((Float)Math.Min(u.x / Defence, 1 - 1e-7), u.y), out pu, out pv, out double areaDensity);
            }
            else
            {
                pu = AlongByWeights(weights, total, Math.Min((u.x - Defence) / (1 - Defence), 1 - 1e-12));
                pv = Math.Min((double)u.y, 1 - 1e-12);
            }
            double jacobian = areaSampler.Jacobian(pu, pv);
            if (!(jacobian > 0))
            {
                return default;
            }
            ShapeSample sample = PointAt(from + pu * (to - from), 2 * pv - 1);
            sample.pdf = (Float)(ParameterDensity(areaSampler, weights, total, pu, pv) / jacobian);
            return PatchSampling.ToSolidAngle(sample, ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            if (facing)
            {
                pbrt.LOG_FATAL("ribbon3: the looks \"curves\" and \"facing\" are strips that face each ray: they have no surface to sample, and cannot be lights.");
            }
            Ray ray = ctx.SpawnRay(wi);
            if (!Trace(ray, Infinity, out double u, out double v, out double t, out double length))
            {
                return 0;
            }
            PatchAreaSampler areaSampler = Sampler();
            Interaction at = PointAt(u, v).intr;
            double along = Math.Max(0, Math.Min(1, (u - from) / (to - from))), across = 0.5 * (v + 1);
            Span<double> weights = stackalloc double[Nodes + 1];
            double total = solidAngleSampling ? Weights(ctx.p(), weights) : 0;
            double areaPdf;
            if (total > 0)
            {
                double jacobian = areaSampler.Jacobian(along, across);
                if (!(jacobian > 0))
                {
                    return 0;
                }
                areaPdf = ParameterDensity(areaSampler, weights, total, along, across) / jacobian;
            }
            else
            {
                areaPdf = areaSampler.AreaDensity(along, across);
            }
            return PatchSampling.AreaToSolidAngle(areaPdf, at.n, at.p(), ctx, wi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ Ribbon3Shape curve: %d of %d ]", curve.Index, curve.Count);
        }

        // Ribbon3Shape Private Methods
        // The number of intervals of the shape's part of u that the weights of a lit point are taken at the ends of
        private const int Nodes = 16;

        // The share of the samples for a lit point that are those of the area
        private const double Defence = 0.1;

        // The weights W(u) at the nodes for the lit point (render space), and their integral over the shape's part of u
        private double Weights(Point3f lit, Span<double> weights)
        {
            SdfVector x = new SdfVector(lit.x, lit.y, lit.z);
            for (int i = 0; i <= Nodes; ++i)
            {
                double u = (double)i / Nodes;
                Tangents(u, out SdfVector center, out SdfVector du, out SdfVector dv);
                SdfVector normal = SdfVector.Cross(du, dv);
                double jacobian = normal.Length();
                SdfVector toLit = x - center;
                double distance = toLit.Length();
                // J |n . D| / |D|^3 with the unit normal n: the cross product of the derivatives is J n
                weights[i] = jacobian > 0 && distance > 0 ? Math.Abs(SdfVector.Dot(normal, toLit)) / (distance * distance * distance) : 0;
            }
            double total = 0;
            for (int i = 0; i < Nodes; ++i)
            {
                total += 0.5 * (weights[i] + weights[i + 1]) / Nodes;
            }
            return double.IsNaN(total) || double.IsInfinity(total) ? 0 : total;
        }

        // The parameter for a number in [0, 1), with the density of the weights joined by lines
        private static double AlongByWeights(Span<double> weights, double total, double choice)
        {
            double target = choice * total;
            for (int i = 0; i < Nodes; ++i)
            {
                double mass = 0.5 * (weights[i] + weights[i + 1]) / Nodes;
                if (target < mass || i == Nodes - 1)
                {
                    double a = weights[i], b = weights[i + 1];
                    double x = mass > 0 ? Math.Max(0, Math.Min(1, target / mass)) : 0.5;
                    double within = Math.Abs(b - a) <= 1e-12 * (a + b) ? x : (Math.Sqrt(Math.Max(0, a * a + (b * b - a * a) * x)) - a) / (b - a);
                    return Math.Min((i + Math.Max(0, Math.Min(1, within))) / Nodes, 1 - 1e-12);
                }
                target -= mass;
            }
            return 0.5;
        }

        // The density of the parameters in [0, 1]^2 of the samples for a lit point
        private static double ParameterDensity(PatchAreaSampler areaSampler, Span<double> weights, double total, double u, double v)
        {
            double position = Math.Max(0, Math.Min(1, u)) * Nodes;
            int bin = Math.Min(Nodes - 1, (int)position);
            double within = position - bin;
            return (1 - Defence) * ((1 - within) * weights[bin] + within * weights[bin + 1]) / total + Defence * areaSampler.Density(u, v);
        }

        // The nearest hit of the ray: u of the curve, v in [-1, 1], the distance t along the direction of unit length in
        // object space, and the length of the direction
        private bool Trace(Ray ray, Float tMax, out double u, out double v, out double t, out double length)
        {
            u = 0;
            v = 0;
            t = 0;
            Point3f o = settings.objectFromRender.Apply(ray.o);
            Vector3f d = settings.objectFromRender.Apply(ray.d);
            length = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
            if (length == 0 || double.IsNaN(length))
            {
                return false;
            }
            SdfVector origin = new SdfVector(o.x, o.y, o.z);
            SdfVector direction = new SdfVector(d.x / length, d.y / length, d.z / length);
            SdfVector e1 = RibbonBuilder.OrthogonalTo(direction);
            SdfVector e2 = SdfVector.Cross(direction, e1);
            t = float.IsPositiveInfinity(tMax) ? 1e30 : tMax * length;
            bool found = false;
            if (Polynomial && TracePolynomial(origin, direction, e1, e2, ref t, ref u, ref v, out found))
            {
                return found;
            }
            for (int i = 0; i < leaves; ++i)
            {
                // The chord of the leaf across the ray, and the point of it that is the nearest to the ray
                SdfVector start = Node(i, 0) - origin, end = Node(i + 1, 0) - origin;
                double ax = SdfVector.Dot(e1, start), ay = SdfVector.Dot(e2, start);
                double cx = SdfVector.Dot(e1, end) - ax, cy = SdfVector.Dot(e2, end) - ay;
                double square = cx * cx + cy * cy;
                double along = square > 0 ? Math.Max(0, Math.Min(1, -(ax * cx + ay * cy) / square)) : 0.5;
                double nx = ax + along * cx, ny = ay + along * cy;
                if (nx * nx + ny * ny > Leaf(i, 0) * Leaf(i, 0))
                {
                    continue;
                }
                double a = from + (to - from) * i / leaves, b = from + (to - from) * (i + 1) / leaves;
                if (!roots)
                {
                    found |= curve.IntersectLeaf(origin, direction, e1, e2, a, b, along, 0, ref t, ref u, ref v);
                    continue;
                }
                // The crossing function and its derivative at the two ends of the leaf, from what is kept for them
                SdfVector acrossStart, acrossEnd;
                if (facing)
                {
                    acrossStart = Node(i, 1) - direction * SdfVector.Dot(direction, Node(i, 1));
                    acrossEnd = Node(i + 1, 1) - direction * SdfVector.Dot(direction, Node(i + 1, 1));
                }
                else
                {
                    acrossStart = SdfVector.Cross(direction, Node(i, 2));
                    acrossEnd = SdfVector.Cross(direction, Node(i + 1, 2));
                }
                double fa = SdfVector.Dot(start, acrossStart), fb = SdfVector.Dot(end, acrossEnd);
                if ((fa > 0) != (fb > 0))
                {
                    found |= HitAt(origin, direction, e1, e2, curve.Root(origin, direction, a, fa, b, fb, facing), ref t, ref u, ref v);
                    continue;
                }
                // The same sign at both ends: the leaf is searched for crossings in pairs (Search)
                double da = SdfVector.Dot(Node(i, 1), acrossStart), db = SdfVector.Dot(Node(i + 1, 1), acrossEnd);
                if (facing)
                {
                    da += SdfVector.Dot(start, Node(i, 2) - direction * SdfVector.Dot(direction, Node(i, 2)));
                    db += SdfVector.Dot(end, Node(i + 1, 2) - direction * SdfVector.Dot(direction, Node(i + 1, 2)));
                }
                else
                {
                    da += SdfVector.Dot(start, SdfVector.Cross(direction, Node(i, 3)));
                    db += SdfVector.Dot(end, SdfVector.Cross(direction, Node(i + 1, 3)));
                }
                // f cannot change by more than `steepest` for a step of 1 in u over the leaf (for a ray that is within the reach
                // of the leaf, which is at most the reach and the chord from any point of its centerline)
                double far = Leaf(i, 0) + (Node(i + 1, 0) - Node(i, 0)).Length();
                double steepest = facing ? Leaf(i, 1) * Leaf(i, 1) + far * curve.LargestAcceleration() : Leaf(i, 1) + far * Leaf(i, 2);
                found |= Search(origin, direction, e1, e2, a, fa, da, b, fb, db, steepest, 4, ref t, ref u, ref v);
            }
            return found;
        }

        // The crossings by the roots of the polynomial of the crossing function (Ribbon3Curve.CrossingPolynomial), over the leaves
        // within whose reach the ray passes; each root is then brought to that of the crossing function itself by Newton's
        // steps. False where the crossing function of the curve is no polynomial (the leaves are then searched as before).
        private unsafe bool TracePolynomial(SdfVector origin, SdfVector direction, SdfVector e1, SdfVector e2, ref double t, ref double u, ref double v, out bool found)
        {
            found = false;
            double* coef = stackalloc double[9];
            // (the leaves in reach first: most rays that come to the bounds have none, and need no polynomial)
            int first = -1, last = -1;
            for (int i = 0; i < leaves; ++i)
            {
                SdfVector start = Node(i, 0) - origin, end = Node(i + 1, 0) - origin;
                double ax = SdfVector.Dot(e1, start), ay = SdfVector.Dot(e2, start);
                double cx = SdfVector.Dot(e1, end) - ax, cy = SdfVector.Dot(e2, end) - ay;
                double square = cx * cx + cy * cy;
                double along = square > 0 ? Math.Max(0, Math.Min(1, -(ax * cx + ay * cy) / square)) : 0.5;
                double nx = ax + along * cx, ny = ay + along * cy;
                if (nx * nx + ny * ny > Leaf(i, 0) * Leaf(i, 0))
                {
                    continue;
                }
                first = first < 0 ? i : first;
                last = i;
            }
            int degree = curve.CrossingPolynomial(origin, direction, facing, coef);
            if (degree < 0)
            {
                return false;
            }
            if (first < 0)
            {
                return true;
            }
            double low = from + (to - from) * first / leaves, high = from + (to - from) * (last + 1) / leaves;
            double* crossings = stackalloc double[8];
            int count = PolynomialRoots.Roots(coef, degree, low, high, 1e-9, crossings);
            for (int i = 0; i < count; ++i)
            {
                double root = crossings[i];
                for (int step = 0; step < 2; ++step)
                {
                    double f, derivative;
                    if (facing)
                    {
                        curve.FacingCrossing(origin, direction, root, out f, out derivative);
                    }
                    else
                    {
                        curve.Crossing(origin, direction, root, out f, out derivative);
                    }
                    double next = derivative != 0 ? root - f / derivative : root;
                    if (!(Math.Abs(next - crossings[i]) <= 1e-6) || next < from || next > to)
                    {
                        break;
                    }
                    root = next;
                }
                found |= HitAt(origin, direction, e1, e2, root, ref t, ref u, ref v);
            }
            return true;
        }

        // The vector `which` kept for the end `node` of a leaf (0: the centerline, 1: its derivative, 2: its second derivative or
        // the bitangent, 3: the derivative of the bitangent)
        private SdfVector Node(int node, int which)
        {
            int at = node * stride + 3 * which;
            return new SdfVector(data[at], data[at + 1], data[at + 2]);
        }

        private void Keep(int node, int which, SdfVector vector)
        {
            int at = node * stride + 3 * which;
            data[at] = vector.x;
            data[at + 1] = vector.y;
            data[at + 2] = vector.z;
        }

        // The number `which` kept for the leaf (0: its reach, 1: the largest speed, 2: the largest rate of the bitangent)
        private double Leaf(int leaf, int which)
        {
            return data[(leaves + 1) * stride + 3 * leaf + which];
        }

        // The crossings of the part [a, b] of a leaf over which f has one sign at both ends (fa, fb; da, db: its derivative
        // there). There is none when f cannot come down to 0 from both ends (it changes by at most `steepest` for a step of 1).
        // Otherwise f is taken at the middle. When the cubic through the two ends foretells that value well and stays clear of
        // 0, there is none either. Otherwise the part is halved, up to `depth` times: a half over which f changes its sign has
        // a crossing; after the last halving f is followed to where it turns back (its derivative changes its sign), and has
        // two crossings when it is beyond 0 there. A ray at a low angle to the ribbon is what makes this needed: it can cross a leaf twice, and f can
        // turn more than once over a leaf.
        private bool Search(SdfVector origin, SdfVector direction, SdfVector e1, SdfVector e2, double a, double fa, double da, double b, double fb, double db,
                            double steepest, int depth, ref double t, ref double u, ref double v)
        {
            if (Math.Abs(fa) + Math.Abs(fb) > steepest * (b - a))
            {
                return false;
            }
            bool found = false;
            double fm, dm;
            if (depth > 0)
            {
                double middle = 0.5 * (a + b);
                Function(origin, direction, middle, out fm, out dm);
                if ((fm > 0) != (fa > 0))
                {
                    found |= HitAt(origin, direction, e1, e2, curve.Root(origin, direction, a, fa, middle, fm, facing), ref t, ref u, ref v);
                    found |= HitAt(origin, direction, e1, e2, curve.Root(origin, direction, middle, fm, b, fb, facing), ref t, ref u, ref v);
                    return found;
                }
                // The cubic through the values and derivatives at the two ends says where f goes in between; how far it is off at
                // the middle says how far it can be trusted. No crossing when the cubic stays clear of 0 by eight times that.
                double m0 = (b - a) * da, m1 = (b - a) * db;
                double off = Math.Abs(fm - (0.5 * (fa + fb) + 0.125 * (m0 - m1)));
                double side = fa > 0 ? 1 : -1;
                double nearest = Math.Min(side * fa, side * fb);
                double c2 = 6 * fa + 3 * m0 - 6 * fb + 3 * m1, c1 = -6 * fa - 4 * m0 + 6 * fb - 2 * m1, c0 = m0;
                double discriminant = c1 * c1 - 4 * c2 * c0;
                if (discriminant >= 0)
                {
                    double root = Math.Sqrt(discriminant);
                    for (int which = 0; which < 2; ++which)
                    {
                        double s = c2 != 0 ? (-c1 + (which == 0 ? root : -root)) / (2 * c2) : (c1 != 0 ? -c0 / c1 : -1);
                        if (s > 0 && s < 1)
                        {
                            double value = (2 * s * s * s - 3 * s * s + 1) * fa + (s * s * s - 2 * s * s + s) * m0 + (-2 * s * s * s + 3 * s * s) * fb + (s * s * s - s * s) * m1;
                            nearest = Math.Min(nearest, side * value);
                        }
                    }
                }
                if (nearest > 8 * off + 1e-12 * (Math.Abs(fa) + Math.Abs(fb)))
                {
                    return false;
                }
                found |= Search(origin, direction, e1, e2, a, fa, da, middle, fm, dm, steepest, depth - 1, ref t, ref u, ref v);
                found |= Search(origin, direction, e1, e2, middle, fm, dm, b, fb, db, steepest, depth - 1, ref t, ref u, ref v);
                return found;
            }
            // f turns back when it heads for 0 at the start and away from it at the end
            if ((da > 0) == (db > 0) || (fa > 0) == (da > 0))
            {
                return false;
            }
            double l = a, r = b;
            for (int iteration = 0; iteration < 30 && r - l > 1e-12; ++iteration)
            {
                double middle = 0.5 * (l + r);
                Function(origin, direction, middle, out fm, out dm);
                if ((fm > 0) != (fa > 0))
                {
                    found |= HitAt(origin, direction, e1, e2, curve.Root(origin, direction, a, fa, middle, fm, facing), ref t, ref u, ref v);
                    found |= HitAt(origin, direction, e1, e2, curve.Root(origin, direction, middle, fm, b, fb, facing), ref t, ref u, ref v);
                    break;
                }
                if ((dm > 0) == (da > 0))
                {
                    l = middle;
                }
                else
                {
                    r = middle;
                }
            }
            return found;
        }

        // The crossing function of the look of the shape, and its derivative, at u
        private void Function(SdfVector origin, SdfVector direction, double u, out double f, out double derivative)
        {
            if (facing)
            {
                curve.FacingCrossing(origin, direction, u, out f, out derivative);
            }
            else
            {
                curve.Crossing(origin, direction, u, out f, out derivative);
            }
        }

        // The hit of the ray where it crosses the ribbon at the root u: with the surface, or with the strip that faces the ray
        private bool HitAt(SdfVector origin, SdfVector direction, SdfVector e1, SdfVector e2, double root, ref double t, ref double u, ref double v)
        {
            return facing ? curve.FacingHitAt(origin, direction, root, narrowed, 0, ref t, ref u, ref v) : curve.HitAt(origin, direction, e1, e2, root, 0, ref t, ref u, ref v);
        }

        // The hit of the strip that faces the ray, as the curves of pbrt-v4 give it: the point of the ray, the derivative along
        // the centerline, and across it the direction across the normal and the centerline with the width that the ray sees (so
        // that the normal is that of the ribbon); the error of the point is that width, which keeps the rays that leave the hit
        // from meeting the strip again
        private SurfaceInteraction FacingInteractionAt(double u, double v, double t, Ray ray)
        {
            Point3f o = settings.objectFromRender.Apply(ray.o);
            Vector3f d = settings.objectFromRender.Apply(ray.d);
            double length = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
            SdfVector direction = new SdfVector(d.x / length, d.y / length, d.z / length);
            SdfVector position = new SdfVector(o.x, o.y, o.z) + direction * t;
            SdfVector velocity = curve.Velocity(u);
            double seen = curve.Width(u);
            // (the whole width: the strip is across the ray, and its normal toward the ray)
            SdfVector across = SdfVector.Cross(direction, velocity);
            if (rounded && across.LengthSquared() > 0 && velocity.LengthSquared() > 0)
            {
                // The cylinder style of pbrt-v4: the strip as it is, with the direction across it turned about the centerline by
                // a right angle from one edge to the other, so that the normal is that of a round strand
                SdfVector axis = velocity.Normalized();
                double turn = -0.5 * Math.PI * v;
                SdfVector flat = across.Normalized();
                across = flat * Math.Cos(turn) + SdfVector.Cross(axis, flat) * Math.Sin(turn);
            }
            if (narrowed)
            {
                SdfVector normal = curve.FacingNormal(u);
                seen *= Math.Abs(SdfVector.Dot(normal, direction));
                SdfVector acrossNormal = SdfVector.Cross(normal, velocity);
                if (acrossNormal.LengthSquared() > 0)
                {
                    across = acrossNormal;
                }
            }
            across = across.LengthSquared() > 0 ? across.Normalized() * (2 * seen) : new SdfVector(0, 0, 0);
            Transform renderFromObject = settings.renderFromObject;
            Point3f p = renderFromObject.Apply(new Point3f((Float)position.x, (Float)position.y, (Float)position.z));
            Vector3f dpdu = renderFromObject.Apply(new Vector3f((Float)(velocity.x * curve.Count), (Float)(velocity.y * curve.Count), (Float)(velocity.z * curve.Count)));
            Vector3f dpdv = renderFromObject.Apply(new Vector3f((Float)across.x, (Float)across.y, (Float)across.z));
            Point2f uv = new Point2f((Float)((curve.Index + u) / curve.Count), (Float)(0.5 * (v + 1)));
            bool flipNormal = settings.reverseOrientation ^ settings.transformSwapsHandedness;
            Float reach = (Float)(2 * seen);
            return new SurfaceInteraction(new Point3fi(p, new Vector3f(reach, reach, reach)), uv, -ray.d, dpdu, dpdv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ray.time,
                                          flipNormal, strandColors);
        }

        // The point of the shape at (u, v) in [0, 1]^2 in render space, in double precision (the densities are from differences
        // of such points)
        private SdfVector Position(double u, double v)
        {
            SdfVector p = curve.Position(from + u * (to - from), 2 * v - 1);
            return new SdfVector(matrix[0] * p.x + matrix[1] * p.y + matrix[2] * p.z + matrix[3], matrix[4] * p.x + matrix[5] * p.y + matrix[6] * p.z + matrix[7],
                                 matrix[8] * p.x + matrix[9] * p.y + matrix[10] * p.z + matrix[11]);
        }

        // The point of the centerline of the shape at u in [0, 1] in render space, and the derivatives of the position there
        // with respect to the parameters in [0, 1]^2
        private void Tangents(double u, out SdfVector center, out SdfVector du, out SdfVector dv)
        {
            double at = from + u * (to - from);
            curve.Frame(at, out SdfVector p, out SdfVector velocity, out double width, out double widthDerivative, out SdfVector bitangent,
                        out SdfVector bitangentDerivative);
            SdfVector a = velocity * (to - from), b = bitangent * (2 * width);
            center = new SdfVector(matrix[0] * p.x + matrix[1] * p.y + matrix[2] * p.z + matrix[3], matrix[4] * p.x + matrix[5] * p.y + matrix[6] * p.z + matrix[7],
                                   matrix[8] * p.x + matrix[9] * p.y + matrix[10] * p.z + matrix[11]);
            du = new SdfVector(matrix[0] * a.x + matrix[1] * a.y + matrix[2] * a.z, matrix[4] * a.x + matrix[5] * a.y + matrix[6] * a.z,
                               matrix[8] * a.x + matrix[9] * a.y + matrix[10] * a.z);
            dv = new SdfVector(matrix[0] * b.x + matrix[1] * b.y + matrix[2] * b.z, matrix[4] * b.x + matrix[5] * b.y + matrix[6] * b.z,
                               matrix[8] * b.x + matrix[9] * b.y + matrix[10] * b.z);
        }

        private PatchAreaSampler Sampler()
        {
            PatchAreaSampler result = sampler;
            if (result == null)
            {
                result = new PatchAreaSampler(Position, 16);
                sampler = result;
            }
            return result;
        }

        // The geometry at the parameters (u of the curve, v in [-1, 1]): the position in render space, the derivatives and the
        // texture coordinates (along the whole ribbon and across its width)
        private void Geometry(double u, double v, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv)
        {
            SdfVector position = curve.Position(u, v);
            curve.Derivatives(u, v, out SdfVector du, out SdfVector dv);
            Transform renderFromObject = settings.renderFromObject;
            p = renderFromObject.Apply(new Point3f((Float)position.x, (Float)position.y, (Float)position.z));
            dpdu = renderFromObject.Apply(new Vector3f((Float)(du.x * curve.Count), (Float)(du.y * curve.Count), (Float)(du.z * curve.Count)));
            dpdv = renderFromObject.Apply(new Vector3f((Float)(dv.x * 2), (Float)(dv.y * 2), (Float)(dv.z * 2)));
            uv = new Point2f((Float)((curve.Index + u) / curve.Count), (Float)(0.5 * (v + 1)));
        }

        private SurfaceInteraction InteractionAt(double u, double v, Ray ray)
        {
            Geometry(u, v, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv);
            bool flipNormal = settings.reverseOrientation ^ settings.transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new SurfaceInteraction(new Point3fi(p, pError), uv, -ray.d, dpdu, dpdv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ray.time, flipNormal,
                                          strandColors);
        }

        // The sample of the point at (u of the curve, v), with the normal of the surface (the density is set by the caller)
        private ShapeSample PointAt(double u, double v)
        {
            Geometry(u, v, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv);
            Normal3f n = pbrt.Normalize((Normal3f)pbrt.Cross(dpdu, dpdv));
            if (settings.reverseOrientation ^ settings.transformSwapsHandedness)
            {
                n = -n;
            }
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, uv), 0);
        }

        // Ribbon3Shape Private Members
        private readonly BezierSurfaceSettings settings;
        private readonly Ribbon3Curve curve;
        private readonly double from, to;
        private readonly int leaves;
        // What is kept at the ends of the leaves and for each leaf (Node, Leaf), and the doubles kept for each end
        private readonly double[] data;
        private readonly int stride;
        private readonly bool roots;
        // The crossings by the roots of a polynomial where the crossing function is one ("intersector" "polynomial"); the search
        // of `roots` otherwise
        public bool Polynomial;
        private readonly bool facing;
        private readonly bool narrowed;
        private readonly bool rounded;
        // The number of the registered colors of the strand (StrandColors), the face index of the hits; 0 without
        private readonly int strandColors;
        private readonly bool solidAngleSampling;
        private readonly Bounds3f bounds;
        private readonly double[] matrix;
        private PatchAreaSampler sampler;
    }

    // Ribbons3 Definition
    public static class Ribbons3
    {
        // The most leaves of one shape: a whole curve (64 leaves at most). Shapes of fewer leaves have tighter bounds, but
        // rendered no faster, and as lights they are many small ones, which the sampling for a lit point does little for.
        private const int LeavesPerShape = 64;

        // The number of leaves of the curve: the least power of two (up to 64) with which neither the centerline nor the
        // bitangent turns by more than the turn of the method (25 or 45 degrees) over a leaf, or by half of it over either half of one
        public static int Leaves(Ribbon3Curve curve, bool roots)
        {
            double half = roots ? HalfTurnRoots : HalfTurn;
            for (int leaves = 1; leaves < 64; leaves *= 2)
            {
                bool flat = true;
                for (int i = 0; i < 2 * leaves && flat; ++i)
                {
                    double a = (double)i / (2 * leaves), b = (double)(i + 1) / (2 * leaves);
                    SdfVector ta = curve.Velocity(a), tb = curve.Velocity(b);
                    double scale = ta.Length() * tb.Length();
                    // (half of the turn that a leaf may have, over half a leaf)
                    flat = SdfVector.Dot(curve.Bitangent(a), curve.Bitangent(b)) >= half && (!(scale > 0) || SdfVector.Dot(ta, tb) >= half * scale);
                }
                if (flat)
                {
                    return leaves;
                }
            }
            return 64;
        }

        // The cosine of half the turn over a leaf for Newton's method in (u, v) (25 degrees over a leaf)
        private const double HalfTurn = 0.9762960071199334;

        // The cosine of half the turn over a leaf for the roots of the crossing function (45 degrees over a leaf: the leaf need
        // not be flat, only such that the function turns back at most once over it)
        private const double HalfTurnRoots = 0.9238795325112867;

        // The curves of a strand of cubic Bezier curves (four values for each: the coordinates of the points, the half widths,
        // and the coordinates of the normals or null). Without normals: the normal of the plane of the start of the strand (of
        // the first curve that is not straight), carried along the strand without twist, and started again where two curves
        // do not meet.
        //
        //
        // like: the segments of "ribbon2" with "flipturns" for the same strand, `parts` for each curve, or null: each curve takes
        // the bitangents of its segments (the behavior of "ribbon" where a curve turns by more than a right angle).
        //
        // interpolation: how the normals given go from one end of a curve to the other (Ribbon3Normals).
        public static List<Ribbon3Curve> Curves(double[] x, double[] y, double[] z, double[] w, double[] nx, double[] ny, double[] nz, Ribbon3Normals interpolation,
                                                List<RibbonSegment> like, int parts)
        {
            int count = x.Length / 4;
            List<Ribbon3Curve> curves = new List<Ribbon3Curve>();
            SdfVector carried = new SdfVector(0, 0, 0);
            SdfVector last = new SdfVector(0, 0, 0);
            double size = 0;
            for (int i = 0; i < x.Length; ++i)
            {
                size = Math.Max(size, Math.Max(Math.Abs(x[i] - x[0]), Math.Max(Math.Abs(y[i] - y[0]), Math.Abs(z[i] - z[0]))));
            }
            for (int s = 0; s < count; ++s)
            {
                SdfVector[] points = new SdfVector[4];
                double[] widths = new double[4];
                for (int k = 0; k < 4; ++k)
                {
                    points[k] = new SdfVector(x[4 * s + k], y[4 * s + k], z[4 * s + k]);
                    widths[k] = Math.Max(0, w[4 * s + k]);
                }
                SdfVector n0, n1;
                SdfVector[] values = null;
                if (nx != null)
                {
                    n0 = new SdfVector(nx[4 * s], ny[4 * s], nz[4 * s]);
                    n1 = new SdfVector(nx[4 * s + 3], ny[4 * s + 3], nz[4 * s + 3]);
                    values = new SdfVector[4];
                    for (int k = 0; k < 4; ++k)
                    {
                        values[k] = new SdfVector(nx[4 * s + k], ny[4 * s + k], nz[4 * s + k]);
                    }
                }
                else
                {
                    if (s == 0 || (points[0] - last).Length() > 1e-9 * size)
                    {
                        carried = PlaneNormal(x, y, z, s, count, carried);
                    }
                    n0 = Across(carried, Tangent(points, 0));
                    // The normal carried along the curve: turned as the tangent turns, in 16 steps
                    SdfVector before = Tangent(points, 0);
                    SdfVector normal = n0;
                    for (int i = 1; i <= 16; ++i)
                    {
                        SdfVector after = Tangent(points, i / 16.0);
                        normal = Carry(normal, before, after);
                        before = after;
                    }
                    n1 = Across(normal, before);
                    carried = n1;
                }
                if (!(n0.LengthSquared() > 0))
                {
                    n0 = RibbonBuilder.OrthogonalTo(Tangent(points, 0));
                }
                if (!(n1.LengthSquared() > 0))
                {
                    n1 = n0;
                }
                last = points[3];
                RibbonSegment[] segments = null;
                if (like != null)
                {
                    segments = new RibbonSegment[parts];
                    for (int k = 0; k < parts; ++k)
                    {
                        segments[k] = like[s * parts + k];
                    }
                }
                curves.Add(new Ribbon3Curve(points, widths, n0, n1, interpolation, values, segments, s, count));
            }
            return curves;
        }

        // The direction of the cubic curve of the points at u (of unit length; that of the chord where the derivative is 0)
        private static SdfVector Tangent(SdfVector[] points, double u)
        {
            double s = 1 - u;
            SdfVector velocity = (points[1] - points[0]) * (3 * s * s) + (points[2] - points[1]) * (6 * u * s) + (points[3] - points[2]) * (3 * u * u);
            if (!(velocity.LengthSquared() > 0))
            {
                velocity = points[3] - points[0];
            }
            return velocity.LengthSquared() > 0 ? velocity.Normalized() : new SdfVector(1, 0, 0);
        }

        // The part of the vector across the tangent, of unit length (a vector across the tangent when there is none)
        private static SdfVector Across(SdfVector vector, SdfVector tangent)
        {
            SdfVector across = vector - tangent * SdfVector.Dot(vector, tangent);
            return across.LengthSquared() > 1e-24 ? across.Normalized() : RibbonBuilder.OrthogonalTo(tangent);
        }

        // The normal turned by the least turn that takes the direction `before` to the direction `after`
        private static SdfVector Carry(SdfVector normal, SdfVector before, SdfVector after)
        {
            SdfVector axis = SdfVector.Cross(before, after);
            double sine = axis.Length(), cosine = SdfVector.Dot(before, after);
            if (!(sine > 1e-14))
            {
                return normal;
            }
            axis = axis / sine;
            return normal * cosine + SdfVector.Cross(axis, normal) * sine + axis * (SdfVector.Dot(axis, normal) * (1 - cosine));
        }

        // The normal of the plane of the first curve from `from` that is not straight (the first and second derivatives at its
        // start, or its chords), on the side of `side`; a vector across the curve when all are straight
        private static SdfVector PlaneNormal(double[] x, double[] y, double[] z, int from, int count, SdfVector side)
        {
            for (int s = from; s < count; ++s)
            {
                SdfVector a = new SdfVector(x[4 * s + 1] - x[4 * s], y[4 * s + 1] - y[4 * s], z[4 * s + 1] - z[4 * s]);
                SdfVector b = new SdfVector(x[4 * s + 2] - x[4 * s + 1], y[4 * s + 2] - y[4 * s + 1], z[4 * s + 2] - z[4 * s + 1]);
                SdfVector c = new SdfVector(x[4 * s + 3] - x[4 * s + 2], y[4 * s + 3] - y[4 * s + 2], z[4 * s + 3] - z[4 * s + 2]);
                SdfVector normal = SdfVector.Cross(a, b);
                if (!(normal.LengthSquared() > 1e-24 * a.LengthSquared() * b.LengthSquared()))
                {
                    normal = SdfVector.Cross(a + b, c);
                }
                if (normal.LengthSquared() > 1e-24 * (a + b).LengthSquared() * Math.Max(b.LengthSquared(), c.LengthSquared()))
                {
                    normal = normal.Normalized();
                    return SdfVector.Dot(normal, side) < 0 ? -normal : normal;
                }
            }
            SdfVector along = new SdfVector(x[4 * from + 3] - x[4 * from], y[4 * from + 3] - y[4 * from], z[4 * from + 3] - z[4 * from]);
            return RibbonBuilder.OrthogonalTo(along.LengthSquared() > 0 ? along : new SdfVector(1, 0, 0));
        }

        // The cubic Bezier values of the curves (four for each) as a chain: the last value of a curve is the first of the next
        private static double[] Chain(double[] values)
        {
            int curves = values.Length / 4;
            double[] chain = new double[3 * curves + 1];
            for (int s = 0; s < curves; ++s)
            {
                chain[3 * s] = values[4 * s];
                chain[3 * s + 1] = values[4 * s + 1];
                chain[3 * s + 2] = values[4 * s + 2];
            }
            chain[3 * curves] = values[values.Length - 1];
            return chain;
        }

        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc)
        {
            List<Point3f> pointList = parameters.GetPoint3fArray("P");
            List<Normal3f> normalList = parameters.GetNormal3fArray("N");
            List<float> widthList = parameters.GetFloatArray("width");
            StrandBasis basis = StrandBasis.Read(parameters, "ribbon3", "b-spline", 2, loc);
            string problem = basis.Check(pointList.Count);
            if (problem != null)
            {
                pbrt.ErrorExit(loc, "ribbon3: \"P\": " + problem + ".");
            }
            if (normalList.Count != 0 && normalList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "ribbon3: \"N\" needs a normal for each point.");
            }
            if (widthList.Count != 0 && widthList.Count != 1 && widthList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "ribbon3: \"width\" needs one value or one for each point.");
            }
            List<RGB> colorList = parameters.GetRGBArray("Cs");
            if (colorList.Count > 1 && colorList.Count != pointList.Count)
            {
                pbrt.ErrorExit(loc, "ribbon3: \"Cs\" needs one color or one for each point.");
            }
            int count = pointList.Count;
            double[] x = new double[count], y = new double[count], z = new double[count];
            SdfVector minimum = new SdfVector(double.MaxValue, double.MaxValue, double.MaxValue);
            SdfVector maximum = new SdfVector(-double.MaxValue, -double.MaxValue, -double.MaxValue);
            for (int i = 0; i < count; ++i)
            {
                x[i] = pointList[i].x;
                y[i] = pointList[i].y;
                z[i] = pointList[i].z;
                minimum = new SdfVector(Math.Min(minimum.x, x[i]), Math.Min(minimum.y, y[i]), Math.Min(minimum.z, z[i]));
                maximum = new SdfVector(Math.Max(maximum.x, x[i]), Math.Max(maximum.y, y[i]), Math.Max(maximum.z, z[i]));
            }
            double diagonal = (maximum - minimum).Length();
            double[] givenX = x, givenY = y, givenZ = z;
            x = basis.Curves(x, 3);
            y = basis.Curves(y, 3);
            z = basis.Curves(z, 3);
            double[] w = new double[x.Length];
            if (widthList.Count > 1)
            {
                double[] given = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    given[i] = widthList[i];
                }
                w = basis.Curves(given, 3);
            }
            else
            {
                // One width for all: not through the basis (the values of hermite and power are not all points)
                for (int i = 0; i < w.Length; ++i)
                {
                    w[i] = widthList.Count == 1 ? widthList[0] : 0.05 * diagonal;
                }
            }
            double[] nx = null, ny = null, nz = null;
            if (normalList.Count != 0)
            {
                nx = new double[count];
                ny = new double[count];
                nz = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    nx[i] = normalList[i].x;
                    ny[i] = normalList[i].y;
                    nz[i] = normalList[i].z;
                }
                nx = basis.Curves(nx, 3);
                ny = basis.Curves(ny, 3);
                nz = basis.Curves(nz, 3);
            }
            RGB[] colors = colorList.Count > 0 ? colorList.ToArray() : null;
            if (colors != null && colors.Length > 1)
            {
                // The colors along the whole ribbon, as a chain of cubic Bezier curves, one for each curve of the ribbon
                double[] red = new double[count], green = new double[count], blue = new double[count];
                for (int i = 0; i < count; ++i)
                {
                    red[i] = colors[i].r;
                    green[i] = colors[i].g;
                    blue[i] = colors[i].b;
                }
                red = Chain(basis.Curves(red, 3));
                green = Chain(basis.Curves(green, 3));
                blue = Chain(basis.Curves(blue, 3));
                colors = new RGB[red.Length];
                for (int i = 0; i < colors.Length; ++i)
                {
                    colors[i] = new RGB((Float)Math.Max(0, red[i]), (Float)Math.Max(0, green[i]), (Float)Math.Max(0, blue[i]));
                }
            }

            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = renderFromObject;
            settings.objectFromRender = objectFromRender;
            settings.reverseOrientation = reverseOrientation;
            settings.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            bool solidAngle = parameters.GetOneBool("solidanglesampling", true);
            int strandColors = colors != null ? StrandColors.Register(colors, true) : 0;
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            // "flipturns": the segments of "ribbon2" with "flipturns" for the strand
            List<RibbonSegment> like = null;
            int pieces = parameters.GetOneInt("flippieces", parameters.GetOneInt("pieces", 4));
            int parts = basis.Degree == 3 ? Math.Max(1, pieces) : 1;
            bool flip = parameters.GetOneBool("flipturns", false);
            string flipStretch = parameters.GetOneString("flipstretch", "curve");
            if (flipStretch != "curve" && flipStretch != "bend")
            {
                pbrt.ErrorExit(loc, "ribbon3: \"flipstretch\" is \"curve\" or \"bend\".");
            }
            if (flip && flipStretch == "bend")
            {
                if (basis.Degree != 3 || nx == null)
                {
                    pbrt.ErrorExit(loc, "ribbon3: \"flipstretch\" \"bend\" is for the cubic bases, with \"N\".");
                }
                like = Ribbons2.BendStretches(x, y, z, nx, ny, nz);
                parts = 2;
            }
            else if (flip)
            {
                double[] givenWidths = null;
                if (widthList.Count > 1)
                {
                    givenWidths = new double[count];
                    for (int i = 0; i < count; ++i)
                    {
                        givenWidths[i] = widthList[i];
                    }
                }
                double[] givenNx = null, givenNy = null, givenNz = null;
                if (normalList.Count != 0)
                {
                    givenNx = new double[count];
                    givenNy = new double[count];
                    givenNz = new double[count];
                    for (int i = 0; i < count; ++i)
                    {
                        givenNx[i] = normalList[i].x;
                        givenNy[i] = normalList[i].y;
                        givenNz[i] = normalList[i].z;
                    }
                }
                List<Ribbons2.Run> runs = Ribbons2.Runs(basis, givenX, givenY, givenZ, givenWidths, widthList.Count == 1 ? widthList[0] : 0.05 * diagonal, givenNx, givenNy,
                                                        givenNz, parts);
                like = Ribbons2.Build(runs, true, out double[] starts, out double[] extents);
            }
            string intersector = parameters.GetOneString("intersector", "polynomial");
            if (intersector != "newton" && intersector != "roots" && intersector != "polynomial")
            {
                pbrt.ErrorExit(loc, "ribbon3: \"intersector\" is \"newton\", \"roots\" or \"polynomial\".");
            }
            string normalsName = parameters.GetOneString("normals", "slerp");
            if (normalsName != "slerp" && normalsName != "linear" && normalsName != "basis")
            {
                pbrt.ErrorExit(loc, "ribbon3: \"normals\" is \"slerp\", \"linear\" or \"basis\".");
            }
            Ribbon3Normals interpolation = normalsName == "linear" ? Ribbon3Normals.Linear : normalsName == "basis" ? Ribbon3Normals.Basis : Ribbon3Normals.Slerp;
            string look = parameters.GetOneString("look", "surface");
            if (look != "surface" && look != "curves" && look != "facing" && look != "cylinder")
            {
                pbrt.ErrorExit(loc, "ribbon3: \"look\" is \"surface\", \"curves\", \"facing\" or \"cylinder\".");
            }
            foreach (Ribbon3Shape shape in Shapes(settings, Curves(x, y, z, w, nx, ny, nz, interpolation, like, parts), strandColors, solidAngle, intersector != "newton", look != "surface",
                                                  look == "curves", look == "cylinder", 0))
            {
                shape.Polynomial = intersector == "polynomial";
                shapes.push_back(shape);
            }
            return shapes;
        }

        // The shapes of the curves: each curve in its leaves, LeavesPerShape of them to a shape
        //
        // split: the shapes that each curve is cut into, at equal parts of its parameter (0: one for the whole curve). More
        // shapes have tighter bounds, which is what matters with very many thin strands; as lights they are many small ones.
        public static List<Ribbon3Shape> Shapes(BezierSurfaceSettings settings, List<Ribbon3Curve> curves, int strandColors, bool solidAngleSampling, bool roots,
                                                bool facing, bool narrowed, bool rounded, int split)
        {
            List<Ribbon3Shape> shapes = new List<Ribbon3Shape>();
            double[] matrix = null;
            foreach (Ribbon3Curve curve in curves)
            {
                int leaves = Leaves(curve, roots || facing);
                matrix = matrix ?? Ribbon3Shape.Matrix(settings);
                if (split > 0)
                {
                    int each = Math.Max(1, leaves / split);
                    for (int i = 0; i < split; ++i)
                    {
                        shapes.Add(new Ribbon3Shape(settings, curve, (double)i / split, (double)(i + 1) / split, each, strandColors, solidAngleSampling, roots, facing, narrowed,
                                                    rounded, matrix));
                    }
                    continue;
                }
                for (int first = 0; first < leaves; first += LeavesPerShape)
                {
                    int number = Math.Min(LeavesPerShape, leaves - first);
                    shapes.Add(new Ribbon3Shape(settings, curve, (double)first / leaves, (double)(first + number) / leaves, number, strandColors, solidAngleSampling, roots,
                                                facing, narrowed, rounded, matrix));
                }
            }
            return shapes;
        }
    }
}
