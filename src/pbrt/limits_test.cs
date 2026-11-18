// (not in pbrt-v4) Tests of what the layered materials gained last: films that absorb on a conductor, the polarisation of the
// one-facet Bragg layer, the layers of flakes on a dielectric, and interfaces that scatter more than once in a stack

using System;
using System.Collections.Generic;
using testing;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public static unsafe class LayerLimitTests
    {
        // The reflectance of films on a substrate of complex index, by their characteristic matrices (Abeles), in double precision
        static double Characteristic(double cosine, Float[] eta, Float[] k, Float[] thickness, double wavelength, System.Numerics.Complex substrate,
                                     bool perpendicular)
        {
            System.Numerics.Complex i = new System.Numerics.Complex(0, 1);
            double snell = Math.Sqrt(1 - cosine * cosine);
            System.Numerics.Complex ratioSubstrate = snell / substrate;
            System.Numerics.Complex cosineSubstrate = System.Numerics.Complex.Sqrt(1 - ratioSubstrate * ratioSubstrate);
            if (cosineSubstrate.Imaginary > 0)
            {
                cosineSubstrate = System.Numerics.Complex.Conjugate(cosineSubstrate);
            }
            System.Numerics.Complex b = 1, c = perpendicular ? substrate * cosineSubstrate : substrate / cosineSubstrate;
            double admittance = perpendicular ? cosine : 1 / cosine;
            for (int layer = eta.Length - 1; layer >= 0; --layer)
            {
                System.Numerics.Complex index = new System.Numerics.Complex(eta[layer], -k[layer]);
                System.Numerics.Complex ratio = snell / index;
                System.Numerics.Complex cosineIn = System.Numerics.Complex.Sqrt(1 - ratio * ratio);
                if (cosineIn.Imaginary > 0)
                {
                    cosineIn = System.Numerics.Complex.Conjugate(cosineIn);
                }
                System.Numerics.Complex tilted = perpendicular ? index * cosineIn : index / cosineIn;
                System.Numerics.Complex turn = 2 * Math.PI * index * (double)thickness[layer] * cosineIn / wavelength;
                System.Numerics.Complex cosTurn = System.Numerics.Complex.Cos(turn), sinTurn = System.Numerics.Complex.Sin(turn);
                System.Numerics.Complex nextB = cosTurn * b + i * sinTurn / tilted * c;
                System.Numerics.Complex nextC = i * tilted * sinTurn * b + cosTurn * c;
                b = nextB;
                c = nextC;
            }
            System.Numerics.Complex r = (admittance * b - c) / (admittance * b + c);
            return r.Real * r.Real + r.Imaginary * r.Imaginary;
        }

        // Films that absorb on a conductor: what the thin-film materials are given is the films written out
        [TEST("LayerLimits", "AbsorbingFilmsOnConductor")]
        public static void AbsorbingFilmsOnConductor()
        {
            RNG rng = new RNG(5);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.61f);
            double worst = 0, most = 0;
            for (int trial = 0; trial < 2000; ++trial)
            {
                int count = 1 + (int)(rng.Uniform<Float>() * 3);
                Float[] eta = new Float[count], k = new Float[count], thickness = new Float[count];
                for (int j = 0; j < count; ++j)
                {
                    eta[j] = Lerp(rng.Uniform<Float>(), 1.2f, 2.4f);
                    k[j] = (Float)Math.Pow(10, Lerp(rng.Uniform<Float>(), -3f, -0.3f));
                    thickness[j] = Lerp(rng.Uniform<Float>(), 40f, 400f);
                }
                Float cosine = Lerp(rng.Uniform<Float>(), 0.05f, 1f);
                Float substrateEta = Lerp(rng.Uniform<Float>(), 0.2f, 2f), substrateK = Lerp(rng.Uniform<Float>(), 1f, 5f);
                ThinFilmStack film = new ThinFilmStack(lambda, eta, thickness);
                film.Conductor(cosine, new SampledSpectrum(substrateEta), new SampledSpectrum(substrateK), out SampledSpectrum clearS,
                               out SampledSpectrum clearP, out SampledSpectrum _, out SampledSpectrum _);
                film.layerKAt = new Float[NSpectrumSamples][];
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    film.layerKAt[c] = k;
                }
                film.Conductor(cosine, new SampledSpectrum(substrateEta), new SampledSpectrum(substrateK), out SampledSpectrum Fs, out SampledSpectrum Fp,
                               out SampledSpectrum _, out SampledSpectrum _);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    System.Numerics.Complex substrate = new System.Numerics.Complex(substrateEta, -substrateK);
                    double Rs = Characteristic(cosine, eta, k, thickness, lambda[c], substrate, true);
                    double Rp = Characteristic(cosine, eta, k, thickness, lambda[c], substrate, false);
                    worst = Math.Max(worst, Math.Max(Math.Abs(Rs - Fs[c]), Math.Abs(Rp - Fp[c])));
                    most = Math.Max(most, Math.Abs(clearS[c] - Fs[c]));
                }
            }
            Console.WriteLine("films that absorb on a conductor: against the films written out {0:E2}; the absorption changes the reflectance by up to {1:F3}",
                              worst, most);
            EXPECT_LT(worst, 2e-4);
            EXPECT_GT(most, 0.3);
        }

        // What a BxDF returns by its value and by its samples, with the first element of its matrix and without (0: value
        // with; 1: samples with; 2: value without; 3: samples without), and what its samples in directions of their own carry
        static void Returned(BxDF bxdf, Vector3f wo, int count, ref RNG rng, double[] mean, double[] error, out int matrices, out double specular,
                             out double specularPlain)
        {
            BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), bxdf);
            double[] sum = new double[4], squares = new double[4];
            matrices = 0;
            specular = 0;
            specularPlain = 0;
            for (int i = 0; i < count; ++i)
            {
                Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                SampledSpectrum value = bxdf.f(frame, wo, wi, TransportMode.Radiance, out bool polarised, out Attenuation A);
                double v = value[0] * AbsCosTheta(wi) * 4 * Math.PI * (polarised ? A.GetMuellerMatrix().Get(0, 0)[0] : 1);
                if (polarised && value[0] > 0)
                {
                    ++matrices;
                }
                sum[0] += v;
                squares[0] += v * v;
                double plain = bxdf.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI;
                // (the density that the integrators weigh the value by: a number, and not zero where there is a value)
                Float density = bxdf.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All);
                EXPECT_FALSE(Float.IsNaN(density) || Float.IsInfinity(density));
                EXPECT_FALSE(double.IsNaN(v) || double.IsNaN(plain));
                if (plain > 0)
                {
                    EXPECT_GT(density, 0);
                }
                sum[2] += plain;
                squares[2] += plain * plain;
                Float uc = rng.Uniform<Float>();
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(frame, wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All, out polarised, out A);
                if (bs && bs.value().pdf > 0 && !bs.value().IsSpecular())
                {
                    // (the integrators divide by the density of a direction that was sampled)
                    EXPECT_GT(bxdf.PDF(wo, bs.value().wi, TransportMode.Radiance, BxDFReflTransFlags.All), 0);
                }
                if (bs && bs.value().pdf > 0)
                {
                    double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf * (polarised ? A.GetMuellerMatrix().Get(0, 0)[0] : 1);
                    if (bs.value().IsSpecular())
                    {
                        specular += w / count;
                    }
                    else
                    {
                        sum[1] += w;
                        squares[1] += w * w;
                    }
                }
                bs = bxdf.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                if (bs && bs.value().pdf > 0)
                {
                    double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                    if (bs.value().IsSpecular())
                    {
                        specularPlain += w / count;
                    }
                    else
                    {
                        sum[3] += w;
                        squares[3] += w * w;
                    }
                }
            }
            for (int k = 0; k < 4; ++k)
            {
                mean[k] = sum[k] / count;
                error[k] = Math.Sqrt(Math.Max(0, squares[k] / count - mean[k] * mean[k]) / count);
            }
        }

        static void Check(string name, BxDF bxdf, Vector3f wo, int count, ref RNG rng, bool expectMatrices)
        {
            double[] mean = new double[4], error = new double[4];
            Returned(bxdf, wo, count, ref rng, mean, error, out int matrices, out double specular, out double specularPlain);
            Console.WriteLine("{0}: with polarisation {1:F4} +- {2:F4} by the value, {3:F4} +- {4:F4} by the samples; without {5:F4} +- {6:F4} and " +
                              "{7:F4} +- {8:F4}; in directions of their own {9:F4} and {10:F4}; {11} values had a matrix", name, mean[0], error[0], mean[1],
                              error[1], mean[2], error[2], mean[3], error[3], specular, specularPlain, matrices);
            EXPECT_LT(Math.Abs(mean[0] - mean[1]), 4 * Math.Sqrt(error[0] * error[0] + error[1] * error[1]) + 0.0005);
            EXPECT_LT(Math.Abs(mean[2] - mean[3]), 4 * Math.Sqrt(error[2] * error[2] + error[3] * error[3]) + 0.0005);
            if (wo.z > 0)
            {
                // (from inside a dielectric the radiance grows by the square of its index)
                EXPECT_LT(mean[1] + specular, 1.01);
                EXPECT_LT(mean[3] + specularPlain, 1.01);
            }
            if (expectMatrices)
            {
                EXPECT_GT(matrices, count / 100);
            }
        }

        // The one-facet Bragg layer (the model of the paper) with polarisation: the matrix of its value is that of the films at
        // the half vector, and its samples carry what its value says
        [TEST("LayerLimits", "BraggOneFacetPolarised")]
        public static void BraggOneFacetPolarised()
        {
            RNG rng = new RNG(31);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            ThinFilmStack film = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f }, new Float[] { 125, 375 });
            film.repeat = 20;
            BraggLayerBxDF layer = new BraggLayerBxDF(film, 1f, new TrowbridgeReitzDistribution(0.3f, 0.3f), true);
            EXPECT_TRUE(layer.HasMuellerMatrix());
            BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), layer);
            double worst = 0, most = 0;
            int compared = 0;
            for (int i = 0; i < 4000; ++i)
            {
                Vector3f a = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Vector3f b = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (a.z < 0.1f || b.z < 0.1f)
                {
                    continue;
                }
                SampledSpectrum value = layer.f(frame, a, b, TransportMode.Radiance, out bool polarised, out Attenuation A);
                EXPECT_EQ(value[0], layer.f(a, b, TransportMode.Radiance)[0]);
                film.Embedded(Dot(a, Normalize(a + b)), 1f, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _, out SampledSpectrum _,
                              out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _);
                if (!polarised || !(Fs[0] + Fp[0] > 0.05f))
                {
                    continue;
                }
                MuellerMatrix m = A.GetMuellerMatrix();
                double degree = Math.Sqrt(m.Get(1, 0)[0] * m.Get(1, 0)[0] + m.Get(2, 0)[0] * m.Get(2, 0)[0]) / m.Get(0, 0)[0];
                double expected = Math.Abs(Fs[0] - Fp[0]) / (Fs[0] + Fp[0]);
                worst = Math.Max(worst, Math.Abs(degree - expected));
                most = Math.Max(most, expected);
                ++compared;
            }
            Console.WriteLine("one facet: degree of polarisation against that of the films, largest difference {0:E2} over {1} pairs (degrees up to {2:F2})",
                              worst, compared, most);
            EXPECT_GT(compared, 500);
            EXPECT_LT(worst, 2e-3);
            EXPECT_GT(most, 0.3);
            Check("one-facet Bragg layer", layer, Normalize(new Vector3f(0.5f, 0.2f, 0.7f)), 300000, ref rng, true);
        }

        static FlakeLayer Flakes(FlakeKind kind, Float alpha, Float thickness, Vector3f axis, Float albedo, Float f0)
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

        static BxDF Stack(params BxDF[] interfaces)
        {
            LayerMedium[] media = new LayerMedium[interfaces.Length - 1];
            for (int i = 0; i < media.Length; ++i)
            {
                media[i].thickness = 1;
                media[i].albedo = new SampledSpectrum(0f);
                media[i].sigmaA = new SampledSpectrum(0f);
            }
            return new LayerStackBxDF(interfaces, media, 1000000, 1, false);
        }

        // Layers of flakes on a dielectric, smooth and rough: the samples carry what the value says, with polarisation and without
        [TEST("LayerLimits", "FlakesOnDielectric")]
        public static void FlakesOnDielectric()
        {
            RNG rng = new RNG(77);
            Vector3f wo = Normalize(new Vector3f(0.6f, 0.2f, 0.5f));
            for (int which = 0; which < 3; ++which)
            {
                SpongeCakeBxDF cake = which == 2
                    ? new SpongeCakeBxDF(new FlakeLayer[] { Flakes(FlakeKind.Fiber, 0.3f, 0.6f, new Vector3f(1, 0.4f, 0), 0.9f, 1f) }, null, false, true, 200, 1)
                    : new SpongeCakeBxDF(new FlakeLayer[] { Flakes(FlakeKind.Surface, 0.4f, 0.7f, new Vector3f(0, 0, 1), 0.9f, 0.8f) }, null, false, true, 200, 1);
                Float alpha = which == 0 ? 0 : 0.25f;
                BxDF stack = Stack(cake, new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(alpha, alpha)));
                Check(which == 0 ? "flakes on smooth glass" : which == 1 ? "flakes on rough glass" : "fibres on rough glass", stack, wo, 300000, ref rng, true);
                Check("the same from inside", stack, Normalize(new Vector3f(-0.3f, -0.1f, -0.9f)), 300000, ref rng, true);
                // (beyond the critical angle of the glass)
                Check("from inside, beyond the critical angle", stack, Normalize(new Vector3f(0.27f, 0.66f, -0.7f)), 300000, ref rng, which != 0);
            }
        }

        // Interfaces that scatter more than once (the walk on the microsurface) in a stack
        [TEST("LayerLimits", "WalkedInterfaces")]
        public static void WalkedInterfaces()
        {
            RNG rng = new RNG(13);
            Vector3f wo = Normalize(new Vector3f(0.4f, -0.3f, 0.6f));
            TrowbridgeReitzDistribution rough = new TrowbridgeReitzDistribution(0.4f, 0.4f);
            BxDF coat = new MultiScatterDielectricBxDF(rough, new MicrosurfaceHeightUniform(), 1.5f, 64);
            Check("walked rough glass over diffuse", Stack(coat, new DiffuseBxDF(new SampledSpectrum(0.7f))), wo, 300000, ref rng, false);
            BxDF metal = new MultiScatterConductorBxDF(rough, new MicrosurfaceHeightUniform(), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f), 64);
            Check("walked rough glass over walked rough metal", Stack(coat, metal), wo, 300000, ref rng, false);
            Check("smooth glass over walked rough metal", Stack(new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0, 0)), metal), wo, 300000, ref rng,
                  false);
        }

        // What the layers of flakes on a dielectric return, by walking between the two with their own samples and nothing of the
        // stack: the stack returns the same, up and down, with a value and in directions of their own. (Its samples did not
        // while its random numbers were those of the flakes: both seeded their streams with the same directions.)
        [TEST("LayerLimits", "FlakesOnDielectricByHand")]
        public static void FlakesOnDielectricByHand()
        {
            RNG rng = new RNG(99);
            Vector3f wo = Normalize(new Vector3f(0.6f, 0.2f, 0.5f));
            for (int which = 0; which < 3; ++which)
            {
                SpongeCakeBxDF cake = which == 2
                    ? new SpongeCakeBxDF(new FlakeLayer[] { Flakes(FlakeKind.Fiber, 0.3f, 0.6f, new Vector3f(1, 0.4f, 0), 0.9f, 1f) }, null, false, true, 200, 1)
                    : new SpongeCakeBxDF(new FlakeLayer[] { Flakes(FlakeKind.Surface, 0.4f, 0.7f, new Vector3f(0, 0, 1), 0.9f, 0.8f) }, null, false, true, 200, 1);
                Float alpha = which == 0 ? 0 : 0.25f;
                BxDF glass = new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(alpha, alpha));
                BxDF stack = Stack(cake, glass);
                int count = 1000000;
                // (up/down, with a value / in a direction of its own)
                double[] truth = new double[4], sampled = new double[4], value = new double[2], squares = new double[2];
                for (int i = 0; i < count; ++i)
                {
                    Vector3f w = wo;
                    double weight = 1;
                    bool specular = true;
                    int at = 0;
                    for (int bounce = 0; bounce < 1000 && weight > 0; ++bounce)
                    {
                        BxDF iface = at == 0 ? cake : glass;
                        pstd.optional<BSDFSample> bs = iface.Sample_f(w, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                      TransportMode.Radiance, BxDFReflTransFlags.All);
                        if (!bs || bs.value().pdf == 0)
                        {
                            weight = 0;
                            break;
                        }
                        weight *= bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                        specular &= bs.value().IsSpecular();
                        Vector3f wi = bs.value().wi;
                        if (at == 0 && wi.z > 0)
                        {
                            truth[specular ? 2 : 0] += weight / count;
                            break;
                        }
                        if (at == 1 && wi.z < 0)
                        {
                            truth[specular ? 3 : 1] += weight / count;
                            break;
                        }
                        at = wi.z < 0 ? 1 : 0;
                        w = -wi;
                    }
                    pstd.optional<BSDFSample> s = stack.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                 TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (s && s.value().pdf > 0)
                    {
                        sampled[(s.value().wi.z > 0 ? 0 : 1) + (s.value().IsSpecular() ? 2 : 0)] += s.value().f[0] * AbsCosTheta(s.value().wi) / s.value().pdf / count;
                    }
                    Vector3f d = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    double v = stack.f(wo, d, TransportMode.Radiance)[0] * AbsCosTheta(d) * 4 * Math.PI;
                    value[d.z > 0 ? 0 : 1] += v / count;
                    squares[d.z > 0 ? 0 : 1] += v * v / count;
                }
                Console.WriteLine("{0}: walked by hand up {1:F4} down {2:F4} (own directions {3:F4} {4:F4}); samples {5:F4} {6:F4} ({7:F4} {8:F4}); value {9:F4} +- {10:F4}, {11:F4} +- {12:F4}",
                                  which, truth[0], truth[1], truth[2], truth[3], sampled[0], sampled[1], sampled[2], sampled[3], value[0],
                                  Math.Sqrt(squares[0] / count), value[1], Math.Sqrt(squares[1] / count));
                for (int k = 0; k < 4; ++k)
                {
                    EXPECT_LT(Math.Abs(truth[k] - sampled[k]), 0.003);
                }
                for (int k = 0; k < 2; ++k)
                {
                    EXPECT_LT(Math.Abs(truth[k] - value[k]), 4 * Math.Sqrt(squares[k] / count) + 0.002);
                }
            }
        }
        // The reflectance and the transmittance of films between two media (the second may absorb or reflect all the light),
        // by their characteristic matrices, in double precision
        static void Characteristic(double cosine, double ambient, Float[] eta, Float[] k, Float[] thickness, bool reversed, double wavelength,
                                   double substrate, bool perpendicular, out double R, out double T)
        {
            System.Numerics.Complex i = new System.Numerics.Complex(0, 1);
            double snell = ambient * Math.Sqrt(1 - cosine * cosine);
            System.Numerics.Complex ratioSubstrate = snell / substrate;
            System.Numerics.Complex cosineSubstrate = System.Numerics.Complex.Sqrt(1 - ratioSubstrate * ratioSubstrate);
            if (cosineSubstrate.Imaginary > 0)
            {
                cosineSubstrate = System.Numerics.Complex.Conjugate(cosineSubstrate);
            }
            System.Numerics.Complex last = perpendicular ? substrate * cosineSubstrate : substrate / cosineSubstrate;
            System.Numerics.Complex b = 1, c = last;
            double admittance = perpendicular ? ambient * cosine : ambient / cosine;
            for (int step = eta.Length - 1; step >= 0; --step)
            {
                int layer = reversed ? eta.Length - 1 - step : step;
                System.Numerics.Complex index = new System.Numerics.Complex(eta[layer], -k[layer]);
                System.Numerics.Complex ratio = snell / index;
                System.Numerics.Complex cosineIn = System.Numerics.Complex.Sqrt(1 - ratio * ratio);
                if (cosineIn.Imaginary > 0)
                {
                    cosineIn = System.Numerics.Complex.Conjugate(cosineIn);
                }
                System.Numerics.Complex tilted = perpendicular ? index * cosineIn : index / cosineIn;
                System.Numerics.Complex turn = 2 * Math.PI * index * (double)thickness[layer] * cosineIn / wavelength;
                System.Numerics.Complex cosTurn = System.Numerics.Complex.Cos(turn), sinTurn = System.Numerics.Complex.Sin(turn);
                System.Numerics.Complex nextB = cosTurn * b + i * sinTurn / tilted * c;
                System.Numerics.Complex nextC = i * tilted * sinTurn * b + cosTurn * c;
                b = nextB;
                c = nextC;
            }
            System.Numerics.Complex sum = admittance * b + c;
            System.Numerics.Complex r = (admittance * b - c) / sum;
            R = r.Real * r.Real + r.Imaginary * r.Imaginary;
            T = 4 * admittance * last.Real / (sum.Real * sum.Real + sum.Imaginary * sum.Imaginary);
        }

        // Films that absorb on a dielectric: the reflectance and the transmittance are the films written out, from both sides
        // and beyond the critical angle; the smooth film returns the two; and the rough ones (one facet, and the walk on the
        // microsurface) are sampled as their value says and lose light
        [TEST("LayerLimits", "AbsorbingFilmsOnDielectric")]
        public static void AbsorbingFilmsOnDielectric()
        {
            RNG rng = new RNG(41);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.23f);
            double worst = 0, worstClear = 0, most = 0;
            int beyond = 0;
            for (int trial = 0; trial < 3000; ++trial)
            {
                int count = 1 + (int)(rng.Uniform<Float>() * 3);
                Float[] eta = new Float[count], k = new Float[count], thickness = new Float[count];
                for (int j = 0; j < count; ++j)
                {
                    eta[j] = Lerp(rng.Uniform<Float>(), 1.2f, 2.4f);
                    k[j] = trial % 4 == 0 ? 0 : (Float)Math.Pow(10, Lerp(rng.Uniform<Float>(), -3f, -0.5f));
                    thickness[j] = Lerp(rng.Uniform<Float>(), 40f, 400f);
                }
                Float substrate = Lerp(rng.Uniform<Float>(), 1.3f, 1.8f);
                bool inside = rng.Uniform<Float>() < 0.5f;
                Float cosine = Lerp(rng.Uniform<Float>(), 0.05f, 1f);
                ThinFilmStack film = new ThinFilmStack(lambda, eta, thickness);
                film.layerKAt = new Float[NSpectrumSamples][];
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    film.layerKAt[c] = k;
                }
                film.Dielectric(inside ? -cosine : cosine, substrate, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _,
                                out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum Ts, out SampledSpectrum Tp);
                bool total = inside && (1 - cosine * cosine) * substrate * substrate >= 1;
                beyond += total ? 1 : 0;
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    Characteristic(cosine, inside ? substrate : 1, eta, k, thickness, inside, lambda[c], inside ? 1 : substrate, true, out double Rs,
                                   out double Tss);
                    Characteristic(cosine, inside ? substrate : 1, eta, k, thickness, inside, lambda[c], inside ? 1 : substrate, false, out double Rp,
                                   out double Tpp);
                    worst = Math.Max(worst, Math.Max(Math.Max(Math.Abs(Rs - Fs[c]), Math.Abs(Rp - Fp[c])), Math.Max(Math.Abs(Tss - Ts[c]), Math.Abs(Tpp - Tp[c]))));
                    if (trial % 4 == 0)
                    {
                        worstClear = Math.Max(worstClear, Math.Max(Math.Abs(1 - Fs[c] - Ts[c]), Math.Abs(1 - Fp[c] - Tp[c])));
                    }
                    most = Math.Max(most, 1 - Fs[c] - Ts[c]);
                    EXPECT_GT(1 - Fs[c] - Ts[c], -1e-4);
                    EXPECT_GT(1 - Fp[c] - Tp[c], -1e-4);
                    if (total)
                    {
                        EXPECT_LT(Math.Abs(Ts[c]) + Math.Abs(Tp[c]), 1e-5);
                    }
                }
            }
            Console.WriteLine("films that absorb on a dielectric: against the films written out {0:E2} ({1} beyond the critical angle); without " +
                              "extinction R + T is 1 to {2:E2}; they absorb up to {3:F3}", worst, beyond, worstClear, most);
            EXPECT_LT(worst, 2e-4);
            EXPECT_LT(worstClear, 2e-4);
            EXPECT_GT(most, 0.5);
            EXPECT_GT(beyond, 100);

            // The BxDFs
            Float[] cellEta = new Float[] { 2.2f }, cellK = new Float[] { 0.08f }, cellThickness = new Float[] { 400 };
            ThinFilmStack clear = new ThinFilmStack(lambda, cellEta, cellThickness);
            ThinFilmStack absorbing = new ThinFilmStack(lambda, cellEta, cellThickness);
            absorbing.layerKAt = new Float[NSpectrumSamples][];
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                absorbing.layerKAt[c] = cellK;
            }
            Vector3f wo = Normalize(new Vector3f(0.5f, 0.2f, 0.7f));
            SampledSpectrum none = new SampledSpectrum(0f);
            for (int which = 0; which < 3; ++which)
            {
                double[] returned = new double[2];
                for (int with = 0; with < 2; ++with)
                {
                    ThinFilmStack film = with == 1 ? absorbing : clear;
                    TrowbridgeReitzDistribution facets = which == 0 ? new TrowbridgeReitzDistribution(0, 0) : new TrowbridgeReitzDistribution(0.3f, 0.3f);
                    BxDF bxdf = which == 2 ? new MultiScatterDielectricBxDF(facets, new MicrosurfaceHeightUniform(), 1.5f, 64, film)
                                           : new ThinFilmBxDF(film, 1.5f, none, none, false, facets);
                    string name = (which == 0 ? "smooth film" : which == 1 ? "rough film, one facet" : "rough film, walked") +
                                  (with == 1 ? ", absorbing" : ", clear");
                    foreach (Vector3f from in new Vector3f[] { wo, -wo })
                    {
                        double[] mean = new double[4], error = new double[4];
                        Returned(bxdf, from, 200000, ref rng, mean, error, out int matrices, out double specular, out double specularPlain);
                        Console.WriteLine("{0}, {1}: {2:F4} +- {3:F4} by the value, {4:F4} +- {5:F4} by the samples (with the matrix {6:F4} and {7:F4}); " +
                                          "in directions of their own {8:F4}", name, from.z > 0 ? "from outside" : "from inside", mean[2], error[2], mean[3],
                                          error[3], mean[0], mean[1], specularPlain);
                        EXPECT_LT(Math.Abs(mean[2] - mean[3]), 4 * Math.Sqrt(error[2] * error[2] + error[3] * error[3]) + 0.0005);
                        EXPECT_LT(Math.Abs(mean[0] - mean[1]), 4 * Math.Sqrt(error[0] * error[0] + error[1] * error[1]) + 0.0005);
                        if (from.z > 0)
                        {
                            returned[with] = mean[3] + specularPlain;
                        }
                        if (which == 0 && from.z > 0)
                        {
                            film.Dielectric(from.z, 1.5f, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _, out SampledSpectrum _,
                                            out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum Ts, out SampledSpectrum Tp);
                            // (a transmission carries radiance, less by the square of the index)
                            double expected = 0.5 * (Fs[0] + Fp[0]) + 0.5 * (Ts[0] + Tp[0]) / (1.5 * 1.5);
                            EXPECT_LT(Math.Abs(specularPlain - expected), 0.005);
                        }
                    }
                }
                Console.WriteLine("returned from outside: clear {0:F4}, absorbing {1:F4}", returned[0], returned[1]);
                EXPECT_LT(returned[1], returned[0] - 0.05);
            }
        }
        // Polarisation inside a material that is asked without it ("polarisedinside"): two flat mirrors then reflect what each
        // polarisation reflects on its own, which the means of the reflectances do not give; and the samples of rough stacks
        // and of the layers of flakes carry what their values say
        [TEST("LayerLimits", "PolarisedInside")]
        public static void PolarisedInside()
        {
            RNG rng = new RNG(19);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            ThinFilmStack films = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f, 1f, 1.5f }, new Float[] { 125, 375, 101, 143 });
            ThinFilmStack first = films.Part(0, 2, 20), second = films.Part(2, 2, 8);
            TrowbridgeReitzDistribution flat = new TrowbridgeReitzDistribution(0, 0);
            Vector3f wo = new Vector3f(0.6f, 0, 0.8f);
            first.Embedded(0.8f, 1f, out SampledSpectrum Fs1, out SampledSpectrum Fp1, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _,
                           out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _);
            second.Embedded(0.8f, 1f, out SampledSpectrum Fs2, out SampledSpectrum Fp2, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _,
                            out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _);
            for (int with = 0; with < 2; ++with)
            {
                LayerStackBxDF stack = (LayerStackBxDF)Stack(new BraggLayerBxDF(first, 1f, flat), new BraggLayerBxDF(second, 1f, flat));
                stack.polarisedInside = with == 1;
                int count = 300000;
                double[] reflected = new double[NSpectrumSamples];
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<BSDFSample> bs = stack.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                  TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (bs && bs.value().pdf > 0 && bs.value().wi.z > 0)
                    {
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            reflected[c] += bs.value().f[c] * AbsCosTheta(bs.value().wi) / bs.value().pdf / count;
                        }
                    }
                }
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    double apart = 0.5 * (Fs1[c] + (1 - Fs1[c]) * (1 - Fs1[c]) * Fs2[c] / (1 - Fs1[c] * Fs2[c]) +
                                          Fp1[c] + (1 - Fp1[c]) * (1 - Fp1[c]) * Fp2[c] / (1 - Fp1[c] * Fp2[c]));
                    double r1 = 0.5 * (Fs1[c] + Fp1[c]), r2 = 0.5 * (Fs2[c] + Fp2[c]);
                    double means = r1 + (1 - r1) * (1 - r1) * r2 / (1 - r1 * r2);
                    Console.WriteLine("{0:F0} nm, {1}: two flat mirrors reflect {2:F4}; each polarisation apart {3:F4}, by the means {4:F4}", lambda[c],
                                      with == 1 ? "polarised inside" : "as before", reflected[c], apart, means);
                    EXPECT_LT(Math.Abs(reflected[c] - (with == 1 ? apart : means)), 0.006);
                }
            }

            TrowbridgeReitzDistribution rough = new TrowbridgeReitzDistribution(0.2f, 0.2f);
            Vector3f from = Normalize(new Vector3f(0.5f, 0.2f, 0.6f));
            LayerStackBxDF mirrors = (LayerStackBxDF)Stack(new BraggLayerBxDF(first, 1f, rough), new BraggLayerBxDF(second, 1f, rough),
                                                           new DiffuseBxDF(new SampledSpectrum(0.3f)));
            mirrors.polarisedInside = true;
            Check("two rough mirrors on a base, polarised inside", mirrors, from, 300000, ref rng, false);
            LayerStackBxDF coat = (LayerStackBxDF)Stack(new DielectricBxDF(1.5f, rough), new ConductorBxDF(rough, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f)));
            mirrors.polarisedInside = false;
            double[] mean = new double[4], error = new double[4];
            Returned(coat, from, 300000, ref rng, mean, error, out int _, out double _, out double _);
            double plain = mean[3], exact = mean[1];
            coat.polarisedInside = true;
            Returned(coat, from, 300000, ref rng, mean, error, out int _, out double _, out double _);
            Console.WriteLine("rough coat on a conductor: {0:F4} as before; with the matrix {1:F4}; polarised inside {2:F4} +- {3:F4} by the samples, " +
                              "{4:F4} +- {5:F4} by the value", plain, exact, mean[3], error[3], mean[2], error[2]);
            EXPECT_LT(Math.Abs(mean[3] - exact), 0.004);
            EXPECT_LT(Math.Abs(mean[2] - mean[3]), 4 * Math.Sqrt(error[2] * error[2] + error[3] * error[3]) + 0.0005);
            SpongeCakeBxDF cake = new SpongeCakeBxDF(new FlakeLayer[] { Flakes(FlakeKind.Surface, 0.4f, 0.7f, new Vector3f(0, 0, 1), 0.9f, 0.5f) }, null, false, true,
                                                     200, 1);
            Returned(cake, from, 300000, ref rng, mean, error, out int _, out double _, out double _);
            exact = mean[1];
            plain = mean[3];
            cake.polarisedInside = true;
            Returned(cake, from, 300000, ref rng, mean, error, out int _, out double _, out double _);
            Console.WriteLine("flakes: {0:F4} as before; with the matrix {1:F4}; polarised inside {2:F4} +- {3:F4} by the samples, {4:F4} +- {5:F4} by the value",
                              plain, exact, mean[3], error[3], mean[2], error[2]);
            EXPECT_LT(Math.Abs(mean[3] - exact), 0.004);
            EXPECT_LT(Math.Abs(mean[2] - mean[3]), 4 * Math.Sqrt(error[2] * error[2] + error[3] * error[3]) + 0.0005);
        }
    }
}
