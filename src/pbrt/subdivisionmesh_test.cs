// (not in pbrt-v4) Tests of the subdivisionmesh adapter (shapes_subdivisionmesh.cs): the tags of RenderMan against the parameters of catmarksubdiv, and
// their effect on the surface (a crease keeps the edge, a corner keeps the vertex, a hole opens a face, interpolateboundary keeps the boundary).

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class SubdivisionMeshTests
    {
        private static ParsedParameter Param(string type, string name, double[] floats, int[] ints, string[] strings)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = type;
            p.name = name;
            if (floats != null)
            {
                foreach (double v in floats)
                {
                    p.floats.push_back((Float)v);
                }
            }
            if (ints != null)
            {
                foreach (int v in ints)
                {
                    p.ints.push_back(v);
                }
            }
            if (strings != null)
            {
                foreach (string v in strings)
                {
                    p.strings.push_back(v);
                }
            }
            return p;
        }

        private static readonly double[] CubePoints = { -1, -1, -1, 1, -1, -1, 1, 1, -1, -1, 1, -1, -1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1, 1 };
        // outward, counterclockwise seen from outside
        private static readonly int[] CubeFaces = { 0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7 };

        private static pstd.vector<Shape> Make(string name, List<ParsedParameter> list)
        {
            Transform id = new Transform();
            ParameterDictionary dict = new ParameterDictionary(list, RGBColorSpace.sRGB);
            return Shape.Create(name, id, id, false, dict, new Dictionary<string, FloatTexture>(), null, new Allocator());
        }

        private static double Area(pstd.vector<Shape> shapes)
        {
            double area = 0;
            for (int i = 0; i < (int)shapes.size(); ++i)
            {
                area += shapes[i].Area();
            }
            return area;
        }

        private static List<ParsedParameter> Cube(params ParsedParameter[] extra)
        {
            List<ParsedParameter> list = new List<ParsedParameter>();
            list.Add(Param("point3", "P", CubePoints, null, null));
            list.Add(Param("integer", "nvertices", null, new int[] { 4, 4, 4, 4, 4, 4 }, null));
            list.Add(Param("integer", "vertices", null, CubeFaces, null));
            list.AddRange(extra);
            return list;
        }

        private static double FirstHit(pstd.vector<Shape> shapes, Point3f o, Vector3f d)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < (int)shapes.size(); ++i)
            {
                pstd.optional<ShapeIntersection> si = shapes[i].Intersect(new Ray(o, d));
                if (si.has_value())
                {
                    best = Math.Min(best, si.value().tHit);
                }
            }
            return best;
        }

        [TEST("SubdivisionMesh", "TagsMatchCatmarkSubdivAndChangeTheSurface")]
        public static void TagsMatchCatmarkSubdivAndChangeTheSurface()
        {
            // no tags: the same surface as catmarksubdiv
            pstd.vector<Shape> smooth = Make("subdivisionmesh", Cube());
            List<ParsedParameter> direct = new List<ParsedParameter>();
            direct.Add(Param("point3", "P", CubePoints, null, null));
            direct.Add(Param("integer", "indices", null, CubeFaces, null));
            direct.Add(Param("integer", "faceVertexCounts", null, new int[] { 4, 4, 4, 4, 4, 4 }, null));
            pstd.vector<Shape> reference = Make("catmarksubdiv", direct);
            double smoothArea = Area(smooth);
            EXPECT_NEAR(1.0, smoothArea / Area(reference), 1e-6, "no tags against catmarksubdiv");
            EXPECT_LT(smoothArea, 24.0, "the smooth cube is smaller than the cube (24)");
            EXPECT_GT(smoothArea, 6.0, "but larger than its inscribed sphere (4 pi)");

            // a crease around the top face (chain of the closed loop 4 5 6 7 4), infinite sharpness
            pstd.vector<Shape> creased = Make("subdivisionmesh", Cube(Param("string", "tags", null, null, new string[] { "crease" }),
                                              Param("integer", "nargs", null, new int[] { 5, 1 }, null), Param("integer", "intargs", null, new int[] { 4, 5, 6, 7, 4 }, null),
                                              Param("float", "floatargs", new double[] { 1e30 }, null, null)));
            List<ParsedParameter> creasedDirect = new List<ParsedParameter>(direct);
            creasedDirect.Add(Param("integer", "creaseindices", null, new int[] { 4, 5, 5, 6, 6, 7, 7, 4 }, null));
            creasedDirect.Add(Param("float", "creaseweights", new double[] { 10, 10, 10, 10 }, null, null));
            EXPECT_NEAR(1.0, Area(creased) / Area(Make("catmarksubdiv", creasedDirect)), 1e-6, "crease against catmarksubdiv");
            EXPECT_GT(Area(creased), smoothArea * 1.01, "the crease keeps the cube sharper");
            // the crease: the top face stays flat, a ray down the z axis hits at z = 1 (smooth: below 1)
            double topSmooth = FirstHit(smooth, new Point3f(0.2f, 0.1f, 5), new Vector3f(0, 0, -1));
            double topCreased = FirstHit(creased, new Point3f(0.2f, 0.1f, 5), new Vector3f(0, 0, -1));
            EXPECT_GT(topSmooth, 4.0 + 0.02, "smooth cube top below the original face");
            EXPECT_NEAR(4.0, topCreased, 5e-2, "creased top near the original face");

            // a corner tag keeps the vertex 6 = (1, 1, 1) on the surface
            pstd.vector<Shape> cornered = Make("subdivisionmesh", Cube(Param("string", "tags", null, null, new string[] { "corner" }),
                                               Param("integer", "nargs", null, new int[] { 1, 1 }, null), Param("integer", "intargs", null, new int[] { 6 }, null),
                                               Param("float", "floatargs", new double[] { 1e30 }, null, null)));
                        List<ParsedParameter> corneredDirect = new List<ParsedParameter>(direct);
            corneredDirect.Add(Param("integer", "cornerindices", null, new int[] { 6 }, null));
            corneredDirect.Add(Param("float", "cornerweights", new double[] { 10 }, null, null));
            EXPECT_NEAR(1.0, Area(cornered) / Area(Make("catmarksubdiv", corneredDirect)), 1e-6, "corner against catmarksubdiv");
            // (that the limit surface of the corner reaches the vertex exactly is not verified: the patches around a sharp corner are end caps of the
            // isolation level)
            EXPECT_GT(Math.Abs(Area(cornered) - smoothArea), 1e-3, "the corner changes the surface");

            // a hole: removing the face 0 (z = -1) lets a ray from below see the inside of the opposite face
            pstd.vector<Shape> holed = Make("subdivisionmesh", Cube(Param("string", "tags", null, null, new string[] { "hole" }),
                                            Param("integer", "nargs", null, new int[] { 1, 0 }, null), Param("integer", "intargs", null, new int[] { 0 }, null)));
            double below = FirstHit(smooth, new Point3f(0.1f, 0.1f, -5), new Vector3f(0, 0, 1));
            double belowHole = FirstHit(holed, new Point3f(0.1f, 0.1f, -5), new Vector3f(0, 0, 1));
            EXPECT_GT(belowHole, below + 1.0, "with the hole the ray passes the removed face");
            EXPECT_LT(Area(holed), smoothArea, "area with a hole");

            // loop scheme is forwarded
            pstd.vector<Shape> loop = Make("subdivisionmesh", new List<ParsedParameter> { Param("string", "scheme", null, null, new string[] { "loop" }),
                                           Param("point3", "P", new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 }, null, null),
                                           Param("integer", "nvertices", null, new int[] { 3, 3, 3, 3 }, null),
                                           Param("integer", "vertices", null, new int[] { 0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3 }, null) });
            EXPECT_GT(loop.size(), 0);
        }

        [TEST("SubdivisionMesh", "InterpolateBoundary")]
        public static void InterpolateBoundary()
        {
            // a 5 x 5 grid of points (16 quads) on [-1, 1]^2, the middle raised
            double[] p = new double[75];
            for (int j = 0; j < 5; ++j)
            {
                for (int i = 0; i < 5; ++i)
                {
                    p[3 * (j * 5 + i)] = 0.5 * (i - 2);
                    p[3 * (j * 5 + i) + 1] = 0.5 * (j - 2);
                    p[3 * (j * 5 + i) + 2] = (i == 2 && j == 2) ? 0.5 : 0;
                }
            }
            int[] faces = new int[64];
            int[] sizes = new int[16];
            for (int j = 0; j < 4; ++j)
            {
                for (int i = 0; i < 4; ++i)
                {
                    int f = j * 4 + i;
                    sizes[f] = 4;
                    faces[4 * f] = j * 5 + i;
                    faces[4 * f + 1] = j * 5 + i + 1;
                    faces[4 * f + 2] = (j + 1) * 5 + i + 1;
                    faces[4 * f + 3] = (j + 1) * 5 + i;
                }
            }
            Func<bool, pstd.vector<Shape>> make = (bool interpolate) =>
            {
                List<ParsedParameter> list = new List<ParsedParameter> { Param("point3", "P", p, null, null), Param("integer", "nvertices", null, sizes, null),
                                                                          Param("integer", "vertices", null, faces, null) };
                if (interpolate)
                {
                    list.Add(Param("string", "tags", null, null, new string[] { "interpolateboundary" }));
                    list.Add(Param("integer", "nargs", null, new int[] { 0, 0 }, null));
                }
                return Make("subdivisionmesh", list);
            };
            pstd.vector<Shape> open = make(false), closed = make(true);
            Func<pstd.vector<Shape>, double> extent = (pstd.vector<Shape> shapes) =>
            {
                Bounds3f b = shapes[0].Bounds();
                for (int i = 1; i < (int)shapes.size(); ++i)
                {
                    b = Union(b, shapes[i].Bounds());
                }
                return b.pMax.x;
            };
            // with the boundary interpolated the surface reaches x = 1 (the bounds are padded a little), without it it falls short
            EXPECT_NEAR(1.0, extent(closed), 5e-2, "interpolated boundary");
            EXPECT_LT(extent(open), 0.8, "without the tag the faces of the boundary are not drawn");
            // the interpolated boundary: a ray at the middle of the edge x = 1 hits at z = 0 on the edge
            double edge = FirstHit(closed, new Point3f(0.9999f, 0.0f, 3), new Vector3f(0, 0, -1));
            EXPECT_NEAR(3.0, edge, 1e-2, "ray down at the boundary");
        }
    }
}  // namespace pbrt
