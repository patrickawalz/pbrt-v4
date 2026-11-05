// OSL port: liboslcomp/ast.h and ast.cpp
//
// The syntax tree. Every node class is partial: this file has the class declarations and what ast.cpp defines; typecheck.cs and codegen.cs add
// the typecheck() and codegen() overrides, as typecheck.cpp and codegen.cpp do in the C++. A null child is a valid entry in the child list.
// C++ reference-counted ASTNode::ref is a plain object reference here.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OIIO;
using static OIIO.TypeDescs;

namespace OSL
{
    namespace pvt
    {
        public abstract partial class ASTNode
        {
            public enum NodeType
            {
                unknown_node,
                shader_declaration_node,
                function_declaration_node,
                variable_declaration_node,
                compound_initializer_node,
                variable_ref_node,
                preincdec_node,
                postincdec_node,
                index_node,
                structselect_node,
                conditional_statement_node,
                loop_statement_node,
                loopmod_statement_node,
                return_statement_node,
                binary_expression_node,
                unary_expression_node,
                assign_expression_node,
                ternary_expression_node,
                comma_operator_node,
                typecast_expression_node,
                type_constructor_node,
                function_call_node,
                literal_node,
                _last_node
            }

            // The operators; ints, as the C++ unscoped enum converts to int
            public const int Nothing = 0, Decr = 1, Incr = 2, Assign = 3, Mul = 4, Div = 5, Add = 6, Sub = 7, Mod = 8, Equal = 9, NotEqual = 10,
                             Greater = 11, Less = 12, GreaterEqual = 13, LessEqual = 14, BitAnd = 15, BitOr = 16, Xor = 17, Compl = 18,
                             And = 19, Or = 20, Not = 21, ShiftLeft = 22, ShiftRight = 23;

            protected NodeType m_nodetype;               ///< Type of node this is
            protected ASTNode m_next;                    ///< Next node in the list
            protected OSLCompilerImpl m_compiler;        ///< Back-pointer to the compiler
            protected ustring m_sourcefile;              ///< Filename of source where the node came from
            protected int m_sourceline;                  ///< Line number in source where the node came from
            protected List<ASTNode> m_children = new List<ASTNode>();  ///< Child nodes
            protected int m_op;                          ///< Operator selection
            protected TypeSpec m_typespec;               ///< Data type of this node
            protected bool m_is_lvalue;                  ///< Is it an lvalue (assignable?)

            private void Init(NodeType nodetype, OSLCompilerImpl compiler, int op)
            {
                m_nodetype = nodetype;
                m_compiler = compiler;
                m_sourcefile = compiler.filename();
                m_sourceline = compiler.lineno();
                m_op = op;
                m_is_lvalue = false;
                m_typespec = new TypeSpec();
            }

            public ASTNode(NodeType nodetype, OSLCompilerImpl compiler)
            {
                Init(nodetype, compiler, 0);
            }

            public ASTNode(NodeType nodetype, OSLCompilerImpl compiler, int op)
            {
                Init(nodetype, compiler, op);
            }

            public ASTNode(NodeType nodetype, OSLCompilerImpl compiler, int op, ASTNode a)
            {
                Init(nodetype, compiler, op);
                addchild(a);
            }

            public ASTNode(NodeType nodetype, OSLCompilerImpl compiler, int op, ASTNode a, ASTNode b)
            {
                Init(nodetype, compiler, op);
                addchild(a);
                addchild(b);
            }

            public ASTNode(NodeType nodetype, OSLCompilerImpl compiler, int op, ASTNode a, ASTNode b, ASTNode c)
            {
                Init(nodetype, compiler, op);
                addchild(a);
                addchild(b);
                addchild(c);
            }

            public ASTNode(NodeType nodetype, OSLCompilerImpl compiler, int op, ASTNode a, ASTNode b, ASTNode c, ASTNode d)
            {
                Init(nodetype, compiler, op);
                addchild(a);
                addchild(b);
                addchild(c);
                addchild(d);
            }

            /// Reverse a list of nodes linked through next().
            public static ASTNode reverse(ASTNode list)
            {
                ASTNode new_list = null;
                while (list != null)
                {
                    ASTNode next = list.next();
                    list.m_next = new_list;
                    new_list = list;
                    list = next;
                }
                return new_list;
            }

            /// Concatenate two node lists; either may be null.
            public static ASTNode concat(ASTNode A, ASTNode B)
            {
                if (A != null)  // A is valid, B may or may not be
                {
                    return B != null ? A.append(B) : A;
                }
                return B;  // A not valid, so just go with B
            }

            public virtual void print(TextWriter o, int indentlevel = 0)
            {
                indent(o, indentlevel);
                o.Write("(" + nodetypename() + " :     (type: " + typespec() + ") " + (opname() != null ? opname() : "") + "\n");
                printchildren(o, indentlevel);
                indent(o, indentlevel);
                o.Write(")\n");
            }

            public NodeType nodetype() { return m_nodetype; }

            /// Return a name denoting the type of node this is.
            public abstract string nodetypename();

            /// The type of the node.
            public TypeSpec typespec() { return m_typespec; }

            /// The name of the operator, if any (e.g. "+").
            public virtual string opname() { return null; }

            /// Number of children.
            public int nchildren() { return m_children.Count; }

            /// Return a name for the i-th child.
            public abstract string childname(int i);

            /// Append a node to the end of the list that begins with this one.
            public ASTNode append(ASTNode newnode)
            {
                ASTNode n = this;
                while (n.nextptr() != null)
                {
                    n = n.nextptr();
                }
                n.m_next = newnode;
                return this;
            }

            public void detach_next() { m_next = null; }

            public ustring sourcefile() { return m_sourcefile; }
            public int sourceline() { return m_sourceline; }
            public void sourceline(int line) { m_sourceline = line; }

            public void errorfmt(string msg) { m_compiler.errorfmt(sourcefile(), sourceline(), msg); }
            public void warningfmt(string msg) { m_compiler.warningfmt(sourcefile(), sourceline(), msg); }
            public void infofmt(string msg) { m_compiler.infofmt(sourcefile(), sourceline(), msg); }
            public void messagefmt(string msg) { m_compiler.messagefmt(sourcefile(), sourceline(), msg); }

            /// Is this an lvalue (assignable)?
            public bool is_lvalue() { return m_is_lvalue; }

            /// Return the next node in the list.
            public ASTNode next() { return m_next; }
            public ASTNode nextptr() { return m_next; }

            protected static void indent(TextWriter o, int indentlevel = 0)
            {
                while (indentlevel-- > 0)
                {
                    o.Write("    ");
                }
            }

            protected static void printlist(TextWriter o, ASTNode A, int indentlevel)
            {
                for (ASTNode n = A; n != null; n = n.nextptr())
                {
                    n.print(o, indentlevel);
                }
            }

            protected static int listlength(ASTNode A)
            {
                int len = 0;
                for (ASTNode n = A; n != null; n = n.nextptr())
                {
                    ++len;
                }
                return len;
            }

            protected static ASTNode list_nth(ASTNode A, int n)
            {
                for (ASTNode node = A; node != null; node = node.nextptr(), --n)
                {
                    if (n == 0)
                    {
                        return node;
                    }
                }
                return null;
            }

            /// Turn a list into a vector of nodes.
            protected static void list_to_vec(ASTNode A, List<ASTNode> vec)
            {
                vec.Clear();
                for (ASTNode node = A; node != null; node = node.next())
                {
                    vec.Add(node);
                }
            }

            /// Turn a vector of nodes back into a list.
            protected static ASTNode vec_to_list(List<ASTNode> vec)
            {
                if (vec.Count > 0)
                {
                    for (int i = 0; i < vec.Count - 1; ++i)
                    {
                        vec[i].m_next = vec[i + 1];
                    }
                    vec[vec.Count - 1].m_next = null;
                    return vec[0];
                }
                return null;
            }

            protected static string list_to_types_string(ASTNode node)
            {
                StringBuilder result = new StringBuilder();
                for (int i = 0; node != null; node = node.nextptr(), ++i)
                {
                    result.Append((i != 0 ? ", " : "") + node.typespec());
                }
                return result.ToString();
            }

            protected ASTNode child(int i) { return (i >= 0 && i < m_children.Count) ? m_children[i] : null; }
            protected void addchild(ASTNode n) { m_children.Add(n); }

            protected void printchildren(TextWriter o, int indentlevel = 0)
            {
                for (int i = 0; i < m_children.Count; ++i)
                {
                    if (child(i) == null)
                    {
                        continue;
                    }
                    indent(o, indentlevel);
                    if (childname(i) != null)
                    {
                        o.Write("  " + childname(i));
                    }
                    else
                    {
                        o.Write("  child" + i);
                    }
                    o.Write(": ");
                    if (typespec() != new TypeSpec() && child(i).next() == null)
                    {
                        o.Write(" (type: " + typespec() + ")");
                    }
                    o.Write("\n");
                    printlist(o, child(i), indentlevel + 1);
                }
            }

            protected void clearchildren() { m_children.Clear(); }

            /// Is the symbol at the root of the given lvalue expression writeable? Warns (unless quiet) if not.
            protected bool check_symbol_writeability(ASTNode var, bool quiet, out Symbol dest_sym)
            {
                dest_sym = null;
                if (var.nodetype() == NodeType.index_node)
                {
                    return check_symbol_writeability(((ASTindex)var).lvalue());
                }
                if (var.nodetype() == NodeType.structselect_node)
                {
                    return check_symbol_writeability(((ASTstructselect)var).lvalue());
                }

                Symbol dest = null;
                if (var.nodetype() == NodeType.variable_ref_node)
                {
                    dest = ((ASTvariable_ref)var).sym();
                }
                else if (var.nodetype() == NodeType.variable_declaration_node)
                {
                    dest = ((ASTvariable_declaration)var).sym();
                }

                if (dest != null)
                {
                    dest_sym = dest;
                    if (dest.readonly_())
                    {
                        if (!quiet)
                        {
                            warningfmt("cannot write to non-output parameter \"" + dest.name() + "\"");
                        }
                        return false;
                    }
                }
                return true;
            }

            protected bool check_symbol_writeability(ASTNode var, bool quiet = false)
            {
                Symbol ignored;
                return check_symbol_writeability(var, quiet, out ignored);
            }
        }

        public sealed partial class ASTshader_declaration : ASTNode
        {
            private ustring m_shadername;

            public ASTshader_declaration(OSLCompilerImpl comp, int stype, ustring name, ASTNode form, ASTNode stmts, ASTNode meta)
                : base(NodeType.shader_declaration_node, comp, stype, meta, form, stmts)
            {
                m_shadername = name;
                for (ASTNode arg = form; arg != null; arg = arg.nextptr())
                {
                    ASTvariable_declaration v = (ASTvariable_declaration)arg;
                    if (v.init() == null)
                    {
                        v.errorfmt("shader parameter '" + v.name() + "' requires a default initializer");
                    }
                    if (v.is_output() && v.typespec().is_unsized_array())
                    {
                        v.errorfmt("shader output parameter '" + v.name() + "' can't be unsized array");
                    }
                }
            }

            public override string nodetypename() { return "shader_declaration"; }

            public override string childname(int i)
            {
                string[] name = { "metadata", "formals", "statements" };
                return name[i];
            }

            public override void print(TextWriter o, int indentlevel = 0)
            {
                indent(o, indentlevel);
                o.Write("(" + nodetypename() + " " + shadertypename() + " \"" + m_shadername + "\"\n");
                printchildren(o, indentlevel);
                indent(o, indentlevel);
                o.Write(")\n");
            }

            public ASTNode metadata() { return child(0); }
            public ASTNode formals() { return child(1); }
            public ASTNode statements() { return child(2); }
            public ustring shadername() { return m_shadername; }
            public string shadertypename() { return PvtFunctions.shadertypename((ShaderType)m_op); }
        }

        public sealed partial class ASTfunction_declaration : ASTNode
        {
            private ustring m_name;
            private Symbol m_sym;
            private bool m_is_builtin;

            public ASTfunction_declaration(OSLCompilerImpl comp, TypeSpec type, ustring name, ASTNode form, ASTNode stmts, ASTNode meta,
                                           int sourceline_start = -1)
                : base(NodeType.function_declaration_node, comp, 0, meta, form, stmts)
            {
                m_name = name;
                m_sym = null;
                m_is_builtin = false;
                if (sourceline_start >= 0)
                {
                    m_sourceline = sourceline_start;
                }

                if (name.@string().StartsWith("___", StringComparison.Ordinal))
                {
                    errorfmt("\"" + name + "\" : sorry, can't start with three underscores");
                }

                Symbol existing_syms = comp.symtab().clash(name);
                if (existing_syms != null && existing_syms.symtype() != SymType.SymTypeFunction)
                {
                    errorfmt("\"" + name + "\" already declared in this scope as a " + existing_syms.typespec());
                    existing_syms = null;
                }

                m_typespec = type;
                string argcodes = m_typespec.code_from_type();
                for (ASTNode arg = form; arg != null; arg = arg.nextptr())
                {
                    TypeSpec t = arg.typespec();
                    if (t == new TypeSpec())  // UNKNOWN
                    {
                        m_typespec = TypeUnknown;
                        return;
                    }
                    argcodes += t.code_from_type();
                    ASTvariable_declaration v = (ASTvariable_declaration)arg;
                    if (v.init() != null)
                    {
                        v.errorfmt("function parameter '" + v.name() + "' may not have a default initializer.");
                    }
                }

                if (stmts != null)
                {
                    string err = "";
                    int current_scope = m_compiler.symtab().scopeid();
                    for (FunctionSymbol f = existing_syms as FunctionSymbol; f != null; f = f.nextpoly())
                    {
                        if (f.scope() == current_scope && f.argcodes().@string() == argcodes)
                        {
                            ASTfunction_declaration other = (ASTfunction_declaration)f.node();
                            if (other == null || (other.statements() != null || other.is_builtin()))
                            {
                                if (err.Length == 0)
                                {
                                    err = "Function '" + type + " " + name + " (" + list_to_types_string(form) + ")' redefined in the same scope\n"
                                          + "  Previous definitions:";
                                }
                                err += "\n    ";
                                if (other != null)
                                {
                                    err += Path.GetFileName(other.sourcefile().@string()) + ":" + other.sourceline();
                                }
                                else
                                {
                                    err += "built-in";
                                }
                            }
                        }
                    }
                    if (err.Length > 0)
                    {
                        warningfmt(err);
                    }
                }

                m_sym = new FunctionSymbol(name, type, this);
                func().nextpoly(existing_syms as FunctionSymbol);

                func().argcodes(new ustring(argcodes));
                m_compiler.symtab().insert(m_sym);

                typecheck(typespec());
            }

            public override string nodetypename() { return "function_declaration"; }

            public override string childname(int i)
            {
                string[] name = { "metadata", "formals", "statements" };
                return name[i];
            }

            public override void print(TextWriter o, int indentlevel = 0)
            {
                indent(o, indentlevel);
                o.Write(nodetypename() + " " + m_sym.mangled());
                if (m_sym.scope() != 0)
                {
                    o.Write(" (" + m_sym.name() + " in scope " + m_sym.scope() + ")");
                }
                o.Write("\n");
                printchildren(o, indentlevel);
            }

            public ASTNode metadata() { return child(0); }
            public ASTNode formals() { return child(1); }
            public ASTNode statements() { return child(2); }
            public FunctionSymbol func() { return (FunctionSymbol)m_sym; }
            public bool is_builtin() { return m_is_builtin; }

            public void add_meta(ASTNode metaref)
            {
                for (ASTNode meta = metaref; meta != null; meta = meta.nextptr())
                {
                    ASTvariable_declaration metavar = (ASTvariable_declaration)meta;
                    Symbol metasym = metavar.sym();
                    if (metasym.name() == "builtin")
                    {
                        m_is_builtin = true;
                        if (func().typespec().is_closure())  // It is a builtin closure
                        {
                            func().argcodes(new ustring(func().argcodes().@string() + "."));
                        }
                        for (ASTNode f = formals(); f != null; f = f.nextptr())
                        {
                            ASTvariable_declaration v = (ASTvariable_declaration)f;
                            if (v.is_output())
                            {
                                func().readwrite_special_case(true);
                            }
                        }
                    }
                    else if (metasym.name() == "derivs")
                    {
                        func().takes_derivs(true);
                    }
                    else if (metasym.name() == "printf_args")
                    {
                        func().printf_args(true);
                    }
                    else if (metasym.name() == "texture_args")
                    {
                        func().texture_args(true);
                    }
                    else if (metasym.name() == "rw")
                    {
                        func().readwrite_special_case(true);
                    }
                }
            }
        }

        public sealed partial class ASTvariable_declaration : ASTNode
        {
            private ustring m_name;     ///< Name of the symbol (unmangled)
            private Symbol m_sym;       ///< The symbol this declares
            private bool m_isparam;     ///< Is this a parameter?
            private bool m_isoutput;    ///< Is this an output parameter?
            private bool m_ismetadata;  ///< Is this declaration a piece of metadata?
            private bool m_initlist;    ///< Was initialized with a list (versus just an expr)
            private List<(ustring, ASTNode)> m_struct_field_inits = new List<(ustring, ASTNode)>();

            public ASTvariable_declaration(OSLCompilerImpl comp, TypeSpec type, ustring name, ASTNode init, bool isparam, bool ismeta,
                                           bool isoutput, bool initlist, int sourceline_start = -1)
                : base(NodeType.variable_declaration_node, comp, 0, init, null /* meta */)
            {
                m_name = name;
                m_sym = null;
                m_isparam = isparam;
                m_isoutput = isoutput;
                m_ismetadata = ismeta;
                m_initlist = initlist;
                if (sourceline_start >= 0)
                {
                    m_sourceline = sourceline_start;
                }

                if (m_initlist && init != null)
                {
                    ((ASTcompound_initializer)init).typecheck(type);
                }

                m_typespec = type;
                Symbol f = comp.symtab().clash(name);
                if (f != null && !m_ismetadata)
                {
                    string e = "\"" + name + "\" already declared in this scope";
                    if (f.node() != null)
                    {
                        string filename = Path.GetFileName(f.node().sourcefile().@string());
                        e += "\n\t\tprevious declaration was at " + filename + ":" + f.node().sourceline();
                    }
                    if (f.scope() == 0 && f.symtype() == SymType.SymTypeFunction && isparam)
                    {
                        warningfmt(e);
                    }
                    else
                    {
                        errorfmt(e);
                    }
                }
                if (name.@string().StartsWith("___", StringComparison.Ordinal))
                {
                    errorfmt("\"" + name + "\" : sorry, can't start with three underscores");
                }
                SymType symtype = isparam ? (isoutput ? SymType.SymTypeOutputParam : SymType.SymTypeParam) : SymType.SymTypeLocal;
                if (symtype == SymType.SymTypeLocal && name.@string().StartsWith("__debug_tmp__", StringComparison.Ordinal))
                {
                    symtype = SymType.SymTypeTemp;
                }
                m_sym = new Symbol(name, type, symtype, this);
                if (!m_ismetadata)
                {
                    m_compiler.symtab().insert(m_sym);
                }

                if (type.is_structure() || type.is_structure_array())
                {
                    m_compiler.add_struct_fields(type.structspec(), m_sym.name(), symtype, type.is_unsized_array() ? -1 : type.arraylength(), this,
                                                 init);
                }
            }

            public override string nodetypename() { return m_isparam ? "parameter" : "variable_declaration"; }

            public override string childname(int i)
            {
                string[] name = { "initializer", "metadata" };
                return name[i];
            }

            public override void print(TextWriter o, int indentlevel = 0)
            {
                indent(o, indentlevel);
                o.Write("(" + nodetypename() + " " + m_sym.mangled() + "\n");
                printchildren(o, indentlevel);
                indent(o, indentlevel);
                o.Write(")\n");
            }

            public ASTNode init() { return child(0); }
            public ASTNode meta() { return child(1); }

            public void add_meta(ASTNode meta)
            {
                while (nchildren() < 2)
                {
                    addchild(null);
                }
                m_children[1] = meta;  // beware changing the order!
            }

            public Symbol sym() { return m_sym; }
            public ustring name() { return m_name; }
            public bool is_output() { return m_isoutput; }

            public void register_struct_init(ustring name, ASTNode init) { m_struct_field_inits.Add((name, init)); }
        }

        public sealed partial class ASTvariable_ref : ASTNode
        {
            private ustring m_name;
            private Symbol m_sym;

            public ASTvariable_ref(OSLCompilerImpl comp, ustring name) : base(NodeType.variable_ref_node, comp)
            {
                m_name = name;
                m_sym = comp.symtab().find(name);
                if (m_sym == null)
                {
                    errorfmt("'" + name + "' was not declared in this scope");
                    return;
                }
                if (m_sym.symtype() == SymType.SymTypeFunction)
                {
                    errorfmt("function '" + name + "' can't be used as a variable");
                    return;
                }
                if (m_sym.symtype() == SymType.SymTypeType)
                {
                    errorfmt("type name '" + name + "' can't be used as a variable");
                    return;
                }
                m_typespec = m_sym.typespec();
            }

            public override string nodetypename() { return "variable_ref"; }
            public override string childname(int i) { return ""; }  // no children

            public override void print(TextWriter o, int indentlevel = 0)
            {
                indent(o, indentlevel);
                o.Write("(" + nodetypename() + " (type: " + (m_sym != null ? m_sym.typespec().@string() : "unknown") + ") "
                        + (m_sym != null ? m_sym.mangled() : m_name.@string()) + ")\n");
            }

            public ustring name() { return m_name; }
            public string mangled() { return m_sym.mangled(); }
            public Symbol sym() { return m_sym; }
        }

        public sealed partial class ASTpreincdec : ASTNode
        {
            public ASTpreincdec(OSLCompilerImpl comp, int op, ASTNode expr) : base(NodeType.preincdec_node, comp, op, expr)
            {
                check_symbol_writeability(expr);
            }

            public override string nodetypename() { return m_op == Incr ? "preincrement" : "predecrement"; }

            public override string childname(int i)
            {
                string[] name = { "expression" };
                return name[i];
            }

            public ASTNode var() { return child(0); }
        }

        public sealed partial class ASTpostincdec : ASTNode
        {
            public ASTpostincdec(OSLCompilerImpl comp, int op, ASTNode expr) : base(NodeType.postincdec_node, comp, op, expr)
            {
                check_symbol_writeability(expr);
            }

            public override string nodetypename() { return m_op == Incr ? "postincrement" : "postdecrement"; }

            public override string childname(int i)
            {
                string[] name = { "expression" };
                return name[i];
            }

            public ASTNode var() { return child(0); }
        }

        public sealed partial class ASTindex : ASTNode
        {
            public ASTindex(OSLCompilerImpl comp, ASTNode expr, ASTNode index, ASTNode index2 = null, ASTNode index3 = null)
                : base(NodeType.index_node, comp, 0, expr, index /*NO: index2, index3*/)
            {
                if (index2 != null)
                {
                    addchild(index2);
                }
                if (index3 != null)
                {
                    addchild(index3);
                }

                if (index2 == null && expr.nodetype() == NodeType.index_node && expr.nchildren() == 2)
                {
                    ASTNode newexpr = ((ASTindex)expr).lvalue();
                    ASTNode newindex = ((ASTindex)expr).index();
                    ASTNode newindex2 = index;
                    clearchildren();
                    addchild(newexpr);
                    expr = newexpr;
                    addchild(newindex);
                    index = newindex;
                    addchild(newindex2);
                    index2 = newindex2;
                }

                if (index2 == null)
                {
                    if (expr.typespec().is_array())  // array dereference
                    {
                        m_typespec = expr.typespec().elementtype();
                    }
                    else if (!expr.typespec().is_closure() && expr.typespec().is_triple())  // component access
                    {
                        m_typespec = TypeFloat;
                    }
                }
                else if (index3 == null)
                {
                    if (expr.typespec().is_matrix())  // matrix component access
                    {
                        m_typespec = TypeFloat;
                    }
                    else if (expr.typespec().is_array() && expr.typespec().elementtype().is_triple())  // triplearray[][]
                    {
                        m_typespec = TypeFloat;
                    }
                }
                else
                {
                    if (expr.typespec().is_array() && expr.typespec().elementtype().is_matrix())  // matrixarray[][]
                    {
                        m_typespec = TypeFloat;
                    }
                }

                if (m_typespec.is_unknown())
                {
                    errorfmt("indexing into non-array or non-component type");
                }
            }

            public override string nodetypename() { return "index"; }

            public override string childname(int i)
            {
                string[] name = { "expression", "index", "index" };
                return name[i];
            }

            public ASTNode lvalue() { return child(0); }
            public ASTNode index() { return child(1); }
            public ASTNode index2() { return child(2); }
            public ASTNode index3() { return child(3); }
        }

        public sealed partial class ASTstructselect : ASTNode
        {
            private ustring m_field;      ///< Name of the field
            private int m_structid;       ///< index of the structure
            private int m_fieldid;        ///< index of the field within the structure
            private ustring m_fieldname;  ///< Name of the field variable
            private Symbol m_fieldsym;    ///< Symbol of the field variable
            private ASTNode m_compindex;  ///< Redirection of named component index

            public ASTstructselect(OSLCompilerImpl comp, ASTNode expr, ustring field) : base(NodeType.structselect_node, comp, 0, expr)
            {
                m_field = field;
                m_structid = -1;
                m_fieldid = -1;
                m_fieldname = field;
                m_fieldsym = null;
                m_fieldsym = find_fieldsym(ref m_structid, ref m_fieldid);
                if (m_fieldsym != null)
                {
                    m_fieldname = m_fieldsym.name();
                    m_typespec = m_fieldsym.typespec();
                }
                else if (m_compindex != null)
                {
                    m_typespec = TypeFloat;  // These cases are always single floats
                }
            }

            private Symbol find_fieldsym(ref int structid, ref int fieldid)
            {
                ASTNode lv = lvalue();
                TypeSpec lvtype = lv.typespec();

                if (lvtype.is_color() && (fieldname() == "r" || fieldname() == "g" || fieldname() == "b"))
                {
                    fieldid = fieldname() == "r" ? 0 : (fieldname() == "g" ? 1 : 2);
                    m_compindex = new ASTindex(m_compiler, lv, new ASTliteral(m_compiler, fieldid));
                    m_is_lvalue = true;
                    return null;
                }
                else if (lvtype.is_vectriple() && (fieldname() == "x" || fieldname() == "y" || fieldname() == "z"))
                {
                    fieldid = fieldname() == "x" ? 0 : (fieldname() == "y" ? 1 : 2);
                    m_compindex = new ASTindex(m_compiler, lv, new ASTliteral(m_compiler, fieldid));
                    m_is_lvalue = true;
                    return null;
                }

                if (!lvtype.is_structure() && !lvtype.is_structure_array())
                {
                    errorfmt("type '" + lvtype + "' does not have a member '" + m_field + "'");
                    return null;
                }

                ustring structsymname = new ustring();
                TypeSpec structtype = new TypeSpec();
                find_structsym(lvalue(), ref structsymname, ref structtype);

                structid = structtype.structure();
                StructSpec structspec = structtype.structspec();
                fieldid = -1;
                for (int i = 0; i < structspec.numfields(); ++i)
                {
                    if (structspec.field(i).name == m_field)
                    {
                        fieldid = i;
                        break;
                    }
                }

                if (fieldid < 0)
                {
                    errorfmt("struct type '" + structspec.name() + "' does not have a member '" + m_field + "'");
                    return null;
                }

                StructSpec.FieldSpec fieldrec = structspec.field(fieldid);
                ustring fieldsymname = new ustring(structsymname + "." + fieldrec.name);
                return m_compiler.symtab().find(fieldsymname);
            }

            private static void find_structsym(ASTNode structnode, ref ustring structname, ref TypeSpec structtype)
            {
                if (structnode.nodetype() == NodeType.variable_ref_node)
                {
                    ASTvariable_ref var = (ASTvariable_ref)structnode;
                    structname = var.name();
                    structtype = var.typespec();
                }
                else if (structnode.nodetype() == NodeType.structselect_node)
                {
                    ASTstructselect thestruct = (ASTstructselect)structnode;
                    int structid = 0, fieldid = 0;
                    Symbol sym = thestruct.find_fieldsym(ref structid, ref fieldid);
                    structname = sym.name();
                    structtype = sym.typespec();
                }
                else if (structnode.nodetype() == NodeType.index_node)
                {
                    ASTindex arrayref = (ASTindex)structnode;
                    find_structsym(arrayref.lvalue(), ref structname, ref structtype);
                    structtype.make_array(0);  // clear its arrayness
                }
                else
                {
                    throw new InvalidOperationException("Malformed ASTstructselect");
                }
            }

            public override string nodetypename() { return "structselect"; }

            public override string childname(int i)
            {
                string[] name = { "structure" };
                return name[i];
            }

            public override void print(TextWriter o, int indentlevel = 0)
            {
                base.print(o, indentlevel);
                indent(o, indentlevel + 1);
                o.Write("select " + field() + "\n");
            }

            public ASTNode lvalue() { return child(0); }
            public ustring field() { return m_field; }
            public ustring fieldname() { return m_fieldname; }
            public Symbol fieldsym() { return m_fieldsym; }
            public int fieldid() { return m_fieldid; }
            public ASTindex compindex() { return (ASTindex)m_compindex; }
        }

        public sealed partial class ASTconditional_statement : ASTNode
        {
            public ASTconditional_statement(OSLCompilerImpl comp, ASTNode cond, ASTNode truestmt, ASTNode falsestmt = null)
                : base(NodeType.conditional_statement_node, comp, 0, cond, truestmt, falsestmt)
            {
            }

            public override string nodetypename() { return "conditional_statement"; }

            public override string childname(int i)
            {
                string[] name = { "condition", "truestatement", "falsestatement" };
                return name[i];
            }

            public ASTNode cond() { return child(0); }
            public ASTNode truestmt() { return child(1); }
            public ASTNode falsestmt() { return child(2); }
        }

        public sealed partial class ASTloop_statement : ASTNode
        {
            public const int LoopWhile = 0, LoopDo = 1, LoopFor = 2;

            public ASTloop_statement(OSLCompilerImpl comp, int looptype, ASTNode init, ASTNode cond, ASTNode iter, ASTNode stmt)
                : base(NodeType.loop_statement_node, comp, looptype, init, cond, iter, stmt)
            {
                if (cond == null)
                {
                    m_children[1] = new ASTliteral(comp, 1);
                }
            }

            public override string nodetypename() { return "loop_statement"; }

            public override string childname(int i)
            {
                string[] name = { "initializer", "condition", "iteration", "bodystatement" };
                return name[i];
            }

            public override string opname()
            {
                switch (m_op)
                {
                    case LoopWhile: return "while";
                    case LoopDo: return "dowhile";
                    case LoopFor: return "for";
                    default: throw new InvalidOperationException("unknown loop type");
                }
            }

            public ASTNode init() { return child(0); }
            public ASTNode cond() { return child(1); }
            public ASTNode iter() { return child(2); }
            public ASTNode stmt() { return child(3); }
        }

        public sealed partial class ASTloopmod_statement : ASTNode
        {
            public const int LoopModBreak = 0, LoopModContinue = 1;

            public ASTloopmod_statement(OSLCompilerImpl comp, int loopmodtype) : base(NodeType.loopmod_statement_node, comp, loopmodtype)
            {
            }

            public override string nodetypename() { return "loopmod_statement"; }
            public override string childname(int i) { return null; }  // no children

            public override string opname()
            {
                switch (m_op)
                {
                    case LoopModBreak: return "break";
                    case LoopModContinue: return "continue";
                    default: throw new InvalidOperationException("unknown loop modifier");
                }
            }
        }

        public sealed partial class ASTreturn_statement : ASTNode
        {
            public ASTreturn_statement(OSLCompilerImpl comp, ASTNode expr) : base(NodeType.return_statement_node, comp, 0, expr)
            {
            }

            public override string nodetypename() { return "return_statement"; }
            public override string childname(int i) { return "expression"; }  // only child

            public ASTNode expr() { return child(0); }
        }

        public partial class ASTtype_constructor : ASTNode
        {
            protected ASTtype_constructor(NodeType n, OSLCompilerImpl c, TypeSpec t, ASTNode a) : base(n, c, Nothing, a)
            {
                m_typespec = t;
            }

            public ASTtype_constructor(OSLCompilerImpl comp, TypeSpec typespec, ASTNode args)
                : this(NodeType.type_constructor_node, comp, typespec, args)
            {
            }

            public override string nodetypename() { return "type_constructor"; }

            public override string childname(int i)
            {
                string[] name = { "args" };
                return name[i];
            }

            public ASTNode args() { return child(0); }
        }

        public sealed partial class ASTcompound_initializer : ASTtype_constructor
        {
            private bool m_ctor;

            public ASTcompound_initializer(OSLCompilerImpl comp, ASTNode exprlist)
                : base(NodeType.compound_initializer_node, comp, new TypeSpec(), exprlist)
            {
                m_ctor = false;
            }

            public override string nodetypename() { return "compound_initializer"; }
            public override string childname(int i) { return canconstruct() ? "args" : "expression_list"; }

            public ASTNode initlist() { return child(0); }
            public bool canconstruct() { return m_ctor; }
            public void canconstruct(bool b) { m_ctor = b; }
        }

        public sealed partial class ASTassign_expression : ASTNode
        {
            public ASTassign_expression(OSLCompilerImpl comp, ASTNode var, int op, ASTNode expr)
                : base(NodeType.assign_expression_node, comp, op, var, expr)
            {
                if (op != Assign)
                {
                    m_op = Assign;
                    m_children[1] = new ASTbinary_expression(comp, op, var, expr);
                }

                check_symbol_writeability(var);
            }

            public override string nodetypename() { return "assign_expression"; }

            public override string childname(int i)
            {
                string[] name = { "variable", "expression" };
                return name[i];
            }

            public override string opname()
            {
                switch (m_op)
                {
                    case Assign: return "=";
                    case Mul: return "*=";
                    case Div: return "/=";
                    case Add: return "+=";
                    case Sub: return "-=";
                    case BitAnd: return "&=";
                    case BitOr: return "|=";
                    case Xor: return "^=";
                    case ShiftLeft: return "<<=";
                    case ShiftRight: return ">>=";
                    default: throw new InvalidOperationException("unknown assignment expression");
                }
            }

            public string opword()
            {
                switch (m_op)
                {
                    case Assign: return "assign";
                    case Mul: return "mul";
                    case Div: return "div";
                    case Add: return "add";
                    case Sub: return "sub";
                    case BitAnd: return "bitand";
                    case BitOr: return "bitor";
                    case Xor: return "xor";
                    case ShiftLeft: return "shl";
                    case ShiftRight: return "shr";
                    default: throw new InvalidOperationException("unknown assignment expression");
                }
            }

            public ASTNode var() { return child(0); }
            public ASTNode expr() { return child(1); }
        }

        public sealed partial class ASTunary_expression : ASTNode
        {
            private FunctionSymbol m_function_overload = null;

            public ASTunary_expression(OSLCompilerImpl comp, int op, ASTNode expr) : base(NodeType.unary_expression_node, comp, op, expr)
            {
                Symbol sym = comp.symtab().find(new ustring("__operator__" + opword() + "__"));
                if (sym != null && sym.symtype() == SymType.SymTypeFunction)
                {
                    m_function_overload = (FunctionSymbol)sym;
                }
            }

            public override string nodetypename() { return "unary_expression"; }

            public override string childname(int i)
            {
                string[] name = { "expression" };
                return name[i];
            }

            public override string opname()
            {
                switch (m_op)
                {
                    case Add: return "+";
                    case Sub: return "-";
                    case Not: return "!";
                    case Compl: return "~";
                    default: throw new InvalidOperationException("unknown unary expression");
                }
            }

            public string opword()
            {
                switch (m_op)
                {
                    case Add: return "add";
                    case Sub: return "neg";
                    case Not: return "not";
                    case Compl: return "compl";
                    default: throw new InvalidOperationException("unknown unary expression");
                }
            }

            public ASTNode expr() { return child(0); }
        }

        public sealed partial class ASTbinary_expression : ASTNode
        {
            private FunctionSymbol m_function_overload = null;

            public ASTbinary_expression(OSLCompilerImpl comp, int op, ASTNode left, ASTNode right)
                : base(NodeType.binary_expression_node, comp, op, left, right)
            {
                if (op != And && op != Or)
                {
                    ustring funcname = new ustring("__operator__" + opword() + "__");
                    Symbol sym = comp.symtab().find(funcname);
                    if (sym != null && sym.symtype() == SymType.SymTypeFunction)
                    {
                        m_function_overload = (FunctionSymbol)sym;
                    }
                }
            }

            /// Construct a binary expression, folding it to a literal when both sides are literals.
            public static ASTNode make(OSLCompilerImpl comp, int op, ASTNode left, ASTNode right)
            {
                if (left.nodetype() == NodeType.literal_node && right.nodetype() == NodeType.literal_node)
                {
                    ASTNode cf = null;  // constant-folded result
                    if (left.typespec().is_int() && right.typespec().is_int())
                    {
                        int lv = ((ASTliteral)left).intval();
                        int rv = ((ASTliteral)right).intval();
                        switch (op)
                        {
                            case Mul: cf = new ASTliteral(comp, unchecked(lv * rv)); break;
                            case Div: cf = new ASTliteral(comp, rv != 0 ? (rv == -1 ? unchecked(-lv) : lv / rv) : 0); break;
                            case Add: cf = new ASTliteral(comp, unchecked(lv + rv)); break;
                            case Sub: cf = new ASTliteral(comp, unchecked(lv - rv)); break;
                            case Mod: cf = new ASTliteral(comp, rv != 0 ? (rv == -1 ? 0 : lv % rv) : 0); break;
                            case Equal: cf = new ASTliteral(comp, lv == rv ? 1 : 0); break;
                            case NotEqual: cf = new ASTliteral(comp, lv != rv ? 1 : 0); break;
                            case Greater: cf = new ASTliteral(comp, lv > rv ? 1 : 0); break;
                            case Less: cf = new ASTliteral(comp, lv < rv ? 1 : 0); break;
                            case GreaterEqual: cf = new ASTliteral(comp, lv >= rv ? 1 : 0); break;
                            case LessEqual: cf = new ASTliteral(comp, lv <= rv ? 1 : 0); break;
                            case BitAnd: cf = new ASTliteral(comp, lv & rv); break;
                            case BitOr: cf = new ASTliteral(comp, lv | rv); break;
                            case Xor: cf = new ASTliteral(comp, lv ^ rv); break;
                            case ShiftLeft: cf = new ASTliteral(comp, lv << rv); break;
                            case ShiftRight: cf = new ASTliteral(comp, lv >> rv); break;
                            default: break;
                        }
                    }
                    else if (left.typespec().is_float() && right.typespec().is_float())
                    {
                        float lv = ((ASTliteral)left).floatval();
                        float rv = ((ASTliteral)right).floatval();
                        switch (op)
                        {
                            case Mul: cf = new ASTliteral(comp, lv * rv); break;
                            case Div: cf = new ASTliteral(comp, rv != 0 ? lv / rv : 0.0f); break;
                            case Add: cf = new ASTliteral(comp, lv + rv); break;
                            case Sub: cf = new ASTliteral(comp, lv - rv); break;
                            case Equal: cf = new ASTliteral(comp, lv == rv ? 1 : 0); break;
                            case NotEqual: cf = new ASTliteral(comp, lv != rv ? 1 : 0); break;
                            case Greater: cf = new ASTliteral(comp, lv > rv ? 1 : 0); break;
                            case Less: cf = new ASTliteral(comp, lv < rv ? 1 : 0); break;
                            case GreaterEqual: cf = new ASTliteral(comp, lv >= rv ? 1 : 0); break;
                            case LessEqual: cf = new ASTliteral(comp, lv <= rv ? 1 : 0); break;
                            default: break;
                        }
                    }
                    if (cf != null)
                    {
                        return cf;
                    }
                }

                return new ASTbinary_expression(comp, op, left, right);
            }

            public override string nodetypename() { return "binary_expression"; }

            public override string childname(int i)
            {
                string[] name = { "left", "right" };
                return name[i];
            }

            public override string opname()
            {
                switch (m_op)
                {
                    case Mul: return "*";
                    case Div: return "/";
                    case Add: return "+";
                    case Sub: return "-";
                    case Mod: return "%";
                    case Equal: return "==";
                    case NotEqual: return "!=";
                    case Greater: return ">";
                    case GreaterEqual: return ">=";
                    case Less: return "<";
                    case LessEqual: return "<=";
                    case BitAnd: return "&";
                    case BitOr: return "|";
                    case Xor: return "^";
                    case And: return "&&";
                    case Or: return "||";
                    case ShiftLeft: return "<<";
                    case ShiftRight: return ">>";
                    default: throw new InvalidOperationException("unknown binary expression");
                }
            }

            public string opword()
            {
                switch (m_op)
                {
                    case Mul: return "mul";
                    case Div: return "div";
                    case Add: return "add";
                    case Sub: return "sub";
                    case Mod: return "mod";
                    case Equal: return "eq";
                    case NotEqual: return "neq";
                    case Greater: return "gt";
                    case GreaterEqual: return "ge";
                    case Less: return "lt";
                    case LessEqual: return "le";
                    case BitAnd: return "bitand";
                    case BitOr: return "bitor";
                    case Xor: return "xor";
                    case And: return "and";
                    case Or: return "or";
                    case ShiftLeft: return "shl";
                    case ShiftRight: return "shr";
                    default: throw new InvalidOperationException("unknown binary expression");
                }
            }

            public ASTNode left() { return child(0); }
            public ASTNode right() { return child(1); }
        }

        public sealed partial class ASTternary_expression : ASTNode
        {
            public ASTternary_expression(OSLCompilerImpl comp, ASTNode cond, ASTNode trueexpr, ASTNode falseexpr)
                : base(NodeType.ternary_expression_node, comp, 0, cond, trueexpr, falseexpr)
            {
            }

            public override string nodetypename() { return "ternary_expression"; }

            public override string childname(int i)
            {
                string[] name = { "condition", "trueexpression", "falseexpression" };
                return name[i];
            }

            public ASTNode cond() { return child(0); }
            public ASTNode trueexpr() { return child(1); }
            public ASTNode falseexpr() { return child(2); }
        }

        public sealed partial class ASTcomma_operator : ASTNode
        {
            public ASTcomma_operator(OSLCompilerImpl comp, ASTNode exprlist) : base(NodeType.comma_operator_node, comp, Nothing, exprlist)
            {
            }

            public override string nodetypename() { return "comma_operator"; }
            public override string childname(int i) { return "expression_list"; }

            public ASTNode expr() { return child(0); }
        }

        public sealed partial class ASTtypecast_expression : ASTNode
        {
            public ASTtypecast_expression(OSLCompilerImpl comp, TypeSpec typespec, ASTNode expr)
                : base(NodeType.typecast_expression_node, comp, 0, expr)
            {
                m_typespec = typespec;
            }

            public override string nodetypename() { return "typecast_expression"; }

            public override string childname(int i)
            {
                string[] name = { "expr" };
                return name[i];
            }

            public ASTNode expr() { return child(0); }
        }

        public sealed partial class ASTfunction_call : ASTNode
        {
            private ustring m_name;                 ///< Name of the function being called
            private Symbol m_sym;                   ///< Symbol of the function
            private FunctionSymbol m_poly;          ///< The specific polymorphic variant
            private uint m_argread;                 ///< Bit field - which args are read
            private uint m_argwrite;                ///< Bit field - which args are written
            private uint m_argtakesderivs;          ///< Bit field - which args take derivs

            public ASTfunction_call(OSLCompilerImpl comp, ustring name, ASTNode args, FunctionSymbol funcsym = null)
                : base(NodeType.function_call_node, comp, 0, args)
            {
                m_name = name;
                m_sym = funcsym != null ? funcsym : comp.symtab().find(name);  // Look it up.
                m_poly = funcsym;       // Default - resolved symbol or null
                m_argread = ~1u;        // Default - all args are read except the first
                m_argwrite = 1;         // Default - first arg only is written by the op
                m_argtakesderivs = 0;   // Default - doesn't take derivs
                if (m_sym == null)
                {
                    errorfmt("function '" + name + "' was not declared in this scope");
                    return;
                }
                if (is_struct_ctr())
                {
                    return;  // It's a struct constructor
                }
                if (m_sym.symtype() != SymType.SymTypeFunction)
                {
                    errorfmt("'" + name + "' is not a function");
                    m_sym = null;
                    return;
                }
            }

            public override string nodetypename() { return "function_call"; }
            public override string childname(int i) { return "param" + i; }
            public override string opname() { return m_name.@string(); }

            public FunctionSymbol func() { return (FunctionSymbol)m_sym; }
            public ASTNode args() { return child(0); }
            public bool is_struct_ctr() { return m_sym != null && m_sym.is_structure(); }

            public bool is_user_function()
            {
                return !is_struct_ctr() && user_function() != null && !user_function().is_builtin();
            }

            public ASTfunction_declaration user_function()
            {
                return func() != null ? (ASTfunction_declaration)func().node() : null;
            }

            private void argread(int arg, bool val)
            {
                if (arg < 32)
                {
                    if (val)
                    {
                        m_argread |= (1u << arg);
                    }
                    else
                    {
                        m_argread &= ~(1u << arg);
                    }
                }
            }

            private void argwrite(int arg, bool val)
            {
                if (arg < 32)
                {
                    if (val)
                    {
                        m_argwrite |= (1u << arg);
                    }
                    else
                    {
                        m_argwrite &= ~(1u << arg);
                    }
                }
            }

            private void argwriteonly(int arg)
            {
                argread(arg, false);
                argwrite(arg, true);
            }

            private void argtakesderivs(int arg, bool val)
            {
                if (arg < 32)
                {
                    if (val)
                    {
                        m_argtakesderivs |= (1u << arg);
                    }
                    else
                    {
                        m_argtakesderivs &= ~(1u << arg);
                    }
                }
            }
        }

        public sealed partial class ASTliteral : ASTNode
        {
            private ustring m_s;
            private int m_i = 0;
            private float m_f = 0.0f;

            public ASTliteral(OSLCompilerImpl comp, int i) : base(NodeType.literal_node, comp)
            {
                m_i = i;
                m_typespec = TypeInt;
            }

            public ASTliteral(OSLCompilerImpl comp, float f) : base(NodeType.literal_node, comp)
            {
                m_f = f;
                m_typespec = TypeFloat;
            }

            public ASTliteral(OSLCompilerImpl comp, ustring s) : base(NodeType.literal_node, comp)
            {
                m_s = s;
                m_typespec = TypeString;
            }

            public override string nodetypename() { return "literal"; }
            public override string childname(int i) { return null; }

            public override void print(TextWriter o, int indentlevel = 0)
            {
                indent(o, indentlevel);
                o.Write("(" + nodetypename() + " (type: " + m_typespec + ") ");
                if (m_typespec.is_int())
                {
                    o.Write(m_i);
                }
                else if (m_typespec.is_float())
                {
                    o.Write(m_f.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (m_typespec.is_string())
                {
                    o.Write("\"" + m_s + "\"");
                }
                o.Write(")\n");
            }

            public string strval() { return m_s.@string(); }
            public int intval() { return m_i; }
            public float floatval() { return m_typespec.is_int() ? (float)m_i : m_f; }
            public ustring ustrval() { return m_s; }

            public void negate()
            {
                m_i = -m_i;
                m_f = -m_f;
            }
        }
    }
}
