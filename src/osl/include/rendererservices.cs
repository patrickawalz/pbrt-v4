// OSL port: include/OSL/rendererservices.h, liboslexec/rendservices.cpp and liboslexec/rs_fallback.cpp
//
// RendererServices is the class a renderer derives from to give shaders access to the renderer: transformations, attributes, user data, textures, point
// clouds, ray tracing, messages. The base class has the defaults of rendservices.cpp (matrix inversion from get_matrix, the texture calls going to the
// TextureSystem, errors going to the ShadingContext). RsFallback is rs_fallback.cpp: the entry points the compiled shader calls (rs_*), which take the
// ShaderGlobals as the opaque execution context and forward to the RendererServices it carries.

using System;
using System.Runtime.InteropServices;
using OIIO;
using OSL.pvt;

namespace OSL
{
    public unsafe class RendererServices
    {
        // Names of the supported features, queried with supports()
        public static class Feature
        {
            public const string vectorized_closures = "vectorized_closures";
        }

        private TextureSystem m_texturesys;
        private static TextureSystem shared_texturesys;
        private static readonly object shared_texturesys_mutex = new object();

        public RendererServices(TextureSystem texsys = null)
        {
            m_texturesys = texsys;
            if (m_texturesys == null)
            {
                // The C++ makes (or shares) an OIIO TextureSystem; here it is the simple default one
                lock (shared_texturesys_mutex)
                {
                    if (shared_texturesys == null)
                    {
                        shared_texturesys = new SimpleTextureSystem();
                    }
                    m_texturesys = shared_texturesys;
                }
                m_texturesys.attribute("automip", 1);
                m_texturesys.attribute("autotile", 64);
            }
        }

        /// Given the name of a 'feature', return whether this RendererServices supports it.
        public virtual int supports(string feature) { return 0; }

        public virtual bool get_matrix(ShaderGlobals* sg, out Matrix44 result, TransformationPtr xform, float time)
        {
            result = Matrix44.identity;
            return false;
        }

        public virtual bool get_inverse_matrix(ShaderGlobals* sg, out Matrix44 result, TransformationPtr xform, float time)
        {
            bool ok = get_matrix(sg, out result, xform, time);
            if (ok)
            {
                result = RsMatrix.invert(result);
            }
            return ok;
        }

        public virtual bool get_matrix(ShaderGlobals* sg, out Matrix44 result, TransformationPtr xform)
        {
            result = Matrix44.identity;
            return false;
        }

        public virtual bool get_inverse_matrix(ShaderGlobals* sg, out Matrix44 result, TransformationPtr xform)
        {
            bool ok = get_matrix(sg, out result, xform);
            if (ok)
            {
                result = RsMatrix.invert(result);
            }
            return ok;
        }

        public virtual bool get_matrix(ShaderGlobals* sg, out Matrix44 result, ulong from, float time)
        {
            result = Matrix44.identity;
            return false;
        }

        public virtual bool get_inverse_matrix(ShaderGlobals* sg, out Matrix44 result, ulong to, float time)
        {
            bool ok = get_matrix(sg, out result, to, time);
            if (ok)
            {
                result = RsMatrix.invert(result);
            }
            return ok;
        }

        public virtual bool get_matrix(ShaderGlobals* sg, out Matrix44 result, ulong from)
        {
            result = Matrix44.identity;
            return false;
        }

        public virtual bool get_inverse_matrix(ShaderGlobals* sg, out Matrix44 result, ulong to)
        {
            bool ok = get_matrix(sg, out result, to);
            if (ok)
            {
                result = RsMatrix.invert(result);
            }
            return ok;
        }

        public virtual bool transform_points(ShaderGlobals* sg, ulong from, ulong to, float time, Vec3* Pin, Vec3* Pout, int npoints,
                                             VECSEMANTICS vectype)
        {
            return false;
        }

        // Error, warning, print and file output: the arguments are encoded (see OSL.Journal.decode_message) and the default is to forward the decoded
        // message to the ShadingContext
        public virtual void errorfmt(ShaderGlobals* sg, ulong fmt_specification, int arg_count, EncodedType* arg_types, uint arg_values_size,
                                     byte* arg_values)
        {
            string message = Journal.decode_message(fmt_specification, arg_count, arg_types, arg_values);
            ShadingContext ctx = ShaderGlobalsAccess.get_context(sg);
            ctx.errorfmt("{}", message);
        }

        public virtual void warningfmt(ShaderGlobals* sg, ulong fmt_specification, int arg_count, EncodedType* arg_types, uint arg_values_size,
                                       byte* arg_values)
        {
            ShadingContext ctx = ShaderGlobalsAccess.get_context(sg);
            if (ctx.allow_warnings())
            {
                string message = Journal.decode_message(fmt_specification, arg_count, arg_types, arg_values);
                ctx.warningfmt("{}", message);
            }
        }

        public virtual void printfmt(ShaderGlobals* sg, ulong fmt_specification, int arg_count, EncodedType* arg_types, uint arg_values_size,
                                     byte* arg_values)
        {
            string message = Journal.decode_message(fmt_specification, arg_count, arg_types, arg_values);
            ShadingContext ctx = ShaderGlobalsAccess.get_context(sg);
            ctx.messagefmt("{}", message);
        }

        public virtual void filefmt(ShaderGlobals* sg, ulong filename_hash, ulong fmt_specification, int arg_count, EncodedType* arg_types,
                                    uint arg_values_size, byte* arg_values)
        {
            string message = Journal.decode_message(fmt_specification, arg_count, arg_types, arg_values);
            string file_message = Fmt.format("{}:{}", OslStrings.from_hash(filename_hash), message);
            ShadingContext ctx = ShaderGlobalsAccess.get_context(sg);
            ctx.messagefmt("{}", file_message);
        }

        public virtual void build_attribute_getter(ShaderGroup group, bool is_object_lookup, ustring? object_name, ustring? attribute_name,
                                                   bool is_array_lookup, int? array_index, TypeDesc type, bool derivatives,
                                                   AttributeGetterSpec spec)
        {
        }

        public virtual bool get_attribute(ShaderGlobals* sg, bool derivatives, ulong obj, TypeDesc type, ulong name, void* val)
        {
            return false;
        }

        public virtual bool get_array_attribute(ShaderGlobals* sg, bool derivatives, ulong obj, TypeDesc type, ulong name, int index, void* val)
        {
            return false;
        }

        public virtual void build_interpolated_getter(ShaderGroup group, ustring param_name, TypeDesc type, bool derivatives,
                                                      InterpolatedGetterSpec spec)
        {
        }

        public virtual bool get_userdata(bool derivatives, ulong name, TypeDesc type, ShaderGlobals* sg, void* val)
        {
            return false;
        }

        public virtual TextureHandle get_texture_handle(ustring filename, ShadingContext context, TextureOpt* options = null)
        {
            if (texturesys() == null)
            {
                return null;
            }
            TexturePerthread thread_info = context != null ? context.texture_thread_info() : null;
            return texturesys().get_texture_handle(filename, thread_info, options);
        }

        public virtual TextureHandle get_texture_handle(ulong filename, ShadingContext context, TextureOpt* options = null)
        {
            return get_texture_handle(OslStrings.from_hash(filename), context, options);
        }

        public virtual bool good(TextureHandle texture_handle)
        {
            return texturesys().good(texture_handle);
        }

        public virtual bool is_udim(TextureHandle texture_handle)
        {
            return texturesys().is_udim(texture_handle);
        }

        private TexturePerthread resolve_thread_info(TexturePerthread thread_info, ShaderGlobals* sg)
        {
            if (thread_info == null)
            {
                ShadingContext context = ShaderGlobalsAccess.get_context(sg);
                thread_info = context.texture_thread_info();
            }
            return thread_info;
        }

        private TextureHandle resolve_handle(TextureHandle handle, ulong filename, TexturePerthread thread_info, TextureOpt* options)
        {
            if (handle == null)
            {
                handle = texturesys().get_texture_handle(OslStrings.from_hash(filename), thread_info, options);
            }
            return handle;
        }

        private void texture_failed(bool status, ShaderGlobals* sg, string who, ulong* errormessage)
        {
            if (!status)
            {
                string err = texturesys().geterror();
                if (err.Length > 0 && sg != null)
                {
                    if (errormessage != null)
                    {
                        *errormessage = new ustring(err).hash();
                    }
                    else
                    {
                        ShaderGlobalsAccess.errorfmt(sg, "[RendererServices::" + who + "] {}", err);
                    }
                }
                else if (errormessage != null)
                {
                    *errormessage = Strings.unknown.hash();
                }
            }
        }

        public virtual bool texture(ulong filename, TextureHandle texture_handle, TexturePerthread texture_thread_info, ref TextureOpt options,
                                    ShaderGlobals* sg, float s, float t, float dsdx, float dtdx, float dsdy, float dtdy, int nchannels,
                                    float* result, float* dresultds, float* dresultdt, ulong* errormessage)
        {
            texture_thread_info = resolve_thread_info(texture_thread_info, sg);
            TextureOpt opt = options;
            texture_handle = resolve_handle(texture_handle, filename, texture_thread_info, &opt);
            bool status = texturesys().texture(texture_handle, texture_thread_info, ref options, s, t, dsdx, dtdx, dsdy, dtdy, nchannels, result,
                                               dresultds, dresultdt);
            texture_failed(status, sg, "texture", errormessage);
            return status;
        }

        public virtual bool texture3d(ulong filename, TextureHandle texture_handle, TexturePerthread texture_thread_info, ref TextureOpt options,
                                      ShaderGlobals* sg, Vec3 P, Vec3 dPdx, Vec3 dPdy, Vec3 dPdz, int nchannels, float* result,
                                      float* dresultds, float* dresultdt, float* dresultdr, ulong* errormessage)
        {
            texture_thread_info = resolve_thread_info(texture_thread_info, sg);
            TextureOpt opt = options;
            texture_handle = resolve_handle(texture_handle, filename, texture_thread_info, &opt);
            bool status = texturesys().texture3d(texture_handle, texture_thread_info, ref options, P, dPdx, dPdy, dPdz, nchannels, result,
                                                 dresultds, dresultdt, dresultdr);
            texture_failed(status, sg, "texture3d", errormessage);
            return status;
        }

        public virtual bool environment(ulong filename, TextureHandle texture_handle, TexturePerthread texture_thread_info, ref TextureOpt options,
                                        ShaderGlobals* sg, Vec3 R, Vec3 dRdx, Vec3 dRdy, int nchannels, float* result, float* dresultds,
                                        float* dresultdt, ulong* errormessage)
        {
            texture_thread_info = resolve_thread_info(texture_thread_info, sg);
            TextureOpt opt = options;
            texture_handle = resolve_handle(texture_handle, filename, texture_thread_info, &opt);
            bool status = texturesys().environment(texture_handle, texture_thread_info, ref options, R, dRdx, dRdy, nchannels, result, dresultds,
                                                   dresultdt);
            texture_failed(status, sg, "environment", errormessage);
            return status;
        }

        private bool get_texture_info_impl(ulong filename, TextureHandle texture_handle, TexturePerthread texture_thread_info, ShaderGlobals* sg,
                                           int subimage, ulong dataname, TypeDesc datatype, void* data, ulong* errormessage)
        {
            texture_thread_info = resolve_thread_info(texture_thread_info, sg);
            texture_handle = resolve_handle(texture_handle, filename, texture_thread_info, null);
            // The texture system fills a managed array that is then copied out to 'data'
            int n = (int)datatype.numelements() * (int)datatype.aggregate;
            object arr;
            switch (datatype.basetype)
            {
                case (byte)TypeDesc.INT: arr = new int[Math.Max(1, n)]; break;
                case (byte)TypeDesc.STRING: arr = new ustring[Math.Max(1, n)]; break;
                default: arr = new float[Math.Max(1, n)]; break;
            }
            ustring dn = OslStrings.from_hash(dataname);
            bool status = texturesys().get_texture_info(texture_handle, texture_thread_info, subimage, dn, datatype, arr);
            if (status)
            {
                if (arr is int[] ia)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        ((int*)data)[i] = ia[i];
                    }
                }
                else if (arr is float[] fa)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        ((float*)data)[i] = fa[i];
                    }
                }
                else if (arr is ustring[] sa)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        ((ulong*)data)[i] = sa[i].hash();
                    }
                }
            }
            else
            {
                string err = texturesys().geterror();
                if (err.Length > 0 && sg != null)
                {
                    if (errormessage != null)
                    {
                        *errormessage = new ustring(err).hash();
                    }
                    else
                    {
                        ShaderGlobalsAccess.errorfmt(sg, "[RendererServices::get_texture_info] {}", err);
                    }
                }
                else if (errormessage != null)
                {
                    // gettextureinfo failed but did not provide an error, so none should be emitted
                    *errormessage = 0UL;
                }
            }
            return status;
        }

        public virtual bool get_texture_info(ulong filename, TextureHandle texture_handle, TexturePerthread texture_thread_info, ShaderGlobals* sg,
                                             int subimage, ulong dataname, TypeDesc datatype, void* data, ulong* errormessage)
        {
            return get_texture_info_impl(filename, texture_handle, texture_thread_info, sg, subimage, dataname, datatype, data, errormessage);
        }

        public virtual bool get_texture_info(ulong filename, TextureHandle texture_handle, float s, float t, TexturePerthread texture_thread_info,
                                             ShaderGlobals* sg, int subimage, ulong dataname, TypeDesc datatype, void* data, ulong* errormessage)
        {
            // The (s,t) form only matters for UDIM textures: resolve the tile, then query it
            texture_thread_info = resolve_thread_info(texture_thread_info, sg);
            texture_handle = resolve_handle(texture_handle, filename, texture_thread_info, null);
            if (texture_handle != null && texturesys().is_udim(texture_handle))
            {
                texture_handle = texturesys().resolve_udim(texture_handle, texture_thread_info, s, t);
            }
            return get_texture_info_impl(filename, texture_handle, texture_thread_info, sg, subimage, dataname, datatype, data, errormessage);
        }

        /// center points at three consecutive Vec3: the center and its x and y derivatives
        public virtual int pointcloud_search(ShaderGlobals* sg, ulong filename, Vec3* center, float radius, int max_points, bool sort,
                                             int* out_indices, float* out_distances, int derivs_offset)
        {
            return PointCloudStore.search(sg, filename, center, radius, max_points, sort, out_indices, out_distances, derivs_offset);
        }

        public virtual int pointcloud_get(ShaderGlobals* sg, ulong filename, int* indices, int count, ulong attr_name, TypeDesc attr_type,
                                          void* out_data)
        {
            return PointCloudStore.get(sg, filename, indices, count, attr_name, attr_type, out_data);
        }

        public virtual bool pointcloud_write(ShaderGlobals* sg, ulong filename, Vec3 pos, int nattribs, ulong* names, TypeDesc* types, void** data)
        {
            return PointCloudStore.write(sg, filename, pos, nattribs, names, types, data);
        }

        public virtual bool trace(ref TraceOpt options, ShaderGlobals* sg, Vec3 P, Vec3 dPdx, Vec3 dPdy, Vec3 R, Vec3 dRdx, Vec3 dRdy)
        {
            return false;
        }

        public virtual bool getmessage(ShaderGlobals* sg, ulong source, ulong name, TypeDesc type, void* val, bool derivatives)
        {
            return false;
        }

        public virtual TextureSystem texturesys() { return m_texturesys; }

        public virtual void* device_alloc(long size) { return null; }
        public virtual void device_free(void* ptr) { }
        public virtual void* copy_to_device(void* dst_device, void* src_host, long size) { return null; }

        public virtual void cache_insert(string cachename, string key, string value) { }
        public virtual bool cache_get(string cachename, string key, out string value)
        {
            value = null;
            return false;
        }
    }

    /// Small helpers shared by the services
    public static class OslStrings
    {
        public static ustring from_hash(ulong h)
        {
            ustring u;
            if (ustring.from_hash(h, out u))
            {
                return u;
            }
            return new ustring("");
        }
    }

    public static class RsMatrix
    {
        /// Matrix inverse (Gauss-Jordan with full pivoting on rows, like Imath::Matrix44::inverse for a general matrix); the zero matrix for a singular input
        public static Matrix44 invert(Matrix44 m)
        {
            double[,] a = new double[4, 8];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    a[i, j] = m[i, j];
                    a[i, j + 4] = i == j ? 1.0 : 0.0;
                }
            }
            for (int c = 0; c < 4; ++c)
            {
                int p = c;
                for (int r = c + 1; r < 4; ++r)
                {
                    if (Math.Abs(a[r, c]) > Math.Abs(a[p, c]))
                    {
                        p = r;
                    }
                }
                if (a[p, c] == 0.0)
                {
                    return new Matrix44();
                }
                if (p != c)
                {
                    for (int j = 0; j < 8; ++j)
                    {
                        double t = a[c, j];
                        a[c, j] = a[p, j];
                        a[p, j] = t;
                    }
                }
                double d = a[c, c];
                for (int j = 0; j < 8; ++j)
                {
                    a[c, j] /= d;
                }
                for (int r = 0; r < 4; ++r)
                {
                    if (r != c)
                    {
                        double f = a[r, c];
                        for (int j = 0; j < 8; ++j)
                        {
                            a[r, j] -= f * a[c, j];
                        }
                    }
                }
            }
            Matrix44 res = new Matrix44();
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    res[i, j] = (float)a[i, j + 4];
                }
            }
            return res;
        }
    }

    /// rs_fallback.cpp: the renderer services entry points the compiled shader calls. The first argument is the OpaqueExecContextPtr, which is the
    /// ShaderGlobals.
    public static unsafe class RsFallback
    {
        private static ShaderGlobals* get_sg(void* exec_ctx) { return (ShaderGlobals*)exec_ctx; }
        private static RendererServices rend(ShaderGlobals* sg) { return ShaderGlobalsAccess.get_renderer(sg); }

        public static bool rs_get_matrix_xform_time(void* exec_ctx, out Matrix44 result, void* from, float time)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).get_matrix(sg, out result, new TransformationPtr(from), time);
        }

        public static bool rs_get_inverse_matrix_xform_time(void* exec_ctx, out Matrix44 result, void* xform, float time)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).get_inverse_matrix(sg, out result, new TransformationPtr(xform), time);
        }

        public static bool rs_get_matrix_space_time(void* exec_ctx, out Matrix44 result, ulong from, float time)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).get_matrix(sg, out result, from, time);
        }

        public static bool rs_get_inverse_matrix_space_time(void* exec_ctx, out Matrix44 result, ulong to, float time)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).get_inverse_matrix(sg, out result, to, time);
        }

        public static bool rs_get_matrix_xform(void* exec_ctx, out Matrix44 result, void* xform)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).get_matrix(sg, out result, new TransformationPtr(xform));
        }

        public static bool rs_get_inverse_matrix_xform(void* exec_ctx, out Matrix44 result, void* xform)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).get_inverse_matrix(sg, out result, new TransformationPtr(xform));
        }

        public static bool rs_get_matrix_space(void* exec_ctx, out Matrix44 result, ulong from)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).get_matrix(sg, out result, from);
        }

        public static bool rs_get_inverse_matrix_space(void* exec_ctx, out Matrix44 result, ulong to)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).get_inverse_matrix(sg, out result, to);
        }

        public static bool rs_transform_points(void* exec_ctx, ulong from, ulong to, float time, Vec3* Pin, Vec3* Pout, int npoints,
                                               VECSEMANTICS vectype)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).transform_points(sg, from, to, time, Pin, Pout, npoints, vectype);
        }

        public static bool rs_texture(void* exec_ctx, ulong filename, TextureHandle texture_handle, TexturePerthread texture_thread_info,
                                      ref TextureOpt options, float s, float t, float dsdx, float dtdx, float dsdy, float dtdy, int nchannels,
                                      float* result, float* dresultds, float* dresultdt, ulong* errormessage)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).texture(filename, texture_handle, texture_thread_info, ref options, sg, s, t, dsdx, dtdx, dsdy, dtdy, nchannels, result,
                                    dresultds, dresultdt, errormessage);
        }

        public static bool rs_texture3d(void* exec_ctx, ulong filename, TextureHandle texture_handle, TexturePerthread texture_thread_info,
                                        ref TextureOpt options, Vec3 P, Vec3 dPdx, Vec3 dPdy, Vec3 dPdz, int nchannels, float* result,
                                        float* dresultds, float* dresultdt, float* dresultdr, ulong* errormessage)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).texture3d(filename, texture_handle, texture_thread_info, ref options, sg, P, dPdx, dPdy, dPdz, nchannels, result,
                                      dresultds, dresultdt, dresultdr, errormessage);
        }

        public static bool rs_environment(void* exec_ctx, ulong filename, TextureHandle texture_handle, TexturePerthread texture_thread_info,
                                          ref TextureOpt options, Vec3 R, Vec3 dRdx, Vec3 dRdy, int nchannels, float* result, float* dresultds,
                                          float* dresultdt, ulong* errormessage)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).environment(filename, texture_handle, texture_thread_info, ref options, sg, R, dRdx, dRdy, nchannels, result, dresultds,
                                        dresultdt, errormessage);
        }

        public static bool rs_get_texture_info(void* exec_ctx, ulong filename, TextureHandle texture_handle, TexturePerthread texture_thread_info,
                                               int subimage, ulong dataname, TypeDesc datatype, void* data, ulong* errormessage)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).get_texture_info(filename, texture_handle, texture_thread_info, sg, subimage, dataname, datatype, data, errormessage);
        }

        public static bool rs_get_texture_info_st(void* exec_ctx, ulong filename, TextureHandle texture_handle, float s, float t,
                                                  TexturePerthread texture_thread_info, int subimage, ulong dataname, TypeDesc datatype, void* data,
                                                  ulong* errormessage)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).get_texture_info(filename, texture_handle, s, t, texture_thread_info, sg, subimage, dataname, datatype, data,
                                             errormessage);
        }

        public static int rs_pointcloud_search(void* exec_ctx, ulong filename, Vec3* center, float radius, int max_points, bool sort, int* out_indices,
                                               float* out_distances, int derivs_offset)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).pointcloud_search(sg, filename, center, radius, max_points, sort, out_indices, out_distances, derivs_offset);
        }

        public static int rs_pointcloud_get(void* exec_ctx, ulong filename, int* indices, int count, ulong attr_name, TypeDesc attr_type, void* out_data)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).pointcloud_get(sg, filename, indices, count, attr_name, attr_type, out_data);
        }

        public static bool rs_pointcloud_write(void* exec_ctx, ulong filename, Vec3 pos, int nattribs, ulong* names, TypeDesc* types, void** data)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).pointcloud_write(sg, filename, pos, nattribs, names, types, data);
        }

        public static bool rs_trace(void* exec_ctx, ref TraceOpt options, Vec3 P, Vec3 dPdx, Vec3 dPdy, Vec3 R, Vec3 dRdx, Vec3 dRdy)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).trace(ref options, sg, P, dPdx, dPdy, R, dRdx, dRdy);
        }

        public static bool rs_trace_get(void* exec_ctx, ulong name, TypeDesc type, void* val, bool derivatives)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return rend(sg).getmessage(sg, Strings.trace.hash(), name, type, val, derivatives);
        }

        public static void* rs_allocate_closure(void* exec_ctx, long size, long alignment)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            return ShaderGlobalsAccess.get_context(sg).allocate_closure((int)size, (int)alignment);
        }

        public static void rs_errorfmt(void* exec_ctx, ulong fmt_specification, int count, EncodedType* argTypes, uint argValuesSize, byte* argValues)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            rend(sg).errorfmt(sg, fmt_specification, count, argTypes, argValuesSize, argValues);
        }

        public static void rs_warningfmt(void* exec_ctx, ulong fmt_specification, int count, EncodedType* argTypes, uint argValuesSize,
                                         byte* argValues)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            rend(sg).warningfmt(sg, fmt_specification, count, argTypes, argValuesSize, argValues);
        }

        public static void rs_printfmt(void* exec_ctx, ulong fmt_specification, int count, EncodedType* argTypes, uint argValuesSize, byte* argValues)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            rend(sg).printfmt(sg, fmt_specification, count, argTypes, argValuesSize, argValues);
        }

        public static void rs_filefmt(void* exec_ctx, ulong filename, ulong fmt_specification, int count, EncodedType* argTypes, uint argValuesSize,
                                      byte* argValues)
        {
            ShaderGlobals* sg = get_sg(exec_ctx);
            rend(sg).filefmt(sg, filename, fmt_specification, count, argTypes, argValuesSize, argValues);
        }
    }
}
