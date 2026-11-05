// OSL port: llvm_gen.cpp (part 4) -- texture, texture3d and environment lookups with their optional-argument handling.

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using OSL;

namespace OSL.pvt
{
    public static unsafe partial class ILGen
    {
        private static bool tex_param_int(BackendIL rop, ILValue opt, ustring name, ustring paramname, string setname, TypeDesc valtype, Symbol Val, int defaultval,
                                          ref bool is_set)
        {
            if (name == paramname && valtype == TypeDescs.TypeInt)
            {
                int ival = (Val.typespec().is_int() && Val.is_constant()) ? Val.get_int() : 0;
                bool has_ival = Val.typespec().is_int() && Val.is_constant();
                if (!is_set && has_ival && ival == defaultval)
                {
                    return true;
                }
                ILValue val = rop.il_load_value(Val);
                rop.ll.call_function("osl_texture_set_" + setname, opt, val);
                is_set = true;
                return true;
            }
            return false;
        }

        private static bool tex_param_float(BackendIL rop, ILValue opt, ustring name, ustring paramname, string setname, TypeDesc valtype, Symbol Val,
                                            float defaultval, ref bool is_set)
        {
            if (name == paramname && (valtype == TypeDescs.TypeFloat || valtype == TypeDescs.TypeInt))
            {
                bool has_ival = Val.typespec().is_int() && Val.is_constant();
                bool has_fval = Val.typespec().is_float() && Val.is_constant();
                if (!is_set && ((has_ival && (float)Val.get_int() == defaultval) || (has_fval && Val.get_float() == defaultval)))
                {
                    return true;
                }
                ILValue val = rop.il_load_value(Val);
                if (valtype == TypeDescs.TypeInt)
                {
                    val = rop.ll.op_int_to_float(val);
                }
                rop.ll.call_function("osl_texture_set_" + setname, opt, val);
                is_set = true;
                return true;
            }
            return false;
        }

        private static bool tex_param_float_str(BackendIL rop, ILValue opt, ustring name, ustring paramname, string basename, TypeDesc valtype, Symbol Val,
                                                float defaultval, ref bool sset, ref bool tset, ref bool rset, bool tex3d)
        {
            if (name == paramname && (valtype == TypeDescs.TypeFloat || valtype == TypeDescs.TypeInt))
            {
                bool has_ival = Val.typespec().is_int() && Val.is_constant();
                bool has_fval = Val.typespec().is_float() && Val.is_constant();
                if (!sset && !tset && !rset && ((has_ival && (float)Val.get_int() == defaultval) || (has_fval && Val.get_float() == defaultval)))
                {
                    return true;
                }
                ILValue val = rop.il_load_value(Val);
                if (valtype == TypeDescs.TypeInt)
                {
                    val = rop.ll.op_int_to_float(val);
                }
                rop.ll.call_function("osl_texture_set_st" + basename, opt, val);
                if (tex3d)
                {
                    rop.ll.call_function("osl_texture_set_r" + basename, opt, val);
                }
                sset = true;
                tset = true;
                rset = true;
                return true;
            }
            return false;
        }

        private static bool tex_param_string_code(BackendIL rop, ILValue opt, ustring name, ustring paramname, string setname, TypeDesc valtype, Symbol Val,
                                                  Func<ustring, int> decoder, int defaultcode, ref bool is_set)
        {
            if (name == paramname && valtype == TypeDescs.TypeString)
            {
                if (Val.is_constant())
                {
                    int code = decoder(Val.get_string());
                    if (!is_set && code == defaultcode)
                    {
                        return true;
                    }
                    if (code >= 0)
                    {
                        ILValue val = rop.ll.constant(code);
                        rop.ll.call_function("osl_texture_set_" + setname + "_code", opt, val);
                    }
                }
                else
                {
                    ILValue val = rop.il_load_value(Val);
                    rop.ll.call_function("osl_texture_set_" + setname, opt, val);
                }
                is_set = true;
                return true;
            }
            return false;
        }

        private static ILValue il_gen_texture_options(BackendIL rop, int opnum, int first_optional_arg, bool tex3d, int nchans, ref ILValue alpha,
                                                      ref ILValue dalphadx, ref ILValue dalphady, ref ILValue errormessage)
        {
            ILValue opt = rop.temp_texture_options_void_ptr();
            rop.ll.call_function("osl_init_texture_options", rop.sg_void_ptr(), opt);
            ILValue missingcolor = null;
            TextureOpt optdefaults = TextureOpt.make_default();
            bool swidth_set = false, twidth_set = false, rwidth_set = false;
            bool sblur_set = false, tblur_set = false, rblur_set = false;
            bool swrap_set = false, twrap_set = false, rwrap_set = false;
            bool firstchannel_set = false, fill_set = false, interp_set = false;
            bool subimage_set = false;
            Opcode op = rop.inst().ops()[opnum];
            for (int a = first_optional_arg; a < op.nargs(); ++a)
            {
                Symbol Name = rop.opargsym(op, a);
                ustring name = Name.get_string();
                ++a;
                if (name.empty())
                {
                    continue;
                }
                Symbol Val = rop.opargsym(op, a);
                TypeDesc valtype = Val.typespec().simpletype();
                if (tex_param_float_str(rop, opt, name, Strings.width, "width", valtype, Val, optdefaults.swidth, ref swidth_set, ref twidth_set,
                                        ref rwidth_set, tex3d))
                {
                    continue;
                }
                if (tex_param_float(rop, opt, name, Strings.swidth, "swidth", valtype, Val, optdefaults.swidth, ref swidth_set))
                {
                    continue;
                }
                if (tex_param_float(rop, opt, name, Strings.twidth, "twidth", valtype, Val, optdefaults.twidth, ref twidth_set))
                {
                    continue;
                }
                if (tex_param_float(rop, opt, name, Strings.rwidth, "rwidth", valtype, Val, optdefaults.rwidth, ref rwidth_set))
                {
                    continue;
                }
                if (tex_param_float_str(rop, opt, name, Strings.blur, "blur", valtype, Val, optdefaults.sblur, ref sblur_set, ref tblur_set,
                                        ref rblur_set, tex3d))
                {
                    continue;
                }
                if (tex_param_float(rop, opt, name, Strings.sblur, "sblur", valtype, Val, optdefaults.sblur, ref sblur_set))
                {
                    continue;
                }
                if (tex_param_float(rop, opt, name, Strings.tblur, "tblur", valtype, Val, optdefaults.tblur, ref tblur_set))
                {
                    continue;
                }
                if (tex_param_float(rop, opt, name, Strings.rblur, "rblur", valtype, Val, optdefaults.rblur, ref rblur_set))
                {
                    continue;
                }
                if (name == Strings.wrap && valtype == TypeDescs.TypeString)
                {
                    if (Val.is_constant())
                    {
                        int mode = ILGlue.decode_wrapmode(Val.get_string());
                        ILValue val = rop.ll.constant(mode);
                        rop.ll.call_function("osl_texture_set_stwrap_code", opt, val);
                        if (tex3d)
                        {
                            rop.ll.call_function("osl_texture_set_rwrap_code", opt, val);
                        }
                    }
                    else
                    {
                        ILValue val = rop.il_load_value(Val);
                        rop.ll.call_function("osl_texture_set_stwrap", opt, val);
                        if (tex3d)
                        {
                            rop.ll.call_function("osl_texture_set_rwrap", opt, val);
                        }
                    }
                    swrap_set = true;
                    twrap_set = true;
                    rwrap_set = true;
                    continue;
                }
                if (tex_param_string_code(rop, opt, name, Strings.swrap, "swrap", valtype, Val, ILGlue.decode_wrapmode, optdefaults.swrap, ref swrap_set))
                {
                    continue;
                }
                if (tex_param_string_code(rop, opt, name, Strings.twrap, "twrap", valtype, Val, ILGlue.decode_wrapmode, optdefaults.twrap, ref twrap_set))
                {
                    continue;
                }
                if (tex_param_string_code(rop, opt, name, Strings.rwrap, "rwrap", valtype, Val, ILGlue.decode_wrapmode, optdefaults.rwrap, ref rwrap_set))
                {
                    continue;
                }
                if (tex_param_float(rop, opt, name, Strings.fill, "fill", valtype, Val, optdefaults.fill, ref fill_set))
                {
                    continue;
                }
                if (tex_param_int(rop, opt, name, Strings.firstchannel, "firstchannel", valtype, Val, optdefaults.firstchannel, ref firstchannel_set))
                {
                    continue;
                }
                if (tex_param_int(rop, opt, name, Strings.subimage, "subimage", valtype, Val, optdefaults.subimage, ref subimage_set))
                {
                    continue;
                }
                if (name == Strings.subimage && valtype == TypeDescs.TypeString)
                {
                    if (Val.is_constant())
                    {
                        ustring v = Val.get_string();
                        if (v.empty() && !subimage_set)
                        {
                            continue;
                        }
                    }
                    ILValue val = rop.il_load_value(Val);
                    rop.ll.call_function("osl_texture_set_subimagename", opt, val);
                    subimage_set = true;
                    continue;
                }
                if (tex_param_string_code(rop, opt, name, Strings.interp, "interp", valtype, Val, ILGlue.tex_interp_to_code, optdefaults.interpmode,
                                          ref interp_set))
                {
                    continue;
                }
                if (name == Strings.alpha && valtype == TypeDescs.TypeFloat)
                {
                    alpha = rop.il_get_pointer(Val);
                    if (Val.has_derivs())
                    {
                        dalphadx = rop.il_get_pointer(Val, 1);
                        dalphady = rop.il_get_pointer(Val, 2);
                    }
                    continue;
                }
                if (name == Strings.errormessage && valtype == TypeDescs.TypeString)
                {
                    errormessage = rop.il_get_pointer(Val);
                    continue;
                }
                if (name == Strings.missingcolor && TypeDesc.equivalent(valtype, TypeDescs.TypeColor))
                {
                    if (missingcolor == null)
                    {
                        missingcolor = rop.ll.op_alloca(16, 16);
                        rop.ll.call_function("osl_texture_set_missingcolor_arena", opt, rop.ll.void_ptr(missingcolor));
                    }
                    rop.ll.op_memcpy(rop.ll.void_ptr(missingcolor), rop.il_void_ptr(Val), 12);
                    continue;
                }
                if (name == Strings.missingalpha && valtype == TypeDescs.TypeFloat)
                {
                    if (missingcolor == null)
                    {
                        missingcolor = rop.ll.op_alloca(16, 16);
                        rop.ll.call_function("osl_texture_set_missingcolor_arena", opt, rop.ll.void_ptr(missingcolor));
                    }
                    ILValue val = rop.il_load_value(Val);
                    rop.ll.call_function("osl_texture_set_missingcolor_alpha", opt, rop.ll.constant(nchans), val);
                    continue;
                }
                if (name == Strings.colorspace && valtype == TypeDescs.TypeString)
                {
                    if (Val.is_constant())
                    {
                        continue;
                    }
                    rop.shadingcontext().errorfmt("texture{} optional argument \"{}\" must be constant after optimization ({}:{})", tex3d ? "3d" : "",
                                                  name, op.sourcefile(), op.sourceline());
                    continue;
                }
                if (name == Strings.time && (valtype == TypeDescs.TypeFloat || valtype == TypeDescs.TypeInt))
                {
                    continue;
                }
                rop.shadingcontext().errorfmt("Unknown texture{} optional argument: \"{}\", <{}> ({}:{})", tex3d ? "3d" : "", name, valtype, op.sourcefile(),
                                              op.sourceline());
            }
            return opt;
        }

        private static ILValue null_or(BackendIL rop, ILValue v)
        {
            return v != null ? rop.ll.void_ptr(v) : rop.ll.void_ptr_null();
        }

        public static bool il_gen_texture(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Filename = rop.opargsym(op, 1);
            Symbol S = rop.opargsym(op, 2);
            Symbol T = rop.opargsym(op, 3);
            int nchans = Result.typespec().aggregate();
            bool user_derivs = false;
            int first_optional_arg = 4;
            if (op.nargs() > 4 && rop.opargsym(op, 4).typespec().is_float())
            {
                user_derivs = true;
                first_optional_arg = 8;
            }
            ILValue alpha = null, dalphadx = null, dalphady = null, errormessage = null;
            ILValue opt = il_gen_texture_options(rop, opnum, first_optional_arg, false, nchans, ref alpha, ref dalphadx, ref dalphady, ref errormessage);
            long texture_handle = 0;
            if (Filename.is_constant() && rop.shadingsys().opt_texture_handle())
            {
                texture_handle = rop.il_texture_handle(Filename.get_string());
            }
            ILValue[] args = new ILValue[]
            {
                rop.sg_void_ptr(), rop.il_load_value(Filename), rop.ll.constant_ptr(texture_handle), opt, rop.il_load_value(S), rop.il_load_value(T),
                user_derivs ? rop.il_load_value(rop.opargsym(op, 4)) : rop.il_load_value(S, 1),
                user_derivs ? rop.il_load_value(rop.opargsym(op, 5)) : rop.il_load_value(T, 1),
                user_derivs ? rop.il_load_value(rop.opargsym(op, 6)) : rop.il_load_value(S, 2),
                user_derivs ? rop.il_load_value(rop.opargsym(op, 7)) : rop.il_load_value(T, 2),
                rop.ll.constant(nchans), rop.ll.void_ptr(rop.il_get_pointer(Result, 0)), rop.ll.void_ptr(rop.il_get_pointer(Result, 1)),
                rop.ll.void_ptr(rop.il_get_pointer(Result, 2)), null_or(rop, alpha), null_or(rop, dalphadx), null_or(rop, dalphady),
                null_or(rop, errormessage)
            };
            rop.ll.call_function("osl_texture", args);
            rop.generated_texture_call(texture_handle != 0);
            return true;
        }

        public static bool il_gen_texture3d(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Filename = rop.opargsym(op, 1);
            Symbol P = rop.opargsym(op, 2);
            int nchans = Result.typespec().aggregate();
            bool user_derivs = false;
            int first_optional_arg = 3;
            if (op.nargs() > 3 && rop.opargsym(op, 3).typespec().is_triple())
            {
                user_derivs = true;
                first_optional_arg = 6;
            }
            ILValue alpha = null, dalphadx = null, dalphady = null, errormessage = null;
            ILValue opt = il_gen_texture_options(rop, opnum, first_optional_arg, true, nchans, ref alpha, ref dalphadx, ref dalphady, ref errormessage);
            long texture_handle = 0;
            if (Filename.is_constant() && rop.shadingsys().opt_texture_handle())
            {
                texture_handle = rop.il_texture_handle(Filename.get_string());
            }
            ILValue[] args = new ILValue[]
            {
                rop.sg_void_ptr(), rop.il_load_value(Filename), rop.ll.constant_ptr(texture_handle), opt, rop.il_void_ptr(P),
                user_derivs ? rop.il_void_ptr(rop.opargsym(op, 3)) : rop.il_void_ptr(P, 1),
                user_derivs ? rop.il_void_ptr(rop.opargsym(op, 4)) : rop.il_void_ptr(P, 2),
                user_derivs ? rop.il_void_ptr(rop.opargsym(op, 5)) : rop.ll.void_ptr_null(), rop.ll.constant(nchans),
                rop.ll.void_ptr(rop.il_void_ptr(Result, 0)), rop.ll.void_ptr(rop.il_void_ptr(Result, 1)), rop.ll.void_ptr(rop.il_void_ptr(Result, 2)),
                null_or(rop, alpha), null_or(rop, dalphadx), null_or(rop, dalphady), null_or(rop, errormessage)
            };
            rop.ll.call_function("osl_texture3d", args);
            rop.generated_texture_call(texture_handle != 0);
            return true;
        }

        public static bool il_gen_environment(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Filename = rop.opargsym(op, 1);
            Symbol R = rop.opargsym(op, 2);
            int nchans = Result.typespec().aggregate();
            bool user_derivs = false;
            int first_optional_arg = 3;
            if (op.nargs() > 3 && rop.opargsym(op, 3).typespec().is_triple())
            {
                user_derivs = true;
                first_optional_arg = 5;
            }
            ILValue alpha = null, dalphadx = null, dalphady = null, errormessage = null;
            ILValue opt = il_gen_texture_options(rop, opnum, first_optional_arg, false, nchans, ref alpha, ref dalphadx, ref dalphady, ref errormessage);
            long texture_handle = 0;
            if (Filename.is_constant() && rop.shadingsys().opt_texture_handle())
            {
                texture_handle = rop.il_texture_handle(Filename.get_string());
            }
            ILValue[] args = new ILValue[]
            {
                rop.sg_void_ptr(), rop.il_load_value(Filename), rop.ll.constant_ptr(texture_handle), opt, rop.il_void_ptr(R),
                user_derivs ? rop.il_void_ptr(rop.opargsym(op, 3)) : rop.il_void_ptr(R, 1),
                user_derivs ? rop.il_void_ptr(rop.opargsym(op, 4)) : rop.il_void_ptr(R, 2), rop.ll.constant(nchans), rop.il_void_ptr(Result, 0),
                rop.il_void_ptr(Result, 1), rop.il_void_ptr(Result, 2), null_or(rop, alpha), null_or(rop, dalphadx), null_or(rop, dalphady),
                null_or(rop, errormessage)
            };
            rop.ll.call_function("osl_environment", args);
            rop.generated_texture_call(texture_handle != 0);
            return true;
        }
    }
}
