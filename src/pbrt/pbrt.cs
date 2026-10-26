// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Magi: C# port of pbrt-v4. File: pbrt.h

#if PBRT_FLOAT_AS_DOUBLE
global using Float = System.Double;
global using FloatBits = System.UInt64;
#else
global using Float = System.Single;
global using FloatBits = System.UInt32;
#endif

global using int8_t = System.SByte;
global using uint8_t = System.Byte;
global using int16_t = System.Int16;
global using uint16_t = System.UInt16;
global using int32_t = System.Int32;
global using uint32_t = System.UInt32;
global using int64_t = System.Int64;
global using uint64_t = System.UInt64;
global using size_t = System.UInt64;
global using ptrdiff_t = System.Int64;
global using uintptr_t = System.UInt64;

global using static pbrt.Globals;

using System;

namespace pbrt
{
    // All of pbrt's free functions (namespace-scope functions and constants) live in
    // this partial class; each file adds its own part. "global using static" above
    // makes them callable unqualified, as in C++.
    public static unsafe partial class Globals
    {
        // Define Cache Line Size Constant
        public const int PBRT_L1_CACHE_LINE_SIZE = 64;

        // Initialization and Cleanup Function Declarations
        // API Function Definitions (pbrt.cpp)
        public static void InitPBRT(PBRTOptions opt)
        {
            Options = opt;
            // API Initialization

            if (Options.quiet)
                SuppressErrorMessages();

            InitLogging(opt.logLevel, opt.logFile, opt.logUtilization, Options.useGPU);

            // General \pbrt Initialization
            int nThreads = Options.nThreads != 0 ? Options.nThreads : AvailableCores();
            ParallelInit(nThreads);  // Threads must be launched before the
                                     // profiler is initialized.

            if (Options.useGPU)
                LOG_FATAL("Options::useGPU set with non-GPU build");
            else
            {
                ColorEncoding.Init(new Allocator());
                // Before RGBColorSpace::Init!
                Spectra.Init(new Allocator());
                RGBToSpectrumTable.Init(new Allocator());

                RGBColorSpace.Init(new Allocator());
                Triangle.Init(new Allocator());
                BilinearPatch.Init(new Allocator());
            }

            InitBufferCaches();
        }

        public static void CleanupPBRT()
        {
            ForEachThread(() => ReportThreadStats());

            if (Options.recordPixelStatistics)
                StatsWritePixelImages();

            if (Options.printStatistics)
            {
                PrintStats(Console.Out);
                ClearStats();
            }
            if (PrintCheckRare(Console.Out))
                ErrorExit("CHECK_RARE failures");

            // API Cleanup
            ParallelCleanup();

            Options = null;
        }
    }
}
