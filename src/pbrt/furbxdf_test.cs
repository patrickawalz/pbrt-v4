// (not in pbrt-v4) furbxdf_test.cs
//
// Tests of the fiber of layers (furbxdf.cs): with one layer that does not scatter it is the fiber of HairBxDF, and without
// absorption no light is lost in it.

using System;
using testing;

namespace pbrt
{
    public static unsafe class FurBxDFTests
    {
        private static Vector3f Direction(double theta, double phi)
        {
            return new Vector3f((Float)Math.Sin(theta), (Float)(Math.Cos(theta) * Math.Cos(phi)), (Float)(Math.Cos(theta) * Math.Sin(phi)));
        }

        [TEST("FurBxDF", "OneLayerIsTheHairOfPbrt")]
        public static void OneLayerIsTheHairOfPbrt()
        {
            SampledSpectrum sigma = new SampledSpectrum(1.2f);
            RNG rng = new RNG(11, 5);
            double worst = 0;
            int compared = 0;
            foreach (Float h in new Float[] { -0.7f, -0.2f, 0.35f, 0.8f })
            {
                HairBxDF hair = new HairBxDF(h, 1.55f, sigma, 0.3f, 0.3f, 0);
                FurBxDF fur = new FurBxDF(h, 1.55f, new Float[] { 1 }, new SampledSpectrum[] { sigma }, new Float[] { 0 }, 0, 0.3f, 0.3f, 0, 1, 64);
                foreach (double thetaO in new double[] { -0.5, 0.1, 0.7 })
                {
                    Vector3f wo = Direction(thetaO, 0.4);
                    for (int k = 0; k < 6; ++k)
                    {
                        Vector3f wi = Direction(-1.2 + 2.4 * rng.Uniform<Float>(), 2 * Math.PI * rng.Uniform<Float>());
                        if (pbrt.AbsCosTheta(wi) < 0.05f)
                        {
                            continue;
                        }
                        Float expected = hair.f(wo, wi, TransportMode.Radiance)[0];
                        Float found = fur.Estimate(wo, wi, ref rng, 1, out Float density)[0] / pbrt.AbsCosTheta(wi);
                        // (one walk: nothing is left to chance in a fiber that does not scatter. HairBxDF spreads the reflections
                        // after the third evenly around the fiber, and the walk has each in its own direction: a little of both)
                        double error = Math.Abs(found - expected) / Math.Max(expected, 0.05);
                        worst = Math.Max(worst, error);
                        ++compared;
                        if (error > 0.03)
                        {
                            Console.WriteLine("fur against hair: h " + h + " theta_o " + thetaO + " wi " + wi + ": hair " + expected + " fur " + found);
                        }
                    }
                }
            }
            Console.WriteLine("fur of one layer against HairBxDF: " + compared + " directions, worst difference " + worst);
            CHECK_GT(compared, 50);
            CHECK_LT(worst, 0.03);
        }

        [TEST("FurBxDF", "NoLightIsLostWithoutAbsorption")]
        public static void NoLightIsLostWithoutAbsorption()
        {
            RNG rng = new RNG(3, 9);
            SampledSpectrum none = new SampledSpectrum(0f);
            // (a medulla of 0.6 of the radius that scatters, and nothing that absorbs)
            FurBxDF fur = new FurBxDF(0.3f, 1.55f, new Float[] { 0.6f, 1 }, new SampledSpectrum[] { none, none }, new Float[] { 3, 0 }, 0.4f, 0.3f, 0.3f, 0, 1, 4096);
            Vector3f wo = Direction(0.3, 1.1);
            int left = 0, count = 200000;
            int[] kinds = new int[4];
            for (int i = 0; i < count; ++i)
            {
                if (fur.Walk(wo, ref rng, 0, out Float reflectance, out double ex, out double ey, out double ez, out SampledSpectrum weight, out int kind))
                {
                    ++left;
                    ++kinds[kind];
                    CHECK_EQ(weight[0], 1f);
                    // (as near to unit length as the single-precision direction that it came in by)
                    CHECK_LT(Math.Abs(ex * ex + ey * ey + ez * ez - 1), 1e-6);
                }
            }
            Console.WriteLine("fur without absorption: " + left + " of " + count + " walks leave (reflected " + kinds[0] + ", through " + kinds[1] + ", reflected inside " + kinds[2] +
                              ", scattered " + kinds[3] + ")");
            CHECK_GT(left, count - count / 1000);
            CHECK_GT(kinds[3], count / 10);
        }

        // The mean of f |cos| / pdf over the samples of a BxDF, and the integral of f |cos| over all directions by directions
        // drawn evenly: the part of the light that the fiber sends on, two ways
        private static void Albedo(BxDF bxdf, Vector3f wo, int count, out double sampled, out double integrated)
        {
            RNG rng = new RNG(21, 4);
            sampled = 0;
            integrated = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<BSDFSample> sample = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Radiance,
                                                                 BxDFReflTransFlags.All);
                if (sample && sample.value().pdf > 0)
                {
                    sampled += sample.value().f[0] * pbrt.AbsCosTheta(sample.value().wi) / sample.value().pdf;
                }
                double zeta = 1 - 2 * rng.Uniform<Float>(), radius = Math.Sqrt(Math.Max(0, 1 - zeta * zeta)), phi = 2 * Math.PI * rng.Uniform<Float>();
                Vector3f wi = new Vector3f((Float)zeta, (Float)(radius * Math.Cos(phi)), (Float)(radius * Math.Sin(phi)));
                integrated += bxdf.f(wo, wi, TransportMode.Radiance)[0] * pbrt.AbsCosTheta(wi) * 4 * Math.PI;
            }
            sampled /= count;
            integrated /= count;
        }

        [TEST("FurBxDF", "TheTableWithoutAMedullaIsTheHairOfPbrt")]
        public static void TheTableWithoutAMedullaIsTheHairOfPbrt()
        {
            SampledSpectrum sigma = new SampledSpectrum(1.6f);
            RNG rng = new RNG(2, 8);
            double worst = 0;
            foreach (Float h in new Float[] { -0.6f, 0.1f, 0.75f })
            {
                HairBxDF hair = new HairBxDF(h, 1.55f, sigma, 0.3f, 0.25f, 2);
                FurTableBxDF fur = new FurTableBxDF(h, 1.55f, sigma, 0, 0, new SampledSpectrum(0f), 0, 0.3f, 0.25f, 2);
                for (int k = 0; k < 40; ++k)
                {
                    Vector3f wo = Direction(-1.2 + 2.4 * rng.Uniform<Float>(), 2 * Math.PI * rng.Uniform<Float>());
                    Vector3f wi = Direction(-1.2 + 2.4 * rng.Uniform<Float>(), 2 * Math.PI * rng.Uniform<Float>());
                    Float expected = hair.f(wo, wi, TransportMode.Radiance)[0], found = fur.f(wo, wi, TransportMode.Radiance)[0];
                    // (HairBxDF has the reflections after the third as well: a little, evenly around the fiber)
                    worst = Math.Max(worst, Math.Abs(found - expected) / Math.Max(expected, 0.05));
                }
            }
            Console.WriteLine("fur table without a medulla against HairBxDF: worst difference " + worst);
            CHECK_LT(worst, 0.01);
        }

        [TEST("FurBxDF", "TheSamplesOfTheTableAreThoseOfItsValue")]
        public static void TheSamplesOfTheTableAreThoseOfItsValue()
        {
            SampledSpectrum sigma = new SampledSpectrum(0.4f), none = new SampledSpectrum(0f);
            foreach (Float h in new Float[] { -0.5f, 0.2f, 0.7f })
            {
                Vector3f wo = Direction(0.3, 0.9);
                FurTableBxDF table = new FurTableBxDF(h, 1.55f, sigma, 0.7f, 3, none, 0.4f, 0.3f, 0.3f, 2);
                Albedo(table, wo, 400000, out double sampled, out double integrated);
                FurBxDF walk = new FurBxDF(h, 1.55f, new Float[] { 0.7f, 1 }, new SampledSpectrum[] { none, sigma }, new Float[] { 3, 0 }, 0.4f, 0.3f, 0.3f, 2, 1, 256);
                RNG rng = new RNG(5, 5);
                double walked = 0;
                int walks = 400000;
                for (int i = 0; i < walks; ++i)
                {
                    if (walk.Walk(wo, ref rng, 0, out Float reflectance, out double ex, out double ey, out double ez, out SampledSpectrum weight, out int kind))
                    {
                        walked += weight[0];
                    }
                }
                walked /= walks;
                Console.WriteLine("fur table, h " + h + ": the light sent on, by its samples " + sampled + ", by its value " + integrated + "; by the walk " + walked);
                CHECK_LT(Math.Abs(sampled - integrated), 0.01 * integrated);
                CHECK_LT(sampled, 1.02);
            }
        }

        [TEST("FiberDiffraction", "TheLobeIsOfUnitIntegralAndItsSamplesFollowIt")]
        public static void TheLobeIsOfUnitIntegralAndItsSamplesFollowIt()
        {
            // The sine integral against Simpson's rule
            foreach (double x in new double[] { 0.3, 2.5, 5.9, 6.1, 17, 80, 600 })
            {
                int steps = 200000;
                double sum = 0, step = x / steps;
                for (int k = 0; k <= steps; ++k)
                {
                    double t = k * step, value = t == 0 ? 1 : Math.Sin(t) / t;
                    sum += value * (k == 0 || k == steps ? 1 : k % 2 == 1 ? 4 : 2);
                }
                sum *= step / 3;
                CHECK_LT(Math.Abs(FiberDiffractionBxDF.SineIntegral(x) - sum), 2e-6);
            }
            // The part of the lobe around the fiber is of unit integral, for narrow and wide fibers
            foreach (double width in new double[] { 0.3, 3, 40, 150 })
            {
                int steps = 2000000;
                double sum = 0;
                for (int k = 0; k < steps; ++k)
                {
                    double phi = -0.5 * Math.PI + Math.PI * (k + 0.5) / steps;
                    sum += Math.Cos(phi) * width * FiberDiffractionBxDF.Sinc2(width * Math.Sin(phi)) / FiberDiffractionBxDF.Whole(width) * Math.PI / steps;
                }
                Console.WriteLine("fiber diffraction: width " + width + " wavelengths: the integral of the lobe around the fiber " + sum);
                CHECK_LT(Math.Abs(sum - 1), 1e-4);
            }
            // With the hair of pbrt: half its light and half the lobe's, by the samples of the two together
            HairBxDF hair = new HairBxDF(0.3f, 1.55f, new SampledSpectrum(0.5f), 0.3f, 0.3f, 2);
            Vector3f wo = Direction(0.25, 0.7);
            Albedo(hair, wo, 400000, out double hairSampled, out double hairIntegrated);
            SampledSpectrum sizes = new SampledSpectrum(0f);
            for (int k = 0; k < pbrt.NSpectrumSamples; ++k)
            {
                // (a fiber of 70 micrometers at 400 to 700 nanometers)
                sizes[k] = 70000f / (400 + 100 * k);
            }
            // The smooth lobe is of unit integral too
            foreach (double width in new double[] { 0.3, 3, 40, 150 })
            {
                int steps = 2000000;
                double sum = 0;
                for (int k = 0; k < steps; ++k)
                {
                    double phi = -0.5 * Math.PI + Math.PI * (k + 0.5) / steps, x = width * Math.Sin(phi);
                    double profile = Math.Abs(x) > 0.75 ? 1 / (2 * Math.PI * Math.PI * x * x) : FiberDiffractionBxDF.Sinc2(x);
                    sum += Math.Cos(phi) * width * profile / FiberDiffractionBxDF.SmoothWhole(width) * Math.PI / steps;
                }
                CHECK_LT(Math.Abs(sum - 1), 1e-4);
            }
            for (int variant = 0; variant < 2; ++variant)
            {
            FiberDiffractionBxDF both = new FiberDiffractionBxDF(hair, sizes, 0.02f, false, variant == 1, false);
            RNG rng = new RNG(8, 1);
            double sampled = 0, forward = 0;
            int count = 400000, lobes = 0;
            for (int k = 0; k < count; ++k)
            {
                pstd.optional<BSDFSample> sample = both.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Radiance,
                                                                 BxDFReflTransFlags.All);
                if (sample && sample.value().pdf > 0)
                {
                    sampled += sample.value().f[0] * pbrt.AbsCosTheta(sample.value().wi) / sample.value().pdf;
                    // (within 30 degrees of straight on: the lobe is a fraction of a degree wide around the fiber, and as wide along it as Mp)
                    if (pbrt.Dot(sample.value().wi, -wo) > Math.Cos(30 * Math.PI / 180))
                    {
                        ++lobes;
                    }
                }
            }
            sampled /= count;
            forward = (double)lobes / count;
            Console.WriteLine("fiber diffraction (" + (variant == 1 ? "smooth" : "exact") + ") with hair: the light sent on " + sampled + " (hair alone " + hairSampled + "); samples within 30 degrees of straight on " + forward);
            CHECK_LT(Math.Abs(sampled - 0.5 * (hairSampled + 1)), 0.01);
            CHECK_GT(forward, 0.45);
            }
        }

        [TEST("FurBxDF", "TheMicrofacetCuticleAndTheEllipseKeepTheLight")]
        public static void TheMicrofacetCuticleAndTheEllipseKeepTheLight()
        {
            SampledSpectrum none = new SampledSpectrum(0f), some = new SampledSpectrum(0.5f);
            Vector3f wo = Direction(0.3, 1.3);
            for (int which = 0; which < 7; ++which)
            {
                // 0, 1: a cuticle of microfacets without and with absorption; 2, 3: an ellipse of smooth cuticle, the same; 4: both.
                // (2 is without the tilt of the scales: with it 2 or 3 walks of 100 in an ellipse that does not absorb turn along
                // the fiber at each reflection inside until they cannot leave, and are given up at the last step.)
                bool absorbing = which == 1 || which == 3 || which == 4;
                FurBxDF fur = new FurBxDF(0.35f, 1.55f, new Float[] { 0.5f, 1 }, new SampledSpectrum[] { none, absorbing ? some : none }, new Float[] { 2, 0 }, 0.3f, 0.3f, 0.3f,
                                          which == 2 ? 0 : 2, 1, 512);
                if (which <= 1 || which == 4)
                {
                    fur.UseMicrofacets(0.2f);
                }
                // (5, 6: the microfacets with the light between them, of Heitz and of FacetForge, not absorbing)
                if (which == 5)
                {
                    fur.UseMicrofacetsOfHeitz(0.2f);
                }
                if (which == 6)
                {
                    fur.UseMicrofacetsOfFacetForge(0.2f);
                }
                if (which >= 2 && which <= 4)
                {
                    fur.Aspect = 0.65f;
                    fur.Orientation = 0.8f;
                }
                Albedo(fur, wo, 600000, out double sampled, out double integrated);
                Console.WriteLine("fur " + (which <= 1 ? "with a cuticle of microfacets" : which < 4 ? "of an ellipse 0.65" : which == 4 ? "of an ellipse with microfacets" :
                                           which == 5 ? "with the microfacets of Heitz" : "with the microfacets of FacetForge") +
                                  (absorbing ? ", absorbing" : ", not absorbing") + ": the light sent on, by its samples " + sampled + ", by its value " + integrated);
                CHECK_LT(Math.Abs(sampled - integrated), 0.02 * integrated);
                CHECK_LT(sampled, 1.005);
                if (!absorbing)
                {
                    // (the microfacets lose the light that would be reflected a second time between them, as those of pbrt do, at
                    // each of the many times that a walk with a medulla is at the surface: about 15 of 100 here)
                    CHECK_GT(sampled, which == 0 ? 0.8 : which >= 5 ? 0.97 : 0.995);
                }
            }
            // A round fiber turned about its axis is the same fiber
            FurBxDF round = new FurBxDF(0.35f, 1.55f, new Float[] { 1 }, new SampledSpectrum[] { some }, new Float[] { 0 }, 0, 0.3f, 0.3f, 2, 1, 64);
            FurBxDF turned = new FurBxDF(0.35f, 1.55f, new Float[] { 1 }, new SampledSpectrum[] { some }, new Float[] { 0 }, 0, 0.3f, 0.3f, 2, 1, 64);
            turned.Orientation = 1.1f;
            RNG first = new RNG(4, 4), second = new RNG(4, 4);
            Vector3f wi = Direction(-0.2, 3.9);
            Float a = round.Estimate(wo, wi, ref first, 1, out Float densityA)[0], b = turned.Estimate(wo, wi, ref second, 1, out Float densityB)[0];
            Console.WriteLine("fur round, and turned about its axis: " + a + " and " + b);
            CHECK_LT(Math.Abs(a - b), 0.02 * a);
        }

        // The reflection of the cuticle of microfacets, over the offsets across the fiber, is the R lobe of Huang et al. 2022
        // (their equation 12): S_R cos theta_i = F / (8 cos theta_o) times the integral of D G around the fiber
        [TEST("FurBxDF", "TheMicrofacetReflectionIsTheRLobeOfThePaper")]
        public static void TheMicrofacetReflectionIsTheRLobeOfThePaper()
        {
            Float roughness = 0.25f, eta = 1.55f;
            TrowbridgeReitzDistribution distribution = new TrowbridgeReitzDistribution(roughness, roughness);
            // (a fiber that lets nothing through: only the reflection of its surface is left)
            SampledSpectrum black = new SampledSpectrum(1000f);
            double worst = 0;
            RNG rng = new RNG(6, 6);
            for (int k = 0; k < 12; ++k)
            {
                Vector3f wo = Direction(-0.9 + 1.8 * rng.Uniform<Float>(), 0.3);
                Vector3f wi = Direction(Math.Asin(-wo.x) + 0.3 * (rng.Uniform<Float>() - 0.5), 0.3 + 2.4 * (rng.Uniform<Float>() - 0.5));
                // The walk, over the offsets
                int offsets = 4000;
                double walked = 0;
                for (int j = 0; j < offsets; ++j)
                {
                    FurBxDF fur = new FurBxDF((Float)(-1 + 2 * (j + 0.5) / offsets), eta, new Float[] { 1 }, new SampledSpectrum[] { black }, new Float[] { 0 }, 0, 0.3f, 0.3f, 0, 1, 8);
                    fur.UseMicrofacets(roughness);
                    walked += fur.Estimate(wo, wi, ref rng, 1, out Float density)[0] / offsets;
                }
                // The integral around the fiber, over the normals that both directions are in front of
                Vector3f half = pbrt.Normalize(wo + wi);
                double integral = 0;
                int steps = 20000;
                for (int j = 0; j < steps; ++j)
                {
                    double phi = 2 * Math.PI * (j + 0.5) / steps;
                    Vector3f normal = new Vector3f(0, (Float)Math.Cos(phi), (Float)Math.Sin(phi));
                    Float cosO = pbrt.Dot(wo, normal), cosI = pbrt.Dot(wi, normal);
                    if (cosO <= 0 || cosI <= 0)
                    {
                        continue;
                    }
                    // (in the frame of the normal)
                    Vector3f side = new Vector3f(1, 0, 0), other = pbrt.Cross(normal, side);
                    Vector3f localO = new Vector3f(pbrt.Dot(wo, side), pbrt.Dot(wo, other), cosO), localI = new Vector3f(pbrt.Dot(wi, side), pbrt.Dot(wi, other), cosI);
                    Vector3f localH = new Vector3f(pbrt.Dot(half, side), pbrt.Dot(half, other), pbrt.Dot(half, normal));
                    integral += distribution.D(localH) * distribution.G(localO, localI) * 2 * Math.PI / steps;
                }
                Float cosThetaO = pbrt.SafeSqrt(1 - wo.x * wo.x);
                double expected = pbrt.FrDielectric(pbrt.AbsDot(wo, half), eta) * integral / (8 * cosThetaO);
                worst = Math.Max(worst, Math.Abs(walked - expected) / Math.Max(expected, 0.02));
            }
            Console.WriteLine("fur with microfacets, the reflection over the offsets against the R lobe of the paper: worst difference " + worst);
            CHECK_LT(worst, 0.01);
        }
    }
}
