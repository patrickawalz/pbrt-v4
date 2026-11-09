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
        // (not in pbrt-v4) The refractive medium of the segment that the walk is following, for the hooks of the segment (null otherwise)
        public RefractiveMedium refractive;
        // (not in pbrt-v4) The vector that the polarisation follows along a curved path (see PolarisationTransport), for polarised walks
        public PolarisationTransport transport;

        // (not in pbrt-v4) The polarisation along the edge that arrives at a vertex, if it was curved: the transport between the
        // frames at its ends, applied before the scattering of the vertex
        public void FlushTransport()
        {
            if (transport == null || !transport.active)
                return;
            if (st.polarised)
            {
                Vector3f t0 = transport.startDirection, v0 = transport.startVector, t1 = transport.tangent, v1 = transport.vector;
                st.Apply(st.fromCamera ? PolarisedLightSystem.Attenuation.Transport((-1) * t1, v1, (-1) * t0, v0)
                                       : PolarisedLightSystem.Attenuation.Transport(t0, v0, t1, v1));
            }
            transport.End();
        }
    }

    // PathWalkHook Definition
    public abstract unsafe class PathWalkHook
    {
        // The top of each iteration; false ends the walk
        public abstract bool BeginIteration(WalkContext ctx);

        // The ray is in a medium and the segment up to tMax (the next surface, or infinity) is about to be sampled
        public virtual void BeginSegment(WalkContext ctx, Medium medium, Float tMax)
        {
        }

        // The walk followed a curved path through a refractive medium (RefractiveMedium) since the last vertex: the segment is
        // not a straight line, so a connection to its far end does not find it
        public virtual void OnCurvedSegment(WalkContext ctx)
        {
        }

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
                Medium rayMedium = MediumTracking.Current(ctx.stack);
                // (not in pbrt-v4) In a refractive medium the path is curved, and the scene is not intersected along a straight ray
                RefractiveMedium refractive = rayMedium as RefractiveMedium;
                ctx.refractive = refractive;
                pstd.optional<ShapeIntersection> si = refractive != null ? default : ctx.integrator.Intersect(new Ray(ctx.ray));
                if (rayMedium != null)
                {
                    Float tMax = si ? si.value().tHit : Infinity;
                    if (refractive == null)
                    {
                        hook.BeginSegment(ctx, rayMedium, tMax);
                    }
                    RNG rng = new RNG(Hash(ctx.ray.o, tMax), Hash(ctx.ray.d));
                    Float u = ctx.sampler.Get1D();
                    Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> collision =
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
                                ctx.FlushTransport();
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
                                return ctx.st.NullCollision(T_maj_, sigma_maj, sigma_n);
                            }
                        };
                    SampledSpectrum T_maj;
                    if (refractive == null)
                    {
                        T_maj = rayMedium.volumeIntegrator.tracker.SampleT_maj(new Ray(ctx.ray), rayMedium, tMax, u, ref rng,
                                                                              ctx.lambda, collision);
                    }
                    else
                    {
                        if (ctx.transport != null && !ctx.transport.active)
                            ctx.transport.Begin(ctx.ray.d);
                        T_maj = refractive.SampleT_majCurved(ctx.ray.o, ctx.ray.d, Infinity, u, ref rng, ctx.lambda,
                            (Point3f p, Vector3f tangent, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                            {
                                // The collision sees the ray as the tangent of the path there
                                ctx.ray.o = p;
                                ctx.ray.d = tangent;
                                return collision(p, mp, sigma_maj, T_maj_);
                            }, out Point3f endPoint, out Vector3f endDirection, out Float endLength, ctx.transport,
                            (Point3f a, Point3f b) =>
                            {
                                // The surfaces of the scene meet the curve along its own chords (the boundary of the volume ends it)
                                pstd.optional<ShapeIntersection> chordHit = ctx.integrator.Intersect(new Ray(a, b - a, ctx.ray.time), 1);
                                return chordHit && chordHit.value().tHit > 1e-4f ? chordHit.value().tHit : -1;
                            });
                        // The path left the region. The bounding surface is found by a ray that ends at the end of the path, from
                        // a little before it (the path is straight to the accuracy of the step over that distance), and crossing it
                        // pops the medium. A path that ends within a few rounding errors of its start (it entered at an edge) has
                        // no distance to start from: the medium is popped here, and the surface that is then found, if any, no longer
                        // leaves a medium that is not on the stack.
                        Float diagonal = Length(refractive.Bounds().pMax - refractive.Bounds().pMin);
                        if (!scattered && !terminated)
                        {
                            if (endLength < 1e-4f * diagonal)
                            {
                                ctx.stack.RemoveMedium(refractive);
                                ctx.ray.o = endLength == 0 ? ctx.ray.o : endPoint;
                                ctx.ray.d = endLength == 0 ? ctx.ray.d : endDirection;
                                si = ctx.integrator.Intersect(new Ray(ctx.ray));
                            }
                            else
                            {
                                Float back = Math.Min(1e-3f * diagonal, 0.5f * endLength);
                                ctx.ray.o = endPoint - back * endDirection;
                                ctx.ray.d = endDirection;
                                si = ctx.integrator.Intersect(new Ray(ctx.ray));
                            }
                            hook.OnCurvedSegment(ctx);
                        }
                    }
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

                ctx.FlushTransport();
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
