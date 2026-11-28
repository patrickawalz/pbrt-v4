// (not in pbrt-v4) Tests of sggxmedium.cs: the medium of SGGX microflakes.

using System;
using testing;

namespace pbrt
{
    public static unsafe class SGGXMediumTests
    {
        private static Vector3f OnSphere(RNG rng)
        {
            Float z = 1 - 2 * rng.Uniform<Float>(), phi = 2 * Pi * rng.Uniform<Float>(), r = SafeSqrt(1 - z * z);
            return new Vector3f(r * std.cos(phi), r * std.sin(phi), z);
        }

        private static SGGXMatrix[] Matrices()
        {
            Vector3f axis = Normalize(new Vector3f(0.3f, -0.5f, 0.8f));
            return new SGGXMatrix[]
            {
                SGGXMatrix.Uniaxial(axis, 1, 0.3f * 0.3f), SGGXMatrix.Uniaxial(axis, 0.3f * 0.3f, 1), SGGXMatrix.Uniaxial(axis, 1, 0.05f * 0.05f),
                SGGXMatrix.Uniaxial(new Vector3f(0, 0, 1), 0.6f * 0.6f, 1), new SGGXMatrix(1, 1, 1, 0, 0, 0),
                new SGGXMatrix(0.9f, 0.5f, 0.2f, 0.15f, -0.1f, 0.05f)
            };
        }

        // The phase function integrates to 1 over the directions, for any direction it is asked from, and the projected area is
        // what the distribution of the normals gives: the integral of D(m) <w, m> over the half of the normals that face w
        [TEST("SGGXMedium", "PhaseFunctionIsNormalised")]
        public static void PhaseFunctionIsNormalised()
        {
            RNG rng = new RNG(3);
            double worstPhase = 0, worstArea = 0;
            foreach (SGGXMatrix S in Matrices())
            {
                SGGXPhaseFunction phase = new SGGXPhaseFunction(S, new SampledSpectrum(SGGXPhaseFunction.PerfectMirrorK));
                for (int trial = 0; trial < 3; ++trial)
                {
                    Vector3f wo = OnSphere(rng);
                    // (the sphere by latitude and longitude around wo, where the phase function is smooth in both)
                    Vector3f t1, t2;
                    CoordinateSystem(wo, &t1, &t2);
                    int nTheta = 3000, nPhi = 1200;
                    double sum = 0, area = 0;
                    for (int i = 0; i < nTheta; ++i)
                    {
                        double theta = Math.PI * (i + 0.5) / nTheta;
                        double sine = Math.Sin(theta), cosine = Math.Cos(theta);
                        for (int j = 0; j < nPhi; ++j)
                        {
                            double phi = 2 * Math.PI * (j + 0.5) / nPhi;
                            Vector3f w = (Float)(sine * Math.Cos(phi)) * t1 + (Float)(sine * Math.Sin(phi)) * t2 + (Float)cosine * wo;
                            double weight = sine * (Math.PI / nTheta) * (2 * Math.PI / nPhi);
                            sum += phase.p(wo, w) * weight;
                            if (cosine > 0)
                            {
                                area += phase.D(w) * cosine * weight;
                            }
                        }
                    }
                    worstPhase = Math.Max(worstPhase, Math.Abs(sum - 1));
                    worstArea = Math.Max(worstArea, Math.Abs(area / S.ProjectedArea(wo) - 1));
                }
            }
            Console.WriteLine("SGGX: the phase function integrates to 1 within " + worstPhase + "; the projected area is that of the normals within " + worstArea);
            CHECK_LT(worstPhase, 5e-3);
            CHECK_LT(worstArea, 5e-3);
        }

        // The samples have the density that they say: a function of the direction averages over them, divided by nothing (the
        // weight p / pdf is 1), to its integral against the phase function; and what the medium scatters from one direction to
        // another, projected area times phase function, is the same both ways
        [TEST("SGGXMedium", "SamplingAndReciprocity")]
        public static void SamplingAndReciprocity()
        {
            RNG rng = new RNG(8);
            double worstMoment = 0, worstReciprocity = 0, worstWeight = 0;
            foreach (SGGXMatrix S in Matrices())
            {
                SGGXPhaseFunction phase = new SGGXPhaseFunction(S, new SampledSpectrum(SGGXPhaseFunction.PerfectMirrorK));
                Vector3f wo = OnSphere(rng);
                Vector3f probe = OnSphere(rng);
                int n = 400000;
                double sampled = 0, sampled2 = 0;
                for (int i = 0; i < n; ++i)
                {
                    pstd.optional<PhaseFunctionSample> s = phase.Sample_p(wo, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (!s)
                    {
                        continue;
                    }
                    PhaseFunctionSample sample = s.value();
                    worstWeight = Math.Max(worstWeight, Math.Abs(sample.p / sample.pdf - 1));
                    EXPECT_NEAR(1, Length(sample.wi), 1e-4f);
                    double d = Dot(sample.wi, probe);
                    sampled += d / n;
                    sampled2 += d * d / n;
                }
                // (the same two moments as integrals against the phase function, over latitude and longitude around wo)
                Vector3f t1, t2;
                CoordinateSystem(wo, &t1, &t2);
                int nTheta = 3000, nPhi = 1200;
                double uniform = 0, uniform2 = 0;
                for (int i = 0; i < nTheta; ++i)
                {
                    double theta = Math.PI * (i + 0.5) / nTheta;
                    double sine = Math.Sin(theta), cosine = Math.Cos(theta);
                    for (int j = 0; j < nPhi; ++j)
                    {
                        double phi = 2 * Math.PI * (j + 0.5) / nPhi;
                        Vector3f w = (Float)(sine * Math.Cos(phi)) * t1 + (Float)(sine * Math.Sin(phi)) * t2 + (Float)cosine * wo;
                        double value = phase.p(wo, w) * sine * (Math.PI / nTheta) * (2 * Math.PI / nPhi);
                        double d = Dot(w, probe);
                        uniform += value * d;
                        uniform2 += value * d * d;
                    }
                }
                worstMoment = Math.Max(worstMoment, Math.Max(Math.Abs(sampled - uniform), Math.Abs(sampled2 - uniform2)));
                for (int i = 0; i < 2000; ++i)
                {
                    Vector3f a = OnSphere(rng), b = OnSphere(rng);
                    double forward = S.ProjectedArea(a) * phase.p(a, b), backward = S.ProjectedArea(b) * phase.p(b, a);
                    worstReciprocity = Math.Max(worstReciprocity, Math.Abs(forward - backward) / Math.Max(forward, 1e-12));
                }
            }
            Console.WriteLine("SGGX: moments of the samples against the integral differ by " + worstMoment + "; reciprocity within " + worstReciprocity +
                              "; weights off 1 by " + worstWeight);
            CHECK_LT(worstMoment, 0.004);
            CHECK_LT(worstReciprocity, 1e-4);
            CHECK_LT(worstWeight, 1e-6);
        }

        // The medium: the coefficients of the medium inside times the projected area for the direction, the majorant of a ray
        // never under them, the largest projected area that of the matrix, and no answer without a direction
        [TEST("SGGXMedium", "ExtinctionFollowsTheDirection")]
        public static void ExtinctionFollowsTheDirection()
        {
            RNG rng = new RNG(12);
            Spectrum sigma_a = new ConstantSpectrum(0.5f), sigma_s = new ConstantSpectrum(2);
            HomogeneousMedium inner = new HomogeneousMedium(sigma_a, sigma_s, 1, new ConstantSpectrum(0), 1, 0, Allocator.Default);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.4f);
            foreach (SGGXMatrix S in Matrices())
            {
                SGGXMedium medium = new SGGXMedium(inner, S, null);
                double largest = 0;
                for (int i = 0; i < 20000; ++i)
                {
                    Vector3f w = OnSphere(rng);
                    Float area = S.ProjectedArea(w);
                    largest = Math.Max(largest, area);
                    if (i < 200)
                    {
                        Point3f p = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                        // (a direction of any length, either way)
                        MediumProperties mp = medium.SamplePoint(p, (i % 2 == 0 ? 3.5f : -0.2f) * w, lambda, 0);
                        EXPECT_NEAR(0.5f * area, mp.sigma_a[0], 1e-5f);
                        EXPECT_NEAR(2 * area, mp.sigma_s[0], 1e-5f);
                        CHECK(mp.phase is SGGXPhaseFunction);
                        Ray ray = new Ray(p, 2 * w);
                        RayMajorantIterator iterator = medium.SampleRay(ray, 1, lambda, new ScratchBuffer());
                        pstd.optional<RayMajorantSegment> segment = iterator.Next();
                        CHECK((bool)segment);
                        CHECK_GE(segment.value().sigma_maj[0], mp.sigma_a[0] + mp.sigma_s[0]);
                        CHECK_LE(segment.value().sigma_maj[0], 1.001f * (mp.sigma_a[0] + mp.sigma_s[0]));
                    }
                }
                CHECK_LE(largest, S.LargestProjectedArea() * 1.0001);
                CHECK_GE(largest, S.LargestProjectedArea() * 0.995);
                bool refused = false;
                try
                {
                    medium.SamplePoint(new Point3f(0, 0, 0), lambda, 0);
                }
                catch (InvalidOperationException)
                {
                    refused = true;
                }
                CHECK(refused);
            }
        }
    }
}
