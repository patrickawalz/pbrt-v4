// OSL port: liboslexec/oslexec_pvt.h (the private structures of the execution library)
//
// ---------------------------------------------------------------------------------------------------------------------------------------------------
// THE CONTRACT WITH THE BACKEND (read this if you write the IL emitter or the optimizer)
// ---------------------------------------------------------------------------------------------------------------------------------------------------
// LLVM is replaced by an IL emitter; everything that the C++ keeps in LLVM members of these classes is a hook here.
//
// Compiled group entry points. A shader group is compiled into an "init" function, and one function per layer. They all have the signature
//
//      unsafe delegate void RunLLVMGroupFunc(void* shaderglobals, void* heap_arena_ptr, void* userdata_base_pointer, void* output_base_pointer,
//                                            int shadeindex, void* interactive_params_ptr);
//
//   shaderglobals          the ShaderGlobals* (see include/shaderglobals.cs: the layout is the C++ one; 'context' and 'renderer' hold GCHandle
//                          pointers that ShaderGlobalsAccess resolves, which is also how the rs_* entry points of RsFallback find their services)
//   heap_arena_ptr         the group data ("groupdata") arena: ShaderGroup.llvm_groupdata_size() bytes, 64-byte aligned, owned by the ShadingContext
//                          (reserve_heap). The init function zeroes the layer-run flags and initializes the userdata slots; Symbol.dataoffset()
//                          is the offset of a symbol in this arena once the backend has laid it out (Symbol.arena() == SymArena.Heap).
//   userdata_base_pointer  base for the symbols the renderer asked to be placed in the user data arena (add_symlocs with SymArena.UserData)
//   output_base_pointer    same for SymArena.Outputs
//   shadeindex             the shade index (also in ShaderGlobals.shade_index)
//   interactive_params_ptr ShaderGroup.interactive_arena_ptr() (SymArena.Interactive), or null
//
// ShadingContext.execute_init calls group.llvm_compiled_init(), and, if 'run', group.llvm_compiled_layer(entry layer) for each entry layer (or the
// "fused" llvm_compiled_version() if the group has one); execute_layer calls one layer; execute_cleanup records the stats and processes the errors.
// Strings in the arena are 64-bit ustring hashes (ustring.hash()). Closures are ClosureColor* pointers into ShadingContext's closure pool, that
// ShadingContext.allocate_closure hands out.
//
// Managed data of the symbols. Symbol.data() (constants and defaults) is the managed array int[], float[] or ustring[] that holds the value
// (not a pointer): ShaderMaster, ShaderInstance and the optimizer all share this representation.
//
// Hooks the backend fills in:
//      ShadingSystemImpl.ilgen_compile_group   (Func<ShaderGroup, ShadingContext, bool>) is called by optimize_group after the optimizer ran
//      OpDescriptor.ilgen                      the IL generator of an op (delegate OpILGen), registered through ShadingSystemImpl.register_ilgen
//      OpDescriptor.folder                     the constant folder of an op (delegate OpFolder), registered through register_folder
// Hooks the optimizer fills in: RuntimeOptimizer (a stub is in rop_stub.cs; the optimizer's file defines the same partial class), and
// ShadingSystemImpl.op_descriptor(), which builds its table from OpFolderTable / OpILGenTable.
// ---------------------------------------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using OIIO;
using OSL.pvt;

namespace OSL
{
    /// Group entry function type (see the contract above)
    public unsafe delegate void RunLLVMGroupFunc(void* shaderglobals, void* heap_arena_ptr, void* userdata_base_pointer, void* output_base_pointer,
                                                 int shadeindex, void* interactive_params_ptr);

    /// PerThreadInfo: per-thread data of the shading system (a pool of ShadingContexts)
    public sealed class PerThreadInfo : IDisposable
    {
        public Stack<ShadingContext> context_pool = new Stack<ShadingContext>();
        private bool m_disposed;

        public PerThreadInfo()
        {
        }

        /// Get the pool top and then pop
        public ShadingContext pop_context()
        {
            if (context_pool.Count == 0)
            {
                return null;
            }
            return context_pool.Pop();
        }

        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                while (context_pool.Count > 0)
                {
                    context_pool.Pop().Dispose();
                }
            }
        }
    }

    /// A pool of memory of fixed-size blocks that hands out aligned chunks and frees everything at once with clear() (the ShadingContext uses it for
    /// closures, messages and scratch space). The memory is unmanaged: closures are addressed by pointers.
    public sealed unsafe class SimplePool : IDisposable
    {
        private int m_blocksize;
        private List<IntPtr> m_blocks = new List<IntPtr>();
        private int m_current_block;
        private int m_block_offset;

        public SimplePool(int blocksize)
        {
            m_blocksize = blocksize;
            m_blocks.Add((IntPtr)NativeMemory.AlignedAlloc((nuint)blocksize, 64));
            m_block_offset = blocksize;
            m_current_block = 0;
            // The first allocation moves to a fresh block (as the C++ constructor leaves the offset at BlockSize)
            m_block_offset = 0;
        }

        ~SimplePool()
        {
            free();
        }

        public void Dispose()
        {
            free();
            GC.SuppressFinalize(this);
        }

        private void free()
        {
            foreach (IntPtr b in m_blocks)
            {
                NativeMemory.AlignedFree((void*)b);
            }
            m_blocks.Clear();
        }

        private static int alignment_offset_calc(byte* ptr, int alignment)
        {
            long ptrbits = (long)ptr;
            long offset = ((ptrbits + alignment - 1) / alignment) * alignment - ptrbits;
            return (int)offset;
        }

        public byte* alloc(int size, int alignment = 1)
        {
            if (size + alignment - 1 > m_blocksize)
            {
                throw new InvalidOperationException("SimplePool: allocation larger than the block size");
            }
            m_block_offset += alignment_offset_calc((byte*)m_blocks[m_current_block] + m_block_offset, alignment);
            if (m_block_offset + size > m_blocksize)
            {
                m_current_block++;
                if (m_blocks.Count == m_current_block)
                {
                    m_blocks.Add((IntPtr)NativeMemory.AlignedAlloc((nuint)m_blocksize, 64));
                }
                m_block_offset = alignment_offset_calc((byte*)m_blocks[m_current_block], alignment);
            }
            byte* ptr = (byte*)m_blocks[m_current_block] + m_block_offset;
            m_block_offset += size;
            return ptr;
        }

        public void clear()
        {
            m_current_block = 0;
            m_block_offset = 0;
        }
    }

    /// The renderer-visible description of a ShaderGroup is the ShaderGroup class below; its members that the C++ keeps for LLVM are the group entry points
    public sealed partial class ShaderGroup : IDisposable
    {
        public ShaderGroup(string name, pvt.ShadingSystemImpl shadingsys)
        {
            m_shadingsys = shadingsys;
            m_name = new ustring(name);
            m_id = Interlocked.Increment(ref s_next_id) - 1;
            m_raytype_queries = -1;
            m_exec_repeat = 1;
            m_unknown_textures_needed = false;
            m_unknown_closures_needed = false;
            m_unknown_attributes_needed = false;
            m_complete = false;
            m_does_nothing = false;
            m_num_entry_layers = 0;
            m_globals_read = 0;
            m_globals_write = 0;
            m_executions = 0;
            m_stat_total_shading_time_ticks = 0;
            m_exec_repeat = 1;
            m_stat_active_layers = 0;
            m_stat_network_depth = 0;
            m_stat_texture_ops = 0;
            m_stat_noise_ops = 0;
            m_interactive_arena_size = 0;
            m_optimized = 0;
            m_jitted = 0;
            m_batch_jitted = 0;
            m_llvm_groupdata_size = 0;
            m_llvm_groupdata_wide_size = 0;
        }

        private static int s_next_id = 0;

        public void Dispose()
        {
            unsafe
            {
                if (m_interactive_arena != null)
                {
                    NativeMemory.Free((void*)m_interactive_arena);
                    m_interactive_arena = null;
                }
            }
        }

        // ----- fields (see the C++ for the comments) -----
        public volatile int m_optimized;
        public volatile int m_jitted;
        public bool m_does_nothing;
        public volatile int m_batch_jitted;
        public long m_llvm_groupdata_size;
        public long m_llvm_groupdata_wide_size;
        public int m_id;
        public int m_num_entry_layers;
        public RunLLVMGroupFunc m_llvm_compiled_version;
        public RunLLVMGroupFunc m_llvm_compiled_init;
        public List<RunLLVMGroupFunc> m_llvm_compiled_layers = new List<RunLLVMGroupFunc>();
        /// IL backend hooks: opaque slots for whatever the emitter wants to keep with the group (the DynamicMethod's owner, the persisted assembly path)
        public object m_il_compiled_state;
        public Delegate m_ilgen_group_entry;
        public List<ShaderInstance> m_layers = new List<ShaderInstance>();
        public ustring m_name;
        public int m_exec_repeat = 1;
        public int m_raytype_queries = -1;
        public int m_raytypes_on;
        public int m_raytypes_off;
        public readonly object m_mutex = new object();
        public int m_globals_read;
        public int m_globals_write;
        public List<ustring> m_textures_needed = new List<ustring>();
        public List<ustring> m_closures_needed = new List<ustring>();
        public List<ustring> m_globals_needed = new List<ustring>();
        public List<ustring> m_userdata_names = new List<ustring>();
        public List<TypeDesc> m_userdata_types = new List<TypeDesc>();
        public List<int> m_userdata_offsets = new List<int>();
        public List<byte> m_userdata_derivs = new List<byte>();
        public List<int> m_userdata_layers = new List<int>();
        public List<IntPtr> m_userdata_init_vals = new List<IntPtr>();
        public List<ustring> m_attributes_needed = new List<ustring>();
        public List<ustring> m_attribute_scopes = new List<ustring>();
        public List<TypeDesc> m_attribute_types = new List<TypeDesc>();
        public List<byte> m_attribute_derivs = new List<byte>();
        public List<ustring> m_renderer_outputs = new List<ustring>();
        public List<SymLocationDesc> m_symlocs = new List<SymLocationDesc>();   // SORTED by name
        public bool m_unknown_textures_needed;
        public bool m_unknown_closures_needed;
        public bool m_unknown_attributes_needed;
        public long m_executions;
        public long m_stat_total_shading_time_ticks;
        public int m_stat_active_layers;
        public int m_stat_network_depth;
        public int m_stat_texture_ops;
        public int m_stat_noise_ops;
        public string m_optix_cache_key = "";
        public ParamValueList m_pending_params = new ParamValueList();
        public List<ParamHints> m_pending_hints = new List<ParamHints>();
        public ustring m_group_use;
        public bool m_complete;
        public pvt.ShadingSystemImpl m_shadingsys;
        public List<InteractiveParamData> m_interactive_params = new List<InteractiveParamData>();
        public unsafe byte* m_interactive_arena;
        public long m_interactive_arena_size;

        public void clear()
        {
            m_layers.Clear();
            m_optimized = 0;
            m_jitted = 0;
            m_executions = 0;
        }

        public void append(ShaderInstance newlayer)
        {
            if (m_optimized != 0)
            {
                throw new InvalidOperationException("should not append to optimized group");
            }
            m_layers.Add(newlayer);
        }

        public int nlayers() { return m_layers.Count; }
        public ShaderInstance layer(int i) { return m_layers[i]; }
        public ShaderInstance this[int i] { get { return layer(i); } }
        public pvt.ShadingSystemImpl shadingsys() { return m_shadingsys; }
        public int optimized() { return m_optimized; }
        public void optimized(int opt) { m_optimized = opt; }
        public int jitted() { return m_jitted; }
        public void jitted(int jitted) { m_jitted = jitted; }
        public int batch_jitted() { return m_batch_jitted; }
        public void batch_jitted(int batch_jitted) { m_batch_jitted = batch_jitted; }
        public long llvm_groupdata_size() { return m_llvm_groupdata_size; }
        public void llvm_groupdata_size(long size) { m_llvm_groupdata_size = size; }
        public long llvm_groupdata_wide_size() { return m_llvm_groupdata_wide_size; }
        public void llvm_groupdata_wide_size(long size) { m_llvm_groupdata_wide_size = size; }
        public RunLLVMGroupFunc llvm_compiled_version() { return m_llvm_compiled_version; }
        public void llvm_compiled_version(RunLLVMGroupFunc func) { m_llvm_compiled_version = func; }
        public RunLLVMGroupFunc llvm_compiled_init() { return m_llvm_compiled_init; }
        public void llvm_compiled_init(RunLLVMGroupFunc func) { m_llvm_compiled_init = func; }

        public RunLLVMGroupFunc llvm_compiled_layer(int layer)
        {
            return layer < m_llvm_compiled_layers.Count ? m_llvm_compiled_layers[layer] : null;
        }

        public void llvm_compiled_layer(int layer, RunLLVMGroupFunc func)
        {
            while (m_llvm_compiled_layers.Count < nlayers())
            {
                m_llvm_compiled_layers.Add(null);
            }
            if (layer < nlayers())
            {
                m_llvm_compiled_layers[layer] = func;
            }
        }

        public bool does_nothing() { return m_does_nothing; }
        public void does_nothing(bool new_val) { m_does_nothing = new_val; }
        public long executions() { return Interlocked.Read(ref m_executions); }
        public int stat_active_layers() { return m_stat_active_layers; }
        public void stat_active_layers(int n) { m_stat_active_layers = n; }
        public int stat_network_depth() { return m_stat_network_depth; }
        public void stat_network_depth(int n) { m_stat_network_depth = n; }
        public int stat_texture_ops() { return m_stat_texture_ops; }
        public void stat_texture_ops(int n) { m_stat_texture_ops = n; }
        public int stat_noise_ops() { return m_stat_noise_ops; }
        public void stat_noise_ops(int n) { m_stat_noise_ops = n; }
        public void start_running() { Interlocked.Increment(ref m_executions); }
        public void name(ustring name) { m_name = name; }
        public ustring name() { return m_name; }
        public string optix_cache_key() { return m_optix_cache_key; }
        public void lock_() { Monitor.Enter(m_mutex); }
        public void unlock() { Monitor.Exit(m_mutex); }
        public int id() { return m_id; }
        public int num_entry_layers() { return m_num_entry_layers; }
        public bool is_last_layer(int layer) { return layer == nlayers() - 1; }
        public bool is_entry_layer(int layer) { return num_entry_layers() != 0 ? m_layers[layer].entry_layer() : is_last_layer(layer); }
        public int raytype_queries() { return m_raytype_queries; }

        public void set_raytypes(int raytypes_on, int raytypes_off)
        {
            m_raytypes_on = raytypes_on;
            m_raytypes_off = raytypes_off;
        }

        public int raytypes_on() { return m_raytypes_on; }
        public int raytypes_off() { return m_raytypes_off; }
        public void mark_entry_layer(ustring layername) { mark_entry_layer(find_layer(layername)); }
        public void clear_symlocs() { m_symlocs.Clear(); }

        // lower_bound by name in the sorted symlocs
        private int symloc_lower_bound(ustring name)
        {
            int lo = 0, hi = m_symlocs.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (m_symlocs[mid].name.CompareTo(name) < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            return lo;
        }

        public void add_symlocs(IEnumerable<SymLocationDesc> symlocs)
        {
            foreach (SymLocationDesc s in symlocs)
            {
                int f = symloc_lower_bound(s.name);
                if (f == m_symlocs.Count)
                {
                    m_symlocs.Add(s);
                }
                else if (m_symlocs[f].name == s.name)
                {
                    m_symlocs[f] = s;
                }
                else
                {
                    m_symlocs.Insert(f, s);
                }
            }
        }

        public SymLocationDesc? find_symloc(ustring name)
        {
            int f = symloc_lower_bound(name);
            return (f == m_symlocs.Count || m_symlocs[f].name != name) ? (SymLocationDesc?)null : m_symlocs[f];
        }

        public SymLocationDesc? find_symloc(ustring name, SymArena arena)
        {
            int f = symloc_lower_bound(name);
            if (f != m_symlocs.Count && m_symlocs[f].name == name && m_symlocs[f].arena == arena && m_symlocs[f].offset != -1)
            {
                return m_symlocs[f];
            }
            return null;
        }

        public SymLocationDesc? find_symloc(ustring name, ustring layer, SymArena arena)
        {
            ustring layersym = new ustring(layer.@string() + "." + name.@string());
            SymLocationDesc? symloc = find_symloc(layersym, arena);
            if (symloc == null)
            {
                symloc = find_symloc(name, arena);
            }
            return symloc;
        }

        public unsafe byte* interactive_arena_ptr() { return m_interactive_arena; }

        public struct InteractiveParamData : IEquatable<InteractiveParamData>, IComparable<InteractiveParamData>
        {
            public int layer;
            public ustring name;
            public int offset;

            public InteractiveParamData(int layer, ustring name, int offset)
            {
                this.layer = layer;
                this.name = name;
                this.offset = offset;
            }

            public bool Equals(InteractiveParamData other) { return layer == other.layer && name == other.name; }

            public int CompareTo(InteractiveParamData other)
            {
                if (layer != other.layer)
                {
                    return layer < other.layer ? -1 : 1;
                }
                return name.CompareTo(other.name);
            }
        }

        public void add_interactive_param(int layer, ustring name, long offset)
        {
            m_interactive_params.Add(new InteractiveParamData(layer, name, (int)offset));
        }

        public int interactive_param_offset(int layer, ustring name)
        {
            foreach (InteractiveParamData f in m_interactive_params)
            {
                if (f.layer == layer && f.name == name)
                {
                    return f.offset;
                }
            }
            return -1;
        }
    }

    /// Messages (setmessage/getmessage) kept by the ShadingContext
    public sealed unsafe class Message
    {
        public ulong name;
        public byte* data;
        public TypeDesc type;
        public int layeridx;
        public ulong sourcefile;
        public int sourceline;
        public Message next;

        public Message(ulong name, TypeDesc type, int layeridx, ulong sourcefile, int sourceline, Message next)
        {
            this.name = name;
            this.data = null;
            this.type = type;
            this.layeridx = layeridx;
            this.sourcefile = sourcefile;
            this.sourceline = sourceline;
            this.next = next;
        }

        public bool has_data() { return data != null; }
    }

    public sealed unsafe class MessageList : IDisposable
    {
        private Message list_head = null;
        private SimplePool message_data = new SimplePool(1024);

        public void clear()
        {
            list_head = null;
            message_data.clear();
        }

        public Message find(ulong name)
        {
            for (Message m = list_head; m != null; m = m.next)
            {
                if (m.name == name)
                {
                    return m;
                }
            }
            return null;
        }

        public void add(ulong name, void* data, TypeDesc type, int layeridx, ulong sourcefile, int sourceline)
        {
            list_head = new Message(name, type, layeridx, sourcefile, sourceline, list_head);
            if (data != null)
            {
                int size = (int)type.size();
                list_head.data = message_data.alloc(size);
                Buffer.MemoryCopy(data, list_head.data, size, size);
            }
        }

        public void Dispose()
        {
            message_data.Dispose();
        }
    }

    /// Buffer for the batched message blackboard (batched execution is not ported, kept for the interface)
    public sealed class BatchedMessageBuffer : IDisposable
    {
        public SimplePool message_data = new SimplePool(16 * 1024);
        public object list_head;

        public void clear()
        {
            list_head = null;
            message_data.clear();
        }

        public void Dispose() { message_data.Dispose(); }
    }
}

namespace OSL.pvt
{
    /// The "uniform" state of the shading system that the compiled shader reaches through ShaderGlobals.shadingStateUniform (shading_state_uniform.h)
    public sealed class ShadingStateUniform
    {
        public ColorSystem m_colorsystem = new ColorSystem();
        public ulong m_commonspace_synonym;
        public bool m_unknown_coordsys_error;
        public int m_max_warnings_per_thread;
    }

    public unsafe delegate int OpFolder(RuntimeOptimizer rop, int opnum);
    /// IL generator of an op: 'backend' is the IL backend object (its type is up to the backend agent)
    public delegate bool OpILGen(object backend, int opnum);

    /// Describes an op: its constant folder, its IL generator and some flags
    public sealed class OpDescriptor
    {
        public ustring name;
        /// Names of the generator and the folder in the op table (llvm_gen_<llvmgen>, constfold_<folder>), to find them in the registries lazily
        public string llvmgen_name, folder_name;
        public OpILGen ilgen;
        public OpFolder folder;
        public bool simple_assign;
        public int flags;

        public const int None = 0, SideEffects = 1, Tex = 2, StrCreate = 4, StrChars = 8, PoliceMisc = 16, Police = Tex | StrCreate | StrChars | PoliceMisc;

        public OpDescriptor() { }

        public OpDescriptor(string n, OpILGen ilgen, OpFolder fold = null, bool simple = false, int flags = 0)
        {
            name = new ustring(n);
            this.ilgen = ilgen;
            folder = fold;
            simple_assign = simple;
            this.flags = flags;
        }
    }

    /// Registries through which the optimizer and the IL backend add their parts of the op table, by op name (the C++ builds the table in
    /// setup_op_descriptors with the function pointers directly)
    public static class OpFolderTable
    {
        public static readonly Dictionary<string, OpFolder> folders = new Dictionary<string, OpFolder>();
    }

    public static class OpILGenTable
    {
        public static readonly Dictionary<string, OpILGen> generators = new Dictionary<string, OpILGen>();
    }

    public struct UserDataNeeded : IComparable<UserDataNeeded>
    {
        public ustring name;
        public int layer_num;
        public TypeDesc type;
        public IntPtr data;
        public bool derivs;

        public UserDataNeeded(ustring name, int layer_num, TypeDesc type, IntPtr data = default(IntPtr), bool derivs = false)
        {
            this.name = name;
            this.layer_num = layer_num;
            this.type = type;
            this.data = data;
            this.derivs = derivs;
        }

        public int CompareTo(UserDataNeeded b)
        {
            if (name != b.name)
            {
                return name.CompareTo(b.name);
            }
            if (type.basetype != b.type.basetype)
            {
                return type.basetype < b.type.basetype ? -1 : 1;
            }
            if (type.aggregate != b.type.aggregate)
            {
                return type.aggregate < b.type.aggregate ? -1 : 1;
            }
            if (type.arraylen != b.type.arraylen)
            {
                return type.arraylen < b.type.arraylen ? -1 : 1;
            }
            return 0;
        }
    }

    public struct AttributeNeeded : IComparable<AttributeNeeded>
    {
        public ustring name;
        public ustring scope;
        public TypeDesc type;
        public bool derivs;

        public AttributeNeeded(ustring name) : this(name, new ustring(""), new TypeDesc(), false)
        {
        }

        public AttributeNeeded(ustring name, ustring scope, TypeDesc type, bool derivs)
        {
            this.name = name;
            this.scope = scope;
            this.type = type;
            this.derivs = derivs;
        }

        public int CompareTo(AttributeNeeded b)
        {
            if (name != b.name)
            {
                return name.CompareTo(b.name);
            }
            if (scope != b.scope)
            {
                return scope.CompareTo(b.scope);
            }
            if (type.basetype != b.type.basetype)
            {
                return type.basetype < b.type.basetype ? -1 : 1;
            }
            if (type.aggregate != b.type.aggregate)
            {
                return type.aggregate < b.type.aggregate ? -1 : 1;
            }
            if (type.arraylen != b.type.arraylen)
            {
                return type.arraylen < b.type.arraylen ? -1 : 1;
            }
            return 0;
        }
    }

    /// Tracks the current, requested and peak values of a counter (memory statistics)
    public sealed class PeakCounter
    {
        private long m_current, m_requested, m_peak;

        public void clear()
        {
            m_current = 0;
            m_requested = 0;
            m_peak = 0;
        }

        public long current() { return m_current; }
        public long requested() { return m_requested; }
        public long peak() { return m_peak; }

        public void set(long newval)
        {
            long cur = m_current;
            if (newval > cur)
            {
                m_requested += (cur - newval);
            }
            m_current = newval;
            if (newval > m_peak)
            {
                m_peak = newval;
            }
        }

        public void add(long sz)
        {
            m_current += sz;
            if (sz > 0)
            {
                m_requested += sz;
                if (m_current > m_peak)
                {
                    m_peak = m_current;
                }
            }
        }

        public void sub(long sz) { m_current -= sz; }
        public void incr() { add(1); }
        public void decr() { sub(1); }

        public override string ToString()
        {
            return m_requested + " requested, " + m_peak + " peak, " + m_current + " current";
        }

        public string memstat()
        {
            return Strutil.memformat(m_requested) + " requested, " + Strutil.memformat(m_peak) + " peak, " + Strutil.memformat(m_current) + " current";
        }
    }

    /// A shader master: the parsed and resolved .oso of one shader, shared by all its instances
    public sealed partial class ShaderMaster
    {
        public ShadingSystemImpl m_shadingsys;
        public ShaderType m_shadertype;
        public string m_shadername = "";
        public string m_osofilename = "";
        public List<Opcode> m_ops = new List<Opcode>();
        public List<int> m_args = new List<int>();
        public List<Symbol> m_symbols = new List<Symbol>();
        public List<int> m_idefaults = new List<int>();
        public List<float> m_fdefaults = new List<float>();
        public List<ustring> m_sdefaults = new List<ustring>();
        public List<int> m_iconsts = new List<int>();
        public List<float> m_fconsts = new List<float>();
        public List<ustring> m_sconsts = new List<ustring>();
        public int m_firstparam, m_lastparam;
        public int m_maincodebegin, m_maincodeend;
        public int m_raytype_queries;
        public bool m_range_checking;

        public ShadingSystemImpl shadingsys() { return m_shadingsys; }
        public Symbol symbol(int index) { return index >= 0 ? m_symbols[index] : null; }
        public string shadername() { return m_shadername; }
        public string osofilename() { return m_osofilename; }
        public ShaderType shadertype() { return m_shadertype; }
        public string shadertypename() { return PvtFunctions.shadertypename(m_shadertype); }
        public int num_params() { return m_lastparam - m_firstparam; }
        public int raytype_queries() { return m_raytype_queries; }
        public bool range_checking() { return m_range_checking; }
        public void range_checking(bool b) { m_range_checking = b; }
    }

    /// The closure primitives known to the shading system (register_closure)
    public sealed class ClosureRegistry
    {
        public sealed class ClosureEntry
        {
            public int id;
            public ustring name;
            public int nformal;
            public int nkeyword;
            public List<ClosureParam> @params = new List<ClosureParam>();
            public int struct_size;
            public PrepareClosureFunc prepare;
            public SetupClosureFunc setup;
        }

        private Dictionary<ustring, int> m_closure_name_to_id = new Dictionary<ustring, int>();
        private List<ClosureEntry> m_closure_table = new List<ClosureEntry>();

        public ClosureEntry get_entry(int id) { return m_closure_table[id]; }
        public bool empty() { return m_closure_table.Count == 0; }

        public ClosureEntry get_entry(ustring name)
        {
            int id;
            if (m_closure_name_to_id.TryGetValue(name, out id))
            {
                return m_closure_table[id];
            }
            return null;
        }

        public void register_closure(string name, int id, ClosureParam[] @params, PrepareClosureFunc prepare, SetupClosureFunc setup)
        {
            if (m_closure_table.Count <= id)
            {
                while (m_closure_table.Count <= id)
                {
                    m_closure_table.Add(new ClosureEntry());
                }
            }
            ClosureEntry entry = m_closure_table[id];
            entry.id = id;
            entry.name = new ustring(name);
            entry.nformal = 0;
            entry.nkeyword = 0;
            entry.@params.Clear();
            int i = 0;
            for (; i < @params.Length && @params[i].type.basetype != TypeDesc.UNKNOWN; ++i)
            {
                entry.@params.Add(@params[i]);
                if (@params[i].key == null)
                {
                    entry.nformal++;
                }
                else
                {
                    entry.nkeyword++;
                }
            }
            entry.@params.Add(@params[i]);  // the CLOSURE_FINISH_PARAM entry
            entry.struct_size = @params[i].offset;
            entry.prepare = prepare;
            entry.setup = setup;
            m_closure_name_to_id[entry.name] = id;
        }
    }

    /// A parameter (or a part of one: an array element or a channel) that a connection reads or writes
    public struct ConnectedParam : IEquatable<ConnectedParam>
    {
        public int param;
        public int arrayindex;
        public int channel;
        public TypeSpec type;

        public static ConnectedParam make()
        {
            ConnectedParam c = new ConnectedParam();
            c.param = -1;
            c.arrayindex = -1;
            c.channel = -1;
            return c;
        }

        public bool valid() { return param >= 0; }
        public bool Equals(ConnectedParam p) { return param == p.param && arrayindex == p.arrayindex && channel == p.channel && type == p.type; }
        public override bool Equals(object o) { return o is ConnectedParam p && Equals(p); }
        public override int GetHashCode() { return param ^ (arrayindex << 8) ^ channel; }
        public static bool operator ==(ConnectedParam a, ConnectedParam b) { return a.Equals(b); }
        public static bool operator !=(ConnectedParam a, ConnectedParam b) { return !a.Equals(b); }
        public bool is_complete() { return arrayindex == -1 && channel == -1; }
    }

    public struct Connection : IEquatable<Connection>
    {
        public int srclayer;
        public ConnectedParam src;
        public ConnectedParam dst;

        public Connection(int srclay, ConnectedParam srccon, ConnectedParam dstcon)
        {
            srclayer = srclay;
            src = srccon;
            dst = dstcon;
        }

        public bool Equals(Connection c) { return srclayer == c.srclayer && src == c.src && dst == c.dst; }
        public override bool Equals(object o) { return o is Connection c && Equals(c); }
        public override int GetHashCode() { return srclayer ^ (src.GetHashCode() << 4) ^ (dst.GetHashCode() << 8); }
        public static bool operator ==(Connection a, Connection b) { return a.Equals(b); }
        public static bool operator !=(Connection a, Connection b) { return !a.Equals(b); }
        public bool is_complete() { return src.is_complete() && dst.is_complete(); }
    }

    /// Per-parameter override info of an instance
    public struct SymOverrideInfo
    {
        public Symbol.ValueSource m_valuesource;
        public bool m_connected_down;
        public bool m_interpolated;
        public bool m_interactive;
        public int m_arraylen;
        public int m_data_offset;

        public static SymOverrideInfo make()
        {
            SymOverrideInfo s = new SymOverrideInfo();
            s.m_valuesource = Symbol.ValueSource.DefaultVal;
            return s;
        }

        public void valuesource(Symbol.ValueSource v) { m_valuesource = v; }
        public Symbol.ValueSource valuesource() { return m_valuesource; }
        public string valuesourcename() { return Symbol.valuesourcename(valuesource()); }
        public bool connected_down() { return m_connected_down; }
        public void connected_down(bool c) { m_connected_down = c; }
        public bool connected() { return valuesource() == Symbol.ValueSource.ConnectedVal; }
        public bool interpolated() { return m_interpolated; }
        public void interpolated(bool val) { m_interpolated = val; }
        public bool interactive() { return m_interactive; }
        public void interactive(bool val) { m_interactive = val; }
        public int arraylen() { return m_arraylen; }
        public void arraylen(int s) { m_arraylen = s; }
        public int dataoffset() { return m_data_offset; }
        public void dataoffset(int o) { m_data_offset = o; }

        public static bool equivalent(SymOverrideInfo a, SymOverrideInfo b)
        {
            return a.valuesource() == b.valuesource() && a.interpolated() == b.interpolated() && a.interactive() == b.interactive()
                   && a.arraylen() == b.arraylen();
        }
    }

    /// One layer of a shader group: a copy of a master with the parameter values of this use
    public sealed partial class ShaderInstance
    {
        public ShaderMaster m_master;
        public SymOverrideInfo[] m_instoverrides = new SymOverrideInfo[0];
        public List<Symbol> m_instsymbols = new List<Symbol>();
        public List<Opcode> m_instops = new List<Opcode>();
        public List<int> m_instargs = new List<int>();
        public ustring m_layername;
        /// Values of the overridden parameters: the managed array (int[], float[], ustring[]) of parameter i is m_paramdata[i], null if the master default
        /// is used. (The C++ keeps three pooled vectors m_iparams/m_fparams/m_sparams; here every parameter owns its array, see the contract note.)
        public object[] m_paramdata = new object[0];
        public int m_id;
        public bool m_writes_globals;
        public bool m_userdata_params;
        public bool m_outgoing_connections;
        public bool m_renderer_outputs;
        public bool m_has_error_op;
        public bool m_has_trace_op;
        public bool m_merged_unused;
        public bool m_last_layer;
        public bool m_entry_layer;
        public List<Connection> m_connections = new List<Connection>();
        public int m_firstparam, m_lastparam;
        public int m_maincodebegin, m_maincodeend;
        public int m_Psym, m_Nsym;

        public ustring layername() { return m_layername; }
        public string shadername() { return m_master.shadername(); }
        public ShaderMaster master() { return m_master; }
        public ShadingSystemImpl shadingsys() { return m_master.shadingsys(); }
        public Symbol symbol(int index) { return index >= 0 && index < m_instsymbols.Count ? m_instsymbols[index] : null; }
        public int symbolindex(Symbol s) { return m_instsymbols.IndexOf(s); }
        public Symbol mastersymbol(int index) { return index >= 0 ? master().symbol(index) : null; }
        public int nconnections() { return m_connections.Count; }
        public Connection connection(int i) { return m_connections[i]; }
        public List<Connection> connections() { return m_connections; }

        public long clear_connections()
        {
            long mem = m_connections.Capacity * 40L;
            m_connections = new List<Connection>();
            return mem;
        }

        public int id() { return m_id; }
        public bool writes_globals() { return m_writes_globals; }
        public void writes_globals(bool val) { m_writes_globals = val; }
        public bool userdata_params() { return m_userdata_params; }
        public void userdata_params(bool val) { m_userdata_params = val; }
        public bool has_error_op() { return m_has_error_op; }
        public void has_error_op(bool val) { m_has_error_op = val; }
        public bool has_trace_op() { return m_has_trace_op; }
        public void has_trace_op(bool val) { m_has_trace_op = val; }
        public bool last_layer() { return m_last_layer; }
        public void last_layer(bool last) { m_last_layer = last; }
        public bool outgoing_connections() { return m_outgoing_connections; }
        public void outgoing_connections(bool out_) { m_outgoing_connections = out_; }
        public bool renderer_outputs() { return m_renderer_outputs; }
        public void renderer_outputs(bool out_) { m_renderer_outputs = out_; }
        public bool entry_layer() { return m_entry_layer; }
        public void entry_layer(bool val) { m_entry_layer = val; }
        public bool merged_unused() { return m_merged_unused; }
        public int maincodebegin() { return m_maincodebegin; }
        public int maincodeend() { return m_maincodeend; }
        public int firstparam() { return m_firstparam; }
        public int lastparam() { return m_lastparam; }
        public int Psym() { return m_Psym; }
        public int Nsym() { return m_Nsym; }
        public List<int> args() { return m_instargs; }
        public int arg(int argnum) { return m_instargs[argnum]; }
        public Symbol argsymbol(int argnum) { return symbol(arg(argnum)); }
        public List<Opcode> ops() { return m_instops; }
        public Opcode op(int opnum) { return m_instops[opnum]; }
        public List<Symbol> symbols() { return m_instsymbols; }
        public SymOverrideInfo instoverride(int i) { return m_instoverrides[i]; }

        /// Set the override info of symbol i (the C++ hands out a pointer into the vector; a struct array needs a ref)
        public ref SymOverrideInfo instoverride_ref(int i) { return ref m_instoverrides[i]; }

        /// Range of parameter symbols: [firstparam, lastparam) of the instance symbols
        public IEnumerable<Symbol> param_range()
        {
            if (m_instsymbols.Count != 0 && firstparam() != lastparam())
            {
                for (int i = firstparam(); i < lastparam(); ++i)
                {
                    yield return m_instsymbols[i];
                }
            }
        }

        public IEnumerable<Symbol> sym_range()
        {
            for (int i = 0; i < m_instsymbols.Count; ++i)
            {
                yield return m_instsymbols[i];
            }
        }

        public bool run_lazily()
        {
            if (!shadingsys().m_lazylayers)
            {
                return false;
            }
            if (last_layer())
            {
                return false;
            }
            if (writes_globals() && !shadingsys().m_lazyglobals)
            {
                return false;
            }
            if (renderer_outputs())
            {
                return false;
            }
            if (!outgoing_connections() && !shadingsys().m_lazyunconnected)
            {
                return false;
            }
            if (!shadingsys().m_lazyerror && has_error_op())
            {
                return false;
            }
            if (!shadingsys().lazy_trace() && has_trace_op())
            {
                return false;
            }
            return true;
        }

        public bool unused()
        {
            if (last_layer() || entry_layer())
            {
                return false;
            }
            bool used = (outgoing_connections() || renderer_outputs() || (writes_globals() && !shadingsys().m_lazyglobals)
                         || (!outgoing_connections() && !shadingsys().m_lazyunconnected) || (!shadingsys().m_lazyerror && has_error_op()));
            return !used || merged_unused();
        }

        public bool empty_instance()
        {
            return symbols().Count == 0 && (ops().Count == 0 || (ops().Count == 1 && ops()[0].opname() == Strings.end));
        }
    }

    /// Base of the passes that work on the instructions of a group's layers (the optimizer and the IL backend)
    public partial class OSOProcessorBase
    {
        public ShadingSystemImpl m_shadingsys;
        public ShaderGroup m_group;
        public ShadingContext m_context;
        public int m_debug;
        public ShaderInstance m_inst;
        public int m_layer;
        public List<int> m_bblockids = new List<int>();
        public List<bool> m_in_conditional = new List<bool>();
        public List<bool> m_in_loop = new List<bool>();
        public int m_first_return;
        public SortedSet<CallLayerKey> m_call_layers_inserted = new SortedSet<CallLayerKey>();

        public struct CallLayerKey : IComparable<CallLayerKey>
        {
            public int bblockid;
            public int layerid;

            public int CompareTo(CallLayerKey other)
            {
                if (bblockid != other.bblockid)
                {
                    return bblockid < other.bblockid ? -1 : 1;
                }
                return layerid.CompareTo(other.layerid);
            }
        }

        public OSOProcessorBase(ShadingSystemImpl shadingsys, ShaderGroup group, ShadingContext context)
        {
            m_shadingsys = shadingsys;
            m_group = group;
            m_context = context;
            m_inst = null;
            m_layer = -1;
            m_debug = shadingsys.debug();
            m_first_return = int.MaxValue;
        }

        public virtual void run() { }
        public ShaderGroup group() { return m_group; }
        public ShadingSystemImpl shadingsys() { return m_shadingsys; }
        public TextureSystem texturesys() { return shadingsys().texturesys(); }
        public RendererServices renderer() { return shadingsys().renderer(); }
        public ShadingContext shadingcontext() { return m_context; }
        public int debug() { return m_debug; }
        public int layer() { return m_layer; }
        public ShaderInstance inst() { return m_inst; }
        public Opcode op(int opnum) { return inst().ops()[opnum]; }
        public Symbol symbol(int symnum) { return inst().symbol(symnum); }
        public int oparg(Opcode op, int argnum) { return inst().arg(op.firstarg() + argnum); }
        public Symbol opargsym(Opcode op, int argnum) { return argnum < op.nargs() ? inst().argsymbol(op.firstarg() + argnum) : null; }
        public bool op_is_unconditionally_executed(int opnum) { return !m_in_conditional[opnum] && opnum < m_first_return; }
        public int bblockid(int opnum) { return m_bblockids[opnum]; }
    }
}
