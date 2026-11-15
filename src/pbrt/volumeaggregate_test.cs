// (not in pbrt-v4) Tests of the volume aggregate (volumeaggregate.cs; Fong et al., "Production Volume Rendering", section 6).

using System;
using testing;

namespace pbrt
{
    public static unsafe class VolumeAggregateTests
    {
        // The services of the renderer for a ray that ends on a wall at a fixed distance
        sealed class Wall : VolumeServices
        {
            public Float distance;

            public Wall(ulong seed, Float distance)
            {
                rng = new RNG(seed);
                this.distance = distance;
            }

            public override bool GetNearestHit(Ray ray, out Point3f P, out Float distance, out VolumeGeometry g)
            {
                g = new VolumeGeometry();
                g.isSurface = true;
                distance = this.distance;
                P = ray.Apply(distance);
                return true;
            }

            public override Float GenerateRandomNumber() { return rng.Uniform<Float>(); }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, out Vector3f wi, out SampledSpectrum L,
                                                     out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                wi = new Vector3f(0, 0, 1);
                L = new SampledSpectrum(0f);
                beamTransmittance = new SampledSpectrum(1f);
                beamR = new SampledSpectrum(1f);
                return false;
            }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L)
            {
                wi = new Vector3f(0, 0, 1);
                L = new SampledSpectrum(0f);
                return false;
            }
        }

        // A grid of densities over a box: a ball of density that falls off from its centre, with some variation
        static GridMedium Ball(Point3f lo, Point3f hi, RGB absorption, RGB scattering, Float scale, int n, Float phase)
        {
            Float[] density = new Float[n * n * n];
            for (int z = 0; z < n; ++z)
            {
                for (int y = 0; y < n; ++y)
                {
                    for (int x = 0; x < n; ++x)
                    {
                        Float fx = (x + 0.5f) / n - 0.5f, fy = (y + 0.5f) / n - 0.5f, fz = (z + 0.5f) / n - 0.5f;
                        Float radius = std.sqrt(fx * fx + fy * fy + fz * fz);
                        Float value = std.max<Float>(0, 1 - 2.2f * radius) * (0.75f + 0.25f * std.sin(phase + 19 * fx + 13 * fy * fz));
                        density[x + n * (y + n * z)] = value;
                    }
                }
            }
            Allocator alloc = new Allocator();
            GridMedium medium = new GridMedium(new Bounds3f(lo, hi), new Transform(), new RGBUnboundedSpectrum(RGBColorSpace.sRGB, absorption),
                                               new RGBUnboundedSpectrum(RGBColorSpace.sRGB, scattering), scale, 0, new SampledGrid<Float>(density, n, n, n, alloc),
                                               default, 1, 0, new ConstantSpectrum(0f), new SampledGrid<Float>(new Float[] { 0 }, 1, 1, 1, alloc), alloc);
            return medium;
        }

        static AggregateMedium Three(out GridMedium[] volumes)
        {
            volumes = new GridMedium[] {
                Ball(new Point3f(0, 0, 0), new Point3f(1, 1, 1), new RGB(0.2f, 0.4f, 1f), new RGB(1f, 2f, 3f), 3, 24, 0.3f),
                Ball(new Point3f(0.5f, 0.2f, 0.1f), new Point3f(1.7f, 1.4f, 1.3f), new RGB(0.5f, 0.2f, 0.1f), new RGB(4f, 3f, 1f), 2, 20, 1.7f),
                // a small one far from the others (section 6.11)
                Ball(new Point3f(3.0f, 0.4f, 0.4f), new Point3f(3.2f, 0.6f, 0.6f), new RGB(0.1f, 0.1f, 0.1f), new RGB(2f, 2f, 2f), 5, 8, 2.9f) };
            return new AggregateMedium(new Medium[] { volumes[0], volumes[1], volumes[2] }, 2, 6, null);
        }

        // The octree: the intervals of a ray follow one another without a gap from where the ray enters the region to where it
        // leaves; the majorant of an interval is at least the extinction anywhere in it, for every wavelength; where no
        // volume is, the interval has none.
        [TEST("VolumeAggregate", "IntervalsAndMajorants")]
        public static void IntervalsAndMajorants()
        {
            GridMedium[] volumes;
            AggregateMedium medium = Three(out volumes);
            VolumeAggregate aggregate = medium.Aggregate();
            Console.WriteLine("MEASURE volume aggregate of three volumes: {0}", medium);
            EXPECT_EQ(3, aggregate.MemberCount());
            EXPECT_GT(aggregate.LeafCount(), 8);
            RNG rng = new RNG(101);
            int intervals = 0, empty = 0, checkedPoints = 0, above = 0;
            double worstGap = 0, tightness = 0;
            int tight = 0;
            for (int i = 0; i < 4000; ++i)
            {
                Point3f o = new Point3f(-1 + 5 * rng.Uniform<Float>(), -1 + 3 * rng.Uniform<Float>(), -1 + 3 * rng.Uniform<Float>());
                Vector3f d = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                VolumeAggregate.Iterator interval = aggregate.GetIterator(o, d, 100);
                Float previous = -1;
                while (interval.Next())
                {
                    ++intervals;
                    EXPECT_GT(interval.End(), interval.Start());
                    if (previous >= 0)
                    {
                        worstGap = Math.Max(worstGap, Math.Abs(interval.Start() - previous));
                    }
                    previous = interval.End();
                    int[] held = interval.Volumes();
                    if (held.Length == 0)
                    {
                        ++empty;
                    }
                    SampledSpectrum majorant = new SampledSpectrum(0f);
                    for (int k = 0; k < held.Length; ++k)
                    {
                        majorant += aggregate.Member(held[k]).medium.AggregateExtinction(lambda) * interval.MaxDensity(k);
                    }
                    for (int s = 0; s < 4; ++s)
                    {
                        Point3f p = o + Lerp(rng.Uniform<Float>(), interval.Start(), interval.End()) * d;
                        // (every volume is asked, in the node or not: a volume that the node does not list has nothing there)
                        SampledSpectrum actual = new SampledSpectrum(0f);
                        for (int m = 0; m < 3; ++m)
                        {
                            if (aggregate.Member(m).Inside(p))
                            {
                                MediumProperties mp = volumes[m].SamplePoint(p, lambda);
                                actual += mp.sigma_a + mp.sigma_s;
                            }
                        }
                        ++checkedPoints;
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            if (actual[c] > majorant[c] * 1.0001f + 1e-6f)
                            {
                                ++above;
                            }
                        }
                        if (majorant[0] > 0)
                        {
                            tightness += actual[0] / majorant[0];
                            ++tight;
                        }
                    }
                }
            }
            Console.WriteLine("MEASURE volume aggregate: {0} intervals on 4000 rays ({1} empty), largest gap between two {2:E1}; {3} points, {4} with an extinction above " +
                              "the majorant; the extinction is {5:F2} of the majorant in the mean", intervals, empty, worstGap, checkedPoints, above, tightness / tight);
            EXPECT_EQ(0, above);
            EXPECT_LT(worstGap, 1e-5);
            EXPECT_GT(empty, 0);
        }

        // The transmittance of the aggregate between two points, against the quadrature of the summed extinction
        [TEST("VolumeAggregate", "Transmittance")]
        public static void Transmittance()
        {
            GridMedium[] volumes;
            AggregateMedium medium = Three(out volumes);
            RNG rng = new RNG(103);
            Point3f[] from = new Point3f[] { new Point3f(-0.5f, 0.5f, 0.5f), new Point3f(0.2f, -0.4f, 0.3f), new Point3f(2.5f, 0.5f, 0.5f) };
            Point3f[] to = new Point3f[] { new Point3f(2.2f, 0.7f, 0.6f), new Point3f(1.4f, 1.6f, 0.9f), new Point3f(3.6f, 0.5f, 0.5f) };
            for (int pair = 0; pair < 3; ++pair)
            {
                double estimate = 0, squares = 0, exact = 0;
                int count = 40000;
                Wall rs = new Wall(7 + (ulong)pair, 1);
                for (int s = 0; s < count; ++s)
                {
                    VolumeContext ctx = new VolumeContext();
                    ctx.lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                    ctx.P = from[pair];
                    SampledSpectrum T = Volume.Create(ctx, medium).Transmittance(rs, from[pair], to[pair]);
                    // (the estimate of each wavelength is T over the mean of the densities, as the path tracer uses it)
                    double mean = (T / ctx.r.Average()).Average();
                    estimate += mean;
                    squares += mean * mean;
                    if (s < 400)
                    {
                        // the quadrature, for these wavelengths
                        int steps = 2000;
                        SampledSpectrum depth = new SampledSpectrum(0f);
                        Vector3f along = to[pair] - from[pair];
                        for (int k = 0; k < steps; ++k)
                        {
                            Point3f p = from[pair] + ((k + 0.5f) / steps) * along;
                            for (int m = 0; m < 3; ++m)
                            {
                                if (medium.Aggregate().Member(m).Inside(p))
                                {
                                    MediumProperties mp = volumes[m].SamplePoint(p, ctx.lambda);
                                    depth += (mp.sigma_a + mp.sigma_s) * (Length(along) / steps);
                                }
                            }
                        }
                        exact += Exp(-depth).Average() / 400;
                    }
                }
                estimate /= count;
                double error = Math.Sqrt(Math.Max(0, squares / count - estimate * estimate) / count);
                Console.WriteLine("MEASURE volume aggregate, transmittance between two points ({0}): {1:F5} +- {2:F5}, quadrature {3:F5}", pair, estimate, error, exact);
                // (the quadrature is the mean over 400 draws of the wavelengths: its own error is that of the colour)
                EXPECT_LT(Math.Abs(estimate - exact), 4 * error + 0.01);
            }
        }

        // The aggregate of one volume integrates as the volume does by itself (MultiScatterHeterogeneousVolume): how often a
        // ray is scattered before the wall, with what weight, and with what weight it reaches the wall
        [TEST("VolumeAggregate", "OneVolumeAsItself")]
        public static void OneVolumeAsItself()
        {
            GridMedium alone = Ball(new Point3f(0, 0, 0), new Point3f(1, 1, 1), new RGB(0.2f, 0.4f, 1f), new RGB(1f, 2f, 3f), 3, 24, 0.3f);
            AggregateMedium medium = new AggregateMedium(new Medium[] { alone }, 2, 6, null);
            RNG rng = new RNG(107);
            double[] scattered = new double[2], through = new double[2], scatteredSquares = new double[2], throughSquares = new double[2], depth = new double[2];
            int count = 200000;
            Point3f origin = new Point3f(-0.4f, 0.45f, 0.55f);
            Vector3f d = Normalize(new Vector3f(1, 0.05f, -0.03f));
            for (int which = 0; which < 2; ++which)
            {
                Wall rs = new Wall(11 + (ulong)which, 2);
                for (int s = 0; s < count; ++s)
                {
                    VolumeContext ctx = new VolumeContext();
                    ctx.lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                    ctx.P = origin;
                    SampledSpectrum L, T, w;
                    Point3f P;
                    Ray wo;
                    VolumeGeometry g;
                    Volume volume = which == 0 ? Volume.Create(ctx, medium) : new MultiScatterHeterogeneousVolume(ctx, alone);
                    bool went = volume.Integrate(rs, new Ray(origin, d, 0), out L, out T, out w, out P, out wo, out g);
                    if (!went)
                    {
                        continue;
                    }
                    double value = (w * T / ctx.r.Average()).Average();
                    if (g.isSurface)
                    {
                        through[which] += value;
                        throughSquares[which] += value * value;
                    }
                    else
                    {
                        scattered[which] += value;
                        scatteredSquares[which] += value * value;
                        depth[which] += value * Distance(origin, P);
                    }
                }
            }
            double es = Math.Sqrt((scatteredSquares[0] + scatteredSquares[1]) / count) / Math.Sqrt(count), et = Math.Sqrt((throughSquares[0] + throughSquares[1]) / count) / Math.Sqrt(count);
            Console.WriteLine("MEASURE volume aggregate of one volume against the volume by itself: scattered {0:F5} / {1:F5} (+- {2:F5}), at the mean distance {3:F4} / {4:F4}; " +
                              "to the wall {5:F5} / {6:F5} (+- {7:F5})", scattered[0] / count, scattered[1] / count, es, depth[0] / scattered[0], depth[1] / scattered[1],
                              through[0] / count, through[1] / count, et);
            EXPECT_LT(Math.Abs(scattered[0] - scattered[1]) / count, 4 * es);
            EXPECT_LT(Math.Abs(through[0] - through[1]) / count, 4 * et);
            EXPECT_LT(Math.Abs(depth[0] / scattered[0] - depth[1] / scattered[1]), 0.01);
        }

        // Two volumes that overlap: what the aggregate scatters and lets through, against the sum of the two as the other
        // integrators see it (the CompositeMedium of bounded members, by the volume of pbrt's majorants)
        [TEST("VolumeAggregate", "OverlapAsTheSum")]
        public static void OverlapAsTheSum()
        {
            GridMedium[] volumes;
            AggregateMedium medium = Three(out volumes);
            RNG rng = new RNG(109);
            double[] scattered = new double[2], through = new double[2], scatteredSquares = new double[2], throughSquares = new double[2];
            int count = 200000;
            Point3f origin = new Point3f(-0.4f, 0.6f, 0.55f);
            Vector3f d = Normalize(new Vector3f(1, 0.08f, 0.04f));
            for (int which = 0; which < 2; ++which)
            {
                Wall rs = new Wall(13 + (ulong)which, 4.5f);
                for (int s = 0; s < count; ++s)
                {
                    VolumeContext ctx = new VolumeContext();
                    ctx.lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                    ctx.P = origin;
                    SampledSpectrum L, T, w;
                    Point3f P;
                    Ray wo;
                    VolumeGeometry g;
                    // (the second: the medium as a plain heterogeneous medium, with the tracker and the majorants of its members)
                    Volume volume = which == 0 ? Volume.Create(ctx, medium) : new MultiScatterHeterogeneousVolume(ctx, medium);
                    if (!volume.Integrate(rs, new Ray(origin, d, 0), out L, out T, out w, out P, out wo, out g))
                    {
                        continue;
                    }
                    double value = (w * T / ctx.r.Average()).Average();
                    if (g.isSurface)
                    {
                        through[which] += value;
                        throughSquares[which] += value * value;
                    }
                    else
                    {
                        scattered[which] += value;
                        scatteredSquares[which] += value * value;
                    }
                }
            }
            double es = Math.Sqrt((scatteredSquares[0] + scatteredSquares[1]) / count) / Math.Sqrt(count), et = Math.Sqrt((throughSquares[0] + throughSquares[1]) / count) / Math.Sqrt(count);
            Console.WriteLine("MEASURE volume aggregate of three volumes against their sum tracked with pbrt's majorants: scattered {0:F5} / {1:F5} (+- {2:F5}); " +
                              "to the wall {3:F5} / {4:F5} (+- {5:F5})", scattered[0] / count, scattered[1] / count, es, through[0] / count, through[1] / count, et);
            EXPECT_LT(Math.Abs(scattered[0] - scattered[1]) / count, 4 * es);
            EXPECT_LT(Math.Abs(through[0] - through[1]) / count, 4 * et);
        }
    }
}
