// (not in pbrt-v4) ribbon.cs
//
// The curved ribbons of Reshetov, "Ray/Ribbon Intersections" (Proc. ACM Comput. Graph. Interact. Tech. 5(3), 2022). A ribbon is the area of a ruled surface
// S(u, s) = B(u) + s g(u) / |g(u)|, |s| <= w(u), around the centerline B(u), a quadratic Bezier curve; the generator g(u) is interpolated linearly between two
// bitangents (plus the terms of equation (9) that make the connections of the segments smooth). The ray intersection solves a cubic equation (the robust solver
// of Appendix A, cubicsolver.cs) and finds the parameters v and t by the intersection with the encapsulating prism of Appendix B (Figure 18).
// A sequence of points defines a quadratic B-spline; each of its segments is one shape, as the triangles of a mesh are.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // What a shape needs of a segment of a ribbon: the parameters u along the ribbon and v in [-1, 1] across its width
    public interface IRibbonSegment
    {
        int Index { get; }
        int Count { get; }
        SdfVector Position(double u, double v);
        void Derivatives(double u, double v, out SdfVector dpdu, out SdfVector dpdv);
        void Bounds(out SdfVector low, out SdfVector high);
        bool Intersect(SdfVector origin, SdfVector direction, double tmin, double tmax, double epsilon, out double t, out double hitU, out double hitV);
    }

    // One segment of a ribbon in object space (ribbon3: a quadratic directrix and a linear generator, a cubic equation)
    public sealed unsafe class RibbonSegment : IRibbonSegment
    {
        // The centerline (a quadratic Bezier curve)
        public SdfVector b0, b1, b2;
        // The bitangents of the generator at the ends: |bitangent0| = 1 and |bitangent1| = alpha
        public SdfVector bitangent0, bitangent1;
        // The terms of equation (9) for the smooth connection
        public double beta, gamma;
        // The quadratic Bezier coefficients of the half width
        public double width0, width1, width2;
        // The range of the parameter along the whole ribbon of the segment (for the texture coordinates), and the number of segments
        public int index, count;

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
            double a = (1 - u) * (1 - u), b = 2 * u * (1 - u), c = u * u;
            return a * b0 + b * b1 + c * b2;
        }

        public SdfVector Velocity(double u)
        {
            return 2 * (1 - u) * (b1 - b0) + 2 * u * (b2 - b1);
        }

        public double Width(double u)
        {
            return (1 - u) * (1 - u) * width0 + 2 * u * (1 - u) * width1 + u * u * width2;
        }

        public double WidthDerivative(double u)
        {
            return 2 * (1 - u) * (width1 - width0) + 2 * u * (width2 - width1);
        }

        // The generator g(u) (not normalized): the interpolated bitangents and the correction e(u) of equation (9)
        public SdfVector Generator(double u)
        {
            SdfVector c1 = 2 * (b1 - b0), c2 = b0 - 2 * b1 + b2;
            SdfVector e = (beta * c2 + gamma * (c1 + u * c2)) * (u * (1 - u));
            return bitangent0 * (1 - u) + bitangent1 * u + e;
        }

        public SdfVector GeneratorDerivative(double u)
        {
            SdfVector c1 = 2 * (b1 - b0), c2 = b0 - 2 * b1 + b2;
            SdfVector m = beta * c2 + gamma * (c1 + u * c2);
            return bitangent1 - bitangent0 + gamma * c2 * (u * (1 - u)) + m * (1 - 2 * u);
        }

        // The point of the ribbon at (u, v), v in [-1, 1] across the width
        public SdfVector Position(double u, double v)
        {
            SdfVector g = Generator(u);
            double length = g.Length();
            SdfVector direction = length > 0 ? g / length : new SdfVector(0, 0, 0);
            return Center(u) + direction * (v * Width(u));
        }

        // The derivatives of the position with respect to u and v
        public void Derivatives(double u, double v, out SdfVector dpdu, out SdfVector dpdv)
        {
            SdfVector g = Generator(u);
            double length = g.Length();
            SdfVector direction = length > 0 ? g / length : new SdfVector(0, 0, 0);
            SdfVector gPrime = GeneratorDerivative(u);
            SdfVector directionPrime = length > 0 ? (gPrime - SdfVector.Dot(gPrime, direction) * direction) / length : new SdfVector(0, 0, 0);
            double w = Width(u);
            dpdu = Velocity(u) + v * (WidthDerivative(u) * direction + w * directionPrime);
            dpdv = w * direction;
        }

        // The bounds of the ribbon: those of the control points of the centerline, grown by the largest half width
        public void Bounds(out SdfVector low, out SdfVector high)
        {
            low = new SdfVector(Math.Min(b0.x, Math.Min(b1.x, b2.x)), Math.Min(b0.y, Math.Min(b1.y, b2.y)), Math.Min(b0.z, Math.Min(b1.z, b2.z)));
            high = new SdfVector(Math.Max(b0.x, Math.Max(b1.x, b2.x)), Math.Max(b0.y, Math.Max(b1.y, b2.y)), Math.Max(b0.z, Math.Max(b1.z, b2.z)));
            double widest = Math.Max(Math.Abs(width0), Math.Max(Math.Abs(width1), Math.Abs(width2)));
            low = low - new SdfVector(widest, widest, widest);
            high = high + new SdfVector(widest, widest, widest);
        }

        // The intersection of the ray (origin, direction of unit length) with the ribbon, the closest in (tmin, tmax); Appendix B of the paper
        public bool Intersect(SdfVector origin, SdfVector direction, double tmin, double tmax, double epsilon, out double t, out double hitU, out double hitV)
        {
            t = tmax;
            hitU = 0;
            hitV = 0;
            SdfVector c0 = b0 - origin;
            SdfVector c1 = 2 * (b1 - b0);
            SdfVector c2 = b0 - 2 * b1 + b2;

            // The scalar triple products of equation (1), (D(u) - O) . (g(u) x d) = 0, with D(u) = C0 + C1 u + C2 u^2 and g(u) = b0 + (b1 - b0) u + e(u)
            SdfVector g0 = SdfVector.Cross(bitangent0, direction);
            SdfVector g1 = SdfVector.Cross(bitangent1 - bitangent0, direction);
            double t01 = SdfVector.Dot(c0, SdfVector.Cross(c1, direction));
            double t02 = SdfVector.Dot(c0, SdfVector.Cross(c2, direction));
            double t12 = SdfVector.Dot(c1, SdfVector.Cross(c2, direction));
            double q0 = SdfVector.Dot(c0, g0);
            double q1 = SdfVector.Dot(c1, g0) + SdfVector.Dot(c0, g1) + beta * t02 + gamma * t01;
            double q2 = SdfVector.Dot(c2, g0) + SdfVector.Dot(c1, g1) + beta * (t12 - t02) + gamma * (t02 - t01);
            double q3 = SdfVector.Dot(c2, g1) - beta * t12 - gamma * t02;

            double* roots = stackalloc double[3];
            int count = RobustCubic.Solve(q3, q2, q1, q0, roots);
            bool found = false;
            for (int i = 0; i < count; ++i)
            {
                double u = roots[i];
                if (u < -1e-7 || u > 1 + 1e-7)
                {
                    continue;
                }
                u = Math.Max(0, Math.Min(1, u));
                double w = Width(u);
                SdfVector generator = Generator(u);
                double generatorLength = generator.Length();
                SdfVector velocity = Velocity(u);
                SdfVector center = c0 + c1 * u + c2 * (u * u);
                if (!(generatorLength > 0))
                {
                    continue;
                }
                SdfVector bn = generator / generatorLength;
                SdfVector sn = SdfVector.Cross(velocity, bn);
                double snLength = sn.Length();
                if (!(snLength > 0))
                {
                    continue;
                }
                sn = sn / snLength;
                // The ray intersects the ribbon only if it intersects one top and one bottom face of the prism around it (Figure 18)
                double ws = SdfVector.Dot(bn, direction) > 0 ? -1 : 1;
                SdfVector pn = (SdfVector.Dot(sn, direction) > 0 ? -1 : 1) * sn + ws * bn;
                SdfVector a = center + w * bn;
                SdfVector b = center - w * bn;
                double den = SdfVector.Dot(pn, direction);
                if (den == 0)
                {
                    continue;
                }
                double num = SdfVector.Dot(pn, center);
                double tn = (num - ws * w) / den;
                double tp = (num + ws * w) / den;
                double dn = SdfVector.Dot(tn * direction - b, sn);
                double dp = SdfVector.Dot(tp * direction - a, sn);
                if ((dn > 0) == (dp > 0))
                {
                    continue;
                }
                double vi = dn / (dn - dp);
                double ti = tn + (tp - tn) * vi;
                if (tmin < ti && ti < t)
                {
                    hitU = u;
                    hitV = 2 * vi - 1;
                    t = ti - epsilon / 2;
                    found = true;
                }
            }
            return found;
        }
    }

    // The segments of the ribbon through a sequence of points: the quadratic B-spline of the points, with the bitangents from the normals
    public static class RibbonBuilder
    {
        // points: at least three; normals: the surface normal at each point, or null (the normal of the plane of the curve); widths: the half width at each point
        public static List<RibbonSegment> Build(SdfVector[] points, SdfVector[] normals, double[] widths)
        {
            List<RibbonSegment> segments = new List<RibbonSegment>();
            int count = points.Length - 2;
            if (count < 1)
            {
                return segments;
            }
            // The tangent and the desired normal at the joints (the ends of the segments): joint 0 is the first point, joint count the last, and the others the
            // middles of the points j and j + 1
            SdfVector[] planeNormals = PlaneNormals(points, count);
            SdfVector[] jointBitangent = new SdfVector[count + 1];
            for (int j = 0; j <= count; ++j)
            {
                SdfVector tangent = j == 0 ? points[1] - points[0] : j == count ? points[points.Length - 1] - points[points.Length - 2] : points[j + 1] - points[j];
                SdfVector normal;
                if (normals != null)
                {
                    normal = j == 0 ? normals[0] : j == count ? normals[points.Length - 1] : (normals[j] + normals[j + 1]) * 0.5;
                }
                else
                {
                    normal = planeNormals[j];
                }
                SdfVector bitangent = SdfVector.Cross(tangent, normal);
                jointBitangent[j] = bitangent.LengthSquared() > 0 ? bitangent.Normalized() : OrthogonalTo(tangent);
            }
            for (int i = 0; i < count; ++i)
            {
                RibbonSegment segment = new RibbonSegment();
                segment.index = i;
                segment.count = count;
                SdfVector start = i == 0 ? points[0] : (points[i] + points[i + 1]) * 0.5;
                SdfVector end = i == count - 1 ? points[points.Length - 1] : (points[i + 1] + points[i + 2]) * 0.5;
                segment.b0 = start;
                segment.b1 = points[i + 1];
                segment.b2 = end;
                double startWidth = i == 0 ? widths[0] : (widths[i] + widths[i + 1]) * 0.5;
                double endWidth = i == count - 1 ? widths[points.Length - 1] : (widths[i + 1] + widths[i + 2]) * 0.5;
                segment.width0 = startWidth;
                segment.width1 = widths[i + 1];
                segment.width2 = endWidth;
                segments.Add(segment);
            }
            // The bitangents and the connections
            for (int i = 0; i < count; ++i)
            {
                RibbonSegment segment = segments[i];
                SdfVector start = i == 0 ? jointBitangent[0] : segments[i - 1].bitangent1.Normalized();
                SdfVector end = jointBitangent[i + 1];
                if (SdfVector.Dot(start, end) < 0)
                {
                    end = -end;
                }
                segment.bitangent0 = start;
                segment.bitangent1 = end;
                segment.beta = 0;
                segment.gamma = 0;
                if (i > 0)
                {
                    Connect(segments[i - 1], segment);
                }
            }
            return segments;
        }

        // The normal of the plane of the curve at every joint: the average of the normals of the planes of the triples of points around it, with the same side
        // for all; where the curve is straight the normal of the neighbor is used
        private static SdfVector[] PlaneNormals(SdfVector[] points, int count)
        {
            SdfVector[] triples = new SdfVector[count];
            bool[] valid = new bool[count];
            for (int i = 0; i < count; ++i)
            {
                SdfVector normal = SdfVector.Cross(points[i + 1] - points[i], points[i + 2] - points[i + 1]);
                valid[i] = normal.LengthSquared() > 1e-24 * (points[i + 1] - points[i]).LengthSquared() * (points[i + 2] - points[i + 1]).LengthSquared();
                triples[i] = valid[i] ? normal.Normalized() : new SdfVector(0, 0, 0);
            }
            for (int i = 1; i < count; ++i)
            {
                if (valid[i] && SdfVector.Dot(triples[i], triples[i - 1]) < 0)
                {
                    triples[i] = -triples[i];
                }
                else if (!valid[i] && valid[i - 1])
                {
                    triples[i] = triples[i - 1];
                    valid[i] = true;
                }
            }
            for (int i = count - 2; i >= 0; --i)
            {
                if (!valid[i] && valid[i + 1])
                {
                    triples[i] = triples[i + 1];
                    valid[i] = true;
                }
            }
            SdfVector[] result = new SdfVector[count + 1];
            for (int j = 0; j <= count; ++j)
            {
                SdfVector sum = new SdfVector(0, 0, 0);
                if (j > 0)
                {
                    sum = sum + triples[j - 1];
                }
                if (j < count)
                {
                    sum = sum + triples[j];
                }
                if (sum.LengthSquared() > 0)
                {
                    result[j] = sum.Normalized();
                }
                else
                {
                    SdfVector tangent = j == 0 ? points[1] - points[0] : points[Math.Min(j + 1, points.Length - 1)] - points[j];
                    result[j] = OrthogonalTo(tangent);
                }
            }
            return result;
        }

        private static SdfVector OrthogonalTo(SdfVector direction)
        {
            SdfVector axis = Math.Abs(direction.x) < Math.Abs(direction.y) && Math.Abs(direction.x) < Math.Abs(direction.z) ? new SdfVector(1, 0, 0) :
                             Math.Abs(direction.y) < Math.Abs(direction.z) ? new SdfVector(0, 1, 0) : new SdfVector(0, 0, 1);
            SdfVector result = SdfVector.Cross(direction, axis);
            return result.LengthSquared() > 0 ? result.Normalized() : new SdfVector(1, 0, 0);
        }

        // Section 4.1: the terms beta, gamma (and the length alpha of the last bitangent) of the segment next that make the surface normals of both segments the
        // same along the joint. The normal at (u, v) is parallel to X + v w Y with X = B' x b and Y = g' x g / |g|^2; the tangent B' is the same on both sides of
        // a joint of the quadratic B-spline and so is g (up to its length), so the normals agree for every v when Y is the same on both sides: with
        // g(u0) = alpha_p b before and b after, the component of g' that is perpendicular to b of the next segment is that of the previous one divided by alpha_p.
        // That is two equations for alpha, beta and gamma; alpha is chosen to minimize beta^2 + gamma^2.
        private static void Connect(RibbonSegment previous, RibbonSegment next)
        {
            SdfVector b = next.bitangent0;
            SdfVector c1 = 2 * (previous.b1 - previous.b0), c2 = previous.b0 - 2 * previous.b1 + previous.b2;
            double previousAlpha = previous.bitangent1.Length();
            // g'(1) of the previous segment
            SdfVector previousGenerator = previous.bitangent1 - previous.bitangent0 - (previous.beta * c2 + previous.gamma * (c1 + c2));
            SdfVector nc1 = 2 * (next.b1 - next.b0), nc2 = next.b0 - 2 * next.b1 + next.b2;
            SdfVector unitEnd = next.bitangent1.Normalized();
            // A basis of the plane perpendicular to b
            SdfVector e1 = OrthogonalTo(b);
            SdfVector e2 = SdfVector.Cross(b, e1).Normalized();
            double m11 = SdfVector.Dot(e1, nc2), m12 = SdfVector.Dot(e1, nc1);
            double m21 = SdfVector.Dot(e2, nc2), m22 = SdfVector.Dot(e2, nc1);
            double determinant = m11 * m22 - m12 * m21;
            if (!(previousAlpha > 0) || Math.Abs(determinant) < 1e-12 * Math.Max(1e-300, Math.Abs(m11 * m22) + Math.Abs(m12 * m21)))
            {
                next.bitangent1 = unitEnd;
                return;
            }
            SdfVector target = previousGenerator / previousAlpha;
            double r1 = SdfVector.Dot(e1, target), r2 = SdfVector.Dot(e2, target);
            double s1 = SdfVector.Dot(e1, unitEnd), s2 = SdfVector.Dot(e2, unitEnd);
            // [beta, gamma] = M^-1 (r - alpha s) = p + alpha q
            double p0 = (m22 * r1 - m12 * r2) / determinant, p1 = (-m21 * r1 + m11 * r2) / determinant;
            double q0 = -(m22 * s1 - m12 * s2) / determinant, q1 = -(-m21 * s1 + m11 * s2) / determinant;
            double denominator = q0 * q0 + q1 * q1;
            double alpha = denominator > 0 ? -(p0 * q0 + p1 * q1) / denominator : 1;
            if (!(alpha > 1e-3) || double.IsNaN(alpha) || double.IsInfinity(alpha))
            {
                // the bitangent must keep its direction: take the smallest positive length that is allowed
                alpha = 1;
            }
            next.beta = p0 + alpha * q0;
            next.gamma = p1 + alpha * q1;
            next.bitangent1 = unitEnd * alpha;
            if (double.IsNaN(next.beta) || double.IsNaN(next.gamma) || double.IsInfinity(next.beta) || double.IsInfinity(next.gamma))
            {
                next.beta = 0;
                next.gamma = 0;
                next.bitangent1 = unitEnd;
            }
        }
    }
}
