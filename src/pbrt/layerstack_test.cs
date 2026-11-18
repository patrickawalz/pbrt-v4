// (not in pbrt-v4) Tests of the flat stack of layers (layerstack.cs)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using testing;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public static unsafe class LayerStackTests
    {
        static LayerMedium Medium(Float thickness, Float sigmaS, Float albedo, Float g, Float sigmaA)
        {
            LayerMedium medium = new LayerMedium();
            medium.thickness = thickness;
            medium.sigmaS = sigmaS;
            medium.albedo = new SampledSpectrum(albedo);
            medium.g = g;
            medium.sigmaA = new SampledSpectrum(sigmaA);
            return medium;
        }

        static BxDF Glass(Float eta, Float alpha)
        {
            return new DielectricBxDF(eta, new TrowbridgeReitzDistribution(alpha, alpha));
        }

        static BxDF Metal(Float alpha)
        {
            return new ConductorBxDF(new TrowbridgeReitzDistribution(alpha, alpha), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f));
        }

        // The stacks of the tests: 0 to 3 are the coated materials of pbrt (the same layers as pbrt's own layered BxDF, which
        // is returned too); 4 and 5 have three interfaces (the same layers as one layered BxDF inside another); 6 lets light
        // through
        static LayerStackBxDF Stack(int kind, out BxDF reference, out string name)
        {
            reference = null;
            SampledSpectrum none = new SampledSpectrum(0f);
            switch (kind)
            {
                case 0:
                    name = "rough coat on a diffuse base";
                    reference = new CoatedDiffuseBxDF((DielectricBxDF)Glass(1.5f, 0.3f), new DiffuseBxDF(new SampledSpectrum(0.8f)), 0.01f, none, 0, 64, 1);
                    return CoatedStack.Create(Glass(1.5f, 0.3f), new DiffuseBxDF(new SampledSpectrum(0.8f)), 0.01f, none, 0, 64, 1, false);
                case 1:
                    name = "smooth coat, scattering medium, diffuse base";
                    reference = new CoatedDiffuseBxDF((DielectricBxDF)Glass(1.5f, 0f), new DiffuseBxDF(new SampledSpectrum(0.8f)), 0.5f,
                                                      new SampledSpectrum(0.7f), 0.5f, 64, 1);
                    return CoatedStack.Create(Glass(1.5f, 0f), new DiffuseBxDF(new SampledSpectrum(0.8f)), 0.5f, new SampledSpectrum(0.7f), 0.5f, 64, 1, false);
                case 2:
                    name = "smooth coat on a rough conductor";
                    reference = new CoatedConductorBxDF((DielectricBxDF)Glass(1.5f, 0f), (ConductorBxDF)Metal(0.2f), 0.01f, none, 0, 64, 1);
                    return CoatedStack.Create(Glass(1.5f, 0f), Metal(0.2f), 0.01f, none, 0, 64, 1, false);
                case 3:
                    name = "rough coat on a mirror";
                    reference = new CoatedConductorBxDF((DielectricBxDF)Glass(1.5f, 0.2f), (ConductorBxDF)Metal(0f), 0.01f, none, 0, 64, 1);
                    return CoatedStack.Create(Glass(1.5f, 0.2f), Metal(0f), 0.01f, none, 0, 64, 1, false);
                case 4:
                    name = "three interfaces: smooth, rough, diffuse";
                    // (the same layers as one layered BxDF in another: the scattering medium with pbrt's parameters, its
                    // optical thickness and the share of an event that scatters)
                    reference = StackBxDF.Absorbing(Glass(1.5f, 0f),
                                                    new StackBxDF(Glass(1.33f / 1.5f, 0.3f), new DiffuseBxDF(new SampledSpectrum(0.8f)), 0.64f,
                                                                  new SampledSpectrum(1.5f * 0.9f / 1.6f), 0.3f, 64, 1, true),
                                                    new SampledSpectrum(0.15f), 64, 1, true);
                    return new LayerStackBxDF(new BxDF[] { Glass(1.5f, 0f), Glass(1.33f / 1.5f, 0.3f), new DiffuseBxDF(new SampledSpectrum(0.8f)) },
                                              new LayerMedium[] { Medium(0.3f, 0, 0, 0, 0.5f), Medium(0.4f, 1.5f, 0.9f, 0.3f, 0.1f) }, 64, 1, true);
                case 5:
                    name = "three interfaces: rough, rough, mirror";
                    reference = StackBxDF.Absorbing(Glass(1.5f, 0.15f), StackBxDF.Absorbing(Glass(1.2f / 1.5f, 0.25f), Metal(0f), none, 64, 1, true),
                                                    new SampledSpectrum(0.02f), 64, 1, true);
                    return new LayerStackBxDF(new BxDF[] { Glass(1.5f, 0.15f), Glass(1.2f / 1.5f, 0.25f), Metal(0f) },
                                              new LayerMedium[] { Medium(0.1f, 0, 0, 0, 0.2f), Medium(0.1f, 0, 0, 0, 0f) }, 64, 1, true);
                case 7:
                    name = "smooth coat, scattering medium, mirror";
                    return new LayerStackBxDF(new BxDF[] { Glass(1.5f, 0f), Metal(0f) }, new LayerMedium[] { Medium(0.5f, 1.2f, 0.9f, 0.3f, 0.1f) }, 64, 1, true);
                case 8:
                    name = "rough coat, smooth interface, mirror";
                    return new LayerStackBxDF(new BxDF[] { Glass(1.5f, 0.25f), Glass(1.2f / 1.5f, 0f), Metal(0f) },
                                              new LayerMedium[] { Medium(0.1f, 0, 0, 0, 0.2f), Medium(0.1f, 0, 0, 0, 0f) }, 64, 1, true);
                case 9:
                    name = "a slab: smooth, scattering medium, smooth";
                    return new LayerStackBxDF(new BxDF[] { Glass(1.5f, 0f), Glass(1 / 1.5f, 0f) }, new LayerMedium[] { Medium(0.5f, 1f, 0.95f, 0.6f, 0.05f) },
                                              64, 1, false);
                default:
                    name = "a slab: rough, scattering medium, rough";
                    return new LayerStackBxDF(new BxDF[] { Glass(1.5f, 0.2f), Glass(1 / 1.5f, 0.3f) },
                                              new LayerMedium[] { Medium(0.5f, 1f, 0.95f, 0.6f, 0.05f) }, 64, 1, false);
            }
        }

        // What the BxDF returns of the light from wo with a value (not in a direction of its own), in the upper hemisphere
        // and in the lower one: by its value over uniform directions, and by its samples. Mean and error of each.
        static void Returned(BxDF bxdf, Vector3f wo, int count, ref RNG rng, double[] value, double[] valueError, double[] samples, double[] sampleError,
                             out double largest, out double all)
        {
            double[] sum = new double[4], squares = new double[4];
            largest = 0;
            all = 0;
            for (int i = 0; i < count; ++i)
            {
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                double v = bxdf.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI;
                int side = wi.z > 0 ? 0 : 1;
                sum[side] += v;
                squares[side] += v * v;
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                             TransportMode.Radiance, BxDFReflTransFlags.All);
                if (bs && bs.value().pdf > 0)
                {
                    double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                    all += w / count;
                    if (!bs.value().IsSpecular())
                    {
                        int sampledSide = bs.value().wi.z > 0 ? 2 : 3;
                        sum[sampledSide] += w;
                        squares[sampledSide] += w * w;
                        largest = Math.Max(largest, w);
                    }
                }
            }
            for (int side = 0; side < 2; ++side)
            {
                value[side] = sum[side] / count;
                valueError[side] = Math.Sqrt(Math.Max(0, squares[side] / count - value[side] * value[side]) / count);
                samples[side] = sum[side + 2] / count;
                sampleError[side] = Math.Sqrt(Math.Max(0, squares[side + 2] / count - samples[side] * samples[side]) / count);
            }
        }

        // The value of every stack is what its samples carry
        [TEST("LayerStack", "ValueIsTheSamples")]
        public static void ValueIsTheSamples()
        {
            RNG rng = new RNG(31);
            double[] value = new double[2], valueError = new double[2], samples = new double[2], sampleError = new double[2];
            for (int kind = 0; kind < 10; ++kind)
            {
                LayerStackBxDF stack = Stack(kind, out BxDF _, out string name);
                foreach (Float cosine in new Float[] { 0.9f, 0.4f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                    Returned(stack, wo, 200000, ref rng, value, valueError, samples, sampleError, out double largest, out double all);
                    for (int side = 0; side < (kind == 6 || kind == 9 ? 2 : 1); ++side)
                    {
                        Console.WriteLine("{0}, cosine {1}, {2}: {3:F4} +- {4:F4} by the value, {5:F4} +- {6:F4} by the samples (largest weight {7:F2})", name,
                                          cosine, side == 0 ? "reflected" : "transmitted", value[side], valueError[side], samples[side], sampleError[side],
                                          largest);
                        EXPECT_LT(Math.Abs(value[side] - samples[side]),
                                  4 * Math.Sqrt(valueError[side] * valueError[side] + sampleError[side] * sampleError[side]) + 0.002);
                    }
                }
            }
        }

        // The flat stack is the layers of pbrt, the coated materials and one layered BxDF inside another: the same value in every
        // part of the hemisphere, and the same samples
        [TEST("LayerStack", "IsTheLayersOfPbrt")]
        public static void IsTheLayersOfPbrt()
        {
            RNG rng = new RNG(37);
            for (int kind = 0; kind < 6; ++kind)
            {
                LayerStackBxDF stack = Stack(kind, out BxDF reference, out string name);
                foreach (Float cosine in new Float[] { 0.9f, 0.4f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                    int count = 300000, bins = 4;
                    double[] a = new double[bins], b = new double[bins], squares = new double[bins], referenceSquares = new double[bins];
                    double sampledA = 0, sampledB = 0, sampledSquares = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f wi = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        // (four parts: towards the mirror direction or away from it, high or low)
                        int bin = (wi.x < 0 ? 0 : 1) + (wi.z > 0.5f ? 0 : 2);
                        double x = stack.f(wo, wi, TransportMode.Radiance)[0] * wi.z * 2 * Math.PI;
                        double y = reference.f(wo, wi, TransportMode.Radiance)[0] * wi.z * 2 * Math.PI;
                        a[bin] += x;
                        b[bin] += y;
                        squares[bin] += x * x;
                        referenceSquares[bin] += y * y;
                        Float uc = rng.Uniform<Float>();
                        Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                        pstd.optional<BSDFSample> s = stack.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                        pstd.optional<BSDFSample> t = reference.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                        double p = s && s.value().pdf > 0 ? s.value().f[0] * AbsCosTheta(s.value().wi) / s.value().pdf : 0;
                        double q = t && t.value().pdf > 0 ? t.value().f[0] * AbsCosTheta(t.value().wi) / t.value().pdf : 0;
                        sampledA += p;
                        sampledB += q;
                        sampledSquares += p * p + q * q;
                    }
                    string line = "";
                    for (int bin = 0; bin < bins; ++bin)
                    {
                        double x = a[bin] / count, y = b[bin] / count;
                        double error = Math.Sqrt((Math.Max(0, squares[bin] / count - x * x) + Math.Max(0, referenceSquares[bin] / count - y * y)) / count);
                        line += string.Format(" {0:F4}/{1:F4}", x, y);
                        EXPECT_LT(Math.Abs(x - y), 4 * error + 0.0005);
                    }
                    double all = sampledA / count, allReference = sampledB / count;
                    double allError = Math.Sqrt(Math.Max(0, sampledSquares / count - all * all - allReference * allReference) / count);
                    Console.WriteLine("{0}, cosine {1}: value in four parts, flat/pbrt{2}; all the samples {3:F4}/{4:F4} +- {5:F4}", name, cosine, line, all,
                                      allReference, allError);
                    EXPECT_LT(Math.Abs(all - allReference), 4 * allError + 0.002);
                }
            }
        }

        // Nothing absorbs: all the light comes back, from two layers and from three
        [TEST("LayerStack", "Conserves")]
        public static void Conserves()
        {
            RNG rng = new RNG(41);
            SampledSpectrum white = new SampledSpectrum(1f);
            BxDF[] stacks = new BxDF[]
            {
                // (smooth interfaces: pbrt's rough dielectric follows one reflection on its facets and loses the rest)
                new LayerStackBxDF(new BxDF[] { Glass(1.5f, 0f), new DiffuseBxDF(white) }, new LayerMedium[] { Medium(0.1f, 0, 0, 0, 0) }, 2000, 1, true),
                new LayerStackBxDF(new BxDF[] { Glass(1.5f, 0f), Glass(1.33f / 1.5f, 0f), new DiffuseBxDF(white) },
                                   new LayerMedium[] { Medium(0.1f, 0, 0, 0, 0), Medium(0.5f, 2f, 1f, 0.4f, 0) }, 2000, 1, true),
                new LayerStackBxDF(new BxDF[] { Glass(1.5f, 0f), Glass(1 / 1.5f, 0f) }, new LayerMedium[] { Medium(0.5f, 1f, 1f, 0.6f, 0) }, 2000, 1, false),
            };
            for (int k = 0; k < stacks.Length; ++k)
            {
                foreach (Float cosine in new Float[] { 0.9f, 0.3f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                    int count = 100000;
                    double all = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        pstd.optional<BSDFSample> bs = stacks[k].Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                          TransportMode.Radiance, BxDFReflTransFlags.All);
                        if (bs && bs.value().pdf > 0)
                        {
                            all += bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / count;
                        }
                    }
                    Console.WriteLine("stack {0}, cosine {1}: returns {2:F4}", k, cosine, all);
                    EXPECT_LT(Math.Abs(all - 1), 0.01);
                }
            }
        }

        // The first element of a matrix and the two that say how it polarises light that was not, in the frames that the two
        // directions give (the same for every BxDF)
        static void Elements(Attenuation A, Vector3f wo, Vector3f wi, out double first, out double second, out double third)
        {
            first = 1;
            second = 0;
            third = 0;
            if (A.WithFrames(wo, -wi))
            {
                MuellerMatrix m = A.GetMuellerMatrix();
                first = m.Get(0, 0)[0];
                second = m.Get(1, 0)[0];
                third = m.Get(2, 0)[0];
            }
        }

        // With polarisation. The first element of the matrix, which is what the polarisation between the layers does to the
        // brightness, and the elements that polarise light that was not, weighted by the value over the two halves of the
        // hemisphere and by the samples, are those of pbrt's layered BxDF with the same layers. The scalar value is the same
        // with and without the matrix. And a smooth coat on a mirror, where every path is known, has the reflectances of the
        // two polarisations each summed over its own reflections.
        [TEST("LayerStack", "Polarised")]
        public static void Polarised()
        {
            RNG rng = new RNG(47);
            for (int kind = 0; kind < 6; ++kind)
            {
                LayerStackBxDF stack = Stack(kind, out BxDF reference, out string name);
                BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), stack), referenceFrame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), reference);
                Vector3f wo = Normalize(new Vector3f(0.7f, 0.1f, 0.5f));
                int count = 200000;
                // (0: the first element; 1 to 4: the second and the third element in each half; then the same by the samples)
                double[] a = new double[10], b = new double[10], squaresA = new double[10], squaresB = new double[10];
                double scalarWith = 0, scalarWithout = 0, scalarSquares = 0;
                for (int i = 0; i < count; ++i)
                {
                    Vector3f wi = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Float uc = rng.Uniform<Float>();
                    Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    for (int which = 0; which < 2; ++which)
                    {
                        BxDF bxdf = which == 0 ? stack : reference;
                        double[] sum = which == 0 ? a : b, squares = which == 0 ? squaresA : squaresB;
                        SampledSpectrum value = bxdf.f(which == 0 ? frame : referenceFrame, wo, wi, TransportMode.Radiance, out bool polarised, out Attenuation A);
                        if (which == 0)
                        {
                            // (the walk with polarisation is another walk, its roulette going by what it carries: the same in the mean)
                            double plain = stack.f(wo, wi, TransportMode.Radiance)[0] * wi.z * 2 * Math.PI, with = value[0] * wi.z * 2 * Math.PI;
                            scalarWith += with;
                            scalarWithout += plain;
                            scalarSquares += with * with + plain * plain;
                        }
                        double weight = value[0] * wi.z * 2 * Math.PI, first = 1, second = 0, third = 0;
                        if (polarised)
                        {
                            Elements(A, wo, wi, out first, out second, out third);
                        }
                        int half = wi.y > 0 ? 1 : 3;
                        sum[0] += weight * first; squares[0] += weight * first * weight * first;
                        sum[half] += weight * second; squares[half] += weight * second * weight * second;
                        sum[half + 1] += weight * third; squares[half + 1] += weight * third * weight * third;
                        pstd.optional<BSDFSample> bs = bxdf.Sample_f(which == 0 ? frame : referenceFrame, wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All,
                                                                     out polarised, out A);
                        if (bs && bs.value().pdf > 0)
                        {
                            weight = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                            first = 1;
                            second = 0;
                            third = 0;
                            if (polarised)
                            {
                                Elements(A, wo, bs.value().wi, out first, out second, out third);
                            }
                            half = bs.value().wi.y > 0 ? 6 : 8;
                            sum[5] += weight * first; squares[5] += weight * first * weight * first;
                            sum[half] += weight * second; squares[half] += weight * second * weight * second;
                            sum[half + 1] += weight * third; squares[half + 1] += weight * third * weight * third;
                        }
                    }
                }
                string line = "";
                for (int k = 0; k < 10; ++k)
                {
                    double x = a[k] / count, y = b[k] / count;
                    double error = Math.Sqrt((Math.Max(0, squaresA[k] / count - x * x) + Math.Max(0, squaresB[k] / count - y * y)) / count);
                    line += string.Format("{0}{1:F4}/{2:F4}", k == 5 ? "; samples " : " ", x, y);
                    EXPECT_LT(Math.Abs(x - y), 4 * error + 0.0005);
                }
                double meanWith = scalarWith / count, meanWithout = scalarWithout / count;
                double scalarError = Math.Sqrt(Math.Max(0, scalarSquares / count - meanWith * meanWith - meanWithout * meanWithout) / count);
                Console.WriteLine("{0}: flat/pbrt, first element, then the polarising elements in each half: value{1}; scalar value with the matrix {2:F4}, " +
                                  "without {3:F4}", name, line, meanWith, meanWithout);
                EXPECT_LT(Math.Abs(meanWith - meanWithout), 4 * scalarError + 0.0005);
            }

            // A smooth coat on a mirror
            foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.25f })
            {
                LayerStackBxDF stack = new LayerStackBxDF(new BxDF[] { Glass(1.5f, 0f), Metal(0f) }, new LayerMedium[] { Medium(0.1f, 0, 0, 0, 0) }, 2000, 1, true);
                BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), stack);
                Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                int count = 200000;
                double first = 0, part = 0;
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<BSDFSample> bs = stack.Sample_f(frame, wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                  TransportMode.Radiance, BxDFReflTransFlags.All, out bool polarised, out Attenuation A);
                    if (bs && bs.value().pdf > 0)
                    {
                        EXPECT_TRUE(polarised);
                        double weight = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / count;
                        Elements(A, wo, bs.value().wi, out double m00, out double m10, out double m20);
                        first += weight * m00;
                        // (every sample is in the mirror direction: with the same frames, the element itself)
                        part += weight * Math.Sqrt(m10 * m10 + m20 * m20) * Math.Sign(Math.Abs(m10) > Math.Abs(m20) ? m10 : m20);
                    }
                }
                FrDielectricPolarised(cosine, 1.5f, out Float fs, out Float fp, out Float _, out Float _);
                Float inside = SafeSqrt(1 - (1 - cosine * cosine) / (1.5f * 1.5f));
                FrComplexPolarised(inside, new pstd.complex(0.2f, 3.5f), out Float ms, out Float mp, out Float _, out Float _);
                double rs = fs + (1 - fs) * (1 - fs) * ms / (1 - fs * ms), rp = fp + (1 - fp) * (1 - fp) * mp / (1 - fp * mp);
                double mean = 0.5 * (fs + fp), meanMirror = 0.5 * (ms + mp);
                double scalar = mean + (1 - mean) * (1 - mean) * meanMirror / (1 - mean * meanMirror);
                Console.WriteLine("smooth coat on a mirror, cosine {0}: reflects {1:F4} (each polarisation summed apart: {2:F4}; by the means: {3:F4}), " +
                                  "polarising part {4:F4} ({5:F4})", cosine, first, 0.5 * (rs + rp), scalar, Math.Abs(part), 0.5 * Math.Abs(rs - rp));
                // (the polarising part is a small difference of large terms: its error is 0.0008 with these samples)
                EXPECT_LT(Math.Abs(first - 0.5 * (rs + rp)), 0.004);
                EXPECT_LT(Math.Abs(Math.Abs(part) - 0.5 * Math.Abs(rs - rp)), 0.004);
            }
        }

        // The closed-form density: positive wherever the stack has a value, about one over the sphere, and close enough to the
        // samples that weighting by it works (the variance of value over density, against that of pbrt's estimated density).
        // And the cost of each operation against pbrt's layered BxDF.
        [TEST("LayerStack", "DensityAndCost")]
        public static void DensityAndCost()
        {
            RNG rng = new RNG(43);
            for (int kind = 0; kind < 7; ++kind)
            {
                LayerStackBxDF stack = Stack(kind, out BxDF reference, out string name);
                Vector3f wo = Normalize(new Vector3f(0.5f, 0.2f, 0.8f));
                int count = 100000, missed = 0;
                double integral = 0, ratio = 0, ratioSquares = 0, referenceRatio = 0, referenceSquares = 0;
                for (int i = 0; i < count; ++i)
                {
                    Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Float density = stack.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All);
                    integral += density * 4 * Math.PI / count;
                    Float value = stack.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi);
                    if (value > 0 && !(density > 0))
                    {
                        ++missed;
                    }
                    // (what a path tracer would add for a direction drawn from the density: the spread of value over density)
                    pstd.optional<BSDFSample> bs = stack.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                  TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (bs && bs.value().pdf > 0 && !bs.value().IsSpecular())
                    {
                        Float at = stack.PDF(wo, bs.value().wi, TransportMode.Radiance, BxDFReflTransFlags.All);
                        Float valueAt = stack.f(wo, bs.value().wi, TransportMode.Radiance)[0] * AbsCosTheta(bs.value().wi);
                        if (at > 0)
                        {
                            double x = valueAt / at;
                            ratio += x;
                            ratioSquares += x * x;
                        }
                        if (reference != null)
                        {
                            Float referenceAt = reference.PDF(wo, bs.value().wi, TransportMode.Radiance, BxDFReflTransFlags.All);
                            Float referenceValue = reference.f(wo, bs.value().wi, TransportMode.Radiance)[0] * AbsCosTheta(bs.value().wi);
                            if (referenceAt > 0)
                            {
                                double y = referenceValue / referenceAt;
                                referenceRatio += y;
                                referenceSquares += y * y;
                            }
                        }
                    }
                }
                double mean = ratio / count, spread = Math.Sqrt(Math.Max(0, ratioSquares / count - mean * mean));
                double referenceMean = referenceRatio / count, referenceSpread = Math.Sqrt(Math.Max(0, referenceSquares / count - referenceMean * referenceMean));
                Console.WriteLine("{0}: density over the sphere {1:F3}; value over density at the samples {2:F3} with spread {3:F3}{4}", name, integral, mean,
                                  spread, reference != null ? string.Format(" (pbrt, with its own density: {0:F3}, spread {1:F3})", referenceMean, referenceSpread)
                                                            : "");
                EXPECT_EQ(0, missed);
                EXPECT_GT(integral, 0.3);
                EXPECT_LT(integral, 1.5);

                // The cost
                if (reference != null)
                {
                    int calls = 200000;
                    Vector3f[] directions = new Vector3f[calls];
                    for (int i = 0; i < calls; ++i)
                    {
                        directions[i] = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    }
                    double[] seconds = new double[6];
                    double sink = 0;
                    for (int which = 0; which < 2; ++which)
                    {
                        BxDF bxdf = which == 0 ? stack : reference;
                        Stopwatch watch = Stopwatch.StartNew();
                        for (int i = 0; i < calls; ++i)
                        {
                            sink += bxdf.f(wo, directions[i], TransportMode.Radiance)[0];
                        }
                        seconds[which * 3] = watch.Elapsed.TotalSeconds;
                        watch.Restart();
                        for (int i = 0; i < calls; ++i)
                        {
                            pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, directions[i].x * 0.5f + 0.5f, new Point2f(directions[i].y * 0.5f + 0.5f, directions[i].z),
                                                                         TransportMode.Radiance, BxDFReflTransFlags.All);
                            sink += bs ? 1 : 0;
                        }
                        seconds[which * 3 + 1] = watch.Elapsed.TotalSeconds;
                        watch.Restart();
                        for (int i = 0; i < calls; ++i)
                        {
                            sink += bxdf.PDF(wo, directions[i], TransportMode.Radiance, BxDFReflTransFlags.All);
                        }
                        seconds[which * 3 + 2] = watch.Elapsed.TotalSeconds;
                    }
                    Console.WriteLine("{0}: microseconds per call, flat/pbrt: value {1:F2}/{2:F2}, sample {3:F2}/{4:F2}, density {5:F2}/{6:F2} ({7})", name,
                                      seconds[0] / calls * 1e6, seconds[3] / calls * 1e6, seconds[1] / calls * 1e6, seconds[4] / calls * 1e6,
                                      seconds[2] / calls * 1e6, seconds[5] / calls * 1e6, sink > 0);
                }
            }
        }
    }
}
