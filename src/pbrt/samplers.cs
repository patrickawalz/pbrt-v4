// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// samplers.h / samplers.cpp

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // Sampler Inline Functions
        public static CameraSample GetCameraSample(Sampler sampler, Point2i pPixel, Filter filter)
        {
            FilterSample fs = filter.Sample(sampler.GetPixel2D());
            CameraSample cs = new CameraSample();
            // Initialize _CameraSample_ member variables
            cs.pFilm = new Point2f(pPixel.x, pPixel.y) + fs.p + new Vector2f(0.5f, 0.5f);
            cs.time = sampler.Get1D();
            cs.pLens = sampler.Get2D();
            cs.filterWeight = fs.weight;

            if (Options.disablePixelJitter)
            {
                cs.pFilm = new Point2f(pPixel.x, pPixel.y) + new Vector2f(0.5f, 0.5f);
                cs.time = 0.5f;
                cs.pLens = new Point2f(0.5f, 0.5f);
                cs.filterWeight = 1;
            }
            return cs;
        }
    }


    // HaltonSampler Definition
    public sealed unsafe class HaltonSampler : Sampler
    {
        // HaltonSampler Public Methods
        public HaltonSampler(int samplesPerPixel, Point2i fullRes,
                             RandomizeStrategy randomize = RandomizeStrategy.PermuteDigits,
                             int seed = 0, Allocator alloc = null)
        {
            this.samplesPerPixel = samplesPerPixel;
            this.randomize = randomize;
            if (randomize == RandomizeStrategy.PermuteDigits)
                digitPermutations = ComputeRadicalInversePermutations((uint32_t)seed, alloc);
            // Find radical inverse base scales and exponents that cover sampling area
            for (int i = 0; i < 2; ++i)
            {
                int base_ = (i == 0) ? 2 : 3;
                int scale = 1, exp = 0;
                while (scale < std.min(fullRes[i], MaxHaltonResolution))
                {
                    scale *= base_;
                    ++exp;
                }
                baseScales[i] = scale;
                baseExponents[i] = exp;
            }

            // Compute multiplicative inverses for _baseScales_
            multInverse0 = (int)multiplicativeInverse(baseScales[1], baseScales[0]);
            multInverse1 = (int)multiplicativeInverse(baseScales[0], baseScales[1]);
        }

        public static string Name() { return "HaltonSampler"; }
        public static HaltonSampler Create(ParameterDictionary parameters, Point2i fullResolution, FileLoc loc, Allocator alloc)
        {
            int nsamp = parameters.GetOneInt("pixelsamples", 16);
            if (Options.pixelSamples)
                nsamp = Options.pixelSamples.value();
            int seed = parameters.GetOneInt("seed", Options.seed);
            if (Options.quickRender)
                nsamp = 1;

            RandomizeStrategy randomizer = RandomizeStrategy.None;
            string s = parameters.GetOneString("randomization", "permutedigits");
            if (s == "none")
                randomizer = RandomizeStrategy.None;
            else if (s == "permutedigits")
                randomizer = RandomizeStrategy.PermuteDigits;
            else if (s == "fastowen")
                ErrorExit("\"fastowen\" randomization not supported by Halton sampler.");
            else if (s == "owen")
                randomizer = RandomizeStrategy.Owen;
            else
                ErrorExit(loc, StringPrintf("%s: unknown randomization strategy given to HaltonSampler", s));

            return new HaltonSampler(nsamp, fullResolution, randomizer, seed, alloc);
        }

        public override int SamplesPerPixel() { return samplesPerPixel; }

        public RandomizeStrategy GetRandomizeStrategy() { return randomize; }

        public override void StartPixelSample(Point2i p, int sampleIndex, int dim = 0)
        {
            haltonIndex = 0;
            int sampleStride = baseScales[0] * baseScales[1];
            // Compute Halton sample index for first sample in pixel _p_
            if (sampleStride > 1)
            {
                Point2i pm = new Point2i(Mod(p[0], MaxHaltonResolution), Mod(p[1], MaxHaltonResolution));
                for (int i = 0; i < 2; ++i)
                {
                    uint64_t dimOffset =
                        (i == 0) ? InverseRadicalInverse((uint64_t)pm[i], 2, baseExponents[i])
                                 : InverseRadicalInverse((uint64_t)pm[i], 3, baseExponents[i]);
                    haltonIndex += (int64_t)(dimOffset * (uint64_t)(sampleStride / baseScales[i]) *
                                             (uint64_t)(i == 0 ? multInverse0 : multInverse1));
                }
                haltonIndex %= sampleStride;
            }

            haltonIndex += (int64_t)sampleIndex * sampleStride;
            dimension = std.max(2, dim);
        }

        public override Float Get1D()
        {
            if (dimension >= PrimeTableSize)
                dimension = 2;
            return SampleDimension(dimension++);
        }

        public override Point2f Get2D()
        {
            if (dimension + 1 >= PrimeTableSize)
                dimension = 2;
            int dim = dimension;
            dimension += 2;
            return new Point2f(SampleDimension(dim), SampleDimension(dim + 1));
        }

        public override Point2f GetPixel2D()
        {
            return new Point2f(RadicalInverse(0, (uint64_t)(haltonIndex >> baseExponents[0])),
                               RadicalInverse(1, (uint64_t)(haltonIndex / baseScales[1])));
        }

        public override Sampler Clone(Allocator alloc = null)
        {
            return (Sampler)MemberwiseClone();
        }

        public override string ToString()
        {
            return StringPrintf("[ HaltonSampler randomize: %s digitPermutations: %s " +
                                "haltonIndex: %d dimension: %d samplesPerPixel: %d " +
                                "baseScales: %s baseExponents: %s multInverse: [ %d %d ] ]",
                                randomize, digitPermutations, haltonIndex, dimension,
                                samplesPerPixel, baseScales, baseExponents, multInverse0,
                                multInverse1);
        }

        // HaltonSampler Private Methods
        static uint64_t multiplicativeInverse(int64_t a, int64_t n)
        {
            int64_t x, y;
            extendedGCD((uint64_t)a, (uint64_t)n, &x, &y);
            return (uint64_t)Mod(x, n);
        }

        static void extendedGCD(uint64_t a, uint64_t b, int64_t* x, int64_t* y)
        {
            if (b == 0)
            {
                *x = 1;
                *y = 0;
                return;
            }
            int64_t d = (int64_t)(a / b), xp, yp;
            extendedGCD(b, a % b, &xp, &yp);
            *x = yp;
            *y = xp - (d * yp);
        }

        Float SampleDimension(int dimension)
        {
            if (randomize == RandomizeStrategy.None)
                return RadicalInverse(dimension, (uint64_t)haltonIndex);
            else if (randomize == RandomizeStrategy.PermuteDigits)
                return ScrambledRadicalInverse(dimension, (uint64_t)haltonIndex,
                                               digitPermutations[dimension]);
            else
            {
                DCHECK(randomize == RandomizeStrategy.Owen);
                return OwenScrambledRadicalInverse(dimension, (uint64_t)haltonIndex,
                                                   (uint32_t)MixBits((uint64_t)(1 + (dimension << 4))));
            }
        }

        // HaltonSampler Private Members
        int samplesPerPixel;
        RandomizeStrategy randomize;
        pstd.vector<DigitPermutation> digitPermutations = null;
        const int MaxHaltonResolution = 128;
        Point2i baseScales, baseExponents;
        int multInverse0, multInverse1;
        int64_t haltonIndex = 0;
        int dimension = 0;
    }

    // PaddedSobolSampler Definition
    public sealed unsafe class PaddedSobolSampler : Sampler
    {
        // PaddedSobolSampler Public Methods
        public static string Name() { return "PaddedSobolSampler"; }
        public static PaddedSobolSampler Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            int nsamp = parameters.GetOneInt("pixelsamples", 16);
            if (Options.pixelSamples)
                nsamp = Options.pixelSamples.value();
            if (Options.quickRender)
                nsamp = 1;
            int seed = parameters.GetOneInt("seed", Options.seed);

            RandomizeStrategy randomizer = RandomizeStrategy.None;
            string s = parameters.GetOneString("randomization", "fastowen");
            if (s == "none")
                randomizer = RandomizeStrategy.None;
            else if (s == "permutedigits")
                randomizer = RandomizeStrategy.PermuteDigits;
            else if (s == "fastowen")
                randomizer = RandomizeStrategy.FastOwen;
            else if (s == "owen")
                randomizer = RandomizeStrategy.Owen;
            else
                ErrorExit(loc, StringPrintf("%s: unknown randomization strategy given to PaddedSobolSampler", s));

            return new PaddedSobolSampler(nsamp, randomizer, seed);
        }

        public PaddedSobolSampler(int samplesPerPixel, RandomizeStrategy randomizer, int seed = 0)
        {
            this.samplesPerPixel = samplesPerPixel;
            this.randomize = randomizer;
            this.seed = seed;
            if (!IsPowerOf2(samplesPerPixel))
                Warning(StringPrintf(
                    "Sobol samplers with non power-of-two sample counts (%d) are suboptimal.",
                    samplesPerPixel));
        }

        public override int SamplesPerPixel() { return samplesPerPixel; }

        public override void StartPixelSample(Point2i p, int index, int dim = 0)
        {
            pixel = p;
            sampleIndex = index;
            dimension = dim;
        }

        public override Float Get1D()
        {
            // Get permuted index for current pixel sample
            uint64_t hash = Hash(pixel, dimension, seed);
            int index = PermutationElement((uint32_t)sampleIndex, (uint32_t)samplesPerPixel, (uint32_t)hash);

            int dim = dimension++;
            // Return randomized 1D van der Corput sample for dimension _dim_
            return SampleDimension(0, (uint32_t)index, (uint32_t)(hash >> 32));
        }

        public override Point2f Get2D()
        {
            // Get permuted index for current pixel sample
            uint64_t hash = Hash(pixel, dimension, seed);
            int index = PermutationElement((uint32_t)sampleIndex, (uint32_t)samplesPerPixel, (uint32_t)hash);

            int dim = dimension;
            dimension += 2;
            // Return randomized 2D Sobol' sample
            return new Point2f(SampleDimension(0, (uint32_t)index, (uint32_t)hash),
                               SampleDimension(1, (uint32_t)index, (uint32_t)(hash >> 32)));
        }

        public override Point2f GetPixel2D() { return Get2D(); }

        public RandomizeStrategy GetRandomizeStrategy() { return randomize; }

        public override Sampler Clone(Allocator alloc = null)
        {
            return (Sampler)MemberwiseClone();
        }

        public override string ToString()
        {
            return StringPrintf("[ PaddedSobolSampler pixel: %s sampleIndex: %d dimension: %d " +
                                "samplesPerPixel: %d seed: %d randomize: %s ]",
                                pixel, sampleIndex, dimension, samplesPerPixel, seed, randomize);
        }

        // PaddedSobolSampler Private Methods
        Float SampleDimension(int dimension, uint32_t a, uint32_t hash)
        {
            if (randomize == RandomizeStrategy.None)
                return SobolSample(a, dimension, new NoRandomizer());
            else if (randomize == RandomizeStrategy.PermuteDigits)
                return SobolSample(a, dimension, new BinaryPermuteScrambler(hash));
            else if (randomize == RandomizeStrategy.FastOwen)
                return SobolSample(a, dimension, new FastOwenScrambler(hash));
            else
                return SobolSample(a, dimension, new OwenScrambler(hash));
        }

        // PaddedSobolSampler Private Members
        int samplesPerPixel, seed;
        RandomizeStrategy randomize;
        Point2i pixel;
        int sampleIndex, dimension;
    }

    // ZSobolSampler Definition
    public sealed unsafe class ZSobolSampler : Sampler
    {
        // ZSobolSampler Public Methods
        public ZSobolSampler(int samplesPerPixel, Point2i fullResolution,
                             RandomizeStrategy randomize, int seed = 0)
        {
            this.randomize = randomize;
            this.seed = seed;
            if (!IsPowerOf2(samplesPerPixel))
                Warning(StringPrintf(
                    "Sobol samplers with non power-of-two sample counts (%d) are suboptimal.",
                    samplesPerPixel));
            log2SamplesPerPixel = Log2Int(samplesPerPixel);
            int res = RoundUpPow2(std.max(fullResolution.x, fullResolution.y));
            int log4SamplesPerPixel = (log2SamplesPerPixel + 1) / 2;
            nBase4Digits = Log2Int(res) + log4SamplesPerPixel;
        }

        public static string Name() { return "ZSobolSampler"; }

        public static ZSobolSampler Create(ParameterDictionary parameters, Point2i fullResolution, FileLoc loc, Allocator alloc)
        {
            int nsamp = parameters.GetOneInt("pixelsamples", 16);
            if (Options.pixelSamples)
                nsamp = Options.pixelSamples.value();
            if (Options.quickRender)
                nsamp = 1;
            int seed = parameters.GetOneInt("seed", Options.seed);

            RandomizeStrategy randomizer = RandomizeStrategy.None;
            string s = parameters.GetOneString("randomization", "fastowen");
            if (s == "none")
                randomizer = RandomizeStrategy.None;
            else if (s == "permutedigits")
                randomizer = RandomizeStrategy.PermuteDigits;
            else if (s == "fastowen")
                randomizer = RandomizeStrategy.FastOwen;
            else if (s == "owen")
                randomizer = RandomizeStrategy.Owen;
            else
                ErrorExit(loc, StringPrintf("%s: unknown randomization strategy given to ZSobolSampler", s));

            return new ZSobolSampler(nsamp, fullResolution, randomizer, seed);
        }

        public override int SamplesPerPixel() { return 1 << log2SamplesPerPixel; }

        public override void StartPixelSample(Point2i p, int index, int dim = 0)
        {
            dimension = dim;
            mortonIndex = (EncodeMorton2((uint32_t)p.x, (uint32_t)p.y) << log2SamplesPerPixel) | (uint64_t)(uint32_t)index;
        }

        public override Float Get1D()
        {
            uint64_t sampleIndex = GetSampleIndex();
            ++dimension;
            // Generate 1D Sobol' sample at _sampleIndex_
            uint32_t sampleHash = (uint32_t)Hash(dimension, seed);
            if (randomize == RandomizeStrategy.None)
                return SobolSample((int64_t)sampleIndex, 0, new NoRandomizer());
            else if (randomize == RandomizeStrategy.PermuteDigits)
                return SobolSample((int64_t)sampleIndex, 0, new BinaryPermuteScrambler(sampleHash));
            else if (randomize == RandomizeStrategy.FastOwen)
                return SobolSample((int64_t)sampleIndex, 0, new FastOwenScrambler(sampleHash));
            else
                return SobolSample((int64_t)sampleIndex, 0, new OwenScrambler(sampleHash));
        }

        public override Point2f Get2D()
        {
            uint64_t sampleIndex = GetSampleIndex();
            dimension += 2;
            // Generate 2D Sobol' sample at _sampleIndex_
            uint64_t bits = Hash(dimension, seed);
            uint32_t sampleHash0 = (uint32_t)bits, sampleHash1 = (uint32_t)(bits >> 32);
            int64_t si = (int64_t)sampleIndex;
            if (randomize == RandomizeStrategy.None)
                return new Point2f(SobolSample(si, 0, new NoRandomizer()),
                                   SobolSample(si, 1, new NoRandomizer()));
            else if (randomize == RandomizeStrategy.PermuteDigits)
                return new Point2f(SobolSample(si, 0, new BinaryPermuteScrambler(sampleHash0)),
                                   SobolSample(si, 1, new BinaryPermuteScrambler(sampleHash1)));
            else if (randomize == RandomizeStrategy.FastOwen)
                return new Point2f(SobolSample(si, 0, new FastOwenScrambler(sampleHash0)),
                                   SobolSample(si, 1, new FastOwenScrambler(sampleHash1)));
            else
                return new Point2f(SobolSample(si, 0, new OwenScrambler(sampleHash0)),
                                   SobolSample(si, 1, new OwenScrambler(sampleHash1)));
        }

        public override Point2f GetPixel2D() { return Get2D(); }

        public override Sampler Clone(Allocator alloc = null)
        {
            return (Sampler)MemberwiseClone();
        }

        public override string ToString()
        {
            return StringPrintf("[ ZSobolSampler randomize: %s log2SamplesPerPixel: %d " +
                                " seed: %d nBase4Digits: %d mortonIndex: %d dimension: %d ]",
                                randomize, log2SamplesPerPixel, seed, nBase4Digits, mortonIndex,
                                dimension);
        }

        // Define the full set of 4-way permutations in _permutations_
        static readonly byte[,] permutations = new byte[24, 4] {
            {0, 1, 2, 3},
            {0, 1, 3, 2},
            {0, 2, 1, 3},
            {0, 2, 3, 1},
            // Define remaining 20 4-way permutations
            {0, 3, 2, 1},
            {0, 3, 1, 2},
            {1, 0, 2, 3},
            {1, 0, 3, 2},
            {1, 2, 0, 3},
            {1, 2, 3, 0},
            {1, 3, 2, 0},
            {1, 3, 0, 2},
            {2, 1, 0, 3},
            {2, 1, 3, 0},
            {2, 0, 1, 3},
            {2, 0, 3, 1},
            {2, 3, 0, 1},
            {2, 3, 1, 0},
            {3, 1, 2, 0},
            {3, 1, 0, 2},
            {3, 2, 1, 0},
            {3, 2, 0, 1},
            {3, 0, 2, 1},
            {3, 0, 1, 2}
        };

        public uint64_t GetSampleIndex()
        {
            uint64_t sampleIndex = 0;
            // Apply random permutations to full base-4 digits
            bool pow2Samples = (log2SamplesPerPixel & 1) != 0;
            int lastDigit = pow2Samples ? 1 : 0;
            for (int i = nBase4Digits - 1; i >= lastDigit; --i)
            {
                // Randomly permute $i$th base-4 digit in _mortonIndex_
                int digitShift = 2 * i - (pow2Samples ? 1 : 0);
                int digit = (int)((mortonIndex >> digitShift) & 3);
                // Choose permutation _p_ to use for _digit_
                uint64_t higherDigits = mortonIndex >> (digitShift + 2);
                int p = (int)((MixBits(higherDigits ^ (uint64_t)(0x55555555u * (uint32_t)dimension)) >> 24) % 24);

                digit = permutations[p, digit];
                sampleIndex |= (uint64_t)digit << digitShift;
            }

            // Handle power-of-2 (but not 4) sample count
            if (pow2Samples)
            {
                int digit = (int)(mortonIndex & 1);
                sampleIndex |=
                    (uint64_t)digit ^ (MixBits((mortonIndex >> 1) ^ (uint64_t)(0x55555555u * (uint32_t)dimension)) & 1);
            }

            return sampleIndex;
        }

        // ZSobolSampler Private Members
        RandomizeStrategy randomize;
        int seed, log2SamplesPerPixel, nBase4Digits;
        uint64_t mortonIndex;
        int dimension;
    }

    // PMJ02BNSampler Definition
    public sealed unsafe class PMJ02BNSampler : Sampler
    {
        // PMJ02BNSampler Public Methods
        public PMJ02BNSampler(int samplesPerPixel, int seed = 0, Allocator alloc = null)
        {
            this.samplesPerPixel = samplesPerPixel;
            this.seed = seed;
            if (!IsPowerOf4(samplesPerPixel))
                Warning("PMJ02BNSampler results are best with power-of-4 samples per " +
                        "pixel (1, 4, 16, 64, ...)");
            // Get sorted pmj02bn samples for pixel samples
            if (samplesPerPixel > nPMJ02bnSamples)
                Error(StringPrintf("PMJ02BNSampler only supports up to %d samples per pixel", nPMJ02bnSamples));
            // Compute _pixelTileSize_ for pmj02bn pixel samples and allocate _pixelSamples_
            pixelTileSize =
                1 << (Log4Int(nPMJ02bnSamples) - Log4Int(RoundUpPow4(samplesPerPixel)));
            int nPixelSamples = pixelTileSize * pixelTileSize * samplesPerPixel;
            pixelSamples = new pstd.vector<Point2f>((ulong)nPixelSamples);

            // Loop over pmj02bn samples and associate them with their pixels
            int[] nStored = new int[pixelTileSize * pixelTileSize];
            for (int i = 0; i < nPMJ02bnSamples; ++i)
            {
                Point2f p = GetPMJ02BNSample(0, i);
                p = p * pixelTileSize;
                int pixelOffset = (int)p.x + (int)p.y * pixelTileSize;
                if (nStored[pixelOffset] == samplesPerPixel)
                {
                    CHECK(!IsPowerOf4(samplesPerPixel));
                    continue;
                }
                int sampleOffset = pixelOffset * samplesPerPixel + nStored[pixelOffset];
                CHECK(pixelSamples[sampleOffset] == new Point2f(0, 0));
                pixelSamples[sampleOffset] = new Point2f(p.x - std.floor(p.x), p.y - std.floor(p.y));
                ++nStored[pixelOffset];
            }

            for (int i = 0; i < nStored.Length; ++i)
                CHECK_EQ(nStored[i], samplesPerPixel);
        }

        public static string Name() { return "PMJ02BNSampler"; }
        public static PMJ02BNSampler Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            int nsamp = parameters.GetOneInt("pixelsamples", 16);
            if (Options.pixelSamples)
                nsamp = Options.pixelSamples.value();
            if (Options.quickRender)
                nsamp = 1;
            int seed = parameters.GetOneInt("seed", Options.seed);
            return new PMJ02BNSampler(nsamp, seed, alloc);
        }

        public override int SamplesPerPixel() { return samplesPerPixel; }

        public override void StartPixelSample(Point2i p, int index, int dim = 0)
        {
            pixel = p;
            sampleIndex = index;
            dimension = std.max(2, dim);
        }

        public override Float Get1D()
        {
            // Find permuted sample index for 1D PMJ02BNSampler sample
            uint64_t hash = Hash(pixel, dimension, seed);
            int index = PermutationElement((uint32_t)sampleIndex, (uint32_t)samplesPerPixel, (uint32_t)hash);

            Float delta = BlueNoise(dimension, pixel);
            ++dimension;
            return std.min((index + delta) / samplesPerPixel, OneMinusEpsilon);
        }

        public override Point2f GetPixel2D()
        {
            int px = pixel.x % pixelTileSize, py = pixel.y % pixelTileSize;
            int offset = (px + py * pixelTileSize) * samplesPerPixel;
            return pixelSamples[offset + sampleIndex];
        }

        public override Point2f Get2D()
        {
            // Compute index for 2D pmj02bn sample
            int index = sampleIndex;
            int pmjInstance = dimension / 2;
            if (pmjInstance >= nPMJ02bnSets)
            {
                // Permute index to be used for pmj02bn sample array
                uint64_t hash = Hash(pixel, dimension, seed);
                index = PermutationElement((uint32_t)sampleIndex, (uint32_t)samplesPerPixel, (uint32_t)hash);
            }

            // Return randomized pmj02bn sample for current dimension
            Point2f u = GetPMJ02BNSample(pmjInstance, index);
            // Apply Cranley-Patterson rotation to pmj02bn sample _u_
            u = u + new Vector2f(BlueNoise(dimension, pixel), BlueNoise(dimension + 1, pixel));
            if (u.x >= 1)
                u.x -= 1;
            if (u.y >= 1)
                u.y -= 1;

            dimension += 2;
            return new Point2f(std.min(u.x, OneMinusEpsilon), std.min(u.y, OneMinusEpsilon));
        }

        public override Sampler Clone(Allocator alloc = null)
        {
            return (Sampler)MemberwiseClone();
        }

        public override string ToString()
        {
            return StringPrintf("[ PMJ02BNSampler pixel: %s sampleIndex: %d dimension: %d " +
                                "samplesPerPixel: %d pixelTileSize: %d pixelSamples: %s ]",
                                pixel, sampleIndex, dimension, samplesPerPixel, pixelTileSize,
                                pixelSamples);
        }

        // PMJ02BNSampler Private Members
        int samplesPerPixel, seed;
        int pixelTileSize;
        pstd.vector<Point2f> pixelSamples;
        Point2i pixel;
        int sampleIndex, dimension;
    }

    // IndependentSampler Definition
    public sealed unsafe class IndependentSampler : Sampler
    {
        // IndependentSampler Public Methods
        public IndependentSampler(int samplesPerPixel, int seed = 0)
        {
            this.samplesPerPixel = samplesPerPixel;
            this.seed = seed;
        }

        public static IndependentSampler Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            int ns = parameters.GetOneInt("pixelsamples", 4);
            if (Options.pixelSamples)
                ns = Options.pixelSamples.value();
            int seed = parameters.GetOneInt("seed", Options.seed);
            return new IndependentSampler(ns, seed);
        }
        public static string Name() { return "IndependentSampler"; }

        public override int SamplesPerPixel() { return samplesPerPixel; }

        public override void StartPixelSample(Point2i p, int sampleIndex, int dimension = 0)
        {
            rng.SetSequence(Hash(p, seed));
            rng.Advance((long)((uint64_t)sampleIndex * 65536ul + (uint64_t)dimension));
        }

        public override Float Get1D() { return rng.Uniform<Float>(); }
        public override Point2f Get2D() { Float a = rng.Uniform<Float>(); Float b = rng.Uniform<Float>(); return new Point2f(a, b); }
        public override Point2f GetPixel2D() { return Get2D(); }

        public override Sampler Clone(Allocator alloc = null)
        {
            return (Sampler)MemberwiseClone();
        }

        public override string ToString()
        {
            return StringPrintf("[ IndependentSampler samplesPerPixel: %d seed: %d rng: %s ]",
                                samplesPerPixel, seed, rng);
        }

        // IndependentSampler Private Members
        int samplesPerPixel, seed;
        RNG rng;
    }

    // SobolSampler Definition
    public sealed unsafe class SobolSampler : Sampler
    {
        // SobolSampler Public Methods
        public SobolSampler(int samplesPerPixel, Point2i fullResolution, RandomizeStrategy randomize,
                            int seed = 0)
        {
            this.samplesPerPixel = samplesPerPixel;
            this.seed = seed;
            this.randomize = randomize;
            if (!IsPowerOf2(samplesPerPixel))
                Warning(StringPrintf("Non power-of-two sample count %d will perform suboptimally with the " +
                        "SobolSampler.", samplesPerPixel));
            scale = RoundUpPow2(std.max(fullResolution.x, fullResolution.y));
        }

        public static string Name() { return "SobolSampler"; }
        public static SobolSampler Create(ParameterDictionary parameters, Point2i fullResolution, FileLoc loc, Allocator alloc)
        {
            int nsamp = parameters.GetOneInt("pixelsamples", 16);
            if (Options.pixelSamples)
                nsamp = Options.pixelSamples.value();
            if (Options.quickRender)
                nsamp = 1;

            RandomizeStrategy randomizer = RandomizeStrategy.None;
            string s = parameters.GetOneString("randomization", "fastowen");
            if (s == "none")
                randomizer = RandomizeStrategy.None;
            else if (s == "permutedigits")
                randomizer = RandomizeStrategy.PermuteDigits;
            else if (s == "fastowen")
                randomizer = RandomizeStrategy.FastOwen;
            else if (s == "owen")
                randomizer = RandomizeStrategy.Owen;
            else
                ErrorExit(loc, StringPrintf("%s: unknown randomization strategy given to SobolSampler", s));

            int seed = parameters.GetOneInt("seed", Options.seed);

            return new SobolSampler(nsamp, fullResolution, randomizer, seed);
        }

        public override int SamplesPerPixel() { return samplesPerPixel; }

        public override void StartPixelSample(Point2i p, int sampleIndex, int dim = 0)
        {
            pixel = p;
            dimension = std.max(2, dim);
            sobolIndex = (int64_t)SobolIntervalToIndex((uint32_t)Log2Int(scale), (uint64_t)sampleIndex, pixel);
        }

        public override Float Get1D()
        {
            if (dimension >= NSobolDimensions)
                dimension = 2;
            return SampleDimension(dimension++);
        }

        public override Point2f Get2D()
        {
            if (dimension + 1 >= NSobolDimensions)
                dimension = 2;
            Point2f u = new Point2f(SampleDimension(dimension), SampleDimension(dimension + 1));
            dimension += 2;
            return u;
        }

        public override Point2f GetPixel2D()
        {
            Point2f u = new Point2f(SobolSample(sobolIndex, 0, new NoRandomizer()),
                                    SobolSample(sobolIndex, 1, new NoRandomizer()));
            // Remap Sobol' dimensions used for pixel samples
            for (int dim = 0; dim < 2; ++dim)
            {
                Float v = Clamp(u[dim] * scale - pixel[dim], 0, OneMinusEpsilon);
                if (dim == 0) u.x = v; else u.y = v;
            }

            return u;
        }

        public override Sampler Clone(Allocator alloc = null)
        {
            return (Sampler)MemberwiseClone();
        }

        public override string ToString()
        {
            return StringPrintf("[ SobolSampler pixel: %s dimension: %d " +
                                "samplesPerPixel: %d scale: %d sobolIndex: %d " +
                                "seed: %d randomize: %s ]",
                                pixel, dimension, samplesPerPixel, scale, sobolIndex, seed,
                                randomize);
        }

        // SobolSampler Private Methods
        Float SampleDimension(int dimension)
        {
            // Return un-randomized Sobol' sample if appropriate
            if (randomize == RandomizeStrategy.None)
                return SobolSample(sobolIndex, dimension, new NoRandomizer());

            // Return randomized Sobol' sample using _randomize_
            uint32_t hash = (uint32_t)Hash(dimension, seed);
            if (randomize == RandomizeStrategy.PermuteDigits)
                return SobolSample(sobolIndex, dimension, new BinaryPermuteScrambler(hash));
            else if (randomize == RandomizeStrategy.FastOwen)
                return SobolSample(sobolIndex, dimension, new FastOwenScrambler(hash));
            else
                return SobolSample(sobolIndex, dimension, new OwenScrambler(hash));
        }

        // SobolSampler Private Members
        int samplesPerPixel, scale, seed;
        RandomizeStrategy randomize;
        Point2i pixel;
        int dimension;
        int64_t sobolIndex;
    }

    // StratifiedSampler Definition
    public sealed unsafe class StratifiedSampler : Sampler
    {
        // StratifiedSampler Public Methods
        public StratifiedSampler(int xPixelSamples, int yPixelSamples, bool jitter, int seed = 0)
        {
            this.xPixelSamples = xPixelSamples;
            this.yPixelSamples = yPixelSamples;
            this.seed = seed;
            this.jitter = jitter;
        }

        public static StratifiedSampler Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            bool jitter = parameters.GetOneBool("jitter", true);
            int xSamples = parameters.GetOneInt("xsamples", 4);
            int ySamples = parameters.GetOneInt("ysamples", 4);
            if (Options.pixelSamples)
            {
                int nSamples = Options.pixelSamples.value();
                int div = (int)std.sqrt(nSamples);
                while ((nSamples % div) != 0)
                {
                    CHECK_GT(div, 0);
                    --div;
                }
                xSamples = nSamples / div;
                ySamples = nSamples / xSamples;
                CHECK_EQ(nSamples, xSamples * ySamples);
                LOG_VERBOSE("xSamples %d ySamples %d", xSamples, ySamples);
            }
            if (Options.quickRender)
                xSamples = ySamples = 1;
            int seed = parameters.GetOneInt("seed", Options.seed);

            return new StratifiedSampler(xSamples, ySamples, jitter, seed);
        }
        public static string Name() { return "StratifiedSampler"; }

        public override int SamplesPerPixel() { return xPixelSamples * yPixelSamples; }

        public override void StartPixelSample(Point2i p, int index, int dim = 0)
        {
            pixel = p;
            sampleIndex = index;
            dimension = dim;
            rng.SetSequence(Hash(p, seed));
            rng.Advance((long)((uint64_t)sampleIndex * 65536ul + (uint64_t)dimension));
        }

        public override Float Get1D()
        {
            // Compute _stratum_ index for current pixel and dimension
            uint64_t hash = Hash(pixel, dimension, seed);
            int stratum = PermutationElement((uint32_t)sampleIndex, (uint32_t)SamplesPerPixel(), (uint32_t)hash);

            ++dimension;
            Float delta = jitter ? rng.Uniform<Float>() : 0.5f;
            return (stratum + delta) / SamplesPerPixel();
        }

        public override Point2f Get2D()
        {
            // Compute _stratum_ index for current pixel and dimension
            uint64_t hash = Hash(pixel, dimension, seed);
            int stratum = PermutationElement((uint32_t)sampleIndex, (uint32_t)SamplesPerPixel(), (uint32_t)hash);

            dimension += 2;
            int x = stratum % xPixelSamples, y = stratum / xPixelSamples;
            Float dx = jitter ? rng.Uniform<Float>() : 0.5f;
            Float dy = jitter ? rng.Uniform<Float>() : 0.5f;
            return new Point2f((x + dx) / xPixelSamples, (y + dy) / yPixelSamples);
        }

        public override Point2f GetPixel2D() { return Get2D(); }

        public override Sampler Clone(Allocator alloc = null)
        {
            return (Sampler)MemberwiseClone();
        }

        public override string ToString()
        {
            return StringPrintf(
                "[ StratifiedSampler pixel: %s sampleIndex: %d dimension: %d " +
                "xPixelSamples: %d yPixelSamples: %d jitter: %s seed: %d rng: %s ]",
                pixel, sampleIndex, dimension, xPixelSamples, yPixelSamples, jitter, seed, rng);
        }

        // StratifiedSampler Private Members
        int xPixelSamples, yPixelSamples, seed;
        bool jitter;
        RNG rng;
        Point2i pixel;
        int sampleIndex = 0, dimension = 0;
    }

    // MLTSampler Definition
    public unsafe class MLTSampler : Sampler
    {
        // MLTSampler Public Methods
        public MLTSampler(int mutationsPerPixel, int rngSequenceIndex, Float sigma,
                          Float largeStepProbability, int streamCount)
        {
            this.mutationsPerPixel = mutationsPerPixel;
            this.rng = new RNG(MixBits((uint64_t)rngSequenceIndex) ^ MixBits((uint64_t)Options.seed));
            this.sigma = sigma;
            this.largeStepProbability = largeStepProbability;
            this.streamCount = streamCount;
            this.X = new pstd.vector<PrimarySample>();
        }

        public void StartIteration()
        {
            currentIteration++;
            largeStep = rng.Uniform<Float>() < largeStepProbability;
        }

        public void Reject()
        {
            for (ulong i = 0; i < X.size(); ++i)
            {
                ref PrimarySample X_i = ref X[i];
                if (X_i.lastModificationIteration == currentIteration)
                    X_i.Restore();
            }
            --currentIteration;
        }

        public void StartStream(int index)
        {
            DCHECK_LT(index, streamCount);
            streamIndex = index;
            sampleIndex = 0;
        }

        public int GetNextIndex() { return streamIndex + streamCount * sampleIndex++; }

        public override int SamplesPerPixel() { return mutationsPerPixel; }

        public override void StartPixelSample(Point2i p, int sampleIndex, int dim = 0)
        {
            rng.SetSequence(Hash(p));
            rng.Advance((long)sampleIndex * 65536 + dim * 8192);
        }

        public override Float Get1D()
        {
            int index = GetNextIndex();
            EnsureReady(index);
            return X[index].value;
        }

        public override Point2f Get2D()
        {
            Float a = Get1D();
            Float b = Get1D();
            return new Point2f(a, b);
        }

        public override Point2f GetPixel2D()
        {
            return Get2D();
        }

        public override Sampler Clone(Allocator alloc = null)
        {
            LOG_FATAL("MLTSampler::Clone() is not implemented");
            return null;
        }

        public void Accept()
        {
            if (largeStep)
                lastLargeStepIteration = currentIteration;
        }

        public string DumpState()
        {
            string state = "";
            for (ulong i = 0; i < X.size(); ++i)
                state += StringPrintf("%f,", X[i].value);
            state += "0";
            return state;
        }

        public override string ToString()
        {
            return StringPrintf(
                "[ MLTSampler rng: %s sigma: %f largeStepProbability: %f " +
                "streamCount: %d X: %s currentIteration: %d largeStep: %s " +
                "lastLargeStepIteration: %d streamIndex: %d sampleIndex: %d ] ",
                rng, sigma, largeStepProbability, streamCount, X, currentIteration, largeStep,
                lastLargeStepIteration, streamIndex, sampleIndex);
        }

        // MLTSampler Private Declarations
        protected struct PrimarySample
        {
            public Float value;
            // PrimarySample Public Methods
            public void Backup()
            {
                valueBackup = value;
                modifyBackup = lastModificationIteration;
            }
            public void Restore()
            {
                value = valueBackup;
                lastModificationIteration = modifyBackup;
            }

            public override string ToString()
            {
                return StringPrintf("[ PrimarySample lastModificationIteration: %d " +
                                    "valueBackup: %f modifyBackup: %d ]",
                                    lastModificationIteration, valueBackup, modifyBackup);
            }

            // PrimarySample Public Members
            public int64_t lastModificationIteration;
            public Float valueBackup;
            public int64_t modifyBackup;
        }

        // MLTSampler Private Methods
        protected void EnsureReady(int index)
        {
            // Enlarge _MLTSampler::X_ if necessary and get current $\VEC{X}_i$
            if ((ulong)index >= X.size())
                X.resize((ulong)(index + 1));
            ref PrimarySample X_i = ref X[index];

            // Reset $\VEC{X}_i$ if a large step took place in the meantime
            if (X_i.lastModificationIteration < lastLargeStepIteration)
            {
                X_i.value = rng.Uniform<Float>();
                X_i.lastModificationIteration = lastLargeStepIteration;
            }

            // Apply remaining sequence of mutations to _sample_
            X_i.Backup();
            if (largeStep)
                X_i.value = rng.Uniform<Float>();
            else
            {
                int64_t nSmall = currentIteration - X_i.lastModificationIteration;
                // Apply _nSmall_ small step mutations to $\VEC{X}_i$
                Float effSigma = sigma * std.sqrt((Float)nSmall);
                Float delta = SampleNormal(rng.Uniform<Float>(), 0, effSigma);
                X_i.value += delta;
                X_i.value -= std.floor(X_i.value);
            }
            X_i.lastModificationIteration = currentIteration;
        }

        // MLTSampler Private Members
        protected int mutationsPerPixel;
        protected RNG rng;
        protected Float sigma, largeStepProbability;
        protected int streamCount;
        protected pstd.vector<PrimarySample> X;
        protected int64_t currentIteration = 0;
        protected bool largeStep = true;
        protected int64_t lastLargeStepIteration = 0;
        protected int streamIndex, sampleIndex;
    }

    public sealed unsafe class DebugMLTSampler : MLTSampler
    {
        public static DebugMLTSampler Create(pstd.span<string> state, int nSampleStreams)
        {
            DebugMLTSampler ds = new DebugMLTSampler(nSampleStreams);
            ds.u.Capacity = (int)state.size();
            for (ulong i = 0; i < state.size(); ++i)
            {
                Float v;
                if (!Atof(state[(int)i], &v))
                    ErrorExit(StringPrintf("Invalid value in --debugstate: %s", state[(int)i]));
                ds.u.Add(v);
            }
            return ds;
        }

        public override Float Get1D()
        {
            int index = GetNextIndex();
            DCHECK_LT(index, u.Count);
            return u[index];
        }

        public override Point2f Get2D() { Float a = Get1D(); Float b = Get1D(); return new Point2f(a, b); }

        public override Point2f GetPixel2D() { return Get2D(); }

        public override string ToString()
        {
            return StringPrintf("[ DebugMLTSampler %s u: %s ]", base.ToString(), u);
        }

        DebugMLTSampler(int nSampleStreams) : base(1, 0, 0.5f, 0.5f, nSampleStreams) { }

        List<Float> u = new List<Float>();
    }

    // Sampler Method Definitions
    public abstract unsafe partial class Sampler
    {
        public static Sampler Create(string name, ParameterDictionary parameters,
                                     Point2i fullRes, FileLoc loc, Allocator alloc)
        {
            Sampler sampler = null;
            if (name == "zsobol")
                sampler = ZSobolSampler.Create(parameters, fullRes, loc, alloc);
            // Create remainder of _Sampler_ types
            else if (name == "paddedsobol")
                sampler = PaddedSobolSampler.Create(parameters, loc, alloc);
            else if (name == "halton")
                sampler = HaltonSampler.Create(parameters, fullRes, loc, alloc);
            else if (name == "sobol")
                sampler = SobolSampler.Create(parameters, fullRes, loc, alloc);
            else if (name == "pmj02bn")
                sampler = PMJ02BNSampler.Create(parameters, loc, alloc);
            else if (name == "independent")
                sampler = IndependentSampler.Create(parameters, loc, alloc);
            else if (name == "stratified")
                sampler = StratifiedSampler.Create(parameters, loc, alloc);
            else
                ErrorExit(loc, StringPrintf("%s: sampler type unknown.", name));
            if (sampler == null)
                ErrorExit(loc, StringPrintf("%s: unable to create sampler.", name));
            parameters.ReportUnused();

            return sampler;
        }
    }

}  // namespace pbrt
