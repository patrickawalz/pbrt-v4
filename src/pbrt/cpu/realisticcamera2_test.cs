// (not in pbrt-v4) realisticcamera2_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class RealisticCamera2Tests
    {
        internal static RealisticCamera2 MakeCamera(Point2i resolution, RealisticCamera2.StrayLightParameters strayLight)
        {
            Transform id = new Transform();
            AnimatedTransform identityA = new AnimatedTransform(id, 0, id, 1);
            Filter filter = new BoxFilter(new Vector2f(0.5f, 0.5f));
            FilmBaseParameters fp = new FilmBaseParameters(resolution, new Bounds2i(new Point2i(0, 0), resolution), filter, 35.0f,
                                                           PixelSensor.CreateDefault(), "realisticcamera2_test.exr");
            RGBFilm rgbFilm = new RGBFilm(fp, RGBColorSpace.sRGB);
            CameraBaseParameters cbp = new CameraBaseParameters(new CameraTransform(identityA), rgbFilm, null,
                                                                new ParameterDictionary(), new FileLoc());
            List<Float> lensParameters = new List<Float>(RealisticCameraPolarisedTests.doubleGauss);
            return new RealisticCamera2(cbp, lensParameters, 5.0f, 17.1f, new Image((Allocator)null), null, null, null, null, null,
                                        null, false, strayLight);
        }

        // The double Gauss lens with a second aperture stop, 1 mm behind its own (in the air: a plane further back would cut
        // into the glass of the next element)
        static List<Float> TwoStopLens()
        {
            List<Float> lens = new List<Float>(RealisticCameraPolarisedTests.doubleGauss);
            lens[4 * 5 + 1] = 1.0f;
            lens.InsertRange(4 * 6, new Float[] { 0.0f, 3.5f, 0.0f, 17.1f });
            return lens;
        }

        static RealisticCamera2 MakeTwoStopCamera(Point2i resolution, List<Float> stopDiameters, List<Image> stopImages)
        {
            Transform id = new Transform();
            AnimatedTransform identityA = new AnimatedTransform(id, 0, id, 1);
            Filter filter = new BoxFilter(new Vector2f(0.5f, 0.5f));
            FilmBaseParameters fp = new FilmBaseParameters(resolution, new Bounds2i(new Point2i(0, 0), resolution), filter, 35.0f,
                                                           PixelSensor.CreateDefault(), "realisticcamera2_test.exr");
            RGBFilm rgbFilm = new RGBFilm(fp, RGBColorSpace.sRGB);
            CameraBaseParameters cbp = new CameraBaseParameters(new CameraTransform(identityA), rgbFilm, null,
                                                                new ParameterDictionary(), new FileLoc());
            return new RealisticCamera2(cbp, TwoStopLens(), 5.0f, 17.1f, new Image((Allocator)null), null, null, null, null, null,
                                        null, false, null, stopDiameters, stopImages);
        }

        // The rays that a camera gives a set of samples: how many, and the sum of their weights
        static int CountRays(RealisticCamera2 camera, Point2i resolution, out double weights)
        {
            RNG rng = new RNG(41);
            int rays = 0;
            weights = 0;
            for (int i = 0; i < 40000; ++i)
            {
                CameraSample sample = default;
                sample.pFilm = new Point2f(rng.Uniform<Float>() * resolution.x, rng.Uniform<Float>() * resolution.y);
                sample.pLens = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                sample.time = 0.5f;
                sample.filterWeight = 1;
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
                pstd.optional<CameraRay> ray = camera.GenerateRay(sample, ref lambda);
                if (ray)
                {
                    ++rays;
                    weights += ray.value().weight[0];
                }
            }

            return rays;
        }

        [TEST("RealisticCamera2", "ApertureStopsOfTheirOwn")]
        public static void RealisticCamera2_ApertureStopsOfTheirOwn()
        {
            Point2i resolution = new Point2i(64, 64);
            // Both stops as wide as the lens file has them: the lens with one stop (the same rays, to the rounding of one more
            // plane on the way)
            RealisticCamera2 oneStop = MakeCamera(resolution, null);
            RealisticCamera2 open = MakeTwoStopCamera(resolution, new List<Float> { 0.0f, 17.1f }, null);
            RNG rng = new RNG(43);
            int both = 0;
            int different = 0;
            for (int i = 0; i < 20000; ++i)
            {
                CameraSample sample = default;
                sample.pFilm = new Point2f(rng.Uniform<Float>() * resolution.x, rng.Uniform<Float>() * resolution.y);
                sample.pLens = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                sample.time = 0.5f;
                sample.filterWeight = 1;
                SampledWavelengths lambdaA = SampledWavelengths.SampleVisible(0.5f);
                SampledWavelengths lambdaB = SampledWavelengths.SampleVisible(0.5f);
                pstd.optional<CameraRay> a = oneStop.GenerateRay(sample, ref lambdaA);
                pstd.optional<CameraRay> b = open.GenerateRay(sample, ref lambdaB);
                if ((bool)a != (bool)b)
                {
                    ++different;
                }
                else if (a)
                {
                    ++both;
                    EXPECT_LT(pbrt.Distance(a.value().ray.o, b.value().ray.o), (Float)1e-5);
                    EXPECT_GT(pbrt.Dot(a.value().ray.d, b.value().ray.d), (Float)(1 - 1e-6));
                }
            }

            EXPECT_GT(both, 1000);
            EXPECT_LT(different, 0.01 * both);

            // Both stops with the one diameter of RealisticCamera's parameter: the second is as wide as the first (in
            // RealisticCamera it is a thousandth of it), and the light is that of the lens with one stop
            RealisticCamera2 same = MakeTwoStopCamera(resolution, null, null);
            CountRays(oneStop, resolution, out double weightOne);
            CountRays(open, resolution, out double weightOpen);
            CountRays(same, resolution, out double weightSame);
            EXPECT_LT(Math.Abs(weightOpen / weightOne - 1), 0.03);
            EXPECT_LT(Math.Abs(weightSame / weightOne - 1), 0.03);

            // Each stop closes by itself: less light with either one smaller, least with both
            RealisticCamera2 first = MakeTwoStopCamera(resolution, new List<Float> { 8.0f, 17.1f }, null);
            RealisticCamera2 second = MakeTwoStopCamera(resolution, new List<Float> { 0.0f, 8.0f }, null);
            RealisticCamera2 small = MakeTwoStopCamera(resolution, new List<Float> { 8.0f, 8.0f }, null);
            CountRays(first, resolution, out double weightFirst);
            CountRays(second, resolution, out double weightSecond);
            CountRays(small, resolution, out double weightSmall);
            Console.WriteLine(pbrt.StringPrintf("  light through: both open %g, same diameter %g, first at 8 mm %g, second at 8 mm %g, both %g",
                                                weightOpen, weightSame, weightFirst, weightSecond, weightSmall));
            // (a stop of 8 mm in place of 17.1 mm: about (8 / 17.1)^2 = 0.22 of the light on the axis, somewhat more over the
            // film, where the other elements vignette the wide stop)
            EXPECT_LT(weightFirst, 0.4 * weightOpen);
            EXPECT_GT(weightFirst, 0.15 * weightOpen);
            EXPECT_LT(weightSecond, 0.4 * weightOpen);
            EXPECT_GT(weightSecond, 0.15 * weightOpen);
            EXPECT_LT(weightSmall, 1.001 * weightFirst);
            EXPECT_LT(weightSmall, 1.001 * weightSecond);
            EXPECT_GT(weightSmall, 0.5 * weightFirst);

            // A shape of its own for each stop: a square on the second one only takes light away from the round one
            Image square = new Image(PixelFormat.Float, new Point2i(64, 64), new string[] { "Y" }, null, null);
            for (int y = 16; y < 48; ++y)
            {
                for (int x = 16; x < 48; ++x)
                {
                    square.SetChannel(new Point2i(x, y), 0, 1.0f);
                }
            }

            RealisticCamera2 shaped = MakeTwoStopCamera(resolution, new List<Float> { 0.0f, 8.0f }, new List<Image> { null, square });
            CountRays(shaped, resolution, out double weightShaped);
            Console.WriteLine(pbrt.StringPrintf("  second stop at 8 mm with a square of half its width: %g", weightShaped));
            EXPECT_LT(weightShaped, 0.6 * weightSecond);
            EXPECT_GT(weightShaped, 0.1 * weightSecond);
        }

        static List<RealisticCamera2.CoatingLayer[]> AllInterfaces(RealisticCamera2.CoatingLayer[] layers)
        {
            List<RealisticCamera2.CoatingLayer[]> list = new List<RealisticCamera2.CoatingLayer[]>();
            for (int i = 0; i < RealisticCameraPolarisedTests.doubleGauss.Length / 4; ++i)
            {
                list.Add(layers);
            }

            return list;
        }

        // The built-in coatings: a quarter wave of magnesium fluoride, and that with a half wave of index 2.10 (the dispersion
        // of zirconia) and a quarter wave of index 1.63 (that of alumina) under it
        static RealisticCamera2.CoatingLayer[] SingleCoating()
        {
            double[] magnesiumFluoride = RealisticCamera2.CoatingMaterial("MgF2");
            Float eta = (Float)RealisticCamera2.SellmeierIndex(magnesiumFluoride, 0.55);
            return new RealisticCamera2.CoatingLayer[]
            {
                new RealisticCamera2.CoatingLayer { eta = 0, thickness = 550 / (4 * eta), sellmeier = magnesiumFluoride }
            };
        }

        static RealisticCamera2.CoatingLayer[] BroadbandCoating()
        {
            return new RealisticCamera2.CoatingLayer[]
            {
                SingleCoating()[0],
                new RealisticCamera2.CoatingLayer { eta = 2.10f, thickness = 550 / (2 * 2.10f), sellmeier = RealisticCamera2.CoatingMaterial("ZrO2") },
                new RealisticCamera2.CoatingLayer { eta = 1.63f, thickness = 550 / (4 * 1.63f), sellmeier = RealisticCamera2.CoatingMaterial("Al2O3") }
            };
        }

        [TEST("RealisticCamera2", "SameAsRealisticCamera")]
        public static void RealisticCamera2_SameAsRealisticCamera()
        {
            // Without any of its parameters the camera is RealisticCamera
            Point2i resolution = new Point2i(64, 64);
            RealisticCamera reference = RealisticCameraPolarisedTests.MakeCamera(resolution);
            RealisticCamera2 camera = MakeCamera(resolution, null);
            RNG rng = new RNG(7);
            int rays = 0;
            for (int i = 0; i < 20000; ++i)
            {
                CameraSample sample = default;
                sample.pFilm = new Point2f(rng.Uniform<Float>() * resolution.x, rng.Uniform<Float>() * resolution.y);
                sample.pLens = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                sample.time = rng.Uniform<Float>();
                sample.filterWeight = 1;
                SampledWavelengths lambdaA = SampledWavelengths.SampleVisible(0.5f);
                SampledWavelengths lambdaB = SampledWavelengths.SampleVisible(0.5f);
                pstd.optional<CameraRay> a = reference.GenerateRay(sample, ref lambdaA);
                pstd.optional<CameraRay> b = camera.GenerateRay(sample, ref lambdaB);
                EXPECT_EQ((bool)a, (bool)b);
                if (a && b)
                {
                    ++rays;
                    EXPECT_EQ(a.value().ray.o, b.value().ray.o);
                    EXPECT_EQ(a.value().ray.d, b.value().ray.d);
                    EXPECT_EQ(a.value().weight[0], b.value().weight[0]);
                }
            }

            EXPECT_GT(rays, 1000);
        }

        [TEST("RealisticCamera2", "CoatingMaterials")]
        public static void RealisticCamera2_CoatingMaterials()
        {
            // The indices at the d line of the materials of the coatings, and that the index of a layer falls with the wavelength
            EXPECT_LT(Math.Abs(RealisticCamera2.SellmeierIndex(RealisticCamera2.CoatingMaterial("MgF2"), 0.5876) - 1.3777), 5e-4);
            EXPECT_LT(Math.Abs(RealisticCamera2.SellmeierIndex(RealisticCamera2.CoatingMaterial("Al2O3"), 0.5876) - 1.7682), 5e-4);
            EXPECT_LT(Math.Abs(RealisticCamera2.SellmeierIndex(RealisticCamera2.CoatingMaterial("ZrO2"), 0.5876) - 2.1585), 2e-3);
            EXPECT_LT(Math.Abs(RealisticCamera2.SellmeierIndex(RealisticCamera2.CoatingMaterial("SiO2"), 0.5876) - 1.4585), 5e-4);
            RealisticCamera2.CoatingLayer zirconia = BroadbandCoating()[1];
            EXPECT_LT(Math.Abs(RealisticCamera2.CoatingLayerEta(zirconia, 550) - 2.10f), 1e-5);
            EXPECT_GT(RealisticCamera2.CoatingLayerEta(zirconia, 420), RealisticCamera2.CoatingLayerEta(zirconia, 550) + 0.03f);
            EXPECT_LT(RealisticCamera2.CoatingLayerEta(zirconia, 680), RealisticCamera2.CoatingLayerEta(zirconia, 550) - 0.01f);
        }

        [TEST("RealisticCamera2", "AxialTransmittance")]
        public static void RealisticCamera2_AxialTransmittance()
        {
            // The ray along the axis meets every interface at normal incidence: the transmittance is the product of 1 - R, with
            // the index of each layer at each wavelength
            Float[] lens = RealisticCameraPolarisedTests.doubleGauss;
            RealisticCamera2.CoatingLayer[][] coatings = { null, SingleCoating(), BroadbandCoating() };
            Float[] transmittances = new Float[3];
            for (int c = 0; c < 3; ++c)
            {
                RealisticCamera2.StrayLightParameters strayLight = new RealisticCamera2.StrayLightParameters();
                strayLight.fresnel = true;
                if (coatings[c] != null)
                {
                    strayLight.coatings = AllInterfaces(coatings[c]);
                }

                RealisticCamera2 camera = MakeCamera(new Point2i(32, 32), strayLight);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
                SampledSpectrum transmittance = camera.MainTransmittance(new Ray(new Point3f(0, 0, 0), new Vector3f(0, 0, 1)), 0,
                                                                         lambda, default, false);
                for (int w = 0; w < pbrt.NSpectrumSamples; ++w)
                {
                    double expected = 1;
                    Float previous = 1;
                    for (int i = 0; i < lens.Length / 4; ++i)
                    {
                        Float eta = lens[4 * i + 2] == 0 ? 1 : lens[4 * i + 2];
                        if (lens[4 * i] != 0 && eta != previous)
                        {
                            double reflectance;
                            if (coatings[c] != null && (eta == 1 || previous == 1))
                            {
                                Float[] layerEta = new Float[coatings[c].Length];
                                Float[] layerThickness = new Float[coatings[c].Length];
                                for (int layer = 0; layer < layerEta.Length; ++layer)
                                {
                                    layerEta[layer] = RealisticCamera2.CoatingLayerEta(coatings[c][layer], lambda[w]);
                                    layerThickness[layer] = coatings[c][layer].thickness;
                                }

                                // From the air side: the reflectance of a stack that does not absorb is the same from both
                                pbrt.FrThinFilmPolarised(1, 1, layerEta, layerThickness, lambda[w],
                                                         new pstd.complex(std.max(eta, previous)), out Float Fs, out Float Fp,
                                                         out Float Ds, out Float Dp);
                                reflectance = Fs;
                            }
                            else
                            {
                                reflectance = Math.Pow((eta - previous) / (eta + previous), 2);
                            }

                            expected *= 1 - reflectance;
                        }

                        previous = eta;
                    }

                    EXPECT_LT(std.abs(transmittance[w] - (Float)expected), (Float)2e-4);
                }

                transmittances[c] = transmittance.Average();
                Console.WriteLine(pbrt.StringPrintf("  axial transmittance, coating %d: %f", c, transmittances[c]));
            }

            // Both coatings let much more through (the glasses of this lens have indices near 1.67, where the single layer of
            // index 1.38 is close to ideal)
            EXPECT_GT(transmittances[1], transmittances[0] + 0.25f);
            EXPECT_GT(transmittances[2], transmittances[0] + 0.25f);
        }

        [TEST("RealisticCamera2", "WeightIsThatOfTheMuellerMatrix")]
        public static void RealisticCamera2_WeightIsThatOfTheMuellerMatrix()
        {
            // The weight of a path (the Stokes vector that the walk carries) is the first element of the Mueller matrix of the
            // path, as the polarised light system multiplies it out: ghosts of a coated lens, from the film and from the scene
            RealisticCamera2.StrayLightParameters strayLight = new RealisticCamera2.StrayLightParameters();
            strayLight.ghosts = true;
            strayLight.sensorReflectance = 0.05f;
            strayLight.coatings = AllInterfaces(BroadbandCoating());
            RealisticCamera2 camera = MakeCamera(new Point2i(32, 32), strayLight);
            Float half = 0.012f;
            Float radius = 1.5f * 0.01f;
            RNG rng = new RNG(31);
            int* plan = stackalloc int[RealisticCamera2.MaximumGhostReflections];
            int[] compared = new int[2];
            double[] worst = new double[2];
            double[] polarising = new double[2];
            for (int i = 0; i < 400000; ++i)
            {
                bool fromScene = (i & 1) == 1;
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                Ray ray;
                if (fromScene)
                {
                    camera.SampleStrayLens(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), 0, ref lambda, out Interaction lens,
                                           out Float pdfFront);
                    Vector3f d = pbrt.SampleUniformCone(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), 0.9f);
                    ray = new Ray(new Point3f(lens.p().x, lens.p().y, -lens.p().z) - 1e-3f * d, d);
                }
                else
                {
                    Point3f pFilm = new Point3f((2 * rng.Uniform<Float>() - 1) * half, (2 * rng.Uniform<Float>() - 1) * half, 0);
                    Point2f pDisc = radius * pbrt.SampleUniformDiskConcentric(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    ray = new Ray(pFilm, new Point3f(pDisc.x, pDisc.y, -camera.LensRearZ()) - pFilm);
                }

                Float u = rng.Uniform<Float>();
                int count = camera.SampleGhostPlan(fromScene, ref u, ref rng, plan, out Float probability);
                SampledSpectrum weight = camera.Walk(ray, fromScene, plan, count, false, true, ref rng, 0, lambda, true,
                                                     out RealisticCamera2.StrayPath path);
                if (!weight || path.reflections == 0)
                {
                    continue;
                }

                SampledSpectrum intensity = path.optics.GetIntensity();
                MuellerMatrix matrix = path.optics.GetMuellerMatrix();
                for (int w = 0; w < pbrt.NSpectrumSamples; ++w)
                {
                    worst[fromScene ? 1 : 0] = Math.Max(worst[fromScene ? 1 : 0], Math.Abs(intensity[w] / weight[w] - 1));
                    polarising[fromScene ? 1 : 0] = Math.Max(polarising[fromScene ? 1 : 0], Math.Abs(matrix.m01[w] / weight[w]));
                }

                ++compared[fromScene ? 1 : 0];
            }

            Console.WriteLine(pbrt.StringPrintf("  from the film: %d ghosts, largest difference %g; from the scene: %d ghosts, largest difference %g",
                                                compared[0], worst[0], compared[1], worst[1]));
            Console.WriteLine(pbrt.StringPrintf("  largest m01 / m00 of a path: %g, %g", polarising[0], polarising[1]));
            EXPECT_GT(compared[0], 5000);
            EXPECT_GT(compared[1], 5000);
            EXPECT_LT(worst[0], 2e-3);
            EXPECT_LT(worst[1], 2e-3);
        }

        // The light of the ghosts over the film by the chosen reflections (planned) and by reflecting with the probability
        // of the reflectance at each interface (natural): all of it, and the part that leaves within 18 degrees of the axis
        static void GhostLight(RealisticCamera2 camera, bool natural, int samples, ulong seed, out double all, out double near)
        {
            all = 0;
            near = 0;
            RNG rng = new RNG(seed);
            Float half = 0.012f;
            Float radius = 1.5f * 0.01f;
            int* plan = stackalloc int[RealisticCamera2.MaximumGhostReflections];
            for (int i = 0; i < samples; ++i)
            {
                Point3f pFilm = new Point3f((2 * rng.Uniform<Float>() - 1) * half, (2 * rng.Uniform<Float>() - 1) * half, 0);
                Point2f pDisc = radius * pbrt.SampleUniformDiskConcentric(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f d = new Point3f(pDisc.x, pDisc.y, -camera.LensRearZ()) - pFilm;
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                Float cos4 = pbrt.Pow4(pbrt.Normalize(d).z);
                SampledSpectrum weight;
                Ray rOut;
                if (natural)
                {
                    weight = camera.Walk(new Ray(pFilm, d), false, null, 0, true, true, ref rng, 0, lambda, false,
                                         out RealisticCamera2.StrayPath path);
                    rOut = path.ray;
                    if (path.reflections == 0)
                    {
                        continue;
                    }
                }
                else
                {
                    Float u = rng.Uniform<Float>();
                    int count = camera.SampleGhostPlan(false, ref u, ref rng, plan, out Float probability);
                    weight = camera.Walk(new Ray(pFilm, d), false, plan, count, false, true, ref rng, 0, lambda, false,
                                         out RealisticCamera2.StrayPath path) / probability;
                    rOut = path.ray;
                    if (path.reflections == 0)
                    {
                        continue;
                    }
                }

                if (!weight)
                {
                    continue;
                }

                double value = weight.Average() * cos4;
                all += value;
                if (rOut.d.z < -0.95f)
                {
                    near += value;
                }
            }

            all /= samples;
            near /= samples;
        }

        // The throughput of the ghosts (the integral of their weight over the area and the projected solid angle) from the
        // scene: over the front element and the directions into it
        static double GhostThroughputFromScene(RealisticCamera2 camera, bool natural, int samples, ulong seed)
        {
            double sum = 0;
            RNG rng = new RNG(seed);
            int* plan = stackalloc int[RealisticCamera2.MaximumGhostReflections];
            Float pdfFront = 0;
            for (int i = 0; i < samples; ++i)
            {
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                camera.SampleStrayLens(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), 0, ref lambda, out Interaction lens,
                                       out pdfFront);
                // Cosine weighted about the normal into the lens: the camera space is the render space here, and the lens
                // space has z the other way round
                Vector3f inward = new Vector3f(-lens.n.x, -lens.n.y, lens.n.z);
                Vector3f d = Frame.FromZ(inward).FromLocal(pbrt.SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(),
                                                                                                   rng.Uniform<Float>())));
                Ray ray = new Ray(new Point3f(lens.p().x, lens.p().y, -lens.p().z) - 1e-3f * d, d);
                SampledSpectrum weight;
                int reflections;
                if (natural)
                {
                    weight = camera.Walk(ray, true, null, 0, true, true, ref rng, 0, lambda, false,
                                         out RealisticCamera2.StrayPath path);
                    reflections = path.reflections;
                }
                else
                {
                    Float u = rng.Uniform<Float>();
                    int count = camera.SampleGhostPlan(true, ref u, ref rng, plan, out Float probability);
                    weight = camera.Walk(ray, true, plan, count, false, true, ref rng, 0, lambda, false,
                                         out RealisticCamera2.StrayPath path) / probability;
                    reflections = path.reflections;
                }

                if (reflections > 0 && (bool)weight)
                {
                    sum += weight.Average();
                }
            }

            // The density of the direction is cos / pi
            return sum / samples * Math.PI / pdfFront;
        }

        [TEST("RealisticCamera2", "GhostsSameFromFilmAndScene")]
        public static void RealisticCamera2_GhostsSameFromFilmAndScene()
        {
            RealisticCamera2.StrayLightParameters strayLight = new RealisticCamera2.StrayLightParameters();
            strayLight.ghosts = true;
            strayLight.sensorReflectance = 0.05f;
            RealisticCamera2 camera = MakeCamera(new Point2i(32, 32), strayLight);

            // From the film: over the film and the disc of the rear element that the stray paths are sampled over
            double side = 0.035 / Math.Sqrt(2);
            Float half = (Float)(side / 2);
            Float radius = 1.5f * 0.01f;
            RNG rng = new RNG(5);
            int* plan = stackalloc int[RealisticCamera2.MaximumGhostReflections];
            int samples = 6000000;
            double sum = 0;
            for (int i = 0; i < samples; ++i)
            {
                Point3f pFilm = new Point3f((2 * rng.Uniform<Float>() - 1) * half, (2 * rng.Uniform<Float>() - 1) * half, 0);
                Point2f pDisc = radius * pbrt.SampleUniformDiskConcentric(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f d = new Point3f(pDisc.x, pDisc.y, -camera.LensRearZ()) - pFilm;
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                Float u = rng.Uniform<Float>();
                int count = camera.SampleGhostPlan(false, ref u, ref rng, plan, out Float probability);
                SampledSpectrum weight = camera.Walk(new Ray(pFilm, d), false, plan, count, false, true, ref rng, 0, lambda,
                                                     false, out RealisticCamera2.StrayPath path) / probability;
                if (path.reflections > 0 && (bool)weight)
                {
                    sum += weight.Average() * pbrt.Pow4(pbrt.Normalize(d).z);
                }
            }

            double fromFilm = sum / samples * side * side * Math.PI * radius * radius / pbrt.Sqr(camera.LensRearZ());
            double fromScenePlanned = GhostThroughputFromScene(camera, false, 40000000, 17);
            double fromSceneNatural = GhostThroughputFromScene(camera, true, 40000000, 19);
            Console.WriteLine(pbrt.StringPrintf("  ghost throughput (m^2 sr): from the film %g, from the scene %g (planned), %g (natural)",
                                                fromFilm, fromScenePlanned, fromSceneNatural));
            EXPECT_LT(Math.Abs(fromScenePlanned / fromFilm - 1), 0.03);
            EXPECT_LT(Math.Abs(fromSceneNatural / fromFilm - 1), 0.03);
        }

        [TEST("RealisticCamera2", "GhostReversesToTheSamePath")]
        public static void RealisticCamera2_GhostReversesToTheSamePath()
        {
            // A ghost from the film, traced back from the scene with its reflections counted from that end, is the same path
            RealisticCamera2.StrayLightParameters strayLight = new RealisticCamera2.StrayLightParameters();
            strayLight.ghosts = true;
            strayLight.sensorReflectance = 0.05f;
            RealisticCamera2 camera = MakeCamera(new Point2i(32, 32), strayLight);
            Float half = 0.012f;
            Float radius = 1.5f * 0.01f;
            RNG rng = new RNG(23);
            int* plan = stackalloc int[RealisticCamera2.MaximumGhostReflections];
            int* reversed = stackalloc int[RealisticCamera2.MaximumGhostReflections];
            int ghosts = 0;
            int same = 0;
            for (int i = 0; i < 2000000; ++i)
            {
                Point3f pFilm = new Point3f((2 * rng.Uniform<Float>() - 1) * half, (2 * rng.Uniform<Float>() - 1) * half, 0);
                Point2f pDisc = radius * pbrt.SampleUniformDiskConcentric(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f d = new Point3f(pDisc.x, pDisc.y, -camera.LensRearZ()) - pFilm;
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                Float u = rng.Uniform<Float>();
                int count = camera.SampleGhostPlan(false, ref u, ref rng, plan, out Float probability);
                SampledSpectrum weight = camera.Walk(new Ray(pFilm, d), false, plan, count, false, true, ref rng, 0, lambda,
                                                     false, out RealisticCamera2.StrayPath path);
                if (!weight || path.reflections == 0)
                {
                    continue;
                }

                ++ghosts;
                RealisticCamera2.ReverseGhostPlan(plan, count, path.met, reversed);
                Ray rOut = path.ray;
                SampledSpectrum back = camera.Walk(new Ray(rOut.o + 1e-3f * rOut.d, -rOut.d), true, reversed, count, false, true,
                                                   ref rng, 0, lambda, false, out RealisticCamera2.StrayPath pathBack);
                if ((bool)back && pbrt.Distance(pathBack.ray.o, pFilm) < 1e-5f && std.abs(back[0] - weight[0]) < 1e-3f * weight[0] &&
                    pathBack.met == path.met + 1)
                {
                    ++same;
                }
            }

            Console.WriteLine(pbrt.StringPrintf("  %d ghosts, %d the same from the scene", ghosts, same));
            EXPECT_GT(ghosts, 10000);
            EXPECT_GT(same, 0.999 * ghosts);
        }

        [TEST("RealisticCamera2", "GhostSequencesMatchNaturalWalk")]
        public static void RealisticCamera2_GhostSequencesMatchNaturalWalk()
        {
            RealisticCamera2.StrayLightParameters strayLight = new RealisticCamera2.StrayLightParameters();
            strayLight.ghosts = true;
            strayLight.sensorReflectance = 0.05f;
            RealisticCamera2 camera = MakeCamera(new Point2i(32, 32), strayLight);
            GhostLight(camera, true, 40000000, 11, out double naturalAll, out double naturalNear);
            GhostLight(camera, false, 4000000, 13, out double plannedAll, out double plannedNear);
            Console.WriteLine(pbrt.StringPrintf("  ghost light: natural %g (near axis %g), planned %g (near axis %g)", naturalAll,
                                                naturalNear, plannedAll, plannedNear));
            EXPECT_GT(naturalAll, 0);
            EXPECT_LT(Math.Abs(plannedAll / naturalAll - 1), 0.03);
            EXPECT_LT(Math.Abs(plannedNear / naturalNear - 1), 0.03);
        }
    }
}
