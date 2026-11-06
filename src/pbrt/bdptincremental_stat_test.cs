// (not in pbrt-v4) bdptincremental_stat_test.cs: images of "bdptinc" against "bdpt" (same seed: the same image; other seeds: the same
// expectation), white furnaces and the Stokes channels (docs/bdpt-incremental.md). The renders are in-process and small.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using testing;

namespace pbrt
{
    public static unsafe class BdptIncrementalStatTests
    {
        // One image of the scene with the given seed: every pixel sample of the integrator, the film at the end (rgb, row by row)
        public static float[] RenderImage(string text, int seed, int spp, out int width, out int height, out double seconds)
        {
            int savedSeed = Options.seed;
            Options.seed = seed;
            try
            {
                BdptIncrementalTests.Fixture fx = BdptIncrementalTests.Fixture.Build(text);
                ImageTileIntegrator integrator = (ImageTileIntegrator)fx.integrator;
                Film film = fx.camera.GetFilm();
                Bounds2i bounds = film.PixelBounds();
                ScratchBuffer scratch = new ScratchBuffer(65536);
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                foreach (Point2i pixel in bounds)
                {
                    for (int i = 0; i < spp; ++i)
                    {
                        fx.sampler.StartPixelSample(pixel, i);
                        integrator.EvaluatePixelSample(pixel, i, fx.sampler, scratch);
                        scratch.Reset();
                    }
                }
                seconds = watch.Elapsed.TotalSeconds;
                ImageMetadata metadata = new ImageMetadata();
                Image image = film.GetImage(metadata, 1.0f / spp);
                width = image.Resolution().x;
                height = image.Resolution().y;
                float[] rgb = new float[3 * width * height];
                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        for (int c = 0; c < 3; ++c)
                        {
                            rgb[3 * (y * width + x) + c] = image.GetChannel(new Point2i(x, y), c);
                        }
                    }
                }
                return rgb;
            }
            finally
            {
                Options.seed = savedSeed;
            }
        }

        static string Line(string integrator, int depth, string extra)
        {
            return "Integrator \"" + integrator + "\" \"integer maxdepth\" " + depth + " " + extra;
        }

        // The same pixel samples under both integrators give the same image (the weights differ by float rounding)
        static void ExpectSameImage(string label, Func<string, string> scene, int spp, int seed, bool mayBeBlack = false)
        {
            float[] a = RenderImage(scene("bdpt"), seed, spp, out int w, out int h, out double ta);
            float[] b = RenderImage(scene("bdptinc"), seed, spp, out int w2, out int h2, out double tb);
            EXPECT_EQ(w, w2);
            double mean = 0;
            for (int i = 0; i < a.Length; ++i)
            {
                mean += Math.Abs(a[i]);
            }
            mean /= a.Length;
            double worst = 0, worstAbs = 0;
            int differing = 0;
            for (int i = 0; i < a.Length; ++i)
            {
                double d = Math.Abs(a[i] - b[i]);
                worstAbs = Math.Max(worstAbs, d);
                double rel = d / Math.Max(Math.Abs(a[i]), 1e-2 * mean);
                worst = Math.Max(worst, rel);
                if (d > 0)
                {
                    ++differing;
                }
            }
            Console.WriteLine("[BdptInc] same-seed image " + label + ": " + w + "x" + h + ", " + spp + " spp, seed " + seed + ", mean " + mean.ToString("G5", CultureInfo.InvariantCulture) +
                              ", pixels that differ " + differing + " of " + a.Length + ", worst relative difference (floor 1% of the mean) " + worst.ToString("G3", CultureInfo.InvariantCulture) +
                              ", worst absolute " + worstAbs.ToString("G3", CultureInfo.InvariantCulture) + ", time " + ta.ToString("F2", CultureInfo.InvariantCulture) + " s vs " + tb.ToString("F2", CultureInfo.InvariantCulture) + " s");
            EXPECT_TRUE(mean > 0 || mayBeBlack);
            EXPECT_TRUE(!(worst >= 1e-3));
        }

        [TEST("BdptInc", "SameSeedImagesSurfaces")]
        public static void BdptInc_SameSeedImagesSurfaces()
        {
            ExpectSameImage("room, regularize", (string i) => BdptIncrementalTests.RoomScene("room", "", Line(i, 8, "\"bool regularize\" true"), 16, 1), 16, 3);
            ExpectSameImage("room, russian roulette", (string i) => BdptIncrementalTests.RoomScene("room", "", Line(i, 12, "\"bool russianroulette\" true"), 16, 1), 16, 4);
            ExpectSameImage("specular spheres", (string i) => BdptIncrementalTests.RoomScene("specular", "", Line(i, 8, ""), 16, 1), 16, 5);
            ExpectSameImage("glossy caustic", (string i) => BdptIncrementalTests.RoomScene("glossy", "", Line(i, 8, ""), 16, 1), 16, 6);
            ExpectSameImage("point light", (string i) => BdptIncrementalTests.RoomScene("point", "", Line(i, 8, ""), 16, 1), 16, 7);
            ExpectSameImage("spot light", (string i) => BdptIncrementalTests.RoomScene("spot", "", Line(i, 8, ""), 16, 1), 16, 8);
            ExpectSameImage("distant light", (string i) => BdptIncrementalTests.RoomScene("distant", "", Line(i, 8, ""), 16, 1), 16, 9);
            ExpectSameImage("infinite light", (string i) => BdptIncrementalTests.RoomScene("infinite", "", Line(i, 8, ""), 16, 1), 16, 10);
            ExpectSameImage("unidirectional strategies", (string i) => BdptIncrementalTests.RoomScene("room", "", Line(i, 8, "\"string strategies\" \"unidirectional\""), 16, 1), 16, 11);
        }

        [TEST("BdptInc", "SameSeedImagesMedia")]
        public static void BdptInc_SameSeedImagesMedia()
        {
            foreach (string media in new string[] { "fog", "foghg", "fogchrom", "foggrid", "fograyleigh", "fognested" })
            {
                string m = media;
                ExpectSameImage(m, (string i) => BdptIncrementalTests.RoomScene("room", m, Line(i, 8, ""), 16, 1), 16, 12);
            }
            ExpectSameImage("emissive medium, gathering", (string i) => BdptIncrementalTests.RoomScene("room", "fogemit", Line(i, 6, ""), 16, 1), 16, 13);
            ExpectSameImage("emissive medium, no gathering", (string i) => BdptIncrementalTests.RoomScene("room", "fogemit", Line(i, 6, "\"bool gathermediumemission\" false"), 16, 1), 16, 14);
            ExpectSameImage("equiangular, point light", (string i) => BdptIncrementalTests.RoomScene("point", "foghg", Line(i, 6, "\"bool equiangular\" true"), 16, 1), 16, 15);
            ExpectSameImage("equiangular 3 samples, chromatic", (string i) => BdptIncrementalTests.RoomScene("room", "fogchrom", Line(i, 6, "\"bool equiangular\" true \"integer equiangularsamples\" 3"), 16, 1), 16, 16);
        }

        [TEST("BdptInc", "SameSeedImagesPolarised")]
        public static void BdptInc_SameSeedImagesPolarised()
        {
            foreach (string measure in new string[] { "intensity", "s1", "s2", "s3" })
            {
                string m = measure;
                ExpectSameImage("polarised " + m, (string i) => BdptIncrementalTests.RoomScene("polarised", "", Line(i, 6, "\"bool polarised\" true \"string measure\" \"" + m + "\""), 16, 1), 16, 17, m == "s3");
            }
            ExpectSameImage("polarised analyser 30 degrees, Rayleigh fog", (string i) => BdptIncrementalTests.RoomScene("polarised", "fograyleigh", Line(i, 6, "\"bool polarised\" true \"float analyser\" 30"), 16, 1), 16, 18);
        }

        // An emitter that moves during the shutter, and a sphere on an animated transformation: the time of the camera ray is the time of every vertex
        static string AnimatedScene(string integrator, int res)
        {
            string s = "LookAt 0 2.6 -7.2  0 0.7 0  0 1 0\nCamera \"perspective\" \"float fov\" 38\nSampler \"independent\"\n";
            s += "Integrator \"" + integrator + "\" \"integer maxdepth\" 6\nFilm \"rgb\" \"integer xresolution\" " + res + " \"integer yresolution\" " + res + " \"string filename\" \"out.exr\"\nWorldBegin\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.55 0.55 0.55]\nShape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-7 0 -7  7 0 -7  7 0 7  -7 0 7]\nAttributeEnd\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.25 0.3 0.6]\nShape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-7 0 3.2  7 0 3.2  7 6 3.2  -7 6 3.2]\nAttributeEnd\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.8 0.45 0.3]\nTranslate 0.2 0.8 0.3\nShape \"sphere\" \"float radius\" 0.8\nAttributeEnd\n";
            s += "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [14 13 11] \"bool twosided\" true\n" +
                 "ActiveTransform StartTime\nTranslate -2.2 2.4 0.2\nActiveTransform EndTime\nTranslate 2.2 2.9 0.2\nActiveTransform All\n" +
                 "Shape \"trianglemesh\" \"point3 P\" [ -0.6 -0.6 0  0.6 -0.6 0  0.6 0.6 0  -0.6 0.6 0 ] \"integer indices\" [0 1 2 0 2 3]\nAttributeEnd\n";
            return s;
        }

        [TEST("BdptInc", "SameSeedImagesAnimatedLight")]
        public static void BdptInc_SameSeedImagesAnimatedLight()
        {
            ExpectSameImage("moving quad light", (string i) => AnimatedScene(i, 24), 16, 19);
        }

        // ------------------------------------------------------------------------------------------
        // Statistics: images over independent seeds

        static double ImageMean(float[] image, int channel)
        {
            double sum = 0;
            int n = 0;
            for (int i = channel < 0 ? 0 : channel; i < image.Length; i += channel < 0 ? 1 : 3)
            {
                sum += image[i];
                ++n;
            }
            return sum / n;
        }

        static void MeanAndError(List<float[]> images, int channel, out double mean, out double standardError)
        {
            double sum = 0, sumSquares = 0;
            foreach (float[] image in images)
            {
                double m = ImageMean(image, channel);
                sum += m;
                sumSquares += m * m;
            }
            int n = images.Count;
            mean = sum / n;
            standardError = Math.Sqrt(Math.Max(sumSquares / n - mean * mean, 0) / (n - 1));
        }

        static double TStatistic(List<float[]> a, List<float[]> b, int channel, out double meanA, out double meanB)
        {
            MeanAndError(a, channel, out meanA, out double seA);
            MeanAndError(b, channel, out meanB, out double seB);
            return (meanA - meanB) / Math.Sqrt(seA * seA + seB * seB);
        }

        static void PixelStatistics(List<float[]> images, out double[] mean, out double[] varianceOfMean)
        {
            int length = images[0].Length;
            mean = new double[length];
            varianceOfMean = new double[length];
            int n = images.Count;
            for (int i = 0; i < length; ++i)
            {
                double sum = 0, sumSquares = 0;
                foreach (float[] image in images)
                {
                    sum += image[i];
                    sumSquares += (double)image[i] * image[i];
                }
                mean[i] = sum / n;
                varianceOfMean[i] = Math.Max(sumSquares / n - mean[i] * mean[i], 0) * n / (n - 1.0) / n;
            }
        }

        // The log-log slope of the root mean square error of the average of G images against the reference (the noise of the reference removed)
        static double ErrorSlope(List<float[]> images, List<float[]> reference, out string table)
        {
            PixelStatistics(reference, out double[] refMean, out double[] refVarOfMean);
            double refNoise = 0;
            for (int i = 0; i < refMean.Length; ++i)
            {
                refNoise += refVarOfMean[i];
            }
            refNoise /= refMean.Length;
            List<double> logG = new List<double>();
            List<double> logError = new List<double>();
            table = "";
            Random random = new Random(12345);
            const int partitions = 40;
            int[][] orders = new int[partitions][];
            for (int p = 0; p < partitions; ++p)
            {
                int[] order = new int[images.Count];
                for (int i = 0; i < order.Length; ++i)
                {
                    order[i] = i;
                }
                for (int i = order.Length - 1; i > 0; --i)
                {
                    int j = random.Next(i + 1);
                    int swap = order[i];
                    order[i] = order[j];
                    order[j] = swap;
                }
                orders[p] = order;
            }
            for (int g = 1; g * 2 <= images.Count; g *= 2)
            {
                int groups = images.Count / g;
                double mse = 0;
                for (int p = 0; p < partitions; ++p)
                {
                    for (int group = 0; group < groups; ++group)
                    {
                        for (int i = 0; i < refMean.Length; ++i)
                        {
                            double sum = 0;
                            for (int j = 0; j < g; ++j)
                            {
                                sum += images[orders[p][group * g + j]][i];
                            }
                            double d = sum / g - refMean[i];
                            mse += d * d;
                        }
                    }
                }
                mse /= partitions * groups * refMean.Length;
                double corrected = mse - refNoise;
                table += " G=" + g + ":" + Math.Sqrt(Math.Max(corrected, 1e-30)).ToString("G3", CultureInfo.InvariantCulture);
                logG.Add(Math.Log(g));
                logError.Add(0.5 * Math.Log(Math.Max(corrected, 1e-30)));
            }
            double mx = 0, my = 0;
            for (int i = 0; i < logG.Count; ++i)
            {
                mx += logG[i];
                my += logError[i];
            }
            mx /= logG.Count;
            my /= logG.Count;
            double sxy = 0, sxx = 0;
            for (int i = 0; i < logG.Count; ++i)
            {
                sxy += (logG[i] - mx) * (logError[i] - my);
                sxx += (logG[i] - mx) * (logG[i] - mx);
            }
            return sxy / sxx;
        }

        // 96 images of "bdpt" (the reference) against 32 images of "bdptinc" with other seeds: the mean of every channel (t statistic, |t| < 4),
        // and the error of the average of G images falls as G^-1/2
        static void Unbiasedness(string label, Func<string, string> scene, int spp, bool expectSignedChannels = false)
        {
            List<float[]> reference = new List<float[]>(), test = new List<float[]>();
            double ta = 0, tb = 0;
            for (int i = 0; i < 96; ++i)
            {
                reference.Add(RenderImage(scene("bdpt"), 1000 + i, spp, out int w, out int h, out double s));
                ta += s;
            }
            for (int i = 0; i < 32; ++i)
            {
                test.Add(RenderImage(scene("bdptinc"), 5000 + i, spp, out int w, out int h, out double s));
                tb += s;
            }
            string channels = "";
            for (int c = 0; c < 3; ++c)
            {
                double t = TStatistic(test, reference, c, out double mt, out double mr);
                channels += " t" + "RGB"[c] + "=" + t.ToString("F2", CultureInfo.InvariantCulture) + " (" + mt.ToString("G5", CultureInfo.InvariantCulture) + " vs " + mr.ToString("G5", CultureInfo.InvariantCulture) + ")";
                EXPECT_TRUE(Math.Abs(t) < 4.0);
            }
            double slope = ErrorSlope(test, reference, out string table);
            Console.WriteLine("[BdptInc] unbiasedness " + label + ": 96 bdpt vs 32 bdptinc images of " + spp + " spp:" + channels + ", RMSE of G averaged images" + table + ", log-log slope " +
                              slope.ToString("F3", CultureInfo.InvariantCulture) + ", render time per image bdpt " + (ta / 96).ToString("F3", CultureInfo.InvariantCulture) + " s, bdptinc " + (tb / 32).ToString("F3", CultureInfo.InvariantCulture) + " s");
            if (!label.EndsWith(" s1") && !label.EndsWith(" s2"))
            {
                // (the Stokes s1 and s2 images are nearly zero-mean noise: no 1/sqrt(n) law to check)
                EXPECT_TRUE(slope > -0.6 && slope < -0.4);
            }
        }

        [TEST("BdptInc", "UnbiasedAgainstBdptSurfaces")]
        public static void BdptInc_UnbiasedAgainstBdptSurfaces()
        {
            Unbiasedness("room", (string i) => BdptIncrementalTests.RoomScene("room", "", Line(i, 6, ""), 8, 1), 8);
            Unbiasedness("specular spheres", (string i) => BdptIncrementalTests.RoomScene("specular", "", Line(i, 6, ""), 8, 1), 8);
            Unbiasedness("glossy caustic", (string i) => BdptIncrementalTests.RoomScene("glossy", "", Line(i, 6, ""), 8, 1), 8);
        }

        [TEST("BdptInc", "UnbiasedAgainstBdptMedia")]
        public static void BdptInc_UnbiasedAgainstBdptMedia()
        {
            Unbiasedness("homogeneous fog", (string i) => BdptIncrementalTests.RoomScene("room", "foghg", Line(i, 6, ""), 8, 1), 8);
            Unbiasedness("grid, chromatic", (string i) => BdptIncrementalTests.RoomScene("room", "foggrid", Line(i, 6, ""), 8, 1), 8);
            Unbiasedness("nested media", (string i) => BdptIncrementalTests.RoomScene("room", "fognested", Line(i, 6, ""), 8, 1), 8);
            Unbiasedness("chromatic fog", (string i) => BdptIncrementalTests.RoomScene("room", "fogchrom", Line(i, 6, ""), 8, 1), 8);
            Unbiasedness("emissive medium", (string i) => BdptIncrementalTests.RoomScene("room", "fogemit", Line(i, 6, ""), 8, 1), 8);
            Unbiasedness("equiangular", (string i) => BdptIncrementalTests.RoomScene("point", "foghg", Line(i, 6, "\"bool equiangular\" true"), 8, 1), 8);
        }

        [TEST("BdptInc", "UnbiasedAgainstBdptPolarised")]
        public static void BdptInc_UnbiasedAgainstBdptPolarised()
        {
            foreach (string measure in new string[] { "intensity", "s1", "s2" })
            {
                string m = measure;
                Unbiasedness("polarised " + m, (string i) => BdptIncrementalTests.RoomScene("polarised", "", Line(i, 6, "\"bool polarised\" true \"string measure\" \"" + m + "\""), 8, 1), 8, true);
            }
        }

        // The polarisation is not lost (the tests above would pass vacuously with a depolarised path): with the same seed, the image behind an analyser at 0
        // degrees differs from the one at 90 degrees (a depolarised transport would give identical images)
        [TEST("BdptInc", "PolarisedAnalyserChangesTheImage")]
        public static void BdptInc_PolarisedAnalyserChangesTheImage()
        {
            string scene(int angle) => BdptIncrementalTests.RoomScene("polarised", "", Line("bdptinc", 6, "\"bool polarised\" true \"float analyser\" " + angle), 24, 1);
            float[] a0 = RenderImage(scene(0), 70, 64, out int w, out int h, out double t0);
            float[] a90 = RenderImage(scene(90), 70, 64, out w, out h, out t0);
            double mean = 0, sumDiff = 0, worst = 0;
            for (int i = 0; i < a0.Length; ++i)
            {
                mean += a0[i];
            }
            mean /= a0.Length;
            for (int i = 0; i < a0.Length; ++i)
            {
                double d = Math.Abs(a0[i] - a90[i]);
                sumDiff += d;
                worst = Math.Max(worst, d / Math.Max(Math.Abs(a0[i]), 0.01 * mean));
            }
            double meanRelative = sumDiff / a0.Length / mean;
            Console.WriteLine("[BdptInc] analyser 0 against 90 degrees, same seed: mean absolute difference / mean " + meanRelative.ToString("G3", CultureInfo.InvariantCulture) +
                              ", worst pixel relative difference " + worst.ToString("G3", CultureInfo.InvariantCulture));
            EXPECT_GT(meanRelative, 1e-5);
        }

        // ------------------------------------------------------------------------------------------
        // White furnaces

        static string Furnace(string integrator, int depth, string body, int res)
        {
            return "LookAt 0 0 -5  0 0 0  0 1 0\nCamera \"perspective\" \"float fov\" 30\nSampler \"independent\"\n" + Line(integrator, depth, "") +
                   "\nFilm \"rgb\" \"integer xresolution\" " + res + " \"integer yresolution\" " + res + " \"string filename\" \"out.exr\"\nWorldBegin\n" +
                   "LightSource \"infinite\" \"rgb L\" [1 1 1]\n" + body;
        }

        // The radiance of a pixel of the object that is entirely covered by it (the middle of the image): the average of the pixels in the middle
        static double CenterMean(float[] image, int width, int height)
        {
            double sum = 0;
            int n = 0;
            for (int y = height / 3; y < 2 * height / 3; ++y)
            {
                for (int x = width / 3; x < 2 * width / 3; ++x)
                {
                    for (int c = 0; c < 3; ++c)
                    {
                        sum += image[3 * (y * width + x) + c];
                        ++n;
                    }
                }
            }
            return sum / n;
        }

        // A convex diffuse object of reflectance 1 under a uniform sky radiates 1 (the rays leave after one bounce), a ball of a medium
        // with albedo 1 does too (after the scatterings, up to the depth)
        [TEST("BdptInc", "WhiteFurnace")]
        public static void BdptInc_WhiteFurnace()
        {
            string diffuse = "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [1 1 1]\nShape \"sphere\" \"float radius\" 1\nAttributeEnd\n";
            string medium = "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0 0 0] \"rgb sigma_s\" [1.5 1.5 1.5] \"float g\" 0.4\n";
            string ball = "AttributeBegin\nMediumInterface \"fog\" \"\"\nMaterial \"interface\"\nShape \"sphere\" \"float radius\" 1\nAttributeEnd\n";
            foreach (string integrator in new string[] { "bdpt", "bdptinc" })
            {
                float[] a = RenderImage(Furnace(integrator, 8, diffuse, 12), 1, 64, out int w, out int h, out double t);
                double mean = CenterMean(a, w, h);
                Console.WriteLine("[BdptInc] furnace " + integrator + " diffuse sphere albedo 1: centre mean " + mean.ToString("F4", CultureInfo.InvariantCulture));
                EXPECT_TRUE(Math.Abs(mean - 1) < 0.02);
                // the medium is declared before the camera: the text has to put MakeNamedMedium in the options block
                string text = Furnace(integrator, 60, ball, 12).Replace("LookAt", medium + "LookAt");
                float[] b = RenderImage(text, 2, 64, out w, out h, out t);
                double meanMedium = CenterMean(b, w, h);
                Console.WriteLine("[BdptInc] furnace " + integrator + " scattering ball albedo 1 (sigma_s 1.5, radius 1, depth 60): centre mean " + meanMedium.ToString("F4", CultureInfo.InvariantCulture));
                EXPECT_TRUE(Math.Abs(meanMedium - 1) < 0.03);
            }
        }
    }
}
