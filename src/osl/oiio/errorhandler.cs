// OSL port: the part of OpenImageIO errorhandler.h that the compiler and the shading system use

using System;
using System.Collections.Generic;
using System.IO;

namespace OIIO
{
    public enum ErrorCode
    {
        EH_NO_ERROR = 0, EH_MESSAGE = 0, EH_INFO = 10, EH_WARNING = 20, EH_ERROR = 30, EH_SEVERE = 40, EH_DEBUG = 200
    }

    public class ErrorHandler
    {
        // The messages arrive already formatted; derived handlers decide where they go
        public virtual void handle(int errcode, string msg)
        {
            TextWriter o = errcode >= (int)ErrorCode.EH_WARNING ? Console.Error : Console.Out;
            o.WriteLine(msg);
        }

        public void errorfmt(string msg) { handle((int)ErrorCode.EH_ERROR, msg); }
        public void warningfmt(string msg) { handle((int)ErrorCode.EH_WARNING, msg); }
        public void infofmt(string msg) { handle((int)ErrorCode.EH_INFO, msg); }
        public void messagefmt(string msg) { handle((int)ErrorCode.EH_MESSAGE, msg); }
        public void severefmt(string msg) { handle((int)ErrorCode.EH_SEVERE, msg); }
        public void debugfmt(string msg) { handle((int)ErrorCode.EH_DEBUG, msg); }

        public static ErrorHandler default_handler = new ErrorHandler();
    }

    // An error handler that keeps everything it is given, for tests and for callers that want to inspect the messages
    public class BufferedErrorHandler : ErrorHandler
    {
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public List<string> messages = new List<string>();

        public override void handle(int errcode, string msg)
        {
            if (errcode >= (int)ErrorCode.EH_ERROR)
            {
                errors.Add(msg);
            }
            else if (errcode >= (int)ErrorCode.EH_WARNING)
            {
                warnings.Add(msg);
            }
            else
            {
                messages.Add(msg);
            }
        }
    }
}
