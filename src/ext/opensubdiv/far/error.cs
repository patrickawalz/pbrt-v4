//
//   Copyright 2013 Pixar
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;

namespace OpenSubdiv.Far
{
    /// \brief Severity codes for error reporting
    public enum ErrorType
    {
        FAR_NO_ERROR,               ///< No error. Move along.
        FAR_FATAL_ERROR,            ///< Issue a fatal error and end the program.
        FAR_INTERNAL_CODING_ERROR,  ///< Issue an internal programming error, but continue execution.
        FAR_CODING_ERROR,           ///< Issue a generic programming error, but continue execution.
        FAR_RUNTIME_ERROR           ///< Issue a generic runtime error, but continue execution.
    }

    /// \brief The error callback function type (default is "printf")
    public delegate void ErrorCallbackFunc(ErrorType err, string message);

    /// \brief The warning callback function type (default is "printf")
    public delegate void WarningCallbackFunc(string message);

    /// \brief Holds the free functions of namespace Far for error reporting:
    ///        Errors.Error(type, message), Errors.Warning(message), SetErrorCallback, SetWarningCallback.
    ///        (A C# class cannot declare a method named like itself, so the class is "Errors";
    ///        use "using static OpenSubdiv.Far.Errors;" to call Error()/Warning() unqualified.)
    public static class Errors
    {
        private static ErrorCallbackFunc errorFunc = null;
        private static WarningCallbackFunc warningFunc = null;

        /// \brief Sets the error callback function (default is "printf")
        ///
        /// \note This function is not thread-safe !
        public static void SetErrorCallback(ErrorCallbackFunc func)
        {
            errorFunc = func;
        }

        /// \brief Sets the warning callback function (default is "printf")
        ///
        /// \note This function is not thread-safe !
        public static void SetWarningCallback(WarningCallbackFunc func)
        {
            warningFunc = func;
        }

        //
        //  The default error and warning callbacks eventually belong in the
        //  internal namespace:
        //

        /// \brief Sends an OSD error with a message (internal use only)
        public static void Error(ErrorType err, string message)
        {
            string[] errorTypeLabel = new string[]
            {
                "No Error",
                "Fatal Error",
                "Coding Error (internal)",
                "Coding Error",
                "Error"
            };

            System.Diagnostics.Debug.Assert(err != ErrorType.FAR_NO_ERROR);

            if (errorFunc != null)
            {
                errorFunc(err, message);
            }
            else
            {
                Console.Out.Write(errorTypeLabel[(int)err] + ": " + message + "\n");
            }
        }

        /// \brief Sends an OSD warning message (internal use only)
        public static void Warning(string message)
        {
            if (warningFunc != null)
            {
                warningFunc(message);
            }
            else
            {
                Console.Out.Write("Warning: " + message + "\n");
            }
        }
    }
}
