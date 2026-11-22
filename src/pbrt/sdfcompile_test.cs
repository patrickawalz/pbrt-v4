// (not in pbrt-v4) sdfcompile_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class SdfCompileTests
    {
        private static string Parts(int count, string blend, bool turned)
        {
            RNG rng = new RNG(71);
            string field = "";
            for (int i = 0; i < count; ++i)
            {
                double angle = 2 * Math.PI * i / count;
                double x = Math.Cos(angle), y = 0.25 * Math.Sin(5 * angle), z = Math.Sin(angle);
                string part = i % 3 == 0 ? "torus(0.12, 0.04)" : (i % 3 == 1 ? "sphere(0.1)" : "box(0.08, 0.06, 0.1, 0.01)");
                if (turned)
                {
                    part = "rotate(" + part + ", " + (360 * rng.Uniform<double>()).ToString("F1") + ", " + (360 * rng.Uniform<double>()).ToString("F1") + ", 0)";
                }
                part = "translate(" + part + ", " + x.ToString("F3") + ", " + y.ToString("F3") + ", " + z.ToString("F3") + ")";
                field = i == 0 ? part : blend.Replace("A", field).Replace("B", part);
            }
            return field;
        }

        private static string[] Names()
        {
            return new string[] { "200 moved parts in a union", "36 moved parts blended", "40 turned and moved parts in a union", "6 moved parts blended (a cell)",
                                  "6 turned and moved parts in a union (a cell)", "a twisted box", "two phases under each other, turned",
                                  "moved twice and turned twice", "noise over 36 blended parts", "a mix of two fields by a third",
                                  "a repeat of an off-centre blend", "ten primitives without a compiled form in a union", "a morph of moving parts",
                                  "6 moved parts in a union (a cell)", "40 moved parts in a union" };
        }

        private static string[] Fields()
        {
            return new string[] { Parts(200, "union(A, B)", false), Parts(36, "smoothunion(A, B, 0.08)", false), Parts(40, "union(A, B)", true),
                                  Parts(6, "smoothunion(A, B, 0.08)", false), Parts(6, "union(A, B)", true), "twist(box(0.5, 1.0, 0.25, 0.05), 1.2)",
                                  "rotate(under(phase(sphere(0.7), 0), phase(translate(box(0.5, 0.4, 0.4), 0.5, 0.1, 0), 1)), 20, 30, 40)",
                                  "translate(rotate(translate(rotate(smoothunion(torus(0.5, 0.2), box(0.3, 0.3, 0.3), 0.1), 90, 0, 0), 0.2, 0, 0), 0, 0, 37), 0, 0.1, 0)",
                                  "displace(" + Parts(36, "smoothunion(A, B, 0.08)", false) + ", 0.02, 6, 3)",
                                  "mix(" + Parts(6, "smoothunion(A, B, 0.08)", false) + ", " + Parts(6, "union(A, B)", true) + ", translate(sphere(0.8), 0.3, 0, 0))",
                                  "repeat(translate(smoothunion(torus(0.12, 0.04), rotate(box(0.1, 0.03, 0.03), 0, 30, 20), 0.03), 0.1, 0.05, 0), 0.4, 0.4, 0.4, 2, 0.05, 1)",
                                  "union(union(union(union(union(union(union(union(union(translate(cone(0.2, 0.3), -1, 0, 0), translate(ellipsoid(0.2, 0.1, 0.15), -0.6, 0, 0)), " +
                                  "translate(octahedron(0.2), -0.2, 0, 0)), translate(hexprism(0.15, 0.1), 0.2, 0, 0)), translate(roundcone(0.1, 0.05, 0.2), 0.6, 0, 0)), " +
                                  "translate(cappedcone(0.15, 0.15, 0.05), 1, 0, 0)), translate(boxframe(0.2, 0.15, 0.1, 0.02), 0, 0, 0.6)), translate(link(0.1, 0.1, 0.03), 0.5, 0, 0.6)), " +
                                  "translate(solidangle(30, 0.25), -0.5, 0, 0.6)), translate(rhombus(0.2, 0.1, 0.05, 0.02), 0, 0, -0.6))",
                                  "morph(translate_t(" + Parts(6, "smoothunion(A, B, 0.08)", false) + ", 0.2, 0.1, 0), displace_t(sphere(0.8), 0.03, 4, 1))",
                                  Parts(6, "union(A, B)", false), Parts(40, "union(A, B)", false) };
        }

        // The compiled form has the values of the tree that is walked
        [TEST("SdfCompile", "SameValues")]
        public static void SdfCompile_SameValues()
        {
            string[] names = Names(), fields = Fields();
            SdfTime.Current = 0.3;
            for (int f = 0; f < fields.Length; ++f)
            {
                SdfNode walked = SdfParser.Parse(fields[f], new SdfParseContext());
                SdfNode compiled = SdfCompiledNode.Compile(walked);
                CHECK(compiled is SdfCompiledNode);
                RNG rng = new RNG(73);
                int different = 0;
                double worst = 0;
                for (int i = 0; i < 100000; ++i)
                {
                    SdfVector p = new SdfVector(1.3 * (2 * rng.Uniform<double>() - 1), 1.3 * (2 * rng.Uniform<double>() - 1), 1.3 * (2 * rng.Uniform<double>() - 1));
                    double expected = walked.Evaluate(p), value = compiled.Evaluate(p);
                    worst = Math.Max(worst, Math.Abs(expected - value));
                    different += Math.Abs(expected - value) > 1e-12 * Math.Max(1.0, Math.Abs(expected)) ? 1 : 0;
                }
                Console.WriteLine("{0}: largest difference between the compiled and the walked value {1:E1}", names[f], worst);
                CHECK_EQ(different, 0);
            }
        }

        // The gradient from the compiled function against the central difference of the field. The two differ where the
        // difference reaches across a crease (the border between two parts of a union), so a few points may; and what a
        // gradient costs each way (not a check)
        [TEST("SdfCompile", "Gradient")]
        public static void SdfCompile_Gradient()
        {
            string[] names = Names(), fields = Fields();
            SdfTime.Current = 0.3;
            for (int f = 0; f < fields.Length; ++f)
            {
                SdfNode walked = SdfParser.Parse(fields[f], new SdfParseContext());
                SdfNode compiled = SdfCompiledNode.Compile(walked);
                SdfGradientFunction function = SdfGradientCompiler.Compile(walked, 1e-6);
                if (function == null)
                {
                    // A field without a blend keeps the gradients of its nodes
                    CHECK(!fields[f].Contains("smooth") && !fields[f].Contains("mix("));
                    continue;
                }
                RNG rng = new RNG(77);
                int count = 100000, apart = 0, wrongValue = 0;
                SdfVector[] points = new SdfVector[count];
                double worst = 0;
                for (int i = 0; i < count; ++i)
                {
                    points[i] = new SdfVector(1.3 * (2 * rng.Uniform<double>() - 1), 0.5 * (2 * rng.Uniform<double>() - 1), 1.3 * (2 * rng.Uniform<double>() - 1));
                    double value = function(points[i].x, points[i].y, points[i].z, out double gx, out double gy, out double gz);
                    SdfVector expected = walked.Gradient(points[i], 1e-6);
                    double difference = (new SdfVector(gx, gy, gz) - expected).Length();
                    wrongValue += Math.Abs(value - walked.Evaluate(points[i])) > 1e-12 * Math.Max(1.0, Math.Abs(value)) ? 1 : 0;
                    if (!(difference <= 1e-4))
                    {
                        ++apart;
                    }
                    else
                    {
                        worst = Math.Max(worst, difference);
                    }
                }
                double[] best = { double.MaxValue, double.MaxValue };
                double sum = 0;
                for (int round = 0; round < 5; ++round)
                {
                    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < count; ++i)
                    {
                        sum += compiled.Gradient(points[i], 1e-6).x;
                    }
                    best[0] = Math.Min(best[0], watch.Elapsed.TotalSeconds * 1e9 / count);
                    watch.Restart();
                    for (int i = 0; i < count; ++i)
                    {
                        sum += function(points[i].x, points[i].y, points[i].z, out double gx, out double gy, out double gz) + gx;
                    }
                    best[1] = Math.Min(best[1], watch.Elapsed.TotalSeconds * 1e9 / count);
                }
                Console.WriteLine("{0}: {1} of {2} gradients more than 1e-4 from the difference (the others within {3:E1}); {4:F0} ns by four values, {5:F0} ns by the one function [{6:F2}]",
                                  names[f], apart, count, worst, best[0], best[1], sum);
                CHECK_EQ(wrongValue, 0);
                CHECK_LT(apart, count / 50);
            }
        }

        // Not a check: what a value costs walked and compiled
        [TEST("SdfCompile", "Speed")]
        public static void SdfCompile_Speed()
        {
            string[] names = Names(), fields = Fields();
            SdfTime.Current = 0.3;
            int count = 400000;
            SdfVector[] points = new SdfVector[count];
            RNG rng = new RNG(75);
            for (int i = 0; i < count; ++i)
            {
                points[i] = new SdfVector(1.3 * (2 * rng.Uniform<double>() - 1), 0.5 * (2 * rng.Uniform<double>() - 1), 1.3 * (2 * rng.Uniform<double>() - 1));
            }
            for (int f = 0; f < fields.Length; ++f)
            {
                SdfNode walked = SdfParser.Parse(fields[f], new SdfParseContext());
                SdfNode compiled = SdfCompiledNode.Compile(walked);
                SdfNode[] forms = { walked, compiled };
                double[] best = { double.MaxValue, double.MaxValue };
                double sum = 0;
                for (int round = 0; round < 7; ++round)
                {
                    for (int which = 0; which < 2; ++which)
                    {
                        SdfNode field = forms[which];
                        int used = which == 0 && f == 0 ? count / 8 : count;
                        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 0; i < used; ++i)
                        {
                            sum += field.Evaluate(points[i]);
                        }
                        best[which] = Math.Min(best[which], watch.Elapsed.TotalSeconds * 1e9 / used);
                    }
                }
                Console.WriteLine("{0}: {1:F0} ns walked, {2:F0} ns compiled ({3:F2} times faster) [{4:F3}]", names[f], best[0], best[1], best[0] / best[1], sum);
            }
        }
    }
}
