// (not in pbrt-v4) sampledspectrum_test.cs

using System;
using System.Diagnostics;
using testing;

namespace pbrt
{
    using static pbrt;

    public static unsafe class SampledSpectrumTests
    {
        static SampledSpectrum Random(ref RNG rng, Float low, Float high)
        {
            SampledSpectrum s = new SampledSpectrum();
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                s[i] = Lerp(rng.Uniform<Float>(), low, high);
            }

            return s;
        }

        // Every operation against the same written out one value at a time
        [TEST("SampledSpectrum", "SameAsOneValueAtATime")]
        public static void SampledSpectrum_SameAsOneValueAtATime()
        {
            RNG rng = new RNG(3);
            for (int trial = 0; trial < 20000; ++trial)
            {
                SampledSpectrum a = Random(ref rng, -4, 4);
                SampledSpectrum b = Random(ref rng, -4, 4);
                Float t = Lerp(rng.Uniform<Float>(), -3, 3);
                if (trial % 5 == 0)
                {
                    b[trial % NSpectrumSamples] = 0;
                }

                if (trial % 7 == 0)
                {
                    a[(trial / 7) % NSpectrumSamples] = 0;
                }

                SampledSpectrum sum = a + b, difference = a - b, product = a * b, scaled = a * t, scaledLeft = t * a, negated = -a;
                SampledSpectrum fromScalar = t - a, safe = SafeDiv(a, b), clamped = Clamp(a, -1, 2), positive = ClampZero(a);
                SampledSpectrum absolute = Abs(a), squared = Sqr(a), root = Sqrt(Abs(a)), safeRoot = SafeSqrt(a), power = Pow(Abs(a), 1.7f);
                SampledSpectrum exponential = Exp(a), fast = FastExp(a), mixed = Lerp(t, a, b), divided = a / t;
                SampledSpectrum inPlace = a;
                inPlace += b;
                inPlace *= b;
                inPlace -= a;
                inPlace *= t;
                inPlace /= t;
                Float smallest = a[0], largest = a[0], total = 0;
                bool any = false, same = true;
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    EXPECT_EQ(sum[i], a[i] + b[i]);
                    EXPECT_EQ(difference[i], a[i] - b[i]);
                    EXPECT_EQ(product[i], a[i] * b[i]);
                    EXPECT_EQ(scaled[i], a[i] * t);
                    EXPECT_EQ(scaledLeft[i], a[i] * t);
                    EXPECT_EQ(negated[i], -a[i]);
                    EXPECT_EQ(fromScalar[i], t - a[i]);
                    EXPECT_EQ(safe[i], b[i] != 0 ? a[i] / b[i] : 0);
                    EXPECT_EQ(clamped[i], Clamp(a[i], -1, 2));
                    EXPECT_EQ(positive[i], std.max<Float>(0, a[i]));
                    EXPECT_EQ(absolute[i], std.abs(a[i]));
                    EXPECT_EQ(squared[i], a[i] * a[i]);
                    EXPECT_EQ(root[i], std.sqrt(std.abs(a[i])));
                    EXPECT_EQ(safeRoot[i], SafeSqrt(a[i]));
                    EXPECT_EQ(power[i], std.pow(std.abs(a[i]), 1.7f));
                    EXPECT_EQ(exponential[i], std.exp(a[i]));
                    EXPECT_EQ(fast[i], FastExp(a[i]));
                    EXPECT_EQ(mixed[i], (1 - t) * a[i] + t * b[i]);
                    EXPECT_EQ(divided[i], a[i] / t);
                    EXPECT_EQ(inPlace[i], ((a[i] + b[i]) * b[i] - a[i]) * t / t);
                    smallest = std.min(smallest, a[i]);
                    largest = std.max(largest, a[i]);
                    total += a[i];
                    any |= a[i] != 0;
                    same &= a[i] == b[i];
                }

                EXPECT_EQ(a.MinComponentValue(), smallest);
                EXPECT_EQ(a.MaxComponentValue(), largest);
                EXPECT_LT(std.abs(a.Average() - total / NSpectrumSamples), (Float)1e-5);
                EXPECT_EQ((bool)a, any);
                EXPECT_EQ(a == b, same);
                EXPECT_TRUE(a == a);
                EXPECT_FALSE(a.HasNaNs());
            }

            SampledSpectrum zero = new SampledSpectrum(0.0f);
            EXPECT_FALSE((bool)zero);
            SampledSpectrum broken = new SampledSpectrum(1.0f);
            broken[2] = Float.NaN;
            EXPECT_TRUE(broken.HasNaNs());
            SampledSpectrum nonZeroDivisor = new SampledSpectrum(2.0f);
            SampledSpectrum quotient = new SampledSpectrum(3.0f) / nonZeroDivisor;
            EXPECT_EQ(quotient[1], 1.5f);
        }

        [TEST("SampledSpectrum", "Speed")]
        public static void SampledSpectrum_Speed()
        {
            // What the integrators do with a spectrum all the time: throughput times a value, a weighted sum, a safe division,
            // an average
            RNG rng = new RNG(5);
            SampledSpectrum[] values = new SampledSpectrum[1024];
            for (int i = 0; i < values.Length; ++i)
            {
                values[i] = Random(ref rng, 0.25f, 1.5f);
            }

            int iterations = 40000000;
            double best = double.MaxValue;
            Float check = 0;
            for (int run = 0; run < 3; ++run)
            {
                SampledSpectrum beta = new SampledSpectrum(1.0f);
                SampledSpectrum L = new SampledSpectrum(0.0f);
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; ++i)
                {
                    SampledSpectrum f = values[i & 1023];
                    SampledSpectrum pdf = values[(i + 511) & 1023];
                    beta *= f;
                    L += beta * SafeDiv(f, pdf) * 0.5f;
                    beta = beta / beta.MaxComponentValue();
                    if (beta.Average() > 4)
                    {
                        beta = new SampledSpectrum(1.0f);
                    }
                }

                watch.Stop();
                best = Math.Min(best, watch.Elapsed.TotalSeconds * 1e9 / iterations);
                check = L.Average();
            }

            Console.WriteLine(StringPrintf("  %f ns an iteration (sum %g)", best, check));
            EXPECT_GT(check, 0);
        }
    }
}
