// bsdfs_test.cpp
//
// Differences from the C++ test: std::function becomes Func<>/delegates; the chi^2 frequency
// tables are written to "chi2test_<description>_NNN.m" in the current directory rather than
// /tmp (the C++ test ignores a failure to open the file; so does this one); Disk takes
// Transform values rather than pointers; the "#if 0" tests (Hair.Reciprocity and the
// MicrofacetReflectionBxDF energy-conservation tests, which refer to classes that no longer
// exist in pbrt-v4) are not ported.

using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using testing;

namespace pbrt
{
    public static unsafe class BSDFsTests
    {
        /* The null hypothesis will be rejected when the associated
           p-value is below the significance level specified here. */
        private const double CHI2_SLEVEL = 0.01;

        /* Resolution of the frequency table discretization. The azimuthal
           resolution is twice this value. */
        private const int CHI2_THETA_RES = 80;
        private const int CHI2_PHI_RES = (2 * CHI2_THETA_RES);

        /* Number of MC samples to compute the observed frequency table */
        private const int CHI2_SAMPLECOUNT = 1000000;

        /* Minimum expected bin frequency. The chi^2 test does not
           work reliably when the expected frequency in a cell is
           low (e.g. less than 5), because normality assumptions
           break down in this case. Therefore, the implementation
           will merge such low-frequency cells when they fall below
           the threshold specified here. */
        private const int CHI2_MINFREQ = 5;

        /* Each provided BSDF will be tested for a few different
           incident directions. The value specified here determines
           how many tests will be executed per BSDF */
        private const int CHI2_RUNS = 5;

        /// Regularized lower incomplete gamma function (based on code from Cephes)
        private static double RLGamma(double a, double x)
        {
            const double epsilon = 0.000000000000001;
            const double big = 4503599627370496.0;
            const double bigInv = 2.22044604925031308085e-16;
            if (a < 0 || x < 0)
                throw new InvalidOperationException("LLGamma: invalid arguments range!");

            if (x == 0)
                return 0.0f;

            double ax = (a * std.log(x)) - x - std.lgamma(a);
            if (ax < -709.78271289338399)
                return a < x ? 1.0 : 0.0;

            if (x <= 1 || x <= a)
            {
                double r2 = a;
                double c2 = 1;
                double ans2 = 1;

                do
                {
                    r2 = r2 + 1;
                    c2 = c2 * x / r2;
                    ans2 += c2;
                } while ((c2 / ans2) > epsilon);

                return std.exp(ax) * ans2 / a;
            }

            int c = 0;
            double y = 1 - a;
            double z = x + y + 1;
            double p3 = 1;
            double q3 = x;
            double p2 = x + 1;
            double q2 = z * x;
            double ans = p2 / q2;
            double error;

            do
            {
                c++;
                y += 1;
                z += 2;
                double yc = y * c;
                double p = (p2 * z) - (p3 * yc);
                double q = (q2 * z) - (q3 * yc);

                if (q != 0)
                {
                    double nextans = p / q;
                    error = std.abs((ans - nextans) / nextans);
                    ans = nextans;
                }
                else
                {
                    // zero div, skip
                    error = 1;
                }

                // shift
                p3 = p2;
                p2 = p;
                q3 = q2;
                q2 = q;

                // normalize fraction when the numerator becomes large
                if (std.abs(p) > big)
                {
                    p3 *= bigInv;
                    p2 *= bigInv;
                    q3 *= bigInv;
                    q2 *= bigInv;
                }
            } while (error > epsilon);

            return 1.0 - (std.exp(ax) * ans);
        }

        /// Chi^2 distribution cumulative distribution function
        private static double Chi2CDF(double x, int dof)
        {
            if (dof < 1 || x < 0)
            {
                return 0.0;
            }
            else if (dof == 2)
            {
                return 1.0 - std.exp(-0.5 * x);
            }
            else
            {
                return (Float)RLGamma(0.5 * dof, 0.5 * x);
            }
        }

        private static int adaptiveCount;

        private static Float Integrate(Func<Float, Float> f, Float a, Float b, Float c, Float fa, Float fb, Float fc,
                                       Float I, Float eps, int depth)
        {
            /* Evaluate the function at two intermediate points */
            Float d = 0.5f * (a + b), e = 0.5f * (b + c), fd = f(d), fe = f(e);

            /* Simpson integration over each subinterval */
            Float h = c - a, I0 = (Float)(1.0 / 12.0) * h * (fa + 4 * fd + fb),
                  I1 = (Float)(1.0 / 12.0) * h * (fb + 4 * fe + fc), Ip = I0 + I1;
            ++adaptiveCount;

            /* Stopping criterion from J.N. Lyness (1969)
              "Notes on the adaptive Simpson quadrature routine" */
            if (depth <= 0 || std.abs(Ip - I) < 15 * eps)
            {
                // Richardson extrapolation
                return Ip + (Float)(1.0 / 15.0) * (Ip - I);
            }

            // (C++ evaluates the operands of + in unspecified order; the sum is commutative
            // and the two calls are independent.)
            return Integrate(f, a, d, b, fa, fd, fb, I0, .5f * eps, depth - 1) +
                   Integrate(f, b, e, c, fb, fe, fc, I1, .5f * eps, depth - 1);
        }

        /// Adaptive Simpson integration over an 1D interval
        private static Float AdaptiveSimpson(Func<Float, Float> f, Float x0, Float x1, Float eps = 1e-6f, int depth = 6)
        {
            adaptiveCount = 0;
            Float a = x0, b = 0.5f * (x0 + x1), c = x1;
            Float fa = f(a), fb = f(b), fc = f(c);
            Float I = (c - a) * (Float)(1.0 / 6.0) * (fa + 4 * fb + fc);
            return Integrate(f, a, b, c, fa, fb, fc, I, eps, depth);
        }

        /// Nested adaptive Simpson integration over a 2D rectangle
        private static Float AdaptiveSimpson2D(Func<Float, Float, Float> f, Float x0, Float y0, Float x1, Float y1,
                                               Float eps = 1e-6f, int depth = 6)
        {
            /* Lambda function that integrates over the X axis */
            Func<Float, Float> integrate = (Float y) =>
                AdaptiveSimpson((Float x) => f(x, y), x0, x1, eps, depth);
            Float value = AdaptiveSimpson(integrate, y0, y1, eps, depth);
            return value;
        }

        /// Generate a histogram of the BSDF density function via MC sampling
        private static void FrequencyTable(BSDF bsdf, Vector3f wo, ref RNG rng, int sampleCount, int thetaRes,
                                           int phiRes, Float[] target)
        {
            Array.Clear(target, 0, thetaRes * phiRes);

            Float factorTheta = thetaRes / Pi, factorPhi = phiRes / (2 * Pi);

            for (int i = 0; i < sampleCount; ++i)
            {
                Float u = rng.Uniform<Float>();
                // Braced-init-list: left-to-right.
                Float sx = rng.Uniform<Float>();
                Float sy = rng.Uniform<Float>();
                Point2f sample = new Point2f(sx, sy);
                pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, u, sample);

                if (!bs.has_value() || bs.value().IsSpecular())
                    continue;

                Vector3f wiL = bsdf.RenderToLocal(bs.value().wi);

                Point2f coords = new Point2f(SafeACos(wiL.z) * factorTheta,
                                             std.atan2(wiL.y, wiL.x) * factorPhi);

                if (coords.y < 0)
                    coords.y += 2 * Pi * factorPhi;

                int thetaBin = std.min(std.max(0, (int)std.floor(coords.x)), thetaRes - 1);
                int phiBin = std.min(std.max(0, (int)std.floor(coords.y)), phiRes - 1);

                target[thetaBin * phiRes + phiBin] += 1;
            }
        }

        // Numerically integrate the probability density function over rectangles in
        // spherical coordinates.
        private static void IntegrateFrequencyTable(BSDF bsdf, Vector3f wo, int sampleCount, int thetaRes, int phiRes,
                                                    Float[] target)
        {
            Array.Clear(target, 0, thetaRes * phiRes);

            Float factorTheta = Pi / thetaRes, factorPhi = (2 * Pi) / phiRes;

            int t = 0;
            for (int i = 0; i < thetaRes; ++i)
            {
                for (int j = 0; j < phiRes; ++j)
                {
                    target[t++] =
                        sampleCount *
                        AdaptiveSimpson2D(
                            (Float theta, Float phi) =>
                            {
                                Float cosTheta = std.cos(theta), sinTheta = std.sin(theta);
                                Float cosPhi = std.cos(phi), sinPhi = std.sin(phi);
                                Vector3f wiL = new Vector3f(sinTheta * cosPhi, sinTheta * sinPhi, cosTheta);
                                return bsdf.PDF(wo, bsdf.LocalToRender(wiL)) * sinTheta;
                            },
                            i * factorTheta, j * factorPhi, (i + 1) * factorTheta, (j + 1) * factorPhi);
                }
            }
        }

        private static string F(Float v) { return v.ToString("G6", CultureInfo.InvariantCulture); }

        /// Write the frequency tables to disk in a format that is nicely plottable by
        /// Octave and MATLAB
        private static void DumpTables(Float[] frequencies, Float[] expFrequencies, int thetaRes, int phiRes,
                                       string filename)
        {
            StreamWriter f;
            try { f = new StreamWriter(filename); }
            catch (Exception) { return; }

            f.Write("frequencies = [ ");
            for (int i = 0; i < thetaRes; ++i)
            {
                for (int j = 0; j < phiRes; ++j)
                {
                    f.Write(F(frequencies[i * phiRes + j]));
                    if (j + 1 < phiRes)
                        f.Write(", ");
                }
                if (i + 1 < thetaRes)
                    f.Write("; ");
            }
            f.Write(" ];\nexpFrequencies = [ ");
            for (int i = 0; i < thetaRes; ++i)
            {
                for (int j = 0; j < phiRes; ++j)
                {
                    f.Write(F(expFrequencies[i * phiRes + j]));
                    if (j + 1 < phiRes)
                        f.Write(", ");
                }
                if (i + 1 < thetaRes)
                    f.Write("; ");
            }
            f.Write(" ];\n" +
                    "colormap(jet);\n" +
                    "clf; subplot(2,1,1);\n" +
                    "imagesc(frequencies);\n" +
                    "title('Observed frequencies');\n" +
                    "axis equal;\n" +
                    "subplot(2,1,2);\n" +
                    "imagesc(expFrequencies);\n" +
                    "axis equal;\n" +
                    "title('Expected frequencies');\n");
            f.Close();
        }

        private struct Cell
        {
            public Float expFrequency;
            public long index;
        }

        /// Run A Chi^2 test based on the given frequency tables
        private static bool Chi2Test(Float[] frequencies, Float[] expFrequencies, int thetaRes, int phiRes,
                                     int sampleCount, Float minExpFrequency, Float significanceLevel, int numTests,
                                     out string result)
        {
            result = "";

            /* Sort all cells by their expected frequencies */
            Cell[] cells = new Cell[thetaRes * phiRes];
            for (int i = 0; i < cells.Length; ++i)
            {
                cells[i].expFrequency = expFrequencies[i];
                cells[i].index = i;
            }
            Array.Sort(cells, (Cell a, Cell b) => a.expFrequency.CompareTo(b.expFrequency));

            /* Compute the Chi^2 statistic and pool cells as necessary */
            Float pooledFrequencies = 0, pooledExpFrequencies = 0, chsq = 0;
            int pooledCells = 0, dof = 0;

            foreach (Cell c in cells)
            {
                if (expFrequencies[c.index] == 0)
                {
                    if (frequencies[c.index] > sampleCount * 1e-5f)
                    {
                        /* Uh oh: samples in a c that should be completely empty
                           according to the probability density function. Ordinarily,
                           even a single sample requires immediate rejection of the null
                           hypothesis. But due to finite-precision computations and
                           rounding
                           errors, this can occasionally happen without there being an
                           actual bug. Therefore, the criterion here is a bit more
                           lenient. */

                        result = pbrt.StringPrintf("Encountered %f samples in a c with expected " +
                                                      "frequency 0. Rejecting the null hypothesis!",
                                                      frequencies[c.index]);
                        return false;
                    }
                }
                else if (expFrequencies[c.index] < minExpFrequency)
                {
                    /* Pool cells with low expected frequencies */
                    pooledFrequencies += frequencies[c.index];
                    pooledExpFrequencies += expFrequencies[c.index];
                    pooledCells++;
                }
                else if (pooledExpFrequencies > 0 && pooledExpFrequencies < minExpFrequency)
                {
                    /* Keep on pooling cells until a sufficiently high
                       expected frequency is achieved. */
                    pooledFrequencies += frequencies[c.index];
                    pooledExpFrequencies += expFrequencies[c.index];
                    pooledCells++;
                }
                else
                {
                    Float diff = frequencies[c.index] - expFrequencies[c.index];
                    chsq += (diff * diff) / expFrequencies[c.index];
                    ++dof;
                }
            }

            if (pooledExpFrequencies > 0 || pooledFrequencies > 0)
            {
                Float diff = pooledFrequencies - pooledExpFrequencies;
                chsq += (diff * diff) / pooledExpFrequencies;
                ++dof;
            }

            /* All parameters are assumed to be known, so there is no
               additional DF reduction due to model parameters */
            dof -= 1;

            if (dof <= 0)
            {
                result = pbrt.StringPrintf("The number of degrees of freedom %d is too low!", dof);
                return false;
            }

            /* Probability of obtaining a test statistic at least
               as extreme as the one observed under the assumption
               that the distributions match */
            Float pval = 1 - (Float)Chi2CDF(chsq, dof);

            /* Apply the Sidak correction term, since we'll be conducting multiple
               independent
               hypothesis tests. This accounts for the fact that the probability of a
               failure
               increases quickly when several hypothesis tests are run in sequence. */
            Float alpha = 1.0f - std.pow(1.0f - significanceLevel, 1.0f / numTests);

            if (pval < alpha || !std.isfinite(pval))
            {
                result = pbrt.StringPrintf("Rejected the null hypothesis (p-value = %f, " +
                                              "significance level = %f", pval, alpha);
                return false;
            }
            else
            {
                return true;
            }
        }

        private static BSDF DiskBSDF(Func<SurfaceInteraction, Allocator, BSDF> createBSDF, out SurfaceInteraction hit)
        {
            // Create BSDF, which requires creating a Shape, casting a Ray that
            // hits the shape to get a SurfaceInteraction object.
            Transform t = RotateX(-90);
            Transform tInv = Inverse(t);
            bool reverseOrientation = false;

            Disk disk = new Disk(t, tInv, reverseOrientation, 0.0f, 1.0f, 0, 360.0f);
            Point3f origin = new Point3f(0.1f, 1, 0);  // offset slightly so we don't hit center of disk
            Vector3f direction = new Vector3f(0, -1, 0);
            Ray r = new Ray(origin, direction);
            pstd.optional<ShapeIntersection> si = disk.Intersect(r);
            ASSERT_TRUE(si.has_value());
            hit = si.value().intr;
            return createBSDF(hit, new Allocator());
        }

        private static void TestBSDF(Func<SurfaceInteraction, Allocator, BSDF> createBSDF, string description)
        {
            const int thetaRes = CHI2_THETA_RES;
            const int phiRes = CHI2_PHI_RES;
            const int sampleCount = CHI2_SAMPLECOUNT;
            Float[] frequencies = new Float[thetaRes * phiRes];
            Float[] expFrequencies = new Float[thetaRes * phiRes];
            RNG rng = new RNG();

            int index = 0;

            SurfaceInteraction hit;
            BSDF bsdf = DiskBSDF(createBSDF, out hit);

            for (int k = 0; k < CHI2_RUNS; ++k)
            {
                /* Randomly pick an outgoing direction on the hemisphere */
                Float sx = rng.Uniform<Float>();
                Float sy = rng.Uniform<Float>();
                Point2f sample = new Point2f(sx, sy);
                Vector3f woL = SampleCosineHemisphere(sample);
                Vector3f wo = bsdf.LocalToRender(woL);

                FrequencyTable(bsdf, wo, ref rng, sampleCount, thetaRes, phiRes, frequencies);

                IntegrateFrequencyTable(bsdf, wo, sampleCount, thetaRes, phiRes, expFrequencies);

                string filename = pbrt.StringPrintf("chi2test_%s_%03d.m", description, ++index);
                DumpTables(frequencies, expFrequencies, thetaRes, phiRes, filename);

                string msg;
                bool ok = Chi2Test(frequencies, expFrequencies, thetaRes, phiRes, sampleCount, CHI2_MINFREQ,
                                   (Float)CHI2_SLEVEL, CHI2_RUNS, out msg);
                EXPECT_TRUE(ok, msg + ", iteration " + k);
            }
        }

        private static BSDF createLambertian(SurfaceInteraction si, Allocator alloc)
        {
            SampledSpectrum Kd = new SampledSpectrum(1.0f);
            return new BSDF(si.shading.n, si.shading.dpdu, new DiffuseBxDF(Kd));
        }

        [TEST("BSDFSampling", "Lambertian")]
        public static void BSDFSampling_Lambertian()
        {
            TestBSDF(createLambertian, "Lambertian");
        }

        private static BSDF createConductorMicrofacet(SurfaceInteraction si, Allocator alloc, float eta, float k,
                                                      float roughx, float roughy)
        {
            Float alphax = TrowbridgeReitzDistribution.RoughnessToAlpha(roughx);
            Float alphay = TrowbridgeReitzDistribution.RoughnessToAlpha(roughy);
            TrowbridgeReitzDistribution distrib = new TrowbridgeReitzDistribution(alphax, alphay);
            return new BSDF(si.shading.n, si.shading.dpdu,
                            new ConductorBxDF(distrib, new SampledSpectrum(eta), new SampledSpectrum(k)));
        }

        private static BSDF createDielectricMicrofacet(SurfaceInteraction si, Allocator alloc, float ior,
                                                       float roughx, float roughy)
        {
            Float alphax = TrowbridgeReitzDistribution.RoughnessToAlpha(roughx);
            Float alphay = TrowbridgeReitzDistribution.RoughnessToAlpha(roughy);
            TrowbridgeReitzDistribution distrib = new TrowbridgeReitzDistribution(alphax, alphay);
            return new BSDF(si.shading.n, si.shading.dpdu, new DielectricBxDF(ior, distrib));
        }

        [TEST("BSDFSampling", "TRCondIso")]
        public static void BSDFSampling_TRCondIso()
        {
            TestBSDF((SurfaceInteraction si, Allocator alloc) => createConductorMicrofacet(si, alloc, 2.0f, 4.0f, 0.5f, 0.5f),
                     "TRCondIso");
        }

        [TEST("BSDFSampling", "TRCondAniso")]
        public static void BSDFSampling_TRCondAniso()
        {
            TestBSDF((SurfaceInteraction si, Allocator alloc) => createConductorMicrofacet(si, alloc, 2.0f, 4.0f, 0.3f, 0.15f),
                     "TRCondAniso");
        }

        [TEST("BSDFSampling", "TRDielIso")]
        public static void BSDFSampling_TRDielIso()
        {
            TestBSDF((SurfaceInteraction si, Allocator alloc) => createDielectricMicrofacet(si, alloc, 1.5f, 0.5f, 0.5f),
                     "TRDielIso");
        }

        [TEST("BSDFSampling", "TRDielAniso")]
        public static void BSDFSampling_TRDielAniso()
        {
            TestBSDF((SurfaceInteraction si, Allocator alloc) => createDielectricMicrofacet(si, alloc, 1.5f, 0.3f, 0.15f),
                     "TRDielAniso");
        }

        [TEST("BSDFSampling", "TRDielIsoInv")]
        public static void BSDFSampling_TRDielIsoInv()
        {
            TestBSDF((SurfaceInteraction si, Allocator alloc) => createDielectricMicrofacet(si, alloc, 1 / 1.5f, 0.5f, 0.5f),
                     "TRDielIsoInv");
        }

        [TEST("BSDFSampling", "TRDielAnisoInv")]
        public static void BSDFSampling_TRDielAnisoInv()
        {
            TestBSDF((SurfaceInteraction si, Allocator alloc) => createDielectricMicrofacet(si, alloc, 1 / 1.5f, 0.3f, 0.15f),
                     "TRDielAnisoInv");
        }

        ///////////////////////////////////////////////////////////////////////////
        // Energy Conservation Tests

        private static void TestEnergyConservation(Func<SurfaceInteraction, Allocator, BSDF> createBSDF,
                                                   string description)
        {
            RNG rng = new RNG();

            SurfaceInteraction hit;
            BSDF bsdf = DiskBSDF(createBSDF, out hit);

            for (int i = 0; i < 10; ++i)
            {
                Float ux = rng.Uniform<Float>();
                Float uy = rng.Uniform<Float>();
                Point2f uo = new Point2f(ux, uy);
                Vector3f woL = SampleUniformHemisphere(uo);
                Vector3f wo = bsdf.LocalToRender(woL);

                const int nSamples = 16384;
                SampledSpectrum Lo = new SampledSpectrum(0.0f);
                for (int j = 0; j < nSamples; ++j)
                {
                    Float u = rng.Uniform<Float>();
                    Float ix = rng.Uniform<Float>();
                    Float iy = rng.Uniform<Float>();
                    Point2f ui = new Point2f(ix, iy);
                    pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, u, ui);
                    if (bs.has_value())
                        Lo += bs.value().f * AbsDot(bs.value().wi, hit.n) / bs.value().pdf;
                }
                Lo /= nSamples;

                EXPECT_LT(Lo.MaxComponentValue(), 1.01,
                          description + ": Lo = " + Lo + ", wo = " + wo);
            }
        }

        [TEST("BSDFEnergyConservation", "LambertianReflection")]
        public static void BSDFEnergyConservation_LambertianReflection()
        {
            TestEnergyConservation(
                (SurfaceInteraction si, Allocator alloc) =>
                    new BSDF(si.shading.n, si.shading.dpdu, new DiffuseBxDF(new SampledSpectrum(1.0f))),
                "LambertianReflection");
        }

        // Hair Tests
        // (Hair.Reciprocity is under "#if 0" in the C++ test.)

        [TEST("Hair", "WhiteFurnace")]
        public static void Hair_WhiteFurnace()
        {
            RNG rng = new RNG();
            Float wx = rng.Uniform<Float>();
            Float wy = rng.Uniform<Float>();
            Vector3f wo = SampleUniformSphere(new Point2f(wx, wy));
            for (Float beta_m = .1f; beta_m < 1; beta_m += .2f)
            {
                for (Float beta_n = .1f; beta_n < 1; beta_n += .2f)
                {
                    // Estimate reflected uniform incident radiance from hair
                    Float ySum = 0;

                    // More samples for the smooth case, since we're sampling blindly.
                    int count = (beta_m < .5 || beta_n < .5) ? 100000 : 20000;

                    if (NSpectrumSamples < 4) count *= 4;

                    for (int i = 0; i < count; ++i)
                    {
                        SampledWavelengths lambda = SampledWavelengths.SampleVisible(RadicalInverse(0, (uint64_t)i));

                        Float h = (Float)Clamp(-1 + 2.0 * RadicalInverse(1, (uint64_t)i), -.999999, .999999);
                        SampledSpectrum sigma_a = new SampledSpectrum(0.0f);
                        HairBxDF hair = new HairBxDF(h, 1.55f, sigma_a, beta_m, beta_n, 0.0f);
                        Vector3f wi = SampleUniformSphere(new Point2f(RadicalInverse(2, (uint64_t)i),
                                                                      RadicalInverse(3, (uint64_t)i)));

                        SampledSpectrum f = hair.f(wo, wi, TransportMode.Radiance) * AbsCosTheta(wi);
                        ySum += f.y(lambda);
                    }

                    Float avg = ySum / (count * UniformSpherePDF());
                    EXPECT_TRUE(avg >= .95 && avg <= 1.05, avg.ToString());
                }
            }
        }

        [TEST("Hair", "HOnTheEdge")]
        public static void Hair_HOnTheEdge()
        {
            Vector3f wo = new Vector3f(0.54986966f, 0.03359017f, 0.83457476f),
                     wi = new Vector3f(-0.37383357f, -0.91920084f, 0.12376696f);
            Float h = -1, beta_m = .1f, beta_n = .1f;
            SampledSpectrum sigma_a = new SampledSpectrum(0.0f);
            HairBxDF hair = new HairBxDF(h, 1.55f, sigma_a, beta_m, beta_n, 0.0f);

            SampledSpectrum f = hair.f(wo, wi, TransportMode.Radiance);
        }

        [TEST("Hair", "WhiteFurnaceSampled")]
        public static void Hair_WhiteFurnaceSampled()
        {
            RNG rng = new RNG();
            Float wx = rng.Uniform<Float>();
            Float wy = rng.Uniform<Float>();
            Vector3f wo = SampleUniformSphere(new Point2f(wx, wy));
            for (Float beta_m = .1f; beta_m < 1; beta_m += .2f)
            {
                for (Float beta_n = .1f; beta_n < 1; beta_n += .2f)
                {
                    Float ySum = 0;

                    int count = 10000;
                    for (int i = 0; i < count; ++i)
                    {
                        SampledWavelengths lambda = SampledWavelengths.SampleVisible(RadicalInverse(0, (uint64_t)i));
                        Float h = (Float)Clamp(-1 + 2.0 * RadicalInverse(1, (uint64_t)i), -.999999, .999999);

                        SampledSpectrum sigma_a = new SampledSpectrum(0.0f);
                        HairBxDF hair = new HairBxDF(h, 1.55f, sigma_a, beta_m, beta_n, 0.0f);

                        Float uc = RadicalInverse(2, (uint64_t)i);
                        Point2f u = new Point2f(RadicalInverse(3, (uint64_t)i), RadicalInverse(4, (uint64_t)i));

                        pstd.optional<BSDFSample> bs = hair.Sample_f(wo, uc, u, TransportMode.Radiance,
                                                                     BxDFReflTransFlags.All);
                        if (bs.has_value())
                        {
                            SampledSpectrum f = bs.value().f * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                            ySum += f.y(lambda);
                        }
                    }

                    Float avg = ySum / count;
                    EXPECT_TRUE(avg >= .99 && avg <= 1.01, avg.ToString());
                }
            }
        }

        [TEST("Hair", "SamplingWeights")]
        public static void Hair_SamplingWeights()
        {
            RNG rng = new RNG();
            for (Float beta_m = .1f; beta_m < 1; beta_m += .2f)
                for (Float beta_n = .4f; beta_n < 1; beta_n += .2f)
                {
                    int count = 10000;
                    for (int i = 0; i < count; ++i)
                    {
                        Float h = (Float)Clamp(-1 + 2.0 * RadicalInverse(0, (uint64_t)i), -.999999, .999999);

                        // Check _HairBxDF::Sample\_f()_ sample weight
                        SampledSpectrum sigma_a = new SampledSpectrum(0.0f);
                        HairBxDF hair = new HairBxDF(h, 1.55f, sigma_a, beta_m, beta_n, 0.0f);

                        Vector3f wo = SampleUniformSphere(new Point2f(RadicalInverse(1, (uint64_t)i),
                                                                      RadicalInverse(2, (uint64_t)i)));
                        Float uc = RadicalInverse(3, (uint64_t)i);
                        Point2f u = new Point2f(RadicalInverse(4, (uint64_t)i), RadicalInverse(5, (uint64_t)i));
                        pstd.optional<BSDFSample> bs = hair.Sample_f(wo, uc, u, TransportMode.Radiance,
                                                                     BxDFReflTransFlags.All);
                        if (bs.has_value())
                        {
                            Float sum = 0;
                            int ny = 20;
                            foreach (Float uu in new Stratified1D(ny))
                            {
                                SampledWavelengths lambda = SampledWavelengths.SampleVisible(uu);
                                sum += bs.value().f.y(lambda) * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                            }

                            // Verify that hair BSDF sample weight is close to 1 for
                            // _wi_
                            Float avg = sum / ny;
                            EXPECT_GT(avg, 0.99);
                            EXPECT_LT(avg, 1.01);
                        }
                    }
                }
        }

        [TEST("Hair", "SamplingConsistency")]
        public static void Hair_SamplingConsistency()
        {
            RNG rng = new RNG();
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            for (Float beta_m = .2f; beta_m < 1; beta_m += .2f)
                for (Float beta_n = .4f; beta_n < 1; beta_n += .2f)
                {
                    // Declare variables for hair sampling test
                    const int count = 64 * 1024;
                    SampledSpectrum sigma_a = new SampledSpectrum(.25f);
                    Float wx = rng.Uniform<Float>();
                    Float wy = rng.Uniform<Float>();
                    Vector3f wo = SampleUniformSphere(new Point2f(wx, wy));
                    Func<Vector3f, SampledSpectrum> Li = (Vector3f w) => new SampledSpectrum(w.z * w.z);
                    SampledSpectrum fImportance = new SampledSpectrum(0.0f), fUniform = new SampledSpectrum(0.0f);
                    for (int i = 0; i < count; ++i)
                    {
                        // Compute estimates of scattered radiance for hair sampling
                        // test
                        Float h = -1 + 2 * rng.Uniform<Float>();
                        HairBxDF hair = new HairBxDF(h, 1.55f, sigma_a, beta_m, beta_n, 0.0f);
                        Vector3f wi;
                        Float uc = rng.Uniform<Float>();
                        Float ux = rng.Uniform<Float>();
                        Float uy = rng.Uniform<Float>();
                        Point2f u = new Point2f(ux, uy);
                        pstd.optional<BSDFSample> bs = hair.Sample_f(wo, uc, u, TransportMode.Radiance,
                                                                     BxDFReflTransFlags.All);
                        if (bs.has_value())
                            fImportance += bs.value().f * Li(bs.value().wi) * AbsCosTheta(bs.value().wi) /
                                           (count * bs.value().pdf);
                        wi = SampleUniformSphere(u);
                        fUniform += hair.f(wo, wi, TransportMode.Radiance) * Li(wi) * AbsCosTheta(wi) /
                                    (count * UniformSpherePDF());
                    }
                    // Verify consistency of estimated hair reflected radiance values
                    Float err = std.abs(fImportance.y(lambda) - fUniform.y(lambda)) / fUniform.y(lambda);
                    EXPECT_LT(err, 0.05);
                }
        }
    }
}
