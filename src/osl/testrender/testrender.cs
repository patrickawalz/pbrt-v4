// OSL port: testrender/testrender.cpp
//
// testrender -- Test Renderer for Open Shading Language: reads a scene description, path traces it and writes the image (HALF channels for OpenEXR).

using System;
using System.Collections.Generic;
using System.Globalization;
using OIIO;
using OSL.TestShade;

namespace OSL
{
    public static unsafe class TestRender
    {
        private static ShadingSystem shadingsys;
        private static bool debug1;
        private static bool debug2;
        private static bool verbose;
        private static bool runstats;
        private static bool profile;
        private static bool O0, O1, O2;
        private static int llvm_opt = 1;  // LLVM optimization level
        private static bool debugnan;
        private static bool debug_uninit;
        private static bool userdata_isconnected;
        private static string extraoptions = "";
        private static string texoptions = "";
        private static int xres = 640, yres = 480;
        private static int aa = 1, max_bounces = 1000000, rr_depth = 5;
        private static bool no_jitter;
        private static float show_albedo_scale;
        private static int show_globals;
        private static int num_threads;
        private static int iters = 1;
        private static string scenefile = "";
        private static string imagefile = "";
        private static string shaderpath = "";

        /// Set shading system global attributes based on command line options.
        private static void set_shadingsys_options()
        {
            shadingsys.attribute("debug", debug2 ? 2 : (debug1 ? 1 : 0));
            shadingsys.attribute("compile_report", (debug1 || debug2) ? 1 : 0);
            int opt = 2;  // default
            if (O0)
            {
                opt = 0;
            }
            if (O1)
            {
                opt = 1;
            }
            if (O2)
            {
                opt = 2;
            }
            string opt_env = Environment.GetEnvironmentVariable("TESTSHADE_OPT");
            if (!string.IsNullOrEmpty(opt_env))  // overrides opt
            {
                opt = int.Parse(opt_env, CultureInfo.InvariantCulture);
            }
            shadingsys.attribute("optimize", opt);
            string llvm_opt_env = Environment.GetEnvironmentVariable("TESTSHADE_LLVM_OPT");
            if (!string.IsNullOrEmpty(llvm_opt_env))  // overrides llvm_opt
            {
                llvm_opt = int.Parse(llvm_opt_env, CultureInfo.InvariantCulture);
            }
            shadingsys.attribute("llvm_optimize", llvm_opt);
            shadingsys.attribute("profile", profile ? 1 : 0);
            shadingsys.attribute("debug_nan", debugnan ? 1 : 0);
            shadingsys.attribute("debug_uninit", debug_uninit ? 1 : 0);
            shadingsys.attribute("userdata_isconnected", userdata_isconnected ? 1 : 0);
            if (shaderpath.Length != 0)
            {
                shadingsys.attribute("searchpath:shader", shaderpath);
            }
            if (extraoptions.Length != 0)
            {
                shadingsys.attribute("options", extraoptions);
            }
            if (texoptions.Length != 0)
            {
                shadingsys.texturesys().attribute("options", texoptions);
            }
            // Always generate llvm debugging info and profiling events
            shadingsys.attribute("llvm_debugging_symbols", 1);
            shadingsys.attribute("llvm_profiling_events", 1);
        }

        private static void usage()
        {
            Console.Error.Write("testrender -- Test Renderer for Open Shading Language\n");
            Console.Error.Write("Usage:  testrender [options] scene.xml outputfilename\n");
            Console.Error.Write("  -v                  Verbose output\n");
            Console.Error.Write("  -t N                Set thread count (default = 0: auto-detect #cores)\n");
            Console.Error.Write("  --res W H, -r W H   Set resolution of output image to W x H\n");
            Console.Error.Write("  --debug, --debug2   Debugging info\n");
            Console.Error.Write("  --runstats, --profile\n");
            Console.Error.Write("  -aa N               Trace NxN rays per pixel\n");
            Console.Error.Write("  --no-jitter         Disable AA pixel jitter\n");
            Console.Error.Write("  -albedo SCALE       Visualize the albedo of each pixel instead of path tracing\n");
            Console.Error.Write("  -normals -geonormals -utangents -vtangents -uvs   Visualize shading globals\n");
            Console.Error.Write("  --iters N           Number of iterations\n");
            Console.Error.Write("  -O0 -O1 -O2         Runtime shader optimization\n");
            Console.Error.Write("  --path SEARCHPATH   Specify oso search path\n");
            Console.Error.Write("  --options LIST, --texoptions LIST\n");
        }

        private static string next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
            {
                Console.Error.Write("testrender: missing argument for " + args[i] + "\n");
                Environment.Exit(1);
            }
            i++;
            return args[i];
        }

        private static void getargs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "-v":
                        verbose = true;
                        break;
                    case "-t":
                        num_threads = int.Parse(next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--res":
                    case "-res":
                    case "-r":
                        xres = int.Parse(next(args, ref i), CultureInfo.InvariantCulture);
                        yres = int.Parse(next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--optix":
                    case "--saveptx":
                    case "--warmup":
                    case "--print-group-stats":
                    case "--optix_no_inline":
                    case "--optix_no_inline_layer_funcs":
                    case "--optix_no_merge_layer_funcs":
                    case "--optix_no_inline_rend_lib":
                    case "--optix_no_rend_lib_bitcode":
                        break;
                    case "--optix_no_inline_thresh":
                    case "--optix_force_inline_thresh":
                        next(args, ref i);
                        break;
                    case "--debug":
                        debug1 = true;
                        break;
                    case "--debug2":
                        debug2 = true;
                        break;
                    case "--runstats":
                    case "--stats":
                        runstats = true;
                        break;
                    case "--profile":
                        profile = true;
                        break;
                    case "-aa":
                    case "--aa":
                        aa = int.Parse(next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--no-jitter":
                    case "-no-jitter":
                        no_jitter = true;
                        break;
                    case "-albedo":
                    case "--albedo":
                        show_albedo_scale = float.Parse(next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "-normals":
                    case "--normals":
                        show_globals = 1;
                        break;
                    case "-geonormals":
                    case "--geonormals":
                        show_globals = 2;
                        break;
                    case "-utangents":
                    case "--utangents":
                        show_globals = 3;
                        break;
                    case "-vtangents":
                    case "--vtangents":
                        show_globals = 4;
                        break;
                    case "-uvs":
                    case "--uvs":
                        show_globals = 5;
                        break;
                    case "--iters":
                    case "-iters":
                        iters = int.Parse(next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "-O0":
                    case "--O0":
                        O0 = true;
                        break;
                    case "-O1":
                    case "--O1":
                        O1 = true;
                        break;
                    case "-O2":
                    case "--O2":
                        O2 = true;
                        break;
                    case "--llvm_opt":
                        llvm_opt = int.Parse(next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--debugnan":
                        debugnan = true;
                        break;
                    case "--path":
                        shaderpath = next(args, ref i);
                        break;
                    case "--options":
                        extraoptions = next(args, ref i);
                        break;
                    case "--texoptions":
                        texoptions = next(args, ref i);
                        break;
                    case "-h":
                    case "--help":
                        usage();
                        Environment.Exit(0);
                        break;
                    default:
                        if (scenefile.Length == 0)
                        {
                            scenefile = a;
                        }
                        else if (imagefile.Length == 0)
                        {
                            imagefile = a;
                        }
                        break;
                }
            }
            if (scenefile.Length == 0)
            {
                Console.Error.Write("testrender: Must specify an xml scene file to open\n\n");
                usage();
                Environment.Exit(1);
            }
            if (imagefile.Length == 0)
            {
                Console.Error.Write("testrender: Must specify a filename for output render\n\n");
                usage();
                Environment.Exit(1);
            }
        }

        public static int Main(string[] argv)
        {
            DateTime timer = DateTime.UtcNow;

            // Read command line arguments
            getargs(argv);

            // Allow magic env variable TESTRENDER_AA to override the --aa option, this is helpful for certain CI tests in special debug modes that would
            // be too slow to be practical.
            string aa_env = Environment.GetEnvironmentVariable("TESTRENDER_AA");
            int aaoverride = 0;
            if (!string.IsNullOrEmpty(aa_env))
            {
                int.TryParse(aa_env, NumberStyles.Integer, CultureInfo.InvariantCulture, out aaoverride);
            }
            if (aaoverride != 0)
            {
                aa = aaoverride;
            }

            SimpleRaytracer rend = new SimpleRaytracer();

            // Other renderer and global options
            if (debug1 || verbose)
            {
                rend.errhandler().verbose = true;
            }
            rend.attribute("max_bounces", max_bounces);
            rend.attribute("rr_depth", rr_depth);
            rend.attribute("aa", aa);
            rend.attribute("no_jitter", no_jitter ? 1 : 0);
            rend.attribute("show_albedo_scale", show_albedo_scale);
            rend.attribute("show_globals", show_globals);
            rend.num_threads = num_threads;

            // Create a new shading system. We pass it the RendererServices object that services callbacks from the shading system, the TextureSystem
            // (note: passing null just makes the ShadingSystem make its own TS), and an error handler.
            shadingsys = new ShadingSystem(rend, null, rend.errhandler());
            rend.shadingsys = shadingsys;

            // Register the layout of all closures known to this renderer. Any closure used by the shader which is not registered, or registered with a
            // different number of arguments will lead to a runtime error.
            ShadingClosures.register_closures(shadingsys);

            // Setup common attributes
            set_shadingsys_options();

            // Loads a scene, creating camera, geometry and assigning shaders
            rend.camera.resolution(xres, yres);
            rend.parse_scene_xml(scenefile);

            rend.prepare_render();

            rend.pixelbuf = new float[xres * yres * 3];

            double setuptime = (DateTime.UtcNow - timer).TotalSeconds;
            timer = DateTime.UtcNow;

            // Launch the kernel to render the scene
            for (int i = 0; i < iters; ++i)
            {
                rend.render(xres, yres);
            }
            double runtime = (DateTime.UtcNow - timer).TotalSeconds;
            timer = DateTime.UtcNow;

            // Write image to disk (HALF channels in OpenEXR)
            OutputBuffer buf = new OutputBuffer("pixels", imagefile, TypeDescs.TypeFloat, 3, xres, yres);
            buf.pixels = rend.pixelbuf;
            string lower = imagefile.ToLowerInvariant();
            if (!(lower.EndsWith(".exr") || lower.EndsWith(".tif") || lower.EndsWith(".tiff")))
            {
                rend.errhandler().errorfmt("Unable to write output image: unsupported format {}", imagefile);
            }
            else
            {
                ImageWriter.write(buf, "half");
            }
            double writetime = (DateTime.UtcNow - timer).TotalSeconds;

            // Print some debugging info
            if (debug1 || runstats || profile)
            {
                Console.Out.Write("\n");
                Console.Out.Write("Setup : " + Strutil.timeintervalformat(setuptime, 4) + "\n");
                Console.Out.Write("Run   : " + Strutil.timeintervalformat(runtime, 4) + "\n");
                Console.Out.Write("Write : " + Strutil.timeintervalformat(writetime, 4) + "\n");
                Console.Out.Write("\n");
                Console.Out.Write(shadingsys.getstats(5) + "\n");
            }

            // We're done with the shading system now
            rend.clear();
            return 0;
        }
    }
}
