// (not in pbrt-v4) Micro benchmark of DiffuseAreaLight.SampleLi, L and PDF_Li for static lights, and of SampleLi for a light that moves. The file compiles
// against the code before the change as well (the lines between BEGIN-NEW and END-NEW and the marker TIME are removed for that), so that the same measurement
// is made on both: see docs/animated-transforms.md.

using System;
using System.Diagnostics;
using System.Globalization;
using testing;

namespace pbrt
{
    public static unsafe class AnimatedLightBenchmark
    {
        private static Shape MakeShape(string kind)
        {
            Transform renderFromObject = Translate(new Vector3f(0.3f, 0.2f, 0.1f)) * Rotate(35, Normalize(new Vector3f(1, 1, 0)));
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
                                                         new pstd.vector<Point3f>(new Point3f[] { new Point3f(-1, -1, 0), new Point3f(1, -1, 0), new Point3f(0, 1, 0) }),
                                                         new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(),
                                                         new pstd.vector<int>(), new Allocator());
                    return Triangle.CreateTriangles(mesh, new Allocator())[0];
                }
                case "sphere":
                    return new Sphere(renderFromObject, objectFromRender, false, 0.7f, -0.7f, 0.7f, 360);
                default:
                    return new Disk(renderFromObject, objectFromRender, false, 0, 0.8f, 0, 360);
            }
        }

        private static DiffuseAreaLight StaticLight(string kind)
        {
            Transform renderFromLight = Translate(new Vector3f(0.3f, 0.2f, 0.1f)) * Rotate(35, Normalize(new Vector3f(1, 1, 0)));
            return new DiffuseAreaLight(renderFromLight, new MediumInterface(), new ConstantSpectrum(2.0f), 1.5f, MakeShape(kind), null, new Image(), null, true);
        }

        private const int Calls = 100000;
        private const int Runs = 9;

        private static void Receivers(out Point3f[] points, out Normal3f[] normals, out Point2f[] us)
        {
            RNG rng = new RNG(5, 5);
            points = new Point3f[Calls];
            normals = new Normal3f[Calls];
            us = new Point2f[Calls];
            for (int i = 0; i < Calls; ++i)
            {
                Vector3f dir = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                points[i] = new Point3f(0, 0, 0) + (3.2f + 2.5f * rng.Uniform<Float>()) * dir;
                normals[i] = new Normal3f(-dir.x, -dir.y, -dir.z);
                us[i] = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
            }
        }

        private static double Best(Action action)
        {
            action();
            double best = double.MaxValue;
            for (int run = 0; run < Runs; ++run)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                action();
                best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds * 1e6 / Calls);
            }
            return best;
        }

        private static float sink;

        [TEST("AnimatedLightBenchmark", "StaticAreaLightSampleLiLAndPDF")]
        public static void StaticAreaLightSampleLiLAndPDF()
        {
            Receivers(out Point3f[] points, out Normal3f[] normals, out Point2f[] us);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            foreach (string kind in new string[] { "quad", "triangle", "sphere", "disk" })
            {
                DiffuseAreaLight light = StaticLight(kind);
                Vector3f w = Normalize(new Vector3f(0.2f, 0.7f, -0.3f));
                double sample = Best(() =>
                {
                    float sum = 0;
                    for (int i = 0; i < Calls; ++i)
                    {
                        LightSampleContext ctx = new LightSampleContext(new Point3fi(points[i]), normals[i], normals[i]);
                        pstd.optional<LightLiSample> ls = light.SampleLi(ctx, us[i], lambda);
                        if (ls)
                            sum += ls.value().pdf;
                    }
                    sink += sum;
                });
                double radiance = Best(() =>
                {
                    float sum = 0;
                    Point3f p = new Point3f(0.3f, 0.2f, 0.1f);
                    Normal3f n = new Normal3f(0, 0, 1);
                    for (int i = 0; i < Calls; ++i)
                    {
                        Vector3f wi = Normalize(points[i] - p);
                        sum += light.L(p, n, new Point2f(0.5f, 0.5f), wi, lambda, 0 /*TIME*/)[0];
                    }
                    sink += sum;
                });
                double density = Best(() =>
                {
                    float sum = 0;
                    for (int i = 0; i < Calls; ++i)
                    {
                        LightSampleContext ctx = new LightSampleContext(new Point3fi(points[i]), normals[i], normals[i]);
                        sum += light.PDF_Li(ctx, Normalize(new Point3f(0.3f, 0.2f, 0.1f) - points[i]));
                    }
                    sink += sum;
                });
                Console.WriteLine("[AnimatedLightBenchmark] static " + kind + ": SampleLi " + sample.ToString("F1", CultureInfo.InvariantCulture) + " ns, L " +
                                  radiance.ToString("F1", CultureInfo.InvariantCulture) + " ns, PDF_Li " + density.ToString("F1", CultureInfo.InvariantCulture) + " ns (best of " + Runs + " runs of " + Calls + " calls)");
            }
        }

        // BEGIN-NEW
        [TEST("AnimatedLightBenchmark", "AnimatedAreaLightSampleLi")]
        public static void AnimatedAreaLightSampleLi()
        {
            Receivers(out Point3f[] points, out Normal3f[] normals, out Point2f[] us);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            AnimatedTransform motion = new AnimatedTransform(new Transform[] { Translate(new Vector3f(-1, 0, 0)), Translate(new Vector3f(0, 0.6f, 0)) * Rotate(60, new Vector3f(0, 0, 1)),
                                                                               Translate(new Vector3f(1, 0, 0)) * Rotate(120, new Vector3f(0, 0, 1)) },
                                                             new Float[] { 0, 0.5f, 1 });
            foreach (string kind in new string[] { "quad", "triangle", "sphere", "disk" })
            {
                // the shape in its own space
                DiffuseAreaLight still = StaticLight(kind);
                DiffuseAreaLight light = new DiffuseAreaLight(motion.startTransform, new MediumInterface(), new ConstantSpectrum(2.0f), 1.5f, MakeShapeInOwnSpace(kind), null, new Image(), null, true);
                light.SetMotion(motion);
                double sample = Best(() =>
                {
                    float sum = 0;
                    for (int i = 0; i < Calls; ++i)
                    {
                        LightSampleContext ctx = new LightSampleContext(new Point3fi(points[i]), normals[i], normals[i], us[i].x);
                        pstd.optional<LightLiSample> ls = light.SampleLi(ctx, us[i], lambda);
                        if (ls)
                            sum += ls.value().pdf;
                    }
                    sink += sum;
                });
                Console.WriteLine("[AnimatedLightBenchmark] animated (3 keys) " + kind + ": SampleLi " + sample.ToString("F1", CultureInfo.InvariantCulture) + " ns (best of " + Runs + " runs of " + Calls + " calls)");
            }
        }

        private static Shape MakeShapeInOwnSpace(string kind)
        {
            Transform identity = new Transform();
            switch (kind)
            {
                case "quad":
                {
                    BilinearPatchMesh mesh = new BilinearPatchMesh(identity, false, new pstd.vector<int>(new int[] { 0, 1, 2, 3 }),
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
                    TriangleMesh mesh = new TriangleMesh(identity, false, new pstd.vector<int>(new int[] { 0, 1, 2 }),
                                                         new pstd.vector<Point3f>(new Point3f[] { new Point3f(-1, -1, 0), new Point3f(1, -1, 0), new Point3f(0, 1, 0) }),
                                                         new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(),
                                                         new pstd.vector<int>(), new Allocator());
                    return Triangle.CreateTriangles(mesh, new Allocator())[0];
                }
                case "sphere":
                    return new Sphere(identity, identity, false, 0.7f, -0.7f, 0.7f, 360);
                default:
                    return new Disk(identity, identity, false, 0, 0.8f, 0, 360);
            }
        }
        // END-NEW
    }
}
