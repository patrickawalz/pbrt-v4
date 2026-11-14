// (not in pbrt-v4) facetforge_test.cs
//
// The direct port of Facet Forge (util/facetforge.cs, facetforge.cs):
//   - its distributions of normals are those of Magi: the same D and the same projected area for Beckmann, GGX and Student-T, and
//     the null Student-T, which is the same distribution reached by null collisions, has the same projected area;
//   - its visible normals are sampled with their density (the moments of the samples against a quadrature of the density);
//   - with one reflection its conductor and the reflection of its dielectric are the lobes of pbrt on the same distribution;
//   - with all the reflections its albedo is that of the walk of Heitz et al. (MultiScatterConductorBxDF, ...DielectricBxDF),
//     and a microsurface that absorbs nothing returns all the light;
//   - strata with the same distribution are one distribution, and the closed form of the first reflection is what the walk
//     estimates;
//   - each configuration is reciprocal, conserves energy and samples what it evaluates.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class FacetForgeTests
    {
        static ff.Surface Heightfield(ff.Kind kind, double ax, double ay, double gamma)
        {
            ff.Surface s = new ff.Surface();
            s.ndf.n = 1;
            s.ndf.c[0].InitHeightfield(kind, ax, ay, gamma);
            s.ndf.c[0].w = 1;
            return s;
        }

        static ff.Surface Blend(bool layered, ff.Kind kind0, double a0, double w0, ff.Kind kind1, double a1, double w1)
        {
            ff.Surface s = new ff.Surface();
            s.ndf.n = 2;
            s.ndf.c[0].InitHeightfield(kind0, a0, a0, 3);
            s.ndf.c[0].w = w0;
            s.ndf.c[1].InitHeightfield(kind1, a1, a1, 3);
            s.ndf.c[1].w = w1;
            s.ndf.Normalize();
            s.ndf.layered = layered;
            return s;
        }

        static ff.Surface Conductor(ff.Surface s, Float eta, Float k)
        {
            s.facet.kind = ff.FacetKind.Conductor;
            s.facet.eta = new SampledSpectrum(eta);
            s.facet.k = new SampledSpectrum(k);
            return s;
        }

        static ff.Surface Dielectric(ff.Surface s)
        {
            s.facet.kind = ff.FacetKind.Dielectric;
            return s;
        }

        static ff.Surface Reflector(ff.Surface s, ff.FacetKind kind, Float reflectance)
        {
            s.facet.kind = kind;
            s.facet.R = new SampledSpectrum(reflectance);
            return s;
        }

        static MicrofacetDistribution Magi(ff.Kind kind, Float ax, Float ay, Float gamma)
        {
            if (kind == ff.Kind.Beckmann)
            {
                return new BeckmannDistribution(ax, ay);
            }
            if (kind == ff.Kind.GGX)
            {
                return new TrowbridgeReitzDistribution(ax, ay);
            }
            return new StudentTDistribution(ax, ay, gamma);
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

        static Vector3f Direction(Float cosine, Float phi)
        {
            Float sine = SafeSqrt(1 - cosine * cosine);
            return new Vector3f(sine * (Float)Math.Cos(phi), sine * (Float)Math.Sin(phi), cosine);
        }

        [TEST("FacetForge", "DistributionsAreThoseOfMagi")]
        public static void DistributionsAreThoseOfMagi()
        {
            RNG rng = new RNG(5);
            foreach (ff.Kind kind in new ff.Kind[] { ff.Kind.Beckmann, ff.Kind.GGX, ff.Kind.StudentT })
            {
                double worstD = 0, worstSigma = 0;
                foreach (Float gamma in new Float[] { 1.8f, 3f, 8f })
                {
                    Float ax = 0.15f + 0.6f * rng.Uniform<Float>(), ay = 0.15f + 0.6f * rng.Uniform<Float>();
                    ff.Comp c = Heightfield(kind, ax, ay, gamma).ndf.c[0];
                    MicrofacetDistribution d = Magi(kind, ax, ay, gamma);
                    for (int i = 0; i < 2000; ++i)
                    {
                        Vector3f wm = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        if (wm.z < 0.05f)
                        {
                            continue;
                        }
                        double mine = c.D(new ff.V3(wm.x, wm.y, wm.z)), theirs = d.D(wm);
                        if (Math.Max(mine, theirs) > 1e-4)
                        {
                            worstD = Math.Max(worstD, Math.Abs(mine - theirs) / Math.Max(mine, theirs));
                        }
                        Vector3f w = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        if (Math.Abs(w.z) > 0.999f)
                        {
                            continue;
                        }
                        double sigma = c.Sigma(new ff.V3(w.x, w.y, w.z)), sigmaTheirs = d.Sigma(w);
                        worstSigma = Math.Max(worstSigma, Math.Abs(sigma - sigmaTheirs) / Math.Max(1e-2, Math.Max(sigma, sigmaTheirs)));
                    }
                }
                Console.WriteLine("MEASURE facet forge {0} against Magi: D {1:E2}, projected area {2:E2}", kind, worstD, worstSigma);
                EXPECT_LT(worstD, 2e-4, kind + ": D");
                EXPECT_LT(worstSigma, 2e-4, kind + ": projected area");
            }
        }

        // The null Student-T is the Student-T heightfield reached by null collisions: the table of its projected area is the
        // closed form. The vMF distribution covers the sphere: its table against a quadrature of D (w . m)+ over the sphere.
        [TEST("FacetForge", "NullDistributions")]
        public static void NullDistributions()
        {
            ff.NullTable student = new ff.NullTable();
            student.Init(ff.Kind.NullStudentT, 0.5, 3);
            ff.Comp closed = Heightfield(ff.Kind.StudentT, 0.5, 0.5, 3).ndf.c[0];
            double worst = 0;
            for (int i = 0; i <= 200; ++i)
            {
                double u = -0.995 + 1.99 * i / 200.0, s = Math.Sqrt(1 - u * u);
                worst = Math.Max(worst, Math.Abs(student.Sigma(u) - closed.Sigma(new ff.V3(s, 0, u))));
            }
            Console.WriteLine("MEASURE facet forge null Student-T: projected area against the closed form {0:E2}", worst);
            EXPECT_LT(worst, 2e-3);

            ff.NullTable vmf = new ff.NullTable();
            vmf.Init(ff.Kind.VMF, 0.7, 2);
            double worstVmf = 0;
            foreach (double u in new double[] { -0.9, -0.3, 0.0, 0.4, 0.95 })
            {
                double s = Math.Sqrt(1 - u * u), sum = 0;
                int nt = 400, np = 400;
                for (int a = 0; a < nt; ++a)
                {
                    double mu = -1 + 2 * (a + 0.5) / nt, sm = Math.Sqrt(1 - mu * mu);
                    for (int b = 0; b < np; ++b)
                    {
                        double phi = 2 * Math.PI * (b + 0.5) / np;
                        double dot = s * sm * Math.Cos(phi) + u * mu;
                        sum += vmf.Dphys(mu) * Math.Max(0.0, dot);
                    }
                }
                sum *= 2.0 / nt * 2 * Math.PI / np;
                worstVmf = Math.Max(worstVmf, Math.Abs(sum - vmf.Sigma(u)) / sum);
            }
            Console.WriteLine("MEASURE facet forge vMF: projected area against a quadrature {0:E2}", worstVmf);
            EXPECT_LT(worstVmf, 2e-3);
        }

        // The mean normal of the samples against the mean of the density of the visible normals, for each sampler
        [TEST("FacetForge", "VisibleNormals")]
        public static void VisibleNormals()
        {
            ff.Rnd rnd = new ff.Rnd(new RNG(71));
            foreach (ff.Kind kind in new ff.Kind[] { ff.Kind.Beckmann, ff.Kind.GGX, ff.Kind.StudentT })
            {
                ff.Surface surface = Heightfield(kind, 0.3, 0.6, 3);
                ff.Comp c = surface.ndf.c[0];
                foreach (double cosine in new double[] { 0.95, 0.5, 0.15 })
                {
                    double sine = Math.Sqrt(1 - cosine * cosine);
                    ff.V3 wi = new ff.V3(sine * Math.Cos(0.7), sine * Math.Sin(0.7), cosine);
                    // The quadrature, in the slopes
                    double norm = 0, mx = 0, my = 0, mz = 0;
                    int n = 1200;
                    for (int a = 0; a < n; ++a)
                    {
                        for (int b = 0; b < n; ++b)
                        {
                            // (slopes by the tangent of a uniform angle: the tails of the Student-T are long)
                            double ta = Math.PI * ((a + 0.5) / n - 0.5), tb = Math.PI * ((b + 0.5) / n - 0.5);
                            double px = Math.Tan(ta), py = Math.Tan(tb);
                            double jac = (1 + px * px) * (1 + py * py) * (Math.PI / n) * (Math.PI / n);
                            ff.V3 m = ff.Normalize(new ff.V3(-px, -py, 1));
                            // dw = dp dq cos^3
                            double density = Math.Max(0.0, ff.Dot(wi, m)) * c.D(m) / c.Sigma(wi) * m.z * m.z * m.z * jac;
                            norm += density;
                            mx += density * m.x;
                            my += density * m.y;
                            mz += density * m.z;
                        }
                    }
                    EXPECT_LT(Math.Abs(norm - 1), 5e-3, kind + ": the visible normals are not normalised");
                    for (int sampler = 0; sampler < 2; ++sampler)
                    {
                        int count = 200000;
                        double sx = 0, sy = 0, sz = 0;
                        for (int i = 0; i < count; ++i)
                        {
                            ff.V3 m = sampler == 0 ? c.SampleVisible(wi, rnd, false) : c.SampleVisibleU(wi, rnd.U(), rnd.U());
                            sx += m.x;
                            sy += m.y;
                            sz += m.z;
                        }
                        double error = Math.Abs(sx / count - mx) + Math.Abs(sy / count - my) + Math.Abs(sz / count - mz);
                        if (cosine == 0.5)
                        {
                            Console.WriteLine("MEASURE facet forge visible normals: {0}, sampler {1}, cosine {2}: mean normal off by {3:E2}", kind, sampler, cosine,
                                              error);
                        }
                        // (the spread of a component of a unit vector is below 1: 5 standard errors of the three, and the quadrature)
                        EXPECT_LT(error, 15 / Math.Sqrt(count) + 5e-3, kind + ": visible normals, sampler " + sampler + ", cosine " + cosine);
                    }
                }
            }
        }

        [TEST("FacetForge", "OneReflectionIsTheLobeOfPbrt")]
        public static void OneReflectionIsTheLobeOfPbrt()
        {
            RNG rng = new RNG(29);
            foreach (ff.Kind kind in new ff.Kind[] { ff.Kind.Beckmann, ff.Kind.GGX, ff.Kind.StudentT })
            {
                ff.Surface conductor = Conductor(Heightfield(kind, 0.25, 0.45, 3), 0.2f, 3.5f);
                conductor.maxWalk = 1;
                FacetForgeBxDF mine = new FacetForgeBxDF(conductor, null, 1.5f, false);
                ConductorBxDF theirs = new ConductorBxDF(Magi(kind, 0.25f, 0.45f, 3), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f));
                ff.Surface glass = Dielectric(Heightfield(kind, 0.25, 0.45, 3));
                glass.maxWalk = 1;
                FacetForgeBxDF mineGlass = new FacetForgeBxDF(glass, null, 1.5f, true);
                DielectricBxDF theirGlass = new DielectricBxDF(1.5f, Magi(kind, 0.25f, 0.45f, 3));
                double worst = 0, worstGlass = 0, transmission = 0, transmissionTheirs = 0;
                for (int i = 0; i < 4000; ++i)
                {
                    Vector3f wo = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f wi = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (wo.z < 0.03f || wi.z < 0.03f)
                    {
                        continue;
                    }
                    double a = mine.f(wo, wi, TransportMode.Radiance)[0], b = theirs.f(wo, wi, TransportMode.Radiance)[0];
                    if (Math.Max(a, b) > 1e-3)
                    {
                        worst = Math.Max(worst, Math.Abs(a - b) / Math.Max(a, b));
                    }
                    double c = mineGlass.f(wo, wi, TransportMode.Radiance)[0], d = theirGlass.f(wo, wi, TransportMode.Radiance)[0];
                    if (Math.Max(c, d) > 1e-3)
                    {
                        worstGlass = Math.Max(worstGlass, Math.Abs(c - d) / Math.Max(c, d));
                    }
                    transmission += mineGlass.f(wo, -wi, TransportMode.Radiance)[0] * wi.z;
                    transmissionTheirs += theirGlass.f(wo, -wi, TransportMode.Radiance)[0] * wi.z;
                }
                Console.WriteLine("MEASURE facet forge one reflection, {0}: conductor against pbrt {1:E2}, dielectric reflection {2:E2}; transmitted share " +
                                  "{3:F4} of that of pbrt (another shadowing: the beta function of the Smith model through the surface)",
                                  kind, worst, worstGlass, transmission / transmissionTheirs);
                EXPECT_LT(worst, 2e-3, kind + ": conductor");
                EXPECT_LT(worstGlass, 2e-3, kind + ": dielectric reflection");
            }
        }

        [TEST("FacetForge", "AllReflectionsAgainstTheWalkOfHeitz")]
        public static void AllReflectionsAgainstTheWalkOfHeitz()
        {
            RNG rng = new RNG(41);
            foreach (ff.Kind kind in new ff.Kind[] { ff.Kind.Beckmann, ff.Kind.GGX })
            {
                FacetForgeBxDF mine = new FacetForgeBxDF(Conductor(Heightfield(kind, 0.6, 0.6, 2), 0.2f, 3.5f), null, 1.5f, false);
                MultiScatterConductorBxDF theirs = new MultiScatterConductorBxDF(Magi(kind, 0.6f, 0.6f, 2), new MicrosurfaceHeightUniform(), new SampledSpectrum(0.2f),
                                                                                 new SampledSpectrum(3.5f), 100);
                FacetForgeBxDF mineGlass = new FacetForgeBxDF(Dielectric(Heightfield(kind, 0.6, 0.6, 2)), null, 1.5f, true);
                MultiScatterDielectricBxDF theirGlass = new MultiScatterDielectricBxDF(Magi(kind, 0.6f, 0.6f, 2), new MicrosurfaceHeightUniform(), 1.5f, 100);
                FacetForgeBxDF mirror = new FacetForgeBxDF(Reflector(Heightfield(kind, 0.6, 0.6, 2), ff.FacetKind.Mirror, 1), null, 1.5f, false);
                foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                {
                    Vector3f wo = Direction(cosine, 0.3f);
                    double e0, e1, e2, e3, e4;
                    double a = Albedo(mine, wo, 100000, ref rng, out e0), b = Albedo(theirs, wo, 100000, ref rng, out e1);
                    double c = Albedo(mineGlass, wo, 100000, ref rng, out e2), d = Albedo(theirGlass, wo, 100000, ref rng, out e3);
                    double white = Albedo(mirror, wo, 100000, ref rng, out e4);
                    Console.WriteLine("MEASURE facet forge all reflections, {0}, cosine {1}: conductor {2:F4} against {3:F4} (+- {4:F4}), dielectric {5:F4} against " +
                                      "{6:F4}, mirror facets {7:F5}", kind, cosine, a, b, Math.Sqrt(e0 * e0 + e1 * e1), c, d, white);
                    EXPECT_LT(Math.Abs(a - b), 5 * Math.Sqrt(e0 * e0 + e1 * e1) + 2e-3, kind + ": conductor, cosine " + cosine);
                    // (a dielectric absorbs nothing: both walks return weights of one, and lose only the walks they cut)
                    EXPECT_LT(Math.Abs(c - d), 5e-3, kind + ": dielectric, cosine " + cosine);
                    EXPECT_GT(c, 0.995, kind + ": the dielectric loses energy");
                    EXPECT_LT(c, 1 + 1e-6);
                    EXPECT_GT(white, 0.995, kind + ": mirror facets lose energy");
                    EXPECT_LT(white, 1 + 1e-6);
                }
            }
        }

        // Strata with the same distribution are that distribution, whatever their shares: the closed form of the first
        // reflection and of the first refraction, to rounding
        [TEST("FacetForge", "EqualStrataAreOneDistribution")]
        public static void EqualStrataAreOneDistribution()
        {
            RNG rng = new RNG(13);
            foreach (bool glass in new bool[] { false, true })
            {
                ff.Surface one = Heightfield(ff.Kind.StudentT, 0.4, 0.4, 3);
                ff.Surface strata = Blend(true, ff.Kind.StudentT, 0.4, 0.3, ff.Kind.StudentT, 0.4, 0.7);
                ff.Surface mixture = Blend(false, ff.Kind.StudentT, 0.4, 0.3, ff.Kind.StudentT, 0.4, 0.7);
                if (glass)
                {
                    Dielectric(one);
                    Dielectric(strata);
                    Dielectric(mixture);
                }
                else
                {
                    Conductor(one, 0.2f, 3.5f);
                    Conductor(strata, 0.2f, 3.5f);
                    Conductor(mixture, 0.2f, 3.5f);
                }
                one.maxWalk = strata.maxWalk = mixture.maxWalk = 1;
                FacetForgeBxDF a = new FacetForgeBxDF(one, null, 1.5f, glass), b = new FacetForgeBxDF(strata, null, 1.5f, glass),
                               c = new FacetForgeBxDF(mixture, null, 1.5f, glass);
                double worstStrata = 0, worstMixture = 0;
                for (int i = 0; i < 3000; ++i)
                {
                    Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (Math.Abs(wo.z) < 0.05f || Math.Abs(wi.z) < 0.05f)
                    {
                        continue;
                    }
                    double x = a.f(wo, wi, TransportMode.Radiance)[0], y = b.f(wo, wi, TransportMode.Radiance)[0], z = c.f(wo, wi, TransportMode.Radiance)[0];
                    if (x > 1e-3)
                    {
                        worstStrata = Math.Max(worstStrata, Math.Abs(x - y) / x);
                        worstMixture = Math.Max(worstMixture, Math.Abs(x - z) / x);
                    }
                }
                Console.WriteLine("MEASURE facet forge equal components, {0}: strata against one distribution {1:E2}, mixture {2:E2}", glass ? "dielectric" : "conductor",
                                  worstStrata, worstMixture);
                EXPECT_LT(worstStrata, 1e-3);
                EXPECT_LT(worstMixture, 1e-3);
            }
        }

        // The closed form of the first reflection is the mean of what the walk estimates for it
        [TEST("FacetForge", "ClosedFormOfTheFirstReflection")]
        public static void ClosedFormOfTheFirstReflection()
        {
            foreach (bool glass in new bool[] { false, true })
            {
                ff.Surface closed = Blend(true, ff.Kind.GGX, 0.6, 0.4, ff.Kind.Beckmann, 0.15, 0.6);
                if (glass)
                {
                    Dielectric(closed);
                }
                else
                {
                    Conductor(closed, 0.2f, 3.5f);
                }
                closed.maxWalk = 1;
                ff.Surface walked = closed.Clone();
                walked.analyticSingle = false;
                FacetForgeBxDF a = new FacetForgeBxDF(closed, null, 1.5f, glass), b = new FacetForgeBxDF(walked, null, 1.5f, glass);
                RNG rng = new RNG(97);
                double worst = 0;
                Vector3f[] wos = new Vector3f[] { Direction(0.8f, 0.2f), Direction(0.35f, 2.0f) };
                foreach (Vector3f wo in wos)
                {
                    // (the third: near the direction a flat interface refracts wo to)
                    Vector3f through = new Vector3f(-wo.x / 1.5f + 0.1f, -wo.y / 1.5f - 0.05f, 0);
                    through.z = -SafeSqrt(1 - through.x * through.x - through.y * through.y);
                    Vector3f[] wis = new Vector3f[] { Direction(0.6f, 3.0f), Direction(0.9f, 4.5f), through };
                    foreach (Vector3f wi in wis)
                    {
                        if (!glass && wi.z < 0)
                        {
                            continue;
                        }
                        double exact = a.f(wo, wi, TransportMode.Importance)[0];
                        double sum = 0, squares = 0;
                        int count = 40000;
                        for (int i = 0; i < count; ++i)
                        {
                            Vector3f d = 1e-5f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                            double value = b.f(Normalize(wo + d), Normalize(wi - d), TransportMode.Importance)[0];
                            sum += value;
                            squares += value * value;
                        }
                        double mean = sum / count, error = Math.Sqrt(Math.Max(0, squares / count - mean * mean) / count);
                        Console.WriteLine("MEASURE facet forge first order, {0}, cosines {1:F2} {2:F2}: closed form {3:F5}, walk {4:F5} +- {5:F5}",
                                          glass ? "dielectric" : "conductor", wo.z, wi.z, exact, mean, error);
                        worst = Math.Max(worst, Math.Abs(exact - mean) / (error + 1e-3 * exact));
                    }
                }
                EXPECT_LT(worst, 5, glass ? "dielectric" : "conductor");
            }
        }

        static BxDFCheckResult Check(BxDF bxdf, string name, bool bothSides, double albedoFloor, int repeats = 24, int samples = 40000, double pdfFloor = 0.95,
                                     bool reciprocityOfThePairs = true)
        {
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.cosines = new Float[] { 0.9f, 0.5f, 0.2f };
            options.pairs = 300;
            options.repeats = repeats;
            options.samples = samples;
            options.bothSides = bothSides;
            BxDFCheckResult result = BxDFCheck.Run(bxdf, name, options);
            EXPECT_EQ(0, result.invalid, name + ": negative or non finite values");
            if (reciprocityOfThePairs)
            {
                EXPECT_LT(result.reciprocityChi2, 1.6, name + ": not reciprocal");
            }
            EXPECT_LT(result.albedoMax, 1.005, name + ": albedo above one");
            EXPECT_GT(result.albedoMin, albedoFloor, name + ": loses energy");
            EXPECT_TRUE(result.albedoSigmas < 5 || result.albedoRelative < 1e-2, name + ": Sample_f and f disagree about the albedo, " + result.albedoSigmas);
            EXPECT_LT(result.pdfMax, 1.03, name + ": the pdf integrates to more than one");
            EXPECT_GT(result.pdfMin, pdfFloor, name + ": the pdf integrates to less than one");
            return result;
        }

        [TEST("FacetForge", "ChecksOfTheConductors")]
        public static void ChecksOfTheConductors()
        {
            Check(new FacetForgeBxDF(Reflector(Heightfield(ff.Kind.StudentT, 0.5, 0.5, 3), ff.FacetKind.Mirror, 1), null, 1.5f, false),
                  "FacetForge Student-T 0.5 gamma 3, mirror facets", false, 0.99);
            Check(new FacetForgeBxDF(Reflector(Blend(false, ff.Kind.GGX, 0.15, 0.5, ff.Kind.Beckmann, 0.6, 0.5), ff.FacetKind.Mirror, 1), null, 1.5f, false),
                  "FacetForge mixture GGX 0.15 + Beckmann 0.6, mirror facets", false, 0.99);
            Check(new FacetForgeBxDF(Reflector(Blend(true, ff.Kind.GGX, 0.6, 0.4, ff.Kind.Beckmann, 0.15, 0.6), ff.FacetKind.Mirror, 1), null, 1.5f, false),
                  "FacetForge strata GGX 0.6 over Beckmann 0.15, mirror facets", false, 0.99);
            // A stratum of gold-like facets over one of mirrors
            ff.Surface two = Reflector(Blend(true, ff.Kind.GGX, 0.5, 0.5, ff.Kind.GGX, 0.2, 0.5), ff.FacetKind.Mirror, 1);
            two.perLayerFacet = true;
            two.layerFacet[0] = two.facet.Clone();
            two.layerFacet[0].R = new SampledSpectrum(0.5f);
            two.layerFacet[1] = two.facet.Clone();
            Check(new FacetForgeBxDF(two, null, 1.5f, false), "FacetForge strata with their own facets", false, 0.4);
            // Diffuse facets on a distribution over the sphere
            ff.Surface porous = new ff.Surface();
            porous.ndf.n = 1;
            porous.ndf.c[0].kind = ff.Kind.VMF;
            porous.ndf.c[0].tab = new ff.NullTable();
            porous.ndf.c[0].tab.Init(ff.Kind.VMF, 1.0, 2);
            Check(new FacetForgeBxDF(Reflector(porous, ff.FacetKind.Diffuse, 1), null, 1.5f, false), "FacetForge vMF 1.0, diffuse facets", false, 0.97);
            // Two scales
            Check(new FacetForgeBxDF(Heightfield(ff.Kind.GGX, 0.4, 0.4, 2), Reflector(Heightfield(ff.Kind.Beckmann, 0.2, 0.2, 2), ff.FacetKind.Mirror, 1), 1.5f, false),
                  "FacetForge two scales, GGX 0.4 of Beckmann 0.2, mirror facets", false, 0.97, 24, 250000);
            // The retroreflective blend
            FacetForgeBxDF retro = new FacetForgeBxDF(Reflector(Heightfield(ff.Kind.GGX, 0.4, 0.4, 2), ff.FacetKind.Mirror, 1), null, 1.5f, false);
            retro.SetRetro(0.4f);
            Check(retro, "FacetForge GGX 0.4, mirror facets, retroreflectivity 0.4", false, 0.99);
        }

        // The light that the surface takes from a cone of directions to another, by the walk from either cone (the walk is exact
        // and its weights are bounded): the same both ways when the BSDF is reciprocal, f(wo, wi) of the radiance being
        // f(wi, wo) of the importance
        static double Exchange(BxDF bxdf, Vector3f from, Float cosFrom, Vector3f to, Float cosTo, TransportMode mode, int count, ref RNG rng, out double error)
        {
            Frame frame = Frame.FromZ(from);
            double sum = 0, squares = 0;
            for (int i = 0; i < count; ++i)
            {
                Vector3f wo = frame.FromLocal(SampleUniformCone(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), cosFrom));
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), mode, BxDFReflTransFlags.All);
                if (bs && Dot(bs.value().wi, to) > cosTo)
                {
                    double weight = AbsCosTheta(wo) * bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                    sum += weight;
                    squares += weight * weight;
                }
            }
            double solidAngle = 2 * Math.PI * (1 - cosFrom);
            double mean = sum / count;
            error = solidAngle * Math.Sqrt(Math.Max(0.0, squares / count - mean * mean) / count);
            return solidAngle * mean;
        }

        // The pairs of BxDFCheck judge an estimate by its spread over some tens of evaluations. That holds for the reflection.
        // Through the surface the estimate of f that starts outside has a tail those evaluations do not see (measured below: away
        // from the refracted lobe one evaluation in a thousand carries all of the value), so the transmission is judged by the
        // exchange between two cones, and that tail by the mean of millions of evaluations against the estimate from inside.
        static void ReciprocityOfADielectric(FacetForgeBxDF bxdf, string name, ulong seed)
        {
            RNG rng = new RNG(seed);
            // The reflection, on either side
            double chi2 = 0;
            int pairs = 0;
            for (int i = 0; i < 120; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                wi.z = wo.z > 0 ? Math.Abs(wi.z) : -Math.Abs(wi.z);
                if (Math.Abs(wo.z) < 0.05f || Math.Abs(wi.z) < 0.05f)
                {
                    continue;
                }
                double sumA = 0, squaresA = 0, sumB = 0, squaresB = 0;
                int repeats = 400;
                for (int k = 0; k < repeats; ++k)
                {
                    Vector3f d = 1e-5f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                    double a = bxdf.f(Normalize(wo + d), Normalize(wi - d), TransportMode.Radiance)[0];
                    double b = bxdf.f(Normalize(wi - d), Normalize(wo + d), TransportMode.Importance)[0];
                    sumA += a;
                    squaresA += a * a;
                    sumB += b;
                    squaresB += b * b;
                }
                double meanA = sumA / repeats, meanB = sumB / repeats;
                double variance = (Math.Max(0, squaresA / repeats - meanA * meanA) + Math.Max(0, squaresB / repeats - meanB * meanB)) / repeats;
                if (Math.Max(meanA, meanB) < 1e-4 || variance == 0)
                {
                    continue;
                }
                chi2 += Sqr(meanA - meanB) / variance;
                ++pairs;
            }
            chi2 /= pairs;

            // The transmission: from a cone outside to the cone it is refracted to, and to a cone away from it
            Vector3f outside = Direction(0.767f, 0);
            Vector3f lobe = new Vector3f(-outside.x / 1.5f, 0, -SafeSqrt(1 - Sqr(outside.x / 1.5f)));
            Vector3f away = Normalize(new Vector3f(0.8f, 0.4f, -0.447f));
            Float cosCone = (Float)Math.Cos(0.3);
            double e0, e1, e2, e3;
            double toLobe = Exchange(bxdf, outside, cosCone, lobe, cosCone, TransportMode.Radiance, 400000, ref rng, out e0);
            double fromLobe = Exchange(bxdf, lobe, cosCone, outside, cosCone, TransportMode.Importance, 400000, ref rng, out e1);
            double toAway = Exchange(bxdf, outside, cosCone, away, cosCone, TransportMode.Radiance, 3000000, ref rng, out e2);
            double fromAway = Exchange(bxdf, away, cosCone, outside, cosCone, TransportMode.Importance, 3000000, ref rng, out e3);
            Console.WriteLine("MEASURE {0}: reflection pairs chi2 {1:F2} ({2} pairs); exchange with the refracted cone {3:E4} against {4:E4} (+- {5:E1}), with a cone " +
                              "away from it {6:E3} against {7:E3} (+- {8:E1})", name, chi2, pairs, toLobe, fromLobe, Math.Sqrt(e0 * e0 + e1 * e1), toAway, fromAway,
                              Math.Sqrt(e2 * e2 + e3 * e3));
            EXPECT_GT(pairs, 50);
            EXPECT_LT(chi2, 1.6, name + ": the reflection is not reciprocal");
            EXPECT_LT(Math.Abs(toLobe - fromLobe), 5 * Math.Sqrt(e0 * e0 + e1 * e1), name + ": the transmission is not reciprocal");
            EXPECT_LT(Math.Abs(toAway - fromAway), 5 * Math.Sqrt(e2 * e2 + e3 * e3), name + ": the transmission away from the lobe is not reciprocal");
            EXPECT_GT(toAway, 0);
        }

        // f() through the surface, away from the refracted lobe: the estimate from inside against the mean of the estimate from
        // outside, whose value comes from one evaluation in a thousand
        [TEST("FacetForge", "TheTailOfTheTransmission")]
        public static void TheTailOfTheTransmission()
        {
            RNG rng = new RNG(23);
            FacetForgeBxDF bxdf = new FacetForgeBxDF(Dielectric(Heightfield(ff.Kind.StudentT, 0.4, 0.4, 3)), null, 1.5f, true);
            // (the order of the fork: with denseStart, the default, both estimates below are the walk from inside)
            bxdf.denseStart = false;
            Vector3f wo = Direction(0.767f, 0), wi = Normalize(new Vector3f(0.8f, 0.4f, -0.447f));
            double inside = 0, outside = 0, largest = 0;
            int countInside = 200000, countOutside = 2000000, above = 0, early = 0;
            for (int i = 0; i < countInside; ++i)
            {
                Vector3f d = 1e-5f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                inside += bxdf.f(Normalize(wi - d), Normalize(wo + d), TransportMode.Importance)[0];
            }
            inside /= countInside;
            double first = 0;
            for (int i = 0; i < countOutside; ++i)
            {
                Vector3f d = 1e-5f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                double value = bxdf.f(Normalize(wo + d), Normalize(wi - d), TransportMode.Radiance)[0];
                outside += value;
                largest = Math.Max(largest, value);
                if (value > 1e-3 * inside)
                {
                    ++above;
                }
                if (i == 2999)
                {
                    first = outside / 3000;
                }
            }
            outside /= countOutside;
            Console.WriteLine("MEASURE facet forge transmission away from the lobe: f from inside {0:E3}; from outside {1:E3} over {2} evaluations ({3:E3} over the " +
                              "first 3000), the largest {4:F2}, {5:P2} of them above a thousandth of the value", inside, outside, countOutside, first, largest,
                              (double)above / countOutside);
            EXPECT_LT(Math.Abs(outside - inside) / inside, 0.15);

            // With the start in the denser medium the same value comes with the spread of the estimate from inside: the mean of
            // 3000 evaluations, which was wrong by the whole value above
            FacetForgeBxDF dense = new FacetForgeBxDF(Dielectric(Heightfield(ff.Kind.StudentT, 0.4, 0.4, 3)), null, 1.5f, true);
            double sum = 0;
            for (int i = 0; i < 3000; ++i)
            {
                Vector3f d = 1e-5f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                sum += dense.f(Normalize(wo + d), Normalize(wi - d), TransportMode.Radiance)[0];
            }
            Console.WriteLine("MEASURE facet forge transmission away from the lobe, start in the denser medium: {0:E3} over 3000 evaluations", sum / 3000);
            EXPECT_LT(Math.Abs(sum / 3000 - inside) / inside, 0.15);
        }

        [TEST("FacetForge", "ChecksOfTheDielectrics")]
        public static void ChecksOfTheDielectrics()
        {
            ReciprocityOfADielectric(new FacetForgeBxDF(Dielectric(Heightfield(ff.Kind.StudentT, 0.4, 0.4, 3)), null, 1.5f, true),
                                     "FacetForge dielectric Student-T 0.4 gamma 3", 101);
            ReciprocityOfADielectric(new FacetForgeBxDF(Dielectric(Blend(true, ff.Kind.GGX, 0.6, 0.4, ff.Kind.Beckmann, 0.15, 0.6)), null, 1.5f, true),
                                     "FacetForge dielectric strata GGX 0.6 over Beckmann 0.15", 103);
            Check(new FacetForgeBxDF(Dielectric(Heightfield(ff.Kind.StudentT, 0.4, 0.4, 3)), null, 1.5f, true), "FacetForge dielectric Student-T 0.4 gamma 3", true,
                  0.99, 24, 40000, 0.95, false);
            Check(new FacetForgeBxDF(Dielectric(Blend(true, ff.Kind.GGX, 0.6, 0.4, ff.Kind.Beckmann, 0.15, 0.6)), null, 1.5f, true),
                  "FacetForge dielectric strata GGX 0.6 over Beckmann 0.15", true, 0.99, 24, 40000, 0.95, false);
            FacetForgeBxDF retro = new FacetForgeBxDF(Dielectric(Heightfield(ff.Kind.GGX, 0.4, 0.4, 2)), null, 1.5f, true);
            retro.SetRetro(0.4f);
            // (the density of the blend is proportional: the view turned by half a turn is chosen with 1 - a = rho / 2 and its
            // transmission is dropped, so the density integrates to a = 0.8 at least)
            Check(retro, "FacetForge dielectric GGX 0.4, retroreflectivity 0.4", true, 0.99, 24, 40000, 0.79, false);
        }

        [TEST("FacetForge", "CoatedDiffuse")]
        public static void CoatedDiffuse()
        {
            RNG rng = new RNG(3);
            // (a layer of pbrt absorbs along its thickness when it does not scatter: a thin one, for the energy)
            FacetForgeBxDF top = new FacetForgeBxDF(Dielectric(Heightfield(ff.Kind.StudentT, 0.3, 0.3, 3)), null, 1.5f, true);
            foreach (int depth in new int[] { 10, 100 })
            {
                FacetCoatedDiffuseBxDF white = new FacetCoatedDiffuseBxDF(top, new DiffuseBxDF(new SampledSpectrum(1f)), 1e-6f, new SampledSpectrum(0f), 0, depth, 1);
                FacetCoatedDiffuseBxDF grey = new FacetCoatedDiffuseBxDF(top, new DiffuseBxDF(new SampledSpectrum(0.5f)), 1e-6f, new SampledSpectrum(0f), 0, depth, 1);
                foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                {
                    Vector3f wo = Direction(cosine, 1.1f);
                    double e0, e1;
                    double a = Albedo(white, wo, 60000, ref rng, out e0), b = Albedo(grey, wo, 60000, ref rng, out e1);
                    Console.WriteLine("MEASURE facet coated diffuse, maxdepth {0}, cosine {1}: albedo {2:F4} +- {3:F4} over a white base, {4:F4} over a grey one", depth,
                                      cosine, a, e0, b);
                    EXPECT_LT(a - 4 * e0, 1 + 1e-4, "albedo above one");
                    EXPECT_LT(b, a);
                    if (depth == 100)
                    {
                        EXPECT_GT(a + 4 * e0, 0.98, "a coat and a base that absorb nothing lose energy");
                    }
                }
            }
        }
    }
}
