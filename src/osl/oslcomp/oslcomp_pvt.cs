// OSL port: liboslcomp/oslcomp_pvt.h (the declaration of OSLCompilerImpl)
//
// The class is split across files as the C++ is split across translation units: this file has the members that the header defines inline,
// oslcomp.cs is oslcomp.cpp, codegen.cs and typecheck.cs hold the OSLCompilerImpl methods of those files.

using System;
using System.Collections.Generic;
using System.IO;
using OIIO;

namespace OSL
{
    namespace pvt
    {
        public partial class OSLCompilerImpl
        {
            private ustring m_filename;                      ///< Current file we're parsing
            private int m_lineno;                            ///< Current line we're parsing
            private string m_output_filename;                ///< Output filename
            private ustring m_main_filename;                 ///< Main input filename
            private string m_cwd;                            ///< Current working directory
            private ASTNode m_shader;                        ///< The shader's syntax tree
            private ErrorHandler m_errhandler;               ///< Error handler
            private bool m_err;                              ///< Has an error occurred?
            private SymbolTable m_symtab;                    ///< Symbol table
            private List<ASTNode> m_func_decls = new List<ASTNode>();  ///< Function decls
            private TypeSpec m_current_typespec;             ///< Currently-declared type
            private bool m_current_output;                   ///< Currently-declared output status
            private bool m_verbose;                          ///< Verbose mode
            private bool m_quiet;                            ///< Quiet mode
            private bool m_debug;                            ///< Debug mode
            private bool m_preprocess_only;                  ///< Preprocess only?
            private bool m_generate_deps = false;            ///< Generate dependencies? -MD or -MMD?
            private bool m_generate_system_deps = false;     ///< Generate system header deps? -MD
            private bool m_embed_source = false;             ///< Embed preprocessed source in oso?
            private bool m_err_on_warning;                   ///< Treat warnings as errors?
            private int m_optimizelevel;                     ///< Optimization level
            private List<Opcode> m_ircode = new List<Opcode>();   ///< Generated IR code
            private List<Symbol> m_opargs = new List<Symbol>();   ///< Arguments for all instructions
            private int m_next_temp;                         ///< Next temporary symbol index
            private int m_next_const;                        ///< Next const symbol index
            private List<ConstantSymbol> m_const_syms = new List<ConstantSymbol>();  ///< All consts we've made
            private TextWriter m_osofile;                    ///< Open .oso stream for output
            private ustring m_codegenmethod;                 ///< Current method we're generating code for
            private Stack<FunctionSymbol> m_function_stack = new Stack<FunctionSymbol>();  ///< Stack of called funcs
            private int m_total_nesting;                     ///< total conditional nesting level (0 == none)
            private int m_loop_nesting;                      ///< just loop nesting level (0 == none)
            private Dictionary<Symbol, HashSet<Symbol>> m_symdeps = new Dictionary<Symbol, HashSet<Symbol>>();  ///< Symbol-to-symbol dependencies
            private Symbol m_derivsym;                       ///< Pseudo-symbol to track deriv dependencies
            private int m_main_method_start;                 ///< Instruction where 'main' starts
            private bool m_declaring_shader_formals;         ///< Are we declaring shader formals?
            private HashSet<(ustring, int)> m_nowarn_lines = new HashSet<(ustring, int)>();  ///< Lines for 'nowarn'
            private Dictionary<ustring, string> m_filecontents_map = new Dictionary<ustring, string>();  ///< Contents of source files we've seen
            private ustring m_last_sourcefile;               ///< Last filename for retrieve_source
            private string m_last_filecontents = null;       //< Last file contents
            private int m_last_sourceline;
            private int m_last_sourceline_offset;
            private string m_deps_filename;                  ///< Where to write deps? -MF
            private string m_deps_target;                    ///< Custom target: -MF
            private SortedSet<ustring> m_file_dependencies = new SortedSet<ustring>();  ///< All include file dependencies
            private Stack<TypeSpec> m_typespec_stack = new Stack<TypeSpec>();  ///< Just for function_declaration

            public ustring filename() { return m_filename; }

            public void filename(ustring f)
            {
                m_filename = f;
                m_file_dependencies.Add(f);
            }

            public int lineno() { return m_lineno; }
            public void lineno(int l) { m_lineno = l; }
            public int incr_lineno() { return ++m_lineno; }

            /// Return a reference to the error handler.
            public ErrorHandler errhandler() { return m_errhandler; }

            private static string TrimNewline(string msg)
            {
                if (msg.Length > 0 && msg[msg.Length - 1] == '\n')
                {
                    return msg.Substring(0, msg.Length - 1);
                }
                return msg;
            }

            /// Error reporting; the message arrives already formatted.
            public void errorfmt(ustring filename, int line, string msg)
            {
                msg = TrimNewline(msg);
                if (filename.size() > 0)
                {
                    m_errhandler.errorfmt(filename + ":" + line + ": error: " + msg);
                }
                else
                {
                    m_errhandler.errorfmt("error: " + msg);
                }
                m_err = true;
            }

            public void warningfmt(ustring filename, int line, string msg)
            {
                if (nowarn(filename, line))
                {
                    return;  // skip if the filename/line is on the nowarn list
                }
                msg = TrimNewline(msg);
                if (m_err_on_warning)
                {
                    errorfmt(filename, line, msg);
                    return;
                }
                if (filename.size() > 0)
                {
                    m_errhandler.warningfmt(filename + ":" + line + ": warning: " + msg);
                }
                else
                {
                    m_errhandler.warningfmt("warning: " + msg);
                }
            }

            public void infofmt(ustring filename, int line, string msg)
            {
                msg = TrimNewline(msg);
                if (filename.size() > 0)
                {
                    m_errhandler.infofmt(filename + ":" + line + ": info: " + msg);
                }
                else
                {
                    m_errhandler.infofmt("info: " + msg);
                }
            }

            public void messagefmt(ustring filename, int line, string msg)
            {
                msg = TrimNewline(msg);
                if (filename.size() > 0)
                {
                    m_errhandler.messagefmt(filename + ":" + line + ": " + msg);
                }
                else
                {
                    m_errhandler.messagefmt(msg);
                }
            }

            /// Has any error occurred thus far?
            public bool error_encountered() { return m_err; }

            /// Has a shader been defined?
            public bool shader_is_defined() { return m_shader != null; }

            /// Define the shader we're compiling with the given AST root.
            public void shader(ASTNode s) { m_shader = s; }
            public ASTNode shader() { return m_shader; }

            public SymbolTable symtab() { return m_symtab; }

            public TypeSpec current_typespec() { return m_current_typespec; }
            public void current_typespec(TypeSpec t) { m_current_typespec = t; }
            public bool current_output() { return m_current_output; }
            public void current_output(bool b) { m_current_output = b; }

            public void declaring_shader_formals(bool val) { m_declaring_shader_formals = val; }
            public bool declaring_shader_formals() { return m_declaring_shader_formals; }

            /// Return the index of the next op that will be emitted.
            public int next_op_label() { return m_ircode.Count; }

            /// Return the last op emitted.
            public Opcode lastop() { return m_ircode[m_ircode.Count - 1]; }

            /// Return the i-th op.
            public Opcode ircode(int index) { return m_ircode[index]; }

            /// Return the list of op arguments.
            public List<Symbol> opargs() { return m_opargs; }

            public ustring codegen_method() { return m_codegenmethod; }

            public string output_filename() { return m_output_filename; }

            public void push_function(FunctionSymbol func)
            {
                m_function_stack.Push(func);
                func.init_nesting();
            }

            public void pop_function() { m_function_stack.Pop(); }

            public FunctionSymbol current_function() { return m_function_stack.Count == 0 ? null : m_function_stack.Peek(); }

            public int nesting_level(bool loops = false) { return loops ? m_loop_nesting : m_total_nesting; }

            public ustring main_filename() { return m_main_filename; }
            public string cwd() { return m_cwd; }
            public bool debug() { return m_debug; }

            public void remember_function_decl(ASTfunction_declaration f) { m_func_decls.Add(f); }

            public void pragma_nowarn() { m_nowarn_lines.Add((filename(), lineno() + 1)); }

            public bool nowarn(ustring filename, int line) { return m_nowarn_lines.Contains((filename, line)); }

            public Stack<TypeSpec> typespec_stack() { return m_typespec_stack; }

            private void track_variable_lifetimes()
            {
                SymtabFunctions.track_variable_lifetimes_main(m_ircode, m_opargs, symtab().allsyms());
            }

            private void coalesce_temporaries() { coalesce_temporaries(m_symtab.allsyms()); }

            private bool op_reads_sym(Opcode op, Symbol sym) { return op_uses_sym(op, sym, true, false); }
            private bool op_writes_sym(Opcode op, Symbol sym) { return op_uses_sym(op, sym, false, true); }

            private ASTshader_declaration shader_decl()
            {
                if (m_shader.nodetype() != ASTNode.NodeType.shader_declaration_node)
                {
                    return null;
                }
                return (ASTshader_declaration)m_shader;
            }
        }
    }
}
