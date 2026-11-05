// (not in pbrt-v4) Open Shading Language integration: what a shader can ask the renderer about the scene. The scene file parser and the scene
// builder register the transformations (render space, named coordinate systems, the objects of the shapes), the camera and the aggregate here,
// and OSLRendererServices answers get_matrix(), getattribute() and trace() from it.
//
// "common" space of OSL is the world space of the scene file. pbrt renders in its own render space (camera-world by default), so every
// position and direction crosses WorldFromRender / RenderFromWorld on its way into and out of a shader.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace pbrt
{
    /// What the shader gets to know about the object that is shaded (object:name, object2common, object:surfacearea)
    public sealed class OSLObjectInfo
    {
        public string name;
        public Transform renderFromObject;
        public AnimatedTransform animatedRenderFromObject;
        public Float area;
    }

    public static unsafe class OSLScene
    {
        /// Set when a scene uses OSL; the scene builder skips the registrations (the areas of the shapes) otherwise
        public static bool Enabled = false;

        public static Transform renderFromWorld = new Transform();
        public static Transform worldFromRender = new Transform();

        public static Float shutterOpen = 0, shutterClose = 1;
        public static Point2i resolution = new Point2i(1280, 720);
        public static string cameraName = "perspective";
        public static Float cameraFov = 90;
        public static Float clipNear = 1e-2f, clipFar = 1e5f;
        public static Float pixelAspect = 1;
        public static Float[] screenWindow = new Float[] { -1, -1, 1, 1 };
        public static AnimatedTransform worldFromCamera = new AnimatedTransform(new Transform());

        public static Primitive aggregate = null;

        private static readonly object mutex = new object();
        private static Dictionary<int, OSLObjectInfo> objects = new Dictionary<int, OSLObjectInfo>();
        private static Dictionary<string, AnimatedTransform> coordinateSystems = new Dictionary<string, AnimatedTransform>();

        public static void Reset()
        {
            lock (mutex)
            {
                objects.Clear();
                coordinateSystems.Clear();
                aggregate = null;
                Enabled = false;
                renderFromWorld = new Transform();
                worldFromRender = new Transform();
            }
        }

        public static void SetRenderSpace(Transform renderFromWorldIn)
        {
            renderFromWorld = renderFromWorldIn;
            worldFromRender = Inverse(renderFromWorldIn);
        }

        /// The camera of the scene file (called when the film and camera are created)
        public static void SetCamera(CameraSceneEntity camera, Point2i filmResolution)
        {
            SetRenderSpace(camera.cameraTransform.RenderFromWorld());
            resolution = filmResolution;
            cameraName = camera.name;
            cameraFov = camera.parameters.GetOneFloat("fov", 90f);
            shutterOpen = camera.parameters.GetOneFloat("shutteropen", 0f);
            shutterClose = camera.parameters.GetOneFloat("shutterclose", 1f);
            Float frame = camera.parameters.GetOneFloat("frameaspectratio", (Float)filmResolution.x / (Float)filmResolution.y);
            if (frame > 1)
            {
                screenWindow = new Float[] { -frame, -1, frame, 1 };
            }
            else
            {
                screenWindow = new Float[] { -1, -1 / frame, 1, 1 / frame };
            }
            pixelAspect = 1;
            AnimatedTransform renderFromCamera = camera.cameraTransform.RenderFromCamera();
            worldFromCamera = new AnimatedTransform(worldFromRender * renderFromCamera.Interpolate(0), 0,
                                                    worldFromRender * renderFromCamera.Interpolate(1), 1);
        }

        public static void AddCoordinateSystem(string name, Transform worldFromCS0, Transform worldFromCS1, Float t0, Float t1)
        {
            lock (mutex)
            {
                coordinateSystems[name] = new AnimatedTransform(worldFromCS0, t0, worldFromCS1, t1);
            }
        }

        public static bool FindCoordinateSystem(string name, out AnimatedTransform worldFromCS)
        {
            lock (mutex)
            {
                return coordinateSystems.TryGetValue(name, out worldFromCS);
            }
        }

        public static void AddObject(int id, string name, Transform renderFromObject, AnimatedTransform animated, Float area)
        {
            lock (mutex)
            {
                OSLObjectInfo info;
                if (objects.TryGetValue(id, out info))
                {
                    info.area += area;
                    return;
                }
                info = new OSLObjectInfo();
                info.name = name;
                info.renderFromObject = renderFromObject;
                info.animatedRenderFromObject = animated;
                info.area = area;
                objects[id] = info;
            }
        }

        public static OSLObjectInfo FindObject(int id)
        {
            OSLObjectInfo info;
            lock (mutex)
            {
                if (objects.TryGetValue(id, out info))
                {
                    return info;
                }
            }
            return null;
        }

        // Render space <-> world space, for the points, vectors and normals of a shader
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f WorldFromRender(Point3f p)
        {
            return worldFromRender.Apply(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f WorldFromRender(Vector3f v)
        {
            return worldFromRender.Apply(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f WorldFromRender(Normal3f n)
        {
            return worldFromRender.Apply(n);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point3f RenderFromWorld(Point3f p)
        {
            return renderFromWorld.Apply(p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3f RenderFromWorld(Vector3f v)
        {
            return renderFromWorld.Apply(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Normal3f RenderFromWorld(Normal3f n)
        {
            return renderFromWorld.Apply(n);
        }
    }
}
