// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/error.h, util/error.cpp
// printf-style format strings are replaced by C# interpolated strings at call sites.

using System;

namespace pbrt
{
    public unsafe class FileLoc
    {
        public FileLoc() { }
        public FileLoc(string filename) { this.filename = filename; }
        public override string ToString() { return $"{filename}:{line}:{column}"; }

        public string filename;
        public int line = 1, column = 0;
    }

    public static unsafe partial class pbrt
    {
        private static bool hasErrored = false;
        private static bool suppressErrorMessages = false;
        private static readonly object errorMutex = new object();

        public static void SuppressErrorMessages() { suppressErrorMessages = true; }

        private static string FormatLocation(FileLoc loc, string prefix, string message)
        {
            string s = prefix;
            if (loc != null) s += $"{loc}: ";
            return s + message;
        }

        public static void Warning(FileLoc loc, string message)
        {
            if (suppressErrorMessages) return;
            lock (errorMutex) Console.Error.WriteLine(FormatLocation(loc, "Warning: ", message));
        }

        public static void Warning(string message) { Warning((FileLoc)null, message); }

        public static void Error(FileLoc loc, string message)
        {
            hasErrored = true;
            if (suppressErrorMessages) return;
            lock (errorMutex) Console.Error.WriteLine(FormatLocation(loc, "Error: ", message));
        }

        public static void Error(string message) { Error((FileLoc)null, message); }

        public static void ErrorExit(FileLoc loc, string message)
        {
            lock (errorMutex) Console.Error.WriteLine(FormatLocation(loc, "Error: ", message));
            throw new FatalException(message);
        }

        public static void ErrorExit(string message) { ErrorExit((FileLoc)null, message); }
    }
}
