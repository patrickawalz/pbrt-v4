// OSL port: the parts of llvm_ops.cpp, opfmt.cpp, optexture.cpp, pointcloud.cpp, opmatrix.cpp, opstring.cpp and shadingsys.cpp that the generated code calls
// and that are not elsewhere in the managed runtime: the context dependent shadeops (they receive the ShaderGlobals as their "opaque execution context"),
// the texture / trace / noise option setters on native option blocks, the printf family, range and NaN checks, and the glue of closure prepare / setup.
//
// The option blocks (TextureOpt, TraceOpt) are plain memory in the C++; the managed TextureOpt holds a ustring and an array, so the generated code builds a
// blittable NativeTextureOpt, which the lookups convert to the managed form.

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using OIIO;
using OSL;

namespace OSL.pvt
{
    /// The block the generated code fills in for the optional arguments of texture(), texture3d() and environment()
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NativeTextureOpt
    {
        public int firstchannel;
        public int subimage;
        public ulong subimagename;
        public int swrap;
        public int twrap;
        public int rwrap;
        public int mipmode;
        public int interpmode;
        public int anisotropic;
        public int conservative_filter;
        public float sblur;
        public float tblur;
        public float rblur;
        public float swidth;
        public float twidth;
        public float rwidth;
        public float fill;
        public float* missingcolor;
        public float time;
        public float rnd;
        public int samples;
        public int envlayout;

        public static NativeTextureOpt create_default()
        {
            TextureOpt d = TextureOpt.make_default();
            NativeTextureOpt o = new NativeTextureOpt();
            o.firstchannel = d.firstchannel;
            o.subimage = d.subimage;
            o.subimagename = d.subimagename.hash();
            o.swrap = d.swrap;
            o.twrap = d.twrap;
            o.rwrap = d.rwrap;
            o.mipmode = d.mipmode;
            o.interpmode = d.interpmode;
            o.anisotropic = d.anisotropic;
            o.conservative_filter = d.conservative_filter;
            o.sblur = d.sblur;
            o.tblur = d.tblur;
            o.rblur = d.rblur;
            o.swidth = d.swidth;
            o.twidth = d.twidth;
            o.rwidth = d.rwidth;
            o.fill = d.fill;
            o.missingcolor = null;
            o.time = d.time;
            o.rnd = d.rnd;
            o.samples = d.samples;
            o.envlayout = 0;
            return o;
        }

        public TextureOpt to_managed()
        {
            TextureOpt d = new TextureOpt();
            d.firstchannel = firstchannel;
            d.subimage = subimage;
            d.subimagename = OslStrings.from_hash(subimagename);
            d.swrap = swrap;
            d.twrap = twrap;
            d.rwrap = rwrap;
            d.mipmode = mipmode;
            d.interpmode = interpmode;
            d.anisotropic = anisotropic;
            d.conservative_filter = conservative_filter;
            d.sblur = sblur;
            d.tblur = tblur;
            d.rblur = rblur;
            d.swidth = swidth;
            d.twidth = twidth;
            d.rwidth = rwidth;
            d.fill = fill;
            if (missingcolor != null)
            {
                d.missingcolor = new float[4];
                for (int i = 0; i < 4; ++i)
                {
                    d.missingcolor[i] = missingcolor[i];
                }
            }
            d.time = time;
            d.rnd = rnd;
            d.samples = samples;
            d.envlayout = envlayout;
            return d;
        }
    }

    public static unsafe class ILGlue
    {
        private static ShadingContext ctx_of(void* sg)
        {
            return ShaderGlobalsAccess.get_context((ShaderGlobals*)sg);
        }

        private static ustring u_from(ulong h)
        {
            return OslStrings.from_hash(h);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Texture option decoding (OIIO::TextureOpt::decode_wrapmode, OSL tex_interp_to_code)
        // ------------------------------------------------------------------------------------------------------------------------------------

        public static int decode_wrapmode(ustring name)
        {
            string s = name.@string();
            switch (s)
            {
                case "default": return TextureOpt.WrapDefault;
                case "black": return TextureOpt.WrapBlack;
                case "clamp": return TextureOpt.WrapClamp;
                case "periodic": return TextureOpt.WrapPeriodic;
                case "mirror": return TextureOpt.WrapMirror;
                case "periodic_pow2": return TextureOpt.WrapPeriodicPow2;
                case "periodic_sharedborder": return TextureOpt.WrapPeriodicSharedBorder;
                default: return TextureOpt.WrapDefault;
            }
        }

        public static int tex_interp_to_code(ustring modename)
        {
            string s = modename.@string();
            switch (s)
            {
                case "closest": return TextureOpt.InterpClosest;
                case "linear": return TextureOpt.InterpBilinear;
                case "cubic": return TextureOpt.InterpBicubic;
                case "smartcubic": return TextureOpt.InterpSmartBicubic;
                default: return -1;
            }
        }

        public static int osl_texture_decode_wrapmode(ulong name_)
        {
            return decode_wrapmode(u_from(name_));
        }

        public static int osl_texture_decode_interpmode(ulong name_)
        {
            return tex_interp_to_code(u_from(name_));
        }

        public static void osl_init_texture_options(void* oec, void* opt)
        {
            *(NativeTextureOpt*)opt = NativeTextureOpt.create_default();
        }

        public static void osl_texture_set_firstchannel(void* opt, int x)
        {
            ((NativeTextureOpt*)opt)->firstchannel = x;
        }

        public static void osl_texture_set_swrap(void* opt, ulong x_)
        {
            ((NativeTextureOpt*)opt)->swrap = decode_wrapmode(u_from(x_));
        }

        public static void osl_texture_set_twrap(void* opt, ulong x_)
        {
            ((NativeTextureOpt*)opt)->twrap = decode_wrapmode(u_from(x_));
        }

        public static void osl_texture_set_rwrap(void* opt, ulong x_)
        {
            ((NativeTextureOpt*)opt)->rwrap = decode_wrapmode(u_from(x_));
        }

        public static void osl_texture_set_stwrap(void* opt, ulong x_)
        {
            int code = decode_wrapmode(u_from(x_));
            ((NativeTextureOpt*)opt)->swrap = code;
            ((NativeTextureOpt*)opt)->twrap = code;
        }

        public static void osl_texture_set_swrap_code(void* opt, int mode)
        {
            ((NativeTextureOpt*)opt)->swrap = mode;
        }

        public static void osl_texture_set_twrap_code(void* opt, int mode)
        {
            ((NativeTextureOpt*)opt)->twrap = mode;
        }

        public static void osl_texture_set_rwrap_code(void* opt, int mode)
        {
            ((NativeTextureOpt*)opt)->rwrap = mode;
        }

        public static void osl_texture_set_stwrap_code(void* opt, int mode)
        {
            ((NativeTextureOpt*)opt)->swrap = mode;
            ((NativeTextureOpt*)opt)->twrap = mode;
        }

        public static void osl_texture_set_sblur(void* opt, float x)
        {
            ((NativeTextureOpt*)opt)->sblur = x;
        }

        public static void osl_texture_set_tblur(void* opt, float x)
        {
            ((NativeTextureOpt*)opt)->tblur = x;
        }

        public static void osl_texture_set_rblur(void* opt, float x)
        {
            ((NativeTextureOpt*)opt)->rblur = x;
        }

        public static void osl_texture_set_stblur(void* opt, float x)
        {
            ((NativeTextureOpt*)opt)->sblur = x;
            ((NativeTextureOpt*)opt)->tblur = x;
        }

        public static void osl_texture_set_swidth(void* opt, float x)
        {
            ((NativeTextureOpt*)opt)->swidth = x;
        }

        public static void osl_texture_set_twidth(void* opt, float x)
        {
            ((NativeTextureOpt*)opt)->twidth = x;
        }

        public static void osl_texture_set_rwidth(void* opt, float x)
        {
            ((NativeTextureOpt*)opt)->rwidth = x;
        }

        public static void osl_texture_set_stwidth(void* opt, float x)
        {
            ((NativeTextureOpt*)opt)->swidth = x;
            ((NativeTextureOpt*)opt)->twidth = x;
        }

        public static void osl_texture_set_fill(void* opt, float x)
        {
            ((NativeTextureOpt*)opt)->fill = x;
        }

        public static void osl_texture_set_time(void* opt, float x)
        {
        }

        public static void osl_texture_set_interp(void* opt, ulong modename_)
        {
            int mode = tex_interp_to_code(u_from(modename_));
            if (mode >= 0)
            {
                ((NativeTextureOpt*)opt)->interpmode = mode;
            }
        }

        public static void osl_texture_set_interp_code(void* opt, int mode)
        {
            ((NativeTextureOpt*)opt)->interpmode = mode;
        }

        public static void osl_texture_set_subimage(void* opt, int subimage)
        {
            ((NativeTextureOpt*)opt)->subimage = subimage;
        }

        public static void osl_texture_set_subimagename(void* opt, ulong subimagename_)
        {
            ((NativeTextureOpt*)opt)->subimagename = subimagename_;
        }

        public static void osl_texture_set_missingcolor_arena(void* opt, void* missing)
        {
            ((NativeTextureOpt*)opt)->missingcolor = (float*)missing;
        }

        public static void osl_texture_set_missingcolor_alpha(void* opt, int alphaindex, float missingalpha)
        {
            float* m = ((NativeTextureOpt*)opt)->missingcolor;
            if (m != null)
            {
                m[alphaindex] = missingalpha;
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Texture lookups
        // ------------------------------------------------------------------------------------------------------------------------------------

        private static TextureHandle handle_of(void* handle)
        {
            if (handle == null)
            {
                return null;
            }
            return GCHandle.FromIntPtr((IntPtr)handle).Target as TextureHandle;
        }

        public static int osl_texture(void* oec, ulong name_, void* handle, void* opt_, float s, float t, float dsdx, float dtdx, float dsdy, float dtdy,
                                      int chans, void* result_, void* dresultdx_, void* dresultdy_, void* alpha_, void* dalphadx_, void* dalphady_,
                                      void* errormessage_)
        {
            NativeTextureOpt* nopt = (NativeTextureOpt*)opt_;
            float* result = (float*)result_;
            float* dresultdx = (float*)dresultdx_;
            float* dresultdy = (float*)dresultdy_;
            float* alpha = (float*)alpha_;
            float* dalphadx = (float*)dalphadx_;
            float* dalphady = (float*)dalphady_;
            ulong* errormessage = (ulong*)errormessage_;
            bool derivs = (dresultdx != null || dalphadx != null);
            ShaderGlobals* sg = (ShaderGlobals*)oec;
            float* result_simd = stackalloc float[4];
            float* dresultds_simd = stackalloc float[4];
            float* dresultdt_simd = stackalloc float[4];
            for (int i = 0; i < 4; ++i)
            {
                result_simd[i] = 0.0f;
                dresultds_simd[i] = 0.0f;
                dresultdt_simd[i] = 0.0f;
            }
            ulong em = 0;
            TextureOpt opt = nopt->to_managed();
            bool ok = RsFallback.rs_texture(oec, name_, handle_of(handle), ctx_of(oec).texture_thread_info(), ref opt, s, t, dsdx, dtdx, dsdy, dtdy, 4,
                                            result_simd, derivs ? dresultds_simd : null, derivs ? dresultdt_simd : null,
                                            errormessage != null ? &em : null);
            for (int i = 0; i < chans; ++i)
            {
                result[i] = result_simd[i];
            }
            if (alpha != null)
            {
                alpha[0] = result_simd[chans];
            }
            if (derivs)
            {
                float* dresultdx_simd = stackalloc float[4];
                float* dresultdy_simd = stackalloc float[4];
                for (int i = 0; i < 4; ++i)
                {
                    dresultdx_simd[i] = dresultds_simd[i] * dsdx + dresultdt_simd[i] * dtdx;
                    dresultdy_simd[i] = dresultds_simd[i] * dsdy + dresultdt_simd[i] * dtdy;
                }
                if (dresultdx != null)
                {
                    for (int i = 0; i < chans; ++i)
                    {
                        dresultdx[i] = dresultdx_simd[i];
                    }
                    for (int i = 0; i < chans; ++i)
                    {
                        dresultdy[i] = dresultdy_simd[i];
                    }
                }
                if (dalphadx != null)
                {
                    dalphadx[0] = dresultdx_simd[chans];
                    dalphady[0] = dresultdy_simd[chans];
                }
            }
            if (errormessage != null)
            {
                *errormessage = em;
            }
            return ok ? 1 : 0;
        }

        public static int osl_texture3d(void* oec, ulong name_, void* handle, void* opt_, void* P_, void* dPdx_, void* dPdy_, void* dPdz_, int chans,
                                        void* result_, void* dresultdx_, void* dresultdy_, void* alpha_, void* dalphadx_, void* dalphady_,
                                        void* errormessage_)
        {
            Vec3 P = *(Vec3*)P_;
            Vec3 dPdx = *(Vec3*)dPdx_;
            Vec3 dPdy = *(Vec3*)dPdy_;
            Vec3 dPdz = new Vec3(0.0f, 0.0f, 0.0f);
            if (dPdz_ != null)
            {
                dPdz = *(Vec3*)dPdz_;
            }
            NativeTextureOpt* nopt = (NativeTextureOpt*)opt_;
            float* result = (float*)result_;
            float* dresultdx = (float*)dresultdx_;
            float* dresultdy = (float*)dresultdy_;
            float* alpha = (float*)alpha_;
            float* dalphadx = (float*)dalphadx_;
            float* dalphady = (float*)dalphady_;
            ulong* errormessage = (ulong*)errormessage_;
            bool derivs = (dresultdx != null || dalphadx != null);
            float* result_simd = stackalloc float[4];
            float* dresultds_simd = stackalloc float[4];
            float* dresultdt_simd = stackalloc float[4];
            float* dresultdr_simd = stackalloc float[4];
            for (int i = 0; i < 4; ++i)
            {
                result_simd[i] = 0.0f;
                dresultds_simd[i] = 0.0f;
                dresultdt_simd[i] = 0.0f;
                dresultdr_simd[i] = 0.0f;
            }
            ulong em = 0;
            TextureOpt opt = nopt->to_managed();
            bool ok = RsFallback.rs_texture3d(oec, name_, handle_of(handle), ctx_of(oec).texture_thread_info(), ref opt, P, dPdx, dPdy, dPdz, 4,
                                              result_simd, derivs ? dresultds_simd : null, derivs ? dresultdt_simd : null,
                                              derivs ? dresultdr_simd : null, errormessage != null ? &em : null);
            for (int i = 0; i < chans; ++i)
            {
                result[i] = result_simd[i];
            }
            if (alpha != null)
            {
                alpha[0] = result_simd[chans];
            }
            if (derivs)
            {
                float* dresultdx_simd = stackalloc float[4];
                float* dresultdy_simd = stackalloc float[4];
                for (int i = 0; i < 4; ++i)
                {
                    dresultdx_simd[i] = dresultds_simd[i] * dPdx.x + dresultdt_simd[i] * dPdx.y + dresultdr_simd[i] * dPdx.z;
                    dresultdy_simd[i] = dresultds_simd[i] * dPdy.x + dresultdt_simd[i] * dPdy.y + dresultdr_simd[i] * dPdy.z;
                }
                if (dresultdx != null)
                {
                    for (int i = 0; i < chans; ++i)
                    {
                        dresultdx[i] = dresultdx_simd[i];
                    }
                    for (int i = 0; i < chans; ++i)
                    {
                        dresultdy[i] = dresultdy_simd[i];
                    }
                }
                if (dalphadx != null)
                {
                    dalphadx[0] = dresultdx_simd[chans];
                    dalphady[0] = dresultdy_simd[chans];
                }
            }
            if (errormessage != null)
            {
                *errormessage = em;
            }
            return ok ? 1 : 0;
        }

        public static int osl_environment(void* oec, ulong name_, void* handle, void* opt_, void* R_, void* dRdx_, void* dRdy_, int chans, void* result_,
                                          void* dresultdx_, void* dresultdy_, void* alpha_, void* dalphadx_, void* dalphady_, void* errormessage_)
        {
            Vec3 R = *(Vec3*)R_;
            Vec3 dRdx = *(Vec3*)dRdx_;
            Vec3 dRdy = *(Vec3*)dRdy_;
            NativeTextureOpt* nopt = (NativeTextureOpt*)opt_;
            float* result = (float*)result_;
            float* dresultdx = (float*)dresultdx_;
            float* dresultdy = (float*)dresultdy_;
            float* alpha = (float*)alpha_;
            float* dalphadx = (float*)dalphadx_;
            float* dalphady = (float*)dalphady_;
            ulong* errormessage = (ulong*)errormessage_;
            float* local_result = stackalloc float[4];
            for (int i = 0; i < 4; ++i)
            {
                local_result[i] = 0.0f;
            }
            ulong em = 0;
            TextureOpt opt = nopt->to_managed();
            bool ok = RsFallback.rs_environment(oec, name_, handle_of(handle), ctx_of(oec).texture_thread_info(), ref opt, R, dRdx, dRdy, 4, local_result,
                                                null, null, errormessage != null ? &em : null);
            for (int i = 0; i < chans; ++i)
            {
                result[i] = local_result[i];
            }
            if (dresultdx != null)
            {
                for (int i = 0; i < chans; ++i)
                {
                    dresultdx[i] = 0.0f;
                }
                for (int i = 0; i < chans; ++i)
                {
                    dresultdy[i] = 0.0f;
                }
            }
            if (alpha != null)
            {
                alpha[0] = local_result[chans];
                if (dalphadx != null)
                {
                    dalphadx[0] = 0.0f;
                }
                if (dalphady != null)
                {
                    dalphady[0] = 0.0f;
                }
            }
            if (errormessage != null)
            {
                *errormessage = em;
            }
            return ok ? 1 : 0;
        }

        public static int osl_get_textureinfo(void* oec, ulong name_, void* handle_, ulong dataname_, int type, int arraylen, int aggregate, void* data,
                                              void* errormessage_)
        {
            TypeDesc typedesc = new TypeDesc();
            typedesc.basetype = (byte)type;
            typedesc.arraylen = arraylen;
            typedesc.aggregate = (byte)aggregate;
            ulong* errormessage = (ulong*)errormessage_;
            ulong em = 0;
            bool ok = RsFallback.rs_get_texture_info(oec, name_, handle_of(handle_), ctx_of(oec).texture_thread_info(), 0, dataname_, typedesc, data,
                                                     errormessage != null ? &em : null);
            if (errormessage != null)
            {
                *errormessage = ok ? 0UL : em;
            }
            return ok ? 1 : 0;
        }

        public static int osl_get_textureinfo_st(void* oec, ulong name_, void* handle_, float s, float t, ulong dataname_, int type, int arraylen,
                                                 int aggregate, void* data, void* errormessage_)
        {
            TypeDesc typedesc = new TypeDesc();
            typedesc.basetype = (byte)type;
            typedesc.arraylen = arraylen;
            typedesc.aggregate = (byte)aggregate;
            ulong* errormessage = (ulong*)errormessage_;
            ulong em = 0;
            bool ok = RsFallback.rs_get_texture_info_st(oec, name_, handle_of(handle_), s, t, ctx_of(oec).texture_thread_info(), 0, dataname_, typedesc,
                                                        data, errormessage != null ? &em : null);
            if (errormessage != null)
            {
                *errormessage = ok ? 0UL : em;
            }
            return ok ? 1 : 0;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Trace
        // ------------------------------------------------------------------------------------------------------------------------------------

        public static void osl_init_trace_options(void* oec, void* opt)
        {
            *(TraceOpt*)opt = TraceOpt.make_default();
        }

        public static void osl_trace_set_mindist(void* opt, float x)
        {
            ((TraceOpt*)opt)->mindist = x;
        }

        public static void osl_trace_set_maxdist(void* opt, float x)
        {
            ((TraceOpt*)opt)->maxdist = x;
        }

        public static void osl_trace_set_shade(void* opt, int x)
        {
            ((TraceOpt*)opt)->shade = x != 0;
        }

        public static void osl_trace_set_traceset(void* opt, ulong x)
        {
            ((TraceOpt*)opt)->traceset = x;
        }

        public static int osl_trace(void* oec, void* opt_, void* Pos_, void* dPosdx_, void* dPosdy_, void* Dir_, void* dDirdx_, void* dDirdy_)
        {
            Vec3 Zero = new Vec3(0.0f, 0.0f, 0.0f);
            Vec3 Pos = *(Vec3*)Pos_;
            Vec3 dPosdx = dPosdx_ != null ? *(Vec3*)dPosdx_ : Zero;
            Vec3 dPosdy = dPosdy_ != null ? *(Vec3*)dPosdy_ : Zero;
            Vec3 Dir = *(Vec3*)Dir_;
            Vec3 dDirdx = dDirdx_ != null ? *(Vec3*)dDirdx_ : Zero;
            Vec3 dDirdy = dDirdy_ != null ? *(Vec3*)dDirdy_ : Zero;
            TraceOpt opt = *(TraceOpt*)opt_;
            return RsFallback.rs_trace(oec, ref opt, Pos, dPosdx, dPosdy, Dir, dDirdx, dDirdy) ? 1 : 0;
        }

        public static int osl_trace_get(void* oec, ulong name_, long type_, void* val, int derivatives)
        {
            return RsFallback.rs_trace_get(oec, name_, TypeDescBits.from_long(type_), val, derivatives != 0) ? 1 : 0;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Point clouds
        // ------------------------------------------------------------------------------------------------------------------------------------

        public static int osl_pointcloud_search(void* oec, ulong filename_, void* center, float radius, int max_points, int sort, void* out_indices_,
                                                void* out_distances_, int derivs_offset, int nattrs, void* names_, void* types_, void* values_)
        {
            ShadingSystemImpl shadingsys = ctx_of(oec).shadingsys();
            if (shadingsys.no_pointcloud())
            {
                return 0;
            }
            int* out_indices = (int*)out_indices_;
            float* out_distances = (float*)out_distances_;
            int count = RsFallback.rs_pointcloud_search(oec, filename_, (Vec3*)center, radius, max_points, sort != 0, out_indices, out_distances,
                                                        derivs_offset);
            ulong* names = (ulong*)names_;
            TypeDesc* types = (TypeDesc*)types_;
            void** values = (void**)values_;
            for (int i = 0; i < nattrs; i++)
            {
                RsFallback.rs_pointcloud_get(oec, filename_, out_indices, count, names[i], types[i], values[i]);
            }
            shadingsys.pointcloud_stats(1, 0, count);
            return count;
        }

        public static int osl_pointcloud_get(void* oec, ulong filename_, void* in_indices, int count, ulong attr_name_, long attr_type, void* out_data)
        {
            ShadingSystemImpl shadingsys = ctx_of(oec).shadingsys();
            if (shadingsys.no_pointcloud())
            {
                return 0;
            }
            shadingsys.pointcloud_stats(0, 1, 0);
            return RsFallback.rs_pointcloud_get(oec, filename_, (int*)in_indices, count, attr_name_, TypeDescBits.from_long(attr_type), out_data);
        }

        public static void osl_pointcloud_write_helper(void* names_, void* types_, void* values_, int index, ulong name_, long type, void* val)
        {
            ulong* names = (ulong*)names_;
            TypeDesc* types = (TypeDesc*)types_;
            void** values = (void**)values_;
            names[index] = name_;
            types[index] = TypeDescBits.from_long(type);
            values[index] = val;
        }

        public static int osl_pointcloud_write(void* oec, ulong filename_, void* pos_, int nattribs, void* names_, void* types_, void* values_)
        {
            ShadingSystemImpl shadingsys = ctx_of(oec).shadingsys();
            if (shadingsys.no_pointcloud())
            {
                return 0;
            }
            shadingsys.pointcloud_stats(0, 0, 0, 1);
            return RsFallback.rs_pointcloud_write(oec, filename_, *(Vec3*)pos_, nattribs, (ulong*)names_, (TypeDesc*)types_, (void**)values_) ? 1 : 0;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // printf family and string glue (opfmt.cpp)
        // ------------------------------------------------------------------------------------------------------------------------------------

        public static void osl_gen_errorfmt(void* oec, ulong fmt_specification, int arg_count, void* arg_types, uint arg_values_size, byte* arg_values)
        {
            RsFallback.rs_errorfmt(oec, fmt_specification, arg_count, (EncodedType*)arg_types, arg_values_size, arg_values);
        }

        public static void osl_gen_warningfmt(void* oec, ulong fmt_specification, int arg_count, void* arg_types, uint arg_values_size, byte* arg_values)
        {
            RsFallback.rs_warningfmt(oec, fmt_specification, arg_count, (EncodedType*)arg_types, arg_values_size, arg_values);
        }

        public static void osl_gen_printfmt(void* oec, ulong fmt_specification, int arg_count, void* arg_types, uint arg_values_size, byte* arg_values)
        {
            RsFallback.rs_printfmt(oec, fmt_specification, arg_count, (EncodedType*)arg_types, arg_values_size, arg_values);
        }

        public static void osl_gen_filefmt(void* oec, ulong filename_hash, ulong fmt_specification, int arg_count, void* arg_types, uint arg_values_size,
                                           byte* arg_values)
        {
            RsFallback.rs_filefmt(oec, filename_hash, fmt_specification, arg_count, (EncodedType*)arg_types, arg_values_size, arg_values);
        }

        public static ulong osl_formatfmt(void* oec, ulong fmt_specification, int arg_count, void* arg_types, uint arg_values_size, byte* arg_values)
        {
            string decoded_str = Journal.decode_message(fmt_specification, arg_count, (EncodedType*)arg_types, arg_values);
            return new ustring(decoded_str).hash();
        }

        public static ulong osl_gen_ustringhash_pod(ulong s)
        {
            return s;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Checks and bookkeeping (shadingsys.cpp)
        // ------------------------------------------------------------------------------------------------------------------------------------

        public static void osl_naninf_check(int ncomps, void* vals_, int has_derivs, void* sg, ulong sourcefile_, int sourceline, ulong symbolname_,
                                            int firstcheck, int nchecks, ulong opname_)
        {
            float* vals = (float*)vals_;
            for (int d = 0; d < (has_derivs != 0 ? 3 : 1); ++d)
            {
                for (int c = firstcheck, e = c + nchecks; c < e; ++c)
                {
                    int i = d * ncomps + c;
                    if (!float.IsFinite(vals[i]))
                    {
                        ShaderGlobalsAccess.errorfmt((ShaderGlobals*)sg, "Detected {} value in {}{} at {}:{} (op {})", vals[i], d > 0 ? "the derivatives of " : "", u_from(symbolname_),
                                            u_from(sourcefile_), sourceline, u_from(opname_));
                        return;
                    }
                }
            }
        }

        public static void osl_uninit_check(long typedesc_, void* vals_, void* sg, ulong sourcefile_, int sourceline, ulong groupname_, int layer,
                                            ulong layername_, ulong shadername_, int opnum, ulong opname_, int argnum, ulong symbolname_, int firstcheck,
                                            int nchecks)
        {
            TypeDesc typedesc = TypeDescBits.from_long(typedesc_);
            bool uninit = false;
            if (typedesc.basetype == TypeDesc.FLOAT)
            {
                float* vals = (float*)vals_;
                for (int c = firstcheck, e = firstcheck + nchecks; c < e; ++c)
                {
                    if (!float.IsFinite(vals[c]))
                    {
                        uninit = true;
                        vals[c] = 0;
                    }
                }
            }
            if (typedesc.basetype == TypeDesc.INT)
            {
                int* vals = (int*)vals_;
                for (int c = firstcheck, e = firstcheck + nchecks; c < e; ++c)
                {
                    if (vals[c] == int.MinValue)
                    {
                        uninit = true;
                        vals[c] = 0;
                    }
                }
            }
            if (typedesc.basetype == TypeDesc.STRING)
            {
                ulong* vals = (ulong*)vals_;
                ulong uninitialized = new ustring("!!!uninitialized!!!").hash();
                for (int c = firstcheck, e = firstcheck + nchecks; c < e; ++c)
                {
                    if (vals[c] == uninitialized)
                    {
                        uninit = true;
                        vals[c] = new ustring("").hash();
                    }
                }
            }
            if (uninit)
            {
                ustring groupname = u_from(groupname_);
                ustring layername = u_from(layername_);
                ShaderGlobalsAccess.errorfmt((ShaderGlobals*)sg, "Detected possible use of uninitialized value in {} {} at {}:{} (group {}, layer {} {}, shader {}, op {} '{}', arg {})",
                                    typedesc, u_from(symbolname_), u_from(sourcefile_), sourceline,
                                    groupname.empty() ? new ustring("<unnamed group>") : groupname, layer,
                                    layername.empty() ? new ustring("<unnamed layer>") : layername, u_from(shadername_), opnum, u_from(opname_), argnum);
            }
        }

        public static int osl_range_check_err(int indexvalue, int length, ulong symname_, void* sg, ulong sourcefile_, int sourceline, ulong groupname_,
                                              int layer, ulong layername_, ulong shadername_)
        {
            if (indexvalue < 0 || indexvalue >= length)
            {
                ustring groupname = u_from(groupname_);
                ustring layername = u_from(layername_);
                ShaderGlobalsAccess.errorfmt((ShaderGlobals*)sg, "Index [{}] out of range {}[0..{}]: {}:{} (group {}, layer {} {}, shader {})", indexvalue, u_from(symname_), length - 1,
                                    u_from(sourcefile_), sourceline, groupname.empty() ? new ustring("<unnamed group>") : groupname, layer,
                                    layername.empty() ? new ustring("<unnamed layer>") : layername, u_from(shadername_));
                if (indexvalue >= length)
                {
                    indexvalue = length - 1;
                }
                else
                {
                    indexvalue = 0;
                }
            }
            return indexvalue;
        }

        public static int osl_range_check(int indexvalue, int length, ulong symname_, void* sg, ulong sourcefile_, int sourceline, ulong groupname_,
                                          int layer, ulong layername_, ulong shadername_)
        {
            if (indexvalue < 0 || indexvalue >= length)
            {
                indexvalue = osl_range_check_err(indexvalue, length, symname_, sg, sourcefile_, sourceline, groupname_, layer, layername_, shadername_);
            }
            return indexvalue;
        }

        public static int osl_raytype_bit(void* oec, int bit)
        {
            return (ShaderGlobalsAccess.get_raytype(oec) & bit) != 0 ? 1 : 0;
        }

        public static int osl_raytype_name(void* sg_, ulong name_)
        {
            ShaderGlobals* sg = (ShaderGlobals*)sg_;
            int bit = ctx_of(sg_).shadingsys().raytype_bit(u_from(name_));
            return (sg->raytype & bit) != 0 ? 1 : 0;
        }

        public static int osl_get_attribute(void* sg_, int dest_derivs, ulong obj_name_, ulong attr_name_, int array_lookup, int index, long attr_type,
                                            void* attr_dest)
        {
            ShaderGlobals* sg = (ShaderGlobals*)sg_;
            return ctx_of(sg_).osl_get_attribute(sg, sg->objdata, dest_derivs, obj_name_, attr_name_, array_lookup, index,
                                                 TypeDescBits.from_long(attr_type), attr_dest)
                       ? 1
                       : 0;
        }

        public static int osl_bind_interpolated_param(void* sg_, ulong name_, long type, int userdata_has_derivs, void* userdata_data,
                                                      int symbol_has_derivs, void* symbol_data, int symbol_data_size, void* userdata_initialized,
                                                      int userdata_index)
        {
            byte* init = (byte*)userdata_initialized;
            byte status = *init;
            if (status == 0)
            {
                ShaderGlobals* sg = (ShaderGlobals*)sg_;
                bool ok = ShaderGlobalsAccess.get_renderer(sg).get_userdata(userdata_has_derivs != 0, name_, TypeDescBits.from_long(type), sg,
                                                                            userdata_data);
                status = (byte)(1 + (ok ? 1 : 0));
                *init = status;
                ctx_of(sg_).incr_get_userdata_calls();
            }
            if (status == 2)
            {
                TypeDesc td = TypeDescBits.from_long(type);
                int udata_size = (userdata_has_derivs != 0 ? 3 : 1) * (int)td.size();
                int n = Math.Min(symbol_data_size, udata_size);
                Buffer.MemoryCopy(userdata_data, symbol_data, n, n);
                if (symbol_data_size > udata_size)
                {
                    new Span<byte>((byte*)symbol_data + udata_size, symbol_data_size - udata_size).Clear();
                }
                return 1;
            }
            return 0;
        }

        public static void osl_incr_get_userdata_calls(void* sg_)
        {
            ctx_of(sg_).incr_get_userdata_calls();
        }

        public static void osl_count_noise(void* sg_)
        {
            ctx_of(sg_).shadingsys().count_noise();
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Derivative based shadeops (llvm_ops.cpp)
        // ------------------------------------------------------------------------------------------------------------------------------------

        private static Vec3 calculatenormal(void* P_, bool flipHandedness)
        {
            Vec3* p = (Vec3*)P_;
            Vec3 dx = p[1];
            Vec3 dy = p[2];
            if (flipHandedness)
            {
                return cross(dy, dx);
            }
            return cross(dx, dy);
        }

        private static Vec3 cross(Vec3 a, Vec3 b)
        {
            return new Vec3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        public static void osl_calculatenormal(void* out_, void* ec, void* P_)
        {
            Vec3 N = calculatenormal(P_, ShaderGlobalsAccess.get_flipHandedness(ec) != 0);
            *(Vec3*)out_ = N;
        }

        public static float osl_area(void* P_)
        {
            Vec3 N = calculatenormal(P_, false);
            return MathF.Sqrt(N.x * N.x + N.y * N.y + N.z * N.z);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Matrices (opmatrix.cpp)
        // ------------------------------------------------------------------------------------------------------------------------------------

        private static readonly ulong h_common = new ustring("common").hash();
        private static readonly ulong h_shader = new ustring("shader").hash();
        private static readonly ulong h_object = new ustring("object").hash();

        private static ulong commonspace_synonym(void* oec)
        {
            return ctx_of(oec).shadingsys().commonspace_synonym().hash();
        }

        public static int osl_get_matrix(void* oec, void* r, ulong from)
        {
            Matrix44* M = (Matrix44*)r;
            if (from == h_common || from == commonspace_synonym(oec))
            {
                *M = Matrix44.identity;
                return 1;
            }
            if (from == h_shader)
            {
                RsFallback.rs_get_matrix_xform_time(oec, out *M, ShaderGlobalsAccess.get_shader2common(oec), ShaderGlobalsAccess.get_time(oec));
                return 1;
            }
            if (from == h_object)
            {
                RsFallback.rs_get_matrix_xform_time(oec, out *M, ShaderGlobalsAccess.get_object2common(oec), ShaderGlobalsAccess.get_time(oec));
                return 1;
            }
            bool ok = RsFallback.rs_get_matrix_space_time(oec, out *M, from, ShaderGlobalsAccess.get_time(oec));
            if (!ok)
            {
                *M = Matrix44.identity;
                if (ctx_of(oec).shadingsys().unknown_coordsys_error())
                {
                    ShaderGlobalsAccess.errorfmt((ShaderGlobals*)oec, "Unknown transformation \"{}\"", u_from(from));
                }
            }
            return ok ? 1 : 0;
        }

        public static int osl_get_inverse_matrix(void* oec, void* r, ulong to)
        {
            Matrix44* M = (Matrix44*)r;
            if (to == h_common || to == commonspace_synonym(oec))
            {
                *M = Matrix44.identity;
                return 1;
            }
            if (to == h_shader)
            {
                RsFallback.rs_get_inverse_matrix_xform_time(oec, out *M, ShaderGlobalsAccess.get_shader2common(oec), ShaderGlobalsAccess.get_time(oec));
                return 1;
            }
            if (to == h_object)
            {
                RsFallback.rs_get_inverse_matrix_xform_time(oec, out *M, ShaderGlobalsAccess.get_object2common(oec), ShaderGlobalsAccess.get_time(oec));
                return 1;
            }
            bool ok = RsFallback.rs_get_inverse_matrix_space_time(oec, out *M, to, ShaderGlobalsAccess.get_time(oec));
            if (!ok)
            {
                *M = Matrix44.identity;
                if (ctx_of(oec).shadingsys().unknown_coordsys_error())
                {
                    ShaderGlobalsAccess.errorfmt((ShaderGlobals*)oec, "Unknown transformation \"{}\"", u_from(to));
                }
            }
            return ok ? 1 : 0;
        }

        public static int osl_prepend_matrix_from(void* oec, void* r, ulong from_)
        {
            Matrix44 m;
            int ok = osl_get_matrix(oec, &m, from_);
            if (ok != 0)
            {
                *(Matrix44*)r = m * *(Matrix44*)r;
            }
            else
            {
                if (ctx_of(oec).shadingsys().unknown_coordsys_error())
                {
                    ShaderGlobalsAccess.errorfmt((ShaderGlobals*)oec, "Unknown transformation \"{}\"", u_from(from_));
                }
            }
            return ok;
        }

        public static int osl_get_from_to_matrix(void* oec, void* r, ulong from_, ulong to_)
        {
            Matrix44 Mfrom;
            Matrix44 Mto;
            int ok = osl_get_matrix(oec, &Mfrom, from_);
            ok &= osl_get_inverse_matrix(oec, &Mto, to_);
            *(Matrix44*)r = Mfrom * Mto;
            return ok;
        }

        public static int osl_transform_triple(void* oec, void* Pin, int Pin_derivs, void* Pout, int Pout_derivs, ulong from_, ulong to_, int vectype)
        {
            Matrix44 M;
            int ok;
            Pin_derivs &= Pout_derivs;
            if (from_ == h_common)
            {
                ok = osl_get_inverse_matrix(oec, &M, to_);
            }
            else if (to_ == h_common)
            {
                ok = osl_get_matrix(oec, &M, from_);
            }
            else
            {
                ok = osl_get_from_to_matrix(oec, &M, from_, to_);
            }
            if (ok != 0)
            {
                if (vectype == (int)VECSEMANTICS.POINT)
                {
                    if (Pin_derivs != 0)
                    {
                        OslRuntime.osl_transform_dvmdv(Pout, &M, Pin);
                    }
                    else
                    {
                        OslRuntime.osl_transform_vmv(Pout, &M, Pin);
                    }
                }
                else if (vectype == (int)VECSEMANTICS.VECTOR)
                {
                    if (Pin_derivs != 0)
                    {
                        OslRuntime.osl_transformv_dvmdv(Pout, &M, Pin);
                    }
                    else
                    {
                        OslRuntime.osl_transformv_vmv(Pout, &M, Pin);
                    }
                }
                else if (vectype == (int)VECSEMANTICS.NORMAL)
                {
                    if (Pin_derivs != 0)
                    {
                        OslRuntime.osl_transformn_dvmdv(Pout, &M, Pin);
                    }
                    else
                    {
                        OslRuntime.osl_transformn_vmv(Pout, &M, Pin);
                    }
                }
            }
            else
            {
                *(Vec3*)Pout = *(Vec3*)Pin;
                if (Pin_derivs != 0)
                {
                    ((Vec3*)Pout)[1] = ((Vec3*)Pin)[1];
                    ((Vec3*)Pout)[2] = ((Vec3*)Pin)[2];
                }
            }
            if (Pout_derivs != 0 && Pin_derivs == 0)
            {
                ((Vec3*)Pout)[1] = new Vec3(0.0f, 0.0f, 0.0f);
                ((Vec3*)Pout)[2] = new Vec3(0.0f, 0.0f, 0.0f);
            }
            return ok;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Regular expressions (osl_regex_impl)
        // ------------------------------------------------------------------------------------------------------------------------------------

        public static int osl_regex_impl(void* sg_, ulong subject_, void* results, int nresults, ulong pattern_, int fullmatch)
        {
            ShadingContext ctx = ctx_of(sg_);
            string subject = u_from(subject_).@string();
            ustring pattern = u_from(pattern_);
            Regex regex = ctx.find_regex(pattern);
            if (fullmatch != 0)
            {
                regex = ctx.find_regex(new ustring("\\A(?:" + pattern.@string() + ")\\z"));
            }
            Match m = regex.Match(subject);
            if (nresults > 0)
            {
                int res = m.Success ? 1 : 0;
                int* mm = (int*)results;
                for (int r = 0; r < nresults; ++r)
                {
                    if (res != 0 && r / 2 < m.Groups.Count)
                    {
                        Group g = m.Groups[r / 2];
                        if ((r & 1) == 0)
                        {
                            mm[r] = g.Success ? g.Index : subject.Length;
                        }
                        else
                        {
                            mm[r] = g.Success ? g.Index + g.Length : subject.Length;
                        }
                    }
                    else
                    {
                        mm[r] = pattern.length();
                    }
                }
                return res;
            }
            return m.Success ? 1 : 0;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Closures: the registry callbacks are managed delegates
        // ------------------------------------------------------------------------------------------------------------------------------------

        public static void osl_closure_prepare(void* sg_, int id, void* mem)
        {
            ShadingContext ctx = ctx_of(sg_);
            ClosureRegistry.ClosureEntry e = ctx.shadingsys().find_closure(id);
            if (e != null && e.prepare != null)
            {
                e.prepare(ctx.renderer(), id, mem);
            }
        }

        public static void osl_closure_setup(void* sg_, int id, void* mem)
        {
            ShadingContext ctx = ctx_of(sg_);
            ClosureRegistry.ClosureEntry e = ctx.shadingsys().find_closure(id);
            if (e != null && e.setup != null)
            {
                e.setup(ctx.renderer(), id, mem);
            }
        }
    }
}
