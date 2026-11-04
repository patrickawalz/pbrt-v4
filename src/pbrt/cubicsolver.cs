// (not in pbrt-v4) cubicsolver.cs
//
// The non-iterative cubic solver of Appendix A of Reshetov, "Ray/Ribbon Intersections" (2022): the cubic is solved either directly or for the reciprocal
// of its roots (the branch with the smaller Cauchy bound, after a shift that moves the solution away from zero), the single most distinct root is found with
// the trigonometric solver, the cubic is deflated by it and the remaining quadratic is solved with a stable solver. A Newton step tunes every root
// when it reduces the value of the polynomial.

using System;

namespace pbrt
{
    public static unsafe class RobustCubic
    {
        // The real roots of a u^3 + b u^2 + c u + d = 0 in ascending order; roots must have room for three values
        public static int Solve(double a, double b, double c, double d, double* roots)
        {
            double scale = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d)));
            if (!(scale > 0) || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                return 0;
            }
            double inverseScale = 1 / scale;
            double na = a * inverseScale, nb = b * inverseScale, nc = c * inverseScale, nd = d * inverseScale;
            int count;
            if (Math.Abs(na) < Epsilon && Math.Abs(nb) < Epsilon)
            {
                count = 0;
                if (Math.Abs(nc) > 0)
                {
                    roots[count++] = Tune(na, nb, nc, nd, -nd / nc);
                }
            }
            else if (Math.Abs(na) < Epsilon)
            {
                count = SolveQuadratic(nb, nc, nd, roots);
                for (int i = 0; i < count; ++i)
                {
                    roots[i] = Tune(na, nb, nc, nd, roots[i]);
                }
            }
            else
            {
                count = SolveCubic(na, nb, nc, nd, roots);
            }
            Sort(roots, count);
            return count;
        }

        private const double Epsilon = 1e-7;

        private static int SolveCubic(double a, double b, double c, double d, double* roots)
        {
            // Move the solution away from zero: u = w + 0.5 when that increases the amplitude of the constant term
            double shift = 0;
            double sa = a, sb = b, sc = c, sd = d;
            double shiftedB = b + 1.5 * a, shiftedC = c + b + 0.75 * a, shiftedD = d + 0.5 * c + 0.25 * b + 0.125 * a;
            if (Math.Abs(shiftedD) > Math.Abs(d))
            {
                shift = 0.5;
                sb = shiftedB;
                sc = shiftedC;
                sd = shiftedD;
            }
            // The direct and the inverse equation, by the bounds of Cauchy on the roots (in the metric L1)
            double directCauchy = Math.Max(Math.Abs(sd), Math.Max(Math.Abs(sc), Math.Abs(sb))) * Math.Abs(sd);
            double inverseCauchy = Math.Max(Math.Abs(sa), Math.Max(Math.Abs(sc), Math.Abs(sb))) * Math.Abs(sa);
            bool inverse = directCauchy > inverseCauchy;
            double* local = stackalloc double[3];
            int count = 0;
            if (Math.Abs(sd) < 1e-300 && !inverse)
            {
                // zero is a root
                local[count++] = 0;
                int quadratic = SolveQuadratic(sa, sb, sc, local + 1);
                count += quadratic;
            }
            else
            {
                // The polynomial that is solved: the original, or the one of the reciprocals of the roots
                double qa = inverse ? sd : sa, qb = inverse ? sc : sb, qc = inverse ? sb : sc, qd = inverse ? sa : sd;
                double x0 = MostDistinctRoot(qa, qb, qc, qd);
                if (double.IsNaN(x0))
                {
                    return 0;
                }
                x0 = TuneNewton(qa, qb, qc, qd, x0);
                local[count++] = x0;
                // Deflate: q(x) = (x - x0) (a2 x^2 + b2 x + c2)
                double a2 = qa, b2 = qb + qa * x0, c2 = qc + b2 * x0;
                count += SolveQuadratic(a2, b2, c2, local + 1);
                if (inverse)
                {
                    int kept = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        if (local[i] != 0)
                        {
                            local[kept++] = 1 / local[i];
                        }
                    }
                    count = kept;
                }
            }
            int result = 0;
            for (int i = 0; i < count; ++i)
            {
                double root = local[i] + shift;
                roots[result++] = Tune(a, b, c, d, root);
            }
            return result;
        }

        // The real roots of a x^2 + b x + c = 0 (a stable solver, after Blinn)
        private static int SolveQuadratic(double a, double b, double c, double* roots)
        {
            if (a == 0)
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
                // A tangent hit that the rounding turned into a miss
                if (discriminant > -1e-14 * Math.Max(b * b, Math.Abs(4 * a * c)))
                {
                    roots[0] = -b / (2 * a);
                    return 1;
                }
                return 0;
            }
            double root = Math.Sqrt(discriminant);
            double q = -0.5 * (b + (b >= 0 ? root : -root));
            double first = q / a;
            double second = q != 0 ? c / q : first;
            roots[0] = first;
            roots[1] = second;
            return 2;
        }

        // The root of the monic cubic that is the farthest from the other two (the most distinct one), by the trigonometric solver
        private static double MostDistinctRoot(double a, double b, double c, double d)
        {
            double p = b / a, q = c / a, r = d / a;
            // x = t - p / 3: t^3 + P t + Q = 0
            double bigP = q - p * p / 3;
            double bigQ = 2 * p * p * p / 27 - p * q / 3 + r;
            double shift = -p / 3;
            double discriminant = bigQ * bigQ / 4 + bigP * bigP * bigP / 27;
            if (discriminant >= 0 || bigP >= 0)
            {
                // One real root (or a multiple one): Cardano
                double root = Math.Sqrt(Math.Max(discriminant, 0));
                double u = Math.Cbrt(-bigQ / 2 + root), v = Math.Cbrt(-bigQ / 2 - root);
                return u + v + shift;
            }
            double m = 2 * Math.Sqrt(-bigP / 3);
            double argument = 3 * bigQ / (bigP * m);
            argument = Math.Max(-1, Math.Min(1, argument));
            double theta = Math.Acos(argument) / 3;
            double t0 = m * Math.Cos(theta) + shift;
            double t1 = m * Math.Cos(theta - 2 * Math.PI / 3) + shift;
            double t2 = m * Math.Cos(theta - 4 * Math.PI / 3) + shift;
            double distance0 = Math.Min(Math.Abs(t0 - t1), Math.Abs(t0 - t2));
            double distance1 = Math.Min(Math.Abs(t1 - t0), Math.Abs(t1 - t2));
            double distance2 = Math.Min(Math.Abs(t2 - t0), Math.Abs(t2 - t1));
            if (distance0 >= distance1 && distance0 >= distance2)
            {
                return t0;
            }
            return distance1 >= distance2 ? t1 : t2;
        }

        // One Newton step on the polynomial, kept only when it reduces its value
        private static double TuneNewton(double a, double b, double c, double d, double x)
        {
            double value = ((a * x + b) * x + c) * x + d;
            double slope = (3 * a * x + 2 * b) * x + c;
            if (slope == 0)
            {
                return x;
            }
            double next = x - value / slope;
            double nextValue = ((a * next + b) * next + c) * next + d;
            return Math.Abs(nextValue) < Math.Abs(value) ? next : x;
        }

        private static double Tune(double a, double b, double c, double d, double x)
        {
            return TuneNewton(a, b, c, d, x);
        }

        private static void Sort(double* values, int count)
        {
            for (int i = 1; i < count; ++i)
            {
                double value = values[i];
                int j = i;
                while (j > 0 && values[j - 1] > value)
                {
                    values[j] = values[j - 1];
                    --j;
                }
                values[j] = value;
            }
        }
    }
}
