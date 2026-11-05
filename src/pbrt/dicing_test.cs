// (not in pbrt-v4) dicing_test.cs
//
// Tests of the diced displacement (dicing.cs, dicing_surfaces.cs): the convergence of the undisplaced dicing, the exact displacement of a constant,
// watertightness, cracks between patches, the sampling of a displaced area light, the shapes without displacement and the trimmed NURBS boundary.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class DicingTests
    {
        private sealed class FunctionTexture : FloatTexture
        {
            private readonly Func<Point3f, Point2f, double> function;

            public FunctionTexture(Func<Point3f, Point2f, double> function)
            {
                this.function = function;
            }

            public override Float Evaluate(TextureEvalContext ctx)
            {
                return (Float)function(ctx.p, ctx.uv);
            }

            public override Float MinValue()
            {
                return -10;
            }

            public override Float MaxValue()
            {
                return 10;
            }

            public override string ToString()
            {
                return "[ FunctionTexture ]";
            }
        }

        private static FloatTexture Constant(double value)
        {
            return new FunctionTexture((Point3f p, Point2f uv) => value);
        }

        private static FloatTexture Bumps(double amplitude, double frequency)
        {
            return new FunctionTexture((Point3f p, Point2f uv) => amplitude * Math.Sin(frequency * p.x) * Math.Cos(frequency * 1.3 * p.y) * Math.Sin(frequency * 0.7 * p.z + 1));
        }

        private static ParsedParameter Floats(string type, string name, params float[] values)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = type;
            p.name = name;
            foreach (float v in values)
            {
                p.floats.push_back(v);
            }
            return p;
        }

        private static ParsedParameter Ints(string name, params int[] values)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = "integer";
            p.name = name;
            foreach (int v in values)
            {
                p.ints.push_back(v);
            }
            return p;
        }

        private static ParsedParameter Text(string type, string name, string value)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = type;
            p.name = name;
            p.strings.push_back(value);
            return p;
        }

        private static ParsedParameter Flag(string name, bool value)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = "bool";
            p.name = name;
            p.bools.push_back(value ? (byte)1 : (byte)0);
            return p;
        }

        private static ParameterDictionary Dictionary(params ParsedParameter[] parameters)
        {
            return new ParameterDictionary(new List<ParsedParameter>(parameters), RGBColorSpace.sRGB);
        }

        private static DicingSettings Settings(FloatTexture texture, double edgeLength)
        {
            return Settings(texture, edgeLength, new Transform(), false);
        }

        private static DicingSettings Settings(FloatTexture texture, double edgeLength, Transform renderFromObject, bool reverse)
        {
            DicingSettings settings = new DicingSettings();
            settings.displacement = texture;
            settings.edgeLength = edgeLength;
            settings.renderFromObject = renderFromObject;
            settings.reverseOrientation = reverse;
            settings.shapeName = "test";
            return settings;
        }

        // The surface of a quadric made by the exact shape creation
        private static IDicedSurface SurfaceOf(string name, params ParsedParameter[] parameters)
        {
            Transform identity = new Transform();
            pstd.vector<Shape> exact = Shape.CreateExact(name, identity, identity, false, Dictionary(parameters), new Dictionary<string, FloatTexture>(), null,
                                                          new Allocator());
            return DicedShapes.SurfaceOf(exact, identity);
        }

        private static SdfVector P(DicedMesh mesh, int i)
        {
            return new SdfVector(mesh.p[i].x, mesh.p[i].y, mesh.p[i].z);
        }

        private static double TriangleArea(DicedMesh mesh, int t)
        {
            return 0.5 * SdfVector.Cross(P(mesh, mesh.indices[3 * t + 1]) - P(mesh, mesh.indices[3 * t]), P(mesh, mesh.indices[3 * t + 2]) - P(mesh, mesh.indices[3 * t])).Length();
        }

        private static double TotalArea(DicedMesh mesh)
        {
            double sum = 0;
            for (int t = 0; t < mesh.TriangleCount; ++t)
            {
                sum += TriangleArea(mesh, t);
            }
            return sum;
        }

        // The number of times every undirected edge occurs, keyed by the positions of its ends
        private static Dictionary<(Point3f, Point3f), int> EdgeCounts(DicedMesh mesh)
        {
            Dictionary<(Point3f, Point3f), int> counts = new Dictionary<(Point3f, Point3f), int>();
            for (int t = 0; t < mesh.TriangleCount; ++t)
            {
                for (int k = 0; k < 3; ++k)
                {
                    Point3f a = mesh.p[mesh.indices[3 * t + k]], b = mesh.p[mesh.indices[3 * t + (k + 1) % 3]];
                    bool swap = a.x != b.x ? a.x > b.x : (a.y != b.y ? a.y > b.y : a.z > b.z);
                    (Point3f, Point3f) key = swap ? (b, a) : (a, b);
                    counts.TryGetValue(key, out int c);
                    counts[key] = c + 1;
                }
            }
            return counts;
        }

        private static SdfVector RandomOnTriangle(DicedMesh mesh, int t, Random random)
        {
            double a = random.NextDouble(), b = random.NextDouble();
            if (a + b > 1)
            {
                a = 1 - a;
                b = 1 - b;
            }
            SdfVector p0 = P(mesh, mesh.indices[3 * t]), p1 = P(mesh, mesh.indices[3 * t + 1]), p2 = P(mesh, mesh.indices[3 * t + 2]);
            return p0 + a * (p1 - p0) + b * (p2 - p0);
        }

        // The largest distance from the surface of random points of the triangles
        private static double WorstDistance(DicedMesh mesh, Func<SdfVector, double> distance, int perTriangle, int seed)
        {
            Random random = new Random(seed);
            double worst = 0;
            for (int t = 0; t < mesh.TriangleCount; ++t)
            {
                for (int k = 0; k < perTriangle; ++k)
                {
                    worst = Math.Max(worst, Math.Abs(distance(RandomOnTriangle(mesh, t, random))));
                }
            }
            return worst;
        }

        [TEST("Dicing", "ZeroDisplacementConvergesToSphere")]
        public static void ZeroDisplacementConvergesToSphere()
        {
            IDicedSurface sphere = SurfaceOf("sphere", Floats("float", "radius", 1.5f));
            double previous = 0;
            for (int level = 0; level < 4; ++level)
            {
                double edge = 0.4 / (1 << level);
                DicedMesh mesh = Dicer.Dice(sphere, Settings(Constant(0), edge));
                double worst = WorstDistance(mesh, (SdfVector p) => p.Length() - 1.5, 4, 1);
                // the triangle with legs h (a half cell) has the circumradius h / sqrt(2): the sagitta there is h^2 / (4 r)
                EXPECT_LT(worst, 1.1 * edge * edge / (4 * 1.5) + 1e-6);
                if (level > 0)
                {
                    // halving the edge divides the error by four
                    EXPECT_LT(worst, 0.32 * previous);
                }
                Console.WriteLine("sphere edge " + edge + ": triangles " + mesh.TriangleCount + " worst distance " + worst);
                previous = worst;
            }
        }

        [TEST("Dicing", "ZeroDisplacementConvergesToTorus")]
        public static void ZeroDisplacementConvergesToTorus()
        {
            IDicedSurface torus = SurfaceOf("torus", Floats("float", "majorradius", 2), Floats("float", "minorradius", 0.5f));
            double previous = 0;
            for (int level = 0; level < 3; ++level)
            {
                double edge = 0.5 / (1 << level);
                DicedMesh mesh = Dicer.Dice(torus, Settings(Constant(0), edge));
                double worst = WorstDistance(mesh, (SdfVector p) =>
                {
                    double rho = Math.Sqrt(p.x * p.x + p.y * p.y);
                    return Math.Sqrt((rho - 2) * (rho - 2) + p.z * p.z) - 0.5;
                }, 4, 2);
                // the error of the polygon in the large circle is that of a segment of the circle of radius 2.5 too
                EXPECT_LT(worst, 2.0 * edge * edge / (8 * 0.5) + 1e-6);
                if (level > 0)
                {
                    EXPECT_LT(worst, 0.35 * previous);
                }
                Console.WriteLine("torus edge " + edge + ": triangles " + mesh.TriangleCount + " worst distance " + worst);
                previous = worst;
            }
        }

        [TEST("Dicing", "ConstantDisplacementOfSphereIsLargerSphere")]
        public static void ConstantDisplacementOfSphereIsLargerSphere()
        {
            IDicedSurface sphere = SurfaceOf("sphere", Floats("float", "radius", 2));
            foreach (bool reverse in new bool[] { false, true })
            {
                double d = 0.35;
                DicedMesh mesh = Dicer.Dice(sphere, Settings(Constant(d), 0.08, new Transform(), reverse));
                // the displacement follows the normal, which ReverseOrientation flips: the sphere shrinks
                double radius = reverse ? 2 - d : 2 + d;
                double worstRadius = 0, worstNormal = 0;
                for (int i = 0; i < mesh.p.Length; ++i)
                {
                    SdfVector p = P(mesh, i);
                    worstRadius = Math.Max(worstRadius, Math.Abs(p.Length() - radius));
                    SdfVector n = new SdfVector(mesh.n[i].x, mesh.n[i].y, mesh.n[i].z);
                    worstNormal = Math.Max(worstNormal, (n - p.Normalized()).Length());
                }
                EXPECT_LT(worstRadius, 2e-5);
                // the normals come from the faces: an angle of the order of the angle between two vertices
                EXPECT_LT(worstNormal, 0.03);
                double area = TotalArea(mesh), expected = 4 * Math.PI * radius * radius;
                EXPECT_LT(Math.Abs(area / expected - 1), 0.004);
                double volume = 0;
                for (int t = 0; t < mesh.TriangleCount; ++t)
                {
                    volume += SdfVector.Dot(P(mesh, mesh.indices[3 * t]), SdfVector.Cross(P(mesh, mesh.indices[3 * t + 1]), P(mesh, mesh.indices[3 * t + 2]))) / 6;
                }
                EXPECT_LT(Math.Abs(volume / (4.0 / 3 * Math.PI * radius * radius * radius) - 1), 0.006);
                Console.WriteLine("constant displacement reverse " + reverse + ": radius error " + worstRadius + " normal error " + worstNormal + " area ratio " +
                                  (area / expected) + " triangles " + mesh.TriangleCount);
            }
        }

        [TEST("Dicing", "ScaleAndOffsetOfTheTexture")]
        public static void ScaleAndOffsetOfTheTexture()
        {
            IDicedSurface sphere = SurfaceOf("sphere");
            DicingSettings settings = Settings(Constant(0.5), 0.2);
            settings.scale = 2;
            settings.offset = -0.25;
            DicedMesh mesh = Dicer.Dice(sphere, settings);
            // 2 * 0.5 - 0.25
            for (int i = 0; i < mesh.p.Length; ++i)
            {
                EXPECT_NEAR(P(mesh, i).Length(), 1.75, 1e-5);
            }
            EXPECT_NEAR(mesh.maxDisplacement, 0.75, 1e-6);
        }

        [TEST("Dicing", "RateIsMeasuredInRenderSpace")]
        public static void RateIsMeasuredInRenderSpace()
        {
            IDicedSurface sphere = SurfaceOf("sphere");
            Transform stretch = pbrt.Scale(6, 1, 1);
            DicedMesh uniform = Dicer.Dice(sphere, Settings(Constant(0), 0.3));
            DicedMesh stretched = Dicer.Dice(sphere, Settings(Constant(0), 0.3, stretch, false));
            EXPECT_GT(stretched.TriangleCount, 2 * uniform.TriangleCount);
            // no edge of the grid is much longer than the edge length in render space (the diagonals are up to 1.5 times the cell)
            double worst = 0;
            for (int t = 0; t < stretched.TriangleCount; ++t)
            {
                for (int k = 0; k < 3; ++k)
                {
                    SdfVector a = P(stretched, stretched.indices[3 * t + k]), b = P(stretched, stretched.indices[3 * t + (k + 1) % 3]);
                    SdfVector d = b - a;
                    worst = Math.Max(worst, new SdfVector(6 * d.x, d.y, d.z).Length());
                }
            }
            Console.WriteLine("stretched sphere: triangles " + stretched.TriangleCount + " vs " + uniform.TriangleCount + ", longest edge in render space " + worst);
            EXPECT_LT(worst, 3 * 0.3);
        }

        private static Primitive Aggregate(pstd.vector<Shape> shapes)
        {
            List<Primitive> primitives = new List<Primitive>();
            for (ulong s = 0; s < shapes.size(); ++s)
            {
                primitives.Add(new SimplePrimitive(shapes[s], null, 0));
            }
            return new BVHAggregate(primitives);
        }

        private static bool Hits(Primitive aggregate, Ray ray)
        {
            return aggregate.IntersectP(ray);
        }

        [TEST("Dicing", "ClosedShapesAreWatertight")]
        public static void ClosedShapesAreWatertight()
        {
            string[] names = { "sphere", "torus" };
            for (int shape = 0; shape < names.Length; ++shape)
            {
                IDicedSurface surface = shape == 0 ? SurfaceOf("sphere") : SurfaceOf("torus", Floats("float", "majorradius", 1.5f), Floats("float", "minorradius", 0.6f));
                DicingSettings settings = Settings(Bumps(0.12, 7), 0.07);
                DicedMesh mesh = Dicer.Dice(surface, settings);
                // every edge is shared by exactly two triangles
                int bad = 0;
                foreach (KeyValuePair<(Point3f, Point3f), int> e in EdgeCounts(mesh))
                {
                    if (e.Value != 2)
                    {
                        ++bad;
                    }
                }
                EXPECT_EQ(0, bad);
                // thousands of rays at points inside the surface never get through
                Primitive shapes = Aggregate(Dicer.CreateShapes(mesh, settings, new Allocator()));
                Random random = new Random(11 + shape);
                int leaks = 0, count = 20000;
                for (int i = 0; i < count; ++i)
                {
                    double x, y, z;
                    do
                    {
                        x = 2 * random.NextDouble() - 1;
                        y = 2 * random.NextDouble() - 1;
                        z = 2 * random.NextDouble() - 1;
                    } while (x * x + y * y + z * z > 0.49 || x * x + y * y + z * z < 0.01);
                    double dx, dy, dz;
                    do
                    {
                        dx = random.NextDouble() - 0.5;
                        dy = random.NextDouble() - 0.5;
                        dz = random.NextDouble() - 0.5;
                    } while (dx * dx + dy * dy + dz * dz < 1e-3);
                    // a ray from the point to the outside (the shape is star shaped about the origin only roughly, so the ray starts at the center of the tube)
                    double ox = x, oy = y, oz = z;
                    if (shape == 1)
                    {
                        double angle = Math.Atan2(y, x);
                        ox = 1.5 * Math.Cos(angle);
                        oy = 1.5 * Math.Sin(angle);
                        oz = 0;
                    }
                    Ray ray = new Ray(new Point3f((Float)ox, (Float)oy, (Float)oz), new Vector3f((Float)dx, (Float)dy, (Float)dz));
                    if (!Hits(shapes, ray))
                    {
                        ++leaks;
                    }
                }
                Console.WriteLine(names[shape] + ": " + mesh.TriangleCount + " triangles, " + count + " rays, " + leaks + " leaks, bad edges " + bad);
                EXPECT_EQ(0, leaks);
            }
        }

        [TEST("Dicing", "PartialSphereBoundaryIsTheDisplacedBoundary")]
        public static void PartialSphereBoundaryIsTheDisplacedBoundary()
        {
            // zmin = -0.5, zmax = 0.8, phimax = 200 degrees
            IDicedSurface surface = SurfaceOf("sphere", Floats("float", "zmin", -0.5f), Floats("float", "zmax", 0.8f), Floats("float", "phimax", 200));
            double d = 0.3;
            DicedMesh mesh = Dicer.Dice(surface, Settings(Constant(d), 0.09));
            double radius = 1 + d;
            double phiMax = 200 * Math.PI / 180;
            int boundary = 0, wrong = 0;
            foreach (KeyValuePair<(Point3f, Point3f), int> e in EdgeCounts(mesh))
            {
                if (e.Value == 1)
                {
                    ++boundary;
                    foreach (Point3f q in new Point3f[] { e.Key.Item1, e.Key.Item2 })
                    {
                        double phi = Math.Atan2(q.y, q.x);
                        if (phi < 0)
                        {
                            phi += 2 * Math.PI;
                        }
                        bool onZ = Math.Abs(q.z - radius * -0.5) < 1e-4 || Math.Abs(q.z - radius * 0.8) < 1e-4;
                        bool onPhi = Math.Abs(phi) < 1e-4 || Math.Abs(phi - phiMax) < 1e-4 || Math.Abs(phi - 2 * Math.PI) < 1e-4;
                        if (!onZ && !onPhi)
                        {
                            ++wrong;
                        }
                    }
                }
                else
                {
                    EXPECT_EQ(2, e.Value);
                }
            }
            Console.WriteLine("partial sphere: " + boundary + " boundary edges, " + wrong + " vertices off the boundary");
            EXPECT_GT(boundary, 0);
            EXPECT_EQ(0, wrong);
        }

        [TEST("Dicing", "PatchMeshHasNoCracks")]
        public static void PatchMeshHasNoCracks()
        {
            // 3 x 3 bicubic Bezier patches (10 x 10 control points) of a wavy sheet, displaced by a texture that varies from patch to patch
            List<float> points = new List<float>();
            for (int j = 0; j < 10; ++j)
            {
                for (int i = 0; i < 10; ++i)
                {
                    points.Add(i / 3.0f);
                    points.Add(j / 3.0f);
                    points.Add(0.35f * (float)(Math.Sin(i * 1.1) * Math.Cos(j * 0.9)));
                }
            }
            Transform identity = new Transform();
            Dictionary<string, FloatTexture> textures = new Dictionary<string, FloatTexture>();
            textures["bumps"] = Bumps(0.15, 9);
            ParameterDictionary parameters = Dictionary(Text("string", "type", "bicubic"), Ints("nu", 10), Ints("nv", 10), Floats("point3", "P", points.ToArray()),
                                                        Text("texture", "displacement", "bumps"), Floats("float", "edgelength", 0.045f));
            pstd.vector<Shape> exact = Shape.CreateExact("patchmesh", identity, identity, false, parameters, textures, null, new Allocator());
            EXPECT_EQ(9, (int)exact.size());
            IDicedSurface surface = DicedShapes.SurfaceOf(exact, identity);
            DicingSettings settings = Settings(textures["bumps"], 0.045);
            DicedMesh mesh = Dicer.Dice(surface, settings);
            int crack = 0, nonManifold = 0, boundary = 0;
            Dictionary<(Point3f, Point3f), int> counts = EdgeCounts(mesh);
            // find the uv of every position
            Dictionary<Point3f, Point2f> uvOf = new Dictionary<Point3f, Point2f>();
            for (int i = 0; i < mesh.p.Length; ++i)
            {
                uvOf[mesh.p[i]] = mesh.uv[i];
            }
            foreach (KeyValuePair<(Point3f, Point3f), int> e in counts)
            {
                if (e.Value > 2)
                {
                    ++nonManifold;
                }
                if (e.Value == 1)
                {
                    ++boundary;
                    Point2f a = uvOf[e.Key.Item1], b = uvOf[e.Key.Item2];
                    bool sameSide = (a.x < 1e-5f && b.x < 1e-5f) || (a.x > 1 - 1e-5f && b.x > 1 - 1e-5f) || (a.y < 1e-5f && b.y < 1e-5f) || (a.y > 1 - 1e-5f && b.y > 1 - 1e-5f);
                    if (!sameSide)
                    {
                        ++crack;
                    }
                }
            }
            // rays from above, one per random (x, y): the sheet is a height field, nothing falls through
            Primitive shapes = Aggregate(Dicer.CreateShapes(mesh, settings, new Allocator()));
            Random random = new Random(5);
            int leaks = 0, count = 20000;
            for (int i = 0; i < count; ++i)
            {
                Ray ray = new Ray(new Point3f((Float)(0.5 + 2.0 * random.NextDouble()), (Float)(0.5 + 2.0 * random.NextDouble()), 20), new Vector3f(0, 0, -1));
                if (!Hits(shapes, ray))
                {
                    ++leaks;
                }
            }
            Console.WriteLine("patch mesh: " + mesh.TriangleCount + " triangles, boundary edges " + boundary + ", cracks " + crack + ", non manifold " + nonManifold +
                              ", leaks " + leaks + " of " + count);
            EXPECT_EQ(0, crack);
            EXPECT_EQ(0, nonManifold);
            EXPECT_EQ(0, leaks);
        }

        [TEST("Dicing", "SamplingOfADisplacedAreaLightIsUnbiased")]
        public static void SamplingOfADisplacedAreaLightIsUnbiased()
        {
            IDicedSurface surface = SurfaceOf("sphere");
            DicingSettings settings = Settings(Bumps(0.15, 5), 0.12);
            DicedMesh mesh = Dicer.Dice(surface, settings);
            pstd.vector<Shape> shapes = Dicer.CreateShapes(mesh, settings, new Allocator());
            Primitive aggregate = Aggregate(shapes);
            // the area of the triangle shapes is the area of the mesh
            double area = 0;
            for (ulong s = 0; s < shapes.size(); ++s)
            {
                area += shapes[s].Area();
            }
            EXPECT_LT(Math.Abs(area / TotalArea(mesh) - 1), 1e-4);

            // the integral over the directions of the number of crossings of the ray: by ray casting and by sampling the triangles
            Point3f reference = new Point3f(3.2f, 0.4f, -0.7f);
            double distance = Math.Sqrt(3.2 * 3.2 + 0.4 * 0.4 + 0.7 * 0.7);
            double radius = 1.2;   // bounds the displaced sphere
            double cosCone = Math.Sqrt(1 - radius * radius / (distance * distance));
            double coneSolidAngle = 2 * Math.PI * (1 - cosCone);
            Vector3f axis = new Vector3f((Float)(-3.2 / distance), (Float)(-0.4 / distance), (Float)(0.7 / distance));
            Vector3f t1, t2;
            pbrt.CoordinateSystem(axis, &t1, &t2);
            Random random = new Random(3);
            int rays = 400000;
            double sum = 0, sum2 = 0;
            for (int i = 0; i < rays; ++i)
            {
                double u0 = random.NextDouble(), u1 = random.NextDouble();
                double cosTheta = 1 - u0 * (1 - cosCone), sinTheta = Math.Sqrt(Math.Max(0, 1 - cosTheta * cosTheta)), phi = 2 * Math.PI * u1;
                Vector3f dir = (Float)(sinTheta * Math.Cos(phi)) * t1 + (Float)(sinTheta * Math.Sin(phi)) * t2 + (Float)cosTheta * axis;
                // all the crossings along the ray
                int crossings = 0;
                Point3f origin = reference;
                for (int guard = 0; guard < 16; ++guard)
                {
                    Ray ray = new Ray(origin, dir);
                    pstd.optional<ShapeIntersection> si = aggregate.Intersect(ray);
                    if (!si)
                    {
                        break;
                    }
                    ++crossings;
                    origin = si.value().intr.SpawnRay(dir).o;
                }
                sum += crossings;
                sum2 += (double)crossings * crossings;
            }
            double mean = sum / rays, variance = sum2 / rays - mean * mean;
            double castEstimate = mean * coneSolidAngle, castSigma = Math.Sqrt(variance / rays) * coneSolidAngle;

            // sum over the triangles of the mean of 1 / pdf (the solid angle of the triangle)
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(reference), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
            int perTriangle = 48;
            double sampled = 0, sampledVariance = 0;
            for (ulong s = 0; s < shapes.size(); ++s)
            {
                double sumTriangle = 0, sumTriangle2 = 0;
                for (int k = 0; k < perTriangle; ++k)
                {
                    Point2f u = new Point2f((Float)((k + random.NextDouble()) / perTriangle), (Float)random.NextDouble());
                    pstd.optional<ShapeSample> ss = shapes[s].Sample(ctx, u);
                    double w = ss && ss.value().pdf > 0 ? 1.0 / ss.value().pdf : 0;
                    sumTriangle += w;
                    sumTriangle2 += w * w;
                }
                double m = sumTriangle / perTriangle;
                sampled += m;
                sampledVariance += Math.Max(0, sumTriangle2 / perTriangle - m * m) / perTriangle;
            }
            double sigma = Math.Sqrt(castSigma * castSigma + sampledVariance);
            double z = (castEstimate - sampled) / sigma;
            Console.WriteLine("area light: ray cast " + castEstimate + " +- " + castSigma + ", triangle sampling " + sampled + " +- " + Math.Sqrt(sampledVariance) + ", z = " + z);
            EXPECT_LT(Math.Abs(z), 4);
        }
        private static double ExactArea(pstd.vector<Shape> exact)
        {
            double sum = 0;
            for (ulong i = 0; i < exact.size(); ++i)
            {
                sum += exact[i].Area();
            }
            return sum;
        }

        private static List<(string, ParsedParameter[])> ShapeList()
        {
            List<(string, ParsedParameter[])> list = new List<(string, ParsedParameter[])>();
            list.Add(("sphere", new ParsedParameter[] { Floats("float", "zmin", -0.6f), Floats("float", "zmax", 0.9f), Floats("float", "phimax", 270) }));
            list.Add(("cylinder", new ParsedParameter[] { Floats("float", "zmin", -1), Floats("float", "zmax", 1), Floats("float", "phimax", 300) }));
            list.Add(("disk", new ParsedParameter[] { Floats("float", "innerradius", 0.3f), Floats("float", "phimax", 330), Floats("float", "height", 0.2f) }));
            list.Add(("cone", new ParsedParameter[] { Floats("float", "height", 1.5f), Floats("float", "zmax", 1.0f), Floats("float", "phimax", 300) }));
            list.Add(("paraboloid", new ParsedParameter[] { Floats("float", "zmin", 0.1f), Floats("float", "phimax", 300) }));
            list.Add(("hyperboloid", new ParsedParameter[] { Floats("point3", "p1", 1, 0, -0.5f), Floats("point3", "p2", 0.6f, 0.8f, 0.5f), Floats("float", "thetamax", 300) }));
            list.Add(("torus", new ParsedParameter[] { Floats("float", "majorradius", 1.5f), Floats("float", "minorradius", 0.4f), Floats("float", "phimin", 30), Floats("float", "phimax", 280), Floats("float", "thetamax", 250) }));
            list.Add(("teapot", new ParsedParameter[] { }));
            List<float> grid = new List<float>();
            for (int j = 0; j < 4; ++j)
            {
                for (int i = 0; i < 4; ++i)
                {
                    grid.Add(i - 1.5f);
                    grid.Add(j - 1.5f);
                    grid.Add(0.4f * (float)Math.Sin(i + 2 * j));
                }
            }
            list.Add(("patch", new ParsedParameter[] { Text("string", "type", "bicubic"), Floats("point3", "P", grid.ToArray()) }));
            list.Add(("patch", new ParsedParameter[] { Text("string", "type", "bilinear"), Floats("point3", "P", 0, 0, 0, 1, 0, 0.3f, 0, 1, 0.2f, 1, 1, -0.4f) }));
            List<float> nurbs = new List<float>();
            nurbs.AddRange(grid);
            list.Add(("nupatch", new ParsedParameter[] { Ints("nu", 4), Ints("nv", 4), Ints("uorder", 4), Ints("vorder", 4), Floats("float", "uknots", 0, 0, 0, 0, 1, 1, 1, 1),
                                                         Floats("float", "vknots", 0, 0, 0, 0, 1, 1, 1, 1), Floats("point3", "P", nurbs.ToArray()) }));
            list.Add(("catmarksubdiv", new ParsedParameter[] { Ints("levels", 2), Ints("faceVertexCounts", 4, 4, 4, 4, 4, 4),
                Ints("indices", 0, 1, 3, 2, 4, 6, 7, 5, 0, 2, 6, 4, 1, 5, 7, 3, 0, 4, 5, 1, 2, 3, 7, 6),
                Floats("point3", "P", -1, -1, -1, 1, -1, -1, -1, 1, -1, 1, 1, -1, -1, -1, 1, 1, -1, 1, -1, 1, 1, 1, 1, 1) }));
            return list;
        }

        [TEST("Dicing", "EveryShapeDicesToItsExactSurface")]
        public static void EveryShapeDicesToItsExactSurface()
        {
            Transform identity = new Transform();
            foreach ((string name, ParsedParameter[] parameters) in ShapeList())
            {
                pstd.vector<Shape> exact = Shape.CreateExact(name, identity, identity, false, Dictionary(parameters), new Dictionary<string, FloatTexture>(), null, new Allocator());
                IDicedSurface surface = DicedShapes.SurfaceOf(exact, identity);
                EXPECT_TRUE(surface != null);
                if (surface == null)
                {
                    continue;
                }
                DicedMesh mesh = Dicer.Dice(surface, Settings(Constant(0), name == "teapot" ? 0.08 : 0.05));
                double area = TotalArea(mesh), expected = ExactArea(exact);
                // the orientation of the triangles: the normal of the exact surface under the triangle points the same way
                Random random = new Random(21);
                int tried = 0, hit = 0, agree = 0;
                for (int k = 0; k < 400 && mesh.TriangleCount > 0; ++k)
                {
                    int t = random.Next(mesh.TriangleCount);
                    SdfVector q = RandomOnTriangle(mesh, t, random);
                    SdfVector fn = SdfVector.Cross(P(mesh, mesh.indices[3 * t + 1]) - P(mesh, mesh.indices[3 * t]), P(mesh, mesh.indices[3 * t + 2]) - P(mesh, mesh.indices[3 * t])).Normalized();
                    SdfVector o = q + 0.02 * fn;
                    Ray ray = new Ray(new Point3f((Float)o.x, (Float)o.y, (Float)o.z), new Vector3f((Float)(-fn.x), (Float)(-fn.y), (Float)(-fn.z)));
                    ++tried;
                    for (ulong s = 0; s < exact.size(); ++s)
                    {
                        pstd.optional<ShapeIntersection> si = exact[s].Intersect(ray, 0.04f);
                        if (si)
                        {
                            ++hit;
                            Normal3f n = si.value().intr.n;
                            if (n.x * fn.x + n.y * fn.y + n.z * fn.z > 0.5)
                            {
                                ++agree;
                            }
                            break;
                        }
                    }
                }
                Console.WriteLine(name + ": " + mesh.TriangleCount + " triangles, area " + area + " vs exact " + expected + " (" + (area / expected) + "), orientation " + agree + "/" + hit + " of " + tried);
                EXPECT_LT(Math.Abs(area / expected - 1), 0.02);
                EXPECT_GT(hit, 0.85 * tried);
                EXPECT_GT(agree, 0.97 * hit);
            }
        }

        [TEST("Dicing", "ShapesWithoutDisplacementAreUnchanged")]
        public static void ShapesWithoutDisplacementAreUnchanged()
        {
            Transform identity = new Transform();
            Dictionary<string, FloatTexture> textures = new Dictionary<string, FloatTexture>();
            textures["t"] = Constant(0.1);
            foreach ((string name, ParsedParameter[] parameters) in ShapeList())
            {
                pstd.vector<Shape> viaCreate = Shape.Create(name, identity, identity, false, Dictionary(parameters), textures, null, new Allocator());
                pstd.vector<Shape> exact = Shape.CreateExact(name, identity, identity, false, Dictionary(parameters), textures, null, new Allocator());
                EXPECT_EQ(exact.size(), viaCreate.size());
                for (ulong i = 0; i < exact.size() && i < viaCreate.size(); ++i)
                {
                    // the same class, and for the quadrics the same description down to the last parameter
                    EXPECT_TRUE(viaCreate[i].GetType() == exact[i].GetType());
                    EXPECT_TRUE(!(viaCreate[i] is Triangle));
                    if (exact[i] is QuadricBase)
                    {
                        EXPECT_EQ(exact[i].ToString(), viaCreate[i].ToString());
                    }
                }
                // with a displacement the shape is a triangle mesh
                List<ParsedParameter> displaced = new List<ParsedParameter>(parameters);
                displaced.Add(Text("texture", "displacement", "t"));
                displaced.Add(Floats("float", "edgelength", 0.2f));
                pstd.vector<Shape> diced = Shape.Create(name, identity, identity, false, Dictionary(displaced.ToArray()), textures, null, new Allocator());
                EXPECT_GT(diced.size(), 0);
                EXPECT_TRUE(diced[0] is Triangle);
            }
        }

        [TEST("Dicing", "SubdivisionCubeHasNoCracks")]
        public static void SubdivisionCubeHasNoCracks()
        {
            Transform identity = new Transform();
            Dictionary<string, FloatTexture> textures = new Dictionary<string, FloatTexture>();
            textures["t"] = Bumps(0.08, 6);
            ParsedParameter[] parameters = null;
            foreach ((string name, ParsedParameter[] p) in ShapeList())
            {
                if (name == "catmarksubdiv")
                {
                    parameters = p;
                }
            }
            pstd.vector<Shape> exact = Shape.CreateExact("catmarksubdiv", identity, identity, false, Dictionary(parameters), textures, null, new Allocator());
            DicingSettings settings = Settings(textures["t"], 0.05);
            DicedMesh mesh = Dicer.Dice(DicedShapes.SurfaceOf(exact, identity), settings);
            int bad = 0;
            foreach (KeyValuePair<(Point3f, Point3f), int> e in EdgeCounts(mesh))
            {
                if (e.Value != 2)
                {
                    ++bad;
                }
            }
            Primitive aggregate = Aggregate(Dicer.CreateShapes(mesh, settings, new Allocator()));
            Random random = new Random(8);
            int leaks = 0;
            for (int i = 0; i < 10000; ++i)
            {
                Vector3f d = new Vector3f((Float)(random.NextDouble() - 0.5), (Float)(random.NextDouble() - 0.5), (Float)(random.NextDouble() - 0.5));
                if (!Hits(aggregate, new Ray(new Point3f(0, 0, 0), d)))
                {
                    ++leaks;
                }
            }
            Console.WriteLine("subdivision cube: " + mesh.TriangleCount + " triangles, bad edges " + bad + ", leaks " + leaks);
            EXPECT_EQ(0, bad);
            EXPECT_EQ(0, leaks);
        }

        private static ParsedParameter[] TrimmedPlane(float centerU, float centerV, float radius)
        {
            float w = (float)Math.Sqrt(0.5);
            float[] cu = { 1, 1, 0, -1, -1, -1, 0, 1, 1 }, cv = { 0, 1, 1, 1, 0, -1, -1, -1, 0 };
            float[] weights = { 1, w, 1, w, 1, w, 1, w, 1 };
            List<float> u = new List<float>(), v = new List<float>(), ww = new List<float>();
            for (int i = 0; i < 9; ++i)
            {
                u.Add((centerU + radius * cu[i]) * weights[i]);
                v.Add((centerV + radius * cv[i]) * weights[i]);
                ww.Add(weights[i]);
            }
            return new ParsedParameter[]
            {
                Ints("nu", 2), Ints("nv", 2), Ints("uorder", 2), Ints("vorder", 2), Floats("float", "uknots", 0, 0, 1, 1), Floats("float", "vknots", 0, 0, 1, 1),
                Floats("point3", "P", 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0),
                Ints("trimncurves", 1), Ints("trimorder", 3), Ints("trimn", 9), Floats("float", "trimknots", 0, 0, 0, 0.25f, 0.25f, 0.5f, 0.5f, 0.75f, 0.75f, 1, 1, 1),
                Floats("float", "trimmin", 0), Floats("float", "trimmax", 1), Floats("float", "trimu", u.ToArray()), Floats("float", "trimv", v.ToArray()), Floats("float", "trimw", ww.ToArray())
            };
        }

        [TEST("Dicing", "TrimmedNurbsBoundaryFollowsTheTrimCurve")]
        public static void TrimmedNurbsBoundaryFollowsTheTrimCurve()
        {
            double radius = 0.3, cu = 0.45, cv = 0.55;
            Transform identity = new Transform();
            pstd.vector<Shape> exact = Shape.CreateExact("nupatch", identity, identity, false, Dictionary(TrimmedPlane((float)cu, (float)cv, (float)radius)),
                                                          new Dictionary<string, FloatTexture>(), null, new Allocator());
            IDicedSurface surface = DicedShapes.SurfaceOf(exact, identity);
            double previousChord = 0;
            for (int level = 0; level < 3; ++level)
            {
                double edge = 0.1 / (1 << level);
                DicedMesh mesh = Dicer.Dice(surface, Settings(Constant(0), edge));
                double area = TotalArea(mesh), expected = Math.PI * radius * radius;
                // the boundary vertices (edges used once) lie on the trim circle; the chords are inside it
                double worstVertex = 0;
                int boundaryEdges = 0;
                double worstChord = 0;
                foreach (KeyValuePair<(Point3f, Point3f), int> e in EdgeCounts(mesh))
                {
                    if (e.Value != 1)
                    {
                        continue;
                    }
                    ++boundaryEdges;
                    foreach (Point3f q in new Point3f[] { e.Key.Item1, e.Key.Item2 })
                    {
                        worstVertex = Math.Max(worstVertex, Math.Abs(Math.Sqrt((q.x - cu) * (q.x - cu) + (q.y - cv) * (q.y - cv)) - radius));
                    }
                    Point3f a = e.Key.Item1, b = e.Key.Item2;
                    double mx = 0.5 * (a.x + b.x), my = 0.5 * (a.y + b.y);
                    worstChord = Math.Max(worstChord, radius - Math.Sqrt((mx - cu) * (mx - cu) + (my - cv) * (my - cv)));
                }
                Console.WriteLine("trimmed disc edge " + edge + ": area ratio " + (area / expected) + ", boundary edges " + boundaryEdges + ", boundary vertex off the circle " +
                                  worstVertex + ", chord depth " + worstChord);
                // float positions: 1e-6; the area of the inscribed polygon is below the disc by about h^2 / 12 R per unit
                EXPECT_LT(worstVertex, 3e-6);
                EXPECT_LT(Math.Abs(area / expected - 1), 0.02 / (1 << (2 * level)) + 1e-4);
                EXPECT_GT(boundaryEdges, 8);
                if (level > 0)
                {
                    EXPECT_LT(worstChord, 0.35 * previousChord);
                }
                previousChord = worstChord;
            }
        }
    }
}  // namespace pbrt
