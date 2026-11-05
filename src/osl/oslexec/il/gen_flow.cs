// OSL port: llvm_gen.cpp (part 2) -- generic shadeop calls, sincos, logic, control flow (if, loops, loop modifiers, function calls, return),
// simple shader-global fields, spline and the other shadeops whose generators are a plain call to the runtime.

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using OSL;

namespace OSL.pvt
{
    public static unsafe partial class ILGen
    {
        private static readonly ustring op_dowhile_u = new ustring("dowhile");

        public static bool il_gen_generic(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol[] args = new Symbol[op.nargs()];
            Symbol Result = rop.opargsym(op, 0);
            bool any_deriv_args = false;
            for (int i = 0; i < op.nargs(); ++i)
            {
                Symbol s = rop.opargsym(op, i);
                args[i] = s;
                any_deriv_args |= (i > 0 && s.has_derivs() && !s.typespec().is_matrix());
            }
            if (any_deriv_args)
            {
                if (op.opname() == op_logb || op.opname() == op_floor || op.opname() == op_ceil || op.opname() == op_round || op.opname() == op_step
                    || op.opname() == op_trunc || op.opname() == op_sign)
                {
                    any_deriv_args = false;
                }
            }
            string name = "osl_" + op.opname().@string() + "_";
            for (int i = 0; i < op.nargs(); ++i)
            {
                Symbol s = rop.opargsym(op, i);
                bool derivs = any_deriv_args && Result.has_derivs() && s.has_derivs();
                name += s.arg_typecode(derivs);
            }
            if (!Result.has_derivs() || !any_deriv_args)
            {
                if (Result.typespec().aggregate() == TypeDesc.SCALAR)
                {
                    Symbol[] rest = new Symbol[op.nargs() - 1];
                    Array.Copy(args, 1, rest, 0, rest.Length);
                    ILValue r = rop.il_call_function(name, rest);
                    rop.il_store_value(r, Result);
                }
                else
                {
                    rop.il_call_function(name, args);
                }
                rop.il_zero_derivs(Result);
            }
            else
            {
                rop.il_call_function(name, args, true);
            }
            return true;
        }

        public static bool il_gen_sincos(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Theta = rop.opargsym(op, 0);
            Symbol Sin_out = rop.opargsym(op, 1);
            Symbol Cos_out = rop.opargsym(op, 2);
            bool theta_deriv = Theta.has_derivs();
            bool result_derivs = (Sin_out.has_derivs() || Cos_out.has_derivs());
            string name = "osl_sincos_";
            for (int i = 0; i < op.nargs(); ++i)
            {
                Symbol s = rop.opargsym(op, i);
                if (s.has_derivs() && result_derivs && theta_deriv)
                {
                    name += "d";
                }
                if (s.typespec().is_float())
                {
                    name += "f";
                }
                else if (s.typespec().is_triple())
                {
                    name += "v";
                }
                else
                {
                    throw new ILGenException("sincos: bad argument type");
                }
            }
            ILValue[] valargs = new ILValue[]
            {
                (theta_deriv && result_derivs) || Theta.typespec().is_triple() ? rop.il_void_ptr(Theta) : rop.il_load_value(Theta),
                rop.il_void_ptr(Sin_out), rop.il_void_ptr(Cos_out)
            };
            rop.ll.call_function(name, valargs);
            if (Sin_out.has_derivs() && !theta_deriv)
            {
                rop.il_zero_derivs(Sin_out);
            }
            if (Cos_out.has_derivs() && !theta_deriv)
            {
                rop.il_zero_derivs(Cos_out);
            }
            return true;
        }

        public static bool il_gen_andor(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol result = rop.opargsym(op, 0);
            Symbol a = rop.opargsym(op, 1);
            Symbol b = rop.opargsym(op, 2);
            ILValue i1_res = null;
            ILValue a_val = rop.il_load_value(a, 0, 0, TypeDescs.TypeInt);
            ILValue b_val = rop.il_load_value(b, 0, 0, TypeDescs.TypeInt);
            if (op.opname() == op_and)
            {
                ILValue b_ne_0 = rop.ll.op_ne(b_val, rop.ll.constant(0));
                ILValue a_ne_0 = rop.ll.op_ne(a_val, rop.ll.constant(0));
                ILValue both_ne_0 = rop.ll.op_and(b_ne_0, a_ne_0);
                i1_res = both_ne_0;
            }
            else
            {
                ILValue or_ab = rop.ll.op_or(a_val, b_val);
                ILValue or_ab_ne_0 = rop.ll.op_ne(or_ab, rop.ll.constant(0));
                i1_res = or_ab_ne_0;
            }
            ILValue i32_res = rop.ll.op_bool_to_int(i1_res);
            rop.il_store_value(i32_res, result, 0, 0);
            return true;
        }

        public static bool il_gen_if(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol cond = rop.opargsym(op, 0);
            ILValue cond_val = rop.il_test_nonzero(cond);
            ILLabel then_block = rop.ll.new_basic_block("then");
            ILLabel else_block = rop.ll.new_basic_block("else");
            ILLabel after_block = rop.ll.new_basic_block("");
            rop.ll.op_branch(cond_val, then_block, else_block);
            rop.build_il_code(opnum + 1, op.jump(0), then_block);
            rop.ll.op_branch(after_block);
            rop.build_il_code(op.jump(0), op.jump(1), else_block);
            rop.ll.op_branch(after_block);
            return true;
        }

        public static bool il_gen_loop_op(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol cond = rop.opargsym(op, 0);
            ILLabel cond_block = rop.ll.new_basic_block("cond");
            ILLabel body_block = rop.ll.new_basic_block("body");
            ILLabel step_block = rop.ll.new_basic_block("step");
            ILLabel after_block = rop.ll.new_basic_block("");
            rop.ll.push_loop(step_block, after_block);
            rop.build_il_code(opnum + 1, op.jump(0));
            rop.ll.op_branch(op.opname() == op_dowhile ? body_block : cond_block);
            rop.build_il_code(op.jump(0), op.jump(1), cond_block);
            ILValue cond_val = rop.il_test_nonzero(cond);
            rop.ll.op_branch(cond_val, body_block, after_block);
            rop.build_il_code(op.jump(1), op.jump(2), body_block);
            rop.ll.op_branch(step_block);
            rop.build_il_code(op.jump(2), op.jump(3), step_block);
            rop.ll.op_branch(cond_block);
            rop.ll.set_insert_point(after_block);
            rop.ll.pop_loop();
            return true;
        }

        public static bool il_gen_loopmod_op(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            if (op.opname() == op_break)
            {
                rop.ll.op_branch(rop.ll.loop_after_block());
            }
            else
            {
                rop.ll.op_branch(rop.ll.loop_step_block());
            }
            ILLabel next_block = rop.ll.new_basic_block("");
            rop.ll.set_insert_point(next_block);
            return true;
        }

        public static bool il_gen_functioncall(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            ILLabel after_block = rop.ll.push_function();
            int op_num_function_starts_at = opnum + 1;
            int op_num_function_ends_at = op.jump(0);
            rop.build_il_code(op_num_function_starts_at, op_num_function_ends_at);
            rop.ll.op_branch(after_block);
            rop.ll.pop_function();
            return true;
        }

        public static bool il_gen_functioncall_nr(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            int op_num_function_starts_at = opnum + 1;
            int op_num_function_ends_at = op.jump(0);
            rop.build_il_code(op_num_function_starts_at, op_num_function_ends_at);
            return true;
        }

        public static bool il_gen_return(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            if (op.opname() == Strings.op_exit)
            {
                rop.ll.op_branch(rop.il_exit_instance_block());
            }
            else
            {
                rop.ll.op_branch(rop.ll.return_block());
            }
            ILLabel next_block = rop.ll.new_basic_block("");
            rop.ll.set_insert_point(next_block);
            return true;
        }

        public static bool il_gen_end(BackendIL rop, int opnum)
        {
            return false;
        }

        public static bool il_gen_isconstant(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol A = rop.opargsym(op, 1);
            rop.il_store_value(rop.ll.constant(A.is_constant() ? 1 : 0), Result);
            return true;
        }

        public static bool il_gen_get_simple_SG_field(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            ILValue sg_field = rop.il_global_symbol_ptr(op.opname());
            ILValue r = rop.ll.op_load(BackendIL.il_scalar_type(Result.typespec().simpletype()), sg_field);
            rop.il_store_value(r, Result);
            return true;
        }

        public static bool il_gen_calculatenormal(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol P = rop.opargsym(op, 1);
            if (!P.has_derivs())
            {
                rop.il_assign_zero(Result);
                return true;
            }
            ILValue[] args = new ILValue[] { rop.il_void_ptr(Result), rop.sg_void_ptr(), rop.il_void_ptr(P) };
            rop.ll.call_function("osl_calculatenormal", args);
            if (Result.has_derivs())
            {
                rop.il_zero_derivs(Result);
            }
            return true;
        }

        public static bool il_gen_area(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol P = rop.opargsym(op, 1);
            if (!P.has_derivs())
            {
                rop.il_assign_zero(Result);
                return true;
            }
            ILValue r = rop.ll.call_function("osl_area", rop.il_void_ptr(P));
            rop.il_store_value(r, Result);
            if (Result.has_derivs())
            {
                rop.il_zero_derivs(Result);
            }
            return true;
        }

        public static bool il_gen_spline(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            bool has_knot_count = (op.nargs() == 5);
            Symbol Result = rop.opargsym(op, 0);
            Symbol Spline = rop.opargsym(op, 1);
            Symbol Value = rop.opargsym(op, 2);
            Symbol Knot_count = rop.opargsym(op, 3);
            Symbol Knots = has_knot_count ? rop.opargsym(op, 4) : rop.opargsym(op, 3);
            string name = "osl_" + op.opname().@string() + "_";
            bool result_derivs = Result.has_derivs() && (Value.has_derivs() || Knots.has_derivs());
            if (result_derivs)
            {
                name += "d";
            }
            if (Result.typespec().is_float())
            {
                name += "f";
            }
            else if (Result.typespec().is_triple())
            {
                name += "v";
            }
            if (result_derivs && Value.has_derivs())
            {
                name += "d";
            }
            if (Value.typespec().is_float())
            {
                name += "f";
            }
            else if (Value.typespec().is_triple())
            {
                name += "v";
            }
            if (result_derivs && Knots.has_derivs())
            {
                name += "d";
            }
            if (Knots.typespec().simpletype().elementtype().basetype == TypeDesc.FLOAT && Knots.typespec().simpletype().elementtype().aggregate == TypeDesc.SCALAR)
            {
                name += "f";
            }
            else if (Knots.typespec().simpletype().elementtype().aggregate == TypeDesc.VEC3)
            {
                name += "v";
            }
            ILValue[] args = new ILValue[]
            {
                rop.il_void_ptr(Result), rop.il_load_value(Spline), rop.il_void_ptr(Value), rop.il_void_ptr(Knots),
                has_knot_count ? rop.il_load_value(Knot_count) : rop.ll.constant(Knots.typespec().arraylength()),
                rop.ll.constant(Knots.typespec().arraylength())
            };
            rop.ll.call_function(name, args);
            if (Result.has_derivs() && !result_derivs)
            {
                rop.il_zero_derivs(Result);
            }
            return true;
        }

        public static bool il_gen_luminance(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol C = rop.opargsym(op, 1);
            bool deriv = C.has_derivs() && Result.has_derivs();
            ILValue[] args = new ILValue[] { rop.sg_void_ptr(), rop.il_void_ptr(Result), rop.il_void_ptr(C) };
            rop.ll.call_function(deriv ? "osl_luminance_dfdv" : "osl_luminance_fv", args);
            if (Result.has_derivs() && !C.has_derivs())
            {
                rop.il_zero_derivs(Result);
            }
            return true;
        }

        public static bool il_gen_blackbody(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Temperature = rop.opargsym(op, 1);
            ILValue[] args = new ILValue[] { rop.sg_void_ptr(), rop.il_void_ptr(Result), rop.il_load_value(Temperature) };
            rop.ll.call_function("osl_" + op.opname().@string() + "_vf", args);
            if (Result.has_derivs())
            {
                rop.il_zero_derivs(Result);
            }
            return true;
        }

        public static bool il_gen_split(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol R = rop.opargsym(op, 0);
            Symbol Str = rop.opargsym(op, 1);
            Symbol Results = rop.opargsym(op, 2);
            ILValue[] args = new ILValue[5];
            args[0] = rop.il_load_value(Str);
            args[1] = rop.il_void_ptr(Results);
            if (op.nargs() >= 4)
            {
                Symbol Sep = rop.opargsym(op, 3);
                args[2] = rop.il_load_value(Sep);
            }
            else
            {
                args[2] = rop.ll.constant(new ustring(""));
            }
            if (op.nargs() >= 5)
            {
                Symbol Maxsplit = rop.opargsym(op, 4);
                args[3] = rop.il_load_value(Maxsplit);
            }
            else
            {
                args[3] = rop.ll.constant(Results.typespec().arraylength());
            }
            args[4] = rop.ll.constant(Results.typespec().arraylength());
            ILValue ret = rop.ll.call_function("osl_split", args);
            rop.il_store_value(ret, R);
            return true;
        }

        public static bool il_gen_raytype(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Name = rop.opargsym(op, 1);
            ILValue[] args;
            string funcname;
            if (Name.is_constant())
            {
                args = new ILValue[] { rop.sg_void_ptr(), rop.ll.constant(rop.shadingsys().raytype_bit(Name.get_string())) };
                funcname = "osl_raytype_bit";
            }
            else
            {
                args = new ILValue[] { rop.sg_void_ptr(), rop.il_load_value(Name) };
                funcname = "osl_raytype_name";
            }
            ILValue ret = rop.ll.call_function(funcname, args);
            rop.il_store_value(ret, Result);
            return true;
        }
    }
}
