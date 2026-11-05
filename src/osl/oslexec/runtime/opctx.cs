// OSL port: src/liboslexec/opfmt.cpp (osl_gen_ustring), opstring.cpp (osl_format and the deprecated printf family) and opmatrix.cpp
// (osl_transform_triple_nonlinear): the remaining shadeops declared in builtindecl.h that were not yet in the repo.
//
// Differences from the C++:
//   * Strings are ustring hashes in this port, so osl_gen_ustring (which returns a const char* in C++) returns the hash itself, exactly as
//     osl_gen_ustringhash_pod does.
//   * osl_format, osl_printf, osl_error, osl_warning and osl_fprintf are C varargs functions in C++. Varargs cannot be called from emitted IL;
//     the generated code uses the *fmt family (osl_formatfmt, osl_gen_printfmt, ...) instead. They are kept with their fixed prefix signature so
//     lookups by name succeed. The printf family is an empty stub in C++ as well ("deprecated but stubs are needed"). osl_format resolves "%%" in the
//     format string, which is what vsprintf yields for a call without conversions.

using System;
using OIIO;

namespace OSL.pvt
{
    public static unsafe class OslCtxOps
    {
        public static ulong osl_gen_ustring(ulong hash)
        {
            return hash;
        }

        public static void osl_printf(void* oec, void* sg, ulong format_str)
        {
        }

        public static void osl_fprintf(void* oec, void* sg, ulong filename, ulong format_str)
        {
        }

        public static void osl_error(void* oec, void* sg, ulong format_str)
        {
        }

        public static void osl_warning(void* oec, void* sg, ulong format_str)
        {
        }

        public static ulong osl_format(ulong format_str)
        {
            ustring fmt;
            string text = "";
            if (ustring.from_hash(format_str, out fmt))
            {
                text = fmt.@string();
            }
            // No printf engine is available here and no arguments are passed, so only the "%%" escape can be resolved.
            string result = text.Replace("%%", "%");
            return new ustring(result).hash();
        }

        public static int osl_transform_triple_nonlinear(void* oec, void* Pin, int Pin_derivs, void* Pout, int Pout_derivs, ulong from_, ulong to_,
                                                         int vectype)
        {
            float time = ShaderGlobalsAccess.get_time(oec);
            if (RsFallback.rs_transform_points(oec, from_, to_, time, (Vec3*)Pin, (Vec3*)Pout, 1, (VECSEMANTICS)vectype))
            {
                // Renderer had a direct way to transform the points between the two spaces.
                if (Pout_derivs != 0)
                {
                    if (Pin_derivs != 0)
                    {
                        RsFallback.rs_transform_points(oec, from_, to_, time, (Vec3*)Pin + 1, (Vec3*)Pout + 1, 2, VECSEMANTICS.VECTOR);
                    }
                    else
                    {
                        ((Vec3*)Pout)[1] = new Vec3(0.0f, 0.0f, 0.0f);
                        ((Vec3*)Pout)[2] = new Vec3(0.0f, 0.0f, 0.0f);
                    }
                }
                return 1;
            }
            // Renderer couldn't or wouldn't transform directly.
            return ILGlue.osl_transform_triple(oec, Pin, Pin_derivs, Pout, Pout_derivs, from_, to_, vectype);
        }
    }
}
