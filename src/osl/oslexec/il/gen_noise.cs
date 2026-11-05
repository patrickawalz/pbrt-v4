// OSL port: llvm_gen.cpp (part 5) -- noise (with its options) and trace (with its options).

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using OSL;

namespace OSL.pvt
{
    public static unsafe partial class ILGen
    {
        private static ILValue il_gen_trace_options(BackendIL rop, int opnum, int first_optional_arg)
        {
            ILValue opt = rop.temp_trace_options_void_ptr();
            rop.ll.call_function("osl_init_trace_options", rop.sg_void_ptr(), opt);
            Opcode op = rop.inst().ops()[opnum];
            for (int a = first_optional_arg; a < op.nargs(); ++a)
            {
                Symbol Name = rop.opargsym(op, a);
                ustring name = Name.get_string();
                ++a;
                Symbol Val = rop.opargsym(op, a);
                TypeDesc valtype = Val.typespec().simpletype();
                ILValue val = rop.il_load_value(Val);
                if (name == Strings.mindist && valtype == TypeDescs.TypeFloat)
                {
                    rop.ll.call_function("osl_trace_set_mindist", opt, val);
                }
                else if (name == Strings.maxdist && valtype == TypeDescs.TypeFloat)
                {
                    rop.ll.call_function("osl_trace_set_maxdist", opt, val);
                }
                else if (name == Strings.shade && valtype == TypeDescs.TypeInt)
                {
                    rop.ll.call_function("osl_trace_set_shade", opt, val);
                }
                else if (name == Strings.traceset && valtype == TypeDescs.TypeString)
                {
                    rop.ll.call_function("osl_trace_set_traceset", opt, val);
                }
                else
                {
                    rop.shadingcontext().errorfmt("Unknown trace() optional argument: \"{}\", <{}> ({}:{})", name, valtype, op.sourcefile(),
                                                  op.sourceline());
                }
            }
            return opt;
        }

        public static bool il_gen_trace(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Pos = rop.opargsym(op, 1);
            Symbol Dir = rop.opargsym(op, 2);
            int first_optional_arg = 3;
            ILValue opt = il_gen_trace_options(rop, opnum, first_optional_arg);
            ILValue[] args = new ILValue[]
            {
                rop.sg_void_ptr(), opt, rop.il_void_ptr(Pos, 0), rop.il_void_ptr(Pos, 1), rop.il_void_ptr(Pos, 2), rop.il_void_ptr(Dir, 0),
                rop.il_void_ptr(Dir, 1), rop.il_void_ptr(Dir, 2)
            };
            ILValue r = rop.ll.call_function("osl_trace", args);
            rop.il_store_value(r, Result);
            rop.inst().has_trace_op(true);
            return true;
        }

        private static ILValue il_gen_noise_options(BackendIL rop, int opnum, int first_optional_arg)
        {
            ILValue opt = rop.temp_noise_options_void_ptr();
            rop.ll.call_function("osl_init_noise_options", rop.sg_void_ptr(), opt);
            Opcode op = rop.inst().ops()[opnum];
            for (int a = first_optional_arg; a < op.nargs(); ++a)
            {
                Symbol Name = rop.opargsym(op, a);
                ustring name = Name.get_string();
                ++a;
                Symbol Val = rop.opargsym(op, a);
                TypeDesc valtype = Val.typespec().simpletype();
                if (name.empty())
                {
                    continue;
                }
                if (name == Strings.anisotropic && Val.typespec().is_int())
                {
                    rop.ll.call_function("osl_noiseparams_set_anisotropic", opt, rop.il_load_value(Val));
                }
                else if (name == Strings.do_filter && Val.typespec().is_int())
                {
                    rop.ll.call_function("osl_noiseparams_set_do_filter", opt, rop.il_load_value(Val));
                }
                else if (name == Strings.direction && Val.typespec().is_triple())
                {
                    rop.ll.call_function("osl_noiseparams_set_direction", opt, rop.il_void_ptr(Val));
                }
                else if (name == Strings.bandwidth && (Val.typespec().is_float() || Val.typespec().is_int()))
                {
                    rop.ll.call_function("osl_noiseparams_set_bandwidth", opt, rop.il_load_value(Val, 0, null, 0, TypeDescs.TypeFloat));
                }
                else if (name == Strings.impulses && (Val.typespec().is_float() || Val.typespec().is_int()))
                {
                    rop.ll.call_function("osl_noiseparams_set_impulses", opt, rop.il_load_value(Val, 0, null, 0, TypeDescs.TypeFloat));
                }
                else
                {
                    rop.shadingcontext().errorfmt("Unknown {} optional argument: \"{}\", <{}> ({}:{})", op.opname(), name, valtype, op.sourcefile(),
                                                  op.sourceline());
                }
            }
            return opt;
        }

        public static bool il_gen_noise(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            bool periodic = (op.opname() == Strings.pnoise || op.opname() == Strings.psnoise);
            int arg = 0;
            Symbol Result = rop.opargsym(op, arg++);
            int outdim = Result.typespec().is_triple() ? 3 : 1;
            Symbol Name = rop.opargsym(op, arg++);
            ustring name;
            if (Name.typespec().is_string())
            {
                name = Name.is_constant() ? Name.get_string() : new ustring("");
            }
            else
            {
                --arg;
                Name = null;
                name = op.opname();
            }
            Symbol S = rop.opargsym(op, arg++);
            Symbol T = null;
            Symbol Sper = null;
            Symbol Tper = null;
            int indim = S.typespec().is_triple() ? 3 : 1;
            bool derivs = S.has_derivs();
            if (periodic)
            {
                if (op.nargs() > (arg + 1) && (rop.opargsym(op, arg + 1).typespec().is_float() || rop.opargsym(op, arg + 1).typespec().is_triple()))
                {
                    ++indim;
                    T = rop.opargsym(op, arg++);
                    derivs |= T.has_derivs();
                }
                Sper = rop.opargsym(op, arg++);
                if (indim == 2 || indim == 4)
                {
                    Tper = rop.opargsym(op, arg++);
                }
            }
            else
            {
                if (op.nargs() > arg && rop.opargsym(op, arg).typespec().is_float())
                {
                    ++indim;
                    T = rop.opargsym(op, arg++);
                    derivs |= T.has_derivs();
                }
            }
            derivs &= Result.has_derivs();
            bool pass_name = false, pass_sg = false, pass_options = false;
            if (name.empty())
            {
                name = periodic ? Strings.genericpnoise : Strings.genericnoise;
                pass_name = true;
                pass_sg = true;
                pass_options = true;
                derivs = true;
            }
            else if (name == Strings.perlin || name == Strings.snoise || name == Strings.psnoise)
            {
                name = periodic ? Strings.psnoise : Strings.snoise;
            }
            else if (name == Strings.uperlin || name == Strings.noise || name == Strings.pnoise)
            {
                name = periodic ? Strings.pnoise : Strings.noise;
            }
            else if (name == Strings.cell || name == Strings.cellnoise)
            {
                name = periodic ? Strings.pcellnoise : Strings.cellnoise;
                derivs = false;
            }
            else if (name == Strings.hash || name == Strings.hashnoise)
            {
                name = periodic ? Strings.phashnoise : Strings.hashnoise;
                derivs = false;
            }
            else if (name == Strings.simplex && !periodic)
            {
                name = Strings.simplexnoise;
            }
            else if (name == Strings.usimplex && !periodic)
            {
                name = Strings.usimplexnoise;
            }
            else if (name == Strings.gabor)
            {
                pass_name = true;
                pass_sg = true;
                pass_options = true;
                derivs = true;
                name = periodic ? Strings.gaborpnoise : Strings.gabornoise;
            }
            else
            {
                rop.shadingcontext().errorfmt("{}noise type \"{}\" is unknown, called from ({}:{})", periodic ? "periodic " : "", name, op.sourcefile(),
                                              op.sourceline());
                return false;
            }
            if (rop.shadingsys().no_noise())
            {
                if (name == Strings.uperlin || name == Strings.noise || name == Strings.usimplexnoise || name == Strings.usimplex || name == Strings.cell
                    || name == Strings.cellnoise || name == Strings.hash || name == Strings.hashnoise || name == Strings.pcellnoise
                    || name == Strings.pnoise)
                {
                    name = new ustring("unullnoise");
                }
                else
                {
                    name = new ustring("nullnoise");
                }
                pass_name = false;
                periodic = false;
                pass_sg = false;
                pass_options = false;
            }
            ILValue opt = null;
            if (pass_options)
            {
                opt = il_gen_noise_options(rop, opnum, arg);
            }
            string funcname = "osl_" + name.@string() + "_" + Result.arg_typecode(derivs);
            List<ILValue> args = new List<ILValue>();
            if (pass_name)
            {
                args.Add(rop.il_load_value(Name));
            }
            ILValue tmpresult = null;
            if (outdim == 3 || derivs)
            {
                if (derivs && !Result.has_derivs())
                {
                    tmpresult = rop.il_load_arg(Result, true);
                    args.Add(tmpresult);
                }
                else
                {
                    args.Add(rop.il_void_ptr(Result));
                }
            }
            funcname += S.arg_typecode(derivs);
            args.Add(rop.il_load_arg(S, derivs));
            if (T != null)
            {
                funcname += T.arg_typecode(derivs);
                args.Add(rop.il_load_arg(T, derivs));
            }
            if (periodic)
            {
                funcname += Sper.arg_typecode(false);
                args.Add(rop.il_load_arg(Sper, false));
                if (Tper != null)
                {
                    funcname += Tper.arg_typecode(false);
                    args.Add(rop.il_load_arg(Tper, false));
                }
            }
            if (pass_sg)
            {
                args.Add(rop.sg_void_ptr());
            }
            if (pass_options)
            {
                args.Add(opt);
            }
            ILValue r = rop.ll.call_function(funcname, args.ToArray());
            if (outdim == 1 && !derivs)
            {
                rop.il_store_value(r, Result);
            }
            else if (derivs && !Result.has_derivs())
            {
                tmpresult = rop.ll.ptr_cast(tmpresult);
                for (int c = 0; c < Result.typespec().aggregate(); ++c)
                {
                    ILValue v = rop.il_load_value(tmpresult, Result.typespec(), 0, null, c);
                    rop.il_store_value(v, Result, 0, c);
                }
            }
            if (Result.has_derivs() && !derivs)
            {
                rop.il_zero_derivs(Result);
            }
            if (rop.shadingsys().profile() >= 1)
            {
                rop.ll.call_function("osl_count_noise", rop.sg_void_ptr());
            }
            return true;
        }
    }
}
