// vecmath_test.cpp
//
// Differences from the C++ test: Vector3<double> / Vector3<long double> references are computed with
// plain doubles (the port has no generic Vector3<T>); long double becomes double.

using System;
using testing;

namespace pbrt
{
    public static unsafe class VecmathTests
    {
        [TEST("Vector2", "Basics")]
        public static void Vector2_Basics()
        {
            Vector2f vf = new Vector2f(-1, 10);
            EXPECT_EQ(vf, (Vector2f)new Vector2i(-1, 10));
            EXPECT_NE(vf, new Vector2f(-1, 100));
            EXPECT_EQ(new Vector2f(-2, 20), vf + vf);
            EXPECT_EQ(new Vector2f(0, 0), vf - vf);
            EXPECT_EQ(new Vector2f(-2, 20), vf * 2);
            EXPECT_EQ(new Vector2f(-2, 20), 2 * vf);
            EXPECT_EQ(new Vector2f(-0.5f, 5), vf / 2);
            EXPECT_EQ(new Vector2f(1, 10), Abs(vf));
            EXPECT_EQ(vf, Ceil(new Vector2f(-1.5f, 9.9f)));
            EXPECT_EQ(vf, Floor(new Vector2f(-.5f, 10.01f)));
            EXPECT_EQ(new Vector2f(-20, 10), Min(vf, new Vector2f(-20, 20)));
            EXPECT_EQ(new Vector2f(-1, 20), Max(vf, new Vector2f(-20, 20)));
            EXPECT_EQ(-1, MinComponentValue(vf));
            EXPECT_EQ(-10, MinComponentValue(-vf));
            EXPECT_EQ(10, MaxComponentValue(vf));
            EXPECT_EQ(1, MaxComponentValue(-vf));
            EXPECT_EQ(1, MaxComponentIndex(vf));
            EXPECT_EQ(0, MaxComponentIndex(-vf));
            EXPECT_EQ(0, MinComponentIndex(vf));
            EXPECT_EQ(1, MinComponentIndex(-vf));
            EXPECT_EQ(vf, Permute(vf, new int[] { 0, 1 }));
            EXPECT_EQ(new Vector2f(10, -1), Permute(vf, new int[] { 1, 0 }));
            EXPECT_EQ(new Vector2f(10, 10), Permute(vf, new int[] { 1, 1 }));
        }

        [TEST("Vector3", "Basics")]
        public static void Vector3_Basics()
        {
            Vector3f vf = new Vector3f(-1, 10, 2);
            EXPECT_EQ(vf, (Vector3f)new Vector3i(-1, 10, 2));
            EXPECT_NE(vf, new Vector3f(-1, 100, 2));
            EXPECT_EQ(new Vector3f(-2, 20, 4), vf + vf);
            EXPECT_EQ(new Vector3f(0, 0, 0), vf - vf);
            EXPECT_EQ(new Vector3f(-2, 20, 4), vf * 2);
            EXPECT_EQ(new Vector3f(-2, 20, 4), 2 * vf);
            EXPECT_EQ(new Vector3f(-0.5f, 5, 1), vf / 2);
            EXPECT_EQ(new Vector3f(1, 10, 2), Abs(vf));
            EXPECT_EQ(vf, Ceil(new Vector3f(-1.5f, 9.9f, 1.01f)));
            EXPECT_EQ(vf, Floor(new Vector3f(-.5f, 10.01f, 2.99f)));
            EXPECT_EQ(new Vector3f(-20, 10, 1.5f), Min(vf, new Vector3f(-20, 20, 1.5f)));
            EXPECT_EQ(new Vector3f(-1, 20, 2), Max(vf, new Vector3f(-20, 20, 0)));
            EXPECT_EQ(-1, MinComponentValue(vf));
            EXPECT_EQ(-10, MinComponentValue(-vf));
            EXPECT_EQ(10, MaxComponentValue(vf));
            EXPECT_EQ(1, MaxComponentValue(-vf));
            EXPECT_EQ(1, MaxComponentIndex(vf));
            EXPECT_EQ(0, MaxComponentIndex(-vf));
            EXPECT_EQ(0, MinComponentIndex(vf));
            EXPECT_EQ(1, MinComponentIndex(-vf));
            EXPECT_EQ(vf, Permute(vf, new int[] { 0, 1, 2 }));
            EXPECT_EQ(new Vector3f(10, -1, 2), Permute(vf, new int[] { 1, 0, 2 }));
            EXPECT_EQ(new Vector3f(2, -1, 10), Permute(vf, new int[] { 2, 0, 1 }));
            EXPECT_EQ(new Vector3f(10, 10, -1), Permute(vf, new int[] { 1, 1, 0 }));
        }

        [TEST("Point2", "InvertBilinear")]
        public static void Point2_InvertBilinear()
        {
            Func<Point2f, Point2f[], Point2f> bilerp = (Point2f u, Point2f[] v) =>
                Lerp(u[0], Lerp(u[1], v[0], v[2]), Lerp(u[1], v[1], v[3]));

            RNG rng = new RNG();
            Point2f[] vRealSimple = { new Point2f(0, 0), new Point2f(1, 0), new Point2f(0, 1), new Point2f(1, 1) };
            for (int i = 0; i < 100; ++i)
            {
                Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                Point2f u = new Point2f(ux, uy);
                Point2f up = bilerp(u, vRealSimple);
                CHECK_LT(Distance(u, up), 1e-5f);  // check bilerp
                CHECK_LT(Distance(u, InvertBilinear(up, new pstd.span<Point2f>(vRealSimple))), 1e-5f);
            }

            Point2f[] vSimple = { new Point2f(10, -4), new Point2f(10, -9), new Point2f(15, -4),
                                  new Point2f(15, -9) };
            for (int i = 0; i < 100; ++i)
            {
                Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                Point2f u = new Point2f(ux, uy);
                Point2f up = bilerp(u, vSimple);
                Point2f uinv = InvertBilinear(up, new pstd.span<Point2f>(vSimple));
                CHECK_LT(Distance(u, uinv), 1e-5f);
            }

            for (int i = 0; i < 100; ++i)
            {
                for (int j = 0; j < 100; ++j)
                {
                    Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                    Point2f u = new Point2f(ux, uy);
                    Point2f up = bilerp(u, vSimple);
                    Point2f uinv = InvertBilinear(up, new pstd.span<Point2f>(vSimple));
                    CHECK_LT(Distance(u, uinv), 1e-5f);
                }
            }
        }

        private static double[] NormalizeD(double x, double y, double z)
        {
            double l = Math.Sqrt(x * x + y * y + z * z);
            return new double[] { x / l, y / l, z / l };
        }

        private static Vector3f RandomUnit(ref RNG rng)
        {
            Float z = -1 + 2 * rng.Uniform<Float>();
            Float y = -1 + 2 * rng.Uniform<Float>();
            Float x = -1 + 2 * rng.Uniform<Float>();
            return Normalize(new Vector3f(x, y, z));
        }

        [TEST("Vector", "AngleBetween")]
        public static void Vector_AngleBetween()
        {
            EXPECT_EQ(0, AngleBetween(new Vector3f(1, 0, 0), new Vector3f(1, 0, 0)));

            EXPECT_LT(std.abs(AngleBetween(new Vector3f(0, 0, 1), new Vector3f(0, 0, -1))) - Pi, 1e-7);
            EXPECT_LT(std.abs(AngleBetween(new Vector3f(1, 0, 0), new Vector3f(0, 1, 0))) - Pi / 2,
                      1e-7);

            Vector3f x = Normalize(new Vector3f(1, -3, 10));
            EXPECT_EQ(0, AngleBetween(x, x));
            EXPECT_LT(std.abs(AngleBetween(x, -x) - Pi), 3e-7);

            Float maxErr = 0, sumErr = 0;
            for (int i = 0; i < 100000; ++i)
            {
                RNG rng = new RNG((ulong)i);
                Vector3f a = RandomUnit(ref rng);
                Vector3f b = RandomUnit(ref rng);

                double[] ad = NormalizeD(a.x, a.y, a.z);
                double[] bd = NormalizeD(b.x, b.y, b.z);

                Float[] v = { (Float)Math.Acos(ad[0] * bd[0] + ad[1] * bd[1] + ad[2] * bd[2]), AngleBetween(a, b) };
                Float err = std.abs(v[0] - v[1]) / v[0];
                maxErr = std.max(err, maxErr);
                sumErr += err;
                EXPECT_LT(err, 5e-6, v[0] + "vs " + v[1] + ", a: " + a + ", b: " + b);
            }
            maxErr = 0;
            sumErr = 0;

            for (int i = 0; i < 100000; ++i)
            {
                RNG rng = new RNG((ulong)(i + 10000000));
                Vector3f a = RandomUnit(ref rng);
                Vector3f b = RandomUnit(ref rng);
                // Make them face in opposite-ish directions
                a.x = std.copysign(a.x, -b.x);
                a.y = std.copysign(a.y, -b.y);
                a.z = std.copysign(a.z, -b.z);

                double[] ad = NormalizeD(a.x, a.y, a.z);
                double[] bd = NormalizeD(b.x, b.y, b.z);

                Float[] v = { (Float)Math.Acos(ad[0] * bd[0] + ad[1] * bd[1] + ad[2] * bd[2]), AngleBetween(a, b) };
                Float err = std.abs(v[0] - v[1]) / v[0];
                maxErr = std.max(err, maxErr);
                sumErr += err;
                EXPECT_LT(err, 5e-6, v[0] + "vs " + v[1] + ", a: " + a + ", b: " + b);
            }

            Vector3f a2 = new Vector3f(1, 1, 1), b2 = new Vector3f(-1, -1.0001f, -1);
            a2 = Normalize(a2);
            b2 = Normalize(b2);
            double[] ad2 = NormalizeD(1, 1, 1), bd2 = NormalizeD(-1, -1.0001, -1);

            Float naive = SafeACos(Dot(a2, b2));
            Float precise = (Float)Math.Acos(Math.Clamp(ad2[0] * bd2[0] + ad2[1] * bd2[1] + ad2[2] * bd2[2], -1.0, 1.0));
            Float abet = AngleBetween(a2, b2);
            EXPECT_EQ(abet, precise, StringPrintf("vs naive %f", naive));
        }

        [TEST("Vector", "CoordinateSystem")]
        public static void Vector_CoordinateSystem()
        {
            // Duff et al 2017 footnote 1
            Func<Vector3f, Vector3f, Vector3f, Float> error = (Vector3f a, Vector3f b, Vector3f c) =>
                (Sqr(Length(a) - 1) + Sqr(Length(b) - 1) + Sqr(Length(c) - 1) +
                 Sqr(Dot(a, b)) + Sqr(Dot(b, c)) + Sqr(Dot(c, a))) /
                6;

            // Coordinate axes.
            Vector3f a, b;
            foreach (Vector3f v in new Vector3f[] { new Vector3f(1, 0, 0), new Vector3f(-1, 0, 0), new Vector3f(0, 1, 0),
                                                    new Vector3f(0, -1, 0), new Vector3f(0, 0, 1), new Vector3f(0, 0, -1) })
            {
                CoordinateSystem(v, &a, &b);
                for (int c = 0; c < 3; ++c)
                {
                    if (v[c] != 0)
                    {
                        EXPECT_EQ(0, a[c]);
                        EXPECT_EQ(0, b[c]);
                    }
                }
            }

            // Bad vectors from Duff et al
            foreach (Vector3f v in new Vector3f[] { new Vector3f(0.00038527316f, 0.00038460016f, -0.99999988079f),
                                                    new Vector3f(-0.00019813581f, -0.00008946839f, -0.99999988079f) })
            {
                CoordinateSystem(v, &a, &b);
                EXPECT_LT(error(v, a, b), 1e-10);
            }

            // Random vectors
            RNG rng = new RNG();
            for (int i = 0; i < 1000; ++i)
            {
                Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                Point2f u = new Point2f(ux, uy);
                Vector3f v = SampleUniformSphere(u);
                CoordinateSystem(v, &a, &b);
                EXPECT_LT(error(v, a, b), 1e-10);
            }
        }

        [TEST("Bounds2", "IteratorBasic")]
        public static void Bounds2_IteratorBasic()
        {
            Bounds2i b = new Bounds2i(new Point2i(0, 1), new Point2i(2, 3));
            Point2i[] e = { new Point2i(0, 1), new Point2i(1, 1), new Point2i(0, 2), new Point2i(1, 2) };
            int offset = 0;
            foreach (Point2i p in b)
            {
                EXPECT_LT(offset, e.Length);
                EXPECT_EQ(e[offset], p, "offset = " + offset);
                ++offset;
            }
        }

        [TEST("Bounds2", "IteratorDegenerate")]
        public static void Bounds2_IteratorDegenerate()
        {
            Bounds2i b = new Bounds2i(new Point2i(0, 0), new Point2i(0, 10));
            foreach (Point2i p in b)
            {
                // This loop should never run.
                bool reached = true;
                EXPECT_FALSE(reached, "p = " + p);
                break;
            }

            Bounds2i b2 = new Bounds2i(new Point2i(0, 0), new Point2i(4, 0));
            foreach (Point2i p in b2)
            {
                // This loop should never run.
                bool reached = true;
                EXPECT_FALSE(reached, "p = " + p);
                break;
            }

            Bounds2i b3 = new Bounds2i();
            foreach (Point2i p in b3)
            {
                // This loop should never run.
                bool reached = true;
                EXPECT_FALSE(reached, "p = " + p);
                break;
            }
        }

        [TEST("Bounds3", "PointDistance")]
        public static void Bounds3_PointDistance()
        {
            {
                Bounds3f b = new Bounds3f(new Point3f(0, 0, 0), new Point3f(1, 1, 1));

                // Points inside the bounding box or on faces
                EXPECT_EQ(0.0, Distance(new Point3f(.5f, .5f, .5f), b));
                EXPECT_EQ(0.0, Distance(new Point3f(0, 1, 1), b));
                EXPECT_EQ(0.0, Distance(new Point3f(.25f, .8f, 1), b));
                EXPECT_EQ(0.0, Distance(new Point3f(0, .25f, .8f), b));
                EXPECT_EQ(0.0, Distance(new Point3f(.7f, 0, .8f), b));

                // Aligned with the plane of one of the faces
                EXPECT_EQ(5.0, Distance(new Point3f(6, 1, 1), b));
                EXPECT_EQ(10.0, Distance(new Point3f(0, -10, 1), b));

                // 2 of the dimensions inside the box's extent
                EXPECT_EQ(2.0, Distance(new Point3f(0.5f, 0.5f, 3), b));
                EXPECT_EQ(3.0, Distance(new Point3f(0.5f, 0.5f, -3), b));
                EXPECT_EQ(2.0, Distance(new Point3f(0.5f, 3, 0.5f), b));
                EXPECT_EQ(3.0, Distance(new Point3f(0.5f, -3, 0.5f), b));
                EXPECT_EQ(2.0, Distance(new Point3f(3, 0.5f, 0.5f), b));
                EXPECT_EQ(3.0, Distance(new Point3f(-3, 0.5f, 0.5f), b));

                // General points
                EXPECT_EQ(3 * 3 + 7 * 7 + 10 * 10, DistanceSquared(new Point3f(4, 8, -10), b));
                EXPECT_EQ(6 * 6 + 10 * 10 + 7 * 7, DistanceSquared(new Point3f(-6, -10, 8), b));
            }

            {
                // A few with a more irregular box, just to be sure
                Bounds3f b = new Bounds3f(new Point3f(-1, -3, 5), new Point3f(2, -2, 18));
                EXPECT_EQ(0.0, Distance(new Point3f(-.99f, -2, 5), b));
                EXPECT_EQ(2 * 2 + 6 * 6 + 4 * 4, DistanceSquared(new Point3f(-3, -9, 22), b));
            }
        }

        [TEST("Bounds2", "Union")]
        public static void Bounds2_Union()
        {
            Bounds2f a = new Bounds2f(new Point2f(-10, -10), new Point2f(0, 20));
            Bounds2f b = new Bounds2f();  // degenerate
            Bounds2f c = Union(a, b);
            EXPECT_EQ(a, c);

            EXPECT_EQ(b, Union(b, b));

            Bounds2f d = new Bounds2f(new Point2f(-15, 10));
            Bounds2f e = Union(a, d);
            EXPECT_EQ(new Bounds2f(new Point2f(-15, -10), new Point2f(0, 20)), e);
        }

        [TEST("Bounds3", "Union")]
        public static void Bounds3_Union()
        {
            Bounds3f a = new Bounds3f(new Point3f(-10, -10, 5), new Point3f(0, 20, 10));
            Bounds3f b = new Bounds3f();  // degenerate
            Bounds3f c = Union(a, b);
            EXPECT_EQ(a, c);

            EXPECT_EQ(b, Union(b, b));

            Bounds3f d = new Bounds3f(new Point3f(-15, 10, 30));
            Bounds3f e = Union(a, d);
            EXPECT_EQ(new Bounds3f(new Point3f(-15, -10, 5), new Point3f(0, 20, 30)), e);
        }

        [TEST("EqualArea", "Randoms")]
        public static void EqualArea_Randoms()
        {
            foreach (Point2f u in new Uniform2D(100))
            {
                Vector3f v = SampleUniformSphere(u);
                Point2f c = EqualAreaSphereToSquare(v);
                Vector3f vp = EqualAreaSquareToSphere(c);
                EXPECT_TRUE(Length(vp) > 0.9999 && Length(vp) < 1.0001, Length(vp).ToString());
                EXPECT_GT(Dot(v, vp), 0.9999, v.ToString());
            }
        }

        [TEST("EqualArea", "RemapEdges")]
        public static void EqualArea_RemapEdges()
        {
            Action<Point2f, Point2f> checkClose = (Point2f a, Point2f b) =>
            {
                Vector3f av = EqualAreaSquareToSphere(a);
                b = WrapEqualAreaSquare(b);
                Vector3f bv = EqualAreaSquareToSphere(b);
                EXPECT_GT(Dot(av, bv), .99);
            };

            checkClose(new Point2f(.25f, .01f), new Point2f(.25f, -.01f));
            checkClose(new Point2f(.89f, .01f), new Point2f(.89f, -.01f));

            checkClose(new Point2f(.25f, .99f), new Point2f(.25f, 1.01f));
            checkClose(new Point2f(.89f, .99f), new Point2f(.89f, 1.01f));

            checkClose(new Point2f(.01f, .66f), new Point2f(-.01f, .66f));
            checkClose(new Point2f(.01f, .15f), new Point2f(-.01f, .15f));

            checkClose(new Point2f(.99f, .66f), new Point2f(1.01f, .66f));
            checkClose(new Point2f(.99f, .15f), new Point2f(1.01f, .15f));

            checkClose(new Point2f(.01f, .01f), new Point2f(-.01f, -.01f));
            checkClose(new Point2f(.99f, .01f), new Point2f(1.01f, -.01f));
            checkClose(new Point2f(.01f, .99f), new Point2f(-.01f, 1.01f));
            checkClose(new Point2f(.99f, .99f), new Point2f(1.01f, 1.01f));
        }

        private static DirectionCone RandomCone(ref RNG rng)
        {
            Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
            Vector3f w = SampleUniformSphere(new Point2f(ux, uy));
            return new DirectionCone(w, -1 + 2 * rng.Uniform<Float>());
        }

        [TEST("DirectionCone", "UnionBasics")]
        public static void DirectionCone_UnionBasics()
        {
            // First encloses second
            DirectionCone c = Union(new DirectionCone(new Vector3f(0, 0, 1), std.cos(Pi / 2)),
                                    new DirectionCone(new Vector3f(.1f, .1f, 1), std.cos(.1f)));
            EXPECT_EQ(c.w, new Vector3f(0, 0, 1));
            EXPECT_EQ(c.cosTheta, std.cos(Pi / 2));

            // Second encloses first
            c = Union(new DirectionCone(new Vector3f(.1f, .1f, 1), std.cos(.1f)),
                      new DirectionCone(new Vector3f(0, 0, 1), std.cos(Pi / 2)));
            EXPECT_EQ(c.w, new Vector3f(0, 0, 1));
            EXPECT_EQ(c.cosTheta, std.cos(Pi / 2));

            // Same direction, first wider
            Vector3f w = new Vector3f(1, .5f, -.25f);
            c = Union(new DirectionCone(w, std.cos(.12f)), new DirectionCone(w, std.cos(.03f)));
            EXPECT_EQ(Normalize(w), c.w);
            EXPECT_FLOAT_EQ(std.cos(.12f), c.cosTheta);

            // Same direction, second wider
            c = Union(new DirectionCone(w, std.cos(.1f)), new DirectionCone(w, std.cos(.2f)));
            EXPECT_EQ(Normalize(w), c.w);
            EXPECT_FLOAT_EQ(std.cos(.2f), c.cosTheta);

            // Exactly pointing in opposite directions and covering the sphere when
            // it's all said and done.
            c = Union(new DirectionCone(new Vector3f(-1, -1, -1), std.cos(Pi / 2)),
                      new DirectionCone(new Vector3f(1, 1, 1), std.cos(Pi / 2)));
            EXPECT_EQ(c.cosTheta, std.cos(Pi));

            // Basically opposite and a bit more than pi/2: should also be the
            // whole sphere.
            c = Union(new DirectionCone(new Vector3f(-1, -1, -1), std.cos((Float)(1.01 * Pi / 2))),
                      new DirectionCone(new Vector3f(1.001f, 1, 1), std.cos((Float)(1.01 * Pi / 2))));
            EXPECT_EQ(c.cosTheta, std.cos(Pi));

            // Narrow and at right angles; angle should be their midpoint
            c = Union(new DirectionCone(new Vector3f(1, 0, 0), std.cos(1e-3f)),
                      new DirectionCone(new Vector3f(0, 1, 0), std.cos(1e-3f)));
            EXPECT_FLOAT_EQ(1, Dot(c.w, Normalize(new Vector3f(1, 1, 0))));
            EXPECT_LT(std.abs(std.cos((Pi / 2 + 2e-3f) / 2) - c.cosTheta), 1e-3);
        }

        [TEST("DirectionCone", "UnionRandoms")]
        public static void DirectionCone_UnionRandoms()
        {
            RNG rng = new RNG(16);

            for (int i = 0; i < 100; ++i)
            {
                DirectionCone a = RandomCone(ref rng);
                DirectionCone b = RandomCone(ref rng);
                DirectionCone c = Union(a, b);

                for (int j = 0; j < 100; ++j)
                {
                    Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                    Vector3f w = SampleUniformSphere(new Point2f(ux, uy));
                    if (Inside(a, w) || Inside(b, w))
                        EXPECT_TRUE(Inside(c, w),
                                    "a: " + a + ", b: " + b + ", union: " + c + ", w: " + w);
                }
            }
        }

        [TEST("DirectionCone", "BoundBounds")]
        public static void DirectionCone_BoundBounds()
        {
            Bounds3f b = new Bounds3f(new Point3f(0, 0, 0), new Point3f(1, 1, 1));

            // Point inside the bbox
            DirectionCone c = BoundSubtendedDirections(b, new Point3f(.1f, .2f, .3f));
            EXPECT_EQ(std.cos(Pi), c.cosTheta);

            // Outside, .5 units away in the middle (so using the direction to the
            // center gives the best bound).
            //
            // tan theta = (sqrt(.5^2 + .5^2)) / .5
            c = BoundSubtendedDirections(b, new Point3f(-.5f, .5f, .5f));
            Float theta = std.acos(c.cosTheta);
            Float precise = (Float)Math.Atan(Math.Sqrt(.5 * .5 + .5 * .5) / .5);
            // Make sure the returned bound isn't too small.
            EXPECT_GE(theta, 1.0001 * precise);
            // It's fine for it to be a bit big (as it is in practice due to
            // approximations for performance), but it shouldn't be too big.
            EXPECT_LT(theta, 1.1 * precise);

            RNG rng = new RNG(512);
            for (int i = 0; i < 1000; ++i)
            {
                Float[] q = new Float[6];
                for (int k = 5; k >= 0; --k) q[k] = Lerp(rng.Uniform<Float>(), -1, 1);
                Bounds3f bb = new Bounds3f(new Point3f(q[0], q[1], q[2]), new Point3f(q[3], q[4], q[5]));

                Float pz = Lerp(rng.Uniform<Float>(), -4, 4);
                Float py = Lerp(rng.Uniform<Float>(), -4, 4);
                Float px = Lerp(rng.Uniform<Float>(), -4, 4);
                Point3f p = new Point3f(px, py, pz);

                c = BoundSubtendedDirections(bb, p);
                if (Inside(p, bb))
                {
                    EXPECT_EQ(std.cos(Pi), c.cosTheta);
                }
                else
                {
                    Vector3f wx, wy;
                    CoordinateSystem(c.w, &wx, &wy);
                    for (int j = 0; j < 1000; ++j)
                    {
                        Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                        Point2f u = new Point2f(ux, uy);
                        Vector3f w = SampleUniformSphere(u);
                        Ray r = new Ray(p, w);
                        bool hit = bb.IntersectP(r.o, r.d);
                        if (hit)
                        {
                            EXPECT_TRUE(Inside(c, w));
                        }
                        if (!Inside(c, w))
                        {
                            EXPECT_FALSE(hit);
                        }
                    }
                }
            }
        }

        [TEST("DirectionCone", "VectorInCone")]
        public static void DirectionCone_VectorInCone()
        {
            RNG rng = new RNG();
            for (int i = 0; i < 100; ++i)
            {
                DirectionCone dc = RandomCone(ref rng);

                for (int j = 0; j < 100; ++j)
                {
                    Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                    Vector3f wRandom = SampleUniformSphere(new Point2f(ux, uy));
                    Vector3f wClosest = dc.ClosestVectorInCone(wRandom);

                    if (Inside(dc, wRandom))
                        EXPECT_GT(Dot(wClosest, wRandom), .999);
                    else
                    {
                        // Uniformly sample the circle at the cone's boundary and
                        // keep the vector that's closest to wRandom.
                        Float sinTheta = SafeSqrt(1 - dc.cosTheta * dc.cosTheta);
                        Frame f = Frame.FromZ(dc.w);

                        Vector3f wBest = new Vector3f();
                        Float bestDot = -1;
                        const int nk = 1000;
                        for (int k = 0; k < nk; ++k)
                        {
                            Float phi = (Float)((k + .5) / nk * 2 * Pi);
                            Vector3f w = SphericalDirection(sinTheta, dc.cosTheta, phi);
                            w = f.FromLocal(w);
                            if (Dot(w, wRandom) > bestDot)
                            {
                                wBest = w;
                                bestDot = Dot(w, wRandom);
                            }
                        }
                        EXPECT_GT(Dot(wBest, wClosest), .999,
                                  wBest + " vs " + wClosest + ", dot " + Dot(wBest, wClosest));
                    }
                }
            }
        }

        [TEST("SphericalTriangleArea", "Basics")]
        public static void SphericalTriangleArea_Basics()
        {
            {
                Float a = SphericalTriangleArea(new Vector3f(1, 0, 0), new Vector3f(0, 1, 0),
                                                new Vector3f(0, 0, 1));
                EXPECT_TRUE(a >= .9999 * Pi / 2 && a <= 1.00001 * Pi / 2, a.ToString());
            }

            {
                Float a = SphericalTriangleArea(new Vector3f(1, 0, 0), Normalize(new Vector3f(1, 1, 0)),
                                                new Vector3f(0, 0, 1));
                EXPECT_TRUE(a >= .9999 * Pi / 4 && a <= 1.00001 * Pi / 4, a.ToString());
            }

            // Random rotations
            RNG rng = new RNG();
            for (int i = 0; i < 100; ++i)
            {
                Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                Vector3f axis = SampleUniformSphere(new Point2f(ux, uy));
                Float theta = 2 * Pi * rng.Uniform<Float>();
                Transform t = Rotate(theta, axis);
                Vector3f va = t.Apply(new Vector3f(1, 0, 0));
                Vector3f vb = t.Apply(new Vector3f(0, 1, 0));
                Vector3f vc = t.Apply(new Vector3f(0, 0, 1));
                Float a = SphericalTriangleArea(va, vb, vc);
                EXPECT_TRUE(a >= .9999 * Pi / 2 && a <= 1.0001 * Pi / 2, a.ToString());
            }

            for (int i = 0; i < 100; ++i)
            {
                Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                Vector3f axis = SampleUniformSphere(new Point2f(ux, uy));
                Float theta = 2 * Pi * rng.Uniform<Float>();
                Transform t = Rotate(theta, axis);
                Vector3f va = t.Apply(new Vector3f(1, 0, 0));
                Vector3f vb = t.Apply(Normalize(new Vector3f(1, 1, 0)));
                Vector3f vc = t.Apply(new Vector3f(0, 0, 1));
                Float a = SphericalTriangleArea(va, vb, vc);
                EXPECT_TRUE(a >= .9999 * Pi / 4 && a <= 1.0001 * Pi / 4, a.ToString());
            }
        }

        [TEST("SphericalTriangleArea", "RandomSampling")]
        public static void SphericalTriangleArea_RandomSampling()
        {
            for (int i = 0; i < 100; ++i)
            {
                RNG rng = new RNG((ulong)i);
                Float ay = rng.Uniform<Float>(), ax = rng.Uniform<Float>();
                Vector3f a = SampleUniformSphere(new Point2f(ax, ay));
                Float by = rng.Uniform<Float>(), bx = rng.Uniform<Float>();
                Vector3f b = SampleUniformSphere(new Point2f(bx, by));
                Float cy = rng.Uniform<Float>(), cx = rng.Uniform<Float>();
                Vector3f c = SampleUniformSphere(new Point2f(cx, cy));

                Vector3f axis = Normalize(a + b + c);
                Frame frame = Frame.FromZ(axis);
                Float cosTheta = std.min(std.min(Dot(a, axis), Dot(b, axis)), Dot(c, axis));

                Float area = SphericalTriangleArea(a, b, c);
                bool sampleSphere = area > Pi;
                int sqrtN = 200;
                int count = 0;
                foreach (Point2f u in new Hammersley2D(sqrtN * sqrtN))
                {
                    Vector3f v;
                    if (sampleSphere)
                        v = SampleUniformSphere(u);
                    else
                    {
                        v = SampleUniformCone(u, cosTheta);
                        v = frame.FromLocal(v);
                    }

                    if (IntersectTriangle(new Ray(new Point3f(0, 0, 0), v), Infinity, (Point3f)a,
                                          (Point3f)b, (Point3f)c).has_value())
                        ++count;
                }

                Float pdf = sampleSphere ? UniformSpherePDF() : UniformConePDF(cosTheta);
                Float estA = (Float)count / (sqrtN * sqrtN * pdf);

                Float error = std.abs((estA - area) / area);
                EXPECT_LT(error, 0.035f, "Area " + area + ", estimate " + estA +
                                         ", va = " + a + ", vb = " + b + ", vc = " + c);
            }
        }

        [TEST("PointVector", "Interval")]
        public static void PointVector_Interval()
        {
            // This is really just to make sure that various expected things
            // compile in the first place when using the interval variants of
            // these...
            Point3fi p = new Point3fi(1, 2, 3), q = new Point3fi(6, 9, 1);
            Vector3fi v = new Vector3fi(4, 5, 6);

            p += v;
            p = (p - v);
            Vector3fi scaled = new Interval(4) * v;
            p = p + scaled;
            Interval d = Dot(v, v);
            d = DistanceSquared(p, q);
            d = Distance(p, q);

            Vector3fi vv = Cross(v, v);
        }

        [TEST("OctahedralVector", "EncodeDecode")]
        public static void OctahedralVector_EncodeDecode()
        {
            foreach (Point2f u in new Hammersley2D(65535))
            {
                Vector3f v = SampleUniformSphere(u);

                OctahedralVector ov = new OctahedralVector(v);
                Vector3f v2 = (Vector3f)ov;

                EXPECT_GT(Length(v2), .999f);
                EXPECT_LT(Length(v2), 1.001f);
                EXPECT_LT(std.abs(1 - Dot(v2, v)), .001f);
            }
        }
    }
}
