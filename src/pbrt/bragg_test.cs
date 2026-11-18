// (not in pbrt-v4) Tests of the Bragg mirrors: the closed form of a periodic stack of thin films (fresnel_polarised.cs).

using System;
using System.Collections.Generic;
using testing;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public static unsafe class BraggTests
    {
        static double PhaseDifference(double a, double b)
        {
            double d = Math.Abs(a - b) % (2 * Math.PI);
            return Math.Min(d, 2 * Math.PI - d);
        }

        // The closed form is the stack with its cell written out `repeat` times: reflectances and phases, in reflection and in
        // transmission, from both sides, on a dielectric and on a conductor, inside and outside of the band gaps
        [TEST("Bragg", "ClosedFormIsTheStack")]
        public static void ClosedFormIsTheStack()
        {
            RNG rng = new RNG(5);
            double worstReflectance = 0, worstPhase = 0;
            int gaps = 0, cases = 0;
            for (int trial = 0; trial < 4000; ++trial)
            {
                int cell = 1 + (int)(rng.Uniform<Float>() * 3);
                int repeat = 1 + (int)(rng.Uniform<Float>() * 24);
                Float[] eta = new Float[cell], thickness = new Float[cell];
                for (int i = 0; i < cell; ++i)
                {
                    eta[i] = Lerp(rng.Uniform<Float>(), 1.2f, 2.4f);
                    thickness[i] = Lerp(rng.Uniform<Float>(), 40f, 400f);
                }
                Float[] etaAll = new Float[cell * repeat], thicknessAll = new Float[cell * repeat];
                for (int i = 0; i < cell * repeat; ++i)
                {
                    etaAll[i] = eta[i % cell];
                    thicknessAll[i] = thickness[i % cell];
                }
                Float cosine = Lerp(rng.Uniform<Float>(), 0.02f, 1f);
                Float wavelength = Lerp(rng.Uniform<Float>(), 360f, 830f);
                Float ambient = trial % 3 == 0 ? 1.5f : 1f;
                pstd.complex substrate = trial % 4 == 0 ? new pstd.complex(0.2f, 3.4f) : new pstd.complex(Lerp(rng.Uniform<Float>(), 1f, 1.8f));
                bool reversed = trial % 2 == 1;
                Float fs, fp, ds, dp, dsT, dpT, gs, gp, es, ep, esT, epT;
                FrThinFilmPolarised(cosine, ambient, etaAll, thicknessAll, reversed, wavelength, substrate, out fs, out fp, out ds, out dp, out dsT, out dpT);
                FrBraggPolarised(cosine, ambient, eta, thickness, repeat, reversed, wavelength, substrate, out gs, out gp, out es, out ep, out esT, out epT);
                worstReflectance = Math.Max(worstReflectance, Math.Max(Math.Abs(fs - gs), Math.Abs(fp - gp)));
                // (the phase of a reflection or a transmission that is next to nothing is not defined)
                if (fs > 1e-4 && fp > 1e-4)
                {
                    worstPhase = Math.Max(worstPhase, Math.Max(PhaseDifference(ds, es), PhaseDifference(dp, ep)));
                }
                if (fs < 1 - 1e-4 && fp < 1 - 1e-4 && substrate.im == 0)
                {
                    worstPhase = Math.Max(worstPhase, Math.Max(PhaseDifference(dsT, esT), PhaseDifference(dpT, epT)));
                }
                if (fs > 0.99f && repeat > 8)
                {
                    ++gaps;
                }
                ++cases;
            }
            Console.WriteLine(cases + " stacks, " + gaps + " of them in a band gap: largest difference of a reflectance " + worstReflectance + ", of a phase " + worstPhase);
            EXPECT_LT(worstReflectance, 2e-5);
            EXPECT_LT(worstPhase, 2e-3);
            EXPECT_GT(gaps, 20);
        }

        // The formula of the paper (Yeh's, its equations 2 and 8 to 10) for a cell of two films in a host of the index of the
        // first film: written here from the paper, with real arithmetic outside of the band gaps and its equation 9 inside
        static double Yeh(double n1, double n2, double d1, double d2, int N, double cosine1, double wavelength, bool perpendicular)
        {
            double sine1 = Math.Sqrt(1 - cosine1 * cosine1);
            double sine2 = n1 * sine1 / n2;
            double cosine2 = Math.Sqrt(1 - sine2 * sine2);
            double k1 = 2 * Math.PI * n1 * cosine1 / wavelength, k2 = 2 * Math.PI * n2 * cosine2 / wavelength;
            double omega = perpendicular ? k2 / k1 + k1 / k2 : n1 * n1 * k2 / (n2 * n2 * k1) + n2 * n2 * k1 / (n1 * n1 * k2);
            double dispersion = Math.Cos(k1 * d1) * Math.Cos(k2 * d2) - 0.5 * omega * Math.Sin(k1 * d1) * Math.Sin(k2 * d2);
            // (|C|^2 from the reflectance of one slab of the second medium in the first: Airy's sum)
            double r12 = perpendicular ? (n1 * cosine1 - n2 * cosine2) / (n1 * cosine1 + n2 * cosine2) : (n2 * cosine1 - n1 * cosine2) / (n2 * cosine1 + n1 * cosine2);
            double phi = k2 * d2;
            System.Numerics.Complex e = System.Numerics.Complex.Exp(new System.Numerics.Complex(0, -2 * phi));
            System.Numerics.Complex slab = r12 * (1 - e) / (1 - r12 * r12 * e);
            double slabReflectance = slab.Real * slab.Real + slab.Imaginary * slab.Imaginary;
            double c2 = slabReflectance / (1 - slabReflectance);
            double ratio;
            if (Math.Abs(dispersion) <= 1)
            {
                double K = Math.Acos(dispersion);
                ratio = Math.Abs(Math.Sin(N * K)) < 1e-12 ? double.PositiveInfinity : Math.Sin(K) / Math.Sin(N * K);
            }
            else
            {
                double K = Math.Log(Math.Abs(dispersion) + Math.Sqrt(dispersion * dispersion - 1));
                ratio = Math.Sinh(K) / Math.Sinh(N * K);
            }
            return double.IsInfinity(ratio) ? 0 : c2 / (c2 + ratio * ratio);
        }

        // In the configuration of the paper the closed form here is the paper's: the mirrors of its first figure (the second
        // index 1.5 in air, 20 cells) and others, at all angles and wavelengths
        [TEST("Bragg", "ClosedFormIsThePaper")]
        public static void ClosedFormIsThePaper()
        {
            double[] d1 = new double[] { 125, 101, 315, 60 }, d2 = new double[] { 375, 143, 315, 200 };
            double[] n1 = new double[] { 1, 1, 1, 1.33 }, n2 = new double[] { 1.5, 1.5, 1.5, 1.56 };
            int[] N = new int[] { 20, 20, 10, 9 };
            double worst = 0, highest = 0;
            for (int c = 0; c < 4; ++c)
            {
                // (the cell of the paper is the first medium then the second, in a host of the first: the last film of the first
                // medium is the host, so the stack ends with the second medium on a substrate of the first)
                Float[] eta = new Float[] { (Float)n1[c], (Float)n2[c] };
                Float[] thickness = new Float[] { (Float)d1[c], (Float)d2[c] };
                for (int a = 0; a < 40; ++a)
                {
                    Float cosine = 0.03f + 0.97f * a / 39;
                    for (Float wavelength = 360; wavelength <= 830; wavelength += 1.7f)
                    {
                        Float fs, fp, ds, dp, dsT, dpT;
                        FrBraggPolarised(cosine, (Float)n1[c], eta, thickness, N[c], false, wavelength, new pstd.complex((Float)n1[c]), out fs, out fp, out ds, out dp,
                                         out dsT, out dpT);
                        // (the same single precision numbers that the closed form is given: at grazing angles the spectrum is so
                        // steep that the eighth digit of an index shows in the fifth of the reflectance)
                        double ys = Yeh(eta[0], eta[1], thickness[0], thickness[1], N[c], cosine, wavelength, true);
                        double yp = Yeh(eta[0], eta[1], thickness[0], thickness[1], N[c], cosine, wavelength, false);
                        worst = Math.Max(worst, Math.Max(Math.Abs(fs - ys), Math.Abs(fp - yp)));
                        highest = Math.Max(highest, ys);
                    }
                }
            }
            Console.WriteLine("largest difference from the formula of the paper " + worst + " (reflectances up to " + highest + ")");
            EXPECT_LT(worst, 2e-5);
            EXPECT_GT(highest, 0.999);
        }

        // A great many cells: the band gap reflects everything, nothing overflows, and the cost is that of one cell
        [TEST("Bragg", "ManyCells")]
        public static void ManyCells()
        {
            Float[] eta = new Float[] { 1.5f, 1f }, thickness = new Float[] { 91.7f, 137.5f };
            Float fs, fp, ds, dp, dsT, dpT;
            foreach (int repeat in new int[] { 1, 10, 1000, 1000000 })
            {
                // (quarter wave films for 550 nm: the middle of the first band gap)
                FrBraggPolarised(1f, 1f, eta, thickness, repeat, false, 550f, new pstd.complex(1f), out fs, out fp, out ds, out dp, out dsT, out dpT);
                EXPECT_FALSE(Float.IsNaN(fs) || Float.IsNaN(ds) || Float.IsNaN(dsT));
                if (repeat >= 10)
                {
                    EXPECT_GT(fs, 0.99f);
                }
                // (outside of the gap the reflectance stays below one and oscillates)
                FrBraggPolarised(1f, 1f, eta, thickness, repeat, false, 760f, new pstd.complex(1f), out fs, out fp, out ds, out dp, out dsT, out dpT);
                EXPECT_FALSE(Float.IsNaN(fs) || Float.IsNaN(ds) || Float.IsNaN(dsT));
                EXPECT_LE(fs, 1f);
            }
        }

        // The reflectance and the transmittance of the films written out one by one, by their characteristic matrices (Abeles):
        // another route than the amplitudes of FrBraggPolarised(), in double precision
        static void Characteristic(double cosine, double ambient, Float[] eta, Float[] k, Float[] thickness, int repeat, double wavelength,
                                   bool perpendicular, out double R, out double T)
        {
            System.Numerics.Complex i = new System.Numerics.Complex(0, 1);
            double snell = ambient * Math.Sqrt(1 - cosine * cosine);
            System.Numerics.Complex b = 1, c = perpendicular ? ambient * cosine : ambient / cosine;
            double admittance = c.Real;
            for (int cell = repeat - 1; cell >= 0; --cell)
            {
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
            }
            System.Numerics.Complex sum = admittance * b + c;
            System.Numerics.Complex r = (admittance * b - c) / sum;
            R = r.Real * r.Real + r.Imaginary * r.Imaginary;
            T = 4 * admittance * admittance / (sum.Real * sum.Real + sum.Imaginary * sum.Imaginary);
        }

        // Films that absorb: the closed form is the films written out; a film of the index of its surroundings absorbs as
        // Beer's law says; the transmittance is the same from both sides, the reflectance is not; and the rough layer loses
        // what the films absorb and nothing else
        [TEST("Bragg", "Absorption")]
        public static void Absorption()
        {
            RNG rng = new RNG(21);
            double worst = 0, most = 0, worstReciprocity = 0, asymmetry = 0;
            for (int trial = 0; trial < 3000; ++trial)
            {
                int cell = 1 + (int)(rng.Uniform<Float>() * 3);
                int repeat = 1 + (int)(rng.Uniform<Float>() * 30);
                Float[] eta = new Float[cell], k = new Float[cell], thickness = new Float[cell];
                for (int j = 0; j < cell; ++j)
                {
                    eta[j] = Lerp(rng.Uniform<Float>(), 1.2f, 2.4f);
                    k[j] = trial % 5 == 0 ? 0 : (Float)Math.Pow(10, Lerp(rng.Uniform<Float>(), -4f, -0.5f));
                    thickness[j] = Lerp(rng.Uniform<Float>(), 40f, 400f);
                }
                Float cosine = Lerp(rng.Uniform<Float>(), 0.05f, 1f);
                Float wavelength = Lerp(rng.Uniform<Float>(), 360f, 830f);
                Float ambient = trial % 3 == 0 ? 1.5f : 1f;
                Float fs, fp, ds, dp, dsT, dpT, ts, tp, gs, gp, us, up;
                FrBraggPolarised(cosine, ambient, eta, k, thickness, repeat, false, wavelength, new pstd.complex(ambient), out fs, out fp, out ds, out dp,
                                 out dsT, out dpT, out ts, out tp);
                double R, T;
                Characteristic(cosine, ambient, eta, k, thickness, repeat, wavelength, true, out R, out T);
                worst = Math.Max(worst, Math.Max(Math.Abs(R - fs), Math.Abs(T - ts)));
                most = Math.Max(most, 1 - R - T);
                EXPECT_GT(1 - fs - ts, -1e-4);
                Characteristic(cosine, ambient, eta, k, thickness, repeat, wavelength, false, out R, out T);
                worst = Math.Max(worst, Math.Max(Math.Abs(R - fp), Math.Abs(T - tp)));
                EXPECT_GT(1 - fp - tp, -1e-4);
                if (trial % 5 == 0)
                {
                    EXPECT_LT(Math.Abs(1 - fs - ts), 1e-4);
                    EXPECT_LT(Math.Abs(1 - fp - tp), 1e-4);
                }
                FrBraggPolarised(cosine, ambient, eta, k, thickness, repeat, true, wavelength, new pstd.complex(ambient), out gs, out gp, out ds, out dp,
                                 out dsT, out dpT, out us, out up);
                worstReciprocity = Math.Max(worstReciprocity, Math.Max(Math.Abs(us - ts), Math.Abs(up - tp)));
                asymmetry = Math.Max(asymmetry, Math.Abs(gs - fs));
            }
            Console.WriteLine("closed form against the films written out {0:E2}, most absorbed {1:F3}, transmittance from the two sides {2:E2}, reflectance {3:F3}",
                              worst, most, worstReciprocity, asymmetry);
            EXPECT_LT(worst, 2e-4);
            EXPECT_GT(most, 0.9);
            EXPECT_LT(worstReciprocity, 2e-4);
            EXPECT_GT(asymmetry, 0.05);

            // Beer's law
            foreach (Float cosine in new Float[] { 1f, 0.6f, 0.3f })
            {
                Float fs, fp, ds, dp, dsT, dpT, ts, tp;
                FrBraggPolarised(cosine, 1.5f, new Float[] { 1.5f }, new Float[] { 1e-3f }, new Float[] { 500f }, 40, false, 550f, new pstd.complex(1.5f),
                                 out fs, out fp, out ds, out dp, out dsT, out dpT, out ts, out tp);
                double beer = Math.Exp(-4 * Math.PI * 1e-3 * 500 * 40 / (550 * cosine));
                Console.WriteLine("cosine {0}: transmittance {1:F5} {2:F5}, Beer's law {3:F5}, reflectance {4:E1}", cosine, ts, tp, beer, fs);
                EXPECT_LT(Math.Abs(ts - beer), 2e-4);
                EXPECT_LT(Math.Abs(tp - beer), 2e-4);
                // (the film reflects a little: its index, with its imaginary part, is not that of its surroundings. About
                // (k / (2 n cos^2))^2, with fringes.)
                EXPECT_LT(fs, 4 * Math.Pow(1e-3 / (2 * 1.5 * cosine * cosine), 2) + 1e-7);
            }

            // The layer: what the samples carry is what the facets return. A smooth layer returns R + T of the films; a
            // rough one, whose facets are met at other angles and more than once, somewhat else, and less than all.
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            foreach (Float alpha in new Float[] { 0f, 0.3f })
            {
                ThinFilmStack film = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f }, new Float[] { 125, 375 });
                film.repeat = 20;
                film.layerKAt = new Float[NSpectrumSamples][];
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    film.layerKAt[c] = new Float[] { 0f, 0.01f };
                }
                BraggLayerBxDF layer = new BraggLayerBxDF(film, 1f, new TrowbridgeReitzDistribution(alpha, alpha));
                Vector3f wo = new Vector3f(0.6f, 0, 0.8f);
                int count = 100000;
                double[] all = new double[NSpectrumSamples];
                double glossy = 0, glossySquares = 0, value = 0, valueSquares = 0;
                for (int j = 0; j < count; ++j)
                {
                    pstd.optional<BSDFSample> bs = layer.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                  TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (bs)
                    {
                        BSDFSample sample = bs.value();
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            all[c] += sample.f[c] * AbsCosTheta(sample.wi) / sample.pdf / count;
                        }
                        if (!sample.IsSpecular())
                        {
                            double v = sample.f[0] * AbsCosTheta(sample.wi) / sample.pdf;
                            glossy += v;
                            glossySquares += v * v;
                        }
                    }
                    if (alpha > 0)
                    {
                        Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        double f = layer.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI;
                        value += f;
                        valueSquares += f * f;
                    }
                }
                film.Embedded(0.8f, 1f, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _,
                              out SampledSpectrum _, out SampledSpectrum Ts, out SampledSpectrum Tp);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    double smooth = 0.5 * (Fs[c] + Fp[c] + Ts[c] + Tp[c]);
                    Console.WriteLine("alpha {0} wavelength {1:F0}: the layer returns {2:F4}, flat films {3:F4}", alpha, lambda[c], all[c], smooth);
                    EXPECT_LT(all[c], 1.0);
                    if (alpha == 0)
                    {
                        EXPECT_LT(Math.Abs(all[c] - smooth), 0.01);
                    }
                    else
                    {
                        EXPECT_LT(Math.Abs(all[c] - smooth), 0.2);
                    }
                }
                if (alpha > 0)
                {
                    double bySamples = glossy / count, byValue = value / count;
                    double errorSamples = Math.Sqrt(Math.Max(0, glossySquares / count - bySamples * bySamples) / count);
                    double errorValue = Math.Sqrt(Math.Max(0, valueSquares / count - byValue * byValue) / count);
                    Console.WriteLine("reflected at least once: {0:F4} +- {1:F4} by the samples, {2:F4} +- {3:F4} by the value", bySamples, errorSamples,
                                      byValue, errorValue);
                    EXPECT_LT(Math.Abs(bySamples - byValue), 4 * Math.Sqrt(errorSamples * errorSamples + errorValue * errorValue));
                }
            }
        }

        // The interfaces side by side in a clear host, as the material has them
        static BxDF Flat(bool twoSided, params BxDF[] interfaces)
        {
            LayerMedium[] media = new LayerMedium[interfaces.Length - 1];
            for (int i = 0; i < media.Length; ++i)
            {
                media[i].thickness = 1;
                media[i].albedo = new SampledSpectrum(0f);
                media[i].sigmaA = new SampledSpectrum(0f);
            }
            return new LayerStackBxDF(interfaces, media, 1000000, 1, twoSided);
        }

        static BraggLayerBxDF Layer(SampledWavelengths lambda, Float alpha)
        {
            ThinFilmStack film = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f }, new Float[] { 125, 375 });
            film.repeat = 20;
            return new BraggLayerBxDF(film, 1f, new TrowbridgeReitzDistribution(alpha, alpha), true);
        }

        static BraggLayerBxDF Walked(SampledWavelengths lambda, Float alpha)
        {
            ThinFilmStack film = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f }, new Float[] { 125, 375 });
            film.repeat = 20;
            return new BraggLayerBxDF(film, 1f, new TrowbridgeReitzDistribution(alpha, alpha));
        }

        // The rough Bragg layer with one facet only (the model of the paper): its reflection is sampled as its value says, the weight of its straight transmission is the
        // integral of the paper (its equation 22), it is reciprocal and it makes no light
        [TEST("Bragg", "RoughLayer")]
        public static void RoughLayer()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            RNG rng = new RNG(9);
            foreach (Float alpha in new Float[] { 0.1f, 0.4f })
            {
                TrowbridgeReitzDistribution distribution = new TrowbridgeReitzDistribution(alpha, alpha);
                BraggLayerBxDF layer = Layer(lambda, alpha);
                ThinFilmStack film = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f }, new Float[] { 125, 375 });
                film.repeat = 20;
                foreach (Float cosine in new Float[] { 0.95f, 0.6f, 0.25f, -0.6f })
                {
                    Float sine = SafeSqrt(1 - cosine * cosine);
                    Vector3f wo = new Vector3f(sine, 0, cosine);
                    int count = 400000;
                    // Reflection: by its samples, and by directions uniform over the hemisphere
                    double[] sampled = new double[2], uniform = new double[2], sampledSquares = new double[2], uniformSquares = new double[2];
                    double through = 0, throughSquares = 0, expectedThrough = 0, expectedSquares = 0, density = 0;
                    double worstValue = 0, worstDensity = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        pstd.optional<BSDFSample> bs = layer.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                      TransportMode.Radiance, BxDFReflTransFlags.Reflection);
                        if (bs && bs.value().pdf > 0)
                        {
                            BSDFSample s = bs.value();
                            for (int c = 0; c < 2; ++c)
                            {
                                double v = s.f[c] * AbsCosTheta(s.wi) / s.pdf;
                                sampled[c] += v;
                                sampledSquares[c] += v * v;
                            }
                            SampledSpectrum value = layer.f(wo, s.wi, TransportMode.Radiance);
                            Float pdf = layer.PDF(wo, s.wi, TransportMode.Radiance, BxDFReflTransFlags.Reflection);
                            // (in units of what the sample adds: the reflectance of a Bragg mirror is so steep in the angle that
                            // the half vector recomputed in single precision moves a small reflectance by a percent of itself)
                            worstValue = Math.Max(worstValue, Math.Abs(value[0] - s.f[0]) * AbsCosTheta(s.wi) / s.pdf);
                            worstDensity = Math.Max(worstDensity, Math.Abs(pdf - s.pdf) / s.pdf);
                        }
                        Vector3f wi = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        if (cosine < 0)
                        {
                            wi = -wi;
                        }
                        SampledSpectrum f = layer.f(wo, wi, TransportMode.Radiance);
                        for (int c = 0; c < 2; ++c)
                        {
                            double v = f[c] * AbsCosTheta(wi) * 2 * Math.PI;
                            uniform[c] += v;
                            uniformSquares[c] += v * v;
                        }
                        density += layer.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.Reflection) * 2 * Math.PI;

                        // Transmission: the weight of the sample, and the paper's integral over the normals of the facets
                        // (uniform over the hemisphere): |wo.m| / |wo.n| T(wo.m) G1(wo)^2 D(m)
                        bs = layer.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Radiance,
                                            BxDFReflTransFlags.Transmission);
                        if (bs)
                        {
                            EXPECT_TRUE(bs.value().wi == -wo);
                            double v = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                            through += v;
                            throughSquares += v * v;
                        }
                        Vector3f wm = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        Vector3f up = cosine < 0 ? -wo : wo;
                        Float facing = Dot(up, wm);
                        if (facing > 0)
                        {
                            SampledSpectrum Fs, Fp;
                            film.Embedded(cosine < 0 ? -facing : facing, 1f, out Fs, out Fp, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _,
                                          out SampledSpectrum _, out SampledSpectrum _);
                            double g1 = distribution.G1(up);
                            double v = facing / Math.Abs(cosine) * (1 - 0.5 * (Fs[0] + Fp[0])) * g1 * g1 * distribution.D(wm) * 2 * Math.PI;
                            expectedThrough += v;
                            expectedSquares += v * v;
                        }
                    }
                    string name = "alpha " + alpha + " cosine " + cosine;
                    for (int c = 0; c < 2; ++c)
                    {
                        double a1 = sampled[c] / count, a2 = uniform[c] / count;
                        double e1 = Math.Sqrt(Math.Max(0, sampledSquares[c] / count - a1 * a1) / count), e2 = Math.Sqrt(Math.Max(0, uniformSquares[c] / count - a2 * a2) / count);
                        if (c == 0)
                        {
                            Console.WriteLine(name + ": reflection " + a1.ToString("F5") + " +- " + e1.ToString("F5") + " by samples, " + a2.ToString("F5") + " +- " + e2.ToString("F5") +
                                              " by uniform directions; integral of the density " + (density / count).ToString("F4"));
                        }
                        EXPECT_LT(Math.Abs(a1 - a2), 4 * Math.Sqrt(e1 * e1 + e2 * e2) + 1e-4, name + " wavelength " + c + ": " + a1 + " vs. " + a2);
                    }
                    double t1 = through / count, t2 = expectedThrough / count;
                    double f1 = Math.Sqrt(Math.Max(0, throughSquares / count - t1 * t1) / count), f2 = Math.Sqrt(Math.Max(0, expectedSquares / count - t2 * t2) / count);
                    Console.WriteLine(name + ": transmission " + t1.ToString("F5") + " +- " + f1.ToString("F5") + " by samples, " + t2.ToString("F5") + " +- " + f2.ToString("F5") +
                                      " by the integral; reflection and transmission together " + (sampled[0] / count + t1).ToString("F4"));
                    EXPECT_LT(Math.Abs(t1 - t2), 4 * Math.Sqrt(f1 * f1 + f2 * f2) + 1e-4, name + ": " + t1 + " vs. " + t2);
                    EXPECT_LT(worstValue, 1e-3, name);
                    EXPECT_LT(worstDensity, 1e-3, name);
                    EXPECT_LT(density / count, 1.01, name);
                    EXPECT_LT(sampled[0] / count + t1, 1.005, name);
                }
                // Reciprocal
                for (int i = 0; i < 2000; ++i)
                {
                    Vector3f a1 = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f b1 = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Float forward = layer.f(a1, b1, TransportMode.Radiance)[0], backward = layer.f(b1, a1, TransportMode.Radiance)[0];
                    EXPECT_LT(Math.Abs(forward - backward), 1e-4f * Math.Max(1, Math.Max(forward, backward)));
                }
            }

            // Smooth: a mirror and a window that share all the light
            BraggLayerBxDF smooth = Layer(lambda, 0f);
            double total = 0;
            for (int i = 0; i < 100000; ++i)
            {
                pstd.optional<BSDFSample> bs = smooth.Sample_f(new Vector3f(0.6f, 0, 0.8f), rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                               TransportMode.Radiance, BxDFReflTransFlags.All);
                total += bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
            }
            EXPECT_LT(Math.Abs(total / 100000 - 1), 0.02);
        }

        // The rough Bragg layer with all the facets (the default): it returns all the light, at every wavelength; the part that
        // was reflected at least once is sampled as its value says, with the density that is reported; and the value is the
        // same from both ends
        [TEST("Bragg", "MultipleScattering")]
        public static void MultipleScattering()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            RNG rng = new RNG(13);
            foreach (Float alpha in new Float[] { 0.1f, 0.4f })
            {
                BraggLayerBxDF layer = Walked(lambda, alpha);
                foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f, -0.5f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                    int count = 150000;
                    double[] all = new double[NSpectrumSamples], allSquares = new double[NSpectrumSamples];
                    double glossy = 0, glossySquares = 0, uniform = 0, uniformSquares = 0, density = 0, densitySquares = 0, ballistic = 0, glossyCount = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        pstd.optional<BSDFSample> bs = layer.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                      TransportMode.Radiance, BxDFReflTransFlags.All);
                        if (bs)
                        {
                            BSDFSample s = bs.value();
                            for (int c = 0; c < NSpectrumSamples; ++c)
                            {
                                double v = s.f[c] * AbsCosTheta(s.wi) / s.pdf;
                                all[c] += v;
                                allSquares[c] += v * v;
                            }
                            double first = s.f[0] * AbsCosTheta(s.wi) / s.pdf;
                            if (s.IsSpecular())
                            {
                                EXPECT_TRUE(s.wi == -wo);
                                ballistic += first;
                            }
                            else
                            {
                                glossy += first;
                                glossySquares += first * first;
                                glossyCount += 1;
                            }
                        }
                        // (the whole sphere, uniformly)
                        Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        double value = layer.f(wo, wi, TransportMode.Radiance)[0] * AbsCosTheta(wi) * 4 * Math.PI;
                        uniform += value;
                        uniformSquares += value * value;
                        double p = layer.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All) * 4 * Math.PI;
                        density += p;
                        densitySquares += p * p;
                    }
                    string name = "alpha " + alpha + " cosine " + cosine;
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        double mean = all[c] / count, error = Math.Sqrt(Math.Max(0, allSquares[c] / count - mean * mean) / count);
                        EXPECT_LT(Math.Abs(mean - 1), 4 * error + 2e-3, name + " wavelength " + c + ": all the light " + mean + " +- " + error);
                    }
                    double g = glossy / count, gError = Math.Sqrt(Math.Max(0, glossySquares / count - g * g) / count);
                    double u = uniform / count, uError = Math.Sqrt(Math.Max(0, uniformSquares / count - u * u) / count);
                    double d = density / count, dError = Math.Sqrt(Math.Max(0, densitySquares / count - d * d) / count);
                    double fraction = glossyCount / count, fractionError = Math.Sqrt(fraction * (1 - fraction) / count);
                    Console.WriteLine(name + ": all " + (all[0] / count).ToString("F4") + "; straight through " + (ballistic / count).ToString("F4") + "; reflected at least once " +
                                      g.ToString("F4") + " +- " + gError.ToString("F4") + " by samples, " + u.ToString("F4") + " +- " + uError.ToString("F4") +
                                      " by the value; density " + d.ToString("F4") + " +- " + dError.ToString("F4") + " for " + fraction.ToString("F4") + " of the samples");
                    EXPECT_LT(Math.Abs(g - u), 4 * Math.Sqrt(gError * gError + uError * uError) + 1e-3, name + ": " + g + " vs. " + u);
                    EXPECT_LT(Math.Abs(d - fraction), 4 * Math.Sqrt(dError * dError + fractionError * fractionError) + 2e-3, name + ": " + d + " vs. " + fraction);
                }

                // Reciprocal: the value is an estimate, so pairs of directions near a pair are averaged, each from both ends
                Vector3f[] firsts = new Vector3f[] { Normalize(new Vector3f(0.3f, 0.1f, 0.9f)), Normalize(new Vector3f(0.6f, -0.2f, 0.5f)), Normalize(new Vector3f(0.2f, 0.5f, 0.7f)) };
                Vector3f[] seconds = new Vector3f[] { Normalize(new Vector3f(-0.5f, 0.2f, 0.6f)), Normalize(new Vector3f(-0.3f, 0.3f, -0.7f)), Normalize(new Vector3f(0.6f, 0.1f, 0.3f)) };
                for (int pair = 0; pair < 3; ++pair)
                {
                    double forward = 0, backward = 0, squares = 0;
                    int count = 60000;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f a = Normalize(firsts[pair] + 0.02f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f));
                        Vector3f b = Normalize(seconds[pair] + 0.02f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f));
                        double x = layer.f(a, b, TransportMode.Radiance)[0], y = layer.f(b, a, TransportMode.Radiance)[0];
                        forward += x;
                        backward += y;
                        squares += (x - y) * (x - y);
                    }
                    forward /= count;
                    backward /= count;
                    double error = Math.Sqrt(squares / count / count);
                    Console.WriteLine("alpha " + alpha + " pair " + pair + ": " + forward.ToString("F5") + " one way, " + backward.ToString("F5") + " the other, +- " + error.ToString("F5"));
                    EXPECT_LT(Math.Abs(forward - backward), 4 * error + 1e-4 * forward);
                }
            }
        }

        // The layered structure: over a white base no light is made, and most of it comes back
        // The value of the layer, of the layer on its base and of both under the surface of a host medium is what their samples
        // carry, at every wavelength: the reflection that has a value, integrated, against the samples that are not a
        // direction of their own
        [TEST("Bragg", "ValueIsTheSamples")]
        public static void ValueIsTheSamples()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            RNG rng = new RNG(3);
            string[] names = new string[] { "the layer", "on a base", "under a host surface", "under a host surface, white base" };
            for (int kind = 0; kind < 4; ++kind)
            {
                BxDF surface = Walked(lambda, 0.1f);
                BxDF under = new DiffuseBxDF(new SampledSpectrum(kind == 3 ? 0.8f : 0.05f));
                if (kind == 1)
                {
                    surface = Flat(true, surface, under);
                }
                else if (kind >= 2)
                {
                    surface = Flat(true, new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0, 0)), surface, under);
                }
                foreach (Float sine in new Float[] { 0.3f, 0.8f })
                {
                    Vector3f wo = new Vector3f(sine, 0, SafeSqrt(1 - sine * sine));
                    int count = 150000;
                    double[] value = new double[NSpectrumSamples], valueSquares = new double[NSpectrumSamples];
                    double[] samples = new double[NSpectrumSamples], sampleSquares = new double[NSpectrumSamples];
                    double largest = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3f wi = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        SampledSpectrum f = surface.f(wo, wi, TransportMode.Radiance);
                        pstd.optional<BSDFSample> bs = surface.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                        TransportMode.Radiance, BxDFReflTransFlags.All);
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            double v = f[c] * wi.z * 2 * Math.PI;
                            value[c] += v;
                            valueSquares[c] += v * v;
                            if (bs && bs.value().pdf > 0 && !bs.value().IsSpecular() && bs.value().wi.z > 0)
                            {
                                double w = bs.value().f[c] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                                samples[c] += w;
                                sampleSquares[c] += w * w;
                                largest = Math.Max(largest, w);
                            }
                        }
                    }
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        double byValue = value[c] / count, bySamples = samples[c] / count;
                        double errorValue = Math.Sqrt(Math.Max(0, valueSquares[c] / count - byValue * byValue) / count);
                        double errorSamples = Math.Sqrt(Math.Max(0, sampleSquares[c] / count - bySamples * bySamples) / count);
                        Console.WriteLine("{0}, sine {1}, {2:F0} nm: {3:F4} +- {4:F4} by the value, {5:F4} +- {6:F4} by the samples (largest weight {7:F2})",
                                          names[kind], sine, lambda[c], byValue, errorValue, bySamples, errorSamples, largest);
                        EXPECT_LT(Math.Abs(byValue - bySamples), 4 * Math.Sqrt(errorValue * errorValue + errorSamples * errorSamples) + 0.002);
                    }
                }
            }
        }

        // Two mirrors, one over the other in the host: the light goes between them any number of times, without interference
        // (they are apart by much more than a wavelength). Smooth, the two reflect R1 + T1^2 R2 / (1 - R1 R2); rough, they
        // return all the light between them.
        [TEST("Bragg", "TwoMirrors")]
        public static void TwoMirrors()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            RNG rng = new RNG(8);
            ThinFilmStack films = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f, 1f, 1.5f }, new Float[] { 125, 375, 101, 143 });
            foreach (Float alpha in new Float[] { 0f, 0.2f })
            {
                TrowbridgeReitzDistribution distribution = new TrowbridgeReitzDistribution(alpha, alpha);
                ThinFilmStack first = films.Part(0, 2, 20), second = films.Part(2, 2, 8);
                BxDF stack = Flat(false, new BraggLayerBxDF(first, 1f, distribution), new BraggLayerBxDF(second, 1f, distribution));
                Vector3f wo = new Vector3f(0.6f, 0, 0.8f);
                int count = 200000;
                double[] reflected = new double[NSpectrumSamples], all = new double[NSpectrumSamples];
                for (int i = 0; i < count; ++i)
                {
                    pstd.optional<BSDFSample> bs = stack.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                  TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (bs && bs.value().pdf > 0)
                    {
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            double v = bs.value().f[c] * AbsCosTheta(bs.value().wi) / bs.value().pdf / count;
                            all[c] += v;
                            if (bs.value().wi.z > 0)
                            {
                                reflected[c] += v;
                            }
                        }
                    }
                }
                first.Embedded(0.8f, 1f, out SampledSpectrum Fs1, out SampledSpectrum Fp1, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _,
                               out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _);
                second.Embedded(0.8f, 1f, out SampledSpectrum Fs2, out SampledSpectrum Fp2, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _,
                                out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    // (each polarisation keeps to itself between two flat mirrors)
                    double expected = 0.5 * (Fs1[c] + (1 - Fs1[c]) * (1 - Fs1[c]) * Fs2[c] / (1 - Fs1[c] * Fs2[c]) +
                                             Fp1[c] + (1 - Fp1[c]) * (1 - Fp1[c]) * Fp2[c] / (1 - Fp1[c] * Fp2[c]));
                    // (the walk without polarisation gives every reflection the mean of the two)
                    double r1 = 0.5 * (Fs1[c] + Fp1[c]), r2 = 0.5 * (Fs2[c] + Fp2[c]);
                    double unpolarised = r1 + (1 - r1) * (1 - r1) * r2 / (1 - r1 * r2);
                    Console.WriteLine("alpha {0}, {1:F0} nm: reflected {2:F4} (flat mirrors: {3:F4} without polarisation, {4:F4} with), all {5:F4}", alpha,
                                      lambda[c], reflected[c], unpolarised, expected, all[c]);
                    EXPECT_LT(Math.Abs(all[c] - 1), 0.01);
                    if (alpha == 0)
                    {
                        EXPECT_LT(Math.Abs(reflected[c] - unpolarised), 0.01);
                    }
                }
            }
        }

        // With polarisation. Between two flat mirrors each polarisation keeps to itself, and what the two reflect is no longer
        // what the means of their reflectances give: the first element of the matrix of the samples has the difference. A
        // layer that is nearly flat polarises the light as its films do. And the scalar is the same with and without.
        [TEST("Bragg", "Polarised")]
        public static void Polarised()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            RNG rng = new RNG(17);
            ThinFilmStack films = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f, 1f, 1.5f }, new Float[] { 125, 375, 101, 143 });
            ThinFilmStack first = films.Part(0, 2, 20), second = films.Part(2, 2, 8);
            TrowbridgeReitzDistribution flat = new TrowbridgeReitzDistribution(0, 0);
            BxDF stack = Flat(false, new BraggLayerBxDF(first, 1f, flat), new BraggLayerBxDF(second, 1f, flat));
            BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), stack);
            Vector3f wo = new Vector3f(0.6f, 0, 0.8f);
            int count = 200000;
            double[] reflected = new double[NSpectrumSamples];
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<BSDFSample> bs = stack.Sample_f(frame, wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                              TransportMode.Radiance, BxDFReflTransFlags.All, out bool polarised, out Attenuation A);
                if (bs && bs.value().pdf > 0 && bs.value().wi.z > 0)
                {
                    EXPECT_TRUE(polarised);
                    MuellerMatrix m = A.GetMuellerMatrix();
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        reflected[c] += bs.value().f[c] * AbsCosTheta(bs.value().wi) / bs.value().pdf * m.Get(0, 0)[c] / count;
                    }
                }
            }
            first.Embedded(0.8f, 1f, out SampledSpectrum Fs1, out SampledSpectrum Fp1, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _,
                           out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _);
            second.Embedded(0.8f, 1f, out SampledSpectrum Fs2, out SampledSpectrum Fp2, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _,
                            out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _);
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                double apart = 0.5 * (Fs1[c] + (1 - Fs1[c]) * (1 - Fs1[c]) * Fs2[c] / (1 - Fs1[c] * Fs2[c]) +
                                      Fp1[c] + (1 - Fp1[c]) * (1 - Fp1[c]) * Fp2[c] / (1 - Fp1[c] * Fp2[c]));
                double r1 = 0.5 * (Fs1[c] + Fp1[c]), r2 = 0.5 * (Fs2[c] + Fp2[c]);
                double means = r1 + (1 - r1) * (1 - r1) * r2 / (1 - r1 * r2);
                Console.WriteLine("{0:F0} nm: two flat mirrors reflect {1:F4} with polarisation; each polarisation apart {2:F4}, by the means {3:F4}", lambda[c],
                                  reflected[c], apart, means);
                EXPECT_LT(Math.Abs(reflected[c] - apart), 0.01);
            }

            // A layer that is nearly flat
            BraggLayerBxDF layer = Walked(lambda, 0.03f);
            BSDF layerFrame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), layer);
            ThinFilmStack cell = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f }, new Float[] { 125, 375 });
            cell.repeat = 20;
            double worstScalar = 0, most = 0;
            int compared = 0, twice = 0;
            for (int i = 0; i < 4000; ++i)
            {
                Vector3f a = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                if (a.z < 0.2f)
                {
                    continue;
                }
                Vector3f wm = Normalize(new Vector3f(0.03f * (rng.Uniform<Float>() - 0.5f), 0.03f * (rng.Uniform<Float>() - 0.5f), 1));
                Vector3f b = Reflect(a, wm);
                SampledSpectrum with = layer.f(layerFrame, a, b, TransportMode.Radiance, out bool polarised, out Attenuation A);
                SampledSpectrum without = layer.f(a, b, TransportMode.Radiance);
                worstScalar = Math.Max(worstScalar, Math.Abs(with[0] - without[0]));
                cell.Embedded(Dot(a, wm), 1f, out SampledSpectrum Fs, out SampledSpectrum Fp, out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _,
                              out SampledSpectrum _, out SampledSpectrum _, out SampledSpectrum _);
                if (polarised && with[0] > 1f && Fs[0] + Fp[0] > 0.2f)
                {
                    MuellerMatrix m = A.GetMuellerMatrix();
                    double linear = Math.Sqrt(m.Get(1, 0)[0] * m.Get(1, 0)[0] + m.Get(2, 0)[0] * m.Get(2, 0)[0]) / m.Get(0, 0)[0];
                    double expected = Math.Abs(Fs[0] - Fp[0]) / (Fs[0] + Fp[0]);
                    // (now and then the estimate of the value is that of a walk that met two facets: another matrix, rightly)
                    if (Math.Abs(linear - expected) > 0.03)
                    {
                        ++twice;
                    }
                    most = Math.Max(most, expected);
                    ++compared;
                }
            }
            Console.WriteLine("nearly flat layer: degree of polarisation of the reflection that of the films but for {0} of {1} pairs (up to {2:F2}); " +
                              "scalar with and without the matrix {3:E1}", twice, compared, most, worstScalar);
            EXPECT_GT(compared, 300);
            EXPECT_GT(most, 0.3);
            EXPECT_LT(twice, compared / 100);
            EXPECT_EQ(0.0, worstScalar);
        }

        // Two mirrors over a base, with polarisation: a mirror reflects one polarisation more than the other, and after a few
        // reflections between the two the light is nearly all of that one. The mirrors choose by what they reflect of it and
        // the roulette of the walk goes by it, so that what a sample carries (its weight times the first element of its
        // matrix) stays bounded: it reached 1794 when the choice was by the mean of the two reflectances.
        [TEST("Bragg", "PolarisedWeights")]
        public static void PolarisedWeights()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            RNG rng = new RNG(3);
            ThinFilmStack films = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f, 1f, 1.5f }, new Float[] { 125, 375, 101, 143 });
            TrowbridgeReitzDistribution rough = new TrowbridgeReitzDistribution(0.1f, 0.1f);
            BxDF surface = Flat(true, new BraggLayerBxDF(films.Part(0, 2, 20), 1f, rough), new BraggLayerBxDF(films.Part(2, 2, 20), 1f, rough),
                                new DiffuseBxDF(new SampledSpectrum(0.05f)));
            BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), surface);
            foreach (Float sine in new Float[] { 0.3f, 0.8f })
            {
                Vector3f wo = new Vector3f(sine, 0, SafeSqrt(1 - sine * sine));
                int count = 60000;
                List<double> plain = new List<double>(), withMatrix = new List<double>(), first = new List<double>(), values = new List<double>(), valuesWith = new List<double>();
                for (int i = 0; i < count; ++i)
                {
                    Float uc = rng.Uniform<Float>();
                    Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    pstd.optional<BSDFSample> bs = surface.Sample_f(frame, wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All, out bool polarised, out Attenuation A);
                    if (bs && bs.value().pdf > 0)
                    {
                        double most = 0, mostWith = 0, m00 = 1;
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            double w = bs.value().f[c] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                            double m = polarised ? A.GetMuellerMatrix().Get(0, 0)[c] : 1;
                            most = Math.Max(most, w);
                            if (w * m > mostWith) { mostWith = w * m; m00 = m; }
                        }
                        plain.Add(most); withMatrix.Add(mostWith); first.Add(m00);
                    }
                    Vector3f wi = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    SampledSpectrum f = surface.f(frame, wo, wi, TransportMode.Radiance, out polarised, out A);
                    double v = 0, vWith = 0;
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        double m = polarised ? A.GetMuellerMatrix().Get(0, 0)[c] : 1;
                        v = Math.Max(v, f[c] * wi.z);
                        vWith = Math.Max(vWith, f[c] * wi.z * m);
                    }
                    values.Add(v); valuesWith.Add(vWith);
                }
                plain.Sort(); withMatrix.Sort(); first.Sort(); values.Sort(); valuesWith.Sort();
                int n = plain.Count;
                EXPECT_LT(withMatrix[n - 1], 12.0);
                Console.WriteLine("sine {0}: sample weight 99.9% {1:F2} max {2:F2}; with first element 99.9% {3:F2} 99.99% {4:F2} max {5:F2}; first element min {6:F3} max {7:F3}; value max {8:F2} with {9:F2}, 99.99% {10:F2} with {11:F2}",
                                  sine, plain[n * 999 / 1000], plain[n - 1], withMatrix[n * 999 / 1000], withMatrix[n - 20], withMatrix[n - 1], first[0], first[n - 1],
                                  values[count - 1], valuesWith[count - 1], values[count - 20], valuesWith[count - 20]);
            }
        }

        // Mirrors, smooth and rough, with other interfaces over a base: the value is what the samples carry, with and without
        // polarisation. (One layered BxDF of pbrt inside another gets two of these wrong: with a rough mirror over a smooth
        // one its value is 0.078 where the samples carry 0.095, and with two rough mirrors 0.698 against 0.552. The material
        // has its interfaces side by side for that reason.)
        [TEST("Bragg", "Stacks")]
        public static void Stacks()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            RNG rng = new RNG(29);
            ThinFilmStack films = new ThinFilmStack(lambda, new Float[] { 1f, 1.5f, 1f, 1.5f }, new Float[] { 125, 375, 101, 143 });
            TrowbridgeReitzDistribution rough = new TrowbridgeReitzDistribution(0.15f, 0.15f), smooth = new TrowbridgeReitzDistribution(0, 0);
            string[] names = new string[] { "rough mirror, smooth mirror, base", "smooth mirror, rough mirror, base", "rough glass, rough mirror, base",
                                            "rough mirror, rough glass, base", "rough mirror, smooth glass, base", "rough mirror, rough mirror, base" };
            for (int kind = 0; kind < 6; ++kind)
            {
                BxDF first, second;
                switch (kind)
                {
                    case 0: first = new BraggLayerBxDF(films.Part(0, 2, 20), 1f, rough); second = new BraggLayerBxDF(films.Part(2, 2, 20), 1f, smooth); break;
                    case 1: first = new BraggLayerBxDF(films.Part(0, 2, 20), 1f, smooth); second = new BraggLayerBxDF(films.Part(2, 2, 20), 1f, rough); break;
                    case 2: first = new DielectricBxDF(1.3f, rough); second = new BraggLayerBxDF(films.Part(2, 2, 20), 1f, rough); break;
                    case 3: first = new BraggLayerBxDF(films.Part(0, 2, 20), 1f, rough); second = new DielectricBxDF(1.3f, rough); break;
                    case 4: first = new BraggLayerBxDF(films.Part(0, 2, 20), 1f, rough); second = new DielectricBxDF(1.3f, smooth); break;
                    default: first = new BraggLayerBxDF(films.Part(0, 2, 20), 1f, rough); second = new BraggLayerBxDF(films.Part(2, 2, 20), 1f, rough); break;
                }
                BxDF stack = Flat(true, first, second, new DiffuseBxDF(new SampledSpectrum(0.3f)));
                BSDF frame = new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), stack);
                Vector3f wo = Normalize(new Vector3f(0.5f, 0.1f, 0.8f));
                int count = 150000;
                // (0: value; 1: samples; 2: value with the first element of the matrix; 3: samples with it)
                double[] sum = new double[4], squares = new double[4], seconds = new double[4];
                System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
                for (int i = 0; i < count; ++i)
                {
                    Vector3f wi = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Float uc = rng.Uniform<Float>();
                    Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    watch.Restart();
                    double v = stack.f(wo, wi, TransportMode.Radiance)[0] * wi.z * 2 * Math.PI;
                    seconds[0] += watch.Elapsed.TotalSeconds;
                    sum[0] += v;
                    squares[0] += v * v;
                    watch.Restart();
                    pstd.optional<BSDFSample> bs = stack.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                    seconds[1] += watch.Elapsed.TotalSeconds;
                    if (bs && bs.value().pdf > 0 && !bs.value().IsSpecular())
                    {
                        double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                        sum[1] += w;
                        squares[1] += w * w;
                    }
                    watch.Restart();
                    SampledSpectrum value = stack.f(frame, wo, wi, TransportMode.Radiance, out bool polarised, out Attenuation A);
                    seconds[2] += watch.Elapsed.TotalSeconds;
                    v = value[0] * wi.z * 2 * Math.PI * (polarised ? A.GetMuellerMatrix().Get(0, 0)[0] : 1);
                    sum[2] += v;
                    squares[2] += v * v;
                    watch.Restart();
                    bs = stack.Sample_f(frame, wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All, out polarised, out A);
                    seconds[3] += watch.Elapsed.TotalSeconds;
                    if (bs && bs.value().pdf > 0 && !bs.value().IsSpecular())
                    {
                        double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf * (polarised ? A.GetMuellerMatrix().Get(0, 0)[0] : 1);
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
                Console.WriteLine("{0}: value {1:F4} +- {2:F4}, samples {3:F4} +- {4:F4}; with polarisation {5:F4} +- {6:F4} and {7:F4} +- {8:F4}; " +
                                  "microseconds per call {9:F0} {10:F0} {11:F0} {12:F0}", names[kind], mean[0], error[0], mean[1], error[1], mean[2], error[2],
                                  mean[3], error[3], seconds[0] / count * 1e6, seconds[1] / count * 1e6, seconds[2] / count * 1e6, seconds[3] / count * 1e6);
                EXPECT_LT(Math.Abs(mean[0] - mean[1]), 4 * Math.Sqrt(error[0] * error[0] + error[1] * error[1]) + 0.0005);
                EXPECT_LT(Math.Abs(mean[2] - mean[3]), 4 * Math.Sqrt(error[2] * error[2] + error[3] * error[3]) + 0.0005);
            }
        }

        [TEST("Bragg", "LayeredStructure")]
        public static void LayeredStructure()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.37f);
            RNG rng = new RNG(21);
            foreach (Float alpha in new Float[] { 0f, 0.1f, 0.4f })
            {
                // (nothing absorbs: the mirror over a white base returns all the light, smooth or rough)
                BxDF stack = Flat(true, Walked(lambda, alpha), new DiffuseBxDF(new SampledSpectrum(1f)));
                foreach (Float cosine in new Float[] { 0.9f, 0.4f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine), 0, cosine);
                    double albedo = 0;
                    int count = 60000;
                    for (int i = 0; i < count; ++i)
                    {
                        pstd.optional<BSDFSample> bs = stack.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                      TransportMode.Radiance, BxDFReflTransFlags.All);
                        if (bs && bs.value().pdf > 0)
                        {
                            albedo += bs.value().f.Average() * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                        }
                    }
                    albedo /= count;
                    Console.WriteLine("alpha " + alpha + " cosine " + cosine + ": albedo over a white base " + albedo.ToString("F4"));
                    EXPECT_LT(Math.Abs(albedo - 1), 0.01);
                }
            }
        }
    }
}
