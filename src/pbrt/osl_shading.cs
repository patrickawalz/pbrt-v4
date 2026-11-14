// (not in pbrt-v4) Open Shading Language integration: running a shader group at a point of the scene.
//
//  * OSLShaderGroup holds a built ShaderGroup with what the integration adds to it (the pbrt textures bound to parameters, the shader space).
//  * OSLThreadState is the per thread state of the shading system (the thread info, the shading context, the matrices that
//    ShaderGlobals points to).
//  * OSLShading fills a ShaderGlobals from a point of the scene, executes the group, and walks the closure tree it returns: the lobes become
//    BxDFs (osl_bxdfs.cs) in an OSLClosureBxDF, the emission closures a spectrum.
//
// Spaces: the shader works in world space ("common"); the point of the scene is in pbrt's render space and converted with OSLScene.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OIIO;
using OSL;

namespace pbrt
{
    /// A pbrt texture that gives the value of a shader parameter (the parameter is "interpolated": OSL asks the renderer for it at each point)
    public sealed class OSLTextureBinding
    {
        public string parameter;
        public FloatTexture floatTexture;
        public SpectrumTexture spectrumTexture;
    }

    /// The ray types of the shaders (raytype() of OSL)
    public static class OSLRayType
    {
        public static int Camera = 1, Shadow = 2, Diffuse = 4, Glossy = 8, Reflection = 16, Refraction = 32, Subsurface = 64;

        public static void Init(ShadingSystem ss)
        {
            Camera = ss.raytype_bit(new ustring("camera"));
            Shadow = ss.raytype_bit(new ustring("shadow"));
            Diffuse = ss.raytype_bit(new ustring("diffuse"));
            Glossy = ss.raytype_bit(new ustring("glossy"));
            Reflection = ss.raytype_bit(new ustring("reflection"));
            Refraction = ss.raytype_bit(new ustring("refraction"));
            Subsurface = ss.raytype_bit(new ustring("subsurface"));
        }

        /// The ray type bits of a ray that was scattered with a lobe of these flags
        public static int FromFlags(BxDFFlags flags)
        {
            int bits = 0;
            if (IsDiffuse(flags))
            {
                bits |= Diffuse;
            }
            if (IsGlossy(flags))
            {
                bits |= Glossy;
            }
            if (IsReflective(flags))
            {
                bits |= Reflection;
            }
            if (IsTransmissive(flags))
            {
                bits |= Refraction;
            }
            return bits;
        }
    }

    /// A shader group of a material or a texture
    public sealed unsafe class OSLShaderGroup
    {
        public ShaderGroup group;
        public string name;
        public Dictionary<ulong, OSLTextureBinding> bindings = new Dictionary<ulong, OSLTextureBinding>();
        /// shader space -> world (pbrt convention) and the same as the matrix that OSL multiplies row vectors with
        public Transform worldFromShader = new Transform();
        /// For a texture: the render space transformation of the texture's coordinate system (the world transformation is known only later)
        public Transform renderFromShader = new Transform();
        public bool hasRenderFromShader = false;
        public string[] outputCandidates = null;
        /// For a texture whose output OSL differentiates: the number of floats of the output (1 or 3) that the group writes, with its two
        /// derivatives after it, in the output arena. 0: the group writes nothing there.
        public int derivativeFloats = 0;
        private bool matrixValid = false;
        private Matrix44 shaderToCommon = Matrix44.identity;

        public Matrix44 ShaderToCommon()
        {
            if (!matrixValid)
            {
                shaderToCommon = OSLShading.RowMatrix(hasRenderFromShader ? OSLScene.worldFromRender * renderFromShader : worldFromShader);
                matrixValid = true;
            }
            return shaderToCommon;
        }
        /// The output that a texture reads
        public string outputName = "";
        public bool outputIsFloat = false;
        public bool needsSurfaceData = true;
        private ShaderSymbol outputSymbol = null;
        private readonly object symbolMutex = new object();

        public ShaderSymbol OutputSymbol(OSLThreadState ts)
        {
            if (outputSymbol == null)
            {
                lock (symbolMutex)
                {
                    if (outputSymbol == null)
                    {
                        ShadingSystem ss = OSLSystem.ShadingSystem;
                        outputSymbol = ss.find_symbol(group, new ustring(outputName));
                        if (outputSymbol == null && outputCandidates != null)
                        {
                            foreach (string candidate in outputCandidates)
                            {
                                outputSymbol = ss.find_symbol(group, new ustring(candidate));
                                if (outputSymbol != null)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            return outputSymbol;
        }
    }

    /// What is given to the callbacks of the renderer services of a shading (the renderstate of the ShaderGlobals)
    public sealed unsafe class OSLShadeState
    {
        public OSLShaderGroup group;
        public SurfaceInteraction si;
        public TextureEvalContext tex;
        public SampledWavelengths lambda;
        public OSLObjectInfo objectInfo;
        public int rayType;
        // the result of the last trace()
        public bool traceHit;
        public SurfaceInteraction traceInteraction;
        public Float traceDistance;
        public Vector3f traceDirection;
    }

    public sealed unsafe class OSLThreadState : IDisposable
    {
        [ThreadStatic] private static OSLThreadState current;

        public PerThreadInfo threadInfo;
        public ShadingContext context;
        public Matrix44* matrices;
        public GCHandle stateHandle;
        public OSLShadeState shade = new OSLShadeState();
        private bool disposed = false;

        private OSLThreadState()
        {
            ShadingSystem ss = OSLSystem.ShadingSystem;
            threadInfo = ss.create_thread_info();
            context = ss.get_context(threadInfo);
            matrices = (Matrix44*)Marshal.AllocHGlobal(sizeof(Matrix44) * 2);
            stateHandle = GCHandle.Alloc(shade);
        }

        ~OSLThreadState()
        {
            Dispose(false);
        }

        public static OSLThreadState Current()
        {
            if (current == null)
            {
                current = new OSLThreadState();
            }
            return current;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (matrices != null)
            {
                Marshal.FreeHGlobal((IntPtr)matrices);
                matrices = null;
            }
            if (stateHandle.IsAllocated)
            {
                stateHandle.Free();
            }
            if (disposing)
            {
                ShadingSystem ss = OSLSystem.ShadingSystem;
                ss.release_context(context);
                ss.destroy_thread_info(threadInfo);
            }
        }
    }

    /// The result of the shading of a point
    public sealed class OSLShadeResult
    {
        public OSLClosureBxDF bxdf;
        public SampledSpectrum emission;
        public bool hasEmission;
        public bool holdout;
        /// Cache key: the point and the frame for which the lobes were made
        public SurfaceInteraction si;
        public Normal3f ns;
        public Vector3f dpdus;
        public SampledWavelengths lambda;
    }

    public static unsafe class OSLShading
    {
        [ThreadStatic] private static OSLShadeResult lastResult;

        /// The matrix of a transformation as OSL wants it (OSL multiplies row vectors: the transposed pbrt matrix)
        public static Matrix44 RowMatrix(Transform t)
        {
            SquareMatrix4 m = t.GetMatrix();
            Matrix44 r = new Matrix44();
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    r[i, j] = m[j][i];
                }
            }
            return r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vec3 ToVec3(Vector3f v)
        {
            return new Vec3(v.x, v.y, v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vec3 ToWorld(Vector3f v)
        {
            return ToVec3(OSLScene.WorldFromRender(v));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vec3 ToWorld(Point3f p)
        {
            Point3f w = OSLScene.WorldFromRender(p);
            return new Vec3(w.x, w.y, w.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vec3 ToWorld(Normal3f n)
        {
            Normal3f w = OSLScene.WorldFromRender(n);
            return new Vec3(w.x, w.y, w.z);
        }

        /// Fills the globals of a shader from a point of the scene. si may be null (a texture lookup that only has a TextureEvalContext).
        public static void FillGlobals(ref ShaderGlobals sg, OSLThreadState ts, OSLShaderGroup group, SurfaceInteraction si, TextureEvalContext tex,
                                       Vector3f wo, int rayType)
        {
            sg = new ShaderGlobals();
            Point3f p;
            Normal3f n, ns;
            Vector3f dpdu, dpdv, dpdx, dpdy;
            Point2f uv;
            Float dudx, dudy, dvdx, dvdy, time;
            int objectId;
            if (si != null)
            {
                p = si.p();
                n = si.n;
                ns = si.shading.n;
                dpdu = si.dpdu;
                dpdv = si.dpdv;
                dpdx = si.dpdx;
                dpdy = si.dpdy;
                uv = si.uv;
                if (object.ReferenceEquals(tex.si, si))
                {
                    // The point of the lookup: it is not the one of the intersection when a difference is taken
                    p = tex.p;
                    uv = tex.uv;
                }
                dudx = si.dudx;
                dudy = si.dudy;
                dvdx = si.dvdx;
                dvdy = si.dvdy;
                time = si.time;
                objectId = si.objectId;
            }
            else
            {
                p = tex.p;
                n = tex.n;
                ns = tex.n;
                dpdx = tex.dpdx;
                dpdy = tex.dpdy;
                uv = tex.uv;
                dudx = tex.dudx;
                dudy = tex.dudy;
                dvdx = tex.dvdx;
                dvdy = tex.dvdy;
                dpdu = new Vector3f(1, 0, 0);
                dpdv = new Vector3f(0, 1, 0);
                time = 0;
                objectId = tex.objectId;
                if (LengthSquared(n) == 0)
                {
                    n = new Normal3f(0, 0, 1);
                    ns = n;
                }
            }
            bool backfacing = Dot(wo, n) < 0;
            sg.P = ToWorld(p);
            sg.dPdx = ToWorld(dpdx);
            sg.dPdy = ToWorld(dpdy);
            sg.I = ToWorld(-wo);
            sg.dIdx = new Vec3(0, 0, 0);
            sg.dIdy = new Vec3(0, 0, 0);
            Vec3 ngw = ToWorld(n);
            Vec3 nsw = ToWorld(ns);
            if (backfacing)
            {
                ngw = new Vec3(-ngw.x, -ngw.y, -ngw.z);
                nsw = new Vec3(-nsw.x, -nsw.y, -nsw.z);
            }
            sg.N = nsw;
            sg.Ng = ngw;
            sg.u = uv.x;
            sg.v = uv.y;
            sg.dudx = dudx;
            sg.dudy = dudy;
            sg.dvdx = dvdx;
            sg.dvdy = dvdy;
            sg.dPdu = ToWorld(dpdu);
            sg.dPdv = ToWorld(dpdv);
            sg.time = time;
            sg.dtime = OSLScene.shutterClose - OSLScene.shutterOpen;
            sg.dPdtime = new Vec3(0, 0, 0);
            sg.Ps = new Vec3(0, 0, 0);
            sg.dPsdx = new Vec3(0, 0, 0);
            sg.dPsdy = new Vec3(0, 0, 0);
            sg.raytype = rayType;
            sg.backfacing = backfacing ? 1 : 0;
            sg.Ci = null;

            // The transformations of the object and of the shader space
            OSLObjectInfo info = OSLScene.FindObject(objectId);
            Matrix44 object2common = Matrix44.identity;
            sg.flipHandedness = 0;
            sg.surfacearea = 0;
            if (info != null)
            {
                Transform worldFromObject = OSLScene.worldFromRender * (info.animatedRenderFromObject != null ? info.animatedRenderFromObject.Interpolate(time) :
                                                                        info.renderFromObject);
                object2common = RowMatrix(worldFromObject);
                sg.flipHandedness = worldFromObject.SwapsHandedness() ? 1 : 0;
                sg.surfacearea = info.area;
            }
            ts.matrices[0] = object2common;
            ts.matrices[1] = group.ShaderToCommon();
            sg.object2common = &ts.matrices[0];
            sg.shader2common = &ts.matrices[1];

            OSLShadeState state = ts.shade;
            state.group = group;
            state.si = si;
            state.tex = tex;
            state.objectInfo = info;
            state.rayType = rayType;
            state.traceHit = false;
            sg.renderstate = (void*)GCHandle.ToIntPtr(ts.stateHandle);
        }

        /// Executes the group at the point; the closure tree (sg.Ci) is valid until the next execution of this thread
        public static bool Execute(OSLShaderGroup group, OSLThreadState ts, ref ShaderGlobals sg)
        {
            ShadingSystem ss = OSLSystem.ShadingSystem;
            return ss.execute(ts.context, group.group, 0, 0, ref sg, null, null);
        }

        public static bool Execute(OSLShaderGroup group, OSLThreadState ts, ref ShaderGlobals sg, void* outputs)
        {
            ShadingSystem ss = OSLSystem.ShadingSystem;
            return ss.execute(ts.context, group.group, 0, 0, ref sg, null, outputs);
        }

        /// Runs a group for a texture lookup with OSL's own derivatives: the two differentials that OSL carries through the shader (the "x" and
        /// the "y" of Dx() and Dy()) are the two directions on the surface instead of the two pixels, so what comes out next to the output is
        /// its change along them. They keep the size of the pixel (the step of pbrt's BumpMap()), because the shader also filters its texture
        /// lookups with them; the derivatives are divided by it afterwards.
        public static bool EvalOutput(OSLShaderGroup group, SurfaceInteraction si, TextureEvalContext tex, ref SampledWavelengths lambda,
                                      TextureDirections directions, float* result, float* dresultdu, float* dresultdv, int count)
        {
            int available = group.derivativeFloats;
            if (available == 0)
            {
                return false;
            }
            OSLThreadState ts = OSLThreadState.Current();
            ShaderGlobals sg = new ShaderGlobals();
            Vector3f wo = si != null ? si.wo : new Vector3f(0, 0, 1);
            if (LengthSquared(wo) == 0)
            {
                wo = new Vector3f(0, 0, 1);
            }
            FillGlobals(ref sg, ts, group, si, tex, wo, si != null && si.rayType > 0 ? si.rayType : OSLRayType.Camera);
            Float du = .5f * (std.abs(tex.dudx) + std.abs(tex.dudy));
            if (du == 0)
            {
                du = .0005f;
            }
            Float dv = .5f * (std.abs(tex.dvdx) + std.abs(tex.dvdy));
            if (dv == 0)
            {
                dv = .0005f;
            }
            sg.dPdx = ToWorld(du * directions.dpdu);
            sg.dudx = du * directions.duvdu.x;
            sg.dvdx = du * directions.duvdu.y;
            sg.dPdy = ToWorld(dv * directions.dpdv);
            sg.dudy = dv * directions.duvdv.x;
            sg.dvdy = dv * directions.duvdv.y;
            ts.shade.lambda = lambda;
            float* arena = stackalloc float[9];
            for (int i = 0; i < 9; ++i)
            {
                arena[i] = 0;
            }
            if (!Execute(group, ts, ref sg, arena))
            {
                return false;
            }
            float* dx = arena + available;
            float* dy = arena + 2 * available;
            if (count == 1 && available == 3)
            {
                result[0] = (arena[0] + arena[1] + arena[2]) * (1f / 3f);
                dresultdu[0] = (dx[0] + dx[1] + dx[2]) * (1f / 3f) / du;
                dresultdv[0] = (dy[0] + dy[1] + dy[2]) * (1f / 3f) / dv;
                return true;
            }
            for (int i = 0; i < count; ++i)
            {
                int c = i < available ? i : available - 1;
                result[i] = arena[c];
                dresultdu[i] = dx[c] / du;
                dresultdv[i] = dy[c] / dv;
            }
            return true;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------
        // Textures: the value of a named output
        // ---------------------------------------------------------------------------------------------------------------------------------

        /// Runs a group for a texture lookup and reads the output (1 or 3 floats)
        public static bool EvalOutput(OSLShaderGroup group, SurfaceInteraction si, TextureEvalContext tex, ref SampledWavelengths lambda, float* result,
                                      int count)
        {
            OSLThreadState ts = OSLThreadState.Current();
            ShaderGlobals sg = new ShaderGlobals();
            Vector3f wo = si != null ? si.wo : new Vector3f(0, 0, 1);
            if (LengthSquared(wo) == 0)
            {
                wo = new Vector3f(0, 0, 1);
            }
            FillGlobals(ref sg, ts, group, si, tex, wo, si != null && si.rayType > 0 ? si.rayType : OSLRayType.Camera);
            ts.shade.lambda = lambda;
            if (!Execute(group, ts, ref sg))
            {
                return false;
            }
            ShaderSymbol sym = group.OutputSymbol(ts);
            if (sym == null)
            {
                return false;
            }
            ShadingSystem ss = OSLSystem.ShadingSystem;
            TypeDesc type = ss.symbol_typedesc(sym);
            object managed;
            void* data = ss.symbol_address(ts.context, sym, out managed);
            GCHandle pin = default(GCHandle);
            if (data == null && managed != null)
            {
                pin = GCHandle.Alloc(managed, GCHandleType.Pinned);
                data = (void*)pin.AddrOfPinnedObject();
            }
            if (data == null)
            {
                return false;
            }
            try
            {
                if (type.basetype != TypeDesc.FLOAT)
                {
                    if (type.basetype == TypeDesc.INT)
                    {
                        result[0] = result[1] = result[2] = (float)*(int*)data;
                        return true;
                    }
                    return false;
                }
                int available = (int)type.aggregate;
                if (count == 1 && available == 3)
                {
                    result[0] = (((float*)data)[0] + ((float*)data)[1] + ((float*)data)[2]) * (1f / 3f);
                    return true;
                }
                for (int i = 0; i < count; ++i)
                {
                    result[i] = ((float*)data)[available == 1 ? 0 : (i < available ? i : available - 1)];
                }
                return true;
            }
            finally
            {
                if (pin.IsAllocated)
                {
                    pin.Free();
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------------------------
        // Surfaces: the closures of the shader
        // ---------------------------------------------------------------------------------------------------------------------------------

        private sealed class Builder
        {
            public OSLClosureBxDF bxdf = new OSLClosureBxDF();
            public Frame frame;
            public SampledWavelengths lambda;
            public bool backfacing;
            public SampledSpectrum emission = new SampledSpectrum(0f);
            public bool hasEmission = false;
            public bool holdout = false;
            public Float selectionTotal = 0;
            /// The reflection albedo of the lobes so far (what the base of a layer is covered by): the selection value, except for the dielectric
            /// interfaces, where only the Fresnel reflection covers the base
            public Float albedoTotal = 0;
            public Float cosWo = 1;
        }

        // The closure normal and tangent as directions in the local frame of the BSDF
        private static Vector3f LocalDirection(Builder b, Vec3 v)
        {
            Vector3f render = OSLScene.RenderFromWorld(new Vector3f(v.x, v.y, v.z));
            return b.frame.ToLocal(render);
        }

        private static Float Average(Color3 c)
        {
            return (c.x + c.y + c.z) * (1f / 3f);
        }

        private static SampledSpectrum Spectrum(Builder b, Color3 c)
        {
            return OSLSystem.ToSpectrum(c.x, c.y, c.z, b.lambda);
        }

        private static Color3 Mul(Color3 a, Color3 b)
        {
            return new Color3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        private static Color3 Mul(Color3 a, Vec3 b)
        {
            return new Color3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        private static Color3 Mul(Vec3 a, Vec3 b)
        {
            return new Color3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        private static void Add(Builder b, BxDF lobe, Color3 weight, Vec3 normal, Vec3 tangent)
        {
            Float sel = Average(weight);
            if (sel <= 0 || float.IsNaN(sel))
            {
                return;
            }
            Vector3f n = LocalDirection(b, normal);
            Vector3f t = (tangent.x == 0 && tangent.y == 0 && tangent.z == 0) ? new Vector3f(0, 0, 0) : LocalDirection(b, tangent);
            b.bxdf.Add(lobe, Spectrum(b, weight), sel, n, t);
            b.selectionTotal += sel;
            b.albedoTotal += sel;
        }

        private static OSLMicrofacetDistribution MakeDistribution(string name, Float alphaX, Float alphaY)
        {
            if (name == "beckmann")
            {
                return new OSLBeckmannDistribution(alphaX, alphaY);
            }
            return new OSLGGXDistribution(alphaX, alphaY);
        }

        private static Float EtaFromF0(Float f0)
        {
            Float s = std.sqrt(Clamp(f0, 0f, 0.999f));
            return (1 + s) / (1 - s);
        }

        private static void Walk(Builder b, ClosureColor* c, Color3 w)
        {
            if (c == null)
            {
                return;
            }
            if (c->id == ClosureColor.MUL)
            {
                ClosureMul* m = (ClosureMul*)c;
                Walk(b, m->closure, Mul(w, m->weight));
                return;
            }
            if (c->id == ClosureColor.ADD)
            {
                ClosureAdd* a = (ClosureAdd*)c;
                Walk(b, a->closureA, w);
                Walk(b, a->closureB, w);
                return;
            }
            ClosureComponent* comp = (ClosureComponent*)c;
            void* data = ClosureComponent.data(comp);
            Color3 weight = Mul(w, comp->w);
            Vec3 zero = new Vec3(0, 0, 0);
            switch (comp->id)
            {
                case OSLClosureId.Emission:
                {
                    b.emission += OSLSystem.ToEmissionSpectrum(weight.x, weight.y, weight.z, b.lambda);
                    b.hasEmission = true;
                    break;
                }
                case OSLClosureId.MxUniformEdf:
                {
                    Color3 e = Mul(weight, OSLClosureData.ReadColor3(data, OSLClosures.MxUniformEdfEmittance));
                    b.emission += OSLSystem.ToEmissionSpectrum(e.x, e.y, e.z, b.lambda);
                    b.hasEmission = true;
                    break;
                }
                case OSLClosureId.Background:
                case OSLClosureId.Debug:
                case OSLClosureId.MxAnisotropicVdf:
                case OSLClosureId.MxMediumVdf:
                    break;
                case OSLClosureId.Holdout:
                    b.holdout = true;
                    break;
                case OSLClosureId.Diffuse:
                    Add(b, new DiffuseBxDF(new SampledSpectrum(1f)), weight, OSLClosureData.ReadVec3(data, 0), zero);
                    break;
                case OSLClosureId.OrenNayar:
                    Add(b, new OSLOrenNayarBxDF(OSLClosureData.ReadFloat(data, 12)), weight, OSLClosureData.ReadVec3(data, 0), zero);
                    break;
                case OSLClosureId.Translucent:
                    Add(b, new DiffuseTransmissionBxDF(new SampledSpectrum(0f), new SampledSpectrum(1f)), weight, OSLClosureData.ReadVec3(data, 0), zero);
                    break;
                case OSLClosureId.Phong:
                    Add(b, new OSLPhongBxDF(OSLClosureData.ReadFloat(data, 12)), weight, OSLClosureData.ReadVec3(data, 0), zero);
                    break;
                case OSLClosureId.Ward:
                    Add(b, new OSLWardBxDF(OSLClosureData.ReadFloat(data, 24), OSLClosureData.ReadFloat(data, 28)), weight,
                        OSLClosureData.ReadVec3(data, 0), OSLClosureData.ReadVec3(data, 12));
                    break;
                case OSLClosureId.Microfacet:
                {
                    string dist = OSLClosureData.ReadString(data, OSLClosures.MicrofacetDist);
                    Float ax = OSLClosureData.ReadFloat(data, OSLClosures.MicrofacetXAlpha);
                    Float ay = OSLClosureData.ReadFloat(data, OSLClosures.MicrofacetYAlpha);
                    Float eta = OSLClosureData.ReadFloat(data, OSLClosures.MicrofacetEta);
                    int refract = OSLClosureData.ReadInt(data, OSLClosures.MicrofacetRefract);
                    Vec3 n = OSLClosureData.ReadVec3(data, OSLClosures.MicrofacetN);
                    Vec3 u = OSLClosureData.ReadVec3(data, OSLClosures.MicrofacetU);
                    AddMicrofacet(b, dist, ax, ay, eta, refract != 1, refract != 0, new SampledSpectrum(1f), new SampledSpectrum(1f), weight, n, u);
                    break;
                }
                case OSLClosureId.FresnelReflection:
                {
                    Float eta = OSLClosureData.ReadFloat(data, 12);
                    Add(b, new OSLSpecularReflectionBxDF(new OSLDielectricFresnel(eta)), weight, OSLClosureData.ReadVec3(data, 0), zero);
                    break;
                }
                case OSLClosureId.Refraction:
                {
                    Float eta = OSLClosureData.ReadFloat(data, 12);
                    Add(b, new OSLSpecularRefractionBxDF(eta), weight, OSLClosureData.ReadVec3(data, 0), zero);
                    break;
                }
                case OSLClosureId.Transparent:
                case OSLClosureId.MxTransparent:
                    Add(b, new OSLTransparentBxDF(), weight, zero, zero);
                    break;
                case OSLClosureId.MxOrenNayarDiffuse:
                {
                    Color3 albedo = Mul(weight, OSLClosureData.ReadColor3(data, OSLClosures.MxOrenNayarAlbedo));
                    Add(b, new OSLOrenNayarBxDF(OSLClosureData.ReadFloat(data, OSLClosures.MxOrenNayarRoughness)), albedo,
                        OSLClosureData.ReadVec3(data, OSLClosures.MxOrenNayarN), zero);
                    break;
                }
                case OSLClosureId.MxBurleyDiffuse:
                {
                    Color3 albedo = Mul(weight, OSLClosureData.ReadColor3(data, OSLClosures.MxBurleyAlbedo));
                    Add(b, new OSLBurleyDiffuseBxDF(OSLClosureData.ReadFloat(data, OSLClosures.MxBurleyRoughness)), albedo,
                        OSLClosureData.ReadVec3(data, OSLClosures.MxBurleyN), zero);
                    break;
                }
                case OSLClosureId.MxTranslucent:
                {
                    Color3 albedo = Mul(weight, OSLClosureData.ReadColor3(data, OSLClosures.MxTranslucentAlbedo));
                    Add(b, new DiffuseTransmissionBxDF(new SampledSpectrum(0f), new SampledSpectrum(1f)), albedo,
                        OSLClosureData.ReadVec3(data, OSLClosures.MxTranslucentN), zero);
                    break;
                }
                case OSLClosureId.MxSheen:
                {
                    Color3 albedo = Mul(weight, OSLClosureData.ReadColor3(data, OSLClosures.MxSheenAlbedo));
                    Float rough = OSLClosureData.ReadFloat(data, OSLClosures.MxSheenRoughness);
                    Add(b, new OSLSheenBxDF(rough), albedo, OSLClosureData.ReadVec3(data, OSLClosures.MxSheenN), zero);
                    break;
                }
                case OSLClosureId.MxSubsurface:
                {
                    // approximated by its diffuse reflection (see docs/osl-pbrt.md)
                    Color3 albedo = Mul(weight, OSLClosureData.ReadColor3(data, OSLClosures.MxSubsurfaceAlbedo));
                    Add(b, new DiffuseBxDF(new SampledSpectrum(1f)), albedo, OSLClosureData.ReadVec3(data, OSLClosures.MxSubsurfaceN), zero);
                    break;
                }
                case OSLClosureId.Subsurface:
                {
                    Color3 albedo = Mul(weight, OSLClosureData.ReadColor3(data, OSLClosures.SubsurfaceAlbedo));
                    Add(b, new DiffuseBxDF(new SampledSpectrum(1f)), albedo, zero, zero);
                    break;
                }
                case OSLClosureId.MxDielectric:
                {
                    string dist = OSLClosureData.ReadString(data, OSLClosures.MxDielectricDistribution);
                    Float rx = OSLClosureData.ReadFloat(data, OSLClosures.MxDielectricRoughnessX);
                    Float ry = OSLClosureData.ReadFloat(data, OSLClosures.MxDielectricRoughnessY);
                    Float ior = OSLClosureData.ReadFloat(data, OSLClosures.MxDielectricIor);
                    Float eta = b.backfacing ? 1 / std.max<Float>(ior, 1e-3f) : ior;
                    Color3 rt = OSLClosureData.ReadColor3(data, OSLClosures.MxDielectricReflTint);
                    Color3 tt = OSLClosureData.ReadColor3(data, OSLClosures.MxDielectricRefrTint);
                    AddMicrofacet(b, dist, rx * rx, ry * ry, eta, true, true, Spectrum(b, rt), Spectrum(b, tt), weight,
                                  OSLClosureData.ReadVec3(data, OSLClosures.MxDielectricN), OSLClosureData.ReadVec3(data, OSLClosures.MxDielectricU));
                    break;
                }
                case OSLClosureId.MxGeneralizedSchlick:
                {
                    string dist = OSLClosureData.ReadString(data, OSLClosures.MxSchlickDistribution);
                    Float rx = OSLClosureData.ReadFloat(data, OSLClosures.MxSchlickRoughnessX);
                    Float ry = OSLClosureData.ReadFloat(data, OSLClosures.MxSchlickRoughnessY);
                    Color3 f0 = OSLClosureData.ReadColor3(data, OSLClosures.MxSchlickF0);
                    Color3 f90 = OSLClosureData.ReadColor3(data, OSLClosures.MxSchlickF90);
                    Float exponent = OSLClosureData.ReadFloat(data, OSLClosures.MxSchlickExponent);
                    Color3 rt = OSLClosureData.ReadColor3(data, OSLClosures.MxSchlickReflTint);
                    Color3 tt = OSLClosureData.ReadColor3(data, OSLClosures.MxSchlickRefrTint);
                    Float eta = EtaFromF0(Average(f0));
                    if (b.backfacing)
                    {
                        eta = 1 / eta;
                    }
                    bool transmits = Average(tt) > 0;
                    OSLFresnel fresnel = new OSLSchlickFresnel(Spectrum(b, f0), Spectrum(b, f90), exponent);
                    Vec3 n = OSLClosureData.ReadVec3(data, OSLClosures.MxSchlickN);
                    Vec3 u = OSLClosureData.ReadVec3(data, OSLClosures.MxSchlickU);
                    Float ax = rx * rx, ay = ry * ry;
                    if (std.max(ax, ay) < 1e-3f)
                    {
                        Add(b, new OSLSpecularReflectionBxDF(fresnel), Mul(weight, rt), n, u);
                        if (transmits)
                        {
                            Add(b, new OSLSpecularRefractionBxDF(eta), Mul(weight, tt), n, u);
                        }
                    }
                    else
                    {
                        Add(b, new OSLMicrofacetBxDF(MakeDistribution(dist, ax, ay), fresnel, eta, true, transmits, new SampledSpectrum(1f),
                                                     new SampledSpectrum(1f)), weight, n, u);
                    }
                    break;
                }
                case OSLClosureId.MxConductor:
                {
                    string dist = OSLClosureData.ReadString(data, OSLClosures.MxConductorDistribution);
                    Float rx = OSLClosureData.ReadFloat(data, OSLClosures.MxConductorRoughnessX);
                    Float ry = OSLClosureData.ReadFloat(data, OSLClosures.MxConductorRoughnessY);
                    Color3 ior = OSLClosureData.ReadColor3(data, OSLClosures.MxConductorIor);
                    Color3 k = OSLClosureData.ReadColor3(data, OSLClosures.MxConductorExtinction);
                    SampledSpectrum eta = Spectrum(b, ior);
                    SampledSpectrum kk = Spectrum(b, k);
                    Float ax = rx * rx, ay = ry * ry;
                    Vec3 n = OSLClosureData.ReadVec3(data, OSLClosures.MxConductorN);
                    Vec3 u = OSLClosureData.ReadVec3(data, OSLClosures.MxConductorU);
                    if (dist == "beckmann" && std.max(ax, ay) >= 1e-3f)
                    {
                        Add(b, new OSLMicrofacetBxDF(new OSLBeckmannDistribution(ax, ay), new OSLConductorFresnel(eta, kk), 1, true, false,
                                                     new SampledSpectrum(1f), new SampledSpectrum(1f)), weight, n, u);
                    }
                    else
                    {
                        Add(b, new ConductorBxDF(new TrowbridgeReitzDistribution(ax, ay), eta, kk), weight, n, u);
                    }
                    break;
                }
                case OSLClosureId.MxLayer:
                {
                    ClosureColor* top = OSLClosureData.ReadClosure(data, OSLClosures.MxLayerTop);
                    ClosureColor* baseClosure = OSLClosureData.ReadClosure(data, OSLClosures.MxLayerBase);
                    Float before = b.albedoTotal;
                    Walk(b, top, weight);
                    Float topAlbedo = Clamp(b.albedoTotal - before, 0f, 1f);
                    Color3 baseWeight = new Color3(weight.x * (1 - topAlbedo), weight.y * (1 - topAlbedo), weight.z * (1 - topAlbedo));
                    Walk(b, baseClosure, baseWeight);
                    break;
                }
                default:
                    break;
            }
        }

        // A rough or smooth dielectric interface (reflection, refraction or both), the microfacet closure of OSL and the dielectric of MaterialX
        private static void AddMicrofacet(Builder b, string dist, Float ax, Float ay, Float eta, bool reflect, bool transmit, SampledSpectrum reflTint,
                                          SampledSpectrum refrTint, Color3 weight, Vec3 n, Vec3 u)
        {
            OSLFresnel fresnel = new OSLDielectricFresnel(eta);
            Float albedoBefore = b.albedoTotal;
            AddMicrofacetLobe(b, dist, ax, ay, eta, reflect, transmit, reflTint, refrTint, weight, n, u, fresnel);
            Float added = b.albedoTotal - albedoBefore;
            if (added > 0 && transmit)
            {
                // only the reflection hides what is below the interface
                Float albedo = reflect ? std.min(1f, reflTint.Average() * FrDielectric(b.cosWo, eta)) : 0f;
                b.albedoTotal = albedoBefore + added * albedo;
            }
        }

        private static void AddMicrofacetLobe(Builder b, string dist, Float ax, Float ay, Float eta, bool reflect, bool transmit, SampledSpectrum reflTint,
                                              SampledSpectrum refrTint, Color3 weight, Vec3 n, Vec3 u, OSLFresnel fresnel)
        {
            if (std.max(ax, ay) < 1e-3f)
            {
                // smooth interface
                if (reflect && transmit)
                {
                    BxDF smooth = new DielectricBxDF(eta, new TrowbridgeReitzDistribution(0, 0));
                    Add(b, new OSLTintedBxDF(smooth, reflTint, refrTint), weight, n, u);
                }
                else if (reflect)
                {
                    Add(b, new OSLSpecularReflectionBxDF(fresnel), weight, n, u);
                }
                else
                {
                    Add(b, new OSLSpecularRefractionBxDF(eta), weight, n, u);
                }
                return;
            }
            Add(b, new OSLMicrofacetBxDF(MakeDistribution(dist, ax, ay), fresnel, eta, reflect, transmit, reflTint, refrTint), weight, n, u);
        }

        /// Shades a surface point: runs the group and converts its closures to a BxDF and an emission. The result of the last shading of the thread
        /// is reused if it is for the same point, frame and wavelengths (emission and BSDF of a hit come from one execution).
        public static OSLShadeResult ShadeSurface(OSLShaderGroup group, SurfaceInteraction si, Normal3f ns, Vector3f dpdus, ref SampledWavelengths lambda)
        {
            OSLShadeResult cached = lastResult;
            if (cached != null && cached.si == si && cached.ns == ns && cached.dpdus == dpdus && cached.lambda == lambda)
            {
                return cached;
            }
            OSLThreadState ts = OSLThreadState.Current();
            ShaderGlobals sg = new ShaderGlobals();
            int rayType = si.rayType > 0 ? si.rayType : OSLRayType.Camera;
            FillGlobals(ref sg, ts, group, si, default(TextureEvalContext), si.wo, rayType);
            ts.shade.lambda = lambda;
            Builder b = new Builder();
            b.lambda = lambda;
            b.backfacing = sg.backfacing != 0;
            b.frame = Frame.FromXZ(Normalize(dpdus), (Vector3f)ns);
            b.cosWo = std.abs(Dot(si.wo, (Vector3f)ns));
            if (Execute(group, ts, ref sg))
            {
                Walk(b, sg.Ci, new Color3(1, 1, 1));
            }
            OSLShadeResult r = new OSLShadeResult();
            r.bxdf = b.bxdf.Count > 0 ? b.bxdf : null;
            r.emission = b.emission;
            r.hasEmission = b.hasEmission;
            r.holdout = b.holdout;
            r.si = si;
            r.ns = ns;
            r.dpdus = dpdus;
            r.lambda = lambda;
            lastResult = r;
            return r;
        }
    }
}
