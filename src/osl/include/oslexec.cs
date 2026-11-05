// OSL port: include/OSL/oslexec.h (the public ShadingSystem interface)

using System;
using System.Collections.Generic;
using OIIO;
using OSL.pvt;

namespace OSL
{
    /// Hints given with Parameter() (ShadingSystem::ParamHints)
    [Flags]
    public enum ParamHints : int
    {
        none = 0,
        interpolated = 1,  // Interpolated across the surface (was lockgeom=0)
        interactive = 2,   // Value may be changed interactively (ReParameter)
    }

    public static class ParamHintsOps
    {
        /// OSL::set(hints, flag, on): set or clear a flag
        public static ParamHints set(ParamHints hints, ParamHints flag, bool on)
        {
            return on ? (hints | flag) : (hints & ~flag);
        }
    }

    public static class OslVersion
    {
        public const string OSL_LIBRARY_VERSION_STRING = "1.16.0.0dev";
    }

    /// Opaque handle to a symbol found in an optimized group (ShaderSymbol is a Symbol underneath, as in the C++)
    public sealed class ShaderSymbol
    {
        public readonly Symbol symbol;
        public ShaderSymbol(Symbol s) { symbol = s; }
    }

    /// The public face of the shading system (OSL::ShadingSystem). It owns a ShadingSystemImpl and forwards to it.
    public sealed class ShadingSystem : IDisposable
    {
        public readonly ShadingSystemImpl m_impl;

        public ShadingSystem(RendererServices renderer = null, TextureSystem texturesystem = null, ErrorHandler err = null)
        {
            m_impl = new ShadingSystemImpl(renderer, texturesystem, err);
        }

        public void Dispose() { m_impl.Dispose(); }

        public bool attribute(string name, TypeDesc type, object val) { return m_impl.attribute(name, type, val); }
        public bool attribute(string name, int val) { return m_impl.attribute(name, val); }
        public bool attribute(string name, float val) { return m_impl.attribute(name, val); }
        public bool attribute(string name, string val) { return m_impl.attribute(name, val); }
        public bool attribute(ShaderGroup group, string name, TypeDesc type, object val) { return m_impl.attribute(group, name, type, val); }
        public bool attribute(ShaderGroup group, string name, int val) { return m_impl.attribute(group, name, TypeDescs.TypeInt, val); }
        public bool attribute(ShaderGroup group, string name, string val) { return m_impl.attribute(group, name, TypeDescs.TypeString, val); }
        public bool getattribute(string name, TypeDesc type, out object val) { return m_impl.getattribute(name, type, out val); }
        public bool getattribute(string name, out int val) { return m_impl.getattribute(name, out val); }
        public bool getattribute(string name, out string val) { return m_impl.getattribute(name, out val); }
        public bool getattribute(ShaderGroup group, string name, TypeDesc type, out object val) { return m_impl.getattribute(group, name, type, out val); }

        public bool LoadMemoryCompiledShader(string shadername, string buffer) { return m_impl.LoadMemoryCompiledShader(shadername, buffer); }

        public ShaderGroup ShaderGroupBegin(string groupname = "") { return m_impl.ShaderGroupBegin(groupname); }
        public ShaderGroup ShaderGroupBegin(string groupname, string usage, string groupspec) { return m_impl.ShaderGroupBegin(groupname, usage, groupspec); }
        public bool ShaderGroupEnd(ShaderGroup group) { return m_impl.ShaderGroupEnd(group); }
        public bool ShaderGroupEnd() { return m_impl.ShaderGroupEnd(); }
        public bool Parameter(ShaderGroup group, string name, TypeDesc t, object val, ParamHints hints = ParamHints.none)
        {
            return m_impl.Parameter(group, name, t, val, hints);
        }
        public bool Parameter(string name, TypeDesc t, object val, ParamHints hints = ParamHints.none) { return m_impl.Parameter(name, t, val, hints); }
        public bool Parameter(ShaderGroup group, string name, int val) { return m_impl.Parameter(group, name, TypeDescs.TypeInt, new int[] { val }); }
        public bool Parameter(ShaderGroup group, string name, float val) { return m_impl.Parameter(group, name, TypeDescs.TypeFloat, new float[] { val }); }
        public bool Parameter(ShaderGroup group, string name, string val) { return m_impl.Parameter(group, name, TypeDescs.TypeString, new string[] { val }); }
        public bool Shader(ShaderGroup group, string shaderusage, string shadername, string layername = "")
        {
            return m_impl.Shader(group, shaderusage, shadername, layername);
        }
        public bool Shader(string shaderusage, string shadername, string layername = "") { return m_impl.Shader(shaderusage, shadername, layername); }
        public bool ConnectShaders(ShaderGroup group, string srclayer, string srcparam, string dstlayer, string dstparam)
        {
            return m_impl.ConnectShaders(group, srclayer, srcparam, dstlayer, dstparam);
        }
        public bool ConnectShaders(string srclayer, string srcparam, string dstlayer, string dstparam)
        {
            return m_impl.ConnectShaders(srclayer, srcparam, dstlayer, dstparam);
        }
        public bool ReParameter(ShaderGroup group, string layername, string paramname, TypeDesc type, object val)
        {
            return m_impl.ReParameter(group, layername, paramname, type, val);
        }

        public PerThreadInfo create_thread_info() { return m_impl.create_thread_info(); }
        public void destroy_thread_info(PerThreadInfo threadinfo) { m_impl.destroy_thread_info(threadinfo); }
        public ShadingContext get_context(PerThreadInfo threadinfo, TexturePerthread texture_threadinfo = null)
        {
            return m_impl.get_context(threadinfo, texture_threadinfo);
        }
        public void release_context(ShadingContext ctx) { m_impl.release_context(ctx); }

        public unsafe bool execute(ShadingContext ctx, ShaderGroup group, int thread_index, int shade_index, ref ShaderGlobals globals,
                                   void* userdata_base_ptr = null, void* output_base_ptr = null, bool run = true)
        {
            return ctx.execute(group, thread_index, shade_index, ref globals, userdata_base_ptr, output_base_ptr, run);
        }
        public unsafe bool execute(ShadingContext ctx, ShaderGroup group, ref ShaderGlobals globals, bool run = true)
        {
            return ctx.execute(group, 0, 0, ref globals, null, null, run);
        }
        public unsafe bool execute_init(ShadingContext ctx, ShaderGroup group, int thread_index, int shade_index, ref ShaderGlobals globals,
                                        void* userdata_base_ptr = null, void* output_base_ptr = null, bool run = true)
        {
            return ctx.execute_init(group, thread_index, shade_index, ref globals, userdata_base_ptr, output_base_ptr, run);
        }
        public unsafe bool execute_layer(ShadingContext ctx, int thread_index, int shade_index, ref ShaderGlobals globals, void* userdata_base_ptr,
                                         void* output_base_ptr, int layernumber)
        {
            return ctx.execute_layer(thread_index, shade_index, ref globals, userdata_base_ptr, output_base_ptr, layernumber);
        }
        public unsafe bool execute_layer(ShadingContext ctx, int thread_index, int shade_index, ref ShaderGlobals globals, void* userdata_base_ptr,
                                         void* output_base_ptr, ustring layername)
        {
            return ctx.execute_layer(thread_index, shade_index, ref globals, userdata_base_ptr, output_base_ptr, ctx.group().find_layer(layername));
        }
        public bool execute_cleanup(ShadingContext ctx) { return ctx.execute_cleanup(); }

        public int find_layer(ShaderGroup group, ustring layername) { return group.find_layer(layername); }

        /// Find a symbol of an optimized group by layer and name; null if not found or the group is not yet optimized
        public ShaderSymbol find_symbol(ShaderGroup group, ustring layername, ustring symbolname)
        {
            if (group.optimized() == 0)
            {
                return null;  // has to be post-optimized
            }
            Symbol s = group.find_symbol(layername, symbolname);
            return s != null ? new ShaderSymbol(s) : null;
        }

        /// "layer.symbol" (or just "symbol") form
        public ShaderSymbol find_symbol(ShaderGroup group, ustring symbolname)
        {
            ustring layername = new ustring("");
            string name = symbolname.@string();
            int dot = name.IndexOf('.');
            if (dot >= 0)
            {
                layername = new ustring(name.Substring(0, dot));
                symbolname = new ustring(name.Substring(dot + 1));
            }
            return find_symbol(group, layername, symbolname);
        }

        public TypeDesc symbol_typedesc(ShaderSymbol sym) { return sym != null ? sym.symbol.typespec().simpletype() : new TypeDesc(); }

        public unsafe void* symbol_address(ShadingContext ctx, ShaderSymbol sym, out object managed)
        {
            return ctx.symbol_data(sym.symbol, out managed);
        }

        public unsafe void* get_symbol(ShadingContext ctx, ustring layername, ustring symbolname, out TypeDesc type, out object managed)
        {
            ShaderSymbol sym = find_symbol(ctx.group(), layername, symbolname);
            managed = null;
            type = new TypeDesc();
            if (sym != null)
            {
                type = symbol_typedesc(sym);
                return symbol_address(ctx, sym, out managed);
            }
            return null;
        }

        public string getstats(int level = 1) { return m_impl.getstats(level); }

        public void register_closure(string name, int id, ClosureParam[] @params, PrepareClosureFunc prepare, SetupClosureFunc setup)
        {
            m_impl.register_closure(name, id, @params, prepare, setup);
        }
        public bool query_closure(ref string name, ref int id, out ClosureParam[] @params) { return m_impl.query_closure(ref name, ref id, out @params); }

        public static SGBits globals_bit(ustring name) { return ShadingSystemImpl.globals_bit(name.@string()); }
        public static ustring globals_name(SGBits bit) { return new ustring(ShadingSystemImpl.globals_name(bit)); }
        public int raytype_bit(ustring name) { return m_impl.raytype_bit(name); }
        public void optimize_all_groups(int nthreads = 0, bool do_jit = true) { m_impl.optimize_all_groups(nthreads, do_jit); }
        public TextureSystem texturesys() { return m_impl.texturesys(); }
        public RendererServices renderer() { return m_impl.renderer(); }
        public bool archive_shadergroup(ShaderGroup group, string filename) { return m_impl.archive_shadergroup(group, filename); }
        public void set_raytypes(ShaderGroup group, int raytypes_on, int raytypes_off) { group.set_raytypes(raytypes_on, raytypes_off); }
        public void clear_symlocs() { m_impl.clear_symlocs(); }
        public void clear_symlocs(ShaderGroup group) { group.clear_symlocs(); }
        public void add_symlocs(IEnumerable<SymLocationDesc> symlocs) { m_impl.add_symlocs(symlocs); }
        public void add_symlocs(ShaderGroup group, IEnumerable<SymLocationDesc> symlocs) { group.add_symlocs(symlocs); }
        public SymLocationDesc? find_symloc(ustring name) { return m_impl.find_symloc(name); }
        public SymLocationDesc? find_symloc(ShaderGroup group, ustring name) { return group.find_symloc(name); }
        public void optimize_group(ShaderGroup group, ShadingContext ctx, bool do_jit = true) { m_impl.optimize_group(group, ctx, do_jit); }
        public void register_inline_function(ustring name) { m_impl.register_inline_function(name); }
        public void unregister_inline_function(ustring name) { m_impl.unregister_inline_function(name); }
        public void register_noinline_function(ustring name) { m_impl.register_noinline_function(name); }
        public void unregister_noinline_function(ustring name) { m_impl.unregister_noinline_function(name); }

        // convert_value works on managed arrays: int, float, int[], float[]; returns the converted array
        public static unsafe bool convert_value(float[] dst, TypeDesc dsttype, float[] src, TypeDesc srctype)
        {
            TypeDesc FloatArray2 = new TypeDesc(TypeDesc.FLOAT, 2);
            TypeDesc FloatArray3 = new TypeDesc(TypeDesc.FLOAT, 3);
            TypeDesc FloatArray4 = new TypeDesc(TypeDesc.FLOAT, 4);
            if (PvtFunctions.equivalent(dsttype, srctype))
            {
                if (dst != null && src != null)
                {
                    Array.Copy(src, dst, dsttype.size() / 4);
                }
                return true;
            }
            if (srctype == TypeDescs.TypeFloat)
            {
                if (PvtFunctions.equivalent(dsttype, TypeDescs.TypePoint) || dsttype == FloatArray2 || dsttype == FloatArray4)
                {
                    if (dst != null && src != null)
                    {
                        for (int i = 0; i < dsttype.size() / 4; ++i)
                        {
                            dst[i] = src[0];
                        }
                    }
                    return true;
                }
                return false;
            }
            if ((srctype == FloatArray3 && PvtFunctions.equivalent(dsttype, TypeDescs.TypePoint)) || (dsttype == FloatArray3 && PvtFunctions.equivalent(srctype, TypeDescs.TypePoint))
                || (srctype == FloatArray4 && PvtFunctions.equivalent(dsttype, TypeDescs.TypeFloat4)) || (dsttype == FloatArray4 && PvtFunctions.equivalent(srctype, TypeDescs.TypeFloat4)))
            {
                if (dst != null && src != null)
                {
                    Array.Copy(src, dst, dsttype.size() / 4);
                }
                return true;
            }
            if (srctype == FloatArray2 && PvtFunctions.equivalent(dsttype, TypeDescs.TypePoint))
            {
                if (dst != null && src != null)
                {
                    dst[0] = src[0];
                    dst[1] = src[1];
                    dst[2] = 0.0f;
                }
                return true;
            }
            return false;
        }
    }
}
