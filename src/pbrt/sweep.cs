// (not in pbrt-v4) sweep.cs
//
// The "sweep" shape: a cross section that is itself a Bezier curve, carried along a cubic Bezier curve.
//
// The cross section is given in the frame of the curve (x along the bitangent, y along the normal), at the start of the curve,
// at both ends, or at each of its points; set into the frames at the four points of a curve, its points give one cubic rail
// each, and the surface is
//
//     S(u, v) = sum_j w_j B_j(v) R_j(u) / sum_j w_j B_j(v)
//
// with B_j the Bernstein polynomials of the degree of the cross section (1: a flat blade, 2, or 3) and w_j its weights (a
// quadratic with weights is an arc of a circle exactly). Nothing but the points of the rails is kept.
//
// The ray: with two planes e1, e2 through it, e . (S - o) = 0 is for each plane a polynomial of the degree of the cross
// section in v whose coefficients are cubics in u (the weights do not depend on u, so they cost no degree). The two have a
// root v in common where their resultant is zero: a polynomial in u alone of degree 6, 12 or 18, whose roots are found from
// its Bernstein coefficients (bernsteinroots.cs); v follows from either polynomial, and (u, v) is then brought onto the ray
// by Newton's steps on the surface itself.
//
// The area: |dS/du x dS/dv| is in closed form, so the density of a sample that is uniform in (u, v) is exact.
//
// Shape "sweep"
//   "point3 P"              the points of the cubic Bezier curves of a strand (3 k + 1)
//   "normal N"              two normals (the start and the end) or one for each point; without, a normal carried without twist
//   "integer crossdegree"   1, 2 (the default) or 3
//   "point2 cross"          the points of the cross section: pieces * degree + 1 for each section (several pieces share
//                           their ends: a keel, a closed tube)
//   "float crossweights"    a weight for each point of a section (1)
//   "integer sections"      1: one cross section all along; 2: at the start and at the end; the number of points of "P": one
//                           at each (the cross section may leave the curve: its points are free)
//   "float scale"           the size of the cross section: one value, two (start and end) or one for each point of "P"
//   "bool adaptive"         each piece at the least degree that draws it (true)
//   "integer splitdepth"    each curve as 2^splitdepth shapes; without, by its length and the width of the cross section

using System;
using System.Collections.Generic;

namespace pbrt
{
    // SweepSurface Definition
    public sealed unsafe class SweepSurface
    {
        // SweepSurface Public Methods

        // net[4 * j + i]: point i of the rail j (object space); weights[j]: the weight of the rail (above zero)
        // index, count: the curve among those of its strand; piece, pieces: the piece of the cross section among its pieces
        public SweepSurface(SdfVector[] net, double[] weights, int degree, int index, int count, int piece, int pieces)
        {
            this.net = net;
            this.weights = weights;
            this.degree = degree;
            this.index = index;
            this.count = count;
            this.piece = piece;
            this.pieces = pieces;
            SdfVector lower = net[0], upper = net[0];
            for (int i = 1; i < net.Length; ++i)
            {
                lower = new SdfVector(Math.Min(lower.x, net[i].x), Math.Min(lower.y, net[i].y), Math.Min(lower.z, net[i].z));
                upper = new SdfVector(Math.Max(upper.x, net[i].x), Math.Max(upper.y, net[i].y), Math.Max(upper.z, net[i].z));
            }
            center = (lower + upper) * 0.5;
            radius = 0;
            for (int i = 0; i < net.Length; ++i)
            {
                radius = Math.Max(radius, (net[i] - center).Length());
            }
            // W (S - center) in powers of u and v: the rails in powers of u with their weights, then across
            SdfVector[] rails = new SdfVector[4 * (degree + 1)];
            for (int j = 0; j <= degree; ++j)
            {
                SdfVector p0 = net[4 * j] - center, p1 = net[4 * j + 1] - center, p2 = net[4 * j + 2] - center, p3 = net[4 * j + 3] - center;
                rails[4 * j] = p0 * weights[j];
                rails[4 * j + 1] = (p1 - p0) * (3 * weights[j]);
                rails[4 * j + 2] = (p0 - 2 * p1 + p2) * (3 * weights[j]);
                rails[4 * j + 3] = (p3 - 3 * p2 + 3 * p1 - p0) * weights[j];
            }
            power = new SdfVector[4 * (degree + 1)];
            omega = new double[degree + 1];
            for (int i = 0; i < 4; ++i)
            {
                SdfVector h0 = rails[i], h1 = rails[4 + i];
                power[i] = h0;
                if (degree == 1)
                {
                    power[4 + i] = h1 - h0;
                }
                else if (degree == 2)
                {
                    SdfVector h2 = rails[8 + i];
                    power[4 + i] = (h1 - h0) * 2;
                    power[8 + i] = h0 - 2 * h1 + h2;
                }
                else
                {
                    SdfVector h2 = rails[8 + i], h3 = rails[12 + i];
                    power[4 + i] = (h1 - h0) * 3;
                    power[8 + i] = (h0 - 2 * h1 + h2) * 3;
                    power[12 + i] = h3 - 3 * h2 + 3 * h1 - h0;
                }
            }
            omega[0] = weights[0];
            if (degree == 1)
            {
                omega[1] = weights[1] - weights[0];
            }
            else if (degree == 2)
            {
                omega[1] = 2 * (weights[1] - weights[0]);
                omega[2] = weights[0] - 2 * weights[1] + weights[2];
            }
            else
            {
                omega[1] = 3 * (weights[1] - weights[0]);
                omega[2] = 3 * (weights[0] - 2 * weights[1] + weights[2]);
                omega[3] = weights[3] - 3 * weights[2] + 3 * weights[1] - weights[0];
            }
        }

        public int Degree
        {
            get { return degree; }
        }

        public int Index
        {
            get { return index; }
        }

        public int Count
        {
            get { return count; }
        }

        public int Piece
        {
            get { return piece; }
        }

        public int Pieces
        {
            get { return pieces; }
        }

        // The number of parts along u (a power of two, 16 at most) with which a part is about as long as the cross section is
        // wide: the boxes of such parts are tight, and few rays that come to one miss the surface
        public int Parts()
        {
            double length = 0, width = 0;
            for (int i = 0; i < 4; ++i)
            {
                if (i > 0)
                {
                    length += (net[i] - net[i - 1]).Length();
                }
                for (int j = 0; j <= degree; ++j)
                {
                    for (int k = j + 1; k <= degree; ++k)
                    {
                        width = Math.Max(width, (net[4 * j + i] - net[4 * k + i]).Length());
                    }
                }
            }
            int parts = 1;
            while (parts < 16 && parts * width < length)
            {
                parts *= 2;
            }
            return parts;
        }

        // The least degree that draws the same cross section: a cubic that is a quadratic with its degree raised, or a
        // quadratic that is a line, is brought down (the polynomial of the ray is then of degree 12 or 6, not 18 or 12). Only
        // for weights that are all the same.
        public static void Reduce(ref SdfVector[] net, ref double[] weights, ref int degree)
        {
            for (int j = 1; j <= degree; ++j)
            {
                if (weights[j] != weights[0])
                {
                    return;
                }
            }
            double size = 0;
            for (int i = 1; i < net.Length; ++i)
            {
                size = Math.Max(size, (net[i] - net[0]).Length());
            }
            double limit = 1e-9 * size;
            if (degree == 3)
            {
                for (int i = 0; i < 4; ++i)
                {
                    if ((net[12 + i] - 3 * net[8 + i] + 3 * net[4 + i] - net[i]).Length() > limit)
                    {
                        return;
                    }
                }
                SdfVector[] lower = new SdfVector[12];
                for (int i = 0; i < 4; ++i)
                {
                    lower[i] = net[i];
                    lower[4 + i] = ((3 * net[4 + i] - net[i]) + (3 * net[8 + i] - net[12 + i])) * 0.25;
                    lower[8 + i] = net[12 + i];
                }
                net = lower;
                weights = new double[] { weights[0], weights[0], weights[0] };
                degree = 2;
            }
            if (degree == 2)
            {
                for (int i = 0; i < 4; ++i)
                {
                    if ((net[8 + i] - 2 * net[4 + i] + net[i]).Length() > limit)
                    {
                        return;
                    }
                }
                SdfVector[] lower = new SdfVector[8];
                for (int i = 0; i < 4; ++i)
                {
                    lower[i] = net[i];
                    lower[4 + i] = net[8 + i];
                }
                net = lower;
                weights = new double[] { weights[0], weights[0] };
                degree = 1;
            }
        }

        // The point of the surface and its derivatives
        public void Evaluate(double u, double v, out SdfVector p, out SdfVector du, out SdfVector dv)
        {
            double a = 1 - u;
            double b0 = a * a * a, b1 = 3 * a * a * u, b2 = 3 * a * u * u, b3 = u * u * u;
            double d0 = -3 * a * a, d1 = 3 * a * (a - 2 * u), d2 = 3 * u * (2 * a - u), d3 = 3 * u * u;
            double* across = stackalloc double[4];
            double* acrossDerivative = stackalloc double[4];
            double c = 1 - v;
            if (degree == 1)
            {
                across[0] = c;
                across[1] = v;
                acrossDerivative[0] = -1;
                acrossDerivative[1] = 1;
            }
            else if (degree == 2)
            {
                across[0] = c * c;
                across[1] = 2 * c * v;
                across[2] = v * v;
                acrossDerivative[0] = -2 * c;
                acrossDerivative[1] = 2 * (c - v);
                acrossDerivative[2] = 2 * v;
            }
            else
            {
                across[0] = c * c * c;
                across[1] = 3 * c * c * v;
                across[2] = 3 * c * v * v;
                across[3] = v * v * v;
                acrossDerivative[0] = -3 * c * c;
                acrossDerivative[1] = 3 * c * (c - 2 * v);
                acrossDerivative[2] = 3 * v * (2 * c - v);
                acrossDerivative[3] = 3 * v * v;
            }
            SdfVector q = default, qu = default, qv = default;
            double w = 0, wv = 0;
            for (int j = 0; j <= degree; ++j)
            {
                SdfVector rail = net[4 * j] * b0 + net[4 * j + 1] * b1 + net[4 * j + 2] * b2 + net[4 * j + 3] * b3;
                SdfVector railDerivative = net[4 * j] * d0 + net[4 * j + 1] * d1 + net[4 * j + 2] * d2 + net[4 * j + 3] * d3;
                double weight = weights[j] * across[j], weightDerivative = weights[j] * acrossDerivative[j];
                q = q + rail * weight;
                qu = qu + railDerivative * weight;
                qv = qv + rail * weightDerivative;
                w += weight;
                wv += weightDerivative;
            }
            p = q / w;
            du = qu / w;
            dv = (qv - p * wv) / w;
        }

        // The box of the part [from, to] of u: the points of the rails of that part (the surface is within them, the weights
        // being above zero)
        public void Bounds(double from, double to, out SdfVector lower, out SdfVector upper)
        {
            lower = new SdfVector(double.MaxValue, double.MaxValue, double.MaxValue);
            upper = new SdfVector(-double.MaxValue, -double.MaxValue, -double.MaxValue);
            for (int j = 0; j <= degree; ++j)
            {
                for (int i = 0; i < 4; ++i)
                {
                    SdfVector point = Blossom(j, i < 3 ? from : to, i < 2 ? from : to, i < 1 ? from : to);
                    lower = new SdfVector(Math.Min(lower.x, point.x), Math.Min(lower.y, point.y), Math.Min(lower.z, point.z));
                    upper = new SdfVector(Math.Max(upper.x, point.x), Math.Max(upper.y, point.y), Math.Max(upper.z, point.z));
                }
            }
        }

        // The nearest hit of the ray (o, unit d; e1, e2 with d a frame) with the part [from, to] of u that is nearer than t
        public bool Intersect(SdfVector o, SdfVector d, SdfVector e1, SdfVector e2, double from, double to, ref double t, ref double hitU, ref double hitV)
        {
            SdfVector toCenter = center - o;
            double along = SdfVector.Dot(toCenter, d);
            // The point of the ray that is the nearest to the middle of the surface, from that middle (the squares of a far
            // origin would take the digits)
            SdfVector near = d * along - toCenter;
            if (near.LengthSquared() > radius * radius)
            {
                return false;
            }
            double nearX = SdfVector.Dot(e1, near), nearY = SdfVector.Dot(e2, near);
            // The two polynomials in v: a[4 * k + i], b[4 * k + i] the coefficient of v^k u^i
            double* a = stackalloc double[16];
            double* b = stackalloc double[16];
            for (int k = 0; k <= degree; ++k)
            {
                for (int i = 0; i < 4; ++i)
                {
                    a[4 * k + i] = SdfVector.Dot(e1, power[4 * k + i]);
                    b[4 * k + i] = SdfVector.Dot(e2, power[4 * k + i]);
                }
                a[4 * k] -= omega[k] * nearX;
                b[4 * k] -= omega[k] * nearY;
            }
            double* coef = stackalloc double[19];
            Resultant(a, b, coef);
            double* roots = stackalloc double[18];
            int rootCount = BernsteinRoots.Roots(coef, 6 * degree, from, to, 1e-9, roots);
            bool found = false;
            double* f = stackalloc double[4];
            double* g = stackalloc double[4];
            double* across = stackalloc double[3];
            for (int r = 0; r < rootCount; ++r)
            {
                double u = roots[r];
                double sizeF = 0, sizeG = 0;
                for (int k = 0; k <= degree; ++k)
                {
                    f[k] = PolynomialRoots.Evaluate(a + 4 * k, 3, u);
                    g[k] = PolynomialRoots.Evaluate(b + 4 * k, 3, u);
                    sizeF += Math.Abs(f[k]);
                    sizeG += Math.Abs(g[k]);
                }
                // v: the roots of the larger of the two polynomials; the one that the other shares is the one from which
                // Newton's steps come to the ray (where a ray all but touches the surface two roots u are near each other
                // and neither is exact, so the other polynomial is not asked)
                int acrossCount = PolynomialRoots.Roots(sizeF >= sizeG ? f : g, degree, -1e-3, 1 + 1e-3, 1e-9, across);
                for (int c = 0; c < acrossCount; ++c)
                {
                    found |= HitAt(o, d, e1, e2, toCenter, nearX, nearY, u, across[c], from, to, ref t, ref hitU, ref hitV);
                }
            }
            return found;
        }

        // SweepSurface Private Methods

        // The point of the cubic rail j at the three parameters (the points of a part of the rail are its values at the ends
        // of the part, taken three at a time)
        private SdfVector Blossom(int j, double u0, double u1, double u2)
        {
            SdfVector p0 = net[4 * j], p1 = net[4 * j + 1], p2 = net[4 * j + 2], p3 = net[4 * j + 3];
            SdfVector q0 = p0 + (p1 - p0) * u0, q1 = p1 + (p2 - p1) * u0, q2 = p2 + (p3 - p2) * u0;
            SdfVector r0 = q0 + (q1 - q0) * u1, r1 = q1 + (q2 - q1) * u1;
            return r0 + (r1 - r0) * u2;
        }

        // m = A_i B_j - A_j B_i: the cubics i and j of the two polynomials (seven coefficients)
        private static void Minor(double* a, double* b, int i, int j, double* m)
        {
            double* ai = a + 4 * i;
            double* aj = a + 4 * j;
            double* bi = b + 4 * i;
            double* bj = b + 4 * j;
            m[0] = ai[0] * bj[0] - aj[0] * bi[0];
            m[1] = ai[0] * bj[1] + ai[1] * bj[0] - aj[0] * bi[1] - aj[1] * bi[0];
            m[2] = ai[0] * bj[2] + ai[1] * bj[1] + ai[2] * bj[0] - aj[0] * bi[2] - aj[1] * bi[1] - aj[2] * bi[0];
            m[3] = ai[0] * bj[3] + ai[1] * bj[2] + ai[2] * bj[1] + ai[3] * bj[0] - aj[0] * bi[3] - aj[1] * bi[2] - aj[2] * bi[1] - aj[3] * bi[0];
            m[4] = ai[1] * bj[3] + ai[2] * bj[2] + ai[3] * bj[1] - aj[1] * bi[3] - aj[2] * bi[2] - aj[3] * bi[1];
            m[5] = ai[2] * bj[3] + ai[3] * bj[2] - aj[2] * bi[3] - aj[3] * bi[2];
            m[6] = ai[3] * bj[3] - aj[3] * bi[3];
        }

        // r += scale x y: polynomials of the degrees dx and dy
        private static void AddProduct(double* x, int dx, double* y, int dy, double scale, double* r)
        {
            for (int i = 0; i <= dx; ++i)
            {
                double xi = scale * x[i];
                for (int j = 0; j <= dy; ++j)
                {
                    r[i + j] += xi * y[j];
                }
            }
        }

        // The resultant in v of the two polynomials, a polynomial in u of degree 6 * degree
        private void Resultant(double* a, double* b, double* coef)
        {
            if (degree == 1)
            {
                Minor(a, b, 0, 1, coef);
                return;
            }
            for (int i = 0; i <= 6 * degree; ++i)
            {
                coef[i] = 0;
            }
            if (degree == 2)
            {
                // (A2 B0 - A0 B2)^2 - (A2 B1 - A1 B2) (A1 B0 - A0 B1)
                double* m20 = stackalloc double[7];
                double* m21 = stackalloc double[7];
                double* m10 = stackalloc double[7];
                Minor(a, b, 2, 0, m20);
                Minor(a, b, 2, 1, m21);
                Minor(a, b, 1, 0, m10);
                AddProduct(m20, 6, m20, 6, 1, coef);
                AddProduct(m21, 6, m10, 6, -1, coef);
                return;
            }
            // The determinant of Bezout's matrix of the two cubics,
            //     | [32] [31]        [30] |
            //     | [31] [30] + [21] [20] |
            //     | [30] [20]        [10] |      [ij] = A_i B_j - A_j B_i
            double* m32 = stackalloc double[7];
            double* m31 = stackalloc double[7];
            double* m30 = stackalloc double[7];
            double* m21c = stackalloc double[7];
            double* m20c = stackalloc double[7];
            double* m10c = stackalloc double[7];
            double* middle = stackalloc double[7];
            Minor(a, b, 3, 2, m32);
            Minor(a, b, 3, 1, m31);
            Minor(a, b, 3, 0, m30);
            Minor(a, b, 2, 1, m21c);
            Minor(a, b, 2, 0, m20c);
            Minor(a, b, 1, 0, m10c);
            for (int i = 0; i < 7; ++i)
            {
                middle[i] = m30[i] + m21c[i];
            }
            double* c0 = stackalloc double[13];
            double* c1 = stackalloc double[13];
            double* c2 = stackalloc double[13];
            for (int i = 0; i < 13; ++i)
            {
                c0[i] = 0;
                c1[i] = 0;
                c2[i] = 0;
            }
            AddProduct(middle, 6, m10c, 6, 1, c0);
            AddProduct(m20c, 6, m20c, 6, -1, c0);
            AddProduct(m31, 6, m10c, 6, 1, c1);
            AddProduct(m20c, 6, m30, 6, -1, c1);
            AddProduct(m31, 6, m20c, 6, 1, c2);
            AddProduct(middle, 6, m30, 6, -1, c2);
            AddProduct(m32, 6, c0, 12, 1, coef);
            AddProduct(m31, 6, c1, 12, -1, coef);
            AddProduct(m30, 6, c2, 12, 1, coef);
        }

        // (u, v) from the roots, brought onto the ray by Newton's steps on the surface; a hit where it comes to the ray within
        // the part and the cross section, in front of the origin and nearer than t
        private bool HitAt(SdfVector o, SdfVector d, SdfVector e1, SdfVector e2, SdfVector toCenter, double nearX, double nearY, double u, double v, double from,
                           double to, ref double t, ref double hitU, ref double hitV)
        {
            double accept = 1e-9 * radius;
            SdfVector p = default;
            bool on = false;
            for (int step = 0; step < 12; ++step)
            {
                Evaluate(u, v, out p, out SdfVector du, out SdfVector dv);
                SdfVector q = p - center;
                double fx = SdfVector.Dot(e1, q) - nearX, fy = SdfVector.Dot(e2, q) - nearY;
                if (fx * fx + fy * fy <= accept * accept)
                {
                    on = true;
                    break;
                }
                double xu = SdfVector.Dot(e1, du), xv = SdfVector.Dot(e1, dv), yu = SdfVector.Dot(e2, du), yv = SdfVector.Dot(e2, dv);
                double determinant = xu * yv - xv * yu;
                if (determinant == 0)
                {
                    return false;
                }
                u -= (fx * yv - fy * xv) / determinant;
                v -= (fy * xu - fx * yu) / determinant;
                if (!(u >= -0.5 && u <= 1.5 && v >= -0.5 && v <= 1.5))
                {
                    return false;
                }
            }
            if (!on || u < from - 1e-9 || u > to + 1e-9 || v < -1e-9 || v > 1 + 1e-9)
            {
                return false;
            }
            double distance = SdfVector.Dot(p - center + toCenter, d);
            if (!(distance > 0) || distance >= t)
            {
                return false;
            }
            t = distance;
            hitU = Math.Max(from, Math.Min(to, u));
            hitV = Math.Max(0, Math.Min(1, v));
            return true;
        }

        // SweepSurface Private Members
        private readonly SdfVector[] net;
        private readonly double[] weights;
        private readonly int degree;
        // W (S - center) and W in powers: power[4 * k + i] of v^k u^i, omega[k] of v^k
        private readonly SdfVector[] power;
        private readonly double[] omega;
        private readonly SdfVector center;
        private readonly double radius;
        private readonly int index, count, piece, pieces;
    }

    // SweepShape Definition
    public sealed unsafe class SweepShape : Shape
    {
        // SweepShape Public Methods
        // from, to: the part of the parameter of the curve that the shape is
        public SweepShape(BezierSurfaceSettings settings, SweepSurface surface, double from, double to)
        {
            this.settings = settings;
            this.surface = surface;
            this.from = from;
            this.to = to;
            surface.Bounds(from, to, out SdfVector lower, out SdfVector upper);
            Bounds3f result = default;
            for (int corner = 0; corner < 8; ++corner)
            {
                Point3f point = settings.renderFromObject.Apply(new Point3f((Float)((corner & 1) != 0 ? upper.x : lower.x), (Float)((corner & 2) != 0 ? upper.y : lower.y),
                                                                            (Float)((corner & 4) != 0 ? upper.z : lower.z)));
                result = corner == 0 ? new Bounds3f(point) : pbrt.Union(result, point);
            }
            Vector3f pad = new Vector3f(1e-4f, 1e-4f, 1e-4f) * pbrt.Length(result.pMax - result.pMin);
            bounds = new Bounds3f(result.pMin - pad, result.pMax + pad);
            area = -1;
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
            Geometry(u, v, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv);
            bool flipNormal = settings.reverseOrientation ^ settings.transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            SurfaceInteraction intr = new SurfaceInteraction(new Point3fi(p, pError), uv, -ray.d, dpdu, dpdv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ray.time,
                                                             flipNormal);
            return new ShapeIntersection(intr, (Float)(t / length));
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Trace(ray, tMax, out double u, out double v, out double t, out double length);
        }

        // The area by Gauss's rule of 8 by 8 points in (u, v)
        public override Float Area()
        {
            if (area < 0)
            {
                double sum = 0;
                for (int i = 0; i < 8; ++i)
                {
                    double u = from + (to - from) * (0.5 + 0.5 * Node(i));
                    for (int j = 0; j < 8; ++j)
                    {
                        sum += Weight(i) * Weight(j) * Jacobian(u, 0.5 + 0.5 * Node(j));
                    }
                }
                area = 0.25 * (to - from) * sum;
            }
            return (Float)area;
        }

        // Uniform in (u, v): the density by area is 1 / ((to - from) |dS/du x dS/dv|), exactly
        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            double along = from + (to - from) * u.x, across = u.y;
            double jacobian = Jacobian(along, across);
            if (!(jacobian > 0))
            {
                return default;
            }
            Geometry(along, across, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv);
            Normal3f n = pbrt.Normalize((Normal3f)pbrt.Cross(dpdu, dpdv));
            if (settings.reverseOrientation ^ settings.transformSwapsHandedness)
            {
                n = -n;
            }
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(p.x, p.y, p.z));
            return new ShapeSample(new Interaction(new Point3fi(p, pError), n, uv), (Float)(1 / ((to - from) * jacobian)));
        }

        public override Float PDF(Interaction intr)
        {
            double u = (double)intr.uv.x * surface.Count - surface.Index, v = (double)intr.uv.y * surface.Pieces - surface.Piece;
            return AreaDensity(Math.Max(from, Math.Min(to, u)), Math.Max(0, Math.Min(1, v)));
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return PatchSampling.ToSolidAngle(Sample(u), ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            if (!Trace(ray, Infinity, out double u, out double v, out double t, out double length))
            {
                return 0;
            }
            Geometry(u, v, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv);
            Normal3f n = pbrt.Normalize((Normal3f)pbrt.Cross(dpdu, dpdv));
            return PatchSampling.AreaToSolidAngle(AreaDensity(u, v), n, p, ctx, wi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SweepShape curve: %d of %d degree: %d ]", surface.Index, surface.Count, surface.Degree);
        }

        // SweepShape Private Methods
        private static double Node(int i)
        {
            switch (i)
            {
            case 0: return -0.9602898564975363;
            case 1: return -0.7966664774136267;
            case 2: return -0.5255324099163290;
            case 3: return -0.1834346424956498;
            case 4: return 0.1834346424956498;
            case 5: return 0.5255324099163290;
            case 6: return 0.7966664774136267;
            default: return 0.9602898564975363;
            }
        }

        private static double Weight(int i)
        {
            switch (i)
            {
            case 0:
            case 7: return 0.1012285362903763;
            case 1:
            case 6: return 0.2223810344533745;
            case 2:
            case 5: return 0.3137066458778873;
            default: return 0.3626837833783620;
            }
        }

        // |dS/du x dS/dv| in render space
        private double Jacobian(double u, double v)
        {
            surface.Evaluate(u, v, out SdfVector p, out SdfVector du, out SdfVector dv);
            Vector3f a = settings.renderFromObject.Apply(new Vector3f((Float)du.x, (Float)du.y, (Float)du.z));
            Vector3f b = settings.renderFromObject.Apply(new Vector3f((Float)dv.x, (Float)dv.y, (Float)dv.z));
            return pbrt.Length(pbrt.Cross(a, b));
        }

        private Float AreaDensity(double u, double v)
        {
            double jacobian = Jacobian(u, v);
            double pdf = 1 / ((to - from) * jacobian);
            return jacobian > 0 && !double.IsInfinity(pdf) ? (Float)pdf : 0;
        }

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
            return surface.Intersect(origin, direction, e1, e2, from, to, ref t, ref u, ref v);
        }

        private void Geometry(double u, double v, out Point3f p, out Vector3f dpdu, out Vector3f dpdv, out Point2f uv)
        {
            surface.Evaluate(u, v, out SdfVector position, out SdfVector du, out SdfVector dv);
            Transform renderFromObject = settings.renderFromObject;
            p = renderFromObject.Apply(new Point3f((Float)position.x, (Float)position.y, (Float)position.z));
            dpdu = renderFromObject.Apply(new Vector3f((Float)(du.x * surface.Count), (Float)(du.y * surface.Count), (Float)(du.z * surface.Count)));
            dpdv = renderFromObject.Apply(new Vector3f((Float)(dv.x * surface.Pieces), (Float)(dv.y * surface.Pieces), (Float)(dv.z * surface.Pieces)));
            uv = new Point2f((Float)((surface.Index + u) / surface.Count), (Float)((surface.Piece + v) / surface.Pieces));
        }

        // SweepShape Private Members
        private readonly BezierSurfaceSettings settings;
        private readonly SweepSurface surface;
        private readonly double from, to;
        private readonly Bounds3f bounds;
        private double area;
    }

    // Sweeps Definition
    public static class Sweeps
    {
        // Sweeps Public Methods

        // The surfaces of a strand of cubic Bezier curves (points: 3 k + 1 of them) and a cross section of `pieces` Bezier
        // pieces of the degree (pieces * degree + 1 points and weights for each section, the pieces sharing their ends).
        // normals: one for each point of the strand, or null (a normal carried along the strand without twist).
        // crossX, crossY: the sections one after the other: one (the same all along), two (the start and the end of the strand,
        // in a line between) or one for each point of the strand. scales: one for each point of the strand.
        // adaptive: each piece is brought down to the least degree that draws it (SweepSurface.Reduce).
        public static List<SweepSurface> Surfaces(SdfVector[] points, SdfVector[] normals, double[] crossX, double[] crossY, double[] crossWeights, double[] scales,
                                                 int degree, int pieces, bool adaptive)
        {
            int count = points.Length, curves = (count - 1) / 3, perSection = pieces * degree + 1, sections = crossX.Length / perSection;
            // The frames at the points: the bitangent and the normal across the tangent of the curve there
            SdfVector[] bitangents = new SdfVector[count], uprights = new SdfVector[count];
            SdfVector carried = default;
            for (int i = 0; i < count; ++i)
            {
                int curve = Math.Min(i / 3, curves - 1), local = i - 3 * curve;
                SdfVector p0 = points[3 * curve], p1 = points[3 * curve + 1], p2 = points[3 * curve + 2], p3 = points[3 * curve + 3];
                double u = local / 3.0, a = 1 - u;
                SdfVector tangent = (p1 - p0) * (a * a) + (p2 - p1) * (2 * a * u) + (p3 - p2) * (u * u);
                if (!(tangent.LengthSquared() > 0))
                {
                    tangent = p3 - p0;
                }
                tangent = tangent.Normalized();
                SdfVector normal = normals != null ? normals[i] : i == 0 ? RibbonBuilder.OrthogonalTo(tangent) : carried;
                SdfVector bitangent = SdfVector.Cross(normal, tangent);
                if (!(bitangent.LengthSquared() > 0))
                {
                    bitangent = RibbonBuilder.OrthogonalTo(tangent);
                }
                bitangents[i] = bitangent.Normalized();
                uprights[i] = SdfVector.Cross(tangent, bitangents[i]);
                carried = uprights[i];
            }
            List<SweepSurface> surfaces = new List<SweepSurface>();
            for (int curve = 0; curve < curves; ++curve)
            {
                for (int piece = 0; piece < pieces; ++piece)
                {
                    SdfVector[] net = new SdfVector[4 * (degree + 1)];
                    double[] weights = new double[degree + 1];
                    for (int j = 0; j <= degree; ++j)
                    {
                        int crossIndex = piece * degree + j;
                        weights[j] = crossWeights[crossIndex];
                        for (int i = 0; i < 4; ++i)
                        {
                            int point = 3 * curve + i;
                            double x, y;
                            if (sections == 1)
                            {
                                x = crossX[crossIndex];
                                y = crossY[crossIndex];
                            }
                            else if (sections == 2)
                            {
                                double along = (double)point / (count - 1);
                                x = crossX[crossIndex] + along * (crossX[perSection + crossIndex] - crossX[crossIndex]);
                                y = crossY[crossIndex] + along * (crossY[perSection + crossIndex] - crossY[crossIndex]);
                            }
                            else
                            {
                                x = crossX[point * perSection + crossIndex];
                                y = crossY[point * perSection + crossIndex];
                            }
                            net[4 * j + i] = points[point] + (bitangents[point] * x + uprights[point] * y) * scales[point];
                        }
                    }
                    int reduced = degree;
                    if (adaptive)
                    {
                        SweepSurface.Reduce(ref net, ref weights, ref reduced);
                    }
                    surfaces.Add(new SweepSurface(net, weights, reduced, curve, curves, piece, pieces));
                }
            }
            return surfaces;
        }

        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters, FileLoc loc)
        {
            List<Point3f> pointList = parameters.GetPoint3fArray("P");
            List<Normal3f> normalList = parameters.GetNormal3fArray("N");
            List<Point2f> crossList = parameters.GetPoint2fArray("cross");
            List<float> weightList = parameters.GetFloatArray("crossweights");
            List<float> scaleList = parameters.GetFloatArray("scale");
            int degree = parameters.GetOneInt("crossdegree", 2);
            int sections = parameters.GetOneInt("sections", 1);
            int splitDepth = parameters.GetOneInt("splitdepth", -1);
            bool adaptive = parameters.GetOneBool("adaptive", true);
            int count = pointList.Count;
            if (count < 4 || (count - 1) % 3 != 0)
            {
                pbrt.ErrorExit(loc, "sweep: \"P\" needs 3 k + 1 points: the cubic Bezier curves of a strand, each starting at the end of the one before.");
            }
            if (degree < 1 || degree > 3)
            {
                pbrt.ErrorExit(loc, "sweep: \"crossdegree\" is 1, 2 or 3.");
            }
            if (sections != 1 && sections != 2 && sections != count)
            {
                pbrt.ErrorExit(loc, "sweep: \"sections\" is 1 (one cross section all along), 2 (the start and the end) or the number of points of \"P\".");
            }
            if (crossList.Count == 0 || crossList.Count % sections != 0 || (crossList.Count / sections - 1) % degree != 0 || crossList.Count / sections < degree + 1)
            {
                pbrt.ErrorExit(loc, "sweep: \"cross\" needs, for each of the \"sections\", the points of Bezier pieces of the degree \"crossdegree\", each starting at " +
                                    "the end of the one before (pieces * degree + 1 points).");
            }
            int perSection = crossList.Count / sections, pieces = (perSection - 1) / degree;
            if (weightList.Count != 0 && weightList.Count != perSection)
            {
                pbrt.ErrorExit(loc, "sweep: \"crossweights\" needs one weight for each point of a cross section.");
            }
            if (normalList.Count != 0 && normalList.Count != 2 && normalList.Count != count)
            {
                pbrt.ErrorExit(loc, "sweep: \"N\" needs two normals (the start and the end of the strand) or one for each point of \"P\".");
            }
            if (scaleList.Count > 2 && scaleList.Count != count)
            {
                pbrt.ErrorExit(loc, "sweep: \"scale\" needs one value, two (the start and the end of the strand) or one for each point of \"P\".");
            }
            if (splitDepth < -1 || splitDepth > 6)
            {
                pbrt.ErrorExit(loc, "sweep: \"splitdepth\" is from 0 to 6 (without it each curve is cut by its length and the width of its cross section).");
            }
            SdfVector[] points = new SdfVector[count];
            SdfVector[] normals = normalList.Count > 0 ? new SdfVector[count] : null;
            double[] scales = new double[count];
            for (int i = 0; i < count; ++i)
            {
                double along = (double)i / (count - 1);
                points[i] = new SdfVector(pointList[i].x, pointList[i].y, pointList[i].z);
                if (normalList.Count == 2)
                {
                    normals[i] = new SdfVector(normalList[0].x + along * (normalList[1].x - normalList[0].x), normalList[0].y + along * (normalList[1].y - normalList[0].y),
                                               normalList[0].z + along * (normalList[1].z - normalList[0].z));
                }
                else if (normalList.Count == count)
                {
                    normals[i] = new SdfVector(normalList[i].x, normalList[i].y, normalList[i].z);
                }
                scales[i] = scaleList.Count == 0 ? 1 : scaleList.Count == 1 ? scaleList[0] : scaleList.Count == 2 ? scaleList[0] + along * (scaleList[1] - scaleList[0]) : scaleList[i];
            }
            double[] crossX = new double[crossList.Count], crossY = new double[crossList.Count], crossWeights = new double[perSection];
            for (int i = 0; i < crossList.Count; ++i)
            {
                crossX[i] = crossList[i].x;
                crossY[i] = crossList[i].y;
            }
            for (int i = 0; i < perSection; ++i)
            {
                crossWeights[i] = weightList.Count > 0 ? weightList[i] : 1;
                if (!(crossWeights[i] > 0))
                {
                    pbrt.ErrorExit(loc, "sweep: the weights of \"crossweights\" are above zero.");
                }
            }
            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = renderFromObject;
            settings.objectFromRender = objectFromRender;
            settings.reverseOrientation = reverseOrientation;
            settings.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            foreach (SweepSurface surface in Surfaces(points, normals, crossX, crossY, crossWeights, scales, degree, pieces, adaptive))
            {
                int parts = splitDepth < 0 ? surface.Parts() : 1 << splitDepth;
                for (int part = 0; part < parts; ++part)
                {
                    shapes.push_back(new SweepShape(settings, surface, (double)part / parts, (double)(part + 1) / parts));
                }
            }
            return shapes;
        }
    }
}
