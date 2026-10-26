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
global using static testing.Gtest;

// pbrt_test.cpp
using System;
using System.Collections.Generic;
using System.Reflection;
using pbrt;

public static class PbrtTestProgram
{
    private static void usage(string msg = "")
    {
        if (msg.Length != 0)
            Console.Error.Write(StringPrintf("pbrt_test: %s\n\n", msg));

        Console.Error.Write(@"pbrt_test arguments:
  --list-tests                List all tests.
  --log-level <level>         Log messages at or above this level, where <level>
                              is ""verbose"", ""error"", or ""fatal"". Default: ""error"".
  --nthreads <num>            Use specified number of threads for rendering.
  --test-filter <regexp>      Regular expression of test names to run.
  --vlog-level <n>            Set VLOG verbosity. (Default: 0, disabled.)
");

        Environment.Exit(msg.Length == 0 ? 0 : 1);
    }

    public static int Main(string[] argv)
    {
        PBRTOptions opt = new PBRTOptions();
        opt.quiet = true;
        string logLevel = "error";
        string testFilter = "";
        bool listTests = false;

        List<string> args = GetCommandLineArguments(argv);

        // Process command-line arguments
        for (int iter = 0; iter < args.Count; ++iter)
        {
            Action<string> onError = (string err) =>
            {
                usage(err);
                Environment.Exit(1);
            };

            if (ParseArg(args, ref iter, args.Count, "list-tests", ref listTests, onError) ||
                ParseArg(args, ref iter, args.Count, "log-level", ref logLevel, onError) ||
                ParseArg(args, ref iter, args.Count, "nthreads", ref opt.nThreads, onError) ||
                ParseArg(args, ref iter, args.Count, "gtest-filter", ref testFilter, onError) ||
                ParseArg(args, ref iter, args.Count, "test-filter", ref testFilter, onError))
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

        opt.logLevel = LogLevelFromString(logLevel);

        InitPBRT(opt);

        int ret = testing.Gtest.RunAllTests(Assembly.GetExecutingAssembly(), testFilter.Length != 0 ? testFilter : null, listTests);

        CleanupPBRT();

        return ret;
    }
}
