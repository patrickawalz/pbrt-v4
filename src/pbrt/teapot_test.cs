// (not in pbrt-v4) teapot_test.cs
//
// The Newell teapot (shapes_teapot.cs): the table is checked as data (counts, indices, bounds, the continuity of the shared edges between patches), and the
// shape as a surface (extent from rays, outward normals, no leaks between patches, options).

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class TeapotTests
    {
        private static pstd.vector<Shape> Make(Transform t, params ParsedParameter[] extra)
        {
            List<ParsedParameter> list = new List<ParsedParameter>(extra);
            return Shape.Create("teapot", t, Inverse(t), false, new ParameterDictionary(list, RGBColorSpace.sRGB), new Dictionary<string, FloatTexture>(), null, new Allocator());
        }

        private static ParsedParameter Bool(string name, bool value)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = "bool";
            p.name = name;
            p.bools.push_back((byte)(value ? 1 : 0));
            return p;
        }

        private static Point3f Vertex(int patch, int row, int column)
        {
            int i = Teapot.Table[16 * patch + 4 * row + column] * 3;
            return new Point3f(Teapot.Vertices[i], Teapot.Vertices[i + 1], Teapot.Vertices[i + 2]);
        }

        [TEST("Teapot", "TableIsConsistent")]
        public static void TableIsConsistent()
        {
            ASSERT_EQ(32 * 16, Teapot.Table.Length);
            ASSERT_EQ(290 * 3, Teapot.Vertices.Length);
            int minIndex = int.MaxValue, maxIndex = -1;
            bool[] used = new bool[290];
            foreach (int index in Teapot.Table)
            {
                minIndex = Math.Min(minIndex, index);
                maxIndex = Math.Max(maxIndex, index);
                used[index] = true;
            }
            EXPECT_EQ(0, minIndex);
            EXPECT_EQ(289, maxIndex);
            foreach (bool u in used)
            {
                EXPECT_TRUE(u, "every control point is used");
            }
            // the control points: z from 0 to 3.15 (the lid knob), y in [-2, 2], x from -3 (the handle) to 3.525 (the tip of the spout)
            float[] lo = { float.MaxValue, float.MaxValue, float.MaxValue }, hi = { -float.MaxValue, -float.MaxValue, -float.MaxValue };
            for (int i = 0; i < 290; ++i)
            {
                for (int a = 0; a < 3; ++a)
                {
                    lo[a] = Math.Min(lo[a], Teapot.Vertices[3 * i + a]);
                    hi[a] = Math.Max(hi[a], Teapot.Vertices[3 * i + a]);
                }
            }
            EXPECT_NEAR(-3.0, lo[0], 1e-6);
            EXPECT_NEAR(3.525, hi[0], 1e-6);
            EXPECT_NEAR(-2.0, lo[1], 1e-6);
            EXPECT_NEAR(2.0, hi[1], 1e-6);
            EXPECT_NEAR(0.0, lo[2], 1e-6);
            EXPECT_NEAR(3.15, hi[2], 1e-6);
            // the boundary curves (4 control points) of the patches: all but 16 (open edges: the rim under the lid, the end of the spout, the edge of the
            // lid) are shared by exactly two patches with identical control points, and 2 are degenerate (a pole)
            Dictionary<string, int> edges = new Dictionary<string, int>();
            int degenerate = 0;
            for (int patch = 0; patch < 32; ++patch)
            {
                for (int side = 0; side < 4; ++side)
                {
                    Point3f[] e = new Point3f[4];
                    for (int k = 0; k < 4; ++k)
                    {
                        e[k] = side == 0 ? Vertex(patch, 0, k) : side == 1 ? Vertex(patch, 3, k) : side == 2 ? Vertex(patch, k, 0) : Vertex(patch, k, 3);
                    }
                    bool pole = e[0].x == e[1].x && e[0].y == e[1].y && e[0].z == e[1].z && e[0].x == e[3].x && e[0].y == e[3].y && e[0].z == e[3].z;
                    if (pole)
                    {
                        ++degenerate;
                        continue;
                    }
                    string forward = string.Join(";", e), backward = string.Join(";", new Point3f[] { e[3], e[2], e[1], e[0] });
                    string key = string.CompareOrdinal(forward, backward) <= 0 ? forward : backward;
                    edges[key] = edges.ContainsKey(key) ? edges[key] + 1 : 1;
                }
            }
            int once = 0, twice = 0, other = 0;
            foreach (int count in edges.Values)
            {
                if (count == 1)
                {
                    ++once;
                }
                else if (count == 2)
                {
                    ++twice;
                }
                else
                {
                    ++other;
                }
            }
            EXPECT_EQ(0, other, "no edge is used more than twice");
            EXPECT_EQ(52, twice, "edges shared by two patches (identical control points)");
            EXPECT_EQ(16, once, "open edges");
            EXPECT_EQ(8, degenerate, "pole edges: 2 poles (lid top, bottom) of 4 patches each");
        }

        [TEST("Teapot", "ShapeCountsAndScale")]
        public static void ShapeCountsAndScale()
        {
            Transform id = new Transform();
            EXPECT_EQ(28, (int)Make(id).size(), "default: no bottom");
            EXPECT_EQ(32, (int)Make(id, Bool("bottom", true)).size(), "with the bottom");
            EXPECT_EQ(20, (int)Make(id, Bool("lid", false)).size(), "without the lid");
            ParsedParameter scale = new ParsedParameter(null);
            scale.type = "float";
            scale.name = "scale";
            scale.floats.push_back(2);
            pstd.vector<Shape> big = Make(id, scale);
            Bounds3f b = big[0].Bounds();
            for (int i = 1; i < (int)big.size(); ++i)
            {
                b = pbrt.Union(b, big[i].Bounds());
            }
            EXPECT_TRUE(b.pMax.z >= 2 * 3.15 - 0.05 && b.pMax.z < 2 * 3.15 + 0.5, "scale 2 doubles the height");
        }

        private static bool CastAny(pstd.vector<Shape> shapes, Ray ray, out double t, out SurfaceInteraction intr)
        {
            t = double.MaxValue;
            intr = default;
            bool any = false;
            for (int i = 0; i < (int)shapes.size(); ++i)
            {
                pstd.optional<ShapeIntersection> si = shapes[i].Intersect(ray);
                if (si.has_value() && si.value().tHit < t)
                {
                    t = si.value().tHit;
                    intr = si.value().intr;
                    any = true;
                }
            }
            return any;
        }

        [TEST("Teapot", "ExtentOrientationAndLeaks")]
        public static void ExtentOrientationAndLeaks()
        {
            Transform id = new Transform();
            pstd.vector<Shape> pot = Make(id, Bool("bottom", true));
            // the extent of the surface by rays along the axes
            double xMax = -1e9, xMin = 1e9, zMax = -1e9, yMax = -1e9;
            for (int i = 0; i <= 200; ++i)
            {
                for (int j = 0; j <= 200; ++j)
                {
                    double a = -2.2 + 4.4 * i / 200, b = 3.3 * j / 200;
                    double t;
                    SurfaceInteraction intr;
                    if (CastAny(pot, new Ray(new Point3f(10, (Float)a, (Float)b), new Vector3f(-1, 0, 0)), out t, out intr))
                    {
                        xMax = Math.Max(xMax, 10 - t);
                    }
                    if (CastAny(pot, new Ray(new Point3f(-10, (Float)a, (Float)b), new Vector3f(1, 0, 0)), out t, out intr))
                    {
                        xMin = Math.Min(xMin, -10 + t);
                    }
                    if (CastAny(pot, new Ray(new Point3f((Float)(a * 1.5), (Float)(b - 1.65), 10), new Vector3f(0, 0, -1)), out t, out intr))
                    {
                        zMax = Math.Max(zMax, 10 - t);
                    }
                    if (CastAny(pot, new Ray(new Point3f(0.3f, 10, (Float)b), new Vector3f(0, -1, 0)), out t, out intr))
                    {
                        yMax = Math.Max(yMax, 10 - t);
                    }
                }
            }
            EXPECT_NEAR(3.434, xMax, 0.01, "the tip of the spout");
            EXPECT_NEAR(-3.0, xMin, 0.01, "the handle");
            EXPECT_NEAR(3.15, zMax, 0.01, "the knob of the lid");
            EXPECT_TRUE(yMax > 1.8 && yMax <= 2.0001, "the body spans y to 2: " + yMax);
            // outward normals: horizontal rays at the body (below the spout and the handle) come from outside and see a normal that faces them
            int facing = 0, total = 0;
            for (int k = 0; k < 360; ++k)
            {
                double angle = k * Math.PI / 180;
                foreach (double z in new double[] { 0.4, 0.9 })
                {
                    Vector3f d = new Vector3f((Float)(-Math.Cos(angle)), (Float)(-Math.Sin(angle)), 0);
                    Ray ray = new Ray(new Point3f((Float)(10 * Math.Cos(angle)), (Float)(10 * Math.Sin(angle)), (Float)z), d);
                    if (CastAny(pot, ray, out double t, out SurfaceInteraction intr))
                    {
                        ++total;
                        if (pbrt.Dot(new Vector3f(intr.n.x, intr.n.y, intr.n.z), -d) > 0)
                        {
                            ++facing;
                        }
                    }
                }
            }
            EXPECT_GT(total, 700);
            EXPECT_EQ(total, facing, "body normals face the outside");
            // the top of the lid faces up, the underside of the bottom faces down
            CastAny(pot, new Ray(new Point3f(0.4f, 0.1f, 10), new Vector3f(0, 0, -1)), out double tl, out SurfaceInteraction lidHit);
            EXPECT_GT((double)lidHit.n.z, 0.5, "lid normal up");
            CastAny(pot, new Ray(new Point3f(0.4f, 0.1f, -10), new Vector3f(0, 0, 1)), out double tb, out SurfaceInteraction bottomHit);
            EXPECT_LT((double)bottomHit.n.z, -0.5, "bottom normal down");
            // no leaks: vertical rays inside the body cross the lid and the bottom; there are no gaps between the patches
            int misses = 0, rays = 0;
            for (int i = 0; i < 300; ++i)
            {
                for (int j = 0; j < 300; ++j)
                {
                    double x = -1.1 + 2.2 * (i + 0.37) / 300, y = -1.1 + 2.2 * (j + 0.61) / 300;
                    if (x * x + y * y > 1.0)
                    {
                        continue;
                    }
                    ++rays;
                    if (!CastAny(pot, new Ray(new Point3f((Float)x, (Float)y, 10), new Vector3f(0, 0, -1)), out double t, out SurfaceInteraction intr) ||
                        !CastAny(pot, new Ray(new Point3f((Float)x, (Float)y, -10), new Vector3f(0, 0, 1)), out t, out intr))
                    {
                        ++misses;
                    }
                }
            }
            EXPECT_GT(rays, 50000);
            EXPECT_EQ(0, misses, "rays through the middle of the pot that leak between patches");
            // horizontal rays through the wall at many heights along the body: always hit
            int bodyMisses = 0;
            for (int i = 0; i < 20000; ++i)
            {
                double angle = 2 * Math.PI * (i * 0.6180339887 % 1.0), z = 0.05 + 2.2 * ((i * 0.7548776662) % 1.0);
                Ray ray = new Ray(new Point3f((Float)(10 * Math.Cos(angle)), (Float)(10 * Math.Sin(angle)), (Float)z), new Vector3f((Float)(-Math.Cos(angle)), (Float)(-Math.Sin(angle)), 0));
                if (!CastAny(pot, ray, out double t, out SurfaceInteraction intr))
                {
                    ++bodyMisses;
                }
            }
            EXPECT_EQ(0, bodyMisses, "horizontal rays toward the axis that miss the pot");
        }
    }
}
