// (not in pbrt-v4) sdfrepeat_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class SdfRepeatTests
    {
        // A repeat of a child against the union of the copies written out one by one: the repeat must not say that the surface
        // is farther than it is (a ray would step past it), must be the same solid, and must be hit by the same rays
        private static void Compare(string child, double period, int count, bool alongY, bool alongZ, double jitter)
        {
            string repeat = "repeat(" + child + ", " + period + ", " + (alongY ? period : 0) + ", " + (alongZ ? period : 0) + ", " + count + ", " + jitter + ", 5)";
            SdfRepeat cells = (SdfRepeat)SdfParser.Parse(repeat, new SdfParseContext());
            string union = "";
            for (int i = -count; i <= count; ++i)
            {
                for (int j = alongY ? -count : 0; j <= (alongY ? count : 0); ++j)
                {
                    for (int k = alongZ ? -count : 0; k <= (alongZ ? count : 0); ++k)
                    {
                        // Each copy where the repeat says it is, jitter and all
                        string copy = "translate(" + child + ", " + (i * period + cells.Jitter(i, j, k, 0)).ToString("R") + ", " +
                                      (j * period + cells.Jitter(i, j, k, 1)).ToString("R") + ", " + (k * period + cells.Jitter(i, j, k, 2)).ToString("R") + ")";
                        union = union.Length == 0 ? copy : "union(" + union + ", " + copy + ")";
                    }
                }
            }
            SdfNode a = SdfParser.Parse(repeat, new SdfParseContext()), b = SdfParser.Parse(union, new SdfParseContext());
            SdfNode compiled = SdfCompiledNode.Compile(a);
            double extent = (count + 1) * period + jitter;
            RNG rng = new RNG(81);
            int tooFar = 0, otherSide = 0, compiledDiffers = 0;
            double worst = 0;
            for (int i = 0; i < 200000; ++i)
            {
                SdfVector p = new SdfVector(extent * (2 * rng.Uniform<double>() - 1), extent * (2 * rng.Uniform<double>() - 1),
                                            extent * (2 * rng.Uniform<double>() - 1));
                double va = a.Evaluate(p), vb = b.Evaluate(p);
                compiledDiffers += Math.Abs(compiled.Evaluate(p) - va) > 1e-12 ? 1 : 0;
                if (Math.Abs(vb) > 1e-9 && (va < 0) != (vb < 0))
                {
                    ++otherSide;
                }
                else if (vb > 0 && va > vb + 1e-9)
                {
                    ++tooFar;
                    worst = Math.Max(worst, va - vb);
                }
            }
            Point3f lower = new Point3f((Float)(-extent), (Float)(-extent), (Float)(-extent)), upper = new Point3f((Float)extent, (Float)extent, (Float)extent);
            SdfShape first = new SdfShape(new Transform(), new Transform(), false, a, lower, upper, new SdfShapeOptions());
            SdfShape second = new SdfShape(new Transform(), new Transform(), false, b, lower, upper, new SdfShapeOptions());
            int rays = 40000, hits = 0, different = 0, apart = 0;
            for (int i = 0; i < rays; ++i)
            {
                Vector3f from = Normalize(new Vector3f((Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1),
                                                       (Float)(2 * rng.Uniform<double>() - 1)));
                Point3f target = new Point3f((Float)(extent * (2 * rng.Uniform<double>() - 1)), (Float)(extent * (2 * rng.Uniform<double>() - 1)),
                                             (Float)(extent * (2 * rng.Uniform<double>() - 1)));
                Point3f o = new Point3f((Float)(4 * extent * from.x), (Float)(4 * extent * from.y), (Float)(4 * extent * from.z));
                Ray ray = new Ray(o, Normalize(target - o));
                pstd.optional<ShapeIntersection> ha = first.Intersect(ray), hb = second.Intersect(ray);
                if (ha.has_value() != hb.has_value())
                {
                    ++different;
                }
                else if (ha.has_value())
                {
                    ++hits;
                    apart += Math.Abs(ha.value().tHit - hb.value().tHit) > 1e-4 ? 1 : 0;
                }
            }
            Console.WriteLine("{0}: of 200000 points {1} too far (by up to {2:F3}), {3} on the other side of the surface, {4} compiled values differ; of {5} rays {6} hit, {7} hit one only, {8} hits apart",
                              repeat, tooFar, worst, otherSide, compiledDiffers, rays, hits, different, apart);
            CHECK_EQ(tooFar, 0);
            CHECK_EQ(otherSide, 0);
            CHECK_EQ(compiledDiffers, 0);
            CHECK_EQ(different, 0);
            CHECK_EQ(apart, 0);
        }

        [TEST("SdfRepeat", "IsTheUnionOfItsCopies")]
        public static void SdfRepeat_IsTheUnionOfItsCopies()
        {
            // A child at the middle of its cell, one off the middle, one that reaches into the next cell, one longer than a cell
            Compare("sphere(0.2)", 0.6, 1, true, true, 0);
            Compare("translate(sphere(0.1), 0.15, 0.1, 0)", 0.6, 1, true, true, 0);
            Compare("translate(sphere(0.2), 0.25, 0, 0)", 0.6, 1, true, true, 0);
            Compare("rotate(box(0.5, 0.05, 0.05), 0, 0, 30)", 0.6, 2, true, false, 0);
            Compare("translate(torus(0.25, 0.06), 0, 0.1, 0)", 0.5, 2, false, false, 0);
            // With a jitter: a small one, one that lets copies touch, one larger than a cell
            Compare("sphere(0.15)", 0.6, 2, true, true, 0.1);
            Compare("sphere(0.2)", 0.6, 1, true, true, 0.25);
            Compare("rotate(box(0.3, 0.05, 0.05), 0, 0, 30)", 0.5, 2, true, false, 0.7);
            Compare("translate(torus(0.2, 0.05), 0.1, 0, 0)", 0.5, 3, false, false, 0.3);
        }
    }
}
