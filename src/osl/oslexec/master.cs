// OSL port: liboslexec/master.cpp (ShaderMaster)
//
// resolve_syms gives every parameter and constant symbol its own managed array holding its value (the C++ points the symbol into the pooled
// m_idefaults/m_fdefaults/m_sdefaults and m_iconsts/m_fconsts/m_sconsts; those pools are kept and Symbol.dataoffset() is the index into them).

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using OSL;

namespace OSL.pvt
{
    public sealed partial class ShaderMaster
    {
        public ShaderMaster(ShadingSystemImpl shadingsys)
        {
            m_shadingsys = shadingsys;
            m_range_checking = shadingsys.range_checking();
            m_firstparam = -1;
            m_lastparam = -1;
        }

        ~ShaderMaster()
        {
            try
            {
                long totalmem = m_ops.Count * 100L + m_args.Count * 4L + m_symbols.Count * 200L + m_idefaults.Count * 4L + m_fdefaults.Count * 4L
                                + m_sdefaults.Count * 8L + m_iconsts.Count * 4L + m_fconsts.Count * 4L + m_sconsts.Count * 8L;
                lock (m_shadingsys.m_stat_mutex)
                {
                    m_shadingsys.m_stat_mem_master.sub(totalmem);
                    m_shadingsys.m_stat_memory.sub(totalmem);
                }
            }
            catch (Exception)
            {
            }
        }

        public int findsymbol(ustring name)
        {
            for (int i = 0; i < m_symbols.Count; ++i)
            {
                if (m_symbols[i].name() == name)
                {
                    return i;
                }
            }
            return -1;
        }

        /// The managed array that holds the default value of parameter 'index'
        public object param_default_storage(int index)
        {
            return symbol(index).data();
        }

        private static object slice(object pool, int offset, int n)
        {
            if (pool is List<int> li)
            {
                int[] a = new int[n];
                for (int i = 0; i < n && offset + i < li.Count; ++i)
                {
                    a[i] = li[offset + i];
                }
                return a;
            }
            if (pool is List<float> lf)
            {
                float[] a = new float[n];
                for (int i = 0; i < n && offset + i < lf.Count; ++i)
                {
                    a[i] = lf[offset + i];
                }
                return a;
            }
            List<ustring> ls = (List<ustring>)pool;
            ustring[] s = new ustring[n];
            for (int i = 0; i < n; ++i)
            {
                s[i] = offset + i < ls.Count ? ls[offset + i] : new ustring("");
            }
            return s;
        }

        public void resolve_syms()
        {
            // Adjust the sizes of the symbols, and point them to their defaults and constants
            List<Symbol> allsymptrs = new List<Symbol>(m_symbols.Count);
            m_firstparam = -1;
            m_lastparam = -1;
            int i = 0;
            foreach (Symbol s in m_symbols)
            {
                allsymptrs.Add(s);
                if (s.typespec().is_closure())
                {
                    int alen = Math.Max(1, s.typespec().arraylength());
                    s.size(alen * IntPtr.Size);  // heap stores ptrs to closures
                }
                else if (s.typespec().is_structure())
                {
                    s.size(0);
                }
                else if (s.typespec().is_unsized_array())
                {
                    s.size(0);
                }
                else
                {
                    s.size(s.typespec().simpletype().size());
                }
                if (s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam)
                {
                    if (m_firstparam < 0)
                    {
                        m_firstparam = i;
                    }
                    m_lastparam = i + 1;
                    if (s.dataoffset() >= 0)
                    {
                        TypeDesc st = s.typespec().simpletype();
                        // Defaults of an unsized array hold as many values as were given (m_sym_default_index of the reader)
                        int nvals = st.aggregate * (st.is_unsized_array() ? Math.Max(0, s.initializers()) : st.numelements());
                        if (st.basetype == TypeDesc.INT)
                        {
                            s.set_dataptr(SymArena.Absolute, slice(m_idefaults, s.dataoffset(), nvals));
                        }
                        else if (st.basetype == TypeDesc.FLOAT)
                        {
                            s.set_dataptr(SymArena.Absolute, slice(m_fdefaults, s.dataoffset(), nvals));
                        }
                        else if (st.basetype == TypeDesc.STRING)
                        {
                            s.set_dataptr(SymArena.Absolute, slice(m_sdefaults, s.dataoffset(), nvals));
                        }
                    }
                }
                if (s.symtype() == SymType.SymTypeConst)
                {
                    if (s.dataoffset() >= 0)
                    {
                        TypeDesc st = s.typespec().simpletype();
                        int nvals = st.aggregate * (st.is_unsized_array() ? 1 : st.numelements());
                        if (st.basetype == TypeDesc.INT)
                        {
                            s.set_dataptr(SymArena.Absolute, slice(m_iconsts, s.dataoffset(), nvals));
                        }
                        else if (st.basetype == TypeDesc.FLOAT)
                        {
                            s.set_dataptr(SymArena.Absolute, slice(m_fconsts, s.dataoffset(), nvals));
                        }
                        else if (st.basetype == TypeDesc.STRING)
                        {
                            s.set_dataptr(SymArena.Absolute, slice(m_sconsts, s.dataoffset(), nvals));
                        }
                    }
                }
                ++i;
            }

            // Mark symbols that are used for renderer-aware tracking (their lifetimes)
            List<Symbol> oparg_ptrs = new List<Symbol>(m_args.Count);
            foreach (int a in m_args)
            {
                oparg_ptrs.Add(symbol(a));
            }
            SymtabFunctions.track_variable_lifetimes_main(m_ops, oparg_ptrs, allsymptrs);

            // Figure out which ray types are queried by this shader
            m_raytype_queries = 0;
            foreach (Opcode op in m_ops)
            {
                if (op.opname() == Strings.raytype)
                {
                    int bit = -1;  // could be any
                    Symbol Name = symbol(m_args[op.firstarg() + 1]);
                    if (Name.is_constant())
                    {
                        int b = shadingsys().raytype_bit(Name.get_string());
                        if (b != 0)
                        {
                            bit = b;
                        }
                    }
                    m_raytype_queries |= bit;
                }
            }

            long totalmem = m_ops.Count * 100L + m_args.Count * 4L + m_symbols.Count * 200L + m_idefaults.Count * 4L + m_fdefaults.Count * 4L
                            + m_sdefaults.Count * 8L + m_iconsts.Count * 4L + m_fconsts.Count * 4L + m_sconsts.Count * 8L;
            lock (shadingsys().m_stat_mutex)
            {
                shadingsys().m_stat_mem_master_ops.add(m_ops.Count * 100L);
                shadingsys().m_stat_mem_master_args.add(m_args.Count * 4L);
                shadingsys().m_stat_mem_master_syms.add(m_symbols.Count * 200L);
                shadingsys().m_stat_mem_master_defaults.add(m_idefaults.Count * 4L + m_fdefaults.Count * 4L + m_sdefaults.Count * 8L);
                shadingsys().m_stat_mem_master_consts.add(m_iconsts.Count * 4L + m_fconsts.Count * 4L + m_sconsts.Count * 8L);
                shadingsys().m_stat_mem_master.add(totalmem);
                shadingsys().m_stat_memory.add(totalmem);
            }
        }

        private static string joinvals<T>(List<T> l, bool quoted)
        {
            StringBuilder o = new StringBuilder();
            foreach (T v in l)
            {
                if (quoted)
                {
                    o.Append('"').Append(v.ToString()).Append("\" ");
                }
                else if (v is float f)
                {
                    o.Append(Fmt.format_g(f, 6, false, false)).Append(' ');
                }
                else
                {
                    o.Append(v.ToString()).Append(' ');
                }
            }
            return o.ToString();
        }

        /// Debugging: describe the master
        public string print()
        {
            StringBuilder o = new StringBuilder();
            o.Append("Shader ").Append(m_shadername).Append(" type=").Append(shadertypename()).Append("\n");
            o.Append("  path = ").Append(m_osofilename).Append("\n");
            o.Append("  symbols:\n");
            for (int i = 0; i < m_symbols.Count; ++i)
            {
                Symbol s = m_symbols[i];
                o.Append("    ").Append(i).Append(": ").Append(s.typespec().@string()).Append(' ').Append(s.name().@string()).Append("\n");
            }
            o.Append("  int consts:\n    ").Append(joinvals(m_iconsts, false)).Append("\n");
            o.Append("  float consts:\n    ").Append(joinvals(m_fconsts, false)).Append("\n");
            o.Append("  string consts:\n    ").Append(joinvals(m_sconsts, true)).Append("\n");
            o.Append("  int defaults:\n    ").Append(joinvals(m_idefaults, false)).Append("\n");
            o.Append("  float defaults:\n    ").Append(joinvals(m_fdefaults, false)).Append("\n");
            o.Append("  string defaults:\n    ").Append(joinvals(m_sdefaults, true)).Append("\n");
            o.Append("  code:\n");
            for (int i = 0; i < m_ops.Count; ++i)
            {
                Opcode op = m_ops[i];
                o.Append("    ").Append(i).Append(": ").Append(op.opname().@string());
                for (int a = 0; a < op.nargs(); ++a)
                {
                    o.Append(' ').Append(m_symbols[m_args[op.firstarg() + a]].name().@string());
                }
                for (int j = 0; j < Opcode.max_jumps; ++j)
                {
                    if (op.jump(j) >= 0)
                    {
                        o.Append(' ').Append(op.jump(j));
                    }
                }
                if (!op.sourcefile().empty())
                {
                    o.Append("\t(").Append(op.sourcefile().@string()).Append(':').Append(op.sourceline()).Append(')');
                }
                o.Append("\n");
            }
            return o.ToString();
        }
    }
}
