// OSL port: testrender/simpleraytracer.h and simpleraytracer.cpp (the CPU path tracer; the OptiX parts and the MaterialX media are not ported)
//
// The SimpleRaytracer is the RendererServices of testrender: it parses the XML scene, builds the shader groups, prepares the lights and the background
// importance table, and path traces the image with next event estimation to the background and to the emissive triangles.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using OIIO;
using OSL.TestShade;

namespace OSL
{
    public sealed class Material
    {
        public ShaderGroup surf;
        public ShaderGroup disp;
    }

    /// The error handler of the renderer: errors are remembered and severe errors end the program
    public sealed class RaytracerErrorHandler : ErrorHandler
    {
        public bool verbose;
        public bool had_error;

        public override void handle(int errcode, string msg)
        {
            if (errcode >= (int)ErrorCode.EH_ERROR && errcode < (int)ErrorCode.EH_DEBUG)
            {
                had_error = true;
            }
            if (errcode >= (int)ErrorCode.EH_SEVERE && errcode < (int)ErrorCode.EH_DEBUG)
            {
                Console.Error.Write("SEVERE ERROR: " + msg + "\n");
                Console.Out.Flush();
                Environment.Exit(1);
            }
            else if (errcode >= (int)ErrorCode.EH_ERROR && errcode < (int)ErrorCode.EH_DEBUG)
            {
                Console.Error.Write("ERROR: " + msg + "\n");
            }
            else if (errcode >= (int)ErrorCode.EH_WARNING && errcode < (int)ErrorCode.EH_DEBUG)
            {
                Console.Error.Write("WARNING: " + msg + "\n");
            }
            else if (errcode == (int)ErrorCode.EH_INFO)
            {
                if (verbose)
                {
                    Console.Out.Write("INFO: " + msg + "\n");
                }
            }
            else if (errcode == (int)ErrorCode.EH_DEBUG)
            {
                if (verbose)
                {
                    Console.Out.Write("DEBUG: " + msg + "\n");
                }
            }
            else
            {
                Console.Out.Write(msg);
            }
        }
    }

    public sealed unsafe class SimpleRaytracer : RendererServices
    {
        private delegate bool AttrGetter(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val);

        private static readonly TypeDesc TypeFloatArray2 = new TypeDesc(TypeDesc.FLOAT, 2);
        private static readonly TypeDesc TypeFloatArray4 = new TypeDesc(TypeDesc.FLOAT, 4);
        private static readonly TypeDesc TypeIntArray2 = new TypeDesc(TypeDesc.INT, 2);

        private static readonly ulong h_perspective = new ustring("perspective").hash();
        private static readonly ulong h_raster = new ustring("raster").hash();
        private static readonly ulong h_camera = new ustring("camera").hash();
        private static readonly ulong h_screen = new ustring("screen").hash();
        private static readonly ulong h_NDC = new ustring("NDC").hash();
        private static readonly ulong h_s = new ustring("s").hash();
        private static readonly ulong h_t = new ustring("t").hash();

        public Camera camera = new Camera();
        public Scene scene = new Scene();
        public Background background = new Background();
        public ShadingSystem shadingsys;
        public Dictionary<string, object> options = new Dictionary<string, object>();
        public float[] pixelbuf;

        // Camera parameters
        private Matrix44 m_world_to_camera;
        private ulong m_projection;
        private float m_fov;
        private float m_pixelaspect;
        private float m_hither;
        private float m_yon;
        private float[] m_shutter = new float[2];
        private float[] m_screen_window = new float[4];

        private int backgroundShaderID = -1;
        private int backgroundResolution = 1024;
        private int aa = 1;
        private bool no_jitter;
        private int max_bounces = 1000000;
        private int rr_depth = 5;
        private float show_albedo_scale;
        private int show_globals;
        private List<Material> m_shaders = new List<Material>();
        private List<bool> m_shader_is_light = new List<bool>();
        private List<float> m_mesh_surfacearea = new List<float>();  // surface area of all triangles in each mesh (one entry per mesh)
        private List<uint> m_lightprims = new List<uint>();          // array of all triangles that have a "light" shader on them
        private RaytracerErrorHandler m_errhandler = new RaytracerErrorHandler();
        private Dictionary<ulong, Matrix44> m_named_xforms = new Dictionary<ulong, Matrix44>();
        private Dictionary<ulong, AttrGetter> m_attr_getters = new Dictionary<ulong, AttrGetter>();
        private int m_num_threads;

        public SimpleRaytracer()
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

        public RaytracerErrorHandler errhandler()
        {
            return m_errhandler;
        }

        public int num_threads
        {
            get { return m_num_threads; }
            set { m_num_threads = value; }
        }

        public List<Material> shaders()
        {
            return m_shaders;
        }

        public void attribute(string name, int value)
        {
            if (name.Length == 0)
            {
                return;
            }
            options[name] = value;
        }

        public void attribute(string name, float value)
        {
            if (name.Length == 0)
            {
                return;
            }
            options[name] = value;
        }

        private int get_int(string name)
        {
            object o;
            if (options.TryGetValue(name, out o) && o is int)
            {
                return (int)o;
            }
            return 0;
        }

        private float get_float(string name)
        {
            object o;
            if (options.TryGetValue(name, out o))
            {
                if (o is float)
                {
                    return (float)o;
                }
                if (o is int)
                {
                    return (int)o;
                }
            }
            return 0.0f;
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
            camera.xres = xres;
            camera.yres = yres;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Scene description
        // ------------------------------------------------------------------------------------------------------------------------------------

        private static Vec3 strtovec(string str)
        {
            float x = 0;
            float y = 0;
            float z = 0;
            string_view sv = new string_view(str);
            Strutil.parse_float(ref sv, out x);
            Strutil.parse_char(ref sv, ',');
            Strutil.parse_float(ref sv, out y);
            Strutil.parse_char(ref sv, ',');
            Strutil.parse_float(ref sv, out z);
            return new Vec3(x, y, z);
        }

        private static bool strtobool(string str)
        {
            return str == "1" || str == "on" || str == "yes";
        }

        private static float from_string_float(string s)
        {
            string_view sv = new string_view(s);
            float f;
            if (Strutil.parse_float(ref sv, out f))
            {
                return f;
            }
            return 0.0f;
        }

        private static int from_string_int(string s)
        {
            string_view sv = new string_view(s);
            int i;
            if (Strutil.parse_int(ref sv, out i))
            {
                return i;
            }
            return 0;
        }

        private static bool parse_prefix_and_floats(string str, string prefix, int nvals, float[] vals)
        {
            string_view sv = new string_view(str);
            bool ok = Strutil.parse_prefix(ref sv, prefix);
            for (int i = 0; i < nvals && ok; ++i)
            {
                ok &= Strutil.parse_float(ref sv, out vals[i]);
            }
            return ok;
        }

        private static bool parse_prefix_and_ints(string str, string prefix, int nvals, int[] vals)
        {
            string_view sv = new string_view(str);
            bool ok = Strutil.parse_prefix(ref sv, prefix);
            for (int i = 0; i < nvals && ok; ++i)
            {
                ok &= Strutil.parse_int(ref sv, out vals[i]);
            }
            return ok;
        }

        private static string attr_value(XmlElement node, string name)
        {
            XmlAttribute a = node.GetAttributeNode(name);
            return a == null ? null : a.Value;
        }

        /// pugixml's node.text().get(): the first character data child
        private static string node_text(XmlElement node)
        {
            foreach (XmlNode c in node.ChildNodes)
            {
                if (c.NodeType == XmlNodeType.Text || c.NodeType == XmlNodeType.CDATA)
                {
                    return c.Value;
                }
            }
            return "";
        }

        public void parse_scene_xml(string scenefile)
        {
            ShaderMap shadermap = new ShaderMap();
            XmlDocument doc = new XmlDocument();
            try
            {
                if (Strutil.ends_with(scenefile, ".xml") && File.Exists(scenefile))
                {
                    XmlReaderSettings settings = new XmlReaderSettings();
                    settings.DtdProcessing = DtdProcessing.Ignore;
                    using (XmlReader reader = XmlReader.Create(scenefile, settings))
                    {
                        doc.Load(reader);
                    }
                }
                else
                {
                    doc.LoadXml(scenefile);
                }
            }
            catch (XmlException e)
            {
                errhandler().severefmt("XML parsed with errors: {} at offset {}", e.Message, 0);
            }
            XmlElement root = doc.DocumentElement;
            if (root == null || root.Name != "World")
            {
                errhandler().severefmt("Error reading scene: Root element <World> is missing");
                return;
            }

            // loop over all children of world
            foreach (XmlNode child in root.ChildNodes)
            {
                XmlElement node = child as XmlElement;
                if (node == null)
                {
                    continue;
                }
                if (node.Name == "Option")
                {
                    foreach (XmlAttribute attr in node.Attributes)
                    {
                        int[] i = new int[1];
                        if (parse_prefix_and_ints(attr.Value, "int ", 1, i))
                        {
                            attribute(attr.Name, i[0]);
                        }
                    }
                }
                else if (node.Name == "Camera")
                {
                    // defaults
                    Vec3 eye = new Vec3(0, 0, 0);
                    Vec3 dir = new Vec3(0, 0, -1);
                    Vec3 up = new Vec3(0, 1, 0);
                    float fov = 90.0f;

                    // load camera (only first attribute counts if duplicates)
                    string eye_attr = attr_value(node, "eye");
                    string dir_attr = attr_value(node, "dir");
                    string at_attr = attr_value(node, "look_at");
                    string up_attr = attr_value(node, "up");
                    string fov_attr = attr_value(node, "fov");
                    if (eye_attr != null)
                    {
                        eye = strtovec(eye_attr);
                    }
                    if (dir_attr != null)
                    {
                        dir = strtovec(dir_attr);
                    }
                    else if (at_attr != null)
                    {
                        dir = strtovec(at_attr) - eye;
                    }
                    if (up_attr != null)
                    {
                        up = strtovec(up_attr);
                    }
                    if (fov_attr != null)
                    {
                        fov = from_string_float(fov_attr);
                    }
                    camera.lookat(eye, dir, up, fov);
                }
                else if (node.Name == "Sphere")
                {
                    string center_attr = attr_value(node, "center");
                    string radius_attr = attr_value(node, "radius");
                    if (center_attr != null && radius_attr != null)
                    {
                        Vec3 center = strtovec(center_attr);
                        float radius = from_string_float(radius_attr);
                        if (radius > 0)
                        {
                            string resolution_attr = attr_value(node, "resolution");
                            int resolution = 64;
                            if (resolution_attr != null)
                            {
                                string_view str = new string_view(resolution_attr);
                                Strutil.parse_int(ref str, out resolution);
                            }
                            scene.add_sphere(center, radius, shaders().Count - 1, resolution);
                        }
                    }
                }
                else if (node.Name == "Quad")
                {
                    string corner_attr = attr_value(node, "corner");
                    string edge_x_attr = attr_value(node, "edge_x");
                    string edge_y_attr = attr_value(node, "edge_y");
                    if (corner_attr != null && edge_x_attr != null && edge_y_attr != null)
                    {
                        Vec3 co = strtovec(corner_attr);
                        Vec3 ex = strtovec(edge_x_attr);
                        Vec3 ey = strtovec(edge_y_attr);
                        int resolution = 1;
                        string resolution_attr = attr_value(node, "resolution");
                        if (resolution_attr != null)
                        {
                            string_view str = new string_view(resolution_attr);
                            Strutil.parse_int(ref str, out resolution);
                        }
                        scene.add_quad(co, ex, ey, shaders().Count - 1, resolution);
                    }
                }
                else if (node.Name == "Model")
                {
                    string filename_attr = attr_value(node, "filename");
                    if (filename_attr != null)
                    {
                        List<string> searchpath = new List<string>();
                        string parent = Path.GetDirectoryName(scenefile);
                        searchpath.Add(parent == null ? "" : parent);
                        string actual_filename = Filesystem.searchpath_find(filename_attr, searchpath, false);
                        if (actual_filename.Length == 0)
                        {
                            errhandler().errorfmt("Unable to find model file {}", filename_attr);
                        }
                        else
                        {
                            // we got a valid filename, try to load the model
                            scene.add_model(actual_filename, shadermap, shaders().Count - 1, errhandler());
                        }
                    }
                }
                else if (node.Name == "Background")
                {
                    string res_attr = attr_value(node, "resolution");
                    if (res_attr != null)
                    {
                        backgroundResolution = from_string_int(res_attr);
                    }
                    backgroundShaderID = shaders().Count - 1;
                }
                else if (node.Name == "ShaderGroup")
                {
                    if (parse_shader_group(node, shadermap))
                    {
                        continue;
                    }
                }
            }
            if (shaders().Count == 0)
            {
                errhandler().severefmt("No shaders in scene");
            }
            if (scene.num_prims() == 0)
            {
                errhandler().severefmt("No primitives in scene");
            }
            camera.finalize();
        }

        /// Builds one shader group. Returns true when it only updated an existing material (the "continue" of the C++ loop).
        private bool parse_shader_group(XmlElement node, ShaderMap shadermap)
        {
            string is_light_attr = attr_value(node, "is_light");
            string name_attr = attr_value(node, "name");
            string name = name_attr != null ? name_attr : "group";
            string type_attr = attr_value(node, "type");
            string shadertype = type_attr != null ? type_attr : "surface";
            string commands_attr = attr_value(node, "commands");
            string commands = commands_attr != null ? commands_attr : node_text(node);
            ShaderGroup group;
            if (commands.Length != 0)
            {
                group = shadingsys.ShaderGroupBegin(name, shadertype, commands);
            }
            else
            {
                group = shadingsys.ShaderGroupBegin(name);
            }
            foreach (XmlNode gchild in node.ChildNodes)
            {
                XmlElement gnode = gchild as XmlElement;
                if (gnode == null)
                {
                    continue;
                }
                if (gnode.Name == "Parameter")
                {
                    // handle parameters
                    foreach (XmlAttribute attr in gnode.Attributes)
                    {
                        int[] i = new int[1];
                        float[] f = new float[3];
                        string val = attr.Value;
                        if (parse_prefix_and_ints(val, "int ", 1, i))
                        {
                            shadingsys.Parameter(group, attr.Name, TypeDescs.TypeInt, new int[] { i[0] });
                        }
                        else if (parse_prefix_and_floats(val, "float ", 1, f))
                        {
                            shadingsys.Parameter(group, attr.Name, TypeDescs.TypeFloat, new float[] { f[0] });
                        }
                        else if (parse_prefix_and_floats(val, "vector ", 3, f))
                        {
                            shadingsys.Parameter(group, attr.Name, TypeDescs.TypeVector, new float[] { f[0], f[1], f[2] });
                        }
                        else if (parse_prefix_and_floats(val, "point ", 3, f))
                        {
                            shadingsys.Parameter(group, attr.Name, TypeDescs.TypePoint, new float[] { f[0], f[1], f[2] });
                        }
                        else if (parse_prefix_and_floats(val, "color ", 3, f))
                        {
                            shadingsys.Parameter(group, attr.Name, TypeDescs.TypeColor, new float[] { f[0], f[1], f[2] });
                        }
                        else
                        {
                            shadingsys.Parameter(group, attr.Name, TypeDescs.TypeString, new string[] { val });
                        }
                    }
                }
                else if (gnode.Name == "Shader")
                {
                    string gtype_attr = attr_value(gnode, "type");
                    string gname_attr = attr_value(gnode, "name");
                    string layer_attr = attr_value(gnode, "layer");
                    string gtype = gtype_attr != null ? gtype_attr : "surface";
                    if (gname_attr != null && layer_attr != null)
                    {
                        shadingsys.Shader(group, gtype, gname_attr, layer_attr);
                    }
                }
                else if (gnode.Name == "ConnectShaders")
                {
                    string sl = attr_value(gnode, "srclayer");
                    string sp = attr_value(gnode, "srcparam");
                    string dl = attr_value(gnode, "dstlayer");
                    string dp = attr_value(gnode, "dstparam");
                    if (sl != null && sp != null && dl != null && dp != null)
                    {
                        shadingsys.ConnectShaders(group, sl, sp, dl, dp);
                    }
                }
            }
            shadingsys.ShaderGroupEnd(group);
            if (name_attr != null)
            {
                int shaderID;
                if (shadermap.TryGetValue(name, out shaderID))
                {
                    if (shaderID >= 0 && shaderID < shaders().Count)
                    {
                        Console.Error.Write("Updating shader " + shaderID.ToString(CultureInfo.InvariantCulture) + " - " + shadertype + "\n");
                        // we already have a material under this name,
                        Material m = shaders()[shaderID];
                        if (shadertype == "displacement")
                        {
                            m.disp = group;
                        }
                        else if (shadertype == "surface")
                        {
                            m.surf = group;
                        }
                        // skip the rest which would add a new material
                        return true;
                    }
                }
                else
                {
                    shadermap[name] = shaders().Count;
                }
            }
            Material material = new Material();
            material.surf = group;
            shaders().Add(material);
            m_shader_is_light.Add(is_light_attr != null ? strtobool(is_light_attr) : false);
            return false;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // RendererServices
        // ------------------------------------------------------------------------------------------------------------------------------------

        public override bool get_matrix(ShaderGlobals* sg, out Matrix44 result, TransformationPtr xform, float time)
        {
            // SimpleRaytracer doesn't understand motion blur and transformations are just simple 4x4 matrices.
            if (xform.ptr != null)
            {
                result = *(Matrix44*)xform.ptr;
            }
            else
            {
                result = Matrix44.identity;
            }
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
            if (xform.ptr != null)
            {
                result = *(Matrix44*)xform.ptr;
            }
            else
            {
                result = Matrix44.identity;
            }
            return true;
        }

        public override bool get_matrix(ShaderGlobals* sg, out Matrix44 result, ulong from)
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
                            Matrix44 ndc_to_raster = new Matrix44(camera.xres, 0, 0, 0, 0, camera.yres, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
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

        public void name_transform(string name, Matrix44 xform)
        {
            m_named_xforms[new ustring(name).hash()] = xform;
        }

        public override bool get_array_attribute(ShaderGlobals* sg, bool derivatives, ulong obj, TypeDesc type, ulong name, int index, void* val)
        {
            AttrGetter getter;
            if (m_attr_getters.TryGetValue(name, out getter))
            {
                return getter(sg, derivatives, obj, type, name, val);
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
            return false;
        }

        private static void zero_derivs(void* val, TypeDesc type)
        {
            byte* p = (byte*)val + type.size();
            for (int i = 0; i < 2 * type.size(); ++i)
            {
                p[i] = 0;
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
                ((int*)val)[0] = camera.xres;
                ((int*)val)[1] = camera.yres;
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

        private bool get_float_attr(float value, bool derivs, TypeDesc type, void* val)
        {
            if (type == TypeDescs.TypeFloat)
            {
                ((float*)val)[0] = value;
                if (derivs)
                {
                    zero_derivs(val, type);
                }
                return true;
            }
            return false;
        }

        private bool get_camera_fov(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            return get_float_attr(m_fov, derivs, type, val);
        }

        private bool get_camera_pixelaspect(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            return get_float_attr(m_pixelaspect, derivs, type, val);
        }

        private bool get_camera_clip(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeFloatArray2)
            {
                ((float*)val)[0] = m_hither;
                ((float*)val)[1] = m_yon;
                if (derivs)
                {
                    zero_derivs(val, type);
                }
                return true;
            }
            return false;
        }

        private bool get_camera_clip_near(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            return get_float_attr(m_hither, derivs, type, val);
        }

        private bool get_camera_clip_far(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            return get_float_attr(m_yon, derivs, type, val);
        }

        private bool get_camera_shutter(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            if (type == TypeFloatArray2)
            {
                ((float*)val)[0] = m_shutter[0];
                ((float*)val)[1] = m_shutter[1];
                if (derivs)
                {
                    zero_derivs(val, type);
                }
                return true;
            }
            return false;
        }

        private bool get_camera_shutter_open(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            return get_float_attr(m_shutter[0], derivs, type, val);
        }

        private bool get_camera_shutter_close(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            return get_float_attr(m_shutter[1], derivs, type, val);
        }

        private bool get_camera_screen_window(ShaderGlobals* sg, bool derivs, ulong obj, TypeDesc type, ulong name, void* val)
        {
            // N.B. in a real renderer, this may be time-dependent
            if (type == TypeFloatArray4)
            {
                ((float*)val)[0] = m_screen_window[0];
                ((float*)val)[1] = m_screen_window[1];
                ((float*)val)[2] = m_screen_window[2];
                ((float*)val)[3] = m_screen_window[3];
                if (derivs)
                {
                    zero_derivs(val, type);
                }
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // CPU renderer helpers
        // ------------------------------------------------------------------------------------------------------------------------------------

        private int mesh_of(int id)
        {
            // std::upper_bound on the last index of each mesh
            int lo = 0;
            int len = scene.last_index.Count;
            while (len != 0)
            {
                int l2 = len / 2;
                if (id < scene.last_index[lo + l2])
                {
                    len = l2;
                }
                else
                {
                    lo += l2 + 1;
                    len -= l2 + 1;
                }
            }
            return lo;
        }

        private void globals_from_hit(out ShaderGlobals sg, Ray r, float t, int id, float u, float v)
        {
            sg = new ShaderGlobals();
            int meshid = mesh_of(id);
            DualVec3 direction = r.dual_direction();
            sg.I = direction.val;
            sg.dIdx = direction.dx;
            sg.dIdy = direction.dy;
            DualVec3 P = r.point_dual(t);
            sg.P = P.val;
            Vec3 Ng;
            sg.N = scene.normal(P, out Ng, id, u, v);
            sg.Ng = Ng;
            // Projecting onto the surface here
            scene.project(ref P, sg.N, sg.I);
            sg.dPdx = P.dx;
            sg.dPdy = P.dy;
            Vec3 dPdu = sg.dPdu;
            Vec3 dPdv = sg.dPdv;
            DualVec2 uv = scene.uv(P, sg.N, ref dPdu, ref dPdv, id, u, v);
            sg.dPdu = dPdu;
            sg.dPdv = dPdv;
            sg.u = uv.val.x;
            sg.dudx = uv.dx.x;
            sg.dudy = uv.dy.x;
            sg.v = uv.val.y;
            sg.dvdx = uv.dx.y;
            sg.dvdy = uv.dy.y;
            sg.surfacearea = m_mesh_surfacearea[meshid];
            sg.backfacing = sg.Ng.dot(sg.I) > 0 ? 1 : 0;
            if (sg.backfacing != 0)
            {
                sg.N = -sg.N;
                sg.Ng = -sg.Ng;
            }
            sg.raytype = r.raytype;
            sg.flipHandedness = sg.dPdx.cross(sg.dPdy).dot(sg.N) < 0 ? 1 : 0;
        }

        private Vec3 eval_background(DualVec3 dir, ShadingContext ctx, int bounce = -1)
        {
            ShaderGlobals sg = new ShaderGlobals();
            sg.I = dir.val;
            sg.dIdx = dir.dx;
            sg.dIdy = dir.dy;
            if (bounce >= 0)
            {
                sg.raytype = bounce > 0 ? Ray.DIFFUSE : Ray.CAMERA;
            }
            shadingsys.execute(ctx, m_shaders[backgroundShaderID].surf, ref sg);
            return ShadingClosures.process_background_closure(sg.Ci);
        }

        private Color3 subpixel_radiance(float x, float y, Sampler sampler, ShadingContext ctx)
        {
            const float inf = float.PositiveInfinity;
            Ray r = camera.get(x, y);
            Color3 path_weight = new Color3(1, 1, 1);
            Color3 path_radiance = new Color3(0, 0, 0);
            uint prev_id = ~0u;
            float bsdf_pdf = inf;  // camera ray has only one possible direction
            MediumStack medium_stack = new MediumStack();

            for (int b = 0; b <= max_bounces; b++)
            {
                ShaderGlobals sg;

                // trace the ray against the scene
                Intersection hit = scene.intersect(r, inf, prev_id);
                if (hit.t == inf)
                {
                    // we hit nothing? check background shader
                    if (backgroundShaderID >= 0)
                    {
                        if (b > 0 && backgroundResolution > 0)
                        {
                            float bg_pdf = 0;
                            Vec3 bg = background.eval(r.direction, out bg_pdf);
                            path_radiance += path_weight * new Color3(bg) * MIS.power_heuristic(MIS.MISMode.WEIGHT_WEIGHT, bsdf_pdf, bg_pdf);
                        }
                        else
                        {
                            // we aren't importance sampling the background - so just run it directly
                            DualVec3 dir = r.dual_direction();
                            path_radiance += path_weight * new Color3(eval_background(dir, ctx, b));
                        }
                    }
                    break;
                }

                if (medium_stack.integrate(r, sampler, ref hit, ref path_weight, ref path_radiance, ref bsdf_pdf))
                {
                    continue;
                }

                // construct a shader globals for the hit point
                globals_from_hit(out sg, r, hit.t, (int)hit.id, hit.u, hit.v);

                if (show_globals != 0)
                {
                    // visualize the main fields of the shader globals
                    Vec3 v = sg.Ng;
                    if (show_globals == 2)
                    {
                        v = sg.N;
                    }
                    if (show_globals == 3)
                    {
                        v = V3.normalize(sg.dPdu);
                    }
                    if (show_globals == 4)
                    {
                        v = V3.normalize(sg.dPdv);
                    }
                    if (show_globals == 5)
                    {
                        v = new Vec3(sg.u, sg.v, 0);
                    }
                    Color3 c = new Color3(v.x, v.y, v.z);
                    if (show_globals != 5)
                    {
                        c = c * 0.5f + new Color3(0.5f);
                    }
                    path_radiance += path_weight * c;
                    break;
                }

                float radius = r.radius + r.spread * hit.t;

                int shaderID = scene.shaderid((int)hit.id);
                if (shaderID < 0 || m_shaders[shaderID].surf == null)
                {
                    break;  // no shader attached? done
                }

                // execute shader and process the resulting list of closures
                shadingsys.execute(ctx, m_shaders[shaderID].surf, ref sg);
                ShadingResult result = new ShadingResult();
                bool last_bounce = b == max_bounces;
                ShadingClosures.process_closure(&sg, r.roughness, result, medium_stack, sg.Ci, last_bounce);

                int lightprims_size = m_lightprims.Count;

                // add self-emission
                float k = 1;
                if (m_shader_is_light[shaderID] && lightprims_size > 0)
                {
                    float light_pick_pdf = 1.0f / lightprims_size;
                    // figure out the probability of reaching this point
                    float light_pdf = light_pick_pdf * scene.shapepdf((int)hit.id, r.origin, sg.P);
                    k = MIS.power_heuristic(MIS.MISMode.WEIGHT_EVAL, bsdf_pdf, light_pdf);
                }
                path_radiance += path_weight * k * result.Le;

                // last bounce? nothing left to do
                if (last_bounce)
                {
                    break;
                }

                // build internal pdf for sampling between bsdf closures
                result.bsdf.prepare(-sg.I, path_weight, b >= rr_depth);

                if (show_albedo_scale > 0)
                {
                    // Instead of path tracing, just visualize the albedo of the bsdf. This can be used to validate the accuracy of the get_albedo method
                    // for a particular bsdf.
                    path_radiance += path_weight * result.bsdf.get_albedo(-sg.I) * show_albedo_scale;
                    break;
                }

                // get three random numbers
                Vec3 s = sampler.get();
                float xi = s.x;
                float yi = s.y;
                float zi = s.z;

                // trace one ray to the background
                if (backgroundResolution > 0)
                {
                    DualVec3 bg_dir;
                    float bg_pdf = 0;
                    Vec3 bg = background.sample(xi, yi, out bg_dir, out bg_pdf);
                    BsdfSample bs = result.bsdf.eval(-sg.I, bg_dir.val);
                    Color3 contrib = path_weight * bs.weight * new Color3(bg) * MIS.power_heuristic(MIS.MISMode.WEIGHT_WEIGHT, bg_pdf, bs.pdf);
                    if ((contrib.x + contrib.y + contrib.z) > 0)
                    {
                        Ray shadow_ray = new Ray(sg.P, bg_dir.val, radius, 0, 0, Ray.SHADOW);
                        Intersection shadow_hit = scene.intersect(shadow_ray, inf, hit.id);
                        if (shadow_hit.t == inf)  // ray reached the background?
                        {
                            path_radiance += contrib;
                        }
                    }
                }

                // trace a shadow ray to one of the light emitting primitives
                if (lightprims_size > 0)
                {
                    float light_pick_pdf = 1.0f / lightprims_size;

                    // uniform probability for each light
                    float xl = xi * lightprims_size;
                    int ls = (int)MathF.Floor(xl);
                    xl -= ls;

                    uint lid = m_lightprims[ls];
                    if (lid != hit.id)
                    {
                        int lightShaderID = scene.shaderid((int)lid);

                        // sample a random direction towards the object
                        LightSample sample = scene.sample((int)lid, sg.P, xl, yi);
                        BsdfSample bs = result.bsdf.eval(-sg.I, sample.dir);
                        Color3 contrib = path_weight * bs.weight * MIS.power_heuristic(MIS.MISMode.EVAL_WEIGHT, light_pick_pdf * sample.pdf, bs.pdf);
                        if ((contrib.x + contrib.y + contrib.z) > 0)
                        {
                            Ray shadow_ray = new Ray(sg.P, sample.dir, radius, 0, 0, Ray.SHADOW);
                            // trace a shadow ray and see if we actually hit the target; in this tiny renderer, tracing a ray is probably cheaper than
                            // evaluating the light shader
                            Intersection shadow_hit = scene.intersect(shadow_ray, sample.dist, hit.id, lid);
                            bool did_hit = shadow_hit.t == sample.dist;
                            if (did_hit)
                            {
                                // setup a shader global for the point on the light
                                ShaderGlobals light_sg;
                                globals_from_hit(out light_sg, shadow_ray, sample.dist, (int)lid, sample.u, sample.v);
                                // execute the light shader (for emissive closures only)
                                shadingsys.execute(ctx, m_shaders[lightShaderID].surf, ref light_sg);
                                ShadingResult light_result = new ShadingResult();
                                ShadingClosures.process_closure(&light_sg, r.roughness, light_result, medium_stack, light_sg.Ci, true);
                                // accumulate contribution
                                path_radiance += contrib * light_result.Le;
                            }
                        }
                    }
                }

                // trace indirect ray and continue
                BsdfSample p = result.bsdf.sample(-sg.I, xi, yi, zi);
                path_weight *= p.weight;
                bsdf_pdf = p.pdf;
                r.raytype = Ray.DIFFUSE;  // FIXME? Use DIFFUSE for all indirect rays
                r.direction = p.wi;
                r.radius = radius;
                // Just simply use roughness as spread slope
                r.spread = MathF.Max(r.spread, p.roughness);
                r.roughness = p.roughness;

                // has the sampled dir crossed the surface?
                bool transmitted = sg.Ng.dot(p.wi) < 0;
                if (transmitted)
                {
                    if (sg.backfacing == 0)
                    {
                        // entering and crossing the surface
                        medium_stack.add_medium(result.medium_data);
                    }
                    else
                    {
                        medium_stack.pop_medium();
                    }
                }

                if (!(path_weight.x > 0) && !(path_weight.y > 0) && !(path_weight.z > 0))
                {
                    break;  // filter out all 0's or NaNs
                }
                prev_id = hit.id;
                r.origin = sg.P;
            }
            return path_radiance;
        }

        private Color3 antialias_pixel(int x, int y, ShadingContext ctx)
        {
            Color3 result = new Color3(0, 0, 0);
            for (int si = 0, n = aa * aa; si < n; si++)
            {
                Sampler sampler = new Sampler(x, y, si);
                // jitter pixel coordinate [0,1)^2
                Vec3 j = no_jitter ? new Vec3(0.5f, 0.5f, 0) : sampler.get();
                // warp distribution to approximate a tent filter [-1,+1)^2
                j.x *= 2;
                j.x = j.x < 1 ? MathF.Sqrt(j.x) - 1 : 1 - MathF.Sqrt(2 - j.x);
                j.y *= 2;
                j.y = j.y < 1 ? MathF.Sqrt(j.y) - 1 : 1 - MathF.Sqrt(2 - j.y);
                // trace eye ray (apply jitter from center of the pixel)
                Color3 r = subpixel_radiance(x + 0.5f + j.x, y + 0.5f + j.y, sampler, ctx);
                // mix in result via lerp for numerical stability
                float t = 1.0f / (si + 1);
                result = new Color3(fmath.lerp(result.x, r.x, t), fmath.lerp(result.y, r.y, t), fmath.lerp(result.z, r.z, t));
            }
            return result;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Preparation and rendering
        // ------------------------------------------------------------------------------------------------------------------------------------

        public void prepare_render()
        {
            // Retrieve and validate options
            aa = Math.Max(1, get_int("aa"));
            no_jitter = get_int("no_jitter") != 0;
            max_bounces = get_int("max_bounces");
            rr_depth = get_int("rr_depth");
            show_albedo_scale = get_float("show_albedo_scale");
            show_globals = get_int("show_globals");

            // prepare background importance table (if requested)
            if (backgroundResolution > 0 && backgroundShaderID >= 0)
            {
                // get a context so we can make several background shader calls
                PerThreadInfo thread_info = shadingsys.create_thread_info();
                ShadingContext ctx = shadingsys.get_context(thread_info);

                // build importance table to optimize background sampling
                background.prepare(backgroundResolution, (DualVec3 dir) => eval_background(dir, ctx));

                // release context
                shadingsys.release_context(ctx);
                shadingsys.destroy_thread_info(thread_info);
            }
            else
            {
                // we aren't directly evaluating the background
                backgroundResolution = 0;
            }

            prepare_geometry();

            // build bvh and prepare triangles
            scene.prepare(errhandler());
            prepare_lights();
        }

        public void prepare_lights()
        {
            // measure the total surface area of each mesh
            int first_index = 0;
            foreach (int last_index in scene.last_index)
            {
                float area = 0;
                for (int index = first_index; index < last_index; index++)
                {
                    area += scene.primitivearea(index);
                }
                m_mesh_surfacearea.Add(area);
                first_index = last_index;
            }
            // collect all light emitting triangles
            for (int t = 0, n = scene.num_prims(); t < n; t++)
            {
                int shaderID = scene.shaderid(t);
                if (shaderID < 0 || m_shaders[shaderID].surf == null)
                {
                    continue;  // no shader attached
                }
                if (m_shader_is_light[shaderID])
                {
                    m_lightprims.Add((uint)t);
                }
            }
            if (m_lightprims.Count != 0)
            {
                errhandler().infofmt("Found {} triangles to be treated as lights", m_lightprims.Count);
            }
        }

        public void prepare_geometry()
        {
            bool have_displacement = false;
            foreach (Material m in shaders())
            {
                if (m.disp != null)
                {
                    have_displacement = true;
                    break;
                }
            }
            if (!have_displacement)
            {
                return;
            }
            errhandler().infofmt("Evaluating displacement shaders");
            // Loop through all triangles and run displacement shader if there is one or copy the input point if there is none
            Vec3[] disp_verts = new Vec3[scene.verts.Count];
            int[] valance = new int[scene.verts.Count];  // number of times each vertex has been displaced

            PerThreadInfo thread_info = shadingsys.create_thread_info();
            ShadingContext ctx = shadingsys.get_context(thread_info);

            bool has_smooth_normals = false;
            for (int primID = 0, nprims = scene.triangles.Count; primID < nprims; primID++)
            {
                Vec3[] p = new Vec3[3];
                Vec3[] n = new Vec3[3];
                Vec2f[] uv = new Vec2f[3];

                TriangleIndices tri = scene.triangles[primID];
                p[0] = scene.verts[tri.a];
                p[1] = scene.verts[tri.b];
                p[2] = scene.verts[tri.c];

                valance[tri.a]++;
                valance[tri.b]++;
                valance[tri.c]++;

                int shaderID = scene.shaderid(primID);
                if (shaderID < 0 || m_shaders[shaderID].disp == null)
                {
                    disp_verts[tri.a] += p[0];
                    disp_verts[tri.b] += p[1];
                    disp_verts[tri.c] += p[2];
                    continue;
                }

                Vec3 Ng = (p[0] - p[1]).cross(p[0] - p[2]);
                float area = 0.5f * Ng.length();
                Ng = V3.normalize(Ng);
                if (scene.n_triangles[primID].a >= 0)
                {
                    n[0] = scene.normals[scene.n_triangles[primID].a];
                    n[1] = scene.normals[scene.n_triangles[primID].b];
                    n[2] = scene.normals[scene.n_triangles[primID].c];
                    has_smooth_normals = true;
                }
                else
                {
                    n[0] = n[1] = n[2] = Ng;
                }

                if (scene.uv_triangles[primID].a >= 0)
                {
                    uv[0] = scene.uvs[scene.uv_triangles[primID].a];
                    uv[1] = scene.uvs[scene.uv_triangles[primID].b];
                    uv[2] = scene.uvs[scene.uv_triangles[primID].c];
                }
                else
                {
                    uv[0] = uv[1] = uv[2] = new Vec2f(0, 0);
                }

                // displace each vertex
                for (int i = 0; i < 3; i++)
                {
                    ShaderGlobals sg = new ShaderGlobals();
                    sg.P = p[i];
                    sg.Ng = Ng;
                    sg.N = n[i];
                    sg.u = uv[i].x;
                    sg.v = uv[i].y;
                    sg.I = V3.normalize(p[i] - camera.eye);
                    sg.surfacearea = area;

                    shadingsys.execute(ctx, m_shaders[shaderID].disp, ref sg);

                    p[i] = sg.P;
                }
                disp_verts[tri.a] += p[0];
                disp_verts[tri.b] += p[1];
                disp_verts[tri.c] += p[2];
            }

            // release context
            shadingsys.release_context(ctx);
            shadingsys.destroy_thread_info(thread_info);

            // average each vertex by the number of times it was displaced
            for (int i = 0, n = scene.verts.Count; i < n; i++)
            {
                if (valance[i] > 0)
                {
                    float inv = valance[i];
                    disp_verts[i] = new Vec3(disp_verts[i].x / inv, disp_verts[i].y / inv, disp_verts[i].z / inv);
                }
                else
                {
                    disp_verts[i] = scene.verts[i];
                }
            }
            // replace old data with the new
            scene.verts = new List<Vec3>(disp_verts);

            if (has_smooth_normals)
            {
                // Recompute the vertex normals (if we had some)
                Vec3[] disp_normals = new Vec3[scene.normals.Count];
                for (int primID = 0, nprims = scene.triangles.Count; primID < nprims; primID++)
                {
                    if (scene.n_triangles[primID].a >= 0)
                    {
                        Vec3[] p = new Vec3[3];
                        p[0] = scene.verts[scene.triangles[primID].a];
                        p[1] = scene.verts[scene.triangles[primID].b];
                        p[2] = scene.verts[scene.triangles[primID].c];
                        // don't normalize to weight by area
                        Vec3 Ng = (p[0] - p[1]).cross(p[0] - p[2]);
                        disp_normals[scene.n_triangles[primID].a] += Ng;
                        disp_normals[scene.n_triangles[primID].b] += Ng;
                        disp_normals[scene.n_triangles[primID].c] += Ng;
                    }
                }
                for (int i = 0; i < disp_normals.Length; i++)
                {
                    disp_normals[i] = V3.normalize(disp_normals[i]);
                }
                scene.normals = new List<Vec3>(disp_normals);
            }
        }

        public void render(int xres, int yres)
        {
            DateTime start = DateTime.UtcNow;
            int nthreads = m_num_threads > 0 ? m_num_threads : Environment.ProcessorCount;
            int chunks = Math.Min(yres, nthreads);
            ParallelOptions po = new ParallelOptions();
            po.MaxDegreeOfParallelism = nthreads;
            Parallel.For(0, chunks, po, (int chunk) =>
            {
                int ybegin = (int)((long)yres * chunk / chunks);
                int yend = (int)((long)yres * (chunk + 1) / chunks);
                // Request an OSL::PerThreadInfo for this thread.
                PerThreadInfo thread_info = shadingsys.create_thread_info();
                // Request a shading context so that we can execute the shader. We could get_context/release_context for each shading point, but to save
                // overhead, it's more efficient to reuse a context within a thread.
                ShadingContext ctx = shadingsys.get_context(thread_info);
                for (int y = ybegin; y < yend; y++)
                {
                    for (int x = 0; x < xres; x++)
                    {
                        Color3 c = antialias_pixel(x, y, ctx);
                        int index = (y * xres + x) * 3;
                        pixelbuf[index] = c.x;
                        pixelbuf[index + 1] = c.y;
                        pixelbuf[index + 2] = c.z;
                    }
                }
                // We're done shading with this context.
                shadingsys.release_context(ctx);
                shadingsys.destroy_thread_info(thread_info);
            });
            double rendertime = (DateTime.UtcNow - start).TotalSeconds;
            errhandler().infofmt("Rendered {}x{} image with {} samples in {}", xres, yres, aa * aa, Strutil.timeintervalformat(rendertime, 2));
        }

        public void clear()
        {
            shaders().Clear();
        }
    }
}
