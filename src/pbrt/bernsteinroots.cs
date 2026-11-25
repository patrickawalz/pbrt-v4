// (not in pbrt-v4) bernsteinroots.cs
//
// The roots of a polynomial in a part of [0, 1], for polynomials of a high degree with few roots there (the resultants of the
// "sweep" shape, of degree 12 and 18): in Bernstein's form the polynomial has no more roots in a part than its coefficients
// for that part change sign, so a part without a change is left at once, a part with one change holds one root (found by
// Newton's steps kept inside the part), and a part with more is cut in two (de Casteljau).

using System;

namespace pbrt
{
    // BernsteinRoots Definition
    public static unsafe class BernsteinRoots
    {
        // BernsteinRoots Public Methods

        // The roots in [x0, x1] (within [0, 1]) of the polynomial of the coefficients coef[0..degree] (of rising powers), in
        // rising order, each to within xError (roots of even multiplicity are not found). roots: room for `degree` values.
        // The number of the roots.
        public static int Roots(double* coef, int degree, double x0, double x1, double xError, double* roots)
        {
            // Bernstein's coefficients over [0, 1], then over [x0, x1]
            double* bernstein = stackalloc double[degree + 1];
            double binomial = 1;
            for (int k = 0; k <= degree; ++k)
            {
                bernstein[k] = coef[k] / binomial;
                binomial = binomial * (degree - k) / (k + 1);
            }
            for (int j = 1; j <= degree; ++j)
            {
                for (int i = degree; i >= j; --i)
                {
                    bernstein[i] += bernstein[i - 1];
                }
            }
            if (x1 < 1)
            {
                for (int j = 1; j <= degree; ++j)
                {
                    for (int i = degree; i >= j; --i)
                    {
                        bernstein[i] = bernstein[i - 1] + x1 * (bernstein[i] - bernstein[i - 1]);
                    }
                }
            }
            if (x0 > 0)
            {
                double t = x0 / x1;
                for (int j = 1; j <= degree; ++j)
                {
                    for (int i = 0; i <= degree - j; ++i)
                    {
                        bernstein[i] = bernstein[i] + t * (bernstein[i + 1] - bernstein[i]);
                    }
                }
            }
            int count = 0;
            Isolate(coef, degree, bernstein, x0, x1, xError, roots, ref count);
            return count;
        }

        // BernsteinRoots Private Methods
        private static void Isolate(double* coef, int degree, double* bernstein, double xa, double xb, double xError, double* roots, ref int count)
        {
            int changes = 0;
            bool negative = false, signed = false;
            for (int i = 0; i <= degree; ++i)
            {
                if (bernstein[i] == 0)
                {
                    continue;
                }
                bool now = bernstein[i] < 0;
                changes += signed && now != negative ? 1 : 0;
                negative = now;
                signed = true;
            }
            if (changes == 0 || count == degree)
            {
                return;
            }
            if (changes == 1)
            {
                roots[count++] = Refine(coef, degree, xa, xb, bernstein[0] < 0 || (bernstein[0] == 0 && bernstein[degree] > 0), xError);
                return;
            }
            if (xb - xa <= xError)
            {
                // (roots nearer to each other than they are looked for: one where the polynomial changes sign over the part)
                if ((bernstein[0] < 0) != (bernstein[degree] < 0))
                {
                    roots[count++] = 0.5 * (xa + xb);
                }
                return;
            }
            double* left = stackalloc double[degree + 1];
            double* right = stackalloc double[degree + 1];
            for (int i = 0; i <= degree; ++i)
            {
                right[i] = bernstein[i];
            }
            for (int j = 0; j <= degree; ++j)
            {
                left[j] = right[0];
                for (int i = 0; i < degree - j; ++i)
                {
                    right[i] = 0.5 * (right[i] + right[i + 1]);
                }
            }
            double middle = 0.5 * (xa + xb);
            Isolate(coef, degree, left, xa, middle, xError, roots, ref count);
            if (right[0] == 0 && count < degree)
            {
                roots[count++] = middle;
            }
            Isolate(coef, degree, right, middle, xb, xError, roots, ref count);
        }

        // The one root in [xa, xb]; negativeAtStart: the polynomial is below zero at xa
        private static double Refine(double* coef, int degree, double xa, double xb, bool negativeAtStart, double xError)
        {
            double x = 0.5 * (xa + xb);
            for (int step = 0; step < 64; ++step)
            {
                double value = coef[degree], derivative = 0;
                for (int i = degree - 1; i >= 0; --i)
                {
                    derivative = Math.FusedMultiplyAdd(derivative, x, value);
                    value = Math.FusedMultiplyAdd(value, x, coef[i]);
                }
                if (value == 0)
                {
                    return x;
                }
                if ((value < 0) == negativeAtStart)
                {
                    xa = x;
                }
                else
                {
                    xb = x;
                }
                double next = derivative != 0 ? x - value / derivative : 0.5 * (xa + xb);
                if (!(next > xa && next < xb))
                {
                    next = 0.5 * (xa + xb);
                }
                if (Math.Abs(next - x) <= xError)
                {
                    return next;
                }
                x = next;
            }
            return x;
        }
    }
}
