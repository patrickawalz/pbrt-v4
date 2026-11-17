// (not in pbrt-v4) Tests of the emission of glowing surfaces (thermalemission.cs).

using System;
using testing;
using PLight = pbrt.PolarisedLightSystem.Light;

namespace pbrt
{
    public static unsafe class ThermalEmissionTests
    {
        private static SurfaceInteraction FlatSurface(Vector3f wo)
        {
            return new SurfaceInteraction(new Point3fi(new Point3f(0, 0, 0)), new Point2f(0, 0), wo, new Vector3f(1, 0, 0), new Vector3f(0, 1, 0),
                                          new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0, false);
        }

        private static Vector3f Direction(Float cosine)
        {
            Float sine = std.sqrt(std.max(0f, 1 - cosine * cosine));
            return new Vector3f(sine * 0.6f, sine * 0.8f, cosine);
        }

        // What the BSDF sends toward wo of surroundings of the radiance one on the side of wo (and of a blackbody of the same
        // temperature on the other side of an interface, whose radiance is the square of the relative index times that): by
        // directions that are uniform over the sphere, not by the samples of the BSDF
        private static void Returned(BSDF bsdf, Vector3f wo, Float relativeIndexBelow, int count, ref RNG rng, out double mean, out double error)
        {
            double sum = 0, squares = 0;
            for (int i = 0; i < count; ++i)
            {
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                double v = bsdf.f(wo, wi)[0] * std.abs(wi.z) * (4 * Pi);
                if (wi.z * wo.z < 0)
                {
                    v *= wo.z > 0 ? relativeIndexBelow * relativeIndexBelow : 1 / (relativeIndexBelow * relativeIndexBelow);
                }
                sum += v;
                squares += v * v;
            }
            mean = sum / count;
            error = Math.Sqrt(Math.Max(0.0, squares / count - mean * mean) / count);
        }

        private static void Emitted(BSDF bsdf, Vector3f wo, bool opaque, int count, ref RNG rng, out double mean, out double error)
        {
            SurfaceInteraction si = FlatSurface(wo);
            double sum = 0, squares = 0;
            for (int i = 0; i < count; ++i)
            {
                Float uc = rng.Uniform<Float>();
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                double v = ThermalEmission.Emissivity(bsdf, si, wo, uc, u, opaque, false, false, out PLight emitted)[0];
                sum += v;
                squares += v * v;
            }
            mean = sum / count;
            error = Math.Sqrt(Math.Max(0.0, squares / count - mean * mean) / count);
        }

        // A smooth conductor emits one minus its Fresnel reflectance, without noise
        [TEST("ThermalEmission", "SmoothConductor")]
        public static void SmoothConductor()
        {
            SampledSpectrum eta = new SampledSpectrum(0.2f), k = new SampledSpectrum(3.5f);
            BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), new ConductorBxDF(new TrowbridgeReitzDistribution(0, 0), eta, k));
            foreach (Float cosine in new Float[] { 1f, 0.8f, 0.5f, 0.2f, 0.05f })
            {
                Vector3f wo = Direction(cosine);
                Float F = FrComplex(cosine, new pstd.complex(eta[0], k[0]));
                Float first = ThermalEmission.Emissivity(bsdf, FlatSurface(wo), wo, 0.3f, new Point2f(0.1f, 0.7f), false, false, false, out PLight a)[0];
                Float second = ThermalEmission.Emissivity(bsdf, FlatSurface(wo), wo, 0.9f, new Point2f(0.6f, 0.2f), false, false, false, out PLight b)[0];
                EXPECT_NEAR(1 - F, first, 1e-5f);
                EXPECT_EQ(first, second);
            }
        }

        // The emission and what the material sends back of a furnace add to the furnace: rough conductor, diffuse, and a rough
        // dielectric seen from outside and from inside (where the furnace is n^2 times brighter, and so is the emission)
        [TEST("ThermalEmission", "CompletesTheFurnace")]
        public static void CompletesTheFurnace()
        {
            RNG rng = new RNG(7);
            Float n = 1.5f;
            BSDF conductor = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0),
                                      new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)));
            BSDF diffuse = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), new DiffuseBxDF(new SampledSpectrum(0.6f)));
            BSDF dielectric = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), new DielectricBxDF(n, new TrowbridgeReitzDistribution(0.3f, 0.3f)));
            foreach (Float cosine in new Float[] { 0.9f, 0.5f, -0.9f, -0.5f })
            {
                Vector3f wo = Direction(cosine);
                double emitted, emittedError, returned, returnedError;
                if (cosine > 0)
                {
                    Emitted(conductor, wo, false, 200000, ref rng, out emitted, out emittedError);
                    Returned(conductor, wo, 1, 2000000, ref rng, out returned, out returnedError);
                    EXPECT_LT(Math.Abs(emitted + returned - 1), 4 * (emittedError + returnedError) + 1e-3);

                    Emitted(diffuse, wo, false, 200000, ref rng, out emitted, out emittedError);
                    Returned(diffuse, wo, 1, 2000000, ref rng, out returned, out returnedError);
                    EXPECT_LT(Math.Abs(emitted + returned - 1), 4 * (emittedError + returnedError) + 1e-3);
                    EXPECT_LT(Math.Abs(emitted - 0.4), 1e-3);
                }
                // (in units of the furnace on the side of wo: n^2 inside)
                double side = cosine > 0 ? 1 : n * n;
                Emitted(dielectric, wo, false, 400000, ref rng, out emitted, out emittedError);
                Returned(dielectric, wo, n, 4000000, ref rng, out returned, out returnedError);
                EXPECT_LT(Math.Abs(emitted / side + returned - 1), 4 * (emittedError / side + returnedError) + 1e-3);
            }
        }

        // An opaque body behind a rough interface emits one minus what the interface reflects: the reflection alone is sampled
        [TEST("ThermalEmission", "OpaqueRoughDielectric")]
        public static void OpaqueRoughDielectric()
        {
            RNG rng = new RNG(11);
            BSDF dielectric = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.3f, 0.3f)));
            foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
            {
                Vector3f wo = Direction(cosine);
                double emitted, emittedError;
                Emitted(dielectric, wo, true, 200000, ref rng, out emitted, out emittedError);
                double sum = 0, squares = 0;
                int count = 2000000;
                for (int i = 0; i < count; ++i)
                {
                    Vector3f wi = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    double v = dielectric.f(wo, wi)[0] * std.abs(wi.z) * (2 * Pi);
                    sum += v;
                    squares += v * v;
                }
                double reflected = sum / count;
                double reflectedError = Math.Sqrt(Math.Max(0.0, squares / count - reflected * reflected) / count);
                EXPECT_LT(Math.Abs(emitted + reflected - 1), 4 * (emittedError + reflectedError) + 1e-3);
                // (and it is far from the emission of a lossless glass, which is the energy that the microfacet model loses)
                EXPECT_GT(emitted, 0.5);
            }
        }

        // The resampled light ("thermal2"): a plate with a hot corner lights a point. The light sample alone, and the light sample
        // with a direction of the cosine, each with the weight that the density that the light reports gives it, have the mean
        // that a quadrature over the plate gives, for 1, 8 and 32 candidates.
        [TEST("ThermalEmission", "ResampledLightIsUnbiased")]
        public static void ResampledLightIsUnbiased()
        {
            Shape shape = AnimatedLightTests.MakeShape("quad", new Transform());
            FloatTexture temperature = new FloatBilerpTexture(new UVMapping(), 850, 850, 850, 1900);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            Point3f x = new Point3f(-0.4f, -0.3f, 1.2f);
            LightSampleContext ctx = new LightSampleContext(new Point3fi(x), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
            DiffuseAreaLight plain = ThermalPlate(shape, temperature, 1);

            // The quadrature: the radiance towards x times the cosine at x, over the solid angle of the plate
            double truth = 0;
            const int side = 1200;
            for (int i = 0; i < side; ++i)
            {
                for (int j = 0; j < side; ++j)
                {
                    Float u = (i + 0.5f) / side, v = (j + 0.5f) / side;
                    Point3f y = new Point3f(-1 + 2 * u, -1 + 2 * v, 0);
                    Vector3f d = x - y;
                    Float r2 = LengthSquared(d);
                    Vector3f w = d / std.sqrt(r2);
                    double L = plain.L(y, new Normal3f(0, 0, 1), new Point2f(u, v), w, lambda, 0)[0];
                    truth += L * std.abs(w.z) * std.abs(w.z) / r2 * (4.0 / (side * side));
                }
            }
            EXPECT_GT(truth, 0);

            RNG rng = new RNG(29);
            foreach (int candidates in new int[] { 1, 8, 32 })
            {
                DiffuseAreaLight light = ThermalPlate(shape, temperature, candidates);
                int count = candidates == 1 ? 3000000 : 600000;
                double sumLight = 0, squaresLight = 0, sumBoth = 0, squaresBoth = 0;
                for (int i = 0; i < count; ++i)
                {
                    double fromLight = 0, weighted = 0;
                    pstd.optional<LightLiSample> ls = light.SampleLi(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), lambda, true);
                    if (ls && ls.value().pdf > 0)
                    {
                        Float cosine = std.abs(ls.value().wi.z);
                        fromLight = ls.value().L[0] * cosine / ls.value().pdf;
                        Float cosinePdf = ls.value().wi.z < 0 ? cosine * InvPi : 0;
                        weighted = fromLight * PowerHeuristic(1, ls.value().pdf, 1, cosinePdf);
                    }
                    // A direction of the cosine about the direction down to the plate
                    Vector3f local = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f wi = new Vector3f(local.x, local.y, -local.z);
                    Float pdfCosine = local.z * InvPi;
                    pstd.optional<ShapeIntersection> hit = shape.Intersect(new Ray(x, wi), Infinity);
                    if (hit && pdfCosine > 0)
                    {
                        Float pdfLight = light.PDF_Li(ctx, wi, true);
                        double L = light.L(hit.value().intr.p(), hit.value().intr.n, hit.value().intr.uv, -wi, lambda, 0)[0];
                        weighted += L * local.z / pdfCosine * PowerHeuristic(1, pdfCosine, 1, pdfLight);
                    }
                    sumLight += fromLight;
                    squaresLight += fromLight * fromLight;
                    sumBoth += weighted;
                    squaresBoth += weighted * weighted;
                }
                double meanLight = sumLight / count, meanBoth = sumBoth / count;
                double errorLight = Math.Sqrt(Math.Max(0.0, squaresLight / count - meanLight * meanLight) / count);
                double errorBoth = Math.Sqrt(Math.Max(0.0, squaresBoth / count - meanBoth * meanBoth) / count);
                Console.WriteLine($"candidates {candidates}: light alone {meanLight / truth:F5} +- {errorLight / truth:F5}, with the cosine {meanBoth / truth:F5} +- {errorBoth / truth:F5} (of the quadrature)");
                EXPECT_LT(Math.Abs(meanLight - truth), 4 * errorLight + 1e-4 * truth);
                EXPECT_LT(Math.Abs(meanBoth - truth), 4 * errorBoth + 1e-4 * truth);
            }
        }

        private static DiffuseAreaLight ThermalPlate(Shape shape, FloatTexture temperature, int candidates)
        {
            DiffuseAreaLight light = new DiffuseAreaLight(new Transform(), new MediumInterface(), RGBColorSpace.sRGB.illuminant, 1, shape, null, new Image(), null,
                                                          true);
            // (the emission of a blackbody whatever the material is: the test is of the sampling)
            light.thermal = new ThermalEmission(shape, temperature, 1, false, true);
            light.thermal.candidates = candidates;
            return light;
        }

        // What a strategy records is the polarised part of what it returns; the rest is unpolarised, and a factor on the returned
        // value goes onto what was recorded since a mark
        [TEST("ThermalEmission", "PolarisedGatherKeepsTheIntensity")]
        public static void PolarisedGatherKeepsTheIntensity()
        {
            PolarisedGather gather = new PolarisedGather();
            // (light polarised by a reflection that favours one of the two directions)
            Vector3f direction = new Vector3f(0.6f, 0, 0.8f), mirrored = new Vector3f(-0.6f, 0, 0.8f), axis = new Vector3f(0, 1, 0);
            PLight polarised = PolarisedLightSystem.Attenuation.Fresnel(new SampledSpectrum(1.0f), 0.9f, 0.1f, 0, 0)
                                   .Scattered(direction, mirrored, new Vector3f(0, 0, 1), true) * new PLight(new SampledSpectrum(2.0f));
            Float recorded = polarised.GetIntensity()[0];
            PolarisedGather.Mark mark = gather.Here();
            gather.Add(new SampledSpectrum(recorded), polarised);
            gather.ScaleSince(mark, 0.5f);
            // (three in all were gathered: half of what was recorded, and the rest without polarisation)
            PLight taken = gather.Take(new SampledSpectrum(3.0f));
            EXPECT_NEAR(3.0f, taken.GetIntensity()[0], 1e-5f);
            StokesVector stokes = taken.RepresentInDirection(direction, axis).ToStokesVector();
            StokesVector whole = polarised.RepresentInDirection(direction, axis).ToStokesVector();
            EXPECT_NEAR(0.5f * whole.s1[0], stokes.s1[0], 1e-5f);
            EXPECT_NEAR(0.5f * whole.s2[0], stokes.s2[0], 1e-5f);
            EXPECT_GT(std.abs(whole.s1[0]), 0.5f);
            // (and nothing is left for the next time)
            EXPECT_NEAR(1.0f, gather.Take(new SampledSpectrum(1.0f)).GetIntensity()[0], 1e-6f);
        }

        // The light that a smooth surface emits is polarised in the plane of the direction and of the normal, at right angles
        // to the light that it reflects: I = 1 - (Fs + Fp) / 2 and Q = -(Fs - Fp) / 2 about the s axis
        [TEST("ThermalEmission", "PolarisedAcrossTheReflection")]
        public static void PolarisedAcrossTheReflection()
        {
            SampledSpectrum eta = new SampledSpectrum(0.2f), k = new SampledSpectrum(3.5f);
            BSDF conductor = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), new ConductorBxDF(new TrowbridgeReitzDistribution(0, 0), eta, k));
            BSDF dielectric = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), new DielectricBxDF(1.9f, new TrowbridgeReitzDistribution(0, 0)));
            foreach (Float cosine in new Float[] { 0.8f, 0.5f, 0.2f })
            {
                Vector3f wo = Direction(cosine);
                Vector3f sAxis = Normalize(Cross(new Vector3f(0, 0, 1), wo));
                Float Fs, Fp, Ds, Dp;

                FrComplexPolarised(cosine, new pstd.complex(eta[0], k[0]), out Fs, out Fp, out Ds, out Dp);
                ThermalEmission.Emissivity(conductor, FlatSurface(wo), wo, 0.3f, new Point2f(0.1f, 0.7f), false, true, true, out PLight glow);
                StokesVector stokes = glow.RepresentInDirection(wo, sAxis).ToStokesVector();
                EXPECT_NEAR(1 - 0.5f * (Fs + Fp), stokes.s0[0], 1e-5f);
                EXPECT_NEAR(-0.5f * (Fs - Fp), stokes.s1[0], 1e-5f);
                EXPECT_NEAR(0, stokes.s2[0], 1e-5f);
                EXPECT_NEAR(0, stokes.s3[0], 1e-5f);
                EXPECT_LT(stokes.s1[0], 0);

                FrDielectricPolarised(cosine, 1.9f, out Fs, out Fp, out Ds, out Dp);
                ThermalEmission.Emissivity(dielectric, FlatSurface(wo), wo, 0.3f, new Point2f(0.1f, 0.7f), true, true, true, out glow);
                stokes = glow.RepresentInDirection(wo, sAxis).ToStokesVector();
                EXPECT_NEAR(1 - 0.5f * (Fs + Fp), stokes.s0[0], 1e-5f);
                EXPECT_NEAR(-0.5f * (Fs - Fp), stokes.s1[0], 1e-5f);
                EXPECT_NEAR(0, stokes.s2[0], 1e-5f);

                // (a lossless glass emits nothing)
                Float none = ThermalEmission.Emissivity(dielectric, FlatSurface(wo), wo, 0.3f, new Point2f(0.1f, 0.7f), false, true, true, out glow)[0];
                EXPECT_EQ(0, none);
            }
        }
    }
}
