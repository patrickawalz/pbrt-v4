// (not in pbrt-v4) cpu/specularpolynomial.cs
//
// Specular polynomials, one bounce (Fan, Guo, Wang, Xiao, Zhang, Zhou, Chen, Hong, Guo and Yan, "Specular Polynomials", ACM
// Transactions on Graphics 43(4), 2024; implemented from the paper, no code of its authors). A specular vertex x on a triangle
// with a normal that is interpolated between its corners (not normalised, so linear in the barycentric coordinates u, v) joins
// two fixed points x0 and x2 where two polynomials of (u, v) vanish:
//
//   coplanarity   A = (d0 x (x2 - x0)) . n                                   degree 2      (the paper's equation 6)
//   angle         B = (d0 . n)(d1 . t) + (d0 . t)(d1 . n),  t = n x e1       degree 4      a reflection (equation 13)
//                 B = |d0 x n|^2 d1^2 - eta^2 |d1 x n|^2 d0^2                degree 6      a refraction (equation 9, squared)
//
// with d0 = x - x0, d1 = x2 - x and eta the index of the side of x2 over that of x0. v is eliminated by the resultant of A
// and B as polynomials of v: with v1, v2 the two roots of A (complex or not), R(u) = a2(u)^m B(u, v1) B(u, v2) is a polynomial
// of u of degree at most 2 m. R is sampled at Chebyshev points, turned into its coefficients, and all its real roots in the
// triangle are isolated between the roots of its derivatives (the method of Yuksel 2022 that the paper refers to). Every root
// u gives the v of A = 0; the caller keeps those that satisfy the law of reflection or refraction itself, since the squares
// and the products also have roots that do not.
//
// Before any of this, the Bernstein coefficients of A over the triangle are looked at: a polynomial lies between the smallest
// and the largest of them (the property that "Bernstein Bounds for Caustics", Fan et al. 2025, builds its bounds on), so if
// they all have one sign the triangle has no solution, and nearly every triangle is passed over at the cost of six values.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace pbrt
{
    using static pbrt;

    // SpecularPolynomial Definition
    public static unsafe class SpecularPolynomial
    {
        // SpecularPolynomial Private Types
        struct Setup
        {
            public double x0x, x0y, x0z, x2x, x2y, x2z;
            public double p0x, p0y, p0z, e1x, e1y, e1z, e2x, e2y, e2z;
            public double n0x, n0y, n0z, g1x, g1y, g1z, g2x, g2y, g2z;
            public bool reflection;
            public double eta;
        }

        static void Evaluate(ref Setup s, double u, double v, out double A, out double B)
        {
            double xx = s.p0x + u * s.e1x + v * s.e2x, xy = s.p0y + u * s.e1y + v * s.e2y, xz = s.p0z + u * s.e1z + v * s.e2z;
            double nx = s.n0x + u * s.g1x + v * s.g2x, ny = s.n0y + u * s.g1y + v * s.g2y, nz = s.n0z + u * s.g1z + v * s.g2z;
            double ax = xx - s.x0x, ay = xy - s.x0y, az = xz - s.x0z;       // d0
            double bx = s.x2x - xx, by = s.x2y - xy, bz = s.x2z - xz;       // d1
            double wx = s.x2x - s.x0x, wy = s.x2y - s.x0y, wz = s.x2z - s.x0z;
            A = (ay * wz - az * wy) * nx + (az * wx - ax * wz) * ny + (ax * wy - ay * wx) * nz;
            if (s.reflection)
            {
                double tx = ny * s.e1z - nz * s.e1y, ty = nz * s.e1x - nx * s.e1z, tz = nx * s.e1y - ny * s.e1x;
                B = (ax * nx + ay * ny + az * nz) * (bx * tx + by * ty + bz * tz) + (ax * tx + ay * ty + az * tz) * (bx * nx + by * ny + bz * nz);
            }
            else
            {
                double cx = ay * nz - az * ny, cy = az * nx - ax * nz, cz = ax * ny - ay * nx;
                double dx = by * nz - bz * ny, dy = bz * nx - bx * nz, dz = bx * ny - by * nx;
                B = (cx * cx + cy * cy + cz * cz) * (bx * bx + by * by + bz * bz) -
                    s.eta * s.eta * (dx * dx + dy * dy + dz * dz) * (ax * ax + ay * ay + az * az);
            }
        }

        // The resultant of A and B as polynomials of v, at a value of u
        static double Resultant(ref Setup s, double u, int degree, double[] nodes, double[] values, double[] divided)
        {
            // A as a quadratic of v, from three of its values
            Evaluate(ref s, u, 0, out double A0, out double unused0);
            Evaluate(ref s, u, 1, out double A1, out double unused1);
            Evaluate(ref s, u, -1, out double Am, out double unused2);
            double a0 = A0, a2 = (A1 + Am) / 2 - A0, a1 = (A1 - Am) / 2;
            // B as a polynomial of v, by its divided differences on degree + 1 nodes
            for (int i = 0; i <= degree; ++i)
            {
                Evaluate(ref s, u, nodes[i], out double unused3, out values[i]);
                divided[i] = values[i];
            }
            for (int level = 1; level <= degree; ++level)
            {
                for (int i = degree; i >= level; --i)
                {
                    divided[i] = (divided[i] - divided[i - 1]) / (nodes[i] - nodes[i - level]);
                }
            }
            if (Math.Abs(a2) < 1e-300)
            {
                // (A is linear in v here: its one root, and the leading coefficient of the resultant's limit)
                if (a1 == 0)
                {
                    return 0;
                }
                double root = -a0 / a1, value = divided[degree];
                for (int i = degree - 1; i >= 0; --i)
                {
                    value = value * (root - nodes[i]) + divided[i];
                }
                return Math.Pow(a1, degree) * value;
            }
            Complex discriminant = Complex.Sqrt(new Complex(a1 * a1 - 4 * a2 * a0, 0));
            Complex v1 = (-a1 + discriminant) / (2 * a2), v2 = (-a1 - discriminant) / (2 * a2);
            Complex b1 = divided[degree], b2 = divided[degree];
            for (int i = degree - 1; i >= 0; --i)
            {
                b1 = b1 * (v1 - nodes[i]) + divided[i];
                b2 = b2 * (v2 - nodes[i]) + divided[i];
            }
            return Math.Pow(a2, degree) * (b1 * b2).Real;
        }

        static double Horner(double[] c, int degree, double t)
        {
            double value = c[degree];
            for (int i = degree - 1; i >= 0; --i)
            {
                value = value * t + c[i];
            }
            return value;
        }

        // The real roots in [lo, hi] of a polynomial (coefficients c[0] .. c[degree]): between two neighbouring roots of its
        // derivative it has at most one, found by bisection where its sign changes
        static void RealRoots(double[] c, int degree, double lo, double hi, List<double> roots)
        {
            roots.Clear();
            while (degree > 0 && c[degree] == 0)
            {
                --degree;
            }
            if (degree == 0)
            {
                return;
            }
            if (degree == 1)
            {
                double root = -c[0] / c[1];
                if (root >= lo && root <= hi)
                {
                    roots.Add(root);
                }
                return;
            }
            double[] derivative = new double[degree];
            for (int i = 1; i <= degree; ++i)
            {
                derivative[i - 1] = i * c[i];
            }
            List<double> turning = new List<double>();
            RealRoots(derivative, degree - 1, lo, hi, turning);
            turning.Add(hi);
            double left = lo, valueLeft = Horner(c, degree, lo);
            foreach (double right in turning)
            {
                double valueRight = Horner(c, degree, right);
                if (valueLeft == 0)
                {
                    roots.Add(left);
                }
                else if (valueLeft * valueRight < 0)
                {
                    double a = left, b = right, fa = valueLeft;
                    for (int iteration = 0; iteration < 80 && b - a > 1e-15 * Math.Max(1.0, Math.Abs(a)); ++iteration)
                    {
                        double middle = 0.5 * (a + b), fm = Horner(c, degree, middle);
                        if (fa * fm <= 0)
                        {
                            b = middle;
                        }
                        else
                        {
                            a = middle;
                            fa = fm;
                        }
                    }
                    roots.Add(0.5 * (a + b));
                }
                left = right;
                valueLeft = valueRight;
            }
            if (valueLeft == 0)
            {
                roots.Add(left);
            }
        }

        // SpecularPolynomial Public Methods
        // Every point of a triangle where the polynomials of a reflection, or of a refraction of relative index eta (the side
        // of x2 over the side of x0), between x0 and x2 vanish
        public static void Roots(Point3f x0, Point3f x2, Point3f p0, Point3f p1, Point3f p2, Vector3f n0, Vector3f n1, Vector3f n2,
                                 bool reflection, Float eta, List<Point3f> points, double margin = 0.01)
        {
            Setup s = new Setup();
            s.x0x = x0.x; s.x0y = x0.y; s.x0z = x0.z;
            s.x2x = x2.x; s.x2y = x2.y; s.x2z = x2.z;
            s.p0x = p0.x; s.p0y = p0.y; s.p0z = p0.z;
            s.e1x = (double)p1.x - p0.x; s.e1y = (double)p1.y - p0.y; s.e1z = (double)p1.z - p0.z;
            s.e2x = (double)p2.x - p0.x; s.e2y = (double)p2.y - p0.y; s.e2z = (double)p2.z - p0.z;
            s.n0x = n0.x; s.n0y = n0.y; s.n0z = n0.z;
            s.g1x = (double)n1.x - n0.x; s.g1y = (double)n1.y - n0.y; s.g1z = (double)n1.z - n0.z;
            s.g2x = (double)n2.x - n0.x; s.g2y = (double)n2.y - n0.y; s.g2z = (double)n2.z - n0.z;
            s.reflection = reflection;
            s.eta = eta;

            // The Bernstein coefficients of the coplanarity polynomial (degree 2) over the triangle: its values at the corners,
            // and for every edge twice the value at the middle less the mean of the two corners
            Evaluate(ref s, 0, 0, out double c00, out double unused0);
            Evaluate(ref s, 1, 0, out double c10, out double unused1);
            Evaluate(ref s, 0, 1, out double c01, out double unused2);
            Evaluate(ref s, 0.5, 0, out double m0, out double unused3);
            Evaluate(ref s, 0, 0.5, out double m1, out double unused4);
            Evaluate(ref s, 0.5, 0.5, out double m2, out double unused5);
            double b0 = 2 * m0 - 0.5 * (c00 + c10), b1 = 2 * m1 - 0.5 * (c00 + c01), b2 = 2 * m2 - 0.5 * (c10 + c01);
            double smallest = Math.Min(Math.Min(c00, c10), Math.Min(Math.Min(c01, b0), Math.Min(b1, b2)));
            double largest = Math.Max(Math.Max(c00, c10), Math.Max(Math.Max(c01, b0), Math.Max(b1, b2)));
            if (smallest > 0 || largest < 0)
            {
                return;
            }

            // The resultant at Chebyshev points of u in [0, 1], and its coefficients
            int m = reflection ? 4 : 6, degree = 2 * m, count = degree + 1;
            double[] nodes = new double[m + 1], values = new double[m + 1], divided = new double[m + 1];
            for (int i = 0; i <= m; ++i)
            {
                nodes[i] = -0.5 + 2.0 * i / m;
            }
            double[] samples = new double[count], chebyshev = new double[count];
            double scale = 0;
            for (int j = 0; j < count; ++j)
            {
                double t = Math.Cos(Math.PI * (j + 0.5) / count);
                samples[j] = Resultant(ref s, 0.5 * (t + 1), m, nodes, values, divided);
                scale = Math.Max(scale, Math.Abs(samples[j]));
            }
            if (!(scale > 0) || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                return;
            }
            for (int k = 0; k < count; ++k)
            {
                double sum = 0;
                for (int j = 0; j < count; ++j)
                {
                    sum += samples[j] / scale * Math.Cos(Math.PI * k * (j + 0.5) / count);
                }
                chebyshev[k] = (k == 0 ? 1.0 : 2.0) / count * sum;
            }
            // From the Chebyshev polynomials to the powers of t: T0 = 1, T1 = t, T(k+1) = 2 t T(k) - T(k-1)
            double[] power = new double[count], previous = new double[count], current = new double[count], next = new double[count];
            previous[0] = 1;
            current[1] = 1;
            power[0] += chebyshev[0];
            if (count > 1)
            {
                power[1] += chebyshev[1];
            }
            for (int k = 2; k < count; ++k)
            {
                Array.Clear(next, 0, count);
                for (int i = 0; i < count - 1; ++i)
                {
                    next[i + 1] += 2 * current[i];
                }
                for (int i = 0; i < count; ++i)
                {
                    next[i] -= previous[i];
                    power[i] += chebyshev[k] * next[i];
                }
                double[] swapped = previous;
                previous = current;
                current = next;
                next = swapped;
            }
            List<double> roots = new List<double>();
            RealRoots(power, degree, -1.02, 1.02, roots);

            foreach (double t in roots)
            {
                double u = 0.5 * (t + 1);
                // The v of the coplanarity polynomial at this u
                Evaluate(ref s, u, 0, out double A0, out double unused6);
                Evaluate(ref s, u, 1, out double A1, out double unused7);
                Evaluate(ref s, u, -1, out double Am, out double unused8);
                double a0 = A0, a2 = (A1 + Am) / 2 - A0, a1 = (A1 - Am) / 2;
                double v1, v2;
                if (Math.Abs(a2) < 1e-14 * (Math.Abs(a1) + Math.Abs(a0)))
                {
                    if (a1 == 0)
                    {
                        continue;
                    }
                    v1 = v2 = -a0 / a1;
                }
                else
                {
                    double discriminant = a1 * a1 - 4 * a2 * a0;
                    if (discriminant < 0)
                    {
                        // (within the accuracy of the root a pair of roots that meet can come out just complex)
                        if (discriminant < -1e-8 * (a1 * a1 + Math.Abs(4 * a2 * a0)))
                        {
                            continue;
                        }
                        discriminant = 0;
                    }
                    double root = Math.Sqrt(discriminant);
                    v1 = (-a1 + root) / (2 * a2);
                    v2 = (-a1 - root) / (2 * a2);
                }
                for (int which = 0; which < (v1 == v2 ? 1 : 2); ++which)
                {
                    double v = which == 0 ? v1 : v2;
                    if (u < -margin || v < -margin || u + v > 1 + margin)
                    {
                        continue;
                    }
                    points.Add(new Point3f((Float)(s.p0x + u * s.e1x + v * s.e2x), (Float)(s.p0y + u * s.e1y + v * s.e2y),
                                           (Float)(s.p0z + u * s.e1z + v * s.e2z)));
                }
            }
        }
    }

}  // namespace pbrt
