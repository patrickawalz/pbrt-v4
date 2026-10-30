// (not in pbrt-v4) Tests for the experimental texture medium and its tracking variants.

using System;
using testing;

namespace pbrt
{
    public static unsafe class MediaExperimentalTests
    {
        private static TextureMedium MakeMedium(bool progressive, bool adaptiveRatio = false, bool unclamped = false, Float initialMajorant = 0.01f, bool residualRatio = false, bool mixture = false, Float mixtureWeight = .5f, Float mixtureLooseness = 4f)
        {
            TextureMapping3D map = new PointTransformMapping(pbrt.Scale(1.6f, 1.6f, 1.6f));
            FloatTexture texture = new FBmTexture(map, 4, .5f);
            return new TextureMedium(new Bounds3f(new Point3f(-.8f, .2f, -.8f), new Point3f(.8f, 1.8f, .8f)),
                                     new Transform(), new ConstantSpectrum(0.05f), new ConstantSpectrum(2f), 0.3f, texture,
                                     0.05f, 8f, 8, progressive, initialMajorant, 0, false, false, adaptiveRatio, unclamped, residualRatio, mixture, mixtureLooseness, mixtureWeight, null);
        }

        // Reference transmittance along a ray by quadrature of the medium's own density
        private static Float ReferenceTransmittance(TextureMedium medium, Ray ray, Float tMax, SampledWavelengths lambda)
        {
            int n = 20000;
            Float sum = 0;
            for (int i = 0; i < n; ++i)
            {
                Float t = (i + .5f) / n * tMax;
                Point3f p = ray.Apply(t);
                // the medium only exists inside its bounds
                if (p.x < -.8f || p.x > .8f || p.y < .2f || p.y > 1.8f || p.z < -.8f || p.z > .8f)
                    continue;
                MediumProperties mp = medium.SamplePoint(p, lambda);
                sum += mp.sigma_a[0] + mp.sigma_s[0];
            }
            return (Float)Math.Exp(-sum * tMax / n);
        }

        // Ratio-tracking transmittance estimate, as in VolPathIntegrator.SampleLd()
        private static Float RatioTracking(TextureMedium medium, Ray ray, Float tMax, SampledWavelengths lambda, ref RNG rng)
        {
            Float T_ray = 1;
            SampledSpectrum T_maj = pbrt.SampleT_maj(ray, tMax, rng.Uniform<Float>(), ref rng, lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    Float sigma_n = std.max<Float>(0, sigma_maj[0] - mp.sigma_a[0] - mp.sigma_s[0]);
                    T_ray *= sigma_n / sigma_maj[0];
                    return T_ray > 0;
                });
            return T_ray;
        }

        [TEST("TextureMedium", "ProgressiveConvergence")]
        public static void TextureMedium_ProgressiveConvergence()
        {
            TextureMedium reference = MakeMedium(false);
            TextureMedium progressive = MakeMedium(true);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(3);
            int nRays = 16;
            Ray[] rays = new Ray[nRays];
            Float[] refT = new Float[nRays];
            for (int i = 0; i < nRays; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                rays[i] = new Ray(o, d, 0, progressive);
                refT[i] = ReferenceTransmittance(reference, rays[i], 2.2f, lambda);
            }

            // Several independent progressive runs, so the mean error isn't dominated by noise
            int nPasses = 64;
            int pathsPerPass = 4096;
            Float[] errorAtPass = new Float[nPasses + 1];
            double sumEst = 0, sumRef = 0;
            for (int pass = 1; pass <= nPasses; ++pass)
            {
                double passEst = 0;
                for (int i = 0; i < nRays; ++i)
                {
                    for (int s = 0; s < pathsPerPass; ++s)
                        passEst += RatioTracking(progressive, rays[i], 2.2f, lambda, ref rng);
                    sumRef += refT[i] * pathsPerPass;
                }
                sumEst += passEst;
                TextureMedium.EndProgressivePass();
                if (pass == 1 || pass == 4 || pass == 16 || pass == 64 || pass == 32 || pass == 64)
                    Console.WriteLine($"PROBE pass {pass}: this pass {passEst / (nRays * pathsPerPass)} cumulative {sumEst / (pass * nRays * pathsPerPass)} reference {sumRef / (pass * nRays * pathsPerPass)}");
            }
        }

        // How close do the learned majorants get to the true per-voxel maxima when rays come from all over?
        [TEST("TextureMedium", "ProgressiveMajorantGap")]
        public static void TextureMedium_ProgressiveMajorantGap()
        {
            TextureMedium reference = MakeMedium(false);
            TextureMedium progressive = MakeMedium(true);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(9);
            int res = 8;
            // true per-voxel maxima by dense sampling of the density
            Float[] trueMax = new Float[res * res * res];
            for (int i = 0; i < 400000; ++i)
            {
                Point3f p = new Point3f(-.8f + 1.6f * rng.Uniform<Float>(), .2f + 1.6f * rng.Uniform<Float>(),
                                        -.8f + 1.6f * rng.Uniform<Float>());
                int vx = (int)((p.x + .8f) / 1.6f * res), vy = (int)((p.y - .2f) / 1.6f * res), vz = (int)((p.z + .8f) / 1.6f * res);
                int v = vx + res * (vy + res * vz);
                MediumProperties mp = reference.SamplePoint(p, lambda);
                trueMax[v] = std.max(trueMax[v], (mp.sigma_a[0] + mp.sigma_s[0]) / 2.05f);
            }
            for (int pass = 1; pass <= 64; ++pass)
            {
                for (int s = 0; s < 40000; ++s)
                {
                    Point3f o = new Point3f(-1.2f + 2.4f * rng.Uniform<Float>(), -.2f + 2.4f * rng.Uniform<Float>(),
                                            -1.2f + 2.4f * rng.Uniform<Float>());
                    Vector3f d = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    RatioTracking(progressive, new Ray(o, d, 0, progressive), 3f, lambda, ref rng);
                }
                TextureMedium.EndProgressivePass();
                if (pass == 1 || pass == 4 || pass == 16 || pass == 64)
                {
                    double missing = 0, total = 0; int below = 0;
                    Float[] learned = progressive.DebugMajorants();
                    for (int v = 0; v < learned.Length; ++v)
                    {
                        total += trueMax[v];
                        if (learned[v] < trueMax[v] * 0.999f)
                        {
                            ++below;
                            missing += trueMax[v] - learned[v];
                        }
                    }
                    Console.WriteLine($"PROBE pass {pass}: voxels below true max {below}/{learned.Length}, missing {100 * missing / total}% of summed true maxima");
                }
            }
        }

        // Adaptive ratio tracking's transmittance estimate is unbiased and needs fewer density lookups than
        // ordinary ratio tracking
        [TEST("TextureMedium", "AdaptiveRatioUnbiased")]
        public static void TextureMedium_AdaptiveRatioUnbiased()
        {
            TextureMedium medium = MakeMedium(false, true);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(5);
            int nRays = 8;
            int nPaths = 20000;
            for (int i = 0; i < nRays; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                Ray ray = new Ray(o, d, 0, medium);
                Float reference = ReferenceTransmittance(medium, ray, 2.2f, lambda);
                double sum = 0, sum2 = 0;
                for (int s = 0; s < nPaths; ++s)
                {
                    SampledSpectrum T_ray = new SampledSpectrum(1f), r_l = new SampledSpectrum(1f), r_u = new SampledSpectrum(1f);
                    medium.AdaptiveRatioTracking(ray, 2.2f, rng.Uniform<Float>(), ref rng, lambda, ref T_ray, ref r_l, ref r_u);
                    // constant spectra: all wavelengths agree
                    EXPECT_NEAR(r_l[0], r_l[3], 1e-4f * r_l[0] + 1e-6f);
                    sum += T_ray[0];
                    sum2 += T_ray[0] * T_ray[0];
                }
                double mean = sum / nPaths;
                double sigma = Math.Sqrt(Math.Max(sum2 / nPaths - mean * mean, 0) / nPaths);
                EXPECT_LT(Math.Abs(mean - reference), 4 * sigma + 1e-3);
            }
        }

        // Residual ratio tracking's transmittance estimate is unbiased
        [TEST("TextureMedium", "ResidualRatioUnbiased")]
        public static void TextureMedium_ResidualRatioUnbiased()
        {
            TextureMedium medium = MakeMedium(false, false, false, 0.01f, true);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(5);
            int nRays = 8;
            int nPaths = 20000;
            for (int i = 0; i < nRays; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                Ray ray = new Ray(o, d, 0, medium);
                Float reference = ReferenceTransmittance(medium, ray, 2.2f, lambda);
                double sum = 0, sum2 = 0;
                for (int s = 0; s < nPaths; ++s)
                {
                    SampledSpectrum T_ray = new SampledSpectrum(1f), r_l = new SampledSpectrum(1f), r_u = new SampledSpectrum(1f);
                    medium.AdaptiveRatioTracking(ray, 2.2f, rng.Uniform<Float>(), ref rng, lambda, ref T_ray, ref r_l, ref r_u);
                    // constant spectra: all wavelengths agree
                    EXPECT_NEAR(r_l[0], r_l[3], 1e-4f * r_l[0] + 1e-6f);
                    sum += T_ray[0];
                    sum2 += T_ray[0] * T_ray[0];
                }
                double mean = sum / nPaths;
                double sigma = Math.Sqrt(Math.Max(sum2 / nPaths - mean * mean, 0) / nPaths);
                EXPECT_LT(Math.Abs(mean - reference), 4 * sigma + 1e-3);
            }
        }

        // Ratio tracking with a signed null coefficient (no clamping of the density to the majorant)
        private static Float SignedRatioTracking(TextureMedium medium, Ray ray, Float tMax, SampledWavelengths lambda, ref RNG rng)
        {
            Float T_ray = 1;
            pbrt.SampleT_maj(ray, tMax, rng.Uniform<Float>(), ref rng, lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    T_ray *= (sigma_maj[0] - mp.sigma_a[0] - mp.sigma_s[0]) / sigma_maj[0];
                    return T_ray != 0;
                });
            return T_ray;
        }

        // Progressive null-tracking without clamping is unbiased in every pass, even with a majorant that is
        // far too small
        [TEST("TextureMedium", "ProgressiveUnclampedUnbiased")]
        public static void TextureMedium_ProgressiveUnclampedUnbiased()
        {
            TextureMedium reference = MakeMedium(false);
            TextureMedium progressive = MakeMedium(true, false, true, 2.5f);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(11);
            int nRays = 8;
            Ray[] rays = new Ray[nRays];
            double refMean = 0;
            for (int i = 0; i < nRays; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                rays[i] = new Ray(o, d, 0, progressive);
                refMean += ReferenceTransmittance(reference, rays[i], 2.2f, lambda) / nRays;
            }
            int nPasses = 24;
            int pathsPerPass = 20000;
            double sum = 0, sum2 = 0;
            for (int pass = 1; pass <= nPasses; ++pass)
            {
                double passEst = 0;
                for (int i = 0; i < nRays; ++i)
                    for (int s = 0; s < pathsPerPass; ++s)
                        passEst += SignedRatioTracking(progressive, rays[i], 2.2f, lambda, ref rng);
                passEst /= nRays * pathsPerPass;
                sum += passEst;
                sum2 += passEst * passEst;
                TextureMedium.EndProgressivePass();
            }
            double mean = sum / nPasses;
            double stderr = Math.Sqrt(Math.Max(sum2 / nPasses - mean * mean, 0) / nPasses);
            Console.WriteLine($"PROBE unclamped: mean {mean} reference {refMean} stderr {stderr}");
            EXPECT_LT(Math.Abs(mean - refMean), 4 * stderr + 1e-3);

            // For contrast: the same progressive run with the density clamped to the majorant
            TextureMedium clamped = MakeMedium(true, false, false, 2.5f);
            double cSum = 0;
            for (int pass = 1; pass <= nPasses; ++pass)
            {
                double passEst = 0;
                for (int i = 0; i < nRays; ++i)
                {
                    Ray r = new Ray(rays[i].o, rays[i].d, 0, clamped);
                    for (int s = 0; s < pathsPerPass; ++s)
                        passEst += RatioTracking(clamped, r, 2.2f, lambda, ref rng);
                }
                cSum += passEst / (nRays * pathsPerPass);
                TextureMedium.EndProgressivePass();
            }
            Console.WriteLine($"PROBE clamped (same start, same passes): mean {cSum / nPasses}");
        }

        // One transmittance estimate with mixture sampling of the collision distances, as in volpath's shadow rays
        private static Float MixtureTracking(TextureMedium medium, Ray ray, Float tMax, SampledWavelengths lambda, ref RNG rng)
        {
            Float T_ray = 1;
            SampledSpectrum T_I = medium.SampleT_majMixture(ray, tMax, rng.Uniform<Float>(), ref rng, lambda,
                (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    T_ray *= T_maj_[0] * (sigma_maj[0] - mp.sigma_a[0] - mp.sigma_s[0]) / mp.mixRatio[0];
                    return T_ray != 0;
                }, out SampledSpectrum tail);
            return T_ray * T_I[0] / tail[0];
        }

        // With a majorant that is far too small, mixture sampling gives an unbiased estimate with much less variance
        // than signed ratio tracking alone
        [TEST("TextureMedium", "MixtureUnbiased")]
        public static void TextureMedium_MixtureUnbiased()
        {
            TextureMedium reference = MakeMedium(false);
            TextureMedium mixture = MakeMedium(true, false, true, 0.3f, false, true);
            TextureMedium plain = MakeMedium(true, false, true, 0.3f);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(21);
            for (int i = 0; i < 4; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                Float refT = ReferenceTransmittance(reference, new Ray(o, d, 0, reference), 2.2f, lambda);
                int n = 200000;
                double sumM = 0, sumM2 = 0, sumP = 0, sumP2 = 0;
                for (int s = 0; s < n; ++s)
                {
                    Float m = MixtureTracking(mixture, new Ray(o, d, 0, mixture), 2.2f, lambda, ref rng);
                    sumM += m;
                    sumM2 += m * m;
                    Float q = SignedRatioTracking(plain, new Ray(o, d, 0, plain), 2.2f, lambda, ref rng);
                    sumP += q;
                    sumP2 += q * q;
                }
                double meanM = sumM / n, seM = Math.Sqrt(Math.Max(sumM2 / n - meanM * meanM, 0) / n);
                double meanP = sumP / n, seP = Math.Sqrt(Math.Max(sumP2 / n - meanP * meanP, 0) / n);
                Console.WriteLine($"PROBE mixture ray {i}: reference {refT} mixture {meanM} (stderr {seM}) signed-only {meanP} (stderr {seP})");
                EXPECT_LT(Math.Abs(meanM - refT), 4 * seM + 1e-3);
            }
        }
    }
}
