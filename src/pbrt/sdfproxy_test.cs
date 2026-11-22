// (not in pbrt-v4) sdfproxy_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class SdfProxyTests
    {
        // A cluster of blended parts about a center
        private static string Cluster(ref RNG rng, double cx, double cy, double cz, int parts)
        {
            string cluster = "";
            for (int i = 0; i < parts; ++i)
            {
                double x = cx + 0.25 * (2 * rng.Uniform<double>() - 1), y = cy + 0.25 * (2 * rng.Uniform<double>() - 1), z = cz + 0.25 * (2 * rng.Uniform<double>() - 1);
                string part = "translate(" + (i % 2 == 0 ? "sphere(0.1)" : "torus(0.12, 0.04)") + ", " + x.ToString("F3") + ", " + y.ToString("F3") + ", " + z.ToString("F3") + ")";
                cluster = i == 0 ? part : "smoothunion(" + cluster + ", " + part + ", 0.06)";
            }
            return cluster;
        }

        // The field with proxies is the same solid, is never above the field without them outside the solid (it stays a bound
        // of the distance), compiles to the same values, and is hit by the same rays
        private static void Compare(string expression, int expectedAtLeast)
        {
            SdfNode plain = SdfParser.Parse(expression, new SdfParseContext());
            SdfNode proxied = plain.WithProxies(6, 0);
            int added = proxied.NodeCount() - plain.NodeCount();
            SdfNode compiled = SdfCompiledNode.Compile(proxied);
            RNG rng = new RNG(91);
            int otherSide = 0, above = 0, compiledDiffers = 0, replaced = 0;
            for (int i = 0; i < 200000; ++i)
            {
                SdfVector p = new SdfVector(1.6 * (2 * rng.Uniform<double>() - 1), 1.6 * (2 * rng.Uniform<double>() - 1), 1.6 * (2 * rng.Uniform<double>() - 1));
                double a = plain.Evaluate(p), b = proxied.Evaluate(p);
                replaced += a != b ? 1 : 0;
                otherSide += Math.Abs(a) > 1e-9 && (a < 0) != (b < 0) ? 1 : 0;
                // The parts are exact distances outside and the proxies give distances to boxes around them
                above += a > 0 && b > a + 1e-9 ? 1 : 0;
                compiledDiffers += Math.Abs(compiled.Evaluate(p) - b) > 1e-12 * Math.Max(1.0, Math.Abs(b)) ? 1 : 0;
            }
            Point3f lower = new Point3f(-1.6f, -1.6f, -1.6f), upper = new Point3f(1.6f, 1.6f, 1.6f);
            SdfShape first = new SdfShape(new Transform(), new Transform(), false, SdfCompiledNode.Compile(plain), lower, upper, new SdfShapeOptions());
            SdfShapeOptions cells = new SdfShapeOptions();
            cells.ddaResolution = 48;
            cells.prune = true;
            SdfShape second = new SdfShape(new Transform(), new Transform(), false, compiled, lower, upper, new SdfShapeOptions());
            SdfShape third = new SdfShape(new Transform(), new Transform(), false, compiled, lower, upper, cells);
            int hits = 0, different = 0, apart = 0;
            for (int i = 0; i < 40000; ++i)
            {
                Vector3f from = Normalize(new Vector3f((Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1)));
                Point3f target = new Point3f((Float)(1.4 * (2 * rng.Uniform<double>() - 1)), (Float)(1.4 * (2 * rng.Uniform<double>() - 1)), (Float)(1.4 * (2 * rng.Uniform<double>() - 1)));
                Point3f o = new Point3f(5 * from.x, 5 * from.y, 5 * from.z);
                Ray ray = new Ray(o, Normalize(target - o));
                pstd.optional<ShapeIntersection> ha = first.Intersect(ray), hb = second.Intersect(ray), hc = third.Intersect(ray);
                if (ha.has_value() != hb.has_value() || ha.has_value() != hc.has_value())
                {
                    ++different;
                }
                else if (ha.has_value())
                {
                    ++hits;
                    apart += Math.Abs(ha.value().tHit - hb.value().tHit) > 1e-5 || Math.Abs(ha.value().tHit - hc.value().tHit) > 1e-5 ? 1 : 0;
                }
            }
            Console.WriteLine("{0}...: {1} proxies; of 200000 points {2} get the box distance, {3} on the other side, {4} above the field, {5} compiled values differ; {6} hits, {7} hit by one only, {8} hits apart",
                              expression.Substring(0, Math.Min(50, expression.Length)), added, replaced, otherSide, above, compiledDiffers, hits, different, apart);
            CHECK_GE(added, expectedAtLeast);
            CHECK_EQ(otherSide, 0);
            CHECK_EQ(above, 0);
            CHECK_EQ(compiledDiffers, 0);
            CHECK_EQ(different, 0);
            CHECK_EQ(apart, 0);
        }

        // Not a check: what a value costs with and without proxies, at points all over the box (as a texture asks for them)
        [TEST("SdfProxy", "Cost")]
        public static void SdfProxy_Cost()
        {
            RNG rng = new RNG(95);
            string all = "";
            for (int c = 0; c < 8; ++c)
            {
                double angle = 2 * Math.PI * c / 8;
                string cluster = Cluster(ref rng, 1.1 * Math.Cos(angle), 0.3 * Math.Sin(2 * angle), 1.1 * Math.Sin(angle), 25);
                all = c == 0 ? cluster : "union(" + all + ", " + cluster + ")";
            }
            SdfNode plain = SdfParser.Parse(all, new SdfParseContext());
            SdfNode[] fields = { SdfCompiledNode.Compile(plain), SdfCompiledNode.Compile(plain.WithProxies(6, 0)) };
            double[] seconds = new double[2];
            for (int f = 0; f < 2; ++f)
            {
                RNG points = new RNG(97);
                double sum = 0;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 2000000; ++i)
                {
                    sum += fields[f].Evaluate(new SdfVector(1.6 * (2 * points.Uniform<double>() - 1), 0.8 * (2 * points.Uniform<double>() - 1),
                                                            1.6 * (2 * points.Uniform<double>() - 1)));
                }
                seconds[f] = watch.Elapsed.TotalSeconds;
            }
            Console.WriteLine("8 clusters of 25 parts, 2 million values: {0:F2} s without proxies, {1:F2} s with ({2:F1} times faster)", seconds[0], seconds[1],
                              seconds[0] / seconds[1]);
        }

        [TEST("SdfProxy", "SameSolid")]
        public static void SdfProxy_SameSolid()
        {
            RNG rng = new RNG(93);
            string a = Cluster(ref rng, -0.8, -0.6, 0, 8), b = Cluster(ref rng, 0.7, -0.5, 0.3, 8), c = Cluster(ref rng, 0, 0.7, -0.5, 8);
            string d = Cluster(ref rng, -0.6, 0.6, 0.6, 8), e = Cluster(ref rng, 0.7, 0.6, 0.6, 8);
            string all = "union(union(union(union(" + a + ", " + b + "), " + c + "), " + d + "), " + e + ")";
            Compare(all, 5);
            Compare("subtract(intersect(" + all + ", box(1.2, 1.0, 1.2)), " + Cluster(ref rng, 0.6, 0.5, 0.5, 8) + ")", 5);
            Compare("rotate(label(union(" + a + ", translate(" + b + ", 0.1, 0.2, 0)), 1), 10, 20, 30)", 2);
            // Below blends: the proxies keep the field out to the widths of the blends above them
            Compare("smoothunion(" + a + ", " + b + ", 0.1)", 2);
            Compare("smoothunion(smoothunion(union(" + a + ", " + c + "), " + b + ", 0.1), union(" + d + ", " + e + "), 0.15)", 4);
            Compare("smoothunion(" + a + ", " + b + ", 0.1, \"cubic\")", 2);
            Compare("smoothintersect(union(" + all + ", sphere(0.3)), box(1.2, 1.0, 1.2), 0.1)", 5);
            Compare("smoothsubtract(smoothunion(" + a + ", translate(" + b + ", -1.2, 0.2, -0.2), 0.1), " + Cluster(ref rng, -0.6, -0.5, 0.1, 8) + ", 0.08)", 2);
            Compare("rotate(smoothunion(intersect(" + a + ", sphere(1.5)), translate(" + d + ", 0, -1, -0.5), 0.12), 10, 20, 30)", 2);
            // No proxy below an offset or a shell
            SdfNode blended = SdfParser.Parse("offset(smoothunion(" + a + ", " + b + ", 0.1), 0.02)", new SdfParseContext());
            CHECK(blended.WithProxies(6, 0) == blended);
        }
    }
}
