// math_test.cpp
//
// Differences from the C++ test:
//  - Pow<N>(x) is the runtime Pow(n, x) in the port.
//  - CompensatedSum.Basics: the "long double" accumulator is not available in .NET, so the
//    ldSum comparison (guarded by sizeof(long double) > sizeof(double) in C++) is skipped.
//  - SquareMatrix<N> is expanded by hand as SquareMatrix2/3/4, so the N-templated
//    randomMatrix<N>() and the Inverse test are written out per size.
//  - EvaluatePolynomial is Float-only in the port (templated on the scalar type in C++).

using System;
using testing;

namespace pbrt
{
    public static unsafe class MathTests
    {
        [TEST("Pow2", "Basics")]
        public static void Pow2_Basics()
        {
            for (int i = 0; i < 32; ++i)
            {
                uint32_t ui = 1u << i;
                EXPECT_EQ(true, IsPowerOf2(ui));
                if (ui > 1)
                {
                    EXPECT_EQ(false, IsPowerOf2(ui + 1));
                }
                if (ui > 2)
                {
                    EXPECT_EQ(false, IsPowerOf2(ui - 1));
                }
            }
        }

        [TEST("RoundUpPow2", "Basics")]
        public static void RoundUpPow2_Basics()
        {
            EXPECT_EQ(RoundUpPow2(7), 8);
            for (int i = 1; i < (1 << 24); ++i)
                if (IsPowerOf2(i))
                    EXPECT_EQ(RoundUpPow2(i), i);
                else
                    EXPECT_EQ(RoundUpPow2(i), 1 << (Log2Int(i) + 1));

            for (int64_t i = 1; i < (1 << 24); ++i)
                if (IsPowerOf2(i))
                    EXPECT_EQ(RoundUpPow2(i), i);
                else
                    EXPECT_EQ(RoundUpPow2(i), 1 << (Log2Int(i) + 1));

            for (int i = 0; i < 30; ++i)
            {
                int v = 1 << i;
                EXPECT_EQ(RoundUpPow2(v), v);
                if (v > 2)
                {
                    EXPECT_EQ(RoundUpPow2(v - 1), v);
                }
                EXPECT_EQ(RoundUpPow2(v + 1), 2 * v);
            }

            for (int i = 0; i < 62; ++i)
            {
                int64_t v = 1L << i;
                EXPECT_EQ(RoundUpPow2(v), v);
                if (v > 2)
                {
                    EXPECT_EQ(RoundUpPow2(v - 1), v);
                }
                EXPECT_EQ(RoundUpPow2(v + 1), 2 * v);
            }
        }

        [TEST("Morton2", "Basics")]
        public static void Morton2_Basics()
        {
            uint16_t x = 0b01010111, y = 0b11000101;
            uint32_t m = (uint32_t)EncodeMorton2(x, y);
            EXPECT_EQ(m, 0b1011000100110111);

            RNG rng = new RNG(12351);
            for (int i = 0; i < 100000; ++i)
            {
                uint32_t xx = rng.Uniform<uint32_t>();
                uint32_t yy = rng.Uniform<uint32_t>();
                uint64_t mm = EncodeMorton2(xx, yy);

                uint32_t xp, yp;
                DecodeMorton2(mm, &xp, &yp);
                EXPECT_EQ(xx, xp);
                EXPECT_EQ(yy, yp);
            }
        }

        [TEST("Math", "Pow")]
        public static void Math_Pow()
        {
            for (int i = 0; i <= 29; ++i)
                EXPECT_EQ(Pow(i, 2.0f), 1 << i);
        }

        [TEST("Math", "NewtonBisection")]
        public static void Math_NewtonBisection()
        {
            EXPECT_FLOAT_EQ(1, NewtonBisection(0, 10, (Float x) => ((Float)(-1 + x), (Float)1)));
            EXPECT_FLOAT_EQ(Pi / 2, NewtonBisection(0, 2, (Float x) => ((Float)std.cos(x), (Float)(-std.sin(x)))));

            // The derivative is a lie--pointing in the wrong direction, even--but
            // it should still work.
            Float bad = NewtonBisection(0, 2, (Float x) => ((Float)std.cos(x), (Float)(10 * std.sin(x))));
            EXPECT_LT(std.abs(Pi / 2 - bad), 1e-5);

            // Multiple zeros in the domain; make sure we find one.
            Float zero = NewtonBisection(.1f, 10.1f, (Float x) => ((Float)std.sin(x), (Float)std.cos(x)));
            EXPECT_LT(std.abs(std.sin(zero)), 1e-6);

            // Ill-behaved function with derivatives that go to infinity (and also
            // multiple zeros).
            Func<Float, (Float first, Float second)> f = (Float x) =>
                ((Float)(std.pow(Sqr(std.sin(x)), .05) - 0.3),
                 (Float)(0.1 * std.cos(x) * std.sin(x) / std.pow(Sqr(std.sin(x)), 0.95)));
            zero = NewtonBisection(.01f, 9.42477798f, f);
            // Extra slop for a messy function.
            EXPECT_LT(std.abs(f(zero).first), 1e-2);
        }

        [TEST("Math", "EvaluatePolynomial")]
        public static void Math_EvaluatePolynomial()
        {
            EXPECT_EQ(4, EvaluatePolynomial(100, 4));
            EXPECT_EQ(10, EvaluatePolynomial(2, 4, 3));

            EXPECT_EQ(1.5 + 2.75 * .5 - 4.25 * Pow(2, .5) + 15.125 * Pow(3, .5),
                      EvaluatePolynomial(.5f, 1.5f, 2.75f, -4.25f, 15.125f));
        }

        [TEST("Math", "CompensatedSum")]
        public static void Math_CompensatedSum()
        {
            // In order of decreasing accuracy...
            CompensatedSum<double> kahanSumD = new CompensatedSum<double>();
            double doubleSum = 0;
            CompensatedSum<float> kahanSumF = new CompensatedSum<float>();
            float floatSum = 0;

            RNG rng = new RNG();
            for (int i = 0; i < 16 * 1024 * 1024; ++i)
            {
                // Hard to sum accurately since the values span many magnitudes.
                float v = (float)std.exp(Lerp(rng.Uniform<Float>(), -5, 20));
                kahanSumD += (double)v;
                doubleSum += v;
                kahanSumF += v;
                floatSum += v;
            }

            int64_t kahanDBits = (int64_t)FloatToBits((double)kahanSumD);
            int64_t doubleBits = (int64_t)FloatToBits(doubleSum);
            int64_t kahanFBits = (int64_t)FloatToBits((double)(float)kahanSumF);
            int64_t floatBits = (int64_t)FloatToBits((double)floatSum);

            int64_t doubleErrorUlps = Math.Abs(doubleBits - kahanDBits);
            int64_t kahanFErrorUlps = Math.Abs(kahanFBits - kahanDBits);
            int64_t floatErrorUlps = Math.Abs(floatBits - kahanDBits);

            // (The long double comparison is skipped; see the note at the top.)
            // Less slop between double and Kahan with floats.
            EXPECT_LT(doubleErrorUlps * 1000, kahanFErrorUlps);
            EXPECT_LT(kahanFErrorUlps * 10000, floatErrorUlps);
        }

        [TEST("Log2Int", "Basics")]
        public static void Log2Int_Basics()
        {
            for (int i = 0; i < 32; ++i)
            {
                uint32_t ui = 1u << i;
                EXPECT_EQ(i, Log2Int(ui));
                EXPECT_EQ(i, Log2Int((uint64_t)ui));
            }

            for (int i = 1; i < 31; ++i)
            {
                uint32_t ui = 1u << i;
                EXPECT_EQ(i, Log2Int(ui + 1));
                EXPECT_EQ(i, Log2Int((uint64_t)(ui + 1)));
            }

            for (int i = 0; i < 64; ++i)
            {
                uint64_t ui = 1ul << i;
                EXPECT_EQ(i, Log2Int(ui));
            }

            for (int i = 1; i < 64; ++i)
            {
                uint64_t ui = 1ul << i;
                EXPECT_EQ(i, Log2Int(ui + 1));
            }

            // Exact powers of two
            for (int i = 120; i >= -120; --i)
                EXPECT_EQ(i, Log2Int((float)Math.Pow(2.0, i)));
            for (int i = 120; i >= -120; --i)
                EXPECT_EQ(i, Log2Int(Math.Pow(2.0, i)));

            // Specifically exercise round to nearest even stuff for the Float version
            for (int i = -31; i < 31; ++i)
            {
                float vlow = (float)Math.Pow(2.0, (double)(i + 0.499f));
                EXPECT_EQ(i, Log2Int(vlow));
                float vhigh = (float)Math.Pow(2.0, (double)(i + 0.501f));
                EXPECT_EQ(i + 1, Log2Int(vhigh));
            }
            for (int i = -31; i < 31; ++i)
            {
                double vlow = Math.Pow(2.0, i + 0.4999999);
                EXPECT_EQ(i, Log2Int(vlow));
                double vhigh = Math.Pow(2.0, i + 0.5000001);
                EXPECT_EQ(i + 1, Log2Int(vhigh));
            }
        }

        [TEST("Log4Int", "Basics")]
        public static void Log4Int_Basics()
        {
            EXPECT_EQ(0, Log4Int(1));
            EXPECT_EQ(1, Log4Int(4));
            EXPECT_EQ(2, Log4Int(16));
            EXPECT_EQ(3, Log4Int(64));

            int log4 = 0, next = 4;
            for (int v = 1; v < 16385; ++v)
            {
                if (v == next)
                {
                    ++log4;
                    next *= 4;
                }
                EXPECT_EQ(log4, Log4Int(v));
            }
        }

        [TEST("Pow4", "Basics")]
        public static void Pow4_Basics()
        {
            for (int i = 0; i < 12; ++i)
            {
                int p4 = 1 << (2 * i);
                EXPECT_TRUE(IsPowerOf4(p4));
                EXPECT_EQ(p4, RoundUpPow4(p4));
                if (i > 0)
                {
                    EXPECT_EQ(p4, RoundUpPow4(p4 - 1));
                }
                EXPECT_EQ(p4 * 4, RoundUpPow4(p4 + 1));
            }
        }

        [TEST("NextPrime", "Basics")]
        public static void NextPrime_Basics()
        {
            EXPECT_EQ(3, NextPrime(2));
            EXPECT_EQ(11, NextPrime(10));
            EXPECT_EQ(37, NextPrime(32));
            EXPECT_EQ(37, NextPrime(37));

            Func<int, bool> isPrime = (int x) =>
            {
                if ((x & 1) == 0)
                    return false;
                for (int i = 3; i < std.sqrt((double)x) + 1; i += 3)
                {
                    if ((x % i) == 0)
                        return false;
                }
                return true;
            };

            for (int n = 3; n < 8000; n += 3)
            {
                if (isPrime(n))
                {
                    EXPECT_EQ(n, NextPrime(n));
                }
                else
                {
                    int np = NextPrime(n);
                    EXPECT_TRUE(isPrime(np));
                }
            }
        }

        [TEST("Math", "ErfInv")]
        public static void Math_ErfInv()
        {
            float[] xvl = { 0.0f, 0.1125f, 0.25f, .753f, 1.521f, 2.5115f };
            foreach (float x in xvl)
            {
                Float e = std.erf(x);
                if (e < 1)
                {
                    Float ei = ErfInv(e);
                    if (x == 0)
                        EXPECT_EQ(0, ei);
                    else
                    {
                        Float err = std.abs(ei - x) / x;
                        EXPECT_LT(err, 1e-4, x + " erf " + e + " inv " + ei);
                    }
                }
            }
        }

        // The next two expect a higher-precision option to verify with.
        [TEST("Math", "DifferenceOfProducts")]
        public static void Math_DifferenceOfProducts()
        {
            for (int i = 0; i < 100000; ++i)
            {
                RNG rng = new RNG((uint64_t)i);
                Func<Float> r = () =>
                {
                    Float logu = Lerp(rng.Uniform<Float>(), -8, 8);
                    return (Float)std.pow(10, logu);
                };
                Float a = r(), b = r(), c = r(), d = r();
                Float sign = rng.Uniform<Float>() < -0.5 ? -1 : 1;
                b *= sign;
                c *= sign;
                Float dp = DifferenceOfProducts(a, b, c, d);
                Float dp2 = (Float)FMA((double)a, (double)b, -(double)c * (double)d);
                Float err = std.abs(dp - dp2);
                Float ulp = NextFloatUp(std.abs(dp2)) - std.abs(dp2);
                EXPECT_LT(err, 2 * ulp);
            }
        }

        [TEST("Math", "SumOfProducts")]
        public static void Math_SumOfProducts()
        {
            for (int i = 0; i < 100000; ++i)
            {
                RNG rng = new RNG((uint64_t)i);
                Func<Float> r = () =>
                {
                    Float logu = Lerp(rng.Uniform<Float>(), -8, 8);
                    return (Float)std.pow(10, logu);
                };
                // Make sure mixed signs...
                Float a = r(), b = r(), c = r(), d = -r();
                Float sign = rng.Uniform<Float>() < -0.5 ? -1 : 1;
                b *= sign;
                c *= sign;
                Float sp = SumOfProducts(a, b, c, d);
                Float sp2 = (Float)FMA((double)a, (double)b, (double)c * (double)d);
                Float err = std.abs(sp - sp2);
                Float ulp = NextFloatUp(std.abs(sp2)) - std.abs(sp2);
                EXPECT_LT(err, 2 * ulp);
            }
        }

        [TEST("FastExp", "Accuracy")]
        public static void FastExp_Accuracy()
        {
            EXPECT_EQ(1, FastExp(0));

            Float maxErr = 0;
            RNG rng = new RNG(6502);
            for (int i = 0; i < 100; ++i)
            {
                Float v = Lerp(rng.Uniform<Float>(), -20.0f, 20.0f);
                Float f = FastExp(v);
                Float e = (Float)std.exp(v);
                Float err = std.abs((f - e) / e);
                maxErr = std.max(err, maxErr);
                EXPECT_LE(err, 0.0003f, "At " + v + ", fast = " + f + ", accurate = " + e +
                                        " -> relative error = " + err);
            }
        }

        [TEST("Math", "GaussianIntegral")]
        public static void Math_GaussianIntegral()
        {
            Float[][] muSigma = { new Float[] { 0, 1 }, new Float[] { 0, 2 }, new Float[] { 0, .1f },
                                  new Float[] { 1, 2 }, new Float[] { -2, 1 } };
            for (int i = 0; i < muSigma.Length; ++i)
            {
                RNG rng = new RNG();
                for (int j = 0; j < 5; ++j)
                {
                    Float x0 = -5 + 10 + rng.Uniform<Float>();
                    Float x1 = -5 + 10 + rng.Uniform<Float>();
                    if (x0 > x1)
                        pstd.swap(ref x0, ref x1);

                    Float mu = muSigma[i][0], sigma = muSigma[i][1];
                    Float sum = 0;
                    int n = 8192;
                    for (int k = 0; k < n; ++k)
                    {
                        Float u = (k + rng.Uniform<Float>()) / n;
                        Float x = Lerp(u, x0, x1);
                        sum += Gaussian(x, mu, sigma);
                    }
                    Float est = (x1 - x0) * sum / n;

                    Func<Float, Float, bool> compareFloats = (Float rf, Float v) =>
                    {
                        if (std.abs(rf) < 1e-4)
                            return std.abs(rf - v) < 1e-5;
                        return std.abs((rf - v) / rf) < 1e-3;
                    };
                    Float inn = GaussianIntegral(x0, x1, mu, sigma);
                    EXPECT_TRUE(compareFloats(est, inn), est + " vs " + inn);
                }
            }
        }

        [TEST("SquareMatrix", "Basics2")]
        public static void SquareMatrix_Basics2()
        {
            SquareMatrix2 m2 = new SquareMatrix2();

            EXPECT_TRUE(m2.IsIdentity());

            EXPECT_EQ(m2, new SquareMatrix2(1, 0, 0, 1));
            EXPECT_NE(m2, new SquareMatrix2(0, 1, 1, 0));

            EXPECT_EQ(new SquareMatrix2(2, 0, 0, -1), SquareMatrix2.Diag(2, -1));

            SquareMatrix2 m = new SquareMatrix2(1, 2, 3, 4);
            EXPECT_FALSE(m.IsIdentity());
            SquareMatrix2 mt = new SquareMatrix2(1, 3, 2, 4);
            EXPECT_EQ(Transpose(m), mt);

            pstd.array2<Float> v = new pstd.array2<Float>(1, -2);
            pstd.array2<Float> vt = m * v;
            EXPECT_EQ(1 - 2 * 2, vt[0]);
            EXPECT_EQ(3 - 4 * 2, vt[1]);

            Vector2f v2 = new Vector2f(1, -2);
            Vector2f v2t = m * v2;
            EXPECT_EQ(1 - 2 * 2, v2t[0]);
            EXPECT_EQ(3 - 4 * 2, v2t[1]);

            pstd.optional<SquareMatrix2> inv = Inverse(m2);
            EXPECT_TRUE(inv.has_value());
            EXPECT_EQ(m2, inv.value());

            SquareMatrix2 ms = new SquareMatrix2(2, 4, -4, 8);
            inv = Inverse(ms);
            EXPECT_TRUE(inv.has_value());
            EXPECT_EQ(new SquareMatrix2(1.0f / 4.0f, -1.0f / 8.0f, 1.0f / 8.0f, 1.0f / 16.0f), inv.value());

            SquareMatrix2 degen = new SquareMatrix2(0, 0, 2, 0);
            inv = Inverse(degen);
            EXPECT_FALSE(inv.has_value());
        }

        [TEST("SquareMatrix", "Basics3")]
        public static void SquareMatrix_Basics3()
        {
            SquareMatrix3 m3 = new SquareMatrix3();
            EXPECT_TRUE(m3.IsIdentity());

            EXPECT_EQ(m3, new SquareMatrix3(1, 0, 0, 0, 1, 0, 0, 0, 1));
            EXPECT_NE(m3, new SquareMatrix3(0, 1, 0, 0, 1, 0, 0, 0, 1));

            EXPECT_EQ(new SquareMatrix3(2, 0, 0, 0, -1, 0, 0, 0, 3), SquareMatrix3.Diag(2, -1, 3));

            SquareMatrix3 m = new SquareMatrix3(1, 2, 3, 4, 5, 6, 7, 8, 9);
            EXPECT_FALSE(m.IsIdentity());
            SquareMatrix3 mt = new SquareMatrix3(1, 4, 7, 2, 5, 8, 3, 6, 9);
            EXPECT_EQ(Transpose(m), mt);

            pstd.array3<Float> v = new pstd.array3<Float>(1, -2, 4);
            pstd.array3<Float> vt = m * v;
            EXPECT_EQ(1 - 4 + 12, vt[0]);
            EXPECT_EQ(4 - 10 + 24, vt[1]);
            EXPECT_EQ(7 - 16 + 36, vt[2]);

            Vector3f v3 = new Vector3f(1, -2, 4);
            Vector3f v3t = m * v3;
            EXPECT_EQ(1 - 4 + 12, v3t[0]);
            EXPECT_EQ(4 - 10 + 24, v3t[1]);
            EXPECT_EQ(7 - 16 + 36, v3t[2]);

            pstd.optional<SquareMatrix3> inv = Inverse(m3);
            EXPECT_TRUE(inv.has_value());
            EXPECT_EQ(m3, inv.value());

            SquareMatrix3 ms = new SquareMatrix3(2, 0, 0, 0, 4, 0, 0, 0, -1);
            inv = Inverse(ms);
            EXPECT_TRUE(inv.has_value());
            EXPECT_EQ(new SquareMatrix3(0.5f, 0, 0, 0, .25f, 0, 0, 0, -1), inv.value());

            SquareMatrix3 degen = new SquareMatrix3(0, 0, 2, 0, 0, 0, 1, 1, 1);
            inv = Inverse(degen);
            EXPECT_FALSE(inv.has_value());
        }

        [TEST("SquareMatrix", "Basics4")]
        public static void SquareMatrix_Basics4()
        {
            SquareMatrix4 m4 = new SquareMatrix4();
            EXPECT_TRUE(m4.IsIdentity());

            EXPECT_EQ(m4, new SquareMatrix4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1));
            EXPECT_NE(m4, new SquareMatrix4(0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0));

            SquareMatrix4 diag = new SquareMatrix4(8, 0, 0, 0, 0, 2, 0, 0, 0, 0, 1, 0, 0, 0, 0, .5f);
            EXPECT_EQ(diag, SquareMatrix4.Diag(8, 2, 1, .5f));

            SquareMatrix4 m = new SquareMatrix4(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
            EXPECT_FALSE(m.IsIdentity());
            SquareMatrix4 mt = new SquareMatrix4(1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15, 4, 8, 12, 16);
            EXPECT_EQ(Transpose(m), mt);

            pstd.optional<SquareMatrix4> inv = Inverse(m4);
            EXPECT_TRUE(inv.has_value());
            EXPECT_EQ(m4, inv.value());

            inv = Inverse(diag);
            EXPECT_TRUE(inv.has_value());
            EXPECT_EQ(SquareMatrix4.Diag(.125f, .5f, 1, 2), inv.value());

            SquareMatrix4 degen = new SquareMatrix4(2, 0, 0, 0, 0, 4, 0, 0, 0, -3, 0, 1, 0, 0, 0, 0);
            inv = Inverse(degen);
            EXPECT_FALSE(inv.has_value());
        }

        [TEST("SquareMatrix", "Inverse")]
        public static void SquareMatrix_Inverse()
        {
            Func<Float, Float, Float, bool> equal = (Float a, Float b, Float tol) =>
            {
                if (std.abs(a) < 1e-5 || std.abs(b) < 1e-5)
                    return std.abs(a) - std.abs(b) < tol;
                return (std.abs(a) - std.abs(b)) / ((std.abs(a) + std.abs(b)) / 2) < tol;
            };

            int nFail = 0;
            int nIters = 1000;
            {
                const int N = 2;
                for (int i = 0; i < nIters; ++i)
                {
                    RNG rng = new RNG((uint64_t)i);
                    SquareMatrix2 m = new SquareMatrix2();
                    for (int a = 0; a < N; ++a)
                        for (int b = 0; b < N; ++b)
                            m[a][b] = -10 + 20 * rng.Uniform<Float>();
                    pstd.optional<SquareMatrix2> inv = Inverse(m);
                    if (!inv.has_value())
                    {
                        ++nFail;
                        continue;
                    }
                    SquareMatrix2 id = m * inv.value();

                    for (int j = 0; j < N; ++j)
                        for (int k = 0; k < N; ++k)
                        {
                            if (j == k)
                                EXPECT_TRUE(equal(id[j][k], 1, 1e-4f), m + ", inv " + inv.value() + " prod " + id);
                            else
                                EXPECT_LT(std.abs(id[j][k]), 1e-4, m + ", inv " + inv.value() + " prod " + id);
                        }
                }
            }
            {
                const int N = 3;
                for (int i = 0; i < nIters; ++i)
                {
                    RNG rng = new RNG((uint64_t)i);
                    SquareMatrix3 m = new SquareMatrix3();
                    for (int a = 0; a < N; ++a)
                        for (int b = 0; b < N; ++b)
                            m[a][b] = -10 + 20 * rng.Uniform<Float>();
                    pstd.optional<SquareMatrix3> inv = Inverse(m);
                    if (!inv.has_value())
                    {
                        ++nFail;
                        continue;
                    }
                    SquareMatrix3 id = m * inv.value();

                    for (int j = 0; j < N; ++j)
                        for (int k = 0; k < N; ++k)
                        {
                            if (j == k)
                                EXPECT_TRUE(equal(id[j][k], 1, 1e-4f), m + ", inv " + inv.value() + " prod " + id);
                            else
                                EXPECT_LT(std.abs(id[j][k]), 1e-4, m + ", inv " + inv.value() + " prod " + id);
                        }
                }
            }
            {
                const int N = 4;
                for (int i = 0; i < nIters; ++i)
                {
                    RNG rng = new RNG((uint64_t)i);
                    SquareMatrix4 m = new SquareMatrix4();
                    for (int a = 0; a < N; ++a)
                        for (int b = 0; b < N; ++b)
                            m[a][b] = -10 + 20 * rng.Uniform<Float>();
                    pstd.optional<SquareMatrix4> inv = Inverse(m);
                    if (!inv.has_value())
                    {
                        ++nFail;
                        continue;
                    }
                    SquareMatrix4 id = m * inv.value();

                    for (int j = 0; j < N; ++j)
                        for (int k = 0; k < N; ++k)
                        {
                            if (j == k)
                                EXPECT_TRUE(equal(id[j][k], 1, 1e-4f), m + ", inv " + inv.value() + " prod " + id);
                            else
                                EXPECT_LT(std.abs(id[j][k]), 1e-4, m + ", inv " + inv.value() + " prod " + id);
                        }
                }
            }

            EXPECT_LT(nFail, 3);
        }

        [TEST("FindInterval", "Basics")]
        public static void FindInterval_Basics()
        {
            float[] a = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            // Check clamping for out of range
            EXPECT_EQ(0, FindInterval((size_t)a.Length, (int index) => a[index] <= -1));
            EXPECT_EQ(a.Length - 2, FindInterval((size_t)a.Length, (int index) => a[index] <= 100));

            for (size_t i = 0; i < (size_t)a.Length - 1; ++i)
            {
                size_t ii = i;
                EXPECT_EQ(ii, FindInterval((size_t)a.Length, (int index) => a[index] <= ii));
                EXPECT_EQ(ii, FindInterval((size_t)a.Length, (int index) => a[index] <= ii + 0.5));
                if (ii > 0)
                    EXPECT_EQ(ii - 1, FindInterval((size_t)a.Length, (int index) => a[index] <= ii - 0.5));
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        // Interval tests

        // Return an exponentially-distributed floating-point value.
        private static Interval getFloat(ref RNG rng, Float minExp = -6.0f, Float maxExp = 6.0f)
        {
            Float logu = Lerp(rng.Uniform<Float>(), minExp, maxExp);
            Float val = (Float)std.pow(10, logu);

            // Choose a random error bound.
            Float err = 0;
            switch (rng.Uniform<uint32_t>(4))
            {
                case 0:
                    // no error
                    break;
                case 1:
                    {
                        // small typical/reasonable error
                        uint32_t ulpError = rng.Uniform<uint32_t>(1024);
                        Float offset = BitsToFloat(FloatToBits(val) + ulpError);
                        err = std.abs(offset - val);
                        break;
                    }
                case 2:
                    {
                        // bigger ~reasonable error
                        uint32_t ulpError = rng.Uniform<uint32_t>(1024 * 1024);
                        Float offset = BitsToFloat(FloatToBits(val) + ulpError);
                        err = std.abs(offset - val);
                        break;
                    }
                case 3:
                    {
                        err = (4 * rng.Uniform<Float>()) * std.abs(val);
                        break;
                    }
            }
            Float sign = rng.Uniform<Float>() < .5 ? -1.0f : 1.0f;
            return Interval.FromValueAndError(sign * val, err);
        }

        // Given an Interval covering some range, choose a double-precision
        // "precise" value that is in the Interval's range.
        private static double getPrecise(Interval ef, ref RNG rng)
        {
            switch (rng.Uniform<uint32_t>(3))
            {
                // 2/3 of the time, pick a value that is right at the end of the range;
                // this is a maximally difficult / adversarial choice, so should help
                // ferret out any bugs.
                case 0:
                    return ef.LowerBound();
                case 1:
                    return ef.UpperBound();
                case 2:
                    {
                        // Otherwise choose a value uniformly inside the Interval's range.
                        Float t = rng.Uniform<Float>();
                        double p = (1 - t) * ef.LowerBound() + t * ef.UpperBound();
                        if (p > ef.UpperBound())
                            p = ef.UpperBound();
                        if (p < ef.LowerBound())
                            p = ef.LowerBound();
                        return p;
                    }
            }
            return (Float)ef;  // NOTREACHED
        }

        private const int kFloatIntervalIters = 1000000;

        [TEST("FloatInterval", "Abs")]
        public static void FloatInterval_Abs()
        {
            for (int trial = 0; trial < kFloatIntervalIters; ++trial)
            {
                RNG rng = new RNG((uint64_t)trial);

                Interval ef = getFloat(ref rng);
                double precise = getPrecise(ef, ref rng);

                Interval efResult = Abs(ef);
                double preciseResult = std.abs(precise);

                EXPECT_GE(preciseResult, efResult.LowerBound());
                EXPECT_LE(preciseResult, efResult.UpperBound());
            }
        }

        [TEST("FloatInterval", "Sqrt")]
        public static void FloatInterval_Sqrt()
        {
            for (int trial = 0; trial < kFloatIntervalIters; ++trial)
            {
                RNG rng = new RNG((uint64_t)trial);

                Interval ef = getFloat(ref rng);
                double precise = getPrecise(ef, ref rng);

                Interval efResult = Sqrt(Abs(ef));
                double preciseResult = std.sqrt(std.abs(precise));

                EXPECT_GE(preciseResult, efResult.LowerBound());
                EXPECT_LE(preciseResult, efResult.UpperBound());
            }
        }

        [TEST("FloatInterval", "Add")]
        public static void FloatInterval_Add()
        {
            for (int trial = 0; trial < kFloatIntervalIters; ++trial)
            {
                RNG rng = new RNG((uint64_t)trial);

                Interval ef0 = getFloat(ref rng), ef1 = getFloat(ref rng);
                double p0 = getPrecise(ef0, ref rng), p1 = getPrecise(ef1, ref rng);

                Interval efResult = ef0 + ef1;
                float preciseResult = (float)(p0 + p1);

                EXPECT_GE(preciseResult, efResult.LowerBound());
                EXPECT_LE(preciseResult, efResult.UpperBound());
            }
        }

        [TEST("FloatInterval", "Sub")]
        public static void FloatInterval_Sub()
        {
            for (int trial = 0; trial < kFloatIntervalIters; ++trial)
            {
                RNG rng = new RNG((uint64_t)trial);

                Interval ef0 = getFloat(ref rng), ef1 = getFloat(ref rng);
                double p0 = getPrecise(ef0, ref rng), p1 = getPrecise(ef1, ref rng);

                Interval efResult = ef0 - ef1;
                float preciseResult = (float)(p0 - p1);

                EXPECT_GE(preciseResult, efResult.LowerBound());
                EXPECT_LE(preciseResult, efResult.UpperBound());
            }
        }

        [TEST("FloatInterval", "Mul")]
        public static void FloatInterval_Mul()
        {
            for (int trial = 0; trial < kFloatIntervalIters; ++trial)
            {
                RNG rng = new RNG((uint64_t)trial);

                Interval ef0 = getFloat(ref rng), ef1 = getFloat(ref rng);
                double p0 = getPrecise(ef0, ref rng), p1 = getPrecise(ef1, ref rng);

                Interval efResult = ef0 * ef1;
                float preciseResult = (float)(p0 * p1);

                EXPECT_GE(preciseResult, efResult.LowerBound());
                EXPECT_LE(preciseResult, efResult.UpperBound());
            }
        }

        [TEST("FloatInterval", "Div")]
        public static void FloatInterval_Div()
        {
            for (int trial = 0; trial < kFloatIntervalIters; ++trial)
            {
                RNG rng = new RNG((uint64_t)trial);

                Interval ef0 = getFloat(ref rng), ef1 = getFloat(ref rng);
                double p0 = getPrecise(ef0, ref rng), p1 = getPrecise(ef1, ref rng);

                // Things get messy if the denominator's interval straddles zero...
                if (ef1.LowerBound() * ef1.UpperBound() < 0.0)
                    continue;

                Interval efResult = ef0 / ef1;
                float preciseResult = (float)(p0 / p1);

                EXPECT_GE(preciseResult, efResult.LowerBound());
                EXPECT_LE(preciseResult, efResult.UpperBound());
            }
        }

        [TEST("FloatInterval", "FMA")]
        public static void FloatInterval_FMA()
        {
            int nTrials = 10000, nIters = 400;
            int ratioCount = 0;
            int nBetter = 0;
            for (int i = 0; i < nTrials; ++i)
            {
                RNG rng = new RNG((uint64_t)i);
                Interval v = Abs(getFloat(ref rng));
                for (int j = 0; j < nIters; ++j)
                {
                    Interval a = v;
                    Interval b = getFloat(ref rng);
                    Interval c = getFloat(ref rng);

                    v = FMA(a, b, c);

                    if (std.isinf(v.LowerBound()) || std.isinf(v.UpperBound()))
                        break;

                    double pa = getPrecise(a, ref rng);
                    double pb = getPrecise(b, ref rng);
                    double pc = getPrecise(c, ref rng);
                    float preciseResult = (float)FMA(pa, pb, pc);
                    EXPECT_GE(preciseResult, v.LowerBound(), v.ToString());
                    EXPECT_LE(preciseResult, v.UpperBound(), v.ToString());

                    Interval vp = a * b + c;
                    EXPECT_GE(v.LowerBound(), vp.LowerBound(), v + " vs " + vp);
                    EXPECT_LE(v.UpperBound(), vp.UpperBound(), v + " vs " + vp);

                    nBetter += (v.LowerBound() > vp.LowerBound() || v.UpperBound() < vp.UpperBound()) ? 1 : 0;
                }
            }

            EXPECT_GT(nBetter, .85 * ratioCount);
        }

        [TEST("FloatInterval", "Sqr")]
        public static void FloatInterval_Sqr()
        {
            Interval a = new Interval(1.75f, 2.25f);
            Interval aas = Sqr(a), at = a * a;
            EXPECT_EQ(aas.UpperBound(), at.UpperBound());
            EXPECT_EQ(aas.LowerBound(), at.LowerBound());

            // Straddle 0
            Interval b = new Interval(-.75f, 1.25f);
            Interval bs = Sqr(b), b2 = b * b;
            EXPECT_EQ(bs.UpperBound(), b2.UpperBound());
            EXPECT_EQ(0, bs.LowerBound());
            EXPECT_LT(b2.LowerBound(), 0);
        }

        [TEST("FloatInterval", "SumSquares")]
        public static void FloatInterval_SumSquares()
        {
            {
                Interval a = new Interval(1), b = new Interval(2), c = new Interval(3);
                EXPECT_EQ(1, (Float)SumSquares(a));
                EXPECT_EQ(4, (Float)SumSquares(b));
                EXPECT_EQ(5, (Float)SumSquares(a, b));
                EXPECT_EQ(14, (Float)SumSquares(a, b, c));
            }
        }

        [TEST("FloatInterval", "DifferenceOfProducts")]
        public static void FloatInterval_DifferenceOfProducts()
        {
            for (int trial = 0; trial < kFloatIntervalIters; ++trial)
            {
                RNG rng = new RNG((uint64_t)trial);

                Interval a = Abs(getFloat(ref rng));
                Interval b = Abs(getFloat(ref rng));
                Interval c = Abs(getFloat(ref rng));
                Interval d = Abs(getFloat(ref rng));

                Float sign = rng.Uniform<Float>() < -0.5 ? -1 : 1;
                b = b * sign;
                c = c * sign;

                double pa = getPrecise(a, ref rng);
                double pb = getPrecise(b, ref rng);
                double pc = getPrecise(c, ref rng);
                double pd = getPrecise(d, ref rng);

                Interval r = DifferenceOfProducts(a, b, c, d);
                double pr = DifferenceOfProducts(pa, pb, pc, pd);

                EXPECT_GE(pr, r.LowerBound(), trial.ToString());
                EXPECT_LE(pr, r.UpperBound(), trial.ToString());
            }
        }

        [TEST("FloatInterval", "SumOfProducts")]
        public static void FloatInterval_SumOfProducts()
        {
            for (int trial = 0; trial < kFloatIntervalIters; ++trial)
            {
                RNG rng = new RNG((uint64_t)trial);

                // Make sure signs are mixed
                Interval a = Abs(getFloat(ref rng));
                Interval b = Abs(getFloat(ref rng));
                Interval c = Abs(getFloat(ref rng));
                Interval d = -Abs(getFloat(ref rng));

                Float sign = rng.Uniform<Float>() < -0.5 ? -1 : 1;
                b = b * sign;
                c = c * sign;

                double pa = getPrecise(a, ref rng);
                double pb = getPrecise(b, ref rng);
                double pc = getPrecise(c, ref rng);
                double pd = getPrecise(d, ref rng);

                Interval r = SumOfProducts(a, b, c, d);
                double pr = SumOfProducts(pa, pb, pc, pd);

                EXPECT_GE(pr, r.LowerBound(), trial.ToString());
                EXPECT_LE(pr, r.UpperBound(), trial.ToString());
            }
        }

        private static Float randomExp(ref RNG rng, int minExp = -10, int maxExp = 10)
        {
            Float logu = Lerp(rng.Uniform<Float>(), minExp, maxExp);
            Float val = (Float)std.pow(10, logu);
            Float sign = rng.Uniform<Float>() < .5 ? -1.0f : 1.0f;
            return val * sign;
        }

        [TEST("Math", "TwoProd")]
        public static void Math_TwoProd()
        {
            for (int i = 0; i < 100000; ++i)
            {
                RNG rng = new RNG((uint64_t)i);
                Float a = randomExp(ref rng), b = randomExp(ref rng);
                CompensatedFloat tp = TwoProd(a, b);
                EXPECT_EQ((Float)tp, a * b);
                EXPECT_EQ((double)tp, (double)a * (double)b);
            }
        }

        [TEST("Math", "TwoSum")]
        public static void Math_TwoSum()
        {
            for (int i = 0; i < 100000; ++i)
            {
                RNG rng = new RNG((uint64_t)i);
                Float a = randomExp(ref rng), b = randomExp(ref rng);
                CompensatedFloat tp = TwoSum(a, b);
                EXPECT_EQ((Float)tp, a + b);
                EXPECT_EQ((double)tp, (double)a + (double)b);
            }
        }

        // This depends on having a higher precision option to compare to.
        [TEST("Math", "InnerProduct")]
        public static void Math_InnerProduct()
        {
            for (int i = 0; i < 100000; ++i)
            {
                RNG rng = new RNG((uint64_t)i);
                Float[] a = new Float[4];
                Float[] b = new Float[4];
                // gcc evaluates the braced-init-list elements left to right.
                for (int k = 0; k < 4; ++k) a[k] = randomExp(ref rng);
                for (int k = 0; k < 4; ++k) b[k] = randomExp(ref rng);
                Float ab = InnerProduct(a[0], b[0], a[1], b[1], a[2], b[2], a[3], b[3]);
                Float dab = (Float)((double)a[0] * (double)b[0] + (double)a[1] * (double)b[1] +
                                    (double)a[2] * (double)b[2] + (double)a[3] * (double)b[3]);
                EXPECT_EQ(ab, dab);
            }
        }

        // Make sure that the permute function is in fact a valid permutation.
        [TEST("PermutationElement", "Valid")]
        public static void PermutationElement_Valid()
        {
            for (int len = 2; len < 1024; ++len)
            {
                for (int iter = 0; iter < 10; ++iter)
                {
                    bool[] seen = new bool[len];

                    for (int i = 0; i < len; ++i)
                    {
                        int offset = PermutationElement((uint32_t)i, (uint32_t)len, (uint32_t)MixBits((uint64_t)(1 + iter)));
                        ASSERT_TRUE(offset >= 0 && offset < seen.Length, offset.ToString());
                        EXPECT_FALSE(seen[offset], StringPrintf("len %d index %d", len, i));
                        seen[offset] = true;
                    }
                }
            }
        }

        [TEST("PermutationElement", "Uniform")]
        public static void PermutationElement_Uniform()
        {
            foreach (int n in new int[] { 2, 3, 4, 5, 9, 14, 16, 22, 27, 36 })
            {
                int[] count = new int[n * n];

                int numIters = 60000 * n;
                for (int seed = 0; seed < numIters; ++seed)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        int ip = PermutationElement((uint32_t)i, (uint32_t)n, (uint32_t)MixBits((uint64_t)seed));
                        int offset = ip * n + i;
                        ++count[offset];
                    }
                }

                for (int i = 0; i < n; ++i)
                {
                    for (int j = 0; j < n; ++j)
                    {
                        Float tol = 0.03f;
                        int offset = j * n + i;
                        EXPECT_TRUE(count[offset] >= (1 - tol) * numIters / n &&
                                    count[offset] <= (1 + tol) * numIters / n,
                                    StringPrintf("Got count %d for %d -> %d (perm size %d). Expected +/- %d.\n",
                                                 count[offset], i, j, n, numIters / n));
                    }
                }
            }
        }

        [TEST("PermutationElement", "UniformDelta")]
        public static void PermutationElement_UniformDelta()
        {
            foreach (int n in new int[] { 2, 3, 4, 5, 9, 14, 16, 22, 27, 36 })
            {
                int[] count = new int[n * n];

                int numIters = 60000 * n;
                for (int seed = 0; seed < numIters; ++seed)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        int ip = PermutationElement((uint32_t)i, (uint32_t)n, (uint32_t)MixBits((uint64_t)seed));
                        int delta = ip - i;
                        if (delta < 0)
                            delta += n;
                        CHECK_LT(delta, n);
                        int offset = delta * n + i;
                        ++count[offset];
                    }
                }

                for (int i = 0; i < n; ++i)
                {
                    for (int j = 0; j < n; ++j)
                    {
                        Float tol = 0.03f;
                        int offset = j * n + i;
                        EXPECT_TRUE(count[offset] >= (1 - tol) * numIters / n &&
                                    count[offset] <= (1 + tol) * numIters / n,
                                    StringPrintf("Got count %d for %d -> %d (perm size %d). Expected +/- %d.\n",
                                                 count[offset], i, j, n, numIters / n));
                    }
                }
            }
        }
    }
}
