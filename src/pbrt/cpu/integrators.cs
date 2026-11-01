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
using PLight = pbrt.PolarisedLightSystem.Light;
using PAttenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{

    // Integrator Definition
    public abstract unsafe class Integrator
    {
        // (not in pbrt-v4) Whether the camera subpaths of bdpt gather the emission of media at their collisions (a strategy with
        // s = 0 whose last vertex is in the emissive medium), next to the strategies that sample the medium as a light
        public bool gatherMediumEmission = false;
        // (not in pbrt-v4) The strategies of bdpt that are used: all of them, or those of a unidirectional path tracer (the
        // camera subpath's own hits of emitters, s = 0, and the light samples at its vertices, s = 1, with t >= 2)
        public bool unidirectionalStrategies = false;
        // (not in pbrt-v4) Equiangular sampling of the scattering points of the camera subpaths of bdpt (see BDPT.EquiangularTerm): whether
        // it is on, and the maximum depth of the paths
        public bool bdptEquiangular = false;
        public int bdptMaxDepth = 0;
        // (Fong et al., 4.1) The number of equiangular samples of a segment, for the scatterings before the given depth
        public int bdptEquiangularSamples = 1;
        // The light sampler of the equiangular strategy, over the lights it takes part with
        public LightSampler bdptEquiangularLightSampler;
        public int bdptEquiangularMultipleDepth = int.MaxValue;

        public int EquiangularCount(int depth)
        {
            return depth < bdptEquiangularMultipleDepth ? bdptEquiangularSamples : 1;
        }

        public bool StrategyAllowed(int s, int t)
        {
            if (!unidirectionalStrategies)
            {
                return true;
            }
            return s <= 1 && t >= 2;
        }

        public bool Unoccluded(Interaction p0, Interaction p1)
        {
            if (!ThinCoatMaterial.anyInScene)
                return !IntersectP(p0.SpawnRayTo(p1), 1 - ShadowEpsilon);
            // (not in pbrt-v4) Thin coats stop only some of the shadow rays
            Ray ray = p0.SpawnRayTo(p1);
            while (true)
            {
                pstd.optional<ShapeIntersection> si = Intersect(ray, 1 - ShadowEpsilon);
                if (!si)
                    return true;
                if (ThinCoatMaterial.Blocks(si.value().intr, ray.d))
                    return false;
                ray = si.value().intr.SpawnRayTo(p1);
            }
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

        // (not in pbrt-v4) Russian roulette for the subpaths of BDPT and for the transmittance of its connections: the walk
        // stops with a probability that grows as its throughput drops below 1, and the transmittance estimate that has dropped
        // below 0.05 survives with probability 0.25 (as in the shadow rays of the volumetric path integrator)
        public bool russianRoulette = false;

        // (not in pbrt-v4) The context for sampling a light from a vertex: at a surface the point moves to the side of it that the
        // BSDF scatters to (shared by volpath's and bdpt's light samples)
        public static LightSampleContext LightContextAt(Interaction intr, bool onSurface, BxDFFlags flags)
        {
            if (!onSurface)
            {
                return new LightSampleContext(intr);
            }
            LightSampleContext ctx = new LightSampleContext(intr.AsSurface());
            // Try to nudge the light sampling position to correct side of the surface
            if (IsReflective(flags) && !IsTransmissive(flags))
            {
                ctx.pi = new Point3fi(intr.OffsetRayOrigin(intr.wo));
            }
            else if (IsTransmissive(flags) && !IsReflective(flags))
            {
                ctx.pi = new Point3fi(intr.OffsetRayOrigin(-intr.wo));
            }
            return ctx;
        }

        // (not in pbrt-v4) The transmittance of a connection with the factors of the spectral MIS weights, as the shadow rays of
        // volpath have them: the estimate is not divided by anything; ru is the ratio, per wavelength, of the probability of
        // the segment as a walk would have sampled it to the probability that the connection samples it with for the hero
        // wavelength, and rl the same for the connection (volpath's r_u and r_l after the shadow ray)
        public SampledSpectrum TrSegments(Interaction p0, Interaction p1, BoundaryStack stack, SampledWavelengths lambda,
                                          out SampledSpectrum ru, out SampledSpectrum rl, out Medium endMedium)
        {
            endMedium = null;
            RNG rng = new RNG(Hash(p0.p()), Hash(p1.p()));
            Ray ray = p0.SpawnRayTo(p1);
            if (LengthSquared(ray.d) == 0)
            {
                ru = new SampledSpectrum(1.0f);
                rl = new SampledSpectrum(1.0f);
                endMedium = MediumTracking.Current(stack);
                return new SampledSpectrum(1.0f);
            }
            SampledSpectrum T_ray = ShadowRay(ray, p0, p1, ref stack, lambda, rng, russianRoulette, out ru, out rl);
            if ((bool)T_ray)
            {
                endMedium = MediumTracking.Current(stack);
            }
            return T_ray;
        }

        // (not in pbrt-v4) The shadow ray of volpath and bdpt: the transmittance estimate from the origin of the ray to the target
        // through the media and the boundaries between them, and the factors r_u and r_l that the null collisions of the
        // estimator give the probability of the segment (as a walk, r_u, and as this connection, r_l, would sample it, relative
        // to the hero wavelength's of the connection). ray starts at origin and ends at target (t = 1); stack is the stack of media
        // on arrival at origin, and ends as the stack at the target. A blocked ray returns zero.
        public SampledSpectrum ShadowRay(Ray lightRay, Interaction origin, Interaction target, ref BoundaryStack stack,
                                         SampledWavelengths lambda, RNG rng, bool roulette,
                                         out SampledSpectrum r_u, out SampledSpectrum r_l)
        {
            MediumTracking.CrossSurface(ref stack, origin, lightRay.d);
            SampledSpectrum T_ray = new SampledSpectrum(1.0f);
            SampledSpectrum rl = new SampledSpectrum(1.0f), ru = new SampledSpectrum(1.0f);

            while (lightRay.d != new Vector3f(0, 0, 0))
            {
                // Trace ray through media to estimate transmittance
                pstd.optional<ShapeIntersection> si = Intersect(lightRay, 1 - ShadowEpsilon);
                // Handle opaque surface along ray's path
                if (si && ThinCoatMaterial.Blocks(si.value().intr, lightRay.d))
                {
                    r_l = rl;
                    r_u = ru;
                    return new SampledSpectrum(0.0f);
                }

                // Update transmittance for current ray segment
                Medium lightMedium = MediumTracking.Current(stack);
                // (not in pbrt-v4) A straight ray through a refractive medium is not a light path: the connection is blocked (volpath
                // connects from inside such a medium with a curved path instead, see RefractiveMedium.ConnectToPoint)
                if (lightMedium is RefractiveMedium)
                {
                    r_l = rl;
                    r_u = ru;
                    return new SampledSpectrum(0.0f);
                }
                if (lightMedium != null)
                {
                    Float tMax = si ? si.value().tHit : (1 - ShadowEpsilon);
                    Float uT = rng.Uniform<Float>();
                    SampledSpectrum T_closed;
                    if (lightMedium.volumeIntegrator.transmittance.TryClosedForm(lightRay, lightMedium, tMax, lambda, out T_closed))
                    {
                        T_ray *= T_closed;
                    }
                    else if (lightMedium.volumeIntegrator.transmittance.TryEstimate(lightRay, lightMedium, tMax, ref rng, lambda, ref T_ray, ref rl, ref ru))
                    {
                    }
                    else
                    {
                        SampledSpectrum T_maj =
                            lightMedium.volumeIntegrator.tracker.SampleT_maj(lightRay, lightMedium, tMax, uT, ref rng, lambda,
                                        (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj,
                                         SampledSpectrum T_maj_) =>
                                        {
                                            // Update ray transmittance estimate at sampled point
                                            // Update _T_ray_ and PDFs using ratio-tracking estimator
                                            SampledSpectrum sigma_n =
                                                ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                                            lightMedium.volumeIntegrator.transmittance.Step(ref T_ray, ref rl, ref ru, T_maj_, sigma_maj, sigma_n, ref rng);

                                            // Possibly terminate transmittance computation using
                                            // Russian roulette
                                            if (roulette)
                                            {
                                                SampledSpectrum Tr = T_ray / (rl + ru).Average();
                                                if (Tr.MaxComponentValue() < 0.05f)
                                                {
                                                    Float q = 0.75f;
                                                    if (rng.Uniform<Float>() < q)
                                                        T_ray = new SampledSpectrum(0.0f);
                                                    else
                                                        T_ray /= 1 - q;
                                                }
                                            }

                                            if (!T_ray)
                                                return false;
                                            return true;
                                        });
                        // Update transmittance estimate for final segment
                        lightMedium.volumeIntegrator.transmittance.Finish(ref T_ray, ref rl, ref ru, T_maj);
                    }
                }

                // Generate next ray segment or return final transmittance
                if (!T_ray)
                {
                    r_l = rl;
                    r_u = ru;
                    return new SampledSpectrum(0.0f);
                }
                if (!si)
                {
                    break;
                }
                lightRay = si.value().intr.SpawnRayTo(target);
                MediumTracking.CrossSurface(ref stack, si.value().intr, lightRay.d);
            }
            r_l = rl;
            r_u = ru;
            return T_ray;
        }

        // Integrator Public Methods
        public static Integrator Create(string name, ParameterDictionary parameters, Camera camera,
                                        Sampler sampler, Primitive aggregate, List<Light> lights,
                                        RGBColorSpace colorSpace, FileLoc loc)
        {
            Integrator integrator = null;
            if (name == "path")
                integrator = PathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
            else if (name == "polpath")
                integrator = PolarisedPathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
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

                // Update start and end wave
                waveStart = waveEnd;
                waveEnd = std.min(spp, waveEnd + nextWaveSize);
                if (referenceImage == null)
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
            camera.GetFilm().AddSample(pPixel, L, lambda, visibleSurface,
                                       cameraSample.filterWeight);
        }

        public abstract SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda,
                                           Sampler sampler, ScratchBuffer scratchBuffer,
                                           VisibleSurface visibleSurface);
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
            MediumTracking.RequireNoPerEntryVolumes("lightpath");
            MediumTracking.RequireNoRefractive("lightpath");
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
                BoundaryStack stack = MediumTracking.Start(camera.GetMedium());

                while (true)
                {
                    // Estimate radiance for ray path using delta tracking
                    pstd.optional<ShapeIntersection> si = Intersect(new Ray(ray));
                    bool scattered = false, terminated = false;
                    Medium rayMedium = MediumTracking.Current(stack);
                    if (rayMedium != null)
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
                        rayMedium.volumeIntegrator.tracker.SampleT_maj(new Ray(ray), rayMedium, tMax, u, ref rng, lambda,
                                    (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj,
                                     SampledSpectrum T_maj) =>
                                    {
                                        // Compute medium event probabilities for interaction

                                        // Randomly sample medium scattering event for delta tracking
                                        MediumEventType mode = MediumEvents.SampleEvent(mp, sigma_maj, uMode);
                                        if (mode == MediumEventType.Absorb)
                                        {
                                            // Handle absorption event for medium sample
                                            Lcap += betaCap * mp.Le;
                                            terminatedCap = true;
                                            return false;
                                        }
                                        else if (mode == MediumEventType.Scatter)
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
                                            pstd.optional<BSDFSample> ps =
                                                new BSDF(mp.phase).Sample_f(-rayCap.d, 0.0f, u2);
                                            if (!ps)
                                            {
                                                terminatedCap = true;
                                                return false;
                                            }

                                            // Update state for recursive evaluation of $L_\roman{i}$
                                            betaCap *= ps.value().f[0] / ps.value().pdf;
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
                    {
                        sIntr.SkipIntersection(ref ray, si.value().tHit);
                        MediumTracking.CrossSurface(ref stack, sIntr, ray.d);
                    }
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

        public EndpointInteraction(Camera camera, Ray ray) : base(ray.o, ray.time, (Medium)null)
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

        public EndpointInteraction(Light light, Ray r) : base(r.o, r.time, (Medium)null)
        {
            this.light = light;
        }

        public EndpointInteraction(Ray ray)
            : base(ray.o + ray.d, new Normal3f(-ray.d.x, -ray.d.y, -ray.d.z), ray.time, (Medium)null)
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
        // (not in pbrt-v4) The polarisation of the throughput on arrival at the vertex: a Mueller matrix from the camera for
        // a camera subpath, the Stokes vector of the light that has come from the light for a light subpath. Their first
        // elements are beta.
        public PAttenuation pbeta;
        public PLight plight;
        // (not in pbrt-v4) The media the path is inside of on arrival at the vertex; pbrt-v4 stores the medium in the Ray
        public BoundaryStack stack;
        // (not in pbrt-v4) A vertex in an emissive medium: the emission per length at the point, and the extinction of the hero
        // wavelength there (the density of a collision at the point, per length, that the MIS weights use)
        public SampledSpectrum emission;
        public Float sigmaT0 = 0;
        // (not in pbrt-v4) The null collisions of the segment that arrives at the vertex along its subpath, and the scattering
        // coefficient at the vertex, for the spectral MIS weights (see PathState.segU): segU and segL are the ratios of the
        // probability of the segment as a walk samples it (segU) and as a connection samples it (segL), per wavelength, to the
        // probability that the hero wavelength's walk had; vtx the ratio of the scattering coefficients; cumU the product of
        // segU along the subpath up to the vertex (volpath's r_u); emissionMaj, for a vertex in an emissive medium, the
        // ratio of the majorants of the wavelengths to the hero's, which a walk that collides there has as its density
        public SampledSpectrum segU = new SampledSpectrum(1.0f), segL = new SampledSpectrum(1.0f), vtx = new SampledSpectrum(1.0f);
        public SampledSpectrum cumU = new SampledSpectrum(1.0f), emissionMaj = new SampledSpectrum(1.0f);
        // (not in pbrt-v4) The scattering coefficient of the hero wavelength at a vertex in a medium
        public Float sigmaS0 = 0;

        // Vertex Public Methods
        public Vertex() { ei = new EndpointInteraction(); }

        public Vertex(VertexType type, EndpointInteraction ei, SampledSpectrum beta)
        {
            this.type = type; this.beta = beta; this.ei = ei;
            pbeta = new PAttenuation(beta); plight = new PLight(beta);
        }

        public Vertex(SurfaceInteraction si, BSDF bsdf, SampledSpectrum beta)
        {
            this.type = VertexType.Surface; this.beta = beta; this.si = si; this.bsdf = bsdf;
            pbeta = new PAttenuation(beta); plight = new PLight(beta);
        }

        public Vertex(MediumInteraction mi, SampledSpectrum beta)
        {
            this.type = VertexType.Medium; this.beta = beta; this.mi = mi; this.bsdf = mi.GetBSDF();
            pbeta = new PAttenuation(beta); plight = new PLight(beta);
        }

        // (C++ copies Vertex objects by value with memcpy)
        public void Assign(Vertex v)
        {
            type = v.type; beta = v.beta; ei = v.ei; mi = v.mi; si = v.si; bsdf = v.bsdf;
            delta = v.delta; pdfFwd = v.pdfFwd; pdfRev = v.pdfRev; stack = v.stack; pbeta = v.pbeta; plight = v.plight;
            emission = v.emission; sigmaT0 = v.sigmaT0;
            segU = v.segU; segL = v.segL; vtx = v.vtx; cumU = v.cumU; emissionMaj = v.emissionMaj; sigmaS0 = v.sigmaS0;
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
                return bsdf.f(mi.wo, wi, mode);
            case VertexType.Light:
                // (not in pbrt-v4) The emission of a volume is the same in all directions
                if (ei.light is VolumeEmissionLight)
                    return new SampledSpectrum(1.0f);
                goto default;
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
            // (not in pbrt-v4) The emission of a medium is the same in all directions
            else if (type == VertexType.Light && ei.light is VolumeEmissionLight)
                return emission;
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
            return pdf * invDist2;
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
                pdf = bsdf.PDF(wp, wn);
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

    // (not in pbrt-v4) The emission of a medium that a camera subpath gathers at a collision: the vertex that ends the path and the
    // index that it has in the camera subpath (the vertices before it are the vertices of the subpath with lower indices)
    public sealed class MediumEmissionEnd
    {
        public MediumEmissionEnd(Vertex vertex, int index)
        {
            this.vertex = vertex;
            this.index = index;
        }

        public Vertex vertex;
        public int index;
    }

    // (not in pbrt-v4) What the camera subpaths of bdpt add by equiangular sampling of their scattering points (see BDPT.EquiangularTerm)
    public sealed class BDPTEquiangularSink
    {
        public BDPTEquiangularSink(int maxDepth)
        {
            this.maxDepth = maxDepth;
        }

        public SampledSpectrum L = new SampledSpectrum(0.0f);
        public int maxDepth;
    }

    // BDPT Utility Functions
    public static unsafe class BDPT
    {
        // (not in pbrt-v4) A light vertex inside an emissive volume
        public static bool IsVolumeEmission(Vertex v)
        {
            return v != null && v.type == VertexType.Light && v.ei.light is VolumeEmissionLight;
        }

        // (not in pbrt-v4) The media that a path is in at p, from the boundaries between the camera and p. Opaque surfaces are
        // passed like the interfaces, since the stack follows the geometry.
        public static BoundaryStack StackAtPoint(Integrator integrator, Camera camera, Point3f p, Float time)
        {
            Point3f o = camera.GetCameraTransform().RenderFromCamera(new Point3f(0, 0, 0), time);
            BoundaryStack stack = MediumTracking.Start(camera.GetMedium());
            Ray ray = new Ray(o, p - o, time);
            Interaction target = new Interaction(p, time, (Medium)null);
            for (int i = 0; i < 256 && LengthSquared(ray.d) > 0; ++i)
            {
                pstd.optional<ShapeIntersection> si = integrator.Intersect(ray, 1 - ShadowEpsilon);
                if (!si)
                    break;
                ray = si.value().intr.SpawnRayTo(target);
                MediumTracking.CrossSurface(ref stack, si.value().intr, ray.d);
            }
            return stack;
        }

        public static int BufferIndex(int s, int t)
        {
            int above = s + t - 2;
            return s + above * (5 + above) / 2;
        }

        public static int GenerateCameraSubpath(Integrator integrator, RayDifferential ray,
                                                ref SampledWavelengths lambda, Sampler sampler,
                                                ScratchBuffer scratchBuffer, int maxDepth, Camera camera,
                                                Vertex[] path, bool regularize, bool polarised = false,
                                                List<MediumEmissionEnd> emissionEnds = null,
                                                BDPTEquiangularSink equiangularSink = null)
        {
            if (maxDepth == 0)
                return 0;
            SampledSpectrum beta = new SampledSpectrum(1.0f);
            // Generate first vertex on camera subpath and start random walk
            Float pdfPos, pdfDir;
            BoundaryStack stack = MediumTracking.Start(camera.GetMedium());
            path[0] = Vertex.CreateCamera(camera, new Ray(ray), beta);
            path[0].stack = stack;
            camera.PDF_We(new Ray(ray), &pdfPos, &pdfDir);
            return RandomWalk(integrator, ref lambda, ray, stack, sampler, camera, scratchBuffer, beta, pdfDir,
                              maxDepth - 1, TransportMode.Radiance, path, 1, regularize, polarised, emissionEnds,
                              equiangularSink) + 1;
        }

        public static int GenerateLightSubpath(Integrator integrator, ref SampledWavelengths lambda,
                                               Sampler sampler, Camera camera, ScratchBuffer scratchBuffer,
                                               int maxDepth, Float time, LightSampler lightSampler,
                                               Vertex[] path, bool regularize, bool polarised = false)
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
            BoundaryStack stack = default;
            if (volumeLight != null)
            {
                // The point only emits if the surfaces around it say that it is in the medium. A sample that misses is a light
                // subpath without throughput and not the absence of one: the s = 1 strategies take samples of their own, and
                // only run if the light subpath has a vertex.
                bool valid = les && les.value().pdfPos != 0 && les.value().pdfDir != 0 && (bool)les.value().L;
                if (valid)
                {
                    stack = StackAtPoint(integrator, camera, les.value().ray.o, time);
                    valid = MediumTracking.Current(stack) == volumeLight.Medium();
                }
                if (!valid)
                {
                    path[0] = Vertex.CreateLight(light, new Ray(new Point3f(0, 0, 0), new Vector3f(0, 0, 1), time),
                                                 new SampledSpectrum(0.0f), lightSamplePDF);
                    path[0].stack = stack;
                    return 1;
                }
            }
            else
            {
                if (!les || les.value().pdfPos == 0 || les.value().pdfDir == 0 || !les.value().L)
                    return 0;
                stack = MediumTracking.Start(les.value().medium);
            }
            RayDifferential ray = new RayDifferential(les.value().ray);

            // Generate first vertex of light subpath
            Float p_l = lightSamplePDF * les.value().pdfPos;
            path[0] = les.value().intr ? Vertex.CreateLight(light, les.value().intr.value(), les.value().L, p_l)
                                       : Vertex.CreateLight(light, new Ray(ray), les.value().L, p_l);

            // (not in pbrt-v4) The extinction at the point of an emissive medium, for the weight of a camera subpath that gathers it
            if (path[0].ei.light is VolumeEmissionLight emissiveMedium)
            {
                SampledSpectrum majorant = emissiveMedium.Medium().MajorantAt(ray.o, lambda);
                path[0].sigmaT0 = majorant[0];
                path[0].emissionMaj = majorant / majorant[0];
            }

            // Follow light subpath random walk
            SampledSpectrum beta = les.value().L * les.value().AbsCosTheta(ray.d) / (p_l * les.value().pdfDir);
            path[0].stack = stack;
            int nVertices = RandomWalk(integrator, ref lambda, ray, stack, sampler, camera, scratchBuffer, beta,
                                       les.value().pdfDir, maxDepth - 1, TransportMode.Importance, path, 1,
                                       regularize, polarised);
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
                                     BoundaryStack stack, Sampler sampler, Camera camera, ScratchBuffer scratchBuffer,
                                     SampledSpectrum beta, Float pdf, int maxDepth, TransportMode mode,
                                     Vertex[] path, int off, bool regularize, bool polarised = false,
                                     List<MediumEmissionEnd> emissionEnds = null,
                                     BDPTEquiangularSink equiangularSink = null)
        {
            if (maxDepth == 0)
                return 0;
            // (not in pbrt-v4) The polarisation of the throughput: pa for a camera subpath (Mueller matrix, applied to
            // what comes later), pl for a light subpath (Stokes vector). Each takes every factor of beta.
            bool fromCamera = mode == TransportMode.Radiance;
            WalkContext ctx = new WalkContext();
            ctx.integrator = integrator;
            ctx.lambda = lambdaRef;
            ctx.ray = ray;
            ctx.stack = stack;
            ctx.sampler = sampler;
            ctx.camera = camera;
            ctx.scratchBuffer = scratchBuffer;
            ctx.mode = mode;
            ctx.st = new PathState(beta, new PAttenuation(beta), new PLight(beta), fromCamera, polarised, false);
            ctx.st.trackSegment = true;
            BDPTWalkHook hook = new BDPTWalkHook(path, off, maxDepth, pdf, regularize, emissionEnds, equiangularSink);
            try
            {
                // Follow random walk to initialize BDPT path vertices
                PathWalk.Run(ctx, hook);
                return hook.bounces;
            }
            finally
            {
                lambdaRef = ctx.lambda;
            }
        }

        // The vertices of a bdpt subpath, with their forward and reverse densities
        sealed class BDPTWalkHook : PathWalkHook
        {
            public BDPTWalkHook(Vertex[] path, int off, int maxDepth, Float pdfFwd, bool regularize,
                                List<MediumEmissionEnd> emissionEnds = null, BDPTEquiangularSink equiangularSink = null)
            {
                this.emissionEnds = emissionEnds;
                this.equiangularSink = equiangularSink;
                this.path = path;
                this.off = off;
                this.maxDepth = maxDepth;
                this.pdfFwd = pdfFwd;
                this.regularize = regularize;
            }

            public int bounces = 0;
            Vertex[] path;
            int off, maxDepth, vIdx;
            Float pdfFwd;
            bool regularize;
            bool anyNonSpecularBounces = false;
            List<MediumEmissionEnd> emissionEnds;
            // (not in pbrt-v4) Equiangular sampling of the camera subpath (see SampleEquiangularVertex); the number of pieces of the
            // segment so far (the walk passes the boundaries between media without a vertex)
            BDPTEquiangularSink equiangularSink;
            int segmentPieces = 0;

            // The collisions of the segment that arrives at the new vertex go to the vertex, and the next segment starts empty
            void StoreSegment(WalkContext ctx, int index)
            {
                path[index].segU = ctx.st.segU;
                path[index].segL = ctx.st.segL;
                path[index].vtx = ctx.st.vtx;
                path[index].cumU = path[index - 1].cumU * ctx.st.segU;
                ctx.st.ResetSegment();
                segmentPieces = 0;
            }

            public override void BeginSegment(WalkContext ctx, Medium medium, Float tMax)
            {
                ++segmentPieces;
                // Only the first piece of a segment (the one that starts at the vertex): the strategy and its weights are defined for it
                if (equiangularSink == null || ctx.mode != TransportMode.Radiance || segmentPieces != 1 || !(bool)ctx.st.beta ||
                    vIdx > equiangularSink.maxDepth)
                    return;
                int count = ctx.integrator.EquiangularCount(vIdx);
                for (int i = 0; i < count; ++i)
                {
                    equiangularSink.L += SampleEquiangularVertex(ctx, medium, tMax, count);
                }
            }

            // (not in pbrt-v4) The contribution of the path that has a scattering point on the segment that the walk is about to sample,
            // found by choosing a light, a point on it and then the distance along the segment by the angle seen from that point
            // (Fong et al., 4.2.2 and 4.2.3), and connected to the point. Its weight is that of volpath's SampleEquiangular
            // (the sum of the strategies that find the same path, relative to this one), with the strategies of bdpt next to those
            // of the walk: MISSum of the path with the scattering point as the last vertex of the camera subpath.
            SampledSpectrum SampleEquiangularVertex(WalkContext ctx, Medium medium, Float tMax, int count)
            {
                SampledSpectrum zero = new SampledSpectrum(0.0f);
                BDPTIntegrator bdpt = (BDPTIntegrator)ctx.integrator;
                LightSampler lightSampler = bdpt.BDPTLightSampler;
                SampledWavelengths lambda = ctx.lambda;
                Sampler sampler = ctx.sampler;
                Float uLightChoice = sampler.Get1D();
                Float uDistance = sampler.Get1D();
                Point2f uLight = sampler.Get2D();
                pstd.optional<SampledLight> sampledLight = ctx.integrator.bdptEquiangularLightSampler.Sample(uLightChoice);
                if (!sampledLight)
                    return zero;
                Light light = sampledLight.value().light;
                DiffuseAreaLight areaLight = light as DiffuseAreaLight;
                Float length = Length(ctx.ray.d);
                Point3f origin = ctx.ray.o;
                Vector3f direction = ctx.ray.d / length;
                Float segmentLength = tMax * length;
                LightSampleContext segmentContext = new LightSampleContext(new Point3fi(origin), new Normal3f(0, 0, 0),
                                                                            new Normal3f(0, 0, 0));
                // The point on the light that the distance is chosen from
                Point3f pLight;
                Interaction yIntr = default;
                Float pdfArea = 1;
                if (areaLight != null)
                {
                    if (!areaLight.SampleArea(uLight, out yIntr, out pdfArea))
                        return zero;
                    pLight = yIntr.p();
                }
                else
                {
                    pstd.optional<LightLiSample> lsOrigin = light.SampleLi(segmentContext, uLight, lambda, true);
                    if (!lsOrigin)
                        return zero;
                    pLight = lsOrigin.value().pLight.p();
                }
                if (!EquiangularSampling.Sample(origin, direction, segmentLength, pLight, uDistance, out Float t, out Float pdfDistance))
                    return zero;
                Point3f p = origin + t * direction;

                // The transmittance to the point, with the null collisions of the walk's tracker
                SampledSpectrum T_seg = new SampledSpectrum(1.0f), rl_seg = new SampledSpectrum(1.0f), ru_seg = new SampledSpectrum(1.0f);
                RNG rng = new RNG(Hash(origin, t), Hash(direction));
                Float uTrack = rng.Uniform<Float>();
                SampledSpectrum T_maj = medium.volumeIntegrator.tracker.SampleT_maj(
                    new Ray(origin, direction, ctx.ray.time), medium, t, uTrack, ref rng, lambda,
                    (Point3f pp, MediumProperties mpp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                    {
                        SampledSpectrum sigma_n = ClampZero(sigma_maj - mpp.sigma_a - mpp.sigma_s);
                        MediumEvents.RatioTrackingStep(ref T_seg, ref rl_seg, ref ru_seg, T_maj_, sigma_maj, sigma_n);
                        return (bool)T_seg;
                    });
                if (!T_seg)
                    return zero;
                MediumEvents.FinishSegment(ref T_seg, ref rl_seg, ref ru_seg, T_maj);
                if (!VolPathIntegrator.MajorantCovers(medium, new Ray(origin, direction, ctx.ray.time), segmentLength, t, lambda))
                    return zero;

                // The scattering at the point
                MediumProperties mp = medium.SamplePoint(p, lambda);
                if (!mp.sigma_s || !(mp.sigma_s[0] > 0))
                    return zero;
                MediumInteraction intr = new MediumInteraction(p, -direction, ctx.ray.time, medium, mp.phase);
                Vertex x = Vertex.CreateMedium(intr, ctx.st.beta, pdfFwd, path[vIdx - 1]);
                x.sigmaS0 = mp.sigma_s[0];
                x.segU = ru_seg;
                x.segL = rl_seg;
                x.vtx = mp.sigma_s / mp.sigma_s[0];
                x.cumU = path[vIdx - 1].cumU * ru_seg;
                x.stack = ctx.stack;

                // The light at the point
                LightSampleContext lightContext = Integrator.LightContextAt(intr, false, default);
                SampledSpectrum radiance;
                Interaction target;
                if (areaLight != null)
                {
                    Vector3f d = yIntr.p() - p;
                    Float r2 = LengthSquared(d);
                    if (r2 == 0)
                        return zero;
                    Vector3f wi = d / MathF.Sqrt(r2);
                    Float cosY = AbsDot(yIntr.n, wi);
                    if (cosY == 0)
                        return zero;
                    SampledSpectrum Lemit = areaLight.L(yIntr.p(), yIntr.n, yIntr.uv, -wi, lambda);
                    if (!Lemit)
                        return zero;
                    radiance = Lemit * (cosY / r2);
                    target = yIntr;
                }
                else
                {
                    pstd.optional<LightLiSample> ls = light.SampleLi(lightContext, uLight, lambda, true);
                    if (!ls || !ls.value().L || ls.value().pdf == 0)
                        return zero;
                    radiance = ls.value().L;
                    target = ls.value().pLight;
                    pdfArea = 1;
                }
                EndpointInteraction ei = new EndpointInteraction(light, target);
                Vertex yVertex = Vertex.CreateLight(ei, new SampledSpectrum(1.0f), 0);
                yVertex.pdfFwd = yVertex.PDFLightOrigin(ctx.integrator.infiniteLights, x, lightSampler);
                SampledSpectrum f_hat = x.f(yVertex, TransportMode.Radiance);
                if (!f_hat)
                    return zero;
                SampledSpectrum T_conn = ctx.integrator.TrSegments(intr, target, ctx.stack, lambda, out SampledSpectrum ru_conn,
                                                                    out SampledSpectrum rl_conn, out Medium endMedium);
                if (!T_conn)
                    return zero;

                // The weight: the ways to find the path, relative to the hero wavelength's connection
                path[vIdx] = x;
                Vertex[] lightVertices = new Vertex[] { new Vertex() };
                if (!MISSum(ctx.integrator, ctx.camera, lightVertices, path, yVertex, 1, vIdx + 1, lightSampler, ru_conn, rl_conn,
                            false, out SampledSpectrum sum))
                    return zero;
                SampledSpectrum r_u0 = path[vIdx - 1].cumU;
                for (int i = 1; i < vIdx; ++i)
                    r_u0 *= path[i].vtx;
                SampledSpectrum equiangular = r_u0 * rl_seg * rl_conn * (count * pdfDistance);
                // (the light is chosen with its probability among the lights of the strategy, and by the light sampler of the
                // strategies of bdpt for the connection)
                Float pmfConnection = lightSampler.PMF(light);
                SampledSpectrum denominator = (equiangular * sampledLight.value().p + sum * (mp.sigma_s[0] * pmfConnection)) * pdfArea;
                Float average = denominator.Average();
                if (!(average > 0))
                    return zero;
                return ctx.st.beta * T_seg * mp.sigma_s * f_hat * T_conn * radiance / average;
            }

            public override bool BeginIteration(WalkContext ctx)
            {
                // Attempt to create the next subpath vertex in _path_
                if (!ctx.st.beta)
                    return false;
                // (not in pbrt-v4) Possibly terminate the subpath with Russian roulette. The vertices keep their densities:
                // the roulette changes the estimate and not the density that the MIS weights are made for.
                if (ctx.integrator.russianRoulette && bounces > 1 && ctx.st.beta.MaxComponentValue() < 1)
                {
                    Float q = std.max<Float>(0, 1 - ctx.st.beta.MaxComponentValue());
                    if (ctx.sampler.Get1D() < q)
                        return false;
                    ctx.st.SurviveRoulette(q);
                }
                vIdx = off + bounces;
                return true;
            }

            public override bool BeginCollision(WalkContext ctx, Medium medium, Point3f p, MediumProperties mp,
                                                SampledSpectrum sigma_maj, SampledSpectrum T_maj)
            {
                // (not in pbrt-v4) A camera subpath gathers the emission of the medium at each of its collisions, like volpath does:
                // the collision is the last vertex of a path with no light subpath, and the strategies that sample the medium as a
                // light share the weight (see MISWeight). The emission end is kept next to the subpath: the walk goes on.
                if (emissionEnds != null && ctx.mode == TransportMode.Radiance && (bool)mp.Le && (bool)ctx.st.beta)
                {
                    VolumeEmissionLight emissionLight = (medium as TextureMedium)?.EmissionLight();
                    if (emissionLight != null)
                    {
                        Float pdfCollision = sigma_maj[0] * T_maj[0];
                        SampledSpectrum w = T_maj / pdfCollision;
                        Vertex end = Vertex.CreateLight(emissionLight, new Interaction(p, ctx.ray.time, medium),
                                                        ctx.st.beta * w, 0);
                        end.emission = mp.sigma_a * mp.Le;
                        // (the density of a collision at the point is the majorant's: every collision gathers, null or not)
                        end.sigmaT0 = sigma_maj[0];
                        end.emissionMaj = sigma_maj / sigma_maj[0];
                        // The null collisions on the way, and the distance to this collision
                        end.segU = ctx.st.segU * T_maj / T_maj[0];
                        end.segL = ctx.st.segL * T_maj / T_maj[0];
                        end.cumU = path[vIdx - 1].cumU * end.segU;
                        // The density of a collision at the point, per volume: the density of the direction at the previous
                        // vertex, as for the vertices of real scatterings, and the majorant for the distance along the ray
                        end.pdfFwd = path[vIdx - 1].ConvertDensity(pdfFwd, end) * end.sigmaT0;
                        end.pbeta = ctx.st.pa;
                        end.pbeta *= w;
                        end.stack = ctx.stack;
                        emissionEnds.Add(new MediumEmissionEnd(end, vIdx));
                    }
                }
                return true;
            }

            public override bool BeginScatter(WalkContext ctx)
            {
                return true;
            }

            public override bool OnScatterVertex(WalkContext ctx, Medium medium, Point3f p, MediumProperties mp,
                                                 out MediumInteraction intr, out BSDF phaseBsdf)
            {
                // Record medium interaction in _path_ and compute forward density
                intr = new MediumInteraction(p, -ctx.ray.d, ctx.ray.time, medium, mp.phase);
                path[vIdx] = Vertex.CreateMedium(intr, ctx.st.beta, pdfFwd, path[vIdx - 1]);
                path[vIdx].sigmaS0 = mp.sigma_s[0];
                StoreSegment(ctx, vIdx);
                path[vIdx].stack = ctx.stack;
                path[vIdx].pbeta = ctx.st.pa;
                path[vIdx].plight = ctx.st.pl;
                phaseBsdf = path[vIdx].bsdf;
                return ++bounces < maxDepth;
            }

            public override void OnPhaseSampled(WalkContext ctx, MediumInteraction intr, Point3f p, BSDFSample ps)
            {
                // Update path state and previous path vertex after medium scattering
                pdfFwd = ps.pdf;
                ctx.ray = intr.SpawnRay(ps.wi);
                anyNonSpecularBounces = true;
                path[vIdx - 1].pdfRev = path[vIdx].ConvertDensity(ps.pdf, path[vIdx - 1]);
            }

            public override bool EndAfterMedium(WalkContext ctx, bool terminated)
            {
                return terminated;
            }

            public override void OnEscape(WalkContext ctx)
            {
                // Capture escaped rays when tracing from the camera
                if (ctx.mode == TransportMode.Radiance)
                {
                    path[vIdx] = Vertex.CreateLight(new EndpointInteraction(new Ray(ctx.ray)), ctx.st.beta, pdfFwd);
                    StoreSegment(ctx, vIdx);
                    path[vIdx].pbeta = ctx.st.pa;
                    ++bounces;
                }
            }

            public override void OnSurfaceHit(WalkContext ctx, SurfaceInteraction isect)
            {
            }

            public override bool OnBSDF(WalkContext ctx, ShapeIntersection si, SurfaceInteraction isect, BSDF bsdf)
            {
                // Possibly regularize the BSDF
                if (regularize && anyNonSpecularBounces)
                {
                    ++PathIntegrator.regularizedBSDFs;
                    bsdf.Regularize();
                }

                ++PathIntegrator.totalBSDFs;
                // Initialize _vertex_ with surface intersection information
                path[vIdx] = Vertex.CreateSurface(isect, bsdf, ctx.st.beta, pdfFwd, path[vIdx - 1]);
                StoreSegment(ctx, vIdx);
                path[vIdx].stack = ctx.stack;
                path[vIdx].pbeta = ctx.st.pa;
                path[vIdx].plight = ctx.st.pl;
                return ++bounces < maxDepth;
            }

            public override bool OnSurfaceScattered(WalkContext ctx, ShapeIntersection si, SurfaceInteraction isect, BSDF bsdf,
                                                    BSDFSample bs)
            {
                Vector3f wo = isect.wo;
                pdfFwd = bs.pdfIsProportional ? bsdf.PDF(wo, bs.wi, ctx.mode, BxDFReflTransFlags.All) : bs.pdf;
                anyNonSpecularBounces |= !bs.IsSpecular();
                // Compute path probabilities at surface vertex
                Float pdfRev = bsdf.PDF(bs.wi, wo, !ctx.mode, BxDFReflTransFlags.All);
                if (bs.IsSpecular())
                {
                    path[vIdx].delta = true;
                    pdfRev = pdfFwd = 0;
                }
                path[vIdx - 1].pdfRev = path[vIdx].ConvertDensity(pdfRev, path[vIdx - 1]);
                return true;
            }
        }

        // (not in pbrt-v4) The geometry term of a connection without its transmittance
        static Float GeometryTerm(Vertex v0, Vertex v1)
        {
            Vector3f d = v0.p() - v1.p();
            Float g = 1 / LengthSquared(d);
            d *= std.sqrt(g);
            if (v0.IsOnSurface())
                g *= AbsDot(v0.ns(), d);
            if (v1.IsOnSurface())
                g *= AbsDot(v1.ns(), d);
            return g;
        }

        static Float remap0(Float f) { return f != 0 ? f : 1; }

        public static Float MISWeight(Integrator integrator, Camera camera, Vertex[] lightVertices,
                                      Vertex[] cameraVertices, Vertex sampled, int s, int t,
                                      LightSampler lightSampler, SampledSpectrum connRu, SampledSpectrum connRl)
        {
            if (!MISSum(integrator, camera, lightVertices, cameraVertices, sampled, s, t, lightSampler, connRu, connRl,
                        true, out SampledSpectrum sum))
                return 1;
            Float average = sum.Average();
            return average > 0 ? 1 / average : 0;
        }

        // (not in pbrt-v4) The sum, per wavelength, of the probabilities of the path under all the strategies, relative to the
        // sample's strategy (the weight of MISWeight is the inverse of its average); false if the weight is one. With
        // withEquiangular the sum has the strategy that finds the scattering point next to the light by equiangular sampling (see
        // EquiangularTerm).
        public static bool MISSum(Integrator integrator, Camera camera, Vertex[] lightVertices,
                                  Vertex[] cameraVertices, Vertex sampled, int s, int t,
                                  LightSampler lightSampler, SampledSpectrum connRu, SampledSpectrum connRl,
                                  bool withEquiangular, out SampledSpectrum sumOut)
        {
            sumOut = default;
            // (not in pbrt-v4) With a camera subpath that gathers the emission of a medium, a path of length 0 has two strategies: the
            // gathered emission (s = 0, t = 2) and the light sample of the medium that connects to the camera (s = 1, t = 1)
            bool mediumEmissionPair = integrator.gatherMediumEmission &&
                ((s == 0 && t == 2 && IsVolumeEmission(cameraVertices[1])) || (s == 1 && t == 1 && IsVolumeEmission(lightVertices[0])));
            if (s + t == 2 && !mediumEmissionPair)
                return false;
            Float sumRi = 0;
            // (not in pbrt-v4) The ratio of the strategy with one light vertex, for the equiangular term (see EquiangularTerm)
            Float riOneLight = s == 1 ? 1 : 0;
            bool oneLightValid = s == 1;

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
                // (not in pbrt-v4) The strategy s = 1, t = 1 samples the camera: its sampled vertex is the camera vertex
                if (t == 1)
                    a1 = new ScopedAssignment<Vertex>(ref cameraVertices[t - 1], sampled);
                else if (s == 1)
                    a1 = new ScopedAssignment<Vertex>(ref lightVertices[s - 1], sampled);

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

                // (not in pbrt-v4) The probability of the path under each strategy, for each wavelength, relative to the hero
                // wavelength's under the strategy of the sample: the scalar ratios of the densities of the vertices (ri), as in
                // pbrt-v4, times the spectral ratios of the media: the null collisions of each segment (as a walk samples it, or
                // as a connection samples it) and the scattering coefficients at the vertices. The sum over the strategies, and
                // the average of that over the wavelengths, is the denominator of the sample (the weights of volpath's r_u and
                // r_l, for every strategy). Without a medium all of these are one.
                SampledSpectrum scatterVertices = new SampledSpectrum(1.0f);
                for (int i = 1; i < t; ++i)
                    scatterVertices *= cameraVertices[i].vtx;
                for (int i = 1; i < s; ++i)
                    scatterVertices *= lightVertices[i].vtx;
                SampledSpectrum cameraWalked = t >= 1 ? cameraVertices[t - 1].cumU : new SampledSpectrum(1.0f);
                SampledSpectrum lightWalked = s >= 1 ? lightVertices[s - 1].cumU : new SampledSpectrum(1.0f);
                SampledSpectrum oneSpectrum = new SampledSpectrum(1.0f);
                // The connection segment of the sample, if there is one, and what it is when a walk samples it
                SampledSpectrum connectionAsConnection = s >= 1 ? connRl : oneSpectrum;
                SampledSpectrum connectionAsWalk = s >= 1 ? connRu : oneSpectrum;
                // An emissive medium is sampled by a walk whose collision gathers at the point (s = 0), with the density of a
                // collision there for each wavelength
                bool hasEmissionEnd = s == 0 && t >= 1 && IsVolumeEmission(cameraVertices[t - 1]);
                SampledSpectrum emissionEnd = hasEmissionEnd ? cameraVertices[t - 1].emissionMaj : oneSpectrum;
                SampledSpectrum sum = scatterVertices * cameraWalked * lightWalked * connectionAsConnection * emissionEnd;

                // Consider hypothetical connection strategies along the camera subpath
                Float ri = 1;
                SampledSpectrum walkedAfter = oneSpectrum;
                for (int i = t - 1; i > 0; --i)
                {
                    ri *= remap0(cameraVertices[i].pdfRev) / remap0(cameraVertices[i].pdfFwd);
                    // See https://github.com/mmp/pbrt-v4/issues/347
                    if (i == 1) ri /= splatScale;
                    // (not in pbrt-v4) The strategy with i camera vertices is a term of the sum only if it is in use
                    if (!cameraVertices[i].delta && !cameraVertices[i - 1].delta && integrator.StrategyAllowed(s + t - i, i))
                    {
                        Float riTerm = t == 1 ? ri / splatScale : ri;
                        if (s == 0 && i == t - 1)
                        {
                            riOneLight = riTerm;
                            oneLightValid = true;
                        }
                        // The segment that arrives at vertex i is the connection of this strategy; the others, and the connection
                        // of the sample, are walked
                        sum += riTerm * (scatterVertices * cameraVertices[i - 1].cumU * walkedAfter * cameraVertices[i].segL *
                                         lightWalked * connectionAsWalk);
                    }
                    walkedAfter *= cameraVertices[i].segU;
                }

                // Consider hypothetical connection strategies along the light subpath
                ri = 1;
                walkedAfter = oneSpectrum;
                for (int i = s - 1; i >= 0; --i)
                {
                    ri *= remap0(lightVertices[i].pdfRev) / remap0(lightVertices[i].pdfFwd);
                    bool deltaLightvertex = i > 0 ? lightVertices[i - 1].delta : lightVertices[0].IsDeltaLight();
                    // (not in pbrt-v4) A camera subpath that gathers the emission of volumes has the s = 0 strategy with the
                    // density of a collision at the point (per volume: the majorant times the density of the direction);
                    // one that does not has no such strategy
                    SampledSpectrum emissionHere = oneSpectrum;
                    bool skip = false;
                    if (i == 0 && IsVolumeEmission(lightVertices[0]))
                    {
                        if (!integrator.gatherMediumEmission)
                            skip = true;
                        else
                        {
                            ri *= lightVertices[0].sigmaT0;
                            emissionHere = lightVertices[0].emissionMaj;
                        }
                    }
                    if (!skip && !lightVertices[i].delta && !deltaLightvertex && integrator.StrategyAllowed(i, t + s - i))
                    {
                        Float riTerm = t == 1 ? ri / splatScale : ri;
                        if (i == 1)
                        {
                            riOneLight = riTerm;
                            oneLightValid = true;
                        }
                        if (i > 0)
                        {
                            sum += riTerm * (scatterVertices * lightVertices[i - 1].cumU * walkedAfter * lightVertices[i].segL *
                                             cameraWalked * connectionAsWalk);
                        }
                        else
                        {
                            // No connection: every segment is walked by the camera subpath
                            sum += riTerm * (scatterVertices * cameraWalked * lightWalked * connectionAsWalk * emissionHere);
                        }
                    }
                    if (i > 0)
                        walkedAfter *= lightVertices[i].segU;
                }

                // (not in pbrt-v4) The strategy that samples the scattering point next to the light by equiangular sampling
                if (withEquiangular && integrator.bdptEquiangular && oneLightValid)
                    sum += EquiangularTerm(integrator, lightVertices, cameraVertices, sampled, s, t, riOneLight, connRu, connRl, lightSampler);

                sumOut = sum;
                return true;
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

        // (not in pbrt-v4) A light that equiangular sampling takes part in: a point or spot light, or an area light that is sampled
        // uniformly by area (see DiffuseAreaLight.SupportsEquiangular)
        public static bool EquiangularLight(Light light)
        {
            if (light is DiffuseAreaLight areaLight)
                return areaLight.SupportsEquiangular();
            return light != null && !(light is VolumeEmissionLight) && light.Type() == LightType.DeltaPosition;
        }

        static Light LightOfVertex(Vertex v)
        {
            return v.type == VertexType.Light ? v.ei.light : (v.type == VertexType.Surface ? v.si.areaLight : null);
        }

        // (not in pbrt-v4) The first piece of the straight segment from z to x (the part before any boundary between media), where the
        // walk that arrives at x from z has x if there is no boundary between them: the origin and direction of the piece, its
        // length (infinite if nothing is behind), and the distance of x from the origin
        static bool FirstPiece(Integrator integrator, Vertex z, Vertex x, out Point3f origin, out Vector3f direction,
                               out Float tMax, out Float tX)
        {
            origin = default;
            direction = default;
            tMax = 0;
            tX = 0;
            Ray ray = z.GetInteraction().SpawnRayTo(x.GetInteraction());
            Float length = Length(ray.d);
            if (length == 0)
                return false;
            pstd.optional<ShapeIntersection> si = integrator.Intersect(ray);
            if (si && si.value().tHit < 1 - ShadowEpsilon)
                return false;
            origin = ray.o;
            direction = ray.d / length;
            tMax = si ? si.value().tHit * length : Infinity;
            tX = Dot(x.p() - origin, direction);
            return tX > 0 && tX <= tMax;
        }

        // (not in pbrt-v4) The strategy that finds a path y0, x1, ... with a scattering point x1 in a medium next to a point or area light
        // y0 by choosing the light point first, and x1 on the segment that arrives at it from the camera side by the angle seen from
        // the light (Fong et al., 4.2.2 and 4.2.3), as a term of the sum of MISSum. It differs from the strategy with one light
        // vertex (the connection of the camera subpath, which has x1, to the light) in the segment that arrives at x1 (a connection
        // that is sampled, with the null collisions as segL), in the scattering coefficient at x1 (which that strategy has and this does
        // not), and in the density of x1: the equiangular one over the scattering coefficient of the hero wavelength (the density of the
        // collision), as in volpath. riOneLight is the ratio of the strategy with one light vertex to the sample's.
        static SampledSpectrum EquiangularTerm(Integrator integrator, Vertex[] lightVertices, Vertex[] cameraVertices,
                                               Vertex sampled, int s, int t, Float riOneLight, SampledSpectrum connRu,
                                               SampledSpectrum connRl, LightSampler lightSampler)
        {
            SampledSpectrum zero = new SampledSpectrum(0.0f);
            SampledSpectrum value = new SampledSpectrum(riOneLight);
            Vertex y0, x1, z;
            if (s == 1)
            {
                if (t < 2)
                    return zero;
                y0 = lightVertices[0];
                x1 = cameraVertices[t - 1];
                z = cameraVertices[t - 2];
                for (int i = 1; i < t - 1; ++i)
                    value *= cameraVertices[i].vtx;
                value *= z.cumU * x1.segL * connRl;
            }
            else if (s == 0)
            {
                if (t < 3)
                    return zero;
                y0 = cameraVertices[t - 1];
                x1 = cameraVertices[t - 2];
                z = cameraVertices[t - 3];
                for (int i = 1; i < t - 2; ++i)
                    value *= cameraVertices[i].vtx;
                value *= z.cumU * x1.segL * y0.segL;
            }
            else
            {
                y0 = lightVertices[0];
                x1 = lightVertices[1];
                for (int i = 1; i < t; ++i)
                    value *= cameraVertices[i].vtx;
                for (int i = 2; i < s; ++i)
                    value *= lightVertices[i].vtx;
                value *= cameraVertices[t - 1].cumU * x1.segL;
                if (s == 2)
                {
                    z = cameraVertices[t - 1];
                    value *= connRl;
                }
                else
                {
                    z = lightVertices[2];
                    value *= z.segL;
                    for (int i = 3; i < s; ++i)
                        value *= lightVertices[i].segU;
                    value *= connRu;
                }
            }
            if (x1.type != VertexType.Medium || !(x1.sigmaS0 > 0) || !EquiangularLight(LightOfVertex(y0)))
                return zero;
            if (!FirstPiece(integrator, z, x1, out Point3f origin, out Vector3f direction, out Float tMax, out Float tX))
                return zero;
            Float pdfDistance = EquiangularSampling.Pdf(origin, direction, tMax, y0.p(), tX);
            if (!(pdfDistance > 0))
                return zero;
            // (the light is chosen with its probability among the lights of the strategy, against that of the connection)
            Light light = LightOfVertex(y0);
            Float pmfConnection = lightSampler.PMF(light);
            if (!(pmfConnection > 0))
                return zero;
            Float pmfEquiangular = integrator.bdptEquiangularLightSampler.PMF(light);
            return value * (integrator.EquiangularCount(s + t - 2) * pdfDistance * pmfEquiangular / (x1.sigmaS0 * pmfConnection));
        }

        public static Float InfiniteLightDensity(List<Light> infiniteLights, LightSampler lightSampler, Vector3f w)
        {
            Float pdf = 0;
            foreach (Light light in infiniteLights)
                pdf += light.PDF_Li(new LightSampleContext(new Interaction()), -w) * lightSampler.PMF(light);
            return pdf;
        }

        // (not in pbrt-v4) The beta of a vertex where the wavelengths without throughput do not divide by zero
        static SampledSpectrum SafeBeta(SampledSpectrum beta)
        {
            SampledSpectrum safe = beta;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                if (safe[i] == 0)
                    safe[i] = 1;
            }
            return safe;
        }

        // (not in pbrt-v4) The Mueller matrix of the scattering at a connection vertex toward wi, with the scalar BSDF or phase
        // function divided out: a Rayleigh scattering in a medium polarises, every other scattering depolarises
        static PAttenuation ConnectionMueller(Vertex v, Vector3f wi, bool fromCamera)
        {
            if (v.type == VertexType.Medium &&
                PolarisedPathIntegrator.PhaseAttenuation(v.mi.phase, v.mi.wo, wi, fromCamera, out PAttenuation M))
                return M;
            return PAttenuation.Lambert(new SampledSpectrum(1.0f));
        }

        // (not in pbrt-v4) The path that a camera subpath makes with the emission that it gathered in a medium: the vertices
        // of the subpath before the collision, then the collision as a light vertex. A complete path, so s = 0.
        public static SampledSpectrum ConnectEmissionEnd(Integrator integrator, ref SampledWavelengths lambdaRef,
                                                         Vertex[] cameraVertices, MediumEmissionEnd end,
                                                         LightSampler lightSampler, Camera camera, Sampler sampler,
                                                         PolarisedPathIntegrator.Sensor pol, Vector3f cameraDirection)
        {
            Vertex[] path = new Vertex[end.index + 1];
            for (int i = 0; i < end.index; ++i)
                path[i] = cameraVertices[i];
            path[end.index] = end.vertex;
            pstd.optional<Point2f> pRaster = default;
            Float misWeight = 0;
            SampledSpectrum Lend = ConnectBDPT(integrator, ref lambdaRef, new Vertex[0], path, 0, end.index + 1, lightSampler, camera,
                               sampler, ref pRaster, ref misWeight, pol, cameraDirection);
            return Lend;
        }

        public static SampledSpectrum ConnectBDPT(Integrator integrator, ref SampledWavelengths lambdaRef,
                                                  Vertex[] lightVertices, Vertex[] cameraVertices, int s, int t,
                                                  LightSampler lightSampler, Camera camera, Sampler sampler,
                                                  ref pstd.optional<Point2f> pRaster, ref Float misWeightOut,
                                                  PolarisedPathIntegrator.Sensor pol = null,
                                                  Vector3f cameraDirection = default)
        {
            SampledWavelengths lambda = lambdaRef;
            SampledSpectrum L = new SampledSpectrum(0.0f);
            // (not in pbrt-v4) The direction of the light at the sensor for a connection to the camera
            Vector3f toCamera = default;
            // (not in pbrt-v4) The factors of the connection segment for the spectral MIS weights (see Integrator.TrSegments)
            SampledSpectrum connRu = new SampledSpectrum(1.0f), connRl = new SampledSpectrum(1.0f);
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
                        toCamera = Normalize(cs.value().pLens.p() - qs.p());
                        L = qs.beta * qs.f(sampled, TransportMode.Importance) * sampled.beta;
                        // (not in pbrt-v4) The throughput of the emission vertex is the emission: divide by the density of
                        // the point and of the choice of the light
                        if (BDPT.IsVolumeEmission(qs))
                            L = qs.pdfFwd > 0 ? L / qs.pdfFwd : new SampledSpectrum(0.0f);
                        if (qs.IsOnSurface())
                            L *= AbsDot(cs.value().wi, qs.ns());
                        DCHECK(!L.HasNaNs());
                        if ((bool)L)
                        {
                            L *= integrator.TrSegments(cs.value().pRef, cs.value().pLens, qs.stack, lambda, out connRu, out connRl,
                                                       out Medium _);

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

                        LightSampleContext ctx = Integrator.LightContextAt(pt.GetInteraction(), pt.IsOnSurface(),
                                                                           pt.IsOnSurface() ? pt.bsdf.Flags() : default);
                        // (not in pbrt-v4) The point of an emissive volume has a third coordinate
                        VolumeEmissionLight volumeLight = light as VolumeEmissionLight;
                        pstd.optional<LightLiSample> lightWeight = volumeLight != null
                            ? volumeLight.SampleLiVolume(ctx, sampler.Get2D(), sampler.Get1D(), lambda)
                            : light.SampleLi(ctx, sampler.Get2D(), lambda);
                        if (lightWeight && (bool)lightWeight.value().L && lightWeight.value().pdf > 0)
                        {
                            EndpointInteraction ei = new EndpointInteraction(light, lightWeight.value().pLight);
                            sampled = Vertex.CreateLight(ei, lightWeight.value().L / (lightWeight.value().pdf * p_l), 0);
                            sampled.pdfFwd = sampled.PDFLightOrigin(integrator.infiniteLights, pt, lightSampler);
                            if (volumeLight != null)
                            {
                                SampledSpectrum majorant = volumeLight.Medium().MajorantAt(lightWeight.value().pLight.p(), lambda);
                                sampled.sigmaT0 = majorant[0];
                                sampled.emissionMaj = majorant / majorant[0];
                            }
                            L = pt.beta * pt.f(sampled, TransportMode.Radiance) * sampled.beta;
                            if (pt.IsOnSurface())
                                L *= AbsDot(lightWeight.value().wi, pt.ns());
                            // Only check visibility if the path would carry radiance.
                            if ((bool)L)
                            {
                                L *= integrator.TrSegments(pt.GetInteraction(), lightWeight.value().pLight, pt.stack, lambda,
                                                           out connRu, out connRl, out Medium endMedium);
                                // The point has to lie in the medium that emits
                                if (volumeLight != null && endMedium != volumeLight.Medium())
                                    L = new SampledSpectrum(0.0f);
                            }
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
                    {
                        L *= GeometryTerm(qs, pt);
                        L *= integrator.TrSegments(qs.GetInteraction(), pt.GetInteraction(), qs.stack, lambda, out connRu,
                                                   out connRl, out Medium _);
                    }
                }
            }

            ++PathIntegrator.totalPaths;
            if (!L)
                ++PathIntegrator.zeroRadiancePaths;
            if (PathIntegrator.pathLength == null)
                PathIntegrator.pathLength = new StatIntDistribution();
            PathIntegrator.pathLength.Add(s + t - 2);
            // Compute MIS weight for connection strategy
            Float misWeight = (bool)L ? MISWeight(integrator, camera, lightVertices, cameraVertices, sampled, s, t, lightSampler,
                                                  connRu, connRl)
                                : 0.0f;
            DCHECK(!IsNaN(misWeight));
            L *= misWeight;
            misWeightOut = misWeight;

            // (not in pbrt-v4) What the sensor measures of the polarised light of the path. The scalar contribution has all the
            // factors of the strategy, so the polarisation is taken relative to the scalar throughputs of the two subpaths: the
            // Mueller matrix of the camera subpath and the Stokes vector of the light subpath, each divided by its beta,
            // with the Mueller matrices of the scattering at the two connection vertices between.
            lambdaRef = lambda;
            if (pol != null && (bool)L)
            {
                PLight lightSide = new PLight(new SampledSpectrum(1.0f));
                PAttenuation cameraSide = PAttenuation.Identity();
                if (s > 0)
                {
                    Vertex qs = s == 1 && t > 1 ? sampled : lightVertices[s - 1];
                    lightSide = qs.plight;
                    lightSide /= SafeBeta(qs.beta);
                    if (s > 1 && t > 0)
                    {
                        Vector3f toVertex = t == 1 ? toCamera : Normalize(cameraVertices[t - 1].p() - qs.p());
                        lightSide = ConnectionMueller(qs, toVertex, false) * lightSide;
                    }
                }
                if (t > 1)
                {
                    Vertex pt = cameraVertices[t - 1];
                    cameraSide = pt.pbeta;
                    cameraSide /= SafeBeta(pt.beta);
                    Vector3f toVertex = s == 0 ? default : Normalize(s == 1 ? sampled.p() - pt.p() : lightVertices[s - 1].p() - pt.p());
                    if (s > 0)
                        lightSide = ConnectionMueller(pt, toVertex, true) * lightSide;
                }
                else if (s > 1)
                {
                    // t = 1: the sampled vertex is the camera, nothing scatters
                }

                PLight Lp = cameraSide * lightSide;
                Lp *= L;
                return PolarisedPathIntegrator.Measure(Lp, pol.analyser, pol.analyserAngle, pol.measurement,
                                                       t == 1 ? toCamera : cameraDirection, pol.cameraUp);
            }

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
                              bool regularize = false, PolarisedPathIntegrator.Sensor sensor = null,
                              bool russianRoulette = false, bool gatherMediumEmission = true,
                              bool unidirectionalStrategies = false, bool equiangular = false,
                              int equiangularSamples = 1, int equiangularMultipleDepth = int.MaxValue)
            : base(camera, sampler, aggregate, EmissionLightsForBDPT(lights))
        {
            // (not in pbrt-v4) Equiangular sampling of the scattering points next to point and area lights (see BDPT.EquiangularTerm)
            this.bdptEquiangular = equiangular && sensor == null;
            if (this.bdptEquiangular)
            {
                List<Light> supported = new List<Light>();
                foreach (Light light in TextureMedium.AddEmissionLights(lights))
                {
                    if (BDPT.EquiangularLight(light))
                    {
                        supported.Add(light);
                    }
                }
                if (supported.Count == 0)
                {
                    this.bdptEquiangular = false;
                }
                else
                {
                    this.bdptEquiangularLightSampler = new PowerLightSampler(supported.ToArray(), new Allocator());
                }
            }
            this.bdptMaxDepth = maxDepth;
            this.bdptEquiangularSamples = Math.Max(1, equiangularSamples);
            this.bdptEquiangularMultipleDepth = equiangularMultipleDepth;
            this.gatherMediumEmission = gatherMediumEmission;
            this.unidirectionalStrategies = unidirectionalStrategies;
            // (not in pbrt-v4) The polarisation of the light is carried along the subpaths next to the scalar throughput
            this.sensor = sensor;
            this.russianRoulette = russianRoulette;
            if (sensor != null)
                sensor.cameraUp = camera.GetCameraTransform().RenderFromCamera(new Vector3f(0, 1, 0), 0);
            this.maxDepth = maxDepth;
            this.regularize = regularize;
            // (not in pbrt-v4) Media with emission are lights too
            this.lightSampler = new PowerLightSampler(TextureMedium.AddEmissionLights(lights).ToArray(), new Allocator());
            this.visualizeStrategies = visualizeStrategies;
            this.visualizeWeights = visualizeWeights;
        }

        // (not in pbrt-v4) Every emissive medium is sampled as a light, next to the gathering of its emission by the camera subpaths
        public static List<Light> EmissionLightsForBDPT(List<Light> lights)
        {
            TextureMedium.EnableEmissionSamplingForAll(16);
            return TextureMedium.AddEmissionLights(lights);
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

        // (not in pbrt-v4) The light sampler of the strategies, for the equiangular sampling of the camera subpaths
        public LightSampler BDPTLightSampler { get { return lightSampler; } }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                           ScratchBuffer scratchBuffer, VisibleSurface visibleSurface)
        {
            // Trace the camera and light subpaths
            // (index 0 of the arrays is the first vertex; RandomWalk is given offset 1)
            Vertex[] cameraVertices = new Vertex[maxDepth + 2];
            for (int i = 0; i < cameraVertices.Length; ++i) cameraVertices[i] = new Vertex();
            List<MediumEmissionEnd> emissionEnds = gatherMediumEmission ? new List<MediumEmissionEnd>() : null;
            BDPTEquiangularSink equiangularSink = bdptEquiangular ? new BDPTEquiangularSink(maxDepth) : null;
            int nCamera = BDPT.GenerateCameraSubpath(this, ray, ref lambda, sampler, scratchBuffer, maxDepth + 2,
                                                     camera, cameraVertices, regularize, sensor != null, emissionEnds,
                                                     equiangularSink);
            Vertex[] lightVertices = new Vertex[maxDepth + 1];
            for (int i = 0; i < lightVertices.Length; ++i) lightVertices[i] = new Vertex();
            int nLight = BDPT.GenerateLightSubpath(this, ref lambda, sampler, camera, scratchBuffer,
                                                   unidirectionalStrategies ? 1 : maxDepth + 1,
                                                   cameraVertices[0].time(), lightSampler, lightVertices,
                                                   regularize, sensor != null);

            SampledSpectrum L = new SampledSpectrum(0.0f);
            // Execute all BDPT connection strategies
            for (int t = 1; t <= nCamera; ++t)
            {
                for (int s = 0; s <= nLight; ++s)
                {
                    int depth = t + s - 2;
                    if ((s == 1 && t == 1 && !BDPT.IsVolumeEmission(lightVertices[0])) || depth < 0 || depth > maxDepth)
                        continue;
                    if (!StrategyAllowed(s, t))
                        continue;
                    // Execute the $(s, t)$ connection strategy and update _L_
                    pstd.optional<Point2f> pFilmNew = default;
                    Float misWeight = 0.0f;
                    SampledSpectrum Lpath = BDPT.ConnectBDPT(this, ref lambda, lightVertices, cameraVertices, s, t,
                                                            lightSampler, camera, sampler, ref pFilmNew,
                                                            ref misWeight, sensor, -ray.d);
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

            if (equiangularSink != null)
                L += equiangularSink.L;

            // (not in pbrt-v4) The emission that the camera subpath gathered in media: each end is a path of its own, with the
            // vertices of the subpath before it, and its MIS weight against the strategies that sample the medium as a light
            if (emissionEnds != null)
            {
                foreach (MediumEmissionEnd end in emissionEnds)
                {
                    int depth = end.index - 1;
                    if (end.index > nCamera || depth < 0 || depth > maxDepth)
                        continue;
                    L += BDPT.ConnectEmissionEnd(this, ref lambda, cameraVertices, end, lightSampler, camera, sampler, sensor, -ray.d);
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
            MediumTracking.RequireNoPerEntryVolumes("bdpt");
            MediumTracking.RequireNoRefractive("bdpt");
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
            bool russianRoulette = parameters.GetOneBool("russianroulette", false);
            // (not in pbrt-v4) Camera subpaths gather the emission of media at their collisions, with MIS against the strategies
            // that sample the media as lights; false leaves the emission to those strategies alone
            bool gatherMediumEmission = parameters.GetOneBool("gathermediumemission", true);
            // (not in pbrt-v4) Equiangular sampling of the scattering points of the camera subpaths next to point and area lights
            // (Fong et al., 4.2); not with polarised light
            bool equiangular = parameters.GetOneBool("equiangular", false);
            if (equiangular && parameters.GetOneBool("polarised", false))
                Warning(loc, "\"equiangular\" is not used with \"polarised\" bdpt.");
            // (not in pbrt-v4) "all" or "unidirectional" (see Integrator.StrategyAllowed)
            string strategies = parameters.GetOneString("strategies", "all");
            if (strategies != "all" && strategies != "unidirectional")
                ErrorExit(loc, StringPrintf("\"%s\": unknown \"strategies\"; expected \"all\" or \"unidirectional\".", strategies));
            PolarisedPathIntegrator.Sensor sensor = null;
            PolarisedPathIntegrator.ParseSensor(parameters, loc, out bool analyser, out Float analyserAngle,
                                                out PolarisedPathIntegrator.Measurement measurement);
            if (parameters.GetOneBool("polarised", false))
            {
                sensor = new PolarisedPathIntegrator.Sensor();
                sensor.analyser = analyser;
                sensor.analyserAngle = analyserAngle;
                sensor.measurement = measurement;
            }

            return new BDPTIntegrator(camera, sampler, aggregate, lights, maxDepth, visualizeStrategies,
                                      visualizeWeights, regularize, sensor, russianRoulette, gatherMediumEmission,
                                      strategies == "unidirectional", equiangular,
                                      parameters.GetOneInt("equiangularsamples", 1),
                                      parameters.GetOneInt("equiangulardepth", int.MaxValue));
        }

        // BDPTIntegrator Private Members
        int maxDepth;
        bool regularize;
        LightSampler lightSampler;
        bool visualizeStrategies, visualizeWeights;
        Film[] weightFilms;
        PolarisedPathIntegrator.Sensor sensor;
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
                             Float largeStepProbability, bool regularize, VolPathIntegrator volpath = null,
                             PolarisedPathIntegrator.Sensor sensor = null, bool gatherMediumEmission = true)
            : base(aggregate, volpath == null ? BDPTIntegrator.EmissionLightsForBDPT(lights) : lights)
        {
            // (not in pbrt-v4) With bdpt the emissive media are lights too, and the camera subpaths gather their emission
            this.gatherMediumEmission = gatherMediumEmission;
            this.lightSampler = new PowerLightSampler(this.lights.ToArray(), new Allocator());
            this.camera = camera;
            this.maxDepth = maxDepth;
            this.nBootstrap = nBootstrap;
            this.nChains = nChains;
            this.mutationsPerPixel = mutationsPerPixel;
            this.sigma = sigma;
            this.largeStepProbability = largeStepProbability;
            this.regularize = regularize;
            // (not in pbrt-v4) What a state of the chains is: a strategy of bdpt or a walk of volpath
            if (volpath != null)
                this.evaluator = new VolPathPathEvaluator(volpath, camera);
            else
                this.evaluator = new BDPTPathEvaluator(this, camera, lightSampler, maxDepth, regularize, sensor);
        }

        // MLTIntegrator Method Definitions
        SampledSpectrum L(ScratchBuffer scratchBuffer, MLTSampler sampler, int depth, out Point2f pRaster,
                          out SampledWavelengths lambda)
        {
            pRaster = default;
            lambda = default;
            if (lights.Count == 0)
                return new SampledSpectrum(0.0f);
            return evaluator.Evaluate(scratchBuffer, sampler, depth, out pRaster, out lambda);
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
                DebugMLTSampler dsampler = DebugMLTSampler.Create(new pstd.span<string>(rest), MLTPathEvaluator.nSampleStreams);

                Point2f pRasterDbg;
                SampledWavelengths lambdaDbg;
                ScratchBuffer sb = new ScratchBuffer(65536);
                L(sb, dsampler, depthDbg, out pRasterDbg, out lambdaDbg);
                return;
            }

            // Generate bootstrap samples and compute normalization constant $b$
            Timer timer = new Timer();
            int nBootstrapSamples = nBootstrap * nDepths;
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
                    for (int depth = 0; depth < nDepths; ++depth)
                    {
                        int rngIndex = (int)(i * nDepths + depth);
                        MLTSampler sampler = new MLTSampler(mutationsPerPixel, rngIndex, sigma,
                                                            largeStepProbability, MLTPathEvaluator.nSampleStreams);
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
            Float b = (Float)nDepths / bootstrapWeights.Length * (Float)bootstrapSum;

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
                int depth = bootstrapIndex % nDepths;

                // Initialize local variables for selected state
                MLTSampler sampler = new MLTSampler(mutationsPerPixel, bootstrapIndex, sigma, largeStepProbability,
                                                    MLTPathEvaluator.nSampleStreams);
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
            // (not in pbrt-v4) "bdpt" mutates the strategies of bidirectional path tracing, "volpath" the paths of the volumetric
            // path tracer (which then also reads its parameters: lightsampler, polarised, the sensor)
            string pathEstimator = parameters.GetOneString("pathestimator", "bdpt");
            VolPathIntegrator volpath = null;
            // (not in pbrt-v4) The sensor of polarised bdpt: the strategies carry the Stokes vector and the sensor measures it
            PolarisedPathIntegrator.Sensor sensor = null;
            if (pathEstimator == "bdpt" && parameters.GetOneBool("polarised", false))
            {
                PolarisedPathIntegrator.ParseSensor(parameters, loc, out bool analyser, out Float analyserAngle,
                                                    out PolarisedPathIntegrator.Measurement measurement);
                sensor = new PolarisedPathIntegrator.Sensor();
                sensor.analyser = analyser;
                sensor.analyserAngle = analyserAngle;
                sensor.measurement = measurement;
            }
            if (pathEstimator == "volpath")
                volpath = VolPathIntegrator.Create(parameters, camera, null, aggregate, lights, loc);
            else if (pathEstimator != "bdpt")
                ErrorExit(loc, StringPrintf("%s: unknown \"pathestimator\" for \"mlt\"; use \"bdpt\" or \"volpath\".", pathEstimator));
            bool gatherMediumEmission = parameters.GetOneBool("gathermediumemission", true);
            return new MLTIntegrator(camera, aggregate, lights, maxDepth, nBootstrap, (int)nChains,
                                     mutationsPerPixel, sigma, largeStepProbability, regularize, volpath, sensor,
                                     gatherMediumEmission);
        }

        // MLTIntegrator Private Members
        // The number of states of the bootstrap: one for each depth of bdpt, one for volpath
        int nDepths { get { return evaluator.StateCount; } }
        MLTPathEvaluator evaluator;
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
                                 bool regularize = false, bool polarised = false, bool analyser = false,
                                 Float analyserAngle = 0,
                                 PolarisedPathIntegrator.Measurement measurement = PolarisedPathIntegrator.Measurement.Intensity,
                                 bool equiangular = false, int equiangularSamples = 1, int equiangularMultipleDepth = int.MaxValue,
                                 bool uniformDistance = false, bool refractiveNee = true)
            : base(camera, sampler, aggregate, TextureMedium.AddEmissionLights(lights))
        {
            // (not in pbrt-v4) From a scattering point inside a refractive medium, the light is found by next event estimation
            // along a curved path (true), or only by the sampled direction (false); each is a complete estimator of that light
            this.refractiveNee = refractiveNee;
            // (not in pbrt-v4) Several equiangular samples per segment for the first scatterings (Fong et al., 4.1)
            this.equiangularSamples = Math.Max(1, equiangularSamples);
            this.equiangularMultipleDepth = equiangularMultipleDepth;
            // (not in pbrt-v4) Uniform distance sampling of the scattering point, with MIS (Fong et al., 4.2.1)
            this.uniformDistance = uniformDistance;
            // (not in pbrt-v4) Equiangular sampling of the scattering point toward point lights (Fong et al., section 4.2.2)
            this.equiangular = equiangular;
            // (not in pbrt-v4) The polarisation of the light is carried along the path next to the scalar throughput
            this.polarised = polarised;
            this.analyser = analyser;
            this.analyserAngle = analyserAngle;
            this.measurement = measurement;
            if (polarised)
                cameraUp = camera.GetCameraTransform().RenderFromCamera(new Vector3f(0, 1, 0), 0);
            this.maxDepth = maxDepth;
            this.lightSampler = LightSampler.Create(lightSampleStrategy, TextureMedium.AddEmissionLights(lights).ToArray(),
                                                    new Allocator());
            this.regularize = regularize;
            // (not in pbrt-v4) Equiangular sampling chooses among the lights it takes part with (a choice of any other light would
            // find nothing); with none it is off
            if (this.equiangular)
            {
                List<Light> supported = new List<Light>();
                foreach (Light light in TextureMedium.AddEmissionLights(lights))
                {
                    if (BDPT.EquiangularLight(light))
                    {
                        supported.Add(light);
                    }
                }
                if (supported.Count == 0)
                {
                    this.equiangular = false;
                }
                else
                {
                    this.equiangularLightSampler = LightSampler.Create(lightSampleStrategy, supported.ToArray(), new Allocator());
                }
            }
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
            // (not in pbrt-v4) The walk is shared with bdpt (see PathWalk); what volpath does at each of its points is the hook.
            // The walk works on its own copy of _lambda_, since GetBSDF() may terminate secondary wavelengths.
            WalkContext ctx = new WalkContext
            {
                integrator = this,
                lambda = lambdaRef,
                ray = ray,
                stack = MediumTracking.Start(camera.GetMedium()),
                sampler = sampler,
                camera = camera,
                scratchBuffer = scratchBuffer,
                mode = TransportMode.Radiance,
                // The Mueller matrix of the throughput and the Stokes vector of the radiance. The matrix takes every scalar
                // factor of beta, so its first element is beta
                st = new PathState(new SampledSpectrum(1.0f), PAttenuation.Identity(), PLight.Zero(), true, polarised, true)
            };
            ctx.st.trackSegment = (equiangular || uniformDistance) && !polarised;
            VolPathWalkHook hook = new VolPathWalkHook(this, visibleSurf, -ray.d);
            try
            {
                PathWalk.Run(ctx, hook);
                return Finish(hook.L, hook.pL, hook.cameraDirection);
            }
            finally
            {
                lambdaRef = ctx.lambda;
            }
        }

        // The volumetric path: the radiance it gathers from emission and from sampling lights, and the Russian roulette
        sealed class VolPathWalkHook : PathWalkHook
        {
            public VolPathWalkHook(VolPathIntegrator vp, VisibleSurface visibleSurf, Vector3f cameraDirection)
            {
                this.vp = vp;
                this.visibleSurf = visibleSurf;
                this.cameraDirection = cameraDirection;
                maxDepth = vp.maxDepth;
                lightSampler = vp.lightSampler;
                regularize = vp.regularize;
                polarised = vp.polarised;
            }

            VolPathIntegrator vp;
            VisibleSurface visibleSurf;
            int maxDepth;
            LightSampler lightSampler;
            bool regularize, polarised;

            public SampledSpectrum L = new SampledSpectrum(0.0f);
            public PLight pL = PLight.Zero();
            public Vector3f cameraDirection;
            bool specularBounce = false, anyNonSpecularBounces = false;
            int depth = 0;
            Float etaScale = 1;
            LightSampleContext prevIntrContext = default;
            // (not in pbrt-v4) For the line sampling of emission (see SampleLdFnee): the density that the last scattering
            // vertex had for the direction of the ray, and the ratio of the density of sampling the direction toward
            // the emission to it, which is looked up when the emission is first found along the ray
            Float fneeScatterPdf = 0, fneeRatio = -1;
            Point3f fneeOrigin = default;
            // (not in pbrt-v4) The path since the last vertex was curved (a refractive medium), and the vertex was in a refractive
            // medium. A curved path is found by the walk only, except from inside a refractive medium, where next event estimation
            // finds the area lights along curved paths: such a light is then reached by that strategy only (the sampled
            // direction's contribution would count the same paths twice), and every other light by the walk only.
            bool curvedSegment = false, vertexInRefractive = false, scatterInRefractive = false;

            public override bool BeginIteration(WalkContext ctx)
            {
                return true;
            }

            // (not in pbrt-v4) Equiangular sampling of the scattering point of the segment (see SampleEquiangular): the segment
            // is recorded for the weight of the vertex that the walk finds on it, and the other way to find one is sampled
            EquiangularSegment equiangularSegment = new EquiangularSegment();
            // (not in pbrt-v4) The segment of the last scattering vertex, for the emission that its phase-sampled direction finds
            EquiangularSegment scatter = new EquiangularSegment();
            bool scatterValid;
            Point3f scatterP;
            SampledSpectrum scatterRl0;
            Float scatterPdf;

            public override void BeginSegment(WalkContext ctx, Medium medium, Float tMax)
            {
                equiangularSegment.active = false;
                if ((!vp.equiangular && !vp.uniformDistance) || polarised)
                {
                    return;
                }
                ctx.st.ResetSegment();
                if (depth >= maxDepth || !(bool)ctx.st.beta || !(bool)ctx.st.r_u)
                {
                    return;
                }
                Float length = Length(ctx.ray.d);
                EquiangularSegment seg = equiangularSegment;
                seg.origin = ctx.ray.o;
                seg.direction = ctx.ray.d / length;
                seg.tMax = tMax * length;
                seg.context = new LightSampleContext(new Point3fi(ctx.ray.o), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
                seg.r_u0 = ctx.st.r_u;
                seg.active = true;
                // (Fong et al., 4.1) Several samples of the segment for the first scatterings, one for the later ones
                seg.count = depth < vp.equiangularMultipleDepth ? vp.equiangularSamples : 1;
                seg.equiangular = vp.equiangular;
                // (Fong 4.2.1) Uniform distance sampling needs a segment of finite length
                seg.pdfUniform = vp.uniformDistance && !Float.IsInfinity(seg.tMax) && seg.tMax > 0 ? 1 / seg.tMax : 0;
                if (vp.equiangular)
                {
                    for (int i = 0; i < seg.count; ++i)
                    {
                        L += vp.SampleSegmentPoint(ctx, medium, seg, false);
                    }
                }
                if (seg.pdfUniform > 0)
                {
                    L += vp.SampleSegmentPoint(ctx, medium, seg, true);
                }
            }

            public override void OnCurvedSegment(WalkContext ctx)
            {
                if (polarised)
                    ErrorExit("Polarised volpath does not support refractive media yet (docs/refractive-media.md, stage 7).");
                curvedSegment = true;
            }

            public override bool BeginCollision(WalkContext ctx, Medium rayMedium, Point3f p, MediumProperties mp,
                                                SampledSpectrum sigma_maj, SampledSpectrum T_maj_)
            {
                // Handle medium scattering event for ray
                if (!ctx.st.beta)
                {
                    return false;
                }
                ++volumeInteractions;
                // Add emission from medium scattering event
                if (depth < maxDepth && (bool)mp.Le)
                {
                    // Compute $\beta'$ at new path vertex
                    Float pdf = sigma_maj[0] * T_maj_[0];
                    SampledSpectrum betap = ctx.st.beta * T_maj_ / pdf;

                    // Compute rescaled path probability for absorption at path vertex
                    SampledSpectrum r_e = ctx.st.r_u * sigma_maj * T_maj_ / pdf;

                    // Update _L_ for medium emission
                    if ((bool)r_e)
                    {
                        // (not in pbrt-v4) A medium whose emission is also sampled from the previous scattering
                        // vertex: the light strategy would have chosen this point with probability density
                        // pmf * pdfPoint * t^2 per direction and length, where the density of this strategy
                        // is sigma_maj T_maj per length (hero values), and the light strategy's null
                        // collisions on the way are already in st.r_l, with the tail of T_maj added here
                        SampledSpectrum r_sum = r_e;
                        VolumeEmissionLight emissionLight = (rayMedium as TextureMedium)?.EmissionLight();
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
                                    fneeRatio = pmfDirection * emissionLight.PdfDirection(fneeOrigin, ctx.ray.d) / fneeScatterPdf;
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
                                r_sum = r_e + ctx.st.r_l * (T_maj_ / T_maj_[0]) * densityRatio;
                            }
                        }
                        L += betap * mp.sigma_a * mp.Le / r_sum.Average();
                        if (polarised)
                            pL += ctx.st.pa * new PLight(T_maj_ / pdf * mp.sigma_a * mp.Le / r_sum.Average());
                    }
                }
                return true;
            }

            public override bool BeginScatter(WalkContext ctx)
            {
                // Stop path sampling if maximum depth has been reached
                return depth++ < maxDepth;
            }

            public override bool OnScatterVertex(WalkContext ctx, Medium rayMedium, Point3f p, MediumProperties mp,
                                                 out MediumInteraction intr, out BSDF phaseBsdf)
            {
                phaseBsdf = default;
                intr = default;
                if (!((bool)ctx.st.beta && (bool)ctx.st.r_u))
                {
                    return false;
                }
                // Sample direct lighting at volume-scattering event
                scatterInRefractive = rayMedium is RefractiveMedium;
                if (scatterInRefractive && polarised)
                    ErrorExit("Polarised volpath does not support refractive media yet (docs/refractive-media.md, stage 7).");
                intr = new MediumInteraction(p, -ctx.ray.d, ctx.ray.time, rayMedium, mp.phase);
                if (polarised)
                {
                    SampledSpectrum Ld = vp.SampleLd(intr, null, ctx.lambda, ctx.sampler, new SampledSpectrum(1.0f), ctx.st.r_u,
                                                     ctx.stack, depth < maxDepth, out Vector3f wiLight);
                    if ((bool)Ld)
                    {
                        PLight Lp = new PLight(Ld);
                        PAttenuation M;
                        if (PolarisedPathIntegrator.PhaseAttenuation(mp.phase, intr.wo, wiLight, true, out M))
                            Lp = M * Lp;
                        pL += ctx.st.pa * Lp;
                    }
                }
                else
                {
                    EquiangularSegment seg = equiangularSegment.active ? equiangularSegment : null;
                    scatterValid = false;
                    if (seg != null)
                    {
                        seg.t = Length(p - seg.origin);
                        seg.sigmaS0 = mp.sigma_s[0];
                        seg.segL = ctx.st.segL;
                        // What the direction that the phase function samples from here needs, if it reaches an area light (OnSurfaceHit)
                        scatter.origin = seg.origin; scatter.direction = seg.direction; scatter.tMax = seg.tMax;
                        scatter.context = seg.context; scatter.r_u0 = seg.r_u0; scatter.segL = seg.segL; scatter.count = seg.count; scatter.equiangular = seg.equiangular; scatter.pdfUniform = seg.pdfUniform;
                        scatter.t = seg.t; scatter.sigmaS0 = seg.sigmaS0;
                        scatterP = p;
                        scatterValid = true;
                    }
                    L += vp.SampleLd(intr, null, ctx.lambda, ctx.sampler, ctx.st.beta, ctx.st.r_u, ctx.stack, depth < maxDepth,
                                     out Vector3f _, seg);
                }
                phaseBsdf = intr.GetBSDF();
                return true;
            }

            public override void OnPhaseSampled(WalkContext ctx, MediumInteraction intr, Point3f p, BSDFSample ps)
            {
                // Update ray path state for indirect volume scattering
                ctx.st.r_l = ctx.st.r_u / ps.pdf;
                scatterRl0 = ctx.st.r_l;
                scatterPdf = ps.pdf;
                prevIntrContext = new LightSampleContext(intr);
                curvedSegment = false;
                vertexInRefractive = scatterInRefractive;
                fneeScatterPdf = ps.pdf;
                fneeRatio = -1;
                fneeOrigin = p;
                ctx.ray.o = p;
                ctx.ray.d = ps.wi;
                specularBounce = false;
                anyNonSpecularBounces = true;
            }

            public override bool EndAfterMedium(WalkContext ctx, bool terminated)
            {
                // Handle terminated, scattered, and unscattered medium rays
                return terminated || !ctx.st.beta || !ctx.st.r_u;
            }

            public override void OnEscape(WalkContext ctx)
            {
                // Accumulate contributions from infinite light sources
                foreach (Light light in vp.infiniteLights)
                {
                    SampledSpectrum Le = light.Le(new Ray(ctx.ray), ctx.lambda);
                    if ((bool)Le)
                    {
                        if (depth == 0 || specularBounce || curvedSegment)
                        {
                            L += ctx.st.beta * Le / ctx.st.r_u.Average();
                            if (polarised)
                                pL += ctx.st.pa * new PLight(Le / ctx.st.r_u.Average());
                        }
                        else
                        {
                            // Add infinite light contribution using both PDFs with MIS
                            Float p_l = lightSampler.PMF(prevIntrContext, light) *
                                        light.PDF_Li(prevIntrContext, ctx.ray.d, true);
                            // (Fong 4.2.1) The uniform distance sample of the segment of the last scattering finds the same path
                            SampledSpectrum r_uniform = new SampledSpectrum(0.0f);
                            if (scatterValid && scatter.pdfUniform > 0)
                            {
                                SampledSpectrum rl2 = new SampledSpectrum(0.0f);
                                for (int c = 0; c < NSpectrumSamples; ++c)
                                    rl2[c] = scatterRl0[c] != 0 ? ctx.st.r_l[c] / scatterRl0[c] : 0;
                                r_uniform = scatter.r_u0 * scatter.segL * rl2 * (scatter.pdfUniform * p_l / (scatter.sigmaS0 * scatterPdf));
                            }
                            ctx.st.r_l *= p_l;
                            L += ctx.st.beta * Le / (ctx.st.r_u + ctx.st.r_l + r_uniform).Average();
                            if (polarised)
                                pL += ctx.st.pa * new PLight(Le / (ctx.st.r_u + ctx.st.r_l).Average());
                        }
                    }
                }
            }

            public override void OnSurfaceHit(WalkContext ctx, SurfaceInteraction isect)
            {
                SampledSpectrum LeSurf = isect.Le(-ctx.ray.d, ctx.lambda);
                if ((bool)LeSurf)
                {
                    // Add contribution of emission from intersected surface
                    if (curvedSegment && vertexInRefractive && vp.refractiveNee && depth > 0 && !specularBounce)
                    {
                        // Found by next event estimation along a curved path from the vertex (SampleLdRefractive)
                    }
                    else if (depth == 0 || specularBounce || curvedSegment)
                    {
                        L += ctx.st.beta * LeSurf / ctx.st.r_u.Average();
                        if (polarised)
                            pL += ctx.st.pa * new PLight(LeSurf / ctx.st.r_u.Average());
                    }
                    else
                    {
                        // Add surface light contribution using both PDFs with MIS
                        Light areaLight = isect.areaLight;
                        Float p_l = lightSampler.PMF(prevIntrContext, areaLight) *
                                    areaLight.PDF_Li(prevIntrContext, ctx.ray.d, true);
                        // (not in pbrt-v4) The third way to this path: equiangular sampling from the start of the segment of the vertex
                        // that the direction came from, toward a point of the light (see SampleLd)
                        SampledSpectrum r_equiangular = new SampledSpectrum(0.0f);
                        DiffuseAreaLight equiangularArea = areaLight as DiffuseAreaLight;
                        SampledSpectrum rl2Scatter = new SampledSpectrum(0.0f);
                        for (int c = 0; c < NSpectrumSamples; ++c)
                            rl2Scatter[c] = scatterRl0[c] != 0 ? ctx.st.r_l[c] / scatterRl0[c] : 0;
                        if (scatterValid && scatter.equiangular && equiangularArea != null && equiangularArea.SupportsEquiangular())
                        {
                            Point3f y = isect.p();
                            Vector3f w = y - scatterP;
                            Float r2 = LengthSquared(w);
                            Float cosY = r2 > 0 ? AbsDot(isect.n, w / MathF.Sqrt(r2)) : 0;
                            if (cosY > 0)
                            {
                                SampledSpectrum rl2 = new SampledSpectrum(0.0f);
                                for (int c = 0; c < NSpectrumSamples; ++c)
                                    rl2[c] = scatterRl0[c] != 0 ? ctx.st.r_l[c] / scatterRl0[c] : 0;
                                Float pmfEquiangular = vp.PmfEquiangularOrZero(scatter.context, areaLight);
                                Float pdfDistance = EquiangularSampling.Pdf(scatter.origin, scatter.direction, scatter.tMax, y, scatter.t);
                                Float pdfArea = equiangularArea.PdfArea();
                                r_equiangular = scatter.r_u0 * scatter.segL * rl2 *
                                                (scatter.count * pmfEquiangular * pdfDistance * pdfArea * r2 / cosY / (scatter.sigmaS0 * scatterPdf));
                            }
                        }
                        // (Fong 4.2.1) The uniform distance sample of the segment finds the same path with the walk's connection
                        if (scatterValid && scatter.pdfUniform > 0)
                        {
                            r_equiangular += scatter.r_u0 * scatter.segL * rl2Scatter *
                                             (scatter.pdfUniform * p_l / (scatter.sigmaS0 * scatterPdf));
                        }
                        scatterValid = false;
                        ctx.st.r_l *= p_l;
                        L += ctx.st.beta * LeSurf / (ctx.st.r_u + ctx.st.r_l + r_equiangular).Average();
                        if (polarised)
                            pL += ctx.st.pa * new PLight(LeSurf / (ctx.st.r_u + ctx.st.r_l).Average());
                    }
                }
            }

            public override bool OnBSDF(WalkContext ctx, ShapeIntersection si, SurfaceInteraction isect, BSDF bsdf)
            {
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

                    visibleSurf.Assign(new VisibleSurface(isect, albedo, ctx.lambda));
                }

                // Terminate path if maximum depth reached
                if (depth++ >= maxDepth)
                    return false;
                if (MediumTracking.Current(ctx.stack) is RefractiveMedium)
                    ErrorExit("A surface inside a refractive medium is not supported (docs/refractive-media.md).");

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
                    if (polarised)
                        pL += ctx.st.pa * new PLight(vp.SampleLd(isect, bsdf, ctx.lambda, ctx.sampler, new SampledSpectrum(1.0f),
                                                                 ctx.st.r_u, ctx.stack, depth < maxDepth, out Vector3f _));
                    else
                        L += vp.SampleLd(isect, bsdf, ctx.lambda, ctx.sampler, ctx.st.beta, ctx.st.r_u, ctx.stack,
                                         depth < maxDepth, out Vector3f _);
                    DCHECK(IsInf(L.y(ctx.lambda)) == false);
                }
                scatterValid = false;
                prevIntrContext = new LightSampleContext(isect);
                return true;
            }

            public override bool OnSurfaceScattered(WalkContext ctx, ShapeIntersection si, SurfaceInteraction isect, BSDF bsdf,
                                                    BSDFSample bs)
            {
                Vector3f wo = isect.wo;
                curvedSegment = false;
                vertexInRefractive = false;
                // Update rescaled path probabilities for BSDF scattering
                if (bs.pdfIsProportional)
                    ctx.st.r_l = ctx.st.r_u / bsdf.PDF(wo, bs.wi);
                else
                    ctx.st.r_l = ctx.st.r_u / bs.pdf;

                DCHECK(IsInf(ctx.st.beta.y(ctx.lambda)) == false);
                fneeScatterPdf = bs.pdfIsProportional ? bsdf.PDF(wo, bs.wi) : bs.pdf;
                fneeRatio = -1;
                fneeOrigin = isect.p();
                // Update volumetric integrator path state after surface scattering
                specularBounce = bs.IsSpecular();
                anyNonSpecularBounces |= !bs.IsSpecular();
                if (bs.IsTransmission())
                    etaScale *= Sqr(bs.eta);

                // Account for attenuated subsurface scattering, if applicable
                BSSRDF bssrdf = isect.GetBSSRDF(ctx.ray, ref ctx.lambda, ctx.camera, ctx.scratchBuffer);
                if (bssrdf != null && bs.IsTransmission())
                {
                    // Sample BSSRDF probe segment to find exit point
                    Float uc = ctx.sampler.Get1D();
                    Point2f up = ctx.sampler.Get2D();
                    pstd.optional<BSSRDFProbeSegment> probeSeg = bssrdf.SampleSp(uc, up);
                    if (!probeSeg)
                        return false;

                    // Sample random intersection along BSSRDF probe segment
                    uint64_t seed = MixBits(FloatToBits((float)ctx.sampler.Get1D()));
                    WeightedReservoirSampler<SubsurfaceInteraction> interactionSampler =
                        new WeightedReservoirSampler<SubsurfaceInteraction>(seed);
                    // Intersect BSSRDF sampling ray against the scene geometry
                    Interaction baseIntr = new Interaction(probeSeg.value().p0, ctx.ray.time, (Medium)null);
                    while (true)
                    {
                        Ray r = baseIntr.SpawnRayTo(probeSeg.value().p1);
                        if (r.d == new Vector3f(0, 0, 0))
                            break;
                        pstd.optional<ShapeIntersection> siProbe = vp.Intersect(r, 1);
                        if (!siProbe)
                            break;
                        baseIntr = siProbe.value().intr;
                        if (siProbe.value().intr.material == isect.material)
                            interactionSampler.Add(new SubsurfaceInteraction(siProbe.value().intr), 1.0f);
                    }

                    if (interactionSampler.HasSample() == 0)
                        return false;

                    // Convert probe intersection to _BSSRDFSample_
                    SubsurfaceInteraction ssi = interactionSampler.GetSample();
                    BSSRDFSample bssrdfSample =
                        bssrdf.ProbeIntersectionToSample(ssi, ctx.scratchBuffer);
                    if (!bssrdfSample.Sp || !bssrdfSample.pdf)
                        return false;

                    // Update path state for subsurface scattering
                    Float pdf = interactionSampler.SampleProbability() * bssrdfSample.pdf[0];
                    ctx.st.beta *= bssrdfSample.Sp / pdf;
                    ctx.st.Apply(PAttenuation.Lambert(bssrdfSample.Sp / pdf));
                    ctx.st.r_u *= bssrdfSample.pdf / bssrdfSample.pdf[0];
                    SurfaceInteraction pi = ssi;
                    pi.wo = bssrdfSample.wo;
                    scatterValid = false;
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
                    if (polarised)
                        pL += ctx.st.pa * new PLight(vp.SampleLd(pi, Sw, ctx.lambda, ctx.sampler, new SampledSpectrum(1.0f),
                                                                 ctx.st.r_u, ctx.stack, depth < maxDepth, out Vector3f _));
                    else
                        L += vp.SampleLd(pi, Sw, ctx.lambda, ctx.sampler, ctx.st.beta, ctx.st.r_u, ctx.stack, depth < maxDepth,
                                         out Vector3f _);

                    // Sample ray for indirect subsurface scattering
                    Float uSw = ctx.sampler.Get1D();
                    pstd.optional<BSDFSample> bsSw = Sw.Sample_f(pi.wo, uSw, ctx.sampler.Get2D());
                    if (!bsSw)
                        return false;
                    ctx.st.beta *= bsSw.value().f * AbsDot(bsSw.value().wi, pi.shading.n) / bsSw.value().pdf;
                    ctx.st.Apply(PAttenuation.Lambert(bsSw.value().f * AbsDot(bsSw.value().wi, pi.shading.n) /
                                                      bsSw.value().pdf));
                    ctx.st.r_l = ctx.st.r_u / bsSw.value().pdf;
                    // Don't increment depth this time...
                    DCHECK(!IsInf(ctx.st.beta.y(ctx.lambda)));
                    specularBounce = bsSw.value().IsSpecular();
                    ctx.ray = new RayDifferential(pi.SpawnRay(bsSw.value().wi));
                    MediumTracking.CrossSurface(ref ctx.stack, pi, ctx.ray.d);
                }

                // Possibly terminate volumetric path with Russian roulette
                if (!ctx.st.beta)
                    return false;
                SampledSpectrum rrBeta = ctx.st.beta * etaScale / ctx.st.r_u.Average();
                Float uRR = ctx.sampler.Get1D();
                if (rrBeta.MaxComponentValue() < 1 && depth > 1)
                {
                    Float q = std.max<Float>(0, 1 - rrBeta.MaxComponentValue());
                    if (uRR < q)
                        return false;
                    ctx.st.SurviveRoulette(q);
                }
                return true;
            }
        }

        // (not in pbrt-v4) The radiance of the path: the scalar one, or what the sensor measures of the Stokes vector
        SampledSpectrum Finish(SampledSpectrum L, PLight pL, Vector3f cameraDirection)
        {
            if (!polarised)
                return L;
            return PolarisedPathIntegrator.Measure(pL, analyser, analyserAngle, measurement, cameraDirection, cameraUp);
        }

        // (not in pbrt-v4) Forward next event estimation of the emission of a medium (after "Line Integration for Rendering
        // Heterogeneous Emissive Volumes", Simon et al. 2017). A direction from the vertex toward the emission is
        // sampled, and the emission is integrated along the whole ray in that direction by the tracking that a path would
        // use for it: it is gathered at every collision, and the ray goes on through null collisions only. The strategy
        // and the sampling of the scattering direction find the same ray integral, so their weights differ by the
        // densities of the direction alone: (P_f + P_s) goes into the denominator, as for the other strategy.
        // stack is the BoundaryStack of the path on arrival at intr.
        SampledSpectrum SampleLdFnee(Interaction intr, BSDF? bsdf, VolumeEmissionLight volumeLight, Float pmf,
                                     SampledWavelengths lambda, Sampler sampler, SampledSpectrum beta, SampledSpectrum r_p,
                                     Point2f uLight, BoundaryStack stack, out Vector3f wiLight)
        {
            wiLight = default;
            if (!volumeLight.SampleDirection(intr.p(), uLight, sampler.Get1D(), out Vector3f wi, out Float pdfDirection))
                return new SampledSpectrum(0.0f);
            wiLight = wi;

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
                BSDF phaseBSDF = intr.AsMedium().GetBSDF();
                f_hat = phaseBSDF.f(wo, wi);
                scatterPDF = phaseBSDF.PDF(wo, wi);
            }
            if (!f_hat)
                return new SampledSpectrum(0.0f);
            Float directionPdfs = pmf * pdfDirection + scatterPDF;

            Ray ray = new Ray(intr.SpawnRay(wi));
            MediumTracking.CrossSurface(ref stack, intr, ray.d);
            SampledSpectrum L = new SampledSpectrum(0.0f), lineBeta = beta * f_hat, r_u = r_p;
            RNG rng = new RNG(Hash(sampler.Get1D()), Hash(sampler.Get1D()));
            for (int segment = 0; segment < 256; ++segment)
            {
                pstd.optional<ShapeIntersection> si = Intersect(ray);
                bool terminated = false;
                Medium rayMedium = MediumTracking.Current(stack);
                if (rayMedium != null)
                {
                    Float tMax = si ? si.value().tHit : Infinity;
                    bool emitting = rayMedium == volumeLight.Medium();
                    SampledSpectrum betaCap = lineBeta, r_uCap = r_u;
                    SampledSpectrum T_maj = rayMedium.volumeIntegrator.tracker.SampleT_maj(ray, rayMedium, tMax, sampler.Get1D(), ref rng, lambda,
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
                                    L += betaCap * T_maj_ / pdf * mp.sigma_a * mp.Le / (directionPdfs * r_e.Average());
                            }
                            // Only null collisions let the ray go on
                            Float um = sampler.Get1D();
                            if (MediumEvents.SampleEvent(mp, sigma_maj, um) != MediumEventType.Null)
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
                if (!si || ThinCoatMaterial.Blocks(si.value().intr, ray.d))
                    break;
                ray = new Ray(si.value().intr.SpawnRay(ray.d));
                MediumTracking.CrossSurface(ref stack, si.value().intr, ray.d);
            }
            return L;
        }

        // (not in pbrt-v4) A scattering point sampled on the medium segment before the walk's own: with uniform false, equiangular sampling
        // (Fong et al., sections 4.2.2 and 4.2.3): a light and a point on it are chosen from the start of the segment, then the distance
        // along it by the angle seen from the point; with uniform true, uniform distance sampling (4.2.1): the distance is uniform over
        // the segment and the light is chosen from the point, as the walk's connection does. The transmittance to the point is ratio
        // tracked with the null collisions of the walk's tracker, so that the weights against the other ways to find the path (a
        // collision, then a connection to the light, SampleLd; the direction of the phase function reaching the light; the other
        // sample) are the spectral ones: all are found with the same ratios r_u0 (the path up to the segment) and the segment's null
        // collisions, as the walk (r_u) and as a connection (r_l) samples them.
        SampledSpectrum SampleSegmentPoint(WalkContext ctx, Medium medium, EquiangularSegment segment, bool uniform)
        {
            SampledSpectrum zero = new SampledSpectrum(0.0f);
            SampledWavelengths lambda = ctx.lambda;
            Sampler sampler = ctx.sampler;
            Float uLightChoice = sampler.Get1D();
            Float uDistance = sampler.Get1D();
            Point2f uLight = sampler.Get2D();
            Light light = null;
            DiffuseAreaLight areaLight = null;
            Interaction yIntr = default;
            Float pdfArea = 0;
            Float pmfChoice = 0;
            Float t;
            Float pdfDistance;
            if (!uniform)
            {
                pstd.optional<SampledLight> sampledLight = equiangularLightSampler.Sample(segment.context, uLightChoice);
                if (!sampledLight)
                {
                    return zero;
                }
                light = sampledLight.value().light;
                pmfChoice = sampledLight.value().p;
                // (Fong 4.2.3) A point on an area light is chosen from the start of the segment
                areaLight = light as DiffuseAreaLight;
                Point3f pLight;
                if (areaLight != null)
                {
                    if (!areaLight.SampleArea(uLight, out yIntr, out pdfArea))
                    {
                        return zero;
                    }
                    pLight = yIntr.p();
                }
                else
                {
                    pstd.optional<LightLiSample> lsOrigin = light.SampleLi(segment.context, uLight, lambda, true);
                    if (!lsOrigin)
                    {
                        return zero;
                    }
                    pLight = lsOrigin.value().pLight.p();
                }
                if (!EquiangularSampling.Sample(segment.origin, segment.direction, segment.tMax, pLight, uDistance, out t, out pdfDistance))
                {
                    return zero;
                }
            }
            else
            {
                t = uDistance * segment.tMax;
                pdfDistance = segment.pdfUniform;
            }
            Point3f p = segment.origin + t * segment.direction;

            // The transmittance to the point, as the walk's tracker sees the segment
            SampledSpectrum T_seg = new SampledSpectrum(1.0f), rl_seg = new SampledSpectrum(1.0f), ru_seg = new SampledSpectrum(1.0f);
            RNG rng = new RNG(Hash(segment.origin, t), Hash(segment.direction));
            Float uTrack = rng.Uniform<Float>();
            SampledSpectrum T_maj = medium.volumeIntegrator.tracker.SampleT_maj(
                new Ray(segment.origin, segment.direction, ctx.ray.time), medium, t, uTrack, ref rng, lambda,
                (Point3f pp, MediumProperties mpp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    SampledSpectrum sigma_n = ClampZero(sigma_maj - mpp.sigma_a - mpp.sigma_s);
                    MediumEvents.RatioTrackingStep(ref T_seg, ref rl_seg, ref ru_seg, T_maj_, sigma_maj, sigma_n);
                    return (bool)T_seg;
                });
            if (!T_seg)
                return zero;
            MediumEvents.FinishSegment(ref T_seg, ref rl_seg, ref ru_seg, T_maj);
            // The walk only finds collisions where a majorant covers the ray, so a point outside them has no medium for this strategy
            // either (a grid whose density is interpolated between cell centres is positive up to half a cell beyond its bounds, where no
            // majorant is)
            if (!MajorantCovers(medium, new Ray(segment.origin, segment.direction, ctx.ray.time), segment.tMax, t, lambda))
                return zero;

            // The scattering at the point
            MediumProperties mp = medium.SamplePoint(p, lambda);
            if (!mp.sigma_s)
                return zero;
            MediumInteraction intr = new MediumInteraction(p, -segment.direction, ctx.ray.time, medium, mp.phase);
            LightSampleContext lightContext = LightContextAt(intr, false, default);
            SampledSpectrum Lemit;
            Interaction target;
            Vector3f wiLight;
            Float pmfWalk;          // the density of the walk's connection to the point, per unit solid angle (delta: the delta measure)
            if (!uniform)
            {
                if (areaLight != null)
                {
                    Vector3f d = yIntr.p() - p;
                    Float r2 = LengthSquared(d);
                    if (r2 == 0)
                        return zero;
                    wiLight = d / MathF.Sqrt(r2);
                    Float cosY = AbsDot(yIntr.n, wiLight);
                    if (cosY == 0)
                        return zero;
                    Lemit = areaLight.L(yIntr.p(), yIntr.n, yIntr.uv, -wiLight, lambda);
                    if (!Lemit)
                        return zero;
                    target = yIntr;
                    pmfWalk = PmfOrZero(lightContext, light) * areaLight.PDF_Li(lightContext, wiLight, true);
                }
                else
                {
                    pstd.optional<LightLiSample> ls = light.SampleLi(lightContext, uLight, lambda, true);
                    if (!ls || !ls.value().L || ls.value().pdf == 0)
                        return zero;
                    Lemit = ls.value().L;
                    target = ls.value().pLight;
                    wiLight = ls.value().wi;
                    pmfWalk = PmfOrZero(lightContext, light) * ls.value().pdf;
                }
            }
            else
            {
                pstd.optional<SampledLight> sampledLight = lightSampler.Sample(lightContext, uLightChoice);
                if (!sampledLight || sampledLight.value().light is VolumeEmissionLight)
                    return zero;
                light = sampledLight.value().light;
                areaLight = light as DiffuseAreaLight;
                pstd.optional<LightLiSample> ls = light.SampleLi(lightContext, uLight, lambda, true);
                if (!ls || !ls.value().L || ls.value().pdf == 0)
                    return zero;
                Lemit = ls.value().L;
                target = ls.value().pLight;
                wiLight = ls.value().wi;
                pmfWalk = sampledLight.value().p * ls.value().pdf;
            }
            BSDF phaseBSDF = intr.GetBSDF();
            SampledSpectrum f_hat = phaseBSDF.f(intr.wo, wiLight);
            if (!f_hat)
                return zero;

            // The connection from the point to the light
            BoundaryStack stack = ctx.stack;
            Ray lightRay = intr.SpawnRayTo(target);
            RNG rngLight = new RNG(Hash(lightRay.o), Hash(lightRay.d));
            SampledSpectrum T_light = ShadowRay(lightRay, intr, target, ref stack, lambda, rngLight, true,
                                                out SampledSpectrum ru_light, out SampledSpectrum rl_light);
            if (!T_light)
                return zero;

            // The ways to find the vertex and the connection, relative to the connection's hero wavelength
            SampledSpectrum r_u0 = segment.r_u0;
            SampledSpectrum r_total = r_u0 * ru_seg * rl_light * mp.sigma_s * pmfWalk;
            // The direction of the phase function reaching the light
            if (!IsDeltaLight(light.Type()))
                r_total += r_u0 * ru_seg * ru_light * mp.sigma_s * phaseBSDF.PDF(intr.wo, wiLight);
            // Equiangular sampling of the point, if the light takes part
            if (segment.equiangular && BDPT.EquiangularLight(light))
            {
                Float pdfDistanceEquiangular = uniform
                    ? EquiangularSampling.Pdf(segment.origin, segment.direction, segment.tMax, target.p(), t)
                    : pdfDistance;
                Float pointDensity = 1;
                if (areaLight != null)
                {
                    Float r2 = LengthSquared(target.p() - p);
                    Float cosY = AbsDot(target.n, wiLight);
                    pointDensity = cosY > 0 ? areaLight.PdfArea() * r2 / cosY : 0;
                }
                Float pmfEquiangular = uniform ? PmfEquiangularOrZero(segment.context, light) : pmfChoice;
                r_total += r_u0 * rl_seg * rl_light * (segment.count * pmfEquiangular * pdfDistanceEquiangular * pointDensity);
            }
            // Uniform distance sampling, with the connection the walk makes
            if (segment.pdfUniform > 0)
                r_total += r_u0 * rl_seg * rl_light * (segment.pdfUniform * pmfWalk);
            return ctx.st.beta * T_seg * mp.sigma_s * f_hat * T_light * Lemit / r_total.Average();
        }

        // (not in pbrt-v4) The probability that equiangular sampling chooses the light: it chooses among the lights it takes part with
        internal Float PmfEquiangularOrZero(LightSampleContext ctx, Light light)
        {
            Float pmf = equiangularLightSampler.PMF(ctx, light);
            return Float.IsNaN(pmf) ? 0 : pmf;
        }

        Float PmfOrZero(LightSampleContext ctx, Light light)
        {
            Float pmf = lightSampler.PMF(ctx, light);
            return Float.IsNaN(pmf) ? 0 : pmf;
        }

        internal static bool MajorantCovers(Medium medium, Ray ray, Float tMax, Float t, SampledWavelengths lambda)
        {
            RayMajorantIterator iterator = medium.SampleRay(ray, tMax, lambda, new ScratchBuffer());
            while (true)
            {
                pstd.optional<RayMajorantSegment> segment = iterator.Next();
                if (!segment)
                    return false;
                if (t >= segment.value().tMin && t <= segment.value().tMax)
                    return true;
                if (segment.value().tMin > t)
                    return false;
            }
        }

        // (not in pbrt-v4) stack is the BoundaryStack of the path on arrival at intr, before it leaves towards the light
        // volumeEmissionAllowed is not in pbrt-v4: the emission of a medium is sampled as a light inside the volume; a point
        // there is only reached by the other strategy while the depth allows an emission vertex, so the two stay a pair
        SampledSpectrum SampleLd(Interaction intr, BSDF? bsdf,
                                 SampledWavelengths lambda, Sampler sampler,
                                 SampledSpectrum beta, SampledSpectrum r_p, BoundaryStack stack, bool volumeEmissionAllowed,
                                 out Vector3f wiLight, EquiangularSegment equiangularSegment = null)
        {
            // (not in pbrt-v4) The direction of the light sample, for the polarisation of the scattering toward it
            wiLight = default;
            // Estimate light-sampled direct illumination at _intr_
            // Initialize _LightSampleContext_ for volumetric light sampling
            LightSampleContext ctx = LightContextAt(intr, bsdf.HasValue, bsdf.HasValue ? bsdf.Value.Flags() : default);

            // Sample a light source using _lightSampler_
            Float u = sampler.Get1D();
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(ctx, u);
            Point2f uLight = sampler.Get2D();
            if (!sampledLight)
                return new SampledSpectrum(0.0f);
            Light light = sampledLight.value().light;
            DCHECK(light != null && sampledLight.value().p != 0);

            // (not in pbrt-v4) From inside a refractive medium the connection to the light is a curved path
            if (!bsdf.HasValue && MediumTracking.Current(stack) is RefractiveMedium refractiveMedium)
            {
                return SampleLdRefractive(intr, light, sampledLight.value().p, ctx, uLight, lambda, beta, r_p, stack,
                                          refractiveMedium, out wiLight);
            }

            // Sample a point on the light source
            // (not in pbrt-v4) The emission of a medium is sampled as a light inside the volume
            pstd.optional<LightLiSample> ls;
            VolumeEmissionLight volumeLight = light as VolumeEmissionLight;
            if (volumeLight != null)
            {
                if (!volumeEmissionAllowed)
                    return new SampledSpectrum(0.0f);
                if (volumeLight.UsesFnee())
                    return SampleLdFnee(intr, bsdf, volumeLight, sampledLight.value().p, lambda, sampler, beta, r_p, uLight,
                                        stack, out wiLight);
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
            wiLight = wi;
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
                BSDF phaseBSDF = intr.AsMedium().GetBSDF();
                f_hat = phaseBSDF.f(wo, wi);
                scatterPDF = phaseBSDF.PDF(wo, wi);
            }
            if (!f_hat)
                return new SampledSpectrum(0.0f);

            // Declare path state variables for ray to light source
            Ray lightRay = intr.SpawnRayTo(ls.value().pLight);
            RNG rng = new RNG(Hash(lightRay.o), Hash(lightRay.d));
            // (not in pbrt-v4) The shadow ray is shared with bdpt (see Integrator.ShadowRay)
            SampledSpectrum T_ray = ShadowRay(lightRay, intr, ls.value().pLight, ref stack, lambda, rng, true,
                                              out SampledSpectrum r_u, out SampledSpectrum r_l);
            if (!T_ray)
                return new SampledSpectrum(0.0f);
            // (not in pbrt-v4) The point has to lie in the medium that emits, and the other strategy reaches it with the
            // density of a collision there: the majorant at the point, per length
            if (volumeLight != null)
            {
                if (MediumTracking.Current(stack) != volumeLight.Medium())
                    return new SampledSpectrum(0.0f);
                r_u *= volumeLight.Medium().MajorantAt(ls.value().pLight.p(), lambda);
            }
            // (not in pbrt-v4) Equiangular sampling finds the same path (this vertex, this point light) by choosing the light and then
            // the distance along the segment: its probability, with the null collisions of the walk as that way connects them
            SampledSpectrum r_equiangular = new SampledSpectrum(0.0f);
            if (equiangularSegment != null && equiangularSegment.equiangular && light.Type() == LightType.DeltaPosition && volumeLight == null)
            {
                Float pmfEquiangular = PmfEquiangularOrZero(equiangularSegment.context, light);
                Float pdfDistance = EquiangularSampling.Pdf(equiangularSegment.origin, equiangularSegment.direction,
                                                            equiangularSegment.tMax, ls.value().pLight.p(), equiangularSegment.t);
                r_equiangular = equiangularSegment.r_u0 * equiangularSegment.segL * r_l *
                                (equiangularSegment.count * pmfEquiangular * pdfDistance / equiangularSegment.sigmaS0);
            }
            // ... and toward an area light, the point on it chosen from the start of the segment (the density per unit area, in
            // the solid angle of the vertex)
            DiffuseAreaLight equiangularArea = light as DiffuseAreaLight;
            if (equiangularSegment != null && equiangularSegment.equiangular && equiangularArea != null && equiangularArea.SupportsEquiangular())
            {
                Point3f y = ls.value().pLight.p();
                Float r2 = LengthSquared(y - intr.p());
                Float cosY = AbsDot(ls.value().pLight.n, wi);
                if (cosY > 0)
                {
                    Float pmfEquiangular = PmfEquiangularOrZero(equiangularSegment.context, light);
                    Float pdfDistance = EquiangularSampling.Pdf(equiangularSegment.origin, equiangularSegment.direction,
                                                                equiangularSegment.tMax, y, equiangularSegment.t);
                    Float pdfArea = equiangularArea.PdfArea();
                    r_equiangular = equiangularSegment.r_u0 * equiangularSegment.segL * r_l *
                                    (equiangularSegment.count * pmfEquiangular * pdfDistance * pdfArea * r2 / cosY / equiangularSegment.sigmaS0);
                }
            }
            // (Fong 4.2.1) ... and by a uniform distance on the segment, with the same connection
            if (equiangularSegment != null && equiangularSegment.pdfUniform > 0 && volumeLight == null)
            {
                r_equiangular += equiangularSegment.r_u0 * equiangularSegment.segL * r_l *
                                 (equiangularSegment.pdfUniform * p_l / equiangularSegment.sigmaS0);
            }
            // Return path contribution function estimate for direct lighting
            r_l *= r_p * p_l;
            r_u *= r_p * scatterPDF;
            if (IsDeltaLight(light.Type()))
            {
                return beta * f_hat * T_ray * ls.value().L / (r_l + r_equiangular).Average();
            }
            else
                return beta * f_hat * T_ray * ls.value().L / (r_l + r_u + r_equiangular).Average();
        }

        // (not in pbrt-v4) Next event estimation from a scattering point inside a refractive medium (docs/refractive-media.md,
        // Pediredla et al. section 5): the path to the point sampled on an area light is found by the connection solver, with
        // the number of searches that find it as the estimate of one over its probability, and the density of the point
        // becomes one on the direction at the vertex through the Jacobian of the end of the path. Only this strategy finds area
        // lights from such a vertex (the hook gives their emission seen by the sampled direction no weight), and the walk alone
        // finds the infinite lights, so the estimate is not divided by a sum of strategies.
        SampledSpectrum SampleLdRefractive(Interaction intr, Light light, Float pmf, LightSampleContext ctx, Point2f uLight,
                                           SampledWavelengths lambda, SampledSpectrum beta, SampledSpectrum r_p,
                                           BoundaryStack stack, RefractiveMedium medium, out Vector3f wiLight)
        {
            wiLight = default;
            SampledSpectrum zero = new SampledSpectrum(0.0f);
            if (!refractiveNee || light is VolumeEmissionLight || light.Type() == LightType.Infinite)
                return zero;
            if (IsDeltaLight(light.Type()))
                ErrorExit("Delta lights are not supported with the next event estimation from inside a refractive medium " +
                          "(docs/refractive-media.md): use area lights.");
            pstd.optional<LightLiSample> ls = light.SampleLi(ctx, uLight, lambda, true);
            if (!ls || !ls.value().L || ls.value().pdf == 0)
                return zero;
            Point3f x = intr.p();
            Point3f y = ls.value().pLight.p();
            if (medium.Contains(y))
                ErrorExit("A light inside a refractive medium is not supported (docs/refractive-media.md).");
            Float distance2 = DistanceSquared(x, y);
            Float cosineStraight = AbsDot(ls.value().pLight.n, ls.value().wi);
            if (cosineStraight == 0 || distance2 == 0)
                return zero;
            // The density of the point on the light, per area
            Float pdfArea = ls.value().pdf * cosineStraight / distance2;

            RNG rng = new RNG(Hash(x), Hash(y));
            RefractiveConnectionOptions options = medium.ConnectionOptions(Distance(x, y));
            if (!medium.ConnectToPoint(x, y, options, ref rng, out RefractiveConnectionPath path, out int count))
                return zero;
            Float cosineEnd = AbsDot(ls.value().pLight.n, path.endDirection);
            if (cosineEnd == 0 || path.areaJacobian == 0)
                return zero;
            Vector3f wo = intr.wo, wi = path.startDirection;
            wiLight = wi;
            BSDF phaseBSDF = intr.AsMedium().GetBSDF();
            SampledSpectrum f_hat = phaseBSDF.f(wo, wi);
            if (!f_hat)
                return zero;
            // The emission toward the vertex, along the end of the path
            SampledSpectrum Le = light.L(y, ls.value().pLight.n, ls.value().pLight.uv, -path.endDirection, lambda);
            if (!Le)
                return zero;

            // The transmittance of the curved part, by the tracking of the medium along the arc length
            SampledSpectrum T_ray = new SampledSpectrum(1.0f), rl = new SampledSpectrum(1.0f), ru = new SampledSpectrum(1.0f);
            Float uT = rng.Uniform<Float>();
            SampledSpectrum T_maj = medium.SampleT_majCurved(x, wi, path.length, uT, ref rng, lambda,
                (Point3f p, Vector3f tangent, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                {
                    SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                    medium.volumeIntegrator.transmittance.Step(ref T_ray, ref rl, ref ru, T_maj_, sigma_maj, sigma_n, ref rng);
                    return (bool)T_ray;
                }, out Point3f exitPoint, out Vector3f exitDirection, out Float exitLength);
            medium.volumeIntegrator.transmittance.Finish(ref T_ray, ref rl, ref ru, T_maj);
            if (!T_ray)
                return zero;

            // The straight part from the boundary to the light: found from the surface that bounds the medium, which the shadow ray
            // crosses first
            Float diagonal = Length(medium.Bounds().pMax - medium.Bounds().pMin);
            Interaction boundaryIntr = intr;
            Ray lightRay;
            if (exitLength < 1e-4f * diagonal)
            {
                // Within a few rounding errors of the vertex: no surface to find from a distance, the medium is left here
                stack.Pop(new BoundaryStackElement(null, medium), MediumTracking.interfacePriority);
                lightRay = new Ray(exitPoint, ls.value().pLight.p() - exitPoint);
            }
            else
            {
                Float back = Math.Min(1e-3f * diagonal, 0.5f * exitLength);
                pstd.optional<ShapeIntersection> boundary = Intersect(new Ray(exitPoint - back * exitDirection, exitDirection), 4 * back);
                if (!boundary)
                    return zero;
                boundaryIntr = boundary.value().intr;
                lightRay = boundaryIntr.SpawnRayTo(ls.value().pLight);
            }
            SampledSpectrum T_out = ShadowRay(lightRay, boundaryIntr, ls.value().pLight, ref stack, lambda, rng, true,
                                              out SampledSpectrum ruOut, out SampledSpectrum rlOut);
            if (!T_out)
                return zero;
            T_ray *= T_out;
            rl *= rlOut;

            // The density of the sample per direction at the vertex, and the count in place of the probability of the path
            Float p_l = pmf * pdfArea * path.areaJacobian / cosineEnd / count;
            rl *= r_p * p_l;
            return beta * f_hat * T_ray * Le / rl.Average();
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
            bool polarised = parameters.GetOneBool("polarised", false);
            PolarisedPathIntegrator.ParseSensor(parameters, loc, out bool analyser, out Float analyserAngle,
                                                out PolarisedPathIntegrator.Measurement measurement);
            bool equiangular = parameters.GetOneBool("equiangular", false);
            int equiangularSamples = parameters.GetOneInt("equiangularsamples", 1);
            int equiangularMultipleDepth = parameters.GetOneInt("equiangulardepth", int.MaxValue);
            return new VolPathIntegrator(maxDepth, camera, sampler, aggregate,
                                         lights, lightStrategy, regularize, polarised, analyser, analyserAngle, measurement,
                                         equiangular, equiangularSamples, equiangularMultipleDepth,
                                         parameters.GetOneBool("uniformdistance", false), parameters.GetOneBool("refractivenee", true));
        }

        // VolPathIntegrator Private Members
        int maxDepth;
        LightSampler lightSampler;
        LightSampler equiangularLightSampler;
        bool regularize;
        bool equiangular;
        int equiangularSamples, equiangularMultipleDepth;
        bool uniformDistance;
        bool polarised, analyser;
        Float analyserAngle;
        PolarisedPathIntegrator.Measurement measurement;
        Vector3f cameraUp;
        bool refractiveNee;
    }

}  // namespace pbrt
