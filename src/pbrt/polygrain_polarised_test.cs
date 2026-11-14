// (not in pbrt-v4) polygrain_polarised_test.cs
//
// The Mueller matrices of the polydisperse micrograin BSDF (polygrain_polarised.cs):
//   - grains of one metal on a black bulk, one scattering: the matrix of a mirror of that metal at the half vector, which is
//     that of the microfacet conductor of pbrt for the same pair of directions; the scalar value is that of the scalar BSDF;
//   - white diffuse grains on a diffuse bulk depolarise;
//   - the light that leaves under an even unpolarised surround is the same by sampled paths and by evaluated directions, with
//     one scattering and with many, walked from one direction and from both, on a diffuse and on a metallic bulk.

using System;
using testing;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public static unsafe class PolyGrainPolarisedTests
    {
        static SampledSpectrum S(Float v) { return new SampledSpectrum(v); }

        [TEST("PolyGrainPolarised", "OneScatteringIsAMirrorOfTheMetal")]
        public static void OneScatteringIsAMirrorOfTheMetal()
        {
            RNG rng = new RNG(3);
            PolyGrainBxDF grains = PolyGrainTests.Gold(PolyGrainTests.One(0.6, 1.0), 0f, 4);
            BxDF lobe = new ConductorBxDF(new TrowbridgeReitzDistribution(0.5f, 0.5f), S(0.2f), S(3.5f));
            BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), grains);
            double worst = 0, worstScalar = 0;
            int compared = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f wo = FacetForgePolarisedTests.Upper(ref rng), wi = FacetForgePolarisedTests.Upper(ref rng);
                foreach (TransportMode mode in new TransportMode[] { TransportMode.Radiance, TransportMode.Importance })
                {
                    MuellerMatrix reference, a;
                    if (grains.f(wo, wi, mode)[0] > 1e-4f && FacetForgePolarisedTests.Matrix(lobe, wo, wi, mode, out reference) &&
                        FacetForgePolarisedTests.Matrix(grains, wo, wi, mode, out a))
                    {
                        worst = Math.Max(worst, FacetForgePolarisedTests.Difference(reference, a));
                        ++compared;
                    }
                    SampledSpectrum with = grains.f(frame, wo, wi, mode, out bool _, out Attenuation _), without = grains.f(wo, wi, mode);
                    worstScalar = Math.Max(worstScalar, Math.Abs(with[0] - without[0]));
                }
            }
            Console.WriteLine("MEASURE polygrain polarised, one scattering on grains of one metal against the microfacet conductor of pbrt (worst element): {0:E1} ({1} " +
                              "pairs); scalar with and without the matrix {2:E1}", worst, compared, worstScalar);
            EXPECT_GT(compared, 1000);
            EXPECT_LT(worst, 1e-3);
            EXPECT_EQ(0.0, worstScalar);
        }

        [TEST("PolyGrainPolarised", "ChalkDepolarises")]
        public static void ChalkDepolarises()
        {
            RNG rng = new RNG(4);
            PolyGrainBxDF chalk = new PolyGrainBxDF(PolyGrainTests.Two(), 4);
            for (int j = 0; j < 2; ++j)
            {
                chalk.SetGrainDiffuse(j, S(0.8f));
            }
            chalk.SetBulk(new DiffuseBxDF(S(0.5f)));
            chalk.maxOrder = 8;
            double worst = 0;
            int compared = 0;
            for (int i = 0; i < 300; ++i)
            {
                Vector3f wo = FacetForgePolarisedTests.Upper(ref rng), wi = FacetForgePolarisedTests.Upper(ref rng);
                MuellerMatrix a;
                if (!FacetForgePolarisedTests.Matrix(chalk, wo, wi, TransportMode.Radiance, out a))
                {
                    continue;
                }
                ++compared;
                for (int row = 0; row < 4; ++row)
                {
                    for (int column = 0; column < 4; ++column)
                    {
                        if (row != 0 || column != 0)
                        {
                            worst = Math.Max(worst, Math.Abs(a.Get(row, column)[0]));
                        }
                    }
                }
                EXPECT_LT(Math.Abs(a.Get(0, 0)[0] - 1), 1e-4);
            }
            Console.WriteLine("MEASURE polygrain polarised, diffuse grains on a diffuse bulk: largest element beside the first {0:E1} ({1} pairs)", worst, compared);
            EXPECT_GT(compared, 250);
            EXPECT_EQ(0.0, worst);
        }

        // A path from the camera and the same path from the light: the walk starts from the other end and meets the events in
        // the other order. The walk from one direction is not reciprocal in its later scatterings (the intensity by 2 % of f,
        // polygrain_walk_test.cs), and neither are its matrices: this measures by how much, as a share of f. The elements have
        // the same signs and sizes from the two ends; f() with `reciprocal` returns their mean.
        [TEST("PolyGrainPolarised", "CameraPathIsLightPath")]
        public static void CameraPathIsLightPath()
        {
            RNG rng = new RNG(8);
            PolyGrainBxDF walk = PolyGrainTests.Gold(PolyGrainTests.Two(), 0.5f, 4);
            walk.maxOrder = 16;
            walk.reciprocal = false;
            PolyGrainBxDF once = PolyGrainTests.Gold(PolyGrainTests.Two(), 0.5f, 4);
            BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), walk), bsdfOnce = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), once);
            double worst = 0;
            for (int pair = 0; pair < 4; ++pair)
            {
                Vector3f wo = FacetForgePolarisedTests.Upper(ref rng), wi = FacetForgePolarisedTests.Upper(ref rng);
                double[] a = new double[16], b = new double[16], first = new double[16], square = new double[16];
                int estimates = 20000;
                for (int e = 0; e < estimates; ++e)
                {
                    Vector3f d = 1e-4f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                    Vector3f o = Normalize(wo + d), i = Normalize(wi - d);
                    SampledSpectrum f1 = bsdf.f(o, i, TransportMode.Radiance, out bool p1, out Attenuation A1);
                    SampledSpectrum f2 = bsdf.f(i, o, TransportMode.Importance, out bool p2, out Attenuation A2);
                    SampledSpectrum f0 = bsdfOnce.f(o, i, TransportMode.Radiance, out bool p0, out Attenuation A0);
                    MuellerMatrix m1 = p1 ? A1.GetMuellerMatrix() : default, m2 = p2 ? A2.GetMuellerMatrix() : default, m0 = p0 ? A0.GetMuellerMatrix() : default;
                    for (int c = 0; c < 16; ++c)
                    {
                        double x = p1 ? m1.Get(c / 4, c % 4)[0] * f1[0] : 0, y = p2 ? m2.Get(c / 4, c % 4)[0] * f2[0] : 0;
                        a[c] += x / estimates;
                        b[c] += y / estimates;
                        first[c] += (p0 ? m0.Get(c / 4, c % 4)[0] * f0[0] : 0) / estimates;
                        square[c] += (x - y) * (x - y);
                    }
                }
                double sigmas = 0, share = 0;
                for (int c = 0; c < 16; ++c)
                {
                    // what the later scatterings add to the element, from each end; they may differ by the 5 % that the walk is not
                    // reciprocal by
                    double la = a[c] - first[c], lb = b[c] - first[c];
                    double error = Math.Sqrt(square[c]) / estimates + 1e-5;
                    double excess = Math.Max(0.0, Math.Abs(la - lb) - 0.05 * Math.Max(Math.Abs(la), Math.Abs(lb)));
                    sigmas = Math.Max(sigmas, excess / error);
                    share = Math.Max(share, (Math.Abs(a[c] - b[c]) - 3 * error) / a[0]);
                }
                Console.WriteLine("MEASURE polygrain polarised, pair {0}: from the camera f {1:F4}, m01 {2:F5}, m10 {3:F5}, m11 {4:F4}, m22 {5:F4}, m33 {6:F4}, m23 {7:F5}; " +
                                  "from the light f {8:F4}, m01 {9:F5}, m10 {10:F5}, m11 {11:F4}, m22 {12:F4}, m33 {13:F4}, m23 {14:F5}; one scattering f {15:F4}, m11 " +
                                  "{16:F4}; largest difference of an element {17:P1} of f", pair, a[0], a[1], a[4], a[5], a[10], a[15], a[11], b[0], b[1], b[4], b[5], b[10], b[15], b[11],
                                  first[0], first[5], share);
                worst = Math.Max(worst, share);
            }
            Console.WriteLine("MEASURE polygrain polarised, the walk from one direction: the elements of camera and light paths differ by up to {0:P1} of f", worst);
            EXPECT_LT(worst, 0.08);
        }

        [TEST("PolyGrainPolarised", "SampledAgainstEvaluated")]
        public static void SampledAgainstEvaluated()
        {
            PolyGrainBxDF once = PolyGrainTests.Gold(PolyGrainTests.Two(), 0.5f, 4);
            FacetForgePolarisedTests.SampledAgainstEvaluated(once, "polygrain, metal grains on a diffuse bulk, one scattering", 200000);
            PolyGrainBxDF oneWay = PolyGrainTests.Gold(PolyGrainTests.Two(), 0.5f, 4);
            oneWay.maxOrder = 16;
            oneWay.reciprocal = false;
            FacetForgePolarisedTests.SampledAgainstEvaluated(oneWay, "polygrain, metal grains on a diffuse bulk, 16 scatterings walked from one direction", 200000);
            PolyGrainBxDF both = PolyGrainTests.Gold(PolyGrainTests.Two(), 0.5f, 4);
            both.maxOrder = 16;
            FacetForgePolarisedTests.SampledAgainstEvaluated(both, "polygrain, metal grains on a diffuse bulk, 16 scatterings walked from both directions", 200000);
            PolyGrainBxDF onMetal = PolyGrainTests.Gold(PolyGrainTests.One(0.4, 1.0), 0.5f, 4);
            onMetal.SetBulk(new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), S(1.1f), S(2.6f)));
            onMetal.maxOrder = 16;
            FacetForgePolarisedTests.SampledAgainstEvaluated(onMetal, "polygrain, metal grains on a rough metal bulk, 16 scatterings walked from both directions", 200000);
        }
    }
}
