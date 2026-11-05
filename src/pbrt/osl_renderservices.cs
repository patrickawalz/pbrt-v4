// (not in pbrt-v4) Open Shading Language integration: the RendererServices of pbrt, which answers what a running shader asks the renderer:
// the coordinate systems (object, shader, camera, screen, NDC, raster, the named coordinate systems of the scene file), standard attributes
// (object:name, camera:resolution, ...), user data (the parameters of a material bound to pbrt textures, the surface parameterization), textures
// (the texture system of OSL reading the images through pbrt's image readers), trace() with the aggregate of the scene, and getmessage().
//
// Positions and directions are in "common" space = world space of the scene file; see osl_scene.cs.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OIIO;
using OSL;

namespace pbrt
{
    public sealed unsafe class OSLRendererServices : RendererServices
    {
        private static readonly ulong hCamera = new ustring("camera").hash();
        private static readonly ulong hScreen = new ustring("screen").hash();
        private static readonly ulong hNDC = new ustring("NDC").hash();
        private static readonly ulong hRaster = new ustring("raster").hash();
        private static readonly ulong hWorld = new ustring("world").hash();
        private static readonly ulong hCommon = new ustring("common").hash();
        private static readonly ulong hPerspective = new ustring("perspective").hash();

        public OSLRendererServices(TextureSystem texturesys) : base(texturesys)
        {
        }

        public override int supports(string feature)
        {
            return 0;
        }

        private static OSLShadeState State(ShaderGlobals* sg)
        {
            if (sg == null || sg->renderstate == null)
            {
                return null;
            }
            return (OSLShadeState)GCHandle.FromIntPtr((IntPtr)sg->renderstate).Target;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------
        // Coordinate systems
        // ---------------------------------------------------------------------------------------------------------------------------------

        public override bool get_matrix(ShaderGlobals* sg, out Matrix44 result, TransformationPtr xform, float time)
        {
            result = *(Matrix44*)xform.ptr;
            return true;
        }

        public override bool get_matrix(ShaderGlobals* sg, out Matrix44 result, TransformationPtr xform)
        {
            result = *(Matrix44*)xform.ptr;
            return true;
        }

        public override bool get_matrix(ShaderGlobals* sg, out Matrix44 result, ulong from)
        {
            return get_matrix(sg, out result, from, sg != null ? sg->time : 0);
        }

        public override bool get_matrix(ShaderGlobals* sg, out Matrix44 result, ulong from, float time)
        {
            result = Matrix44.identity;
            if (from == hWorld || from == hCommon)
            {
                return true;
            }
            if (from == hCamera)
            {
                result = OSLShading.RowMatrix(OSLScene.worldFromCamera.Interpolate(time));
                return true;
            }
            if (from == hScreen || from == hNDC || from == hRaster)
            {
                Matrix44 commonToSpace;
                if (!CommonToProjective(from, time, out commonToSpace))
                {
                    return false;
                }
                result = RsMatrix.invert(commonToSpace);
                return true;
            }
            AnimatedTransform worldFromCS;
            if (OSLScene.FindCoordinateSystem(OslStrings.from_hash(from).@string(), out worldFromCS))
            {
                result = OSLShading.RowMatrix(worldFromCS.Interpolate(time));
                return true;
            }
            return false;
        }

        // The matrix from common space to camera, screen, NDC or raster space (row vector convention), like testshade's SimpleRenderer
        private bool CommonToProjective(ulong to, float time, out Matrix44 result)
        {
            Matrix44 worldToCamera = RsMatrix.invert(OSLShading.RowMatrix(OSLScene.worldFromCamera.Interpolate(time)));
            result = worldToCamera;
            if (to == hCamera)
            {
                return true;
            }
            float hither = OSLScene.clipNear, yon = OSLScene.clipFar;
            float depthrange = (float)((double)yon - (double)hither);
            Matrix44 cameraToScreen;
            if (OSLScene.cameraName == "orthographic")
            {
                cameraToScreen = new Matrix44(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 / depthrange, 0, 0, 0, -hither / depthrange, 1);
            }
            else
            {
                float tanhalffov = (float)Math.Tan(0.5f * OSLScene.cameraFov * Math.PI / 180.0);
                cameraToScreen = new Matrix44(1 / tanhalffov, 0, 0, 0, 0, 1 / tanhalffov, 0, 0, 0, 0, yon / depthrange, 1, 0, 0,
                                              -yon * hither / depthrange, 0);
            }
            result = result * cameraToScreen;
            if (to == hScreen)
            {
                return true;
            }
            // screen -> NDC: the screen window to [0,1]^2, y down
            float left = OSLScene.screenWindow[0], bottom = OSLScene.screenWindow[1];
            float width = OSLScene.screenWindow[2] - left, height = OSLScene.screenWindow[3] - bottom;
            Matrix44 screenToNdc = new Matrix44(1 / width, 0, 0, 0, 0, -1 / height, 0, 0, 0, 0, 1, 0, -left / width, 1 + bottom / height, 0, 1);
            result = result * screenToNdc;
            if (to == hRaster)
            {
                Matrix44 ndcToRaster = new Matrix44(OSLScene.resolution.x, 0, 0, 0, 0, OSLScene.resolution.y, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
                result = result * ndcToRaster;
            }
            return true;
        }

        public override bool get_inverse_matrix(ShaderGlobals* sg, out Matrix44 result, ulong to, float time)
        {
            if (to == hCamera || to == hScreen || to == hNDC || to == hRaster)
            {
                return CommonToProjective(to, time, out result);
            }
            return base.get_inverse_matrix(sg, out result, to, time);
        }

        public override bool get_inverse_matrix(ShaderGlobals* sg, out Matrix44 result, ulong to)
        {
            return get_inverse_matrix(sg, out result, to, sg != null ? sg->time : 0);
        }

        // ---------------------------------------------------------------------------------------------------------------------------------
        // Attributes
        // ---------------------------------------------------------------------------------------------------------------------------------

        private static bool IsArray(TypeDesc type, int basetype, int agg, int len)
        {
            return type.basetype == basetype && type.aggregate == agg && type.arraylen == len;
        }

        private static void ZeroDerivs(bool derivs, TypeDesc type, void* val)
        {
            if (derivs)
            {
                int bytes = (int)type.size();
                byte* p = (byte*)val + bytes;
                for (int i = 0; i < 2 * bytes; ++i)
                {
                    p[i] = 0;
                }
            }
        }

        public override bool get_attribute(ShaderGlobals* sg, bool derivatives, ulong obj, TypeDesc type, ulong name, void* val)
        {
            return get_array_attribute(sg, derivatives, obj, type, name, -1, val);
        }

        public override bool get_array_attribute(ShaderGlobals* sg, bool derivatives, ulong obj, TypeDesc type, ulong name, int index, void* val)
        {
            string attr = OslStrings.from_hash(name).@string();
            OSLShadeState state = State(sg);
            switch (attr)
            {
                case "object:name":
                case "geom:name":
                    if (type == TypeDescs.TypeString)
                    {
                        string objectName = state != null && state.objectInfo != null ? state.objectInfo.name : "";
                        *(ulong*)val = new ustring(objectName).hash();
                        return true;
                    }
                    return false;
                case "object:surfacearea":
                    if (type == TypeDescs.TypeFloat)
                    {
                        *(float*)val = state != null && state.objectInfo != null ? state.objectInfo.area : 0;
                        ZeroDerivs(derivatives, type, val);
                        return true;
                    }
                    return false;
                case "raytype":
                    if (type == TypeDescs.TypeInt)
                    {
                        *(int*)val = sg != null ? sg->raytype : 0;
                        return true;
                    }
                    return false;
                case "camera:resolution":
                    if (IsArray(type, TypeDesc.INT, TypeDesc.SCALAR, 2))
                    {
                        ((int*)val)[0] = OSLScene.resolution.x;
                        ((int*)val)[1] = OSLScene.resolution.y;
                        return true;
                    }
                    return false;
                case "camera:pixelaspect":
                    if (type == TypeDescs.TypeFloat)
                    {
                        *(float*)val = OSLScene.pixelAspect;
                        ZeroDerivs(derivatives, type, val);
                        return true;
                    }
                    return false;
                case "camera:projection":
                    if (type == TypeDescs.TypeString)
                    {
                        *(ulong*)val = new ustring(OSLScene.cameraName == "orthographic" ? "orthographic" : "perspective").hash();
                        return true;
                    }
                    return false;
                case "camera:fov":
                    if (type == TypeDescs.TypeFloat)
                    {
                        *(float*)val = OSLScene.cameraFov;
                        ZeroDerivs(derivatives, type, val);
                        return true;
                    }
                    return false;
                case "camera:clip":
                    if (IsArray(type, TypeDesc.FLOAT, TypeDesc.SCALAR, 2))
                    {
                        ((float*)val)[0] = OSLScene.clipNear;
                        ((float*)val)[1] = OSLScene.clipFar;
                        ZeroDerivs(derivatives, type, val);
                        return true;
                    }
                    return false;
                case "camera:clip_near":
                    return ReturnFloat(type, val, derivatives, OSLScene.clipNear);
                case "camera:clip_far":
                    return ReturnFloat(type, val, derivatives, OSLScene.clipFar);
                case "camera:shutter":
                case "shutter":
                    if (IsArray(type, TypeDesc.FLOAT, TypeDesc.SCALAR, 2))
                    {
                        ((float*)val)[0] = OSLScene.shutterOpen;
                        ((float*)val)[1] = OSLScene.shutterClose;
                        ZeroDerivs(derivatives, type, val);
                        return true;
                    }
                    return false;
                case "camera:shutter_open":
                    return ReturnFloat(type, val, derivatives, OSLScene.shutterOpen);
                case "camera:shutter_close":
                    return ReturnFloat(type, val, derivatives, OSLScene.shutterClose);
                case "camera:screen_window":
                    if (IsArray(type, TypeDesc.FLOAT, TypeDesc.SCALAR, 4))
                    {
                        for (int i = 0; i < 4; ++i)
                        {
                            ((float*)val)[i] = OSLScene.screenWindow[i];
                        }
                        ZeroDerivs(derivatives, type, val);
                        return true;
                    }
                    return false;
                default:
                    break;
            }
            // anything else the shader asks for without naming an object may be user data
            if (obj == new ustring("").hash() && index == -1)
            {
                return get_userdata(derivatives, name, type, sg, val);
            }
            return false;
        }

        private static bool ReturnFloat(TypeDesc type, void* val, bool derivs, float value)
        {
            if (type != TypeDescs.TypeFloat)
            {
                return false;
            }
            *(float*)val = value;
            ZeroDerivs(derivs, type, val);
            return true;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------
        // User data
        // ---------------------------------------------------------------------------------------------------------------------------------

        private static void StoreFloat(float* dst, int index, float value, float dx, float dy, bool derivs, int stride)
        {
            dst[index] = value;
            if (derivs)
            {
                dst[stride + index] = dx;
                dst[2 * stride + index] = dy;
            }
        }

        public override bool get_userdata(bool derivatives, ulong name, TypeDesc type, ShaderGlobals* sg, void* val)
        {
            OSLShadeState state = State(sg);
            if (state == null)
            {
                return false;
            }
            // a parameter of the shader that a pbrt texture gives
            OSLTextureBinding binding;
            if (state.group != null && state.group.bindings.TryGetValue(name, out binding))
            {
                return EvaluateBinding(state, binding, derivatives, type, val);
            }
            string key = OslStrings.from_hash(name).@string();
            SurfaceInteraction si = state.si;
            // the surface parameterization and geometry as user data
            switch (key)
            {
                case "uv":
                    if (type == TypeDescs.TypePoint || type == TypeDescs.TypeVector || type == TypeDescs.TypeColor)
                    {
                        ((float*)val)[0] = sg->u;
                        ((float*)val)[1] = sg->v;
                        ((float*)val)[2] = 0;
                        ZeroDerivs(derivatives, type, val);
                        return true;
                    }
                    if (IsArray(type, TypeDesc.FLOAT, TypeDesc.SCALAR, 2))
                    {
                        ((float*)val)[0] = sg->u;
                        ((float*)val)[1] = sg->v;
                        ZeroDerivs(derivatives, type, val);
                        return true;
                    }
                    return false;
                case "s":
                    if (type == TypeDescs.TypeFloat)
                    {
                        StoreFloat((float*)val, 0, sg->u, sg->dudx, sg->dudy, derivatives, 1);
                        return true;
                    }
                    return false;
                case "t":
                    if (type == TypeDescs.TypeFloat)
                    {
                        StoreFloat((float*)val, 0, sg->v, sg->dvdx, sg->dvdy, derivatives, 1);
                        return true;
                    }
                    return false;
                case "dPdu":
                    return ReturnVec3(type, val, derivatives, sg->dPdu);
                case "dPdv":
                    return ReturnVec3(type, val, derivatives, sg->dPdv);
                case "Ng":
                    return ReturnVec3(type, val, derivatives, sg->Ng);
                case "Ns":
                    return ReturnVec3(type, val, derivatives, sg->N);
                case "faceIndex":
                case "face_index":
                    if (type == TypeDescs.TypeInt)
                    {
                        *(int*)val = si != null ? si.faceIndex : (state.tex.faceIndex);
                        return true;
                    }
                    return false;
                case "objectId":
                    if (type == TypeDescs.TypeInt)
                    {
                        *(int*)val = si != null ? si.objectId : state.tex.objectId;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static bool ReturnVec3(TypeDesc type, void* val, bool derivs, Vec3 v)
        {
            if (type != TypeDescs.TypeVector && type != TypeDescs.TypeNormal && type != TypeDescs.TypePoint && type != TypeDescs.TypeColor)
            {
                return false;
            }
            ((float*)val)[0] = v.x;
            ((float*)val)[1] = v.y;
            ((float*)val)[2] = v.z;
            ZeroDerivs(derivs, type, val);
            return true;
        }

        // The value of a pbrt texture at the shading point, for the parameter of a shader
        private static bool EvaluateBinding(OSLShadeState state, OSLTextureBinding binding, bool derivs, TypeDesc type, void* val)
        {
            TextureEvalContext ctx = state.si != null ? new TextureEvalContext(state.si) : state.tex;
            float* v = (float*)val;
            if (binding.floatTexture != null)
            {
                float f = binding.floatTexture.Evaluate(ctx);
                if (type == TypeDescs.TypeFloat)
                {
                    v[0] = f;
                }
                else if (type.aggregate == TypeDesc.VEC3)
                {
                    v[0] = v[1] = v[2] = f;
                }
                else
                {
                    return false;
                }
                ZeroDerivs(derivs, type, val);
                return true;
            }
            if (binding.spectrumTexture != null)
            {
                SampledSpectrum s = binding.spectrumTexture.Evaluate(ctx, state.lambda);
                Float r, g, b;
                OSLSystem.ToRGB(s, state.lambda, out r, out g, out b);
                if (type.aggregate == TypeDesc.VEC3 && type.basetype == TypeDesc.FLOAT)
                {
                    v[0] = r;
                    v[1] = g;
                    v[2] = b;
                }
                else if (type == TypeDescs.TypeFloat)
                {
                    v[0] = (r + g + b) * (1f / 3f);
                }
                else
                {
                    return false;
                }
                ZeroDerivs(derivs, type, val);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------
        // Ray tracing: trace() and getmessage("trace", ...)
        // ---------------------------------------------------------------------------------------------------------------------------------

        public override bool trace(ref TraceOpt options, ShaderGlobals* sg, Vec3 P, Vec3 dPdx, Vec3 dPdy, Vec3 R, Vec3 dRdx, Vec3 dRdy)
        {
            OSLShadeState state = State(sg);
            if (state != null)
            {
                state.traceHit = false;
            }
            Primitive aggregate = OSLScene.aggregate;
            if (aggregate == null || state == null)
            {
                return false;
            }
            Point3f o = OSLScene.RenderFromWorld(new Point3f(P.x, P.y, P.z));
            Vector3f d = OSLScene.RenderFromWorld(new Vector3f(R.x, R.y, R.z));
            Float len = Length(d);
            if (len == 0)
            {
                return false;
            }
            Float tMax = options.maxdist >= 1e29f ? Infinity : options.maxdist * len;
            Ray ray = new Ray(o, d, sg->time);
            if (options.mindist > 0)
            {
                ray.o = ray.o + d * (options.mindist / len);
            }
            pstd.optional<ShapeIntersection> isect = aggregate.Intersect(ray, tMax);
            if (!isect)
            {
                return false;
            }
            ShapeIntersection si = isect.value();
            state.traceHit = true;
            state.traceInteraction = si.intr;
            state.traceDistance = si.tHit * len / len;
            state.traceDirection = d / len;
            return true;
        }

        public override bool getmessage(ShaderGlobals* sg, ulong source, ulong name, TypeDesc type, void* val, bool derivatives)
        {
            if (OslStrings.from_hash(source).@string() != "trace")
            {
                return false;
            }
            OSLShadeState state = State(sg);
            if (state == null)
            {
                return false;
            }
            string key = OslStrings.from_hash(name).@string();
            if (key == "hit" && type == TypeDescs.TypeInt)
            {
                *(int*)val = state.traceHit ? 1 : 0;
                return true;
            }
            if (!state.traceHit)
            {
                return false;
            }
            SurfaceInteraction hit = state.traceInteraction;
            switch (key)
            {
                case "hitdist":
                    return ReturnFloat(type, val, derivatives, state.traceDistance);
                case "geom:name":
                {
                    if (type != TypeDescs.TypeString)
                    {
                        return false;
                    }
                    OSLObjectInfo info = OSLScene.FindObject(hit.objectId);
                    *(ulong*)val = new ustring(info != null ? info.name : "").hash();
                    return true;
                }
                case "P":
                {
                    Point3f w = OSLScene.WorldFromRender(hit.p());
                    return ReturnVec3(type, val, derivatives, new Vec3(w.x, w.y, w.z));
                }
                case "N":
                {
                    Normal3f w = Normalize(OSLScene.WorldFromRender(hit.shading.n));
                    return ReturnVec3(type, val, derivatives, new Vec3(w.x, w.y, w.z));
                }
                case "Ng":
                {
                    Normal3f w = Normalize(OSLScene.WorldFromRender(hit.n));
                    return ReturnVec3(type, val, derivatives, new Vec3(w.x, w.y, w.z));
                }
                case "u":
                    return ReturnFloat(type, val, derivatives, hit.uv.x);
                case "v":
                    return ReturnFloat(type, val, derivatives, hit.uv.y);
                default:
                    return false;
            }
        }
    }
}
