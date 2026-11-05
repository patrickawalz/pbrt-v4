// (not in pbrt-v4) Tests of the polygon primitives (shapes_polygons.cs): the triangulation of concave polygons with holes against the shoelace area
// and an even-odd point-in-polygon test, the ray intersection of the created shapes against that test, the winding, and the parser.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class PolygonTests
    {
        private sealed class TestPolygon
        {
            public List<Point3f> points = new List<Point3f>();
            public List<int[]> loops = new List<int[]>();
            public List<Point2f[]> loops2D = new List<Point2f[]>();
            public double area;
        }

        // A star-shaped (so concave) polygon around the origin with radii in [0.3, 1], and nHoles holes (small polygons around the points of a
        // fixed set inside radius 0.3). reverse reverses all the loops.
        private static TestPolygon MakePolygon(Random rng, int nOuter, int nHoles, bool reverse)
        {
            TestPolygon polygon = new TestPolygon();
            double[] angles = new double[nOuter];
            for (int i = 0; i < nOuter; ++i)
            {
                angles[i] = 2 * Math.PI * (i + 0.2 + 0.6 * rng.NextDouble()) / nOuter;
            }
            List<Point2f> outer = new List<Point2f>();
            for (int i = 0; i < nOuter; ++i)
            {
                double radius = 0.3 + 0.7 * rng.NextDouble();
                outer.Add(new Point2f((Float)(radius * Math.Cos(angles[i])), (Float)(radius * Math.Sin(angles[i]))));
            }
            AddLoop(polygon, outer, reverse);
            Point2f[] centers = { new Point2f(0.1f, 0.0f), new Point2f(-0.1f, 0.06f), new Point2f(0.0f, -0.12f) };
            for (int h = 0; h < nHoles; ++h)
            {
                List<Point2f> hole = new List<Point2f>();
                int n = 3 + h + rng.Next(3);
                for (int i = 0; i < n; ++i)
                {
                    double angle = -2 * Math.PI * (i + 0.1 + 0.8 * rng.NextDouble()) / n;  // clockwise: the opposite of the outer loop
                    double radius = 0.025 + 0.025 * rng.NextDouble();
                    hole.Add(new Point2f((Float)(centers[h].x + radius * Math.Cos(angle)), (Float)(centers[h].y + radius * Math.Sin(angle))));
                }
                if (HoleFits(outer, hole))
                {
                    AddLoop(polygon, hole, reverse);
                }
            }
            // the area: the outer loop (counterclockwise) minus the holes
            polygon.area = 0;
            for (int l = 0; l < polygon.loops2D.Count; ++l)
            {
                double a = Math.Abs(SignedArea(polygon.loops2D[l]));
                polygon.area += l == 0 ? a : -a;
            }
            return polygon;
        }

        // Whether the hole lies inside the outer loop without touching it: no edge of the hole meets an edge of the outer loop, and one of its
        // vertices is inside (the holes are disjoint by construction)
        private static bool HoleFits(List<Point2f> outer, List<Point2f> hole)
        {
            for (int i = 0; i < hole.Count; ++i)
            {
                Point2f a = hole[i], b = hole[(i + 1) % hole.Count];
                for (int j = 0; j < outer.Count; ++j)
                {
                    Point2f c = outer[j], d = outer[(j + 1) % outer.Count];
                    double d1 = Orient(a, b, c), d2 = Orient(a, b, d), d3 = Orient(c, d, a), d4 = Orient(c, d, b);
                    if ((d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0))
                    {
                        return false;
                    }
                }
            }
            bool inside = false;
            Point2f p = hole[0];
            for (int j = 0; j < outer.Count; ++j)
            {
                Point2f c = outer[j], d = outer[(j + 1) % outer.Count];
                if ((c.y > p.y) != (d.y > p.y) && p.x < c.x + (p.y - c.y) * (d.x - c.x) / (d.y - c.y))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static double Orient(Point2f a, Point2f b, Point2f c)
        {
            return ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);
        }

        private static void AddLoop(TestPolygon polygon, List<Point2f> loop, bool reverse)
        {
            if (reverse)
            {
                loop.Reverse();
            }
            int[] indices = new int[loop.Count];
            for (int i = 0; i < loop.Count; ++i)
            {
                indices[i] = polygon.points.Count;
                polygon.points.Add(new Point3f(loop[i].x, loop[i].y, 0));
            }
            polygon.loops.Add(indices);
            polygon.loops2D.Add(loop.ToArray());
        }

        private static double SignedArea(Point2f[] loop)
        {
            double area = 0;
            for (int i = 0; i < loop.Length; ++i)
            {
                Point2f a = loop[i], b = loop[(i + 1) % loop.Length];
                area += (double)a.x * b.y - (double)b.x * a.y;
            }
            return 0.5 * area;
        }

        // The even-odd rule over all the loops, and the distance to the nearest edge
        private static bool Inside(TestPolygon polygon, double x, double y, out double edgeDistance)
        {
            bool inside = false;
            edgeDistance = double.PositiveInfinity;
            foreach (Point2f[] loop in polygon.loops2D)
            {
                for (int i = 0; i < loop.Length; ++i)
                {
                    Point2f a = loop[i], b = loop[(i + 1) % loop.Length];
                    if ((a.y > y) != (b.y > y) && x < a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y))
                    {
                        inside = !inside;
                    }
                    double dx = b.x - a.x, dy = b.y - a.y;
                    double t = Math.Max(0, Math.Min(1, ((x - a.x) * dx + (y - a.y) * dy) / (dx * dx + dy * dy)));
                    edgeDistance = Math.Min(edgeDistance, Math.Sqrt(Math.Pow(x - a.x - t * dx, 2) + Math.Pow(y - a.y - t * dy, 2)));
                }
            }
            return inside;
        }

        [TEST("Polygon", "TriangulationTilesThePolygon")]
        public static void TriangulationTilesThePolygon()
        {
            Random rng = new Random(11);
            int checkedPolygons = 0;
            for (int trial = 0; trial < 3000; ++trial)
            {
                int nOuter = 3 + rng.Next(18);
                int nHoles = rng.Next(4);
                bool reverse = trial % 2 == 1;
                TestPolygon polygon = MakePolygon(rng, nOuter, nHoles, reverse);
                List<int> triangles = PolygonTriangulator.Triangulate(polygon.points, polygon.loops);
                string label = "polygon " + trial + " (" + nOuter + " vertices, " + nHoles + " holes, reversed " + reverse + ")";
                // the total area is the area of the polygon, every triangle winds like the outer loop
                double total = 0;
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    Point3f a = polygon.points[triangles[t]], b = polygon.points[triangles[t + 1]], c = polygon.points[triangles[t + 2]];
                    double signed = 0.5 * (((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x));
                    total += Math.Abs(signed);
                    EXPECT_TRUE(reverse ? signed < 0 : signed > 0, label + " triangle winding");
                }
                EXPECT_NEAR(polygon.area, total, 1e-5, label + " area of the triangles");
                // every point of the plane is in exactly one triangle if it is inside the polygon and in none if it is outside
                int wrong = 0;
                for (int k = 0; k < 400; ++k)
                {
                    double x = -1.1 + 2.2 * rng.NextDouble(), y = -1.1 + 2.2 * rng.NextDouble();
                    double edgeDistance;
                    bool inside = Inside(polygon, x, y, out edgeDistance);
                    if (edgeDistance < 1e-3)
                    {
                        continue;
                    }
                    int count = 0;
                    for (int t = 0; t < triangles.Count; t += 3)
                    {
                        Point3f a = polygon.points[triangles[t]], b = polygon.points[triangles[t + 1]], c = polygon.points[triangles[t + 2]];
                        double d0 = ((double)b.x - a.x) * (y - a.y) - ((double)b.y - a.y) * (x - a.x);
                        double d1 = ((double)c.x - b.x) * (y - b.y) - ((double)c.y - b.y) * (x - b.x);
                        double d2 = ((double)a.x - c.x) * (y - c.y) - ((double)a.y - c.y) * (x - c.x);
                        if ((d0 >= 0 && d1 >= 0 && d2 >= 0) || (d0 <= 0 && d1 <= 0 && d2 <= 0))
                        {
                            ++count;
                        }
                    }
                    if (count != (inside ? 1 : 0))
                    {
                        ++wrong;
                    }
                }
                EXPECT_EQ(0, wrong, label + " points covered other than once inside or zero times outside");
                ++checkedPolygons;
            }
            EXPECT_EQ(3000, checkedPolygons);
        }

        private static ParsedParameter Param(string type, string name, Float[] floats, int[] ints)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = type;
            p.name = name;
            if (floats != null)
            {
                foreach (Float v in floats)
                {
                    p.floats.push_back(v);
                }
            }
            if (ints != null)
            {
                foreach (int v in ints)
                {
                    p.ints.push_back(v);
                }
            }
            return p;
        }

        private static Float[] Flatten(List<Point3f> points)
        {
            Float[] values = new Float[3 * points.Count];
            for (int i = 0; i < points.Count; ++i)
            {
                values[3 * i] = points[i].x;
                values[3 * i + 1] = points[i].y;
                values[3 * i + 2] = points[i].z;
            }
            return values;
        }

        [TEST("Polygon", "GeneralPolygonRaysMatchPointInPolygon")]
        public static void GeneralPolygonRaysMatchPointInPolygon()
        {
            Random rng = new Random(5);
            Transform[] transforms = { new Transform(), Translate(new Vector3f(0.7f, -0.4f, 1.1f)) * Rotate(37, Normalize(new Vector3f(1, 2, -0.5f))) };
            foreach (Transform t in transforms)
            {
                for (int trial = 0; trial < 60; ++trial)
                {
                    TestPolygon polygon = MakePolygon(rng, 4 + rng.Next(14), rng.Next(4), trial % 2 == 1);
                    List<int> sizes = new List<int>();
                    foreach (int[] loop in polygon.loops)
                    {
                        sizes.Add(loop.Length);
                    }
                    List<ParsedParameter> list = new List<ParsedParameter>();
                    list.Add(Param("point3", "P", Flatten(polygon.points), null));
                    list.Add(Param("integer", "nvertices", null, sizes.ToArray()));
                    ParameterDictionary dict = new ParameterDictionary(list, RGBColorSpace.sRGB);
                    pstd.vector<Shape> shapes = Shape.Create("generalpolygon", t, Inverse(t), false, dict, new Dictionary<string, FloatTexture>(), null, new Allocator());
                    ASSERT_TRUE(shapes.size() > 0);
                    int wrong = 0, wrongNormal = 0, hits = 0;
                    Vector3f down = t.Apply(new Vector3f(0, 0, -1));
                    Vector3f up = t.Apply(new Vector3f(0, 0, 1));
                    for (int k = 0; k < 500; ++k)
                    {
                        double x = -1.1 + 2.2 * rng.NextDouble(), y = -1.1 + 2.2 * rng.NextDouble();
                        double edgeDistance;
                        bool inside = Inside(polygon, x, y, out edgeDistance);
                        if (edgeDistance < 2e-3)
                        {
                            continue;
                        }
                        Ray ray = new Ray(t.Apply(new Point3f((Float)x, (Float)y, 1)), down);
                        int count = 0;
                        Float nz = 0;
                        for (int s = 0; s < (int)shapes.size(); ++s)
                        {
                            pstd.optional<ShapeIntersection> si = shapes[s].Intersect(ray);
                            if (si.has_value())
                            {
                                ++count;
                                nz = Dot((Vector3f)si.value().intr.n, up);
                            }
                        }
                        if (count != (inside ? 1 : 0))
                        {
                            ++wrong;
                        }
                        else if (inside)
                        {
                            ++hits;
                            // counterclockwise outer loop: the normal is +z, reversed loops: -z
                            if (Math.Abs(nz - (trial % 2 == 1 ? -1 : 1)) > 1e-3)
                            {
                                ++wrongNormal;
                            }
                        }
                    }
                    EXPECT_EQ(0, wrong, "trial " + trial + " rays that hit other than inside the polygon");
                    EXPECT_EQ(0, wrongNormal, "trial " + trial + " normals against the winding");
                    EXPECT_GT(hits, 10, "trial " + trial);
                }
            }
        }

        [TEST("Polygon", "SamplesLieInThePolygonAndAreasAdd")]
        public static void SamplesLieInThePolygonAndAreasAdd()
        {
            Random rng = new Random(9);
            for (int trial = 0; trial < 40; ++trial)
            {
                TestPolygon polygon = MakePolygon(rng, 5 + rng.Next(12), 1 + rng.Next(3), false);
                List<int> sizes = new List<int>();
                foreach (int[] loop in polygon.loops)
                {
                    sizes.Add(loop.Length);
                }
                List<ParsedParameter> list = new List<ParsedParameter>();
                list.Add(Param("point3", "P", Flatten(polygon.points), null));
                list.Add(Param("integer", "nvertices", null, sizes.ToArray()));
                Transform id = new Transform();
                ParameterDictionary dict = new ParameterDictionary(list, RGBColorSpace.sRGB);
                pstd.vector<Shape> shapes = Shape.Create("generalpolygon", id, id, false, dict, new Dictionary<string, FloatTexture>(), null, new Allocator());
                double area = 0;
                for (int s = 0; s < (int)shapes.size(); ++s)
                {
                    area += shapes[s].Area();
                }
                EXPECT_NEAR(polygon.area, area, 1e-5, "trial " + trial + " area");
                int outside = 0;
                for (int k = 0; k < 400; ++k)
                {
                    Shape shape = shapes[rng.Next((int)shapes.size())];
                    pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                    ASSERT_TRUE(ss.has_value());
                    Point3f p = ss.value().intr.p();
                    double edgeDistance;
                    if (!Inside(polygon, p.x, p.y, out edgeDistance) && edgeDistance > 1e-4)
                    {
                        ++outside;
                    }
                }
                EXPECT_EQ(0, outside, "trial " + trial + " samples outside the polygon");
            }
        }

        [TEST("Polygon", "PointsPolygonsAndTheParser")]
        public static void PointsPolygonsAndTheParser()
        {
            Transform id = new Transform();
            Func<string, List<ParsedParameter>, pstd.vector<Shape>> create = (string name, List<ParsedParameter> list) =>
            {
                ParameterDictionary dict = new ParameterDictionary(list, RGBColorSpace.sRGB);
                return Shape.Create(name, id, id, false, dict, new Dictionary<string, FloatTexture>(), null, new Allocator());
            };
            // a unit square polygon, with st texture coordinates
            Float[] square = { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0 };
            Float[] st = { 0, 0, 1, 0, 1, 1, 0, 1 };
            pstd.vector<Shape> polygon = create("polygon", new List<ParsedParameter> { Param("point3", "P", square, null), Param("point2", "st", st, null) });
            EXPECT_EQ(2, (int)polygon.size());
            double area = 0;
            for (int s = 0; s < (int)polygon.size(); ++s)
            {
                area += polygon[s].Area();
            }
            EXPECT_NEAR(1.0, area, 1e-6);
            // the uv of a hit follows st
            pstd.optional<ShapeIntersection> hit = default;
            for (int s = 0; s < (int)polygon.size() && !hit.has_value(); ++s)
            {
                hit = polygon[s].Intersect(new Ray(new Point3f(0.3f, 0.6f, 1), new Vector3f(0, 0, -1)));
            }
            ASSERT_TRUE(hit.has_value());
            EXPECT_NEAR(0.3, hit.value().intr.uv.x, 1e-5);
            EXPECT_NEAR(0.6, hit.value().intr.uv.y, 1e-5);

            // two quads and a triangle sharing the vertices
            Float[] p = { 0, 0, 0, 1, 0, 0, 2, 0, 0, 0, 1, 0, 1, 1, 0, 2, 1, 0 };
            pstd.vector<Shape> mesh = create("pointspolygons", new List<ParsedParameter> { Param("point3", "P", p, null),
                                             Param("integer", "nvertices", null, new int[] { 4, 3 }), Param("integer", "vertices", null, new int[] { 0, 1, 4, 3, 1, 2, 5 }) });
            area = 0;
            for (int s = 0; s < (int)mesh.size(); ++s)
            {
                area += mesh[s].Area();
            }
            EXPECT_EQ(3, (int)mesh.size());
            EXPECT_NEAR(1.5, area, 1e-6);

            // a general polygon of the mesh: a square with a square hole (area 4 - 1), and a second one without a hole
            Float[] q = { 0, 0, 0, 3, 0, 0, 3, 3, 0, 0, 3, 0, 1, 1, 0, 1, 2, 0, 2, 2, 0, 2, 1, 0, 5, 0, 0, 6, 0, 0, 6, 1, 0 };
            pstd.vector<Shape> general = create("pointsgeneralpolygons", new List<ParsedParameter> { Param("point3", "P", q, null),
                                                Param("integer", "nloops", null, new int[] { 2, 1 }), Param("integer", "nvertices", null, new int[] { 4, 4, 3 }),
                                                Param("integer", "vertices", null, new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }) });
            area = 0;
            for (int s = 0; s < (int)general.size(); ++s)
            {
                area += general[s].Area();
            }
            EXPECT_NEAR(8.5, area, 1e-5);
            // a ray through the hole misses, one beside it hits
            bool throughHole = false, besideHole = false;
            for (int s = 0; s < (int)general.size(); ++s)
            {
                throughHole |= general[s].IntersectP(new Ray(new Point3f(1.5f, 1.5f, 1), new Vector3f(0, 0, -1)));
                besideHole |= general[s].IntersectP(new Ray(new Point3f(0.5f, 1.5f, 1), new Vector3f(0, 0, -1)));
            }
            EXPECT_TRUE(!throughHole);
            EXPECT_TRUE(besideHole);
        }
    }
}  // namespace pbrt
