// OSL port: liboslexec/context.cpp and the ShadingContext class of oslexec_pvt.h
//
// A ShadingContext holds what one thread needs while executing shader groups: the group-data heap, the closure pool, the message blackboard, the
// buffered errors, the dictionary. The batched execution (Batched<WidthT>) of the C++ is not ported.
//
// ShaderGlobals.context and .renderer are GCHandle pointers (see shaderglobals.cs): the context keeps a handle to itself, and the shading system one to
// its RendererServices, so that the compiled code and the rs_* entry points can find them from the ShaderGlobals pointer alone.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using OIIO;
using Timer = OIIO.Timer;
using OSL.pvt;

namespace OSL
{
    public sealed unsafe partial class ShadingContext : IDisposable
    {
        private static readonly object buffered_errors_mutex = new object();

        private ShadingSystemImpl m_shadingsys;
        private RendererServices m_renderer;
        private PerThreadInfo m_threadinfo;
        private TexturePerthread m_texture_thread_info;
        private ShaderGroup m_group;
        private byte* m_heap;
        private long m_heapsize;
        private Dictionary<ustring, Regex> m_regex_map = new Dictionary<ustring, Regex>();
        private MessageList m_messages = new MessageList();
        private BatchedMessageBuffer m_batched_messages_buffer = new BatchedMessageBuffer();
        private int m_max_warnings;
        private int m_stat_get_userdata_calls;
        private int m_stat_layers_executed;
        private long m_ticks;
        private SimplePool m_closure_pool = new SimplePool(20 * 1024);
        private SimplePool m_scratch_pool = new SimplePool(64 * 1024);
        private Dictionary m_dictionary;
        private IntPtr m_self_handle;
        private bool m_disposed;

        private struct ErrorItem
        {
            public int err_code;
            public string msgString;

            public ErrorItem(int err_code, string msg)
            {
                this.err_code = err_code;
                msgString = msg;
            }
        }

        private List<ErrorItem> m_buffered_errors = new List<ErrorItem>();
        private int batch_size_executed;

        public ShadingContext(ShadingSystemImpl shadingsys, PerThreadInfo threadinfo)
        {
            m_shadingsys = shadingsys;
            m_renderer = m_shadingsys.renderer();
            m_threadinfo = threadinfo;
            m_group = null;
            m_max_warnings = shadingsys.max_warnings_per_thread();
            m_dictionary = null;
            batch_size_executed = 0;
            lock (m_shadingsys.m_stat_mutex)
            {
                m_shadingsys.m_stat_contexts.incr();
            }
            m_texture_thread_info = null;
            m_self_handle = GCHandle.ToIntPtr(GCHandle.Alloc(this));
        }

        ~ShadingContext()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            if (disposing)
            {
                process_errors();
                lock (m_shadingsys.m_stat_mutex)
                {
                    m_shadingsys.m_stat_contexts.decr();
                }
                free_dict_resources();
                m_messages.Dispose();
                m_batched_messages_buffer.Dispose();
                m_closure_pool.Dispose();
                m_scratch_pool.Dispose();
            }
            if (m_heap != null)
            {
                NativeMemory.AlignedFree(m_heap);
                m_heap = null;
            }
            if (m_self_handle != IntPtr.Zero)
            {
                GCHandle.FromIntPtr(m_self_handle).Free();
                m_self_handle = IntPtr.Zero;
            }
        }

        public ShadingSystemImpl shadingsys() { return m_shadingsys; }
        public RendererServices renderer() { return m_renderer; }

        // JIT happens in a context that is "repurposed": its errors flushed, its texture thread info cleared
        private TexturePerthread repurposeForJit()
        {
            process_errors();
            TexturePerthread restore = texture_thread_info();
            texture_thread_info(null);
            return restore;
        }

        private void restoreFromJit(TexturePerthread restore_state)
        {
            process_errors();
            texture_thread_info(restore_state);
        }

        public bool execute_init(ShaderGroup sgroup, int threadindex, int shadeindex, ref ShaderGlobals ssg, void* userdata_base_ptr,
                                 void* output_base_ptr, bool run)
        {
            // Make sure init and cleanup are matched (as if the user had called execute_cleanup)
            if (m_group != null)
            {
                execute_cleanup();
            }
            batch_size_executed = 0;
            m_group = sgroup;
            m_ticks = 0;

            // Optimize if we haven't already
            if (sgroup.nlayers() != 0)
            {
                sgroup.start_running();
                if (sgroup.jitted() == 0)
                {
                    TexturePerthread restore_state = repurposeForJit();
                    shadingsys().optimize_group(sgroup, this, true);
                    if (shadingsys().m_greedyjit && shadingsys().m_groups_to_compile_count != 0)
                    {
                        // If we are greedily JITing, optimize/JIT everything now
                        shadingsys().optimize_all_groups();
                    }
                    restoreFromJit(restore_state);
                }
                if (sgroup.does_nothing())
                {
                    return false;
                }
            }
            else
            {
                // empty shader - nothing to do!
                return false;
            }

            int profile = shadingsys().m_profile;
            Timer timer = new Timer(profile != 0 ? Timer.StartNowVal.StartNow : Timer.StartNowVal.DontStartNow);

            // Allocate enough space on the heap
            long heap_size_needed = sgroup.llvm_groupdata_size();
            reserve_heap(heap_size_needed);
            // Zero out the heap memory we will need
            if (shadingsys().m_clearmemory)
            {
                NativeMemory.Clear(m_heap, (nuint)heap_size_needed);
            }

            // Clear the closure pool, message blackboard, scratch memory
            m_closure_pool.clear();
            m_messages.clear();
            m_scratch_pool.clear();
            clear_runtime_stats();

            if (run)
            {
                RunLLVMGroupFunc run_func = sgroup.llvm_compiled_init();
                if (run_func == null)
                {
                    return false;
                }
                // Set up the ShaderGlobals: the context and the renderer are handle pointers
                fixed (ShaderGlobals* pssg = &ssg)
                {
                    pssg->context = (void*)m_self_handle;
                    pssg->shadingStateUniform = (void*)shadingsys().shading_state_uniform_handle();
                    pssg->renderer = (void*)shadingsys().renderer_handle();
                    pssg->Ci = null;
                    pssg->thread_index = threadindex;
                    pssg->shade_index = shadeindex;
                    m_exec_sg = pssg;
                    t_exec_sg = pssg;
                    run_func(pssg, m_heap, userdata_base_ptr, output_base_ptr, shadeindex, sgroup.interactive_arena_ptr());
                    m_exec_sg = null;
                    t_exec_sg = null;
                }
            }

            if (profile != 0)
            {
                m_ticks += timer.ticks();
            }
            return true;
        }

        public bool execute_layer(int threadindex, int shadeindex, ref ShaderGlobals ssg, void* userdata_base_ptr, void* output_base_ptr, int layernumber)
        {
            if (group() == null || group().nlayers() == 0 || group().does_nothing())
            {
                return false;
            }
            int profile = shadingsys().m_profile;
            Timer timer = new Timer(profile != 0 ? Timer.StartNowVal.StartNow : Timer.StartNowVal.DontStartNow);

            RunLLVMGroupFunc run_func = group().llvm_compiled_layer(layernumber);
            if (run_func == null)
            {
                return false;
            }
            fixed (ShaderGlobals* pssg = &ssg)
            {
                m_exec_sg = pssg;
                    t_exec_sg = pssg;
                run_func(pssg, m_heap, userdata_base_ptr, output_base_ptr, shadeindex, group().interactive_arena_ptr());
                m_exec_sg = null;
                    t_exec_sg = null;
            }
            if (profile != 0)
            {
                m_ticks += timer.ticks();
            }
            return true;
        }

        public bool execute_cleanup()
        {
            if (group() == null)
            {
                errorfmt("execute_cleanup called again on a cleaned-up context");
                return false;
            }
            // Process any queued up error messages, warnings, printfs from shaders
            process_errors();

            if (shadingsys().m_profile != 0)
            {
                // Transfer runtime stats to the shadingsys
                record_runtime_stats();
                Interlocked.Add(ref shadingsys().m_stat_total_shading_time_ticks, m_ticks);
                Interlocked.Add(ref group().m_stat_total_shading_time_ticks, m_ticks);
            }
            return true;
        }

        public bool execute(ShaderGroup sgroup, int threadindex, int shadeindex, ref ShaderGlobals ssg, void* userdata_base_ptr, void* output_base_ptr,
                            bool run)
        {
            int n = sgroup.m_exec_repeat;
            Vec3 Psave = default(Vec3), Nsave = default(Vec3);  // for repeats
            bool repeat = (n > 1);
            if (repeat)
            {
                Psave = ssg.P;
                Nsave = ssg.N;
                if (!run)
                {
                    n = 1;
                }
            }
            bool result = true;
            while (true)
            {
                if (!execute_init(sgroup, threadindex, shadeindex, ref ssg, userdata_base_ptr, output_base_ptr, run))
                {
                    return false;
                }
                if (run && n != 0)
                {
                    execute_layer(threadindex, shadeindex, ref ssg, userdata_base_ptr, output_base_ptr, group().nlayers() - 1);
                }
                result = execute_cleanup();
                if (--n < 1)
                {
                    break;  // done
                }
                if (repeat)
                {
                    // for repeats, need to reset P, N for the next iteration
                    ssg.P = Psave;
                    ssg.N = Nsave;
                    ssg.Ci = null;
                }
            }
            return result;
        }

        // ----- closures -----

        public void* allocate_closure(int size, int alignment)
        {
            return m_closure_pool.alloc(size, alignment);
        }

        /// ClosureComponent with room for 'prim_size' bytes of parameters (the C++ shadeops do this arithmetic themselves)
        public ClosureComponent* closure_component_allot(int id, int prim_size, Vec3 w)
        {
            const int alignment = 16;
            ClosureComponent* comp = (ClosureComponent*)m_closure_pool.alloc(sizeof(ClosureComponent) + prim_size, alignment);
            comp->id = id;
            comp->w = w;
            return comp;
        }

        public ClosureMul* closure_mul_allot() { return (ClosureMul*)m_closure_pool.alloc(sizeof(ClosureMul), 8); }
        public ClosureAdd* closure_add_allot() { return (ClosureAdd*)m_closure_pool.alloc(sizeof(ClosureAdd), 8); }

        // ----- symbols -----

        public Symbol symbol(ustring layername, ustring symbolname)
        {
            return group().find_symbol(layername, symbolname);
        }

        public Symbol symbol(ustring symbolname) { return symbol(new ustring(""), symbolname); }

        /// The address of the data of a symbol after execution (in the group-data heap), null if it is not something that can be retrieved. For
        /// parameters that kept their default or instance value, the managed array of the value is returned through 'managed'.
        public void* symbol_data(Symbol sym, out object managed)
        {
            managed = null;
            ShaderGroup sgroup = group();
            if (sgroup.jitted() == 0)
            {
                return null;  // can't retrieve symbol if we didn't optimize & jit
            }
            if (sym.dataoffset() >= 0 && m_heapsize > sym.dataoffset())
            {
                return m_heap + sym.dataoffset();
            }
            if ((sym.symtype() == SymType.SymTypeParam || sym.symtype() == SymType.SymTypeOutputParam)
                && (sym.valuesource() == Symbol.ValueSource.DefaultVal || sym.valuesource() == Symbol.ValueSource.InstanceVal))
            {
                managed = sym.dataptr();
            }
            return null;  // not something we can retrieve
        }

        public Regex find_regex(ustring r)
        {
            Regex found;
            if (m_regex_map.TryGetValue(r, out found))
            {
                return found;
            }
            found = new Regex(EcmaRegex.translate(r.@string()), RegexOptions.CultureInvariant);
            m_regex_map[r] = found;
            lock (m_shadingsys.m_stat_mutex)
            {
                m_shadingsys.m_stat_regexes += 1;
            }
            return found;
        }

        public ShaderGroup group() { return m_group; }
        public void group(ShaderGroup grp) { m_group = grp; }
        public MessageList messages() { return m_messages; }
        public BatchedMessageBuffer batched_messages_buffer() { return m_batched_messages_buffer; }
        public PerThreadInfo thread_info() { return m_threadinfo; }

        public TexturePerthread texture_thread_info()
        {
            if (m_texture_thread_info == null)
            {
                m_texture_thread_info = shadingsys().texturesys().get_perthread_info();
            }
            return m_texture_thread_info;
        }

        public void texture_thread_info(TexturePerthread t) { m_texture_thread_info = t; }
        public void* alloc_scratch(int size, int align = 1) { return m_scratch_pool.alloc(size, align); }
        public void incr_layers_executed() { ++m_stat_layers_executed; }
        public void incr_get_userdata_calls() { ++m_stat_get_userdata_calls; }

        public void clear_runtime_stats()
        {
            m_stat_get_userdata_calls = 0;
            m_stat_layers_executed = 0;
        }

        public void record_runtime_stats()
        {
            Interlocked.Add(ref shadingsys().m_stat_get_userdata_calls, m_stat_get_userdata_calls);
            Interlocked.Add(ref shadingsys().m_stat_layers_executed, m_stat_layers_executed);
        }

        public bool allow_warnings()
        {
            if (m_max_warnings > 0)
            {
                m_max_warnings--;
                return true;
            }
            return false;
        }

        public void reserve_heap(long size)
        {
            if (size > m_heapsize)
            {
                if (m_heap != null)
                {
                    NativeMemory.AlignedFree(m_heap);
                }
                m_heap = (byte*)NativeMemory.AlignedAlloc((nuint)size, 64);
                m_heapsize = size;
            }
        }

        public byte* heap() { return m_heap; }
        public long heapsize() { return m_heapsize; }

        // ----- errors -----

        public void record_error(int code, string text)
        {
            m_buffered_errors.Add(new ErrorItem(code, text));
            if (!shadingsys().m_buffer_printf)
            {
                process_errors();
            }
        }

        public void process_errors()
        {
            int nerrors = m_buffered_errors.Count;
            if (nerrors == 0)
            {
                return;
            }
            lock (buffered_errors_mutex)
            {
                for (int i = 0; i < nerrors; ++i)
                {
                    ErrorItem error_item = m_buffered_errors[i];
                    switch ((ErrorCode)error_item.err_code)
                    {
                        case ErrorCode.EH_MESSAGE:
                        case ErrorCode.EH_DEBUG:
                            shadingsys().message(error_item.msgString);
                            break;
                        case ErrorCode.EH_INFO:
                            shadingsys().info(error_item.msgString);
                            break;
                        case ErrorCode.EH_WARNING:
                            shadingsys().warning(error_item.msgString);
                            break;
                        case ErrorCode.EH_ERROR:
                        case ErrorCode.EH_SEVERE:
                            shadingsys().error(error_item.msgString);
                            break;
                        default:
                            break;
                    }
                }
                m_buffered_errors.Clear();
            }
        }

        public void process_file_output()
        {
            // Only the batched execution buffers file output
        }

        private ShaderGlobals* m_exec_sg;

        /// The ShaderGlobals of the shader being executed on this thread, for runtime callbacks that don't get it passed in
        [ThreadStatic]
        public static ShaderGlobals* t_exec_sg;

        /// The context the optimizer on this thread is working with
        [ThreadStatic]
        public static ShadingContext t_opt_ctx;

        /// An error raised by the runtime while a shader executes (OSL::errorfmt(sg, ...) in the C++): it goes through the renderer, so that a
        /// renderer that journals its messages keeps them in order with the printf output. Outside of execution it is an ordinary context error.
        public void runtime_errorfmt(string fmt, params object[] args)
        {
            if (m_exec_sg != null)
            {
                ShaderGlobalsAccess.errorfmt(m_exec_sg, fmt, args);
            }
            else
            {
                errorfmt(fmt, args);
            }
        }

        public void errorfmt(string fmt, params object[] args) { record_error((int)ErrorCode.EH_ERROR, Fmt.format(fmt, args)); }
        public void warningfmt(string fmt, params object[] args) { record_error((int)ErrorCode.EH_WARNING, Fmt.format(fmt, args)); }
        public void infofmt(string fmt, params object[] args) { record_error((int)ErrorCode.EH_INFO, Fmt.format(fmt, args)); }
        public void messagefmt(string fmt, params object[] args) { record_error((int)ErrorCode.EH_MESSAGE, Fmt.format(fmt, args)); }

        // ----- attributes -----

        public bool osl_get_attribute(ShaderGlobals* sg, void* objdata, int dest_derivs, ulong obj_name, ulong attr_name, int array_lookup, int index,
                                      TypeDesc attr_type, void* attr_dest)
        {
            Timer timer = new Timer(Timer.StartNowVal.StartNow);
            bool ok;
            if (array_lookup != 0)
            {
                ok = renderer().get_array_attribute(sg, dest_derivs != 0, obj_name, attr_type, attr_name, index, attr_dest);
            }
            else
            {
                ok = renderer().get_attribute(sg, dest_derivs != 0, obj_name, attr_type, attr_name, attr_dest);
            }
            double time = timer.elapsed();
            lock (shadingsys().m_stat_mutex)
            {
                shadingsys().m_stat_getattribute_time += time;
                if (!ok)
                {
                    shadingsys().m_stat_getattribute_fail_time += time;
                }
            }
            Interlocked.Increment(ref shadingsys().m_stat_getattribute_calls);
            return ok;
        }

    }
}
