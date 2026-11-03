// (not in pbrt-v4) sdf_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class SdfTests
    {
        private static SdfShape MakeShape(string expression, Transform renderFromObject, double extent = 2, bool reverse = false,
                                          SdfShapeOptions options = null)
        {
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = extent * Math.Sqrt(3);
            context.meshLoader = SdfMeshLoader.Load;
            SdfNode field = SdfParser.Parse(expression, context);
            return new SdfShape(renderFromObject, Inverse(renderFromObject), reverse, field,
                                new Point3f((Float)(-extent), (Float)(-extent), (Float)(-extent)),
                                new Point3f((Float)extent, (Float)extent, (Float)extent), options ?? new SdfShapeOptions());
        }

        private static Ray RandomRay(ref RNG rng, double distance, double aim)
        {
            Vector3f from = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
            Vector3f jitter = new Vector3f((Float)(aim * (rng.Uniform<Float>() - .5)), (Float)(aim * (rng.Uniform<Float>() - .5)),
                                           (Float)(aim * (rng.Uniform<Float>() - .5)));
            Point3f o = new Point3f(0, 0, 0) + (Float)distance * from;
            Vector3f d = Normalize(new Point3f(0, 0, 0) + jitter - o);
            return new Ray(o, d);
        }

        [TEST("Sdf", "SphereMatchesAnalytic")]
        public static void Sdf_SphereMatchesAnalytic()
        {
            Transform t = Translate(new Vector3f(0.3f, -0.2f, 0.5f)) * Scale(1.7f, 1.7f, 1.7f);
            Sphere analytic = new Sphere(t, Inverse(t), false, 1, -1, 1, 360);
            SdfShape sdf = MakeShape("sphere(1)", t, 2);
            RNG rng = new RNG(3);
            int hits = 0;
            for (int i = 0; i < 4000; ++i)
            {
                Ray ray = RandomRay(ref rng, 7, 2.6);
                pstd.optional<ShapeIntersection> a = analytic.Intersect(ray);
                pstd.optional<ShapeIntersection> b = sdf.Intersect(ray);
                EXPECT_EQ((bool)a, (bool)b);
                EXPECT_EQ(analytic.IntersectP(ray), sdf.IntersectP(ray));
                if (!a || !b)
                {
                    continue;
                }
                ++hits;
                // The float analytic quadric loses accuracy at grazing angles, where the hit moves fast along the ray
                double grazing = Math.Max(0.01, Math.Abs(Dot(b.value().intr.n, -ray.d)));
                EXPECT_NEAR(a.value().tHit, b.value().tHit, 4e-6 / grazing);
                EXPECT_NEAR(Distance(a.value().intr.p(), b.value().intr.p()), 0, 4e-6 / grazing);
                EXPECT_NEAR(Dot(a.value().intr.n, b.value().intr.n), 1, 1e-5);
                // Both normals face away from the center
                EXPECT_GT(Dot(b.value().intr.n, a.value().intr.p() - t.Apply(new Point3f(0, 0, 0))), 0);
            }
            EXPECT_GT(hits, 500);
        }

        [TEST("Sdf", "SphereDerivativesMatchAnalytic")]
        public static void Sdf_SphereDerivativesMatchAnalytic()
        {
            // For the unit sphere dn/du = dp/du and dn/dv = dp/dv, whatever the parameterization
            SdfShape sdf = MakeShape("sphere(1)", new Transform(), 2);
            RNG rng = new RNG(5);
            int checkedHits = 0;
            for (int i = 0; i < 200; ++i)
            {
                Ray ray = RandomRay(ref rng, 5, 1.2);
                pstd.optional<ShapeIntersection> b = sdf.Intersect(ray);
                if (!b)
                {
                    continue;
                }
                SurfaceInteraction si = b.value().intr;
                ++checkedHits;
                EXPECT_NEAR(Dot(si.dpdu, (Vector3f)si.n), 0, 1e-4 * Length(si.dpdu));
                EXPECT_NEAR(Dot(si.dpdv, (Vector3f)si.n), 0, 1e-4 * Length(si.dpdv));
                EXPECT_NEAR(Length((Vector3f)si.dndu - si.dpdu), 0, 2e-3 * (1 + Length(si.dpdu)));
                EXPECT_NEAR(Length((Vector3f)si.dndv - si.dpdv), 0, 2e-3 * (1 + Length(si.dpdv)));
            }
            EXPECT_GT(checkedHits, 50);
        }

        [TEST("Sdf", "ReverseOrientationFlipsNormal")]
        public static void Sdf_ReverseOrientationFlipsNormal()
        {
            SdfShape a = MakeShape("sphere(1)", new Transform(), 2, false);
            SdfShape b = MakeShape("sphere(1)", new Transform(), 2, true);
            Ray ray = new Ray(new Point3f(0, 0, 5), new Vector3f(0, 0, -1));
                        EXPECT_GT(Dot(a.Intersect(ray).value().intr.n, new Normal3f(0, 0, 1)), 0.99);
            EXPECT_LT(Dot(b.Intersect(ray).value().intr.n, new Normal3f(0, 0, 1)), -0.99);
        }

        [TEST("Sdf", "RaysFromTheSurfaceDoNotHitItself")]
        public static void Sdf_RaysFromTheSurfaceDoNotHitItself()
        {
            SdfShape sdf = MakeShape("smoothunion(sphere(1), translate(box(.6,.6,.6), 1, 0, 0), .3)", new Transform(), 3);
            RNG rng = new RNG(9);
            int spawned = 0;
            for (int i = 0; i < 3000; ++i)
            {
                Ray ray = RandomRay(ref rng, 6, 1.6);
                pstd.optional<ShapeIntersection> hit = sdf.Intersect(ray);
                if (!hit)
                {
                    continue;
                }
                SurfaceInteraction si = hit.value().intr;
                // Leave the surface outward (a reflection) and inward (a transmission) along the normal side
                Vector3f outward = Normalize((Vector3f)si.n + 0.3f * SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
                if (Dot(outward, (Vector3f)si.n) <= 0)
                {
                    continue;
                }
                ++spawned;
                Ray reflected = si.SpawnRay(outward);
                pstd.optional<ShapeIntersection> next = sdf.Intersect(reflected);
                // A smooth union is convex enough along most rays; whatever is hit must be well away from the origin
                if (next)
                {
                    EXPECT_GT(next.value().tHit, 1e-4);
                }
                Ray transmitted = si.SpawnRay(-outward);
                pstd.optional<ShapeIntersection> inside = sdf.Intersect(transmitted);
                if (inside)
                {
                    EXPECT_GT(inside.value().tHit, 1e-4);
                }
            }
            EXPECT_GT(spawned, 300);
        }

        // Sphere tracing must find every surface that a very fine march along the ray finds
        private static void CheckNoMissedHits(string expression, double extent, int rays, double relaxation = 1.2,
                                              double allowedMissFraction = 0.0)
        {
            SdfShapeOptions options = new SdfShapeOptions();
            options.relaxation = relaxation;
            SdfShape sdf = MakeShape(expression, new Transform(), extent, false, options);
            SdfNode field = sdf.Field();
            RNG rng = new RNG(17);
            int found = 0, missed = 0, late = 0;
            for (int i = 0; i < rays; ++i)
            {
                Ray ray = RandomRay(ref rng, extent * 2, extent * 1.2);
                // Fine march for the first sign change inside the box
                double tFine = -1;
                double previous = double.NaN;
                int steps = 6000;
                double length = extent * 4;
                for (int s = 0; s <= steps; ++s)
                {
                    double t = length * s / steps;
                    Point3f p = ray.o + (Float)t * ray.d;
                    if (Math.Abs(p.x) > extent || Math.Abs(p.y) > extent || Math.Abs(p.z) > extent)
                    {
                        previous = double.NaN;
                        continue;
                    }
                    double f = field.Evaluate(new SdfVector(p.x, p.y, p.z));
                    if (!double.IsNaN(previous) && (f < 0) != (previous < 0))
                    {
                        tFine = t;
                        break;
                    }
                    previous = f;
                }
                pstd.optional<ShapeIntersection> hit = sdf.Intersect(ray);
                if (tFine < 0)
                {
                    continue;
                }
                ++found;
                if (!hit)
                {
                    ++missed;
                    SdfShapeOptions many = new SdfShapeOptions();
                    many.maxSteps = 5000000;
                    many.relaxation = 1;
                    SdfShape slow = MakeShape(expression, new Transform(), extent, false, many);
                    pstd.optional<ShapeIntersection> slowHit = slow.Intersect(ray);
                    Console.WriteLine("  missed ray o=" + ray.o + " d=" + ray.d + " fine t=" + tFine + " with many steps: " +
                                      (slowHit ? slowHit.value().tHit.ToString() : "still a miss"));
                }
                else if (hit.value().tHit > tFine + length / steps)
                {
                    ++late;
                }
            }
            if (missed > 0 || late > 0)
            {
                Console.WriteLine("tracing: " + expression + ": " + found + " crossings, " + missed + " missed, " + late + " late");
            }
            EXPECT_LE(missed, allowedMissFraction * found);
            EXPECT_LE(late, allowedMissFraction * found);
            EXPECT_GT(found, rays / 20);
        }

        [TEST("Sdf", "TracingIsConservative")]
        public static void Sdf_TracingIsConservative()
        {
            // A ray that grazes a smooth surface converges slowly and can use up the step budget: allow a few in a thousand
            CheckNoMissedHits("sphere(1) | translate(box(.3,.5,.3), 1.2, 0, 0)", 2, 600, 1.2, 0.01);
            CheckNoMissedHits("smoothunion(torus(1,.25), sphere(.5), .4, \"cubic\")", 2, 600);
            CheckNoMissedHits("twist(box(.4,1,.4), 1.5)", 2, 600);
            CheckNoMissedHits("bend(box(1,.2,.3), 0.8)", 2, 600);
            CheckNoMissedHits("displace(sphere(1), .08, 3, 3, .5)", 2, 600);
            CheckNoMissedHits("scale(ellipsoid(1,.5,.3), 1.2, 1, 1)", 2, 600);
            CheckNoMissedHits("repeat(sphere(.25), 1, 1, 1, 1)", 2, 600);
            // The Mandelbulb's estimate is not Lipschitz bounded (IsRigorous is false), so a few misses are expected
            CheckNoMissedHits("mandelbulb(8, 8)", 1.5, 600, 1.2, 0.03);
            CheckNoMissedHits("gyroid(1.5, .08) & sphere(1.4)", 2, 600);
            CheckNoMissedHits("menger(3)", 1.5, 400);
            CheckNoMissedHits("mirror(translate(cone(.4, 1), .4, -.5, 0), \"x\")", 2, 400);
            CheckNoMissedHits("sphere(1.2) - cylinder(.4, 2)", 2, 400, 1.0);
        }

        [TEST("Sdf", "RelaxationAgreesWithPlainTracing")]
        public static void Sdf_RelaxationAgreesWithPlainTracing()
        {
            string expression = "smoothunion(torus(1,.3), translate(sphere(.6), .8, .4, 0), .25)";
            SdfShapeOptions relaxed = new SdfShapeOptions();
            relaxed.relaxation = 1.5;
            SdfShapeOptions plain = new SdfShapeOptions();
            plain.relaxation = 1;
            SdfShape a = MakeShape(expression, new Transform(), 2, false, relaxed);
            SdfShape b = MakeShape(expression, new Transform(), 2, false, plain);
            RNG rng = new RNG(21);
            for (int i = 0; i < 3000; ++i)
            {
                Ray ray = RandomRay(ref rng, 5, 2.4);
                pstd.optional<ShapeIntersection> ha = a.Intersect(ray), hb = b.Intersect(ray);
                EXPECT_EQ((bool)ha, (bool)hb);
                if (ha && hb)
                {
                    EXPECT_NEAR(ha.value().tHit, hb.value().tHit, 1e-6);
                }
            }
        }

        [TEST("Sdf", "ParserPrecedenceAndOperators")]
        public static void Sdf_ParserPrecedenceAndOperators()
        {
            SdfParseContext context = new SdfParseContext();
            // Inside the first sphere only, inside both, and inside the second only
            SdfNode difference = SdfParser.Parse("sphere(1) - translate(sphere(1), 1, 0, 0)", context);
            EXPECT_LT(difference.Evaluate(new SdfVector(-0.5, 0, 0)), 0);
            EXPECT_GT(difference.Evaluate(new SdfVector(0.5, 0, 0)), 0);
            SdfNode unionNode = SdfParser.Parse("sphere(1) | translate(sphere(1), 3, 0, 0)", context);
            EXPECT_LT(unionNode.Evaluate(new SdfVector(3, 0, 0)), 0);
            EXPECT_GT(unionNode.Evaluate(new SdfVector(1.5, 0, 0)), 0);
            // & binds tighter than | :  a | b & c = a | (b & c)
            SdfNode mixed = SdfParser.Parse("sphere(.2) | box(1,1,1) & translate(box(1,1,1), 5, 0, 0)", context);
            EXPECT_GT(mixed.Evaluate(new SdfVector(0.8, 0, 0)), 0);
            EXPECT_LT(mixed.Evaluate(new SdfVector(0, 0, 0)), 0);
            // Numbers: signs, exponents, pi
            SdfNode signs = SdfParser.Parse("translate(sphere(1e-1), -1.5, +.5, -2E0)", context);
            EXPECT_NEAR(signs.Evaluate(new SdfVector(-1.5, 0.5, -2)), -0.1, 1e-12);
            SdfNode angle = SdfParser.Parse("rotate(box(2, .1, .1), 0, 0, 90)", context);
            EXPECT_LT(angle.Evaluate(new SdfVector(0, 1.5, 0)), 0);
            EXPECT_GT(angle.Evaluate(new SdfVector(1.5, 0, 0)), 0);
        }

        [TEST("Sdf", "PrimitivesAreExactDistances")]
        public static void Sdf_PrimitivesAreExactDistances()
        {
            SdfParseContext context = new SdfParseContext();
            // Distance to a surface point displaced along the normal is the displacement
            EXPECT_NEAR(SdfParser.Parse("sphere(1)", context).Evaluate(new SdfVector(0, 3, 0)), 2, 1e-12);
            EXPECT_NEAR(SdfParser.Parse("box(1,2,3)", context).Evaluate(new SdfVector(4, 0, 0)), 3, 1e-12);
            EXPECT_NEAR(SdfParser.Parse("box(1,2,3)", context).Evaluate(new SdfVector(4, 6, 0)), Math.Sqrt(9 + 16), 1e-12);
            EXPECT_NEAR(SdfParser.Parse("torus(2, .5)", context).Evaluate(new SdfVector(3.5, 0, 0)), 1, 1e-12);
            EXPECT_NEAR(SdfParser.Parse("cylinder(1, 2)", context).Evaluate(new SdfVector(3, 5, 0)), Math.Sqrt(4 + 9), 1e-12);
            EXPECT_NEAR(SdfParser.Parse("capsule(0,0,0, 0,2,0, .5)", context).Evaluate(new SdfVector(2, 1, 0)), 1.5, 1e-12);
            EXPECT_NEAR(SdfParser.Parse("plane(0,1,0, 1)", context).Evaluate(new SdfVector(7, 4, 2)), 3, 1e-12);
            EXPECT_NEAR(SdfParser.Parse("octahedron(1)", context).Evaluate(new SdfVector(2, 0, 0)), 1, 1e-12);
            EXPECT_NEAR(SdfParser.Parse("cone(1, 2)", context).Evaluate(new SdfVector(3, 0, 0)), 2, 1e-12);
            EXPECT_NEAR(SdfParser.Parse("box(1,1,1,.25)", context).Evaluate(new SdfVector(2, 0, 0)), 1, 1e-12);
            // Smooth operators reduce to the sharp ones for a zero width
            SdfNode sharp = SdfParser.Parse("union(sphere(1), translate(sphere(1), 1.5, 0, 0))", context);
            SdfNode smooth0 = SdfParser.Parse("smoothunion(sphere(1), translate(sphere(1), 1.5, 0, 0), 0)", context);
            EXPECT_NEAR(sharp.Evaluate(new SdfVector(.7, .3, 0)), smooth0.Evaluate(new SdfVector(.7, .3, 0)), 1e-14);
            // The smooth union never exceeds the sharp one (it adds material)
            SdfNode smooth = SdfParser.Parse("smoothunion(sphere(1), translate(sphere(1), 1.5, 0, 0), .5)", context);
            EXPECT_LE(smooth.Evaluate(new SdfVector(.75, .8, 0)), sharp.Evaluate(new SdfVector(.75, .8, 0)));
        }

        [TEST("Sdf", "GradientsAreUnitForExactFields")]
        public static void Sdf_GradientsAreUnitForExactFields()
        {
            SdfParseContext context = new SdfParseContext();
            string[] expressions = { "sphere(1)", "box(1,.5,.8)", "torus(1,.3)", "capsule(0,0,0, 1,1,0, .3)", "octahedron(1)",
                                     "cone(.7, 1.5)", "cylinder(.5, 1)" };
            RNG rng = new RNG(31);
            foreach (string expression in expressions)
            {
                SdfNode node = SdfParser.Parse(expression, context);
                int unit = 0;
                for (int i = 0; i < 400; ++i)
                {
                    SdfVector p = new SdfVector(4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2);
                    if (Math.Abs(node.Evaluate(p)) < 0.02)
                    {
                        continue;
                    }
                    double length = node.Gradient(p, 1e-6).Length();
                    // Inside a box-like field the gradient is also a unit vector except on the medial axis; allow those few points
                    if (Math.Abs(length - 1) < 1e-4)
                    {
                        ++unit;
                    }
                    EXPECT_LE(length, node.Lipschitz() + 1e-4);
                }
                EXPECT_GT(unit, 300);
            }
        }

        // A UV sphere mesh of radius 1
        private static void SphereMesh(int rings, int segments, out SdfVector[] vertices, out int[] indices)
        {
            List<SdfVector> v = new List<SdfVector>();
            List<int> idx = new List<int>();
            v.Add(new SdfVector(0, 0, 1));
            for (int r = 1; r < rings; ++r)
            {
                double theta = Math.PI * r / rings;
                for (int s = 0; s < segments; ++s)
                {
                    double phi = 2 * Math.PI * s / segments;
                    v.Add(new SdfVector(Math.Sin(theta) * Math.Cos(phi), Math.Sin(theta) * Math.Sin(phi), Math.Cos(theta)));
                }
            }
            v.Add(new SdfVector(0, 0, -1));
            int south = v.Count - 1;
            for (int s = 0; s < segments; ++s)
            {
                int a = 1 + s, b = 1 + (s + 1) % segments;
                idx.Add(0); idx.Add(a); idx.Add(b);
            }
            for (int r = 0; r < rings - 2; ++r)
            {
                for (int s = 0; s < segments; ++s)
                {
                    int a = 1 + r * segments + s, b = 1 + r * segments + (s + 1) % segments;
                    int c = a + segments, d = b + segments;
                    idx.Add(a); idx.Add(c); idx.Add(b);
                    idx.Add(b); idx.Add(c); idx.Add(d);
                }
            }
            int last = 1 + (rings - 2) * segments;
            for (int s = 0; s < segments; ++s)
            {
                int a = last + s, b = last + (s + 1) % segments;
                idx.Add(a); idx.Add(south); idx.Add(b);
            }
            vertices = v.ToArray();
            indices = idx.ToArray();
        }

        [TEST("Sdf", "MeshDistanceIsExactAndSigned")]
        public static void Sdf_MeshDistanceIsExactAndSigned()
        {
            SphereMesh(48, 96, out SdfVector[] vertices, out int[] indices);
            SdfNode mesh = SdfMeshLoader.FromTriangles(vertices, indices, 0, 0);
            RNG rng = new RNG(41);
            for (int i = 0; i < 3000; ++i)
            {
                SdfVector p = new SdfVector(4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2);
                double expected = p.Length() - 1;
                double got = mesh.Evaluate(p);
                // Inscribed polygons lie inside the sphere by at most the sagitta (about 5e-4 here)
                EXPECT_NEAR(got, expected, 1.5e-3);
                if (Math.Abs(expected) > 2e-3)
                {
                    EXPECT_EQ(got < 0, expected < 0);
                }
            }
        }

        [TEST("Sdf", "BakedGridMatchesTheMeshDistance")]
        public static void Sdf_BakedGridMatchesTheMeshDistance()
        {
            SphereMesh(32, 64, out SdfVector[] vertices, out int[] indices);
            SdfNode exact = SdfMeshLoader.FromTriangles(vertices, indices, 0, 0);
            SdfNode cubic = SdfMeshLoader.FromTriangles(vertices, indices, 48, 0.2);
            RNG rng = new RNG(43);
            double worst = 0;
            for (int i = 0; i < 3000; ++i)
            {
                SdfVector p = new SdfVector(2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1, 2 * rng.Uniform<Float>() - 1);
                // The distance has a cone shaped singularity at the center of the sphere that no reconstruction follows
                if (p.Length() < 0.3)
                {
                    continue;
                }
                worst = Math.Max(worst, Math.Abs(cubic.Evaluate(p) - exact.Evaluate(p)));
            }
            // The grid spacing is 1.4 / 48 = 0.03 and the cubic reconstruction of a smooth distance is far better than that
            EXPECT_LT(worst, 2e-3);
        }

        [TEST("Sdf", "MeshShapeRendersLikeTheAnalyticSphere")]
        public static void Sdf_MeshShapeRendersLikeTheAnalyticSphere()
        {
            SphereMesh(48, 96, out SdfVector[] vertices, out int[] indices);
            SdfNode grid = SdfMeshLoader.FromTriangles(vertices, indices, 64, 0.1);
            SdfShape shape = new SdfShape(new Transform(), new Transform(), false, grid, new Point3f(-1.3f, -1.3f, -1.3f),
                                          new Point3f(1.3f, 1.3f, 1.3f), new SdfShapeOptions());
            Sphere analytic = new Sphere(new Transform(), new Transform(), false, 1, -1, 1, 360);
            RNG rng = new RNG(47);
            int hits = 0;
            for (int i = 0; i < 1500; ++i)
            {
                Ray ray = RandomRay(ref rng, 5, 1.8);
                pstd.optional<ShapeIntersection> a = analytic.Intersect(ray), b = shape.Intersect(ray);
                // The polygons of the mesh lie inside the sphere by up to 2e-3 and a grazing ray magnifies that, so only compare
                // the hits that are not near the silhouette
                if (a && b && AbsDot(a.value().intr.n, ray.d) > 0.3)
                {
                    ++hits;
                    EXPECT_NEAR(a.value().tHit, b.value().tHit, 7e-3);
                    EXPECT_GT(Dot(a.value().intr.n, b.value().intr.n), 0.98);
                }
            }
            EXPECT_GT(hits, 300);
        }

        [TEST("Sdf", "AreaOfPrimitives")]
        public static void Sdf_AreaOfPrimitives()
        {
            SdfShapeOptions options = new SdfShapeOptions();
            options.tessellation = 48;
            options.areaSubdivisions = 3;
            double pi = Math.PI;
            EXPECT_NEAR(MakeShape("sphere(1)", new Transform(), 1.5, false, options).Area() / (4 * pi), 1, 2e-3);
            EXPECT_NEAR(MakeShape("torus(1,.35)", new Transform(), 1.8, false, options).Area() / (4 * pi * pi * 0.35), 1, 3e-3);
            EXPECT_NEAR(MakeShape("box(.7,.5,.9)", new Transform(), 1.5, false, options).Area() / (8 * (.7 * .5 + .5 * .9 + .7 * .9)), 1, 1e-2);
            // A transform that scales: the area is measured in the render space
            Transform scaled = Scale(2, 2, 2);
            EXPECT_NEAR(MakeShape("sphere(1)", scaled, 1.5, false, options).Area() / (16 * pi), 1, 2e-3);
        }

        [TEST("Sdf", "SamplesAreOnTheSurfaceAndUniform")]
        public static void Sdf_SamplesAreOnTheSurfaceAndUniform()
        {
            SdfShape sdf = MakeShape("sphere(1)", new Transform(), 1.5);
            RNG rng = new RNG(51);
            int n = 40000;
            int[] bins = new int[10];
            double meanX = 0;
            for (int i = 0; i < n; ++i)
            {
                pstd.optional<ShapeSample> s = sdf.Sample(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Point3f p = s.value().intr.p();
                EXPECT_NEAR(Length((Vector3f)p), 1, 1e-5);
                // The normal is the outward radial direction
                EXPECT_GT(Dot(s.value().intr.n, new Normal3f(p.x, p.y, p.z)), 0.9999);
                EXPECT_NEAR(s.value().pdf * sdf.Area(), 1, 1e-6);
                // On a sphere the height is uniform (Archimedes)
                bins[std.min(9, (int)((p.z + 1) * 5))]++;
                meanX += p.x;
            }
            for (int b = 0; b < 10; ++b)
            {
                EXPECT_NEAR((double)bins[b] / n, 0.1, 0.012);
            }
            EXPECT_NEAR(meanX / n, 0, 0.012);
        }

        [TEST("Sdf", "SamplingMatchesTheAnalyticSphereLight")]
        public static void Sdf_SamplingMatchesTheAnalyticSphereLight()
        {
            // The irradiance at a point facing a unit sphere emitter of radiance 1 from distance d is pi / d^2 (a point on the axis)
            SdfShape sdf = MakeShape("sphere(1)", new Transform(), 1.5);
            Sphere analytic = new Sphere(new Transform(), new Transform(), false, 1, -1, 1, 360);
            Point3f receiver = new Point3f(0, 0, 3);
            Normal3f receiverNormal = new Normal3f(0, 0, -1);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(receiver), receiverNormal, receiverNormal, 0);
            double expected = Math.PI / 9.0;
            RNG rng = new RNG(53);
            double sumSdf = 0, sumAnalytic = 0;
            int n = 200000;
            for (int i = 0; i < n; ++i)
            {
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<ShapeSample> s = sdf.Sample(ctx, u);
                if (s)
                {
                    Vector3f wi = Normalize(s.value().intr.p() - receiver);
                    // Light leaves only from the outside of the surface, and a sample behind the sphere is hidden by it
                    pstd.optional<ShapeIntersection> first = sdf.Intersect(ctx.SpawnRay(wi));
                    bool visible = first && Distance(first.value().intr.p(), s.value().intr.p()) < 1e-4;
                    if (visible && Dot(s.value().intr.n, -wi) > 0)
                    {
                        sumSdf += AbsDot(receiverNormal, wi) / s.value().pdf;
                    }
                }
                pstd.optional<ShapeSample> a = analytic.Sample(ctx, u);
                if (a)
                {
                    Vector3f wi = Normalize(a.value().intr.p() - receiver);
                    sumAnalytic += AbsDot(receiverNormal, wi) / a.value().pdf;
                }
            }
            EXPECT_NEAR(sumAnalytic / n / expected, 1, 0.01);
            EXPECT_NEAR(sumSdf / n / expected, 1, 0.012);
        }

        [TEST("Sdf", "PdfOfTheHitMatchesTheSampleDensity")]
        public static void Sdf_PdfOfTheHitMatchesTheSampleDensity()
        {
            SdfShape sdf = MakeShape("sphere(1)", new Transform(), 1.5);
            Point3f receiver = new Point3f(0.5f, -0.3f, 3);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(receiver), new Normal3f(0, 0, -1), new Normal3f(0, 0, -1), 0);
            RNG rng = new RNG(55);
            int compared = 0;
            for (int i = 0; i < 1000; ++i)
            {
                pstd.optional<ShapeSample> s = sdf.Sample(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (!s)
                {
                    continue;
                }
                Vector3f wi = Normalize(s.value().intr.p() - receiver);
                pstd.optional<ShapeIntersection> first = sdf.Intersect(ctx.SpawnRay(wi));
                if (!first || Distance(first.value().intr.p(), s.value().intr.p()) > 1e-4)
                {
                    continue;
                }
                // Away from grazing angles, where the density per solid angle is sensitive to the hit position
                if (AbsDot(s.value().intr.n, -wi) < 0.2)
                {
                    continue;
                }
                ++compared;
                EXPECT_NEAR(sdf.PDF(ctx, wi) / s.value().pdf, 1, 1e-3);
            }
            EXPECT_GT(compared, 100);
        }

        [TEST("Sdf", "TextureBoundsContainTheValues")]
        public static void Sdf_TextureBoundsContainTheValues()
        {
            Transform t = Translate(new Vector3f(0.1f, 0.2f, -0.1f)) * Scale(1.3f, 1.3f, 1.3f);
            string[] expressions = { "sphere(1)", "smoothunion(torus(1,.25), sphere(.6), .3)", "displace(sphere(.9), .1, 4, 3)", "mandelbulb(8, 8)" };
            SdfDensityMode[] modes = { SdfDensityMode.Solid, SdfDensityMode.Exponential, SdfDensityMode.Shell, SdfDensityMode.Distance };
            SdfParseContext context = new SdfParseContext();
            RNG rng = new RNG(61);
            foreach (string expression in expressions)
            {
                foreach (SdfDensityMode mode in modes)
                {
                    SdfFloatTexture texture = new SdfFloatTexture(t, SdfParser.Parse(expression, context), mode, 0.05, 0.2, 0.2, 0.4);
                    int violations = 0;
                    for (int i = 0; i < 300; ++i)
                    {
                        Point3f c = new Point3f(4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2, 4 * rng.Uniform<Float>() - 2);
                        Float size = 0.05f + 1.0f * rng.Uniform<Float>();
                        Bounds3f region = new Bounds3f(c, c + new Vector3f(size, size * 0.7f, size * 1.3f));
                        texture.RegionBounds(region, out Float lo, out Float hi);
                        for (int k = 0; k < 20; ++k)
                        {
                            Point3f p = region.Lerp(new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>()));
                            TextureEvalContext ctx = new TextureEvalContext(p, default, default, default, default, 0, 0, 0, 0, 0);
                            Float value = texture.Evaluate(ctx);
                            if (value < lo - 1e-5 || value > hi + 1e-5)
                            {
                                ++violations;
                            }
                        }
                    }
                    if (violations > 0)
                    {
                        Console.WriteLine("bounds violated: " + expression + " mode " + mode + ": " + violations);
                    }
                    EXPECT_EQ(0, violations);
                }
            }
        }

        [TEST("Sdf", "TextureBoundsAreTightFarFromTheSurface")]
        public static void Sdf_TextureBoundsAreTightFarFromTheSurface()
        {
            SdfParseContext context = new SdfParseContext();
            SdfFloatTexture solid = new SdfFloatTexture(new Transform(), SdfParser.Parse("sphere(1)", context), SdfDensityMode.Solid, 0.02, 0.1, 0.1, 0.4);
            solid.RegionBounds(new Bounds3f(new Point3f(3, 3, 3), new Point3f(3.2f, 3.2f, 3.2f)), out Float lo, out Float hi);
            EXPECT_EQ(hi, 1e-6f);
            solid.RegionBounds(new Bounds3f(new Point3f(-0.1f, -0.1f, -0.1f), new Point3f(0.1f, 0.1f, 0.1f)), out lo, out hi);
            EXPECT_GE(lo, 1 - 1e-5f);
        }

        [TEST("Sdf", "ErrorsAreReported")]
        public static void Sdf_ErrorsAreReported()
        {
            SdfParseContext context = new SdfParseContext();
            EXPECT_DEATH(() => SdfParser.Parse("sphere(1", context), "expected");
            EXPECT_DEATH(() => SdfParser.Parse("sfear(1)", context), "unknown function");
            EXPECT_DEATH(() => SdfParser.Parse("box(1, 2)", context), "takes");
            EXPECT_DEATH(() => SdfParser.Parse("union(sphere(1), 3)", context), "must be a shape");
        }

        [TEST("Sdf", "CompiledEvaluatorMatchesTheInterpreter")]
        public static void CompiledEvaluatorMatchesTheInterpreter()
        {
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = 4;
            context.meshLoader = SdfMeshLoader.Load;
            string[] expressions = new string[]
            {
                "sphere(1)", "box(1, .5, .7, .1)", "torus(1, .3)", "cylinder(.5, 1)", "capsule(0,0,0, 0,1,.5, .3)", "plane(0,1,0, .2)",
                "cone(1, 2)", "ellipsoid(1, .5, .7)", "octahedron(1)", "gyroid(3, .1)",
                "sphere(1) | translate(box(.5,.5,.5), 1, 0, 0)",
                "sphere(1) & translate(box(.5,.5,.5), .5, 0, 0)",
                "sphere(1) - translate(box(.5,.5,.5), .5, 0, 0)",
                "smoothunion(sphere(1), translate(sphere(.7), 1, 0, 0), .4)",
                "smoothintersect(sphere(1), translate(sphere(.7), 1, 0, 0), .4)",
                "smoothsubtract(sphere(1), translate(sphere(.7), 1, 0, 0), .4)",
                "mix(sphere(1), box(1,1,1), .3)", "offset(shell(sphere(1), .2), .05)",
                "rotate(box(1, .2, .3), 20, 30, 40)", "rotateaxis(torus(1, .3), 1, 1, 0, 33)", "scale(sphere(1), 2, 1, 1.5)",
                "twist(box(.3, 1, .3), 1.2)", "bend(box(1, .2, .2), .8)", "repeat(sphere(.3), 1, 1, 0, 2)", "mirror(translate(sphere(.4), 1, 0, 0), \"x\")",
                "elongate(sphere(.4), .5, .2, .1)", "mandelbulb(8, 6)", "menger(3)", "displace(sphere(1), .1, 3, 2)",
                "smoothunion(twist(box(.3, 1, .3), 1.2), mandelbulb(8, 6), .2)"
            };
            RNG rng = new RNG(5);
            for (int e = 0; e < expressions.Length; ++e)
            {
                SdfNode interpreted = SdfParser.Parse(expressions[e], context);
                SdfNode compiled = SdfCompiledNode.Compile(interpreted);
                for (int i = 0; i < 2000; ++i)
                {
                    SdfVector p = new SdfVector(6 * (rng.Uniform<Float>() - .5), 6 * (rng.Uniform<Float>() - .5), 6 * (rng.Uniform<Float>() - .5));
                    double expected = interpreted.Evaluate(p);
                    EXPECT_NEAR(compiled.Evaluate(p), expected, 1e-12 * (1 + Math.Abs(expected)));
                }
                EXPECT_EQ(compiled.Lipschitz(), interpreted.Lipschitz());
                EXPECT_EQ(compiled.IsRigorous(), interpreted.IsRigorous());
            }
        }

        [TEST("Sdf", "CompiledEvaluatorBenchmark")]
        public static void CompiledEvaluatorBenchmark()
        {
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = 4;
            string[] expressions = new string[]
            {
                "sphere(1)",
                "smoothunion(smoothunion(sphere(1), translate(box(.5,.5,.5,.1), 1, 0, 0), .3), rotate(torus(1, .2), 30, 0, 20), .2)",
                "twist(smoothunion(box(.3, 1, .3), translate(sphere(.5), 0, .8, 0), .2), 1.5)"
            };
            for (int e = 0; e < expressions.Length; ++e)
            {
                SdfNode interpreted = SdfParser.Parse(expressions[e], context);
                SdfNode compiled = SdfCompiledNode.Compile(interpreted);
                SdfVector[] points = new SdfVector[1024];
                for (int i = 0; i < points.Length; ++i)
                {
                    double t = i * 0.37;
                    points[i] = new SdfVector(Math.Sin(t) * 2, Math.Cos(t * 1.3) * 2, Math.Sin(t * .7) * 2);
                }
                for (int pass = 0; pass < 4; ++pass)
                {
                    SdfNode node = (pass & 1) == 0 ? interpreted : compiled;
                    double sum = 0;
                    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < 8000000; ++i)
                    {
                        sum += node.Evaluate(points[i & 1023]);
                    }
                    watch.Stop();
                    if (pass >= 2)
                    {
                        Console.WriteLine("BENCH {0} {1}: {2:F1} ns/eval (checksum {3:F3})", expressions[e].Substring(0, Math.Min(30, expressions[e].Length)),
                                          pass == 2 ? "interpreted" : "compiled", watch.Elapsed.TotalMilliseconds * 1e6 / 8000000, sum);
                    }
                }
            }
        }
    }
}
