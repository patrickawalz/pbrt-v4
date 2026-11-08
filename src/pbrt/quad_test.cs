// (not in pbrt-v4) quad_test.cs
//
// Tests of the Quad shape (shapes_quad.cs): the closed-form (u,v) solve, the test that decides whether a quadrilateral is a Quad, the ray intersection against an
// independent plane-and-polygon computation, the watertightness of a mesh of quads (rays aimed at the shared edges, diagonals and vertices), the sampling by area
// and by solid angle against a quadrature, and the point on a quad light.

using System;
using System.Collections.Generic;
using System.Globalization;
using testing;

namespace pbrt
{
    public static unsafe class QuadTests
    {
        private static Point3f[] RandomFrame(Random random, out Vector3f ex, out Vector3f ey, out Vector3f ez)
        {
            Vector3f a = RandomDirection(random);
            Vector3f helper = Math.Abs(a.x) < 0.9f ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0);
            ex = pbrt.Normalize(pbrt.Cross(helper, a));
            ey = pbrt.Cross(a, ex);
            ez = a;
            return null;
        }

        internal static Vector3f RandomDirection(Random random)
        {
            double z = 1 - 2 * random.NextDouble();
            double r = Math.Sqrt(Math.Max(0, 1 - z * z));
            double phi = 2 * Math.PI * random.NextDouble();
            return new Vector3f((Float)(r * Math.Cos(phi)), (Float)(r * Math.Sin(phi)), (Float)z);
        }

        // Four points of a random convex quadrilateral in a random plane, counterclockwise around ez, as the points of a polygon around a center
        internal static Point3f[] RandomQuad(Random random, Point3f center, Float size, out Vector3f ez)
        {
            while (true)
            {
                Vector3f ex, ey;
                RandomFrame(random, out ex, out ey, out ez);
                Point3f[] p = new Point3f[4];
                double offset = random.NextDouble() * 2 * Math.PI;
                for (int i = 0; i < 4; ++i)
                {
                    double angle = offset + (i + 0.25 * (random.NextDouble() - 0.5)) * Math.PI / 2;
                    double radius = size * (0.6 + 0.8 * random.NextDouble());
                    p[i] = center + (Float)(radius * Math.Cos(angle)) * ex + (Float)(radius * Math.Sin(angle)) * ey;
                }
                int rotation;
                if (Quad.Classify(p[0], p[1], p[2], p[3], out rotation))
                {
                    return p;
                }
            }
        }

        private static Quad MakeQuad(Point3f[] p, Point2f[] uv, bool projected = false)
        {
            pstd.vector<Shape> shapes = Quad.CreateShapes(new Transform(), false, new List<int> { 0, 1, 2, 3 }, new List<Point3f>(p), new List<Normal3f>(),
                                                          uv != null ? new List<Point2f>(uv) : new List<Point2f>(), new List<int>(), projected, new Allocator());
            EXPECT_EQ(1, (int)shapes.size());
            return shapes[0] as Quad;
        }

        // The point (u,v) of the bilinear patch of four corners in order around the patch (corner 0 at (0,0), 1 at (1,0), 2 at (1,1), 3 at (0,1))
        private static double[] Bilinear(Point3f[] c, double u, double v)
        {
            double[] result = new double[3];
            for (int k = 0; k < 3; ++k)
            {
                result[k] = (1 - u) * (1 - v) * c[0][k] + u * (1 - v) * c[1][k] + u * v * c[2][k] + (1 - u) * v * c[3][k];
            }
            return result;
        }

        private static double Distance(double[] a, Point3f b)
        {
            return Math.Sqrt((a[0] - b.x) * (a[0] - b.x) + (a[1] - b.y) * (a[1] - b.y) + (a[2] - b.z) * (a[2] - b.z));
        }

        // The solve of (u,v) from the coordinates in the frame of the quad returns the (u,v) it started from, for quads of every shape, including those whose b11 is 1
        [TEST("Quad", "BilinearSolveRoundTrip")]
        public static void Quad_BilinearSolveRoundTrip()
        {
            Random random = new Random(5);
            Vector3f ez;
            int count = 0;
            for (int trial = 0; trial < 400; ++trial)
            {
                Point3f[] p = RandomQuad(random, new Point3f(0, 0, 0), 1, out ez);
                if (trial % 4 == 1)
                {
                    // a parallelogram: the fourth corner is p1 + p3 - p0
                    p[2] = p[1] + (p[3] - p[0]);
                }
                else if (trial % 4 == 2)
                {
                    // a trapezoid: p3 - p0 and p2 - p1 are parallel
                    p[2] = p[1] + 0.5f * (p[3] - p[0]);
                }
                int rotation;
                if (!Quad.Classify(p[0], p[1], p[2], p[3], out rotation))
                {
                    continue;
                }
                double a11, b11;
                Quad.ComputeV11Coordinates(p[0], p[1], p[2], p[3], out a11, out b11);
                for (int i = 0; i < 50; ++i)
                {
                    double u = random.NextDouble(), v = random.NextDouble();
                    // the coordinates (alpha, beta) of the point in the frame: p - p0 = alpha (p1 - p0) + beta (p3 - p0)
                    double alpha = u * (1 + v * (a11 - 1)), beta = v * (1 + u * (b11 - 1));
                    double u2, v2;
                    Quad.SolveBilinear(a11, b11, alpha, beta, out u2, out v2);
                    EXPECT_NEAR(u, u2, 2e-6);
                    EXPECT_NEAR(v, v2, 2e-6);
                    ++count;
                }
            }
            EXPECT_GT(count, 5000);
        }

        // Which quadrilaterals are Quads, and which are two triangles
        [TEST("Quad", "ClassifyAcceptsPlanarConvexOnly")]
        public static void Quad_ClassifyAcceptsPlanarConvexOnly()
        {
            int rotation;
            EXPECT_TRUE(Quad.Classify(new Point3f(0, 0, 0), new Point3f(1, 0, 0), new Point3f(1, 1, 0), new Point3f(0, 1, 0), out rotation));
            EXPECT_EQ(0, rotation);
            // a long thin trapezoid and a large coordinate offset
            EXPECT_TRUE(Quad.Classify(new Point3f(1000, 1000, 1000), new Point3f(1010, 1000, 1000), new Point3f(1008, 1001, 1000), new Point3f(1002, 1001, 1000), out rotation));
            // a quad given clockwise is still a quad (its normal points the other way)
            EXPECT_TRUE(Quad.Classify(new Point3f(0, 0, 0), new Point3f(0, 1, 0), new Point3f(1, 1, 0), new Point3f(1, 0, 0), out rotation));
            // not planar
            EXPECT_FALSE(Quad.Classify(new Point3f(0, 0, 0), new Point3f(1, 0, 0), new Point3f(1, 1, 0.2f), new Point3f(0, 1, 0), out rotation));
            // concave (the corner p2 is inside the triangle p3 p0 p1)
            EXPECT_FALSE(Quad.Classify(new Point3f(0, 0, 0), new Point3f(1, 0, 0), new Point3f(0.2f, 0.2f, 0), new Point3f(0, 1, 0), out rotation));
            // a bow tie
            EXPECT_FALSE(Quad.Classify(new Point3f(0, 0, 0), new Point3f(1, 1, 0), new Point3f(1, 0, 0), new Point3f(0, 1, 0), out rotation));
            // two coincident corners (a triangle) and a collinear corner
            EXPECT_FALSE(Quad.Classify(new Point3f(0, 0, 0), new Point3f(0, 0, 0), new Point3f(1, 0, 0), new Point3f(0, 1, 0), out rotation));
            EXPECT_FALSE(Quad.Classify(new Point3f(0, 0, 0), new Point3f(0.5f, 0, 0), new Point3f(1, 0, 0), new Point3f(0, 1, 0), out rotation));

            // a mesh creates a Quad for the first and two Triangles for the second
            List<Point3f> points = new List<Point3f> { new Point3f(0, 0, 0), new Point3f(1, 0, 0), new Point3f(1, 1, 0), new Point3f(0, 1, 0), new Point3f(2, 0, 0),
                                                       new Point3f(2, 1, 0.3f) };
            pstd.vector<Shape> shapes = Quad.CreateShapes(new Transform(), false, new List<int> { 0, 1, 2, 3, 1, 4, 5, 2 }, points, new List<Normal3f>(),
                                                          new List<Point2f>(), new List<int>(), false, new Allocator());
            int quads = 0, triangles = 0;
            for (int i = 0; i < (int)shapes.size(); ++i)
            {
                quads += shapes[i] is Quad ? 1 : 0;
                triangles += shapes[i] is Triangle ? 1 : 0;
            }
            EXPECT_EQ(1, quads);
            EXPECT_EQ(2, triangles);
        }

        // Rays against quads: the hit, the distance and the (u,v) agree with the plane of the quad, the polygon in it and the bilinear surface
        [TEST("Quad", "IntersectionMatchesPlaneAndPolygon")]
        public static void Quad_IntersectionMatchesPlaneAndPolygon()
        {
            Random random = new Random(21);
            int hits = 0, misses = 0, compared = 0;
            for (int trial = 0; trial < 300; ++trial)
            {
                Vector3f ez;
                Point3f center = new Point3f((Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2));
                Point3f[] p = RandomQuad(random, center, (Float)(0.1 + random.NextDouble() * 3), out ez);
                // the texture coordinates of the corners: a skewed affine map, the same in every rotation
                Point2f[] uv = trial % 2 == 0 ? null : new Point2f[] { new Point2f(0.1f, 0.2f), new Point2f(0.9f, 0.3f), new Point2f(1.1f, 0.8f), new Point2f(0.0f, 1.0f) };
                Quad quad = MakeQuad(p, uv);
                Vector3f normal = pbrt.Normalize(pbrt.Cross(p[2] - p[0], p[3] - p[1]));
                double size = pbrt.Length(p[2] - p[0]);

                for (int i = 0; i < 400; ++i)
                {
                    // a ray toward a point in the plane, around the quad
                    Vector3f toward = (Float)(random.NextDouble() * 3 - 1.5) * (p[1] - p[0]) + (Float)(random.NextDouble() * 3 - 1.5) * (p[3] - p[0]);
                    Point3f target = p[0] + (Vector3f)toward + (Vector3f)((p[2] - p[0]) * 0.3f);
                    Vector3f offset = RandomDirection(random) * (Float)(0.3 + random.NextDouble() * 4);
                    Point3f origin = target + offset;
                    Vector3f d = pbrt.Normalize(target - origin);
                    double cosine = Math.Abs(pbrt.Dot(d, normal));
                    if (cosine < 0.05)
                    {
                        continue;
                    }

                    // the polygon test in double precision: the plane hit and the signs of the turns
                    double t = pbrt.Dot(normal, p[0] - origin) / pbrt.Dot(normal, d);
                    double[] q = { origin.x + t * d.x, origin.y + t * d.y, origin.z + t * d.z };
                    double minTurn = double.MaxValue;
                    for (int k = 0; k < 4; ++k)
                    {
                        Point3f a = p[k], b = p[(k + 1) & 3];
                        double ex = b.x - a.x, ey = b.y - a.y, ez2 = b.z - a.z;
                        double rx = q[0] - a.x, ry = q[1] - a.y, rz = q[2] - a.z;
                        double cx = ey * rz - ez2 * ry, cy = ez2 * rx - ex * rz, cz = ex * ry - ey * rx;
                        double distance = (cx * normal.x + cy * normal.y + cz * normal.z) / Math.Sqrt(ex * ex + ey * ey + ez2 * ez2);
                        minTurn = Math.Min(minTurn, distance);
                    }
                    if (t <= 1e-3 || Math.Abs(minTurn) < 1e-4 * size)
                    {
                        continue;
                    }
                    bool expectHit = minTurn > 0;
                    Ray ray = new Ray(origin, d);
                    pstd.optional<ShapeIntersection> isect = quad.Intersect(ray);
                    EXPECT_EQ(expectHit, isect.has_value());
                    EXPECT_EQ(expectHit, quad.IntersectP(ray));
                    ++compared;
                    if (!expectHit || !isect.has_value())
                    {
                        ++misses;
                        continue;
                    }
                    ++hits;
                    EXPECT_NEAR(t, isect.value().tHit, 1e-4 * (1 + t));
                    EXPECT_LT(Distance(q, isect.value().intr.p()), 2e-5 * (size + 1));
                    // (the quad was given with uv or without: the hit point is the bilinear point of the st of the hit, in either case)
                    Point2f st = isect.value().intr.uv;
                    if (uv == null)
                    {
                        EXPECT_LT(Distance(Bilinear(p, st[0], st[1]), isect.value().intr.p()), 2e-5 * (size + 1));
                    }
                    else
                    {
                        // st is the bilinear interpolation of the corner uvs at (u,v): find (u,v) by Newton from the hit point
                        double u = 0.5, v = 0.5;
                        for (int it = 0; it < 30; ++it)
                        {
                            double[] f = Bilinear(p, u, v);
                            double[] fu = Bilinear(p, u + 1e-6, v), fv = Bilinear(p, u, v + 1e-6);
                            double[] r = { isect.value().intr.p().x - f[0], isect.value().intr.p().y - f[1], isect.value().intr.p().z - f[2] };
                            double[] du = { (fu[0] - f[0]) / 1e-6, (fu[1] - f[1]) / 1e-6, (fu[2] - f[2]) / 1e-6 };
                            double[] dv = { (fv[0] - f[0]) / 1e-6, (fv[1] - f[1]) / 1e-6, (fv[2] - f[2]) / 1e-6 };
                            double a = du[0] * du[0] + du[1] * du[1] + du[2] * du[2], b = du[0] * dv[0] + du[1] * dv[1] + du[2] * dv[2];
                            double c = dv[0] * dv[0] + dv[1] * dv[1] + dv[2] * dv[2];
                            double e = du[0] * r[0] + du[1] * r[1] + du[2] * r[2], g = dv[0] * r[0] + dv[1] * r[1] + dv[2] * r[2];
                            double det = a * c - b * b;
                            u += (c * e - b * g) / det;
                            v += (a * g - b * e) / det;
                        }
                        double[] expected = new double[2];
                        for (int k = 0; k < 2; ++k)
                        {
                            expected[k] = (1 - u) * (1 - v) * uv[0][k] + u * (1 - v) * uv[1][k] + u * v * uv[2][k] + (1 - u) * v * uv[3][k];
                        }
                        EXPECT_NEAR(expected[0], st[0], 1e-4);
                        EXPECT_NEAR(expected[1], st[1], 1e-4);
                    }
                    // the normal is that of the triangles of the quad in the same order
                    EXPECT_GT(pbrt.Dot((Vector3f)isect.value().intr.n, normal), 0.999f);
                }
            }
            EXPECT_GT(hits, 5000);
            EXPECT_GT(misses, 2000);
        }

        // A grid of quads in one plane (the plane, the jitter and the rotation are random, the points are single precision numbers, so the quads are planar to
        // single precision): rays aimed at points of the shared sides, of the diagonals and at the vertices pass through at least one quad of the neighbors,
        // and the same for a grid of Triangles, for comparison
        [TEST("Quad", "WatertightAtSharedEdgesAndVertices")]
        public static void Quad_WatertightAtSharedEdgesAndVertices()
        {
            Random random = new Random(99);
            const int n = 12;
            int totalRays = 0, quadMisses = 0, triangleMisses = 0, grazingRays = 0, grazingQuadMisses = 0, grazingTriangleMisses = 0;
            for (int grid = 0; grid < 8; ++grid)
            {
                Vector3f ex, ey, ez;
                RandomFrame(random, out ex, out ey, out ez);
                Point3f origin = new Point3f((Float)(random.NextDouble() * 6 - 3), (Float)(random.NextDouble() * 6 - 3), (Float)(random.NextDouble() * 6 - 3));
                Float scale = grid % 2 == 0 ? 1 : 0.01f;
                Point3f[] vertex = new Point3f[(n + 1) * (n + 1)];
                for (int j = 0; j <= n; ++j)
                {
                    for (int i = 0; i <= n; ++i)
                    {
                        double jitterX = (i == 0 || i == n) ? 0 : 0.3 * (random.NextDouble() - 0.5);
                        double jitterY = (j == 0 || j == n) ? 0 : 0.3 * (random.NextDouble() - 0.5);
                        vertex[j * (n + 1) + i] = origin + (Float)((i + jitterX) * scale) * ex + (Float)((j + jitterY) * scale) * ey;
                    }
                }
                List<int> around = new List<int>();
                List<int> triangleIndices = new List<int>();
                for (int j = 0; j < n; ++j)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        int v0 = j * (n + 1) + i, v1 = v0 + 1, v2 = v1 + n + 1, v3 = v0 + n + 1;
                        around.AddRange(new int[] { v0, v1, v2, v3 });
                        triangleIndices.AddRange(new int[] { v0, v1, v2, v0, v2, v3 });
                    }
                }
                pstd.vector<Shape> quads = Quad.CreateShapes(new Transform(), false, around, new List<Point3f>(vertex), new List<Normal3f>(), new List<Point2f>(), new List<int>(),
                                                             false, new Allocator());
                int quadCount = 0;
                for (int i = 0; i < (int)quads.size(); ++i)
                {
                    quadCount += quads[i] is Quad ? 1 : 0;
                }
                EXPECT_EQ(n * n, quadCount);
                TriangleMesh mesh = new TriangleMesh(new Transform(), false, new pstd.vector<int>(triangleIndices), new pstd.vector<Point3f>(vertex),
                                                     new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(), new pstd.vector<int>(), new Allocator());
                pstd.vector<Shape> triangles = Triangle.CreateTriangles(mesh, new Allocator());

                for (int ray = 0; ray < 400000; ++ray)
                {
                    // a target on a side, a diagonal or at a vertex; the shapes to test are those that touch it
                    int i = random.Next(1, n - 1), j = random.Next(1, n - 1);
                    int kind = random.Next(4);
                    Point3f a = vertex[j * (n + 1) + i], b;
                    Float s = kind == 3 ? 0 : (Float)random.NextDouble();
                    if (kind == 0)
                    {
                        b = vertex[j * (n + 1) + i + 1];
                    }
                    else if (kind == 1)
                    {
                        b = vertex[(j + 1) * (n + 1) + i];
                    }
                    else if (kind == 2)
                    {
                        b = vertex[(j + 1) * (n + 1) + i + 1];
                    }
                    else
                    {
                        b = a;
                    }
                    Point3f target = a + s * (b - a);
                    Vector3f offset = RandomDirection(random) * (Float)((0.5 + random.NextDouble() * 3) * (scale == 1 ? 1 : 0.1));
                    Point3f from = target + offset;
                    Ray r = new Ray(from, target - from);

                    bool quadHit = false, triangleHit = false;
                    for (int dj = -1; dj <= 1; ++dj)
                    {
                        for (int di = -1; di <= 1; ++di)
                        {
                            int cell = (j + dj) * n + (i + di);
                            quadHit |= quads[cell].IntersectP(r);
                            triangleHit |= triangles[2 * cell].IntersectP(r) || triangles[2 * cell + 1].IntersectP(r);
                        }
                    }
                    // (a ray within 1e-3 of the plane is skipped: the test of the distance of the hit, which keeps a hit from being behind the origin, is
                    // conservative by the error of the position over the cosine, and rejects such a hit for triangles and quads alike)
                    if (Math.Abs(pbrt.Dot(pbrt.Normalize(r.d), ez)) < 1e-3)
                    {
                        ++grazingRays;
                        grazingQuadMisses += quadHit ? 0 : 1;
                        grazingTriangleMisses += triangleHit ? 0 : 1;
                        continue;
                    }
                    ++totalRays;
                    quadMisses += quadHit ? 0 : 1;
                    triangleMisses += triangleHit ? 0 : 1;
                }
            }
            Console.WriteLine("[Quad] watertight: " + totalRays + " rays at shared edges, diagonals and vertices; missed by the quads: " + quadMisses + ", by triangles: " + triangleMisses +
                              "; grazing rays (cosine < 1e-3, not counted): " + grazingRays + ", missed by the quads: " + grazingQuadMisses + ", by triangles: " + grazingTriangleMisses);
            EXPECT_EQ(0, quadMisses);
        }

        // A grid in which the cells of one parity are Quads and the others two Triangles, the two kinds sharing the sides between the cells: a ray aimed at those
        // sides passes through one of the neighbors (the edge functions of the two kinds of shape are the same numbers)
        [TEST("Quad", "SeamBetweenQuadsAndTrianglesIsWatertight")]
        public static void Quad_SeamBetweenQuadsAndTrianglesIsWatertight()
        {
            Random random = new Random(1234);
            const int n = 12;
            int totalRays = 0, misses = 0;
            for (int grid = 0; grid < 8; ++grid)
            {
                Vector3f ex, ey, ez;
                RandomFrame(random, out ex, out ey, out ez);
                Point3f origin = new Point3f((Float)(random.NextDouble() * 6 - 3), (Float)(random.NextDouble() * 6 - 3), (Float)(random.NextDouble() * 6 - 3));
                Point3f[] vertex = new Point3f[(n + 1) * (n + 1)];
                for (int j = 0; j <= n; ++j)
                {
                    for (int i = 0; i <= n; ++i)
                    {
                        double jitterX = (i == 0 || i == n) ? 0 : 0.3 * (random.NextDouble() - 0.5);
                        double jitterY = (j == 0 || j == n) ? 0 : 0.3 * (random.NextDouble() - 0.5);
                        vertex[j * (n + 1) + i] = origin + (Float)(i + jitterX) * ex + (Float)(j + jitterY) * ey;
                    }
                }
                List<int> around = new List<int>();
                List<int> triangleIndices = new List<int>();
                for (int j = 0; j < n; ++j)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        int v0 = j * (n + 1) + i, v1 = v0 + 1, v2 = v1 + n + 1, v3 = v0 + n + 1;
                        if (((i + j) & 1) == 0)
                        {
                            around.AddRange(new int[] { v0, v1, v2, v3 });
                        }
                        else
                        {
                            triangleIndices.AddRange(new int[] { v0, v1, v2, v0, v2, v3 });
                        }
                    }
                }
                pstd.vector<Shape> quads = Quad.CreateShapes(new Transform(), false, around, new List<Point3f>(vertex), new List<Normal3f>(), new List<Point2f>(), new List<int>(),
                                                             false, new Allocator());
                TriangleMesh mesh = new TriangleMesh(new Transform(), false, new pstd.vector<int>(triangleIndices), new pstd.vector<Point3f>(vertex),
                                                     new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(), new pstd.vector<int>(), new Allocator());
                pstd.vector<Shape> triangles = Triangle.CreateTriangles(mesh, new Allocator());
                List<Shape> all = new List<Shape>();
                for (int i = 0; i < (int)quads.size(); ++i)
                {
                    all.Add(quads[i]);
                }
                for (int i = 0; i < (int)triangles.size(); ++i)
                {
                    all.Add(triangles[i]);
                }

                for (int ray = 0; ray < 400000; ++ray)
                {
                    int i = random.Next(1, n - 1), j = random.Next(1, n - 1);
                    int kind = random.Next(3);
                    Point3f a = vertex[j * (n + 1) + i];
                    Point3f b = kind == 0 ? vertex[j * (n + 1) + i + 1] : (kind == 1 ? vertex[(j + 1) * (n + 1) + i] : a);
                    Point3f target = a + (Float)random.NextDouble() * (b - a);
                    Point3f from = target + RandomDirection(random) * (Float)(0.5 + random.NextDouble() * 3);
                    Ray r = new Ray(from, target - from);
                    if (Math.Abs(pbrt.Dot(pbrt.Normalize(r.d), ez)) < 1e-3)
                    {
                        continue;
                    }
                    // (every shape: a ray that hits nowhere has passed between two of them)
                    bool hit = false;
                    for (int k = 0; k < all.Count && !hit; ++k)
                    {
                        hit = all[k].IntersectP(r);
                    }
                    ++totalRays;
                    misses += hit ? 0 : 1;
                }
            }
            Console.WriteLine("[Quad] seam between quads and triangles: " + totalRays + " rays at shared sides and vertices, missed: " + misses);
            EXPECT_EQ(0, misses);
        }

        // The time of the ray-triangle test (printed, not checked)
        [TEST("Quad", "TriangleIntersectionSpeed")]
        public static void Quad_TriangleIntersectionSpeed()
        {
            Random random = new Random(7);
            const int triangles = 1024;
            const int rays = 4096;
            Point3f[] p = new Point3f[3 * triangles];
            for (int i = 0; i < triangles; ++i)
            {
                Point3f c = new Point3f((Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2));
                for (int k = 0; k < 3; ++k)
                {
                    p[3 * i + k] = c + RandomDirection(random) * (Float)(0.2 + random.NextDouble());
                }
            }
            Ray[] r = new Ray[rays];
            for (int i = 0; i < rays; ++i)
            {
                Point3f from = new Point3f(0, 0, 0) + RandomDirection(random) * 4;
                Point3f to = new Point3f((Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2));
                r[i] = new Ray(from, to - from);
            }
            const int count = 40000000;
            long hits = 0;
            double best = double.MaxValue;
            for (int repeat = 0; repeat < 4; ++repeat)
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                hits = 0;
                for (int i = 0; i < count; ++i)
                {
                    if (pbrt.IntersectTriangle(r[i & (rays - 1)], pbrt.Infinity, p[3 * (i % triangles)], p[3 * (i % triangles) + 1], p[3 * (i % triangles) + 2]).has_value())
                    {
                        ++hits;
                    }
                }
                watch.Stop();
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine("[Quad] IntersectTriangle: " + (best * 1e6 / count).ToString("F2", CultureInfo.InvariantCulture) + " ns per test (best of 4), hits " + hits);
        }

        private struct Measurement
        {
            public double pdfIntegral;
            public double sampledOverQuadrature;
            public double mismatchFraction;
            public double badPoints;
        }

        // The shape from the point p with the normal n: the cosine of the receiver is |dot(n, wi)|. The quadrature is the midpoint rule over the (u,v) of the
        // bilinear patch of the corners, with the area element |dpdu x dpdv|.
        private static Measurement Measure(Shape shape, Point3f[] corners, Point3f p, Normal3f n, bool warp, int count, int seed)
        {
            Random random = new Random(seed);
            Normal3f ns = warp ? n : new Normal3f(0, 0, 0);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p, new Vector3f(0, 0, 0)), ns, ns, 0);
            Measurement m = new Measurement();
            double size = pbrt.Length(corners[2] - corners[0]);

            double estimate = 0;
            int mismatches = 0;
            int bad = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                if (!ss.has_value() || ss.value().pdf == 0)
                {
                    continue;
                }
                Vector3f wi = pbrt.Normalize(ss.value().intr.p() - p);
                estimate += pbrt.AbsDot(n, wi) / ss.value().pdf;
                double other = shape.PDF(ctx, wi);
                if (Math.Abs(other - ss.value().pdf) > 1e-2 * Math.Abs(ss.value().pdf))
                {
                    ++mismatches;
                }
                if (i < 300)
                {
                    // the sampled point is the bilinear point of its (u,v)
                    Point2f st = ss.value().intr.uv;
                    if (Distance(Bilinear(corners, st[0], st[1]), ss.value().intr.p()) > 1e-4 * size)
                    {
                        ++bad;
                    }
                }
            }
            estimate /= count;
            m.mismatchFraction = (double)mismatches / count;
            m.badPoints = bad;

            const int cells = 400;
            const int pdfStride = 4;
            double quadrature = 0;
            double pdfSum = 0;
            for (int i = 0; i < cells; ++i)
            {
                for (int j = 0; j < cells; ++j)
                {
                    double u = (i + 0.5) / cells, v = (j + 0.5) / cells;
                    double[] q = Bilinear(corners, u, v);
                    Vector3f dpdu = (Float)(1 - v) * (corners[1] - corners[0]) + (Float)v * (corners[2] - corners[3]);
                    Vector3f dpdv = (Float)(1 - u) * (corners[3] - corners[0]) + (Float)u * (corners[2] - corners[1]);
                    Vector3f cross = pbrt.Cross(dpdu, dpdv);
                    double areaElement = pbrt.Length(cross) / ((double)cells * cells);
                    Vector3f planeNormal = pbrt.Normalize(cross);
                    Vector3f d = new Vector3f((Float)(q[0] - p.x), (Float)(q[1] - p.y), (Float)(q[2] - p.z));
                    Vector3f wi = pbrt.Normalize(d);
                    double cosines = pbrt.AbsDot(planeNormal, wi) / pbrt.LengthSquared(d) * areaElement;
                    quadrature += pbrt.AbsDot(n, wi) * cosines;
                    if (i % pdfStride == 1 && j % pdfStride == 1)
                    {
                        pdfSum += shape.PDF(ctx, wi) * cosines * pdfStride * pdfStride;
                    }
                }
            }
            m.pdfIntegral = pdfSum;
            m.sampledOverQuadrature = quadrature > 0 ? estimate / quadrature : 1;
            return m;
        }

        private static void CheckReceivers(Point3f[] corners, string what, Point3f[] receivers, double tolerance, bool projected)
        {
            Quad quad = MakeQuad(corners, null, projected);
            int seed = 0;
            foreach (Point3f p in receivers)
            {
                foreach (bool warp in new bool[] { true, false })
                {
                    Measurement m = Measure(quad, corners, p, new Normal3f(0, 1, 0), warp, 60000, ++seed);
                    bool ok = Math.Abs(m.sampledOverQuadrature - 1) <= tolerance && (m.pdfIntegral == 0 || Math.Abs(m.pdfIntegral - 1) <= 0.02) &&
                              m.mismatchFraction < 0.05 && m.badPoints == 0;
                    EXPECT_NEAR(1.0, m.sampledOverQuadrature, tolerance);
                    if (m.pdfIntegral > 0)
                    {
                        EXPECT_NEAR(1.0, m.pdfIntegral, 0.02);
                    }
                    EXPECT_LT(m.mismatchFraction, 0.05);
                    EXPECT_EQ(0.0, m.badPoints);
                    if (!ok)
                    {
                        Console.WriteLine("[Quad] failing receiver (" + what + ") p=" + p + " warp=" + warp + " projected=" + projected + " s/q=" + m.sampledOverQuadrature.ToString("F4", CultureInfo.InvariantCulture) +
                                          " pdfInt=" + m.pdfIntegral.ToString("F4", CultureInfo.InvariantCulture) + " mismatch=" + m.mismatchFraction.ToString("E2", CultureInfo.InvariantCulture) +
                                          " badPoints=" + m.badPoints);
                    }
                }
            }
        }

        private static Point3f[] FloorReceivers()
        {
            List<Point3f> list = new List<Point3f>();
            foreach (float z in new float[] { -6, -1, 0.3f, 1, 6 })
            {
                foreach (float x in new float[] { -5, -2, 3 })
                {
                    list.Add(new Point3f(x, 0, z));
                }
            }
            return list.ToArray();
        }

        // The quads of every kind: Sample() is an unbiased estimator of the irradiance by the quadrature, PDF() is the density Sample() used, the PDF integrates to
        // one, the (u,v) of a sample are those of its point. A general quadrilateral and a trapezoid sample two spherical triangles; a rectangle (in any of the
        // four frames) the spherical rectangle; a parallelogram two triangles.
        [TEST("Quad", "SamplingMatchesQuadratureAndPDF")]
        public static void Quad_SamplingMatchesQuadratureAndPDF()
        {
            CheckAllKinds(false);
        }

        // The same with the quads sampled proportional to their projected solid angle (sampling "projected"): without a shading normal they are sampled as above
        [TEST("Quad", "ProjectedSamplingMatchesQuadratureAndPDF")]
        public static void Quad_ProjectedSamplingMatchesQuadratureAndPDF()
        {
            CheckAllKinds(true);
        }

        private static void CheckAllKinds(bool projected)
        {
            Point3f centre = new Point3f(-2.0f, 2.5f, 0.1f);
            Vector3f ax = pbrt.Normalize(new Vector3f(0.999128209f, 0.00290597103f, -0.0416458633f));
            Vector3f ay = pbrt.Normalize(new Vector3f(0.00290597103f, 0.99031343f, 0.138819544f));
            Point3f[] receivers = FloorReceivers();
            // a general convex quadrilateral
            CheckReceivers(new Point3f[] { centre - 0.9f * ax - 0.5f * ay, centre + 0.7f * ax - 0.7f * ay, centre + 0.5f * ax + 0.8f * ay, centre - 0.6f * ax + 0.6f * ay },
                           "general", receivers, 0.015, projected);
            // a trapezoid, then the same corners starting from each of the other corners
            Point3f[] trapezoid = { centre - 0.9f * ax - 0.5f * ay, centre + 0.9f * ax - 0.5f * ay, centre + 0.4f * ax + 0.6f * ay, centre - 0.4f * ax + 0.6f * ay };
            for (int k = 0; k < 4; ++k)
            {
                Point3f[] rotated = { trapezoid[k], trapezoid[(k + 1) & 3], trapezoid[(k + 2) & 3], trapezoid[(k + 3) & 3] };
                CheckReceivers(rotated, "trapezoid from corner " + k, new Point3f[] { receivers[0], receivers[7], receivers[13] }, 0.015, projected);
            }
            // a parallelogram
            CheckReceivers(new Point3f[] { centre - 0.8f * ax - 0.5f * ay, centre + 0.4f * ax - 0.5f * ay, centre + 0.8f * ax + 0.5f * ay, centre - 0.4f * ax + 0.5f * ay },
                           "parallelogram", receivers, 0.015, projected);
            // a rectangle, in each of the four frames of the corners
            Point3f[] rectangle = { centre - 0.8f * ax - 0.5f * ay, centre + 0.8f * ax - 0.5f * ay, centre + 0.8f * ax + 0.5f * ay, centre - 0.8f * ax + 0.5f * ay };
            for (int k = 0; k < 4; ++k)
            {
                Point3f[] rotated = { rectangle[k], rectangle[(k + 1) & 3], rectangle[(k + 2) & 3], rectangle[(k + 3) & 3] };
                CheckReceivers(rotated, "rectangle from corner " + k, receivers, 0.015, projected);
            }
        }

        // The integral of |dot(n, wi)| over the solid angle of the quad (cells x cells cells of the (u,v) of the bilinear patch, sub x sub points in each): the
        // projected solid angle of the quad seen from p, in total and per cell
        private static double[] ProjectedSolidAngleCells(Point3f[] corners, Point3f p, Vector3f n, int cells, int sub)
        {
            double[] result = new double[cells * cells];
            int fine = cells * sub;
            for (int i = 0; i < fine; ++i)
            {
                for (int j = 0; j < fine; ++j)
                {
                    double u = (i + 0.5) / fine, v = (j + 0.5) / fine;
                    double[] q = Bilinear(corners, u, v);
                    Vector3f dpdu = (Float)(1 - v) * (corners[1] - corners[0]) + (Float)v * (corners[2] - corners[3]);
                    Vector3f dpdv = (Float)(1 - u) * (corners[3] - corners[0]) + (Float)u * (corners[2] - corners[1]);
                    Vector3f cross = pbrt.Cross(dpdu, dpdv);
                    double areaElement = pbrt.Length(cross) / ((double)fine * fine);
                    Vector3f planeNormal = pbrt.Normalize(cross);
                    Vector3f d = new Vector3f((Float)(q[0] - p.x), (Float)(q[1] - p.y), (Float)(q[2] - p.z));
                    Vector3f wi = pbrt.Normalize(d);
                    result[(i / sub) * cells + (j / sub)] += pbrt.AbsDot(n, wi) * pbrt.AbsDot(planeNormal, wi) / pbrt.LengthSquared(d) * areaElement;
                }
            }
            return result;
        }

        private static double Sum(double[] values)
        {
            double sum = 0;
            for (int i = 0; i < values.Length; ++i)
            {
                sum += values[i];
            }
            return sum;
        }

        // The corners relative to p in the coordinates of a frame whose z-axis is n, for ProjectedSolidAnglePolygon
        private static void LocalCorners(Point3f[] corners, Point3f p, Vector3f ex, Vector3f ey, Vector3f ez, double[] x, double[] y, double[] z)
        {
            for (int k = 0; k < corners.Length; ++k)
            {
                Vector3f d = corners[k] - p;
                x[k] = (double)d.x * ex.x + (double)d.y * ex.y + (double)d.z * ex.z;
                y[k] = (double)d.x * ey.x + (double)d.y * ey.y + (double)d.z * ey.z;
                z[k] = (double)d.x * ez.x + (double)d.y * ez.y + (double)d.z * ez.z;
            }
        }

        // The projected solid angle of the polygon (above and below the horizon) against the quadrature, for random quads and receivers: the sum of the
        // sectors of the central and the decentral case, the clipping, both windings
        [TEST("Quad", "ProjectedSolidAngleMatchesQuadrature")]
        public static void Quad_ProjectedSolidAngleMatchesQuadrature()
        {
            Random random = new Random(21);
            ProjectedSolidAnglePolygon upper = new ProjectedSolidAnglePolygon();
            ProjectedSolidAnglePolygon lower = new ProjectedSolidAnglePolygon();
            int central = 0, decentral = 0, straddling = 0, skipped = 0;
            double worst = 0;
            for (int trial = 0; trial < 300; ++trial)
            {
                Vector3f ez;
                Point3f center = new Point3f((Float)(4 * random.NextDouble() - 2), (Float)(4 * random.NextDouble() - 2), (Float)(4 * random.NextDouble() - 2));
                Point3f[] corners = RandomQuad(random, center, (Float)(0.3 + 2 * random.NextDouble()), out ez);
                Vector3f nx, ny, nz;
                RandomFrame(random, out nx, out ny, out nz);
                Point3f p = new Point3f(0, 0, 0);
                double[] x = new double[4], y = new double[4], z = new double[4];
                LocalCorners(corners, p, nx, ny, nz, x, y, z);
                double areaUpper = upper.Prepare(4, x, y, z, 1) ? upper.ProjectedSolidAngle : 0;
                double areaLower = lower.Prepare(4, x, y, z, -1) ? lower.ProjectedSolidAngle : 0;
                if (areaUpper > 0)
                {
                    if (upper.IsCentral)
                    {
                        ++central;
                    }
                    else
                    {
                        ++decentral;
                    }
                }
                if (areaUpper > 0 && areaLower > 0)
                {
                    ++straddling;
                }
                // The quadrature is the reference: skip the quads so near the receiver that it is not converged (the integrand is sharply peaked there)
                double expected = Sum(ProjectedSolidAngleCells(corners, p, nz, 1, 300));
                double refined = Sum(ProjectedSolidAngleCells(corners, p, nz, 1, 600));
                if (Math.Abs(expected - refined) > 5e-4 * refined)
                {
                    ++skipped;
                    continue;
                }
                expected = refined;
                double relative = Math.Abs(areaUpper + areaLower - expected) / Math.Max(expected, 1e-9);
                worst = Math.Max(worst, relative);
                EXPECT_LT(relative, 2e-3);
            }
            Console.WriteLine("[Quad] projected solid angle vs quadrature over 300 random quads: worst relative difference " + worst.ToString("E2", CultureInfo.InvariantCulture) +
                              ", skipped " + skipped + " unconverged references, upper polygons: " + central + " central, " + decentral + " decentral, " + straddling + " quads straddling the horizon");
            EXPECT_GT(central, 10);
            EXPECT_GT(decentral, 10);
            EXPECT_GT(straddling, 10);
        }

        // The distribution of the samples: the fraction of the samples in each of 8 x 8 cells of the (u,v) of the quad against the integral of |cos| over the
        // cell by quadrature, divided by the total. The z-score of every cell must be small. This is what the pdf alone cannot show (f / pdf is constant).
        private static void CheckDistribution(Point3f[] corners, string what, Point3f p, Vector3f n, int count, int seed)
        {
            const int cells = 8;
            Quad quad = MakeQuad(corners, null, true);
            double[] expected = ProjectedSolidAngleCells(corners, p, n, cells, 25);
            double total = Sum(expected);
            Normal3f ns = new Normal3f(n.x, n.y, n.z);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p, new Vector3f(0, 0, 0)), ns, ns, 0);
            int[] counts = new int[cells * cells];
            int accepted = 0;
            Random random = new Random(seed);
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = quad.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                if (!ss.has_value() || ss.value().pdf == 0)
                {
                    continue;
                }
                Point2f st = ss.value().intr.uv;
                int cu = Math.Min(cells - 1, Math.Max(0, (int)(st[0] * cells)));
                int cv = Math.Min(cells - 1, Math.Max(0, (int)(st[1] * cells)));
                ++counts[cu * cells + cv];
                ++accepted;
            }
            EXPECT_GT(accepted, count * 99 / 100);
            double worstZ = 0;
            double chiSquare = 0;
            int degrees = 0;
            for (int c = 0; c < cells * cells; ++c)
            {
                double probability = expected[c] / total;
                double mean = accepted * probability;
                double sigma = Math.Sqrt(Math.Max(accepted * probability * (1 - probability), 1.0));
                double z = (counts[c] - mean) / sigma;
                worstZ = Math.Max(worstZ, Math.Abs(z));
                if (mean >= 5)
                {
                    chiSquare += z * z;
                    ++degrees;
                }
            }
            Console.WriteLine("[Quad] projected distribution (" + what + "): worst |z| " + worstZ.ToString("F2", CultureInfo.InvariantCulture) + ", chi2/dof " +
                              (chiSquare / Math.Max(degrees, 1)).ToString("F2", CultureInfo.InvariantCulture));
            EXPECT_LT(worstZ, 5.0);
            EXPECT_LT(chiSquare / Math.Max(degrees, 1), 1.6);
        }

        // Central (the polygon contains the zenith), decentral, straddling the horizon, behind the receiver (the lower part), almost in its plane, and the quads
        // whose edges lie in planes through the zenith (degenerate ellipses), exactly and slightly rotated
        [TEST("Quad", "ProjectedSamplingDistributionMatchesQuadrature")]
        public static void Quad_ProjectedSamplingDistributionMatchesQuadrature()
        {
            Vector3f up = new Vector3f(0, 1, 0);
            Point3f[] general = { new Point3f(-1.2f, 2.0f, -0.9f), new Point3f(1.1f, 2.0f, -1.1f), new Point3f(0.8f, 2.0f, 1.3f), new Point3f(-0.9f, 2.0f, 0.7f) };
            CheckDistribution(general, "central", new Point3f(0, 0, 0), up, 400000, 1);
            CheckDistribution(general, "decentral", new Point3f(3.0f, 0, 0.5f), up, 400000, 2);
            CheckDistribution(general, "decentral, far to the side", new Point3f(9.0f, 0.5f, -3f), up, 400000, 3);
            CheckDistribution(general, "straddling the horizon", new Point3f(2.5f, 2.0f - 0.0f + 0.3f, 0.2f), up, 400000, 4);
            CheckDistribution(general, "behind the receiver", new Point3f(0.3f, 5.0f, 0.1f), up, 400000, 5);
            CheckDistribution(general, "almost in the plane", new Point3f(0.3f, 1.98f, 0.1f), up, 400000, 6);
            // a tilted quad, a tilted normal
            Vector3f tilted = pbrt.Normalize(new Vector3f(0.3f, 0.8f, -0.4f));
            Vector3f planeX = pbrt.Normalize(new Vector3f(1, 0.2f, 0.1f));
            Vector3f planeY = pbrt.Normalize(new Vector3f(-0.1f, 0.7f, 0.9f));
            Point3f planeCenter = new Point3f(0.4f, 2.2f, 0.3f);
            Point3f[] slanted = { planeCenter - 1.4f * planeX - 0.9f * planeY, planeCenter + 1.6f * planeX - 1.0f * planeY, planeCenter + 1.1f * planeX + 1.2f * planeY,
                                  planeCenter - 0.8f * planeX + 0.8f * planeY };
            CheckDistribution(slanted, "tilted quad and normal", new Point3f(0.2f, 0, 0), tilted, 400000, 7);

            // Edges in planes through the zenith: a quad in the plane z = 2 with vertical edges, seen from the origin with the normal along y
            Point3f[] axisAligned = { new Point3f(-1, 1, 2), new Point3f(1, 1, 2), new Point3f(1, 3, 2), new Point3f(-1, 3, 2) };
            CheckDistribution(axisAligned, "degenerate ellipses", new Point3f(0, 0, 0), up, 400000, 8);
            CheckDistribution(axisAligned, "degenerate ellipses, decentral", new Point3f(3, 0, 0), up, 400000, 9);
            CheckDistribution(axisAligned, "degenerate ellipses, straddling", new Point3f(0, 2, 0), up, 400000, 10);
            // The same turned around the normal (the edges stay in planes through the zenith) and around another axis (they do not, nearly)
            foreach (double angle in new double[] { 0.3, 1.1, 2.7 })
            {
                Point3f[] turned = new Point3f[4];
                for (int k = 0; k < 4; ++k)
                {
                    turned[k] = new Point3f((Float)(Math.Cos(angle) * axisAligned[k].x + Math.Sin(angle) * axisAligned[k].z), axisAligned[k].y,
                                            (Float)(-Math.Sin(angle) * axisAligned[k].x + Math.Cos(angle) * axisAligned[k].z));
                }
                CheckDistribution(turned, "degenerate ellipses turned by " + angle.ToString("F1", CultureInfo.InvariantCulture), new Point3f(0, 0, 0), up, 400000, 11);
                Point3f[] nearly = new Point3f[4];
                for (int k = 0; k < 4; ++k)
                {
                    nearly[k] = new Point3f(turned[k].x, (Float)(Math.Cos(1e-6) * turned[k].y - Math.Sin(1e-6) * turned[k].z),
                                            (Float)(Math.Sin(1e-6) * turned[k].y + Math.Cos(1e-6) * turned[k].z));
                }
                CheckDistribution(nearly, "nearly degenerate ellipses, turned by " + angle.ToString("F1", CultureInfo.InvariantCulture), new Point3f(0, 0, 0), up, 400000, 12);
            }
        }

        // The iteration of the decentral case converges: the residual area of the sampled direction over random polygons (including slivers at the horizon) and
        // random numbers
        [TEST("Quad", "ProjectedSamplingIterationConverges")]
        public static void Quad_ProjectedSamplingIterationConverges()
        {
            Random random = new Random(33);
            ProjectedSolidAnglePolygon polygon = new ProjectedSolidAnglePolygon();
            polygon.recordResidual = true;
            double worst = 0;
            int samples = 0;
            int decentral = 0;
            double worstRelative = 0;
            for (int trial = 0; trial < 6000; ++trial)
            {
                Vector3f ez;
                double distance = 0.2 + 6 * random.NextDouble();
                Vector3f direction = RandomDirection(random);
                Point3f center = new Point3f((Float)(distance * direction.x), (Float)(distance * direction.y), (Float)(distance * direction.z));
                Point3f[] corners = RandomQuad(random, center, (Float)(0.1 + 3 * random.NextDouble()), out ez);
                double[] x = new double[4], y = new double[4], z = new double[4];
                for (int k = 0; k < 4; ++k)
                {
                    x[k] = corners[k].x;
                    y[k] = corners[k].y;
                    z[k] = corners[k].z;
                }
                foreach (double sign in new double[] { 1, -1 })
                {
                    if (!polygon.Prepare(4, x, y, z, sign) || polygon.IsCentral)
                    {
                        continue;
                    }
                    ++decentral;
                    for (int s = 0; s < 40; ++s)
                    {
                        double dx, dy, dz;
                        double saveU0 = random.NextDouble(), saveU1 = random.NextDouble();
                        polygon.Sample(saveU0, saveU1, out dx, out dy, out dz);
                        EXPECT_NEAR(1.0, Math.Sqrt(dx * dx + dy * dy + dz * dz), 1e-9);
                        EXPECT_GE(dz, 0.0);
                        worst = Math.Max(worst, polygon.lastAbsoluteResidual);
                        worstRelative = Math.Max(worstRelative, polygon.lastRelativeResidual);
                        ++samples;
                    }
                }
            }
            Console.WriteLine("[Quad] projected sampling iteration: worst residual area " + worst.ToString("E2", CultureInfo.InvariantCulture) + " (unit disk: pi), relative to its sector " +
                              worstRelative.ToString("E2", CultureInfo.InvariantCulture) + ", over " + samples + " samples of " + decentral + " decentral polygons");
            EXPECT_LT(worst, 2e-14);
        }

        // From a distance (the solid angle is under the threshold of spherical sampling) and from a point on the plane of the quad, the sampling is by area
        [TEST("Quad", "AreaSamplingFallbackMatchesQuadrature")]
        public static void Quad_AreaSamplingFallbackMatchesQuadrature()
        {
            Point3f[] corners = { new Point3f(-0.9f, 2.5f, -0.5f), new Point3f(0.7f, 2.5f, -0.7f), new Point3f(0.5f, 2.5f, 0.8f), new Point3f(-0.6f, 2.5f, 0.6f) };
            Quad quad = MakeQuad(corners, null);
            // far away: the solid angle is about 1.5 / 4e4
            Measurement far = Measure(quad, corners, new Point3f(10, 0, 200), new Normal3f(0, 1, 0), true, 200000, 1);
            EXPECT_NEAR(1.0, far.sampledOverQuadrature, 0.03);
            EXPECT_LT(far.mismatchFraction, 0.05);
            EXPECT_EQ(0.0, far.badPoints);
            // a point in the plane of the quad, outside it, sees no irradiance: the estimate is 0 or the samples are dropped, never the irradiance of a hemisphere
            ShapeSampleContext inPlane = new ShapeSampleContext(new Point3fi(new Point3f(3, 2.5f, 0), new Vector3f(0, 0, 0)), new Normal3f(0, 1, 0), new Normal3f(0, 1, 0), 0);
            Random random = new Random(3);
            double estimate = 0;
            for (int i = 0; i < 2000; ++i)
            {
                pstd.optional<ShapeSample> ss = quad.Sample(inPlane, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                if (ss.has_value() && ss.value().pdf > 0)
                {
                    estimate += pbrt.AbsDot(new Normal3f(0, 1, 0), pbrt.Normalize(ss.value().intr.p() - inPlane.p())) / ss.value().pdf;
                }
            }
            EXPECT_LT(estimate / 2000, 1e-3);
        }

        // The point on a quad light for equiangular sampling: uniform by area, with the density 1 / area, and the light supports it
        [TEST("Quad", "QuadLightAreaDensity")]
        public static void Quad_QuadLightAreaDensity()
        {
            Point3f[] p = { new Point3f(-1, -1, 0), new Point3f(2, -1, 0), new Point3f(1, 1, 0), new Point3f(-1, 0.5f, 0) };
            Quad quad = MakeQuad(p, null);
            Transform id = new Transform();
            DiffuseAreaLight light = new DiffuseAreaLight(id, new MediumInterface(), new ConstantSpectrum(1.0f), 1.0f, quad, null, new Image(), null, true);
            EXPECT_TRUE(light.SupportsEquiangular());
            // the area of the quad: the shoelace formula in the plane z = 0
            double area = 0;
            for (int k = 0; k < 4; ++k)
            {
                area += (double)p[k].x * p[(k + 1) & 3].y - (double)p[(k + 1) & 3].x * p[k].y;
            }
            area = 0.5 * Math.Abs(area);
            EXPECT_NEAR((Float)(1 / area), light.PdfArea(), 1e-5);
            // the centroid of the quad: the mean of the centroids of the triangles (p0 p1 p2) and (p0 p2 p3), by area
            double[] c = new double[2];
            double sumArea = 0;
            for (int part = 0; part < 2; ++part)
            {
                Point3f a = p[0], b = part == 0 ? p[1] : p[2], d = part == 0 ? p[2] : p[3];
                double t = 0.5 * Math.Abs((b.x - a.x) * (d.y - a.y) - (d.x - a.x) * (b.y - a.y));
                c[0] += t * (a.x + b.x + d.x) / 3;
                c[1] += t * (a.y + b.y + d.y) / 3;
                sumArea += t;
            }
            EXPECT_NEAR(area, sumArea, 1e-6);
            Random random = new Random(8);
            double mx = 0, my = 0;
            int n = 40000;
            for (int i = 0; i < n; ++i)
            {
                EXPECT_TRUE(light.SampleArea(new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()), out Interaction y, out Float pdfArea));
                EXPECT_NEAR(light.PdfArea(), pdfArea, 1e-5);
                mx += y.p().x / n;
                my += y.p().y / n;
            }
            EXPECT_NEAR(c[0] / sumArea, mx, 0.02);
            EXPECT_NEAR(c[1] / sumArea, my, 0.02);
        }
    }
}
