// (not in pbrt-v4) wireframe_test.cs
//
// The wireframe texture (wireframe.cs): the distance to the edges of a triangle and of a patch against the distance measured
// in space, in the units of the scene and in pixels; the longest edge of a triangle hidden for quadrilaterals; the lines of
// the parameters of a shape without edges; the width of a line in pixels does not depend on the size of the pixel.

using System;
using testing;

namespace pbrt
{
    public static unsafe class WireframeTests
    {
        static WireframeSettings Settings(WireframeUnits units, Float width, bool quads)
        {
            WireframeSettings s = new WireframeSettings();
            s.units = units;
            s.width = width;
            s.quads = quads;
            s.uSteps = 8;
            s.vSteps = 4;
            return s;
        }

        static double ToSegmentLine(Point3f p, Point3f a, Point3f b)
        {
            Vector3f d = Normalize(b - a);
            Vector3f off = p - a;
            return Length(off - Dot(off, d) * d);
        }

        [TEST("Wireframe", "TriangleAndPatchEdges")]
        public static void TriangleAndPatchEdges()
        {
            RNG rng = new RNG(3);
            double worstWorld = 0, worstPixel = 0, worstPatch = 0;
            for (int n = 0; n < 4000; ++n)
            {
                Point3f p0 = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                Point3f p1 = p0 + new Vector3f(1 + rng.Uniform<Float>(), 0.3f * rng.Uniform<Float>(), 0.2f * rng.Uniform<Float>());
                Point3f p2 = p0 + new Vector3f(0.4f * rng.Uniform<Float>(), 1 + rng.Uniform<Float>(), 0.3f * rng.Uniform<Float>());
                Float b1 = rng.Uniform<Float>(), b2 = rng.Uniform<Float>();
                if (b1 + b2 > 1)
                {
                    b1 = 1 - b1;
                    b2 = 1 - b2;
                }
                SurfaceInteraction si = new SurfaceInteraction();
                si.edgeKind = 1;
                si.edgeCoordinates = new Point2f(b1, b2);
                si.edgeU = p1 - p0;
                si.edgeV = p2 - p0;
                Point3f p = p0 + b1 * si.edgeU + b2 * si.edgeV;
                // A pixel: two perpendicular steps of the same length in the plane of the triangle
                Float pixel = 0.001f + 0.02f * rng.Uniform<Float>();
                Vector3f normal = Normalize(Cross(si.edgeU, si.edgeV));
                Vector3f along = Normalize(si.edgeU + rng.Uniform<Float>() * si.edgeV);
                TextureEvalContext ctx = new TextureEvalContext();
                ctx.si = si;
                ctx.dpdx = pixel * along;
                ctx.dpdy = pixel * Cross(normal, along);
                double expected = Math.Min(ToSegmentLine(p, p0, p1), Math.Min(ToSegmentLine(p, p0, p2), ToSegmentLine(p, p1, p2)));
                double world = Wireframe.Distance(ctx, Settings(WireframeUnits.World, 0.02f, false));
                double pixels = Wireframe.Distance(ctx, Settings(WireframeUnits.Pixels, 1.5f, false));
                worstWorld = Math.Max(worstWorld, Math.Abs(world - expected));
                worstPixel = Math.Max(worstPixel, Math.Abs(pixels - expected / pixel) / (1 + expected / pixel));

                // The same vertices as a patch (a parallelogram): its four edges
                si.edgeKind = 2;
                si.edgeCoordinates = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                Point3f q = p0 + si.edgeCoordinates[0] * si.edgeU + si.edgeCoordinates[1] * si.edgeV;
                Point3f p3 = p1 + si.edgeV;
                double patch = Math.Min(Math.Min(ToSegmentLine(q, p0, p1), ToSegmentLine(q, p0, p2)), Math.Min(ToSegmentLine(q, p1, p3), ToSegmentLine(q, p2, p3)));
                worstPatch = Math.Max(worstPatch, Math.Abs(Wireframe.Distance(ctx, Settings(WireframeUnits.World, 0.02f, false)) - patch));
            }
            Console.WriteLine("MEASURE wireframe: distance to the edges of a triangle off by {0:E2} in space and {1:E2} (relative) in pixels, of a patch {2:E2}", worstWorld,
                              worstPixel, worstPatch);
            EXPECT_LT(worstWorld, 2e-5);
            EXPECT_LT(worstPixel, 2e-3);
            EXPECT_LT(worstPatch, 2e-5);
        }

        [TEST("Wireframe", "QuadsAndCoverage")]
        public static void QuadsAndCoverage()
        {
            // Half of a unit square: the diagonal (from p1 to p2) is hidden for quadrilaterals
            SurfaceInteraction si = new SurfaceInteraction();
            si.edgeKind = 1;
            si.edgeU = new Vector3f(1, 0, 0);
            si.edgeV = new Vector3f(0, 1, 0);
            si.edgeCoordinates = new Point2f(0.49f, 0.49f);
            TextureEvalContext ctx = new TextureEvalContext();
            ctx.si = si;
            ctx.dpdx = new Vector3f(0.01f, 0, 0);
            ctx.dpdy = new Vector3f(0, 0.01f, 0);
            EXPECT_LT(Math.Abs(Wireframe.Distance(ctx, Settings(WireframeUnits.World, 0.02f, false)) - 0.02 / Math.Sqrt(2)), 1e-5);
            EXPECT_LT(Math.Abs(Wireframe.Distance(ctx, Settings(WireframeUnits.World, 0.02f, true)) - 0.49), 1e-5);
            EXPECT_EQ(1f, Wireframe.Coverage(ctx, Settings(WireframeUnits.World, 0.05f, false)));
            EXPECT_EQ(0f, Wireframe.Coverage(ctx, Settings(WireframeUnits.World, 0.05f, true)));

            // A line of w pixels covers w pixels across it, whatever the size of a pixel
            foreach (Float pixel in new Float[] { 0.002f, 0.01f, 0.03f })
            {
                foreach (Float width in new Float[] { 1, 2.5f, 4 })
                {
                    ctx.dpdx = new Vector3f(pixel, 0, 0);
                    ctx.dpdy = new Vector3f(0, pixel, 0);
                    // (across the edge b = 0, from the edge into the triangle: the line is half of its width on this side)
                    double covered = 0;
                    int steps = 4000;
                    Float reach = 6 * pixel;
                    for (int i = 0; i < steps; ++i)
                    {
                        si.edgeCoordinates = new Point2f(0.3f, (i + 0.5f) / steps * reach);
                        covered += Wireframe.Coverage(ctx, Settings(WireframeUnits.Pixels, width, true)) * reach / steps;
                    }
                    EXPECT_LT(Math.Abs(covered / pixel - width / 2), 0.01);
                }
            }
            FloatWireframeTexture texture = new FloatWireframeTexture(Settings(WireframeUnits.World, 0.05f, true), new FloatConstantTexture(3), new FloatConstantTexture(0.5f));
            si.edgeCoordinates = new Point2f(0.3f, 0.3f);
            EXPECT_EQ(0.5f, texture.Evaluate(ctx));
            si.edgeCoordinates = new Point2f(0.3f, 0.01f);
            EXPECT_EQ(3f, texture.Evaluate(ctx));
            EXPECT_EQ(3f, texture.MaxValue());
            EXPECT_EQ(0.5f, texture.MinValue());
        }

        [TEST("Wireframe", "ParameterLines")]
        public static void ParameterLines()
        {
            // A shape without edges: 8 lines over u and 4 over v
            TextureEvalContext ctx = new TextureEvalContext();
            ctx.uv = new Point2f(0.26f, 0.4f);
            ctx.dudx = 0.005f;
            ctx.dvdy = 0.01f;
            EXPECT_LT(Math.Abs(Wireframe.Distance(ctx, Settings(WireframeUnits.Parameter, 0.01f, true)) - 0.01), 1e-6);
            EXPECT_LT(Math.Abs(Wireframe.Distance(ctx, Settings(WireframeUnits.Pixels, 1, true)) - 2), 1e-4);
            ctx.uv = new Point2f(0.31f, 0.52f);
            EXPECT_LT(Math.Abs(Wireframe.Distance(ctx, Settings(WireframeUnits.Parameter, 0.01f, true)) - 0.02), 1e-6);
            EXPECT_LT(Math.Abs(Wireframe.Distance(ctx, Settings(WireframeUnits.Pixels, 1, true)) - 2), 1e-4);
            WireframeSettings onlyU = Settings(WireframeUnits.Parameter, 0.01f, true);
            onlyU.vSteps = 0;
            EXPECT_LT(Math.Abs(Wireframe.Distance(ctx, onlyU) - 0.06), 1e-6);
        }
    }
}
