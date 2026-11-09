// (not in pbrt-v4) tracer_volpath.cs
//
// Volumetric path tracing as a client of the UnifiedTracer: the integrator never asks whether an event is on a surface or in a
// volume. It gathers the emission the tracer found, samples a light at the event, and asks the tracer for the next direction.
// Beside VolPathIntegrator ("volpath"), as "tracervolpath". Not in this first version: equiangular, uniform and shadow-aware
// distance sampling, polarisation, subsurface scattering, the emission of media as sampled lights (medium emission is gathered by
// the walk alone), regularization, visible surfaces.

using System;
using System.Collections.Generic;
using static pbrt.pbrt;

namespace pbrt
{

    // TracerVolPathIntegrator Definition
    public sealed unsafe class TracerVolPathIntegrator : RayIntegrator
    {
        public TracerVolPathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights,
                                       string lightSampleStrategy)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
            this.lightSampler = LightSampler.Create(lightSampleStrategy, lights.ToArray(), new Allocator());
            this.tracer = new UnifiedTracer(this, camera, maxDepth);
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambdaRef, Sampler sampler,
                                           ScratchBuffer scratchBuffer, VisibleSurface visibleSurf)
        {
            SampledWavelengths lambda = lambdaRef;
            PathState state = new PathState(new SampledSpectrum(1.0f), PolarisedLightSystem.Attenuation.Identity(),
                                            PolarisedLightSystem.Light.Zero(), true, false, true);
            BoundaryStack stack = MediumTracking.Start(camera.GetMedium());
            TraceBuffers buffers = new TraceBuffers();
            SampledSpectrum L = new SampledSpectrum(0.0f);
            bool specularBounce = false;
            // The last scattering was in a refractive volume: its area lights are found by the curved connection, not by the walk
            bool vertexInRefractive = false;
            Float etaScale = 1;
            LightSampleContext previous = default;
            try
            {
                while (true)
                {
                    ScatterEvent scatter = tracer.Trace(ref ray, ref state, ref stack, sampler, ref lambda, scratchBuffer, buffers);

                    // The emission found on the way
                    for (int i = 0; i < scatter.emission.Count; ++i)
                    {
                        EmissionSample emission = scatter.emission[i];
                        if (emission.isMedium)
                        {
                            L += emission.beta * emission.radiance / emission.r_e.Average();
                        }
                        else if (emission.curved && vertexInRefractive && emission.depth > 0 && !specularBounce && !debugWalkOnly)
                        {
                            // Found by the curved next event estimation from the vertex (SampleLdRefractive)
                        }
                        else if (emission.depth == 0 || specularBounce || emission.curved || emission.light == null)
                        {
                            L += emission.beta * emission.radiance / emission.r_u.Average();
                        }
                        else
                        {
                            Float p_l = lightSampler.PMF(previous, emission.light) * emission.light.PDF_Li(previous, emission.direction, true);
                            L += emission.beta * emission.radiance / (emission.r_u + emission.r_l * p_l).Average();
                        }
                    }

                    if (scatter.kind == EventKind.Escaped)
                    {
                        foreach (Light light in infiniteLights)
                        {
                            SampledSpectrum Le = light.Le(new Ray(ray), lambda);
                            if (!(bool)Le)
                            {
                                continue;
                            }
                            if (scatter.curved && vertexInRefractive && state.depth > 0 && !specularBounce)
                            {
                                // Found by the curved next event estimation from the vertex
                            }
                            else if (state.depth == 0 || specularBounce || scatter.curved)
                            {
                                L += state.beta * Le / state.r_u.Average();
                            }
                            else
                            {
                                Float p_l = lightSampler.PMF(previous, light) * light.PDF_Li(previous, ray.d, true);
                                L += state.beta * Le / (state.r_u + state.r_l * p_l).Average();
                            }
                        }
                        break;
                    }
                    if (scatter.kind == EventKind.Terminated)
                    {
                        break;
                    }

                    // Light sampling at the event, a surface or a medium point alike
                    vertexInRefractive = MediumTracking.Current(stack) is RefractiveMedium;
                    if ((scatter.inMedium || IsNonSpecular(scatter.bsdf.Flags())) && !(debugWalkOnly && vertexInRefractive))
                    {
                        L += SampleLd(scatter, state, stack, sampler, lambda);
                    }
                    previous = scatter.inMedium ? new LightSampleContext(scatter.interaction)
                                                : new LightSampleContext(scatter.interaction.AsSurface());

                    // The next direction
                    if (!tracer.Continue(scatter, ref ray, ref state, ref stack, sampler, out BSDFSample sample))
                    {
                        break;
                    }
                    specularBounce = sample.IsSpecular();
                    if (!scatter.inMedium && sample.IsTransmission())
                    {
                        etaScale *= Sqr(sample.eta);
                    }

                    // Russian roulette
                    if (!state.beta)
                    {
                        break;
                    }
                    SampledSpectrum rrBeta = state.beta * etaScale / state.r_u.Average();
                    Float uRR = sampler.Get1D();
                    if (rrBeta.MaxComponentValue() < 1 && state.depth > 1)
                    {
                        Float q = std.max<Float>(0, 1 - rrBeta.MaxComponentValue());
                        if (uRR < q)
                        {
                            break;
                        }
                        state.SurviveRoulette(q);
                    }
                }
                return L;
            }
            finally
            {
                lambdaRef = lambda;
            }
        }

        // Next event estimation at a scattering event: the tracer samples the light (a straight sample outside the refractive volumes, a
        // curved path inside them) and this is one formula for both
        SampledSpectrum SampleLd(ScatterEvent scatter, PathState state, BoundaryStack stack, Sampler sampler, SampledWavelengths lambda)
        {
            SampledSpectrum zero = new SampledSpectrum(0.0f);
            Interaction interaction = scatter.interaction;
            bool onSurface = !scatter.inMedium;
            LightSampleContext context = LightContextAt(interaction, onSurface, onSurface ? scatter.bsdf.Flags() : default);
            Float u = sampler.Get1D();
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(context, u);
            Point2f uLight = sampler.Get2D();
            if (!sampledLight)
            {
                return zero;
            }
            Light light = sampledLight.value().light;
            if (!tracer.SampleLight(interaction, context, light, uLight, stack, lambda, out LightArrival arrival))
            {
                return zero;
            }
            Float p_l = sampledLight.value().p * arrival.pdf;
            Vector3f wo = interaction.wo;
            SampledSpectrum f_hat = scatter.bsdf.f(wo, arrival.wi);
            if (onSurface)
            {
                f_hat *= AbsDot(arrival.wi, interaction.AsSurface().shading.n);
            }
            if (!f_hat)
            {
                return zero;
            }
            Float scatterPDF = scatter.bsdf.PDF(wo, arrival.wi);
            if (!tracer.ResolveShadow(interaction, ref arrival, stack, lambda))
            {
                return zero;
            }
            SampledSpectrum r_l = arrival.r_l * state.r_u * p_l;
            if (arrival.delta || arrival.neeOnly)
            {
                return state.beta * f_hat * arrival.T * arrival.L / r_l.Average();
            }
            SampledSpectrum r_u = arrival.r_u * state.r_u * scatterPDF;
            return state.beta * f_hat * arrival.T * arrival.L / (r_l + r_u).Average();
        }

        public override string ToString()
        {
            return StringPrintf("[ TracerVolPathIntegrator maxDepth: %d lightSampler: %s ]", maxDepth, lightSampler);
        }

        public static TracerVolPathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler, Primitive aggregate,
                                                     List<Light> lights, FileLoc loc)
        {
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            string lightStrategy = parameters.GetOneString("lightsampler", "bvh");
            return new TracerVolPathIntegrator(maxDepth, camera, sampler, aggregate, lights, lightStrategy);
        }

        // TEMPORARY: the walk alone finds the lights from a vertex in a refractive volume (an unbiased reference for the curved NEE)
        static readonly bool debugWalkOnly = Environment.GetEnvironmentVariable("REFR_WALKONLY") == "1";
        readonly int maxDepth;
        readonly LightSampler lightSampler;
        readonly UnifiedTracer tracer;
    }

}  // namespace pbrt
