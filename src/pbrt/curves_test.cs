// (not in pbrt-v4) curves_test.cs
//
// Tests of the RenderMan curves (shapes_curves.cs): the centerline of every segment of every basis is written out again here as a polynomial (not
// through the basis matrices) and rays that pass just inside or just outside of the width at that point must hit or miss.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class CurvesTests
    {
        private static ParsedParameter Param(string type, string name)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = type;
            p.name = name;
            return p;
        }

        private static List<ParsedParameter> Parameters(int[] nvertices, double[][] points, string type, string wrap, string basis, int step, float[] widths)
        {
            List<ParsedParameter> list = new List<ParsedParameter>();
            ParsedParameter nv = Param("integer", "nvertices");
            foreach (int n in nvertices)
            {
                nv.ints.push_back(n);
            }
            list.Add(nv);
            ParsedParameter pp = Param("point3", "P");
            foreach (double[] q in points)
            {
                pp.floats.push_back((float)q[0]);
                pp.floats.push_back((float)q[1]);
                pp.floats.push_back((float)q[2]);
            }
            list.Add(pp);
            ParsedParameter t = Param("string", "type");
            t.strings.push_back(type);
            list.Add(t);
            ParsedParameter w = Param("string", "wrap");
            w.strings.push_back(wrap);
            list.Add(w);
            ParsedParameter b = Param("string", "basis");
            b.strings.push_back(basis);
            list.Add(b);
            if (step > 0)
            {
                ParsedParameter s = Param("integer", "step");
                s.ints.push_back(step);
                list.Add(s);
            }
            if (widths != null)
            {
                ParsedParameter wd = Param("float", "width");
                foreach (float v in widths)
                {
                    wd.floats.push_back(v);
                }
                list.Add(wd);
            }
            return list;
        }

        private static pstd.vector<Shape> Make(List<ParsedParameter> list)
        {
            return Shape.Create("curves", new Transform(), new Transform(), false, new ParameterDictionary(list, RGBColorSpace.sRGB), new Dictionary<string, FloatTexture>(),
                                null, new Allocator());
        }

        // The point of the segment with the control points g at s, written out per basis
        private static double[] Evaluate(string basis, double[][] g, double s)
        {
            double[] r = new double[3];
            double s2 = s * s, s3 = s2 * s, m = 1 - s;
            double[] w = new double[4];
            switch (basis)
            {
                case "bezier":
                    w = new double[] { m * m * m, 3 * m * m * s, 3 * m * s2, s3 };
                    break;
                case "b-spline":
                    w = new double[] { m * m * m / 6, (3 * s3 - 6 * s2 + 4) / 6, (-3 * s3 + 3 * s2 + 3 * s + 1) / 6, s3 / 6 };
                    break;
                case "catmull-rom":
                    w = new double[] { 0.5 * (-s3 + 2 * s2 - s), 0.5 * (3 * s3 - 5 * s2 + 2), 0.5 * (-3 * s3 + 4 * s2 + s), 0.5 * (s3 - s2) };
                    break;
                default:
                    w = new double[] { 2 * s3 - 3 * s2 + 1, s3 - 2 * s2 + s, -2 * s3 + 3 * s2, s3 - s2 };
                    break;
            }
            for (int k = 0; k < 4; ++k)
            {
                for (int a = 0; a < 3; ++a)
                {
                    r[a] += w[k] * g[k][a];
                }
            }
            return r;
        }

        private static double[][] Wave(int count, double x0, double y0, double dx, double amplitude, double phase)
        {
            double[][] p = new double[count][];
            for (int i = 0; i < count; ++i)
            {
                p[i] = new double[] { x0 + dx * i, y0 + amplitude * Math.Sin(0.9 * i + phase), 0 };
            }
            return p;
        }

        // Rays along -z at the lateral offsets of the fraction of the half width
        private static void CheckSegment(string label, pstd.vector<Shape> shapes, Func<double, double[]> position, double w0, double w1)
        {
            for (int k = 0; k < 8; ++k)
            {
                // (not at the multiples of 1/8, where the pieces of a segment meet)
                double s = 0.08 + 0.12 * k;
                double[] c = position(s);
                double[] a = position(s - 1e-4), b = position(s + 1e-4);
                double tx = b[0] - a[0], ty = b[1] - a[1], l = Math.Sqrt(tx * tx + ty * ty);
                double lx = -ty / l, ly = tx / l;
                double half = 0.5 * (w0 + (w1 - w0) * s);
                foreach (double side in new double[] { -1, 1 })
                {
                    foreach (double fraction in new double[] { 0.0, 0.85, 1.2 })
                    {
                        Ray ray = new Ray(new Point3f((Float)(c[0] + side * fraction * half * lx), (Float)(c[1] + side * fraction * half * ly), 5), new Vector3f(0, 0, -1));
                        bool hit = false;
                        double tHit = 0;
                        for (int i = 0; i < (int)shapes.size(); ++i)
                        {
                            pstd.optional<ShapeIntersection> si = shapes[i].Intersect(ray);
                            if (si.has_value())
                            {
                                hit = true;
                                tHit = si.value().tHit;
                            }
                        }
                        EXPECT_EQ(fraction < 1, hit, label + " s " + s + " side " + side + " fraction " + fraction);
                        if (hit)
                        {
                            EXPECT_NEAR(5.0, tHit, 1e-3, label + " t");
                        }
                    }
                }
            }
        }

        [TEST("Curves", "CubicBases")]
        public static void CubicBases()
        {
            string[] bases = { "bezier", "b-spline", "catmull-rom", "hermite" };
            int[] counts = { 7, 6, 6, 6 };
            int[] steps = { 3, 1, 1, 2 };
            int[] segmentCounts = { 2, 3, 3, 2 };
            for (int bi = 0; bi < bases.Length; ++bi)
            {
                double[][] points = Wave(counts[bi], 0, 0, 1.3, 0.8, bi);
                int segments = segmentCounts[bi];
                float[] widths = new float[segments + 1];
                for (int i = 0; i <= segments; ++i)
                {
                    widths[i] = 0.12f + 0.07f * i;
                }
                List<ParsedParameter> list = Parameters(new int[] { counts[bi] }, points, "cubic", "nonperiodic", bases[bi], steps[bi], widths);
                ParsedParameter split = Param("integer", "splitdepth");
                split.ints.push_back(0);
                list.Add(split);
                pstd.vector<Shape> shapes = Make(list);
                EXPECT_EQ(segments, (int)shapes.size(), bases[bi] + " number of segments");
                list.RemoveAt(list.Count - 1);
                shapes = Make(list);
                EXPECT_EQ(segments * 8, (int)shapes.size(), bases[bi] + " number of pieces");
                for (int s = 0; s < segments; ++s)
                {
                    int first = s * steps[bi];
                    double[][] g = { points[first], points[first + 1], points[first + 2], points[first + 3] };
                    string basis = bases[bi];
                    CheckSegment(bases[bi] + " segment " + s, shapes, t => Evaluate(basis, g, t), widths[s], widths[s + 1]);
                }
            }
        }

        [TEST("Curves", "PeriodicAndSeveralCurves")]
        public static void PeriodicAndSeveralCurves()
        {
            // a closed b-spline loop of 6 vertices (6 segments, the last ones wrap)
            double[][] loop = new double[6][];
            for (int i = 0; i < 6; ++i)
            {
                loop[i] = new double[] { 2.5 * Math.Cos(2 * Math.PI * i / 6), 2.5 * Math.Sin(2 * Math.PI * i / 6), 0 };
            }
            List<ParsedParameter> single = Parameters(new int[] { 6 }, loop, "cubic", "periodic", "b-spline", 1, new float[] { 0.1f, 0.12f, 0.14f, 0.16f, 0.18f, 0.2f });
            pstd.vector<Shape> shapes = Make(single);
            EXPECT_EQ(6 * 8, (int)shapes.size(), "periodic segments");
            for (int s = 0; s < 6; ++s)
            {
                double[][] g = { loop[s], loop[(s + 1) % 6], loop[(s + 2) % 6], loop[(s + 3) % 6] };
                CheckSegment("periodic segment " + s, shapes, t => Evaluate("b-spline", g, t), 0.1 + 0.02 * s, 0.1 + 0.02 * ((s + 1) % 6));
            }
            // several nonperiodic curves in one shape: the counts add up and the second curve takes its own points and widths
            double[][] first = Wave(4, 0, 0, 1.0, 0.5, 0), second = Wave(7, 0, 5, 1.0, 0.5, 2);
            List<double[]> both = new List<double[]>(first);
            both.AddRange(second);
            List<ParsedParameter> list = Parameters(new int[] { 4, 7 }, both.ToArray(), "cubic", "nonperiodic", "bezier", 3, new float[] { 0.2f, 0.2f, 0.1f, 0.2f, 0.3f });
            shapes = Make(list);
            EXPECT_EQ((1 + 2) * 8, (int)shapes.size(), "two curves");
            CheckSegment("second curve, segment 1", shapes, t => Evaluate("bezier", new double[][] { second[3], second[4], second[5], second[6] }, t), 0.2, 0.3);
        }

        [TEST("Curves", "LinearCurves")]
        public static void LinearCurves()
        {
            double[][] points = { new double[] { 0, 0, 0 }, new double[] { 2, 1, 0 }, new double[] { 3, -1, 0 }, new double[] { 5, 0.5, 0 } };
            float[] widths = { 0.1f, 0.3f, 0.2f, 0.4f };
            pstd.vector<Shape> shapes = Make(Parameters(new int[] { 4 }, points, "linear", "nonperiodic", "bezier", 0, widths));
            EXPECT_EQ(3 * 8, (int)shapes.size());
            for (int s = 0; s < 3; ++s)
            {
                double[] a = points[s], b = points[s + 1];
                CheckSegment("linear segment " + s, shapes, t => new double[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, 0 }, widths[s], widths[s + 1]);
            }
            // periodic: the closing segment from the last vertex to the first (a rectangle, so that no segment is near another)
            double[][] square = { new double[] { 0, 0, 0 }, new double[] { 4, 0, 0 }, new double[] { 4, 3, 0 }, new double[] { 0, 3, 0 } };
            float[] squareWidths = { 0.2f, 0.1f, 0.2f, 0.3f };
            shapes = Make(Parameters(new int[] { 4 }, square, "linear", "periodic", "bezier", 0, squareWidths));
            EXPECT_EQ(4 * 8, (int)shapes.size());
            double[] la = square[3], lb = square[0];
            CheckSegment("closing segment", shapes, t => new double[] { la[0] + (lb[0] - la[0]) * t, la[1] + (lb[1] - la[1]) * t, 0 }, squareWidths[3], squareWidths[0]);
        }

        [TEST("Curves", "RibbonWithNormals")]
        public static void RibbonWithNormals()
        {
            double[][] points = Wave(4, 0, 0, 1.5, 0.6, 0);
            List<ParsedParameter> list = Parameters(new int[] { 4 }, points, "cubic", "nonperiodic", "bezier", 3, new float[] { 0.3f, 0.3f });
            ParsedParameter n = Param("normal", "N");
            foreach (float v in new float[] { 0, 0, 1, 0, 0, 1 })
            {
                n.floats.push_back(v);
            }
            list.Add(n);
            pstd.vector<Shape> shapes = Make(list);
            EXPECT_EQ(8, (int)shapes.size());
            double[] c = Evaluate("bezier", points, 0.5);
            Ray above = new Ray(new Point3f((Float)c[0], (Float)c[1], 3), new Vector3f(0, 0, -1));
            bool hit = false;
            for (int i = 0; i < (int)shapes.size(); ++i)
            {
                hit |= shapes[i].IntersectP(above);
            }
            EXPECT_TRUE(hit, "ribbon from above");
        }
    }
}
