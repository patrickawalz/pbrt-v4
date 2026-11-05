// Tests of the N-key AnimatedTransform (keys with a time each, interpolated per segment) and of the
// MotionBegin/MotionEnd directive that supplies the keys in a scene file. See docs/animated-transforms.md.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using testing;

namespace pbrt
{
    public static unsafe class AnimatedTransformNKeyTests
    {
        // ------------------------------------------------------------------------------------------
        // Helpers

        // The kind of transformation of the existing AnimatedTransform randoms test (wild: it contains very
        // large scales and rotations of many turns)
        private static Transform WildTransform(ref RNG rng)
        {
            Transform t = new Transform();
            for (int i = 0; i < 10; ++i)
            {
                switch (rng.Uniform<uint32_t>(3))
                {
                    case 0:
                        {
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

        // A translation, a rotation about a random axis and a positive scale in the axes of the object
        private static Transform MildTransform(ref RNG rng)
        {
            Float tz = -5f + 10f * rng.Uniform<Float>();
            Float ty = -5f + 10f * rng.Uniform<Float>();
            Float tx = -5f + 10f * rng.Uniform<Float>();
            Float v = rng.Uniform<Float>();
            Float u = rng.Uniform<Float>();
            Vector3f axis = SampleUniformSphere(new Point2f(u, v));
            Float angle = -180f + 360f * rng.Uniform<Float>();
            Float sz = 0.5f + 1.5f * rng.Uniform<Float>();
            Float sy = 0.5f + 1.5f * rng.Uniform<Float>();
            Float sx = 0.5f + 1.5f * rng.Uniform<Float>();
            return Translate(new Vector3f(tx, ty, tz)) * Rotate(angle, axis) * Scale(sx, sy, sz);
        }

        private static Bounds3f RandomBox(ref RNG rng)
        {
            Float[] c = new Float[6];
            for (int k = 5; k >= 0; --k)
                c[k] = -10f + 20f * rng.Uniform<Float>();
            return new Bounds3f(new Point3f(c[0], c[1], c[2]), new Point3f(c[3], c[4], c[5]));
        }

        private static Point3f RandomPoint(ref RNG rng)
        {
            Float z = -10f + 20f * rng.Uniform<Float>();
            Float y = -10f + 20f * rng.Uniform<Float>();
            Float x = -10f + 20f * rng.Uniform<Float>();
            return new Point3f(x, y, z);
        }

        private static bool SameBits(Float a, Float b)
        {
            return BitConverter.DoubleToInt64Bits((double)a) == BitConverter.DoubleToInt64Bits((double)b);
        }

        private static bool SameBits(SquareMatrix4 a, SquareMatrix4 b)
        {
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                    if (!SameBits(a[i][j], b[i][j]))
                        return false;
            return true;
        }

        private static bool SameBits(Transform a, Transform b)
        {
            return SameBits(a.GetMatrix(), b.GetMatrix()) && SameBits(a.GetInverseMatrix(), b.GetInverseMatrix());
        }

        private static bool SameBits(Point3f a, Point3f b)
        {
            return SameBits(a.x, b.x) && SameBits(a.y, b.y) && SameBits(a.z, b.z);
        }

        private static bool SameBits(Bounds3f a, Bounds3f b)
        {
            return SameBits(a.pMin, b.pMin) && SameBits(a.pMax, b.pMax);
        }

        // How far p is outside of the bounds b (0 if it is inside)
        private static Float Outside(Bounds3f b, Point3f p)
        {
            Float d = 0;
            d = std.max(d, b.pMin.x - p.x);
            d = std.max(d, b.pMin.y - p.y);
            d = std.max(d, b.pMin.z - p.z);
            d = std.max(d, p.x - b.pMax.x);
            d = std.max(d, p.y - b.pMax.y);
            d = std.max(d, p.z - b.pMax.z);
            return d;
        }

        private static double BoxVolume(Bounds3f b)
        {
            return (double)(b.pMax.x - b.pMin.x) * (double)(b.pMax.y - b.pMin.y) * (double)(b.pMax.z - b.pMin.z);
        }

        private static Float Distance(Point3f a, Point3f b)
        {
            return Length(a - b);
        }

        private static void Report(string line)
        {
            Console.WriteLine("[NKEY] " + line);
        }

        // ------------------------------------------------------------------------------------------
        // (a) two keys: bit for bit the AnimatedTransform of pbrt-v4 before the generalisation

        [TEST("AnimatedTransformNKey", "TwoKeysAreBitIdenticalToLegacy")]
        public static void TwoKeysAreBitIdenticalToLegacy()
        {
            RNG rng = new RNG();
            int nCompared = 0;
            int nBoundsCompared = 0;
            int nMismatch = 0;
            for (int i = 0; i < 400; ++i)
            {
                Transform t0, t1;
                switch (i % 4)
                {
                    case 0: t0 = WildTransform(ref rng); t1 = WildTransform(ref rng); break;
                    case 1: t0 = MildTransform(ref rng); t1 = MildTransform(ref rng); break;
                    case 2:
                        // no rotation: the bounds are those of the two end points
                        t0 = Translate(new Vector3f(rng.Uniform<Float>(), 2f, 3f)) * Scale(1f, 2f, 3f);
                        t1 = Translate(new Vector3f(5f, rng.Uniform<Float>(), 1f)) * Scale(2f, 1f, 0.5f);
                        break;
                    default:
                        // a tiny rotation (below the hasRotation threshold) and equal transforms
                        t0 = MildTransform(ref rng);
                        t1 = (i % 8 == 3) ? t0 : t0 * Rotate(1f, new Vector3f(0, 0, 1));
                        break;
                }
                Float time0 = -2f + 4f * rng.Uniform<Float>();
                Float time1 = time0 + 0.1f + 3f * rng.Uniform<Float>();
                LegacyAnimatedTransform legacy = new LegacyAnimatedTransform(t0, time0, t1, time1);
                AnimatedTransform current = new AnimatedTransform(t0, time0, t1, time1);
                AnimatedTransform viaKeys = new AnimatedTransform(new Transform[] { t0, t1 }, new Float[] { time0, time1 });

                EXPECT_EQ(legacy.IsAnimated(), current.IsAnimated());
                EXPECT_EQ(legacy.HasScale(), current.HasScale());
                EXPECT_EQ(2, current.KeyCount);
                for (int j = 0; j < 60; ++j)
                {
                    Float time;
                    if (j == 0) time = time0;
                    else if (j == 1) time = time1;
                    else if (j == 2) time = time0 - 1f;
                    else if (j == 3) time = time1 + 1f;
                    else time = time0 + (time1 - time0) * (-0.1f + 1.2f * rng.Uniform<Float>());
                    Transform a = legacy.Interpolate(time);
                    Transform b = current.Interpolate(time);
                    Transform c = viaKeys.Interpolate(time);
                    bool same = SameBits(a, b) && SameBits(a, c);
                    Point3f p = RandomPoint(ref rng);
                    same &= SameBits(legacy.Apply(p, time), current.Apply(p, time));
                    same &= SameBits(legacy.ApplyInverse(p, time), current.ApplyInverse(p, time));
                    if (!same)
                        ++nMismatch;
                    ++nCompared;
                }
                for (int j = 0; j < 5; ++j)
                {
                    Bounds3f box = RandomBox(ref rng);
                    Point3f p = RandomPoint(ref rng);
                    bool same = SameBits(legacy.MotionBounds(box), current.MotionBounds(box)) &&
                                SameBits(legacy.MotionBounds(box), viaKeys.MotionBounds(box)) &&
                                SameBits(legacy.BoundPointMotion(p), current.BoundPointMotion(p));
                    if (!same)
                        ++nMismatch;
                    ++nBoundsCompared;
                }
            }
            Report("two keys vs legacy: " + nCompared + " interpolations and " + nBoundsCompared +
                   " bounds compared bit for bit, " + nMismatch + " mismatches");
            EXPECT_EQ(0, nMismatch);
        }

        // ------------------------------------------------------------------------------------------
        // (b) the keys are reproduced exactly at the key times, and the animation is continuous there

        [TEST("AnimatedTransformNKey", "KeysAreReproducedAtKeyTimes")]
        public static void KeysAreReproducedAtKeyTimes()
        {
            RNG rng = new RNG();
            Float worstJump = 0;
            for (int i = 0; i < 60; ++i)
            {
                int n = 3 + (int)rng.Uniform<uint32_t>(6);
                Transform[] keys = new Transform[n];
                Float[] times = new Float[n];
                Float time = -1f + 2f * rng.Uniform<Float>();
                for (int k = 0; k < n; ++k)
                {
                    keys[k] = MildTransform(ref rng);
                    times[k] = time;
                    time += 0.25f + 2f * rng.Uniform<Float>();
                }
                AnimatedTransform at = new AnimatedTransform(keys, times);
                EXPECT_EQ(n, at.KeyCount);
                EXPECT_EQ(n - 1, at.SegmentCount);
                for (int k = 0; k < n; ++k)
                {
                    EXPECT_TRUE(SameBits(keys[k], at.Interpolate(times[k])));
                    EXPECT_TRUE(SameBits(keys[k], at.GetKeyTransform(k)));
                    EXPECT_EQ(times[k], at.GetKeyTime(k));
                    // The animation is continuous at the key: just before and after the key time the
                    // transformed points are close to those of the key (the decomposition is exact)
                    Float step = 1e-5f;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Float tNear = times[k] + side * step;
                        if (k == 0 && side < 0 || k == n - 1 && side > 0)
                            continue;
                        Transform near = at.Interpolate(tNear);
                        for (int j = 0; j < 4; ++j)
                        {
                            Point3f p = RandomPoint(ref rng);
                            Float jump = Distance(near.Apply(p), keys[k].Apply(p));
                            worstJump = std.max(worstJump, jump);
                            EXPECT_LT(jump, 0.02f);
                        }
                    }
                }
                // Outside of the key times the animation is clamped
                EXPECT_TRUE(SameBits(keys[0], at.Interpolate(times[0] - 5f)));
                EXPECT_TRUE(SameBits(keys[n - 1], at.Interpolate(times[n - 1] + 5f)));
            }
            Report("keys: the largest jump of a transformed point within 1e-5 of a key time: " + worstJump);
        }

        [TEST("AnimatedTransformNKey", "InvalidKeysAreRejected")]
        public static void InvalidKeysAreRejected()
        {
            Transform id = new Transform();
            Transform tr = Translate(new Vector3f(1, 0, 0));
            bool threw;

            threw = false;
            try { new AnimatedTransform(new Transform[] { id }, new Float[] { 0 }); }
            catch (ArgumentException) { threw = true; }
            EXPECT_TRUE(threw);

            threw = false;
            try { new AnimatedTransform(new Transform[] { id, tr, id }, new Float[] { 0, 1 }); }
            catch (ArgumentException) { threw = true; }
            EXPECT_TRUE(threw);

            threw = false;
            try { new AnimatedTransform(new Transform[] { id, tr, id }, new Float[] { 0, 1, 1 }); }
            catch (ArgumentException) { threw = true; }
            EXPECT_TRUE(threw);

            threw = false;
            try { new AnimatedTransform(new Transform[] { id, tr, id }, new Float[] { 0, 2, 1 }); }
            catch (ArgumentException) { threw = true; }
            EXPECT_TRUE(threw);
        }

        [TEST("AnimatedTransformNKey", "SegmentLookupAndStaticSegments")]
        public static void SegmentLookupAndStaticSegments()
        {
            // Keys 1 and 2 are equal: the segment between them is static (and exact), the others move
            Transform a = Translate(new Vector3f(0, 0, 0));
            Transform b = Translate(new Vector3f(4, 0, 0));
            AnimatedTransform at = new AnimatedTransform(new Transform[] { a, b, b, a },
                                                         new Float[] { 0f, 1f, 3f, 4f });
            EXPECT_TRUE(at.IsAnimated());
            Point3f origin = new Point3f(0, 0, 0);
            EXPECT_FLOAT_EQ(at.Apply(origin, 0.5f).x, 2f);
            EXPECT_FLOAT_EQ(at.Apply(origin, 1f).x, 4f);
            EXPECT_TRUE(SameBits(b, at.Interpolate(2f)));
            EXPECT_TRUE(SameBits(b, at.Interpolate(2.999f)));
            EXPECT_FLOAT_EQ(at.Apply(origin, 3.5f).x, 2f);
            EXPECT_FLOAT_EQ(at.Apply(origin, 4f).x, 0f);
            // The first and the last key are equal but the animation is not static
            AnimatedTransform same = new AnimatedTransform(new Transform[] { a, b, a }, new Float[] { 0f, 1f, 2f });
            EXPECT_TRUE(same.IsAnimated());
            // All keys equal: static
            AnimatedTransform still = new AnimatedTransform(new Transform[] { b, b, b }, new Float[] { 0f, 1f, 2f });
            EXPECT_FALSE(still.IsAnimated());
            EXPECT_TRUE(SameBits(b, still.Interpolate(0.5f)));
        }

        // ------------------------------------------------------------------------------------------
        // (c) a rotation through 270 degrees needs the intermediate keys

        [TEST("AnimatedTransformNKey", "RotationThrough270Degrees")]
        public static void RotationThrough270Degrees()
        {
            Vector3f z = new Vector3f(0, 0, 1);
            Transform[] four = new Transform[4];
            for (int k = 0; k < 4; ++k)
                four[k] = Rotate(90f * k, z);
            AnimatedTransform at4 = new AnimatedTransform(four, new Float[] { 0f, 1f, 2f, 3f });
            AnimatedTransform at2 = new AnimatedTransform(four[0], 0f, four[3], 3f);

            Point3f p = new Point3f(1, 0, 0);
            Float worst4 = 0;
            for (int s = 0; s <= 60; ++s)
            {
                Float time = 3f * s / 60f;
                Float angle = Radians(90f * time);
                Point3f expected = new Point3f(std.cos(angle), std.sin(angle), 0);
                worst4 = std.max(worst4, Distance(at4.Apply(p, time), expected));
            }
            Report("270 degree rotation, 4 keys: largest distance from the exact circle: " + worst4);
            EXPECT_LT(worst4, 2e-5f);

            // The mid-segment times: 45, 135 and 225 degrees
            for (int seg = 0; seg < 3; ++seg)
            {
                Float angle = Radians(90f * (seg + 0.5f));
                Point3f onCircle = new Point3f(std.cos(angle), std.sin(angle), 0);
                EXPECT_LT(Distance(at4.Apply(p, seg + 0.5f), onCircle), 2e-5f);
            }

            // Two keys (0 and 270 degrees) take the short way, 90 degrees back: at the middle time they are at
            // -135 degrees, i.e. at the opposite side of the circle from where the 4 keys are at 135 degrees...
            Point3f mid2 = at2.Apply(p, 1.5f);
            Point3f mid4 = at4.Apply(p, 1.5f);
            Point3f shortArc = new Point3f(std.cos(Radians(-45f)), std.sin(Radians(-45f)), 0);
            EXPECT_LT(Distance(mid2, shortArc), 2e-5f);
            Float chord = Distance(mid2, mid4);
            Report("270 degree rotation: at the middle time the 2 key path is " + chord +
                   " (unit circle) away from the 4 key path (its chord to the 135 degree point is 2 sin(90) = 2)");
            EXPECT_GT(chord, 1.9f);

            // The 4 key rotation about a random axis
            RNG rng = new RNG();
            for (int i = 0; i < 20; ++i)
            {
                Float v = rng.Uniform<Float>();
                Float u = rng.Uniform<Float>();
                Vector3f axis = SampleUniformSphere(new Point2f(u, v));
                Transform[] keys = new Transform[4];
                for (int k = 0; k < 4; ++k)
                    keys[k] = Rotate(90f * k, axis);
                AnimatedTransform at = new AnimatedTransform(keys, new Float[] { 0f, 1f, 2f, 3f });
                for (int s = 0; s < 30; ++s)
                {
                    Float time = 3f * rng.Uniform<Float>();
                    Point3f q = RandomPoint(ref rng);
                    Point3f expected = Rotate(90f * time, axis).Apply(q);
                    EXPECT_LT(Distance(at.Apply(q, time), expected), 5e-4f);
                }
            }
        }

        [TEST("AnimatedTransformNKey", "RotationWithScaleAndTranslation")]
        public static void RotationWithScaleAndTranslation()
        {
            Vector3f z = new Vector3f(0, 0, 1);
            Float[] times = { 0f, 1f, 2.5f, 3f };
            Vector3f[] trans = { new Vector3f(0, 0, 0), new Vector3f(3, 1, 0), new Vector3f(3, 4, 2), new Vector3f(-2, 4, 2) };
            Vector3f[] scale = { new Vector3f(1, 1, 1), new Vector3f(2, 1, 0.5f), new Vector3f(2, 3, 0.5f), new Vector3f(1, 3, 2) };
            Float[] angle = { 0f, 100f, 200f, 330f };
            Transform[] keys = new Transform[4];
            for (int k = 0; k < 4; ++k)
                keys[k] = Translate(trans[k]) * Rotate(angle[k], z) * Scale(scale[k].x, scale[k].y, scale[k].z);
            AnimatedTransform at = new AnimatedTransform(keys, times);

            Float worst = 0;
            RNG rng = new RNG();
            for (int s = 0; s < 400; ++s)
            {
                Float time = 3f * rng.Uniform<Float>();
                int k = time < 1f ? 0 : (time < 2.5f ? 1 : 2);
                Float dt = (time - times[k]) / (times[k + 1] - times[k]);
                Vector3f tr = (1 - dt) * trans[k] + dt * trans[k + 1];
                Vector3f sc = (1 - dt) * scale[k] + dt * scale[k + 1];
                Float an = (1 - dt) * angle[k] + dt * angle[k + 1];
                Transform expected = Translate(tr) * Rotate(an, z) * Scale(sc.x, sc.y, sc.z);
                Point3f p = RandomPoint(ref rng);
                worst = std.max(worst, Distance(at.Apply(p, time), expected.Apply(p)) / (1f + Length(new Vector3f(p.x, p.y, p.z))));
            }
            Report("rotation + scale + translation, 4 keys: worst relative error " + worst);
            EXPECT_LT(worst, 2e-5f);
        }

        // Every segment takes the shortest arc between its two keys, whatever the sign of the quaternion
        // of the decomposition is
        [TEST("AnimatedTransformNKey", "EverySegmentTakesTheShortestArc")]
        public static void EverySegmentTakesTheShortestArc()
        {
            RNG rng = new RNG();
            for (int i = 0; i < 40; ++i)
            {
                int n = 3 + (int)rng.Uniform<uint32_t>(5);
                Transform[] keys = new Transform[n];
                Float[] times = new Float[n];
                for (int k = 0; k < n; ++k)
                {
                    keys[k] = MildTransform(ref rng);
                    times[k] = k;
                }
                AnimatedTransform at = new AnimatedTransform(keys, times);
                for (int k = 0; k < n - 1; ++k)
                {
                    // Each segment of the N key animation is the 2 key animation of its two keys
                    AnimatedTransform seg = new AnimatedTransform(keys[k], 0f, keys[k + 1], 1f);
                    for (int s = 1; s < 8; ++s)
                    {
                        Float dt = s / 8f;
                        Point3f p = RandomPoint(ref rng);
                        // (k and dt are exact floats: the segment parameter is dt itself, the results are the same bits)
                        EXPECT_TRUE(SameBits(at.Interpolate(k + dt), seg.Interpolate(dt)));
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // (d) negative determinants and (nearly) singular transformations: what pbrt does for two keys,
        // and that the N keys do the same per segment

        [TEST("AnimatedTransformNKey", "NegativeDeterminantFollowsTwoKeyBehaviour")]
        public static void NegativeDeterminantFollowsTwoKeyBehaviour()
        {
            Transform plain = Translate(new Vector3f(1, 2, 3)) * Rotate(30f, new Vector3f(0, 1, 0));
            Transform mirrored = plain * Scale(-1f, 1f, 1f);
            Transform other = Translate(new Vector3f(-2, 0, 1)) * Rotate(70f, new Vector3f(1, 0, 0));
            EXPECT_TRUE(mirrored.SwapsHandedness());

            // Per segment the N key animation is the two key animation (bit for bit)
            AnimatedTransform three = new AnimatedTransform(new Transform[] { plain, mirrored, other }, new Float[] { 0f, 1f, 2f });
            AnimatedTransform seg0 = new AnimatedTransform(plain, 0f, mirrored, 1f);
            AnimatedTransform seg1 = new AnimatedTransform(mirrored, 1f, other, 2f);
            for (int s = 1; s < 20; ++s)
            {
                Float dt = s / 20f;
                EXPECT_TRUE(SameBits(three.Interpolate(dt), seg0.Interpolate(dt)));
                EXPECT_TRUE(SameBits(three.Interpolate(1f + dt), seg1.Interpolate(1f + dt)));
            }

            // What pbrt does with a key that swaps the handedness: the polar decomposition keeps the sign (the
            // rotation is a reflection, "XXX TODO FIXME deal with flip" in Decompose) and the quaternion made
            // from it is not the matrix of the key. The key itself is returned exactly at the key time, but
            // just after it the animation jumps. Documented here, not fixed (two keys must stay bit identical).
            Point3f p = new Point3f(1, 2, 3);
            Float jump = Distance(seg0.Apply(p, 1f - 1e-5f), mirrored.Apply(p));
            Float jumpAtStart = Distance(seg0.Apply(p, 1e-5f), plain.Apply(p));
            Report("negative determinant key: jump of a point just before the mirrored key " + jump +
                   ", just after the plain key " + jumpAtStart + " (a point at distance " + Length(new Vector3f(1, 2, 3)) + ")");
            EXPECT_TRUE(SameBits(mirrored, seg0.Interpolate(1f)));
            EXPECT_TRUE(SameBits(plain, seg0.Interpolate(0f)));
        }

        [TEST("AnimatedTransformNKey", "SingularKeysAreAFatalError")]
        public static void SingularKeysAreAFatalError()
        {
            // A zero scale cannot be decomposed (the polar decomposition inverts the matrix): pbrt dies for
            // two keys and so it does for N keys
            Transform flat = Scale(1f, 1f, 0f);
            Transform ok = Translate(new Vector3f(1, 0, 0));
            bool twoThrew = false, threeThrew = false;
            try { new AnimatedTransform(ok, 0f, flat, 1f); }
            catch (FatalException) { twoThrew = true; }
            try { new AnimatedTransform(new Transform[] { ok, ok, flat }, new Float[] { 0f, 1f, 2f }); }
            catch (FatalException) { threeThrew = true; }
            EXPECT_TRUE(twoThrew);
            EXPECT_TRUE(threeThrew);
            // ... but only when the animation moves at all: with equal keys nothing is decomposed
            AnimatedTransform still = new AnimatedTransform(new Transform[] { flat, flat, flat }, new Float[] { 0f, 1f, 2f });
            EXPECT_FALSE(still.IsAnimated());

            // Nearly singular but invertible scales work and are continuous
            Transform thin = Scale(1f, 1f, 1e-3f);
            AnimatedTransform at = new AnimatedTransform(new Transform[] { ok, thin, ok * Rotate(40f, new Vector3f(1, 1, 0)) },
                                                         new Float[] { 0f, 1f, 2f });
            Point3f p = new Point3f(1, 2, 3);
            EXPECT_LT(Distance(at.Apply(p, 1f - 1e-4f), thin.Apply(p)), 0.05f);
            EXPECT_LT(Distance(at.Apply(p, 1f + 1e-4f), thin.Apply(p)), 0.05f);
        }

        // ------------------------------------------------------------------------------------------
        // (e) the bounds

        private struct BoundsStats
        {
            public long samples;
            public long outside;          // sampled points outside of the bounds by any amount
            public long outsideBeyondRounding;   // ... by more than the rounding error of the arithmetic
            public double worstOutside;   // relative to the diagonal of the bounds
            public double volumeRatio;    // bounds / sampled bounds
        }

        // The bounds are computed with derivative roots and the sampled points with a different sequence of
        // floating point operations: they agree up to the rounding error, a few units in the last place of the
        // largest coordinate involved.
        private static Float RoundingTolerance(Bounds3f bounds)
        {
            Float m = 0;
            m = std.max(m, std.max(std.abs(bounds.pMin.x), std.max(std.abs(bounds.pMin.y), std.abs(bounds.pMin.z))));
            m = std.max(m, std.max(std.abs(bounds.pMax.x), std.max(std.abs(bounds.pMax.y), std.abs(bounds.pMax.z))));
            return 8f * 1.1920929e-7f * m;
        }

        // Samples nTimes times in [t0, t1], transforms the points that points(tr) returns and counts the ones that
        // are outside of the bounds
        private static BoundsStats CheckBounds(Func<Float, Transform> interpolate, Float t0, Float t1, Bounds3f bounds,
                                               Point3f[] points, int nTimes, ref RNG rng)
        {
            Bounds3f sampled = new Bounds3f();
            BoundsStats stats = new BoundsStats();
            Float diagonal = Length(bounds.Diagonal());
            Float tolerance = RoundingTolerance(bounds);
            for (int i = 0; i < nTimes; ++i)
            {
                Float time = t0 + (t1 - t0) * ((i + rng.Uniform<Float>()) / nTimes);
                Transform tr = interpolate(time);
                for (int c = 0; c < points.Length; ++c)
                {
                    Point3f p = tr.Apply(points[c]);
                    sampled = Union(sampled, p);
                    Float outside = Outside(bounds, p);
                    ++stats.samples;
                    if (outside > 0)
                    {
                        ++stats.outside;
                        stats.worstOutside = Math.Max(stats.worstOutside, (double)outside / diagonal);
                        if (outside > tolerance)
                            ++stats.outsideBeyondRounding;
                    }
                }
            }
            stats.volumeRatio = BoxVolume(bounds) / BoxVolume(sampled);
            return stats;
        }

        private static Point3f[] Corners(Bounds3f box)
        {
            Point3f[] corners = new Point3f[8];
            for (int c = 0; c < 8; ++c)
                corners[c] = box.Corner(c);
            return corners;
        }

        private static void RandomKeys(ref RNG rng, int i, int n, out Transform[] keys, out Float[] times)
        {
            keys = new Transform[n];
            times = new Float[n];
            Float time = rng.Uniform<Float>();
            for (int k = 0; k < n; ++k)
            {
                keys[k] = (i % 3 == 0) ? WildTransform(ref rng) : MildTransform(ref rng);
                times[k] = time;
                time += 0.2f + 1.5f * rng.Uniform<Float>();
            }
        }

        [TEST("AnimatedTransformNKey", "MotionBoundsAreConservative")]
        public static void MotionBoundsAreConservative()
        {
            RNG rng = new RNG();
            BoundsStats total = new BoundsStats();
            double sumRatio = 0, worstRatio = 0;
            int nCases = 0;
            for (int i = 0; i < 24; ++i)
            {
                Transform[] keys;
                Float[] times;
                int n = 2 + (i % 7);
                RandomKeys(ref rng, i, n, out keys, out times);
                AnimatedTransform at = new AnimatedTransform(keys, times);
                Bounds3f box = RandomBox(ref rng);
                BoundsStats stats = CheckBounds((Float t) => at.Interpolate(t), times[0], times[n - 1], at.MotionBounds(box),
                                                Corners(box), 100000, ref rng);
                total.samples += stats.samples;
                total.outside += stats.outside;
                total.outsideBeyondRounding += stats.outsideBeyondRounding;
                total.worstOutside = Math.Max(total.worstOutside, stats.worstOutside);
                sumRatio += stats.volumeRatio;
                worstRatio = Math.Max(worstRatio, stats.volumeRatio);
                ++nCases;
            }
            Report("bounds (N keys): " + nCases + " random animations of 2..8 keys, " + total.samples +
                   " sampled box corners; " + total.outside + " outside by any amount (worst " + total.worstOutside +
                   " of the diagonal, i.e. rounding), " + total.outsideBeyondRounding + " outside by more than 8 ulp; " +
                   "bounds volume / sampled volume: mean " + (sumRatio / nCases) + ", worst " + worstRatio);

            // The same measure for the original two key code: the rounding level excesses are inherent to the method
            BoundsStats legacyTotal = new BoundsStats();
            for (int i = 0; i < 24; ++i)
            {
                Transform[] keys;
                Float[] times;
                RandomKeys(ref rng, i, 2, out keys, out times);
                LegacyAnimatedTransform legacy = new LegacyAnimatedTransform(keys[0], times[0], keys[1], times[1]);
                Bounds3f box = RandomBox(ref rng);
                BoundsStats stats = CheckBounds((Float t) => legacy.Interpolate(t), times[0], times[1], legacy.MotionBounds(box),
                                                Corners(box), 100000, ref rng);
                legacyTotal.samples += stats.samples;
                legacyTotal.outside += stats.outside;
                legacyTotal.outsideBeyondRounding += stats.outsideBeyondRounding;
                legacyTotal.worstOutside = Math.Max(legacyTotal.worstOutside, stats.worstOutside);
            }
            Report("bounds (two key code of pbrt-v4, before the change): " + legacyTotal.samples + " sampled box corners; " +
                   legacyTotal.outside + " outside by any amount (worst " + legacyTotal.worstOutside + " of the diagonal), " +
                   legacyTotal.outsideBeyondRounding + " outside by more than 8 ulp");

            EXPECT_EQ(0L, total.outsideBeyondRounding);
            EXPECT_EQ(0L, legacyTotal.outsideBeyondRounding);
            EXPECT_LT(worstRatio, 1.01);
        }

        [TEST("AnimatedTransformNKey", "BoundPointMotionIsConservative")]
        public static void BoundPointMotionIsConservative()
        {
            RNG rng = new RNG();
            BoundsStats total = new BoundsStats();
            for (int i = 0; i < 24; ++i)
            {
                Transform[] keys;
                Float[] times;
                int n = 2 + (i % 7);
                RandomKeys(ref rng, i, n, out keys, out times);
                AnimatedTransform at = new AnimatedTransform(keys, times);
                Point3f p = RandomPoint(ref rng);
                BoundsStats stats = CheckBounds((Float t) => at.Interpolate(t), times[0], times[n - 1], at.BoundPointMotion(p),
                                                new Point3f[] { p }, 100000, ref rng);
                total.samples += stats.samples;
                total.outside += stats.outside;
                total.outsideBeyondRounding += stats.outsideBeyondRounding;
                total.worstOutside = Math.Max(total.worstOutside, stats.worstOutside);
            }
            Report("point bounds: " + total.samples + " samples; " + total.outside + " outside by any amount (worst " +
                   total.worstOutside + " of the diagonal), " + total.outsideBeyondRounding + " outside by more than 8 ulp");
            EXPECT_EQ(0L, total.outsideBeyondRounding);
        }

        [TEST("AnimatedTransformNKey", "BoundsAreTheUnionOfTheSegmentBounds")]
        public static void BoundsAreTheUnionOfTheSegmentBounds()
        {
            RNG rng = new RNG();
            int nCases = 0;
            int nDifferent = 0;
            for (int i = 0; i < 60; ++i)
            {
                int n = 3 + (i % 6);
                Transform[] keys = new Transform[n];
                Float[] times = new Float[n];
                for (int k = 0; k < n; ++k)
                {
                    keys[k] = (i % 3 == 0) ? WildTransform(ref rng) : MildTransform(ref rng);
                    times[k] = 0.5f * k;
                }
                AnimatedTransform at = new AnimatedTransform(keys, times);
                Bounds3f box = RandomBox(ref rng);
                Point3f p = RandomPoint(ref rng);
                // The union of the bounds of the two key animations of the segments, with the original method
                Bounds3f union = new Bounds3f();
                Bounds3f unionPoint = new Bounds3f();
                for (int k = 0; k < n - 1; ++k)
                {
                    LegacyAnimatedTransform seg = new LegacyAnimatedTransform(keys[k], times[k], keys[k + 1], times[k + 1]);
                    union = Union(union, seg.MotionBounds(box));
                    unionPoint = Union(unionPoint, seg.BoundPointMotion(p));
                    // ... and the per segment bounds of the N key animation are those of the segment
                    EXPECT_TRUE(SameBits(seg.MotionBounds(box), at.SegmentMotionBounds(k, box)));
                    EXPECT_TRUE(SameBits(seg.BoundPointMotion(p), at.SegmentBoundPointMotion(k, p)));
                }
                if (!SameBits(union, at.MotionBounds(box)))
                    ++nDifferent;
                EXPECT_TRUE(SameBits(unionPoint, at.BoundPointMotion(p)));
                ++nCases;
            }
            Report("union of the segment bounds: " + nDifferent + " of " + nCases + " differ from the MotionBounds (bit for bit)");
            EXPECT_EQ(0, nDifferent);
        }

        [TEST("AnimatedTransformNKey", "BouncingBallBoundsContainTheBounce")]
        public static void BouncingBallBoundsContainTheBounce()
        {
            // The first and the last key are equal: two keys would not move at all
            Transform a = new Transform();
            Transform b = Translate(new Vector3f(0, 5, 0));
            AnimatedTransform at = new AnimatedTransform(new Transform[] { a, b, a }, new Float[] { 0f, 0.5f, 1f });
            Bounds3f box = new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1));
            Bounds3f bounds = at.MotionBounds(box);
            EXPECT_FLOAT_EQ(bounds.pMin.y, -1f);
            EXPECT_FLOAT_EQ(bounds.pMax.y, 6f);
            EXPECT_FLOAT_EQ(bounds.pMin.x, -1f);
            EXPECT_FLOAT_EQ(bounds.pMax.x, 1f);
            // The segments are tighter than the union
            Bounds3f s0 = at.SegmentMotionBounds(0, box);
            Bounds3f s1 = at.SegmentMotionBounds(1, box);
            EXPECT_FLOAT_EQ(s0.pMax.y, 6f);
            EXPECT_FLOAT_EQ(s0.pMin.y, -1f);
            EXPECT_FLOAT_EQ(s1.pMax.y, 6f);
        }

        [TEST("AnimatedTransformNKey", "SegmentBoundsAreTighterThanTheUnion")]
        public static void SegmentBoundsAreTighterThanTheUnion()
        {
            // An object that moves along x, then along y, then along z: each segment's box is a third of the union
            Transform[] keys =
            {
                new Transform(),
                Translate(new Vector3f(10, 0, 0)),
                Translate(new Vector3f(10, 10, 0)),
                Translate(new Vector3f(10, 10, 10)),
            };
            AnimatedTransform at = new AnimatedTransform(keys, new Float[] { 0f, 1f, 2f, 3f });
            Bounds3f box = new Bounds3f(new Point3f(0, 0, 0), new Point3f(1, 1, 1));
            double union = BoxVolume(at.MotionBounds(box));
            double sum = 0;
            for (int k = 0; k < 3; ++k)
                sum += BoxVolume(at.SegmentMotionBounds(k, box));
            Report("segment boxes of an L-shaped path: sum of the segment volumes " + sum + " vs union volume " + union);
            EXPECT_LT(sum, union / 2.0);
        }

        // ------------------------------------------------------------------------------------------
        // (f) the MotionBegin / MotionEnd directive

        private const string WorldPrefix =
            "Option \"rendercoordsys\" \"world\"\n" +
            "Film \"rgb\" \"integer xresolution\" 8 \"integer yresolution\" 8\n" +
            "Sampler \"independent\"\nCamera \"perspective\"\nWorldBegin\n";

        private static BasicScene ParseScene(string text)
        {
            BasicScene scene = new BasicScene();
            BasicSceneBuilder builder = new BasicSceneBuilder(scene);
            pbrt.ParseString(builder, text);
            return scene;
        }

        // The message of the error that parsing the text gives, or null if it parses
        private static string ParseError(string text)
        {
            try
            {
                ParseScene(text);
            }
            catch (FatalException e)
            {
                return e.Message;
            }
            return null;
        }

        private static void WithWorldRenderSpace(Action body)
        {
            RenderingCoordinateSystem saved = Options.renderingSpace;
            try
            {
                body();
            }
            finally
            {
                Options.renderingSpace = saved;
            }
        }

        private static Point3f Origin(AnimatedTransform at, Float time)
        {
            return at.Apply(new Point3f(0, 0, 0), time);
        }

        [TEST("MotionBlock", "ThreeTranslateKeysComposeWithTheCTM")]
        public static void ThreeTranslateKeysComposeWithTheCTM()
        {
            WithWorldRenderSpace(() =>
            {
                BasicScene scene = ParseScene(WorldPrefix +
                    "Translate 0 0 5\n" +
                    "MotionBegin [ 0 1 3 ]\n" +
                    "  Translate 0 0 0\n" +
                    "  Translate 2 0 0\n" +
                    "  Translate 2 3 0\n" +
                    "MotionEnd\n" +
                    "Shape \"sphere\"\n");
                ASSERT_EQ(1, scene.animatedShapes.Count);
                ASSERT_EQ(0, scene.shapes.Count);
                AnimatedTransform at = scene.animatedShapes[0].renderFromObject;
                EXPECT_EQ(3, at.KeyCount);
                EXPECT_EQ(0f, at.GetKeyTime(0));
                EXPECT_EQ(1f, at.GetKeyTime(1));
                EXPECT_EQ(3f, at.GetKeyTime(2));
                EXPECT_LT(Distance(Origin(at, 0f), new Point3f(0, 0, 5)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 1f), new Point3f(2, 0, 5)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 3f), new Point3f(2, 3, 5)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 0.5f), new Point3f(1, 0, 5)), 1e-5f);
                EXPECT_LT(Distance(Origin(at, 2f), new Point3f(2, 1.5f, 5)), 1e-5f);
            });
        }

        [TEST("MotionBlock", "RotateKeysTakeTheShortArcPerSegment")]
        public static void RotateKeysTakeTheShortArcPerSegment()
        {
            WithWorldRenderSpace(() =>
            {
                BasicScene scene = ParseScene(WorldPrefix +
                    "MotionBegin [ 0 1 2 3 ]\n" +
                    "  Rotate 0 0 0 1\n  Rotate 90 0 0 1\n  Rotate 180 0 0 1\n  Rotate 270 0 0 1\n" +
                    "MotionEnd\n" +
                    "Shape \"sphere\"\n");
                AnimatedTransform at = scene.animatedShapes[0].renderFromObject;
                EXPECT_EQ(4, at.KeyCount);
                Point3f p = new Point3f(1, 0, 0);
                for (int s = 0; s <= 30; ++s)
                {
                    Float time = 3f * s / 30f;
                    Float angle = Radians(90f * time);
                    EXPECT_LT(Distance(at.Apply(p, time), new Point3f(std.cos(angle), std.sin(angle), 0)), 3e-5f);
                }
            });
        }

        [TEST("MotionBlock", "OtherKindsOfCalls")]
        public static void OtherKindsOfCalls()
        {
            WithWorldRenderSpace(() =>
            {
                // Scale
                BasicScene scene = ParseScene(WorldPrefix +
                    "MotionBegin [ 0 1 ]\n Scale 1 1 1\n Scale 2 3 4\nMotionEnd\nShape \"sphere\"\n");
                AnimatedTransform at = scene.animatedShapes[0].renderFromObject;
                EXPECT_EQ(2, at.KeyCount);
                EXPECT_LT(Distance(at.Apply(new Point3f(1, 1, 1), 1f), new Point3f(2, 3, 4)), 1e-6f);

                // Transform replaces the CTM (like the call does outside of a block), ConcatTransform multiplies it
                scene = ParseScene(WorldPrefix +
                    "Translate 100 0 0\n" +
                    "MotionBegin [ 0 1 2 ]\n" +
                    " Transform [ 1 0 0 0  0 1 0 0  0 0 1 0  1 0 0 1 ]\n" +
                    " Transform [ 1 0 0 0  0 1 0 0  0 0 1 0  2 0 0 1 ]\n" +
                    " Transform [ 1 0 0 0  0 1 0 0  0 0 1 0  3 0 0 1 ]\n" +
                    "MotionEnd\nShape \"sphere\"\n");
                at = scene.animatedShapes[0].renderFromObject;
                EXPECT_LT(Distance(Origin(at, 0f), new Point3f(1, 0, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 2f), new Point3f(3, 0, 0)), 1e-6f);
                scene = ParseScene(WorldPrefix +
                    "Translate 100 0 0\n" +
                    "MotionBegin [ 0 1 2 ]\n" +
                    " ConcatTransform [ 1 0 0 0  0 1 0 0  0 0 1 0  1 0 0 1 ]\n" +
                    " ConcatTransform [ 1 0 0 0  0 1 0 0  0 0 1 0  2 0 0 1 ]\n" +
                    " ConcatTransform [ 1 0 0 0  0 1 0 0  0 0 1 0  3 0 0 1 ]\n" +
                    "MotionEnd\nShape \"sphere\"\n");
                at = scene.animatedShapes[0].renderFromObject;
                EXPECT_LT(Distance(Origin(at, 0f), new Point3f(101, 0, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 2f), new Point3f(103, 0, 0)), 1e-6f);

                // LookAt
                scene = ParseScene(WorldPrefix +
                    "MotionBegin [ 0 1 ]\n LookAt 0 0 -5  0 0 0  0 1 0\n LookAt 0 0 -10  0 0 0  0 1 0\nMotionEnd\nShape \"sphere\"\n");
                at = scene.animatedShapes[0].renderFromObject;
                EXPECT_LT(Distance(Origin(at, 0f), new Point3f(0, 0, 5)), 1e-5f);
                EXPECT_LT(Distance(Origin(at, 1f), new Point3f(0, 0, 10)), 1e-5f);
            });
        }

        [TEST("MotionBlock", "ActiveTransformAfterTheBlock")]
        public static void ActiveTransformAfterTheBlock()
        {
            WithWorldRenderSpace(() =>
            {
                string block = "MotionBegin [ 0 1 2 ]\n Translate 0 0 0\n Translate 0 1 0\n Translate 0 2 0\nMotionEnd\n";
                // StartTime changes the first key only, EndTime the last one, All every key
                BasicScene scene = ParseScene(WorldPrefix + block +
                    "ActiveTransform StartTime\nTranslate 10 0 0\nActiveTransform All\nShape \"sphere\"\n");
                AnimatedTransform at = scene.animatedShapes[0].renderFromObject;
                EXPECT_LT(Distance(Origin(at, 0f), new Point3f(10, 0, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 1f), new Point3f(0, 1, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 2f), new Point3f(0, 2, 0)), 1e-6f);

                scene = ParseScene(WorldPrefix + block +
                    "ActiveTransform EndTime\nTranslate 10 0 0\nActiveTransform All\nShape \"sphere\"\n");
                at = scene.animatedShapes[0].renderFromObject;
                EXPECT_LT(Distance(Origin(at, 0f), new Point3f(0, 0, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 1f), new Point3f(0, 1, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 2f), new Point3f(10, 2, 0)), 1e-6f);

                scene = ParseScene(WorldPrefix + block + "Translate 10 0 0\nShape \"sphere\"\n");
                at = scene.animatedShapes[0].renderFromObject;
                EXPECT_LT(Distance(Origin(at, 0f), new Point3f(10, 0, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 1f), new Point3f(10, 1, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(at, 2f), new Point3f(10, 2, 0)), 1e-6f);

                // ActiveTransform has no effect between MotionBegin and MotionEnd
                scene = ParseScene(WorldPrefix + "ActiveTransform StartTime\n" + block + "ActiveTransform All\nShape \"sphere\"\n");
                at = scene.animatedShapes[0].renderFromObject;
                EXPECT_EQ(3, at.KeyCount);
                EXPECT_LT(Distance(Origin(at, 2f), new Point3f(0, 2, 0)), 1e-6f);
            });
        }

        [TEST("MotionBlock", "AttributeBeginEndNamedCoordinateSystemsAndInstances")]
        public static void AttributeBeginEndNamedCoordinateSystemsAndInstances()
        {
            WithWorldRenderSpace(() =>
            {
                string block = "MotionBegin [ 0 1 2 ]\n Translate 0 0 0\n Translate 0 1 0\n Translate 0 2 0\nMotionEnd\n";

                // The keys live in the attribute scope: after AttributeEnd the CTM is static again
                BasicScene scene = ParseScene(WorldPrefix +
                    "AttributeBegin\n" + block + "Shape \"sphere\"\nAttributeEnd\nShape \"sphere\"\n");
                EXPECT_EQ(1, scene.animatedShapes.Count);
                EXPECT_EQ(1, scene.shapes.Count);

                // A block inside an attribute scope that is inside a block's result...
                scene = ParseScene(WorldPrefix + block +
                    "AttributeBegin\nTranslate 0 0 7\nShape \"sphere\"\nAttributeEnd\nShape \"sphere\"\n");
                EXPECT_EQ(2, scene.animatedShapes.Count);
                EXPECT_LT(Distance(Origin(scene.animatedShapes[0].renderFromObject, 1f), new Point3f(0, 1, 7)), 1e-6f);
                EXPECT_LT(Distance(Origin(scene.animatedShapes[1].renderFromObject, 1f), new Point3f(0, 1, 0)), 1e-6f);

                // CoordinateSystem keeps all the keys, CoordSysTransform brings them back
                scene = ParseScene(WorldPrefix +
                    "AttributeBegin\n" + block + "CoordinateSystem \"moving\"\nAttributeEnd\n" +
                    "Translate 5 0 0\nCoordSysTransform \"moving\"\nShape \"sphere\"\n");
                ASSERT_EQ(1, scene.animatedShapes.Count);
                AnimatedTransform at = scene.animatedShapes[0].renderFromObject;
                EXPECT_EQ(3, at.KeyCount);
                EXPECT_LT(Distance(Origin(at, 2f), new Point3f(0, 2, 0)), 1e-6f);
                // ... and a CoordSysTransform of a static system makes the CTM static again
                scene = ParseScene(WorldPrefix + block + "CoordSysTransform \"world\"\nShape \"sphere\"\n");
                EXPECT_EQ(0, scene.animatedShapes.Count);
                EXPECT_EQ(1, scene.shapes.Count);

                // Instances: ObjectInstance takes the keys of the CTM
                scene = ParseScene(WorldPrefix +
                    "ObjectBegin \"ball\"\nShape \"sphere\"\nObjectEnd\n" + block + "ObjectInstance \"ball\"\n");
                ASSERT_EQ(1, scene.instances.Count);
                EXPECT_TRUE(scene.instances[0].renderFromInstanceAnim != null);
                EXPECT_EQ(3, scene.instances[0].renderFromInstanceAnim.KeyCount);
                EXPECT_LT(Distance(Origin(scene.instances[0].renderFromInstanceAnim, 1.5f), new Point3f(0, 1.5f, 0)), 1e-5f);

                // The shapes of an object definition may have keys too
                scene = ParseScene(WorldPrefix + "ObjectBegin \"ball\"\n" + block + "Shape \"sphere\"\nObjectEnd\n");
                EXPECT_TRUE(true);
            });
        }

        [TEST("MotionBlock", "CameraWithKeysInTheOptionsBlock")]
        public static void CameraWithKeysInTheOptionsBlock()
        {
            WithWorldRenderSpace(() =>
            {
                // The CTM is the camera from world: the camera is at the opposite of the translation
                BasicScene scene = ParseScene(
                    "Option \"rendercoordsys\" \"world\"\n" +
                    "MotionBegin [ 0 1 2 ]\n Translate 0 0 0\n Translate -1 0 0\n Translate -1 -1 0\nMotionEnd\n" +
                    "Film \"rgb\" \"integer xresolution\" 8 \"integer yresolution\" 8\nSampler \"independent\"\n" +
                    "Camera \"perspective\"\nWorldBegin\n");
                CameraTransform ct = scene.GetCamera().GetCameraTransform();
                AnimatedTransform rfc = ct.RenderFromCamera();
                EXPECT_EQ(3, rfc.KeyCount);
                EXPECT_LT(Distance(Origin(rfc, 0f), new Point3f(0, 0, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(rfc, 1f), new Point3f(1, 0, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(rfc, 2f), new Point3f(1, 1, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(rfc, 1.5f), new Point3f(1, 0.5f, 0)), 1e-6f);
            });
        }

        [TEST("MotionBlock", "CTMBeforeTheBlockMayBeAnimated")]
        public static void CTMBeforeTheBlockMayBeAnimated()
        {
            WithWorldRenderSpace(() =>
            {
                // The CTM before the block has two keys (times 0 and 2): translation 0 at t = 0 and (4, 0, 0) at t = 2.
                // The key i of the block is the CTM at the time of the key times the call of the key.
                BasicScene scene = ParseScene(
                    "Option \"rendercoordsys\" \"world\"\n" +
                    "TransformTimes 0 2\n" +
                    "ActiveTransform EndTime\nTranslate 4 0 0\nActiveTransform All\n" +
                    "MotionBegin [ 0 1 2 ]\n Translate 0 0 0\n Translate 0 1 0\n Translate 0 0 3\nMotionEnd\n" +
                    "Film \"rgb\" \"integer xresolution\" 8 \"integer yresolution\" 8\nSampler \"independent\"\n" +
                    "Camera \"perspective\"\nWorldBegin\n");
                AnimatedTransform rfc = scene.GetCamera().GetCameraTransform().RenderFromCamera();
                EXPECT_EQ(3, rfc.KeyCount);
                // The camera is at the inverse of the CTM: -(0,0,0), -((2,0,0) + (0,1,0)), -((4,0,0) + (0,0,3))
                EXPECT_LT(Distance(Origin(rfc, 0f), new Point3f(0, 0, 0)), 1e-6f);
                EXPECT_LT(Distance(Origin(rfc, 1f), new Point3f(-2, -1, 0)), 1e-5f);
                EXPECT_LT(Distance(Origin(rfc, 2f), new Point3f(-4, 0, -3)), 1e-5f);
            });
        }

        [TEST("MotionBlock", "TwoKeyBehaviourIsUnchanged")]
        public static void TwoKeyBehaviourIsUnchanged()
        {
            WithWorldRenderSpace(() =>
            {
                BasicScene scene = ParseScene(
                    "Option \"rendercoordsys\" \"world\"\n" +
                    "Film \"rgb\" \"integer xresolution\" 8 \"integer yresolution\" 8\nSampler \"independent\"\n" +
                    "TransformTimes 0.25 0.75\nCamera \"perspective\"\nWorldBegin\n" +
                    "ActiveTransform EndTime\nTranslate 3 0 0\nActiveTransform All\nShape \"sphere\"\n");
                AnimatedTransform at = scene.animatedShapes[0].renderFromObject;
                EXPECT_EQ(2, at.KeyCount);
                EXPECT_EQ(0.25f, at.startTime);
                EXPECT_EQ(0.75f, at.endTime);
                EXPECT_LT(Distance(Origin(at, 0.5f), new Point3f(1.5f, 0, 0)), 1e-6f);
            });
        }

        [TEST("MotionBlock", "SyntaxAndNestingErrors")]
        public static void SyntaxAndNestingErrors()
        {
            WithWorldRenderSpace(() =>
            {
                string error;

                // Nested blocks
                error = ParseError(WorldPrefix + "MotionBegin [ 0 1 ]\nMotionBegin [ 0 1 ]\n");
                ASSERT_TRUE(error != null);
                EXPECT_TRUE(error.Contains("MotionBegin"));

                // Fewer transformation calls than times
                error = ParseError(WorldPrefix + "MotionBegin [ 0 1 2 ]\nTranslate 0 0 0\nTranslate 1 0 0\nMotionEnd\n");
                ASSERT_TRUE(error != null);
                EXPECT_TRUE(error.Contains("3 times") && error.Contains("2 transformation calls"));

                // More calls than times
                error = ParseError(WorldPrefix + "MotionBegin [ 0 1 ]\nTranslate 0 0 0\nTranslate 1 0 0\nTranslate 2 0 0\nMotionEnd\n");
                ASSERT_TRUE(error != null);
                EXPECT_TRUE(error.Contains("2 times"));

                // Times that do not increase
                error = ParseError(WorldPrefix + "MotionBegin [ 0 2 1 ]\n");
                ASSERT_TRUE(error != null);
                EXPECT_TRUE(error.Contains("increase strictly"));
                error = ParseError(WorldPrefix + "MotionBegin [ 0 1 1 ]\n");
                ASSERT_TRUE(error != null);
                EXPECT_TRUE(error.Contains("increase strictly"));
                error = ParseError(WorldPrefix + "MotionBegin [ 1 0 ]\n");
                ASSERT_TRUE(error != null);

                // Fewer than two times
                error = ParseError(WorldPrefix + "MotionBegin [ 0 ]\n");
                ASSERT_TRUE(error != null);
                EXPECT_TRUE(error.Contains("at least two"));
                error = ParseError(WorldPrefix + "MotionBegin [ ]\n");
                ASSERT_TRUE(error != null);

                // Different kinds of calls
                error = ParseError(WorldPrefix + "MotionBegin [ 0 1 ]\nTranslate 0 0 0\nScale 1 1 1\nMotionEnd\n");
                ASSERT_TRUE(error != null);
                EXPECT_TRUE(error.Contains("same kind"));

                // MotionEnd without MotionBegin
                error = ParseError(WorldPrefix + "MotionEnd\n");
                ASSERT_TRUE(error != null);
                EXPECT_TRUE(error.Contains("MotionEnd without"));

                // No MotionEnd
                error = ParseError(WorldPrefix + "MotionBegin [ 0 1 ]\nTranslate 0 0 0\nTranslate 1 0 0\n");
                ASSERT_TRUE(error != null);

                // The times need brackets
                error = ParseError(WorldPrefix + "MotionBegin 0 1\nTranslate 0 0 0\nTranslate 1 0 0\nMotionEnd\n");
                ASSERT_TRUE(error != null);

                // Directives that are not transformations
                string[] forbidden =
                {
                    "Shape \"sphere\"", "AttributeBegin", "AttributeEnd", "ObjectBegin \"o\"", "ObjectInstance \"o\"",
                    "CoordSysTransform \"world\"", "CoordinateSystem \"c\"", "ActiveTransform All", "Material \"diffuse\"",
                    "WorldBegin", "MotionBegin [ 0 1 ]", "LightSource \"point\"", "Include \"x.pbrt\"",
                };
                foreach (string directive in forbidden)
                {
                    error = ParseError(WorldPrefix + "MotionBegin [ 0 1 ]\nTranslate 0 0 0\n" + directive + "\n");
                    ASSERT_TRUE(error != null);
                    EXPECT_TRUE(error.Contains("not allowed between MotionBegin and MotionEnd"));
                }

                // A block in a world block with the minimal number of calls is fine
                error = ParseError(WorldPrefix + "MotionBegin [ 0 1 ]\nTranslate 0 0 0\nTranslate 1 0 0\nMotionEnd\n");
                EXPECT_TRUE(error == null);
            });
        }

        // ------------------------------------------------------------------------------------------
        // (g) rays against a moving sphere: the primitives and the BVH

        private static Primitive MovingSphere(Transform[] keys, Float[] times, Float radius)
        {
            Transform id = new Transform();
            Shape sphere = new Sphere(id, id, false, radius, -radius, radius, 360);
            Primitive prim = new SimplePrimitive(sphere, null);
            return new AnimatedPrimitive(prim, new AnimatedTransform(keys, times));
        }

        [TEST("AnimatedTransformNKey", "RaysHitAMovingSphere")]
        public static void RaysHitAMovingSphere()
        {
            // A sphere that moves from (0,0,0) to (4,0,0) to (4,4,0) (three keys, a piecewise linear path)
            Float radius = 0.5f;
            Transform[] keys =
            {
                new Transform(),
                Translate(new Vector3f(4, 0, 0)),
                Translate(new Vector3f(4, 4, 0)),
            };
            Float[] times = { 0f, 1f, 3f };
            Primitive moving = MovingSphere(keys, times, radius);

            // A decoy and the BVH that holds both
            Transform farAway = Translate(new Vector3f(-20, 0, 0));
            Shape decoyShape = new Sphere(farAway, Inverse(farAway), false, 1f, -1f, 1f, 360);
            List<Primitive> prims = new List<Primitive>();
            prims.Add(new SimplePrimitive(decoyShape, null));
            prims.Add(moving);
            BVHAggregate bvh = new BVHAggregate(prims);

            // The path: at time t the center is c(t)
            Func<Float, Point3f> center = (Float t) =>
            {
                if (t <= 1f)
                    return new Point3f(4f * t, 0, 0);
                return new Point3f(4f, 4f * (t - 1f) / 2f, 0);
            };

            Float offset = 0.2f;
            Float expectedT = 10f - std.sqrt(radius * radius - offset * offset);
            int nHits = 0;
            for (int i = 0; i <= 40; ++i)
            {
                Float time = 3f * i / 40f;
                Point3f c = center(time);
                // Aimed at the center of the sphere at that time, with an offset in y (x for the vertical path)
                Point3f origin = new Point3f(c.x + (time <= 1f ? 0f : offset), c.y + (time <= 1f ? offset : 0f), -10f);
                Ray ray = new Ray(origin, new Vector3f(0, 0, 1), time);

                pstd.optional<ShapeIntersection> viaBvh = bvh.Intersect(ray, Infinity);
                pstd.optional<ShapeIntersection> direct = moving.Intersect(ray, Infinity);
                ASSERT_TRUE(viaBvh.has_value());
                ASSERT_TRUE(direct.has_value());
                EXPECT_LT(std.abs(viaBvh.value().tHit - expectedT), 2e-4f);
                EXPECT_EQ(direct.value().tHit, viaBvh.value().tHit);
                EXPECT_TRUE(bvh.IntersectP(ray, Infinity));
                ++nHits;

                // The same ray at a time when the sphere is elsewhere (more than a diameter away) misses
                Float otherTime = time + 0.5f > 3f ? time - 0.5f : time + 0.5f;
                Point3f cOther = center(otherTime);
                if (Distance(new Point3f(cOther.x, cOther.y, 0), new Point3f(c.x, c.y, 0)) > 1.5f)
                {
                    Ray late = new Ray(origin, new Vector3f(0, 0, 1), otherTime);
                    EXPECT_FALSE(bvh.Intersect(late, Infinity).has_value());
                    EXPECT_FALSE(bvh.IntersectP(late, Infinity));
                }
            }
            EXPECT_EQ(41, nHits);

            // Outside the key times the sphere stays at the first / last key
            Ray before = new Ray(new Point3f(0, offset, -10f), new Vector3f(0, 0, 1), -5f);
            EXPECT_TRUE(bvh.Intersect(before, Infinity).has_value());
            Ray after = new Ray(new Point3f(4f + offset, 4f, -10f), new Vector3f(0, 0, 1), 9f);
            EXPECT_TRUE(bvh.Intersect(after, Infinity).has_value());
        }

        [TEST("AnimatedTransformNKey", "TheBVHUsesTheBoundsOfAllTheSegments")]
        public static void TheBVHUsesTheBoundsOfAllTheSegments()
        {
            // A bouncing ball whose first and last key are equal: only the middle key leaves the start box
            Float radius = 0.5f;
            Transform[] keys = { new Transform(), Translate(new Vector3f(0, 8, 0)), new Transform() };
            Primitive moving = MovingSphere(keys, new Float[] { 0f, 0.5f, 1f }, radius);
            Bounds3f b = moving.Bounds();
            EXPECT_LE(b.pMin.y, -radius + 1e-5f);
            EXPECT_GE(b.pMax.y, 8f + radius - 1e-5f);

            Transform farAway = Translate(new Vector3f(-20, 0, 0));
            List<Primitive> prims = new List<Primitive>();
            prims.Add(new SimplePrimitive(new Sphere(farAway, Inverse(farAway), false, 1f, -1f, 1f, 360), null));
            prims.Add(moving);
            BVHAggregate bvh = new BVHAggregate(prims);
            // At the top of the bounce (t = 0.5) a ray at y = 8 hits, at t = 0 it does not
            Ray top = new Ray(new Point3f(0, 8, -10), new Vector3f(0, 0, 1), 0.5f);
            Ray ground = new Ray(new Point3f(0, 8, -10), new Vector3f(0, 0, 1), 0f);
            EXPECT_TRUE(bvh.Intersect(top, Infinity).has_value());
            EXPECT_FALSE(bvh.Intersect(ground, Infinity).has_value());
            // At t = 0.25 the ball is at y = 4
            Ray quarter = new Ray(new Point3f(0, 4, -10), new Vector3f(0, 0, 1), 0.25f);
            EXPECT_TRUE(bvh.Intersect(quarter, Infinity).has_value());
        }

        // ------------------------------------------------------------------------------------------
        // (h) a camera with three keys

        private static void CameraImageShift(bool cameraWorldSpace)
        {
            RenderingCoordinateSystem saved = Options.renderingSpace;
            try
            {
                // The camera is at (0,0,0) at t = 0, (1,0,0) at t = 1 and (1,1,0) at t = 2, looking along +z
                string options = "Option \"rendercoordsys\" \"" + (cameraWorldSpace ? "cameraworld" : "world") + "\"\n";
                BasicScene scene = ParseScene(
                    options +
                    "MotionBegin [ 0 1 2 ]\n Translate 0 0 0\n Translate -1 0 0\n Translate -1 -1 0\nMotionEnd\n" +
                    "Film \"rgb\" \"integer xresolution\" 64 \"integer yresolution\" 64\nSampler \"independent\"\n" +
                    "Camera \"perspective\" \"float fov\" 90\nWorldBegin\n");
                Camera camera = scene.GetCamera();
                CameraTransform ct = camera.GetCameraTransform();
                Transform worldFromRender = ct.WorldFromRender();

                // The point P in the world, at depth 10
                Point3f P = new Point3f(0.5f, 0.25f, 10f);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
                Float[] camX = { 0f, 0.5f, 1f, 1f, 1f };
                Float[] camY = { 0f, 0f, 0f, 0.5f, 1f };
                Float[] times = { 0f, 0.5f, 1f, 1.5f, 2f };
                Float firstRasterX = 0, firstRasterY = 0;  // raster position of P at the first time
                Float worstShift = 0;
                for (int ti = 0; ti < times.Length; ++ti)
                {
                    // The pixel whose ray passes closest to P
                    Float bestDistance = Infinity;
                    int bestX = -1, bestY = -1;
                    Point3f ro0 = new Point3f();
                    for (int y = 0; y < 64; ++y)
                    {
                        for (int x = 0; x < 64; ++x)
                        {
                            CameraSample cs = new CameraSample();
                            cs.pFilm = new Point2f(x + 0.5f, y + 0.5f);
                            cs.time = times[ti];
                            cs.pLens = new Point2f(0.5f, 0.5f);
                            cs.filterWeight = 1;
                            pstd.optional<CameraRay> cr = camera.GenerateRay(cs, ref lambda);
                            ASSERT_TRUE(cr.has_value());
                            Ray r = cr.value().ray;
                            Point3f o = worldFromRender.Apply(r.o);
                            Vector3f d = Normalize(worldFromRender.Apply(r.d));
                            if (x == 0 && y == 0)
                            {
                                // The origin of the ray is the position of the camera at that time
                                EXPECT_LT(Distance(o, new Point3f(camX[ti], camY[ti], 0)), 2e-5f);
                            }
                            ro0 = o;
                            Vector3f toP = P - o;
                            Float along = Dot(toP, d);
                            Float dist = Length(toP - along * d);
                            if (dist < bestDistance)
                            {
                                bestDistance = dist;
                                bestX = x;
                                bestY = y;
                            }
                        }
                    }
                    // Refine to a twentieth of a pixel around the best pixel
                    Float bestFx = bestX + 0.5f, bestFy = bestY + 0.5f;
                    for (int iy = -20; iy <= 20; ++iy)
                    {
                        for (int ix = -20; ix <= 20; ++ix)
                        {
                            CameraSample cs = new CameraSample();
                            cs.pFilm = new Point2f(bestX + 0.5f + ix * 0.05f, bestY + 0.5f + iy * 0.05f);
                            cs.time = times[ti];
                            cs.pLens = new Point2f(0.5f, 0.5f);
                            cs.filterWeight = 1;
                            Ray r = camera.GenerateRay(cs, ref lambda).value().ray;
                            Point3f o = worldFromRender.Apply(r.o);
                            Vector3f d = Normalize(worldFromRender.Apply(r.d));
                            Vector3f toP = P - o;
                            Float dist = Length(toP - Dot(toP, d) * d);
                            if (dist < bestDistance)
                            {
                                bestDistance = dist;
                                bestFx = cs.pFilm.x;
                                bestFy = cs.pFilm.y;
                            }
                        }
                    }
                    if (ti == 0)
                    {
                        firstRasterX = bestFx;
                        firstRasterY = bestFy;
                    }
                    // fov 90 over 64 pixels: 32 pixels per unit of x / depth. Moving the camera by +dx moves the
                    // image of P by -dx * 32 / depth in raster x; raster y grows downwards while camera y goes up, so
                    // moving the camera up by dy moves the image of P by +dy * 32 / depth in raster y
                    Float expectedShiftX = -camX[ti] * 32f / P.z;
                    Float expectedShiftY = camY[ti] * 32f / P.z;
                    Float shiftX = bestFx - firstRasterX;
                    Float shiftY = bestFy - firstRasterY;
                    worstShift = std.max(worstShift, std.max(std.abs(shiftX - expectedShiftX), std.abs(shiftY - expectedShiftY)));
                    EXPECT_LE(std.abs(shiftX - expectedShiftX), 0.15f);
                    EXPECT_LE(std.abs(shiftY - expectedShiftY), 0.15f);
                }
                Report("camera with 3 keys (" + (cameraWorldSpace ? "camera-world" : "world") +
                       " render space): the worst error of the image shift of a point is " + worstShift + " pixels");
            }
            finally
            {
                Options.renderingSpace = saved;
            }
        }

        [TEST("AnimatedTransformNKey", "CameraWithThreeKeysShiftsTheImageWorldSpace")]
        public static void CameraWithThreeKeysShiftsTheImageWorldSpace()
        {
            CameraImageShift(false);
        }

        [TEST("AnimatedTransformNKey", "CameraWithThreeKeysShiftsTheImageCameraWorldSpace")]
        public static void CameraWithThreeKeysShiftsTheImageCameraWorldSpace()
        {
            // The default render space: the render space is translated to the position of the camera at the
            // middle time; the keys of the render from camera are moved by that translation
            CameraImageShift(true);
        }

        [TEST("AnimatedTransformNKey", "PreMultiplyKeepsTheTimes")]
        public static void PreMultiplyKeepsTheTimes()
        {
            Transform[] keys = { new Transform(), Translate(new Vector3f(1, 0, 0)), Translate(new Vector3f(1, 2, 0)) };
            AnimatedTransform at = new AnimatedTransform(keys, new Float[] { 0.5f, 1.5f, 4f });
            Transform shift = Translate(new Vector3f(0, 0, 10));
            AnimatedTransform moved = at.PreMultiply(shift);
            EXPECT_EQ(3, moved.KeyCount);
            for (int k = 0; k < 3; ++k)
            {
                EXPECT_EQ(at.GetKeyTime(k), moved.GetKeyTime(k));
                EXPECT_TRUE(SameBits(shift * keys[k], moved.GetKeyTransform(k)));
            }
            EXPECT_LT(Distance(moved.Apply(new Point3f(0, 0, 0), 1f), new Point3f(0.5f, 0, 10)), 1e-6f);
        }

        // ------------------------------------------------------------------------------------------
        // Timing: Interpolate and the motion bounds, two keys against the code before the change, and per extra key

        private static double TimeIt(Action body, int repeats)
        {
            body();
            Stopwatch sw = Stopwatch.StartNew();
            for (int r = 0; r < repeats; ++r)
                body();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds * 1e6 / repeats;   // nanoseconds
        }

        [TEST("AnimatedTransformNKey", "BenchmarkInterpolateAndBounds")]
        public static void BenchmarkInterpolateAndBounds()
        {
            RNG rng = new RNG();
            Transform t0 = MildTransform(ref rng);
            Transform t1 = MildTransform(ref rng);
            LegacyAnimatedTransform legacy = new LegacyAnimatedTransform(t0, 0f, t1, 1f);
            AnimatedTransform current = new AnimatedTransform(t0, 0f, t1, 1f);
            Bounds3f box = new Bounds3f(new Point3f(-1, -2, -3), new Point3f(2, 1, 1));
            const int nTimes = 4096;
            Float[] times = new Float[nTimes];
            for (int i = 0; i < nTimes; ++i)
                times[i] = rng.Uniform<Float>();

            Float sink = 0;
            double interpolateLegacy = 0, interpolateCurrent = 0, boundsLegacy = 0, boundsCurrent = 0;
            // Alternate several rounds and keep the best, to be robust against noise
            interpolateLegacy = interpolateCurrent = boundsLegacy = boundsCurrent = double.MaxValue;
            for (int round = 0; round < 5; ++round)
            {
                interpolateLegacy = Math.Min(interpolateLegacy, TimeIt(() =>
                {
                    Float s = 0;
                    for (int i = 0; i < nTimes; ++i)
                        s += legacy.Interpolate(times[i]).GetMatrix()[0][3];
                    sink += s;
                }, 20) / nTimes);
                interpolateCurrent = Math.Min(interpolateCurrent, TimeIt(() =>
                {
                    Float s = 0;
                    for (int i = 0; i < nTimes; ++i)
                        s += current.Interpolate(times[i]).GetMatrix()[0][3];
                    sink += s;
                }, 20) / nTimes);
                boundsLegacy = Math.Min(boundsLegacy, TimeIt(() => { sink += legacy.MotionBounds(box).pMax.x; }, 2000));
                boundsCurrent = Math.Min(boundsCurrent, TimeIt(() => { sink += current.MotionBounds(box).pMax.x; }, 2000));
            }
            Report("benchmark Interpolate, 2 keys: legacy " + interpolateLegacy.ToString("F1") + " ns, new " +
                   interpolateCurrent.ToString("F1") + " ns");
            Report("benchmark MotionBounds, 2 keys: legacy " + boundsLegacy.ToString("F0") + " ns, new " +
                   boundsCurrent.ToString("F0") + " ns");

            // Per key
            foreach (int n in new int[] { 2, 3, 4, 8, 16, 32 })
            {
                Transform[] keys = new Transform[n];
                Float[] keyTimes = new Float[n];
                for (int k = 0; k < n; ++k)
                {
                    keys[k] = MildTransform(ref rng);
                    keyTimes[k] = k;
                }
                AnimatedTransform at = new AnimatedTransform(keys, keyTimes);
                Float scale = n - 1;
                double interp = double.MaxValue, bnd = double.MaxValue;
                for (int round = 0; round < 3; ++round)
                {
                    interp = Math.Min(interp, TimeIt(() =>
                    {
                        Float s = 0;
                        for (int i = 0; i < nTimes; ++i)
                            s += at.Interpolate(times[i] * scale).GetMatrix()[0][3];
                        sink += s;
                    }, 20) / nTimes);
                    bnd = Math.Min(bnd, TimeIt(() => { sink += at.MotionBounds(box).pMax.x; }, 500));
                }
                Report("benchmark " + n + " keys: Interpolate " + interp.ToString("F1") + " ns, MotionBounds " +
                       (bnd / 1000.0).ToString("F1") + " us (" + (n - 1) + " segments)");
            }
            if (sink == 12345.678f)
                Console.WriteLine();
        }
    }
}
