// OSL port: liboslcomp/codegen.cpp
//
// Code generation: each node's codegen() emits the IR ops for the shader into the compiler's op list and returns the symbol holding its value.
// "{:.9g}" in the default-literal code is Strutil.format_g9; std::string out-parameters are StringBuilders.

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using static OIIO.TypeDescs;

namespace OSL
{
    namespace pvt
    {
        public partial class OSLCompilerImpl
        {
            public int add_op_args(int nargs, Symbol[] args)
            {
                int n = m_opargs.Count;
                for (int i = 0; i < nargs; ++i)
                {
                    m_opargs.Add(args[i].dealias());
                }
                return n;
            }

            public void codegen_method(ustring method)
            {
                m_codegenmethod = method;
                if (method == main_method_name())
                {
                    m_main_method_start = next_op_label();
                }
            }

            private static readonly ustring main_method_name_ustring = new ustring("___main___");

            public static ustring main_method_name()
            {
                return main_method_name_ustring;
            }

            public int emitcode(string opname, int nargs, Symbol[] args, ASTNode node)
            {
                return insert_code(m_ircode.Count, opname, nargs, args, node);
            }

            public int insert_code(int opnum, string opname, int nargs, Symbol[] args, ASTNode node)
            {
                Opcode op = new Opcode(new ustring(opname), m_codegenmethod, m_opargs.Count, nargs);
                if (node != null)
                {
                    op.source(node.sourcefile(), node.sourceline());
                }
                m_ircode.Insert(opnum, op);
                add_op_args(nargs, args);

                if (opnum < m_ircode.Count - 1)
                {
                    // Inserting into the middle of the code: adjust the jump addresses and the parameter init ranges that point past the insertion
                    foreach (Opcode c in m_ircode)
                    {
                        for (int j = 0; j < Opcode.max_jumps && c.jump(j) >= 0; ++j)
                        {
                            if (c.jump(j) > opnum)
                            {
                                c.jump(j, c.jump(j) + 1);
                            }
                        }
                    }
                    foreach (Symbol s in symtab().allsyms())
                    {
                        if (s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam)
                        {
                            if (s.initbegin() > opnum)
                            {
                                s.initbegin(s.initbegin() + 1);
                            }
                            if (s.initend() > opnum)
                            {
                                s.initend(s.initend() + 1);
                            }
                        }
                    }
                }

                return opnum;
            }

            public Symbol make_temporary(TypeSpec type)
            {
                ustring name = new ustring("$tmp" + (++m_next_temp));
                Symbol s = new Symbol(name, type, SymType.SymTypeTemp);
                symtab().insert(s);

                // If the temporary is a struct, make a temp for each of its fields
                if (type.is_structure() || type.is_structure_array())
                {
                    add_struct_fields(type.structspec(), name, SymType.SymTypeTemp, type.arraylength());
                }

                return s;
            }

            /// Make a symbol for every field of a struct, named "basename.field", recursing into nested structs.
            public void add_struct_fields(StructSpec structspec, ustring basename, SymType symtype, int arraylen, ASTNode node = null, ASTNode init = null)
            {
                if (init != null)
                {
                    if (init.nodetype() == ASTNode.NodeType.variable_ref_node)
                    {
                        init = null;  // So we don't register whole-struct assignment
                    }
                    else if (init.nodetype() == ASTNode.NodeType.compound_initializer_node)
                    {
                        init = ((ASTcompound_initializer)init).initlist();
                    }
                }
                for (int i = 0; i < structspec.numfields(); ++i)
                {
                    StructSpec.FieldSpec field = structspec.field(i);
                    ustring fieldname = new ustring(basename + "." + field.name);
                    TypeSpec type = field.type;
                    int arr = type.arraylength();
                    if (arr != 0 && arraylen != 0)
                    {
                        errorfmt(node != null ? node.sourcefile() : new ustring(), node != null ? node.sourceline() : 1,
                                 "Nested structs with >1 levels of arrays are not allowed: " + structspec.name());
                    }
                    if (arraylen != 0 || arr != 0)
                    {
                        arr = Math.Max(1, arraylen) * Math.Max(1, arr);
                        type.make_array(arr);
                    }
                    Symbol sym = new Symbol(fieldname, type, symtype, node);
                    sym.fieldid(i);
                    symtab().insert(sym);
                    if (field.type.is_structure() || field.type.is_structure_array())
                    {
                        add_struct_fields(type.structspec(), fieldname, symtype, arr, node, arr != 0 ? null : init);
                    }
                    else
                    {
                        if (node != null && init != null && arr == 0)
                        {
                            ((ASTvariable_declaration)node).register_struct_init(fieldname, init);
                        }
                    }
                    if (init != null)
                    {
                        init = init.nextptr();
                    }
                }
            }

            public Symbol make_constant(ustring val)
            {
                foreach (ConstantSymbol sym in m_const_syms)
                {
                    if (sym.typespec().is_string() && sym.get_string() == val)
                    {
                        return sym;
                    }
                }
                ustring name = new ustring("$const" + (++m_next_const));
                ConstantSymbol s = new ConstantSymbol(name, val);
                symtab().insert(s);
                m_const_syms.Add(s);
                return s;
            }

            /// Make a constant of the given (array or aggregate) type from its values, held in an int[], float[] or ustring[].
            public Symbol make_constant(TypeDesc type, Array val)
            {
                foreach (ConstantSymbol sym in m_const_syms)
                {
                    if (sym.typespec().simpletype() == type && ConstantValuesEqual(val, (Array)sym.dataptr()))
                    {
                        return sym;
                    }
                }
                ustring name = new ustring("$const" + (++m_next_const));
                ConstantSymbol s = new ConstantSymbol(name, type);
                Array.Copy(val, (Array)s.dataptr(), Math.Min(val.Length, ((Array)s.dataptr()).Length));
                symtab().insert(s);
                m_const_syms.Add(s);
                return s;
            }

            private static bool ConstantValuesEqual(Array a, Array b)
            {
                if (a.GetType() != b.GetType())
                {
                    return false;
                }
                int n = Math.Min(a.Length, b.Length);
                for (int i = 0; i < n; ++i)
                {
                    if (!a.GetValue(i).Equals(b.GetValue(i)))
                    {
                        return false;
                    }
                }
                return true;
            }

            public Symbol make_constant(int val)
            {
                foreach (ConstantSymbol sym in m_const_syms)
                {
                    if (sym.typespec().is_int() && sym.get_int() == val)
                    {
                        return sym;
                    }
                }
                ustring name = new ustring("$const" + (++m_next_const));
                ConstantSymbol s = new ConstantSymbol(name, val);
                symtab().insert(s);
                m_const_syms.Add(s);
                return s;
            }

            public Symbol make_constant(float val)
            {
                foreach (ConstantSymbol sym in m_const_syms)
                {
                    if (sym.typespec().is_float() && sym.floatval() == val)
                    {
                        return sym;
                    }
                }
                ustring name = new ustring("$const" + (++m_next_const));
                ConstantSymbol s = new ConstantSymbol(name, val);
                symtab().insert(s);
                m_const_syms.Add(s);
                return s;
            }

            public Symbol make_constant(TypeDesc type, float x, float y, float z)
            {
                Vec3 val = new Vec3(x, y, z);
                foreach (ConstantSymbol sym in m_const_syms)
                {
                    if (sym.typespec().simpletype() == type && sym.typespec().is_triple() && sym.get_vec3() == val)
                    {
                        return sym;
                    }
                }
                ustring name = new ustring("$const" + (++m_next_const));
                ConstantSymbol s = new ConstantSymbol(name, type, x, y, z);
                symtab().insert(s);
                m_const_syms.Add(s);
                return s;
            }
        }

        public abstract partial class ASTNode
        {
            protected int emitcode(string opname, Symbol arg0 = null, Symbol arg1 = null, Symbol arg2 = null, Symbol arg3 = null)
            {
                Symbol[] args = { arg0, arg1, arg2, arg3 };
                int nargs = (arg0 != null ? 1 : 0) + (arg1 != null ? 1 : 0) + (arg2 != null ? 1 : 0) + (arg3 != null ? 1 : 0);
                return m_compiler.emitcode(opname, nargs, args, this);
            }

            protected int emitcode(string opname, int nargs, Symbol[] args)
            {
                return m_compiler.emitcode(opname, nargs, args, this);
            }

            /// Generate code to convert 'sym' to the given type (if it isn't already), returning the symbol with the converted value.
            protected Symbol coerce(Symbol sym, TypeSpec type, bool acceptfloat = false)
            {
                if (PvtFunctions.equivalent(sym.typespec(), type))
                {
                    return sym;  // No coercion necessary
                }

                if (type.is_closure())
                {
                    return sym;  // No coercion necessary
                }

                if (acceptfloat && sym.typespec().is_float())
                {
                    return sym;
                }

                // Unsized array formals accept any array of the right element type
                if (type.is_unsized_array() && sym.typespec().is_array() && PvtFunctions.equivalent(sym.typespec().elementtype(), type.elementtype()))
                {
                    return sym;
                }

                // If the source is a constant int and the destination is float based, make a float constant
                if (sym.symtype() == SymType.SymTypeConst && sym.typespec().is_int() && type.is_float_based() && !type.is_array())
                {
                    ConstantSymbol constsym = (ConstantSymbol)sym;
                    sym = m_compiler.make_constant(constsym.floatval());
                    if (type.is_float() || acceptfloat)
                    {
                        return sym;
                    }
                }

                Symbol t = m_compiler.make_temporary(type);
                emitcode("assign", t, sym);
                return t;
            }

            public virtual Symbol codegen(Symbol dest)
            {
                throw new InvalidOperationException("Unimplemented codegen for " + nodetypename() + " (" + (opname() != null ? opname() : "") + ")");
            }

            public Symbol codegen()
            {
                return codegen((Symbol)null);
            }

            protected void codegen_children()
            {
                foreach (ASTNode c in m_children)
                {
                    codegen_list(c);
                }
            }

            protected static Symbol codegen_list(ASTNode node, Symbol dest = null)
            {
                Symbol sym = null;
                while (node != null)
                {
                    bool last = node.nextptr() == null;
                    sym = node.codegen(last ? dest : null);
                    node = node.next();
                }
                return sym;
            }

            /// Generate code for the node as an int (or boolean 0/1 when requested), converting from other types as needed.
            public Symbol codegen_int(Symbol dest = null, bool boolify = false, bool invert = false)
            {
                dest = codegen();
                TypeSpec type = dest.typespec();
                if (!type.is_int() || boolify || invert)
                {
                    Symbol tempvar = m_compiler.make_temporary(TypeInt);
                    Symbol zerovar = null;
                    if (type.is_closure())
                    {
                        zerovar = m_compiler.make_constant((int)0);
                    }
                    else if (type.is_string())
                    {
                        zerovar = m_compiler.make_constant(new ustring(""));
                    }
                    else if (type.is_int())
                    {
                        zerovar = m_compiler.make_constant((int)0);
                    }
                    else
                    {
                        zerovar = m_compiler.make_constant(0.0f);
                    }
                    emitcode(invert ? "eq" : "neq", tempvar, dest, zerovar);
                    dest = tempvar;
                }
                return dest;
            }

            protected void codegen_assign_struct(StructSpec structspec, ustring dstsym, ustring srcsym, Symbol arrayindex, bool copywholearrays,
                                                 int intindex, bool paraminit)
            {
                for (int i = 0; i < structspec.numfields(); ++i)
                {
                    TypeSpec fieldtype = structspec.field(i).type;
                    if (fieldtype.is_structure())
                    {
                        // struct within struct -- recurse
                        ustring fieldname = structspec.field(i).name;
                        codegen_assign_struct(fieldtype.structspec(), new ustring(dstsym + "." + fieldname), new ustring(srcsym + "." + fieldname),
                                              arrayindex, copywholearrays, 0, paraminit);
                        continue;
                    }

                    if (fieldtype.is_structure_array() && arrayindex == null)
                    {
                        // array of struct within struct -- loop over the elements
                        ustring fieldname = structspec.field(i).name;
                        ustring dstfield = new ustring(dstsym + "." + fieldname);
                        ustring srcfield = new ustring(srcsym + "." + fieldname);
                        for (int k = 0; k < fieldtype.arraylength(); ++k)
                        {
                            codegen_assign_struct(fieldtype.structspec(), dstfield, srcfield, m_compiler.make_constant(k), copywholearrays, k, paraminit);
                        }
                        continue;
                    }

                    Symbol dfield, ofield;
                    m_compiler.struct_field_pair(structspec, i, dstsym, srcsym, out dfield, out ofield);
                    if (paraminit)
                    {
                        m_compiler.codegen_method(new ustring(dfield.mangled()));
                        dfield.initbegin(m_compiler.next_op_label());
                    }
                    if (arrayindex != null)
                    {
                        if (ofield.typespec().is_array())
                        {
                            TypeSpec elemtype = dfield.typespec().elementtype();
                            if (copywholearrays && intindex >= 0)
                            {
                                if (intindex == 0)
                                {
                                    emitcode("arraycopy", dfield, ofield);
                                }
                            }
                            else
                            {
                                Symbol tmp = m_compiler.make_temporary(elemtype);
                                emitcode("aref", tmp, ofield, arrayindex);
                                emitcode("aassign", dfield, arrayindex, tmp);
                            }
                        }
                        else
                        {
                            emitcode("aassign", dfield, arrayindex, ofield);
                        }
                    }
                    else if (dfield.typespec().is_array())
                    {
                        emitcode("arraycopy", dfield, ofield);
                    }
                    else
                    {
                        emitcode("assign", dfield, ofield);
                    }
                    if (paraminit)
                    {
                        dfield.initend(m_compiler.next_op_label());
                    }
                }
            }

            private static void AppendTriple(StringBuilder result, float a, float b, float c, string sep)
            {
                result.Append(Strutil.format_g9(a) + sep + Strutil.format_g9(b) + sep + Strutil.format_g9(c));
            }

            /// Append the constant default of one initializer of 'sym' to 'result'; returns true if the whole initialization was expressed.
            protected bool one_default_literal(Symbol sym, ASTNode init, StringBuilder result, string sep = " ")
            {
                bool islit = init != null && init.nodetype() == NodeType.literal_node;
                ASTliteral lit = init as ASTliteral;
                bool completed = true;  // have we output the full initialization?
                TypeSpec type = sym.typespec().elementtype();
                if (type.is_closure())
                {
                    completed = islit;
                }
                else if (type.is_structure())
                {
                    completed = false;
                }
                else if (type.is_int())
                {
                    if (islit && lit.typespec().is_int())
                    {
                        result.Append(lit.intval());
                    }
                    else
                    {
                        result.Append("0");  // FIXME?
                        completed = false;
                    }
                }
                else if (type.is_float())
                {
                    if (islit && lit.typespec().is_int())
                    {
                        result.Append(lit.intval());
                    }
                    else if (islit && lit.typespec().is_float())
                    {
                        result.Append(Strutil.format_g9(lit.floatval()));
                    }
                    else
                    {
                        result.Append("0");  // FIXME?
                        completed = false;
                    }
                }
                else if (type.is_triple())
                {
                    if (islit && lit.typespec().is_int())
                    {
                        float f = lit.intval();
                        AppendTriple(result, f, f, f, sep);
                    }
                    else if (islit && lit.typespec().is_float())
                    {
                        float f = lit.floatval();
                        AppendTriple(result, f, f, f, sep);
                    }
                    else if (init != null && init.typespec() == type
                             && (init.nodetype() == NodeType.type_constructor_node
                                 || (init.nodetype() == NodeType.compound_initializer_node && ((ASTcompound_initializer)init).canconstruct())))
                    {
                        ASTtype_constructor ctr = (ASTtype_constructor)init;
                        ASTNode val = ctr.args();
                        float[] f = new float[3];
                        int nargs = 0;
                        if (val != null && val.nodetype() == NodeType.literal_node && val.typespec().is_string())  // "space" name case
                        {
                            val = val.next();
                            completed = false;
                        }
                        bool one_arg = val != null && val.nextptr() == null;
                        for (int c = 0; c < 3; ++c)
                        {
                            if (val != null)
                            {
                                ++nargs;
                            }
                            if (val != null && val.nodetype() == NodeType.literal_node && val.typespec().is_int_or_float())
                            {
                                f[c] = ((ASTliteral)val).floatval();
                                val = val.next();
                            }
                            else if (c > 0 && one_arg)
                            {
                                f[c] = f[0];
                            }
                            else
                            {
                                f[c] = 0;
                                completed = false;
                            }
                        }
                        if (nargs == 1)
                        {
                            AppendTriple(result, f[0], f[0], f[0], sep);
                        }
                        else
                        {
                            AppendTriple(result, f[0], f[1], f[2], sep);
                        }
                    }
                    else
                    {
                        result.Append("0" + sep + "0" + sep + "0");
                        completed = false;
                    }
                }
                else if (type.is_matrix())
                {
                    if (islit && lit.typespec().is_int())
                    {
                        float f = lit.intval();
                        for (int c = 0; c < 16; ++c)
                        {
                            result.Append(Strutil.format_g9((c / 4) == (c % 4) ? f : 0.0f) + (c < 15 ? sep : ""));
                        }
                    }
                    else if (islit && lit.typespec().is_float())
                    {
                        float f = lit.floatval();
                        for (int c = 0; c < 16; ++c)
                        {
                            result.Append(Strutil.format_g9((c / 4) == (c % 4) ? f : 0.0f) + (c < 15 ? sep : ""));
                        }
                    }
                    else if (init != null && init.typespec() == type && init.nodetype() == NodeType.type_constructor_node)
                    {
                        ASTtype_constructor ctr = (ASTtype_constructor)init;
                        ASTNode val = ctr.args();
                        float[] f = new float[16];
                        int nargs = 0;
                        if (val.nodetype() == NodeType.literal_node && val.typespec().is_string())
                        {
                            val = val.next();
                            completed = false;
                        }
                        for (int c = 0; c < 16; ++c)
                        {
                            if (val != null)
                            {
                                ++nargs;
                            }
                            if (val != null && val.nodetype() == NodeType.literal_node && val.typespec().is_int_or_float())
                            {
                                f[c] = ((ASTliteral)val).floatval();
                                val = val.next();
                                if (val == null)  // we're done -- no more args
                                {
                                    break;
                                }
                            }
                            else
                            {
                                f[c] = 0;
                                completed = false;
                            }
                        }
                        if (nargs == 1)
                        {
                            for (int c = 0; c < 16; ++c)
                            {
                                result.Append(Strutil.format_g9((c / 4) == (c % 4) ? f[0] : 0.0f) + (c < 15 ? sep : ""));
                            }
                        }
                        else
                        {
                            for (int c = 0; c < 16; ++c)
                            {
                                result.Append(Strutil.format_g9(f[c]) + (c < 15 ? sep : ""));
                            }
                        }
                    }
                    else
                    {
                        for (int c = 0; c < 16; ++c)
                        {
                            result.Append("0" + (c < 15 ? sep : ""));
                        }
                        completed = false;
                    }
                }
                else if (type.is_string())
                {
                    if (islit && lit.typespec().is_string())
                    {
                        result.Append("\"" + Strutil.escape_chars(lit.strval()) + "\"");
                    }
                    else
                    {
                        result.Append("\"\"");  // FIXME?
                        completed = false;
                    }
                }
                else
                {
                    throw new InvalidOperationException("help with initializer");
                }
                return completed;
            }

            protected Symbol codegen_aassign(TypeSpec elemtype, Symbol src, Symbol lval, Symbol ind, int i = 0)
            {
                TypeSpec srctype = src.typespec();
                if (!PvtFunctions.equivalent(elemtype, srctype))
                {
                    // Not equivalent, so we need a temporary of the element type, unless both are plain scalars
                    if (elemtype.is_closure() || !elemtype.is_scalarnum() || srctype.is_closure() || !srctype.is_scalarnum())
                    {
                        Symbol tmp = m_compiler.make_temporary(elemtype);
                        emitcode("assign", tmp, src);
                        src = tmp;
                    }
                }

                if (ind == null)
                {
                    ind = m_compiler.make_constant(i);
                }

                emitcode("aassign", lval, ind, src);

                return src;
            }

            protected void codegen_initlist(ASTNode init, TypeSpec type, Symbol sym)
            {
                bool paraminit = m_compiler.codegen_method() != OSLCompilerImpl.main_method_name()
                                 && (sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam);

                if (type.is_structure())
                {
                    StructSpec structspec = type.structspec();
                    for (int i = 0; init != null && i < structspec.numfields(); init = init.next(), ++i)
                    {
                        StructSpec.FieldSpec field = structspec.field(i);
                        ustring fieldname = new ustring(sym.mangled() + "." + field.name);
                        Symbol fieldsym = m_compiler.symtab().find_exact(fieldname);
                        if (paraminit)
                        {
                            ASTvariable_declaration v = (ASTvariable_declaration)this;
                            StringBuilder result = new StringBuilder();
                            if (v.param_default_literals(fieldsym, init, result))
                            {
                                continue;  // Skip if we had a static initializer
                            }
                        }
                        codegen_initlist(init, fieldsym.typespec(), fieldsym);
                    }
                    return;
                }

                if (paraminit)
                {
                    if (type.is_structure_array())
                    {
                        errorfmt("array of struct are not allowed as parameters");
                        return;
                    }
                    m_compiler.codegen_method(sym.name());
                    sym.initbegin(m_compiler.next_op_label());
                }

                if (type.is_array() && !type.is_closure_based() && !type.is_structure_array())
                {
                    // Array of literals of the right type become a single constant assigned in one op
                    TypeDesc elemtype = type.simpletype().elementtype();
                    bool all_const = true;
                    int length = 0;
                    for (ASTNode i = init; i != null; i = i.next(), ++length)
                    {
                        if (i.nodetype() != NodeType.literal_node)
                        {
                            all_const = false;
                            break;
                        }
                        TypeDesc itype = i.typespec().simpletype();
                        if (itype != elemtype && !(itype == TypeInt && elemtype == TypeFloat))
                        {
                            all_const = false;
                            break;
                        }
                    }
                    if (all_const && init != null)
                    {
                        int count = type.simpletype().numelements();
                        Array arrayvals;
                        if (elemtype == TypeInt)
                        {
                            arrayvals = new int[count];
                        }
                        else if (elemtype == TypeFloat)
                        {
                            arrayvals = new float[count];
                        }
                        else
                        {
                            arrayvals = new ustring[count];
                        }
                        for (int i = 0; init != null; init = init.next(), ++i)
                        {
                            ASTliteral lit = (ASTliteral)init;
                            if (elemtype == TypeInt)
                            {
                                ((int[])arrayvals)[i] = lit.intval();
                            }
                            else if (elemtype == TypeFloat)
                            {
                                ((float[])arrayvals)[i] = lit.floatval();
                            }
                            else if (elemtype == TypeString)
                            {
                                ((ustring[])arrayvals)[i] = lit.ustrval();
                            }
                        }
                        Symbol c = m_compiler.make_constant(type.simpletype(), arrayvals);
                        emitcode("assign", sym, c);
                        if (paraminit)
                        {
                            sym.initend(m_compiler.next_op_label());
                        }
                        return;
                    }
                }
                else if (type.is_structure_array())
                {
                    for (int i = 0; init != null && i < type.arraylength(); ++i)
                    {
                        ASTNode expr = init;
                        bool ctor = false;
                        Symbol dest = sym;
                        switch (expr.nodetype())
                        {
                            case NodeType.function_call_node:
                                {
                                    ASTfunction_call fcall = (ASTfunction_call)expr;
                                    if ((ctor = fcall.is_struct_ctr()))
                                    {
                                        expr = fcall.args();
                                    }
                                }
                                break;
                            case NodeType.compound_initializer_node:
                                ctor = ((ASTcompound_initializer)expr).canconstruct();
                                break;
                            default:
                                break;
                        }
                        codegen_struct_initializers(expr, dest, ctor, m_compiler.make_constant(i));

                        init = init.next();
                    }
                    if (paraminit)
                    {
                        sym.initend(m_compiler.next_op_label());
                    }
                    return;
                }
                else if (init.nodetype() == NodeType.compound_initializer_node)
                {
                    init = ((ASTcompound_initializer)init).initlist();
                }

                for (int i = 0; init != null; init = init.next(), ++i)
                {
                    // Structs initialized from a nested list
                    if (sym.typespec().is_structure() && init.nodetype() == NodeType.compound_initializer_node)
                    {
                        ASTNode finit = ((ASTcompound_initializer)init).initlist();
                        codegen_struct_initializers(finit, sym);
                        break;
                    }

                    Symbol dest = init.codegen(sym);
                    if (dest != sym)
                    {
                        if (sym.typespec().is_array())
                        {
                            dest = codegen_aassign(sym.typespec().elementtype(), dest, sym, null, i);
                        }
                        else
                        {
                            emitcode("assign", sym, dest);
                        }
                    }
                    if (!sym.typespec().is_array())  // non-array type shouldn't loop
                    {
                        break;                       //  even if init is a list
                    }
                }

                if (paraminit)
                {
                    sym.initend(m_compiler.next_op_label());
                }
            }

            protected Symbol codegen_struct_initializers(ASTNode init, Symbol sym, bool is_constructor = false, Symbol arrayindex = null)
            {
                bool paraminit = m_compiler.codegen_method() != OSLCompilerImpl.main_method_name()
                                 && (sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam);

                if (init.nodetype() != NodeType.compound_initializer_node && !is_constructor)
                {
                    // Not an initializer list: the struct is assigned from another struct expression
                    Symbol initsym = init.codegen(sym);
                    if (initsym != sym)
                    {
                        StructSpec structspec = sym.typespec().structspec();
                        codegen_assign_struct(structspec, new ustring(sym.mangled()), new ustring(initsym.mangled()), arrayindex, true, 0, paraminit);
                    }
                    return sym;
                }

                if (!is_constructor && init.nodetype() == NodeType.compound_initializer_node)
                {
                    init = ((ASTcompound_initializer)init).initlist();
                }
                StructSpec spec = sym.typespec().structspec();
                for (int i = 0; init != null && i < spec.numfields(); init = init.next(), ++i)
                {
                    StructSpec.FieldSpec field = spec.field(i);
                    ustring fieldname = new ustring(sym.mangled() + "." + field.name);
                    Symbol fieldsym = m_compiler.symtab().find_exact(fieldname);
                    if (fieldsym.typespec().is_structure_based()
                        && (init.nodetype() == NodeType.type_constructor_node || init.nodetype() == NodeType.compound_initializer_node))
                    {
                        bool ctor = init.nodetype() == NodeType.type_constructor_node ? true : ((ASTcompound_initializer)init).canconstruct();
                        codegen_struct_initializers(init, fieldsym, ctor, arrayindex);
                        continue;
                    }

                    if (paraminit && nodetype() == NodeType.variable_declaration_node)
                    {
                        StringBuilder result = new StringBuilder();
                        ASTvariable_declaration v = (ASTvariable_declaration)this;
                        if (v.param_default_literals(fieldsym, init, result))
                        {
                            continue;
                        }
                    }
                    if (paraminit)
                    {
                        m_compiler.codegen_method(fieldname);
                        fieldsym.initbegin(m_compiler.next_op_label());
                    }

                    if (init.nodetype() == NodeType.compound_initializer_node && !((ASTcompound_initializer)init).canconstruct())
                    {
                        codegen_initlist(((ASTcompound_initializer)init).initlist(), field.type, fieldsym);
                    }
                    else if (init.nodetype() == NodeType.function_call_node && ((ASTfunction_call)init).is_struct_ctr())
                    {
                        codegen_struct_initializers(((ASTfunction_call)init).args(), fieldsym, true, arrayindex);
                    }
                    else
                    {
                        Symbol dest = init.codegen(fieldsym);
                        if (dest != fieldsym)
                        {
                            if (arrayindex == null)
                            {
                                emitcode("assign", fieldsym, dest);
                            }
                            else
                            {
                                dest = codegen_aassign(fieldsym.typespec().elementtype(), dest, fieldsym, arrayindex);
                            }
                        }
                    }

                    if (paraminit)
                    {
                        fieldsym.initend(m_compiler.next_op_label());
                    }
                }
                return sym;
            }
        }

        public sealed partial class ASTshader_declaration
        {
            public override Symbol codegen(Symbol dest)
            {
                // Generate the code for the parameters' initializers that couldn't be reduced to literals
                for (ASTNode f = formals(); f != null; f = f.next())
                {
                    ASTvariable_declaration v = (ASTvariable_declaration)f;
                    StringBuilder result = new StringBuilder();
                    if (!v.param_default_literals(v.sym(), v.init(), result))
                    {
                        v.codegen();
                    }
                }

                // Generate the main body
                m_compiler.codegen_method(OSLCompilerImpl.main_method_name());
                codegen_list(statements());
                return null;
            }
        }

        public sealed partial class ASTfunction_declaration
        {
            public override Symbol codegen(Symbol dest)
            {
                return null;  // generates no code on its own
            }
        }

        public sealed partial class ASTvariable_declaration
        {
            /// The default of a parameter as .oso literal text; false if it needs code to compute.
            public bool param_default_literals(Symbol sym, ASTNode init, StringBuilder result, string separator = " ")
            {
                result.Clear();

                // Structs have no literal defaults of their own; their fields do
                if (sym.typespec().is_structure() || sym.typespec().is_structure_array())
                {
                    return false;
                }

                // A struct field gets its initializer from the registered ones
                if (sym.fieldid() >= 0)
                {
                    init = null;
                    foreach ((ustring, ASTNode) n in m_struct_field_inits)
                    {
                        if (n.Item1 == sym.name())
                        {
                            init = n.Item2;
                            break;
                        }
                    }
                }

                bool compound = init != null && init.nodetype() == NodeType.compound_initializer_node;
                if (compound)
                {
                    compound = !((ASTcompound_initializer)init).canconstruct();
                    if (compound)
                    {
                        init = ((ASTcompound_initializer)init).initlist();
                    }
                }

                bool completed = true;  // have we output the full initialization?
                for (int i = 0; i == 0 || init != null; ++i, init = init.nextptr())
                {
                    if (i != 0)
                    {
                        result.Append(separator);
                    }
                    completed &= one_default_literal(sym, init, result, separator);
                    if (!compound || init == null)
                    {
                        break;
                    }
                }
                return completed;
            }

            public override Symbol codegen(Symbol dst)
            {
                if (init() != null)
                {
                    codegen_initializer(init(), m_sym);
                }
                return m_sym;
            }

            public void codegen_initializer(ASTNode init, Symbol sym)
            {
                if (typespec().is_structure())
                {
                    codegen_struct_initializers(init, sym);
                }
                else if (typespec().is_array())
                {
                    init = ((ASTcompound_initializer)init).initlist();
                    codegen_initlist(init, typespec(), sym);
                }
                else
                {
                    if (init.nodetype() == NodeType.compound_initializer_node)
                    {
                        ASTcompound_initializer cinit = (ASTcompound_initializer)init;
                        init = cinit.initlist();
                        if (cinit.canconstruct())
                        {
                            bool paraminit = m_compiler.codegen_method() != OSLCompilerImpl.main_method_name()
                                             && (m_sym.symtype() == SymType.SymTypeParam || m_sym.symtype() == SymType.SymTypeOutputParam);
                            if (paraminit)
                            {
                                m_compiler.codegen_method(sym.name());
                                sym.initbegin(m_compiler.next_op_label());
                            }

                            Symbol dest = cinit.codegen(sym);
                            if (dest != sym)
                            {
                                emitcode("assign", sym, dest);
                            }

                            if (paraminit)
                            {
                                sym.initend(m_compiler.next_op_label());
                            }
                            return;
                        }
                    }
                    codegen_initlist(init, m_typespec, m_sym);
                }
            }
        }

        public sealed partial class ASTreturn_statement
        {
            public override Symbol codegen(Symbol dest)
            {
                FunctionSymbol myfunc = m_compiler.current_function();
                if (myfunc != null)
                {
                    // Generate code for the return value, directly into the function's return location
                    if (expr() != null)
                    {
                        Symbol retloc = myfunc.return_location();
                        dest = expr().codegen(retloc);
                        if (dest != retloc)
                        {
                            if (retloc.typespec().is_structure())
                            {
                                StructSpec structspec = retloc.typespec().structspec();
                                codegen_assign_struct(structspec, new ustring(retloc.mangled()), new ustring(dest.mangled()), null, true, 0,
                                                      false /*not a shader param init*/);
                            }
                            else
                            {
                                emitcode("assign", myfunc.return_location(), dest);
                            }
                        }
                    }
                    // Only emit a return op if there are more statements, or we're nested
                    if (this.nextptr() != null || myfunc.nesting_level() > 0)
                    {
                        emitcode("return");
                    }
                }
                else
                {
                    // Return from the main shader body is the same as exit
                    emitcode("exit");
                }
                return null;
            }
        }

        public sealed partial class ASTcompound_initializer
        {
            public override Symbol codegen(Symbol sym)
            {
                if (canconstruct())
                {
                    return base.codegen(sym);
                }

                if (m_typespec.is_structure_based())
                {
                    if (sym == null)
                    {
                        sym = m_compiler.make_temporary(m_typespec);
                    }
                    return codegen_struct_initializers(initlist(), sym, true /*is_constructor*/);
                }

                if (m_typespec.is_sized_array())
                {
                    if (sym == null)
                    {
                        sym = m_compiler.make_temporary(m_typespec);
                    }
                    codegen_initlist(initlist(), m_typespec, sym);
                    return sym;
                }

                errorfmt("Possible compiler bug: compound_initializer codegen does not know how to handle type " + typespec());
                return null;
            }
        }

        public sealed partial class ASTassign_expression
        {
            public override Symbol codegen(Symbol dest)
            {
                ASTindex index = null;
                if (var().nodetype() == NodeType.index_node)
                {
                    index = (ASTindex)var();
                    dest = null;
                }
                else if (var().nodetype() == NodeType.structselect_node)
                {
                    if (((ASTstructselect)var()).compindex() == null)
                    {
                        dest = var().codegen();
                    }
                }
                else
                {
                    dest = var().codegen();
                }

                Symbol operand = expr().codegen(dest);

                if (typespec().is_structure_array())
                {
                    if (operand != dest)
                    {
                        StructSpec structspec = typespec().structspec();
                        codegen_assign_struct(structspec, new ustring(dest.mangled()), new ustring(operand.mangled()), null, true, 0,
                                              false /*not a shader param init*/);
                    }
                    return dest;
                }

                if (typespec().is_structure())
                {
                    if (operand != dest)
                    {
                        StructSpec structspec = typespec().structspec();
                        Symbol arrayindex = index != null ? index.index().codegen() : null;
                        if (arrayindex != null)
                        {
                            Symbol v = index.lvalue().codegen();
                            codegen_assign_struct(structspec, new ustring(v.mangled()), new ustring(operand.mangled()), arrayindex, false,
                                                  -1 /* means we don't know */, false /*not a shader param init*/);
                        }
                        else
                        {
                            codegen_assign_struct(structspec, new ustring(dest.mangled()), new ustring(operand.mangled()), null, true, 0,
                                                  false /*not a shader param init*/);
                        }
                    }
                    return dest;
                }

                if (var().nodetype() == NodeType.structselect_node)
                {
                    ASTstructselect ss = (ASTstructselect)var();
                    ss.codegen_assign(dest, operand);
                    return dest;
                }

                if (index != null)
                {
                    index.codegen_assign(operand);
                    dest = operand;  // so transitive assignment works for array refs
                }
                else if (operand != dest)
                {
                    emitcode(typespec().is_array() ? "arraycopy" : "assign", dest, operand);
                }
                return dest;
            }
        }

        public sealed partial class ASTvariable_ref
        {
            public override Symbol codegen(Symbol dest)
            {
                return m_sym;
            }
        }

        public sealed partial class ASTpreincdec
        {
            public override Symbol codegen(Symbol dest)
            {
                Symbol sym = var().codegen();
                Symbol one = sym.typespec().is_int() ? m_compiler.make_constant(1) : m_compiler.make_constant(1.0f);
                emitcode(m_op == Incr ? "add" : "sub", sym, sym, one);
                return sym;
            }
        }

        public sealed partial class ASTpostincdec
        {
            public override Symbol codegen(Symbol dest)
            {
                Symbol sym = var().codegen();
                Symbol one = sym.typespec().is_int() ? m_compiler.make_constant(1) : m_compiler.make_constant(1.0f);
                if (dest == null)
                {
                    dest = m_compiler.make_temporary(sym.typespec());
                }
                emitcode("assign", dest, sym);
                emitcode(m_op == Incr ? "add" : "sub", sym, sym, one);
                return dest;
            }
        }

        public sealed partial class ASTindex
        {
            public override Symbol codegen(Symbol dest)
            {
                Symbol ind = null, ind2 = null, ind3 = null;
                return codegen(dest, ref ind, ref ind2, ref ind3);
            }

            public Symbol codegen(Symbol dest, ref Symbol ind, ref Symbol ind2, ref Symbol ind3)
            {
                Symbol lv = lvalue().codegen();
                ind = index().codegen();
                ind2 = index2() != null ? index2().codegen() : null;
                ind3 = index3() != null ? index3().codegen() : null;
                if (dest == null || !PvtFunctions.equivalent(dest.typespec(), typespec()))
                {
                    dest = m_compiler.make_temporary(typespec());
                }
                if (lv.typespec().is_array())
                {
                    if (index3() != null)
                    {
                        // Array of matrices, component of a matrix
                        Symbol tmp = m_compiler.make_temporary(lv.typespec().elementtype());
                        emitcode("aref", tmp, lv, ind);
                        emitcode("mxcompref", dest, tmp, ind2, ind3);
                    }
                    else if (index2() != null)
                    {
                        // Array of triples, component of a triple
                        Symbol tmp = m_compiler.make_temporary(lv.typespec().elementtype());
                        emitcode("aref", tmp, lv, ind);
                        emitcode("compref", dest, tmp, ind2);
                    }
                    else if (lv.typespec().is_structure_array())
                    {
                        codegen_copy_struct_array_element(lv.typespec().structspec(), new ustring(dest.mangled()), new ustring(lv.mangled()), ind);
                    }
                    else
                    {
                        emitcode("aref", dest, lv, ind);
                    }
                }
                else if (lv.typespec().is_triple())
                {
                    emitcode("compref", dest, lv, ind);
                }
                else if (lv.typespec().is_matrix())
                {
                    emitcode("mxcompref", dest, lv, ind, ind2);
                }
                else
                {
                    throw new InvalidOperationException("ASTindex::codegen on an unindexable type");
                }
                return dest;
            }

            public void codegen_copy_struct_array_element(StructSpec structspec, ustring destname, ustring srcname, Symbol index)
            {
                for (int fi = 0; fi < structspec.numfields(); ++fi)
                {
                    StructSpec.FieldSpec field = structspec.field(fi);
                    TypeSpec type = field.type;
                    if (type.is_structure())
                    {
                        codegen_copy_struct_array_element(type.structspec(), new ustring(destname + "." + field.name), new ustring(srcname + "." + field.name),
                                                          index);
                    }
                    else
                    {
                        Symbol dfield, sfield;
                        m_compiler.struct_field_pair(structspec, fi, destname, srcname, out dfield, out sfield);
                        emitcode("aref", dfield, sfield, index);
                    }
                }
            }

            public void codegen_assign(Symbol src, Symbol ind = null, Symbol ind2 = null, Symbol ind3 = null)
            {
                Symbol lv = lvalue().codegen();
                if (ind == null)
                {
                    ind = index().codegen();
                }
                if (ind2 == null)
                {
                    ind2 = index2() != null ? index2().codegen() : null;
                }
                if (ind3 == null)
                {
                    ind3 = index3() != null ? index3().codegen() : null;
                }
                if (lv.typespec().is_array())
                {
                    TypeSpec elemtype = lv.typespec().elementtype();
                    if (ind3 != null && elemtype.is_matrix())
                    {
                        Symbol temp = m_compiler.make_temporary(elemtype);
                        emitcode("aref", temp, lv, ind);
                        emitcode("mxcompassign", temp, ind2, ind3, src);
                        emitcode("aassign", lv, ind, temp);
                    }
                    else if (ind2 != null && elemtype.is_triple())
                    {
                        Symbol temp = m_compiler.make_temporary(elemtype);
                        emitcode("aref", temp, lv, ind);
                        emitcode("compassign", temp, ind2, src);
                        emitcode("aassign", lv, ind, temp);
                    }
                    else
                    {
                        src = codegen_aassign(elemtype, src, lv, ind);
                    }
                }
                else if (lv.typespec().is_triple())
                {
                    emitcode("compassign", lv, ind, src);
                }
                else if (lv.typespec().is_matrix())
                {
                    emitcode("mxcompassign", lv, ind, ind2, src);
                }
                else
                {
                    throw new InvalidOperationException("ASTindex::codegen_assign on an unindexable type");
                }
            }
        }

        public sealed partial class ASTstructselect
        {
            public override Symbol codegen(Symbol dest)
            {
                if (compindex() != null)
                {
                    return compindex().codegen(dest);
                }

                Symbol indexsym = codegen_index();

                if (indexsym != null)
                {
                    // The struct is an array element: pull the field out of the field array
                    Symbol tmp = m_compiler.make_temporary(typespec());
                    emitcode("aref", tmp, m_fieldsym, indexsym);
                    return tmp;
                }
                return m_fieldsym;
            }

            public void codegen_assign(Symbol dest, Symbol src)
            {
                if (compindex() != null)
                {
                    compindex().codegen_assign(src);
                    return;
                }

                src = coerce(src, typespec());

                Symbol indexsym = codegen_index();

                if (indexsym != null)
                {
                    emitcode("aassign", m_fieldsym, indexsym, src);
                }
                else
                {
                    emitcode("assign", dest, src);
                }
            }

            private Symbol codegen_index()
            {
                ASTNode node = this;
                Symbol indexsym = null;
                while (node != null)
                {
                    if (node.nodetype() == NodeType.variable_ref_node)
                    {
                        break;
                    }
                    else if (node.nodetype() == NodeType.structselect_node)
                    {
                        node = ((ASTstructselect)node).lvalue();
                    }
                    else if (node.nodetype() == NodeType.index_node)
                    {
                        ASTindex arrayref = (ASTindex)node;
                        indexsym = arrayref.index().codegen();
                        node = arrayref.lvalue();
                    }
                    else
                    {
                        throw new InvalidOperationException("ASTstructselect::codegen_index on an unexpected node");
                    }
                }

                return indexsym;
            }
        }

        public sealed partial class ASTconditional_statement
        {
            public override Symbol codegen(Symbol dest)
            {
                // Generate code for the condition
                Symbol condvar = cond().codegen_int();

                // Generate the 'if' op, with the condition read but not written
                int ifop = emitcode("if", condvar);
                m_compiler.lastop().argread(0, true);
                m_compiler.lastop().argwrite(0, false);

                // Generate code for the true and false statements, remembering where each begins
                m_compiler.push_nesting(false);
                codegen_list(truestmt());
                int falselabel = m_compiler.next_op_label();
                codegen_list(falsestmt());
                int donelabel = m_compiler.next_op_label();
                m_compiler.pop_nesting(false);

                // Fix up the jump targets of the 'if' op
                m_compiler.ircode(ifop).set_jump(falselabel, donelabel);

                return null;
            }
        }

        public sealed partial class ASTloop_statement
        {
            public override Symbol codegen(Symbol dest)
            {
                // Generate the loop op itself; its argument is the condition, which is not known until later
                int loop_op = emitcode(opname());
                m_compiler.lastop().argread(0, true);
                m_compiler.lastop().argwrite(0, false);

                m_compiler.push_nesting(true);
                codegen_list(init());

                int condlabel = m_compiler.next_op_label();
                Symbol condvar = cond().codegen_int(null, true);

                // Now that the condition symbol is known, add it as the loop op's argument
                Symbol[] condargs = { condvar };
                int argstart = m_compiler.add_op_args(1, condargs);
                m_compiler.ircode(loop_op).set_args(argstart, 1);

                int bodylabel = m_compiler.next_op_label();
                codegen_list(stmt());
                int iterlabel = m_compiler.next_op_label();
                codegen_list(iter());
                int donelabel = m_compiler.next_op_label();
                m_compiler.pop_nesting(true);

                m_compiler.ircode(loop_op).set_jump(condlabel, bodylabel, iterlabel, donelabel);

                return null;
            }
        }

        public sealed partial class ASTloopmod_statement
        {
            public override Symbol codegen(Symbol dest)
            {
                emitcode(opname());
                return null;
            }
        }

        public sealed partial class ASTunary_expression
        {
            public override Symbol codegen(Symbol dest)
            {
                // Code for the overloaded operator is a call to the user's function
                if (m_function_overload != null)
                {
                    ustring funcname = new ustring("__operator__" + opword() + "__");
                    ASTfunction_call fc = new ASTfunction_call(m_compiler, funcname, expr(), m_function_overload);
                    fc.typecheck(typespec());
                    return dest = fc.codegen(dest);
                }

                if (m_op == Not)
                {
                    // Special case for !x: generate the int code, inverted
                    return expr().codegen_int(null, true /*boolify*/, true /*invert*/);
                }

                Symbol esym = expr().codegen();

                if (m_op == Add)
                {
                    // Unary + is a no-op
                    return esym;
                }

                if (dest == null || !PvtFunctions.equivalent(dest.typespec(), typespec()))
                {
                    dest = m_compiler.make_temporary(typespec());
                }

                if (esym.typespec().is_closure())
                {
                    // Negation of a closure is multiplication by -1
                    emitcode("mul", dest, esym, m_compiler.make_constant(-1.0f));
                    return dest;
                }

                emitcode(opword(), dest, esym);

                return dest;
            }
        }

        public sealed partial class ASTbinary_expression
        {
            public override Symbol codegen(Symbol dest)
            {
                // Code for the overloaded operator is a call to the user's function
                if (m_function_overload != null)
                {
                    ustring funcname = new ustring("__operator__" + opword() + "__");
                    if (left().nextptr() != null || right().nextptr() != null)
                    {
                        errorfmt("Overloaded %s cannot be passed arguments " + left().nodetypename() + " and " + right().nodetypename());
                        return dest;
                    }
                    ASTNode args = left();
                    args.append(right());
                    ASTfunction_call fc = new ASTfunction_call(m_compiler, funcname, args, m_function_overload);
                    fc.typecheck(typespec());
                    dest = fc.codegen(dest);
                    left().detach_next();
                    return dest;
                }

                // Logical && and || short-circuit
                if (m_op == And || m_op == Or)
                {
                    return codegen_logic(dest);
                }

                Symbol lsym = left().codegen();
                Symbol rsym = right().codegen();
                if (dest == null || !PvtFunctions.equivalent(dest.typespec(), typespec()))
                {
                    dest = m_compiler.make_temporary(typespec());
                }

                if (typespec().is_closure())
                {
                    // Closure arithmetic: color-by-closure multiplications use a color factor
                    if (m_op == Mul || m_op == Div)
                    {
                        rsym = coerce(rsym, TypeColor, true);
                    }
                    emitcode(opword(), dest, lsym, rsym);
                    return dest;
                }

                // Mixed int and float: promote the int
                if (m_op == Mul || m_op == Div || m_op == Add || m_op == Sub)
                {
                    if (lsym.typespec().is_float_based() && rsym.typespec().is_int())
                    {
                        if (rsym.symtype() == SymType.SymTypeConst)
                        {
                            float val = ((ConstantSymbol)rsym).floatval();
                            rsym = m_compiler.make_constant(val);
                        }
                        else
                        {
                            Symbol tmp = rsym;
                            rsym = m_compiler.make_temporary(lsym.typespec());
                            emitcode("assign", rsym, tmp);  // type coercion
                        }
                    }
                    else if (lsym.typespec().is_int() && rsym.typespec().is_float_based())
                    {
                        if (lsym.symtype() == SymType.SymTypeConst)
                        {
                            float val = ((ConstantSymbol)lsym).floatval();
                            lsym = m_compiler.make_constant(val);
                        }
                        else
                        {
                            Symbol tmp = lsym;
                            lsym = m_compiler.make_temporary(rsym.typespec());
                            emitcode("assign", lsym, tmp);  // type coercion
                        }
                    }
                }

                emitcode(opword(), dest, lsym, rsym);
                return dest;
            }

            private Symbol codegen_logic(Symbol dest)
            {
                // Generate code for the left operand as a boolean int
                dest = left().codegen_int(null, true);

                // Generate the 'if' op: skip the right operand if the left decides the result
                int ifop = emitcode("if", dest);
                m_compiler.lastop().argread(0, true);
                m_compiler.lastop().argwrite(0, false);
                int falselabel;
                m_compiler.push_nesting(false);

                if (m_op == And)
                {
                    Symbol rsym = right().codegen_int(dest, true);
                    if (rsym != dest)
                    {
                        emitcode("assign", dest, rsym);
                    }
                    falselabel = m_compiler.next_op_label();
                }
                else  /* Or */
                {
                    falselabel = m_compiler.next_op_label();
                    Symbol rsym = right().codegen_int(dest, true);
                    if (rsym != dest)
                    {
                        emitcode("assign", dest, rsym);
                    }
                }

                int donelabel = m_compiler.next_op_label();
                m_compiler.pop_nesting(false);
                m_compiler.ircode(ifop).set_jump(falselabel, donelabel);
                return dest;
            }
        }

        public sealed partial class ASTternary_expression
        {
            public override Symbol codegen(Symbol dest)
            {
                if (dest == null)
                {
                    dest = m_compiler.make_temporary(typespec());
                }

                // Generate the condition
                Symbol condvar = cond().codegen_int();

                int ifop = emitcode("if", condvar);
                m_compiler.lastop().argread(0, true);
                m_compiler.lastop().argwrite(0, false);

                // Generate the true and false expressions into the same destination
                m_compiler.push_nesting(false);
                Symbol trueval = trueexpr().codegen(dest);
                if (trueval != dest)
                {
                    emitcode("assign", dest, trueval);
                }

                int falselabel = m_compiler.next_op_label();

                m_compiler.push_nesting(false);
                Symbol falseval = falseexpr().codegen(dest);
                if (falseval != dest)
                {
                    emitcode("assign", dest, falseval);
                }

                int donelabel = m_compiler.next_op_label();
                m_compiler.pop_nesting(false);

                m_compiler.ircode(ifop).set_jump(falselabel, donelabel);

                return dest;
            }
        }

        public sealed partial class ASTcomma_operator
        {
            public override Symbol codegen(Symbol dest)
            {
                return codegen_list(expr(), dest);
            }
        }

        public sealed partial class ASTtypecast_expression
        {
            public override Symbol codegen(Symbol dest)
            {
                Symbol e = expr().codegen(dest);

                if (PvtFunctions.equivalent(typespec(), e.typespec()))
                {
                    return e;
                }

                if (dest == null || !PvtFunctions.equivalent(dest.typespec(), typespec()))
                {
                    dest = m_compiler.make_temporary(typespec());
                }
                emitcode("assign", dest, e);
                return dest;
            }
        }

        public partial class ASTtype_constructor
        {
            public override Symbol codegen(Symbol dest)
            {
                if (dest == null || !PvtFunctions.equivalent(dest.typespec(), typespec()))
                {
                    dest = m_compiler.make_temporary(typespec());
                }

                // Triples built entirely from literals become constants
                if (typespec().is_triple())
                {
                    bool all_literals = true;
                    ASTNode val = args();
                    float[] f = new float[3];
                    for (int c = 0; c < 3; ++c)
                    {
                        if (val.nodetype() == NodeType.literal_node && (val.typespec().is_float() || val.typespec().is_int()))
                        {
                            f[c] = ((ASTliteral)val).floatval();
                        }
                        else
                        {
                            all_literals = false;
                        }
                        if (val.next() != null)
                        {
                            val = val.next();
                        }
                    }
                    if (all_literals)
                    {
                        return m_compiler.make_constant(typespec().simpletype(), f[0], f[1], f[2]);
                    }
                }

                // A single float argument of a float constructor can compute straight into dest
                Symbol argevaldest = null;
                if (dest != null && typespec().is_float() && nchildren() == 1 && child(0).typespec().is_float())
                {
                    argevaldest = dest;
                }

                List<Symbol> argdest = new List<Symbol>();
                argdest.Add(dest);
                int nargs = 0;
                for (ASTNode a = args(); a != null; a = a.next(), ++nargs)
                {
                    Symbol argval = a.codegen(argevaldest);
                    if (argval.typespec().is_int() && !typespec().is_int())
                    {
                        if (a.nodetype() == NodeType.literal_node)
                        {
                            int i = ((ASTliteral)a).intval();
                            argval = m_compiler.make_constant((float)i);
                        }
                        else
                        {
                            Symbol tmp = argval;
                            argval = m_compiler.make_temporary(new TypeSpec(TypeFloat));
                            emitcode("assign", argval, tmp);
                        }
                    }
                    argdest.Add(argval);
                }
                if (nargs == 1 && argdest.Count == 2 && argdest[1] == dest)
                {
                    // The single float argument was computed straight into dest: nothing to do
                }
                else if (nargs == 1)
                {
                    emitcode("assign", argdest.Count, argdest.ToArray());
                }
                else
                {
                    emitcode(typespec().@string(), argdest.Count, argdest.ToArray());
                }
                return dest;
            }
        }

        public sealed partial class ASTfunction_call
        {
            private bool argread(int arg)
            {
                if (is_user_function())
                {
                    if (!typespec().is_void() && arg == 0)
                    {
                        return false;
                    }
                    return true;
                }
                // built-in function
                return arg < 32 ? (m_argread & (1u << arg)) != 0 : true;
            }

            private bool argwrite(int arg)
            {
                if (is_user_function())
                {
                    if (typespec().is_void())
                    {
                        ASTvariable_declaration formal = (ASTvariable_declaration)list_nth(user_function().formals(), arg);
                        return formal.is_output();
                    }
                    if (arg == 0)
                    {
                        return true;  // return value always writes
                    }
                    ASTvariable_declaration formal2 = (ASTvariable_declaration)list_nth(user_function().formals(), arg - 1);
                    return formal2.is_output();
                }
                // built-in function
                return arg < 32 ? (m_argwrite & (1u << arg)) != 0 : false;
            }

            public override Symbol codegen(Symbol dest)
            {
                if (is_struct_ctr())
                {
                    if (dest == null)
                    {
                        dest = m_compiler.make_temporary(typespec());
                    }
                    codegen_struct_initializers(args(), dest, true /*is_constructor*/);
                    return dest;
                }

                // Set up a place to put the return value, if there is one
                if (!typespec().is_void())
                {
                    if (dest == null || !PvtFunctions.equivalent(dest.typespec(), typespec()))
                    {
                        dest = m_compiler.make_temporary(typespec());
                    }
                }

                // Get the formal parameter types of the chosen overload
                List<TypeSpec> polyargs = new List<TypeSpec>();
                string param_argcodes = func().argcodes().@string();
                int len;
                TypeSpec.type_from_code(param_argcodes, 0, out len);  // skip ret type
                TypeSpec.typespecs_from_codes(param_argcodes.Substring(len), polyargs);

                List<Symbol> argdest = new List<Symbol>();
                List<Symbol> index = new List<Symbol>();
                List<Symbol> index2 = new List<Symbol>();
                List<Symbol> index3 = new List<Symbol>();
                bool indexed_output_params = false;
                int argdest_return_offset = 0;
                ASTNode a = args();

                int returnarg = !typespec().is_void() ? 1 : 0;
                ASTNode form = is_user_function() ? user_function().formals() : null;
                for (int i = 0; a != null; a = a.nextptr(), ++i)
                {
                    TypeSpec formaltype = (i < polyargs.Count) ? polyargs[i] : new TypeSpec(TypeUnknown);
                    bool writearg = argwrite(i + returnarg);
                    codegen_arg(argdest, index, index2, index3, i, a, form, formaltype, writearg, ref indexed_output_params);
                    if (form != null)
                    {
                        form = form.nextptr();
                    }
                }

                if (is_user_function())
                {
                    func().return_location(typespec().is_void() ? null : dest);

                    // Bind the formal parameters to the actual arguments, by aliasing
                    ASTNode uform = user_function().formals();
                    ASTNode ua = args();
                    for (int i = 0; ua != null; ua = ua.nextptr(), uform = uform.nextptr(), ++i)
                    {
                        ASTvariable_declaration f = (ASTvariable_declaration)uform;
                        TypeSpec ftype = f.sym().typespec();
                        if (ftype.is_structure() || ftype.is_structure_array())
                        {
                            // Alias the fields of the formal struct to the fields of the actual
                            if (ua.nodetype() == NodeType.variable_ref_node || ua.nodetype() == NodeType.function_call_node
                                || ua.nodetype() == NodeType.compound_initializer_node || ua.nodetype() == NodeType.binary_expression_node
                                || ua.nodetype() == NodeType.unary_expression_node)
                            {
                                struct_pair_all_fields(ftype.structspec(), new ustring(f.sym().mangled()), new ustring(argdest[i].mangled()));
                            }
                            else if (ua.nodetype() == NodeType.structselect_node)
                            {
                                struct_pair_all_fields(ftype.structspec(), new ustring(f.sym().mangled()),
                                                       new ustring(((ASTstructselect)ua).fieldsym().mangled()));
                            }
                            else if (ua.nodetype() == NodeType.index_node)
                            {
                                ASTindex ind = (ASTindex)ua;
                                Symbol arrayindex = ind.index().codegen();
                                struct_pair_all_fields(ftype.structspec(), new ustring(f.sym().mangled()), new ustring(argdest[i].mangled()), arrayindex);
                            }
                            else
                            {
                                throw new InvalidOperationException("unhandled structure designation");
                            }
                        }
                        f.sym().alias(argdest[i]);
                    }

                    // Inline the function body between 'functioncall' and its end label
                    int loop_op = emitcode("functioncall", m_compiler.make_constant(m_name));

                    m_compiler.push_function(func());
                    codegen_list(user_function().statements());
                    m_compiler.pop_function();

                    m_compiler.ircode(loop_op).argread(0, true);    // read
                    m_compiler.ircode(loop_op).argwrite(0, false);  // not written
                    int endlabel = m_compiler.next_op_label();
                    m_compiler.ircode(loop_op).set_jump(endlabel);
                }
                else
                {
                    // Built-in function: a single op named after the function
                    bool isclosure = func() != null && func().typespec().is_closure();
                    if (isclosure)
                    {
                        Symbol clname = m_compiler.make_constant(m_name);
                        argdest.Insert(0, clname);
                        argdest_return_offset++;
                    }
                    if (!typespec().is_void())  // Insert the return dest if non-void
                    {
                        argdest.Insert(0, dest);
                        argdest_return_offset++;
                    }
                    emitcode(isclosure ? "closure" : m_name.@string(), argdest.Count, argdest.ToArray());
                    m_compiler.lastop().set_argbits(m_argread, m_argwrite, m_argtakesderivs);
                }

                // Copy-out for the array and component elements passed as output parameters
                if (indexed_output_params)
                {
                    a = args();
                    for (int i = 0; a != null; a = a.nextptr(), ++i)
                    {
                        if (index[i] != null)
                        {
                            ASTindex indexnode = (a.nodetype() == NodeType.index_node) ? (ASTindex)a : ((ASTstructselect)a).compindex();
                            indexnode.codegen_assign(argdest[i + argdest_return_offset], index[i], index2[i], index3[i]);
                        }
                    }
                }

                return dest;
            }

            private void codegen_arg(List<Symbol> argdest, List<Symbol> index1, List<Symbol> index2, List<Symbol> index3, int argnum, ASTNode arg,
                                     ASTNode form, TypeSpec formaltype, bool writearg, ref bool indexed_output_params)
            {
                Symbol thisarg = null;
                Symbol ind1 = null, ind2 = null, ind3 = null;  // array/component indices

                bool is_struct = arg.typespec().is_structure();
                if (is_struct)
                {
                    thisarg = arg.codegen();
                }
                else if (arg != null && writearg
                         && (arg.nodetype() == NodeType.index_node
                             || (arg.nodetype() == NodeType.structselect_node && ((ASTstructselect)arg).compindex() != null)))
                {
                    // Output parameter that is an array element or a component: pass a temporary, copy out afterwards
                    ASTindex indexnode = (arg.nodetype() == NodeType.index_node) ? (ASTindex)arg : ((ASTstructselect)arg).compindex();
                    thisarg = indexnode.codegen(null, ref ind1, ref ind2, ref ind3);
                    indexed_output_params = true;
                }
                else
                {
                    thisarg = arg.codegen();
                }
                if (!is_struct && formaltype.simpletype() != new TypeDesc(TypeDesc.UNKNOWN)
                    && formaltype.simpletype() != new TypeDesc(TypeDesc.UNKNOWN, -1))
                {
                    Symbol origarg = thisarg;
                    thisarg = coerce(thisarg, formaltype);
                    if (thisarg != origarg && form != null && !PvtFunctions.equivalent(origarg.typespec(), form.typespec())
                        && form.nodetype() == NodeType.variable_declaration_node && ((ASTvariable_declaration)form).is_output())
                    {
                        errorfmt("Cannot pass '" + origarg.typespec() + " " + origarg.name() + "' as argument " + (argnum + 1) + " to "
                                 + user_function().func().name() + "\n\tbecause it is an output parameter that must be a " + form.typespec());
                    }
                }
                if (thisarg != null)
                {
                    argdest.Add(thisarg);
                    index1.Add(ind1);
                    index2.Add(ind2);
                    index3.Add(ind3);
                }
                else
                {
                    arg.errorfmt("Invalid argument to function");
                }
            }

            private void struct_pair_all_fields(StructSpec structspec, ustring formal, ustring actual, Symbol arrayindex = null)
            {
                for (int fi = 0; fi < structspec.numfields(); ++fi)
                {
                    StructSpec.FieldSpec field = structspec.field(fi);
                    TypeSpec type = field.type;
                    if (type.is_structure() || type.is_structure_array())
                    {
                        struct_pair_all_fields(type.structspec(), new ustring(formal + "." + field.name), new ustring(actual + "." + field.name), arrayindex);
                    }
                    else
                    {
                        Symbol fsym, asym;
                        m_compiler.struct_field_pair(structspec, fi, formal, actual, out fsym, out asym);
                        fsym.alias(asym);
                    }
                }
            }
        }

        public sealed partial class ASTliteral
        {
            public override Symbol codegen(Symbol dest)
            {
                TypeSpec t = typespec();
                if (t.is_string())
                {
                    return m_compiler.make_constant(new ustring(strval()));
                }
                if (t.is_int())
                {
                    return m_compiler.make_constant(intval());
                }
                if (t.is_float())
                {
                    return m_compiler.make_constant(floatval());
                }
                throw new InvalidOperationException("Don't know how to generate code for this literal");
            }
        }
    }
}
