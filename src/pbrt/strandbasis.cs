// (not in pbrt-v4) strandbasis.cs
//
// The bases of the centerline of the strands "rocap" and "ribbon2": the values given at the control points (a coordinate of the
// points, a radius, a color) are turned into the Bezier values of the curves of the strand, which every basis here does exactly.
//
//   "string basis":
//     "linear"       straight pieces from point to point (the "linear" curves of RenderMan and USD); n points give n - 1 curves.
//     "bezier"       "integer degree" 3: 4 + n step points (step 3); 2: 2n + 1 points.
//     "b-spline"     "integer degree" 3: the uniform cubic B-spline; 2: the uniform quadratic one, which starts at the first and
//                    ends at the last point as that of "ribbon" does (n points give n - 2 curves).
//                    With "float knots" (n + degree + 1 of them, not falling): the B-spline of those knots (non-uniform, with
//                    repeated knots for corners and for ends that reach the points), by the blossom of each span.
//     "catmull-rom"  cubic, through the points.
//     "cardinal"     cubic, through the points, with "float tension" (0.5): the tangent at a point is the tension times the
//                    difference of its two neighbors; 0.5 is catmull-rom, 0 gives straight pieces.
//     "monotone"     cubic, through all the points (n points give n - 1 curves, n when closed; no "step" and no pinning):
//                    catmull-rom with its ends pinned for the points and the normals (Vectors), and for every other value
//                    (a radius, a width, a color: Curves) the same tangents held to twice the smaller of the two differences
//                    at a point, and none where the values turn (Steffen 1990): the value between two points never leaves
//                    theirs. A radius that falls to zero stays above it, and a step in a color makes no fringe. Where
//                    nothing is held it is catmull-rom. (Held by coordinate, the points would follow the axes: they are not.)
//     "hermite", "power"  cubic, as in RenderMan.
//   "integer step"   of the cubic bases, as in RenderMan (bezier, power 3; hermite 2; the others 1).
//   "string wrap"    "nonperiodic" (the default);
//                    "periodic": the strand is closed, the control points are taken around;
//                    "pinned" (as in USD, for the cubic b-spline, catmull-rom and cardinal): a point is added before the first
//                    and after the last, the neighbor mirrored in the end point, so that the strand starts at the first point and
//                    ends at the last (n points give n - 1 curves). The other bases reach their ends already.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // StrandBasis Definition
    public sealed class StrandBasis
    {
        // StrandBasis Public Methods
        public StrandBasis(string name, int degree, int step, string wrap, double tension, double[] knots)
        {
            this.name = name;
            this.degree = name == "linear" ? 1 : degree;
            this.step = step;
            this.wrap = wrap;
            this.tension = tension;
            this.knots = knots;
            if (name == "cardinal")
            {
                double t = tension;
                matrix = new double[,] { { -t, 2 - t, t - 2, t }, { 2 * t, t - 3, 3 - 2 * t, -t }, { -t, 0, t, 0 }, { 0, 1, 0, 0 } };
            }
            else if (name == "monotone")
            {
                PatchBases.TryGet("catmull-rom", out matrix);
            }
            else if (this.degree == 3 && knots == null)
            {
                PatchBases.TryGet(name, out matrix);
            }
            if (matrix != null)
            {
                PatchBases.TryGet("bezier", out double[,] bezier);
                toBezier = PatchBases.Multiply(PatchBases.Inverse(bezier), matrix);
            }
        }

        // The basis of the parameters of a shape. defaultName, defaultDegree: of the shape ("degree" is only read for bezier and
        // b-spline; the other bases have one degree).
        public static StrandBasis Read(ParameterDictionary parameters, string shape, string defaultName, int defaultDegree, FileLoc loc)
        {
            // (knots without a basis are those of a b-spline)
            List<float> knotList = parameters.GetFloatArray("knots");
            string name = parameters.GetOneString("basis", knotList.Count > 0 ? "b-spline" : defaultName);
            if (name == "bspline")
            {
                name = "b-spline";
            }
            if (name == "catmullrom" || name == "catmullRom")
            {
                name = "catmull-rom";
            }
            bool known = name == "linear" || name == "bezier" || name == "b-spline" || name == "catmull-rom" || name == "cardinal" || name == "monotone" ||
                         name == "hermite" || name == "power";
            if (!known)
            {
                pbrt.ErrorExit(loc, shape + ": unknown basis \"" + name + "\" (linear, bezier, b-spline, catmull-rom, cardinal, monotone, hermite, power).");
            }
            bool eitherDegree = name == "bezier" || name == "b-spline";
            int degree = name == "linear" ? 1 : (eitherDegree ? parameters.GetOneInt("degree", defaultDegree) : 3);
            if (eitherDegree && degree != 2 && degree != 3)
            {
                pbrt.ErrorExit(loc, shape + ": \"degree\" is 2 or 3.");
            }
            int step = name == "monotone" ? 1 : parameters.GetOneInt("step", name == "cardinal" ? 1 : PatchBases.DefaultStep(name));
            string wrap = parameters.GetOneString("wrap", "nonperiodic");
            if (wrap != "nonperiodic" && wrap != "periodic" && wrap != "pinned")
            {
                pbrt.ErrorExit(loc, shape + ": \"wrap\" is \"nonperiodic\", \"periodic\" or \"pinned\".");
            }
            double tension = parameters.GetOneFloat("tension", 0.5f);
            double[] knots = null;
            if (knotList.Count > 0)
            {
                if (name != "b-spline" || wrap == "periodic")
                {
                    pbrt.ErrorExit(loc, shape + ": \"knots\" are for the basis \"b-spline\" that is not periodic.");
                }
                knots = new double[knotList.Count];
                for (int i = 0; i < knots.Length; ++i)
                {
                    knots[i] = knotList[i];
                }
            }
            return new StrandBasis(name, degree, step, wrap, tension, knots);
        }

        // The degree of the curves: 1, 2 or 3
        public int Degree
        {
            get { return degree; }
        }

        // The chain of cubic Bezier curves that share their ends (3n + 1 points), which needs no change
        // The matrix that takes the four control values of a curve of the basis to its four Bezier values (row i: the Bezier value
        // i), for the cubic bases without knots; null for the others
        public double[,] BezierMatrix
        {
            get { return toBezier; }
        }

        public bool IsCubicBezierChain
        {
            get { return name == "bezier" && degree == 3 && step == 3 && wrap != "periodic" && knots == null; }
        }

        // What is wrong with `count` control points for the basis, or null
        public string Check(int count)
        {
            if (knots != null)
            {
                if (count < degree + 1 || knots.Length != count + degree + 1)
                {
                    return "\"knots\" needs the number of points + the degree + 1 values (" + (count + degree + 1) + "), and at least " + (degree + 1) + " points";
                }
                for (int i = 1; i < knots.Length; ++i)
                {
                    if (knots[i] < knots[i - 1])
                    {
                        return "\"knots\" must not fall";
                    }
                }
                return knots[count] > knots[degree] ? null : "\"knots\" leave no span between knot " + degree + " and knot " + count;
            }
            bool periodic = wrap == "periodic";
            if (degree == 1)
            {
                return count >= (periodic ? 3 : 2) ? null : "at least two points are needed (three for a closed strand)";
            }
            if (degree == 2 && name == "bezier")
            {
                if (periodic)
                {
                    return count >= 4 && count % 2 == 0 ? null : "a closed quadratic bezier needs 2n points, n >= 2";
                }
                return count >= 3 && count % 2 == 1 ? null : "a quadratic bezier needs 2n + 1 points";
            }
            if (degree == 2)
            {
                return count >= 3 ? null : "at least three points are needed";
            }
            if (step < 1)
            {
                return "\"step\" is at least 1";
            }
            if (periodic)
            {
                return count >= 3 && count % step == 0 ? null : "a closed strand needs n * step points (step " + step + "), at least three";
            }
            if (Pinned())
            {
                return count >= 2 ? null : "at least two points are needed";
            }
            return count >= 4 && (count - 4) % step == 0 ? null : "4 + n * step points are needed (the basis \"" + name + "\" with the step " + step + ")";
        }

        // The Bezier values of the curves of the strand for the values g at the control points, of the degree `to` (2 or 3, not
        // less than the degree of the basis): to + 1 values for each curve. A curve of a lower degree is the same curve in the
        // higher one (degree elevation).
        public double[] Curves(double[] g, int to)
        {
            return Raised(name == "monotone" ? Monotone(g) : Native(g), to);
        }

        // The same for a coordinate of the points or of the normals of the strand: the curve must not depend on the axes, and
        // "monotone" holds nothing there
        public double[] Vectors(double[] g, int to)
        {
            return Raised(Native(g), to);
        }

        // StrandBasis Private Methods
        private double[] Raised(double[] values, int to)
        {
            int from = degree;
            if (from == 1 && to >= 2)
            {
                int curves = values.Length / 2;
                double[] raised = new double[3 * curves];
                for (int s = 0; s < curves; ++s)
                {
                    raised[3 * s] = values[2 * s];
                    raised[3 * s + 1] = 0.5 * (values[2 * s] + values[2 * s + 1]);
                    raised[3 * s + 2] = values[2 * s + 1];
                }
                values = raised;
                from = 2;
            }
            if (from == 2 && to == 3)
            {
                int curves = values.Length / 3;
                double[] raised = new double[4 * curves];
                for (int s = 0; s < curves; ++s)
                {
                    raised[4 * s] = values[3 * s];
                    raised[4 * s + 1] = (values[3 * s] + 2 * values[3 * s + 1]) / 3;
                    raised[4 * s + 2] = (2 * values[3 * s + 1] + values[3 * s + 2]) / 3;
                    raised[4 * s + 3] = values[3 * s + 2];
                }
                values = raised;
            }
            return values;
        }

        private bool Pinned()
        {
            if (name == "monotone")
            {
                return wrap != "periodic";
            }
            return wrap == "pinned" && degree == 3 && knots == null && step == 1 && (name == "b-spline" || name == "catmull-rom" || name == "cardinal");
        }

        // The Bezier values of the curves of "monotone" for a value that is not a coordinate: from point to point with the
        // tangents of catmull-rom (half the difference of the neighbors; at an end that is not closed the difference to the
        // one neighbor, as pinned), held to twice the smaller of the differences to the two neighbors and zero where those
        // differ in sign. The two inner Bezier values of a curve are then between its ends, and so is the curve.
        private double[] Monotone(double[] g)
        {
            int n = g.Length;
            bool periodic = wrap == "periodic";
            double[] tangent = new double[n];
            for (int i = 0; i < n; ++i)
            {
                if (!periodic && i == 0)
                {
                    tangent[i] = g[1] - g[0];
                    continue;
                }
                if (!periodic && i == n - 1)
                {
                    tangent[i] = g[n - 1] - g[n - 2];
                    continue;
                }
                double before = g[i] - g[(i + n - 1) % n], after = g[(i + 1) % n] - g[i];
                if (!(before * after > 0))
                {
                    tangent[i] = 0;
                    continue;
                }
                double held = Math.Min(0.5 * Math.Abs(before + after), 2 * Math.Min(Math.Abs(before), Math.Abs(after)));
                tangent[i] = before > 0 ? held : -held;
            }
            int curves = periodic ? n : n - 1;
            double[] values = new double[4 * curves];
            for (int s = 0; s < curves; ++s)
            {
                // (closed, the first curve is from the second point, as that of catmull-rom)
                int from = periodic ? (s + 1) % n : s, next = (from + 1) % n;
                values[4 * s] = g[from];
                values[4 * s + 1] = g[from] + tangent[from] / 3;
                values[4 * s + 2] = g[next] - tangent[next] / 3;
                values[4 * s + 3] = g[next];
            }
            return values;
        }

        // The Bezier values of the curves in the degree of the basis: degree + 1 values for each
        private double[] Native(double[] g)
        {
            int n = g.Length;
            bool periodic = wrap == "periodic";
            if (knots != null)
            {
                return Spans(g);
            }
            if (degree == 1)
            {
                int curves = periodic ? n : n - 1;
                double[] values = new double[2 * curves];
                for (int s = 0; s < curves; ++s)
                {
                    values[2 * s] = g[s];
                    values[2 * s + 1] = g[(s + 1) % n];
                }
                return values;
            }
            if (degree == 2 && name == "bezier")
            {
                int curves = periodic ? n / 2 : (n - 1) / 2;
                double[] values = new double[3 * curves];
                for (int s = 0; s < curves; ++s)
                {
                    values[3 * s] = g[2 * s];
                    values[3 * s + 1] = g[2 * s + 1];
                    values[3 * s + 2] = g[(2 * s + 2) % n];
                }
                return values;
            }
            if (degree == 2)
            {
                // The quadratic B-spline: the curves end at the middles of the control points; not closed, the first curve
                // starts at the first point and the last ends at the last (as "ribbon")
                int curves = periodic ? n : n - 2;
                double[] values = new double[3 * curves];
                for (int s = 0; s < curves; ++s)
                {
                    double a = g[s], b = g[(s + 1) % n], c = g[(s + 2) % n];
                    values[3 * s] = !periodic && s == 0 ? a : 0.5 * (a + b);
                    values[3 * s + 1] = b;
                    values[3 * s + 2] = !periodic && s == curves - 1 ? c : 0.5 * (b + c);
                }
                return values;
            }
            double[] control = g;
            if (Pinned())
            {
                control = new double[n + 2];
                Array.Copy(g, 0, control, 1, n);
                control[0] = 2 * g[0] - g[1];
                control[n + 1] = 2 * g[n - 1] - g[n - 2];
                n += 2;
            }
            int count = periodic ? n / step : (n - 4) / step + 1;
            double[] result = new double[4 * count];
            for (int s = 0; s < count; ++s)
            {
                for (int i = 0; i < 4; ++i)
                {
                    double value = 0;
                    for (int k = 0; k < 4; ++k)
                    {
                        value += toBezier[i, k] * control[(s * step + k) % n];
                    }
                    result[4 * s + i] = value;
                }
            }
            return result;
        }

        // The Bezier values of the spans of the B-spline with the knots: for the span from the knot a to the knot b, value k is
        // the blossom of the spline with degree - k arguments a and k arguments b (the algorithm of de Boor with one argument
        // for each level). Spans without length are left out.
        private double[] Spans(double[] g)
        {
            int n = g.Length, d = degree;
            List<double> values = new List<double>();
            Span<double> level = stackalloc double[4];
            for (int span = d; span < n; ++span)
            {
                double a = knots[span], b = knots[span + 1];
                if (!(b > a))
                {
                    continue;
                }
                for (int k = 0; k <= d; ++k)
                {
                    for (int j = 0; j <= d; ++j)
                    {
                        level[j] = g[span - d + j];
                    }
                    for (int r = 1; r <= d; ++r)
                    {
                        // The arguments: a for the first d - k levels, b for the others
                        double argument = r <= d - k ? a : b;
                        for (int j = d; j >= r; --j)
                        {
                            int index = span - d + j;
                            double low = knots[index], high = knots[index + d - r + 1];
                            double alpha = (argument - low) / (high - low);
                            level[j] = (1 - alpha) * level[j - 1] + alpha * level[j];
                        }
                    }
                    values.Add(level[d]);
                }
            }
            return values.ToArray();
        }

        // StrandBasis Private Members
        private readonly string name;
        private readonly int degree;
        private readonly int step;
        private readonly string wrap;
        private readonly double tension;
        private readonly double[] knots;
        private readonly double[,] matrix;
        private readonly double[,] toBezier;
    }
}
