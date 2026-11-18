// (not in pbrt-v4) Tests of the index of refraction from the absorption (anomalousdispersion.cs).

using System;
using System.Collections.Generic;
using System.Numerics;
using testing;

namespace pbrt
{
    public static unsafe class AnomalousDispersionTests
    {
        static ParsedParameter Parameter(string type, string name, params Float[] values)
        {
            ParsedParameter p = new ParsedParameter(new FileLoc());
            p.type = type;
            p.name = name;
            foreach (Float v in values)
            {
                p.AddFloat(v);
            }
            return p;
        }

        static TextureParameterDictionary Dictionary(params ParsedParameter[] parameters)
        {
            return new TextureParameterDictionary(new ParameterDictionary(new List<ParsedParameter>(parameters), RGBColorSpace.sRGB), new NamedTextures());
        }

        // The complex index of a Lorentz oscillator on a constant background: a material whose index and absorption are known
        // together in closed form, n + i k = sqrt(n_b^2 + S w0^2 / (w0^2 - w^2 - i g w)) in the wavenumber w = 1 / lambda
        static Complex Lorentz(double lambda, double background, double centre, double width, double strength)
        {
            double w = 1 / lambda, w0 = 1 / centre, g = width / (centre * centre);
            return Complex.Sqrt(background * background + strength * w0 * w0 / (w0 * w0 - w * w - new Complex(0, g * w)));
        }

        // The paper's formula gives the index of a Lorentz band from its absorption alone: to 5e-5 with the range and step
        // used here, to 3e-4 with the paper's range, and exactly as the range widens
        [TEST("AnomalousDispersion", "LorentzBand")]
        public static void LorentzBand()
        {
            double background = 1.333, centre = 550, width = 30, strength = 0.00582;
            double[] firsts = new double[] { 300, 300, 100 };
            double[] lasts = new double[] { 900, 720, 20000 };
            int[] counts = new int[] { 121, 85, 19901 };
            double[] limits = new double[] { 5e-5, 3e-4, 1e-6 };
            for (int c = 0; c < 3; ++c)
            {
                double[] k = new double[counts[c]];
                double h = (lasts[c] - firsts[c]) / (counts[c] - 1);
                double peak = 0;
                for (int i = 0; i < k.Length; ++i)
                {
                    k[i] = Lorentz(firsts[c] + i * h, background, centre, width, strength).Imaginary;
                    peak = Math.Max(peak, k[i]);
                }
                // (a band as strong as the strongest solution of the paper)
                EXPECT_LT(Math.Abs(peak - 0.04), 0.002);
                double[] correction = AnomalousDispersionSpectrum.Maclaurin(k, firsts[c], lasts[c]);
                double worst = 0, lowest = 10, highest = 0;
                for (int i = 0; i < k.Length; ++i)
                {
                    double lambda = firsts[c] + i * h;
                    if (lambda < 400 || lambda > 700)
                    {
                        continue;
                    }
                    double expected = Lorentz(lambda, background, centre, width, strength).Real;
                    worst = Math.Max(worst, Math.Abs(background + correction[i] - expected));
                    lowest = Math.Min(lowest, expected);
                    highest = Math.Max(highest, expected);
                }
                Console.WriteLine("range " + firsts[c] + " to " + lasts[c] + " nm, " + counts[c] + " samples: index " + lowest + " to " + highest + ", largest error " + worst);
                EXPECT_LT(worst, limits[c]);
                EXPECT_GT(highest - lowest, 0.035);
            }
        }

        // The spectrum of a material: the anomalous shape (the index is lower on the short side of the band than on the long
        // side, and rises through the band with the wavelength), the same from the extinction and from the absorption, and the
        // background alone without either
        [TEST("AnomalousDispersion", "MaterialIndex")]
        public static void MaterialIndex()
        {
            Allocator alloc = new Allocator();
            // (a Gaussian band at 550 nm; the absorption per micrometre, the scene unit, that has the extinction 0.04 there)
            Float peak = (Float)(0.04 * 4 * Math.PI / 550e-9 * 1e-6);
            Spectrum fromAbsorption = ParametricSpectra.Index(Dictionary(Parameter("float", "eta", 1.333f), Parameter("gaussian", "absorption", 550, 20, peak),
                                                                         Parameter("float", "metersperunit", 1e-6f)), "eta", alloc);
            EXPECT_TRUE(fromAbsorption is AnomalousDispersionSpectrum);
            AnomalousDispersionSpectrum index = (AnomalousDispersionSpectrum)fromAbsorption;
            EXPECT_LT(Math.Abs(index.Extinction(550) - 0.04), 1e-6);
            // (anomalous: the index rises with the wavelength through the band)
            EXPECT_LT(index.Evaluate(530), 1.333f - 0.01f);
            EXPECT_GT(index.Evaluate(570), 1.333f + 0.01f);
            EXPECT_LT(index.Evaluate(530), index.Evaluate(550));
            EXPECT_LT(index.Evaluate(550), index.Evaluate(570));
            // (normal on both sides of it: the index falls with the wavelength)
            EXPECT_GT(index.Evaluate(420), index.Evaluate(480));
            EXPECT_GT(index.Evaluate(620), index.Evaluate(700));
            // (antisymmetric about a narrow band, nearly)
            EXPECT_LT(Math.Abs(index.Correction(550)), 2e-3);

            // The same band as an extinction coefficient: a Gaussian in k is not the same curve as a Gaussian in the absorption
            // (k has the factor of the wavelength), so the table of k is made from the first spectrum
            List<Float> pairs = new List<Float>();
            for (int i = 0; i <= 120; ++i)
            {
                Float lambda = 300 + 5 * i;
                pairs.Add(lambda);
                pairs.Add(index.Extinction(lambda));
            }
            Spectrum fromExtinction = ParametricSpectra.Index(Dictionary(Parameter("float", "eta", 1.333f), Parameter("spectrum", "extinction", pairs.ToArray())), "eta", alloc);
            for (Float lambda = 400; lambda <= 700; lambda += 7)
            {
                EXPECT_LT(Math.Abs(fromExtinction.Evaluate(lambda) - fromAbsorption.Evaluate(lambda)), 1e-6);
            }

            // The concentration scales the correction; a Cauchy background stays under it; no absorption, no change
            Spectrum third = ParametricSpectra.Index(Dictionary(Parameter("float", "cauchy", 1.32f, 0.004f), Parameter("gaussian", "absorption", 550, 20, peak),
                                                                Parameter("float", "metersperunit", 1e-6f), Parameter("float", "absorptionscale", 1f / 3)), "eta", alloc);
            Float cauchy = CauchySpectrum.Value(1.32f, 0.004f, 0, 570);
            EXPECT_LT(Math.Abs((third.Evaluate(570) - cauchy) - index.Correction(570) / 3), 1e-6);
            EXPECT_TRUE(ParametricSpectra.Index(Dictionary(Parameter("float", "eta", 1.333f)), "eta", alloc) is ConstantSpectrum);
            EXPECT_TRUE(ParametricSpectra.Index(Dictionary(Parameter("float", "cauchy", 1.32f, 0.004f)), "eta", alloc) is CauchySpectrum);
        }
    }
}
