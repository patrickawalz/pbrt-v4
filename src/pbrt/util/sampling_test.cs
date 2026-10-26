// sampling_test.cpp
//
// Differences from the C++ test: std::vector<Float>/initializer lists become Float[] wrapped in
// pstd.span<Float>; std::atomic<int> counters become Interlocked adds; VarianceEstimator,
// Array2D etc. use the port's names. gcc's right-to-left evaluation of constructor arguments
// that draw from the RNG is reproduced where it matters.

using System;
using System.Threading;
using testing;

namespace pbrt
{
    public static unsafe class SamplingTests
    {
        private static pstd.span<Float> Sp(params Float[] a)
        {
            return new pstd.span<Float>(a, 0, a.Length);
        }

        private static bool checkErr(Float a, Float b)
        {
            Float err;
            if (std.min(std.abs(a), std.abs(b)) < 1e-2)
                err = std.abs(a - b);
            else
                err = std.abs(2 * (a - b) / (a + b));
            return err > 1e-2;
        }

        [TEST("SampleDiscrete", "Basics")]
        public static void SampleDiscrete_Basics()
        {
            Float pdf;

            EXPECT_EQ(0, SampleDiscrete(Sp(5), 0.251f, &pdf));
            EXPECT_EQ(1, pdf);

            EXPECT_EQ(0, SampleDiscrete(Sp(0.5f, 0.5f), 0.0f, &pdf));
            EXPECT_EQ(0.5f, pdf);

            EXPECT_EQ(0, SampleDiscrete(Sp(0.5f, 0.5f), 0.499f, &pdf));
            EXPECT_EQ(0.5f, pdf);

            Float uRemapped;
            EXPECT_EQ(1, SampleDiscrete(Sp(0.5f, 0.5f), 0.5f, &pdf, &uRemapped));
            EXPECT_EQ(0.5f, pdf);
            EXPECT_EQ(0, uRemapped);
        }

        [TEST("SampleDiscrete", "VsPiecewiseConstant1D")]
        public static void SampleDiscrete_VsPiecewiseConstant1D()
        {
            Float[] values = new Float[15];
            RNG rng = new RNG();
            for (int i = 0; i < 15; ++i)
                values[i] = std.max<Float>(0, rng.Uniform<Float>() - .2f);

            PiecewiseConstant1D dist = new PiecewiseConstant1D(Sp(values));

            for (int i = 0; i < 100; ++i)
            {
                Float u = rng.Uniform<Float>();
                Float sdPDF;
                int sdIndex = SampleDiscrete(Sp(values), u, &sdPDF);

                Float pcPDF;
                Float pcOffset = dist.Sample(u, &pcPDF);
                int pcIndex = (int)(pcOffset * values.Length);
                pcPDF /= values.Length;

                EXPECT_EQ(sdIndex, pcIndex, u.ToString());
                EXPECT_LT(std.abs(sdPDF - pcPDF), 1e-3, u.ToString());
            }
        }

        [TEST("Sampling", "InvertUniformHemisphere")]
        public static void Sampling_InvertUniformHemisphere()
        {
            foreach (Point2f u in new Uniform2D(1000))
            {
                Vector3f v = SampleUniformHemisphere(u);
                Point2f up = InvertUniformHemisphereSample(v);

                EXPECT_LT(std.abs(u.x - up.x), 1e-3, "u " + u + " -> " + v + " -> " + up);
                EXPECT_LT(std.abs(u.y - up.y), 1e-3, "u " + u + " -> " + v + " -> " + up);
            }
        }

        [TEST("Sampling", "InvertCosineHemisphere")]
        public static void Sampling_InvertCosineHemisphere()
        {
            foreach (Point2f u in new Uniform2D(1000))
            {
                Vector3f v = SampleCosineHemisphere(u);
                Point2f up = InvertCosineHemisphereSample(v);

                EXPECT_LT(std.abs(u.x - up.x), 1e-3, "u " + u + " -> " + v + " -> " + up);
                EXPECT_LT(std.abs(u.y - up.y), 1e-3, "u " + u + " -> " + v + " -> " + up);
            }
        }

        [TEST("Sampling", "InvertUniformSphere")]
        public static void Sampling_InvertUniformSphere()
        {
            foreach (Point2f u in new Uniform2D(1000))
            {
                Vector3f v = SampleUniformSphere(u);
                Point2f up = InvertUniformSphereSample(v);

                EXPECT_LT(std.abs(u.x - up.x), 1e-3, "u " + u + " -> " + v + " -> " + up);
                EXPECT_LT(std.abs(u.y - up.y), 1e-3, "u " + u + " -> " + v + " -> " + up);
            }
        }

        [TEST("Sampling", "InvertUniformTriangle")]
        public static void Sampling_InvertUniformTriangle()
        {
            foreach (Point2f u in new Uniform2D(1000))
            {
                pstd.array3<Float> b = SampleUniformTriangle(u);
                Point2f up = InvertUniformTriangleSample(b);

                EXPECT_LT(std.abs(u.x - up.x), 1e-3, "u " + u + " -> " + b[0] + ", " + b[1] + ", " + b[2] + " -> " + up);
                EXPECT_LT(std.abs(u.y - up.y), 1e-3, "u " + u + " -> " + b[0] + ", " + b[1] + ", " + b[2] + " -> " + up);
            }
        }

        [TEST("Sampling", "InvertUniformCone")]
        public static void Sampling_InvertUniformCone()
        {
            RNG rng = new RNG();
            foreach (Point2f u in new Uniform2D(1000))
            {
                Float cosThetaMax = rng.Uniform<Float>();
                Vector3f v = SampleUniformCone(u, cosThetaMax);
                Point2f up = InvertUniformConeSample(v, cosThetaMax);

                EXPECT_LT(std.abs(u.x - up.x), 1e-3, "u " + u + " -> " + v + " -> " + up);
                EXPECT_LT(std.abs(u.y - up.y), 1e-3, "u " + u + " -> " + v + " -> " + up);
            }
        }

        [TEST("Sampling", "InvertUniformDiskPolar")]
        public static void Sampling_InvertUniformDiskPolar()
        {
            foreach (Point2f u in new Uniform2D(1000))
            {
                Point2f p = SampleUniformDiskPolar(u);
                Point2f up = InvertUniformDiskPolarSample(p);

                EXPECT_LT(std.abs(u.x - up.x), 1e-3, "u " + u + " -> " + p + " -> " + up);
                EXPECT_LT(std.abs(u.y - up.y), 1e-3, "u " + u + " -> " + p + " -> " + up);
            }
        }

        [TEST("Sampling", "InvertUniformDiskConcentric")]
        public static void Sampling_InvertUniformDiskConcentric()
        {
            foreach (Point2f u in new Uniform2D(1000))
            {
                Point2f p = SampleUniformDiskConcentric(u);
                Point2f up = InvertUniformDiskConcentricSample(p);

                EXPECT_LT(std.abs(u.x - up.x), 1e-3, "u " + u + " -> " + p + " -> " + up);
                EXPECT_LT(std.abs(u.y - up.y), 1e-3, "u " + u + " -> " + p + " -> " + up);
            }
        }

        [TEST("LowDiscrepancy", "RadicalInverse")]
        public static void LowDiscrepancy_RadicalInverse()
        {
            for (int a = 0; a < 1024; ++a)
            {
                EXPECT_EQ(ReverseBits32((uint32_t)a) * 2.3283064365386963e-10f, RadicalInverse(0, (uint64_t)a));
            }
        }

        [TEST("LowDiscrepancy", "SobolFirstDimension")]
        public static void LowDiscrepancy_SobolFirstDimension()
        {
            // Make sure first dimension is the regular base 2 radical inverse
            for (int i = 0; i < 8192; ++i)
            {
                EXPECT_EQ(SobolSample(i, 0, new NoRandomizer()),
                          ReverseBits32((uint32_t)i) * 2.3283064365386963e-10f);
            }
        }

        [TEST("Sobol", "IntervalToIndex")]
        public static void Sobol_IntervalToIndex()
        {
            for (int logRes = 0; logRes < 8; ++logRes)
            {
                int res = 1 << logRes;
                for (int y = 0; y < res; ++y)
                {
                    for (int x = 0; x < res; ++x)
                    {
                        // For each pixel sample.
                        bool sawCorner = false;
                        for (int s = 0; s < 16; ++s)
                        {
                            uint64_t seqIndex = SobolIntervalToIndex((uint32_t)logRes, (uint64_t)s, new Point2i(x, y));
                            Point2f samp = new Point2f(SobolSample((int64_t)seqIndex, 0, new NoRandomizer()),
                                                       SobolSample((int64_t)seqIndex, 1, new NoRandomizer()));
                            Point2f ss = new Point2f(samp[0] * res, samp[1] * res);
                            // Make sure all samples aren't landing at the
                            // lower-left corner.
                            if (ss == new Point2f(x, y))
                            {
                                EXPECT_FALSE(sawCorner,
                                             "Multi corner " + new Point2i(x, y) + ", res " + res + ", samp " + s +
                                             ", seq index " + seqIndex + ", got " + ss + ", from samp " + samp);
                                sawCorner = true;
                            }
                            // Most of the time, the Sobol sample should be within the
                            // pixel's extent. Due to rounding, it may rarely be at the
                            // upper end of the extent; the check here is written
                            // carefully to only accept points just at the upper limit
                            // but not into the next pixel.
                            EXPECT_TRUE(new Point2i(x, y) == new Point2i((int)ss.x, (int)ss.y) ||
                                        (x == (int)ss.x && (Float)(y + 1) == ss.y) ||
                                        ((Float)(x + 1) == ss.x && y == (int)ss.y),
                                        "Pixel " + new Point2i(x, y) + ", sample " + s + ", got " + ss);
                        }
                    }
                }
            }
        }

        [TEST("Sobol", "IntervalToIndexRandoms")]
        public static void Sobol_IntervalToIndexRandoms()
        {
            RNG rng = new RNG();
            for (int i = 0; i < 100000; ++i)
            {
                int logRes = rng.Uniform<int>(16);
                int res = 1 << logRes;
                int x = rng.Uniform<int>(res), y = rng.Uniform<int>(res);
                int s = rng.Uniform<int>(8192);

                uint64_t seqIndex = SobolIntervalToIndex((uint32_t)logRes, (uint64_t)s, new Point2i(x, y));
                Point2f samp = new Point2f(SobolSample((int64_t)seqIndex, 0, new NoRandomizer()),
                                           SobolSample((int64_t)seqIndex, 1, new NoRandomizer()));
                Point2f ss = new Point2f((int)(samp[0] * res), (int)(samp[1] * res));
                // Most of the time, the Sobol sample should be within the
                // pixel's extent. Due to rounding, it may rarely be at the
                // upper end of the extent; the check here is written carefully
                // to only accept points just at the upper limit but not into
                // the next pixel.
                EXPECT_TRUE(new Point2i(x, y) == new Point2i((int)ss.x, (int)ss.y) ||
                            (x == (int)ss.x && (Float)(y + 1) == ss.y) ||
                            ((Float)(x + 1) == ss.x && y == (int)ss.y),
                            "Pixel " + new Point2i(x, y) + ", sample " + s + ", got " + ss);
            }
        }

        [TEST("PiecewiseConstant1D", "Continuous")]
        public static void PiecewiseConstant1D_Continuous()
        {
            PiecewiseConstant1D dist = new PiecewiseConstant1D(Sp(1.0f, 1.0f, 2.0f, 4.0f, 8.0f));
            EXPECT_EQ(5, dist.size());

            Float pdf;
            int offset;
            EXPECT_EQ(0.0, dist.Sample(0, &pdf, &offset));
            EXPECT_FLOAT_EQ(dist.size() * 1.0 / 16.0, pdf);
            EXPECT_EQ(0, offset);

            // Right at the boundary between the 4 and the 8 segments.
            EXPECT_FLOAT_EQ(.8, dist.Sample(0.5f, &pdf, &offset));

            // Middle of the 8 segment
            EXPECT_FLOAT_EQ(.9, dist.Sample(0.75f, &pdf, &offset));
            EXPECT_FLOAT_EQ(dist.size() * 8.0 / 16.0, pdf);
            EXPECT_EQ(4, offset);

            EXPECT_FLOAT_EQ(0.0, dist.Sample(0, &pdf));
            EXPECT_FLOAT_EQ(1.0, dist.Sample(1, &pdf));
        }

        [TEST("PiecewiseConstant1D", "Range")]
        public static void PiecewiseConstant1D_Range()
        {
            pstd.vector<Float> values = Sample1DFunction((Float x) => 1 + x, 65536, 4, -1.0f, 3.0f);
            PiecewiseConstant1D dist = new PiecewiseConstant1D(values, -1.0f, 3.0f);
            // p(x) = (1+x) / 8
            // xi = int_{-1}^x p(x) ->
            // xi = 1/16 x^2 + x/8 + 1/16 ->
            // Solve 0 = 1/16 x^2 + x/8 + 1/16 - xi to sample

            foreach (Float u in new Uniform1D(100))
            {
                Float pd;
                Float xd = dist.Sample(u, &pd);

                Float t0, t1;
                ASSERT_TRUE(Quadratic((float)(1.0 / 16.0), (float)(1.0 / 8.0), (float)(1.0 / 16 - u), &t0, &t1));
                Float xa = (t0 >= -1 && t0 <= 3) ? t0 : t1;
                Float pa = (1 + xa) / 8;

                EXPECT_LT(std.abs(xd - xa) / xa, 2e-3, xd + " vs " + xa);
                EXPECT_LT(std.abs(pd - pa) / pa, 2e-3, pd + " vs " + pa);
            }
        }

        [TEST("PiecewiseConstant1D", "InverseUniform")]
        public static void PiecewiseConstant1D_InverseUniform()
        {
            Float[] values = { 1, 1, 1 };

            PiecewiseConstant1D dist = new PiecewiseConstant1D(Sp(values));
            EXPECT_EQ(0, dist.Invert(0).value());
            EXPECT_EQ(0.5, dist.Invert(0.5f).value());
            EXPECT_EQ(0.75, dist.Invert(0.75f).value());

            PiecewiseConstant1D dist2 = new PiecewiseConstant1D(Sp(values), -1, 3);
            EXPECT_EQ(0, dist2.Invert(-1).value());
            EXPECT_EQ(0.25, dist2.Invert(0).value());
            EXPECT_EQ(0.5, dist2.Invert(1).value());
            EXPECT_EQ(0.75, dist2.Invert(2).value());
            EXPECT_EQ(1, dist2.Invert(3).value());
        }

        [TEST("PiecewiseConstant1D", "InverseGeneral")]
        public static void PiecewiseConstant1D_InverseGeneral()
        {
            Float[] values = { 0, 0.25f, 0.5f, 0.25f };

            PiecewiseConstant1D dist = new PiecewiseConstant1D(Sp(values));
            EXPECT_EQ(0, dist.Invert(0).value());
            EXPECT_EQ(1, dist.Invert(1).value());
            EXPECT_EQ(0.25, dist.Invert(0.5f).value());
            EXPECT_EQ(0.5, dist.Invert(0.625f).value());
            EXPECT_EQ(0.75, dist.Invert(0.75f).value());
            EXPECT_FLOAT_EQ(0.825, dist.Invert(0.825f).value());

            PiecewiseConstant1D dist2 = new PiecewiseConstant1D(Sp(values), -1, 3);
            EXPECT_EQ(0, dist2.Invert(-1).value());
            EXPECT_EQ(1, dist2.Invert(3).value());
            EXPECT_EQ(0.25, dist2.Invert(Lerp(0.5f, -1, 3)).value());
            EXPECT_EQ(0.5, dist2.Invert(Lerp(0.625f, -1, 3)).value());
            EXPECT_EQ(0.75, dist2.Invert(Lerp(0.75f, -1, 3)).value());
            EXPECT_FLOAT_EQ(0.825, dist2.Invert(Lerp(0.825f, -1, 3)).value());
        }

        [TEST("PiecewiseConstant1D", "InverseRandoms")]
        public static void PiecewiseConstant1D_InverseRandoms()
        {
            Float[] values = { 0, 1.25f, 0.5f, 0.25f, 3.7f };

            PiecewiseConstant1D dist = new PiecewiseConstant1D(Sp(values));
            foreach (Float u in new Uniform1D(100))
            {
                Float v = dist.Sample(u);
                pstd.optional<Float> inv = dist.Invert(v);
                ASSERT_TRUE(inv.has_value());
                Float err = std.min(std.abs(inv.value() - u), std.abs(inv.value() - u) / u);
                EXPECT_LT(err, 1e-4, "u " + u + " vs inv " + inv.value());
            }

            PiecewiseConstant1D dist2 = new PiecewiseConstant1D(Sp(values), -1, 3);
            foreach (Float u in new Uniform1D(100))
            {
                Float v = dist.Sample(u);
                pstd.optional<Float> inv = dist.Invert(v);
                ASSERT_TRUE(inv.has_value());
                Float err = std.min(std.abs(inv.value() - u), std.abs(inv.value() - u) / u);
                EXPECT_LT(err, 1e-4, "u " + u + " vs inv " + inv.value());
            }
        }

        [TEST("PiecewiseConstant1D", "Integral")]
        public static void PiecewiseConstant1D_Integral()
        {
            Float[] values = { 1, 0.5f, 0, 0.25f };

            PiecewiseConstant1D dist = new PiecewiseConstant1D(Sp(values));
            EXPECT_EQ(0.4375f, dist.Integral());

            PiecewiseConstant1D dist2 = new PiecewiseConstant1D(Sp(values), 0, 4);
            EXPECT_EQ(1.75f, dist2.Integral());

            PiecewiseConstant1D dist3 = new PiecewiseConstant1D(Sp(values), -3, 5);
            EXPECT_EQ(3.5f, dist3.Integral());
        }

        [TEST("PiecewiseConstant2D", "InverseUniform")]
        public static void PiecewiseConstant2D_InverseUniform()
        {
            Float[] values = { 1, 1, 1, 1, 1, 1 };

            PiecewiseConstant2D dist = new PiecewiseConstant2D(Sp(values), 3, 2);
            EXPECT_EQ(new Point2f(0, 0), dist.Invert(new Point2f(0, 0)).value());
            EXPECT_EQ(new Point2f(1, 1), dist.Invert(new Point2f(1, 1)).value());
            EXPECT_EQ(new Point2f(0.5f, 0.5f), dist.Invert(new Point2f(0.5f, 0.5f)).value());
            EXPECT_EQ(new Point2f(0.25f, 0.75f), dist.Invert(new Point2f(0.25f, 0.75f)).value());

            Bounds2f domain = new Bounds2f(new Point2f(-1, -0.5f), new Point2f(3, 1.5f));
            PiecewiseConstant2D dist2 = new PiecewiseConstant2D(Sp(values), 3, 2, domain);
            EXPECT_EQ(new Point2f(0, 0), dist2.Invert(domain.Lerp(new Point2f(0, 0))).value());
            EXPECT_EQ(new Point2f(1, 1), dist2.Invert(domain.Lerp(new Point2f(1, 1))).value());
            EXPECT_EQ(new Point2f(0.5f, 0.5f), dist2.Invert(domain.Lerp(new Point2f(0.5f, 0.5f))).value());
            EXPECT_EQ(new Point2f(0.25f, 0.75f), dist2.Invert(domain.Lerp(new Point2f(0.25f, 0.75f))).value());
        }

        [TEST("PiecewiseConstant2D", "InverseRandoms")]
        public static void PiecewiseConstant2D_InverseRandoms()
        {
            int nx = 4, ny = 5;
            Float[] values = new Float[nx * ny];
            RNG rng = new RNG();
            for (int i = 0; i < nx * ny; ++i)
                values[i] = rng.Uniform<Float>();

            PiecewiseConstant2D dist = new PiecewiseConstant2D(Sp(values), nx, ny);
            foreach (Point2f u in new Uniform2D(100))
            {
                Point2f v = dist.Sample(u);
                pstd.optional<Point2f> inv = dist.Invert(v);
                ASSERT_TRUE(inv.has_value());
                Point2f iv = inv.value();
                Point2f err = new Point2f(
                    std.min(std.abs(iv[0] - u[0]), std.abs(iv[0] - u[0]) / u[0]),
                    std.min(std.abs(iv[1] - u[1]), std.abs(iv[1] - u[1]) / u[1]));
                EXPECT_LT(err.x, 1e-4, "u.x " + u.x + " vs inv " + iv.x + " at " + v.x);
                EXPECT_LT(err.y, 1e-4, "u.y " + u.y + " vs inv " + iv.y + " at " + v.y);
            }

            Bounds2f domain = new Bounds2f(new Point2f(-1, -0.5f), new Point2f(3, 1.5f));
            PiecewiseConstant2D dist2 = new PiecewiseConstant2D(Sp(values), nx, ny, domain);
            foreach (Point2f u in new Uniform2D(100, 235351))
            {
                Point2f v = dist2.Sample(u);
                pstd.optional<Point2f> inv = dist2.Invert(v);
                ASSERT_TRUE(inv.has_value());
                Point2f iv = inv.value();
                Point2f err = new Point2f(
                    std.min(std.abs(iv[0] - u[0]), std.abs(iv[0] - u[0]) / u[0]),
                    std.min(std.abs(iv[1] - u[1]), std.abs(iv[1] - u[1]) / u[1]));
                EXPECT_LT(err.x, 1e-4, "u.x " + u.x + " vs inv " + iv.x + " at " + v.x);
                EXPECT_LT(err.y, 1e-4, "u.y " + u.y + " vs inv " + iv.y + " at " + v.y);
            }
        }

        [TEST("PiecewiseConstant2D", "FromFuncLInfinity")]
        public static void PiecewiseConstant2D_FromFuncLInfinity()
        {
            Func<Float, Float, Float> f = (Float x, Float y) => x * x * y;
            Array2D<Float> values = Sample2DFunction(f, 4, 2, 1, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));
            PiecewiseConstant2D dSampled = new PiecewiseConstant2D((pstd.span<Float>)values, 4, 2);

            Float[] exact = {
                (Float)(Sqr(0.25) * (Float)0.5), (Float)(Sqr(0.5) * (Float)0.5),
                (Float)(Sqr(0.75) * (Float)0.5), (Float)(Sqr(1.0) * (Float)0.5),
                (Float)(Sqr(0.25) * (Float)1),   (Float)(Sqr(0.5) * (Float)1),
                (Float)(Sqr(0.75) * (Float)1),   (Float)(Sqr(1.0) * (Float)1) };
            PiecewiseConstant2D dExact = new PiecewiseConstant2D(Sp(exact), 4, 2);
            PiecewiseConstant2D.TestCompareDistributions(dSampled, dExact);
        }

        [TEST("PiecewiseConstant2D", "Integral")]
        public static void PiecewiseConstant2D_Integral()
        {
            Float[] values = { 1, 2, 0, 7, 3, 1, 2, 0 };

            PiecewiseConstant2D dist = new PiecewiseConstant2D(Sp(values), 4, 2);
            EXPECT_EQ(2, dist.Integral());

            PiecewiseConstant2D dist2 = new PiecewiseConstant2D(Sp(values), 2, 4);
            EXPECT_EQ(2, dist2.Integral());

            PiecewiseConstant2D dist3 = new PiecewiseConstant2D(Sp(values), 4, 2, new Bounds2f(new Point2f(0, 0), new Point2f(4, 2)));
            EXPECT_EQ(16, dist3.Integral());

            PiecewiseConstant2D dist4 = new PiecewiseConstant2D(Sp(values), 4, 2, new Bounds2f(new Point2f(-1, 0), new Point2f(0, 4)));
            EXPECT_EQ(8, dist4.Integral());
        }

        [TEST("Sampling", "SphericalTriangle")]
        public static void Sampling_SphericalTriangle()
        {
            int count = 1024 * 1024;
            pstd.array3<Point3f> v = new pstd.array3<Point3f>(new Point3f(4, 1, 1), new Point3f(-10, 3, 3),
                                                              new Point3f(-2, -8, 10));
            Float A = 0.5f * Length(Cross(v[1] - v[0], v[2] - v[0]));
            Vector3f N = Normalize(Cross(v[1] - v[0], v[2] - v[0]));
            Point3f p = new Point3f(.5f, -.4f, .7f);

            // Integrate this function over the projection of the triangle given by
            // |v| at the unit sphere surrounding |p|.
            Func<Point3f, Float> f = (Point3f pp) => pp.x * pp.y * pp.z;

            Float sphSum = 0, areaSum = 0;
            for (int i = 0; i < count; ++i)
            {
                Float uy = RadicalInverse(1, (uint64_t)i), ux = RadicalInverse(0, (uint64_t)i);
                Point2f u = new Point2f(ux, uy);

                Float pdf;
                pstd.array3<Float> bs = SampleSphericalTriangle(v, p, u, &pdf);
                Point3f pTri = bs[0] * v[0] + bs[1] * v[1] + bs[2] * v[2];
                sphSum += f(pTri) / pdf;

                pstd.array3<Float> ba = SampleUniformTriangle(u);
                pdf = 1 / A;
                pTri = ba[0] * v[0] + ba[1] * v[1] + (1 - ba[0] - ba[1]) * v[2];
                areaSum += f(pTri) * AbsDot(N, Normalize(pTri - p)) / (pdf * DistanceSquared(p, pTri));
            }
            Float sphInt = sphSum / count;
            Float areaInt = areaSum / count;

            EXPECT_LT(std.abs(areaInt - sphInt), 1e-3);
        }

        [TEST("Sampling", "SphericalTriangleInverse")]
        public static void Sampling_SphericalTriangleInverse()
        {
            RNG rng = new RNG();
            Func<Float, Float, Point3f> rp = (Float low, Float high) =>
            {
                Float z = Lerp(rng.Uniform<Float>(), low, high);
                Float y = Lerp(rng.Uniform<Float>(), low, high);
                Float x = Lerp(rng.Uniform<Float>(), low, high);
                return new Point3f(x, y, z);
            };

            for (int i = 0; i < 100; ++i)
            {
                Point3f v0 = rp(-10, 10), v1 = rp(-10, 10), v2 = rp(-10, 10);
                pstd.array3<Point3f> v = new pstd.array3<Point3f>(v0, v1, v2);
                Point3f p = rp(-1, 1);
                if (SphericalTriangleArea(Normalize(v[0] - p), Normalize(v[1] - p),
                                          Normalize(v[2] - p)) < 5e-3)
                    continue;

                for (int j = 0; j < 100; ++j)
                {
                    Float pdf;
                    Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                    Point2f u = new Point2f(ux, uy);
                    pstd.array3<Float> bs = SampleSphericalTriangle(v, p, u, &pdf);

                    Point3f pTri = bs[0] * v[0] + bs[1] * v[1] + bs[2] * v[2];

                    Point2f ui = InvertSphericalTriangleSample(v, p, Normalize(pTri - p));

                    Func<Float, Float, Float> err = (Float a, Float rf) =>
                    {
                        if (rf < 0.05f)
                            return std.abs(a - rf);
                        else
                            return std.abs((a - rf) / rf);
                    };
                    // The tolerance has to be fairly high, unfortunately...
                    EXPECT_LT(std.abs(ui[0] - u[0]), 0.025f, u + " vs inverse " + ui);
                    EXPECT_LT(err(ui[1], u[1]), 0.005f, u + " vs inverse " + ui);
                }
            }
        }

        [TEST("Sampling", "SphericalQuad")]
        public static void Sampling_SphericalQuad()
        {
            int count = 1024 * 1024;
            Point3f[] v = { new Point3f(4, 1, 1), new Point3f(6, 1, -2), new Point3f(4, 4, 1), new Point3f(6, 4, -2) };
            Float A = Length(v[0] - v[1]) * Length(v[0] - v[2]);
            Vector3f N = Normalize(Cross(v[1] - v[0], v[2] - v[0]));
            Point3f p = new Point3f(.5f, -.4f, .7f);

            // Integrate this function over the projection of the quad given by
            // |v| at the unit sphere surrounding |p|.
            Func<Point3f, Float> f = (Point3f pp) => pp.x * pp.y * pp.z;

            Float sphSum = 0, areaSum = 0;
            foreach (Point2f u in new Hammersley2D(count))
            {
                Float pdf;
                Point3f pq = SampleSphericalRectangle(p, v[0], v[1] - v[0], v[2] - v[0], u, &pdf);
                sphSum += f(pq) / pdf;

                pq = Lerp(u[1], Lerp(u[0], v[0], v[1]), Lerp(u[0], v[2], v[3]));
                pdf = 1 / A;
                areaSum += f(pq) * AbsDot(N, Normalize(pq - p)) / (pdf * DistanceSquared(p, pq));
            }
            Float sphInt = sphSum / count;
            Float areaInt = areaSum / count;

            EXPECT_LT(std.abs(areaInt - sphInt), 1e-3, "area " + areaInt + " sph " + sphInt);
        }

        [TEST("Sampling", "SphericalQuadInverse")]
        public static void Sampling_SphericalQuadInverse()
        {
            int count = 64 * 1024;
            Point3f[,] v = { { new Point3f(4, 1, 1), new Point3f(6, 1, -2) },
                             { new Point3f(4, 4, 1), new Point3f(6, 4, -2) } };

            RNG rng = new RNG();
            int nTested = 0;
            foreach (Point2f u in new Hammersley2D(count))
            {
                int a = rng.Uniform<int>() & 1, b = rng.Uniform<int>() & 1;
                int na = 1 - a, nb = 1 - b;

                Float pz = Lerp(rng.Uniform<Float>(), -10, 10);
                Float py = Lerp(rng.Uniform<Float>(), -10, 10);
                Float px = Lerp(rng.Uniform<Float>(), -10, 10);
                Point3f p = new Point3f(px, py, pz);
                Float pdf;
                Point3f pq = SampleSphericalRectangle(p, v[a, b], v[na, b] - v[a, b],
                                                      v[a, nb] - v[a, b], u, &pdf);

                Float solidAngle = 1 / pdf;
                if (solidAngle < .01)
                    continue;
                ++nTested;
                Point2f ui = InvertSphericalRectangleSample(p, v[a, b], v[na, b] - v[a, b],
                                                            v[a, nb] - v[a, b], pq);

                Func<Float, Float, Float> err = (Float aa, Float rf) =>
                {
                    if (rf < 1e-2)
                        return std.abs(aa - rf);
                    else
                        return std.abs((aa - rf) / rf);
                };
                // The tolerance has to be fairly high, unfortunately...
                // FIXME: super high for now to find the really bad cases...
                EXPECT_LT(err(ui[0], u[0]), 0.01, u + " vs inverse " + ui + ", solid angle " + 1 / pdf);
                EXPECT_LT(err(ui[1], u[1]), 0.01, u + " vs inverse " + ui + ", solid angle " + 1 / pdf);
            }
            EXPECT_GT(nTested, count / 2);
        }

        [TEST("Sampling", "SmoothStep")]
        public static void Sampling_SmoothStep()
        {
            Float start = SampleSmoothStep(0, 10, 20);
            // Fairly high slop since lots of values close to the start are close
            // to zero.
            EXPECT_LT(std.abs(start - 10), .1, start.ToString());

            Float end = SampleSmoothStep(1, -10, -5);
            EXPECT_LT(std.abs(end - -5), 1e-5, end.ToString());

            Float mid = SampleSmoothStep(0.5f, 0, 1);
            // Solved this numericalla in Mathematica.
            EXPECT_LT(std.abs(mid - 0.733615), 1e-5, mid.ToString());

            foreach (Float u in new Uniform1D(1000))
            {
                Float x = SampleSmoothStep(u, -3, 5);
                Float ratio = SmoothStep(x, -3, 5) / SmoothStepPDF(x, -3, 5);
                // SmoothStep over [-3,5] integrates to 4.
                EXPECT_LT(std.abs(ratio - 4), 1e-5, ratio.ToString());

                EXPECT_FALSE(checkErr(u, InvertSmoothStepSample(x, -3, 5)));
            }

            pstd.vector<Float> values = Sample1DFunction((Float v) => SmoothStep(v, 0, 1), 1024, 64 * 1024, 0.0f, 1.0f);
            PiecewiseConstant1D distrib = new PiecewiseConstant1D(values);
            foreach (Float u in new Uniform1D(100, 62351))
            {
                Float cx = SampleSmoothStep(u, 0, 1);
                Float cp = SmoothStepPDF(cx, 0, 1);

                Float dp;
                Float dx = distrib.Sample(u, &dp);
                EXPECT_LT(std.abs(cx - dx), 3e-3, "Closed form = " + cx + ", distrib = " + dx);
                EXPECT_LT(std.abs(cp - dp), 3e-3, "Closed form PDF = " + cp + ", distrib PDF = " + dp);
            }
        }

        [TEST("Sampling", "Linear")]
        public static void Sampling_Linear()
        {
            int nBuckets = 32;
            int[] buckets = new int[nBuckets];

            int[][] ranges = { new int[] { 0, 1 }, new int[] { 1, 2 }, new int[] { 5, 50 }, new int[] { 100, 0 }, new int[] { 75, 50 } };
            foreach (int[] r in ranges)
            {
                Float f0 = r[0], f1 = r[1];
                int nSamples = 1000000;
                for (int i = 0; i < nSamples; ++i)
                {
                    Float u = (Float)((i + .5) / nSamples);
                    Float t = SampleLinear(u, f0, f1);
                    ++buckets[std.min((int)(t * nBuckets), nBuckets - 1)];
                }

                for (int i = 0; i < nBuckets; ++i)
                {
                    int expected = (int)Lerp((Float)i / (nBuckets - 1), (Float)buckets[0], (Float)buckets[nBuckets - 1]);
                    EXPECT_GE(buckets[i], .99 * expected);
                    EXPECT_LE(buckets[i], 1.01 * expected);
                }
            }

            pstd.vector<Float> values = Sample1DFunction((Float v) => 1 + 3 * v, 1024, 64 * 1024, 0.0f, 1.0f);
            PiecewiseConstant1D distrib = new PiecewiseConstant1D(values);
            foreach (Float u in new Uniform1D(100))
            {
                Float cx = SampleLinear(u, 1, 4);
                Float cp = LinearPDF(cx, 1, 4);

                Float dp;
                Float dx = distrib.Sample(u, &dp);
                EXPECT_LT(std.abs(cx - dx), 3e-3, "Closed form = " + cx + ", distrib = " + dx);
                EXPECT_LT(std.abs(cp - dp), 3e-3, "Closed form PDF = " + cp + ", distrib PDF = " + dp);
            }

            RNG rng = new RNG();
            foreach (Float u in new Uniform1D(100))
            {
                Float low = rng.Uniform<Float>() * 10;
                Float high = rng.Uniform<Float>() * 10;
                if (low < high)
                    pstd.swap(ref low, ref high);
                Float x = SampleLinear(u, low, high);

                EXPECT_FALSE(checkErr(u, InvertLinearSample(x, low, high)),
                             " u = " + u + " -> x " + x + " -> " + InvertLinearSample(x, low, high) +
                             " (over " + low + " - " + high);
            }
        }

        [TEST("Sampling", "Tent")]
        public static void Sampling_Tent()
        {
            // Make sure stratification is preserved at the midpoint of the
            // sampling domain.
            Float dist = std.abs(SampleTent(.501f, 1) - SampleTent(.499f, 1));
            EXPECT_LT(dist, .01);

            Float[] rad = { 1, 2.5f, .125f };
            RNG rng = new RNG();
            foreach (Float radius in rad)
            {
                Float rr = radius;
                Func<Float, Float> tent = (Float x) => std.max<Float>(0, 1 - std.abs(x) / rr);

                pstd.vector<Float> values = Sample1DFunction(tent, 8192, 64, -radius, radius);
                PiecewiseConstant1D distrib = new PiecewiseConstant1D(values, -radius, radius);
                for (int i = 0; i < 100; ++i)
                {
                    Float u = rng.Uniform<Float>();
                    Float tx = SampleTent(u, radius);
                    Float tp = TentPDF(tx, radius);

                    Float dp;
                    Float dx = distrib.Sample(u, &dp);
                    EXPECT_LT(std.abs(tx - dx), 3e-3, "Closed form = " + tx + ", distrib = " + dx);
                    EXPECT_LT(std.abs(tp - dp), 3e-3, "Closed form PDF = " + tp + ", distrib PDF = " + dp);

                    EXPECT_FALSE(checkErr(u, InvertTentSample(tx, radius)),
                                 "u " + u + " radius " + radius + " x " + tx + " inverse " + InvertTentSample(tx, radius));
                }
            }
        }

        [TEST("Sampling", "CatmullRom")]
        public static void Sampling_CatmullRom()
        {
            Float[] nodes = { 0, .1f, .4f, .88f, 1 };
            Float[] values = { 0, 5, 2, 10, 5 };
            Float[] cdf = new Float[values.Length];

            IntegrateCatmullRom(Sp(nodes), Sp(values), Sp(cdf));

            PiecewiseConstant1D distrib = new PiecewiseConstant1D(
                Sample1DFunction((Float v) => CatmullRom(Sp(nodes), Sp(values), v), 8192, 1024, 0.0f, 1.0f));
            foreach (Float u in new Uniform1D(100))
            {
                Float cp;
                Float cx = SampleCatmullRom(Sp(nodes), Sp(values), Sp(cdf), u, null, &cp);

                Float dp;
                Float dx = distrib.Sample(u, &dp);
                EXPECT_LT(std.abs(cx - dx), 3e-3, "Closed form = " + cx + ", distrib = " + dx);
                EXPECT_LT(std.abs(cp - dp), 3e-3, "Closed form PDF = " + cp + ", distrib PDF = " + dp);
            }
        }

        [TEST("Sampling", "Bilinear")]
        public static void Sampling_Bilinear()
        {
            RNG rng = new RNG();
            Float[][] quads = { new Float[] { 0, .5f, 1.3f, 4.7f }, new Float[] { 1, 1, 1, 1 }, new Float[] { 11, .25f, 1, 20 } };
            foreach (Float[] v in quads)
            {
                Float[] vv = v;
                Func<Float, Float, Float> bilerp = (Float x, Float y) =>
                    ((1 - x) * (1 - y) * vv[0] + x * (1 - y) * vv[1] + (1 - x) * y * vv[2] + x * y * vv[3]);

                Array2D<Float> values = Sample2DFunction(bilerp, 1024, 1024, 16,
                                                         new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));
                PiecewiseConstant2D distrib = new PiecewiseConstant2D((pstd.span<Float>)values, 1024, 1024);
                foreach (Point2f u in new Uniform2D(100))
                {
                    Point2f pb = SampleBilinear(u, Sp(v));
                    Float bp = BilinearPDF(pb, Sp(v));

                    Float dp;
                    Point2f pd = distrib.Sample(u, &dp);
                    EXPECT_LT(std.abs(pb[0] - pd[0]), 3e-3, "X: Closed form = " + pb[0] + ", distrib = " + pd[0]);
                    EXPECT_LT(std.abs(pb[1] - pd[1]), 3e-3, "Y: Closed form = " + pb[1] + ", distrib = " + pd[1]);
                    EXPECT_LT(std.abs(bp - dp), 3e-3, "Closed form PDF = " + bp + ", distrib PDF = " + dp);

                    // Check InvertBilinear...
                    Point2f up = InvertBilinearSample(pb, Sp(v));
                    EXPECT_LT(std.abs(up[0] - u[0]), 3e-3,
                              "Invert failure: u[0] = " + u[0] + ", p = " + pb[0] + ", up = " + up[0]);
                    EXPECT_LT(std.abs(up[1] - u[1]), 3e-3,
                              "Invert failure: u[1] = " + u[1] + ", p = " + pb[1] + ", up = " + up[1]);
                }
            }
        }

        [TEST("Sampling", "Logistic")]
        public static void Sampling_Logistic()
        {
            Float[][] pars = { new Float[] { 1.0f, -Pi, Pi }, new Float[] { 5, 0, 3 }, new Float[] { .25f, -5, -3 } };
            foreach (Float[] p in pars)
            {
                Float s = p[0], a = p[1], b = p[2];
                Func<Float, Float> logistic = (Float v) => TrimmedLogistic(v, s, a, b);

                pstd.vector<Float> values = Sample1DFunction(logistic, 8192, 16, a, b);
                PiecewiseConstant1D distrib = new PiecewiseConstant1D(values, a, b);
                foreach (Float u in new Uniform1D(100))
                {
                    Float cx = SampleTrimmedLogistic(u, s, a, b);
                    Float cp = TrimmedLogisticPDF(cx, s, a, b);

                    Float dp;
                    Float dx = distrib.Sample(u, &dp);
                    EXPECT_LT(std.abs(cx - dx), 3e-3, "Closed form = " + cx + ", distrib = " + dx);
                    EXPECT_LT(std.abs(cp - dp), 3e-3, "Closed form PDF = " + cp + ", distrib PDF = " + dp);

                    EXPECT_FALSE(checkErr(u, InvertTrimmedLogisticSample(cx, s, a, b)),
                                 "u = " + u + " -> x = " + cx + " -> ... " + InvertTrimmedLogisticSample(cx, s, a, b));
                }
            }
        }

        [TEST("Sampling", "TrimmedExponential")]
        public static void Sampling_TrimmedExponential()
        {
            Float[][] pars = { new Float[] { 1.0f, 2.0f }, new Float[] { 5, 10 }, new Float[] { .25f, 20 } };
            foreach (Float[] p in pars)
            {
                Float c = p[0], xMax = p[1];
                Func<Float, Float> exp = (Float x) => (Float)std.exp(-c * x);

                pstd.vector<Float> values = Sample1DFunction(exp, 32768, 16, 0, xMax);
                PiecewiseConstant1D distrib = new PiecewiseConstant1D(values, 0, xMax);
                foreach (Float u in new Uniform1D(100))
                {
                    Float cx = SampleTrimmedExponential(u, c, xMax);
                    Float cp = TrimmedExponentialPDF(cx, c, xMax);

                    Float dp;
                    Float dx = distrib.Sample(u, &dp);
                    EXPECT_LT(std.abs(cx - dx), 1e-2, "Closed form = " + cx + ", distrib = " + dx);
                    EXPECT_LT(std.abs(cp - dp), 1e-2, "Closed form PDF = " + cp + ", distrib PDF = " + dp);

                    EXPECT_FALSE(checkErr(u, InvertTrimmedExponentialSample(cx, c, xMax)),
                                 "u = " + u + " -> x = " + cx + " -> ... " + InvertTrimmedExponentialSample(cx, c, xMax));
                }
            }
        }

        [TEST("Sampling", "Normal")]
        public static void Sampling_Normal()
        {
            Float[][] pars = { new Float[] { 0.0f, 1.0f }, new Float[] { -.5f, .8f }, new Float[] { .25f, .005f }, new Float[] { 3.6f, 1.6f } };
            foreach (Float[] p in pars)
            {
                Float mu = p[0], sigma = p[1];
                Func<Float, Float> normal = (Float x) =>
                    (Float)(1 / std.sqrt(2 * Pi * sigma * sigma) * std.exp(-Sqr(x - mu) / (2 * sigma * sigma)));
                pstd.vector<Float> values = Sample1DFunction(normal, 8192, 16, mu - 7 * sigma, mu + 7 * sigma);
                PiecewiseConstant1D distrib = new PiecewiseConstant1D(values, mu - 7 * sigma, mu + 7 * sigma);

                foreach (Float u in new Uniform1D(100))
                {
                    Float cx = SampleNormal(u, mu, sigma);
                    Float cp = NormalPDF(cx, mu, sigma);

                    Float dp;
                    Float dx = distrib.Sample(u, &dp);
                    EXPECT_LT(std.abs(cx - dx), 3e-3, "Closed form = " + cx + ", distrib = " + dx);
                    EXPECT_LT(std.abs(cp - dp) / dp, .025, "Closed form PDF = " + cp + ", distrib PDF = " + dp);

                    EXPECT_FALSE(checkErr(u, InvertNormalSample(cx, mu, sigma)),
                                 " u " + u + " -> x = " + cx + " -> " + InvertNormalSample(cx, mu, sigma) +
                                 " with mu " + mu + " and sigma " + sigma);
                }
            }
        }

        [TEST("VarianceEstimator", "Zero")]
        public static void VarianceEstimator_Zero()
        {
            VarianceEstimator<Float> ve = new VarianceEstimator<Float>();
            for (int i = 0; i < 100; ++i)
                ve.Add(10);
            EXPECT_EQ(ve.Variance(), 0);
        }

        [TEST("VarianceEstimator", "VsClosedForm")]
        public static void VarianceEstimator_VsClosedForm()
        {
            VarianceEstimator<double> ve = new VarianceEstimator<double>();
            int count = 10000;
            double sum = 0;
            foreach (Float u in new Stratified1D(count))
            {
                Float v = Lerp(u, -1, 1);
                ve.Add(v);
                sum += v;
            }

            // f(x) = 0, random variables x_i uniform in [-1,1] ->
            // variance is E[x^2] on [-1,1] == 1/3
            Float err = (Float)std.abs(ve.Variance() - 1.0 / 3.0);
            EXPECT_LT(err, 1e-3, ve.Variance().ToString());

            err = (Float)std.abs((sum / count - ve.Mean()) / (sum / count));
            EXPECT_LT(err, 1e-5);
        }

        [TEST("VarianceEstimator", "Merge")]
        public static void VarianceEstimator_Merge()
        {
            int n = 16;
            VarianceEstimator<double>[] ve = new VarianceEstimator<double>[n];

            RNG rng = new RNG();
            int count = 10000;
            double sum = 0;
            foreach (Float u in new Stratified1D(count))
            {
                Float v = Lerp(u, -1, 1);
                int index = rng.Uniform<int>(ve.Length);
                ve[index].Add(v);
                sum += v;
            }

            VarianceEstimator<double> veFinal = new VarianceEstimator<double>();
            foreach (VarianceEstimator<double> v in ve)
                veFinal.Merge(v);

            // f(x) = 0, random variables x_i uniform in [-1,1] ->
            // variance is E[x^2] on [-1,1] == 1/3
            Float err = (Float)std.abs(veFinal.Variance() - 1.0 / 3.0);
            EXPECT_LT(err, 1e-3, veFinal.Variance().ToString());

            err = (Float)std.abs((sum / count - veFinal.Mean()) / (sum / count));
            EXPECT_LT(err, 1e-5);
        }

        [TEST("VarianceEstimator", "MergeTwo")]
        public static void VarianceEstimator_MergeTwo()
        {
            VarianceEstimator<double>[] ve = new VarianceEstimator<double>[2];
            VarianceEstimator<double> veBoth = new VarianceEstimator<double>();

            int index = 0;
            foreach (Float u in new Stratified1D(1000))
            {
                if (index++ < 400)
                    ve[0].Add(u * u * u);
                else
                    ve[1].Add(u * u * u);
                veBoth.Add(u * u * u);
            }

            ve[0].Merge(ve[1]);

            Float meanErr = (Float)(std.abs(ve[0].Mean() - veBoth.Mean()) / veBoth.Mean());
            EXPECT_LT(meanErr, 1e-5);

            Float varError = (Float)(std.abs(ve[0].Variance() - veBoth.Variance()) / veBoth.Variance());
            EXPECT_LT(varError, 1e-5);
        }

        [TEST("WeightedReservoir", "Basic")]
        public static void WeightedReservoir_Basic()
        {
            RNG rng = new RNG();
            const int n = 16;
            float[] weights = new float[n];
            for (int i = 0; i < n; ++i)
                weights[i] = (float)(.05 + Sqr(rng.Uniform<Float>()));

            int[] count = new int[n];
            int64_t nTrials = 1000000;
            ParallelFor(0, nTrials, (long start, long end) =>
            {
                int[] localCount = new int[n];

                for (long i = start; i < end; ++i)
                {
                    WeightedReservoirSampler<int> wrs = new WeightedReservoirSampler<int>((uint64_t)i);
                    int[] perm = new int[n];
                    for (int j = 0; j < n; ++j)
                        perm[j] = j;

                    for (int j = 0; j < n; ++j)
                    {
                        int index = perm[j];
                        wrs.Add(index, weights[index]);
                    }

                    int idx = wrs.GetSample();
                    ASSERT_TRUE(idx >= 0 && idx < n);
                    ++localCount[idx];
                }

                for (int i = 0; i < n; ++i)
                    Interlocked.Add(ref count[i], localCount[i]);
            });

            Float sumW = 0;
            for (int i = 0; i < n; ++i)
                sumW += weights[i];
            for (int i = 0; i < n; ++i)
            {
                EXPECT_LE(.97 * count[i] / (double)nTrials, weights[i] / sumW);
                EXPECT_GE(1.03 * count[i] / (double)nTrials, weights[i] / sumW);
            }
        }

        [TEST("WeightedReservoir", "MergeReservoirs")]
        public static void WeightedReservoir_MergeReservoirs()
        {
            RNG rng = new RNG(6502);
            const int n = 8;
            float[] weights = new float[n];
            for (int i = 0; i < n; ++i)
                weights[i] = (float)(.01 + rng.Uniform<Float>());

            int[] count = new int[n];
            int64_t nTrials = 1000000;
            ParallelFor(0, nTrials, (long start, long end) =>
            {
                int[] localCount = new int[n];

                for (long i = start; i < end; ++i)
                {
                    WeightedReservoirSampler<int> wrs0 = new WeightedReservoirSampler<int>((uint64_t)i);
                    WeightedReservoirSampler<int> wrs1 = new WeightedReservoirSampler<int>((uint64_t)(i + 1));

                    for (int j = 0; j < n; ++j)
                    {
                        if ((j & 1) != 0)
                            wrs0.Add(j, weights[j]);
                        else
                            wrs1.Add(j, weights[j]);
                    }

                    wrs0.Merge(wrs1);
                    ++localCount[wrs0.GetSample()];
                }

                for (int i = 0; i < n; ++i)
                    Interlocked.Add(ref count[i], localCount[i]);
            });

            Float sumW = 0;
            for (int i = 0; i < n; ++i)
                sumW += weights[i];
            for (int i = 0; i < n; ++i)
            {
                EXPECT_LE(.98 * count[i] / (double)nTrials, weights[i] / sumW);
                EXPECT_GE(1.02 * count[i] / (double)nTrials, weights[i] / sumW);
            }
        }

        [TEST("Generators", "Uniform1D")]
        public static void Generators_Uniform1D()
        {
            int count = 0;
            foreach (Float u in new Uniform1D(120))
            {
                EXPECT_TRUE(u >= 0 && u < 1);
                ++count;
            }
            EXPECT_EQ(120, count);
        }

        [TEST("Generators", "Uniform1DSeed")]
        public static void Generators_Uniform1DSeed()
        {
            System.Collections.Generic.List<Float> samples = new System.Collections.Generic.List<Float>();
            foreach (Float u in new Uniform1D(1250))
                samples.Add(u);

            // Different seed
            int i = 0;
            foreach (Float u in new Uniform1D(samples.Count, 1))
            {
                EXPECT_NE(u, samples[i]);
                ++i;
            }
        }

        [TEST("Generators", "Uniform2D")]
        public static void Generators_Uniform2D()
        {
            int count = 0;
            foreach (Point2f u in new Uniform2D(32))
            {
                EXPECT_TRUE(u[0] >= 0 && u[0] < 1 && u[1] >= 0 && u[1] < 1);
                ++count;
            }
            EXPECT_EQ(32, count);
        }

        [TEST("Generators", "Uniform2DSeed")]
        public static void Generators_Uniform2DSeed()
        {
            System.Collections.Generic.List<Point2f> samples = new System.Collections.Generic.List<Point2f>();
            foreach (Point2f u in new Uniform2D(83))
                samples.Add(u);

            // Different seed
            int i = 0;
            foreach (Point2f u in new Uniform2D(samples.Count, 1))
            {
                EXPECT_NE(u, samples[i]);
                ++i;
            }
        }

        [TEST("Generators", "Uniform3D")]
        public static void Generators_Uniform3D()
        {
            int count = 0;
            foreach (Point3f u in new Uniform3D(32))
            {
                EXPECT_TRUE(u[0] >= 0 && u[0] < 1 && u[1] >= 0 && u[1] < 1 && u[2] >= 0 && u[2] < 1);
                ++count;
            }
            EXPECT_EQ(32, count);
        }

        [TEST("Generators", "Stratified1D")]
        public static void Generators_Stratified1D()
        {
            int count = 0, n = 128;  // power of 2
            foreach (Float u in new Stratified1D(n))
            {
                EXPECT_TRUE(u >= (Float)count / (Float)n && u < (Float)(count + 1) / (Float)n);
                ++count;
            }
            EXPECT_EQ(n, count);
        }

        [TEST("Generators", "Stratified2D")]
        public static void Generators_Stratified2D()
        {
            int count = 0, nx = 16, ny = 4;  // power of 2
            foreach (Point2f u in new Stratified2D(nx, ny))
            {
                int ix = count % nx;
                int iy = count / nx;
                EXPECT_TRUE(u[0] >= (Float)ix / (Float)nx && u[0] < (Float)(ix + 1) / (Float)nx);
                EXPECT_TRUE(u[1] >= (Float)iy / (Float)ny && u[1] < (Float)(iy + 1) / (Float)ny);
                ++count;
            }
            EXPECT_EQ(nx * ny, count);
        }

        [TEST("Generators", "Stratified3D")]
        public static void Generators_Stratified3D()
        {
            int count = 0, nx = 4, ny = 32, nz = 8;  // power of 2
            foreach (Point3f u in new Stratified3D(nx, ny, nz))
            {
                int ix = count % nx;
                int iy = (count / nx) % ny;
                int iz = count / (nx * ny);
                EXPECT_TRUE(u[0] >= (Float)ix / (Float)nx && u[0] < (Float)(ix + 1) / (Float)nx);
                EXPECT_TRUE(u[1] >= (Float)iy / (Float)ny && u[1] < (Float)(iy + 1) / (Float)ny);
                EXPECT_TRUE(u[2] >= (Float)iz / (Float)nz && u[2] < (Float)(iz + 1) / (Float)nz);
                ++count;
            }
            EXPECT_EQ(nx * ny * nz, count);
        }

        [TEST("Generators", "Hammersley2D")]
        public static void Generators_Hammersley2D()
        {
            int count = 0;
            foreach (Point2f u in new Hammersley2D(32))
            {
                EXPECT_EQ((Float)count / 32.0f, u[0]);
                EXPECT_EQ(RadicalInverse(0, (uint64_t)count), u[1]);
                ++count;
            }
            EXPECT_EQ(32, count);
        }

        [TEST("Generators", "Hammersley3D")]
        public static void Generators_Hammersley3D()
        {
            int count = 0;
            foreach (Point3f u in new Hammersley3D(128))
            {
                EXPECT_EQ((Float)count / 128.0f, u[0]);
                EXPECT_EQ(RadicalInverse(0, (uint64_t)count), u[1]);
                EXPECT_EQ(RadicalInverse(1, (uint64_t)count), u[2]);
                ++count;
            }
            EXPECT_EQ(128, count);
        }

        [TEST("AliasTable", "Uniform")]
        public static void AliasTable_Uniform()
        {
            Float[] values = { 2, 2, 2, 2, 2 };

            AliasTable table = new AliasTable(Sp(values));
            EXPECT_EQ(5, table.size());

            int[] count = new int[5];
            int iters = 10000;
            foreach (Float u in new Stratified1D(iters))
            {
                Float pdf;
                int offset = table.Sample(u, &pdf);
                ASSERT_TRUE(offset >= 0 && offset < 5);
                ++count[offset];
                EXPECT_GT(pdf, .99 * 1.0f / 5.0f);
                EXPECT_LT(pdf, 1.01 * 1.0f / 5.0f);
            }

            for (int i = 0; i < 5; ++i)
            {
                EXPECT_GT((Float)count[i] / iters, .99f * 1.0f / 5.0f);
                EXPECT_LT((Float)count[i] / iters, 1.01f * 1.0f / 5.0f);
            }
        }

        [TEST("AliasTable", "Varying")]
        public static void AliasTable_Varying()
        {
            RNG rng = new RNG();
            int n = 103;
            Float[] values = new Float[n];
            for (int i = 0; i < n; ++i)
                values[i] = .25f * 2 * rng.Uniform<Float>();

            Float sum = 0;
            for (int i = 0; i < n; ++i)
                sum += values[i];

            AliasTable table = new AliasTable(Sp(values));
            EXPECT_EQ(n, values.Length);

            int[] count = new int[n];
            int iters = 1000000;
            foreach (Float u in new Stratified1D(iters))
            {
                Float pdf;
                int offset = table.Sample(u, &pdf);
                ASSERT_TRUE(offset >= 0 && offset < n);
                ++count[offset];

                Float refPdf = values[offset] / sum;
                EXPECT_GT(pdf, .99 * refPdf);
                EXPECT_LT(pdf, 1.01 * refPdf);
            }

            for (int i = 0; i < n; ++i)
            {
                Float pdf = values[i] / sum;
                EXPECT_GT((Float)count[i] / iters, .99f * pdf);
                EXPECT_LT((Float)count[i] / iters, 1.01f * pdf);
            }
        }

        [TEST("SummedArea", "Constant")]
        public static void SummedArea_Constant()
        {
            Array2D<Float> v = new Array2D<Float>(4, 4);

            for (int y = 0; y < v.YSize(); ++y)
                for (int x = 0; x < v.XSize(); ++x)
                    v[x, y] = 1;

            SummedAreaTable sat = new SummedAreaTable(v);

            EXPECT_EQ(1, sat.Integral(new Bounds2f(new Point2f(0, 0), new Point2f(1, 1))));
            EXPECT_EQ(0.5, sat.Integral(new Bounds2f(new Point2f(0, 0), new Point2f(1, 0.5f))));
            EXPECT_EQ(0.5, sat.Integral(new Bounds2f(new Point2f(0, 0), new Point2f(0.5f, 1))));
            EXPECT_EQ(3.0 / 16.0, sat.Integral(new Bounds2f(new Point2f(0, 0), new Point2f(.25f, .75f))));
            EXPECT_EQ(3.0 / 16.0, sat.Integral(new Bounds2f(new Point2f(0.5f, 0.25f), new Point2f(0.75f, 1))));
        }

        [TEST("SummedArea", "Rect")]
        public static void SummedArea_Rect()
        {
            Array2D<Float> v = new Array2D<Float>(8, 4);

            for (int y = 0; y < v.YSize(); ++y)
                for (int x = 0; x < v.XSize(); ++x)
                    v[x, y] = x + y;

            SummedAreaTable sat = new SummedAreaTable(v);

            // All boxes that line up with boundaries exactly
            for (int y0 = 0; y0 < v.YSize(); ++y0)
                for (int x0 = 0; x0 < v.XSize(); ++x0)
                    for (int y1 = y0; y1 < v.YSize(); ++y1)
                        for (int x1 = x0; x1 < v.XSize(); ++x1)
                        {
                            Float mySum = 0;
                            for (int y = y0; y < y1; ++y)
                                for (int x = x0; x < x1; ++x)
                                    mySum += v[x, y];

                            Bounds2f b = new Bounds2f(new Point2f((Float)x0 / v.XSize(), (Float)y0 / v.YSize()),
                                                      new Point2f((Float)x1 / v.XSize(), (Float)y1 / v.YSize()));
                            EXPECT_EQ(mySum / (v.XSize() * v.YSize()), sat.Integral(b));
                        }
        }

        [TEST("SummedArea", "Randoms")]
        public static void SummedArea_Randoms()
        {
            int[][] dims = { new int[] { 1, 6 }, new int[] { 6, 1 }, new int[] { 12, 19 }, new int[] { 16, 16 },
                             new int[] { 100, 300 }, new int[] { 49, 2 } };
            RNG rng = new RNG();
            foreach (int[] d in dims)
            {
                Array2D<Float> v = new Array2D<Float>(d[0], d[1]);

                for (int y = 0; y < v.YSize(); ++y)
                    for (int x = 0; x < v.XSize(); ++x)
                        v[x, y] = rng.Uniform<int>(32);

                SummedAreaTable sat = new SummedAreaTable(v);

                for (int i = 0; i < 100; ++i)
                {
                    // gcc evaluates the second Point2i argument first; Bounds2i is symmetric in
                    // its two corners, so the resulting box is identical either way.
                    int q3 = rng.Uniform<int>(v.XSize()), q4 = rng.Uniform<int>(v.YSize());
                    int q1 = rng.Uniform<int>(v.XSize()), q2 = rng.Uniform<int>(v.YSize());
                    Bounds2i bi = new Bounds2i(new Point2i(q1, q2), new Point2i(q3, q4));
                    Bounds2f bf = new Bounds2f(new Point2f((Float)bi.pMin.x / (Float)v.XSize(),
                                                           (Float)bi.pMin.y / (Float)v.YSize()),
                                               new Point2f((Float)bi.pMax.x / (Float)v.XSize(),
                                                           (Float)bi.pMax.y / (Float)v.YSize()));
                    double refv = 0;

                    foreach (Point2i p in bi)
                        refv += v[p];
                    refv /= v.XSize() * v.YSize();

                    double s = sat.Integral(bf);
                    if (refv != s)
                    {
                        EXPECT_LT(std.abs((refv - s) / refv), 1e-3f, StringPrintf("ref %f s %f", refv, s));
                    }
                }
            }
        }

        [TEST("SummedArea", "NonCellAligned")]
        public static void SummedArea_NonCellAligned()
        {
            int[][] dims = { new int[] { 1, 6 }, new int[] { 6, 1 }, new int[] { 12, 19 }, new int[] { 16, 16 },
                             new int[] { 100, 300 }, new int[] { 49, 2 } };
            RNG rng = new RNG();
            foreach (int[] d in dims)
            {
                Array2D<Float> v = new Array2D<Float>(d[0], d[1]);

                for (int y = 0; y < v.YSize(); ++y)
                    for (int x = 0; x < v.XSize(); ++x)
                        v[x, y] = rng.Uniform<int>(32);

                SummedAreaTable sat = new SummedAreaTable(v);

                Float b3 = rng.Uniform<Float>(), b4 = rng.Uniform<Float>();
                Float b1 = rng.Uniform<Float>(), b2 = rng.Uniform<Float>();
                Bounds2f b = new Bounds2f(new Point2f(b1, b2), new Point2f(b3, b4));

                Float sampledSum = 0;
                int nSamples = 100000;
                foreach (Point2f u in new Hammersley2D(nSamples))
                {
                    Point2f p = b.Lerp(u);
                    Point2i pi = new Point2i((int)(p.x * v.XSize()), (int)(p.y * v.YSize()));
                    sampledSum += v[pi];
                }
                Float sampledResult = sampledSum * b.Area() / nSamples;

                double s = sat.Integral(b);
                if (sampledResult != s)
                {
                    EXPECT_LT(std.abs((sampledResult - s) / sampledResult), 1e-3f,
                              StringPrintf("sampled %f s %f", sampledResult, s));
                }
            }
        }

        [TEST("Sampling", "HGExtremes")]
        public static void Sampling_HGExtremes()
        {
            foreach (Float g in new Float[] { -.999f, .999f, -1.0f, 1.0f })
            {
                foreach (Float u in new Float[] { 0, OneMinusEpsilon })
                {
                    Float pdf;
                    Vector3f w = SampleHenyeyGreenstein(new Vector3f(0, 0, -1), g, new Point2f(u, 0.5f), &pdf);
                    EXPECT_FALSE(w.HasNaN());
                }
            }
        }
    }
}
