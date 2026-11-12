// (not in pbrt-v4) scattering_microfacet_test.cs
//
// The MicrofacetDistribution interface (util/scattering.cs), checked by quadrature over the normals of the upper hemisphere,
// for directions above and below the horizon:
//   - Sigma(w) is the integral of D(wm) max(0, w . wm), which makes PDF_SingleSided a density (Tokuyoshi 2021);
//   - the normals that Sample_wm_SingleSided returns have that density (a histogram against the integral of the density over
//     its cells);
//   - above the horizon the single sided density is the two sided one of pbrt-v4, G1(w) / cos(theta) D(wm) |w . wm|;
//   - below the horizon the two sided normalisation, were it used without mirroring the direction, integrates to less than 1:
//     the bias the single sided normalisation removes. It is printed.

using System;
using testing;

namespace pbrt
{
    public static unsafe class MicrofacetDistributionTests
    {
        const int ThetaSteps = 4000;
        const int PhiSteps = 720;
        const int ThetaCells = 8;
        const int PhiCells = 8;

        static Vector3f Direction(Float cosTheta, Float phi)
        {
            Float sinTheta = SafeSqrt(1 - cosTheta * cosTheta);
            return new Vector3f(sinTheta * (Float)Math.Cos(phi), sinTheta * (Float)Math.Sin(phi), cosTheta);
        }

        static void Check(MicrofacetDistribution distribution, string name)
        {
            RNG rng = new RNG(11);
            foreach (Float cosTheta in new Float[] { 1.0f, 0.8f, 0.3f, 0.05f, -0.2f, -0.6f })
            {
                Vector3f w = Direction(cosTheta, 0.7f);

                // Quadrature of D(wm) max(0, w . wm) over the upper hemisphere, by cells of the histogram
                double[] cells = new double[ThetaCells * PhiCells];
                double integral = 0;
                // The polar angle is (pi / 2) (1 - s^2) with s in steps: fine at the horizon, where a distribution may grow
                // without bound (the Student's t one, below gamma = 2)
                double dPhi = 2 * Math.PI / PhiSteps;
                for (int i = 0; i < ThetaSteps; ++i)
                {
                    double step = (i + 0.5) / ThetaSteps;
                    double theta = 0.5 * Math.PI * (1 - step * step);
                    double dTheta = Math.PI * step / ThetaSteps;
                    double sinTheta = Math.Sin(theta), cosThetaM = Math.Cos(theta);
                    for (int j = 0; j < PhiSteps; ++j)
                    {
                        double phi = (j + 0.5) * dPhi;
                        Vector3f wm = new Vector3f((Float)(sinTheta * Math.Cos(phi)), (Float)(sinTheta * Math.Sin(phi)), (Float)cosThetaM);
                        double value = distribution.D(wm) * Math.Max(0.0, (double)Dot(w, wm)) * sinTheta * dTheta * dPhi;
                        integral += value;
                        cells[Math.Min(ThetaCells - 1, (int)(theta / (0.5 * Math.PI) * ThetaCells)) * PhiCells + j * PhiCells / PhiSteps] += value;
                    }
                }

                // The cross section
                double sigma = distribution.Sigma(w);
                double sigmaError = Math.Abs(sigma - integral) / integral;
                EXPECT_LT(sigmaError, 2e-3, name + ": Sigma is not the integral of D(wm) max(0, w . wm)");

                // Above the horizon: the density of pbrt-v4. Below: what its normalisation would integrate to
                double twoSided = 0;
                double densityError = 0;
                if (cosTheta > 0)
                {
                    for (int i = 0; i < 200; ++i)
                    {
                        Vector3f wm = distribution.Sample_wm_SingleSided(w, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        double a = distribution.PDF_SingleSided(w, wm), b = distribution.PDF(w, wm);
                        if (Math.Max(a, b) > 1e-3)
                        {
                            densityError = Math.Max(densityError, Math.Abs(a - b) / Math.Max(a, b));
                        }
                    }
                    EXPECT_LT(densityError, 2e-3, name + ": above the horizon the single sided density is not the two sided one");
                    twoSided = 1;
                }
                else
                {
                    twoSided = integral * distribution.G1(w) / Math.Abs(cosTheta);
                }

                // The sampled normals against the density
                int samples = 400000;
                int[] counts = new int[ThetaCells * PhiCells];
                int invalid = 0;
                for (int i = 0; i < samples; ++i)
                {
                    Vector3f wm = distribution.Sample_wm_SingleSided(w, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (!(wm.z > 0) || !(Dot(w, wm) >= -1e-4f) || IsNaN(wm.x + wm.y + wm.z))
                    {
                        ++invalid;
                        continue;
                    }
                    double theta = Math.Acos(Math.Min(1.0, (double)wm.z));
                    double phi = Math.Atan2(wm.y, wm.x);
                    if (phi < 0)
                    {
                        phi += 2 * Math.PI;
                    }
                    int a = Math.Min(ThetaCells - 1, (int)(theta / (0.5 * Math.PI) * ThetaCells));
                    int b = Math.Min(PhiCells - 1, (int)(phi / (2 * Math.PI) * PhiCells));
                    ++counts[a * PhiCells + b];
                }
                EXPECT_EQ(0, invalid, name + ": normals below the horizon, or facing away from the direction");
                double worst = 0;
                for (int c = 0; c < cells.Length; ++c)
                {
                    double expected = samples * cells[c] / integral;
                    if (expected < 20)
                    {
                        continue;
                    }
                    // The quadrature of a cell is good to about 0.1%
                    double deviation = Math.Abs(counts[c] - expected) / Math.Sqrt(expected + Sqr(1e-3 * expected));
                    worst = Math.Max(worst, deviation);
                }
                EXPECT_LT(worst, 5.0, name + ": the sampled normals do not have the density");

                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet %s, cos %.2f: Sigma %.6f (quadrature %.6f, relative %.1e), " +
                                                          "samples against density %.1f sigma worst cell, two sided normalisation integrates to %.4f",
                                                          name, cosTheta, sigma, integral, sigmaError, worst, twoSided));
            }
        }

        [TEST("MicrofacetDistribution", "TrowbridgeReitz")]
        public static void MicrofacetDistribution_TrowbridgeReitz()
        {
            Check(new TrowbridgeReitzDistribution(0.3f, 0.3f), "GGX 0.3");
            Check(new TrowbridgeReitzDistribution(0.5f, 0.15f), "GGX 0.5 0.15");
        }

        [TEST("MicrofacetDistribution", "Beckmann")]
        public static void MicrofacetDistribution_Beckmann()
        {
            Check(new BeckmannDistribution(0.3f, 0.3f), "Beckmann 0.3");
            Check(new BeckmannDistribution(0.5f, 0.15f), "Beckmann 0.5 0.15");
        }

        [TEST("MicrofacetDistribution", "StudentT")]
        public static void MicrofacetDistribution_StudentT()
        {
            Check(new StudentTDistribution(0.3f, 0.3f, 1.75f), "StudentT 0.3 gamma 1.75");
            Check(new StudentTDistribution(0.3f, 0.3f, 4f), "StudentT 0.3 gamma 4");
            Check(new StudentTDistribution(0.5f, 0.15f, 2.5f), "StudentT 0.5 0.15 gamma 2.5");
            Check(new StudentTDistribution(0.5f, 0.15f, 20f), "StudentT 0.5 0.15 gamma 20");
        }

        // The Student's t distribution is the Trowbridge-Reitz one at gamma = 2, and tends to the Beckmann one as gamma grows
        [TEST("MicrofacetDistribution", "StudentTLimits")]
        public static void MicrofacetDistribution_StudentTLimits()
        {
            RNG rng = new RNG(3);
            MicrofacetDistribution ggx = new TrowbridgeReitzDistribution(0.5f, 0.15f), student2 = new StudentTDistribution(0.5f, 0.15f, 2f);
            MicrofacetDistribution beckmann = new BeckmannDistribution(0.5f, 0.15f), studentLarge = new StudentTDistribution(0.5f, 0.15f, 2000f);
            double worstGgx = 0, worstBeckmann = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f w = Direction(1 - 1.9f * rng.Uniform<Float>(), 6 * rng.Uniform<Float>());
                Vector3f wm = Direction(0.05f + 0.95f * rng.Uniform<Float>(), 6 * rng.Uniform<Float>());
                double[] a = new double[] { student2.D(wm), student2.Sigma(w), student2.Lambda(w) };
                double[] b = new double[] { ggx.D(wm), ggx.Sigma(w), ggx.Lambda(w) };
                double[] c = new double[] { studentLarge.D(wm), studentLarge.Sigma(w), studentLarge.Lambda(w) };
                double[] d = new double[] { beckmann.D(wm), beckmann.Sigma(w), beckmann.Lambda(w) };
                for (int k = 0; k < 3; ++k)
                {
                    if (Math.Max(a[k], b[k]) > 1e-4)
                    {
                        worstGgx = Math.Max(worstGgx, Math.Abs(a[k] - b[k]) / Math.Max(a[k], b[k]));
                    }
                    if (Math.Max(c[k], d[k]) > 1e-2)
                    {
                        worstBeckmann = Math.Max(worstBeckmann, Math.Abs(c[k] - d[k]) / Math.Max(c[k], d[k]));
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet StudentT: gamma 2 against GGX, worst relative difference %.1e; " +
                                                      "gamma 2000 against Beckmann %.1e", worstGgx, worstBeckmann));
            EXPECT_LT(worstGgx, 1e-3);
            EXPECT_LT(worstBeckmann, 2e-2);
        }

        // The cost of a sample of each distribution
        [TEST("MicrofacetDistribution", "SamplingCost")]
        public static void MicrofacetDistribution_SamplingCost()
        {
            MicrofacetDistribution[] distributions = new MicrofacetDistribution[]
            {
                new TrowbridgeReitzDistribution(0.3f, 0.3f), new BeckmannDistribution(0.3f, 0.3f), new StudentTDistribution(0.3f, 0.3f, 3f)
            };
            foreach (MicrofacetDistribution distribution in distributions)
            {
                RNG rng = new RNG(1);
                Vector3f w = Direction(0.5f, 0.7f);
                Float sum = 0;
                int n = 300000;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < n; ++i)
                {
                    sum += distribution.Sample_wm_SingleSided(w, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())).z;
                }
                double sample = watch.Elapsed.TotalMilliseconds * 1e6 / n;
                watch.Restart();
                for (int i = 0; i < n; ++i)
                {
                    sum += distribution.Sigma(Direction(0.1f + 0.8f * rng.Uniform<Float>(), 0.7f));
                }
                double sigma = watch.Elapsed.TotalMilliseconds * 1e6 / n;
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet cost %s: sample %.0f ns, Sigma %.0f ns (%f)", distribution.ToString(), sample, sigma, sum));
            }
        }

        // The two sided functions for a direction below the horizon are those of its mirror image: the convention of pbrt-v4
        [TEST("MicrofacetDistribution", "TwoSided")]
        public static void MicrofacetDistribution_TwoSided()
        {
            RNG rng = new RNG(5);
            MicrofacetDistribution[] distributions = new MicrofacetDistribution[]
            {
                new TrowbridgeReitzDistribution(0.5f, 0.15f), new BeckmannDistribution(0.5f, 0.15f), new StudentTDistribution(0.5f, 0.15f, 3f)
            };
            foreach (MicrofacetDistribution distribution in distributions)
            {
                for (int i = 0; i < 1000; ++i)
                {
                    Vector3f w = Direction(-0.05f - 0.9f * rng.Uniform<Float>(), 6 * rng.Uniform<Float>());
                    Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    Vector3f wm = distribution.Sample_wm(w, u);
                    Vector3f mirrored = distribution.Sample_wm(-w, u);
                    EXPECT_LT(Length(wm - mirrored), 1e-5f);
                    EXPECT_GT(wm.z, 0f);
                    Float a = distribution.PDF(w, wm), b = distribution.PDF_SingleSided(-w, wm);
                    EXPECT_LT(Math.Abs(a - b), 2e-3f * Math.Max(a, b) + 1e-5f);
                }
            }
        }

        // The V-cavity functions of the interface: the density of the visible normals, G(w, wm) D(wm) (w . wm) / (H(w) |w . n|),
        // integrates to 1 over the normals above and below the horizon (which is what makes H the hit probability), and the
        // sampled normals have it. The H of the Trowbridge-Reitz and Beckmann distributions is that of the closed forms of
        // Tokuyoshi 2021.
        static void CheckVCavity(MicrofacetDistribution distribution, string name)
        {
            RNG rng = new RNG(21);
            foreach (Float cosTheta in new Float[] { 0.8f, 0.3f, -0.2f, -0.6f })
            {
                Vector3f w = Direction(cosTheta, 0.7f);
                double[] cells = new double[ThetaCells * PhiCells];
                double integral = 0;
                double dPhi = 2 * Math.PI / PhiSteps;
                for (int i = 0; i < ThetaSteps; ++i)
                {
                    double step = (i + 0.5) / ThetaSteps;
                    double theta = 0.5 * Math.PI * (1 - step * step);
                    double dTheta = Math.PI * step / ThetaSteps;
                    double sinTheta = Math.Sin(theta), cosThetaM = Math.Cos(theta);
                    for (int j = 0; j < PhiSteps; ++j)
                    {
                        double phi = (j + 0.5) * dPhi;
                        Vector3f wm = new Vector3f((Float)(sinTheta * Math.Cos(phi)), (Float)(sinTheta * Math.Sin(phi)), (Float)cosThetaM);
                        double value = distribution.PDF_VCavity(w, wm) * sinTheta * dTheta * dPhi;
                        integral += value;
                        cells[Math.Min(ThetaCells - 1, (int)(theta / (0.5 * Math.PI) * ThetaCells)) * PhiCells + j * PhiCells / PhiSteps] += value;
                    }
                }
                EXPECT_LT(Math.Abs(integral - 1), 3e-3, name + ": the V-cavity density does not integrate to 1");

                int samples = 400000;
                int[] counts = new int[ThetaCells * PhiCells];
                int invalid = 0;
                for (int i = 0; i < samples; ++i)
                {
                    Vector3f wm = distribution.Sample_wm_VCavity(w, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (!(wm.z > 0) || !(Dot(w, wm) >= -1e-4f) || IsNaN(wm.x + wm.y + wm.z))
                    {
                        ++invalid;
                        continue;
                    }
                    double theta = Math.Acos(Math.Min(1.0, (double)wm.z));
                    double phi = Math.Atan2(wm.y, wm.x);
                    if (phi < 0)
                    {
                        phi += 2 * Math.PI;
                    }
                    int a = Math.Min(ThetaCells - 1, (int)(theta / (0.5 * Math.PI) * ThetaCells));
                    int b = Math.Min(PhiCells - 1, (int)(phi / (2 * Math.PI) * PhiCells));
                    ++counts[a * PhiCells + b];
                }
                EXPECT_EQ(0, invalid, name + ": normals below the horizon, or facing away from the direction");
                double worst = 0;
                for (int c = 0; c < cells.Length; ++c)
                {
                    double expected = samples * cells[c] / integral;
                    if (expected < 20)
                    {
                        continue;
                    }
                    // The quadrature of a cell is good to about 0.3% (the density has a step and a kink)
                    double deviation = Math.Abs(counts[c] - expected) / Math.Sqrt(expected + Sqr(3e-3 * expected));
                    worst = Math.Max(worst, deviation);
                }
                EXPECT_LT(worst, 5.0, name + ": the sampled normals do not have the V-cavity density");
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet V-cavity %s, cos %.2f: H %.6f, density integrates to %.5f, samples against density %.1f sigma worst cell",
                                                          name, cosTheta, distribution.HitProbability_VCavity(w), integral, worst));
            }
        }

        [TEST("MicrofacetDistribution", "VCavity")]
        public static void MicrofacetDistribution_VCavity()
        {
            CheckVCavity(new TrowbridgeReitzDistribution(0.3f, 0.3f), "GGX 0.3");
            CheckVCavity(new TrowbridgeReitzDistribution(0.5f, 0.15f), "GGX 0.5 0.15");
            CheckVCavity(new BeckmannDistribution(0.3f, 0.3f), "Beckmann 0.3");
            CheckVCavity(new BeckmannDistribution(0.5f, 0.15f), "Beckmann 0.5 0.15");
            CheckVCavity(new StudentTDistribution(0.3f, 0.3f, 4f), "StudentT 0.3 gamma 4");
            CheckVCavity(new StudentTDistribution(0.5f, 0.15f, 2.5f), "StudentT 0.5 0.15 gamma 2.5");
        }

        // The hit probability against the closed forms of Tokuyoshi 2021 (supplementary document, section 2.2)
        [TEST("MicrofacetDistribution", "VCavityHitProbability")]
        public static void MicrofacetDistribution_VCavityHitProbability()
        {
            RNG rng = new RNG(6);
            Float ax = 0.5f, ay = 0.15f;
            MicrofacetDistribution ggx = new TrowbridgeReitzDistribution(ax, ay), beckmann = new BeckmannDistribution(ax, ay);
            double worstGgx = 0, worstBeckmann = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f w = Direction(-0.02f - 0.95f * rng.Uniform<Float>(), 6 * rng.Uniform<Float>());
                double b = Sqr((double)ax * w.x) + Sqr((double)ay * w.y), z = w.z;
                double hGgx = 1 + (Math.Sqrt(b + 9 * z * z) - Math.Sqrt(b + z * z)) / (2 * z);
                double v = z / Math.Sqrt(b);
                double hBeckmann = 1 + (Math.Exp(-9 * v * v) - Math.Exp(-v * v)) / (2 * Math.Sqrt(Math.PI) * v) + (3 * pbrt.Erf(3 * v) - pbrt.Erf(v)) / 2;
                double a = ggx.HitProbability_VCavity(w), c = beckmann.HitProbability_VCavity(w);
                if (hGgx > 1e-4)
                {
                    worstGgx = Math.Max(worstGgx, Math.Abs(a - hGgx) / hGgx);
                }
                if (hBeckmann > 1e-4)
                {
                    worstBeckmann = Math.Max(worstBeckmann, Math.Abs(c - hBeckmann) / hBeckmann);
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE microfacet V-cavity hit probability against the closed forms: GGX %.1e, Beckmann %.1e (worst relative)",
                                                      worstGgx, worstBeckmann));
            EXPECT_LT(worstGgx, 1e-4);
            EXPECT_LT(worstBeckmann, 1e-4);
        }
    }
}
