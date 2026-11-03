// (not in pbrt-v4) vectordisplacement_test.cs
//
// Tests of vector displacement: the tessellation free shapes (vectordisplacement.cs) against a brute force intersection of all micro
// triangles, against the scalar shell of displacedmesh.cs, and against the tessellated displacement of pbrt.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class VectorDisplacementTests
    {
        private const double TwoPi = 2 * Math.PI;

        // The vector field of a curling wave: the lateral displacement folds the surface over itself (dVx/du < -dP/du)
        private static void Curl(double u, double v, out double x, out double y, out double z)
        {
            x = -0.45 * Math.Sin(TwoPi * u);
            y = 0.1 * Math.Sin(TwoPi * v);
            z = 0.25 * Math.Cos(TwoPi * u) + 0.1 * Math.Sin(2 * TwoPi * v);
        }

        private static VectorMapImage MapOf(int cells, Func<double, double, double[]> field)
        {
            int width = cells + 1;
            float[] values = new float[3 * width * width];
            for (int j = 0; j < width; ++j)
            {
                for (int i = 0; i < width; ++i)
                {
                    double[] v = field((double)i / cells, (double)j / cells);
                    for (int c = 0; c < 3; ++c)
                    {
                        values[3 * (j * width + i) + c] = (float)v[c];
                    }
                }
            }
            return new VectorMapImage(width, width, values);
        }

        private static double[] CurlField(double u, double v)
        {
            Curl(u, v, out double x, out double y, out double z);
            return new double[] { x, y, z };
        }

        private static List<VectorDisplacedTriangle> Triangles(VectorDisplacedMesh mesh)
        {
            List<VectorDisplacedTriangle> list = new List<VectorDisplacedTriangle>();
            for (int i = 0; i < mesh.TriangleCount(); ++i)
            {
                list.Add(new VectorDisplacedTriangle(mesh, i));
            }
            return list;
        }

        private static double Closest(List<VectorDisplacedTriangle> triangles, Ray ray, out SurfaceInteraction interaction)
        {
            interaction = default;
            double best = -1;
            Float tMax = Infinity;
            foreach (VectorDisplacedTriangle triangle in triangles)
            {
                pstd.optional<ShapeIntersection> result = triangle.Intersect(ray, tMax);
                if (result)
                {
                    interaction = result.value().intr;
                    tMax = result.value().tHit;
                    best = tMax;
                }
            }
            return best;
        }

        // Moller-Trumbore, double precision; the distance or -1
        private static double Moller(Point3f o, Vector3f d, SdfVector a, SdfVector b, SdfVector c, double tMax)
        {
            SdfVector w = new SdfVector(d.x, d.y, d.z), origin = new SdfVector(o.x, o.y, o.z);
            SdfVector e1 = b - a, e2 = c - a;
            SdfVector pvec = SdfVector.Cross(w, e2);
            double determinant = SdfVector.Dot(e1, pvec);
            if (Math.Abs(determinant) < 1e-300)
            {
                return -1;
            }
            double inverse = 1 / determinant;
            SdfVector tvec = origin - a;
            double s = SdfVector.Dot(tvec, pvec) * inverse;
            if (s < 0 || s > 1)
            {
                return -1;
            }
            SdfVector qvec = SdfVector.Cross(tvec, e1);
            double t = SdfVector.Dot(w, qvec) * inverse;
            if (t < 0 || s + t > 1)
            {
                return -1;
            }
            double distance = SdfVector.Dot(e2, qvec) * inverse;
            return distance > 0 && distance < tMax ? distance : -1;
        }

        // The micro triangles of the displaced unit square (-1..1)^2 with the identity frame, as the shape defines them
        private static List<SdfVector[]> SquareMicroTriangles(int cells, Func<double, double, double[]> field, bool uvSwapped = false)
        {
            List<SdfVector[]> list = new List<SdfVector[]>();
            SdfVector[,] grid = new SdfVector[cells + 1, cells + 1];
            for (int j = 0; j <= cells; ++j)
            {
                for (int i = 0; i <= cells; ++i)
                {
                    double[] v = field((double)i / cells, (double)j / cells);
                    grid[i, j] = new SdfVector(-1 + 2.0 * i / cells + v[0], -1 + 2.0 * j / cells + v[1], v[2]);
                }
            }
            for (int j = 0; j < cells; ++j)
            {
                for (int i = 0; i < cells; ++i)
                {
                    list.Add(new SdfVector[] { grid[i, j], grid[i + 1, j], grid[i + 1, j + 1] });
                    list.Add(new SdfVector[] { grid[i, j], grid[i + 1, j + 1], grid[i, j + 1] });
                }
            }
            return list;
        }

        private static double BruteForce(List<SdfVector[]> triangles, Ray ray, out int layers)
        {
            double best = double.MaxValue;
            layers = 0;
            foreach (SdfVector[] t in triangles)
            {
                double d = Moller(ray.o, ray.d, t[0], t[1], t[2], double.MaxValue);
                if (d > 0)
                {
                    ++layers;
                    best = Math.Min(best, d);
                }
            }
            return best == double.MaxValue ? -1 : best;
        }

        // Rays toward random points of the sheet, from both sides and at all angles
        private static Ray[] Rays(int count, int seed)
        {
            RNG rng = new RNG((ulong)seed);
            Ray[] rays = new Ray[count];
            for (int i = 0; i < count; ++i)
            {
                double side = i % 4 == 3 ? -1 : 1;
                double angle = i % 3 == 0 ? 1.2 : (i % 3 == 1 ? 0.6 : 0.1);
                double azimuth = TwoPi * rng.Uniform<Float>();
                Point3f target = new Point3f((Float)(2 * rng.Uniform<Float>() - 1), (Float)(2 * rng.Uniform<Float>() - 1), 0.1f);
                Vector3f d = pbrt.Normalize(new Vector3f((Float)(Math.Sin(angle) * Math.Cos(azimuth)), (Float)(Math.Sin(angle) * Math.Sin(azimuth)),
                                                          (Float)(-side * Math.Cos(angle))));
                Point3f o = target - d * 2.5f;
                rays[i] = new Ray(o, d);
            }
            return rays;
        }

        private static VectorDisplacedMesh SquareMesh(ShellVectorField field, bool tangent = true, bool smooth = false)
        {
            DisplacedMeshTests.Square(out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            return new VectorDisplacedMesh(new Transform(), false, indices, p, n, uv, field, tangent, smooth);
        }

        [TEST("Vector", "OverhangingSheetMatchesTheBruteForceMicroTriangles")]
        public static void Vector_OverhangingSheetMatchesTheBruteForceMicroTriangles()
        {
            int cells = 48;
            VectorDisplacedMesh mesh = SquareMesh(new ShellVectorGrid(MapOf(cells, CurlField)));
            List<VectorDisplacedTriangle> triangles = Triangles(mesh);
            List<SdfVector[]> micro = SquareMicroTriangles(cells, CurlField);
            Ray[] rays = Rays(3000, 5);
            int hits = 0, mismatches = 0, layered = 0, shadowDisagreements = 0;
            double worst = 0;
            foreach (Ray ray in rays)
            {
                double expected = BruteForce(micro, ray, out int layers);
                double actual = Closest(triangles, ray, out SurfaceInteraction unused);
                if (layers >= 2)
                {
                    ++layered;
                }
                if (expected >= 0)
                {
                    ++hits;
                }
                if ((expected < 0) != (actual < 0) || (expected >= 0 && Math.Abs(expected - actual) > 2e-5))
                {
                    ++mismatches;
                }
                else if (expected >= 0)
                {
                    worst = Math.Max(worst, Math.Abs(expected - actual));
                }
                bool any = false;
                foreach (VectorDisplacedTriangle triangle in triangles)
                {
                    any |= triangle.IntersectP(ray, Infinity);
                }
                if (any != (expected >= 0))
                {
                    ++shadowDisagreements;
                }
            }
            Console.WriteLine("vector sheet: " + rays.Length + " rays, " + hits + " hits, " + layered + " with two or more layers, " + mismatches +
                              " mismatches, " + shadowDisagreements + " IntersectP disagreements, worst t error " + worst);
            CHECK_GT(hits, 1500);
            CHECK_GT(layered, 100);
            CHECK_LE(mismatches, 6);
            CHECK_LE(shadowDisagreements, 6);
        }

        [TEST("Vector", "ZeroLateralVectorsEqualTheScalarShell")]
        public static void Vector_ZeroLateralVectorsEqualTheScalarShell()
        {
            int cells = 64;
            Func<double, double, double> height = (double u, double v) => 0.5 * (0.5 + 0.4 * Math.Sin(7 * u + 1) * Math.Cos(5 * v) + 0.08 * Math.Sin(31 * u) * Math.Cos(29 * v));
            DisplacedMeshTests.Square(out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            DisplacedMesh scalar = DisplacedMeshTests.MakeMesh(p, indices, uv, n, (double u, double v) => height(u, v) / 0.5, cells + 1, 0, 0.5);
            List<DisplacedTriangle> scalarTriangles = DisplacedMeshTests.Triangles(scalar);
            VectorDisplacedMesh vector = SquareMesh(new ShellVectorGrid(MapOf(cells, (double u, double v) => new double[] { 0, 0, height(u, v) })));
            List<VectorDisplacedTriangle> vectorTriangles = Triangles(vector);
            Ray[] rays = Rays(2000, 9);
            int mismatches = 0, hits = 0;
            foreach (Ray ray in rays)
            {
                bool a = DisplacedMeshTests.Closest(scalarTriangles, ray, out ShapeIntersection scalarHit);
                double b = Closest(vectorTriangles, ray, out SurfaceInteraction unused);
                hits += a ? 1 : 0;
                if (a != (b >= 0) || (a && Math.Abs(scalarHit.tHit - b) > 3e-5))
                {
                    ++mismatches;
                }
            }
            Console.WriteLine("vector against scalar shell: " + hits + " hits, " + mismatches + " mismatches");
            CHECK_GT(hits, 500);
            CHECK_LE(mismatches, 4);
        }

        [TEST("Vector", "TangentAndObjectSpaceUseTheirFrames")]
        public static void Vector_TangentAndObjectSpaceUseTheirFrames()
        {
            // A square in z = 0 whose uv is swapped, (u, v) = (y, x): dP/du = +y, dP/dv = +x, so the uv map is left handed with respect to
            // the normal +z, and the frame is T = y, B = x, N = z
            Point3f[] p = new Point3f[4] { new Point3f(-1, -1, 0), new Point3f(1, -1, 0), new Point3f(1, 1, 0), new Point3f(-1, 1, 0) };
            int[] indices = new int[6] { 0, 1, 2, 0, 2, 3 };
            Point2f[] uv = new Point2f[4];
            Normal3f[] n = new Normal3f[4];
            for (int i = 0; i < 4; ++i)
            {
                uv[i] = new Point2f((p[i].y + 1) / 2, (p[i].x + 1) / 2);
                n[i] = new Normal3f(0, 0, 1);
            }
            VectorMapImage image = MapOf(8, (double u, double v) => new double[] { 0.1, 0.2, 0.3 });
            Ray ray = new Ray(new Point3f(0.5f, 0.5f, 1), new Vector3f(0, 0, -1));

            VectorDisplacedMesh tangent = new VectorDisplacedMesh(new Transform(), false, indices, p, n, uv, new ShellVectorGrid(image), true, false);
            double t = Closest(Triangles(tangent), ray, out SurfaceInteraction hit);
            // The displacement is 0.1 T + 0.2 B + 0.3 N = 0.1 y + 0.2 x + 0.3 z: the point (0.5, 0.5) is the image of (0.3, 0.4)
            Console.WriteLine("tangent: t " + t + ", uv " + hit.uv.x + " " + hit.uv.y);
            CHECK_LT(Math.Abs(t - 0.7), 1e-6);
            CHECK_LT(Math.Abs(hit.uv.x - 0.7), 1e-5);
            CHECK_LT(Math.Abs(hit.uv.y - 0.65), 1e-5);
            CHECK_GT(hit.n.z, 0.999);

            VectorDisplacedMesh objectSpace = new VectorDisplacedMesh(new Transform(), false, indices, p, n, uv, new ShellVectorGrid(image), false, false);
            t = Closest(Triangles(objectSpace), ray, out hit);
            // The displacement is (0.1, 0.2, 0.3): the image of (0.4, 0.3)
            Console.WriteLine("object: t " + t + ", uv " + hit.uv.x + " " + hit.uv.y);
            CHECK_LT(Math.Abs(t - 0.7), 1e-6);
            CHECK_LT(Math.Abs(hit.uv.x - 0.65), 1e-5);
            CHECK_LT(Math.Abs(hit.uv.y - 0.7), 1e-5);
        }

        [TEST("Vector", "BoundsContainTheDisplacedSurface")]
        public static void Vector_BoundsContainTheDisplacedSurface()
        {
            int cells = 48;
            VectorDisplacedMesh mesh = SquareMesh(new ShellVectorGrid(MapOf(cells, CurlField)));
            List<VectorDisplacedTriangle> triangles = Triangles(mesh);
            Bounds3f box = triangles[0].Bounds();
            box = pbrt.Union(box, triangles[1].Bounds());
            int outside = 0;
            foreach (SdfVector[] t in SquareMicroTriangles(cells, CurlField))
            {
                foreach (SdfVector q in t)
                {
                    if (q.x < box.pMin.x || q.y < box.pMin.y || q.z < box.pMin.z || q.x > box.pMax.x || q.y > box.pMax.y || q.z > box.pMax.z)
                    {
                        ++outside;
                    }
                }
            }
            Console.WriteLine("bounds: " + outside + " vertices outside, box " + box.pMin + " " + box.pMax);
            CHECK_EQ(outside, 0);
        }

        [TEST("Vector", "NodeBoxesContainTheVectors")]
        public static void Vector_NodeBoxesContainTheVectors()
        {
            int cells = 64;
            ShellVectorGrid grid = new ShellVectorGrid(MapOf(cells, CurlField));
            ShellVectorProcedural procedural = new ShellVectorProcedural(
                ShellExpression.Parse("-0.45*sin(6.2831853*u)"), ShellExpression.Parse("0.1*sin(6.2831853*v)"),
                ShellExpression.Parse("0.25*cos(6.2831853*u)+0.1*sin(12.566371*v)"), 6, 1);
            int outside = 0, checkedNodes = 0;
            double[] box = new double[6];
            foreach (ShellVectorField field in new ShellVectorField[] { grid, procedural })
            {
                for (int level = 0; level < field.levels; ++level)
                {
                    for (int j = 0; j < field.LevelHeight(level); ++j)
                    {
                        for (int i = 0; i < field.LevelWidth(level); ++i)
                        {
                            field.NodeBox(level, i, j, box);
                            int size = 1 << level;
                            for (int y = j * size; y <= Math.Min((j + 1) * size, field.cellsY); ++y)
                            {
                                for (int x = i * size; x <= Math.Min((i + 1) * size, field.cellsX); ++x)
                                {
                                    field.Sample(x, y, out double vx, out double vy, out double vz);
                                    if (vx < box[0] - 1e-6 || vy < box[1] - 1e-6 || vz < box[2] - 1e-6 || vx > box[3] + 1e-6 ||
                                        vy > box[4] + 1e-6 || vz > box[5] + 1e-6)
                                    {
                                        ++outside;
                                    }
                                }
                            }
                            ++checkedNodes;
                        }
                    }
                }
            }
            Console.WriteLine("node boxes: " + checkedNodes + " nodes, " + outside + " vectors outside their box");
            CHECK_EQ(outside, 0);
        }

        [TEST("Vector", "ProceduralFieldMatchesTheSampledGrid")]
        public static void Vector_ProceduralFieldMatchesTheSampledGrid()
        {
            int depth = 5, cells = 1 << depth;
            string[] source = new string[] { "-0.45*sin(6.2831853*u)", "0.1*sin(6.2831853*v)", "0.25*cos(6.2831853*u)+0.1*sin(12.566371*v)" };
            ShellExpression[] expressions = new ShellExpression[] { ShellExpression.Parse(source[0]), ShellExpression.Parse(source[1]),
                                                                    ShellExpression.Parse(source[2]) };
            VectorDisplacedMesh procedural = SquareMesh(new ShellVectorProcedural(expressions[0], expressions[1], expressions[2], depth, 1));
            VectorDisplacedMesh sampled = SquareMesh(new ShellVectorGrid(MapOf(cells, (double u, double v) =>
                new double[] { expressions[0].Evaluate(u, v), expressions[1].Evaluate(u, v), expressions[2].Evaluate(u, v) })));
            List<VectorDisplacedTriangle> a = Triangles(procedural), b = Triangles(sampled);
            Ray[] rays = Rays(1500, 21);
            int mismatches = 0, hits = 0;
            foreach (Ray ray in rays)
            {
                double ta = Closest(a, ray, out SurfaceInteraction unused);
                double tb = Closest(b, ray, out unused);
                hits += ta >= 0 ? 1 : 0;
                if ((ta < 0) != (tb < 0) || (ta >= 0 && Math.Abs(ta - tb) > 2e-5))
                {
                    ++mismatches;
                }
            }
            Console.WriteLine("procedural vector field: " + hits + " hits, " + mismatches + " mismatches against the sampled grid");
            CHECK_GT(hits, 300);
            CHECK_LE(mismatches, 3);
        }

        [TEST("Vector", "ShellAgreesWithTheTessellatedDisplacementOfPbrt")]
        public static void Vector_ShellAgreesWithTheTessellatedDisplacementOfPbrt()
        {
            int cells = 64;
            VectorMapImage image = MapOf(cells, CurlField);
            DisplacedMeshTests.Square(out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            VectorDisplacedMesh shell = SquareMesh(new ShellVectorGrid(image));
            List<VectorDisplacedTriangle> shellTriangles = Triangles(shell);

            TriQuadMesh mesh = new TriQuadMesh();
            mesh.p = new pstd.vector<Point3f>(p);
            mesh.n = new pstd.vector<Normal3f>(n);
            mesh.uv = new pstd.vector<Point2f>(uv);
            mesh.triIndices = new pstd.vector<int>(indices);
            // Edges shorter than a little more than a cell: the refined mesh has the vertices of the grid
            TriQuadMesh tessellated = mesh.Displace((Point3f a, Point3f b) => pbrt.Distance(a, b), 1.5f * 2 / cells,
                                                    (TriQuadMesh refined) => VectorTessellation.Apply(refined, image, true));
            int[] tessellatedIndices = tessellated.triIndices.ToArray();
            Point3f[] tessellatedP = tessellated.p.ToArray();
            List<SdfVector[]> triangles = new List<SdfVector[]>();
            for (int t = 0; t < tessellatedIndices.Length / 3; ++t)
            {
                SdfVector[] q = new SdfVector[3];
                for (int k = 0; k < 3; ++k)
                {
                    Point3f point = tessellatedP[tessellatedIndices[3 * t + k]];
                    q[k] = new SdfVector(point.x, point.y, point.z);
                }
                triangles.Add(q);
            }
            Ray[] rays = Rays(1500, 33);
            int agreeing = 0, hitsBoth = 0, disagreeingHits = 0;
            List<double> errors = new List<double>();
            foreach (Ray ray in rays)
            {
                double expected = BruteForce(triangles, ray, out int layers);
                double actual = Closest(shellTriangles, ray, out SurfaceInteraction unused);
                if ((expected < 0) == (actual < 0))
                {
                    ++agreeing;
                    if (expected >= 0)
                    {
                        ++hitsBoth;
                        errors.Add(Math.Abs(expected - actual));
                    }
                }
                else
                {
                    ++disagreeingHits;
                }
            }
            errors.Sort();
            double median = errors[errors.Count / 2], ninetyFifth = errors[(int)(0.95 * errors.Count)];
            Console.WriteLine("shell against tessellated: " + tessellatedIndices.Length / 3 + " triangles, " + hitsBoth + " common hits, " + disagreeingHits +
                              " hit/miss disagreements, |dt| median " + median + ", 95th percentile " + ninetyFifth);
            CHECK_GT(hitsBoth, 500);
            CHECK_LE(disagreeingHits, 30);
            CHECK_LT(median, 5e-4);
            CHECK_LT(ninetyFifth, 5e-3);
        }

        [TEST("Vector", "CurvedMeshHasNoCracksAlongBaseEdges")]
        public static void Vector_CurvedMeshHasNoCracksAlongBaseEdges()
        {
            DisplacedMeshTests.Cap(6, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            int cells = 96;
            VectorMapImage image = MapOf(cells, (double u, double v) => new double[] { 0.06 * Math.Sin(TwoPi * 3 * v), 0.06 * Math.Cos(TwoPi * 3 * u),
                                                                                       0.08 + 0.05 * Math.Sin(TwoPi * 2 * u) * Math.Cos(TwoPi * 2 * v) });
            VectorDisplacedMesh shell = new VectorDisplacedMesh(new Transform(), false, indices, p, n, uv, new ShellVectorGrid(image), true, false);
            List<VectorDisplacedTriangle> triangles = Triangles(shell);
            Bounds3f box = triangles[0].Bounds();
            foreach (VectorDisplacedTriangle triangle in triangles)
            {
                box = pbrt.Union(box, triangle.Bounds());
            }
            // Rays at random points of the box, along the average normal of the cap: the number of rays that hit must be stable against a
            // tiny shift of every ray (cracks would make some rays leak through at the base edges and the count would drop)
            RNG rng = new RNG(77);
            int count = 6000, leaks = 0, hits = 0;
            Vector3f d = new Vector3f(0, 0, -1);
            for (int i = 0; i < count; ++i)
            {
                Point3f target = new Point3f(box.pMin.x + (box.pMax.x - box.pMin.x) * rng.Uniform<Float>(),
                                             box.pMin.y + (box.pMax.y - box.pMin.y) * rng.Uniform<Float>(), 0);
                Ray ray = new Ray(new Point3f(target.x, target.y, box.pMax.z + 1), d);
                bool hit = false;
                foreach (VectorDisplacedTriangle triangle in triangles)
                {
                    hit |= triangle.IntersectP(ray, Infinity);
                }
                hits += hit ? 1 : 0;
                // A ray that hits the surface at (x, y) must also hit when it is shifted by less than a micro triangle: look for isolated
                // misses among hits with 4 neighbours
                if (!hit)
                {
                    int neighbours = 0;
                    for (int k = 0; k < 4; ++k)
                    {
                        Point3f o = ray.o + new Vector3f((k == 0 ? 1 : k == 1 ? -1 : 0) * 1e-4f, (k == 2 ? 1 : k == 3 ? -1 : 0) * 1e-4f, 0);
                        bool neighbourHit = false;
                        foreach (VectorDisplacedTriangle triangle in triangles)
                        {
                            neighbourHit |= triangle.IntersectP(new Ray(o, d), Infinity);
                        }
                        neighbours += neighbourHit ? 1 : 0;
                    }
                    if (neighbours == 4)
                    {
                        ++leaks;
                    }
                }
            }
            Console.WriteLine("cap with a vector map: " + hits + " of " + count + " rays hit, " + leaks + " isolated misses (cracks)");
            CHECK_GT(hits, 2000);
            CHECK_EQ(leaks, 0);
        }

        [TEST("Vector", "CurvedMeshAgreesWithTheTessellatedDisplacement")]
        public static void Vector_CurvedMeshAgreesWithTheTessellatedDisplacement()
        {
            DisplacedMeshTests.Cap(6, out Point3f[] p, out int[] indices, out Point2f[] uv, out Normal3f[] n);
            int cells = 96;
            VectorMapImage image = MapOf(cells, (double u, double v) => new double[] { 0.06 * Math.Sin(TwoPi * 3 * v), 0.06 * Math.Cos(TwoPi * 3 * u),
                                                                                       0.08 + 0.05 * Math.Sin(TwoPi * 2 * u) * Math.Cos(TwoPi * 2 * v) });
            VectorDisplacedMesh shell = new VectorDisplacedMesh(new Transform(), false, indices, p, n, uv, new ShellVectorGrid(image), true, false);
            List<VectorDisplacedTriangle> shellTriangles = Triangles(shell);
            TriQuadMesh mesh = new TriQuadMesh();
            mesh.p = new pstd.vector<Point3f>(p);
            mesh.n = new pstd.vector<Normal3f>(n);
            mesh.uv = new pstd.vector<Point2f>(uv);
            mesh.triIndices = new pstd.vector<int>(indices);
            TriQuadMesh tessellated = mesh.Displace((Point3f a, Point3f b) => pbrt.Distance(a, b), 0.01f,
                                                    (TriQuadMesh refined) => VectorTessellation.Apply(refined, image, true));
            int[] tessellatedIndices = tessellated.triIndices.ToArray();
            Point3f[] tessellatedP = tessellated.p.ToArray();
            List<SdfVector[]> triangles = new List<SdfVector[]>();
            for (int t = 0; t < tessellatedIndices.Length / 3; ++t)
            {
                SdfVector[] q = new SdfVector[3];
                for (int k = 0; k < 3; ++k)
                {
                    Point3f point = tessellatedP[tessellatedIndices[3 * t + k]];
                    q[k] = new SdfVector(point.x, point.y, point.z);
                }
                triangles.Add(q);
            }
            Bounds3f box = shellTriangles[0].Bounds();
            foreach (VectorDisplacedTriangle triangle in shellTriangles)
            {
                box = pbrt.Union(box, triangle.Bounds());
            }
            RNG rng = new RNG(5);
            int common = 0, disagreements = 0;
            List<double> errors = new List<double>();
            for (int i = 0; i < 800; ++i)
            {
                Point3f target = new Point3f(box.pMin.x + (box.pMax.x - box.pMin.x) * rng.Uniform<Float>(),
                                             box.pMin.y + (box.pMax.y - box.pMin.y) * rng.Uniform<Float>(), 0);
                Ray ray = new Ray(new Point3f(target.x, target.y, box.pMax.z + 1), new Vector3f(0, 0, -1));
                double expected = BruteForce(triangles, ray, out int layers);
                double actual = Closest(shellTriangles, ray, out SurfaceInteraction unused);
                if ((expected < 0) != (actual < 0))
                {
                    ++disagreements;
                }
                else if (expected >= 0)
                {
                    ++common;
                    errors.Add(Math.Abs(expected - actual));
                }
            }
            errors.Sort();
            Console.WriteLine("cap, shell against tessellated (" + tessellatedIndices.Length / 3 + " triangles): " + common + " common hits, " + disagreements +
                              " disagreements, |dt| median " + errors[errors.Count / 2] + ", 95th " + errors[(int)(0.95 * errors.Count)] + ", max " +
                              errors[errors.Count - 1]);
            CHECK_GT(common, 400);
            CHECK_LE(disagreements, 16);
            CHECK_LT(errors[errors.Count / 2], 2e-3);
        }

        // The box to mushroom example of the vector displacement literature: the six faces of a box, each with a tile of an atlas, and an
        // object space vector that moves every point of the box to the mushroom (a surface of revolution whose profile, a Catmull-Rom
        // curve through control points, has an undercut cap that a height map cannot give)
        private static readonly double[] ProfileR = new double[] { 0, 0.40, 0.75, 0.98, 1.02, 0.88, 0.60, 0.36, 0.28, 0.27, 0.30, 0.40, 0.22, 0 };
        private static readonly double[] ProfileZ = new double[] { 1.00, 0.97, 0.85, 0.62, 0.42, 0.30, 0.36, 0.46, 0.20, -0.20, -0.58, -0.86, -0.98, -1.00 };

        private static SdfVector Mushroom(SdfVector q)
        {
            SdfVector d = q.Normalized();
            double s = Math.Acos(Math.Max(-1, Math.Min(1, d.z))) / Math.PI;
            int count = ProfileR.Length - 1;
            double x = Math.Max(0, Math.Min(1, s)) * count;
            int k = Math.Min((int)Math.Floor(x), count - 1);
            double t = x - k;
            double[] r = new double[4], z = new double[4];
            for (int m = 0; m < 4; ++m)
            {
                int index = Math.Max(0, Math.Min(count, k - 1 + m));
                r[m] = ProfileR[index];
                z[m] = ProfileZ[index];
            }
            Func<double[], double> curve = (double[] c) => 0.5 * ((2 * c[1]) + (-c[0] + c[2]) * t + (2 * c[0] - 5 * c[1] + 4 * c[2] - c[3]) * t * t +
                                                                  (-c[0] + 3 * c[1] - 3 * c[2] + c[3]) * t * t * t);
            double radius = curve(r), height = curve(z), phi = Math.Atan2(d.y, d.x);
            return new SdfVector(radius * Math.Cos(phi), radius * Math.Sin(phi), height);
        }

        private static readonly SdfVector[][] BoxFaces = new SdfVector[][]
        {
            new SdfVector[] { new SdfVector(1, 0, 0), new SdfVector(0, 1, 0), new SdfVector(0, 0, 1) },
            new SdfVector[] { new SdfVector(-1, 0, 0), new SdfVector(0, 0, 1), new SdfVector(0, 1, 0) },
            new SdfVector[] { new SdfVector(0, 1, 0), new SdfVector(0, 0, 1), new SdfVector(1, 0, 0) },
            new SdfVector[] { new SdfVector(0, -1, 0), new SdfVector(1, 0, 0), new SdfVector(0, 0, 1) },
            new SdfVector[] { new SdfVector(0, 0, 1), new SdfVector(1, 0, 0), new SdfVector(0, 1, 0) },
            new SdfVector[] { new SdfVector(0, 0, -1), new SdfVector(0, 1, 0), new SdfVector(1, 0, 0) }
        };

        // The mesh of the box (24 vertices), the vector map atlas (3 x 2 tiles of faceCells + 1 vertices, a cell of gutter between them) and
        // the micro triangles of the displaced box for the brute force
        private static VectorDisplacedMesh MushroomMesh(int faceCells, out List<SdfVector[]> micro)
        {
            int tile = faceCells + 1, width = 3 * tile, height = 2 * tile;
            float[] values = new float[3 * width * height];
            micro = new List<SdfVector[]>();
            Point3f[] p = new Point3f[24];
            Normal3f[] n = new Normal3f[24];
            Point2f[] uv = new Point2f[24];
            List<int> indexList = new List<int>();
            for (int f = 0; f < 6; ++f)
            {
                SdfVector normal = BoxFaces[f][0], t1 = BoxFaces[f][1], t2 = BoxFaces[f][2];
                int ox = (f % 3) * tile, oy = (f / 3) * tile;
                SdfVector[,] grid = new SdfVector[tile, tile];
                for (int j = 0; j < tile; ++j)
                {
                    for (int i = 0; i < tile; ++i)
                    {
                        SdfVector q = normal + t1 * (2.0 * i / faceCells - 1) + t2 * (2.0 * j / faceCells - 1);
                        SdfVector v = Mushroom(q) - q;
                        int offset = 3 * ((oy + j) * width + ox + i);
                        values[offset] = (float)v.x;
                        values[offset + 1] = (float)v.y;
                        values[offset + 2] = (float)v.z;
                        grid[i, j] = q + new SdfVector(values[offset], values[offset + 1], values[offset + 2]);
                    }
                }
                for (int j = 0; j < faceCells; ++j)
                {
                    for (int i = 0; i < faceCells; ++i)
                    {
                        micro.Add(new SdfVector[] { grid[i, j], grid[i + 1, j], grid[i + 1, j + 1] });
                        micro.Add(new SdfVector[] { grid[i, j], grid[i + 1, j + 1], grid[i, j + 1] });
                    }
                }
                for (int k = 0; k < 4; ++k)
                {
                    int a = k == 1 || k == 2 ? 1 : 0, b = k >= 2 ? 1 : 0;
                    SdfVector q = normal + t1 * (2.0 * a - 1) + t2 * (2.0 * b - 1);
                    p[4 * f + k] = new Point3f((Float)q.x, (Float)q.y, (Float)q.z);
                    n[4 * f + k] = new Normal3f((Float)normal.x, (Float)normal.y, (Float)normal.z);
                    uv[4 * f + k] = new Point2f((Float)(ox + a * faceCells) / (width - 1), (Float)(oy + b * faceCells) / (height - 1));
                }
                indexList.AddRange(new int[] { 4 * f, 4 * f + 1, 4 * f + 2, 4 * f, 4 * f + 2, 4 * f + 3 });
            }
            VectorMapImage image = new VectorMapImage(width, height, values);
            return new VectorDisplacedMesh(new Transform(), false, indexList.ToArray(), p, n, uv, new ShellVectorGrid(image), false, false);
        }

        [TEST("Vector", "BoxToMushroom")]
        public static void Vector_BoxToMushroom()
        {
            VectorDisplacedMesh mesh = MushroomMesh(40, out List<SdfVector[]> micro);
            List<VectorDisplacedTriangle> triangles = Triangles(mesh);

            // Rays from all around toward points of the mushroom, and toward points on the edges of the box (where the faces meet)
            RNG rng = new RNG(3);
            int hits = 0, mismatches = 0, layered = 0, edgeRays = 0, edgeMisses = 0;
            double worst = 0;
            VectorDisplacedTriangle.ResetThreadStatistics();
            for (int i = 0; i < 4000; ++i)
            {
                bool onEdge = i % 4 == 0;
                SdfVector target;
                if (onEdge)
                {
                    // A point on one of the 12 edges of the box, moved to the mushroom
                    int edge = (int)(rng.Uniform<Float>() * 12) % 12;
                    int axis = edge / 4, corner = edge % 4;
                    double along = 2 * rng.Uniform<Float>() - 1;
                    double[] c = new double[3];
                    c[axis] = along;
                    c[(axis + 1) % 3] = (corner & 1) != 0 ? 1 : -1;
                    c[(axis + 2) % 3] = (corner & 2) != 0 ? 1 : -1;
                    target = Mushroom(new SdfVector(c[0], c[1], c[2]));
                }
                else
                {
                    target = new SdfVector(2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1) * 0.9;
                }
                double theta = Math.Acos(1 - 2 * rng.Uniform<Float>()), azimuth = TwoPi * rng.Uniform<Float>();
                Vector3f d = new Vector3f((Float)(Math.Sin(theta) * Math.Cos(azimuth)), (Float)(Math.Sin(theta) * Math.Sin(azimuth)), (Float)Math.Cos(theta));
                Point3f o = new Point3f((Float)(target.x - 3 * d.x), (Float)(target.y - 3 * d.y), (Float)(target.z - 3 * d.z));
                Ray ray = new Ray(o, d);
                double expected = BruteForce(micro, ray, out int layers);
                double actual = Closest(triangles, ray, out SurfaceInteraction unused);
                layered += layers >= 2 ? 1 : 0;
                hits += expected >= 0 ? 1 : 0;
                bool bad = (expected < 0) != (actual < 0) || (expected >= 0 && Math.Abs(expected - actual) > 3e-5);
                mismatches += bad ? 1 : 0;
                if (!bad && expected >= 0)
                {
                    worst = Math.Max(worst, Math.Abs(expected - actual));
                }
                if (onEdge)
                {
                    ++edgeRays;
                    edgeMisses += expected >= 0 && actual < 0 ? 1 : 0;
                }
            }
            ShellStatistics statistics = VectorDisplacedTriangle.ThreadStatistics();
            Console.WriteLine("box to mushroom traversal: " + (double)statistics.nodes / statistics.traces + " nodes and " + (double)statistics.leaves / statistics.traces +
                              " leaves per trace (a trace is one ray against one base triangle)");
            Console.WriteLine("box to mushroom: " + triangles.Count + " base triangles, 4000 rays, " + hits + " hits, " + layered + " with two or more layers, " +
                              mismatches + " mismatches, " + edgeRays + " rays at the edges of the box with " + edgeMisses + " leaks, worst t error " + worst);
            CHECK_GT(hits, 1500);
            CHECK_GT(layered, 200);
            CHECK_LE(mismatches, 8);
            CHECK_EQ(edgeMisses, 0);
            CHECK_LT(worst, 2e-6);

            // The cap overhangs the stem: a ray going up outside the stem (r = 0.7), below the rim, meets the underside of the cap, and its
            // normal points down; the box alone would have given the floor of the box at z = -1
            Ray up = new Ray(new Point3f(0.7f, 0, -1.5f), new Vector3f(0, 0, 1));
            double t = Closest(triangles, up, out SurfaceInteraction under);
            Console.WriteLine("undercut: t " + t + " z " + (-1.5 + t) + " normal z " + under.n.z);
            CHECK_GT(t, 0);
            CHECK_GT(-1.5 + t, 0.2);
            CHECK_LT(-1.5 + t, 0.6);
            CHECK_LT(under.n.z, -0.2);
        }

        [TEST("Vector", "TraversalCostOnALargeMap")]
        public static void Vector_TraversalCostOnALargeMap()
        {
            foreach (int cells in new int[] { 256, 1024 })
            {
                VectorDisplacedMesh mesh = SquareMesh(new ShellVectorGrid(MapOf(cells, CurlField)));
                List<VectorDisplacedTriangle> triangles = Triangles(mesh);
                Ray[] rays = Rays(2000, 4);
                VectorDisplacedTriangle.ResetThreadStatistics();
                int hits = 0;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                foreach (Ray ray in rays)
                {
                    hits += Closest(triangles, ray, out SurfaceInteraction unused) >= 0 ? 1 : 0;
                }
                ShellStatistics statistics = VectorDisplacedTriangle.ThreadStatistics();
                Console.WriteLine("vector traversal at " + cells + "^2 cells: " + hits + " hits, " + (double)statistics.nodes / statistics.traces +
                                  " nodes and " + (double)statistics.leaves / statistics.traces + " leaves per trace, " +
                                  watch.Elapsed.TotalMilliseconds * 1000 / rays.Length + " us per ray");
                CHECK_GT(hits, 800);
            }
        }
    }
}
