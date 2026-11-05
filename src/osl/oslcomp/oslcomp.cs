// OSL port: liboslcomp/oslcomp.cpp
//
// The compiler driver: construction, option handling, compile()/compile_buffer(), the .oso writer, the nesting and struct-field helpers
// used by codegen, and the dependency / illegal-write / temporary-coalescing passes. Preprocessing (clang in the C++) and the bison
// parser live in their own files; PreprocessBuffer and ParseBuffer below are the seams.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OIIO;

namespace OSL
{
    /// The public compiler interface (oslcomp.h)
    public class OSLCompiler
    {
        private pvt.OSLCompilerImpl m_impl;

        public OSLCompiler(ErrorHandler errhandler = null)
        {
            m_impl = new pvt.OSLCompilerImpl(errhandler);
        }

        public bool compile(string filename, List<string> options, string stdoslpath = "")
        {
            return m_impl.compile(filename, options, stdoslpath);
        }

        public bool compile_buffer(string sourcecode, out string osobuffer, List<string> options, string stdoslpath = "", string filename = "")
        {
            return m_impl.compile_buffer(sourcecode, out osobuffer, options, stdoslpath, filename);
        }

        public string output_filename()
        {
            return m_impl.output_filename();
        }
    }

    namespace pvt
    {
        public partial class OSLCompilerImpl
        {
            public const int OSO_FILE_VERSION_MAJOR = 1;
            public const int OSO_FILE_VERSION_MINOR = 0;
            public static string OSL_SHADER_INSTALL_DIR = "";
            public const string OSL_LIBRARY_VERSION_STRING = "1.16.0.0dev";

            public OSLCompilerImpl(ErrorHandler errhandler)
            {
                m_errhandler = errhandler != null ? errhandler : ErrorHandler.default_handler;
                m_err = false;
                m_symtab = new SymbolTable();
                m_current_typespec = new TypeSpec(new TypeDesc(TypeDesc.UNKNOWN));
                m_current_output = false;
                m_verbose = false;
                m_quiet = false;
                m_debug = false;
                m_preprocess_only = false;
                m_err_on_warning = false;
                m_optimizelevel = 1;
                m_next_temp = 0;
                m_next_const = 0;
                m_osofile = null;
                m_total_nesting = 0;
                m_loop_nesting = 0;
                m_derivsym = null;
                m_main_method_start = -1;
                m_declaring_shader_formals = false;
                initialize_globals();
                initialize_builtin_funcs();
            }

            public bool preprocess_file(string filename, string stdoslpath, List<string> defines, List<string> includepaths, out string result)
            {
                result = "";
                string instring;
                try
                {
                    instring = File.ReadAllText(filename);
                }
                catch (Exception)
                {
                    errorfmt(new ustring(filename), 0, "Could not open \"" + filename + "\"\n");
                    return false;
                }
                return preprocess_buffer(instring, filename, stdoslpath, defines, includepaths, out result);
            }

            public void read_compile_options(List<string> options, List<string> defines, List<string> includepaths)
            {
                m_output_filename = "";
                m_preprocess_only = false;
                for (int i = 0; i < options.Count; ++i)
                {
                    string o = options[i];
                    if (o == "-v")
                    {
                        m_verbose = true;
                    }
                    else if (o == "-q")
                    {
                        m_quiet = true;
                    }
                    else if (o == "-d")
                    {
                        m_debug = true;
                    }
                    else if (o == "-E")
                    {
                        m_preprocess_only = true;
                    }
                    else if (o == "-o" && i < options.Count - 1)
                    {
                        ++i;
                        m_output_filename = options[i];
                    }
                    else if (o == "-O0")
                    {
                        m_optimizelevel = 0;
                    }
                    else if (o == "-O" || o == "-O1")
                    {
                        m_optimizelevel = 1;
                    }
                    else if (o == "-O2")
                    {
                        m_optimizelevel = 2;
                    }
                    else if (o == "-Werror")
                    {
                        m_err_on_warning = true;
                    }
                    else if (o == "-embed-source" || o == "--embed-source")
                    {
                        m_embed_source = true;
                    }
                    else if (o == "-MD" || o == "--write-dependencies")
                    {
                        m_generate_deps = true;
                        m_generate_system_deps = true;
                    }
                    else if (o == "-MMD" || o == "--write-user-dependencies")
                    {
                        m_generate_deps = true;
                    }
                    else if (o == "-M" || o == "--dependencies")
                    {
                        m_generate_deps = true;
                        m_preprocess_only = true;
                        m_generate_system_deps = true;
                        if (string.IsNullOrEmpty(m_deps_filename))
                        {
                            m_deps_filename = "stdout";
                        }
                    }
                    else if (o == "-MM" || o == "--user-dependencies")
                    {
                        m_generate_deps = true;
                        m_preprocess_only = true;
                        if (string.IsNullOrEmpty(m_deps_filename))
                        {
                            m_deps_filename = "stdout";
                        }
                    }
                    else if (o == "-MF")
                    {
                        m_deps_filename = options[++i];
                    }
                    else if (o.StartsWith("-MF", StringComparison.Ordinal))
                    {
                        m_deps_filename = o.Substring(3);
                    }
                    else if (o == "-MT")
                    {
                        m_deps_target = options[++i];
                    }
                    else if (o.StartsWith("-MT", StringComparison.Ordinal))
                    {
                        m_deps_target = o.Substring(3);
                    }
                    else if (o.Length > 2 && o[0] == '-')
                    {
                        // options meant for the preprocessor
                        if (o[1] == 'D' || o[1] == 'U')
                        {
                            defines.Add(o);
                        }
                        else if (o[1] == 'I')
                        {
                            includepaths.Add(o.Substring(2));
                        }
                    }
                }
            }

            // Guess the path for stdosl.h. Only called if no explicit stdoslpath is given.
            private static string find_stdoslpath(List<string> includepaths)
            {
                foreach (string dir in includepaths)
                {
                    string path = dir + "/stdosl.h";
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }

                // The shader install directory (OSL_SHADER_INSTALL_DIR in the C++ build; here it is set by the host)
                if (OSL_SHADER_INSTALL_DIR.Length != 0)
                {
                    string install_loc = OSL_SHADER_INSTALL_DIR + "/stdosl.h";
                    if (File.Exists(install_loc))
                    {
                        return install_loc;
                    }
                }

                string myprefix = Path.GetDirectoryName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('/', '\\')) ?? "") ?? "";
                string[] prefixes =
                {
                    Environment.GetEnvironmentVariable("OSL_ROOT") ?? "", Environment.GetEnvironmentVariable("OSL_ROOT_DIR") ?? "",
                    Environment.GetEnvironmentVariable("OSLHOME") ?? "", myprefix, Path.GetDirectoryName(myprefix) ?? ""
                };
                string[] suffixes =
                {
                    "/share/OSL/shaders/stdosl.h", "/share/shaders/stdosl.h", "/lib/OSL/include/std/stdosl.h", "/lib/osl/include/std/stdosl.h",
                    "/shaders/stdosl.h"
                };
                foreach (string dir in prefixes)
                {
                    if (dir.Length != 0 && Directory.Exists(dir))
                    {
                        foreach (string s in suffixes)
                        {
                            if (File.Exists(dir + s))
                            {
                                return dir + s;
                            }
                        }
                    }
                }
                // Look for an "oslc" binary in the $PATH (in case the running executable is not the actual oslc), and if found, look in
                // places relative to it.
                string oslcbin = OperatingSystem.IsWindows() ? "oslc.exe" : "oslc";
                string pathvar = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string pdir in pathvar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (File.Exists(Path.Combine(pdir, oslcbin)))
                    {
                        string dir = Path.GetDirectoryName(pdir.TrimEnd('/', '\\'));  // the parent of the bin dir
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            foreach (string sfx in suffixes)
                            {
                                if (File.Exists(dir + sfx))
                                {
                                    return dir + sfx;
                                }
                            }
                        }
                        break;
                    }
                }

                return "";
            }

            public bool compile(string filename, List<string> options, string stdoslpath)
            {
                if (!File.Exists(filename))
                {
                    errorfmt(new ustring(), 0, "Input file \"" + filename + "\" not found");
                    return false;
                }

                List<string> defines = new List<string>();
                List<string> includepaths = new List<string>();
                m_cwd = Directory.GetCurrentDirectory();
                m_main_filename = new ustring(filename);
                clear_filecontents_cache();

                read_compile_options(options, defines, includepaths);

                if (string.IsNullOrEmpty(stdoslpath))
                {
                    stdoslpath = find_stdoslpath(includepaths);
                }
                if (string.IsNullOrEmpty(stdoslpath) || !File.Exists(stdoslpath))
                {
                    warningfmt(new ustring(filename), 0, "Unable to find \"stdosl.h\"");
                }
                else
                {
                    includepaths.Add(Path.GetDirectoryName(stdoslpath));
                }

                string preprocess_result;
                if (!preprocess_file(filename, stdoslpath, defines, includepaths, out preprocess_result))
                {
                    return false;
                }

                if (m_preprocess_only && !m_generate_deps)
                {
                    Console.Out.Write(preprocess_result);
                }
                else
                {
                    bool parseerr = osl_parse_buffer(preprocess_result);
                    if (!parseerr)
                    {
                        if (shader() != null)
                        {
                            shader().typecheck();
                        }
                        else
                        {
                            errorfmt(new ustring(), 0, "No shader function defined");
                        }
                    }

                    if (m_debug)
                    {
                        Console.Out.Write(symtab().print());
                        if (shader() != null)
                        {
                            shader().print(Console.Out);
                        }
                    }

                    if (m_generate_deps)
                    {
                        write_dependency_file(filename);
                    }

                    if (!error_encountered())
                    {
                        shader().codegen();
                        track_variable_dependencies();
                        track_variable_lifetimes();
                        check_for_illegal_writes();
                    }

                    if (!error_encountered())
                    {
                        if (m_output_filename.Length == 0)
                        {
                            m_output_filename = default_output_filename();
                        }

                        StreamWriter oso_output;
                        try
                        {
                            oso_output = new StreamWriter(m_output_filename, false, new UTF8Encoding(false));
                            oso_output.NewLine = "\n";
                        }
                        catch (Exception)
                        {
                            errorfmt(new ustring(), 0, "Could not open \"" + m_output_filename + "\"");
                            return false;
                        }
                        m_osofile = oso_output;

                        write_oso_file(string.Join(" ", options), preprocess_result);

                        oso_output.Dispose();
                    }
                }

                return !error_encountered();
            }

            public bool compile_buffer(string sourcecode, out string osobuffer, List<string> options, string stdoslpath, string filename)
            {
                osobuffer = "";
                if (string.IsNullOrEmpty(filename))
                {
                    filename = "<buffer>";
                }

                List<string> defines = new List<string>();
                List<string> includepaths = new List<string>();
                read_compile_options(options, defines, includepaths);

                m_cwd = Directory.GetCurrentDirectory();
                m_main_filename = new ustring(filename);
                clear_filecontents_cache();

                if (string.IsNullOrEmpty(stdoslpath))
                {
                    stdoslpath = find_stdoslpath(includepaths);
                }
                if (string.IsNullOrEmpty(stdoslpath) || !File.Exists(stdoslpath))
                {
                    warningfmt(new ustring(filename), 0, "Unable to find \"stdosl.h\"");
                }

                string preprocess_result;
                if (!preprocess_buffer(sourcecode, filename, stdoslpath, defines, includepaths, out preprocess_result))
                {
                    return false;
                }

                if (m_preprocess_only)
                {
                    Console.Out.Write(preprocess_result);
                }
                else
                {
                    bool parseerr = osl_parse_buffer(preprocess_result);
                    if (!parseerr)
                    {
                        if (shader() != null)
                        {
                            shader().typecheck();
                        }
                        else
                        {
                            errorfmt(new ustring(), 0, "No shader function defined");
                        }
                    }

                    if (m_debug)
                    {
                        Console.Out.Write(symtab().print());
                        if (shader() != null)
                        {
                            shader().print(Console.Out);
                        }
                    }

                    if (!error_encountered())
                    {
                        shader().codegen();
                        track_variable_dependencies();
                        track_variable_lifetimes();
                        check_for_illegal_writes();
                    }

                    if (!error_encountered())
                    {
                        if (m_output_filename.Length == 0)
                        {
                            m_output_filename = default_output_filename();
                        }

                        StringWriter oso_output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
                        oso_output.NewLine = "\n";
                        m_osofile = oso_output;

                        write_oso_file(string.Join(" ", options), preprocess_result);
                        osobuffer = oso_output.ToString();
                    }
                }

                return !error_encountered();
            }

            public void write_dependency_file(string filename)
            {
                if (string.IsNullOrEmpty(m_deps_filename))
                {
                    m_deps_filename = Path.ChangeExtension(filename, ".d");
                }
                string target = m_deps_target;
                if (string.IsNullOrEmpty(target))
                {
                    target = m_output_filename.Length != 0 ? m_output_filename : Path.ChangeExtension(filename, ".oso");
                }
                StringBuilder sb = new StringBuilder();
                sb.Append(target + ": " + filename);
                foreach (ustring dep in m_file_dependencies)
                {
                    string d = dep.@string();
                    if (d.EndsWith("stdosl.h", StringComparison.Ordinal) && !m_generate_system_deps)
                    {
                        continue;
                    }
                    if (d.StartsWith("<", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (d == filename)
                    {
                        continue;
                    }
                    sb.Append(" \\\n  " + d);
                }
                sb.Append("\n");
                try
                {
                    if (m_deps_filename == "stdout")
                    {
                        Console.Out.Write(sb.ToString());
                    }
                    else
                    {
                        File.WriteAllText(m_deps_filename, sb.ToString());
                    }
                }
                catch (Exception)
                {
                    errorfmt(new ustring(), 0, "Could not open dependency file '" + m_deps_filename + "' for writing");
                }
            }

            private struct GlobalTable
            {
                public string name;
                public TypeSpec type;
                public bool readonly_;

                public GlobalTable(string name, TypeSpec type, bool readonly_ = true)
                {
                    this.name = name;
                    this.type = type;
                    this.readonly_ = readonly_;
                }
            }

            public void initialize_globals()
            {
                GlobalTable[] globals =
                {
                    new GlobalTable("P", new TypeSpec(TypeDescs.TypePoint), false), new GlobalTable("I", new TypeSpec(TypeDescs.TypeVector), false),
                    new GlobalTable("N", new TypeSpec(TypeDescs.TypeNormal), false), new GlobalTable("Ng", new TypeSpec(TypeDescs.TypeNormal)),
                    new GlobalTable("u", new TypeSpec(TypeDescs.TypeFloat)), new GlobalTable("v", new TypeSpec(TypeDescs.TypeFloat)),
                    new GlobalTable("dPdu", new TypeSpec(TypeDescs.TypeVector)), new GlobalTable("dPdv", new TypeSpec(TypeDescs.TypeVector)),
                    new GlobalTable("Ps", new TypeSpec(TypeDescs.TypePoint)), new GlobalTable("Ci", new TypeSpec(TypeDescs.TypeColor, true), false),
                    new GlobalTable("time", new TypeSpec(TypeDescs.TypeFloat)), new GlobalTable("dtime", new TypeSpec(TypeDescs.TypeFloat)),
                    new GlobalTable("dPdtime", new TypeSpec(TypeDescs.TypeVector))
                };

                foreach (GlobalTable g in globals)
                {
                    Symbol s = new Symbol(new ustring(g.name), g.type, SymType.SymTypeGlobal);
                    s.readonly_(g.readonly_);
                    symtab().insert(s);
                }
            }

            public string default_output_filename()
            {
                if (m_shader != null && shader_decl() != null)
                {
                    return shader_decl().shadername().@string() + ".oso";
                }
                return "";
            }

            private void osofmt(string s)
            {
                m_osofile.Write(s);
            }

            public void write_oso_metadata(ASTNode metanode)
            {
                ASTvariable_declaration metavar = (ASTvariable_declaration)metanode;
                Symbol metasym = metavar.sym();
                TypeSpec ts = metasym.typespec();
                StringBuilder pdl = new StringBuilder();
                bool ok = metavar.param_default_literals(metasym, metavar.init(), pdl, ",");
                if (ok)
                {
                    osofmt("%meta{" + ts.@string() + "," + metasym.name() + "," + pdl + "} ");
                }
                else
                {
                    errorfmt(metanode.sourcefile(), metanode.sourceline(),
                             "Don't know how to print metadata " + metasym.name() + " (" + ts.@string() + ") with node type " + metavar.init().nodetypename());
                }
            }

            public void write_oso_const_value(ConstantSymbol sym)
            {
                TypeDesc type = sym.typespec().simpletype();
                TypeDesc elemtype = type.elementtype();
                int nelements = Math.Max(1, type.arraylen);
                StringBuilder o = new StringBuilder();
                string sep = nelements > 1 ? " " : "";
                if (elemtype == new TypeDesc(TypeDesc.STRING))
                {
                    for (int i = 0; i < nelements; ++i)
                    {
                        o.Append("\"" + Strutil.escape_chars(sym.get_string(i).@string()) + "\"" + sep);
                    }
                }
                else if (elemtype == new TypeDesc(TypeDesc.INT))
                {
                    for (int i = 0; i < nelements; ++i)
                    {
                        o.Append(sym.get_int(i).ToString(System.Globalization.CultureInfo.InvariantCulture) + sep);
                    }
                }
                else if (elemtype == new TypeDesc(TypeDesc.FLOAT))
                {
                    for (int i = 0; i < nelements; ++i)
                    {
                        o.Append(Strutil.format_g9(sym.floatval(i)) + sep);
                    }
                }
                else if (PvtFunctions.equivalent(elemtype, TypeDescs.TypeVector))
                {
                    for (int i = 0; i < nelements; ++i)
                    {
                        Vec3 v = sym.get_vec3(i);
                        o.Append(Strutil.format_g9(v.x) + " " + Strutil.format_g9(v.y) + " " + Strutil.format_g9(v.z) + sep);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Don't know how to output this constant type");
                }
                osofmt(o.ToString());
            }

            public void write_oso_symbol(Symbol sym)
            {
                // symtype / datatype / name
                osofmt(sym.symtype_shortname() + "\t" + sym.typespec().@string() + "\t" + sym.mangled());

                ASTvariable_declaration v = null;
                if (sym.node() != null && sym.node().nodetype() == ASTNode.NodeType.variable_declaration_node)
                {
                    v = (ASTvariable_declaration)sym.node();
                }

                bool isparam = sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam;
                if (sym.symtype() == SymType.SymTypeConst)
                {
                    osofmt("\t");
                    write_oso_const_value((ConstantSymbol)sym);
                    osofmt("\t");
                }
                else if (v != null && isparam)
                {
                    StringBuilder o = new StringBuilder();
                    v.param_default_literals(sym, v.init(), o);
                    osofmt("\t" + o + "\t");
                }

                int hints = 0;

                // %meta{} encodes metadata
                if (v != null)
                {
                    for (ASTNode m = v.meta(); m != null; m = m.next())
                    {
                        if (hints++ == 0)
                        {
                            osofmt("\t");
                        }
                        write_oso_metadata(m);
                    }
                }

                // %read and %write give the range of ops over which a symbol is used
                osofmt((hints++ != 0 ? " " : "\t") + "%read{" + sym.firstread() + "," + sym.lastread() + "} %write{" + sym.firstwrite() + ","
                       + sym.lastwrite() + "}");

                // %struct, %structfields, %structfieldtypes
                if (sym.typespec().is_structure())
                {
                    StructSpec structspec = sym.typespec().structspec();
                    StringBuilder fieldlist = new StringBuilder();
                    StringBuilder signature = new StringBuilder();
                    for (int i = 0; i < structspec.numfields(); ++i)
                    {
                        if (i > 0)
                        {
                            fieldlist.Append(",");
                        }
                        fieldlist.Append(structspec.field(i).name.@string());
                        signature.Append(structspec.field(i).type.code_from_type());
                    }
                    osofmt((hints++ != 0 ? " " : "\t") + "%struct{\"" + structspec.mangled() + "\"} %structfields{" + fieldlist
                           + "} %structfieldtypes{\"" + signature + "\"} %structnfields{" + structspec.numfields() + "}");
                }
                // %mystruct and %mystructfield link field symbols back to their struct
                if (sym.fieldid() >= 0)
                {
                    ASTvariable_declaration vd = (ASTvariable_declaration)sym.node();
                    if (vd != null)
                    {
                        osofmt((hints++ != 0 ? " " : "\t") + "%mystruct{" + vd.sym().mangled() + "} %mystructfield{" + sym.fieldid() + "}");
                    }
                }

                // %derivs marks symbols that need to carry derivatives
                if (sym.has_derivs())
                {
                    osofmt((hints++ != 0 ? " " : "\t") + "%derivs");
                }

                // %initexpr marks parameters whose default needs code
                if (isparam && sym.has_init_ops())
                {
                    osofmt((hints++ != 0 ? " " : "\t") + "%initexpr");
                }

                osofmt("\n");
            }

            public void write_oso_file(string options, string preprocessed_source)
            {
                osofmt("OpenShadingLanguage " + OSO_FILE_VERSION_MAJOR + "." + OSO_FILE_VERSION_MINOR.ToString("D2") + "\n");
                osofmt("# Compiled by oslc " + OSL_LIBRARY_VERSION_STRING + "\n");
                osofmt("# options: " + options + "\n");

                ASTshader_declaration shaderdecl = shader_decl();
                osofmt(shaderdecl.shadertypename() + " " + shaderdecl.shadername());

                // output global hints and metadata
                int hints = 0;
                for (ASTNode m = shaderdecl.metadata(); m != null; m = m.next())
                {
                    if (hints++ == 0)
                    {
                        osofmt("\t");
                    }
                    write_oso_metadata(m);
                }

                osofmt("\n");

                // Output params, so they are first
                foreach (Symbol s in symtab())
                {
                    if (s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam)
                    {
                        write_oso_symbol(s);
                    }
                }
                // Output globals, locals, temps, const
                foreach (Symbol s in symtab())
                {
                    if (s.symtype() == SymType.SymTypeLocal || s.symtype() == SymType.SymTypeTemp || s.symtype() == SymType.SymTypeGlobal
                        || s.symtype() == SymType.SymTypeConst)
                    {
                        // Don't bother writing symbols that are never used
                        if (s.lastuse() >= 0)
                        {
                            write_oso_symbol(s);
                        }
                    }
                }

                // Output all opcodes
                int lastline = -1;
                ustring lastfile = new ustring();
                ustring lastmethod = new ustring("___uninitialized___");
                foreach (Opcode op in m_ircode)
                {
                    if (lastmethod != op.method())
                    {
                        osofmt("code " + op.method() + "\n");
                        lastmethod = op.method();
                        lastfile = new ustring();
                        lastline = -1;
                    }

                    if (!op.sourcefile().empty())
                    {
                        ustring file = op.sourcefile();
                        int line = op.sourceline();
                        if (file != lastfile || line != lastline)
                        {
                            osofmt("# " + file + ":" + line + "\n# " + retrieve_source(file, line) + "\n");
                        }
                    }

                    // Op name
                    osofmt("\t" + op.opname());

                    // Register arguments
                    if (op.nargs() != 0)
                    {
                        osofmt(op.opname().@string().Length < 8 ? "\t\t" : "\t");
                    }
                    for (int i = 0; i < op.nargs(); ++i)
                    {
                        int arg = op.firstarg() + i;
                        osofmt(m_opargs[arg].dealias().mangled() + " ");
                    }

                    // Jump targets
                    for (int i = 0; i < Opcode.max_jumps; ++i)
                    {
                        if (op.jump(i) >= 0)
                        {
                            osofmt(op.jump(i) + " ");
                        }
                    }

                    // Opcode hints
                    bool firsthint = true;

                    // %filename and %line, only when they differ from the previous op
                    if (!op.sourcefile().empty())
                    {
                        if (op.sourcefile() != lastfile)
                        {
                            lastfile = op.sourcefile();
                            osofmt((firsthint ? "\t" : " ") + "%filename{\"" + lastfile + "\"}");
                            firsthint = false;
                        }
                        if (op.sourceline() != lastline)
                        {
                            lastline = op.sourceline();
                            osofmt((firsthint ? "\t" : " ") + "%line{" + lastline + "}");
                            firsthint = false;
                        }
                    }

                    // %argrw documents which arguments are read, written, or both
                    if (op.nargs() != 0)
                    {
                        StringBuilder rw = new StringBuilder();
                        for (int i = 0; i < op.nargs(); ++i)
                        {
                            if (op.argwrite(i))
                            {
                                rw.Append(op.argread(i) ? "W" : "w");
                            }
                            else
                            {
                                rw.Append(op.argread(i) ? "r" : "-");
                            }
                        }
                        osofmt((firsthint ? "\t" : " ") + "%argrw{\"" + rw + "\"}");
                        firsthint = false;
                    }

                    // %argderivs documents which arguments have derivs taken of them by the op
                    if (op.argtakesderivs_all() != 0)
                    {
                        StringBuilder dv = new StringBuilder(" %argderivs{");
                        int any = 0;
                        for (int i = 0; i < op.nargs(); ++i)
                        {
                            if (op.argtakesderivs(i))
                            {
                                if (any++ != 0)
                                {
                                    dv.Append(",");
                                }
                                dv.Append(i);
                            }
                        }
                        dv.Append("}");
                        osofmt(dv.ToString());
                        firsthint = false;
                    }

                    osofmt("\n");
                }

                // If no code, still need a code marker
                if (lastmethod != main_method_name())
                {
                    osofmt("code " + main_method_name() + "\n");
                }

                osofmt("\tend\n");

                if (m_embed_source)
                {
                    osofmt("%preprocessed_source\n" + preprocessed_source + "\n");
                }

                m_osofile = null;
            }

            public void clear_filecontents_cache()
            {
                m_filecontents_map.Clear();
                m_last_sourcefile = new ustring();
                m_last_filecontents = null;
                m_last_sourceline = 1;  // the first line is called "1" for users
                m_last_sourceline_offset = 0;
            }

            public string retrieve_source(ustring filename, int line)
            {
                // If we don't have a valid "last", look it up in the cache
                if (filename != m_last_sourcefile || m_last_filecontents == null)
                {
                    m_last_sourceline = 1;
                    m_last_sourceline_offset = 0;
                    string contents;
                    if (!m_filecontents_map.TryGetValue(filename, out contents))
                    {
                        try
                        {
                            contents = File.ReadAllText(filename.@string());
                        }
                        catch (Exception)
                        {
                            m_last_sourcefile = new ustring();
                            m_last_filecontents = null;
                            return "<file not found>";
                        }
                        m_filecontents_map[filename] = contents;
                    }
                    m_last_sourcefile = filename;
                    m_last_filecontents = contents;
                }

                // Now read lines up to and including the line we want
                string text = m_last_filecontents;
                int start = 0;
                int orig_sourceline = line;
                if (line >= m_last_sourceline)
                {
                    // Shortcut: continue from where the last search left off
                    start = m_last_sourceline_offset;
                    line -= m_last_sourceline - 1;
                }
                else
                {
                    m_last_sourceline_offset = 0;
                    m_last_sourceline = 1;
                }
                int offset = m_last_sourceline_offset;
                for (; line > 1; --line)
                {
                    int p = text.IndexOf('\n', start);
                    if (p < 0)
                    {
                        return "<line not found>";
                    }
                    offset += p + 1 - start;
                    start = p + 1;
                }
                int end = text.IndexOf('\n', start);
                string s = end < 0 ? text.Substring(start) : text.Substring(start, end - start);
                m_last_sourceline_offset = offset;
                m_last_sourceline = orig_sourceline;
                return s;
            }

            public void push_nesting(bool isloop = false)
            {
                ++m_total_nesting;
                if (isloop)
                {
                    ++m_loop_nesting;
                }
                if (current_function() != null)
                {
                    current_function().push_nesting(isloop);
                }
            }

            public void pop_nesting(bool isloop = false)
            {
                --m_total_nesting;
                if (isloop)
                {
                    --m_loop_nesting;
                }
                if (current_function() != null)
                {
                    current_function().pop_nesting(isloop);
                }
            }

            public void struct_field_pair(Symbol sym1, Symbol sym2, int fieldnum, out Symbol field1, out Symbol field2)
            {
                // Find the StructSpec for the type of struct that the symbols are
                StructSpec structspec = sym1.typespec().structspec();
                // Find the FieldSpec for the field we are interested in
                StructSpec.FieldSpec field = structspec.field(fieldnum);
                // Construct mangled names that describe the symbols for the individual fields
                ustring name1 = new ustring(sym1.mangled() + "." + field.name);
                ustring name2 = new ustring(sym2.mangled() + "." + field.name);
                field1 = symtab().find_exact(name1);
                field2 = symtab().find_exact(name2);
            }

            public void struct_field_pair(StructSpec structspec, int fieldnum, ustring sym1, ustring sym2, out Symbol field1, out Symbol field2)
            {
                StructSpec.FieldSpec field = structspec.field(fieldnum);
                ustring name1 = new ustring(sym1 + "." + field.name);
                ustring name2 = new ustring(sym2 + "." + field.name);
                field1 = symtab().find_exact(name1);
                field2 = symtab().find_exact(name2);
                if (field1 == null || field2 == null)
                {
                    throw new InvalidOperationException("struct_field_pair: missing field symbol " + name1 + " or " + name2);
                }
            }

            /// Verify that the given symbol (written by the given op) is legal to be written.
            public void check_write_legality(Opcode op, int opnum, Symbol sym)
            {
                // We can never write to constant symbols
                if (sym.symtype() == SymType.SymTypeConst)
                {
                    errorfmt(op.sourcefile(), op.sourceline(), "Attempted to write to a constant value");
                }

                // Params can only write if it's part of their initialization
                if (sym.symtype() == SymType.SymTypeParam && (opnum < sym.initbegin() || opnum >= sym.initend()))
                {
                    errorfmt(op.sourcefile(), op.sourceline(), "cannot write to non-output parameter \"" + sym.name() + "\"");
                }
            }

            public void check_for_illegal_writes()
            {
                int opnum = 0;
                foreach (Opcode op in m_ircode)
                {
                    for (int a = 0; a < op.nargs(); ++a)
                    {
                        Symbol s = m_opargs[op.firstarg() + a];
                        if (op.argwrite(a))
                        {
                            check_write_legality(op, opnum, s);
                        }
                    }
                    ++opnum;
                }
            }

            // Add to the dependency map that "A depends on B"
            private static void add_dependency(Dictionary<Symbol, HashSet<Symbol>> dmap, Symbol A, Symbol B)
            {
                HashSet<Symbol> set;
                if (!dmap.TryGetValue(A, out set))
                {
                    set = new HashSet<Symbol>();
                    dmap[A] = set;
                }
                set.Add(B);
            }

            private static void mark_symbol_derivatives(Dictionary<Symbol, HashSet<Symbol>> dmap, HashSet<Symbol> visited, Symbol sym)
            {
                HashSet<Symbol> deps;
                if (!dmap.TryGetValue(sym, out deps))
                {
                    return;
                }
                foreach (Symbol r in new List<Symbol>(deps))
                {
                    if (visited.Add(r))
                    {
                        r.has_derivs(true);
                        mark_symbol_derivatives(dmap, visited, r);
                    }
                }
            }

            /// For each op mark its 'written' arguments as dependent upon its 'read' arguments, then tag everything that feeds a derivative.
            /// This is conservative: it overestimates dependencies but never misses one, so it must run BEFORE temporaries are coalesced.
            public void track_variable_dependencies()
            {
                m_symdeps.Clear();
                List<Symbol> read = new List<Symbol>();
                List<Symbol> written = new List<Symbol>();
                // A pseudo-symbol that "depends on" whatever things have derivs taken of them
                if (m_derivsym == null)
                {
                    m_derivsym = new Symbol(new ustring("$derivs"), new TypeSpec(), SymType.SymTypeGlobal);
                }
                for (int opnum = 0; opnum < m_ircode.Count; ++opnum)
                {
                    Opcode op = m_ircode[opnum];
                    read.Clear();
                    written.Clear();
                    syms_used_in_op_range(opnum, opnum + 1, read, written);

                    bool deriv = op.argtakesderivs_all() != 0;
                    foreach (Symbol wsym in written)
                    {
                        foreach (Symbol rsym in read)
                        {
                            if (rsym.symtype() != SymType.SymTypeConst)
                            {
                                add_dependency(m_symdeps, wsym, rsym);
                            }
                        }
                        if (deriv)
                        {
                            for (int a = 0; a < op.nargs(); ++a)
                            {
                                if (op.argtakesderivs(a))
                                {
                                    add_dependency(m_symdeps, m_derivsym, m_opargs[a + op.firstarg()]);
                                }
                            }
                        }
                    }
                }

                // Recursively tag all symbols that need derivatives
                HashSet<Symbol> visited = new HashSet<Symbol>();
                mark_symbol_derivatives(m_symdeps, visited, m_derivsym);
            }

            // Is the symbol coalescable?
            private static bool coalescable(Symbol s)
            {
                return s.symtype() == SymType.SymTypeTemp && s.everused() && s.dealias() == s && !s.typespec().is_structure() && s.fieldid() < 0;
            }

            /// Coalesce temporaries: merge ones of equivalent type and non-overlapping lifetimes.
            public static void coalesce_temporaries(List<Symbol> symtab)
            {
                int ncoalesced = 1;
                while (ncoalesced != 0)
                {
                    ncoalesced = 0;
                    for (int si = 0; si < symtab.Count; ++si)
                    {
                        Symbol s = symtab[si];
                        if (!coalescable(s))
                        {
                            continue;
                        }

                        int sfirst = s.firstuse();
                        int slast = s.lastuse();

                        for (int ti = si + 1; ti < symtab.Count; ++ti)
                        {
                            Symbol t = symtab[ti];
                            if (coalescable(t) && PvtFunctions.equivalent(s.typespec(), t.typespec()) && (slast < t.firstuse() || sfirst > t.lastuse()))
                            {
                                // Make all future t references alias to s
                                t.alias(s);
                                // s gets union of the lifetimes
                                s.union_rw(t.firstread(), t.lastread(), t.firstwrite(), t.lastwrite());
                                sfirst = s.firstuse();
                                slast = s.lastuse();
                                // t gets marked as unused
                                t.clear_rw();
                                ++ncoalesced;
                            }
                        }
                    }
                }
            }

            public bool op_uses_sym(Opcode op, Symbol sym, bool read = true, bool write = true)
            {
                for (int i = 0; i < op.nargs(); ++i)
                {
                    if (m_opargs[i + op.firstarg()] == sym && ((read && op.argread(i)) || (write && op.argwrite(i))))
                    {
                        return true;
                    }
                }
                return false;
            }

            public void syms_used_in_op_range(int opbegin, int opend, List<Symbol> rsyms, List<Symbol> wsyms)
            {
                for (int n = opbegin; n < opend; ++n)
                {
                    Opcode op = m_ircode[n];
                    for (int i = 0; i < op.nargs(); ++i)
                    {
                        Symbol s = m_opargs[i + op.firstarg()];
                        if (rsyms != null && op.argread(i) && !rsyms.Contains(s))
                        {
                            rsyms.Add(s);
                        }
                        if (wsyms != null && op.argwrite(i) && !wsyms.Contains(s))
                        {
                            wsyms.Add(s);
                        }
                    }
                }
            }
        }
    }
}
