// (not in pbrt-v4) invertsample_test.cs
//
// BxDF.InvertSample() and PhaseFunction.InvertSample_p(): for random directions and random numbers, the direction that
// Sample_f() returns is inverted, uc falls in the interval of the inverse, and sampling again with the inverted numbers
// returns the direction. Both sides of the surface and both lobes of the BxDFs that have two.

using System;
using testing;

namespace pbrt
{
    public static unsafe class InvertSampleTests
    {
        static void RoundTrip(BxDF bxdf, string name, bool usesU)
        {
            RNG rng = new RNG(7);
            double worstDirection = 0, worstNumber = 0;
            int tried = 0, inverted = 0, outside = 0;
            for (int i = 0; i < 20000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Float uc = rng.Uniform<Float>();
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                if (!bs || bs.value().pdf == 0 || !bs.value().f)
                {
                    continue;
                }
                ++tried;
                Vector3f wi = bs.value().wi;
                pstd.optional<BxDFSampleInverse> inverse = bxdf.InvertSample(wo, wi, TransportMode.Radiance);
                if (!inverse)
                {
                    continue;
                }
                ++inverted;
                if (uc < inverse.value().ucMin - 1e-4f || uc > inverse.value().ucMax + 1e-4f)
                {
                    ++outside;
                }
                EXPECT_TRUE(inverse.value().anyU == !usesU);
                if (usesU)
                {
                    // (numbers at the two ends of a circle are the same number)
                    double du = Math.Abs(inverse.value().u.x - u.x), dv = Math.Abs(inverse.value().u.y - u.y);
                    worstNumber = Math.Max(worstNumber, Math.Min(Math.Max(Math.Min(du, 1 - du), Math.Min(dv, 1 - dv)), 1.0));
                }
                Float middle = (inverse.value().ucMin + inverse.value().ucMax) / 2;
                pstd.optional<BSDFSample> again = bxdf.Sample_f(wo, middle, inverse.value().u, TransportMode.Radiance, BxDFReflTransFlags.All);
                worstDirection = Math.Max(worstDirection, again ? Length(again.value().wi - wi) : 2.0);
            }
            Console.WriteLine("MEASURE InvertSample, {0}: {1} of {2} samples inverted; direction again off by at most {3:E2}, numbers by {4:E2}; uc outside its interval {5}",
                              name, inverted, tried, worstDirection, worstNumber, outside);
            EXPECT_GT(inverted, (int)(0.99 * tried));
            EXPECT_LT(worstDirection, 5e-3);
            EXPECT_EQ(outside, 0);
        }

        [TEST("InvertSample", "BxDFs")]
        public static void BxDFs()
        {
            RoundTrip(new DiffuseBxDF(new SampledSpectrum(0.8f)), "diffuse", true);
            RoundTrip(new DiffuseTransmissionBxDF(new SampledSpectrum(0.5f), new SampledSpectrum(0.4f)), "diffuse transmission", true);
            RoundTrip(new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), new SampledSpectrum(0.2f), new SampledSpectrum(3.9f)),
                      "rough conductor 0.3", true);
            RoundTrip(new ConductorBxDF(new TrowbridgeReitzDistribution(0.5f, 0.05f), new SampledSpectrum(0.2f), new SampledSpectrum(3.9f)),
                      "rough conductor 0.5 0.05", true);
            RoundTrip(new ConductorBxDF(new TrowbridgeReitzDistribution(0, 0), new SampledSpectrum(0.2f), new SampledSpectrum(3.9f)),
                      "smooth conductor", false);
            RoundTrip(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0, 0)), "smooth dielectric", false);
            RoundTrip(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.3f, 0.3f)), "rough dielectric 0.3", true);
            RoundTrip(new ThinDielectricBxDF(1.5f), "thin dielectric", false);
            RoundTrip(new PhaseBxDF(new HGPhaseFunction(0.6f)), "Henyey-Greenstein 0.6", true);
            RoundTrip(new PhaseBxDF(new HGPhaseFunction(-0.3f)), "Henyey-Greenstein -0.3", true);
            RoundTrip(new PhaseBxDF(new HGPhaseFunction(0)), "isotropic", true);
        }
    }
}
