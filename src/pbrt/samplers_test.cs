// samplers_test.cpp

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class SamplersTests
    {
        // Make sure all samplers give the same sample values if we go back to the
        // same pixel / sample index.
        [TEST("Sampler", "ConsistentValues")]
        public static void Sampler_ConsistentValues()
        {
            const int rootSpp = 4;
            const int spp = rootSpp * rootSpp;
            Point2i resolution = new Point2i(100, 101);

            List<Sampler> samplers = new List<Sampler>();
            samplers.Add(new HaltonSampler(spp, resolution));
            samplers.Add(new IndependentSampler(spp));
            samplers.Add(new PaddedSobolSampler(spp, RandomizeStrategy.None));
            samplers.Add(new PaddedSobolSampler(spp, RandomizeStrategy.PermuteDigits));
            samplers.Add(new PaddedSobolSampler(spp, RandomizeStrategy.FastOwen));
            samplers.Add(new PaddedSobolSampler(spp, RandomizeStrategy.Owen));
            samplers.Add(new ZSobolSampler(spp, resolution, RandomizeStrategy.None));
            samplers.Add(new ZSobolSampler(spp, resolution, RandomizeStrategy.PermuteDigits));
            samplers.Add(new ZSobolSampler(spp, resolution, RandomizeStrategy.FastOwen));
            samplers.Add(new ZSobolSampler(spp, resolution, RandomizeStrategy.Owen));
            samplers.Add(new PMJ02BNSampler(spp));
            samplers.Add(new StratifiedSampler(rootSpp, rootSpp, true));
            samplers.Add(new SobolSampler(spp, resolution, RandomizeStrategy.None));
            samplers.Add(new SobolSampler(spp, resolution, RandomizeStrategy.PermuteDigits));
            samplers.Add(new SobolSampler(spp, resolution, RandomizeStrategy.Owen));
            samplers.Add(new SobolSampler(spp, resolution, RandomizeStrategy.FastOwen));

            foreach (Sampler sampler in samplers)
            {
                List<Float>[] s1d = new List<Float>[spp];
                List<Point2f>[] s2d = new List<Point2f>[spp];
                for (int i = 0; i < spp; ++i)
                {
                    s1d[i] = new List<Float>();
                    s2d[i] = new List<Point2f>();
                }

                for (int s = 0; s < spp; ++s)
                {
                    sampler.StartPixelSample(new Point2i(1, 5), s);
                    for (int i = 0; i < 10; ++i)
                    {
                        s2d[s].Add(sampler.Get2D());
                        s1d[s].Add(sampler.Get1D());
                    }
                }

                // Go somewhere else and generate some samples, just to make sure
                // things are shaken up.
                sampler.StartPixelSample(new Point2i(0, 6), 10);
                sampler.Get2D();
                sampler.Get2D();
                sampler.Get1D();

                // Now go back and generate samples again, but enumerate them in a
                // different order to make sure the sampler is doing the right
                // thing.
                for (int s = spp - 1; s >= 0; --s)
                {
                    sampler.StartPixelSample(new Point2i(1, 5), s);
                    for (int i = 0; i < s2d[s].Count; ++i)
                    {
                        EXPECT_EQ(s2d[s][i], sampler.Get2D());
                        EXPECT_EQ(s1d[s][i], sampler.Get1D());
                    }
                }
            }
        }

        private static void checkElementary(string name, List<Point2f> samples, int logSamples)
        {
            for (int i = 0; i <= logSamples; ++i)
            {
                // Check one set of elementary intervals: number of intervals
                // in each dimension.
                int nx = 1 << i, ny = 1 << (logSamples - i);

                int[] count = new int[1 << logSamples];
                foreach (Point2f s in samples)
                {
                    // Map the sample to an interval
                    Float x = nx * s.x, y = ny * s.y;
                    EXPECT_GE(x, 0);
                    EXPECT_LT(x, nx);
                    EXPECT_GE(y, 0);
                    EXPECT_LT(y, ny);
                    int index = (int)std.floor(y) * nx + (int)std.floor(x);
                    EXPECT_GE(index, 0);
                    EXPECT_LT(index, count.Length);
                    if (index < 0 || index >= count.Length)
                        continue;

                    // This should be the first time a sample has landed in its
                    // interval.
                    EXPECT_EQ(0, count[index], "Sampler " + name + " with interval " + nx + " x " + ny);
                    ++count[index];
                }
            }
        }

        private static void checkElementarySampler(string name, Sampler sampler, int logSamples, int res = 1)
        {
            // Get all of the samples for a pixel.
            int spp = sampler.SamplesPerPixel();
            List<Point2f> samples = new List<Point2f>();
            foreach (Point2i p in new Bounds2i(new Point2i(0, 0), new Point2i(res, res)))
            {
                samples.Clear();
                for (int i = 0; i < spp; ++i)
                {
                    sampler.StartPixelSample(p, i);
                    samples.Add(sampler.GetPixel2D());
                }

                checkElementary(name, samples, logSamples);
            }
        }

        // TODO: check Halton (where the elementary intervals are (2^i, 3^j)).

        [TEST("PaddedSobolSampler", "ElementaryIntervals")]
        public static void PaddedSobolSampler_ElementaryIntervals()
        {
            foreach (RandomizeStrategy rand in new RandomizeStrategy[] { RandomizeStrategy.None, RandomizeStrategy.PermuteDigits })
                for (int logSamples = 2; logSamples <= 10; ++logSamples)
                    checkElementarySampler("PaddedSobolSampler", new PaddedSobolSampler(1 << logSamples, rand), logSamples);
        }

        [TEST("ZSobolSampler", "ElementaryIntervals")]
        public static void ZSobolSampler_ElementaryIntervals()
        {
            foreach (int seed in new int[] { 0, 1, 5, 6, 10, 15 })
                foreach (RandomizeStrategy rand in new RandomizeStrategy[] { RandomizeStrategy.None, RandomizeStrategy.PermuteDigits })
                    for (int logSamples = 2; logSamples <= 8; ++logSamples)
                        checkElementarySampler(StringPrintf("ZSobolSampler - %s - %d", Globals.ToString(rand), seed),
                                               new ZSobolSampler(1 << logSamples, new Point2i(10, 10), rand, seed),
                                               logSamples, 10);
        }

        [TEST("SobolUnscrambledSampler", "ElementaryIntervals")]
        public static void SobolUnscrambledSampler_ElementaryIntervals()
        {
            for (int logSamples = 2; logSamples <= 10; ++logSamples)
                checkElementarySampler("Sobol Unscrambled",
                                       new SobolSampler(1 << logSamples, new Point2i(1, 1), RandomizeStrategy.None),
                                       logSamples);
        }

        [TEST("SobolXORScrambledSampler", "ElementaryIntervals")]
        public static void SobolXORScrambledSampler_ElementaryIntervals()
        {
            for (int logSamples = 2; logSamples <= 10; ++logSamples)
                checkElementarySampler("Sobol XOR Scrambled",
                                       new SobolSampler(1 << logSamples, new Point2i(1, 1), RandomizeStrategy.PermuteDigits),
                                       logSamples);
        }

        [TEST("SobolOwenScrambledSampler", "ElementaryIntervals")]
        public static void SobolOwenScrambledSampler_ElementaryIntervals()
        {
            for (int logSamples = 2; logSamples <= 10; ++logSamples)
                checkElementarySampler("Sobol Owen Scrambled",
                                       new SobolSampler(1 << logSamples, new Point2i(1, 1), RandomizeStrategy.Owen),
                                       logSamples);
        }

        [TEST("PMJ02BNSampler", "ElementaryIntervals")]
        public static void PMJ02BNSampler_ElementaryIntervals()
        {
            for (int logSamples = 2; logSamples <= 10; logSamples += 2)
                checkElementarySampler("PMJ02BNSampler", new PMJ02BNSampler(1 << logSamples), logSamples);
        }

        [TEST("ZSobolSampler", "ValidIndices")]
        public static void ZSobolSampler_ValidIndices()
        {
            Point2i res = new Point2i(16, 9);
            for (int logSamples = 0; logSamples <= 10; ++logSamples)
            {
                int spp = 1 << logSamples;
                ZSobolSampler sampler = new ZSobolSampler(spp, res, RandomizeStrategy.PermuteDigits);

                for (int dim = 0; dim < 7; dim += 3)
                {
                    HashSet<uint64_t> returnedIndices = new HashSet<uint64_t>();
                    foreach (Point2i p in new Bounds2i(new Point2i(0, 0), res))
                    {
                        uint64_t pow2Base = 0;
                        for (int i = 0; i < spp; ++i)
                        {
                            sampler.StartPixelSample(p, i, dim);
                            uint64_t index = sampler.GetSampleIndex();

                            // Make sure no index is repeated across multiple pixels
                            EXPECT_TRUE(!returnedIndices.Contains(index));
                            returnedIndices.Add(index);

                            // Make sure that all samples for this pixel are within the
                            // same pow2 aligned and sized range of the sample indices.
                            if (i == 0)
                                pow2Base = index / (uint64_t)spp;
                            else
                                EXPECT_EQ(index / (uint64_t)spp, pow2Base);
                        }
                    }
                }
            }
        }
    }
}
