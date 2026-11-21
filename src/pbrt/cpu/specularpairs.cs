// (not in pbrt-v4) cpu/specularpairs.cs
//
// Chains of two specular reflections between two fixed points, on triangles with interpolated normals: which pairs of
// triangles can hold one, and where on them.
//
// The pairs are found as in "Path Cuts" (Wang, Chiang, Yuksel and Yan 2020; the pruning that the code of "Specular
// Polynomials", Fan et al. 2024, takes its triangle tuples from): the triangles of a mesh are in a tree whose nodes hold a
// box of their positions and a box of their normals; a pair of nodes is given up when interval arithmetic shows that at
// one of the two vertices the half vector cannot be along a normal of the node, and the larger node is split otherwise.
//
// A pair of triangles is solved by the bivariate specular polynomials of Fan et al. 2024 (their section 3.5, the case RR),
// implemented from the paper, no code of its authors. With (u, v) the barycentric coordinates of the first vertex x1, its
// normal n1 linear in (u, v) and d0 = x1 - x0:
//
//   the reflected direction        d = (n1 . n1) d0 - 2 (d0 . n1) n1                               degree 3
//   the second vertex              by the ray x1 + t d against the second triangle (Moeller and Trumbore): its barycentric
//                                  coordinates are U / K and V / K with U, V of degree 4 and K of degree 3 (the paper's
//                                  rational coordinate mapping, equation 14), so that K x2 and K n2 are of degree 4
//   coplanarity at x2              A = (d x (xL - x1)) . (K n2)                                    degree 8
//   angle at x2                    B = (d . N)(D . t) + (D . N)(d . t), N = K n2, D = K (xL - x2), t = N x e   degree 15
//
// u is eliminated by the Bezout resultant of A and B as polynomials of u. As in the authors' code (its "cutoff_matrix"),
// both are first cut to the degree 8, with lengths in units of the distance from x0 to the triangle, so that the powers
// left out are small when the triangles are small against the distances: an approximation (the paper calls its method
// accurate for one bounce). Without the cut the sign of the determinant is noise in floating point. For a value of v the
// Bezout matrix (8 by 8) is filled from the coefficients and its determinant is taken by elimination. The roots of that determinant in v are
// isolated by its changes of sign over samples and bisection (the determinant is not expanded into a polynomial); for each
// the roots of A in u are found the same way, and the points that satisfy the law of reflection at both vertices are
// returned. They are seeds: the caller brings them to the exact chain by the manifold walk.

using System;
using System.Collections.Generic;

namespace pbrt
{
    using static pbrt;

    // SpecularPairs Definition
    public static class SpecularPairs
    {
        // A triangle with the normals of its corners
        public sealed class Triangle
        {
            public Point3f p0, p1, p2;
            public Vector3f n0, n1, n2;
        }

        // Interval Definition
        public struct Interval
        {
            public Interval(double lo, double hi)
            {
                this.lo = lo;
                this.hi = hi;
            }

            public static Interval operator +(Interval a, Interval b)
            {
                return new Interval(a.lo + b.lo, a.hi + b.hi);
            }

            public static Interval operator -(Interval a, Interval b)
            {
                return new Interval(a.lo - b.hi, a.hi - b.lo);
            }

            public static Interval operator *(Interval a, Interval b)
            {
                double p0 = a.lo * b.lo, p1 = a.lo * b.hi, p2 = a.hi * b.lo, p3 = a.hi * b.hi;
                return new Interval(Math.Min(Math.Min(p0, p1), Math.Min(p2, p3)), Math.Max(Math.Max(p0, p1), Math.Max(p2, p3)));
            }

            public bool CoversZero()
            {
                return lo <= 0 && hi >= 0;
            }

            // The largest and the smallest square of a number of the interval
            public double LargestSquare()
            {
                return Math.Max(lo * lo, hi * hi);
            }

            public double SmallestSquare()
            {
                return CoversZero() ? 0 : Math.Min(lo * lo, hi * hi);
            }

            public double lo, hi;
        }

        // Interval3 Definition
        public struct Interval3
        {
            public Interval3(Interval x, Interval y, Interval z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public Interval3(Point3f p)
            {
                x = new Interval(p.x, p.x);
                y = new Interval(p.y, p.y);
                z = new Interval(p.z, p.z);
            }

            public static Interval3 operator +(Interval3 a, Interval3 b)
            {
                return new Interval3(a.x + b.x, a.y + b.y, a.z + b.z);
            }

            public static Interval3 operator -(Interval3 a, Interval3 b)
            {
                return new Interval3(a.x - b.x, a.y - b.y, a.z - b.z);
            }

            public void Include(double px, double py, double pz)
            {
                x = new Interval(Math.Min(x.lo, px), Math.Max(x.hi, px));
                y = new Interval(Math.Min(y.lo, py), Math.Max(y.hi, py));
                z = new Interval(Math.Min(z.lo, pz), Math.Max(z.hi, pz));
            }

            public Interval x, y, z;
        }

        // A node of the tree of the triangles of a mesh: a leaf has one triangle
        public sealed class Node
        {
            public Interval3 position, normal;
            public double size;
            public Node left, right;
            public Triangle triangle;
        }

        // SpecularPairs Public Methods
        public static Node Build(List<Triangle> triangles, int start, int end)
        {
            Node node = new Node();
            double infinity = double.PositiveInfinity;
            node.position = new Interval3(new Interval(infinity, -infinity), new Interval(infinity, -infinity), new Interval(infinity, -infinity));
            node.normal = node.position;
            for (int i = start; i < end; ++i)
            {
                Triangle t = triangles[i];
                node.position.Include(t.p0.x, t.p0.y, t.p0.z);
                node.position.Include(t.p1.x, t.p1.y, t.p1.z);
                node.position.Include(t.p2.x, t.p2.y, t.p2.z);
                Vector3f n0 = Normalize(t.n0), n1 = Normalize(t.n1), n2 = Normalize(t.n2);
                node.normal.Include(n0.x, n0.y, n0.z);
                node.normal.Include(n1.x, n1.y, n1.z);
                node.normal.Include(n2.x, n2.y, n2.z);
            }
            double dx = node.position.x.hi - node.position.x.lo, dy = node.position.y.hi - node.position.y.lo,
                   dz = node.position.z.hi - node.position.z.lo;
            node.size = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (end - start == 1)
            {
                node.triangle = triangles[start];
                return node;
            }
            // (the triangles in the order of their centres along the longest side of the box, cut in the middle)
            int axis = dx >= dy && dx >= dz ? 0 : dy >= dz ? 1 : 2;
            triangles.Sort(start, end - start, Comparer<Triangle>.Create((Triangle a, Triangle b) =>
                (a.p0[axis] + a.p1[axis] + a.p2[axis]).CompareTo(b.p0[axis] + b.p1[axis] + b.p2[axis])));
            int middle = (start + end) / 2;
            node.left = Build(triangles, start, middle);
            node.right = Build(triangles, middle, end);
            return node;
        }

        // The pairs of triangles of two trees that can hold a chain x -> first -> second -> target of two reflections: added
        // to `pairs`, the first and the second triangle of each one after the other
        public static void Pairs(Node first, Node second, Point3f x, Point3f target, List<Triangle> pairs)
        {
            if (!VertexValid(new Interval3(x), first, second.position) || !VertexValid(first.position, second, new Interval3(target)))
            {
                return;
            }
            if (first.triangle != null && second.triangle != null)
            {
                if (!ReferenceEquals(first.triangle, second.triangle))
                {
                    pairs.Add(first.triangle);
                    pairs.Add(second.triangle);
                }
                return;
            }
            if (second.triangle != null || (first.triangle == null && first.size >= second.size))
            {
                Pairs(first.left, second, x, target, pairs);
                Pairs(first.right, second, x, target, pairs);
            }
            else
            {
                Pairs(first, second.left, x, target, pairs);
                Pairs(first, second.right, x, target, pairs);
            }
        }

        // The degree that the two polynomials are cut to
        const int Cutoff = 8;

        // The points of the first triangle where a chain x0 -> a -> b -> xL of two reflections can be (seeds: see the top)
        public static void TwoReflections(Point3f x0, Point3f xL, Triangle a, Triangle b, List<Point3f> points)
        {
            TwoReflectionSystem system = new TwoReflectionSystem(x0, xL, a, b);
            const int Samples = 48;
            const double Margin = 0.02;
            double previousV = 0;
            int previousSign = 0;
            for (int i = 0; i <= Samples; ++i)
            {
                double v = -Margin + (1 + 2 * Margin) * i / Samples;
                int sign = system.ResultantSign(v);
                if (i > 0 && sign != 0 && previousSign != 0 && sign != previousSign)
                {
                    double lo = previousV, hi = v;
                    for (int iteration = 0; iteration < 40; ++iteration)
                    {
                        double middle = 0.5 * (lo + hi);
                        int middleSign = system.ResultantSign(middle);
                        if (middleSign == previousSign)
                        {
                            lo = middle;
                        }
                        else
                        {
                            hi = middle;
                        }
                    }
                    system.SolveBack(0.5 * (lo + hi), Margin, points);
                }
                if (sign != 0)
                {
                    previousSign = sign;
                    previousV = v;
                }
            }
        }

        // SpecularPairs Private Methods
        // The unit vectors of the directions of a box of vectors
        static Interval3 Normalized(Interval3 v)
        {
            double longest = Math.Sqrt(v.x.LargestSquare() + v.y.LargestSquare() + v.z.LargestSquare());
            double shortest = Math.Sqrt(v.x.SmallestSquare() + v.y.SmallestSquare() + v.z.SmallestSquare());
            if (!(shortest > 0))
            {
                Interval all = new Interval(-1, 1);
                return new Interval3(all, all, all);
            }
            return new Interval3(NormalizedComponent(v.x, shortest, longest), NormalizedComponent(v.y, shortest, longest),
                                 NormalizedComponent(v.z, shortest, longest));
        }

        static Interval NormalizedComponent(Interval c, double shortest, double longest)
        {
            double lo = c.lo > 0 ? c.lo / longest : c.lo / shortest, hi = c.hi > 0 ? c.hi / shortest : c.hi / longest;
            return new Interval(Math.Max(-1.0, lo), Math.Min(1.0, hi));
        }

        // Whether a vertex of a node can reflect between a point of `pre` and a point of `next`: the two are not on opposite
        // sides of every normal, and the half vector can be along a normal (their cross product can be zero)
        static bool VertexValid(Interval3 pre, Node node, Interval3 next)
        {
            Interval3 toPre = pre - node.position, toNext = next - node.position;
            Interval3 n = node.normal;
            Interval dotPre = n.x * toPre.x + n.y * toPre.y + n.z * toPre.z, dotNext = n.x * toNext.x + n.y * toNext.y + n.z * toNext.z;
            if ((dotPre.hi <= 0 && dotNext.lo >= 0) || (dotPre.lo >= 0 && dotNext.hi <= 0))
            {
                return false;
            }
            Interval3 half = Normalized(toPre) + Normalized(toNext);
            return (half.y * n.z - half.z * n.y).CoversZero() && (half.z * n.x - half.x * n.z).CoversZero() &&
                   (half.x * n.y - half.y * n.x).CoversZero();
        }

        // The polynomials of a pair of triangles, as polynomials of u for a value of v (arrays of coefficients, the constant
        // first), in coordinates that start at the first corner of the first triangle and are scaled by its size
        sealed class TwoReflectionSystem
        {
            public TwoReflectionSystem(Point3f x0, Point3f xL, Triangle a, Triangle b)
            {
                this.a = a;
                this.b = b;
                this.x0World = x0;
                this.xLWorld = xL;
                // (lengths in units of the distance from x0 to the first triangle: the coefficients of the powers of u then fall
                // as the size of the triangles over the distances, which is what lets the polynomials be cut short)
                double scale = 1 / Math.Max(1e-20, (double)Distance(x0, a.p0));
                this.x0 = Scaled(x0 - a.p0, scale);
                this.xL = Scaled(xL - a.p0, scale);
                e11 = Scaled(a.p1 - a.p0, scale);
                e12 = Scaled(a.p2 - a.p0, scale);
                n10 = Scaled(a.n0, 1);
                g11 = Scaled(a.n1 - a.n0, 1);
                g12 = Scaled(a.n2 - a.n0, 1);
                p20 = Scaled(b.p0 - a.p0, scale);
                e21 = Scaled(b.p1 - b.p0, scale);
                e22 = Scaled(b.p2 - b.p0, scale);
                n20 = Scaled(b.n0, 1);
                g21 = Scaled(b.n1 - b.n0, 1);
                g22 = Scaled(b.n2 - b.n0, 1);
            }

            static double[] Scaled(Vector3f v, double scale)
            {
                return new double[] { v.x * scale, v.y * scale, v.z * scale };
            }

            // The polynomials A (degree 8) and B (degree 15) of u at v
            void Polynomials(double v, out double[] A, out double[] B)
            {
                // x1 and n1, linear in u
                double[][] x1 = Linear(e12, v, e11, null), n1 = Linear(g12, v, g11, n10);
                double[][] d0 = SubtractConstant(x1, x0);
                double[] nn = DotPolynomials(n1, n1), dn = DotPolynomials(d0, n1);
                double[][] d = Subtract(Multiply(nn, d0), Multiply(Scale(dn, 2), n1));
                // The ray x1 + t d against the second triangle
                double[][] P = CrossConstant(d, e22);
                double[] K = DotConstant(P, e21);
                double[][] T = SubtractConstant(x1, p20);
                double[] U = DotPolynomials(P, T);
                double[][] Q = CrossConstant(T, e21);
                double[] V = DotPolynomials(d, Q);
                // K n2 and K (xL - x2)
                double[][] N = Add(Add(MultiplyConstant(K, n20), MultiplyConstant(U, g21)), MultiplyConstant(V, g22));
                double[][] X = Add(Add(MultiplyConstant(K, p20), MultiplyConstant(U, e21)), MultiplyConstant(V, e22));
                double[][] D = Subtract(MultiplyConstant(K, xL), X);
                double[][] toLight = new double[][] { Negated(x1[0], xL[0]), Negated(x1[1], xL[1]), Negated(x1[2], xL[2]) };
                A = DotPolynomials(CrossPolynomials(d, toLight), N);
                double[][] t = CrossConstant(N, e21);
                B = AddPolynomials(MultiplyPolynomials(DotPolynomials(d, N), DotPolynomials(D, t)), MultiplyPolynomials(DotPolynomials(D, N), DotPolynomials(d, t)));
            }

            // The sign of the determinant of the Bezout matrix of A and B at v (zero if it cannot be told)
            public int ResultantSign(double v)
            {
                Polynomials(v, out double[] A, out double[] B);
                // (both cut to the degree 8, as the authors' code does with its "cutoff_matrix": the Bezout matrix is 8 by 8)
                const int n = Cutoff;
                double[] f = new double[n + 1], g = new double[n + 1];
                double largestF = 0, largestG = 0;
                for (int i = 0; i <= n; ++i)
                {
                    f[i] = i < A.Length ? A[i] : 0;
                    g[i] = i < B.Length ? B[i] : 0;
                    largestF = Math.Max(largestF, Math.Abs(f[i]));
                    largestG = Math.Max(largestG, Math.Abs(g[i]));
                }
                if (!(largestF > 0) || !(largestG > 0))
                {
                    return 0;
                }
                for (int i = 0; i <= n; ++i)
                {
                    f[i] /= largestF;
                    g[i] /= largestG;
                }
                // The Bezout matrix: the coefficients of (f(s) g(t) - f(t) g(s)) / (s - t)
                double[,] m = new double[n, n];
                for (int i = 0; i < n; ++i)
                {
                    for (int j = 0; j < n; ++j)
                    {
                        double sum = 0;
                        int last = Math.Min(i, n - 1 - j);
                        for (int k = 0; k <= last; ++k)
                        {
                            sum += f[j + k + 1] * g[i - k] - f[i - k] * g[j + k + 1];
                        }
                        m[i, j] = sum;
                    }
                }
                // The sign of the determinant, by elimination with the largest pivot of each column
                int sign = 1;
                for (int column = 0; column < n; ++column)
                {
                    int pivot = column;
                    for (int row = column + 1; row < n; ++row)
                    {
                        if (Math.Abs(m[row, column]) > Math.Abs(m[pivot, column]))
                        {
                            pivot = row;
                        }
                    }
                    double value = m[pivot, column];
                    if (value == 0 || double.IsNaN(value))
                    {
                        return 0;
                    }
                    if (pivot != column)
                    {
                        for (int j = column; j < n; ++j)
                        {
                            double swap = m[pivot, j];
                            m[pivot, j] = m[column, j];
                            m[column, j] = swap;
                        }
                        sign = -sign;
                    }
                    if (value < 0)
                    {
                        sign = -sign;
                    }
                    for (int row = column + 1; row < n; ++row)
                    {
                        double factor = m[row, column] / value;
                        if (factor != 0)
                        {
                            for (int j = column + 1; j < n; ++j)
                            {
                                m[row, j] -= factor * m[column, j];
                            }
                        }
                    }
                }
                return sign;
            }

            // The points of the first triangle at v where A vanishes and the chain reflects at both vertices
            public void SolveBack(double v, double margin, List<Point3f> points)
            {
                Polynomials(v, out double[] A, out double[] B);
                const int Samples = 24;
                double from = -margin, to = 1 - v + margin;
                if (!(to > from))
                {
                    return;
                }
                double previousU = from, previous = Evaluate(A, from);
                for (int i = 1; i <= Samples; ++i)
                {
                    double u = from + (to - from) * i / Samples, value = Evaluate(A, u);
                    if ((previous < 0) != (value < 0))
                    {
                        double lo = previousU, hi = u;
                        bool negative = previous < 0;
                        for (int iteration = 0; iteration < 40; ++iteration)
                        {
                            double middle = 0.5 * (lo + hi);
                            if ((Evaluate(A, middle) < 0) == negative)
                            {
                                lo = middle;
                            }
                            else
                            {
                                hi = middle;
                            }
                        }
                        Check(0.5 * (lo + hi), v, margin, points);
                    }
                    previousU = u;
                    previous = value;
                }
            }

            // Keeps the point (u, v) of the first triangle if the ray reflected there meets the second triangle and is
            // reflected there toward xL, nearly
            void Check(double u, double v, double margin, List<Point3f> points)
            {
                Point3f x1 = a.p0 + (Float)u * (a.p1 - a.p0) + (Float)v * (a.p2 - a.p0);
                Vector3f n1 = a.n0 + (Float)u * (a.n1 - a.n0) + (Float)v * (a.n2 - a.n0);
                if (LengthSquared(n1) == 0 || DistanceSquared(x1, x0World) == 0)
                {
                    return;
                }
                n1 = Normalize(n1);
                Vector3f arriving = Normalize(x1 - x0World);
                Vector3f leaving = arriving - 2 * Dot(arriving, n1) * n1;
                // (Moeller and Trumbore)
                Vector3f e1 = b.p1 - b.p0, e2 = b.p2 - b.p0;
                Vector3f p = Cross(leaving, e2);
                Float determinant = Dot(p, e1);
                if (determinant == 0)
                {
                    return;
                }
                Vector3f toOrigin = x1 - b.p0;
                Float u2 = Dot(p, toOrigin) / determinant;
                Vector3f q = Cross(toOrigin, e1);
                Float v2 = Dot(leaving, q) / determinant, distance = Dot(e2, q) / determinant;
                if (!(distance > 0) || u2 < -margin || v2 < -margin || u2 + v2 > 1 + margin)
                {
                    return;
                }
                Point3f x2 = b.p0 + u2 * e1 + v2 * e2;
                Vector3f n2 = b.n0 + u2 * (b.n1 - b.n0) + v2 * (b.n2 - b.n0);
                if (LengthSquared(n2) == 0 || DistanceSquared(x2, xLWorld) == 0)
                {
                    return;
                }
                Vector3f half = Normalize(xLWorld - x2) - leaving;
                if (LengthSquared(half) == 0)
                {
                    return;
                }
                if (Length(Cross(Normalize(half), Normalize(n2))) < 0.05f)
                {
                    points.Add(x1);
                }
            }

            // Polynomials of u: arrays of coefficients; vectors of them: three arrays
            static double Evaluate(double[] p, double u)
            {
                double value = 0;
                for (int i = p.Length - 1; i >= 0; --i)
                {
                    value = value * u + p[i];
                }
                return value;
            }

            // constant[c] + v * atV[c] + u * atU[c] (constant may be null)
            static double[][] Linear(double[] atV, double v, double[] atU, double[] constant)
            {
                double[][] result = new double[3][];
                for (int c = 0; c < 3; ++c)
                {
                    result[c] = new double[] { (constant != null ? constant[c] : 0) + v * atV[c], atU[c] };
                }
                return result;
            }

            // constant - p
            static double[] Negated(double[] p, double constant)
            {
                double[] result = new double[p.Length];
                for (int i = 0; i < p.Length; ++i)
                {
                    result[i] = -p[i];
                }
                result[0] += constant;
                return result;
            }

            static double[] Scale(double[] p, double s)
            {
                double[] result = new double[p.Length];
                for (int i = 0; i < p.Length; ++i)
                {
                    result[i] = s * p[i];
                }
                return result;
            }

            static double[] AddPolynomials(double[] p, double[] q)
            {
                double[] result = new double[Math.Max(p.Length, q.Length)];
                for (int i = 0; i < p.Length; ++i)
                {
                    result[i] += p[i];
                }
                for (int i = 0; i < q.Length; ++i)
                {
                    result[i] += q[i];
                }
                return result;
            }

            static double[] MultiplyPolynomials(double[] p, double[] q)
            {
                double[] result = new double[p.Length + q.Length - 1];
                for (int i = 0; i < p.Length; ++i)
                {
                    for (int j = 0; j < q.Length; ++j)
                    {
                        result[i + j] += p[i] * q[j];
                    }
                }
                return result;
            }

            static double[][] SubtractConstant(double[][] p, double[] constant)
            {
                double[][] result = new double[3][];
                for (int c = 0; c < 3; ++c)
                {
                    result[c] = (double[])p[c].Clone();
                    result[c][0] -= constant[c];
                }
                return result;
            }

            static double[][] Add(double[][] p, double[][] q)
            {
                return new double[][] { AddPolynomials(p[0], q[0]), AddPolynomials(p[1], q[1]), AddPolynomials(p[2], q[2]) };
            }

            static double[][] Subtract(double[][] p, double[][] q)
            {
                return new double[][] { AddPolynomials(p[0], Scale(q[0], -1)), AddPolynomials(p[1], Scale(q[1], -1)),
                                        AddPolynomials(p[2], Scale(q[2], -1)) };
            }

            // A polynomial times a vector of polynomials, and times a constant vector
            static double[][] Multiply(double[] p, double[][] q)
            {
                return new double[][] { MultiplyPolynomials(p, q[0]), MultiplyPolynomials(p, q[1]), MultiplyPolynomials(p, q[2]) };
            }

            static double[][] MultiplyConstant(double[] p, double[] constant)
            {
                return new double[][] { Scale(p, constant[0]), Scale(p, constant[1]), Scale(p, constant[2]) };
            }

            static double[] DotPolynomials(double[][] p, double[][] q)
            {
                return AddPolynomials(AddPolynomials(MultiplyPolynomials(p[0], q[0]), MultiplyPolynomials(p[1], q[1])),
                                      MultiplyPolynomials(p[2], q[2]));
            }

            static double[] DotConstant(double[][] p, double[] constant)
            {
                return AddPolynomials(AddPolynomials(Scale(p[0], constant[0]), Scale(p[1], constant[1])), Scale(p[2], constant[2]));
            }

            static double[][] CrossPolynomials(double[][] p, double[][] q)
            {
                return new double[][] { AddPolynomials(MultiplyPolynomials(p[1], q[2]), Scale(MultiplyPolynomials(p[2], q[1]), -1)),
                                        AddPolynomials(MultiplyPolynomials(p[2], q[0]), Scale(MultiplyPolynomials(p[0], q[2]), -1)),
                                        AddPolynomials(MultiplyPolynomials(p[0], q[1]), Scale(MultiplyPolynomials(p[1], q[0]), -1)) };
            }

            static double[][] CrossConstant(double[][] p, double[] constant)
            {
                return new double[][] { AddPolynomials(Scale(p[1], constant[2]), Scale(p[2], -constant[1])),
                                        AddPolynomials(Scale(p[2], constant[0]), Scale(p[0], -constant[2])),
                                        AddPolynomials(Scale(p[0], constant[1]), Scale(p[1], -constant[0])) };
            }

            Triangle a, b;
            Point3f x0World, xLWorld;
            double[] x0, xL, e11, e12, n10, g11, g12, p20, e21, e22, n20, g21, g22;
        }
    }

}  // namespace pbrt
