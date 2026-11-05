// OSL port: liboslcomp/symtab.h and symtab.cpp (FunctionSymbol, ConstantSymbol, SymbolTable, track_variable_lifetimes_main)

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using static OIIO.TypeDescs;

namespace OSL
{
    namespace pvt
    {
        public sealed class FunctionSymbol : Symbol
        {
            private ustring m_argcodes;          ///< Encoded arg types
            private FunctionSymbol m_nextpoly;   ///< Next polymorphic version
            // Below, temporary storage used during code generation
            private Symbol m_return_location;      ///< Store return value location
            private int m_number_of_returns;        ///< How many returns?
            private int m_function_loop_nesting;    ///< Loop nesting level within the func
            private int m_function_total_nesting;   ///< Total nesting level within the func
            private bool m_readwrite_special_case;  ///< Unusual in how it r/w's its args
            private bool m_texture_args;            ///< Has texture-like token/value args
            private bool m_printf_args;             ///< Has printf-like varargs
            private bool m_takes_derivs;            ///< Takes derivatives of its args

            public FunctionSymbol(ustring n, TypeSpec type, ASTNode node = null) : base(n, type, SymType.SymTypeFunction, node)
            {
            }

            public void nextpoly(FunctionSymbol nextpoly) { m_nextpoly = nextpoly; }
            public FunctionSymbol nextpoly() { return m_nextpoly; }
            public void argcodes(ustring args) { m_argcodes = args; }
            public ustring argcodes() { return m_argcodes; }
            public Symbol return_location() { return m_return_location; }
            public void return_location(Symbol r) { m_return_location = r; }
            public void encountered_return() { ++m_number_of_returns; }
            public int number_of_returns() { return m_number_of_returns; }

            public void push_nesting(bool isloop)
            {
                ++m_function_total_nesting;
                if (isloop)
                {
                    ++m_function_loop_nesting;
                }
            }

            public void pop_nesting(bool isloop)
            {
                --m_function_total_nesting;
                if (isloop)
                {
                    --m_function_loop_nesting;
                }
            }

            public int nesting_level() { return m_function_total_nesting; }

            public void init_nesting()
            {
                m_function_total_nesting = 0;
                m_function_loop_nesting = 0;
            }

            public void readwrite_special_case(bool s) { m_readwrite_special_case = s; }
            public bool readwrite_special_case() { return m_readwrite_special_case; }
            public void texture_args(bool s) { m_texture_args = s; }
            public bool texture_args() { return m_texture_args; }
            public void printf_args(bool s) { m_printf_args = s; }
            public bool printf_args() { return m_printf_args; }
            public void takes_derivs(bool s) { m_takes_derivs = s; }
            public bool takes_derivs() { return m_takes_derivs; }
        }

        public sealed class ConstantSymbol : Symbol
        {
            public ConstantSymbol(ustring n, ustring val) : base(n, TypeString, SymType.SymTypeConst)
            {
                set_dataptr(SymArena.Absolute, new ustring[] { val });
            }

            public ConstantSymbol(ustring n, int val) : base(n, TypeInt, SymType.SymTypeConst)
            {
                set_dataptr(SymArena.Absolute, new int[] { val });
            }

            public ConstantSymbol(ustring n, float val) : base(n, TypeFloat, SymType.SymTypeConst)
            {
                set_dataptr(SymArena.Absolute, new float[] { val });
            }

            public ConstantSymbol(ustring n, TypeDesc type, float x, float y, float z) : base(n, type, SymType.SymTypeConst)
            {
                set_dataptr(SymArena.Absolute, new float[] { x, y, z });
            }

            // Allocate room for a value of the given type; the caller fills it through the typed array returned by data()
            public ConstantSymbol(ustring n, TypeDesc type) : base(n, type, SymType.SymTypeConst)
            {
                int count = type.basevalues();
                if (type.basetype == TypeDesc.FLOAT)
                {
                    set_dataptr(SymArena.Absolute, new float[Math.Max(count, 1)]);
                }
                else if (type.basetype == TypeDesc.INT)
                {
                    set_dataptr(SymArena.Absolute, new int[Math.Max(count, 1)]);
                }
                else if (type.basetype == TypeDesc.STRING)
                {
                    set_dataptr(SymArena.Absolute, new ustring[Math.Max(count, 1)]);
                }
                else
                {
                    set_dataptr(SymArena.Absolute, new byte[Math.Max(type.size(), 1)]);
                }
            }

            // Return the index'th float, automatically cast from int if necessary.
            public float floatval(int index = 0) { return coerce_float(index); }
        }

        public class SymbolTable
        {
            private List<Symbol> m_allsyms = new List<Symbol>();                  ///< Master list of all symbols
            private List<Dictionary<ustring, Symbol>> m_scopetables = new List<Dictionary<ustring, Symbol>>();  ///< Stack of symbol scopes
            private Stack<int> m_scopestack = new Stack<int>();                    ///< Stack of current scope IDs
            private Dictionary<ustring, Symbol> m_allmangled = new Dictionary<ustring, Symbol>();  ///< All syms, mangled, in a hash table
            private int m_scopeid;                                                 ///< Current scope ID
            private int m_nextscopeid;                                             ///< Next unique scope ID

            public SymbolTable()
            {
                m_scopeid = -1;
                m_nextscopeid = 0;
                push();  // Create scope 0 -- global scope
            }

            /// Find a symbol, optionally restricting to those declared at or outside a scope: if 'last' is given, only match OUTSIDE the
            /// scope of 'last'.
            public Symbol find(ustring name, Symbol last = null)
            {
                int scopelevel = m_scopetables.Count - 1;
                if (last != null)
                {
                    // We only want to match OUTSIDE the scope of 'last'.  So first search for last.  Then advance to the next outer scope.
                    for (; scopelevel >= 0; --scopelevel)
                    {
                        Symbol s;
                        if (m_scopetables[scopelevel].TryGetValue(name, out s) && s == last)
                        {
                            --scopelevel;
                            break;
                        }
                    }
                }
                for (; scopelevel >= 0; --scopelevel)
                {
                    Symbol s;
                    if (m_scopetables[scopelevel].TryGetValue(name, out s))
                    {
                        return s;
                    }
                }
                return null;  // not found
            }

            /// Find a symbol by its mangled name.
            public Symbol find_exact(ustring mangled_name)
            {
                Symbol s;
                return m_allmangled.TryGetValue(mangled_name, out s) ? s : null;
            }

            /// Search for a name in the current scope only; return the clashing symbol if there is one.
            public Symbol clash(ustring name)
            {
                Symbol s = find(name);
                return (s != null && s.scope() == scopeid()) ? s : null;
            }

            /// Insert a new symbol in the current scope.
            public void insert(Symbol sym)
            {
                sym.scope(scopeid());
                m_scopetables[m_scopetables.Count - 1][sym.name()] = sym;
                m_allsyms.Add(sym);
                m_allmangled[new ustring(sym.mangled())] = sym;
            }

            /// Create a new structure type with the given name in the current scope, and a symbol for it; return its structure id.
            public int new_struct(ustring name)
            {
                int structid = TypeSpec.new_struct(new StructSpec(name, scopeid()));
                insert(new Symbol(name, new TypeSpec("", structid), SymType.SymTypeType));
                return structid;
            }

            /// Add a field to the most recently created struct.
            public void add_struct_field(TypeSpec type, ustring name)
            {
                StructSpec s = current_struct();
                s.add_field(type, name);
            }

            public StructSpec current_struct()
            {
                List<StructSpec> list = TypeSpec.struct_list();
                return list[list.Count - 1];
            }

            public int scopeid() { return m_scopeid; }

            /// Push a new scope.
            public void push()
            {
                m_scopestack.Push(m_scopeid);  // push old scope id on the scope stack
                m_scopeid = m_nextscopeid++;   // set to new scope id
                m_scopetables.Add(new Dictionary<ustring, Symbol>());
            }

            /// Pop the current scope.
            public void pop()
            {
                m_scopetables.RemoveAt(m_scopetables.Count - 1);
                m_scopeid = m_scopestack.Pop();
            }

            public void delete_syms()
            {
                m_allsyms.Clear();
                TypeSpec.struct_list().Clear();
            }

            public string print()
            {
                StringBuilder o = new StringBuilder();
                List<StructSpec> structs = TypeSpec.struct_list();
                if (structs.Count > 0)
                {
                    o.Append("Structure table:\n");
                    int structid = 1;
                    foreach (StructSpec s in structs)
                    {
                        if (s == null)
                        {
                            continue;
                        }
                        o.Append("    " + structid + ": struct " + s.mangled());
                        if (s.scope() != 0)
                        {
                            o.Append(" (" + s.name() + " in scope " + s.scope() + ")");
                        }
                        o.Append(" :\n");
                        for (int i = 0; i < s.numfields(); ++i)
                        {
                            StructSpec.FieldSpec f = s.field(i);
                            o.Append("\t" + f.name + " : " + f.type.@string() + "\n");
                        }
                        ++structid;
                    }
                    o.Append("\n");
                }

                o.Append("Symbol table:\n");
                foreach (Symbol s in m_allsyms)
                {
                    if (s.is_structure())
                    {
                        continue;
                    }
                    o.Append("\t" + s.mangled() + " : ");
                    o.Append(s.typespec().@string());
                    if (s.scope() != 0)
                    {
                        o.Append(" (" + s.name() + " in scope " + s.scope() + ")");
                    }
                    if (s.is_function())
                    {
                        FunctionSymbol f = (FunctionSymbol)s;
                        o.Append(" function (" + TypeSpec.typelist_from_code(f.argcodes().@string()) + ") ");
                    }
                    o.Append("\n");
                }
                o.Append("\n");
                return o.ToString();
            }

            public List<Symbol> allsyms() { return m_allsyms; }
            public IEnumerator<Symbol> GetEnumerator() { return m_allsyms.GetEnumerator(); }
        }

        public static class SymtabFunctions
        {
            private static readonly ustring op_for = new ustring("for");
            private static readonly ustring op_while = new ustring("while");
            private static readonly ustring op_dowhile = new ustring("dowhile");

            /// Called after code is generated, this function loops over all the ops and figures out the lifetimes of all variables, based on
            /// whether the args in each op are read or written. Used both in the compiler and the runtime optimizer.
            public static void track_variable_lifetimes_main(List<Opcode> code, List<Symbol> opargs, List<Symbol> allsyms, List<int> bblockids = null)
            {
                // Clear the lifetimes for all symbols
                foreach (Symbol s in allsyms)
                {
                    s.clear_rw();
                }

                // Keep track of the nested loops we're inside, as begin/end instruction numbers for the loop body (the end is inclusive).
                List<(int, int)> loop_bounds = new List<(int, int)>();

                // For each op, mark its arguments as being used at that op
                int opnum = 0;
                foreach (Opcode op in code)
                {
                    if (op.opname() == op_for || op.opname() == op_while || op.opname() == op_dowhile)
                    {
                        // If this is a loop op, we need to mark its control variable (the only arg) as used for the duration of the loop!
                        Symbol s = opargs[op.firstarg()];
                        int loopcond = op.jump(0);  // after initialization, before test
                        int loopend = op.farthest_jump() - 1;  // inclusive end
                        s.mark_rw(opnum + 1, true, true);
                        s.mark_rw(loopend, true, true);
                        // Also push the loop bounds for this loop
                        loop_bounds.Add((loopcond, loopend));
                    }

                    // Some work to do for each argument to the op...
                    for (int a = 0; a < op.nargs(); ++a)
                    {
                        Symbol s = opargs[op.firstarg() + a];

                        // Mark that it's read and/or written for this op
                        bool readhere = op.argread(a);
                        bool writtenhere = op.argwrite(a);
                        s.mark_rw(opnum, readhere, writtenhere);

                        // Adjust lifetimes of symbols whose values need to be preserved between loop iterations.
                        foreach ((int, int) oprange in loop_bounds)
                        {
                            int loopcond = oprange.Item1;
                            int loopend = oprange.Item2;
                            // Special case: a temp or local, even if written inside a loop, if its entire lifetime is within one basic block
                            // and it's strictly written before being read, then its lifetime is truly local.
                            if (bblockids != null && (s.symtype() == SymType.SymTypeLocal || s.symtype() == SymType.SymTypeTemp)
                                && bblockids[s.firstuse()] == bblockids[s.lastuse()] && s.lastwrite() < s.firstread())
                            {
                                continue;
                            }
                            // Syms written before or inside the loop, and referenced inside or after the loop, need to preserve their value
                            // for the duration of the loop.
                            if (s.firstwrite() <= loopend)
                            {
                                s.mark_rw(loopcond, readhere, writtenhere);
                                s.mark_rw(loopend, readhere, writtenhere);
                            }
                        }
                    }

                    ++opnum;  // Advance to the next op index

                    // Pop any loop bounds for loops we've just exited
                    while (loop_bounds.Count > 0 && loop_bounds[loop_bounds.Count - 1].Item2 < opnum)
                    {
                        loop_bounds.RemoveAt(loop_bounds.Count - 1);
                    }
                }
            }
        }
    }
}
