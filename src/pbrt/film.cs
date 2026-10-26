// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// film.h / film.cpp

// PhysLight code contributed by Anders Langlands and Luca Fascione
// Copyright (c) 2020, Weta Digital, Ltd.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace pbrt
{

    // PixelSensor Definition
    public sealed unsafe class PixelSensor
    {
        // PixelSensor Public Methods
        public PixelSensor(Spectrum r, Spectrum g, Spectrum b, RGBColorSpace outputColorSpace, Spectrum sensorIllum,
                           Float imagingRatio, Allocator alloc)
        {
            r_bar = new DenselySampledSpectrum(r, alloc);
            g_bar = new DenselySampledSpectrum(g, alloc);
            b_bar = new DenselySampledSpectrum(b, alloc);
            this.imagingRatio = imagingRatio;
            XYZFromSensorRGB = new SquareMatrix3();
            // Compute XYZ from camera RGB matrix
            // Compute _rgbCamera_ values for training swatches
            Float[,] rgbCamera = new Float[nSwatchReflectances, 3];
            for (int i = 0; i < nSwatchReflectances; ++i)
            {
                RGB rgb = ProjectReflectance_RGB(swatchReflectances[i], sensorIllum, r_bar, g_bar, b_bar);
                for (int c = 0; c < 3; ++c)
                    rgbCamera[i, c] = rgb[c];
            }

            // Compute _xyzOutput_ values for training swatches
            Float[,] xyzOutput = new Float[24, 3];
            Float sensorWhiteG = Globals.InnerProduct(sensorIllum, g_bar);
            Float sensorWhiteY = Globals.InnerProduct(sensorIllum, Spectra.Y());
            for (int i = 0; i < nSwatchReflectances; ++i)
            {
                Spectrum s = swatchReflectances[i];
                XYZ xyz = ProjectReflectance_XYZ(s, outputColorSpace.illuminant, Spectra.X(), Spectra.Y(), Spectra.Z()) *
                          (sensorWhiteY / sensorWhiteG);
                for (int c = 0; c < 3; ++c)
                    xyzOutput[i, c] = xyz[c];
            }

            // Initialize _XYZFromSensorRGB_ using linear least squares
            pstd.optional<SquareMatrix3> m = Globals.LinearLeastSquares3(rgbCamera, xyzOutput, nSwatchReflectances);
            if (!m)
                Globals.ErrorExit("Sensor XYZ from RGB matrix could not be solved.");
            XYZFromSensorRGB = m.value();
        }

        public PixelSensor(RGBColorSpace outputColorSpace, Spectrum sensorIllum, Float imagingRatio, Allocator alloc)
        {
            r_bar = new DenselySampledSpectrum(Spectra.X(), alloc);
            g_bar = new DenselySampledSpectrum(Spectra.Y(), alloc);
            b_bar = new DenselySampledSpectrum(Spectra.Z(), alloc);
            this.imagingRatio = imagingRatio;
            XYZFromSensorRGB = new SquareMatrix3();
            // Compute white balancing matrix for XYZ _PixelSensor_
            if (sensorIllum != null)
            {
                Point2f sourceWhite = Globals.SpectrumToXYZ(sensorIllum).xy();
                Point2f targetWhite = outputColorSpace.w;
                XYZFromSensorRGB = Globals.WhiteBalance(sourceWhite, targetWhite);
            }
        }

        public RGB ToSensorRGB(SampledSpectrum L, SampledWavelengths lambda)
        {
            L = Globals.SafeDiv(L, lambda.PDF());
            return imagingRatio * new RGB((r_bar.Sample(lambda) * L).Average(), (g_bar.Sample(lambda) * L).Average(),
                                          (b_bar.Sample(lambda) * L).Average());
        }

        // PixelSensor Method Definitions
        public static PixelSensor Create(ParameterDictionary parameters, RGBColorSpace colorSpace, Float exposureTime,
                                         FileLoc loc, Allocator alloc)
        {
            // Imaging ratio parameters
            // The defaults here represent a "passthrough" setup such that the imaging
            // ratio will be exactly 1. This is a useful default since scenes that
            // weren't authored with a physical camera in mind will render as expected.
            Float ISO = parameters.GetOneFloat("iso", 100.0f);
            Float whiteBalanceTemp = parameters.GetOneFloat("whitebalance", 0);

            string sensorName = parameters.GetOneString("sensor", "cie1931");

            // Pass through 0 for cie1931 if it's unspecified so that it doesn't do
            // any white balancing. For actual sensors, 6500 is the default...
            if (sensorName != "cie1931" && whiteBalanceTemp == 0)
                whiteBalanceTemp = 6500;

            // Note: in the talk we mention using 312.5 for historical reasons. The
            // choice of 100 here just means that the other parameters make nice
            // "round" numbers like 1 and 100.
            Float imagingRatio = exposureTime * ISO / 100;

            DenselySampledSpectrum dIllum = Spectra.D(whiteBalanceTemp == 0 ? 6500.0f : whiteBalanceTemp, alloc);
            Spectrum sensorIllum = whiteBalanceTemp != 0 ? dIllum : null;

            if (sensorName == "cie1931")
            {
                return new PixelSensor(colorSpace, sensorIllum, imagingRatio, alloc);
            }
            else
            {
                Spectrum r = Globals.GetNamedSpectrum(sensorName + "_r");
                Spectrum g = Globals.GetNamedSpectrum(sensorName + "_g");
                Spectrum b = Globals.GetNamedSpectrum(sensorName + "_b");

                if (r == null || g == null || b == null)
                    Globals.ErrorExit(loc, Globals.StringPrintf("%s: unknown sensor type", sensorName));

                return new PixelSensor(r, g, b, colorSpace, sensorIllum, imagingRatio, alloc);
            }
        }

        public static PixelSensor CreateDefault(Allocator alloc = null)
        {
            return Create(new ParameterDictionary(), RGBColorSpace.sRGB, 1.0f, null, alloc);
        }

        // PixelSensor Public Members
        public SquareMatrix3 XYZFromSensorRGB;

        // PixelSensor Private Methods
        // (template <typename Triplet> ProjectReflectance, expanded for RGB and XYZ)
        static RGB ProjectReflectance_RGB(Spectrum refl, Spectrum illum, Spectrum b1, Spectrum b2, Spectrum b3)
        {
            RGB result = default;
            Float g_integral = 0;
            for (Float lambda = Lambda_min; lambda <= Lambda_max; ++lambda)
            {
                g_integral += b2.Apply(lambda) * illum.Apply(lambda);
                result[0] += b1.Apply(lambda) * refl.Apply(lambda) * illum.Apply(lambda);
                result[1] += b2.Apply(lambda) * refl.Apply(lambda) * illum.Apply(lambda);
                result[2] += b3.Apply(lambda) * refl.Apply(lambda) * illum.Apply(lambda);
            }
            return result / g_integral;
        }

        static XYZ ProjectReflectance_XYZ(Spectrum refl, Spectrum illum, Spectrum b1, Spectrum b2, Spectrum b3)
        {
            XYZ result = default;
            Float g_integral = 0;
            for (Float lambda = Lambda_min; lambda <= Lambda_max; ++lambda)
            {
                g_integral += b2.Apply(lambda) * illum.Apply(lambda);
                result[0] += b1.Apply(lambda) * refl.Apply(lambda) * illum.Apply(lambda);
                result[1] += b2.Apply(lambda) * refl.Apply(lambda) * illum.Apply(lambda);
                result[2] += b3.Apply(lambda) * refl.Apply(lambda) * illum.Apply(lambda);
            }
            return result / g_integral;
        }

        // PixelSensor Private Members
        DenselySampledSpectrum r_bar, g_bar, b_bar;
        Float imagingRatio;
        const int nSwatchReflectances = 24;

        // Swatch reflectances are taken from Danny Pascale's Macbeth chart measurements
        // BabelColor ColorChecker data: Copyright (c) 2004-2012 Danny Pascale
        // (www.babelcolor.com); used by permission.
        // http://www.babelcolor.com/index_htm_files/ColorChecker_RGB_and_spectra.zip
        static Spectrum[] swatchReflectances = new Spectrum[nSwatchReflectances] {
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.055f, 390.0f, 0.058f, 400.0f, 0.061f, 410.0f, 0.062f, 420.0f, 0.062f, 430.0f, 0.062f,
                440.0f, 0.062f, 450.0f, 0.062f, 460.0f, 0.062f, 470.0f, 0.062f, 480.0f, 0.062f, 490.0f, 0.063f,
                500.0f, 0.065f, 510.0f, 0.070f, 520.0f, 0.076f, 530.0f, 0.079f, 540.0f, 0.081f, 550.0f, 0.084f,
                560.0f, 0.091f, 570.0f, 0.103f, 580.0f, 0.119f, 590.0f, 0.134f, 600.0f, 0.143f, 610.0f, 0.147f,
                620.0f, 0.151f, 630.0f, 0.158f, 640.0f, 0.168f, 650.0f, 0.179f, 660.0f, 0.188f, 670.0f, 0.190f,
                680.0f, 0.186f, 690.0f, 0.181f, 700.0f, 0.182f, 710.0f, 0.187f, 720.0f, 0.196f, 730.0f, 0.209f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.117f, 390.0f, 0.143f, 400.0f, 0.175f, 410.0f, 0.191f, 420.0f, 0.196f, 430.0f, 0.199f,
                440.0f, 0.204f, 450.0f, 0.213f, 460.0f, 0.228f, 470.0f, 0.251f, 480.0f, 0.280f, 490.0f, 0.309f,
                500.0f, 0.329f, 510.0f, 0.333f, 520.0f, 0.315f, 530.0f, 0.286f, 540.0f, 0.273f, 550.0f, 0.276f,
                560.0f, 0.277f, 570.0f, 0.289f, 580.0f, 0.339f, 590.0f, 0.420f, 600.0f, 0.488f, 610.0f, 0.525f,
                620.0f, 0.546f, 630.0f, 0.562f, 640.0f, 0.578f, 650.0f, 0.595f, 660.0f, 0.612f, 670.0f, 0.625f,
                680.0f, 0.638f, 690.0f, 0.656f, 700.0f, 0.678f, 710.0f, 0.700f, 720.0f, 0.717f, 730.0f, 0.734f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.130f, 390.0f, 0.177f, 400.0f, 0.251f, 410.0f, 0.306f, 420.0f, 0.324f, 430.0f, 0.330f,
                440.0f, 0.333f, 450.0f, 0.331f, 460.0f, 0.323f, 470.0f, 0.311f, 480.0f, 0.298f, 490.0f, 0.285f,
                500.0f, 0.269f, 510.0f, 0.250f, 520.0f, 0.231f, 530.0f, 0.214f, 540.0f, 0.199f, 550.0f, 0.185f,
                560.0f, 0.169f, 570.0f, 0.157f, 580.0f, 0.149f, 590.0f, 0.145f, 600.0f, 0.142f, 610.0f, 0.141f,
                620.0f, 0.141f, 630.0f, 0.141f, 640.0f, 0.143f, 650.0f, 0.147f, 660.0f, 0.152f, 670.0f, 0.154f,
                680.0f, 0.150f, 690.0f, 0.144f, 700.0f, 0.136f, 710.0f, 0.132f, 720.0f, 0.135f, 730.0f, 0.147f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.051f, 390.0f, 0.054f, 400.0f, 0.056f, 410.0f, 0.057f, 420.0f, 0.058f, 430.0f, 0.059f,
                440.0f, 0.060f, 450.0f, 0.061f, 460.0f, 0.062f, 470.0f, 0.063f, 480.0f, 0.065f, 490.0f, 0.067f,
                500.0f, 0.075f, 510.0f, 0.101f, 520.0f, 0.145f, 530.0f, 0.178f, 540.0f, 0.184f, 550.0f, 0.170f,
                560.0f, 0.149f, 570.0f, 0.133f, 580.0f, 0.122f, 590.0f, 0.115f, 600.0f, 0.109f, 610.0f, 0.105f,
                620.0f, 0.104f, 630.0f, 0.106f, 640.0f, 0.109f, 650.0f, 0.112f, 660.0f, 0.114f, 670.0f, 0.114f,
                680.0f, 0.112f, 690.0f, 0.112f, 700.0f, 0.115f, 710.0f, 0.120f, 720.0f, 0.125f, 730.0f, 0.130f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.144f, 390.0f, 0.198f, 400.0f, 0.294f, 410.0f, 0.375f, 420.0f, 0.408f, 430.0f, 0.421f,
                440.0f, 0.426f, 450.0f, 0.426f, 460.0f, 0.419f, 470.0f, 0.403f, 480.0f, 0.379f, 490.0f, 0.346f,
                500.0f, 0.311f, 510.0f, 0.281f, 520.0f, 0.254f, 530.0f, 0.229f, 540.0f, 0.214f, 550.0f, 0.208f,
                560.0f, 0.202f, 570.0f, 0.194f, 580.0f, 0.193f, 590.0f, 0.200f, 600.0f, 0.214f, 610.0f, 0.230f,
                620.0f, 0.241f, 630.0f, 0.254f, 640.0f, 0.279f, 650.0f, 0.313f, 660.0f, 0.348f, 670.0f, 0.366f,
                680.0f, 0.366f, 690.0f, 0.359f, 700.0f, 0.358f, 710.0f, 0.365f, 720.0f, 0.377f, 730.0f, 0.398f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.136f, 390.0f, 0.179f, 400.0f, 0.247f, 410.0f, 0.297f, 420.0f, 0.320f, 430.0f, 0.337f,
                440.0f, 0.355f, 450.0f, 0.381f, 460.0f, 0.419f, 470.0f, 0.466f, 480.0f, 0.510f, 490.0f, 0.546f,
                500.0f, 0.567f, 510.0f, 0.574f, 520.0f, 0.569f, 530.0f, 0.551f, 540.0f, 0.524f, 550.0f, 0.488f,
                560.0f, 0.445f, 570.0f, 0.400f, 580.0f, 0.350f, 590.0f, 0.299f, 600.0f, 0.252f, 610.0f, 0.221f,
                620.0f, 0.204f, 630.0f, 0.196f, 640.0f, 0.191f, 650.0f, 0.188f, 660.0f, 0.191f, 670.0f, 0.199f,
                680.0f, 0.212f, 690.0f, 0.223f, 700.0f, 0.232f, 710.0f, 0.233f, 720.0f, 0.229f, 730.0f, 0.229f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.054f, 390.0f, 0.054f, 400.0f, 0.053f, 410.0f, 0.054f, 420.0f, 0.054f, 430.0f, 0.055f,
                440.0f, 0.055f, 450.0f, 0.055f, 460.0f, 0.056f, 470.0f, 0.057f, 480.0f, 0.058f, 490.0f, 0.061f,
                500.0f, 0.068f, 510.0f, 0.089f, 520.0f, 0.125f, 530.0f, 0.154f, 540.0f, 0.174f, 550.0f, 0.199f,
                560.0f, 0.248f, 570.0f, 0.335f, 580.0f, 0.444f, 590.0f, 0.538f, 600.0f, 0.587f, 610.0f, 0.595f,
                620.0f, 0.591f, 630.0f, 0.587f, 640.0f, 0.584f, 650.0f, 0.584f, 660.0f, 0.590f, 670.0f, 0.603f,
                680.0f, 0.620f, 690.0f, 0.639f, 700.0f, 0.655f, 710.0f, 0.663f, 720.0f, 0.663f, 730.0f, 0.667f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.122f, 390.0f, 0.164f, 400.0f, 0.229f, 410.0f, 0.286f, 420.0f, 0.327f, 430.0f, 0.361f,
                440.0f, 0.388f, 450.0f, 0.400f, 460.0f, 0.392f, 470.0f, 0.362f, 480.0f, 0.316f, 490.0f, 0.260f,
                500.0f, 0.209f, 510.0f, 0.168f, 520.0f, 0.138f, 530.0f, 0.117f, 540.0f, 0.104f, 550.0f, 0.096f,
                560.0f, 0.090f, 570.0f, 0.086f, 580.0f, 0.084f, 590.0f, 0.084f, 600.0f, 0.084f, 610.0f, 0.084f,
                620.0f, 0.084f, 630.0f, 0.085f, 640.0f, 0.090f, 650.0f, 0.098f, 660.0f, 0.109f, 670.0f, 0.123f,
                680.0f, 0.143f, 690.0f, 0.169f, 700.0f, 0.205f, 710.0f, 0.244f, 720.0f, 0.287f, 730.0f, 0.332f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.096f, 390.0f, 0.115f, 400.0f, 0.131f, 410.0f, 0.135f, 420.0f, 0.133f, 430.0f, 0.132f,
                440.0f, 0.130f, 450.0f, 0.128f, 460.0f, 0.125f, 470.0f, 0.120f, 480.0f, 0.115f, 490.0f, 0.110f,
                500.0f, 0.105f, 510.0f, 0.100f, 520.0f, 0.095f, 530.0f, 0.093f, 540.0f, 0.092f, 550.0f, 0.093f,
                560.0f, 0.096f, 570.0f, 0.108f, 580.0f, 0.156f, 590.0f, 0.265f, 600.0f, 0.399f, 610.0f, 0.500f,
                620.0f, 0.556f, 630.0f, 0.579f, 640.0f, 0.588f, 650.0f, 0.591f, 660.0f, 0.593f, 670.0f, 0.594f,
                680.0f, 0.598f, 690.0f, 0.602f, 700.0f, 0.607f, 710.0f, 0.609f, 720.0f, 0.609f, 730.0f, 0.610f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.092f, 390.0f, 0.116f, 400.0f, 0.146f, 410.0f, 0.169f, 420.0f, 0.178f, 430.0f, 0.173f,
                440.0f, 0.158f, 450.0f, 0.139f, 460.0f, 0.119f, 470.0f, 0.101f, 480.0f, 0.087f, 490.0f, 0.075f,
                500.0f, 0.066f, 510.0f, 0.060f, 520.0f, 0.056f, 530.0f, 0.053f, 540.0f, 0.051f, 550.0f, 0.051f,
                560.0f, 0.052f, 570.0f, 0.052f, 580.0f, 0.051f, 590.0f, 0.052f, 600.0f, 0.058f, 610.0f, 0.073f,
                620.0f, 0.096f, 630.0f, 0.119f, 640.0f, 0.141f, 650.0f, 0.166f, 660.0f, 0.194f, 670.0f, 0.227f,
                680.0f, 0.265f, 690.0f, 0.309f, 700.0f, 0.355f, 710.0f, 0.396f, 720.0f, 0.436f, 730.0f, 0.478f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.061f, 390.0f, 0.061f, 400.0f, 0.062f, 410.0f, 0.063f, 420.0f, 0.064f, 430.0f, 0.066f,
                440.0f, 0.069f, 450.0f, 0.075f, 460.0f, 0.085f, 470.0f, 0.105f, 480.0f, 0.139f, 490.0f, 0.192f,
                500.0f, 0.271f, 510.0f, 0.376f, 520.0f, 0.476f, 530.0f, 0.531f, 540.0f, 0.549f, 550.0f, 0.546f,
                560.0f, 0.528f, 570.0f, 0.504f, 580.0f, 0.471f, 590.0f, 0.428f, 600.0f, 0.381f, 610.0f, 0.347f,
                620.0f, 0.327f, 630.0f, 0.318f, 640.0f, 0.312f, 650.0f, 0.310f, 660.0f, 0.314f, 670.0f, 0.327f,
                680.0f, 0.345f, 690.0f, 0.363f, 700.0f, 0.376f, 710.0f, 0.381f, 720.0f, 0.378f, 730.0f, 0.379f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.063f, 390.0f, 0.063f, 400.0f, 0.063f, 410.0f, 0.064f, 420.0f, 0.064f, 430.0f, 0.064f,
                440.0f, 0.065f, 450.0f, 0.066f, 460.0f, 0.067f, 470.0f, 0.068f, 480.0f, 0.071f, 490.0f, 0.076f,
                500.0f, 0.087f, 510.0f, 0.125f, 520.0f, 0.206f, 530.0f, 0.305f, 540.0f, 0.383f, 550.0f, 0.431f,
                560.0f, 0.469f, 570.0f, 0.518f, 580.0f, 0.568f, 590.0f, 0.607f, 600.0f, 0.628f, 610.0f, 0.637f,
                620.0f, 0.640f, 630.0f, 0.642f, 640.0f, 0.645f, 650.0f, 0.648f, 660.0f, 0.651f, 670.0f, 0.653f,
                680.0f, 0.657f, 690.0f, 0.664f, 700.0f, 0.673f, 710.0f, 0.680f, 720.0f, 0.684f, 730.0f, 0.688f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.066f, 390.0f, 0.079f, 400.0f, 0.102f, 410.0f, 0.146f, 420.0f, 0.200f, 430.0f, 0.244f,
                440.0f, 0.282f, 450.0f, 0.309f, 460.0f, 0.308f, 470.0f, 0.278f, 480.0f, 0.231f, 490.0f, 0.178f,
                500.0f, 0.130f, 510.0f, 0.094f, 520.0f, 0.070f, 530.0f, 0.054f, 540.0f, 0.046f, 550.0f, 0.042f,
                560.0f, 0.039f, 570.0f, 0.038f, 580.0f, 0.038f, 590.0f, 0.038f, 600.0f, 0.038f, 610.0f, 0.039f,
                620.0f, 0.039f, 630.0f, 0.040f, 640.0f, 0.041f, 650.0f, 0.042f, 660.0f, 0.044f, 670.0f, 0.045f,
                680.0f, 0.046f, 690.0f, 0.046f, 700.0f, 0.048f, 710.0f, 0.052f, 720.0f, 0.057f, 730.0f, 0.065f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.052f, 390.0f, 0.053f, 400.0f, 0.054f, 410.0f, 0.055f, 420.0f, 0.057f, 430.0f, 0.059f,
                440.0f, 0.061f, 450.0f, 0.066f, 460.0f, 0.075f, 470.0f, 0.093f, 480.0f, 0.125f, 490.0f, 0.178f,
                500.0f, 0.246f, 510.0f, 0.307f, 520.0f, 0.337f, 530.0f, 0.334f, 540.0f, 0.317f, 550.0f, 0.293f,
                560.0f, 0.262f, 570.0f, 0.230f, 580.0f, 0.198f, 590.0f, 0.165f, 600.0f, 0.135f, 610.0f, 0.115f,
                620.0f, 0.104f, 630.0f, 0.098f, 640.0f, 0.094f, 650.0f, 0.092f, 660.0f, 0.093f, 670.0f, 0.097f,
                680.0f, 0.102f, 690.0f, 0.108f, 700.0f, 0.113f, 710.0f, 0.115f, 720.0f, 0.114f, 730.0f, 0.114f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.050f, 390.0f, 0.049f, 400.0f, 0.048f, 410.0f, 0.047f, 420.0f, 0.047f, 430.0f, 0.047f,
                440.0f, 0.047f, 450.0f, 0.047f, 460.0f, 0.046f, 470.0f, 0.045f, 480.0f, 0.044f, 490.0f, 0.044f,
                500.0f, 0.045f, 510.0f, 0.046f, 520.0f, 0.047f, 530.0f, 0.048f, 540.0f, 0.049f, 550.0f, 0.050f,
                560.0f, 0.054f, 570.0f, 0.060f, 580.0f, 0.072f, 590.0f, 0.104f, 600.0f, 0.178f, 610.0f, 0.312f,
                620.0f, 0.467f, 630.0f, 0.581f, 640.0f, 0.644f, 650.0f, 0.675f, 660.0f, 0.690f, 670.0f, 0.698f,
                680.0f, 0.706f, 690.0f, 0.715f, 700.0f, 0.724f, 710.0f, 0.730f, 720.0f, 0.734f, 730.0f, 0.738f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.058f, 390.0f, 0.054f, 400.0f, 0.052f, 410.0f, 0.052f, 420.0f, 0.053f, 430.0f, 0.054f,
                440.0f, 0.056f, 450.0f, 0.059f, 460.0f, 0.067f, 470.0f, 0.081f, 480.0f, 0.107f, 490.0f, 0.152f,
                500.0f, 0.225f, 510.0f, 0.336f, 520.0f, 0.462f, 530.0f, 0.559f, 540.0f, 0.616f, 550.0f, 0.650f,
                560.0f, 0.672f, 570.0f, 0.694f, 580.0f, 0.710f, 590.0f, 0.723f, 600.0f, 0.731f, 610.0f, 0.739f,
                620.0f, 0.746f, 630.0f, 0.752f, 640.0f, 0.758f, 650.0f, 0.764f, 660.0f, 0.769f, 670.0f, 0.771f,
                680.0f, 0.776f, 690.0f, 0.782f, 700.0f, 0.790f, 710.0f, 0.796f, 720.0f, 0.799f, 730.0f, 0.804f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.145f, 390.0f, 0.195f, 400.0f, 0.283f, 410.0f, 0.346f, 420.0f, 0.362f, 430.0f, 0.354f,
                440.0f, 0.334f, 450.0f, 0.306f, 460.0f, 0.276f, 470.0f, 0.248f, 480.0f, 0.218f, 490.0f, 0.190f,
                500.0f, 0.168f, 510.0f, 0.149f, 520.0f, 0.127f, 530.0f, 0.107f, 540.0f, 0.100f, 550.0f, 0.102f,
                560.0f, 0.104f, 570.0f, 0.109f, 580.0f, 0.137f, 590.0f, 0.200f, 600.0f, 0.290f, 610.0f, 0.400f,
                620.0f, 0.516f, 630.0f, 0.615f, 640.0f, 0.687f, 650.0f, 0.732f, 660.0f, 0.760f, 670.0f, 0.774f,
                680.0f, 0.783f, 690.0f, 0.793f, 700.0f, 0.803f, 710.0f, 0.812f, 720.0f, 0.817f, 730.0f, 0.825f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.108f, 390.0f, 0.141f, 400.0f, 0.192f, 410.0f, 0.236f, 420.0f, 0.261f, 430.0f, 0.286f,
                440.0f, 0.317f, 450.0f, 0.353f, 460.0f, 0.390f, 470.0f, 0.426f, 480.0f, 0.446f, 490.0f, 0.444f,
                500.0f, 0.423f, 510.0f, 0.385f, 520.0f, 0.337f, 530.0f, 0.283f, 540.0f, 0.231f, 550.0f, 0.185f,
                560.0f, 0.146f, 570.0f, 0.118f, 580.0f, 0.101f, 590.0f, 0.090f, 600.0f, 0.082f, 610.0f, 0.076f,
                620.0f, 0.074f, 630.0f, 0.073f, 640.0f, 0.073f, 650.0f, 0.074f, 660.0f, 0.076f, 670.0f, 0.077f,
                680.0f, 0.076f, 690.0f, 0.075f, 700.0f, 0.073f, 710.0f, 0.072f, 720.0f, 0.074f, 730.0f, 0.079f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.189f, 390.0f, 0.255f, 400.0f, 0.423f, 410.0f, 0.660f, 420.0f, 0.811f, 430.0f, 0.862f,
                440.0f, 0.877f, 450.0f, 0.884f, 460.0f, 0.891f, 470.0f, 0.896f, 480.0f, 0.899f, 490.0f, 0.904f,
                500.0f, 0.907f, 510.0f, 0.909f, 520.0f, 0.911f, 530.0f, 0.910f, 540.0f, 0.911f, 550.0f, 0.914f,
                560.0f, 0.913f, 570.0f, 0.916f, 580.0f, 0.915f, 590.0f, 0.916f, 600.0f, 0.914f, 610.0f, 0.915f,
                620.0f, 0.918f, 630.0f, 0.919f, 640.0f, 0.921f, 650.0f, 0.923f, 660.0f, 0.924f, 670.0f, 0.922f,
                680.0f, 0.922f, 690.0f, 0.925f, 700.0f, 0.927f, 710.0f, 0.930f, 720.0f, 0.930f, 730.0f, 0.933f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.171f, 390.0f, 0.232f, 400.0f, 0.365f, 410.0f, 0.507f, 420.0f, 0.567f, 430.0f, 0.583f,
                440.0f, 0.588f, 450.0f, 0.590f, 460.0f, 0.591f, 470.0f, 0.590f, 480.0f, 0.588f, 490.0f, 0.588f,
                500.0f, 0.589f, 510.0f, 0.589f, 520.0f, 0.591f, 530.0f, 0.590f, 540.0f, 0.590f, 550.0f, 0.590f,
                560.0f, 0.589f, 570.0f, 0.591f, 580.0f, 0.590f, 590.0f, 0.590f, 600.0f, 0.587f, 610.0f, 0.585f,
                620.0f, 0.583f, 630.0f, 0.580f, 640.0f, 0.578f, 650.0f, 0.576f, 660.0f, 0.574f, 670.0f, 0.572f,
                680.0f, 0.571f, 690.0f, 0.569f, 700.0f, 0.568f, 710.0f, 0.568f, 720.0f, 0.566f, 730.0f, 0.566f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.144f, 390.0f, 0.192f, 400.0f, 0.272f, 410.0f, 0.331f, 420.0f, 0.350f, 430.0f, 0.357f,
                440.0f, 0.361f, 450.0f, 0.363f, 460.0f, 0.363f, 470.0f, 0.361f, 480.0f, 0.359f, 490.0f, 0.358f,
                500.0f, 0.358f, 510.0f, 0.359f, 520.0f, 0.360f, 530.0f, 0.360f, 540.0f, 0.361f, 550.0f, 0.361f,
                560.0f, 0.360f, 570.0f, 0.362f, 580.0f, 0.362f, 590.0f, 0.361f, 600.0f, 0.359f, 610.0f, 0.358f,
                620.0f, 0.355f, 630.0f, 0.352f, 640.0f, 0.350f, 650.0f, 0.348f, 660.0f, 0.345f, 670.0f, 0.343f,
                680.0f, 0.340f, 690.0f, 0.338f, 700.0f, 0.335f, 710.0f, 0.334f, 720.0f, 0.332f, 730.0f, 0.331f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.105f, 390.0f, 0.131f, 400.0f, 0.163f, 410.0f, 0.180f, 420.0f, 0.186f, 430.0f, 0.190f,
                440.0f, 0.193f, 450.0f, 0.194f, 460.0f, 0.194f, 470.0f, 0.192f, 480.0f, 0.191f, 490.0f, 0.191f,
                500.0f, 0.191f, 510.0f, 0.192f, 520.0f, 0.192f, 530.0f, 0.192f, 540.0f, 0.192f, 550.0f, 0.192f,
                560.0f, 0.192f, 570.0f, 0.193f, 580.0f, 0.192f, 590.0f, 0.192f, 600.0f, 0.191f, 610.0f, 0.189f,
                620.0f, 0.188f, 630.0f, 0.186f, 640.0f, 0.184f, 650.0f, 0.182f, 660.0f, 0.181f, 670.0f, 0.179f,
                680.0f, 0.178f, 690.0f, 0.176f, 700.0f, 0.174f, 710.0f, 0.173f, 720.0f, 0.172f, 730.0f, 0.171f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.068f, 390.0f, 0.077f, 400.0f, 0.084f, 410.0f, 0.087f, 420.0f, 0.089f, 430.0f, 0.090f,
                440.0f, 0.092f, 450.0f, 0.092f, 460.0f, 0.091f, 470.0f, 0.090f, 480.0f, 0.090f, 490.0f, 0.090f,
                500.0f, 0.090f, 510.0f, 0.090f, 520.0f, 0.090f, 530.0f, 0.090f, 540.0f, 0.090f, 550.0f, 0.090f,
                560.0f, 0.090f, 570.0f, 0.090f, 580.0f, 0.090f, 590.0f, 0.089f, 600.0f, 0.089f, 610.0f, 0.088f,
                620.0f, 0.087f, 630.0f, 0.086f, 640.0f, 0.086f, 650.0f, 0.085f, 660.0f, 0.084f, 670.0f, 0.084f,
                680.0f, 0.083f, 690.0f, 0.083f, 700.0f, 0.082f, 710.0f, 0.081f, 720.0f, 0.081f, 730.0f, 0.081f},
                false, null),
            PiecewiseLinearSpectrum.FromInterleaved(new Float[] {
                380.0f, 0.031f, 390.0f, 0.032f, 400.0f, 0.032f, 410.0f, 0.033f, 420.0f, 0.033f, 430.0f, 0.033f,
                440.0f, 0.033f, 450.0f, 0.033f, 460.0f, 0.032f, 470.0f, 0.032f, 480.0f, 0.032f, 490.0f, 0.032f,
                500.0f, 0.032f, 510.0f, 0.032f, 520.0f, 0.032f, 530.0f, 0.032f, 540.0f, 0.032f, 550.0f, 0.032f,
                560.0f, 0.032f, 570.0f, 0.032f, 580.0f, 0.032f, 590.0f, 0.032f, 600.0f, 0.032f, 610.0f, 0.032f,
                620.0f, 0.032f, 630.0f, 0.032f, 640.0f, 0.032f, 650.0f, 0.032f, 660.0f, 0.032f, 670.0f, 0.032f,
                680.0f, 0.032f, 690.0f, 0.032f, 700.0f, 0.032f, 710.0f, 0.032f, 720.0f, 0.032f, 730.0f, 0.033f},
                false, null)
        };
    }

    // VisibleSurface Definition
    public sealed unsafe class VisibleSurface
    {
        // VisibleSurface Public Methods
        public VisibleSurface(SurfaceInteraction si, SampledSpectrum albedo, SampledWavelengths lambda)
        {
            this.albedo = albedo;
            set = true;
            // Initialize geometric _VisibleSurface_ members
            p = si.p();
            Vector3f wo = si.wo;
            n = Globals.FaceForward(si.n, wo);
            ns = Globals.FaceForward(si.shading.n, wo);
            uv = si.uv;
            time = si.time;
            dpdx = si.dpdx;
            dpdy = si.dpdy;
        }

        public static implicit operator bool(VisibleSurface v) { return v != null && v.set; }

        public VisibleSurface() { }

        public override string ToString()
        {
            return Globals.StringPrintf("[ VisibleSurface set: %s p: %s n: %s ns: %s dpdx: %f dpdy: %f " +
                                        "time: %f albedo: %s ]",
                                        set, p, n, ns, dpdx, dpdy, time, albedo);
        }

        // (C++ code assigns through a VisibleSurface pointer: *visibleSurf = VisibleSurface(...))
        public void Assign(VisibleSurface o)
        {
            p = o.p; n = o.n; ns = o.ns; uv = o.uv; time = o.time;
            dpdx = o.dpdx; dpdy = o.dpdy; albedo = o.albedo; set = o.set;
        }

        // VisibleSurface Public Members
        public Point3f p;
        public Normal3f n, ns;
        public Point2f uv;
        public Float time = 0;
        public Vector3f dpdx, dpdy;
        public SampledSpectrum albedo;
        public bool set = false;
    }

    // FilmBaseParameters Definition
    public unsafe struct FilmBaseParameters
    {
        public FilmBaseParameters(Point2i fullResolution, Bounds2i pixelBounds, Filter filter, Float diagonal,
                                  PixelSensor sensor, string filename)
        {
            this.fullResolution = fullResolution;
            this.pixelBounds = pixelBounds;
            this.filter = filter;
            this.diagonal = diagonal;
            this.sensor = sensor;
            this.filename = filename;
        }

        // FilmBaseParameters Method Definitions
        public FilmBaseParameters(ParameterDictionary parameters, Filter filter, PixelSensor sensor, FileLoc loc)
        {
            this.filter = filter;
            this.sensor = sensor;
            filename = parameters.GetOneString("filename", "");
            if (Globals.Options.imageFile.Length != 0)
            {
                if (filename.Length != 0)
                    Globals.Warning(loc, Globals.StringPrintf(
                                             "Output filename supplied on command line, \"%s\" will " +
                                             "override " +
                                             "filename provided in scene description file, \"%s\".",
                                             Globals.Options.imageFile, filename));
                filename = Globals.Options.imageFile;
            }
            else if (filename.Length == 0)
                filename = "pbrt.exr";

            if (Globals.Options.fullscreen)
            {
                // GUI::GetResolution() -- the GUI is not ported.
                Globals.ErrorExit("Fullscreen mode requires the GUI, which is not ported.");
                fullResolution = default;
            }
            else
            {
                fullResolution = new Point2i(parameters.GetOneInt("xresolution", 1280),
                                             parameters.GetOneInt("yresolution", 720));
            }
            if (Globals.Options.quickRender)
            {
                fullResolution.x = std.max(1, fullResolution.x / 4);
                fullResolution.y = std.max(1, fullResolution.y / 4);
            }

            pixelBounds = new Bounds2i(new Point2i(0, 0), fullResolution);
            List<int> pb = parameters.GetIntArray("pixelbounds");
            if (Globals.Options.pixelBounds)
            {
                Bounds2i newBounds = Globals.Options.pixelBounds.value();
                if (Globals.Intersect(newBounds, pixelBounds) != newBounds)
                    Globals.Warning(loc, "Supplied pixel bounds extend beyond image " +
                                         "resolution. Clamping.");
                pixelBounds = Globals.Intersect(newBounds, pixelBounds);

                if (pb.Count != 0)
                    Globals.Warning(loc, "Both pixel bounds and crop window were specified. Using the " +
                                         "crop window.");
            }
            else if (pb.Count != 0)
            {
                if (pb.Count != 4)
                    Globals.Error(loc, Globals.StringPrintf("%d values supplied for \"pixelbounds\". Expected 4.",
                                                            (int)pb.Count));
                else
                {
                    Bounds2i newBounds = new Bounds2i(new Point2i(pb[0], pb[2]), new Point2i(pb[1], pb[3]));
                    if (Globals.Intersect(newBounds, pixelBounds) != newBounds)
                        Globals.Warning(loc, "Supplied pixel bounds extend beyond image " +
                                             "resolution. Clamping.");
                    pixelBounds = Globals.Intersect(newBounds, pixelBounds);
                }
            }

            List<Float> cr = parameters.GetFloatArray("cropwindow");
            if (Globals.Options.cropWindow)
            {
                Bounds2f crop = Globals.Options.cropWindow.value();
                if (Globals.Intersect(crop, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1))) != crop)
                {
                    Globals.Error(loc, Globals.StringPrintf(
                                           "Film crop window %s is not in [0,1] range; did you " +
                                           "mean to use \"pixelbounds\"? Clamping to valid range.", crop));
                    crop = Globals.Intersect(crop, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));
                }

                // Compute film image bounds
                pixelBounds = new Bounds2i(new Point2i((int)pstd.ceil(fullResolution.x * crop.pMin.x),
                                                       (int)pstd.ceil(fullResolution.y * crop.pMin.y)),
                                           new Point2i((int)pstd.ceil(fullResolution.x * crop.pMax.x),
                                                       (int)pstd.ceil(fullResolution.y * crop.pMax.y)));

                if (cr.Count != 0)
                    Globals.Warning(loc, "Crop window supplied on command line will override " +
                                         "crop window specified with Film.");
                if (Globals.Options.pixelBounds || pb.Count != 0)
                    Globals.Warning(loc, "Both pixel bounds and crop window were specified. Using the " +
                                         "crop window.");
            }
            else if (cr.Count != 0)
            {
                if (Globals.Options.pixelBounds)
                    Globals.Warning(loc, "Ignoring \"cropwindow\" since pixel bounds were specified " +
                                         "on the command line.");
                else if (cr.Count == 4)
                {
                    if (pb.Count != 0)
                        Globals.Warning(loc, "Both pixel bounds and crop window were " +
                                             "specified. Using the " +
                                             "crop window.");

                    Bounds2f crop = new Bounds2f();
                    crop.pMin.x = Globals.Clamp(std.min(cr[0], cr[1]), 0.0f, 1.0f);
                    crop.pMax.x = Globals.Clamp(std.max(cr[0], cr[1]), 0.0f, 1.0f);
                    crop.pMin.y = Globals.Clamp(std.min(cr[2], cr[3]), 0.0f, 1.0f);
                    crop.pMax.y = Globals.Clamp(std.max(cr[2], cr[3]), 0.0f, 1.0f);

                    // Compute film image bounds
                    pixelBounds = new Bounds2i(new Point2i((int)pstd.ceil(fullResolution.x * crop.pMin.x),
                                                           (int)pstd.ceil(fullResolution.y * crop.pMin.y)),
                                               new Point2i((int)pstd.ceil(fullResolution.x * crop.pMax.x),
                                                           (int)pstd.ceil(fullResolution.y * crop.pMax.y)));
                }
                else
                    Globals.Error(loc, Globals.StringPrintf("%d values supplied for \"cropwindow\". Expected 4.",
                                                            (int)cr.Count));
            }

            if (pixelBounds.IsEmpty())
                Globals.ErrorExit(loc, Globals.StringPrintf("Degenerate pixel bounds provided to film: %s.", pixelBounds));

            diagonal = parameters.GetOneFloat("diagonal", 35.0f);
        }

        public Point2i fullResolution;
        public Bounds2i pixelBounds;
        public Filter filter;
        public Float diagonal;
        public PixelSensor sensor;
        public string filename;
    }

    // FilmBase Definition
    public abstract unsafe class FilmBase : Film
    {
        // STAT_MEMORY_COUNTER("Memory/Film pixels", filmPixelMemory);
        [ThreadStatic] internal static long filmPixelMemory;
        static StatRegisterer STATS_REG_FILMPIXELS = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/Film pixels", filmPixelMemory);
            filmPixelMemory = 0;
        });

        // FilmBase Public Methods
        protected FilmBase(FilmBaseParameters p)
        {
            fullResolution = p.fullResolution;
            pixelBounds = p.pixelBounds;
            filter = p.filter;
            diagonal = p.diagonal * .001f;
            sensor = p.sensor;
            filename = p.filename;
            Globals.CHECK(!pixelBounds.IsEmpty());
            Globals.CHECK_GE(pixelBounds.pMin.x, 0);
            Globals.CHECK_LE(pixelBounds.pMax.x, fullResolution.x);
            Globals.CHECK_GE(pixelBounds.pMin.y, 0);
            Globals.CHECK_LE(pixelBounds.pMax.y, fullResolution.y);
            Globals.LOG_VERBOSE("Created film with full resolution %s, pixelBounds %s", fullResolution, pixelBounds);
        }

        public override Point2i FullResolution() { return fullResolution; }
        public override Bounds2i PixelBounds() { return pixelBounds; }
        public override Float Diagonal() { return diagonal; }
        public override Filter GetFilter() { return filter; }
        public override PixelSensor GetPixelSensor() { return sensor; }
        public override string GetFilename() { return filename; }

        public override SampledWavelengths SampleWavelengths(Float u)
        {
            return SampledWavelengths.SampleVisible(u);
        }

        // FilmBase Method Definitions
        public override Bounds2f SampleBounds()
        {
            Vector2f radius = filter.Radius();
            return new Bounds2f(new Point2f(pixelBounds.pMin.x, pixelBounds.pMin.y) - radius + new Vector2f(0.5f, 0.5f),
                                new Point2f(pixelBounds.pMax.x, pixelBounds.pMax.y) + radius - new Vector2f(0.5f, 0.5f));
        }

        protected string BaseToString()
        {
            return Globals.StringPrintf("fullResolution: %s diagonal: %f filter: %s filename: %s " +
                                        "pixelBounds: %s",
                                        fullResolution, diagonal, filter, filename, pixelBounds);
        }

        // FilmBase Protected Members
        protected Point2i fullResolution;
        protected Bounds2i pixelBounds;
        protected Filter filter;
        protected Float diagonal;
        protected PixelSensor sensor;
        protected string filename;
    }

    // RGBFilm Definition
    public sealed unsafe class RGBFilm : FilmBase
    {
        // RGBFilm::Pixel Definition
        struct Pixel
        {
            public fixed double rgbSum[3];  // = {0., 0., 0.}
            public double weightSum;        // = 0.
            public AtomicDouble rgbSplat0, rgbSplat1, rgbSplat2;

            public ref AtomicDouble rgbSplat(int c)
            {
                if (c == 0) return ref rgbSplat0;
                if (c == 1) return ref rgbSplat1;
                return ref rgbSplat2;
            }
        }

        // RGBFilm Public Methods
        public override bool UsesVisibleSurface() { return false; }

        public override void AddSample(Point2i pFilm, SampledSpectrum L, SampledWavelengths lambda,
                                       VisibleSurface visibleSurface, Float weight)
        {
            // Convert sample radiance to _PixelSensor_ RGB
            RGB rgb = sensor.ToSensorRGB(L, lambda);

            // Optionally clamp sensor RGB value
            Float m = std.max(rgb.r, std.max(rgb.g, rgb.b));
            if (m > maxComponentValue)
                rgb *= maxComponentValue / m;

            Globals.DCHECK(Globals.InsideExclusive(pFilm, pixelBounds));
            // Update pixel values with filtered sample contribution
            ref Pixel pixel = ref pixels[pFilm];
            for (int c = 0; c < 3; ++c)
                pixel.rgbSum[c] += weight * rgb[c];
            pixel.weightSum += weight;
        }

        public override RGB GetPixelRGB(Point2i p, Float splatScale = 1)
        {
            ref Pixel pixel = ref pixels[p];
            RGB rgb = new RGB((Float)pixel.rgbSum[0], (Float)pixel.rgbSum[1], (Float)pixel.rgbSum[2]);
            // Normalize _rgb_ with weight sum
            Float weightSum = (Float)pixel.weightSum;
            if (weightSum != 0)
                rgb /= weightSum;

            // Add splat value at pixel
            for (int c = 0; c < 3; ++c)
                rgb[c] += splatScale * (Float)(double)pixel.rgbSplat(c) / filterIntegral;

            // Convert _rgb_ to output RGB color space
            rgb = outputRGBFromSensorRGB * rgb;

            return rgb;
        }

        // RGBFilm Method Definitions
        public RGBFilm(FilmBaseParameters p, RGBColorSpace colorSpace, Float maxComponentValue = Infinity,
                       bool writeFP16 = true, Allocator alloc = null)
            : base(p)
        {
            pixels = new Array2D<Pixel>(p.pixelBounds, alloc);
            this.colorSpace = colorSpace;
            this.maxComponentValue = maxComponentValue;
            this.writeFP16 = writeFP16;
            filterIntegral = filter.Integral();
            Globals.CHECK(!pixelBounds.IsEmpty());
            Globals.CHECK(colorSpace != null);
            filmPixelMemory += pixelBounds.Area() * sizeof(Pixel);
            // Compute _outputRGBFromSensorRGB_ matrix
            outputRGBFromSensorRGB = colorSpace.RGBFromXYZ * sensor.XYZFromSensorRGB;
        }

        public override void AddSplat(Point2f p, SampledSpectrum L, SampledWavelengths lambda)
        {
            Globals.CHECK(!L.HasNaNs());
            // Convert sample radiance to _PixelSensor_ RGB
            RGB rgb = sensor.ToSensorRGB(L, lambda);

            // Optionally clamp sensor RGB value
            Float m = std.max(rgb.r, std.max(rgb.g, rgb.b));
            if (m > maxComponentValue)
                rgb *= maxComponentValue / m;

            // Compute bounds of affected pixels for splat, _splatBounds_
            Point2f pDiscrete = p + new Vector2f(0.5f, 0.5f);
            Vector2f radius = filter.Radius();
            Bounds2i splatBounds = new Bounds2i((Point2i)Globals.Floor(pDiscrete - radius),
                                                (Point2i)Globals.Floor(pDiscrete + radius) + new Vector2i(1, 1));
            splatBounds = Globals.Intersect(splatBounds, pixelBounds);

            foreach (Point2i pi in splatBounds)
            {
                // Evaluate filter at _pi_ and add splat contribution
                Float wt = filter.Evaluate((Point2f)(p - (Point2f)pi - new Vector2f(0.5f, 0.5f)));
                if (wt != 0)
                {
                    ref Pixel pixel = ref pixels[pi];
                    for (int i = 0; i < 3; ++i)
                        pixel.rgbSplat(i).Add(wt * rgb[i]);
                }
            }
        }

        public override void WriteImage(ImageMetadata metadata, Float splatScale = 1)
        {
            Image image = GetImage(metadata, splatScale);
            Globals.LOG_VERBOSE("Writing image %s with bounds %s", filename, pixelBounds);
            image.Write(filename, metadata);
        }

        public override Image GetImage(ImageMetadata metadata, Float splatScale = 1)
        {
            // Convert image to RGB and compute final pixel values
            Globals.LOG_VERBOSE("Converting image to RGB and computing final weighted pixel values");
            PixelFormat format = writeFP16 ? PixelFormat.Half : PixelFormat.Float;
            Vector2i diag = pixelBounds.Diagonal();
            Image image = new Image(format, new Point2i(diag.x, diag.y), new string[] { "R", "G", "B" });

            int nClamped = 0;
            Globals.ParallelFor2D(pixelBounds, (Point2i p) =>
            {
                RGB rgb = GetPixelRGB(p, splatScale);

                if (writeFP16 && std.max(rgb.r, std.max(rgb.g, rgb.b)) > 65504)
                {
                    if (rgb.r > 65504)
                        rgb.r = 65504;
                    if (rgb.g > 65504)
                        rgb.g = 65504;
                    if (rgb.b > 65504)
                        rgb.b = 65504;
                    Interlocked.Increment(ref nClamped);
                }

                Point2i pOffset = new Point2i(p.x - pixelBounds.pMin.x, p.y - pixelBounds.pMin.y);
                image.SetChannels(pOffset, new Float[] { rgb[0], rgb[1], rgb[2] });
            });

            if (Volatile.Read(ref nClamped) > 0)
                Globals.Warning(Globals.StringPrintf("%d pixel values clamped to maximum fp16 value.", Volatile.Read(ref nClamped)));

            metadata.pixelBounds = pixelBounds;
            metadata.fullResolution = fullResolution;
            metadata.colorSpace = colorSpace;

            return image;
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ RGBFilm %s colorSpace: %s maxComponentValue: %f writeFP16: %s ]",
                                        BaseToString(), colorSpace, maxComponentValue, writeFP16);
        }

        public static RGBFilm Create(ParameterDictionary parameters, Float exposureTime, Filter filter,
                                     RGBColorSpace colorSpace, FileLoc loc, Allocator alloc)
        {
            Float maxComponentValue = parameters.GetOneFloat("maxcomponentvalue", Infinity);
            bool writeFP16 = parameters.GetOneBool("savefp16", true);

            PixelSensor sensor = PixelSensor.Create(parameters, colorSpace, exposureTime, loc, alloc);
            FilmBaseParameters filmBaseParameters = new FilmBaseParameters(parameters, filter, sensor, loc);

            return new RGBFilm(filmBaseParameters, colorSpace, maxComponentValue, writeFP16, alloc);
        }

        public override RGB ToOutputRGB(SampledSpectrum L, SampledWavelengths lambda)
        {
            RGB sensorRGB = sensor.ToSensorRGB(L, lambda);
            return outputRGBFromSensorRGB * sensorRGB;
        }

        public override void ResetPixel(Point2i p)
        {
            pixels[p] = default;
        }

        // RGBFilm Private Members
        RGBColorSpace colorSpace;
        Float maxComponentValue;
        bool writeFP16;
        Float filterIntegral;
        SquareMatrix3 outputRGBFromSensorRGB;
        Array2D<Pixel> pixels;
    }

    // GBufferFilm Definition
    public sealed unsafe class GBufferFilm : FilmBase
    {
        // GBufferFilm::Pixel Definition
        struct Pixel
        {
            public fixed double rgbSum[3];  // = {0., 0., 0.}
            public double weightSum, gBufferWeightSum;  // = 0.
            public AtomicDouble rgbSplat0, rgbSplat1, rgbSplat2;
            public Point3f pSum;
            public Float dzdxSum, dzdySum;  // = 0
            public Normal3f nSum, nsSum;
            public Point2f uvSum;
            public fixed double rgbAlbedoSum[3];  // = {0., 0., 0.}
            public VarianceEstimator<Float> rgbVariance0, rgbVariance1, rgbVariance2;

            public ref AtomicDouble rgbSplat(int c)
            {
                if (c == 0) return ref rgbSplat0;
                if (c == 1) return ref rgbSplat1;
                return ref rgbSplat2;
            }

            public ref VarianceEstimator<Float> rgbVariance(int c)
            {
                if (c == 0) return ref rgbVariance0;
                if (c == 1) return ref rgbVariance1;
                return ref rgbVariance2;
            }
        }

        // GBufferFilm Method Definitions
        public override void AddSample(Point2i pFilm, SampledSpectrum L, SampledWavelengths lambda,
                                       VisibleSurface visibleSurface, Float weight)
        {
            RGB rgb = sensor.ToSensorRGB(L, lambda);
            Float m = std.max(rgb.r, std.max(rgb.g, rgb.b));
            if (m > maxComponentValue)
                rgb *= maxComponentValue / m;

            ref Pixel p = ref pixels[pFilm];
            if (visibleSurface != null && visibleSurface)
            {
                p.gBufferWeightSum += weight;

                // Update variance estimates.
                for (int c = 0; c < 3; ++c)
                    p.rgbVariance(c).Add(rgb[c]);

                if (applyInverse)
                {
                    p.pSum += weight * outputFromRender.ApplyInverse(visibleSurface.p, visibleSurface.time);
                    p.nSum += weight * outputFromRender.ApplyInverse(visibleSurface.n, visibleSurface.time);
                    p.nsSum += weight * outputFromRender.ApplyInverse(visibleSurface.ns, visibleSurface.time);
                    p.dzdxSum += weight * outputFromRender.ApplyInverse(visibleSurface.dpdx, visibleSurface.time).z;
                    p.dzdySum += weight * outputFromRender.ApplyInverse(visibleSurface.dpdy, visibleSurface.time).z;
                }
                else
                {
                    p.pSum += weight * outputFromRender.Apply(visibleSurface.p, visibleSurface.time);
                    p.nSum += weight * outputFromRender.Apply(visibleSurface.n, visibleSurface.time);
                    p.nsSum += weight * outputFromRender.Apply(visibleSurface.ns, visibleSurface.time);
                    p.dzdxSum += weight * outputFromRender.Apply(visibleSurface.dpdx, visibleSurface.time).z;
                    p.dzdySum += weight * outputFromRender.Apply(visibleSurface.dpdy, visibleSurface.time).z;
                }
                p.uvSum += weight * visibleSurface.uv;

                SampledSpectrum albedo = visibleSurface.albedo * colorSpace.illuminant.Sample(lambda);
                RGB albedoRGB = albedo.ToRGB(lambda, colorSpace);
                for (int c = 0; c < 3; ++c)
                    p.rgbAlbedoSum[c] += weight * albedoRGB[c];
            }

            for (int c = 0; c < 3; ++c)
                p.rgbSum[c] += rgb[c] * weight;
            p.weightSum += weight;
        }

        public GBufferFilm(FilmBaseParameters p, AnimatedTransform outputFromRender, bool applyInverse,
                           RGBColorSpace colorSpace, Float maxComponentValue = Infinity, bool writeFP16 = true,
                           Allocator alloc = null)
            : base(p)
        {
            this.outputFromRender = outputFromRender;
            this.applyInverse = applyInverse;
            pixels = new Array2D<Pixel>(pixelBounds, alloc);
            this.colorSpace = colorSpace;
            this.maxComponentValue = maxComponentValue;
            this.writeFP16 = writeFP16;
            filterIntegral = filter.Integral();
            Globals.CHECK(!pixelBounds.IsEmpty());
            filmPixelMemory += pixelBounds.Area() * sizeof(Pixel);
            outputRGBFromSensorRGB = colorSpace.RGBFromXYZ * sensor.XYZFromSensorRGB;
        }

        public override void AddSplat(Point2f p, SampledSpectrum v, SampledWavelengths lambda)
        {
            // NOTE: same code as RGBFilm::AddSplat()...
            Globals.CHECK(!v.HasNaNs());
            RGB rgb = sensor.ToSensorRGB(v, lambda);
            Float m = std.max(rgb.r, std.max(rgb.g, rgb.b));
            if (m > maxComponentValue)
                rgb *= maxComponentValue / m;

            Point2f pDiscrete = p + new Vector2f(0.5f, 0.5f);
            Bounds2i splatBounds = new Bounds2i((Point2i)Globals.Floor(pDiscrete - filter.Radius()),
                                                (Point2i)Globals.Floor(pDiscrete + filter.Radius()) + new Vector2i(1, 1));
            splatBounds = Globals.Intersect(splatBounds, pixelBounds);
            foreach (Point2i pi in splatBounds)
            {
                Float wt = filter.Evaluate((Point2f)(p - (Point2f)pi - new Vector2f(0.5f, 0.5f)));
                if (wt != 0)
                {
                    ref Pixel pixel = ref pixels[pi];
                    for (int i = 0; i < 3; ++i)
                        pixel.rgbSplat(i).Add(wt * rgb[i]);
                }
            }
        }

        public override RGB ToOutputRGB(SampledSpectrum L, SampledWavelengths lambda)
        {
            RGB cameraRGB = sensor.ToSensorRGB(L, lambda);
            return outputRGBFromSensorRGB * cameraRGB;
        }

        public override bool UsesVisibleSurface() { return true; }

        public override RGB GetPixelRGB(Point2i p, Float splatScale = 1)
        {
            ref Pixel pixel = ref pixels[p];
            RGB rgb = new RGB((Float)pixel.rgbSum[0], (Float)pixel.rgbSum[1], (Float)pixel.rgbSum[2]);

            // Normalize pixel with weight sum
            Float weightSum = (Float)pixel.weightSum;
            if (weightSum != 0)
                rgb /= weightSum;

            // Add splat value at pixel
            for (int c = 0; c < 3; ++c)
                rgb[c] += splatScale * (Float)(double)pixel.rgbSplat(c) / filterIntegral;

            rgb = outputRGBFromSensorRGB * rgb;

            return rgb;
        }

        public override void WriteImage(ImageMetadata metadata, Float splatScale = 1)
        {
            Image image = GetImage(metadata, splatScale);
            Globals.LOG_VERBOSE("Writing image %s with bounds %s", filename, pixelBounds);
            image.Write(filename, metadata);
        }

        public override Image GetImage(ImageMetadata metadata, Float splatScale = 1)
        {
            // Convert image to RGB and compute final pixel values
            Globals.LOG_VERBOSE("Converting image to RGB and computing final weighted pixel values");
            PixelFormat format = writeFP16 ? PixelFormat.Half : PixelFormat.Float;
            Vector2i diag = pixelBounds.Diagonal();
            Image image = new Image(format, new Point2i(diag.x, diag.y),
                                    new string[] { "R", "G", "B", "Albedo.R", "Albedo.G", "Albedo.B", "P.X", "P.Y", "P.Z",
                                                   "dzdx", "dzdy", "N.X", "N.Y", "N.Z", "Ns.X", "Ns.Y", "Ns.Z", "u", "v",
                                                   "Variance.R", "Variance.G", "Variance.B", "RelativeVariance.R",
                                                   "RelativeVariance.G", "RelativeVariance.B" });

            ImageChannelDesc rgbDesc = image.GetChannelDesc(new string[] { "R", "G", "B" });
            ImageChannelDesc pDesc = image.GetChannelDesc(new string[] { "P.X", "P.Y", "P.Z" });
            ImageChannelDesc dzDesc = image.GetChannelDesc(new string[] { "dzdx", "dzdy" });
            ImageChannelDesc nDesc = image.GetChannelDesc(new string[] { "N.X", "N.Y", "N.Z" });
            ImageChannelDesc nsDesc = image.GetChannelDesc(new string[] { "Ns.X", "Ns.Y", "Ns.Z" });
            ImageChannelDesc uvDesc = image.GetChannelDesc(new string[] { "u", "v" });
            ImageChannelDesc albedoRgbDesc = image.GetChannelDesc(new string[] { "Albedo.R", "Albedo.G", "Albedo.B" });
            ImageChannelDesc varianceDesc = image.GetChannelDesc(new string[] { "Variance.R", "Variance.G", "Variance.B" });
            ImageChannelDesc relVarianceDesc = image.GetChannelDesc(
                new string[] { "RelativeVariance.R", "RelativeVariance.G", "RelativeVariance.B" });

            int nClamped = 0;
            Globals.ParallelFor2D(pixelBounds, (Point2i p) =>
            {
                ref Pixel pixel = ref pixels[p];
                RGB rgb = new RGB((Float)pixel.rgbSum[0], (Float)pixel.rgbSum[1], (Float)pixel.rgbSum[2]);
                RGB albedoRgb = new RGB((Float)pixel.rgbAlbedoSum[0], (Float)pixel.rgbAlbedoSum[1],
                                        (Float)pixel.rgbAlbedoSum[2]);

                // Normalize pixel with weight sum
                Float weightSum = (Float)pixel.weightSum, gBufferWeightSum = (Float)pixel.gBufferWeightSum;
                Point3f pt = pixel.pSum;
                Point2f uv = pixel.uvSum;
                Float dzdx = pixel.dzdxSum, dzdy = pixel.dzdySum;
                if (weightSum != 0)
                {
                    rgb /= weightSum;
                    albedoRgb /= weightSum;
                }
                if (gBufferWeightSum != 0)
                {
                    pt /= gBufferWeightSum;
                    uv /= gBufferWeightSum;
                    dzdx /= gBufferWeightSum;
                    dzdy /= gBufferWeightSum;
                }

                // Add splat value at pixel
                for (int c = 0; c < 3; ++c)
                    rgb[c] += splatScale * (Float)(double)pixel.rgbSplat(c) / filterIntegral;

                rgb = outputRGBFromSensorRGB * rgb;

                if (writeFP16 && std.max(rgb.r, std.max(rgb.g, rgb.b)) > 65504)
                {
                    if (rgb.r > 65504)
                        rgb.r = 65504;
                    if (rgb.g > 65504)
                        rgb.g = 65504;
                    if (rgb.b > 65504)
                        rgb.b = 65504;
                    Interlocked.Increment(ref nClamped);
                }

                Point2i pOffset = new Point2i(p.x - pixelBounds.pMin.x, p.y - pixelBounds.pMin.y);
                image.SetChannels(pOffset, rgbDesc, new Float[] { rgb[0], rgb[1], rgb[2] });
                image.SetChannels(pOffset, albedoRgbDesc, new Float[] { albedoRgb[0], albedoRgb[1], albedoRgb[2] });

                Normal3f n = Globals.LengthSquared(pixel.nSum) > 0 ? Globals.Normalize(pixel.nSum) : new Normal3f(0, 0, 0);
                Normal3f ns = Globals.LengthSquared(pixel.nsSum) > 0 ? Globals.Normalize(pixel.nsSum) : new Normal3f(0, 0, 0);
                image.SetChannels(pOffset, pDesc, new Float[] { pt.x, pt.y, pt.z });
                image.SetChannels(pOffset, dzDesc, new Float[] { std.abs(dzdx), std.abs(dzdy) });
                image.SetChannels(pOffset, nDesc, new Float[] { n.x, n.y, n.z });
                image.SetChannels(pOffset, nsDesc, new Float[] { ns.x, ns.y, ns.z });
                image.SetChannels(pOffset, uvDesc, new Float[] { uv[0], uv[1] });
                image.SetChannels(pOffset, varianceDesc,
                                  new Float[] { pixel.rgbVariance(0).Variance(), pixel.rgbVariance(1).Variance(),
                                                pixel.rgbVariance(2).Variance() });
                image.SetChannels(pOffset, relVarianceDesc,
                                  new Float[] { pixel.rgbVariance(0).RelativeVariance(),
                                                pixel.rgbVariance(1).RelativeVariance(),
                                                pixel.rgbVariance(2).RelativeVariance() });
            });

            if (Volatile.Read(ref nClamped) > 0)
                Globals.Warning(Globals.StringPrintf("%d pixel values clamped to maximum fp16 value.", Volatile.Read(ref nClamped)));

            metadata.pixelBounds = pixelBounds;
            metadata.fullResolution = fullResolution;
            metadata.colorSpace = colorSpace;

            return image;
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ GBufferFilm %s outputFromRender: %s applyInverse: %s " +
                                        "colorSpace: %s maxComponentValue: %f writeFP16: %s ]",
                                        BaseToString(), outputFromRender, applyInverse, colorSpace, maxComponentValue,
                                        writeFP16);
        }

        public static GBufferFilm Create(ParameterDictionary parameters, Float exposureTime,
                                         CameraTransform cameraTransform, Filter filter, RGBColorSpace colorSpace,
                                         FileLoc loc, Allocator alloc)
        {
            Float maxComponentValue = parameters.GetOneFloat("maxcomponentvalue", Infinity);
            bool writeFP16 = parameters.GetOneBool("savefp16", true);

            PixelSensor sensor = PixelSensor.Create(parameters, colorSpace, exposureTime, loc, alloc);

            FilmBaseParameters filmBaseParameters = new FilmBaseParameters(parameters, filter, sensor, loc);

            if (!Globals.HasExtension(filmBaseParameters.filename, "exr"))
                Globals.ErrorExit(loc, Globals.StringPrintf("%s: EXR is the only format supported by the GBufferFilm.",
                                                            filmBaseParameters.filename));

            string coordinateSystem = parameters.GetOneString("coordinatesystem", "camera");
            AnimatedTransform outputFromRender = new AnimatedTransform();
            bool applyInverse = false;
            if (coordinateSystem == "camera")
            {
                outputFromRender = cameraTransform.RenderFromCamera();
                applyInverse = true;
            }
            else if (coordinateSystem == "world")
                outputFromRender = new AnimatedTransform(cameraTransform.WorldFromRender());
            else
                Globals.ErrorExit(loc, Globals.StringPrintf("%s: unknown coordinate system for GBufferFilm. (Expecting \"camera\" " +
                                                            "or \"world\".)", coordinateSystem));

            return new GBufferFilm(filmBaseParameters, outputFromRender, applyInverse, colorSpace, maxComponentValue,
                                   writeFP16, alloc);
        }

        public override void ResetPixel(Point2i p)
        {
            pixels[p] = default;
        }

        // GBufferFilm Private Members
        AnimatedTransform outputFromRender;
        bool applyInverse;
        Array2D<Pixel> pixels;
        RGBColorSpace colorSpace;
        Float maxComponentValue;
        bool writeFP16;
        Float filterIntegral;
        SquareMatrix3 outputRGBFromSensorRGB;
    }

    // SpectralFilm Definition
    public sealed unsafe class SpectralFilm : FilmBase
    {
        // SpectralFilm::Pixel Definition
        struct Pixel
        {
            // Continue to store RGB, both to include in the final image as
            // well as for previews during rendering.
            public fixed double rgbSum[3];  // = {0., 0., 0.}
            public double rgbWeightSum;     // = 0.
            public AtomicDouble rgbSplat0, rgbSplat1, rgbSplat2;
            // The following will all have nBuckets entries.
            public double* bucketSums, weightSums;
            public AtomicDouble* bucketSplats;

            public ref AtomicDouble rgbSplat(int c)
            {
                if (c == 0) return ref rgbSplat0;
                if (c == 1) return ref rgbSplat1;
                return ref rgbSplat2;
            }
        }

        // SpectralFilm Public Methods
        public override bool UsesVisibleSurface() { return false; }

        public override SampledWavelengths SampleWavelengths(Float u)
        {
            return SampledWavelengths.SampleUniform(u, lambdaMin, lambdaMax);
        }

        public override void AddSample(Point2i pFilm, SampledSpectrum L, SampledWavelengths lambda,
                                       VisibleSurface visibleSurface, Float weight)
        {
            // Start by doing more or less what RGBFilm::AddSample() does so
            // that we can maintain accurate RGB values.

            // Convert sample radiance to _PixelSensor_ RGB
            RGB rgb = sensor.ToSensorRGB(L, lambda);

            // Optionally clamp sensor RGB value
            Float m = std.max(rgb.r, std.max(rgb.g, rgb.b));
            if (m > maxComponentValue)
                rgb *= maxComponentValue / m;

            Globals.DCHECK(Globals.InsideExclusive(pFilm, pixelBounds));
            // Update RGB fields in Pixel structure.
            ref Pixel pixel = ref pixels[pFilm];
            for (int c = 0; c < 3; ++c)
                pixel.rgbSum[c] += weight * rgb[c];
            pixel.rgbWeightSum += weight;

            // Spectral processing starts here.
            // Optionally clamp spectral value. (TODO: for spectral should we
            // just clamp channels individually?)
            Float lm = L.MaxComponentValue();
            if (lm > maxComponentValue)
                L *= maxComponentValue / lm;

            // The CIE_Y_integral factor effectively cancels out the effect of
            // the conversion of light sources to use photometric units for
            // specification.  We then do *not* divide by the PDF in |lambda|
            // but take advantage of the fact that we know that it is uniform
            // in SampleWavelengths(), the fact that the buckets all have the
            // same extend, and can then just average radiance in buckets
            // below.
            L *= weight * CIE_Y_integral;

            // Accumulate contributions in spectral buckets.
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                int b = LambdaToBucket(lambda[i]);
                pixel.bucketSums[b] += L[i];
                pixel.weightSums[b] += weight;
            }
        }

        // SpectralFilm Method Definitions
        public SpectralFilm(FilmBaseParameters p, Float lambdaMin, Float lambdaMax, int nBuckets,
                            RGBColorSpace colorSpace, Float maxComponentValue = Infinity, bool writeFP16 = true,
                            Allocator alloc = null)
            : base(p)
        {
            this.colorSpace = colorSpace;
            this.lambdaMin = lambdaMin;
            this.lambdaMax = lambdaMax;
            this.nBuckets = nBuckets;
            this.maxComponentValue = maxComponentValue;
            this.writeFP16 = writeFP16;
            pixels = new Array2D<Pixel>(p.pixelBounds, alloc);
            // Compute _outputRGBFromSensorRGB_ matrix
            outputRGBFromSensorRGB = colorSpace.RGBFromXYZ * sensor.XYZFromSensorRGB;

            filterIntegral = filter.Integral();
            Globals.CHECK(!pixelBounds.IsEmpty());
            filmPixelMemory += pixelBounds.Area() * (sizeof(Pixel) + 3 * nBuckets * sizeof(double));

            // Allocate memory for the pixel buffers in big arrays. Note that it's
            // wasteful (but convenient) to be storing three pointers in each
            // SpectralFilm::Pixel structure since the addresses could be computed
            // based on the base pointers and pixel coordinates.
            int nPixels = pixelBounds.Area();
            double* bucketWeightBuffer = (double*)NativeMemory.AllocZeroed((nuint)(2 * nBuckets * nPixels), sizeof(double));
            AtomicDouble* splatBuffer = (AtomicDouble*)NativeMemory.AllocZeroed((nuint)(nBuckets * nPixels), sizeof(double));

            foreach (Point2i pp in pixelBounds)
            {
                ref Pixel pixel = ref pixels[pp];
                pixel.bucketSums = bucketWeightBuffer;
                bucketWeightBuffer += nBuckets;
                pixel.weightSums = bucketWeightBuffer;
                bucketWeightBuffer += nBuckets;
                pixel.bucketSplats = splatBuffer;
                splatBuffer += nBuckets;
            }
        }

        public override RGB GetPixelRGB(Point2i p, Float splatScale = 1)
        {
            // Note: this is effectively the same as RGBFilm::GetPixelRGB

            ref Pixel pixel = ref pixels[p];
            RGB rgb = new RGB((Float)pixel.rgbSum[0], (Float)pixel.rgbSum[1], (Float)pixel.rgbSum[2]);
            // Normalize _rgb_ with weight sum
            Float weightSum = (Float)pixel.rgbWeightSum;
            if (weightSum != 0)
                rgb /= weightSum;

            // Add splat value at pixel
            for (int c = 0; c < 3; ++c)
                rgb[c] += splatScale * (Float)(double)pixel.rgbSplat(c) / filterIntegral;

            // Convert _rgb_ to output RGB color space
            rgb = outputRGBFromSensorRGB * rgb;

            return rgb;
        }

        public override void AddSplat(Point2f p, SampledSpectrum L, SampledWavelengths lambda)
        {
            // This, too, is similar to RGBFilm::AddSplat(), with additions for
            // spectra.

            Globals.CHECK(!L.HasNaNs());

            // Convert sample radiance to _PixelSensor_ RGB
            RGB rgb = sensor.ToSensorRGB(L, lambda);

            // Optionally clamp sensor RGB value
            Float m = std.max(rgb.r, std.max(rgb.g, rgb.b));
            if (m > maxComponentValue)
                rgb *= maxComponentValue / m;

            // Spectral clamping and normalization.
            Float lm = L.MaxComponentValue();
            if (lm > maxComponentValue)
                L *= maxComponentValue / lm;
            L = Globals.SafeDiv(L, lambda.PDF()) / NSpectrumSamples;

            // Compute bounds of affected pixels for splat, _splatBounds_
            Point2f pDiscrete = p + new Vector2f(0.5f, 0.5f);
            Vector2f radius = filter.Radius();
            Bounds2i splatBounds = new Bounds2i((Point2i)Globals.Floor(pDiscrete - radius),
                                                (Point2i)Globals.Floor(pDiscrete + radius) + new Vector2i(1, 1));
            splatBounds = Globals.Intersect(splatBounds, pixelBounds);

            // Splat both RGB and spectral bucket contributions.
            foreach (Point2i pi in splatBounds)
            {
                // Evaluate filter at _pi_ and add splat contribution
                Float wt = filter.Evaluate((Point2f)(p - (Point2f)pi - new Vector2f(0.5f, 0.5f)));
                if (wt != 0)
                {
                    ref Pixel pixel = ref pixels[pi];

                    for (int i = 0; i < 3; ++i)
                        pixel.rgbSplat(i).Add(wt * rgb[i]);

                    for (int i = 0; i < NSpectrumSamples; ++i)
                    {
                        int b = LambdaToBucket(lambda[i]);
                        pixel.bucketSplats[b].Add(wt * L[i]);
                    }
                }
            }
        }

        public override void WriteImage(ImageMetadata metadata, Float splatScale = 1)
        {
            Image image = GetImage(metadata, splatScale);
            Globals.LOG_VERBOSE("Writing image %s with bounds %s", filename, pixelBounds);
            image.Write(filename, metadata);
        }

        // Returns an image with both RGB and spectral components, following
        // the layout proposed in "An OpenEXR Layout for Sepctral Images" by
        // Fichet et al., https://jcgt.org/published/0010/03/01/.
        public override Image GetImage(ImageMetadata metadata, Float splatScale = 1)
        {
            // Convert image to RGB and compute final pixel values
            Globals.LOG_VERBOSE("Computing final weighted pixel values");
            PixelFormat format = writeFP16 ? PixelFormat.Half : PixelFormat.Float;

            List<string> imageChannels = new List<string> { "R", "G", "B" };
            for (int i = 0; i < nBuckets; ++i)
            {
                // The OpenEXR spectral layout takes the bucket center (and then
                // determines bucket widths based on the neighbor wavelengths).
                string lambda = Globals.StringPrintf("%.3fnm", Globals.Lerp((i + 0.5f) / nBuckets, lambdaMin, lambdaMax));
                // Convert any '.' to ',' in the number since OpenEXR uses '.' for
                // separating layers.
                lambda = lambda.Replace('.', ',');

                imageChannels.Add("S0." + lambda);
            }
            Vector2i diag = pixelBounds.Diagonal();
            Image image = new Image(format, new Point2i(diag.x, diag.y), imageChannels);

            int nClamped = 0;
            Globals.ParallelFor2D(pixelBounds, (Point2i p) =>
            {
                ref Pixel pixel = ref pixels[p];

                RGB rgb = GetPixelRGB(p, splatScale);

                // Clamp to max representable fp16 to avoid Infs
                if (writeFP16)
                {
                    for (int c = 0; c < 3; ++c)
                    {
                        if (rgb[c] > 65504)
                        {
                            rgb[c] = 65504;
                            Interlocked.Increment(ref nClamped);
                        }
                    }
                }

                Point2i pOffset = new Point2i(p.x - pixelBounds.pMin.x, p.y - pixelBounds.pMin.y);
                image.SetChannels(pOffset, new Float[] { rgb[0], rgb[1], rgb[2] });

                // Set spectral channels. Hardcoded assuming that they come
                // immediately after RGB, as is currently specified above.
                for (int i = 0; i < nBuckets; ++i)
                {
                    Float c = 0;
                    if (pixel.weightSums[i] > 0)
                    {
                        c = (Float)(pixel.bucketSums[i] / pixel.weightSums[i] +
                                    splatScale * (double)pixel.bucketSplats[i] / filterIntegral);
                        if (writeFP16 && c > 65504)
                        {
                            c = 65504;
                            Interlocked.Increment(ref nClamped);
                        }
                    }
                    image.SetChannel(pOffset, 3 + i, c);
                }
            });

            if (Volatile.Read(ref nClamped) > 0)
                Globals.Warning(Globals.StringPrintf("%d pixel values clamped to maximum fp16 value.", Volatile.Read(ref nClamped)));

            metadata.pixelBounds = pixelBounds;
            metadata.fullResolution = fullResolution;
            metadata.colorSpace = colorSpace;
            metadata.strings["spectralLayoutVersion"] = "1.0";
            // FIXME: if the RealisticCamera is being used, then we're actually
            // storing "J.m^-2", but that isn't a supported value for
            // "emissiveUnits" in the spec.
            metadata.strings["emissiveUnits"] = "W.m^-2.sr^-1";

            return image;
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ SpectralFilm %s lambdaMin: %f lambdaMax: %f nBuckets: %d " +
                                        "writeFP16: %s maxComponentValue: %f ]",
                                        BaseToString(), lambdaMin, lambdaMax, nBuckets, writeFP16, maxComponentValue);
        }

        public static SpectralFilm Create(ParameterDictionary parameters, Float exposureTime, Filter filter,
                                          RGBColorSpace colorSpace, FileLoc loc, Allocator alloc)
        {
            PixelSensor sensor = PixelSensor.Create(parameters, colorSpace, exposureTime, loc, alloc);
            FilmBaseParameters filmBaseParameters = new FilmBaseParameters(parameters, filter, sensor, loc);
            bool writeFP16 = parameters.GetOneBool("savefp16", true);

            if (!Globals.HasExtension(filmBaseParameters.filename, "exr"))
                Globals.ErrorExit(loc, Globals.StringPrintf("%s: EXR is the only output format supported by the SpectralFilm.",
                                                            filmBaseParameters.filename));

            int nBuckets = parameters.GetOneInt("nbuckets", 16);
            Float lambdaMin = parameters.GetOneFloat("lambdamin", Lambda_min);
            Float lambdaMax = parameters.GetOneFloat("lambdamax", Lambda_max);
            if (lambdaMin < Lambda_min || lambdaMax > Lambda_max)
                Globals.ErrorExit(Globals.StringPrintf(
                    "Unfortunately pbrt must be recompiled to render wavelengths " +
                    "beyond the [%f,%f] range ([%f,%f] was specified). Please " +
                    "update Lambda_min and/or Lambda_max as necessary in " +
                    "src/pbrt/util/spectrum.h and recompile.",
                    (Float)Lambda_min, (Float)Lambda_max, lambdaMin, lambdaMax));

            Float maxComponentValue = parameters.GetOneFloat("maxcomponentvalue", Infinity);

            return new SpectralFilm(filmBaseParameters, lambdaMin, lambdaMax, nBuckets, colorSpace, maxComponentValue,
                                    writeFP16, alloc);
        }

        public override RGB ToOutputRGB(SampledSpectrum L, SampledWavelengths lambda)
        {
            Globals.LOG_FATAL("ToOutputRGB() is unimplemented. But that's ok since it's only used " +
                              "in the SPPM integrator, which is inherently very much based on " +
                              "RGB output.");
            return default;
        }

        public override void ResetPixel(Point2i p)
        {
            ref Pixel pix = ref pixels[p];
            pix.rgbSum[0] = pix.rgbSum[1] = pix.rgbSum[2] = 0.0;
            pix.rgbWeightSum = 0.0;
            pix.rgbSplat0 = pix.rgbSplat1 = pix.rgbSplat2 = new AtomicDouble(0.0);
            for (int i = 0; i < nBuckets; ++i)
            {
                pix.bucketSums[i] = 0;
                pix.weightSums[i] = 0;
                pix.bucketSplats[i] = new AtomicDouble(0.0);
            }
        }

        // SpectralFilm Private Methods
        int LambdaToBucket(Float lambda)
        {
            Globals.DCHECK_RARE(1e6f, lambda < lambdaMin || lambda > lambdaMax);
            int bucket = (int)(nBuckets * (lambda - lambdaMin) / (lambdaMax - lambdaMin));
            return Globals.Clamp(bucket, 0, nBuckets - 1);
        }

        // SpectralFilm Private Members
        RGBColorSpace colorSpace;
        Float lambdaMin, lambdaMax;
        int nBuckets;
        Float maxComponentValue;
        bool writeFP16;
        Float filterIntegral;
        Array2D<Pixel> pixels;
        SquareMatrix3 outputRGBFromSensorRGB;
    }

    public abstract unsafe partial class Film
    {
        public static Film Create(string name, ParameterDictionary parameters, Float exposureTime,
                                  CameraTransform cameraTransform, Filter filter, FileLoc loc, Allocator alloc)
        {
            Film film = null;
            if (name == "rgb")
                film = RGBFilm.Create(parameters, exposureTime, filter, parameters.ColorSpace(), loc, alloc);
            else if (name == "gbuffer")
                film = GBufferFilm.Create(parameters, exposureTime, cameraTransform, filter, parameters.ColorSpace(), loc,
                                          alloc);
            else if (name == "spectral")
                film = SpectralFilm.Create(parameters, exposureTime, filter, parameters.ColorSpace(), loc, alloc);
            else
                Globals.ErrorExit(loc, Globals.StringPrintf("%s: film type unknown.", name));

            if (film == null)
                Globals.ErrorExit(loc, Globals.StringPrintf("%s: unable to create film.", name));

            parameters.ReportUnused();
            return film;
        }
    }

}  // namespace pbrt
