// (not in pbrt-v4) cpu/mltevaluator.cs
//
// What Metropolis light transport mutates: a path that is a function of the sampler alone. The integrator keeps the chains, the
// bootstrap and the acceptance; an MLTPathEvaluator turns the values of an MLTSampler into the contribution of one path, its
// position on the film and its wavelengths. bdpt (a strategy of a given depth) and volpath (a whole walk) are two evaluators
// that share how the camera sample is drawn, how the wavelengths are drawn and how the weight of the camera ray enters.

using System;
using System.Collections.Generic;

namespace pbrt
{
    using static pbrt;

    // MLTPathEvaluator Definition
    public abstract unsafe class MLTPathEvaluator
    {
        // The streams of the sampler: the values of the camera sample and the camera subpath, of the light subpath and of the connection
        public const int cameraStreamIndex = 0;
        public const int lightStreamIndex = 1;
        public const int connectionStreamIndex = 2;
        public const int nSampleStreams = 3;

        // The number of states of the bootstrap, each a chain of its own (the depths of bdpt, a single one for volpath)
        public abstract int StateCount { get; }

        // The contribution of the path that the values of _sampler_ define in _state_, with its film position and wavelengths
        public abstract SampledSpectrum Evaluate(ScratchBuffer scratchBuffer, MLTSampler sampler, int state,
                                                 out Point2f pRaster, out SampledWavelengths lambda);

        // The wavelengths and the camera ray of a path, drawn from the sampler in this order: wavelengths, position on the film,
        // time, lens. False if the camera gives no ray. Shared by every evaluator, so they differ only in what they do with it.
        protected static bool SampleCameraRay(Camera camera, MLTSampler sampler, out Point2f pRaster,
                                              ref SampledWavelengths lambda, out RayDifferential ray,
                                              out SampledSpectrum weight)
        {
            ray = default;
            weight = default;
            // Sample wavelengths for MLT path
            if (Options.disableWavelengthJitter)
                lambda = camera.GetFilm().SampleWavelengths(0.5f);
            else
                lambda = camera.GetFilm().SampleWavelengths(sampler.Get1D());

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
                return false;
            Float rayDiffScale = std.max<Float>(.125f, 1 / std.sqrt((Float)sampler.SamplesPerPixel()));
            ray = crd.value().ray;
            ray.ScaleDifferentials(rayDiffScale);
            weight = crd.value().weight;
            return true;
        }
    }

    // BDPTPathEvaluator Definition: one connection strategy (s, t) of bidirectional path tracing, with s + t - 2 = the state
    public sealed unsafe class BDPTPathEvaluator : MLTPathEvaluator
    {
        public BDPTPathEvaluator(Integrator integrator, Camera camera, LightSampler lightSampler, int maxDepth, bool regularize,
                                 PolarisedPathIntegrator.Sensor sensor)
        {
            this.integrator = integrator;
            this.camera = camera;
            this.lightSampler = lightSampler;
            this.maxDepth = maxDepth;
            this.regularize = regularize;
            this.sensor = sensor;
            hasEmissiveMedia = integrator.lights.Exists((Light light) => light is VolumeEmissionLight);
            if (sensor != null)
                sensor.cameraUp = camera.GetCameraTransform().RenderFromCamera(new Vector3f(0, 1, 0), 0);
        }

        public override int StateCount { get { return maxDepth + 1; } }

        public override SampledSpectrum Evaluate(ScratchBuffer scratchBuffer, MLTSampler sampler, int depth,
                                                 out Point2f pRaster, out SampledWavelengths lambda)
        {
            pRaster = default;
            lambda = default;
            sampler.StartStream(cameraStreamIndex);
            // Determine the number of available strategies and pick a specific one
            int s, t, nStrategies;
            // (not in pbrt-v4) With the camera subpaths gathering the emission of media, a path of length 0 has the second
            // strategy of the light sample of a medium that connects to the camera
            bool gather = integrator.gatherMediumEmission;
            if (depth == 0)
            {
                nStrategies = hasEmissiveMedia ? 2 : 1;
                s = hasEmissiveMedia ? std.min<int>((int)(sampler.Get1D() * nStrategies), nStrategies - 1) : 0;
                t = 2 - s;
            }
            else
            {
                nStrategies = depth + 2;
                s = std.min<int>((int)(sampler.Get1D() * nStrategies), nStrategies - 1);
                t = nStrategies - s;
            }

            // Sample wavelengths and the camera ray for the MLT camera path
            if (!SampleCameraRay(camera, sampler, out pRaster, ref lambda, out RayDifferential crdRay, out SampledSpectrum _))
                return new SampledSpectrum(0.0f);

            // Generate a camera subpath with exactly _t_ vertices
            Vertex[] cameraVertices = new Vertex[t];
            for (int i = 0; i < t; ++i) cameraVertices[i] = new Vertex();
            // (not in pbrt-v4) The s = 0 strategy also takes the emission that the subpath gathers in a medium at its t - 1st
            // vertex, so the subpath may end one vertex short of t
            List<MediumEmissionEnd> emissionEnds = gather && s == 0 ? new List<MediumEmissionEnd>() : null;
            int nCamera = BDPT.GenerateCameraSubpath(integrator, crdRay, ref lambda, sampler, scratchBuffer, t, camera,
                                                     cameraVertices, regularize, sensor != null, emissionEnds);
            if (nCamera != t && !(emissionEnds != null && nCamera >= t - 1))
                return new SampledSpectrum(0.0f);

            // Generate a light subpath with exactly _s_ vertices
            sampler.StartStream(lightStreamIndex);
            Vertex[] lightVertices = new Vertex[s];
            for (int i = 0; i < s; ++i) lightVertices[i] = new Vertex();
            if (BDPT.GenerateLightSubpath(integrator, ref lambda, sampler, camera, scratchBuffer, s,
                                          cameraVertices[0].time(), lightSampler, lightVertices,
                                          regularize, sensor != null) != s)
                return new SampledSpectrum(0.0f);

            // Execute connection strategy and return the radiance estimate
            sampler.StartStream(connectionStreamIndex);
            pstd.optional<Point2f> pRasterNew = default;
            Float misWeight = 0;
            SampledSpectrum Lval = new SampledSpectrum(0.0f);
            // A light sample of a medium is the only light sample of length 0; the others are the s = 0 strategy
            if (!(s == 1 && t == 1 && !BDPT.IsVolumeEmission(lightVertices[0])))
            {
                if (nCamera == t)
                    Lval = BDPT.ConnectBDPT(integrator, ref lambda, lightVertices, cameraVertices, s, t,
                                            lightSampler, camera, sampler, ref pRasterNew,
                                            ref misWeight, sensor, -crdRay.d);
                if (emissionEnds != null)
                {
                    foreach (MediumEmissionEnd end in emissionEnds)
                    {
                        if (end.index == t - 1 && end.index <= nCamera)
                            Lval += BDPT.ConnectEmissionEnd(integrator, ref lambda, cameraVertices, end, lightSampler, camera,
                                                            sampler, sensor, -crdRay.d);
                    }
                }
            }
            Lval *= (Float)nStrategies;
            if (pRasterNew)
                pRaster = pRasterNew.value();
            return Lval;
        }

        Integrator integrator;
        Camera camera;
        LightSampler lightSampler;
        int maxDepth;
        bool regularize;
        bool hasEmissiveMedia;
        PolarisedPathIntegrator.Sensor sensor;
    }

    // VolPathPathEvaluator Definition: a whole walk of the volumetric path tracer, with its own MIS between phase function and
    // light sampling, so there is no choice of a strategy and a single state
    public sealed unsafe class VolPathPathEvaluator : MLTPathEvaluator
    {
        public VolPathPathEvaluator(VolPathIntegrator volpath, Camera camera)
        {
            this.volpath = volpath;
            this.camera = camera;
        }

        public override int StateCount { get { return 1; } }

        public override SampledSpectrum Evaluate(ScratchBuffer scratchBuffer, MLTSampler sampler, int state,
                                                 out Point2f pRaster, out SampledWavelengths lambda)
        {
            pRaster = default;
            lambda = default;
            sampler.StartStream(cameraStreamIndex);
            if (!SampleCameraRay(camera, sampler, out pRaster, ref lambda, out RayDifferential crdRay,
                                 out SampledSpectrum weight))
                return new SampledSpectrum(0.0f);
            SampledSpectrum Lval = weight * volpath.Li(crdRay, ref lambda, sampler, scratchBuffer, null);
            if (Lval.HasNaNs() || IsInf(Lval.y(lambda)))
                return new SampledSpectrum(0.0f);
            return Lval;
        }

        VolPathIntegrator volpath;
        Camera camera;
    }

}  // namespace pbrt
