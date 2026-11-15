// (not in pbrt-v4) Tests of the lobes of OpenPBR's thin-walled mode (thinwall.cs) and of the mode itself.

using System;
using testing;

namespace pbrt
{
    public static unsafe class ThinWallTests
    {
        // What a BxDF reflects and what it transmits of the light from wo, by its samples
        private static void Split(BxDF bxdf, Vector3f wo, int count, ref RNG rng, out double reflected, out double transmitted, out double error)
        {
            double r = 0, t = 0, rr = 0, tt = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Radiance,
                                                             BxDFReflTransFlags.All);
                if (!bs || bs.value().pdf == 0)
                {
                    continue;
                }
                double v = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                if (bs.value().wi.z * wo.z > 0)
                {
                    r += v;
                    rr += v * v;
                }
                else
                {
                    t += v;
                    tt += v * v;
                }
            }
            reflected = r / count;
            transmitted = t / count;
            // (the larger of the errors of the two parts)
            error = Math.Sqrt(Math.Max(Math.Max(0.0, rr / count - reflected * reflected), Math.Max(0.0, tt / count - transmitted * transmitted)) / count);
        }

        // The ladder against light followed between the two faces one bounce at a time
        [TEST("ThinWall", "TheLadderOfTheSheet")]
        public static void TheLadderOfTheSheet()
        {
            RNG rng = new RNG(41);
            foreach (Float colour in new Float[] { 1f, 0.5f, 0.1f })
            {
                foreach (Float cosine in new Float[] { 1f, 0.6f, 0.2f })
                {
                    Float eta = 1.5f;
                    ThinSheetBxDF sheet = new ThinSheetBxDF(eta, 0, 0, new SampledSpectrum(colour));
                    SampledSpectrum R, T;
                    sheet.Sheet(cosine, out R, out T);
                    // (the two polarisations keep to themselves between the faces: a path is of one or of the other)
                    Float Fs, Fp, Ds, Dp;
                    FrDielectricPolarised(cosine, eta, out Fs, out Fp, out Ds, out Dp);
                    double cosInside = Math.Sqrt(1 - (1 - cosine * cosine) / (eta * eta)), a = Math.Pow(colour, 1 / cosInside);
                    int count = 400000;
                    double r = 0, t = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        double face = (i & 1) == 0 ? Fs : Fp;
                        if (rng.Uniform<double>() < face)
                        {
                            r += 1;
                            continue;
                        }
                        double weight = 1;
                        bool towardsBack = true;
                        while (true)
                        {
                            weight *= a;
                            if (rng.Uniform<double>() >= face)
                            {
                                if (towardsBack)
                                {
                                    t += weight;
                                }
                                else
                                {
                                    r += weight;
                                }
                                break;
                            }
                            towardsBack = !towardsBack;
                            if (weight < 1e-9)
                            {
                                break;
                            }
                        }
                    }
                    r /= count;
                    t /= count;
                    double sigma = 0.5 / Math.Sqrt(count);
                    Console.WriteLine("MEASURE sheet colour {0}, cosine {1}: reflected {2:F4} (followed {3:F4}), transmitted {4:F4} (followed {5:F4})", colour, cosine, R[0],
                                      r, T[0], t);
                    EXPECT_LT(Math.Abs(R[0] - r), 4 * sigma);
                    EXPECT_LT(Math.Abs(T[0] - t), 4 * sigma);
                    // the samples of the smooth sheet give the same
                    double sr, st, se;
                    Split(sheet, CombineTests.Direction(cosine == 1 ? 0.9999f : cosine, 0.7f), 100000, ref rng, out sr, out st, out se);
                    if (cosine != 1)
                    {
                        EXPECT_LT(Math.Abs(sr - R[0]), 4 * se + 1e-4);
                        EXPECT_LT(Math.Abs(st - T[0]), 4 * se + 1e-4);
                    }
                }
            }
        }

        // The rough sheet: its samples agree with f, f is the same with the directions exchanged, and what it loses is what a
        // microfacet lobe loses (one reflection on the facets)
        [TEST("ThinWall", "TheRoughSheet")]
        public static void TheRoughSheet()
        {
            RNG rng = new RNG(43);
            foreach (Float alpha in new Float[] { 0.1f, 0.3f, 0.6f })
            {
                ThinSheetBxDF sheet = new ThinSheetBxDF(1.5f, alpha, alpha, new SampledSpectrum(0.7f));
                foreach (Float cosine in new Float[] { 0.9f, 0.4f })
                {
                    Vector3f wo = CombineTests.Direction(cosine, 0.3f);
                    double sr, st, se;
                    Split(sheet, wo, 200000, ref rng, out sr, out st, out se);
                    double fr = 0, ft = 0, squares = 0, density = 0;
                    int count = 400000;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        double v = sheet.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI;
                        squares += v * v;
                        density += sheet.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All) * 4 * Math.PI / count;
                        if (wi.z > 0)
                        {
                            fr += v;
                        }
                        else
                        {
                            ft += v;
                        }
                    }
                    fr /= count;
                    ft /= count;
                    double fe = Math.Sqrt(Math.Max(0.0, squares / count - (fr + ft) * (fr + ft)) / count);
                    SampledSpectrum R, T;
                    sheet.Sheet(cosine, out R, out T);
                    Console.WriteLine("MEASURE rough sheet alpha {0}, cosine {1}: reflected {2:F4} by samples, {3:F4} by f; transmitted {4:F4}, {5:F4} (+- {6:F4}); " +
                                      "smooth sheet {7:F4} + {8:F4}; density {9:F4}", alpha, cosine, sr, fr, st, ft, Math.Sqrt(se * se + fe * fe), R[0], T[0], density);
                    EXPECT_LT(Math.Abs(sr - fr), 4 * Math.Sqrt(se * se + fe * fe));
                    EXPECT_LT(Math.Abs(st - ft), 4 * Math.Sqrt(se * se + fe * fe));
                    EXPECT_LT(sr + st, R[0] + T[0] + 6 * se);
                    EXPECT_LT(density, 1.02);
                }
                double worst = 0;
                for (int i = 0; i < 2000; ++i)
                {
                    Vector3f a = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f b = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (std.abs(a.z) < 0.05f || std.abs(b.z) < 0.05f)
                    {
                        continue;
                    }
                    double f1 = sheet.f(a, b, TransportMode.Radiance)[0], f2 = sheet.f(b, a, TransportMode.Radiance)[0];
                    worst = Math.Max(worst, Math.Abs(f1 - f2) / Math.Max(1e-6, Math.Max(f1, f2)));
                }
                Console.WriteLine("MEASURE rough sheet alpha {0}: f(a, b) against f(b, a), worst relative difference {1:E2}", alpha, worst);
                EXPECT_LT(worst, 1e-4);
            }
        }

        // The thin sheet that scatters: the shares of the paper's Eq. 94
        [TEST("ThinWall", "TheSharesOfTheScatteringSheet")]
        public static void TheSharesOfTheScatteringSheet()
        {
            RNG rng = new RNG(47);
            foreach (Float g in new Float[] { -1f, -0.5f, 0f, 0.5f, 1f })
            {
                Float S = 0.8f, roughness = 0.4f;
                TranslucentEonBxDF sheet = new TranslucentEonBxDF(new SampledSpectrum(S), g, roughness);
                EonDiffuseBxDF lobe = new EonDiffuseBxDF(new SampledSpectrum(S), roughness);
                Vector3f wo = CombineTests.Direction(0.7f, 2.1f);
                double sr, st, se, whole, we;
                Split(sheet, wo, 200000, ref rng, out sr, out st, out se);
                CombineTests.Albedo(lobe, wo, 200000, ref rng, out whole, out we);
                Console.WriteLine("MEASURE scattering sheet g {0}: reflected {1:F4} (expected {2:F4}), transmitted {3:F4} (expected {4:F4}); the EON lobe {5:F4}", g, sr,
                                  0.5 * (1 - g) * whole, st, 0.5 * (1 + g) * whole, whole);
                EXPECT_LT(Math.Abs(sr - 0.5 * (1 - g) * whole), 4 * Math.Sqrt(se * se + we * we));
                EXPECT_LT(Math.Abs(st - 0.5 * (1 + g) * whole), 4 * Math.Sqrt(se * se + we * we));
                EXPECT_LT(sr + st, S + 6 * se);
                // from below it is the same sheet
                Vector3f below = new Vector3f(wo.x, wo.y, -wo.z);
                double br, bt, be;
                Split(sheet, below, 200000, ref rng, out br, out bt, out be);
                EXPECT_LT(Math.Abs(br - sr), 4 * Math.Sqrt(se * se + be * be));
                EXPECT_LT(Math.Abs(bt - st), 4 * Math.Sqrt(se * se + be * be));
            }
        }

        // The mode: the wall is the same from either side, and nothing is lost where nothing absorbs
        [TEST("ThinWall", "TheSameFromEitherSide")]
        public static void TheSameFromEitherSide()
        {
            RNG rng = new RNG(53);
            string[] names = new string[] { "glass sheet", "coloured rough glass sheet", "paper (subsurface), g 0.5", "coated paper with fuzz", "white paper, clear sheet" };
            BxDF[] walls = new BxDF[] {
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("transmission_weight", 1), OpenPBRTests.N("specular_roughness", 0)),
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("transmission_weight", 1), OpenPBRTests.N("specular_roughness", 0.4f),
                                   OpenPBRTests.C("transmission_color", 0.9f, 0.6f, 0.3f)),
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("subsurface_weight", 1), OpenPBRTests.N("subsurface_scatter_anisotropy", 0.5f)),
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("subsurface_weight", 1), OpenPBRTests.N("coat_weight", 1),
                                   OpenPBRTests.N("fuzz_weight", 0.5f)),
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("subsurface_weight", 1), OpenPBRTests.C("subsurface_color", 1, 1, 1),
                                   OpenPBRTests.N("specular_roughness", 0), OpenPBRTests.N("base_diffuse_roughness", 0)) };
            for (int c = 0; c < walls.Length; ++c)
            {
                Vector3f above = CombineTests.Direction(0.6f, 0.9f), below = new Vector3f(above.x, above.y, -above.z);
                double ar, at, ae, br, bt, be;
                Split(walls[c], above, 150000, ref rng, out ar, out at, out ae);
                Split(walls[c], below, 150000, ref rng, out br, out bt, out be);
                Console.WriteLine("MEASURE thin wall, {0}: from above reflected {1:F4} transmitted {2:F4}; from below {3:F4} {4:F4} (+- {5:F4})", names[c], ar, at, br, bt,
                                  Math.Sqrt(ae * ae + be * be));
                EXPECT_LT(Math.Abs(ar - br), 4 * Math.Sqrt(ae * ae + be * be));
                EXPECT_LT(Math.Abs(at - bt), 4 * Math.Sqrt(ae * ae + be * be));
                EXPECT_LT(ar + at, 1 + 6 * ae);
                if (c == 0 || c == 4)
                {
                    EXPECT_GT(ar + at, 1 - 6 * ae - 2e-3);
                }
            }
        }

        // The samples of the wall and its values agree (the light sampling of a renderer uses the values)
        [TEST("ThinWall", "SamplesAgreeWithValues")]
        public static void SamplesAgreeWithValues()
        {
            RNG rng = new RNG(67);
            string[] names = new string[] { "paper, g 0", "paper, g 0.6", "rough-coated paper", "rough-coated coloured rough glass sheet", "rough glass (not thin)",
                                            "rough-coated rough glass (not thin)", "smooth-coated rough glass (not thin)" };
            BxDF[] walls = new BxDF[] {
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("subsurface_weight", 1)),
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("subsurface_weight", 1), OpenPBRTests.N("subsurface_scatter_anisotropy", 0.6f)),
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("subsurface_weight", 1), OpenPBRTests.N("coat_weight", 1),
                                   OpenPBRTests.N("coat_roughness", 0.3f)),
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("transmission_weight", 1), OpenPBRTests.N("specular_roughness", 0.4f),
                                   OpenPBRTests.C("transmission_color", 0.9f, 0.6f, 0.3f), OpenPBRTests.N("coat_weight", 1), OpenPBRTests.N("coat_roughness", 0.3f)),
                OpenPBRTests.Build(OpenPBRTests.N("transmission_weight", 1), OpenPBRTests.N("specular_roughness", 0.4f)),
                OpenPBRTests.Build(OpenPBRTests.N("transmission_weight", 1), OpenPBRTests.N("specular_roughness", 0.4f), OpenPBRTests.N("coat_weight", 1),
                                   OpenPBRTests.N("coat_roughness", 0.3f)),
                OpenPBRTests.Build(OpenPBRTests.N("transmission_weight", 1), OpenPBRTests.N("specular_roughness", 0.4f), OpenPBRTests.N("coat_weight", 1)) };
            for (int c = 0; c < walls.Length; ++c)
            {
                Vector3f wo = CombineTests.Direction(0.6f, 0.9f);
                double sr, st, se;
                Split(walls[c], wo, 150000, ref rng, out sr, out st, out se);
                double fr = 0, ft = 0, rr = 0, tt = 0;
                int count = 300000;
                for (int i = 0; i < count; ++i)
                {
                    Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    double v = walls[c].f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI;
                    if (wi.z > 0)
                    {
                        fr += v;
                        rr += v * v;
                    }
                    else
                    {
                        ft += v;
                        tt += v * v;
                    }
                }
                fr /= count;
                ft /= count;
                double er = Math.Sqrt(Math.Max(0.0, rr / count - fr * fr) / count), et = Math.Sqrt(Math.Max(0.0, tt / count - ft * ft) / count);
                Console.WriteLine("MEASURE thin wall, {0}: reflected {1:F4} by samples, {2:F4} +- {3:F4} by values; transmitted {4:F4}, {5:F4} +- {6:F4}", names[c], sr, fr, er,
                                  st, ft, et);
                if (c != 6)
                {
                    // (the smooth coat reflects as a mirror: not in the values)
                    EXPECT_LT(Math.Abs(sr - fr), 4 * Math.Sqrt(se * se + er * er));
                }
                EXPECT_LT(Math.Abs(st - ft), 4 * Math.Sqrt(se * se + et * et));
            }
        }

        // (the evaluated side of the comparison draws directions evenly: the narrow lobe of the light that goes through needs many)
        // The Mueller matrices. The smooth clear sheet against the matrix that the polarised integrator has for pbrt's thin
        // dielectric (both with a first element of 1: pbrt's scalar reflectance is not the mean of the two polarisations); the
        // rough and the coloured sheet, sampled against evaluated; and the wall of OpenPBR that holds them.
        [TEST("ThinWall", "Polarised")]
        public static void Polarised()
        {
            RNG rng = new RNG(71);
            BxDF sheet = new ThinSheetBxDF(1.5f, 0, 0, new SampledSpectrum(1f)), thin = new ThinDielectricBxDF(1.5f);
            BSDF mine = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), sheet), pbrts = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), thin);
            SurfaceInteraction surface = new SurfaceInteraction();
            surface.shading.n = new Normal3f(0, 0, 1);
            surface.n = surface.shading.n;
            double worst = 0;
            int compared = 0;
            for (int i = 0; i < 4000; ++i)
            {
                Vector3f wo = FacetForgePolarisedTests.Upper(ref rng);
                if ((i & 1) == 1)
                {
                    wo = new Vector3f(wo.x, wo.y, -wo.z);
                }
                // (a number that makes both lobes reflect, or both transmit)
                Float uc = (i & 2) == 0 ? 1e-4f : 0.9999f;
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<BSDFSample> bs = pbrts.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                PolarisedLightSystem.Attenuation reference, A;
                bool polarised;
                pstd.optional<BSDFSample> bm = mine.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All, out polarised, out A);
                if (!bs || !bm || !polarised || bs.value().IsReflection() != bm.value().IsReflection() ||
                    !PolarisedPathIntegrator.SpecularAttenuation(pbrts, surface, wo, bs.value(), TransportMode.Radiance, out reference))
                {
                    continue;
                }
                Float first = reference.GetMuellerMatrix().m00[0];
                reference = reference * new SampledSpectrum(1 / first);
                worst = Math.Max(worst, FacetForgePolarisedTests.Difference(reference.GetMuellerMatrix(), A.GetMuellerMatrix()));
                ++compared;
            }
            Console.WriteLine("MEASURE polarised smooth clear sheet against the thin dielectric of the integrator: worst element {0:E1} ({1} samples)", worst, compared);
            EXPECT_GT(compared, 3000);
            EXPECT_LT(worst, 1e-4);

            FacetForgePolarisedTests.SampledAgainstEvaluated(new ThinSheetBxDF(1.5f, 0.3f, 0.3f, new SampledSpectrum(0.6f)), "rough coloured thin sheet", 1000000);
            FacetForgePolarisedTests.SampledAgainstEvaluated(new OrientedBxDF(new ThinSheetBxDF(1.5f, 0.3f, 0.2f, new SampledSpectrum(0.6f))),
                                                             "rough coloured thin sheet, oriented", 1000000);
            FacetForgePolarisedTests.SampledAgainstEvaluated(OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("transmission_weight", 1),
                                                                                OpenPBRTests.N("specular_roughness", 0.4f),
                                                                                OpenPBRTests.C("transmission_color", 0.9f, 0.6f, 0.3f)),
                                                             "openpbr thin wall, rough coloured glass", 3000000);
            FacetForgePolarisedTests.SampledAgainstEvaluated(OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("subsurface_weight", 1),
                                                                                OpenPBRTests.N("coat_weight", 1), OpenPBRTests.N("coat_roughness", 0.3f)),
                                                             "openpbr thin wall, rough-coated paper", 200000);
        }

        // "thin_walled_lobes" "legacy": the lobes before section 7 (pbrt's thin dielectric, an even diffuse sheet, the wall not
        // the same from below), with the corrections that are not a matter of lobes
        [TEST("ThinWall", "TheLobesBefore")]
        public static void TheLobesBefore()
        {
            RNG rng = new RNG(73);
            Vector3f above = CombineTests.Direction(0.6f, 0.9f), below = new Vector3f(above.x, above.y, -above.z);
            // the anisotropy is not heard, and the gloss is on one side only
            BxDF even = OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.T("thin_walled_lobes", "legacy"), OpenPBRTests.N("subsurface_weight", 1));
            BxDF forward = OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.T("thin_walled_lobes", "legacy"), OpenPBRTests.N("subsurface_weight", 1),
                                              OpenPBRTests.N("subsurface_scatter_anisotropy", 0.6f));
            BxDF paper = OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("subsurface_weight", 1),
                                            OpenPBRTests.N("subsurface_scatter_anisotropy", 0.6f));
            double er, et, ee, fr, ft, fe, pr, pt, pe, br, bt, be;
            Split(even, above, 150000, ref rng, out er, out et, out ee);
            Split(forward, above, 150000, ref rng, out fr, out ft, out fe);
            Split(paper, above, 150000, ref rng, out pr, out pt, out pe);
            Split(even, below, 150000, ref rng, out br, out bt, out be);
            Console.WriteLine("MEASURE legacy thin subsurface: transmitted {0:F4}, with anisotropy 0.6 {1:F4} (the paper's lobes: {2:F4}); from below reflected {3:F4} " +
                              "transmitted {5:F4}, against {4:F4} reflected from above", et, ft, pt, br, er, bt);
            EXPECT_LT(Math.Abs(et - ft), 4 * Math.Sqrt(ee * ee + fe * fe));
            EXPECT_GT(pt, ft + 0.05);
            EXPECT_GT(Math.Abs(br - er), 0.02);
            EXPECT_LT(br + bt, 1 + 6 * be);
            EXPECT_LT(er + et, 1 + 6 * ee);
            // the glass sheet: smooth whatever the roughness
            BxDF glass = OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.T("thin_walled_lobes", "legacy"), OpenPBRTests.N("transmission_weight", 1),
                                            OpenPBRTests.N("specular_roughness", 0.4f));
            pstd.optional<BSDFSample> bs = glass.Sample_f(above, 0.9f, new Point2f(0.3f, 0.6f), TransportMode.Radiance, BxDFReflTransFlags.All);
            ASSERT_TRUE((bool)bs);
            EXPECT_TRUE(bs.value().IsSpecular());
        }

        // What the approximation of the paper costs: the light meets the coat and the gloss on the side of wo only, so the
        // transmission through a wall with layers is not the same with the directions exchanged. Measured, not required.
        [TEST("ThinWall", "ReciprocityOfTransmission")]
        public static void ReciprocityOfTransmission()
        {
            RNG rng = new RNG(59);
            string[] names = new string[] { "rough glass sheet", "paper", "coated paper" };
            BxDF[] walls = new BxDF[] {
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("transmission_weight", 1), OpenPBRTests.N("specular_roughness", 0.4f)),
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("subsurface_weight", 1)),
                OpenPBRTests.Build(OpenPBRTests.B("geometry_thin_walled", true), OpenPBRTests.N("subsurface_weight", 1), OpenPBRTests.N("coat_weight", 1)) };
            for (int c = 0; c < walls.Length; ++c)
            {
                double worst = 0, mean = 0;
                int pairs = 0;
                foreach (Float ca in new Float[] { 0.95f, 0.6f, 0.25f })
                {
                    foreach (Float cb in new Float[] { 0.9f, 0.5f, 0.2f })
                    {
                        Vector3f a = CombineTests.Direction(ca, 0.4f), b = CombineTests.Direction(cb, 2.6f);
                        b = new Vector3f(b.x, b.y, -b.z);
                        double f1 = 0, f2 = 0;
                        int repeats = c == 0 ? 1 : 3000;
                        for (int i = 0; i < repeats; ++i)
                        {
                            // (the layered lobes are estimates: averaged over directions that differ in the last bits)
                            Vector3f aa = Normalize(a + new Vector3f(1e-5f * i, 0, 0)), bb = Normalize(b + new Vector3f(0, 1e-5f * i, 0));
                            f1 += walls[c].f(aa, bb, TransportMode.Radiance)[0] / repeats;
                            f2 += walls[c].f(bb, aa, TransportMode.Radiance)[0] / repeats;
                        }
                        double difference = Math.Abs(f1 - f2) / Math.Max(1e-6, 0.5 * (f1 + f2));
                        worst = Math.Max(worst, difference);
                        mean += difference;
                        ++pairs;
                    }
                }
                Console.WriteLine("MEASURE thin wall, {0}: transmission with the directions exchanged differs by {1:F1} % at worst, {2:F1} % in the mean", names[c],
                                  100 * worst, 100 * mean / pairs);
                if (c == 0)
                {
                    EXPECT_LT(worst, 1e-4);
                }
            }
        }
    }
}
