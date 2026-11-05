// (not in pbrt-v4) Tests of the points primitive (shapes_points.cs)

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class PointsTests
    {
        private static ParsedParameter Param(string type, string name, Float[] floats, string[] strings)
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
            if (strings != null)
            {
                foreach (string v in strings)
                {
                    p.strings.push_back(v);
                }
            }
            return p;
        }

        private static pstd.vector<Shape> Make(Transform t, List<ParsedParameter> list)
        {
            ParameterDictionary dict = new ParameterDictionary(list, RGBColorSpace.sRGB);
            return Shape.Create("points", t, Inverse(t), false, dict, new Dictionary<string, FloatTexture>(), null, new Allocator());
        }

        [TEST("Points", "SpheresAndDisks")]
        public static void SpheresAndDisks()
        {
            Float[] p = { 0, 0, 0, 3, 0, 0, 0, 3, 1, -2, -2, -2 };
            Float[] widths = { 1, 2, 0.5f, 3 };
            Transform t = Translate(new Vector3f(1, 2, 3)) * Rotate(40, Normalize(new Vector3f(1, 1, 0)));
            pstd.vector<Shape> spheres = Make(t, new List<ParsedParameter> { Param("point3", "P", p, null), Param("float", "width", widths, null) });
            ASSERT_EQ(4, (int)spheres.size());
            double expectedArea = 0;
            foreach (Float w in widths)
            {
                expectedArea += Math.PI * w * w;
            }
            double area = 0;
            for (int i = 0; i < 4; ++i)
            {
                area += spheres[i].Area();
            }
            EXPECT_NEAR(1.0, area / expectedArea, 1e-5, "total area of the spheres");
            // a ray through the center of the second point hits at its near surface
            Point3f c = t.Apply(new Point3f(3, 0, 0));
            Vector3f d = Normalize(t.Apply(new Vector3f(0, 0, -1)));
            pstd.optional<ShapeIntersection> hit = spheres[1].Intersect(new Ray(c - 5 * d, d));
            ASSERT_TRUE(hit.has_value());
            EXPECT_NEAR(4.0, hit.value().tHit, 1e-4, "distance to a sphere of radius 1");

            pstd.vector<Shape> constant = Make(new Transform(), new List<ParsedParameter> { Param("point3", "P", p, null), Param("float", "constantwidth", new Float[] { 0.4f }, null),
                                                                                           Param("string", "type", null, new string[] { "disk" }) });
            ASSERT_EQ(4, (int)constant.size());
            EXPECT_NEAR(Math.PI * 0.04, constant[2].Area(), 1e-5, "disk of width 0.4");
            // the disks face +z: a ray down +z through the center hits
            EXPECT_TRUE(constant[1].IntersectP(new Ray(new Point3f(3, 0, 5), new Vector3f(0, 0, -1))));
            // with normals
            Float[] n = { 1, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            pstd.vector<Shape> oriented = Make(new Transform(), new List<ParsedParameter> { Param("point3", "P", p, null), Param("normal", "N", n, null),
                                                                                           Param("float", "constantradius", new Float[] { 0.5f }, null), Param("string", "type", null, new string[] { "disk" }) });
            EXPECT_TRUE(oriented[1].IntersectP(new Ray(new Point3f(8, 0, 0), new Vector3f(-1, 0, 0))), "disk with the normal +x");
            EXPECT_TRUE(!oriented[1].IntersectP(new Ray(new Point3f(3, 0, 5), new Vector3f(0, 0, -1))), "that disk edge-on from above");
            EXPECT_TRUE(oriented[2].IntersectP(new Ray(new Point3f(0, 8, 1), new Vector3f(0, -1, 0))), "disk with the normal +y");
        }
    }
}  // namespace pbrt
