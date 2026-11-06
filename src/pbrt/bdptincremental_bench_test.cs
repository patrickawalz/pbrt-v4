// (not in pbrt-v4) bdptincremental_bench_test.cs: in-process timing of "bdpt" and "bdptinc" (docs/bdpt-incremental.md). Runs only
// when BDPTINC_BENCH is set (BDPTINC_SCENES, BDPTINC_DEPTHS, BDPTINC_SAMPLES, BDPTINC_ROUNDS choose the cases).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using testing;

namespace pbrt
{
    public static unsafe class BdptIncrementalBench
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Timespec
        {
            public long seconds;
            public long nanoseconds;
        }

        [DllImport("libc", EntryPoint = "clock_gettime")]
        static extern int ClockGetTime(int clock, out Timespec ts);

        // CPU time of the calling thread, in seconds (not disturbed by the other processes of the machine, unlike the wall clock)
        static double ThreadCpuSeconds()
        {
            ClockGetTime(3, out Timespec ts);
            return ts.seconds + ts.nanoseconds * 1e-9;
        }

        static string Env(string name, string fallback)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        const int Resolution = 32;

        static string SceneText(string kind, string integrator, int depth)
        {
            string media = kind == "fog" ? "fog" : kind == "grid" ? "foggrid" : "";
            string variant = kind == "fog" || kind == "grid" ? "room" : kind;
            string polarised = kind == "polarised" ? " \"bool polarised\" true" : "";
            return BdptIncrementalTests.RoomScene(variant, media, "Integrator \"" + integrator + "\" \"integer maxdepth\" " + depth + polarised, Resolution, 1);
        }

        // CPU seconds and allocated bytes per pixel sample
        static void TimeSamples(RayIntegrator integrator, Sampler prototype, int count, int firstIndex, out double cpu, out double wall, out double bytes)
        {
            Sampler sampler = prototype.Clone();
            ScratchBuffer scratch = new ScratchBuffer(65536);
            long bytes0 = GC.GetAllocatedBytesForCurrentThread();
            double c0 = ThreadCpuSeconds();
            long w0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; ++i)
            {
                Point2i pixel = new Point2i((i * 7) % Resolution, (i / Resolution * 5 + i) % Resolution);
                int index = firstIndex + i;
                sampler.StartPixelSample(pixel, index);
                integrator.EvaluatePixelSample(pixel, index, sampler, scratch);
                scratch.Reset();
            }
            double c1 = ThreadCpuSeconds();
            long w1 = Stopwatch.GetTimestamp();
            bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes0) / (double)count;
            cpu = (c1 - c0) / count;
            wall = (w1 - w0) / (double)Stopwatch.Frequency / count;
        }

        static double Median(List<double> v)
        {
            List<double> s = new List<double>(v);
            s.Sort();
            return s[s.Count / 2];
        }

        [TEST("BdptInc", "BenchmarkInProcess")]
        public static void BdptInc_BenchmarkInProcess()
        {
            if (Environment.GetEnvironmentVariable("BDPTINC_BENCH") == null)
            {
                return;
            }
            string[] scenes = Env("BDPTINC_SCENES", "cornell,glossy,fog,grid,polarised").Split(',');
            string[] depths = Env("BDPTINC_DEPTHS", "5,10,20").Split(',');
            int samples = int.Parse(Env("BDPTINC_SAMPLES", "3000"), CultureInfo.InvariantCulture);
            int rounds = int.Parse(Env("BDPTINC_ROUNDS", "7"), CultureInfo.InvariantCulture);
            Console.WriteLine("[BdptIncBench] vertex sizes: VertexMIS " + System.Runtime.CompilerServices.Unsafe.SizeOf<VertexMIS>() + " bytes");
            {
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                Vertex v = new Vertex();
                long b1 = GC.GetAllocatedBytesForCurrentThread();
                Console.WriteLine("[BdptIncBench] new Vertex() allocates " + (b1 - b0) + " bytes (Vertex plus its EndpointInteraction)");
                GC.KeepAlive(v);
            }
            foreach (string scene in scenes)
            {
                foreach (string depthText in depths)
                {
                    int depth = int.Parse(depthText, CultureInfo.InvariantCulture);
                    BdptIncrementalTests.Fixture oldFx = BdptIncrementalTests.Fixture.Build(SceneText(scene, "bdpt", depth));
                    BdptIncrementalTests.Fixture newFx = BdptIncrementalTests.Fixture.Build(SceneText(scene, "bdptinc", depth));
                    RayIntegrator oldI = (RayIntegrator)oldFx.integrator, newI = (RayIntegrator)newFx.integrator;
                    // warm up (JIT tiers, caches)
                    for (int w = 0; w < 3; ++w)
                    {
                        TimeSamples(oldI, oldFx.sampler, samples / 2, 0, out double _, out double _, out double _);
                        TimeSamples(newI, newFx.sampler, samples / 2, 0, out double _, out double _, out double _);
                    }
                    List<double> oldCpu = new List<double>(), newCpu = new List<double>(), oldBytes = new List<double>(), newBytes = new List<double>();
                    for (int r = 0; r < rounds; ++r)
                    {
                        double c, w, b;
                        if (r % 2 == 0)
                        {
                            TimeSamples(oldI, oldFx.sampler, samples, 1000 + r * samples, out c, out w, out b);
                            oldCpu.Add(c);
                            oldBytes.Add(b);
                            TimeSamples(newI, newFx.sampler, samples, 1000 + r * samples, out c, out w, out b);
                            newCpu.Add(c);
                            newBytes.Add(b);
                        }
                        else
                        {
                            TimeSamples(newI, newFx.sampler, samples, 1000 + r * samples, out c, out w, out b);
                            newCpu.Add(c);
                            newBytes.Add(b);
                            TimeSamples(oldI, oldFx.sampler, samples, 1000 + r * samples, out c, out w, out b);
                            oldCpu.Add(c);
                            oldBytes.Add(b);
                        }
                    }
                    oldCpu.Sort();
                    newCpu.Sort();
                    Console.WriteLine("[BdptIncBench] " + scene + " depth " + depth + ": bdpt median " + (Median(oldCpu) * 1e6).ToString("F2", CultureInfo.InvariantCulture) +
                                      " us/sample (min " + (oldCpu[0] * 1e6).ToString("F2", CultureInfo.InvariantCulture) + ", max " + (oldCpu[oldCpu.Count - 1] * 1e6).ToString("F2", CultureInfo.InvariantCulture) +
                                      "), bdptinc median " + (Median(newCpu) * 1e6).ToString("F2", CultureInfo.InvariantCulture) +
                                      " us/sample (min " + (newCpu[0] * 1e6).ToString("F2", CultureInfo.InvariantCulture) + ", max " + (newCpu[newCpu.Count - 1] * 1e6).ToString("F2", CultureInfo.InvariantCulture) +
                                      "), ratio new/old of medians " + (Median(newCpu) / Median(oldCpu)).ToString("F3", CultureInfo.InvariantCulture) +
                                      ", ratio of minima " + (newCpu[0] / oldCpu[0]).ToString("F3", CultureInfo.InvariantCulture) +
                                      ", allocated bytes/sample old " + Median(oldBytes).ToString("F0", CultureInfo.InvariantCulture) + " new " + Median(newBytes).ToString("F0", CultureInfo.InvariantCulture));
                }
            }
        }

        // The split of the time of one sample: subpath generation, the connections without the weight, and the weight (old: BDPT.MISWeight,
        // new: BDPTIncremental.MISWeight on the same inputs). Timestamps cost about 25 ns each; the overhead is measured and reported.
        sealed class Split
        {
            public long generate, connect, weight;
            public long weightCalls;
            public long connections;
        }

        static void ProfileSamples(BdptIncrementalTests.Fixture fx, bool incremental, int depth, int count, Split split)
        {
            Camera camera = fx.camera;
            Sampler sampler = fx.sampler.Clone();
            Integrator integrator = fx.integrator;
            LightSampler lightSampler = incremental ? ((BDPTIncrementalIntegrator)integrator).BDPTLightSampler : ((BDPTIntegrator)integrator).BDPTLightSampler;
            ScratchBuffer scratch = new ScratchBuffer(65536);
            Film film = camera.GetFilm();
            Float splatScale = (Float)film.FullResolution().x * (Float)film.FullResolution().y / (Float)film.PixelBounds().Area();
            VertexMIS[] cameraState = new VertexMIS[depth + 2];
            VertexMIS[] lightState = new VertexMIS[depth + 1];
            for (int i = 0; i < count; ++i)
            {
                Point2i pixel = new Point2i((i * 7) % Resolution, (i / Resolution * 5 + i) % Resolution);
                sampler.StartPixelSample(pixel, i);
                Float lu = sampler.Get1D();
                SampledWavelengths lambda = film.SampleWavelengths(lu);
                CameraSample cameraSample = GetCameraSampleForBench(sampler, pixel, film);
                pstd.optional<CameraRayDifferential> cameraRay = camera.GenerateRayDifferential(cameraSample, ref lambda);
                if (!cameraRay)
                {
                    continue;
                }
                RayDifferential ray = cameraRay.value().ray;
                long t0 = Stopwatch.GetTimestamp();
                Vertex[] cameraVertices = new Vertex[depth + 2];
                for (int k = 0; k < cameraVertices.Length; ++k)
                {
                    cameraVertices[k] = new Vertex();
                }
                Vertex[] lightVertices = new Vertex[depth + 1];
                for (int k = 0; k < lightVertices.Length; ++k)
                {
                    lightVertices[k] = new Vertex();
                }
                bool polarised = false;
                int nCamera, nLight;
                if (incremental)
                {
                    nCamera = BDPTIncremental.GenerateCameraSubpath(integrator, ray, ref lambda, sampler, scratch, depth + 2, camera, cameraVertices,
                                                                    cameraState, false, splatScale, polarised, null, null);
                    nLight = BDPTIncremental.GenerateLightSubpath(integrator, ref lambda, sampler, camera, scratch, depth + 1, cameraVertices[0].time(),
                                                                  lightSampler, lightVertices, lightState, false, splatScale, polarised);
                }
                else
                {
                    nCamera = BDPT.GenerateCameraSubpath(integrator, ray, ref lambda, sampler, scratch, depth + 2, camera, cameraVertices, false,
                                                         polarised, null, null);
                    nLight = BDPT.GenerateLightSubpath(integrator, ref lambda, sampler, camera, scratch, depth + 1, cameraVertices[0].time(),
                                                       lightSampler, lightVertices, false, polarised);
                }
                long t1 = Stopwatch.GetTimestamp();
                split.generate += t1 - t0;
                for (int t = 1; t <= nCamera; ++t)
                {
                    for (int s = 0; s <= nLight; ++s)
                    {
                        int d = t + s - 2;
                        if ((s == 1 && t == 1) || d < 0 || d > depth)
                        {
                            continue;
                        }
                        pstd.optional<Point2f> pFilm = default;
                        long a = Stopwatch.GetTimestamp();
                        BDPTIncremental.Connection c = BDPTIncremental.EvaluateConnection(integrator, ref lambda, lightVertices, cameraVertices, s, t,
                                                                                          lightSampler, camera, sampler, ref pFilm);
                        long b = Stopwatch.GetTimestamp();
                        split.connect += b - a;
                        ++split.connections;
                        if (c.valid && (bool)c.L)
                        {
                            Float w = incremental
                                ? BDPTIncremental.MISWeight(integrator, camera, lightVertices, lightState, cameraVertices, cameraState, c.sampled, s, t,
                                                            lightSampler, c.connRu, c.connRl)
                                : BDPT.MISWeight(integrator, camera, lightVertices, cameraVertices, c.sampled, s, t, lightSampler, c.connRu, c.connRl);
                            long e = Stopwatch.GetTimestamp();
                            split.weight += e - b;
                            ++split.weightCalls;
                            GC.KeepAlive(w);
                        }
                    }
                }
            }
        }

        static CameraSample GetCameraSampleForBench(Sampler sampler, Point2i pixel, Film film)
        {
            return pbrt.GetCameraSample(sampler, pixel, film.GetFilter());
        }

        [TEST("BdptInc", "ProfileSplit")]
        public static void BdptInc_ProfileSplit()
        {
            if (Environment.GetEnvironmentVariable("BDPTINC_BENCH") == null)
            {
                return;
            }
            string[] scenes = Env("BDPTINC_SCENES", "cornell,glossy,fog,grid,polarised").Split(',');
            string[] depths = Env("BDPTINC_DEPTHS", "5,10,20").Split(',');
            int samples = int.Parse(Env("BDPTINC_SAMPLES", "3000"), CultureInfo.InvariantCulture);
            // the cost of a timestamp pair
            long o0 = Stopwatch.GetTimestamp();
            long sink = 0;
            for (int i = 0; i < 1000000; ++i)
            {
                sink += Stopwatch.GetTimestamp();
            }
            double perTimestampNs = (Stopwatch.GetTimestamp() - o0) * 1e9 / Stopwatch.Frequency / 1000000;
            Console.WriteLine("[BdptIncBench] timestamp cost " + perTimestampNs.ToString("F1", CultureInfo.InvariantCulture) + " ns (sink " + (sink & 1) + ")");
            foreach (string scene in scenes)
            {
                foreach (string depthText in depths)
                {
                    int depth = int.Parse(depthText, CultureInfo.InvariantCulture);
                    foreach (bool incremental in new bool[] { false, true })
                    {
                        BdptIncrementalTests.Fixture fx = BdptIncrementalTests.Fixture.Build(SceneText(scene, incremental ? "bdptinc" : "bdpt", depth));
                        ProfileSamples(fx, incremental, depth, samples / 3, new Split());
                        Split split = new Split();
                        ProfileSamples(fx, incremental, depth, samples, split);
                        double total = split.generate + split.connect + split.weight;
                        double toUs = 1e6 / Stopwatch.Frequency / samples;
                        Console.WriteLine("[BdptIncBench] split " + scene + " depth " + depth + " " + (incremental ? "bdptinc" : "bdpt   ") + ": generate " +
                                          (split.generate * toUs).ToString("F2", CultureInfo.InvariantCulture) + " us, connections " +
                                          (split.connect * toUs).ToString("F2", CultureInfo.InvariantCulture) + " us (" + (split.connections / (double)samples).ToString("F1", CultureInfo.InvariantCulture) +
                                          " per sample), weights " + (split.weight * toUs).ToString("F2", CultureInfo.InvariantCulture) + " us (" +
                                          (split.weightCalls / (double)samples).ToString("F1", CultureInfo.InvariantCulture) + " per sample, " +
                                          (split.weight * 1e9 / Stopwatch.Frequency / Math.Max(split.weightCalls, 1)).ToString("F0", CultureInfo.InvariantCulture) + " ns each), weight share " +
                                          (100 * split.weight / total).ToString("F1", CultureInfo.InvariantCulture) + " %");
                    }
                }
            }
        }
    }
}
