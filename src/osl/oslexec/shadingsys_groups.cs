// OSL port: liboslexec/shadingsys.cpp, part 2 (attributes, group construction, connections, execution entry points, optimization driver, stats)

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using OIIO;
using Timer = OIIO.Timer;
using OSL;
using static OSL.pvt.PvtFunctions;

namespace OSL.pvt
{
    public struct GroupStatSnapshot
    {
        public int ngroups;
        public int vmin, vmax, vmedian;
        public List<KeyValuePair<int, ustring>> ranked;
    }

    public sealed unsafe partial class ShadingSystemImpl
    {
        // ----- attribute tables -----
        // attribute name -> field. int-valued (bool or int), ustring and string fields.
        private static readonly string[] s_int_attrs =
        {
            "statistics:level=m_statslevel", "stat:rank_groups=m_stat_rank_groups", "debug=m_debug", "lazylayers=m_lazylayers", "lazyglobals=m_lazyglobals",
            "lazyunconnected=m_lazyunconnected", "lazyerror=m_lazyerror", "lazytrace=m_lazy_trace", "lazy_userdata=m_lazy_userdata",
            "userdata_isconnected=m_userdata_isconnected", "clearmemory=m_clearmemory", "debug_nan=m_debugnan", "debugnan=m_debugnan",
            "debug_uninit=m_debug_uninit", "lockgeom=m_lockgeom_default", "profile=m_profile", "optimize=m_optimize",
            "opt_simplify_param=m_opt_simplify_param", "opt_constant_fold=m_opt_constant_fold", "opt_stale_assign=m_opt_stale_assign",
            "opt_elide_useless_ops=m_opt_elide_useless_ops", "opt_elide_unconnected_outputs=m_opt_elide_unconnected_outputs",
            "opt_peephole=m_opt_peephole", "opt_coalesce_temps=m_opt_coalesce_temps", "opt_assign=m_opt_assign", "opt_mix=m_opt_mix",
            "opt_merge_instances=m_opt_merge_instances", "opt_merge_instances_with_userdata=m_opt_merge_instances_with_userdata",
            "opt_fold_getattribute=m_opt_fold_getattribute", "opt_middleman=m_opt_middleman", "opt_texture_handle=m_opt_texture_handle",
            "opt_seed_bblock_aliases=m_opt_seed_bblock_aliases", "opt_useparam=m_opt_useparam", "opt_groupdata=m_opt_groupdata",
            "opt_batched_analysis=m_opt_batched_analysis", "llvm_jit_fma=m_llvm_jit_fma", "llvm_jit_aggressive=m_llvm_jit_aggressive",
            "vector_width=m_vector_width", "opt_passes=m_opt_passes", "optimize_nondebug=m_optimize_nondebug", "llvm_optimize=m_llvm_optimize",
            "llvm_debug=m_llvm_debug", "llvm_debug_layers=m_llvm_debug_layers", "llvm_debug_ops=m_llvm_debug_ops", "llvm_target_host=m_llvm_target_host",
            "llvm_debugging_symbols=m_llvm_debugging_symbols", "llvm_profiling_events=m_llvm_profiling_events",
            "llvm_output_bitcode=m_llvm_output_bitcode", "llvm_dumpasm=m_llvm_dumpasm",
            "dump_forced_llvm_bool_symbols=m_dump_forced_llvm_bool_symbols", "dump_uniform_symbols=m_dump_uniform_symbols",
            "dump_varying_symbols=m_dump_varying_symbols", "debug_output_cpp=m_debug_output_cpp", "strict_messages=m_strict_messages",
            "range_checking=m_range_checking", "connection_error=m_connection_error", "greedyjit=m_greedyjit",
            "relaxed_param_typecheck=m_relaxed_param_typecheck", "countlayerexecs=m_countlayerexecs", "max_local_mem_KB=m_max_local_mem_KB",
            "compile_report=m_compile_report", "max_optix_groupdata_alloc=m_max_optix_groupdata_alloc", "buffer_printf=m_buffer_printf",
            "no_noise=m_no_noise", "no_pointcloud=m_no_pointcloud", "force_derivs=m_force_derivs", "allow_shader_replacement=m_allow_shader_replacement",
            "exec_repeat=m_exec_repeat", "opt_warnings=m_opt_warnings", "gpu_opt_error=m_gpu_opt_error", "optix_no_inline=m_optix_no_inline",
            "optix_no_inline_layer_funcs=m_optix_no_inline_layer_funcs", "optix_merge_layer_funcs=m_optix_merge_layer_funcs",
            "optix_no_inline_rend_lib=m_optix_no_inline_rend_lib", "optix_no_inline_thresh=m_optix_no_inline_thresh",
            "optix_force_inline_thresh=m_optix_force_inline_thresh",
        };

        private static readonly string[] s_ustring_attrs =
        {
            "llvm_jit_target=m_llvm_jit_target", "llvm_prune_ir_strategy=m_llvm_prune_ir_strategy", "debug_groupname=m_debug_groupname",
            "debug_layername=m_debug_layername", "opt_layername=m_opt_layername", "only_groupname=m_only_groupname",
            "archive_groupname=m_archive_groupname", "archive_filename=m_archive_filename",
        };

        private static readonly string[] s_string_attrs = { "cpp_output_dir=m_cpp_output_dir", "cpp_compiler=m_cpp_compiler", "cpp_compiler_flags=m_cpp_compiler_flags" };

        // attributes the C++ can set but not get
        private static readonly HashSet<string> s_set_only = new HashSet<string> { "lazyerror_unused" };
        // attributes that can be read but not set (the C++ getattribute-only ones)
        private static readonly HashSet<string> s_get_only = new HashSet<string> { };

        private static Dictionary<string, FieldInfo> s_attr_fields;

        private static void init_attr_fields()
        {
            if (s_attr_fields != null)
            {
                return;
            }
            Dictionary<string, FieldInfo> d = new Dictionary<string, FieldInfo>();
            foreach (string[] table in new string[][] { s_int_attrs, s_ustring_attrs, s_string_attrs })
            {
                foreach (string e in table)
                {
                    int eq = e.IndexOf('=');
                    d[e.Substring(0, eq)] = typeof(ShadingSystemImpl).GetField(e.Substring(eq + 1));
                }
            }
            s_attr_fields = d;
        }

        private void setup_attribute_table()
        {
            init_attr_fields();
        }

        private static bool is_type(TypeDesc t, TypeDesc u) { return t == u; }

        /// Set an attribute of the shading system. 'val' is a boxed int, float or string, or an array (string[] / ustring[] for string arrays).
        public bool attribute(string name, TypeDesc type, object val)
        {
            if (name == "options" && type == TypeDescs.TypeString)
            {
                return optparser(val is ustring us ? us.@string() : (string)val);
            }
            lock (m_mutex)
            {
                FieldInfo f;
                if (s_attr_fields.TryGetValue(name, out f))
                {
                    if (f.FieldType == typeof(bool) || f.FieldType == typeof(int))
                    {
                        if (type == TypeDescs.TypeInt)
                        {
                            int v = Convert.ToInt32(val);
                            f.SetValue(this, f.FieldType == typeof(bool) ? (object)(v != 0) : v);
                            return true;
                        }
                        return false;
                    }
                    if (type == TypeDescs.TypeString)
                    {
                        string s = val is ustring u2 ? u2.@string() : (string)val;
                        f.SetValue(this, f.FieldType == typeof(ustring) ? (object)new ustring(s) : s);
                        return true;
                    }
                    return false;
                }
                if (name == "unknown_coordsys_error" && type == TypeDescs.TypeInt)
                {
                    m_shading_state_uniform.m_unknown_coordsys_error = Convert.ToInt32(val) != 0;
                    return true;
                }
                if (name == "max_warnings_per_thread" && type == TypeDescs.TypeInt)
                {
                    m_shading_state_uniform.m_max_warnings_per_thread = Convert.ToInt32(val);
                    return true;
                }
                if (name == "commonspace" && type == TypeDescs.TypeString)
                {
                    m_shading_state_uniform.m_commonspace_synonym = new ustring(val is ustring u3 ? u3.@string() : (string)val).hash();
                    return true;
                }
                if (name == "searchpath:shader" && type == TypeDescs.TypeString)
                {
                    m_searchpath = (string)val;
                    m_searchpath_dirs.Clear();
                    Filesystem.searchpath_split(m_searchpath, m_searchpath_dirs);
                    return true;
                }
                if (name == "searchpath:library" && type == TypeDescs.TypeString)
                {
                    m_library_searchpath = (string)val;
                    m_library_searchpath_dirs.Clear();
                    Filesystem.searchpath_split(m_library_searchpath, m_library_searchpath_dirs);
                    return true;
                }
                if (name == "colorspace" && type == TypeDescs.TypeString)
                {
                    ustring c = new ustring((string)val);
                    if (colorsystem().set_colorspace(c.hash()))
                    {
                        m_colorspace = c;
                    }
                    else
                    {
                        errorfmt("Unknown color space \"{}\"", c);
                    }
                    return true;
                }
                if (name == "raytypes" && type.basetype == TypeDesc.STRING)
                {
                    if (type.numelements() > 32)
                    {
                        throw new InvalidOperationException("ShaderGlobals.raytype is an int, max of 32 raytypes");
                    }
                    m_raytypes.Clear();
                    foreach (ustring s in as_ustrings(val, type.numelements()))
                    {
                        m_raytypes.Add(s);
                    }
                    return true;
                }
                if (name == "renderer_outputs" && type.basetype == TypeDesc.STRING)
                {
                    m_renderer_outputs.Clear();
                    foreach (ustring s in as_ustrings(val, type.numelements()))
                    {
                        m_renderer_outputs.Add(s);
                    }
                    return true;
                }
                if (name == "error_repeats")
                {
                    // Special case: setting error_repeats also clears the "previously seen" error and warning lists.
                    m_errseen.Clear();
                    m_warnseen.Clear();
                    if (type == TypeDescs.TypeInt)
                    {
                        m_error_repeats = Convert.ToInt32(val) != 0;
                        return true;
                    }
                }
            }
            return false;
        }

        public bool attribute(string name, int val) { return attribute(name, TypeDescs.TypeInt, val); }
        public bool attribute(string name, float val) { return attribute(name, TypeDescs.TypeFloat, val); }
        public bool attribute(string name, string val) { return attribute(name, TypeDescs.TypeString, val); }

        public static IEnumerable<ustring> as_ustrings(object val, int n)
        {
            if (val is string s)
            {
                return new ustring[] { new ustring(s) };
            }
            if (val is ustring u)
            {
                return new ustring[] { u };
            }
            if (val is string[] sa)
            {
                return sa.Take(n).Select(x => new ustring(x));
            }
            if (val is ustring[] ua)
            {
                return ua.Take(n);
            }
            throw new ArgumentException("string attribute value must be a string or an array of strings");
        }

        /// OIIO::optparser: "name=value,name2=value2" ; ints, floats and strings (the type is inferred from the text)
        public bool optparser(string optstring)
        {
            bool ok = true;
            int i = 0;
            while (i < optstring.Length)
            {
                while (i < optstring.Length && (char.IsWhiteSpace(optstring[i]) || optstring[i] == ','))
                {
                    ++i;
                }
                int ns = i;
                while (i < optstring.Length && optstring[i] != '=' && optstring[i] != ',')
                {
                    ++i;
                }
                string name = optstring.Substring(ns, i - ns).Trim();
                string value = "1";
                if (i < optstring.Length && optstring[i] == '=')
                {
                    ++i;
                    if (i < optstring.Length && optstring[i] == '"')
                    {
                        int end = optstring.IndexOf('"', i + 1);
                        if (end < 0)
                        {
                            end = optstring.Length;
                        }
                        value = optstring.Substring(i + 1, end - i - 1);
                        i = Math.Min(optstring.Length, end + 1);
                    }
                    else
                    {
                        int vs = i;
                        while (i < optstring.Length && optstring[i] != ',')
                        {
                            ++i;
                        }
                        value = optstring.Substring(vs, i - vs).Trim();
                    }
                }
                if (name.Length == 0)
                {
                    continue;
                }
                int iv;
                float fv;
                if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out iv))
                {
                    ok &= attribute(name, TypeDescs.TypeInt, iv);
                }
                else if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fv))
                {
                    ok &= attribute(name, TypeDescs.TypeFloat, fv);
                }
                else
                {
                    ok &= attribute(name, TypeDescs.TypeString, value);
                }
            }
            return ok;
        }

        // The stat counters that getattribute exposes: attribute name -> field
        private static readonly Dictionary<string, string> s_stat_fields = new Dictionary<string, string>
        {
            { "stat:masters", "m_stat_shaders_loaded" }, { "stat:groups", "m_stat_groups" }, { "stat:instances_compiled", "m_stat_instances_compiled" },
            { "stat:groups_compiled", "m_stat_groups_compiled" }, { "stat:empty_instances", "m_stat_empty_instances" },
            { "stat:merged_inst", "m_stat_merged_inst" }, { "stat:merged_inst_opt", "m_stat_merged_inst_opt" },
            { "stat:empty_groups", "m_stat_empty_groups" }, { "stat:instances", "m_stat_groupinstances" }, { "stat:regexes", "m_stat_regexes" },
            { "stat:preopt_syms", "m_stat_preopt_syms" }, { "stat:postopt_syms", "m_stat_postopt_syms" },
            { "stat:syms_with_derivs", "m_stat_syms_with_derivs" }, { "stat:preopt_ops", "m_stat_preopt_ops" },
            { "stat:postopt_ops", "m_stat_postopt_ops" }, { "stat:middlemen_eliminated", "m_stat_middlemen_eliminated" },
            { "stat:const_connections", "m_stat_const_connections" }, { "stat:global_connections", "m_stat_global_connections" },
            { "stat:tex_calls_codegened", "m_stat_tex_calls_codegened" }, { "stat:tex_calls_as_handles", "m_stat_tex_calls_as_handles" },
            { "stat:useparam_ops", "m_stat_useparam_ops" }, { "stat:call_layers_inserted", "m_stat_call_layers_inserted" },
            { "stat:master_load_time", "m_stat_master_load_time" }, { "stat:optimization_time", "m_stat_optimization_time" },
            { "stat:opt_locking_time", "m_stat_opt_locking_time" }, { "stat:specialization_time", "m_stat_specialization_time" },
            { "stat:total_llvm_time", "m_stat_total_llvm_time" }, { "stat:llvm_setup_time", "m_stat_llvm_setup_time" },
            { "stat:llvm_irgen_time", "m_stat_llvm_irgen_time" }, { "stat:llvm_opt_time", "m_stat_llvm_opt_time" },
            { "stat:llvm_jit_time", "m_stat_llvm_jit_time" }, { "stat:inst_merge_time", "m_stat_inst_merge_time" },
            { "stat:getattribute_calls", "m_stat_getattribute_calls" }, { "stat:get_userdata_calls", "m_stat_get_userdata_calls" },
            { "stat:noise_calls", "m_stat_noise_calls" }, { "stat:pointcloud_searches", "m_stat_pointcloud_searches" },
            { "stat:pointcloud_gets", "m_stat_pointcloud_gets" }, { "stat:pointcloud_writes", "m_stat_pointcloud_writes" },
            { "stat:pointcloud_searches_total_results", "m_stat_pointcloud_searches_total_results" },
            { "stat:pointcloud_max_results", "m_stat_pointcloud_max_results" }, { "stat:pointcloud_failures", "m_stat_pointcloud_failures" },
            { "stat:reparam_calls_total", "m_stat_reparam_calls_total" }, { "stat:reparam_bytes_total", "m_stat_reparam_bytes_total" },
            { "stat:reparam_calls_changed", "m_stat_reparam_calls_changed" }, { "stat:reparam_bytes_changed", "m_stat_reparam_bytes_changed" },
        };

        private static readonly string[] s_peak_counters =
        {
            "memory", "mem_master", "mem_master_ops", "mem_master_args", "mem_master_syms", "mem_master_defaults", "mem_master_consts", "mem_inst",
            "mem_inst_syms", "mem_inst_paramvals", "mem_inst_connections",
        };

        public bool gather_group_stats(string metric, out GroupStatSnapshot snap)
        {
            snap = new GroupStatSnapshot();
            snap.ranked = new List<KeyValuePair<int, ustring>>();
            Func<ShaderGroup, int> getter;
            if (metric == "active_layers")
            {
                getter = g => g.stat_active_layers();
            }
            else if (metric == "network_depth")
            {
                getter = g => g.stat_network_depth();
            }
            else if (metric == "texture_ops")
            {
                getter = g => g.stat_texture_ops();
            }
            else if (metric == "noise_ops")
            {
                getter = g => g.stat_noise_ops();
            }
            else
            {
                return false;
            }

            // Collect a snapshot of all still-live compiled (optimized) groups. This never forces optimization of a group that isn't optimized yet.
            List<ShaderGroup> groups = new List<ShaderGroup>();
            lock (m_all_shader_groups_mutex)
            {
                foreach (WeakReference<ShaderGroup> w in m_all_shader_groups)
                {
                    ShaderGroup g;
                    if (w.TryGetTarget(out g) && g.optimized() != 0)
                    {
                        groups.Add(g);
                    }
                }
            }
            snap.ngroups = groups.Count;
            if (groups.Count == 0)
            {
                return true;
            }
            List<int> vals = groups.Select(g => getter(g)).ToList();
            vals.Sort();
            snap.vmin = vals[0];
            snap.vmax = vals[vals.Count - 1];
            snap.vmedian = vals[vals.Count / 2];
            foreach (ShaderGroup g in groups)
            {
                int v = getter(g);
                if (v > 0)
                {
                    snap.ranked.Add(new KeyValuePair<int, ustring>(v, g.name()));
                }
            }
            snap.ranked.Sort((a, b) => a.Key != b.Key ? b.Key.CompareTo(a.Key) : a.Value.CompareTo(b.Value));
            return true;
        }

        /// Get an attribute of the shading system. The result is an int, float, long or string (or string[] / int[] when the type is an array).
        public bool getattribute(string name, TypeDesc type, out object val)
        {
            val = null;
            // Ranked and aggregate post-optimization compile stats. Handled before taking m_mutex (see the C++).
            if (Strutil.starts_with(name, "stat:compiled_"))
            {
                string rest = name.Substring("stat:compiled_".Length);
                int colon = rest.IndexOf(':');
                GroupStatSnapshot snap;
                if (colon >= 0 && gather_group_stats(rest.Substring(0, colon), out snap))
                {
                    string sub = rest.Substring(colon + 1);
                    if (sub == "top_count" && type == TypeDescs.TypeInt)
                    {
                        val = snap.ranked.Count;
                        return true;
                    }
                    if (sub == "top_names" && type.basetype == TypeDesc.STRING)
                    {
                        string[] r = new string[type.numelements()];
                        for (int i = 0; i < r.Length; ++i)
                        {
                            r[i] = i < snap.ranked.Count ? snap.ranked[i].Value.@string() : "";
                        }
                        val = r;
                        return true;
                    }
                    if (sub == "top_values" && type.basetype == TypeDesc.INT)
                    {
                        int[] r = new int[type.numelements()];
                        for (int i = 0; i < r.Length && i < snap.ranked.Count; ++i)
                        {
                            r[i] = snap.ranked[i].Key;
                        }
                        val = r;
                        return true;
                    }
                    if (sub == "min" && type == TypeDescs.TypeInt)
                    {
                        val = snap.vmin;
                        return true;
                    }
                    if (sub == "max" && type == TypeDescs.TypeInt)
                    {
                        val = snap.vmax;
                        return true;
                    }
                    if (sub == "median" && type == TypeDescs.TypeInt)
                    {
                        val = snap.vmedian;
                        return true;
                    }
                }
            }

            lock (m_mutex)
            {
                if (name == "searchpath:shader" && type == TypeDescs.TypeString) { val = m_searchpath; return true; }
                if (name == "searchpath:library" && type == TypeDescs.TypeString) { val = m_library_searchpath; return true; }
                if (name == "colorspace" && type == TypeDescs.TypeString) { val = m_colorspace.@string(); return true; }
                FieldInfo f;
                if (s_attr_fields.TryGetValue(name, out f) && name != "llvm_prune_ir_strategy")
                {
                    object fv = f.GetValue(this);
                    if (f.FieldType == typeof(bool) || f.FieldType == typeof(int))
                    {
                        if (type == TypeDescs.TypeInt)
                        {
                            val = f.FieldType == typeof(bool) ? ((bool)fv ? 1 : 0) : (int)fv;
                            return true;
                        }
                        return false;
                    }
                    if (type == TypeDescs.TypeString)
                    {
                        val = fv is ustring u ? u.@string() : (string)fv;
                        return true;
                    }
                    return false;
                }
                if (name == "error_repeats" && type == TypeDescs.TypeInt) { val = m_error_repeats ? 1 : 0; return true; }
                if (name == "unknown_coordsys_error" && type == TypeDescs.TypeInt) { val = m_shading_state_uniform.m_unknown_coordsys_error ? 1 : 0; return true; }
                if (name == "max_warnings_per_thread" && type == TypeDescs.TypeInt) { val = m_shading_state_uniform.m_max_warnings_per_thread; return true; }
                if (name == "commonspace" && type == TypeDescs.TypeString) { val = OslStrings.from_hash(m_shading_state_uniform.m_commonspace_synonym).@string(); return true; }

                string sf;
                if (s_stat_fields.TryGetValue(name, out sf))
                {
                    object fv = typeof(ShadingSystemImpl).GetField(sf).GetValue(this);
                    if (fv is int iv && type == TypeDescs.TypeInt) { val = iv; return true; }
                    if (fv is double dv && type == TypeDescs.TypeFloat) { val = (float)dv; return true; }
                    if (fv is long lv && type.basetype == TypeDesc.LONGLONG && type.arraylen == 0) { val = lv; return true; }
                    return false;
                }
                if (Strutil.starts_with(name, "stat:") && type.basetype == TypeDesc.LONGLONG)
                {
                    foreach (string pc in s_peak_counters)
                    {
                        PeakCounter c = (PeakCounter)typeof(ShadingSystemImpl).GetField("m_stat_" + pc).GetValue(this);
                        if (name == "stat:" + pc + "_current") { val = c.current(); return true; }
                        if (name == "stat:" + pc + "_peak") { val = c.peak(); return true; }
                    }
                }
                if (name == "colorsystem" && type.basetype == TypeDesc.PTR) { val = colorsystem(); return true; }
                if ((name == "osl:simd" || name == "build:simd") && type == TypeDescs.TypeString) { val = ""; return true; }
                if (name == "build:compiler" && type == TypeDescs.TypeString) { val = ".NET " + Environment.Version; return true; }
                if (name == "build:platform" && type == TypeDescs.TypeString)
                {
                    val = (OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "MacOS" : "UnknownOS") + "/"
                          + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
                    return true;
                }
                if (name == "build:dependencies" && type == TypeDescs.TypeString) { val = ""; return true; }
                if ((name == "build:batched" || name == "hw:batched") && type == TypeDescs.TypeString) { val = ""; return true; }
                if (name == "hw:simd" && type == TypeDescs.TypeString) { val = ""; return true; }
                if ((name == "osl:cuda_version" || name == "osl:optix_version") && type == TypeDescs.TypeString) { val = ""; return true; }
                if (name == "osl:dependencies" && type == TypeDescs.TypeString)
                {
                    val = ".NET-" + Environment.Version + ",Cuda-NONE,OptiX-NONE";
                    return true;
                }
            }
            return false;
        }

        public bool getattribute(string name, out int val)
        {
            object o;
            val = 0;
            if (getattribute(name, TypeDescs.TypeInt, out o))
            {
                val = (int)o;
                return true;
            }
            return false;
        }

        public bool getattribute(string name, out string val)
        {
            object o;
            val = null;
            if (getattribute(name, TypeDescs.TypeString, out o))
            {
                val = (string)o;
                return true;
            }
            return false;
        }

        // ----- group attributes -----

        public bool attribute(ShaderGroup group, string name, TypeDesc type, object val)
        {
            // No current group attributes to set
            if (group == null)
            {
                return attribute(name, type, val);
            }
            lock (group.m_mutex)
            {
                if (name == "renderer_outputs" && type.basetype == TypeDesc.STRING)
                {
                    group.m_renderer_outputs.Clear();
                    group.m_renderer_outputs.AddRange(as_ustrings(val, type.numelements()));
                    return true;
                }
                if (name == "entry_layers" && type.basetype == TypeDesc.STRING)
                {
                    group.clear_entry_layers();
                    foreach (ustring s in as_ustrings(val, type.numelements()))
                    {
                        group.mark_entry_layer(s);
                    }
                    return true;
                }
                if (name == "exec_repeat" && type == TypeDescs.TypeInt)
                {
                    group.m_exec_repeat = Convert.ToInt32(val);
                    return true;
                }
                if (name == "groupname" && type == TypeDescs.TypeString)
                {
                    group.name(new ustring(val is ustring u ? u.@string() : val is string[] sa ? sa[0] : (string)val));
                    return true;
                }
            }
            return false;
        }

        /// Group attributes. For attributes the C++ returns as pointers to arrays (textures_needed ...), the result is the managed list (List of ustring,
        /// TypeDesc, int or byte); the others as in getattribute(name,...).
        public bool getattribute(ShaderGroup group, string name, TypeDesc type, out object val)
        {
            val = null;
            if (group == null)
            {
                return false;
            }

            if (name == "groupname" && type == TypeDescs.TypeString) { val = group.name().@string(); return true; }
            if (name == "num_layers" && type == TypeDescs.TypeInt) { val = group.nlayers(); return true; }
            if (name == "layer_names" && type.basetype == TypeDesc.STRING)
            {
                int n = Math.Min(type.numelements(), group.nlayers());
                string[] r = new string[type.numelements()];
                for (int i = 0; i < r.Length; ++i)
                {
                    r[i] = i < n ? group[i].layername().@string() : "";
                }
                val = r;
                return true;
            }
            if (name == "num_renderer_outputs" && type.basetype == TypeDesc.INT) { val = group.m_renderer_outputs.Count; return true; }
            if (name == "renderer_outputs" && type.basetype == TypeDesc.STRING)
            {
                string[] r = new string[type.numelements()];
                for (int i = 0; i < r.Length; ++i)
                {
                    r[i] = i < group.m_renderer_outputs.Count ? group.m_renderer_outputs[i].@string() : "";
                }
                val = r;
                return true;
            }
            if (name == "raytype_queries" && type.basetype == TypeDesc.INT) { val = group.raytype_queries(); return true; }
            if (name == "num_entry_layers" && type.basetype == TypeDesc.INT)
            {
                int n = 0;
                for (int i = 0; i < group.nlayers(); ++i)
                {
                    n += group.layer(i).entry_layer() ? 1 : 0;
                }
                val = n;
                return true;
            }
            if (name == "entry_layers" && type.basetype == TypeDesc.STRING)
            {
                string[] r = new string[type.numelements()];
                int n = 0;
                for (int i = 0; i < group.nlayers() && i < type.numelements(); ++i)
                {
                    if (group.layer(i).entry_layer())
                    {
                        r[n++] = group[i].layername().@string();
                    }
                }
                for (; n < r.Length; ++n)
                {
                    r[n] = "";
                }
                val = r;
                return true;
            }
            if (name == "group_init_name" && type.basetype == TypeDesc.STRING) { val = LayerNames.init_function_name(this, group, true); return true; }
            if (name == "group_entry_name" && type.basetype == TypeDesc.STRING)
            {
                int nlayers = group.nlayers();
                val = LayerNames.layer_function_name(group, group[nlayers - 1], true);
                return true;
            }
            if (name == "group_fused_name" && type.basetype == TypeDesc.STRING) { val = LayerNames.fused_function_name(group); return true; }
            if (name == "layer_osofiles" && type.basetype == TypeDesc.STRING)
            {
                string[] r = new string[type.numelements()];
                for (int i = 0; i < r.Length; ++i)
                {
                    r[i] = i < group.nlayers() ? group[i].master().osofilename() : "";
                }
                val = r;
                return true;
            }
            if (name == "pickle" && type == TypeDescs.TypeString) { val = group.serialize(); return true; }
            if (name == "exec_repeat" && type == TypeDescs.TypeInt) { val = group.m_exec_repeat; return true; }
            if (name == "stat:compiled_active_layers" && type == TypeDescs.TypeInt) { val = group.stat_active_layers(); return true; }
            if (name == "stat:compiled_network_depth" && type == TypeDescs.TypeInt) { val = group.stat_network_depth(); return true; }
            if (name == "stat:compiled_texture_ops" && type == TypeDescs.TypeInt) { val = group.stat_texture_ops(); return true; }
            if (name == "stat:compiled_noise_ops" && type == TypeDescs.TypeInt) { val = group.stat_noise_ops(); return true; }
            if (name == "interactive_params" && type.basetype == TypeDesc.PTR) { val = (IntPtr)group.m_interactive_arena; return true; }

            // All the remaining attributes require the group to already be optimized.
            if (group.optimized() == 0)
            {
                PerThreadInfo threadinfo = create_thread_info();
                ShadingContext ctx = get_context(threadinfo);
                optimize_group(group, ctx, false);
                release_context(ctx);
                destroy_thread_info(threadinfo);
            }

            if (name == "num_textures_needed" && type == TypeDescs.TypeInt) { val = group.m_textures_needed.Count; return true; }
            if (name == "textures_needed" && type.basetype == TypeDesc.PTR) { val = group.m_textures_needed; return true; }
            if (name == "unknown_textures_needed" && type == TypeDescs.TypeInt) { val = group.m_unknown_textures_needed ? 1 : 0; return true; }
            if (name == "num_closures_needed" && type == TypeDescs.TypeInt) { val = group.m_closures_needed.Count; return true; }
            if (name == "closures_needed" && type.basetype == TypeDesc.PTR) { val = group.m_closures_needed; return true; }
            if (name == "unknown_closures_needed" && type == TypeDescs.TypeInt) { val = group.m_unknown_closures_needed ? 1 : 0; return true; }
            if (name == "num_globals_needed" && type == TypeDescs.TypeInt) { val = group.m_globals_needed.Count; return true; }
            if (name == "globals_needed" && type.basetype == TypeDesc.PTR) { val = group.m_globals_needed; return true; }
            if (name == "globals_read" && type.basetype == TypeDesc.INT) { val = group.m_globals_read; return true; }
            if (name == "globals_write" && type.basetype == TypeDesc.INT) { val = group.m_globals_write; return true; }
            if (name == "num_userdata" && type == TypeDescs.TypeInt) { val = group.m_userdata_names.Count; return true; }
            if (name == "userdata_names" && type.basetype == TypeDesc.PTR) { val = group.m_userdata_names; return true; }
            if (name == "userdata_types" && type.basetype == TypeDesc.PTR) { val = group.m_userdata_types; return true; }
            if (name == "userdata_offsets" && type.basetype == TypeDesc.PTR) { val = group.m_userdata_offsets; return true; }
            if (name == "userdata_derivs" && type.basetype == TypeDesc.PTR) { val = group.m_userdata_derivs; return true; }
            if (name == "num_attributes_needed" && type == TypeDescs.TypeInt) { val = group.m_attributes_needed.Count; return true; }
            if (name == "attributes_needed" && type.basetype == TypeDesc.PTR) { val = group.m_attributes_needed; return true; }
            if (name == "attribute_scopes" && type.basetype == TypeDesc.PTR) { val = group.m_attribute_scopes; return true; }
            if (name == "attribute_types" && type.basetype == TypeDesc.PTR) { val = group.m_attribute_types; return true; }
            if (name == "attribute_derivs" && type.basetype == TypeDesc.PTR) { val = group.m_attribute_derivs; return true; }
            if (name == "unknown_attributes_needed" && type == TypeDescs.TypeInt) { val = group.m_unknown_attributes_needed ? 1 : 0; return true; }
            if (name == "group_id" && type == TypeDescs.TypeInt) { val = group.id(); return true; }
            if (name == "userdata_layers" && type.basetype == TypeDesc.PTR) { val = group.m_userdata_layers; return true; }
            if (name == "userdata_init_vals" && type.basetype == TypeDesc.PTR) { val = group.m_userdata_init_vals; return true; }
            if (name == "llvm_groupdata_size" && type == TypeDescs.TypeInt) { val = (int)group.llvm_groupdata_size(); return true; }
            return false;
        }

        // ----- statistics -----

        private sealed class GroupTimeCompare : IComparer<KeyValuePair<ustring, long>>
        {
            public int Compare(KeyValuePair<ustring, long> a, KeyValuePair<ustring, long> b) { return b.Value.CompareTo(a.Value); }
        }

        public string getstats(int level)
        {
            int columns = Sysutil.terminal_columns() - 2;
            if (level <= 0)
            {
                return "";
            }
            StringBuilder o = new StringBuilder();
            o.Append("Open Shading Language ").Append(OslVersion.OSL_LIBRARY_VERSION_STRING).Append("\n");
            string build_deps;
            getattribute("osl:dependencies", out build_deps);
            o.Append("  Build deps: ").Append(Strutil.wordwrap(string.Join(", ", (build_deps ?? "").Split(',')), columns, 14)).Append("\n");

            StringBuilder opt = new StringBuilder();
            Action<string, object> add = (n, v) => opt.Append(Fmt.format("{}={} ", n, v is bool b ? (b ? 1 : 0) : v));
            Action<string, ustring> adds = (n, v) =>
            {
                if (v.size() != 0)
                {
                    opt.Append(Fmt.format("{}=\"{}\" ", n, v));
                }
            };
            add("optimize", m_optimize); add("llvm_optimize", m_llvm_optimize); add("debug", m_debug); add("profile", m_profile);
            add("llvm_debug", m_llvm_debug); add("llvm_debug_layers", m_llvm_debug_layers); add("llvm_debug_ops", m_llvm_debug_ops);
            add("llvm_target_host", m_llvm_target_host); add("llvm_output_bitcode", m_llvm_output_bitcode); add("llvm_dumpasm", m_llvm_dumpasm);
            add("llvm_prune_ir_strategy", m_llvm_prune_ir_strategy.size() != 0 ? 1 : 0);
            add("lazylayers", m_lazylayers); add("lazyglobals", m_lazyglobals); add("lazyunconnected", m_lazyunconnected); add("lazyerror", m_lazyerror);
            add("lazy_userdata", m_lazy_userdata); add("lazy_trace", m_lazy_trace); add("userdata_isconnected", m_userdata_isconnected);
            add("clearmemory", m_clearmemory); add("debugnan", m_debugnan); add("debug_uninit", m_debug_uninit); add("lockgeom_default", m_lockgeom_default);
            add("strict_messages", m_strict_messages); add("error_repeats", m_error_repeats); add("range_checking", m_range_checking);
            add("greedyjit", m_greedyjit); add("countlayerexecs", m_countlayerexecs); add("opt_simplify_param", m_opt_simplify_param);
            add("opt_constant_fold", m_opt_constant_fold); add("opt_stale_assign", m_opt_stale_assign); add("opt_elide_useless_ops", m_opt_elide_useless_ops);
            add("opt_elide_unconnected_outputs", m_opt_elide_unconnected_outputs); add("opt_peephole", m_opt_peephole);
            add("opt_coalesce_temps", m_opt_coalesce_temps); add("opt_assign", m_opt_assign); add("opt_mix", m_opt_mix);
            add("opt_merge_instances", m_opt_merge_instances); add("opt_merge_instances_with_userdata", m_opt_merge_instances_with_userdata);
            add("opt_fold_getattribute", m_opt_fold_getattribute); add("opt_middleman", m_opt_middleman); add("opt_texture_handle", m_opt_texture_handle);
            add("opt_seed_bblock_aliases", m_opt_seed_bblock_aliases); add("opt_batched_analysis", m_opt_batched_analysis);
            add("llvm_jit_fma", m_llvm_jit_fma); add("llvm_jit_aggressive", m_llvm_jit_aggressive); add("vector_width", m_vector_width);
            adds("llvm_jit_target", m_llvm_jit_target); add("opt_passes", m_opt_passes); add("no_noise", m_no_noise ? 1 : 0);
            add("no_pointcloud", m_no_pointcloud ? 1 : 0); add("force_derivs", m_force_derivs ? 1 : 0);
            add("allow_shader_replacement", m_allow_shader_replacement ? 1 : 0); add("exec_repeat", m_exec_repeat); add("opt_warnings", m_opt_warnings);
            add("gpu_opt_error", m_gpu_opt_error); add("optix_no_inline", m_optix_no_inline); add("optix_no_inline_layer_funcs", m_optix_no_inline_layer_funcs);
            add("optix_merge_layer_funcs", m_optix_merge_layer_funcs); add("optix_no_inline_rend_lib", m_optix_no_inline_rend_lib);
            add("optix_no_inline_thresh", m_optix_no_inline_thresh); add("optix_force_inline_thresh", m_optix_force_inline_thresh);
            adds("debug_groupname", m_debug_groupname); adds("debug_layername", m_debug_layername); adds("archive_groupname", m_archive_groupname);
            adds("archive_filename", m_archive_filename);

            o.Append("  Build HW support: no SIMD\n");
            o.Append(Strutil.wordwrap(Fmt.format("  Runtime HW: {} cores, {:.1f}GB, {}", Sysutil.hardware_concurrency(), Sysutil.physical_memory() / (float)(1 << 30), ""), columns, 8)).Append("\n");
            o.Append("\n");
            o.Append("ShadingSystem Options:\n");
            o.Append("    ").Append(Strutil.wordwrap(opt.ToString(), columns, 4)).Append("\n");
            o.Append("\nOSL ShadingSystem statistics (").Append(Fmt.format("{}", System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this))).Append(")\n");
            if (m_stat_shaders_requested == 0 && m_stat_shaders_loaded == 0)
            {
                o.Append("  No shaders requested or loaded\n");
                return o.ToString();
            }

            o.Append("  Shaders:\n");
            o.Append("    Requested: ").Append(m_stat_shaders_requested).Append("\n");
            o.Append("    Loaded:    ").Append(m_stat_shaders_loaded).Append("\n");
            o.Append("    Masters:   ").Append(m_stat_shaders_loaded).Append("\n");
            o.Append("    Instances: ").Append(m_stat_instances).Append("\n");
            o.Append("  Time loading masters: ").Append(Strutil.timeintervalformat(m_stat_master_load_time, 2)).Append("\n");
            o.Append("  Shading groups:   ").Append(m_stat_groups).Append("\n");
            o.Append("    Total instances in all groups: ").Append(m_stat_groupinstances).Append("\n");
            float iperg = (float)m_stat_groupinstances / Math.Max(m_stat_groups, 1);
            o.Append("    Avg instances per group: ").Append(Fmt.format("{:.1f}", iperg)).Append("\n");
            o.Append("  Shading contexts: ").Append(m_stat_contexts).Append("\n");
            if (m_countlayerexecs)
            {
                o.Append("  Total layers executed: ").Append(m_stat_layers_executed).Append("\n");
            }
            o.Append("  Compiled ").Append(m_stat_groups_compiled).Append(" groups, ").Append(m_stat_instances_compiled).Append(" instances\n");
            o.Append("  Merged ").Append(m_stat_merged_inst + m_stat_merged_inst_opt).Append(" instances (").Append(m_stat_merged_inst).Append(" initial, ")
             .Append(m_stat_merged_inst_opt).Append(" after opt) in ").Append(Strutil.timeintervalformat(m_stat_inst_merge_time, 2)).Append("\n");
            if (m_stat_instances_compiled > 0)
            {
                o.Append("  After optimization, ").Append(m_stat_empty_instances).Append(" empty instances (")
                 .Append((int)(100.0f * m_stat_empty_instances / m_stat_instances_compiled)).Append("%)\n");
            }
            if (m_stat_groups_compiled > 0)
            {
                o.Append("  After optimization, ").Append(m_stat_empty_groups).Append(" empty groups (")
                 .Append((int)(100.0f * m_stat_empty_groups / m_stat_groups_compiled)).Append("%)\n");
            }
            if (m_stat_instances_compiled > 0 || m_stat_groups_compiled > 0)
            {
                o.Append(Fmt.format("  Optimized {} ops to {} ({:.1f}%)\n", m_stat_preopt_ops, m_stat_postopt_ops,
                                    100.0 * ((double)m_stat_postopt_ops / Math.Max(1, m_stat_preopt_ops) - 1.0)));
                o.Append(Fmt.format("  Optimized {} symbols to {} ({:.1f}%)\n", m_stat_preopt_syms, m_stat_postopt_syms,
                                    100.0 * ((double)m_stat_postopt_syms / Math.Max(1, m_stat_preopt_syms) - 1.0)));
                o.Append(Fmt.format("  Optimized {} useparam ops into {} llvm run layer calls\n", m_stat_useparam_ops, m_stat_call_layers_inserted));
            }
            o.Append(Fmt.format("  Constant connections eliminated: {}\n", m_stat_const_connections));
            o.Append(Fmt.format("  Global connections eliminated: {}\n", m_stat_global_connections));
            o.Append(Fmt.format("  Middlemen eliminated: {}\n", m_stat_middlemen_eliminated));
            o.Append(Fmt.format("  Derivatives needed on {} / {} symbols ({:.1f}%)\n", m_stat_syms_with_derivs, m_stat_postopt_syms,
                                (100.0 * m_stat_syms_with_derivs) / Math.Max(m_stat_postopt_syms, 1)));
            o.Append("  Runtime optimization cost: ").Append(Strutil.timeintervalformat(m_stat_optimization_time, 2)).Append("\n");
            o.Append("    locking:                   ").Append(Strutil.timeintervalformat(m_stat_opt_locking_time, 2)).Append("\n");
            o.Append("    runtime specialization:    ").Append(Strutil.timeintervalformat(m_stat_specialization_time, 2)).Append("\n");
            if (m_stat_total_llvm_time > 0.0)
            {
                o.Append("    LLVM setup:                ").Append(Strutil.timeintervalformat(m_stat_llvm_setup_time, 2)).Append("\n");
                o.Append("    LLVM IR gen:               ").Append(Strutil.timeintervalformat(m_stat_llvm_irgen_time, 2)).Append("\n");
                o.Append("    LLVM optimize:             ").Append(Strutil.timeintervalformat(m_stat_llvm_opt_time, 2)).Append("\n");
                o.Append("    LLVM JIT:                  ").Append(Strutil.timeintervalformat(m_stat_llvm_jit_time, 2)).Append("\n");
            }
            o.Append("  Texture calls compiled: ").Append(m_stat_tex_calls_codegened).Append(" (").Append(m_stat_tex_calls_as_handles).Append(" used handles)\n");
            o.Append("  Regex's compiled: ").Append(m_stat_regexes).Append("\n");
            o.Append("  Largest generated function local memory size: ").Append(m_stat_max_llvm_local_mem / 1024).Append(" KB\n");
            if (m_stat_getattribute_calls != 0)
            {
                o.Append("  getattribute calls: ").Append(m_stat_getattribute_calls).Append(" (").Append(Strutil.timeintervalformat(m_stat_getattribute_time, 2)).Append(")\n");
                o.Append("     (fail time ").Append(Strutil.timeintervalformat(m_stat_getattribute_fail_time, 2)).Append(")\n");
            }
            o.Append("  Number of get_userdata calls: ").Append(m_stat_get_userdata_calls).Append("\n");
            if (profile() > 1)
            {
                o.Append("  Number of noise calls: ").Append(m_stat_noise_calls).Append("\n");
            }
            if (m_stat_pointcloud_searches != 0 || m_stat_pointcloud_writes != 0)
            {
                o.Append("  Pointcloud operations:\n");
                o.Append("    pointcloud_search calls: ").Append(m_stat_pointcloud_searches).Append("\n");
                o.Append("      max query results: ").Append(m_stat_pointcloud_max_results).Append("\n");
                double avg = m_stat_pointcloud_searches != 0 ? (double)m_stat_pointcloud_searches_total_results / m_stat_pointcloud_searches : 0.0;
                o.Append("      average query results: ").Append(Fmt.format("{:.1f}", avg)).Append("\n");
                o.Append("      failures: ").Append(m_stat_pointcloud_failures).Append("\n");
                o.Append("    pointcloud_get calls: ").Append(m_stat_pointcloud_gets).Append("\n");
                o.Append("    pointcloud_write calls: ").Append(m_stat_pointcloud_writes).Append("\n");
            }
            if (m_stat_reparam_calls_total != 0)
            {
                o.Append(Fmt.format("  ReParameter: {} calls ({}) total, changed {} calls ({})\n", m_stat_reparam_calls_total,
                                    Strutil.memformat(m_stat_reparam_bytes_total), m_stat_reparam_calls_changed,
                                    Strutil.memformat(m_stat_reparam_bytes_changed)));
            }
            o.Append("  Memory total: ").Append(m_stat_memory.memstat()).Append('\n');
            o.Append("    Master memory: ").Append(m_stat_mem_master.memstat()).Append('\n');
            o.Append("        Master ops:            ").Append(m_stat_mem_master_ops.memstat()).Append('\n');
            o.Append("        Master args:           ").Append(m_stat_mem_master_args.memstat()).Append('\n');
            o.Append("        Master syms:           ").Append(m_stat_mem_master_syms.memstat()).Append('\n');
            o.Append("        Master defaults:       ").Append(m_stat_mem_master_defaults.memstat()).Append('\n');
            o.Append("        Master consts:         ").Append(m_stat_mem_master_consts.memstat()).Append('\n');
            o.Append("    Instance memory: ").Append(m_stat_mem_inst.memstat()).Append('\n');
            o.Append("        Instance syms:         ").Append(m_stat_mem_inst_syms.memstat()).Append('\n');
            o.Append("        Instance param values: ").Append(m_stat_mem_inst_paramvals.memstat()).Append('\n');
            o.Append("        Instance connections:  ").Append(m_stat_mem_inst_connections.memstat()).Append('\n');
            o.Append("    LLVM JIT memory: ").Append(Strutil.memformat(0)).Append('\n');

            if (m_profile != 0)
            {
                o.Append("  Execution profile:\n");
                o.Append("    Total shader execution time: ").Append(Strutil.timeintervalformat(Timer.seconds(m_stat_total_shading_time_ticks), 2))
                 .Append(" (sum of all threads)\n");
                lock (m_all_shader_groups_mutex)
                {
                    foreach (WeakReference<ShaderGroup> grp in m_all_shader_groups)
                    {
                        ShaderGroup g;
                        if (grp.TryGetTarget(out g))
                        {
                            long ticks = g.m_stat_total_shading_time_ticks;
                            long prev;
                            m_group_profile_times.TryGetValue(g.name(), out prev);
                            m_group_profile_times[g.name()] = prev + ticks;
                            g.m_stat_total_shading_time_ticks -= ticks;
                        }
                    }
                }
                lock (m_stat_mutex)
                {
                    List<KeyValuePair<ustring, long>> grouptimes = m_group_profile_times.ToList();
                    grouptimes.Sort(new GroupTimeCompare());
                    if (grouptimes.Count > 5)
                    {
                        grouptimes.RemoveRange(5, grouptimes.Count - 5);
                    }
                    if (grouptimes.Count != 0)
                    {
                        o.Append("    Most expensive shader groups:\n");
                    }
                    foreach (KeyValuePair<ustring, long> i in grouptimes)
                    {
                        o.Append("      ").Append(Strutil.timeintervalformat(Timer.seconds(i.Value), 2)).Append(' ')
                         .Append(i.Key.size() != 0 ? i.Key.@string() : "<unnamed group>").Append("\n");
                    }
                }
            }

            if (m_stat_groups_compiled > 0)
            {
                o.Append("  Shader compilation stats, post-optimized:\n");
                Action<string, string, string> emit = (label, unit, metric) =>
                {
                    GroupStatSnapshot snap;
                    if (!gather_group_stats(metric, out snap) || snap.ngroups == 0)
                    {
                        return;
                    }
                    o.Append(Fmt.format("    {}: min={} max={} median={}\n", label, snap.vmin, snap.vmax, snap.vmedian));
                    if (snap.ranked.Count == 0)
                    {
                        return;
                    }
                    int n = Math.Min(snap.ranked.Count, m_stat_rank_groups);
                    o.Append("      Top shader groups:\n");
                    for (int i = 0; i < n; ++i)
                    {
                        o.Append(Fmt.format("      {:>6} {}  \"{}\"\n", snap.ranked[i].Key, unit, snap.ranked[i].Value.size() != 0 ? snap.ranked[i].Value.@string() : "<unnamed>"));
                    }
                };
                emit("Active layers", "layers", "active_layers");
                emit("Network depth", "depth", "network_depth");
                emit("Texture ops", "ops", "texture_ops");
                emit("Noise ops", "ops", "noise_ops");
            }
            return o.ToString();
        }

        public void printstats()
        {
            if (m_statslevel == 0)
            {
                return;
            }
            m_err.messagefmt(getstats(m_statslevel));
        }

        // ----- groups -----

        public bool Parameter(string name, TypeDesc t, object val, ParamHints hints = ParamHints.none)
        {
            if (m_curgroup == null)
            {
                ShaderGroupBegin("");
            }
            return Parameter(m_curgroup, name, t, val, hints);
        }

        public bool Parameter(ShaderGroup group, string name, TypeDesc t, object val, ParamHints hints = ParamHints.none)
        {
            group.m_pending_params.Add(new ParamValue(name, t, 1, val));
            group.m_pending_hints.Add(hints);
            return true;
        }

        public ShaderGroup ShaderGroupBegin(string groupname = "")
        {
            ShaderGroup group = new ShaderGroup(groupname, this);
            group.m_exec_repeat = m_exec_repeat;
            lock (m_all_shader_groups_mutex)
            {
                // Group inherits global symbol location information that was active at the time the group was created.
                group.add_symlocs(m_symlocs);
                m_all_shader_groups.Add(new WeakReference<ShaderGroup>(group));
                ++m_groups_to_compile_count;
                m_curgroup = group;
            }
            return group;
        }

        public bool ShaderGroupEnd()
        {
            if (m_curgroup == null)
            {
                error("ShaderGroupEnd() was called without ShaderGroupBegin()");
                return false;
            }
            bool ok = ShaderGroupEnd(m_curgroup);
            m_curgroup = null;
            return ok;
        }

        public bool ShaderGroupEnd(ShaderGroup group)
        {
            lock (m_mutex)
            {
                // Mark the layers that can be run lazily
                if (!string.IsNullOrEmpty(group.m_group_use.@string()))
                {
                    int nlayers = group.nlayers();
                    for (int layer = 0; layer < nlayers; ++layer)
                    {
                        ShaderInstance inst = group[layer];
                        if (inst == null)
                        {
                            continue;
                        }
                        inst.last_layer(layer == nlayers - 1);
                    }

                    // Merge instances now if they really want it bad, otherwise wait until we optimize the group.
                    if (m_opt_merge_instances >= 2)
                    {
                        merge_instances(group);
                    }
                }

                // Merge the raytype_queries of all the individual layers
                group.m_raytype_queries = 0;
                for (int layer = 0, n = group.nlayers(); layer < n; ++layer)
                {
                    ShaderInstance inst = group[layer];
                    if (inst != null)
                    {
                        group.m_raytype_queries |= inst.master().raytype_queries();
                    }
                }

                ustring groupname = group.name();
                if (groupname.size() != 0 && groupname == m_archive_groupname)
                {
                    string filename = m_archive_filename.@string();
                    if (filename.Length == 0)
                    {
                        filename = Filesystem.filename(groupname.@string()) + ".tar.gz";
                        // Transform characters that will ruin our day when passed to tar as a filename.
                        if (Strutil.contains(filename, ":"))
                        {
                            filename = Strutil.replace(filename, ":", "_");
                        }
                        if (Strutil.contains(filename, "|"))
                        {
                            filename = Strutil.replace(filename, "|", "_");
                        }
                    }
                    archive_shadergroup(group, filename);
                }
                group.m_complete = true;
            }
            return true;
        }

        public bool Shader(string shaderusage, string shadername, string layername)
        {
            // Make sure we have a current attrib state
            bool singleton = m_curgroup == null;
            if (singleton)
            {
                ShaderGroupBegin("");
            }
            return Shader(m_curgroup, shaderusage, shadername, layername);
        }

        public bool Shader(ShaderGroup group, string shaderusage, string shadername, string layername)
        {
            ShaderMaster master = loadshader(shadername);
            if (master == null)
            {
                errorfmt("Could not find shader \"{}\"\n        group: {}", shadername, group.name());
                return false;
            }

            if (string.IsNullOrEmpty(shaderusage))
            {
                errorfmt("Shader usage required\n        group: {}", shadername, group.name());
                return false;
            }

            // If a layer name was not supplied, make one up.
            if (string.IsNullOrEmpty(layername))
            {
                layername = Fmt.format("{}_{}", master.shadername(), group.nlayers());
            }

            ShaderInstance instance = new ShaderInstance(master, layername);
            instance.parameters(group.m_pending_params, group.m_pending_hints);
            group.m_pending_params = new ParamValueList();
            group.m_pending_hints = new List<ParamHints>();

            if (string.IsNullOrEmpty(group.m_group_use.@string()))
            {
                // First in a group
                group.clear();
                m_stat_groups += 1;
                group.m_group_use = new ustring(shaderusage);
            }
            else if (shaderusage != group.m_group_use.@string())
            {
                errorfmt("Shader usage \"{}\" does not match current group ({})\n        group: {}", shaderusage, group.m_group_use, group.name());
                return false;
            }

            group.append(instance);
            m_stat_groupinstances += 1;
            return true;
        }

        public bool ConnectShaders(string srclayer, string srcparam, string dstlayer, string dstparam)
        {
            if (m_curgroup == null)
            {
                error("ConnectShaders can only be called within ShaderGroupBegin/End");
                return false;
            }
            return ConnectShaders(m_curgroup, srclayer, srcparam, dstlayer, dstparam);
        }

        public bool ConnectShaders(ShaderGroup group, string srclayer, string srcparam, string dstlayer, string dstparam)
        {
            // Basic sanity checks
            if (srclayer.Length == 0 || srcparam.Length == 0)
            {
                errorfmt("ConnectShaders: badly formed source layer/parameter\n        group: {}", group.name());
                return false;
            }
            if (dstlayer.Length == 0 || dstparam.Length == 0)
            {
                errorfmt("ConnectShaders: badly formed destination layer/parameter\n        group: {}", group.name());
                return false;
            }

            // Decode the layers, finding the indices within our group and pointers to the instances.
            ShaderInstance srcinst, dstinst;
            int srcinstindex = find_named_layer_in_group(group, new ustring(srclayer), out srcinst);
            int dstinstindex = find_named_layer_in_group(group, new ustring(dstlayer), out dstinst);
            if (srcinst == null)
            {
                errorfmt("ConnectShaders: source layer \"{}\" not found\n        group: {}", srclayer, group.name());
                return false;
            }
            if (dstinst == null)
            {
                errorfmt("ConnectShaders: destination layer \"{}\" not found\n        group: {}", dstlayer, group.name());
                return false;
            }
            if (dstinstindex <= srcinstindex)
            {
                errorfmt("ConnectShaders: destination layer must follow source layer (tried to connect {}.{} -> {}.{})\n        group: {}", srclayer, srcparam,
                         dstlayer, dstparam, group.name());
                return false;
            }

            // Decode the parameter names, find their symbols in their respective layers, and also decode request to attach specific array elements or
            // color/vector channels.
            ConnectedParam srccon = decode_connected_param(srcparam, srclayer, srcinst);
            ConnectedParam dstcon = decode_connected_param(dstparam, dstlayer, dstinst);
            if (!(srccon.valid() && dstcon.valid()))
            {
                if (connection_error())
                {
                    errorfmt("ConnectShaders: cannot connect a {} ({}) to a {} ({}), invalid connection\n        group: {}", srccon.type, srcparam, dstcon.type,
                             dstparam, group.name());
                }
                else
                {
                    warningfmt("ConnectShaders: cannot connect a {} ({}) to a {} ({}), invalid connection\n        group: {}", srccon.type, srcparam,
                               dstcon.type, dstparam, group.name());
                }
                return false;
            }

            if (srccon.type.is_structure() && dstcon.type.is_structure() && equivalent(srccon.type, dstcon.type))
            {
                // If the connection is whole struct-to-struct (and they are structs with equivalent data layout), implement it underneath as connections
                // between their respective fields.
                StructSpec srcstruct = srccon.type.structspec();
                StructSpec dststruct = dstcon.type.structspec();
                for (int i = 0; i < srcstruct.numfields(); ++i)
                {
                    string s = Fmt.format("{}.{}", srcparam, srcstruct.field(i).name);
                    string d = Fmt.format("{}.{}", dstparam, dststruct.field(i).name);
                    ConnectShaders(group, srclayer, s, dstlayer, d);
                }
                return true;
            }

            if (!assignable(dstcon.type, srccon.type))
            {
                if (connection_error())
                {
                    errorfmt("ConnectShaders: cannot connect a {} ({}) to a {} ({})\n        group: {}", srccon.type, srcparam, dstcon.type, dstparam,
                             group.name());
                }
                else
                {
                    warningfmt("ConnectShaders: cannot connect a {} ({}) to a {} ({})\n        group: {}", srccon.type, srcparam, dstcon.type, dstparam,
                               group.name());
                }
                return false;
            }

            Symbol dstsym = dstinst.mastersymbol(dstcon.param);
            if (dstsym != null && !dstsym.allowconnect())
            {
                string name = dstlayer.Length != 0 ? Fmt.format("{}.{}", dstlayer, dstparam) : dstparam;
                errorfmt("ConnectShaders: cannot connect to {} because it has metadata allowconnect=0\n        group: {}", name, group.name());
                return false;
            }

            dstinst.add_connection(srcinstindex, srccon, dstcon);
            dstinst.instoverride_ref(dstcon.param).valuesource(Symbol.ValueSource.ConnectedVal);
            srcinst.instoverride_ref(srccon.param).connected_down(true);
            srcinst.outgoing_connections(true);
            return true;
        }

        /// ShaderGroupBegin with a textual group description: "param float x 1; shader surface name layer; connect a.b c.d;"
        public ShaderGroup ShaderGroupBegin(string groupname, string usage, string groupspec)
        {
            ShaderGroup g = ShaderGroupBegin(groupname);
            bool err = false;
            string errdesc = "";
            string errstatement = "";
            List<int> intvals = new List<int>();
            List<float> floatvals = new List<float>();
            List<string> stringvals = new List<string>();
            string_view p = new string_view(groupspec);  // parse view
            while (p.size() != 0)
            {
                string_view pstart = p;  // save where we were for error reporting
                Strutil.skip_whitespace(ref p);
                if (p.size() == 0)
                {
                    break;
                }
                while (Strutil.parse_char(ref p, ';'))  // skip blank statements
                {
                }
                string keyword = Strutil.parse_word(ref p).ToString();

                if (keyword == "shader")
                {
                    string shadername = Strutil.parse_identifier(ref p, "").ToString();
                    Strutil.skip_whitespace(ref p);
                    string layername = Strutil.parse_until(ref p, " \t\r\n,;").ToString();
                    bool ok = Shader(g, usage, shadername, layername);
                    if (!ok)
                    {
                        errstatement = pstart.ToString();
                        err = true;
                        break;
                    }
                    if (!Strutil.parse_char(ref p, ';'))
                    {
                        Strutil.parse_char(ref p, ',');
                    }
                    Strutil.skip_whitespace(ref p);
                    continue;
                }

                if (keyword == "connect")
                {
                    Strutil.skip_whitespace(ref p);
                    string lay1 = Strutil.parse_until(ref p, " \t\r\n.").ToString();
                    Strutil.parse_char(ref p, '.');
                    string param1 = Strutil.parse_until(ref p, " \t\r\n,;").ToString();
                    Strutil.skip_whitespace(ref p);
                    string lay2 = Strutil.parse_until(ref p, " \t\r\n.").ToString();
                    Strutil.parse_char(ref p, '.');
                    string param2 = Strutil.parse_until(ref p, " \t\r\n,;").ToString();
                    bool ok = ConnectShaders(g, lay1, param1, lay2, param2);
                    if (!ok)
                    {
                        errstatement = pstart.ToString();
                        err = true;
                        break;
                    }
                    if (!Strutil.parse_char(ref p, ';'))
                    {
                        Strutil.parse_char(ref p, ',');
                    }
                    Strutil.skip_whitespace(ref p);
                    continue;
                }

                // Remaining case -- it should be declaring a parameter.
                string typestring;
                if (keyword == "param")
                {
                    typestring = Strutil.parse_word(ref p).ToString();
                }
                else if (keyword.Length != 0 && TypeDescParse.fromstring(keyword).basetype != TypeDesc.UNKNOWN)
                {
                    // compatibility: let the 'param' keyword be optional, if it's obvious that it's a type name.
                    typestring = keyword;
                }
                else
                {
                    err = true;
                    errdesc = Fmt.format("Unknown statement (expected 'param', 'shader', or 'connect'): \"{}\"", keyword);
                    break;
                }
                TypeDesc type;
                if (typestring == "int") { type = TypeDescs.TypeInt; }
                else if (typestring == "float") { type = TypeDescs.TypeFloat; }
                else if (typestring == "color") { type = TypeDescs.TypeColor; }
                else if (typestring == "point") { type = TypeDescs.TypePoint; }
                else if (typestring == "vector") { type = TypeDescs.TypeVector; }
                else if (typestring == "normal") { type = TypeDescs.TypeNormal; }
                else if (typestring == "matrix") { type = TypeDescs.TypeMatrix; }
                else if (typestring == "string") { type = TypeDescs.TypeString; }
                else
                {
                    err = true;
                    errdesc = Fmt.format("Unknown type: {}", typestring);
                    break;  // error
                }
                if (Strutil.parse_char(ref p, '['))
                {
                    int arraylen = -1;
                    int parsed_len;
                    if (Strutil.parse_int(ref p, out parsed_len))
                    {
                        arraylen = parsed_len;  // the C++ leaves it alone when there is no number: that is the unsized array
                    }
                    Strutil.parse_char(ref p, ']');
                    type.arraylen = arraylen;
                }
                StringBuilder paramname_string = new StringBuilder();
                while (true)
                {
                    paramname_string.Append(Strutil.parse_identifier(ref p, "").ToString());
                    Strutil.skip_whitespace(ref p);
                    if (Strutil.parse_char(ref p, '.'))
                    {
                        paramname_string.Append('.');
                    }
                    else
                    {
                        break;
                    }
                }
                string paramname = paramname_string.ToString();
                int lockgeom = m_lockgeom_default ? 1 : 0;
                ParamHints hints = ParamHints.none;
                int vals_to_preallocate = type.is_unsized_array() ? 1 : type.numelements() * type.aggregate;
                // Stop parsing values when we hit the limit based on the declaration.
                int max_vals = type.is_unsized_array() ? 1 << 28 : vals_to_preallocate;
                if (type.basetype == TypeDesc.INT)
                {
                    intvals.Clear();
                    int i;
                    for (i = 0; i < max_vals; ++i)
                    {
                        int val = 0;
                        if (Strutil.parse_int(ref p, out val))
                        {
                            intvals.Add(val);
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (type.is_unsized_array())
                    {
                        // For unsized arrays, now set the size based on how many values we actually read.
                        type.arraylen = Math.Max(1, i / type.aggregate);
                    }
                    // Zero-pad if we parsed fewer values than we needed
                    while (intvals.Count < type.numelements() * type.aggregate)
                    {
                        intvals.Add(0);
                    }
                }
                else if (type.basetype == TypeDesc.FLOAT)
                {
                    floatvals.Clear();
                    int i;
                    for (i = 0; i < max_vals; ++i)
                    {
                        float val = 0;
                        if (Strutil.parse_float(ref p, out val))
                        {
                            floatvals.Add(val);
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (type.is_unsized_array())
                    {
                        type.arraylen = Math.Max(1, i / type.aggregate);
                    }
                    while (floatvals.Count < type.numelements() * type.aggregate)
                    {
                        floatvals.Add(0);
                    }
                }
                else if (type.basetype == TypeDesc.STRING)
                {
                    stringvals.Clear();
                    int i;
                    for (i = 0; i < max_vals; ++i)
                    {
                        string s;
                        Strutil.skip_whitespace(ref p);
                        if (p.size() != 0 && p[0] == '"')
                        {
                            string_view sv;
                            if (!Strutil.parse_string(ref p, out sv))
                            {
                                break;
                            }
                            s = Strutil.unescape_chars(sv.ToString());
                        }
                        else
                        {
                            s = Strutil.parse_until(ref p, " \t\r\n;").ToString();
                            if (s.Length == 0)
                            {
                                break;
                            }
                        }
                        stringvals.Add(s);
                    }
                    if (type.is_unsized_array())
                    {
                        type.arraylen = Math.Max(1, i / type.aggregate);
                    }
                    while (stringvals.Count < type.numelements() * type.aggregate)
                    {
                        stringvals.Add("");
                    }
                }

                if (Strutil.parse_prefix(ref p, "[["))  // hints
                {
                    do
                    {
                        Strutil.skip_whitespace(ref p);
                        string hint_typename = Strutil.parse_word(ref p).ToString();
                        string hint_name = Strutil.parse_identifier(ref p, "").ToString();
                        TypeDesc hint_type = hint_typename.Length != 0 ? TypeDescParse.fromstring(hint_typename) : new TypeDesc();
                        if (hint_name.Length == 0 || hint_type.basetype == TypeDesc.UNKNOWN)
                        {
                            err = true;
                            errdesc = "malformed hint";
                            break;
                        }
                        if (!Strutil.parse_char(ref p, '='))
                        {
                            err = true;
                            errdesc = "hint expected value";
                            break;
                        }
                        if (hint_name == "lockgeom" && hint_type == TypeDescs.TypeInt)
                        {
                            if (!Strutil.parse_int(ref p, out lockgeom))
                            {
                                err = true;
                                errdesc = Fmt.format("hint {} expected int value", hint_name);
                                break;
                            }
                            hints = ParamHintsOps.set(hints, ParamHints.interpolated, lockgeom == 0);
                        }
                        else if (hint_name == "interpolated" && hint_type == TypeDescs.TypeInt)
                        {
                            int v = 0;
                            if (!Strutil.parse_int(ref p, out v))
                            {
                                err = true;
                                errdesc = Fmt.format("hint {} expected int value", hint_name);
                                break;
                            }
                            hints = ParamHintsOps.set(hints, ParamHints.interpolated, v != 0);
                        }
                        else if (hint_name == "interactive" && hint_type == TypeDescs.TypeInt)
                        {
                            int v = 0;
                            if (!Strutil.parse_int(ref p, out v))
                            {
                                err = true;
                                errdesc = Fmt.format("hint {} expected int value", hint_name);
                                break;
                            }
                            hints = ParamHintsOps.set(hints, ParamHints.interactive, v != 0);
                        }
                        else
                        {
                            err = true;
                            errdesc = Fmt.format("unknown hint '{} {}'", hint_type, hint_name);
                            break;
                        }
                    }
                    while (Strutil.parse_char(ref p, ','));
                    if (err)
                    {
                        break;
                    }
                    if (!Strutil.parse_prefix(ref p, "]]"))
                    {
                        err = true;
                        errdesc = "malformed hint";
                        break;
                    }
                }

                bool pok = true;
                if (type.basetype == TypeDesc.INT)
                {
                    pok = Parameter(g, paramname, type, intvals.ToArray(), hints);
                }
                else if (type.basetype == TypeDesc.FLOAT)
                {
                    pok = Parameter(g, paramname, type, floatvals.ToArray(), hints);
                }
                else if (type.basetype == TypeDesc.STRING)
                {
                    pok = Parameter(g, paramname, type, stringvals.ToArray(), hints);
                }
                if (!pok)
                {
                    errstatement = pstart.ToString();
                    err = true;
                    break;
                }

                Strutil.skip_whitespace(ref p);
                if (p.size() == 0)
                {
                    break;
                }
                if (Strutil.parse_char(ref p, ';') || Strutil.parse_char(ref p, ','))
                {
                    continue;  // next command
                }

                Strutil.parse_until_char(ref p, ';');
                if (!Strutil.parse_char(ref p, ';'))
                {
                    err = true;
                    errdesc = "semicolon expected";
                }
            }

            if (err)
            {
                string msg = Fmt.format("ShaderGroupBegin: error parsing group description: {}\n        group: {}", errdesc, g.name());
                if (errstatement.Length == 0)
                {
                    int offset = Math.Min(p.data_offset(), Math.Max(0, groupspec.Length - 1));
                    int a = groupspec.Length == 0 ? -1 : groupspec.LastIndexOf(';', offset);
                    int b = groupspec.Length == 0 ? -1 : groupspec.LastIndexOf(',', offset);
                    int begin_stmt = a < 0 ? b : (b < 0 ? a : Math.Min(a, b));
                    int end_stmt = groupspec.IndexOf(';', begin_stmt + 1);
                    if (end_stmt < 0)
                    {
                        end_stmt = groupspec.Length;
                    }
                    errstatement = groupspec.Substring(begin_stmt + 1, Math.Min(end_stmt - begin_stmt, groupspec.Length - (begin_stmt + 1)));
                }
                if (errstatement.Length != 0)
                {
                    msg += Fmt.format("\n        problem might be here: {}", errstatement);
                }
                error(msg);
                if (debug() != 0)
                {
                    infofmt("Broken group was:\n---{}\n---\n", groupspec);
                }
                return null;
            }
            return g;
        }

        public bool ReParameter(ShaderGroup group, string layername_, string paramname, TypeDesc type, object val)
        {
            // Find the named layer
            ustring layername = new ustring(layername_);
            ShaderInstance layer = null;
            int layerindex = -1;
            for (int i = 0, e = group.nlayers(); i < e; ++i)
            {
                if (group[i].layername() == layername)
                {
                    layer = group[i];
                    layerindex = i;
                    break;
                }
            }
            if (layer == null)
            {
                return false;  // could not find the named layer
            }

            // Find the named parameter within the layer
            int paramindex = layer.findparam(new ustring(paramname), false /* don't go to master */);
            if (paramindex < 0)
            {
                paramindex = layer.findparam(new ustring(paramname), true);
                if (paramindex >= 0)
                {
                    // This param exists, but it got optimized away, no failure
                    return true;
                }
            }
            if (paramindex < 0)
            {
                return false;  // could not find the named parameter
            }

            Symbol sym = layer.symbol(paramindex);
            if (sym == null)
            {
                return false;
            }

            // Check that it's declared to be an interactive parameter
            if (!sym.interactive())
            {
                errorfmt("ReParameter cannot adjust {}.{}, which was not declared interactive", layername, paramname);
                return false;
            }

            // Check for mismatch versus previously-declared type
            if (!relaxed_equivalent(sym.typespec(), new TypeSpec(type)))
            {
                return false;
            }

            // Can't change param value if the group has already been optimized, unless that parameter is marked lockgeom=0.
            if (group.optimized() != 0 && sym.lockgeom())
            {
                return false;
            }

            // Do the deed
            int offset = group.interactive_param_offset(layerindex, sym.name());
            if (offset >= 0)
            {
                int size = (int)type.size();
                Interlocked.Add(ref m_stat_reparam_calls_total, 1);
                Interlocked.Add(ref m_stat_reparam_bytes_total, (long)size);

                // Serialize the value as it is stored: ustringhashes for strings
                byte[] payload = new byte[size];
                if (type == TypeDescs.TypeString)
                {
                    string s = val is ustring u ? u.@string() : val is string[] sa ? sa[0] : (string)val;
                    BitConverter.GetBytes(new ustring(s).hash()).CopyTo(payload, 0);
                }
                else if (val is int[] ia)
                {
                    Buffer.BlockCopy(ia, 0, payload, 0, Math.Min(size, ia.Length * 4));
                }
                else if (val is float[] fa)
                {
                    Buffer.BlockCopy(fa, 0, payload, 0, Math.Min(size, fa.Length * 4));
                }
                else if (val is int iv)
                {
                    BitConverter.GetBytes(iv).CopyTo(payload, 0);
                }
                else if (val is float fv)
                {
                    BitConverter.GetBytes(fv).CopyTo(payload, 0);
                }
                else
                {
                    throw new ArgumentException("ReParameter: unsupported value");
                }

                byte* arena = group.interactive_arena_ptr();
                bool differs = false;
                for (int i = 0; i < size; ++i)
                {
                    if (arena[offset + i] != payload[i])
                    {
                        differs = true;
                        arena[offset + i] = payload[i];
                    }
                }
                if (differs)
                {
                    Interlocked.Add(ref m_stat_reparam_calls_changed, 1);
                    Interlocked.Add(ref m_stat_reparam_bytes_changed, (long)size);
                }
            }
            return true;
        }

        // ----- threads and contexts -----

        public PerThreadInfo create_thread_info() { return new PerThreadInfo(); }

        public void destroy_thread_info(PerThreadInfo threadinfo)
        {
            if (threadinfo != null)
            {
                threadinfo.Dispose();
            }
        }

        public ShadingContext get_context(PerThreadInfo threadinfo, TexturePerthread texture_threadinfo = null)
        {
            if (threadinfo == null)
            {
                error("ShadingSystem::get_context called without a PerThreadInfo");
                return null;
            }
            ShadingContext ctx = threadinfo.context_pool.Count == 0 ? new ShadingContext(this, threadinfo) : threadinfo.pop_context();
            ctx.texture_thread_info(texture_threadinfo);
            return ctx;
        }

        public void release_context(ShadingContext ctx)
        {
            if (ctx == null)
            {
                return;
            }
            ctx.process_errors();
            ctx.thread_info().context_pool.Push(ctx);
        }

        public bool execute(ShadingContext ctx, ShaderGroup group, int thread_index, int shade_index, ref ShaderGlobals ssg, void* userdata_base_ptr,
                            void* output_base_ptr, bool run = true)
        {
            return ctx.execute(group, thread_index, shade_index, ref ssg, userdata_base_ptr, output_base_ptr, run);
        }

        public Symbol get_symbol(ShadingContext ctx, ustring layername, ustring symbolname, out TypeDesc type)
        {
            type = new TypeDesc();
            Symbol sym = ctx.symbol(layername, symbolname);
            if (sym != null)
            {
                type = sym.typespec().simpletype();
            }
            return sym;
        }

        public int find_named_layer_in_group(ShaderGroup group, ustring layername, out ShaderInstance inst)
        {
            inst = null;
            if (group.m_group_use.size() == 0)
            {
                return -1;
            }
            for (int i = 0; i < group.nlayers(); ++i)
            {
                if (group[i].layername() == layername)
                {
                    inst = group[i];
                    return i;
                }
            }
            return -1;
        }

        public ConnectedParam decode_connected_param(string connectionname, string layername, ShaderInstance inst)
        {
            ConnectedParam c = ConnectedParam.make();  // initializes to "invalid"

            // Look for a bracket in the "parameter name"
            int bracketpos = connectionname.IndexOf('[');
            // Grab just the part of the param name up to the bracket
            ustring param = new ustring(bracketpos < 0 ? connectionname : connectionname.Substring(0, bracketpos));
            string_view cname_remaining = bracketpos < 0 ? new string_view("") : new string_view(connectionname, bracketpos, connectionname.Length - bracketpos);

            // Search for the param with that name, fail if not found
            c.param = inst.findsymbol(param);
            if (c.param < 0)
            {
                if (connection_error())
                {
                    errorfmt("ConnectShaders: \"{}\" is not a parameter or global of layer \"{}\" (shader \"{}\")", param, layername, inst.shadername());
                }
                else
                {
                    warningfmt("ConnectShaders: \"{}\" is not a parameter or global of layer \"{}\" (shader \"{}\")", param, layername, inst.shadername());
                }
                return c;
            }

            Symbol sym = inst.mastersymbol(c.param);

            // Only params, output params, and globals are legal for connections
            if (!(sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam || sym.symtype() == SymType.SymTypeGlobal))
            {
                errorfmt("ConnectShaders: \"{}\" is not a parameter or global of layer \"{}\" (shader \"%s\")", param, layername, inst.shadername());
                c.param = -1;  // mark as invalid
                return c;
            }

            c.type = sym.typespec();

            if (cname_remaining.size() != 0 && c.type.is_array())
            {
                // There was at least one set of brackets that appears to be selecting an array element.
                int index = 0;
                if (!(Strutil.parse_char(ref cname_remaining, '[') && Strutil.parse_int(ref cname_remaining, out index) && Strutil.parse_char(ref cname_remaining, ']')))
                {
                    errorfmt("ConnectShaders: malformed parameter \"{}\"", connectionname);
                    c.param = -1;  // mark as invalid
                    return c;
                }
                c.arrayindex = index;
                if (c.arrayindex >= c.type.arraylength())
                {
                    errorfmt("ConnectShaders: cannot request array element {} from a {}", connectionname, c.type);
                    c.arrayindex = c.type.arraylength() - 1;  // clamp it
                }
                c.type.make_array(0);                              // chop to the element type
                Strutil.skip_whitespace(ref cname_remaining);      // skip to next bracket
            }

            if (cname_remaining.size() != 0 && cname_remaining.front() == '[' && !c.type.is_closure() && c.type.aggregate() != TypeDesc.SCALAR)
            {
                // There was at least one set of brackets that appears to be selecting a color/vector component.
                int index = 0;
                if (!(Strutil.parse_char(ref cname_remaining, '[') && Strutil.parse_int(ref cname_remaining, out index) && Strutil.parse_char(ref cname_remaining, ']')))
                {
                    errorfmt("ConnectShaders: malformed parameter \"{}\"", connectionname);
                    c.param = -1;  // mark as invalid
                    return c;
                }
                c.channel = index;
                if (c.channel >= (int)c.type.aggregate())
                {
                    errorfmt("ConnectShaders: cannot request component {} from a {}", connectionname, c.type);
                    c.channel = (int)c.type.aggregate() - 1;  // clamp it
                }
                // chop to just the scalar part
                c.type = new TypeSpec(new TypeDesc(c.type.simpletype().basetype));
                Strutil.skip_whitespace(ref cname_remaining);
            }

            // Deal with left over nonsense or unsupported param designations
            if (cname_remaining.size() != 0)
            {
                // Still a leftover bracket, no idea what to do about that
                errorfmt("ConnectShaders: don't know how to connect '{}' when \"{}\" is a \"{}\"", connectionname, param, c.type);
                c.param = -1;  // mark as invalid
            }
            return c;
        }

        public bool is_renderer_output(ustring layername, ustring paramname, ShaderGroup group)
        {
            ustring name2 = new ustring(Fmt.format("{}.{}", layername, paramname));
            if (group != null)
            {
                foreach (SymLocationDesc sl in group.m_symlocs)
                {
                    if (sl.arena == SymArena.Outputs && (sl.name == paramname || sl.name == name2))
                    {
                        return true;
                    }
                }
                List<ustring> gaovs = group.m_renderer_outputs;
                if (gaovs.Count > 0)
                {
                    if (gaovs.Contains(paramname) || gaovs.Contains(name2))
                    {
                        return true;
                    }
                }
            }
            List<ustring> aovs = m_renderer_outputs;
            if (aovs.Count > 0)
            {
                if (aovs.Contains(paramname) || aovs.Contains(name2))
                {
                    return true;
                }
            }
            return false;
        }

        public void group_post_jit_cleanup(ShaderGroup group)
        {
            // Once we're generated the IR, we really don't need the ops and args, and we only need the syms that include the params.
            long symmem = 0;
            long connectionmem = 0;
            for (int layer = 0; layer < group.nlayers(); ++layer)
            {
                ShaderInstance inst = group[layer];
                // We no longer needs ops and args
                inst.ops().Clear();
                inst.args().Clear();
                if (inst.unused())
                {
                    // If we'll never use the layer, we don't need the syms at all
                    symmem += inst.symbols().Count * 128;
                    inst.symbols().Clear();
                    // also don't need the connection info any more
                    connectionmem += inst.clear_connections();
                }
            }
            if (ilgen_release != null)
            {
                ilgen_release(group);
            }
            lock (m_stat_mutex)
            {
                // adjust memory stats
                m_stat_mem_inst_syms.sub(symmem);
                m_stat_mem_inst_connections.sub(connectionmem);
                m_stat_mem_inst.sub(symmem + connectionmem);
                m_stat_memory.sub(symmem + connectionmem);
            }
        }

        public void optimize_group(ShaderGroup group, ShadingContext ctx, bool do_jit)
        {
            if (ctx != null)
            {
                // Always have ShadingContext remember the group we just optimized to allow calls to find_symbol and get_symbol to be valid after
                // optimization without needed to execute or jit
                ctx.group(group);
            }
            if (group.optimized() != 0 && (!do_jit || group.jitted() != 0))
            {
                return;  // already optimized and optionally jitted
            }

            Timer timer = new Timer();
            lock (group.m_mutex)
            {
                bool need_jit = do_jit && group.jitted() == 0;
                if (group.optimized() != 0 && !need_jit)
                {
                    // The group was somehow optimized by another thread between the time we checked group.optimized() and now that we have the lock.
                    lock (m_stat_mutex)
                    {
                        double t = timer.elapsed();
                        m_stat_optimization_time += t;
                        m_stat_opt_locking_time += t;
                    }
                    return;
                }

                if (m_only_groupname.size() != 0 && m_only_groupname != group.name())
                {
                    // For debugging purposes, we are requested to compile only one shader group, and this is not it.
                    group.does_nothing(true);
                    group.m_optimized = 1;
                    group.m_jitted = 1;
                    lock (m_stat_mutex)
                    {
                        double t = timer.elapsed();
                        m_stat_optimization_time += t;
                        m_stat_opt_locking_time += t;
                    }
                    return;
                }

                double locking_time = timer.elapsed();

                bool ctx_allocated = false;
                PerThreadInfo thread_info = null;
                if (ctx == null)
                {
                    thread_info = create_thread_info();
                    ctx = get_context(thread_info);
                    ctx_allocated = true;
                }
                if (group.optimized() == 0)
                {
                    RuntimeOptimizer rop = new RuntimeOptimizer(this, group, ctx);
                    ShadingContext previous_opt_ctx = ShadingContext.t_opt_ctx;
                    ShadingContext.t_opt_ctx = ctx;  // for runtime services called while folding constants that have no ShadingContext argument
                    try
                    {
                        rop.run();
                    }
                    finally
                    {
                        ShadingContext.t_opt_ctx = previous_opt_ctx;
                    }
                    rop.police_failed_optimizations();

                    // Copy some info recorded by the RuntimeOptimizer into the group
                    group.m_unknown_textures_needed = rop.m_unknown_textures_needed;
                    foreach (ustring f in rop.m_textures_needed)
                    {
                        group.m_textures_needed.Add(f);
                    }
                    group.m_unknown_closures_needed = rop.m_unknown_closures_needed;
                    foreach (ustring f in rop.m_closures_needed)
                    {
                        group.m_closures_needed.Add(f);
                    }
                    foreach (ustring f in rop.m_globals_needed)
                    {
                        group.m_globals_needed.Add(f);
                    }
                    group.m_globals_read = rop.m_globals_read;
                    group.m_globals_write = rop.m_globals_write;
                    int num_userdata = rop.m_userdata_needed.Count;
                    for (int i = 0; i < num_userdata; ++i)
                    {
                        group.m_userdata_offsets.Add(0);
                    }
                    foreach (UserDataNeeded n in rop.m_userdata_needed)
                    {
                        group.m_userdata_names.Add(n.name);
                        group.m_userdata_types.Add(n.type);
                        group.m_userdata_derivs.Add((byte)(n.derivs ? 1 : 0));
                        group.m_userdata_layers.Add(n.layer_num);
                        group.m_userdata_init_vals.Add(n.data);
                    }
                    group.m_unknown_attributes_needed = rop.m_unknown_attributes_needed;
                    foreach (AttributeNeeded f in rop.m_attributes_needed)
                    {
                        group.m_attributes_needed.Add(f.name);
                        group.m_attribute_scopes.Add(f.scope);
                        group.m_attribute_types.Add(f.type);
                        group.m_attribute_derivs.Add((byte)(f.derivs ? 1 : 0));
                    }
                    group.m_optimized = 1;

                    if (use_optix_cache())
                    {
                        group.generate_optix_cache_key(rop.serialize());
                    }

                    lock (m_stat_mutex)
                    {
                        if (!need_jit)
                        {
                            m_stat_opt_locking_time += locking_time;
                            m_stat_optimization_time += timer.elapsed();
                        }
                        m_stat_opt_locking_time += rop.m_stat_opt_locking_time;
                        m_stat_opt_locking_time += locking_time + rop.m_stat_opt_locking_time;
                        m_stat_specialization_time += rop.m_stat_specialization_time;
                    }
                }

                if (need_jit)
                {
                    bool ok = true;
                    if (ilgen_compile_group != null)
                    {
                        // The IL backend plays the role of BackendLLVM here
                        ok = ilgen_compile_group(group, ctx);
                    }
                    else
                    {
                        ok = false;
                    }
                    if (ok)
                    {
                        group_post_jit_cleanup(group);
                        group.m_jitted = 1;
                        lock (m_stat_mutex)
                        {
                            m_stat_opt_locking_time += locking_time;
                            m_stat_optimization_time += timer.elapsed();
                        }
                    }
                }

                if (ctx_allocated)
                {
                    release_context(ctx);
                    destroy_thread_info(thread_info);
                }

                Interlocked.Increment(ref m_stat_groups_compiled);
                Interlocked.Add(ref m_stat_instances_compiled, group.nlayers());
                Interlocked.Decrement(ref m_groups_to_compile_count);
            }
        }

        public void optimize_all_groups(int nthreads, int mythread, int totalthreads, bool do_jit)
        {
            // Spawn a bunch of threads to do this in parallel -- just call this routine again (with threads=1) for each thread.
            if (nthreads < 1)  // threads <= 0 means use all hardware available
            {
                nthreads = Math.Min(Environment.ProcessorCount, m_groups_to_compile_count);
            }
            if (nthreads > 1)
            {
                if (m_threads_currently_compiling != 0)
                {
                    return;  // never mind, somebody else spawned the JIT threads
                }
                List<Thread> threads = new List<Thread>();
                m_threads_currently_compiling += nthreads;
                for (int t = 0; t < nthreads; ++t)
                {
                    int tt = t;
                    int nn = nthreads;
                    Thread th = new Thread(() => optimize_all_groups(1, tt, nn, do_jit));
                    threads.Add(th);
                    th.Start();
                }
                foreach (Thread th in threads)
                {
                    th.Join();
                }
                m_threads_currently_compiling -= nthreads;
                return;
            }

            // And here's the single thread case
            int ngroups = 0;
            lock (m_all_shader_groups_mutex)
            {
                ngroups = m_all_shader_groups.Count;
            }
            PerThreadInfo threadinfo = create_thread_info();
            ShadingContext ctx = get_context(threadinfo);
            for (int i = 0; i < ngroups; ++i)
            {
                // Assign to threads based on mod of totalthreads
                if ((i % totalthreads) == mythread)
                {
                    ShaderGroup group = null;
                    lock (m_all_shader_groups_mutex)
                    {
                        m_all_shader_groups[i].TryGetTarget(out group);
                    }
                    if (group != null && group.m_complete)
                    {
                        optimize_group(group, ctx, do_jit);
                    }
                }
            }
            release_context(ctx);
            destroy_thread_info(threadinfo);
        }

        public void optimize_all_groups(int nthreads = 0, bool do_jit = true) { optimize_all_groups(nthreads, 0, 1, do_jit); }

        public int merge_instances(ShaderGroup group, bool post_opt = false)
        {
            // Look through the shader group for pairs of nodes/layers that actually do exactly the same thing, and eliminate one of the redundant shaders,
            // carefully rewiring all its outgoing connections to later layers to refer to the one we keep.
            if (m_opt_merge_instances == 0 || optimize() < 1)
            {
                return 0;
            }

            Timer timer = new Timer();   // Time we spend looking for and doing merges
            int merges = 0;              // number of merges we do
            long connectionmem = 0;      // Connection memory we free
            int nlayers = group.nlayers();

            // Need to quickly make sure userdata_params is up to date before any mergeability tests.
            for (int layer = 0; layer < nlayers; ++layer)
            {
                if (!group[layer].unused())
                {
                    group[layer].evaluate_writes_globals_and_userdata_params();
                }
            }

            // Loop over all layers...
            for (int a = 0; a < nlayers - 1; ++a)
            {
                if (group[a].unused() || group[a].entry_layer())  // Don't merge a layer that's not used or if it's an entry layer
                {
                    continue;
                }
                // Check all later layers...
                for (int b = a + 1; b < nlayers; ++b)
                {
                    if (group[b].unused())  // Don't merge a layer that's not used
                    {
                        continue;
                    }
                    if (b == nlayers - 1)  // Don't merge the last layer -- causes many tears because it's the group entry
                    {
                        continue;
                    }

                    // Now we have two used layers, a and b, to examine. See if they are mergeable (identical).
                    if (!group[a].mergeable(group[b], group))
                    {
                        continue;
                    }

                    // The two nodes a and b are mergeable, so merge them.
                    ShaderInstance A = group[a];
                    ShaderInstance B = group[b];
                    ++merges;

                    // We'll keep A, get rid of B. For all layers later than B, check its incoming connections and replace all references to B with
                    // references to A.
                    for (int j = b + 1; j < nlayers; ++j)
                    {
                        ShaderInstance inst = group[j];
                        if (inst.unused())  // don't bother if it's unused
                        {
                            continue;
                        }
                        for (int c = 0, ce = inst.nconnections(); c < ce; ++c)
                        {
                            Connection con = inst.m_connections[c];
                            if (con.srclayer == b)
                            {
                                con.srclayer = a;
                                inst.m_connections[c] = con;
                                A.outgoing_connections(true);
                            }
                        }
                    }

                    // Mark parameters of B as no longer connected
                    for (int p = B.firstparam(); p < B.lastparam(); ++p)
                    {
                        if (B.symbols().Count != 0)
                        {
                            B.symbol(p).connected_down(false);
                        }
                        if (B.m_instoverrides.Length != 0)
                        {
                            B.instoverride_ref(p).connected_down(false);
                        }
                    }
                    // B won't be used, so mark it as having no outgoing connections and clear its incoming connections (which are no longer used).
                    B.outgoing_connections(false);
                    connectionmem += B.clear_connections();
                    B.m_merged_unused = true;
                }
            }

            lock (m_stat_mutex)
            {
                // Adjust stats
                m_stat_mem_inst_connections.sub(connectionmem);
                m_stat_mem_inst.sub(connectionmem);
                m_stat_memory.sub(connectionmem);
                if (post_opt)
                {
                    m_stat_merged_inst_opt += merges;
                }
                else
                {
                    m_stat_merged_inst += merges;
                }
                m_stat_inst_merge_time += timer.elapsed();
            }
            return merges;
        }

        public bool archive_shadergroup(ShaderGroup group, string filename)
        {
            string extension = "";
            string fn = filename;
            for (string e = Path.GetExtension(fn); e.Length != 0 && fn.Length != 0; e = Path.GetExtension(fn))
            {
                extension = e + extension;
                fn = fn.Substring(0, fn.Length - e.Length);
            }
            if (extension.Length < 2 || extension[0] != '.')
            {
                errorfmt("archive_shadergroup: invalid filename \"{}\"", fn);
                return false;
            }

            string tmpdir = Path.Combine(Path.GetTempPath(), "OSL-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string errmessage;
            if (!Filesystem.create_directory(tmpdir, out errmessage))
            {
                errorfmt("archive_shadergroup: Could not create temp directory {} ({})", tmpdir, errmessage);
                return false;
            }

            bool ok = true;
            try
            {
                File.WriteAllText(Path.Combine(tmpdir, "shadergroup"), group.serialize());
            }
            catch (IOException)
            {
                error("archive_shadergroup: Could not open shadergroup file");
                ok = false;
            }

            lock (group.m_mutex)
            {
                HashSet<string> entries = new HashSet<string>();  // to avoid duplicates
                for (int i = 0, nl = group.nlayers(); i < nl; ++i)
                {
                    string osofile = group[i].master().osofilename();
                    string osoname = Path.GetFileName(osofile);
                    if (!entries.Contains(osoname))
                    {
                        entries.Add(osoname);
                        try
                        {
                            File.Copy(osofile, Path.Combine(tmpdir, osoname), true);
                        }
                        catch (IOException)
                        {
                        }
                    }
                }
            }

            string full_filename = Strutil.escape_chars(fn) + extension;
            try
            {
                if (extension == ".tar")
                {
                    System.Formats.Tar.TarFile.CreateFromDirectory(tmpdir, full_filename, false);
                }
                else if (extension == ".tar.gz" || extension == ".tgz")
                {
                    using (FileStream fs = File.Create(full_filename))
                    using (GZipStream gz = new GZipStream(fs, CompressionLevel.Optimal))
                    {
                        System.Formats.Tar.TarFile.CreateFromDirectory(tmpdir, gz, false);
                    }
                }
                else if (extension == ".zip")
                {
                    if (File.Exists(full_filename))
                    {
                        File.Delete(full_filename);
                    }
                    ZipFile.CreateFromDirectory(tmpdir, full_filename);
                }
                else
                {
                    error("archive_shadergroup: no archiving/compressing command");
                    ok = false;
                }
            }
            catch (Exception)
            {
                error(extension == ".zip" ? "archive_shadergroup: executing zip command failed" : "archive_shadergroup: executing tar command failed");
                ok = false;
            }

            try
            {
                Directory.Delete(tmpdir, true);
            }
            catch (IOException)
            {
            }
            return ok;
        }

        public static string globals_name(SGBits bit)
        {
            foreach (KeyValuePair<string, SGBits> t in sgbit_table)
            {
                if (bit == t.Value)
                {
                    return t.Key;
                }
            }
            return "";
        }

        public static SGBits globals_bit(string name)
        {
            foreach (KeyValuePair<string, SGBits> t in sgbit_table)
            {
                if (name == t.Key)
                {
                    return t.Value;
                }
            }
            return SGBits.None;
        }

        public static readonly KeyValuePair<string, SGBits>[] sgbit_table =
        {
            new KeyValuePair<string, SGBits>("P", SGBits.P), new KeyValuePair<string, SGBits>("I", SGBits.I), new KeyValuePair<string, SGBits>("N", SGBits.N),
            new KeyValuePair<string, SGBits>("Ng", SGBits.Ng), new KeyValuePair<string, SGBits>("u", SGBits.u), new KeyValuePair<string, SGBits>("v", SGBits.v),
            new KeyValuePair<string, SGBits>("dPdu", SGBits.dPdu), new KeyValuePair<string, SGBits>("dPdv", SGBits.dPdv),
            new KeyValuePair<string, SGBits>("time", SGBits.time), new KeyValuePair<string, SGBits>("dtime", SGBits.dtime),
            new KeyValuePair<string, SGBits>("dPdtime", SGBits.dPdtime), new KeyValuePair<string, SGBits>("Ps", SGBits.Ps),
            new KeyValuePair<string, SGBits>("Ci", SGBits.Ci),
        };
    }
}
