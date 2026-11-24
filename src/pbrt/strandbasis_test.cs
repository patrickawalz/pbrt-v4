// (not in pbrt-v4) strandbasis_test.cs
//
// Tests of the bases of the strands (strandbasis.cs): each against the direct evaluation of the basis.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class StrandBasisTests
    {
        private static double[] Values(int count, int seed)
        {
            Random random = new Random(seed);
            double[] values = new double[count];
            for (int i = 0; i < count; ++i)
            {
                values[i] = 2 * random.NextDouble() - 1 + 0.3 * i;
            }
            return values;
        }

        // The Bezier curve `curve` of the degree `degree` at t
        private static double Bezier(double[] values, int degree, int curve, double t)
        {
            double s = 1 - t;
            int at = (degree + 1) * curve;
            if (degree == 2)
            {
                return values[at] * (s * s) + values[at + 1] * (2 * t * s) + values[at + 2] * (t * t);
            }
            return values[at] * (s * s * s) + values[at + 1] * (3 * t * s * s) + values[at + 2] * (3 * t * t * s) + values[at + 3] * (t * t * t);
        }

        // The derivative of the Bezier curve at its start (end false) or its end (end true)
        private static double EndDerivative(double[] values, int degree, int curve, bool end)
        {
            int at = (degree + 1) * curve;
            return end ? degree * (values[at + degree] - values[at + degree - 1]) : degree * (values[at + 1] - values[at]);
        }

        // The straight pieces, as quadratic and as cubic curves
        [TEST("StrandBasis", "LinearIsStraightPieces")]
        public static void LinearIsStraightPieces()
        {
            double[] g = Values(6, 1);
            double worst = 0;
            for (int closed = 0; closed < 2; ++closed)
            {
                StrandBasis basis = new StrandBasis("linear", 1, 1, closed == 1 ? "periodic" : "nonperiodic", 0.5, null);
                CHECK(basis.Check(g.Length) == null);
                for (int degree = 2; degree <= 3; ++degree)
                {
                    double[] curves = basis.Curves(g, degree);
                    CHECK_EQ(curves.Length / (degree + 1), closed == 1 ? 6 : 5);
                    for (int c = 0; c < curves.Length / (degree + 1); ++c)
                    {
                        for (int j = 0; j <= 8; ++j)
                        {
                            double t = j / 8.0;
                            worst = Math.Max(worst, Math.Abs(Bezier(curves, degree, c, t) - ((1 - t) * g[c] + t * g[(c + 1) % 6])));
                        }
                    }
                }
            }
            Console.WriteLine("linear: differs from the straight pieces by " + worst);
            CHECK_LT(worst, 1e-14);
        }

        // Cardinal: through the points, with the tangent the tension times the difference of the neighbors; 0.5 is catmull-rom
        [TEST("StrandBasis", "CardinalHasTheTension")]
        public static void CardinalHasTheTension()
        {
            double[] g = Values(8, 2);
            double worstPoint = 0, worstTangent = 0, worstCatmull = 0;
            foreach (double tension in new double[] { 0, 0.25, 0.5, 0.9 })
            {
                StrandBasis basis = new StrandBasis("cardinal", 3, 1, "nonperiodic", tension, null);
                double[] curves = basis.Curves(g, 3);
                CHECK_EQ(curves.Length, 4 * 5);
                for (int c = 0; c < 5; ++c)
                {
                    worstPoint = Math.Max(worstPoint, Math.Max(Math.Abs(curves[4 * c] - g[c + 1]), Math.Abs(curves[4 * c + 3] - g[c + 2])));
                    worstTangent = Math.Max(worstTangent, Math.Abs(EndDerivative(curves, 3, c, false) - tension * (g[c + 2] - g[c])));
                    worstTangent = Math.Max(worstTangent, Math.Abs(EndDerivative(curves, 3, c, true) - tension * (g[c + 3] - g[c + 1])));
                }
                if (tension == 0.5)
                {
                    double[] catmull = new StrandBasis("catmull-rom", 3, 1, "nonperiodic", 0.5, null).Curves(g, 3);
                    for (int i = 0; i < curves.Length; ++i)
                    {
                        worstCatmull = Math.Max(worstCatmull, Math.Abs(curves[i] - catmull[i]));
                    }
                }
            }
            Console.WriteLine("cardinal: misses the points by " + worstPoint + ", the tangents by " + worstTangent + "; tension 0.5 differs from catmull-rom by " + worstCatmull);
            CHECK_LT(worstPoint, 1e-14);
            CHECK_LT(worstTangent, 1e-13);
            CHECK_LT(worstCatmull, 1e-14);
        }

        // Pinned: n - 1 curves from the first point to the last, leaving along the first two points; the curves between are
        // those of the strand that is not pinned
        [TEST("StrandBasis", "PinnedReachesTheEnds")]
        public static void PinnedReachesTheEnds()
        {
            double[] g = Values(7, 3);
            foreach (string name in new string[] { "b-spline", "catmull-rom", "cardinal" })
            {
                double[] pinned = new StrandBasis(name, 3, 1, "pinned", 0.3, null).Curves(g, 3);
                double[] open = new StrandBasis(name, 3, 1, "nonperiodic", 0.3, null).Curves(g, 3);
                CHECK_EQ(pinned.Length, 4 * 6);
                CHECK_EQ(open.Length, 4 * 4);
                double ends = Math.Max(Math.Abs(pinned[0] - g[0]), Math.Abs(pinned[pinned.Length - 1] - g[6]));
                double startSlope = EndDerivative(pinned, 3, 0, false) / (g[1] - g[0]), endSlope = EndDerivative(pinned, 3, 5, true) / (g[6] - g[5]);
                double inner = 0;
                for (int i = 0; i < open.Length; ++i)
                {
                    inner = Math.Max(inner, Math.Abs(open[i] - pinned[4 + i]));
                }
                Console.WriteLine("pinned " + name + ": misses the ends by " + ends + ", leaves along the first two points with " + startSlope + " and " + endSlope +
                                  " of their difference, inner curves differ by " + inner);
                CHECK_LT(ends, 1e-14);
                CHECK_LT(inner, 1e-14);
                CHECK_GT(startSlope, 0);
                CHECK_GT(endSlope, 0);
            }
        }

        // Periodic: the strand is closed, with the smoothness of the basis at the seam too
        [TEST("StrandBasis", "PeriodicIsClosed")]
        public static void PeriodicIsClosed()
        {
            string[] names = { "linear", "bezier", "b-spline", "bezier", "b-spline", "catmull-rom", "cardinal" };
            int[] degrees = { 1, 2, 2, 3, 3, 3, 3 };
            for (int which = 0; which < names.Length; ++which)
            {
                int count = names[which] == "bezier" ? 6 : 7;
                int step = names[which] == "bezier" ? 3 : 1;
                double[] g = Values(count, 4);
                StrandBasis basis = new StrandBasis(names[which], degrees[which], step, "periodic", 0.4, null);
                CHECK(basis.Check(count) == null);
                int degree = Math.Max(2, degrees[which]);
                double[] curves = basis.Curves(g, degree);
                int number = curves.Length / (degree + 1);
                int expected = names[which] == "bezier" ? (degrees[which] == 2 ? 3 : 2) : 7;
                CHECK_EQ(number, expected);
                double gap = 0, slope = 0;
                for (int c = 0; c < number; ++c)
                {
                    int next = (c + 1) % number;
                    gap = Math.Max(gap, Math.Abs(Bezier(curves, degree, c, 1) - Bezier(curves, degree, next, 0)));
                    slope = Math.Max(slope, Math.Abs(EndDerivative(curves, degree, c, true) - EndDerivative(curves, degree, next, false)));
                }
                Console.WriteLine("periodic " + names[which] + " of the degree " + degrees[which] + ": " + number + " curves, gaps " + gap + ", tangents differ by " + slope);
                CHECK_LT(gap, 1e-14);
                if (names[which] != "linear" && names[which] != "bezier")
                {
                    CHECK_LT(slope, 1e-13);
                }
            }
        }

        // The quadratic bases as cubic curves (for "rocap") are the same curves, and the quadratic B-spline is that of "ribbon"
        [TEST("StrandBasis", "QuadraticBasesAsCubics")]
        public static void QuadraticBasesAsCubics()
        {
            double worst = 0, worstRibbon = 0;
            foreach (string name in new string[] { "bezier", "b-spline" })
            {
                double[] g = Values(9, 5);
                StrandBasis basis = new StrandBasis(name, 2, 1, "nonperiodic", 0.5, null);
                CHECK(basis.Check(9) == null);
                double[] quadratic = basis.Curves(g, 2), cubic = basis.Curves(g, 3);
                CHECK_EQ(quadratic.Length / 3, name == "bezier" ? 4 : 7);
                CHECK_EQ(cubic.Length / 4, quadratic.Length / 3);
                for (int c = 0; c < quadratic.Length / 3; ++c)
                {
                    for (int j = 0; j <= 8; ++j)
                    {
                        worst = Math.Max(worst, Math.Abs(Bezier(quadratic, 2, c, j / 8.0) - Bezier(cubic, 3, c, j / 8.0)));
                    }
                }
                if (name == "b-spline")
                {
                    SdfVector[] points = new SdfVector[9];
                    double[] widths = new double[9];
                    for (int i = 0; i < 9; ++i)
                    {
                        points[i] = new SdfVector(g[i], 0, 0);
                    }
                    Ribbons2.Run run = Ribbons2.FromSpline(points, widths, null);
                    for (int c = 0; c < 7; ++c)
                    {
                        for (int k = 0; k < 3; ++k)
                        {
                            worstRibbon = Math.Max(worstRibbon, Math.Abs(run.points[2 * c + k].x - quadratic[3 * c + k]));
                        }
                    }
                }
            }
            Console.WriteLine("quadratic bases: the cubic form differs by " + worst + ", the b-spline from that of ribbon by " + worstRibbon);
            CHECK_LT(worst, 1e-14);
            CHECK_LT(worstRibbon, 1e-14);
        }

        // The basis function i of the degree d of the knots at u (Cox and de Boor)
        private static double BasisFunction(double[] knots, int i, int d, double u)
        {
            if (d == 0)
            {
                return u >= knots[i] && u < knots[i + 1] ? 1 : 0;
            }
            double left = knots[i + d] > knots[i] ? (u - knots[i]) / (knots[i + d] - knots[i]) * BasisFunction(knots, i, d - 1, u) : 0;
            double right = knots[i + d + 1] > knots[i + 1] ? (knots[i + d + 1] - u) / (knots[i + d + 1] - knots[i + 1]) * BasisFunction(knots, i + 1, d - 1, u) : 0;
            return left + right;
        }

        // The B-spline with knots: the Bezier curves of the spans against the sum of the basis functions, for knots that are
        // not uniform, with repeated knots inside and at the ends; uniform knots give the uniform cubic B-spline
        [TEST("StrandBasis", "KnotsGiveTheBSpline")]
        public static void KnotsGiveTheBSpline()
        {
            for (int degree = 2; degree <= 3; ++degree)
            {
                double[] g = Values(8, 6 + degree);
                double[] knots = degree == 2 ? new double[] { 0, 0, 0, 0.7, 1.1, 1.1, 2.5, 3, 4, 4, 4 } : new double[] { 0, 0, 0, 0, 0.7, 1.1, 1.1, 2.5, 4, 4, 4, 4 };
                StrandBasis basis = new StrandBasis("b-spline", degree, 1, "nonperiodic", 0.5, knots);
                CHECK(basis.Check(g.Length) == null);
                double[] curves = basis.Curves(g, degree);
                List<int> spans = new List<int>();
                for (int span = degree; span < g.Length; ++span)
                {
                    if (knots[span + 1] > knots[span])
                    {
                        spans.Add(span);
                    }
                }
                CHECK_EQ(curves.Length / (degree + 1), spans.Count);
                double worst = 0;
                for (int c = 0; c < spans.Count; ++c)
                {
                    for (int j = 0; j < 16; ++j)
                    {
                        double t = (j + 0.5) / 16, u = knots[spans[c]] + t * (knots[spans[c] + 1] - knots[spans[c]]);
                        double expected = 0;
                        for (int i = 0; i < g.Length; ++i)
                        {
                            expected += g[i] * BasisFunction(knots, i, degree, u);
                        }
                        worst = Math.Max(worst, Math.Abs(Bezier(curves, degree, c, t) - expected));
                    }
                }
                double ends = Math.Max(Math.Abs(curves[0] - g[0]), Math.Abs(curves[curves.Length - 1] - g[7]));
                Console.WriteLine("b-spline with knots, degree " + degree + ": " + spans.Count + " spans, differs from the basis functions by " + worst +
                                  "; the ends miss the first and last point by " + ends);
                CHECK_LT(worst, 1e-13);
                CHECK_LT(ends, 1e-14);
            }
            double[] values = Values(7, 9);
            double[] uniform = new double[11];
            for (int i = 0; i < uniform.Length; ++i)
            {
                uniform[i] = i;
            }
            double[] byKnots = new StrandBasis("b-spline", 3, 1, "nonperiodic", 0.5, uniform).Curves(values, 3);
            double[] byMatrix = new StrandBasis("b-spline", 3, 1, "nonperiodic", 0.5, null).Curves(values, 3);
            CHECK_EQ(byKnots.Length, byMatrix.Length);
            double difference = 0;
            for (int i = 0; i < byKnots.Length; ++i)
            {
                difference = Math.Max(difference, Math.Abs(byKnots[i] - byMatrix[i]));
            }
            Console.WriteLine("b-spline with uniform knots differs from the uniform cubic b-spline by " + difference);
            CHECK_LT(difference, 1e-14);
        }

        // The bases of RenderMan through StrandBasis are the curves that "rocap" had (Rocaps.ToBezierCurves)
        [TEST("StrandBasis", "CubicBasesAreUnchanged")]
        public static void CubicBasesAreUnchanged()
        {
            foreach (string name in new string[] { "bezier", "b-spline", "catmull-rom", "hermite", "power" })
            {
                int step = PatchBases.DefaultStep(name);
                double[] g = Values(4 + 2 * step, 10);
                PatchBases.TryGet(name, out double[,] matrix);
                double[] before = Rocaps.ToBezierCurves(g, matrix, step), now = new StrandBasis(name, 3, step, "nonperiodic", 0.5, null).Curves(g, 3);
                CHECK_EQ(before.Length, now.Length);
                for (int i = 0; i < now.Length; ++i)
                {
                    CHECK_EQ(before[i], now[i]);
                }
            }
        }

        // The matrices to Bezier values are the known ones: catmull-rom and cardinal (the inner values a third of the tangent
        // from the points), the uniform cubic b-spline, and bezier itself
        [TEST("StrandBasis", "BezierMatricesAreTheKnownOnes")]
        public static void BezierMatricesAreTheKnownOnes()
        {
            foreach (double tension in new double[] { 0.5, 0.2, 0.9 })
            {
                double third = tension / 3;
                double[,] cardinal = { { 0, 1, 0, 0 }, { -third, 1, third, 0 }, { 0, third, 1, -third }, { 0, 0, 1, 0 } };
                Same(new StrandBasis("cardinal", 3, 1, "nonperiodic", tension, null).BezierMatrix, cardinal);
                if (tension == 0.5)
                {
                    Same(new StrandBasis("catmull-rom", 3, 1, "nonperiodic", 0.5, null).BezierMatrix, cardinal);
                }
            }
            double sixth = 1.0 / 6;
            double[,] spline = { { sixth, 4 * sixth, sixth, 0 }, { 0, 4 * sixth, 2 * sixth, 0 }, { 0, 2 * sixth, 4 * sixth, 0 }, { 0, sixth, 4 * sixth, sixth } };
            Same(new StrandBasis("b-spline", 3, 1, "nonperiodic", 0.5, null).BezierMatrix, spline);
            double[,] identity = { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } };
            Same(new StrandBasis("bezier", 3, 3, "nonperiodic", 0.5, null).BezierMatrix, identity);
        }

        private static void Same(double[,] matrix, double[,] expected)
        {
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    CHECK_LT(Math.Abs(matrix[i, j] - expected[i, j]), 1e-12);
                }
            }
        }
    }
}
