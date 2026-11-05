// OSL port: liboslexec/backendllvm.h, backendllvm.cpp and llvm_instance.cpp -- the code generator of a shader group, with an IL emitter instead of LLVM.
//
// BackendIL is BackendLLVM. The llvm_* members are il_* members here (llvm_load_value is il_load_value, loadLLVMValue is loadILValue ...), the
// llvm::Value* is an ILValue and LLVM_Util is ILUtil. The structure of the generated code is the C++ one:
//
//      void osl_init_group_<group>(sg, groupdata, userdata_base, output_base, shadeindex, interactive_params)     clears the layer run flags
//      void osl_layer_group_<group>_name_<layer>(same arguments)                                                  one per layer
//
// The one thing that is different is the group data: LLVM describes it with a struct type, here the byte offsets of the fields are computed (the same
// layout algorithm, in il_type_groupdata) and the fields are addressed as groupdata + offset. Symbols that are locals, temporaries and constants live in
// the frame of the function (alloca) or in a data blob (constants).

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using OIIO;
using OSL;
using static OSL.pvt.PvtFunctions;

namespace OSL.pvt
{
    /// The IL equivalent of the BackendLLVM class
    public sealed unsafe partial class BackendIL : OSOProcessorBase
    {
        public ILUtil ll;
        private ILTarget m_target;

        private List<int> m_layer_remap = new List<int>();
        private HashSet<int> m_layers_already_run = new HashSet<int>();
        private int m_num_used_layers;
        private bool m_layout_only;

        private Dictionary<string, ILValue> m_named_values = new Dictionary<string, ILValue>();
        private Dictionary<Symbol, int> m_param_order_map = new Dictionary<Symbol, int>();
        private ILValue m_il_shaderglobals_ptr;
        private ILValue m_il_groupdata_ptr;
        private ILValue m_il_interactive_params_ptr;
        private ILValue m_il_userdata_base_ptr;
        private ILValue m_il_output_base_ptr;
        private ILValue m_il_shadeindex;
        private ILValue m_il_temp_texture_options_ptr;
        private ILValue m_il_temp_trace_options_ptr;
        private ILValue m_il_temp_noise_options_ptr;
        private ILLabel m_exit_instance_block;
        private int m_il_local_mem;
        private Dictionary<string, ILValue> m_const_map = new Dictionary<string, ILValue>();
        /// Byte offset of each field of the group data (the fields of the struct type of the C++)
        private List<int> m_groupdata_field_offsets = new List<int>();
        private List<string> m_groupdata_field_names = new List<string>();
        private bool m_groupdata_layout_done;
        private Dictionary<string, ILFunction> m_functions = new Dictionary<string, ILFunction>();
        private ILFunction m_init_function;
        private List<ILFunction> m_layer_functions = new List<ILFunction>();

        public static readonly TypeDesc NoCast = new TypeDesc();

        public BackendIL(ShadingSystemImpl shadingsys, ShaderGroup group, ShadingContext context, ILTarget target)
            : base(shadingsys, group, context)
        {
            m_target = target;
            ll = new ILUtil(target);
        }

        public void set_layout_only(bool lo)
        {
            m_layout_only = lo;
        }

        public int llvm_debug()
        {
            if (shadingsys().llvm_debug() == 0)
            {
                return 0;
            }
            if (!shadingsys().debug_groupname().empty() && shadingsys().debug_groupname() != group().name())
            {
                return 0;
            }
            if (inst() != null && !shadingsys().debug_layername().empty() && shadingsys().debug_layername() != inst().layername())
            {
                return 0;
            }
            return shadingsys().llvm_debug();
        }

        public int layer_remap(int origlayer)
        {
            return m_layer_remap[origlayer];
        }

        public ILValue sg_ptr() { return m_il_shaderglobals_ptr; }
        public ILValue sg_void_ptr() { return m_il_shaderglobals_ptr; }
        public ILValue groupdata_ptr() { return m_il_groupdata_ptr; }
        public ILValue groupdata_void_ptr() { return m_il_groupdata_ptr; }
        public ILValue userdata_base_ptr() { return m_il_userdata_base_ptr; }
        public ILValue output_base_ptr() { return m_il_output_base_ptr; }
        public ILValue shadeindex() { return m_il_shadeindex; }
        public bool use_optix() { return false; }
        public bool use_optix_cache() { return shadingsys().use_optix_cache(); }
        public Dictionary<string, ILValue> named_values() { return m_named_values; }
        public Dictionary<string, ILValue> get_const_map() { return m_const_map; }

        /// OSOProcessorBase::set_inst (runtimeoptimize.cpp)
        public void il_set_inst(int newlayer)
        {
            m_layer = newlayer;
            m_inst = group()[m_layer];
        }

        /// OSOProcessorBase::find_conditionals (runtimeoptimize.cpp)
        public void il_find_conditionals()
        {
            List<Opcode> code = inst().ops();
            m_in_conditional.Clear();
            for (int i = 0; i < code.Count; ++i)
            {
                m_in_conditional.Add(false);
            }
            m_in_loop.Clear();
            for (int i = 0; i < code.Count; ++i)
            {
                m_in_loop.Add(false);
            }
            m_first_return = code.Count;
            for (int i = 0; i < code.Count; ++i)
            {
                if (code[i].jump(0) >= 0)
                {
                    for (int k = i; k < code[i].farthest_jump(); ++k)
                    {
                        m_in_conditional[k] = true;
                    }
                    if (code[i].opname() == Strings.op_dowhile || code[i].opname() == Strings.op_for || code[i].opname() == Strings.op_while)
                    {
                        for (int k = i; k < code[i].farthest_jump(); ++k)
                        {
                            m_in_loop[k] = true;
                        }
                    }
                }
                if (code[i].opname() == Strings.op_exit)
                {
                    m_first_return = Math.Min(m_first_return, i);
                }
            }
        }

        private static readonly ustring u_functioncall_nr = new ustring("functioncall_nr");
        private static readonly ustring u_break = new ustring("break");
        private static readonly ustring u_continue = new ustring("continue");
        private static readonly ustring u_return = new ustring("return");

        /// OSOProcessorBase::find_basic_blocks (runtimeoptimize.cpp)
        public void il_find_basic_blocks()
        {
            List<Opcode> code = inst().ops();
            m_bblockids.Clear();
            for (int i = 0; i < code.Count; ++i)
            {
                m_bblockids.Add(0);
            }
            bool[] block_begin = new bool[code.Count + 1];
            foreach (Symbol s in inst().param_range())
            {
                if (s.has_init_ops())
                {
                    block_begin[s.initbegin()] = true;
                }
            }
            block_begin[inst().maincodebegin()] = true;
            for (int opnum = 0; opnum < code.Count; ++opnum)
            {
                Opcode op = code[opnum];
                if (op.opname() == u_functioncall_nr)
                {
                    continue;
                }
                for (int j = 0; j < Opcode.max_jumps; ++j)
                {
                    if (op.jump(j) >= 0)
                    {
                        block_begin[op.jump(j)] = true;
                    }
                    else
                    {
                        break;
                    }
                }
                if (op.jump(0) >= 0)
                {
                    block_begin[opnum + 1] = true;
                }
                if (op.opname() == u_break || op.opname() == u_continue || op.opname() == u_return || op.opname() == Strings.op_exit)
                {
                    block_begin[opnum + 1] = true;
                }
            }
            int bbid = 1;
            for (int opnum = 0; opnum < code.Count; ++opnum)
            {
                if (block_begin[opnum])
                {
                    ++bbid;
                }
                m_bblockids[opnum] = bbid;
            }
        }

        /// OSOProcessorBase::is_zero (runtimeoptimize.cpp)
        public bool il_is_zero(Symbol A)
        {
            if (!A.is_constant())
            {
                return false;
            }
            TypeSpec Atype = A.typespec();
            return (Atype.is_float() && A.get_float() == 0) || (Atype.is_int() && A.get_int() == 0)
                   || (Atype.is_triple() && A.get_float(0) == 0 && A.get_float(1) == 0 && A.get_float(2) == 0);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Types
        // ------------------------------------------------------------------------------------------------------------------------------------------

        /// The type the C++ describes the storage of a typespec with (closures are pointers)
        public TypeDesc il_typedesc(TypeSpec typespec)
        {
            if (typespec.is_closure_based())
            {
                return new TypeDesc(TypeDesc.PTR, TypeDesc.SCALAR, 0) { arraylen = typespec.arraylength() };
            }
            return typespec.simpletype();
        }

        /// The machine type of a scalar component of a type
        public static ILType il_scalar_type(TypeDesc t)
        {
            switch ((int)t.basetype)
            {
                case TypeDesc.FLOAT:
                    return ILType.F4;
                case TypeDesc.DOUBLE:
                    return ILType.F8;
                case TypeDesc.INT:
                case TypeDesc.UINT:
                    return ILType.I4;
                case TypeDesc.STRING:
                case TypeDesc.USTRINGHASH:
                case TypeDesc.LONGLONG:
                case TypeDesc.ULONGLONG:
                    return ILType.I8;
                case TypeDesc.PTR:
                    return ILType.Ptr;
                case TypeDesc.CHAR:
                case TypeDesc.UCHAR:
                    return ILType.I1;
                default:
                    throw new ILGenException("no machine type for " + t.c_str());
            }
        }

        public int il_elemsize(TypeSpec typespec)
        {
            return (int)il_typedesc(typespec).elementtype().size();
        }

        public static int il_scalar_size(TypeDesc t)
        {
            return t.basesize();
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Zeroing
        // ------------------------------------------------------------------------------------------------------------------------------------------

        public void il_assign_zero(Symbol sym)
        {
            int len;
            if (sym.typespec().is_closure_based())
            {
                len = sizeof(void*) * sym.typespec().numelements();
            }
            else
            {
                len = sym.derivsize();
            }
            int align = sym.typespec().is_closure_based() ? sizeof(void*) : sym.typespec().simpletype().basesize();
            ll.op_memset(il_void_ptr(sym), 0, len, align);
        }

        public void il_zero_derivs(Symbol sym)
        {
            if (sym.typespec().is_closure_based())
            {
                return;
            }
            TypeSpec elemtype = sym.typespec().elementtype();
            if (sym.has_derivs() && elemtype.is_float_based())
            {
                int len = sym.size();
                int align = sym.typespec().simpletype().basesize();
                ll.op_memset(il_void_ptr(sym, 1), 0, 2 * len, align);
            }
        }

        public void il_zero_derivs(Symbol sym, ILValue count)
        {
            if (sym.typespec().is_closure_based())
            {
                return;
            }
            TypeSpec elemtype = sym.typespec().elementtype();
            if (sym.has_derivs() && elemtype.is_float_based())
            {
                int esize = sym.typespec().simpletype().elementsize();
                int align = sym.typespec().simpletype().basesize();
                count = ll.op_mul(count, ll.constant(esize));
                ll.op_memset(il_void_ptr(sym, 1), 0, count, align);
                ll.op_memset(il_void_ptr(sym, 2), 0, count, align);
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // ShaderGlobals
        // ------------------------------------------------------------------------------------------------------------------------------------------

        private static readonly string[] s_sg_names = new string[]
        {
            "P", "_dPdz", "I", "N", "Ng", "u", "v", "dPdu", "dPdv", "time", "dtime", "dPdtime", "Ps", "renderstate", "tracedata", "objdata",
            "shadingcontext", "shadingStateUniform", "thread_index", "shade_index", "renderer", "object2common", "shader2common", "Ci", "surfacearea",
            "raytype", "flipHandedness", "backfacing"
        };

        private static readonly string[] s_sg_fields = new string[]
        {
            "P", "dPdz", "I", "N", "Ng", "u", "v", "dPdu", "dPdv", "time", "dtime", "dPdtime", "Ps", "renderstate", "tracedata", "objdata",
            "context", "shadingStateUniform", "thread_index", "shade_index", "renderer", "object2common", "shader2common", "Ci", "surfacearea",
            "raytype", "flipHandedness", "backfacing"
        };

        private static int[] s_sg_offsets;

        public static int[] build_offsets_of_ShaderGlobals()
        {
            if (s_sg_offsets == null)
            {
                int[] offs = new int[s_sg_fields.Length];
                for (int i = 0; i < offs.Length; ++i)
                {
                    offs[i] = (int)Marshal.OffsetOf(typeof(ShaderGlobals), s_sg_fields[i]);
                }
                s_sg_offsets = offs;
            }
            return s_sg_offsets;
        }

        public int ShaderGlobalNameToIndex(ustring name)
        {
            for (int i = 0; i < s_sg_names.Length; ++i)
            {
                if (name.@string() == s_sg_names[i])
                {
                    return i;
                }
            }
            return -1;
        }

        public ILValue il_global_symbol_ptr(ustring name)
        {
            int sg_index = ShaderGlobalNameToIndex(name);
            if (sg_index < 0)
            {
                throw new ILGenException("unknown shader global " + name.@string());
            }
            return ll.offset_ptr(sg_ptr(), build_offsets_of_ShaderGlobals()[sg_index]);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Symbol addresses
        // ------------------------------------------------------------------------------------------------------------------------------------------

        public ILValue il_const_hash(ustring str)
        {
            return ll.constant(str);
        }

        public ILValue il_const_hash(string str)
        {
            return ll.constant(new ustring(str));
        }

        public ILValue groupdata_field_ptr(int fieldnum)
        {
            return ll.offset_ptr(groupdata_ptr(), m_groupdata_field_offsets[fieldnum]);
        }

        public ILValue layer_run_ref(int layer)
        {
            return ll.offset_ptr(groupdata_ptr(), m_groupdata_field_offsets[0] + layer);
        }

        public ILValue userdata_initialized_ref(int userdata_index = 0)
        {
            return ll.offset_ptr(groupdata_ptr(), m_groupdata_field_offsets[1] + userdata_index);
        }

        public ILValue symloc_ptr(SymLocationDesc symloc, ILValue base_ptr, ILValue sindex = null)
        {
            ILValue offset = ll.constanti64(symloc.offset);
            ILValue stride = ll.constanti64(symloc.stride);
            if (sindex == null)
            {
                sindex = ll.op_int_to_longlong(m_il_shadeindex);
            }
            ILValue fulloffset = ll.op_add(offset, ll.op_mul(stride, sindex));
            return ll.offset_ptr(base_ptr, fulloffset);
        }

        public ILValue getILSymbolBase(Symbol sym)
        {
            Symbol dealiased = sym.dealias();
            if (sym.symtype() == SymType.SymTypeGlobal)
            {
                return il_global_symbol_ptr(sym.name());
            }
            if (sym.symtype() == SymType.SymTypeParam && sym.interactive() && !sym.connected())
            {
                int offset = group().interactive_param_offset(layer(), sym.name());
                return ll.offset_ptr(m_il_interactive_params_ptr, offset);
            }
            if (sym.symtype() == SymType.SymTypeParam || (sym.symtype() == SymType.SymTypeOutputParam && !can_treat_param_as_local(sym)))
            {
                int fieldnum = m_param_order_map[sym];
                return groupdata_field_ptr(fieldnum);
            }
            string mangled_name = dealiased.mangled();
            ILValue v;
            if (!named_values().TryGetValue(mangled_name, out v))
            {
                shadingcontext().errorfmt("Couldn't find symbol '{}' (unmangled = '{}'). Did you forget to allocate it?", mangled_name,
                                          dealiased.unmangled());
                return null;
            }
            return v;
        }

        public ILValue il_alloca(TypeSpec type, bool derivs, string name = "", int align = 0)
        {
            TypeDesc t = il_typedesc(type);
            int n = derivs ? 3 : 1;
            int size = (int)t.size() * n;
            m_il_local_mem += size;
            return ll.op_alloca(size, align > 0 ? align : Math.Max(4, Math.Min(16, t.basesize())), name);
        }

        public bool can_treat_param_as_local(Symbol sym)
        {
            if (!shadingsys().m_opt_groupdata)
            {
                return false;
            }
            return sym.symtype() == SymType.SymTypeOutputParam && !sym.renderer_output() && !sym.typespec().is_closure_based() && !sym.connected();
        }

        public ILValue getOrAllocateILSymbol(Symbol sym)
        {
            Symbol dealiased = sym.dealias();
            string mangled_name = dealiased.mangled();
            ILValue v;
            if (!named_values().TryGetValue(mangled_name, out v))
            {
                v = il_alloca(sym.typespec(), sym.has_derivs(), mangled_name + "_mem");
                named_values()[mangled_name] = v;
            }
            return v;
        }

        public string global_unique_symname(Symbol sym)
        {
            string sym_name = sym.name().@string().Replace(".", "_");
            int layer = sym.layer();
            ShaderInstance inst_ = group()[layer];
            return Fmt.format("{}{}_{}_{}_{}", sym_name.Length > 0 && sym_name[0] == '$' ? "_" : "", sym_name, group().name(), inst_.layername(), layer);
        }

        public ILValue il_get_pointer(Symbol sym, int deriv = 0, ILValue arrayindex = null)
        {
            bool has_derivs = sym.has_derivs();
            if (!has_derivs && deriv != 0)
            {
                return ll.void_ptr_null();
            }
            ILValue result;
            if (sym.symtype() == SymType.SymTypeConst)
            {
                string unique_symname = global_unique_symname(sym);
                if (!get_const_map().TryGetValue(unique_symname, out result))
                {
                    throw new ILGenException("constant " + unique_symname + " was not created");
                }
                return result;
            }
            else
            {
                result = getILSymbolBase(sym);
            }
            if (result == null)
            {
                return null;
            }
            TypeDesc t = sym.typespec().simpletype();
            if (t.arraylen != 0 || has_derivs)
            {
                int d = deriv * Math.Max(1, t.arraylen);
                if (arrayindex != null && d != 0)
                {
                    arrayindex = ll.op_add(arrayindex, ll.constant(d));
                }
                else if (arrayindex == null)
                {
                    arrayindex = ll.constant(d);
                }
                result = ll.GEP(result, arrayindex, il_elemsize(sym.typespec()));
            }
            return result;
        }

        public ILValue il_void_ptr(Symbol sym, int deriv = 0)
        {
            return il_get_pointer(sym, deriv);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Loads and stores
        // ------------------------------------------------------------------------------------------------------------------------------------------

        public ILValue il_load_value(Symbol sym, int deriv, ILValue arrayindex, int component, TypeDesc cast = default(TypeDesc))
        {
            bool has_derivs = sym.has_derivs();
            if (!has_derivs && deriv != 0)
            {
                return ll.constant(0.0f);
            }
            if (sym.is_constant() && !sym.typespec().is_array() && arrayindex == null)
            {
                if (sym.typespec().is_float())
                {
                    if (cast == TypeDescs.TypeInt)
                    {
                        return ll.constant((int)sym.get_float());
                    }
                    return ll.constant(sym.get_float());
                }
                if (sym.typespec().is_int())
                {
                    if (cast == TypeDescs.TypeFloat)
                    {
                        return ll.constant((float)sym.get_int());
                    }
                    return ll.constant(sym.get_int());
                }
                if (sym.typespec().is_triple() || sym.typespec().is_matrix())
                {
                    return ll.constant(sym.get_float(component));
                }
                if (sym.typespec().is_string())
                {
                    return il_const_hash(sym.get_string());
                }
                throw new ILGenException("unhandled constant type");
            }
            return il_load_value(il_get_pointer(sym), sym.typespec(), deriv, arrayindex, component, cast);
        }

        public ILValue il_load_value(Symbol sym, int deriv = 0, int component = 0, TypeDesc cast = default(TypeDesc))
        {
            return il_load_value(sym, deriv, null, component, cast);
        }

        public ILValue il_load_value(ILValue ptr, TypeSpec type, int deriv, ILValue arrayindex, int component, TypeDesc cast = default(TypeDesc))
        {
            if (ptr == null)
            {
                return null;
            }
            TypeDesc t = type.simpletype();
            if (t.arraylen != 0 || deriv != 0)
            {
                int d = deriv * Math.Max(1, t.arraylen);
                if (arrayindex != null)
                {
                    arrayindex = d != 0 ? ll.op_add(arrayindex, ll.constant(d)) : arrayindex;
                }
                else
                {
                    arrayindex = ll.constant(d);
                }
                ptr = ll.GEP(ptr, arrayindex, il_elemsize(type));
            }
            if (!type.is_closure_based() && t.aggregate > 1)
            {
                ptr = ll.offset_ptr(ptr, component * t.basesize());
            }
            ILType component_type = type.is_closure_based() ? ILType.Ptr : il_scalar_type(t);
            ILValue result = ll.op_load(component_type, ptr);
            if (type.is_closure_based())
            {
                return result;
            }
            if (type.is_float_based() && !type.is_array() && cast == TypeDescs.TypeInt)
            {
                result = ll.op_float_to_int(result);
            }
            else if (type.is_int() && cast == TypeDescs.TypeFloat)
            {
                result = ll.op_int_to_float(result);
            }
            return result;
        }

        public ILValue il_load_constant_value(Symbol sym, int arrayindex, int component, TypeDesc cast = default(TypeDesc))
        {
            if (!sym.typespec().is_array())
            {
                arrayindex = 0;
            }
            int ncomps = (int)sym.typespec().aggregate();
            if (ncomps == 1)
            {
                component = 0;
            }
            int linear_index = ncomps * arrayindex + component;
            if (sym.typespec().is_triple() || sym.typespec().is_matrix())
            {
                return ll.constant(sym.get_float(linear_index));
            }
            if (sym.typespec().is_float_based())
            {
                if (cast == TypeDescs.TypeInt)
                {
                    return ll.constant((int)sym.get_float(linear_index));
                }
                return ll.constant(sym.get_float(linear_index));
            }
            if (sym.typespec().is_int_based())
            {
                if (cast == TypeDescs.TypeFloat)
                {
                    return ll.constant((float)sym.get_int(linear_index));
                }
                return ll.constant(sym.get_int(linear_index));
            }
            if (sym.typespec().is_string_based())
            {
                return il_const_hash(sym.get_string(linear_index));
            }
            throw new ILGenException("unhandled constant type");
        }

        public ILValue il_load_component_value(Symbol sym, int deriv, ILValue component)
        {
            bool has_derivs = sym.has_derivs();
            if (!has_derivs && deriv != 0)
            {
                return ll.constant(0.0f);
            }
            ILValue result = il_get_pointer(sym, deriv);
            if (result == null)
            {
                return null;
            }
            result = ll.GEP(result, component, 4);
            return ll.op_load(ILType.F4, result);
        }

        public ILValue loadILValue(Symbol sym, int component = 0, int deriv = 0, TypeDesc cast = default(TypeDesc))
        {
            return il_load_value(sym, deriv, null, component, cast);
        }

        /// The value passed to a shadeop for an argument: the value itself, or a pointer to it (llvm_load_arg)
        public ILValue il_load_arg(Symbol sym, bool derivs)
        {
            if (sym.typespec().is_int() || (sym.typespec().is_float() && !derivs))
            {
                return il_load_value(sym);
            }
            if (derivs && !sym.has_derivs())
            {
                TypeSpec t = sym.typespec();
                ILValue tmpptr = il_alloca(t, true);
                for (int c = 0; c < t.aggregate(); ++c)
                {
                    ILValue v = il_load_value(sym, 0, c);
                    il_store_value(v, tmpptr, t, 0, null, c);
                }
                ILValue zero = ll.constant(0.0f);
                for (int c = 0; c < t.aggregate(); ++c)
                {
                    il_store_value(zero, tmpptr, t, 1, null, c);
                }
                for (int c = 0; c < t.aggregate(); ++c)
                {
                    il_store_value(zero, tmpptr, t, 2, null, c);
                }
                return tmpptr;
            }
            return il_void_ptr(sym);
        }

        public ILValue il_load_arg(Symbol sym)
        {
            return il_load_arg(sym, sym.has_derivs());
        }

        public bool il_store_value(ILValue new_val, Symbol sym, int deriv, ILValue arrayindex, int component)
        {
            bool has_derivs = sym.has_derivs();
            if (!has_derivs && deriv != 0)
            {
                return true;
            }
            return il_store_value(new_val, il_get_pointer(sym), sym.typespec(), deriv, arrayindex, component);
        }

        public bool il_store_value(ILValue new_val, ILValue dst_ptr, TypeSpec type, int deriv, ILValue arrayindex, int component)
        {
            if (dst_ptr == null)
            {
                return false;
            }
            TypeDesc t = type.simpletype();
            if (t.arraylen != 0 || deriv != 0)
            {
                int d = deriv * Math.Max(1, t.arraylen);
                if (arrayindex != null)
                {
                    arrayindex = d != 0 ? ll.op_add(arrayindex, ll.constant(d)) : arrayindex;
                }
                else
                {
                    arrayindex = ll.constant(d);
                }
                dst_ptr = ll.GEP(dst_ptr, arrayindex, il_elemsize(type));
            }
            if (!type.is_closure_based() && t.aggregate > 1)
            {
                dst_ptr = ll.offset_ptr(dst_ptr, component * t.basesize());
            }
            ll.op_store(new_val, dst_ptr);
            return true;
        }

        public bool il_store_value(ILValue new_val, Symbol sym, int deriv = 0, int component = 0)
        {
            return il_store_value(new_val, sym, deriv, null, component);
        }

        public bool il_store_component_value(ILValue new_val, Symbol sym, int deriv, ILValue component)
        {
            bool has_derivs = sym.has_derivs();
            if (!has_derivs && deriv != 0)
            {
                return true;
            }
            ILValue result = il_get_pointer(sym, deriv);
            if (result == null)
            {
                return false;
            }
            result = ll.GEP(result, component, 4);
            ll.op_store(new_val, result);
            return true;
        }

        public bool storeILValue(ILValue new_val, Symbol sym, int component = 0, int deriv = 0)
        {
            return il_store_value(new_val, sym, deriv, component);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Calls of shadeops with symbols as arguments
        // ------------------------------------------------------------------------------------------------------------------------------------------

        public ILValue il_call_function(string name, Symbol[] args, bool deriv_ptrs = false)
        {
            ILValue[] valargs = new ILValue[args.Length];
            for (int i = 0; i < args.Length; ++i)
            {
                Symbol s = args[i];
                if (s.typespec().is_closure())
                {
                    valargs[i] = il_load_value(s);
                }
                else if (s.typespec().simpletype().aggregate > 1 || (deriv_ptrs && s.has_derivs()))
                {
                    valargs[i] = il_void_ptr(s);
                }
                else
                {
                    valargs[i] = il_load_value(s);
                }
            }
            return ll.call_function(name, valargs);
        }

        public ILValue il_call_function(string name, Symbol A, bool deriv_ptrs = false)
        {
            return il_call_function(name, new Symbol[] { A }, deriv_ptrs);
        }

        public ILValue il_call_function(string name, Symbol A, Symbol B, bool deriv_ptrs = false)
        {
            return il_call_function(name, new Symbol[] { A, B }, deriv_ptrs);
        }

        public ILValue il_call_function(string name, Symbol A, Symbol B, Symbol C, bool deriv_ptrs = false)
        {
            return il_call_function(name, new Symbol[] { A, B, C }, deriv_ptrs);
        }

        public ILValue il_test_nonzero(Symbol val, bool test_derivs = false)
        {
            TypeSpec ts = val.typespec();
            TypeDesc t = ts.simpletype();
            if (t == TypeDescs.TypeInt)
            {
                return ll.op_ne(il_load_value(val), ll.constant(0));
            }
            int ncomps = t.aggregate;
            int nderivs = (test_derivs && val.has_derivs()) ? 3 : 1;
            ILValue isnonzero = null;
            for (int d = 0; d < nderivs; ++d)
            {
                for (int c = 0; c < ncomps; ++c)
                {
                    ILValue v = il_load_value(val, d, c);
                    ILValue nz = ll.op_ne(v, ll.constant(0.0f), true);
                    if (isnonzero != null)
                    {
                        isnonzero = ll.op_or(nz, isnonzero);
                    }
                    else
                    {
                        isnonzero = nz;
                    }
                }
            }
            return isnonzero;
        }

        public bool il_assign_impl(Symbol Result, Symbol Src, int arrayindex = -1, int srccomp = -1, int dstcomp = -1)
        {
            TypeSpec result_t = Result.typespec();
            TypeSpec src_t = Src.typespec();
            ILValue arrind = arrayindex >= 0 ? ll.constant(arrayindex) : null;
            if (Result.typespec().is_closure() || Src.typespec().is_closure())
            {
                if (Src.typespec().is_closure())
                {
                    ILValue srcval = il_load_value(Src, 0, arrind, 0);
                    il_store_value(srcval, Result, 0, arrind, 0);
                }
                else
                {
                    ILValue nul = ll.void_ptr_null();
                    il_store_value(nul, Result, 0, arrind, 0);
                }
                return true;
            }
            if (Result.typespec().is_matrix() && Src.typespec().is_int_or_float())
            {
                ILValue src = il_load_value(Src, 0, arrind, 0, TypeDescs.TypeFloat);
                ILValue zero = ll.constant(0.0f);
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        il_store_value(i == j ? src : zero, Result, 0, arrind, i * 4 + j);
                    }
                }
                il_zero_derivs(Result);
                return true;
            }
            if (result_t.is_array() && !Src.is_constant() && src_t.is_array() && arrayindex == -1)
            {
                ILValue resultptr = il_get_pointer(Result);
                ILValue srcptr = il_get_pointer(Src);
                int len = Math.Min(Result.size(), Src.size());
                int align = result_t.is_closure_based() ? sizeof(void*) : result_t.simpletype().basesize();
                if (Result.has_derivs() && Src.has_derivs())
                {
                    ll.op_memcpy(resultptr, srcptr, 3 * len, align);
                }
                else
                {
                    ll.op_memcpy(resultptr, srcptr, len, align);
                    if (Result.has_derivs())
                    {
                        il_zero_derivs(Result);
                    }
                }
                return true;
            }
            TypeDesc rt = Result.typespec().simpletype();
            TypeDesc basetype = new TypeDesc(rt.basetype);
            int num_components = rt.aggregate;
            bool singlechan = (srccomp != -1) || (dstcomp != -1);
            if (!singlechan)
            {
                if (rt.is_array() && arrayindex == -1)
                {
                    int num_elements = Math.Min(rt.numelements(), src_t.simpletype().numelements());
                    for (int a = 0; a < num_elements; ++a)
                    {
                        ILValue const_arrind = ll.constant(a);
                        for (int i = 0; i < num_components; ++i)
                        {
                            ILValue src_val = Src.is_constant()
                                                  ? il_load_constant_value(Src, a, i, basetype)
                                                  : il_load_value(Src, 0, Src.typespec().is_array() ? const_arrind : null,
                                                                  (Src.typespec().aggregate() == 1) ? 0 : i, basetype);
                            if (src_val == null)
                            {
                                return false;
                            }
                            il_store_value(src_val, Result, 0, const_arrind, i);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < num_components; ++i)
                    {
                        ILValue src_val = Src.is_constant() ? il_load_constant_value(Src, arrayindex, i, basetype)
                                                            : il_load_value(Src, 0, arrind, i, basetype);
                        if (src_val == null)
                        {
                            return false;
                        }
                        il_store_value(src_val, Result, 0, arrind, i);
                    }
                }
            }
            else
            {
                if (srccomp == -1)
                {
                    srccomp = 0;
                }
                ILValue src_val = Src.is_constant() ? il_load_constant_value(Src, arrayindex, srccomp, basetype)
                                                    : il_load_value(Src, 0, arrind, srccomp, basetype);
                if (src_val == null)
                {
                    return false;
                }
                if (dstcomp == -1)
                {
                    for (int i = 0; i < num_components; ++i)
                    {
                        il_store_value(src_val, Result, 0, arrind, i);
                    }
                }
                else
                {
                    il_store_value(src_val, Result, 0, arrind, dstcomp);
                }
            }
            if (Result.has_derivs())
            {
                if (Src.has_derivs())
                {
                    if (!singlechan)
                    {
                        for (int d = 1; d <= 2; ++d)
                        {
                            for (int i = 0; i < num_components; ++i)
                            {
                                ILValue val = il_load_value(Src, d, arrind, i);
                                il_store_value(val, Result, d, arrind, i);
                            }
                        }
                    }
                    else
                    {
                        for (int d = 1; d <= 2; ++d)
                        {
                            ILValue val = il_load_value(Src, d, arrind, srccomp);
                            if (dstcomp == -1)
                            {
                                for (int i = 0; i < num_components; ++i)
                                {
                                    il_store_value(val, Result, d, arrind, i);
                                }
                            }
                            else
                            {
                                il_store_value(val, Result, d, arrind, dstcomp);
                            }
                        }
                    }
                }
                else
                {
                    if (dstcomp != -1)
                    {
                        if (Result.has_derivs() && Result.typespec().elementtype().is_float_based())
                        {
                            ll.op_memset(ll.offset_ptr(il_void_ptr(Result, 1), dstcomp * rt.basesize()), 0, rt.basesize(), rt.basesize());
                            ll.op_memset(ll.offset_ptr(il_void_ptr(Result, 2), dstcomp * rt.basesize()), 0, rt.basesize(), rt.basesize());
                        }
                    }
                    else
                    {
                        il_zero_derivs(Result);
                    }
                }
            }
            return true;
        }

        public int find_userdata_index(Symbol sym)
        {
            int userdata_index = -1;
            for (int i = 0, e = group().m_userdata_names.Count; i < e; ++i)
            {
                if (sym.name() == group().m_userdata_names[i] && TypeDesc.equivalent(sym.typespec().simpletype(), group().m_userdata_types[i]))
                {
                    userdata_index = i;
                    break;
                }
            }
            return userdata_index;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Debug helpers (llvm_gen_debug_printf, ...)
        // ------------------------------------------------------------------------------------------------------------------------------------------

        private void il_gen_msg(string func, ustring s)
        {
            ILValue[] valargs = new ILValue[]
            {
                sg_void_ptr(), il_const_hash(s), ll.constant(0), ll.void_ptr_null(), ll.constant(0), ll.void_ptr_null()
            };
            ll.call_function(func, valargs);
        }

        public void il_gen_debug_printf(string message)
        {
            il_gen_msg("osl_gen_printfmt", new ustring(Fmt.format("({} {}) {}\n", inst().shadername(), inst().layername(), message)));
        }

        public void il_gen_warning(string message)
        {
            il_gen_msg("osl_gen_warningfmt", new ustring(message + "\n"));
        }

        public void il_gen_error(string message)
        {
            il_gen_msg("osl_gen_errorfmt", new ustring(message + "\n"));
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Calling other layers
        // ------------------------------------------------------------------------------------------------------------------------------------------

        public void il_call_layer(int layer, bool unconditional = false)
        {
            ILValue[] args = new ILValue[] { sg_ptr(), groupdata_ptr(), userdata_base_ptr(), output_base_ptr(), shadeindex(), m_il_interactive_params_ptr };
            ShaderInstance parent = group()[layer];
            ILValue trueval = ll.constant_bool(true);
            ILValue layerfield = layer_run_ref(layer_remap(layer));
            ILLabel then_block = null;
            ILLabel after_block = null;
            if (!unconditional)
            {
                ILValue executed = ll.op_load(ILType.I1, layerfield);
                executed = ll.op_ne(executed, trueval);
                then_block = ll.new_basic_block("");
                after_block = ll.new_basic_block("");
                ll.op_branch(executed, then_block, after_block);
            }
            ll.call_function(m_functions[LayerNames.layer_function_name(group(), parent)], args);
            if (!unconditional)
            {
                ll.op_branch(after_block);
            }
            shadingsys().m_stat_call_layers_inserted++;
        }

        public void il_run_connected_layers(Symbol sym, int symindex, int opnum = -1, HashSet<int> already_run = null)
        {
            if (sym.valuesource() != Symbol.ValueSource.ConnectedVal)
            {
                return;
            }
            bool inmain = (opnum >= inst().maincodebegin() && opnum < inst().maincodeend());
            for (int c = 0; c < inst().nconnections(); ++c)
            {
                Connection con = inst().connection(c);
                if (con.dst.param == symindex)
                {
                    if (!group()[con.srclayer].run_lazily())
                    {
                        continue;
                    }
                    if (already_run != null)
                    {
                        if (already_run.Contains(con.srclayer))
                        {
                            continue;
                        }
                        else
                        {
                            already_run.Add(con.srclayer);
                        }
                    }
                    if (inmain)
                    {
                        if (m_layers_already_run.Contains(con.srclayer))
                        {
                            continue;
                        }
                        if (!m_in_conditional[opnum])
                        {
                            m_layers_already_run.Add(con.srclayer);
                        }
                    }
                    if (shadingsys().m_opt_useparam && inmain)
                    {
                        CallLayerKey key = new CallLayerKey();
                        key.bblockid = m_bblockids[opnum];
                        key.layerid = con.srclayer;
                        if (m_call_layers_inserted.Contains(key))
                        {
                            continue;
                        }
                        m_call_layers_inserted.Add(key);
                    }
                    il_call_layer(con.srclayer);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Exit block
        // ------------------------------------------------------------------------------------------------------------------------------------------

        public bool il_has_exit_instance_block()
        {
            return m_exit_instance_block != null;
        }

        public ILLabel il_exit_instance_block()
        {
            if (m_exit_instance_block == null)
            {
                m_exit_instance_block = ll.new_basic_block(inst().layername() + "_" + inst().id() + "_exit_");
            }
            return m_exit_instance_block;
        }

        public void generated_texture_call(bool handle)
        {
            shadingsys().m_stat_tex_calls_codegened += 1;
            if (handle)
            {
                shadingsys().m_stat_tex_calls_as_handles += 1;
            }
        }

        public void increment_useparam_ops()
        {
            shadingsys().m_stat_useparam_ops++;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // The group data layout (llvm_type_groupdata)
        // ------------------------------------------------------------------------------------------------------------------------------------------

        private static int round_up(int v, int align)
        {
            return align <= 1 ? v : (v + align - 1) / align * align;
        }

        public void il_type_groupdata()
        {
            if (m_groupdata_layout_done)
            {
                return;
            }
            m_groupdata_layout_done = true;
            m_groupdata_field_offsets.Clear();
            m_groupdata_field_names.Clear();
            int offset = 0;
            int order = 0;
            if (llvm_debug() >= 2)
            {
                Console.WriteLine("Group param struct:");
                Console.WriteLine("  layers run flags: " + m_num_used_layers + " at offset " + offset);
            }
            int sz = (m_num_used_layers + 3) & (~3);
            m_groupdata_field_offsets.Add(offset);
            m_groupdata_field_names.Add("layer_runflags");
            offset += sz;
            ++order;
            int nuserdata = group().m_userdata_names.Count;
            if (nuserdata != 0)
            {
                if (llvm_debug() >= 2)
                {
                    Console.WriteLine("  userdata initialized flags: " + nuserdata + " at offset " + offset + ", field " + order);
                }
                int sz2 = (nuserdata + 3) & (~3);
                m_groupdata_field_offsets.Add(offset);
                m_groupdata_field_names.Add("userdata_init_flags");
                offset += sz2;
                ++order;
                for (int i = 0; i < nuserdata; ++i)
                {
                    TypeDesc type = group().m_userdata_types[i];
                    type.arraylen = type.basetype == TypeDesc.FLOAT ? type.numelements() * 3 : type.numelements();
                    int align = type.basesize();
                    offset = round_up(offset, align);
                    m_groupdata_field_offsets.Add(offset);
                    m_groupdata_field_names.Add(Fmt.format("userdata{}_{}_", i, group().m_userdata_names[i]));
                    if (llvm_debug() >= 2)
                    {
                        Console.WriteLine("  userdata " + group().m_userdata_names[i] + " " + type.c_str() + ", field " + order + ", offset " + offset);
                    }
                    group().m_userdata_offsets[i] = offset;
                    offset += (int)type.size();
                    ++order;
                }
            }
            m_param_order_map.Clear();
            for (int layer = 0; layer < group().nlayers(); ++layer)
            {
                ShaderInstance inst = group()[layer];
                if (inst.unused())
                {
                    continue;
                }
                foreach (Symbol sym in inst.param_range())
                {
                    TypeSpec ts = sym.typespec();
                    if (ts.is_structure())
                    {
                        continue;
                    }
                    if (can_treat_param_as_local(sym))
                    {
                        continue;
                    }
                    int arraylen = Math.Max(1, sym.typespec().arraylength());
                    int derivSize = sym.has_derivs() ? 3 : 1;
                    ts.make_array(arraylen * derivSize);
                    m_groupdata_field_names.Add(Fmt.format("lay{}param_{}_", layer, sym.name()));
                    int align = sym.typespec().is_closure_based() ? sizeof(void*) : sym.typespec().simpletype().basesize();
                    offset = round_up(offset, align);
                    if (llvm_debug() >= 2)
                    {
                        Console.WriteLine("  " + inst.layername() + " (" + inst.id() + ") " + sym.mangled() + " " + ts.c_str() + ", field " + order
                                          + ", size " + derivSize * sym.size() + ", offset " + offset + (sym.interpolated() ? " (interpolated)" : "")
                                          + (sym.interactive() ? " (interactive)" : ""));
                    }
                    sym.dataoffset(offset);
                    m_groupdata_field_offsets.Add(offset);
                    offset += derivSize * sym.size();
                    m_param_order_map[sym] = order;
                    ++order;
                }
            }
            group().llvm_groupdata_size(offset);
            if (llvm_debug() >= 2)
            {
                Console.WriteLine(" Group struct had " + order + " fields, total size " + offset + "\n");
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Constants
        // ------------------------------------------------------------------------------------------------------------------------------------------

        public void il_create_constant(Symbol sym)
        {
            TypeDesc t = sym.typespec().simpletype();
            int num_components = t.aggregate;
            int num_elements = t.numelements();
            int array_len = num_elements * num_components;
            int esize = sym.typespec().is_string_based() ? 8 : 4;
            byte[] bytes = new byte[array_len * esize];
            for (int a = 0; a < num_elements; ++a)
            {
                for (int i = 0; i < num_components; ++i)
                {
                    int linear_index = num_components * a + i;
                    int at = linear_index * esize;
                    if (sym.typespec().is_float_based())
                    {
                        BitConverter.GetBytes(sym.get_float(linear_index)).CopyTo(bytes, at);
                    }
                    else if (sym.typespec().is_int_based())
                    {
                        BitConverter.GetBytes(sym.get_int(linear_index)).CopyTo(bytes, at);
                    }
                    else if (sym.typespec().is_string_based())
                    {
                        ustring s = sym.get_string(linear_index);
                        ll.constant(s);
                        BitConverter.GetBytes(s.hash()).CopyTo(bytes, at);
                    }
                    else
                    {
                        throw new ILGenException("unhandled type");
                    }
                }
            }
            string unique_symname = global_unique_symname(sym);
            ILValue global_var = ll.create_global_constant(bytes, unique_symname);
            m_const_map[unique_symname] = global_var;
        }
    }
}
