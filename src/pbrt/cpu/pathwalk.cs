// (not in pbrt-v4) cpu/pathwalk.cs
//
// The random walk that volpath and bdpt share, after LuxCore's PathTracer and pbrt-v4's own BDPT::RandomWalk(): one loop that
// intersects the scene, follows the ray through the medium it is in with the tracker of that medium, handles a collision as
// an absorption, a real scattering or a null collision, passes through surfaces that only mark a boundary, and samples the
// BSDF at the others. What an integrator does at each of those points (store a vertex, sample the light, gather an
// emission) is a PathWalkHook. The order in which the walk draws random numbers from the sampler is fixed here, so an
// integrator that is a function of its sampler (as Metropolis light transport needs) stays one.

using System;

namespace pbrt
{
    using static pbrt;

    // WalkContext Definition: the state of one walk, shared with the hook
    public sealed unsafe class WalkContext
    {
        public Integrator integrator;
        public SampledWavelengths lambda;
        public RayDifferential ray;
        public BoundaryStack stack;
        public Sampler sampler;
        public Camera camera;
        public ScratchBuffer scratchBuffer;
        public TransportMode mode;
        public PathState st;
    }

    // PathWalkHook Definition
    public abstract unsafe class PathWalkHook
    {
        // The top of each iteration; false ends the walk
        public abstract bool BeginIteration(WalkContext ctx);

        // A collision of the majorant, before its event is chosen; false ends the walk
        public abstract bool BeginCollision(WalkContext ctx, Medium medium, Point3f p, MediumProperties mp,
                                            SampledSpectrum sigma_maj, SampledSpectrum T_maj);

        // A real scattering was chosen, before the throughput is updated; false ends the walk
        public abstract bool BeginScatter(WalkContext ctx);

        // The throughput is updated for the scattering. The hook makes the interaction and the BSDF of its phase function;
        // false ends the walk.
        public abstract bool OnScatterVertex(WalkContext ctx, Medium medium, Point3f p, MediumProperties mp,
                                             out MediumInteraction intr, out BSDF phaseBsdf);

        // A direction was sampled from the phase function and the throughput is updated; the hook moves the ray
        public abstract void OnPhaseSampled(WalkContext ctx, MediumInteraction intr, Point3f p, BSDFSample ps);

        // After the tracker has returned; true ends the walk
        public abstract bool EndAfterMedium(WalkContext ctx, bool terminated);

        // The ray left the scene
        public abstract void OnEscape(WalkContext ctx);

        // The ray reached a surface, before its BSDF is made
        public abstract void OnSurfaceHit(WalkContext ctx, SurfaceInteraction isect);

        // The surface has a BSDF; false ends the walk
        public abstract bool OnBSDF(WalkContext ctx, ShapeIntersection si, SurfaceInteraction isect, BSDF bsdf);

        // A direction was sampled from the BSDF, the throughput is updated and the ray is spawned; false ends the walk
        public abstract bool OnSurfaceScattered(WalkContext ctx, ShapeIntersection si, SurfaceInteraction isect, BSDF bsdf,
                                                BSDFSample bs);
    }

    // PathWalk Definition
    public static unsafe class PathWalk
    {
        public static void Run(WalkContext ctx, PathWalkHook hook)
        {
            while (true)
            {
                if (!hook.BeginIteration(ctx))
                {
                    return;
                }
                bool scattered = false, terminated = false;
                // Trace a ray and sample the medium, if any
                pstd.optional<ShapeIntersection> si = ctx.integrator.Intersect(new Ray(ctx.ray));
                Medium rayMedium = MediumTracking.Current(ctx.stack);
                if (rayMedium != null)
                {
                    Float tMax = si ? si.value().tHit : Infinity;
                    RNG rng = new RNG(Hash(ctx.ray.o, tMax), Hash(ctx.ray.d));
                    Float u = ctx.sampler.Get1D();
                    SampledSpectrum T_maj = rayMedium.volumeIntegrator.tracker.SampleT_maj(
                        new Ray(ctx.ray), rayMedium, tMax, u, ref rng, ctx.lambda,
                        (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                        {
                            if (!hook.BeginCollision(ctx, rayMedium, p, mp, sigma_maj, T_maj_))
                            {
                                terminated = true;
                                return false;
                            }
                            // Randomly sample the medium event
                            Float um = ctx.sampler.Get1D();
                            MediumEventType evt = MediumEvents.SampleEvent(mp, sigma_maj, um);
                            if (evt == MediumEventType.Absorb)
                            {
                                terminated = true;
                                return false;
                            }
                            else if (evt == MediumEventType.Scatter)
                            {
                                if (!hook.BeginScatter(ctx))
                                {
                                    terminated = true;
                                    return false;
                                }
                                ctx.st.RealScatter(T_maj_, mp.sigma_s);
                                if (!hook.OnScatterVertex(ctx, rayMedium, p, mp, out MediumInteraction intr, out BSDF phaseBsdf))
                                {
                                    terminated = true;
                                    return false;
                                }
                                // Sample a direction from the phase function
                                pstd.optional<BSDFSample> ps = phaseBsdf.Sample_f(-ctx.ray.d, 0.0f, ctx.sampler.Get2D());
                                if (!ps || ps.value().pdf == 0)
                                {
                                    terminated = true;
                                    return false;
                                }
                                ctx.st.ScatterPhase(mp.phase, -ctx.ray.d, ps.value());
                                hook.OnPhaseSampled(ctx, intr, p, ps.value());
                                scattered = true;
                                return false;
                            }
                            else
                            {
                                // Null collision
                                SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                                if (ctx.st.rescaled)
                                {
                                    return ctx.st.NullCollision(T_maj_, sigma_maj, sigma_n);
                                }
                                return ctx.st.NullCollision(T_maj_, sigma_n);
                            }
                        });
                    if (hook.EndAfterMedium(ctx, terminated))
                    {
                        return;
                    }
                    if (scattered)
                    {
                        continue;
                    }
                    ctx.st.EndSegment(T_maj);
                }

                // Handle escaped rays after no medium scattering event
                if (!si)
                {
                    hook.OnEscape(ctx);
                    return;
                }

                // Handle surface interaction for path generation
                SurfaceInteraction isect = si.value().intr;
                hook.OnSurfaceHit(ctx, isect);
                // Get BSDF and skip over medium boundaries
                BSDF bsdf = isect.GetBSDF(ctx.ray, ref ctx.lambda, ctx.camera, ctx.scratchBuffer, ctx.sampler);
                if (!bsdf)
                {
                    if (ctx.st.polarised && PolarisedPathIntegrator.PassedCoat(isect, -ctx.ray.d, ctx.mode, out PolarisedLightSystem.Attenuation Ac))
                    {
                        ctx.st.Apply(Ac);
                    }
                    isect.SkipIntersection(ref ctx.ray, si.value().tHit);
                    MediumTracking.CrossSurface(ref ctx.stack, isect, ctx.ray.d);
                    continue;
                }

                if (!hook.OnBSDF(ctx, si.value(), isect, bsdf))
                {
                    return;
                }
                // Sample the BSDF at the current vertex
                Vector3f wo = isect.wo;
                Float uc = ctx.sampler.Get1D();
                pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, uc, ctx.sampler.Get2D(), ctx.mode, BxDFReflTransFlags.All);
                if (!bs)
                {
                    return;
                }
                ctx.st.ScatterSurface(bsdf, isect, wo, bs.value(), ctx.mode);
                ctx.ray = isect.SpawnRay(ctx.ray, bsdf, bs.value().wi, bs.value().flags, bs.value().eta);
                MediumTracking.CrossSurface(ref ctx.stack, isect, ctx.ray.d);
                if (!hook.OnSurfaceScattered(ctx, si.value(), isect, bsdf, bs.value()))
                {
                    return;
                }
            }
        }
    }

}  // namespace pbrt
