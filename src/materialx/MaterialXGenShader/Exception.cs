// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/Exception.h.

namespace MaterialX
{
    // ExceptionShaderGenError Definition
    // An exception that is thrown when shader generation fails.
    public class ExceptionShaderGenError : Exception
    {
        public ExceptionShaderGenError(string msg) : base(msg)
        {
        }
    }
}
