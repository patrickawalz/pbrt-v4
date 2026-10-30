// (not in pbrt-v4) Tests for the experimental texture medium and its tracking variants.

using System;
using testing;

namespace pbrt
{
    public static unsafe class MediaExperimentalTests
    {
        private static TextureMedium MakeMedium(bool progressive, bool adaptiveRatio = false, bool unclamped = false, Float initialMajorant = 0.01f, bool residualRatio = false, bool mixture = false, Float mixtureWeight = .5f, Float mixtureLooseness = 4f, Float boundsFloor = 0f, bool hierarchical = false, bool kettunen = false)
        {
            TextureMapping3D map = new PointTransformMapping(pbrt.Scale(1.6f, 1.6f, 1.6f));
            FloatTexture texture = new FBmTexture(map, 4, .5f);
            return new TextureMedium(new Bounds3f(new Point3f(-.8f, .2f, -.8f), new Point3f(.8f, 1.8f, .8f)),
                                     new Transform(), new ConstantSpectrum(0.05f), new ConstantSpectrum(2f), 0.3f, texture,
                                     0.05f, 8f, 8, progressive, initialMajorant, 0, false, false, adaptiveRatio, unclamped, residualRatio, mixture, mixtureLooseness, mixtureWeight, boundsFloor, hierarchical, 4, 2, .7f, kettunen, 4, 0f, false, "", null);
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

        // Progressive tracking started from the analytic texture bounds (floored at a fraction of them) needs no
        // warm-up, stays unbiased and has bounded variance
        [TEST("TextureMedium", "BoundsFloorUnbiased")]
        public static void TextureMedium_BoundsFloorUnbiased()
        {
            TextureMedium reference = MakeMedium(false);
            TextureMedium floored = MakeMedium(true, false, false, 0.01f, false, false, .5f, 4f, .25f);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(31);
            for (int i = 0; i < 4; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                Float refT = ReferenceTransmittance(reference, new Ray(o, d, 0, reference), 2.2f, lambda);
                int n = 200000;
                double sum = 0, sum2 = 0;
                for (int s = 0; s < n; ++s)
                {
                    Float q = SignedRatioTracking(floored, new Ray(o, d, 0, floored), 2.2f, lambda, ref rng);
                    sum += q;
                    sum2 += q * q;
                }
                double mean = sum / n;
                double sigma = Math.Sqrt(Math.Max(sum2 / n - mean * mean, 0) / n);
                Console.WriteLine($"PROBE boundsfloor: mean {mean} reference {refT} stderr {sigma}");
                EXPECT_LT(Math.Abs(mean - refT), 4 * sigma + 1e-3);
            }
        }

        // The hierarchical majorant grid bounds the density everywhere, is never looser than the flat grid, and
        // gives an unbiased transmittance
        [TEST("TextureMedium", "HierarchicalGridValid")]
        public static void TextureMedium_HierarchicalGridValid()
        {
            TextureMedium flat = MakeMedium(false);
            TextureMedium tree = MakeMedium(false, false, false, 0.01f, false, false, .5f, 4f, 0f, true);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(41);
            double flatSum = 0, treeSum = 0;
            int violations = 0, nSegments = 0;
            for (int i = 0; i < 200; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .6f * (rng.Uniform<Float>() - .5f), .6f * (rng.Uniform<Float>() - .5f)));
                RayMajorantIterator it = tree.SampleRay(new Ray(o, d, 0, tree), 2.2f, lambda);
                RayMajorantIterator itFlat = flat.SampleRay(new Ray(o, d, 0, flat), 2.2f, lambda);
                Float tPrev = -1;
                for (pstd.optional<RayMajorantSegment> seg = it.Next(); seg; seg = it.Next())
                {
                    RayMajorantSegment sg = seg.value();
                    // segments are contiguous and ordered
                    EXPECT_GE(sg.tMin, tPrev - 1e-4f);
                    tPrev = sg.tMax;
                    ++nSegments;
                    treeSum += sg.sigma_maj[0] * (sg.tMax - sg.tMin);
                    for (int k = 0; k < 8; ++k)
                    {
                        Float t = sg.tMin + (sg.tMax - sg.tMin) * rng.Uniform<Float>();
                        MediumProperties mp = tree.SamplePoint(new Point3f(o.x + t * d.x, o.y + t * d.y, o.z + t * d.z), lambda);
                        if (mp.sigma_a[0] + mp.sigma_s[0] > sg.sigma_maj[0] * (1 + 1e-3f) + 1e-4f)
                            ++violations;
                    }
                }
                for (pstd.optional<RayMajorantSegment> seg = itFlat.Next(); seg; seg = itFlat.Next())
                    flatSum += seg.value().sigma_maj[0] * (seg.value().tMax - seg.value().tMin);
            }
            Console.WriteLine($"PROBE hierarchical: optical depth of the majorant tree {treeSum} flat {flatSum} segments {nSegments} violations {violations}");
            EXPECT_EQ(violations, 0);
            EXPECT_LE(treeSum, flatSum * (1 + 1e-3));

            for (int i = 0; i < 3; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                Float refT = ReferenceTransmittance(flat, new Ray(o, d, 0, flat), 2.2f, lambda);
                int n = 100000;
                double sum = 0, sum2 = 0;
                for (int s = 0; s < n; ++s)
                {
                    Float q = RatioTracking(tree, new Ray(o, d, 0, tree), 2.2f, lambda, ref rng);
                    sum += q;
                    sum2 += q * q;
                }
                double mean = sum / n;
                double sigma = Math.Sqrt(Math.Max(sum2 / n - mean * mean, 0) / n);
                Console.WriteLine($"PROBE hierarchical: mean {mean} reference {refT} stderr {sigma}");
                EXPECT_LT(Math.Abs(mean - refT), 4 * sigma + 1e-3);
            }
        }

        // The unbiased ray-marching transmittance estimator matches the quadrature reference, with the stderr of
        // ratio tracking printed for comparison
        [TEST("TextureMedium", "KettunenUnbiased")]
        public static void TextureMedium_KettunenUnbiased()
        {
            TextureMedium reference = MakeMedium(false, false, false, 0.01f, false, false, .5f, 4f, 0f, false, false);
            TextureMedium kettunen = MakeMedium(false, false, false, 0.01f, false, false, .5f, 4f, 0f, false, true);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(51);
            for (int i = 0; i < 4; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                Float refT = ReferenceTransmittance(reference, new Ray(o, d, 0, reference), 2.2f, lambda);
                int n = 100000;
                double sum = 0, sum2 = 0, rSum = 0, rSum2 = 0;
                for (int s = 0; s < n; ++s)
                {
                    Float q = kettunen.KettunenTransmittance(new Ray(o, d, 0, kettunen), 2.2f, ref rng, lambda)[0];
                    sum += q;
                    sum2 += q * q;
                    Float r = RatioTracking(reference, new Ray(o, d, 0, reference), 2.2f, lambda, ref rng);
                    rSum += r;
                    rSum2 += r * r;
                }
                double mean = sum / n;
                double sigma = Math.Sqrt(Math.Max(sum2 / n - mean * mean, 0) / n);
                double rSigma = Math.Sqrt(Math.Max(rSum2 / n - (rSum / n) * (rSum / n), 0) / n);
                Console.WriteLine($"PROBE kettunen: mean {mean} reference {refT} stderr {sigma} (ratio tracking stderr {rSigma})");
                EXPECT_LT(Math.Abs(mean - refT), 4 * sigma + 1e-3);
            }
        }

        // The min/max pyramid bounds a bilinear lookup over any rectangle of texture coordinates, for every wrap mode
        [TEST("TextureBounds", "ImageRegionRange")]
        public static void TextureBounds_ImageRegionRange()
        {
            Point2i res = new Point2i(37, 23);
            Image image = new Image(PixelFormat.Float, res, new string[] { "Y" });
            RNG rng = new RNG(61);
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                    image.SetChannel(new Point2i(x, y), 0, rng.Uniform<Float>() < .3f ? 0f : rng.Uniform<Float>() * 5);
            ImageRangePyramid pyramid = new ImageRangePyramid(image);
            WrapMode[] wraps = new WrapMode[] { WrapMode.Repeat, WrapMode.Clamp, WrapMode.Black };
            int violations = 0, queries = 0;
            double slack = 0, slackCount = 0;
            foreach (WrapMode wrap in wraps)
            {
                for (int i = 0; i < 400; ++i)
                {
                    Float s0 = -.7f + 2.4f * rng.Uniform<Float>(), t0 = -.7f + 2.4f * rng.Uniform<Float>();
                    Float ds = (i % 3 == 0 ? .02f : i % 3 == 1 ? .2f : 1.4f) * rng.Uniform<Float>();
                    Float dt = (i % 3 == 0 ? .02f : i % 3 == 1 ? .2f : 1.4f) * rng.Uniform<Float>();
                    bool ok = pbrt.ImageRegionRange(pyramid, wrap, s0, s0 + ds, t0, t0 + dt, out Float lo, out Float hi);
                    EXPECT_TRUE(ok);
                    Float bLo = Infinity, bHi = -Infinity;
                    for (int k = 0; k < 200; ++k)
                    {
                        Point2f st = new Point2f(s0 + ds * rng.Uniform<Float>(), t0 + dt * rng.Uniform<Float>());
                        Float v = image.BilerpChannel(st, 0, new WrapMode2D(wrap));
                        bLo = std.min(bLo, v);
                        bHi = std.max(bHi, v);
                    }
                    ++queries;
                    if (bLo < lo - 1e-5f || bHi > hi + 1e-5f)
                        ++violations;
                    slack += (hi - lo) - (bHi - bLo);
                    ++slackCount;
                }
            }
            Console.WriteLine($"PROBE image range: {queries} queries, {violations} violations, mean looseness {slack / slackCount}");
            EXPECT_EQ(violations, 0);
        }

        // A medium driven by an image texture through a planar mapping: the region bounds from the min/max pyramid bound
        // the density everywhere, are much tighter than the global bound, and the transmittance is unbiased
        [TEST("TextureMedium", "ImageDrivenUnbiased")]
        public static void TextureMedium_ImageDrivenUnbiased()
        {
            Point2i res = new Point2i(40, 30);
            Image image = new Image(PixelFormat.Float, res, new string[] { "R", "G", "B" });
            RNG imgRng = new RNG(71);
            for (int y = 0; y < res.y; ++y)
            {
                for (int x = 0; x < res.x; ++x)
                {
                    Float u = (x + .5f) / res.x - .35f, v = (y + .5f) / res.y - .6f;
                    Float blob = (Float)Math.Exp(-(u * u + v * v) * 18) + .6f * (Float)Math.Exp(-((u + .5f) * (u + .5f) + (v + .3f) * (v + .3f)) * 30);
                    Float value = std.max<Float>(0, blob - .1f + .15f * (imgRng.Uniform<Float>() - .5f));
                    for (int c = 0; c < 3; ++c)
                        image.SetChannel(new Point2i(x, y), c, value);
                }
            }
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "magi_image_medium_test.exr");
            EXPECT_TRUE(image.Write(file));
            MIPMapFilterOptions options = new MIPMapFilterOptions();
            options.filter = FilterFunction.Bilinear;
            TextureMapping2D mapping = new PlanarMapping(new Transform(), new Vector3f(.625f, 0, 0), new Vector3f(0, 0, .625f), .5f, .5f);
            FloatTexture texture = new FloatImageTexture(mapping, file, options, WrapMode.Clamp, 1f, false, ColorEncoding.Linear, null);

            Bounds3f bounds = new Bounds3f(new Point3f(-.8f, .2f, -.8f), new Point3f(.8f, 1.8f, .8f));
            TextureMedium MakeImageMedium(int majorantRes, bool hierarchical)
            {
                return new TextureMedium(bounds, new Transform(), new ConstantSpectrum(0.05f), new ConstantSpectrum(2f), 0.3f, texture,
                                         0f, 6f, majorantRes, false, 0.01f, 0, false, false, false, false, false, false, 4f, .5f, 0f,
                                         hierarchical, 4, 1, .7f, false, 4, 0f, true, "", null);
            }
            TextureMedium coarse = MakeImageMedium(1, false);
            TextureMedium fine = MakeImageMedium(16, false);
            TextureMedium tree = MakeImageMedium(8, true);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(72);
            int violations = 0;
            double coarseDepth = 0, fineDepth = 0, treeDepth = 0;
            for (int i = 0; i < 300; ++i)
            {
                Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, .5f * (rng.Uniform<Float>() - .5f), .5f * (rng.Uniform<Float>() - .5f)));
                foreach ((TextureMedium medium, int which) in new (TextureMedium, int)[] { (coarse, 0), (fine, 1), (tree, 2) })
                {
                    RayMajorantIterator it = medium.SampleRay(new Ray(o, d, 0, medium), 2.2f, lambda);
                    for (pstd.optional<RayMajorantSegment> seg = it.Next(); seg; seg = it.Next())
                    {
                        RayMajorantSegment sg = seg.value();
                        double depth = sg.sigma_maj[0] * (sg.tMax - sg.tMin);
                        if (which == 0) coarseDepth += depth;
                        else if (which == 1) fineDepth += depth;
                        else treeDepth += depth;
                        for (int k = 0; k < 6; ++k)
                        {
                            Float t = sg.tMin + (sg.tMax - sg.tMin) * rng.Uniform<Float>();
                            MediumProperties mp = medium.SamplePoint(new Point3f(o.x + t * d.x, o.y + t * d.y, o.z + t * d.z), lambda);
                            if (mp.sigma_a[0] + mp.sigma_s[0] > sg.sigma_maj[0] * (1 + 1e-3f) + 1e-4f)
                                ++violations;
                        }
                    }
                }
            }
            Console.WriteLine($"PROBE image medium: violations {violations}, majorant optical depth coarse {coarseDepth} grid16 {fineDepth} tree {treeDepth}");
            EXPECT_EQ(violations, 0);
            EXPECT_LT(fineDepth, coarseDepth);

            foreach (TextureMedium medium in new TextureMedium[] { fine, tree })
            {
                for (int i = 0; i < 3; ++i)
                {
                    Point3f o = new Point3f(-1, .3f + 1.4f * rng.Uniform<Float>(), -.7f + 1.4f * rng.Uniform<Float>());
                    Vector3f d = Normalize(new Vector3f(1, .3f * (rng.Uniform<Float>() - .5f), .3f * (rng.Uniform<Float>() - .5f)));
                    Float refT = ReferenceTransmittance(fine, new Ray(o, d, 0, fine), 2.2f, lambda);
                    int n = 100000;
                    double sum = 0, sum2 = 0;
                    for (int s2 = 0; s2 < n; ++s2)
                    {
                        Float q = RatioTracking(medium, new Ray(o, d, 0, medium), 2.2f, lambda, ref rng);
                        sum += q;
                        sum2 += q * q;
                    }
                    double mean = sum / n;
                    double sigma = Math.Sqrt(Math.Max(sum2 / n - mean * mean, 0) / n);
                    Console.WriteLine($"PROBE image medium: mean {mean} reference {refT} stderr {sigma}");
                    EXPECT_LT(Math.Abs(mean - refT), 4 * sigma + 1e-3);
                }
            }
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
