// OSL port: include/OSL/oslconfig.h, encodedtypes.h and the small option structs of oslexec.h / rendererservices.h
//
// ustringhash is a ulong here (the hash of a ustring), as in the unmanaged ABI.

using System;
using System.Runtime.InteropServices;
using OIIO;

namespace OSL
{
    /// How the arguments of a printf-style call are encoded in the argument buffer (encodedtypes.h)
    public enum EncodedType : byte
    {
        kUstringHash = 0,
        kInt32,
        kFloat,
        kInt64,
        kDouble,
        kUInt32,
        kUInt64,
        kPointer,
        kTypeDesc,
        kCount
    }

    /// Options for the trace() call
    [StructLayout(LayoutKind.Sequential)]
    public struct TraceOpt
    {
        public float mindist;
        public float maxdist;
        public bool shade;
        public ulong traceset;

        public static TraceOpt make_default()
        {
            TraceOpt t = new TraceOpt();
            t.mindist = 0.0f;
            t.maxdist = 1.0e30f;
            t.shade = false;
            t.traceset = 0;
            return t;
        }
    }

    /// Options for the noise functions the renderer may override (RendererServices::NoiseOpt)
    public struct NoiseOpt
    {
        public int anisotropic;
        public int do_filter;
        public Vec3 direction;
        public float bandwidth;
        public float impulses;

        public static NoiseOpt make_default()
        {
            NoiseOpt n = new NoiseOpt();
            n.anisotropic = 0;
            n.do_filter = 1;
            n.direction = new Vec3(1.0f, 0.0f, 0.0f);
            n.bandwidth = 1.0f;
            n.impulses = 16.0f;
            return n;
        }
    }

    /// Describes where the renderer wants a shader symbol's final value stored (ShadingSystem::add_symlocs). 'name' is the name of the symbol,
    /// 'type' its type, 'offset' where it goes, relative to the base pointer given to execute. 'stride' is the number of bytes between values.
    public struct SymLocationDesc
    {
        public ustring name;
        public TypeDesc type;
        public bool derivs;
        public long offset;
        public long stride;
        public SymArena arena;
        public bool is_output;

        public SymLocationDesc(string name, TypeDesc type, bool derivs, SymArena arena, long offset, long stride = 0, bool is_output = false)
        {
            this.name = new ustring(name);
            this.type = type;
            this.derivs = derivs;
            this.arena = arena;
            this.offset = offset;
            this.stride = stride;
            this.is_output = is_output;
        }
    }

    /// Placeholders of the getter specification machinery of the renderer (build_attribute_getter and build_interpolated_getter): the CUDA/OptiX
    /// use of them is not ported, so the specs carry nothing.
    public class AttributeGetterSpec { }
    public class InterpolatedGetterSpec { }

    /// A transformation passed by the renderer to get_matrix: opaque to OSL
    public unsafe struct TransformationPtr
    {
        public void* ptr;
        public TransformationPtr(void* p) { ptr = p; }
        public bool isnull() { return ptr == null; }
    }
}
