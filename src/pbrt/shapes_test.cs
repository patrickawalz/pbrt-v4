// shapes_test.cpp
//
// Differences from the C++ test: the Triangle.Watertight test is under "#if 0" in C++ (it fails
// on github's servers there) and is not ported either. TriangleMesh's std::vector arguments are
// pstd.vector<>; Shape handles are the Shape class hierarchy (CastOrNullptr<Triangle> becomes
// "as Triangle").

using System;
using testing;

namespace pbrt
{
    public static unsafe class ShapesTests
    {
        private static Float pExp(ref RNG rng, Float exp = 8.0f)
        {
            Float logu = Lerp(rng.Uniform<Float>(), -exp, exp);
            return (Float)std.pow(10, logu);
        }

        private static Float pUnif(ref RNG rng, Float range = 10.0f)
        {
            return Lerp(rng.Uniform<Float>(), -range, range);
        }

        private static Triangle GetRandomTriangle(Func<Float> value)
        {
            // Triangle vertices
            Point3f[] v = new Point3f[3];
            for (int j = 0; j < 3; ++j)
                for (int k = 0; k < 3; ++k)
                    v[j][k] = value();

            if (LengthSquared(Cross(v[1] - v[0], v[2] - v[0])) < 1e-20)
                // Don't into trouble with ~degenerate triangles.
                return null;

            // Create the corresponding Triangle.
            Transform identity = new Transform();
            // Leaks...
            TriangleMesh mesh = new TriangleMesh(identity, false, new pstd.vector<int>(new int[] { 0, 1, 2 }),
                                                 new pstd.vector<Point3f>(v), new pstd.vector<Vector3f>(),
                                                 new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(),
                                                 new pstd.vector<int>(), new Allocator());
            pstd.vector<Shape> triVec = Triangle.CreateTriangles(mesh, new Allocator());
            EXPECT_EQ(1, triVec.size());
            return triVec[0] as Triangle;
        }

        [TEST("Triangle", "Reintersect")]
        public static void Triangle_Reintersect()
        {
            for (int i = 0; i < 1000; ++i)
            {
                RNG rng = new RNG((uint64_t)i);
                Triangle tri = GetRandomTriangle(() => pExp(ref rng));
                if (tri == null)
                    continue;

                // Sample a point on the triangle surface to shoot the ray toward.
                Point2f u = new Point2f();
                u[0] = rng.Uniform<Float>();
                u[1] = rng.Uniform<Float>();
                pstd.optional<ShapeSample> ts = tri.Sample(u);
                ASSERT_TRUE(ts.has_value());

                // Choose a ray origin.
                Point3f o = new Point3f();
                for (int j = 0; j < 3; ++j)
                    o[j] = pExp(ref rng);

                // Intersect the ray with the triangle.
                Ray r = new Ray(o, ts.value().intr.p() - o);
                pstd.optional<ShapeIntersection> si = tri.Intersect(r);
                if (!si.has_value())
                    // We should almost always find an intersection, but rarely
                    // miss, due to round-off error. Just do another go-around in
                    // this case.
                    continue;
                SurfaceInteraction isect = si.value().intr;

                // Now trace a bunch of rays leaving the intersection point.
                for (int j = 0; j < 1000; ++j)
                {
                    // Random direction leaving the intersection point.
                    Point2f u2 = new Point2f();
                    u2[0] = rng.Uniform<Float>();
                    u2[1] = rng.Uniform<Float>();
                    Vector3f w = SampleUniformSphere(u2);
                    Ray rOut = isect.SpawnRay(w);
                    EXPECT_FALSE(tri.IntersectP(rOut));
                    EXPECT_FALSE(tri.Intersect(rOut).has_value());

                    // Choose a random point to trace rays to.
                    Point3f p2 = new Point3f();
                    for (int k = 0; k < 3; ++k)
                        p2[k] = pExp(ref rng);
                    rOut = isect.SpawnRayTo(p2);

                    EXPECT_FALSE(tri.IntersectP(rOut, 1));
                    EXPECT_FALSE(tri.Intersect(rOut, 1).has_value());
                }
            }
        }

        // Checks the closed-form solid angle computation for triangles against a
        // Monte Carlo estimate of it.
        [TEST("Triangle", "SolidAngle")]
        public static void Triangle_SolidAngle()
        {
            for (int i = 0; i < 50; ++i)
            {
                const Float range = 10;
                RNG rng = new RNG((uint64_t)(100 + i));  // Use different triangles than the Triangle/Sample test.
                Triangle tri = GetRandomTriangle(() => pUnif(ref rng, range));
                if (tri == null)
                    continue;

                // Ensure that the reference point isn't too close to the
                // triangle's surface (which makes the Monte Carlo stuff have more
                // variance, thus requiring more samples).
                Float pcx = pUnif(ref rng, range), pcy = pUnif(ref rng, range), pcz = pUnif(ref rng, range);
                Point3f pc = new Point3f(pcx, pcy, pcz);
                // C++17: the right-hand side of an assignment is sequenced before the left.
                Float side = rng.Uniform<Float>() > .5 ? (-range - 3) : (range + 3);
                pc[(int)(rng.Uniform<uint32_t>() % 3)] = side;

                // Compute a reference value using Triangle::Sample()
                const int count = 64 * 1024;
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(pc), new Normal3f(), new Normal3f(), 0);
                double triSampleEstimate = 0;
                for (int j = 0; j < count; ++j)
                {
                    Point2f u = new Point2f(RadicalInverse(0, (uint64_t)j), RadicalInverse(1, (uint64_t)j));
                    pstd.optional<ShapeSample> ss = tri.Sample(ctx, u);
                    ASSERT_TRUE(ss.has_value());
                    EXPECT_GT(ss.value().pdf, 0);
                    triSampleEstimate += 1.0 / (count * ss.value().pdf);
                }

                Func<Float, Float, Float> error = (Float a, Float b) =>
                {
                    if (std.abs(a) < 1e-4 || std.abs(b) < 1e-4)
                        return std.abs(a - b);
                    return std.abs((a - b) / b);
                };

                // Now compute the subtended solid angle of the triangle in closed
                // form.
                Float sphericalArea = tri.SolidAngle(pc);

                EXPECT_LT(error(sphericalArea, (Float)triSampleEstimate), .015,
                          "spherical area: " + sphericalArea + ", tri sampling: " + triSampleEstimate +
                          ", pc = " + pc + ", tri index " + i);
            }
        }

        // Check for incorrect self-intersection: assumes that the shape is convex,
        // such that if the dot product of an outgoing ray and the surface normal
        // at a point is positive, then a ray leaving that point in that direction
        // should never intersect the shape.
        private static int TestReintersectConvex(Shape shape, ref RNG rng)
        {
            // Ray origin
            Point3f o = new Point3f();
            for (int c = 0; c < 3; ++c)
                o[c] = pExp(ref rng);

            // Destination: a random point in the shape's bounding box.
            Bounds3f bbox = shape.Bounds();
            Point3f t = new Point3f();
            for (int c = 0; c < 3; ++c)
                t[c] = rng.Uniform<Float>();
            Point3f p2 = bbox.Lerp(t);

            // Ray to intersect with the shape.
            Ray r = new Ray(o, p2 - o);
            if (rng.Uniform<Float>() < .5)
                r.d = Normalize(r.d);

            // We should usually (but not always) find an intersection.
            pstd.optional<ShapeIntersection> si = shape.Intersect(r);
            if (!si.has_value())
                return 0;
            SurfaceInteraction isect = si.value().intr;

            // Now trace a bunch of rays leaving the intersection point.
            int n = 0;
            for (int j = 0; j < 1000; ++j)
            {
                // Random direction leaving the intersection point.
                Point2f u = new Point2f();
                u[0] = rng.Uniform<Float>();
                u[1] = rng.Uniform<Float>();
                Vector3f w = SampleUniformSphere(u);
                // Make sure it's in the same hemisphere as the surface normal.
                w = FaceForward(w, isect.n);
                Ray rOut = isect.SpawnRay(w);

                if (shape.IntersectP(rOut))
                    ++n;
                if (shape.Intersect(rOut).has_value())
                    ++n;

                // Choose a random point to trace rays to.
                Point3f pp = new Point3f();
                for (int c = 0; c < 3; ++c)
                    pp[c] = pExp(ref rng);
                // Make sure that the point we're tracing rays toward is in the
                // hemisphere about the intersection point's surface normal.
                w = pp - isect.p();
                w = FaceForward(w, isect.n);
                pp = isect.p() + w;
                rOut = isect.SpawnRayTo(pp);

                if (shape.IntersectP(rOut, 1))
                    ++n;
                if (shape.Intersect(rOut, 1).has_value())
                    ++n;
            }

            return n;
        }

        private static Transform randomTransform(ref RNG rngRef)
        {
            RNG rng = rngRef;
            Transform t = new Transform();
            Func<double> rr = () => -10.0 + 20.0 * rng.Uniform<Float>();
            Func<Float> rt = () =>
            {
                Float f = pExp(ref rng);
                return rng.Uniform<Float>() > .5 ? f : -f;
            };

            for (int i = 0; i < 1; ++i)
            {
                Float s0 = pExp(ref rng, 4), s1 = pExp(ref rng, 4), s2 = pExp(ref rng, 4);
                t = t * Scale(s0, s1, s2);
                Float tx = rt(), ty = rt(), tz = rt();
                t = t * Translate(new Vector3f(tx, ty, tz));
                Float ua = rng.Uniform<Float>(), ub = rng.Uniform<Float>();
                Point2f u = new Point2f(ua, ub);
                t = t * Rotate((Float)(rr() * 20.0), SampleUniformSphere(u));
            }
            rngRef = rng;
            return t;
        }

        private const int nReintersect = 1000;

        [TEST("FullSphere", "Reintersect")]
        public static void FullSphere_Reintersect()
        {
            ParallelFor(0, nReintersect, (long i) =>
            {
                RNG rng = new RNG((uint64_t)i);
                Transform identity = new Transform();
                Float radius = pExp(ref rng, 4);
                Float zMin = -radius;
                Float zMax = radius;
                Float phiMax = 360;
                Sphere sphere = new Sphere(identity, identity, false, radius, zMin, zMax, phiMax);
                EXPECT_EQ(0, TestReintersectConvex(sphere, ref rng), i.ToString());

                Transform tr = randomTransform(ref rng);
                Transform ti = Inverse(tr);
                Sphere tsphere = new Sphere(tr, ti, false, radius, zMin, zMax, phiMax);
                EXPECT_EQ(0, TestReintersectConvex(tsphere, ref rng), i + ", tr " + tr);
            });
        }

        [TEST("ParialSphere", "Normal")]
        public static void ParialSphere_Normal()
        {
            for (int i = 0; i < 100; ++i)
            {
                RNG rng = new RNG((uint64_t)i);
                Transform identity = new Transform();
                Float radius = pExp(ref rng, 4);
                Float zMin = rng.Uniform<Float>() < 0.5 ? -radius : Lerp(rng.Uniform<Float>(), -radius, radius);
                Float zMax = rng.Uniform<Float>() < 0.5 ? radius : Lerp(rng.Uniform<Float>(), -radius, radius);
                Float phiMax = rng.Uniform<Float>() < 0.5 ? 360.0f : rng.Uniform<Float>() * 360.0f;
                Sphere sphere = new Sphere(identity, identity, false, radius, zMin, zMax, phiMax);

                // Ray origin
                Point3f o = new Point3f();
                for (int c = 0; c < 3; ++c)
                    o[c] = pExp(ref rng);

                // Destination: a random point in the shape's bounding box.
                Bounds3f bbox = sphere.Bounds();
                Point3f t = new Point3f();
                for (int c = 0; c < 3; ++c)
                    t[c] = rng.Uniform<Float>();
                Point3f p2 = bbox.Lerp(t);

                // Ray to intersect with the shape.
                Ray r = new Ray(o, p2 - o);
                if (rng.Uniform<Float>() < .5)
                    r.d = Normalize(r.d);

                // We should usually (but not always) find an intersection.
                pstd.optional<ShapeIntersection> si = sphere.Intersect(r, Infinity);
                if (!si.has_value())
                    continue;

                Float dot = Dot(Normalize(si.value().intr.n), Normalize((Vector3f)si.value().intr.p()));
                EXPECT_FLOAT_EQ(1.0, dot);
            }
        }

        [TEST("PartialSphere", "Reintersect")]
        public static void PartialSphere_Reintersect()
        {
            ParallelFor(0, nReintersect, (long i) =>
            {
                RNG rng = new RNG((uint64_t)i);
                Transform identity = new Transform();
                Float radius = pExp(ref rng, 4);
                Float zMin = rng.Uniform<Float>() < 0.5 ? -radius : Lerp(rng.Uniform<Float>(), -radius, radius);
                Float zMax = rng.Uniform<Float>() < 0.5 ? radius : Lerp(rng.Uniform<Float>(), -radius, radius);
                Float phiMax = rng.Uniform<Float>() < 0.5 ? 360.0f : rng.Uniform<Float>() * 360.0f;
                Sphere sphere = new Sphere(identity, identity, false, radius, zMin, zMax, phiMax);

                EXPECT_EQ(0, TestReintersectConvex(sphere, ref rng), i.ToString());
            });
        }

        [TEST("Cylinder", "Reintersect")]
        public static void Cylinder_Reintersect()
        {
            ParallelFor(0, nReintersect, (long i) =>
            {
                RNG rng = new RNG((uint64_t)i);
                Transform identity = new Transform();
                Float radius = pExp(ref rng, 4);
                Float zMinA = pExp(ref rng, 4);
                Float zMin = zMinA * (rng.Uniform<Float>() < 0.5 ? -1 : 1);
                Float zMaxA = pExp(ref rng, 4);
                Float zMax = zMaxA * (rng.Uniform<Float>() < 0.5 ? -1 : 1);
                Float phiMax = rng.Uniform<Float>() < 0.5 ? 360.0f : rng.Uniform<Float>() * 360.0f;
                Cylinder cyl = new Cylinder(identity, identity, false, radius, zMin, zMax, phiMax);

                EXPECT_EQ(0, TestReintersectConvex(cyl, ref rng), i.ToString());

                Transform tr = randomTransform(ref rng);
                Transform ti = Inverse(tr);
                Cylinder tcyl = new Cylinder(tr, ti, false, radius, zMin, zMax, phiMax);
                EXPECT_EQ(0, TestReintersectConvex(tcyl, ref rng), i.ToString());
            });
        }

        [TEST("Triangle", "BadCases")]
        public static void Triangle_BadCases()
        {
            Transform identity = new Transform();
            pstd.vector<int> indices = new pstd.vector<int>(new int[] { 0, 1, 2 });
            pstd.vector<Point3f> p = new pstd.vector<Point3f>(new Point3f[] {
                new Point3f(-1113.45459f, -79.049614f, -56.2431908f),
                new Point3f(-1113.45459f, -87.0922699f, -56.2431908f),
                new Point3f(-1113.45459f, -79.2090149f, -56.2431908f) });
            TriangleMesh mesh = new TriangleMesh(identity, false, indices, p, new pstd.vector<Vector3f>(),
                                                 new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(),
                                                 new pstd.vector<int>(), new Allocator());
            pstd.vector<Shape> tris = Triangle.CreateTriangles(mesh, new Allocator());
            ASSERT_EQ(1, tris.size());

            Ray ray = new Ray(new Point3f(-1081.47925f, 99.9999542f, 87.7701111f),
                              new Vector3f(-32.1072998f, -183.355865f, -144.607635f), 0.9999f);

            EXPECT_FALSE(tris[0].Intersect(ray).has_value());
        }

        [TEST("BilinearPatch", "Offset")]
        public static void BilinearPatch_Offset()
        {
            RNG rng = new RNG();
            for (int i = 0; i < 100; ++i)
            {
                Float height = -400 + 800 * rng.Uniform<Float>();
                Float x0 = -400 + 800 * rng.Uniform<Float>();
                Float x1 = x0 + 400 * rng.Uniform<Float>();
                Float z0 = -400 + 800 * rng.Uniform<Float>();
                Float z1 = x0 + 400 * rng.Uniform<Float>();
                Point3f[] p = { new Point3f(x0, height, z0), new Point3f(x1, height, z0),
                                new Point3f(x0, height, x1), new Point3f(x1, height, z1) };

                BilinearPatchMesh mesh = new BilinearPatchMesh(new Transform(), false,
                                                               new pstd.vector<int>(new int[] { 0, 1, 2, 3 }),
                                                               new pstd.vector<Point3f>(p),
                                                               new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(),
                                                               new pstd.vector<int>(), null, new Allocator());
                for (int j = 0; j < 100; ++j)
                {
                    // gcc evaluates constructor arguments right to left.
                    Float oz = -20 + 40 * rng.Uniform<Float>();
                    Float oy = -20 + 40 * rng.Uniform<Float>();
                    Float ox = -20 + 40 * rng.Uniform<Float>();
                    Point3f o = new Point3f(ox, oy, oz);

                    Float uvy = rng.Uniform<Float>(), uvx = rng.Uniform<Float>();
                    Point2f uv = new Point2f(uvx, uvy);
                    Point3f pPatch = ((1 - uv[0]) * (1 - uv[1]) * p[0] +
                                      uv[0] * (1 - uv[1]) * p[1] +
                                      (1 - uv[0]) * uv[1] * p[2] +
                                      uv[0] * uv[1] * p[3]);
                    Ray r = new Ray(o, pPatch - o);
                    pstd.optional<BilinearIntersection> isect = IntersectBilinearPatch(r, Infinity, p[0], p[1], p[2], p[3]);
                    if (!isect.has_value())
                        // Hmm...
                        continue;

                    SurfaceInteraction intr = BilinearPatch.InteractionFromIntersection(mesh, 0, isect.value().uv, 0.0f, -r.d);

                    Ray spawned = intr.SpawnRay(r.d);
                    pstd.optional<BilinearIntersection> spawnedIsect = IntersectBilinearPatch(spawned, Infinity,
                                                                                              p[0], p[1], p[2], p[3]);
                    EXPECT_FALSE(spawnedIsect.has_value());
                }
            }
        }
    }
}
