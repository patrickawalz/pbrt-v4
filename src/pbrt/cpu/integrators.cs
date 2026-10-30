// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// cpu/integrators.h / cpu/integrators.cpp
//
// Ported: Integrator, ImageTileIntegrator, RayIntegrator, VolPathIntegrator, Integrator::Create.
// NOT PORTED (for now): RandomWalk, SimplePath, Path, SimpleVolPath, AO, LightPath, BDPT, MLT,
// SPPM and Function integrators; Integrator::Create exits with an error for them.

using System;
using System.Collections.Generic;
using System.IO;

namespace pbrt
{

    // Integrator Definition
    public abstract unsafe class Integrator
    {

        public bool Unoccluded(Interaction p0, Interaction p1)
        {
            return !IntersectP(p0.SpawnRayTo(p1), 1 - ShadowEpsilon);
        }

        public abstract void Render();

        // Integrator Utility Functions
        // STAT_COUNTER("Intersections/Regular ray intersection tests", nIntersectionTests);
        // STAT_COUNTER("Intersections/Shadow ray intersection tests", nShadowTests);
        [ThreadStatic] static long nIntersectionTests, nShadowTests;
        static StatRegisterer STATS_REG_ISECT = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Intersections/Regular ray intersection tests", nIntersectionTests);
            nIntersectionTests = 0;
            accum.ReportCounter("Intersections/Shadow ray intersection tests", nShadowTests);
            nShadowTests = 0;
        });

        // Integrator Method Definitions
        public pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            ++nIntersectionTests;
            DCHECK_NE(ray.d, new Vector3f(0, 0, 0));
            if (aggregate != null)
                return aggregate.Intersect(ray, tMax);
            else
                return default;
        }

        public bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            ++nShadowTests;
            DCHECK_NE(ray.d, new Vector3f(0, 0, 0));
            if (aggregate != null)
                return aggregate.IntersectP(ray, tMax);
            else
                return false;
        }

        public SampledSpectrum Tr(Interaction p0, Interaction p1, SampledWavelengths lambda)
        {
            RNG rng = new RNG(Hash(p0.p()), Hash(p1.p()));

            // :-(
            Ray ray =
                p0.IsSurfaceInteraction() ? p0.AsSurface().SpawnRayTo(p1) : p0.SpawnRayTo(p1);
            SampledSpectrum Tr = new SampledSpectrum(1.0f), inv_w = new SampledSpectrum(1.0f);
            if (LengthSquared(ray.d) == 0)
                return Tr;

            while (true)
            {
                pstd.optional<ShapeIntersection> si = Intersect(ray, 1 - ShadowEpsilon);
                // Handle opaque surface along ray's path
                if (si && si.value().intr.material != null)
                    return new SampledSpectrum(0.0f);

                // Update transmittance for current ray segment
                if (ray.medium != null)
                {
                    Point3f pExit = ray.Apply(si ? si.value().tHit : (1 - ShadowEpsilon));
                    ray.d = pExit - ray.o;

                    SampledSpectrum T_maj =
                        SampleT_maj(ray, 1.0f, rng.Uniform<Float>(), ref rng, lambda,
                                    (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj,
                                     SampledSpectrum T_maj_) =>
                                    {
                                        // (not in pbrt-v4) The null coefficient of progressive tracking without clamping is signed
                                        SampledSpectrum sigma_n = mp.signedNull ? sigma_maj - mp.sigma_a - mp.sigma_s
                                                                                : ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);

                                        // ratio-tracking: only evaluate null scattering
                                        Float pr = T_maj_[0] * sigma_maj[0];
                                        Tr *= T_maj_ * sigma_n / pr;
                                        inv_w *= T_maj_ * sigma_maj / pr;

                                        if (!Tr || !inv_w)
                                            return false;

                                        return true;
                                    });
                    Tr *= T_maj / T_maj[0];
                    inv_w *= T_maj / T_maj[0];
                }

                // Generate next ray segment or return final transmittance
                if (!si)
                    break;
                ray = si.value().intr.SpawnRayTo(p1);
            }
            return Tr / inv_w.Average();
        }
        // Integrator Public Methods
        public static Integrator Create(string name, ParameterDictionary parameters, Camera camera,
                                        Sampler sampler, Primitive aggregate, List<Light> lights,
                                        RGBColorSpace colorSpace, FileLoc loc)
        {
            Integrator integrator = null;
            if (name == "path")
                integrator = PathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
            else if (name == "simplepath")
                integrator = SimplePathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
            else if (name == "lightpath")
                integrator = LightPathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
            else if (name == "simplevolpath")
                integrator = SimpleVolPathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
            else if (name == "volpath")
                integrator = VolPathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
            else if (name == "ambientocclusion")
                integrator = AOIntegrator.Create(parameters, colorSpace.illuminant, camera, sampler, aggregate,
                                                 lights, loc);
            else if (name == "randomwalk")
                integrator = RandomWalkIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
            else if (name == "bdpt")
                integrator = BDPTIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
            else if (name == "mlt")
                integrator = MLTIntegrator.Create(parameters, camera, aggregate, lights, loc);
            else if (name == "function")
                integrator = FunctionIntegrator.Create(parameters, camera, sampler, loc);
            else if (name == "sppm")
                integrator = SPPMIntegrator.Create(parameters, colorSpace, camera, sampler, aggregate, lights, loc);
            else
                ErrorExit(loc, StringPrintf("%s: integrator type unknown.", name));

            if (integrator == null)
                ErrorExit(loc, StringPrintf("%s: unable to create integrator.", name));

            // (not in pbrt-v4) The lights are preprocessed by now, so their powers can be evaluated
            if (camera != null && camera.GetFilm().WantsLightWavelengths())
                camera.GetFilm().SetWavelengthDistribution(WavelengthDistribution.FromLights(TextureMedium.AddEmissionLights(lights)));

            parameters.ReportUnused();
            return integrator;
        }

        public abstract override string ToString();

        // Integrator Public Members
        public Primitive aggregate;
        public List<Light> lights;
        public List<Light> infiniteLights = new List<Light>();

        // Integrator Protected Methods
        protected Integrator(Primitive aggregate, List<Light> lights)
        {
            this.aggregate = aggregate;
            this.lights = lights;
            // Integrator constructor implementation
            Bounds3f sceneBounds = aggregate != null ? aggregate.Bounds() : new Bounds3f();
            LOG_VERBOSE("Scene bounds %s", sceneBounds);
            foreach (Light light in lights)
            {
                light.Preprocess(sceneBounds);
                if (light.Type() == LightType.Infinite)
                    infiniteLights.Add(light);
            }
        }
    }

    // ImageTileIntegrator Definition
    public abstract unsafe class ImageTileIntegrator : Integrator
    {
        // STAT_COUNTER("Integrator/Camera rays traced", nCameraRays);  (defined in integrators.cpp)
        [ThreadStatic] protected static long nCameraRays;
        static StatRegisterer STATS_REG_CAMRAYS = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Integrator/Camera rays traced", nCameraRays);
            nCameraRays = 0;
        });

        // ImageTileIntegrator Public Methods
        public ImageTileIntegrator(Camera camera, Sampler sampler, Primitive aggregate,
                                   List<Light> lights)
            : base(aggregate, lights)
        {
            this.camera = camera;
            this.samplerPrototype = sampler;
        }

        [ThreadStatic] static Point2i threadPixel;
        [ThreadStatic] static int threadSampleIndex;

        public override void Render()
        {
            // Handle debugStart, if set
            if (Options.debugStart.Length != 0)
            {
                List<int> c = SplitStringToInts(Options.debugStart, ',');
                if (c.Count == 0)
                    ErrorExit(StringPrintf("Didn't find integer values after --debugstart: %s",
                                           Options.debugStart));
                if (c.Count != 3)
                    ErrorExit(StringPrintf("Didn't find three integer values after --debugstart: %s",
                                           Options.debugStart));

                Point2i pPixel = new Point2i(c[0], c[1]);
                int sampleIndex = c[2];

                ScratchBuffer scratchBuffer = new ScratchBuffer(65536);
                Sampler tileSampler = samplerPrototype.Clone(new Allocator());
                tileSampler.StartPixelSample(pPixel, sampleIndex);

                EvaluatePixelSample(pPixel, sampleIndex, tileSampler, scratchBuffer);

                return;
            }

            // (CheckCallbackScope is replaced by the try/catch in the tile loop below.)

            // Declare common variables for rendering image in tiles
            ThreadLocal<ScratchBuffer> scratchBuffers = new ThreadLocal<ScratchBuffer>(() => new ScratchBuffer());

            ThreadLocal<Sampler> samplers = new ThreadLocal<Sampler>(() => samplerPrototype.Clone());

            Bounds2i pixelBounds = camera.GetFilm().PixelBounds();
            int spp = samplerPrototype.SamplesPerPixel();
            ProgressReporter progress = new ProgressReporter((int64_t)spp * pixelBounds.Area(), "Rendering",
                                                             Options.quiet);

            int waveStart = 0, waveEnd = 1, nextWaveSize = 1;

            if (Options.recordPixelStatistics)
                StatsEnablePixelStats(pixelBounds,
                                      RemoveExtension(camera.GetFilm().GetFilename()));
            // Handle MSE reference image, if provided
            Image referenceImage = null;
            StreamWriter mseOutFile = null;
            if (Options.mseReferenceImage.Length != 0)
            {
                ImageAndMetadata mse = Image.Read(Options.mseReferenceImage);
                referenceImage = mse.image;

                Bounds2i msePixelBounds =
                    mse.metadata.pixelBounds
                        ? mse.metadata.pixelBounds.value()
                        : new Bounds2i(new Point2i(0, 0), referenceImage.Resolution());
                if (!Inside(pixelBounds, msePixelBounds))
                    ErrorExit(StringPrintf("Output image pixel bounds %s aren't inside the MSE " +
                                           "image's pixel bounds %s.",
                                           pixelBounds, msePixelBounds));

                // Transform the pixelBounds of the image we're rendering to the
                // coordinate system with msePixelBounds.pMin at the origin, which
                // in turn gives us the section of the MSE image to crop. (This is
                // complicated by the fact that Image doesn't support pixel
                // bounds...)
                Bounds2i cropBounds = new Bounds2i(new Point2i(pixelBounds.pMin - msePixelBounds.pMin),
                                                   new Point2i(pixelBounds.pMax - msePixelBounds.pMin));
                referenceImage = referenceImage.Crop(cropBounds);
                CHECK_EQ(referenceImage.Resolution(), new Point2i(pixelBounds.Diagonal()));

                FileStream fs = FOpenWrite(Options.mseReferenceOutput);
                if (fs == null)
                    ErrorExit(StringPrintf("%s: %s", Options.mseReferenceOutput, "unable to open file"));
                mseOutFile = new StreamWriter(fs);
            }

            // Connect to display server if needed
            if (Options.displayServer.Length != 0)
                ErrorExit("--display-server is not supported by Magi.");

            // (not in pbrt-v4) Progressive null-tracking media can learn their majorants in discarded passes first
            int warmupPasses = TextureMedium.ProgressiveWarmupPasses();
            RayIntegrator.discardSamples = warmupPasses > 0;
            for (int warmup = 0; warmup < warmupPasses; ++warmup)
            {
                int warmupSampleIndex = spp + warmup;
                ParallelFor2D(pixelBounds, (Bounds2i tileBounds) =>
                {
                    ScratchBuffer scratchBuffer = scratchBuffers.Get();
                    Sampler sampler = samplers.Get();
                    foreach (Point2i pPixel in tileBounds)
                    {
                        sampler.StartPixelSample(pPixel, warmupSampleIndex);
                        EvaluatePixelSample(pPixel, warmupSampleIndex, sampler, scratchBuffer);
                        scratchBuffer.Reset();
                    }
                });
                TextureMedium.EndProgressivePass();
            }
            RayIntegrator.discardSamples = false;

            // Render image in waves
            while (waveStart < spp)
            {
                // Render current wave's image tiles in parallel
                int waveStartCopy = waveStart, waveEndCopy = waveEnd;
                ParallelFor2D(pixelBounds, (Bounds2i tileBounds) =>
                {
                    // Render image tile given by _tileBounds_
                    ScratchBuffer scratchBuffer = scratchBuffers.Get();
                    Sampler sampler = samplers.Get();
                    try
                    {
                        foreach (Point2i pPixel in tileBounds)
                        {
                            StatsReportPixelStart(pPixel);
                            threadPixel = pPixel;
                            // Render samples in pixel _pPixel_
                            for (int sampleIndex = waveStartCopy; sampleIndex < waveEndCopy; ++sampleIndex)
                            {
                                threadSampleIndex = sampleIndex;
                                sampler.StartPixelSample(pPixel, sampleIndex);
                                EvaluatePixelSample(pPixel, sampleIndex, sampler, scratchBuffer);
                                scratchBuffer.Reset();
                            }

                            StatsReportPixelEnd(pPixel);
                        }
                    }
                    catch (Exception)
                    {
                        Console.Error.WriteLine(StringPrintf("Rendering failed at pixel (%d, %d) sample %d. Debug with " +
                                                             "\"--debugstart %d,%d,%d\"\n",
                                                             threadPixel.x, threadPixel.y, threadSampleIndex,
                                                             threadPixel.x, threadPixel.y, threadSampleIndex));
                        throw;
                    }
                    progress.Update((waveEndCopy - waveStartCopy) * tileBounds.Area());
                });

                // (not in pbrt-v4) Progressive null-tracking media learn their majorants between waves,
                // and need many short passes
                bool progressivePasses = TextureMedium.AnyProgressive();
                if (progressivePasses)
                    TextureMedium.EndProgressivePass();

                // Update start and end wave
                waveStart = waveEnd;
                waveEnd = std.min(spp, waveEnd + nextWaveSize);
                if (referenceImage == null && !progressivePasses)
                    nextWaveSize = std.min(2 * nextWaveSize, 64);
                if (waveStart == spp)
                    progress.Done();

                // Optionally write current image to disk
                if (waveStart == spp || Options.writePartialImages || referenceImage != null)
                {
                    LOG_VERBOSE("Writing image with spp = %d", waveStart);
                    ImageMetadata metadata = new ImageMetadata();
                    metadata.renderTimeSeconds = (float)progress.ElapsedSeconds();
                    metadata.samplesPerPixel = waveStart;
                    if (referenceImage != null)
                    {
                        ImageMetadata filmMetadata = new ImageMetadata();
                        Image filmImage =
                            camera.GetFilm().GetImage(filmMetadata, 1.0f / waveStart);
                        ImageChannelValues mse =
                            filmImage.MSE(filmImage.AllChannelsDesc(), referenceImage);
                        mseOutFile.Write(StringPrintf("%d, %.9g\n", waveStart, mse.Average()));
                        metadata.MSE = (float)mse.Average();
                        mseOutFile.Flush();
                    }
                    if (waveStart == spp || Options.writePartialImages)
                    {
                        camera.InitMetadata(metadata);
                        camera.GetFilm().WriteImage(metadata, 1.0f / waveStart);
                    }
                }
            }

            if (mseOutFile != null)
                mseOutFile.Close();
            LOG_VERBOSE("Rendering finished");
        }

        public abstract void EvaluatePixelSample(Point2i pPixel, int sampleIndex, Sampler sampler,
                                                 ScratchBuffer scratchBuffer);

        // ImageTileIntegrator Protected Members
        protected Camera camera;
        protected Sampler samplerPrototype;
    }

    // RayIntegrator Definition
    public abstract unsafe class RayIntegrator : ImageTileIntegrator
    {
        // RayIntegrator Public Methods
        public RayIntegrator(Camera camera, Sampler sampler, Primitive aggregate,
                             List<Light> lights)
            : base(camera, sampler, aggregate, lights) { }

        public sealed override void EvaluatePixelSample(Point2i pPixel, int sampleIndex, Sampler sampler,
                                                        ScratchBuffer scratchBuffer)
        {
            // Sample wavelengths for the ray
            Float lu = sampler.Get1D();
            if (Options.disableWavelengthJitter)
                lu = 0.5f;
            SampledWavelengths lambda = camera.GetFilm().SampleWavelengths(lu);

            // Initialize _CameraSample_ for current sample
            Filter filter = camera.GetFilm().GetFilter();
            CameraSample cameraSample = GetCameraSample(sampler, pPixel, filter);

            // Generate camera ray for current sample
            pstd.optional<CameraRayDifferential> cameraRay =
                camera.GenerateRayDifferential(cameraSample, ref lambda);

            // Trace _cameraRay_ if valid
            SampledSpectrum L = new SampledSpectrum(0.0f);
            VisibleSurface visibleSurface = new VisibleSurface();
            if (cameraRay)
            {
                // Double check that the ray's direction is normalized.
                DCHECK_GT(Length(cameraRay.value().ray.d), (Float).999f);
                DCHECK_LT(Length(cameraRay.value().ray.d), (Float)1.001f);
                // Scale camera ray differentials based on image sampling rate
                Float rayDiffScale =
                    std.max<Float>(.125f, 1 / std.sqrt((Float)sampler.SamplesPerPixel()));
                if (!Options.disablePixelJitter)
                    cameraRay.Ref().ray.ScaleDifferentials(rayDiffScale);

                ++nCameraRays;
                // Evaluate radiance along camera ray
                bool initializeVisibleSurface = camera.GetFilm().UsesVisibleSurface();
                L = cameraRay.value().weight * Li(cameraRay.value().ray, ref lambda, sampler, scratchBuffer,
                                                  initializeVisibleSurface ? visibleSurface : null);

                // Issue warning if unexpected radiance value is returned
                if (L.HasNaNs())
                {
                    LOG_ERROR("Not-a-number radiance value returned for pixel (%d, " +
                              "%d), sample %d. Setting to black.",
                              pPixel.x, pPixel.y, sampleIndex);
                    L = new SampledSpectrum(0.0f);
                }
                else if (IsInf(L.y(lambda)))
                {
                    LOG_ERROR("Infinite radiance value returned for pixel (%d, %d), " +
                              "sample %d. Setting to black.",
                              pPixel.x, pPixel.y, sampleIndex);
                    L = new SampledSpectrum(0.0f);
                }
            }
            // Add camera ray's contribution to image
            if (!discardSamples)
                camera.GetFilm().AddSample(pPixel, L, lambda, visibleSurface,
                                           cameraSample.filterWeight);
        }

        public abstract SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda,
                                           Sampler sampler, ScratchBuffer scratchBuffer,
                                           VisibleSurface visibleSurface);

        // (not in pbrt-v4) Warm-up passes are traced but not added to the film
        public static volatile bool discardSamples;
    }

    // RandomWalkIntegrator Definition
    public sealed unsafe class RandomWalkIntegrator : RayIntegrator
    {
        // RandomWalkIntegrator Public Methods
        public RandomWalkIntegrator(int maxDepth, Camera camera, Sampler sampler,
                                    Primitive aggregate, List<Light> lights)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                           ScratchBuffer scratchBuffer, VisibleSurface visibleSurface)
        {
            return LiRandomWalk(ray, ref lambda, sampler, scratchBuffer, 0);
        }

        // RandomWalkIntegrator Private Methods
        SampledSpectrum LiRandomWalk(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                     ScratchBuffer scratchBuffer, int depth)
        {
            // Intersect ray with scene and return if no intersection
            pstd.optional<ShapeIntersection> si = Intersect(new Ray(ray));
            if (!si)
            {
                // Return emitted light from infinite light sources
                SampledSpectrum Le0 = new SampledSpectrum(0.0f);
                foreach (Light light in infiniteLights)
                    Le0 += light.Le(new Ray(ray), lambda);
                return Le0;
            }
            SurfaceInteraction isect = si.value().intr;

            // Get emitted radiance at surface intersection
            Vector3f wo = -ray.d;
            SampledSpectrum Le = isect.Le(wo, lambda);

            // Terminate random walk if maximum depth has been reached
            if (depth == maxDepth)
                return Le;

            // Compute BSDF at random walk intersection point
            BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
            if (!bsdf)
                return Le;

            // Randomly sample direction leaving surface for random walk
            Point2f u = sampler.Get2D();
            Vector3f wp = SampleUniformSphere(u);

            // Evaluate BSDF at surface for sampled direction
            SampledSpectrum fcos = bsdf.f(wo, wp) * AbsDot(wp, isect.shading.n);
            if (!fcos)
                return Le;

            // Recursively trace ray to estimate incident radiance at surface
            ray = isect.SpawnRay(wp);
            return Le + fcos * LiRandomWalk(ray, ref lambda, sampler, scratchBuffer, depth + 1) /
                            (1 / (4 * Pi));
        }

        // RandomWalkIntegrator Method Definitions
        public static RandomWalkIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                                  Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            return new RandomWalkIntegrator(maxDepth, camera, sampler, aggregate, lights);
        }

        public override string ToString()
        {
            return StringPrintf("[ RandomWalkIntegrator maxDepth: %d ]", maxDepth);
        }

        // RandomWalkIntegrator Private Members
        int maxDepth;
    }

    // SimplePathIntegrator Definition
    public sealed unsafe class SimplePathIntegrator : RayIntegrator
    {
        // SimplePathIntegrator Method Definitions
        public SimplePathIntegrator(int maxDepth, bool sampleLights, bool sampleBSDF, Camera camera,
                                    Sampler sampler, Primitive aggregate, List<Light> lights)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
            this.sampleLights = sampleLights;
            this.sampleBSDF = sampleBSDF;
            this.lightSampler = new UniformLightSampler(lights.ToArray(), new Allocator());
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                           ScratchBuffer scratchBuffer, VisibleSurface visibleSurface)
        {
            // Estimate radiance along ray using simple path tracing
            SampledSpectrum L = new SampledSpectrum(0.0f), beta = new SampledSpectrum(1.0f);
            bool specularBounce = true;
            int depth = 0;
            while ((bool)beta)
            {
                // Find next _SimplePathIntegrator_ vertex and accumulate contribution
                // Intersect _ray_ with scene
                pstd.optional<ShapeIntersection> si = Intersect(new Ray(ray));

                // Account for infinite lights if ray has no intersection
                if (!si)
                {
                    if (!sampleLights || specularBounce)
                        foreach (Light light in infiniteLights)
                            L += beta * light.Le(new Ray(ray), lambda);
                    break;
                }

                // Account for emissive surface if light was not sampled
                SurfaceInteraction isect = si.value().intr;
                if (!sampleLights || specularBounce)
                    L += beta * isect.Le(-ray.d, lambda);

                // End path if maximum depth reached
                if (depth++ == maxDepth)
                    break;

                // Get BSDF and skip over medium boundaries
                BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                if (!bsdf)
                {
                    specularBounce = true;
                    isect.SkipIntersection(ref ray, si.value().tHit);
                    continue;
                }

                // Sample direct illumination if _sampleLights_ is true
                Vector3f wo = -ray.d;
                if (sampleLights)
                {
                    pstd.optional<SampledLight> sampledLight = lightSampler.Sample(sampler.Get1D());
                    if (sampledLight)
                    {
                        // Sample point on _sampledLight_ to estimate direct illumination
                        Point2f uLight = sampler.Get2D();
                        pstd.optional<LightLiSample> ls =
                            sampledLight.value().light.SampleLi(new LightSampleContext(isect), uLight, lambda);
                        if (ls && (bool)ls.value().L && ls.value().pdf > 0)
                        {
                            // Evaluate BSDF for light and possibly add scattered radiance
                            Vector3f wi = ls.value().wi;
                            SampledSpectrum f = bsdf.f(wo, wi) * AbsDot(wi, isect.shading.n);
                            if ((bool)f && Unoccluded(isect, ls.value().pLight))
                                L += beta * f * ls.value().L / (sampledLight.value().p * ls.value().pdf);
                        }
                    }
                }

                // Sample outgoing direction at intersection to continue path
                if (sampleBSDF)
                {
                    // Sample BSDF for new path direction
                    Float u = sampler.Get1D();
                    pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, u, sampler.Get2D());
                    if (!bs)
                        break;
                    beta *= bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                    specularBounce = bs.value().IsSpecular();
                    ray = isect.SpawnRay(bs.value().wi);
                }
                else
                {
                    // Uniformly sample sphere or hemisphere to get new path direction
                    Float pdf;
                    Vector3f wi;
                    BxDFFlags flags = bsdf.Flags();
                    if (IsReflective(flags) && IsTransmissive(flags))
                    {
                        wi = SampleUniformSphere(sampler.Get2D());
                        pdf = UniformSpherePDF();
                    }
                    else
                    {
                        wi = SampleUniformHemisphere(sampler.Get2D());
                        pdf = UniformHemispherePDF();
                        if (IsReflective(flags) && Dot(wo, isect.n) * Dot(wi, isect.n) < 0)
                            wi = -wi;
                        else if (IsTransmissive(flags) && Dot(wo, isect.n) * Dot(wi, isect.n) > 0)
                            wi = -wi;
                    }
                    beta *= bsdf.f(wo, wi) * AbsDot(wi, isect.shading.n) / pdf;
                    specularBounce = false;
                    ray = isect.SpawnRay(wi);
                }

                CHECK_GE(beta.y(lambda), (Float)0);
                DCHECK(!IsInf(beta.y(lambda)));
            }
            return L;
        }

        public override string ToString()
        {
            return StringPrintf("[ SimplePathIntegrator maxDepth: %d sampleLights: %s sampleBSDF: %s ]",
                                maxDepth, sampleLights, sampleBSDF);
        }

        public static SimplePathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                                  Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            bool sampleLights = parameters.GetOneBool("samplelights", true);
            bool sampleBSDF = parameters.GetOneBool("samplebsdf", true);
            return new SimplePathIntegrator(maxDepth, sampleLights, sampleBSDF, camera, sampler, aggregate, lights);
        }

        // SimplePathIntegrator Private Members
        int maxDepth;
        bool sampleLights, sampleBSDF;
        UniformLightSampler lightSampler;
    }

    // LightPathIntegrator Definition
    public sealed unsafe class LightPathIntegrator : ImageTileIntegrator
    {
        // LightPathIntegrator Method Definitions
        public LightPathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive aggregate,
                                   List<Light> lights)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
            this.lightSampler = new PowerLightSampler(lights.ToArray(), new Allocator());
        }

        public override void EvaluatePixelSample(Point2i pPixel, int sampleIndex, Sampler sampler,
                                                 ScratchBuffer scratchBuffer)
        {
            // Sample wavelengths for the ray
            Float lu = sampler.Get1D();
            if (Options.disableWavelengthJitter)
                lu = 0.5f;
            SampledWavelengths lambda = camera.GetFilm().SampleWavelengths(lu);

            // Sample light to start light path
            Float ul = sampler.Get1D();
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(ul);
            if (!sampledLight)
                return;
            Light light = sampledLight.value().light;
            Float p_l = sampledLight.value().p;

            // Sample point on light source for light path
            Float time = camera.SampleTime(sampler.Get1D());
            Point2f ul0 = sampler.Get2D();
            Point2f ul1 = sampler.Get2D();
            pstd.optional<LightLeSample> les = light.SampleLe(ul0, ul1, ref lambda, time);
            if (!les || les.value().pdfPos == 0 || les.value().pdfDir == 0 || !les.value().L)
                return;

            // Add contribution of directly visible light source
            if (les.value().intr)
            {
                pstd.optional<CameraWiSample> cs =
                    camera.SampleWi(les.value().intr.value(), sampler.Get2D(), ref lambda);
                if (cs && cs.value().pdf != 0)
                {
                    Float pdf = light.PDF_Li(new LightSampleContext(cs.value().pLens), -cs.value().wi);
                    if (pdf > 0)
                    {
                        // Add light's emitted radiance if nonzero and light is visible
                        SampledSpectrum Le =
                            light.L(les.value().intr.value().p(), les.value().intr.value().n,
                                    les.value().intr.value().uv, cs.value().wi, lambda);
                        if ((bool)Le && Unoccluded(cs.value().pRef, cs.value().pLens))
                        {
                            // Compute visible light's path contribution and add to film
                            SampledSpectrum L = Le *
                                                DistanceSquared(cs.value().pRef.p(), cs.value().pLens.p()) *
                                                cs.value().Wi / (p_l * pdf * cs.value().pdf);
                            camera.GetFilm().AddSplat(cs.value().pRaster, L, lambda);
                        }
                    }
                }
            }

            // Follow light path and accumulate contributions to image
            int depth = 0;
            // Initialize light path ray and weighted path throughput _beta_
            RayDifferential ray = new RayDifferential(les.value().ray);
            SampledSpectrum beta =
                les.value().L * les.value().AbsCosTheta(ray.d) / (p_l * les.value().pdfPos * les.value().pdfDir);

            while (true)
            {
                // Intersect light path ray with scene
                pstd.optional<ShapeIntersection> si = Intersect(new Ray(ray));
                if (!si)
                    break;
                SurfaceInteraction isect = si.value().intr;

                // Get BSDF and skip over medium boundaries
                BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                if (!bsdf)
                {
                    isect.SkipIntersection(ref ray, si.value().tHit);
                    continue;
                }

                // End path if maximum depth reached
                if (depth++ == maxDepth)
                    break;

                // Splat contribution into film if intersection point is visible to camera
                Point2f u = sampler.Get2D();
                pstd.optional<CameraWiSample> cs2 = camera.SampleWi(isect, u, ref lambda);
                if (cs2 && cs2.value().pdf != 0)
                {
                    SampledSpectrum L = beta *
                                        bsdf.f(isect.wo, cs2.value().wi, TransportMode.Importance) *
                                        AbsDot(cs2.value().wi, isect.shading.n) * cs2.value().Wi / cs2.value().pdf;
                    if ((bool)L && Unoccluded(cs2.value().pRef, cs2.value().pLens))
                        camera.GetFilm().AddSplat(cs2.value().pRaster, L, lambda);
                }

                // Sample BSDF and update light path state
                Float uc = sampler.Get1D();
                pstd.optional<BSDFSample> bs =
                    bsdf.Sample_f(isect.wo, uc, sampler.Get2D(), TransportMode.Importance, BxDFReflTransFlags.All);
                if (!bs)
                    break;
                beta *= bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                ray = isect.SpawnRay(ray, bsdf, bs.value().wi, bs.value().flags, bs.value().eta);
            }
        }

        public override string ToString()
        {
            return StringPrintf("[ LightPathIntegrator maxDepth: %d lightSampler: %s ]", maxDepth, lightSampler);
        }

        public static LightPathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                                 Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            if (!(camera is PerspectiveCamera))
                ErrorExit("Only the \"perspective\" camera is currently supported with the " +
                          "\"lightpath\" integrator.");
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            return new LightPathIntegrator(maxDepth, camera, sampler, aggregate, lights);
        }

        // LightPathIntegrator Private Members
        int maxDepth;
        PowerLightSampler lightSampler;
    }

    // PathIntegrator Definition
    public sealed unsafe class PathIntegrator : RayIntegrator
    {
        // STAT_PERCENT("Integrator/Zero-radiance paths", zeroRadiancePaths, totalPaths);
        // STAT_PERCENT("Integrator/Regularized BSDFs", regularizedBSDFs, totalBSDFs);
        // STAT_INT_DISTRIBUTION("Integrator/Path length", pathLength);
        [ThreadStatic] internal static long zeroRadiancePaths, totalPaths, regularizedBSDFs, totalBSDFs;
        [ThreadStatic] internal static StatIntDistribution pathLength;
        static StatRegisterer STATS_REG = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportPercentage("Integrator/Zero-radiance paths", zeroRadiancePaths, totalPaths);
            zeroRadiancePaths = totalPaths = 0;
            accum.ReportPercentage("Integrator/Regularized BSDFs", regularizedBSDFs, totalBSDFs);
            regularizedBSDFs = totalBSDFs = 0;
            if (pathLength != null)
            {
                accum.ReportIntDistribution("Integrator/Path length", pathLength.sum, pathLength.count,
                                            pathLength.min, pathLength.max);
                pathLength = null;
            }
        });

        // PathIntegrator Method Definitions
        public PathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive aggregate,
                              List<Light> lights, string lightSampleStrategy = "bvh", bool regularize = false)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
            this.lightSampler = LightSampler.Create(lightSampleStrategy, lights.ToArray(), new Allocator());
            this.regularize = regularize;
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                           ScratchBuffer scratchBuffer, VisibleSurface visibleSurf)
        {
            // Declare local variables for _PathIntegrator::Li()_
            SampledSpectrum L = new SampledSpectrum(0.0f), beta = new SampledSpectrum(1.0f);
            int depth = 0;

            Float p_b = 0, etaScale = 1;
            bool specularBounce = false, anyNonSpecularBounces = false;
            LightSampleContext prevIntrCtx = default;

            // Sample path from camera and accumulate radiance estimate
            while (true)
            {
                // Trace ray and find closest path vertex and its BSDF
                pstd.optional<ShapeIntersection> si = Intersect(new Ray(ray));
                // Add emitted light at intersection point or from the environment
                if (!si)
                {
                    // Incorporate emission from infinite lights for escaped ray
                    foreach (Light light in infiniteLights)
                    {
                        SampledSpectrum Le0 = light.Le(new Ray(ray), lambda);
                        if (depth == 0 || specularBounce)
                            L += beta * Le0;
                        else
                        {
                            // Compute MIS weight for infinite light
                            Float p_l = lightSampler.PMF(prevIntrCtx, light) *
                                        light.PDF_Li(prevIntrCtx, ray.d, true);
                            Float w_b = PowerHeuristic(1, p_b, 1, p_l);

                            L += beta * w_b * Le0;
                        }
                    }

                    break;
                }
                // Incorporate emission from surface hit by ray
                SampledSpectrum Le = si.value().intr.Le(-ray.d, lambda);
                if ((bool)Le)
                {
                    if (depth == 0 || specularBounce)
                        L += beta * Le;
                    else
                    {
                        // Compute MIS weight for area light
                        Light areaLight = si.value().intr.areaLight;
                        Float p_l = lightSampler.PMF(prevIntrCtx, areaLight) *
                                    areaLight.PDF_Li(prevIntrCtx, ray.d, true);
                        Float w_l = PowerHeuristic(1, p_b, 1, p_l);

                        L += beta * w_l * Le;
                    }
                }

                SurfaceInteraction isect = si.value().intr;
                // Get BSDF and skip over medium boundaries
                BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                if (!bsdf)
                {
                    specularBounce = true;  // disable MIS if the indirect ray hits a light
                    isect.SkipIntersection(ref ray, si.value().tHit);
                    continue;
                }

                // Initialize _visibleSurf_ at first intersection
                if (depth == 0 && visibleSurf != null)
                {
                    // Estimate BSDF's albedo
                    // Define sample arrays _ucRho_ and _uRho_ for reflectance estimate
                    const int nRhoSamples = 16;
                    Float[] ucRho = ToFloats(
                        0.75741637, 0.37870818, 0.7083487, 0.18935409, 0.9149363, 0.35417435,
                        0.5990858, 0.09467703, 0.8578725, 0.45746812, 0.686759, 0.17708716,
                        0.9674518, 0.2995429, 0.5083201, 0.047338516);
                    Point2f[] uRho = new Point2f[nRhoSamples] {
                        P2(0.855985, 0.570367), P2(0.381823, 0.851844),
                        P2(0.285328, 0.764262), P2(0.733380, 0.114073),
                        P2(0.542663, 0.344465), P2(0.127274, 0.414848),
                        P2(0.964700, 0.947162), P2(0.594089, 0.643463),
                        P2(0.095109, 0.170369), P2(0.825444, 0.263359),
                        P2(0.429467, 0.454469), P2(0.244460, 0.816459),
                        P2(0.756135, 0.731258), P2(0.516165, 0.152852),
                        P2(0.180888, 0.214174), P2(0.898579, 0.503897)};

                    SampledSpectrum albedo = bsdf.rho(isect.wo, ucRho, uRho);

                    visibleSurf.Assign(new VisibleSurface(isect, albedo, lambda));
                }

                // Possibly regularize the BSDF
                if (regularize && anyNonSpecularBounces)
                {
                    ++regularizedBSDFs;
                    bsdf.Regularize();
                }

                ++totalBSDFs;

                // End path if maximum depth reached
                if (depth++ == maxDepth)
                    break;

                // Sample direct illumination from the light sources
                if (IsNonSpecular(bsdf.Flags()))
                {
                    ++totalPaths;
                    SampledSpectrum Ld = SampleLd(isect, bsdf, ref lambda, sampler);
                    if (!Ld)
                        ++zeroRadiancePaths;
                    L += beta * Ld;
                }

                // Sample BSDF to get new path direction
                Vector3f wo = -ray.d;
                Float u = sampler.Get1D();
                pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, u, sampler.Get2D());
                if (!bs)
                    break;
                // Update path state variables after surface scattering
                beta *= bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                p_b = bs.value().pdfIsProportional ? bsdf.PDF(wo, bs.value().wi) : bs.value().pdf;
                DCHECK(!IsInf(beta.y(lambda)));
                specularBounce = bs.value().IsSpecular();
                anyNonSpecularBounces |= !bs.value().IsSpecular();
                if (bs.value().IsTransmission())
                    etaScale *= Sqr(bs.value().eta);
                prevIntrCtx = new LightSampleContext(si.value().intr);

                ray = isect.SpawnRay(ray, bsdf, bs.value().wi, bs.value().flags, bs.value().eta);

                // Possibly terminate the path with Russian roulette
                SampledSpectrum rrBeta = beta * etaScale;
                if (rrBeta.MaxComponentValue() < 1 && depth > 1)
                {
                    Float q = std.max<Float>(0, 1 - rrBeta.MaxComponentValue());
                    if (sampler.Get1D() < q)
                        break;
                    beta /= 1 - q;
                    DCHECK(!IsInf(beta.y(lambda)));
                }
            }
            if (pathLength == null)
                pathLength = new StatIntDistribution();
            pathLength.Add(depth);
            return L;
        }

        static Float[] ToFloats(params double[] v)
        {
            Float[] r = new Float[v.Length];
            for (int i = 0; i < v.Length; ++i)
                r[i] = (Float)v[i];
            return r;
        }

        static Point2f P2(double x, double y) { return new Point2f((Float)x, (Float)y); }

        SampledSpectrum SampleLd(SurfaceInteraction intr, BSDF bsdf, ref SampledWavelengths lambda, Sampler sampler)
        {
            // Initialize _LightSampleContext_ for light sampling
            LightSampleContext ctx = new LightSampleContext(intr);
            // Try to nudge the light sampling position to correct side of the surface
            BxDFFlags flags = bsdf.Flags();
            if (IsReflective(flags) && !IsTransmissive(flags))
                ctx.pi = intr.OffsetRayOrigin(intr.wo);
            else if (IsTransmissive(flags) && !IsReflective(flags))
                ctx.pi = intr.OffsetRayOrigin(-intr.wo);

            // Choose a light source for the direct lighting calculation
            Float u = sampler.Get1D();
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(ctx, u);
            Point2f uLight = sampler.Get2D();
            if (!sampledLight)
                return new SampledSpectrum(0.0f);

            // Sample a point on the light source for direct lighting
            Light light = sampledLight.value().light;
            DCHECK(light != null && sampledLight.value().p > 0);
            pstd.optional<LightLiSample> ls = light.SampleLi(ctx, uLight, lambda, true);
            if (!ls || !ls.value().L || ls.value().pdf == 0)
                return new SampledSpectrum(0.0f);

            // Evaluate BSDF for light sample and check light visibility
            Vector3f wo = intr.wo, wi = ls.value().wi;
            SampledSpectrum f = bsdf.f(wo, wi) * AbsDot(wi, intr.shading.n);
            if (!f || !Unoccluded(intr, ls.value().pLight))
                return new SampledSpectrum(0.0f);

            // Return light's contribution to reflected radiance
            Float p_l = sampledLight.value().p * ls.value().pdf;
            if (IsDeltaLight(light.Type()))
                return ls.value().L * f / p_l;
            else
            {
                Float p_b = bsdf.PDF(wo, wi);
                Float w_l = PowerHeuristic(1, p_l, 1, p_b);
                return w_l * ls.value().L * f / p_l;
            }
        }

        public override string ToString()
        {
            return StringPrintf("[ PathIntegrator maxDepth: %d lightSampler: %s regularize: %s ]",
                                maxDepth, lightSampler, regularize);
        }

        public static PathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                            Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            string lightStrategy = parameters.GetOneString("lightsampler", "bvh");
            bool regularize = parameters.GetOneBool("regularize", false);
            return new PathIntegrator(maxDepth, camera, sampler, aggregate, lights, lightStrategy, regularize);
        }

        // PathIntegrator Private Members
        int maxDepth;
        LightSampler lightSampler;
        bool regularize;
    }

    // SimpleVolPathIntegrator Definition
    public sealed unsafe class SimpleVolPathIntegrator : RayIntegrator
    {
        // SimpleVolPathIntegrator Method Definitions
        public SimpleVolPathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive aggregate,
                                       List<Light> lights)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
            foreach (Light light in lights)
            {
                if (IsDeltaLight(light.Type()))
                    ErrorExit("SimpleVolPathIntegrator only supports area and infinite light sources");
            }
        }

        public override SampledSpectrum Li(RayDifferential ray0, ref SampledWavelengths lambdaRef, Sampler sampler,
                                           ScratchBuffer buf, VisibleSurface visibleSurface)
        {
            // (Lambdas cannot capture ref parameters: work on local copies and write back.)
            SampledWavelengths lambda = lambdaRef;
            RayDifferential ray = ray0;
            try
            {
                // Declare local variables for delta tracking integration
                SampledSpectrum L = new SampledSpectrum(0.0f);
                Float beta = 1f;
                int depth = 0;

                // Terminate secondary wavelengths before starting random walk
                lambda.TerminateSecondary();

                while (true)
                {
                    // Estimate radiance for ray path using delta tracking
                    pstd.optional<ShapeIntersection> si = Intersect(new Ray(ray));
                    bool scattered = false, terminated = false;
                    if (ray.medium != null)
                    {
                        // Initialize _RNG_ for sampling the majorant transmittance
                        uint64_t hash0 = Hash(sampler.Get1D());
                        uint64_t hash1 = Hash(sampler.Get1D());
                        RNG rng = new RNG(hash0, hash1);

                        // Sample medium using delta tracking
                        Float tMax = si ? si.value().tHit : Infinity;
                        Float u = sampler.Get1D();
                        Float uMode = sampler.Get1D();
                        RayDifferential rayCap = ray;
                        SampledSpectrum Lcap = L;
                        Float betaCap = beta;
                        int depthCap = depth;
                        bool scatteredCap = false, terminatedCap = false;
                        Vector3f newD = default;
                        Point3f newO = default;
                        SampleT_maj(new Ray(ray), tMax, u, ref rng, lambda,
                                    (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj,
                                     SampledSpectrum T_maj) =>
                                    {
                                        // Compute medium event probabilities for interaction
                                        Float pAbsorb = mp.sigma_a[0] / sigma_maj[0];
                                        Float pScatter = mp.sigma_s[0] / sigma_maj[0];
                                        Float pNull = std.max<Float>(0, 1 - pAbsorb - pScatter);

                                        // Randomly sample medium scattering event for delta tracking
                                        int mode = SampleDiscrete(new Float[] { pAbsorb, pScatter, pNull }, uMode);
                                        if (mode == 0)
                                        {
                                            // Handle absorption event for medium sample
                                            Lcap += betaCap * mp.Le;
                                            terminatedCap = true;
                                            return false;
                                        }
                                        else if (mode == 1)
                                        {
                                            // Handle regular scattering event for medium sample
                                            // Stop path sampling if maximum depth has been reached
                                            if (depthCap++ >= maxDepth)
                                            {
                                                terminatedCap = true;
                                                return false;
                                            }

                                            // Sample phase function for medium scattering event
                                            Point2f u2 = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                                            pstd.optional<PhaseFunctionSample> ps =
                                                mp.phase.Sample_p(-rayCap.d, u2);
                                            if (!ps)
                                            {
                                                terminatedCap = true;
                                                return false;
                                            }

                                            // Update state for recursive evaluation of $L_\roman{i}$
                                            betaCap *= ps.value().p / ps.value().pdf;
                                            newO = p;
                                            newD = ps.value().wi;
                                            scatteredCap = true;
                                            return false;
                                        }
                                        else
                                        {
                                            // Handle null-scattering event for medium sample
                                            uMode = rng.Uniform<Float>();
                                            return true;
                                        }
                                    });
                        L = Lcap;
                        beta = betaCap;
                        depth = depthCap;
                        terminated = terminatedCap;
                        scattered = scatteredCap;
                        if (scattered)
                        {
                            ray.o = newO;
                            ray.d = newD;
                        }
                    }
                    // Handle terminated and unscattered rays after medium sampling
                    if (terminated)
                        return L;
                    if (scattered)
                        continue;
                    // Add emission to surviving ray
                    if (si)
                        L += beta * si.value().intr.Le(-ray.d, lambda);
                    else
                    {
                        foreach (Light light in infiniteLights)
                            L += beta * light.Le(new Ray(ray), lambda);
                        return L;
                    }

                    // Handle surface intersection along ray path
                    SurfaceInteraction sIntr = si.value().intr;
                    BSDF bsdf = sIntr.GetBSDF(ray, ref lambda, camera, buf, sampler);
                    if (!bsdf)
                        sIntr.SkipIntersection(ref ray, si.value().tHit);
                    else
                    {
                        // Report error if BSDF returns a valid sample
                        Float uc = sampler.Get1D();
                        Point2f u = sampler.Get2D();
                        if (bsdf.Sample_f(-ray.d, uc, u))
                            ErrorExit("SimpleVolPathIntegrator doesn't support surface scattering.");
                        else
                            break;
                    }
                }
                return L;
            }
            finally
            {
                lambdaRef = lambda;
            }
        }

        public override string ToString()
        {
            return StringPrintf("[ SimpleVolPathIntegrator maxDepth: %d ] ", maxDepth);
        }

        public static SimpleVolPathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                                     Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            return new SimpleVolPathIntegrator(maxDepth, camera, sampler, aggregate, lights);
        }

        // SimpleVolPathIntegrator Private Members
        int maxDepth;
    }

    // AOIntegrator Definition
    public sealed unsafe class AOIntegrator : RayIntegrator
    {
        // AOIntegrator Method Definitions
        public AOIntegrator(bool cosSample, Float maxDist, Camera camera, Sampler sampler, Primitive aggregate,
                            List<Light> lights, Spectrum illuminant)
            : base(camera, sampler, aggregate, lights)
        {
            this.cosSample = cosSample;
            this.maxDist = maxDist;
            this.illuminant = illuminant;
            this.illumScale = 1f / SpectrumToPhotometric(illuminant);
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                           ScratchBuffer scratchBuffer, VisibleSurface visibleSurface)
        {
            // Intersect _ray_ with scene and store intersection in _isect_
            pstd.optional<ShapeIntersection> si;
        retry:
            si = Intersect(new Ray(ray));
            if (si)
            {
                SurfaceInteraction isect = si.value().intr;
                BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                if (!bsdf)
                {
                    isect.SkipIntersection(ref ray, si.value().tHit);
                    goto retry;
                }

                // Compute coordinate frame based on true geometry, not shading
                // geometry.
                Normal3f n = FaceForward(isect.n, -ray.d);

                Vector3f wi;
                Float pdf;
                Point2f u = sampler.Get2D();
                if (cosSample)
                {
                    wi = SampleCosineHemisphere(u);
                    pdf = CosineHemispherePDF(std.abs(wi.z));
                }
                else
                {
                    wi = SampleUniformHemisphere(u);
                    pdf = UniformHemispherePDF();
                }
                if (pdf == 0)
                    return new SampledSpectrum(0.0f);

                Frame f = Frame.FromZ(n);
                wi = f.FromLocal(wi);

                // Divide by pi so that fully visible is one.
                Ray r = isect.SpawnRay(wi);
                if (!IntersectP(r, maxDist))
                {
                    return illumScale * illuminant.Sample(lambda) * Dot(wi, n) / (Pi * pdf);
                }
            }
            return new SampledSpectrum(0.0f);
        }

        public override string ToString()
        {
            return StringPrintf("[ AOIntegrator cosSample: %s maxDist: %f illuminant: %s ]",
                                cosSample, maxDist, illuminant);
        }

        public static AOIntegrator Create(ParameterDictionary parameters, Spectrum illuminant, Camera camera,
                                          Sampler sampler, Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            bool cosSample = parameters.GetOneBool("cossample", true);
            Float maxDist = parameters.GetOneFloat("maxdistance", Infinity);
            return new AOIntegrator(cosSample, maxDist, camera, sampler, aggregate, lights, illuminant);
        }

        bool cosSample;
        Float maxDist;
        Spectrum illuminant;
        Float illumScale;
    }

    // VertexType Definition
    public enum VertexType { Camera, Light, Surface, Medium }

    // Types that are copied by value in C++ but are classes here (so "*target = value" copies the contents).
    public interface IAssignable<T>
    {
        T Clone();
        void Assign(T other);
    }

    // ScopedAssignment Definition
    // (a ref struct holding a reference in place of the C++ Type *target; "Type *target = nullptr" is the
    // default value and the C++ destructor is Dispose(), which is called explicitly in reverse order of
    // construction.)
    public ref struct ScopedAssignment<Type>
    {
        private static readonly bool assignable = typeof(IAssignable<Type>).IsAssignableFrom(typeof(Type));

        // ScopedAssignment Public Methods
        public ScopedAssignment(ref Type target, Type value)
        {
            this.target = ref target;
            backup = default;
            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref this.target))
            {
                backup = Get();
                Set(value);
            }
        }
        public void Dispose()
        {
            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref target))
                Set(backup);
        }

        private Type Get()
        {
            if (assignable)
                return ((IAssignable<Type>)target).Clone();
            return target;
        }
        private void Set(Type v)
        {
            if (assignable)
                ((IAssignable<Type>)target).Assign(v);
            else
                target = v;
        }

        private ref Type target;
        private Type backup;
    }

    // EndpointInteraction Definition
    public sealed unsafe class EndpointInteraction : Interaction
    {
        // (union { Camera camera; Light light; })
        public Camera camera;
        public Light light;

        // EndpointInteraction Public Methods
        public EndpointInteraction() : base() { light = null; }

        void CopyFrom(Interaction it)
        {
            pi = it.pi; time = it.time; wo = it.wo; n = it.n; uv = it.uv;
            mediumInterface = it.mediumInterface; medium = it.medium;
        }

        public EndpointInteraction(Interaction it, Camera camera) : base()
        {
            CopyFrom(it);
            this.camera = camera;
        }

        public EndpointInteraction(Camera camera, Ray ray) : base(ray.o, ray.time, ray.medium)
        {
            this.camera = camera;
        }

        public EndpointInteraction(EndpointInteraction ei) : base()
        {
            CopyFrom(ei);
            camera = ei.camera;
            light = ei.light;
        }

        public EndpointInteraction(Light light, Interaction intr) : base()
        {
            CopyFrom(intr);
            this.light = light;
        }

        public EndpointInteraction(Light light, Ray r) : base(r.o, r.time, r.medium)
        {
            this.light = light;
        }

        public EndpointInteraction(Ray ray)
            : base(ray.o + ray.d, new Normal3f(-ray.d.x, -ray.d.y, -ray.d.z), ray.time, ray.medium)
        {
            light = null;
        }
    }

    // BDPT Vertex Definition
    public sealed unsafe class Vertex : IAssignable<Vertex>
    {
        // Vertex Public Members
        public VertexType type;
        public SampledSpectrum beta;
        public EndpointInteraction ei;
        public MediumInteraction mi;
        public SurfaceInteraction si;
        public BSDF bsdf;
        public bool delta = false;
        public Float pdfFwd = 0, pdfRev = 0;
        // (not in pbrt-v4) For a vertex that is a point of an emissive volume: the majorant (of the first wavelength) at the
        // point, which is the density of a collision there when the point is found by following a path through the medium.
        public Float sigmaMaj = 0;
        // (not in pbrt-v4) Spectral MIS: how much more (or less) likely the subpath up to this vertex is under each wavelength
        // than under the first one (the hero wavelength), as in the r_u of VolPathIntegrator. It is the product over the
        // majorant transmittances, null collisions and real collisions of the subpath.
        public SampledSpectrum rCum = new SampledSpectrum(1.0f);

        // Vertex Public Methods
        public Vertex() { ei = new EndpointInteraction(); }

        public Vertex(VertexType type, EndpointInteraction ei, SampledSpectrum beta)
        {
            this.type = type; this.beta = beta; this.ei = ei;
        }

        public Vertex(SurfaceInteraction si, BSDF bsdf, SampledSpectrum beta)
        {
            this.type = VertexType.Surface; this.beta = beta; this.si = si; this.bsdf = bsdf;
        }

        public Vertex(MediumInteraction mi, SampledSpectrum beta)
        {
            this.type = VertexType.Medium; this.beta = beta; this.mi = mi;
        }

        // (C++ copies Vertex objects by value with memcpy)
        public void Assign(Vertex v)
        {
            type = v.type; beta = v.beta; ei = v.ei; mi = v.mi; si = v.si; bsdf = v.bsdf;
            delta = v.delta; pdfFwd = v.pdfFwd; pdfRev = v.pdfRev; sigmaMaj = v.sigmaMaj;
            rCum = v.rCum;
        }

        public Vertex Clone() { Vertex v = new Vertex(); v.Assign(this); return v; }

        public static Vertex CreateCamera(Camera camera, Ray ray, SampledSpectrum beta)
        {
            return new Vertex(VertexType.Camera, new EndpointInteraction(camera, ray), beta);
        }

        public static Vertex CreateCamera(Camera camera, Interaction it, SampledSpectrum beta)
        {
            return new Vertex(VertexType.Camera, new EndpointInteraction(it, camera), beta);
        }

        public static Vertex CreateLight(Light light, Ray ray, SampledSpectrum Le, Float pdf)
        {
            Vertex v = new Vertex(VertexType.Light, new EndpointInteraction(light, ray), Le);
            v.pdfFwd = pdf;
            return v;
        }

        public static Vertex CreateLight(Light light, Interaction intr, SampledSpectrum Le, Float pdf)
        {
            Vertex v = new Vertex(VertexType.Light, new EndpointInteraction(light, intr), Le);
            v.pdfFwd = pdf;
            return v;
        }

        public static Vertex CreateLight(EndpointInteraction ei, SampledSpectrum beta, Float pdf)
        {
            Vertex v = new Vertex(VertexType.Light, ei, beta);
            v.pdfFwd = pdf;
            return v;
        }

        public static Vertex CreateSurface(SurfaceInteraction si, BSDF bsdf, SampledSpectrum beta, Float pdf,
                                           Vertex prev)
        {
            Vertex v = new Vertex(si, bsdf, beta);
            v.pdfFwd = prev.ConvertDensity(pdf, v);
            return v;
        }

        public static Vertex CreateMedium(MediumInteraction mi, SampledSpectrum beta, Float pdf, Vertex prev)
        {
            Vertex v = new Vertex(mi, beta);
            v.pdfFwd = prev.ConvertDensity(pdf, v);
            return v;
        }

        public Interaction GetInteraction()
        {
            switch (type)
            {
            case VertexType.Medium:
                return mi;
            case VertexType.Surface:
                return si;
            default:
                return ei;
            }
        }

        public Point3f p() { return GetInteraction().p(); }

        public Float time() { return GetInteraction().time; }
        public Normal3f ng() { return GetInteraction().n; }
        public Normal3f ns()
        {
            if (type == VertexType.Surface)
                return si.shading.n;
            else
                return GetInteraction().n;
        }

        public bool IsOnSurface() { return ng() != new Normal3f(); }

        public SampledSpectrum f(Vertex next, TransportMode mode)
        {
            Vector3f wi = next.p() - p();
            if (LengthSquared(wi) == 0)
                return new SampledSpectrum(0.0f);
            wi = Normalize(wi);
            switch (type)
            {
            case VertexType.Surface:
                return bsdf.f(si.wo, wi, mode);
            case VertexType.Medium:
                return new SampledSpectrum(mi.phase.p(mi.wo, wi));
            default:
                LOG_FATAL("Vertex::f(): Unimplemented");
                return new SampledSpectrum(0.0f);
            }
        }

        public bool IsConnectible()
        {
            switch (type)
            {
            case VertexType.Medium:
                return true;
            case VertexType.Light:
                return ei.light.Type() != LightType.DeltaDirection;
            case VertexType.Camera:
                return true;
            case VertexType.Surface:
                return IsNonSpecular(bsdf.Flags());
            }
            LOG_FATAL("Unhandled vertex type in IsConnectible()");
            return false;
        }

        public bool IsLight()
        {
            return type == VertexType.Light || (type == VertexType.Surface && si.areaLight != null);
        }

        public bool IsDeltaLight()
        {
            return type == VertexType.Light && ei.light != null && pbrt.IsDeltaLight(ei.light.Type());
        }

        public bool IsInfiniteLight()
        {
            return type == VertexType.Light &&
                   (ei.light == null || ei.light.Type() == LightType.Infinite ||
                    ei.light.Type() == LightType.DeltaDirection);
        }

        public SampledSpectrum Le(List<Light> infiniteLights, Vertex v, SampledWavelengths lambda)
        {
            if (!IsLight())
                return new SampledSpectrum(0.0f);
            Vector3f w = v.p() - p();
            if (LengthSquared(w) == 0)
                return new SampledSpectrum(0.0f);
            w = Normalize(w);
            if (IsInfiniteLight())
            {
                // Return emitted radiance for infinite light sources
                SampledSpectrum Le = new SampledSpectrum(0.0f);
                foreach (Light light in infiniteLights)
                    Le += light.Le(new Ray(p(), -w), lambda);
                return Le;
            }
            else if (type == VertexType.Light && ei.light is VolumeEmissionLight)
                return ((VolumeEmissionLight)ei.light).Medium().EmissionAt(p(), lambda);
            else if (si.areaLight != null)
                return si.areaLight.L(si.p(), si.n, si.uv, w, lambda);
            else
                return new SampledSpectrum(0.0f);
        }

        public override string ToString()
        {
            string s = "[ Vertex type: ";
            switch (type)
            {
            case VertexType.Camera: s += "camera"; break;
            case VertexType.Light: s += "light"; break;
            case VertexType.Surface: s += "surface"; break;
            case VertexType.Medium: s += "medium"; break;
            }
            s += StringPrintf(" connectible: %s p: %s ng: %s pdfFwd: %f pdfRev: %f beta: %s",
                              IsConnectible(), p(), ng(), pdfFwd, pdfRev, beta);
            switch (type)
            {
            case VertexType.Surface: s += "\n  bsdf: " + bsdf.ToString(); break;
            case VertexType.Medium: s += "\n  phase: " + mi.phase.ToString(); break;
            default: break;
            }
            s += " ]";
            return s;
        }

        public Float ConvertDensity(Float pdf, Vertex next)
        {
            // Return solid angle density if _next_ is an infinite area light
            if (next.IsInfiniteLight())
                return pdf;

            Vector3f w = next.p() - p();
            if (LengthSquared(w) == 0)
                return 0;
            Float invDist2 = 1 / LengthSquared(w);
            if (next.IsOnSurface())
                pdf *= AbsDot(next.ng(), w * std.sqrt(invDist2));
            pdf *= invDist2;
            // (not in pbrt-v4) A point of an emissive volume has a density per volume, not per area
            if (next.type == VertexType.Light && next.ei.light is VolumeEmissionLight)
                pdf *= next.sigmaMaj;
            return pdf;
        }

        public Float PDF(Integrator integrator, Vertex prev, Vertex next)
        {
            if (type == VertexType.Light)
                return PDFLight(integrator, next);
            // Compute directions to preceding and next vertex
            Vector3f wn = next.p() - p();
            if (LengthSquared(wn) == 0)
                return 0;
            wn = Normalize(wn);
            Vector3f wp = default;
            if (prev != null)
            {
                wp = prev.p() - p();
                if (LengthSquared(wp) == 0)
                    return 0;
                wp = Normalize(wp);
            }
            else
                CHECK(type == VertexType.Camera);

            // Compute directional density depending on the vertex type
            Float pdf = 0, unused = 0;
            if (type == VertexType.Camera)
                ei.camera.PDF_We(new Ray(ei.SpawnRay(wn)), &unused, &pdf);
            else if (type == VertexType.Surface)
                pdf = bsdf.PDF(wp, wn);
            else if (type == VertexType.Medium)
                pdf = mi.phase.p(wp, wn);
            else
                LOG_FATAL("Vertex::PDF(): Unimplemented");

            // Return probability per unit area at vertex _next_
            return ConvertDensity(pdf, next);
        }

        public Float PDFLight(Integrator integrator, Vertex v)
        {
            Vector3f w = v.p() - p();
            Float invDist2 = 1 / LengthSquared(w);
            w *= std.sqrt(invDist2);
            // Compute sampling density _pdf_ for light type
            Float pdf;
            if (IsInfiniteLight())
            {
                // Compute planar sampling density for infinite light sources
                Bounds3f sceneBounds = integrator.aggregate.Bounds();
                Point3f sceneCenter;
                Float sceneRadius;
                sceneBounds.BoundingSphere(&sceneCenter, &sceneRadius);
                pdf = 1 / (Pi * Sqr(sceneRadius));
            }
            else if (IsOnSurface())
            {
                // Compute sampling density at emissive surface
                if (type == VertexType.Light)
                    CHECK(ei.light is DiffuseAreaLight);
                Light light = (type == VertexType.Light) ? ei.light : si.areaLight;
                Float pdfPos, pdfDir;
                light.PDF_Le(GetInteraction(), w, &pdfPos, &pdfDir);
                pdf = pdfDir * invDist2;
            }
            else
            {
                // Compute sampling density for noninfinite light sources
                CHECK(type == VertexType.Light);
                CHECK(ei.light != null);
                Float pdfPos, pdfDir;
                ei.light.PDF_Le(new Ray(p(), w, time()), &pdfPos, &pdfDir);
                pdf = pdfDir * invDist2;
            }

            if (v.IsOnSurface())
                pdf *= AbsDot(v.ng(), w);
            return pdf;
        }

        public Float PDFLightOrigin(List<Light> infiniteLights, Vertex v, LightSampler lightSampler)
        {
            Vector3f w = v.p() - p();
            if (LengthSquared(w) == 0)
                return 0;
            w = Normalize(w);
            if (IsInfiniteLight())
            {
                // Return sampling density for infinite light sources
                return BDPT.InfiniteLightDensity(infiniteLights, lightSampler, w);
            }
            else
            {
                // Return sampling density for noninfinite light source
                Light light = (type == VertexType.Light) ? ei.light : si.areaLight;
                Float pdfPos, pdfDir, pdfChoice = lightSampler.PMF(light);
                if (IsOnSurface())
                    light.PDF_Le(GetInteraction(), w, &pdfPos, &pdfDir);
                else
                    light.PDF_Le(new Ray(p(), w, time()), &pdfPos, &pdfDir);
                return pdfPos * pdfChoice;
            }
        }
    }

    // (not in pbrt-v4) A point of an emissive volume that a camera subpath passed, and the number of the vertex that it
    // would be on the subpath. Together with the vertices before it, it is a complete path: the s = 0 strategy.
    public sealed class EmissionVertexRecord
    {
        public EmissionVertexRecord(Vertex vertex, int index)
        {
            this.vertex = vertex;
            this.index = index;
        }

        public Vertex vertex;
        public int index;
    }

    // (not in pbrt-v4) The volumes whose emission the camera subpaths gather, and what they found
    public sealed class VolumeEmissionGather
    {
        public VolumeEmissionGather(HashSet<Light> lights)
        {
            this.lights = lights;
        }

        public HashSet<Light> lights;
        public List<EmissionVertexRecord> records = new List<EmissionVertexRecord>();
    }

    // BDPT Utility Functions
    public static unsafe class BDPT
    {
        public static int BufferIndex(int s, int t)
        {
            int above = s + t - 2;
            return s + above * (5 + above) / 2;
        }

        public static int GenerateCameraSubpath(Integrator integrator, RayDifferential ray,
                                                ref SampledWavelengths lambda, Sampler sampler,
                                                ScratchBuffer scratchBuffer, int maxDepth, Camera camera,
                                                Vertex[] path, bool regularize,
                                                VolumeEmissionGather emissionGather = null)
        {
            if (maxDepth == 0)
                return 0;
            SampledSpectrum beta = new SampledSpectrum(1.0f);
            // Generate first vertex on camera subpath and start random walk
            Float pdfPos, pdfDir;
            path[0] = Vertex.CreateCamera(camera, new Ray(ray), beta);
            camera.PDF_We(new Ray(ray), &pdfPos, &pdfDir);
            return RandomWalk(integrator, ref lambda, ray, sampler, camera, scratchBuffer, beta, pdfDir,
                              maxDepth - 1, TransportMode.Radiance, path, 1, regularize, emissionGather) + 1;
        }

        public static int GenerateLightSubpath(Integrator integrator, ref SampledWavelengths lambda,
                                               Sampler sampler, Camera camera, ScratchBuffer scratchBuffer,
                                               int maxDepth, Float time, LightSampler lightSampler,
                                               Vertex[] path, bool regularize, Vertex cameraVertex = null)
        {
            // Generate light subpath and initialize _path_ vertices
            if (maxDepth == 0)
                return 0;
            // Sample initial ray for light subpath
            // Sample light for BDPT light subpath
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(sampler.Get1D());
            if (!sampledLight)
                return 0;
            Light light = sampledLight.value().light;
            Float lightSamplePDF = sampledLight.value().p;

            Point2f ul0 = sampler.Get2D();
            Point2f ul1 = sampler.Get2D();
            // (not in pbrt-v4) The point of an emissive volume has a third coordinate
            VolumeEmissionLight volumeLight = light as VolumeEmissionLight;
            pstd.optional<LightLeSample> les = volumeLight != null
                ? volumeLight.SampleLeVolume(ul0, ul1, sampler.Get1D(), lambda, time)
                : light.SampleLe(ul0, ul1, ref lambda, time);
            if (volumeLight != null)
            {
                // The point of the volume is only in the medium if the surfaces around it say so
                bool valid = les && les.value().pdfPos != 0 && les.value().pdfDir != 0 && (bool)les.value().L &&
                             cameraVertex != null &&
                             MediumContainsPoint(integrator, cameraVertex, les.value().ray.o, volumeLight.Medium(), time);
                if (!valid)
                {
                    // A sample that misses the emission is a light subpath without throughput and not the absence of a
                    // light subpath: the s = 1 strategies take samples of their own, and only run if the light subpath has
                    // a vertex.
                    path[0] = Vertex.CreateLight(light, new Ray(new Point3f(0, 0, 0), new Vector3f(0, 0, 1), time),
                                                 new SampledSpectrum(0.0f), lightSamplePDF);
                    return 1;
                }
            }
            else if (!les || les.value().pdfPos == 0 || les.value().pdfDir == 0 || !les.value().L)
                return 0;
            RayDifferential ray = new RayDifferential(les.value().ray);

            // Generate first vertex of light subpath
            Float p_l = lightSamplePDF * les.value().pdfPos;
            path[0] = les.value().intr ? Vertex.CreateLight(light, les.value().intr.value(), les.value().L, p_l)
                                       : Vertex.CreateLight(light, new Ray(ray), les.value().L, p_l);
            if (volumeLight != null)
                path[0].sigmaMaj = std.max(volumeLight.Medium().MajorantAt(ray.o, lambda)[0], 1e-30f);

            // Follow light subpath random walk
            SampledSpectrum beta = les.value().L * les.value().AbsCosTheta(ray.d) / (p_l * les.value().pdfDir);
            int nVertices = RandomWalk(integrator, ref lambda, ray, sampler, camera, scratchBuffer, beta,
                                       les.value().pdfDir, maxDepth - 1, TransportMode.Importance, path, 1,
                                       regularize);
            // Correct subpath sampling densities for infinite area lights
            if (path[0].IsInfiniteLight())
            {
                // Set spatial density of _path[1]_ for infinite area light
                if (nVertices > 0)
                {
                    path[1].pdfFwd = les.value().pdfPos;
                    if (path[1].IsOnSurface())
                        path[1].pdfFwd *= AbsDot(ray.d, path[1].ng());
                }

                // Set spatial density of _path[0]_ for infinite area light
                path[0].pdfFwd = InfiniteLightDensity(integrator.infiniteLights, lightSampler, ray.d);
            }

            return nVertices + 1;
        }

        // (C++ passes "path + 1": here the array plus the offset of the first vertex to fill.
        //  path[off - 1] is the predecessor, path[off + bounces] the current vertex.)
        public static int RandomWalk(Integrator integrator, ref SampledWavelengths lambdaRef, RayDifferential ray,
                                     Sampler sampler, Camera camera, ScratchBuffer scratchBuffer,
                                     SampledSpectrum beta, Float pdf, int maxDepth, TransportMode mode,
                                     Vertex[] path, int off, bool regularize,
                                     VolumeEmissionGather emissionGather = null)
        {
            if (maxDepth == 0)
                return 0;
            // (Lambdas cannot capture ref parameters: work on a local copy and write back.)
            SampledWavelengths lambda = lambdaRef;
            try
            {
                // Follow random walk to initialize BDPT path vertices
                int bounces = 0;
                bool anyNonSpecularBounces = false;
                Float pdfFwd = pdf;
                // (not in pbrt-v4) Spectral MIS ratios of the segment that is being sampled, and of the subpath so far
                SampledSpectrum segR = new SampledSpectrum(1.0f);
                SampledSpectrum cumR = path[off - 1].rCum;
                while (true)
                {
                    // Attempt to create the next subpath vertex in _path_
                    if (!beta)
                        break;
                    bool scattered = false, terminated = false;
                    // Trace a ray and sample the medium, if any
                    int vIdx = off + bounces;
                    pstd.optional<ShapeIntersection> si = integrator.Intersect(new Ray(ray));
                    if (ray.medium != null)
                    {
                        // Sample participating medium for _RandomWalk()_ ray
                        Float tMax = si ? si.value().tHit : Infinity;
                        RNG rng = new RNG(Hash(ray.o, tMax), Hash(ray.d));
                        Float u = sampler.Get1D();
                        SampledSpectrum Tmaj = SampleT_maj(
                            new Ray(ray), tMax, u, ref rng, lambda,
                            (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                            {
                                // (not in pbrt-v4) Gather the emission of an emissive volume at the collision. The path
                                // that ends here is complete, so the vertex is kept for the s = 0 strategy.
                                if (emissionGather != null && mode == TransportMode.Radiance && (bool)mp.Le)
                                {
                                    TextureMedium emissive = ray.medium as TextureMedium;
                                    VolumeEmissionLight volumeLight = emissive != null ? emissive.EmissionLight() : null;
                                    Float pdfCollision = sigma_maj[0] * T_maj[0];
                                    if (volumeLight != null && emissionGather.lights.Contains(volumeLight) && pdfCollision > 0)
                                    {
                                        SampledSpectrum emission = emissive.EmissionOf(mp);
                                        if ((bool)emission)
                                        {
                                            Vertex emissionVertex = Vertex.CreateLight(
                                                new EndpointInteraction(volumeLight, new Interaction(p, ray.time, ray.medium)),
                                                beta * T_maj / pdfCollision, 0);
                                            emissionVertex.sigmaMaj = std.max(sigma_maj[0], 1e-30f);
                                            // The collision that finds the emission has the density T_maj sigma_maj
                                            emissionVertex.rCum = cumR * segR * T_maj * sigma_maj / (T_maj[0] * sigma_maj[0]);
                                            emissionVertex.pdfFwd = path[vIdx - 1].ConvertDensity(pdfFwd, emissionVertex);
                                            emissionGather.records.Add(new EmissionVertexRecord(emissionVertex, vIdx));
                                        }
                                    }
                                }

                                // Compute medium event probabilities for interaction
                                // (not in pbrt-v4) With a signed null coefficient (progressive tracking without clamping) the
                                // event probabilities follow the absolute values of the coefficients, as in VolPathIntegrator
                                SampledSpectrum sigma_n_signed = sigma_maj - mp.sigma_a - mp.sigma_s;
                                SampledSpectrum sigma_c = mp.signedNull ? mp.sigma_a + mp.sigma_s + Abs(sigma_n_signed) : sigma_maj;
                                Float pAbsorb = mp.sigma_a[0] / sigma_c[0];
                                Float pScatter = mp.sigma_s[0] / sigma_c[0];
                                Float pNull = mp.signedNull ? std.abs(sigma_n_signed[0]) / sigma_c[0]
                                                            : std.max<Float>(0, 1 - pAbsorb - pScatter);

                                // Randomly sample medium event for _RandomRalk()_ ray
                                Float um = sampler.Get1D();
                                int evt = SampleDiscrete(new Float[] { pAbsorb, pScatter, pNull }, um);
                                if (evt == 0)
                                {
                                    // Handle absorption for _RandomWalk()_ ray
                                    terminated = true;
                                    return false;
                                }
                                else if (evt == 1)
                                {
                                    // Handle scattering for _RandomWalk()_ ray
                                    SampledSpectrum scatterVertexRatio = mp.sigma_s / mp.sigma_s[0];
                                    if (mp.signedNull)
                                    {
                                        SampledSpectrum densityRate = T_maj * sigma_maj;
                                        Float pdfScatter = densityRate[0] * pScatter;
                                        beta *= T_maj * mp.sigma_s / pdfScatter;
                                        scatterVertexRatio = (sigma_maj / sigma_maj[0]) * SafeDiv(mp.sigma_s, sigma_c) / (pScatter);
                                    }
                                    else
                                        beta *= T_maj * mp.sigma_s / (T_maj[0] * mp.sigma_s[0]);
                                    // Record medium interaction in _path_ and compute forward density
                                    MediumInteraction intr = new MediumInteraction(p, -ray.d, ray.time, ray.medium, mp.phase);
                                    path[vIdx] = Vertex.CreateMedium(intr, beta, pdfFwd, path[vIdx - 1]);
                                    cumR = cumR * segR * (T_maj / T_maj[0]) * scatterVertexRatio;
                                    path[vIdx].rCum = cumR;
                                    segR = new SampledSpectrum(1.0f);
                                    if (++bounces >= maxDepth)
                                    {
                                        terminated = true;
                                        return false;
                                    }

                                    // Sample direction and compute reverse density at preceding vertex
                                    pstd.optional<PhaseFunctionSample> ps = intr.phase.Sample_p(-ray.d, sampler.Get2D());
                                    if (!ps || ps.value().pdf == 0)
                                    {
                                        terminated = true;
                                        return false;
                                    }
                                    // Update path state and previous path vertex after medium scattering
                                    pdfFwd = ps.value().pdf;
                                    beta *= ps.value().p / ps.value().pdf;
                                    ray = intr.SpawnRay(ps.value().wi);
                                    anyNonSpecularBounces = true;
                                    path[vIdx - 1].pdfRev = path[vIdx].ConvertDensity(ps.value().pdf, path[vIdx - 1]);

                                    scattered = true;
                                    return false;
                                }
                                else
                                {
                                    // Handle null scattering for _RandomWalk()_ ray
                                    if (mp.signedNull)
                                    {
                                        SampledSpectrum densityRate = T_maj * sigma_maj;
                                        Float pdfU = densityRate[0] * pNull;
                                        if (pdfU == 0)
                                            beta = new SampledSpectrum(0.0f);
                                        else
                                        {
                                            beta *= T_maj * sigma_n_signed / pdfU;
                                            segR = segR * densityRate * SafeDiv(Abs(sigma_n_signed), sigma_c) / pdfU;
                                        }
                                        return (bool)beta;
                                    }
                                    SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                                    Float pdfN = T_maj[0] * sigma_n[0];
                                    if (pdfN == 0)
                                        beta = new SampledSpectrum(0.0f);
                                    else
                                    {
                                        beta *= T_maj * sigma_n / pdfN;
                                        segR = segR * T_maj * sigma_n / pdfN;
                                    }
                                    return (bool)beta;
                                }
                            });
                        // Update _beta_ for medium transmittance
                        if (!scattered)
                        {
                            beta *= Tmaj / Tmaj[0];
                            segR = segR * Tmaj / Tmaj[0];
                        }
                    }

                    if (terminated)
                        return bounces;
                    if (scattered)
                        continue;
                    // Handle escaped rays after no medium scattering event
                    if (!si)
                    {
                        // Capture escaped rays when tracing from the camera
                        if (mode == TransportMode.Radiance)
                        {
                            path[vIdx] = Vertex.CreateLight(new EndpointInteraction(new Ray(ray)), beta, pdfFwd);
                            path[vIdx].rCum = cumR * segR;
                            ++bounces;
                        }

                        break;
                    }

                    // Handle surface interaction for path generation
                    SurfaceInteraction isect = si.value().intr;
                    // Get BSDF and skip over medium boundaries
                    BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                    if (!bsdf)
                    {
                        isect.SkipIntersection(ref ray, si.value().tHit);
                        continue;
                    }

                    // Possibly regularize the BSDF
                    if (regularize && anyNonSpecularBounces)
                    {
                        ++PathIntegrator.regularizedBSDFs;
                        bsdf.Regularize();
                    }

                    ++PathIntegrator.totalBSDFs;
                    // Initialize _vertex_ with surface intersection information
                    path[vIdx] = Vertex.CreateSurface(isect, bsdf, beta, pdfFwd, path[vIdx - 1]);
                    cumR = cumR * segR;
                    path[vIdx].rCum = cumR;
                    segR = new SampledSpectrum(1.0f);

                    if (++bounces >= maxDepth)
                        break;
                    // Sample BSDF at current vertex
                    Vector3f wo = isect.wo;
                    Float uc = sampler.Get1D();
                    pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, uc, sampler.Get2D(), mode, BxDFReflTransFlags.All);
                    if (!bs)
                        break;
                    pdfFwd = bs.value().pdfIsProportional ? bsdf.PDF(wo, bs.value().wi, mode, BxDFReflTransFlags.All) : bs.value().pdf;
                    anyNonSpecularBounces |= !bs.value().IsSpecular();
                    beta *= bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                    ray = isect.SpawnRay(ray, bsdf, bs.value().wi, bs.value().flags, bs.value().eta);

                    // Compute path probabilities at surface vertex
                    Float pdfRev = bsdf.PDF(bs.value().wi, wo, !mode, BxDFReflTransFlags.All);
                    if (bs.value().IsSpecular())
                    {
                        path[vIdx].delta = true;
                        pdfRev = pdfFwd = 0;
                    }
                    path[vIdx - 1].pdfRev = path[vIdx].ConvertDensity(pdfRev, path[vIdx - 1]);
                }
                return bounces;
            }
            finally
            {
                lambdaRef = lambda;
            }
        }

        // (not in pbrt-v4) Whether the medium is the one at the point p. The medium at the camera is known, and the medium
        // changes where the segment from the camera to p crosses a surface with a different medium on each side, so
        // following the segment (through all surfaces, which only matter if they change the medium) ends in the medium at p.
        public static bool MediumContainsPoint(Integrator integrator, Vertex cameraVertex, Point3f p, Medium medium,
                                               Float time)
        {
            Interaction origin = cameraVertex.GetInteraction();
            Ray ray = new Ray(origin.p(), p - origin.p(), time, origin.GetMedium(p - origin.p()));
            if (LengthSquared(ray.d) == 0)
                return ray.medium == medium;
            for (int i = 0; i < 256; ++i)
            {
                pstd.optional<ShapeIntersection> si = integrator.Intersect(ray, 1 - ShadowEpsilon);
                if (!si)
                    return ray.medium == medium;
                ray = si.value().intr.SpawnRayTo(p);
            }
            return false;
        }

        // (not in pbrt-v4) The s = 0 strategy for a point of an emissive volume that a camera subpath passed
        public static SampledSpectrum ConnectVolumeEmission(Integrator integrator, Vertex[] cameraVertices,
                                                            EmissionVertexRecord record, SampledWavelengths lambda,
                                                            LightSampler lightSampler, Camera camera)
        {
            int t = record.index + 1;
            Vertex[] path = new Vertex[t];
            for (int i = 0; i < record.index; ++i)
                path[i] = cameraVertices[i];
            path[record.index] = record.vertex;
            SampledSpectrum L = record.vertex.Le(integrator.infiniteLights, path[t - 2], lambda) * record.vertex.beta;
            if (!L)
                return new SampledSpectrum(0.0f);
            return L * MISWeight(integrator, camera, null, path, null, 0, t, lightSampler);
        }

        public static SampledSpectrum G(Integrator integrator, Sampler sampler, Vertex v0, Vertex v1,
                                        SampledWavelengths lambda)
        {
            Vector3f d = v0.p() - v1.p();
            Float g = 1 / LengthSquared(d);
            d *= std.sqrt(g);
            if (v0.IsOnSurface())
                g *= AbsDot(v0.ns(), d);
            if (v1.IsOnSurface())
                g *= AbsDot(v1.ns(), d);
            return g * integrator.Tr(v0.GetInteraction(), v1.GetInteraction(), lambda);
        }

        static Float remap0(Float f) { return f != 0 ? f : 1; }

        public static Float MISWeight(Integrator integrator, Camera camera, Vertex[] lightVertices,
                                      Vertex[] cameraVertices, Vertex sampled, int s, int t,
                                      LightSampler lightSampler)
        {
            if (s + t == 2)
                return 1;
            Float sumRi = 0;

            // Temporarily update vertex properties for current strategy
            // Look up connection vertices and their predecessors
            Vertex qs = s > 0 ? lightVertices[s - 1] : null,
                   pt = t > 0 ? cameraVertices[t - 1] : null,
                   qsMinus = s > 1 ? lightVertices[s - 2] : null,
                   ptMinus = t > 1 ? cameraVertices[t - 2] : null;

            // Update sampled vertex for $s=1$ or $t=1$ strategy
            ScopedAssignment<Vertex> a1 = default;
            // Mark connection vertices as non-degenerate
            ScopedAssignment<bool> a2 = default, a3 = default;
            // Update reverse density of vertex $\pt{}_{t-1}$
            ScopedAssignment<Float> a4 = default;
            // Update reverse density of vertex $\pt{}_{t-2}$
            ScopedAssignment<Float> a5 = default;
            // Update reverse density of vertices $\pq{}_{s-1}$ and $\pq{}_{s-2}$
            ScopedAssignment<Float> a6 = default;
            ScopedAssignment<Float> a7 = default;
            try
            {
                if (s == 1)
                    a1 = new ScopedAssignment<Vertex>(ref lightVertices[s - 1], sampled);
                else if (t == 1)
                    a1 = new ScopedAssignment<Vertex>(ref cameraVertices[t - 1], sampled);

                if (pt != null)
                    a2 = new ScopedAssignment<bool>(ref pt.delta, false);
                if (qs != null)
                    a3 = new ScopedAssignment<bool>(ref qs.delta, false);

                if (pt != null)
                    a4 = new ScopedAssignment<Float>(ref pt.pdfRev,
                        s > 0 ? qs.PDF(integrator, qsMinus, pt)
                              : pt.PDFLightOrigin(integrator.infiniteLights, ptMinus, lightSampler));

                if (ptMinus != null)
                    a5 = new ScopedAssignment<Float>(ref ptMinus.pdfRev,
                        s > 0 ? pt.PDF(integrator, qs, ptMinus) : pt.PDFLight(integrator, ptMinus));

                if (qs != null)
                    a6 = new ScopedAssignment<Float>(ref qs.pdfRev, pt.PDF(integrator, ptMinus, qs));
                if (qsMinus != null)
                    a7 = new ScopedAssignment<Float>(ref qsMinus.pdfRev, qs.PDF(integrator, pt, qsMinus));

                Film film = camera.GetFilm();
                Float splatScale = (Float)film.FullResolution().x * (Float)film.FullResolution().y /
                                   (Float)film.PixelBounds().Area();

                // Consider hypothetical connection strategies along the camera subpath
                Float ri = 1;
                for (int i = t - 1; i > 0; --i)
                {
                    ri *= remap0(cameraVertices[i].pdfRev) / remap0(cameraVertices[i].pdfFwd);
                    // See https://github.com/mmp/pbrt-v4/issues/347
                    if (i == 1) ri /= splatScale;
                    if (!cameraVertices[i].delta && !cameraVertices[i - 1].delta)
                        sumRi += ri;
                }

                // Consider hypothetical connection strategies along the light subpath
                ri = 1;
                for (int i = s - 1; i >= 0; --i)
                {
                    ri *= remap0(lightVertices[i].pdfRev) / remap0(lightVertices[i].pdfFwd);
                    bool deltaLightvertex = i > 0 ? lightVertices[i - 1].delta : lightVertices[0].IsDeltaLight();
                    if (!lightVertices[i].delta && !deltaLightvertex)
                        sumRi += ri;
                }

                // See https://github.com/mmp/pbrt-v4/issues/347
                if (t == 1) sumRi /= splatScale;
                // (not in pbrt-v4) Spectral MIS: the techniques are the strategies and the choice of the hero wavelength. The
                // chromatic part of the density of a strategy is normalized by its average over the wavelengths, which makes
                // the weights of the hero wavelengths of one strategy sum to one, whatever the other strategies do. What is left is
                // dividing by the average of the ratios of this subpath pair, as VolPathIntegrator does with r_u.
                BDPTIntegrator bdpt = integrator as BDPTIntegrator;
                if (bdpt == null || bdpt.spectralMIS)
                {
                    SampledSpectrum Q = cameraVertices[t - 1].rCum * (s > 0 ? lightVertices[s - 1].rCum : new SampledSpectrum(1.0f));
                    Float averageRatio = Q.Average();
                    return averageRatio > 0 ? 1 / ((1 + sumRi) * averageRatio) : 0;
                }
                return 1 / (1 + sumRi);

            }
            finally
            {
                // ~ScopedAssignment() in reverse order of construction
                a7.Dispose();
                a6.Dispose();
                a5.Dispose();
                a4.Dispose();
                a3.Dispose();
                a2.Dispose();
                a1.Dispose();
            }
        }

        public static Float InfiniteLightDensity(List<Light> infiniteLights, LightSampler lightSampler, Vector3f w)
        {
            Float pdf = 0;
            foreach (Light light in infiniteLights)
                pdf += light.PDF_Li(new LightSampleContext(new Interaction()), -w) * lightSampler.PMF(light);
            return pdf;
        }

        public static SampledSpectrum ConnectBDPT(Integrator integrator, ref SampledWavelengths lambdaRef,
                                                  Vertex[] lightVertices, Vertex[] cameraVertices, int s, int t,
                                                  LightSampler lightSampler, Camera camera, Sampler sampler,
                                                  ref pstd.optional<Point2f> pRaster, ref Float misWeightOut)
        {
            SampledWavelengths lambda = lambdaRef;
            SampledSpectrum L = new SampledSpectrum(0.0f);
            // Ignore invalid connections related to infinite area lights
            if (t > 1 && s != 0 && cameraVertices[t - 1].type == VertexType.Light)
                return new SampledSpectrum(0.0f);

            // Perform connection and write contribution to _L_
            Vertex sampled = new Vertex();
            if (s == 0)
            {
                // Interpret the camera subpath as a complete path
                Vertex pt = cameraVertices[t - 1];
                if (pt.IsLight())
                    L = pt.Le(integrator.infiniteLights, cameraVertices[t - 2], lambda) * pt.beta;
                DCHECK(!L.HasNaNs());
            }
            else if (t == 1)
            {
                // Sample a point on the camera and connect it to the light subpath
                Vertex qs = lightVertices[s - 1];
                if (qs.IsConnectible())
                {
                    pstd.optional<CameraWiSample> cs = camera.SampleWi(qs.GetInteraction(), sampler.Get2D(), ref lambda);
                    if (cs)
                    {
                        pRaster = new pstd.optional<Point2f>(cs.value().pRaster);
                        // Initialize dynamically sampled vertex and _L_ for $t=1$ case
                        sampled = Vertex.CreateCamera(camera, cs.value().pLens, cs.value().Wi / cs.value().pdf);
                        L = qs.beta * qs.f(sampled, TransportMode.Importance) * sampled.beta;
                        if (qs.IsOnSurface())
                            L *= AbsDot(cs.value().wi, qs.ns());
                        DCHECK(!L.HasNaNs());
                        if ((bool)L)
                        {
                            L *= integrator.Tr(cs.value().pRef, cs.value().pLens, lambda);

                            // See https://github.com/mmp/pbrt-v4/issues/347
                            Film film = camera.GetFilm();
                            L *= (Float)film.FullResolution().x * (Float)film.FullResolution().y /
                                 (Float)film.PixelBounds().Area();
                        }
                    }
                }
            }
            else if (s == 1)
            {
                // Sample a point on a light and connect it to the camera subpath
                Vertex pt = cameraVertices[t - 1];
                if (pt.IsConnectible())
                {
                    pstd.optional<SampledLight> sampledLight = lightSampler.Sample(sampler.Get1D());

                    if (sampledLight)
                    {
                        Light light = sampledLight.value().light;
                        Float p_l = sampledLight.value().p;

                        LightSampleContext ctx;
                        if (pt.IsOnSurface())
                        {
                            SurfaceInteraction si = pt.GetInteraction().AsSurface();
                            ctx = new LightSampleContext(si);
                            // Try to nudge the light sampling position to correct side of the surface
                            BxDFFlags flags = pt.bsdf.Flags();
                            if (IsReflective(flags) && !IsTransmissive(flags))
                                ctx.pi = si.OffsetRayOrigin(si.wo);
                            else if (IsTransmissive(flags) && !IsReflective(flags))
                                ctx.pi = si.OffsetRayOrigin(-si.wo);
                        }
                        else
                            ctx = new LightSampleContext(pt.GetInteraction());
                        // (not in pbrt-v4) The point of an emissive volume has a third coordinate
                        VolumeEmissionLight volumeLight = light as VolumeEmissionLight;
                        pstd.optional<LightLiSample> lightWeight = volumeLight != null
                            ? volumeLight.SampleLiVolume(ctx, sampler.Get2D(), sampler.Get1D(), lambda)
                            : light.SampleLi(ctx, sampler.Get2D(), lambda);
                        if (lightWeight && (bool)lightWeight.value().L && lightWeight.value().pdf > 0 &&
                            (volumeLight == null || MediumContainsPoint(integrator, cameraVertices[0],
                                                                        lightWeight.value().pLight.p(), volumeLight.Medium(),
                                                                        pt.time())))
                        {
                            EndpointInteraction ei = new EndpointInteraction(light, lightWeight.value().pLight);
                            sampled = Vertex.CreateLight(ei, lightWeight.value().L / (lightWeight.value().pdf * p_l), 0);
                            if (volumeLight != null)
                                sampled.sigmaMaj = std.max(volumeLight.Medium().MajorantAt(ei.p(), lambda)[0], 1e-30f);
                            sampled.pdfFwd = sampled.PDFLightOrigin(integrator.infiniteLights, pt, lightSampler);
                            L = pt.beta * pt.f(sampled, TransportMode.Radiance) * sampled.beta;
                            if (pt.IsOnSurface())
                                L *= AbsDot(lightWeight.value().wi, pt.ns());
                            // Only check visibility if the path would carry radiance.
                            if ((bool)L)
                                L *= integrator.Tr(pt.GetInteraction(), lightWeight.value().pLight, lambda);
                        }
                    }
                }
            }
            else
            {
                // Handle all other bidirectional connection cases
                Vertex qs = lightVertices[s - 1], pt = cameraVertices[t - 1];
                if (qs.IsConnectible() && pt.IsConnectible())
                {
                    L = qs.beta * qs.f(pt, TransportMode.Importance) * pt.f(qs, TransportMode.Radiance) * pt.beta;
                    if ((bool)L)
                        L *= G(integrator, sampler, qs, pt, lambda);
                }
            }

            ++PathIntegrator.totalPaths;
            if (!L)
                ++PathIntegrator.zeroRadiancePaths;
            if (PathIntegrator.pathLength == null)
                PathIntegrator.pathLength = new StatIntDistribution();
            PathIntegrator.pathLength.Add(s + t - 2);
            // Compute MIS weight for connection strategy
            Float misWeight = (bool)L ? MISWeight(integrator, camera, lightVertices, cameraVertices, sampled, s, t, lightSampler)
                                : 0.0f;
            DCHECK(!IsNaN(misWeight));
            L *= misWeight;
            misWeightOut = misWeight;

            lambdaRef = lambda;
            return L;
        }
    }

    // BDPTIntegrator Definition
    public sealed unsafe class BDPTIntegrator : RayIntegrator
    {
        // BDPTIntegrator Public Methods
        public BDPTIntegrator(Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights,
                              int maxDepth, bool visualizeStrategies, bool visualizeWeights,
                              bool regularize = false, bool spectralMIS = true)
            : base(camera, sampler, aggregate, TextureMedium.AddEmissionLights(lights))
        {
            this.spectralMIS = spectralMIS;
            this.maxDepth = maxDepth;
            this.regularize = regularize;
            // (not in pbrt-v4) Media that ask for their emission to be sampled are lights too
            List<Light> allLights = TextureMedium.AddEmissionLights(lights);
            this.lightSampler = new PowerLightSampler(allLights.ToArray(), new Allocator());
            HashSet<Light> volumeLights = new HashSet<Light>();
            foreach (Light light in allLights)
            {
                if (light is VolumeEmissionLight)
                    volumeLights.Add(light);
            }
            this.emissionGather = volumeLights.Count > 0 ? volumeLights : null;
            this.visualizeStrategies = visualizeStrategies;
            this.visualizeWeights = visualizeWeights;
        }

        // BDPT Method Definitions
        public override void Render()
        {
            // Allocate buffers for debug visualization
            if (visualizeStrategies || visualizeWeights)
            {
                int bufferCount = (1 + maxDepth) * (6 + maxDepth) / 2;
                weightFilms = new Film[bufferCount];
                for (int depth = 0; depth <= maxDepth; ++depth)
                {
                    for (int s = 0; s <= depth + 2; ++s)
                    {
                        int t = depth + 2 - s;
                        if (t == 0 || (s == 1 && t == 1))
                            continue;

                        string filename = StringPrintf("bdpt_d%02d_s%02d_t%02d.exr", depth, s, t);

                        FilmBaseParameters p = new FilmBaseParameters(
                            camera.GetFilm().FullResolution(),
                            new Bounds2i(new Point2i(0, 0), camera.GetFilm().FullResolution()),
                            new BoxFilter(),
                            camera.GetFilm().Diagonal() * 1000, PixelSensor.CreateDefault(), filename);
                        weightFilms[BDPT.BufferIndex(s, t)] = new RGBFilm(p, RGBColorSpace.sRGB);
                    }
                }
            }

            base.Render();

            // Write buffers for debug visualization
            if (visualizeStrategies || visualizeWeights)
            {
                Float invSampleCount = 1.0f / samplerPrototype.SamplesPerPixel();
                for (int i = 0; i < weightFilms.Length; ++i)
                {
                    ImageMetadata metadata = new ImageMetadata();
                    if (weightFilms[i] != null)
                        weightFilms[i].WriteImage(metadata, invSampleCount);
                }
                weightFilms = null;
            }
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                           ScratchBuffer scratchBuffer, VisibleSurface visibleSurface)
        {
            // Trace the camera and light subpaths
            // (index 0 of the arrays is the first vertex; RandomWalk is given offset 1)
            Vertex[] cameraVertices = new Vertex[maxDepth + 2];
            for (int i = 0; i < cameraVertices.Length; ++i) cameraVertices[i] = new Vertex();
            VolumeEmissionGather gather = emissionGather != null ? new VolumeEmissionGather(emissionGather) : null;
            int nCamera = BDPT.GenerateCameraSubpath(this, ray, ref lambda, sampler, scratchBuffer, maxDepth + 2,
                                                     camera, cameraVertices, regularize, gather);
            Vertex[] lightVertices = new Vertex[maxDepth + 1];
            for (int i = 0; i < lightVertices.Length; ++i) lightVertices[i] = new Vertex();
            int nLight = BDPT.GenerateLightSubpath(this, ref lambda, sampler, camera, scratchBuffer, maxDepth + 1,
                                                   cameraVertices[0].time(), lightSampler, lightVertices,
                                                   regularize, cameraVertices[0]);

            SampledSpectrum L = new SampledSpectrum(0.0f);
            // (not in pbrt-v4) The s = 0 strategies for the points of emissive volumes that the camera subpath passed
            if (gather != null)
            {
                foreach (EmissionVertexRecord record in gather.records)
                {
                    if (record.index - 1 <= maxDepth)
                        L += BDPT.ConnectVolumeEmission(this, cameraVertices, record, lambda, lightSampler, camera);
                }
            }
            // Execute all BDPT connection strategies
            for (int t = 1; t <= nCamera; ++t)
            {
                for (int s = 0; s <= nLight; ++s)
                {
                    int depth = t + s - 2;
                    if ((s == 1 && t == 1) || depth < 0 || depth > maxDepth)
                        continue;
                    // Execute the $(s, t)$ connection strategy and update _L_
                    pstd.optional<Point2f> pFilmNew = default;
                    Float misWeight = 0.0f;
                    SampledSpectrum Lpath = BDPT.ConnectBDPT(this, ref lambda, lightVertices, cameraVertices, s, t,
                                                            lightSampler, camera, sampler, ref pFilmNew,
                                                            ref misWeight);
                    if ((bool)Lpath && (visualizeStrategies || visualizeWeights))
                    {
                        SampledSpectrum value = default;
                        if (visualizeStrategies)
                            value = misWeight == 0 ? new SampledSpectrum(0.0f) : Lpath / misWeight;
                        if (visualizeWeights)
                            value = Lpath;
                        if (pFilmNew)
                            weightFilms[BDPT.BufferIndex(s, t)].AddSplat(pFilmNew.value(), value, lambda);
                        else
                        {
                            // Unfortunately we no longer have the pixel coordinates of the sample easily
                            // available, so we need to go back to the camera and ask for them.
                            pstd.optional<CameraWiSample> cs = camera.SampleWi(
                                new Interaction(ray.o + ray.d * 100.0f, (Float)0, (Medium)null), new Point2f(0.5f, 0.5f),
                                ref lambda);
                            if (cs)
                                weightFilms[BDPT.BufferIndex(s, t)].AddSplat(cs.value().pRaster, value, lambda);
                        }
                    }
                    if (t != 1)
                        L += Lpath;
                    else if ((bool)Lpath)
                    {
                        CHECK(pFilmNew.has_value());
                        camera.GetFilm().AddSplat(pFilmNew.value(), Lpath, lambda);
                    }
                }
            }

            return L;
        }

        public override string ToString()
        {
            return StringPrintf("[ BDPTIntegrator maxDepth: %d visualizeStrategies: %s " +
                                "visualizeWeights: %s regularize: %s lightSampler: %s ]",
                                maxDepth, visualizeStrategies, visualizeWeights, regularize, lightSampler);
        }

        public static BDPTIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                            Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            if (!(camera is PerspectiveCamera))
                ErrorExit("Only the \"perspective\" camera is currently supported with the " +
                          "\"bdpt\" integrator.");
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            bool visualizeStrategies = parameters.GetOneBool("visualizestrategies", false);
            bool visualizeWeights = parameters.GetOneBool("visualizeweights", false);

            if ((visualizeStrategies || visualizeWeights) && maxDepth > 5)
            {
                Warning(loc, "visualizestrategies/visualizeweights was enabled, limiting maxdepth to 5");
                maxDepth = 5;
            }

            bool regularize = parameters.GetOneBool("regularize", false);
            // (not in pbrt-v4) Whether the weights also compare the wavelengths, which matters for media with a chromatic extinction
            bool spectralMIS = parameters.GetOneBool("spectralmis", true);
            return new BDPTIntegrator(camera, sampler, aggregate, lights, maxDepth, visualizeStrategies,
                                      visualizeWeights, regularize, spectralMIS);
        }

        // BDPTIntegrator Private Members
        public bool spectralMIS = true;
        int maxDepth;
        bool regularize;
        LightSampler lightSampler;
        HashSet<Light> emissionGather;
        bool visualizeStrategies, visualizeWeights;
        Film[] weightFilms;
    }

    // MLTIntegrator Definition
    public sealed unsafe class MLTIntegrator : Integrator
    {
        // STAT_PERCENT("Integrator/Acceptance rate", acceptedMutations, totalMutations);
        [ThreadStatic] static long acceptedMutations, totalMutations;
        static StatRegisterer STATS_REG = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportPercentage("Integrator/Acceptance rate", acceptedMutations, totalMutations);
            acceptedMutations = totalMutations = 0;
        });

        // MLTIntegrator Public Methods
        public MLTIntegrator(Camera camera, Primitive aggregate, List<Light> lights, int maxDepth,
                             int nBootstrap, int nChains, int mutationsPerPixel, Float sigma,
                             Float largeStepProbability, bool regularize)
            : base(aggregate, lights)
        {
            this.lightSampler = new PowerLightSampler(lights.ToArray(), new Allocator());
            this.camera = camera;
            this.maxDepth = maxDepth;
            this.nBootstrap = nBootstrap;
            this.nChains = nChains;
            this.mutationsPerPixel = mutationsPerPixel;
            this.sigma = sigma;
            this.largeStepProbability = largeStepProbability;
            this.regularize = regularize;
        }

        // MLTIntegrator Constants
        const int cameraStreamIndex = 0;
        const int lightStreamIndex = 1;
        const int connectionStreamIndex = 2;
        const int nSampleStreams = 3;

        // MLTIntegrator Method Definitions
        SampledSpectrum L(ScratchBuffer scratchBuffer, MLTSampler sampler, int depth, out Point2f pRaster,
                          out SampledWavelengths lambda)
        {
            pRaster = default;
            lambda = default;
            if (lights.Count == 0)
                return new SampledSpectrum(0.0f);
            sampler.StartStream(cameraStreamIndex);
            // Determine the number of available strategies and pick a specific one
            int s, t, nStrategies;
            if (depth == 0)
            {
                nStrategies = 1;
                s = 0;
                t = 2;
            }
            else
            {
                nStrategies = depth + 2;
                s = std.min<int>((int)(sampler.Get1D() * nStrategies), nStrategies - 1);
                t = nStrategies - s;
            }

            // Sample wavelengths for MLT path
            if (Options.disableWavelengthJitter)
                lambda = camera.GetFilm().SampleWavelengths(0.5f);
            else
                lambda = camera.GetFilm().SampleWavelengths(sampler.Get1D());

            // Generate a camera subpath with exactly _t_ vertices
            Vertex[] cameraVertices = new Vertex[t];
            for (int i = 0; i < t; ++i) cameraVertices[i] = new Vertex();
            // Compute camera sample for MLT camera path
            Bounds2f sampleBounds = camera.GetFilm().SampleBounds();
            pRaster = sampleBounds.Lerp(sampler.GetPixel2D());
            CameraSample cameraSample = new CameraSample();
            cameraSample.pFilm = pRaster;
            cameraSample.time = sampler.Get1D();
            cameraSample.pLens = sampler.Get2D();

            // Generate camera ray for MLT camera path
            pstd.optional<CameraRayDifferential> crd = camera.GenerateRayDifferential(cameraSample, ref lambda);
            if (!crd || !crd.value().weight)
                return new SampledSpectrum(0.0f);
            Float rayDiffScale = std.max<Float>(.125f, 1 / std.sqrt((Float)sampler.SamplesPerPixel()));
            RayDifferential crdRay = crd.value().ray;
            crdRay.ScaleDifferentials(rayDiffScale);

            if (BDPT.GenerateCameraSubpath(this, crdRay, ref lambda, sampler, scratchBuffer, t, camera,
                                           cameraVertices, regularize) != t)
                return new SampledSpectrum(0.0f);

            // Generate a light subpath with exactly _s_ vertices
            sampler.StartStream(lightStreamIndex);
            Vertex[] lightVertices = new Vertex[s];
            for (int i = 0; i < s; ++i) lightVertices[i] = new Vertex();
            if (BDPT.GenerateLightSubpath(this, ref lambda, sampler, camera, scratchBuffer, s,
                                          cameraVertices[0].time(), lightSampler, lightVertices,
                                          regularize) != s)
                return new SampledSpectrum(0.0f);

            // Execute connection strategy and return the radiance estimate
            sampler.StartStream(connectionStreamIndex);
            pstd.optional<Point2f> pRasterNew = default;
            Float misWeight = 0;
            SampledSpectrum Lval = BDPT.ConnectBDPT(this, ref lambda, lightVertices, cameraVertices, s, t,
                                                   lightSampler, camera, sampler, ref pRasterNew,
                                                   ref misWeight) * (Float)nStrategies;
            if (pRasterNew)
                pRaster = pRasterNew.value();
            return Lval;
        }

        static Float c(SampledSpectrum L, SampledWavelengths lambda) { return L.y(lambda); }
        static Point2i PI2(Point2f p) { return new Point2i((int)p.x, (int)p.y); }

        public override void Render()
        {
            // Handle statistics and debugstart for MLTIntegrator
            if (Options.recordPixelStatistics)
                StatsEnablePixelStats(camera.GetFilm().PixelBounds(),
                                      RemoveExtension(camera.GetFilm().GetFilename()));

            if (Options.debugStart.Length != 0)
            {
                string[] cc = Options.debugStart.Split(',');
                if (cc.Length == 0)
                    ErrorExit(StringPrintf("Didn't find comma-separated values after --debugstart: %s",
                                           Options.debugStart));

                int depthDbg;
                if (!int.TryParse(cc[0], out depthDbg))
                    ErrorExit(StringPrintf("Unable to decode first --debugstart value: %s", cc[0]));

                string[] rest = new string[cc.Length - 1];
                Array.Copy(cc, 1, rest, 0, rest.Length);
                DebugMLTSampler dsampler = DebugMLTSampler.Create(new pstd.span<string>(rest), nSampleStreams);

                Point2f pRasterDbg;
                SampledWavelengths lambdaDbg;
                ScratchBuffer sb = new ScratchBuffer(65536);
                L(sb, dsampler, depthDbg, out pRasterDbg, out lambdaDbg);
                return;
            }

            // Generate bootstrap samples and compute normalization constant $b$
            Timer timer = new Timer();
            int nBootstrapSamples = nBootstrap * (maxDepth + 1);
            Float[] bootstrapWeights = new Float[nBootstrapSamples];
            // Allocate scratch buffers for MLT samples
            ThreadLocal<ScratchBuffer> threadScratchBuffers = new ThreadLocal<ScratchBuffer>(() => new ScratchBuffer());

            // Generate bootstrap samples in parallel
            ProgressReporter progress = new ProgressReporter(nBootstrap, "Generating bootstrap paths", Options.quiet);
            ParallelFor(0, nBootstrap, (long start, long end) =>
            {
                ScratchBuffer buf = threadScratchBuffers.Get();
                for (long i = start; i < end; ++i)
                {
                    // Generate _i_th bootstrap sample
                    for (int depth = 0; depth <= maxDepth; ++depth)
                    {
                        int rngIndex = (int)(i * (maxDepth + 1) + depth);
                        MLTSampler sampler = new MLTSampler(mutationsPerPixel, rngIndex, sigma,
                                                            largeStepProbability, nSampleStreams);
                        // Evaluate path radiance using _sampler_ and update _bootstrapWeights_
                        Point2f pRaster;
                        SampledWavelengths lambda;
                        SampledSpectrum L_i = L(buf, sampler, depth, out pRaster, out lambda);
                        bootstrapWeights[rngIndex] = c(L_i, lambda);

                        buf.Reset();
                    }
                }
                progress.Update(end - start);
            });
            progress.Done();

            double bootstrapSum = 0;
            for (int i = 0; i < bootstrapWeights.Length; ++i)
                bootstrapSum += bootstrapWeights[i];
            if (bootstrapSum == 0.0)
                ErrorExit("No light carrying paths found during bootstrap sampling! " +
                          "Are you trying to render a black image?");
            AliasTable bootstrapTable = new AliasTable(new pstd.span<Float>(bootstrapWeights));
            Float b = (Float)(maxDepth + 1) / bootstrapWeights.Length * (Float)bootstrapSum;

            // Set up connection to display server, if enabled
            if (Options.displayServer.Length != 0)
                ErrorExit("--display-server is not supported by Magi.");

            // Follow _nChains_ Markov chains to render image
            Film film = camera.GetFilm();
            int64_t nTotalMutations = (int64_t)film.SampleBounds().Area() * (int64_t)mutationsPerPixel;
            ProgressReporter progressRender = new ProgressReporter(nChains, "Rendering", Options.quiet);
            // Run _nChains_ Markov chains in parallel
            ParallelFor(0, nChains, (int i) =>
            {
                ScratchBuffer scratchBuffer = threadScratchBuffers.Get();
                // Compute number of mutations to apply in current Markov chain
                int64_t nChainMutations = std.min((i + 1) * nTotalMutations / nChains, nTotalMutations) -
                                          i * nTotalMutations / nChains;

                // Select initial state from the set of bootstrap samples
                RNG rng = new RNG((uint64_t)i);
                int bootstrapIndex = bootstrapTable.Sample(rng.Uniform<Float>());
                int depth = bootstrapIndex % (maxDepth + 1);

                // Initialize local variables for selected state
                MLTSampler sampler = new MLTSampler(mutationsPerPixel, bootstrapIndex, sigma, largeStepProbability,
                                                    nSampleStreams);
                Point2f pCurrent;
                SampledWavelengths lambdaCurrent;
                SampledSpectrum LCurrent = L(scratchBuffer, sampler, depth, out pCurrent, out lambdaCurrent);

                // Run the Markov chain for _nChainMutations_ steps
                for (int64_t j = 0; j < nChainMutations; ++j)
                {
                    StatsReportPixelStart(PI2(pCurrent));
                    sampler.StartIteration();
                    // Generate proposed sample and compute its radiance
                    Point2f pProposed;
                    SampledWavelengths lambdaProposed;
                    SampledSpectrum LProposed = L(scratchBuffer, sampler, depth, out pProposed, out lambdaProposed);

                    // Compute acceptance probability for proposed sample
                    Float cProposed = c(LProposed, lambdaProposed);
                    Float cCurrent = c(LCurrent, lambdaCurrent);
                    Float accept = std.min<Float>(1, cProposed / cCurrent);

                    // Splat both current and proposed samples to _film_
                    if (accept > 0)
                        film.AddSplat(pProposed, LProposed * accept / cProposed, lambdaProposed);
                    film.AddSplat(pCurrent, LCurrent * (1 - accept) / cCurrent, lambdaCurrent);

                    // Accept or reject the proposal
                    if (rng.Uniform<Float>() < accept)
                    {
                        StatsReportPixelEnd(PI2(pCurrent));
                        StatsReportPixelStart(PI2(pProposed));
                        pCurrent = pProposed;
                        LCurrent = LProposed;
                        lambdaCurrent = lambdaProposed;
                        sampler.Accept();
                        ++acceptedMutations;
                    }
                    else
                        sampler.Reject();

                    ++totalMutations;
                    scratchBuffer.Reset();
                    StatsReportPixelEnd(PI2(pCurrent));
                }

                progressRender.Update(1);
            });

            progressRender.Done();

            // Store final image computed with MLT
            ImageMetadata metadata = new ImageMetadata();
            metadata.renderTimeSeconds = (float)timer.ElapsedSeconds();
            camera.InitMetadata(metadata);
            camera.GetFilm().WriteImage(metadata, b / mutationsPerPixel);
        }

        public override string ToString()
        {
            return StringPrintf("[ MLTIntegrator camera: %s maxDepth: %d nBootstrap: %d " +
                                "nChains: %d mutationsPerPixel: %d sigma: %f " +
                                "largeStepProbability: %f lightSampler: %s regularize: %s ]",
                                camera, maxDepth, nBootstrap, nChains, mutationsPerPixel, sigma,
                                largeStepProbability, lightSampler, regularize);
        }

        public static MLTIntegrator Create(ParameterDictionary parameters, Camera camera, Primitive aggregate,
                                           List<Light> lights, FileLoc loc)
        {
            if (!(camera is PerspectiveCamera))
                ErrorExit("Only the \"perspective\" camera is currently supported with the " +
                          "\"mlt\" integrator.");
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            int nBootstrap = parameters.GetOneInt("bootstrapsamples", 100000);
            int64_t nChains = parameters.GetOneInt("chains", 1000);
            int mutationsPerPixel = parameters.GetOneInt("mutationsperpixel", 100);
            if (Options.pixelSamples)
                mutationsPerPixel = Options.pixelSamples.value();
            Float largeStepProbability = parameters.GetOneFloat("largestepprobability", 0.3f);
            Float sigma = parameters.GetOneFloat("sigma", .01f);
            if (Options.quickRender)
            {
                mutationsPerPixel = std.max(1, mutationsPerPixel / 16);
                nBootstrap = std.max(1, nBootstrap / 16);
            }
            bool regularize = parameters.GetOneBool("regularize", false);
            return new MLTIntegrator(camera, aggregate, lights, maxDepth, nBootstrap, (int)nChains,
                                     mutationsPerPixel, sigma, largeStepProbability, regularize);
        }

        // MLTIntegrator Private Members
        Camera camera;
        bool regularize;
        LightSampler lightSampler;
        int maxDepth, nBootstrap;
        int mutationsPerPixel;
        Float sigma, largeStepProbability;
        int nChains;
    }

    // SPPMPixel Definition
    public sealed unsafe class SPPMPixel
    {
        // SPPMPixel Public Members
        public Float radius = 0;
        public RGB Ld;
        public sealed class VisiblePoint
        {
            // VisiblePoint Public Methods
            public VisiblePoint() { beta = new SampledSpectrum(0.0f); }
            public VisiblePoint(Point3f p, Vector3f wo, BSDF bsdf, SampledSpectrum beta,
                                bool secondaryLambdaTerminated)
            {
                this.p = p;
                this.wo = wo;
                this.bsdf = bsdf;
                this.beta = beta;
                this.secondaryLambdaTerminated = secondaryLambdaTerminated;
            }

            // VisiblePoint Public Members
            public Point3f p;
            public Vector3f wo;
            public BSDF bsdf;
            public SampledSpectrum beta;
            public bool secondaryLambdaTerminated;
        }
        public VisiblePoint vp = new VisiblePoint();
        public AtomicFloat[] Phi_i = new AtomicFloat[3];
        public int m = 0;  // (std::atomic<int>: use Interlocked)
        public RGB tau;
        public Float n = 0;
    }

    // SPPMPixelListNode Definition
    public sealed class SPPMPixelListNode
    {
        public SPPMPixel pixel;
        public SPPMPixelListNode next;
    }

    // SPPMIntegrator Definition
    public sealed unsafe class SPPMIntegrator : Integrator
    {
        // STAT_RATIO("Stochastic Progressive Photon Mapping/Visible points checked per photon intersection", ...)
        [ThreadStatic] static long visiblePointsChecked, totalPhotonSurfaceInteractions, photonPaths;
        [ThreadStatic] static StatIntDistribution gridCellsPerVisiblePoint;
        static StatRegisterer STATS_REG = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportRatio("Stochastic Progressive Photon Mapping/Visible points checked per photon intersection",
                              visiblePointsChecked, totalPhotonSurfaceInteractions);
            visiblePointsChecked = totalPhotonSurfaceInteractions = 0;
            accum.ReportCounter("Stochastic Progressive Photon Mapping/Photon paths followed", photonPaths);
            photonPaths = 0;
            if (gridCellsPerVisiblePoint != null)
            {
                accum.ReportIntDistribution("Stochastic Progressive Photon Mapping/Grid cells per visible point",
                                            gridCellsPerVisiblePoint.sum, gridCellsPerVisiblePoint.count,
                                            gridCellsPerVisiblePoint.min, gridCellsPerVisiblePoint.max);
                gridCellsPerVisiblePoint = null;
            }
        });

        // SPPMIntegrator Public Methods
        public SPPMIntegrator(Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights,
                              int photonsPerIteration, int maxDepth, Float initialSearchRadius, int seed,
                              RGBColorSpace colorSpace)
            : base(aggregate, lights)
        {
            this.camera = camera;
            this.samplerPrototype = sampler;
            this.initialSearchRadius = initialSearchRadius;
            this.maxDepth = maxDepth;
            this.photonsPerIteration = photonsPerIteration > 0 ? photonsPerIteration
                                                               : camera.GetFilm().PixelBounds().Area();
            this.colorSpace = colorSpace;
            this.digitPermutationsSeed = seed;
        }

        // SPPM Utility Functions
        static bool ToGrid(Point3f p, Bounds3f bounds, int[] gridRes, out Point3i pi)
        {
            bool inBounds = true;
            Vector3f pg = bounds.Offset(p);
            int[] v = new int[3];
            for (int i = 0; i < 3; ++i)
            {
                v[i] = (int)(gridRes[i] * pg[i]);
                inBounds &= v[i] >= 0 && v[i] < gridRes[i];
                v[i] = Clamp(v[i], 0, gridRes[i] - 1);
            }
            pi = new Point3i(v[0], v[1], v[2]);
            return inBounds;
        }

        // SPPM Method Definitions
        public override void Render()
        {
            // Initialize local variables for _SPPMIntegrator::Render()_
            if (Options.recordPixelStatistics)
                StatsEnablePixelStats(camera.GetFilm().PixelBounds(),
                                      RemoveExtension(camera.GetFilm().GetFilename()));
            // Define variables for commonly used values in SPPM rendering
            int nIterations = samplerPrototype.SamplesPerPixel();
            ProgressReporter progress = new ProgressReporter(2 * nIterations, "Rendering", Options.quiet);
            Float invSqrtSPP = 1.0f / std.sqrt((Float)nIterations);
            Film film = camera.GetFilm();
            Bounds2i pixelBounds = film.PixelBounds();
            int nPixels = pixelBounds.Area();

            // Initialize _pixels_ array for SPPM
            CHECK(!pixelBounds.IsEmpty());
            Array2D<SPPMPixel> pixels = new Array2D<SPPMPixel>(pixelBounds);
            foreach (Point2i pp in pixelBounds)
            {
                SPPMPixel sp = new SPPMPixel();
                sp.radius = initialSearchRadius;
                pixels[pp] = sp;
            }

            // Create light samplers for SPPM rendering
            BVHLightSampler lightSampler = new BVHLightSampler(lights.ToArray(), new Allocator());
            PowerLightSampler shootLightSampler = new PowerLightSampler(lights.ToArray(), new Allocator());

            // Allocate per-thread _ScratchBuffer_s for SPPM rendering
            ThreadLocal<ScratchBuffer> threadScratchBuffers =
                new ThreadLocal<ScratchBuffer>(() => new ScratchBuffer(1024 * 1024));

            // Allocate samplers for SPPM rendering
            ThreadLocal<Sampler> threadSamplers = new ThreadLocal<Sampler>(() => samplerPrototype.Clone());
            pstd.vector<DigitPermutation> digitPermutations =
                ComputeRadicalInversePermutations((uint32_t)digitPermutationsSeed);

            for (int iter = 0; iter < nIterations; ++iter)
            {
                // Connect to display server for SPPM if requested
                if (iter == 0 && Options.displayServer.Length != 0)
                    ErrorExit("--display-server is not supported by Magi.");

                int iterCopy = iter;
                // Generate SPPM visible points
                // Sample wavelengths for SPPM pass
                Float uLambda = Options.disableWavelengthJitter ? (Float)0.5 : RadicalInverse(1, (uint64_t)iter);
                SampledWavelengths passLambda = film.SampleWavelengths(uLambda);

                Float timeSample = RadicalInverse(2, (uint64_t)iter);

                ParallelFor2D(pixelBounds, (Bounds2i tileBounds) =>
                {
                    // Follow camera paths for _tileBounds_ in image for SPPM
                    ScratchBuffer scratchBuffer = threadScratchBuffers.Get();
                    Sampler sampler = threadSamplers.Get();
                    foreach (Point2i pPixel in tileBounds)
                    {
                        sampler.StartPixelSample(pPixel, iterCopy);
                        // Generate camera ray for pixel for SPPM
                        SampledWavelengths lambda = passLambda;
                        CameraSample cs = GetCameraSample(sampler, pPixel, film.GetFilter());
                        cs.time = timeSample;
                        pstd.optional<CameraRayDifferential> crd = camera.GenerateRayDifferential(cs, ref lambda);
                        if (!crd || !crd.value().weight)
                            continue;
                        SampledSpectrum beta = crd.value().weight;
                        RayDifferential ray = crd.value().ray;
                        if (!Options.disablePixelJitter)
                            ray.ScaleDifferentials(invSqrtSPP);

                        // Follow camera ray path until a visible point is created
                        SPPMPixel pixel = pixels[pPixel];
                        Float etaScale = 1, p_b = 0;
                        bool specularBounce = true, haveSetVisiblePoint = false;
                        LightSampleContext prevIntrCtx = default;
                        int depth = 0;
                        while (true)
                        {
                            ++totalPhotonSurfaceInteractions;
                            pstd.optional<ShapeIntersection> si = Intersect(new Ray(ray));
                            // Accumulate light contributions for ray with no intersection
                            if (!si)
                            {
                                SampledSpectrum L0 = new SampledSpectrum(0.0f);
                                // Incorporate emission from infinite lights for escaped ray
                                foreach (Light light in infiniteLights)
                                {
                                    SampledSpectrum Le0 = light.Le(new Ray(ray), lambda);
                                    if (depth == 0 || specularBounce)
                                        L0 += beta * Le0;
                                    else
                                    {
                                        // Compute MIS weight for infinite light
                                        Float p_l = lightSampler.PMF(prevIntrCtx, light) *
                                                    light.PDF_Li(prevIntrCtx, ray.d, true);
                                        Float w_b = PowerHeuristic(1, p_b, 1, p_l);

                                        L0 += beta * w_b * Le0;
                                    }
                                }

                                pixel.Ld += film.ToOutputRGB(L0, lambda);
                                break;
                            }

                            // Process SPPM camera ray intersection
                            // Compute BSDF at SPPM camera ray intersection
                            SurfaceInteraction isect = si.value().intr;
                            BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                            if (!bsdf)
                            {
                                isect.SkipIntersection(ref ray, si.value().tHit);
                                continue;
                            }

                            ++PathIntegrator.totalBSDFs;
                            // Add emission from directly visible emissive surfaces to _pixel.Ld_
                            Vector3f wo = -ray.d;
                            SampledSpectrum L = new SampledSpectrum(0.0f);
                            // Incorporate emission from surface hit by ray
                            SampledSpectrum Le = si.value().intr.Le(-ray.d, lambda);
                            if ((bool)Le)
                            {
                                if (depth == 0 || specularBounce)
                                    L += beta * Le;
                                else
                                {
                                    // Compute MIS weight for area light
                                    Light areaLight = si.value().intr.areaLight;
                                    Float p_l = lightSampler.PMF(prevIntrCtx, areaLight) *
                                                areaLight.PDF_Li(prevIntrCtx, ray.d, true);
                                    Float w_l = PowerHeuristic(1, p_b, 1, p_l);

                                    L += beta * w_l * Le;
                                }
                            }

                            pixel.Ld += film.ToOutputRGB(L, lambda);

                            // Terminate path at maximum depth or if visible point has been set
                            if (depth++ == maxDepth || haveSetVisiblePoint)
                                break;

                            // Accumulate direct illumination at SPPM camera ray intersection
                            SampledSpectrum Ld = SampleLd(isect, bsdf, ref lambda, sampler, lightSampler);
                            if ((bool)Ld)
                                pixel.Ld += film.ToOutputRGB(beta * Ld, lambda);

                            // Possibly create visible point and end camera path
                            BxDFFlags flags = bsdf.Flags();
                            if (IsDiffuse(flags) || (IsGlossy(flags) && depth == maxDepth))
                            {
                                pixel.vp = new SPPMPixel.VisiblePoint(isect.p(), wo, bsdf, beta,
                                                                      lambda.SecondaryTerminated());
                                haveSetVisiblePoint = true;
                            }

                            // Spawn ray from SPPM camera path vertex
                            Float u = sampler.Get1D();
                            pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, u, sampler.Get2D());
                            if (!bs)
                                break;
                            specularBounce = bs.value().IsSpecular();
                            if (bs.value().IsTransmission())
                                etaScale *= Sqr(bs.value().eta);

                            beta *= bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                            p_b = bs.value().pdfIsProportional ? bsdf.PDF(wo, bs.value().wi) : bs.value().pdf;

                            SampledSpectrum rrBeta = beta * etaScale;
                            if (rrBeta.MaxComponentValue() < 1)
                            {
                                Float q = std.max<Float>(.05f, 1 - rrBeta.MaxComponentValue());
                                if (sampler.Get1D() < q)
                                    break;
                                beta /= 1 - q;
                            }
                            ray = isect.SpawnRay(ray, bsdf, bs.value().wi, bs.value().flags, bs.value().eta);
                            prevIntrCtx = new LightSampleContext(isect);
                        }
                    }
                });
                progress.Update(1);
                // Create grid of all SPPM visible points
                // Allocate grid for SPPM visible points
                int hashSize = NextPrime(nPixels);
                SPPMPixelListNode[] grid = new SPPMPixelListNode[hashSize];

                // Compute grid bounds for SPPM visible points
                Bounds3f gridBounds = new Bounds3f();
                Float maxRadius = 0;
                foreach (SPPMPixel pixel in pixels)
                {
                    if (!pixel.vp.beta)
                        continue;
                    Bounds3f vpBound = Expand(new Bounds3f(pixel.vp.p), pixel.radius);
                    gridBounds = Union(gridBounds, vpBound);
                    maxRadius = std.max(maxRadius, pixel.radius);
                }

                // Compute resolution of SPPM grid in each dimension
                int[] gridRes = new int[3];
                Vector3f diag = gridBounds.Diagonal();
                Float maxDiag = MaxComponentValue(diag);
                int baseGridRes = (int)(maxDiag / maxRadius);
                for (int i = 0; i < 3; ++i)
                    gridRes[i] = std.max<int>((int)(baseGridRes * diag[i] / maxDiag), 1);

                // Add visible points to SPPM grid
                ParallelFor2D(pixelBounds, (Bounds2i tileBounds) =>
                {
                    foreach (Point2i pPixel in tileBounds)
                    {
                        SPPMPixel pixel = pixels[pPixel];
                        if ((bool)pixel.vp.beta)
                        {
                            // Add pixel's visible point to applicable grid cells
                            // Find grid cell bounds for pixel's visible point, _pMin_ and _pMax_
                            Float r = pixel.radius;
                            Point3i pMin, pMax;
                            ToGrid(pixel.vp.p - new Vector3f(r, r, r), gridBounds, gridRes, out pMin);
                            ToGrid(pixel.vp.p + new Vector3f(r, r, r), gridBounds, gridRes, out pMax);

                            for (int z = pMin.z; z <= pMax.z; ++z)
                                for (int y = pMin.y; y <= pMax.y; ++y)
                                    for (int x = pMin.x; x <= pMax.x; ++x)
                                    {
                                        // Add visible point to grid cell $(x, y, z)$
                                        int h = (int)(Hash(new Point3i(x, y, z)) % (ulong)hashSize);
                                        CHECK_GE(h, 0);
                                        SPPMPixelListNode node = new SPPMPixelListNode();
                                        node.pixel = pixel;

                                        // Atomically add _node_ to the start of _grid[h]_'s linked list
                                        do
                                        {
                                            node.next = grid[h];
                                        } while (System.Threading.Interlocked.CompareExchange(ref grid[h], node, node.next) !=
                                                 node.next);
                                    }
                            if (gridCellsPerVisiblePoint == null)
                                gridCellsPerVisiblePoint = new StatIntDistribution();
                            gridCellsPerVisiblePoint.Add((1 + pMax.x - pMin.x) * (1 + pMax.y - pMin.y) *
                                                         (1 + pMax.z - pMin.z));
                        }
                    }
                });

                // Trace photons and accumulate contributions
                // Create per-thread scratch buffers for photon shooting
                ThreadLocal<ScratchBuffer> photonShootScratchBuffers =
                    new ThreadLocal<ScratchBuffer>(() => new ScratchBuffer());

                ParallelFor(0, photonsPerIteration, (long start, long end) =>
                {
                    // Follow photon paths for photon index range _start_ - _end_
                    ScratchBuffer scratchBuffer = photonShootScratchBuffers.Get();
                    Sampler sampler = threadSamplers.Get();
                    for (long photonIndex = start; photonIndex < end; ++photonIndex)
                    {
                        // Follow photon path for _photonIndex_
                        // Define sampling lambda functions for photon shooting
                        uint64_t haltonIndex = (uint64_t)iterCopy * (uint64_t)photonsPerIteration + (uint64_t)photonIndex;
                        int haltonDim = 0;
                        Func<Float> Sample1D = () =>
                        {
                            Float uu = ScrambledRadicalInverse(haltonDim, haltonIndex, digitPermutations[haltonDim]);
                            ++haltonDim;
                            return uu;
                        };

                        Func<Point2f> Sample2D = () =>
                        {
                            Point2f uu = new Point2f(
                                ScrambledRadicalInverse(haltonDim, haltonIndex, digitPermutations[haltonDim]),
                                ScrambledRadicalInverse(haltonDim + 1, haltonIndex, digitPermutations[haltonDim + 1]));
                            haltonDim += 2;
                            return uu;
                        };

                        // Choose light to shoot photon from
                        Float ul = Sample1D();
                        pstd.optional<SampledLight> sampledLight = shootLightSampler.Sample(ul);
                        if (!sampledLight)
                            continue;
                        Light light = sampledLight.value().light;
                        Float p_l = sampledLight.value().p;

                        // Compute sample values for photon ray leaving light source
                        Point2f uLight0 = Sample2D();
                        Point2f uLight1 = Sample2D();
                        Float uLightTime = camera.SampleTime(timeSample);

                        // Generate _photonRay_ from light source and initialize _beta_
                        SampledWavelengths lambda = passLambda;
                        pstd.optional<LightLeSample> les = light.SampleLe(uLight0, uLight1, ref lambda, uLightTime);
                        if (!les || les.value().pdfPos == 0 || les.value().pdfDir == 0 || !les.value().L)
                            continue;
                        RayDifferential photonRay = new RayDifferential(les.value().ray);
                        SampledSpectrum beta = (les.value().AbsCosTheta(photonRay.d) * les.value().L) /
                                               (p_l * les.value().pdfPos * les.value().pdfDir);
                        if (!beta)
                            continue;

                        // Follow photon path through scene and record intersections
                        for (int depth = 0; depth < maxDepth; ++depth)
                        {
                            // Intersect photon ray with scene and return if ray escapes
                            pstd.optional<ShapeIntersection> si = Intersect(new Ray(photonRay));
                            if (!si)
                                break;
                            SurfaceInteraction isect = si.value().intr;

                            ++totalPhotonSurfaceInteractions;
                            if (depth > 0)
                            {
                                // Add photon contribution to nearby visible points
                                Point3i photonGridIndex;
                                if (ToGrid(isect.p(), gridBounds, gridRes, out photonGridIndex))
                                {
                                    int h = (int)(Hash(photonGridIndex) % (ulong)hashSize);
                                    CHECK_GE(h, 0);
                                    // Add photon contribution to visible points in _grid[h]_
                                    for (SPPMPixelListNode node = System.Threading.Volatile.Read(ref grid[h]);
                                         node != null; node = node.next)
                                    {
                                        ++visiblePointsChecked;
                                        SPPMPixel pixel = node.pixel;
                                        if (DistanceSquared(pixel.vp.p, isect.p()) > Sqr(pixel.radius))
                                            continue;
                                        // Update _pixel_ $\Phi$ and $m$ for nearby photon
                                        Vector3f wi = -photonRay.d;
                                        SampledSpectrum Phi = beta * pixel.vp.bsdf.f(pixel.vp.wo, wi);
                                        // Update _Phi_i_ for photon contribution
                                        SampledWavelengths photonLambda = lambda;
                                        if (pixel.vp.secondaryLambdaTerminated)
                                            photonLambda.TerminateSecondary();
                                        RGB Phi_i = film.ToOutputRGB(pixel.vp.beta * Phi, photonLambda);
                                        for (int i = 0; i < 3; ++i)
                                            pixel.Phi_i[i].Add(Phi_i[i]);

                                        System.Threading.Interlocked.Increment(ref pixel.m);
                                    }
                                }
                            }
                            // Sample new photon ray direction
                            // Compute BSDF at photon intersection point
                            BSDF photonBSDF = isect.GetBSDF(photonRay, ref lambda, camera, scratchBuffer, sampler);
                            if (!photonBSDF)
                            {
                                isect.SkipIntersection(ref photonRay, si.value().tHit);
                                --depth;
                                continue;
                            }

                            // Sample BSDF _fr_ and direction _wi_ for reflected photon
                            Vector3f wo = -photonRay.d;
                            Point2f us2 = Sample2D();  // (C++ evaluates these arguments in unspecified order; GCC: right to left)
                            Float us1 = Sample1D();
                            pstd.optional<BSDFSample> bs =
                                photonBSDF.Sample_f(wo, us1, us2, TransportMode.Importance, BxDFReflTransFlags.All);
                            if (!bs)
                                break;
                            SampledSpectrum bnew = beta * bs.value().f * AbsDot(bs.value().wi, isect.shading.n) /
                                                   bs.value().pdf;

                            // Possibly terminate photon path with Russian roulette
                            Float betaRatio = bnew.MaxComponentValue() / beta.MaxComponentValue();
                            Float q = std.max<Float>(0, 1 - betaRatio);
                            if (Sample1D() < q)
                                break;
                            beta = bnew / (1 - q);

                            photonRay = new RayDifferential(isect.SpawnRay(bs.value().wi));
                        }

                        scratchBuffer.Reset();
                    }
                });
                // Reset _threadScratchBuffers_ after tracing photons
                threadScratchBuffers.ForAll((ScratchBuffer buffer) => buffer.Reset());

                progress.Update(1);
                photonPaths += photonsPerIteration;

                // Update pixel values from this pass's photons
                ParallelFor2D(pixelBounds, (Point2i pPixel) =>
                {
                    SPPMPixel p = pixels[pPixel];
                    int mm = p.m;
                    if (mm > 0)
                    {
                        // Compute new photon count and search radius given photons
                        Float gamma = (Float)2 / (Float)3;
                        Float nNew = p.n + gamma * mm;
                        Float rNew = p.radius * std.sqrt(nNew / (p.n + mm));

                        // Update $\tau$ for pixel
                        RGB Phi_i = new RGB(p.Phi_i[0].Get(), p.Phi_i[1].Get(), p.Phi_i[2].Get());
                        p.tau = (p.tau + Phi_i) * Sqr(rNew) / Sqr(p.radius);

                        // Set remaining pixel values for next photon pass
                        p.n = nNew;
                        p.radius = rNew;
                        p.m = 0;
                        for (int i = 0; i < 3; ++i)
                            p.Phi_i[i] = new AtomicFloat(0);
                    }
                    // Reset _VisiblePoint_ in pixel
                    p.vp.beta = new SampledSpectrum(0.0f);
                    p.vp.bsdf = default;
                });

                // Periodically write SPPM image to disk
                if (iter + 1 == nIterations || (iter + 1 <= 64 && IsPowerOf2(iter + 1)) || ((iter + 1) % 64 == 0))
                {
                    uint64_t np = (uint64_t)(iter + 1) * (uint64_t)photonsPerIteration;
                    Image rgbImage = new Image(PixelFormat.Float, new Point2i(pixelBounds.Diagonal()),
                                               new string[] { "R", "G", "B" });

                    ParallelFor2D(pixelBounds, (Point2i pPixel) =>
                    {
                        // Compute radiance _L_ for SPPM pixel _pPixel_
                        SPPMPixel pixel = pixels[pPixel];
                        RGB L = pixel.Ld / (Float)(iterCopy + 1) + pixel.tau / ((Float)np * Pi * Sqr(pixel.radius));

                        Point2i pImage = new Point2i(pPixel - pixelBounds.pMin);
                        rgbImage.SetChannels(pImage, new Float[] { L.r, L.g, L.b });
                    });

                    ImageMetadata metadata = new ImageMetadata();
                    metadata.renderTimeSeconds = (float)progress.ElapsedSeconds();
                    metadata.samplesPerPixel = iter + 1;
                    metadata.pixelBounds = pixelBounds;
                    metadata.fullResolution = camera.GetFilm().FullResolution();
                    metadata.colorSpace = colorSpace;
                    camera.InitMetadata(metadata);
                    rgbImage.Write(camera.GetFilm().GetFilename(), metadata);

                    // Write SPPM radius image, if requested
                    if (Environment.GetEnvironmentVariable("SPPM_RADIUS") != null)
                    {
                        Image rimg = new Image(PixelFormat.Float, new Point2i(pixelBounds.Diagonal()),
                                               new string[] { "Radius" });
                        Float minrad = 1e30f, maxrad = 0;
                        foreach (SPPMPixel p in pixels)
                        {
                            minrad = std.min(minrad, p.radius);
                            maxrad = std.max(maxrad, p.radius);
                        }
                        Console.Error.Write(StringPrintf("iterations: %d (%.2f s) radius range: %f - %f\n",
                                                         iter + 1, progress.ElapsedSeconds(), minrad, maxrad));
                        foreach (Point2i pPixel in pixelBounds)
                        {
                            SPPMPixel p = pixels[pPixel];
                            Float v = 1.0f - (p.radius - minrad) / (maxrad - minrad);
                            Point2i pImage = new Point2i(pPixel - pixelBounds.pMin);
                            rimg.SetChannel(pImage, 0, v);
                        }
                        ImageMetadata metadata2 = new ImageMetadata();
                        metadata2.pixelBounds = pixelBounds;
                        metadata2.fullResolution = camera.GetFilm().FullResolution();
                        rimg.Write("sppm_radius.png", metadata2);
                    }
                }
            }
            progress.Done();
        }

        SampledSpectrum SampleLd(SurfaceInteraction intr, BSDF bsdf, ref SampledWavelengths lambda, Sampler sampler,
                                 LightSampler lightSampler)
        {
            // Initialize _LightSampleContext_ for light sampling
            LightSampleContext ctx = new LightSampleContext(intr);
            // Try to nudge the light sampling position to correct side of the surface
            BxDFFlags flags = bsdf.Flags();
            if (IsReflective(flags) && !IsTransmissive(flags))
                ctx.pi = intr.OffsetRayOrigin(intr.wo);
            else if (IsTransmissive(flags) && !IsReflective(flags))
                ctx.pi = intr.OffsetRayOrigin(-intr.wo);

            // Choose a light source for the direct lighting calculation
            Float u = sampler.Get1D();
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(ctx, u);
            Point2f uLight = sampler.Get2D();
            if (!sampledLight)
                return new SampledSpectrum(0.0f);

            // Sample a point on the light source for direct lighting
            Light light = sampledLight.value().light;
            DCHECK(light != null && sampledLight.value().p > 0);
            pstd.optional<LightLiSample> ls = light.SampleLi(ctx, uLight, lambda, true);
            if (!ls || !ls.value().L || ls.value().pdf == 0)
                return new SampledSpectrum(0.0f);

            // Evaluate BSDF for light sample and check light visibility
            Vector3f wo = intr.wo, wi = ls.value().wi;
            SampledSpectrum f = bsdf.f(wo, wi) * AbsDot(wi, intr.shading.n);
            if (!f || !Unoccluded(intr, ls.value().pLight))
                return new SampledSpectrum(0.0f);

            // Return light's contribution to reflected radiance
            Float p_l = sampledLight.value().p * ls.value().pdf;
            if (IsDeltaLight(light.Type()))
                return ls.value().L * f / p_l;
            else
            {
                Float p_b = bsdf.PDF(wo, wi);
                Float w_l = PowerHeuristic(1, p_l, 1, p_b);
                return w_l * ls.value().L * f / p_l;
            }
        }

        public override string ToString()
        {
            return StringPrintf("[ SPPMIntegrator camera: %s initialSearchRadius: %f " +
                                "maxDepth: %d photonsPerIteration: %d " +
                                "colorSpace: %s digitPermutations: (elided) ]",
                                camera, initialSearchRadius, maxDepth, photonsPerIteration, colorSpace);
        }

        public static SPPMIntegrator Create(ParameterDictionary parameters, RGBColorSpace colorSpace, Camera camera,
                                            Sampler sampler, Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            int photonsPerIter = parameters.GetOneInt("photonsperiteration", -1);
            Float radius = parameters.GetOneFloat("radius", 1.0f);
            int seed = parameters.GetOneInt("seed", Options.seed);
            return new SPPMIntegrator(camera, sampler, aggregate, lights, photonsPerIter, maxDepth, radius, seed,
                                      colorSpace);
        }

        // SPPMIntegrator Private Members
        Camera camera;
        Float initialSearchRadius;
        Sampler samplerPrototype;
        int digitPermutationsSeed;
        int maxDepth;
        int photonsPerIteration;
        RGBColorSpace colorSpace;
    }

    // FunctionIntegrator Definition
    public sealed unsafe class FunctionIntegrator : Integrator
    {
        // FunctionIntegrator Method Definitions
        public FunctionIntegrator(Func<Point2f, double> func, string outputFilename, Camera camera, Sampler sampler,
                                  bool skipBad, string imageFilename)
            : base(null, new List<Light>())
        {
            this.func = func;
            this.outputFilename = outputFilename;
            this.camera = camera;
            this.baseSampler = sampler;
            this.skipBad = skipBad;
            this.imageFilename = imageFilename;
        }

        static class funcs
        {
            public static double step(Point2f p) { return (p.x < 0.5) ? 2 : 0; }
            public static double diagonal(Point2f p) { return (p.x + p.y < 1) ? 2 : 0; }
            public static double disk(Point2f p)
            {
                return Distance(p, new Point2f(0.5f, 0.5f)) < 0.5 ? (1 / (Pi * Sqr(0.5f))) : 0;
            }
            public static double checkerboard(Point2f p)
            {
                int freq = 10;
                Point2i pi = new Point2i((int)(p.x * freq), (int)(p.y * freq));
                return ((pi.x & 1) ^ (pi.y & 1)) != 0 ? 2 : 0;
            }
            static double sa = Math.Sin(Radians(45)), ca = Math.Cos(Radians(45));
            public static double rotatedCheckerboard(Point2f p)
            {
                double nrm = 1.00006866455078125;
                return checkerboard(new Point2f((Float)(10 + p.x * ca - p.y * sa),
                                                (Float)(10 + p.x * sa + p.y * ca))) / nrm;
            }
            static double Gaussian(double x, double mu = 0, double sigma = 1)
            {
                return 1 / Math.Sqrt(2 * Pi * sigma * sigma) * Math.Exp(-Sqr(x - mu) / (2 * sigma * sigma));
            }
            static double GaussianIntegral(double x0, double x1, double mu = 0, double sigma = 1)
            {
                double sigmaRoot2 = sigma * 1.414213562373095;
                return 0.5f * (std.erf((mu - x0) / sigmaRoot2) - std.erf((mu - x1) / sigmaRoot2));
            }
            static double nrm = Sqr(GaussianIntegral(0, 1, 0.5, 0.25));
            public static double gaussian(Point2f p)
            {
                double mu = 0.5, sigma = 0.25;
                return Gaussian(p.x, mu, sigma) * Gaussian(p.y, mu, sigma) / nrm;
            }
        }

        public static FunctionIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                                FileLoc loc)
        {
            string funcName = parameters.GetOneString("function", "step");
            string defaultOut = Options.imageFile.Length == 0 ? StringPrintf("%s-mse.txt", funcName)
                                                              : Options.imageFile;
            string outputFilename = parameters.GetOneString("filename", defaultOut);

            Func<Point2f, double> func = null;
            if (funcName == "step")
                func = funcs.step;
            else if (funcName == "diagonal")
                func = funcs.diagonal;
            else if (funcName == "disk")
                func = funcs.disk;
            else if (funcName == "checkerboard")
                func = funcs.checkerboard;
            else if (funcName == "rotatedcheckerboard")
                func = funcs.rotatedCheckerboard;
            else if (funcName == "gaussian")
                func = funcs.gaussian;

            if (func == null)
                ErrorExit(loc, StringPrintf("%s: function for FunctionIntegrator unknown", funcName));

            if (sampler is SobolSampler)
                ErrorExit(loc, "\"sobol\" sampler should be replaced with \"paddedsobol\" for " +
                               "the \"function\" integrator.");

            bool skipBad = parameters.GetOneBool("skipbad", true);
            string imageFilename = parameters.GetOneString("imagefilename", "");

            return new FunctionIntegrator(func, outputFilename, camera, sampler, skipBad, imageFilename);
        }

        public override void Render()
        {
            string result = "";
            Bounds2i pixelBounds = camera.GetFilm().PixelBounds();
            int nPixels = pixelBounds.Area();
            Array2D<double> sumv = new Array2D<double>(pixelBounds);
            int nSamples = baseSampler.SamplesPerPixel();

            if (imageFilename.Length != 0)
            {
                RNG rng0 = new RNG();
                Vector2i res = pixelBounds.pMax - pixelBounds.pMin;
                Image image = new Image(PixelFormat.Float, new Point2i(res.x, res.y), new string[] { "Y" });
                for (int y = 0; y < res.y; ++y)
                    for (int x = 0; x < res.x; ++x)
                    {
                        int nSamples2 = 256;
                        Float sum0 = 0;
                        for (int i = 0; i < nSamples2; ++i)
                            sum0 += (Float)func(new Point2f((x + rng0.Uniform<Float>()) / res.x,
                                                            (y + rng0.Uniform<Float>()) / res.y));
                        image.SetChannel(new Point2i(x, y), 0, sum0 / nSamples2);
                    }
                image.Write(imageFilename);
            }

            ProgressReporter prog = new ProgressReporter(nSamples, "Sampling", Options.quiet);

            bool isHalton = baseSampler is HaltonSampler;
            bool isStratified = baseSampler is StratifiedSampler;
            bool isSobol = baseSampler is PaddedSobolSampler;
            DigitPermutation[][] digitPermutations = new DigitPermutation[2][];
            uint64_t[][] owenHash = new uint64_t[2][];
            RNG rng = new RNG();
            if (isHalton)
            {
                for (int d = 0; d < 2; ++d)
                {
                    digitPermutations[d] = new DigitPermutation[nPixels];
                    for (int i = 0; i < nPixels; ++i)
                        digitPermutations[d][i] = new DigitPermutation(d == 0 ? 2 : 3, rng.Uniform<uint32_t>(), null);
                }
            }
            if (isHalton || isSobol)
            {
                for (int d = 0; d < 2; ++d)
                {
                    owenHash[d] = new uint64_t[nPixels];
                    for (int i = 0; i < nPixels; ++i)
                        owenHash[d][i] = rng.Uniform<uint64_t>();
                }
            }

            int nTakenSamples = 0;
            ThreadLocal<Sampler> threadSamplers = new ThreadLocal<Sampler>(() => baseSampler.Clone());
            for (int sampleIndex = 0; sampleIndex < nSamples; ++sampleIndex)
            {
                bool reportResult = true;
                if (skipBad)
                {
                    int nSamples3 = sampleIndex + 1;
                    if (isStratified && Sqr((int)std.sqrt((Float)nSamples3)) != nSamples3)
                    {
                        prog.Update(1);
                        continue;
                    }
                    else if (isSobol && !IsPowerOf2(nSamples3))
                        reportResult = false;
                    else if (isHalton)
                    {
                        int n2 = 0, n3 = 0;
                        while (true)
                        {
                            if ((nSamples3 % 2) == 0)
                            {
                                nSamples3 /= 2;
                                ++n2;
                            }
                            else if ((nSamples3 % 3) == 0)
                            {
                                nSamples3 /= 3;
                                ++n3;
                            }
                            else
                                break;
                        }
                        if (nSamples3 != 1 || n2 != n3)
                            reportResult = false;
                    }
                }

                ++nTakenSamples;
                int sampleIndexCopy = sampleIndex, nTakenCopy = nTakenSamples;

                if (isStratified)
                {
                    int spp = sampleIndex + 1;
                    int factor = (int)std.sqrt((Float)spp);

                    while ((spp % factor) != 0)
                        --factor;

                    ParallelFor2D(pixelBounds, (Point2i pPixel) =>
                    {
                        int pixelIndex = (pPixel.x - pixelBounds.pMin.x) +
                                         (pPixel.y - pixelBounds.pMin.y) * (pixelBounds.pMax.x - pixelBounds.pMin.x);
                        RNG prng = new RNG((uint64_t)pixelIndex, MixBits((uint64_t)(sampleIndexCopy + 1)));

                        Float v = 0;
                        int nx = factor, ny = spp / factor;
                        for (int x = 0; x < nx; ++x)
                        {
                            for (int y = 0; y < ny; ++y)
                            {
                                Point2f u = new Point2f((x + prng.Uniform<Float>()) / nx,
                                                        (y + prng.Uniform<Float>()) / ny);
                                v += (Float)func(u);
                            }
                        }
                        // Need nTakenSamples factor to cancel out division in sumSE computation.
                        sumv[pPixel] = v * nTakenCopy / (nx * ny);
                    });
                }
                else
                {
                    ParallelFor2D(pixelBounds, (Bounds2i bounds) =>
                    {
                        foreach (Point2i pPixel in bounds)
                        {
                            Point2f u = default;
                            if (isHalton)
                            {
                                int pixelIndex = (pPixel.x - pixelBounds.pMin.x) +
                                                 (pPixel.y - pixelBounds.pMin.y) *
                                                     (pixelBounds.pMax.x - pixelBounds.pMin.x);
                                DCHECK_GE(pixelIndex, 0);
                                DCHECK_LT(pixelIndex, nPixels);

                                switch (((HaltonSampler)baseSampler).GetRandomizeStrategy())
                                {
                                case RandomizeStrategy.None:
                                    u = new Point2f(RadicalInverse(0, (uint64_t)sampleIndexCopy),
                                                    RadicalInverse(1, (uint64_t)sampleIndexCopy));
                                    break;
                                case RandomizeStrategy.PermuteDigits:
                                    u = new Point2f(
                                        ScrambledRadicalInverse(0, (uint64_t)sampleIndexCopy,
                                                                digitPermutations[0][pixelIndex]),
                                        ScrambledRadicalInverse(1, (uint64_t)sampleIndexCopy,
                                                                digitPermutations[1][pixelIndex]));
                                    break;
                                case RandomizeStrategy.Owen:
                                    u = new Point2f(
                                        OwenScrambledRadicalInverse(0, (uint64_t)sampleIndexCopy,
                                                                    (uint32_t)owenHash[0][pixelIndex]),
                                        OwenScrambledRadicalInverse(1, (uint64_t)sampleIndexCopy,
                                                                    (uint32_t)owenHash[1][pixelIndex]));
                                    break;
                                default:
                                    LOG_FATAL("Unhandled randomization strategy");
                                    break;
                                }
                            }
                            else if (isSobol)
                            {
                                int pixelIndex = (pPixel.x - pixelBounds.pMin.x) +
                                                 (pPixel.y - pixelBounds.pMin.y) *
                                                     (pixelBounds.pMax.x - pixelBounds.pMin.x);
                                DCHECK_GE(pixelIndex, 0);
                                DCHECK_LT(pixelIndex, nPixels);

                                switch (((PaddedSobolSampler)baseSampler).GetRandomizeStrategy())
                                {
                                case RandomizeStrategy.None:
                                    u = new Point2f(SobolSample(sampleIndexCopy, 0, new NoRandomizer()),
                                                    SobolSample(sampleIndexCopy, 1, new NoRandomizer()));
                                    break;
                                case RandomizeStrategy.PermuteDigits:
                                    u = new Point2f(
                                        SobolSample(sampleIndexCopy, 0,
                                                    new BinaryPermuteScrambler((uint32_t)owenHash[0][pixelIndex])),
                                        SobolSample(sampleIndexCopy, 1,
                                                    new BinaryPermuteScrambler((uint32_t)owenHash[1][pixelIndex])));
                                    break;
                                case RandomizeStrategy.FastOwen:
                                    u = new Point2f(
                                        SobolSample(sampleIndexCopy, 0,
                                                    new FastOwenScrambler((uint32_t)owenHash[0][pixelIndex])),
                                        SobolSample(sampleIndexCopy, 1,
                                                    new FastOwenScrambler((uint32_t)owenHash[1][pixelIndex])));
                                    break;
                                case RandomizeStrategy.Owen:
                                    u = new Point2f(
                                        SobolSample(sampleIndexCopy, 0,
                                                    new OwenScrambler((uint32_t)owenHash[0][pixelIndex])),
                                        SobolSample(sampleIndexCopy, 1,
                                                    new OwenScrambler((uint32_t)owenHash[1][pixelIndex])));
                                    break;
                                default:
                                    LOG_FATAL("Unhandled randomization strategy");
                                    break;
                                }
                            }
                            else
                            {
                                Sampler sampler = threadSamplers.Get();
                                sampler.StartPixelSample(pPixel, sampleIndexCopy, 0);
                                u = sampler.GetPixel2D();
                            }
                            sumv[pPixel] += (Float)func(u);
                        }
                    });
                }

                // Compute average MSE/variance
                if (reportResult)
                {
                    double sumSE = 0;
                    foreach (double v in sumv)
                        sumSE += Sqr(v / nTakenSamples - 1);
                    Float mse = (Float)(sumSE / nPixels);

                    result += StringPrintf("%d %f\n", sampleIndex + 1, mse);
                }
                prog.Update(1);
            }

            // Make sure that it's basically one...
            double sum = 0;
            foreach (double v in sumv)
                sum += v;
            double avg = sum / ((double)nPixels * (double)nTakenSamples);
            if (avg < 0.999 || avg > 1.001)
                Warning(StringPrintf("Average estimate is %f, which is suspiciously far from 1.", avg));

            prog.Done();

            WriteFileContents(outputFilename, result);
        }

        public override string ToString()
        {
            return StringPrintf("[ FunctionIntegrator outputFilename: %s camera: %s baseSampler: %s ]",
                                outputFilename, camera, baseSampler);
        }

        Func<Point2f, double> func;
        string outputFilename;
        Camera camera;
        Sampler baseSampler;
        bool skipBad;
        string imageFilename;
    }

    // VolPathIntegrator Definition
    public sealed unsafe class VolPathIntegrator : RayIntegrator
    {
        // STAT_COUNTER("Integrator/Volume interactions", volumeInteractions);
        // STAT_COUNTER("Integrator/Surface interactions", surfaceInteractions);
        // STAT_PERCENT("Integrator/Regularized BSDFs", regularizedBSDFs, totalBSDFs);  (defined in integrators.cpp)
        [ThreadStatic] static long volumeInteractions, surfaceInteractions, regularizedBSDFs, totalBSDFs;
        static StatRegisterer STATS_REG = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Integrator/Volume interactions", volumeInteractions);
            volumeInteractions = 0;
            accum.ReportCounter("Integrator/Surface interactions", surfaceInteractions);
            surfaceInteractions = 0;
            accum.ReportPercentage("Integrator/Regularized BSDFs", regularizedBSDFs, totalBSDFs);
            regularizedBSDFs = totalBSDFs = 0;
        });

        // VolPathIntegrator Public Methods
        public VolPathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive aggregate,
                                 List<Light> lights,
                                 string lightSampleStrategy = "bvh",
                                 bool regularize = false)
            : base(camera, sampler, aggregate, TextureMedium.AddEmissionLights(lights))
        {
            this.maxDepth = maxDepth;
            // (not in pbrt-v4) Media that ask for next event estimation of their emission are lights too
            this.lightSampler = LightSampler.Create(lightSampleStrategy, TextureMedium.AddEmissionLights(lights).ToArray(),
                                                    new Allocator());
            this.regularize = regularize;
        }

        static Float[] ToFloats(params double[] v)
        {
            Float[] r = new Float[v.Length];
            for (int i = 0; i < v.Length; ++i)
                r[i] = (Float)v[i];
            return r;
        }

        static Point2f P2(double x, double y) { return new Point2f((Float)x, (Float)y); }

        // VolPathIntegrator Method Definitions
        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambdaRef,
                                           Sampler sampler, ScratchBuffer scratchBuffer,
                                           VisibleSurface visibleSurf)
        {
            // (Lambdas cannot capture ref parameters: work on a local copy of _lambda_ and write it
            // back when leaving, since GetBSDF() may terminate secondary wavelengths.)
            SampledWavelengths lambda = lambdaRef;
            try
            {
                // Declare state variables for volumetric path sampling
                SampledSpectrum L = new SampledSpectrum(0.0f), beta = new SampledSpectrum(1.0f),
                                r_u = new SampledSpectrum(1.0f), r_l = new SampledSpectrum(1.0f);
                bool specularBounce = false, anyNonSpecularBounces = false;
                // (not in pbrt-v4) The path since its last scattering vertex went through a medium whose shadow rays use the
                // unbiased ray-marching transmittance estimator for area lights, which has no MIS partner
                bool throughKettunen = false;
                // (not in pbrt-v4) The path's last vertex was a medium scattering event, so media that don't allow
                // multiple scattering (LuxCore's "multiscattering false") only attenuate the ray
                bool scatteredInMedium = false;
                int depth = 0;
                Float etaScale = 1;

                LightSampleContext prevIntrContext = default;
                // (not in pbrt-v4) For the line sampling of emission (see SampleLdFnee): the density that the last scattering
                // vertex had for the direction of the ray, and the ratio of the density of sampling the direction toward
                // the emission to it, which is looked up when the emission is first found along the ray
                Float fneeScatterPdf = 0, fneeRatio = -1;
                Point3f fneeOrigin = default;

                while (true)
                {
                    // Sample segment of volumetric scattering path
                    pstd.optional<ShapeIntersection> si = Intersect(new Ray(ray));
                    if (ray.medium != null)
                    {
                        // Sample the participating medium
                        bool scattered = false, terminated = false;
                        Float tMax = si ? si.value().tHit : Infinity;
                        if (ray.medium is TextureMedium kettunenMedium && kettunenMedium.UsesKettunen())
                            throughKettunen = true;
                        // Initialize _RNG_ for sampling the majorant transmittance
                        uint64_t hash0 = Hash(sampler.Get1D());
                        uint64_t hash1 = Hash(sampler.Get1D());
                        RNG rng = new RNG(hash0, hash1);

                        SampledWavelengths lambdaCap = lambda;
                        Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> mediumCallback =
                            (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj,
                             SampledSpectrum T_maj_) =>
                            {
                                // Handle medium scattering event for ray
                                if (!beta)
                                {
                                    terminated = true;
                                    return false;
                                }
                                ++volumeInteractions;
                                // Add emission from medium scattering event
                                if (depth < maxDepth && (bool)mp.Le)
                                {
                                    // Compute $\beta'$ at new path vertex
                                    Float pdf = sigma_maj[0] * T_maj_[0];
                                    SampledSpectrum betap = beta * T_maj_ / pdf;

                                    // Compute rescaled path probability for absorption at path vertex
                                    SampledSpectrum r_e = r_u * sigma_maj * T_maj_ / pdf;

                                    // Update _L_ for medium emission
                                    if ((bool)r_e)
                                    {
                                        // (not in pbrt-v4) A medium whose emission is also sampled from the previous scattering
                                        // vertex: the light strategy would have chosen this point with probability density
                                        // pmf * pdfPoint * t^2 per direction and length, where the density of this strategy
                                        // is sigma_maj T_maj per length (hero values), and the light strategy's null
                                        // collisions on the way are already in r_l, with the tail of T_maj added here
                                        SampledSpectrum r_sum = r_e;
                                        VolumeEmissionLight emissionLight = (ray.medium as TextureMedium)?.EmissionLight();
                                        if (emissionLight != null && emissionLight.UsesFnee())
                                        {
                                            // The strategy that samples the direction toward the emission integrates along the
                                            // same ray with the same tracking, so the two differ by the densities of the
                                            // direction alone
                                            if (depth > 0 && !specularBounce && fneeScatterPdf > 0)
                                            {
                                                if (fneeRatio < 0)
                                                {
                                                    Float pmfDirection = lightSampler.PMF(prevIntrContext, emissionLight);
                                                    fneeRatio = pmfDirection * emissionLight.PdfDirection(fneeOrigin, ray.d) / fneeScatterPdf;
                                                }
                                                r_sum = r_e * (1 + fneeRatio);
                                            }
                                        }
                                        else if (emissionLight != null && depth > 0 && !specularBounce)
                                        {
                                            Float pmf = lightSampler.PMF(prevIntrContext, emissionLight);
                                            if (pmf > 0)
                                            {
                                                Float t2 = DistanceSquared(prevIntrContext.p(), p);
                                                Float densityRatio = pmf * emissionLight.PdfPoint(p) * t2 / sigma_maj[0];
                                                r_sum = r_e + r_l * (T_maj_ / T_maj_[0]) * densityRatio;
                                            }
                                        }
                                        L += betap * (mp.emissionPerLength ? mp.Le : mp.sigma_a * mp.Le) / r_sum.Average();
                                    }
                                }

                                // Compute medium event probabilities for interaction
                                // (not in pbrt-v4) With a signed null coefficient the event probabilities are
                                // proportional to the absolute values of the coefficients, which sum to
                                // _sigma_c_ >= _sigma_maj_, and the weights carry the sign.
                                SampledSpectrum sigma_n_signed = sigma_maj - mp.sigma_a - mp.sigma_s;
                                SampledSpectrum sigma_c = mp.signedNull ? mp.sigma_a + mp.sigma_s + Abs(sigma_n_signed)
                                                                        : sigma_maj;
                                Float pAbsorb = mp.sigma_a[0] / sigma_c[0];
                                Float pScatter = mp.sigma_s[0] / sigma_c[0];
                                Float pNull = mp.signedNull ? std.abs(sigma_n_signed[0]) / sigma_c[0]
                                                            : std.max<Float>(0, 1 - pAbsorb - pScatter);
                                // Scattering that isn't allowed is attenuation, which terminating the path at the
                                // collision gives in expectation
                                bool scatterAllowed = !(mp.singleScatter && scatteredInMedium);
                                if (!scatterAllowed)
                                {
                                    pAbsorb += pScatter;
                                    pScatter = 0;
                                }
                                bool spectralTracking = mp.spectralTracking;
                                if (spectralTracking)
                                {
                                    // Spectral tracking (Kutz et al. 2017): the probability of each event
                                    // follows the path's spectral throughput times the event's coefficient
                                    SampledSpectrum sigma_n_ = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                                    Float wAbsorb = (beta * mp.sigma_a).Average();
                                    Float wScatter = scatterAllowed ? (beta * mp.sigma_s).Average() : 0;
                                    if (!scatterAllowed)
                                        wAbsorb += (beta * mp.sigma_s).Average();
                                    Float wNull = (beta * sigma_n_).Average();
                                    Float wSum = wAbsorb + wScatter + wNull;
                                    if (!(wSum > 0))
                                    {
                                        terminated = true;
                                        return false;
                                    }
                                    pAbsorb = wAbsorb / wSum;
                                    pScatter = wScatter / wSum;
                                    pNull = wNull / wSum;
                                }
                                else
                                    CHECK_GE(1 - pAbsorb - pScatter, (Float)(-1e-6));
                                // Sample medium scattering event type and update path
                                Float um = rng.Uniform<Float>();
                                int mode = SampleDiscrete(new Float[] { pAbsorb, pScatter, pNull }, um);
                                if (mode == 0)
                                {
                                    // Handle absorption along ray path
                                    terminated = true;
                                    return false;
                                }
                                else if (mode == 1)
                                {
                                    // Handle scattering along ray path
                                    // Stop path sampling if maximum depth has been reached
                                    if (depth++ >= maxDepth)
                                    {
                                        terminated = true;
                                        return false;
                                    }

                                    // Update _beta_ and _r_u_ for real-scattering event
                                    Float pdf = T_maj_[0] * mp.sigma_s[0];
                                    if (spectralTracking)
                                    {
                                        pdf = T_maj_[0] * sigma_maj[0] * pScatter;
                                        beta *= T_maj_ * mp.sigma_s / pdf;
                                    }
                                    else if (mp.signedNull)
                                    {
                                        SampledSpectrum densityRate = mp.mixture ? mp.mixRatio : T_maj_ * sigma_maj;
                                        pdf = densityRate[0] * pScatter;
                                        beta *= T_maj_ * mp.sigma_s / pdf;
                                        r_u *= densityRate * SafeDiv(mp.sigma_s, sigma_c) / pdf;
                                    }
                                    else
                                    {
                                        beta *= T_maj_ * mp.sigma_s / pdf;
                                        r_u *= T_maj_ * mp.sigma_s / pdf;
                                    }

                                    if ((bool)beta && (bool)r_u)
                                    {
                                        // Sample direct lighting at volume-scattering event
                                        MediumInteraction intr = new MediumInteraction(p, -ray.d, ray.time, ray.medium,
                                                                                       mp.phase);
                                        L += SampleLd(intr, null, lambdaCap, sampler, beta, r_u, depth < maxDepth);

                                        // Sample new direction at real-scattering event
                                        Point2f u = sampler.Get2D();
                                        pstd.optional<PhaseFunctionSample> ps =
                                            intr.phase.Sample_p(-ray.d, u);
                                        if (!ps || ps.value().pdf == 0)
                                            terminated = true;
                                        else
                                        {
                                            // Update ray path state for indirect volume scattering
                                            beta *= ps.value().p / ps.value().pdf;
                                            r_l = r_u / ps.value().pdf;
                                            prevIntrContext = new LightSampleContext(intr);
                                            fneeScatterPdf = ps.value().pdf;
                                            fneeRatio = -1;
                                            fneeOrigin = p;
                                            scattered = true;
                                            ray.o = p;
                                            ray.d = ps.value().wi;
                                            specularBounce = false;
                                            anyNonSpecularBounces = true;
                                        }
                                    }
                                    return false;
                                }
                                else
                                {
                                    // Handle null scattering along ray path
                                    SampledSpectrum sigma_n =
                                        ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                                    if (spectralTracking)
                                    {
                                        // Every wavelength uses the same majorant, so only the event
                                        // probability differs between this path and light sampling's
                                        Float pdfS = T_maj_[0] * sigma_maj[0] * pNull;
                                        beta *= T_maj_ * sigma_n / pdfS;
                                        r_l *= T_maj_ * sigma_maj / pdfS;
                                        return (bool)beta && (bool)r_u;
                                    }
                                    if (mp.signedNull)
                                    {
                                        SampledSpectrum densityRate = mp.mixture ? mp.mixRatio : T_maj_ * sigma_maj;
                                        Float pdfU = densityRate[0] * pNull;
                                        beta *= T_maj_ * sigma_n_signed / pdfU;
                                        if (pdfU == 0)
                                            beta = new SampledSpectrum(0.0f);
                                        r_u *= densityRate * SafeDiv(Abs(sigma_n_signed), sigma_c) / pdfU;
                                        r_l *= densityRate / pdfU;
                                        return (bool)beta && (bool)r_u;
                                    }
                                    Float pdf = T_maj_[0] * sigma_n[0];
                                    beta *= T_maj_ * sigma_n / pdf;
                                    if (pdf == 0)
                                        beta = new SampledSpectrum(0.0f);
                                    r_u *= T_maj_ * sigma_n / pdf;
                                    r_l *= (mp.adaptiveLight ? mp.lightDensity
                                                                 : T_maj_ * ((bool)mp.sigma_maj_light ? mp.sigma_maj_light : sigma_maj)) / pdf;
                                    return (bool)beta && (bool)r_u;
                                }
                            };
                        SampledSpectrum tailLight = default;
                        bool adaptiveLight = ray.medium is TextureMedium adaptiveMedium && adaptiveMedium.UsesAdaptiveRatio();
                        bool mixtureSampling = ray.medium is TextureMedium mixtureMedium && mixtureMedium.UsesMixture();
                        SampledSpectrum T_maj = mixtureSampling
                            ? ((TextureMedium)ray.medium).SampleT_majMixture(
                                  new Ray(ray), tMax, sampler.Get1D(), ref rng, lambda, mediumCallback, out tailLight)
                            : adaptiveLight
                            ? ((TextureMedium)ray.medium).SampleT_majAdaptiveCamera(
                                  new Ray(ray), tMax, sampler.Get1D(), ref rng, lambda, mediumCallback, out tailLight)
                            : SampleT_maj(new Ray(ray), tMax, sampler.Get1D(), ref rng, lambda, mediumCallback, true);
                        // Handle terminated, scattered, and unscattered medium rays
                        if (terminated || !beta || !r_u)
                            return L;
                        if (scattered)
                        {
                            throughKettunen = false;
                            scatteredInMedium = true;
                            continue;
                        }

                        if (mixtureSampling)
                        {
                            beta *= T_maj / tailLight[0];
                            r_u *= tailLight / tailLight[0];
                            r_l *= tailLight / tailLight[0];
                        }
                        else
                        {
                            beta *= T_maj / T_maj[0];
                            r_u *= T_maj / T_maj[0];
                            r_l *= (adaptiveLight ? tailLight : T_maj) / T_maj[0];
                        }
                    }
                    // Handle surviving unscattered rays
                    // Add emitted light at volume path vertex or from the environment
                    if (!si)
                    {
                        // Accumulate contributions from infinite light sources
                        foreach (Light light in infiniteLights)
                        {
                            SampledSpectrum Le = light.Le(new Ray(ray), lambda);
                            if ((bool)Le)
                            {
                                if (depth == 0 || specularBounce)
                                    L += beta * Le / r_u.Average();
                                else
                                {
                                    // Add infinite light contribution using both PDFs with MIS
                                    Float p_l = lightSampler.PMF(prevIntrContext, light) *
                                                light.PDF_Li(prevIntrContext, ray.d, true);
                                    r_l *= p_l;
                                    L += beta * Le / (r_u + r_l).Average();
                                }
                            }
                        }

                        break;
                    }
                    SurfaceInteraction isect = si.value().intr;
                    SampledSpectrum LeSurf = isect.Le(-ray.d, lambda);
                    if ((bool)LeSurf)
                    {
                        // Add contribution of emission from intersected surface
                        if (depth == 0 || specularBounce)
                            L += beta * LeSurf / r_u.Average();
                        else if (throughKettunen)
                        {
                            // Light sampling alone accounts for paths through such media
                        }
                        else
                        {
                            // Add surface light contribution using both PDFs with MIS
                            Light areaLight = isect.areaLight;
                            Float p_l = lightSampler.PMF(prevIntrContext, areaLight) *
                                        areaLight.PDF_Li(prevIntrContext, ray.d, true);
                            r_l *= p_l;
                            L += beta * LeSurf / (r_u + r_l).Average();
                        }
                    }

                    // Get BSDF and skip over medium boundaries
                    BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                    if (!bsdf)
                    {
                        isect.SkipIntersection(ref ray, si.value().tHit);
                        continue;
                    }

                    // Initialize _visibleSurf_ at first intersection
                    if (depth == 0 && visibleSurf != null)
                    {
                        // Estimate BSDF's albedo
                        // Define sample arrays _ucRho_ and _uRho_ for reflectance estimate
                        const int nRhoSamples = 16;
                        Float[] ucRho = ToFloats(
                            0.75741637, 0.37870818, 0.7083487, 0.18935409, 0.9149363, 0.35417435,
                            0.5990858, 0.09467703, 0.8578725, 0.45746812, 0.686759, 0.17708716,
                            0.9674518, 0.2995429, 0.5083201, 0.047338516);
                        Point2f[] uRho = new Point2f[nRhoSamples] {
                            P2(0.855985, 0.570367), P2(0.381823, 0.851844),
                            P2(0.285328, 0.764262), P2(0.733380, 0.114073),
                            P2(0.542663, 0.344465), P2(0.127274, 0.414848),
                            P2(0.964700, 0.947162), P2(0.594089, 0.643463),
                            P2(0.095109, 0.170369), P2(0.825444, 0.263359),
                            P2(0.429467, 0.454469), P2(0.244460, 0.816459),
                            P2(0.756135, 0.731258), P2(0.516165, 0.152852),
                            P2(0.180888, 0.214174), P2(0.898579, 0.503897)};

                        SampledSpectrum albedo = bsdf.rho(isect.wo, ucRho, uRho);

                        visibleSurf.Assign(new VisibleSurface(isect, albedo, lambda));
                    }

                    // Terminate path if maximum depth reached
                    if (depth++ >= maxDepth)
                        return L;

                    ++surfaceInteractions;
                    // Possibly regularize the BSDF
                    if (regularize && anyNonSpecularBounces)
                    {
                        ++regularizedBSDFs;
                        bsdf.Regularize();
                    }

                    // Sample illumination from lights to find attenuated path contribution
                    if (IsNonSpecular(bsdf.Flags()))
                    {
                        L += SampleLd(isect, bsdf, lambda, sampler, beta, r_u, depth < maxDepth);
                        DCHECK(IsInf(L.y(lambda)) == false);
                    }
                    prevIntrContext = new LightSampleContext(isect);

                    // Sample BSDF to get new volumetric path direction
                    Vector3f wo = isect.wo;
                    Float u = sampler.Get1D();
                    pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, u, sampler.Get2D());
                    if (!bs)
                        break;
                    // Update _beta_ and rescaled path probabilities for BSDF scattering
                    beta *= bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                    if (bs.value().pdfIsProportional)
                        r_l = r_u / bsdf.PDF(wo, bs.value().wi);
                    else
                        r_l = r_u / bs.value().pdf;
                    fneeScatterPdf = bs.value().pdfIsProportional ? bsdf.PDF(wo, bs.value().wi) : bs.value().pdf;
                    fneeRatio = -1;
                    fneeOrigin = isect.p();

                    DCHECK(IsInf(beta.y(lambda)) == false);
                    // Update volumetric integrator path state after surface scattering
                    specularBounce = bs.value().IsSpecular();
                    throughKettunen = false;
                    scatteredInMedium = false;
                    anyNonSpecularBounces |= !bs.value().IsSpecular();
                    if (bs.value().IsTransmission())
                        etaScale *= Sqr(bs.value().eta);
                    ray = isect.SpawnRay(ray, bsdf, bs.value().wi, bs.value().flags, bs.value().eta);

                    // Account for attenuated subsurface scattering, if applicable
                    BSSRDF bssrdf = isect.GetBSSRDF(ray, ref lambda, camera, scratchBuffer);
                    if (bssrdf != null && bs.value().IsTransmission())
                    {
                        // Sample BSSRDF probe segment to find exit point
                        Float uc = sampler.Get1D();
                        Point2f up = sampler.Get2D();
                        pstd.optional<BSSRDFProbeSegment> probeSeg = bssrdf.SampleSp(uc, up);
                        if (!probeSeg)
                            break;

                        // Sample random intersection along BSSRDF probe segment
                        uint64_t seed = MixBits(FloatToBits((float)sampler.Get1D()));
                        WeightedReservoirSampler<SubsurfaceInteraction> interactionSampler =
                            new WeightedReservoirSampler<SubsurfaceInteraction>(seed);
                        // Intersect BSSRDF sampling ray against the scene geometry
                        Interaction baseIntr = new Interaction(probeSeg.value().p0, ray.time, (Medium)null);
                        while (true)
                        {
                            Ray r = baseIntr.SpawnRayTo(probeSeg.value().p1);
                            if (r.d == new Vector3f(0, 0, 0))
                                break;
                            pstd.optional<ShapeIntersection> siProbe = Intersect(r, 1);
                            if (!siProbe)
                                break;
                            baseIntr = siProbe.value().intr;
                            if (siProbe.value().intr.material == isect.material)
                                interactionSampler.Add(new SubsurfaceInteraction(siProbe.value().intr), 1.0f);
                        }

                        if (interactionSampler.HasSample() == 0)
                            break;

                        // Convert probe intersection to _BSSRDFSample_
                        SubsurfaceInteraction ssi = interactionSampler.GetSample();
                        BSSRDFSample bssrdfSample =
                            bssrdf.ProbeIntersectionToSample(ssi, scratchBuffer);
                        if (!bssrdfSample.Sp || !bssrdfSample.pdf)
                            break;

                        // Update path state for subsurface scattering
                        Float pdf = interactionSampler.SampleProbability() * bssrdfSample.pdf[0];
                        beta *= bssrdfSample.Sp / pdf;
                        r_u *= bssrdfSample.pdf / bssrdfSample.pdf[0];
                        SurfaceInteraction pi = ssi;
                        pi.wo = bssrdfSample.wo;
                        prevIntrContext = new LightSampleContext(pi);
                        // Possibly regularize subsurface BSDF
                        BSDF Sw = bssrdfSample.Sw;
                        anyNonSpecularBounces = true;
                        if (regularize)
                        {
                            ++regularizedBSDFs;
                            Sw.Regularize();
                        }
                        else
                            ++totalBSDFs;

                        // Account for attenuated direct illumination subsurface scattering
                        L += SampleLd(pi, Sw, lambda, sampler, beta, r_u, depth < maxDepth);

                        // Sample ray for indirect subsurface scattering
                        Float uSw = sampler.Get1D();
                        pstd.optional<BSDFSample> bsSw = Sw.Sample_f(pi.wo, uSw, sampler.Get2D());
                        if (!bsSw)
                            break;
                        beta *= bsSw.value().f * AbsDot(bsSw.value().wi, pi.shading.n) / bsSw.value().pdf;
                        r_l = r_u / bsSw.value().pdf;
                        // Don't increment depth this time...
                        DCHECK(!IsInf(beta.y(lambda)));
                        specularBounce = bsSw.value().IsSpecular();
                        throughKettunen = false;
                        scatteredInMedium = false;
                        ray = new RayDifferential(pi.SpawnRay(bsSw.value().wi));
                    }

                    // Possibly terminate volumetric path with Russian roulette
                    if (!beta)
                        break;
                    SampledSpectrum rrBeta = beta * etaScale / r_u.Average();
                    Float uRR = sampler.Get1D();
                    if (rrBeta.MaxComponentValue() < 1 && depth > 1)
                    {
                        Float q = std.max<Float>(0, 1 - rrBeta.MaxComponentValue());
                        if (uRR < q)
                            break;
                        beta /= 1 - q;
                    }
                }
                return L;
            }
            finally
            {
                lambdaRef = lambda;
            }
        }

        // (not in pbrt-v4) Forward next event estimation of the emission of a medium (after "Line Integration for Rendering
        // Heterogeneous Emissive Volumes", Simon et al. 2017). A direction from the vertex toward the emission is
        // sampled, and the emission is integrated along the whole ray in that direction by the tracking that a path would
        // use for it: it is gathered at every collision, and the ray goes on through null collisions only. The strategy
        // and the sampling of the scattering direction find the same ray integral, so their weights differ by the
        // densities of the direction alone: (P_f + P_s) goes into the denominator, as for the other strategy.
        SampledSpectrum SampleLdFnee(Interaction intr, BSDF? bsdf, VolumeEmissionLight volumeLight, Float pmf,
                                     SampledWavelengths lambda, Sampler sampler, SampledSpectrum beta, SampledSpectrum r_p,
                                     Point2f uLight)
        {
            if (!volumeLight.SampleDirection(intr.p(), uLight, sampler.Get1D(), out Vector3f wi, out Float pdfDirection))
                return new SampledSpectrum(0.0f);

            // Evaluate BSDF or phase function for the direction
            Float scatterPDF;
            SampledSpectrum f_hat;
            Vector3f wo = intr.wo;
            if (bsdf.HasValue)
            {
                f_hat = bsdf.Value.f(wo, wi) * AbsDot(wi, intr.AsSurface().shading.n);
                scatterPDF = bsdf.Value.PDF(wo, wi);
            }
            else
            {
                CHECK(intr.IsMediumInteraction());
                PhaseFunction phase = intr.AsMedium().phase;
                f_hat = new SampledSpectrum(phase.p(wo, wi));
                scatterPDF = phase.PDF(wo, wi);
            }
            if (!f_hat)
                return new SampledSpectrum(0.0f);
            Float directionPdfs = pmf * pdfDirection + scatterPDF;

            Ray ray = new Ray(intr.SpawnRay(wi));
            SampledSpectrum L = new SampledSpectrum(0.0f), lineBeta = beta * f_hat, r_u = r_p;
            RNG rng = new RNG(Hash(sampler.Get1D()), Hash(sampler.Get1D()));
            for (int segment = 0; segment < 256; ++segment)
            {
                pstd.optional<ShapeIntersection> si = Intersect(ray);
                bool terminated = false;
                if (ray.medium != null)
                {
                    Float tMax = si ? si.value().tHit : Infinity;
                    bool emitting = ray.medium == volumeLight.Medium();
                    SampledSpectrum betaCap = lineBeta, r_uCap = r_u;
                    SampledSpectrum T_maj = SampleT_maj(ray, tMax, sampler.Get1D(), ref rng, lambda,
                        (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                        {
                            if (!betaCap || !r_uCap)
                            {
                                terminated = true;
                                return false;
                            }
                            // Add the emission at the collision
                            if (emitting && (bool)mp.Le)
                            {
                                Float pdf = sigma_maj[0] * T_maj_[0];
                                SampledSpectrum r_e = r_uCap * sigma_maj * T_maj_ / pdf;
                                if ((bool)r_e)
                                    L += betaCap * T_maj_ / pdf * (mp.emissionPerLength ? mp.Le : mp.sigma_a * mp.Le) /
                                         (directionPdfs * r_e.Average());
                            }
                            // Only null collisions let the ray go on
                            Float pAbsorb = mp.sigma_a[0] / sigma_maj[0];
                            Float pScatter = mp.sigma_s[0] / sigma_maj[0];
                            Float pNull = std.max<Float>(0, 1 - pAbsorb - pScatter);
                            Float um = sampler.Get1D();
                            if (SampleDiscrete(new Float[] { pAbsorb, pScatter, pNull }, um) != 2)
                            {
                                terminated = true;
                                return false;
                            }
                            SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                            Float pdfN = T_maj_[0] * sigma_n[0];
                            if (pdfN == 0)
                                betaCap = new SampledSpectrum(0.0f);
                            else
                            {
                                betaCap *= T_maj_ * sigma_n / pdfN;
                                r_uCap *= T_maj_ * sigma_n / pdfN;
                            }
                            return (bool)betaCap && (bool)r_uCap;
                        });
                    if (terminated || !betaCap || !r_uCap)
                        return L;
                    lineBeta = betaCap * T_maj / T_maj[0];
                    r_u = r_uCap * T_maj / T_maj[0];
                }
                // The ray ends at a surface that isn't an interface between media
                if (!si || si.value().intr.material != null)
                    break;
                ray = new Ray(si.value().intr.SpawnRay(ray.d));
            }
            return L;
        }

        SampledSpectrum SampleLd(Interaction intr, BSDF? bsdf,
                                 SampledWavelengths lambda, Sampler sampler,
                                 SampledSpectrum beta, SampledSpectrum r_p, bool volumeEmissionAllowed)
        {
            // Estimate light-sampled direct illumination at _intr_
            // Initialize _LightSampleContext_ for volumetric light sampling
            LightSampleContext ctx;
            if (bsdf.HasValue)
            {
                ctx = new LightSampleContext(intr.AsSurface());
                // Try to nudge the light sampling position to correct side of the surface
                BxDFFlags flags = bsdf.Value.Flags();
                if (IsReflective(flags) && !IsTransmissive(flags))
                    ctx.pi = new Point3fi(intr.OffsetRayOrigin(intr.wo));
                else if (IsTransmissive(flags) && !IsReflective(flags))
                    ctx.pi = new Point3fi(intr.OffsetRayOrigin(-intr.wo));
            }
            else
                ctx = new LightSampleContext(intr);

            // Sample a light source using _lightSampler_
            Float u = sampler.Get1D();
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(ctx, u);
            Point2f uLight = sampler.Get2D();
            if (!sampledLight)
                return new SampledSpectrum(0.0f);
            Light light = sampledLight.value().light;
            DCHECK(light != null && sampledLight.value().p != 0);

            // Sample a point on the light source
            // (not in pbrt-v4) The emission of a medium is sampled as a light inside the volume; a point there is only
            // reached by the other strategy while the depth allows an emission vertex, so the two stay a pair
            pstd.optional<LightLiSample> ls;
            VolumeEmissionLight volumeLight = light as VolumeEmissionLight;
            if (volumeLight != null)
            {
                if (!volumeEmissionAllowed)
                    return new SampledSpectrum(0.0f);
                if (volumeLight.UsesFnee())
                    return SampleLdFnee(intr, bsdf, volumeLight, sampledLight.value().p, lambda, sampler, beta, r_p, uLight);
                ls = volumeLight.SampleLiVolume(ctx, uLight, sampler.Get1D(), lambda);
            }
            else
                ls = light.SampleLi(ctx, uLight, lambda, true);
            if (!ls || !ls.value().L || ls.value().pdf == 0)
                return new SampledSpectrum(0.0f);
            Float p_l = sampledLight.value().p * ls.value().pdf;

            // Evaluate BSDF or phase function for light sample direction
            Float scatterPDF;
            SampledSpectrum f_hat;
            Vector3f wo = intr.wo, wi = ls.value().wi;
            if (bsdf.HasValue)
            {
                // Update _f_hat_ and _scatterPDF_ accounting for the BSDF
                f_hat = bsdf.Value.f(wo, wi) * AbsDot(wi, intr.AsSurface().shading.n);
                scatterPDF = bsdf.Value.PDF(wo, wi);
            }
            else
            {
                // Update _f_hat_ and _scatterPDF_ accounting for the phase function
                CHECK(intr.IsMediumInteraction());
                PhaseFunction phase = intr.AsMedium().phase;
                f_hat = new SampledSpectrum(phase.p(wo, wi));
                scatterPDF = phase.PDF(wo, wi);
            }
            if (!f_hat)
                return new SampledSpectrum(0.0f);

            // Declare path state variables for ray to light source
            Ray lightRay = intr.SpawnRayTo(ls.value().pLight);
            SampledSpectrum T_ray = new SampledSpectrum(1.0f), r_l = new SampledSpectrum(1.0f),
                            r_u = new SampledSpectrum(1.0f);
            RNG rng = new RNG(Hash(lightRay.o), Hash(lightRay.d));
            bool throughKettunenShadow = false;

            while (lightRay.d != new Vector3f(0, 0, 0))
            {
                // Trace ray through media to estimate transmittance
                pstd.optional<ShapeIntersection> si = Intersect(lightRay, 1 - ShadowEpsilon);
                // Handle opaque surface along ray's path
                if (si && si.value().intr.material != null)
                    return new SampledSpectrum(0.0f);

                // Update transmittance for current ray segment
                if (lightRay.medium != null)
                {
                    Float tMax = si ? si.value().tHit : (1 - ShadowEpsilon);
                    Float uT = rng.Uniform<Float>();
                    if (lightRay.medium is TextureMedium kettunenMedium && kettunenMedium.UsesKettunen() && light.Type() == LightType.Area &&
                        volumeLight == null)
                    {
                        // Unbiased ray-marching transmittance (Kettunen et al. 2021): no null-collision vertices, so
                        // the unidirectional strategy is dropped for this path (see _throughKettunen_)
                        T_ray *= kettunenMedium.KettunenTransmittance(lightRay, tMax, ref rng, lambda);
                        throughKettunenShadow = true;
                    }
                    else if (lightRay.medium is TextureMedium adaptiveMedium && adaptiveMedium.UsesAdaptiveRatio())
                    {
                        adaptiveMedium.AdaptiveRatioTracking(lightRay, tMax, uT, ref rng, lambda, ref T_ray, ref r_l,
                                                             ref r_u);
                    }
                    else
                    {
                    Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> shadowCallback =
                                    (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj,
                                     SampledSpectrum T_maj_) =>
                                    {
                                        // Update ray transmittance estimate at sampled point
                                        // Update _T_ray_ and PDFs using ratio-tracking estimator
                                        SampledSpectrum sigma_n =
                                            ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                                        Float pdf = T_maj_[0] * sigma_maj[0];
                                        if (mp.spectralTracking)
                                        {
                                            // The probability that a camera path with the throughput so far
                                            // would have had a null event here (see volpath's camera paths)
                                            SampledSpectrum history = beta * f_hat * T_ray;
                                            Float wSum = (history * (mp.sigma_a + mp.sigma_s + sigma_n)).Average();
                                            Float pNull = wSum > 0 ? (history * sigma_n).Average() / wSum : 0;
                                            T_ray *= T_maj_ * sigma_n / pdf;
                                            r_l *= T_maj_ * sigma_maj / pdf;
                                            r_u *= pNull;
                                        }
                                        else if (mp.signedNull)
                                        {
                                            SampledSpectrum sigma_n_signed = sigma_maj - mp.sigma_a - mp.sigma_s;
                                            SampledSpectrum sigma_c = mp.sigma_a + mp.sigma_s + Abs(sigma_n_signed);
                                            SampledSpectrum densityRate = mp.mixture ? mp.mixRatio : T_maj_ * sigma_maj;
                                            pdf = densityRate[0];
                                            T_ray *= T_maj_ * sigma_n_signed / pdf;
                                            r_l *= densityRate / pdf;
                                            r_u *= densityRate * SafeDiv(Abs(sigma_n_signed), sigma_c) / pdf;
                                        }
                                        else
                                        {
                                            T_ray *= T_maj_ * sigma_n / pdf;
                                            r_l *= T_maj_ * sigma_maj / pdf;
                                            r_u *= T_maj_ * sigma_n / pdf;
                                        }

                                        // Possibly terminate transmittance computation using
                                        // Russian roulette
                                        SampledSpectrum Tr = T_ray / (r_l + r_u).Average();
                                        if (Abs(Tr).MaxComponentValue() < 0.05f)
                                        {
                                            Float q = 0.75f;
                                            if (rng.Uniform<Float>() < q)
                                                T_ray = new SampledSpectrum(0.0f);
                                            else
                                                T_ray /= 1 - q;
                                        }

                                        if (!T_ray)
                                            return false;
                                        return true;
                                    };
                    if (lightRay.medium is TextureMedium mixtureMedium && mixtureMedium.UsesMixture())
                    {
                        SampledSpectrum T_I = mixtureMedium.SampleT_majMixture(lightRay, tMax, uT, ref rng, lambda,
                                                                               shadowCallback, out SampledSpectrum tail);
                        T_ray *= T_I / tail[0];
                        r_l *= tail / tail[0];
                        r_u *= tail / tail[0];
                    }
                    else
                    {
                        SampledSpectrum T_maj = SampleT_maj(lightRay, tMax, uT, ref rng, lambda, shadowCallback);
                        // Update transmittance estimate for final segment
                        T_ray *= T_maj / T_maj[0];
                        r_l *= T_maj / T_maj[0];
                        r_u *= T_maj / T_maj[0];
                    }
                    }
                }

                // Generate next ray segment or return final transmittance
                if (!T_ray)
                    return new SampledSpectrum(0.0f);
                if (!si)
                    break;
                lightRay = si.value().intr.SpawnRayTo(ls.value().pLight);
            }
            // (not in pbrt-v4) The point has to lie in the medium that emits, and the other strategy reaches it with the
            // density of a collision there: the majorant at the point, per length
            if (volumeLight != null)
            {
                if (lightRay.medium != volumeLight.Medium())
                    return new SampledSpectrum(0.0f);
                r_u *= volumeLight.Medium().MajorantAt(ls.value().pLight.p(), lambda);
            }
            // Return path contribution function estimate for direct lighting
            r_l *= r_p * p_l;
            r_u *= r_p * scatterPDF;
            if (IsDeltaLight(light.Type()) || throughKettunenShadow)
                return beta * f_hat * T_ray * ls.value().L / r_l.Average();
            else
                return beta * f_hat * T_ray * ls.value().L / (r_l + r_u).Average();
        }

        public override string ToString()
        {
            return StringPrintf(
                "[ VolPathIntegrator maxDepth: %d lightSampler: %s regularize: %s ]", maxDepth,
                lightSampler, regularize);
        }

        public static VolPathIntegrator Create(
            ParameterDictionary parameters, Camera camera, Sampler sampler,
            Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            string lightStrategy = parameters.GetOneString("lightsampler", "bvh");
            bool regularize = parameters.GetOneBool("regularize", false);
            return new VolPathIntegrator(maxDepth, camera, sampler, aggregate,
                                         lights, lightStrategy, regularize);
        }

        // VolPathIntegrator Private Members
        int maxDepth;
        LightSampler lightSampler;
        bool regularize;
    }

}  // namespace pbrt
