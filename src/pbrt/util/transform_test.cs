// transform_test.cpp

using System;
using testing;

namespace pbrt
{
    public static unsafe class TransformTests
    {
        private static Transform RandomTransform(ref RNG rng)
        {
            Transform t = new Transform();
            for (int i = 0; i < 10; ++i)
            {
                switch (rng.Uniform<uint32_t>(3))
                {
                    case 0:
                        {
                            // (C++ evaluates function arguments right to left under gcc)
                            Float z = std.abs(-10f + 20f * rng.Uniform<Float>());
                            Float y = std.abs(-10f + 20f * rng.Uniform<Float>());
                            Float x = std.abs(-10f + 20f * rng.Uniform<Float>());
                            t = t * Scale(x, y, z);
                            break;
                        }
                    case 1:
                        {
                            Float z = -10f + 20f * rng.Uniform<Float>();
                            Float y = -10f + 20f * rng.Uniform<Float>();
                            Float x = -10f + 20f * rng.Uniform<Float>();
                            t = t * Translate(new Vector3f(x, y, z));
                            break;
                        }
                    case 2:
                        {
                            Float v = rng.Uniform<Float>();
                            Float u = rng.Uniform<Float>();
                            Vector3f axis = SampleUniformSphere(new Point2f(u, v));
                            Float angle = (-10f + 20f * rng.Uniform<Float>()) * 20f;
                            t = t * Rotate(angle, axis);
                            break;
                        }
                }
            }
            return t;
        }

        [TEST("AnimatedTransform", "Randoms")]
        public static void AnimatedTransform_Randoms()
        {
            RNG rng = new RNG();

            for (int i = 0; i < 200; ++i)
            {
                // Generate a pair of random transformation matrices.
                Transform t0 = RandomTransform(ref rng);
                Transform t1 = RandomTransform(ref rng);
                AnimatedTransform at = new AnimatedTransform(t0, 0f, t1, 1f);

                for (int j = 0; j < 5; ++j)
                {
                    // Generate a random bounding box and find the bounds of its motion.
                    Float[] c = new Float[6];
                    for (int k = 5; k >= 0; --k)
                        c[k] = -10f + 20f * rng.Uniform<Float>();
                    Bounds3f bounds = new Bounds3f(new Point3f(c[0], c[1], c[2]), new Point3f(c[3], c[4], c[5]));
                    Bounds3f motionBounds = at.MotionBounds(bounds);

                    for (Float t = 0f; t <= 1f; t += 1e-2f * rng.Uniform<Float>())
                    {
                        // Now, interpolate the transformations at a bunch of times
                        // along the time range and then transform the bounding box
                        // with the result.
                        Transform tr = at.Interpolate(t);
                        Bounds3f tb = tr.Apply(bounds);

                        // Add a little slop to allow for floating-point round-off
                        // error in computing the motion extrema times.
                        tb.pMin += (Float)1e-4 * tb.Diagonal();
                        tb.pMax -= (Float)1e-4 * tb.Diagonal();

                        // Now, the transformed bounds should be inside the motion
                        // bounds.
                        EXPECT_GE(tb.pMin.x, motionBounds.pMin.x);
                        EXPECT_LE(tb.pMax.x, motionBounds.pMax.x);
                        EXPECT_GE(tb.pMin.y, motionBounds.pMin.y);
                        EXPECT_LE(tb.pMax.y, motionBounds.pMax.y);
                        EXPECT_GE(tb.pMin.z, motionBounds.pMin.z);
                        EXPECT_LE(tb.pMax.z, motionBounds.pMax.z);
                    }
                }
            }
        }

        [TEST("RotateFromTo", "Simple")]
        public static void RotateFromTo_Simple()
        {
            {
                // Same directions...
                Vector3f from = new Vector3f(0, 0, 1), to = new Vector3f(0, 0, 1);
                Transform r = RotateFromTo(from, to);
                Vector3f toNew = r.Apply(from);
                EXPECT_EQ(to, toNew);
            }

            {
                Vector3f from = new Vector3f(0, 0, 1), to = new Vector3f(1, 0, 0);
                Transform r = RotateFromTo(from, to);
                Vector3f toNew = r.Apply(from);
                EXPECT_EQ(to, toNew);
            }

            {
                Vector3f from = new Vector3f(0, 0, 1), to = new Vector3f(0, 1, 0);
                Transform r = RotateFromTo(from, to);
                Vector3f toNew = r.Apply(from);
                EXPECT_EQ(to, toNew);
            }
        }

        [TEST("RotateFromTo", "Randoms")]
        public static void RotateFromTo_Randoms()
        {
            RNG rng = new RNG();
            for (int i = 0; i < 100; ++i)
            {
                Float b0 = rng.Uniform<Float>(); Float b1 = rng.Uniform<Float>();
                Vector3f from = SampleUniformSphere(new Point2f(b0, b1));
                Float c0 = rng.Uniform<Float>(); Float c1 = rng.Uniform<Float>();
                Vector3f to = SampleUniformSphere(new Point2f(c0, c1));

                Transform r = RotateFromTo(from, to);
                Vector3f toNew = r.Apply(from);
                EXPECT_LT(std.abs(Length(toNew) - 1), 1e-3f);
                EXPECT_GT(Dot(to, toNew), .999f);
            }
        }
    }
}
