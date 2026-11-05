// OSL port: testshade/simplerend.cpp and simplerend.h -- the SimpleRenderer that testshade uses: closure registration, named transformations, camera
// attributes, user data, a fake trace()/getmessage(). The image output buffers of the C++ (OIIO::ImageBuf) are plain float arrays here.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OIIO;
using OSL;
using OSL.pvt;

namespace OSL.TestShade
{
    /// One piece of user data given on the command line (testshade --userdata)
    public sealed class UserDataItem
    {
        public string name;
        public TypeDesc type;
        public byte[] data;
    }

    public static unsafe class ClosureIDs
    {
        public const int EMISSION_ID = 1;
        public const int BACKGROUND_ID = 2;
        public const int DIFFUSE_ID = 3;
        public const int OREN_NAYAR_ID = 4;
        public const int TRANSLUCENT_ID = 5;
        public const int PHONG_ID = 6;
        public const int WARD_ID = 7;
        public const int MICROFACET_ID = 8;
        public const int REFLECTION_ID = 9;
        public const int FRESNEL_REFLECTION_ID = 10;
        public const int REFRACTION_ID = 11;
        public const int TRANSPARENT_ID = 12;
        public const int DEBUG_ID = 13;
        public const int HOLDOUT_ID = 14;
        public const int PARAMETER_TEST_ID = 15;
    }

    /// Lays out the fields of a C struct the way the C++ compiler would (natural alignment), to give the ClosureParam offsets
    public sealed class StructLayout_
    {
        public int size;
        public int alignment = 1;
        public List<ClosureParam> @params = new List<ClosureParam>();

        private int place(int fieldsize, int fieldalign)
        {
            alignment = Math.Max(alignment, fieldalign);
            size = (size + fieldalign - 1) / fieldalign * fieldalign;
            int offset = size;
            size += fieldsize;
            return offset;
        }

        private void add(TypeDesc type, string key, int fieldsize, int fieldalign, int count)
        {
            int offset = place(fieldsize * count, fieldalign);
            TypeDesc t = count > 1 ? new TypeDesc(type.basetype, type.aggregate, type.vecsemantics, count) : type;
            @params.Add(new ClosureParam(t, offset, key, fieldsize * count));
        }

        public StructLayout_ i(string key = null, int count = 1)
        {
            add(TypeDescs.TypeInt, key, 4, 4, count);
            return this;
        }

        public StructLayout_ f(string key = null, int count = 1)
        {
            add(TypeDescs.TypeFloat, key, 4, 4, count);
            return this;
        }

        public StructLayout_ color(string key = null, int count = 1)
        {
            add(TypeDescs.TypeColor, key, 12, 4, count);
            return this;
        }

        public StructLayout_ vec(string key = null, int count = 1)
        {
            add(TypeDescs.TypeVector, key, 12, 4, count);
            return this;
        }

        public StructLayout_ str(string key = null, int count = 1)
        {
            add(TypeDescs.TypeString, key, 8, 8, count);
            return this;
        }

        /// A field that is part of the C struct but is not a closure parameter
        public StructLayout_ skip(int fieldsize, int fieldalign)
        {
            place(fieldsize, fieldalign);
            return this;
        }

        public ClosureParam[] finish()
        {
            List<ClosureParam> result = new List<ClosureParam>(@params);
            int total = (size + alignment - 1) / alignment * alignment;
            result.Add(ClosureParam.CLOSURE_FINISH_PARAM(total, alignment));
            return result.ToArray();
        }
    }

    /// The behavior of OIIO::ErrorHandler::operator(): messages go to stdout as they are, errors and warnings get a prefix and go to stderr
    public sealed class TestshadeErrorHandler : ErrorHandler
    {
        public bool verbose;

        public override void handle(int errcode, string msg)
        {
            if (errcode >= (int)ErrorCode.EH_SEVERE && errcode < (int)ErrorCode.EH_DEBUG)
            {
                Console.Error.Write(with_newline("SEVERE ERROR: " + msg));
            }
            else if (errcode >= (int)ErrorCode.EH_ERROR && errcode < (int)ErrorCode.EH_DEBUG)
            {
                Console.Error.Write(with_newline("ERROR: " + msg));
            }
            else if (errcode >= (int)ErrorCode.EH_WARNING && errcode < (int)ErrorCode.EH_DEBUG)
            {
                Console.Error.Write(with_newline("WARNING: " + msg));
            }
            else if (errcode == (int)ErrorCode.EH_INFO)
            {
                if (verbose)
                {
                    Console.Out.Write(with_newline("INFO: " + msg));
                }
            }
            else if (errcode == (int)ErrorCode.EH_DEBUG)
            {
                if (verbose)
                {
                    Console.Out.Write(with_newline("DEBUG: " + msg));
                }
            }
            else
            {
                Console.Out.Write(msg);
            }
        }

        private static string with_newline(string s)
        {
            return s + "\n";
        }
    }

    public unsafe class SimpleRenderer : RendererServices
    {
        private delegate bool AttrGetter(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val);

        public TestshadeErrorHandler errhandler_ = new TestshadeErrorHandler();
        public List<UserDataItem> userdata = new List<UserDataItem>();
        public List<ShaderGroup> m_shaders = new List<ShaderGroup>();
        public bool m_use_rs_bitcode;

        private Matrix44 m_world_to_camera;
        private ulong m_projection;
        private float m_fov;
        private float m_pixelaspect;
        private float m_hither;
        private float m_yon;
        private float[] m_shutter = new float[2];
        private float[] m_screen_window = new float[4];
        private int m_xres;
        private int m_yres;
        private Dictionary<ulong, Matrix44> m_named_xforms = new Dictionary<ulong, Matrix44>();
        private Dictionary<ulong, AttrGetter> m_attr_getters = new Dictionary<ulong, AttrGetter>();
        private List<OutputBuffer> m_outputbufs = new List<OutputBuffer>();
        private List<ustring> m_outputvars = new List<ustring>();
        public Dictionary<string, object> options = new Dictionary<string, object>();

        private static readonly TypeDesc TypeFloatArray2 = new TypeDesc(TypeDesc.FLOAT, 2);
        private static readonly TypeDesc TypeFloatArray4 = new TypeDesc(TypeDesc.FLOAT, 4);
        private static readonly TypeDesc TypeIntArray2 = new TypeDesc(TypeDesc.INT, 2);

        private static readonly ulong h_perspective = new ustring("perspective").hash();
        private static readonly ulong h_raster = new ustring("raster").hash();
        private static readonly ulong h_camera = new ustring("camera").hash();
        private static readonly ulong h_screen = new ustring("screen").hash();
        private static readonly ulong h_NDC = new ustring("NDC").hash();
        private static readonly ulong h_options = new ustring("options").hash();
        private static readonly ulong h_blahblah = new ustring("blahblah").hash();
        private static readonly ulong h_shading_index = new ustring("shading:index").hash();
        private static readonly ulong h_face_idx = new ustring("face_idx").hash();
        private static readonly ulong h_s = new ustring("s").hash();
        private static readonly ulong h_t = new ustring("t").hash();
        private static readonly ulong h_red = new ustring("red").hash();
        private static readonly ulong h_green = new ustring("green").hash();
        private static readonly ulong h_blue = new ustring("blue").hash();

        public SimpleRenderer()
        {
            Matrix44 M = Matrix44.identity;
            camera_params(M, h_perspective, 90.0f, 0.1f, 1000.0f, 256, 256);

            // Set up getters
            m_attr_getters[new ustring("osl:version").hash()] = get_osl_version;
            m_attr_getters[new ustring("camera:resolution").hash()] = get_camera_resolution;
            m_attr_getters[new ustring("camera:projection").hash()] = get_camera_projection;
            m_attr_getters[new ustring("camera:pixelaspect").hash()] = get_camera_pixelaspect;
            m_attr_getters[new ustring("camera:screen_window").hash()] = get_camera_screen_window;
            m_attr_getters[new ustring("camera:fov").hash()] = get_camera_fov;
            m_attr_getters[new ustring("camera:clip").hash()] = get_camera_clip;
            m_attr_getters[new ustring("camera:clip_near").hash()] = get_camera_clip_near;
            m_attr_getters[new ustring("camera:clip_far").hash()] = get_camera_clip_far;
            m_attr_getters[new ustring("camera:shutter").hash()] = get_camera_shutter;
            m_attr_getters[new ustring("camera:shutter_open").hash()] = get_camera_shutter_open;
            m_attr_getters[new ustring("camera:shutter_close").hash()] = get_camera_shutter_close;
        }

        public enum JournalKind
        {
            Error,
            Warning,
            Print,
            File,
        }

        public sealed class JournalEntry
        {
            public JournalKind kind;
            public int thread_index;
            public int shade_index;
            public string filename;
            public string message;
        }

        private readonly List<JournalEntry> m_journal = new List<JournalEntry>();
        private readonly Dictionary<int, int> m_warnings_per_thread = new Dictionary<int, int>();
        private readonly object m_journal_mutex = new object();

        private void record(JournalKind kind, ShaderGlobals* sg, string filename, ulong fmt_specification, int arg_count, EncodedType* arg_types,
                            byte* arg_values)
        {
            JournalEntry entry = new JournalEntry();
            entry.kind = kind;
            entry.thread_index = sg->thread_index;
            entry.shade_index = sg->shade_index;
            entry.filename = filename;
            entry.message = Journal.decode_message(fmt_specification, arg_count, arg_types, arg_values);
            lock (m_journal_mutex)
            {
                m_journal.Add(entry);
            }
        }

        public override void errorfmt(ShaderGlobals* sg, ulong fmt_specification, int arg_count, EncodedType* arg_types, uint arg_values_size,
                                      byte* arg_values)
        {
            record(JournalKind.Error, sg, null, fmt_specification, arg_count, arg_types, arg_values);
        }

        public override void warningfmt(ShaderGlobals* sg, ulong fmt_specification, int arg_count, EncodedType* arg_types, uint arg_values_size,
                                        byte* arg_values)
        {
            int max_warnings = ShaderGlobalsAccess.get_context(sg).shadingsys().max_warnings_per_thread();
            lock (m_journal_mutex)
            {
                int count;
                m_warnings_per_thread.TryGetValue(sg->thread_index, out count);
                if (count >= max_warnings)
                {
                    return;
                }
                m_warnings_per_thread[sg->thread_index] = count + 1;
            }
            record(JournalKind.Warning, sg, null, fmt_specification, arg_count, arg_types, arg_values);
        }

        public override void printfmt(ShaderGlobals* sg, ulong fmt_specification, int arg_count, EncodedType* arg_types, uint arg_values_size,
                                      byte* arg_values)
        {
            record(JournalKind.Print, sg, null, fmt_specification, arg_count, arg_types, arg_values);
        }

        public override void filefmt(ShaderGlobals* sg, ulong filename_hash, ulong fmt_specification, int arg_count, EncodedType* arg_types,
                                     uint arg_values_size, byte* arg_values)
        {
            record(JournalKind.File, sg, OslStrings.from_hash(filename_hash).@string(), fmt_specification, arg_count, arg_types, arg_values);
        }

        /// Hands the messages the shaders journaled to the reporter, in the order of the threads and then of the shades (journal::Reader::process)
        public void process_journal(OSL.journal.Reporter reporter)
        {
            List<JournalEntry> entries;
            lock (m_journal_mutex)
            {
                entries = new List<JournalEntry>(m_journal);
                m_journal.Clear();
                m_warnings_per_thread.Clear();
            }
            foreach (JournalEntry e in entries)
            {
                switch (e.kind)
                {
                    case JournalKind.Error:
                        reporter.report_error(e.thread_index, e.shade_index, e.message);
                        break;
                    case JournalKind.Warning:
                        reporter.report_warning(e.thread_index, e.shade_index, e.message);
                        break;
                    case JournalKind.Print:
                        reporter.report_print(e.thread_index, e.shade_index, e.message);
                        break;
                    case JournalKind.File:
                        reporter.report_file_print(e.thread_index, e.shade_index, e.filename, e.message);
                        break;
                }
            }
        }

        public TestshadeErrorHandler errhandler()
        {
            return errhandler_;
        }

        public override int supports(string feature)
        {
            return 0;
        }

        public void init_shadingsys(ShadingSystem ss)
        {
        }

        public static void register_JIT_Global_Variables()
        {
        }

        public void prepare_render()
        {
        }

        public void warmup()
        {
        }

        public void finalize_pixel_buffer()
        {
        }

        public void clear()
        {
            m_shaders.Clear();
            m_outputbufs.Clear();
            m_outputvars.Clear();
        }

        public List<ShaderGroup> shaders()
        {
            return m_shaders;
        }

        public void use_rs_bitcode(bool v)
        {
            m_use_rs_bitcode = v;
        }

        public void camera_params(Matrix44 world_to_camera, ulong projection, float hfov, float hither, float yon, int xres, int yres)
        {
            m_world_to_camera = world_to_camera;
            m_projection = projection;
            m_fov = hfov;
            m_pixelaspect = 1.0f;  // hard-coded
            m_hither = hither;
            m_yon = yon;
            m_shutter[0] = 0.0f;
            m_shutter[1] = 1.0f;  // hard-coded
            float frame_aspect = (float)xres / (float)yres * m_pixelaspect;
            m_screen_window[0] = -frame_aspect;
            m_screen_window[1] = -1.0f;
            m_screen_window[2] = frame_aspect;
            m_screen_window[3] = 1.0f;
            m_xres = xres;
            m_yres = yres;
        }

        public void name_transform(string name, Matrix44 xform)
        {
            m_named_xforms[new ustring(name).hash()] = xform;
        }

        public override bool get_matrix(ShaderGlobals* sg, out Matrix44 result, TransformationPtr xform, float time)
        {
            // SimpleRenderer doesn't understand motion blur and transformations are just simple 4x4 matrices.
            result = *(Matrix44*)xform.ptr;
            return true;
        }

        public override bool get_matrix(ShaderGlobals* sg, out Matrix44 result, ulong from, float time)
        {
            Matrix44 found;
            if (m_named_xforms.TryGetValue(from, out found))
            {
                result = found;
                return true;
            }
            result = Matrix44.identity;
            return false;
        }

        public override bool get_matrix(ShaderGlobals* sg, out Matrix44 result, TransformationPtr xform)
        {
            result = *(Matrix44*)xform.ptr;
            return true;
        }

        public override bool get_matrix(ShaderGlobals* sg, out Matrix44 result, ulong from)
        {
            // SimpleRenderer doesn't understand motion blur, so we never fail on account of time-varying transformations.
            Matrix44 found;
            if (m_named_xforms.TryGetValue(from, out found))
            {
                result = found;
                return true;
            }
            result = Matrix44.identity;
            return false;
        }

        public override bool get_inverse_matrix(ShaderGlobals* sg, out Matrix44 result, ulong to, float time)
        {
            if (to == h_camera || to == h_screen || to == h_NDC || to == h_raster)
            {
                Matrix44 M = m_world_to_camera;
                if (to == h_screen || to == h_NDC || to == h_raster)
                {
                    float depthrange = (float)((double)m_yon - (double)m_hither);
                    if (m_projection == h_perspective)
                    {
                        float tanhalffov = (float)Math.Tan(0.5f * m_fov * Math.PI / 180.0);
                        Matrix44 camera_to_screen = new Matrix44(1 / tanhalffov, 0, 0, 0, 0, 1 / tanhalffov, 0, 0, 0, 0, m_yon / depthrange, 1, 0, 0,
                                                                 -m_yon * m_hither / depthrange, 0);
                        M = M * camera_to_screen;
                    }
                    else
                    {
                        Matrix44 camera_to_screen = new Matrix44(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 / depthrange, 0, 0, 0, -m_hither / depthrange, 1);
                        M = M * camera_to_screen;
                    }
                    if (to == h_NDC || to == h_raster)
                    {
                        float screenleft = -1.0f, screenwidth = 2.0f;
                        float screenbottom = -1.0f, screenheight = 2.0f;
                        Matrix44 screen_to_ndc = new Matrix44(1 / screenwidth, 0, 0, 0, 0, 1 / screenheight, 0, 0, 0, 0, 1, 0, -screenleft / screenwidth,
                                                              -screenbottom / screenheight, 0, 1);
                        M = M * screen_to_ndc;
                        if (to == h_raster)
                        {
                            Matrix44 ndc_to_raster = new Matrix44(m_xres, 0, 0, 0, 0, m_yres, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
                            M = M * ndc_to_raster;
                        }
                    }
                }
                result = M;
                return true;
            }

            Matrix44 found;
            if (m_named_xforms.TryGetValue(to, out found))
            {
                result = RsMatrix.invert(found);
                return true;
            }
            result = Matrix44.identity;
            return false;
        }

        public override bool get_array_attribute(ShaderGlobals* sg, bool derivatives, ulong obj, TypeDesc type, ulong name, int index, void* val)
        {
            AttrGetter getter;
            if (m_attr_getters.TryGetValue(name, out getter))
            {
                return getter(sg, derivatives, obj, type, name, val);
            }

            // In order to test getattribute(), respond positively to "options"/"blahblah"
            if (obj == h_options && name == h_blahblah && type == TypeDescs.TypeFloat)
            {
                *(float*)val = 3.14159f;
                return true;
            }

            if (obj == new ustring("").hash() && name == h_shading_index && type == TypeDescs.TypeInt)
            {
                *(int*)val = sg->shade_index;
                return true;
            }

            // If no named attribute was found, allow userdata to bind to the attribute request.
            if (obj == new ustring("").hash() && index == -1)
            {
                return get_userdata(derivatives, name, type, sg, val);
            }

            return false;
        }

        public override bool get_attribute(ShaderGlobals* sg, bool derivatives, ulong obj, TypeDesc type, ulong name, void* val)
        {
            return get_array_attribute(sg, derivatives, obj, type, name, -1, val);
        }

        public override bool get_userdata(bool derivatives, ulong name, TypeDesc type, ShaderGlobals* sg, void* val)
        {
            // Just to illustrate how this works, respect s and t userdata, filled in with the uv coordinates. In a real renderer, it would probably look
            // up something specific to the primitive, rather than have hard-coded names.
            if (name == h_face_idx && type == TypeDescs.TypeInt)
            {
                ((int*)val)[0] = (int)(4 * sg->u);
                return true;
            }
            if (name == h_s && type == TypeDescs.TypeFloat)
            {
                ((float*)val)[0] = sg->u;
                if (derivatives)
                {
                    ((float*)val)[1] = sg->dudx;
                    ((float*)val)[2] = sg->dudy;
                }
                return true;
            }
            if (name == h_t && type == TypeDescs.TypeFloat)
            {
                ((float*)val)[0] = sg->v;
                if (derivatives)
                {
                    ((float*)val)[1] = sg->dvdx;
                    ((float*)val)[2] = sg->dvdy;
                }
                return true;
            }
            if (name == h_red && type == TypeDescs.TypeFloat && sg->P.x > 0.5f)
            {
                ((float*)val)[0] = sg->u;
                if (derivatives)
                {
                    ((float*)val)[1] = sg->dudx;
                    ((float*)val)[2] = sg->dudy;
                }
                return true;
            }
            if (name == h_green && type == TypeDescs.TypeFloat && sg->P.x < 0.5f)
            {
                ((float*)val)[0] = sg->v;
                if (derivatives)
                {
                    ((float*)val)[1] = sg->dvdx;
                    ((float*)val)[2] = sg->dvdy;
                }
                return true;
            }
            if (name == h_blue && type == TypeDescs.TypeFloat && (((int)(sg->P.y * 12)) % 2) == 0)
            {
                ((float*)val)[0] = 1.0f - sg->u;
                if (derivatives)
                {
                    ((float*)val)[1] = -sg->dudx;
                    ((float*)val)[2] = -sg->dudy;
                }
                return true;
            }

            string namestr = OslStrings.from_hash(name).@string();
            foreach (UserDataItem item in userdata)
            {
                if (item.name == namestr && item.type == type)
                {
                    int size = item.data.Length;
                    byte* dst = (byte*)val;
                    for (int i = 0; i < size; ++i)
                    {
                        dst[i] = item.data[i];
                    }
                    if (derivatives)
                    {
                        for (int i = 0; i < 2 * size; ++i)
                        {
                            dst[size + i] = 0;
                        }
                    }
                    return true;
                }
            }

            return false;
        }

        public override bool trace(ref TraceOpt options, ShaderGlobals* sg, Vec3 P, Vec3 dPdx, Vec3 dPdy, Vec3 R, Vec3 dRdx, Vec3 dRdy)
        {
            float dot_val = P.x * R.x + P.y * R.y + P.z * R.z;
            if ((sg->u) / dot_val > 0.5f)
            {
                return true;  // 1 in batched
            }
            return false;
        }

        public override bool getmessage(ShaderGlobals* sg, ulong source_, ulong name_, TypeDesc type, void* val, bool derivatives)
        {
            string source = OslStrings.from_hash(source_).@string();
            string name = OslStrings.from_hash(name_).@string();
            if (source != "trace")
            {
                throw new InvalidOperationException("getmessage source must be trace");
            }
            if (sg->u > 0.5f)
            {
                if (name == "hitdist")
                {
                    if (type == TypeDescs.TypeFloat)
                    {
                        *(float*)val = 0.5f;
                    }
                }
                if (name == "hit")
                {
                    if (type == TypeDescs.TypeInt)
                    {
                        *(int*)val = 1;
                    }
                }
                if (name == "geom:name")
                {
                    if (type == TypeDescs.TypeString)
                    {
                        *(ulong*)val = new ustring("teapot").hash();
                    }
                }
                if (name == "N")
                {
                    if (type == TypeDescs.TypeNormal)
                    {
                        *(Vec3*)val = new Vec3(1.0f - sg->v, 0.25f, 1.0f - sg->u);
                    }
                    else
                    {
                        throw new InvalidOperationException("Oops");
                    }
                }
                return true;  // 1 in batched
            }
            else
            {
                if (name == "hit")
                {
                    if (type == TypeDescs.TypeInt)
                    {
                        *(int*)val = 0;
                    }
                }
                return false;
            }
        }

        private bool get_osl_version(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeDescs.TypeInt)
            {
                ((int*)val)[0] = 11600;  // OSL_VERSION
                return true;
            }
            return false;
        }

        private bool get_camera_resolution(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeIntArray2)
            {
                ((int*)val)[0] = m_xres;
                ((int*)val)[1] = m_yres;
                return true;
            }
            return false;
        }

        private bool get_camera_projection(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeDescs.TypeString)
            {
                ((ulong*)val)[0] = m_projection;
                return true;
            }
            return false;
        }

        private static void zero_derivs(bool derivs, TypeDesc type, void* val)
        {
            if (derivs)
            {
                int size = (int)type.size();
                byte* p = (byte*)val + size;
                for (int i = 0; i < 2 * size; ++i)
                {
                    p[i] = 0;
                }
            }
        }

        private bool get_camera_fov(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeDescs.TypeFloat)
            {
                ((float*)val)[0] = m_fov;
                zero_derivs(derivs, type, val);
                return true;
            }
            return false;
        }

        private bool get_camera_pixelaspect(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeDescs.TypeFloat)
            {
                ((float*)val)[0] = m_pixelaspect;
                zero_derivs(derivs, type, val);
                return true;
            }
            return false;
        }

        private bool get_camera_clip(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeFloatArray2)
            {
                ((float*)val)[0] = m_hither;
                ((float*)val)[1] = m_yon;
                zero_derivs(derivs, type, val);
                return true;
            }
            return false;
        }

        private bool get_camera_clip_near(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeDescs.TypeFloat)
            {
                ((float*)val)[0] = m_hither;
                zero_derivs(derivs, type, val);
                return true;
            }
            return false;
        }

        private bool get_camera_clip_far(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeDescs.TypeFloat)
            {
                ((float*)val)[0] = m_yon;
                zero_derivs(derivs, type, val);
                return true;
            }
            return false;
        }

        private bool get_camera_shutter(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeFloatArray2)
            {
                ((float*)val)[0] = m_shutter[0];
                ((float*)val)[1] = m_shutter[1];
                zero_derivs(derivs, type, val);
                return true;
            }
            return false;
        }

        private bool get_camera_shutter_open(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeDescs.TypeFloat)
            {
                ((float*)val)[0] = m_shutter[0];
                zero_derivs(derivs, type, val);
                return true;
            }
            return false;
        }

        private bool get_camera_shutter_close(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeDescs.TypeFloat)
            {
                ((float*)val)[0] = m_shutter[1];
                zero_derivs(derivs, type, val);
                return true;
            }
            return false;
        }

        private bool get_camera_screen_window(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeFloatArray4)
            {
                ((float*)val)[0] = m_screen_window[0];
                ((float*)val)[1] = m_screen_window[1];
                ((float*)val)[2] = m_screen_window[2];
                ((float*)val)[3] = m_screen_window[3];
                zero_derivs(derivs, type, val);
                return true;
            }
            return false;
        }

        public bool add_output(string varname_, string filename, TypeDesc datatype, int nchannels)
        {
            m_outputvars.Add(new ustring(varname_));
            m_outputbufs.Add(new OutputBuffer(varname_, filename, datatype, nchannels, m_xres, m_yres));
            return true;
        }

        public int noutputs()
        {
            return m_outputbufs.Count;
        }

        public OutputBuffer outputbuf(int i)
        {
            return m_outputbufs[i];
        }

        public ustring outputname(int i)
        {
            return m_outputvars[i];
        }

        public static void register_closures(ShadingSystem shadingsys)
        {
            // Describe the memory layout of each closure type to the OSL runtime
            reg(shadingsys, "emission", ClosureIDs.EMISSION_ID, new StructLayout_());
            reg(shadingsys, "background", ClosureIDs.BACKGROUND_ID, new StructLayout_());
            reg(shadingsys, "diffuse", ClosureIDs.DIFFUSE_ID, new StructLayout_().vec().str("label"));
            reg(shadingsys, "oren_nayar", ClosureIDs.OREN_NAYAR_ID, new StructLayout_().vec().f());
            // translucent uses DiffuseParams (N, label) but only N is a closure parameter
            reg(shadingsys, "translucent", ClosureIDs.TRANSLUCENT_ID, new StructLayout_().vec().skip(8, 8));
            reg(shadingsys, "phong", ClosureIDs.PHONG_ID, new StructLayout_().vec().f().str("label"));
            reg(shadingsys, "ward", ClosureIDs.WARD_ID, new StructLayout_().vec().vec().f().f());
            reg(shadingsys, "microfacet", ClosureIDs.MICROFACET_ID, new StructLayout_().str().vec().vec().f().f().f().i());
            reg(shadingsys, "reflection", ClosureIDs.REFLECTION_ID, new StructLayout_().vec().skip(4, 4));
            reg(shadingsys, "reflection", ClosureIDs.FRESNEL_REFLECTION_ID, new StructLayout_().vec().f());
            reg(shadingsys, "refraction", ClosureIDs.REFRACTION_ID, new StructLayout_().vec().f());
            reg(shadingsys, "transparent", ClosureIDs.TRANSPARENT_ID, new StructLayout_());
            reg(shadingsys, "debug", ClosureIDs.DEBUG_ID, new StructLayout_().str());
            reg(shadingsys, "holdout", ClosureIDs.HOLDOUT_ID, new StructLayout_());
            reg(shadingsys, "parameter_test", ClosureIDs.PARAMETER_TEST_ID,
                new StructLayout_().i().f().color().vec().str().i(null, 5).vec(null, 5).color(null, 5).f(null, 5).str(null, 5).i("int_key").f("float_key")
                                   .color("color_key").vec("vector_key").str("string_key"));
        }

        private static void reg(ShadingSystem shadingsys, string name, int id, StructLayout_ layout)
        {
            shadingsys.register_closure(name, id, layout.finish(), null, null);
        }
    }
}
