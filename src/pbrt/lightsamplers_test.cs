// lightsamplers_test.cpp
//
// Differences from the C++ test: std::unordered_map<Light, int> is a Dictionary keyed by the
// Light object reference; std::vector<Light> is a Light[] converted to pstd.span<Light>;
// Interaction arguments to Sample()/SampleLi() are wrapped in LightSampleContext explicitly.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class LightSamplersTests
    {
        [TEST("BVHLightSampling", "OneSpot")]
        public static void BVHLightSampling_OneSpot()
        {
            Transform id = new Transform();
            List<Light> lights = new List<Light>();
            ConstantSpectrum one = new ConstantSpectrum(1.0f);
            lights.Add(new SpotLight(id, new MediumInterface(), one, 1.0f /* scale */,
                                     45.0f /* total width */, 44.0f /* falloff start */));
            BVHLightSampler distrib = new BVHLightSampler(lights.ToArray(), new Allocator());

            RNG rng = new RNG();
            for (int i = 0; i < 100; ++i)
            {
                // Random point in [-5, 5]
                Float px = Lerp(rng.Uniform<Float>(), -5, 5);
                Float py = Lerp(rng.Uniform<Float>(), -5, 5);
                Float pz = Lerp(rng.Uniform<Float>(), -5, 5);
                Point3f p = new Point3f(px, py, pz);

                // Avoid possibly ambiguous cases right at the edge.
                Float angle = Degrees(std.acos(Normalize((Vector3f)p).z));
                if (44.75f < angle && angle < 45.25f)
                {
                    --i;
                    continue;
                }

                Interaction inter = new Interaction(p, 0.0f, (Medium)null);
                Float ux = rng.Uniform<Float>();
                Float uy = rng.Uniform<Float>();
                Point2f u = new Point2f(ux, uy);
                SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
                LightSampleContext ctx = new LightSampleContext(inter);
                pstd.optional<LightLiSample> ls = lights[0].SampleLi(ctx, u, lambda);

                pstd.optional<SampledLight> sampledLight = distrib.Sample(ctx, rng.Uniform<Float>());

                if (!sampledLight.has_value())
                {
                    EXPECT_FALSE(ls.has_value());
                    continue;
                }
                else
                    EXPECT_TRUE(ls.has_value());

                EXPECT_EQ(1, sampledLight.value().p);
                EXPECT_TRUE(sampledLight.value().light == lights[0]);
                EXPECT_TRUE((bool)ls.value().L, ls.value().L + " @ " + p);
            }
        }

        // For a random collection of point lights, make sure that they're all sampled
        // with an appropriate ratio of frequency to pdf.
        [TEST("BVHLightSampling", "Point")]
        public static void BVHLightSampling_Point()
        {
            RNG rng = new RNG();
            List<Light> lights = new List<Light>();
            Dictionary<Light, int> lightToIndex = new Dictionary<Light, int>();
            ConstantSpectrum one = new ConstantSpectrum(1.0f);
            for (int i = 0; i < 33; ++i)
            {
                // Random point in [-5, 5]
                Float px = Lerp(rng.Uniform<Float>(), -5, 5);
                Float py = Lerp(rng.Uniform<Float>(), -5, 5);
                Float pz = Lerp(rng.Uniform<Float>(), -5, 5);
                Vector3f p = new Vector3f(px, py, pz);
                lights.Add(new PointLight(Translate(p), new MediumInterface(), one, 1.0f));
                lightToIndex[lights[lights.Count - 1]] = i;
            }
            BVHLightSampler distrib = new BVHLightSampler(lights.ToArray(), new Allocator());

            for (int i = 0; i < 10; ++i)
            {
                // Don't get too close to the light bbox
                Func<Float> r = () => rng.Uniform<Float>() < .5 ? Lerp(rng.Uniform<Float>(), -15, -7)
                                                                : Lerp(rng.Uniform<Float>(), 7, 16);
                Float rx = r(), ry = r(), rz = r();
                Point3f p = new Point3f(rx, ry, rz);

                Float[] sumWt = new Float[lights.Count];
                const int nSamples = 10000;
                foreach (Float u in new Stratified1D(nSamples))
                {
                    Interaction intr = new Interaction(p, 0.0f, (Medium)null);
                    LightSampleContext ctx = new LightSampleContext(intr);
                    pstd.optional<SampledLight> sampledLight = distrib.Sample(ctx, u);
                    // Can assume this because it's all point lights
                    ASSERT_TRUE(sampledLight.has_value());

                    EXPECT_GT(sampledLight.value().p, 0);
                    sumWt[lightToIndex[sampledLight.value().light]] += 1 / (sampledLight.value().p * nSamples);

                    EXPECT_FLOAT_EQ(sampledLight.value().p, distrib.PMF(ctx, sampledLight.value().light));
                }

                for (int j = 0; j < lights.Count; ++j)
                {
                    EXPECT_GE(sumWt[j], .98);
                    EXPECT_LT(sumWt[j], 1.02);
                }
            }
        }

        // Similar to BVHLightSampling.Point, but vary light power
        [TEST("BVHLightSampling", "PointVaryPower")]
        public static void BVHLightSampling_PointVaryPower()
        {
            RNG rng = new RNG(53251);
            List<Light> lights = new List<Light>();
            List<Float> lightPower = new List<Float>();
            List<ConstantSpectrum> lightSpectra = new List<ConstantSpectrum>();
            Float sumPower = 0;
            Dictionary<Light, int> lightToIndex = new Dictionary<Light, int>();
            for (int i = 0; i < 82; ++i)
            {
                // Random point in [-5, 5]
                Float px = Lerp(rng.Uniform<Float>(), -5, 5);
                Float py = Lerp(rng.Uniform<Float>(), -5, 5);
                Float pz = Lerp(rng.Uniform<Float>(), -5, 5);
                Vector3f p = new Vector3f(px, py, pz);
                lightPower.Add(rng.Uniform<Float>());
                lightSpectra.Add(new ConstantSpectrum(lightPower[lightPower.Count - 1]));
                sumPower += lightPower[lightPower.Count - 1];
                lights.Add(new PointLight(Translate(p), new MediumInterface(),
                                          lightSpectra[lightSpectra.Count - 1], 1.0f));
                lightToIndex[lights[lights.Count - 1]] = i;
            }
            BVHLightSampler distrib = new BVHLightSampler(lights.ToArray(), new Allocator());

            for (int i = 0; i < 10; ++i)
            {
                // Don't get too close to the light bbox
                Func<Float> r = () => rng.Uniform<Float>() < .5 ? Lerp(rng.Uniform<Float>(), -15, -7)
                                                                : Lerp(rng.Uniform<Float>(), 7, 16);
                Float rx = r(), ry = r(), rz = r();
                Point3f p = new Point3f(rx, ry, rz);

                Float[] sumWt = new Float[lights.Count];
                const int nSamples = 100000;
                foreach (Float u in new Stratified1D(nSamples))
                {
                    Interaction intrI = new Interaction(new Point3fi(p), new Normal3f(0, 0, 0), new Point2f(0, 0));
                    LightSampleContext intr = new LightSampleContext(intrI);
                    pstd.optional<SampledLight> sampledLight = distrib.Sample(intr, u);
                    // Again because it's all point lights...
                    ASSERT_TRUE(sampledLight.has_value());

                    Light light = sampledLight.value().light;
                    Float pdf = sampledLight.value().p;
                    EXPECT_GT(pdf, 0);
                    sumWt[lightToIndex[light]] += 1 / (pdf * nSamples);

                    EXPECT_LT(std.abs(distrib.PMF(intr, light) - pdf) / pdf, 1e-4);
                }

                for (int j = 0; j < lights.Count; ++j)
                {
                    EXPECT_GE(sumWt[j], .95);
                    EXPECT_LT(sumWt[j], 1.05);
                }
            }

            // Now, for very far away points (so d^2 is about the same for all
            // lights), make sure that sampling frequencies for each light are
            // basically proportional to their power
            for (int i = 0; i < 10; ++i)
            {
                // Don't get too close to the light bbox
                Func<Float> r = () => rng.Uniform<Float>() < .5 ? Lerp(rng.Uniform<Float>(), -15, -7)
                                                                : Lerp(rng.Uniform<Float>(), 7, 16);
                // gcc evaluates the arguments of the multiplications right to left in the
                // braced-init-list? No: braced-init-lists are sequenced left to right.
                Float rx = r(), ry = r(), rz = r();
                Point3f p = new Point3f(10000 * rx, 10000 * ry, 10000 * rz);
                Interaction intrI = new Interaction(new Point3fi(p), new Normal3f(0, 0, 0), new Point2f(0, 0));
                LightSampleContext intr = new LightSampleContext(intrI);

                int[] counts = new int[lights.Count];
                const int nSamples = 100000;
                foreach (Float u in new Stratified1D(nSamples))
                {
                    pstd.optional<SampledLight> sampledLight = distrib.Sample(intr, u);
                    ASSERT_TRUE(sampledLight.has_value());
                    Light light = sampledLight.value().light;
                    Float pdf = sampledLight.value().p;
                    EXPECT_GT(pdf, 0);
                    ++counts[lightToIndex[light]];

                    EXPECT_FLOAT_EQ(pdf, distrib.PMF(intr, light));
                }

                for (int j = 0; j < lights.Count; ++j)
                {
                    Float expected = nSamples * lightPower[j] / sumPower;
                    EXPECT_GE(counts[j], .97 * expected);
                    EXPECT_LT(counts[j], 1.03 * expected);
                }
            }
        }

        [TEST("BVHLightSampling", "OneTri")]
        public static void BVHLightSampling_OneTri()
        {
            RNG rng = new RNG(5251);
            Transform id = new Transform();
            pstd.vector<int> indices = new pstd.vector<int>(new int[] { 0, 1, 2 });
            // Light is illuminating points with z > 0
            pstd.vector<Point3f> pv = new pstd.vector<Point3f>(new Point3f[] {
                new Point3f(-1, -1, 0), new Point3f(1, -1, 0), new Point3f(0, 1, 0) });
            TriangleMesh mesh = new TriangleMesh(id, false /* rev orientation */, indices, pv,
                                                 new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(),
                                                 new pstd.vector<Point2f>(), new pstd.vector<int>(), new Allocator());
            pstd.vector<Shape> tris = Triangle.CreateTriangles(mesh, new Allocator());

            ASSERT_EQ(1, tris.size());
            List<Light> lights = new List<Light>();
            ConstantSpectrum one = new ConstantSpectrum(1.0f);
            lights.Add(new DiffuseAreaLight(id, new MediumInterface(), one, 1.0f, tris[0],
                                            null, new Image(), null, false /* two sided */));

            BVHLightSampler distrib = new BVHLightSampler(lights.ToArray(), new Allocator());

            for (int i = 0; i < 10; ++i)
            {
                // Random point in [-5, 5]
                Float px = Lerp(rng.Uniform<Float>(), -5, 5);
                Float py = Lerp(rng.Uniform<Float>(), -5, 5);
                Float pz = Lerp(rng.Uniform<Float>(), -5, 5);
                Point3f p = new Point3f(px, py, pz);

                Interaction inter = new Interaction(p, 0.0f, (Medium)null);
                LightSampleContext ctx = new LightSampleContext(inter);
                Float ux = rng.Uniform<Float>();
                Float uy = rng.Uniform<Float>();
                Point2f u = new Point2f(ux, uy);
                SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
                pstd.optional<LightLiSample> ls = lights[0].SampleLi(ctx, u, lambda);

                pstd.optional<SampledLight> sampledLight = distrib.Sample(ctx, rng.Uniform<Float>());

                // Note that the converse (sampledLight ->
                // ls) isn't always true since the light importance
                // metric is conservative.
                if (!sampledLight.has_value())
                {
                    EXPECT_FALSE(ls.has_value());
                }
            }
        }

        private static void randomLights(int n, Allocator alloc, out List<Light> lights, out List<Shape> allTris)
        {
            lights = new List<Light>();
            allTris = new List<Shape>();
            RNG rng = new RNG(6502);
            RNG rngLocal = rng;
            Func<Float> r = () => rngLocal.Uniform<Float>();

            Transform id = new Transform();
            for (int i = 0; i < n; ++i)
            {
                // Triangle
                {
                    pstd.vector<int> indices = new pstd.vector<int>(new int[] { 0, 1, 2 });
                    // Braced-init-list: left-to-right evaluation.
                    Point3f[] parr = new Point3f[3];
                    for (int k = 0; k < 3; ++k)
                    {
                        Float x = r(), y = r(), z = r();
                        parr[k] = new Point3f(x, y, z);
                    }
                    pstd.vector<Point3f> p = new pstd.vector<Point3f>(parr);
                    // leaks...
                    TriangleMesh mesh = new TriangleMesh(id, false /* rev orientation */, indices, p,
                                                         new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(),
                                                         new pstd.vector<Point2f>(), new pstd.vector<int>(),
                                                         new Allocator());
                    pstd.vector<Shape> tris = Triangle.CreateTriangles(mesh, new Allocator());
                    Globals.CHECK_EQ(1, (int)tris.size());
                    Transform sid = new Transform();
                    lights.Add(new DiffuseAreaLight(sid, new MediumInterface(), new ConstantSpectrum(r()), 1.0f,
                                                    tris[0], null, new Image(), null, false /* two sided */));
                    allTris.Add(tris[0]);
                }

                // Random point light
                {
                    Float x = Lerp(rngLocal.Uniform<Float>(), -5, 5);
                    Float y = Lerp(rngLocal.Uniform<Float>(), -5, 5);
                    Float z = Lerp(rngLocal.Uniform<Float>(), -5, 5);
                    Vector3f p = new Vector3f(x, y, z);
                    lights.Add(new PointLight(Translate(p), new MediumInterface(), new ConstantSpectrum(r()), 1.0f));
                }
            }
        }

        [TEST("BVHLightSampling", "PdfMethod")]
        public static void BVHLightSampling_PdfMethod()
        {
            RNG rng = new RNG(5251);

            List<Light> lights;
            List<Shape> tris;
            randomLights(20, new Allocator(), out lights, out tris);

            BVHLightSampler distrib = new BVHLightSampler(lights.ToArray(), new Allocator());
            for (int i = 0; i < 100; ++i)
            {
                Float rx = rng.Uniform<Float>(), ry = rng.Uniform<Float>(), rz = rng.Uniform<Float>();
                Point3f p = new Point3f(-1 + 3 * rx, -1 + 3 * ry, -1 + 3 * rz);
                Float u = rng.Uniform<Float>();
                Interaction intrI = new Interaction(new Point3fi(p), new Normal3f(0, 0, 0), new Point2f(0, 0));
                LightSampleContext intr = new LightSampleContext(intrI);
                pstd.optional<SampledLight> sampledLight = distrib.Sample(intr, u);
                if (sampledLight.has_value())
                    // It's actually legit to sometimes get no lights; as the bounds
                    // tighten up as we get deeper in the tree, it may turn out that
                    // the path we followed didn't have any lights after all.
                    EXPECT_FLOAT_EQ(sampledLight.value().p, distrib.PMF(intr, sampledLight.value().light));
            }
        }

        private delegate LightSampler MakeSampler(Light[] lights);

        private static void PdfMethodTest(MakeSampler make)
        {
            RNG rng = new RNG(5251);

            List<Light> lights;
            List<Shape> tris;
            randomLights(20, new Allocator(), out lights, out tris);

            LightSampler distrib = make(lights.ToArray());
            for (int i = 0; i < 100; ++i)
            {
                Float rx = rng.Uniform<Float>(), ry = rng.Uniform<Float>(), rz = rng.Uniform<Float>();
                Point3f p = new Point3f(-1 + 3 * rx, -1 + 3 * ry, -1 + 3 * rz);
                Interaction intrI = new Interaction(new Point3fi(p), new Normal3f(0, 0, 0), new Point2f(0, 0));
                LightSampleContext intr = new LightSampleContext(intrI);
                pstd.optional<SampledLight> sampledLight = distrib.Sample(intr, rng.Uniform<Float>());
                ASSERT_TRUE(sampledLight.has_value(), i + " - " + p);
                EXPECT_FLOAT_EQ(sampledLight.value().p, distrib.PMF(intr, sampledLight.value().light));
            }
        }

        [TEST("ExhaustiveLightSampling", "PdfMethod")]
        public static void ExhaustiveLightSampling_PdfMethod()
        {
            PdfMethodTest((Light[] l) => new ExhaustiveLightSampler(l, new Allocator()));
        }

        [TEST("UniformLightSampling", "PdfMethod")]
        public static void UniformLightSampling_PdfMethod()
        {
            PdfMethodTest((Light[] l) => new UniformLightSampler(l, new Allocator()));
        }

        [TEST("PowerLightSampling", "PdfMethod")]
        public static void PowerLightSampling_PdfMethod()
        {
            PdfMethodTest((Light[] l) => new PowerLightSampler(l, new Allocator()));
        }
    }
}
