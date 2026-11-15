// (not in pbrt-v4) exactpolygrain_test.cs
//
// ExactPolyGrainBxDF (exactpolygrain.cs), the micrograin surface traced through grains drawn as the path meets them:
//   - its first scattering, traced, is the closed form of the paper (the claim that the model of the paper is this geometry):
//     mirror and diffuse grains, two types, anisotropic grains;
//   - nothing is lost where nothing absorbs;
//   - f(wo, wi) = f(wi, wo);
//   - Sample_f and f agree about the albedo;
//   - against grains laid out on a plane and traced, beside the position-free walk of polygrain_walk.cs.

using System;
using testing;

namespace pbrt
{
    public static unsafe class ExactPolyGrainTests
    {
        static ExactPolyGrainBxDF Exact(PolyGrainBxDF paper, int order, bool closedFirst)
        {
            paper.maxOrder = 1;
            return new ExactPolyGrainBxDF(paper, order, closedFirst);
        }

        static void Mean(BxDF bxdf, Vector3f wo, Vector3f wi, int count, ref RNG rng, out double mean, out double error)
        {
            double sum = 0, squares = 0;
            for (int i = 0; i < count; ++i)
            {
                Vector3f d = 1e-5f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                double v = bxdf.f(Normalize(wo + d), Normalize(wi - d), TransportMode.Radiance)[0];
                sum += v;
                squares += v * v;
            }
            mean = sum / count;
            error = Math.Sqrt(Math.Max(0.0, squares / count - mean * mean) / count);
        }

        [TEST("ExactPolyGrain", "FirstScatteringIsThePaper")]
        public static void FirstScatteringIsThePaper()
        {
            RNG rng = new RNG(101);
            string[] names = new string[] { "mirror grains, one type, black bulk", "diffuse grains on a diffuse bulk, two types", "mirror grains, anisotropic, grey bulk",
                                            "mirror grains, two types, grey bulk" };
            PolyGrainBxDF[] papers = new PolyGrainBxDF[] { PolyGrainWalkTests.Mirror(PolyGrainTests.One(0.5, 1.0), 0f, 1), PolyGrainWalkTests.Chalk(PolyGrainTests.Two(), 1),
                                                           PolyGrainWalkTests.Mirror(PolyGrainTests.One(0.6, 0.5), 0.5f, 1),
                                                           PolyGrainWalkTests.Mirror(PolyGrainTests.Two(), 0.5f, 1) };
            Vector3f[] a = new Vector3f[] { PolyGrainWalkTests.Direction(0.9f, 0.3f), PolyGrainWalkTests.Direction(0.6f, 1.2f), PolyGrainWalkTests.Direction(0.3f, 0.4f) };
            Vector3f[] b = new Vector3f[] { PolyGrainWalkTests.Direction(0.7f, 2.8f), PolyGrainWalkTests.Direction(0.8f, 4.0f), PolyGrainWalkTests.Direction(0.4f, 3.0f) };
            for (int c = 0; c < papers.Length; ++c)
            {
                ExactPolyGrainBxDF traced = Exact(papers[c], 1, false);
                double worst = 0, largest = 0;
                for (int p = 0; p < a.Length; ++p)
                {
                    double mean, error;
                    Mean(traced, a[p], b[p], 30000, ref rng, out mean, out error);
                    // (the diffuse term of the paper is itself an estimate, of a few samples for each evaluation)
                    double closed, closedError;
                    Mean(papers[c], a[p], b[p], 30000, ref rng, out closed, out closedError);
                    error = Math.Sqrt(error * error + closedError * closedError);
                    Console.WriteLine("MEASURE exact polygrain, first scattering, {0}, pair {1}: traced {2:F5}, the paper {3:F5} (+- {4:F5})", names[c], p, mean, closed, error);
                    worst = Math.Max(worst, Math.Abs(mean - closed) / Math.Max(error, 1e-9));
                    largest = Math.Max(largest, Math.Abs(mean - closed) / closed);
                    EXPECT_LT(Math.Abs(mean - closed), 4 * error + 1e-5, names[c]);
                }
                Console.WriteLine("MEASURE exact polygrain, first scattering, {0}: worst {1:F1} sigma, {2:P2} of f", names[c], worst, largest);
            }
        }

        [TEST("ExactPolyGrain", "NothingIsLost")]
        public static void NothingIsLost()
        {
            RNG rng = new RNG(102);
            mg.PolyGrain[] cases = new mg.PolyGrain[] { PolyGrainTests.One(0.5, 1.0), PolyGrainTests.One(0.8, 0.5), PolyGrainTests.Two() };
            for (int c = 0; c < cases.Length; ++c)
            {
                foreach (bool mirror in new bool[] { true, false })
                {
                    ExactPolyGrainBxDF traced = Exact(mirror ? PolyGrainWalkTests.Mirror(cases[c], 1f, 1) : PolyGrainWalkTests.Chalk(cases[c], 1), 400, true);
                    foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                    {
                        double albedo, error;
                        CombineTests.Albedo(traced, PolyGrainWalkTests.Direction(cosine, 0.7f), 40000, ref rng, out albedo, out error);
                        Console.WriteLine("MEASURE exact polygrain white furnace, case {0}, {1}, cosine {2}: {3:F4} +- {4:F4}", c, mirror ? "mirror grains" : "white grains",
                                          cosine, albedo, error);
                        EXPECT_GT(albedo, 0.998 - 4 * error);
                        EXPECT_LT(albedo, 1 + 1e-4);
                    }
                }
            }
        }

        [TEST("ExactPolyGrain", "Reciprocal")]
        public static void Reciprocal()
        {
            RNG rng = new RNG(103);
            PolyGrainBxDF[] papers = new PolyGrainBxDF[] { PolyGrainWalkTests.Mirror(PolyGrainTests.Two(), 1f, 1), PolyGrainWalkTests.Chalk(PolyGrainTests.Two(), 1),
                                                           PolyGrainWalkTests.Mirror(PolyGrainTests.One(0.8, 0.5), 0.5f, 1) };
            string[] names = new string[] { "mirror grains on a white bulk", "white grains on a white bulk", "anisotropic mirror grains on a grey bulk" };
            Vector3f[] a = new Vector3f[] { PolyGrainWalkTests.Direction(0.95f, 0.2f), PolyGrainWalkTests.Direction(0.8f, 1.0f), PolyGrainWalkTests.Direction(0.9f, 4.0f),
                                            PolyGrainWalkTests.Direction(0.3f, 0.5f), PolyGrainWalkTests.Direction(0.6f, 2.0f) };
            Vector3f[] b = new Vector3f[] { PolyGrainWalkTests.Direction(0.25f, 3.0f), PolyGrainWalkTests.Direction(0.5f, 4.2f), PolyGrainWalkTests.Direction(0.15f, 0.9f),
                                            PolyGrainWalkTests.Direction(0.35f, 3.5f), PolyGrainWalkTests.Direction(0.7f, 5.5f) };
            for (int c = 0; c < papers.Length; ++c)
            {
                ExactPolyGrainBxDF traced = Exact(papers[c], 32, true);
                PolyGrainBxDF first = papers[c];
                double worstSigma = 0, worst = 0, mean = 0;
                for (int p = 0; p < a.Length; ++p)
                {
                    double x, ex, y, ey;
                    Mean(traced, a[p], b[p], 20000, ref rng, out x, out ex);
                    Mean(traced, b[p], a[p], 20000, ref rng, out y, out ey);
                    double error = Math.Sqrt(ex * ex + ey * ey);
                    double once = first.f(a[p], b[p], TransportMode.Radiance)[0];
                    Console.WriteLine("MEASURE exact polygrain, {0}, pair {1}: f {2:F5} one way, {3:F5} the other (+- {4:F5}); one scattering {5:F5}", names[c], p, x, y, error,
                                      once);
                    worstSigma = Math.Max(worstSigma, Math.Abs(x - y) / error);
                    worst = Math.Max(worst, Math.Abs(x - y) / Math.Max(x, y));
                    mean += Math.Abs(x - y) / Math.Max(x, y) / a.Length;
                    EXPECT_LT(Math.Abs(x - y), 4 * error, names[c]);
                }
                Console.WriteLine("MEASURE exact polygrain, {0}: f(wo, wi) against f(wi, wo), worst {1:F1} sigma, {2:P1} of f (mean {3:P1})", names[c], worstSigma, worst, mean);
            }
        }

        // The connection of a point to a direction by a mirror reflection on a grain that is already known: the direction that
        // Newton's method finds and the solid angles it relates, against rays traced to that grain
        [TEST("ExactPolyGrain", "ConnectionByAKnownMirror")]
        public static void ConnectionByAKnownMirror()
        {
            RNG rng = new RNG(106);
            foreach (double beta in new double[] { 1.0, 0.5 })
            {
                ExactPolyGrainBxDF traced = Exact(PolyGrainWalkTests.Mirror(PolyGrainTests.One(0.5, beta), 0.5f, 1), 8, true);
                Vector3f[] to = new Vector3f[] { PolyGrainWalkTests.Direction(0.9f, 0.3f), PolyGrainWalkTests.Direction(0.5f, 2.0f), PolyGrainWalkTests.Direction(0.3f, 4.0f),
                                                 PolyGrainWalkTests.Direction(0.7f, 5.5f) };
                double[][] centres = new double[][] { new double[] { 1.6, 0.3 }, new double[] { -1.2, 1.5 }, new double[] { 2.5, -1.0 }, new double[] { 0.4, -1.9 } };
                for (int c = 0; c < to.Length; ++c)
                {
                    double counted;
                    double formula = traced.KnownMirrorDensity(0, centres[c][0], centres[c][1], new Vector3f(0, 0, 0), to[c], 4000000, 0.04, ref rng, out counted);
                    Console.WriteLine("MEASURE exact polygrain, mirror on a known grain (beta {0}), case {1}: connection {2:F5}, traced {3:F5}", beta, c, formula, counted);
                    if (formula < 0)
                    {
                        EXPECT_LT(counted, 1e-3);
                    }
                    else
                    {
                        EXPECT_LT(Math.Abs(formula - counted), 0.05 * formula + 3e-4);
                    }
                }
            }
        }

        [TEST("ExactPolyGrain", "SamplesAgreeWithF")]
        public static void SamplesAgreeWithF()
        {
            RNG rng = new RNG(104);
            ExactPolyGrainBxDF traced = Exact(PolyGrainTests.Gold(PolyGrainTests.Two(), 0.5f, 4), 32, true);
            foreach (Float cosine in new Float[] { 0.9f, 0.4f })
            {
                Vector3f wo = PolyGrainWalkTests.Direction(cosine, 0.4f);
                double sampled, es;
                CombineTests.Albedo(traced, wo, 60000, ref rng, out sampled, out es);
                // the share of the light that leaves by a mirror reflection on a grain that the path already knew
                double[] shares = new double[4];
                for (int i = 0; i < 60000; ++i)
                {
                    pstd.optional<BSDFSample> bs = traced.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Radiance,
                                                                   BxDFReflTransFlags.All);
                    if ((bool)bs)
                    {
                        shares[traced.lastEnd] += bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / 60000;
                    }
                }
                Console.WriteLine("MEASURE exact polygrain, cosine {0}: of the albedo, {1:F4} leaves by a mirror on a known grain after a wide scattering, {2:F4} after a " +
                                  "mirror on a new grain, {3:F4} after a mirror on a known grain", cosine, shares[1], shares[2], shares[3]);
                double sum = 0, squares = 0, density = 0;
                int count = 60000;
                for (int i = 0; i < count; ++i)
                {
                    Vector3f wi = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    double v = traced.f(wo, wi, TransportMode.Radiance)[0] * Math.PI;
                    sum += v;
                    squares += v * v;
                    // (PDF() mixes its estimate with a uniform density, 0.9 to 0.1: taken out again here)
                    density += (traced.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All) - 0.1 / (4 * Math.PI)) / 0.9 * Math.PI / wi.z / count;
                }
                double evaluated = sum / count, ee = Math.Sqrt(Math.Max(0.0, squares / count - evaluated * evaluated) / count);
                Console.WriteLine("MEASURE exact polygrain, gold grains on a grey bulk, cosine {0}: albedo by samples {1:F4} +- {2:F4}, by evaluation {3:F4} +- {4:F4}; the " +
                                  "density integrates to {5:F4}", cosine, sampled, es, evaluated, ee, density);
                // (what the evaluation has no connection for is the third share)
                EXPECT_LT(Math.Abs(sampled - evaluated), 4 * Math.Sqrt(es * es + ee * ee));
                EXPECT_LT(Math.Abs(density - 1), 0.03);
            }
        }

        [TEST("ExactPolyGrain", "Solvers")]
        public static void Solvers()
        {
            RNG rng = new RNG(109);
            ExactPolyGrainBxDF traced = Exact(PolyGrainWalkTests.Mirror(PolyGrainTests.One(0.5, 1.0), 0f, 1), 32, true);
            int single, singleFound, twice, twiceFound;
            traced.SolverRecovers(0, 400000, ref rng, out single, out singleFound, out twice, out twiceFound);
            Console.WriteLine("MEASURE solvers: one mirror {0} of {1}, two mirrors {2} of {3}", singleFound, single, twiceFound, twice);
            CHECK_GE(singleFound, single - 2);
            CHECK_GE(twiceFound, (int)(0.99 * twice));
        }

        [TEST("ExactPolyGrain", "Gap")]
        public static void Gap()
        {
            RNG rng = new RNG(107);
            string[] names = new string[] { "chalk two types", "mirror one type black bulk", "mirror one type white bulk", "mirror dense black bulk" };
            PolyGrainBxDF[] papers = new PolyGrainBxDF[] { PolyGrainWalkTests.Chalk(PolyGrainTests.Two(), 1), PolyGrainWalkTests.Mirror(PolyGrainTests.One(0.5, 1.0), 0f, 1),
                                                           PolyGrainWalkTests.Mirror(PolyGrainTests.One(0.5, 1.0), 1f, 1), PolyGrainWalkTests.Mirror(PolyGrainTests.One(0.85, 1.0), 0f, 1) };
            for (int c = 0; c < papers.Length; ++c)
            {
                ExactPolyGrainBxDF traced = Exact(papers[c], 32, true);
                Vector3f wo = PolyGrainWalkTests.Direction(0.8f, 0.4f);
                double sampled, es;
                CombineTests.Albedo(traced, wo, 60000, ref rng, out sampled, out es);
                double[] shares = new double[4];
                for (int i = 0; i < 60000; ++i)
                {
                    pstd.optional<BSDFSample> bs = traced.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Radiance,
                                                                   BxDFReflTransFlags.All);
                    if ((bool)bs)
                    {
                        shares[traced.lastEnd] += bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / 60000;
                    }
                }
                double sum = 0, squares = 0;
                int count = 60000;
                traced.captured[0] = traced.captured[1] = traced.captured[2] = 0;
                for (int i = 0; i < count; ++i)
                {
                    Vector3f wi = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    double v = traced.f(wo, wi, TransportMode.Radiance)[0] * Math.PI;
                    sum += v;
                    squares += v * v;
                }
                double evaluated = sum / count, ee = Math.Sqrt(Math.Max(0.0, squares / count - evaluated * evaluated) / count);
                double wide = traced.captured[0] * Math.PI / count / papers[c].Samples(), fresh = traced.captured[1] * Math.PI / count / papers[c].Samples();
                double chained = traced.captured[2] * Math.PI / count / papers[c].Samples();
                Console.WriteLine("MEASURE gap, {0}: sampled {1:F4} +- {2:F4}, evaluated {3:F4} +- {4:F4}; revisits {5:F4} {6:F4} {7:F4}", names[c], sampled, es, evaluated, ee,
                                  shares[1], shares[2], shares[3]);
                Console.WriteLine("MEASURE   connections found: after wide scattering {0:F4} (sampled {1:F4}), after a drawn mirror {2:F4} (sampled {3:F4}), chains of known mirrors {4:F4} (sampled {5:F4})", wide, shares[1], fresh, shares[2], chained, shares[3]);
                // evaluation lacks nothing
                CHECK_LT(Math.Abs(sampled - evaluated), 4 * Math.Sqrt(es * es + ee * ee));
                CHECK_LT(Math.Abs(chained - shares[3]), 0.3 * shares[3] + 5e-4);
                CHECK_LT(Math.Abs(wide - shares[1]), 0.2 * shares[1] + 1e-3);
                CHECK_LT(Math.Abs(fresh - shares[2]), 0.2 * shares[2] + 1e-3);
            }
        }

        [TEST("ExactPolyGrain", "ChainsOfKnownMirrors")]
        public static void ChainsOfKnownMirrors()
        {
            // the share of the albedo that leaves by two or more mirrors on grains already known: by samples, and as evaluation finds it
            RNG rng = new RNG(113);
            PolyGrainBxDF paper = PolyGrainWalkTests.Mirror(PolyGrainTests.One(0.85, 1.0), 0f, 1);
            ExactPolyGrainBxDF traced = Exact(paper, 32, true);
            Vector3f wo = PolyGrainWalkTests.Direction(0.8f, 0.4f);
            int count = 1500000;
            double share = 0, shareSquares = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<BSDFSample> bs = traced.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Radiance,
                                                               BxDFReflTransFlags.All);
                if ((bool)bs && traced.lastEnd == 3)
                {
                    double v = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                    share += v;
                    shareSquares += v * v;
                }
            }
            share /= count;
            double shareError = Math.Sqrt(Math.Max(0.0, shareSquares / count - share * share) / count);
            double sum = 0, squares = 0;
            for (int i = 0; i < count; ++i)
            {
                Vector3f wi = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                traced.captured[2] = 0;
                traced.f(wo, wi, TransportMode.Radiance);
                double v = traced.captured[2] * Math.PI / paper.Samples();
                sum += v;
                squares += v * v;
            }
            double found = sum / count, foundError = Math.Sqrt(Math.Max(0.0, squares / count - found * found) / count);
            Console.WriteLine("MEASURE chains of known mirrors, dense mirror grains: share by samples {0:F5} +- {1:F5}, found by evaluation {2:F5} +- {3:F5}", share, shareError, found, foundError);
            CHECK_LT(Math.Abs(share - found), 4 * Math.Sqrt(shareError * shareError + foundError * foundError));
        }

        [TEST("ExactPolyGrain", "AgainstGrainsLaidOut")]
        public static void AgainstGrainsLaidOut()
        {
            RNG rng = new RNG(105);
            double worstExact = 0, worstWalk = 0, worstPaper = 0;
            foreach (double tau in new double[] { 0.3, 0.6, 0.85 })
            {
                PolyGrainWalkTests.Layout layout = new PolyGrainWalkTests.Layout(tau, 60, 1234);
                mg.PolyGrain d = PolyGrainTests.One(tau, 1.0);
                foreach (bool mirror in new bool[] { true, false })
                {
                    foreach (Float baseAlbedo in new Float[] { 0f, 0.8f })
                    {
                        PolyGrainBxDF paper = mirror ? PolyGrainWalkTests.Mirror(d, baseAlbedo, 1) : PolyGrainWalkTests.Chalk(d, 1);
                        PolyGrainBxDF walk = mirror ? PolyGrainWalkTests.Mirror(d, baseAlbedo, 64) : PolyGrainWalkTests.Chalk(d, 64);
                        PolyGrainBxDF forExact = mirror ? PolyGrainWalkTests.Mirror(d, baseAlbedo, 1) : PolyGrainWalkTests.Chalk(d, 1);
                        if (!mirror)
                        {
                            paper.SetBulk(new DiffuseBxDF(new SampledSpectrum(baseAlbedo)));
                            walk.SetBulk(new DiffuseBxDF(new SampledSpectrum(baseAlbedo)));
                            forExact.SetBulk(new DiffuseBxDF(new SampledSpectrum(baseAlbedo)));
                        }
                        walk.reciprocal = false;
                        ExactPolyGrainBxDF traced = new ExactPolyGrainBxDF(forExact, 64, true);
                        foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                        {
                            Vector3f wo = PolyGrainWalkTests.Direction(cosine, 0.7f);
                            double all, once, a0, e0, a1, e1, a2, e2;
                            PolyGrainWalkTests.Simulate(layout, wo, mirror, baseAlbedo, 150000, ref rng, out all, out once);
                            CombineTests.Albedo(paper, wo, 80000, ref rng, out a0, out e0);
                            CombineTests.Albedo(walk, wo, 80000, ref rng, out a1, out e1);
                            CombineTests.Albedo(traced, wo, 80000, ref rng, out a2, out e2);
                            Console.WriteLine("MEASURE grains laid out, tau0 {0}, {1} grains, base {2}, cosine {3}: laid out {4:F4}; exact {5:F4} +- {6:F4}; position-free " +
                                              "walk {7:F4}; the paper (one scattering) {8:F4}", tau, mirror ? "mirror" : "white", baseAlbedo, cosine, all, a2, e2, a1, a0);
                            worstExact = Math.Max(worstExact, Math.Abs(all - a2));
                            worstWalk = Math.Max(worstWalk, Math.Abs(all - a1));
                            worstPaper = Math.Max(worstPaper, Math.Abs(all - a0));
                            EXPECT_LT(Math.Abs(all - a2), 0.012 + 4 * e2);
                        }
                    }
                }
            }
            Console.WriteLine("MEASURE grains laid out: furthest from the traced layout, exact {0:F4}, position-free walk {1:F4}, the paper {2:F4}", worstExact, worstWalk,
                              worstPaper);
        }
    }
}
