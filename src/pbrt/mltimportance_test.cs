// (not in pbrt-v4) mltimportance_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class MLTImportanceTests
    {
        // Fills a 16 by 16 film with four groups: the pixels of a square have the estimates noisy, the others quiet
        static void FillNoise(MLTNoiseEstimate noise, Float[] noisy, Float[] quiet)
        {
            for (int y = 0; y < 16; ++y)
            {
                for (int x = 0; x < 16; ++x)
                {
                    bool inside = x >= 4 && x < 8 && y >= 8 && y < 12;
                    for (int k = 0; k < 4; ++k)
                    {
                        // The estimate of a group is four times its sum, over one mutation per pixel
                        noise.Add(x, y, k, 25, (inside ? noisy[k] : quiet[k]) / 4);
                    }
                }
            }
        }

        // The weights of the next stage are larger where the groups disagree, stay in the range, and are looked up at the pixel that a raster
        // position is in, from the corner of the film
        [TEST("MLTImportance", "NoiseMakesNoisyPixelsHeavier")]
        public static void MLTImportance_NoiseMakesNoisyPixelsHeavier()
        {
            Bounds2i bounds = new Bounds2i(new Point2i(10, 20), new Point2i(26, 36));
            MLTImportanceMap map = new MLTImportanceMap(bounds, 8);
            MLTNoiseEstimate noise = new MLTNoiseEstimate(map, 4);
            FillNoise(noise, new Float[] { 1.5f, 0.5f, 1.5f, 0.6f }, new Float[] { 1.0f, 1.0f, 1.0f, 1.001f });
            map.UpdateFromStage(noise, 1, 1, 0.05f, 0, true, 0.75f);
            map.Select(MLTStageKind.Noise);
            Float noisyWeight = map.Weight(new Point2f(10 + 5.5f, 20 + 9.5f));
            Float quietWeight = map.Weight(new Point2f(10 + 12.5f, 20 + 2.5f));
            EXPECT_GT(noisyWeight, 4 * quietWeight);
            EXPECT_LE(noisyWeight, 8.0f + 1e-5f);
            EXPECT_GE(quietWeight, 1.0f / 8 - 1e-5f);
            // Positions outside the film use the nearest pixel
            EXPECT_EQ(map.Weight(new Point2f(-100, -100)), map.Weight(new Point2f(10.5f, 20.5f)));
            // After more stages with the same noise, with the filter, the weights stay in the range
            for (int stage = 0; stage < 4; ++stage)
            {
                map.UpdateFromStage(noise, 1, 2, 0.05f, 2, true, 0.75f);
            map.Select(MLTStageKind.Noise);
            }
            for (int y = 20; y < 36; ++y)
            {
                for (int x = 10; x < 26; ++x)
                {
                    Float w = map.Weight(new Point2f(x + 0.5f, y + 0.5f));
                    EXPECT_GE(w, 1.0f / 8 - 1e-5f);
                    EXPECT_LE(w, 8.0f + 1e-5f);
                }
            }
        }

        // One group with a firefly does not make a pixel heavy with the robust variance, and does without it
        [TEST("MLTImportance", "RobustVarianceIgnoresOneFirefly")]
        public static void MLTImportance_RobustVarianceIgnoresOneFirefly()
        {
            Bounds2i bounds = new Bounds2i(new Point2i(0, 0), new Point2i(16, 16));
            Float[] firefly = { 1, 1, 1, 50 };
            Float[] quiet = { 1, 1, 1, 1.0f };
            MLTImportanceMap robustMap = new MLTImportanceMap(bounds, 64);
            MLTNoiseEstimate robustNoise = new MLTNoiseEstimate(robustMap, 4);
            FillNoise(robustNoise, firefly, quiet);
            robustMap.UpdateFromStage(robustNoise, 1, 1, 0.05f, 0, true, 0.75f);
            robustMap.Select(MLTStageKind.Noise);
            MLTImportanceMap plainMap = new MLTImportanceMap(bounds, 64);
            MLTNoiseEstimate plainNoise = new MLTNoiseEstimate(plainMap, 4);
            FillNoise(plainNoise, firefly, quiet);
            plainMap.UpdateFromStage(plainNoise, 1, 1, 0.05f, 0, false, 0.75f);
            plainMap.Select(MLTStageKind.Noise);
            Point2f inside = new Point2f(5.5f, 9.5f), outside = new Point2f(12.5f, 2.5f);
            EXPECT_LT(Math.Abs(robustMap.Weight(inside) / robustMap.Weight(outside) - 1), 0.01f);
            EXPECT_GT(plainMap.Weight(inside), 10 * plainMap.Weight(outside));
        }

        // The chain with the importance c w and the splats f / (c w) times the integral b of the importance give the same image for every positive weight: a Metropolis chain over
        // pixels (the proposals are uniform), with the formulas of MLTIntegrator
        [TEST("MLTImportance", "ImageDoesNotDependOnWeights")]
        public static void MLTImportance_ImageDoesNotDependOnWeights()
        {
            Float[] f = { 0.1f, 2.0f, 0.5f, 8.0f, 0.0f, 1.0f, 3.0f, 0.25f };
            int pixels = f.Length;
            Float[][] weightSets =
            {
                new Float[] { 1, 1, 1, 1, 1, 1, 1, 1 },
                new Float[] { 4, 0.5f, 2, 0.125f, 1, 1, 0.25f, 8 },
                new Float[] { 0.2f, 3, 3, 0.2f, 1, 3, 0.2f, 3 },
            };
            foreach (Float[] w in weightSets)
            {
                double b = 0;
                for (int j = 0; j < pixels; ++j)
                {
                    b += f[j] * w[j];
                }
                b /= pixels;
                double[] image = new double[pixels];
                RNG rng = new RNG(5);
                int current = 3;
                int n = 3000000;
                for (int i = 0; i < n; ++i)
                {
                    int proposed = (int)(rng.Uniform<Float>() * pixels) % pixels;
                    Float iCurrent = f[current] * w[current], iProposed = f[proposed] * w[proposed];
                    Float accept = Math.Min(1, iProposed / iCurrent);
                    if (accept > 0)
                    {
                        image[proposed] += accept * b / w[proposed];
                    }
                    image[current] += (1 - accept) * b / w[current];
                    if (rng.Uniform<Float>() < accept)
                    {
                        current = proposed;
                    }
                }
                for (int j = 0; j < pixels; ++j)
                {
                    // splats hold b f / I = b / w per visit; pixels * (visits) / n estimates f
                    double estimate = image[j] * pixels / n;
                    EXPECT_LT(Math.Abs(estimate - f[j]), 0.03 * f[j] + 1e-9);
                }
            }
        }

        // The tail map is heavy at a pixel where one group has a firefly, and the noise map (robust) is not; the pilot weights are one
        [TEST("MLTImportance", "TailStageFollowsPlainVariance")]
        public static void MLTImportance_TailStageFollowsPlainVariance()
        {
            Bounds2i bounds = new Bounds2i(new Point2i(0, 0), new Point2i(16, 16));
            MLTImportanceMap map = new MLTImportanceMap(bounds, 64);
            MLTNoiseEstimate noise = new MLTNoiseEstimate(map, 4);
            FillNoise(noise, new Float[] { 1, 1, 1, 50 }, new Float[] { 1, 1, 1, 1.0f });
            map.UpdateFromStage(noise, 1, 1, 0.05f, 0, true, 0.75f);
            EXPECT_LT(Math.Abs(map.NoiseWeight(5, 9) / map.NoiseWeight(12, 2) - 1), 0.01f);
            EXPECT_NE(map.TailWeight(12, 2), 1.0f);
            EXPECT_GT(map.TailWeight(5, 9), 10 * map.TailWeight(12, 2));
            map.Select(MLTStageKind.Pilot);
            EXPECT_EQ(map.Weight(new Point2f(5.5f, 9.5f)), 1.0f);
            map.Select(MLTStageKind.Tail);
            EXPECT_EQ(map.Weight(new Point2f(5.5f, 9.5f)), map.TailWeight(5, 9));
        }

        // With the importance c^beta w (beta below one) the chain and the splats f / I times the integral b of I give the same image too
        [TEST("MLTImportance", "ImageDoesNotDependOnExponent")]
        public static void MLTImportance_ImageDoesNotDependOnExponent()
        {
            Float[] f = { 0.1f, 2.0f, 0.5f, 8.0f, 0.0f, 1.0f, 3.0f, 0.25f };
            Float[] w = { 4, 0.5f, 2, 0.125f, 1, 1, 0.25f, 8 };
            int pixels = f.Length;
            foreach (Float beta in new Float[] { 1.0f, 0.75f, 0.5f })
            {
                double b = 0;
                for (int j = 0; j < pixels; ++j)
                {
                    b += Math.Pow(f[j], beta) * w[j];
                }
                b /= pixels;
                double[] image = new double[pixels];
                RNG rng = new RNG(9);
                int current = 3;
                int n = 4000000;
                for (int i = 0; i < n; ++i)
                {
                    int proposed = (int)(rng.Uniform<Float>() * pixels) % pixels;
                    Float iCurrent = MathF.Pow(f[current], beta) * w[current];
                    Float iProposed = MathF.Pow(f[proposed], beta) * w[proposed];
                    Float accept = Math.Min(1, iProposed / iCurrent);
                    if (accept > 0)
                    {
                        image[proposed] += f[proposed] * accept * b / iProposed;
                    }
                    image[current] += f[current] * (1 - accept) * b / iCurrent;
                    if (rng.Uniform<Float>() < accept)
                    {
                        current = proposed;
                    }
                }
                for (int j = 0; j < pixels; ++j)
                {
                    double estimate = image[j] * pixels / n;
                    EXPECT_LT(Math.Abs(estimate - f[j]), 0.05 * f[j] + 1e-9);
                }
            }
        }

        // A mixture of importance functions, each divided by its integral, is one fixed target: the chain and the splats f / I times the
        // integral b of I give the image whatever the probabilities of the mixture are
        [TEST("MLTImportance", "ImageDoesNotDependOnMixture")]
        public static void MLTImportance_ImageDoesNotDependOnMixture()
        {
            Float[] f = { 0.1f, 2.0f, 0.5f, 8.0f, 0.0f, 1.0f, 3.0f, 0.25f };
            Float[] wNoise = { 4, 0.5f, 2, 0.125f, 1, 1, 0.25f, 8 };
            Float[] wTail = { 0.3f, 2, 5, 1, 1, 0.2f, 1, 3 };
            int pixels = f.Length;
            Float tailExponent = 0.75f;
            double integralPilot = 0, integralNoise = 0, integralTail = 0;
            for (int j = 0; j < pixels; ++j)
            {
                integralPilot += f[j] / pixels;
                integralNoise += f[j] * wNoise[j] / pixels;
                integralTail += Math.Pow(f[j], tailExponent) * wTail[j] / pixels;
            }
            Float[][] alphaSets = { new Float[] { 0.2f, 0.5f, 0.3f }, new Float[] { 0.0f, 0.5f, 0.5f }, new Float[] { 1.0f, 0.0f, 0.0f } };
            foreach (Float[] alpha in alphaSets)
            {
                Float[] importance = new Float[pixels];
                double b = 0;
                for (int j = 0; j < pixels; ++j)
                {
                    importance[j] = (Float)(alpha[0] * f[j] / integralPilot + alpha[1] * f[j] * wNoise[j] / integralNoise +
                                            alpha[2] * Math.Pow(f[j], tailExponent) * wTail[j] / integralTail);
                    b += importance[j] / pixels;
                }
                double[] image = new double[pixels];
                RNG rng = new RNG(11);
                int current = 3;
                int n = 4000000;
                for (int i = 0; i < n; ++i)
                {
                    int proposed = (int)(rng.Uniform<Float>() * pixels) % pixels;
                    Float accept = Math.Min(1, importance[proposed] / importance[current]);
                    if (accept > 0)
                    {
                        image[proposed] += f[proposed] * accept * b / importance[proposed];
                    }
                    image[current] += f[current] * (1 - accept) * b / importance[current];
                    if (rng.Uniform<Float>() < accept)
                    {
                        current = proposed;
                    }
                }
                for (int j = 0; j < pixels; ++j)
                {
                    double estimate = image[j] * pixels / n;
                    EXPECT_LT(Math.Abs(estimate - f[j]), 0.05 * f[j] + 1e-9);
                }
            }
        }

        // Energy redistribution on a toy: the paths are uniform random pixels, each starts floor(u + c / (m e_d)) chains of m steps,
        // and a step deposits e_d split by the acceptance probability: the pixels get f on average, for every deposition energy
        [TEST("MLTImportance", "EnergyRedistributionIsUnbiased")]
        public static void MLTImportance_EnergyRedistributionIsUnbiased()
        {
            Float[] f = { 0.1f, 2.0f, 0.5f, 8.0f, 0.0f, 1.0f, 3.0f, 0.25f };
            int pixels = f.Length;
            double mean = 0;
            for (int j = 0; j < pixels; ++j)
            {
                mean += f[j] / pixels;
            }
            foreach (double mutationsPerBase in new double[] { 4, 32 })
            {
                double depositEnergy = mean / mutationsPerBase;
                int chainLength = 10;
                double[] image = new double[pixels];
                RNG rng = new RNG(21);
                int bases = 400000;
                for (int i = 0; i < bases; ++i)
                {
                    int start = (int)(rng.Uniform<Float>() * pixels) % pixels;
                    if (!(f[start] > 0))
                    {
                        continue;
                    }
                    int chains = (int)Math.Floor(rng.Uniform<Float>() + f[start] / (chainLength * depositEnergy));
                    for (int chain = 0; chain < chains; ++chain)
                    {
                        int current = start;
                        for (int step = 0; step < chainLength; ++step)
                        {
                            int proposed = (int)(rng.Uniform<Float>() * pixels) % pixels;
                            Float accept = Math.Min(1, f[proposed] / f[current]);
                            image[proposed] += depositEnergy * accept;
                            image[current] += depositEnergy * (1 - accept);
                            if (rng.Uniform<Float>() < accept)
                            {
                                current = proposed;
                            }
                        }
                    }
                }
                for (int j = 0; j < pixels; ++j)
                {
                    double estimate = image[j] * pixels / bases;
                    EXPECT_LT(Math.Abs(estimate - f[j]), 0.04 * f[j] + 1e-9);
                }
            }
        }

        static readonly Float[] toyValues = { 0.1f, 2.0f, 0.5f, 8.0f, 0.0f, 1.0f, 3.0f, 0.25f };

        // Whether the toy path of the pixel is an outlier (a fixed classification, as the outlier detector has during a stage)
        static bool ToyIsOutlier(int pixel)
        {
            return toyValues[pixel] > 2.0f;
        }

        // The toy of jump restore: uniform candidates start tours that deposit L / I K0 P / (1 + K) at every state, end with the probability
        // K / (1 + K) with K = K0 P / I and otherwise take an independent uniform step accepted with min(1, I' / I). With selective, the
        // paths that are not outliers count as they are and the target I is zero for them. The image is the same for every K0 and
        // every normaliser P, which need not be the integral of I
        [TEST("MLTImportance", "JumpRestoreIsUnbiased")]
        public static void MLTImportance_JumpRestoreIsUnbiased()
        {
            int pixels = toyValues.Length;
            double mean = 0;
            double outlierMean = 0;
            for (int j = 0; j < pixels; ++j)
            {
                mean += toyValues[j] / pixels;
                outlierMean += ToyIsOutlier(j) ? toyValues[j] / pixels : 0;
            }
            foreach (bool selective in new bool[] { false, true })
            {
                double integral = selective ? outlierMean : mean;
                foreach (double normaliserFactor in new double[] { 0.2, 1.0, 6.0 })
                {
                    foreach (double restoreRate in new double[] { 0.5, 1.0 })
                    {
                        double normaliser = normaliserFactor * integral;
                        double[] image = new double[pixels];
                        RNG rng = new RNG(33);
                        int candidates = 600000;
                        for (int i = 0; i < candidates; ++i)
                        {
                            int current = (int)(rng.Uniform<Float>() * pixels) % pixels;
                            if (!(toyValues[current] > 0))
                            {
                                continue;
                            }
                            if (selective && !ToyIsOutlier(current))
                            {
                                image[current] += 1.0 * toyValues[current];
                                continue;
                            }
                            while (true)
                            {
                                double iCurrent = toyValues[current];
                                double kill = restoreRate * normaliser / iCurrent;
                                double hold = 1.0 / (1.0 + kill);
                                image[current] += toyValues[current] / iCurrent * restoreRate * normaliser * hold;
                                if (rng.Uniform<Float>() < kill * hold)
                                {
                                    break;
                                }
                                int proposed = (int)(rng.Uniform<Float>() * pixels) % pixels;
                                double iProposed = (!selective || ToyIsOutlier(proposed)) ? toyValues[proposed] : 0;
                                if (rng.Uniform<Float>() < Math.Min(1.0, iProposed / iCurrent))
                                {
                                    current = proposed;
                                }
                            }
                        }
                        for (int j = 0; j < pixels; ++j)
                        {
                            double estimate = image[j] * pixels / candidates;
                            EXPECT_LT(Math.Abs(estimate - toyValues[j]), 0.05 * toyValues[j] + 1e-9);
                        }
                    }
                }
            }
        }

        // The toy of selectively Metropolised Monte Carlo: the non-outliers count as they are, the outlier candidates are the seeds
        // (in proportion to c) of chains of small uniform steps with I = c for the outliers and zero otherwise; every step deposits L / I times the
        // sum of c of the outlier candidates over the candidates and the steps
        [TEST("MLTImportance", "SelectiveSplitIsUnbiased")]
        public static void MLTImportance_SelectiveSplitIsUnbiased()
        {
            int pixels = toyValues.Length;
            double[] image = new double[pixels];
            RNG rng = new RNG(44);
            int candidates = 200000;
            List<int> seeds = new List<int>();
            double seedTotal = 0;
            for (int i = 0; i < candidates; ++i)
            {
                int pixel = (int)(rng.Uniform<Float>() * pixels) % pixels;
                if (!(toyValues[pixel] > 0))
                {
                    continue;
                }
                if (ToyIsOutlier(pixel))
                {
                    seeds.Add(pixel);
                    seedTotal += toyValues[pixel];
                }
                else
                {
                    image[pixel] += toyValues[pixel];
                }
            }
            int chains = 2000;
            int chainLength = 100;
            double stepScale = seedTotal / ((double)chains * chainLength);
            for (int chain = 0; chain < chains; ++chain)
            {
                double target = rng.Uniform<Float>() * seedTotal;
                int current = seeds[0];
                double running = 0;
                foreach (int seed in seeds)
                {
                    running += toyValues[seed];
                    if (running >= target)
                    {
                        current = seed;
                        break;
                    }
                }
                for (int step = 0; step < chainLength; ++step)
                {
                    int proposed = (int)(rng.Uniform<Float>() * pixels) % pixels;
                    double iProposed = ToyIsOutlier(proposed) ? toyValues[proposed] : 0;
                    double accept = Math.Min(1.0, iProposed / toyValues[current]);
                    if (accept > 0)
                    {
                        image[proposed] += accept * stepScale;
                    }
                    image[current] += (1 - accept) * stepScale;
                    if (rng.Uniform<Float>() < accept)
                    {
                        current = proposed;
                    }
                }
            }
            for (int j = 0; j < pixels; ++j)
            {
                double estimate = image[j] * pixels / candidates;
                EXPECT_LT(Math.Abs(estimate - toyValues[j]), 0.05 * toyValues[j] + 1e-9);
            }
        }
    }
}
