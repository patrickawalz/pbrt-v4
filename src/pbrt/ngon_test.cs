// (not in pbrt-v4) ngon_test.cs
//
// Tests of the Ngon shape (shapes_ngon.cs): the test that decides whether a polygon is an Ngon, the ray intersection against an independent plane-and-polygon
// computation, the watertightness of a mesh of hexagons next to a Quad and Triangles, and the sampling (by area, by solid angle, by projected solid angle)
// against a quadrature, together with the density that PDF returns.

using System;
using System.Collections.Generic;
using System.Globalization;
using testing;

namespace pbrt
{
    public static unsafe class NgonTests
    {
        // A convex polygon of n corners in a plane with coordinates (x, y), counterclockwise, the first edge along +x from corner 0
        internal static double[][] ConvexPolygon2D(Random random, int n, double size)
        {
            while (true)
            {
                double[] angles = new double[n];
                double sum = 0;
                double[] steps = new double[n];
                for (int i = 0; i < n; ++i)
                {
                    steps[i] = 0.5 + random.NextDouble();
                    sum += steps[i];
                }
                double angle = 0;
                double[][] points = new double[n][];
                for (int i = 0; i < n; ++i)
                {
                    angle += 2 * Math.PI * steps[i] / sum;
                    double radius = size * (0.8 + 0.2 * random.NextDouble());
                    points[i] = new double[] { radius * Math.Cos(angle), radius * Math.Sin(angle) };
                    angles[i] = angle;
                }
                // radii vary, so check convexity and a minimum turn at every corner
                bool convex = true;
                for (int i = 0; i < n && convex; ++i)
                {
                    double[] a = points[i], b = points[(i + 1) % n], c = points[(i + 2) % n];
                    double turn = (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]);
                    if (turn <= 1e-3 * size * size)
                    {
                        convex = false;
                    }
                }
                if (!convex)
                {
                    continue;
                }
                // rotate so that the first edge points along +x, and put corner 0 at the origin
                double ex = points[1][0] - points[0][0], ey = points[1][1] - points[0][1];
                double length = Math.Sqrt(ex * ex + ey * ey);
                double cs = ex / length, sn = ey / length;
                double[][] result = new double[n][];
                for (int i = 0; i < n; ++i)
                {
                    double dx = points[i][0] - points[0][0], dy = points[i][1] - points[0][1];
                    result[i] = new double[] { dx * cs + dy * sn, -dx * sn + dy * cs };
                }
                return result;
            }
        }

        private static Vector3f RandomDirection(Random random)
        {
            double z = 1 - 2 * random.NextDouble();
            double r = Math.Sqrt(Math.Max(0, 1 - z * z));
            double phi = 2 * Math.PI * random.NextDouble();
            return new Vector3f((Float)(r * Math.Cos(phi)), (Float)(r * Math.Sin(phi)), (Float)z);
        }

        internal static void RandomPlane(Random random, out Vector3f ex, out Vector3f ey, out Vector3f ez)
        {
            ez = RandomDirection(random);
            Vector3f helper = Math.Abs(ez.x) < 0.9f ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0);
            ex = pbrt.Normalize(pbrt.Cross(helper, ez));
            ey = pbrt.Cross(ez, ex);
        }

        internal static Point3f Embed(double[] q, Point3f origin, Vector3f ex, Vector3f ey)
        {
            return origin + (Float)q[0] * ex + (Float)q[1] * ey;
        }

        private static Ngon MakeNgon(Point3f[] p, bool projected)
        {
            List<int> indices = new List<int>();
            for (int i = 0; i < p.Length; ++i)
            {
                indices.Add(i);
            }
            pstd.vector<Shape> shapes = Ngon.CreateShapes(new Transform(), false, new List<int> { p.Length }, indices, new List<Point3f>(p), new List<Normal3f>(),
                                                          new List<Point2f>(), new List<int>(), projected, new Allocator());
            EXPECT_EQ(1, (int)shapes.size());
            return shapes[0] as Ngon;
        }

        private static Rectangle MakeRectangle(Point3f[] p, bool projected)
        {
            Ngon inner = MakeNgon(p, projected);
            EXPECT_TRUE(Rectangle.IsRectangle(p));
            return new Rectangle(inner, projected);
        }

        private static double[][] ToDouble(Point3f[] p)
        {
            double[][] c = new double[p.Length][];
            for (int i = 0; i < p.Length; ++i)
            {
                c[i] = new double[] { p[i].x, p[i].y, p[i].z };
            }
            return c;
        }

        private static bool Inside2D(double[][] polygon, double x, double y)
        {
            int n = polygon.Length;
            for (int i = 0; i < n; ++i)
            {
                double[] a = polygon[i], b = polygon[(i + 1) % n];
                if ((b[0] - a[0]) * (y - a[1]) - (b[1] - a[1]) * (x - a[0]) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        [TEST("Ngon", "ClassifyAcceptsPlanarConvexOnly")]
        public static void Ngon_ClassifyAcceptsPlanarConvexOnly()
        {
            Random random = new Random(5);
            int[] kept;
            for (int n = 3; n <= Ngon.MaxCornerCount; ++n)
            {
                double[][] polygon = ConvexPolygon2D(random, n, 1);
                double[][] c = new double[n][];
                for (int i = 0; i < n; ++i)
                {
                    c[i] = new double[] { polygon[i][0], polygon[i][1], 0 };
                }
                EXPECT_TRUE(Ngon.Classify(c, out kept));
            }
            // too many corners
            double[][] big = new double[Ngon.MaxCornerCount + 1][];
            for (int i = 0; i < big.Length; ++i)
            {
                double angle = 2 * Math.PI * i / big.Length;
                big[i] = new double[] { Math.Cos(angle), Math.Sin(angle), 0 };
            }
            EXPECT_FALSE(Ngon.Classify(big, out kept));
            // a concave pentagon, a bow tie, a non-planar hexagon, a repeated corner
            double[][] concave = { new double[] { 0, 0, 0 }, new double[] { 2, 0, 0 }, new double[] { 2, 2, 0 }, new double[] { 1, 0.5, 0 }, new double[] { 0, 2, 0 } };
            EXPECT_FALSE(Ngon.Classify(concave, out kept));
            double[][] bow = { new double[] { 0, 0, 0 }, new double[] { 1, 1, 0 }, new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 } };
            EXPECT_FALSE(Ngon.Classify(bow, out kept));
            double[][] bent = new double[6][];
            for (int i = 0; i < 6; ++i)
            {
                double angle = 2 * Math.PI * i / 6;
                bent[i] = new double[] { Math.Cos(angle), Math.Sin(angle), i == 2 ? 0.05 : 0 };
            }
            EXPECT_FALSE(Ngon.Classify(bent, out kept));
            double[][] repeated = { new double[] { 0, 0, 0 }, new double[] { 1, 0, 0 }, new double[] { 1, 0, 0 }, new double[] { 1, 1, 0 }, new double[] { 0, 1, 0 } };
            EXPECT_FALSE(Ngon.Classify(repeated, out kept));
            // a collinear corner stays an Ngon, but is not a corner of the polygon the projected sampling uses
            double[][] collinear = { new double[] { 0, 0, 0 }, new double[] { 1, 0, 0 }, new double[] { 2, 0, 0 }, new double[] { 2, 2, 0 }, new double[] { 0, 2, 0 } };
            EXPECT_TRUE(Ngon.Classify(collinear, out kept));
            EXPECT_TRUE(kept != null);
            EXPECT_EQ(4, kept.Length);
        }

        [TEST("Ngon", "IntersectionMatchesPlaneAndPolygon")]
        public static void Ngon_IntersectionMatchesPlaneAndPolygon()
        {
            Random random = new Random(33);
            int hits = 0, misses = 0;
            for (int trial = 0; trial < 220; ++trial)
            {
                int n = 3 + trial % (Ngon.MaxCornerCount - 2);
                double size = 0.2 + random.NextDouble() * 3;
                double[][] polygon = ConvexPolygon2D(random, n, size);
                Vector3f ex, ey, ez;
                RandomPlane(random, out ex, out ey, out ez);
                Point3f origin = new Point3f((Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2), (Float)(random.NextDouble() * 4 - 2));
                Point3f[] p = new Point3f[n];
                for (int i = 0; i < n; ++i)
                {
                    p[i] = Embed(polygon[i], origin, ex, ey);
                }
                Ngon ngon = MakeNgon(p, true);
                for (int i = 0; i < 300; ++i)
                {
                    double tx = (random.NextDouble() * 3 - 1.5) * size, ty = (random.NextDouble() * 3 - 1.5) * size;
                    Point3f target = Embed(new double[] { tx, ty }, origin, ex, ey);
                    Point3f from = target + RandomDirection(random) * (Float)(0.3 + random.NextDouble() * 4);
                    Vector3f d = pbrt.Normalize(target - from);
                    if (Math.Abs(pbrt.Dot(d, ez)) < 0.05)
                    {
                        continue;
                    }
                    // distance from the nearest edge, in the plane
                    double margin = double.MaxValue;
                    for (int k = 0; k < n; ++k)
                    {
                        double[] a = polygon[k], b = polygon[(k + 1) % n];
                        double len = Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
                        margin = Math.Min(margin, ((b[0] - a[0]) * (ty - a[1]) - (b[1] - a[1]) * (tx - a[0])) / len);
                    }
                    if (Math.Abs(margin) < 1e-4 * size)
                    {
                        continue;
                    }
                    bool expectHit = margin > 0;
                    double t = pbrt.Length(target - from);
                    Ray ray = new Ray(from, d);
                    pstd.optional<ShapeIntersection> isect = ngon.Intersect(ray);
                    EXPECT_EQ(expectHit, isect.has_value());
                    EXPECT_EQ(expectHit, ngon.IntersectP(ray));
                    if (!expectHit || !isect.has_value())
                    {
                        ++misses;
                        continue;
                    }
                    ++hits;
                    EXPECT_NEAR(t, isect.value().tHit, 1e-4 * (1 + t));
                    EXPECT_LT(pbrt.Length(isect.value().intr.p() - target), 2e-5 * (size + 1));
                    EXPECT_GT(std.abs(pbrt.Dot((Vector3f)isect.value().intr.n, ez)), 0.999f);
                }
            }
            EXPECT_GT(hits, 4000);
            EXPECT_GT(misses, 1500);
        }

        // A plane tiled by hexagons whose vertices are shared (bitwise), with one hexagon replaced by a Quad and two Triangles (a seam between each pair of kinds):
        // rays aimed at points of the shared sides, and at the vertices, pass through some shape of the mesh.
        [TEST("Ngon", "WatertightAtSharedEdgesAndVertices")]
        public static void Ngon_WatertightAtSharedEdgesAndVertices()
        {
            Random random = new Random(77);
            int rays = 0, missed = 0;
            for (int trial = 0; trial < 12; ++trial)
            {
                Vector3f ex, ey, ez;
                RandomPlane(random, out ex, out ey, out ez);
                Point3f origin = new Point3f((Float)(random.NextDouble() * 2 - 1), (Float)(random.NextDouble() * 2 - 1), (Float)(random.NextDouble() * 2 - 1));
                // vertices of a hexagonal tiling: cell (i, j) has its hexagon around the center of the lattice point
                const int cells = 5;
                double radius = 0.5;
                double width = Math.Sqrt(3) * radius;
                Dictionary<long, int> lookup = new Dictionary<long, int>();
                List<Point3f> points = new List<Point3f>();
                List<int[]> polygons = new List<int[]>();
                for (int j = 0; j < cells; ++j)
                {
                    for (int i = 0; i < cells; ++i)
                    {
                        double cx = (i + 0.5 * (j & 1)) * width, cy = j * 1.5 * radius;
                        int[] corners = new int[6];
                        for (int k = 0; k < 6; ++k)
                        {
                            double angle = Math.PI / 6 + k * Math.PI / 3;
                            // integer key of the vertex (in units of width / 4, radius / 4)
                            long kx = (long)Math.Round((cx + radius * Math.Cos(angle)) / (width / 2) * 2);
                            long ky = (long)Math.Round((cy + radius * Math.Sin(angle)) / (radius / 2) * 2);
                            long key = kx * 100003 + ky;
                            int index;
                            if (!lookup.TryGetValue(key, out index))
                            {
                                index = points.Count;
                                lookup[key] = index;
                                points.Add(Embed(new double[] { cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle) }, origin, ex, ey));
                            }
                            corners[k] = index;
                        }
                        polygons.Add(corners);
                    }
                }

                // The hexagon in the middle of the grid becomes a Quad (corners 0, 1, 2, 3) and two Triangles (3, 4, 5) and (3, 5, 0) -- not convex-planar issues:
                // a hexagon cut by a chord of corners 0 and 3 gives two quads, which are Quads
                List<Shape> shapes = new List<Shape>();
                int middle = (cells / 2) * cells + cells / 2;
                for (int h = 0; h < polygons.Count; ++h)
                {
                    int[] corners = polygons[h];
                    if (h != middle)
                    {
                        Point3f[] cp = new Point3f[6];
                        for (int k = 0; k < 6; ++k)
                        {
                            cp[k] = points[corners[k]];
                        }
                        shapes.Add(MakeNgon(cp, true));
                        continue;
                    }
                    Point3f[] first = { points[corners[0]], points[corners[1]], points[corners[2]], points[corners[3]] };
                    Point3f[] second = { points[corners[3]], points[corners[4]], points[corners[5]], points[corners[0]] };
                    List<int> quadIndices = new List<int> { 0, 1, 2, 3 };
                    pstd.vector<Shape> q1 = Quad.CreateShapes(new Transform(), false, quadIndices, new List<Point3f>(first), new List<Normal3f>(),
                                                              new List<Point2f>(), new List<int>(), false, new Allocator());
                    shapes.Add(q1[0]);
                    // the second half as two Triangles
                    TriangleMesh mesh = new TriangleMesh(new Transform(), false, new pstd.vector<int>(new List<int> { 0, 1, 2, 0, 2, 3 }),
                                                         new pstd.vector<Point3f>(new List<Point3f>(second)), new pstd.vector<Vector3f>(),
                                                         new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(), new pstd.vector<int>(), new Allocator());
                    pstd.vector<Shape> triangles = Triangle.CreateTriangles(mesh, new Allocator());
                    shapes.Add(triangles[0]);
                    shapes.Add(triangles[1]);
                }

                // the sides shared by two polygons (a side of the outer boundary may be missed legitimately), and the chords inside the split hexagon
                Dictionary<long, int> sideUse = new Dictionary<long, int>();
                for (int h = 0; h < polygons.Count; ++h)
                {
                    for (int k = 0; k < 6; ++k)
                    {
                        int sa = polygons[h][k], sb = polygons[h][(k + 1) % 6];
                        long key = (long)Math.Min(sa, sb) * 1000003L + Math.Max(sa, sb);
                        int uses;
                        sideUse.TryGetValue(key, out uses);
                        sideUse[key] = uses + 1;
                    }
                }
                List<int[]> targets = new List<int[]>();
                int[] vertexUse = new int[points.Count];
                for (int h = 0; h < polygons.Count; ++h)
                {
                    for (int k = 0; k < 6; ++k)
                    {
                        ++vertexUse[polygons[h][k]];
                    }
                }
                for (int h = 0; h < polygons.Count; ++h)
                {
                    int[] hexagon = polygons[h];
                    for (int k = 0; k < 6; ++k)
                    {
                        int sa = hexagon[k], sb = hexagon[(k + 1) % 6];
                        if (sideUse[(long)Math.Min(sa, sb) * 1000003L + Math.Max(sa, sb)] == 2)
                        {
                            targets.Add(new int[] { sa, sb });
                        }
                    }
                }
                int[] mid = polygons[middle];
                targets.Add(new int[] { mid[0], mid[3] });
                targets.Add(new int[] { mid[3], mid[5] });
                for (int t = 0; t < targets.Count; ++t)
                {
                    for (int s = 0; s <= 8; ++s)
                    {
                        Float f = s == 0 ? 0 : (s == 8 ? 1 : (Float)random.NextDouble());
                        // a vertex counts only where it is interior to the tiling (3 polygons): a ray aimed at a boundary vertex can legitimately pass outside
                        if ((s == 0 && vertexUse[targets[t][0]] < 3 && targets[t][0] != mid[0] && targets[t][0] != mid[3]) ||
                            (s == 8 && vertexUse[targets[t][1]] < 3 && targets[t][1] != mid[3] && targets[t][1] != mid[5]))
                        {
                            continue;
                        }
                        Point3f target = points[targets[t][0]] + f * (points[targets[t][1]] - points[targets[t][0]]);
                        Point3f from = target + RandomDirection(random) * (Float)(0.5 + random.NextDouble() * 3);
                        Vector3f d = pbrt.Normalize(target - from);
                        if (std.abs(pbrt.Dot(d, ez)) < 0.1f)
                        {
                            continue;
                        }
                        // only sides inside the tiling (shared by two polygons) count: a boundary side may be missed legitimately
                        Ray ray = new Ray(from, d);
                        bool hit = false;
                        for (int i = 0; i < shapes.Count && !hit; ++i)
                        {
                            hit = shapes[i].IntersectP(ray);
                        }
                        ++rays;
                        if (!hit)
                        {
                            ++missed;
                            if (missed <= 6)
                            {
                                Console.WriteLine("DBG miss target " + t + " of " + targets.Count + " s=" + s + " f=" + f + " cos=" + std.abs(pbrt.Dot(d, ez)));
                            }
                        }
                    }
                }
            }
            EXPECT_GT(rays, 1000);
            Console.WriteLine("[Ngon] watertight: " + missed + " of " + rays + " rays missed");
            EXPECT_EQ(0, missed);
        }

        // The polygon as seen from a reference point: the cells of its (u, v) box, integrated by quadrature. weight: cos at the surface times (cos at the reference
        // if projected) over the squared distance.
        private static double[] CellWeights(double[][] polygon, Point3f origin, Vector3f ex, Vector3f ey, Vector3f ez, Point3f p, Vector3f n, bool projected, bool uniformArea, int cells,
                                            int sub, double minX, double maxX, double minY, double maxY)
        {
            double[] weights = new double[cells * cells];
            double cellWidth = (maxX - minX) / cells, cellHeight = (maxY - minY) / cells;
            double area = cellWidth * cellHeight / (sub * sub);
            for (int cu = 0; cu < cells; ++cu)
            {
                for (int cv = 0; cv < cells; ++cv)
                {
                    double sum = 0;
                    for (int a = 0; a < sub; ++a)
                    {
                        for (int b = 0; b < sub; ++b)
                        {
                            double x = minX + (cu + (a + 0.5) / sub) * cellWidth;
                            double y = minY + (cv + (b + 0.5) / sub) * cellHeight;
                            if (!Inside2D(polygon, x, y))
                            {
                                continue;
                            }
                            Point3f q = Embed(new double[] { x, y }, origin, ex, ey);
                            Vector3f w = q - p;
                            double distance2 = pbrt.LengthSquared(w);
                            double distance = Math.Sqrt(distance2);
                            double cosSurface = Math.Abs(pbrt.Dot(w, ez)) / distance;
                            double value = cosSurface / distance2;
                            if (projected)
                            {
                                value *= Math.Abs(pbrt.Dot(w, n)) / distance;
                            }
                            sum += (uniformArea ? 1.0 : value) * area;
                        }
                    }
                    weights[cu * cells + cv] = sum;
                }
            }
            return weights;
        }

        private static void CheckSampling(int n, bool projected, int seed, bool edgeOn, bool rectangle = false)
        {
            Random random = new Random(seed);
            double[][] polygon;
            if (rectangle)
            {
                double w = 0.4 + random.NextDouble() * 1.6, h = 0.4 + random.NextDouble() * 1.6;
                polygon = new double[][] { new double[] { 0, 0 }, new double[] { w, 0 }, new double[] { w, h }, new double[] { 0, h } };
            }
            else
            {
                polygon = ConvexPolygon2D(random, n, 1);
            }
            Vector3f ex, ey, ez;
            RandomPlane(random, out ex, out ey, out ez);
            Point3f origin = new Point3f(0.3f, -0.2f, 0.5f);
            Point3f[] corners = new Point3f[n];
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < n; ++i)
            {
                corners[i] = Embed(polygon[i], origin, ex, ey);
                minX = Math.Min(minX, polygon[i][0]);
                maxX = Math.Max(maxX, polygon[i][0]);
                minY = Math.Min(minY, polygon[i][1]);
                maxY = Math.Max(maxY, polygon[i][1]);
            }
            Shape ngon = rectangle ? MakeRectangle(corners, projected) : MakeNgon(corners, projected);

            // The reference point: off to a side of the polygon, at a distance of about its size (close enough for a large solid angle); with a normal in a random
            // direction (half of the polygon may then be below the horizon)
            Vector3f toSide = RandomDirection(random);
            Point3f p = origin + toSide * (Float)(edgeOn ? 2.5 : 1.2);
            if (edgeOn)
            {
                // nearly in the plane of the polygon
                p = origin + (Float)3 * ex + (Float)0.02 * ez;
            }
            Vector3f nrm = RandomDirection(random);
            Normal3f ns = new Normal3f(nrm.x, nrm.y, nrm.z);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p, new Vector3f(0, 0, 0)), ns, ns, 0);

            const int cells = 6;
            double[] expected = CellWeights(polygon, origin, ex, ey, ez, p, nrm, projected, edgeOn && !projected && !rectangle, cells, 40, minX, maxX, minY, maxY);
            double total = 0;
            for (int c = 0; c < expected.Length; ++c)
            {
                total += expected[c];
            }

            int[] counts = new int[cells * cells];
            int accepted = 0;
            double worstPdfError = 0;
            int boundaryMisses = 0;
            double worstSolidAngleError = 0;
            const int count = 60000;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = ngon.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                if (!ss.has_value() || ss.value().pdf == 0)
                {
                    continue;
                }
                // position in the plane of the polygon
                Vector3f rel = ss.value().intr.p() - origin;
                double x = pbrt.Dot(rel, ex), y = pbrt.Dot(rel, ey);
                int cu = Math.Min(cells - 1, Math.Max(0, (int)((x - minX) / (maxX - minX) * cells)));
                int cv = Math.Min(cells - 1, Math.Max(0, (int)((y - minY) / (maxY - minY) * cells)));
                ++counts[cu * cells + cv];
                ++accepted;
                if (rectangle && !projected && !edgeOn)
                {
                    // the density is one over the solid angle (the integral of the cosine over the squared distance, by quadrature)
                    worstSolidAngleError = Math.Max(worstSolidAngleError, Math.Abs(1 / (ss.value().pdf * total) - 1));
                }
                // the density of Sample and PDF agree
                Vector3f wi = pbrt.Normalize(ss.value().intr.p() - p);
                Float pdf = ngon.PDF(ctx, wi);
                double mismatch = Math.Abs(pdf - ss.value().pdf) / ss.value().pdf;
                if (mismatch > 2e-3)
                {
                    // a sample on the boundary (to rounding): the ray back to it can miss, as for a Triangle
                    ++boundaryMisses;
                    continue;
                }
                worstPdfError = Math.Max(worstPdfError, mismatch);
            }
            EXPECT_GT(accepted, count * 99 / 100);
            double chiSquare = 0, worstZ = 0;
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
            Console.WriteLine((rectangle ? "[Rectangle] n=" : "[Ngon] n=") + n + (projected ? " projected" : " solidangle") + (edgeOn ? " edge-on" : "") + ": worst |z| " +
                              worstZ.ToString("F2", CultureInfo.InvariantCulture) + ", chi2/dof " +
                              (chiSquare / Math.Max(degrees, 1)).ToString("F2", CultureInfo.InvariantCulture) + ", pdf mismatches " + boundaryMisses + ", worst pdf mismatch " +
                              worstPdfError.ToString("E1", CultureInfo.InvariantCulture));
            EXPECT_LT(worstZ, 5.0);
            EXPECT_LT(chiSquare / Math.Max(degrees, 1), 2.0);
            EXPECT_LT(boundaryMisses, accepted / 2000);
            EXPECT_LT(worstSolidAngleError, 2e-3);
            EXPECT_LT(worstPdfError, 2e-3);
        }

        [TEST("Ngon", "ProjectedSamplingMatchesQuadrature")]
        public static void Ngon_ProjectedSamplingMatchesQuadrature()
        {
            for (int n = 3; n <= Ngon.MaxCornerCount; ++n)
            {
                CheckSampling(n, true, 100 + n, false);
            }
            CheckSampling(5, true, 300, true);
        }

        [TEST("Ngon", "SolidAngleSamplingMatchesQuadrature")]
        public static void Ngon_SolidAngleSamplingMatchesQuadrature()
        {
            for (int n = 3; n <= Ngon.MaxCornerCount; ++n)
            {
                CheckSampling(n, false, 200 + n, false);
            }
            CheckSampling(6, false, 301, true);
        }

        [TEST("Rectangle", "SolidAngleSamplingMatchesQuadrature")]
        public static void Rectangle_SolidAngleSamplingMatchesQuadrature()
        {
            for (int seed = 700; seed < 712; ++seed)
            {
                CheckSampling(4, false, seed, false, true);
            }
            CheckSampling(4, false, 750, true, true);
        }

        [TEST("Rectangle", "ProjectedSamplingMatchesQuadrature")]
        public static void Rectangle_ProjectedSamplingMatchesQuadrature()
        {
            for (int seed = 800; seed < 806; ++seed)
            {
                CheckSampling(4, true, seed, false, true);
            }
        }
    }
}  // namespace pbrt
