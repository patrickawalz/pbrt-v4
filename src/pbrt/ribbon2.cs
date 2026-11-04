// (not in pbrt-v4) ribbon2.cs
//
// Ribbons2 of Reshetov, "Ray/Ribbon Intersections" (2022), Section 3: a ribbon inside a bilinear patch Q(u, v) = A(u) + v G(u), the area near the diagonal
// B(u) = Q(u, u) (a quadratic Bezier curve) with |u - v| <= w(u) psi(u), psi = |Qt| / |Qv x Qt| (equation (7); the tangent of the diagonal Qt, the derivative Qv).
// The ray intersection is a quadratic equation (the line of the patch at u meets the ray) with v allowed outside [0, 1]. A sequence of segments is joined along
// the lines u = 1 and u = 0 of consecutive patches with the binding factor y of Section 3.2, chosen to minimize the variance of psi (equation (8)).
// The width is evaluated exactly at the hit instead of by the quadratic approximation of the paper.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class Ribbon2Segment : IRibbonSegment
    {
        // The control points of the bilinear patch Q(u, v) = (1 - u)(1 - v) q00 + (1 - u) v q01 + u (1 - v) q10 + u v q11
        public SdfVector q00, q01, q10, q11;
        // The quadratic Bezier coefficients of the half width (measured in the surface along the generator)
        public double width0, width1, width2;
        public int index, count;

        public int Index
        {
            get { return index; }
        }

        public int Count
        {
            get { return count; }
        }

        public SdfVector Directrix(double u)
        {
            return q00 + (q10 - q00) * u;
        }

        public SdfVector Generator(double u)
        {
            return (q01 - q00) + (q00 - q01 - q10 + q11) * u;
        }

        // The patch
        public SdfVector Patch(double u, double v)
        {
            return Directrix(u) + Generator(u) * v;
        }

        // The tangent of the diagonal Q(u, u)
        public SdfVector Tangent(double u)
        {
            SdfVector b0 = q00, b1 = (q01 + q10) * 0.5, b2 = q11;
            return 2 * (1 - u) * (b1 - b0) + 2 * u * (b2 - b1);
        }

        public double Width(double u)
        {
            return (1 - u) * (1 - u) * width0 + 2 * u * (1 - u) * width1 + u * u * width2;
        }

        // psi(u) of equation (7): the conversion of a distance on the surface across the diagonal to the distance |u - v| in the parameters
        public double Psi(double u)
        {
            SdfVector tangent = Tangent(u);
            SdfVector generator = Generator(u);
            double across = SdfVector.Cross(generator, tangent).Length();
            double tangentLength = tangent.Length();
            if (!(across > 1e-300))
            {
                return double.MaxValue;
            }
            return tangentLength / across;
        }

        // The half width in the parameters: w(u) psi(u)
        public double Omega(double u)
        {
            return Math.Abs(Width(u)) * Psi(u);
        }

        // The point at (u, s), s in [-1, 1] across the ribbon
        public SdfVector Position(double u, double s)
        {
            return Patch(u, u + s * Omega(u));
        }

        public void Derivatives(double u, double s, out SdfVector dpdu, out SdfVector dpdv)
        {
            const double h = 1e-6;
            double omega = Omega(u);
            double omegaPrime = (Omega(Math.Min(1, u + h)) - Omega(Math.Max(0, u - h))) / (Math.Min(1, u + h) - Math.Max(0, u - h));
            double v = u + s * omega;
            SdfVector qu = (q10 - q00) + (q00 - q01 - q10 + q11) * v;
            SdfVector qv = Generator(u);
            dpdu = qu + qv * (1 + s * omegaPrime);
            dpdv = qv * omega;
        }

        // The box of the edges of the ribbon (the inside is on the lines between them), by samples along the edges, with a margin
        public void Bounds(out SdfVector low, out SdfVector high)
        {
            const int samples = 128;
            low = new SdfVector(double.MaxValue, double.MaxValue, double.MaxValue);
            high = new SdfVector(-double.MaxValue, -double.MaxValue, -double.MaxValue);
            for (int i = 0; i <= samples; ++i)
            {
                double u = (double)i / samples;
                for (int side = -1; side <= 1; ++side)
                {
                    SdfVector p = Position(u, side);
                    low = new SdfVector(Math.Min(low.x, p.x), Math.Min(low.y, p.y), Math.Min(low.z, p.z));
                    high = new SdfVector(Math.Max(high.x, p.x), Math.Max(high.y, p.y), Math.Max(high.z, p.z));
                }
            }
            SdfVector extent = high - low;
            double pad = 0.02 * extent.Length() + 1e-12;
            low = low - new SdfVector(pad, pad, pad);
            high = high + new SdfVector(pad, pad, pad);
        }

        // The intersection of the ray (origin, direction of unit length) with the patch near the diagonal, the nearest in (tmin, tmax)
        public bool Intersect(SdfVector origin, SdfVector direction, double tmin, double tmax, double epsilon, out double t, out double hitU, out double hitV)
        {
            t = tmax;
            hitU = 0;
            hitV = 0;
            SdfVector a0 = q00 - origin, a1 = q10 - q00;
            SdfVector g0 = q01 - q00, g1 = q00 - q01 - q10 + q11;
            // (A(u) - o) . (G(u) x d) = 0
            SdfVector g0d = SdfVector.Cross(g0, direction), g1d = SdfVector.Cross(g1, direction);
            double c0 = SdfVector.Dot(a0, g0d);
            double c1 = SdfVector.Dot(a1, g0d) + SdfVector.Dot(a0, g1d);
            double c2 = SdfVector.Dot(a1, g1d);
            double* roots = stackalloc double[3];
            int n = RobustCubic.Solve(0, c2, c1, c0, roots);
            bool found = false;
            for (int i = 0; i < n; ++i)
            {
                double u = roots[i];
                if (u < -1e-7 || u > 1 + 1e-7)
                {
                    continue;
                }
                u = Math.Max(0, Math.Min(1, u));
                SdfVector a = Directrix(u);
                SdfVector g = Generator(u);
                SdfVector normal = SdfVector.Cross(g, direction);
                double nn = normal.LengthSquared();
                if (!(nn > 1e-300))
                {
                    continue;
                }
                SdfVector r = origin - a;
                double v = SdfVector.Dot(SdfVector.Cross(r, direction), normal) / nn;
                double ti = SdfVector.Dot(SdfVector.Cross(r, g), normal) / nn;
                if (!(tmin < ti && ti < t))
                {
                    continue;
                }
                double omega = Omega(u);
                if (!(omega > 0) || Math.Abs(u - v) > omega)
                {
                    continue;
                }
                hitU = u;
                hitV = (v - u) / omega;
                t = ti - epsilon / 2;
                found = true;
            }
            return found;
        }
    }

    public static class Ribbon2Builder
    {
        // The ratio of the lengths of the two diagonals of a patch that is not exceeded (the stretched patches are close to the intersector's limits)
        public const double MaxStretch = 200;

        // points: at least three (the quadratic B-spline); normals: optional, for the orientation of the first patch only (the other patches cannot follow it);
        // widths: the half width at each point
        public static List<IRibbonSegment> Build(SdfVector[] points, SdfVector[] normals, double[] widths)
        {
            List<IRibbonSegment> result = new List<IRibbonSegment>();
            int count = points.Length - 2;
            Ribbon2Segment previous = null;
            for (int i = 0; i < count; ++i)
            {
                SdfVector b0 = i == 0 ? points[0] : (points[i] + points[i + 1]) * 0.5;
                SdfVector b1 = points[i + 1];
                SdfVector b2 = i == count - 1 ? points[points.Length - 1] : (points[i + 1] + points[i + 2]) * 0.5;
                Ribbon2Segment segment = new Ribbon2Segment();
                segment.index = i;
                segment.count = count;
                segment.width0 = i == 0 ? widths[0] : (widths[i] + widths[i + 1]) * 0.5;
                segment.width1 = widths[i + 1];
                segment.width2 = i == count - 1 ? widths[points.Length - 1] : (widths[i + 1] + widths[i + 2]) * 0.5;
                if (previous == null)
                {
                    FirstPatch(segment, b0, b1, b2, normals != null ? normals[0] : default, normals != null ? JointNormal(normals, i + 1, points.Length) : default, normals != null);
                }
                else
                {
                    NextPatch(segment, previous, b0, b1, b2, normals != null ? JointNormal(normals, i + 1, points.Length) : default, normals != null ? JointNormal(normals, i, points.Length) : default, normals != null);
                }
                result.Add(segment);
                previous = segment;
            }
            return result;
        }

        // Section 3.1: Q01 - Q10 orthogonal to the plane of the curve (or along the bitangent when a normal is given), as long as the ratio of the diagonals allows
        private static void FirstPatch(Ribbon2Segment segment, SdfVector b0, SdfVector b1, SdfVector b2, SdfVector normal, SdfVector endNormal, bool hasNormal)
        {
            SdfVector direction;
            if (hasNormal)
            {
                SdfVector bitangent = SdfVector.Cross(b1 - b0, normal);
                direction = bitangent.LengthSquared() > 0 ? bitangent.Normalized() : default;
            }
            else
            {
                SdfVector planeNormal = SdfVector.Cross(b1 - b0, b1 - b2);
                direction = planeNormal.LengthSquared() > 0 ? planeNormal.Normalized() : default;
            }
            if (direction.LengthSquared() == 0)
            {
                // a straight curve: any direction across it
                SdfVector along = b2 - b0;
                SdfVector axis = Math.Abs(along.x) < Math.Abs(along.y) && Math.Abs(along.x) < Math.Abs(along.z) ? new SdfVector(1, 0, 0) :
                                 Math.Abs(along.y) < Math.Abs(along.z) ? new SdfVector(0, 1, 0) : new SdfVector(0, 0, 1);
                direction = SdfVector.Cross(along, axis).Normalized();
            }
            double diagonal = (b2 - b0).Length();
            // |Q01 - Q10| = 2 s: a ratio of MaxStretch / 2 of the diagonals (the symmetric patch)
            double s = 0.5 * Math.Max(diagonal, 1e-12) * MaxStretch * 0.5;
            segment.q00 = b0;
            segment.q11 = b2;
            if (hasNormal)
            {
                SdfVector wantedEnd = SdfVector.Cross(b2 - b1, endNormal);
                if (wantedEnd.LengthSquared() > 0)
                {
                    wantedEnd = wantedEnd.Normalized();
                    for (int flip = 0; flip < 2; ++flip)
                    {
                        SdfVector d0 = flip == 0 ? direction : -1 * direction;
                        if (ExactPatch(segment, b1, d0, SdfVector.Dot(wantedEnd, d0) < 0 ? -1 * wantedEnd : wantedEnd))
                        {
                            return;
                        }
                    }
                }
            }
            segment.q01 = b1 + direction * s;
            segment.q10 = b1 - direction * s;
        }

        // Section 3.2: N00 = P11, N01 = P11 + y (P11 - P10), N10 = 2 B1 - N01; y minimizes the variation of psi

        // A patch whose lines at both ends are exactly the given directions (d0 at u = 0, d1 at u = 1): Q01 = Q00 + a d0, Q10 = Q11 - b d1 with the diagonal's middle control
        // point (Q01 + Q10) / 2 as close to the control point b1 as the two directions allow (least squares: only the part of the offset out of their plane remains).
        // The edges of neighboring patches then meet exactly. False when the directions are (nearly) the same or the patch is not a thin ribbon.
        private static bool ExactPatch(Ribbon2Segment segment, SdfVector b1, SdfVector d0, SdfVector d1)
        {
            SdfVector offset = 2 * b1 - segment.q00 - segment.q11;
            double dd = SdfVector.Dot(d0, d1);
            double det = 1 - dd * dd;
            if (!(det > 1e-10))
            {
                return false;
            }
            double sa = SdfVector.Dot(offset, d0), sb = -SdfVector.Dot(offset, d1);
            double a = (sa + dd * sb) / det, b = (sb + dd * sa) / det;
            if (!(a > 0 && b > 0))
            {
                // the generator would point against d0 at the start or at the end (the strip would turn over: its sides swap at the joint)
                return false;
            }
            segment.q01 = segment.q00 + d0 * a;
            segment.q10 = segment.q11 - d1 * b;
            double across = (segment.q01 - segment.q10).Length();
            double diagonal = (segment.q11 - segment.q00).Length();
            double ratio = across > diagonal ? across / Math.Max(diagonal, 1e-300) : diagonal / Math.Max(across, 1e-300);
            // not folded: the generator keeps away from the tangent of the diagonal (the width itself is exact for any size of the patch)
            for (int k = 0; k <= 32; ++k)
            {
                double u = k / 32.0;
                SdfVector tangent = segment.Tangent(u);
                SdfVector generator = segment.Generator(u);
                double product = tangent.Length() * generator.Length();
                if (!(product > 0) || SdfVector.Cross(generator, tangent).Length() / product < 0.25)
                {
                    return false;
                }
            }
            return true;
        }

        // The normal at the end of the segment that starts at point i: the end is the midpoint of the points i + 1 and i + 2 (or the last point)
        private static SdfVector JointNormal(SdfVector[] normals, int i, int count)
        {
            SdfVector n = i + 1 < count - 1 ? normals[i] + normals[i + 1] : normals[count - 1];
            return n.LengthSquared() > 0 ? n.Normalized() : normals[i];
        }

        private static void NextPatch(Ribbon2Segment segment, Ribbon2Segment previous, SdfVector b0, SdfVector b1, SdfVector b2, SdfVector endNormal, SdfVector startNormal, bool hasNormal)
        {
            SdfVector line = previous.q11 - previous.q10;
            if (hasNormal)
            {
                // With normals the line at the start of the patch is the bitangent of the start (the same line as the end of the previous patch when it was reached, a
                // slightly turned one otherwise: the strip is continuous, with a small crease): only then can the strip twist out of the plane of its curve
                SdfVector start = SdfVector.Cross(b1 - b0, startNormal);
                if (start.LengthSquared() > 0)
                {
                    start = start.Normalized() * line.Length();
                    line = SdfVector.Dot(start, line) < 0 ? -1 * start : start;
                }
            }
            segment.q00 = previous.q11;
            segment.q11 = b2;
            double diagonal = (b2 - segment.q00).Length();
            double bestCost = double.MaxValue, bestY = 1;
            // With normals the line at the end of the patch is turned toward the bitangent of the end (the only freedom of the patch is y): the strip follows the twist
            SdfVector wanted = default;
            if (hasNormal)
            {
                wanted = SdfVector.Cross(b2 - b1, endNormal);
                wanted = wanted.LengthSquared() > 0 ? wanted.Normalized() : default;
            }
            if (wanted.LengthSquared() > 0)
            {
                SdfVector d0 = (previous.q11 - previous.q10).Normalized();
                SdfVector d1 = SdfVector.Dot(wanted, d0) < 0 ? -1 * wanted : wanted;
                if (ExactPatch(segment, b1, d0, d1))
                {
                    return;
                }
            }
            Func<double, double> evaluate = (double y) =>
            {
                SdfVector q01 = previous.q11 + line * y;
                SdfVector q10 = 2 * b1 - q01;
                double across = (q01 - q10).Length();
                double ratio = across > diagonal ? across / Math.Max(diagonal, 1e-300) : diagonal / Math.Max(across, 1e-300);
                if (!(ratio <= MaxStretch))
                {
                    return double.MaxValue;
                }
                segment.q01 = q01;
                segment.q10 = q10;
                double cost = Cost(segment);
                if (cost < double.MaxValue && wanted.LengthSquared() > 0)
                {
                    SdfVector end = segment.q11 - segment.q10;
                    double length = end.Length();
                    cost += length > 0 ? 1e6 * (1 - Math.Abs(SdfVector.Dot(end, wanted)) / length) : 1e6;
                }
                return cost;
            };
            // y > 0: the generator keeps its direction through the joint (a negative y would swap the sides of the strip)
            for (int sign = 1; sign <= 1; sign += 2)
            {
                for (int k = -6; k <= 10; ++k)
                {
                    double y = sign * Math.Pow(10, 0.5 * k - 1);
                    double cost = evaluate(y);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestY = y;
                    }
                }
            }
            // The grid is coarse: the end line must meet the bitangent closely, or the edges jump at the joint. Narrowing scans around the best factor.
            if (bestCost < double.MaxValue)
            {
                double span = 3.2;
                for (int pass = 0; pass < 8; ++pass)
                {
                    double center = bestY;
                    for (int k = -20; k <= 20; ++k)
                    {
                        double y = center * Math.Pow(span, k / 20.0);
                        double cost = evaluate(y);
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestY = y;
                        }
                    }
                    span = 1 + (span - 1) * 0.2;
                }
            }
            segment.q01 = previous.q11 + line * bestY;
            segment.q10 = 2 * b1 - segment.q01;
        }

        // The cost of a patch: the relative variance of psi on [0, 1] (equation (8), normalized by the mean so that stretching alone does not lower it); a patch whose
        // generator comes close to the tangent of the diagonal is not a ribbon (the width goes to infinity: the folds of Figure 12) and costs more than any other
        private static double Cost(Ribbon2Segment segment)
        {
            const int samples = 32;
            const double minimumSine = 0.25;
            double sum = 0, sumSquares = 0, smallestSine = 1;
            for (int i = 0; i <= samples; ++i)
            {
                double u = (double)i / samples;
                SdfVector tangent = segment.Tangent(u);
                SdfVector generator = segment.Generator(u);
                double product = tangent.Length() * generator.Length();
                double sine = product > 0 ? SdfVector.Cross(generator, tangent).Length() / product : 0;
                smallestSine = Math.Min(smallestSine, sine);
                double psi = segment.Psi(u);
                if (!(psi < 1e300))
                {
                    return double.MaxValue;
                }
                if (segment.Omega(u) > 0.5)
                {
                    // the band would reach far outside of the patch: the strip is not thin there
                    return double.MaxValue;
                }
                sum += psi;
                sumSquares += psi * psi;
            }
            double mean = sum / (samples + 1);
            double variance = sumSquares / (samples + 1) - mean * mean;
            double cost = mean > 0 ? Math.Max(0, variance) / (mean * mean) : double.MaxValue;
            if (smallestSine < minimumSine)
            {
                cost += 1e9 * (minimumSine - smallestSine) + 1e8;
            }
            return cost;
        }
    }
}
