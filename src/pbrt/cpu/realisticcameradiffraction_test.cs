// (not in pbrt-v4) realisticcameradiffraction_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class RealisticCameraDiffractionTests
    {
        // The Bessel function J_n(x) from its integral (1 / pi) integral_0^pi cos(n t - x sin t) dt
        static double Bessel(int order, double x)
        {
            const int steps = 4000;
            double sum = 0;
            for (int i = 0; i < steps; ++i)
            {
                double t = Math.PI * (i + 0.5) / steps;
                sum += Math.Cos(order * t - x * Math.Sin(t));
            }

            return sum / steps;
        }

        // The fraction of the light of an Airy pattern inside x = k s r: 1 - J0(x)^2 - J1(x)^2
        static double AiryEncircled(double x)
        {
            double j0 = Bessel(0, x);
            double j1 = Bessel(1, x);
            return 1 - j0 * j0 - j1 * j1;
        }

        // The width of a pixel on the film of the test cameras (a square film of 35 mm diagonal) in metres
        static double PixelPitch(int resolution)
        {
            return 0.035 / Math.Sqrt(2) / resolution;
        }

        // The samples of the point spread function at the centre of the film as displacements in metres
        static List<double[]> Displacements(RealisticCamera camera, int resolution, Float wavelength, int count)
        {
            RNG rng = new RNG(77);
            List<double[]> result = new List<double[]>();
            Point2f centre = new Point2f(resolution / 2.0f, resolution / 2.0f);
            for (int i = 0; i < count; ++i)
            {
                bool moved = camera.SampleDiffractionOffset(centre, wavelength, rng.Uniform<Float>(), rng.Uniform<Float>(),
                                                            new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                            out Vector2f offset);
                EXPECT_TRUE(moved);
                result.Add(new double[] { offset.x * PixelPitch(resolution), offset.y * PixelPitch(resolution) });
            }

            return result;
        }

        [TEST("RealisticCameraDiffraction", "AiryPattern")]
        public static void RealisticCameraDiffraction_AiryPattern()
        {
            // On the axis the pupil is the circle of the stop: the light inside a radius r is that of an Airy pattern, 1 - J0^2 -
            // J1^2 of x = 2 pi s r / lambda, with s the sine of the circle of the pupil. Also for other wavelengths (the scaling).
            int resolution = 64;
            RealisticCamera camera = RealisticCameraPolarisedTests.MakeCamera(new Point2i(resolution, resolution),
                "realisticcameradiffraction_test.exr", null, null, null, null, null, true);
            EXPECT_TRUE(camera.HasDiffraction());
            EXPECT_TRUE(camera.DiffractionTableInfo(3, 3, out double area, out double cellSize));
            double sine = Math.Sqrt(area / Math.PI);
            double[] xs = { 1.5, 3.8317, 5.1356, 7.0156 };
            Float[] wavelengths = { 450, 550, 650 };
            foreach (Float wavelength in wavelengths)
            {
                List<double[]> displacements = Displacements(camera, resolution, wavelength, 100000);
                foreach (double x in xs)
                {
                    double radius = x * wavelength * 1e-9 / (2 * Math.PI * sine);
                    int inside = 0;
                    foreach (double[] d in displacements)
                    {
                        if (Math.Sqrt(d[0] * d[0] + d[1] * d[1]) < radius)
                        {
                            ++inside;
                        }
                    }

                    double encircled = (double)inside / displacements.Count;
                    EXPECT_NEAR(encircled, AiryEncircled(x), 0.015, "wavelength " + wavelength + ", x " + x + ": " + encircled);
                }
            }

            EXPECT_GT(cellSize, 0.0);
        }

        [TEST("RealisticCameraDiffraction", "VignettedPupil")]
        public static void RealisticCameraDiffraction_VignettedPupil()
        {
            // The pupil of a film point at the edge of the film is smaller than that on the axis (the other elements of the lens cut
            // it), so its point spread function is wider
            int resolution = 64;
            RealisticCamera camera = RealisticCameraPolarisedTests.MakeCamera(new Point2i(resolution, resolution),
                "realisticcameradiffraction_test.exr", null, null, null, null, null, true);
            EXPECT_TRUE(camera.DiffractionTableInfo(3, 3, out double centre, out double centreCell));
            EXPECT_TRUE(camera.DiffractionTableInfo(0, 0, out double corner, out double cornerCell));
            EXPECT_LT(corner, 0.97 * centre, "corner " + corner + ", centre " + centre);
            EXPECT_GT(corner, 0.1 * centre);
        }

        [TEST("RealisticCameraDiffraction", "SquareAperture")]
        public static void RealisticCameraDiffraction_SquareAperture()
        {
            // A square stop spreads the light along the two axes of the square (the transform of a square is a product of sincs):
            // far from the centre there is much more light in the directions of the axes than in the diagonal directions. The stop
            // is smaller than the apertures of the elements, which at the full stop of the lens cut the corners of the square.
            int resolution = 64;
            Image square = new Image(PixelFormat.Float, new Point2i(64, 64), new string[] { "Y" }, null, (Allocator)null);
            for (int y = 0; y < 64; ++y)
            {
                for (int x = 0; x < 64; ++x)
                {
                    square.SetChannel(new Point2i(x, y), 0, 1);
                }
            }

            RealisticCamera camera = RealisticCameraPolarisedTests.MakeCamera(new Point2i(resolution, resolution),
                "realisticcameradiffraction_test.exr", null, null, null, null, null, true, square, 8);
            EXPECT_TRUE(camera.DiffractionTableInfo(3, 3, out double area, out double cellSize));
            double side = Math.Sqrt(area);
            double unit = 550e-9 / side;
            int axis = 0;
            int diagonal = 0;
            foreach (double[] d in Displacements(camera, resolution, 550, 200000))
            {
                double radius = Math.Sqrt(d[0] * d[0] + d[1] * d[1]) / unit;
                if (radius < 3 || radius > 12)
                {
                    continue;
                }

                double angle = Math.Abs(Math.Atan2(d[1], d[0]));
                angle = Math.Abs(angle % (Math.PI / 2));
                if (angle < 0.2 || angle > Math.PI / 2 - 0.2)
                {
                    ++axis;
                }
                else if (Math.Abs(angle - Math.PI / 4) < 0.2)
                {
                    ++diagonal;
                }
            }

            EXPECT_GT(diagonal, 0);
            EXPECT_GT(axis, 4 * diagonal, "axes " + axis + ", diagonals " + diagonal);
        }

        [TEST("RealisticCameraDiffraction", "EnergyAndIntegrators")]
        public static void RealisticCameraDiffraction_EnergyAndIntegrators()
        {
            // Diffraction moves light on the film and keeps its amount: the mean of the image of the closed emissive sphere is that
            // without diffraction, for volpath and for bdpt (whose light subpaths splat through the camera)
            double reference = RealisticCameraPolarisedTests.RenderMean(false);
            double volpath = RealisticCameraPolarisedTests.RenderMean(false, null, null, true);
            double bdpt = RealisticCameraPolarisedTests.RenderMean(true, null, null, true);
            EXPECT_GT(reference, 0.0);
            EXPECT_NEAR(volpath, reference, 0.015 * reference, "reference " + reference + ", volpath " + volpath);
            EXPECT_NEAR(bdpt, volpath, 0.015 * reference, "volpath " + volpath + ", bdpt " + bdpt);
        }
    }
}
