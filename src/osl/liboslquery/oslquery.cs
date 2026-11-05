// OSL port: src/liboslquery/oslquery.cpp and src/include/OSL/oslquery.h
//
// OSLQuery reads the parameters, defaults and metadata of a compiled shader straight from its .oso file (no ShadingSystem needed).
// Differences from the C++: Parameter.data (a raw pointer to the default values) is the default list itself (idefault, fdefault or sdefault) typed
// as object, and Parameter is a class with an explicit copy (the C++ copy constructors re-point 'data' into the copied vectors).
// OSLQuery::init(ShaderGroup, layer) and the deprecated group constructor live in the shading system and are not part of this file.

using System;
using System.Collections.Generic;
using OIIO;
using OSL.pvt;

namespace OSL
{
    public class OSLQuery
    {
        public class Parameter
        {
            public ustring name;
            public TypeDesc type;
            public bool isoutput = false;
            public bool validdefault = false;
            public bool varlenarray = false;
            public bool isstruct = false;
            public bool isclosure = false;
            public object data = null;
            public List<int> idefault = new List<int>();
            public List<float> fdefault = new List<float>();
            public List<ustring> sdefault = new List<ustring>();
            public List<ustring> spacename = new List<ustring>();
            public List<ustring> fields = new List<ustring>();
            public ustring structname;
            public List<Parameter> metadata = new List<Parameter>();

            public Parameter()
            {
            }

            public Parameter(Parameter src)
            {
                name = src.name;
                type = src.type;
                isoutput = src.isoutput;
                validdefault = src.validdefault;
                varlenarray = src.varlenarray;
                isstruct = src.isstruct;
                isclosure = src.isclosure;
                idefault = new List<int>(src.idefault);
                fdefault = new List<float>(src.fdefault);
                sdefault = new List<ustring>(src.sdefault);
                spacename = new List<ustring>(src.spacename);
                fields = new List<ustring>(src.fields);
                structname = src.structname;
                metadata = new List<Parameter>(src.metadata);
                repoint_data();
            }

            /// Point 'data' at the default values list that matches the type
            public void repoint_data()
            {
                if (type.basetype == TypeDesc.INT)
                {
                    data = idefault;
                }
                else if (type.basetype == TypeDesc.FLOAT)
                {
                    data = fdefault;
                }
                else if (type.basetype == TypeDesc.STRING)
                {
                    data = sdefault;
                }
            }

            public string type_name()
            {
                return type.c_str();
            }
        }

        internal ustring m_shadername;
        internal ustring m_shadertypename;
        private string m_error = "";
        internal List<Parameter> m_params = new List<Parameter>();
        internal List<Parameter> m_meta = new List<Parameter>();

        public OSLQuery()
        {
        }

        public OSLQuery(string shadername, string searchpath = "")
        {
            open(shadername, searchpath);
        }

        /// Open the shader (.oso) by name, looking in the searchpath (a list of directories) if it is not empty.
        public bool open(string shadername, string searchpath = "")
        {
            OSOReaderQuery oso = new OSOReaderQuery(this);
            string filename = shadername;
            if (Filesystem.extension(filename) != ".oso")
            {
                filename += ".oso";
            }
            if (searchpath.Length != 0)
            {
                List<string> dirs = new List<string>();
                Filesystem.searchpath_split(searchpath, dirs);
                filename = Filesystem.searchpath_find(filename, dirs);
            }
            if (filename.Length == 0)
            {
                errorfmt("File \"{}\" could not be found.", shadername);
                return false;
            }
            return oso.parse_file(filename);
        }

        /// Read the shader from the text of an .oso held in memory
        public bool open_bytecode(string buffer)
        {
            OSOReaderQuery oso = new OSOReaderQuery(this);
            return oso.parse_memory(buffer);
        }

        public ustring shadertype()
        {
            return m_shadertypename;
        }

        public ustring shadername()
        {
            return m_shadername;
        }

        public int nparams()
        {
            return m_params.Count;
        }

        public Parameter getparam(int i)
        {
            if (i < 0 || i >= nparams())
            {
                return null;
            }
            return m_params[i];
        }

        public Parameter getparam(string name)
        {
            for (int i = 0; i < nparams(); ++i)
            {
                if (m_params[i].name == name)
                {
                    return m_params[i];
                }
            }
            return null;
        }

        public List<Parameter> parameters()
        {
            return m_params;
        }

        public List<Parameter> metadata()
        {
            return m_meta;
        }

        public string geterror(bool clear_error = true)
        {
            string e = m_error;
            if (clear_error)
            {
                m_error = "";
            }
            return e;
        }

        internal void errorfmt(string fmt, params object[] args)
        {
            append_error(Fmt.format(fmt, args));
        }

        private void append_error(string message)
        {
            if (m_error.Length != 0)
            {
                m_error += '\n';
            }
            m_error += message;
        }
    }

    namespace pvt
    {
        /// Custom subclass of OSOReader that just reads the .oso file and fills out the right fields in the OSLQuery.
        internal class OSOReaderQuery : OSOReader
        {
            private OSLQuery m_query;
            private bool m_reading_param;
            private int m_default_values;

            public OSOReaderQuery(OSLQuery query)
            {
                m_query = query;
                m_reading_param = false;
                m_default_values = 0;
            }

            public override void version(string specid, int major, int minor)
            {
            }

            public override void shader(string shadertype, string name)
            {
                m_query.m_shadername = new ustring(name);
                m_query.m_shadertypename = new ustring(shadertype);
            }

            public override void symbol(SymType symtype, TypeSpec typespec, string name)
            {
                if (symtype == SymType.SymTypeParam || symtype == SymType.SymTypeOutputParam)
                {
                    m_reading_param = true;
                    m_default_values = 0;
                    OSLQuery.Parameter p = new OSLQuery.Parameter();
                    p.name = new ustring(name);
                    p.type = typespec.simpletype();
                    p.isoutput = (symtype == SymType.SymTypeOutputParam);
                    p.varlenarray = typespec.is_unsized_array();
                    p.isstruct = typespec.is_structure();
                    p.isclosure = typespec.is_closure();
                    m_query.m_params.Add(p);
                }
                else
                {
                    m_reading_param = false;
                }
            }

            public override void symdefault(int def)
            {
                if (m_reading_param && m_query.nparams() > 0)
                {
                    OSLQuery.Parameter p = m_query.m_params[m_query.nparams() - 1];
                    if (p.type.basetype == TypeDesc.FLOAT)
                    {
                        p.fdefault.Add((float)def);
                    }
                    else
                    {
                        p.idefault.Add(def);
                    }
                    p.validdefault = true;
                    m_default_values++;
                }
            }

            public override void symdefault(float def)
            {
                if (m_reading_param && m_query.nparams() > 0)
                {
                    OSLQuery.Parameter p = m_query.m_params[m_query.nparams() - 1];
                    p.fdefault.Add(def);
                    p.validdefault = true;
                    m_default_values++;
                }
            }

            public override void symdefault(string def)
            {
                if (m_reading_param && m_query.nparams() > 0)
                {
                    OSLQuery.Parameter p = m_query.m_params[m_query.nparams() - 1];
                    p.sdefault.Add(new ustring(def));
                    p.validdefault = true;
                    m_default_values++;
                }
            }

            public override void parameter_done()
            {
                if (m_reading_param && m_query.nparams() > 0)
                {
                    // Make sure all value defaults have the right number of elements in case they were only partially initialized.
                    OSLQuery.Parameter p = m_query.m_params[m_query.nparams() - 1];
                    int nvalues;
                    if (p.varlenarray)
                    {
                        nvalues = m_default_values;
                    }
                    else
                    {
                        nvalues = p.type.numelements() * p.type.aggregate;
                    }
                    if (p.type.basetype == TypeDesc.INT)
                    {
                        resize(p.idefault, nvalues, 0);
                    }
                    else if (p.type.basetype == TypeDesc.FLOAT)
                    {
                        resize(p.fdefault, nvalues, 0.0f);
                    }
                    else if (p.type.basetype == TypeDesc.STRING)
                    {
                        resize(p.sdefault, nvalues, new ustring());
                    }
                    p.repoint_data();
                    if (p.spacename.Count != 0)
                    {
                        resize(p.spacename, p.type.numelements(), new ustring());
                    }
                }
                m_reading_param = false;
            }

            private static void resize<T>(List<T> list, int n, T fill)
            {
                if (n < list.Count)
                {
                    list.RemoveRange(n, list.Count - n);
                }
                while (list.Count < n)
                {
                    list.Add(fill);
                }
            }

            public override void hint(string hintstring)
            {
                string_view hv = new string_view(hintstring);
                if (!Strutil.parse_char(ref hv, '%'))
                {
                    return;
                }
                if (Strutil.parse_prefix(ref hv, "meta{"))
                {
                    Strutil.skip_whitespace(ref hv);
                    string type = Strutil.parse_until(ref hv, ",}").ToString();
                    Strutil.parse_char(ref hv, ',');
                    string name = Strutil.parse_until(ref hv, ",}").ToString();
                    Strutil.parse_char(ref hv, ',');
                    OSLQuery.Parameter p = new OSLQuery.Parameter();
                    p.name = new ustring(name);
                    p.type = TypeDescParse.fromstring(type);
                    if (p.type.basetype == TypeDesc.STRING)
                    {
                        string_view val;
                        while (Strutil.parse_string(ref hv, out val))
                        {
                            p.sdefault.Add(new ustring(Strutil.unescape_chars(val.ToString())));
                            if (Strutil.parse_char(ref hv, '}'))
                            {
                                break;
                            }
                            Strutil.parse_char(ref hv, ',');
                        }
                    }
                    else if (p.type.basetype == TypeDesc.INT)
                    {
                        int val;
                        while (Strutil.parse_int(ref hv, out val))
                        {
                            p.idefault.Add(val);
                            Strutil.parse_char(ref hv, ',');
                        }
                    }
                    else if (p.type.basetype == TypeDesc.FLOAT)
                    {
                        float val;
                        while (Strutil.parse_float(ref hv, out val))
                        {
                            p.fdefault.Add(val);
                            Strutil.parse_char(ref hv, ',');
                        }
                    }
                    Strutil.parse_char(ref hv, '}');
                    if (m_reading_param)
                    {
                        m_query.m_params[m_query.nparams() - 1].metadata.Add(p);
                    }
                    else
                    {
                        m_query.m_meta.Add(p);
                    }
                    return;
                }
                if (m_reading_param && Strutil.parse_prefix(ref hv, "structfields{"))
                {
                    OSLQuery.Parameter param = m_query.m_params[m_query.nparams() - 1];
                    while (true)
                    {
                        string_view ident = Strutil.parse_identifier(ref hv);
                        if (ident.size() != 0)
                        {
                            param.fields.Add(new ustring(ident.ToString()));
                            Strutil.parse_char(ref hv, ',');
                        }
                        else
                        {
                            break;
                        }
                    }
                    Strutil.parse_char(ref hv, '}');
                    return;
                }
                if (m_reading_param && Strutil.parse_prefix(ref hv, "struct{"))
                {
                    string_view str;
                    Strutil.parse_string(ref hv, out str);
                    m_query.m_params[m_query.nparams() - 1].structname = new ustring(str.ToString());
                    Strutil.parse_char(ref hv, '}');
                    return;
                }
                if (m_reading_param && Strutil.parse_prefix(ref hv, "initexpr"))
                {
                    m_query.m_params[m_query.nparams() - 1].validdefault = false;
                    return;
                }
            }

            public override void codemarker(string name)
            {
                m_reading_param = false;
            }

            public override bool parse_code_section()
            {
                return false;
            }

            public override bool stop_parsing_at_temp_symbols()
            {
                return true;
            }
        }
    }
}
