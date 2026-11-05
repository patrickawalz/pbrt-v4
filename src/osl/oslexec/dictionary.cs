// OSL port: liboslexec/dictionary.cpp
//
// The XML dictionary of dict_find / dict_next / dict_value. pugixml's XPath is System.Xml's XPath here. The shadeops (osl_dict_*) take the execution
// context pointer, which is the ShaderGlobals, to find the ShadingContext.

using System;
using System.Collections.Generic;
using System.Xml;
using OIIO;

namespace OSL.pvt
{
    public sealed class Dictionary
    {
        private struct Query : IEquatable<Query>
        {
            public int document;
            public int node;
            public ustring name;
            public TypeDesc type;  // UNKNOWN signifies a node, versus an attribute value

            public Query(int doc_, int node_, ustring name_, TypeDesc type_ = default(TypeDesc))
            {
                document = doc_;
                node = node_;
                name = name_;
                type = type_;
            }

            public bool Equals(Query q) { return document == q.document && node == q.node && name == q.name && type == q.type; }
            public override bool Equals(object o) { return o is Query q && Equals(q); }
            public override int GetHashCode() { return (int)(name.hash() + (ulong)(17 * node) + (ulong)(79 * document)); }
        }

        private struct QueryResult
        {
            public int valueoffset;  // Offset into one of the 'data' vectors, or nodeID
            public bool is_valid;

            public QueryResult(bool valid)
            {
                valueoffset = 0;
                is_valid = valid;
            }

            public QueryResult(bool isnode, int value)
            {
                valueoffset = value;
                is_valid = true;
            }
        }

        private sealed class Node
        {
            public int document;
            public XmlNode node;
            public int next;

            public Node(int d, XmlNode n)
            {
                document = d;
                node = n;
                next = 0;
            }
        }

        private ShadingContext m_context;
        private List<XmlDocument> m_documents = new List<XmlDocument>();
        private Dictionary<ustring, int> m_document_map = new Dictionary<ustring, int>();
        private Dictionary<Query, QueryResult> m_cache = new Dictionary<Query, QueryResult>();
        private List<Node> m_nodes = new List<Node>();
        private List<float> m_floatdata = new List<float>();
        private List<int> m_intdata = new List<int>();
        private List<ustring> m_stringdata = new List<ustring>();

        public Dictionary(ShadingContext ctx)
        {
            m_context = ctx;
            m_nodes.Add(new Node(0, null));
        }

        private void report(object ec, string msg)
        {
            m_context.errorfmt("{}", msg);
        }

        private int get_document_index(object ec, ustring dictionaryname)
        {
            int dindex;
            if (!m_document_map.TryGetValue(dictionaryname, out dindex))
            {
                dindex = m_documents.Count;
                m_document_map[dictionaryname] = dindex;
                XmlDocument doc = new XmlDocument();
                m_documents.Add(doc);
                try
                {
                    if (Strutil.ends_with(dictionaryname.@string(), ".xml"))
                    {
                        doc.Load(dictionaryname.@string());
                    }
                    else
                    {
                        doc.LoadXml(dictionaryname.@string());
                    }
                }
                catch (XmlException e)
                {
                    report(ec, "XML parsed with errors: " + e.Message + ", at offset " + e.LinePosition);
                    m_document_map[dictionaryname] = -1;
                    return -1;
                }
                catch (System.IO.IOException e)
                {
                    report(ec, "XML parsed with errors: " + e.Message + ", at offset 0");
                    m_document_map[dictionaryname] = -1;
                    return -1;
                }
            }
            return dindex;
        }

        private int add_matches(Query q, int document, XmlNodeList matches)
        {
            int firstmatch = m_nodes.Count;
            int last = -1;
            foreach (XmlNode m in matches)
            {
                m_nodes.Add(new Node(document, m));
                int nodeid = m_nodes.Count - 1;
                if (last < 0)
                {
                    m_cache[q] = new QueryResult(true, nodeid);
                }
                else
                {
                    m_nodes[last].next = nodeid;
                }
                last = nodeid;
            }
            return firstmatch;
        }

        public int dict_find(object ec, ustring dictionaryname, ustring query)
        {
            int dindex = get_document_index(ec, dictionaryname);
            if (dindex < 0)
            {
                return dindex;
            }
            Query q = new Query(dindex, 0, query);
            QueryResult qfound;
            if (m_cache.TryGetValue(q, out qfound))
            {
                return qfound.valueoffset;
            }
            XmlDocument doc = m_documents[dindex];
            XmlNodeList matches;
            try
            {
                matches = doc.SelectNodes(query.@string());
            }
            catch (System.Xml.XPath.XPathException e)
            {
                report(ec, "Invalid dict_find query '" + query.@string() + "': " + e.Message);
                return 0;
            }
            if (matches == null || matches.Count == 0)
            {
                m_cache[q] = new QueryResult(false);  // mark invalid
                return 0;                             // Not found
            }
            return add_matches(q, dindex, matches);
        }

        public int dict_find(object ec, int nodeID, ustring query)
        {
            if (nodeID <= 0 || nodeID >= m_nodes.Count)
            {
                return 0;  // invalid node ID
            }
            int document = m_nodes[nodeID].document;
            Query q = new Query(document, nodeID, query);
            QueryResult qfound;
            if (m_cache.TryGetValue(q, out qfound))
            {
                return qfound.valueoffset;
            }
            XmlNodeList matches;
            try
            {
                matches = m_nodes[nodeID].node.SelectNodes(query.@string());
            }
            catch (System.Xml.XPath.XPathException e)
            {
                report(ec, "Invalid dict_find query '" + query.@string() + "': " + e.Message);
                return 0;
            }
            if (matches == null || matches.Count == 0)
            {
                m_cache[q] = new QueryResult(false);
                return 0;
            }
            return add_matches(q, document, matches);
        }

        public int dict_next(int nodeID)
        {
            if (nodeID <= 0 || nodeID >= m_nodes.Count)
            {
                return 0;  // invalid node ID
            }
            return m_nodes[nodeID].next;
        }

        /// 'data' is an int[], float[] or ustring[] (ulong[] when the strings are to be hashes) that receives the value
        public int dict_value(int nodeID, ustring attribname, TypeDesc type, object data, bool treat_ustrings_as_hash)
        {
            if (nodeID <= 0 || nodeID >= m_nodes.Count)
            {
                return 0;  // invalid node ID
            }
            Node node = m_nodes[nodeID];
            Query q = new Query(node.document, nodeID, attribname, type);
            QueryResult qfound;
            int n = type.numelements() * type.aggregate;
            if (m_cache.TryGetValue(q, out qfound))
            {
                int offset = qfound.valueoffset;
                if (type.basetype == TypeDesc.STRING)
                {
                    set_string(data, m_stringdata[offset], treat_ustrings_as_hash);
                    return 1;
                }
                if (type.basetype == TypeDesc.INT)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        ((int[])data)[i] = m_intdata[offset++];
                    }
                    return 1;
                }
                if (type.basetype == TypeDesc.FLOAT)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        ((float[])data)[i] = m_floatdata[offset++];
                    }
                    return 1;
                }
                return 0;  // Unknown type
            }

            // OK, the query wasn't cached, look for the attribute value
            string val = null;
            if (attribname.empty())
            {
                // pugixml's value() is the text of a text-like node, empty for an element
                XmlNode xn = node.node;
                if (xn is XmlElement)
                {
                    val = "";
                }
                else
                {
                    val = xn.Value ?? "";
                }
            }
            else if (node.node.Attributes != null)
            {
                foreach (XmlAttribute a in node.node.Attributes)
                {
                    if (a.Name == attribname.@string())
                    {
                        val = a.Value;
                        break;
                    }
                }
            }
            if (val == null)
            {
                return 0;  // not found
            }
            QueryResult r = new QueryResult(false, 0);
            if (type.basetype == TypeDesc.STRING && n == 1)
            {
                r.valueoffset = m_stringdata.Count;
                ustring s = new ustring(val);
                m_stringdata.Add(s);
                set_string(data, s, treat_ustrings_as_hash);
                m_cache[q] = r;
                return 1;
            }
            if (type.basetype == TypeDesc.INT)
            {
                r.valueoffset = m_intdata.Count;
                string_view valstr = new string_view(val);
                for (int i = 0; i < n; ++i)
                {
                    int v;
                    Strutil.parse_int(ref valstr, out v);
                    Strutil.parse_char(ref valstr, ',');
                    m_intdata.Add(v);
                    ((int[])data)[i] = v;
                }
                m_cache[q] = r;
                return 1;
            }
            if (type.basetype == TypeDesc.FLOAT)
            {
                r.valueoffset = m_floatdata.Count;
                string_view valstr = new string_view(val);
                for (int i = 0; i < n; ++i)
                {
                    float v;
                    Strutil.parse_float(ref valstr, out v);
                    Strutil.parse_char(ref valstr, ',');
                    m_floatdata.Add(v);
                    ((float[])data)[i] = v;
                }
                m_cache[q] = r;
                return 1;
            }
            return 0;
        }

        private static void set_string(object data, ustring s, bool hash)
        {
            if (data is ulong[] h)
            {
                h[0] = s.hash();
            }
            else if (data is ustring[] u)
            {
                u[0] = s;
            }
        }
    }
}

namespace OSL
{
    using OSL.pvt;

    /// The TypeDesc as the 64-bit value the compiled code passes it as (TYPEDESC): basetype, aggregate, vecsemantics, reserved, arraylen
    public static class TypeDescBits
    {
        public static TypeDesc from_long(long v)
        {
            TypeDesc t = new TypeDesc();
            t.basetype = (byte)(v & 0xff);
            t.aggregate = (byte)((v >> 8) & 0xff);
            t.vecsemantics = (byte)((v >> 16) & 0xff);
            t.reserved = (byte)((v >> 24) & 0xff);
            t.arraylen = (int)(v >> 32);
            return t;
        }

        public static long to_long(TypeDesc t)
        {
            return (long)t.basetype | ((long)t.aggregate << 8) | ((long)t.vecsemantics << 16) | ((long)t.reserved << 24) | ((long)t.arraylen << 32);
        }
    }

    public sealed unsafe partial class ShadingContext
    {
        public int dict_find(object ec, ustring dictionaryname, ustring query)
        {
            if (m_dictionary == null)
            {
                m_dictionary = new Dictionary(this);
            }
            return m_dictionary.dict_find(ec, dictionaryname, query);
        }

        public int dict_find(object ec, int nodeID, ustring query)
        {
            if (m_dictionary == null)
            {
                m_dictionary = new Dictionary(this);
            }
            return m_dictionary.dict_find(ec, nodeID, query);
        }

        public int dict_next(int nodeID)
        {
            if (m_dictionary == null)
            {
                return 0;
            }
            return m_dictionary.dict_next(nodeID);
        }

        public int dict_value(int nodeID, ustring attribname, TypeDesc type, object data, bool treat_ustrings_as_hash)
        {
            if (m_dictionary == null)
            {
                return 0;
            }
            return m_dictionary.dict_value(nodeID, attribname, type, data, treat_ustrings_as_hash);
        }

        private void free_dict_resources()
        {
            m_dictionary = null;
        }
    }

    public static unsafe partial class OslRuntime
    {
        private static ShadingContext ctx_of(void* oec)
        {
            return ShaderGlobalsAccess.get_context((ShaderGlobals*)oec);
        }

        public static int osl_dict_find_iis(void* oec, int nodeID, ulong query)
        {
            return ctx_of(oec).dict_find(null, nodeID, OslStrings.from_hash(query));
        }

        public static int osl_dict_find_iss(void* oec, ulong dictionary, ulong query)
        {
            return ctx_of(oec).dict_find(null, OslStrings.from_hash(dictionary), OslStrings.from_hash(query));
        }

        public static int osl_dict_next(void* oec, int nodeID)
        {
            return ctx_of(oec).dict_next(nodeID);
        }

        /// 'type' is the TypeDesc as a long (TYPEDESC), 'data' the unmanaged destination
        public static int osl_dict_value(void* oec, int nodeID, ulong attribname, long type, void* data)
        {
            TypeDesc t = TypeDescBits.from_long(type);
            int n = t.numelements() * t.aggregate;
            object tmp;
            if (t.basetype == TypeDesc.STRING)
            {
                ulong[] h = new ulong[1];
                int r = ctx_of(oec).dict_value(nodeID, OslStrings.from_hash(attribname), t, h, true);
                if (r != 0)
                {
                    *(ulong*)data = h[0];
                }
                return r;
            }
            if (t.basetype == TypeDesc.INT)
            {
                int[] a = new int[n];
                tmp = a;
                int r = ctx_of(oec).dict_value(nodeID, OslStrings.from_hash(attribname), t, tmp, true);
                if (r != 0)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        ((int*)data)[i] = a[i];
                    }
                }
                return r;
            }
            if (t.basetype == TypeDesc.FLOAT)
            {
                float[] a = new float[n];
                tmp = a;
                int r = ctx_of(oec).dict_value(nodeID, OslStrings.from_hash(attribname), t, tmp, true);
                if (r != 0)
                {
                    for (int i = 0; i < n; ++i)
                    {
                        ((float*)data)[i] = a[i];
                    }
                }
                return r;
            }
            return 0;
        }

        public static void osl_incr_layers_executed(ShaderGlobals* sg)
        {
            ShaderGlobalsAccess.get_context(sg).incr_layers_executed();
        }
    }
}
