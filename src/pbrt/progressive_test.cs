// (not in pbrt-v4) progressive_test.cs: the tile buffers and the light layers of the film, and a render session that is paused,
// resumed, given fewer and more threads, shown, and stopped.

using System;
using System.IO;
using System.Threading;
using testing;

namespace pbrt
{
    using static pbrt;
    using static testing.Gtest;

    public static unsafe class ProgressiveTests
    {
        private static RGBFilm NewFilm(Point2i resolution)
        {
            FilmBaseParameters p = new FilmBaseParameters(resolution, new Bounds2i(new Point2i(0, 0), resolution), new BoxFilter(), 35,
                                                          PixelSensor.CreateDefault(), "progressive_test.exr");
            return new RGBFilm(p, RGBColorSpace.sRGB, Infinity, false);
        }

        private static SampledSpectrum Radiance(RNG rng)
        {
            SampledSpectrum L = new SampledSpectrum(0f);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                L[i] = 3 * rng.Uniform<Float>();
            }
            return L;
        }

        // The samples of tiles that are handed over and merged are the samples added directly, bit for bit
        [TEST("Progressive", "TilesAreTheFilm")]
        public static void TilesAreTheFilm()
        {
            Point2i resolution = new Point2i(37, 23);
            RGBFilm direct = NewFilm(resolution), tiled = NewFilm(resolution);
            for (int pass = 0; pass < 5; ++pass)
            {
                for (int ty = 0; ty < resolution.y; ty += 8)
                {
                    for (int tx = 0; tx < resolution.x; tx += 8)
                    {
                        Bounds2i tile = Intersect(new Bounds2i(new Point2i(tx, ty), new Point2i(tx + 8, ty + 8)), tiled.PixelBounds());
                        tiled.BeginTile(tile);
                        foreach (Point2i p in tile)
                        {
                            RNG rng = new RNG((ulong)(p.x + 1000 * p.y), (ulong)pass);
                            SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                            SampledSpectrum L = Radiance(rng);
                            Float weight = 0.5f + rng.Uniform<Float>();
                            direct.AddSample(p, L, lambda, null, weight);
                            tiled.AddSample(p, L, lambda, null, weight);
                        }
                        tiled.EndTile();
                    }
                }
                // (a pass is merged before the next: the order of a pixel's samples is that of the passes)
                EXPECT_GT(tiled.MergeTiles(), 0);
            }
            EXPECT_EQ(tiled.MergeTiles(), 0);
            foreach (Point2i p in direct.PixelBounds())
            {
                RGB a = direct.GetPixelRGB(p), b = tiled.GetPixelRGB(p);
                EXPECT_EQ(a.r, b.r);
                EXPECT_EQ(a.g, b.g);
                EXPECT_EQ(a.b, b.b);
            }
        }

        // What a sample has of the light layers goes to them, the default has the rest, and the image is each layer times what
        // its settings make of it
        [TEST("Progressive", "LightLayers")]
        public static void LightLayersOfTheFilm()
        {
            LightLayers.Scene.Reset();
            int warm = LightLayers.Scene.Index("warm"), cool = LightLayers.Scene.Index("cool");
            EXPECT_EQ(warm, 1);
            EXPECT_EQ(cool, 2);
            EXPECT_EQ(LightLayers.Scene.Index("warm"), 1);
            EXPECT_EQ(LightLayers.Scene.Count, 3);

            Point2i resolution = new Point2i(6, 4);
            RGBFilm film = NewFilm(resolution), plain = NewFilm(resolution), warmOnly = NewFilm(resolution), coolOnly = NewFilm(resolution);
            film.SetLayerCount(LightLayers.Scene.Count);
            RNG rng = new RNG(5);
            foreach (Point2i p in film.PixelBounds())
            {
                for (int sample = 0; sample < 3; ++sample)
                {
                    SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                    SampledSpectrum fromDefault = Radiance(rng), fromWarm = Radiance(rng), fromCool = Radiance(rng);
                    LightLayers.Add(warm, 0.5f * fromWarm);
                    LightLayers.Add(warm, 0.5f * fromWarm);
                    LightLayers.Add(cool, fromCool);
                    film.AddSample(p, fromDefault + fromWarm + fromCool, lambda, null, 1);
                    plain.AddSample(p, fromDefault + fromWarm + fromCool, lambda, null, 1);
                    warmOnly.AddSample(p, fromWarm, lambda, null, 1);
                    coolOnly.AddSample(p, fromCool, lambda, null, 1);
                }
                // splats of a layer
                SampledWavelengths splatLambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                SampledSpectrum splat = Radiance(rng);
                film.AddSplat(new Point2f(p.x + 0.5f, p.y + 0.5f), splat, splatLambda, cool);
                plain.AddSplat(new Point2f(p.x + 0.5f, p.y + 0.5f), splat, splatLambda);
                coolOnly.AddSplat(new Point2f(p.x + 0.5f, p.y + 0.5f), splat, splatLambda);
            }
            // The scratch is empty again
            EXPECT_TRUE(LightLayers.Scratch() == null);

            // As they are, the layers are the image without layers
            Float splatScale = 0.25f;
            foreach (Point2i p in film.PixelBounds())
            {
                RGB a = film.GetPixelRGB(p, splatScale), b = plain.GetPixelRGB(p, splatScale);
                for (int c = 0; c < 3; ++c)
                {
                    EXPECT_NEAR(a[c], b[c], 2e-5f * (1 + Math.Abs(b[c])));
                }
            }
            Image image = film.GetImage(new ImageMetadata(), splatScale, LightLayers.Scene);
            ImageChannelDesc rgbDesc = image.GetChannelDesc(new string[] { "R", "G", "B" });
            ImageChannelDesc warmDesc = image.GetChannelDesc(new string[] { "warm.R", "warm.G", "warm.B" });
            ImageChannelDesc coolDesc = image.GetChannelDesc(new string[] { "cool.R", "cool.G", "cool.B" });
            ImageChannelDesc defaultDesc = image.GetChannelDesc(new string[] { "default.R", "default.G", "default.B" });
            ASSERT_TRUE((bool)rgbDesc && (bool)warmDesc && (bool)coolDesc && (bool)defaultDesc);
            foreach (Point2i p in film.PixelBounds())
            {
                ImageChannelValues all = image.GetChannels(p, rgbDesc), w = image.GetChannels(p, warmDesc), k = image.GetChannels(p, coolDesc);
                RGB b = plain.GetPixelRGB(p, splatScale), bw = warmOnly.GetPixelRGB(p, splatScale), bk = coolOnly.GetPixelRGB(p, splatScale);
                for (int c = 0; c < 3; ++c)
                {
                    EXPECT_NEAR(all[c], b[c], 2e-5f * (1 + Math.Abs(b[c])));
                    EXPECT_NEAR(w[c], bw[c], 2e-5f * (1 + Math.Abs(bw[c])));
                    EXPECT_NEAR(k[c], bk[c], 2e-5f * (1 + Math.Abs(bk[c])));
                }
            }

            // With settings: one layer off, one scaled and tinted. The layers themselves are in the image as they were.
            LightLayers.Scene[warm].enabled = false;
            LightLayers.Scene[cool].scale = 2;
            LightLayers.Scene[cool].tint = new RGB(1, 0.5f, 0.25f);
            Image set = film.GetImage(new ImageMetadata(), splatScale, LightLayers.Scene);
            foreach (Point2i p in film.PixelBounds())
            {
                ImageChannelValues all = set.GetChannels(p, rgbDesc), d = set.GetChannels(p, defaultDesc), k = set.GetChannels(p, coolDesc);
                ImageChannelValues before = image.GetChannels(p, coolDesc);
                EXPECT_NEAR(all[0], d[0] + 2 * k[0], 1e-4f * (1 + Math.Abs(all[0])));
                EXPECT_NEAR(all[1], d[1] + 2 * 0.5f * k[1], 1e-4f * (1 + Math.Abs(all[1])));
                EXPECT_NEAR(all[2], d[2] + 2 * 0.25f * k[2], 1e-4f * (1 + Math.Abs(all[2])));
                EXPECT_EQ(k[0], before[0]);
            }
            // A colour temperature below that of white makes a layer redder than it makes it blue
            RGB warmth = new LightLayers().Factor(0, RGBColorSpace.sRGB);
            EXPECT_EQ(warmth.r, 1f);
            LightLayers.Scene[cool].tint = new RGB(1, 1, 1);
            LightLayers.Scene[cool].scale = 1;
            LightLayers.Scene[cool].temperature = 2800;
            RGB tungsten = LightLayers.Scene.Factor(cool, RGBColorSpace.sRGB);
            EXPECT_GT(tungsten.r, tungsten.g);
            EXPECT_GT(tungsten.g, tungsten.b);
            LightLayers.Scene.Reset();
        }

        private const string scene =
            "LookAt 0 1 -4.2  0 0.6 0  0 1 0\nCamera \"perspective\" \"float fov\" 36\n" +
            "Sampler \"independent\" \"integer pixelsamples\" 4\nIntegrator \"path\" \"integer maxdepth\" 5\n" +
            "Film \"rgb\" \"integer xresolution\" 96 \"integer yresolution\" 72 \"string filename\" \"FILENAME\"\nWorldBegin\n" +
            "LightSource \"infinite\" \"rgb L\" [0.2 0.25 0.4] \"string layer\" \"sky\"\n" +
            "AttributeBegin\n  Attribute \"light\" \"string layer\" \"lamp\"\n  AreaLightSource \"diffuse\" \"rgb L\" [14 12 9]\n" +
            "  Translate -1.2 2.4 0\n  Shape \"sphere\" \"float radius\" 0.25\nAttributeEnd\n" +
            "Material \"diffuse\" \"rgb reflectance\" [0.6 0.6 0.6]\n" +
            "Shape \"trianglemesh\" \"point3 P\" [-6 0 -6 6 0 -6 6 0 6 -6 0 6] \"integer indices\" [0 1 2 0 2 3]\n" +
            "AttributeBegin\n  Translate 0 0.6 0\n  Shape \"sphere\" \"float radius\" 0.6\nAttributeEnd\n";

        // A render in the process of the tests leaves what a render sets on its threads: put back for the tests that follow
        private static void AfterRender()
        {
            Wireframe.differentialScale = 1;
            ForEachThread(() => { Wireframe.differentialScale = 1; });
            LightLayers.Scene.Reset();
        }

        private static void WaitFor(Func<bool> condition, int milliseconds)
        {
            int waited = 0;
            while (!condition() && waited < milliseconds)
            {
                Thread.Sleep(10);
                waited += 10;
            }
        }

        // A render that is watched, paused, rebalanced, resumed with other numbers of threads, and stopped: it ends on a whole
        // pass, well short of the passes it was allowed, and its file is that many samples a pixel
        [TEST("Progressive", "ASessionIsPausedAndStopped")]
        public static void ASessionIsPausedAndStopped()
        {
            string filename = Path.Combine(Path.GetTempPath(), "magi_progressive_test.exr");
            File.Delete(filename);
            BasicScene basicScene = new BasicScene();
            BasicSceneBuilder builder = new BasicSceneBuilder(basicScene);
            ParseString(builder, scene.Replace("FILENAME", filename.Replace("\\", "/")));
            // (the layers, in the order of the scene file)
            ASSERT_EQ(LightLayers.Scene.Count, 3);
            EXPECT_EQ(LightLayers.Scene[1].name, "sky");
            EXPECT_EQ(LightLayers.Scene[2].name, "lamp");

            int threadsBefore = RunningThreads();
            RenderSession session = new RenderSession();
            session.HaltSpp = 100000;
            session.DevelopInterval = 0.02;
            int developed = 0;
            session.Developed += (RenderSession s) => { Interlocked.Increment(ref developed); };
            RenderSession.Next = session;
            Thread render = new Thread(() => RenderCPU(basicScene));
            render.Start();

            // It renders, and there are images of it
            WaitFor(() => session.Passes >= 3 && session.LatestImage != null, 60000);
            ASSERT_TRUE(session.Passes >= 3);
            ASSERT_TRUE(session.LatestImage != null);
            EXPECT_TRUE(session.IsRendering);
            int area = session.Film.PixelBounds().Area();

            // Paused: the threads finish the tiles they are in, and then nothing moves
            session.Pause();
            EXPECT_TRUE(session.IsPaused);
            Thread.Sleep(300);
            long samples = session.Film.SamplesCounted();
            int passes = session.Passes;
            double elapsed = session.Elapsed;
            Thread.Sleep(300);
            EXPECT_EQ(session.Film.SamplesCounted(), samples);
            EXPECT_EQ(session.Passes, passes);
            EXPECT_EQ(session.Elapsed, elapsed);

            // A paused render still has images: with the lamp's layer off the picture is darker, with it doubled brighter
            Func<double> mean = () =>
            {
                Image image = session.LatestImage;
                ImageChannelDesc desc = image.GetChannelDesc(new string[] { "R", "G", "B" });
                double sum = 0;
                for (int y = 0; y < image.Resolution().y; ++y)
                {
                    for (int x = 0; x < image.Resolution().x; ++x)
                    {
                        sum += image.GetChannels(new Point2i(x, y), desc).Average();
                    }
                }
                return sum / (image.Resolution().x * image.Resolution().y);
            };
            Func<double> develop = () =>
            {
                Image before = session.LatestImage;
                session.RequestDevelop();
                WaitFor(() => !ReferenceEquals(session.LatestImage, before), 10000);
                EXPECT_FALSE(ReferenceEquals(session.LatestImage, before));
                return mean();
            };
            double all = develop();
            LightLayers.Scene[2].enabled = false;
            double withoutLamp = develop();
            LightLayers.Scene[2].enabled = true;
            LightLayers.Scene[2].scale = 2;
            double lampDoubled = develop();
            LightLayers.Scene[2].scale = 1;
            EXPECT_LT(withoutLamp, 0.95 * all);
            EXPECT_NEAR(lampDoubled - all, all - withoutLamp, 1e-3 * all);
            // (the splats of an image are those of the samples there are: a whole number of samples at a pause is not asked)
            EXPECT_GE(session.LatestImageSpp, passes);

            // Resumed with one thread, then with more than there were
            session.Threads = 1;
            EXPECT_EQ(session.Threads, 1);
            session.Resume();
            EXPECT_FALSE(session.IsPaused);
            WaitFor(() => session.Passes >= passes + 2, 60000);
            EXPECT_GE(session.Passes, passes + 2);
            session.Threads = threadsBefore + 2;
            EXPECT_EQ(session.Threads, threadsBefore + 2);
            int withMore = session.Passes;
            WaitFor(() => session.Passes >= withMore + 2, 60000);
            EXPECT_GE(session.Passes, withMore + 2);

            // Stopped, from a pause: the pass under way is finished
            session.Pause();
            session.Stop();
            EXPECT_TRUE(render.Join(60000));
            EXPECT_FALSE(session.IsRendering);
            int done = session.Passes;
            EXPECT_LT(done, 100000);
            EXPECT_EQ(session.Film.SamplesCounted(), (long)done * area);
            EXPECT_GT(developed, 3);
            session.Threads = threadsBefore;
            EXPECT_EQ(RunningThreads(), threadsBefore);

            // The file: that many samples, and the layers
            ASSERT_TRUE(File.Exists(filename));
            ImageAndMetadata written = Image.Read(filename);
            EXPECT_TRUE((bool)written.metadata.samplesPerPixel);
            EXPECT_EQ(written.metadata.samplesPerPixel.value(), done);
            EXPECT_TRUE((bool)written.image.GetChannelDesc(new string[] { "sky.R", "lamp.G", "default.B" }));
            File.Delete(filename);
            session.Dispose();
            AfterRender();
        }

        // A session with neither a sample count nor a time renders until it is stopped, and mlt renders in passes: well past
        // its mutations a pixel, with its chains kept from pass to pass
        [TEST("Progressive", "MltUntilStopped")]
        public static void MltUntilStopped()
        {
            string filename = Path.Combine(Path.GetTempPath(), "magi_progressive_mlt_test.exr");
            BasicScene basicScene = new BasicScene();
            BasicSceneBuilder builder = new BasicSceneBuilder(basicScene);
            ParseString(builder, scene.Replace("FILENAME", filename.Replace("\\", "/"))
                                      .Replace("Integrator \"path\" \"integer maxdepth\" 5",
                                               "Integrator \"mlt\" \"integer maxdepth\" 4 \"integer mutationsperpixel\" 2 \"integer bootstrapsamples\" 2000"));
            RenderSession session = new RenderSession();
            EXPECT_EQ(session.HaltSpp, 0);
            EXPECT_EQ(session.HaltTime, 0.0);
            session.DevelopInterval = 0.05;
            RenderSession.Next = session;
            Thread render = new Thread(() => RenderCPU(basicScene));
            render.Start();
            WaitFor(() => session.Passes >= 8 && session.LatestImage != null, 120000);
            EXPECT_GE(session.Passes, 8);
            EXPECT_TRUE(render.IsAlive);
            ASSERT_TRUE(session.LatestImage != null);
            // Paused and resumed
            session.Pause();
            Thread.Sleep(200);
            int paused = session.Passes;
            Thread.Sleep(200);
            EXPECT_EQ(session.Passes, paused);
            session.Resume();
            WaitFor(() => session.Passes >= paused + 2, 120000);
            EXPECT_GE(session.Passes, paused + 2);
            session.Stop();
            EXPECT_TRUE(render.Join(120000));
            int done = session.Passes;
            EXPECT_EQ(session.Film.SamplesCounted(), (long)done * session.Film.PixelBounds().Area());
            ImageAndMetadata written = Image.Read(filename);
            EXPECT_EQ(written.metadata.samplesPerPixel.value(), done);
            // (the image of the passes: about as bright as the path tracer's of the same scene, whatever their number)
            ImageChannelDesc desc = written.image.GetChannelDesc(new string[] { "R", "G", "B" });
            double sum = 0;
            for (int y = 0; y < written.image.Resolution().y; ++y)
            {
                for (int x = 0; x < written.image.Resolution().x; ++x)
                {
                    sum += written.image.GetChannels(new Point2i(x, y), desc).Average();
                }
            }
            double meanValue = sum / (written.image.Resolution().x * written.image.Resolution().y);
            EXPECT_GT(meanValue, 0.1);
            EXPECT_LT(meanValue, 0.5);
            File.Delete(filename);
            session.Dispose();
            AfterRender();
        }

        // jrlt renders in passes too (without the noise importance): until it is stopped, well past its mutations a pixel
        [TEST("Progressive", "JrltUntilStopped")]
        public static void JrltUntilStopped()
        {
            string filename = Path.Combine(Path.GetTempPath(), "magi_progressive_jrlt_test.exr");
            BasicScene basicScene = new BasicScene();
            BasicSceneBuilder builder = new BasicSceneBuilder(basicScene);
            ParseString(builder, scene.Replace("FILENAME", filename.Replace("\\", "/"))
                                      .Replace("Integrator \"path\" \"integer maxdepth\" 5",
                                               "Integrator \"jrlt\" \"integer maxdepth\" 4 \"integer mutationsperpixel\" 2 \"integer bootstrapsamples\" 2000"));
            RenderSession session = new RenderSession();
            EXPECT_EQ(session.HaltSpp, 0);
            EXPECT_EQ(session.HaltTime, 0.0);
            session.DevelopInterval = 0.05;
            RenderSession.Next = session;
            Thread render = new Thread(() => RenderCPU(basicScene));
            render.Start();
            WaitFor(() => session.Passes >= 8 && session.LatestImage != null, 120000);
            EXPECT_GE(session.Passes, 8);
            EXPECT_TRUE(render.IsAlive);
            ASSERT_TRUE(session.LatestImage != null);
            // Paused and resumed
            session.Pause();
            Thread.Sleep(200);
            int paused = session.Passes;
            Thread.Sleep(200);
            EXPECT_EQ(session.Passes, paused);
            session.Resume();
            WaitFor(() => session.Passes >= paused + 2, 120000);
            EXPECT_GE(session.Passes, paused + 2);
            session.Stop();
            EXPECT_TRUE(render.Join(120000));
            int done = session.Passes;
            EXPECT_EQ(session.Film.SamplesCounted(), (long)done * session.Film.PixelBounds().Area());
            ImageAndMetadata written = Image.Read(filename);
            EXPECT_EQ(written.metadata.samplesPerPixel.value(), done);
            // (the image of the passes: about as bright as the path tracer's of the same scene, whatever their number)
            ImageChannelDesc desc = written.image.GetChannelDesc(new string[] { "R", "G", "B" });
            double sum = 0;
            for (int y = 0; y < written.image.Resolution().y; ++y)
            {
                for (int x = 0; x < written.image.Resolution().x; ++x)
                {
                    sum += written.image.GetChannels(new Point2i(x, y), desc).Average();
                }
            }
            double meanValue = sum / (written.image.Resolution().x * written.image.Resolution().y);
            EXPECT_GT(meanValue, 0.1);
            EXPECT_LT(meanValue, 0.5);
            File.Delete(filename);
            session.Dispose();
            AfterRender();
        }

        // A time: the render ends at the end of the pass the time runs out in
        [TEST("Progressive", "HaltTime")]
        public static void HaltTime()
        {
            string filename = Path.Combine(Path.GetTempPath(), "magi_progressive_time_test.exr");
            BasicScene basicScene = new BasicScene();
            BasicSceneBuilder builder = new BasicSceneBuilder(basicScene);
            ParseString(builder, scene.Replace("FILENAME", filename.Replace("\\", "/")));
            RenderSession session = new RenderSession();
            session.HaltTime = 1.0;
            RenderSession.Next = session;
            RenderCPU(basicScene);
            EXPECT_GE(session.Elapsed, 1.0);
            EXPECT_LT(session.Elapsed, 20.0);
            EXPECT_GT(session.Passes, 4);
            EXPECT_EQ(session.Film.SamplesCounted(), (long)session.Passes * session.Film.PixelBounds().Area());
            EXPECT_EQ(Image.Read(filename).metadata.samplesPerPixel.value(), session.Passes);
            File.Delete(filename);
            session.Dispose();
            AfterRender();
        }
    }
}
