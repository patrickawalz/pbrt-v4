// (not in pbrt-v4) realisticcameraglass_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class RealisticCameraGlassTests
    {
        // The glasses behind each interface of the double Gauss lens ("" for air and the stop)
        static readonly string[] glasses = { "BK7", "", "SF11", "BK7", "", "", "SF11", "BK7", "", "BK7", "" };

        static List<Float> UniformSellmeier(double[] coefficients)
        {
            List<Float> values = new List<Float>();
            for (int i = 0; i < glasses.Length; ++i)
            {
                for (int k = 0; k < 6; ++k)
                {
                    values.Add(glasses[i].Length > 0 ? (Float)coefficients[k] : 0);
                }
            }

            return values;
        }

        static List<string> Names(string name)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < glasses.Length; ++i)
            {
                names.Add(glasses[i].Length > 0 ? name : "");
            }

            return names;
        }

        [TEST("RealisticCameraGlass", "CatalogueIndices")]
        public static void RealisticCameraGlass_CatalogueIndices()
        {
            // The d line index and the Abbe number V_d = (n_d - 1) / (n_F - n_C) against the catalogue values of the glasses
            string[] names = { "BK7", "SF11", "fused silica" };
            double[] indexD = { 1.5168, 1.78472, 1.45846 };
            double[] abbe = { 64.17, 25.68, 67.82 };
            for (int g = 0; g < 3; ++g)
            {
                double[] coefficients = RealisticCamera.CatalogueGlass(names[g]);
                EXPECT_TRUE(coefficients != null, names[g]);
                double nd = RealisticCamera.SellmeierIndex(coefficients, 0.5875618);
                double nF = RealisticCamera.SellmeierIndex(coefficients, 0.4861327);
                double nC = RealisticCamera.SellmeierIndex(coefficients, 0.6562725);
                EXPECT_NEAR(nd, indexD[g], 1e-4, names[g] + " n_d " + nd);
                EXPECT_NEAR((nd - 1) / (nF - nC), abbe[g], 0.15, names[g] + " V_d " + (nd - 1) / (nF - nC));
            }

            EXPECT_TRUE(RealisticCamera.CatalogueGlass("unobtainium") == null);
        }

        [TEST("RealisticCameraGlass", "NamesAreCoefficients")]
        public static void RealisticCameraGlass_NamesAreCoefficients()
        {
            // A catalogue name and its Sellmeier coefficients give the same camera, and the camera disperses: blue and red leave
            // the lens in different directions
            Point2i resolution = new Point2i(64, 64);
            List<string> names = Names("SF11");
            RealisticCamera named = RealisticCameraPolarisedTests.MakeCamera(resolution, "realisticcameraglass_test.exr", null,
                                                                             null, null, null, names);
            RealisticCamera coefficients = RealisticCameraPolarisedTests.MakeCamera(resolution, "realisticcameraglass_test.exr",
                null, null, UniformSellmeier(RealisticCamera.CatalogueGlass("SF11")));
            RealisticCamera plain = RealisticCameraPolarisedTests.MakeCamera(resolution);
            RNG rng = new RNG(41);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            int tested = 0;
            for (int i = 0; i < 1000; ++i)
            {
                Point3f reference = new Point3f(1.2f * (rng.Uniform<Float>() - 0.5f), 0.9f * (rng.Uniform<Float>() - 0.5f),
                                                1.5f + 3 * rng.Uniform<Float>());
                Interaction referenceInteraction = new Interaction(reference, new Normal3f(0, 0, -1), 0, null);
                pstd.optional<CameraWiSample> sample = named.SampleWi(referenceInteraction,
                    new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), ref lambda);
                if (!sample.has_value())
                {
                    continue;
                }

                Ray ray = new Ray(sample.value().pLens.p(), -sample.value().wi);
                if (!named.PolarisedSensor(ray, 450, out PolarisedLightSystem.Attenuation opticsBlue, out Vector3f blue,
                                           out Vector3f upBlue) ||
                    !named.PolarisedSensor(ray, 650, out PolarisedLightSystem.Attenuation opticsRed, out Vector3f red,
                                           out Vector3f upRed) ||
                    !coefficients.PolarisedSensor(ray, 450, out PolarisedLightSystem.Attenuation opticsBlueC, out Vector3f blueC,
                                                  out Vector3f upBlueC) ||
                    !plain.PolarisedSensor(ray, 0, out PolarisedLightSystem.Attenuation opticsPlain, out Vector3f d,
                                           out Vector3f upPlain) ||
                    !named.PolarisedSensor(ray, 0, out PolarisedLightSystem.Attenuation opticsD, out Vector3f dNamed,
                                           out Vector3f upD))
                {
                    continue;
                }

                EXPECT_NEAR(blue.x, blueC.x, 1e-6f);
                EXPECT_NEAR(blue.y, blueC.y, 1e-6f);
                EXPECT_NEAR(blue.z, blueC.z, 1e-6f);
                // The d line keeps the index of the lens file
                EXPECT_NEAR(d.x, dNamed.x, 1e-5f);
                EXPECT_NEAR(d.y, dNamed.y, 1e-5f);
                EXPECT_GT(pbrt.Length(blue - red), 1e-4f);
                ++tested;
            }

            EXPECT_GT(tested, 300);
        }

        [TEST("RealisticCameraGlass", "FlintDispersesMoreThanCrown")]
        public static void RealisticCameraGlass_FlintDispersesMoreThanCrown()
        {
            // The same lens with SF11 (V_d 25.7) in every glass spreads blue and red further apart than with BK7 (V_d 64.2); a
            // Cauchy relation without terms in 1 / lambda^2 does not spread them at all
            Point2i resolution = new Point2i(64, 64);
            RealisticCamera flint = RealisticCameraPolarisedTests.MakeCamera(resolution, "realisticcameraglass_test.exr", null,
                                                                             null, null, null, Names("SF11"));
            RealisticCamera crown = RealisticCameraPolarisedTests.MakeCamera(resolution, "realisticcameraglass_test.exr", null,
                                                                             null, null, null, Names("BK7"));
            List<Float> flat = new List<Float>();
            for (int i = 0; i < glasses.Length; ++i)
            {
                flat.Add(1.5f);
                flat.Add(0);
                flat.Add(0);
            }

            RealisticCamera noDispersion = RealisticCameraPolarisedTests.MakeCamera(resolution, "realisticcameraglass_test.exr",
                                                                                    null, null, null, flat);
            RNG rng = new RNG(43);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            double flintSpread = 0;
            double crownSpread = 0;
            double flatSpread = 0;
            int tested = 0;
            for (int i = 0; i < 1000; ++i)
            {
                Point3f reference = new Point3f(1.2f * (rng.Uniform<Float>() - 0.5f), 0.9f * (rng.Uniform<Float>() - 0.5f),
                                                1.5f + 3 * rng.Uniform<Float>());
                Interaction referenceInteraction = new Interaction(reference, new Normal3f(0, 0, -1), 0, null);
                pstd.optional<CameraWiSample> sample = flint.SampleWi(referenceInteraction,
                    new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), ref lambda);
                if (!sample.has_value())
                {
                    continue;
                }

                Ray ray = new Ray(sample.value().pLens.p(), -sample.value().wi);
                Vector3f[] flintDirections = new Vector3f[2];
                Vector3f[] crownDirections = new Vector3f[2];
                Vector3f[] flatDirections = new Vector3f[2];
                bool all = true;
                Float[] wavelengths = { 450, 650 };
                for (int k = 0; k < 2; ++k)
                {
                    all &= flint.PolarisedSensor(ray, wavelengths[k], out PolarisedLightSystem.Attenuation o0,
                                                 out flintDirections[k], out Vector3f u0);
                    all &= crown.PolarisedSensor(ray, wavelengths[k], out PolarisedLightSystem.Attenuation o1,
                                                 out crownDirections[k], out Vector3f u1);
                    all &= noDispersion.PolarisedSensor(ray, wavelengths[k], out PolarisedLightSystem.Attenuation o2,
                                                        out flatDirections[k], out Vector3f u2);
                }

                if (!all)
                {
                    continue;
                }

                flintSpread += pbrt.Length(flintDirections[0] - flintDirections[1]);
                crownSpread += pbrt.Length(crownDirections[0] - crownDirections[1]);
                flatSpread += pbrt.Length(flatDirections[0] - flatDirections[1]);
                ++tested;
            }

            EXPECT_GT(tested, 300);
            EXPECT_GT(flintSpread, 1.3 * crownSpread, "flint " + flintSpread + ", crown " + crownSpread);
            EXPECT_LT(flatSpread, 1e-6 * tested, "A-only Cauchy " + flatSpread);
        }

        [TEST("RealisticCameraGlass", "BDPTMatchesVolPath")]
        public static void RealisticCameraGlass_BDPTMatchesVolPath()
        {
            List<string> names = new List<string>(glasses);
            double volpath = RealisticCameraPolarisedTests.RenderMean(false, null, names);
            double bdpt = RealisticCameraPolarisedTests.RenderMean(true, null, names);
            EXPECT_GT(volpath, 0.0);
            EXPECT_NEAR(volpath, bdpt, 0.015 * volpath, "volpath " + volpath + ", bdpt " + bdpt);
        }
    }
}
