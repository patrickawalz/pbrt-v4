// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Exception.h.

namespace MaterialX
{
    // Exception Definition
    public class Exception : System.Exception
    {
        // Exception Public Methods
        public Exception(string msg) : base(msg)
        {
        }

        public string what()
        {
            return Message;
        }
    }

    // ExceptionTypeError Definition
    public class ExceptionTypeError : Exception
    {
        public ExceptionTypeError(string msg) : base(msg)
        {
        }
    }
}
