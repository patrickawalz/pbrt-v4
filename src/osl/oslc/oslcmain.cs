// OSL port: oslc/oslcmain.cpp -- the oslc command line compiler

using System;
using System.Collections.Generic;
using System.IO;
using OIIO;

namespace OSL
{
    public static class OslcMain
    {
        private static void usage()
        {
            Console.Out.Write("oslc -- Open Shading Language compiler " + pvt.OSLCompilerImpl.OSL_LIBRARY_VERSION_STRING
                              + "\nCopyright Contributors to the Open Shading Language project.\n"
                              + "Usage:  oslc [options] file\n"
                              + "  Options:\n"
                              + "\t--help         Print this usage message\n"
                              + "\t-o filename    Specify output filename\n"
                              + "\t-v             Verbose mode\n"
                              + "\t-q             Quiet mode\n"
                              + "\t-Ipath         Add path to the #include search path\n"
                              + "\t-Dsym[=val]    Define preprocessor symbol\n"
                              + "\t-Usym          Undefine preprocessor symbol\n"
                              + "\t-O0, -O1, -O2  Set optimization level (default=1)\n"
                              + "\t-d             Debug mode\n"
                              + "\t-E             Only preprocess the input and output to stdout\n"
                              + "\t-Werror        Treat all warnings as errors\n"
                              + "\t-embed-source  Embed preprocessed source in the oso file\n"
                              + "\t-buffer        (debugging) Force compile from buffer\n"
                              + "\t-MD, -MMD      Write a depfile containing headers used, to a file\n"
                              + "\t-M, -MM        Like -MD, but write depfile to stdout\n"
                              + "\t-MF filename   Specify the name of the depfile to output (for -MD, -MMD)\n"
                              + "\t-MT target     Specify a custom dependency target name for -M...\n");
        }

        // Subclass ErrorHandler because we want our messages to appear somewhat different than the default ErrorHandler base class, in
        // order to match typical compiler command line messages.
        private sealed class OSLC_ErrorHandler : ErrorHandler
        {
            public bool verbose;
            public bool quiet;
            private static readonly object err_mutex = new object();

            public override void handle(int errcode, string msg)
            {
                lock (err_mutex)
                {
                    switch (errcode)
                    {
                        case (int)ErrorCode.EH_INFO:
                            if (verbose)
                            {
                                Console.Out.WriteLine(msg);
                            }
                            break;
                        case (int)ErrorCode.EH_WARNING:
                            Console.Error.WriteLine(msg);
                            break;
                        case (int)ErrorCode.EH_ERROR:
                        case (int)ErrorCode.EH_SEVERE:
                            Console.Error.WriteLine(msg);
                            break;
                        case (int)ErrorCode.EH_DEBUG:
                            break;
                        default:
                            if (!quiet)
                            {
                                Console.Out.Write(msg);
                            }
                            break;
                    }
                }
            }
        }

        public static int Main(string[] argv)
        {
            if (argv.Length == 0)
            {
                usage();
                return 0;
            }

            List<string> args = new List<string>();
            bool quiet = false;
            bool compile_from_buffer = false;
            string shader_path = "";

            // Parse arguments from command line
            for (int a = 0; a < argv.Length; ++a)
            {
                string arg = argv[a];
                if (arg == "--help" || arg == "-h")
                {
                    usage();
                    return 0;
                }
                else if (arg == "-q" || arg == "-v")
                {
                    args.Add(arg);
                    quiet = arg == "-q";
                }
                else if (arg == "-E" || arg == "-M" || arg == "--dependencies" || arg == "-MM" || arg == "--user-dependencies")
                {
                    args.Add(arg);
                    quiet = true;
                }
                else if (arg == "-d" || arg == "-O" || arg == "-O0" || arg == "-O1" || arg == "-O2" || arg == "-Werror" || arg == "-embed-source"
                         || arg == "--embed-source" || arg == "-MD" || arg == "--write-dependencies" || arg == "-MMD"
                         || arg == "--write-user-dependencies" || arg.StartsWith("-MF", StringComparison.Ordinal)
                         || arg.StartsWith("-MT", StringComparison.Ordinal))
                {
                    // Valid command-line argument
                    args.Add(arg);
                    if (a < argv.Length - 1 && (arg == "-MF" || arg == "-MT"))
                    {
                        ++a;
                        args.Add(argv[a]);
                    }
                }
                else if (arg == "-o" && a < argv.Length - 1)
                {
                    // Output filepath
                    args.Add(arg);
                    ++a;
                    args.Add(argv[a]);
                }
                else if (arg.Length > 1 && arg[0] == '-' && (arg[1] == 'D' || arg[1] == 'U' || arg[1] == 'I'))
                {
                    args.Add(arg);
                }
                else if (arg == "-buffer")
                {
                    compile_from_buffer = true;
                }
                else
                {
                    // Shader to compile
                    shader_path = arg;
                }
            }

            if (shader_path.Length == 0)
            {
                Console.Out.Write("ERROR: Missing shader path\n\n");
                usage();
                return 1;
            }

            OSLC_ErrorHandler handler = new OSLC_ErrorHandler();
            handler.quiet = quiet;
            handler.verbose = args.Contains("-v");
            OSLCompiler compiler = new OSLCompiler(handler);
            bool ok = true;
            if (compile_from_buffer)
            {
                // Force a compile-from-buffer for debugging purposes
                string osobuffer = "";
                string sourcecode = "";
                try
                {
                    sourcecode = File.ReadAllText(shader_path);
                }
                catch (Exception)
                {
                    ok = false;
                }
                if (ok)
                {
                    ok = compiler.compile_buffer(sourcecode, out osobuffer, args, "", shader_path);
                }
                if (ok)
                {
                    try
                    {
                        File.WriteAllText(compiler.output_filename(), osobuffer, new System.Text.UTF8Encoding(false));
                    }
                    catch (Exception)
                    {
                        ok = false;
                    }
                }
            }
            else
            {
                // Ordinary compile from file
                ok = compiler.compile(shader_path, args);
            }

            if (ok)
            {
                if (!quiet)
                {
                    Console.Out.Write("Compiled " + shader_path + " -> " + compiler.output_filename() + "\n");
                }
            }
            else
            {
                Console.Out.Write("FAILED " + shader_path + "\n");
                return 1;
            }
            return 0;
        }
    }
}
