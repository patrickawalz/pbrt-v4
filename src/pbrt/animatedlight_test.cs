// (not in pbrt-v4) Tests of the area lights and the emissive media that move (AnimatedTransform): see docs/animated-transforms.md.
//
// A light that moves is a DiffuseAreaLight whose shape is in its own space and whose transformation is animated
// (DiffuseAreaLight.SetMotion). The tests compare it, at a time t, with a static light made with the transformation
// at t, then with independent computations (brute force quadrature of the moving emitter, the bounds sampled over
// the shutter), then with renders of the same scenes.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using testing;

namespace pbrt
{
    public static unsafe class AnimatedLightTests
    {
        // ------------------------------------------------------------------------------------------
        // Helpers: shapes, lights, animations

        private static readonly string[] shapeKinds = new string[] { "quad", "triangle", "sphere", "disk" };

        // The shape in the space given by the transformation (the vertices of the meshes are transformed by it, the quadrics hold it)
        private static Shape MakeShape(string kind, Transform renderFromObject)
        {
            Transform objectFromRender = Inverse(renderFromObject);
            switch (kind)
            {
                case "quad":
                {
                    BilinearPatchMesh mesh = new BilinearPatchMesh(renderFromObject, false, new pstd.vector<int>(new int[] { 0, 1, 2, 3 }),
                                                                   new pstd.vector<Point3f>(new Point3f[] { new Point3f(-1, -1, 0), new Point3f(1, -1, 0),
                                                                                                            new Point3f(-1, 1, 0), new Point3f(1, 1, 0) }),
                                                                   new pstd.vector<Normal3f>(),
                                                                   new pstd.vector<Point2f>(new Point2f[] { new Point2f(0, 0), new Point2f(1, 0),
                                                                                                            new Point2f(0, 1), new Point2f(1, 1) }),
                                                                   new pstd.vector<int>(), null, new Allocator());
                    return BilinearPatch.CreatePatches(mesh, new Allocator())[0];
                }
                case "triangle":
                {
                    TriangleMesh mesh = new TriangleMesh(renderFromObject, false, new pstd.vector<int>(new int[] { 0, 1, 2 }),
                                                         new pstd.vector<Point3f>(new Point3f[] { new Point3f(-1, -1, 0), new Point3f(1, -1, 0),
                                                                                                  new Point3f(0, 1, 0) }),
                                                         new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(),
                                                         new pstd.vector<int>(), new Allocator());
                    return Triangle.CreateTriangles(mesh, new Allocator())[0];
                }
                case "sphere":
                    return new Sphere(renderFromObject, objectFromRender, false, 0.7f, -0.7f, 0.7f, 360);
                case "disk":
                    return new Disk(renderFromObject, objectFromRender, false, 0, 0.8f, 0, 360);
            }
            throw new ArgumentException(kind);
        }

        private static bool TwoSidedByDefault(string kind)
        {
            return kind != "sphere";
        }

        private static readonly ConstantSpectrum emission = new ConstantSpectrum(2.0f);

        private static DiffuseAreaLight StaticLight(string kind, Transform renderFromLight, bool twoSided, FloatTexture alpha = null)
        {
            return new DiffuseAreaLight(renderFromLight, new MediumInterface(), emission, 1.5f, MakeShape(kind, renderFromLight), alpha, new Image(), null,
                                        twoSided);
        }

        private static DiffuseAreaLight MovingLight(string kind, AnimatedTransform renderFromLight, bool twoSided, FloatTexture alpha = null)
        {
            DiffuseAreaLight light = new DiffuseAreaLight(renderFromLight.startTransform, new MediumInterface(), emission, 1.5f, MakeShape(kind, new Transform()),
                                                          alpha, new Image(), null, twoSided);
            light.SetMotion(renderFromLight);
            return light;
        }

        private static Transform Pose(Float x, Float y, Float z, Float degrees, Float ax, Float ay, Float az)
        {
            return Translate(new Vector3f(x, y, z)) * Rotate(degrees, Normalize(new Vector3f(ax, ay, az)));
        }

        // Two keys: a translation and a rotation of 75 degrees
        private static AnimatedTransform TwoKeys()
        {
            return new AnimatedTransform(Pose(-1.2f, 0.1f, 0, 0, 0, 1, 0), 0, Pose(1.0f, 0.4f, 0.3f, 75, 1, 1, 0), 1);
        }

        // Four keys, 200 degrees of rotation in all, the path turns
        private static AnimatedTransform FourKeys()
        {
            return new AnimatedTransform(new Transform[] { Pose(-1.0f, 0, 0, 0, 0.3f, 1, 0.2f), Pose(-0.3f, 0.6f, 0.2f, 60, 0.3f, 1, 0.2f),
                                                           Pose(0.5f, 0.3f, -0.4f, 130, 0.3f, 1, 0.2f), Pose(1.0f, -0.4f, 0.3f, 200, 0.3f, 1, 0.2f) },
                                         new Float[] { 0, 0.25f, 0.6f, 1 });
        }

        private static AnimatedTransform Keys(int count)
        {
            return count == 2 ? TwoKeys() : FourKeys();
        }

        private static AnimatedTransform LargeRotation()
        {
            return new AnimatedTransform(Pose(0, 0, 0, 0, 0, 0, 1), 0, Pose(0.5f, 0, 0, 170, 0.2f, 0.3f, 1), 1);
        }

        private static AnimatedTransform ManyTurns()
        {
            return new AnimatedTransform(new Transform[] { Pose(0, 0, 0, 0, 0, 0, 1), Pose(0.3f, 0, 0, 100, 0, 0, 1), Pose(0.6f, 0, 0, 200, 0, 0, 1),
                                                           Pose(0.9f, 0, 0, 300, 0, 0, 1) },
                                         new Float[] { 0, 0.3f, 0.6f, 1 });
        }

        private static AnimatedTransform TranslationOnly()
        {
            return new AnimatedTransform(Pose(-1, 0, 0, 30, 0, 1, 0), 0, Pose(1, 0.5f, 0, 30, 0, 1, 0), 1);
        }

        // A receiver outside the emitters (they stay within 2.6 of the origin)
        private static void RandomReceiver(ref RNG rng, out Point3f p, out Normal3f n)
        {
            Vector3f dir = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
            p = new Point3f(0, 0, 0) + (3.2f + 2.5f * rng.Uniform<Float>()) * dir;
            Vector3f toward = Normalize(-dir + 0.5f * SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
            n = new Normal3f(toward.x, toward.y, toward.z);
        }

        private static LightSampleContext Context(Point3f p, Normal3f n, Float time)
        {
            return new LightSampleContext(new Point3fi(p), n, n, time);
        }

        private static double Rel(double a, double b)
        {
            return Math.Abs(a - b) / Math.Max(1e-6, Math.Max(Math.Abs(a), Math.Abs(b)));
        }

        private static bool BitsEqual(Float a, Float b)
        {
            return BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        }

        private static void Report(string text)
        {
            Console.WriteLine("[AnimatedLight] " + text);
        }

        // ------------------------------------------------------------------------------------------
        // 1. A light with a transformation that does not move is the static light, bit for bit

        [TEST("AnimatedLight", "NonAnimatedTransformIsTheStaticLightBitForBit")]
        public static void NonAnimatedTransformIsTheStaticLightBitForBit()
        {
            Transform t = Pose(0.4f, -0.2f, 0.1f, 33, 1, 2, 3);
            AnimatedTransform notMoving = new AnimatedTransform(t);
            EXPECT_FALSE(notMoving.IsAnimated());
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.37f);
            int compared = 0;
            foreach (string kind in shapeKinds)
            {
                foreach (bool twoSided in new bool[] { false, true })
                {
                    DiffuseAreaLight reference = StaticLight(kind, t, twoSided);
                    // the same construction with a transformation given as an AnimatedTransform that does not move: the shape is in render space
                    DiffuseAreaLight same = new DiffuseAreaLight(t, new MediumInterface(), emission, 1.5f, MakeShape(kind, t), null, new Image(), null, twoSided);
                    same.SetMotion(notMoving);
                    EXPECT_FALSE(same.IsAnimated());
                    pstd.optional<LightBounds> b0 = reference.Bounds(), b1 = same.Bounds();
                    EXPECT_TRUE(BitsEqual(b0.value().bounds.pMin.x, b1.value().bounds.pMin.x) && BitsEqual(b0.value().bounds.pMax.z, b1.value().bounds.pMax.z));
                    EXPECT_TRUE(BitsEqual(b0.value().w.x, b1.value().w.x) && BitsEqual(b0.value().cosTheta_o, b1.value().cosTheta_o) &&
                                BitsEqual(b0.value().phi, b1.value().phi));
                    EXPECT_TRUE(BitsEqual(reference.Phi(lambda)[1], same.Phi(lambda)[1]));
                    RNG rng = new RNG(31, 7);
                    for (int i = 0; i < 300; ++i)
                    {
                        RandomReceiver(ref rng, out Point3f p, out Normal3f n);
                        Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                        LightSampleContext ctx = Context(p, n, 0.3f);
                        pstd.optional<LightLiSample> a = reference.SampleLi(ctx, u, lambda), b = same.SampleLi(ctx, u, lambda);
                        ASSERT_EQ(a.has_value(), b.has_value());
                        if (!a)
                            continue;
                        EXPECT_TRUE(BitsEqual(a.value().pdf, b.value().pdf) && BitsEqual(a.value().wi.x, b.value().wi.x) &&
                                    BitsEqual(a.value().L[0], b.value().L[0]) && BitsEqual(a.value().pLight.p().y, b.value().pLight.p().y));
                        Vector3f w = Normalize(new Vector3f(-p.x, -p.y, -p.z) + new Vector3f(rng.Uniform<Float>() - 0.5f, 0.2f, 0.1f));
                        EXPECT_TRUE(BitsEqual(reference.PDF_Li(ctx, w), same.PDF_Li(ctx, w)));
                        Float l0 = reference.L(a.value().pLight.p(), a.value().pLight.n, a.value().pLight.uv, -a.value().wi, lambda, 0.3f)[0];
                        Float l1 = same.L(a.value().pLight.p(), a.value().pLight.n, a.value().pLight.uv, -a.value().wi, lambda, 0.3f)[0];
                        EXPECT_TRUE(BitsEqual(l0, l1));
                        ++compared;
                    }
                }
            }
            Report("static vs not moving AnimatedTransform: " + compared + " samples, all bit for bit");
        }

        // ------------------------------------------------------------------------------------------
        // 2. A light that moves, at a time t, is the static light made with Interpolate(t)

        // The differences between the light that moves and the static light at that time, and, for the scale of the differences that float arithmetic
        // alone makes, the differences between the static light and itself when the reference point moves by 1e-5 (the sampling of the spherical
        // triangle and the rectangle, and the density near a grazing direction, are not well conditioned in single precision)
        private sealed class Errors
        {
            public double pdf, wi, L, point, pdfLi, phi, normal;
            public double pdfFloor, wiFloor, pointFloor, pdfLiFloor;
            public double surface;
            public double worstPlaneDistance = -1, worstCosine = -1;
            public int samples, misses, illConditioned;
        }

        private static bool HitsShape(string kind, Transform renderFromObject, Point3f origin, Vector3f direction, Float time)
        {
            Shape shape = MakeShape(kind, renderFromObject);
            return shape.Intersect(new Ray(origin, direction, time)).has_value();
        }

        private static void CompareWithStatic(string kind, AnimatedTransform motion, Float time, bool twoSided, FloatTexture alpha, Errors errors)
        {
            Transform atTime = motion.Interpolate(time);
            DiffuseAreaLight moving = MovingLight(kind, motion, twoSided, alpha);
            DiffuseAreaLight reference = StaticLight(kind, atTime, twoSided, alpha);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.61f);
            errors.phi = Math.Max(errors.phi, Rel(moving.Phi(lambda)[0], reference.Phi(lambda)[0]));
            bool pointwise = kind != "sphere";   // the sampling of the cone of a sphere turns about the axis of the cone in a way that depends on the frame
            RNG rng = new RNG((ulong)kind.Length * 977, (ulong)(time * 1000) + 5);
            for (int i = 0; i < 400; ++i)
            {
                RandomReceiver(ref rng, out Point3f p, out Normal3f n);
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                LightSampleContext ctx = Context(p, n, time);
                LightSampleContext ctxNear = Context(p + new Vector3f(1e-5f, -1e-5f, 1e-5f), n, time);
                pstd.optional<LightLiSample> a = moving.SampleLi(ctx, u, lambda), b = reference.SampleLi(ctx, u, lambda);
                pstd.optional<LightLiSample> c = reference.SampleLi(ctxNear, u, lambda);
                if (a.has_value() != b.has_value())
                {
                    ++errors.misses;
                    continue;
                }
                // a direction near the emitter
                Vector3f toLight = Normalize(new Vector3f(-p.x, -p.y, -p.z) + 0.25f * SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
                // PDF_Li is only defined for directions that hit the emitter (the spherical triangle and rectangle densities do not intersect)
                if (HitsShape(kind, atTime, p, toLight, time))
                {
                    double relativeDifference = Rel(moving.PDF_Li(ctx, toLight), reference.PDF_Li(ctx, toLight));
                    if (relativeDifference > errors.pdfLi)
                    {
                        Point3f pObject = atTime.ApplyInverse(p);
                        Vector3f wObject = atTime.ApplyInverse(toLight);
                        errors.worstPlaneDistance = Math.Abs(pObject.z);
                        errors.worstCosine = Math.Abs(wObject.z) / Length(wObject);
                    }
                    errors.pdfLi = Math.Max(errors.pdfLi, relativeDifference);
                    errors.pdfLiFloor = Math.Max(errors.pdfLiFloor, Rel(reference.PDF_Li(ctxNear, toLight), reference.PDF_Li(ctx, toLight)));
                }
                if (!a)
                    continue;
                ++errors.samples;
                errors.pdf = Math.Max(errors.pdf, Rel(a.value().pdf, b.value().pdf));
                if (c)
                    errors.pdfFloor = Math.Max(errors.pdfFloor, Rel(c.value().pdf, b.value().pdf));
                if (pointwise)
                {
                    errors.wi = Math.Max(errors.wi, Length(a.value().wi - b.value().wi));
                    errors.point = Math.Max(errors.point, Distance(a.value().pLight.p(), b.value().pLight.p()));
                    errors.normal = Math.Max(errors.normal, Length(a.value().pLight.n - b.value().pLight.n));
                    if (c)
                    {
                        errors.wiFloor = Math.Max(errors.wiFloor, Length(c.value().wi - b.value().wi));
                        errors.pointFloor = Math.Max(errors.pointFloor, Distance(c.value().pLight.p(), b.value().pLight.p()));
                    }
                }
                else
                {
                    // on the moved sphere, with the outward normal
                    Point3f centre = atTime.Apply(new Point3f(0, 0, 0));
                    Vector3f radial = a.value().pLight.p() - centre;
                    errors.surface = Math.Max(errors.surface, Math.Abs(Length(radial) - 0.7f));
                    errors.normal = Math.Max(errors.normal, Length(Normalize(radial) - new Vector3f(a.value().pLight.n.x, a.value().pLight.n.y, a.value().pLight.n.z)));
                }
                errors.L = Math.Max(errors.L, Rel(a.value().L[0], b.value().L[0]));
                // PDF_Li along the sampled direction. When that direction is nearly parallel to the emitter the density goes with 1 / cosine and 1e-7 of
                // rounding becomes a large difference: the static light does not even agree with itself when the receiver moves by 1e-5. Those samples
                // are counted and left out, the others are compared.
                {
                    double conditioning = Rel(reference.PDF_Li(ctxNear, a.value().wi), reference.PDF_Li(ctx, a.value().wi));
                    if (conditioning > 1e-3)
                    {
                        ++errors.illConditioned;
                    }
                    else
                    {
                        errors.pdfLi = Math.Max(errors.pdfLi, Rel(moving.PDF_Li(ctx, a.value().wi), reference.PDF_Li(ctx, a.value().wi)));
                        errors.pdfLiFloor = Math.Max(errors.pdfLiFloor, conditioning);
                    }
                }
                // the density of the sample is the density of its direction
                errors.pdfLi = Math.Max(errors.pdfLi, 0);
                // L() of the emitter hit by a ray at that time
                Float lm = moving.L(a.value().pLight.p(), a.value().pLight.n, a.value().pLight.uv, -a.value().wi, lambda, time)[0];
                Float lr = reference.L(a.value().pLight.p(), a.value().pLight.n, a.value().pLight.uv, -a.value().wi, lambda, time)[0];
                errors.L = Math.Max(errors.L, Rel(lm, lr));
                // the reverse side: the same on both
                Float lbm = moving.L(a.value().pLight.p(), a.value().pLight.n, a.value().pLight.uv, a.value().wi, lambda, time)[0];
                Float lbr = reference.L(a.value().pLight.p(), a.value().pLight.n, a.value().pLight.uv, a.value().wi, lambda, time)[0];
                errors.L = Math.Max(errors.L, Rel(lbm, lbr));
            }
        }

        private sealed class UvAlpha : FloatTexture
        {
            public override string ToString() { return "[ UvAlpha ]"; }
            public override Float MinValue() { return 0; }
            public override Float MaxValue() { return 1; }
            public override Float Evaluate(TextureEvalContext ctx) { return ctx.uv.x < 0.5f ? 0 : 1; }
        }

        private static string F(double v)
        {
            return v.ToString("E2", CultureInfo.InvariantCulture);
        }

        [TEST("AnimatedLight", "MovingLightEqualsStaticLightAtThatTime")]
        public static void MovingLightEqualsStaticLightAtThatTime()
        {
            foreach (int keys in new int[] { 2, 4 })
            {
                AnimatedTransform motion = Keys(keys);
                foreach (string kind in shapeKinds)
                {
                    foreach (bool twoSided in new bool[] { false, true })
                    {
                        Errors e = new Errors();
                        foreach (Float time in new Float[] { 0, 0.13f, 0.4f, 0.77f, 1 })
                            CompareWithStatic(kind, motion, time, twoSided, null, e);
                        Report(keys + " keys, " + kind + (twoSided ? " two sided" : " one sided") + ": " + e.samples + " samples, " + e.misses +
                               " presence mismatches; moving vs static (static vs itself with the point moved by 1e-5): pdf " + F(e.pdf) + " (" + F(e.pdfFloor) + ") wi " +
                               F(e.wi) + " (" + F(e.wiFloor) + ") point " + F(e.point) + " (" + F(e.pointFloor) + ") PDF_Li (" + e.illConditioned + " grazing samples left out; worst: receiver " + F(e.worstPlaneDistance) + " from the plane, cosine " + F(e.worstCosine) + ") " + F(e.pdfLi) + " (" + F(e.pdfLiFloor) +
                               "); normal " + F(e.normal) + " L " + F(e.L) + " Phi " + F(e.phi) + (kind == "sphere" ? " off the sphere " + F(e.surface) : ""));
                        EXPECT_GT(e.samples, 300);
                        EXPECT_EQ(0, e.misses);
                        // the exact quantities
                        EXPECT_EQ(0.0, e.L);
                        EXPECT_LT(e.phi, 1e-6);
                        EXPECT_LT(e.normal, 1e-6);
                        EXPECT_LT(e.surface, 1e-6);
                        // the others are as close as the static light is to itself when its reference point moves by 1e-5 (times 25), at least a few ulp
                        EXPECT_LE(e.pdf, 25 * e.pdfFloor + 1e-5);
                        EXPECT_LE(e.wi, 25 * e.wiFloor + 1e-5);
                        EXPECT_LE(e.point, 25 * e.pointFloor + 1e-5);
                        EXPECT_LE(e.pdfLi, 25 * e.pdfLiFloor + 1e-5);
                    }
                }
            }
        }

        [TEST("AnimatedLight", "MovingLightWithAlphaEqualsStaticLight")]
        public static void MovingLightWithAlphaEqualsStaticLight()
        {
            Errors e = new Errors();
            foreach (Float time in new Float[] { 0.1f, 0.5f, 0.9f })
                CompareWithStatic("quad", TwoKeys(), time, true, new UvAlpha(), e);
            Report("alpha masked quad: " + e.samples + " samples, " + e.misses + " mismatches, max error pdf " + e.pdf.ToString("E2", CultureInfo.InvariantCulture) +
                   " L " + e.L.ToString("E2", CultureInfo.InvariantCulture));
            EXPECT_EQ(0, e.misses);
            EXPECT_GT(e.samples, 100);
            EXPECT_LT(e.pdf, 5e-4);
            EXPECT_LT(e.L, 1e-6);
        }

        // SampleLe at a time: the point and the density are those of the static light at that time (pdfPos, pdfDir, the ray, L)
        [TEST("AnimatedLight", "SampleLeEqualsStaticLightAtThatTime")]
        public static void SampleLeEqualsStaticLightAtThatTime()
        {
            double worstPos = 0, worstDir = 0, worstL = 0, worstPdf = 0, worstPdfLe = 0;
            int n = 0;
            foreach (string kind in shapeKinds)
            {
                foreach (int keys in new int[] { 2, 4 })
                {
                    AnimatedTransform motion = Keys(keys);
                    foreach (Float time in new Float[] { 0.05f, 0.5f, 0.95f })
                    {
                        DiffuseAreaLight moving = MovingLight(kind, motion, true);
                        DiffuseAreaLight reference = StaticLight(kind, motion.Interpolate(time), true);
                        RNG rng = new RNG(5, (ulong)(time * 100) + (ulong)keys);
                        for (int i = 0; i < 200; ++i)
                        {
                            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.4f);
                            Point2f u1 = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                            Point2f u2 = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                            SampledWavelengths la = lambda, lb = lambda;
                            pstd.optional<LightLeSample> a = moving.SampleLe(u1, u2, ref la, time), b = reference.SampleLe(u1, u2, ref lb, time);
                            ASSERT_EQ(a.has_value(), b.has_value());
                            if (!a)
                                continue;
                            ++n;
                            worstPos = Math.Max(worstPos, Distance(a.value().ray.o, b.value().ray.o));
                            worstDir = Math.Max(worstDir, Length(a.value().ray.d - b.value().ray.d));
                            worstL = Math.Max(worstL, Rel(a.value().L[0], b.value().L[0]));
                            worstPdf = Math.Max(worstPdf, Math.Max(Rel(a.value().pdfPos, b.value().pdfPos), Rel(a.value().pdfDir, b.value().pdfDir)));
                            EXPECT_EQ(time, a.value().ray.time);
                            EXPECT_EQ(time, a.value().intr.value().time);
                            // PDF_Le at the sampled point
                            Float pp0, pd0, pp1, pd1;
                            moving.PDF_Le(a.value().intr.value(), a.value().ray.d, &pp0, &pd0);
                            reference.PDF_Le(b.value().intr.value(), b.value().ray.d, &pp1, &pd1);
                            worstPdfLe = Math.Max(worstPdfLe, Math.Max(Rel(pp0, pp1), Rel(pd0, pd1)));
                        }
                    }
                }
            }
            Report("SampleLe: " + n + " samples, worst position " + worstPos.ToString("E2", CultureInfo.InvariantCulture) + " direction " +
                   worstDir.ToString("E2", CultureInfo.InvariantCulture) + " L " + worstL.ToString("E2", CultureInfo.InvariantCulture) + " pdfs " +
                   worstPdf.ToString("E2", CultureInfo.InvariantCulture) + " PDF_Le " + worstPdfLe.ToString("E2", CultureInfo.InvariantCulture));
            EXPECT_GT(n, 3000);
            EXPECT_LT(worstPos, 5e-5);
            EXPECT_LT(worstDir, 5e-4);
            EXPECT_LT(worstL, 1e-6);
            EXPECT_LT(worstPdf, 1e-5);
            EXPECT_LT(worstPdfLe, 1e-4);   // the cosine of a grazing direction is the worst conditioned term
        }

        // A uniform scale in the keys: the areas (power, density of the point) follow its square
        [TEST("AnimatedLight", "UniformScaleOfTheKeysScalesTheArea")]
        public static void UniformScaleOfTheKeysScalesTheArea()
        {
            Transform a = Translate(new Vector3f(-1, 0, 0)) * Rotate(10, new Vector3f(0, 1, 0)) * Scale(2, 2, 2);
            Transform b = Translate(new Vector3f(1, 0, 0)) * Rotate(80, new Vector3f(0, 1, 0)) * Scale(2, 2, 2);
            AnimatedTransform motion = new AnimatedTransform(a, 0, b, 1);
            DiffuseAreaLight moving = MovingLight("quad", motion, true);
            DiffuseAreaLight reference = StaticLight("quad", motion.Interpolate(0.5f), true);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            EXPECT_LT(Rel(moving.Phi(lambda)[0], reference.Phi(lambda)[0]), 1e-5);
            EXPECT_LT(Rel(moving.Phi(lambda)[0], Pi * 2 * 16 * 3.0), 1e-5);   // two sided, area 4 * 2^2, radiance 1.5 * 2
            SampledWavelengths la = lambda, lb = lambda;
            pstd.optional<LightLeSample> x = moving.SampleLe(new Point2f(0.3f, 0.6f), new Point2f(0.2f, 0.7f), ref la, 0.5f);
            pstd.optional<LightLeSample> y = reference.SampleLe(new Point2f(0.3f, 0.6f), new Point2f(0.2f, 0.7f), ref lb, 0.5f);
            EXPECT_LT(Rel(x.value().pdfPos, y.value().pdfPos), 1e-5);
            EXPECT_LT(Rel(x.value().pdfPos, 1.0 / 16.0), 1e-5);
        }


        // ------------------------------------------------------------------------------------------
        // 3. Monte Carlo against brute force quadrature of the moving emitter

        private sealed class Cell
        {
            public Point3f p;
            public Vector3f n;
            public double dA;
        }

        // The surface of the shape in its own space as cells (midpoint rule), with the unit normal
        private static List<Cell> SurfaceCells(string kind, int n)
        {
            List<Cell> cells = new List<Cell>();
            switch (kind)
            {
                case "quad":
                    for (int i = 0; i < n; ++i)
                        for (int j = 0; j < n; ++j)
                            cells.Add(new Cell { p = new Point3f(-1 + 2 * (i + 0.5f) / n, -1 + 2 * (j + 0.5f) / n, 0), n = new Vector3f(0, 0, 1), dA = 4.0 / (n * n) });
                    break;
                case "triangle":
                {
                    Point3f v0 = new Point3f(-1, -1, 0), v1 = new Point3f(1, -1, 0), v2 = new Point3f(0, 1, 0);
                    for (int i = 0; i < n; ++i)
                    {
                        for (int j = 0; j < n; ++j)
                        {
                            double su = Math.Sqrt((i + 0.5) / n);
                            double b0 = 1 - su, b1 = (j + 0.5) / n * su, b2 = 1 - b0 - b1;
                            cells.Add(new Cell { p = new Point3f((Float)(b0 * v0.x + b1 * v1.x + b2 * v2.x), (Float)(b0 * v0.y + b1 * v1.y + b2 * v2.y), 0),
                                                 n = new Vector3f(0, 0, 1), dA = 2.0 / (n * n) });
                        }
                    }
                    break;
                }
                case "sphere":
                {
                    double r = 0.7;
                    for (int i = 0; i < n; ++i)
                    {
                        double theta = Math.PI * (i + 0.5) / n;
                        for (int j = 0; j < 2 * n; ++j)
                        {
                            double phi = Math.PI * (j + 0.5) / n;
                            Vector3f dir = new Vector3f((Float)(Math.Sin(theta) * Math.Cos(phi)), (Float)(Math.Sin(theta) * Math.Sin(phi)), (Float)Math.Cos(theta));
                            cells.Add(new Cell { p = new Point3f((Float)r * dir.x, (Float)r * dir.y, (Float)r * dir.z), n = dir,
                                                 dA = r * r * Math.Sin(theta) * (Math.PI / n) * (Math.PI / n) });
                        }
                    }
                    break;
                }
                case "disk":
                {
                    double R = 0.8;
                    for (int i = 0; i < n; ++i)
                    {
                        double r = R * (i + 0.5) / n;
                        for (int j = 0; j < 2 * n; ++j)
                        {
                            double phi = Math.PI * (j + 0.5) / n;
                            cells.Add(new Cell { p = new Point3f((Float)(r * Math.Cos(phi)), (Float)(r * Math.Sin(phi)), 0), n = new Vector3f(0, 0, 1),
                                                 dA = r * (R / n) * (Math.PI / n) });
                        }
                    }
                    break;
                }
            }
            return cells;
        }

        // The irradiance (cos weighted at the receiver) and the solid angle that the emitter at the time has, seen from the receiver, by quadrature
        private static void Quadrature(List<Cell> cells, Transform renderFromLight, bool twoSided, Point3f p, Normal3f nr, double radiance, out double irradiance,
                                       out double solidAngle)
        {
            irradiance = 0;
            solidAngle = 0;
            foreach (Cell c in cells)
            {
                Point3f q = renderFromLight.Apply(c.p);
                Vector3f nl = Normalize(renderFromLight.Apply(c.n));
                Vector3f d = q - p;
                double d2 = LengthSquared(d);
                Vector3f wi = d / (Float)Math.Sqrt(d2);
                double cosL = Dot(nl, -wi);
                cosL = twoSided ? Math.Abs(cosL) : Math.Max(0, cosL);
                double cosR = Math.Max(0, Dot(new Vector3f(nr.x, nr.y, nr.z), wi));
                solidAngle += cosL / d2 * c.dA;
                irradiance += radiance * cosL * cosR / d2 * c.dA;
            }
        }

        [TEST("AnimatedLight", "SampleLiIntegralEqualsQuadratureOfTheMovingEmitter")]
        public static void SampleLiIntegralEqualsQuadratureOfTheMovingEmitter()
        {
            const int samples = 250000;
            double worstZ = 0, worstZOmega = 0;
            foreach (string kind in shapeKinds)
            {
                List<Cell> cells = SurfaceCells(kind, kind == "sphere" || kind == "disk" ? 300 : 450);
                bool twoSided = TwoSidedByDefault(kind);
                foreach (int keys in new int[] { 2, 4 })
                {
                    AnimatedTransform motion = Keys(keys);
                    foreach (Float time in new Float[] { 0.1f, 0.5f, 0.9f })
                    {
                        DiffuseAreaLight light = MovingLight(kind, motion, twoSided);
                        Transform renderFromLight = motion.Interpolate(time);
                        SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
                        RNG rng = new RNG(77, (ulong)(time * 100) + (ulong)keys + (ulong)kind.Length);
                        RandomReceiver(ref rng, out Point3f p, out Normal3f nr);
                        Quadrature(cells, renderFromLight, twoSided, p, nr, 3.0, out double irradiance, out double solidAngle);
                        LightSampleContext ctx = Context(p, nr, time);
                        double sum = 0, sum2 = 0, sumO = 0, sumO2 = 0;
                        for (int i = 0; i < samples; ++i)
                        {
                            double x = 0, o = 0;
                            pstd.optional<LightLiSample> ls = light.SampleLi(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), lambda);
                            if (ls && ls.value().pdf > 0)
                            {
                                x = ls.value().L[0] * Math.Max(0, Dot(new Vector3f(nr.x, nr.y, nr.z), ls.value().wi)) / ls.value().pdf;
                                o = 1.0 / ls.value().pdf;
                            }
                            sum += x;
                            sum2 += x * x;
                            sumO += o;
                            sumO2 += o * o;
                        }
                        double mean = sum / samples, se = Math.Sqrt(Math.Max(1e-30, sum2 / samples - mean * mean) / samples);
                        double meanO = sumO / samples, seO = Math.Sqrt(Math.Max(1e-30, sumO2 / samples - meanO * meanO) / samples);
                        double z = (mean - irradiance) / Math.Max(se, 1e-9 * irradiance + 1e-12);
                        double zO = (meanO - solidAngle) / Math.Max(seO, 1e-9 * solidAngle + 1e-12);
                        worstZ = Math.Max(worstZ, Math.Abs(z));
                        worstZOmega = Math.Max(worstZOmega, Math.Abs(zO));
                        Report(kind + " " + keys + " keys t=" + time.ToString(CultureInfo.InvariantCulture) + ": irradiance MC " + mean.ToString("F5", CultureInfo.InvariantCulture) +
                               " +- " + se.ToString("F5", CultureInfo.InvariantCulture) + ", quadrature " + irradiance.ToString("F5", CultureInfo.InvariantCulture) +
                               " (z " + z.ToString("F2", CultureInfo.InvariantCulture) + "); E[1/pdf] " + meanO.ToString("F5", CultureInfo.InvariantCulture) + " +- " +
                               seO.ToString("F5", CultureInfo.InvariantCulture) + ", solid angle by quadrature " + solidAngle.ToString("F5", CultureInfo.InvariantCulture) +
                               " (z " + zO.ToString("F2", CultureInfo.InvariantCulture) + ")");
                        // the quadrature itself has an error of about 1e-4 (relative)
                        EXPECT_LT(Math.Abs(mean - irradiance), 4.5 * se + 2e-4 * irradiance);
                        EXPECT_LT(Math.Abs(meanO - solidAngle), 4.5 * seO + 2e-4 * solidAngle);
                    }
                }
            }
            Report("worst z of the irradiance " + worstZ.ToString("F2", CultureInfo.InvariantCulture) + ", of the solid angle " + worstZOmega.ToString("F2", CultureInfo.InvariantCulture));
        }

        [TEST("AnimatedLight", "PDFLiIntegratesToOneOverTheMovingEmitter")]
        public static void PDFLiIntegratesToOneOverTheMovingEmitter()
        {
            const int samples = 1500000;
            foreach (string kind in shapeKinds)
            {
                foreach (Float time in new Float[] { 0.2f, 0.8f })
                {
                    DiffuseAreaLight light = MovingLight(kind, FourKeys(), TwoSidedByDefault(kind));
                    RNG rng = new RNG(3, (ulong)(time * 10) + (ulong)kind.Length);
                    RandomReceiver(ref rng, out Point3f p, out Normal3f nr);
                    LightSampleContext ctx = Context(p, nr, time);
                    DiffuseAreaLight reference = StaticLight(kind, FourKeys().Interpolate(time), TwoSidedByDefault(kind));
                    Shape shapeAtTime = MakeShape(kind, FourKeys().Interpolate(time));
                    double sum = 0, sum2 = 0, sumStatic = 0;
                    for (int i = 0; i < samples; ++i)
                    {
                        Vector3f w = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        if (!shapeAtTime.Intersect(new Ray(p, w, time)).has_value())
                            continue;   // the density is only defined for directions that hit the emitter
                        double v = light.PDF_Li(ctx, w) * 4 * Math.PI;
                        sum += v;
                        sum2 += v * v;
                        sumStatic += reference.PDF_Li(ctx, w) * 4 * Math.PI;
                    }
                    double mean = sum / samples, se = Math.Sqrt((sum2 / samples - mean * mean) / samples);
                    Report(kind + " t=" + time.ToString(CultureInfo.InvariantCulture) + ": the static light at that time gives " + (sumStatic / samples).ToString("F5", CultureInfo.InvariantCulture));
                    Report(kind + " t=" + time.ToString(CultureInfo.InvariantCulture) + ": integral of PDF_Li " + mean.ToString("F5", CultureInfo.InvariantCulture) + " +- " +
                           se.ToString("F5", CultureInfo.InvariantCulture));
                    EXPECT_LT(Math.Abs(mean - 1), 4.5 * se + 1e-3);
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // 4. Light samplers

        private static double AngleBetween(Vector3f a, Vector3f b)
        {
            return Math.Acos(Math.Max(-1, Math.Min(1, (double)Dot(Normalize(a), Normalize(b)))));
        }

        // Samples the moving shape at random times of the shutter and checks the points and normals against the bounds of the light. Returns the
        // largest distance outside the box (in units of the size of the box) and the largest angle outside the cone, in radians.
        private static void CheckBounds(string kind, AnimatedTransform motion, int samples, out double outsideBox, out double outsideCone, out LightBounds lb)
        {
            DiffuseAreaLight light = MovingLight(kind, motion, TwoSidedByDefault(kind));
            lb = light.Bounds().value();
            Shape shape = MakeShape(kind, new Transform());
            RNG rng = new RNG(99, (ulong)kind.Length * 13);
            outsideBox = 0;
            outsideCone = 0;
            Float extent = Length(lb.bounds.Diagonal());
            double coneAngle = Math.Acos(Math.Max(-1, Math.Min(1, (double)lb.cosTheta_o)));
            for (int i = 0; i < samples; ++i)
            {
                Float time = i < 6 ? (Float)(i % 2) : rng.Uniform<Float>();
                if (i >= 6 && i < 6 + motion.KeyCount)
                    time = motion.GetKeyTime(i - 6);
                pstd.optional<ShapeSample> ss = shape.Sample(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Transform t = motion.Interpolate(time);
                Point3f p = t.Apply(ss.value().intr.p());
                Vector3f n = Normalize(t.Apply(new Vector3f(ss.value().intr.n.x, ss.value().intr.n.y, ss.value().intr.n.z)));
                double d = Math.Max(Math.Max(Math.Max(lb.bounds.pMin.x - p.x, p.x - lb.bounds.pMax.x), Math.Max(lb.bounds.pMin.y - p.y, p.y - lb.bounds.pMax.y)),
                                    Math.Max(lb.bounds.pMin.z - p.z, p.z - lb.bounds.pMax.z));
                outsideBox = Math.Max(outsideBox, d / extent);
                if (lb.cosTheta_o > -1)
                    outsideCone = Math.Max(outsideCone, AngleBetween(n, lb.w) - coneAngle);
            }
        }

        [TEST("AnimatedLight", "LightBoundsAreConservativeOverTheShutter")]
        public static void LightBoundsAreConservativeOverTheShutter()
        {
            Dictionary<string, AnimatedTransform> motions = new Dictionary<string, AnimatedTransform>();
            motions["two keys 75 degrees"] = TwoKeys();
            motions["four keys 200 degrees"] = FourKeys();
            motions["two keys 170 degrees"] = LargeRotation();
            motions["translation only"] = TranslationOnly();
            motions["four keys 300 degrees"] = ManyTurns();
            foreach (string kind in new string[] { "quad", "triangle", "disk", "sphere" })
            {
                foreach (KeyValuePair<string, AnimatedTransform> kv in motions)
                {
                    CheckBounds(kind, kv.Value, 100000, out double outsideBox, out double outsideCone, out LightBounds lb);
                    Report(kind + ", " + kv.Key + ": 1e5 samples, largest distance outside the box " + outsideBox.ToString("E2", CultureInfo.InvariantCulture) +
                           " of its diagonal, largest angle outside the cone " + outsideCone.ToString("E2", CultureInfo.InvariantCulture) + " rad, cone half angle " +
                           (Math.Acos(Math.Max(-1, Math.Min(1, (double)lb.cosTheta_o))) * 180 / Math.PI).ToString("F3", CultureInfo.InvariantCulture) + " degrees");
                    EXPECT_LT(outsideBox, 2e-6);
                    EXPECT_LT(outsideCone, 2e-6);
                }
            }
            // the cone is as tight as the method allows: half of the rotation of the path, plus the cone of the shape
            DiffuseAreaLight quad = MovingLight("quad", TwoKeys(), true);
            double half = Math.Acos(quad.Bounds().value().cosTheta_o) * 180 / Math.PI;
            Report("quad, two keys: cone half angle " + half.ToString("F4", CultureInfo.InvariantCulture) + " degrees (rotation 75 degrees)");
            EXPECT_NEAR(37.5, half, 0.01);
            DiffuseAreaLight quad4 = MovingLight("quad", FourKeys(), true);
            EXPECT_NEAR(100, Math.Acos(quad4.Bounds().value().cosTheta_o) * 180 / Math.PI, 0.01);
            // a translation does not open the cone
            EXPECT_GT(MovingLight("quad", TranslationOnly(), true).Bounds().value().cosTheta_o, 1 - 1e-6f);
            // more than a half turn of rotation in all: the entire sphere
            AnimatedTransform fiveKeys = new AnimatedTransform(new Transform[] { Pose(0, 0, 0, 0, 0, 0, 1), Pose(0, 0, 0, 100, 0, 0, 1), Pose(0, 0, 0, 200, 0, 0, 1),
                                                                                  Pose(0, 0, 0, 300, 0, 0, 1), Pose(0, 0, 0, 400, 0, 0, 1) },
                                                               new Float[] { 0, 0.25f, 0.5f, 0.75f, 1 });
            EXPECT_EQ(-1f, MovingLight("quad", fiveKeys, true).Bounds().value().cosTheta_o);
            CheckBounds("quad", fiveKeys, 100000, out double ob, out double oc, out LightBounds lb5);
            EXPECT_LT(ob, 2e-6);
        }

        [TEST("AnimatedLight", "BVHLightSamplerProbabilitiesAreConsistentAndTimeIndependent")]
        public static void BVHLightSamplerProbabilitiesAreConsistentAndTimeIndependent()
        {
            Light[] lights = new Light[]
            {
                MovingLight("quad", TwoKeys(), true), MovingLight("sphere", FourKeys(), false), MovingLight("triangle", LargeRotation(), true),
                StaticLight("disk", Pose(0.5f, 1.0f, -1.5f, 40, 1, 0, 0), true), MovingLight("disk", ManyTurns(), true)
            };
            BVHLightSampler sampler = new BVHLightSampler(lights, new Allocator());
            RNG rng = new RNG(1234, 5);
            for (int receiver = 0; receiver < 4; ++receiver)
            {
                RandomReceiver(ref rng, out Point3f p, out Normal3f n);
                Float[] firstPmf = new Float[lights.Length];
                foreach (Float time in new Float[] { 0.1f, 0.5f, 0.9f })
                {
                    LightSampleContext ctx = Context(p, n, time);
                    double total = 0;
                    for (int l = 0; l < lights.Length; ++l)
                    {
                        Float pmf = sampler.PMF(ctx, lights[l]);
                        total += pmf;
                        if (time == 0.1f)
                            firstPmf[l] = pmf;
                        // the light bounds are those of the whole shutter: the probability does not depend on the time
                        EXPECT_TRUE(BitsEqual(pmf, firstPmf[l]));
                    }
                    EXPECT_NEAR(1.0, total, 1e-5);
                    // frequencies of Sample() against PMF()
                    const int draws = 300000;
                    int[] counts = new int[lights.Length];
                    for (int i = 0; i < draws; ++i)
                    {
                        pstd.optional<SampledLight> sl = sampler.Sample(ctx, rng.Uniform<Float>());
                        if (!sl)
                            continue;
                        int index = Array.IndexOf(lights, sl.value().light);
                        ++counts[index];
                        if (i < 50)
                            EXPECT_NEAR(sampler.PMF(ctx, sl.value().light), sl.value().p, 1e-6);
                    }
                    string line = "receiver " + receiver + " t=" + time.ToString(CultureInfo.InvariantCulture) + ": pmf/frequency";
                    for (int l = 0; l < lights.Length; ++l)
                    {
                        double pmf = firstPmf[l], freq = (double)counts[l] / draws, se = Math.Sqrt(pmf * (1 - pmf) / draws);
                        line += " " + pmf.ToString("F4", CultureInfo.InvariantCulture) + "/" + freq.ToString("F4", CultureInfo.InvariantCulture);
                        EXPECT_LT(Math.Abs(freq - pmf), 4.5 * se + 1e-6);
                    }
                    Report(line);
                }
            }
        }

        // The estimate of the irradiance by the BVH light sampler and SampleLi equals the sum of the irradiances of the lights, by quadrature
        [TEST("AnimatedLight", "IrradianceFromSeveralMovingLightsThroughTheBVHSampler")]
        public static void IrradianceFromSeveralMovingLightsThroughTheBVHSampler()
        {
            string[] kinds = new string[] { "quad", "sphere", "triangle", "disk" };
            AnimatedTransform[] motions = new AnimatedTransform[] { TwoKeys(), FourKeys(), LargeRotation(), ManyTurns() };
            Light[] lights = new Light[kinds.Length];
            for (int i = 0; i < kinds.Length; ++i)
                lights[i] = MovingLight(kinds[i], motions[i], TwoSidedByDefault(kinds[i]));
            BVHLightSampler sampler = new BVHLightSampler(lights, new Allocator());
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            foreach (Float time in new Float[] { 0.15f, 0.55f, 0.95f })
            {
                RNG rng = new RNG(8, (ulong)(time * 100));
                RandomReceiver(ref rng, out Point3f p, out Normal3f nr);
                double truth = 0;
                for (int i = 0; i < kinds.Length; ++i)
                {
                    Quadrature(SurfaceCells(kinds[i], 300), motions[i].Interpolate(time), TwoSidedByDefault(kinds[i]), p, nr, 3.0, out double e, out double omega);
                    truth += e;
                }
                LightSampleContext ctx = Context(p, nr, time);
                const int samples = 400000;
                double sum = 0, sum2 = 0;
                for (int i = 0; i < samples; ++i)
                {
                    double x = 0;
                    pstd.optional<SampledLight> sl = sampler.Sample(ctx, rng.Uniform<Float>());
                    if (sl)
                    {
                        pstd.optional<LightLiSample> ls = sl.value().light.SampleLi(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), lambda);
                        if (ls && ls.value().pdf > 0)
                            x = ls.value().L[0] * Math.Max(0, Dot(new Vector3f(nr.x, nr.y, nr.z), ls.value().wi)) / (ls.value().pdf * sl.value().p);
                    }
                    sum += x;
                    sum2 += x * x;
                }
                double mean = sum / samples, se = Math.Sqrt((sum2 / samples - mean * mean) / samples);
                Report("four moving lights, t=" + time.ToString(CultureInfo.InvariantCulture) + ": BVH sampler estimate " + mean.ToString("F5", CultureInfo.InvariantCulture) +
                       " +- " + se.ToString("F5", CultureInfo.InvariantCulture) + ", quadrature " + truth.ToString("F5", CultureInfo.InvariantCulture));
                EXPECT_LT(Math.Abs(mean - truth), 4.5 * se + 3e-4 * truth);
            }
        }

        // ------------------------------------------------------------------------------------------
        // 5. Emissive media that move

        // A medium of the unit cube [-1, 1]^3 in its own space, whose density is a grid texture that is anchored to the medium (the texture maps
        // the render space of the first pose into the grid), emitting a constant colour times the density
        private static TextureMedium MakeVolumeMedium(Transform pose, AnimatedTransform motion)
        {
            Bounds3f box = new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1));
            RNG rng = new RNG(4242);
            Float[] values = new Float[8 * 8 * 8];
            for (int i = 0; i < values.Length; ++i)
                values[i] = rng.Uniform<Float>() < 0.3f ? 0 : rng.Uniform<Float>();
            Transform textureFromRender = Scale(0.5f, 0.5f, 0.5f) * Translate(new Vector3f(1, 1, 1)) * Inverse(pose);
            FloatTexture density = new FloatGridTexture(new PointTransformMapping(textureFromRender), 8, 8, 8, values, WrapMode.Black);
            TextureMedium medium = new TextureMedium(box, pose, new ConstantSpectrum(0.7f), new ConstantSpectrum(1.3f), 2.0f, 0.2f, density, 0.0f, 3.0f, 8, 4, 0, 0.5f,
                                                     new SpectrumConstantTexture(new ConstantSpectrum(1.5f)), 1.0f, null);
            if (motion != null)
                medium.SetMotion(motion);
            return medium;
        }

        private static AnimatedTransform VolumeMotion(int keys)
        {
            if (keys == 2)
                return new AnimatedTransform(Pose(-0.8f, 0, 0, 0, 0, 1, 0), 0, Pose(0.9f, 0.3f, 0.2f, 100, 1, 1, 0), 1);
            return new AnimatedTransform(new Transform[] { Pose(-0.8f, 0, 0, 0, 0.3f, 1, 0.2f), Pose(-0.2f, 0.5f, 0.2f, 70, 0.3f, 1, 0.2f),
                                                           Pose(0.5f, 0.3f, -0.4f, 150, 0.3f, 1, 0.2f), Pose(0.9f, -0.4f, 0.3f, 250, 0.3f, 1, 0.2f) },
                                         new Float[] { 0, 0.25f, 0.6f, 1 });
        }

        private static Ray RandomVolumeRay(ref RNG rng, Float time, out Float tMax)
        {
            Vector3f dir = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
            Point3f origin = new Point3f(0, 0, 0) + 4.0f * dir;
            Point3f target = new Point3f(-1.5f + 3 * rng.Uniform<Float>(), -1.5f + 3 * rng.Uniform<Float>(), -1.5f + 3 * rng.Uniform<Float>());
            Vector3f d = Normalize(target - origin);
            tMax = 8.0f;
            return new Ray(origin, d, time);
        }

        private sealed class Segment
        {
            public Float tMin, tMax;
            public SampledSpectrum sigma;
        }

        private static List<Segment> Segments(Medium medium, Ray ray, Float tMax, SampledWavelengths lambda)
        {
            List<Segment> list = new List<Segment>();
            RayMajorantIterator it = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            while (true)
            {
                pstd.optional<RayMajorantSegment> seg = it.Next();
                if (!seg)
                    break;
                list.Add(new Segment { tMin = seg.value().tMin, tMax = seg.value().tMax, sigma = seg.value().sigma_maj });
            }
            return list;
        }

        private static double OpticalDepthBound(List<Segment> segments)
        {
            double sum = 0;
            foreach (Segment s in segments)
                sum += s.sigma[0] * (s.tMax - s.tMin);
            return sum;
        }

        [TEST("AnimatedLight", "MovingTextureMediumEqualsStaticMediumAtThatTime")]
        public static void MovingTextureMediumEqualsStaticMediumAtThatTime()
        {
            foreach (int keys in new int[] { 2, 4 })
            {
                AnimatedTransform motion = VolumeMotion(keys);
                TextureMedium moving = MakeVolumeMedium(motion.startTransform, motion);
                EXPECT_TRUE(moving.IsAnimated());
                foreach (Float time in new Float[] { 0, 0.2f, 0.55f, 0.9f, 1 })
                {
                    TextureMedium reference = MakeVolumeMedium(motion.Interpolate(time), null);
                    RNG rng = new RNG(11, (ulong)(time * 100) + (ulong)keys);
                    SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.43f);
                    double worst = 0;
                    int nonzero = 0;
                    for (int i = 0; i < 20000; ++i)
                    {
                        Point3f p = new Point3f(-2.2f + 4.4f * rng.Uniform<Float>(), -2.2f + 4.4f * rng.Uniform<Float>(), -2.2f + 4.4f * rng.Uniform<Float>());
                        MediumProperties a = moving.SamplePoint(p, lambda, time), b = reference.SamplePoint(p, lambda);
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            worst = Math.Max(worst, Math.Abs(a.sigma_a[c] - b.sigma_a[c]));
                            worst = Math.Max(worst, Math.Abs(a.sigma_s[c] - b.sigma_s[c]));
                            worst = Math.Max(worst, Math.Abs(a.Le[c] - b.Le[c]));
                        }
                        if (a.sigma_a[0] > 0)
                            ++nonzero;
                        EXPECT_NEAR(moving.Density(p, time), reference.Density(p), 1e-4);
                    }
                    // the ray majorants
                    double worstDepth = -1e9, sumRatio = 0;
                    int rays = 0;
                    for (int i = 0; i < 500; ++i)
                    {
                        Ray ray = RandomVolumeRay(ref rng, time, out Float tMax);
                        List<Segment> a = Segments(moving, ray, tMax, lambda), b = Segments(reference, ray, tMax, lambda);
                        double da = OpticalDepthBound(a), db = OpticalDepthBound(b);
                        // The majorants are bounds, not values. The static medium at that pose bounds the density of the box of a rotated cell, which is larger
                        // than the cell, so its bounds are never tighter than the ones of the moving medium, whose grid is in the space of the medium.
                        worstDepth = Math.Max(worstDepth, (da - db) / Math.Max(1e-3, db));
                        sumRatio += da / Math.Max(1e-6, db);
                        ++rays;
                    }
                    Report(keys + " keys, t=" + time.ToString(CultureInfo.InvariantCulture) + ": moving vs static texture medium: largest difference of the coefficients " + F(worst) + " over " +
                           nonzero + " non empty points of 20000, largest relative excess of the optical depth bound over the one of the static medium (never tighter than it) " + F(worstDepth) +
                           ", mean ratio moving / static " + F(sumRatio / rays));
                    EXPECT_GT(nonzero, 1000);
                    EXPECT_LT(worst, 1e-3);
                    EXPECT_LT(worstDepth, 1e-3);
                }
            }
        }

        [TEST("AnimatedLight", "MovingTextureMediumMajorantsAreSoundOverTheShutter")]
        public static void MovingTextureMediumMajorantsAreSoundOverTheShutter()
        {
            foreach (int keys in new int[] { 2, 4 })
            {
                AnimatedTransform motion = VolumeMotion(keys);
                TextureMedium moving = MakeVolumeMedium(motion.startTransform, motion);
                RNG rng = new RNG(51, (ulong)keys);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.8f);
                int checkedPoints = 0, dense = 0, violations = 0, uncovered = 0, majorantViolations = 0;
                for (int i = 0; i < 20000; ++i)
                {
                    Float time = rng.Uniform<Float>();
                    Ray ray = RandomVolumeRay(ref rng, time, out Float tMax);
                    List<Segment> segments = Segments(moving, ray, tMax, lambda);
                    for (int k = 0; k < 16; ++k)
                    {
                        Float t = tMax * rng.Uniform<Float>();
                        Point3f p = ray.Apply(t);
                        MediumProperties mp = moving.SamplePoint(p, lambda, time);
                        SampledSpectrum sigma_t = mp.sigma_a + mp.sigma_s;
                        ++checkedPoints;
                        if (sigma_t[0] > 0)
                            ++dense;
                        Segment containing = null;
                        foreach (Segment sg in segments)
                            if (t >= sg.tMin && t < sg.tMax)
                                containing = sg;
                        if (containing == null)
                        {
                            if (sigma_t[0] > 1e-6f || sigma_t[1] > 1e-6f)
                                ++uncovered;
                            continue;
                        }
                        for (int c = 0; c < NSpectrumSamples; ++c)
                            if (sigma_t[c] > containing.sigma[c] * (1 + 1e-4f) + 1e-6f)
                                ++violations;
                        // the majorant looked up by point is the one of the segment
                        SampledSpectrum maj = moving.MajorantAt(p, lambda, time);
                        if (maj[0] < sigma_t[0] * (1 - 1e-4f) - 1e-6f)
                            ++majorantViolations;
                    }
                }
                Report(keys + " keys: " + checkedPoints + " points on rays at random times, " + dense + " with density: extinction above the majorant of the segment " + violations +
                       " times, density outside every segment " + uncovered + " times, MajorantAt below the extinction " + majorantViolations + " times");
                EXPECT_GT(dense, 20000);
                EXPECT_EQ(0, violations);
                EXPECT_EQ(0, uncovered);
                EXPECT_EQ(0, majorantViolations);
            }
        }

        // The emission gathered by delta tracking along a ray in the moving medium (as volpath does) against the quadrature of the same integral
        [TEST("AnimatedLight", "MovingTextureMediumEmissionAlongRaysAgainstQuadrature")]
        public static void MovingTextureMediumEmissionAlongRaysAgainstQuadrature()
        {
            AnimatedTransform motion = VolumeMotion(4);
            TextureMedium moving = MakeVolumeMedium(motion.startTransform, motion);
            RNG rng = new RNG(66, 3);
            double worstZ = 0;
            for (int r = 0; r < 6; ++r)
            {
                Float time = (r + 0.5f) / 6;
                Ray ray = RandomVolumeRay(ref rng, time, out Float tMax);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                int steps = 8000;
                double h = tMax / steps, tau = 0, reference = 0;
                for (int i = 0; i < steps; ++i)
                {
                    MediumProperties mp = moving.SamplePoint(ray.Apply((Float)((i + 0.5) * h)), lambda, time);
                    tau += (mp.sigma_a[0] + mp.sigma_s[0]) * 0.5 * h;
                    reference += mp.sigma_a[0] * mp.Le[0] * Math.Exp(-tau) * h;
                    tau += (mp.sigma_a[0] + mp.sigma_s[0]) * 0.5 * h;
                }
                int n = 30000;
                double sum = 0, sum2 = 0;
                for (int i = 0; i < n; ++i)
                {
                    RNG rg = new RNG(700 + (ulong)r, (ulong)i * 31UL);
                    double L = 0, beta = 1;
                    Float u = rg.Uniform<Float>();
                    SampleT_maj(ray, moving, tMax, u, ref rg, lambda, (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                    {
                        L += beta * mp.sigma_a[0] * mp.Le[0] / sigma_maj[0];
                        beta *= Math.Max(0, sigma_maj[0] - mp.sigma_a[0] - mp.sigma_s[0]) / sigma_maj[0];
                        return beta > 0;
                    });
                    sum += L;
                    sum2 += L * L;
                }
                double mean = sum / n, se = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean) / (n - 1));
                double z = (mean - reference) / Math.Max(se, 1e-12);
                Report("t=" + time.ToString("F3", CultureInfo.InvariantCulture) + ": emission along a ray, delta tracking " + mean.ToString("F5", CultureInfo.InvariantCulture) + " +- " +
                       se.ToString("F5", CultureInfo.InvariantCulture) + ", quadrature " + reference.ToString("F5", CultureInfo.InvariantCulture) + " (z " + z.ToString("F2", CultureInfo.InvariantCulture) + ")");
                if (reference > 1e-4)
                {
                    EXPECT_LT(Math.Abs(z), 4.5);
                    worstZ = Math.Max(worstZ, Math.Abs(z));
                }
            }
        }

        private static double VoxelWeightedVolume(Transform renderFromMedium)
        {
            Vector3f ex = renderFromMedium.Apply(new Vector3f(2, 0, 0)), ey = renderFromMedium.Apply(new Vector3f(0, 2, 0)), ez = renderFromMedium.Apply(new Vector3f(0, 0, 2));
            return Math.Abs(Dot(Cross(ex, ey), ez));
        }

        // Sampling the emission of the moving medium as a light: the estimate of the integral of the emission divided by the squared distance
        // equals the quadrature over the volume, the density is positive wherever there is emission, and the points stay in the bounds of the light
        [TEST("AnimatedLight", "VolumeEmissionLightOfAMovingMedium")]
        public static void VolumeEmissionLightOfAMovingMedium()
        {
            foreach (int keys in new int[] { 2, 4 })
            {
                AnimatedTransform motion = VolumeMotion(keys);
                TextureMedium medium = MakeVolumeMedium(motion.startTransform, motion);
                VolumeEmissionLight light = new VolumeEmissionLight(medium, 8);
                EXPECT_TRUE(light.HasEmission());
                LightBounds lb = light.Bounds().value();
                SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
                foreach (Float time in new Float[] { 0.1f, 0.5f, 0.9f })
                {
                    RNG rng = new RNG(5, (ulong)(time * 100) + (ulong)keys);
                    Vector3f dir = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Point3f x = new Point3f(0, 0, 0) + 4.5f * dir;
                    Transform renderFromMedium = motion.Interpolate(time);
                    // quadrature: 64^3 midpoints of the volume
                    int n = 64;
                    double dV = VoxelWeightedVolume(renderFromMedium) / ((double)n * n * n), quadrature = 0;
                    for (int iz = 0; iz < n; ++iz)
                    {
                        for (int iy = 0; iy < n; ++iy)
                        {
                            for (int ix = 0; ix < n; ++ix)
                            {
                                Point3f pm = new Point3f(-1 + 2 * (ix + 0.5f) / n, -1 + 2 * (iy + 0.5f) / n, -1 + 2 * (iz + 0.5f) / n);
                                Point3f pr = renderFromMedium.Apply(pm);
                                quadrature += medium.EmissionAt(pr, lambda, time)[0] / LengthSquared(pr - x) * dV;
                            }
                        }
                    }
                    LightSampleContext ctx = Context(x, new Normal3f(0, 0, 0), time);
                    const int samples = 300000;
                    double sum = 0, sum2 = 0;
                    int outside = 0, pdfMismatch = 0;
                    for (int i = 0; i < samples; ++i)
                    {
                        pstd.optional<LightLiSample> ls = light.SampleLi(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), lambda);
                        double v = 0;
                        if (ls && ls.value().pdf > 0)
                        {
                            Point3f p = ls.value().pLight.p();
                            v = ls.value().L[0] / ls.value().pdf;
                            Point3f pm = renderFromMedium.ApplyInverse(p);
                            if (pm.x < -1.0001f || pm.x > 1.0001f || pm.y < -1.0001f || pm.y > 1.0001f || pm.z < -1.0001f || pm.z > 1.0001f)
                                ++outside;
                            if (p.x < lb.bounds.pMin.x - 1e-4f || p.x > lb.bounds.pMax.x + 1e-4f || p.y < lb.bounds.pMin.y - 1e-4f || p.y > lb.bounds.pMax.y + 1e-4f ||
                                p.z < lb.bounds.pMin.z - 1e-4f || p.z > lb.bounds.pMax.z + 1e-4f)
                                ++outside;
                            if (Rel(light.PdfPoint(p, time) * LengthSquared(p - x), ls.value().pdf) > 1e-3)
                                ++pdfMismatch;
                        }
                        sum += v;
                        sum2 += v * v;
                    }
                    double mean = sum / samples, se = Math.Sqrt((sum2 / samples - mean * mean) / samples);
                    // The same count for a static medium at that pose: a sample on a voxel face of the density can be looked up in the next voxel
                    int staticMismatch = 0;
                    {
                        TextureMedium stillMedium = MakeVolumeMedium(renderFromMedium, null);
                        VolumeEmissionLight stillLight = new VolumeEmissionLight(stillMedium, 8);
                        for (int i = 0; i < samples; ++i)
                        {
                            pstd.optional<LightLiSample> ls = stillLight.SampleLi(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), lambda);
                            if (ls && ls.value().pdf > 0)
                            {
                                Point3f p = ls.value().pLight.p();
                                if (Rel(stillLight.PdfPoint(p, time) * LengthSquared(p - x), ls.value().pdf) > 1e-3)
                                    ++staticMismatch;
                            }
                        }
                    }
                    // the density is positive where there is emission
                    int starved = 0, emitting = 0;
                    for (int i = 0; i < 100000; ++i)
                    {
                        Point3f pm = new Point3f(-1 + 2 * rng.Uniform<Float>(), -1 + 2 * rng.Uniform<Float>(), -1 + 2 * rng.Uniform<Float>());
                        Point3f pr = renderFromMedium.Apply(pm);
                        if (medium.EmissionAt(pr, lambda, time)[0] > 0)
                        {
                            ++emitting;
                            if (!(light.PdfPoint(pr, time) > 0))
                                ++starved;
                        }
                    }
                    Report(keys + " keys, t=" + time.ToString(CultureInfo.InvariantCulture) + ": integral of E/d^2 by sampling the emission light " + mean.ToString("F5", CultureInfo.InvariantCulture) +
                           " +- " + se.ToString("F5", CultureInfo.InvariantCulture) + ", quadrature " + quadrature.ToString("F5", CultureInfo.InvariantCulture) + "; samples outside the medium or the bounds " +
                           outside + ", density mismatches " + pdfMismatch + " (static medium at that pose: " + staticMismatch + " of " + samples + ", samples on voxel faces), emitting points with zero density " + starved + " of " + emitting);
                    EXPECT_LT(Math.Abs(mean - quadrature), 4.5 * se + 1e-3 * quadrature);
                    EXPECT_EQ(0, outside);
                    EXPECT_LE(pdfMismatch, staticMismatch + 3);
                    EXPECT_EQ(0, starved);
                    EXPECT_GT(emitting, 10000);
                }
            }
        }

        // The densities of the emission light integrate to one over the points (by area it is a volume density) and over the directions (PdfDirection)
        [TEST("AnimatedLight", "VolumeEmissionLightDensitiesIntegrateToOne")]
        public static void VolumeEmissionLightDensitiesIntegrateToOne()
        {
            AnimatedTransform motion = VolumeMotion(4);
            TextureMedium medium = MakeVolumeMedium(motion.startTransform, motion);
            VolumeEmissionLight light = new VolumeEmissionLight(medium, 8);
            foreach (Float time in new Float[] { 0.15f, 0.7f })
            {
                RNG rng = new RNG(19, (ulong)(time * 100));
                Transform renderFromMedium = motion.Interpolate(time);
                // over the volume: uniform points of the box of the medium
                double volume = VoxelWeightedVolume(renderFromMedium);
                double sum = 0, sum2 = 0;
                int samples = 400000;
                for (int i = 0; i < samples; ++i)
                {
                    Point3f pm = new Point3f(-1 + 2 * rng.Uniform<Float>(), -1 + 2 * rng.Uniform<Float>(), -1 + 2 * rng.Uniform<Float>());
                    double v = light.PdfPoint(renderFromMedium.Apply(pm), time) * volume;
                    sum += v;
                    sum2 += v * v;
                }
                double mean = sum / samples, se = Math.Sqrt((sum2 / samples - mean * mean) / samples);
                Report("t=" + time.ToString(CultureInfo.InvariantCulture) + ": integral of PdfPoint " + mean.ToString("F5", CultureInfo.InvariantCulture) + " +- " + se.ToString("F5", CultureInfo.InvariantCulture));
                EXPECT_LT(Math.Abs(mean - 1), 4.5 * se + 1e-3);
                // over the directions
                Point3f x = new Point3f(0, 0, 0) + 4.0f * SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                sum = 0;
                sum2 = 0;
                samples = 1000000;
                for (int i = 0; i < samples; ++i)
                {
                    Vector3f w = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    double v = light.PdfDirection(x, w, time) * 4 * Math.PI;
                    sum += v;
                    sum2 += v * v;
                }
                mean = sum / samples;
                se = Math.Sqrt((sum2 / samples - mean * mean) / samples);
                Report("t=" + time.ToString(CultureInfo.InvariantCulture) + ": integral of PdfDirection " + mean.ToString("F5", CultureInfo.InvariantCulture) + " +- " + se.ToString("F5", CultureInfo.InvariantCulture));
                EXPECT_LT(Math.Abs(mean - 1), 4.5 * se + 2e-3);
            }
        }

        // The bounds of the light cover the medium at every time of the shutter
        [TEST("AnimatedLight", "VolumeEmissionLightBoundsCoverTheShutter")]
        public static void VolumeEmissionLightBoundsCoverTheShutter()
        {
            AnimatedTransform motion = VolumeMotion(4);
            TextureMedium medium = MakeVolumeMedium(motion.startTransform, motion);
            VolumeEmissionLight light = new VolumeEmissionLight(medium, 8);
            LightBounds lb = light.Bounds().value();
            RNG rng = new RNG(2, 2);
            double worst = 0;
            for (int i = 0; i < 100000; ++i)
            {
                Float time = rng.Uniform<Float>();
                Point3f pm = new Point3f(-1 + 2 * rng.Uniform<Float>(), -1 + 2 * rng.Uniform<Float>(), -1 + 2 * rng.Uniform<Float>());
                Point3f p = motion.Interpolate(time).Apply(pm);
                worst = Math.Max(worst, Math.Max(Math.Max(Math.Max(lb.bounds.pMin.x - p.x, p.x - lb.bounds.pMax.x), Math.Max(lb.bounds.pMin.y - p.y, p.y - lb.bounds.pMax.y)),
                                                 Math.Max(lb.bounds.pMin.z - p.z, p.z - lb.bounds.pMax.z)));
            }
            Report("the bounds of the emission light cover 1e5 points of the moving medium; the largest distance outside is " + F(worst));
            EXPECT_LT(worst, 1e-5);
            // the static medium has smaller bounds than the moving one
            TextureMedium still = MakeVolumeMedium(motion.startTransform, null);
            LightBounds ls = new VolumeEmissionLight(still, 8).Bounds().value();
            EXPECT_GT(lb.bounds.Diagonal().x + lb.bounds.Diagonal().y + lb.bounds.Diagonal().z, ls.bounds.Diagonal().x + ls.bounds.Diagonal().y + ls.bounds.Diagonal().z);
        }

        // OSL media
        private static ParsedParameter OslString(string name, string value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "string";
            p.name = name;
            p.AddString(value);
            return p;
        }

        private static OSLMedium MakeOslMedium(Transform pose, AnimatedTransform motion)
        {
            List<ParsedParameter> list = new List<ParsedParameter>();
            list.Add(OslString("shader", "vglow"));
            Medium m = Medium.Create("osl", new ParameterDictionary(list, RGBColorSpace.sRGB), pose, new FileLoc(), new Allocator(), motion);
            return (OSLMedium)m;
        }

        private static void ResetOsl()
        {
            OSLScene.Reset();
            OSLSystem.spectralMode = OSLSpectralMode.Upsample;
            OSLSystem.emissionIlluminant = true;
            OSLSystem.AddSearchPath(OslVolumeShaders.Directory());
        }

        [TEST("AnimatedLight", "MovingOslMediumEqualsStaticMediumAtThatTime")]
        public static void MovingOslMediumEqualsStaticMediumAtThatTime()
        {
            ResetOsl();
            AnimatedTransform motion = VolumeMotion(4);
            OSLMedium moving = MakeOslMedium(motion.startTransform, motion);
            EXPECT_TRUE(moving.IsAnimated());
            EXPECT_TRUE(moving.IsEmissive());
            foreach (Float time in new Float[] { 0, 0.3f, 0.7f, 1 })
            {
                OSLMedium reference = MakeOslMedium(motion.Interpolate(time), null);
                RNG rng = new RNG(13, (ulong)(time * 100));
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.31f);
                double worst = 0;
                int nonzero = 0;
                Transform pose = motion.Interpolate(time);
                for (int i = 0; i < 8000; ++i)
                {
                    // the medium is the unit cube of its space
                    Point3f p = pose.Apply(new Point3f(-0.2f + 1.4f * rng.Uniform<Float>(), -0.2f + 1.4f * rng.Uniform<Float>(), -0.2f + 1.4f * rng.Uniform<Float>()));
                    MediumProperties a = moving.SamplePoint(p, lambda, time), b = reference.SamplePoint(p, lambda, time);
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        worst = Math.Max(worst, Math.Abs(a.sigma_a[c] - b.sigma_a[c]));
                        worst = Math.Max(worst, Math.Abs(a.Le[c] - b.Le[c]) / Math.Max(1, Math.Abs(b.Le[c])));
                    }
                    if (a.sigma_a[0] > 0.01f)
                        ++nonzero;
                }
                Report("OSL medium, t=" + time.ToString(CultureInfo.InvariantCulture) + ": moving vs static, largest difference " + F(worst) + " over " + nonzero + " points with density, " +
                       "majorant violations so far: moving " + moving.Violations() + ", static " + reference.Violations());
                EXPECT_GT(nonzero, 500);
                EXPECT_LT(worst, 1e-3);
                EXPECT_EQ(0L, moving.Violations());
            }
        }

        [TEST("AnimatedLight", "MovingOslMediumMajorantsAndEmissionAlongRays")]
        public static void MovingOslMediumMajorantsAndEmissionAlongRays()
        {
            ResetOsl();
            AnimatedTransform motion = VolumeMotion(2);
            OSLMedium moving = MakeOslMedium(motion.startTransform, motion);
            RNG rng = new RNG(71, 1);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.6f);
            int violations = 0, dense = 0, uncovered = 0;
            for (int i = 0; i < 16000; ++i)
            {
                Float time = rng.Uniform<Float>();
                Ray ray = RandomVolumeRay(ref rng, time, out Float tMax);
                List<Segment> segments = Segments(moving, ray, tMax, lambda);
                for (int k = 0; k < 8; ++k)
                {
                    Float t = tMax * rng.Uniform<Float>();
                    MediumProperties mp = moving.SamplePoint(ray.Apply(t), lambda, time);
                    SampledSpectrum sigma_t = mp.sigma_a + mp.sigma_s;
                    if (sigma_t[0] > 0)
                        ++dense;
                    Segment containing = null;
                    foreach (Segment sg in segments)
                        if (t >= sg.tMin && t < sg.tMax)
                            containing = sg;
                    if (containing == null)
                    {
                        if (sigma_t[0] > 1e-6f)
                            ++uncovered;
                        continue;
                    }
                    if (sigma_t[0] > containing.sigma[0] * (1 + 1e-4f) + 1e-6f)
                        ++violations;
                }
            }
            Report("OSL medium moving: " + dense + " points with density on rays at random times, extinction above the majorant " + violations + " times (counter of the medium " +
                   moving.Violations() + "), density outside every segment " + uncovered + " times");
            EXPECT_GT(dense, 1000);
            EXPECT_EQ(0, violations);
            EXPECT_EQ(0L, moving.Violations());
            EXPECT_EQ(0, uncovered);
            // emission along a ray: delta tracking against quadrature
            double worstZ = 0;
            for (int r = 0; r < 5; ++r)
            {
                Float time = (r + 0.5f) / 5;
                Transform pose = motion.Interpolate(time);
                Point3f centre = pose.Apply(new Point3f(0.5f, 0.5f, 0.5f));
                Vector3f dir = Normalize(new Vector3f(0.3f + 0.2f * r, 1, -0.4f + 0.1f * r));
                Ray ray = new Ray(centre - 1.5f * dir + new Vector3f(0.05f * r, 0, 0), dir, time);
                Float tMax = 3.0f;
                int steps = 6000;
                double h = tMax / steps, tau = 0, reference = 0;
                for (int i = 0; i < steps; ++i)
                {
                    MediumProperties mp = moving.SamplePoint(ray.Apply((Float)((i + 0.5) * h)), lambda, time);
                    tau += (mp.sigma_a[0] + mp.sigma_s[0]) * 0.5 * h;
                    reference += mp.sigma_a[0] * mp.Le[0] * Math.Exp(-tau) * h;
                    tau += (mp.sigma_a[0] + mp.sigma_s[0]) * 0.5 * h;
                }
                int n = 20000;
                double sum = 0, sum2 = 0;
                for (int i = 0; i < n; ++i)
                {
                    RNG rg = new RNG(900 + (ulong)r, (ulong)i * 31UL);
                    double L = 0, beta = 1;
                    Float u = rg.Uniform<Float>();
                    SampleT_maj(ray, moving, tMax, u, ref rg, lambda, (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                    {
                        L += beta * mp.sigma_a[0] * mp.Le[0] / sigma_maj[0];
                        beta *= Math.Max(0, sigma_maj[0] - mp.sigma_a[0] - mp.sigma_s[0]) / sigma_maj[0];
                        return beta > 0;
                    });
                    sum += L;
                    sum2 += L * L;
                }
                double mean = sum / n, se = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean) / (n - 1));
                double z = (mean - reference) / Math.Max(se, 1e-12);
                Report("OSL t=" + time.ToString("F2", CultureInfo.InvariantCulture) + ": emission along a ray, delta tracking " + mean.ToString("F5", CultureInfo.InvariantCulture) + " +- " +
                       se.ToString("F5", CultureInfo.InvariantCulture) + ", quadrature " + reference.ToString("F5", CultureInfo.InvariantCulture) + " (z " + z.ToString("F2", CultureInfo.InvariantCulture) + ")");
                if (reference > 1e-3)
                {
                    EXPECT_LT(Math.Abs(z), 4.5);
                    worstZ = Math.Max(worstZ, Math.Abs(z));
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // 6. Scenes: an animated shape with an area light loads, and its emitter is where it is at the time of the ray

        private const string WorldPrefix =
            "Option \"rendercoordsys\" \"world\"\n" +
            "Film \"rgb\" \"integer xresolution\" 8 \"integer yresolution\" 8\n" +
            "Sampler \"independent\"\nCamera \"perspective\"\nWorldBegin\n";

        private sealed class BuiltScene
        {
            public List<Light> lights;
            public Primitive accel;
            public BasicScene scene;
        }

        private static BuiltScene BuildScene(string text)
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

        private const string QuadShape = "Shape \"bilinearmesh\" \"point3 P\" [ -0.5 -0.5 0  0.5 -0.5 0  -0.5 0.5 0  0.5 0.5 0 ]\n";

        private static bool Hits(BuiltScene scene, Point3f o, Float time, out SurfaceInteraction intr)
        {
            Ray ray = new Ray(o, new Vector3f(0, 0, 1), time);
            pstd.optional<ShapeIntersection> si = scene.accel.Intersect(ray, Infinity);
            intr = si ? si.value().intr : default;
            return si.has_value();
        }

        [TEST("AnimatedLight", "SceneWithAnimatedAreaLightLoadsTwoKeys")]
        public static void SceneWithAnimatedAreaLightLoadsTwoKeys()
        {
            BuiltScene built = BuildScene(WorldPrefix + "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [ 4 4 4 ] \"bool twosided\" true\n" +
                                          "ActiveTransform StartTime\nTranslate -1 0 3\nActiveTransform EndTime\nTranslate 1 0 3\nActiveTransform All\n" + QuadShape + "AttributeEnd\n");
            ASSERT_EQ(1, built.lights.Count);
            DiffuseAreaLight light = (DiffuseAreaLight)built.lights[0];
            EXPECT_TRUE(light.IsAnimated());
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            // at time 0 the emitter is at x = -1, at time 1 at x = 1, at 0.5 at x = 0
            EXPECT_TRUE(Hits(built, new Point3f(-1, 0, 0), 0, out SurfaceInteraction a));
            EXPECT_TRUE(a.areaLight == light);
            EXPECT_NEAR(3, a.p().z, 1e-5);
            EXPECT_FALSE(Hits(built, new Point3f(1, 0, 0), 0, out SurfaceInteraction a2));
            EXPECT_FALSE(Hits(built, new Point3f(-1, 0, 0), 1, out SurfaceInteraction b2));
            EXPECT_TRUE(Hits(built, new Point3f(1, 0, 0), 1, out SurfaceInteraction b));
            EXPECT_TRUE(b.areaLight == light);
            EXPECT_TRUE(Hits(built, new Point3f(0, 0, 0), 0.5f, out SurfaceInteraction c));
            EXPECT_FALSE(Hits(built, new Point3f(-1, 0, 0), 0.5f, out SurfaceInteraction c2));
            // the emission seen by a ray at that time
            SampledSpectrum Le = c.Le(new Vector3f(0, 0, -1), lambda);
            EXPECT_GT(Le[0], 0);
            BuiltScene still = BuildScene(WorldPrefix + "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [ 4 4 4 ] \"bool twosided\" true\nTranslate 0 0 3\n" + QuadShape + "AttributeEnd\n");
            EXPECT_TRUE(Hits(still, new Point3f(0, 0, 0), 0, out SurfaceInteraction s));
            EXPECT_EQ((float)s.Le(new Vector3f(0, 0, -1), lambda)[0], (float)Le[0]);
            // light sampling at 0.5: the point is on the emitter at x = 0
            LightSampleContext ctx = Context(new Point3f(0, 0, 0), new Normal3f(0, 0, 1), 0.5f);
            for (int i = 0; i < 50; ++i)
            {
                pstd.optional<LightLiSample> ls = light.SampleLi(ctx, new Point2f((i + 0.5f) / 50, (i * 0.618f) % 1), lambda);
                ASSERT_TRUE(ls.has_value());
                EXPECT_LE(Math.Abs(ls.value().pLight.p().x), 0.5001f);
                EXPECT_LE(Math.Abs(ls.value().pLight.p().y), 0.5001f);
                EXPECT_NEAR(3, ls.value().pLight.p().z, 1e-5);
                EXPECT_EQ(0.5f, ls.value().pLight.time);
            }
            // and at time 1 it is at x = 1
            pstd.optional<LightLiSample> l1 = light.SampleLi(Context(new Point3f(0, 0, 0), new Normal3f(0, 0, 1), 1), new Point2f(0.5f, 0.5f), lambda);
            EXPECT_NEAR(1, l1.value().pLight.p().x, 0.5001);
        }

        [TEST("AnimatedLight", "SceneWithAnimatedAreaLightLoadsFourKeys")]
        public static void SceneWithAnimatedAreaLightLoadsFourKeys()
        {
            BuiltScene built = BuildScene(WorldPrefix + "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [ 4 4 4 ] \"bool twosided\" true\n" +
                                          "MotionBegin [ 0 0.25 0.5 1 ]\n Translate -1 0 3\n Translate 0 -1 3\n Translate 0 1 3\n Translate 1 0 3\nMotionEnd\n" + QuadShape + "AttributeEnd\n");
            ASSERT_EQ(1, built.lights.Count);
            DiffuseAreaLight light = (DiffuseAreaLight)built.lights[0];
            EXPECT_TRUE(light.IsAnimated());
            EXPECT_TRUE(Hits(built, new Point3f(0, -1, 0), 0.25f, out SurfaceInteraction a));
            EXPECT_TRUE(a.areaLight == light);
            EXPECT_TRUE(Hits(built, new Point3f(0, 1, 0), 0.5f, out SurfaceInteraction b));
            EXPECT_TRUE(b.areaLight == light);
            EXPECT_FALSE(Hits(built, new Point3f(0, 1, 0), 0.25f, out SurfaceInteraction c));
            EXPECT_TRUE(Hits(built, new Point3f(0.5f, 0, 0), 0.75f, out SurfaceInteraction d));
            Bounds3f lb = light.Bounds().value().bounds;
            EXPECT_LE(lb.pMin.x, -1.5f);
            EXPECT_GE(lb.pMax.x, 1.5f);
            EXPECT_LE(lb.pMin.y, -1.5f);
            EXPECT_GE(lb.pMax.y, 1.5f);
        }

        [TEST("AnimatedLight", "StaticSceneKeepsStaticLights")]
        public static void StaticSceneKeepsStaticLights()
        {
            BuiltScene built = BuildScene(WorldPrefix + "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [ 4 4 4 ]\nTranslate 0 0 3\n" + QuadShape + "AttributeEnd\n");
            ASSERT_EQ(1, built.lights.Count);
            EXPECT_FALSE(((DiffuseAreaLight)built.lights[0]).IsAnimated());
        }

        // ------------------------------------------------------------------------------------------
        // 7. Renders: the image of a moving emitter is the mean of the images of static emitters

        // The pose at a time, written independently of AnimatedTransform: the rotation is about a fixed axis and the angle and the position are
        // piecewise linear in the times of the keys. (A rotation about a fixed axis is what the interpolation of the rotations of the keys makes.)
        private sealed class PoseSpec
        {
            public Float[] times;
            public Vector3f[] position;
            public Float[] degrees;
            public Vector3f axis;
            public Float scale = 1;
            public Vector3f pivot = new Vector3f(0, 0, 0);

            public Transform At(Float time)
            {
                time = Math.Max(times[0], Math.Min(times[times.Length - 1], time));
                int k = 0;
                while (k < times.Length - 2 && time >= times[k + 1])
                    ++k;
                Float f = (time - times[k]) / (times[k + 1] - times[k]);
                Vector3f p = position[k] * (1 - f) + position[k + 1] * f;
                Float angle = degrees[k] * (1 - f) + degrees[k + 1] * f;
                return Translate(p) * Rotate(angle, Normalize(axis)) * Scale(scale, scale, scale) * Translate(pivot);
            }

            private static string Matrix(Transform t)
            {
                // the columns of the matrix are the images of the axes and of the origin
                Vector3f ex = t.Apply(new Vector3f(1, 0, 0)), ey = t.Apply(new Vector3f(0, 1, 0)), ez = t.Apply(new Vector3f(0, 0, 1));
                Point3f o = t.Apply(new Point3f(0, 0, 0));
                Float[] v = new Float[] { ex.x, ex.y, ex.z, 0, ey.x, ey.y, ey.z, 0, ez.x, ez.y, ez.z, 0, o.x, o.y, o.z, 1 };
                string text = "";
                for (int i = 0; i < 16; ++i)
                    text += v[i].ToString("R", CultureInfo.InvariantCulture) + " ";
                return "[ " + text + "]";
            }

            public string Moving()
            {
                if (times.Length == 2)
                    return "ActiveTransform StartTime\nConcatTransform " + Matrix(At(times[0])) + "\nActiveTransform EndTime\nConcatTransform " + Matrix(At(times[1])) + "\nActiveTransform All\n";
                string text = "MotionBegin [ ";
                foreach (Float t in times)
                    text += t.ToString("R", CultureInfo.InvariantCulture) + " ";
                text += "]\n";
                foreach (Float t in times)
                    text += "  ConcatTransform " + Matrix(At(t)) + "\n";
                return text + "MotionEnd\n";
            }

            public string Still(Float time)
            {
                return "ConcatTransform " + Matrix(At(time)) + "\n";
            }
        }

        private static PoseSpec SpinningQuad()
        {
            PoseSpec spec = new PoseSpec();
            spec.times = new Float[] { 0, 0.3f, 0.65f, 1 };
            spec.position = new Vector3f[] { new Vector3f(-2.2f, 2.4f, 0.2f), new Vector3f(-0.7f, 3.3f, -0.5f), new Vector3f(0.9f, 2.0f, 0.6f), new Vector3f(2.2f, 2.9f, 0.2f) };
            spec.degrees = new Float[] { 0, 80, 170, 250 };
            spec.axis = new Vector3f(1, 0.3f, 0);
            return spec;
        }

        private static PoseSpec BouncingBall()
        {
            PoseSpec spec = new PoseSpec();
            spec.times = new Float[] { 0, 0.5f, 1 };
            spec.position = new Vector3f[] { new Vector3f(-2.0f, 0.45f, -0.6f), new Vector3f(0, 2.6f, -0.6f), new Vector3f(2.0f, 0.45f, -0.6f) };
            spec.degrees = new Float[] { 0, 0, 0 };
            spec.axis = new Vector3f(0, 1, 0);
            return spec;
        }

        private const string Room =
            "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.55 0.55 0.55]\nShape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-7 0 -7  7 0 -7  7 0 7  -7 0 7]\nAttributeEnd\n" +
            "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.25 0.3 0.6]\nShape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-7 0 3.2  7 0 3.2  7 6 3.2  -7 6 3.2]\nAttributeEnd\n" +
            "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.8 0.45 0.3]\nTranslate 0.2 0.8 0.3\nShape \"sphere\" \"float radius\" 0.8\nAttributeEnd\n";

        private static string Header(string integrator, string arguments, int xres, int yres)
        {
            return "LookAt 0 2.6 -7.2  0 0.7 0  0 1 0\nCamera \"perspective\" \"float fov\" 38\nSampler \"independent\"\n" +
                   "Integrator \"" + integrator + "\" " + arguments + "\nFilm \"rgb\" \"integer xresolution\" " + xres + " \"integer yresolution\" " + yres + " \"string filename\" \"out.exr\"\nWorldBegin\n";
        }

        private static string QuadEmitter(string pose)
        {
            return "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [14 13 11] \"bool twosided\" true\n" + pose +
                   "Shape \"bilinearmesh\" \"point3 P\" [ -0.6 -0.6 0  0.6 -0.6 0  -0.6 0.6 0  0.6 0.6 0 ] \"integer indices\" [0 1 2 3]\nAttributeEnd\n";
        }

        // The same square as two triangles
        private static string TriangleQuadEmitter(string pose)
        {
            return "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [14 13 11] \"bool twosided\" true\n" + pose +
                   "Shape \"trianglemesh\" \"point3 P\" [ -0.6 -0.6 0  0.6 -0.6 0  0.6 0.6 0  -0.6 0.6 0 ] \"integer indices\" [0 1 2 0 2 3]\nAttributeEnd\n";
        }

        private static string BallEmitter(string pose)
        {
            return "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [30 24 14]\n" + pose + "Shape \"sphere\" \"float radius\" 0.35\nAttributeEnd\n";
        }

        private const int xres = 64, yres = 48, stills = 16;

        private static OslRender Mean(OslRender[] images)
        {
            OslRender mean = new OslRender();
            mean.width = images[0].width;
            mean.height = images[0].height;
            mean.rgb = new float[images[0].rgb.Length];
            foreach (OslRender image in images)
                for (int i = 0; i < mean.rgb.Length; ++i)
                    mean.rgb[i] += image.rgb[i] / images.Length;
            return mean;
        }

        private static OslRender RenderStills(string name, Func<string, string> scene, PoseSpec pose, int spp)
        {
            OslRender[] images = new OslRender[stills];
            for (int i = 0; i < stills; ++i)
                images[i] = OslRenderTools.Render(name + "_still" + i, scene(pose.Still((i + 0.5f) / stills)), spp, 1000 + i, "", OslVolumeShaders.Directory());
            return Mean(images);
        }

        // The image of the moving emitter, rendered with two seeds, against the mean of the stills at the times of the shutter. The noise floor is the
        // difference between the two seeds; the difference to the stills must stay within it (the stills have the noise of 16 renders of 1/4 of the samples,
        // so their share of the difference is smaller than the one of a render), and an image of the emitter at its first pose must be much further away.
        private static void CompareMovingWithStills(string label, string name, Func<string, string> movingScene, OslRender truth, OslRender firstPose, int spp, double allowedRatio)
        {
            OslRender a = OslRenderTools.Render(name + "_a", movingScene(null), spp, 1, "", OslVolumeShaders.Directory());
            OslRender b = OslRenderTools.Render(name + "_b", movingScene(null), spp, 2, "", OslVolumeShaders.Directory());
            ASSERT_TRUE(a != null && b != null && truth != null);
            double floor = OslRender.MeanAbsDiff(a, b);
            double toTruthA = OslRender.MeanAbsDiff(a, truth), toTruthB = OslRender.MeanAbsDiff(b, truth);
            double toFirst = OslRender.MeanAbsDiff(a, firstPose);
            Report(label + ": mean abs difference to the mean of " + stills + " stills " + toTruthA.ToString("F5", CultureInfo.InvariantCulture) + " and " + toTruthB.ToString("F5", CultureInfo.InvariantCulture) +
                   " (two seeds), noise floor (seed against seed) " + floor.ToString("F5", CultureInfo.InvariantCulture) + ", emitter fixed at its first pose " + toFirst.ToString("F5", CultureInfo.InvariantCulture));
            EXPECT_LT(toTruthA, allowedRatio * floor);
            EXPECT_LT(toTruthB, allowedRatio * floor);
            EXPECT_GT(toFirst, 3 * floor);
        }

        [TEST("AnimatedLight", "RenderMovingQuadLightWithPathVolpathSimplepathBdpt")]
        public static void RenderMovingQuadLightWithPathVolpathSimplepathBdpt()
        {
            PoseSpec pose = SpinningQuad();
            OslRender truth = RenderStills("anim_quad", (string p) => Header("path", "\"integer maxdepth\" 5", xres, yres) + Room + QuadEmitter(p), pose, 32);
            OslRender first = OslRenderTools.Render("anim_quad_first", Header("path", "\"integer maxdepth\" 5", xres, yres) + Room + QuadEmitter(pose.Still(0)), 256, 7, "", OslVolumeShaders.Directory());
            foreach (string integrator in new string[] { "path", "volpath", "simplepath", "bdpt" })
            {
                string integratorName = integrator;
                CompareMovingWithStills("quad light, 4 keys, " + integrator + " 128 spp", "anim_quad_" + integrator,
                                        (string unused) => Header(integratorName, "\"integer maxdepth\" 5", xres, yres) + Room + QuadEmitter(pose.Moving()), truth, first, 128, 1.5);
            }
        }

        [TEST("AnimatedLight", "RenderMovingBallLightSeenDirectlyAndLightingTheScene")]
        public static void RenderMovingBallLightSeenDirectlyAndLightingTheScene()
        {
            PoseSpec pose = BouncingBall();
            OslRender truth = RenderStills("anim_ball", (string p) => Header("path", "\"integer maxdepth\" 5", xres, yres) + Room + BallEmitter(p), pose, 32);
            OslRender first = OslRenderTools.Render("anim_ball_first", Header("path", "\"integer maxdepth\" 5", xres, yres) + Room + BallEmitter(pose.Still(0)), 256, 7, "", OslVolumeShaders.Directory());
            foreach (string integrator in new string[] { "path", "volpath", "simplepath" })
            {
                string integratorName = integrator;
                CompareMovingWithStills("ball light, 3 keys, " + integrator + " 128 spp", "anim_ball_" + integrator,
                                        (string unused) => Header(integratorName, "\"integer maxdepth\" 5", xres, yres) + Room + BallEmitter(pose.Moving()), truth, first, 128, 1.5);
            }
        }

        // Light sampling alone, BSDF sampling alone and both with MIS give the same image: the density of the light at the point that a ray of the BSDF
        // sampling hits, at the time of the ray, is the one of the sample of the light (otherwise the weights of MIS do not add to one)
        [TEST("AnimatedLight", "RenderStrategiesOfSimplepathAgreeForMovingLights")]
        public static void RenderStrategiesOfSimplepathAgreeForMovingLights()
        {
            foreach (int which in new int[] { 0, 1 })
            {
                PoseSpec pose = which == 0 ? SpinningQuad() : BouncingBall();
                Func<string, string> emitter = which == 0 ? (Func<string, string>)TriangleQuadEmitter : BallEmitter;
                string name = which == 0 ? "triangle quad" : "ball";
                string[] arguments = new string[] { "\"integer maxdepth\" 5 \"bool samplebsdf\" false", "\"integer maxdepth\" 5 \"bool samplelights\" false", "\"integer maxdepth\" 5" };
                string[] modes = new string[] { "lights only", "BSDF only", "MIS" };
                OslRender[] images = new OslRender[3], second = new OslRender[3];
                for (int m = 0; m < 3; ++m)
                {
                    images[m] = OslRenderTools.Render("anim_strategy_" + name + m, Header("simplepath", arguments[m], xres, yres) + Room + emitter(pose.Moving()), 512, 1, "", OslVolumeShaders.Directory());
                    second[m] = OslRenderTools.Render("anim_strategy_" + name + m, Header("simplepath", arguments[m], xres, yres) + Room + emitter(pose.Moving()), 512, 2, "", OslVolumeShaders.Directory());
                }
                for (int m = 0; m < 3; ++m)
                {
                    double floor = OslRender.MeanAbsDiff(images[m], second[m]);
                    double toMis = OslRender.MeanAbsDiff(images[m], images[2]);
                    double toMisSecond = OslRender.MeanAbsDiff(images[m], second[2]);
                    double floorMis = OslRender.MeanAbsDiff(images[2], second[2]);
                    // the difference of two independent images has the noise of both: sqrt(floor^2 + floorMis^2) / sqrt(2) in expectation, with margin
                    double noise = Math.Sqrt(0.5 * (floor * floor + floorMis * floorMis));
                    Report(name + ", simplepath " + modes[m] + " 512 spp: seed against seed " + floor.ToString("F5", CultureInfo.InvariantCulture) + ", against MIS " +
                           toMis.ToString("F5", CultureInfo.InvariantCulture) + " and " + toMisSecond.ToString("F5", CultureInfo.InvariantCulture) + " (expected from noise alone about " + noise.ToString("F5", CultureInfo.InvariantCulture) + ")");
                    if (m != 2)
                        EXPECT_LT(Math.Max(toMis, toMisSecond), 1.35 * noise);
                }
            }
        }

        // (a measurement, not a check) The same strategies with the emitter as a bilinear patch. In the code before this change a static two sided
        // bilinear patch already gives an image with light sampling alone that is 6 to 11 % brighter (depending on the pose) than the one with BSDF sampling alone (the same two
        // triangles agree), so the difference is not made by the motion: it is reported here and not asserted.
        [TEST("AnimatedLight", "ReportBilinearPatchLightSamplingAgainstBSDFSampling")]
        public static void ReportBilinearPatchLightSamplingAgainstBSDFSampling()
        {
            foreach (bool moving in new bool[] { false, true })
            {
                PoseSpec pose = SpinningQuad();
                string placement = moving ? pose.Moving() : pose.Still(0.5f);
                OslRender lights = OslRenderTools.Render("anim_bilinear_L" + (moving ? "m" : "s"), Header("simplepath", "\"integer maxdepth\" 5 \"bool samplebsdf\" false", xres, yres) + Room + QuadEmitter(placement), 512, 1, "", OslVolumeShaders.Directory());
                OslRender bsdf = OslRenderTools.Render("anim_bilinear_B" + (moving ? "m" : "s"), Header("simplepath", "\"integer maxdepth\" 5 \"bool samplelights\" false", xres, yres) + Room + QuadEmitter(placement), 512, 1, "", OslVolumeShaders.Directory());
                double meanLights = 0, meanBsdf = 0;
                for (int i = 0; i < lights.rgb.Length; ++i)
                {
                    meanLights += lights.rgb[i];
                    meanBsdf += bsdf.rgb[i];
                }
                Report("bilinear patch emitter, " + (moving ? "moving (4 keys)" : "static") + ", simplepath 512 spp: mean of the image with light sampling alone / BSDF sampling alone = " +
                       (meanLights / meanBsdf).ToString("F4", CultureInfo.InvariantCulture));
            }
        }

        // Emissive media
        private static string GridMedium(string pose, string firstPose, bool nee)
        {
            Random random = new Random(7);
            string values = "";
            for (int i = 0; i < 512; ++i)
            {
                int x = i % 8, y = (i / 8) % 8, z = i / 64;
                double d = 0;
                double[][] centres = new double[][] { new double[] { 0.3, 0.35, 0.4 }, new double[] { 0.7, 0.6, 0.35 }, new double[] { 0.5, 0.75, 0.7 }, new double[] { 0.35, 0.3, 0.75 } };
                foreach (double[] c in centres)
                    d += Math.Exp(-((x + 0.5) / 8 - c[0]) * ((x + 0.5) / 8 - c[0]) / (2 * 0.13 * 0.13) - ((y + 0.5) / 8 - c[1]) * ((y + 0.5) / 8 - c[1]) / (2 * 0.13 * 0.13) -
                                   ((z + 0.5) / 8 - c[2]) * ((z + 0.5) / 8 - c[2]) / (2 * 0.13 * 0.13));
                values += (d / 1.6).ToString("F5", CultureInfo.InvariantCulture) + " ";
            }
            return "AttributeBegin\n" + firstPose +
                   "Texture \"dens\" \"float\" \"densitygrid\" \"integer nx\" 8 \"integer ny\" 8 \"integer nz\" 8 \"float density\" [ " + values + "]\n" +
                   "Texture \"em\" \"spectrum\" \"constant\" \"rgb value\" [1.0 0.55 0.25]\nAttributeEnd\nAttributeBegin\n" + pose +
                   "MakeNamedMedium \"glow\" \"string type\" \"texture\" \"string density\" \"dens\" \"rgb sigma_a\" [2 2.5 3] \"rgb sigma_s\" [0.6 0.6 0.6] \"float densityscale\" 2.5 " +
                   "\"integer majorantres\" 8 \"string emission_texture\" \"em\" \"float Lescale\" 6 \"bool emission_nee\" " + (nee ? "true" : "false") + "\nAttributeEnd\n";
        }

        private static string OslGlowMedium(string pose)
        {
            return "AttributeBegin\n" + pose + "MakeNamedMedium \"glow\" \"string type\" \"osl\" \"string shader\" \"vglow\" \"float amp\" 7 \"float width\" 0.22 \"float emit\" 6\nAttributeEnd\n";
        }

        private const string VolumeRoom =
            "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.55 0.55 0.55]\nShape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-7 0 -7  7 0 -7  7 0 7  -7 0 7]\nAttributeEnd\n" +
            "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.25 0.3 0.6]\nShape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-7 0 3.2  7 0 3.2  7 6 3.2  -7 6 3.2]\nAttributeEnd\n" +
            "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.8 0.45 0.3]\nTranslate 0.2 0.5 -0.9\nShape \"sphere\" \"float radius\" 0.5\nAttributeEnd\n" +
            "AttributeBegin\nMediumInterface \"glow\" \"\"\nMaterial \"interface\"\nTranslate 0 1.8 0\nShape \"sphere\" \"float radius\" 4.4\nAttributeEnd\n";

        private static PoseSpec MovingVolume(bool osl)
        {
            PoseSpec spec = new PoseSpec();
            spec.scale = 1.7f;
            spec.pivot = new Vector3f(-0.5f, -0.5f, -0.5f);
            if (osl)
            {
                spec.times = new Float[] { 0, 0.5f, 1 };
                spec.position = new Vector3f[] { new Vector3f(-1.8f, 1.3f, 0), new Vector3f(0, 2.3f, 0.2f), new Vector3f(1.8f, 1.3f, 0) };
                spec.degrees = new Float[] { 0, 90, 200 };
                spec.axis = new Vector3f(0, 1, 0.3f);
            }
            else
            {
                spec.times = new Float[] { 0, 0.3f, 0.7f, 1 };
                spec.position = new Vector3f[] { new Vector3f(-1.9f, 1.2f, 0), new Vector3f(-0.6f, 2.3f, -0.4f), new Vector3f(0.8f, 1.4f, 0.5f), new Vector3f(1.9f, 2.1f, 0) };
                spec.degrees = new Float[] { 0, 70, 160, 260 };
                spec.axis = new Vector3f(0.3f, 1, 0.2f);
            }
            return spec;
        }

        [TEST("AnimatedLight", "RenderMovingEmissiveVolumesWithVolpath")]
        public static void RenderMovingEmissiveVolumesWithVolpath()
        {
            foreach (int which in new int[] { 0, 1, 2 })
            {
                bool osl = which == 2;
                bool nee = which != 1;
                PoseSpec pose = MovingVolume(osl);
                Func<string, string> medium = (string p) => osl ? OslGlowMedium(p) : GridMedium(p, pose.Still(0), nee);
                Func<string, string, string> scene = (string integratorArguments, string p) => Header("volpath", integratorArguments, xres, yres) + VolumeRoom.Replace("AttributeBegin\nMediumInterface", medium(p) + "AttributeBegin\nMediumInterface");
                string name = osl ? "anim_oslvolume" : (nee ? "anim_gridvolume" : "anim_gridvolume_tracking");
                // the stills are those with the light sampling of the emission (it must agree with the tracking alone: the same physical image)
                OslRender[] images = new OslRender[stills];
                for (int i = 0; i < stills; ++i)
                {
                    Float time = (i + 0.5f) / stills;
                    Func<string, string> mediumStill = (string p) => osl ? OslGlowMedium(p) : GridMedium(p, p, true);
                    string text = Header("volpath", "\"integer maxdepth\" 6", xres, yres) + VolumeRoom.Replace("AttributeBegin\nMediumInterface", mediumStill(pose.Still(time)) + "AttributeBegin\nMediumInterface");
                    images[i] = OslRenderTools.Render((osl ? "anim_osl" : "anim_grid") + "_still" + i, text, 48, 3000 + i, "", OslVolumeShaders.Directory());
                }
                OslRender truth = Mean(images);
                string firstText = Header("volpath", "\"integer maxdepth\" 6", xres, yres) + VolumeRoom.Replace("AttributeBegin\nMediumInterface", (osl ? OslGlowMedium(pose.Still(0)) : GridMedium(pose.Still(0), pose.Still(0), true)) + "AttributeBegin\nMediumInterface");
                OslRender first = OslRenderTools.Render((osl ? "anim_osl" : "anim_grid") + "_first", firstText, 512, 7, "", OslVolumeShaders.Directory());
                CompareMovingWithStills((osl ? "OSL emissive medium, 3 keys" : (nee ? "grid emissive medium, 4 keys, NEE of the emission" : "grid emissive medium, 4 keys, tracking only")) + ", volpath 128 spp", name,
                                        (string unused) => scene("\"integer maxdepth\" 6", pose.Moving()), truth, first, 128, 1.5);
            }
        }
    }
}
