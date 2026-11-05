// (not in pbrt-v4) quadrics_alpha_test.cs
//
// The alpha test path of the new quadrics: a GeometricPrimitive with a cutout texture (alpha 0 for u < 1/2, in the RenderMan u of the shape) must show the
// first hit with u >= 1/2 along the ray, found by spawning new rays past the hits that are cut out. The reference is the same loop over the bare shape and
// the angle of the hit point, so it checks that the (u, v) of the hits agree with the position and that rays spawned from a hit do not hit it again.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class QuadricAlphaTests
    {
        private sealed class CutoutTexture : FloatTexture
        {
            public override string ToString() { return "[ CutoutTexture ]"; }
            public override Float Evaluate(TextureEvalContext ctx) { return ctx.uv.x < 0.5f ? 0.0f : 1.0f; }
            public override Float MinValue() { return 0; }
            public override Float MaxValue() { return 1; }
        }

        [TEST("QuadricAlpha", "CutoutByRenderManU")]
        public static void CutoutByRenderManU()
        {
            Transform id = new Transform();
            Shape[] shapes =
            {
                new Cone(id, id, false, 2, 1.2f, 0, 2, 360),
                new Paraboloid(id, id, false, 1.3f, -0.5f, 1.5f, 360),
                new Hyperboloid(id, id, false, new Point3f(1, 0, -1), new Point3f(0.6f, 0.5f, 1), 360),
                new Torus(id, id, false, 1.5f, 0.5f, 0, 360, 360)
            };
            string[] names = { "cone", "paraboloid", "hyperboloid", "torus" };
            for (int k = 0; k < shapes.Length; ++k)
            {
                Shape shape = shapes[k];
                GeometricPrimitive primitive = new GeometricPrimitive(shape, null, null, new MediumInterface(), new CutoutTexture());
                Random rng = new Random(100 + k);
                int hits = 0, cutOut = 0;
                for (int i = 0; i < 3000; ++i)
                {
                    Vector3f toward = pbrt.Normalize(new Vector3f((Float)(rng.NextDouble() * 2 - 1), (Float)(rng.NextDouble() * 2 - 1), (Float)(rng.NextDouble() * 2 - 1)));
                    Point3f origin = new Point3f((Float)(6 * (rng.NextDouble() - 0.5)), (Float)(6 * (rng.NextDouble() - 0.5)), (Float)(6 * (rng.NextDouble() - 0.5))) +
                                     toward * -5.0f;
                    Point3f aim = new Point3f((Float)(1.5 * (rng.NextDouble() - 0.5)), (Float)(1.5 * (rng.NextDouble() - 0.5)), (Float)(1.5 * (rng.NextDouble() - 0.5)));
                    Ray ray = new Ray(origin, pbrt.Normalize(aim - origin));
                    // the reference: the hits of the shape in order, the first with u >= 1/2
                    bool expected = false;
                    double expectedT = 0;
                    Ray current = ray;
                    double offset = 0;
                    for (int step = 0; step < 8; ++step)
                    {
                        pstd.optional<ShapeIntersection> si = shape.Intersect(current);
                        if (!si.has_value())
                        {
                            break;
                        }
                        Point3f p = si.value().intr.p();
                        double phi = Math.Atan2(p.y, p.x);
                        if (phi < 0)
                        {
                            phi += 2 * Math.PI;
                        }
                        double u = phi / (2 * Math.PI);
                        // the u of the hit is the angle of its position (up to the seam); the generator line of the twisted hyperboloid turns with v, which
                        // the parameterization test of the quadrics checks instead
                        bool seam = Math.Abs(u - si.value().intr.uv.x) < 1e-3 || Math.Abs(Math.Abs(u - si.value().intr.uv.x) - 1) < 1e-3;
                        EXPECT_TRUE(seam || k == 2, names[k] + " ray " + i + ": u " + si.value().intr.uv.x + " vs angle " + u);
                        offset += si.value().tHit;
                        if (si.value().intr.uv.x >= 0.5f)
                        {
                            expected = true;
                            expectedT = offset;
                            break;
                        }
                        ++cutOut;
                        current = new Ray(si.value().intr.SpawnRay(current.d));
                    }
                    pstd.optional<ShapeIntersection> got = primitive.Intersect(ray);
                    EXPECT_EQ(expected, got.has_value(), names[k] + " ray " + i);
                    EXPECT_EQ(expected, primitive.IntersectP(ray), names[k] + " IntersectP ray " + i);
                    if (expected && got.has_value())
                    {
                        ++hits;
                        EXPECT_NEAR(expectedT, got.value().tHit, 1e-3 * Math.Max(1, expectedT), names[k] + " t, ray " + i);
                        EXPECT_TRUE(got.value().intr.uv.x >= 0.5f, names[k] + " u of the hit");
                    }
                }
                EXPECT_GT(hits, 100, names[k] + " hits");
                EXPECT_GT(cutOut, 100, names[k] + " hits that the alpha cuts out");
            }
        }
    }
}
