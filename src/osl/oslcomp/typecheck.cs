// OSL port: liboslcomp/typecheck.cpp
//
// The type checking pass: every node's typecheck(), the compound initializer TypeAdjuster, function overload resolution
// (CandidateFunctions, and the pre-1.10 LegacyOverload behind OSL_LEGACY_FUNCTION_RESOLUTION) and the table of built-in function signatures.
// The C++ walks type-code strings with a char pointer; here a string and an index do the same, and '\0' stands for the end of the string.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OIIO;
using static OIIO.TypeDescs;

namespace OSL
{
    namespace pvt
    {
        public abstract partial class ASTNode
        {
            internal static char CharAt(string s, int pos)
            {
                return pos < s.Length ? s[pos] : '\0';
            }

            public virtual TypeSpec typecheck(TypeSpec expected)
            {
                typecheck_children(expected);
                if (m_typespec == new TypeSpec())
                {
                    m_typespec = expected;
                }
                return m_typespec;
            }

            public TypeSpec typecheck()
            {
                return typecheck(new TypeSpec());
            }

            protected void typecheck_children(TypeSpec expected)
            {
                foreach (ASTNode c in m_children)
                {
                    typecheck_list(c, expected);
                }
            }

            protected void typecheck_children()
            {
                typecheck_children(new TypeSpec());
            }

            protected static TypeSpec typecheck_list(ASTNode node, TypeSpec expected)
            {
                TypeSpec t = new TypeSpec();
                while (node != null)
                {
                    t = node.typecheck(expected);
                    node = node.next();
                }
                return t;
            }

            protected static TypeSpec typecheck_list(ASTNode node)
            {
                return typecheck_list(node, new TypeSpec());
            }

            /// Check one actual argument type against the next formal in the type-code string, advancing 'pos' past it.
            protected bool check_simple_arg(TypeSpec argtype, string formals, ref int pos, bool coerce)
            {
                int advance;
                TypeSpec formaltype = TypeSpec.type_from_code(formals, pos, out advance);
                pos += advance;
                if (argtype == formaltype)
                {
                    return true;  // ok, move on to next arg
                }
                if (coerce && PvtFunctions.assignable(formaltype, argtype))
                {
                    return true;
                }
                if (formaltype.is_unsized_array() && argtype.is_sized_array() && formaltype.elementtype() == argtype.elementtype())
                {
                    return true;
                }
                return false;
            }

            /// Check an argument list against a formals type-code string.
            protected bool check_arglist(string funcname, ASTNode arg, string formals, bool coerce = false, bool bind = true)
            {
                if (formals == null)
                {
                    return false;
                }
                int pos = 0;
                for (; arg != null; arg = arg.next())
                {
                    char f = CharAt(formals, pos);
                    if (f == '\0')  // More formal args, but no more actual args
                    {
                        return false;
                    }
                    if (f == '*')  // Will match anything left
                    {
                        return true;
                    }
                    if (f == '.')  // Special case for token/value pairs
                    {
                        if (arg.typespec().is_string() && arg.next() != null)
                        {
                            arg = arg.next();
                            continue;
                        }
                        return false;
                    }
                    if (f == '?')
                    {
                        if (CharAt(formals, pos + 1) == '[' && CharAt(formals, pos + 2) == ']')
                        {
                            pos += 3;
                            if (arg.typespec().is_array())
                            {
                                continue;  // match
                            }
                            return false;  // wanted an array, didn't get one
                        }
                        if (arg.typespec().is_array())
                        {
                            return false;  // wanted any scalar, got an array
                        }
                        pos += 1;
                        continue;  // match anything
                    }

                    TypeSpec formaltype = new TypeSpec();
                    if (arg.nodetype() == NodeType.compound_initializer_node)
                    {
                        int advance;
                        TypeSpec initformal = TypeSpec.type_from_code(formals, pos, out advance);

                        using (ASTcompound_initializer.TypeAdjuster ta = new ASTcompound_initializer.TypeAdjuster(m_compiler, ASTcompound_initializer.TypeAdjuster.no_errors))
                        {
                            TypeSpec itype = ta.typecheck((ASTcompound_initializer)arg, initformal);
                            if (!bind)
                            {
                                ta.nobind();
                            }
                        }
                    }
                    else
                    {
                        formaltype = arg.typespec();
                    }

                    if (!check_simple_arg(formaltype, formals, ref pos, coerce))
                    {
                        return false;
                    }
                }
                char last = CharAt(formals, pos);
                if (last != '\0' && last != '*' && last != '.')
                {
                    return false;  // Non-*, non-... formals expected, no more actuals
                }

                return true;  // Is this safe?
            }
        }

        public sealed partial class ASTfunction_declaration
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                m_compiler.push_function(func());
                typecheck_children(expected);
                m_compiler.pop_function();
                if (m_typespec == new TypeSpec())
                {
                    m_typespec = expected;
                }
                return m_typespec;
            }
        }

        public sealed partial class ASTvariable_declaration
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                typecheck_children(m_typespec);
                ASTNode init = this.init();

                if (init == null)
                {
                    return m_typespec;
                }

                TypeSpec vt = m_typespec;        // Type of the variable
                TypeSpec et = init.typespec();   // Type of expression assigned

                if (m_typespec.is_structure() && !m_initlist && et.structure() != vt.structure())
                {
                    errorfmt("Cannot initialize " + vt + " " + name() + " = " + et);
                    return m_typespec;
                }

                if (init.nodetype() == NodeType.compound_initializer_node)
                {
                    if (init.nextptr() != null)
                    {
                        errorfmt("compound_initializer should be the only initializer");
                    }
                    init = ((ASTcompound_initializer)init).initlist();
                    if (init == null)
                    {
                        return m_typespec;
                    }
                }

                // Special case: it's ok to initialize a closure with 0
                if (vt.is_closure() && !et.is_closure() && (et.is_float() || et.is_int()) && init.nodetype() == NodeType.literal_node
                    && ((ASTliteral)init).floatval() == 0.0f)
                {
                    return m_typespec;  // it's ok
                }

                if (!PvtFunctions.assignable(vt, et))
                {
                    if (vt.is_int() && et.is_float())
                    {
                        warningfmt("Assignment may lose precision: " + vt + " " + name() + " = " + et);
                    }
                    else
                    {
                        errorfmt("Cannot assign " + vt + " " + name() + " = " + et);
                    }
                    return m_typespec;
                }

                return m_typespec;
            }
        }

        public sealed partial class ASTvariable_ref
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                m_is_lvalue = true;  // A var ref is an lvalue
                return m_typespec;
            }
        }

        public sealed partial class ASTpreincdec
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                typecheck_children();
                m_is_lvalue = var().is_lvalue();
                m_typespec = var().typespec();
                return m_typespec;
            }
        }

        public sealed partial class ASTpostincdec
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                typecheck_children();
                if (!var().is_lvalue())
                {
                    errorfmt(nodetypename() + " can only be applied to an lvalue");
                }
                m_is_lvalue = false;
                m_typespec = var().typespec();
                return m_typespec;
            }
        }

        public sealed partial class ASTindex
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                typecheck_children();
                string indextype = "";
                TypeSpec t = lvalue().typespec();
                if (t.is_structure())
                {
                    errorfmt("Cannot use [] indexing on a struct");
                    return new TypeSpec();
                }
                if (t.is_closure())
                {
                    errorfmt("Cannot use [] indexing on a closure");
                    return new TypeSpec();
                }
                if (index3() != null)
                {
                    if (!t.is_array() && !t.elementtype().is_matrix())
                    {
                        errorfmt("[][][] only valid for a matrix array");
                    }
                    m_typespec = TypeFloat;
                }
                else if (t.is_array())
                {
                    indextype = "array";
                    m_typespec = t.elementtype();
                    if (index2() != null)
                    {
                        if (t.aggregate() == TypeDesc.SCALAR)
                        {
                            errorfmt("can't use [][] on a simple array");
                        }
                        m_typespec = TypeFloat;
                    }
                }
                else if (t.aggregate() == TypeDesc.VEC3)
                {
                    indextype = "component";
                    TypeDesc tnew = t.simpletype();
                    tnew.aggregate = (byte)TypeDesc.SCALAR;
                    tnew.vecsemantics = (byte)TypeDesc.NOXFORM;
                    m_typespec = tnew;
                    if (index2() != null)
                    {
                        errorfmt("can't use [][] on a " + t);
                    }
                }
                else if (t.aggregate() == TypeDesc.MATRIX44)
                {
                    indextype = "component";
                    TypeDesc tnew = t.simpletype();
                    tnew.aggregate = (byte)TypeDesc.SCALAR;
                    tnew.vecsemantics = (byte)TypeDesc.NOXFORM;
                    m_typespec = tnew;
                    if (index2() == null)
                    {
                        errorfmt("must use [][] on a matrix, not just []");
                    }
                }
                else
                {
                    errorfmt("can only use [] indexing for arrays or multi-component types");
                    return new TypeSpec();
                }

                for (int c = 1; c < nchildren(); ++c)
                {
                    if (!child(c).typespec().is_int())
                    {
                        errorfmt(indextype + " index must be an integer, not a " + index().typespec());
                    }
                }

                m_is_lvalue = lvalue().is_lvalue();

                return m_typespec;
            }
        }

        public sealed partial class ASTstructselect
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                if (compindex() != null)
                {
                    return compindex().typecheck(expected);
                }

                if (m_fieldid < 0 || m_fieldsym == null)
                {
                    return new TypeSpec();
                }

                typecheck_children();
                StructSpec structspec = TypeSpec.structspec(m_structid);
                m_typespec = structspec.field(m_fieldid).type;
                m_is_lvalue = lvalue().is_lvalue();
                return m_typespec;
            }
        }

        public sealed partial class ASTconditional_statement
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                typecheck_list(cond());
                m_compiler.push_nesting(false);
                typecheck_list(truestmt());
                typecheck_list(falsestmt());
                m_compiler.pop_nesting(false);

                TypeSpec c = cond().typespec();
                if (c.is_structure())
                {
                    errorfmt("Cannot use a struct as an 'if' condition");
                }
                if (c.is_array())
                {
                    errorfmt("Cannot use an array as an 'if' condition");
                }
                return m_typespec = new TypeDesc(TypeDesc.NONE);
            }
        }

        public sealed partial class ASTloop_statement
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                typecheck_list(init());
                m_compiler.push_nesting(true);
                typecheck_list(cond());
                typecheck_list(iter());
                typecheck_list(stmt());
                m_compiler.pop_nesting(true);

                TypeSpec c = cond().typespec();
                if (c.is_closure())
                {
                    errorfmt("Cannot use a closure as an '" + opname() + "' condition");
                }
                if (c.is_structure())
                {
                    errorfmt("Cannot use a struct as an '" + opname() + "' condition");
                }
                if (c.is_array())
                {
                    errorfmt("Cannot use an array as an '" + opname() + "' condition");
                }
                return m_typespec = new TypeDesc(TypeDesc.NONE);
            }
        }

        public sealed partial class ASTloopmod_statement
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                if (m_compiler.nesting_level(true /*loops*/) < 1)
                {
                    errorfmt("Cannot '" + opname() + "' here -- not inside a loop.");
                }
                return m_typespec = new TypeDesc(TypeDesc.NONE);
            }
        }

        public sealed partial class ASTassign_expression
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                TypeSpec vt = var().typecheck();     // Type of the variable
                TypeSpec et = expr().typecheck(vt);  // Type of the expression assigned
                m_typespec = vt;

                if (!var().is_lvalue())
                {
                    errorfmt("Can't assign via " + opname() + " to something that isn't an lvalue");
                    return new TypeSpec();
                }

                ustring varname = new ustring();
                if (var().nodetype() == NodeType.variable_ref_node)
                {
                    varname = ((ASTvariable_ref)var()).name();
                }

                if (vt.is_array() || et.is_array())
                {
                    // Special case: array assignments, which are only allowed when the lengths match or the source is shorter
                    if (vt.is_array() && et.is_array() && vt.arraylength() >= et.arraylength())
                    {
                        // Special case: struct arrays
                        if (vt.structure() != 0 && (vt.structure() == et.structure()))
                        {
                            return m_typespec;
                        }
                        // Ordinary arrays of equivalent element types
                        if (PvtFunctions.equivalent(vt.elementtype(), et.elementtype()) && vt.structure() == 0 && et.structure() == 0)
                        {
                            return m_typespec;
                        }
                    }
                    errorfmt("Cannot assign " + vt + " " + varname + " = " + et);
                    return m_typespec;
                }

                // Special case: it's ok to assign 0 to a closure
                if (vt.is_closure() && !et.is_closure() && (et.is_float() || et.is_int()) && expr().nodetype() == NodeType.literal_node
                    && ((ASTliteral)expr()).floatval() == 0.0f)
                {
                    return m_typespec;  // it's ok
                }

                if (vt.is_structure() || et.is_structure())
                {
                    int vts = vt.structure(), ets = et.structure();
                    if (vts == ets)
                    {
                        return m_typespec = vt;
                    }
                    errorfmt("Cannot assign " + vt + " " + varname + " = " + et);
                    return m_typespec;
                }

                if (!PvtFunctions.assignable(vt, et))
                {
                    if (vt.is_int() && et.is_float())
                    {
                        warningfmt("Assignment may lose precision: " + vt + " " + varname + " = " + et);
                    }
                    else
                    {
                        errorfmt("Cannot assign " + vt + " " + varname + " = " + et);
                    }
                    return m_typespec;
                }

                return m_typespec;
            }
        }

        public sealed partial class ASTreturn_statement
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                FunctionSymbol myfunc = m_compiler.current_function();
                if (myfunc != null)
                {
                    // If we're in a function, make sure the return type matches the function's
                    if (expr() != null)
                    {
                        TypeSpec et = expr().typecheck(myfunc.typespec());
                        if (!PvtFunctions.assignable(myfunc.typespec(), et))
                        {
                            errorfmt("Cannot return a '" + et + "' from '" + myfunc.typespec() + " " + myfunc.name() + "()'");
                        }
                    }
                    else
                    {
                        if (!myfunc.typespec().is_void())
                        {
                            errorfmt("You must return a '" + myfunc.typespec() + "' from function '" + myfunc.name() + "'");
                        }
                    }
                    myfunc.encountered_return();
                }
                else
                {
                    // Not in a function: we are in a shader body, which can't return a value
                    if (expr() != null)
                    {
                        errorfmt("Cannot return a value from a shader body");
                    }
                }
                return new TypeSpec();  // TODO: what should be returned here?
            }
        }

        public sealed partial class ASTunary_expression
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                typecheck_children(expected);
                TypeSpec t = expr().typespec();

                if (m_function_overload != null)
                {
                    // Look for a user-supplied operator overload
                    for (FunctionSymbol f = m_function_overload; f != null; f = f.nextpoly())
                    {
                        string code = f.argcodes().@string();
                        int advance;
                        TypeSpec returntype = TypeSpec.type_from_code(code, 0, out advance);
                        int pos = advance;
                        if (CharAt(code, pos) != '\0' && check_simple_arg(t, code, ref pos, true) && CharAt(code, pos) == '\0')
                        {
                            return m_typespec = returntype;
                        }
                    }
                    m_function_overload = null;
                }

                if (t.is_structure() || t.is_array())
                {
                    errorfmt("Can't do '" + opname() + "' to a " + t + ".");
                    return new TypeSpec();
                }
                switch (m_op)
                {
                    case Sub:
                    case Add:
                        if (!(t.is_closure() || t.is_numeric()))
                        {
                            errorfmt("Can't do '" + opname() + "' to a " + t + ".");
                            return new TypeSpec();
                        }
                        m_typespec = t;
                        break;
                    case Not:
                        m_typespec = TypeInt;  // ! is always an int
                        break;
                    case Compl:
                        if (!t.is_int())
                        {
                            errorfmt("Operator '~' can only be done to an int");
                            return new TypeSpec();
                        }
                        m_typespec = t;
                        break;
                    default:
                        errorfmt("unknown unary operator");
                        break;
                }
                return m_typespec;
            }
        }

        public sealed partial class ASTbinary_expression
        {
            private static TypeDesc higherprecision(TypeDesc a, TypeDesc b)
            {
                if (a.aggregate > b.aggregate)
                {
                    return a;
                }
                else if (b.aggregate > a.aggregate)
                {
                    return b;
                }
                // Same aggregate, so the higher of the base types
                if (b.basetype == TypeDesc.FLOAT)
                {
                    return b;
                }
                return a;
            }

            internal static TypeDesc higherprecisionOf(TypeDesc a, TypeDesc b)
            {
                return higherprecision(a, b);
            }

            public override TypeSpec typecheck(TypeSpec expected)
            {
                typecheck_children(expected);
                TypeSpec l = left().typespec();
                TypeSpec r = right().typespec();

                if (m_function_overload != null)
                {
                    // Look for a user-supplied operator overload
                    for (FunctionSymbol f = m_function_overload; f != null; f = f.nextpoly())
                    {
                        string code = f.argcodes().@string();
                        int advance;
                        TypeSpec returntype = TypeSpec.type_from_code(code, 0, out advance);
                        int pos = advance;
                        if (CharAt(code, pos) != '\0' && check_simple_arg(l, code, ref pos, true) && CharAt(code, pos) != '\0'
                            && check_simple_arg(r, code, ref pos, true) && CharAt(code, pos) == '\0')
                        {
                            return m_typespec = returntype;
                        }
                    }
                    m_function_overload = null;
                }

                // Structs and arrays are not allowed in any binary expression
                if (l.is_structure() || r.is_structure() || l.is_array() || r.is_array())
                {
                    errorfmt("Not allowed: '" + l + " " + opname() + " " + r + "'");
                    return new TypeSpec();
                }

                // Special cases for closures
                if (l.is_color_closure() || r.is_color_closure())
                {
                    if (m_op == Add)
                    {
                        if (l.is_color_closure() && r.is_color_closure())
                        {
                            return m_typespec = l;
                        }
                    }
                    if (m_op == Mul)
                    {
                        if (l.is_color_closure() != r.is_color_closure())
                        {
                            if (l.is_color_closure() && (r.is_color() || r.is_int_or_float()))
                            {
                                return m_typespec = l;
                            }
                            if (r.is_color_closure() && (l.is_color() || l.is_int_or_float()))
                            {
                                // Swap the operands so the closure is on the left
                                ASTNode swap = m_children[0];
                                m_children[0] = m_children[1];
                                m_children[1] = swap;
                                return m_typespec = r;
                            }
                        }
                    }
                    if (m_op == And || m_op == Or)
                    {
                        return m_typespec = TypeInt;
                    }
                    errorfmt("Not allowed: '" + l + " " + opname() + " " + r + "'");
                    return new TypeSpec();
                }

                switch (m_op)
                {
                    case Sub:
                    case Add:
                    case Mul:
                    case Div:
                        // No strings
                        if (l.is_string() || r.is_string())
                        {
                            break;  // Dispense with strings trivially
                        }
                        // No matrix + or -
                        if ((m_op == Sub || m_op == Add) && (l.is_matrix() || r.is_matrix()))
                        {
                            break;  // Matrices don't combine for + and -
                        }
                        // Equivalent types are always ok
                        if (PvtFunctions.equivalent(l, r))
                        {
                            if (m_op == Sub && l.is_point() && r.is_point())  // p-p == v
                            {
                                return m_typespec = TypeVector;
                            }
                            if ((m_op == Add || m_op == Sub) && (l.is_point() || r.is_point()))  // p +/- v, v +/- p == p
                            {
                                return m_typespec = TypePoint;
                            }
                            return m_typespec = l;
                        }
                        // Numeric combinations with a scalar
                        if ((l.is_numeric() && r.is_int_or_float()) || (l.is_int_or_float() && r.is_numeric()))
                        {
                            return m_typespec = higherprecision(l.simpletype(), r.simpletype());
                        }
                        break;

                    case Mod:
                        if (l.is_int() && r.is_int())
                        {
                            return m_typespec = TypeInt;
                        }
                        break;

                    case Equal:
                    case NotEqual:
                        if (PvtFunctions.equivalent(l, r) || (l.is_numeric() && r.is_int_or_float()) || (l.is_int_or_float() && r.is_numeric()))
                        {
                            return m_typespec = TypeInt;
                        }
                        break;

                    case Greater:
                    case Less:
                    case GreaterEqual:
                    case LessEqual:
                        if (l.is_int_or_float() && r.is_int_or_float())
                        {
                            return m_typespec = TypeInt;
                        }
                        break;

                    case BitAnd:
                    case BitOr:
                    case Xor:
                    case ShiftLeft:
                    case ShiftRight:
                        if (l.is_int() && r.is_int())
                        {
                            return m_typespec = TypeInt;
                        }
                        break;

                    case And:
                    case Or:
                        return m_typespec = TypeInt;

                    default:
                        errorfmt("unknown binary operator");
                        break;
                }

                errorfmt("Not allowed: '" + l + " " + opname() + " " + r + "'");
                return new TypeSpec();
            }
        }

        public sealed partial class ASTternary_expression
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                TypeSpec c = typecheck_list(cond(), TypeInt);
                TypeSpec t = typecheck_list(trueexpr(), expected);
                TypeSpec f = typecheck_list(falseexpr(), expected);

                if (c.is_closure())
                {
                    errorfmt("Cannot use a closure as a condition");
                }
                if (c.is_structure())
                {
                    errorfmt("Cannot use a struct as a condition");
                }
                if (c.is_array())
                {
                    errorfmt("Cannot use an array as a condition");
                }

                // No arrays
                if (t.is_array() || f.is_array())
                {
                    errorfmt("Not allowed: '" + c + " ? " + t + " : " + f + "'");
                    return new TypeSpec();
                }

                // The ternary takes the higher precision type of the two branches
                if (PvtFunctions.assignable(t, f) || PvtFunctions.assignable(f, t))
                {
                    m_typespec = ASTbinary_expression.higherprecisionOf(t.simpletype(), f.simpletype());
                }
                else
                {
                    errorfmt("Not allowed: '" + c + " ? " + t + " : " + f + "'");
                }

                return m_typespec;
            }
        }

        public sealed partial class ASTcomma_operator
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                return m_typespec = typecheck_list(expr(), expected);
            }
        }

        public sealed partial class ASTtypecast_expression
        {
            public override TypeSpec typecheck(TypeSpec expected)
            {
                typecheck_children(m_typespec);
                TypeSpec t = expr().typespec();
                if (!PvtFunctions.assignable(m_typespec, t) && !(m_typespec.is_int() && t.is_float())  // (int)float is ok
                    && !(m_typespec.is_triple() && t.is_triple()))
                {
                    errorfmt("Cannot cast '" + t + "' to '" + m_typespec + "'");
                }
                return m_typespec;
            }
        }

        public partial class ASTtype_constructor
        {
            private static readonly string[] float_patterns = { "ff", "fi" };
            private static readonly string[] triple_patterns = { "cf", "cfff", "csfff", "cc", "cp", "cv", "cn" };
            private static readonly string[] matrix_patterns = { "mf", "msf", "mss", "mffffffffffffffff", "msffffffffffffffff", "mm" };
            private static readonly string[] int_patterns = { "if", "ii" };

            public override TypeSpec typecheck(TypeSpec expected)
            {
                return typecheck(m_typespec, true, true);
            }

            public TypeSpec typecheck(TypeSpec expected, bool report, bool bind)
            {
                string[] patterns = null;
                TypeSpec argexpected = new TypeSpec();  // default to unknown
                if (expected.is_float())
                {
                    patterns = float_patterns;
                    argexpected = TypeFloat;
                }
                else if (expected.is_triple())
                {
                    patterns = triple_patterns;
                    if (listlength(args()) == 1)
                    {
                        argexpected = expected;
                    }
                    else
                    {
                        argexpected = TypeFloat;
                    }
                }
                else if (expected.is_matrix())
                {
                    patterns = matrix_patterns;
                }
                else if (expected.is_int())
                {
                    patterns = int_patterns;
                }
                else
                {
                    if (report)
                    {
                        errorfmt("Cannot construct type '" + expected + "'");
                    }
                    return new TypeSpec();
                }

                typecheck_children(argexpected);

                // Try twice, first with exact matches, then with coercion
                for (int co = 0; co < 2; ++co)
                {
                    bool coerce = co != 0;
                    foreach (string pat in patterns)
                    {
                        if (check_arglist(expected.type_c_str(), args(), pat.Substring(1), coerce, bind))
                        {
                            return expected;
                        }
                    }
                }

                if (report)
                {
                    StringBuilder err = new StringBuilder("Cannot construct " + expected + " (");
                    for (ASTNode a = args(); a != null; a = a.next())
                    {
                        err.Append(a.typespec().@string());
                        if (a.next() != null)
                        {
                            err.Append(", ");
                        }
                    }
                    err.Append(")");
                    errorfmt(err.ToString());
                }
                return new TypeSpec();
            }
        }

        public sealed partial class ASTcompound_initializer
        {
            /// Type checks initializer lists against the type they initialize, and records what each list turned out to be; the records are
            /// committed when the adjuster is disposed, as the C++ destructor does.
            public sealed class TypeAdjuster : IDisposable
            {
                public const int default_flags = 0;
                public const int no_errors = 1;        /// Don't report errors in typecheck calls
                public const int must_init_all = 1 << 1;  /// All fields/elements must be inited
                public const int function_arg = no_errors | must_init_all;

                private List<(ASTcompound_initializer, TypeSpec, bool)> m_adjust = new List<(ASTcompound_initializer, TypeSpec, bool)>();
                private OSLCompilerImpl m_compiler;
                private readonly int m_mode;
                private bool m_success;
                private bool m_debug_successful;  // Only for nobind() & bind() cycle assertion.
                private bool m_disposed;

                public TypeAdjuster(OSLCompilerImpl c, int m = default_flags)
                {
                    m_compiler = c;
                    m_mode = m;
                    m_success = true;
                    m_debug_successful = false;
                }

                ~TypeAdjuster()
                {
                    Dispose(false);
                }

                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                private void Dispose(bool disposing)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                    if (disposing && m_success)
                    {
                        foreach ((ASTcompound_initializer, TypeSpec, bool) initer in m_adjust)
                        {
                            ASTcompound_initializer ciptr = initer.Item1;
                            TypeSpec type = initer.Item2;
                            if (!(ciptr.m_typespec.is_sized_array() && type.is_unsized_array()))
                            {
                                ciptr.m_typespec = type;
                            }
                            ciptr.m_ctor = initer.Item3;
                        }
                    }
                }

                public void typecheck_init(ASTcompound_initializer cinit, TypeSpec to)
                {
                    if (cinit.nchildren() == 0)
                    {
                        if ((m_mode & must_init_all) != 0)
                        {
                            m_success = false;
                            if (errors())
                            {
                                cinit.errorfmt("Empty initializer list not allowed to represent '" + to + "' here");
                            }
                        }
                        return;
                    }

                    if (((ASTtype_constructor)cinit).typecheck(to, errors(), false) == to)
                    {
                        mark_type(cinit, to, true);
                    }
                    else
                    {
                        m_success = false;
                    }
                }

                public void typecheck_array(ASTcompound_initializer init, TypeSpec expected)
                {
                    if (init.initlist() == null)
                    {
                        return;  // early out for empty initializer { }
                    }
                    TypeSpec elemtype = expected.elementtype();

                    int nelem = 1;
                    ASTcompound_initializer cinit = init;

                    if (init.initlist().nodetype() != NodeType.compound_initializer_node)
                    {
                        if (!typecheck_expr(init.initlist(), elemtype))
                        {
                            return;
                        }
                        cinit = next_initlist(init.initlist(), elemtype, ref nelem);
                    }
                    else
                    {
                        cinit = (ASTcompound_initializer)cinit.initlist();
                    }

                    if (!elemtype.is_structure())
                    {
                        while (cinit != null)
                        {
                            typecheck_init(cinit, elemtype);
                            cinit = next_initlist(cinit, elemtype, ref nelem);
                        }
                    }
                    else
                    {
                        while (cinit != null)
                        {
                            typecheck_fields(cinit, cinit.initlist(), elemtype);
                            cinit = next_initlist(cinit, elemtype, ref nelem);
                        }
                    }

                    if (m_success && (expected.is_unsized_array() || validate_size(nelem, expected.arraylength())))
                    {
                        mark_type(init, expected);
                        return;
                    }

                    m_success = false;
                    if (errors())
                    {
                        init.errorfmt("Too " + (nelem < expected.arraylength() ? "few" : "many") + " initializers for a '" + expected + "'");
                    }
                }

                public void typecheck_fields(ASTNode parent, ASTNode arg, TypeSpec expected)
                {
                    StructSpec structspec = expected.structspec();
                    int ninits = 0, nfields = structspec.numfields();
                    while (arg != null && ninits < nfields)
                    {
                        StructSpec.FieldSpec field = structspec.field(ninits++);
                        TypeSpec ftype = field.type;
                        if (arg.nodetype() == NodeType.compound_initializer_node)
                        {
                            ASTcompound_initializer cinit = (ASTcompound_initializer)arg;
                            if (!field.type.is_array())
                            {
                                if (!field.type.is_structure())
                                {
                                    typecheck_init(cinit, field.type);
                                }
                                else if (cinit.initlist() != null)
                                {
                                    typecheck_fields(cinit, cinit.initlist(), ftype);
                                }
                                else if ((m_mode & must_init_all) != 0)
                                {
                                    m_success = false;  // empty init list not allowed
                                }
                            }
                            else
                            {
                                typecheck_array(cinit, ftype);
                            }

                            if (!m_success && !errors())
                            {
                                return;
                            }
                        }
                        else if (!typecheck_expr(arg, ftype, structspec, field))
                        {
                            return;
                        }

                        arg = arg.next();
                    }

                    if (m_success && arg == null && validate_size(ninits, nfields))
                    {
                        if (parent.nodetype() == NodeType.compound_initializer_node)
                        {
                            mark_type((ASTcompound_initializer)parent, expected);
                        }
                        return;
                    }

                    m_success = false;
                    if (errors() && (arg != null || nfields > ninits))
                    {
                        parent.errorfmt("Too " + (ninits < nfields ? "few" : "many") + " initializers for struct '" + structspec.name() + "'");
                    }
                }

                public TypeSpec typecheck(ASTcompound_initializer ilist, TypeSpec expected)
                {
                    if (!expected.is_array())
                    {
                        if (expected.is_structure())
                        {
                            typecheck_fields(ilist, ilist.initlist(), expected);
                        }
                        else
                        {
                            typecheck_init(ilist, expected);
                        }
                    }
                    else
                    {
                        typecheck_array(ilist, expected);
                    }

                    return type();
                }

                public TypeSpec nobind()
                {
                    m_debug_successful = m_success;
                    m_success = false;  // Turn off the binding in destructor
                    return type();
                }

                public void bind()
                {
                    m_success = true;
                }

                public TypeSpec type()
                {
                    return (m_success || m_debug_successful) && m_adjust.Count > 0 ? m_adjust[m_adjust.Count - 1].Item2 : new TypeSpec(new TypeDesc(TypeDesc.NONE));
                }

                public bool success() { return m_success; }

                private void mark_type(ASTcompound_initializer i, TypeSpec t, bool c = false)
                {
                    m_adjust.Add((i, t, c));
                }

                private bool errors() { return (m_mode & no_errors) == 0; }

                private bool validate_size(int expected, int actual)
                {
                    if (expected > actual)
                    {
                        return false;
                    }
                    return (m_mode & must_init_all) != 0 ? (expected == actual) : true;
                }

                private ASTcompound_initializer next_initlist(ASTNode node, TypeSpec expected, ref int nelem)
                {
                    if (m_success || errors())
                    {
                        for (node = node.nextptr(); node != null; node = node.nextptr())
                        {
                            ++nelem;
                            if (node.nodetype() == NodeType.compound_initializer_node)
                            {
                                return (ASTcompound_initializer)node;
                            }
                            node.typecheck(expected);
                        }
                    }
                    return null;
                }

                // The C++ overload that takes a single expression node, renamed here because C# would pick it for derived types too
                private bool typecheck_expr(ASTNode node, TypeSpec expected, StructSpec spec = null, StructSpec.FieldSpec? field = null)
                {
                    if (node.typecheck(expected) != expected && !PvtFunctions.assignable(expected, node.typespec())
                        && !(expected.is_closure() && !node.typespec().is_closure() && node.typespec().is_int_or_float()
                             && node.nodetype() == NodeType.literal_node && ((ASTliteral)node).floatval() == 0.0f))
                    {
                        m_success = false;
                        if (!errors())
                        {
                            return false;
                        }

                        node.errorfmt("Can't assign '" + node.typespec() + "' to '" + expected
                                      + (spec == null ? "" : " " + spec.name() + "." + field.Value.name) + "'");
                    }
                    return true;
                }
            }

            public TypeSpec typecheck(TypeSpec expected, int mode)
            {
                // Typecheck is called more than once, and the second time we need to leave the node alone
                if (m_ctor || m_typespec.is_structure_based() || m_typespec.simpletype().basetype != TypeDesc.UNKNOWN)
                {
                    if (m_typespec != expected)
                    {
                        errorfmt("Cannot construct type '" + expected + "'");
                    }
                    return m_typespec;
                }

                using (TypeAdjuster adjuster = new TypeAdjuster(m_compiler, mode))
                {
                    adjuster.typecheck(this, expected);
                }

                return m_typespec;
            }

            public override TypeSpec typecheck(TypeSpec expected)
            {
                return typecheck(expected, 0);
            }
        }

        public sealed partial class ASTfunction_call
        {
            private void mark_optional_output(int firstopt, string[] tags)
            {
                bool mark_all = tags.Length > 0 && tags[0] != null && tags[0].Length > 0 && tags[0][0] == '*';
                List<ASTNode> argvec = new List<ASTNode>();
                list_to_vec(args(), argvec);

                int nargs = listlength(args());
                while (firstopt < nargs && !argvec[firstopt].typespec().is_string())
                {
                    ++firstopt;
                }

                for (int a = firstopt; a < argvec.Count - 1; a += 2)
                {
                    ASTNode s = argvec[a];
                    bool isoutput = false;
                    if (s.typespec().is_string())
                    {
                        if (s.nodetype() == NodeType.literal_node)
                        {
                            foreach (string tag in tags)
                            {
                                if (isoutput)
                                {
                                    break;
                                }
                                isoutput = isoutput || mark_all || ((ASTliteral)s).strval() == tag;
                            }
                        }
                        else
                        {
                            isoutput = true;
                        }
                    }
                    if (isoutput)
                    {
                        if (a + 2 < 32)
                        {
                            argwriteonly(a + 2);  // mark writeable
                        }
                        else
                        {
                            // Too many arguments to track by bit: move the pair to the front
                            ASTNode t0 = argvec[firstopt];
                            argvec[firstopt] = argvec[a];
                            argvec[a] = t0;
                            ASTNode t1 = argvec[firstopt + 1];
                            argvec[firstopt + 1] = argvec[a + 1];
                            argvec[a + 1] = t1;
                            argwriteonly(firstopt + 1);
                            firstopt += 2;  // advance in case another is needed
                        }
                    }
                }

                m_children[0] = vec_to_list(argvec);
            }

            private bool typecheck_printf_args(string format, ASTNode arg)
            {
                int argnum = (m_name == "fprintf") ? 3 : 2;
                int p = 0;
                while (p < format.Length)
                {
                    if (format[p] == '%')
                    {
                        if (CharAt(format, p + 1) == '%')
                        {
                            p += 2;  // skip both percentages
                            continue;
                        }
                        // Skip to the format char
                        while (p < format.Length && "cdeifgmnopsuvxX".IndexOf(format[p]) < 0)
                        {
                            ++p;
                        }
                        char formatchar = CharAt(format, p++);  // Also eat the format char
                        if (arg == null)
                        {
                            errorfmt(m_name + " has mismatched format string and arguments (not enough args)");
                            return false;
                        }
                        if (arg.typespec().is_structure_based())
                        {
                            errorfmt("struct '" + arg.typespec().structspec().name() + "' is not a valid argument");
                            return false;
                        }

                        TypeDesc simpletype = arg.typespec().simpletype();
                        if ((arg.typespec().is_closure_based() || simpletype.basetype == TypeDesc.STRING) && formatchar != 's')
                        {
                            errorfmt(m_name + " has mismatched format string and arguments (arg " + argnum + " needs %s)");
                            return false;
                        }
                        if (simpletype.basetype == TypeDesc.INT && formatchar != 'd' && formatchar != 'i' && formatchar != 'o' && formatchar != 'x'
                            && formatchar != 'X')
                        {
                            errorfmt(m_name + " has mismatched format string and arguments (arg " + argnum + " needs %d, %i, %o, %x, or %X)");
                            return false;
                        }
                        if (simpletype.basetype == TypeDesc.FLOAT && formatchar != 'f' && formatchar != 'g' && formatchar != 'c' && formatchar != 'e'
                            && formatchar != 'm' && formatchar != 'n' && formatchar != 'p' && formatchar != 'v')
                        {
                            errorfmt(m_name + " has mismatched format string and arguments (arg " + argnum + " needs %f, %g, or %e)");
                            return false;
                        }

                        arg = arg.nextptr();
                        ++argnum;
                    }
                    else
                    {
                        ++p;
                    }
                }
                if (arg != null)
                {
                    errorfmt(m_name + " has mismatched format string and arguments (too many args)");
                    return false;
                }
                return true;  // all ok
            }

            private void typecheck_builtin_specialcase()
            {
                string[] tex_out_args = { "alpha", "errormessage" };
                string[] pointcloud_out_args = { "*" };

                if (m_name == "transform")
                {
                    if (typespec().simpletype() == TypeVector)
                    {
                        m_name = new ustring("transformv");
                    }
                    else if (typespec().simpletype() == TypeNormal)
                    {
                        m_name = new ustring("transformn");
                    }
                }

                // Void functions don't write their first argument
                if (typespec().is_void())
                {
                    argread(0, true);
                    argwrite(0, false);
                }

                if (func().readwrite_special_case())
                {
                    int nargs = listlength(args());
                    if (m_name == "sincos")
                    {
                        argwriteonly(1);
                        argwriteonly(2);
                    }
                    else if (m_name == "getattribute" || m_name == "getmessage" || m_name == "gettextureinfo" || m_name == "getmatrix"
                             || m_name == "dict_value")
                    {
                        argwriteonly(nargs);
                    }
                    else if (m_name == "pointcloud_get")
                    {
                        argwriteonly(5);
                    }
                    else if (m_name == "pointcloud_search")
                    {
                        mark_optional_output(5, pointcloud_out_args);
                    }
                    else if ((m_name == "regex_search" || m_name == "regex_match") && nargs == 3)
                    {
                        argwriteonly(2);
                    }
                    else if (m_name == "split")
                    {
                        argwriteonly(2);
                    }
                    else if (func().texture_args())
                    {
                        mark_optional_output(2, tex_out_args);
                    }
                }

                if (func().printf_args())
                {
                    ASTNode arg = args();  // first arg
                    if (arg != null && m_name == "fprintf")
                    {
                        arg = arg.nextptr();  // skip filename param for fprintf
                    }
                    string format = null;
                    if (arg != null && arg.nodetype() == NodeType.literal_node && arg.typespec().is_string()
                        && (format = ((ASTliteral)arg).strval()) != null)
                    {
                        arg = arg.nextptr();
                        typecheck_printf_args(format, arg);
                    }
                    else
                    {
                        warningfmt(m_name + "() uses a format string that is not a constant.");
                    }
                }

                if (func().takes_derivs())
                {
                    int nargs = listlength(args());
                    if (m_name == "area" || m_name == "filterwidth")
                    {
                        argtakesderivs(1, true);
                    }
                    else if (m_name == "bump" || m_name == "displace")
                    {
                        // handled elsewhere
                    }
                    else if (m_name == "calculatenormal")
                    {
                        argtakesderivs(1, true);
                    }
                    else if (m_name == "Dx" || m_name == "Dy" || m_name == "Dz")
                    {
                        argtakesderivs(1, true);
                    }
                    else if (m_name == "texture")
                    {
                        // Derivs are needed for the s,t coordinates, if the optional arguments don't start with a string
                        if (nargs == 3 || list_nth(args(), 3).typespec().is_string())
                        {
                            argtakesderivs(2, true);
                            argtakesderivs(3, true);
                        }
                    }
                    else if (m_name == "texture3d")
                    {
                        if (nargs == 2 || list_nth(args(), 2).typespec().is_string())
                        {
                            argtakesderivs(2, true);
                        }
                    }
                    else if (m_name == "environment")
                    {
                        if (nargs == 2 || list_nth(args(), 2).typespec().is_string())
                        {
                            argtakesderivs(2, true);
                        }
                    }
                    else if (m_name == "trace")
                    {
                        argtakesderivs(1, true);
                        argtakesderivs(2, true);
                    }
                    else if (m_name == "noise" || m_name == "pnoise")
                    {
                        ASTNode arg = args();  // first argument
                        if (arg.typespec().is_string())
                        {
                            ASTliteral lit = (arg.nodetype() == NodeType.literal_node) ? (ASTliteral)arg : null;
                            if (lit == null || (lit.ustrval() == "gabor"))
                            {
                                arg = arg.nextptr();  // advance to position
                                for (int n = 2; arg != null && !arg.typespec().is_string(); ++n)
                                {
                                    argtakesderivs(n, true);
                                    arg = arg.nextptr();
                                }
                            }
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Missed a takes_derivs case!");
                    }
                }
            }

            private TypeSpec typecheck_struct_constructor()
            {
                StructSpec structspec = m_sym.typespec().structspec();
                m_typespec = m_sym.typespec();
                if (structspec.numfields() != listlength(args()))
                {
                    errorfmt("Constructor for '" + structspec.name() + "' has the wrong number of arguments (expected " + structspec.numfields()
                             + ", got " + listlength(args()) + ")");
                }

                using (ASTcompound_initializer.TypeAdjuster adjuster = new ASTcompound_initializer.TypeAdjuster(m_compiler))
                {
                    adjuster.typecheck_fields(this, args(), m_typespec);
                }
                return m_typespec;
            }

            public override TypeSpec typecheck(TypeSpec expected)
            {
                if (is_struct_ctr())
                {
                    return typecheck_struct_constructor();
                }

                // Typecheck the args, but skip compound initializers: they can only be checked against a chosen overload
                bool any_args_are_compound_initializers = false;
                for (ASTNode arg = args(); arg != null; arg = arg.next())
                {
                    if (arg.nodetype() != NodeType.compound_initializer_node)
                    {
                        typecheck_list(arg, expected);
                    }
                    else
                    {
                        any_args_are_compound_initializers = true;
                    }
                }

                FunctionSymbol poly = func();

                // The bindings of initializer lists commit when the candidates go away, at the end of this method
                using (CandidateFunctions candidates = new CandidateFunctions(m_compiler, expected, args(), poly))
                {
                    (FunctionSymbol, TypeSpec) chosen = candidates.best(this, m_name);
                    m_sym = chosen.Item1;
                    m_typespec = chosen.Item2;

                    string legacyMode = Environment.GetEnvironmentVariable("OSL_LEGACY_FUNCTION_RESOLUTION");
                    if (!candidates.hadinitlist() && legacyMode != null && legacyMode != "0")
                    {
                        FunctionSymbol legacy = new LegacyOverload(m_compiler, this, poly).resolve(expected);
                        if (m_sym != legacy)
                        {
                            bool as_warning = true;
                            if (string.Equals(legacyMode, "err", StringComparison.OrdinalIgnoreCase))
                            {
                                as_warning = false;  // full error
                            }
                            string errmsg = "  Current overload is\n";
                            if (m_sym != null)
                            {
                                errmsg += candidates.reportFunction((FunctionSymbol)m_sym);
                            }
                            else
                            {
                                errmsg += "<none>";
                            }
                            errmsg += "\n  Prior overload was ";
                            if (legacy != null)
                            {
                                errmsg += candidates.reportFunction(legacy);
                            }
                            else
                            {
                                errmsg += "<none>";
                            }
                            if (string.Equals(legacyMode, "use", StringComparison.OrdinalIgnoreCase))
                            {
                                m_sym = legacy;
                            }
                            if (as_warning)
                            {
                                warningfmt("overload chosen differs from OSL 1.9\n" + errmsg);
                            }
                            else
                            {
                                errorfmt("overload chosen differs from OSL 1.9\n" + errmsg);
                            }
                        }
                    }

                    // Compound initializer arguments to unsized-array formals take the length of their list
                    if (any_args_are_compound_initializers && is_user_function())
                    {
                        ASTNode formal = user_function().formals();
                        for (ASTNode arg = args(); arg != null && formal != null; arg = arg.nextptr(), formal = formal.nextptr())
                        {
                            if (arg.nodetype() == NodeType.compound_initializer_node && formal.typespec().is_unsized_array())
                            {
                                ASTcompound_initializer ci_arg = (ASTcompound_initializer)arg;
                                TypeSpec expectedarray = formal.typespec();
                                expectedarray.make_array(listlength(ci_arg.initlist()));
                                arg.typecheck(expectedarray);
                            }
                        }
                    }

                    if (m_sym == null)
                    {
                        return new TypeSpec();  // no match found
                    }

                    // Warn when an output parameter of a user function is bound to something we can't write
                    if (is_user_function())
                    {
                        ASTfunction_declaration uf = user_function();
                        int i = 0;
                        ASTNode f = uf.formals();  // formal param
                        ASTNode a = args();        // actual param
                        for (; f != null && a != null; f = f.nextptr(), a = a.nextptr(), ++i)
                        {
                            ASTvariable_declaration formal = (ASTvariable_declaration)f;
                            Symbol dest_sym;
                            if (formal.is_output() && !check_symbol_writeability(a, true, out dest_sym))
                            {
                                warningfmt("cannot write to non-output parameter \"" + dest_sym.name() + "\", as passed to function " + func().name()
                                           + "() as arg " + (i + 1) + " (output parameter \"" + formal.name() + "\")");
                            }
                        }
                    }

                    if (is_user_function())
                    {
                        if (func().number_of_returns() == 0 && !func().typespec().is_void())
                        {
                            errorfmt("non-void function \"" + func().name() + "\" had no 'return' statement.");
                        }
                    }
                    else
                    {
                        typecheck_builtin_specialcase();
                    }
                    return m_typespec;
                }
            }

            /// Pre-1.10 overload resolution, kept for comparison behind OSL_LEGACY_FUNCTION_RESOLUTION.
            internal bool check_arglist_for_legacy(string funcname, ASTNode arg, string formals, bool coerce, bool bind)
            {
                return check_arglist(funcname, arg, formals, coerce, bind);
            }
        }

        internal sealed class LegacyOverload
        {
            private OSLCompilerImpl m_compiler;
            private ASTfunction_call m_func;
            private FunctionSymbol m_root;

            public LegacyOverload(OSLCompilerImpl comp, ASTfunction_call func, FunctionSymbol root)
            {
                m_compiler = comp;
                m_func = func;
                m_root = root;
            }

            private (FunctionSymbol, TypeSpec) typecheck_polys(TypeSpec expected, bool coerceargs, bool equivreturn)
            {
                string name = m_func.func().name().@string();
                for (FunctionSymbol poly = m_root; poly != null; poly = poly.nextpoly())
                {
                    string code = poly.argcodes().@string();
                    int advance;
                    TypeSpec returntype = TypeSpec.type_from_code(code, 0, out advance);
                    if (m_func.check_arglist_for_legacy(name, m_func.args(), code.Substring(advance), coerceargs, false))
                    {
                        if (expected == returntype || (equivreturn && PvtFunctions.equivalent(expected, returntype)) || expected == new TypeSpec())
                        {
                            return (poly, returntype);
                        }
                    }
                }
                return (null, new TypeSpec());
            }

            public FunctionSymbol resolve(TypeSpec expected)
            {
                bool match = false;
                (FunctionSymbol, TypeSpec) r;
                FunctionSymbol sym = null;

                // First, look for an exact match of the return type
                r = typecheck_polys(expected, false, false);
                sym = r.Item1;
                if (r.Item2 != new TypeSpec())
                {
                    match = true;
                }

                // Next, an equivalent return type
                r = typecheck_polys(expected, false, true);
                sym = r.Item1;
                if (r.Item2 != new TypeSpec())
                {
                    match = true;
                }

                // Then ignore the return type
                if (!match && expected != new TypeSpec())
                {
                    r = typecheck_polys(new TypeSpec(), false, false);
                    sym = r.Item1;
                    if (r.Item2 != new TypeSpec())
                    {
                        match = true;
                    }
                }

                // Then the same three, with coercible arguments
                if (!match)
                {
                    r = typecheck_polys(expected, true, false);
                    sym = r.Item1;
                    if (r.Item2 != new TypeSpec())
                    {
                        match = true;
                    }
                }
                if (!match)
                {
                    r = typecheck_polys(expected, true, true);
                    sym = r.Item1;
                    if (r.Item2 != new TypeSpec())
                    {
                        match = true;
                    }
                }
                if (!match && expected != new TypeSpec())
                {
                    r = typecheck_polys(new TypeSpec(), true, false);
                    sym = r.Item1;
                    if (r.Item2 != new TypeSpec())
                    {
                        match = true;
                    }
                }

                return match ? sym : null;
            }
        }

        /// Scores every overload of a function against the actual arguments and picks the best one.
        internal sealed class CandidateFunctions : IDisposable
        {
            private const int kExactMatch = 100;
            private const int kIntegralToFP = 77;
            private const int kArrayMatch = 44;
            private const int kCoercable = 23;
            private const int kMatchAnything = 1;
            private const int kNoMatch = 0;

            private const int kFPToIntegral = kNoMatch;
            private const int kSpatialCoerce = kCoercable + 9;
            private const int kTripleCoerce = kCoercable + 4;

            private sealed class Candidate
            {
                public FunctionSymbol sym;
                public TypeSpec rtype;
                public List<(ASTcompound_initializer, ASTcompound_initializer.TypeAdjuster)> bindings
                    = new List<(ASTcompound_initializer, ASTcompound_initializer.TypeAdjuster)>();
                public int ascore;
                public int rscore;

                public Candidate(FunctionSymbol s, TypeSpec rt, int ascore, int rscore)
                {
                    sym = s;
                    rtype = rt;
                    this.ascore = ascore;
                    this.rscore = rscore;
                }
            }

            private OSLCompilerImpl m_compiler;
            private List<Candidate> m_candidates = new List<Candidate>();
            private HashSet<ustring> m_scored = new HashSet<ustring>();
            private TypeSpec m_rval;
            private ASTNode m_args;
            private int m_nargs;
            private FunctionSymbol m_called;  // Function called by name (can be NULL!)
            private bool m_had_initlist;

            private static char CharAt(string s, int pos)
            {
                return pos < s.Length ? s[pos] : '\0';
            }

            // A wildcard in the formals scores the remaining actual arguments as "match anything"
            private int scoreWildcard(ref int argscore, ref int fargs, int pos)
            {
                while (fargs < m_nargs)
                {
                    argscore += kMatchAnything;
                    ++fargs;
                }
                return pos + 1;
            }

            private static int scoreType(TypeSpec expected, TypeSpec actual)
            {
                // Exact match
                if (expected == actual)
                {
                    return kExactMatch;
                }

                // Numeric conversions of scalars
                if (!actual.is_closure() && actual.is_scalarnum() && !expected.is_closure() && expected.is_scalarnum())
                {
                    return expected.is_int() ? kFPToIntegral : kIntegralToFP;
                }

                // A sized array can be passed for an unsized one
                if (expected.is_unsized_array() && actual.is_sized_array() && expected.elementtype() == actual.elementtype())
                {
                    return kArrayMatch;
                }

                if (PvtFunctions.assignable(expected, actual))
                {
                    if (actual.is_vectriple_based() && expected.is_vectriple_based())
                    {
                        return kSpatialCoerce;
                    }
                    if (!actual.is_closure() && actual.is_triple() && !expected.is_closure() && expected.is_triple())
                    {
                        return kTripleCoerce;
                    }
                    return kCoercable;
                }

                return kNoMatch;
            }

            private int addCandidate(FunctionSymbol func)
            {
                if (m_scored.Contains(func.argcodes()))
                {
                    return kNoMatch;
                }
                m_scored.Add(func.argcodes());

                int advance;
                string formals = func.argcodes().@string();
                TypeSpec rtype = TypeSpec.type_from_code(formals, 0, out advance);
                int pos = advance;

                List<(ASTcompound_initializer, ASTcompound_initializer.TypeAdjuster)> bindings
                    = new List<(ASTcompound_initializer, ASTcompound_initializer.TypeAdjuster)>();
                int argscore = 0;
                int fargs = 0;
                for (ASTNode arg = m_args; CharAt(formals, pos) != '\0' && arg != null; ++fargs, arg = arg.next())
                {
                    switch (CharAt(formals, pos))
                    {
                        case '*':  // Will match anything left
                            pos = scoreWildcard(ref argscore, ref fargs, pos);
                            continue;

                        case '.':  // Token/value pairs
                            if (arg.typespec().is_string() && arg.next() != null)
                            {
                                pos = scoreWildcard(ref argscore, ref fargs, pos);
                                continue;
                            }
                            return kNoMatch;

                        case '?':
                            if (CharAt(formals, pos + 1) == '[' && CharAt(formals, pos + 2) == ']')
                            {
                                pos += 3;
                                if (!arg.typespec().is_array())
                                {
                                    return kNoMatch;  // wanted an array, didn't get one
                                }
                                argscore += kMatchAnything;
                            }
                            else if (!arg.typespec().is_array())
                            {
                                pos += 1;  // match anything
                                argscore += kMatchAnything;
                            }
                            else
                            {
                                return kNoMatch;  // wanted any scalar, got an array
                            }
                            continue;

                        default:
                            break;
                    }
                    if (fargs >= m_nargs)
                    {
                        return kNoMatch;
                    }

                    TypeSpec argtype = new TypeSpec();
                    TypeSpec formaltype = TypeSpec.type_from_code(formals, pos, out advance);
                    pos += advance;

                    if (arg.nodetype() == ASTNode.NodeType.compound_initializer_node)
                    {
                        m_had_initlist = true;
                        ASTcompound_initializer ilist = (ASTcompound_initializer)arg;
                        ASTcompound_initializer.TypeAdjuster adjuster = new ASTcompound_initializer.TypeAdjuster(m_compiler, ASTcompound_initializer.TypeAdjuster.function_arg);
                        bindings.Add((ilist, adjuster));

                        adjuster.typecheck(ilist, formaltype);

                        // Don't bind until we've settled on this overload
                        argtype = adjuster.nobind();

                        if (argtype.simpletype().basetype == TypeDesc.NONE)
                        {
                            return kNoMatch;
                        }
                    }
                    else
                    {
                        argtype = arg.typespec();
                    }

                    int score = scoreType(formaltype, argtype);
                    if (score == kNoMatch)
                    {
                        return kNoMatch;
                    }

                    argscore += score;
                }

                // Make sure we've used up all the formals
                switch (CharAt(formals, pos))
                {
                    case '*':
                    case '.':
                        ++pos;
                        ++fargs;
                        goto case '\0';
                    case '\0':
                        if (fargs < m_nargs)
                        {
                            return kNoMatch;
                        }
                        break;

                    default:
                        return kNoMatch;
                }

                int highscore = m_candidates.Count == 0 ? 0 : m_candidates[0].ascore;
                if (argscore < highscore)
                {
                    return kNoMatch;
                }

                if (argscore != highscore)
                {
                    m_candidates.Clear();
                }

                m_candidates.Add(new Candidate(func, rtype, argscore, scoreType(m_rval, rtype)));
                m_candidates[m_candidates.Count - 1].bindings = bindings;

                return argscore;
            }

            public CandidateFunctions(OSLCompilerImpl compiler, TypeSpec rval, ASTNode args, FunctionSymbol func)
            {
                m_compiler = compiler;
                m_rval = rval;
                m_args = args;
                m_nargs = 0;
                m_called = func;
                m_had_initlist = false;
                for (ASTNode arg = m_args; arg != null; arg = arg.next())
                {
                    ++m_nargs;
                }

                while (func != null)
                {
                    addCandidate(func);
                    func = func.nextpoly();
                }
            }

            // The adjusters that were bound for the chosen candidate commit their initializer types here
            public void Dispose()
            {
                foreach (Candidate c in m_candidates)
                {
                    foreach ((ASTcompound_initializer, ASTcompound_initializer.TypeAdjuster) b in c.bindings)
                    {
                        b.Item2.Dispose();
                    }
                }
            }

            public string reportAmbiguity(ustring funcname, bool candidateMsg, string msg)
            {
                StringBuilder argstr = new StringBuilder(funcname.@string());
                argstr.Append(" (");
                string comma = "";
                for (ASTNode arg = m_args; arg != null; arg = arg.next())
                {
                    argstr.Append(comma);
                    if (arg.typespec().simpletype().basetype == TypeDesc.UNKNOWN && arg.nodetype() == ASTNode.NodeType.compound_initializer_node)
                    {
                        argstr.Append("initializer-list");
                    }
                    else
                    {
                        argstr.Append(arg.typespec().@string());
                    }
                    comma = ", ";
                }
                argstr.Append(")");
                return msg + " '" + argstr + "'" + (candidateMsg ? "\n  Candidates are:" : "") + "\n";
            }

            public string reportFunction(FunctionSymbol sym)
            {
                int advance;
                string formals = sym.argcodes().@string();
                TypeSpec returntype = TypeSpec.type_from_code(formals, 0, out advance);
                string msg = "    ";
                ASTNode decl = sym.node();
                if (decl != null)
                {
                    msg += decl.sourcefile() + ":" + decl.sourceline() + "\t";
                }
                msg += returntype + " " + sym.name() + " (" + TypeSpec.typelist_from_code(formals.Substring(advance)) + ")\n";
                return msg;
            }

            private (FunctionSymbol, TypeSpec) bestOf(Candidate c)
            {
                foreach ((ASTcompound_initializer, ASTcompound_initializer.TypeAdjuster) t in c.bindings)
                {
                    t.Item2.bind();
                }
                return (c.sym, c.rtype);
            }

            public (FunctionSymbol, TypeSpec) best(ASTNode caller, ustring funcname)
            {
                string errmsg;
                switch (m_candidates.Count)
                {
                    case 0:
                        errmsg = reportAmbiguity(funcname, m_called != null /*Candidate Msg?*/, "No matching function call to");
                        for (FunctionSymbol f = m_called; f != null; f = f.nextpoly())
                        {
                            errmsg += reportFunction(f);
                        }
                        caller.errorfmt(errmsg);
                        return (null, new TypeSpec());

                    case 1:  // Success
                        return bestOf(m_candidates[0]);

                    default:
                        break;
                }

                // More than one candidate: pick by return type, which is the only tiebreaker left
                int ambiguity = -1;
                Candidate bestc = null;
                int bestscore = -1;
                foreach (Candidate candidate in m_candidates)
                {
                    if (candidate.rscore > bestscore)
                    {
                        ambiguity = -1;  // higher score, no longer ambiguous
                        bestc = candidate;
                        bestscore = candidate.rscore;
                    }
                    else if (candidate.rscore == bestscore)
                    {
                        ambiguity = candidate.rscore;
                    }
                }

                if (ambiguity != -1)
                {
                    // Ambiguous: rank the candidates by return type and take the first
                    int userstructs = 0;

                    Func<TypeSpec, int> rank = (TypeSpec s) =>
                    {
                        TypeDesc td = s.simpletype();
                        if (td == TypeFloat) return 0;
                        if (td == TypeInt) return 1;
                        if (td == TypeColor) return 2;
                        if (td == TypeVector) return 3;
                        if (td == TypePoint) return 4;
                        if (td == TypeNormal) return 5;
                        if (td == TypeMatrix) return 6;
                        if (td == TypeString) return 7;

                        if (s.is_color_closure()) return 8;
                        if (s.is_structure_based())
                        {
                            ++userstructs;
                            return 9;
                        }
                        if (s.is_void()) return 10;

                        return int.MaxValue;
                    };

                    m_candidates = m_candidates.OrderBy(c => rank(c.rtype)).ToList();  // stable
                    bestc = m_candidates[0];
                    bestscore = m_candidates[0].rscore;

                    // If there are two or more user struct returns, the ambiguity is an error
                    if (userstructs != 0)
                    {
                        userstructs = 0;
                        for (int i = m_candidates.Count - 1; i >= 0; --i)
                        {
                            if (m_candidates[i].rtype.is_structure_based() && ++userstructs > 1)
                            {
                                break;
                            }
                        }
                    }

                    bool warn = userstructs < 2;

                    // If any initializer lists were adjusted differently by the candidates it is an error as well
                    if (warn && m_had_initlist)
                    {
                        List<(ASTcompound_initializer, ASTcompound_initializer.TypeAdjuster)> first = null;
                        foreach (Candidate candidate in m_candidates)
                        {
                            if (first == null)
                            {
                                first = candidate.bindings;
                                continue;
                            }
                            for (int k = 0; k < first.Count; ++k)
                            {
                                if (first[k].Item2.type() != candidate.bindings[k].Item2.type())
                                {
                                    warn = false;
                                    break;
                                }
                            }
                        }
                    }

                    errmsg = reportAmbiguity(funcname, !warn /* "Candidates are" msg*/, "Ambiguous call to");
                    if (warn)
                    {
                        errmsg += "  Chosen function is:\n" + reportFunction(bestc.sym);
                        errmsg += "  Other candidates are:\n";
                        foreach (Candidate candidate in m_candidates)
                        {
                            if (candidate.sym != bestc.sym)
                            {
                                errmsg += reportFunction(candidate.sym);
                            }
                        }
                        caller.warningfmt(errmsg);
                    }
                    else
                    {
                        foreach (Candidate candidate in m_candidates)
                        {
                            errmsg += reportFunction(candidate.sym);
                        }
                        caller.errorfmt(errmsg);
                    }
                }

                return bestOf(bestc);
            }

            public bool empty() { return m_candidates.Count == 0; }
            public bool hadinitlist() { return m_had_initlist; }
        }

        public partial class OSLCompilerImpl
        {
            private static readonly string[] builtin_func_args =
            {
                "area", "fp", "!deriv", null,
                "arraylength", "i?[]", null,
                "bump", "xf", "xsf", "xv", "!deriv", null,
                "calculatenormal", "vp", "!deriv", null,
                "cellnoise", "ff", "fff", "fp", "fpf", "cf", "cff", "cp", "cpf", "vf", "vff", "vp", "vpf", null,
                "concat", "sss", null,   // FIXME -- further checking
                "dict_find", "iss", "iis", null,
                "dict_next", "ii", null,
                "dict_value", "iis?", "!rw", null,
                "Dx", "ff", "vp", "vv", "vn", "cc", "!deriv", null,
                "Dy", "ff", "vp", "vv", "vn", "cc", "!deriv", null,
                "Dz", "ff", "vp", "vv", "vn", "cc", "!deriv", null,
                "displace", "xf", "xsf", "xv", "!deriv", null,
                "environment", "fsv.", "fsvvv.", "csv.", "csvvv.", "vsv.", "vsvvv.", "!tex", "!rw", "!deriv", null,
                "error", "xs*", "!printf", null,
                "exit", "x", null,
                "filterwidth", "ff", "vp", "vv", "!deriv", null,
                "format", "ss*", "!printf", null,
                "fprintf", "xss*", "!printf", null,
                "getattribute", "is?", "is?[]", "iss?", "iss?[]", "isi?", "isi?[]", "issi?", "issi?[]", "!rw", null,   // FIXME -- further checking?
                "getmessage", "is?", "is?[]", "iss?", "iss?[]", "!rw", null,
                "gettextureinfo", "iss?", "iss?[]", "isffs?", "isffs?[]", "!rw", null,   // FIXME -- further checking?
                "hashnoise", "ff", "fff", "fp", "fpf", "cf", "cff", "cp", "cpf", "vf", "vff", "vp", "vpf", null,
                "isconnected", "i?", null,
                "isconstant", "i?", null,
                "noise", "fsf.", "fsff.", "fsp.", "fspf.", "csf.", "csff.", "csp.", "cspf.", "vsf.", "vsff.", "vsp.", "vspf.",
                         "ff", "fff", "fp", "fpf", "cf", "cff", "cp", "cpf", "vf", "vff", "vp", "vpf", "!deriv", null,
                "pnoise", "fsff.", "fsffff.", "fspp.", "fspfpf.", "csff.", "csffff.", "cspp.", "cspfpf.", "vsff.", "vsffff.", "vspp.", "vspfpf.",
                          "fff", "fffff", "fpp", "fpfpf", "cff", "cffff", "cpp", "cpfpf", "vff", "vffff", "vpp", "vpfpf", "!deriv", null,
                "pointcloud_search", "ispfi.", "ispfii.", "!rw", null,
                "pointcloud_get", "isi[]is?[]", "!rw", null,
                "pointcloud_write", "isp.", null,
                "printf", "xs*", "!printf", null,
                "psnoise", "fff", "fffff", "fpp", "fpfpf", "cff", "cffff", "cpp", "cpfpf", "vff", "vffff", "vpp", "vpfpf", null,
                "random", "f", "c", "p", "v", "n", null,
                "regex_match", "iss", "isi[]s", "!rw", null,
                "regex_search", "iss", "isi[]s", "!rw", null,
                "setmessage", "xs?", "xs?[]", null,
                "sincos", "xfff", "xccc", "xppp", "xvvv", "xnnn", "!rw", null,
                "snoise", "ff", "fff", "fp", "fpf", "cf", "cff", "cp", "cpf", "vf", "vff", "vp", "vpf", null,
                "spline", "fsff[]", "csfc[]", "psfp[]", "vsfv[]", "nsfn[]", "fsfif[]", "csfic[]", "psfip[]", "vsfiv[]", "nsfin[]", null,
                "splineinverse", "fsff[]", "fsfif[]", null,
                "split", "iss[]si", "iss[]s", "iss[]", "!rw", null,
                "surfacearea", "f", null,
                "texture", "fsff.", "fsffffff.", "csff.", "csffffff.", "vsff.", "vsffffff.", "!tex", "!rw", "!deriv", null,
                "texture3d", "fsp.", "fspvvv.", "csp.", "cspvvv.", "vsp.", "vspvvv.", "!tex", "!rw", "!deriv", null,
                "trace", "ipv.", "!deriv", null,
                "warning", "xs*", "!printf", null,   // FIXME -- further checking
                null
            };

            private void initialize_builtin_funcs()
            {
                for (int i = 0; builtin_func_args[i] != null; ++i)
                {
                    ustring funcname = new ustring(builtin_func_args[i++]);
                    int npoly = 0;
                    bool readwrite_special_case = false;
                    bool texture_args = false;
                    bool printf_args = false;
                    bool takes_derivs = false;
                    // Count the polymorphic variants, and note any special hints
                    for (npoly = 0; builtin_func_args[i + npoly] != null; ++npoly)
                    {
                        if (builtin_func_args[i + npoly] == "!rw")
                        {
                            readwrite_special_case = true;
                        }
                        else if (builtin_func_args[i + npoly] == "!tex")
                        {
                            texture_args = true;
                        }
                        else if (builtin_func_args[i + npoly] == "!printf")
                        {
                            printf_args = true;
                        }
                        else if (builtin_func_args[i + npoly] == "!deriv")
                        {
                            takes_derivs = true;
                        }
                    }
                    for (int j = npoly - 1; j >= 0; --j)
                    {
                        if (builtin_func_args[i + j][0] == '!')  // Skip special hints
                        {
                            continue;
                        }
                        ustring poly = new ustring(builtin_func_args[i + j]);
                        Symbol last = symtab().clash(funcname);
                        TypeSpec rettype = TypeSpec.type_from_code(poly.@string());
                        FunctionSymbol f = new FunctionSymbol(funcname, rettype);
                        f.nextpoly(last as FunctionSymbol);
                        f.argcodes(poly);
                        f.readwrite_special_case(readwrite_special_case);
                        f.texture_args(texture_args);
                        f.printf_args(printf_args);
                        f.takes_derivs(takes_derivs);
                        symtab().insert(f);
                    }
                    i += npoly;
                }
            }
        }
    }
}
