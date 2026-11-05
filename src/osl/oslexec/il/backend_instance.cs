// OSL port: llvm_instance.cpp (second half) -- building the init function, the layer functions and the group: BackendLLVM::build_llvm_code,
// build_llvm_init, build_llvm_instance, llvm_assign_initial_value, the debug checks and run().
//
// Not ported (they exist only for the GPU or for the LLVM pipeline): OptiX callables, bitcode/PTX output, the optimization passes, the free-function
// renderer services bitcode, the interpolated getter specs of the renderer (build_interpolated_getter, a GPU feature).

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using OIIO;
using OSL;
using static OSL.pvt.PvtFunctions;

namespace OSL.pvt
{
    public sealed unsafe partial class BackendIL
    {
        private static readonly ustring op_end = new ustring("end");
        private static readonly ustring op_nop = new ustring("nop");
        private static readonly ustring op_aassign = new ustring("aassign");
        private static readonly ustring op_compassign = new ustring("compassign");
        private static readonly ustring op_mxcompassign = new ustring("mxcompassign");
        private static readonly ustring op_aref = new ustring("aref");
        private static readonly ustring op_compref = new ustring("compref");
        private static readonly ustring op_mxcompref = new ustring("mxcompref");
        private static readonly ustring op_useparam = new ustring("useparam");
        private static readonly ustring op_pointcloud_get = new ustring("pointcloud_get");
        private static readonly ustring op_spline = new ustring("spline");
        private static readonly ustring op_splineinverse = new ustring("splineinverse");

        public void il_assign_initial_value(Symbol sym, bool force = false)
        {
            // Don't need to assign initial value for connected params: the connection will do it at the proper time
            if (!force && sym.valuesource() == Symbol.ValueSource.ConnectedVal && !sym.typespec().is_closure_based())
            {
                return;
            }
            // Closures need to get their storage before anything can be assigned to them. Unless they are params, in which case we did that at group init time
            if (sym.typespec().is_closure_based() && sym.symtype() == SymType.SymTypeGlobal)
            {
                return;
            }
            if (sym.typespec().is_closure_based() && sym.symtype() != SymType.SymTypeParam && sym.symtype() != SymType.SymTypeOutputParam)
            {
                il_assign_zero(sym);
                return;
            }
            if ((sym.symtype() == SymType.SymTypeLocal || sym.symtype() == SymType.SymTypeTemp) && shadingsys().debug_uninit())
            {
                bool isarray = sym.typespec().is_array();
                int alen = isarray ? sym.typespec().arraylength() : 1;
                ILValue u = null;
                if (sym.typespec().is_closure_based())
                {
                    // Closures are always initialized to null
                }
                else if (sym.typespec().is_float_based())
                {
                    u = ll.constant(float.NaN);
                }
                else if (sym.typespec().is_int_based())
                {
                    u = ll.constant(int.MinValue);
                }
                else if (sym.typespec().is_string_based())
                {
                    u = il_const_hash(Strings.uninitialized_string);
                }
                if (u != null)
                {
                    for (int a = 0; a < alen; ++a)
                    {
                        ILValue aval = isarray ? ll.constant(a) : null;
                        for (int c = 0; c < (int)sym.typespec().aggregate(); ++c)
                        {
                            il_store_value(u, sym, 0, aval, c);
                        }
                    }
                }
                return;
            }
            if ((sym.symtype() == SymType.SymTypeLocal || sym.symtype() == SymType.SymTypeTemp) && sym.typespec().is_string_based())
            {
                il_assign_zero(sym);
                return;
            }
            if (!(sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam))
            {
                throw new ILGenException("il_assign_initial_value: symtype was " + (int)sym.symtype() + ", data type was " + sym.typespec().c_str());
            }
            ILLabel after_userdata_block = null;
            SymLocationDesc? symloc = null;
            if (sym.interpolated() && !sym.typespec().is_closure())
            {
                ustring symname = sym.name();
                TypeDesc type = sym.typespec().simpletype();
                int userdata_index = find_userdata_index(sym);
                ILValue got_userdata = null;
                symloc = group().find_symloc(sym.name(), inst().layername(), SymArena.UserData);
                if (symloc != null)
                {
                    int size = (int)symloc.Value.type.size();
                    if (symloc.Value.derivs && sym.has_derivs())
                    {
                        size *= 3;
                    }
                    ILValue srcptr = symloc_ptr(symloc.Value, m_il_userdata_base_ptr);
                    ILValue dstptr = il_void_ptr(sym);
                    ll.op_memcpy(dstptr, srcptr, size);
                    if (sym.has_derivs() && !symloc.Value.derivs)
                    {
                        ll.op_memset(ll.offset_ptr(dstptr, size), 0, 2 * size);
                    }
                }
                else
                {
                    ILValue[] args = new ILValue[]
                    {
                        sg_void_ptr(),
                        il_const_hash(symname),
                        ll.constant(type),
                        ll.constant((int)group().m_userdata_derivs[userdata_index]),
                        groupdata_field_ptr(2 + userdata_index),
                        ll.constant(sym.has_derivs() ? 1 : 0),
                        il_void_ptr(sym),
                        ll.constant(sym.derivsize()),
                        userdata_initialized_ref(userdata_index),
                        ll.constant(userdata_index)
                    };
                    got_userdata = ll.call_function("osl_bind_interpolated_param", args);
                }
                if (shadingsys().debug_nan() && type.basetype == TypeDesc.FLOAT)
                {
                    int ncomps = type.numelements() * type.aggregate;
                    ILValue[] args = new ILValue[]
                    {
                        ll.constant(ncomps), il_void_ptr(sym), ll.constant(sym.has_derivs() ? 1 : 0), sg_void_ptr(),
                        il_const_hash(inst().shadername()), ll.constant(0), il_const_hash(sym.unmangled()), ll.constant(0), ll.constant(ncomps),
                        il_const_hash("<get_userdata>")
                    };
                    ll.call_function("osl_naninf_check", args);
                }
                if (symloc == null)
                {
                    ILLabel no_userdata_block = ll.new_basic_block("no_userdata");
                    after_userdata_block = ll.new_basic_block();
                    ILValue cond_val = ll.op_eq(got_userdata, ll.constant(0));
                    ll.op_branch(cond_val, no_userdata_block, after_userdata_block);
                }
            }
            if (symloc == null)
            {
                if (sym.has_init_ops() && sym.valuesource() == Symbol.ValueSource.DefaultVal)
                {
                    // Handle init ops.
                    build_il_code(sym.initbegin(), sym.initend());
                }
                else
                {
                    // Use default value
                    int num_components = sym.typespec().simpletype().aggregate;
                    TypeSpec elemtype = sym.typespec().elementtype();
                    int arraylen = Math.Max(1, sym.typespec().arraylength());
                    for (int a = 0, c = 0; a < arraylen; ++a)
                    {
                        ILValue arrind = sym.typespec().is_array() ? ll.constant(a) : null;
                        if (sym.typespec().is_closure_based())
                        {
                            continue;
                        }
                        for (int i = 0; i < num_components; ++i, ++c)
                        {
                            ILValue init_val = null;
                            if (elemtype.is_float_based())
                            {
                                init_val = ll.constant(sym.get_float(c));
                            }
                            else if (elemtype.is_string())
                            {
                                init_val = il_const_hash(sym.get_string(c));
                            }
                            else if (elemtype.is_int())
                            {
                                init_val = ll.constant(sym.get_int(c));
                            }
                            il_store_value(init_val, sym, 0, arrind, i);
                        }
                    }
                    if (sym.has_derivs())
                    {
                        il_zero_derivs(sym);
                    }
                }
                if (after_userdata_block != null)
                {
                    ll.op_branch(after_userdata_block);
                }
            }
        }

        public void il_generate_debugnan(Opcode op)
        {
            for (int i = 0; i < op.nargs(); ++i)
            {
                if (!op.argwrite(i))
                {
                    continue;
                }
                Symbol sym = opargsym(op, i);
                TypeDesc t = sym.typespec().simpletype();
                if (t.basetype != TypeDesc.FLOAT)
                {
                    continue;
                }
                ILValue ncomps = ll.constant(t.numelements() * t.aggregate);
                ILValue offset = ll.constant(0);
                ILValue ncheck = ncomps;
                if (op.opname() == op_aassign)
                {
                    ILValue ind = il_load_value(opargsym(op, 1));
                    ILValue agg = ll.constant((int)t.aggregate);
                    offset = t.aggregate == 1 ? ind : ll.op_mul(ind, agg);
                    ncheck = agg;
                }
                else if (op.opname() == op_compassign)
                {
                    ILValue ind = il_load_value(opargsym(op, 1));
                    offset = ind;
                    ncheck = ll.constant(1);
                }
                else if (op.opname() == op_mxcompassign)
                {
                    ILValue row_ind = il_load_value(opargsym(op, 1));
                    ILValue col_ind = il_load_value(opargsym(op, 2));
                    ILValue comp = ll.op_mul(row_ind, ll.constant(4));
                    comp = ll.op_add(comp, col_ind);
                    offset = comp;
                    ncheck = ll.constant(1);
                }
                ILValue[] args = new ILValue[]
                {
                    ncomps, il_void_ptr(sym), ll.constant(sym.has_derivs() ? 1 : 0), sg_void_ptr(), il_const_hash(op.sourcefile()),
                    ll.constant(op.sourceline()), il_const_hash(sym.unmangled()), offset, ncheck, il_const_hash(op.opname())
                };
                ll.call_function("osl_naninf_check", args);
            }
        }

        public void il_generate_debug_uninit(Opcode op)
        {
            if (op.opname() == op_useparam)
            {
                return;
            }
            for (int i = 0; i < op.nargs(); ++i)
            {
                if (!op.argread(i))
                {
                    continue;
                }
                Symbol sym = opargsym(op, i);
                if (sym.typespec().is_closure_based())
                {
                    continue;
                }
                TypeDesc t = sym.typespec().simpletype();
                if (t.basetype != TypeDesc.FLOAT && t.basetype != TypeDesc.INT && t.basetype != TypeDesc.STRING)
                {
                    continue;
                }
                if (op.opname() == Strings.op_for && i == 0)
                {
                    continue;
                }
                if ((op.opname() == Strings.op_dowhile || op.opname() == Strings.op_while) && i == 0)
                {
                    if (op.jump(0) != op.jump(1))
                    {
                        continue;
                    }
                }
                ILValue ncheck = ll.constant(t.numelements() * t.aggregate);
                ILValue offset = ll.constant(0);
                if (op.opname() == op_aref && i == 1)
                {
                    ILValue ind = il_load_value(opargsym(op, 2));
                    ILValue agg = ll.constant((int)t.aggregate);
                    offset = t.aggregate == 1 ? ind : ll.op_mul(ind, agg);
                    ncheck = agg;
                }
                else if (op.opname() == op_compref && i == 1)
                {
                    ILValue ind = il_load_value(opargsym(op, 2));
                    offset = ind;
                    ncheck = ll.constant(1);
                }
                else if (op.opname() == op_mxcompref && i == 1)
                {
                    ILValue row_ind = il_load_value(opargsym(op, 2));
                    ILValue col_ind = il_load_value(opargsym(op, 3));
                    ILValue comp = ll.op_mul(row_ind, ll.constant(4));
                    comp = ll.op_add(comp, col_ind);
                    offset = comp;
                    ncheck = ll.constant(1);
                }
                else if (op.opname() == op_pointcloud_get && i == 2)
                {
                    ncheck = il_load_value(opargsym(op, 3));
                }
                else if ((op.opname() == op_spline || op.opname() == op_splineinverse) && i == 4)
                {
                    bool has_knot_count = (op.nargs() == 5);
                    if (has_knot_count)
                    {
                        ncheck = il_load_value(opargsym(op, 3));
                    }
                }
                ILValue[] args = new ILValue[]
                {
                    ll.constant(t), il_void_ptr(sym), sg_void_ptr(), il_const_hash(op.sourcefile()), ll.constant(op.sourceline()),
                    il_const_hash(group().name()), ll.constant(layer()), il_const_hash(inst().layername()), il_const_hash(inst().shadername()),
                    ll.constant(inst().ops().IndexOf(op)), il_const_hash(op.opname()), ll.constant(i), il_const_hash(sym.unmangled()), offset, ncheck
                };
                ll.call_function("osl_uninit_check", args);
            }
        }

        public void il_generate_debug_op_printf(Opcode op)
        {
            StringBuilder msg = new StringBuilder();
            msg.Append(op.sourcefile().@string()).Append(':').Append(op.sourceline()).Append(' ').Append(op.opname().@string());
            for (int i = 0; i < op.nargs(); ++i)
            {
                msg.Append(' ').Append(opargsym(op, i).mangled());
            }
            il_gen_debug_printf(msg.ToString());
        }

        public bool build_il_code(int beginop, int endop, ILLabel bb = null)
        {
            if (bb != null)
            {
                ll.set_insert_point(bb);
            }
            for (int opnum = beginop; opnum < endop; ++opnum)
            {
                Opcode op = inst().ops()[opnum];
                OpDescriptor opd = shadingsys().op_descriptor(op.opname());
                if (opd != null && opd.ilgen != null)
                {
                    if (shadingsys().debug_uninit())
                    {
                        il_generate_debug_uninit(op);
                    }
                    if (shadingsys().llvm_debug_ops() != 0)
                    {
                        il_generate_debug_op_printf(op);
                    }
                    bool ok = opd.ilgen(this, opnum);
                    if (!ok)
                    {
                        return false;
                    }
                    if (shadingsys().debug_nan() && op.farthest_jump() < 0)
                    {
                        il_generate_debugnan(op);
                    }
                }
                else if (op.opname() == op_nop || op.opname() == op_end)
                {
                    // Skip this op, it does nothing...
                }
                else
                {
                    shadingcontext().errorfmt("ILOSL: Unsupported op {} in layer {}\n", op.opname(), inst().layername());
                    return false;
                }
                int next = op.farthest_jump();
                if (next >= 0)
                {
                    opnum = next - 1;
                }
            }
            return true;
        }

        private ILType[] group_func_params()
        {
            return new ILType[] { ILType.Ptr, ILType.Ptr, ILType.Ptr, ILType.Ptr, ILType.I4, ILType.Ptr };
        }

        private void set_function_args()
        {
            m_il_shaderglobals_ptr = ll.current_function_arg(0);
            m_il_groupdata_ptr = ll.current_function_arg(1);
            m_il_userdata_base_ptr = ll.current_function_arg(2);
            m_il_output_base_ptr = ll.current_function_arg(3);
            m_il_shadeindex = ll.current_function_arg(4);
            m_il_interactive_params_ptr = ll.current_function_arg(5);
            m_il_temp_texture_options_ptr = null;
            m_il_temp_trace_options_ptr = null;
            m_il_temp_noise_options_ptr = null;
        }

        public ILFunction build_il_init()
        {
            string unique_name = LayerNames.init_function_name(shadingsys(), group());
            ILFunction f = ll.make_function(unique_name, ILType.Void, group_func_params());
            m_functions[unique_name] = f;
            ll.begin_function(f);
            set_function_args();
            // Make a layer_run_flags array
            if (m_num_used_layers > 1)
            {
                int sz = (m_num_used_layers + 3) & (~3);
                ll.op_memset(layer_run_ref(0), 0, sz, 4);
            }
            int num_userdata = group().m_userdata_names.Count;
            if (num_userdata != 0)
            {
                int sz = (num_userdata + 3) & (~3);
                ll.op_memset(userdata_initialized_ref(0), 0, sz, 4);
            }
            // Group init clears all the "layer_run" and "userdata_initialized" flags, and any closure params that need initializing
            for (int i = 0; i < group().nlayers(); ++i)
            {
                ShaderInstance gi = group()[i];
                if (gi.unused() || gi.empty_instance())
                {
                    continue;
                }
                foreach (Symbol sym in gi.param_range())
                {
                    if (sym.typespec().is_closure_based())
                    {
                        int arraylen = Math.Max(1, sym.typespec().arraylength());
                        ILValue val = ll.void_ptr_null();
                        for (int a = 0; a < arraylen; ++a)
                        {
                            ILValue arrind = sym.typespec().is_array() ? ll.constant(a) : null;
                            // The closure params of layer i are in the group data of the layer, so they are stored with the layer's own symbol
                            ILValue ptr = ll.offset_ptr(groupdata_ptr(), sym.dataoffset());
                            if (arrind != null)
                            {
                                ptr = ll.GEP(ptr, arrind, sizeof(void*));
                            }
                            ll.op_store(val, ptr);
                        }
                    }
                }
            }
            ll.op_return();
            if (llvm_debug() != 0)
            {
                Console.WriteLine("group init func (" + unique_name + ") after il  = " + ILUtil.dump(f));
            }
            ll.end_function();
            return f;
        }

        public ILFunction build_il_instance(bool groupentry)
        {
            // Make a layer function: each takes the group data pointer, the userdata base pointer, the output base pointer, the shade index and the interactive params
            string unique_layer_name = LayerNames.layer_function_name(group(), inst());
            bool is_entry_layer = group().is_entry_layer(layer());
            ILFunction f = ll.make_function(unique_layer_name, ILType.Void, group_func_params());
            m_functions[unique_layer_name] = f;
            ll.begin_function(f);
            set_function_args();
            m_exit_instance_block = null;
            m_const_map.Clear();
            // Get the address of the flag telling if this layer has been executed yet.
            ILValue layerfield = layer_run_ref(layer_remap(layer()));
            // For layers that already ran, return immediately: only entry layers, since the layers that are not entry points are only called when needed
            if (is_entry_layer && !group().is_last_layer(layer()))
            {
                if (shadingsys().llvm_debug_layers() != 0)
                {
                    il_gen_debug_printf(Fmt.format("checking for already-run layer {} {} {}", this.layer(), inst().layername(), inst().shadername()));
                }
                ILValue executed = ll.op_eq(ll.op_load(ILType.I1, layerfield), ll.constant_bool(true));
                ILLabel then_block = ll.new_basic_block("");
                ILLabel after_block = ll.new_basic_block("");
                ll.op_branch(executed, then_block, after_block);
                // insert point is now then_block
                if (shadingsys().llvm_debug_layers() != 0)
                {
                    il_gen_debug_printf(Fmt.format("  taking early exit, already executed layer {} {} {}", this.layer(), inst().layername(),
                                                   inst().shadername()));
                }
                ll.op_return();
                ll.set_insert_point(after_block);
            }
            if (shadingsys().llvm_debug_layers() != 0)
            {
                il_gen_debug_printf(Fmt.format("enter layer {} {} {}", this.layer(), inst().layername(), inst().shadername()));
            }
            // Mark this layer as executed
            if (!group().is_last_layer(layer()))
            {
                ll.op_store8(ll.constant(1), layerfield);
                if (shadingsys().countlayerexecs())
                {
                    ll.call_function("osl_incr_layers_executed", sg_void_ptr());
                }
            }
            // Setup the symbolic memory offsets
            m_named_values.Clear();
            m_layers_already_run.Clear();
            foreach (Symbol s in inst().symbols())
            {
                if (s.symtype() == SymType.SymTypeConst)
                {
                    il_create_constant(s);
                    continue;
                }
                if (s.typespec().is_structure())
                {
                    continue;
                }
                // Allocate space for locals, temps, aggregate constants
                if (s.symtype() == SymType.SymTypeLocal || s.symtype() == SymType.SymTypeTemp || s.symtype() == SymType.SymTypeConst
                    || can_treat_param_as_local(s))
                {
                    getOrAllocateILSymbol(s);
                }
                // Set initial value for constants, closures, and strings that are not parameters.
                if (s.symtype() != SymType.SymTypeParam && s.symtype() != SymType.SymTypeOutputParam && s.symtype() != SymType.SymTypeGlobal
                    && (s.is_constant() || s.typespec().is_closure_based() || s.typespec().is_string_based()
                        || ((s.symtype() == SymType.SymTypeLocal || s.symtype() == SymType.SymTypeTemp) && shadingsys().debug_uninit())))
                {
                    il_assign_initial_value(s);
                }
                // If debugnan is turned on, globals check that their values are ok
                if (s.symtype() == SymType.SymTypeGlobal && shadingsys().debug_nan())
                {
                    TypeDesc t = s.typespec().simpletype();
                    if (t.basetype == TypeDesc.FLOAT)
                    {
                        int ncomps = t.numelements() * t.aggregate;
                        ILValue[] args = new ILValue[]
                        {
                            ll.constant(ncomps), il_void_ptr(s), ll.constant(s.has_derivs() ? 1 : 0), sg_void_ptr(),
                            il_const_hash(inst().shadername()), ll.constant(0), il_const_hash(s.unmangled()), ll.constant(0), ll.constant(ncomps),
                            il_const_hash("<none>")
                        };
                        ll.call_function("osl_naninf_check", args);
                    }
                }
            }
            // make a second pass for the parameters (which may make use of locals and constants from the first pass)
            foreach (Symbol s in inst().param_range())
            {
                if (s.typespec().is_structure())
                {
                    continue;
                }
                // Skip if it's never read and isn't connected
                if (!s.everread() && !s.connected_down() && !s.connected() && !s.renderer_output())
                {
                    continue;
                }
                // Skip if it's an interpolated (userdata) parameter and we're initializing them lazily, or if it's an interactive parameter
                if ((s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam) && !s.typespec().is_closure()
                    && !s.connected() && !s.connected_down() && (s.interactive() || (s.interpolated() && shadingsys().lazy_userdata())))
                {
                    continue;
                }
                // Set initial value for params (may contain init ops)
                il_assign_initial_value(s);
            }
            // All the symbols are stack allocated now.
            if (groupentry)
            {
                // Group entry points also need to run any earlier layers that haven't run yet
                for (int i = 0; i < group().nlayers() - 1; ++i)
                {
                    ShaderInstance gi = group()[i];
                    if (!gi.unused() && !gi.empty_instance() && !gi.run_lazily())
                    {
                        il_call_layer(i, true);
                    }
                }
            }
            // Mark all the basic blocks, including entry points for all the code in the layer
            il_find_basic_blocks();
            il_find_conditionals();
            m_call_layers_inserted.Clear();
            build_il_code(inst().maincodebegin(), inst().maincodeend());
            if (il_has_exit_instance_block())
            {
                ll.op_branch(m_exit_instance_block);
            }
            // Transfer all of this layer's outputs into the downstream shader's inputs.
            HashSet<Symbol> initedsyms = new HashSet<Symbol>();
            for (int layer = this.layer() + 1; layer < group().nlayers(); ++layer)
            {
                // Skip unused or empty layers, which won't have a layer function
                if (m_layer_remap[layer] == -1)
                {
                    continue;
                }
                ShaderInstance child = group()[layer];
                for (int c = 0, Nc = child.nconnections(); c < Nc; ++c)
                {
                    Connection con = child.connection(c);
                    if (con.srclayer == this.layer())
                    {
                        Symbol srcsym = inst().symbol(con.src.param);
                        Symbol dstsym = child.symbol(con.dst.param);
                        // Check remaining connections to see if any channels of this aggregate were connected.
                        if (con.dst.channel != -1 && !initedsyms.Contains(dstsym))
                        {
                            initedsyms.Add(dstsym);
                            bool[] inited = new bool[32];
                            int ninit = (int)dstsym.typespec().aggregate() - 1;
                            for (int rc = c + 1; rc < Nc && ninit != 0; ++rc)
                            {
                                Connection next = child.connection(rc);
                                if (next.srclayer == this.layer())
                                {
                                    if (child.symbol(next.dst.param) == dstsym)
                                    {
                                        if (next.dst.channel != -1)
                                        {
                                            if (!inited[next.dst.channel])
                                            {
                                                inited[next.dst.channel] = true;
                                                --ninit;
                                            }
                                        }
                                        else
                                        {
                                            ninit = 0;
                                        }
                                    }
                                }
                            }
                            if (ninit != 0)
                            {
                                il_assign_initial_value(dstsym, true);
                            }
                        }
                        il_run_connected_layers(srcsym, con.src.param);
                        il_assign_impl(dstsym, srcsym, -1, con.src.channel, con.dst.channel);
                    }
                }
            }
            // Copy the renderer outputs to the output arena
            ILValue sindex = null;
            foreach (Symbol s in inst().param_range())
            {
                if (!s.renderer_output())
                {
                    continue;
                }
                SymLocationDesc? symloc = group().find_symloc(s.name(), inst().layername(), SymArena.Outputs);
                if (symloc == null)
                {
                    continue;
                }
                if (!PvtFunctions.equivalent(s.typespec(), new TypeSpec(symloc.Value.type)) || s.typespec().is_closure())
                {
                    Console.WriteLine("No output copy for " + s.typespec().c_str() + " " + s.name() + " because of type mismatch vs symloc="
                                      + symloc.Value.type.c_str());
                    continue;
                }
                int size = (int)symloc.Value.type.size();
                if (symloc.Value.derivs && s.has_derivs())
                {
                    size *= 3;
                }
                ILValue srcptr = il_void_ptr(s);
                if (sindex == null)
                {
                    sindex = ll.op_int_to_longlong(m_il_shadeindex);
                }
                ILValue dstptr = symloc_ptr(symloc.Value, m_il_output_base_ptr, sindex);
                ll.op_memcpy(dstptr, srcptr, size);
                if (symloc.Value.derivs && !s.has_derivs())
                {
                    ll.op_memset(ll.offset_ptr(dstptr, size), 0, 2 * size);
                }
            }
            if (shadingsys().llvm_debug_layers() != 0)
            {
                il_gen_debug_printf(Fmt.format("exit layer {} {} {}", this.layer(), inst().layername(), inst().shadername()));
            }
            ll.op_return();
            if (llvm_debug() != 0)
            {
                Console.WriteLine("layer_func (" + unique_layer_name + ") " + this.layer() + "/" + group().nlayers() + " after il  = " + ILUtil.dump(f));
            }
            ll.end_function();
            return f;
        }

        /// BackendLLVM::run(): generate the init function and the functions of all the layers
        public override void run()
        {
            if (group().does_nothing())
            {
                group().llvm_compiled_init(empty_group_func);
                group().llvm_compiled_version(empty_group_func);
                return;
            }
            int nlayers = group().nlayers();
            m_layer_remap.Clear();
            for (int i = 0; i < nlayers; ++i)
            {
                m_layer_remap.Add(-1);
            }
            m_num_used_layers = 0;
            if (debug() >= 1)
            {
                Console.WriteLine("\nLayers used: (group " + group().name() + ")");
            }
            for (int layer = 0; layer < nlayers; ++layer)
            {
                // Skip unused or empty layers, unless they are callable entry points.
                ShaderInstance inst = group()[layer];
                bool is_single_entry = (layer == (nlayers - 1) && group().num_entry_layers() == 0);
                if (inst.entry_layer() || is_single_entry || (!inst.unused() && !inst.empty_instance()))
                {
                    if (debug() >= 1)
                    {
                        Console.WriteLine("  " + layer + " " + inst.layername());
                    }
                    m_layer_remap[layer] = m_num_used_layers++;
                }
            }
            shadingsys().m_stat_empty_instances += nlayers - m_num_used_layers;
            m_groupdata_layout_done = false;
            il_type_groupdata();
            if (m_layout_only)
            {
                return;
            }
            m_il_local_mem = 0;
            m_init_function = build_il_init();
            ILFunction[] funcs = new ILFunction[nlayers];
            for (int layer = 0; layer < nlayers; ++layer)
            {
                il_set_inst(layer);
                if (m_layer_remap[layer] != -1)
                {
                    bool is_single_entry = (layer == (nlayers - 1) && group().num_entry_layers() == 0);
                    funcs[layer] = build_il_instance(is_single_entry);
                }
            }
            m_layer_functions = new List<ILFunction>(funcs);
            if (shadingsys().m_max_local_mem_KB != 0 && m_il_local_mem / 1024 > shadingsys().m_max_local_mem_KB)
            {
                shadingcontext().errorfmt("Shader group \"{}\" needs too much local storage: {} KB", group().name(), m_il_local_mem / 1024);
            }
        }

        private static unsafe void empty_group_func(void* sg, void* heap, void* userdata, void* output, int shadeindex, void* iparams)
        {
        }

        /// The managed entry points of the generated functions (the funcs[] of run(), after the target emitted them)
        public ILFunction init_function() { return m_init_function; }
        public List<ILFunction> layer_functions() { return m_layer_functions; }
        public ILUtil ilutil() { return ll; }
    }
}
