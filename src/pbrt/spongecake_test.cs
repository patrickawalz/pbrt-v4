// (not in pbrt-v4) Tests of the layers of microflakes (spongecake.cs)

using System;
using System.Collections.Generic;
using testing;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public static unsafe class SpongeCakeTests
    {
        static FlakeLayer Layer(FlakeKind kind, Float alpha, Float thickness, Vector3f axis, Float albedo, Float f0)
        {
            FlakeLayer layer = new FlakeLayer();
            layer.kind = kind;
            layer.alpha = alpha;
            layer.thickness = thickness;
            layer.axis = Normalize(axis);
            layer.g = 0.4f;
            layer.albedo = new SampledSpectrum(albedo);
            Float r = Math.Min(f0, 0.9999f);
            layer.flakeK = new SampledSpectrum(2 * MathF.Sqrt(r) / MathF.Sqrt(1 - r));
            return layer;
        }

        // The cakes of the tests
        static SpongeCakeBxDF Cake(int which, bool single, out string name)
        {
            Vector3f up = new Vector3f(0, 0, 1);
            switch (which)
            {
                case 0:
                    name = "one layer of flakes";
                    return new SpongeCakeBxDF(new FlakeLayer[] { Layer(FlakeKind.Surface, 0.4f, 1.5f, up, 0.8f, 0.9f) }, null, single, true, 200, 1);
                case 1:
                    name = "flakes over tilted fibres";
                    return new SpongeCakeBxDF(new FlakeLayer[] { Layer(FlakeKind.Surface, 0.5f, 0.5f, up, 0.9f, 1f),
                                                                 Layer(FlakeKind.Fiber, 0.3f, 2f, new Vector3f(1, 0.3f, 0.2f), 0.7f, 0.8f) },
                                              null, single, true, 200, 1);
                case 2:
                    name = "fibres on a diffuse base";
                    return new SpongeCakeBxDF(new FlakeLayer[] { Layer(FlakeKind.Fiber, 0.2f, 0.8f, new Vector3f(1, 0, 0), 0.8f, 1f) },
                                              new DiffuseBxDF(new SampledSpectrum(0.6f)), single, true, 200, 1);
                case 4:
                    name = "flakes on a mirror";
                    return new SpongeCakeBxDF(new FlakeLayer[] { Layer(FlakeKind.Surface, 0.4f, 0.7f, up, 0.9f, 0.8f) },
                                              new ConductorBxDF(new TrowbridgeReitzDistribution(0, 0), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)),
                                              single, true, 200, 1);
                case 5:
                    name = "fibres on a rough conductor";
                    return new SpongeCakeBxDF(new FlakeLayer[] { Layer(FlakeKind.Fiber, 0.3f, 0.6f, new Vector3f(1, 0.5f, 0), 0.9f, 1f) },
                                              new ConductorBxDF(new TrowbridgeReitzDistribution(0.3f, 0.3f), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)),
                                              single, true, 200, 1);
                default:
                    name = "flakes, a Henyey-Greenstein layer, flakes";
                    return new SpongeCakeBxDF(new FlakeLayer[] { Layer(FlakeKind.Surface, 0.2f, 0.3f, up, 1f, 0.5f),
                                                                 Layer(FlakeKind.HenyeyGreenstein, 1f, 1f, up, 0.8f, 1f),
                                                                 Layer(FlakeKind.Surface, 0.6f, 0.4f, new Vector3f(0.2f, 0, 1), 0.9f, 1f) },
                                              null, single, true, 200, 1);
            }
        }

        // The SGGX flakes: the projected area is the integral of the distribution over the normals that the direction sees,
        // the phase function integrates to one, and the sampled directions have the phase function as their density
        [TEST("SpongeCake", "Flakes")]
        public static void Flakes()
        {
            RNG rng = new RNG(5);
            Vector3f axis = new Vector3f(0.3f, -0.2f, 1);
            foreach (FlakeKind kind in new FlakeKind[] { FlakeKind.Surface, FlakeKind.Fiber })
            {
                foreach (Float alpha in new Float[] { 0.3f, 0.6f, 1f })
                {
                    FlakeLayer layer = Layer(kind, alpha, 1, axis, 1, 1);
                    Vector3f a = Normalize(new Vector3f(0.5f, 0.4f, 0.6f)), probe = Normalize(new Vector3f(-0.3f, 0.8f, 0.4f));
                    int count = 400000;
                    double area = 0, one = 0, first = 0, second = 0, sampledFirst = 0, sampledSecond = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f v = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        area += layer.D(v) * Math.Max(0, Dot(a, v)) * 4 * Math.PI / count;
                        double phase = layer.Phase(a, v) * 4 * Math.PI / count;
                        one += phase;
                        first += phase * Dot(v, probe);
                        second += phase * v.z * v.z;
                        Vector3f b = Reflect(a, layer.SampleNormal(a, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
                        sampledFirst += Dot(b, probe) / count;
                        sampledSecond += b.z * b.z / (double)count;
                    }
                    Console.WriteLine("{0} alpha {1}: projected area {2:F4} (integral {3:F4}); phase function integrates to {4:F4}; moments of the samples " +
                                      "{5:F4} {6:F4} (of the phase function {7:F4} {8:F4})", kind, alpha, layer.Sigma(a), area, one, sampledFirst, sampledSecond,
                                      first, second);
                    EXPECT_LT(Math.Abs(area - layer.Sigma(a)), 0.02 * layer.Sigma(a));
                    EXPECT_LT(Math.Abs(one - 1), 0.02);
                    EXPECT_LT(Math.Abs(first - sampledFirst), 0.02);
                    EXPECT_LT(Math.Abs(second - sampledSecond), 0.02);
                }
            }
        }

        // The closed form. Isotropic flakes in a deep layer are Chandrasekhar's half space, a / (4 pi (cos + cos)) (the paper's
        // equation 13). The value is the same with the two directions exchanged, in reflection and in transmission. And the
        // closed form is what a walk that stops at its first scattering carries, on each side of the layers and with a base;
        // the light that meets no flake is what the layers let through.
        [TEST("SpongeCake", "SingleScattering")]
        public static void SingleScattering()
        {
            RNG rng = new RNG(6);
            SpongeCakeBxDF deep = new SpongeCakeBxDF(new FlakeLayer[] { Layer(FlakeKind.Surface, 1f, 60f, new Vector3f(0, 0, 1), 0.7f, 1f) }, null, true,
                                                     true, 64, 1);
            double worst = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Vector3f wo = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f wi = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (wo.z < 0.05f || wi.z < 0.05f)
                {
                    continue;
                }
                double expected = 0.7 / (4 * Math.PI * (wo.z + wi.z));
                worst = Math.Max(worst, Math.Abs(deep.f(wo, wi, TransportMode.Radiance)[0] / expected - 1));
            }
            Console.WriteLine("isotropic half space against Chandrasekhar: largest relative difference {0:E2}", worst);
            EXPECT_LT(worst, 2e-3);

            for (int which = 0; which < 6; ++which)
            {
                SpongeCakeBxDF cake = Cake(which, true, out string name);
                double reciprocity = 0;
                for (int i = 0; i < 3000; ++i)
                {
                    Vector3f a = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f b = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (Math.Abs(a.z) < 0.05f || Math.Abs(b.z) < 0.05f)
                    {
                        continue;
                    }
                    Float there = cake.f(a, b, TransportMode.Radiance)[0], back = cake.f(b, a, TransportMode.Radiance)[0];
                    if (there + back > 1e-6f)
                    {
                        reciprocity = Math.Max(reciprocity, Math.Abs(there - back) / (there + back));
                    }
                }
                EXPECT_LT(reciprocity, 1e-3);
                foreach (Float cosine in new Float[] { 0.9f, 0.35f })
                {
                    Vector3f wo = Normalize(new Vector3f(SafeSqrt(1 - cosine * cosine), 0.1f, cosine));
                    int count = 300000;
                    double[] value = new double[2], valueSquares = new double[2], samples = new double[2], sampleSquares = new double[2];
                    double straight = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        double v = cake.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI;
                        int side = wi.z > 0 ? 0 : 1;
                        value[side] += v;
                        valueSquares[side] += v * v;
                        pstd.optional<BSDFSample> bs = cake.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                     TransportMode.Radiance, BxDFReflTransFlags.All);
                        if (bs && bs.value().pdf > 0)
                        {
                            double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                            if (bs.value().IsSpecular())
                            {
                                straight += w / count;
                            }
                            else
                            {
                                side = bs.value().wi.z > 0 ? 0 : 1;
                                samples[side] += w;
                                sampleSquares[side] += w * w;
                            }
                        }
                    }
                    string line = "";
                    for (int side = 0; side < 2; ++side)
                    {
                        double x = value[side] / count, y = samples[side] / count;
                        double error = Math.Sqrt((Math.Max(0, valueSquares[side] / count - x * x) + Math.Max(0, sampleSquares[side] / count - y * y)) / count);
                        line += string.Format(" {0} {1:F4} +- {2:F4} / {3:F4}", side == 0 ? "up" : "down", x, error, y);
                        EXPECT_LT(Math.Abs(x - y), 4 * error + 0.0005);
                    }
                    Console.WriteLine("{0}, cosine {1}: closed form / walk with one scattering:{2}; unscattered {3:F4}; exchange of directions {4:E1}", name,
                                      cosine, line, straight, reciprocity);
                }
            }
        }

        // With every scattering: the value is what the samples carry; and flakes that absorb nothing return all the light
        // (the white furnace)
        [TEST("SpongeCake", "MultipleScattering")]
        public static void MultipleScattering()
        {
            RNG rng = new RNG(7);
            for (int which = 0; which < 6; ++which)
            {
                SpongeCakeBxDF cake = Cake(which, false, out string name);
                SpongeCakeBxDF first = Cake(which, true, out string _);
                foreach (Float cosine in new Float[] { 0.9f, 0.35f })
                {
                    Vector3f wo = Normalize(new Vector3f(SafeSqrt(1 - cosine * cosine), 0.1f, cosine));
                    int count = 300000;
                    double[] value = new double[2], valueSquares = new double[2], samples = new double[2], sampleSquares = new double[2], once = new double[2];
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        double v = cake.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI;
                        int side = wi.z > 0 ? 0 : 1;
                        value[side] += v;
                        valueSquares[side] += v * v;
                        once[side] += first.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI / count;
                        pstd.optional<BSDFSample> bs = cake.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                     TransportMode.Radiance, BxDFReflTransFlags.All);
                        if (bs && bs.value().pdf > 0 && !bs.value().IsSpecular())
                        {
                            double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                            side = bs.value().wi.z > 0 ? 0 : 1;
                            samples[side] += w;
                            sampleSquares[side] += w * w;
                        }
                    }
                    string line = "";
                    for (int side = 0; side < 2; ++side)
                    {
                        double x = value[side] / count, y = samples[side] / count;
                        double error = Math.Sqrt((Math.Max(0, valueSquares[side] / count - x * x) + Math.Max(0, sampleSquares[side] / count - y * y)) / count);
                        line += string.Format(" {0} {1:F4} +- {2:F4} / {3:F4} (once {4:F4})", side == 0 ? "up" : "down", x, error, y, once[side]);
                        EXPECT_LT(Math.Abs(x - y), 4 * error + 0.0005);
                    }
                    Console.WriteLine("{0}, cosine {1}: value / samples:{2}", name, cosine, line);
                }
            }

            // The white furnace
            Vector3f up = new Vector3f(0, 0, 1);
            SpongeCakeBxDF[] white = new SpongeCakeBxDF[]
            {
                new SpongeCakeBxDF(new FlakeLayer[] { Layer(FlakeKind.Surface, 0.4f, 1.5f, up, 1f, 1f) }, null, false, true, 4000, 1),
                new SpongeCakeBxDF(new FlakeLayer[] { Layer(FlakeKind.Fiber, 0.3f, 1f, new Vector3f(1, 0.2f, 0.1f), 1f, 1f),
                                                      Layer(FlakeKind.HenyeyGreenstein, 1f, 2f, up, 1f, 1f) }, null, false, true, 4000, 1),
                new SpongeCakeBxDF(new FlakeLayer[] { Layer(FlakeKind.Surface, 0.7f, 2f, up, 1f, 1f) }, new DiffuseBxDF(new SampledSpectrum(1f)), false, true,
                                   4000, 1),
            };
            for (int k = 0; k < white.Length; ++k)
            {
                foreach (Float cosine in new Float[] { 0.9f, 0.3f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                    int count = 100000;
                    double all = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        pstd.optional<BSDFSample> bs = white[k].Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                         TransportMode.Radiance, BxDFReflTransFlags.All);
                        if (bs && bs.value().pdf > 0)
                        {
                            all += bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf / count;
                        }
                    }
                    Console.WriteLine("white cake {0}, cosine {1}: returns {2:F4}", k, cosine, all);
                    // (the flakes reflect 0.9999 of the light)
                    EXPECT_LT(Math.Abs(all - 1), 0.01);
                }
            }
        }

        // With polarisation. One scattering by a flake polarises as the mirror that the flake is: the degree of polarisation
        // of the closed form is that of the Fresnel reflectances at the half vector. With every scattering, the first element
        // of the matrix weighted by the value is the one weighted by the samples, and the brightness is that of the walk
        // without polarisation.
        [TEST("SpongeCake", "Polarised")]
        public static void Polarised()
        {
            RNG rng = new RNG(8);
            FlakeLayer flakes = Layer(FlakeKind.Surface, 0.5f, 1f, new Vector3f(0, 0, 1), 0.9f, 0.3f);
            SpongeCakeBxDF thin = new SpongeCakeBxDF(new FlakeLayer[] { flakes }, null, true, true, 64, 1);
            BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), thin);
            double worst = 0, most = 0;
            for (int i = 0; i < 3000; ++i)
            {
                Vector3f a = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f b = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (a.z < 0.1f || Math.Abs(b.z) < 0.1f)
                {
                    continue;
                }
                SampledSpectrum value = thin.f(frame, a, b, TransportMode.Radiance, out bool polarised, out Attenuation A);
                EXPECT_EQ(value[0], thin.f(a, b, TransportMode.Radiance)[0]);
                if (!polarised || !(value[0] > 0))
                {
                    continue;
                }
                MuellerMatrix m = A.GetMuellerMatrix();
                double degree = Math.Sqrt(m.Get(1, 0)[0] * m.Get(1, 0)[0] + m.Get(2, 0)[0] * m.Get(2, 0)[0]) / m.Get(0, 0)[0];
                flakes.Fresnel(Dot(a, Normalize(a + b)), out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _, out SampledSpectrum _);
                double expected = Math.Abs(Fs[0] - Fp[0]) / (Fs[0] + Fp[0]);
                worst = Math.Max(worst, Math.Abs(degree - expected));
                most = Math.Max(most, expected);
                EXPECT_LT(Math.Abs(m.Get(0, 0)[0] - 1), 1e-3);
            }
            Console.WriteLine("one scattering: degree of polarisation against that of the flake, largest difference {0:E2} (degrees up to {1:F2})", worst, most);
            EXPECT_LT(worst, 2e-3);
            EXPECT_GT(most, 0.4);

            for (int which = 0; which < 6; ++which)
            {
                SpongeCakeBxDF cake = Cake(which, false, out string name);
                BSDF cakeFrame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), cake);
                Vector3f wo = Normalize(new Vector3f(0.6f, 0.2f, 0.5f));
                int count = 200000;
                // (0: value times first element; 1: samples times first element; 2: value without; 3: samples without)
                double[] sum = new double[4], squares = new double[4];
                for (int i = 0; i < count; ++i)
                {
                    Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    SampledSpectrum value = cake.f(cakeFrame, wo, wi, TransportMode.Radiance, out bool polarised, out Attenuation A);
                    double v = value[0] * AbsCosTheta(wi) * 4 * Math.PI * (polarised ? A.GetMuellerMatrix().Get(0, 0)[0] : 1);
                    sum[0] += v;
                    squares[0] += v * v;
                    double plain = cake.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI;
                    sum[2] += plain;
                    squares[2] += plain * plain;
                    Float uc = rng.Uniform<Float>();
                    Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    pstd.optional<BSDFSample> bs = cake.Sample_f(cakeFrame, wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All, out polarised, out A);
                    if (bs && bs.value().pdf > 0 && !bs.value().IsSpecular())
                    {
                        double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf * (polarised ? A.GetMuellerMatrix().Get(0, 0)[0] : 1);
                        sum[1] += w;
                        squares[1] += w * w;
                    }
                    bs = cake.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (bs && bs.value().pdf > 0 && !bs.value().IsSpecular())
                    {
                        double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                        sum[3] += w;
                        squares[3] += w * w;
                    }
                }
                double[] mean = new double[4], error = new double[4];
                for (int k = 0; k < 4; ++k)
                {
                    mean[k] = sum[k] / count;
                    error[k] = Math.Sqrt(Math.Max(0, squares[k] / count - mean[k] * mean[k]) / count);
                }
                Console.WriteLine("{0}: with polarisation {1:F4} +- {2:F4} by the value, {3:F4} +- {4:F4} by the samples; without {5:F4} and {6:F4}", name,
                                  mean[0], error[0], mean[1], error[1], mean[2], mean[3]);
                EXPECT_LT(Math.Abs(mean[0] - mean[1]), 4 * Math.Sqrt(error[0] * error[0] + error[1] * error[1]) + 0.0005);
            }
        }
    }
}
