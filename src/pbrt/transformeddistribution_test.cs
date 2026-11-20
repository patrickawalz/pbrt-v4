// (not in pbrt-v4) transformeddistribution_test.cs
//
// The microsurface transformations (util/transformeddistribution.cs, and the "transform" of the components of Facet Forge):
//   - the numerical validation of the paper, with matrices drawn at random: the transformed distribution is a distribution (the
//     projected area of its normals along the normal is 1), and the projected area of its visible normals is that of its
//     masking, in the directions below the horizon too (the single sided form of the branch);
//   - a diagonal matrix is the anisotropy of the roughnesses, and a rotation is the distribution in a turned frame;
//   - the lobes of pbrt on a transformed distribution are reciprocal, keep their energy and sample what they evaluate;
//   - they are the first reflection of Facet Forge with the same transformation, and the walks of the two agree on the albedo;
//   - a transformed vMF of Facet Forge: its projected area against a quadrature, its visible normals against their density,
//     and a white furnace;
//   - the scene syntax.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class TransformedDistributionTests
    {
        // A rotation times a stretch and a shear
        static Float[] Matrix(ref RNG rng)
        {
            double angle = 2 * Math.PI * rng.Uniform<double>();
            double sx = 0.5 + 1.5 * rng.Uniform<double>(), sy = 0.5 + 1.5 * rng.Uniform<double>(), shear = 3 * rng.Uniform<double>() - 1.5;
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return new Float[] { (Float)(c * sx), (Float)(c * shear - s * sy), (Float)(s * sx), (Float)(s * shear + c * sy) };
        }

        static MicrofacetDistribution Original(int which)
        {
            switch (which)
            {
            case 0:
                return new TrowbridgeReitzDistribution(0.3f, 0.15f);
            case 1:
                return new BeckmannDistribution(0.4f, 0.25f);
            case 2:
                return new StudentTDistribution(0.3f, 0.5f, 3);
            default:
                return new MixtureDistribution(new MicrofacetDistribution[] { new BeckmannDistribution(0.5f, 0.5f), new TrowbridgeReitzDistribution(0.12f, 0.2f) },
                                               new Float[] { 0.4f, 0.6f });
            }
        }

        static string NameOf(int which)
        {
            return which == 0 ? "ggx" : which == 1 ? "beckmann" : which == 2 ? "studentt" : "mixture";
        }

        [TEST("TransformedDistribution", "ConstraintsUnderRandomMatrices")]
        public static void ConstraintsUnderRandomMatrices()
        {
            RNG rng = new RNG(41);
            const int Directions = 6;
            for (int which = 0; which < 4; ++which)
            {
                double worstArea = 0, worstVisible = 0, worstMasking = 0;
                for (int trial = 0; trial < 4; ++trial)
                {
                    Float[] m = Matrix(ref rng);
                    TransformedDistribution d = new TransformedDistribution(Original(which), m[0], m[1], m[2], m[3]);
                    Vector3f[] directions = new Vector3f[Directions];
                    double[] visible = new double[Directions];
                    for (int i = 0; i < Directions; ++i)
                    {
                        // (the first half above the horizon, the others below)
                        double cosine = (i < Directions / 2 ? 1 : -1) * (0.1 + 0.85 * rng.Uniform<double>()), phi = 2 * Math.PI * rng.Uniform<double>();
                        double sine = Math.Sqrt(1 - cosine * cosine);
                        directions[i] = new Vector3f((Float)(sine * Math.Cos(phi)), (Float)(sine * Math.Sin(phi)), (Float)cosine);
                    }
                    double area = 0;
                    // The hemisphere of the normals, by the polar angle (pi / 2) t^2 (finer at the normal, where the distributions peak)
                    // and the azimuth: the distributions are bounded there, which their slopes are not
                    int n = 1600;
                    for (int a = 0; a < n; ++a)
                    {
                        double t = (a + 0.5) / n, theta = 0.5 * Math.PI * t * t;
                        double sinTheta = Math.Sin(theta), cosTheta = Math.Cos(theta);
                        double jac = sinTheta * Math.PI * t / n * (2 * Math.PI / n);
                        for (int b = 0; b < n; ++b)
                        {
                            double phi = 2 * Math.PI * (b + 0.5) / n;
                            Vector3f h = new Vector3f((Float)(sinTheta * Math.Cos(phi)), (Float)(sinTheta * Math.Sin(phi)), (Float)cosTheta);
                            double weight = d.D(h) * jac;
                            area += weight * h.z;
                            for (int i = 0; i < Directions; ++i)
                            {
                                visible[i] += weight * Math.Max(0.0, Dot(directions[i], h));
                            }
                        }
                    }
                    worstArea = Math.Max(worstArea, Math.Abs(area - 1));
                    for (int i = 0; i < Directions; ++i)
                    {
                        double sigma = d.Sigma(directions[i]);
                        // (below the horizon the projected area is small, and the single precision of Sigma is an absolute error near
                        // 1e-6, that of the untransformed distribution too)
                        worstVisible = Math.Max(worstVisible, Math.Max(0.0, Math.Abs(visible[i] - sigma) - 5e-6) / visible[i]);
                        if (directions[i].z > 0)
                        {
                            // The shadowing constraint of the paper: the visible normals fill the projected area v.z / G1(v)
                            worstMasking = Math.Max(worstMasking, Math.Abs(visible[i] * d.G1(directions[i]) / directions[i].z - 1));
                        }
                    }
                }
                Console.WriteLine("MEASURE transformed {0}: projected area along the normal off 1 by {1:E2}; visible normals against Sigma {2:E2}; shadowing " +
                                  "constraint {3:E2}", NameOf(which), worstArea, worstVisible, worstMasking);
                EXPECT_LT(worstArea, 1e-4);
                EXPECT_LT(worstVisible, 1e-4);
                EXPECT_LT(worstMasking, 1e-4);
            }
        }

        [TEST("TransformedDistribution", "StretchAndRotation")]
        public static void StretchAndRotation()
        {
            RNG rng = new RNG(5);
            Float alpha = 0.4f, sx = 1.6f, sy = 0.7f;
            TransformedDistribution stretched = new TransformedDistribution(new TrowbridgeReitzDistribution(alpha, alpha), sx, 0, 0, sy);
            TrowbridgeReitzDistribution direct = new TrowbridgeReitzDistribution(alpha / sx, alpha / sy);
            Float angle = 0.6f, c = std.cos(angle), s = std.sin(angle);
            TrowbridgeReitzDistribution anisotropic = new TrowbridgeReitzDistribution(0.5f, 0.1f);
            TransformedDistribution turned = new TransformedDistribution(anisotropic, c, -s, s, c);
            double worstStretch = 0, worstTurn = 0;
            for (int i = 0; i < 4000; ++i)
            {
                Vector3f w = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f h = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (Math.Abs(w.z) < 0.02f || h.z < 0.02f)
                {
                    continue;
                }
                worstStretch = Math.Max(worstStretch, Math.Abs(stretched.D(h) - direct.D(h)) / Math.Max(direct.D(h), 1e-3f));
                worstStretch = Math.Max(worstStretch, Math.Abs(stretched.Sigma(w) - direct.Sigma(w)) / Math.Max(direct.Sigma(w), 1e-3f));
                worstStretch = Math.Max(worstStretch, Math.Abs(stretched.G1(w) - direct.G1(w)));
                // The turned frame: R^T v
                Vector3f hBack = new Vector3f(c * h.x + s * h.y, -s * h.x + c * h.y, h.z), wBack = new Vector3f(c * w.x + s * w.y, -s * w.x + c * w.y, w.z);
                worstTurn = Math.Max(worstTurn, Math.Abs(turned.D(h) - anisotropic.D(hBack)) / Math.Max(anisotropic.D(hBack), 1e-3f));
                worstTurn = Math.Max(worstTurn, Math.Abs(turned.Sigma(w) - anisotropic.Sigma(wBack)) / Math.Max(anisotropic.Sigma(wBack), 1e-3f));
            }
            Console.WriteLine("MEASURE transformed ggx: a stretch against the roughnesses divided by it {0:E2}; a rotation against the turned frame {1:E2}", worstStretch,
                              worstTurn);
            EXPECT_LT(worstStretch, 1e-3);
            EXPECT_LT(worstTurn, 1e-3);
            EXPECT_LT(Math.Abs(stretched.AlphaX() - alpha / sx), 1e-5);
            EXPECT_LT(Math.Abs(stretched.AlphaY() - alpha / sy), 1e-5);
        }

        [TEST("TransformedDistribution", "ChecksOfTheLobes")]
        public static void ChecksOfTheLobes()
        {
            BxDFCheckOptions options = new BxDFCheckOptions();
            // (a sheared ggx is a ggx of two roughnesses in a turned frame, here 0.26 and 0.15: a stronger shear is a rougher ggx,
            // whose density of sampled directions loses more below the horizon than the check of the density allows, as it does
            // without the transformation)
            TransformedDistribution sheared = new TransformedDistribution(new TrowbridgeReitzDistribution(0.2f, 0.2f), 1, 0.5f, 0, 1);
            BxDFCheck.Expect(BxDFCheck.Run(new ConductorBxDF(sheared, new SampledSpectrum(1f), new SampledSpectrum(1e4f)), "Conductor on a sheared ggx", options),
                             "Conductor on a sheared ggx", 2e-3, 1.005, 5, 0.9);
            BxDFCheck.Expect(BxDFCheck.Run(new DielectricBxDF(1.5f, sheared), "Dielectric on a sheared ggx", options), "Dielectric on a sheared ggx", 2e-3, 1.005, 5, 0.9);
            TransformedDistribution mixture = new TransformedDistribution(Original(3), 0.8f, -0.7f, 0.5f, 1.3f);
            BxDFCheck.Expect(BxDFCheck.Run(new ConductorBxDF(mixture, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)), "Conductor on a transformed mixture",
                                           options), "Conductor on a transformed mixture", 2e-3, 1.005, 5, 0.9);
            // Strata of a sheared and a plain distribution
            LayeredDistribution strata = new LayeredDistribution(new MicrofacetDistribution[] {
                new TransformedDistribution(new BeckmannDistribution(0.4f, 0.4f), 1, -1.1f, 0, 1), new TrowbridgeReitzDistribution(0.2f, 0.2f) }, new Float[] { 0.5f, 0.5f });
            BxDFCheck.Expect(BxDFCheck.Run(new ConductorBxDF(strata, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)), "Conductor on strata with a sheared one",
                                           options), "Conductor on strata with a sheared one", 2e-3, 1.005, 5, 0.9);
        }

        static double Worst(BxDF a, BxDF b, ulong seed, out int count)
        {
            RNG rng = new RNG(seed);
            double worst = 0;
            count = 0;
            for (int i = 0; i < 6000; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (Math.Abs(wo.z) < 0.05f || Math.Abs(wi.z) < 0.05f || wo.z * wi.z < 0)
                {
                    continue;
                }
                double x = a.f(wo, wi, TransportMode.Radiance)[0], y = b.f(wo, wi, TransportMode.Radiance)[0];
                if (Math.Max(x, y) > 1e-3)
                {
                    worst = Math.Max(worst, Math.Abs(x - y) / Math.Max(x, y));
                    ++count;
                }
            }
            return worst;
        }

        static double Albedo(BxDF bxdf, Vector3f wo, int count, ref RNG rng, out double error)
        {
            double sum = 0, squares = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Importance,
                                                             BxDFReflTransFlags.All);
                if (bs)
                {
                    double weight = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                    sum += weight;
                    squares += weight * weight;
                }
            }
            double mean = sum / count;
            error = Math.Sqrt(Math.Max(0.0, squares / count - mean * mean) / count);
            return mean;
        }

        [TEST("TransformedDistribution", "AgainstFacetForge")]
        public static void AgainstFacetForge()
        {
            Float[] m = new Float[] { 0.9f, 0.8f, -0.3f, 1.2f };
            TransformedDistribution d = new TransformedDistribution(new TrowbridgeReitzDistribution(0.35f, 0.2f), m[0], m[1], m[2], m[3]);
            ff.Surface surface = new ff.Surface();
            surface.ndf.n = 1;
            surface.ndf.c[0].InitHeightfield(ff.Kind.GGX, 0.35, 0.2, 2);
            surface.ndf.c[0].SetTransform(m[0], m[1], m[2], m[3]);
            surface.facet.kind = ff.FacetKind.Conductor;
            surface.facet.eta = new SampledSpectrum(0.2f);
            surface.facet.k = new SampledSpectrum(3.5f);
            ff.Surface once = surface.Clone();
            once.maxWalk = 1;
            int count;
            double worst = Worst(new ConductorBxDF(d, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)), new FacetForgeBxDF(once, null, 1.5f, false), 3, out count);
            Console.WriteLine("MEASURE transformed ggx: the conductor of pbrt against the first reflection of facet forge {0:E2} ({1} pairs)", worst, count);
            EXPECT_GT(count, 500);
            EXPECT_LT(worst, 2e-3);

            RNG rng = new RNG(9);
            MultiScatterConductorBxDF walked = new MultiScatterConductorBxDF(d, new MicrosurfaceHeightUniform(), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f), 100);
            FacetForgeBxDF forge = new FacetForgeBxDF(surface, null, 1.5f, false);
            foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
            {
                Vector3f wo = new Vector3f(0.6f * SafeSqrt(1 - cosine * cosine), 0.8f * SafeSqrt(1 - cosine * cosine), cosine);
                double e0, e1;
                double mine = Albedo(walked, wo, 100000, ref rng, out e0), theirs = Albedo(forge, wo, 100000, ref rng, out e1);
                Console.WriteLine("MEASURE transformed ggx, cosine {0}: albedo with every reflection {1:F4} against facet forge {2:F4} (+- {3:F4})", cosine, mine, theirs,
                                  Math.Sqrt(e0 * e0 + e1 * e1));
                EXPECT_LT(Math.Abs(mine - theirs), 5 * Math.Sqrt(e0 * e0 + e1 * e1) + 2e-3);
            }
        }

        // The share of the albedo that leaves on the side of wo
        static double ReflectedAlbedo(BxDF bxdf, Vector3f wo, int count, ref RNG rng, out double error, out double total)
        {
            double sum = 0, squares = 0, all = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Importance,
                                                             BxDFReflTransFlags.All);
                if (bs)
                {
                    double weight = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                    all += weight;
                    if (bs.value().wi.z * wo.z > 0)
                    {
                        sum += weight;
                        squares += weight * weight;
                    }
                }
            }
            double mean = sum / count;
            error = Math.Sqrt(Math.Max(0.0, squares / count - mean * mean) / count);
            total = all / count;
            return mean;
        }

        // The walks of Heitz et al. 2016 of the branch (multiscatterconductor, multiscatterdielectric, multiscatterdiffuse) on a
        // mixture of two sheared parts, against Facet Forge with the same transformations, and their checks
        [TEST("TransformedDistribution", "WalksOfHeitz")]
        public static void WalksOfHeitz()
        {
            Float[] m0 = new Float[] { 0.9f, 0.8f, -0.3f, 1.2f }, m1 = new Float[] { 1.4f, -0.6f, 0, 0.8f };
            MixtureDistribution d = new MixtureDistribution(new MicrofacetDistribution[] {
                new TransformedDistribution(new TrowbridgeReitzDistribution(0.35f, 0.2f), m0[0], m0[1], m0[2], m0[3]),
                new TransformedDistribution(new BeckmannDistribution(0.4f, 0.4f), m1[0], m1[1], m1[2], m1[3]) }, new Float[] { 0.6f, 0.4f });
            ff.Surface surface = new ff.Surface();
            surface.ndf.n = 2;
            surface.ndf.c[0].InitHeightfield(ff.Kind.GGX, 0.35, 0.2, 2);
            surface.ndf.c[0].SetTransform(m0[0], m0[1], m0[2], m0[3]);
            surface.ndf.c[0].w = 0.6;
            surface.ndf.c[1].InitHeightfield(ff.Kind.Beckmann, 0.4, 0.4, 2);
            surface.ndf.c[1].SetTransform(m1[0], m1[1], m1[2], m1[3]);
            surface.ndf.c[1].w = 0.4;
            surface.maxWalk = 100;
            ff.Surface glass = surface.Clone();
            glass.facet.kind = ff.FacetKind.Dielectric;
            ff.Surface chalk = surface.Clone();
            chalk.facet.kind = ff.FacetKind.Diffuse;
            chalk.facet.R = new SampledSpectrum(0.8f);
            MicrosurfaceHeight height = new MicrosurfaceHeightUniform();
            BxDF[] mine = new BxDF[] { new MultiScatterDielectricBxDF(d, height, 1.5f, 100), new MultiScatterDiffuseBxDF(d, height, new SampledSpectrum(0.8f), 100) };
            BxDF[] theirs = new BxDF[] { new FacetForgeBxDF(glass, null, 1.5f, true), new FacetForgeBxDF(chalk, null, 1.5f, false) };
            string[] names = new string[] { "dielectric", "diffuse 0.8" };
            RNG rng = new RNG(13);
            for (int which = 0; which < 2; ++which)
            {
                foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                {
                    Vector3f wo = new Vector3f(0.6f * SafeSqrt(1 - cosine * cosine), 0.8f * SafeSqrt(1 - cosine * cosine), cosine);
                    double e0, e1, t0, t1;
                    double a = ReflectedAlbedo(mine[which], wo, 100000, ref rng, out e0, out t0), b = ReflectedAlbedo(theirs[which], wo, 100000, ref rng, out e1, out t1);
                    Console.WriteLine("MEASURE walk of Heitz on two sheared parts, {0}, cosine {1}: reflected albedo {2:F4} against facet forge {3:F4} (+- {4:F4}); " +
                                      "whole albedo {5:F4} against {6:F4}", names[which], cosine, a, b, Math.Sqrt(e0 * e0 + e1 * e1), t0, t1);
                    EXPECT_LT(Math.Abs(a - b), 5 * Math.Sqrt(e0 * e0 + e1 * e1) + 2e-3);
                    if (which == 0)
                    {
                        // Nothing is absorbed
                        EXPECT_LT(Math.Abs(t0 - 1), 5e-3);
                    }
                }
            }
            // A white furnace of the conductor
            MultiScatterConductorBxDF mirror = new MultiScatterConductorBxDF(d, height, new SampledSpectrum(1f), new SampledSpectrum(1e5f), 200);
            double e, t;
            double white = ReflectedAlbedo(mirror, new Vector3f(0.48f, 0.64f, 0.6f), 100000, ref rng, out e, out t);
            Console.WriteLine("MEASURE walk of Heitz on two sheared parts, conductor: white furnace {0:F4} (+- {1:F4})", white, e);
            EXPECT_LT(Math.Abs(white - 1), 5 * e + 3e-3);
        }

        [TEST("TransformedDistribution", "TransformedVmf")]
        public static void TransformedVmf()
        {
            ff.NullTable table = new ff.NullTable();
            table.Init(ff.Kind.VMF, 0.5, 2);
            ff.Surface surface = new ff.Surface();
            surface.ndf.n = 1;
            ff.Comp c = surface.ndf.c[0];
            c.kind = ff.Kind.VMF;
            c.tab = table;
            c.SetTransform(1.3, 0.9, -0.2, 0.6);
            ff.Rnd rnd = new ff.Rnd(new RNG(23));
            foreach (double cosine in new double[] { 0.9, 0.3, -0.4 })
            {
                double sine = Math.Sqrt(1 - cosine * cosine);
                ff.V3 wi = new ff.V3(sine * Math.Cos(2.1), sine * Math.Sin(2.1), cosine);
                // The quadrature over the sphere of the normals
                double sigma = 0, mx = 0, my = 0, mz = 0;
                int nt = 1200, np = 1200;
                for (int a = 0; a < nt; ++a)
                {
                    double mu = -1 + 2 * (a + 0.5) / nt, sm = Math.Sqrt(1 - mu * mu);
                    for (int b = 0; b < np; ++b)
                    {
                        double phi = 2 * Math.PI * (b + 0.5) / np;
                        ff.V3 normal = new ff.V3(sm * Math.Cos(phi), sm * Math.Sin(phi), mu);
                        double weight = c.D(normal) * Math.Max(0.0, ff.Dot(wi, normal)) * (2.0 / nt) * (2 * Math.PI / np);
                        sigma += weight;
                        mx += weight * normal.x;
                        my += weight * normal.y;
                        mz += weight * normal.z;
                    }
                }
                double off = Math.Abs(sigma - c.Sigma(wi)) / sigma;
                int count = 200000;
                double sx = 0, sy = 0, sz = 0;
                for (int i = 0; i < count; ++i)
                {
                    ff.V3 normal = c.SampleVisible(wi, rnd, false);
                    sx += normal.x;
                    sy += normal.y;
                    sz += normal.z;
                }
                double error = Math.Abs(sx / count - mx / sigma) + Math.Abs(sy / count - my / sigma) + Math.Abs(sz / count - mz / sigma);
                Console.WriteLine("MEASURE transformed vmf, cosine {0}: projected area against a quadrature {1:E2}; mean visible normal off by {2:E2}", cosine, off, error);
                EXPECT_LT(off, 3e-3);
                EXPECT_LT(error, 15 / Math.Sqrt(count) + 5e-3);
            }

            // A white furnace: mirror facets lose nothing
            surface.facet.kind = ff.FacetKind.Mirror;
            surface.facet.R = new SampledSpectrum(1f);
            surface.maxWalk = 256;
            FacetForgeBxDF forge = new FacetForgeBxDF(surface, null, 1.5f, false);
            RNG rng = new RNG(31);
            foreach (Float cosine in new Float[] { 0.9f, 0.4f })
            {
                Vector3f wo = new Vector3f(0.6f * SafeSqrt(1 - cosine * cosine), -0.8f * SafeSqrt(1 - cosine * cosine), cosine);
                double e;
                double albedo = Albedo(forge, wo, 60000, ref rng, out e);
                Console.WriteLine("MEASURE transformed vmf, cosine {0}: white furnace {1:F4} (+- {2:F4})", cosine, albedo, e);
                EXPECT_LT(Math.Abs(albedo - 1), 5 * e + 3e-3);
            }
        }

        // The two samplers of the visible normals of a vMF: the mean normal against a quadrature, the time, and the samples that
        // ran out of proposals (which return the direction itself: a bias)
        [TEST("TransformedDistribution", "VmfLobeSampler")]
        public static void VmfLobeSampler()
        {
            foreach (double alpha in new double[] { 0.5, 0.2, 0.05, 0.02 })
            {
                foreach (double cosine in new double[] { 0.9, 0.3, 0.02, -0.05 })
                {
                    double sine = Math.Sqrt(1 - cosine * cosine);
                    ff.V3 wi = new ff.V3(sine * Math.Cos(2.1), sine * Math.Sin(2.1), cosine);
                    ff.NullTable table = new ff.NullTable();
                    table.Init(ff.Kind.VMF, alpha, 2);
                    ff.Comp c = new ff.Comp();
                    c.kind = ff.Kind.VMF;
                    c.tab = table;
                    // The quadrature about the lobe: polar angle alpha s^2 scaled to reach pi
                    double sigma = 0, mx = 0, my = 0, mz = 0;
                    int nt = 3000, np = 720;
                    for (int a = 0; a < nt; ++a)
                    {
                        double t = (a + 0.5) / nt, theta = Math.PI * t * t * t;
                        double sm = Math.Sin(theta), mu = Math.Cos(theta);
                        double jac = sm * 3 * Math.PI * t * t / nt * (2 * Math.PI / np);
                        for (int b = 0; b < np; ++b)
                        {
                            double phi = 2 * Math.PI * (b + 0.5) / np;
                            ff.V3 normal = new ff.V3(sm * Math.Cos(phi), sm * Math.Sin(phi), mu);
                            double weight = c.D(normal) * Math.Max(0.0, ff.Dot(wi, normal)) * jac;
                            sigma += weight;
                            mx += weight * normal.x;
                            my += weight * normal.y;
                            mz += weight * normal.z;
                        }
                    }
                    if (sigma < 1e-5)
                    {
                        continue;
                    }
                    int count = 20000;
                    double[] error = new double[2], seconds = new double[2];
                    int[] failed = new int[2];
                    for (int sampler = 0; sampler < 2; ++sampler)
                    {
                        table.lobeSampler = sampler == 1;
                        ff.Rnd rnd = new ff.Rnd(new RNG(77));
                        double sx = 0, sy = 0, sz = 0;
                        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 0; i < count; ++i)
                        {
                            ff.V3 normal = c.SampleVisible(wi, rnd, false);
                            if (normal.x == wi.x && normal.y == wi.y && normal.z == wi.z)
                            {
                                ++failed[sampler];
                            }
                            sx += normal.x;
                            sy += normal.y;
                            sz += normal.z;
                        }
                        seconds[sampler] = watch.Elapsed.TotalSeconds;
                        error[sampler] = Math.Abs(sx / count - mx / sigma) + Math.Abs(sy / count - my / sigma) + Math.Abs(sz / count - mz / sigma);
                    }
                    Console.WriteLine("MEASURE vmf alpha {0}, cosine {1}: sigma {2:E2}; disc {3:F3} s, mean normal off {4:E2}, {5} without a normal; lobe {6:F3} s, " +
                                      "off {7:E2}, {8} without a normal; {9:F0} times faster", alpha, cosine, sigma, seconds[0], error[0], failed[0], seconds[1],
                                      error[1], failed[1], seconds[0] / seconds[1]);
                    // (the spread of the normals of a lobe is near alpha)
                    EXPECT_LT(error[1], 15 * Math.Max(alpha, 0.05) / Math.Sqrt(count) + 2e-3);
                    EXPECT_EQ(failed[1], 0);
                }
            }
        }

        static ParsedParameter Number(string name, Float value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "float";
            p.name = name;
            p.AddFloat(value);
            return p;
        }

        static ParsedParameter Numbers(string name, Float[] values)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "float";
            p.name = name;
            for (int i = 0; i < values.Length; ++i)
            {
                p.AddFloat(values[i]);
            }
            return p;
        }

        static ParsedParameter Text(string name, string value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "string";
            p.name = name;
            p.AddString(value);
            return p;
        }

        static ParsedParameter Flag(string name, bool value)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = "bool";
            p.name = name;
            p.AddBool(value);
            return p;
        }

        [TEST("TransformedDistribution", "SceneSyntax")]
        public static void SceneSyntax()
        {
            Float[] first = new Float[] { 1, 0.9f, 0, 1 }, second = new Float[] { 0.7f, 0, 0.4f, 1.5f };
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            SampledSpectrum eta = GetNamedSpectrum("metal-Cu-eta").Sample(lambda), k = GetNamedSpectrum("metal-Cu-k").Sample(lambda);
            for (int parts = 1; parts <= 2; ++parts)
            {
                List<ParsedParameter> list = new List<ParsedParameter>(new ParsedParameter[] {
                    Flag("remaproughness", false), Text("distribution", "beckmann"), Number("roughness", 0.5f), Numbers("transform", first) });
                MicrofacetDistribution direct = new TransformedDistribution(new BeckmannDistribution(0.5f, 0.5f), first[0], first[1], first[2], first[3]);
                if (parts == 2)
                {
                    list.Add(Number("weight", 0.3f));
                    list.Add(Text("distribution1", "ggx"));
                    list.Add(Number("roughness1", 0.2f));
                    list.Add(Number("weight1", 0.7f));
                    list.Add(Numbers("transform1", second));
                    direct = new MixtureDistribution(new MicrofacetDistribution[] {
                        direct, new TransformedDistribution(new TrowbridgeReitzDistribution(0.2f, 0.2f), second[0], second[1], second[2], second[3]) },
                        new Float[] { 0.3f, 0.7f });
                }
                TextureParameterDictionary d = new TextureParameterDictionary(new ParameterDictionary(list, RGBColorSpace.sRGB), new NamedTextures());
                ConductorMaterial material = ConductorMaterial.Create(d, null, new FileLoc(), new Allocator());
                ConductorBxDF built = material.GetBxDF(new UniversalTextureEvaluator(), new MaterialEvalContext(), ref lambda);
                int count;
                double worst = Worst(built, new ConductorBxDF(direct, eta, k), 17, out count);
                Console.WriteLine("MEASURE scene syntax, {0} transformed distribution(s): the conductor of the parameters against the one built directly {1:E2}", parts,
                                  worst);
                EXPECT_GT(count, 300);
                EXPECT_LT(worst, 1e-4);
            }
        }
    }
}
