// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// cmd/pbrt.cpp

#if PBRT_FLOAT_AS_DOUBLE
global using Float = System.Double;
#else
global using Float = System.Single;
#endif

using System;
using System.Collections.Generic;
using pbrt;
using static global::pbrt.pbrt;

public static unsafe class PbrtMain
{
    static void usage(string msg = "")
    {
        if (msg.Length != 0)
            Console.Error.Write(StringPrintf("pbrt: %s\n\n", msg));

        Console.Error.Write(StringPrintf(@"usage: pbrt [<options>] <filename.pbrt...>

Rendering options:
  --cropwindow <x0,x1,y0,y1>    Specify an image crop window w.r.t. [0,1]^2.
  --debugstart <values>         Inform the Integrator where to start rendering for
                                faster debugging. (<values> are Integrator-specific
                                and come from error message text.)
  --disable-image-textures      Always return the average value of image textures.
  --disable-pixel-jitter        Always sample pixels at their centers.
  --disable-texture-filtering   Point-sample all textures.
  --disable-wavelength-jitter   Always sample the same %d wavelengths of light.
  --displacement-edge-scale <s> Scale target triangle edge length by given value.
                                (Default: 1)
  --display-server <addr:port>  Connect to display server at given address and port
                                to display the image as it's being rendered.
  --force-diffuse               Convert all materials to be diffuse.)
  --fullscreen                  Render fullscreen. Only supported with --interactive.)
  --help                        Print this help text.
  --interactive                 Enable interactive rendering mode.
  --mse-reference-image         Filename for reference image to use for MSE computation.
  --mse-reference-out           File to write MSE error vs spp results.
  --nthreads <num>              Use specified number of threads for rendering.
  --outfile <filename>          Write the final image to the given filename.
  --pixel <x,y>                 Render just the specified pixel.
  --pixelbounds <x0,x1,y0,y1>   Specify an image crop window w.r.t. pixel coordinates.
  --pixelmaterial <x,y>         Print information about the material visible in the
                                center of the pixel's extent.
  --pixelstats                  Record per-pixel statistics and write additional images
                                with their values.
  --quick                       Automatically reduce a number of quality settings
                                to render more quickly.
  --quiet                       Suppress all text output other than error messages.
  --render-coord-sys <name>     Coordinate system to use for the scene when rendering,
                                where name is ""camera"", ""cameraworld"", or ""world"".
  --seed <n>                    Set random number generator seed. Default: 0.
  --stats                       Print various statistics after rendering completes.
  --spp <n>                     Override number of pixel samples specified in scene
                                description file.
  --wavefront                   Use wavefront volumetric path integrator.
  --write-partial-images        Periodically write the current image to disk, rather
                                than waiting for the end of rendering. Default: disabled.

Logging options:
  --log-file <filename>         Filename to write logging messages to. Default: none;
                                messages are printed to standard error. Implies
                                --log-level verbose if specified.
  --log-level <level>           Log messages at or above this level, where <level>
                                is ""verbose"", ""error"", or ""fatal"". Default: ""error"".
  --log-utilization             Periodically print processor and memory use in verbose-
                                level logging.

Reformatting options:
  --format                      Print a reformatted version of the input file(s) to
                                standard output. Does not render an image.
  --toply                       Print a reformatted version of the input file(s) to
                                standard output and convert all triangle meshes to
                                PLY files. Does not render an image.
  --upgrade                     Upgrade a pbrt-v3 file to pbrt-v4's format.
", NSpectrumSamples));
        Environment.Exit(msg.Length == 0 ? 0 : 1);
    }

    // main program
    public static int Main(string[] argv)
    {
        // Convert command-line arguments to vector of strings
        List<string> args = GetCommandLineArguments(argv);

        // Declare variables for parsed command line
        PBRTOptions options = new PBRTOptions();
        List<string> filenames = new List<string>();
        string logLevel = "error";
        string renderCoordSys = "cameraworld";
        bool format = false, toPly = false;

        Action<string> onError = (string err) =>
        {
            usage(err);
            Environment.Exit(1);
        };

        // Process command-line arguments
        int end = args.Count;
        for (int iter = 0; iter != end; ++iter)
        {
            if (args[iter][0] != '-')
            {
                filenames.Add(args[iter]);
                continue;
            }

            string cropWindow = "", pixelBounds = "", pixel = "", pixelMaterial = "";
            if (ParseArg(args, ref iter, end, "cropwindow", ref cropWindow, onError))
            {
                List<Float> c = SplitStringToFloats(cropWindow, ',');
                if (c.Count != 4)
                {
                    usage("Didn't find four values after --cropwindow");
                    return 1;
                }
                options.cropWindow = new Bounds2f(new Point2f(c[0], c[2]), new Point2f(c[1], c[3]));
            }
            else if (ParseArg(args, ref iter, end, "pixel", ref pixel, onError))
            {
                List<int> p = SplitStringToInts(pixel, ',');
                if (p.Count != 2)
                {
                    usage("Didn't find two values after --pixel");
                    return 1;
                }
                options.pixelBounds = new Bounds2i(new Point2i(p[0], p[1]), new Point2i(p[0] + 1, p[1] + 1));
            }
            else if (ParseArg(args, ref iter, end, "pixelbounds", ref pixelBounds, onError))
            {
                List<int> p = SplitStringToInts(pixelBounds, ',');
                if (p.Count != 4)
                {
                    usage("Didn't find four integer values after --pixelbounds");
                    return 1;
                }
                options.pixelBounds = new Bounds2i(new Point2i(p[0], p[2]), new Point2i(p[1], p[3]));
            }
            else if (ParseArg(args, ref iter, end, "pixelmaterial", ref pixelMaterial, onError))
            {
                List<int> p = SplitStringToInts(pixelMaterial, ',');
                if (p.Count != 2)
                {
                    usage("Didn't find two values after --pixelmaterial");
                    return 1;
                }
                options.pixelMaterial = new Point2i(p[0], p[1]);
            }
            else if (ParseArg(args, ref iter, end, "debugstart", ref options.debugStart, onError) ||
                     ParseArg(args, ref iter, end, "disable-image-textures", ref options.disableImageTextures, onError) ||
                     ParseArg(args, ref iter, end, "disable-pixel-jitter", ref options.disablePixelJitter, onError) ||
                     ParseArg(args, ref iter, end, "disable-texture-filtering", ref options.disableTextureFiltering, onError) ||
                     ParseArg(args, ref iter, end, "disable-wavelength-jitter", ref options.disableWavelengthJitter, onError) ||
                     ParseArg(args, ref iter, end, "displacement-edge-scale", ref options.displacementEdgeScale, onError) ||
                     ParseArg(args, ref iter, end, "display-server", ref options.displayServer, onError) ||
                     ParseArg(args, ref iter, end, "force-diffuse", ref options.forceDiffuse, onError) ||
                     ParseArg(args, ref iter, end, "format", ref format, onError) ||
                     ParseArg(args, ref iter, end, "log-level", ref logLevel, onError) ||
                     ParseArg(args, ref iter, end, "log-utilization", ref options.logUtilization, onError) ||
                     ParseArg(args, ref iter, end, "log-file", ref options.logFile, onError) ||
                     ParseArg(args, ref iter, end, "interactive", ref options.interactive, onError) ||
                     ParseArg(args, ref iter, end, "fullscreen", ref options.fullscreen, onError) ||
                     ParseArg(args, ref iter, end, "mse-reference-image", ref options.mseReferenceImage, onError) ||
                     ParseArg(args, ref iter, end, "mse-reference-out", ref options.mseReferenceOutput, onError) ||
                     ParseArg(args, ref iter, end, "nthreads", ref options.nThreads, onError) ||
                     ParseArg(args, ref iter, end, "outfile", ref options.imageFile, onError) ||
                     ParseArg(args, ref iter, end, "pixelstats", ref options.recordPixelStatistics, onError) ||
                     ParseArg(args, ref iter, end, "quick", ref options.quickRender, onError) ||
                     ParseArg(args, ref iter, end, "quiet", ref options.quiet, onError) ||
                     ParseArg(args, ref iter, end, "render-coord-sys", ref renderCoordSys, onError) ||
                     ParseArg(args, ref iter, end, "seed", ref options.seed, onError) ||
                     ParseArg(args, ref iter, end, "spp", ref options.pixelSamples, onError) ||
                     ParseArg(args, ref iter, end, "stats", ref options.printStatistics, onError) ||
                     ParseArg(args, ref iter, end, "toply", ref toPly, onError) ||
                     ParseArg(args, ref iter, end, "wavefront", ref options.wavefront, onError) ||
                     ParseArg(args, ref iter, end, "write-partial-images", ref options.writePartialImages, onError) ||
                     ParseArg(args, ref iter, end, "upgrade", ref options.upgrade, onError))
            {
                // success
            }
            else if (args[iter] == "--help" || args[iter] == "-help" || args[iter] == "-h")
            {
                usage();
                return 0;
            }
            else
            {
                usage(StringPrintf("argument \"%s\" unknown", args[iter]));
                return 1;
            }
        }

        // Print welcome banner
        if (!options.quiet && !format && !toPly && !options.upgrade)
        {
            Console.Out.Write("pbrt version 4 (Magi: C# port; built from pbrt-v4 sources)\n");
            Console.Out.Write("Copyright (c)1998-2021 Matt Pharr, Wenzel Jakob, and Greg Humphreys.\n");
            Console.Out.Write("The source code to pbrt (but *not* the book contents) is covered " +
                              "by the Apache 2.0 License.\n");
            Console.Out.Write("See the file LICENSE.txt for the conditions of the license.\n");
            Console.Out.Flush();
        }

        // Check validity of provided arguments
        if (renderCoordSys == "camera")
            options.renderingSpace = RenderingCoordinateSystem.Camera;
        else if (renderCoordSys == "cameraworld")
            options.renderingSpace = RenderingCoordinateSystem.CameraWorld;
        else if (renderCoordSys == "world")
            options.renderingSpace = RenderingCoordinateSystem.World;
        else
            ErrorExit(StringPrintf("%s: unknown rendering coordinate system.", renderCoordSys));

        if (options.mseReferenceImage.Length != 0 && options.mseReferenceOutput.Length == 0)
            ErrorExit("Must provide MSE reference output filename via --mse-reference-out");
        if (options.mseReferenceOutput.Length != 0 && options.mseReferenceImage.Length == 0)
            ErrorExit("Must provide MSE reference image via --mse-reference-image");

        if (options.pixelMaterial && options.useGPU)
        {
            Warning("Disabling --use-gpu since --pixelmaterial was specified.");
            options.useGPU = false;
        }

        if (options.useGPU && options.wavefront)
            Warning("Both --gpu and --wavefront were specified; --gpu takes precedence.");

        if (options.pixelMaterial && options.wavefront)
        {
            Warning("Disabling --wavefront since --pixelmaterial was specified.");
            options.wavefront = false;
        }

        if (options.interactive && !(options.useGPU || options.wavefront))
            ErrorExit("The --interactive option is only supported with the --gpu " +
                      "and --wavefront integrators.");

        if (options.fullscreen && !options.interactive)
            ErrorExit("The --fullscreen option is only supported in interactive mode");

        if (options.interactive && options.quickRender)
            ErrorExit("The --quick option is not supported in interactive mode");

        options.logLevel = LogLevelFromString(logLevel);

        // Initialize pbrt
        InitPBRT(options);

        if (format || toPly || options.upgrade)
        {
            FormattingParserTarget formattingTarget = new FormattingParserTarget(toPly, options.upgrade);
            ParseFiles(formattingTarget, filenames.ToArray());
            formattingTarget.Finish();
        }
        else
        {
            // Parse provided scene description files
            BasicScene scene = new BasicScene();
            BasicSceneBuilder builder = new BasicSceneBuilder(scene);
            ParseFiles(builder, filenames.ToArray());

            // Render the scene
            if (Options.useGPU || Options.wavefront)
                ErrorExit("The wavefront/GPU renderer requires CUDA/OptiX and is not ported to Magi; use the CPU integrators.");
            else
                RenderCPU(scene);

            LOG_VERBOSE("Memory used after post-render cleanup: %s", GetCurrentRSS());
            // Clean up after rendering the scene
            CleanupPBRT();
        }
        return 0;
    }
}
