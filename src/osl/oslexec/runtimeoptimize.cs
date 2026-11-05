// OSL port: liboslexec/runtimeoptimize.h and runtimeoptimize.cpp (the first half: the OSOProcessorBase parts, the helpers that edit instance code and
// symbols, and the per-layer optimization passes). The rest of the class (run, lifetimes, derivatives, coalescing, collapsing, the group inventory and
// the policing of failed optimizations) is in runtimeoptimize_run.cs, and the constant folders of constfold.cpp are in constfold*.cs.
//
// Differences from the C++ that follow from the data model (see pvt.cs and instance.cs):
//   * Constant data is not passed as 'const void*' but as the managed array that Symbol.data() holds (int[], float[] or ustring[]), with an optional
//     starting position in base values, so that "the constant at element i of an array" can still be named.
//   * Symbols are objects. The C++ pointer comparisons (&A == &B) are reference comparisons, and the "pointers into the vector may change" caveats of
//     make_symbol_room do not apply (the call is kept, it only reserves room).
//   * A reference to an Opcode is a reference to the op object, not to its slot, so the places where the C++ re-fetches 'op' after ops were inserted
//     re-fetch it here too, and turn_into_* find the position of the op they were given (the C++ subtracts pointers).
//   * The values of the parameters of an instance are the managed arrays m_paramdata[i] (see instance.cs) instead of m_iparams/m_fparams/m_sparams.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OIIO;
using static OIIO.TypeDescs;

namespace OSL.pvt
{
    /// Parts of OSOProcessorBase that live in runtimeoptimize.cpp
    public partial class OSOProcessorBase
    {
        private static readonly ustring u_nop_base = new ustring("nop");
        private static readonly ustring u_functioncall_nr_base = new ustring("functioncall_nr");
        private static readonly ustring u_break_base = new ustring("break");
        private static readonly ustring u_continue_base = new ustring("continue");
        private static readonly ustring u_return_base = new ustring("return");
        private static readonly ustring u_exit_base = new ustring("exit");

        public virtual void set_debug()
        {
            // start with the shading system's idea of debugging level
            m_debug = shadingsys().debug();

            // If either group or layer was specified for debug, surely they want debugging turned on.
            if (!shadingsys().debug_groupname().empty() || !shadingsys().debug_layername().empty())
            {
                m_debug = Math.Max(m_debug, 1);
            }

            // Force debugging off if a specific group was selected for debug and we're not it, or a specific layer was selected for debug and we're
            // not it.
            bool wronggroup = (!shadingsys().debug_groupname().empty() && shadingsys().debug_groupname() != group().name());
            bool wronglayer = (!shadingsys().debug_layername().empty() && inst() != null && shadingsys().debug_layername() != inst().layername());
            if (wronggroup || wronglayer)
            {
                m_debug = 0;
            }
        }

        /// Set which instance (layer within the group) we are currently examining. This lets you walk through the layers in turn.
        public virtual void set_inst(int newlayer)
        {
            m_layer = newlayer;
            m_inst = group()[m_layer];
            System.Diagnostics.Debug.Assert(m_inst != null);
            set_debug();
        }

        /// Is the symbol a constant whose value is 0?
        public static bool is_zero(Symbol A)
        {
            if (!A.is_constant())
            {
                return false;
            }
            TypeSpec Atype = A.typespec();
            Vec3 Vzero = new Vec3(0, 0, 0);
            return (Atype.is_float() && A.get_float() == 0) || (Atype.is_int() && A.get_int() == 0) || (Atype.is_triple() && A.get_vec3() == Vzero);
        }

        /// Is the symbol a constant whose value is 1?
        public static bool is_one(Symbol A)
        {
            if (!A.is_constant())
            {
                return false;
            }
            TypeSpec Atype = A.typespec();
            Vec3 Vone = new Vec3(1, 1, 1);
            Matrix44 Mone = new Matrix44(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
            return (Atype.is_float() && A.get_float() == 1) || (Atype.is_int() && A.get_int() == 1) || (Atype.is_triple() && A.get_vec3() == Vone)
                   || (Atype.is_matrix() && RuntimeOptimizer.matrix_from_data((float[])A.dataptr()) == Mone);
        }

        /// Is the symbol a constant whose value is nonzero in all components?
        public static bool is_nonzero(Symbol A)
        {
            if (!A.is_constant())
            {
                return false;
            }
            TypeSpec Atype = A.typespec();
            int ncomponents = Atype.numelements() * Atype.aggregate();
            if (Atype.is_float_based())
            {
                float[] val = (float[])A.data();
                for (int i = 0; i < ncomponents; ++i)
                {
                    if (val[i] == 0.0f)
                    {
                        return false;
                    }
                }
                return true;
            }
            if (Atype.is_int_based())
            {
                int[] val = (int[])A.data();
                for (int i = 0; i < ncomponents; ++i)
                {
                    if (val[i] == 0)
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        /// For debugging, express A's constant value as a string.
        public static string const_value_as_string(Symbol A)
        {
            if (!A.is_constant())
            {
                return "";
            }
            TypeDesc type = A.typespec().simpletype();
            int n = type.numelements() * type.aggregate;
            StringBuilder s = new StringBuilder();
            if (type.basetype == TypeDesc.FLOAT)
            {
                for (int i = 0; i < n; ++i)
                {
                    s.Append(i != 0 ? "," : "").Append(Fmt.format("{}", A.get_float(i)));
                }
            }
            else if (type.basetype == TypeDesc.INT)
            {
                for (int i = 0; i < n; ++i)
                {
                    s.Append(i != 0 ? "," : "").Append(A.get_int(i));
                }
            }
            else if (type.basetype == TypeDesc.STRING)
            {
                for (int i = 0; i < n; ++i)
                {
                    s.Append(i != 0 ? "," : "").Append('"').Append(A.get_string(i).@string()).Append('"');
                }
            }
            return s.ToString();
        }

        /// Set up m_in_conditional[] to be true for all ops that are inside of conditionals, false for all unconditionally-executed ops, m_in_loop[]
        /// to be true for all ops that are inside a loop, and m_first_return to be the op number of the first return/exit statement (or code.size() if
        /// there is no return/exit statement).
        public void find_conditionals()
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

        /// Identify basic blocks by assigning a basic block ID for each instruction. Within any basic bock, there are no jumps in or out. Also note
        /// which instructions are inside conditional states.
        public void find_basic_blocks()
        {
            List<Opcode> code = inst().ops();

            // Start by setting all basic block IDs to 0
            m_bblockids.Clear();
            for (int i = 0; i < code.Count; ++i)
            {
                m_bblockids.Add(0);
            }

            // First, keep track of all the spots where blocks begin
            bool[] block_begin = new bool[code.Count];

            // Init ops start basic blocks
            foreach (Symbol s in inst().param_range())
            {
                if (s.has_init_ops())
                {
                    block_begin[s.initbegin()] = true;
                }
            }

            // Main code starts a basic block
            block_begin[inst().maincodebegin()] = true;

            for (int opnum = 0; opnum < code.Count; ++opnum)
            {
                Opcode op = code[opnum];
                if (op.opname() == u_functioncall_nr_base)
                {
                    // Treat the 'no return' function call as if it were a nop. we use later to generate correct inline debug information.
                    continue;
                }
                // Anyplace that's the target of a jump instruction starts a basic block
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
                // The first instruction in a conditional or loop (which is not itself a jump target) also begins a basic block. If the op has any
                // jump targets at all, it must be a conditional or loop.
                if (op.jump(0) >= 0)
                {
                    set_block_begin(block_begin, opnum + 1);
                }
                // 'break', 'continue', 'return', and 'exit' also cause the next statement to begin a new basic block.
                if (op.opname() == u_break_base || op.opname() == u_continue_base || op.opname() == u_return_base || op.opname() == u_exit_base)
                {
                    set_block_begin(block_begin, opnum + 1);
                }
            }

            // Now color the blocks with unique identifiers
            int bbid = 1;  // next basic block ID to use
            for (int opnum = 0; opnum < code.Count; ++opnum)
            {
                if (block_begin[opnum])
                {
                    ++bbid;
                }
                m_bblockids[opnum] = bbid;
            }
        }

        // The C++ writes block_begin[opnum + 1] even for the last op (a vector<bool> of code.size() elements, which is an out-of-range write there);
        // here the write past the end is simply dropped
        private static void set_block_begin(bool[] block_begin, int index)
        {
            if (index < block_begin.Length)
            {
                block_begin[index] = true;
            }
        }
    }

    /// OSOProcessor that does runtime optimization on shaders.
    public unsafe partial class RuntimeOptimizer : OSOProcessorBase, IDisposable
    {
        // names of ops we'll be using frequently
        private static readonly ustring u_nop = new ustring("nop");
        private static readonly ustring u_exit = new ustring("exit");
        private static readonly ustring u_assign = new ustring("assign");
        private static readonly ustring u_add = new ustring("add");
        private static readonly ustring u_sub = new ustring("sub");
        private static readonly ustring u_mul = new ustring("mul");
        private static readonly ustring u_if = new ustring("if");
        private static readonly ustring u_for = new ustring("for");
        private static readonly ustring u_while = new ustring("while");
        private static readonly ustring u_dowhile = new ustring("dowhile");
        private static readonly ustring u_functioncall = new ustring("functioncall");
        private static readonly ustring u_functioncall_nr = new ustring("functioncall_nr");
        private static readonly ustring u_break = new ustring("break");
        private static readonly ustring u_continue = new ustring("continue");
        private static readonly ustring u_return = new ustring("return");
        private static readonly ustring u_useparam = new ustring("useparam");
        private static readonly ustring u_closure = new ustring("closure");
        private static readonly ustring u_pointcloud_write = new ustring("pointcloud_write");
        private static readonly ustring u_isconnected = new ustring("isconnected");
        private static readonly ustring u_setmessage = new ustring("setmessage");
        private static readonly ustring u_getmessage = new ustring("getmessage");
        private static readonly ustring u_getattribute = new ustring("getattribute");
        private static readonly ustring u_backfacing = new ustring("backfacing");
        private static readonly ustring u_calculatenormal = new ustring("calculatenormal");
        private static readonly ustring u_flipHandedness = new ustring("flipHandedness");
        private static readonly ustring u_N = new ustring("N");
        private static readonly ustring u_I = new ustring("I");
        private static readonly ustring main_method_name = new ustring("___main___");

        private static readonly object s_debug_mutex = new object();

        /// Maximum number of new constant symbols that a constant-folding function is able to add.
        public const int max_new_consts_per_fold = 10;

        public enum RecomputeRWRangesOption
        {
            DontRecomputeRWRanges,
            RecomputeRWRanges
        }

        public enum InsertRelation
        {
            NoRelation = 0,
            GroupWithPrevious = -1,
            GroupWithNext = 1
        }

        // The bit field of police_()
        public const int police_opt_warn = 1;
        public const int police_gpu_err_only = 2;
        public const int police_gpu_err = 3;
        public const int police_string_create_only = 4;
        public const int police_string_create = 5;
        public const int police_string_chars_only = 8;
        public const int police_string_chars = 9;

        private int m_optimize;                         ///< Current optimization level
        private bool m_opt_simplify_param;              ///< Turn instance params into const?
        private bool m_opt_constant_fold;               ///< Allow constant folding?
        private bool m_opt_stale_assign;                ///< Optimize stale assignments?
        private bool m_opt_elide_useless_ops;           ///< Optimize away useless ops?
        private bool m_opt_elide_unconnected_outputs;   ///< Optimize unconnected outputs?
        private bool m_opt_peephole;                    ///< Do some peephole optimizations?
        private bool m_opt_coalesce_temps;              ///< Coalesce temporary variables?
        private bool m_opt_assign;                      ///< Do various assign optimizations?
        private bool m_opt_mix;                         ///< Do mix optimizations?
        private bool m_opt_middleman;                   ///< Do middleman optimizations?
        private bool m_opt_batched_analysis;            ///< Perform extra analysis required for batched execution?
        private bool m_keep_no_return_function_calls;   ///< To generate debug info, keep no return function calls
        private ShaderGlobals* m_shaderglobals;         ///< Dummy ShaderGlobals (made when first asked for)
        private RendererServices m_rendererservices;    ///< Dummy RendererServices

        // Keep track of some things for the whole shader group:
        private List<Dictionary<ustring, ustring>> m_params_holding_globals = new List<Dictionary<ustring, ustring>>();  ///< Which params of each layer really just hold globals

        // All below is just for the one inst we're optimizing at the moment:
        private int m_pass;                                                          ///< Optimization pass we're on now
        private List<int> m_all_consts = new List<int>();                            ///< All const symbol indices for inst
        private int m_next_newconst;                                                 ///< Unique ID for next new const we add
        private int m_next_newtemp;                                                  ///< Unique ID for next new temp we add
        private Dictionary<int, int> m_symbol_aliases = new Dictionary<int, int>();  ///< Global symbol aliases
        private Dictionary<int, int> m_block_aliases = new Dictionary<int, int>();   ///< Local block aliases
        private List<Dictionary<int, int>> m_block_aliases_stack = new List<Dictionary<int, int>>();  ///< Stack of saved local block aliases
        private Dictionary<int, int> m_param_aliases = new Dictionary<int, int>();   ///< Params aliasing to params/globals
        private Dictionary<int, int> m_stale_syms = new Dictionary<int, int>();      ///< Stale symbols for this block
        private bool m_local_unknown_message_sent;                                   ///< Non-const setmessage in this inst
        private List<ustring> m_local_messages_sent = new List<ustring>();           ///< Messages set in this inst
        public SortedSet<ustring> m_textures_needed = new SortedSet<ustring>();
        public SortedSet<ustring> m_closures_needed = new SortedSet<ustring>();
        public SortedSet<ustring> m_globals_needed = new SortedSet<ustring>();
        public int m_globals_read = 0;
        public int m_globals_write = 0;
        public SortedSet<AttributeNeeded> m_attributes_needed = new SortedSet<AttributeNeeded>();
        public bool m_unknown_textures_needed;
        public bool m_unknown_closures_needed;
        public bool m_unknown_attributes_needed;
        public SortedSet<UserDataNeeded> m_userdata_needed = new SortedSet<UserDataNeeded>();
        public double m_stat_opt_locking_time;      ///<   locking time
        public double m_stat_specialization_time;   ///<   specialization time
        private bool m_stop_optimizing;             ///< for debugging
        private int m_raytypes_on;                  ///< Ray types known to be on
        private int m_raytypes_off;                 ///< Ray types known to be off

        // Persistent data shared between layers
        private bool m_unknown_message_sent;                                     ///< Somebody did a non-const setmessage
        private List<ustring> m_messages_sent = new List<ustring>();             ///< Names of messages set

        private bool m_disposed;

        static RuntimeOptimizer()
        {
            // The op table (optable.cs) looks the folders up by name in this registry
            ConstFold.register_folders();
        }

        public RuntimeOptimizer(ShadingSystemImpl shadingsys, ShaderGroup group, ShadingContext context)
            : base(shadingsys, group, context)
        {
            base.set_debug();
            m_optimize = shadingsys.optimize();
            m_opt_simplify_param = shadingsys.m_opt_simplify_param;
            m_opt_constant_fold = shadingsys.m_opt_constant_fold;
            m_opt_stale_assign = shadingsys.m_opt_stale_assign;
            m_opt_elide_useless_ops = shadingsys.m_opt_elide_useless_ops;
            m_opt_elide_unconnected_outputs = shadingsys.m_opt_elide_unconnected_outputs;
            m_opt_peephole = shadingsys.m_opt_peephole;
            m_opt_coalesce_temps = shadingsys.m_opt_coalesce_temps;
            m_opt_assign = shadingsys.m_opt_assign;
            m_opt_mix = shadingsys.m_opt_mix;
            m_opt_middleman = shadingsys.m_opt_middleman;
            m_opt_batched_analysis = shadingsys.m_opt_batched_analysis;
            m_keep_no_return_function_calls = shadingsys.m_llvm_debugging_symbols != 0;
            m_pass = 0;
            m_next_newconst = 0;
            m_next_newtemp = 0;
            m_stat_opt_locking_time = 0;
            m_stat_specialization_time = 0;
            m_stop_optimizing = false;
            m_raytypes_on = group.raytypes_on();
            m_raytypes_off = group.raytypes_off();

            // Disable no_function_return_calls for OptiX renderers, because we aren't yet set up to support use of debugging symbols for PTX.
            // FIXME: some day, we are going to want debugging symbols for PTX, and will need some refactoring of the debugging symbol code.
            if (shadingsys.renderer().supports("OptiX") != 0)
            {
                m_keep_no_return_function_calls = false;
            }
        }

        ~RuntimeOptimizer()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                m_disposed = true;
                if (m_shaderglobals != null)
                {
                    ShaderGlobalsAccess.free_handle(m_shaderglobals->context);
                    ShaderGlobalsAccess.free_handle(m_shaderglobals->renderer);
                    NativeMemory.Free(m_shaderglobals);
                    m_shaderglobals = null;
                }
            }
        }

        public override void set_inst(int newlayer)
        {
            base.set_inst(newlayer);
            m_all_consts.Clear();
            m_symbol_aliases.Clear();
            m_block_aliases.Clear();
            m_param_aliases.Clear();
            m_bblockids.Clear();
        }

        public override void set_debug()
        {
            base.set_debug();

            // If a specific group is isolated for debugging and the 'optimize_dondebug' flag is on, fully optimize all other groups.
            if (!shadingsys().debug_groupname().empty() && shadingsys().debug_groupname() != group().name())
            {
                if (shadingsys().m_optimize_nondebug)
                {
                    // Debugging trick: if user said to only debug one group, turn on full optimization for all others! This prevents everything from
                    // running 10x slower just because you want to debug one shader.
                    m_optimize = 3;
                    m_opt_simplify_param = true;
                    m_opt_constant_fold = true;
                    m_opt_stale_assign = true;
                    m_opt_elide_useless_ops = true;
                    m_opt_elide_unconnected_outputs = true;
                    m_opt_peephole = true;
                    m_opt_coalesce_temps = true;
                    m_opt_assign = true;
                    m_opt_mix = true;
                    m_opt_middleman = true;
                }
            }
        }

        /// Optionally set which ray types are known to be on or off (0 means not known at optimize time).
        public void set_raytypes(int raytypes_on, int raytypes_off)
        {
            m_raytypes_on = raytypes_on;
            m_raytypes_off = raytypes_off;
        }

        public int raytypes_on() { return m_raytypes_on; }
        public int raytypes_off() { return m_raytypes_off; }

        /// What's our current optimization level?
        public int optimize() { return m_optimize; }

        public bool opt_elide_unconnected_outputs() { return m_opt_elide_unconnected_outputs; }

        /// Are special optimizations to 'mix' requested?
        public bool opt_mix() { return m_opt_mix; }

        /// Should no return function calls be kept in order to generate correct inline function debug info
        public bool keep_no_return_function_calls() { return m_keep_no_return_function_calls; }

        /// Which optimization pass are we on?
        public int optimization_pass() { return m_pass; }

        /// Retrieve ptr to the dummy shader globals
        public ShaderGlobals* shaderglobals()
        {
            if (m_shaderglobals == null)
            {
                m_shaderglobals = (ShaderGlobals*)NativeMemory.AllocZeroed((nuint)sizeof(ShaderGlobals));
                m_shaderglobals->context = ShaderGlobalsAccess.make_handle(shadingcontext());
                // To handle error/warning/print that might be reported through the renderer services, we will need to provide the default one which will
                // just report through the ShadingContext
                m_rendererservices = new RendererServices();
                m_shaderglobals->renderer = ShaderGlobalsAccess.make_handle(m_rendererservices);
            }
            return m_shaderglobals;
        }

        public void stop_optimizing() { m_stop_optimizing = true; }

        public string op_string(Opcode op)
        {
            string s = op.opname().@string();
            for (int a = 0; a < op.nargs(); ++a)
            {
                s = s + ' ' + opargsym(op, a).name().@string();
            }
            return s;
        }

        // The position of an op in the instance's code (the C++ subtracts the address of the first op)
        private int opnum_of(Opcode op)
        {
            return inst().ops().IndexOf(op);
        }

        // The Matrix44 held in 16 floats
        public static Matrix44 matrix_from_data(float[] m)
        {
            return new Matrix44(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        }

        // Whether the bits of n values of the two arrays are the same (the memcmp of the C++, which compares the memory of the values)
        public static bool data_equal(object a, int aoffset, object b, int boffset, int n)
        {
            if (a is ustring[] sa)
            {
                if (!(b is ustring[] sb) || aoffset + n > sa.Length || boffset + n > sb.Length)
                {
                    return false;
                }
                for (int i = 0; i < n; ++i)
                {
                    if (sa[aoffset + i] != sb[boffset + i])
                    {
                        return false;
                    }
                }
                return true;
            }
            for (int i = 0; i < n; ++i)
            {
                int wa, wb;
                if (!word_at(a, aoffset + i, out wa) || !word_at(b, boffset + i, out wb) || wa != wb)
                {
                    return false;
                }
            }
            return true;
        }

        // The 32 bits of value i of an int[] or float[]
        private static bool word_at(object a, int i, out int word)
        {
            word = 0;
            if (a is float[] fa)
            {
                if (i >= fa.Length)
                {
                    return false;
                }
                word = BitConverter.SingleToInt32Bits(fa[i]);
                return true;
            }
            if (a is int[] ia)
            {
                if (i >= ia.Length)
                {
                    return false;
                }
                word = ia[i];
                return true;
            }
            return false;
        }

        /// Search the instance for a constant whose type and value match type and data[...]. Return -1 if no matching const is found.
        public int find_constant(TypeSpec type, object data, int dataoffset)
        {
            foreach (int c in m_all_consts)
            {
                Symbol s = inst().symbol(c);
                System.Diagnostics.Debug.Assert(s.symtype() == SymType.SymTypeConst);
                if (PvtFunctions.equivalent(s.typespec(), type) && data_equal(s.data(), 0, data, dataoffset, s.typespec().simpletype().basevalues()))
                {
                    return c;
                }
            }
            return -1;
        }

        /// Search for a constant whose type and value match type and data[...], returning its index if one exists, or else creating a new constant and
        /// returning its index. 'data' is the array that holds the value (int[], float[] or ustring[]), starting at the base value 'dataoffset'.
        public int add_constant(TypeSpec type, object data, TypeDesc datatype, int dataoffset)
        {
            int ind = find_constant(type, data, dataoffset);
            if (ind < 0)
            {
                // support varlen arrays
                TypeSpec newtype = type;
                if (type.is_unsized_array())
                {
                    newtype.make_array(datatype.numelements());
                }

                ustring symname;
                if (newtype.is_int())
                {
                    // If it's a constant integer, make the name $newconst_int42 and if it's negative, $newconst_int_42 (because we can't put a minus
                    // sign in a symbol name). This is easier to read than naming them sequentially (you know the value in the constant) and it means
                    // the name will be the same regardless of the order in which we made the constants (helpful for PTX cache behavior).
                    int val = ((int[])data)[dataoffset];
                    int absval = unchecked(val < 0 ? -val : val);
                    symname = new ustring(Fmt.format("$newconst_int{}{}", val < 0 ? "_" : "", absval));
                }
                else if (newtype.is_string())
                {
                    // If it's a constant string, make the name $newconst_str_HASH where HASH is the hex value of the hash of that ustring. This
                    // ensures that the name will be the same regardless of the order in which we made the constants (helpful for PTX cache behavior).
                    ustring val = ((ustring[])data)[dataoffset];
                    symname = new ustring("$newconst_str" + val.hash().ToString("x8"));
                }
                else
                {
                    // All other cases, just name it sequentially as $newconst_1, $newconst_2, etc.
                    symname = new ustring(Fmt.format("$newconst{}", m_next_newconst++));
                }
                Symbol newconst = new Symbol(symname, newtype, SymType.SymTypeConst);
                object newdata = null;
                TypeDesc t = newtype.simpletype();
                int n = t.aggregate * t.numelements();
                if (datatype == new TypeDesc())
                {
                    datatype = t;
                }
                int datan = datatype.aggregate * datatype.numelements();
                if (t.basetype == TypeDesc.INT && datatype.basetype == TypeDesc.INT && n == datan)
                {
                    int[] idata = inst().shadingsys().alloc_int_constants(n);
                    Array.Copy((int[])data, dataoffset, idata, 0, n);
                    newdata = idata;
                }
                else if (t.basetype == TypeDesc.FLOAT && datatype.basetype == TypeDesc.FLOAT)
                {
                    float[] fdata = inst().shadingsys().alloc_float_constants(n);
                    float[] src = (float[])data;
                    if (n == datan)
                    {
                        for (int i = 0; i < n; ++i)
                        {
                            fdata[i] = src[dataoffset + i];
                        }
                    }
                    else if (datan == 1)
                    {
                        for (int i = 0; i < n; ++i)
                        {
                            fdata[i] = src[dataoffset];
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("unsupported type for add_constant");
                    }
                    newdata = fdata;
                }
                else if (t.basetype == TypeDesc.FLOAT && datatype.basetype == TypeDesc.INT)
                {
                    float[] fdata = inst().shadingsys().alloc_float_constants(n);
                    int[] src = (int[])data;
                    if (n == datan)
                    {
                        for (int i = 0; i < n; ++i)
                        {
                            fdata[i] = src[dataoffset + i];
                        }
                    }
                    else if (datan == 1)
                    {
                        for (int i = 0; i < n; ++i)
                        {
                            fdata[i] = src[dataoffset];
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("unsupported type for add_constant");
                    }
                    newdata = fdata;
                }
                else if (t.basetype == TypeDesc.STRING && datatype.basetype == TypeDesc.STRING && n == datan)
                {
                    ustring[] sdata = inst().shadingsys().alloc_string_constants(n);
                    Array.Copy((ustring[])data, dataoffset, sdata, 0, n);
                    newdata = sdata;
                }
                else
                {
                    throw new InvalidOperationException("unsupported type for add_constant");
                }
                newconst.set_dataptr(SymArena.Absolute, newdata);
                ind = add_symbol(newconst);
                m_all_consts.Add(ind);
            }
            return ind;
        }

        public int add_constant(TypeSpec type, object data)
        {
            return add_constant(type, data, new TypeDesc(), 0);
        }

        public int add_constant(float c) { return add_constant(TypeFloat, new float[] { c }); }
        public int add_constant(int c) { return add_constant(TypeInt, new int[] { c }); }
        public int add_constant(ustring s) { return add_constant(TypeString, new ustring[] { s }); }

        public int add_constant(Matrix44 c)
        {
            return add_constant(TypeMatrix, new float[] { c.m00, c.m01, c.m02, c.m03, c.m10, c.m11, c.m12, c.m13, c.m20, c.m21, c.m22, c.m23,
                                                          c.m30, c.m31, c.m32, c.m33 });
        }

        public int add_constantc(Color3 c) { return add_constant(TypeColor, new float[] { c.x, c.y, c.z }); }

        /// Create a new temporary variable of the given type, return its index.
        public int add_temp(TypeSpec type)
        {
            return add_symbol(new Symbol(new ustring(Fmt.format("$opttemp{}", m_next_newtemp++)), type, SymType.SymTypeTemp));
        }

        /// Search for the given global, adding it to the symbol table if necessary, and returning its index.
        public int add_global(ustring name, TypeSpec type)
        {
            int index = inst().findsymbol(name);
            if (index < 0)
            {
                index = add_symbol(new Symbol(name, type, SymType.SymTypeGlobal));
            }
            return index;
        }

        /// Add a new symbol to the current instance's symbol list. Don't push onto the symbol table yourself during optimization; this does some other
        /// essential housekeeping.
        public int add_symbol(Symbol sym)
        {
            int index = inst().symbols().Count;
            inst().symbols().Add(sym);
            // Mark the symbol as always read. Next time we recompute symbol lifetimes, it'll get the correct range for when it's read and written. But
            // for now, just make sure it doesn't accidentally look entirely unused.
            inst().symbols()[inst().symbols().Count - 1].mark_always_used();
            return index;
        }

        public void debug_opt_impl(string message)
        {
            lock (s_debug_mutex)
            {
                Console.Out.Write(message);
            }
        }

        public void debug_optfmt(string fmt, params object[] args)
        {
            debug_opt_impl(Fmt.format(fmt, args));
        }

        public void debug_opt_ops(int opbegin, int opend, string message)
        {
            Opcode op = inst().ops()[opbegin];
            string oprange = "";
            if (opbegin >= 0 && opend - opbegin > 1)
            {
                oprange = Fmt.format("ops {}-{} ", opbegin, opend);
            }
            else if (opbegin >= 0)
            {
                oprange = Fmt.format("op {} ", opbegin);
            }
            debug_optfmt("  {}{} (@ {}:{})\n", oprange, message, op.sourcefile(), op.sourceline());
        }

        public void debug_turn_into(Opcode op, int numops, string newop, int newarg0, int newarg1, int newarg2, string why)
        {
            int opnum = opnum_of(op);
            string msg;
            if (numops == 1)
            {
                msg = Fmt.format("turned '{}' to '{}", op_string(op), newop);
            }
            else
            {
                msg = Fmt.format("turned to '{}", newop);
            }
            if (newarg0 >= 0)
            {
                msg += Fmt.format(" {}", inst().symbol(newarg0).name());
            }
            if (newarg1 >= 0)
            {
                msg += Fmt.format(" {}", inst().symbol(newarg1).name());
            }
            if (newarg2 >= 0)
            {
                msg += Fmt.format(" {}", inst().symbol(newarg2).name());
            }
            msg += "'";
            if (why != null && why.Length != 0)
            {
                msg += Fmt.format(" : {}", why);
            }
            debug_opt_ops(opnum, opnum + numops, msg);
        }

        /// Turn the op into a new simple unary or binary op with arguments newarg0 (the result, newarg1, and newarg2. If newarg2 < 0, then it's a unary
        /// op, otherwise a binary op. The original arg list must have at least as many operands as the new one, since no new arg space is allocated.
        public void turn_into_new_op(Opcode op, ustring newop, int newarg0, int newarg1, int newarg2, string why)
        {
            int opnum = opnum_of(op);
            System.Diagnostics.Debug.Assert(opnum >= 0 && opnum < inst().ops().Count);
            if (debug() > 1)
            {
                debug_turn_into(op, 1, newop.@string(), newarg0, newarg1, newarg2, why);
            }
            op.reset(newop, newarg2 < 0 ? 2 : 3);
            inst().args()[op.firstarg() + 0] = newarg0;
            op.argwriteonly(0);
            opargsym(op, 0).mark_rw(opnum, false, true);
            inst().args()[op.firstarg() + 1] = newarg1;
            op.argreadonly(1);
            opargsym(op, 1).mark_rw(opnum, true, false);
            if (newarg2 >= 0)
            {
                inst().args()[op.firstarg() + 2] = newarg2;
                op.argreadonly(2);
                opargsym(op, 2).mark_rw(opnum, true, false);
            }
        }

        /// Turn the op into a simple assignment of the new symbol index to the previous first argument of the op. That is, changes "OP arg0 arg1..."
        /// into "assign arg0 newarg".
        public void turn_into_assign(Opcode op, int newarg, string why)
        {
            // We don't know the op num here, so we find the op
            int opnum = opnum_of(op);
            if (debug() > 1)
            {
                debug_turn_into(op, 1, "assign", oparg(op, 0), newarg, -1, why);
            }
            op.reset(u_assign, 2);
            inst().args()[op.firstarg() + 1] = newarg;
            op.argwriteonly(0);
            op.argread(1, true);
            op.argwrite(1, false);
            // Need to make sure the symbol we're assigning is marked as read for this op.
            System.Diagnostics.Debug.Assert(opnum >= 0 && opnum < inst().ops().Count);
            Symbol arg = opargsym(op, 1);
            arg.mark_rw(opnum, true, false);
        }

        public void turn_into_assign(Opcode op, int newarg)
        {
            turn_into_assign(op, newarg, null);
        }

        /// Turn the op into a simple assignment of zero to the previous first argument of the op. That is, changes "OP arg0 arg1 ..." into "assign arg0
        /// zero".
        public void turn_into_assign_zero(Opcode op, string why)
        {
            float[] zero = new float[16];
            Symbol R = inst().argsymbol(op.firstarg() + 0);
            int cind = add_constant(R.typespec(), zero);
            turn_into_assign(op, cind, why);
        }

        /// Turn the op into a simple assignment of one to the previous first argument of the op. That is, changes "OP arg0 arg1 ..." into "assign arg0
        /// one".
        public void turn_into_assign_one(Opcode op, string why)
        {
            Symbol R = inst().argsymbol(op.firstarg() + 0);
            if (R.typespec().is_int())
            {
                int[] one = new int[] { 1 };
                int cind = add_constant(R.typespec(), one);
                turn_into_assign(op, cind, why);
            }
            else
            {
                System.Diagnostics.Debug.Assert(R.typespec().is_triple() || R.typespec().is_float());
                float[] one = new float[] { 1, 1, 1 };
                int cind = add_constant(R.typespec(), one);
                turn_into_assign(op, cind, why);
            }
        }

        /// Turn the op into a no-op. Return 1 if it changed, 0 if it was already a nop.
        public int turn_into_nop(Opcode op, string why)
        {
            if (op.opname() != u_nop)
            {
                if (debug() > 1)
                {
                    debug_turn_into(op, 1, "nop", -1, -1, -1, why);
                }
                op.reset(u_nop, 0);
                return 1;
            }
            return 0;
        }

        public int turn_into_nop(Opcode op)
        {
            return turn_into_nop(op, null);
        }

        /// Turn the whole range [begin,end) into no-ops. Return the number of instructions that were altered.
        public int turn_into_nop(int begin, int end, string why)
        {
            int changed = 0;
            for (int i = begin; i < end; ++i)
            {
                Opcode op = inst().ops()[i];
                if (op.opname() != u_nop)
                {
                    op.reset(u_nop, 0);
                    ++changed;
                }
            }
            if (debug() > 1 && changed != 0)
            {
                debug_turn_into(inst().ops()[begin], end - begin, "nop", -1, -1, -1, why);
            }
            return changed;
        }

        /// Transmute regular function call into a 'no return' function call. The funcationcall_nr is useful to generate debug info for the inlined
        /// function call. Its existence shouldn't otherwise modify resulting code generation.
        public int turn_into_functioncall_nr(Opcode op, string why)
        {
            if (op.opname() == u_functioncall)
            {
                if (debug() > 1)
                {
                    debug_turn_into(op, 1, "functioncall_nr", -1, -1, -1, why);
                }
                op.transmute_opname(u_functioncall_nr);
                return 1;
            }
            return 0;
        }

        /// Insert instruction 'opname' with arguments 'args_to_add' into the code at instruction 'opnum'. The recompute_rw_ranges parameter determines
        /// whether all symbols' read/write ranges should be adjusted to take the new instruction into consideration. Relation indicates its relation to
        /// surrounding instructions: GroupWithPrevious means it should have the same method, sourcefile, and sourceline as the preceding instruction;
        /// GroupWithNext means it should have the same method, sourcefile, and sourceline as the subsequent instruction; NoRelation means we have no
        /// information, so don't copy that info from anywhere.
        public void insert_code(int opnum, ustring opname, IList<int> args_to_add, RecomputeRWRangesOption recompute_rw_ranges, InsertRelation relation)
        {
            List<Opcode> code = inst().ops();
            List<int> opargs = inst().args();
            ustring method = (opnum < code.Count) ? code[opnum].method() : main_method_name;
            int nargs = args_to_add.Count;
            Opcode op = new Opcode(opname, method, opargs.Count, nargs);
            code.Insert(opnum, op);
            for (int i = 0; i < nargs; ++i)
            {
                opargs.Add(args_to_add[i]);
            }
            if (opnum < inst().m_maincodebegin)
            {
                ++inst().m_maincodebegin;
            }
            ++inst().m_maincodeend;
            int rel = (int)relation;
            if ((rel == -1 && opnum > 0) || (rel == 1 && opnum < code.Count - 1))
            {
                code[opnum].method(code[opnum + rel].method());
                code[opnum].source(code[opnum + rel].sourcefile(), code[opnum + rel].sourceline());
            }

            // Unless we were inserting at the end, we may need to adjust the jump addresses of other ops and the param init ranges.
            if (opnum < code.Count - 1)
            {
                // Adjust jump offsets
                foreach (Opcode c in code)
                {
                    for (int j = 0; j < Opcode.max_jumps && c.jump(j) >= 0; ++j)
                    {
                        if (c.jump(j) > opnum)
                        {
                            c.jump(j, c.jump(j) + 1);
                        }
                    }
                }
                // Adjust param init ranges
                foreach (Symbol s in inst().param_range())
                {
                    if (s.initbegin() > opnum)
                    {
                        s.initbegin(s.initbegin() + 1);
                    }
                    if (s.initend() > opnum)
                    {
                        s.initend(s.initend() + 1);
                    }
                }
            }

            // Inserting the instruction may change the read/write ranges of symbols. Not adjusting this can throw off other optimizations.
            if (recompute_rw_ranges == RecomputeRWRangesOption.RecomputeRWRanges)
            {
                foreach (Symbol s in inst().symbols())
                {
                    if (s.everread())
                    {
                        int first = s.firstread(), last = s.lastread();
                        if (first >= opnum)
                        {
                            ++first;
                        }
                        if (last >= opnum)
                        {
                            ++last;
                        }
                        s.set_read(first, last);
                    }
                    if (s.everwritten())
                    {
                        int first = s.firstwrite(), last = s.lastwrite();
                        if (first >= opnum)
                        {
                            ++first;
                        }
                        if (last >= opnum)
                        {
                            ++last;
                        }
                        s.set_write(first, last);
                    }
                }
            }

            // Adjust the basic block IDs and which instructions are inside conditionals.
            if (m_bblockids.Count != 0)
            {
                System.Diagnostics.Debug.Assert(m_bblockids.Count == code.Count - 1);
                m_bblockids.Insert(opnum, m_bblockids[Math.Min(opnum, m_bblockids.Count - 1)]);
            }
            if (m_in_conditional.Count != 0)
            {
                System.Diagnostics.Debug.Assert(m_in_conditional.Count == code.Count - 1);
                m_in_conditional.Insert(opnum, m_in_conditional[Math.Min(opnum, m_in_conditional.Count - 1)]);
            }
            if (m_in_loop.Count != 0)
            {
                System.Diagnostics.Debug.Assert(m_in_loop.Count == code.Count - 1);
                m_in_loop.Insert(opnum, m_in_loop[Math.Min(opnum, m_in_loop.Count - 1)]);
            }
            // If the first return happened after this, bump it up
            if (m_first_return >= opnum)
            {
                ++m_first_return;
            }

            if (opname == u_if)
            {
                // special case for 'if' -- the arg is read, not written
                inst().symbol(args_to_add[0]).mark_rw(opnum, true, false);
            }
            else if (opname != u_useparam)
            {
                // Mark the args as being used for this op (assume that the first is written, the others are read).
                for (int a = 0; a < nargs; ++a)
                {
                    inst().symbol(args_to_add[a]).mark_rw(opnum, a > 0, a == 0);
                }
            }
        }

        public void insert_code(int opnum, ustring opname, IList<int> args_to_add, RecomputeRWRangesOption recompute_rw_ranges)
        {
            insert_code(opnum, opname, args_to_add, recompute_rw_ranges, InsertRelation.GroupWithNext);
        }

        /// insert_code with explicit arguments (up to 4, a value of -1 means the arg isn't used). Presume recompute_rw_ranges is true.
        public void insert_code(int opnum, ustring opname, InsertRelation relation, int arg0, int arg1, int arg2, int arg3)
        {
            List<int> args = new List<int>(4);
            if (arg0 >= 0)
            {
                args.Add(arg0);
            }
            if (arg1 >= 0)
            {
                args.Add(arg1);
            }
            if (arg2 >= 0)
            {
                args.Add(arg2);
            }
            if (arg3 >= 0)
            {
                args.Add(arg3);
            }
            insert_code(opnum, opname, args, RecomputeRWRangesOption.RecomputeRWRanges, relation);
        }

        public void insert_code(int opnum, ustring opname, InsertRelation relation, int arg0, int arg1, int arg2)
        {
            insert_code(opnum, opname, relation, arg0, arg1, arg2, -1);
        }

        public void insert_code(int opnum, ustring opname, InsertRelation relation, int arg0, int arg1)
        {
            insert_code(opnum, opname, relation, arg0, arg1, -1, -1);
        }

        public void insert_code(int opnum, ustring opname, InsertRelation relation, int arg0)
        {
            insert_code(opnum, opname, relation, arg0, -1, -1, -1);
        }

        /// Insert a 'useparam' instruction in front of instruction 'opnum', to reference the symbols in 'params'.
        public void insert_useparam(int opnum, List<int> params_to_use)
        {
            System.Diagnostics.Debug.Assert(params_to_use.Count > 0);
            List<Opcode> code = inst().ops();
            insert_code(opnum, u_useparam, params_to_use, RecomputeRWRangesOption.RecomputeRWRanges, InsertRelation.GroupWithNext);

            // All ops are "read"
            code[opnum].argwrite(0, false);
            code[opnum].argread(0, true);
            if (opnum < code.Count - 1)
            {
                // We have no parse node, but we set the new instruction's "source" to the one of the statement right after.
                code[opnum].source(code[opnum + 1].sourcefile(), code[opnum + 1].sourceline());
                // Set the method id to the same as the statement right after
                code[opnum].method(code[opnum + 1].method());
            }
            else
            {
                // If there IS no "next" instruction, just call it main
                code[opnum].method(main_method_name);
            }
        }

        /// Add a 'useparam' before any op that reads parameters. This is what tells the runtime that it needs to run the layer it came from, if not
        /// already done.
        public void add_useparam(List<Symbol> allsyms)
        {
            List<Opcode> code = inst().ops();
            List<int> opargs = inst().args();

            // Mark all symbols as un-initialized
            foreach (Symbol s in inst().symbols())
            {
                s.initialized(false);
            }

            if (inst().m_maincodebegin < 0)
            {
                inst().m_maincodebegin = code.Count;
            }

            // Take care of the output params right off the bat -- as soon as the shader starts running 'main'.
            List<int> outputparams = new List<int>();
            for (int i = 0; i < inst().symbols().Count; ++i)
            {
                Symbol s = inst().symbol(i);
                if (s.symtype() == SymType.SymTypeOutputParam
                    && (s.connected() || s.connected_down() || s.renderer_output() || (s.valuesource() == Symbol.ValueSource.DefaultVal && s.has_init_ops())))
                {
                    outputparams.Add(i);
                    s.initialized(true);
                }
            }
            if (outputparams.Count != 0)
            {
                insert_useparam(inst().m_maincodebegin, outputparams);
            }

            // Figure out which statements are inside conditional states
            find_conditionals();

            // Loop over all ops...
            for (int opnum = 0; opnum < code.Count; ++opnum)
            {
                Opcode op = code[opnum];  // handy ref to the op
                if (op.opname() == u_useparam)
                {
                    continue;  // skip useparam ops themselves, if we hit one
                }
                bool simple_assign = is_simple_assign(op);
                bool in_main_code = (opnum >= inst().m_maincodebegin);
                List<int> @params = new List<int>();  // list of params referenced by this op
                // For each argument...
                for (int a = 0; a < op.nargs(); ++a)
                {
                    int argind = op.firstarg() + a;
                    Symbol s = inst().argsymbol(argind);
                    System.Diagnostics.Debug.Assert(s.dealias() == s);
                    // If this arg is a param and is read, remember it
                    if (s.symtype() != SymType.SymTypeParam && s.symtype() != SymType.SymTypeOutputParam)
                    {
                        continue;  // skip non-params
                    }
                    // skip if we've already 'usedparam'ed it unconditionally
                    if (s.initialized() && in_main_code)
                    {
                        continue;
                    }

                    bool inside_init = (opnum >= s.initbegin() && opnum < s.initend());
                    if (op.argread(a) || (op.argwrite(a) && !inside_init))
                    {
                        // Don't add it more than once
                        if (@params.IndexOf(opargs[argind]) < 0)
                        {
                            // If this arg is the one being written to by a "simple" assignment, it doesn't need a useparam here.
                            if (!(simple_assign && a == 0))
                            {
                                @params.Add(opargs[argind]);
                            }
                            // mark as already initialized unconditionally, if we do
                            if (op_is_unconditionally_executed(opnum) && op.method() == main_method_name)
                            {
                                s.initialized(true);
                            }
                        }
                    }
                }

                // If the arg we are examining read any params, insert a "useparam" op whose arguments are the list of params we are about to use.
                if (@params.Count != 0)
                {
                    insert_useparam(opnum, @params);
                    // Skip the op we just added
                    ++opnum;
                }
            }

            // Mark all symbols as un-initialized
            foreach (Symbol s in inst().symbols())
            {
                s.initialized(false);
            }

            // Re-track variable lifetimes, since the inserted useparam instructions will have change the instruction numbers.
            find_basic_blocks();
            track_variable_lifetimes(allsyms);
        }

        public void register_message(ustring name)
        {
            m_local_messages_sent.Add(name);
        }

        public void register_unknown_message()
        {
            m_local_unknown_message_sent = true;
        }

        /// Is it possible that the message with the given name was set?
        public bool message_possibly_set(ustring name)
        {
            return m_local_unknown_message_sent || m_unknown_message_sent || m_messages_sent.IndexOf(name) >= 0 || m_local_messages_sent.IndexOf(name) >= 0;
        }

        /// For all the instance's parameters (that can't be overridden by the geometry), if they can be found to be effectively constants or globals,
        /// make constants for them and alias them to the constant. If they are connected to an earlier layer's output, if it can determine that the
        /// output will be a constant or global, then sever the connection and just alias our parameter to that value.
        public void simplify_params()
        {
            for (int i = inst().firstparam(); i < inst().lastparam(); ++i)
            {
                Symbol s = inst().symbol(i);
                if (s.symtype() != SymType.SymTypeParam)
                {
                    continue;  // Skip non-params
                }
                // Don't simplify params that are interpolated or interactively editable
                if (s.interpolated() || s.interactive())
                {
                    continue;
                }
                if (s.typespec().is_structure() || s.typespec().is_closure_based())
                {
                    continue;  // We don't mess with struct placeholders or closures
                }

                if (s.valuesource() == Symbol.ValueSource.InstanceVal)
                {
                    // Instance value -- turn it into a constant and remove init ops
                    make_symbol_room(1);
                    s = inst().symbol(i);  // In case make_symbol_room changed ptrs
                    int cind = add_constant(s.typespec(), s.data());
                    global_alias(i, cind);  // Alias this symbol to the new const
                    turn_into_nop(s.initbegin(), s.initend(), "instance value doesn't need init ops");
                }
                else if (s.valuesource() == Symbol.ValueSource.DefaultVal && !s.has_init_ops())
                {
                    // Plain default value without init ops -- turn it into a constant
                    make_symbol_room(1);
                    s = inst().symbol(i);  // In case make_symbol_room changed ptrs
                    int cind = add_constant(s.typespec(), s.data(), s.typespec().simpletype(), 0);
                    global_alias(i, cind);  // Alias this symbol to the new const
                }
                else if (s.valuesource() == Symbol.ValueSource.DefaultVal && s.has_init_ops())
                {
                    // Default val comes from init ops -- special cases? Yes, if it's a simple assignment from a global whose value is not reassigned
                    // later, we can just alias it, and if we're lucky that may eliminate all uses of the parameter.

                    // First, trim init ops in case nops have accumulated
                    while (s.has_init_ops() && op(s.initbegin()).opname() == u_nop)
                    {
                        s.initbegin(s.initbegin() + 1);
                    }
                    while (s.has_init_ops() && op(s.initend() - 1).opname() == u_nop)
                    {
                        s.initend(s.initend() - 1);
                    }
                    if (s.initbegin() == s.initend() - 1)  // just one op
                    {
                        Opcode initop = inst().ops()[s.initbegin()];
                        if (initop.opname() == u_assign)
                        {
                            // The default value has init ops, but they consist of just a single assignment op...
                            Symbol src = inst().argsymbol(initop.firstarg() + 1);
                            // Is it assigning a global, or a parameter that's got a default or instance value and isn't on the geom, and its value is
                            // never changed and the types match?
                            if ((src.symtype() == SymType.SymTypeGlobal || src.symtype() == SymType.SymTypeConst
                                 || (src.symtype() == SymType.SymTypeParam && src.lockgeom()
                                     && (src.valuesource() == Symbol.ValueSource.DefaultVal || src.valuesource() == Symbol.ValueSource.InstanceVal)))
                                && !src.everwritten() && PvtFunctions.equivalent(src.typespec(), s.typespec()))
                            {
                                // Great, so let's remember the alias. We can't call global_alias() here, because we're still in init ops, that'll
                                // screw us up. So we just record it in m_param_aliases and then we'll establish the global aliases when we hit the
                                // main code.
                                m_param_aliases[i] = inst().arg(initop.firstarg() + 1);
                            }
                        }
                    }
                }
                else if (s.valuesource() == Symbol.ValueSource.ConnectedVal)
                {
                    // It's connected to an earlier layer. If the output var of the upstream shader is effectively constant or a global, then so is this
                    // variable.
                    foreach (Connection c in inst().connections())
                    {
                        if (c.dst.param != i)
                        {
                            continue;
                        }
                        if (c.dst.is_complete())
                        {
                            // All components are being set through either float->triple or triple->triple. Get rid of the un-needed init ops.
                            turn_into_nop(s.initbegin(), s.initend(), "connected value doesn't need init ops");
                        }
                        if (c.is_complete())
                        {
                            // srcsym is the earlier group's output param, which is fully connected as the input to the param we're examining.
                            ShaderInstance uplayer = group()[c.srclayer];
                            Symbol srcsym = uplayer.symbol(c.src.param);
                            if (srcsym.interpolated())
                            {
                                continue;  // Not if it can be overridden by geometry
                            }

                            // Is the source symbol known to be a global, from earlier analysis by find_params_holding_globals? If so, make sure the
                            // global is in this instance's symbol table, and alias the parameter to it.
                            Dictionary<ustring, ustring> g = m_params_holding_globals[c.srclayer];
                            ustring found;
                            if (g.TryGetValue(srcsym.name(), out found))
                            {
                                if (debug() > 1)
                                {
                                    debug_optfmt("Remapping {}.{} because it's connected to {}.{}, which is known to be {}\n", inst().layername(), s.name(),
                                                 uplayer.layername(), srcsym.name(), found);
                                }
                                make_symbol_room(1);
                                s = inst().symbol(i);  // In case make_symbol_room changed ptrs
                                int ind = add_global(found, srcsym.typespec());
                                global_alias(i, ind);
                                System.Threading.Interlocked.Add(ref shadingsys().m_stat_global_connections, 1);
                                break;
                            }

                            if (!srcsym.everwritten()
                                && (srcsym.valuesource() == Symbol.ValueSource.DefaultVal || srcsym.valuesource() == Symbol.ValueSource.InstanceVal)
                                && !srcsym.has_init_ops())
                            {
                                make_symbol_room(1);
                                s = inst().symbol(i);  // In case make_symbol_room changed ptrs
                                int cind = add_constant(s.typespec(), srcsym.data(), srcsym.typespec().simpletype(), 0);
                                // Alias this symbol to the new const
                                global_alias(i, cind);
                                make_param_use_instanceval(s, "- upstream layer sets it to a constant");
                                replace_param_value(s, srcsym.data(), srcsym.typespec());
                                System.Threading.Interlocked.Add(ref shadingsys().m_stat_const_connections, 1);
                                break;
                            }
                        }
                    }
                    // FIXME / N.B.: We only optimize "fully complete" connections, not those involving individual components or array elements of the
                    // connected parameters, because we sure don't track the constness or aliasing of individual components/element, only whole
                    // variables. But there are two cases where the logic above fails to fully exploit the connection propagating a constant value.
                    // (a) Partial-to-whole connections, for example connecting one component of an upstream triple output to a downstream float
                    // input, should propagate the constant, but we currently neglect this case. (b) If *multiple* connections combine to fully
                    // propagate values, for example if someone was foolish enough to connect R, G, and B components of color parameters *separately*,
                    // we sure don't notice that and treat it as a full connection of the color.
                }
            }
        }

        /// For all the instance's parameters, if they are simply assigned globals, record that in m_params_holding_globals.
        public void find_params_holding_globals()
        {
            foreach (Symbol s in inst().param_range())
            {
                // Skip if this isn't a shader output parameter that's connected to a later layer.
                if (s.symtype() != SymType.SymTypeParam && s.symtype() != SymType.SymTypeOutputParam)
                {
                    continue;  // Skip non-params
                }
                if (!s.connected_down())
                {
                    continue;  // Skip unconnected params -- who cares
                }
                if (s.valuesource() != Symbol.ValueSource.DefaultVal)
                {
                    continue;  // Skip -- must be connected or an instance value
                }
                if (s.firstwrite() < 0 || s.firstwrite() != s.lastwrite())
                {
                    continue;  // Skip -- written more than once
                }

                int opnum = s.firstwrite();
                Opcode o = inst().ops()[opnum];
                if (o.opname() != u_assign || !op_is_unconditionally_executed(opnum))
                {
                    continue;  // Not a simple assignment unconditionally performed
                }

                // what s is assigned from (fully dealiased)
                Symbol src = inst().symbol(dealias_symbol(oparg(o, 1), opnum));

                if (src.symtype() != SymType.SymTypeGlobal)
                {
                    continue;  // only interested in global assignments
                }

                if (debug() > 1)
                {
                    debug_optfmt("I think that {}.{} will always be {}\n", inst().layername(), s.name(), src.name());
                }
                m_params_holding_globals[layer()][s.name()] = src.name();
            }
        }

        /// For 'R = A_const' where R and A are different, but coerceable, types, turn it into a constant assignment of the exact type. Return true if
        /// a change was made, otherwise return false.
        public bool coerce_assigned_constant(Opcode op)
        {
            System.Diagnostics.Debug.Assert(op.opname() == u_assign);
            Symbol R = inst().argsymbol(op.firstarg() + 0);
            Symbol A = inst().argsymbol(op.firstarg() + 1);

            if (!A.is_constant() || R.typespec().is_closure_based())
            {
                return false;  // we don't handle those cases
            }

            // turn 'R_float = A_int_const' into a float const assignment
            if (A.typespec().is_int() && R.typespec().is_float())
            {
                int cind = add_constant(A.coerce_float());
                turn_into_assign(op, cind, "coerce to correct type");
                return true;
            }

            // turn 'R_int = A_float_const' into an int const assignment
            if (A.typespec().is_float() && R.typespec().is_int())
            {
                int cind = add_constant(unchecked((int)A.get_float()));
                turn_into_assign(op, cind, "coerce to correct type");
                return true;
            }

            // turn 'R_triple = A_int_or_float_const' into a float const assignment
            if (A.typespec().is_int_or_float() && R.typespec().is_triple())
            {
                float f = A.coerce_float();
                int cind = add_constant(R.typespec(), new float[] { f, f, f });
                turn_into_assign(op, cind, "coerce to correct type");
                return true;
            }

            // Turn 'R_triple = A_other_triple_constant' into a triple const assign
            if (A.typespec().is_triple() && R.typespec().is_triple() && A.typespec() != R.typespec())
            {
                int cind = add_constant(R.typespec(), A.data());
                turn_into_assign(op, cind, "coerce to correct type");
                return true;
            }

            // turn 'R_matrix = A_int_or_float_const' into a matrix const assignment
            if (A.typespec().is_int_or_float() && R.typespec().is_matrix())
            {
                float f = A.coerce_float();
                int cind = add_constant(R.typespec(), new float[] { f, 0, 0, 0, 0, f, 0, 0, 0, 0, f, 0, 0, 0, 0, f });
                turn_into_assign(op, cind, "coerce to correct type");
                return true;
            }

            return false;
        }

        /// Clear the stale symbol list -- we do this when entering a new basic block.
        public void clear_stale_syms()
        {
            m_stale_syms.Clear();
        }

        /// Take a symbol out of the stale list -- we do this when a symbol is used in any way.
        public void use_stale_sym(int sym)
        {
            m_stale_syms.Remove(sym);
        }

        /// Is the given symbol stale? A "stale" symbol is one that, within the current basic block, has been assigned in a simple manner (by a single op
        /// with no other side effects), but not yet used. The point is that if they are simply assigned again before being used, that first assignment
        /// can be turned into a no-op.
        public bool sym_is_stale(int sym)
        {
            return m_stale_syms.ContainsKey(sym);
        }

        /// Is the op a "simple" assignment (arg 0 completely overwritten, no side effects or funny business)? Optional OpDescriptor is passed to save an
        /// extra lookup.
        public bool is_simple_assign(Opcode op, OpDescriptor opd)
        {
            // Simple only if arg0 is the only write, and is write only.
            if (op.argwrite_bits() != 1 || op.argread(0))
            {
                return false;
            }
            if (opd == null)
            {
                opd = shadingsys().op_descriptor(op.opname());
            }
            if (opd == null || !opd.simple_assign)
            {
                return false;  // reject all other known non-simple assignments
            }
            // Make sure the result isn't also read
            int result = oparg(op, 0);
            for (int i = 1, e = op.nargs(); i < e; ++i)
            {
                if (oparg(op, i) == result)
                {
                    return false;
                }
            }
            return true;
        }

        public bool is_simple_assign(Opcode op)
        {
            return is_simple_assign(op, null);
        }

        /// Called when symbol sym is "simply" assigned at the given op. An assignment is considered simple if it completely overwrites the symbol in a
        /// single op and has no side effects. When this happens, we mark the symbol as "stale", meaning it's got a value that hasn't been read yet. If
        /// it's wholly assigned again before it's read, we can go back and remove the earlier assignment.
        public void simple_sym_assign(int sym, int opnum)
        {
            if (optimize() >= 2 && m_opt_stale_assign)
            {
                int staleop;
                if (m_stale_syms.TryGetValue(sym, out staleop))
                {
                    Opcode uselessop = inst().ops()[staleop];
                    if (uselessop.opname() != u_nop && uselessop.opname() != u_functioncall_nr)
                    {
                        turn_into_nop(uselessop, debug() > 1
                                                     ? Fmt.format("remove stale value assignment to {}, reassigned on op {}", opargsym(uselessop, 0).name(), opnum)
                                                     : "");
                    }
                }
            }
            m_stale_syms[sym] = opnum;
        }

        /// Return true if assignments to A on this op have no effect because they will not be subsequently used.
        public bool unread_after(Symbol A, int opnum)
        {
            // Try to figure out if this symbol is completely unused after this op (and thus, any values written to it now will never be needed).

            // Globals may be read by later layers
            if (A.symtype() == SymType.SymTypeGlobal)
            {
                return false;
            }

            // Params may be read afterwards if connected to a downstream layer or if "elide_unconnected_outputs" is turned off.
            if (A.symtype() == SymType.SymTypeOutputParam || A.symtype() == SymType.SymTypeParam)
            {
                if (!m_opt_elide_unconnected_outputs)
                {
                    return false;  // Asked not do do this optimization
                }
                if (A.connected_down())
                {
                    return false;  // Connected to something downstream
                }
                if (A.renderer_output())
                {
                    return false;  // This is a renderer output -- don't cull it
                }
            }

            // For all else, check if it's either never read at all in this layer or it's only read earlier and we're not part of a loop
            return !A.everread() || (A.lastread() <= opnum && !m_in_loop[opnum]);
        }

        // The array that holds the instance's own value of a parameter, made (from the default) if the instance doesn't have one yet
        private object instance_param_storage(Symbol R)
        {
            int index = inst().symbolindex(R);
            System.Diagnostics.Debug.Assert(index >= 0 && index < inst().m_paramdata.Length);
            object storage = inst().m_paramdata[index];
            if (storage == null)
            {
                storage = ShaderInstance.clone_array(R.data());
                inst().m_paramdata[index] = storage;
            }
            return storage;
        }

        /// Replace R's instance value with new data.
        public void replace_param_value(Symbol R, object newdata, TypeSpec newdata_type)
        {
            System.Diagnostics.Debug.Assert(R.symtype() == SymType.SymTypeParam || R.symtype() == SymType.SymTypeOutputParam);
            TypeDesc Rtype = R.typespec().simpletype();
            int Rnvals = Rtype.aggregate * Rtype.numelements();
            TypeDesc Ntype = newdata_type.simpletype();
            if (Ntype == new TypeDesc())
            {
                Ntype = Rtype;
            }
            int Nnvals = Ntype.aggregate * Ntype.numelements();
            object storage = instance_param_storage(R);
            if (Rtype.basetype == TypeDesc.FLOAT && Ntype.basetype == TypeDesc.FLOAT)
            {
                float[] Rdefault = (float[])storage;
                float[] src = (float[])newdata;
                if (Rnvals == Nnvals)  // straight copy
                {
                    for (int i = 0; i < Rnvals; ++i)
                    {
                        Rdefault[i] = src[i];
                    }
                }
                else if (Nnvals == 1)  // scalar -> aggregate, by replication
                {
                    for (int i = 0; i < Rnvals; ++i)
                    {
                        Rdefault[i] = src[0];
                    }
                }
                else
                {
                    throw new InvalidOperationException("replace_param_value: unexpected types");
                }
            }
            else if (Rtype.basetype == TypeDesc.FLOAT && Ntype.basetype == TypeDesc.INT)
            {
                // Careful, this is an int-to-float conversion
                float[] Rdefault = (float[])storage;
                int[] src = (int[])newdata;
                if (Rnvals == Nnvals)  // straight copy
                {
                    for (int i = 0; i < Rnvals; ++i)
                    {
                        Rdefault[i] = src[i];
                    }
                }
                else if (Nnvals == 1)  // scalar -> aggregate, by replication
                {
                    for (int i = 0; i < Rnvals; ++i)
                    {
                        Rdefault[i] = src[0];
                    }
                }
                else
                {
                    throw new InvalidOperationException("replace_param_value: unexpected types");
                }
            }
            else if (Rtype.basetype == TypeDesc.INT && Ntype.basetype == TypeDesc.INT && Rnvals == Nnvals)
            {
                int[] Rdefault = (int[])storage;
                int[] src = (int[])newdata;
                for (int i = 0; i < Rnvals; ++i)
                {
                    Rdefault[i] = src[i];
                }
            }
            else if (Rtype.basetype == TypeDesc.STRING && Ntype.basetype == TypeDesc.STRING && Rnvals == Nnvals)
            {
                ustring[] Rdefault = (ustring[])storage;
                ustring[] src = (ustring[])newdata;
                for (int i = 0; i < Rnvals; ++i)
                {
                    Rdefault[i] = src[i];
                }
            }
            else
            {
                throw new InvalidOperationException("replace_param_value: unexpected types");
            }
        }

        /// Symbol R in the current instance has a connection or init ops we no longer need; turn it into a a plain old instance-value parameter.
        public void make_param_use_instanceval(Symbol R, string why)
        {
            if (debug() > 1)
            {
                Console.Out.Write("Turning " + R.valuesourcename() + ' ' + R.typespec().@string() + ' ' + R.name().@string() + " into an instance value " + why
                                  + "\n");
            }

            // Mark its source as the instance value, not connected
            R.valuesource(Symbol.ValueSource.InstanceVal);
            // If it isn't a connection or computed, it doesn't need derivs.
            R.has_derivs(false);

            // Point the symbol's data pointer to its instance value
            object Rdefault = instance_param_storage(R);
            System.Diagnostics.Debug.Assert(Rdefault != null);
            R.set_dataptr(SymArena.Absolute, Rdefault);

            // Get rid of any init ops
            if (R.has_init_ops())
            {
                turn_into_nop(R.initbegin(), R.initend(), "init ops not needed");
                R.initbegin(0);
                R.initend(0);
            }
            // Erase R's incoming connections
            inst().connections().RemoveAll(c => inst().symbol(c.dst.param) == R);
        }

        /// Check for conditions under which assignments to output parameters can be removed. Return true if the assignment is removed entirely.
        public bool outparam_assign_elision(int opnum, Opcode op)
        {
            System.Diagnostics.Debug.Assert(op.opname() == u_assign);
            Symbol R = inst().argsymbol(op.firstarg() + 0);
            Symbol A = inst().argsymbol(op.firstarg() + 1);

            if (R.symtype() != SymType.SymTypeOutputParam)
            {
                return false;  // This logic is only about output params
            }

            // Check for assignment of output params that are written only once in the whole shader -- on this statement -- and assigned a constant, and
            // the assignment is unconditional. In that case, just alias it to the constant from here on out.
            if (  // R is being assigned a constant of the right type:
                A.is_constant() && R.typespec() == A.typespec()
                // FIXME -- can this be equivalent() rather than == ?
                // and it's written only on this op, and unconditionally:
                && R.firstwrite() == opnum && R.lastwrite() == opnum && !m_in_conditional[opnum]
                // and this is not a case of an init op for an output param that actually will get an instance value or a connection:
                && !((R.valuesource() == Symbol.ValueSource.InstanceVal || R.connected()) && R.initbegin() <= opnum && R.initend() > opnum))
            {
                // Alias it to the constant it's being assigned
                int cind = inst().args()[op.firstarg() + 1];
                global_alias(inst().args()[op.firstarg()], cind);
                // If it's also never read before this assignment and isn't a designated renderer output (which we obviously must write!), just
                // replace its default value entirely and get rid of the assignment.
                if (R.firstread() > opnum && !R.renderer_output() && m_opt_elide_unconnected_outputs)
                {
                    make_param_use_instanceval(R, Fmt.format("- written once, with a constant ({}), before any reads", const_value_as_string(A)));
                    replace_param_value(R, A.data(), A.typespec());
                    turn_into_nop(op, debug() > 1 ? Fmt.format("oparam {} never subsequently read or connected", R.name()) : "");
                    return true;
                }
            }

            // If the output param will neither be read later in the shader nor connected to a downstream layer, then we don't really need this assignment
            // at all. Note that unread_after() does take into consideration whether it's a renderer output.
            if (unread_after(R, opnum))
            {
                turn_into_nop(op, debug() > 1 ? Fmt.format("oparam {} never subsequently read or connected", R.name()) : "");
                return true;
            }

            return false;
        }

        /// If every potentially-written argument to this op is NEVER read, turn it into a nop and return true. We don't do this to ops that have no
        /// written args at all, since they tend to have side effects (e.g., printf, setmessage).
        public bool useless_op_elision(Opcode op, int opnum)
        {
            if (op.nargs() != 0)
            {
                bool writes_something = false;
                for (int a = 0; a < op.nargs(); ++a)
                {
                    if (op.argwrite(a))
                    {
                        writes_something = true;
                        Symbol A = opargsym(op, a);
                        if (!unread_after(A, opnum))
                        {
                            return false;
                        }
                    }
                }
                // If we get this far, nothing written had any effect
                if (writes_something)
                {
                    // Enumerate exceptions -- ops that write something, but have side effects that means they shouldn't be eliminated.
                    if (op.opname() == u_pointcloud_write)
                    {
                        return false;
                    }
                    // It's a useless op, eliminate it
                    turn_into_nop(op, "eliminated op whose writes will never be read");
                    return true;
                }
            }
            return false;
        }

        /// Return the index of the symbol ultimately de-aliases to (it may be itself, if it doesn't alias to anything else). Local block aliases are
        /// considered higher precedent than global aliases.
        public int dealias_symbol(int symindex, int opnum)
        {
            // N.B. the C++ loop is "do { ... continue; ... } while (0)", in which 'continue' leaves the loop: at most one alias is followed
            do
            {
                int i = block_alias(symindex);
                if (i >= 0)
                {
                    // block-specific alias for the sym
                    symindex = i;
                    continue;
                }
                int foundindex;
                if (m_symbol_aliases.TryGetValue(symindex, out foundindex))
                {
                    // permanent alias for the sym
                    symindex = foundindex;
                    continue;
                }
                if (inst().symbol(symindex).symtype() == SymType.SymTypeParam && opnum >= inst().maincodebegin())
                {
                    // Only check parameter aliases for main code
                    if (m_param_aliases.TryGetValue(symindex, out foundindex))
                    {
                        symindex = foundindex;
                        continue;
                    }
                }
            } while (false);
            return symindex;
        }

        public int dealias_symbol(int symindex)
        {
            return dealias_symbol(symindex, -1);
        }

        /// Return the index of the symbol that 'symindex' aliases to, locally, or -1 if it has no block-local alias.
        public int block_alias(int symindex)
        {
            int i;
            return m_block_aliases.TryGetValue(symindex, out i) ? i : -1;
        }

        /// Set the new block-local alias of 'symindex' to 'alias'.
        public void block_alias(int symindex, int alias)
        {
            m_block_aliases[symindex] = alias;
        }

        /// Reset the block-local alias of 'symindex' so it doesn't alias to anything. Also will unalias from any saved alias lists on the
        /// m_block_aliases_stack.
        public void block_unalias(int symindex)
        {
            m_block_aliases.Remove(symindex);
            // In addition to the current block_aliases, unalias from any saved alias lists.
            foreach (Dictionary<int, int> ba in m_block_aliases_stack)
            {
                ba.Remove(symindex);
            }
        }

        /// Clear local block aliases for any args that are written by this op.
        public void block_unalias_written_args(Opcode op)
        {
            for (int i = 0, e = op.nargs(); i < e; ++i)
            {
                if (op.argwrite(i))
                {
                    block_unalias(inst().arg(op.firstarg() + i));
                }
            }
        }

        /// Reset all block-local aliases (done when we enter a new basic block). If new_block_aliases is non-null, copy its contents to the current block
        /// aliases.
        public void clear_block_aliases(Dictionary<int, int> new_block_aliases)
        {
            if (new_block_aliases != null)
            {
                if (!ReferenceEquals(new_block_aliases, m_block_aliases))
                {
                    m_block_aliases = new Dictionary<int, int>(new_block_aliases);
                }
            }
            else
            {
                m_block_aliases.Clear();
            }
        }

        public void clear_block_aliases()
        {
            clear_block_aliases(null);
        }

        /// Set the new global alias of 'symindex' to 'alias'.
        public void global_alias(int symindex, int alias)
        {
            m_symbol_aliases[symindex] = alias;
        }

        /// Make sure there's room for at least one more symbol, so that we can add a const if we need to, without worrying about the addresses of
        /// symbols changing if we add a new one soon.
        public void make_symbol_room(int howmany)
        {
            inst().make_symbol_room(howmany);
        }

        /// Return the index of the next instruction within the same basic block that isn't a NOP. If there are no more non-NOP instructions in the same
        /// basic block as opnum, return 0.
        public int next_block_instruction(int opnum)
        {
            int end = inst().ops().Count;
            for (int n = opnum + 1; n < end && m_bblockids[n] == m_bblockids[opnum]; ++n)
            {
                if (inst().ops()[n].opname() != u_nop && inst().ops()[n].opname() != u_functioncall_nr)
                {
                    return n;  // Found it!
                }
            }
            return 0;  // End of ops or end of basic block
        }

        /// Perform peephole optimization on pairs of adjacent instructions within the same basic block.
        public int peephole2(int opnum, int op2num)
        {
            Opcode op = inst().ops()[opnum];
            Opcode next = inst().ops()[op2num];

            // N.B. Some of these transformations may look strange, you may think "nobody will write code that does that", but (a) they do; and (b) it
            // can end up like that after other optimizations have changed the code around.

            // Ping-pong assignments can eliminate the second one:
            //     assign a b
            //     assign b a    <-- turn into nop
            // But note that if a is an int and b is a float, this transformation is not safe because of the intentional truncation.
            if (op.opname() == u_assign && next.opname() == u_assign)
            {
                Symbol a = opargsym(op, 0);
                Symbol b = opargsym(op, 1);
                Symbol c = opargsym(next, 0);
                Symbol d = opargsym(next, 1);
                if (a == d && b == c)
                {
                    // Exclude the integer truncation case
                    if (!(a.typespec().is_int() && b.typespec().is_float_based()))
                    {
                        turn_into_nop(next, "ping-pong assignments");
                        return 1;
                    }
                }
            }

            // Daisy chain assignments -> use common source
            //     assign a b
            //     assign c a
            // turns into:
            //     assign a b
            //     assign c b
            // This may allow a to be eliminated if it's not used elsewhere. But note that this doesn't work for float = int = float, which
            // intentionally truncates before the assignment to c!
            if (op.opname() == u_assign && next.opname() == u_assign)
            {
                Symbol a = opargsym(op, 0);
                Symbol b = opargsym(op, 1);
                Symbol c = opargsym(next, 0);
                Symbol d = opargsym(next, 1);
                if (a == d && PvtFunctions.assignable(c.typespec(), b.typespec()))
                {
                    // Exclude the float=int=float case
                    if (!(a.typespec().is_int() && b.typespec().is_float_based() && c.typespec().is_float_based() && !c.typespec().is_array()))
                    {
                        turn_into_assign(next, inst().arg(op.firstarg() + 1), "daisy-chain assignments");
                        return 1;
                    }
                }
            }

            // Look for adjacent add and subtract of the same value:
            //     add a a b
            //     sub a a b
            // (or vice versa)
            if (((op.opname() == u_add && next.opname() == u_sub) || (op.opname() == u_sub && next.opname() == u_add))
                && opargsym(op, 0) == opargsym(next, 0) && opargsym(op, 1) == opargsym(next, 1) && opargsym(op, 2) == opargsym(next, 2)
                && opargsym(op, 0) == opargsym(op, 1))
            {
                turn_into_nop(op, "simplify add/sub pair");
                turn_into_nop(next, "simplify add/sub pair");
                return 2;
            }

            // Look for add of a value then subtract of the same value
            //     add a b c     or:    sub a b c
            //     sub d a c            add d a c
            // the second instruction should be changed to
            //     assign d b
            // and furthermore, if the only use of a is on these two lines or if a == d, then the first instruction can be changed to a 'nop'.
            // Careful, "only used on these two lines" can be tricky if 'a' is a global or output parameter, which are used after the shader finishes!
            if (((op.opname() == u_add && next.opname() == u_sub) || (op.opname() == u_sub && next.opname() == u_add))
                && opargsym(op, 0) == opargsym(next, 1) && opargsym(op, 2) == opargsym(next, 2) && opargsym(op, 0) != opargsym(next, 2) /* a != c */)
            {
                Symbol a = opargsym(op, 0);
                Symbol d = opargsym(next, 0);
                turn_into_assign(next, oparg(op, 1) /*b*/, "simplify add/sub pair");
                if ((a.firstuse() >= opnum && a.lastuse() <= op2num && ((a.symtype() != SymType.SymTypeGlobal && a.symtype() != SymType.SymTypeOutputParam)))
                    || a == d)
                {
                    turn_into_nop(op, "simplify add/sub pair");
                    return 2;
                }
                else
                {
                    return 1;
                }
            }

            // Look for simple functions followed by an assignment:
            //    OP a b...
            //    assign c a
            // If OP is "simple" (completely overwrites its first argument, only reads the rest), and a and c are the same type, and a is never used
            // again, then we can replace those two instructions with:
            //    OP c b...
            // Careful, "never used again" can be tricky if 'a' is a global or output parameter, which are used after the shader finishes!
            if (next.opname() == u_assign && op.nargs() >= 1 && opargsym(op, 0) == opargsym(next, 1) && is_simple_assign(op))
            {
                Symbol a = opargsym(op, 0);
                Symbol c = opargsym(next, 0);
                if (a.firstuse() >= opnum && a.lastuse() <= op2num && (a.symtype() != SymType.SymTypeGlobal && a.symtype() != SymType.SymTypeOutputParam)
                    && PvtFunctions.equivalent(a.typespec(), c.typespec()))
                {
                    if (debug() > 1)
                    {
                        debug_opt_ops(opnum, opnum + 1, Fmt.format("turned '{} {}...' to '{} {}...' as part of daisy-chain", op.opname(), a.name(),
                                                                    op.opname(), c.name()));
                    }
                    inst().args()[op.firstarg()] = inst().args()[next.firstarg()];
                    c.mark_rw(opnum, false, true);
                    // Any time we write to a variable that wasn't written to at this op previously, we need to block_unalias it, or it can dealias to
                    // the wrong thing when examining subsequent instructions.
                    block_unalias(oparg(op, 0));  // clear any aliases
                    turn_into_nop(next, "daisy-chain op and assignment");
                    return 2;
                }
            }

            // Convert this combination
            //     closure A name arg...
            //     mul B A weight
            // into
            //     closure B C name arg...
            // That is, collapse a creation and immediate scale of a closure into a single closure-with-scale constructor. (Valid if A is not used
            // elsewhere.) Further refinement: if weight = 1, no need to do the scale, and if weight == 0, eliminate the work entirely. We only do
            // this optimization on pass > 1, to give a fair chance for other optimizations to be able to turn the weight into a constant before we do
            // this one (since if it's 1 or 0, we can simplify further).
            if (op.opname() == u_closure && next.opname() == u_mul && optimization_pass() > 1)
            {
                Symbol a = opargsym(op, 0);
                Symbol name = opargsym(op, 1);
                Symbol aa = opargsym(next, 1);
                Symbol weight = opargsym(next, 2);
                int weightarg = 2;
                if (weight.typespec().is_closure())  // opposite order
                {
                    Symbol swap = aa;
                    aa = weight;
                    weight = swap;
                    weightarg = 1;
                }
                if (name.typespec().is_string() && a.firstuse() >= opnum && a.lastuse() <= op2num && a == aa && weight.typespec().is_triple())
                {
                    if (is_zero(weight))
                    {
                        turn_into_nop(op, "zero-weighted closure");
                        turn_into_assign(next, add_constant(0.0f), "zero-weighted closure");
                        return 1;
                    }
                    // FIXME - handle weight being a float as well
                    List<int> newargs = new List<int>();
                    newargs.Add(oparg(next, 0));  // B
                    if (!is_one(weight))
                    {
                        newargs.Add(oparg(next, weightarg));  // weight
                    }
                    for (int i = 1; i < op.nargs(); ++i)
                    {
                        newargs.Add(oparg(op, i));
                    }
                    turn_into_nop(op, "combine closure+mul");
                    turn_into_nop(next, "combine closure+mul");
                    insert_code(opnum, u_closure, newargs, RecomputeRWRangesOption.RecomputeRWRanges, InsertRelation.GroupWithNext);
                    if (debug() > 1)
                    {
                        Console.Out.Write("op " + opnum + "-" + op2num + " combined closure+mul\n");
                    }
                    return 1;
                }
            }

            // No changes
            return 0;
        }

        /// Mark our params that feed to later layers, and whether we have any outgoing connections.
        public void mark_outgoing_connections()
        {
            System.Diagnostics.Debug.Assert(inst().m_instoverrides.Length == 0);  // don't call this before copy_code_from_master
            inst().outgoing_connections(false);
            foreach (Symbol s in inst().param_range())
            {
                s.connected_down(false);
            }
            for (int lay = layer() + 1; lay < group().nlayers(); ++lay)
            {
                foreach (Connection c in group()[lay].m_connections)
                {
                    if (c.srclayer == layer())
                    {
                        inst().symbol(c.src.param).connected_down(true);
                        inst().outgoing_connections(true);
                    }
                }
            }
        }

        // Predicate to test if a symbol (specified by symbol index, symbol pointer, or by the inbound Connection record) is never used within the shader
        // or passed along. Subtlety: you can't base the test for params on sym->everused(), since of course it may be used within its own init ops, but
        // then never subsequently used, and thus be a prime candidate for culling. Instead, for params we test whether it was used at any point AFTER its
        // init ops.
        public sealed class SymNeverUsed
        {
            private readonly RuntimeOptimizer m_rop;
            private readonly ShaderInstance m_inst;

            public SymNeverUsed(RuntimeOptimizer rop, ShaderInstance inst)
            {
                m_rop = rop;
                m_inst = inst;
            }

            public bool Invoke(Symbol sym)
            {
                if (sym.symtype() == SymType.SymTypeParam)
                {
                    return (sym.lastuse() < sym.initend()) && !sym.connected_down();
                }
                if (sym.symtype() == SymType.SymTypeOutputParam)
                {
                    if (!m_rop.opt_elide_unconnected_outputs())
                    {
                        return false;  // Asked not to do this optimization
                    }
                    if (sym.connected_down())
                    {
                        return false;  // Connected to something downstream
                    }
                    if (sym.renderer_output())
                    {
                        return false;  // This is a renderer output
                    }
                    return (sym.lastuse() < sym.initend());
                }
                return !sym.everused();  // all other symbol types
            }

            public bool Invoke(int symid)
            {
                return Invoke(m_inst.symbol(symid));
            }

            public bool Invoke(Connection c)
            {
                return Invoke(c.dst.param);
            }
        }

        /// Check all params and output params to find any that are neither used in the shader (aside from their own init ops, which shouldn't count)
        /// nor connected to downstream layers, and for those, remove their init ops and connections. Precondition: mark_outgoing_connections should be
        /// up to date.
        public int remove_unused_params()
        {
            int alterations = 0;
            SymNeverUsed param_never_used = new SymNeverUsed(this, inst());  // handy predicate

            // Get rid of unused params' init ops and clear their read/write ranges
            foreach (Symbol s in inst().param_range())
            {
                if (param_never_used.Invoke(s) && s.has_init_ops())
                {
                    string why = null;
                    if (debug() > 1)
                    {
                        why = Fmt.format("remove init ops of unused param {} {}", s.typespec(), s.name());
                    }
                    turn_into_nop(s.initbegin(), s.initend(), why);
                    s.set_initrange(0, 0);
                    s.clear_rw();  // mark as totally unused
                    ++alterations;
                }
            }

            // Get rid of the Connections themselves
            if (debug() > 1)
            {
                foreach (Connection c in inst().connections())
                {
                    if (param_never_used.Invoke(c))
                    {
                        debug_optfmt("  Connection no longer needed: {} {}\n", group()[c.srclayer].layername(), c.str(group(), inst()));
                    }
                }
            }
            inst().connections().RemoveAll(c => param_never_used.Invoke(c));

            return alterations;
        }

        /// Add to syms the indices of all symbols that may be written by instructions in the half-closed range [opbegin, opend).
        public void catalog_symbol_writes(int opbegin, int opend, HashSet<int> syms)
        {
            for (int i = opbegin; i < opend; ++i)
            {
                Opcode op = inst().ops()[i];
                for (int a = 0, nargs = op.nargs(); a < nargs; ++a)
                {
                    if (op.argwrite(a))
                    {
                        syms.Add(oparg(op, a));
                    }
                }
            }
        }

        /// Find situations where an output is simply a copy of a connected input, and eliminate the middleman.
        public int eliminate_middleman()
        {
            int changed = 0;
            foreach (Symbol s in inst().param_range())
            {
                // Skip if this isn't a shader output parameter that's connected to a later layer.
                if (s.symtype() != SymType.SymTypeOutputParam || !s.connected_down())
                {
                    continue;
                }
                // If it's written more than once, or has init ops, don't bother
                if (s.firstwrite() != s.lastwrite() || s.has_init_ops())
                {
                    continue;
                }
                // Ok, s is a connected output, written only once, without init ops.

                // If the one time it's written isn't a simple assignment, never mind
                int opnum = s.firstwrite();
                Opcode o = inst().ops()[opnum];
                if (o.opname() != u_assign)
                {
                    continue;  // only consider direct assignments
                }
                // Now what's it assigned from? If it's not a connected parameter, or if it's not an equivalent data type, or if it's a closure, never
                // mind.
                int src_index = oparg(o, 1);
                Symbol src = opargsym(o, 1);

                if (!(src.symtype() == SymType.SymTypeParam && src.connected()) || !PvtFunctions.equivalent(src.typespec(), s.typespec())
                    || s.typespec().is_closure())
                {
                    continue;
                }

                // Only works if the assignment is unconditional. Needs to not be in a conditional or loop, and not have any exit or return statement
                // before the assignment.
                if (!op_is_unconditionally_executed(opnum))
                {
                    continue;
                }

                // OK, output param 's' is simply and unconditionally assigned the value of the equivalently-typed input parameter 'src'. Doctor
                // downstream shaders that use s to connect directly to src.

                // First, find what src is connected to.
                int upstream_layer = -1, upstream_symbol = -1;
                for (int i = 0, e = inst().nconnections(); i < e; ++i)
                {
                    Connection c = inst().connection(i);
                    if (c.dst.param == src_index &&  // the connection we want
                        c.src.is_complete() && c.dst.is_complete() && PvtFunctions.equivalent(c.src.type, c.dst.type) && !c.src.type.is_closure()
                        && !c.dst.type.is_closure())
                    {
                        upstream_layer = c.srclayer;
                        upstream_symbol = c.src.param;
                        break;
                    }
                }
                if (upstream_layer < 0 || upstream_symbol < 0)
                {
                    continue;  // not a complete connection, forget it
                }

                ShaderInstance upinst = group()[upstream_layer];
                if (debug() > 1)
                {
                    Console.Out.Write("Noticing that " + inst().layername() + "." + s.name() + " merely copied from " + src.name() + ", connected from "
                                      + upinst.layername() + "." + upinst.symbol(upstream_symbol).name() + "\n");
                }

                // Find all the downstream connections of s, make them connections to src.
                int s_index = inst().symbolindex(s);
                for (int laynum = layer() + 1; laynum < group().nlayers(); ++laynum)
                {
                    ShaderInstance downinst = group()[laynum];
                    for (int i = 0, e = downinst.nconnections(); i < e; ++i)
                    {
                        Connection c = downinst.connections()[i];
                        if (c.srclayer == layer() &&   // connected to our layer
                            c.src.param == s_index &&  // connected to s
                            c.src.is_complete() && c.dst.is_complete() && PvtFunctions.equivalent(c.src.type, c.dst.type))
                        {
                            // just change the connection's referrant to the upstream source of s.
                            c.srclayer = upstream_layer;
                            c.src.param = upstream_symbol;
                            downinst.connections()[i] = c;
                            ++changed;
                            System.Threading.Interlocked.Add(ref shadingsys().m_stat_middlemen_eliminated, 1);
                            if (debug() > 1)
                            {
                                Symbol dsym = downinst.symbol(c.dst.param);
                                if (dsym == null)
                                {
                                    dsym = downinst.mastersymbol(c.dst.param);
                                }
                                Symbol usym = upinst.symbol(upstream_symbol);
                                if (usym == null)
                                {
                                    usym = upinst.mastersymbol(upstream_symbol);
                                }
                                System.Diagnostics.Debug.Assert(dsym != null && usym != null);
                                Console.Out.Write("Removed " + inst().layername() + "." + s.name() + " middleman for " + downinst.layername() + "."
                                                  + dsym.name() + ", now connected to " + upinst.layername() + "." + usym.name() + "\n");
                            }
                        }
                    }
                }
            }
            return changed;
        }

        /// Perform various optimizations specific to assignments.
        public int optimize_assignment(Opcode op, int opnum)
        {
            // Various optimizations specific to assignment statements
            System.Diagnostics.Debug.Assert(op.opname() == u_assign);
            int changed = 0;
            Symbol R = inst().argsymbol(op.firstarg() + 0);
            Symbol A = inst().argsymbol(op.firstarg() + 1);
            bool R_local_or_tmp = (R.symtype() == SymType.SymTypeLocal || R.symtype() == SymType.SymTypeTemp);
            if (block_alias(inst().arg(op.firstarg())) == inst().arg(op.firstarg() + 1)
                || block_alias(inst().arg(op.firstarg() + 1)) == inst().arg(op.firstarg()))
            {
                // We're re-assigning something already aliased, skip it
                turn_into_nop(op, "reassignment of current value (2)");
                return ++changed;
            }
            if (coerce_assigned_constant(op))
            {
                // A may have changed, so we need to reset it
                A = inst().argsymbol(op.firstarg() + 1);
                ++changed;
            }
            // NOW do assignment constant folding, only after we have performed all the other transformations that may turn this op into an assignment.
            changed += ConstFold.constfold_assign(this, opnum);
            if (op.opname() != u_assign)
            {
                // The const fold has changed the assignment to something other than assign (presumably nop), so skip the other assignment
                // transformations below.
                return 0;
            }
            if ((A.is_constant() || A.lastwrite() < opnum) && PvtFunctions.equivalent(R.typespec(), A.typespec()))
            {
                // Safe to alias R to A for this block, if A is a constant or if it's never written to again.
                block_alias(inst().arg(op.firstarg()), inst().arg(op.firstarg() + 1));
            }
            if (A.is_constant() && R.typespec() == A.typespec() && R_local_or_tmp && R.firstwrite() == opnum && R.lastwrite() == opnum)
            {
                // This local or temp is written only once in the whole shader -- on this statement -- and it's assigned a constant. So just alias it
                // to the constant.
                int cind = inst().args()[op.firstarg() + 1];
                global_alias(inst().args()[op.firstarg()], cind);
                turn_into_nop(op, "replace symbol with constant");
                return ++changed;
            }
            if (R_local_or_tmp && !R.everread())
            {
                // This local is written but NEVER READ. nop it.
                turn_into_nop(op, "local/tmp never read");
                return ++changed;
            }
            if (outparam_assign_elision(opnum, op))
            {
                return ++changed;
            }
            if (R == A)
            {
                // Just an assignment to itself -- turn into NOP!
                turn_into_nop(op, "self-assignment");
                return ++changed;
            }
            else if (R_local_or_tmp && R.lastread() < opnum && !m_in_loop[opnum])
            {
                // Don't bother assigning if we never read it again
                turn_into_nop(op, "symbol never read again");
                return ++changed;
            }
            return changed;
        }

        /// Copy block_aliases from old to new, except any aliases involving symbols in the excluded list (which may be null if there no exclusions). If
        /// copy_temps is false, aliases involving temp symbols will not be copied.
        public void copy_block_aliases(Dictionary<int, int> old_block_aliases, Dictionary<int, int> new_block_aliases, HashSet<int> excluded, bool copy_temps)
        {
            System.Diagnostics.Debug.Assert(!ReferenceEquals(old_block_aliases, new_block_aliases));  // copy_block_aliases does not work in-place
            // Find all symbols written anywhere in the instruction range
            new_block_aliases.Clear();
            foreach (KeyValuePair<int, int> oba in old_block_aliases)
            {
                if (oba.Value < 0)
                {
                    continue;  // erased alias -- don't copy
                }
                if (!copy_temps && (inst().symbol(oba.Key).is_temp() || inst().symbol(oba.Value).is_temp()))
                {
                    continue;  // don't copy temp aliases unless told to
                }
                if (excluded != null && (excluded.Contains(oba.Key) || excluded.Contains(oba.Value)))
                {
                    continue;  // don't copy from excluded list
                }
                new_block_aliases[oba.Key] = oba.Value;
            }
        }

        public void copy_block_aliases(Dictionary<int, int> old_block_aliases, Dictionary<int, int> new_block_aliases)
        {
            copy_block_aliases(old_block_aliases, new_block_aliases, null, false);
        }

        /// One optimization pass over a range of instructions [begin, end). Return the number of changes made. If seed_block_aliases is not null, use
        /// that as the initial set of block_aliases.
        public int optimize_ops(int beginop, int endop, Dictionary<int, int> seed_block_aliases)
        {
            if (beginop >= endop)
            {
                return 0;
            }

            // Constant aliases valid for just this basic block
            clear_block_aliases();

            // Provide a place where, if we recurse, we can save prior block aliases. Register them on the block_aliases_stack so that calls to
            // block_unalias() will unalias from there, too.
            Dictionary<int, int> saved_block_aliases = new Dictionary<int, int>();
            m_block_aliases_stack.Add(saved_block_aliases);

            int lastblock = -1;
            int skipops = 0;  // extra inserted ops to skip over
            int changed = 0;
            int num_ops = inst().ops().Count;
            int old_num_ops = num_ops;  // track when it changes
            for (int opnum = beginop; opnum < endop; opnum += 1)
            {
                System.Diagnostics.Debug.Assert(old_num_ops == num_ops);  // better not happen unknowingly
                System.Diagnostics.Debug.Assert(num_ops == inst().ops().Count);
                System.Diagnostics.Debug.Assert(opnum < inst().ops().Count);
                if (m_stop_optimizing)
                {
                    break;
                }
                Opcode op = inst().ops()[opnum];
                if (skipops != 0)
                {
                    // If a previous optimization inserted ops and told us to skip over the new ones, we still need to unalias any symbols written by
                    // this op, but otherwise skip all subsequent optimizations until we run down the skipops counter.
                    block_unalias_written_args(op);
                    System.Diagnostics.Debug.Assert(lastblock == m_bblockids[opnum]);  // this should not be a new basic block
                    --skipops;
                    continue;  // Move along to the next op, no optimization here
                }
                // Things to do if we've just moved to a new basic block
                if (lastblock != m_bblockids[opnum])
                {
                    clear_block_aliases(seed_block_aliases);
                    seed_block_aliases = null;  // only the first time
                    clear_stale_syms();
                    lastblock = m_bblockids[opnum];
                }
                // Things to do at the start of main code:
                // * Alias output params to their initial values, if known.
                if (opnum == inst().m_maincodebegin)
                {
                    for (int i = inst().firstparam(); i < inst().lastparam(); ++i)
                    {
                        Symbol s = inst().symbol(i);
                        if (s.symtype() == SymType.SymTypeOutputParam && !s.interpolated()
                            && (s.valuesource() == Symbol.ValueSource.DefaultVal || s.valuesource() == Symbol.ValueSource.InstanceVal) && !s.has_init_ops()
                            && !s.typespec().is_closure_based() && !s.typespec().is_structure_based())
                        {
                            make_symbol_room(1);  // Make sure add_constant is ok
                            s = inst().symbol(i);
                            int cind = add_constant(s.typespec(), s.data());
                            block_alias(i, cind);  // Alias this symbol to the new const
                        }
                    }
                }
                // Nothing below here to do for no-ops, take early out.
                if (op.opname() == u_nop || op.opname() == u_functioncall_nr)
                {
                    continue;
                }
                // De-alias the readable args to the op and figure out if there are any constants involved.
                for (int i = 0, e = op.nargs(); i < e; ++i)
                {
                    if (!op.argwrite(i))  // Don't de-alias args that are written
                    {
                        int argindex = op.firstarg() + i;
                        int argsymindex = dealias_symbol(inst().arg(argindex), opnum);
                        inst().args()[argindex] = argsymindex;
                    }
                    if (op.argread(i))
                    {
                        use_stale_sym(oparg(op, i));
                    }
                }

                OpDescriptor opd = shadingsys().op_descriptor(op.opname());
                // If it's a simple assignment and the lvalue is "stale", go back and eliminate its last assignment.
                if (is_simple_assign(op, opd))
                {
                    simple_sym_assign(oparg(op, 0), opnum);
                }
                // Make sure there's room for several more symbols, so that we can add a few consts if we need to, without worrying about the addresses
                // of symbols changing when we add a new one below.
                make_symbol_room(max_new_consts_per_fold);
                // For various ops that we know how to effectively constant-fold, dispatch to the appropriate routine.
                if (optimize() >= 2 && m_opt_constant_fold)
                {
                    if (opd != null && opd.folder != null)
                    {
                        int c = opd.folder(this, opnum);
                        if (c != 0)
                        {
                            changed += c;
                            // Re-check num_ops in case the folder inserted something
                            num_ops = inst().ops().Count;
                            skipops = num_ops - old_num_ops;
                            endop += num_ops - old_num_ops;  // adjust how far we loop
                            old_num_ops = num_ops;
                            op = inst().ops()[opnum];  // in case ops resized
                        }
                    }
                }
                // Clear local block aliases for any args that were written by this op
                block_unalias_written_args(op);

                // Now we handle assignments.
                if (optimize() >= 2 && op.opname() == u_assign && m_opt_assign)
                {
                    changed += optimize_assignment(op, opnum);
                }
                if (optimize() >= 2 && m_opt_elide_useless_ops && opd != null && (opd.flags & OpDescriptor.SideEffects) == 0)
                {
                    changed += useless_op_elision(op, opnum) ? 1 : 0;
                }
                if (m_stop_optimizing)
                {
                    break;
                }
                // Peephole optimization involving pair of instructions (the second instruction will be in the same basic block.
                if (optimize() >= 2 && m_opt_peephole && op.opname() != u_nop && op.opname() != u_functioncall_nr)
                {
                    // Find the next instruction in the same basic block
                    int op2num = next_block_instruction(opnum);
                    if (op2num != 0)
                    {
                        int c = peephole2(opnum, op2num);
                        if (c != 0)
                        {
                            changed += c;
                            // Re-check num_ops in case the folder inserted something
                            num_ops = inst().ops().Count;
                            // skipops = num_ops - old_num_ops;
                            endop += num_ops - old_num_ops;  // adjust how far we loop
                            old_num_ops = num_ops;
                            op = inst().ops()[opnum];  // in case ops resized
                        }
                    }
                }

                // Special cases for "if", "functioncall", and loops: Optimize the sequences of instructions in the bodies recursively in a way that
                // allows us to be clever about the basic block alias tracking.
                ustring opname = op.opname();
                if ((opname == u_if || opname == u_functioncall || opname == u_for || opname == u_while || opname == u_dowhile)
                    && shadingsys().m_opt_seed_bblock_aliases)
                {
                    // Find all symbols written anywhere in the instruction range of the bodies.
                    HashSet<int> symwrites = new HashSet<int>();
                    catalog_symbol_writes(opnum + 1, op.farthest_jump(), symwrites);
                    // Save the aliases from the basic block we are exiting. If & function call: save all prior aliases. Loops: dont save aliases
                    // involving syms written in the loop. Note that for both cases, we don't copy aliases involving temps, because that breaks our
                    // later assumptions (for temp coalescing) that temp uses never cross basic block boundaries.
                    if (opname == u_if || opname == u_functioncall)
                    {
                        copy_block_aliases(m_block_aliases, saved_block_aliases);
                    }
                    else
                    {
                        copy_block_aliases(m_block_aliases, saved_block_aliases, symwrites, false);
                    }
                    // 'if' has 2 blocks (then, else), function call has just one (the body), loops have 4 (init, cond, body, incr),
                    int njumps = (opname == u_if) ? 2 : (opname == u_functioncall ? 1 : 4);
                    // Recursively optimize each body block.
                    for (int j = 0; j < njumps; ++j)
                    {
                        changed += optimize_ops(j == 0 ? opnum + 1 : op.jump(j - 1), op.jump(j), saved_block_aliases);
                        op = inst().ops()[opnum];  // in case ops resized
                    }
                    // Adjust optimization loop end if any instructions were added
                    num_ops = inst().ops().Count;
                    endop += num_ops - old_num_ops;
                    old_num_ops = num_ops;
                    // Now we can restore the original aliases to seed the basic block that follows. For if/function, we need to remove all aliases
                    // referencing syms written within the conditional or function body. For loops, recall that we already excluded the written syms
                    // from the saved_block_aliases.
                    if (opname == u_if || opname == u_functioncall)
                    {
                        // (the C++ swaps the saved aliases into a fresh map, leaving the saved aliases empty)
                        Dictionary<int, int> restored_aliases = new Dictionary<int, int>(saved_block_aliases);
                        saved_block_aliases.Clear();
                        // catalog again, in case optimizations in those blocks caused writes that weren't apparent before.
                        catalog_symbol_writes(opnum + 1, op.farthest_jump(), symwrites);
                        copy_block_aliases(restored_aliases, saved_block_aliases, symwrites, false);
                    }
                    seed_block_aliases = saved_block_aliases;
                    // Get ready to increment to the next instruction
                    opnum = op.farthest_jump() - 1;
                }
            }
            m_block_aliases_stack.RemoveAt(m_block_aliases_stack.Count - 1);  // Done with saved_block_aliases
            return changed;
        }

        public int optimize_ops(int beginop, int endop)
        {
            return optimize_ops(beginop, endop, null);
        }

        /// Optimize one layer of a group, given what we know about its instance variables and connections.
        public void optimize_instance()
        {
            // If "opt_layername" attribute is set, only optimize the named layer
            if (!shadingsys().m_opt_layername.empty() && shadingsys().m_opt_layername != inst().layername())
            {
                return;
            }

            // Make a list of the indices of all constants.
            for (int i = 0, e = inst().symbols().Count; i < e; ++i)
            {
                if (inst().symbol(i).symtype() == SymType.SymTypeConst)
                {
                    m_all_consts.Add(i);
                }
            }

            // Turn all parameters with instance or default values, and which cannot be overridden by geometry values, into constants or aliases for
            // globals. Also turn connections from earlier layers' outputs that are known to be constants or globals into constants or global aliases
            // without any connection.
            if (optimize() >= 2 && m_opt_simplify_param)
            {
                simplify_params();
            }

#if DEBUG
            // Confirm that the symbols between [firstparam,lastparam] are all input or output params.
            foreach (Symbol s in inst().param_range())
            {
                System.Diagnostics.Debug.Assert(s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam);
            }
#endif

            // Recompute which of our params have downstream connections.
            mark_outgoing_connections();

            // Try to fold constants. We take several passes, until we get to the point that not much is improving. It rarely goes beyond 3-4 passes,
            // but we have a hard cutoff just to be sure we don't ever get into an infinite loop from an unforeseen cycle where we end up
            // inadvertently transforming A => B => A => etc.
            int reallydone = 0;  // Force a few passes after we think we're done
            int npasses = shadingsys().opt_passes();
            for (m_pass = 0; m_pass < npasses; ++m_pass)
            {
                // Once we've made one pass (and therefore called mark_outgoing_connections), we may notice that the layer is unused, and therefore can
                // stop doing work to optimize it.
                if (m_pass != 0 && inst().unused())
                {
                    break;
                }

                if (m_stop_optimizing)
                {
                    break;
                }

                if (debug() > 1)
                {
                    debug_optfmt("layer {} \"{}\", pass {}:\n", layer(), inst().layername(), m_pass);
                }

                // Track basic blocks and conditional states
                find_conditionals();
                find_basic_blocks();

                // Clear local messages for this instance
                m_local_unknown_message_sent = false;
                m_local_messages_sent.Clear();

                // Figure out which params are just aliases for globals (only necessary to do once, on the first pass).
                if (m_pass == 0 && optimize() >= 2)
                {
                    find_params_holding_globals();
                }

                // Here is the meat of the optimization, where we pass over the code for this instance and make various transformations.
                int changed = optimize_ops(0, inst().ops().Count);

                // Now that we've rewritten the code, we need to re-track the variable lifetimes.
                track_variable_lifetimes();

                // Recompute which of our params have downstream connections.
                mark_outgoing_connections();

                // Find situations where an output is simply a copy of a connected input, and eliminate the middleman.
                if (optimize() >= 2 && m_opt_middleman)
                {
                    int c = eliminate_middleman();
                    if (c != 0)
                    {
                        mark_outgoing_connections();
                    }
                    changed += c;
                }

                // Elide unconnected parameters that are never read.
                if (optimize() >= 1)
                {
                    changed += remove_unused_params();
                }

                // FIXME -- we should re-evaluate whether writes_globals() is still true for this layer.

                // If nothing changed, we're done optimizing. But wait, it may be that after re-tracking variable lifetimes, we can notice new
                // optimizations! So force another pass, then we're really done.
                if (changed < 1)
                {
                    if (++reallydone > 3)
                    {
                        break;
                    }
                }
                else
                {
                    reallydone = 0;
                }
            }

            // A layer that was allowed to run lazily originally, if it no longer (post-optimized) has any outgoing connections, is no longer needed at
            // all.
            if (inst().unused())
            {
                // Not needed. Remove all its connections and ops.
                inst().connections().Clear();
                turn_into_nop(0, inst().ops().Count - 1,
                              debug() > 1 ? Fmt.format("eliminate layer {} with no outward connections", inst().layername()) : "");
                foreach (Symbol s in inst().symbols())
                {
                    s.clear_rw();
                }
            }

            // Now that we've optimized this layer, walk through the ops and note which messages may have been sent, so subsequent layers will know.
            foreach (Opcode o in inst().ops())
            {
                if (o.opname() == u_setmessage)
                {
                    Symbol Name = inst().argsymbol(o.firstarg() + 0);
                    if (Name.is_constant())
                    {
                        m_messages_sent.Add(Name.get_string());
                    }
                    else
                    {
                        m_unknown_message_sent = true;
                    }
                }
            }
        }
    }
}
