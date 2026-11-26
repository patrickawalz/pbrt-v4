// (not in pbrt-v4) temporalgrid_test.cs
//
// Tests of the temporally unstructured volumes (temporalgrid.cs): the compression of the functions of time, the lookup against
// the interpolation of the states it was made from, the volume made from velocities against following the velocities at every
// lookup, and the medium's transmittance at a time against the integral of its density along the ray.

using System;
using System.Diagnostics;
using testing;

namespace pbrt
{
    public static unsafe class TemporalGridTests
    {
        private static int Compressed(Float[] times, Float[] values, Float tolerance)
        {
            fixed (Float* timesPointer = times, valuesPointer = values)
            {
                return TemporalGridBuilder.Compress(timesPointer, valuesPointer, times.Length, tolerance);
            }
        }

        // The function through the first n pairs at a time
        private static Float Evaluate(Float[] times, Float[] values, int n, Float time)
        {
            if (n == 0)
            {
                return 0;
            }
            if (time <= times[0])
            {
                return values[0];
            }
            for (int i = 1; i < n; ++i)
            {
                if (time <= times[i])
                {
                    return values[i - 1] + (time - times[i - 1]) / (times[i] - times[i - 1]) * (values[i] - values[i - 1]);
                }
            }
            return values[n - 1];
        }

        // What the compression keeps: two pairs of a line, one of a constant, none of zero; the zeros about a pulse; and a
        // smooth function to within the tolerance
        [TEST("TemporalGrid", "Compression")]
        public static void Compression()
        {
            // A line, its samples given out of order
            Float[] times = { 0.5f, 0, 0.125f, 1, 0.75f, 0.25f, 0.375f, 0.875f, 0.625f };
            Float[] values = new Float[9];
            for (int i = 0; i < 9; ++i)
            {
                values[i] = 2 + 4 * times[i];
            }
            CHECK_EQ(Compressed(times, values, 1e-4f), 2);
            CHECK_EQ(times[0], 0f);
            CHECK_EQ(times[1], 1f);
            CHECK_EQ(values[0], 2f);
            CHECK_EQ(values[1], 6f);

            Float[] constantTimes = { 0, 0.3f, 0.6f, 1 }, constant = { 3, 3, 3, 3 };
            CHECK_EQ(Compressed(constantTimes, constant, 0), 1);
            Float[] zeroTimes = { 0, 0.3f, 0.6f, 1 }, zero = { 0, 0, 0, 0 };
            CHECK_EQ(Compressed(zeroTimes, zero, 0), 0);

            // A pulse between zeros: the zeros next to it stay, at any tolerance, so there is no material before and after it
            Float[] pulseTimes = { 0, 0.2f, 0.4f, 0.5f, 0.6f, 0.8f, 1 }, pulse = { 0, 0, 0, 1, 0, 0, 0 };
            int kept = Compressed(pulseTimes, pulse, 10f);
            CHECK_EQ(kept, 5);
            CHECK_EQ(Evaluate(pulseTimes, pulse, kept, 0.39f), 0f);
            CHECK_EQ(Evaluate(pulseTimes, pulse, kept, 0.61f), 0f);
            CHECK_EQ(Evaluate(pulseTimes, pulse, kept, 0.5f), 1f);

            // A smooth function of 65 samples
            const int count = 65;
            Float[] smoothTimes = new Float[count], smooth = new Float[count], originalTimes = new Float[count], original = new Float[count];
            for (int i = 0; i < count; ++i)
            {
                originalTimes[i] = smoothTimes[i] = (Float)i / (count - 1);
                original[i] = smooth[i] = 1 + MathF.Sin(5 * smoothTimes[i]) * MathF.Exp(-smoothTimes[i]);
            }
            const float tolerance = 0.01f;
            kept = Compressed(smoothTimes, smooth, tolerance);
            double worst = 0;
            for (int i = 0; i < count; ++i)
            {
                worst = Math.Max(worst, Math.Abs(Evaluate(smoothTimes, smooth, kept, originalTimes[i]) - original[i]));
            }
            Console.WriteLine("temporal grid compression: a smooth function of " + count + " samples in " + kept + " pairs, off by " + worst.ToString("e2") + " at most");
            CHECK_LT(kept, count / 3);
            CHECK_LT(worst, 4 * tolerance * 2);
        }

        // A density of a blob in the unit cube
        private static Float Blob(Float x, Float y, Float z, Float cx, Float cy, Float cz, Float radius)
        {
            Float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy) + (z - cz) * (z - cz)) / radius;
            return d < 1 ? (1 - d * d) * (1 - d * d) : 0;
        }

        // The lookup of a volume made from the states of a density at several times is the interpolation in time of the
        // lookups of the states; and with a tolerance it is that to within the tolerance, in fewer pairs
        [TEST("TemporalGrid", "StatesAreInterpolated")]
        public static void StatesAreInterpolated()
        {
            const int n = 20, nt = 6;
            Float[] density = new Float[n * n * n * nt];
            Float[] times = new Float[nt];
            SampledGrid<Float>[] states = new SampledGrid<Float>[nt];
            for (int t = 0; t < nt; ++t)
            {
                // (the times are not evenly spaced)
                times[t] = (Float)t / (nt - 1) * (Float)t / (nt - 1);
                Float[] state = new Float[n * n * n];
                for (int z = 0; z < n; ++z)
                {
                    for (int y = 0; y < n; ++y)
                    {
                        for (int x = 0; x < n; ++x)
                        {
                            // (a blob that goes along a curve, and grows)
                            Float value = Blob((x + 0.5f) / n, (y + 0.5f) / n, (z + 0.5f) / n, 0.3f + 0.4f * times[t], 0.5f + 0.2f * MathF.Sin(3 * times[t]), 0.5f,
                                               0.15f + 0.1f * times[t]);
                            state[x + n * (y + n * z)] = value;
                            density[x + n * (y + n * (z + n * t))] = value;
                        }
                    }
                }
                states[t] = new SampledGrid<Float>(state, n, n, n);
            }
            TemporalGrid exact = TemporalGridBuilder.FromStates(density, n, n, n, times, 0);
            TemporalGrid compressed = TemporalGridBuilder.FromStates(density, n, n, n, times, 0.02f);
            RNG rng = new RNG(5);
            double worstExact = 0, worstCompressed = 0;
            for (int i = 0; i < 200000; ++i)
            {
                Point3f p = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                Float time = -0.1f + 1.2f * rng.Uniform<Float>();
                Float clamped = Math.Max(0, Math.Min(1, time));
                int t = 0;
                while (t < nt - 2 && times[t + 1] <= clamped)
                {
                    ++t;
                }
                Float w = (clamped - times[t]) / (times[t + 1] - times[t]);
                Float expected = states[t].Lookup(p) + w * (states[t + 1].Lookup(p) - states[t].Lookup(p));
                worstExact = Math.Max(worstExact, Math.Abs(exact.Lookup(p, time) - expected));
                worstCompressed = Math.Max(worstCompressed, Math.Abs(compressed.Lookup(p, time) - expected));
            }
            Console.WriteLine("temporal grid from " + nt + " states of " + n + "^3: " + exact.SampleCount() + " pairs in " + exact.BlockCount() + " blocks (of " +
                              (long)n * n * n * nt + " values), off by " + worstExact.ToString("e2") + "; with a tolerance of 0.02 " + compressed.SampleCount() +
                              " pairs, off by " + worstCompressed.ToString("e2"));
            CHECK_LT(worstExact, 2e-6);
            CHECK_LT(worstCompressed, 0.05);
            CHECK_LT(exact.SampleCount(), (long)n * n * n * nt);
            CHECK_LE(compressed.SampleCount(), exact.SampleCount());
        }

        // A blob and a field of velocities that turns it about the middle of the cube: the grids of a medium of n^3
        private static void Turning(int n, Float turn, out Float[] density, out Float[] velocity)
        {
            density = new Float[n * n * n];
            velocity = new Float[3 * n * n * n];
            for (int z = 0; z < n; ++z)
            {
                for (int y = 0; y < n; ++y)
                {
                    for (int x = 0; x < n; ++x)
                    {
                        Float px = (x + 0.5f) / n, py = (y + 0.5f) / n, pz = (z + 0.5f) / n;
                        int i = x + n * (y + n * z);
                        density[i] = Blob(px, py, pz, 0.72f, 0.5f, 0.5f, 0.16f);
                        // (about the z axis through the middle: `turn` radians a unit of time)
                        velocity[3 * i] = -turn * (py - 0.5f);
                        velocity[3 * i + 1] = turn * (px - 0.5f);
                        velocity[3 * i + 2] = 0;
                    }
                }
            }
        }

        private static TemporalGridMedium TurningMedium(int n, Float turn, int timeSamples, int steps, bool eulerian)
        {
            Turning(n, turn, out Float[] density, out Float[] velocity);
            Float[] vx = new Float[n * n * n], vy = new Float[n * n * n], vz = new Float[n * n * n];
            Float maxSpeed = 0;
            for (int i = 0; i < n * n * n; ++i)
            {
                vx[i] = velocity[3 * i];
                vy[i] = velocity[3 * i + 1];
                vz[i] = velocity[3 * i + 2];
                maxSpeed = Math.Max(maxSpeed, Math.Max(Math.Abs(vx[i]), Math.Abs(vy[i])));
            }
            SampledGrid<Float> densityGrid = new SampledGrid<Float>(density, n, n, n);
            SampledGrid<Float> velocityX = new SampledGrid<Float>(vx, n, n, n), velocityY = new SampledGrid<Float>(vy, n, n, n);
            SampledGrid<Float> velocityZ = new SampledGrid<Float>(vz, n, n, n);
            Bounds3f bounds = new Bounds3f(new Point3f(0, 0, 0), new Point3f(1, 1, 1));
            if (eulerian)
            {
                return new TemporalGridMedium(bounds, new Transform(), new ConstantSpectrum(1f), new ConstantSpectrum(1f), 8, 0, null, densityGrid, velocityX,
                                              velocityY, velocityZ, maxSpeed, 0, 0, 1, steps, null);
            }
            TemporalGrid grid = TemporalGridBuilder.FromVelocities(densityGrid, n, n, n, velocityX, velocityY, velocityZ, 0, 0, 1, timeSamples, 1, steps, 0.005f);
            return new TemporalGridMedium(bounds, new Transform(), new ConstantSpectrum(1f), new ConstantSpectrum(1f), 8, 0, grid, null, null, null, null, 0, 0, 0, 1,
                                          steps, null);
        }

        // The volume made from a density and velocities is the density followed back along the velocities: the more samples
        // of time a voxel has, the nearer; and a lookup of the volume costs less than following the velocities
        [TEST("TemporalGrid", "VelocitiesAreFollowed")]
        public static void VelocitiesAreFollowed()
        {
            const int n = 48, steps = 8;
            const float turn = 1.6f;
            TemporalGridMedium reference = TurningMedium(n, turn, 1, steps, true);
            double previous = double.MaxValue;
            foreach (int timeSamples in new int[] { 2, 8, 32 })
            {
                TemporalGridMedium medium = TurningMedium(n, turn, timeSamples, steps, false);
                RNG rng = new RNG(9);
                double difference = 0, total = 0;
                const int count = 300000;
                Point3f[] points = new Point3f[count];
                Float[] times = new Float[count];
                for (int i = 0; i < count; ++i)
                {
                    points[i] = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                    times[i] = rng.Uniform<Float>();
                    Float expected = reference.Density(points[i], times[i]);
                    difference += Math.Abs(medium.Density(points[i], times[i]) - expected);
                    total += expected;
                }
                // (the time of a lookup, once the code is compiled in full)
                double volumeTime = double.MaxValue, followedTime = double.MaxValue;
                Float sink = 0;
                for (int pass = 0; pass < 6; ++pass)
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    for (int i = 0; i < count; ++i)
                    {
                        sink += medium.Density(points[i], times[i]);
                    }
                    volumeTime = Math.Min(volumeTime, watch.Elapsed.TotalMilliseconds * 1e6 / count);
                    watch.Restart();
                    for (int i = 0; i < count; ++i)
                    {
                        sink += reference.Density(points[i], times[i]);
                    }
                    followedTime = Math.Min(followedTime, watch.Elapsed.TotalMilliseconds * 1e6 / count);
                }
                double relative = difference / total;
                Console.WriteLine("temporal grid of " + n + "^3 from velocities, " + timeSamples + " samples of time a voxel: " + medium.Grid.SampleCount() + " pairs (" +
                                  ((double)medium.Grid.SampleCount() / ((long)n * n * n)).ToString("f2") + " a voxel), " + medium.Grid.BlockCount() + " blocks, " +
                                  (medium.Grid.BytesAllocated() / 1024) + " KiB; the mean difference from following the velocities (" + steps + " steps) is " +
                                  (100 * relative).ToString("f2") + "% of the mean density; a lookup " + volumeTime.ToString("f0") + " ns against " +
                                  followedTime.ToString("f0") + " ns" + (sink < 0 ? " " : ""));
                CHECK_LT(relative, previous);
                previous = relative;
            }
            CHECK_LT(previous, 0.03);
        }

        // The medium at a time: the fraction of the walks of delta tracking that go through is the transmittance, the
        // exponential of the integral of the density at that time along the ray; at another time it is another
        [TEST("TemporalGrid", "TransmittanceAtATime")]
        public static void TransmittanceAtATime()
        {
            TemporalGridMedium medium = TurningMedium(32, 1.6f, 16, 8, false);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            Point3f from = new Point3f(0.5f, -0.2f, 0.5f), to = new Point3f(0.75f, 1.2f, 0.5f);
            double[] expected = new double[2];
            for (int k = 0; k < 2; ++k)
            {
                Float time = k == 0 ? 0.05f : 0.95f;
                const int quadrature = 20000;
                double depth = 0;
                for (int i = 0; i < quadrature; ++i)
                {
                    MediumProperties mp = medium.SamplePoint(Lerp((Float)((i + 0.5) / quadrature), from, to), lambda, time);
                    depth += (mp.sigma_a[0] + mp.sigma_s[0]) * Distance(from, to) / quadrature;
                }
                expected[k] = Math.Exp(-depth);
                Ray ray = new Ray(from, to - from, time);
                RNG rng = new RNG(13);
                const int walks = 400000;
                int through = 0;
                for (int i = 0; i < walks; ++i)
                {
                    bool collided = false;
                    SampleT_maj(ray, medium, 1f, rng.Uniform<Float>(), ref rng, lambda,
                        (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T) =>
                        {
                            EXPECT_LE(mp.sigma_a[0] + mp.sigma_s[0], sigma_maj[0] * 1.0001f);
                            if (rng.Uniform<Float>() * sigma_maj[0] < mp.sigma_a[0] + mp.sigma_s[0])
                            {
                                collided = true;
                                return false;
                            }
                            return true;
                        });
                    through += collided ? 0 : 1;
                }
                double estimate = (double)through / walks;
                Console.WriteLine("temporal grid medium at the time " + time + ": transmittance " + estimate.ToString("f4") + " by delta tracking, " +
                                  expected[k].ToString("f4") + " by the integral of the density");
                CHECK_LT(Math.Abs(estimate - expected[k]), 4 * Math.Sqrt(expected[k] * (1 - expected[k]) / walks) + 1e-4);
            }
            // (the blob has turned out of the ray, or into it)
            CHECK_GT(Math.Abs(expected[0] - expected[1]), 0.05);
        }
    }
}
