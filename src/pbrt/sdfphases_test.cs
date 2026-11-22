// (not in pbrt-v4) sdfphases_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class SdfPhaseTests
    {
        private const int Points = 40000;

        // These tests are of the nodes of sdfphases.cs
        private static SdfParseContext Legacy()
        {
            SdfParseContext context = new SdfParseContext();
            context.legacyPhases = true;
            return context;
        }

        private static SdfVector Random(ref RNG rng, double extent)
        {
            return new SdfVector(extent * (2 * rng.Uniform<double>() - 1), extent * (2 * rng.Uniform<double>() - 1),
                                 extent * (2 * rng.Uniform<double>() - 1));
        }

        // The properties that the paper proves for every multiphase field: at most one phase is negative at a point (the phases
        // do not overlap), every phase is a bound of a distance (it does not change faster than the distance moved), the phase
        // of a point inside is the one that is negative, and the field as an ordinary one is the smallest phase
        private static void CheckProperties(string expression)
        {
            SdfNode node = SdfParser.Parse(expression, Legacy());
            CHECK(node is SdfPhaseNode);
            SdfPhaseNode field = (SdfPhaseNode)node;
            RNG rng = new RNG(51);
            Span<double> values = stackalloc double[SdfPhaseNode.MaxPhases];
            Span<double> moved = stackalloc double[SdfPhaseNode.MaxPhases];
            int overlaps = 0, steep = 0, wrongPhase = 0, wrongUnion = 0, inside = 0;
            double lipschitz = field.Lipschitz();
            for (int i = 0; i < Points; ++i)
            {
                SdfVector p = Random(ref rng, 1.5), q = p + Random(ref rng, 0.02);
                field.Phases(p, values);
                field.Phases(q, moved);
                int negative = 0, which = -1;
                double smallest = double.PositiveInfinity;
                for (int k = 0; k < SdfPhaseNode.MaxPhases; ++k)
                {
                    smallest = Math.Min(smallest, values[k]);
                    if (values[k] < 0)
                    {
                        ++negative;
                        which = k;
                    }
                    if (!double.IsInfinity(values[k]) && Math.Abs(values[k] - moved[k]) > lipschitz * (p - q).Length() * (1 + 1e-9) + 1e-12)
                    {
                        ++steep;
                    }
                }
                overlaps += negative > 1 ? 1 : 0;
                wrongUnion += smallest != field.Evaluate(p) ? 1 : 0;
                if (negative == 1)
                {
                    ++inside;
                    wrongPhase += field.PhaseAt(p) != which ? 1 : 0;
                }
            }
            if (overlaps > 0 || steep > 0 || wrongPhase > 0 || wrongUnion > 0)
            {
                Console.WriteLine("{0}: {1} points in two phases, {2} values that change too fast, {3} wrong phases, {4} wrong unions",
                                  expression, overlaps, steep, wrongPhase, wrongUnion);
            }
            if (inside <= 200) Console.WriteLine("{0}: only {1} points inside", expression, inside);
            CHECK_GT(inside, 200);
            CHECK_EQ(overlaps, 0);
            CHECK_EQ(steep, 0);
            CHECK_EQ(wrongPhase, 0);
            CHECK_EQ(wrongUnion, 0);
        }

        // The number of random points at which two fields are not on the same side of their surfaces
        private static int Disagreements(string first, string second)
        {
            SdfNode a = SdfParser.Parse(first, Legacy()), b = SdfParser.Parse(second, Legacy());
            RNG rng = new RNG(53);
            int different = 0;
            for (int i = 0; i < Points; ++i)
            {
                SdfVector p = Random(ref rng, 1.5);
                double va = a.Evaluate(p), vb = b.Evaluate(p);
                different += Math.Abs(va) > 1e-9 && Math.Abs(vb) > 1e-9 && (va < 0) != (vb < 0) ? 1 : 0;
            }
            return different;
        }

        [TEST("SdfPhases", "Properties")]
        public static void SdfPhases_Properties()
        {
            string ball = "sphere(0.7)", block = "translate(box(0.5, 0.4, 0.4), 0.5, 0.1, 0)", ring = "translate(torus(0.6, 0.2), 0, 0.3, 0)";
            string two = "under(phase(" + ball + ", 0), phase(" + block + ", 1))";
            string three = "over(" + two + ", phase(" + ring + ", 2))";
            string[] trees = {
                "phase(" + ball + ", 3)", two, "over(phase(" + ball + ", 0), phase(" + block + ", 1))", three,
                "under(" + two + ", phase(" + ring + ", 0))",
                "contact(" + ball + ", " + block + ", 0, 1)", "contact(" + ball + ", " + block + ", 0, 1, 0.2)",
                "contact(" + ball + ", " + block + ", 0, 1, 0.8)", "contactsmooth(" + ball + ", " + block + ", 0, 1, 0.2)",
                "layer(" + two + ", 2, 0.15)", "layer(layer(" + three + ", 3, 0.1), 4, 0.1)",
                "intersect(" + three + ", plane(0, 0, 1, 0))", "subtract(" + three + ", translate(sphere(0.6), 0.6, 0.6, 0.6))",
                three + " - translate(sphere(0.6), 0.6, 0.6, 0.6)", three + " & box(1, 0.5, 1)",
                "change(" + two + ", translate(sphere(0.4), -0.4, 0.3, 0), 0, 5)", "remove(" + three + ", 1)", "shrink(" + three + ", 0.03)",
                "intersect(checker(0, 1, 0.4), " + ball + ")", "under(" + two + ", intersect(checker(2, 3, 0.3), " + ring + "))" };
            foreach (string tree in trees)
            {
                CheckProperties(tree);
            }
        }

        // The compiled form gives the values of the tree that is walked
        private static void CheckCompiled(string expression)
        {
            SdfNode field = SdfParser.Parse(expression, Legacy());
            SdfNode compiled = SdfCompiledNode.Compile(field);
            CHECK(compiled is SdfCompiledNode);
            RNG rng = new RNG(57);
            int different = 0;
            for (int i = 0; i < Points; ++i)
            {
                SdfVector p = Random(ref rng, 1.5);
                double expected = field.Evaluate(p), value = compiled.Evaluate(p);
                different += Math.Abs(expected - value) > 1e-12 * Math.Max(1.0, Math.Abs(expected)) ? 1 : 0;
            }
            if (different > 0)
            {
                Console.WriteLine("{0}: {1} compiled values differ", expression, different);
            }
            CHECK_EQ(different, 0);
        }

        [TEST("SdfPhases", "Compiled")]
        public static void SdfPhases_Compiled()
        {
            string ball = "sphere(0.7)", block = "translate(box(0.5, 0.4, 0.4), 0.5, 0.1, 0)", ring = "translate(torus(0.6, 0.2), 0, 0.3, 0)";
            string two = "under(phase(" + ball + ", 0), phase(" + block + ", 1))";
            string three = "over(" + two + ", phase(" + ring + ", 2))";
            string[] trees = {
                two, three, "under(" + two + ", phase(" + ring + ", 0))", "contact(" + ball + ", " + block + ", 0, 1, 0.3)",
                "contactsmooth(" + ball + ", " + block + ", 0, 1, 0.2)", "layer(layer(" + three + ", 3, 0.1), 4, 0.1)",
                "layer(" + two + ", 1, 0.1)", "subtract(" + three + ", translate(sphere(0.6), 0.6, 0.6, 0.6))", three + " & box(1, 0.5, 1)",
                "change(" + two + ", translate(sphere(0.4), -0.4, 0.3, 0), 0, 5)", "change(" + two + ", translate(sphere(0.4), -0.4, 0.3, 0), 0, 1)",
                "remove(" + three + ", 1)", "shrink(" + three + ", 0.03)", "intersect(checker(0, 1, 0.4), " + ball + ")",
                "under(" + two + ", intersect(checker(2, 3, 0.3), " + ring + "))" };
            foreach (string tree in trees)
            {
                CheckCompiled(tree);
                CheckCompiled("merge(" + tree + ", 0)");
                CheckCompiled("merge(" + tree + ", 1, 2)");
                CheckCompiled("union(" + tree + ", translate(sphere(0.2), -1, -1, -1))");
            }
        }

        // What each operator is meant to build
        [TEST("SdfPhases", "Operators")]
        public static void SdfPhases_Operators()
        {
            string ball = "sphere(0.7)", block = "translate(box(0.5, 0.4, 0.4), 0.5, 0.1, 0)";
            string under = "under(phase(" + ball + ", 0), phase(" + block + ", 1))", over = "over(phase(" + ball + ", 0), phase(" + block + ", 1))";
            // Together the two phases are the union, whichever is on top
            CHECK_EQ(Disagreements(under, "union(" + ball + ", " + block + ")"), 0);
            CHECK_EQ(Disagreements(over, "union(" + ball + ", " + block + ")"), 0);
            CHECK_EQ(Disagreements("contact(" + ball + ", " + block + ", 0, 1)", "union(" + ball + ", " + block + ")"), 0);
            // (the comparison sees a difference when there is one)
            CHECK_GT(Disagreements("merge(" + under + ", 1)", block), 500);
            // Under keeps the first whole and gives the second what is left; over the other way round
            CHECK_EQ(Disagreements("merge(" + under + ", 0)", ball), 0);
            CHECK_EQ(Disagreements("merge(" + under + ", 1)", "subtract(" + block + ", " + ball + ")"), 0);
            CHECK_EQ(Disagreements("merge(" + over + ", 1)", block), 0);
            CHECK_EQ(Disagreements("merge(" + over + ", 0)", "subtract(" + ball + ", " + block + ")"), 0);
            // A contact at alpha 1 is under, at 0 it is over
            CHECK_EQ(Disagreements("merge(contact(" + ball + ", " + block + ", 0, 1, 1), 0)", ball), 0);
            CHECK_EQ(Disagreements("merge(contact(" + ball + ", " + block + ", 0, 1, 0), 1)", block), 0);
            // A coat is the solid grown by its thickness, less the solid; the whole is the grown solid
            CHECK_EQ(Disagreements("layer(phase(" + ball + ", 0), 1, 0.2)", "sphere(0.9)"), 0);
            CHECK_EQ(Disagreements("merge(layer(phase(" + ball + ", 0), 1, 0.2), 1)", "subtract(sphere(0.9), " + ball + ")"), 0);
            // A cut cuts every phase; a change moves the part of a phase inside the region to another phase
            CHECK_EQ(Disagreements("merge(intersect(" + under + ", plane(0, 0, 1, 0)), 1)",
                                   "intersect(subtract(" + block + ", " + ball + "), plane(0, 0, 1, 0))"), 0);
            string region = "translate(sphere(0.4), -0.4, 0.3, 0)";
            CHECK_EQ(Disagreements("merge(change(" + under + ", " + region + ", 0, 5), 5)", "intersect(" + ball + ", " + region + ")"), 0);
            CHECK_EQ(Disagreements("merge(change(" + under + ", " + region + ", 0, 5), 0)", "subtract(" + ball + ", " + region + ")"), 0);
            CHECK_EQ(Disagreements("merge(remove(" + under + ", 0))", "subtract(" + block + ", " + ball + ")"), 0);
            CHECK_EQ(Disagreements("merge(" + under + ", 0, 1)", "union(" + ball + ", " + block + ")"), 0);

            // The phase of a point reaches the labels that the sdflabel texture reads
            SdfNode field = SdfParser.Parse(under, Legacy());
            CHECK_EQ(((SdfPhaseNode)field).PhaseAt(new SdfVector(-0.3, 0, 0)), 0);
            CHECK_EQ(((SdfPhaseNode)field).PhaseAt(new SdfVector(0.9, 0.1, 0)), 1);
            // A box for the shape
            CHECK(field.Bounds(out SdfVector lower, out SdfVector upper, out bool grows));
            CHECK(lower.x <= -0.7 && upper.x >= 1.0 && grows);
        }
    }
}
