// OSL port: llvm_gen.cpp (part 1) -- the op generators: printf, arithmetic, clamp/mix/select/minmax, bitwise, assign, component/array access,
// color/triple/matrix construction, transforms, derivatives, comparisons, regex.
//
// Each generator is the llvm_gen_<name> of the C++ with the same body; LLVMGEN(name) is "bool il_gen_<name>(BackendIL rop, int opnum)". The generators
// are registered by their op table names in ILGen.register_all (OpILGenTable.generators).

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using OSL;

namespace OSL.pvt
{
    public static unsafe partial class ILGen
    {
        private static readonly ustring op_and = new ustring("and");
        private static readonly ustring op_bitand = new ustring("bitand");
        private static readonly ustring op_bitor = new ustring("bitor");
        private static readonly ustring op_break = new ustring("break");
        private static readonly ustring op_ceil = new ustring("ceil");
        private static readonly ustring op_cellnoise = new ustring("cellnoise");
        private static readonly ustring op_color = new ustring("color");
        private static readonly ustring op_compl = new ustring("compl");
        private static readonly ustring op_continue = new ustring("continue");
        private static readonly ustring op_dowhile = new ustring("dowhile");
        private static readonly ustring op_eq = new ustring("eq");
        private static readonly ustring op_error = new ustring("error");
        private static readonly ustring op_fabs = new ustring("fabs");
        private static readonly ustring op_floor = new ustring("floor");
        private static readonly ustring op_for = new ustring("for");
        private static readonly ustring op_format = new ustring("format");
        private static readonly ustring op_fprintf = new ustring("fprintf");
        private static readonly ustring op_ge = new ustring("ge");
        private static readonly ustring op_gt = new ustring("gt");
        private static readonly ustring op_hashnoise = new ustring("hashnoise");
        private static readonly ustring op_if = new ustring("if");
        private static readonly ustring op_le = new ustring("le");
        private static readonly ustring op_logb = new ustring("logb");
        private static readonly ustring op_lt = new ustring("lt");
        private static readonly ustring op_min = new ustring("min");
        private static readonly ustring op_neq = new ustring("neq");
        private static readonly ustring op_normal = new ustring("normal");
        private static readonly ustring op_or = new ustring("or");
        private static readonly ustring op_point = new ustring("point");
        private static readonly ustring op_printf = new ustring("printf");
        private static readonly ustring op_round = new ustring("round");
        private static readonly ustring op_shl = new ustring("shl");
        private static readonly ustring op_shr = new ustring("shr");
        private static readonly ustring op_sign = new ustring("sign");
        private static readonly ustring op_step = new ustring("step");
        private static readonly ustring op_trunc = new ustring("trunc");
        private static readonly ustring op_vector = new ustring("vector");
        private static readonly ustring op_warning = new ustring("warning");
        private static readonly ustring op_xor = new ustring("xor");
        private static readonly ustring u_distance = new ustring("distance");
        private static readonly ustring u_index = new ustring("index");

        public static uint size_of_encoded_type(EncodedType et)
        {
            switch (et)
            {
                case EncodedType.kUstringHash: return 8;
                case EncodedType.kInt32: return 4;
                case EncodedType.kFloat: return 4;
                case EncodedType.kInt64: return 8;
                case EncodedType.kDouble: return 8;
                case EncodedType.kUInt32: return 4;
                case EncodedType.kUInt64: return 8;
                case EncodedType.kPointer: return 8;
                default: return 8;
            }
        }

        public static bool il_gen_nop(BackendIL rop, int opnum)
        {
            return true;
        }

        public static bool il_gen_useparam(BackendIL rop, int opnum)
        {
            HashSet<int> already_run = new HashSet<int>();
            Opcode op = rop.inst().ops()[opnum];
            for (int i = 0; i < op.nargs(); ++i)
            {
                Symbol sym = rop.opargsym(op, i);
                int symindex = rop.inst().arg(op.firstarg() + i);
                rop.il_run_connected_layers(sym, symindex, opnum, already_run);
                // If it's an interpolated (userdata) parameter and we're initializing them lazily, now we have to do it.
                if ((sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam) && sym.interpolated()
                    && !sym.typespec().is_closure() && !sym.connected() && !sym.connected_down() && rop.shadingsys().lazy_userdata())
                {
                    rop.il_assign_initial_value(sym);
                }
            }
            rop.increment_useparam_ops();
            return true;
        }

        /// printf, error, warning, fprintf and format: the format string is turned into a {fmt} specification and the arguments are packed into a
        /// buffer with a type code per argument (llvm_gen_print_fmt)
        public static bool il_gen_printf(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            int format_arg = (op.opname() == op_format || op.opname() == op_fprintf) ? 1 : 0;
            Symbol format_sym = rop.opargsym(op, format_arg);
            List<ILValue> call_args = new List<ILValue>();
            if (!format_sym.is_constant())
            {
                rop.shadingcontext().warningfmt("{} must currently have constant format\n", op.opname());
                return false;
            }
            call_args.Add(rop.sg_void_ptr());
            if (op.opname() == op_fprintf)
            {
                Symbol Filename = rop.opargsym(op, 0);
                ustring filename_ustring = Filename.get_string();
                call_args.Add(rop.il_const_hash(filename_ustring));
            }
            ustring format_ustring = format_sym.get_string();
            string format = format_ustring.@string();
            int fpos = 0;
            StringBuilder s = new StringBuilder();
            int arg = format_arg + 1;
            List<EncodedType> encodedtypes = new List<EncodedType>();
            int arg_values_size = 0;
            List<ILValue> loaded_arg_values = new List<ILValue>();
            while (fpos < format.Length)
            {
                if (format[fpos] == '%')
                {
                    if (fpos + 1 < format.Length && format[fpos + 1] == '%')
                    {
                        s.Append('%');
                        fpos += 2;
                        continue;
                    }
                    int oldfmt = fpos;
                    while (fpos < format.Length && "cdefgimnopsuvxX".IndexOf(format[fpos]) < 0)
                    {
                        ++fpos;
                    }
                    char formatchar = fpos < format.Length ? format[fpos] : '\0';
                    if (fpos < format.Length)
                    {
                        ++fpos;
                    }
                    if (arg >= op.nargs())
                    {
                        rop.shadingcontext().errorfmt("Mismatch between format string and arguments ({}:{})", op.sourcefile(), op.sourceline());
                        return false;
                    }
                    StringBuilder ourformat = new StringBuilder(format.Substring(oldfmt, fpos - oldfmt));
                    int pos_of_minus = ourformat.ToString().IndexOf('-');
                    if (pos_of_minus >= 0)
                    {
                        ourformat[pos_of_minus] = '<';
                        pos_of_minus = ourformat.ToString().IndexOf('-');
                        while (pos_of_minus >= 0)
                        {
                            ourformat.Remove(pos_of_minus, 1);
                            pos_of_minus = ourformat.ToString().IndexOf('-');
                        }
                    }
                    Symbol sym = rop.opargsym(op, arg);
                    TypeDesc simpletype = sym.typespec().simpletype();
                    int num_elements = simpletype.numelements();
                    int num_components = simpletype.aggregate;
                    if ((sym.typespec().is_closure_based() || simpletype.basetype == TypeDesc.STRING) && formatchar != 's')
                    {
                        ourformat[ourformat.Length - 1] = 's';
                    }
                    if (simpletype.basetype == TypeDesc.INT && formatchar != 'd' && formatchar != 'o' && formatchar != 'u' && formatchar != 'x'
                        && formatchar != 'X')
                    {
                        ourformat[ourformat.Length - 1] = 'd';
                    }
                    if (simpletype.basetype == TypeDesc.FLOAT && formatchar != 'f' && formatchar != 'g' && formatchar != 'e')
                    {
                        ourformat[ourformat.Length - 1] = 'f';
                    }
                    EncodedType et = EncodedType.kUstringHash;
                    if (simpletype.basetype == TypeDesc.INT)
                    {
                        if (formatchar == 'x' || formatchar == 'X')
                        {
                            et = EncodedType.kUInt32;
                        }
                        else
                        {
                            et = EncodedType.kInt32;
                        }
                    }
                    if (simpletype.basetype == TypeDesc.FLOAT)
                    {
                        et = EncodedType.kFloat;
                    }
                    string myformat = "{:";
                    ourformat.Remove(0, 1);
                    myformat += ourformat.ToString();
                    myformat += "}";
                    TypeDesc symty = sym.typespec().simpletype();
                    TypeDesc basetype = new TypeDesc(symty.basetype);
                    for (int a = 0; a < num_elements; ++a)
                    {
                        ILValue const_arrind = simpletype.arraylen != 0 ? rop.ll.constant(a) : null;
                        if (sym.typespec().is_closure_based())
                        {
                            s.Append(myformat);
                            ILValue v = rop.il_load_value(sym, 0, const_arrind, 0);
                            v = rop.ll.call_function("osl_closure_to_ustringhash", rop.sg_void_ptr(), v);
                            encodedtypes.Add(et);
                            arg_values_size += (int)size_of_encoded_type(et);
                            loaded_arg_values.Add(v);
                            continue;
                        }
                        for (int c = 0; c < num_components; c++)
                        {
                            if (c != 0 || a != 0)
                            {
                                s.Append(' ');
                            }
                            s.Append(myformat);
                            ILValue loaded = sym.is_constant() ? rop.il_load_constant_value(sym, a, c, basetype)
                                                               : rop.il_load_value(sym, 0, const_arrind, c, basetype);
                            encodedtypes.Add(et);
                            arg_values_size += (int)size_of_encoded_type(et);
                            loaded_arg_values.Add(loaded);
                        }
                    }
                    ++arg;
                }
                else
                {
                    char current_char = format[fpos++];
                    s.Append(current_char);
                    if (current_char == '{' || current_char == '}')
                    {
                        s.Append(current_char);
                    }
                }
            }
            string str = s.ToString();
            if (!rop.use_optix_cache())
            {
                if (op.opname() == op_error || op.opname() == op_warning)
                {
                    str = Fmt.format("Shader {} [{}]: {}", op.opname(), rop.inst().shadername(), str);
                }
            }
            ustring s_ustring = new ustring(str);
            call_args.Add(rop.il_const_hash(s_ustring));
            int arg_count = encodedtypes.Count;
            call_args.Add(rop.ll.constant(arg_count));
            ILValue encodedtypes_on_stack = rop.ll.op_alloca(arg_count, 4, "encodedtypes");
            ILValue loaded_arg_values_on_stack = rop.ll.op_alloca(arg_values_size, 8, "argValues");
            int bytesToArg = 0;
            for (int argindex = 0; argindex < arg_count; ++argindex)
            {
                EncodedType et = encodedtypes[argindex];
                rop.ll.op_store8(rop.ll.constant8((int)et), rop.ll.offset_ptr(encodedtypes_on_stack, argindex));
                ILValue loadedArgValue = loaded_arg_values[argindex];
                rop.ll.op_store(loadedArgValue, rop.ll.offset_ptr(loaded_arg_values_on_stack, bytesToArg));
                bytesToArg += (int)size_of_encoded_type(et);
            }
            call_args.Add(encodedtypes_on_stack);
            call_args.Add(rop.ll.constant(arg_values_size));
            call_args.Add(loaded_arg_values_on_stack);
            string rs_func_name = null;
            if (op.opname() == op_printf)
            {
                rs_func_name = "osl_gen_printfmt";
            }
            if (op.opname() == op_error)
            {
                rs_func_name = "osl_gen_errorfmt";
            }
            if (op.opname() == op_warning)
            {
                rs_func_name = "osl_gen_warningfmt";
            }
            if (op.opname() == op_fprintf)
            {
                rs_func_name = "osl_gen_filefmt";
            }
            if (op.opname() == op_format)
            {
                rs_func_name = "osl_formatfmt";
            }
            ILValue ret = rop.ll.call_function(rs_func_name, call_args.ToArray());
            if (op.opname() == op_format)
            {
                rop.il_store_value(ret, rop.opargsym(op, 0));
            }
            return true;
        }

        public static bool il_gen_add(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            if (Result.typespec().is_closure())
            {
                ILValue[] valargs = new ILValue[] { rop.sg_void_ptr(), rop.il_load_value(A), rop.il_load_value(B) };
                ILValue res = rop.ll.call_function("osl_add_closure_closure", valargs);
                rop.il_store_value(res, Result, 0, null, 0);
                return true;
            }
            TypeDesc type = Result.typespec().simpletype();
            int num_components = type.aggregate;
            for (int i = 0; i < num_components; i++)
            {
                ILValue a = rop.loadILValue(A, i, 0, type);
                ILValue b = rop.loadILValue(B, i, 0, type);
                if (a == null || b == null)
                {
                    return false;
                }
                ILValue r = rop.ll.op_add(a, b);
                rop.storeILValue(r, Result, i, 0);
            }
            if (Result.has_derivs())
            {
                if (A.has_derivs() || B.has_derivs())
                {
                    for (int d = 1; d <= 2; ++d)
                    {
                        for (int i = 0; i < num_components; i++)
                        {
                            ILValue a = rop.loadILValue(A, i, d, type);
                            ILValue b = rop.loadILValue(B, i, d, type);
                            ILValue r = rop.ll.op_add(a, b);
                            rop.storeILValue(r, Result, i, d);
                        }
                    }
                }
                else
                {
                    rop.il_zero_derivs(Result);
                }
            }
            return true;
        }

        public static bool il_gen_sub(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            TypeDesc type = Result.typespec().simpletype();
            int num_components = type.aggregate;
            for (int i = 0; i < num_components; i++)
            {
                ILValue a = rop.loadILValue(A, i, 0, type);
                ILValue b = rop.loadILValue(B, i, 0, type);
                if (a == null || b == null)
                {
                    return false;
                }
                ILValue r = rop.ll.op_sub(a, b);
                rop.storeILValue(r, Result, i, 0);
            }
            if (Result.has_derivs())
            {
                if (A.has_derivs() || B.has_derivs())
                {
                    for (int d = 1; d <= 2; ++d)
                    {
                        for (int i = 0; i < num_components; i++)
                        {
                            ILValue a = rop.loadILValue(A, i, d, type);
                            ILValue b = rop.loadILValue(B, i, d, type);
                            ILValue r = rop.ll.op_sub(a, b);
                            rop.storeILValue(r, Result, i, d);
                        }
                    }
                }
                else
                {
                    rop.il_zero_derivs(Result);
                }
            }
            return true;
        }

        public static bool il_gen_mul(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            TypeDesc type = Result.typespec().simpletype();
            int num_components = type.aggregate;
            // multiplication involving closures
            if (Result.typespec().is_closure())
            {
                ILValue[] valargs = new ILValue[3];
                valargs[0] = rop.sg_void_ptr();
                bool tfloat;
                if (A.typespec().is_closure())
                {
                    tfloat = B.typespec().is_float();
                    valargs[1] = rop.il_load_value(A);
                    valargs[2] = tfloat ? rop.il_load_value(B) : rop.il_void_ptr(B);
                }
                else
                {
                    tfloat = A.typespec().is_float();
                    valargs[1] = rop.il_load_value(B);
                    valargs[2] = tfloat ? rop.il_load_value(A) : rop.il_void_ptr(A);
                }
                ILValue res = tfloat ? rop.ll.call_function("osl_mul_closure_float", valargs) : rop.ll.call_function("osl_mul_closure_color", valargs);
                rop.il_store_value(res, Result, 0, null, 0);
                return true;
            }
            // multiplication involving matrices
            if (Result.typespec().is_matrix())
            {
                if (A.typespec().is_float())
                {
                    if (B.typespec().is_matrix())
                    {
                        rop.il_call_function("osl_mul_mmf", Result, B, A);
                    }
                    else
                    {
                        throw new ILGenException("frontend should not allow");
                    }
                }
                else if (A.typespec().is_matrix())
                {
                    if (B.typespec().is_float())
                    {
                        rop.il_call_function("osl_mul_mmf", Result, A, B);
                    }
                    else if (B.typespec().is_matrix())
                    {
                        rop.il_call_function("osl_mul_mmm", Result, A, B);
                    }
                    else
                    {
                        throw new ILGenException("frontend should not allow");
                    }
                }
                else
                {
                    throw new ILGenException("frontend should not allow");
                }
                if (Result.has_derivs())
                {
                    rop.il_zero_derivs(Result);
                }
                return true;
            }
            // multiplication involving ints, floats and triples
            for (int i = 0; i < num_components; i++)
            {
                ILValue a = rop.il_load_value(A, 0, i, type);
                ILValue b = rop.il_load_value(B, 0, i, type);
                if (a == null || b == null)
                {
                    return false;
                }
                ILValue r = rop.ll.op_mul(a, b);
                rop.il_store_value(r, Result, 0, i);
                if (Result.has_derivs() && (A.has_derivs() || B.has_derivs()))
                {
                    // Multiplication of duals: (a*b, a*b.dx + a.dx*b, a*b.dy + a.dy*b)
                    ILValue ax = rop.il_load_value(A, 1, i, type);
                    ILValue bx = rop.il_load_value(B, 1, i, type);
                    ILValue abx = rop.ll.op_mul(a, bx);
                    ILValue axb = rop.ll.op_mul(ax, b);
                    ILValue rx = rop.ll.op_add(abx, axb);
                    ILValue ay = rop.il_load_value(A, 2, i, type);
                    ILValue by = rop.il_load_value(B, 2, i, type);
                    ILValue aby = rop.ll.op_mul(a, by);
                    ILValue ayb = rop.ll.op_mul(ay, b);
                    ILValue ry = rop.ll.op_add(aby, ayb);
                    rop.il_store_value(rx, Result, 1, i);
                    rop.il_store_value(ry, Result, 2, i);
                }
            }
            // Zero out the derivs of the result if they are needed but the operands had none
            if (Result.has_derivs() && !(A.has_derivs() || B.has_derivs()))
            {
                rop.il_zero_derivs(Result);
            }
            return true;
        }

        public static bool il_gen_div(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            TypeDesc type = Result.typespec().simpletype();
            bool is_float = Result.typespec().is_float_based();
            int num_components = type.aggregate;
            // division involving matrices
            if (Result.typespec().is_matrix())
            {
                if (A.typespec().is_float())
                {
                    if (B.typespec().is_matrix())
                    {
                        rop.il_call_function("osl_div_mfm", Result, A, B);
                    }
                    else
                    {
                        throw new ILGenException("frontend should not allow");
                    }
                }
                else if (A.typespec().is_matrix())
                {
                    if (B.typespec().is_float())
                    {
                        rop.il_call_function("osl_div_mmf", Result, A, B);
                    }
                    else if (B.typespec().is_matrix())
                    {
                        rop.il_call_function("osl_div_mmm", Result, A, B);
                    }
                    else
                    {
                        throw new ILGenException("frontend should not allow");
                    }
                }
                else
                {
                    throw new ILGenException("frontend should not allow");
                }
                if (Result.has_derivs())
                {
                    rop.il_zero_derivs(Result);
                }
                return true;
            }
            // Division of float/int/triple
            string safe_div = is_float ? "osl_safe_div_fff" : "osl_safe_div_iii";
            bool deriv = (Result.has_derivs() && (A.has_derivs() || B.has_derivs()));
            for (int i = 0; i < num_components; i++)
            {
                ILValue a = rop.il_load_value(A, 0, i, type);
                ILValue b = rop.il_load_value(B, 0, i, type);
                if (a == null || b == null)
                {
                    return false;
                }
                ILValue a_div_b;
                if (B.is_constant() && !rop.il_is_zero(B))
                {
                    a_div_b = rop.ll.op_div(a, b);
                }
                else
                {
                    a_div_b = rop.ll.call_function(safe_div, a, b);
                }
                ILValue rx = null;
                ILValue ry = null;
                if (deriv)
                {
                    // Division of duals: (a/b, 1/b*(a.dx - a/b*b.dx), 1/b*(a.dy - a/b*b.dy))
                    ILValue binv;
                    if (B.is_constant() && !rop.il_is_zero(B))
                    {
                        binv = rop.ll.op_div(rop.ll.constant(1.0f), b);
                    }
                    else
                    {
                        binv = rop.ll.call_function(safe_div, rop.ll.constant(1.0f), b);
                    }
                    ILValue ax = rop.il_load_value(A, 1, i, type);
                    ILValue bx = rop.il_load_value(B, 1, i, type);
                    ILValue a_div_b_mul_bx = rop.ll.op_mul(a_div_b, bx);
                    ILValue ax_minus_a_div_b_mul_bx = rop.ll.op_sub(ax, a_div_b_mul_bx);
                    rx = rop.ll.op_mul(binv, ax_minus_a_div_b_mul_bx);
                    ILValue ay = rop.il_load_value(A, 2, i, type);
                    ILValue by = rop.il_load_value(B, 2, i, type);
                    ILValue a_div_b_mul_by = rop.ll.op_mul(a_div_b, by);
                    ILValue ay_minus_a_div_b_mul_by = rop.ll.op_sub(ay, a_div_b_mul_by);
                    ry = rop.ll.op_mul(binv, ay_minus_a_div_b_mul_by);
                }
                rop.il_store_value(a_div_b, Result, 0, i);
                if (deriv)
                {
                    rop.il_store_value(rx, Result, 1, i);
                    rop.il_store_value(ry, Result, 2, i);
                }
            }
            if (Result.has_derivs() && !(A.has_derivs() || B.has_derivs()))
            {
                rop.il_zero_derivs(Result);
            }
            return true;
        }

        public static bool il_gen_modulus(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            TypeDesc type = Result.typespec().simpletype();
            bool is_float = Result.typespec().is_float_based();
            int num_components = type.aggregate;
string safe_mod = is_float ? "osl_fmod_fff" : "osl_safe_mod_iii";
            for (int i = 0; i < num_components; i++)
            {
                ILValue a = rop.loadILValue(A, i, 0, type);
                ILValue b = rop.loadILValue(B, i, 0, type);
                if (a == null || b == null)
                {
                    return false;
                }
                ILValue r;
                if (!rop.use_optix() && B.is_constant() && !rop.il_is_zero(B))
                {
                    r = rop.ll.op_mod(a, b);
                }
                else
                {
                    r = rop.ll.call_function(safe_mod, a, b);
                }
                rop.storeILValue(r, Result, i, 0);
            }
            if (Result.has_derivs())
            {
                if (A.has_derivs())
                {
                    for (int d = 1; d <= 2; ++d)
                    {
                        for (int i = 0; i < num_components; i++)
                        {
                            ILValue deriv = rop.loadILValue(A, i, d, type);
                            rop.storeILValue(deriv, Result, i, d);
                        }
                    }
                }
                else
                {
                    rop.il_zero_derivs(Result);
                }
            }
            return true;
        }

        public static bool il_gen_neg(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            TypeDesc type = Result.typespec().simpletype();
            int num_components = type.aggregate;
            for (int d = 0; d < 3; ++d)
            {
                for (int i = 0; i < num_components; i++)
                {
                    ILValue a = rop.il_load_value(A, d, i, type);
                    ILValue r = rop.ll.op_neg(a);
                    rop.il_store_value(r, Result, d, i);
                }
                if (!Result.has_derivs())
                {
                    break;
                }
            }
            return true;
        }

        public static bool il_gen_clamp(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol X = rop.opargsym(op, 1);
            Symbol Min = rop.opargsym(op, 2);
            Symbol Max = rop.opargsym(op, 3);
            TypeDesc type = Result.typespec().simpletype();
            int num_components = type.aggregate;
            for (int i = 0; i < num_components; i++)
            {
                // First do the lower bound clamping
                ILValue val = rop.il_load_value(X, 0, i, type);
                ILValue min = rop.il_load_value(Min, 0, i, type);
                ILValue cond = rop.ll.op_lt(val, min);
                val = rop.ll.op_select(cond, min, val);
                ILValue valdx = null;
                ILValue valdy = null;
                if (Result.has_derivs())
                {
                    valdx = rop.il_load_value(X, 1, i, type);
                    valdy = rop.il_load_value(X, 2, i, type);
                    ILValue mindx = rop.il_load_value(Min, 1, i, type);
                    ILValue mindy = rop.il_load_value(Min, 2, i, type);
                    valdx = rop.ll.op_select(cond, mindx, valdx);
                    valdy = rop.ll.op_select(cond, mindy, valdy);
                }
                // Now do the upper bound clamping
                ILValue max = rop.il_load_value(Max, 0, i, type);
                cond = rop.ll.op_gt(val, max);
                val = rop.ll.op_select(cond, max, val);
                if (Result.has_derivs())
                {
                    ILValue maxdx = rop.il_load_value(Max, 1, i, type);
                    ILValue maxdy = rop.il_load_value(Max, 2, i, type);
                    valdx = rop.ll.op_select(cond, maxdx, valdx);
                    valdy = rop.ll.op_select(cond, maxdy, valdy);
                }
                rop.il_store_value(val, Result, 0, i);
                rop.il_store_value(valdx, Result, 1, i);
                rop.il_store_value(valdy, Result, 2, i);
            }
            return true;
        }

        public static bool il_gen_mix(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            Symbol X = rop.opargsym(op, 3);
            TypeDesc type = Result.typespec().simpletype();
            int num_components = type.aggregate;
            int x_components = X.typespec().aggregate();
            bool derivs = (Result.has_derivs() && (A.has_derivs() || B.has_derivs() || X.has_derivs()));
            ILValue one = rop.ll.constant(1.0f);
            ILValue x = rop.il_load_value(X, 0, 0, type);
            ILValue one_minus_x = rop.ll.op_sub(one, x);
            ILValue xx = derivs ? rop.il_load_value(X, 1, 0, type) : null;
            ILValue xy = derivs ? rop.il_load_value(X, 2, 0, type) : null;
            for (int i = 0; i < num_components; i++)
            {
                ILValue a = rop.il_load_value(A, 0, i, type);
                ILValue b = rop.il_load_value(B, 0, i, type);
                if (a == null || b == null)
                {
                    return false;
                }
                if (i > 0 && x_components > 1)
                {
                    x = rop.il_load_value(X, 0, i, type);
                    one_minus_x = rop.ll.op_sub(one, x);
                }
                ILValue r1 = rop.ll.op_mul(a, one_minus_x);
                ILValue r2 = rop.ll.op_mul(b, x);
                ILValue r = rop.ll.op_add(r1, r2);
                rop.il_store_value(r, Result, 0, i);
                if (derivs)
                {
                    ILValue ax = rop.il_load_value(A, 1, i, type);
                    ILValue bx = rop.il_load_value(B, 1, i, type);
                    if (i > 0 && x_components > 1)
                    {
                        xx = rop.il_load_value(X, 1, i, type);
                    }
                    ILValue rx1 = rop.ll.op_mul(a, xx);
                    ILValue rx2 = rop.ll.op_mul(ax, one_minus_x);
                    ILValue rx = rop.ll.op_sub(rx2, rx1);
                    ILValue rx3 = rop.ll.op_mul(b, xx);
                    rx = rop.ll.op_add(rx, rx3);
                    ILValue rx4 = rop.ll.op_mul(bx, x);
                    rx = rop.ll.op_add(rx, rx4);
                    ILValue ay = rop.il_load_value(A, 2, i, type);
                    ILValue by = rop.il_load_value(B, 2, i, type);
                    if (i > 0 && x_components > 1)
                    {
                        xy = rop.il_load_value(X, 2, i, type);
                    }
                    ILValue ry1 = rop.ll.op_mul(a, xy);
                    ILValue ry2 = rop.ll.op_mul(ay, one_minus_x);
                    ILValue ry = rop.ll.op_sub(ry2, ry1);
                    ILValue ry3 = rop.ll.op_mul(b, xy);
                    ry = rop.ll.op_add(ry, ry3);
                    ILValue ry4 = rop.ll.op_mul(by, x);
                    ry = rop.ll.op_add(ry, ry4);
                    rop.il_store_value(rx, Result, 1, i);
                    rop.il_store_value(ry, Result, 2, i);
                }
            }
            if (Result.has_derivs() && !derivs)
            {
                rop.il_zero_derivs(Result);
            }
            return true;
        }

        public static bool il_gen_select(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            Symbol X = rop.opargsym(op, 3);
            TypeDesc type = Result.typespec().simpletype();
            int num_components = type.aggregate;
            int x_components = X.typespec().aggregate();
            bool derivs = (Result.has_derivs() && (A.has_derivs() || B.has_derivs()));
            ILValue zero = X.typespec().is_int() ? rop.ll.constant(0) : rop.ll.constant(0.0f);
            ILValue[] cond = new ILValue[3];
            for (int i = 0; i < x_components; ++i)
            {
                cond[i] = rop.ll.op_ne(rop.il_load_value(X, 0, i), zero);
            }
            for (int i = 0; i < num_components; i++)
            {
                ILValue a = rop.il_load_value(A, 0, i, type);
                ILValue b = rop.il_load_value(B, 0, i, type);
                ILValue c = (i >= x_components) ? cond[0] : cond[i];
                ILValue r = rop.ll.op_select(c, b, a);
                rop.il_store_value(r, Result, 0, i);
                if (derivs)
                {
                    for (int d = 1; d < 3; ++d)
                    {
                        a = rop.il_load_value(A, d, i, type);
                        b = rop.il_load_value(B, d, i, type);
                        r = rop.ll.op_select(c, b, a);
                        rop.il_store_value(r, Result, d, i);
                    }
                }
            }
            if (Result.has_derivs() && !derivs)
            {
                rop.il_zero_derivs(Result);
            }
            return true;
        }

        public static bool il_gen_minmax(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol x = rop.opargsym(op, 1);
            Symbol y = rop.opargsym(op, 2);
            TypeDesc type = Result.typespec().simpletype();
            int num_components = type.aggregate;
            for (int i = 0; i < num_components; i++)
            {
                // First, get the values
                ILValue x_val = rop.il_load_value(x, 0, i, type);
                ILValue y_val = rop.il_load_value(y, 0, i, type);
                ILValue cond = null;
                if (op.opname() == op_min)
                {
                    cond = rop.ll.op_le(x_val, y_val);
                }
                else
                {
                    cond = rop.ll.op_gt(x_val, y_val);
                }
                ILValue res_val = rop.ll.op_select(cond, x_val, y_val);
                rop.il_store_value(res_val, Result, 0, i);
                if (Result.has_derivs())
                {
                    ILValue x_dx = rop.il_load_value(x, 1, i, type);
                    ILValue x_dy = rop.il_load_value(x, 2, i, type);
                    ILValue y_dx = rop.il_load_value(y, 1, i, type);
                    ILValue y_dy = rop.il_load_value(y, 2, i, type);
                    rop.il_store_value(rop.ll.op_select(cond, x_dx, y_dx), Result, 1, i);
                    rop.il_store_value(rop.ll.op_select(cond, x_dy, y_dy), Result, 2, i);
                }
            }
            return true;
        }

        public static bool il_gen_bitwise_binary_op(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            ILValue a = rop.loadILValue(A);
            ILValue b = rop.loadILValue(B);
            if (a == null || b == null)
            {
                return false;
            }
            ILValue r = null;
            if (op.opname() == op_bitand)
            {
                r = rop.ll.op_and(a, b);
            }
            else if (op.opname() == op_bitor)
            {
                r = rop.ll.op_or(a, b);
            }
            else if (op.opname() == op_xor)
            {
                r = rop.ll.op_xor(a, b);
            }
            else if (op.opname() == op_shl)
            {
                r = rop.ll.op_shl(a, b);
            }
            else if (op.opname() == op_shr)
            {
                r = rop.ll.op_shr(a, b);
            }
            else
            {
                return false;
            }
            rop.storeILValue(r, Result);
            return true;
        }

        public static bool il_gen_unary_op(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol dst = rop.opargsym(op, 0);
            Symbol src = rop.opargsym(op, 1);
            bool dst_derivs = dst.has_derivs();
            int num_components = dst.typespec().simpletype().aggregate;
            bool dst_float = dst.typespec().is_float_based();
            bool src_float = src.typespec().is_float_based();
            for (int i = 0; i < num_components; i++)
            {
                // Get src1/2 component i
                ILValue src_load = rop.loadILValue(src, i, 0);
                if (src_load == null)
                {
                    return false;
                }
                ILValue src_val = src_load;
                // Perform the op
                ILValue result = null;
                ustring opname = op.opname();
                if (opname == op_compl)
                {
                    result = rop.ll.op_not(src_val);
                }
                else
                {
                    rop.shadingcontext().errorfmt("Don't know how to handle op '{}', eliding the store\n", opname);
                }
                // Store the result
                if (result != null)
                {
                    if (dst_float && !src_float)
                    {
                        result = rop.ll.op_int_to_float(result);
                    }
                    else if (!dst_float && src_float)
                    {
                        result = rop.ll.op_float_to_int(result);
                    }
                    rop.storeILValue(result, dst, i, 0);
                }
                if (dst_derivs)
                {
                    rop.shadingcontext().infofmt("punting on derivatives for now\n");
                }
            }
            return true;
        }

        public static bool il_gen_assign(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Src = rop.opargsym(op, 1);
            return rop.il_assign_impl(Result, Src);
        }

        public static bool il_gen_arraycopy(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Src = rop.opargsym(op, 1);
            return rop.il_assign_impl(Result, Src);
        }

        /// The arguments of osl_range_check (the call that clamps an index and reports errors)
        private static ILValue il_range_check(BackendIL rop, Opcode op, ILValue index, int length, ustring symname)
        {
            ILValue[] args = new ILValue[]
            {
                index, rop.ll.constant(length), rop.il_const_hash(symname), rop.sg_void_ptr(), rop.il_const_hash(op.sourcefile()),
                rop.ll.constant(op.sourceline()), rop.il_const_hash(rop.group().name()), rop.ll.constant(rop.layer()),
                rop.il_const_hash(rop.inst().layername()), rop.il_const_hash(rop.inst().shadername())
            };
            return rop.ll.call_function("osl_range_check", args);
        }

        public static bool il_gen_compref(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Val = rop.opargsym(op, 1);
            Symbol Index = rop.opargsym(op, 2);
            ILValue c = rop.il_load_value(Index);
            if (rop.inst().master().range_checking())
            {
                if (!(Index.is_constant() && Index.get_int() >= 0 && Index.get_int() < 3))
                {
                    c = il_range_check(rop, op, c, 3, new ustring(Val.unmangled()));
                }
            }
            for (int d = 0; d < 3; ++d)
            {
                ILValue val = null;
                if (Index.is_constant())
                {
                    int i = Index.get_int();
                    i = Math.Clamp(i, 0, 2);
                    val = rop.il_load_value(Val, d, i);
                }
                else
                {
                    val = rop.il_load_component_value(Val, d, c);
                }
                rop.il_store_value(val, Result, d);
                if (!Result.has_derivs())
                {
                    break;
                }
            }
            return true;
        }

        public static bool il_gen_compassign(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Index = rop.opargsym(op, 1);
            Symbol Val = rop.opargsym(op, 2);
            ILValue c = rop.il_load_value(Index);
            if (rop.inst().master().range_checking())
            {
                if (!(Index.is_constant() && Index.get_int() >= 0 && Index.get_int() < 3))
                {
                    c = il_range_check(rop, op, c, 3, new ustring(Result.unmangled()));
                }
            }
            for (int d = 0; d < 3; ++d)
            {
                ILValue val = rop.il_load_value(Val, d, null, 0, TypeDescs.TypeFloat);
                if (Index.is_constant())
                {
                    int i = Index.get_int();
                    i = Math.Clamp(i, 0, 2);
                    rop.il_store_value(val, Result, d, i);
                }
                else
                {
                    rop.il_store_component_value(val, Result, d, c);
                }
                if (!Result.has_derivs())
                {
                    break;
                }
            }
            return true;
        }

        public static bool il_gen_mxcompref(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol M = rop.opargsym(op, 1);
            Symbol Row = rop.opargsym(op, 2);
            Symbol Col = rop.opargsym(op, 3);
            ILValue row = rop.il_load_value(Row);
            ILValue col = rop.il_load_value(Col);
            if (rop.inst().master().range_checking())
            {
                if (!(Row.is_constant() && Col.is_constant() && Row.get_int() >= 0 && Row.get_int() < 4 && Col.get_int() >= 0 && Col.get_int() < 4))
                {
                    if (!(Row.is_constant() && Row.get_int() >= 0 && Row.get_int() < 4))
                    {
                        row = il_range_check(rop, op, row, 4, M.name());
                    }
                    if (!(Col.is_constant() && Col.get_int() >= 0 && Col.get_int() < 4))
                    {
                        col = il_range_check(rop, op, col, 4, M.name());
                    }
                }
            }
            ILValue val = null;
            if (Row.is_constant() && Col.is_constant())
            {
                int r = Math.Clamp(Row.get_int(), 0, 3);
                int c = Math.Clamp(Col.get_int(), 0, 3);
                int comp = 4 * r + c;
                val = rop.il_load_value(M, 0, comp);
            }
            else
            {
                ILValue comp = rop.ll.op_mul(row, rop.ll.constant(4));
                comp = rop.ll.op_add(comp, col);
                val = rop.il_load_component_value(M, 0, comp);
            }
            rop.il_store_value(val, Result);
            rop.il_zero_derivs(Result);
            return true;
        }

        public static bool il_gen_mxcompassign(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Row = rop.opargsym(op, 1);
            Symbol Col = rop.opargsym(op, 2);
            Symbol Val = rop.opargsym(op, 3);
            ILValue row = rop.il_load_value(Row);
            ILValue col = rop.il_load_value(Col);
            if (rop.inst().master().range_checking())
            {
                if (!(Row.is_constant() && Col.is_constant() && Row.get_int() >= 0 && Row.get_int() < 4 && Col.get_int() >= 0 && Col.get_int() < 4))
                {
                    if (!(Row.is_constant() && Row.get_int() >= 0 && Row.get_int() < 4))
                    {
                        row = il_range_check(rop, op, row, 4, Result.name());
                    }
                    if (!(Col.is_constant() && Col.get_int() >= 0 && Col.get_int() < 4))
                    {
                        col = il_range_check(rop, op, col, 4, Result.name());
                    }
                }
            }
            ILValue val = rop.il_load_value(Val, 0, null, 0, TypeDescs.TypeFloat);
            if (Row.is_constant() && Col.is_constant())
            {
                int r = Math.Clamp(Row.get_int(), 0, 3);
                int c = Math.Clamp(Col.get_int(), 0, 3);
                int comp = 4 * r + c;
                rop.il_store_value(val, Result, 0, comp);
            }
            else
            {
                ILValue comp = rop.ll.op_mul(row, rop.ll.constant(4));
                comp = rop.ll.op_add(comp, col);
                rop.il_store_component_value(val, Result, 0, comp);
            }
            return true;
        }

        public static bool il_gen_arraylength(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            int len = A.typespec().is_unsized_array() ? A.initializers() : A.typespec().arraylength();
            rop.il_store_value(rop.ll.constant(len), Result);
            return true;
        }

        public static bool il_gen_aref(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Src = rop.opargsym(op, 1);
            Symbol Index = rop.opargsym(op, 2);
            ILValue index = rop.loadILValue(Index);
            if (index == null)
            {
                return false;
            }
            if (rop.inst().master().range_checking())
            {
                if (!(Index.is_constant() && Index.get_int() >= 0 && Index.get_int() < Src.typespec().arraylength()))
                {
                    index = il_range_check(rop, op, index, Src.typespec().arraylength(), new ustring(Src.unmangled()));
                }
            }
            int num_components = Src.typespec().simpletype().aggregate;
            for (int d = 0; d <= 2; ++d)
            {
                for (int c = 0; c < num_components; ++c)
                {
                    ILValue val = rop.il_load_value(Src, d, index, c);
                    rop.storeILValue(val, Result, c, d);
                }
                if (!Result.has_derivs())
                {
                    break;
                }
            }
            return true;
        }

        public static bool il_gen_aassign(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Index = rop.opargsym(op, 1);
            Symbol Src = rop.opargsym(op, 2);
            ILValue index = rop.loadILValue(Index);
            if (index == null)
            {
                return false;
            }
            if (rop.inst().master().range_checking())
            {
                if (!(Index.is_constant() && Index.get_int() >= 0 && Index.get_int() < Result.typespec().arraylength()))
                {
                    index = il_range_check(rop, op, index, Result.typespec().arraylength(), new ustring(Result.unmangled()));
                }
            }
            int num_components = Result.typespec().simpletype().aggregate;
            // Allow float <=> int casting
            TypeDesc cast = default(TypeDesc);
            if (num_components == 1 && !Result.typespec().is_closure() && !Src.typespec().is_closure()
                && (Result.typespec().is_int_based() || Result.typespec().is_float_based())
                && (Src.typespec().is_int_based() || Src.typespec().is_float_based()))
            {
                cast = Result.typespec().simpletype();
                cast.arraylen = 0;
            }
            for (int d = 0; d <= 2; ++d)
            {
                for (int c = 0; c < num_components; ++c)
                {
                    ILValue val = rop.loadILValue(Src, c, d, cast);
                    rop.il_store_value(val, Result, d, index, c);
                }
                if (!Result.has_derivs())
                {
                    break;
                }
            }
            return true;
        }

        public static bool il_gen_construct_color(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            bool using_space = (op.nargs() == 5);
            Symbol Space = rop.opargsym(op, 1);
            int dmax = Result.has_derivs() ? 3 : 1;
            for (int d = 0; d < dmax; ++d)
            {
                for (int c = 0; c < 3; ++c)
                {
                    Symbol comp = rop.opargsym(op, c + 1 + (using_space ? 1 : 0));
                    ILValue val = rop.il_load_value(comp, d, null, 0, TypeDescs.TypeFloat);
                    rop.il_store_value(val, Result, d, null, c);
                }
            }
            // N.B. color(space, ...) is handled by the C++ with a call to osl_prepend_color_from
            if (using_space)
            {
                ILValue[] args = new ILValue[] { rop.sg_void_ptr(), rop.il_void_ptr(Result, 0), rop.il_load_value(Space) };
                rop.ll.call_function("osl_prepend_color_from", args);
                if (Result.has_derivs())
                {
                    rop.il_zero_derivs(Result);
                }
            }
            return true;
        }

        public static bool il_gen_construct_triple(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            bool using_space = (op.nargs() == 5);
            Symbol Space = rop.opargsym(op, 1);
            int dmax = Result.has_derivs() ? 3 : 1;
            for (int d = 0; d < dmax; ++d)
            {
                for (int c = 0; c < 3; ++c)
                {
                    Symbol comp = rop.opargsym(op, c + 1 + (using_space ? 1 : 0));
                    ILValue val = rop.il_load_value(comp, d, null, 0, TypeDescs.TypeFloat);
                    rop.il_store_value(val, Result, d, null, c);
                }
            }
            // Do the transformation in-place, if called for
            if (using_space)
            {
                ustring from = new ustring("");
                ustring to = new ustring("");
                if (Space.is_constant())
                {
                    from = Space.get_string();
                    if (from == Strings.common || from == rop.shadingsys().commonspace_synonym())
                    {
                        return true;
                    }
                }
                VECSEMANTICS vectype = VECSEMANTICS.POINT;
                if (op.opname() == op_vector)
                {
                    vectype = VECSEMANTICS.VECTOR;
                }
                else if (op.opname() == op_normal)
                {
                    vectype = VECSEMANTICS.NORMAL;
                }
                ILValue from_arg = rop.il_load_value(Space);
                ILValue to_arg = rop.il_const_hash(Strings.common);
                ILValue[] args = new ILValue[]
                {
                    rop.sg_void_ptr(), rop.il_void_ptr(Result), rop.ll.constant(Result.has_derivs() ? 1 : 0), rop.il_void_ptr(Result),
                    rop.ll.constant(Result.has_derivs() ? 1 : 0), from_arg, to_arg, rop.ll.constant((int)vectype)
                };
                RendererServices rend = rop.shadingsys().renderer();
                if (rend.transform_points(null, from.hash(), to.hash(), 0.0f, null, null, 0, vectype))
                {
                    rop.ll.call_function("osl_transform_triple_nonlinear", args);
                }
                else
                {
                    rop.ll.call_function("osl_transform_triple", args);
                }
            }
            return true;
        }

        public static bool il_gen_matrix(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            int nargs = op.nargs();
            bool using_space = (nargs == 3 || nargs == 18);
            int usp = using_space ? 1 : 0;
            bool using_two_spaces = (nargs == 3 && rop.opargsym(op, 2).typespec().is_string());
            int nfloats = nargs - 1 - usp;
            if (using_two_spaces)
            {
                ILValue[] args = new ILValue[] { rop.sg_void_ptr(), rop.il_void_ptr(Result), rop.il_load_value(rop.opargsym(op, 1)),
                                                 rop.il_load_value(rop.opargsym(op, 2)) };
                rop.ll.call_function("osl_get_from_to_matrix", args);
            }
            else
            {
                if (nfloats == 1)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        ILValue src_val = ((i % 4) == (i / 4)) ? rop.il_load_value(rop.opargsym(op, 1 + usp)) : rop.ll.constant(0.0f);
                        rop.il_store_value(src_val, Result, 0, i);
                    }
                }
                else if (nfloats == 16)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        ILValue src_val = rop.il_load_value(rop.opargsym(op, i + 1 + usp));
                        rop.il_store_value(src_val, Result, 0, i);
                    }
                }
                else
                {
                    throw new ILGenException("bad matrix constructor");
                }
                if (using_space)
                {
                    ILValue[] args = new ILValue[] { rop.sg_void_ptr(), rop.il_void_ptr(Result), rop.il_load_value(rop.opargsym(op, 1)) };
                    rop.ll.call_function("osl_prepend_matrix_from", args);
                }
            }
            if (Result.has_derivs())
            {
                rop.il_zero_derivs(Result);
            }
            return true;
        }

        public static bool il_gen_getmatrix(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol From = rop.opargsym(op, 1);
            Symbol To = rop.opargsym(op, 2);
            Symbol M = rop.opargsym(op, 3);
            ILValue[] args = new ILValue[] { rop.sg_void_ptr(), rop.il_void_ptr(M), rop.il_load_value(From), rop.il_load_value(To) };
            ILValue result = rop.ll.call_function("osl_get_from_to_matrix", args);
            rop.il_store_value(result, Result);
            rop.il_zero_derivs(M);
            return true;
        }

        public static bool il_gen_transform(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            int nargs = op.nargs();
            Symbol Result = rop.opargsym(op, 0);
            Symbol From = (nargs == 3) ? null : rop.opargsym(op, 1);
            Symbol To = rop.opargsym(op, (nargs == 3) ? 1 : 2);
            Symbol P = rop.opargsym(op, (nargs == 3) ? 2 : 3);
            if (To.typespec().is_matrix())
            {
                // Transform by a matrix: handled by the generic shadeop
                il_gen_generic(rop, opnum);
                return true;
            }
            // Named space transformation
            ustring from = new ustring("");
            ustring to = new ustring("");
            if ((From == null || From.is_constant()) && To.is_constant())
            {
                from = From != null ? From.get_string() : Strings.common;
                to = To.get_string();
                ustring syn = rop.shadingsys().commonspace_synonym();
                if (from == syn)
                {
                    from = Strings.common;
                }
                if (to == syn)
                {
                    to = Strings.common;
                }
                if (from == to)
                {
                    if (Result != P)
                    {
                        rop.il_assign_impl(Result, P);
                    }
                    return true;
                }
            }
            VECSEMANTICS vectype = VECSEMANTICS.POINT;
            if (op.opname().@string() == "transformv")
            {
                vectype = VECSEMANTICS.VECTOR;
            }
            else if (op.opname().@string() == "transformn")
            {
                vectype = VECSEMANTICS.NORMAL;
            }
            ILValue[] args = new ILValue[]
            {
                rop.sg_void_ptr(), rop.il_void_ptr(P), rop.ll.constant(P.has_derivs() ? 1 : 0), rop.il_void_ptr(Result),
                rop.ll.constant(Result.has_derivs() ? 1 : 0), From != null ? rop.il_load_value(From) : rop.il_const_hash(Strings.common),
                rop.il_load_value(To), rop.ll.constant((int)vectype)
            };
            RendererServices rend = rop.shadingsys().renderer();
            if (rend.transform_points(null, from.hash(), to.hash(), 0.0f, null, null, 0, vectype))
            {
                rop.ll.call_function("osl_transform_triple_nonlinear", args);
            }
            else
            {
                rop.ll.call_function("osl_transform_triple", args);
            }
            return true;
        }

        public static bool il_gen_transformc(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol From = rop.opargsym(op, 1);
            Symbol To = rop.opargsym(op, 2);
            Symbol C = rop.opargsym(op, 3);
            ILValue[] args = new ILValue[]
            {
                rop.sg_void_ptr(), rop.il_void_ptr(C), rop.ll.constant(C.has_derivs() ? 1 : 0), rop.il_void_ptr(Result),
                rop.ll.constant(Result.has_derivs() ? 1 : 0), rop.il_load_value(From), rop.il_load_value(To)
            };
            rop.ll.call_function("osl_transformc", args);
            return true;
        }

        public static bool il_gen_DxDy(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Src = rop.opargsym(op, 1);
            int deriv = (op.opname().@string() == "Dx") ? 1 : 2;
            for (int i = 0; i < Result.typespec().aggregate(); ++i)
            {
                ILValue src_val = rop.il_load_value(Src, deriv, i);
                rop.storeILValue(src_val, Result, i, 0);
            }
            rop.il_zero_derivs(Result);
            return true;
        }

        public static bool il_gen_Dz(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Src = rop.opargsym(op, 1);
            if (Src == rop.inst().symbol(rop.inst().Psym()))
            {
                // dPdz -- the only Dz we know how to take
                int deriv = 3;
                for (int i = 0; i < Result.typespec().aggregate(); ++i)
                {
                    ILValue src_val = rop.il_load_value(Src, deriv, i);
                    rop.storeILValue(src_val, Result, i, 0);
                }
                rop.il_zero_derivs(Result);
            }
            else
            {
                // Punt, everything else for now returns 0 for Dz
                rop.il_assign_zero(Result);
            }
            return true;
        }

        public static bool il_gen_filterwidth(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Src = rop.opargsym(op, 1);
            if (Src.has_derivs())
            {
                if (Src.typespec().is_float())
                {
                    ILValue r = rop.ll.call_function("osl_filterwidth_fdf", rop.il_void_ptr(Src));
                    rop.il_store_value(r, Result);
                }
                else
                {
                    rop.ll.call_function("osl_filterwidth_vdv", rop.il_void_ptr(Result), rop.il_void_ptr(Src));
                }
                // Don't have 2nd order derivs
                rop.il_zero_derivs(Result);
            }
            else
            {
                // No derivs to be had
                rop.il_assign_zero(Result);
            }
            return true;
        }

        public static bool il_gen_compare_op(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            if (A.typespec().is_closure())
            {
                ILValue a = rop.il_load_value(A);
                ILValue b = rop.ll.void_ptr_null();
                ILValue r = (op.opname() == op_eq) ? rop.ll.op_eq(a, b) : rop.ll.op_ne(a, b);
                r = rop.ll.op_bool_to_int(r);
                rop.il_store_value(r, Result);
                return true;
            }
            int num_components = Math.Max(A.typespec().aggregate(), B.typespec().aggregate());
            bool float_based = A.typespec().is_float_based() || B.typespec().is_float_based();
            TypeDesc cast = new TypeDesc(float_based ? TypeDesc.FLOAT : TypeDesc.UNKNOWN);
            ILValue final_result = null;
            ustring opname = op.opname();
            for (int i = 0; i < num_components; i++)
            {
                // Get A&B component i -- note that these correspond to indices 1&2 of the instruction's arguments
                ILValue a = rop.loadILValue(A, i, 0, cast);
                ILValue b = rop.loadILValue(B, i, 0, cast);
                // Trickery for mixed matrix/scalar comparisons -- compare on-diagonal to the scalar, off-diagonal to zero
                if (A.typespec().is_matrix() && !B.typespec().is_matrix())
                {
                    if ((i / 4) != (i % 4))
                    {
                        b = rop.ll.constant(0.0f);
                    }
                }
                if (!A.typespec().is_matrix() && B.typespec().is_matrix())
                {
                    if ((i / 4) != (i % 4))
                    {
                        a = rop.ll.constant(0.0f);
                    }
                }
                // Perform the op
                ILValue result = null;
                if (opname == op_lt)
                {
                    result = rop.ll.op_lt(a, b);
                }
                else if (opname == op_le)
                {
                    result = rop.ll.op_le(a, b);
                }
                else if (opname == op_eq)
                {
                    result = rop.ll.op_eq(a, b);
                }
                else if (opname == op_ge)
                {
                    result = rop.ll.op_ge(a, b);
                }
                else if (opname == op_gt)
                {
                    result = rop.ll.op_gt(a, b);
                }
                else if (opname == op_neq)
                {
                    result = rop.ll.op_ne(a, b);
                }
                else
                {
                    throw new ILGenException("Comparison error");
                }
                if (final_result != null)
                {
                    // Combine the component bool based on the op
                    if (opname != op_neq)
                    {
                        final_result = rop.ll.op_and(final_result, result);
                    }
                    else
                    {
                        final_result = rop.ll.op_or(final_result, result);
                    }
                }
                else
                {
                    final_result = result;
                }
            }
            // Convert the single bit bool into an int for now
            final_result = rop.ll.op_bool_to_int(final_result);
            rop.storeILValue(final_result, Result, 0, 0);
            return true;
        }

        public static bool il_gen_regex(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            int nargs = op.nargs();
            Symbol Result = rop.opargsym(op, 0);
            Symbol Subject = rop.opargsym(op, 1);
            bool do_match_results = (nargs == 4);
            bool fullmatch = (op.opname().@string() == "regex_match");
            Symbol Match = rop.opargsym(op, 2);
            Symbol Pattern = rop.opargsym(op, 2 + (do_match_results ? 1 : 0));
            ILValue[] call_args = new ILValue[]
            {
                rop.sg_void_ptr(), rop.il_load_value(Subject), rop.il_void_ptr(Match),
                do_match_results ? rop.ll.constant(Match.typespec().arraylength()) : rop.ll.constant(0), rop.il_load_value(Pattern),
                rop.ll.constant(fullmatch ? 1 : 0)
            };
            ILValue ret = rop.ll.call_function("osl_regex_impl", call_args);
            rop.il_store_value(ret, Result);
            return true;
        }
    }
}
