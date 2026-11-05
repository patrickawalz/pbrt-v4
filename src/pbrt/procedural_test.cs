// (not in pbrt-v4) Tests of the procedural primitive (shapes_procedural.cs)

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class ProceduralTests
    {
        private static int expansions;

        [TEST("Procedural", "ExpandsOnFirstHitOnly")]
        public static void ExpandsOnFirstHitOnly()
        {
            expansions = 0;
            // a procedural that makes three unit spheres at x = 0, 3, 6 (the transform of the shape applies to them)
            Procedurals.Register("threespheres", (Transform rfo, Transform ofr, bool reverse, ParameterDictionary parameters, FileLoc loc, Allocator alloc) =>
            {
                ++expansions;
                pstd.vector<Shape> shapes = new pstd.vector<Shape>();
                for (int i = 0; i < 3; ++i)
                {
                    Transform t = rfo * Translate(new Vector3f(3 * i, 0, 0));
                    shapes.push_back(new Sphere(t, Inverse(t), reverse, 1, -1, 1, 360));
                }
                return shapes;
            });
            List<ParsedParameter> list = new List<ParsedParameter>();
            ParsedParameter name = new ParsedParameter(null);
            name.type = "string";
            name.name = "name";
            name.strings.push_back("threespheres");
            list.Add(name);
            ParsedParameter bound = new ParsedParameter(null);
            bound.type = "float";
            bound.name = "bound";
            foreach (float v in new float[] { -1, 7, -1, 1, -1, 1 })
            {
                bound.floats.push_back(v);
            }
            list.Add(bound);
            Transform place = Translate(new Vector3f(0, 2, 0)) * Rotate(30, new Vector3f(0, 0, 1));
            pstd.vector<Shape> made = Shape.Create("procedural", place, Inverse(place), false, new ParameterDictionary(list, RGBColorSpace.sRGB),
                                                  new Dictionary<string, FloatTexture>(), null, new Allocator());
            ASSERT_EQ(1, (int)made.size());
            Shape shape = made[0];
            EXPECT_EQ(0, expansions, "nothing is expanded at creation");
            // the bounds contain the transformed spheres
            Bounds3f b = shape.Bounds();
            EXPECT_TRUE(b.pMin.x < place.Apply(new Point3f(0, 0, 0)).x && b.pMax.y > place.Apply(new Point3f(6, 0, 0)).y);
            // a ray that misses the bound does not expand it
            EXPECT_TRUE(!shape.IntersectP(new Ray(new Point3f(0, 50, 0), new Vector3f(0, 1, 0))));
            EXPECT_EQ(0, expansions, "a ray that misses the bound");
            // a ray at the second sphere expands once and hits it at the right distance
            Point3f center = place.Apply(new Point3f(3, 0, 0));
            pstd.optional<ShapeIntersection> hit = shape.Intersect(new Ray(center + new Vector3f(0, 0, 5), new Vector3f(0, 0, -1)));
            ASSERT_TRUE(hit.has_value());
            EXPECT_NEAR(4.0, hit.value().tHit, 1e-4);
            EXPECT_TRUE(shape.IntersectP(new Ray(center + new Vector3f(0, 0, 5), new Vector3f(0, 0, -1))));
            EXPECT_EQ(1, expansions, "expanded once");
            // the nearest of the children is found
            Point3f first = place.Apply(new Point3f(-5, 0, 0));
            Vector3f along = Normalize(place.Apply(new Vector3f(1, 0, 0)));
            pstd.optional<ShapeIntersection> nearest = shape.Intersect(new Ray(first, along));
            ASSERT_TRUE(nearest.has_value());
            EXPECT_NEAR(4.0, nearest.value().tHit, 1e-4, "nearest of three");
            // area and sampling: three spheres
            EXPECT_NEAR(3 * 4 * Math.PI, shape.Area(), 1e-3);
            Random rng = new Random(1);
            double sum = 0, squares = 0;
            int n = 200000;
            for (int i = 0; i < n; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                ASSERT_TRUE(ss.has_value());
                double f = (ss.value().intr.p().x) / ss.value().pdf;
                sum += f;
                squares += f * f;
            }
            // the integral of x over the spheres: 4 pi (x0)  for each sphere with its center x0 (after the transform): the sum of the centers
            double expected = 0;
            for (int i = 0; i < 3; ++i)
            {
                expected += 4 * Math.PI * place.Apply(new Point3f(3 * i, 0, 0)).x;
            }
            double mean = sum / n, error = Math.Sqrt(Math.Max(0, squares / n - mean * mean) / n);
            EXPECT_LT(Math.Abs(mean - expected) / error, 4.0, "E[f / pdf] " + mean + " +- " + error + " against " + expected);
            // the solid angle density integrates to the visible share: from far away the three spheres cover little; the sample pdf equals PDF(ctx, wi)
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(place.Apply(new Point3f(3, 0, 8))), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
            int compared = 0, mismatched = 0;
            for (int i = 0; i < 2000; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                if (!ss.has_value())
                {
                    continue;
                }
                Vector3f wi = Normalize(ss.value().intr.p() - ctx.p());
                ++compared;
                if (Math.Abs(shape.PDF(ctx, wi) / ss.value().pdf - 1) > 2e-3)
                {
                    ++mismatched;
                }
            }
            EXPECT_GT(compared, 1500);
            EXPECT_LT(mismatched, compared / 100, "pdf of the samples against PDF(ctx, wi)");
        }
    }
}  // namespace pbrt
