// (not in OpenShadingLanguage) backend_persist.cs: the second compile path of the IL backend -- a group is written as an assembly file (PersistedAssemblyBuilder) with
// one init entry and one entry per used layer, all with the RunLLVMGroupFunc signature, and a loader that reads the file back (Assembly.LoadFrom) and creates the
// delegates. This plays the role of the LLVM bitcode / precompiled-library workflows of the C++ (llvm_compile_to_bitcode, the cached ptx of the optix path).
//
// The group data layout (the symbol offsets and groupdata size) is not stored in the file: the loader repeats the layout pass of BackendIL.run on the same,
// optimized, group (set_layout_only) so the offsets agree with those the file was generated with. Hence a file only fits an identical group.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using OIIO;

namespace OSL.pvt
{
    public sealed unsafe partial class BackendIL
    {
        public const string PersistedTypeName = "OSLGroup";
        public const string PersistedStringsName = "osl_group_strings";

        /// Generates the assembly of an optimized group and saves it. Returns false (and reports the error) on failure.
        public static bool save_group_assembly(ShaderGroup group, ShadingContext ctx, string filename)
        {
            ShadingSystemImpl ss = ctx.shadingsys();
            ss.optimize_group(group, ctx, false);
            string asmname = Path.GetFileNameWithoutExtension(filename);
            PersistedAssemblyBuilder ab = new PersistedAssemblyBuilder(new AssemblyName(asmname), typeof(object).Assembly);
            ModuleBuilder mb = ab.DefineDynamicModule(asmname);
            TypeBuilder tb = mb.DefineType(PersistedTypeName, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class);
            PersistedILTarget target = new PersistedILTarget(tb);
            BackendIL il = new BackendIL(ss, group, ctx, target);
            try
            {
                il.run();
                if (!group.does_nothing())
                {
                    il.define_strings_function(tb);
                }
                tb.CreateType();
                ab.Save(filename);
            }
            catch (Exception e)
            {
                ss.errorfmt("IL assembly generation for group \"{}\" failed: {}", group.name(), e.ToString());
                return false;
            }
            return true;
        }

        /// "osl_group_strings()": registers the strings the code uses as hashes (the ustrings of the C++ live in the process-wide table; here a separate process
        /// that loads the file has not seen them)
        private void define_strings_function(TypeBuilder tb)
        {
            MethodBuilder m = tb.DefineMethod(PersistedStringsName, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            ILGenerator g = m.GetILGenerator();
            MethodInfo reg = typeof(ILRuntimeTable).GetMethod("register_string", BindingFlags.Public | BindingFlags.Static);
            foreach (KeyValuePair<ulong, string> kv in ll.string_constants)
            {
                g.Emit(OpCodes.Ldstr, kv.Value);
                g.Emit(OpCodes.Call, reg);
            }
            g.Emit(OpCodes.Ret);
        }

        /// Loads the file saved for an identical group, installs the entry points in the group and marks it compiled (so execute does not compile it again)
        public static bool load_group_assembly(ShaderGroup group, ShadingContext ctx, string filename)
        {
            ShadingSystemImpl ss = ctx.shadingsys();
            ss.optimize_group(group, ctx, false);
            lock (group.m_mutex)
            {
                if (group.jitted() != 0)
                {
                    return true;
                }
                try
                {
                    BackendIL il;
                    if (group.does_nothing())
                    {
                        DynamicILTarget empty = new DynamicILTarget();
                        il = new BackendIL(ss, group, ctx, empty);
                        il.run();
                        empty.Dispose();
                        group.m_jitted = 1;
                        return true;
                    }
                    // Layout only: the offsets of the symbols in the group data
                    il = new BackendIL(ss, group, ctx, new DynamicILTarget());
                    il.set_layout_only(true);
                    il.run();

                    Assembly asm = Assembly.LoadFrom(Path.GetFullPath(filename));
                    Type t = asm.GetType(PersistedTypeName, true);
                    MethodInfo strings = t.GetMethod(PersistedStringsName, BindingFlags.Public | BindingFlags.Static);
                    if (strings != null)
                    {
                        strings.Invoke(null, null);
                    }
                    MethodInfo init = t.GetMethod(LayerNames.init_function_name(ss, group), BindingFlags.Public | BindingFlags.Static);
                    if (init == null)
                    {
                        throw new InvalidOperationException("no init function for group " + group.name() + " in " + filename);
                    }
                    group.llvm_compiled_init((RunLLVMGroupFunc)init.CreateDelegate(typeof(RunLLVMGroupFunc)));
                    int nlayers = group.nlayers();
                    for (int layer = 0; layer < nlayers; ++layer)
                    {
                        MethodInfo m = t.GetMethod(LayerNames.layer_function_name(group, group[layer]), BindingFlags.Public | BindingFlags.Static);
                        if (m != null && group.is_entry_layer(layer))
                        {
                            group.llvm_compiled_layer(layer, (RunLLVMGroupFunc)m.CreateDelegate(typeof(RunLLVMGroupFunc)));
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
                    group.m_jitted = 1;
                    return true;
                }
                catch (Exception e)
                {
                    ss.errorfmt("loading IL assembly {} for group \"{}\" failed: {}", filename, group.name(), e.ToString());
                    return false;
                }
            }
        }
    }
}
