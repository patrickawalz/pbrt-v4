// OSL port: liboslexec/pointcloud.cpp and pointcloud.h
//
// The point cloud services of the default RendererServices. The C++ keeps its clouds in Partio; this port holds them in memory (one PointCloud per
// file name, shared by all threads) and reads and writes the Houdini ASCII ".geo" form (PGEOMETRY V5) that Partio produces for point clouds, so the
// files are interchangeable with the C++ ones. Clouds written during the run are saved when the process exits (the C++ saves them when its static
// cloud map is destroyed).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OIIO;
using OSL.pvt;

namespace OSL
{
    /// The attribute types Partio has (Partio::ParticleAttributeType)
    public enum PartioType
    {
        NONE = 0, VECTOR = 1, FLOAT = 2, INT = 3, INDEXEDSTR = 4
    }

    /// One attribute of a point cloud: the values of all the particles, count values each
    public sealed class PartioAttribute
    {
        public string name;
        public PartioType type;
        public int count;
        public float[] fdata = new float[0];  // FLOAT and VECTOR
        public int[] idata = new int[0];  // INT, and INDEXEDSTR (index into strings)
        public List<string> strings = new List<string>();

        public int particle_count()
        {
            if (count <= 0)
            {
                return 0;
            }
            return type == PartioType.FLOAT || type == PartioType.VECTOR ? fdata.Length / count : idata.Length / count;
        }

        public void grow(int particles)
        {
            if (type == PartioType.FLOAT || type == PartioType.VECTOR)
            {
                Array.Resize(ref fdata, particles * count);
            }
            else
            {
                Array.Resize(ref idata, particles * count);
            }
        }
    }

    public sealed class PointCloud
    {
        public ulong m_filename;
        public bool m_write;
        public int m_num_particles;
        public List<PartioAttribute> m_all = new List<PartioAttribute>();  // every attribute, in file order; the position first when there is one
        public Dictionary<ulong, PartioAttribute> m_attributes = new Dictionary<ulong, PartioAttribute>();  // by name hash, as the lookups of OSL see them
        public PartioAttribute m_position_attribute;
        public readonly object m_mutex = new object();

        public PointCloud(ulong filename, bool write)
        {
            m_filename = filename;
            m_write = write;
        }

        public PartioAttribute add_attribute(string name, PartioType type, int count)
        {
            PartioAttribute a = new PartioAttribute();
            a.name = name;
            a.type = type;
            a.count = count;
            a.grow(m_num_particles);
            m_all.Add(a);
            return a;
        }

        public int add_particle()
        {
            int p = m_num_particles++;
            for (int i = 0; i < m_all.Count; i++)
            {
                m_all[i].grow(m_num_particles);
            }
            return p;
        }
    }

    public static unsafe class PointCloudStore
    {
        private static readonly Dictionary<ulong, PointCloud> s_clouds = new Dictionary<ulong, PointCloud>();
        private static readonly object s_mutex = new object();
        private static bool s_exit_hook;

        private static ulong hash_of(string s)
        {
            return new ustring(s).hash();
        }

        private static string name_of(ulong hash)
        {
            return OslStrings.from_hash(hash).c_str();
        }

        private static void error(ShaderGlobals* sg, string message)
        {
            if (sg != null)
            {
                ShadingContext ctx = ShaderGlobalsAccess.get_context(sg);
                if (ctx != null)
                {
                    ctx.errorfmt("{}", message);
                }
            }
        }

        public static PointCloud get_cloud(ulong filename, bool write)
        {
            if (filename == hash_of(""))
            {
                return null;
            }
            lock (s_mutex)
            {
                PointCloud found;
                if (s_clouds.TryGetValue(filename, out found))
                {
                    return found;
                }
                PointCloud pc;
                if (!write)
                {
                    pc = read_geo(filename);
                    if (pc == null)
                    {
                        return null;
                    }
                }
                else
                {
                    pc = new PointCloud(filename, true);
                    if (!s_exit_hook)
                    {
                        s_exit_hook = true;
                        AppDomain.CurrentDomain.ProcessExit += (object sender, EventArgs e) => { save_all(); };
                    }
                }
                s_clouds[filename] = pc;
                return pc;
            }
        }

        /// Save every cloud that was written to (what the destruction of the C++ cloud map does at exit)
        public static void save_all()
        {
            lock (s_mutex)
            {
                foreach (KeyValuePair<ulong, PointCloud> kv in s_clouds)
                {
                    PointCloud pc = kv.Value;
                    if (pc.m_write)
                    {
                        string file = name_of(pc.m_filename);
                        if (file.Length > 0)
                        {
                            try
                            {
                                File.WriteAllText(file, format_geo(pc), new UTF8Encoding(false));
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // Houdini ASCII .geo
        // ------------------------------------------------------------------------------------------------------------------------------------

        private static string float_text(float v)
        {
            return Fmt.format_g(v, 6, false, false);
        }

        private static string format_geo(PointCloud pc)
        {
            StringBuilder o = new StringBuilder();
            List<PartioAttribute> user = new List<PartioAttribute>();
            for (int i = 0; i < pc.m_all.Count; i++)
            {
                if (pc.m_all[i] != pc.m_position_attribute)
                {
                    user.Add(pc.m_all[i]);
                }
            }
            int n = pc.m_num_particles;
            o.Append("PGEOMETRY V5\n");
            o.Append("NPoints ").Append(n).Append(" NPrims 1\n");
            o.Append("NPointGroups 0 NPrimGroups 0\n");
            o.Append("NPointAttrib ").Append(user.Count).Append(" NVertexAttrib 0 NPrimAttrib 1 NAttrib 0\n");
            o.Append("PointAttrib\n\n");
            for (int i = 0; i < user.Count; i++)
            {
                PartioAttribute a = user[i];
                o.Append(a.name).Append(' ').Append(a.count).Append(' ');
                switch (a.type)
                {
                    case PartioType.VECTOR:
                        o.Append("vector");
                        for (int c = 0; c < a.count; c++)
                        {
                            o.Append(" 0");
                        }
                        break;
                    case PartioType.FLOAT:
                        o.Append("float");
                        for (int c = 0; c < a.count; c++)
                        {
                            o.Append(" 0");
                        }
                        break;
                    case PartioType.INT:
                        o.Append("int");
                        for (int c = 0; c < a.count; c++)
                        {
                            o.Append(" 0");
                        }
                        break;
                    default:
                        o.Append("index ").Append(a.strings.Count);
                        for (int s = 0; s < a.strings.Count; s++)
                        {
                            o.Append(" \"").Append(a.strings[s].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                        }
                        break;
                }
                o.Append('\n');
            }
            PartioAttribute pos = pc.m_position_attribute;
            for (int p = 0; p < n; p++)
            {
                if (pos != null)
                {
                    o.Append(float_text(pos.fdata[p * 3])).Append(' ').Append(float_text(pos.fdata[p * 3 + 1])).Append(' ')
                     .Append(float_text(pos.fdata[p * 3 + 2])).Append(" 1 (");
                }
                else
                {
                    o.Append("0 0 0 1 (");
                }
                for (int i = 0; i < user.Count; i++)
                {
                    PartioAttribute a = user[i];
                    if (i > 0)
                    {
                        o.Append('\t');
                    }
                    for (int c = 0; c < a.count; c++)
                    {
                        if (c > 0)
                        {
                            o.Append(' ');
                        }
                        if (a.type == PartioType.FLOAT || a.type == PartioType.VECTOR)
                        {
                            o.Append(float_text(a.fdata[p * a.count + c]));
                        }
                        else
                        {
                            o.Append(a.idata[p * a.count + c].ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }
                o.Append(")\n");
            }
            o.Append("PrimitiveAttrib\n");
            o.Append("generator 1 index 1 papi\n");
            o.Append("Part ").Append(n);
            for (int p = 0; p < n; p++)
            {
                o.Append(' ').Append(p);
            }
            o.Append(" [0]\n");
            o.Append("beginExtra\nendExtra\n");
            return o.ToString();
        }

        private static List<string> tokenize(string text)
        {
            List<string> tokens = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                }
                else if (c == '(' || c == ')')
                {
                    tokens.Add(c.ToString());
                    i++;
                }
                else if (c == '"')
                {
                    StringBuilder s = new StringBuilder("\"");
                    i++;
                    while (i < text.Length && text[i] != '"')
                    {
                        if (text[i] == '\\' && i + 1 < text.Length)
                        {
                            i++;
                        }
                        s.Append(text[i]);
                        i++;
                    }
                    i++;
                    tokens.Add(s.ToString());
                }
                else
                {
                    int start = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')')
                    {
                        i++;
                    }
                    tokens.Add(text.Substring(start, i - start));
                }
            }
            return tokens;
        }

        private static PointCloud read_geo(ulong filename)
        {
            string file = name_of(filename);
            string text;
            try
            {
                if (!File.Exists(file))
                {
                    return null;
                }
                text = File.ReadAllText(file);
            }
            catch (Exception)
            {
                return null;
            }
            if (!text.StartsWith("PGEOMETRY"))
            {
                return null;
            }
            List<string> t = tokenize(text);
            int pos = 0;
            int npoints = 0;
            int nattrib = 0;
            // header
            while (pos < t.Count && t[pos] != "PointAttrib")
            {
                if (t[pos] == "NPoints" && pos + 1 < t.Count)
                {
                    npoints = int.Parse(t[pos + 1], CultureInfo.InvariantCulture);
                }
                else if (t[pos] == "NPointAttrib" && pos + 1 < t.Count)
                {
                    nattrib = int.Parse(t[pos + 1], CultureInfo.InvariantCulture);
                }
                pos++;
            }
            if (pos >= t.Count)
            {
                return null;
            }
            pos++;
            PointCloud pc = new PointCloud(filename, false);
            pc.m_position_attribute = new PartioAttribute();
            pc.m_position_attribute.name = "position";
            pc.m_position_attribute.type = PartioType.VECTOR;
            pc.m_position_attribute.count = 3;
            pc.m_all.Add(pc.m_position_attribute);
            List<PartioAttribute> user = new List<PartioAttribute>();
            try
            {
                for (int i = 0; i < nattrib; i++)
                {
                    PartioAttribute a = new PartioAttribute();
                    a.name = t[pos++];
                    a.count = int.Parse(t[pos++], CultureInfo.InvariantCulture);
                    string type = t[pos++];
                    if (type == "vector")
                    {
                        a.type = PartioType.VECTOR;
                        pos += a.count;
                    }
                    else if (type == "float")
                    {
                        a.type = PartioType.FLOAT;
                        pos += a.count;
                    }
                    else if (type == "int")
                    {
                        a.type = PartioType.INT;
                        pos += a.count;
                    }
                    else if (type == "index")
                    {
                        a.type = PartioType.INDEXEDSTR;
                        int nstr = int.Parse(t[pos++], CultureInfo.InvariantCulture);
                        for (int s = 0; s < nstr; s++)
                        {
                            string str = t[pos++];
                            a.strings.Add(str.StartsWith("\"") ? str.Substring(1) : str);
                        }
                    }
                    else
                    {
                        return null;
                    }
                    user.Add(a);
                }
                pc.m_position_attribute.fdata = new float[npoints * 3];
                foreach (PartioAttribute a in user)
                {
                    a.grow(npoints);
                }
                for (int p = 0; p < npoints; p++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        pc.m_position_attribute.fdata[p * 3 + c] = float.Parse(t[pos++], CultureInfo.InvariantCulture);
                    }
                    pos++;  // the homogeneous w
                    if (t[pos] != "(")
                    {
                        return null;
                    }
                    pos++;
                    foreach (PartioAttribute a in user)
                    {
                        for (int c = 0; c < a.count; c++)
                        {
                            if (a.type == PartioType.FLOAT || a.type == PartioType.VECTOR)
                            {
                                a.fdata[p * a.count + c] = float.Parse(t[pos++], CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                a.idata[p * a.count + c] = int.Parse(t[pos++], CultureInfo.InvariantCulture);
                            }
                        }
                    }
                    if (t[pos] != ")")
                    {
                        return null;
                    }
                    pos++;
                }
            }
            catch (Exception)
            {
                return null;
            }
            pc.m_num_particles = npoints;
            foreach (PartioAttribute a in user)
            {
                pc.m_all.Add(a);
            }
            for (int i = 0; i < pc.m_all.Count; i++)
            {
                pc.m_attributes[hash_of(pc.m_all[i].name)] = pc.m_all[i];
            }
            return pc;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------
        // The services
        // ------------------------------------------------------------------------------------------------------------------------------------

        private static PartioType partio_type_of(TypeDesc t)
        {
            if (t.basetype == TypeDesc.FLOAT && t.aggregate == TypeDesc.SCALAR && t.arraylen == 0)
            {
                return PartioType.FLOAT;
            }
            if (t.basetype == TypeDesc.FLOAT && t.aggregate == TypeDesc.VEC3)
            {
                return PartioType.VECTOR;
            }
            if (t.basetype == TypeDesc.INT && t.aggregate == TypeDesc.SCALAR && t.arraylen == 0)
            {
                return PartioType.INT;
            }
            if (t.basetype == TypeDesc.STRING && t.aggregate == TypeDesc.SCALAR && t.arraylen == 0)
            {
                return PartioType.INDEXEDSTR;
            }
            return PartioType.NONE;
        }

        private static TypeDesc typedesc_of(PartioAttribute a)
        {
            TypeDesc type = new TypeDesc();
            switch (a.type)
            {
                case PartioType.INT:
                    type = new TypeDesc(TypeDesc.INT);
                    if (a.count > 1)
                    {
                        type.arraylen = a.count;
                    }
                    break;
                case PartioType.FLOAT:
                    type = new TypeDesc(TypeDesc.FLOAT);
                    if (a.count > 1)
                    {
                        type.arraylen = a.count;
                    }
                    break;
                case PartioType.VECTOR:
                    type = new TypeDesc(TypeDesc.FLOAT, TypeDesc.VEC3, TypeDesc.NOSEMANTICS);
                    if (a.count != 3)
                    {
                        type = new TypeDesc();
                    }
                    break;
                case PartioType.INDEXEDSTR:
                    type = new TypeDesc(TypeDesc.STRING);
                    break;
            }
            return type;
        }

        private static int basevals(TypeDesc t)
        {
            return t.numelements() * t.aggregate;
        }

        private static bool compatible(TypeDesc partio_type, TypeDesc osl_element_type)
        {
            if (TypeDesc.equivalent(partio_type, osl_element_type))
            {
                return true;
            }
            if (partio_type.basetype == osl_element_type.basetype && basevals(partio_type) == basevals(osl_element_type))
            {
                return true;
            }
            if (partio_type.basetype == osl_element_type.basetype && osl_element_type.aggregate == TypeDesc.SCALAR && osl_element_type.arraylen == 0)
            {
                return true;
            }
            return false;
        }

        public static int search(ShaderGlobals* sg, ulong filename, Vec3* center_, float radius, int max_points, bool sort, int* out_indices,
                                 float* out_distances, int derivs_offset)
        {
            if (filename == hash_of(""))
            {
                return 0;
            }
            PointCloud pc = get_cloud(filename, false);
            if (pc == null)
            {
                error(sg, "pointcloud_search: could not open \"" + name_of(filename) + "\"");
                return 0;
            }
            if (pc.m_num_particles == 0)
            {
                return 0;
            }
            PartioAttribute pos_attr = null;
            if (derivs_offset != 0)
            {
                if (!pc.m_attributes.TryGetValue(hash_of("position"), out pos_attr))
                {
                    return 0;
                }
            }
            PartioAttribute positions = pos_attr != null ? pos_attr : pc.m_position_attribute;
            if (positions == null)
            {
                return 0;
            }
            Vec3 center = center_[0];
            float r2 = radius * radius;
            List<KeyValuePair<float, int>> found = new List<KeyValuePair<float, int>>();
            float[] pf = positions.fdata;
            for (int p = 0; p < pc.m_num_particles; p++)
            {
                float dx = pf[p * 3] - center.x;
                float dy = pf[p * 3 + 1] - center.y;
                float dz = pf[p * 3 + 2] - center.z;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (d2 <= r2)
                {
                    found.Add(new KeyValuePair<float, int>(d2, p));
                }
            }
            // The nearest max_points of those, nearest first (ties by particle order)
            found.Sort((KeyValuePair<float, int> a, KeyValuePair<float, int> b) =>
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : a.Value.CompareTo(b.Value);
            });
            int count = Math.Min(found.Count, max_points);
            for (int i = 0; i < count; i++)
            {
                if (out_indices != null)
                {
                    out_indices[i] = found[i].Value;
                }
                if (out_distances != null)
                {
                    out_distances[i] = (float)Math.Sqrt(found[i].Key);
                }
            }
            if (out_distances != null && derivs_offset != 0)
            {
                Vec3 dCdx = center_[1];
                Vec3 dCdy = center_[2];
                float* d_distance_dx = out_distances + derivs_offset;
                float* d_distance_dy = out_distances + derivs_offset * 2;
                for (int i = 0; i < count; i++)
                {
                    int p = found[i].Value;
                    if (out_distances[i] > 0)
                    {
                        float px = pf[p * 3];
                        float py = pf[p * 3 + 1];
                        float pz = pf[p * 3 + 2];
                        d_distance_dx[i] = 1.0f / out_distances[i] * ((center.x - px) * dCdx.x + (center.y - py) * dCdx.y + (center.z - pz) * dCdx.z);
                        d_distance_dy[i] = 1.0f / out_distances[i] * ((center.x - px) * dCdy.x + (center.y - py) * dCdy.y + (center.z - pz) * dCdy.z);
                    }
                    else
                    {
                        d_distance_dx[i] = 0;
                        d_distance_dy[i] = 0;
                    }
                }
            }
            return count;
        }

        public static int get(ShaderGlobals* sg, ulong filename, int* in_indices, int count, ulong attr_name, TypeDesc attr_type, void* out_data)
        {
            if (count == 0)
            {
                return 1;
            }
            PointCloud pc = get_cloud(filename, false);
            if (pc == null)
            {
                error(sg, "pointcloud_get: could not open \"" + name_of(filename) + "\"");
                return 0;
            }
            PartioAttribute attr;
            if (!pc.m_attributes.TryGetValue(attr_name, out attr))
            {
                error(sg, "Accessing unexisting attribute " + name_of(attr_name) + " in pointcloud \"" + name_of(filename) + "\"");
                return 0;
            }
            TypeDesc partio_type = typedesc_of(attr);
            TypeDesc element_type = attr_type.elementtype();
            if (!compatible(partio_type, element_type))
            {
                error(sg, "Type of attribute \"" + name_of(attr_name) + "\" : " + partio_type.ToString() + " not compatible with OSL's "
                          + element_type.ToString() + " in \"" + name_of(filename) + "\" pointcloud");
                return 0;
            }
            int maxn = basevals(attr_type) / basevals(partio_type);
            if (maxn < count)
            {
                error(sg, "Point cloud attribute \"" + name_of(attr_name) + "\" : " + partio_type.ToString() + " with retrieval count " + count
                          + " will not fit in %s");
                count = maxn;
            }
            lock (pc.m_mutex)
            {
                int stride = attr.count;
                if (attr.type == PartioType.INDEXEDSTR)
                {
                    ulong* o = (ulong*)out_data;
                    for (int i = 0; i < count; i++)
                    {
                        int p = in_indices[i];
                        int ind = p >= 0 && p < pc.m_num_particles ? attr.idata[p] : -1;
                        o[i] = ind >= 0 && ind < attr.strings.Count ? hash_of(attr.strings[ind]) : hash_of("");
                    }
                }
                else if (attr.type == PartioType.INT)
                {
                    int* o = (int*)out_data;
                    for (int i = 0; i < count; i++)
                    {
                        int p = in_indices[i];
                        for (int c = 0; c < stride; c++)
                        {
                            o[i * stride + c] = p >= 0 && p < pc.m_num_particles ? attr.idata[p * stride + c] : 0;
                        }
                    }
                }
                else
                {
                    float* o = (float*)out_data;
                    for (int i = 0; i < count; i++)
                    {
                        int p = in_indices[i];
                        for (int c = 0; c < stride; c++)
                        {
                            o[i * stride + c] = p >= 0 && p < pc.m_num_particles ? attr.fdata[p * stride + c] : 0.0f;
                        }
                    }
                }
            }
            return 1;
        }

        public static bool write(ShaderGlobals* sg, ulong filename, Vec3 pos, int nattribs, ulong* names, TypeDesc* types, void** data)
        {
            if (filename == hash_of(""))
            {
                return false;
            }
            PointCloud pc = get_cloud(filename, true);
            if (pc == null)
            {
                return false;
            }
            lock (pc.m_mutex)
            {
                pc.m_write = true;
                if (pc.m_num_particles == 0 && pc.m_position_attribute == null)
                {
                    pc.m_position_attribute = pc.add_attribute("position", PartioType.VECTOR, 3);
                }
                bool ok = true;
                PartioAttribute[] partattrs = new PartioAttribute[nattribs];
                for (int i = 0; i < nattribs; i++)
                {
                    PartioAttribute a;
                    if (!pc.m_attributes.TryGetValue(names[i], out a))
                    {
                        PartioType pt = partio_type_of(types[i]);
                        if (pt == PartioType.NONE)
                        {
                            ok = false;
                            a = null;
                        }
                        else
                        {
                            a = pc.add_attribute(name_of(names[i]), pt, pt == PartioType.VECTOR ? 3 : 1);
                            pc.m_attributes[names[i]] = a;
                        }
                    }
                    partattrs[i] = a;
                }
                int p = pc.add_particle();
                PartioAttribute position = pc.m_position_attribute;
                position.fdata[p * 3] = pos.x;
                position.fdata[p * 3 + 1] = pos.y;
                position.fdata[p * 3 + 2] = pos.z;
                for (int i = 0; i < nattribs; i++)
                {
                    PartioAttribute a = partattrs[i];
                    if (a == null || partio_type_of(types[i]) != a.type)
                    {
                        continue;
                    }
                    switch (a.type)
                    {
                        case PartioType.FLOAT:
                            a.fdata[p] = *(float*)data[i];
                            break;
                        case PartioType.VECTOR:
                            a.fdata[p * 3] = ((float*)data[i])[0];
                            a.fdata[p * 3 + 1] = ((float*)data[i])[1];
                            a.fdata[p * 3 + 2] = ((float*)data[i])[2];
                            break;
                        case PartioType.INT:
                            a.idata[p] = *(int*)data[i];
                            break;
                        case PartioType.INDEXEDSTR:
                            {
                                string s = name_of(*(ulong*)data[i]);
                                int index = a.strings.IndexOf(s);
                                if (index == -1)
                                {
                                    a.strings.Add(s);
                                    index = a.strings.Count - 1;
                                }
                                a.idata[p] = index;
                            }
                            break;
                    }
                }
                return ok;
            }
        }
    }
}
