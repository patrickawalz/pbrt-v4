// OSL port: the glue of BackendIL to the shading system -- registration of the op generators, the in-memory (DynamicMethod) compilation of a group
// (what ShadingSystemImpl::optimize_group does with BackendLLVM::run and the JIT), and a few BackendLLVM members that need the runtime glue types.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using OIIO;
using OSL;

namespace OSL.pvt
{
    public sealed unsafe partial class BackendIL
    {
        private static bool s_generators_registered;
        private static readonly object s_register_lock = new object();

        /// Register every il_gen_<name> of ILGen in the table the op descriptors are bound from (the C++ table is built in setup_op_descriptors)
        public static void register_generators()
        {
            lock (s_register_lock)
            {
                if (s_generators_registered)
                {
                    return;
                }
                s_generators_registered = true;
                foreach (MethodInfo mi in typeof(ILGen).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!mi.Name.StartsWith("il_gen_"))
                    {
                        continue;
                    }
                    Func<BackendIL, int, bool> f = (Func<BackendIL, int, bool>)Delegate.CreateDelegate(typeof(Func<BackendIL, int, bool>), mi);
                    OpILGenTable.generators[mi.Name.Substring(7)] = (object b, int opnum) => f((BackendIL)b, opnum);
                }
            }
        }

        public static void install_default_hooks(ShadingSystemImpl ss)
        {
            register_generators();
            ss.ilgen_compile_group = compile_group_default;
            ss.ilgen_release = release_group;
        }

        /// The state kept alive by a compiled group
        private sealed class CompiledState : IDisposable
        {
            public DynamicILTarget target;

            public void Dispose()
            {
                if (target != null)
                {
                    target.Dispose();
                    target = null;
                }
            }
        }

        /// group_post_jit_cleanup's hook: the C++ frees the LLVM module and the execution engine's IR; the compiled code (here the DynamicMethods and the
        /// data blobs of the DynamicILTarget, kept by the group) must stay alive for as long as the group, and is freed by the finalizer of the target.
        public static void release_group(ShaderGroup group)
        {
        }

        private static RunLLVMGroupFunc make_delegate(ILFunction f)
        {
            return (RunLLVMGroupFunc)((DynamicMethod)f.method).CreateDelegate(typeof(RunLLVMGroupFunc));
        }

        /// ShadingSystemImpl::optimize_group's "BackendLLVM lljitter(...); lljitter.run();" followed by getting the JIT'd entry points
        public static bool compile_group_default(ShaderGroup group, ShadingContext ctx)
        {
            DynamicILTarget target = new DynamicILTarget();
            BackendIL il = new BackendIL(ctx.shadingsys(), group, ctx, target);
            try
            {
                il.run();
            }
            catch (Exception e)
            {
                ctx.shadingsys().errorfmt("IL generation for group \"{}\" failed: {}", group.name(), e.ToString());
                target.Dispose();
                return false;
            }
            if (group.does_nothing())
            {
                target.Dispose();
                return true;
            }
            int nlayers = group.nlayers();
            ILFunction init = il.init_function();
            group.llvm_compiled_init(make_delegate(init));
            List<ILFunction> funcs = il.layer_functions();
            for (int layer = 0; layer < nlayers; ++layer)
            {
                ILFunction f = layer < funcs.Count ? funcs[layer] : null;
                if (f != null && group.is_entry_layer(layer))
                {
                    group.llvm_compiled_layer(layer, make_delegate(f));
                }
            }
            if (group.num_entry_layers() != 0)
            {
                group.llvm_compiled_version(null);
            }
            else
            {
                group.llvm_compiled_version(group.llvm_compiled_layer(nlayers - 1));
            }
            CompiledState st = new CompiledState();
            st.target = target;
            group.m_il_compiled_state = st;
            return true;
        }

        // --------------------------------------------------------------------------------------------------------------------------------------
        // Temporaries and handles
        // --------------------------------------------------------------------------------------------------------------------------------------

        public ILValue temp_texture_options_ptr()
        {
            if (m_il_temp_texture_options_ptr == null)
            {
                m_il_temp_texture_options_ptr = ll.op_alloca(sizeof(NativeTextureOpt), 16);
            }
            return m_il_temp_texture_options_ptr;
        }

        public ILValue temp_texture_options_void_ptr()
        {
            return ll.void_ptr(temp_texture_options_ptr());
        }

        public ILValue temp_trace_options_ptr()
        {
            if (m_il_temp_trace_options_ptr == null)
            {
                m_il_temp_trace_options_ptr = ll.op_alloca(sizeof(TraceOpt), 16);
            }
            return m_il_temp_trace_options_ptr;
        }

        public ILValue temp_trace_options_void_ptr()
        {
            return ll.void_ptr(temp_trace_options_ptr());
        }

        public ILValue temp_noise_options_ptr()
        {
            if (m_il_temp_noise_options_ptr == null)
            {
                m_il_temp_noise_options_ptr = ll.op_alloca(sizeof(NoiseParams), 16);
            }
            return m_il_temp_noise_options_ptr;
        }

        public ILValue temp_noise_options_void_ptr()
        {
            return ll.void_ptr(temp_noise_options_ptr());
        }

        /// The pointer-sized token of the TextureHandle of a constant filename (0 when the target cannot embed it; the runtime then looks the name up)
        public long il_texture_handle(ustring filename)
        {
            TextureHandle h = renderer().get_texture_handle(filename, shadingcontext(), null);
            if (h == null)
            {
                return 0;
            }
            return m_target.pin_object(h);
        }
    }
}
