// OSL port: liboslexec/loadshader.cpp (OSOReaderToMaster, ShadingSystemImpl::loadshader, LoadMemoryCompiledShader)

using System;
using System.Collections.Generic;
using OIIO;
using OSL;
using static OSL.pvt.PvtFunctions;

namespace OSL.pvt
{
    /// An OSOReader that builds a ShaderMaster
    public sealed class OSOReaderToMaster : OSOReader
    {
        private ShadingSystemImpl m_shadingsys;
        private ShaderMaster m_master;
        private int m_firstarg;
        private int m_nargs;
        private bool m_reading_instruction;
        private ustring m_sourcefile = new ustring("");
        private int m_sourceline;
        private ustring m_codesection = new ustring("");
        private int m_codesym;
        private int m_oso_major, m_oso_minor;
        private int m_sym_default_index;
        private bool m_errors;
        private Dictionary<ustring, int> m_symmap = new Dictionary<ustring, int>();

        public OSOReaderToMaster(ShadingSystemImpl shadingsys)
            : base(shadingsys.errhandler())
        {
            m_shadingsys = shadingsys;
            m_master = new ShaderMaster(shadingsys);
            m_reading_instruction = false;
            m_errors = false;
        }

        public ShaderMaster master() { return m_master; }

        public override bool parse_file(string filename)
        {
            m_master.m_osofilename = filename;
            m_master.m_maincodebegin = 0;
            m_master.m_maincodeend = 0;
            m_codesection = new ustring("");
            m_codesym = -1;
            return base.parse_file(filename) && !m_errors;
        }

        public override bool parse_memory(string oso)
        {
            m_master.m_osofilename = "<none>";
            m_master.m_maincodebegin = 0;
            m_master.m_maincodeend = 0;
            m_codesection = new ustring("");
            m_codesym = -1;
            return base.parse_memory(oso) && !m_errors;
        }

        public override void version(string specid, int major, int minor)
        {
            m_oso_major = major;
            m_oso_minor = minor;
        }

        public override void shader(string shadertype, string name)
        {
            m_master.m_shadername = name;
            m_master.m_shadertype = shadertype_from_name(shadertype);
        }

        private static void expand_f(List<float> v, int n) { for (int i = 0; i < n; ++i) { v.Add(0); } }
        private static void expand_i(List<int> v, int n) { for (int i = 0; i < n; ++i) { v.Add(0); } }
        private static void expand_s(List<ustring> v, int n) { for (int i = 0; i < n; ++i) { v.Add(new ustring("")); } }

        public override void symbol(SymType symtype, TypeSpec typespec, string name_)
        {
            ustring name = new ustring(name_);
            Symbol sym = new Symbol(name, typespec, symtype);
            TypeDesc t = typespec.simpletype();
            int nvals = t.aggregate * (t.is_unsized_array() ? 1 : t.numelements());
            if (sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam)
            {
                // Skip structs for now, they're just placeholders
                if (typespec.is_structure())
                {
                }
                else if (typespec.simpletype().basetype == TypeDesc.FLOAT)
                {
                    sym.dataoffset(m_master.m_fdefaults.Count);
                    expand_f(m_master.m_fdefaults, nvals);
                }
                else if (typespec.simpletype().basetype == TypeDesc.INT)
                {
                    sym.dataoffset(m_master.m_idefaults.Count);
                    expand_i(m_master.m_idefaults, nvals);
                }
                else if (typespec.simpletype().basetype == TypeDesc.STRING)
                {
                    sym.dataoffset(m_master.m_sdefaults.Count);
                    expand_s(m_master.m_sdefaults, nvals);
                }
                else if (typespec.is_closure_based())
                {
                    // Closure params still need space reserved for the default (always null)
                    sym.dataoffset(m_master.m_sdefaults.Count);
                    expand_s(m_master.m_sdefaults, nvals);
                }
                else
                {
                    throw new InvalidOperationException("unexpected type");
                }
            }
            if (sym.symtype() == SymType.SymTypeConst)
            {
                if (typespec.simpletype().basetype == TypeDesc.FLOAT)
                {
                    sym.dataoffset(m_master.m_fconsts.Count);
                    expand_f(m_master.m_fconsts, nvals);
                }
                else if (typespec.simpletype().basetype == TypeDesc.INT)
                {
                    sym.dataoffset(m_master.m_iconsts.Count);
                    expand_i(m_master.m_iconsts, nvals);
                }
                else if (typespec.simpletype().basetype == TypeDesc.STRING)
                {
                    sym.dataoffset(m_master.m_sconsts.Count);
                    expand_s(m_master.m_sconsts, nvals);
                }
                else
                {
                    throw new InvalidOperationException("unexpected type");
                }
            }
            sym.interpolated(!m_shadingsys.lockgeom_default());
            m_master.m_symbols.Add(sym);
            m_symmap[name] = m_master.m_symbols.Count - 1;
            m_sym_default_index = 0;
        }

        private void add_param_default(float def, int offset, Symbol sym)
        {
            if (sym.typespec().is_unsized_array() && offset >= m_master.m_fdefaults.Count)
            {
                m_master.m_fdefaults.Add(def);
            }
            else
            {
                m_master.m_fdefaults[offset] = def;
            }
        }

        private void add_param_default(int def, int offset, Symbol sym)
        {
            if (sym.typespec().is_unsized_array() && offset >= m_master.m_idefaults.Count)
            {
                m_master.m_idefaults.Add(def);
            }
            else
            {
                m_master.m_idefaults[offset] = def;
            }
        }

        private void add_param_default(string def, int offset, Symbol sym)
        {
            if (sym.typespec().is_unsized_array() && offset >= m_master.m_sdefaults.Count)
            {
                m_master.m_sdefaults.Add(new ustring(def));
            }
            else
            {
                m_master.m_sdefaults[offset] = new ustring(def);
            }
        }

        public override void symdefault(int def)
        {
            Symbol sym = m_master.m_symbols[m_master.m_symbols.Count - 1];
            int offset = sym.dataoffset() + m_sym_default_index;
            ++m_sym_default_index;
            if (sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam)
            {
                if (sym.typespec().simpletype().basetype == TypeDesc.FLOAT)
                {
                    add_param_default((float)def, offset, sym);
                }
                else if (sym.typespec().simpletype().basetype == TypeDesc.INT)
                {
                    add_param_default(def, offset, sym);
                }
            }
            else if (sym.symtype() == SymType.SymTypeConst)
            {
                if (sym.typespec().simpletype().basetype == TypeDesc.FLOAT)
                {
                    m_master.m_fconsts[offset] = (float)def;
                }
                else if (sym.typespec().simpletype().basetype == TypeDesc.INT)
                {
                    m_master.m_iconsts[offset] = def;
                }
            }
        }

        public override void symdefault(float def)
        {
            Symbol sym = m_master.m_symbols[m_master.m_symbols.Count - 1];
            int offset = sym.dataoffset() + m_sym_default_index;
            ++m_sym_default_index;
            if (sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam)
            {
                if (sym.typespec().simpletype().basetype == TypeDesc.FLOAT)
                {
                    add_param_default(def, offset, sym);
                }
            }
            else if (sym.symtype() == SymType.SymTypeConst)
            {
                if (sym.typespec().simpletype().basetype == TypeDesc.FLOAT)
                {
                    m_master.m_fconsts[offset] = def;
                }
            }
        }

        public override void symdefault(string def)
        {
            Symbol sym = m_master.m_symbols[m_master.m_symbols.Count - 1];
            int offset = sym.dataoffset() + m_sym_default_index;
            ++m_sym_default_index;
            if (sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam)
            {
                if (sym.typespec().simpletype().basetype == TypeDesc.STRING)
                {
                    add_param_default(def, offset, sym);
                }
            }
            else if (sym.symtype() == SymType.SymTypeConst)
            {
                if (sym.typespec().simpletype().basetype == TypeDesc.STRING)
                {
                    m_master.m_sconsts[offset] = new ustring(def);
                }
            }
        }

        public override void parameter_done()
        {
            Symbol sym = m_master.m_symbols[m_master.m_symbols.Count - 1];
            if (sym.symtype() == SymType.SymTypeParam && sym.typespec().is_unsized_array())
            {
                sym.initializers(m_sym_default_index / sym.typespec().aggregate());
            }
        }

        public override void hint(string hintstring)
        {
            string_view h = new string_view(hintstring);
            if (Strutil.parse_prefix(ref h, "%filename{\""))
            {
                m_sourcefile = new ustring(Strutil.parse_until(ref h, "\"").ToString());
                return;
            }
            if (Strutil.parse_prefix(ref h, "%line{"))
            {
                Strutil.parse_int(ref h, out m_sourceline);
                return;
            }
            if (Strutil.parse_prefix(ref h, "%structfields{") && m_master.m_symbols.Count != 0)
            {
                Symbol sym = m_master.m_symbols[m_master.m_symbols.Count - 1];
                StructSpec structspec = sym.typespec().structspec();
                if (structspec.numfields() == 0)
                {
                    while (true)
                    {
                        string afield = Strutil.parse_until(ref h, ",}").ToString();
                        Strutil.parse_char(ref h, ',');  // skip the separator
                        if (afield.Length == 0)
                        {
                            break;
                        }
                        structspec.add_field(new TypeSpec(), new ustring(afield));
                    }
                }
                return;
            }
            if (Strutil.parse_prefix(ref h, "%mystructfield{") && m_master.m_symbols.Count != 0)
            {
                int ival = -1;
                if (Strutil.parse_int(ref h, out ival) && ival >= 0)
                {
                    m_master.m_symbols[m_master.m_symbols.Count - 1].fieldid(ival);
                }
                return;
            }
            if (Strutil.parse_prefix(ref h, "%read{") && m_master.m_symbols.Count != 0)
            {
                Symbol sym = m_master.m_symbols[m_master.m_symbols.Count - 1];
                int first, last;
                if (Strutil.parse_int(ref h, out first) && Strutil.parse_char(ref h, ',') && Strutil.parse_int(ref h, out last))
                {
                    sym.set_read(first, last);
                }
                return;
            }
            if (Strutil.parse_prefix(ref h, "%write{") && m_master.m_symbols.Count != 0)
            {
                Symbol sym = m_master.m_symbols[m_master.m_symbols.Count - 1];
                int first, last;
                if (Strutil.parse_int(ref h, out first) && Strutil.parse_char(ref h, ',') && Strutil.parse_int(ref h, out last))
                {
                    sym.set_write(first, last);
                }
                return;
            }
            if (Strutil.parse_prefix(ref h, "%argrw{") && m_master.m_ops.Count != 0)
            {
                Opcode op = m_master.m_ops[m_master.m_ops.Count - 1];
                string_view str = Strutil.parse_until(ref h, "}");
                string_view unq;
                Strutil.parse_string(ref str, out unq, false, Strutil.QuoteBehavior.DeleteQuotes);
                str = unq;
                if (str.size() != m_nargs)
                {
                    m_shadingsys.errorfmt("Parsing shader {}: malformed hint '{}' on op {} line {}", m_master.shadername(), hintstring,
                                          m_master.m_ops[m_master.m_ops.Count - 1].opname(), m_sourceline);
                    m_errors = true;
                }
                string s = str.ToString();
                for (int i = 0; s.Length > i && i < m_nargs; ++i)
                {
                    char c = s[i];
                    op.argwrite(i, c == 'w' || c == 'W');
                    op.argread(i, c == 'r' || c == 'W');
                }
                ustring opname = op.opname();
                if (opname == "getmatrix")
                {
                    // getmatrix is the only thing that writes to its last (output) argument only
                    op.argwriteonly(m_nargs - 1);
                }
                if ((opname == "regex_search" || opname == "regex_match") && m_nargs > 3)
                {
                    // regex_search and regex_match: the result (arg 0) is written, the subject (arg 1) read, the results array (arg 2) write-only
                    op.argwriteonly(2);
                }
                return;
            }
            if (Strutil.parse_prefix(ref h, "%argderivs{"))
            {
                while (true)
                {
                    string_view afield = Strutil.parse_until(ref h, ",}");
                    Strutil.parse_char(ref h, ',');  // skip the separator
                    if (afield.size() == 0)
                    {
                        break;
                    }
                    int arg = -1;
                    if (Strutil.parse_int(ref afield, out arg) && arg >= 0)
                    {
                        m_master.m_ops[m_master.m_ops.Count - 1].argtakesderivs(arg, true);
                    }
                }
                return;
            }
            if (Strutil.parse_prefix(ref h, "%meta{"))
            {
                int ival = -1;
                TypeDesc type = TypeDescParse.fromstring(Strutil.parse_identifier(ref h, "", true).ToString());
                Strutil.parse_char(ref h, ',');
                string ident = Strutil.parse_identifier(ref h, "", true).ToString();
                Strutil.parse_char(ref h, ',');
                if (m_master.m_symbols.Count != 0)
                {
                    Symbol sym = m_master.m_symbols[m_master.m_symbols.Count - 1];
                    if (type == TypeDescs.TypeInt && ident == "lockgeom" && Strutil.parse_int(ref h, out ival) && ival >= 0)
                    {
                        sym.interpolated(ival == 0);  // soft deprecated
                    }
                    else if (type == TypeDescs.TypeInt && ident == "interpolated" && Strutil.parse_int(ref h, out ival) && ival >= 0)
                    {
                        sym.interpolated(ival != 0);
                    }
                    else if (type == TypeDescs.TypeInt && ident == "interactive" && Strutil.parse_int(ref h, out ival) && ival >= 0)
                    {
                        sym.interactive(ival != 0);
                    }
                    else if (type == TypeDescs.TypeInt && ident == "allowconnect" && Strutil.parse_int(ref h, out ival) && ival >= 0)
                    {
                        sym.allowconnect(ival != 0);
                    }
                }
                else
                {
                    if (type == TypeDescs.TypeInt && ident == "range_checking" && Strutil.parse_int(ref h, out ival) && ival >= 0)
                    {
                        m_master.range_checking(ival != 0);
                    }
                }
                return;
            }
        }

        public override void codemarker(string name)
        {
            m_sourcefile = new ustring("");
            int nextop = m_master.m_ops.Count;
            codeend();  // Mark the end spot, if we were parsing ops before
            m_codesection = new ustring(name);
            m_codesym = m_master.findsymbol(m_codesection);
            if (m_codesym >= 0)
            {
                m_master.symbol(m_codesym).initbegin(nextop);
            }
            if (m_codesection == "___main___")
            {
                m_master.m_maincodebegin = nextop;
            }
            else if (m_codesym < 0)
            {
                m_shadingsys.errorfmt("Parsing shader {}: don't know what to do with code section \"{}\"", m_master.shadername(), name);
                m_errors = true;
            }
        }

        public override void codeend()
        {
            int nextop = m_master.m_ops.Count;
            if (m_codesym >= 0)
            {
                m_master.symbol(m_codesym).initend(nextop);
            }
            else if (m_codesection == "___main___")
            {
                m_master.m_maincodeend = nextop;
            }
        }

        public override void instruction(int label, string opcode)
        {
            ustring uopcode = new ustring(opcode);
            Opcode op = new Opcode(uopcode, m_codesection);
            m_master.m_ops.Add(op);
            m_firstarg = m_master.m_args.Count;
            m_nargs = 0;
            m_reading_instruction = true;
            OpDescriptor od = m_shadingsys.op_descriptor(uopcode);
            if (od != null)
            {
                uopcode = od.name;
            }
            else
            {
                m_shadingsys.errorfmt("Parsing shader \"{}\": instruction \"{}\" is not known. Maybe compiled with a too-new oslc?",
                                      m_master.shadername(), opcode);
                m_errors = true;
            }
        }

        public override void instruction_arg(string name)
        {
            ustring argname = new ustring(name);
            int found;
            if (m_symmap.TryGetValue(argname, out found))
            {
                m_master.m_args.Add(found);
                ++m_nargs;
                return;
            }
            m_shadingsys.errorfmt("Parsing shader {}: unknown arg {}", m_master.shadername(), name);
            m_errors = true;
        }

        public override void instruction_jump(int target)
        {
            m_master.m_ops[m_master.m_ops.Count - 1].add_jump(target);
        }

        public override void instruction_end()
        {
            m_master.m_ops[m_master.m_ops.Count - 1].set_args(m_firstarg, m_nargs);
            m_master.m_ops[m_master.m_ops.Count - 1].source(m_sourcefile, m_sourceline);
            m_reading_instruction = false;
        }
    }

    public sealed partial class ShadingSystemImpl
    {
        public ShaderMaster loadshader(string cname)
        {
            if (Strutil.ends_with(cname, ".oso"))
            {
                cname = cname.Substring(0, cname.Length - 4);  // strip superfluous .oso
            }
            if (cname.Length == 0)
            {
                error("Attempt to load shader with empty name \"\".");
                return null;
            }
            System.Threading.Interlocked.Increment(ref m_stat_shaders_requested);
            ustring name = new ustring(cname);
            lock (m_mutex)  // Thread safety
            {
                ShaderMaster found;
                if (m_shader_masters.TryGetValue(name, out found))
                {
                    return found;
                }
                // Not found, we need to load it
                OSOReaderToMaster oso = new OSOReaderToMaster(this);
                bool testcwd = m_searchpath_dirs.Count == 0;  // test "." if there's no searchpath
                string filename = Filesystem.searchpath_find(name.@string() + ".oso", m_searchpath_dirs, testcwd);
                if (filename.Length == 0)
                {
                    errorfmt("No .oso file could be found for shader \"{}\"", name);
                    return null;
                }
                Timer timer = new Timer();
                bool ok = oso.parse_file(filename);
                ShaderMaster r = ok ? oso.master() : null;
                m_shader_masters[name] = r;
                double loadtime = timer.elapsed();
                lock (m_stat_mutex)
                {
                    m_stat_master_load_time += loadtime;
                }
                if (ok)
                {
                    System.Threading.Interlocked.Increment(ref m_stat_shaders_loaded);
                    if (debug() != 0)
                    {
                        infofmt("Loaded \"{}\" (took {})", filename, Strutil.timeintervalformat(loadtime, 2));
                    }
                    r.resolve_syms();
                }
                else
                {
                    errorfmt("Unable to read \"{}\"", filename);
                }
                return r;
            }
        }

        public bool LoadMemoryCompiledShader(string shadername, string buffer)
        {
            if (shadername.Length == 0)
            {
                error("Attempt to load shader with empty name \"\".");
                return false;
            }
            if (buffer.Length == 0)
            {
                errorfmt("Attempt to load shader \"{}\" with empty OSO data.", shadername);
                return false;
            }
            ustring name = new ustring(shadername);
            lock (m_mutex)  // Thread safety
            {
                ShaderMaster found;
                if (m_shader_masters.TryGetValue(name, out found) && !allow_shader_replacement())
                {
                    if (debug() != 0)
                    {
                        infofmt("Preload shader {} already exists in shader_masters", name);
                    }
                    return false;
                }
                OSOReaderToMaster reader = new OSOReaderToMaster(this);
                Timer timer = new Timer();
                bool ok = reader.parse_memory(buffer);
                ShaderMaster r = ok ? reader.master() : null;
                m_shader_masters[name] = r;
                double loadtime = timer.elapsed();
                lock (m_stat_mutex)
                {
                    m_stat_master_load_time += loadtime;
                }
                if (ok)
                {
                    System.Threading.Interlocked.Increment(ref m_stat_shaders_loaded);
                    if (debug() != 0)
                    {
                        infofmt("Loaded \"{}\" (took {})", shadername, Strutil.timeintervalformat(loadtime, 2));
                    }
                    r.resolve_syms();
                }
                else
                {
                    errorfmt("Unable to parse preloaded shader \"{}\"", shadername);
                }
                return ok;
            }
        }
    }
}
