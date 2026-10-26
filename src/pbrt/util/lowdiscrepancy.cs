// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

using System;
using System.Runtime.CompilerServices;

namespace pbrt
{
    // DigitPermutation Definition
    public unsafe class DigitPermutation
    {
        // DigitPermutation Public Methods
        public DigitPermutation() { }
        public DigitPermutation(int base_, uint32_t seed, Allocator alloc)
        {
            this.base_ = base_;
            CHECK_LT(base_, 65536);  // uint16_t
            // Compute number of digits needed for _base_
            nDigits = 0;
            Float invBase = (Float)1 / (Float)base_, invBaseM = 1;
            while (1 - (base_ - 1) * invBaseM < 1)
            {
                ++nDigits;
                invBaseM *= invBase;
            }

            permutations = new uint16_t[nDigits * base_];
            // Compute random permutations for all digits
            for (int digitIndex = 0; digitIndex < nDigits; ++digitIndex)
            {
                uint64_t dseed = Globals.Hash(base_, digitIndex, seed);
                for (int digitValue = 0; digitValue < base_; ++digitValue)
                {
                    int index = digitIndex * base_ + digitValue;
                    permutations[index] = (uint16_t)Globals.PermutationElement((uint32_t)digitValue, (uint32_t)base_, (uint32_t)dseed);
                }
            }
        }

        public int Permute(int digitIndex, int digitValue)
        {
            Globals.DCHECK_LT(digitIndex, nDigits);
            Globals.DCHECK_LT(digitValue, base_);
            return permutations[digitIndex * base_ + digitValue];
        }

        public override string ToString()
        {
            string s = $"[ DigitPermitation base: {base_} nDigits: {nDigits} permutations: ";
            for (int digitIndex = 0; digitIndex < nDigits; ++digitIndex)
            {
                s += $"[{digitIndex}] ( ";
                for (int digitValue = 0; digitValue < base_; ++digitValue)
                {
                    s += $"{permutations[digitIndex * base_ + digitValue]}";
                    if (digitValue != base_ - 1)
                        s += ", ";
                }
                s += ") ";
            }

            return s + " ]";
        }

        // DigitPermutation Private Members
        private int base_, nDigits;
        private uint16_t[] permutations;
    }

    // Randomizer interface for the C++ template parameter R of SobolSample().
    public interface IRandomizer
    {
        uint32_t Apply(uint32_t v);
    }

    // NoRandomizer Definition
    public struct NoRandomizer : IRandomizer
    {
        public uint32_t Apply(uint32_t v) { return v; }
    }

    // BinaryPermuteScrambler Definition
    public struct BinaryPermuteScrambler : IRandomizer
    {
        public BinaryPermuteScrambler(uint32_t perm) { permutation = perm; }
        public uint32_t Apply(uint32_t v) { return permutation ^ v; }
        public uint32_t permutation;
    }

    // FastOwenScrambler Definition
    public struct FastOwenScrambler : IRandomizer
    {
        public FastOwenScrambler(uint32_t seed) { this.seed = seed; }
        // FastOwenScrambler Public Methods
        public uint32_t Apply(uint32_t v)
        {
            v = Globals.ReverseBits32(v);
            v ^= v * 0x3d20adea;
            v += seed;
            v *= (seed >> 16) | 1;
            v ^= v * 0x05526c56;
            v ^= v * 0x53a22864;
            return Globals.ReverseBits32(v);
        }

        public uint32_t seed;
    }

    // OwenScrambler Definition
    public struct OwenScrambler : IRandomizer
    {
        public OwenScrambler(uint32_t seed) { this.seed = seed; }
        // OwenScrambler Public Methods
        public uint32_t Apply(uint32_t v)
        {
            if ((seed & 1) != 0)
                v ^= 1u << 31;
            for (int b = 1; b < 32; ++b)
            {
                // Apply Owen scrambling to binary digit _b_ in _v_
                uint32_t mask = (~0u) << (32 - b);
                if (((uint32_t)Globals.MixBits((v & mask) ^ seed) & (1u << b)) != 0)
                    v ^= 1u << (31 - b);
            }
            return v;
        }

        public uint32_t seed;
    }

    // RandomizeStrategy Definition
    public enum RandomizeStrategy { None, PermuteDigits, FastOwen, Owen }

    public static unsafe partial class Globals
    {
        public static string ToString(RandomizeStrategy r)
        {
            switch (r)
            {
                case RandomizeStrategy.None:
                    return "None";
                case RandomizeStrategy.PermuteDigits:
                    return "PermuteDigits";
                case RandomizeStrategy.FastOwen:
                    return "FastOwen";
                case RandomizeStrategy.Owen:
                    return "Owen";
                default:
                    LOG_FATAL("Unhandled RandomizeStrategy");
                    return "";
            }
        }

        // Low Discrepancy Function Definitions
        public static pstd.vector<DigitPermutation> ComputeRadicalInversePermutations(uint32_t seed, Allocator alloc = null)
        {
            pstd.vector<DigitPermutation> perms = new pstd.vector<DigitPermutation>();
            perms.resize(PrimeTableSize);
            for (int i = 0; i < PrimeTableSize; ++i)
                perms[i] = new DigitPermutation(Primes[i], seed, alloc);
            return perms;
        }

        // Low Discrepancy Inline Functions
        public static Float RadicalInverse(int baseIndex, uint64_t a)
        {
            uint base_ = (uint)Primes[baseIndex];
            // We have to stop once reversedDigits is >= limit since otherwise the
            // next digit of |a| may cause reversedDigits to overflow.
            uint64_t limit = ~0UL / base_ - base_;
            Float invBase = (Float)1 / (Float)base_, invBaseM = 1;
            uint64_t reversedDigits = 0;
            while (a != 0 && reversedDigits < limit)
            {
                // Extract least significant digit from _a_ and update _reversedDigits_
                uint64_t next = a / base_;
                uint64_t digit = a - next * base_;
                reversedDigits = reversedDigits * base_ + digit;
                invBaseM *= invBase;
                a = next;
            }
            return std.min(reversedDigits * invBaseM, OneMinusEpsilon);
        }

        public static uint64_t InverseRadicalInverse(uint64_t inverse, int base_, int nDigits)
        {
            uint64_t index = 0;
            for (int i = 0; i < nDigits; ++i)
            {
                uint64_t digit = inverse % (uint64_t)base_;
                inverse /= (uint64_t)base_;
                index = index * (uint64_t)base_ + digit;
            }
            return index;
        }

        public static Float ScrambledRadicalInverse(int baseIndex, uint64_t a, DigitPermutation perm)
        {
            uint base_ = (uint)Primes[baseIndex];
            // We have to stop once reversedDigits is >= limit since otherwise the
            // next digit of |a| may cause reversedDigits to overflow.
            uint64_t limit = ~0UL / base_ - base_;
            Float invBase = (Float)1 / (Float)base_, invBaseM = 1;
            uint64_t reversedDigits = 0;
            int digitIndex = 0;
            while (1 - (base_ - 1) * invBaseM < 1 && reversedDigits < limit)
            {
                // Permute least significant digit from _a_ and update _reversedDigits_
                uint64_t next = a / base_;
                int digitValue = (int)(a - next * base_);
                reversedDigits = reversedDigits * base_ + (uint64_t)perm.Permute(digitIndex, digitValue);
                invBaseM *= invBase;
                ++digitIndex;
                a = next;
            }
            return std.min(invBaseM * reversedDigits, OneMinusEpsilon);
        }

        public static Float OwenScrambledRadicalInverse(int baseIndex, uint64_t a, uint32_t hash)
        {
            uint base_ = (uint)Primes[baseIndex];
            // We have to stop once reversedDigits is >= limit since otherwise the
            // next digit of |a| may cause reversedDigits to overflow.
            uint64_t limit = ~0UL / base_ - base_;
            Float invBase = (Float)1 / (Float)base_, invBaseM = 1;
            uint64_t reversedDigits = 0;
            int digitIndex = 0;
            while (1 - invBaseM < 1 && reversedDigits < limit)
            {
                // Compute Owen-scrambled digit for _digitIndex_
                uint64_t next = a / base_;
                int digitValue = (int)(a - next * base_);
                uint32_t digitHash = (uint32_t)MixBits(hash ^ reversedDigits);
                digitValue = PermutationElement((uint32_t)digitValue, base_, digitHash);
                reversedDigits = reversedDigits * base_ + (uint64_t)digitValue;
                invBaseM *= invBase;
                ++digitIndex;
                a = next;
            }
            return std.min(invBaseM * reversedDigits, OneMinusEpsilon);
        }

        public static uint32_t MultiplyGenerator(pstd.span<uint32_t> C, uint32_t a)
        {
            uint32_t v = 0;
            for (int i = 0; a != 0; ++i, a >>= 1)
                if ((a & 1) != 0)
                    v ^= C[i];
            return v;
        }

        public static Float SobolSample<R>(int64_t a, int dimension, R randomizer) where R : IRandomizer
        {
            DCHECK_LT(dimension, NSobolDimensions);
            DCHECK(a >= 0 && a < (1L << SobolMatrixSize));
            // Compute initial Sobol\+$'$ sample _v_ using generator matrices
            uint32_t v = 0;
            for (int i = dimension * SobolMatrixSize; a != 0; a >>= 1, i++)
                if ((a & 1) != 0)
                    v ^= SobolMatrices32[i];

            // Randomize Sobol\+$'$ sample and return floating-point value
            v = randomizer.Apply(v);
            return std.min(v * 2.3283064365386963e-10f, FloatOneMinusEpsilon);
        }

        private static readonly byte[][] BlueNoiseSample_permutations = new byte[][] {
            new byte[] {0, 1, 2, 3}, new byte[] {0, 1, 3, 2}, new byte[] {0, 2, 1, 3}, new byte[] {0, 2, 3, 1}, new byte[] {0, 3, 2, 1},
            new byte[] {0, 3, 1, 2}, new byte[] {1, 0, 2, 3}, new byte[] {1, 0, 3, 2}, new byte[] {1, 2, 0, 3}, new byte[] {1, 2, 3, 0},
            new byte[] {1, 3, 2, 0}, new byte[] {1, 3, 0, 2}, new byte[] {2, 1, 0, 3}, new byte[] {2, 1, 3, 0}, new byte[] {2, 0, 1, 3},
            new byte[] {2, 0, 3, 1}, new byte[] {2, 3, 0, 1}, new byte[] {2, 3, 1, 0}, new byte[] {3, 1, 2, 0}, new byte[] {3, 1, 0, 2},
            new byte[] {3, 2, 1, 0}, new byte[] {3, 2, 0, 1}, new byte[] {3, 0, 2, 1}, new byte[] {3, 0, 1, 2}};

        public static Float BlueNoiseSample(Point2i p, int instance)
        {
            Func<uint64_t, int> HashPerm = (uint64_t index) =>
            {
                return (int)((uint32_t)(MixBits(index ^ (uint64_t)(0x55555555 * instance)) >> 24) % 24);
            };

            int nBase4Digits = 8;  // Log2Int(256)
            p.x &= 255;
            p.y &= 255;
            uint64_t mortonIndex = EncodeMorton2((uint32_t)p.x, (uint32_t)p.y);

            uint32_t sampleIndex = 0;
            for (int i = nBase4Digits - 1; i >= 0; --i)
            {
                int digitShift = 2 * i;
                int digit = (int)((mortonIndex >> digitShift) & 3);
                int pp = HashPerm(mortonIndex >> (digitShift + 2));
                digit = BlueNoiseSample_permutations[pp][digit];
                sampleIndex |= (uint32_t)(digit << digitShift);
            }

            return ReverseBits32(sampleIndex) * 2.3283064365386963e-10f;
        }

        public static uint64_t SobolIntervalToIndex(uint32_t m, uint64_t frame, Point2i p)
        {
            if (m == 0)
                return frame;

            uint32_t m2 = m << 1;
            uint64_t index = (uint64_t)frame << (int)m2;

            uint64_t delta = 0;
            for (int c = 0; frame != 0; frame >>= 1, ++c)
                if ((frame & 1) != 0)  // Add flipped column m + c + 1.
                    delta ^= VdCSobolMatrices[m - 1][c];

            // flipped b
            uint64_t b = (((uint64_t)((uint32_t)p.x) << (int)m) | ((uint32_t)p.y)) ^ delta;

            for (int c = 0; b != 0; b >>= 1, ++c)
                if ((b & 1) != 0)  // Add column 2 * m - c.
                    index ^= VdCSobolMatricesInv[m - 1][c];

            return index;
        }
    }
}
