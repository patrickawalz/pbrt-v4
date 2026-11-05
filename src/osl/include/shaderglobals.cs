// OSL port: include/OSL/shaderglobals.h
//
// ShaderGlobals is the state describing a point to be shaded. It is passed to the compiled shader by pointer, so the structure is unmanaged and its
// layout is that of the C++ (312 bytes on a 64-bit platform): the fields are the same, in the same order. The two fields that hold C++ objects
// (the ShadingContext and the RendererServices) hold GCHandle pointers here (ShaderGlobalsAccess resolves them); the renderer's own pointers
// (renderstate, tracedata, objdata and the transformation pointers) are opaque to OSL, as in the C++.

using System;
using System.Runtime.InteropServices;
using OIIO;

namespace OSL
{
    /// Bits for which "global" variables a shader needs or writes
    [Flags]
    public enum SGBits
    {
        None = 0,
        P = 1 << 0,
        I = 1 << 1,
        N = 1 << 2,
        Ng = 1 << 3,
        u = 1 << 4,
        v = 1 << 5,
        dPdu = 1 << 6,
        dPdv = 1 << 7,
        time = 1 << 8,
        dtime = 1 << 9,
        dPdtime = 1 << 10,
        Ps = 1 << 11,
        Ci = 1 << 12,
        last = 1 << 13
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ShaderGlobals
    {
        /// Surface position (and its x & y differentials).
        public Vec3 P, dPdx, dPdy;
        /// P's z differential, used for volume shading only.
        public Vec3 dPdz;
        /// Incident ray, and its x and y derivatives.
        public Vec3 I, dIdx, dIdy;
        /// Shading normal, already front-facing.
        public Vec3 N;
        /// True geometric normal.
        public Vec3 Ng;
        /// 2D surface parameter u, and its differentials.
        public float u, dudx, dudy;
        /// 2D surface parameter v, and its differentials.
        public float v, dvdx, dvdy;
        /// Surface tangents: derivative of P with respect to surface u and v.
        public Vec3 dPdu, dPdv;
        /// Time for this shading sample.
        public float time;
        /// Time interval for the frame (or shading sample).
        public float dtime;
        /// Velocity vector: derivative of position P with respect to time.
        public Vec3 dPdtime;
        /// For lights or light attenuation shaders: the point being illuminated (Ps), and its differentials.
        public Vec3 Ps, dPsdx, dPsdy;
        /// Opaque pointers the renderer sets for later retrieval inside its RendererServices methods.
        public void* renderstate;
        public void* tracedata;
        public void* objdata;
        /// Back-pointer to the ShadingContext (set and used by OSL itself): a GCHandle pointer, see ShaderGlobalsAccess.
        public void* context;
        public void* shadingStateUniform;
        /// 0 based thread index, required by the journaling mechanism; set by the shading system
        public int thread_index;
        /// Set by the shading system
        public int shade_index;
        /// The RendererServices, for callbacks to the renderer: a GCHandle pointer, see ShaderGlobalsAccess.
        public void* renderer;
        /// Opaque pointers for the renderer's get_matrix / get_inverse_matrix: the object->common and shader->common transformations.
        public void* object2common;
        public void* shader2common;
        /// The output closure is placed here. The renderer should initialize this to NULL before shading execution.
        public ClosureColor* Ci;
        /// Surface area of the emissive object (used by light shaders for energy normalization).
        public float surfacearea;
        /// Bit field of ray type flags.
        public int raytype;
        /// If nonzero, will flip the result of calculatenormal().
        public int flipHandedness;
        /// If nonzero, we are shading the back side of a surface.
        public int backfacing;
    }

    /// Resolution of the object handles kept in a ShaderGlobals, and the accessor functions of shaderglobals.h
    public static unsafe class ShaderGlobalsAccess
    {
        /// A pointer that stands for a managed object (what the unmanaged ShaderGlobals holds)
        public static void* make_handle(object o)
        {
            return (void*)GCHandle.ToIntPtr(GCHandle.Alloc(o, GCHandleType.Normal));
        }

        public static void free_handle(void* h)
        {
            if (h != null)
            {
                GCHandle.FromIntPtr((IntPtr)h).Free();
            }
        }

        public static object from_handle(void* h)
        {
            return h == null ? null : GCHandle.FromIntPtr((IntPtr)h).Target;
        }

        public static ShadingContext get_context(ShaderGlobals* sg) { return (ShadingContext)from_handle(sg->context); }

        /// OSL::errorfmt(sg, ...): the error goes to the renderer (RendererServices::errorfmt), which decides where it ends up
        public static void errorfmt(ShaderGlobals* sg, string fmt, params object[] args)
        {
            string message = OIIO.Fmt.format(fmt, args);
            EncodedType type = EncodedType.kUstringHash;
            ulong value = new OIIO.ustring(message).hash();
            ((RendererServices)from_handle(sg->renderer)).errorfmt(sg, new OIIO.ustring("{}").hash(), 1, &type, 8, (byte*)&value);
        }

        /// OSL::warningfmt(sg, ...)
        public static void warningfmt(ShaderGlobals* sg, string fmt, params object[] args)
        {
            string message = OIIO.Fmt.format(fmt, args);
            EncodedType type = EncodedType.kUstringHash;
            ulong value = new OIIO.ustring(message).hash();
            ((RendererServices)from_handle(sg->renderer)).warningfmt(sg, new OIIO.ustring("{}").hash(), 1, &type, 8, (byte*)&value);
        }
        public static RendererServices get_renderer(ShaderGlobals* sg) { return (RendererServices)from_handle(sg->renderer); }

        // The accessor functions of shaderglobals.h, taking the opaque execution context pointer (which is the ShaderGlobals)
        public static ShaderGlobals* get_ec(void* oec) { return (ShaderGlobals*)oec; }
        public static ClosureColor* get_Ci(void* oec) { return ((ShaderGlobals*)oec)->Ci; }
        public static void* get_object2common(void* oec) { return ((ShaderGlobals*)oec)->object2common; }
        public static void* get_shader2common(void* oec) { return ((ShaderGlobals*)oec)->shader2common; }
        public static int get_shade_index(void* oec) { return ((ShaderGlobals*)oec)->shade_index; }
        public static int get_thread_index(void* oec) { return ((ShaderGlobals*)oec)->thread_index; }
        public static Vec3 get_P(void* oec) { return ((ShaderGlobals*)oec)->P; }
        public static Vec3 get_dPdx(void* oec) { return ((ShaderGlobals*)oec)->dPdx; }
        public static Vec3 get_dPdy(void* oec) { return ((ShaderGlobals*)oec)->dPdy; }
        public static Vec3 get_dPdz(void* oec) { return ((ShaderGlobals*)oec)->dPdz; }
        public static Vec3 get_I(void* oec) { return ((ShaderGlobals*)oec)->I; }
        public static Vec3 get_dIdx(void* oec) { return ((ShaderGlobals*)oec)->dIdx; }
        public static Vec3 get_dIdy(void* oec) { return ((ShaderGlobals*)oec)->dIdy; }
        public static Vec3 get_N(void* oec) { return ((ShaderGlobals*)oec)->N; }
        public static Vec3 get_Ng(void* oec) { return ((ShaderGlobals*)oec)->Ng; }
        public static float get_u(void* oec) { return ((ShaderGlobals*)oec)->u; }
        public static float get_dudx(void* oec) { return ((ShaderGlobals*)oec)->dudx; }
        public static float get_dudy(void* oec) { return ((ShaderGlobals*)oec)->dudy; }
        public static float get_v(void* oec) { return ((ShaderGlobals*)oec)->v; }
        public static float get_dvdx(void* oec) { return ((ShaderGlobals*)oec)->dvdx; }
        public static float get_dvdy(void* oec) { return ((ShaderGlobals*)oec)->dvdy; }
        public static Vec3 get_dPdu(void* oec) { return ((ShaderGlobals*)oec)->dPdu; }
        public static Vec3 get_dPdv(void* oec) { return ((ShaderGlobals*)oec)->dPdv; }
        public static float get_time(void* oec) { return ((ShaderGlobals*)oec)->time; }
        public static float get_dtime(void* oec) { return ((ShaderGlobals*)oec)->dtime; }
        public static Vec3 get_dPdtime(void* oec) { return ((ShaderGlobals*)oec)->dPdtime; }
        public static Vec3 get_Ps(void* oec) { return ((ShaderGlobals*)oec)->Ps; }
        public static Vec3 get_dPsdx(void* oec) { return ((ShaderGlobals*)oec)->dPsdx; }
        public static Vec3 get_dPsdy(void* oec) { return ((ShaderGlobals*)oec)->dPsdy; }
        public static int get_backfacing(void* oec) { return ((ShaderGlobals*)oec)->backfacing; }
        public static int get_flipHandedness(void* oec) { return ((ShaderGlobals*)oec)->flipHandedness; }
        public static int get_raytype(void* oec) { return ((ShaderGlobals*)oec)->raytype; }
        public static float get_surfacearea(void* oec) { return ((ShaderGlobals*)oec)->surfacearea; }

        /// get_max_warnings_per_thread (opfmt.cpp): from the shading state uniform the ShaderGlobals points at
        public static int get_max_warnings_per_thread(void* oec)
        {
            ShaderGlobals* ec = (ShaderGlobals*)oec;
            return ((pvt.ShadingStateUniform)from_handle(ec->shadingStateUniform)).m_max_warnings_per_thread;
        }
    }
}
