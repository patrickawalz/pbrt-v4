// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/log.h, util/log.cpp (CPU-only: GPU logging omitted)

using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace pbrt
{
    public enum LogLevel { Verbose, Error, Fatal, Invalid }

    public unsafe class FatalException : Exception
    {
        public FatalException(string s) : base(s) { }
    }

    public static unsafe partial class Globals
    {
        public static string ToString(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Verbose: return "VERBOSE";
                case LogLevel.Error: return "ERROR";
                case LogLevel.Fatal: return "FATAL";
                default: return "UNKNOWN";
            }
        }

        public static LogLevel LogLevelFromString(string s)
        {
            if (s == "verbose") return LogLevel.Verbose;
            else if (s == "error") return LogLevel.Error;
            else if (s == "fatal") return LogLevel.Fatal;
            else return LogLevel.Invalid;
        }

        public static void ShutdownLogging() { logging.Flush(); }

        public static void InitLogging(LogLevel level, string logFile, bool logUtilization, bool useGPU)
        {
            if (level == LogLevel.Invalid)
                ErrorExit("Invalid --log-level specified.");
            logging.logLevel = level;
            if (!string.IsNullOrEmpty(logFile))
                logging.logFile = new StreamWriter(logFile);
        }

        public static void Log(LogLevel level, string file, int line, string s)
        {
            string levelString = level == LogLevel.Verbose ? "VERBOSE" : (level == LogLevel.Error ? "ERROR" : "FATAL");
            string msg = $"[ {levelString} ] {Path.GetFileName(file)}:{line}: {s}";
            lock (logging.mutex)
            {
                if (logging.logFile != null)
                    logging.logFile.WriteLine(msg);
                else
                    Console.Error.WriteLine(msg);
            }
        }

        public static void LogFatal(LogLevel level, string file, int line, string s)
        {
            string msg = $"[ FATAL ] {Path.GetFileName(file)}:{line}: {s}";
            lock (logging.mutex)
            {
                Console.Error.WriteLine(msg);
                logging.Flush();
            }
            throw new FatalException(msg);
        }

        public static bool LOG_VERBOSE(string s, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (LogLevel.Verbose >= logging.logLevel) { Log(LogLevel.Verbose, file, line, s); return true; }
            return false;
        }

        public static bool LOG_VERBOSE(string fmt, params object[] args)
        {
            if (LogLevel.Verbose >= logging.logLevel) { Log(LogLevel.Verbose, "", 0, StringPrintf(fmt, args)); return true; }
            return false;
        }

        public static bool LOG_ERROR(string s, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (LogLevel.Error >= logging.logLevel) { Log(LogLevel.Error, file, line, s); return true; }
            return false;
        }

        public static bool LOG_ERROR(string fmt, params object[] args)
        {
            if (LogLevel.Error >= logging.logLevel) { Log(LogLevel.Error, "", 0, StringPrintf(fmt, args)); return true; }
            return false;
        }

        public static void LOG_FATAL(string s, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            LogFatal(LogLevel.Fatal, file, line, s);
        }
    }

    public static unsafe class logging
    {
        public static LogLevel logLevel = LogLevel.Error;
        public static StreamWriter logFile = null;
        public static readonly object mutex = new object();
        public static void Flush() { if (logFile != null) logFile.Flush(); }
    }
}
