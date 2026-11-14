// (not in pbrt-v4) polygrain_test.cs
//
// The polydisperse micrograin model (util/micrograin.cs, polygrain.cs):
//   - the shadow areas of a grain against a count of the points of the plane that the grain shadows;
//   - the distribution of normals is normalised, its sampling has its density, and the grains seen from a direction cover what
//     the pores do not: tau0 int D(m) <w, m> G~(w, m) dm = cos(theta) (1 - Vp(w)), the identity the walk rests on;
//   - the BxDF of the paper is reciprocal, loses energy only, and samples what it evaluates.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class PolyGrainTests
    {
        public static mg.PolyGrain Two()
        {
            mg.PolyGrain d = new mg.PolyGrain();
            d.n = 2;
            d.SetType(0, 0.35, 1.0, 1.0, 1.0, 0);
            d.SetType(1, 0.25, 0.5, 1.6, 0.8, 0.6);
            d.Finalize_();
            return d;
        }

        public static mg.PolyGrain One(double tau, double beta)
        {
            mg.PolyGrain d = new mg.PolyGrain();
            d.n = 1;
            d.SetType(0, tau, 1.0, beta, beta, 0);
            d.Finalize_();
            return d;
        }

        static mg.Vec3d Dir(double cosine, double phi)
        {
            double s = Math.Sqrt(1 - cosine * cosine);
            return new mg.Vec3d(s * Math.Cos(phi), s * Math.Sin(phi), cosine);
        }

        // A point x of the plane at height h is in the shadow of the unit sphere along d if the ray from it along d meets the
        // sphere, and it is outside the sphere
        static bool Shadowed(double x, double y, double h, mg.Vec3d d)
        {
            if (x * x + y * y + h * h <= 1)
            {
                return false;
            }
            double b = x * d.x + y * d.y + h * d.z, c = x * x + y * y + h * h - 1;
            double disc = b * b - c;
            return disc >= 0 && -b - Math.Sqrt(disc) >= 0 && (h + (-b - Math.Sqrt(disc)) * d.z) >= 0;
        }

        [TEST("PolyGrain", "ShadowAreas")]
        public static void ShadowAreas()
        {
            RNG rng = new RNG(3);
            double worstOne = 0, worstBoth = 0;
            for (int test = 0; test < 12; ++test)
            {
                mg.Vec3d i = Dir(0.15 + 0.8 * rng.Uniform<double>(), 6.28 * rng.Uniform<double>()), o = Dir(0.15 + 0.8 * rng.Uniform<double>(), 6.28 * rng.Uniform<double>());
                double h = 0.9 * rng.Uniform<double>();
                // (the shadow of a direction of cosine c reaches 1 / c from the centre)
                double reach = 1.05 / Math.Min(i.z, o.z);
                int n = 1400, one = 0, both = 0;
                for (int a = 0; a < n; ++a)
                {
                    for (int b = 0; b < n; ++b)
                    {
                        double x = reach * (2 * (a + 0.5) / n - 1), y = reach * (2 * (b + 0.5) / n - 1);
                        bool si = Shadowed(x, y, h, i), so = Shadowed(x, y, h, o);
                        one += si ? 1 : 0;
                        both += (si && so) ? 1 : 0;
                    }
                }
                double cell = 4 * reach * reach / ((double)n * n);
                worstOne = Math.Max(worstOne, Math.Abs(one * cell - mg.ShadowArea(i, h)));
                worstBoth = Math.Max(worstBoth, Math.Abs(both * cell - mg.ShadowOverlapArea(i, o, h)));
            }
            Console.WriteLine("MEASURE polygrain shadow areas against a count on a grid: one direction {0:E2}, the overlap of two {1:E2}", worstOne, worstBoth);
            EXPECT_LT(worstOne, 2e-2);
            EXPECT_LT(worstBoth, 2e-2);
        }

        [TEST("PolyGrain", "DistributionAndVisibleGrains")]
        public static void DistributionAndVisibleGrains()
        {
            foreach (mg.PolyGrain d in new mg.PolyGrain[] { One(0.5, 1.0), One(0.8, 0.5), Two() })
            {
                // int D cos = 1, and the probabilities of the types
                int n = 1200;
                double norm = 0;
                double[] visible = new double[3];
                mg.Vec3d[] w = new mg.Vec3d[] { Dir(0.95, 0.3), Dir(0.5, 1.2), Dir(0.15, 4.0) };
                for (int a = 0; a < n; ++a)
                {
                    for (int b = 0; b < n; ++b)
                    {
                        double px = Math.Tan(Math.PI * ((a + 0.5) / n - 0.5)), py = Math.Tan(Math.PI * ((b + 0.5) / n - 0.5));
                        double jac = (1 + px * px) * (1 + py * py) * (Math.PI / n) * (Math.PI / n);
                        double len = Math.Sqrt(1 + px * px + py * py);
                        mg.Vec3d m = new mg.Vec3d(-px / len, -py / len, 1 / len);
                        double dw = m.z * m.z * m.z * jac;
                        norm += d.NDF(m) * m.z * dw;
                        for (int q = 0; q < 3; ++q)
                        {
                            double cosine = w[q].x * m.x + w[q].y * m.y + w[q].z * m.z;
                            if (cosine <= 0)
                            {
                                continue;
                            }
                            for (int i = 0; i < d.n; ++i)
                            {
                                double h = d.Height(i, m), g = 1;
                                for (int j = 0; j < d.n; ++j)
                                {
                                    g *= d.GdistUni(j, w[q], h / d.r[j]);
                                }
                                visible[q] += d.PTypeNormal(i, m) / m.z * cosine * g * dw;
                            }
                        }
                    }
                }
                EXPECT_LT(Math.Abs(norm - 1), 5e-3);
                for (int q = 0; q < 3; ++q)
                {
                    double pores = (1 - d.tauBar0) * d.BulkVisUni(w[q]);
                    double grains = d.tauBar0 * visible[q] / w[q].z;
                    Console.WriteLine("MEASURE polygrain, {0} types, tau0 {1:F2}, cosine {2}: grains seen {3:F4} + pores seen {4:F4} = {5:F4}", d.n, d.tauBar0, w[q].z,
                                      grains, pores, grains + pores);
                    EXPECT_LT(Math.Abs(grains + pores - 1), 2e-2);
                }
                // the sampling of the normals against its density: the mean height
                RNG rng = new RNG(7);
                double mean = 0, expected = 0;
                int count = 200000;
                for (int s = 0; s < count; ++s)
                {
                    d.SampleSliceType(rng.Uniform<double>(), rng.Uniform<double>(), out int k, out int type, out double pj);
                    if (d.SampleNormal(k, type, rng.Uniform<double>(), rng.Uniform<double>(), out mg.Vec3d m, out double pdf))
                    {
                        mean += m.z / count;
                    }
                }
                for (int a = 0; a < n; ++a)
                {
                    for (int b = 0; b < n; ++b)
                    {
                        double px = Math.Tan(Math.PI * ((a + 0.5) / n - 0.5)), py = Math.Tan(Math.PI * ((b + 0.5) / n - 0.5));
                        double jac = (1 + px * px) * (1 + py * py) * (Math.PI / n) * (Math.PI / n);
                        double len = Math.Sqrt(1 + px * px + py * py);
                        mg.Vec3d m = new mg.Vec3d(-px / len, -py / len, 1 / len);
                        expected += d.NDF(m) * m.z * m.z * m.z * m.z * m.z * jac;
                    }
                }
                EXPECT_LT(Math.Abs(mean - expected), 4e-3);
            }
        }

        public static PolyGrainBxDF Gold(mg.PolyGrain d, Float bulkReflectance, int samples)
        {
            PolyGrainBxDF bxdf = new PolyGrainBxDF(d, samples);
            for (int j = 0; j < d.n; ++j)
            {
                bxdf.SetGrainDiffuse(j, new SampledSpectrum(0f));
                bxdf.SetGrainConductor(j, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f));
            }
            bxdf.SetBulk(new DiffuseBxDF(new SampledSpectrum(bulkReflectance)));
            return bxdf;
        }

        [TEST("PolyGrain", "ChecksOfThePaper")]
        public static void ChecksOfThePaper()
        {
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.cosines = new Float[] { 0.9f, 0.5f, 0.2f };
            options.samples = 40000;
            options.pairs = 600;
            options.bothSides = false;
            BxDFCheckResult metal = BxDFCheck.Run(Gold(Two(), 0.5f, 4), "PolyGrain, two types of conductor grains on a diffuse bulk", options);
            EXPECT_EQ(0, metal.invalid);
            EXPECT_LT(metal.reciprocity, 2e-3);
            EXPECT_LT(metal.albedoMax, 1.0);
            EXPECT_TRUE(metal.albedoSigmas < 5 || metal.albedoRelative < 5e-3);
            EXPECT_LT(metal.pdfMax, 1.01);
            EXPECT_LT(metal.sampleF, 2e-3);
            EXPECT_LT(metal.samplePdf, 2e-3);
        }
    }
}
