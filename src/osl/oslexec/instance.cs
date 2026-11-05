// OSL port: liboslexec/instance.cpp (ShaderInstance, ConnectedParam, Connection, ShaderGroup)
//
// Differences from the C++ that follow from the data model (see pvt.cs): the parameter values of an instance are not three pooled vectors but one
// managed array per overridden parameter (m_paramdata[i], int[], float[] or ustring[]); SymOverrideInfo.dataoffset() still counts positions as if they
// were pooled so that it can be compared and reported as the C++ does. Symbols are objects, so "copy the symbols of the master" clones each.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using OIIO;
using static OSL.pvt.PvtFunctions;

namespace OSL.pvt
{
    public sealed partial class ShaderInstance
    {
        private static int next_id = 0;

        /// Clone a symbol (the C++ copies the Symbol objects by value)
        public static Symbol clone_symbol(Symbol s)
        {
            Symbol c = new Symbol(s.name(), s.typespec(), s.symtype(), s.node());
            c.Assign(s);
            return c;
        }

        public ShaderInstance(ShaderMaster master, string layername = null)
        {
            m_master = master;
            m_layername = layername != null ? new ustring(layername) : new ustring("");
            m_writes_globals = false;
            m_outgoing_connections = false;
            m_renderer_outputs = false;
            m_has_error_op = false;
            m_has_trace_op = false;
            m_merged_unused = false;
            m_last_layer = false;
            m_entry_layer = false;
            m_firstparam = m_master.m_firstparam;
            m_lastparam = m_master.m_lastparam;
            m_maincodebegin = m_master.m_maincodebegin;
            m_maincodeend = m_master.m_maincodeend;
            m_id = Interlocked.Increment(ref next_id);
            lock (shadingsys().m_stat_mutex)
            {
                shadingsys().m_stat_instances.incr();
            }
            m_Psym = findsymbol(Strings.P);
            m_Nsym = findsymbol(Strings.N);
            ShadingSystemImpl ss = shadingsys();
            long totalmem = 256;
            lock (ss.m_stat_mutex)
            {
                ss.m_stat_mem_inst.add(totalmem);
                ss.m_stat_memory.add(totalmem);
            }
        }

        ~ShaderInstance()
        {
            try
            {
                ShadingSystemImpl ss = shadingsys();
                lock (ss.m_stat_mutex)
                {
                    ss.m_stat_instances.decr();
                }
            }
            catch (Exception)
            {
            }
        }

        public int findsymbol(ustring name)
        {
            for (int i = 0, e = m_instsymbols.Count; i < e; ++i)
            {
                if (m_instsymbols[i].name() == name)
                {
                    return i;
                }
            }
            if (m_instsymbols.Count == 0)
            {
                return m_master.findsymbol(name);
            }
            return -1;
        }

        public int findparam(ustring name, bool search_master = true)
        {
            if (m_instsymbols.Count != 0)
            {
                for (int i = m_firstparam, e = m_lastparam; i < e; ++i)
                {
                    if (m_instsymbols[i].name() == name)
                    {
                        return i;
                    }
                }
            }
            if (search_master)
            {
                for (int i = m_firstparam, e = m_lastparam; i < e; ++i)
                {
                    if (master().symbol(i).name() == name)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        /// The storage of the value of parameter 'index': its own array if the instance overrides it, else the master's default array
        public object param_storage(int index)
        {
            if (index >= 0 && index < m_paramdata.Length && m_paramdata[index] != null)
            {
                return m_paramdata[index];
            }
            Symbol sym = m_instsymbols.Count != 0 ? symbol(index) : mastersymbol(index);
            if (sym == null)
            {
                return null;
            }
            if (m_instsymbols.Count != 0 && sym.data() != null && (sym.valuesource() == Symbol.ValueSource.InstanceVal))
            {
                return sym.data();
            }
            return m_master.param_default_storage(index);
        }

        private static bool compatible_param(TypeDesc a, TypeDesc b)
        {
            return TypeDesc.equivalent(a, b) || (a.is_vec3() && b == TypeDescs.TypeFloat);
        }

        private static int array_length(object a)
        {
            return a is int[] ia ? ia.Length : a is float[] fa ? fa.Length : a is ustring[] sa ? sa.Length : 0;
        }

        private static object new_array_like(TypeDesc basetype, int n)
        {
            if (basetype.basetype == TypeDesc.INT)
            {
                return new int[n];
            }
            if (basetype.basetype == TypeDesc.FLOAT)
            {
                return new float[n];
            }
            return new ustring[n];
        }

        public void parameters(ParamValueList paramlist, IList<ParamHints> hints)
        {
            m_instoverrides = new SymOverrideInfo[Math.Max(0, lastparam())];
            m_paramdata = new object[m_instoverrides.Length];
            for (int i = 0, e = m_instoverrides.Length; i < e; ++i)
            {
                Symbol sym = master().symbol(i);
                m_instoverrides[i] = SymOverrideInfo.make();
                m_instoverrides[i].interpolated(sym.interpolated());
                m_instoverrides[i].interactive(sym.interactive());
                m_instoverrides[i].dataoffset(sym.dataoffset());
            }
            // Pooled position of the next unsized-array value, to keep dataoffset as the C++ numbers it
            int ni = m_master.m_idefaults.Count, nf = m_master.m_fdefaults.Count, ns = m_master.m_sdefaults.Count;

            for (int pi = 0; pi < paramlist.Count; ++pi)
            {
                ParamValue p = paramlist[pi];
                if (p.name().size() == 0)
                {
                    continue;  // skip empty names
                }
                int i = findparam(p.name());
                if (i >= 0)
                {
                    Symbol sm = master().symbol(i);  // This sym in the master
                    TypeSpec sm_typespec = sm.typespec();
                    if (sm_typespec.is_closure_based())
                    {
                        shadingsys().warningfmt("skipping assignment of closure: {}", sm.name());
                        continue;
                    }
                    if (sm_typespec.is_structure())
                    {
                        continue;  // structs are just placeholders; skip
                    }
                    object data = p.data();
                    TypeDesc paramtype = sm_typespec.simpletype();  // what the shader writer wants
                    TypeDesc valuetype = p.type();                  // what the data provided actually is
                    if (master().shadingsys().relaxed_param_typecheck())
                    {
                        if ((paramtype == TypeDescs.TypeFloat || paramtype.is_vec3()) && valuetype.basetype == TypeDesc.INT && valuetype.basevalues() == 1)
                        {
                            int val = ((int[])p.data())[0];
                            float conv = (float)val;
                            if (val != (int)conv)
                            {
                                shadingsys().errorfmt("attempting to set parameter from wrong type would change the value: {} (set {:.9g} from {})",
                                                      sm.name(), conv, val);
                            }
                            data = new float[] { conv };
                            valuetype = TypeDescs.TypeFloat;
                        }
                        if (!relaxed_equivalent(sm_typespec, new TypeSpec(valuetype)))
                        {
                            shadingsys().errorfmt("attempting to set parameter from incompatible type: {} (expected '{}', received '{}')", sm.name(),
                                                  paramtype, valuetype);
                            continue;
                        }
                    }
                    else if (!compatible_param(paramtype, valuetype))
                    {
                        shadingsys().warningfmt("attempting to set parameter with wrong type: {} (expected '{}', received '{}')", sm.name(), paramtype,
                                                valuetype);
                        continue;
                    }

                    SymOverrideInfo so = m_instoverrides[i];  // Slot for sym's override info (a struct, stored back below)
                    so.valuesource(Symbol.ValueSource.InstanceVal);
                    ParamHints hint = hints[pi];
                    so.interpolated(sm.interpolated() || (hint & ParamHints.interpolated) == ParamHints.interpolated);
                    so.interactive((hint & ParamHints.interactive) == ParamHints.interactive);
                    bool lockgeom = !so.interpolated() && !so.interactive();
                    so.dataoffset(sm.dataoffset());

                    if (paramtype.is_vec3() && valuetype == TypeDescs.TypeFloat)
                    {
                        float f = ((float[])data)[0];
                        data = new float[] { f, f, f };
                        valuetype = paramtype;
                    }

                    int nelements_total = Math.Max(1, valuetype.basevalues());
                    object storage;
                    if (paramtype.arraylen < 0)
                    {
                        // Unsized array: the instance has its own storage of the size given
                        int nelements = valuetype.basevalues();
                        so.arraylen(nelements / paramtype.aggregate);
                        if (paramtype.basetype == TypeDesc.FLOAT)
                        {
                            so.dataoffset(nf);
                            nf += nelements;
                        }
                        else if (paramtype.basetype == TypeDesc.INT)
                        {
                            so.dataoffset(ni);
                            ni += nelements;
                        }
                        else if (paramtype.basetype == TypeDesc.STRING)
                        {
                            so.dataoffset(ns);
                            ns += nelements;
                        }
                        else
                        {
                            throw new InvalidOperationException("unexpected type");
                        }
                        storage = new_array_like(paramtype, nelements);
                    }
                    else
                    {
                        object defaultdata = m_master.param_default_storage(i);
                        storage = clone_array(defaultdata);
                        if (lockgeom && !sm.has_init_ops() && arrays_equal(defaultdata, data, nelements_total))
                        {
                            so.valuesource(Symbol.ValueSource.DefaultVal);
                        }
                    }
                    copy_array(data, storage, nelements_total);
                    m_paramdata[i] = so.valuesource() == Symbol.ValueSource.DefaultVal ? null : storage;
                    m_instoverrides[i] = so;
                }
                else
                {
                    shadingsys().warningfmt("attempting to set nonexistent parameter: {}", p.name());
                }
            }
            ShadingSystemImpl ss = shadingsys();
            long symmem = m_instoverrides.Length * 8L;
            long parammem = 0;
            lock (ss.m_stat_mutex)
            {
                ss.m_stat_mem_inst_syms.add(symmem);
                ss.m_stat_mem_inst_paramvals.add(parammem);
                ss.m_stat_mem_inst.add(symmem + parammem);
                ss.m_stat_memory.add(symmem + parammem);
            }
        }

        public static object clone_array(object a)
        {
            return a == null ? null : ((Array)a).Clone();
        }

        /// memcmp of the first n values (n bounded by both arrays)
        public static bool arrays_equal(object a, object b, int n)
        {
            if (a == null || b == null)
            {
                return false;
            }
            if (a is int[] ia && b is int[] ib)
            {
                n = Math.Min(n, Math.Min(ia.Length, ib.Length));
                for (int i = 0; i < n; ++i)
                {
                    if (ia[i] != ib[i])
                    {
                        return false;
                    }
                }
                return true;
            }
            if (a is float[] fa && b is float[] fb)
            {
                n = Math.Min(n, Math.Min(fa.Length, fb.Length));
                for (int i = 0; i < n; ++i)
                {
                    if (BitConverter.SingleToInt32Bits(fa[i]) != BitConverter.SingleToInt32Bits(fb[i]))
                    {
                        return false;
                    }
                }
                return true;
            }
            if (a is ustring[] sa && b is ustring[] sb)
            {
                n = Math.Min(n, Math.Min(sa.Length, sb.Length));
                for (int i = 0; i < n; ++i)
                {
                    if (sa[i] != sb[i])
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        public static void copy_array(object src, object dst, int n)
        {
            n = Math.Min(n, Math.Min(((Array)src).Length, ((Array)dst).Length));
            Array.Copy((Array)src, (Array)dst, n);
        }

        public void make_symbol_room(int moresyms = 1)
        {
            int oldsize = m_instsymbols.Capacity;
            if (oldsize < m_instsymbols.Count + moresyms)
            {
                const int extra_room = 10;
                int newsize = m_instsymbols.Count + moresyms + extra_room;
                m_instsymbols.Capacity = newsize;
                lock (shadingsys().m_stat_mutex)
                {
                    long mem = (long)(newsize - oldsize) * 200;
                    shadingsys().m_stat_mem_inst_syms.add(mem);
                    shadingsys().m_stat_mem_inst.add(mem);
                    shadingsys().m_stat_memory.add(mem);
                }
            }
        }

        public void add_connection(int srclayer, ConnectedParam srccon, ConnectedParam dstcon)
        {
            if (dstcon.type.is_unsized_array())
            {
                // Make room for the value of the unsized array that gets the connection
                SymOverrideInfo so = m_instoverrides[dstcon.param];
                so.arraylen(srccon.type.arraylength());
                TypeDesc type = srccon.type.simpletype();
                if (type.basetype == TypeDesc.FLOAT)
                {
                    so.dataoffset(m_master.m_fdefaults.Count);
                    m_paramdata[dstcon.param] = new float[type.size() / 4];
                }
                else if (type.basetype == TypeDesc.INT)
                {
                    so.dataoffset(m_master.m_idefaults.Count);
                    m_paramdata[dstcon.param] = new int[type.size() / 4];
                }
                else if (type.basetype == TypeDesc.STRING)
                {
                    so.dataoffset(m_master.m_sdefaults.Count);
                    m_paramdata[dstcon.param] = new ustring[type.size() / 8];
                }
                else
                {
                    throw new InvalidOperationException("unexpected type");
                }
                m_instoverrides[dstcon.param] = so;
            }
            long oldmem = m_connections.Capacity * 40L;
            m_connections.Add(new Connection(srclayer, srccon, dstcon));
            long mem = m_connections.Capacity * 40L - oldmem;
            lock (shadingsys().m_stat_mutex)
            {
                shadingsys().m_stat_mem_inst_connections.add(mem);
                shadingsys().m_stat_mem_inst.add(mem);
                shadingsys().m_stat_memory.add(mem);
            }
        }

        public void evaluate_writes_globals_and_userdata_params()
        {
            writes_globals(false);
            userdata_params(false);
            foreach (Symbol s in symbols())
            {
                if (s.symtype() == SymType.SymTypeGlobal && s.everwritten())
                {
                    writes_globals(true);
                }
                if ((s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam) && !s.lockgeom() && !s.connected())
                {
                    userdata_params(true);
                }
                if (s.symtype() == SymType.SymTypeTemp)  // Once we hit a temp, we'll never
                {
                    break;                               // see another global or param.
                }
            }
            foreach (SymOverrideInfo s in m_instoverrides)
            {
                if (s.interpolated())
                {
                    userdata_params(true);
                }
            }
        }

        public void copy_code_from_master(ShaderGroup group)
        {
            if (m_instops.Count != 0 || m_instargs.Count != 0)
            {
                throw new InvalidOperationException("copy_code_from_master: instance already has code");
            }
            m_instops = new List<Opcode>(master().m_ops.Count + 10);
            foreach (Opcode o in master().m_ops)
            {
                m_instops.Add(new Opcode(o));
            }
            m_instargs = new List<int>(master().m_args);
            if (m_instsymbols.Count != 0)
            {
                throw new InvalidOperationException("should not have copied m_instsymbols yet");
            }
            m_instsymbols = new List<Symbol>(m_master.m_symbols.Count + 10);
            foreach (Symbol s in m_master.m_symbols)
            {
                m_instsymbols.Add(clone_symbol(s));
            }
            if (m_instoverrides.Length != Math.Max(0, lastparam()))
            {
                throw new InvalidOperationException("instoverrides size mismatch");
            }
            if (m_instoverrides.Length != 0)
            {
                for (int i = 0, e = lastparam(); i < e; ++i)
                {
                    Symbol si = m_instsymbols[i];
                    if (m_instoverrides[i].valuesource() == Symbol.ValueSource.DefaultVal)
                    {
                        // Defaults of an unsized array: its length is the number of initializers
                        if (si.typespec().is_unsized_array())
                        {
                            si.arraylen(si.initializers());
                        }
                    }
                    else
                    {
                        if (m_instoverrides[i].arraylen() != 0)
                        {
                            si.arraylen(m_instoverrides[i].arraylen());
                        }
                        si.valuesource(m_instoverrides[i].valuesource());
                        si.connected_down(m_instoverrides[i].connected_down());
                        si.interpolated(m_instoverrides[i].interpolated());
                        si.interactive(m_instoverrides[i].interactive());
                        si.dataoffset(m_instoverrides[i].dataoffset());
                        si.set_dataptr(SymArena.Absolute, param_storage(i));
                    }
                    if (shadingsys().is_renderer_output(layername(), si.name(), group))
                    {
                        si.renderer_output(true);
                        renderer_outputs(true);
                    }
                }
            }
            evaluate_writes_globals_and_userdata_params();
            long symmem = (long)m_instsymbols.Capacity * 200 - m_instoverrides.Length * 8L;
            m_instoverrides = new SymOverrideInfo[0];  // free it
            lock (shadingsys().m_stat_mutex)
            {
                shadingsys().m_stat_mem_inst_syms.add(symmem);
                shadingsys().m_stat_mem_inst.add(symmem);
                shadingsys().m_stat_memory.add(symmem);
            }
        }

        // equivalent() of symbols and ops, used by mergeable
        private static bool equivalent_symbol(Symbol a, Symbol b)
        {
            if (!a.everused() && !b.everused())
            {
                return true;
            }
            if (a.symtype() != b.symtype() || a.typespec() != b.typespec())
            {
                return false;
            }
            if (a.symtype() != SymType.SymTypeTemp && a.symtype() != SymType.SymTypeConst && a.name() != b.name())
            {
                return false;
            }
            if (a.symtype() == SymType.SymTypeConst)
            {
                int n = (int)(a.typespec().simpletype().size() / (a.typespec().simpletype().basetype == TypeDesc.STRING ? 8 : 4));
                if (!arrays_equal(a.data(), b.data(), Math.Max(1, n)))
                {
                    return false;
                }
            }
            return a.has_derivs() == b.has_derivs() && a.lockgeom() == b.lockgeom() && a.valuesource() == b.valuesource() && a.fieldid() == b.fieldid()
                   && a.initbegin() == b.initbegin() && a.initend() == b.initend();
        }

        public bool mergeable(ShaderInstance b, ShaderGroup g)
        {
            // Must call the same master
            if (master() != b.master())
            {
                return false;
            }
            // If either instance has renderer outputs, don't merge
            if (renderer_outputs() || b.renderer_outputs())
            {
                return false;
            }
            bool optimized = (m_instsymbols.Count != 0 || m_instops.Count != 0);

            // Same instance override parameters
            if (m_instoverrides.Length != 0 || b.m_instoverrides.Length != 0)
            {
                if (optimized)
                {
                    throw new InvalidOperationException("mergeable: should not be post-opt");
                }
                for (int i = 0, e = m_instoverrides.Length; i < e; ++i)
                {
                    if ((m_instoverrides[i].valuesource() == Symbol.ValueSource.DefaultVal || m_instoverrides[i].valuesource() == Symbol.ValueSource.InstanceVal)
                        && (b.m_instoverrides[i].valuesource() == Symbol.ValueSource.DefaultVal
                            || b.m_instoverrides[i].valuesource() == Symbol.ValueSource.InstanceVal))
                    {
                        continue;
                    }
                    if (!SymOverrideInfo.equivalent(m_instoverrides[i], b.m_instoverrides[i]))
                    {
                        Symbol sym = mastersymbol(i);
                        Symbol bsym = b.mastersymbol(i);
                        if (!sym.everused_in_group() && !bsym.everused_in_group())
                        {
                            continue;
                        }
                        return false;
                    }
                    if (m_instoverrides[i].interpolated() != b.m_instoverrides[i].interpolated()
                        || m_instoverrides[i].interactive() != b.m_instoverrides[i].interactive())
                    {
                        return false;
                    }
                }
            }

            // Make sure that the two nodes have the same parameter values. If the values are equal, then the symbols are too
            for (int i = firstparam(); i < lastparam(); ++i)
            {
                Symbol sym = optimized ? symbol(i) : mastersymbol(i);
                if (!sym.everused_in_group())
                {
                    continue;
                }
                if (sym.typespec().is_closure())
                {
                    continue;  // Closures can't have instance override values
                }
                Symbol b_sym = optimized ? b.symbol(i) : b.mastersymbol(i);
                if ((sym.valuesource() == Symbol.ValueSource.InstanceVal || sym.valuesource() == Symbol.ValueSource.DefaultVal))
                {
                    TypeDesc t = sym.typespec().simpletype();
                    int n = Math.Max(1, (int)(t.size() / (t.basetype == TypeDesc.STRING ? 8 : 4)));
                    if (!arrays_equal(param_storage(i), b.param_storage(i), n) || b_sym.interactive())
                    {
                        return false;
                    }
                }
            }

            // Make sure that the two nodes have the same connections
            if (run_lazily() != b.run_lazily())
            {
                return false;
            }
            if (m_connections.Count != b.m_connections.Count)
            {
                return false;
            }
            for (int i = 0; i < m_connections.Count; ++i)
            {
                if (m_connections[i] != b.m_connections[i])
                {
                    return false;
                }
            }
            if (!shadingsys().m_opt_merge_instances_with_userdata && (userdata_params() || b.userdata_params()))
            {
                return false;
            }
            if (!optimized)
            {
                return true;
            }

            // Post-optimization: the code, symbols and args must be equivalent
            if (m_instsymbols.Count != b.m_instsymbols.Count)
            {
                return false;
            }
            for (int i = 0; i < m_instsymbols.Count; ++i)
            {
                if (!equivalent_symbol(m_instsymbols[i], b.m_instsymbols[i]))
                {
                    return false;
                }
            }
            if (m_instops.Count != b.m_instops.Count)
            {
                return false;
            }
            for (int i = 0; i < m_instops.Count; ++i)
            {
                if (!Opcode.equivalent(m_instops[i], b.m_instops[i]))
                {
                    return false;
                }
            }
            if (m_instargs.Count != b.m_instargs.Count)
            {
                return false;
            }
            for (int i = 0; i < m_instargs.Count; ++i)
            {
                if (m_instargs[i] != b.m_instargs[i])
                {
                    return false;
                }
            }
            if (m_firstparam != b.m_firstparam || m_lastparam != b.m_lastparam || m_maincodebegin != b.m_maincodebegin || m_maincodeend != b.m_maincodeend
                || m_Psym != b.m_Psym || m_Nsym != b.m_Nsym)
            {
                return false;
            }
            return true;
        }
    }

    public static class ConnectedParamExt
    {
        public static string str(this ConnectedParam c, ShaderInstance inst, bool unmangle)
        {
            Symbol s = inst.symbol(c.param);
            return Fmt.format("{}{}{} ({})", unmangle ? s.unmangled() : s.name().@string(), c.arrayindex >= 0 ? Fmt.format("[{}]", c.arrayindex) : "",
                              c.channel >= 0 ? Fmt.format("[{}]", c.channel) : "", c.type.@string());
        }

        public static string str(this Connection c, ShaderGroup group, ShaderInstance dstinst, bool unmangle = true)
        {
            return Fmt.format("{} -> {}", c.src.str(group[c.srclayer], unmangle), c.dst.str(dstinst, unmangle));
        }
    }
}

namespace OSL
{
    using OSL.pvt;

    public sealed partial class ShaderGroup
    {
        public int find_layer(ustring layername)
        {
            int i;
            for (i = nlayers() - 1; i >= 0 && layer(i).layername() != layername; --i)
            {
            }
            return i;  // will be -1 if we never found a match
        }

        public Symbol find_symbol(ustring layername, ustring symbolname)
        {
            for (int layer = nlayers() - 1; layer >= 0; --layer)
            {
                ShaderInstance inst = m_layers[layer];
                if (layername.size() != 0 && layername != inst.layername())
                {
                    continue;  // They asked for a specific layer and this isn't it
                }
                int symidx = inst.findsymbol(symbolname);
                if (symidx >= 0)
                {
                    return inst.symbol(symidx);
                }
            }
            return null;
        }

        public void clear_entry_layers()
        {
            for (int i = 0; i < nlayers(); ++i)
            {
                m_layers[i].entry_layer(false);
            }
            m_num_entry_layers = 0;
        }

        public void mark_entry_layer(int layer)
        {
            if (layer >= 0 && layer < nlayers() && !m_layers[layer].entry_layer())
            {
                m_layers[layer].entry_layer(true);
                ++m_num_entry_layers;
            }
        }

        public unsafe void setup_interactive_arena(byte[] paramblock)
        {
            if (m_interactive_arena != null)
            {
                System.Runtime.InteropServices.NativeMemory.Free(m_interactive_arena);
                m_interactive_arena = null;
            }
            if (paramblock != null && paramblock.Length != 0)
            {
                m_interactive_arena_size = paramblock.Length;
                m_interactive_arena = (byte*)System.Runtime.InteropServices.NativeMemory.Alloc((nuint)paramblock.Length);
                System.Runtime.InteropServices.Marshal.Copy(paramblock, 0, (IntPtr)m_interactive_arena, paramblock.Length);
            }
            else
            {
                m_interactive_arena_size = 0;
            }
        }

        public void generate_optix_cache_key(string code)
        {
            ulong ir_key = Strutil_hash.strhash64(code);
            string safegroup = Strutil.replace(name().@string(), "/", "_", true);
            safegroup = Strutil.replace(safegroup, ":", "_", true);
            ShaderInstance inst = layer(nlayers() - 1);
            ustring layername = inst.layername();
            m_optix_cache_key = Fmt.format("cache-osl-ptx-{}-{}-{}", safegroup, layername, ir_key);
        }

        /// The group as the text of a group specification (the form ShaderGroupBegin(name, usage, groupspec) reads)
        public string serialize()
        {
            StringBuilder o = new StringBuilder();
            lock (m_mutex)
            {
                for (int i = 0, nl = nlayers(); i < nl; ++i)
                {
                    ShaderInstance inst = m_layers[i];
                    bool dstsyms_exist = inst.symbols().Count != 0;
                    for (int p = 0; p < inst.lastparam(); ++p)
                    {
                        Symbol s = dstsyms_exist ? inst.symbol(p) : inst.mastersymbol(p);
                        if (s == null || (s.symtype() != SymType.SymTypeParam && s.symtype() != SymType.SymTypeOutputParam))
                        {
                            continue;
                        }
                        Symbol.ValueSource vs = dstsyms_exist ? s.valuesource() : inst.instoverride(p).valuesource();
                        if (vs == Symbol.ValueSource.InstanceVal)
                        {
                            TypeDesc type = s.typespec().simpletype();
                            if (type.is_unsized_array() && !dstsyms_exist)
                            {
                                type.arraylen = inst.instoverride(p).arraylen();
                            }
                            o.Append("param ").Append(type.ToString()).Append(' ').Append(s.name().@string());
                            int nvals = type.numelements() * type.aggregate;
                            object storage = inst.param_storage(p);
                            if (type.basetype == TypeDesc.INT)
                            {
                                int[] vals = (int[])storage;
                                for (int k = 0; k < nvals; ++k)
                                {
                                    o.Append(' ').Append(vals[k].ToString(System.Globalization.CultureInfo.InvariantCulture));
                                }
                            }
                            else if (type.basetype == TypeDesc.FLOAT)
                            {
                                float[] vals = (float[])storage;
                                for (int k = 0; k < nvals; ++k)
                                {
                                    o.Append(' ').Append(Strutil.format_g9(vals[k]));
                                }
                            }
                            else if (type.basetype == TypeDesc.STRING)
                            {
                                ustring[] vals = (ustring[])storage;
                                for (int k = 0; k < nvals; ++k)
                                {
                                    o.Append(' ').Append('"').Append(Strutil.escape_chars(vals[k].@string())).Append('"');
                                }
                            }
                            else
                            {
                                throw new InvalidOperationException("unknown type for serialization: " + type.ToString());
                            }
                            if (dstsyms_exist ? s.interpolated() : inst.instoverride(p).interpolated())
                            {
                                o.Append(" [[int interpolated=1]]");
                            }
                            if (dstsyms_exist ? s.interactive() : inst.instoverride(p).interactive())
                            {
                                o.Append(" [[int interactive=1]]");
                            }
                            o.Append(" ;\n");
                        }
                    }
                    o.Append("shader ").Append(inst.shadername()).Append(' ').Append(inst.layername().@string()).Append(" ;\n");
                    for (int c = 0, nc = inst.nconnections(); c < nc; ++c)
                    {
                        Connection con = inst.connection(c);
                        ShaderInstance srclayer = m_layers[con.srclayer];
                        ustring srclayername = srclayer.layername();
                        bool srcsyms_exist = srclayer.symbols().Count != 0;
                        ustring srcparam = srcsyms_exist ? srclayer.symbol(con.src.param).name() : srclayer.mastersymbol(con.src.param).name();
                        ustring dstparam = dstsyms_exist ? inst.symbol(con.dst.param).name() : inst.mastersymbol(con.dst.param).name();
                        o.Append("connect ").Append(srclayername.@string()).Append('.').Append(srcparam.@string()).Append(' ')
                         .Append(inst.layername().@string()).Append('.').Append(dstparam.@string()).Append(" ;\n");
                    }
                }
            }
            return o.ToString();
        }
    }
}
