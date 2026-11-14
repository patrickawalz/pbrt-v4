// (not in pbrt-v4) featureline_test.cs
//
// The test for a feature line by a ray (featureline.cs): the outline of a sphere is a line as wide as asked, against nothing
// and against another object; without the lines between objects the step in depth still draws it; the threshold on the
// normals decides the lines on a smooth surface; nothing is drawn away from the features.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class FeatureLineTests
    {
        static FeatureLineSettings Settings(bool objects, Float degrees, Float depth)
        {
            FeatureLineSettings s = new FeatureLineSettings();
            s.width = 2;
            s.samples = 16;
            s.objectLines = objects;
            s.cosNormal = degrees > 0 ? MathF.Cos(Radians(degrees)) : -2;
            s.depthThreshold = depth;
            s.bounces = FeatureLineBounces.Specular;
            return s;
        }

        // How far across the outline of the unit sphere (at x = 1, seen along -z by parallel rays) a line is found
        static void Outline(Primitive scene, FeatureLineSettings settings, Float radius, out double from, out double to, out int found)
        {
            Outline(scene, settings, radius, out from, out to, out found, out int _);
        }

        // (`missed`: the rays that find no line although the circle of the stencil crosses the outline by more than the angle
        // between two of its rays, whatever its turn)
        static void Outline(Primitive scene, FeatureLineSettings settings, Float radius, out double from, out double to, out int found, out int missed)
        {
            missed = 0;
            Float sure = radius * MathF.Cos(Pi / settings.samples) * 0.98f;
            from = Double.PositiveInfinity;
            to = Double.NegativeInfinity;
            found = 0;
            RNG rng = new RNG(7);
            for (int i = 0; i < 4000; ++i)
            {
                Float x = 0.5f + (i + 0.5f) / 4000;
                Ray ray = new Ray(new Point3f(x, 0.013f, 10), new Vector3f(0, 0, -1));
                if (FeatureLines.Detect(scene, ray, scene.Intersect(ray, Infinity), settings, radius, 0, rng.Uniform<Float>(), true))
                {
                    ++found;
                    from = Math.Min(from, x);
                    to = Math.Max(to, x);
                }
                else if (Math.Abs(x - 1) < sure)
                {
                    ++missed;
                }
            }
        }

        [TEST("FeatureLines", "Outline")]
        public static void OutlineOfASphere()
        {
            Transform identity = new Transform();
            Transform back = Translate(new Vector3f(0, 0, -30));
            Sphere sphere = new Sphere(identity, identity, false, 1f, -1f, 1f, 360);
            Sphere wall = new Sphere(back, Inverse(back), false, 25f, -25f, 25f, 360);
            Primitive alone = new BVHAggregate(new List<Primitive> { new SimplePrimitive(sphere, null, 1) });
            Primitive both = new BVHAggregate(new List<Primitive> { new SimplePrimitive(sphere, null, 1), new SimplePrimitive(wall, null, 2) });

            foreach (Float radius in new Float[] { 0.02f, 0.05f, 0.1f })
            {
                double from, to;
                int found;
                // Against nothing
                Outline(alone, Settings(true, 0, 0), radius, out from, out to, out found, out int missed);
                Console.WriteLine("MEASURE feature line: outline of a sphere, radius of the stencil {0}: line from {1:F4} to {2:F4} ({3:F4} wide, asked {4:F4})", radius,
                                  from, to, to - from, 2 * radius);
                EXPECT_LT(Math.Abs(from - (1 - radius)), 0.12 * radius + 5e-4);
                EXPECT_LT(Math.Abs(to - (1 + radius)), 0.12 * radius + 5e-4);
                // (no gaps in the line)
                EXPECT_EQ(0, missed);
                // Against another object, by the objects and by the step in depth alone
                Outline(both, Settings(true, 0, 0), radius, out from, out to, out found);
                EXPECT_LT(Math.Abs((to - from) - 2 * radius), 0.25 * radius + 1e-3);
                Outline(both, Settings(false, 0, 3), radius, out from, out to, out found);
                EXPECT_LT(Math.Abs(to - (1 + radius)), 0.12 * radius + 5e-4);
                EXPECT_GT(found, 0);
                // Neither: no line
                Outline(both, Settings(false, 0, 0), radius, out from, out to, out found);
                EXPECT_EQ(0, found);
            }
        }

        [TEST("FeatureLines", "Thresholds")]
        public static void Thresholds()
        {
            Transform identity = new Transform();
            Sphere sphere = new Sphere(identity, identity, false, 1f, -1f, 1f, 360);
            Primitive scene = new BVHAggregate(new List<Primitive> { new SimplePrimitive(sphere, null, 1) });
            // The middle of the sphere, a stencil of radius 0.2: the normals turn by 11.5 degrees, the surface is 0.02 off the
            // plane of its neighbour (0.1 radii)
            Ray ray = new Ray(new Point3f(0, 0, 10), new Vector3f(0, 0, -1));
            pstd.optional<ShapeIntersection> si = scene.Intersect(ray, Infinity);
            EXPECT_FALSE(FeatureLines.Detect(scene, ray, si, Settings(true, 40, 3), 0.2f, 0, 0.3f, true));
            EXPECT_FALSE(FeatureLines.Detect(scene, ray, si, Settings(true, 12.5f, 0), 0.2f, 0, 0.3f, true));
            EXPECT_TRUE(FeatureLines.Detect(scene, ray, si, Settings(true, 10.5f, 0), 0.2f, 0, 0.3f, true));
            EXPECT_FALSE(FeatureLines.Detect(scene, ray, si, Settings(true, 0, 0.25f), 0.2f, 0, 0.3f, true));
            EXPECT_TRUE(FeatureLines.Detect(scene, ray, si, Settings(true, 0, 0.05f), 0.2f, 0, 0.3f, true));
            // A cone that grows: the radius at the hit is the radius at the start and the spread times the distance (9)
            EXPECT_FALSE(FeatureLines.Detect(scene, ray, si, Settings(true, 10.5f, 0), 0.02f, 0.01f, 0.3f, true));
            EXPECT_TRUE(FeatureLines.Detect(scene, ray, si, Settings(true, 10.5f, 0), 0.02f, 0.021f, 0.3f, true));
            // A ray that leaves the scene far from everything: no line
            Ray away = new Ray(new Point3f(5, 0, 10), new Vector3f(0, 0, -1));
            EXPECT_FALSE(FeatureLines.Detect(scene, away, scene.Intersect(away, Infinity), Settings(true, 40, 3), 0.2f, 0.01f, 0.3f, true));
        }
    }
}
