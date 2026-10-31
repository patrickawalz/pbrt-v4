// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// scene.h / scene.cpp
//
// Notes: the scene entity structs are classes; InternedString is string and the transform cache
// is not needed since Transform is a value type; std::mutex-guarded futures use AsyncJob.

using System;
using System.Collections.Generic;
using System.Threading;

namespace pbrt
{

    // SceneEntity Definition
    public unsafe class SceneEntity
    {
        // SceneEntity Public Methods
        public SceneEntity() { parameters = new ParameterDictionary(); loc = new FileLoc(); }
        public SceneEntity(string name, ParameterDictionary parameters, FileLoc loc)
        {
            this.name = name;
            this.parameters = parameters;
            this.loc = loc;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SceneEntity name: %s parameters: %s loc: %s ]", name, parameters, loc);
        }

        // SceneEntity Public Members
        public string name = "";
        public FileLoc loc;
        public ParameterDictionary parameters;
    }

    public unsafe class TransformedSceneEntity : SceneEntity
    {
        public TransformedSceneEntity() { renderFromObject = new AnimatedTransform(); }
        public TransformedSceneEntity(string name, ParameterDictionary parameters, FileLoc loc,
                                      AnimatedTransform renderFromObject)
            : base(name, parameters, loc)
        {
            this.renderFromObject = renderFromObject;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ TransformedSeneEntity name: %s parameters: %s loc: %s renderFromObject: %s ]",
                                        name, parameters, loc, renderFromObject);
        }

        public AnimatedTransform renderFromObject;
    }

    // CameraSceneEntity Definition
    public unsafe class CameraSceneEntity : SceneEntity
    {
        // CameraSceneEntity Public Methods
        public CameraSceneEntity() { }
        public CameraSceneEntity(string name, ParameterDictionary parameters, FileLoc loc,
                                 CameraTransform cameraTransform, string medium)
            : base(name, parameters, loc)
        {
            this.cameraTransform = cameraTransform;
            this.medium = medium;
        }

        public CameraTransform cameraTransform;
        public string medium = "";
    }

    public unsafe class ShapeSceneEntity : SceneEntity
    {
        public ShapeSceneEntity() { }
        public ShapeSceneEntity(string name, ParameterDictionary parameters, FileLoc loc,
                                Transform renderFromObject, Transform objectFromRender,
                                bool reverseOrientation, int materialIndex, string materialName,
                                int lightIndex, string insideMedium, string outsideMedium)
            : base(name, parameters, loc)
        {
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            this.materialIndex = materialIndex;
            this.materialName = materialName;
            this.lightIndex = lightIndex;
            this.insideMedium = insideMedium;
            this.outsideMedium = outsideMedium;
        }

        public Transform renderFromObject = new Transform(), objectFromRender = new Transform();
        public bool reverseOrientation = false;
        public int materialIndex;
        public string materialName = "";
        public int lightIndex = -1;
        public string insideMedium = "", outsideMedium = "";
    }

    public unsafe class AnimatedShapeSceneEntity : TransformedSceneEntity
    {
        public AnimatedShapeSceneEntity() { }
        public AnimatedShapeSceneEntity(string name, ParameterDictionary parameters, FileLoc loc,
                                        AnimatedTransform renderFromObject, Transform identity,
                                        bool reverseOrientation, int materialIndex, string materialName,
                                        int lightIndex, string insideMedium, string outsideMedium)
            : base(name, parameters, loc, renderFromObject)
        {
            this.identity = identity;
            this.reverseOrientation = reverseOrientation;
            this.materialIndex = materialIndex;
            this.materialName = materialName;
            this.lightIndex = lightIndex;
            this.insideMedium = insideMedium;
            this.outsideMedium = outsideMedium;
        }

        public Transform identity = new Transform();
        public bool reverseOrientation = false;
        public int materialIndex;
        public string materialName = "";
        public int lightIndex = -1;
        public string insideMedium = "", outsideMedium = "";
    }

    public unsafe class InstanceDefinitionSceneEntity
    {
        public InstanceDefinitionSceneEntity() { }
        public InstanceDefinitionSceneEntity(string name, FileLoc loc)
        {
            this.name = name;
            this.loc = loc;
        }

        public string name = "";
        public FileLoc loc;
        public List<ShapeSceneEntity> shapes = new List<ShapeSceneEntity>();
        public List<AnimatedShapeSceneEntity> animatedShapes = new List<AnimatedShapeSceneEntity>();
    }

    // (using MediumSceneEntity = TransformedSceneEntity; using TextureSceneEntity = TransformedSceneEntity;)

    public unsafe class LightSceneEntity : TransformedSceneEntity
    {
        public LightSceneEntity() { }
        public LightSceneEntity(string name, ParameterDictionary parameters, FileLoc loc,
                                AnimatedTransform renderFromLight, string medium)
            : base(name, parameters, loc, renderFromLight)
        {
            this.medium = medium;
        }

        public string medium = "";
    }

    public unsafe class InstanceSceneEntity
    {
        public InstanceSceneEntity() { }
        public InstanceSceneEntity(string n, FileLoc loc, AnimatedTransform renderFromInstanceAnim)
        {
            name = n;
            this.loc = loc;
            this.renderFromInstanceAnim = renderFromInstanceAnim;
            CHECK(this.renderFromInstanceAnim.IsAnimated());
        }
        public InstanceSceneEntity(string n, FileLoc loc, Transform renderFromInstance)
        {
            name = n;
            this.loc = loc;
            this.renderFromInstance = renderFromInstance;
            hasRenderFromInstance = true;
        }

        public string name = "";
        public FileLoc loc;
        public AnimatedTransform renderFromInstanceAnim = null;
        public Transform renderFromInstance = new Transform();
        public bool hasRenderFromInstance = false;
    }

    // TransformSet Definition
    public unsafe class TransformSet
    {
        public const int MaxTransforms = 2;

        public TransformSet()
        {
            t = new Transform[MaxTransforms];
            for (int i = 0; i < MaxTransforms; ++i)
                t[i] = new Transform();
        }

        // TransformSet Public Methods
        public Transform this[int i]
        {
            get
            {
                CHECK_GE(i, 0);
                CHECK_LT(i, MaxTransforms);
                return t[i];
            }
            set
            {
                CHECK_GE(i, 0);
                CHECK_LT(i, MaxTransforms);
                t[i] = value;
            }
        }
        public static TransformSet Inverse(TransformSet ts)
        {
            TransformSet tInv = new TransformSet();
            for (int i = 0; i < MaxTransforms; ++i)
                tInv.t[i] = pbrt.Inverse(ts.t[i]);
            return tInv;
        }
        public bool IsAnimated()
        {
            for (int i = 0; i < MaxTransforms - 1; ++i)
                if (t[i] != t[i + 1])
                    return true;
            return false;
        }
        public TransformSet Clone()
        {
            TransformSet c = new TransformSet();
            for (int i = 0; i < MaxTransforms; ++i)
                c.t[i] = t[i];
            return c;
        }

        Transform[] t;
    }

    // BasicScene Definition
    public unsafe class BasicScene
    {

        public Camera GetCamera()
        {
            lock (cameraJobMutex)
            {
                if (camera == null)
                    camera = cameraJob.GetResult();
            }
            LOG_VERBOSE("Retrieved Camera from future");
            return camera;
        }

        public Sampler GetSampler()
        {
            lock (samplerJobMutex)
            {
                if (sampler == null)
                    sampler = samplerJob.GetResult();
            }
            LOG_VERBOSE("Retrieved Sampler from future");
            return sampler;
        }

        public void SetOptions(SceneEntity filter, SceneEntity film, CameraSceneEntity camera,
                               SceneEntity sampler, SceneEntity integ, SceneEntity accel)
        {
            // Store information for specified integrator and accelerator
            filmColorSpace = film.parameters.ColorSpace();
            integrator = integ;
            accelerator = accel;

            // Immediately create filter and film
            LOG_VERBOSE("Starting to create filter and film");
            Allocator alloc = new Allocator();
            Filter filt = Filter.Create(filter.name, filter.parameters, filter.loc, alloc);

            // It's a little ugly to poke into the camera's parameters here, but we
            // have this circular dependency that Camera::Create() expects a
            // Film, yet now the film needs to know the exposure time from
            // the camera....
            Float exposureTime = camera.parameters.GetOneFloat("shutterclose", 1f) -
                                 camera.parameters.GetOneFloat("shutteropen", 0f);
            if (exposureTime <= 0)
                ErrorExit(camera.loc,
                          "The specified camera shutter times imply that the shutter " +
                          "does not open.  A black image will result.");

            this.film = Film.Create(film.name, film.parameters, exposureTime,
                                    camera.cameraTransform, filt, film.loc, alloc);
            LOG_VERBOSE("Finished creating filter and film");

            // Enqueue asynchronous job to create sampler
            samplerJob = RunAsync<Sampler>(() =>
            {
                LOG_VERBOSE("Starting to create sampler");
                Allocator a = new Allocator();
                Point2i res = this.film.FullResolution();
                return Sampler.Create(sampler.name, sampler.parameters, res, sampler.loc, a);
            });

            // Enqueue asynchronous job to create camera
            cameraJob = RunAsync<Camera>(() =>
            {
                LOG_VERBOSE("Starting to create camera");
                Allocator a = new Allocator();
                Medium cameraMedium = GetMedium(camera.medium, camera.loc);

                Camera c = Camera.Create(camera.name, camera.parameters, cameraMedium,
                                         camera.cameraTransform, this.film, camera.loc, a);
                LOG_VERBOSE("Finished creating camera");
                return c;
            });
        }

        public void AddMedium(TransformedSceneEntity medium)
        {
            // Define _create_ lambda function for _Medium_ creation
            Func<Medium> create = () =>
            {
                string type = medium.parameters.GetOneString("type", "");
                // Check for missing medium ``type'' or animated medium transform
                if (type.Length == 0)
                    ErrorExit(medium.loc, "No parameter \"string type\" found for medium.");
                if (medium.renderFromObject.IsAnimated())
                    Warning(medium.loc, "Animated transformation provided for medium. Only the " +
                                        "start transform will be used.");

                return Medium.Create(type, medium.parameters, medium.renderFromObject.startTransform,
                                     medium.loc, new Allocator());
            };

            lock (mediaMutex)
            {
                mediumJobs[medium.name] = RunAsync<Medium>(create);
            }
        }

        Medium GetMedium(string name, FileLoc loc)
        {
            if (name.Length == 0)
                return null;

            AsyncJob<Medium> job;
            lock (mediaMutex)
            {
                if (mediaMap.TryGetValue(name, out Medium m))
                    return m;
                if (!mediumJobs.TryGetValue(name, out job))
                    ErrorExit(loc, StringPrintf("%s: medium is not defined.", name));
            }
            Medium result = job.GetResult();
            lock (mediaMutex)
            {
                mediaMap[name] = result;
                mediumJobs.Remove(name);
            }
            return result;
        }

        public SortedDictionary<string, Medium> CreateMedia()
        {
            lock (mediaMutex)
            {
                if (mediumJobs.Count != 0)
                {
                    // Consume results for asynchronously-created _Medium_ objects
                    LOG_VERBOSE("Consume media futures start");
                    foreach (KeyValuePair<string, AsyncJob<Medium>> m in mediumJobs)
                        if (!mediaMap.ContainsKey(m.Key))
                            mediaMap[m.Key] = m.Value.GetResult();
                    LOG_VERBOSE("Consume media futures finished");
                    mediumJobs.Clear();
                }
                return new SortedDictionary<string, Medium>(mediaMap, StringComparer.Ordinal);
            }
        }

        public Integrator CreateIntegrator(Camera camera, Sampler sampler, Primitive accel, List<Light> lights)
        {
            return Integrator.Create(integrator.name, integrator.parameters, camera, sampler,
                                     accel, lights, filmColorSpace, integrator.loc);
        }
        // BasicScene Public Methods
        public BasicScene() { }

        public void AddNamedMaterial(string name, SceneEntity material)
        {
            lock (materialMutex)
            {
                startLoadingNormalMaps(material.parameters);
                namedMaterials.Add((name, material));
            }
        }

        public int AddMaterial(SceneEntity material)
        {
            lock (materialMutex)
            {
                startLoadingNormalMaps(material.parameters);
                materials.Add(material);
                return materials.Count - 1;
            }
        }

        void startLoadingNormalMaps(ParameterDictionary parameters)
        {
            string filename = ResolveFilename(parameters.GetOneString("normalmap", ""));
            if (filename.Length == 0)
                return;

            // Overload materialMutex, which we already hold, for the futures...
            if (normalMapJobs.ContainsKey(filename))
                // It's already in flight.
                return;

            Func<string, Image> create = (string fn) =>
            {
                Allocator alloc = new Allocator();
                ImageAndMetadata immeta = Image.Read(fn, new Allocator(), ColorEncoding.Linear);
                Image image = immeta.image;
                ImageChannelDesc rgbDesc = image.GetChannelDesc(new string[] { "R", "G", "B" });
                if (!rgbDesc)
                    ErrorExit(StringPrintf("%s: normal map image must contain R, G, and B channels", fn));
                Image normalMap = image.SelectChannels(rgbDesc);
                return normalMap;
            };
            normalMapJobs[filename] = RunAsync<string, Image>(create, filename);
        }

        public void AddFloatTexture(string name, TransformedSceneEntity texture)
        {
            if (texture.renderFromObject.IsAnimated())
                Warning(texture.loc, "Animated world to texture transforms are not supported. " +
                                     "Using start transform.");

            lock (textureMutex)
            {
                if (texture.name != "imagemap" && texture.name != "ptex")
                {
                    serialFloatTextures.Add((name, texture));
                    return;
                }

                string filename = ResolveFilename(texture.parameters.GetOneString("filename", ""));
                if (filename.Length == 0)
                {
                    Error(texture.loc, "\"string filename\" not provided for image texture.");
                    ++nMissingTextures;
                    return;
                }
                if (!FileExists(filename))
                {
                    Error(texture.loc, StringPrintf("%s: file not found.", filename));
                    ++nMissingTextures;
                    return;
                }

                if (loadingTextureFilenames.Contains(filename))
                {
                    serialFloatTextures.Add((name, texture));
                    return;
                }
                loadingTextureFilenames.Add(filename);

                Func<TransformedSceneEntity, FloatTexture> create = (TransformedSceneEntity tex) =>
                {
                    Allocator alloc = new Allocator();

                    Transform renderFromTexture = tex.renderFromObject.startTransform;
                    // Pass nullptr for the textures, since they shouldn't be accessed
                    // anyway.
                    TextureParameterDictionary texDict = new TextureParameterDictionary(tex.parameters, null);
                    return FloatTexture.Create(tex.name, renderFromTexture, texDict, tex.loc, alloc,
                                               Options.useGPU);
                };
                floatTextureJobs[name] = RunAsync<TransformedSceneEntity, FloatTexture>(create, texture);
            }
        }

        public void AddSpectrumTexture(string name, TransformedSceneEntity texture)
        {
            lock (textureMutex)
            {
                if (texture.name != "imagemap" && texture.name != "ptex")
                {
                    serialSpectrumTextures.Add((name, texture));
                    return;
                }

                string filename = ResolveFilename(texture.parameters.GetOneString("filename", ""));
                if (filename.Length == 0)
                {
                    Error(texture.loc, "\"string filename\" not provided for image texture.");
                    ++nMissingTextures;
                    return;
                }
                if (!FileExists(filename))
                {
                    Error(texture.loc, StringPrintf("%s: file not found.", filename));
                    ++nMissingTextures;
                    return;
                }

                if (loadingTextureFilenames.Contains(filename))
                {
                    serialSpectrumTextures.Add((name, texture));
                    return;
                }
                loadingTextureFilenames.Add(filename);

                asyncSpectrumTextures.Add((name, texture));

                Func<TransformedSceneEntity, SpectrumTexture> create = (TransformedSceneEntity tex) =>
                {
                    Allocator alloc = new Allocator();

                    Transform renderFromTexture = tex.renderFromObject.startTransform;
                    // nullptr for the textures, as with float textures.
                    TextureParameterDictionary texDict = new TextureParameterDictionary(tex.parameters, null);
                    // Only create SpectrumType::Albedo for now; will get the other two
                    // types in CreateTextures().
                    return SpectrumTexture.Create(tex.name, renderFromTexture, texDict, SpectrumType.Albedo,
                                                  tex.loc, alloc, Options.useGPU);
                };
                spectrumTextureJobs[name] = RunAsync<TransformedSceneEntity, SpectrumTexture>(create, texture);
            }
        }

        public void AddLight(LightSceneEntity light)
        {
            Medium lightMedium = GetMedium(light.medium, light.loc);
            lock (lightMutex)
            {
                if (light.renderFromObject.IsAnimated())
                    Warning(light.loc, "Animated lights aren't supported. Using the start transform.");

                Func<Light> create = () =>
                {
                    return Light.Create(light.name, light.parameters, light.renderFromObject.startTransform,
                                        GetCamera().GetCameraTransform(), lightMedium, light.loc,
                                        new Allocator());
                };
                lightJobs.Add(RunAsync<Light>(create));
            }
        }

        public int AddAreaLight(SceneEntity light)
        {
            lock (areaLightMutex)
            {
                areaLights.Add(light);
                return areaLights.Count - 1;
            }
        }

        public void AddShapes(List<ShapeSceneEntity> s)
        {
            lock (shapeMutex)
            {
                shapes.AddRange(s);
            }
        }

        public void AddAnimatedShape(AnimatedShapeSceneEntity shape)
        {
            lock (animatedShapeMutex)
            {
                animatedShapes.Add(shape);
            }
        }

        public void AddInstanceDefinition(InstanceDefinitionSceneEntity instance)
        {
            lock (instanceDefinitionMutex)
            {
                instanceDefinitions[instance.name] = instance;
            }
        }

        public void AddInstanceUses(List<InstanceSceneEntity> inst)
        {
            lock (instanceUseMutex)
            {
                instances.AddRange(inst);
            }
        }

        public void Done()
        {
            // (the unused-texture reporting is disabled in pbrt-v4 as well: "#if 0")
        }

        public void CreateMaterials(NamedTextures textures,
                                    SortedDictionary<string, Material> namedMaterialsOut,
                                    List<Material> materialsOut)
        {
            LOG_VERBOSE("Starting to consume %d normal map futures", normalMapJobs.Count);
            lock (materialMutex)
            {
                foreach (KeyValuePair<string, AsyncJob<Image>> job in normalMapJobs)
                {
                    CHECK(!normalMaps.ContainsKey(job.Key));
                    normalMaps[job.Key] = job.Value.GetResult();
                }
                normalMapJobs.Clear();
                LOG_VERBOSE("Finished consuming normal map futures");

                // Named materials
                foreach ((string name, SceneEntity mtl) in namedMaterials)
                {
                    Allocator alloc = new Allocator();

                    if (namedMaterialsOut.ContainsKey(name))
                    {
                        ErrorExit(mtl.loc, StringPrintf("%s: trying to redefine named material.", name));
                        continue;
                    }

                    string type = mtl.parameters.GetOneString("type", "");
                    if (type.Length == 0)
                    {
                        ErrorExit(mtl.loc,
                                  StringPrintf("%s: \"string type\" not provided in named material's parameters.", name));
                        continue;
                    }

                    string fn = ResolveFilename(mtl.parameters.GetOneString("normalmap", ""));
                    Image normalMap = null;
                    if (fn.Length != 0)
                    {
                        CHECK(normalMaps.ContainsKey(fn));
                        normalMap = normalMaps[fn];
                    }

                    TextureParameterDictionary texDict = new TextureParameterDictionary(mtl.parameters, textures);
                    Material m = Material.Create(type, texDict, normalMap, namedMaterialsOut, mtl.loc, alloc);
                    namedMaterialsOut[name] = m;
                }

                // Regular materials
                foreach (SceneEntity mtl in materials)
                {
                    Allocator alloc = new Allocator();
                    string fn = ResolveFilename(mtl.parameters.GetOneString("normalmap", ""));
                    Image normalMap = null;
                    if (fn.Length != 0)
                    {
                        CHECK(normalMaps.ContainsKey(fn));
                        normalMap = normalMaps[fn];
                    }

                    TextureParameterDictionary texDict = new TextureParameterDictionary(mtl.parameters, textures);
                    Material m = Material.Create(mtl.name, texDict, normalMap, namedMaterialsOut, mtl.loc, alloc);
                    materialsOut.Add(m);
                }
            }
        }

        public NamedTextures CreateTextures()
        {
            NamedTextures textures = new NamedTextures();

            if (nMissingTextures > 0)
                ErrorExit(StringPrintf("%d missing textures", nMissingTextures));

            // Consume futures
            LOG_VERBOSE("Starting to consume texture futures");
            lock (textureMutex)
            {
                foreach (KeyValuePair<string, AsyncJob<FloatTexture>> tex in floatTextureJobs)
                    textures.floatTextures[tex.Key] = tex.Value.GetResult();
                floatTextureJobs.Clear();
                foreach (KeyValuePair<string, AsyncJob<SpectrumTexture>> tex in spectrumTextureJobs)
                    textures.albedoSpectrumTextures[tex.Key] = tex.Value.GetResult();
                spectrumTextureJobs.Clear();
            }
            LOG_VERBOSE("Finished consuming texture futures");

            LOG_VERBOSE("Starting to create remaining textures");
            Allocator alloc = new Allocator();
            // Create the other SpectrumTypes for the spectrum textures.
            foreach ((string tname, TransformedSceneEntity tex) in asyncSpectrumTextures)
            {
                Transform renderFromTexture = tex.renderFromObject.startTransform;
                // These are all image textures, so nullptr is fine for the
                // textures, as earlier.
                TextureParameterDictionary texDict = new TextureParameterDictionary(tex.parameters, null);

                // These should be fast since they should hit the texture cache
                SpectrumTexture unboundedTex = SpectrumTexture.Create(
                    tex.name, renderFromTexture, texDict, SpectrumType.Unbounded, tex.loc, alloc, Options.useGPU);
                SpectrumTexture illumTex = SpectrumTexture.Create(
                    tex.name, renderFromTexture, texDict, SpectrumType.Illuminant, tex.loc, alloc, Options.useGPU);

                textures.unboundedSpectrumTextures[tname] = unboundedTex;
                textures.illuminantSpectrumTextures[tname] = illumTex;
            }

            // And do the rest serially
            foreach ((string tname, TransformedSceneEntity tex) in serialFloatTextures)
            {
                Transform renderFromTexture = tex.renderFromObject.startTransform;
                TextureParameterDictionary texDict = new TextureParameterDictionary(tex.parameters, textures);
                FloatTexture t = FloatTexture.Create(tex.name, renderFromTexture, texDict, tex.loc, alloc,
                                                     Options.useGPU);
                textures.floatTextures[tname] = t;
            }

            foreach ((string tname, TransformedSceneEntity tex) in serialSpectrumTextures)
            {
                if (tex.renderFromObject.IsAnimated())
                    Warning(tex.loc, "Animated world to texture transform not supported. " +
                                     "Using start transform.");

                Transform renderFromTexture = tex.renderFromObject.startTransform;
                TextureParameterDictionary texDict = new TextureParameterDictionary(tex.parameters, textures);
                SpectrumTexture albedoTex = SpectrumTexture.Create(
                    tex.name, renderFromTexture, texDict, SpectrumType.Albedo, tex.loc, alloc, Options.useGPU);
                SpectrumTexture unboundedTex = SpectrumTexture.Create(
                    tex.name, renderFromTexture, texDict, SpectrumType.Unbounded, tex.loc, alloc, Options.useGPU);
                SpectrumTexture illumTex = SpectrumTexture.Create(
                    tex.name, renderFromTexture, texDict, SpectrumType.Illuminant, tex.loc, alloc, Options.useGPU);

                textures.albedoSpectrumTextures[tname] = albedoTex;
                textures.unboundedSpectrumTextures[tname] = unboundedTex;
                textures.illuminantSpectrumTextures[tname] = illumTex;
            }

            LOG_VERBOSE("Done creating textures");
            return textures;
        }

        public List<Light> CreateLights(NamedTextures textures,
                                        Dictionary<int, List<Light>> shapeIndexToAreaLights)
        {
            Func<string, FileLoc, Medium> findMedium = (string s, FileLoc loc) =>
            {
                if (s.Length == 0)
                    return null;

                if (!mediaMap.TryGetValue(s, out Medium m))
                    ErrorExit(loc, StringPrintf("%s: medium not defined", s));
                return m;
            };

            Allocator alloc = new Allocator();

            Func<ParameterDictionary, FileLoc, FloatTexture> getAlphaTexture =
                (ParameterDictionary parameters, FileLoc loc) =>
                {
                    string alphaTexName = parameters.GetTexture("alpha");
                    if (alphaTexName.Length != 0)
                    {
                        if (textures.floatTextures.TryGetValue(alphaTexName, out FloatTexture ft))
                            return ft;
                        else
                            ErrorExit(loc, StringPrintf("%s: couldn't find float texture for \"alpha\" parameter.",
                                                        alphaTexName));
                        return null;
                    }
                    Float alpha = parameters.GetOneFloat("alpha", 1f);
                    if (alpha < 1f)
                        return new FloatConstantTexture(alpha);
                    return null;
                };

            LOG_VERBOSE("Starting area lights");
            List<Light> lights = new List<Light>();
            // Area Lights
            for (int i = 0; i < shapes.Count; ++i)
            {
                ShapeSceneEntity sh = shapes[i];

                if (sh.lightIndex == -1)
                    continue;

                string materialName;
                if (sh.materialName.Length != 0)
                {
                    int idx = namedMaterials.FindIndex(((string, SceneEntity) p) => p.Item1 == sh.materialName);
                    if (idx < 0)
                        ErrorExit(sh.loc, StringPrintf("%s: no named material defined.", sh.materialName));
                    CHECK(namedMaterials[idx].Item2.parameters.GetStringArray("type").Count > 0);
                    materialName = namedMaterials[idx].Item2.parameters.GetOneString("type", "");
                }
                else
                {
                    CHECK_LT(sh.materialIndex, materials.Count);
                    materialName = materials[sh.materialIndex].name;
                }
                if (materialName == "interface" || materialName == "none" || materialName == "")
                {
                    Warning(sh.loc, "Ignoring area light specification for shape " +
                                    "with \"interface\" material.");
                    continue;
                }

                pstd.vector<Shape> shapeObjects = Shape.Create(
                    sh.name, sh.renderFromObject, sh.objectFromRender, sh.reverseOrientation,
                    sh.parameters, textures.floatTextures, sh.loc, alloc);

                FloatTexture alphaTex = getAlphaTexture(sh.parameters, sh.loc);

                MediumInterface mi = new MediumInterface(findMedium(sh.insideMedium, sh.loc),
                                                         findMedium(sh.outsideMedium, sh.loc),
                                                         sh.parameters.GetOneInt("mediumpriority", MediumInterface.noPriority));

                List<Light> shapeLights = new List<Light>();
                SceneEntity areaLightEntity = areaLights[sh.lightIndex];
                for (ulong k = 0; k < shapeObjects.size(); ++k)
                {
                    Shape ps = shapeObjects[k];
                    Light area = Light.CreateArea(areaLightEntity.name, areaLightEntity.parameters,
                                                  sh.renderFromObject, mi, ps, alphaTex,
                                                  areaLightEntity.loc, alloc);
                    if (area != null)
                    {
                        lights.Add(area);
                        shapeLights.Add(area);
                    }
                }

                shapeIndexToAreaLights[i] = shapeLights;
            }

            LOG_VERBOSE("Finished area lights");

            LOG_VERBOSE("Starting to consume non-area light futures");
            lock (lightMutex)
            {
                foreach (AsyncJob<Light> job in lightJobs)
                    lights.Add(job.GetResult());
            }
            LOG_VERBOSE("Finished consuming non-area light futures");

            return lights;
        }

        public Primitive CreateAggregate(NamedTextures textures,
                                         Dictionary<int, List<Light>> shapeIndexToAreaLights,
                                         SortedDictionary<string, Medium> media,
                                         SortedDictionary<string, Material> namedMaterialsIn,
                                         List<Material> materialsIn)
        {
            Allocator alloc = new Allocator();
            Func<string, FileLoc, Medium> findMedium = (string s, FileLoc loc) =>
            {
                if (s.Length == 0)
                    return null;

                if (!media.TryGetValue(s, out Medium m))
                    ErrorExit(loc, StringPrintf("%s: medium not defined", s));
                return m;
            };

            // Primitives
            Func<ParameterDictionary, FileLoc, FloatTexture> getAlphaTexture =
                (ParameterDictionary parameters, FileLoc loc) =>
                {
                    string alphaTexName = parameters.GetTexture("alpha");
                    if (alphaTexName.Length != 0)
                    {
                        if (textures.floatTextures.TryGetValue(alphaTexName, out FloatTexture ft))
                            return ft;
                        else
                            ErrorExit(loc, StringPrintf("%s: couldn't find float texture for \"alpha\" parameter.",
                                                        alphaTexName));
                        return null;
                    }
                    Float alpha = parameters.GetOneFloat("alpha", 1f);
                    if (alpha < 1f)
                        return new FloatConstantTexture(alpha);
                    return null;
                };

            // Non-animated shapes
            Func<List<ShapeSceneEntity>, List<Primitive>> CreatePrimitivesForShapes =
                (List<ShapeSceneEntity> shapesIn) =>
                {
                    // Parallelize Shape::Create calls, which will in turn
                    // parallelize PLY file loading, etc...
                    pstd.vector<Shape>[] shapeVectors = new pstd.vector<Shape>[shapesIn.Count];
                    ParallelFor(0, shapesIn.Count, (long i) =>
                    {
                        ShapeSceneEntity sh = shapesIn[(int)i];
                        shapeVectors[i] = Shape.Create(
                            sh.name, sh.renderFromObject, sh.objectFromRender, sh.reverseOrientation,
                            sh.parameters, textures.floatTextures, sh.loc, alloc);
                    });

                    List<Primitive> prims = new List<Primitive>();
                    for (int i = 0; i < shapesIn.Count; ++i)
                    {
                        ShapeSceneEntity sh = shapesIn[i];
                        pstd.vector<Shape> shapeObjs = shapeVectors[i];
                        if (shapeObjs.empty())
                            continue;

                        FloatTexture alphaTex = getAlphaTexture(sh.parameters, sh.loc);
                        int mediumPriority = sh.parameters.GetOneInt("mediumpriority", MediumInterface.noPriority);
                        sh.parameters.ReportUnused();  // do now so can grab alpha...

                        Material mtl = null;
                        if (sh.materialName.Length != 0)
                        {
                            if (!namedMaterialsIn.TryGetValue(sh.materialName, out mtl))
                                ErrorExit(sh.loc, StringPrintf("%s: no named material defined.", sh.materialName));
                        }
                        else
                        {
                            CHECK_LT(sh.materialIndex, materialsIn.Count);
                            mtl = materialsIn[sh.materialIndex];
                        }

                        MediumInterface mi = new MediumInterface(findMedium(sh.insideMedium, sh.loc),
                                                                 findMedium(sh.outsideMedium, sh.loc), mediumPriority);

                        shapeIndexToAreaLights.TryGetValue(i, out List<Light> areaList);
                        for (ulong j = 0; j < shapeObjs.size(); ++j)
                        {
                            // Possibly create area light for shape
                            Light area = null;
                            // Will not be present in the map if it has an "interface"
                            // material...
                            if (sh.lightIndex != -1 && areaList != null)
                                area = areaList[(int)j];

                            if (area == null && !mi.IsMediumTransition() && alphaTex == null)
                                prims.Add(new SimplePrimitive(shapeObjs[j], mtl));
                            else
                                prims.Add(new GeometricPrimitive(shapeObjs[j], mtl, area, mi, alphaTex));
                        }
                        sh.parameters.FreeParameters();
                    }
                    return prims;
                };

            LOG_VERBOSE("Starting shapes");
            List<Primitive> primitives = CreatePrimitivesForShapes(shapes);

            shapes.Clear();

            // Animated shapes
            Func<List<AnimatedShapeSceneEntity>, List<Primitive>> CreatePrimitivesForAnimatedShapes =
                (List<AnimatedShapeSceneEntity> shapesIn) =>
                {
                    List<Primitive> prims0 = new List<Primitive>();

                    foreach (AnimatedShapeSceneEntity sh in shapesIn)
                    {
                        pstd.vector<Shape> shapeObjs =
                            Shape.Create(sh.name, sh.identity, sh.identity, sh.reverseOrientation,
                                         sh.parameters, textures.floatTextures, sh.loc, alloc);
                        if (shapeObjs.empty())
                            continue;

                        FloatTexture alphaTex = getAlphaTexture(sh.parameters, sh.loc);
                        int mediumPriority = sh.parameters.GetOneInt("mediumpriority", MediumInterface.noPriority);
                        sh.parameters.ReportUnused();  // do now so can grab alpha...

                        // Create initial shape or shapes for animated shape

                        Material mtl = null;
                        if (sh.materialName.Length != 0)
                        {
                            if (!namedMaterialsIn.TryGetValue(sh.materialName, out mtl))
                                ErrorExit(sh.loc, StringPrintf("%s: no named material defined.", sh.materialName));
                        }
                        else
                        {
                            CHECK_LT(sh.materialIndex, materialsIn.Count);
                            mtl = materialsIn[sh.materialIndex];
                        }

                        MediumInterface mi = new MediumInterface(findMedium(sh.insideMedium, sh.loc),
                                                                 findMedium(sh.outsideMedium, sh.loc), mediumPriority);

                        List<Primitive> prims = new List<Primitive>();
                        for (ulong k = 0; k < shapeObjs.size(); ++k)
                        {
                            Shape s = shapeObjs[k];
                            if (sh.lightIndex != -1)
                            {
                                CHECK(sh.renderFromObject.IsAnimated());
                                ErrorExit(sh.loc, "Animated area lights are not supported.");
                            }

                            if (!mi.IsMediumTransition() && alphaTex == null)
                                prims.Add(new SimplePrimitive(s, mtl));
                            else
                                prims.Add(new GeometricPrimitive(s, mtl, null /* area light */, mi, alphaTex));
                        }

                        // TODO: could try to be greedy or even segment them according
                        // to same sh.renderFromObject...

                        // Create single _Primitive_ for _prims_
                        if (prims.Count > 1)
                        {
                            Primitive bvh = new BVHAggregate(prims);
                            prims = new List<Primitive>();
                            prims.Add(bvh);
                        }
                        prims0.Add(new AnimatedPrimitive(prims[0], sh.renderFromObject));

                        sh.parameters.FreeParameters();
                    }
                    return prims0;
                };
            List<Primitive> animatedPrimitives = CreatePrimitivesForAnimatedShapes(animatedShapes);
            primitives.AddRange(animatedPrimitives);

            animatedShapes.Clear();
            LOG_VERBOSE("Finished shapes");

            // Instance definitions
            LOG_VERBOSE("Starting instances");
            Dictionary<string, Primitive> instanceDefs = new Dictionary<string, Primitive>();
            object instanceDefinitionsMutex = new object();
            List<KeyValuePair<string, InstanceDefinitionSceneEntity>> instanceDefinitionIterators =
                new List<KeyValuePair<string, InstanceDefinitionSceneEntity>>(this.instanceDefinitions);
            ParallelFor(0, instanceDefinitionIterators.Count, (long i) =>
            {
                KeyValuePair<string, InstanceDefinitionSceneEntity> inst = instanceDefinitionIterators[(int)i];

                List<Primitive> instancePrimitives = CreatePrimitivesForShapes(inst.Value.shapes);
                List<Primitive> movingInstancePrimitives =
                    CreatePrimitivesForAnimatedShapes(inst.Value.animatedShapes);
                instancePrimitives.AddRange(movingInstancePrimitives);

                if (instancePrimitives.Count > 1)
                {
                    Primitive bvh = new BVHAggregate(instancePrimitives);
                    instancePrimitives = new List<Primitive>();
                    instancePrimitives.Add(bvh);
                }

                lock (instanceDefinitionsMutex)
                {
                    if (instancePrimitives.Count == 0)
                        instanceDefs[inst.Key] = null;
                    else
                        instanceDefs[inst.Key] = instancePrimitives[0];
                }
            });

            this.instanceDefinitions.Clear();

            // Instances
            foreach (InstanceSceneEntity inst in instances)
            {
                if (!instanceDefs.TryGetValue(inst.name, out Primitive instPrim))
                    ErrorExit(inst.loc, StringPrintf("%s: object instance not defined", inst.name));

                if (instPrim == null)
                    // empty instance
                    continue;

                if (inst.hasRenderFromInstance)
                    primitives.Add(new TransformedPrimitive(instPrim, inst.renderFromInstance));
                else
                    primitives.Add(new AnimatedPrimitive(instPrim, inst.renderFromInstanceAnim));
            }

            instances.Clear();
            LOG_VERBOSE("Finished instances");

            // Accelerator
            Primitive aggregate = null;
            LOG_VERBOSE("Starting top-level accelerator");
            if (primitives.Count != 0)
                aggregate = CreateAccelerator(accelerator.name, primitives, accelerator.parameters);
            LOG_VERBOSE("Finished top-level accelerator");
            return aggregate;
        }

        // BasicScene Public Members
        public SceneEntity integrator, accelerator;
        public RGBColorSpace filmColorSpace;
        public List<ShapeSceneEntity> shapes = new List<ShapeSceneEntity>();
        public List<AnimatedShapeSceneEntity> animatedShapes = new List<AnimatedShapeSceneEntity>();
        public List<InstanceSceneEntity> instances = new List<InstanceSceneEntity>();
        public SortedDictionary<string, InstanceDefinitionSceneEntity> instanceDefinitions =
            new SortedDictionary<string, InstanceDefinitionSceneEntity>(StringComparer.Ordinal);

        // BasicScene Private Members
        AsyncJob<Sampler> samplerJob = null;
        Camera camera;
        Film film;
        object cameraJobMutex = new object();
        AsyncJob<Camera> cameraJob = null;
        object samplerJobMutex = new object();
        Sampler sampler;
        object mediaMutex = new object();
        Dictionary<string, AsyncJob<Medium>> mediumJobs = new Dictionary<string, AsyncJob<Medium>>();
        Dictionary<string, Medium> mediaMap = new Dictionary<string, Medium>();
        object materialMutex = new object();
        SortedDictionary<string, AsyncJob<Image>> normalMapJobs = new SortedDictionary<string, AsyncJob<Image>>(StringComparer.Ordinal);
        Dictionary<string, Image> normalMaps = new Dictionary<string, Image>();

        List<(string, SceneEntity)> namedMaterials = new List<(string, SceneEntity)>();
        List<SceneEntity> materials = new List<SceneEntity>();

        object lightMutex = new object();
        List<AsyncJob<Light>> lightJobs = new List<AsyncJob<Light>>();

        object areaLightMutex = new object();
        List<SceneEntity> areaLights = new List<SceneEntity>();

        object textureMutex = new object();
        List<(string, TransformedSceneEntity)> serialFloatTextures = new List<(string, TransformedSceneEntity)>();
        List<(string, TransformedSceneEntity)> serialSpectrumTextures = new List<(string, TransformedSceneEntity)>();
        List<(string, TransformedSceneEntity)> asyncSpectrumTextures = new List<(string, TransformedSceneEntity)>();
        HashSet<string> loadingTextureFilenames = new HashSet<string>();
        SortedDictionary<string, AsyncJob<FloatTexture>> floatTextureJobs = new SortedDictionary<string, AsyncJob<FloatTexture>>(StringComparer.Ordinal);
        SortedDictionary<string, AsyncJob<SpectrumTexture>> spectrumTextureJobs = new SortedDictionary<string, AsyncJob<SpectrumTexture>>(StringComparer.Ordinal);
        int nMissingTextures = 0;

        object shapeMutex = new object(), animatedShapeMutex = new object();
        object instanceDefinitionMutex = new object(), instanceUseMutex = new object();
    }

    // BasicSceneBuilder Definition
    public unsafe class BasicSceneBuilder : ParserTarget
    {
        const int MaxTransforms = TransformSet.MaxTransforms;

        // BasicSceneBuilder::GraphicsState Definition
        sealed class GraphicsState
        {

            public void ForActiveTransforms(Func<Transform, Transform> func)
            {
                for (int i = 0; i < MaxTransforms; ++i)
                    if ((activeTransformBits & (1 << i)) != 0)
                        ctm[i] = func(ctm[i]);
            }

            public GraphicsState Clone()
            {
                GraphicsState g = (GraphicsState)MemberwiseClone();
                g.ctm = ctm.Clone();
                g.shapeAttributes = new List<ParsedParameter>(shapeAttributes);
                g.lightAttributes = new List<ParsedParameter>(lightAttributes);
                g.materialAttributes = new List<ParsedParameter>(materialAttributes);
                g.mediumAttributes = new List<ParsedParameter>(mediumAttributes);
                g.textureAttributes = new List<ParsedParameter>(textureAttributes);
                return g;
            }
            // GraphicsState Public Methods
            public GraphicsState() { currentMaterialIndex = 0; }

            // GraphicsState Public Members
            public string currentInsideMedium = "", currentOutsideMedium = "";

            public int currentMaterialIndex = 0;
            public string currentMaterialName = "";

            public string areaLightName = "";
            public ParameterDictionary areaLightParams = new ParameterDictionary();
            public FileLoc areaLightLoc = new FileLoc();

            public List<ParsedParameter> shapeAttributes = new List<ParsedParameter>();
            public List<ParsedParameter> lightAttributes = new List<ParsedParameter>();
            public List<ParsedParameter> materialAttributes = new List<ParsedParameter>();
            public List<ParsedParameter> mediumAttributes = new List<ParsedParameter>();
            public List<ParsedParameter> textureAttributes = new List<ParsedParameter>();
            public bool reverseOrientation = false;
            public RGBColorSpace colorSpace = RGBColorSpace.sRGB;
            public TransformSet ctm = new TransformSet();
            public uint activeTransformBits = AllTransformsBits;
            public Float transformStartTime = 0, transformEndTime = 1;
        }

        public enum BlockState { OptionsBlock, WorldBlock }

        const int StartTransformBits = 1 << 0;
        const int EndTransformBits = 1 << 1;
        const int AllTransformsBits = (1 << MaxTransforms) - 1;

        sealed class ActiveInstanceDefinition
        {
            public ActiveInstanceDefinition(string name, FileLoc loc) { entity = new InstanceDefinitionSceneEntity(name, loc); }

            public object mutex = new object();
            public int activeImports = 1;
            public InstanceDefinitionSceneEntity entity;
            public ActiveInstanceDefinition parent = null;
        }

        // API State Macros
        bool VERIFY_OPTIONS(string func, FileLoc loc)
        {
            if (currentBlock == BlockState.WorldBlock)
            {
                ErrorExit(loc, StringPrintf("Options cannot be set inside world block; \"%s\" is not allowed.", func));
                return false;
            }
            return true;
        }
        bool VERIFY_WORLD(string func, FileLoc loc)
        {
            if (currentBlock == BlockState.OptionsBlock)
            {
                ErrorExit(loc, StringPrintf("Scene description must be inside world block; \"%s\" is not allowed.", func));
                return false;
            }
            return true;
        }

        // STAT_COUNTER("Scene/Object instances created", nObjectInstancesCreated);
        [ThreadStatic] static long nObjectInstancesCreated;
        static StatRegisterer STATS_REG = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Scene/Object instances created", nObjectInstancesCreated);
            nObjectInstancesCreated = 0;
        });

        // BasicSceneBuilder Private Methods
        global::pbrt.Transform RenderFromObject(int index)
        {
            return new global::pbrt.Transform((renderFromWorld * graphicsState.ctm[index]).GetMatrix());
        }

        AnimatedTransform RenderFromObject()
        {
            return new AnimatedTransform(RenderFromObject(0), graphicsState.transformStartTime,
                                         RenderFromObject(1), graphicsState.transformEndTime);
        }

        bool CTMIsAnimated() { return graphicsState.ctm.IsAnimated(); }

        public override string ToString()
        {
            return StringPrintf("[ BasicSceneBuilder camera: %s film: %s sampler: %s integrator: %s " +
                                "filter: %s accelerator: %s ]", camera, film, sampler, integrator, filter, accelerator);
        }

        // BasicSceneBuilder Method Definitions
        public BasicSceneBuilder(BasicScene scene)
        {
            this.scene = scene;
            // Set scene defaults
            camera.name = "perspective";
            sampler.name = "zsobol";
            filter.name = "gaussian";
            integrator.name = "volpath";
            accelerator.name = "bvh";

            film.name = "rgb";
            film.parameters = new ParameterDictionary(new List<ParsedParameter>(), RGBColorSpace.sRGB);

            ParameterDictionary dict = new ParameterDictionary(new List<ParsedParameter>(), RGBColorSpace.sRGB);
            currentMaterialIndex = scene.AddMaterial(new SceneEntity("diffuse", dict, new FileLoc()));
        }

        public override void ReverseOrientation(FileLoc loc)
        {
            if (!VERIFY_WORLD("ReverseOrientation", loc)) return;
            graphicsState.reverseOrientation = !graphicsState.reverseOrientation;
        }

        public override void ColorSpace(string name, FileLoc loc)
        {
            RGBColorSpace cs = RGBColorSpace.GetNamed(name);
            if (cs != null)
                graphicsState.colorSpace = cs;
            else
                Error(loc, StringPrintf("%s: color space unknown", name));
        }

        public override void Identity(FileLoc loc)
        {
            graphicsState.ForActiveTransforms((Transform t) => new global::pbrt.Transform());
        }

        public override void Translate(Float dx, Float dy, Float dz, FileLoc loc)
        {
            graphicsState.ForActiveTransforms((Transform t) => t * pbrt.Translate(new Vector3f(dx, dy, dz)));
        }

        public override void CoordinateSystem(string origName, FileLoc loc)
        {
            string name = NormalizeUTF8(origName);
            namedCoordinateSystems[name] = graphicsState.ctm.Clone();
        }

        public override void CoordSysTransform(string origName, FileLoc loc)
        {
            string name = NormalizeUTF8(origName);
            if (namedCoordinateSystems.ContainsKey(name))
                graphicsState.ctm = namedCoordinateSystems[name].Clone();
            else
                Warning(loc, StringPrintf("Couldn't find named coordinate system \"%s\"", name));
        }

        public override void Camera(string name, List<ParsedParameter> parms, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.colorSpace);

            if (!VERIFY_OPTIONS("Camera", loc)) return;

            TransformSet cameraFromWorld = graphicsState.ctm.Clone();
            TransformSet worldFromCamera = TransformSet.Inverse(graphicsState.ctm);
            namedCoordinateSystems["camera"] = TransformSet.Inverse(cameraFromWorld);

            CameraTransform cameraTransform = new CameraTransform(
                new AnimatedTransform(worldFromCamera[0], graphicsState.transformStartTime,
                                      worldFromCamera[1], graphicsState.transformEndTime));
            renderFromWorld = cameraTransform.RenderFromWorld();

            camera = new CameraSceneEntity(name, dict, loc, cameraTransform,
                                           graphicsState.currentOutsideMedium);
        }

        public override void AttributeBegin(FileLoc loc)
        {
            if (!VERIFY_WORLD("AttributeBegin", loc)) return;
            pushedGraphicsStates.Add(graphicsState.Clone());
            pushStack.Add(('a', loc));
        }

        public override void AttributeEnd(FileLoc loc)
        {
            if (!VERIFY_WORLD("AttributeEnd", loc)) return;
            // Issue error on unmatched _AttributeEnd_
            if (pushedGraphicsStates.Count == 0)
            {
                Error(loc, "Unmatched AttributeEnd encountered. Ignoring it.");
                return;
            }

            // NOTE: must keep the following consistent with code in ObjectEnd
            graphicsState = pushedGraphicsStates[pushedGraphicsStates.Count - 1];
            pushedGraphicsStates.RemoveAt(pushedGraphicsStates.Count - 1);

            if (pushStack[pushStack.Count - 1].Item1 == 'o')
                ErrorExitDeferred(loc, StringPrintf("Mismatched nesting: open ObjectBegin from %s at AttributeEnd",
                                                    pushStack[pushStack.Count - 1].Item2));
            else
                CHECK_EQ(pushStack[pushStack.Count - 1].Item1, 'a');
            pushStack.RemoveAt(pushStack.Count - 1);
        }

        public override void Attribute(string target, List<ParsedParameter> attrib, FileLoc loc)
        {
            List<ParsedParameter> currentAttributes = null;
            if (target == "shape")
                currentAttributes = graphicsState.shapeAttributes;
            else if (target == "light")
                currentAttributes = graphicsState.lightAttributes;
            else if (target == "material")
                currentAttributes = graphicsState.materialAttributes;
            else if (target == "medium")
                currentAttributes = graphicsState.mediumAttributes;
            else if (target == "texture")
                currentAttributes = graphicsState.textureAttributes;
            else
            {
                ErrorExitDeferred(loc,
                                  StringPrintf("Unknown attribute target \"%s\". Must be \"shape\", \"light\", " +
                                               "\"material\", \"medium\", or \"texture\".", target));
                return;
            }

            // Note that we hold on to the current color space and associate it
            // with the parameters...
            foreach (ParsedParameter p in attrib)
            {
                p.mayBeUnused = true;
                p.colorSpace = graphicsState.colorSpace;
                currentAttributes.Add(p);
            }
        }

        public override void Sampler(string name, List<ParsedParameter> parms, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.colorSpace);
            if (!VERIFY_OPTIONS("Sampler", loc)) return;
            sampler = new SceneEntity(name, dict, loc);
        }

        public override void WorldBegin(FileLoc loc)
        {
            if (!VERIFY_OPTIONS("WorldBegin", loc)) return;
            // Reset graphics state for _WorldBegin_
            currentBlock = BlockState.WorldBlock;
            for (int i = 0; i < MaxTransforms; ++i)
                graphicsState.ctm[i] = new global::pbrt.Transform();
            graphicsState.activeTransformBits = AllTransformsBits;
            namedCoordinateSystems["world"] = graphicsState.ctm.Clone();

            // Pass pre-_WorldBegin_ entities to _scene_
            scene.SetOptions(filter, film, camera, sampler, integrator, accelerator);
        }

        public override void MakeNamedMedium(string origName, List<ParsedParameter> parms, FileLoc loc)
        {
            string name = NormalizeUTF8(origName);
            // Issue error if medium _name_ is multiply defined
            if (mediumNames.Contains(name))
            {
                ErrorExitDeferred(loc, StringPrintf("Named medium \"%s\" redefined.", name));
                return;
            }
            mediumNames.Add(name);

            // Create _ParameterDictionary_ for medium and call _AddMedium()_
            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.mediumAttributes,
                                                               graphicsState.colorSpace);
            scene.AddMedium(new TransformedSceneEntity(name, dict, loc, RenderFromObject()));
        }

        public override void LightSource(string name, List<ParsedParameter> parms, FileLoc loc)
        {
            if (!VERIFY_WORLD("LightSource", loc)) return;
            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.lightAttributes,
                                                               graphicsState.colorSpace);
            scene.AddLight(new LightSceneEntity(name, dict, loc, RenderFromObject(),
                                                graphicsState.currentOutsideMedium));
        }

        public override void Shape(string name, List<ParsedParameter> parms, FileLoc loc)
        {
            if (!VERIFY_WORLD("Shape", loc)) return;

            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.shapeAttributes,
                                                               graphicsState.colorSpace);

            int areaLightIndex = -1;
            if (graphicsState.areaLightName.Length != 0)
            {
                areaLightIndex = scene.AddAreaLight(new SceneEntity(graphicsState.areaLightName,
                                                                    graphicsState.areaLightParams,
                                                                    graphicsState.areaLightLoc));
                if (activeInstanceDefinition != null)
                    Warning(loc, "Area lights not supported with object instancing");
            }

            if (CTMIsAnimated())
            {
                AnimatedTransform renderFromShape = RenderFromObject();
                global::pbrt.Transform identity = new global::pbrt.Transform();

                AnimatedShapeSceneEntity entity = new AnimatedShapeSceneEntity(
                    name, dict, loc, renderFromShape, identity,
                    graphicsState.reverseOrientation, graphicsState.currentMaterialIndex,
                    graphicsState.currentMaterialName, areaLightIndex,
                    graphicsState.currentInsideMedium, graphicsState.currentOutsideMedium);

                if (activeInstanceDefinition != null)
                    activeInstanceDefinition.entity.animatedShapes.Add(entity);
                else
                    scene.AddAnimatedShape(entity);
            }
            else
            {
                global::pbrt.Transform renderFromObject = RenderFromObject(0);
                global::pbrt.Transform objectFromRender = pbrt.Inverse(renderFromObject);

                ShapeSceneEntity entity = new ShapeSceneEntity(
                    name, dict, loc, renderFromObject, objectFromRender,
                    graphicsState.reverseOrientation, graphicsState.currentMaterialIndex,
                    graphicsState.currentMaterialName, areaLightIndex,
                    graphicsState.currentInsideMedium, graphicsState.currentOutsideMedium);
                if (activeInstanceDefinition != null)
                    activeInstanceDefinition.entity.shapes.Add(entity);
                else
                    shapes.Add(entity);
            }
        }

        public override void ObjectBegin(string origName, FileLoc loc)
        {
            string name = NormalizeUTF8(origName);

            if (!VERIFY_WORLD("ObjectBegin", loc)) return;
            pushedGraphicsStates.Add(graphicsState.Clone());

            pushStack.Add(('o', loc));

            if (activeInstanceDefinition != null)
            {
                ErrorExitDeferred(loc, "ObjectBegin called inside of instance definition");
                return;
            }

            if (instanceNames.Contains(name))
            {
                ErrorExitDeferred(loc, StringPrintf("%s: trying to redefine an object instance", name));
                return;
            }
            instanceNames.Add(name);

            activeInstanceDefinition = new ActiveInstanceDefinition(name, loc);
        }

        public override void ObjectEnd(FileLoc loc)
        {
            if (!VERIFY_WORLD("ObjectEnd", loc)) return;
            if (activeInstanceDefinition == null)
            {
                ErrorExitDeferred(loc, "ObjectEnd called outside of instance definition");
                return;
            }
            if (activeInstanceDefinition.parent != null)
            {
                ErrorExitDeferred(loc, "ObjectEnd called inside Import for instance definition");
                return;
            }

            // NOTE: Must keep the following consistent with AttributeEnd
            graphicsState = pushedGraphicsStates[pushedGraphicsStates.Count - 1];
            pushedGraphicsStates.RemoveAt(pushedGraphicsStates.Count - 1);

            ++nObjectInstancesCreated;

            if (pushStack[pushStack.Count - 1].Item1 == 'a')
                ErrorExitDeferred(loc, StringPrintf("Mismatched nesting: open AttributeBegin from %s at ObjectEnd",
                                                    pushStack[pushStack.Count - 1].Item2));
            else
                CHECK_EQ(pushStack[pushStack.Count - 1].Item1, 'o');
            pushStack.RemoveAt(pushStack.Count - 1);

            // Otherwise it will be taken care of in MergeImported()
            if (Interlocked.Decrement(ref activeInstanceDefinition.activeImports) == 0)
                scene.AddInstanceDefinition(activeInstanceDefinition.entity);

            activeInstanceDefinition = null;
        }

        public override void ObjectInstance(string origName, FileLoc loc)
        {
            string name = NormalizeUTF8(origName);
            if (!VERIFY_WORLD("ObjectInstance", loc)) return;

            if (activeInstanceDefinition != null)
            {
                ErrorExitDeferred(loc, "ObjectInstance can't be called inside instance definition");
                return;
            }

            global::pbrt.Transform worldFromRender = pbrt.Inverse(renderFromWorld);

            if (CTMIsAnimated())
            {
                AnimatedTransform animatedRenderFromInstance = new AnimatedTransform(
                    RenderFromObject(0) * worldFromRender, graphicsState.transformStartTime,
                    RenderFromObject(1) * worldFromRender, graphicsState.transformEndTime);

                // For very small changes, animatedRenderFromInstance may have both
                // xforms equal even if CTMIsAnimated() has returned true. Fall
                // through to create a regular non-animated instance in that case.
                if (animatedRenderFromInstance.IsAnimated())
                {
                    instanceUses.Add(new InstanceSceneEntity(name, loc, animatedRenderFromInstance));
                    return;
                }
            }

            global::pbrt.Transform renderFromInstance = RenderFromObject(0) * worldFromRender;
            instanceUses.Add(new InstanceSceneEntity(name, loc, renderFromInstance));
        }

        public override void EndOfFiles()
        {
            if (currentBlock != BlockState.WorldBlock)
                ErrorExitDeferred("End of files before \"WorldBegin\".");

            // Ensure there are no pushed graphics states
            while (pushedGraphicsStates.Count != 0)
            {
                ErrorExitDeferred("Missing end to AttributeBegin");
                pushedGraphicsStates.RemoveAt(pushedGraphicsStates.Count - 1);
            }

            if (errorExit)
                ErrorExit("Fatal errors during scene construction");

            if (shapes.Count != 0)
                scene.AddShapes(shapes);
            if (instanceUses.Count != 0)
                scene.AddInstanceUses(instanceUses);

            scene.Done();
        }

        public BasicSceneBuilder CopyForImport()
        {
            BasicSceneBuilder importBuilder = new BasicSceneBuilder(scene);
            importBuilder.renderFromWorld = renderFromWorld;
            importBuilder.graphicsState = graphicsState.Clone();
            importBuilder.currentBlock = currentBlock;
            if (activeInstanceDefinition != null)
            {
                importBuilder.activeInstanceDefinition = new ActiveInstanceDefinition(
                    activeInstanceDefinition.entity.name, activeInstanceDefinition.entity.loc);

                // In case of nested imports, go up to the true root parent since
                // that's where we need to merge our shapes and that's where the
                // refcount is.
                ActiveInstanceDefinition parent = activeInstanceDefinition;
                while (parent.parent != null)
                    parent = parent.parent;
                importBuilder.activeInstanceDefinition.parent = parent;
                Interlocked.Increment(ref parent.activeImports);
            }
            return importBuilder;
        }

        public void MergeImported(BasicSceneBuilder imported)
        {
            while (imported.pushedGraphicsStates.Count != 0)
            {
                ErrorExitDeferred("Missing end to AttributeBegin");
                imported.pushedGraphicsStates.RemoveAt(imported.pushedGraphicsStates.Count - 1);
            }

            errorExit |= imported.errorExit;

            if (imported.shapes.Count != 0)
                scene.AddShapes(imported.shapes);
            if (imported.instanceUses.Count != 0)
                scene.AddInstanceUses(imported.instanceUses);

            if (imported.activeInstanceDefinition != null)
            {
                ActiveInstanceDefinition current = imported.activeInstanceDefinition;
                ActiveInstanceDefinition parent = current.parent;
                CHECK(parent != null);

                lock (parent.mutex)
                {
                    parent.entity.shapes.AddRange(current.entity.shapes);
                    parent.entity.animatedShapes.AddRange(current.entity.animatedShapes);

                    if (Interlocked.Decrement(ref parent.activeImports) == 0)
                        scene.AddInstanceDefinition(parent.entity);
                }
            }

            Action<HashSet<string>, HashSet<string>, string> mergeSet =
                (HashSet<string> b, HashSet<string> imp, string name) =>
                {
                    foreach (string item in imp)
                    {
                        if (b.Contains(item))
                            ErrorExitDeferred(StringPrintf("%s: multiply defined %s.", item, name));
                        b.Add(item);
                    }
                    imp.Clear();
                };
            mergeSet(namedMaterialNames, imported.namedMaterialNames, "named material");
            mergeSet(floatTextureNames, imported.floatTextureNames, "texture");
            mergeSet(spectrumTextureNames, imported.spectrumTextureNames, "texture");
        }

        public override void Option(string name, string value, FileLoc loc)
        {
            string nName = normalizeArg(name);

            Func<string, bool?> parseBool = (string v) =>
            {
                if (v == "true") return true;
                if (v == "false") return false;
                ErrorExitDeferred(loc, StringPrintf("%s: expected \"true\" or \"false\" for option value", v));
                return null;
            };

            if (nName == "disablepixeljitter")
            {
                bool? b = parseBool(value);
                if (b.HasValue) Options.disablePixelJitter = b.Value;
            }
            else if (nName == "disabletexturefiltering")
            {
                bool? b = parseBool(value);
                if (b.HasValue) Options.disableTextureFiltering = b.Value;
            }
            else if (nName == "disablewavelengthjitter")
            {
                bool? b = parseBool(value);
                if (b.HasValue) Options.disableWavelengthJitter = b.Value;
            }
            else if (nName == "displacementedgescale")
            {
                Float v = 0;
                if (!Atof(value, &v))
                    ErrorExitDeferred(loc, StringPrintf("%s: expected floating-point option value", value));
                else
                    Options.displacementEdgeScale = v;
            }
            else if (nName == "msereferenceimage")
            {
                if (value.Length < 3 || value[0] != '"' || value[value.Length - 1] != '"')
                    ErrorExitDeferred(loc, StringPrintf("%s: expected quoted string for option value", value));
                else
                    Options.mseReferenceImage = value.Substring(1, value.Length - 2);
            }
            else if (nName == "msereferenceout")
            {
                if (value.Length < 3 || value[0] != '"' || value[value.Length - 1] != '"')
                    ErrorExitDeferred(loc, StringPrintf("%s: expected quoted string for option value", value));
                else
                    Options.mseReferenceOutput = value.Substring(1, value.Length - 2);
            }
            else if (nName == "rendercoordsys")
            {
                if (value.Length < 3 || value[0] != '"' || value[value.Length - 1] != '"')
                    ErrorExitDeferred(loc, StringPrintf("%s: expected quoted string for option value", value));
                else
                {
                    string renderCoordSys = value.Substring(1, value.Length - 2);
                    if (renderCoordSys == "camera")
                        Options.renderingSpace = RenderingCoordinateSystem.Camera;
                    else if (renderCoordSys == "cameraworld")
                        Options.renderingSpace = RenderingCoordinateSystem.CameraWorld;
                    else if (renderCoordSys == "world")
                        Options.renderingSpace = RenderingCoordinateSystem.World;
                    else
                        ErrorExit(StringPrintf("%s: unknown rendering coordinate system.", renderCoordSys));
                }
            }
            else if (nName == "seed")
            {
                int.TryParse(value, out int sd);
                Options.seed = sd;
            }
            else if (nName == "forcediffuse")
            {
                bool? b = parseBool(value);
                if (b.HasValue) Options.forceDiffuse = b.Value;
            }
            else if (nName == "pixelstats")
            {
                bool? b = parseBool(value);
                if (b.HasValue) Options.recordPixelStatistics = b.Value;
            }
            else if (nName == "wavefront")
            {
                bool? b = parseBool(value);
                if (b.HasValue) Options.wavefront = b.Value;
            }
            else
                ErrorExitDeferred(loc, StringPrintf("%s: unknown option", name));
        }

        public override void Transform(Float[] tr, FileLoc loc)
        {
            graphicsState.ForActiveTransforms((Transform t) =>
                pbrt.Transpose(new global::pbrt.Transform(new SquareMatrix4((pstd.span<Float>)tr))));
        }

        public override void ConcatTransform(Float[] tr, FileLoc loc)
        {
            graphicsState.ForActiveTransforms((Transform t) =>
                t * pbrt.Transpose(new global::pbrt.Transform(new SquareMatrix4((pstd.span<Float>)tr))));
        }

        public override void Rotate(Float angle, Float dx, Float dy, Float dz, FileLoc loc)
        {
            graphicsState.ForActiveTransforms((Transform t) => t * pbrt.Rotate(angle, new Vector3f(dx, dy, dz)));
        }

        public override void Scale(Float sx, Float sy, Float sz, FileLoc loc)
        {
            graphicsState.ForActiveTransforms((Transform t) => t * pbrt.Scale(sx, sy, sz));
        }

        public override void LookAt(Float ex, Float ey, Float ez, Float lx, Float ly, Float lz,
                                    Float ux, Float uy, Float uz, FileLoc loc)
        {
            global::pbrt.Transform lookAt = pbrt.LookAt(new Point3f(ex, ey, ez), new Point3f(lx, ly, lz), new Vector3f(ux, uy, uz));
            graphicsState.ForActiveTransforms((Transform t) => t * lookAt);
        }

        public override void ActiveTransformAll(FileLoc loc)
        {
            graphicsState.activeTransformBits = AllTransformsBits;
        }

        public override void ActiveTransformEndTime(FileLoc loc)
        {
            graphicsState.activeTransformBits = EndTransformBits;
        }

        public override void ActiveTransformStartTime(FileLoc loc)
        {
            graphicsState.activeTransformBits = StartTransformBits;
        }

        public override void TransformTimes(Float start, Float end, FileLoc loc)
        {
            if (!VERIFY_OPTIONS("TransformTimes", loc)) return;
            graphicsState.transformStartTime = start;
            graphicsState.transformEndTime = end;
        }

        public override void PixelFilter(string name, List<ParsedParameter> parms, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.colorSpace);
            if (!VERIFY_OPTIONS("PixelFilter", loc)) return;
            filter = new SceneEntity(name, dict, loc);
        }

        public override void Film(string type, List<ParsedParameter> parms, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.colorSpace);
            if (!VERIFY_OPTIONS("Film", loc)) return;
            film = new SceneEntity(type, dict, loc);
        }

        public override void Accelerator(string name, List<ParsedParameter> parms, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.colorSpace);
            if (!VERIFY_OPTIONS("Accelerator", loc)) return;
            accelerator = new SceneEntity(name, dict, loc);
        }

        public override void Integrator(string name, List<ParsedParameter> parms, FileLoc loc)
        {
            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.colorSpace);

            if (!VERIFY_OPTIONS("Integrator", loc)) return;
            integrator = new SceneEntity(name, dict, loc);
        }

        public override void MediumInterface(string origInsideName, string origOutsideName, FileLoc loc)
        {
            string insideName = NormalizeUTF8(origInsideName);
            string outsideName = NormalizeUTF8(origOutsideName);

            graphicsState.currentInsideMedium = insideName;
            graphicsState.currentOutsideMedium = outsideName;
        }

        public override void Texture(string origName, string type, string texname,
                                     List<ParsedParameter> parms, FileLoc loc)
        {
            string name = NormalizeUTF8(origName);
            if (!VERIFY_WORLD("Texture", loc)) return;

            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.textureAttributes,
                                                               graphicsState.colorSpace);

            if (type != "float" && type != "spectrum")
            {
                ErrorExitDeferred(loc, StringPrintf("%s: texture type unknown. Must be \"float\" or \"spectrum\".", type));
                return;
            }

            HashSet<string> names = (type == "float") ? floatTextureNames : spectrumTextureNames;
            if (names.Contains(name))
            {
                ErrorExitDeferred(loc, StringPrintf("Redefining texture \"%s\".", name));
                return;
            }
            names.Add(name);

            if (type == "float")
                scene.AddFloatTexture(name, new TransformedSceneEntity(texname, dict, loc, RenderFromObject()));
            else
                scene.AddSpectrumTexture(name, new TransformedSceneEntity(texname, dict, loc, RenderFromObject()));
        }

        public override void Material(string name, List<ParsedParameter> parms, FileLoc loc)
        {
            if (!VERIFY_WORLD("Material", loc)) return;

            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.materialAttributes,
                                                               graphicsState.colorSpace);

            graphicsState.currentMaterialIndex = scene.AddMaterial(new SceneEntity(name, dict, loc));
            graphicsState.currentMaterialName = "";
        }

        public override void MakeNamedMaterial(string origName, List<ParsedParameter> parms, FileLoc loc)
        {
            string name = NormalizeUTF8(origName);
            if (!VERIFY_WORLD("MakeNamedMaterial", loc)) return;

            ParameterDictionary dict = new ParameterDictionary(parms, graphicsState.materialAttributes,
                                                               graphicsState.colorSpace);

            if (namedMaterialNames.Contains(name))
            {
                ErrorExitDeferred(loc, StringPrintf("%s: named material redefined.", name));
                return;
            }
            namedMaterialNames.Add(name);

            scene.AddNamedMaterial(name, new SceneEntity("", dict, loc));
        }

        public override void NamedMaterial(string origName, FileLoc loc)
        {
            string name = NormalizeUTF8(origName);
            if (!VERIFY_WORLD("NamedMaterial", loc)) return;
            graphicsState.currentMaterialName = name;
            graphicsState.currentMaterialIndex = -1;
        }

        public override void AreaLightSource(string name, List<ParsedParameter> parms, FileLoc loc)
        {
            if (!VERIFY_WORLD("AreaLightSource", loc)) return;
            graphicsState.areaLightName = name;
            graphicsState.areaLightParams = new ParameterDictionary(parms, graphicsState.lightAttributes,
                                                                    graphicsState.colorSpace);
            graphicsState.areaLightLoc = loc;
        }

        // BasicSceneBuilder Private Members
        BasicScene scene;
        public BlockState currentBlock = BlockState.OptionsBlock;
        GraphicsState graphicsState = new GraphicsState();
        Dictionary<string, TransformSet> namedCoordinateSystems = new Dictionary<string, TransformSet>();
        global::pbrt.Transform renderFromWorld = new global::pbrt.Transform();
        List<GraphicsState> pushedGraphicsStates = new List<GraphicsState>();
        List<(char, FileLoc)> pushStack = new List<(char, FileLoc)>();  // 'a': attribute, 'o': object
        ActiveInstanceDefinition activeInstanceDefinition = null;

        // Buffer these both to avoid mutex contention and so that they are
        // consistently ordered across runs.
        List<ShapeSceneEntity> shapes = new List<ShapeSceneEntity>();
        List<InstanceSceneEntity> instanceUses = new List<InstanceSceneEntity>();

        HashSet<string> namedMaterialNames = new HashSet<string>(), mediumNames = new HashSet<string>();
        HashSet<string> floatTextureNames = new HashSet<string>(), spectrumTextureNames = new HashSet<string>(),
                        instanceNames = new HashSet<string>();
        int currentMaterialIndex = 0, currentLightIndex = -1;
        SceneEntity sampler = new SceneEntity(), film = new SceneEntity(), integrator = new SceneEntity(),
                    filter = new SceneEntity(), accelerator = new SceneEntity();
        CameraSceneEntity camera = new CameraSceneEntity();
    }

}  // namespace pbrt
