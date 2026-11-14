// (not in pbrt-v4) pearlescent_phase_test.cs
//
// PearlescentPhaseFunction (pearlescent_phase.cs):
//   - nothing is absorbed: over the samples of a direction, the weights spec / pdf (reflections and rays that go on) have
//     the mean 1 at every wavelength;
//   - the reflections alone have the mean of the integral of pSpectral() over the sphere, and PDF() is the density of the
//     sampled reflections (the same integral computed with the samples and with a quadrature over the normals);
//   - the kernel is symmetric: pSpectral(wo, wi) = pSpectral(wi, wo);
//   - a collision reflects with the reflectance of the platelet of PearlescentBxDF's slab.

using System;
using testing;

namespace pbrt
{
    public static unsafe class PearlescentPhaseTests
    {
        static PearlescentPhaseParameters Parameters(Float alphaX, Float alphaY, Vector3f axis)
        {
            PearlescentPhaseParameters p = new PearlescentPhaseParameters();
            p.stack.containerEta = 1.57;
            p.stack.coatingEta = 2.5;
            p.stack.coatingAbbe = 9;
            p.stack.coatingThickness = 60;
            p.stack.substrateEta = 1.58;
            p.stack.substrateAbbe = 63;
            p.stack.substrateThickness = 500;
            p.stack.substrateDeviation = 60;
            p.alphaX = alphaX;
            p.alphaY = alphaY;
            p.kappa = 3;
            p.kappaMaj = p.kappa * std.max<Float>(1f, std.max(alphaX, alphaY));
            p.axis = axis;
            return p;
        }

        [TEST("PearlescentPhase", "EnergyAndSampling")]
        public static void EnergyAndSampling()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            RNG rng = new RNG(7);
            double worstEnergy = 0, worstReflected = 0, worstDensity = 0, worstSymmetry = 0;
            foreach (Float alpha in new Float[] { 0.1f, 0.3f, 0.8f })
            {
                Float alphaY = 0.5f * alpha + 0.05f;
                Vector3f axis = new Vector3f(0.2f, -0.3f, 1);
                PearlescentPhaseFunction phase = new PearlescentPhaseFunction(Parameters(alpha, alphaY, axis), lambda);
                EXPECT_TRUE(phase.WavelengthDependent());
                for (int direction = 0; direction < 6; ++direction)
                {
                    Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    const int samples = 400000;
                    double[] total = new double[NSpectrumSamples], reflected = new double[NSpectrumSamples];
                    for (int i = 0; i < samples; ++i)
                    {
                        pstd.optional<PhaseFunctionSample> ps = phase.Sample_p(wo, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        if (!ps)
                        {
                            continue;
                        }
                        PhaseFunctionSample s = ps.value();
                        EXPECT_TRUE(s.hasSpec);
                        if (s.delta)
                        {
                            EXPECT_LT(Length(s.wi + wo), 1e-6f);
                        }
                        else if (i < 2000)
                        {
                            // The sample carries the value and the density of its direction
                            SampledSpectrum value = phase.pSpectral(wo, s.wi);
                            Float pdf = phase.PDF(wo, s.wi);
                            worstDensity = Math.Max(worstDensity, Math.Abs(pdf - s.pdf) / s.pdf);
                            worstDensity = Math.Max(worstDensity, Math.Abs(value[0] - s.spec[0]) / (s.spec[0] + 1e-6));
                        }
                        for (int k = 0; k < NSpectrumSamples; ++k)
                        {
                            total[k] += s.spec[k] / s.pdf / samples;
                            if (!s.delta)
                            {
                                reflected[k] += s.spec[k] / s.pdf / samples;
                            }
                        }
                    }
                    // The integral of the reflection part over the sphere: over the normals m of the platelets on a grid drawn
                    // from D(m) m.z (the directions follow by reflection, d wi = 4 |wo . m| dm), so that the peak of a smooth
                    // distribution is resolved
                    double[] integral = new double[NSpectrumSamples];
                    Vector3f n = Normalize(axis);
                    Vector3f helper = std.abs(n.x) > 0.9f ? new Vector3f(0, 1, 0) : new Vector3f(1, 0, 0);
                    Vector3f t = Normalize(Cross(helper, n));
                    Vector3f b = Cross(n, t);
                    Vector3f local = new Vector3f(Dot(wo, t), Dot(wo, b), Dot(wo, n));
                    const int grid = 700;
                    for (int i = 0; i < grid; ++i)
                    {
                        for (int j = 0; j < grid; ++j)
                        {
                            double u1 = (i + 0.5) / grid, phi = 2 * Math.PI * (j + 0.5) / grid;
                            double r = Math.Sqrt(u1 / (1 - u1));
                            Vector3f m = Normalize(new Vector3f((Float)(alpha * r * Math.Cos(phi)), (Float)(alphaY * r * Math.Sin(phi)), 1));
                            double d = m.x * m.x / (alpha * alpha) + m.y * m.y / (alphaY * alphaY) + m.z * m.z;
                            double D = 1 / (Math.PI * alpha * alphaY * d * d);
                            Float cosine = Dot(local, m);
                            Vector3f reflected_ = m * (2 * cosine) - local;
                            Vector3f wi = t * reflected_.x + b * reflected_.y + n * reflected_.z;
                            SampledSpectrum value = phase.pSpectral(wo, wi);
                            double weight = 4 * Math.Abs(cosine) / (D * m.z) / ((double)grid * grid);
                            for (int k = 0; k < NSpectrumSamples; ++k)
                            {
                                integral[k] += value[k] * weight;
                            }
                        }
                    }
                    double pdfIntegral = 0;
                    for (int i = 0; i < 2000; ++i)
                    {
                        Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        SampledSpectrum value = phase.pSpectral(wo, wi);
                        SampledSpectrum swapped = phase.pSpectral(wi, wo);
                        worstSymmetry = Math.Max(worstSymmetry, Math.Abs(swapped[0] - value[0]) / (value[0] + 1e-4));
                    }
                    for (int k = 0; k < NSpectrumSamples; ++k)
                    {
                        worstEnergy = Math.Max(worstEnergy, Math.Abs(total[k] - 1));
                        worstReflected = Math.Max(worstReflected, Math.Abs(reflected[k] - integral[k]) / (integral[k] + 1e-3));
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE pearlescent phase: mean weight - 1: %.2e; reflected weight against the integral of pSpectral: " +
                                                      "%.2e; sample against p and PDF: %.2e; p(wo, wi) against p(wi, wo): %.2e", worstEnergy, worstReflected,
                                                      worstDensity, worstSymmetry));
            EXPECT_LT(worstEnergy, 5e-3);
            EXPECT_LT(worstReflected, 0.03);
            EXPECT_LT(worstDensity, 1e-3);
            EXPECT_LT(worstSymmetry, 1e-3);
        }
    }
}
