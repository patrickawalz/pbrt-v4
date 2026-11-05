// OSL port: liboslexec/shadingsys.cpp (ShadingSystemImpl: construction, attribute / getattribute, groups, connections, optimization driver, statistics)
//
// Differences from the C++ that follow from the managed data model:
//  * attribute(), getattribute() and Parameter() take 'object' for the void* of the C++: a boxed value or an array int[], float[], string[] / ustring[] (or
//    long[] for the 64-bit statistics). getattribute fills the array it is given (a 1-element array for a scalar). Attributes that return pointers
//    to lists (textures_needed, userdata_names ...) take a 1-element object[] and put the managed list in it.
//  * The things the LLVM backend does (JIT, bitcode) are the hooks ilgen_compile_group and ilgen_release (see pvt.cs).
//  * The options with the "llvm_" prefix are kept (they are part of the attribute API) and apply to the IL backend where it has an equivalent.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using OIIO;
using Timer = OIIO.Timer;
using OSL;
using static OSL.pvt.PvtFunctions;

namespace OSL.pvt
{
    /// Names of the functions the backend gives the layers and the init of a group
    public static class LayerNames
    {
        public static string layer_function_name(ShaderGroup group, ShaderInstance inst, bool api = false)
        {
            bool use_optix = inst.shadingsys().use_optix();
            string prefix = use_optix && api ? "__direct_callable__" : "";
            return Fmt.format("{}osl_layer_group_{}_name_{}", prefix, group.name(), inst.layername());
        }

        public static string init_function_name(ShadingSystemImpl shadingsys, ShaderGroup group, bool api = false)
        {
            bool use_optix = shadingsys.use_optix();
            string prefix = use_optix && api ? "__direct_callable__" : "";
            return Fmt.format("{}osl_init_group_{}", prefix, group.name());
        }

        public static string fused_function_name(ShaderGroup group)
        {
            int nlayers = group.nlayers();
            ShaderInstance inst = group[nlayers - 1];
            return Fmt.format("__direct_callable__fused_{}_name_{}", group.name(), inst.layername());
        }
    }

    public sealed unsafe partial class ShadingSystemImpl : IDisposable
    {
        // ----- members (shadingsys's private data in oslexec_pvt.h) -----
        public RendererServices m_renderer;
        public TextureSystem m_texturesys;
        public ErrorHandler m_err;
        private LinkedList<string> m_errseen = new LinkedList<string>(), m_warnseen = new LinkedList<string>();
        private const int m_errseenmax = 32;
        private readonly object m_errmutex = new object();
        public Dictionary<ustring, ShaderMaster> m_shader_masters = new Dictionary<ustring, ShaderMaster>();
        public Dictionary<ustring, OpDescriptor> m_op_descriptor = new Dictionary<ustring, OpDescriptor>();

        public int m_statslevel;
        public int m_stat_rank_groups = 5;
        public bool m_lazylayers;
        public bool m_lazyglobals;
        public bool m_lazyunconnected;
        public bool m_lazyerror;
        public bool m_lazy_userdata;
        public bool m_lazy_trace;
        public bool m_userdata_isconnected;
        public bool m_clearmemory;
        public bool m_debugnan;
        public bool m_debug_uninit;
        public bool m_lockgeom_default;
        public bool m_strict_messages;
        public bool m_error_repeats;
        public bool m_range_checking;
        public bool m_connection_error;
        public bool m_greedyjit;
        public bool m_countlayerexecs;
        public bool m_relaxed_param_typecheck;
        public int m_profile;
        public int m_optimize;
        public bool m_opt_simplify_param;
        public bool m_opt_constant_fold;
        public bool m_opt_stale_assign;
        public bool m_opt_elide_useless_ops;
        public bool m_opt_elide_unconnected_outputs;
        public bool m_opt_peephole;
        public bool m_opt_coalesce_temps;
        public bool m_opt_assign;
        public bool m_opt_mix;
        public int m_opt_merge_instances;
        public bool m_opt_merge_instances_with_userdata;
        public bool m_opt_fold_getattribute;
        public bool m_opt_middleman;
        public bool m_opt_texture_handle;
        public bool m_opt_seed_bblock_aliases;
        public bool m_opt_useparam;
        public bool m_opt_groupdata;
        public bool m_opt_batched_analysis;
        public bool m_llvm_jit_fma;
        public bool m_llvm_jit_aggressive;
        public bool m_optimize_nondebug;
        public ustring m_llvm_jit_target;
        public int m_vector_width;
        public int m_opt_passes;
        public int m_llvm_optimize;
        public int m_debug;
        public int m_llvm_debug;
        public int m_llvm_debug_layers;
        public int m_llvm_debug_ops;
        public int m_llvm_target_host;
        public int m_llvm_debugging_symbols;
        public int m_llvm_profiling_events;
        public int m_llvm_output_bitcode;
        public int m_llvm_dumpasm;
        public bool m_dump_forced_llvm_bool_symbols;
        public bool m_dump_uniform_symbols;
        public bool m_dump_varying_symbols;
        public int m_debug_output_cpp = 0;
        public string m_cpp_output_dir = "";
        public string m_cpp_compiler = "";
        public string m_cpp_compiler_flags = "";
        public ustring m_llvm_prune_ir_strategy;
        public ustring m_debug_groupname;
        public ustring m_debug_layername;
        public ustring m_opt_layername;
        public ustring m_only_groupname;
        public ustring m_archive_groupname;
        public ustring m_archive_filename;
        public string m_searchpath = "";
        public List<string> m_searchpath_dirs = new List<string>();
        public string m_library_searchpath = "";
        public List<string> m_library_searchpath_dirs = new List<string>();
        public List<ustring> m_raytypes = new List<ustring>();
        public List<ustring> m_renderer_outputs = new List<ustring>();
        public List<SymLocationDesc> m_symlocs = new List<SymLocationDesc>();
        public int m_max_local_mem_KB;
        public int m_compile_report;
        public bool m_use_optix;
        public bool m_use_optix_cache;
        public int m_max_optix_groupdata_alloc;
        public bool m_buffer_printf;
        public bool m_no_noise;
        public bool m_no_pointcloud;
        public bool m_force_derivs;
        public bool m_allow_shader_replacement;
        public int m_exec_repeat;
        public int m_opt_warnings;
        public int m_gpu_opt_error;
        public bool m_optix_no_inline;
        public bool m_optix_no_inline_layer_funcs;
        public bool m_optix_merge_layer_funcs;
        public bool m_optix_no_inline_rend_lib;
        public int m_optix_no_inline_thresh;
        public int m_optix_force_inline_thresh;
        public ustring m_colorspace;
        public ShadingStateUniform m_shading_state_uniform = new ShadingStateUniform();
        public readonly object m_mutex = new object();

        // Statistics. Counters shared between threads are updated with Interlocked or under m_stat_mutex.
        public int m_stat_shaders_loaded, m_stat_shaders_requested;
        public PeakCounter m_stat_instances = new PeakCounter();
        public PeakCounter m_stat_contexts = new PeakCounter();
        public int m_stat_groups, m_stat_groupinstances, m_stat_instances_compiled, m_stat_groups_compiled, m_stat_empty_instances, m_stat_merged_inst,
                   m_stat_merged_inst_opt, m_stat_empty_groups, m_stat_regexes, m_stat_preopt_syms, m_stat_postopt_syms, m_stat_syms_with_derivs,
                   m_stat_preopt_ops, m_stat_postopt_ops, m_stat_middlemen_eliminated, m_stat_const_connections, m_stat_global_connections,
                   m_stat_tex_calls_codegened, m_stat_tex_calls_as_handles, m_stat_useparam_ops, m_stat_call_layers_inserted;
        public double m_stat_master_load_time, m_stat_optimization_time, m_stat_opt_locking_time, m_stat_specialization_time, m_stat_total_llvm_time,
                      m_stat_llvm_setup_time, m_stat_llvm_irgen_time, m_stat_llvm_opt_time, m_stat_llvm_jit_time, m_stat_inst_merge_time,
                      m_stat_getattribute_time, m_stat_getattribute_fail_time;
        public long m_stat_getattribute_calls, m_stat_get_userdata_calls, m_stat_noise_calls, m_stat_pointcloud_searches,
                    m_stat_pointcloud_searches_total_results, m_stat_pointcloud_gets, m_stat_pointcloud_writes, m_stat_layers_executed,
                    m_stat_total_shading_time_ticks, m_stat_reparam_calls_total, m_stat_reparam_bytes_total, m_stat_reparam_calls_changed,
                    m_stat_reparam_bytes_changed;
        public int m_stat_pointcloud_max_results, m_stat_pointcloud_failures;
        public int m_stat_max_llvm_local_mem;
        public PeakCounter m_stat_memory = new PeakCounter(), m_stat_mem_master = new PeakCounter(), m_stat_mem_master_ops = new PeakCounter(),
                           m_stat_mem_master_args = new PeakCounter(), m_stat_mem_master_syms = new PeakCounter(),
                           m_stat_mem_master_defaults = new PeakCounter(), m_stat_mem_master_consts = new PeakCounter(),
                           m_stat_mem_inst = new PeakCounter(), m_stat_mem_inst_syms = new PeakCounter(), m_stat_mem_inst_paramvals = new PeakCounter(),
                           m_stat_mem_inst_connections = new PeakCounter();
        public readonly object m_stat_mutex = new object();

        public ClosureRegistry m_closure_registry = new ClosureRegistry();
        private List<WeakReference<ShaderGroup>> m_all_shader_groups = new List<WeakReference<ShaderGroup>>();
        private readonly object m_all_shader_groups_mutex = new object();
        private ShaderGroup m_curgroup;
        public int m_groups_to_compile_count;
        public int m_threads_currently_compiling;
        private Dictionary<ustring, long> m_group_profile_times = new Dictionary<ustring, long>();
        private HashSet<ustring> m_inline_functions = new HashSet<ustring>();
        private HashSet<ustring> m_noinline_functions = new HashSet<ustring>();

        // Handles of the objects that ShaderGlobals points at
        private IntPtr m_renderer_handle;
        private IntPtr m_ssu_handle;

        /// Hooks of the IL backend: compile the (optimized) group to the entry points llvm_compiled_init / llvm_compiled_layer; return false on failure.
        /// The C++ code is the BackendLLVM constructor and run().
        public Func<ShaderGroup, ShadingContext, bool> ilgen_compile_group;
        /// Called when a group's compiled state can be dropped (group_post_jit_cleanup)
        public Action<ShaderGroup> ilgen_release;

        // The attribute tables
        private sealed class Attr
        {
            public int kind;  // 0 int, 1 float, 2 long, 3 string
            public Func<object> get;
            public Action<object> set;
        }

        private Dictionary<string, Attr> m_attrs = new Dictionary<string, Attr>();

        public ShadingSystemImpl(RendererServices renderer = null, TextureSystem texturesystem = null, ErrorHandler err = null)
        {
            m_renderer = renderer != null ? renderer : new RendererServices();
            m_texturesys = texturesystem;
            m_err = err;
            m_statslevel = 0;
            m_lazylayers = true;
            m_lazyglobals = true;
            m_lazyunconnected = true;
            m_lazyerror = true;
            m_lazy_userdata = false;
            m_lazy_trace = true;
            m_userdata_isconnected = false;
            m_clearmemory = false;
            m_debugnan = false;
            m_debug_uninit = false;
            m_lockgeom_default = true;
            m_strict_messages = true;
            m_error_repeats = false;
            m_range_checking = true;
            m_connection_error = true;
            m_greedyjit = false;
            m_countlayerexecs = false;
            m_relaxed_param_typecheck = false;
            m_profile = 0;
            m_optimize = 2;
            m_opt_simplify_param = true;
            m_opt_constant_fold = true;
            m_opt_stale_assign = true;
            m_opt_elide_useless_ops = true;
            m_opt_elide_unconnected_outputs = true;
            m_opt_peephole = true;
            m_opt_coalesce_temps = true;
            m_opt_assign = true;
            m_opt_mix = true;
            m_opt_merge_instances = 1;
            m_opt_merge_instances_with_userdata = true;
            m_opt_fold_getattribute = true;
            m_opt_middleman = true;
            m_opt_texture_handle = true;
            m_opt_seed_bblock_aliases = true;
            m_opt_useparam = false;
            m_opt_groupdata = true;
            m_opt_batched_analysis = false;
            m_llvm_jit_fma = false;
            m_llvm_jit_aggressive = false;
            m_optimize_nondebug = false;
            m_llvm_jit_target = new ustring("");
            m_llvm_prune_ir_strategy = new ustring("");
            m_debug_groupname = new ustring("");
            m_debug_layername = new ustring("");
            m_opt_layername = new ustring("");
            m_only_groupname = new ustring("");
            m_archive_groupname = new ustring("");
            m_archive_filename = new ustring("");
            m_vector_width = 4;
            m_opt_passes = 10;
            m_llvm_optimize = 1;
            m_debug = 0;
            m_llvm_debug = 0;
            m_llvm_debug_layers = 0;
            m_llvm_debug_ops = 0;
            m_llvm_target_host = 1;
            m_llvm_debugging_symbols = 0;
            m_llvm_profiling_events = 0;
            m_llvm_output_bitcode = 0;
            m_llvm_dumpasm = 0;
            m_dump_forced_llvm_bool_symbols = false;
            m_dump_uniform_symbols = false;
            m_dump_varying_symbols = false;
            m_max_local_mem_KB = 2048;
            m_compile_report = 0;
            m_use_optix = m_renderer.supports("OptiX") != 0;
            m_use_optix_cache = m_use_optix && m_renderer.supports("optix_ptx_cache") != 0;
            m_max_optix_groupdata_alloc = 0;
            m_buffer_printf = true;
            m_no_noise = false;
            m_no_pointcloud = false;
            m_force_derivs = false;
            m_allow_shader_replacement = false;
            m_exec_repeat = 1;
            m_opt_warnings = 0;
            m_gpu_opt_error = 0;
            m_optix_no_inline = false;
            m_optix_no_inline_layer_funcs = false;
            m_optix_merge_layer_funcs = true;
            m_optix_no_inline_rend_lib = false;
            m_optix_no_inline_thresh = 100000;
            m_optix_force_inline_thresh = 0;
            m_colorspace = new ustring("Rec709");
            m_stat_max_llvm_local_mem = 0;
            m_groups_to_compile_count = 0;
            m_threads_currently_compiling = 0;

            m_shading_state_uniform.m_commonspace_synonym = Strings.world.hash();
            m_shading_state_uniform.m_unknown_coordsys_error = true;
            m_shading_state_uniform.m_max_warnings_per_thread = 100;

            if (m_err == null)
            {
                m_err = ErrorHandler.default_handler;
            }
            if (m_texturesys == null)
            {
                m_texturesys = m_renderer.texturesys();
            }
            if (m_texturesys == null)
            {
                m_texturesys = new SimpleTextureSystem();
                m_texturesys.attribute("automip", 1);
                m_texturesys.attribute("autotile", 64);
            }

            // The compiled code finds the renderer and the shading state through handles in the ShaderGlobals
            m_renderer_handle = GCHandle.ToIntPtr(GCHandle.Alloc(m_renderer));
            m_ssu_handle = GCHandle.ToIntPtr(GCHandle.Alloc(m_shading_state_uniform));
            // The colour shadeops find their ColorSystem through the ShaderGlobals
            OslRuntime.get_colorsystem_hook = (void* oec) =>
            {
                ShaderGlobals* sg = (ShaderGlobals*)oec;
                if (sg != null && sg->shadingStateUniform != null)
                {
                    return ((ShadingStateUniform)ShaderGlobalsAccess.from_handle(sg->shadingStateUniform)).m_colorsystem;
                }
                return m_shading_state_uniform.m_colorsystem;
            };

            string llvm_debug_env = Sysutil.getenv("OSL_LLVM_DEBUG");
            if (!string.IsNullOrEmpty(llvm_debug_env))
            {
                int.TryParse(llvm_debug_env, out m_llvm_debug);
            }
            string cpp_debug_env = Sysutil.getenv("OSL_DEBUG_OUTPUT_CPP");
            if (!string.IsNullOrEmpty(cpp_debug_env))
            {
                int.TryParse(cpp_debug_env, out m_debug_output_cpp);
            }

            setup_attribute_table();

            string[] raytypes = { /*1*/ "camera", /*2*/ "shadow", /*4*/ "reflection", /*8*/ "refraction", /*16*/ "diffuse", /*32*/ "glossy",
                                  /*64*/ "subsurface", /*128*/ "displacement" };
            attribute("raytypes", new TypeDesc(TypeDesc.STRING, raytypes.Length), raytypes);

            string options = Sysutil.getenv("OSL_OPTIONS");
            if (!string.IsNullOrEmpty(options))
            {
                attribute("options", TypeDescs.TypeString, options);
            }

            setup_op_descriptors();
            m_shading_state_uniform.m_colorsystem.set_colorspace(m_colorspace.hash());
        }

        ~ShadingSystemImpl()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool m_disposed;

        private void Dispose(bool disposing)
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            if (disposing)
            {
                List<ShaderGroup> groups = new List<ShaderGroup>();
                lock (m_all_shader_groups_mutex)
                {
                    foreach (WeakReference<ShaderGroup> w in m_all_shader_groups)
                    {
                        ShaderGroup g;
                        if (w.TryGetTarget(out g))
                        {
                            groups.Add(g);
                        }
                    }
                }
                foreach (ShaderGroup g in groups)
                {
                    if (g.jitted() == 0 || g.batch_jitted() == 0)
                    {
                        group_post_jit_cleanup(g);
                    }
                }
                printstats();
            }
            if (m_renderer_handle != IntPtr.Zero)
            {
                GCHandle.FromIntPtr(m_renderer_handle).Free();
                m_renderer_handle = IntPtr.Zero;
            }
            if (m_ssu_handle != IntPtr.Zero)
            {
                GCHandle.FromIntPtr(m_ssu_handle).Free();
                m_ssu_handle = IntPtr.Zero;
            }
        }

        public IntPtr renderer_handle() { return m_renderer_handle; }
        public IntPtr shading_state_uniform_handle() { return m_ssu_handle; }

        // ----- op table -----

        private void setup_op_descriptors()
        {
            foreach (OpTableEntry e in OpTable.entries)
            {
                OpDescriptor d = new OpDescriptor();
                d.name = new ustring(e.name);
                d.llvmgen_name = e.llvmgen;
                d.folder_name = e.folder;
                d.simple_assign = e.simple;
                d.flags = e.flags;
                m_op_descriptor[new ustring(e.alias)] = d;
            }
        }

        /// Look up the descriptor of an op. The constant folder and the IL generator are bound by name from the registries when first asked for.
        public OpDescriptor op_descriptor(ustring opname)
        {
            OpDescriptor d;
            if (m_op_descriptor.TryGetValue(opname, out d))
            {
                if (d.folder == null && d.folder_name != "none")
                {
                    OpFolder f;
                    if (OpFolderTable.folders.TryGetValue(d.folder_name, out f))
                    {
                        d.folder = f;
                    }
                }
                if (d.ilgen == null)
                {
                    OpILGen g;
                    if (OpILGenTable.generators.TryGetValue(d.llvmgen_name, out g))
                    {
                        d.ilgen = g;
                    }
                }
                return d;
            }
            return null;
        }

        public OpDescriptor op_descriptormod(ustring opname)
        {
            return op_descriptor(opname);
        }

        // ----- closures -----

        public void register_closure(string name, int id, ClosureParam[] @params, PrepareClosureFunc prepare, SetupClosureFunc setup)
        {
            for (int i = 0; @params != null && i < @params.Length && @params[i].type != new TypeDesc(); ++i)
            {
                if (@params[i].key == null && @params[i].type.size() != @params[i].field_size)
                {
                    errorfmt("Parameter {} of '{}' closure is assigned to a field of incompatible size", i + 1, name);
                    return;
                }
            }
            m_closure_registry.register_closure(name, id, @params, prepare, setup);
        }

        public bool query_closure(ref string name, ref int id, out ClosureParam[] @params)
        {
            @params = null;
            ClosureRegistry.ClosureEntry entry = !string.IsNullOrEmpty(name) ? m_closure_registry.get_entry(new ustring(name)) : m_closure_registry.get_entry(id);
            if (entry == null)
            {
                return false;
            }
            name = entry.name.@string();
            id = entry.id;
            @params = entry.@params.ToArray();
            return true;
        }

        public ClosureRegistry.ClosureEntry find_closure(ustring name) { return m_closure_registry.get_entry(name); }
        public ClosureRegistry.ClosureEntry find_closure(int id) { return m_closure_registry.get_entry(id); }

        // ----- simple accessors -----

        public int debug() { return m_debug; }
        public RendererServices renderer() { return m_renderer; }
        public TextureSystem texturesys() { return m_texturesys; }
        public ErrorHandler errhandler() { return m_err; }
        public bool use_optix() { return m_use_optix; }
        public bool use_optix_cache() { return m_use_optix_cache; }
        public bool debug_nan() { return m_debugnan; }
        public bool debug_uninit() { return m_debug_uninit; }
        public bool lockgeom_default() { return m_lockgeom_default; }
        public bool strict_messages() { return m_strict_messages; }
        public bool range_checking() { return m_range_checking; }
        public bool unknown_coordsys_error() { return m_shading_state_uniform.m_unknown_coordsys_error; }
        public bool connection_error() { return m_connection_error; }
        public bool relaxed_param_typecheck() { return m_relaxed_param_typecheck; }
        public int optimize() { return m_optimize; }
        public int llvm_optimize() { return m_llvm_optimize; }
        public int llvm_debug() { return m_llvm_debug; }
        public int llvm_debug_layers() { return m_llvm_debug_layers; }
        public int llvm_debug_ops() { return m_llvm_debug_ops; }
        public int llvm_target_host() { return m_llvm_target_host; }
        public int llvm_debugging_symbols() { return m_llvm_debugging_symbols; }
        public int llvm_profiling_events() { return m_llvm_profiling_events; }
        public int llvm_output_bitcode() { return m_llvm_output_bitcode; }
        public bool dump_forced_llvm_bool_symbols() { return m_dump_forced_llvm_bool_symbols; }
        public bool dump_uniform_symbols() { return m_dump_uniform_symbols; }
        public bool dump_varying_symbols() { return m_dump_varying_symbols; }
        public int debug_output_cpp() { return m_debug_output_cpp; }
        public string cpp_output_dir() { return m_cpp_output_dir; }
        public string cpp_compiler() { return m_cpp_compiler; }
        public string cpp_compiler_flags() { return m_cpp_compiler_flags; }
        public ustring llvm_prune_ir_strategy() { return m_llvm_prune_ir_strategy; }
        public bool fold_getattribute() { return m_opt_fold_getattribute; }
        public bool opt_texture_handle() { return m_opt_texture_handle; }
        public int opt_passes() { return m_opt_passes; }
        public int max_warnings_per_thread() { return m_shading_state_uniform.m_max_warnings_per_thread; }
        public bool countlayerexecs() { return m_countlayerexecs; }
        public bool lazy_userdata() { return m_lazy_userdata; }
        public bool lazy_trace() { return m_lazy_trace; }
        public bool userdata_isconnected() { return m_userdata_isconnected; }
        public int profile() { return m_profile; }
        public bool no_noise() { return m_no_noise; }
        public bool no_pointcloud() { return m_no_pointcloud; }
        public bool force_derivs() { return m_force_derivs; }
        public bool allow_shader_replacement() { return m_allow_shader_replacement; }
        public ustring commonspace_synonym() { return OslStrings.from_hash(m_shading_state_uniform.m_commonspace_synonym); }
        public bool llvm_jit_fma() { return m_llvm_jit_fma; }
        public ustring llvm_jit_target() { return m_llvm_jit_target; }
        public ustring debug_groupname() { return m_debug_groupname; }
        public ustring debug_layername() { return m_debug_layername; }
        public string library_searchpath() { return m_library_searchpath; }
        public List<string> library_searchpath_dirs() { return m_library_searchpath_dirs; }
        public bool optix_no_inline() { return m_optix_no_inline; }
        public bool optix_no_inline_layer_funcs() { return m_optix_no_inline_layer_funcs; }
        public bool optix_merge_layer_funcs() { return m_optix_merge_layer_funcs; }
        public bool optix_no_inline_rend_lib() { return m_optix_no_inline_rend_lib; }
        public int optix_no_inline_thresh() { return m_optix_no_inline_thresh; }
        public int optix_force_inline_thresh() { return m_optix_force_inline_thresh; }
        public ColorSystem colorsystem() { return m_shading_state_uniform.m_colorsystem; }
        public void count_noise(int number = 1) { Interlocked.Add(ref m_stat_noise_calls, number); }

        public void pointcloud_stats(int search, int get, int results, int writes = 0)
        {
            lock (m_stat_mutex)
            {
                m_stat_pointcloud_searches += search;
                m_stat_pointcloud_gets += get;
                m_stat_pointcloud_searches_total_results += results;
                if (search != 0 && results == 0)
                {
                    ++m_stat_pointcloud_failures;
                }
                m_stat_pointcloud_max_results = Math.Max(m_stat_pointcloud_max_results, results);
                m_stat_pointcloud_writes += writes;
            }
        }

        public int raytype_bit(ustring name)
        {
            for (int i = 0, e = m_raytypes.Count; i < e; ++i)
            {
                if (name == m_raytypes[i])
                {
                    return 1 << i;
                }
            }
            return 0;  // not found
        }

        public void clear_symlocs() { m_symlocs.Clear(); }
        public void add_symlocs(IEnumerable<SymLocationDesc> symlocs) { m_symlocs.AddRange(symlocs); m_symlocs.Sort((a, b) => a.name.CompareTo(b.name)); }

        public SymLocationDesc? find_symloc(ustring name)
        {
            foreach (SymLocationDesc s in m_symlocs)
            {
                if (s.name == name)
                {
                    return s;
                }
            }
            return null;
        }

        public SymLocationDesc? find_symloc(ustring name, SymArena arena)
        {
            foreach (SymLocationDesc s in m_symlocs)
            {
                if (s.name == name && s.arena == arena && s.offset != -1)
                {
                    return s;
                }
            }
            return null;
        }

        public void register_inline_function(ustring name) { m_inline_functions.Add(name); }
        public void unregister_inline_function(ustring name) { m_inline_functions.Remove(name); }
        public void register_noinline_function(ustring name) { m_noinline_functions.Add(name); }
        public void unregister_noinline_function(ustring name) { m_noinline_functions.Remove(name); }
        public bool is_inline_function(ustring name) { return m_inline_functions.Contains(name); }
        public bool is_noinline_function(ustring name) { return m_noinline_functions.Contains(name); }

        // ----- constant pools (the optimizer allocates constants through these in the C++) -----

        public int[] alloc_int_constants(int n) { return new int[n]; }
        public float[] alloc_float_constants(int n) { return new float[n]; }
        public ustring[] alloc_string_constants(int n) { return new ustring[n]; }

        // ----- errors -----

        public void error(string msg)
        {
            lock (m_errmutex)
            {
                int n = 0;
                foreach (string s in m_errseen)
                {
                    if (s == msg && !m_error_repeats)
                    {
                        return;
                    }
                    ++n;
                }
                if (n >= m_errseenmax)
                {
                    m_errseen.RemoveFirst();
                }
                m_errseen.AddLast(msg);
                m_err.errorfmt(msg);
            }
        }

        public void warning(string msg)
        {
            lock (m_errmutex)
            {
                int n = 0;
                foreach (string s in m_warnseen)
                {
                    if (s == msg && !m_error_repeats)
                    {
                        return;
                    }
                    ++n;
                }
                if (n >= m_errseenmax)
                {
                    m_warnseen.RemoveFirst();
                }
                m_warnseen.AddLast(msg);
                m_err.warningfmt(msg);
            }
        }

        public void info(string msg)
        {
            lock (m_errmutex)
            {
                m_err.infofmt(msg);
            }
        }

        public void message(string msg)
        {
            lock (m_errmutex)
            {
                m_err.messagefmt(msg);
            }
        }

        public void errorfmt(string fmt, params object[] args) { error(Fmt.format(fmt, args)); }
        public void warningfmt(string fmt, params object[] args) { warning(Fmt.format(fmt, args)); }
        public void infofmt(string fmt, params object[] args) { info(Fmt.format(fmt, args)); }
        public void messagefmt(string fmt, params object[] args) { message(Fmt.format(fmt, args)); }
    }
}
