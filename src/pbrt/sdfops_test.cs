// (not in pbrt-v4) sdfops_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class SdfOpsTests
    {
        private static SdfNode Parse(string expression)
        {
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = 4;
            return SdfParser.Parse(expression, context);
        }

        private static double At(SdfNode node, double x, double y, double z)
        {
            return node.Evaluate(new SdfVector(x, y, z));
        }

        [TEST("SdfOps", "InkInsetClip")]
        public static void SdfOps_InkInsetClip()
        {
            SdfNode ink = Parse("ink(box(1, 1, 1), plane(1, 0, 0, 0), 0.2)");
            EXPECT_TRUE(At(ink, 0, 0, 0) > 0);     // in the groove
            EXPECT_TRUE(At(ink, 0.5, 0, 0) < 0);   // beside it
            EXPECT_TRUE(At(ink, -0.5, 0, 0) < 0);

            SdfNode inset = Parse("inset(sphere(1), box(0.3, 0.3, 0.3), 0.2, 0.05)");
            EXPECT_NEAR(At(inset, 0, 0, 0), -0.8, 1e-12);          // inside b: pushed in by the depth
            EXPECT_NEAR(At(inset, 0, 0, 0.9), -0.1, 1e-12);         // outside b: the sphere itself
            EXPECT_NEAR(At(inset, 0, 0, 2), 1, 1e-12);

            SdfNode clip = Parse("clip(sphere(1), plane(0, 0, 1, 0), 0.01)");
            EXPECT_TRUE(At(clip, 0, 0, -1) < 0);   // the skin below the plane stays
            EXPECT_TRUE(At(clip, 0, 0, 1) > 0);    // the skin above it is cut away
            EXPECT_TRUE(At(clip, 0, 0, 0) > 0);    // and it is hollow
        }

        [TEST("SdfOps", "MetaballsAndJulia")]
        public static void SdfOps_MetaballsAndJulia()
        {
            // One ball of strength 1: the surface is where (1 - s^2)^3 = 0.5
            SdfNode balls = Parse("metaballs(0.5, 0, 0, 0, 1, 1)");
            double s = Math.Sqrt(1 - Math.Pow(0.5, 1.0 / 3.0));
            EXPECT_TRUE(At(balls, 0.9 * s, 0, 0) < 0);
            EXPECT_TRUE(At(balls, 1.1 * s, 0, 0) > 0);
            // Two close balls join into one blob: the middle is inside though neither alone reaches it
            SdfNode pair = Parse("metaballs(0.5, -0.4, 0, 0, 1, 1, 0.4, 0, 0, 1, 1)");
            SdfNode single = Parse("metaballs(0.5, -0.4, 0, 0, 1, 1)");
            EXPECT_TRUE(At(pair, 0, 0.6, 0) < At(single, 0, 0.6, 0));
            // The field bound: no step of the field crosses the surface
            RNG rng = new RNG(2);
            for (int i = 0; i < 2000; ++i)
            {
                SdfVector p = new SdfVector(2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1);
                SdfVector q = p + new SdfVector(0.01, 0.0, 0.0);
                EXPECT_TRUE(Math.Abs(pair.Evaluate(p) - pair.Evaluate(q)) <= 0.01 * pair.Lipschitz() + 1e-12);
            }

            SdfNode julia = Parse("julia(-0.2, 0.6, 0.2, 0, 12)");
            EXPECT_TRUE(At(julia, 3, 0, 0) > 0);
            bool inside = false;
            for (double x = -1.5; x <= 1.5 && !inside; x += 0.05)
            {
                for (double y = -1.5; y <= 1.5 && !inside; y += 0.05)
                {
                    inside = At(julia, x, y, 0) < 0;
                }
            }
            EXPECT_TRUE(inside);
        }

        [TEST("SdfOps", "MaterialWeights")]
        public static void SdfOps_MaterialWeights()
        {
            SdfNode union = Parse("label(sphere(1), 1) | translate(label(sphere(1), 2), 3, 0, 0)");
            SdfWeights w = new SdfWeights();
            union.Weights(new SdfVector(-1, 0, 0), w, 1);
            EXPECT_NEAR(w.Get(1), 1, 1e-12);
            w = new SdfWeights();
            union.Weights(new SdfVector(4, 0, 0), w, 1);
            EXPECT_NEAR(w.Get(2), 1, 1e-12);

            // Across a smooth union the weights move from one operand to the other and add up to one
            SdfNode smooth = Parse("smoothunion(label(sphere(1), 1), translate(label(sphere(1), 2), 2, 0, 0), 0.8)");
            double previous = 2;
            for (double x = 0.5; x <= 1.5; x += 0.1)
            {
                w = new SdfWeights();
                smooth.Weights(new SdfVector(x, 0, 0), w, 1);
                EXPECT_NEAR(w.Get(1) + w.Get(2), 1, 1e-12);
                EXPECT_TRUE(w.Get(1) <= previous + 1e-12);
                previous = w.Get(1);
            }
            w = new SdfWeights();
            smooth.Weights(new SdfVector(1, 0, 0), w, 1);
            EXPECT_NEAR(w.Get(1), 0.5, 1e-9);

            // Transfer: the shape of the first and the labels of the second
            SdfNode transfer = Parse("transfer(sphere(1), label(box(2, 2, 2), 5))");
            w = new SdfWeights();
            transfer.Weights(new SdfVector(0, 0, 1), w, 1);
            EXPECT_NEAR(w.Get(5), 1, 1e-12);
            EXPECT_NEAR(At(transfer, 0, 0, 3), 2, 1e-12);

            // A subtracted shape owns the surface it cuts
            SdfNode cut = Parse("label(sphere(1), 1) - translate(label(sphere(0.5), 2), 0, 0, 1)");
            w = new SdfWeights();
            cut.Weights(new SdfVector(0, 0, 0.5), w, 1);
            EXPECT_NEAR(w.Get(2), 1, 1e-12);
        }

        [TEST("SdfOps", "TimeVaryingFields")]
        public static void SdfOps_TimeVaryingFields()
        {
            SdfNode morph = Parse("morph(sphere(1), sphere(2))");
            SdfNode move = Parse("translate_t(sphere(0.5), 2, 0, 0)");
            SdfNode turn = Parse("rotate_t(translate(sphere(0.5), 1, 0, 0), 0, 0, 90)");
            try
            {
                SdfTime.Current = 0;
                EXPECT_NEAR(At(morph, 0, 0, 3), 2, 1e-12);
                EXPECT_NEAR(At(move, 0, 0, 0), -0.5, 1e-12);
                SdfTime.Current = 1;
                EXPECT_NEAR(At(morph, 0, 0, 3), 1, 1e-12);
                EXPECT_NEAR(At(move, 2, 0, 0), -0.5, 1e-12);
                EXPECT_NEAR(At(turn, 0, 1, 0), -0.5, 1e-12);                    // the sphere at x = 1 has gone round to y = 1
                SdfTime.Current = 0.5;
                EXPECT_NEAR(At(morph, 0, 0, 3), 1.5, 1e-12);
                EXPECT_NEAR(At(move, 1, 0, 0), -0.5, 1e-12);

                // The shape follows the time of the ray
                SdfShapeOptions options = new SdfShapeOptions();
                SdfShape shape = new SdfShape(new Transform(), new Transform(), false, move, new Point3f(-3, -1, -1), new Point3f(3, 1, 1),
                                              options);
                Ray early = new Ray(new Point3f(-5, 0, 0), new Vector3f(1, 0, 0), 0);
                Ray late = new Ray(new Point3f(-5, 0, 0), new Vector3f(1, 0, 0), 1);
                pstd.optional<ShapeIntersection> a = shape.Intersect(early), b = shape.Intersect(late);
                EXPECT_TRUE((bool)a && (bool)b);
                if (a && b)
                {
                    EXPECT_NEAR(a.value().intr.p().x, -0.5, 1e-3);
                    EXPECT_NEAR(b.value().intr.p().x, 1.5, 1e-3);
                }
            }
            finally
            {
                SdfTime.Current = 0;
            }
        }

        [TEST("SdfOps", "TextureDisplacement")]
        public static void SdfOps_TextureDisplacement()
        {
            // A checkerboard of height 0.1 on a sphere: the surface sits at radius 1 + 0.1 on the white squares only
            SdfNode checker = Parse("texdisplace(sphere(1), \"checker\", 0.1, 0, 1, \"xy\", 1)");
            double up = At(checker, 0.5, 0.5, 0), down = At(checker, 1.5, 0.5, 0);
            double bare = Parse("sphere(1)").Evaluate(new SdfVector(0.5, 0.5, 0));
            EXPECT_NEAR(Math.Abs(up - bare) + Math.Abs(down - Parse("sphere(1)").Evaluate(new SdfVector(1.5, 0.5, 0))), 0.1, 1e-12);
            // The bound grows as the step scale shrinks
            SdfNode fine = Parse("texdisplace(sphere(1), \"fbm\", 0.1, 0, 0.5, \"xyz\", 4)");
            SdfNode coarse = Parse("texdisplace(sphere(1), \"fbm\", 0.1, 0, 1, \"xyz\", 4)");
            EXPECT_NEAR(fine.Lipschitz(), 2 * coarse.Lipschitz(), 1e-9);
            EXPECT_TRUE(!coarse.IsRigorous());
        }
    }
}
