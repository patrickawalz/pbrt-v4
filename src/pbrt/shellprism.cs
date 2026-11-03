// (not in pbrt-v4) shellprism.cs
//
// The geometry of a shell: the layer between a base triangle and its offset triangle (a prism whose sides are bilinear patches), in
// double precision. A point of the shell has canonical coordinates (alpha, beta, h): the barycentric coordinates in the triangle
// S0(h) S1(h) S2(h) with Si(h) = Pi + h Ni, and the height h in [0, 1]. This is the space of Ogaki, "Nonlinear Ray Tracing for
// Displacement and Shell Mapping" (SIGGRAPH Asia 2023), which Kuwana and Fujishiro (2024) and the displacement shape of this port
// build on.
//
//  * Inverse(): world point -> (alpha, beta, h), by the cubic <q - S0(h), N(h)> = 0 (Jeschke et al. 2007).
//  * RayCoefficients(): a straight ray in the shell is, in canonical space, alpha(h) = A(h) / D(h), beta(h) = B(h) / D(h) with
//    quadratic A, B, D (Ogaki, Eq. 7), and in texture space the same with the uv of the base vertices mixed in (Eq. 8).
//  * EntryExit(): where a ray enters and leaves the prism (the two bilinear-patch or triangle faces it crosses) and the height there.

using System;

namespace pbrt
{

    // ShellPolynomial Definition: real roots of low degree polynomials
    public static class ShellPolynomial
    {
        // The roots of c0 + c1 x + c2 x^2 within [lower, upper], in increasing order; returns their number (0 to 2)
        public static int QuadraticRoots(double c0, double c1, double c2, double lower, double upper, double[] roots, int count = 0)
        {
            double scale = Math.Max(Math.Abs(c0), Math.Max(Math.Abs(c1), Math.Abs(c2)));
            if (scale == 0)
            {
                return count;
            }
            if (Math.Abs(c2) <= 1e-14 * scale)
            {
                if (c1 != 0)
                {
                    double x = -c0 / c1;
                    if (x >= lower && x <= upper)
                    {
                        roots[count++] = x;
                    }
                }
                return count;
            }
            double discriminant = c1 * c1 - 4 * c2 * c0;
            if (discriminant < 0)
            {
                return count;
            }
            double root = Math.Sqrt(discriminant);
            double q = -0.5 * (c1 + (c1 >= 0 ? root : -root));
            double r0 = q / c2;
            double r1 = q != 0 ? c0 / q : r0;
            if (r0 > r1)
            {
                double swap = r0;
                r0 = r1;
                r1 = swap;
            }
            if (r0 >= lower && r0 <= upper)
            {
                roots[count++] = r0;
            }
            if (r1 >= lower && r1 <= upper && r1 != r0)
            {
                roots[count++] = r1;
            }
            return count;
        }

        // The roots of c0 + c1 x + c2 x^2 + c3 x^3 within [lower, upper], in increasing order (0 to 3). The interval is split at the
        // critical points of the polynomial, and a root is found in every piece where the sign changes (bisection with Newton steps),
        // which is robust where the closed formula for the cubic loses digits.
        public static int CubicRoots(double c0, double c1, double c2, double c3, double lower, double upper, double[] roots)
        {
            double scale = Math.Max(Math.Max(Math.Abs(c0), Math.Abs(c1)), Math.Max(Math.Abs(c2), Math.Abs(c3)));
            if (scale == 0)
            {
                return 0;
            }
            if (Math.Abs(c3) <= 1e-14 * scale)
            {
                return QuadraticRoots(c0, c1, c2, lower, upper, roots);
            }
            double[] critical = new double[2];
            int criticalCount = QuadraticRoots(c1, 2 * c2, 3 * c3, lower, upper, critical);
            double[] breaks = new double[4];
            int breakCount = 0;
            breaks[breakCount++] = lower;
            for (int i = 0; i < criticalCount; ++i)
            {
                breaks[breakCount++] = critical[i];
            }
            breaks[breakCount++] = upper;
            int count = 0;
            double previous = breaks[0];
            double previousValue = Evaluate(c0, c1, c2, c3, previous);
            for (int i = 1; i < breakCount; ++i)
            {
                double next = breaks[i];
                double nextValue = Evaluate(c0, c1, c2, c3, next);
                if (previousValue == 0)
                {
                    if (count == 0 || roots[count - 1] != previous)
                    {
                        roots[count++] = previous;
                    }
                }
                else if ((previousValue < 0) != (nextValue < 0) && nextValue != 0)
                {
                    roots[count++] = Bisect(c0, c1, c2, c3, previous, next, previousValue);
                }
                previous = next;
                previousValue = nextValue;
            }
            if (previousValue == 0 && (count == 0 || roots[count - 1] != previous))
            {
                roots[count++] = previous;
            }
            return count;
        }

        public static double Evaluate(double c0, double c1, double c2, double c3, double x)
        {
            return ((c3 * x + c2) * x + c1) * x + c0;
        }

        private static double Bisect(double c0, double c1, double c2, double c3, double lower, double upper, double lowerValue)
        {
            bool lowerNegative = lowerValue < 0;
            double x = 0.5 * (lower + upper);
            for (int iteration = 0; iteration < 100; ++iteration)
            {
                double value = Evaluate(c0, c1, c2, c3, x);
                if (value == 0)
                {
                    return x;
                }
                if ((value < 0) == lowerNegative)
                {
                    lower = x;
                }
                else
                {
                    upper = x;
                }
                double derivative = (3 * c3 * x + 2 * c2) * x + c1;
                double newton = derivative != 0 ? x - value / derivative : double.NaN;
                double next = newton > lower && newton < upper ? newton : 0.5 * (lower + upper);
                if (Math.Abs(next - x) <= 1e-16 * Math.Max(Math.Abs(x), 1e-300) || upper - lower <= 1e-16 * Math.Max(Math.Abs(upper), 1e-300))
                {
                    return next;
                }
                x = next;
            }
            return x;
        }
    }

    // ShellRay Definition: a straight ray in a prism as rational functions of the height (Ogaki, Eqs. 7 and 8). Each of the three
    // polynomials is stored as three coefficients, constant term first. The sign of the denominator is made positive over the
    // height range the ray spends in the prism.
    public sealed class ShellRay
    {
        public readonly double[] alpha = new double[3];
        public readonly double[] beta = new double[3];
        public readonly double[] denominator = new double[3];

        public static double Polynomial(double[] c, double h)
        {
            return (c[2] * h + c[1]) * h + c[0];
        }
    }

    // ShellPrism Definition
    public sealed class ShellPrism
    {
        // ShellPrism Public Methods

        // p[i] are the vertices of the base triangle and n[i] the offsets to the vertices of the top triangle (the height of the
        // shell at the vertex)
        public ShellPrism(SdfVector[] p, SdfVector[] n)
        {
            this.p = new SdfVector[3] { p[0], p[1], p[2] };
            this.n = new SdfVector[3] { n[0], n[1], n[2] };
        }

        // Replaces the prism (so that one object can serve every prism a thread tests)
        public void Set(SdfVector p0, SdfVector p1, SdfVector p2, SdfVector n0, SdfVector n1, SdfVector n2)
        {
            p[0] = p0;
            p[1] = p1;
            p[2] = p2;
            n[0] = n0;
            n[1] = n1;
            n[2] = n2;
        }

        // The vertex i of the triangle at the height h
        public SdfVector Vertex(int i, double h)
        {
            return p[i] + n[i] * h;
        }

        public SdfVector Point(double alpha, double beta, double h)
        {
            return Vertex(0, h) * (1 - alpha - beta) + Vertex(1, h) * alpha + Vertex(2, h) * beta;
        }

        // The offset (not normalized) at the canonical point: d Point / d h
        public SdfVector Offset(double alpha, double beta)
        {
            return n[0] * (1 - alpha - beta) + n[1] * alpha + n[2] * beta;
        }

        // The edge vectors E0 = S1 - S0 and E1 = S2 - S0 of the triangle at the height h
        public void Edges(double h, out SdfVector e0, out SdfVector e1)
        {
            e0 = Vertex(1, h) - Vertex(0, h);
            e1 = Vertex(2, h) - Vertex(0, h);
        }

        // The (not normalized) normal of the triangle at the height h
        public SdfVector TriangleNormal(double h)
        {
            Edges(h, out SdfVector e0, out SdfVector e1);
            return SdfVector.Cross(e0, e1);
        }

        // The canonical coordinates of a point: the heights h in [lower, upper] at which the point lies in the plane of the triangle
        // at that height (roots of a cubic), each with alpha and beta from the linear system in the plane. Returns the number found.
        public int Inverse(SdfVector q, double lower, double upper, double[] heights, double[] alphas, double[] betas)
        {
            SdfVector e00 = p[1] - p[0], e10 = p[2] - p[0];
            SdfVector de0 = n[1] - n[0], de1 = n[2] - n[0];
            SdfVector a = SdfVector.Cross(e00, e10);
            SdfVector b = SdfVector.Cross(e00, de1) + SdfVector.Cross(de0, e10);
            SdfVector c = SdfVector.Cross(de0, de1);
            SdfVector r = q - p[0];
            double c0 = SdfVector.Dot(r, a);
            double c1 = SdfVector.Dot(r, b) - SdfVector.Dot(n[0], a);
            double c2 = SdfVector.Dot(r, c) - SdfVector.Dot(n[0], b);
            double c3 = -SdfVector.Dot(n[0], c);
            int count = ShellPolynomial.CubicRoots(c0, c1, c2, c3, lower, upper, heights);
            for (int i = 0; i < count; ++i)
            {
                SolveBarycentric(q, heights[i], out alphas[i], out betas[i]);
            }
            return count;
        }

        // alpha and beta of a point that lies in the plane of the triangle at the height h (least squares in the plane, which is the
        // exact solution for a point in the plane)
        public void SolveBarycentric(SdfVector q, double h, out double alpha, out double beta)
        {
            Edges(h, out SdfVector e0, out SdfVector e1);
            SdfVector r = q - Vertex(0, h);
            double g00 = SdfVector.Dot(e0, e0), g01 = SdfVector.Dot(e0, e1), g11 = SdfVector.Dot(e1, e1);
            double determinant = g00 * g11 - g01 * g01;
            double r0 = SdfVector.Dot(r, e0), r1 = SdfVector.Dot(r, e1);
            if (determinant == 0)
            {
                alpha = 0;
                beta = 0;
                return;
            }
            alpha = (r0 * g11 - r1 * g01) / determinant;
            beta = (g00 * r1 - g01 * r0) / determinant;
        }

        // The rational functions alpha(h), beta(h) of the ray o + w t (w a unit vector); see the header
        public void RayCoefficients(SdfVector o, SdfVector w, ShellRay ray)
        {
            SdfVector helper = Math.Abs(w.x) < 0.9 ? new SdfVector(1, 0, 0) : new SdfVector(0, 1, 0);
            SdfVector e0 = SdfVector.Cross(w, helper).Normalized();
            SdfVector e1 = SdfVector.Cross(w, e0);
            SdfVector[] axis = new SdfVector[2] { e0, e1 };
            // a[k, j] = <e_k, E_j(h)> and r[k] = <e_k, o - S0(h)>, each linear in h as (constant, slope)
            double[,,] a = new double[2, 2, 2];
            double[,] r = new double[2, 2];
            for (int k = 0; k < 2; ++k)
            {
                for (int j = 0; j < 2; ++j)
                {
                    a[k, j, 0] = SdfVector.Dot(axis[k], p[j + 1] - p[0]);
                    a[k, j, 1] = SdfVector.Dot(axis[k], n[j + 1] - n[0]);
                }
                r[k, 0] = SdfVector.Dot(axis[k], o - p[0]);
                r[k, 1] = -SdfVector.Dot(axis[k], n[0]);
            }
            for (int i = 0; i < 3; ++i)
            {
                ray.denominator[i] = ProductCoefficient(a, 0, 0, a, 1, 1, i) - ProductCoefficient(a, 0, 1, a, 1, 0, i);
                // alpha = (r0 a11 - r1 a01) / det and beta = (a00 r1 - a10 r0) / det
                ray.alpha[i] = LinearProduct(r, 0, a, 1, 1, i) - LinearProduct(r, 1, a, 0, 1, i);
                ray.beta[i] = LinearProduct(r, 1, a, 0, 0, i) - LinearProduct(r, 0, a, 1, 0, i);
            }
        }

        // The point of the ray at the height h (canonical coordinates and the distance along the ray)
        public void RayAt(ShellRay ray, SdfVector o, SdfVector w, double h, out double alpha, out double beta, out double t)
        {
            double d = ShellRay.Polynomial(ray.denominator, h);
            alpha = ShellRay.Polynomial(ray.alpha, h) / d;
            beta = ShellRay.Polynomial(ray.beta, h) / d;
            t = SdfVector.Dot(w, Point(alpha, beta, h) - o);
        }

        // The faces of the prism that the ray o + w t (w a unit vector) crosses at distances in (0, tMax]: the distances and heights
        // of the crossings, up to ten of them. The sides of the prism are bilinear patches whose second parameter is the height.
        public int FaceCrossings(SdfVector o, SdfVector w, double tMax, double[] distances, double[] heights, double[] roots)
        {
            int count = 0;
            // The bottom (h = 0) and top (h = 1) triangles
            for (int level = 0; level < 2; ++level)
            {
                double h = level;
                SdfVector v0 = Vertex(0, h), v1 = Vertex(1, h), v2 = Vertex(2, h);
                SdfVector normal = SdfVector.Cross(v1 - v0, v2 - v0);
                double denominator = SdfVector.Dot(normal, w);
                if (denominator == 0)
                {
                    continue;
                }
                double t = SdfVector.Dot(normal, v0 - o) / denominator;
                if (t <= 0 || t > tMax)
                {
                    continue;
                }
                SdfVector q = o + w * t;
                SolveBarycentric(q, h, out double alpha, out double beta);
                const double tolerance = 1e-12;
                if (alpha >= -tolerance && beta >= -tolerance && alpha + beta <= 1 + tolerance)
                {
                    distances[count] = t;
                    heights[count] = h;
                    ++count;
                }
            }
            // The three sides: Q(s, h) = (1 - h) ((1 - s) Pi + s Pj) + h ((1 - s) (Pi + Ni) + s (Pj + Nj))
            SdfVector helper = Math.Abs(w.x) < 0.9 ? new SdfVector(1, 0, 0) : new SdfVector(0, 1, 0);
            SdfVector e0 = SdfVector.Cross(w, helper).Normalized();
            SdfVector e1 = SdfVector.Cross(w, e0);
            for (int side = 0; side < 3; ++side)
            {
                int i = side, j = (side + 1) % 3;
                SdfVector qi = p[i], qj = p[j], ni = n[i], nj = n[j];
                // Q = Q00 + s (Q10 - Q00) + h (Q01 - Q00) + s h (Q11 - Q10 - Q01 + Q00)
                SdfVector q00 = qi, q10 = qj, q01 = qi + ni, q11 = qj + nj;
                SdfVector ds = q10 - q00, dh = q01 - q00, dsh = q11 - q10 - q01 + q00;
                double c0 = SdfVector.Dot(e0, q00 - o), a0 = SdfVector.Dot(e0, ds), b0 = SdfVector.Dot(e0, dh), m0 = SdfVector.Dot(e0, dsh);
                double c1 = SdfVector.Dot(e1, q00 - o), a1 = SdfVector.Dot(e1, ds), b1 = SdfVector.Dot(e1, dh), m1 = SdfVector.Dot(e1, dsh);
                // From g0 = c0 + a0 s + (b0 + m0 s) h = 0 and g1 = c1 + a1 s + (b1 + m1 s) h = 0, eliminating h
                double quadratic = a1 * m0 - m1 * a0;
                double linear = c1 * m0 + a1 * b0 - b1 * a0 - m1 * c0;
                double constant = c1 * b0 - b1 * c0;
                const double tolerance = 1e-12;
                int rootCount = ShellPolynomial.QuadraticRoots(constant, linear, quadratic, -tolerance, 1 + tolerance, roots);
                for (int k = 0; k < rootCount; ++k)
                {
                    double s = roots[k];
                    double d0 = b0 + m0 * s, d1 = b1 + m1 * s;
                    double h;
                    if (Math.Abs(d0) >= Math.Abs(d1))
                    {
                        if (d0 == 0)
                        {
                            continue;
                        }
                        h = -(c0 + a0 * s) / d0;
                    }
                    else
                    {
                        h = -(c1 + a1 * s) / d1;
                    }
                    if (h < -tolerance || h > 1 + tolerance)
                    {
                        continue;
                    }
                    SdfVector q = q00 + ds * s + dh * h + dsh * (s * h);
                    double t = SdfVector.Dot(w, q - o);
                    if (t <= 0 || t > tMax)
                    {
                        continue;
                    }
                    distances[count] = t;
                    heights[count] = Math.Min(Math.Max(h, 0), 1);
                    ++count;
                }
            }
            return count;
        }

        // ShellPrism Public Members
        public readonly SdfVector[] p;
        public readonly SdfVector[] n;

        // ShellPrism Private Methods

        // The coefficient of h^i in (linear a[k0, j0]) times (linear a[k1, j1])
        private static double ProductCoefficient(double[,,] a, int k0, int j0, double[,,] b, int k1, int j1, int i)
        {
            double a0 = a[k0, j0, 0], a1 = a[k0, j0, 1], b0 = b[k1, j1, 0], b1 = b[k1, j1, 1];
            if (i == 0)
            {
                return a0 * b0;
            }
            if (i == 1)
            {
                return a0 * b1 + a1 * b0;
            }
            return a1 * b1;
        }

        // The coefficient of h^i in (linear r[k]) times (linear a[k1, j1])
        private static double LinearProduct(double[,] r, int k, double[,,] a, int k1, int j1, int i)
        {
            double r0 = r[k, 0], r1 = r[k, 1], b0 = a[k1, j1, 0], b1 = a[k1, j1, 1];
            if (i == 0)
            {
                return r0 * b0;
            }
            if (i == 1)
            {
                return r0 * b1 + r1 * b0;
            }
            return r1 * b1;
        }
    }

}  // namespace pbrt
