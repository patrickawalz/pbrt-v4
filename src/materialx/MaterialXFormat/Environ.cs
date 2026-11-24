// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXFormat/Environ.h and
// Environ.cpp.

using System;

namespace MaterialX
{
    // Environment Functions
    public static partial class Mx
    {
        public static string getEnviron(string name)
        {
            string result = Environment.GetEnvironmentVariable(name);
            if (result != null)
            {
                return result;
            }
            return EMPTY_STRING;
        }

        public static bool setEnviron(string name, string value)
        {
            bool succeeded = true;
            try
            {
                Environment.SetEnvironmentVariable(name, value);
            }
            catch (ArgumentException)
            {
                succeeded = false;
            }
            if (OperatingSystem.IsWindows())
            {
                return succeeded;
            }
            // Outside Windows the C++ returns the result of setenv, which is zero on success.
            return !succeeded;
        }

        public static bool removeEnviron(string name)
        {
            try
            {
                Environment.SetEnvironmentVariable(name, null);
            }
            catch (ArgumentException)
            {
                return false;
            }
            return true;
        }
    }
}
