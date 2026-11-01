// media_test.cpp

using System;
using testing;

namespace pbrt
{
    public static unsafe class MediaTests
    {
        [TEST("HenyeyGreenstein", "SamplingMatch")]
        public static void HenyeyGreenstein_SamplingMatch()
        {
            RNG rng = new RNG();
            for (float g = -.75f; g <= 0.75f; g += 0.25f)
            {
                HGPhaseFunction hg = new HGPhaseFunction(g);
                for (int i = 0; i < 100; ++i)
                {
                    Float wa = rng.Uniform<Float>(), wb = rng.Uniform<Float>();
                    Vector3f wo = SampleUniformSphere(new Point2f(wa, wb));
                    Float ua = rng.Uniform<Float>(), ub = rng.Uniform<Float>();
                    Point2f u = new Point2f(ua, ub);
                    pstd.optional<PhaseFunctionSample> ps = hg.Sample_p(wo, u);
                    EXPECT_TRUE(ps.has_value());
                    // Phase function is normalized, and the sampling method should be
                    // exact.
                    EXPECT_EQ(ps.value().p, ps.value().pdf);
                    EXPECT_NEAR(ps.value().p, hg.p(wo, ps.value().wi), 1e-4f, "Failure with g = " + g);
                }
            }
        }

        [TEST("HenyeyGreenstein", "SamplingOrientationForward")]
        public static void HenyeyGreenstein_SamplingOrientationForward()
        {
            HGPhaseFunction hg = new HGPhaseFunction(0.95f);
            Vector3f wo = new Vector3f(-1, 0, 0);
            int nForward = 0, nBackward = 0;
            foreach (Point2f u in new Uniform2D(100))
            {
                pstd.optional<PhaseFunctionSample> ps = hg.Sample_p(wo, u);
                EXPECT_TRUE(ps.has_value());
                if (ps.value().wi.x > 0)
                    ++nForward;
                else
                    ++nBackward;
            }
            // With g = 0.95, almost all of the samples should have wi.x > 0.
            EXPECT_GE(nForward, 10 * nBackward);
        }

        [TEST("HenyeyGreenstein", "SamplingOrientationBackward")]
        public static void HenyeyGreenstein_SamplingOrientationBackward()
        {
            HGPhaseFunction hg = new HGPhaseFunction(-0.95f);
            Vector3f wo = new Vector3f(-1, 0, 0);
            int nForward = 0, nBackward = 0;
            foreach (Point2f u in new Uniform2D(100))
            {
                pstd.optional<PhaseFunctionSample> ps = hg.Sample_p(wo, u);
                EXPECT_TRUE(ps.has_value());
                if (ps.value().wi.x > 0)
                    ++nForward;
                else
                    ++nBackward;
            }
            // With g = -0.95, almost all of the samples should have wi.x < 0.
            EXPECT_GE(nBackward, 10 * nForward);
        }

        [TEST("HenyeyGreenstein", "Normalized")]
        public static void HenyeyGreenstein_Normalized()
        {
            RNG rng = new RNG();
            for (float g = -.75f; g <= 0.75f; g += 0.25f)
            {
                HGPhaseFunction hg = new HGPhaseFunction(g);
                Float wa = rng.Uniform<Float>(), wb = rng.Uniform<Float>();
                Vector3f wo = SampleUniformSphere(new Point2f(wa, wb));
                Float sum = 0;
                int sqrtSamples = 64;
                int nSamples = sqrtSamples * sqrtSamples;
                foreach (Point2f u in new Stratified2D(sqrtSamples, sqrtSamples))
                {
                    Vector3f wi = SampleUniformSphere(u);
                    sum += hg.p(wo, wi);
                }
                // Phase function should integrate to 1/4pi.
                EXPECT_NEAR(sum / nSamples, 1.0 / (4.0 * Pi), 1e-3f);
            }
        }

        [TEST("HenyeyGreenstein", "g")]
        public static void HenyeyGreenstein_g()
        {
            RNG rng = new RNG();
            for (float g = -.75f; g <= 0.75f; g += 0.25f)
            {
                HGPhaseFunction hg = new HGPhaseFunction(g);
                Float wa = rng.Uniform<Float>(), wb = rng.Uniform<Float>();
                Vector3f wo = SampleUniformSphere(new Point2f(wa, wb));
                Float sum = 0;
                int sqrtSamples = 64;
                int nSamples = sqrtSamples * sqrtSamples;
                foreach (Point2f u in new Stratified2D(sqrtSamples, sqrtSamples))
                {
                    Vector3f wi = SampleUniformSphere(u);
                    // Negate dot to match direction convention
                    sum += hg.p(wo, wi) * -Dot(wo, wi);
                }
                Float gEst = sum / (nSamples * UniformSpherePDF());
                EXPECT_NEAR(g, gEst, .01);
            }
        }

        // (not in pbrt-v4) The phase function of a collision in overlapping volumes: sampling matches evaluation and the mean
        // cosine is the weighted mean of the components
        [TEST("MixturePhaseFunction", "SamplingMatchAndMeanCosine")]
        public static void MixturePhaseFunction_SamplingMatchAndMeanCosine()
        {
            PhaseFunction[] phases = { new HGPhaseFunction(0.6f), new HGPhaseFunction(-0.4f), new HGPhaseFunction(0.1f) };
            Float[] weights = { 0.5f, 0.3f, 0.2f };
            MixturePhaseFunction mixture = new MixturePhaseFunction(phases, weights);
            Vector3f wo = new Vector3f(0, 0, 1);
            RNG rng = new RNG();
            Float meanCosine = 0;
            int n = 200000;
            for (int i = 0; i < n; ++i)
            {
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<PhaseFunctionSample> ps = mixture.Sample_p(wo, u);
                EXPECT_TRUE(ps.has_value());
                EXPECT_NEAR(ps.value().p, mixture.p(wo, ps.value().wi), 1e-4f);
                EXPECT_NEAR(ps.value().pdf, mixture.PDF(wo, ps.value().wi), 1e-4f);
                meanCosine += -Dot(wo, ps.value().wi);
            }
            meanCosine /= n;
            EXPECT_NEAR(0.5f * 0.6f + 0.3f * -0.4f + 0.2f * 0.1f, meanCosine, 0.01f);

            // Evaluation integrates to one
            Float sum = 0;
            int sqrtSamples = 64;
            foreach (Point2f u in new Stratified2D(sqrtSamples, sqrtSamples))
                sum += mixture.p(wo, SampleUniformSphere(u));
            EXPECT_NEAR(1.0f, sum / (sqrtSamples * sqrtSamples * UniformSpherePDF()), 0.01f);
        }

        // (not in pbrt-v4) Fong et al., section 3.9: the coefficients and the majorants of overlapping media add up
        [TEST("CompositeMedium", "SumsCoefficientsAndMajorants")]
        public static void CompositeMedium_SumsCoefficientsAndMajorants()
        {
            ConstantSpectrum zero = new ConstantSpectrum(0.0f);
            Medium a = new HomogeneousMedium(new ConstantSpectrum(0.5f), new ConstantSpectrum(1.5f), 1.0f, zero, 0.0f, 0.3f, null);
            Medium b = new HomogeneousMedium(new ConstantSpectrum(0.25f), new ConstantSpectrum(2.0f), 1.0f, zero, 0.0f, 0.3f, null);
            CompositeMedium composite = new CompositeMedium(new Medium[] { a, b }, new int[] { 0, 0 });
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);

            MediumProperties mp = composite.SamplePoint(new Point3f(0, 0, 0), lambda);
            EXPECT_NEAR(0.75f, mp.sigma_a[0], 1e-5f);
            EXPECT_NEAR(3.5f, mp.sigma_s[0], 1e-5f);

            Ray ray = new Ray(new Point3f(0, 0, 0), new Vector3f(0, 0, 1));
            RayMajorantIterator iterator = composite.SampleRay(ray, 5, lambda, new ScratchBuffer());
            pstd.optional<RayMajorantSegment> segment = iterator.Next();
            EXPECT_TRUE(segment.has_value());
            EXPECT_NEAR(0.0f, segment.value().tMin, 1e-6f);
            EXPECT_NEAR(5.0f, segment.value().tMax, 1e-5f);
            EXPECT_NEAR(4.25f, segment.value().sigma_maj[0], 1e-5f);
            EXPECT_FALSE(iterator.Next().has_value());
        }
    }
}
