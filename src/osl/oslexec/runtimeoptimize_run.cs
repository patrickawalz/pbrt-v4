// OSL port: liboslexec/runtimeoptimize.cpp (the second half): isconnected resolution, variable lifetimes and dependencies (derivatives),
// temporary coalescing, post-optimization cleanup, collapsing of the symbols and ops, printing/serializing an instance, run() and the policing of
// failed optimizations. See runtimeoptimize.cs for the first half and the notes on the differences from the C++.

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using static OIIO.TypeDescs;

namespace OSL.pvt
{
    public unsafe partial class RuntimeOptimizer
    {
        // Fake symbol index for "derivatives" entry in dependency map.
        private const int DerivSym = -1;

        /// Replace every 'isconnected' op by the constant that answers it.
        public void resolve_isconnected()
        {
            foreach (Opcode op in inst().ops())
            {
                if (op.opname() == u_isconnected)
                {
                    inst().make_symbol_room(1);
                    Symbol s = inst().argsymbol(op.firstarg() + 1);
                    StructSpec structspec;
                    while ((structspec = s.typespec().structspec()) != null)
                    {
                        // How to deal with structures -- just change the reference to the first field in the struct.
                        // FIXME -- if we ever allow separate layer connection of individual struct members, this will need something more
                        // sophisticated.
                        System.Diagnostics.Debug.Assert(structspec != null && structspec.numfields() >= 1);
                        string fieldname = s.name().@string() + "." + structspec.field(0).name.@string();
                        int fieldsymid = inst().findparam(new ustring(fieldname));
                        System.Diagnostics.Debug.Assert(fieldsymid >= 0);
                        s = inst().symbol(fieldsymid);
                    }
                    bool upconnected = s.connected();
                    if (s.interpolated() && shadingsys().userdata_isconnected())
                    {
                        upconnected = true;
                    }
                    bool downconnected = s.connected_down() || s.renderer_output();
                    int val = (upconnected ? 1 : 0) + (downconnected ? 2 : 0);
                    turn_into_assign(op, add_constant(val), "resolve isconnected()");
                }
            }
        }

        public void track_variable_lifetimes(List<Symbol> allsymptrs)
        {
            List<Symbol> oparg_ptrs = new List<Symbol>(inst().args().Count);
            foreach (int a in inst().args())
            {
                oparg_ptrs.Add(inst().symbol(a));
            }

            if (m_bblockids.Count != inst().ops().Count)
            {
                find_basic_blocks();
            }

            SymtabFunctions.track_variable_lifetimes_main(inst().ops(), oparg_ptrs, allsymptrs, m_bblockids);
        }

        public void track_variable_lifetimes()
        {
            List<Symbol> allsymptrs = new List<Symbol>(inst().symbols().Count);
            foreach (Symbol s in inst().symbols())
            {
                allsymptrs.Add(s);
            }

            track_variable_lifetimes(allsymptrs);
        }

        // Add to the dependency map that "symbol A depends on symbol B".
        public void add_dependency(Dictionary<int, HashSet<int>> dmap, int A, int B)
        {
            System.Diagnostics.Debug.Assert(A < inst().symbols().Count);
            System.Diagnostics.Debug.Assert(B < inst().symbols().Count);
            HashSet<int> deps;
            if (!dmap.TryGetValue(A, out deps))
            {
                deps = new HashSet<int>();
                dmap[A] = deps;
            }
            deps.Add(B);
        }

        public void syms_used_in_op(Opcode op, List<int> rsyms, List<int> wsyms)
        {
            rsyms.Clear();
            wsyms.Clear();
            for (int i = 0; i < op.nargs(); ++i)
            {
                int arg = inst().arg(i + op.firstarg());
                if (op.argread(i))
                {
                    if (rsyms.IndexOf(arg) < 0)
                    {
                        rsyms.Add(arg);
                    }
                }
                if (op.argwrite(i))
                {
                    if (wsyms.IndexOf(arg) < 0)
                    {
                        wsyms.Add(arg);
                    }
                }
            }
        }

        // Recursively mark symbols that have derivatives from dependency map
        public void mark_symbol_derivatives(Dictionary<int, HashSet<int>> symdeps, HashSet<int> visited, int d)
        {
            HashSet<int> deps;
            if (!symdeps.TryGetValue(d, out deps))
            {
                return;
            }
            // (the C++ iterates the set while recursing into other entries of the map; these sets are not modified here)
            foreach (int r in deps)
            {
                if (!visited.Contains(r))
                {
                    visited.Add(r);

                    Symbol s = inst().symbol(r);

                    if (s.typespec().elementtype().is_float_based())
                    {
                        s.has_derivs(true);
                    }

                    mark_symbol_derivatives(symdeps, visited, r);
                }
            }
        }

        // Is it one of the globals that are allowed to have derivatives?
        private static bool global_can_have_derivs(Symbol s)
        {
            string m = s.mangled();
            return m == Strings.P.@string() || m == Strings.I.@string() || m == Strings.u.@string() || m == Strings.v.@string()
                   || m == Strings.Ps.@string();
        }

        /// Run through all the ops, for each one marking its 'written' arguments as dependent upon its 'read' arguments (and performing unification
        /// as we go), yielding a dependency map that lets us look up any symbol and see the set of other symbols on which it ever depends on during
        /// execution of the shader.
        public void track_variable_dependencies()
        {
            Dictionary<int, HashSet<int>> symdeps = new Dictionary<int, HashSet<int>>();

            // It's important to note that this is simplistically conservative in that it overestimates dependencies. To see why this is the case,
            // consider the following code:
            //       // inputs a,b; outputs x,y; local variable t
            //       t = a;
            //       x = t;
            //       t = b;
            //       y = t;
            // We can see that x depends on a and y depends on b. But the dependency analysis we do below thinks that y also depends on a (because t
            // depended on both a and b, but at different times).
            //
            // This naivite will never miss a dependency, but it may overestimate dependencies. (Hence we call this "conservative" rather than
            // "wrong.") We deem this acceptable for now, since it's so much easier to implement the conservative dependency analysis, and it's not
            // yet clear that getting it closer to optimal will have any performance impact on final shaders. Also because this is probably no
            // worse than the "dependency slop" that would happen with loops and conditionals. But we certainly may revisit with a more
            // sophisticated algorithm if this crops up a legitimate issue.
            //
            // Because of this conservative approach, it is critical that this analysis is done BEFORE temporaries are coalesced (which would cause
            // them to be reassigned in exactly the way that confuses this analysis).

            List<int> read = new List<int>();
            List<int> written = new List<int>();
            bool forcederivs = shadingsys().force_derivs();
            // Loop over all ops...
            foreach (Opcode op in inst().ops())
            {
                // Gather the list of syms read and written by the op. Reuse the vectors defined outside the loop to cut down on malloc/free.
                read.Clear();
                written.Clear();
                syms_used_in_op(op, read, written);

                // FIXME -- special cases here! like if any ops implicitly read or write to globals without them needing to be arguments.

                // For each symbol w written by the op...
                foreach (int w in written)
                {
                    // For each symbol r read by the op, make w depend on r. (Unless r is a constant , in which case it's not necessary.)
                    foreach (int r in read)
                    {
                        if (inst().symbol(r).symtype() != SymType.SymTypeConst)
                        {
                            add_dependency(symdeps, w, r);
                        }
                    }
                    // If the op takes derivs, make the pseudo-symbol DerivSym depend on those arguments.
                    if (op.argtakesderivs_all() != 0 || forcederivs)
                    {
                        for (int a = 0; a < op.nargs(); ++a)
                        {
                            if (op.argtakesderivs(a) || forcederivs)
                            {
                                Symbol s = opargsym(op, a);
                                // Constants can't take derivs
                                if (s.symtype() == SymType.SymTypeConst)
                                {
                                    continue;
                                }
                                // Non-float types can't take derivs
                                if (s.typespec().is_closure() || s.typespec().simpletype().basetype != TypeDesc.FLOAT)
                                {
                                    continue;
                                }
                                // Careful -- not all globals can take derivs
                                if (s.symtype() == SymType.SymTypeGlobal && !global_can_have_derivs(s))
                                {
                                    continue;
                                }
                                add_dependency(symdeps, DerivSym, inst().arg(a + op.firstarg()));
                            }
                        }
                    }
                }
            }

            // Propagate derivative dependencies for any syms already known to need derivs. It's probably marked that way because another layer
            // downstream connects to it and needs derivatives of that connection.
            int snum = 0;
            foreach (Symbol s in inst().symbols())
            {
                // Globals that get written should always provide derivs. Exclude N, since its derivs are unreliable anyway, so no point making it
                // cause the whole disp shader to need derivs.
                if (s.symtype() == SymType.SymTypeGlobal && s.everwritten() && !s.typespec().is_closure_based() && s.mangled() != Strings.N.@string())
                {
                    s.has_derivs(true);
                }
                // If the renderer requests a symbol to be written as an output with derivs, mark them to be generated.
                if (s.renderer_output())
                {
                    SymLocationDesc? symloc = group().find_symloc(s.name(), inst().layername(), SymArena.Outputs);
                    if (symloc.HasValue && symloc.Value.derivs && s.everwritten() && !s.typespec().is_closure_based())
                    {
                        s.has_derivs(true);
                    }
                }
                if (s.has_derivs())
                {
                    add_dependency(symdeps, DerivSym, snum);
                }
                ++snum;
            }

            // Mark all symbols needing derivatives as such
            HashSet<int> visited = new HashSet<int>();
            mark_symbol_derivatives(symdeps, visited, DerivSym);

            // Only some globals are allowed to have derivatives
            foreach (Symbol s in inst().symbols())
            {
                if (s.symtype() == SymType.SymTypeGlobal && !global_can_have_derivs(s))
                {
                    s.has_derivs(false);
                }
            }
        }

        // Is the symbol coalescable?
        private static bool coalescable(Symbol s)
        {
            return s.symtype() == SymType.SymTypeTemp &&    // only coalesce temporaries
                   s.everused() &&                          // only if they're used
                   s.dealias() == s &&                      // only if not already aliased
                   !s.typespec().is_structure() &&          // only if not a struct
                   s.fieldid() < 0;                         //    or a struct field
        }

        /// Coalesce temporaries. During code generation, we make a new temporary EVERY time we need one. Now we examine them all and merge ones of
        /// identical type and non-overlapping lifetimes.
        public void coalesce_temporaries()
        {
            // We keep looping until we can't coalesce any more.
            int ncoalesced = 1;
            while (ncoalesced != 0)
            {
                ncoalesced = 0;  // assume we're done, unless we coalesce something

                // We use a greedy algorithm that loops over each symbol, and then examines all higher-numbered symbols (in order) and tries to
                // merge the first one it can find that doesn't overlap lifetimes. The temps were created as we generated code, so they are already
                // sorted by their "first use". Thus, for any pair t1 and t2 that are merged, it is guaranteed that t2 is the symbol whose first use
                // the earliest of all symbols whose lifetimes do not overlap t1.

                List<Symbol> syms = inst().symbols();
                for (int si = 0; si < syms.Count; ++si)
                {
                    Symbol s = syms[si];
                    // Skip syms that can't be (or don't need to be) coalesced
                    if (!coalescable(s))
                    {
                        continue;
                    }

                    int sfirst = s.firstuse();
                    int slast = s.lastuse();

                    // Loop through every other symbol
                    for (int ti = si + 1; ti < syms.Count; ++ti)
                    {
                        Symbol t = syms[ti];
                        // Coalesce s and t if both syms are coalescable, equivalent types, have nonoverlapping lifetimes, and either both do or
                        // both do not need derivatives.
                        if (coalescable(t) && PvtFunctions.equivalent(s.typespec(), t.typespec()) && s.has_derivs() == t.has_derivs()
                            && (slast < t.firstuse() || sfirst > t.lastuse()) && (s.is_uniform() == t.is_uniform())
                            && (s.forced_llvm_bool() == t.forced_llvm_bool()))
                        {
                            // Make all future t references alias to s
                            t.alias(s);
                            // s gets union of the lifetimes
                            s.union_rw(t.firstread(), t.lastread(), t.firstwrite(), t.lastwrite());
                            sfirst = s.firstuse();
                            slast = s.lastuse();
                            // t gets marked as unused
                            t.clear_rw();
                            ++ncoalesced;
                        }
                    }
                }
            }

            // Since we may have aliased temps, now we need to make sure all symbol refs are dealiased.
            List<int> args = inst().args();
            for (int i = 0; i < args.Count; ++i)
            {
                Symbol s = inst().symbol(args[i]);
                s = s.dealias();
                args[i] = inst().symbolindex(s);
            }
        }

        public void post_optimize_instance()
        {
            inst().evaluate_writes_globals_and_userdata_params();

            if (inst().unused())
            {
                return;  // skip the expensive stuff if we're not used anyway
            }

            List<Symbol> allsymptrs = new List<Symbol>(inst().symbols().Count);
            foreach (Symbol s in inst().symbols())
            {
                allsymptrs.Add(s);
            }

            m_bblockids.Clear();  // Keep insert_code from getting confused
            m_in_conditional.Clear();
            m_in_loop.Clear();

            add_useparam(allsymptrs);

            // (OSL_USE_BATCHED is not set in this port: the batched analysis that would run here is not ported)

            if (optimize() >= 1 && m_opt_coalesce_temps)
            {
                coalesce_temporaries();
            }
        }

        public void collapse_syms()
        {
            //
            // Make a new symbol table that removes all the unused symbols.
            //

            // Mark our params that feed to later layers, so that unused params that aren't needed downstream can be removed.
            mark_outgoing_connections();

            List<Symbol> new_symbols = new List<Symbol>();  // buffer for new symbol table
            List<int> symbol_remap = new List<int>();       // mapping of old sym index to new
            int total_syms = 0;                             // number of new symbols we'll need
            SymNeverUsed never_used = new SymNeverUsed(this, inst());  // handy predicate

            // First, just count how many we need and set up the mapping
            foreach (Symbol s in inst().symbols())
            {
                symbol_remap.Add(total_syms);
                if (!never_used.Invoke(s))
                {
                    ++total_syms;
                }
            }

            // Now make a new table of the right (new) size, and copy the used syms
            new_symbols.Capacity = total_syms;
            foreach (Symbol s in inst().symbols())
            {
                if (!never_used.Invoke(s))
                {
                    new_symbols.Add(s);
                }
            }

            // Remap all the function arguments to the new indices
            List<int> instargs = inst().m_instargs;
            for (int i = 0; i < instargs.Count; ++i)
            {
                instargs[i] = symbol_remap[instargs[i]];
            }

            // Fix our connections from upstream shaders
            List<Connection> conns = inst().m_connections;
            for (int i = 0; i < conns.Count; ++i)
            {
                Connection c = conns[i];
                c.dst.param = symbol_remap[c.dst.param];
                conns[i] = c;
            }

            // Fix downstream connections that reference us
            for (int lay = layer() + 1; lay < group().nlayers(); ++lay)
            {
                List<Connection> dconns = group()[lay].m_connections;
                for (int i = 0; i < dconns.Count; ++i)
                {
                    Connection c = dconns[i];
                    if (c.srclayer == layer())
                    {
                        c.src.param = symbol_remap[c.src.param];
                        dconns[i] = c;
                    }
                }
            }

            // The parameter values are kept per symbol (m_paramdata in the instance), so they have to follow the symbols to their new positions
            if (inst().m_paramdata.Length != 0)
            {
                object[] new_paramdata = new object[inst().m_paramdata.Length];
                for (int i = 0; i < inst().m_paramdata.Length && i < symbol_remap.Count; ++i)
                {
                    if (!never_used.Invoke(inst().symbols()[i]))
                    {
                        new_paramdata[symbol_remap[i]] = inst().m_paramdata[i];
                    }
                }
                inst().m_paramdata = new_paramdata;
            }

            // Swap the new symbol list for the old.
            long oldbytes = inst().m_instsymbols.Count * 200L;
            inst().m_instsymbols = new_symbols;
            {
                // adjust memory stats
                long mem = oldbytes - new_symbols.Count * 200L;
                ShadingSystemImpl ss = shadingsys();
                lock (ss.m_stat_mutex)
                {
                    ss.m_stat_mem_inst_syms.sub(mem);
                    ss.m_stat_mem_inst.sub(mem);
                    ss.m_stat_memory.sub(mem);
                }
            }

            // Miscellaneous cleanup of other things that used symbol indices
            inst().m_Psym = -1;
            inst().m_Nsym = -1;
            inst().m_firstparam = -1;
            inst().m_lastparam = -1;
            int idx = 0;
            foreach (Symbol s in inst().symbols())
            {
                if (s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam)
                {
                    if (inst().m_firstparam < 0)
                    {
                        inst().m_firstparam = idx;
                    }
                    inst().m_lastparam = idx + 1;
                }
                if (s.name() == Strings.P)
                {
                    inst().m_Psym = idx;
                }
                else if (s.name() == Strings.N)
                {
                    inst().m_Nsym = idx;
                }
                ++idx;
            }
#if DEBUG
            // Confirm that the symbols between [firstparam,lastparam] are all input or output params.
            foreach (Symbol s in inst().param_range())
            {
                System.Diagnostics.Debug.Assert(s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam);
            }
#endif
        }

        public void collapse_ops()
        {
            //
            // Make new code that removes all the nops
            //
            List<Opcode> new_ops = new List<Opcode>();  // buffer for new code
            List<int> op_remap = new List<int>();       // mapping of old opcode indices to new
            int total_ops = 0;                          // number of new ops we'll need

            // First, just count how many we need and set up the mapping
            foreach (Opcode op in inst().ops())
            {
                op_remap.Add(total_ops);
                if (op.opname() != u_nop)
                {
                    ++total_ops;
                }
            }

            // Now make a new table of the right (new) size, copy the used ops, and reset the jump addresses.
            new_ops.Capacity = total_ops;
            foreach (Opcode op in inst().ops())
            {
                if (op.opname() != u_nop)
                {
                    Opcode newop = new Opcode(op);
                    new_ops.Add(newop);
                    for (int i = 0; i < Opcode.max_jumps; ++i)
                    {
                        if (newop.jump(i) >= 0)
                        {
                            newop.jump(i, op_remap[newop.jump(i)]);
                        }
                    }
                }
            }

            // Adjust 'main' code range and init op ranges
            inst().m_maincodebegin = op_remap[inst().m_maincodebegin];
            inst().m_maincodeend = new_ops.Count;
            foreach (Symbol s in inst().param_range())
            {
                if (s.has_init_ops())
                {
                    s.initbegin(op_remap[s.initbegin()]);
                    if (s.initend() < op_remap.Count)
                    {
                        s.initend(op_remap[s.initend()]);
                    }
                    else
                    {
                        s.initend(new_ops.Count);
                    }
                }
            }

            // Swap the new code for the old.
            inst().m_instops = new_ops;

            // These are no longer valid
            m_bblockids.Clear();
            m_in_conditional.Clear();
            m_in_loop.Clear();
        }

        public void printinst(StringBuilder @out)
        {
            @out.Append("Shader ").Append(inst().shadername()).Append("\n");
            @out.Append(inst().unused() ? " UNUSED" : "");
            @out.Append(" connections in=").Append(inst().nconnections());
            @out.Append(" out=").Append(inst().outgoing_connections() ? 1 : 0);
            @out.Append(inst().writes_globals() ? " writes_globals" : "");
            @out.Append(inst().userdata_params() ? " userdata_params" : "");
            @out.Append(inst().run_lazily() ? " run_lazily" : " run_unconditionally");
            @out.Append(inst().outgoing_connections() ? " outgoing_connections" : "");
            @out.Append(inst().renderer_outputs() ? " renderer_outputs" : "");
            @out.Append(inst().writes_globals() ? " writes_globals" : "");
            @out.Append(inst().entry_layer() ? " entry_layer" : "");
            @out.Append(inst().last_layer() ? " last_layer" : "");
            @out.Append("\n");
            @out.Append("  symbols:\n");
            for (int i = 0, e = inst().symbols().Count; i < e; ++i)
            {
                @out.Append(inst().symbol(i).print(256));
            }
            @out.Append("  code:\n");
            for (int i = 0, e = inst().ops().Count; i < e; ++i)
            {
                Opcode op = inst().ops()[i];
                if (i == inst().maincodebegin())
                {
                    @out.Append("(main)\n");
                }
                @out.Append("    ").Append(i).Append(": ").Append(op.opname().@string());
                bool allconst = true;
                for (int a = 0; a < op.nargs(); ++a)
                {
                    Symbol s = inst().argsymbol(op.firstarg() + a);
                    @out.Append(" ").Append(s.name().@string());
                    if (s.symtype() == SymType.SymTypeConst)
                    {
                        @out.Append(" (");
                        @out.Append(s.print_vals(16));
                        @out.Append(")");
                    }
                    if (op.argread(a))
                    {
                        allconst &= s.is_constant();
                    }
                }
                for (int j = 0; j < Opcode.max_jumps; ++j)
                {
                    if (op.jump(j) >= 0)
                    {
                        @out.Append(" ").Append(op.jump(j));
                    }
                }
                @out.Append("\t# ");
                if (op.argtakesderivs_all() != 0)
                {
                    @out.Append(" %derivs(").Append(op.argtakesderivs_all()).Append(") ");
                }
                if (allconst)
                {
                    @out.Append("  CONST");
                }
                if (i == 0 || (i < m_bblockids.Count && bblockid(i) != bblockid(i - 1)))
                {
                    @out.Append("  BBLOCK-START");
                }
                string filename = op.sourcefile().@string();
                int slash = filename.LastIndexOf('/');
                if (slash >= 0)
                {
                    filename = filename.Remove(0, slash + 1);
                }
                if (filename.Length != 0)
                {
                    @out.Append("  (").Append(filename).Append(":").Append(op.sourceline()).Append(")");
                }
                @out.Append("\n");
            }
            if (inst().nconnections() != 0)
            {
                @out.Append("  connections upstream:\n");
                for (int i = 0, e = inst().nconnections(); i < e; ++i)
                {
                    Connection c = inst().connection(i);
                    @out.Append("    ").Append(c.dst.type.c_str()).Append(' ').Append(inst().symbol(c.dst.param).name().@string());
                    if (c.dst.arrayindex >= 0)
                    {
                        @out.Append('[').Append(c.dst.arrayindex).Append(']');
                    }
                    @out.Append(" upconnected from layer ").Append(c.srclayer).Append(' ');
                    ShaderInstance up = group()[c.srclayer];
                    @out.Append("(").Append(up.layername().@string()).Append(") ");
                    @out.Append("    ").Append(c.src.type.c_str()).Append(' ').Append(up.symbol(c.src.param).name().@string());
                    if (c.src.arrayindex >= 0)
                    {
                        @out.Append('[').Append(c.src.arrayindex).Append(']');
                    }
                    @out.Append("\n");
                }
            }
        }

        public string serialize()
        {
            StringBuilder ss = new StringBuilder();
            int nlayers = group().nlayers();
            for (int lay = 0; lay < nlayers; ++lay)
            {
                set_inst(lay);
                printinst(ss);
            }

            return ss.ToString();
        }

        public void run()
        {
            Timer rop_timer = new Timer();
            int nlayers = group().nlayers();
            if (debug() != 0)
            {
                shadingcontext().infofmt("About to optimize shader group {} ({} layers):", group().name(), nlayers);
            }
            if (debug() != 0)
            {
                Console.Out.Write("About to optimize shader group " + group().name() + "\n");
            }

            for (int lay = 0; lay < nlayers; ++lay)
            {
                set_inst(lay);
                // These need to happen before merge_instances
                inst().copy_code_from_master(group());
                mark_outgoing_connections();
            }

            // Inventory the network and print pre-optimized debug info
            long old_nsyms = 0, old_nops = 0;
            for (int lay = 0; lay < nlayers; ++lay)
            {
                set_inst(lay);
                if (debug() != 0 /* && optimize() >= 1*/)
                {
                    find_basic_blocks();
                    Console.Out.Flush();
                    StringBuilder sb = new StringBuilder();
                    sb.Append("Before optimizing layer ").Append(lay).Append(" \"").Append(inst().layername().@string()).Append("\" (ID ")
                      .Append(inst().id()).Append(") :\n");
                    printinst(sb);
                    sb.Append("\n--------------------------------\n");
                    Console.Out.WriteLine(sb.ToString());
                }
                old_nsyms += inst().symbols().Count;
                old_nops += inst().ops().Count;
            }

            // Clear messages sent for the group, they will be filled in by optimize_instance().
            m_messages_sent.Clear();
            m_unknown_message_sent = false;

            if (shadingsys().m_opt_merge_instances == 1)
            {
                shadingsys().merge_instances(group());
            }

            m_params_holding_globals.Clear();
            for (int i = 0; i < nlayers; ++i)
            {
                m_params_holding_globals.Add(new Dictionary<ustring, ustring>());
            }

            // Inventory for error calls so that if lazyerror=0 we don't incorrectly assume the layer is unused.
            check_for_error_calls(false);

            // Optimize each layer, from first to last
            for (int lay = 0; lay < nlayers; ++lay)
            {
                set_inst(lay);
                if (inst().unused())
                {
                    continue;
                }
                // N.B. we need to resolve isconnected() calls before the instance is otherwise optimized, or else isconnected() may not reflect the
                // original connectivity after substitutions are made.
                resolve_isconnected();
                optimize_instance();
            }
            check_for_error_calls(false);  // re-check

            // Optimize each layer again, from last to first (because some optimizations are only apparent when the subsequent shaders have been
            // simplified).
            for (int lay = nlayers - 1; lay >= 0; --lay)
            {
                set_inst(lay);
                if (!inst().unused())
                {
                    optimize_instance();
                }
            }

            // Try merging instances again, now that we've optimized
            shadingsys().merge_instances(group(), true);

            for (int lay = nlayers - 1; lay >= 0; --lay)
            {
                set_inst(lay);
                if (inst().unused())
                {
                    continue;
                }
                find_basic_blocks();
                track_variable_dependencies();

                // For our parameters that require derivatives, mark their upstream connections as also needing derivatives.
                foreach (Connection c in inst().m_connections)
                {
                    if (inst().symbol(c.dst.param).has_derivs())
                    {
                        Symbol source = group()[c.srclayer].symbol(c.src.param);
                        if (source.typespec().elementtype().is_float_based())
                        {
                            source.has_derivs(true);
                        }
                    }
                }
            }

            // Post-opt cleanup: add useparam, coalesce temporaries, etc.
            for (int lay = 0; lay < nlayers; ++lay)
            {
                set_inst(lay);
                post_optimize_instance();
            }

            // Last chance to eliminate duplicate instances
            shadingsys().merge_instances(group(), true);

            // Last inventory of error() calls, issue warnings if needed.
            check_for_error_calls(true);

            // Get rid of nop instructions and unused symbols.
            long new_nsyms = 0, new_nops = 0, new_deriv_syms = 0;
            for (int lay = 0; lay < nlayers; ++lay)
            {
                set_inst(lay);
                if (inst().unused())
                {
                    continue;  // no need to print or gather stats for unused layers
                }
                if (optimize() >= 1)
                {
                    collapse_syms();
                    collapse_ops();
                }
                if (debug() != 0 && !inst().unused())
                {
                    track_variable_lifetimes();
                    StringBuilder sb = new StringBuilder();
                    sb.Append("After optimizing layer ").Append(lay).Append(" \"").Append(inst().layername().@string()).Append("\" (ID ")
                      .Append(inst().id()).Append(") :\n");
                    printinst(sb);
                    sb.Append("\n--------------------------------\n");
                    Console.Out.WriteLine(sb.ToString());
                }
                new_nsyms += inst().symbols().Count;
                new_nops += inst().ops().Count;
            }

            m_unknown_textures_needed = false;
            m_unknown_closures_needed = false;
            m_unknown_attributes_needed = false;
            m_textures_needed.Clear();
            m_closures_needed.Clear();
            m_globals_read = 0;
            m_globals_write = 0;
            m_globals_needed.Clear();
            m_userdata_needed.Clear();
            m_attributes_needed.Clear();
            bool does_nothing = true;
            int active_layers = 0;
            int n_texture_ops = 0;
            int n_noise_ops = 0;
            List<byte> interactive_data = new List<byte>();
            for (int lay = 0; lay < nlayers; ++lay)
            {
                set_inst(lay);
                if (inst().unused())
                {
                    continue;  // no need to print or gather stats for unused layers
                }
                ++active_layers;
                foreach (Symbol s in inst().sym_range())
                {
                    // set the layer numbers
                    s.layer(lay);
                    // Find interpolated parameters
                    if ((s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam) && s.interpolated())
                    {
                        // (the data pointer of the C++ names the default value of the parameter, which is a managed array here and cannot be
                        // addressed from a native struct)
                        UserDataNeeded udn = new UserDataNeeded(s.name(), lay, s.typespec().simpletype(), IntPtr.Zero, s.has_derivs());
                        UserDataNeeded found;
                        if (!m_userdata_needed.TryGetValue(udn, out found))
                        {
                            m_userdata_needed.Add(udn);
                        }
                        else if (udn.derivs && !found.derivs)
                        {
                            m_userdata_needed.Remove(found);
                            m_userdata_needed.Add(udn);
                        }
                    }
                    // Find interactive parameters
                    if (s.symtype() == SymType.SymTypeParam && s.interactive())
                    {
                        // Make enough room in interactive_data to accommodate this interactive parameter, including correct alignment.
                        int offset = interactive_data.Count;
                        int typesize = (int)s.typespec().simpletype().size();
                        int totalsize = typesize * (s.has_derivs() ? 3 : 1);
                        int alignment = typesize > 4 ? 8 : 4;
                        offset = (offset + alignment - 1) & ~(alignment - 1);
                        byte[] block = new byte[totalsize];
                        // Copy from the instance value to the interactive block. If the value is a string, copy its hash.
                        if (s.typespec().is_string())
                        {
                            ustring string_data = ((ustring[])s.data())[0];
                            byte[] hashbytes = BitConverter.GetBytes(string_data.hash());
                            Buffer.BlockCopy(hashbytes, 0, block, 0, Math.Min(typesize, hashbytes.Length));
                        }
                        else
                        {
                            Buffer.BlockCopy((Array)s.data(), 0, block, 0, typesize);
                        }
                        while (interactive_data.Count < offset)
                        {
                            interactive_data.Add(0);
                        }
                        interactive_data.AddRange(block);
                        // Make sure the symbol remembers it's stored in the interactive arena with the right offset.
                        group().add_interactive_param(lay, s.name(), offset);
                    }
                    // Track which globals the group needs
                    if (s.symtype() == SymType.SymTypeGlobal)
                    {
                        m_globals_needed.Add(s.name());
                        int bit = (int)ShadingSystem.globals_bit(s.name());
                        if (s.everread())
                        {
                            m_globals_read |= bit;
                        }
                        if (s.everwritten())
                        {
                            m_globals_write |= bit;
                        }
                    }
                    if (s.has_derivs())
                    {
                        ++new_deriv_syms;
                    }
                }
                foreach (Opcode op in inst().ops())
                {
                    OpDescriptor opd = shadingsys().op_descriptor(op.opname());
                    if (opd == null)
                    {
                        continue;
                    }
                    // a non-unused layer with a nontrivial op does something
                    if (op.opname() != Strings.end && op.opname() != Strings.useparam)
                    {
                        does_nothing = false;
                    }
                    // Useparam of a down-connected or renderer output does something
                    if (op.opname() == Strings.useparam)
                    {
                        for (int i = 0, e = op.nargs(); i < e; ++i)
                        {
                            Symbol sym = opargsym(op, i);
                            if (sym.connected_down() || sym.renderer_output())
                            {
                                does_nothing = false;
                            }
                        }
                    }
                    if ((opd.flags & OpDescriptor.Tex) != 0)
                    {
                        ++n_texture_ops;
                        // for all the texture ops, arg 1 is the texture name
                        Symbol sym = opargsym(op, 1);
                        System.Diagnostics.Debug.Assert(sym != null && sym.typespec().is_string());
                        if (sym.is_constant())
                        {
                            m_textures_needed.Add(sym.get_string());
                        }
                        else
                        {
                            m_unknown_textures_needed = true;
                        }
                    }
                    if (op.opname() == Strings.noise || op.opname() == Strings.snoise || op.opname() == Strings.pnoise
                        || op.opname() == Strings.psnoise || op.opname() == Strings.cellnoise || op.opname() == Strings.hashnoise)
                    {
                        ++n_noise_ops;
                    }
                    if (op.opname() == u_closure)
                    {
                        // It's either 'closure result weight name' or 'closure result name'
                        Symbol sym = opargsym(op, 1);  // arg 1 is the closure name
                        if (sym != null && !sym.typespec().is_string())
                        {
                            sym = opargsym(op, 2);
                        }
                        System.Diagnostics.Debug.Assert(sym != null && sym.typespec().is_string());
                        if (sym.is_constant())
                        {
                            ustring closurename = sym.get_string();
                            m_closures_needed.Add(closurename);
                        }
                        else
                        {
                            m_unknown_closures_needed = true;
                        }
                    }
                    else if (op.opname() == u_backfacing)
                    {
                        m_globals_needed.Add(u_backfacing);
                    }
                    else if (op.opname() == u_calculatenormal)
                    {
                        m_globals_needed.Add(u_flipHandedness);
                    }
                    else if (op.opname() == u_getattribute)
                    {
                        Symbol sym1 = opargsym(op, 1);
                        System.Diagnostics.Debug.Assert(sym1 != null && sym1.typespec().is_string());
                        if (sym1.is_constant())
                        {
                            if (op.nargs() == 3)
                            {
                                Symbol sym2 = opargsym(op, 2);
                                // getattribute( attributename, result )
                                insert_attribute_needed(new AttributeNeeded(sym1.get_string(), new ustring(), sym2.typespec().simpletype(),
                                                                            sym2.has_derivs()));
                            }
                            else if (op.nargs() == 4)
                            {
                                Symbol sym2 = opargsym(op, 2);
                                Symbol sym3 = opargsym(op, 3);
                                if (sym2.typespec().is_string())
                                {
                                    // getattribute( scopename, attributename, result )
                                    if (sym2.is_constant())
                                    {
                                        insert_attribute_needed(new AttributeNeeded(sym2.get_string(), sym1.get_string(), sym3.typespec().simpletype(),
                                                                                    sym3.has_derivs()));
                                    }
                                    else
                                    {
                                        m_unknown_attributes_needed = true;
                                    }
                                }
                                else
                                {
                                    // getattribute( attributename, arrayindex, result )
                                    insert_attribute_needed(new AttributeNeeded(sym1.get_string(), new ustring(), sym3.typespec().simpletype(),
                                                                                sym3.has_derivs()));
                                }
                            }
                            else if (op.nargs() == 5)
                            {
                                Symbol sym2 = opargsym(op, 2);
                                Symbol sym4 = opargsym(op, 4);
                                if (sym2.typespec().is_string())
                                {
                                    // getattribute( scopename, attributename, arrayindex, result )
                                    if (sym2.is_constant())
                                    {
                                        insert_attribute_needed(new AttributeNeeded(sym2.get_string(), sym1.get_string(), sym4.typespec().simpletype(),
                                                                                    sym4.has_derivs()));
                                    }
                                    else
                                    {
                                        m_unknown_attributes_needed = true;
                                    }
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.Assert(false);
                            }
                        }
                        else
                        {
                            // sym1 not constant
                            m_unknown_attributes_needed = true;
                        }
                    }
                }
            }
            group().does_nothing(does_nothing);
            group().setup_interactive_arena(interactive_data.ToArray());

            // Compute the maximum depth of the shader network: the length of the longest chain of layer-to-layer connections. Layers are already in
            // topological order, so a single forward pass suffices.
            int[] layer_depth = new int[nlayers];
            int max_network_depth = 0;
            for (int lay = 0; lay < nlayers; ++lay)
            {
                if (group()[lay].unused())
                {
                    continue;
                }
                layer_depth[lay] = 1;
                foreach (Connection c in group()[lay].connections())
                {
                    if (!group()[c.srclayer].unused())
                    {
                        layer_depth[lay] = Math.Max(layer_depth[lay], layer_depth[c.srclayer] + 1);
                    }
                }
                max_network_depth = Math.Max(max_network_depth, layer_depth[lay]);
            }

            group().stat_active_layers(active_layers);
            group().stat_network_depth(max_network_depth);
            group().stat_texture_ops(n_texture_ops);
            group().stat_noise_ops(n_noise_ops);

            m_stat_specialization_time = rop_timer.elapsed();
            {
                // adjust memory stats
                ShadingSystemImpl ss = shadingsys();
                lock (ss.m_stat_mutex)
                {
                    ss.m_stat_preopt_syms += (int)old_nsyms;
                    ss.m_stat_preopt_ops += (int)old_nops;
                    ss.m_stat_postopt_syms += (int)new_nsyms;
                    ss.m_stat_postopt_ops += (int)new_nops;
                    ss.m_stat_syms_with_derivs += (int)new_deriv_syms;
                    if (does_nothing)
                    {
                        ss.m_stat_empty_groups += 1;
                    }
                }
            }
            if (shadingsys().m_compile_report != 0)
            {
                shadingcontext().infofmt("Optimized shader group {}:", group().name());
                shadingcontext().infofmt(" spec {:1.2f}s, New syms {}/{} ({:5.1f}%), ops {}/{} ({:5.1f}%)", m_stat_specialization_time, new_nsyms,
                                         old_nsyms, 100.0 * (double)(new_nsyms - old_nsyms) / (double)old_nsyms, new_nops, old_nops,
                                         100.0 * (double)(new_nops - old_nops) / (double)old_nops);
                shadingcontext().infofmt(" Group active layers: {}", active_layers);
                shadingcontext().infofmt(" Group network connection depth: {}", max_network_depth);
                shadingcontext().infofmt(" Group texture ops: {}", n_texture_ops);
                shadingcontext().infofmt(" Group noise ops: {}", n_noise_ops);
            }
            if (shadingsys().m_compile_report > 1)
            {
                if (does_nothing)
                {
                    shadingcontext().infofmt(" Group does nothing");
                }
                if (m_textures_needed.Count != 0)
                {
                    shadingcontext().infofmt(" Group needs textures:");
                    foreach (ustring f in m_textures_needed)
                    {
                        shadingcontext().infofmt("    {}", f);
                    }
                    if (m_unknown_textures_needed)
                    {
                        shadingcontext().infofmt("    Also may construct texture names on the fly.");
                    }
                }
                if (m_userdata_needed.Count != 0)
                {
                    shadingcontext().infofmt(" Group potentially needs userdata:");
                    foreach (UserDataNeeded f in m_userdata_needed)
                    {
                        shadingcontext().infofmt("    {} {} {}", f.name, f.type, f.derivs ? "(derivs)" : "");
                    }
                }
                if (m_attributes_needed.Count != 0)
                {
                    shadingcontext().infofmt(" Group needs attributes:");
                    foreach (AttributeNeeded f in m_attributes_needed)
                    {
                        shadingcontext().infofmt("    {} {} {}", f.name, f.scope, f.type);
                    }
                    if (m_unknown_attributes_needed)
                    {
                        shadingcontext().infofmt("    Also may construct attribute names on the fly.");
                    }
                }
            }
        }

        // The 'insert' lambda of run(): add the attribute, or replace the one found if it didn't ask for derivatives but this does
        private void insert_attribute_needed(AttributeNeeded attr)
        {
            AttributeNeeded found;
            if (!m_attributes_needed.TryGetValue(attr, out found))
            {
                m_attributes_needed.Add(attr);
            }
            else if (attr.derivs && !found.derivs)
            {
                m_attributes_needed.Remove(found);
                m_attributes_needed.Add(attr);
            }
        }

        public bool police_(int type, Opcode op, string msg)
        {
            if ((type & police_gpu_err) != 0 && shadingsys().m_gpu_opt_error != 0)
            {
                shadingcontext().errorfmt("Optimization error for GPUs:\n  group:  {}\n  layer:  {}\n  source: {}:{}\n  issue:  {}", group().name(),
                                          inst().layername(), op.sourcefile(), op.sourceline(), msg);
                return true;
            }
            else if ((type & police_opt_warn) != 0 && shadingsys().m_opt_warnings != 0)
            {
                shadingcontext().warningfmt("Optimization warning:\n  group:  {}\n  layer:  {}\n  source: {}:{}\n  issue:  {}", group().name(),
                                            inst().layername(), op.sourcefile(), op.sourceline(), msg);
            }
            return false;
        }

        public bool police(int type, Opcode op, string fmt, params object[] args)
        {
            return police_(type, op, Fmt.format(fmt, args));
        }

        public bool police(Opcode op, string fmt, params object[] args)
        {
            return police_(police_opt_warn, op, Fmt.format(fmt, args));
        }

        public bool check_for_error_calls(bool warn)
        {
            // If the "lazyerror" option is set, there's nothing to do.
            if (shadingsys().m_lazyerror)
            {
                return false;
            }

            // Check all the layers (even ones we think are unused) for `error()` calls that still remain after runtime optimization. If found,
            // warn and mark the layer as having error calls.
            bool err = false;
            int nlayers = group().nlayers();
            for (int lay = 0; lay < nlayers; ++lay)
            {
                set_inst(lay);
                inst().has_error_op(false);
                foreach (Opcode op in inst().ops())
                {
                    if (op.opname() == Strings.error)
                    {
                        inst().has_error_op(true);
                        if (warn)
                        {
                            err |= police(op, "error() call present in optimized shader.");
                        }
                    }
                }
            }
            return err;
        }

        public bool police_failed_optimizations()
        {
            bool err = false;
            bool do_warn = shadingsys().m_opt_warnings != 0;
            bool do_gpu_err = shadingsys().m_gpu_opt_error != 0;
            if (!do_warn && !do_gpu_err)
            {
                return false;  // no need for any of this expense
            }

            int nlayers = group().nlayers();
            for (int lay = 0; lay < nlayers; ++lay)
            {
                set_inst(lay);
                if (inst().unused())
                {
                    continue;  // no need to print or gather stats for unused layers
                }
                foreach (Opcode op in inst().ops())
                {
                    OpDescriptor opd = shadingsys().op_descriptor(op.opname());
                    if (opd == null)
                    {
                        continue;
                    }
                    if ((opd.flags & OpDescriptor.Police) == 0)
                    {
                        continue;
                    }
                    if ((opd.flags & OpDescriptor.Tex) != 0)
                    {
                        Symbol sym = opargsym(op, 1);  // arg 1 is texture name
                        System.Diagnostics.Debug.Assert(sym != null && sym.typespec().is_string());
                        if (!sym.is_constant())
                        {
                            err |= police(police_gpu_err, op, "{}(): texture name cannot be converted to a handle.", op.opname());
                        }
                    }
                    if ((opd.flags & OpDescriptor.StrChars) != 0)
                    {
                        err |= police(op, "{}(): need for string characters couldn't be optimized away.", op.opname());
                    }
                    if ((opd.flags & OpDescriptor.StrCreate) != 0)
                    {
                        err |= police(op, "{}(): new string creation couldn't be optimized to a constant.", op.opname());
                    }
                    // FIXME: Will add more tests and warnings as we go
                }
            }
            return err;
        }
    }
}
