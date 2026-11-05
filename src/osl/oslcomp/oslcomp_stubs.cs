// Temporary: the seams for the pieces that are not ported yet (clang-based preprocessing, the bison parser)
using System.Collections.Generic;

namespace OSL
{
    namespace pvt
    {
        public partial class OSLCompilerImpl
        {
            public bool preprocess_buffer(string buffer, string filename, string stdoslpath, List<string> defines, List<string> includepaths, out string result)
            {
                throw new System.NotImplementedException("preprocessor");
            }

            /// Parse the preprocessed source; returns true if there was an ERROR (as in the C++).
            public bool osl_parse_buffer(string buffer)
            {
                throw new System.NotImplementedException("parser");
            }
        }
    }
}
