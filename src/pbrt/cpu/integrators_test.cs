// cpu/integrators_test.cpp
//
// Differences from the C++ test: the gtest value-parameterized test (TEST_P with
// INSTANTIATE_TEST_CASE_P over GetIntegrators()) is expressed with the harness's
// [TEST_P] attribute over an integer index; the (scene, integrator family, sampler)
// combination for an index is built on demand when that test runs instead of all
// integrators being created up front. Index order is the same as in C++: for each scene,
// Path/Perspective x 9 samplers, Path/Ortho x 9, VolPath/Perspective x 9, VolPath/Ortho x 9,
// SimplePath x 9, BDPT x 9, then MLT. The "#if 0" Uber material scenes are not ported.

using System;
using System.Collections.Generic;
using System.IO;
using testing;

namespace pbrt
{
    public static unsafe class IntegratorsTests
    {
        private static string inTestDir(string path)
        {
            return path;
        }

        private class TestScene
        {
            public Primitive aggregate;
            public List<Light> lights;
            public string description;
            public float expected;
        }

        private static void CheckSceneAverage(string filename, float expected)
        {
            ImageAndMetadata im = Image.Read(filename);
            ASSERT_EQ(im.image.NChannels(), 3);

            float delta = .025f;
            float sum = 0;

            Image image = im.image;
            for (int t = 0; t < image.Resolution()[1]; ++t)
                for (int s = 0; s < image.Resolution()[0]; ++s)
                    for (int c = 0; c < 3; ++c)
                        sum += image.GetChannel(new Point2i(s, t), c);
            int nPixels = image.Resolution().x * image.Resolution().y * 3;
            EXPECT_NEAR(expected, sum / nPixels, delta);
        }

        private static readonly Transform identity = new Transform();

        private static TestScene GetScene(int index)
        {
            Allocator alloc = new Allocator();
            switch (index)
            {
                case 0:
                    {
                        // Unit sphere, Kd = 0.5, point light I = 3.1415 at center
                        // -> With GI, should have radiance of 1.
                        Shape sphere = new Sphere(identity, identity, true /* reverse orientation */, 1, -1, 1, 360);

                        ConstantSpectrum cs = new ConstantSpectrum(0.5f);
                        SpectrumTexture Kd = new SpectrumConstantTexture(cs);
                        // FIXME: here and below, Materials leak...
                        Material material = new DiffuseMaterial(Kd, null, null);

                        MediumInterface mediumInterface = new MediumInterface();
                        List<Primitive> prims = new List<Primitive>();
                        prims.Add(new GeometricPrimitive(sphere, material, null, mediumInterface));
                        Primitive bvh = new BVHAggregate(prims);

                        // We have to do this little dance here to make sure the spectrum is
                        // properly normalized (this is usually all handled inside *Light::Create())
                        ConstantSpectrum I = new ConstantSpectrum(1);
                        Float scale = Pi / SpectrumToPhotometric(I);
                        List<Light> lights = new List<Light>();
                        lights.Add(new PointLight(identity, new MediumInterface(), I, scale));

                        return new TestScene { aggregate = bvh, lights = lights, description = "Sphere, 1 light, Kd = 0.5", expected = 1.0f };
                    }
                case 1:
                    {
                        // Unit sphere, Kd = 0.5, 4 point lights I = 3.1415/4 at center
                        // -> With GI, should have radiance of 1.
                        Shape sphere = new Sphere(identity, identity, true /* reverse orientation */, 1, -1, 1, 360);

                        ConstantSpectrum cs = new ConstantSpectrum(0.5f);
                        SpectrumTexture Kd = new SpectrumConstantTexture(cs);
                        Material material = new DiffuseMaterial(Kd, null, null);

                        MediumInterface mediumInterface = new MediumInterface();
                        List<Primitive> prims = new List<Primitive>();
                        prims.Add(new GeometricPrimitive(sphere, material, null, mediumInterface));
                        Primitive bvh = new BVHAggregate(prims);

                        // We have to do this little dance here to make sure the spectrum is
                        // properly normalized (this is usually all handled inside *Light::Create())
                        ConstantSpectrum I = new ConstantSpectrum(1);
                        Float scale = Pi / (4 * SpectrumToPhotometric(I));
                        List<Light> lights = new List<Light>();
                        lights.Add(new PointLight(identity, new MediumInterface(), I, scale));
                        lights.Add(new PointLight(identity, new MediumInterface(), I, scale));
                        lights.Add(new PointLight(identity, new MediumInterface(), I, scale));
                        lights.Add(new PointLight(identity, new MediumInterface(), I, scale));

                        return new TestScene { aggregate = bvh, lights = lights, description = "Sphere, 1 light, Kd = 0.5", expected = 1.0f };
                    }
                default:
                    {
                        // Unit sphere, Kd = 0.5, Le = 0.5
                        // -> With GI, should have radiance of 1.
                        Shape sphere = new Sphere(identity, identity, true /* reverse orientation */, 1, -1, 1, 360);

                        ConstantSpectrum cs = new ConstantSpectrum(0.5f);
                        SpectrumTexture Kd = new SpectrumConstantTexture(cs);
                        Material material = new DiffuseMaterial(Kd, null, null);

                        // We have to do this little dance here to make sure the spectrum is
                        // properly normalized (this is usually all handled inside *Light::Create())
                        ConstantSpectrum Le = new ConstantSpectrum(1);
                        Float scale = 0.5f / SpectrumToPhotometric(Le);
                        Light areaLight = new DiffuseAreaLight(identity, new MediumInterface(), Le, scale,
                                                               sphere, null, new Image(), null, false);

                        List<Light> lights = new List<Light>();
                        lights.Add(areaLight);

                        MediumInterface mediumInterface = new MediumInterface();
                        List<Primitive> prims = new List<Primitive>();
                        prims.Add(new GeometricPrimitive(sphere, material, lights[lights.Count - 1], mediumInterface));
                        Primitive bvh = new BVHAggregate(prims);

                        return new TestScene { aggregate = bvh, lights = lights, description = "Sphere, Kd = 0.5, Le = 0.5", expected = 1.0f };
                    }
            }
        }

        private static readonly string[] samplerNames = {
            "Halton", "Padded Sobolspp", "Z Sobol", "Sobol Not Randomized", "Sobol XOR Scramble",
            "Sobol Owen Scramble", "Independent", "Stratified", "PMJ02bn" };

        private static Sampler GetSampler(int index, Point2i resolution)
        {
            int sqrtSpp = 16;
            if (NSpectrumSamples < 4)
                sqrtSpp *= 2;
            int spp = sqrtSpp * sqrtSpp;

            switch (index)
            {
                case 0: return new HaltonSampler(spp, resolution);
                case 1: return new PaddedSobolSampler(spp, RandomizeStrategy.PermuteDigits);
                case 2: return new ZSobolSampler(spp, new Point2i(16, 16), RandomizeStrategy.PermuteDigits);
                case 3: return new SobolSampler(spp, resolution, RandomizeStrategy.None);
                case 4: return new SobolSampler(spp, resolution, RandomizeStrategy.PermuteDigits);
                case 5: return new SobolSampler(spp, resolution, RandomizeStrategy.Owen);
                case 6: return new IndependentSampler(spp);
                case 7: return new StratifiedSampler(sqrtSpp, sqrtSpp, true);
                default: return new PMJ02BNSampler(spp);
            }
        }

        private static Camera MakeCamera(bool perspective, Point2i resolution, out Film film)
        {
            Transform id = new Transform();
            AnimatedTransform identityA = new AnimatedTransform(id, 0, id, 1);
            Filter filter = new BoxFilter(new Vector2f(0.5f, 0.5f));
            FilmBaseParameters fp = new FilmBaseParameters(resolution, new Bounds2i(new Point2i(0, 0), resolution),
                                                           filter, 1.0f, PixelSensor.CreateDefault(),
                                                           inTestDir("test.exr"));
            RGBFilm rgbFilm = new RGBFilm(fp, RGBColorSpace.sRGB);
            CameraBaseParameters cbp = new CameraBaseParameters(new CameraTransform(identityA), rgbFilm, null,
                                                                new ParameterDictionary(), new FileLoc());
            Camera camera;
            if (perspective)
                camera = new PerspectiveCamera(cbp, 45, new Bounds2f(new Point2f(-1, -1), new Point2f(1, 1)), 0.0f, 10.0f);
            else
                camera = new OrthographicCamera(cbp, new Bounds2f(new Point2f(-.1f, -.1f), new Point2f(.1f, .1f)),
                                                0.0f, 10.0f);
            film = camera.GetFilm();
            return camera;
        }

        private const int perScene = 6 * 9 + 1;

        private static string[] familyNames = {
            "Path, depth 8, Perspective", "Path, depth 8, Ortho", "VolPath, depth 8, Perspective",
            "VolPath, depth 8, Ortho", "SimplePath, depth 8, Perspective", "BDPT, depth 8, Perspective" };

        private static void RenderTest_RadianceMatches(int index)
        {
            int sceneIndex = index / perScene;
            int k = index % perScene;
            TestScene scene = GetScene(sceneIndex);

            Point2i resolution = new Point2i(10, 10);
            Integrator integrator;
            string description;
            Film film;
            if (k == 6 * 9)
            {
                // MLT
                Camera camera = MakeCamera(true, resolution, out film);
                integrator = new MLTIntegrator(camera, scene.aggregate, scene.lights, 8 /* depth */,
                                               100000 /* n bootstrap */, 1000 /* nchains */,
                                               1024 /* mutations per pixel */, 0.01f /* sigma */,
                                               0.3f /* large step prob */, false /* regularize */);
                description = "MLT, depth 8, Perspective, " + scene.description;
            }
            else
            {
                int family = k / 9;
                int samplerIndex = k % 9;
                Sampler sampler = GetSampler(samplerIndex, resolution);
                bool perspective = family == 0 || family == 2 || family == 4 || family == 5;
                Camera camera = MakeCamera(perspective, resolution, out film);
                switch (family)
                {
                    case 0:
                    case 1:
                        integrator = new PathIntegrator(8, camera, sampler, scene.aggregate, scene.lights);
                        break;
                    case 2:
                    case 3:
                        integrator = new VolPathIntegrator(8, camera, sampler, scene.aggregate, scene.lights);
                        break;
                    case 4:
                        // Simple path (perspective only, still sample light and BSDFs). Yolo
                        integrator = new SimplePathIntegrator(8, true, true, camera, sampler, scene.aggregate,
                                                              scene.lights);
                        break;
                    default:
                        integrator = new BDPTIntegrator(camera, sampler, scene.aggregate, scene.lights,
                                                        6, false, false, false);
                        break;
                }
                description = familyNames[family] + ", " + samplerNames[samplerIndex] + ", " + scene.description;
            }

            Console.WriteLine("[  INFO  ] " + description);
            integrator.Render();
            CheckSceneAverage(inTestDir("test.exr"), scene.expected);

            bool removed = true;
            try { File.Delete(inTestDir("test.exr")); } catch (Exception) { removed = false; }
            EXPECT_TRUE(removed && !File.Exists(inTestDir("test.exr")));
        }

        [TEST_P("AnalyticTestScenes/RenderTest", "RadianceMatches",
                0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
                30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54,
                55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80,
                81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105,
                106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126,
                127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147,
                148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164)]
        public static void RenderTest_RadianceMatches_P(int index)
        {
            RenderTest_RadianceMatches(index);
        }
    }
}
