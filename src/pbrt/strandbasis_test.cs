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

        // "monotone": through the points; a value between two points stays between theirs (where catmull-rom leaves them);
        // where nothing is held it is catmull-rom pinned; the points and the normals are catmull-rom pinned always
        [TEST("StrandBasis", "MonotoneStaysBetweenThePoints")]
        public static void MonotoneStaysBetweenThePoints()
        {
            foreach (bool closed in new bool[] { false, true })
            {
                string wrap = closed ? "periodic" : "nonperiodic";
                StrandBasis monotone = new StrandBasis("monotone", 3, 1, wrap, 0.5, null);
                StrandBasis catmull = new StrandBasis("catmull-rom", 3, 1, closed ? "periodic" : "pinned", 0.5, null);
                // (a radius that falls to nothing, a step, and values at random)
                double[][] cases = { new double[] { 1, 1, 1, 0.05, 0, 0 }, new double[] { 0, 0, 0, 1, 1, 1, 0.2, 0.2 }, Values(9, 5), Values(12, 8) };
                foreach (double[] g in cases)
                {
                    int n = g.Length, curves = closed ? n : n - 1;
                    double[] held = monotone.Curves(g, 3), free = catmull.Curves(g, 3);
                    CHECK_EQ(held.Length, 4 * curves);
                    CHECK_EQ(monotone.Check(n) == null, true);
                    double misses = 0, outside = 0, outsideFree = 0, jump = 0;
                    for (int s = 0; s < curves; ++s)
                    {
                        // (closed, the first curve is from the second point, as that of catmull-rom)
                        int from = closed ? (s + 1) % n : s;
                        double a = g[from], b = g[(from + 1) % n], low = Math.Min(a, b), high = Math.Max(a, b);
                        misses = Math.Max(misses, Math.Max(Math.Abs(held[4 * s] - a), Math.Abs(held[4 * s + 3] - b)));
                        for (int k = 0; k <= 50; ++k)
                        {
                            double value = Bezier(held, 3, s, k / 50.0), other = Bezier(free, 3, s, k / 50.0);
                            outside = Math.Max(outside, Math.Max(low - value, value - high));
                            outsideFree = Math.Max(outsideFree, Math.Max(low - other, other - high));
                        }
                        // (the tangent goes on from one curve to the next)
                        if (closed || s + 1 < curves)
                        {
                            int next = (s + 1) % curves;
                            jump = Math.Max(jump, Math.Abs(held[4 * s + 3] - held[4 * s + 2] - (held[4 * next + 1] - held[4 * next])));
                        }
                    }
                    Console.WriteLine("monotone (" + wrap + ", " + n + " values): misses the points by " + misses + ", leaves them by " + outside + " (catmull-rom by " +
                                      outsideFree + "), the tangent jumps by " + jump);
                    CHECK_LT(misses, 1e-15);
                    CHECK_LT(outside, 1e-15);
                    CHECK_LT(jump, 1e-14);
                }
                // Values that rise evenly enough that nothing is held: catmull-rom
                double[] gentle = new double[8];
                for (int i = 0; i < 8; ++i)
                {
                    gentle[i] = 0.4 * i + 0.05 * i * i;
                }
                double[] one = monotone.Curves(gentle, 3), two = catmull.Curves(gentle, 3), vectors = monotone.Vectors(Values(8, 2), 3), plain = catmull.Curves(Values(8, 2), 3);
                double worst = 0, worstVectors = 0;
                for (int i = 0; i < one.Length; ++i)
                {
                    worst = Math.Max(worst, Math.Abs(one[i] - two[i]));
                    worstVectors = Math.Max(worstVectors, Math.Abs(vectors[i] - plain[i]));
                }
                Console.WriteLine("monotone (" + wrap + "): differs from catmull-rom where nothing is held by " + worst + ", its points by " + worstVectors);
                // (closed, the values fall back from the last to the first, and are held there: the curves away from that)
                if (closed)
                {
                    worst = 0;
                    for (int i = 4; i < one.Length - 12; ++i)
                    {
                        worst = Math.Max(worst, Math.Abs(one[i] - two[i]));
                    }
                }
                CHECK_LT(worst, 1e-14);
                CHECK_LT(worstVectors, 1e-14);
            }
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

        // The points of a strand at uneven distances: `count` of them, in three coordinates
        private static void UnevenPoints(int count, int seed, out double[] x, out double[] y, out double[] z)
        {
            Random random = new Random(seed);
            x = new double[count];
            y = new double[count];
            z = new double[count];
            double px = 0, py = 0, pz = 0;
            for (int i = 0; i < count; ++i)
            {
                // (steps from a hundredth to ten: three orders between neighbors)
                double length = Math.Pow(10, 3 * random.NextDouble() - 2);
                double dx = 2 * random.NextDouble() - 1, dy = 2 * random.NextDouble() - 1, dz = 2 * random.NextDouble() - 1;
                double norm = Math.Sqrt(dx * dx + dy * dy + dz * dz) + 1e-9;
                px += length * dx / norm;
                py += length * dy / norm;
                pz += length * dz / norm;
                x[i] = px;
                y[i] = py;
                z[i] = pz;
            }
        }

        // The control values with those of "pinned" or of a closed strand around them, and the knots of the points
        private static double[] Around(double[] g, string wrap)
        {
            int n = g.Length;
            if (wrap == "pinned")
            {
                double[] control = new double[n + 2];
                Array.Copy(g, 0, control, 1, n);
                control[0] = 2 * g[0] - g[1];
                control[n + 1] = 2 * g[n - 1] - g[n - 2];
                return control;
            }
            if (wrap == "periodic")
            {
                double[] control = new double[n + 3];
                for (int i = 0; i < n + 3; ++i)
                {
                    control[i] = g[i % n];
                }
                return control;
            }
            return g;
        }

        // Catmull-Rom with the knots k at the four values g from `at`, at the knot value t between k[at + 1] and k[at + 2]: the
        // pyramid of Barry and Goldman, which the two papers start from
        private static double Pyramid(double[] g, double[] k, int at, double t)
        {
            double t0 = k[at], t1 = k[at + 1], t2 = k[at + 2], t3 = k[at + 3];
            double a1 = (t1 - t) / (t1 - t0) * g[at] + (t - t0) / (t1 - t0) * g[at + 1];
            double a2 = (t2 - t) / (t2 - t1) * g[at + 1] + (t - t1) / (t2 - t1) * g[at + 2];
            double a3 = (t3 - t) / (t3 - t2) * g[at + 2] + (t - t2) / (t3 - t2) * g[at + 3];
            double b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
            double b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
            return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
        }

        // "parameterization": the Bezier curves are the Catmull-Rom curve of the knots of the points (the distance between two
        // points raised to the power), for the points and for a value along them, with every wrap
        [TEST("StrandBasis", "ParameterizedIsTheCurveOfItsKnots")]
        public static void ParameterizedIsTheCurveOfItsKnots()
        {
            double worst = 0;
            int curvesSeen = 0;
            foreach (double power in new double[] { 0.25, 0.5, 2.0 / 3.0, 1 })
            {
                foreach (string wrap in new string[] { "nonperiodic", "pinned", "periodic" })
                {
                    for (int seed = 1; seed <= 6; ++seed)
                    {
                        int n = 5 + seed;
                        UnevenPoints(n, 10 * seed + 3, out double[] x, out double[] y, out double[] z);
                        double[] radius = Values(n, seed);
                        StrandBasis basis = new StrandBasis("catmull-rom", 3, 1, wrap, 0.5, null, power).Parameterized(x, y, z);
                        CHECK(basis.Check(n) == null);
                        double[] ax = Around(x, wrap), ay = Around(y, wrap), az = Around(z, wrap);
                        double[] knots = new double[ax.Length];
                        for (int i = 1; i < ax.Length; ++i)
                        {
                            double dx = ax[i] - ax[i - 1], dy = ay[i] - ay[i - 1], dz = az[i] - az[i - 1];
                            knots[i] = knots[i - 1] + Math.Pow(Math.Sqrt(dx * dx + dy * dy + dz * dz), power);
                        }
                        double[][] given = { x, y, z, radius };
                        for (int value = 0; value < 4; ++value)
                        {
                            double[] curves = value < 3 ? basis.Vectors(given[value], 3) : basis.Curves(given[value], 3);
                            double[] around = Around(given[value], wrap);
                            CHECK_EQ(curves.Length / 4, around.Length - 3);
                            double scale = 0;
                            foreach (double g in around)
                            {
                                scale = Math.Max(scale, Math.Abs(g));
                            }
                            for (int c = 0; c < curves.Length / 4; ++c)
                            {
                                ++curvesSeen;
                                for (int j = 0; j <= 16; ++j)
                                {
                                    double u = j / 16.0;
                                    double t = (1 - u) * knots[c + 1] + u * knots[c + 2];
                                    worst = Math.Max(worst, Math.Abs(Bezier(curves, 3, c, u) - Pyramid(around, knots, c, t)) / scale);
                                }
                            }
                        }
                    }
                }
            }
            Console.WriteLine("parameterized catmull-rom: " + curvesSeen + " curves differ from the pyramid of their knots by " + worst + " (relative)");
            CHECK_LT(worst, 1e-9);
        }

        // Points at even distances have uniform knots whatever the power: the curve of the matrix
        [TEST("StrandBasis", "ParameterizedWithEvenPointsIsUniform")]
        public static void ParameterizedWithEvenPointsIsUniform()
        {
            int n = 9;
            double[] x = new double[n], y = new double[n], z = new double[n];
            for (int i = 0; i < n; ++i)
            {
                // (a ring: the same chord between every two neighbors)
                x[i] = 3 * Math.Cos(2 * Math.PI * i / n);
                y[i] = 3 * Math.Sin(2 * Math.PI * i / n);
                z[i] = 0.5;
            }
            double[] radius = Values(n, 4);
            double worst = 0;
            foreach (string name in new string[] { "catmull-rom", "monotone" })
            {
                foreach (double power in new double[] { 0.5, 1 })
                {
                    StrandBasis uniform = new StrandBasis(name, 3, 1, "periodic", 0.5, null);
                    StrandBasis basis = new StrandBasis(name, 3, 1, "periodic", 0.5, null, power).Parameterized(x, y, z);
                    double[][] given = { x, y, z, radius };
                    for (int value = 0; value < 4; ++value)
                    {
                        double[] expected = value < 3 ? uniform.Vectors(given[value], 3) : uniform.Curves(given[value], 3);
                        double[] curves = value < 3 ? basis.Vectors(given[value], 3) : basis.Curves(given[value], 3);
                        CHECK_EQ(curves.Length, expected.Length);
                        for (int i = 0; i < curves.Length; ++i)
                        {
                            worst = Math.Max(worst, Math.Abs(curves[i] - expected[i]));
                        }
                    }
                }
            }
            Console.WriteLine("parameterized, even points: differs from the uniform basis by " + worst);
            CHECK_LT(worst, 1e-12);
        }

        // The distance of a curve from the line through its two points, over the distance between them: no more than a quarter
        // for the centripetal and the chordal curve and an eighth for the power 2/3 (section 4 of the papers), whatever the
        // neighbors. The uniform curve has no such bound. And the centripetal curve does not stop where it is looked at (the
        // papers: no cusp; they give no least speed).
        [TEST("StrandBasis", "CentripetalStaysByItsEdges")]
        public static void CentripetalStaysByItsEdges()
        {
            double[] powers = { 0, 0.5, 2.0 / 3.0, 1 };
            double[] farthest = new double[4], slowest = { double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue };
            for (int seed = 1; seed <= 400; ++seed)
            {
                int n = 8;
                UnevenPoints(n, seed, out double[] x, out double[] y, out double[] z);
                for (int p = 0; p < 4; ++p)
                {
                    StrandBasis basis = new StrandBasis("catmull-rom", 3, 1, "pinned", 0.5, null, powers[p]).Parameterized(x, y, z);
                    double[] cx = basis.Vectors(x, 3), cy = basis.Vectors(y, 3), cz = basis.Vectors(z, 3);
                    CHECK_EQ(cx.Length, 4 * (n - 1));
                    for (int c = 0; c < n - 1; ++c)
                    {
                        double ex = x[c + 1] - x[c], ey = y[c + 1] - y[c], ez = z[c + 1] - z[c];
                        double edge = Math.Sqrt(ex * ex + ey * ey + ez * ez);
                        for (int j = 0; j <= 64; ++j)
                        {
                            double t = j / 64.0;
                            double dx = Bezier(cx, 3, c, t) - x[c], dy = Bezier(cy, 3, c, t) - y[c], dz = Bezier(cz, 3, c, t) - z[c];
                            double along = (dx * ex + dy * ey + dz * ez) / edge;
                            double away = Math.Sqrt(Math.Max(0, dx * dx + dy * dy + dz * dz - along * along));
                            farthest[p] = Math.Max(farthest[p], away / edge);
                            // (the speed of the curve over the distance between its points)
                            double s = 1 - t;
                            double vx = 3 * (s * s * (cx[4 * c + 1] - cx[4 * c]) + 2 * s * t * (cx[4 * c + 2] - cx[4 * c + 1]) + t * t * (cx[4 * c + 3] - cx[4 * c + 2]));
                            double vy = 3 * (s * s * (cy[4 * c + 1] - cy[4 * c]) + 2 * s * t * (cy[4 * c + 2] - cy[4 * c + 1]) + t * t * (cy[4 * c + 3] - cy[4 * c + 2]));
                            double vz = 3 * (s * s * (cz[4 * c + 1] - cz[4 * c]) + 2 * s * t * (cz[4 * c + 2] - cz[4 * c + 1]) + t * t * (cz[4 * c + 3] - cz[4 * c + 2]));
                            slowest[p] = Math.Min(slowest[p], Math.Sqrt(vx * vx + vy * vy + vz * vz) / edge);
                        }
                    }
                }
            }
            for (int p = 0; p < 4; ++p)
            {
                Console.WriteLine("power " + powers[p].ToString("F3") + ": farthest from the line of its two points " + farthest[p].ToString("F4") +
                                  " of their distance; slowest speed " + slowest[p].ToString("G4") + " of it");
            }
            CHECK_GT(farthest[0], 1);
            CHECK_LT(farthest[1], 0.25 + 1e-9);
            CHECK_LT(farthest[2], 0.125 + 1e-9);
            CHECK_LT(farthest[3], 0.25 + 1e-9);
            CHECK_GT(slowest[1], 0);
        }

        // "monotone" with a parameterization: the values between two points stay between theirs, also with points that are the
        // same (no interval between them), and the curve still goes through the values
        [TEST("StrandBasis", "ParameterizedMonotoneStaysBetweenThePoints")]
        public static void ParameterizedMonotoneStaysBetweenThePoints()
        {
            double outside = 0, misses = 0;
            foreach (bool closed in new bool[] { false, true })
            {
                for (int seed = 1; seed <= 40; ++seed)
                {
                    int n = 9;
                    UnevenPoints(n, seed, out double[] x, out double[] y, out double[] z);
                    if (seed % 4 == 0)
                    {
                        x[4] = x[3];
                        y[4] = y[3];
                        z[4] = z[3];
                    }
                    double[] g = seed % 2 == 0 ? Values(n, seed) : new double[] { 1, 1, 1, 0.05, 0, 0, 0.7, 0.2, 0.2 };
                    StrandBasis basis = new StrandBasis("monotone", 3, 1, closed ? "periodic" : "nonperiodic", 0.5, null, 0.5).Parameterized(x, y, z);
                    double[] held = basis.Curves(g, 3);
                    int curves = closed ? n : n - 1;
                    CHECK_EQ(held.Length, 4 * curves);
                    foreach (double[] coordinate in new double[][] { basis.Vectors(x, 3), basis.Vectors(y, 3), basis.Vectors(z, 3), held })
                    {
                        foreach (double value in coordinate)
                        {
                            CHECK(!double.IsNaN(value) && !double.IsInfinity(value));
                        }
                    }
                    for (int s = 0; s < curves; ++s)
                    {
                        int from = closed ? (s + 1) % n : s;
                        double a = g[from], b = g[(from + 1) % n], low = Math.Min(a, b), high = Math.Max(a, b);
                        misses = Math.Max(misses, Math.Max(Math.Abs(held[4 * s] - a), Math.Abs(held[4 * s + 3] - b)));
                        for (int k = 0; k <= 50; ++k)
                        {
                            double value = Bezier(held, 3, s, k / 50.0);
                            outside = Math.Max(outside, Math.Max(low - value, value - high));
                        }
                    }
                }
            }
            Console.WriteLine("parameterized monotone: leaves the values of its two points by " + outside + ", misses them by " + misses);
            CHECK_LT(outside, 1e-12);
            CHECK_LT(misses, 1e-12);
        }

        // The distance from the point p to the straight piece from a to b
        private static double FromPiece(double px, double py, double pz, double ax, double ay, double az, double bx, double by, double bz)
        {
            double ex = bx - ax, ey = by - ay, ez = bz - az;
            double length2 = ex * ex + ey * ey + ez * ez;
            double t = length2 > 0 ? Math.Clamp(((px - ax) * ex + (py - ay) * ey + (pz - az) * ez) / length2, 0, 1) : 0;
            double dx = px - ax - t * ex, dy = py - ay - t * ey, dz = pz - az - t * ez;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // "cardinal" with a parameterization: the tension 0.5 is catmull-rom, 0 gives straight pieces, and the tangents are in
        // proportion to the tension
        [TEST("StrandBasis", "ParameterizedCardinalHasTheTension")]
        public static void ParameterizedCardinalHasTheTension()
        {
            double sameAsCatmullRom = 0, straight = 0, proportion = 0;
            foreach (string wrap in new string[] { "nonperiodic", "pinned", "periodic" })
            {
                UnevenPoints(9, 21, out double[] x, out double[] y, out double[] z);
                double[] radius = Values(9, 2);
                StrandBasis catmull = new StrandBasis("catmull-rom", 3, 1, wrap, 0.5, null, 0.5).Parameterized(x, y, z);
                StrandBasis half = new StrandBasis("cardinal", 3, 1, wrap, 0.5, null, 0.5).Parameterized(x, y, z);
                StrandBasis none = new StrandBasis("cardinal", 3, 1, wrap, 0, null, 0.5).Parameterized(x, y, z);
                StrandBasis third = new StrandBasis("cardinal", 3, 1, wrap, 0.3, null, 0.5).Parameterized(x, y, z);
                foreach (double[] g in new double[][] { x, y, z, radius })
                {
                    double[] expected = catmull.Vectors(g, 3), same = half.Vectors(g, 3), pieces = none.Curves(g, 3), scaled = third.Vectors(g, 3);
                    for (int i = 0; i < expected.Length; ++i)
                    {
                        sameAsCatmullRom = Math.Max(sameAsCatmullRom, Math.Abs(expected[i] - same[i]));
                    }
                    for (int c = 0; c < expected.Length / 4; ++c)
                    {
                        double a = expected[4 * c], b = expected[4 * c + 3];
                        straight = Math.Max(straight, Math.Max(Math.Abs(pieces[4 * c + 1] - a), Math.Abs(pieces[4 * c + 2] - b)));
                        // (the tangents: 0.3 / 0.5 of those of catmull-rom)
                        proportion = Math.Max(proportion, Math.Abs((scaled[4 * c + 1] - a) - 0.6 * (expected[4 * c + 1] - a)));
                        proportion = Math.Max(proportion, Math.Abs((scaled[4 * c + 2] - b) - 0.6 * (expected[4 * c + 2] - b)));
                    }
                }
            }
            Console.WriteLine("parameterized cardinal: tension 0.5 differs from catmull-rom by " + sameAsCatmullRom + ", tension 0 from straight pieces by " + straight +
                              ", tension 0.3 from 0.6 of the tangents by " + proportion);
            CHECK_LT(sameAsCatmullRom, 1e-12);
            CHECK_LT(straight, 1e-12);
            CHECK_LT(proportion, 1e-12);
        }

        // DistanceBounds: no point of a curve is farther from the straight piece between its two points than the bound, which
        // comes from the distances between the points alone; how much of the bound the curves use is printed
        [TEST("StrandBasis", "DistanceBoundsHoldTheCurves")]
        public static void DistanceBoundsHoldTheCurves()
        {
            foreach (double power in new double[] { 0.5, 2.0 / 3.0, 0.8, 1 })
            {
                double worst = 0, used = 0, largest = 0;
                int curvesSeen = 0;
                foreach (string wrap in new string[] { "nonperiodic", "pinned", "periodic" })
                {
                    for (int seed = 1; seed <= 300; ++seed)
                    {
                        int n = 8;
                        UnevenPoints(n, 1000 + seed, out double[] x, out double[] y, out double[] z);
                        StrandBasis basis = new StrandBasis("catmull-rom", 3, 1, wrap, 0.5, null, power).Parameterized(x, y, z);
                        CHECK(basis.DistanceBounds(x, y, z, out double[] bounds));
                        double[] cx = basis.Vectors(x, 3), cy = basis.Vectors(y, 3), cz = basis.Vectors(z, 3);
                        CHECK_EQ(bounds.Length, cx.Length / 4);
                        for (int c = 0; c < bounds.Length; ++c)
                        {
                            double ax = cx[4 * c], ay = cy[4 * c], az = cz[4 * c], bx = cx[4 * c + 3], by = cy[4 * c + 3], bz = cz[4 * c + 3];
                            double edge = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay) + (bz - az) * (bz - az));
                            double farthest = 0;
                            for (int j = 0; j <= 64; ++j)
                            {
                                double t = j / 64.0;
                                farthest = Math.Max(farthest, FromPiece(Bezier(cx, 3, c, t), Bezier(cy, 3, c, t), Bezier(cz, 3, c, t), ax, ay, az, bx, by, bz));
                            }
                            worst = Math.Max(worst, (farthest - bounds[c]) / edge);
                            used += bounds[c] > 0 ? farthest / bounds[c] : 1;
                            largest = Math.Max(largest, bounds[c] / edge);
                            ++curvesSeen;
                        }
                    }
                }
                Console.WriteLine("power " + power.ToString("F3") + ": " + curvesSeen + " curves, past their bound by " + worst.ToString("G3") +
                                  " of the piece at most; the curves use " + (100 * used / curvesSeen).ToString("F1") + "% of the bound on average; largest bound " +
                                  largest.ToString("F4") + " of the piece");
                CHECK_LT(worst, 1e-9);
            }
            // No bound: the uniform curve, "monotone", a cardinal curve of another tension
            UnevenPoints(8, 5, out double[] ux, out double[] uy, out double[] uz);
            CHECK(!new StrandBasis("catmull-rom", 3, 1, "pinned", 0.5, null).DistanceBounds(ux, uy, uz, out double[] _));
            CHECK(!new StrandBasis("monotone", 3, 1, "nonperiodic", 0.5, null, 0.5).Parameterized(ux, uy, uz).DistanceBounds(ux, uy, uz, out double[] _));
            CHECK(!new StrandBasis("cardinal", 3, 1, "pinned", 0.3, null, 0.5).Parameterized(ux, uy, uz).DistanceBounds(ux, uy, uz, out double[] _));
            CHECK(new StrandBasis("cardinal", 3, 1, "pinned", 0.5, null, 0.5).Parameterized(ux, uy, uz).DistanceBounds(ux, uy, uz, out double[] _));
        }

        // The closest that two curves of a strand that are not in a row come to each other, by their points at 33 values each
        private static double ClosestApproach(double[] cx, double[] cy, double[] cz, bool closed)
        {
            int curves = cx.Length / 4;
            double closest = double.MaxValue;
            for (int s = 0; s < curves; ++s)
            {
                for (int t = s + 2; t < curves; ++t)
                {
                    if (closed && (t + 1) % curves == s)
                    {
                        continue;
                    }
                    for (int i = 0; i <= 32; ++i)
                    {
                        double ax = Bezier(cx, 3, s, i / 32.0), ay = Bezier(cy, 3, s, i / 32.0), az = Bezier(cz, 3, s, i / 32.0);
                        for (int j = 0; j <= 32; ++j)
                        {
                            double dx = Bezier(cx, 3, t, j / 32.0) - ax, dy = Bezier(cy, 3, t, j / 32.0) - ay, dz = Bezier(cz, 3, t, j / 32.0) - az;
                            closest = Math.Min(closest, Math.Sqrt(dx * dx + dy * dy + dz * dz));
                        }
                    }
                }
            }
            return closest;
        }

        // SelfIntersection: a helix passes the rules, a strand that crosses itself and one with a sharp turn do not, and of
        // strands in a plane at random none that passes comes near itself
        [TEST("StrandBasis", "SelfIntersectionRules")]
        public static void SelfIntersectionRules()
        {
            StrandBasis centripetal = new StrandBasis("catmull-rom", 3, 1, "pinned", 0.5, null, 0.5);
            int n = 24;
            double[] x = new double[n], y = new double[n], z = new double[n];
            for (int i = 0; i < n; ++i)
            {
                x[i] = Math.Cos(0.9 * i);
                y[i] = Math.Sin(0.9 * i);
                z[i] = 0.35 * i;
            }
            string helix = centripetal.Parameterized(x, y, z).SelfIntersection(x, y, z);
            Console.WriteLine("helix: " + (helix ?? "free"));
            CHECK(helix == null);

            // (a figure of eight in a plane: it crosses itself)
            double[] ex = { 0, 2, 4, 4, 2, 0, 0 }, ey = { 0, 2, 2, 0, 0, 2, 0.3 }, ez = new double[7];
            string eight = centripetal.Parameterized(ex, ey, ez).SelfIntersection(ex, ey, ez);
            Console.WriteLine("figure of eight: " + eight);
            CHECK(eight != null);

            // (a turn of 150 degrees: an angle of 30 between the pieces)
            double[] sx = { 0, 1, 2, 2 - Math.Cos(Math.PI / 6), 2 - 2 * Math.Cos(Math.PI / 6) }, sy = { 0, 0, 0, 0.5, 1 }, sz = new double[5];
            string sharp = centripetal.Parameterized(sx, sy, sz).SelfIntersection(sx, sy, sz);
            Console.WriteLine("sharp turn: " + sharp);
            CHECK(sharp != null && sharp.Contains("angle at point 2"));

            // (another parameterization: the rules are not for it)
            CHECK(new StrandBasis("catmull-rom", 3, 1, "pinned", 0.5, null, 1).Parameterized(x, y, z).SelfIntersection(x, y, z) != null);

            // Strands in a plane at random (where a strand crosses itself often): those that pass stay away from themselves
            Random random = new Random(77);
            int passed = 0, failed = 0, crossingAmongFailed = 0;
            double closestPassed = double.MaxValue;
            for (int trial = 0; trial < 3000; ++trial)
            {
                int count = 7;
                double[] px = new double[count], py = new double[count], pz = new double[count];
                double heading = 2 * Math.PI * random.NextDouble();
                for (int i = 1; i < count; ++i)
                {
                    heading += (2 * random.NextDouble() - 1) * 1.9;
                    double length = 0.3 + 1.7 * random.NextDouble();
                    px[i] = px[i - 1] + length * Math.Cos(heading);
                    py[i] = py[i - 1] + length * Math.Sin(heading);
                }
                StrandBasis basis = centripetal.Parameterized(px, py, pz);
                double closest = ClosestApproach(basis.Vectors(px, 3), basis.Vectors(py, 3), basis.Vectors(pz, 3), false);
                if (basis.SelfIntersection(px, py, pz) == null)
                {
                    ++passed;
                    closestPassed = Math.Min(closestPassed, closest);
                }
                else
                {
                    ++failed;
                    crossingAmongFailed += closest < 0.02 ? 1 : 0;
                }
            }
            Console.WriteLine("strands in a plane: " + passed + " pass (the closest that one comes to itself: " + closestPassed.ToString("F4") + "), " + failed +
                              " do not (" + crossingAmongFailed + " of them come within 0.02 of themselves)");
            CHECK_GT(passed, 100);
            CHECK_GT(closestPassed, 0.02);
            CHECK_GT(crossingAmongFailed, 50);
        }
    }
}
