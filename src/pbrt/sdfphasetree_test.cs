// (not in pbrt-v4) sdfphasetree_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class SdfPhaseTreeTests
    {
        private const int Points = 40000;

        private static SdfVector Random(ref RNG rng, double extent)
        {
            return new SdfVector(extent * (2 * rng.Uniform<double>() - 1), extent * (2 * rng.Uniform<double>() - 1),
                                 extent * (2 * rng.Uniform<double>() - 1));
        }

        private static SdfNode Parse(string expression, bool legacy)
        {
            SdfParseContext context = new SdfParseContext();
            context.legacyPhases = legacy;
            context.quiet = true;
            return SdfParser.Parse(expression, context);
        }

        // The number of random points at which two fields are not on the same side of their surfaces
        private static int Disagreements(string first, string second)
        {
            SdfNode a = Parse(first, false), b = Parse(second, false);
            RNG rng = new RNG(53);
            int different = 0;
            for (int i = 0; i < Points; ++i)
            {
                SdfVector p = Random(ref rng, 1.5);
                double va = a.Evaluate(p), vb = b.Evaluate(p);
                different += Math.Abs(va) > 1e-9 && Math.Abs(vb) > 1e-9 && (va < 0) != (vb < 0) ? 1 : 0;
            }
            if (different > 0)
            {
                Console.WriteLine("{0} against {1}: {2} points on different sides", first, second, different);
            }
            return different;
        }

        // A bundle has the properties of a multiphase field: at most one phase is negative at a point, a point of the solid is
        // in a phase and a point outside it in none, the label of a point inside is its phase, every phase is a bound of a
        // distance, and the compiled solid and phases have the values of the nodes
        private static void CheckProperties(string expression)
        {
            // A repeat caps its value by the distance to the boxes of the copies it does not look at, and which those are
            // changes from cell to cell: its field is a bound of the distance (sdfrepeat_test.cs) that is not continuous far
            // from the copies, with or without phases
            bool slopes = !expression.Contains("repeat(");
            SdfNode node = Parse(expression, false);
            CHECK(node is SdfPhaseTree);
            SdfPhaseTree tree = (SdfPhaseTree)node;
            SdfNode[] phases = new SdfNode[tree.Count()], compiled = new SdfNode[tree.Count()];
            for (int k = 0; k < phases.Length; ++k)
            {
                phases[k] = tree.Phase(k);
                compiled[k] = phases[k] == null ? null : SdfCompiledNode.Compile(phases[k]);
            }
            SdfNode solid = SdfCompiledNode.Compile(tree);
            RNG rng = new RNG(51);
            int overlaps = 0, steep = 0, wrongPhase = 0, wrongSolid = 0, inside = 0, compiledDiffers = 0;
            for (int i = 0; i < Points; ++i)
            {
                SdfVector p = Random(ref rng, 1.5), q = p + Random(ref rng, 0.02);
                int negative = 0, which = -1;
                bool near = false;
                for (int k = 0; k < phases.Length; ++k)
                {
                    if (phases[k] == null)
                    {
                        continue;
                    }
                    double value = phases[k].Evaluate(p);
                    near |= Math.Abs(value) < 1e-9;
                    if (value < 0)
                    {
                        ++negative;
                        which = k;
                    }
                    steep += slopes && Math.Abs(value - phases[k].Evaluate(q)) > phases[k].Lipschitz() * (p - q).Length() * (1 + 1e-9) + 1e-12 ? 1 : 0;
                    compiledDiffers += Math.Abs(compiled[k].Evaluate(p) - value) > 1e-12 * Math.Max(1.0, Math.Abs(value)) ? 1 : 0;
                }
                double whole = tree.Evaluate(p);
                compiledDiffers += Math.Abs(solid.Evaluate(p) - whole) > 1e-12 * Math.Max(1.0, Math.Abs(whole)) ? 1 : 0;
                if (near || Math.Abs(whole) < 1e-9)
                {
                    continue;
                }
                overlaps += negative > 1 ? 1 : 0;
                wrongSolid += (whole < 0) != (negative > 0) ? 1 : 0;
                if (negative == 1)
                {
                    ++inside;
                    wrongPhase += tree.PhaseAt(p) != which ? 1 : 0;
                }
            }
            if (overlaps > 0 || steep > 0 || wrongPhase > 0 || wrongSolid > 0 || compiledDiffers > 0 || inside <= 200)
            {
                Console.WriteLine("{0}: {1} points in two phases, {2} values that change too fast, {3} wrong labels, {4} points of the solid in no phase or the other way round, {5} compiled values differ, {6} points inside",
                                  expression, overlaps, steep, wrongPhase, wrongSolid, compiledDiffers, inside);
            }
            CHECK_GT(inside, 200);
            CHECK_EQ(overlaps, 0);
            CHECK_EQ(steep, 0);
            CHECK_EQ(wrongPhase, 0);
            CHECK_EQ(wrongSolid, 0);
            CHECK_EQ(compiledDiffers, 0);
        }

        // The bundle against the nodes of sdfphases.cs for the same expression: the same solid and the same phase everywhere
        private static void CheckSameAsNodes(string expression)
        {
            SdfPhaseNode old = (SdfPhaseNode)Parse(expression, true);
            SdfPhaseTree tree = (SdfPhaseTree)Parse(expression, false);
            Span<double> values = stackalloc double[SdfPhaseNode.MaxPhases];
            RNG rng = new RNG(59);
            int different = 0, inside = 0;
            for (int i = 0; i < Points; ++i)
            {
                SdfVector p = Random(ref rng, 1.5);
                old.Phases(p, values);
                bool near = false;
                for (int k = 0; k < SdfPhaseNode.MaxPhases; ++k)
                {
                    SdfNode phase = tree.Phase(k);
                    double value = phase == null ? double.PositiveInfinity : phase.Evaluate(p);
                    near |= Math.Abs(value) < 1e-9 || Math.Abs(values[k]) < 1e-9;
                }
                if (near)
                {
                    continue;
                }
                for (int k = 0; k < SdfPhaseNode.MaxPhases; ++k)
                {
                    SdfNode phase = tree.Phase(k);
                    bool isIn = phase != null && phase.Evaluate(p) < 0;
                    different += isIn != (values[k] < 0) ? 1 : 0;
                    inside += isIn ? 1 : 0;
                }
            }
            if (different > 0)
            {
                Console.WriteLine("{0}: {1} points in a different phase than with the nodes of sdfphases.cs", expression, different);
            }
            CHECK_GT(inside, 200);
            CHECK_EQ(different, 0);
        }

        [TEST("SdfPhaseTree", "SameAsNodes")]
        public static void SdfPhaseTree_SameAsNodes()
        {
            string ball = "sphere(0.7)", block = "translate(box(0.5, 0.4, 0.4), 0.5, 0.1, 0)", ring = "translate(torus(0.6, 0.2), 0, 0.3, 0)";
            string two = "under(phase(" + ball + ", 0), phase(" + block + ", 1))";
            string three = "over(" + two + ", phase(" + ring + ", 2))";
            string[] trees = {
                "phase(" + ball + ", 3)", two, "over(phase(" + ball + ", 0), phase(" + block + ", 1))", three,
                "under(" + two + ", phase(" + ring + ", 0))",
                "contact(" + ball + ", " + block + ", 0, 1)", "contact(" + ball + ", " + block + ", 0, 1, 0.2)",
                "contact(" + ball + ", " + block + ", 0, 1, 0.8)", "contactsmooth(" + ball + ", " + block + ", 0, 1, 0.2)",
                "layer(" + two + ", 2, 0.15)", "layer(layer(" + three + ", 3, 0.1), 4, 0.1)", "layer(" + two + ", 1, 0.1)",
                "intersect(" + three + ", plane(0, 0, 1, 0))", "subtract(" + three + ", translate(sphere(0.6), 0.6, 0.6, 0.6))",
                three + " - translate(sphere(0.6), 0.6, 0.6, 0.6)", three + " & box(1, 0.5, 1)",
                "change(" + two + ", translate(sphere(0.4), -0.4, 0.3, 0), 0, 5)", "change(" + two + ", translate(sphere(0.4), -0.4, 0.3, 0), 0, 1)",
                "remove(" + three + ", 1)", "shrink(" + three + ", 0.03)",
                "intersect(checker(0, 1, 0.4), " + ball + ")", "under(" + two + ", intersect(checker(2, 3, 0.3), " + ring + "))" };
            foreach (string tree in trees)
            {
                CheckSameAsNodes(tree);
                CheckProperties(tree);
            }
        }

        [TEST("SdfPhaseTree", "Properties")]
        public static void SdfPhaseTree_Properties()
        {
            string ball = "sphere(0.7)", block = "translate(box(0.5, 0.4, 0.4), 0.5, 0.1, 0)", ring = "translate(torus(0.6, 0.2), 0, 0.3, 0)";
            string two = "under(phase(" + ball + ", 0), phase(" + block + ", 1))";
            string other = "contact(" + ring + ", translate(sphere(0.4), -0.5, -0.4, 0.3), 2, 3)";
            string knob = "translate(sphere(0.3), -0.6, 0.5, 0.2)";
            string[] trees = {
                "translate(" + two + ", 0.2, -0.1, 0.1)", "rotate(" + two + ", 20, 30, 40)", "scale(" + two + ", 0.8)",
                "twist(" + two + ", 1.2)", "bend(" + two + ", 0.6)", "mirror(" + two + ", \"x\")", "elongate(" + two + ", 0.1, 0.2, 0)",
                "repeat(scale(" + two + ", 0.3), 1, 1, 0, 1)", "repeat(scale(" + two + ", 0.3), 1, 1, 0, 1, 0.1, 3)",
                "offset(" + two + ", 0.1)", "offset(" + two + ", -0.1)", "shell(" + two + ", 0.05)", "displace(" + two + ", 0.05, 4, 3)",
                "union(" + two + ", " + other + ")", "intersect(" + two + ", " + other + ")", "subtract(" + two + ", " + other + ")",
                "xor(" + two + ", " + other + ")", two + " ^ " + other, two + " | " + other,
                "union(" + two + ", " + knob + ")", "union(" + knob + ", " + two + ")", "xor(" + two + ", " + knob + ")",
                "intersect(translate(sphere(0.8), -0.2, 0.2, 0), " + two + ")",
                "smoothunion(" + two + ", " + other + ", 0.15)", "smoothunion(" + two + ", " + other + ", 0.15, \"cubic\")",
                "smoothunion(" + two + ", " + knob + ", 0.15)", "smoothunion(" + knob + ", " + two + ", 0.15, \"exp\")",
                "smoothintersect(" + two + ", box(1, 0.5, 1), 0.1)", "smoothintersect(" + two + ", " + other + ", 0.1)",
                "smoothsubtract(" + two + ", " + knob + ", 0.1)", "smoothsubtract(" + two + ", " + other + ", 0.1, \"cubic\")",
                "contactsmooth(" + ball + ", " + block + ", 0, 1, 0.2, 0.5, \"cubic\")",
                // One after the other, and after an operator that leaves the phases to the smallest field
                "under(smoothunion(" + two + ", " + knob + ", 0.15), " + other + ")",
                "layer(offset(smoothunion(" + two + ", " + other + ", 0.1), 0.05), 9, 0.1)",
                "twist(smoothunion(smoothunion(" + two + ", " + knob + ", 0.1), " + other + ", 0.1), 0.8)",
                "remove(displace(" + two + ", 0.05, 4, 3), 0)", "change(offset(" + two + ", 0.1), " + knob + ", 0, 4)",
                "shrink(smoothunion(" + two + ", " + other + ", 0.1), 0.03)", "subtract(xor(" + two + ", " + other + "), " + knob + ")",
                // More phases than the nodes of sdfphases.cs have room for
                "under(under(" + two + ", phase(" + ring + ", 20)), phase(" + knob + ", 300))" };
            foreach (string tree in trees)
            {
                CheckProperties(tree);
            }
        }

        // What each of the functions that the nodes of sdfphases.cs did not have is meant to build
        [TEST("SdfPhaseTree", "Functions")]
        public static void SdfPhaseTree_Functions()
        {
            string ball = "sphere(0.7)", block = "translate(box(0.5, 0.4, 0.4), 0.5, 0.1, 0)", ring = "translate(torus(0.6, 0.2), 0, 0.3, 0)";
            string a = "under(phase(" + ball + ", 0), phase(" + block + ", 1))";
            string b = "contact(" + ring + ", translate(sphere(0.4), -0.5, -0.4, 0.3), 0, 3)";
            string knob = "translate(sphere(0.3), -0.6, 0.5, 0.2)";
            // (the comparison sees a difference when there is one)
            CHECK_GT(Disagreements("merge(twist(" + a + ", 1.2), 1)", "merge(" + a + ", 1)"), 200);
            // A function that moves or warps does to each phase what it does to an ordinary field
            string[] moves = { "translate(X, 0.2, -0.1, 0.1)", "rotate(X, 20, 30, 40)", "rotateaxis(X, 1, 1, 0, 35)", "scale(X, 0.8)", "twist(X, 1.2)",
                               "bend(X, 0.6)", "mirror(X, \"x\")", "elongate(X, 0.1, 0.2, 0)", "repeat(scale(X, 0.3), 1, 1, 0, 1, 0.1, 3)" };
            foreach (string move in moves)
            {
                for (int phase = 0; phase < 2; ++phase)
                {
                    CHECK_EQ(Disagreements("merge(" + move.Replace("X", a) + ", " + phase + ")", move.Replace("X", "merge(" + a + ", " + phase + ")")), 0);
                }
                CHECK_EQ(Disagreements("merge(" + move.Replace("X", a) + ")", move.Replace("X", "merge(" + a + ")")), 0);
            }
            // The union of two multiphase fields is under
            CHECK_EQ(Disagreements("merge(union(" + a + ", " + b + "), 0)", "merge(under(" + a + ", " + b + "), 0)"), 0);
            CHECK_EQ(Disagreements("merge(union(" + a + ", " + b + "), 3)", "subtract(merge(" + b + ", 3), merge(" + a + "))"), 0);
            // An intersection or a subtraction keeps the phases of the first inside or outside the solid of the second
            CHECK_EQ(Disagreements("merge(intersect(" + a + ", " + b + "), 1)", "intersect(merge(" + a + ", 1), merge(" + b + "))"), 0);
            CHECK_EQ(Disagreements("merge(subtract(" + a + ", " + b + "), 0)", "subtract(merge(" + a + ", 0), merge(" + b + "))"), 0);
            CHECK_EQ(Disagreements("merge(intersect(" + knob + ", " + a + "), 0)", "intersect(merge(" + a + ", 0), " + knob + ")"), 0);
            // An ordinary field less a multiphase one is ordinary
            CHECK(!(Parse("subtract(" + knob + ", " + a + ")", false) is SdfPhaseTree));
            CHECK_EQ(Disagreements("subtract(" + ball + ", " + b + ")", "subtract(" + ball + ", merge(" + b + "))"), 0);
            // xor: each one's phases outside the other
            CHECK_EQ(Disagreements("merge(xor(" + a + ", " + b + "))", "xor(merge(" + a + "), merge(" + b + "))"), 0);
            CHECK_EQ(Disagreements("merge(xor(" + a + ", " + b + "), 0)",
                                   "union(subtract(merge(" + a + ", 0), merge(" + b + ")), subtract(merge(" + b + ", 0), merge(" + a + ")))"), 0);
            CHECK_EQ(Disagreements("merge(xor(" + a + ", " + knob + "))", "xor(merge(" + a + "), " + knob + ")"), 0);
            // A blend of two multiphase fields is the blend of their solids, and each phase is what it was inside them
            CHECK_EQ(Disagreements("merge(smoothunion(" + a + ", " + b + ", 0.15))", "smoothunion(merge(" + a + "), merge(" + b + "), 0.15)"), 0);
            for (int phase = 0; phase < 4; phase += phase == 1 ? 2 : 1)
            {
                CHECK_EQ(Disagreements("intersect(merge(smoothunion(" + a + ", " + b + ", 0.15), " + phase + "), merge(under(" + a + ", " + b + ")))",
                                       "merge(under(" + a + ", " + b + "), " + phase + ")"), 0);
            }
            // An ordinary field that joins a multiphase one, and what an offset adds, leave the phases as they were inside
            CHECK_EQ(Disagreements("merge(union(" + a + ", " + knob + "))", "union(merge(" + a + "), " + knob + ")"), 0);
            CHECK_EQ(Disagreements("merge(smoothunion(" + knob + ", " + a + ", 0.1))", "smoothunion(" + knob + ", merge(" + a + "), 0.1)"), 0);
            CHECK_EQ(Disagreements("merge(offset(" + a + ", 0.1))", "offset(merge(" + a + "), 0.1)"), 0);
            CHECK_EQ(Disagreements("merge(displace(" + a + ", 0.05, 4, 3))", "displace(merge(" + a + "), 0.05, 4, 3)"), 0);
            for (int phase = 0; phase < 2; ++phase)
            {
                CHECK_EQ(Disagreements("intersect(merge(union(" + a + ", " + knob + "), " + phase + "), merge(" + a + "))", "merge(" + a + ", " + phase + ")"), 0);
                CHECK_EQ(Disagreements("intersect(merge(offset(" + a + ", 0.1), " + phase + "), merge(" + a + "))", "merge(" + a + ", " + phase + ")"), 0);
            }
            // The smooth cuts cut the solid as they cut an ordinary field, and what is left keeps its phases
            CHECK_EQ(Disagreements("merge(smoothintersect(" + a + ", " + knob + ", 0.1))", "smoothintersect(merge(" + a + "), " + knob + ", 0.1)"), 0);
            CHECK_EQ(Disagreements("merge(smoothsubtract(" + a + ", " + knob + ", 0.1, \"cubic\"))", "smoothsubtract(merge(" + a + "), " + knob + ", 0.1, \"cubic\")"), 0);
            CHECK_EQ(Disagreements("merge(smoothsubtract(" + a + ", " + knob + ", 0.1), 0)", "intersect(smoothsubtract(merge(" + a + "), " + knob + ", 0.1), merge(" + a + ", 0))"), 0);
            // Any number of phases
            CHECK_EQ(Disagreements("merge(under(" + a + ", phase(" + ring + ", 300)), 300)", "subtract(" + ring + ", merge(" + a + "))"), 0);
            // The phase of a point is its label, and the solid has a box
            SdfPhaseTree tree = (SdfPhaseTree)Parse("twist(" + a + ", 0.5)", false);
            SdfWeights weights = new SdfWeights();
            tree.Weights(new SdfVector(0.9, 0.1, 0), weights, 1);
            CHECK_EQ(weights.Get(1), 1);
            CHECK(Parse(a, false).Bounds(out SdfVector lower, out SdfVector upper, out bool grows));
            CHECK(lower.x <= -0.7 && upper.x >= 1.0 && grows);
        }

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

        // The whole field and one of its phases as shapes: with proxies, the grid and the fields of the cells they are hit by the
        // same rays at the same distances as without, and the cells and the proxies have something to take away
        private static void CheckAccelerated(string expression)
        {
            SdfNode plain = Parse(expression, false);
            SdfNode proxied = plain.WithProxies(6, 0);
            Point3f lower = new Point3f(-1.6f, -1.6f, -1.6f), upper = new Point3f(1.6f, 1.6f, 1.6f);
            SdfShapeOptions cells = new SdfShapeOptions();
            cells.ddaResolution = 48;
            cells.prune = true;
            SdfShape first = new SdfShape(new Transform(), new Transform(), false, SdfCompiledNode.Compile(plain), lower, upper, new SdfShapeOptions());
            SdfShape second = new SdfShape(new Transform(), new Transform(), false, SdfCompiledNode.Compile(proxied), lower, upper, cells);
            RNG rng = new RNG(61);
            int hits = 0, different = 0, apart = 0;
            for (int i = 0; i < 30000; ++i)
            {
                Vector3f from = Normalize(new Vector3f((Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1)));
                Point3f target = new Point3f((Float)(1.4 * (2 * rng.Uniform<double>() - 1)), (Float)(1.4 * (2 * rng.Uniform<double>() - 1)), (Float)(1.4 * (2 * rng.Uniform<double>() - 1)));
                Point3f o = new Point3f(5 * from.x, 5 * from.y, 5 * from.z);
                Ray ray = new Ray(o, Normalize(target - o));
                pstd.optional<ShapeIntersection> ha = first.Intersect(ray), hb = second.Intersect(ray);
                if (ha.has_value() != hb.has_value())
                {
                    ++different;
                }
                else if (ha.has_value())
                {
                    ++hits;
                    apart += Math.Abs(ha.value().tHit - hb.value().tHit) > 1e-5 ? 1 : 0;
                }
            }
            Console.WriteLine("{0}...: {1} nodes, {2} proxies, {3} fields of cells; {4} hits, {5} hit by one only, {6} hits apart",
                              expression.Substring(0, Math.Min(60, expression.Length)), plain.NodeCount(), proxied.NodeCount() - plain.NodeCount(),
                              second.PrunedFields(), hits, different, apart);
            CHECK_GT(hits, 500);
            CHECK_GT(proxied.NodeCount(), plain.NodeCount());
            CHECK_GT(second.PrunedFields(), 5);
            CHECK_EQ(different, 0);
            CHECK_EQ(apart, 0);
        }

        [TEST("SdfPhaseTree", "Accelerated")]
        public static void SdfPhaseTree_Accelerated()
        {
            RNG rng = new RNG(63);
            string a = Cluster(ref rng, -0.6, -0.4, 0, 8), b = Cluster(ref rng, 0.5, -0.3, 0.3, 8), c = Cluster(ref rng, 0, 0.5, -0.4, 8);
            string d = Cluster(ref rng, -0.3, 0.2, 0.5, 8);
            string tree = "under(under(under(phase(" + a + ", 0), phase(" + b + ", 1)), phase(" + c + ", 2)), phase(" + d + ", 1))";
            CheckAccelerated(tree);
            CheckAccelerated("merge(" + tree + ", 1)");
            CheckAccelerated("merge(" + tree + ", 2)");
            CheckAccelerated("rotate(smoothunion(layer(" + tree + ", 3, 0.05), translate(sphere(0.3), 0.8, 0.8, 0), 0.1), 10, 20, 30)");
            CheckAccelerated("merge(translate(smoothunion(" + tree + ", translate(sphere(0.3), 0.8, 0.8, 0), 0.1), 0.1, 0, -0.1), 0)");
        }
    }
}
