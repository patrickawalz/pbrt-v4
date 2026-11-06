// (not in pbrt-v4) Tests of motion blur: see docs/motion-blur.md.
//
// Part 1 (group MotionBlur.Transform*): the transform blur of every shape type. A scene with the shape under an animated transformation
// (MotionBegin ... MotionEnd) is built next to a scene with the shape under the static transformation Interpolate(t); a ray at time t must
// give the same hit (hit or miss, t, p, n, uv, dpdu) in the two, and every hit must be inside of the bounds of the BVH.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using testing;

namespace pbrt
{
    public static unsafe class MotionBlurTests
    {
        // ------------------------------------------------------------------------------------------
        // Helpers

        private const string WorldPrefix =
            "Option \"rendercoordsys\" \"world\"\n" +
            "Film \"rgb\" \"integer xresolution\" 8 \"integer yresolution\" 8\n" +
            "Sampler \"independent\"\nCamera \"perspective\"\nWorldBegin\n";

        internal sealed class BuiltScene
        {
            public List<Light> lights;
            public Primitive accel;
            public BasicScene scene;
        }

        internal static BuiltScene BuildScene(string text)
        {
            BuiltScene built = new BuiltScene();
            RenderingCoordinateSystem saved = Options.renderingSpace;
            try
            {
                BasicScene scene = new BasicScene();
                BasicSceneBuilder builder = new BasicSceneBuilder(scene);
                ParseString(builder, text);
                built.scene = scene;
                SortedDictionary<string, Medium> media = scene.CreateMedia();
                NamedTextures textures = scene.CreateTextures();
                Dictionary<int, List<Light>> shapeIndexToAreaLights = new Dictionary<int, List<Light>>();
                built.lights = scene.CreateLights(textures, shapeIndexToAreaLights);
                SortedDictionary<string, Material> namedMaterials = new SortedDictionary<string, Material>(StringComparer.Ordinal);
                List<Material> materials = new List<Material>();
                scene.CreateMaterials(textures, namedMaterials, materials);
                built.accel = scene.CreateAggregate(textures, shapeIndexToAreaLights, media, namedMaterials, materials);
            }
            finally
            {
                Options.renderingSpace = saved;
            }
            return built;
        }

        internal static string F(Float v)
        {
            return ((float)v).ToString("R", CultureInfo.InvariantCulture);
        }

        // "ConcatTransform [ ... ]" with the matrix of the transform (the file lists the columns)
        internal static string MatrixText(Transform t, string directive = "ConcatTransform")
        {
            Vector3f cx = t.Apply(new Vector3f(1, 0, 0)), cy = t.Apply(new Vector3f(0, 1, 0)), cz = t.Apply(new Vector3f(0, 0, 1));
            Point3f o = t.Apply(new Point3f(0, 0, 0));
            StringBuilder sb = new StringBuilder();
            sb.Append(directive).Append(" [ ");
            sb.Append(F(cx.x)).Append(' ').Append(F(cx.y)).Append(' ').Append(F(cx.z)).Append(" 0 ");
            sb.Append(F(cy.x)).Append(' ').Append(F(cy.y)).Append(' ').Append(F(cy.z)).Append(" 0 ");
            sb.Append(F(cz.x)).Append(' ').Append(F(cz.y)).Append(' ').Append(F(cz.z)).Append(" 0 ");
            sb.Append(F(o.x)).Append(' ').Append(F(o.y)).Append(' ').Append(F(o.z)).Append(" 1");
            sb.Append(" ]\n");
            return sb.ToString();
        }

        internal static string MotionBlock(AnimatedTransform a)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("MotionBegin [");
            for (int i = 0; i < a.KeyCount; ++i)
            {
                sb.Append(' ').Append(F(a.GetKeyTime(i)));
            }
            sb.Append(" ]\n");
            for (int i = 0; i < a.KeyCount; ++i)
            {
                sb.Append(MatrixText(a.GetKeyTransform(i)));
            }
            sb.Append("MotionEnd\n");
            return sb.ToString();
        }

        internal static Transform Pose(Float x, Float y, Float z, Float degrees, Float ax, Float ay, Float az)
        {
            return Translate(new Vector3f(x, y, z)) * Rotate(degrees, Normalize(new Vector3f(ax, ay, az)));
        }

        // Two keys: a translation and a rotation of 75 degrees
        internal static AnimatedTransform TwoKeys()
        {
            return new AnimatedTransform(Pose(-0.6f, 0.1f, 0, 0, 0, 1, 0), 0, Pose(0.5f, 0.3f, 0.2f, 75, 1, 1, 0), 1);
        }

        // Four keys, 200 degrees of rotation in all, the path turns
        internal static AnimatedTransform FourKeys()
        {
            return new AnimatedTransform(new Transform[] { Pose(-0.5f, 0, 0, 0, 0.3f, 1, 0.2f), Pose(-0.2f, 0.4f, 0.2f, 60, 0.3f, 1, 0.2f),
                                                           Pose(0.3f, 0.2f, -0.3f, 130, 0.3f, 1, 0.2f), Pose(0.6f, -0.3f, 0.2f, 200, 0.3f, 1, 0.2f) },
                                         new Float[] { 0, 0.25f, 0.6f, 1 });
        }

        internal static AnimatedTransform Keys(int count)
        {
            return count == 2 ? TwoKeys() : FourKeys();
        }

        // ------------------------------------------------------------------------------------------
        // The shapes: a snippet of scene file each, around the origin, about 1 unit in size

        internal sealed class ShapeCase
        {
            public string name;
            public string prelude = "";
            public string shape;
            // One string is the Shape directive, two are a directive that goes before the attribute block (a texture) and the shape
            public ShapeCase(string name, string first, string second = null)
            {
                this.name = name;
                if (second == null)
                {
                    shape = first;
                }
                else
                {
                    prelude = first;
                    shape = second;
                }
            }
        }

        private const string Cube = "\"integer indices\" [ 0 2 1  0 3 2  4 5 6  4 6 7  0 1 5  0 5 4  3 7 6  3 6 2  0 4 7  0 7 3  1 2 6  1 6 5 ] " +
                                    "\"point3 P\" [ -0.6 -0.6 -0.6  0.6 -0.6 -0.6  0.6 0.6 -0.6  -0.6 0.6 -0.6  -0.6 -0.6 0.6  0.6 -0.6 0.6  0.6 0.6 0.6  -0.6 0.6 0.6 ] ";
        private const string CubeFaces = "\"integer faceVertexCounts\" [ 4 4 4 4 4 4 ] \"integer indices\" [ 0 3 2 1  4 5 6 7  0 1 5 4  3 7 6 2  0 4 7 3  1 2 6 5 ] " +
                                         "\"point3 P\" [ -0.6 -0.6 -0.6  0.6 -0.6 -0.6  0.6 0.6 -0.6  -0.6 0.6 -0.6  -0.6 -0.6 0.6  0.6 -0.6 0.6  0.6 0.6 0.6  -0.6 0.6 0.6 ] ";

        private static string plyPath;

        private static string PlyFile()
        {
            if (plyPath == null)
            {
                string path = Path.Combine(Path.GetTempPath(), "motionblur_test_tetra.ply");
                File.WriteAllText(path,
                    "ply\nformat ascii 1.0\nelement vertex 4\nproperty float x\nproperty float y\nproperty float z\nelement face 4\nproperty list uchar int vertex_indices\nend_header\n" +
                    "0.7 0.7 0.7\n0.7 -0.7 -0.7\n-0.7 0.7 -0.7\n-0.7 -0.7 0.7\n3 0 1 2\n3 0 3 1\n3 0 2 3\n3 1 3 2\n");
                plyPath = path;
            }
            return plyPath;
        }

        private static bool proceduralRegistered;

        private static void RegisterProcedural()
        {
            if (proceduralRegistered)
            {
                return;
            }
            proceduralRegistered = true;
            Procedurals.Register("motionblur_test", (Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                     FileLoc loc, Allocator alloc) =>
            {
                pstd.vector<Shape> shapes = new pstd.vector<Shape>();
                shapes.push_back(new Sphere(renderFromObject * Translate(new Vector3f(0.3f, 0, 0)), Inverse(renderFromObject * Translate(new Vector3f(0.3f, 0, 0))),
                                            reverseOrientation, 0.5f, -0.5f, 0.5f, 360));
                TriangleMesh mesh = new TriangleMesh(renderFromObject, reverseOrientation, new pstd.vector<int>(new int[] { 0, 1, 2 }),
                                                     new pstd.vector<Point3f>(new Point3f[] { new Point3f(-0.9f, -0.5f, 0.2f), new Point3f(-0.2f, -0.5f, 0.2f),
                                                                                              new Point3f(-0.5f, 0.6f, 0.2f) }),
                                                     new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(), new pstd.vector<int>(), alloc);
                pstd.vector<Shape> triangles = Triangle.CreateTriangles(mesh, alloc);
                shapes.push_back(triangles[0]);
                return shapes;
            });
        }

        internal static List<ShapeCase> ShapeCases()
        {
            RegisterProcedural();
            List<ShapeCase> cases = new List<ShapeCase>();
            cases.Add(new ShapeCase("sphere", "Shape \"sphere\" \"float radius\" 0.8 \"float zmin\" -0.6 \"float zmax\" 0.7 \"float phimax\" 300\n"));
            cases.Add(new ShapeCase("cylinder", "Shape \"cylinder\" \"float radius\" 0.6 \"float zmin\" -0.7 \"float zmax\" 0.7\n"));
            cases.Add(new ShapeCase("disk", "Shape \"disk\" \"float radius\" 0.9 \"float innerradius\" 0.2 \"float height\" 0.1\n"));
            cases.Add(new ShapeCase("cone", "Shape \"cone\" \"float radius\" 0.7 \"float height\" 1.2\n"));
            cases.Add(new ShapeCase("paraboloid", "Shape \"paraboloid\" \"float radius\" 0.7 \"float zmin\" -0.2 \"float zmax\" 0.9\n"));
            cases.Add(new ShapeCase("hyperboloid", "Shape \"hyperboloid\" \"point3 p1\" [ 0.4 0 -0.6 ] \"point3 p2\" [ 0.5 0.3 0.6 ]\n"));
            cases.Add(new ShapeCase("torus", "Shape \"torus\" \"float majorradius\" 0.6 \"float minorradius\" 0.25\n"));
            cases.Add(new ShapeCase("sdf", "Shape \"sdf\" \"string sdf\" \"smoothunion(sphere(0.6), translate(box(0.3,0.3,0.3), 0.5,0,0), 0.2)\" " +
                                           "\"point3 pmin\" [ -1.2 -1.2 -1.2 ] \"point3 pmax\" [ 1.2 1.2 1.2 ]\n"));
            cases.Add(new ShapeCase("trianglemesh", "Shape \"trianglemesh\" " + Cube + "\"normal N\" [ -1 -1 -1  1 -1 -1  1 1 -1  -1 1 -1  -1 -1 1  1 -1 1  1 1 1  -1 1 1 ] " +
                                                    "\"point2 uv\" [ 0.35 0.62  1.15 0.58  1.17 1.38  0.37 1.42  0.83 0.38  1.63 0.34  1.65 1.14  0.85 1.18 ]\n"));
            cases.Add(new ShapeCase("plymesh", "Shape \"plymesh\" \"string filename\" \"" + PlyFile() + "\"\n"));
            cases.Add(new ShapeCase("loopsubdiv", "Shape \"loopsubdiv\" \"integer levels\" 2 " + Cube + "\n"));
            cases.Add(new ShapeCase("catmarksubdiv", "Shape \"catmarksubdiv\" " + CubeFaces + "\n"));
            cases.Add(new ShapeCase("subdivisionmesh", "Shape \"subdivisionmesh\" \"integer nvertices\" [ 4 4 4 4 4 4 ] \"integer vertices\" [ 0 3 2 1  4 5 6 7  0 1 5 4  3 7 6 2  0 4 7 3  1 2 6 5 ] " +
                                                       "\"point3 P\" [ -0.6 -0.6 -0.6  0.6 -0.6 -0.6  0.6 0.6 -0.6  -0.6 0.6 -0.6  -0.6 -0.6 0.6  0.6 -0.6 0.6  0.6 0.6 0.6  -0.6 0.6 0.6 ]\n"));
            cases.Add(new ShapeCase("curvedpatches", "Shape \"curvedpatches\" \"integer faceVertexCounts\" [ 3 3 3 3 3 3 3 3 3 3 3 3 ] " + Cube + "\n"));
            cases.Add(new ShapeCase("ribbon", "Shape \"ribbon\" \"point3 P\" [ -0.8 0 0  -0.3 0.4 0  0.3 -0.4 0  0.8 0 0 ] \"float width\" 0.2\n"));
            cases.Add(new ShapeCase("rocap", "Shape \"rocap\" \"point3 P\" [ -0.8 0 0  -0.3 0.4 0  0.3 -0.4 0  0.8 0 0 ] \"float radius\" 0.15\n"));
            cases.Add(new ShapeCase("displacedmesh", "Texture \"bumps\" \"float\" \"constant\" \"float value\" 0.1\n",
                                    "Shape \"trianglemesh\" \"integer indices\" [ 0 1 2 0 2 3 ] \"point3 P\" [ -0.8 -0.8 0  0.8 -0.8 0  0.8 0.8 0  -0.8 0.8 0 ] " +
                                    "\"point2 uv\" [ 0 0 1 0 1 1 0 1 ] \"normal N\" [ 0 0 1 0 0 1 0 0 1 0 0 1 ] \"texture displacement\" \"bumps\" \"string displacementmode\" \"shell\"\n"));
            cases.Add(new ShapeCase("trianglemesh_tessellated", "Texture \"bumps2\" \"float\" \"constant\" \"float value\" 0.05\n",
                                    "Shape \"trianglemesh\" \"integer indices\" [ 0 1 2 0 2 3 ] \"point3 P\" [ -0.8 -0.8 0  0.8 -0.8 0  0.8 0.8 0  -0.8 0.8 0 ] " +
                                    "\"point2 uv\" [ 0 0 1 0 1 1 0 1 ] \"normal N\" [ 0 0 1 0 0 1 0 0 1 0 0 1 ] \"texture displacement\" \"bumps2\" \"float edgelength\" 0.4\n"));
            cases.Add(new ShapeCase("sphere_diced", "Texture \"bumps3\" \"float\" \"constant\" \"float value\" 0.05\n",
                                    "Shape \"sphere\" \"float radius\" 0.8 \"texture displacement\" \"bumps3\"\n"));
            cases.Add(new ShapeCase("polygon", "Shape \"polygon\" \"point3 P\" [ -0.7 -0.5 0  0 -0.8 0  0.7 -0.5 0  0.5 0.6 0  -0.5 0.6 0 ]\n"));
            cases.Add(new ShapeCase("generalpolygon", "Shape \"generalpolygon\" \"integer nvertices\" [ 4 3 ] " +
                                                      "\"point3 P\" [ -0.8 -0.8 0  0.8 -0.8 0  0.8 0.8 0  -0.8 0.8 0  -0.3 -0.3 0  0.3 -0.3 0  0 0.3 0 ]\n"));
            cases.Add(new ShapeCase("pointspolygons", "Shape \"pointspolygons\" \"integer nvertices\" [ 3 3 ] \"integer vertices\" [ 0 1 2  0 2 3 ] " +
                                                      "\"point3 P\" [ -0.8 -0.8 0  0.8 -0.8 0.1  0.8 0.8 0  -0.8 0.8 0.1 ]\n"));
            cases.Add(new ShapeCase("pointsgeneralpolygons", "Shape \"pointsgeneralpolygons\" \"integer nloops\" [ 2 ] \"integer nvertices\" [ 4 3 ] " +
                                                             "\"integer vertices\" [ 0 1 2 3  4 5 6 ] " +
                                                             "\"point3 P\" [ -0.8 -0.8 0  0.8 -0.8 0  0.8 0.8 0  -0.8 0.8 0  -0.3 -0.3 0  0.3 -0.3 0  0 0.3 0 ]\n"));
            cases.Add(new ShapeCase("points", "Shape \"points\" \"point3 P\" [ -0.5 0 0  0.3 0.4 0.1  0.2 -0.5 -0.2 ] \"float width\" [ 0.5 0.4 0.6 ]\n"));
            cases.Add(new ShapeCase("points_disk", "Shape \"points\" \"string type\" \"disk\" \"point3 P\" [ -0.5 0 0  0.3 0.4 0.1 ] \"normal N\" [ 0 0 1 0.3 0.3 0.9 ] \"float width\" [ 0.8 0.7 ]\n"));
            cases.Add(new ShapeCase("teapot", "Shape \"teapot\" \"float scale\" 0.3\n"));
            cases.Add(new ShapeCase("geometry", "Shape \"geometry\" \"string type\" \"teapot\" \"float scale\" 0.3\n"));
            cases.Add(new ShapeCase("volume", "Shape \"volume\" \"string type\" \"box\" \"point3 p0\" [ -0.6 -0.5 -0.4 ] \"point3 p1\" [ 0.6 0.5 0.4 ]\n"));
            cases.Add(new ShapeCase("curves", "Shape \"curves\" \"integer nvertices\" [ 4 ] \"point3 P\" [ -0.8 0 0  -0.3 0.5 0  0.3 -0.5 0  0.8 0 0 ] \"float width\" [ 0.2 0.1 ]\n"));
            cases.Add(new ShapeCase("curve", "Shape \"curve\" \"string type\" \"cylinder\" \"point3 P\" [ -0.8 0 0  -0.3 0.5 0  0.3 -0.5 0  0.8 0 0 ] \"float width\" 0.2\n"));
            cases.Add(new ShapeCase("blobby", "Shape \"blobby\" \"integer nleaf\" 1 \"integer code\" [ 1001 0 ] \"float flt\" [ 1 0 0 0  0 1 0 0  0 0 1 0  0 0 0 1 ]\n"));
            cases.Add(new ShapeCase("procedural", "Shape \"procedural\" \"string name\" \"motionblur_test\" \"float bound\" [ -1 1 -1 1 -1 1 ]\n"));
            cases.Add(new ShapeCase("nupatch", "Shape \"nupatch\" \"integer nu\" 4 \"integer nv\" 4 \"integer uorder\" 4 \"integer vorder\" 4 " +
                                               "\"float uknots\" [ 0 0 0 0 1 1 1 1 ] \"float vknots\" [ 0 0 0 0 1 1 1 1 ] " +
                                               "\"point3 P\" [ -0.8 -0.8 0 -0.8 -0.3 0.3 -0.8 0.3 0.3 -0.8 0.8 0  -0.3 -0.8 0.3 -0.3 -0.3 0.6 -0.3 0.3 0.6 -0.3 0.8 0.3 " +
                                               "0.3 -0.8 0.3 0.3 -0.3 0.6 0.3 0.3 0.6 0.3 0.8 0.3  0.8 -0.8 0 0.8 -0.3 0.3 0.8 0.3 0.3 0.8 0.8 0 ]\n"));
            cases.Add(new ShapeCase("patch", "Shape \"patch\" \"string type\" \"bilinear\" \"point3 P\" [ -0.8 -0.8 0  0.8 -0.8 0.2  -0.8 0.8 0.1  0.8 0.8 0 ]\n"));
            cases.Add(new ShapeCase("patchmesh", "Shape \"patchmesh\" \"string type\" \"bicubic\" \"integer nu\" 4 \"integer nv\" 4 \"point3 P\" [ -0.8 -0.8 0 -0.8 -0.3 0.3 -0.8 0.3 0.3 -0.8 0.8 0  " +
                                                 "-0.3 -0.8 0.3 -0.3 -0.3 0.6 -0.3 0.3 0.6 -0.3 0.8 0.3  0.3 -0.8 0.3 0.3 -0.3 0.6 0.3 0.3 0.6 0.3 0.8 0.3  0.8 -0.8 0 0.8 -0.3 0.3 0.8 0.3 0.3 0.8 0.8 0 ]\n"));
            cases.Add(new ShapeCase("bilinearmesh", "Shape \"bilinearmesh\" \"point3 P\" [ -0.8 -0.8 0  0.8 -0.8 0.2  -0.8 0.8 0.1  0.8 0.8 0 ] \"point2 uv\" [ 0 0 1 0 0 1 1 1 ]\n"));
            return cases;
        }

        // ------------------------------------------------------------------------------------------
        // Rays

        private static Float Eps = 1.1920929e-7f;

        // A ray at time t that goes through a random point of the box of the shape (in the frame of the shape at time t)
        private static Ray RandomRay(ref RNG rng, Transform shapeAtTime, Float time)
        {
            Point3f aim = shapeAtTime.Apply(new Point3f(-1.2f + 2.4f * rng.Uniform<Float>(), -1.2f + 2.4f * rng.Uniform<Float>(), -1.2f + 2.4f * rng.Uniform<Float>()));
            Vector3f dir = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
            return new Ray(aim - 4 * dir, dir, time);
        }

        internal sealed class Comparison
        {
            public int rays, hits, mismatches;
            public double maxT, maxP, maxN, maxUv, maxDpdu;
            public int outsideBounds;
            public double maxOutside;
            public string worstN = "";
            public int zeroObjectId, notBitwise;
        }

        private static double Ulps(double difference, double scale)
        {
            return difference / (Eps * Math.Max(scale, 1e-3));
        }

        private static void Accumulate(Comparison c, Ray ray, pstd.optional<ShapeIntersection> a, pstd.optional<ShapeIntersection> s, Bounds3f bounds)
        {
            ++c.rays;
            if (a.has_value() != s.has_value())
            {
                ++c.mismatches;
                return;
            }
            if (!a.has_value())
            {
                return;
            }
            ++c.hits;
            ShapeIntersection x = a.value(), y = s.value();
            if (x.intr.objectId == 0 && y.intr.objectId != 0)
            {
                ++c.zeroObjectId;
            }
            c.maxT = Math.Max(c.maxT, Ulps(Math.Abs(x.tHit - y.tHit), Math.Abs(y.tHit)));
            Point3f pa = x.intr.p(), ps = y.intr.p();
            c.maxP = Math.Max(c.maxP, Ulps(Length(pa - ps), Math.Max(Math.Abs(ps.x) + Math.Abs(ps.y) + Math.Abs(ps.z), 1)));
            double nUlps = Ulps(Length(x.intr.n - y.intr.n), 1);
            if (nUlps > c.maxN)
            {
                c.worstN = "uv (" + y.intr.uv.x.ToString("F3") + "," + y.intr.uv.y.ToString("F3") + ") t " + ray.time.ToString("F2") + " |dpdu| " + Length(y.intr.dpdu).ToString("E2") + " |dpdv| " + Length(y.intr.dpdv).ToString("E2");
            }
            c.maxN = Math.Max(c.maxN, nUlps);
            c.maxUv = Math.Max(c.maxUv, Ulps(Math.Abs(x.intr.uv.x - y.intr.uv.x) + Math.Abs(x.intr.uv.y - y.intr.uv.y), 1));
            Vector3f da = Normalize(x.intr.dpdu), ds = Normalize(y.intr.dpdu);
            if (!IsNaN(da.x) && !IsNaN(ds.x))
            {
                c.maxDpdu = Math.Max(c.maxDpdu, Ulps(Length(da - ds), 1));
            }
            // the hit must be inside of the bounds of the BVH
            Float slack = 8 * Eps * (Math.Abs(pa.x) + Math.Abs(pa.y) + Math.Abs(pa.z) + 1);
            double outside = Math.Max(Math.Max(Math.Max(bounds.pMin.x - pa.x, pa.x - bounds.pMax.x), Math.Max(bounds.pMin.y - pa.y, pa.y - bounds.pMax.y)),
                                      Math.Max(bounds.pMin.z - pa.z, pa.z - bounds.pMax.z));
            if (outside > slack)
            {
                ++c.outsideBounds;
                c.maxOutside = Math.Max(c.maxOutside, outside);
            }
        }

        private static string CaseScene(ShapeCase shape, string transformText)
        {
            return WorldPrefix + shape.prelude + "AttributeBegin\n" + transformText + shape.shape + "AttributeEnd\n";
        }

        private static readonly Float[] Times = new Float[] { 0, 0.13f, 0.37f, 0.5f, 0.71f, 1 };

        internal static Comparison CompareTransformBlur(ShapeCase shape, AnimatedTransform animation, int raysPerTime, ulong seed)
        {
            BuiltScene animated = BuildScene(CaseScene(shape, MotionBlock(animation)));
            Comparison result = new Comparison();
            Bounds3f bounds = animated.accel.Bounds();
            RNG rng = new RNG(seed);
            foreach (Float time in Times)
            {
                Transform at = animation.Interpolate(time);
                BuiltScene stat = BuildScene(CaseScene(shape, MatrixText(at)));
                for (int i = 0; i < raysPerTime; ++i)
                {
                    Ray ray = RandomRay(ref rng, at, time);
                    pstd.optional<ShapeIntersection> a = animated.accel.Intersect(ray, Infinity);
                    pstd.optional<ShapeIntersection> s = stat.accel.Intersect(ray, Infinity);
                    Accumulate(result, ray, a, s, bounds);
                    bool pa = animated.accel.IntersectP(ray, Infinity), ps = stat.accel.IntersectP(ray, Infinity);
                    if (pa != ps)
                    {
                        ++result.mismatches;
                    }
                }
            }
            return result;
        }

        private static void Report(string label, Comparison c)
        {
            Console.WriteLine("[MotionBlur] " + label + ": rays " + c.rays + " hits " + c.hits + " mismatches " + c.mismatches + " ulps t " + c.maxT.ToString("F1") + " p " +
                              c.maxP.ToString("F1") + " n " + c.maxN.ToString("F1") + " uv " + c.maxUv.ToString("F1") + " dpdu " + c.maxDpdu.ToString("F1") +
                              " worst n at " + c.worstN + " zero object ids " + c.zeroObjectId + " outside bounds " + c.outsideBounds + (c.outsideBounds > 0 ? " (max " + c.maxOutside.ToString("E2") + ")" : ""));
        }

        [TEST("MotionBlurTransform", "EveryShapeTwoKeys")]
        public static void EveryShapeTwoKeys()
        {
            AnimatedTransform animation = Keys(2);
            foreach (ShapeCase shape in ShapeCases())
            {
                Comparison c = CompareTransformBlur(shape, animation, 400, 11);
                Report("2 keys " + shape.name, c);
                EXPECT_TRUE(c.hits >= 20);
                EXPECT_EQ(0, c.mismatches);
                EXPECT_EQ(0, c.outsideBounds);
                EXPECT_EQ(0, c.zeroObjectId);
                EXPECT_TRUE(c.maxT < 200 && c.maxP < 400);
                EXPECT_TRUE(c.maxN < 10000 && c.maxUv < 10000 && c.maxDpdu < 10000);
            }
        }

        [TEST("MotionBlurTransform", "EveryShapeFourKeys")]
        public static void EveryShapeFourKeys()
        {
            AnimatedTransform animation = Keys(4);
            foreach (ShapeCase shape in ShapeCases())
            {
                Comparison c = CompareTransformBlur(shape, animation, 400, 12);
                Report("4 keys " + shape.name, c);
                EXPECT_TRUE(c.hits >= 20);
                EXPECT_EQ(0, c.mismatches);
                EXPECT_EQ(0, c.outsideBounds);
                EXPECT_EQ(0, c.zeroObjectId);
                EXPECT_TRUE(c.maxT < 200 && c.maxP < 400);
                EXPECT_TRUE(c.maxN < 10000 && c.maxUv < 10000 && c.maxDpdu < 10000);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Area lights on every shape: a light on an animated shape at the time t against the light on the static shape at Interpolate(t)

        private static string LightScene(ShapeCase shape, string transformText)
        {
            return WorldPrefix + shape.prelude + "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [ 3 3 3 ] \"bool twosided\" true\n" + transformText + shape.shape + "AttributeEnd\n";
        }

        internal sealed class LightComparison
        {
            public int lights, samples;
            public double worstIrradiance, worstPhi;
            public List<double> pdfDifferences = new List<double>();
            public int boundsEscape;
            public string unsupported = "";
            public double PdfPercentile(double q)
            {
                if (pdfDifferences.Count == 0)
                {
                    return 0;
                }
                pdfDifferences.Sort();
                return pdfDifferences[Math.Min(pdfDifferences.Count - 1, (int)(q * pdfDifferences.Count))];
            }
        }

        private static double Irradiance(Light light, Point3f p, Normal3f n, Float time, int count, List<Vector3f> directions, List<Float> pdfs)
        {
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            double sum = 0;
            LightSampleContext ctx = new LightSampleContext(new Point3fi(p), n, n, time);
            for (int i = 0; i < count; ++i)
            {
                Point2f u = new Point2f((i + 0.5f) / count, (Float)((i * 0.6180339887498949) % 1.0));
                pstd.optional<LightLiSample> ls = light.SampleLi(ctx, u, lambda, false);
                if (!ls || ls.value().pdf == 0)
                {
                    if (directions != null)
                    {
                        directions.Add(new Vector3f(0, 0, 0));
                        pdfs.Add(0);
                    }
                    continue;
                }
                sum += ls.value().L[0] * AbsDot(ls.value().wi, n) / ls.value().pdf;
                if (directions != null)
                {
                    directions.Add(ls.value().wi);
                    pdfs.Add(ls.value().pdf);
                }
            }
            return sum / count;
        }

        private static int Samples(int lightCount)
        {
            return lightCount > 40 ? 64 : 512;
        }

        internal static LightComparison CompareLights(ShapeCase shape, AnimatedTransform animation, ulong seed)
        {
            LightComparison result = new LightComparison();
            BuiltScene animated;
            try
            {
                animated = BuildScene(LightScene(shape, MotionBlock(animation)));
            }
            catch (Exception e)
            {
                result.unsupported = e.Message;
                return result;
            }
            RNG rng = new RNG(seed);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            foreach (Float time in new Float[] { 0, 0.55f, 1 })
            {
                Transform at = animation.Interpolate(time);
                BuiltScene stat = BuildScene(LightScene(shape, MatrixText(at)));
                if (animated.lights.Count != stat.lights.Count)
                {
                    result.lights = -1;
                    return result;
                }
                result.lights = animated.lights.Count;
                Point3f center = at.Apply(new Point3f(0, 0, 0));
                for (int receiver = 0; receiver < 2; ++receiver)
                {
                    Vector3f dir = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Point3f p = center + 3.5f * dir;
                    Normal3f n = new Normal3f(-dir.x, -dir.y, -dir.z);
                    double totalA = 0, totalS = 0;
                    for (int i = 0; i < animated.lights.Count; ++i)
                    {
                        List<Vector3f> dirs = new List<Vector3f>();
                        List<Float> pdfs = new List<Float>();
                        double a = Irradiance(animated.lights[i], p, n, time, Samples(animated.lights.Count), null, null);
                        double s = Irradiance(stat.lights[i], p, n, time, Samples(animated.lights.Count), dirs, pdfs);
                        totalA += a;
                        totalS += s;
                        LightSampleContext ctx = new LightSampleContext(new Point3fi(p), n, n, time);
                        for (int k = 0; k < dirs.Count; ++k)
                        {
                            if (pdfs[k] == 0)
                            {
                                continue;
                            }
                            Float pa = animated.lights[i].PDF_Li(ctx, dirs[k], false), ps = stat.lights[i].PDF_Li(ctx, dirs[k], false);
                            ++result.samples;
                            if (ps > 0)
                            {
                                result.pdfDifferences.Add(Math.Abs(pa - ps) / ps);
                            }
                        }
                        SampledSpectrum phiA = animated.lights[i].Phi(lambda), phiS = stat.lights[i].Phi(lambda);
                        result.worstPhi = Math.Max(result.worstPhi, Math.Abs(phiA[0] - phiS[0]) / Math.Max(1e-6f, phiS[0]));
                        // the bounds of the animated light contain the bounds of the static light
                        pstd.optional<LightBounds> ba = animated.lights[i].Bounds(), bs = stat.lights[i].Bounds();
                        if (ba.has_value() && bs.has_value())
                        {
                            Bounds3f x = ba.value().bounds, y = bs.value().bounds;
                            Float slack = 1e-4f;
                            if (y.pMin.x < x.pMin.x - slack || y.pMin.y < x.pMin.y - slack || y.pMin.z < x.pMin.z - slack ||
                                y.pMax.x > x.pMax.x + slack || y.pMax.y > x.pMax.y + slack || y.pMax.z > x.pMax.z + slack)
                            {
                                ++result.boundsEscape;
                            }
                        }
                    }
                    if (Math.Max(totalA, totalS) > 0)
                    {
                        result.worstIrradiance = Math.Max(result.worstIrradiance, Math.Abs(totalA - totalS) / Math.Max(totalA, totalS));
                    }
                }
            }
            return result;
        }

        [TEST("MotionBlurTransform", "AreaLightOnEveryShape")]
        public static void AreaLightOnEveryShape()
        {
            AnimatedTransform animation = Keys(4);
            foreach (ShapeCase shape in ShapeCases())
            {
                LightComparison c = CompareLights(shape, animation, 21);
                Console.WriteLine("[MotionBlur] light on " + shape.name + ": lights " + c.lights + " pdf samples " + c.samples + " irradiance rel " + c.worstIrradiance.ToString("E2") +
                                  " pdf rel median " + c.PdfPercentile(0.5).ToString("E2") + " 99% " + c.PdfPercentile(0.99).ToString("E2") + " max " + c.PdfPercentile(1).ToString("E2") + " phi rel " + c.worstPhi.ToString("E2") + " bounds escapes " + c.boundsEscape);
                if (c.unsupported.Length > 0)
                {
                    Console.WriteLine("[MotionBlur] light on " + shape.name + ": not supported (" + c.unsupported + ")");
                    continue;
                }
                EXPECT_TRUE(c.lights > 0);
                EXPECT_TRUE(c.worstIrradiance < 2e-3);
                EXPECT_TRUE(c.worstPhi < 1e-3);
                EXPECT_EQ(0, c.boundsEscape);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Part 2 (group MotionBlurDeform*): deformation blur. A shape of several keys at the time t against the static shape of the interpolated keys.
        //
        // Every case is a function of a phase phi that is affine in all the numbers of the shape, so that the shape of the keys phi_k at times t_k
        // interpolated linearly in time is the static shape at the phase phi(t), the piecewise linear function of the time through the (t_k, phi_k).

        internal sealed class DeformCase
        {
            public string name;
            public Func<Float, string> shape;
            public string prelude = "";
            public bool exact = true;   // false: a SlicedShape (the time is quantized)
            public DeformCase(string name, Func<Float, string> shape, bool exact = true, string prelude = "")
            {
                this.name = name;
                this.shape = shape;
                this.exact = exact;
                this.prelude = prelude;
            }
        }

        private static string Arr(IEnumerable<double> values)
        {
            StringBuilder sb = new StringBuilder("[ ");
            foreach (double v in values)
            {
                sb.Append(F((Float)v)).Append(' ');
            }
            return sb.Append(']').ToString();
        }

        private static double[] CubeBase = new double[] { -0.6, -0.6, -0.6, 0.6, -0.6, -0.6, 0.6, 0.6, -0.6, -0.6, 0.6, -0.6, -0.6, -0.6, 0.6, 0.6, -0.6, 0.6, 0.6, 0.6, 0.6, -0.6, 0.6, 0.6 };

        // The cube with the vertices moved by an amount that depends on the vertex
        private static IEnumerable<double> MovedCube(Float phi, double amount)
        {
            for (int i = 0; i < 8; ++i)
            {
                yield return CubeBase[3 * i] + phi * amount * Math.Sin(1.7 * i + 0.3);
                yield return CubeBase[3 * i + 1] + phi * amount * Math.Cos(2.3 * i);
                yield return CubeBase[3 * i + 2] + phi * amount * Math.Sin(0.9 * i + 1);
            }
        }

        private const string CubeTriangles = "\"integer indices\" [ 0 2 1  0 3 2  4 5 6  4 6 7  0 1 5  0 5 4  3 7 6  3 6 2  0 4 7  0 7 3  1 2 6  1 6 5 ] ";

        private static IEnumerable<double> Spread(Float phi, int count, double scale, double offset)
        {
            for (int i = 0; i < count; ++i)
            {
                yield return offset + phi * scale * Math.Sin(1.3 * i + 0.4);
            }
        }

        internal static List<DeformCase> DeformCases()
        {
            List<DeformCase> cases = new List<DeformCase>();
            cases.Add(new DeformCase("trianglemesh", (Float phi) =>
            {
                List<double> n = new List<double>();
                for (int i = 0; i < 8; ++i)
                {
                    double x = CubeBase[3 * i], y = CubeBase[3 * i + 1], z = CubeBase[3 * i + 2];
                    n.Add(x + phi * 0.4 * Math.Sin(i)); n.Add(y + phi * 0.4 * Math.Cos(i)); n.Add(z - phi * 0.3);
                }
                return "Shape \"trianglemesh\" " + CubeTriangles + "\"point3 P\" " + Arr(MovedCube(phi, 0.3)) + " \"normal N\" " + Arr(n) +
                       " \"point2 uv\" [ 0.35 0.62  1.15 0.58  1.17 1.38  0.37 1.42  0.83 0.38  1.63 0.34  1.65 1.14  0.85 1.18 ]\n";
            }));
            cases.Add(new DeformCase("trianglemesh_nonormals", (Float phi) =>
                "Shape \"trianglemesh\" " + CubeTriangles + "\"point3 P\" " + Arr(MovedCube(phi, 0.4)) + "\n"));
            cases.Add(new DeformCase("bilinearmesh", (Float phi) =>
            {
                double[] p = new double[] { -0.8, -0.8, 0, 0.8, -0.8, 0.2, -0.8, 0.8, 0.1, 0.8, 0.8, 0 };
                double[] d = new double[] { 0.1, 0.2, 0.5, -0.3, 0.1, -0.4, 0.2, -0.2, 0.6, 0.0, 0.3, -0.5 };
                List<double> q = new List<double>();
                List<double> n = new List<double>();
                for (int i = 0; i < 12; ++i)
                {
                    q.Add(p[i] + phi * d[i]);
                }
                for (int i = 0; i < 4; ++i)
                {
                    n.Add(0.2 * phi * (i - 1.5)); n.Add(0.1 * phi); n.Add(1);
                }
                return "Shape \"bilinearmesh\" \"point3 P\" " + Arr(q) + " \"normal N\" " + Arr(n) + " \"point2 uv\" [ 0 0 1 0 0 1 1 1 ]\n";
            }));
            cases.Add(new DeformCase("polygon", (Float phi) =>
                "Shape \"polygon\" \"point3 P\" " + Arr(new double[] { -0.7 + phi * 0.2, -0.5, phi * 0.3, 0, -0.8 - phi * 0.2, 0, 0.7, -0.5 + phi * 0.2, 0, 0.5, 0.6, phi * -0.3,
                                                                  -0.5, 0.6 - phi * 0.1, 0.1 * phi }) + "\n"));
            cases.Add(new DeformCase("sphere", (Float phi) =>
                "Shape \"sphere\" \"float radius\" " + F(0.5f + 0.4f * phi) + " \"float zmin\" " + F(-0.4f - 0.2f * phi) + " \"float zmax\" " + F(0.5f + 0.2f * phi) +
                " \"float phimax\" " + F(360 - 90 * phi) + "\n"));
            cases.Add(new DeformCase("cylinder", (Float phi) =>
                "Shape \"cylinder\" \"float radius\" " + F(0.4f + 0.3f * phi) + " \"float zmin\" -0.7 \"float zmax\" " + F(0.5f + 0.3f * phi) + " \"float phimax\" " + F(360 - 60 * phi) + "\n"));
            cases.Add(new DeformCase("disk", (Float phi) =>
                "Shape \"disk\" \"float height\" " + F(0.1f * phi) + " \"float radius\" " + F(0.6f + 0.3f * phi) + " \"float innerradius\" " + F(0.1f + 0.1f * phi) + "\n"));
            cases.Add(new DeformCase("cone", (Float phi) =>
                "Shape \"cone\" \"float radius\" " + F(0.5f + 0.3f * phi) + " \"float height\" " + F(1.0f + 0.4f * phi) + "\n"));
            cases.Add(new DeformCase("paraboloid", (Float phi) =>
                "Shape \"paraboloid\" \"float radius\" " + F(0.5f + 0.3f * phi) + " \"float zmin\" 0 \"float zmax\" " + F(0.8f + 0.3f * phi) + "\n"));
            cases.Add(new DeformCase("hyperboloid", (Float phi) =>
                "Shape \"hyperboloid\" \"point3 p1\" [ " + F(0.3f + 0.2f * phi) + " 0 -0.6 ] \"point3 p2\" [ 0.5 " + F(0.3f * phi) + " 0.6 ]\n"));
            cases.Add(new DeformCase("torus", (Float phi) =>
                "Shape \"torus\" \"float majorradius\" " + F(0.6f + 0.2f * phi) + " \"float minorradius\" " + F(0.2f + 0.1f * phi) + "\n"));
            cases.Add(new DeformCase("points", (Float phi) =>
                "Shape \"points\" \"point3 P\" " + Arr(new double[] { -0.5 + 0.4 * phi, 0, 0, 0.3, 0.4 - 0.3 * phi, 0.1, 0.2, -0.5, -0.2 + 0.5 * phi }) +
                " \"float width\" " + Arr(new double[] { 0.5 + 0.3 * phi, 0.4, 0.6 - 0.3 * phi }) + "\n"));
            cases.Add(new DeformCase("points_disk", (Float phi) =>
                "Shape \"points\" \"string type\" \"disk\" \"point3 P\" " + Arr(new double[] { -0.5 + 0.4 * phi, 0, 0, 0.3, 0.4, 0.1 }) +
                " \"normal N\" " + Arr(new double[] { 0, 0.5 * phi, 1, 0.3, 0.3, 0.9 - 0.5 * phi }) + " \"float width\" " + Arr(new double[] { 0.8, 0.7 + 0.3 * phi }) + "\n"));
            cases.Add(new DeformCase("curves", (Float phi) =>
                "Shape \"curves\" \"integer nvertices\" [ 4 ] \"point3 P\" " + Arr(new double[] { -0.8, 0, 0, -0.3, 0.5 - 0.6 * phi, 0, 0.3, -0.5 + 0.7 * phi, 0.2 * phi, 0.8, 0, 0 }) +
                " \"float width\" " + Arr(new double[] { 0.2 + 0.1 * phi, 0.1 }) + "\n"));
            cases.Add(new DeformCase("curve", (Float phi) =>
                "Shape \"curve\" \"string type\" \"cylinder\" \"point3 P\" " + Arr(new double[] { -0.8, 0, 0, -0.3, 0.5 - 0.6 * phi, 0, 0.3, -0.5 + 0.7 * phi, 0.2 * phi, 0.8, 0, 0 }) +
                " \"float width\" " + F(0.2f + 0.1f * phi) + "\n"));
            // The shapes that have no exact method (a SlicedShape)
            cases.Add(new DeformCase("loopsubdiv", (Float phi) =>
                "Shape \"loopsubdiv\" \"integer levels\" 2 " + CubeTriangles + "\"point3 P\" " + Arr(MovedCube(phi, 0.3)) + "\n"));
            cases.Add(new DeformCase("catmarksubdiv", (Float phi) =>
                "Shape \"catmarksubdiv\" \"integer faceVertexCounts\" [ 4 4 4 4 4 4 ] \"integer indices\" [ 0 3 2 1  4 5 6 7  0 1 5 4  3 7 6 2  0 4 7 3  1 2 6 5 ] \"point3 P\" " +
                Arr(MovedCube(phi, 0.3)) + "\n", false));
            cases.Add(new DeformCase("patchmesh", (Float phi) =>
            {
                List<double> p = new List<double>();
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        p.Add(-0.8 + 0.5333 * i); p.Add(-0.8 + 0.5333 * j); p.Add(0.3 * Math.Sin(i + 2 * j) * (1 + phi));
                    }
                }
                return "Shape \"patchmesh\" \"string type\" \"bicubic\" \"integer nu\" 4 \"integer nv\" 4 \"point3 P\" " + Arr(p) + "\n";
            }, false));
            cases.Add(new DeformCase("nupatch", (Float phi) =>
            {
                List<double> p = new List<double>();
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        p.Add(-0.8 + 0.5333 * i); p.Add(-0.8 + 0.5333 * j); p.Add(0.3 * Math.Sin(i + 2 * j) * (1 + phi));
                    }
                }
                return "Shape \"nupatch\" \"integer nu\" 4 \"integer nv\" 4 \"integer uorder\" 4 \"integer vorder\" 4 \"float uknots\" [ 0 0 0 0 1 1 1 1 ] " +
                       "\"float vknots\" [ 0 0 0 0 1 1 1 1 ] \"point3 P\" " + Arr(p) + "\n";
            }, false));
            cases.Add(new DeformCase("curvedpatches", (Float phi) =>
                "Shape \"curvedpatches\" \"integer faceVertexCounts\" [ 3 3 3 3 3 3 3 3 3 3 3 3 ] " + CubeTriangles + "\"point3 P\" " + Arr(MovedCube(phi, 0.3)) + "\n", false));
            cases.Add(new DeformCase("ribbon", (Float phi) =>
                "Shape \"ribbon\" \"point3 P\" " + Arr(new double[] { -0.8, 0, 0, -0.3, 0.4 + 0.3 * phi, 0, 0.3, -0.4, 0.3 * phi, 0.8, 0, 0 }) + " \"float width\" " + F(0.2f + 0.1f * phi) + "\n", false));
            cases.Add(new DeformCase("rocap", (Float phi) =>
                "Shape \"rocap\" \"point3 P\" " + Arr(new double[] { -0.8, 0, 0, -0.3, 0.4 + 0.3 * phi, 0, 0.3, -0.4, 0.3 * phi, 0.8, 0, 0 }) + " \"float radius\" " + F(0.15f + 0.05f * phi) + "\n", false));
            cases.Add(new DeformCase("teapot", (Float phi) => "Shape \"teapot\" \"float scale\" " + F(0.25f + 0.1f * phi) + "\n", false));
            cases.Add(new DeformCase("volume", (Float phi) =>
                "Shape \"volume\" \"string type\" \"box\" \"point3 p0\" [ -0.6 -0.5 -0.4 ] \"point3 p1\" [ " + F(0.4f + 0.3f * phi) + " 0.5 0.4 ]\n", false));
            cases.Add(new DeformCase("blobby", (Float phi) =>
                "Shape \"blobby\" \"integer nleaf\" 1 \"integer code\" [ 1001 0 ] \"float flt\" " + Arr(new double[] { 1 + 0.5 * phi, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0.3 * phi, 0, 0, 1 }) + "\n", false));
            cases.Add(new DeformCase("sphere_diced", (Float phi) => "Shape \"sphere\" \"float radius\" " + F(0.6f + 0.3f * phi) + " \"texture displacement\" \"bumps3\" \"integer dicemaxrate\" 6 \"float edgelength\" 0.001\n", true,
                                     "Texture \"bumps3\" \"float\" \"constant\" \"float value\" 0.05\n"));
            return cases;
        }

        // The keys: phases at times
        internal static readonly Float[] KeysPhi2 = new Float[] { 0, 1 };
        internal static readonly Float[] KeysTime2 = new Float[] { 0, 1 };
        internal static readonly Float[] KeysPhi4 = new Float[] { 0, 0.9f, 0.3f, 1 };
        internal static readonly Float[] KeysTime4 = new Float[] { 0, 0.3f, 0.65f, 1 };

        internal static Float PhaseAt(Float[] phis, Float[] times, Float time)
        {
            int k;
            Float f;
            ShapeMotion.FindKey(times, time, out k, out f);
            return (1 - f) * phis[k] + f * phis[k + 1];
        }

        internal static string DeformBlock(DeformCase c, Float[] phis, Float[] times)
        {
            StringBuilder sb = new StringBuilder("MotionBegin [");
            foreach (Float t in times)
            {
                sb.Append(' ').Append(F(t));
            }
            sb.Append(" ]\n");
            foreach (Float phi in phis)
            {
                sb.Append(c.shape(phi));
            }
            sb.Append("MotionEnd\n");
            return sb.ToString();
        }

        internal static string DeformScene(DeformCase c, string transformText, string shapeText)
        {
            return WorldPrefix + c.prelude + "AttributeBegin\n" + transformText + shapeText + "AttributeEnd\n";
        }

        // The time at which the shape of a case is a static shape: the time itself for the exact shapes, the time of the nearest slice for the others
        internal static Float EffectiveTime(DeformCase c, Float[] times, Float time)
        {
            if (c.exact)
            {
                return time;
            }
            int k;
            Float f;
            ShapeMotion.FindKey(times, time, out k, out f);
            int j = (int)(f * ShapeMotion.DefaultSliceCount + 0.5f);
            return SlicedShape.SliceTime(times, ShapeMotion.DefaultSliceCount, k * ShapeMotion.DefaultSliceCount + j);
        }

        internal static Comparison CompareDeform(DeformCase c, Float[] phis, Float[] times, AnimatedTransform animation, int raysPerTime, ulong seed, bool bitwise = false)
        {
            string transformText = animation != null ? MotionBlock(animation) : "";
            BuiltScene deforming = BuildScene(DeformScene(c, transformText, DeformBlock(c, phis, times)));
            Comparison result = new Comparison();
            Bounds3f bounds = deforming.accel.Bounds();
            RNG rng = new RNG(seed);
            List<Float> testTimes = new List<Float>(times);
            testTimes.AddRange(new Float[] { 0.05f, 0.2f, 0.37f, 0.5f, 0.71f, 0.93f });
            foreach (Float time in testTimes)
            {
                Float effective = EffectiveTime(c, times, time);
                Transform at = animation != null ? animation.Interpolate(time) : new Transform();
                string staticTransform = animation != null ? MatrixText(at) : "";
                BuiltScene stat = BuildScene(DeformScene(c, staticTransform, c.shape(PhaseAt(phis, times, effective))));
                for (int i = 0; i < raysPerTime; ++i)
                {
                    Ray ray = RandomRay(ref rng, at, time);
                    pstd.optional<ShapeIntersection> a = deforming.accel.Intersect(ray, Infinity);
                    pstd.optional<ShapeIntersection> s = stat.accel.Intersect(ray, Infinity);
                    Accumulate(result, ray, a, s, bounds);
                    if (bitwise && a.has_value() && s.has_value())
                    {
                        if (a.value().tHit != s.value().tHit || a.value().intr.p() != s.value().intr.p() || a.value().intr.n != s.value().intr.n ||
                            a.value().intr.uv != s.value().intr.uv || a.value().intr.dpdu != s.value().intr.dpdu)
                        {
                            ++result.notBitwise;
                        }
                    }
                    if (deforming.accel.IntersectP(ray, Infinity) != stat.accel.IntersectP(ray, Infinity))
                    {
                        ++result.mismatches;
                    }
                }
            }
            return result;
        }

        private static void CheckDeform(string label, DeformCase c, Float[] phis, Float[] times, AnimatedTransform animation)
        {
            Comparison r = CompareDeform(c, phis, times, animation, 300, 31);
            Report(label + " " + c.name + (c.exact ? "" : " (sliced)"), r);
            EXPECT_TRUE(r.hits >= 20);
            EXPECT_EQ(0, r.mismatches);
            EXPECT_EQ(0, r.outsideBounds);
            EXPECT_TRUE(r.maxT < 400 && r.maxP < 800);
            EXPECT_TRUE(r.maxN < 12000 && r.maxUv < 12000 && r.maxDpdu < 12000);
        }

        [TEST("MotionBlurDeform", "EveryShapeTwoKeys")]
        public static void DeformEveryShapeTwoKeys()
        {
            foreach (DeformCase c in DeformCases())
            {
                CheckDeform("deform 2 keys", c, KeysPhi2, KeysTime2, null);
            }
        }

        [TEST("MotionBlurDeform", "EveryShapeFourKeys")]
        public static void DeformEveryShapeFourKeys()
        {
            foreach (DeformCase c in DeformCases())
            {
                CheckDeform("deform 4 keys", c, KeysPhi4, KeysTime4, null);
            }
        }

        [TEST("MotionBlurDeform", "WithTransformBlur")]
        public static void DeformWithTransformBlur()
        {
            foreach (DeformCase c in DeformCases())
            {
                CheckDeform("deform + transform 2 keys", c, KeysPhi2, KeysTime2, Keys(2));
                CheckDeform("deform + transform 4 keys", c, KeysPhi4, KeysTime4, Keys(4));
            }
        }

        // Is the construction of a shape deterministic? (a static shape built twice must intersect bitwise equally; the comparisons mean nothing otherwise)
        [TEST("MotionBlurDeform", "StaticConstructionIsDeterministic")]
        public static void StaticDeterministic()
        {
            foreach (DeformCase c in DeformCases())
            {
                BuiltScene first = BuildScene(DeformScene(c, "", c.shape(0.4f)));
                BuiltScene second = BuildScene(DeformScene(c, "", c.shape(0.4f)));
                RNG rng = new RNG(5);
                int different = 0, hits = 0;
                for (int i = 0; i < 1000; ++i)
                {
                    Ray ray = RandomRay(ref rng, new Transform(), 0);
                    pstd.optional<ShapeIntersection> a = first.accel.Intersect(ray, Infinity);
                    pstd.optional<ShapeIntersection> b = second.accel.Intersect(ray, Infinity);
                    if (a.has_value() != b.has_value() || (a.has_value() && (a.value().tHit != b.value().tHit || a.value().intr.n != b.value().intr.n)))
                    {
                        ++different;
                    }
                    hits += a.has_value() ? 1 : 0;
                }
                Console.WriteLine("[MotionBlur] determinism " + c.name + ": hits " + hits + " different " + different);
                EXPECT_EQ(0, different);
            }
        }

        [TEST("MotionBlurDeform", "KeysAreReproducedExactly")]
        public static void DeformKeysExact()
        {
            // At the key times only (the other times differ by the rounding of the interpolation)
            foreach (DeformCase c in DeformCases())
            {
                int notBitwise = 0, hits = 0;
                for (int k = 0; k < KeysTime4.Length; ++k)
                {
                    BuiltScene deforming = BuildScene(DeformScene(c, "", DeformBlock(c, KeysPhi4, KeysTime4)));
                    BuiltScene stat = BuildScene(DeformScene(c, "", c.shape(KeysPhi4[k])));
                    RNG rng = new RNG(77);
                    for (int i = 0; i < 400; ++i)
                    {
                        Ray ray = RandomRay(ref rng, new Transform(), KeysTime4[k]);
                        pstd.optional<ShapeIntersection> a = deforming.accel.Intersect(ray, Infinity);
                        pstd.optional<ShapeIntersection> s = stat.accel.Intersect(ray, Infinity);
                        EXPECT_EQ(a.has_value(), s.has_value());
                        if (a.has_value() && s.has_value())
                        {
                            ++hits;
                            if (a.value().tHit != s.value().tHit || a.value().intr.p() != s.value().intr.p() || a.value().intr.n != s.value().intr.n ||
                                a.value().intr.uv != s.value().intr.uv || a.value().intr.dpdu != s.value().intr.dpdu || a.value().intr.dpdv != s.value().intr.dpdv)
                            {
                                ++notBitwise;
                            }
                        }
                    }
                }
                Console.WriteLine("[MotionBlur] at the key times " + c.name + ": hits " + hits + ", not bitwise equal to the static key " + notBitwise);
                EXPECT_TRUE(hits >= 20);
                EXPECT_EQ(0, notBitwise);
            }
        }

        // ------------------------------------------------------------------------------------------
        // (c) Bounds: every point of the surface at every time of the shutter is inside of the bounds of the BVH (1e5 rays at 100 times per shape)

        [TEST("MotionBlurDeform", "BoundsContainTheSurfaceAtEveryTime")]
        public static void DeformBounds()
        {
            foreach (DeformCase c in DeformCases())
            {
                foreach (int keyCount in new int[] { 2, 4 })
                {
                    Float[] phis = keyCount == 2 ? KeysPhi2 : KeysPhi4;
                    Float[] times = keyCount == 2 ? KeysTime2 : KeysTime4;
                    BuiltScene deforming = BuildScene(DeformScene(c, "", DeformBlock(c, phis, times)));
                    Bounds3f bounds = deforming.accel.Bounds();
                    RNG rng = new RNG(99);
                    int hits = 0, outside = 0;
                    double worst = 0;
                    const int timesCount = 100, raysPerTime = 1000;
                    for (int t = 0; t < timesCount; ++t)
                    {
                        Float time = (t + 0.5f) / timesCount;
                        BuiltScene stat = BuildScene(DeformScene(c, "", c.shape(PhaseAt(phis, times, EffectiveTime(c, times, time)))));
                        for (int i = 0; i < raysPerTime; ++i)
                        {
                            Ray ray = RandomRay(ref rng, new Transform(), time);
                            pstd.optional<ShapeIntersection> s = stat.accel.Intersect(ray, Infinity);
                            if (!s.has_value())
                            {
                                continue;
                            }
                            ++hits;
                            Point3f p = s.value().intr.p();
                            double escape = Math.Max(Math.Max(bounds.pMin.x - p.x, p.x - bounds.pMax.x), Math.Max(Math.Max(bounds.pMin.y - p.y, p.y - bounds.pMax.y), Math.Max(bounds.pMin.z - p.z, p.z - bounds.pMax.z)));
                            if (escape > 1e-5)
                            {
                                ++outside;
                                worst = Math.Max(worst, escape);
                            }
                        }
                    }
                    Console.WriteLine("[MotionBlur] bounds " + c.name + " " + keyCount + " keys: " + (timesCount * raysPerTime) + " rays, " + hits + " surface points, outside " + outside + (outside > 0 ? " max " + worst.ToString("E2") : ""));
                    EXPECT_TRUE(hits > 1000);
                    EXPECT_EQ(0, outside);
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // (d) Syntax

        private static string ErrorOf(string text)
        {
            try
            {
                BuildScene(text);
            }
            catch (Exception e)
            {
                Exception inner = e;
                while (inner.InnerException != null && !(inner is FatalException))
                {
                    inner = inner.InnerException;
                }
                return inner.Message;
            }
            return null;
        }

        private const string Tri = "\"integer indices\" [ 0 1 2 ] ";

        [TEST("MotionBlurDeform", "SyntaxIsAccepted")]
        public static void SyntaxAccepted()
        {
            // two keys, three keys, shapes of other types, with an animated transformation
            string[] good = new string[]
            {
                WorldPrefix + "MotionBegin [ 0 1 ]\nShape \"trianglemesh\" " + Tri + "\"point3 P\" [ 0 0 0 1 0 0 0 1 0 ]\nShape \"trianglemesh\" " + Tri + "\"point3 P\" [ 0 0 1 1 0 1 0 1 1 ]\nMotionEnd\n",
                WorldPrefix + "MotionBegin [ 0 0.5 1 ]\nShape \"sphere\" \"float radius\" 1\nShape \"sphere\" \"float radius\" 2\nShape \"sphere\" \"float radius\" 1\nMotionEnd\n",
                WorldPrefix + "MotionBegin [ 0 1 ]\nTranslate 0 0 0\nTranslate 1 0 0\nMotionEnd\nMotionBegin [ 0 1 ]\nShape \"sphere\" \"float radius\" 1\nShape \"sphere\" \"float radius\" 2\nMotionEnd\n",
                WorldPrefix + "AreaLightSource \"diffuse\" \"rgb L\" [ 1 1 1 ]\nMotionBegin [ 0 1 ]\nShape \"disk\" \"float radius\" 1\nShape \"disk\" \"float radius\" 2\nMotionEnd\n",
            };
            for (int i = 0; i < good.Length; ++i)
            {
                string e = ErrorOf(good[i]);
                Console.WriteLine("[MotionBlur] syntax " + i + ": " + (e == null ? "accepted" : "ERROR " + e));
                EXPECT_TRUE(e == null);
            }
        }

        [TEST("MotionBlurDeform", "SyntaxErrors")]
        public static void SyntaxErrors()
        {
            string t1 = "Shape \"trianglemesh\" " + Tri + "\"point3 P\" [ 0 0 0 1 0 0 0 1 0 ]\n";
            string t2 = "Shape \"trianglemesh\" " + Tri + "\"point3 P\" [ 0 0 1 1 0 1 0 1 1 ]\n";
            string[][] bad = new string[][]
            {
                new string[] { "different shape names", "MotionBegin [ 0 1 ]\n" + t1 + "Shape \"sphere\" \"float radius\" 1\nMotionEnd\n", "same" },
                new string[] { "more shapes than times", "MotionBegin [ 0 1 ]\n" + t1 + t2 + t1 + "MotionEnd\n", "" },
                new string[] { "fewer shapes than times", "MotionBegin [ 0 0.5 1 ]\n" + t1 + t2 + "MotionEnd\n", "" },
                new string[] { "times not increasing", "MotionBegin [ 0 0 ]\n" + t1 + t2 + "MotionEnd\n", "" },
                new string[] { "times decreasing", "MotionBegin [ 1 0 ]\n" + t1 + t2 + "MotionEnd\n", "" },
                new string[] { "different vertex counts", "MotionBegin [ 0 1 ]\n" + t1 + "Shape \"trianglemesh\" " + Tri + "\"point3 P\" [ 0 0 1 1 0 1 0 1 1 5 5 5 ]\nMotionEnd\n", "" },
                new string[] { "different indices", "MotionBegin [ 0 1 ]\n" + t1 + "Shape \"trianglemesh\" \"integer indices\" [ 0 2 1 ] \"point3 P\" [ 0 0 1 1 0 1 0 1 1 ]\nMotionEnd\n", "" },
                new string[] { "different parameters", "MotionBegin [ 0 1 ]\n" + t1 + "Shape \"trianglemesh\" " + Tri + "\"point3 P\" [ 0 0 1 1 0 1 0 1 1 ] \"normal N\" [ 0 0 1 0 0 1 0 0 1 ]\nMotionEnd\n", "" },
                new string[] { "different strings", "MotionBegin [ 0 1 ]\nShape \"sphere\" \"string note\" \"a\"\nShape \"sphere\" \"string note\" \"b\"\nMotionEnd\n", "" },
                new string[] { "transform and shape in one block", "MotionBegin [ 0 1 ]\nTranslate 1 0 0\n" + t1 + "MotionEnd\n", "" },
                new string[] { "shape and transform in one block", "MotionBegin [ 0 1 ]\n" + t1 + "Translate 1 0 0\nMotionEnd\n", "" },
                new string[] { "one key", "MotionBegin [ 0 ]\n" + t1 + "MotionEnd\n", "" },
                new string[] { "no shape", "MotionBegin [ 0 1 ]\nMotionEnd\n", "" },
                new string[] { "index out of range", "MotionBegin [ 0 1 ]\n" + t1 + "Shape \"trianglemesh\" " + Tri + "\"point3 P\" [ 0 0 1 1 0 1 ]\nMotionEnd\n", "" },
                new string[] { "texture changed", "Texture \"a\" \"float\" \"constant\" \"float value\" 1\nMotionBegin [ 0 1 ]\nShape \"sphere\" \"texture alpha\" \"a\"\nShape \"sphere\" \"texture alpha\" \"b\"\nMotionEnd\n", "" },
            };
            foreach (string[] b in bad)
            {
                string e = ErrorOf(WorldPrefix + b[1]);
                Console.WriteLine("[MotionBlur] syntax error " + b[0] + ": " + (e == null ? "NO ERROR" : e));
                EXPECT_TRUE(e != null);
                if (e != null && b[2].Length > 0)
                {
                    EXPECT_TRUE(e.Contains(b[2]));
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // (e) Area lights: the light of a shape with deformation. At each time the light must be the one of the static shape of that time (same samples, same pdf), the
        // power must be the mean of the powers over the shutter, the bounds must contain the bounds at every time, and sampling the light must agree with casting rays
        // at the shape (the irradiance of a receiver by light sampling against cosine sampled rays, within a t statistic).

        private static string DeformLightScene(DeformCase c, string shapeText)
        {
            return WorldPrefix + c.prelude + "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [ 3 3 3 ] \"bool twosided\" true\n" + shapeText + "AttributeEnd\n";
        }

        private static void DeformLight(DeformCase c, Float[] phis, Float[] times, AnimatedTransform animation)
        {
            string transformText = animation != null ? MotionBlock(animation) : "";
            BuiltScene animated;
            try
            {
                animated = BuildScene(DeformLightScene(c, transformText + DeformBlock(c, phis, times)));
            }
            catch (Exception e)
            {
                Console.WriteLine("[MotionBlur] deforming light on " + c.name + ": NOT SUPPORTED (" + e.Message + ")");
                return;
            }
            RNG rng = new RNG(23);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            double worstIrradiance = 0, worstTStatistic = 0;
            int escapes = 0, pdfChecks = 0, pdfBad = 0;
            double meanPhiStatic = 0;
            const int phiTimes = 33;
            for (int i = 0; i < phiTimes; ++i)
            {
                Float time = (i + 0.5f) / phiTimes;
                Transform at = animation != null ? animation.Interpolate(time) : new Transform();
                BuiltScene stat = BuildScene(DeformLightScene(c, (animation != null ? MatrixText(at) : "") + c.shape(PhaseAt(phis, times, EffectiveTime(c, times, time)))));
                double phiStatic = 0;
                for (int l = 0; l < stat.lights.Count; ++l)
                {
                    phiStatic += stat.lights[l].Phi(lambda)[0];
                }
                meanPhiStatic += phiStatic / phiTimes;
            }
            double phiAnimated = 0;
            for (int l = 0; l < animated.lights.Count; ++l)
            {
                phiAnimated += animated.lights[l].Phi(lambda)[0];
            }
            foreach (Float time in new Float[] { 0, 0.3f, 0.55f, 0.9f, 1 })
            {
                Transform at = animation != null ? animation.Interpolate(time) : new Transform();
                BuiltScene stat = BuildScene(DeformLightScene(c, (animation != null ? MatrixText(at) : "") + c.shape(PhaseAt(phis, times, EffectiveTime(c, times, time)))));
                if (stat.lights.Count != animated.lights.Count)
                {
                    EXPECT_EQ(stat.lights.Count, animated.lights.Count);
                    return;
                }
                Point3f center = at.Apply(new Point3f(0, 0, 0));
                for (int receiver = 0; receiver < 2; ++receiver)
                {
                    Vector3f dir = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Point3f p = center + 3.5f * dir;
                    Normal3f n = new Normal3f(-dir.x, -dir.y, -dir.z);
                    double totalA = 0, totalS = 0;
                    List<double> samplesA = new List<double>();
                    for (int l = 0; l < animated.lights.Count; ++l)
                    {
                        List<Vector3f> dirs = new List<Vector3f>();
                        List<Float> pdfs = new List<Float>();
                        totalA += Irradiance(animated.lights[l], p, n, time, Samples(animated.lights.Count), null, null);
                        totalS += Irradiance(stat.lights[l], p, n, time, Samples(animated.lights.Count), dirs, pdfs);
                        LightSampleContext ctx = new LightSampleContext(new Point3fi(p), n, n, time);
                        for (int k = 0; k < dirs.Count; ++k)
                        {
                            if (pdfs[k] == 0)
                            {
                                continue;
                            }
                            Float pa = animated.lights[l].PDF_Li(ctx, dirs[k], false), ps = stat.lights[l].PDF_Li(ctx, dirs[k], false);
                            ++pdfChecks;
                            if (Math.Abs(pa - ps) > 2e-3 * ps)
                            {
                                ++pdfBad;
                            }
                        }
                        pstd.optional<LightBounds> ba = animated.lights[l].Bounds(), bs = stat.lights[l].Bounds();
                        if (ba.has_value() && bs.has_value())
                        {
                            Bounds3f x = ba.value().bounds, y = bs.value().bounds;
                            Float slack = 1e-4f;
                            if (y.pMin.x < x.pMin.x - slack || y.pMin.y < x.pMin.y - slack || y.pMin.z < x.pMin.z - slack ||
                                y.pMax.x > x.pMax.x + slack || y.pMax.y > x.pMax.y + slack || y.pMax.z > x.pMax.z + slack)
                            {
                                ++escapes;
                            }
                        }
                    }
                    if (Math.Max(totalA, totalS) > 0)
                    {
                        worstIrradiance = Math.Max(worstIrradiance, Math.Abs(totalA - totalS) / Math.Max(totalA, totalS));
                    }
                    // light sampling of the animated light against cosine sampled rays at the animated shape at this time
                    const int castSamples = 60000;
                    double sumCast = 0, sumCast2 = 0;
                    for (int k = 0; k < castSamples; ++k)
                    {
                        Vector3f w = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        Frame frame = Frame.FromZ(new Vector3f(n.x, n.y, n.z));
                        Vector3f wi = frame.FromLocal(w);
                        pstd.optional<ShapeIntersection> hit = animated.accel.Intersect(new Ray(p, wi, time), Infinity);
                        double value = 0;
                        if (hit.has_value())
                        {
                            value = Math.PI * hit.value().intr.Le(-wi, lambda)[0];
                        }
                        sumCast += value;
                        sumCast2 += value * value;
                    }
                    double meanCast = sumCast / castSamples, varianceCast = sumCast2 / castSamples - meanCast * meanCast;
                    double sumLight = 0, sumLight2 = 0;
                    const int lightSamples = 4096;
                    for (int k = 0; k < lightSamples; ++k)
                    {
                        Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                        double value = 0;
                        for (int l = 0; l < animated.lights.Count; ++l)
                        {
                            pstd.optional<LightLiSample> ls = animated.lights[l].SampleLi(new LightSampleContext(new Point3fi(p), n, n, time), u, lambda, false);
                            if (ls && ls.value().pdf > 0)
                            {
                                // visibility: the sampled point is the first hit of the ray (a closed shape hides its far side)
                                Ray shadow = new Ray(p, ls.value().wi, time);
                                pstd.optional<ShapeIntersection> first = animated.accel.Intersect(shadow, Infinity);
                                Float distance = Distance(p, ls.value().pLight.p());
                                if (first.has_value() && first.value().tHit < distance * (1 - 1e-3f))
                                {
                                    continue;
                                }
                                value += ls.value().L[0] * AbsDot(ls.value().wi, n) / ls.value().pdf;
                            }
                        }
                        sumLight += value;
                        sumLight2 += value * value;
                    }
                    double meanLight = sumLight / lightSamples, varianceLight = sumLight2 / lightSamples - meanLight * meanLight;
                    double standardError = Math.Sqrt(varianceCast / castSamples + varianceLight / lightSamples);
                    double tStatistic = standardError > 0 ? (meanLight - meanCast) / standardError : 0;
                    worstTStatistic = Math.Max(worstTStatistic, Math.Abs(tStatistic));
                }
            }
            double phiRel = Math.Abs(phiAnimated - meanPhiStatic) / Math.Max(1e-9, meanPhiStatic);
            Console.WriteLine("[MotionBlur] deforming light on " + c.name + (animation != null ? " + transform" : "") + ": irradiance against the static shape of the time " + worstIrradiance.ToString("E2") +
                              ", pdf checks " + pdfChecks + " bad " + pdfBad + ", power against the mean of 33 static powers " + phiRel.ToString("E2") + ", bounds escapes " + escapes +
                              ", light sampling against cosine sampled rays: worst |t| " + worstTStatistic.ToString("F2"));
            EXPECT_TRUE(worstIrradiance < 2e-3);
            EXPECT_EQ(0, pdfBad);
            EXPECT_TRUE(phiRel < 5e-3);
            EXPECT_EQ(0, escapes);
            EXPECT_TRUE(worstTStatistic < 4.5);
        }

        [TEST("MotionBlurDeform", "AreaLights")]
        public static void DeformAreaLights()
        {
            foreach (DeformCase c in DeformCases())
            {
                DeformLight(c, KeysPhi4, KeysTime4, null);
            }
        }

        [TEST("MotionBlurDeform", "AreaLightsWithTransformBlur")]
        public static void DeformAreaLightsWithTransform()
        {
            foreach (string name in new string[] { "trianglemesh", "bilinearmesh", "sphere", "cylinder", "disk" })
            {
                foreach (DeformCase c in DeformCases())
                {
                    if (c.name == name)
                    {
                        DeformLight(c, KeysPhi2, KeysTime2, Keys(2));
                    }
                }
            }
        }
    }
}
