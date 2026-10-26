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
    }
}
