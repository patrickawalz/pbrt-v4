// (not in pbrt-v4) polynomialroots.cs
//
// The real roots of a polynomial in an interval, after
//
//   Cem Yuksel. 2022. High-Performance Polynomial Root Finding for Graphics. Proc. ACM Comput. Graph. Interact. Tech. 5, 3,
//   Article 7.
//
// and its implementation cyPolynomial.h of cyCodeBase, which is under this notice:
//
//   Copyright (c) 2022, Cem Yuksel <cem@cemyuksel.com>
//   All rights reserved.
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
//   files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
//   modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
//   Software is furnished to do so, subject to the following conditions:
//
//   The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
//   Software.
//
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
//   WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
//   COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
//   ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// The polynomial is cut at the roots of its derivative (found the same way, down to the quadratic) into parts in which it
// rises or falls only; a part whose ends have different signs holds one root, found by Newton's steps kept inside the part,
// with a step to the middle whenever one leaves it.
//
// The arithmetic is that of pbrt-v4's own solvers: the values by Horner's rule with fused multiply-adds, and the quadratic
// with the discriminant as a difference of products. (The values with the rounding errors of Horner's rule added back, as
// CompensatedFloat does for inner products, were tried for the polynomial of degree 10 of the swept disks: the roots were
// no nearer, at 1.4 times the time; what loses digits there is where the ray starts, which the shapes see to.)

using System;

namespace pbrt
{
    // PolynomialRoots Definition
    public static unsafe class PolynomialRoots
    {
        // PolynomialRoots Public Methods

        // The value of the polynomial of the coefficients coef[0..degree] (of rising powers) at x
        public static double Evaluate(double* coef, int degree, double x)
        {
            double value = coef[degree];
            for (int i = degree - 1; i >= 0; --i)
            {
                value = Math.FusedMultiplyAdd(value, x, coef[i]);
            }
            return value;
        }

        // The roots in [x0, x1] of the polynomial of the coefficients coef[0..degree], in rising order, each to within xError
        // (the roots of even multiplicity, where the polynomial touches zero without crossing it, are not found). roots:
        // room for `degree` values. The number of the roots.
        public static int Roots(double* coef, int degree, double x0, double x1, double xError, double* roots)
        {
            while (degree > 0 && coef[degree] == 0)
            {
                --degree;
            }
            if (degree == 0)
            {
                return 0;
            }
            if (degree == 1)
            {
                double root = -coef[0] / coef[1];
                roots[0] = root;
                return root >= x0 && root <= x1 ? 1 : 0;
            }
            if (degree == 2)
            {
                return Quadratic(coef, x0, x1, roots);
            }
            // The parts between the roots of the derivative
            double* derivative = stackalloc double[degree];
            for (int i = 1; i <= degree; ++i)
            {
                derivative[i - 1] = i * coef[i];
            }
            double* turns = stackalloc double[degree - 1];
            int turnCount = Roots(derivative, degree - 1, x0, x1, xError, turns);
            int count = 0;
            double xa = x0, ya = Evaluate(coef, degree, x0);
            for (int i = 0; i <= turnCount; ++i)
            {
                double xb = i < turnCount ? turns[i] : x1;
                double yb = Evaluate(coef, degree, xb);
                if ((ya < 0) != (yb < 0))
                {
                    roots[count++] = FindClosed(coef, derivative, degree, xa, xb, ya, xError);
                }
                xa = xb;
                ya = yb;
            }
            return count;
        }

        // PolynomialRoots Private Methods

        // The roots of coef[0] + coef[1] x + coef[2] x^2 in [x0, x1]
        private static int Quadratic(double* coef, double x0, double x1, double* roots)
        {
            double c = coef[0], b = coef[1], a = coef[2];
            double discriminant = pbrt.DifferenceOfProducts(b, b, 4 * a, c);
            if (discriminant < 0)
            {
                return 0;
            }
            if (discriminant == 0)
            {
                double root = -0.5 * b / a;
                roots[0] = root;
                return root >= x0 && root <= x1 ? 1 : 0;
            }
            double q = -0.5 * (b + Math.CopySign(Math.Sqrt(discriminant), b));
            double first = q / a, second = c / q;
            double low = Math.Min(first, second), high = Math.Max(first, second);
            int count = 0;
            if (low >= x0 && low <= x1)
            {
                roots[count++] = low;
            }
            if (high >= x0 && high <= x1)
            {
                roots[count++] = high;
            }
            return count;
        }

        // The one root in [x0, x1], where the polynomial rises or falls only, and y0 is its value at x0: Newton's steps from
        // the middle, the part cut at every value, and a step to the middle of what is left whenever Newton's leaves it
        private static double FindClosed(double* coef, double* derivative, int degree, double x0, double x1, double y0, double xError)
        {
            double root = 0.5 * (x0 + x1);
            if (x1 - x0 <= 2 * xError)
            {
                return root;
            }
            double value = Evaluate(coef, degree, root);
            double low = x0, high = x1;
            while (true)
            {
                if ((y0 < 0) != (value < 0))
                {
                    high = root;
                }
                else
                {
                    low = root;
                }
                double next = root - value / Evaluate(derivative, degree - 1, root);
                if (next > low && next < high)
                {
                    double step = Math.Abs(root - next);
                    root = next;
                    if (!(step > xError))
                    {
                        break;
                    }
                }
                else
                {
                    root = 0.5 * (low + high);
                    if (root == low || root == high || high - low <= 2 * xError)
                    {
                        break;
                    }
                }
                value = Evaluate(coef, degree, root);
            }
            return root;
        }
    }
}
