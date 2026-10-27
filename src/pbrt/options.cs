// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Magi: C# port of pbrt-v4. File: options.h, options.cpp

namespace pbrt
{
    // RenderingCoordinateSystem Definition
    public enum RenderingCoordinateSystem { Camera, CameraWorld, World }

    // BasicPBRTOptions Definition
    public unsafe class BasicPBRTOptions
    {
        public int seed = 0;
        public bool quiet = false;
        public bool disablePixelJitter = false, disableWavelengthJitter = false;
        public bool disableTextureFiltering = false;
        public bool disableImageTextures = false;
        public bool forceDiffuse = false;
        public bool useGPU = false;
        public bool wavefront = false;
        public bool interactive = false;
        public bool fullscreen = false;
        public RenderingCoordinateSystem renderingSpace = RenderingCoordinateSystem.CameraWorld;
    }

    // PBRTOptions Definition
    public unsafe class PBRTOptions : BasicPBRTOptions
    {
        public int nThreads = 0;
        public LogLevel logLevel = LogLevel.Error;
        public string logFile = "";
        public bool logUtilization = false;
        public bool writePartialImages = false;
        public bool recordPixelStatistics = false;
        public bool printStatistics = false;
        public pstd.optional<int> pixelSamples;
        public pstd.optional<int> gpuDevice;
        public bool quickRender = false;
        public bool upgrade = false;
        public string imageFile = "";
        public string mseReferenceImage = "", mseReferenceOutput = "";
        public string debugStart = "";
        public string displayServer = "";
        public pstd.optional<Bounds2f> cropWindow;
        public pstd.optional<Bounds2i> pixelBounds;
        public pstd.optional<Point2i> pixelMaterial;
        public Float displacementEdgeScale = 1;

        public override string ToString()
        {
            return StringPrintf(
                "[ PBRTOptions seed: %s quiet: %s disablePixelJitter: %s " +
                "disableWavelengthJitter: %s disableTextureFiltering: %s disableImageTextures: %s " +
                "forceDiffuse: %s useGPU: %s wavefront: %s interactive: %s fullscreen %s " +
                "renderingSpace: %s nThreads: %s logLevel: %s logFile: %s logUtilization: %s " +
                "writePartialImages: %s recordPixelStatistics: %s " +
                "printStatistics: %s pixelSamples: %s gpuDevice: %s quickRender: %s upgrade: %s " +
                "imageFile: %s mseReferenceImage: %s mseReferenceOutput: %s debugStart: %s " +
                "displayServer: %s cropWindow: %s pixelBounds: %s pixelMaterial: %s " +
                "displacementEdgeScale: %f ]",
                seed, quiet, disablePixelJitter, disableWavelengthJitter, disableTextureFiltering,
                disableImageTextures, forceDiffuse, useGPU, wavefront, interactive, fullscreen,
                renderingSpace, nThreads, logLevel, logFile, logUtilization, writePartialImages,
                recordPixelStatistics, printStatistics, pixelSamples, gpuDevice, quickRender, upgrade,
                imageFile, mseReferenceImage, mseReferenceOutput, debugStart, displayServer, cropWindow,
                pixelBounds, pixelMaterial, displacementEdgeScale);
        }
    }

    public static unsafe partial class pbrt
    {
        public static string ToString(RenderingCoordinateSystem r)
        {
            if (r == RenderingCoordinateSystem.Camera)
                return "RenderingCoordinateSystem::Camera";
            else if (r == RenderingCoordinateSystem.CameraWorld)
                return "RenderingCoordinateSystem::CameraWorld";
            else
            {
                CHECK(r == RenderingCoordinateSystem.World);
                return "RenderingCoordinateSystem::World";
            }
        }

        // Options Global Variable Declaration
        public static PBRTOptions Options;

        // Options Inline Functions
        public static BasicPBRTOptions GetOptions() { return Options; }
    }
}
