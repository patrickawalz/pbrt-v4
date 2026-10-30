// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// cpu/render.h / cpu/render.cpp

using System;
using System.Collections.Generic;

namespace pbrt
{

    public static unsafe partial class pbrt
    {
        public static void RenderCPU(BasicScene parsedScene)
        {
            Allocator alloc = new Allocator();

            // Create media first (so have them for the camera...)
            SortedDictionary<string, Medium> media = parsedScene.CreateMedia();

            // Textures
            LOG_VERBOSE("Starting textures");
            NamedTextures textures = parsedScene.CreateTextures();
            LOG_VERBOSE("Finished textures");

            // (not in pbrt-v4) Texture media driven by a named texture get it now that the textures exist
            foreach (KeyValuePair<string, Medium> m in media)
                if (m.Value is TextureMedium textureMedium)
                    textureMedium.ResolveTextures(textures, new FileLoc());

            // Lights
            Dictionary<int, List<Light>> shapeIndexToAreaLights = new Dictionary<int, List<Light>>();
            List<Light> lights = parsedScene.CreateLights(textures, shapeIndexToAreaLights);

            LOG_VERBOSE("Starting materials");
            SortedDictionary<string, Material> namedMaterials = new SortedDictionary<string, Material>(StringComparer.Ordinal);
            List<Material> materials = new List<Material>();
            parsedScene.CreateMaterials(textures, namedMaterials, materials);
            LOG_VERBOSE("Finished materials");

            // (CreateAggregate consumes parsedScene.shapes / animatedShapes, so the media-warning scan
            // below runs on copies taken here.)
            bool haveScatteringMedia = false;
            foreach (ShapeSceneEntity sh in parsedScene.shapes)
                if (sh.insideMedium.Length != 0 || sh.outsideMedium.Length != 0)
                    haveScatteringMedia = true;
            foreach (AnimatedShapeSceneEntity sh in parsedScene.animatedShapes)
                if (sh.insideMedium.Length != 0 || sh.outsideMedium.Length != 0)
                    haveScatteringMedia = true;

            Primitive accel = parsedScene.CreateAggregate(textures, shapeIndexToAreaLights, media,
                                                          namedMaterials, materials);

            Camera camera = parsedScene.GetCamera();
            Film film = camera.GetFilm();
            Sampler sampler = parsedScene.GetSampler();

            // Integrator
            LOG_VERBOSE("Starting to create integrator");
            Integrator integrator = parsedScene.CreateIntegrator(camera, sampler, accel, lights);
            LOG_VERBOSE("Finished creating integrator");

            // Helpful warnings
            if (haveScatteringMedia && parsedScene.integrator.name != "volpath" &&
                parsedScene.integrator.name != "simplevolpath" &&
                parsedScene.integrator.name != "bdpt" && parsedScene.integrator.name != "mlt")
                Warning(StringPrintf("Scene has scattering media but \"%s\" integrator doesn't support " +
                                     "volume scattering. Consider using \"volpath\", \"simplevolpath\", " +
                                     "\"bdpt\", or \"mlt\".",
                                     parsedScene.integrator.name));

            bool haveLights = lights.Count != 0;
            foreach (KeyValuePair<string, Medium> m in media)
                haveLights |= m.Value.IsEmissive();

            if (!haveLights && parsedScene.integrator.name != "ambientocclusion" &&
                parsedScene.integrator.name != "aov")
                Warning("No light sources defined in scene; rendering a black image.");

            if (film is GBufferFilm && !(parsedScene.integrator.name == "path" ||
                                         parsedScene.integrator.name == "volpath"))
                Warning(StringPrintf("GBufferFilm is not supported by the \"%s\" integrator. The channels " +
                                     "other than R, G, B will be zero.",
                                     parsedScene.integrator.name));

            bool haveSubsurface = false;
            foreach (Material mtl in materials)
                haveSubsurface |= mtl != null && mtl.HasSubsurfaceScattering();
            foreach (KeyValuePair<string, Material> namedMtl in namedMaterials)
                haveSubsurface |= namedMtl.Value != null && namedMtl.Value.HasSubsurfaceScattering();

            if (haveSubsurface && parsedScene.integrator.name != "volpath")
                Warning(StringPrintf("Some objects in the scene have subsurface scattering, which is " +
                                     "not supported by the %s integrator. Use the \"volpath\" integrator " +
                                     "to render them correctly.",
                                     parsedScene.integrator.name));

            LOG_VERBOSE("Memory used after scene creation: %d", GetCurrentRSS());

            if (Options.pixelMaterial)
            {
                SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);

                CameraSample cs = new CameraSample();
                cs.pFilm = new Point2f(Options.pixelMaterial.value().x, Options.pixelMaterial.value().y) + new Vector2f(0.5f, 0.5f);
                cs.time = 0.5f;
                cs.pLens = new Point2f(0.5f, 0.5f);
                cs.filterWeight = 1;
                pstd.optional<CameraRay> cr = camera.GenerateRay(cs, ref lambda);
                if (!cr)
                    ErrorExit("Unable to generate camera ray for specified pixel.");

                int depth = 1;
                Ray ray = cr.value().ray;
                while (true)
                {
                    pstd.optional<ShapeIntersection> isect = accel.Intersect(ray, Infinity);
                    if (!isect)
                    {
                        if (depth == 1)
                            ErrorExit("No geometry visible at specified pixel.");
                        else
                            break;
                    }

                    SurfaceInteraction intr = isect.value().intr;
                    if (intr.material == null)
                        Warning("Ignoring \"interface\" material at intersection.");
                    else
                    {
                        Transform worldFromRender = camera.GetCameraTransform().WorldFromRender();
                        Printf("Intersection depth %d\n", depth);
                        Printf("World-space p: %s\n", worldFromRender.Apply(intr.p()));
                        Printf("World-space n: %s\n", worldFromRender.Apply(intr.n));
                        Printf("World-space ns: %s\n", worldFromRender.Apply(intr.shading.n));
                        Printf("Distance from camera: %f\n", Distance(intr.p(), cr.value().ray.o));

                        bool isNamed = false;
                        foreach (KeyValuePair<string, Material> mtl in namedMaterials)
                            if (mtl.Value == intr.material)
                            {
                                Printf("Named material: %s\n\n", mtl.Key);
                                isNamed = true;
                                break;
                            }
                        if (!isNamed)
                            // If we didn't find a named material, dump out the whole thing.
                            Printf("%s\n\n", intr.material.ToString());

                        ++depth;
                        {
                            RayDifferential rd = intr.SpawnRay(ray.d);
                            ray = new Ray(rd.o, rd.d, rd.time, rd.medium);
                        }
                    }
                }

                return;
            }

            // Render!
            integrator.Render();

            LOG_VERBOSE("Memory used after rendering: %s", GetCurrentRSS());

            PtexTextureBase.ReportStats();
            ImageTextureBase.ClearCache();
        }
    }

}  // namespace pbrt
