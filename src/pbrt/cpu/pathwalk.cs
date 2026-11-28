// (not in pbrt-v4) cpu/pathwalk.cs
//
// The random walk of bdpt (and of mlt through it), after LuxCore's PathTracer and pbrt-v4's own BDPT::RandomWalk(): one loop
// that intersects the scene, passes through surfaces that only mark a boundary, and samples the BSDF at the others. A ray
// in a medium is given to the volume of that medium (Volume.Integrate, productionvolume.cs), which answers with the next
// point of the path, a surface or a point of the volume with its phase function, and the weight of the step: the walk does
// not track collisions itself. A bidirectional renderer keeps the densities of the path in its vertices, so it asks the
// volume for the walk of collisions (VolumeContext.tracked): the volume then reports every collision (the camera subpaths
// gather the emission of a medium there) and what the segment is to the weights of the strategies. What an integrator does
// at each point (store a vertex, gather an emission) is a PathWalkHook. The random numbers of the sampler are drawn in a
// fixed order, and those of a walk through a medium come from a generator seeded by the ray, so an integrator that is a
// function of its sampler (as Metropolis light transport needs) stays one.
//
// "volpath" does not come through here: it is the path tracer of the volume framework itself (ProductionPathIntegrator).

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

        // The ray is in a medium and the segment up to tMax (the next surface, or infinity) is about to be sampled
        // A surface that only bounds media was passed: the next piece of the segment does not start at the vertex
        public virtual void OnBoundaryPassed(WalkContext ctx)
        {
        }

        public virtual void BeginSegment(WalkContext ctx, Medium medium, Float tMax)
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

    // WalkServices Definition: what the walk gives the volume that integrates a ray of it. The nearest hit is the one that
    // the walk found already; the random numbers are the sampler's; there are no light samples (the strategies of the
    // renderer are its light samples); every collision goes to the hook with the state of the path as it is there.
    public sealed unsafe class WalkServices : VolumeServices
    {
        public WalkServices(WalkContext ctx, PathWalkHook hook, Medium medium, pstd.optional<ShapeIntersection> si, Float distance)
        {
            this.ctx = ctx;
            this.hook = hook;
            this.medium = medium;
            this.si = si;
            this.distance = distance;
            saved = ctx.st;
        }

        public override bool GetNearestHit(Ray ray, out Point3f P, out Float distance, out VolumeGeometry g)
        {
            g = null;
            if (!si)
            {
                P = ray.o;
                distance = Infinity;
                return false;
            }
            distance = this.distance;
            P = ray.Apply(distance);
            g = new VolumeGeometry();
            g.isSurface = true;
            g.surface = si.value();
            return true;
        }

        public override Float GenerateRandomNumber() { return ctx.sampler.Get1D(); }

        public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, out Vector3f wi, out SampledSpectrum L,
                                                 out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
        {
            wi = default;
            L = default;
            beamTransmittance = default;
            beamR = new SampledSpectrum(1f);
            return false;
        }

        public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L)
        {
            wi = default;
            L = default;
            return false;
        }

        // The path as it is at the collision: its state before the ray, times the walk of the volume up to here
        public override void Collision(VolumeContext volumeContext, Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj)
        {
            ctx.st = saved;
            ctx.st.Scale(volumeContext.walked);
            if (ctx.st.trackSegment)
            {
                ctx.st.segU *= volumeContext.segU;
                ctx.st.segL *= volumeContext.segL;
            }
            hook.BeginCollision(ctx, medium, p, mp, sigma_maj, T_maj);
        }

        public void Restore()
        {
            ctx.st = saved;
        }

        private WalkContext ctx;
        private PathWalkHook hook;
        private Medium medium;
        private pstd.optional<ShapeIntersection> si;
        private Float distance;
        private PathState saved;
    }

    // PathWalk Definition
    public static unsafe class PathWalk
    {
        private static bool warnedScattering, warnedSpectral;

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
                    hook.BeginSegment(ctx, rayMedium, tMax);
                    // The volume of the medium integrates the ray (its direction of length one)
                    Float length = Length(ctx.ray.d);
                    Ray unit = new Ray(ctx.ray.o, ctx.ray.d / length, ctx.ray.time);
                    VolumeContext volumeContext = new VolumeContext();
                    volumeContext.SetP(ctx.ray.o);
                    volumeContext.time = ctx.ray.time;
                    volumeContext.lambda = ctx.lambda;
                    volumeContext.medium = rayMedium;
                    volumeContext.throughput = ctx.st.beta;
                    volumeContext.tracked = true;
                    // (the emission of a medium is gathered at the collisions that are reported, with the weights of the
                    // strategies: the volume leaves it out)
                    volumeContext.emissionWeight = 0;
                    WalkServices rs = new WalkServices(ctx, hook, rayMedium, si, tMax * length);
                    rs.rng = new RNG(Hash(ctx.ray.o, tMax), Hash(ctx.ray.d));
                    if (rayMedium.volumeIntegrator.scattering != VolumeScattering.Multiple && !warnedScattering)
                    {
                        warnedScattering = true;
                        Warning("bdpt and mlt follow every scattering in a medium: \"string scattering\" of a medium other than \"multiple\" is taken as \"multiple\".");
                    }
                    if (rayMedium.volumeIntegrator.spectralTracking && !warnedSpectral)
                    {
                        warnedSpectral = true;
                        Warning("bdpt and mlt weigh their strategies by the densities of the wavelength that samples: \"string spectral\" \"tracking\" of a medium is taken as \"hero\".");
                    }
                    Volume volume = new MultiScatterHeterogeneousVolume(volumeContext, rayMedium);
                    SampledSpectrum gathered, transmittance, weight;
                    Point3f P;
                    Ray leaving;
                    VolumeGeometry g;
                    volume.Integrate(rs, unit, out gathered, out transmittance, out weight, out P, out leaving, out g);
                    // The state of the path as it was before the ray (the collisions that were reported saw it as it was there)
                    rs.Restore();
                    SampledSpectrum step = weight * transmittance;
                    if (!step)
                    {
                        // (an absorption, or a walk whose weight fell to nothing)
                        terminated = true;
                    }
                    else if (g != null && !g.isSurface)
                    {
                        // A real scattering
                        if (!hook.BeginScatter(ctx))
                        {
                            terminated = true;
                        }
                        else
                        {
                            ctx.st.Scale(step);
                            if (ctx.st.trackSegment)
                            {
                                ctx.st.segU *= volumeContext.segU;
                                ctx.st.segL *= volumeContext.segL;
                                ctx.st.vtx *= volumeContext.vtx;
                            }
                            MediumProperties mp = g.properties;
                            if (!hook.OnScatterVertex(ctx, rayMedium, P, mp, out MediumInteraction intr, out BSDF phaseBsdf))
                            {
                                terminated = true;
                            }
                            else
                            {
                                // Sample a direction from the phase function
                                pstd.optional<BSDFSample> ps = phaseBsdf.Sample_f(-ctx.ray.d, 0.0f, ctx.sampler.Get2D());
                                if (!ps || ps.value().pdf == 0)
                                {
                                    terminated = true;
                                }
                                else
                                {
                                    ctx.st.ScatterPhase(mp.phase, -ctx.ray.d, ps.value());
                                    hook.OnPhaseSampled(ctx, intr, P, ps.value());
                                    scattered = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        // The ray went through to the end of the segment
                        ctx.st.Scale(step);
                        if (ctx.st.trackSegment)
                        {
                            ctx.st.segU *= volumeContext.segU;
                            ctx.st.segL *= volumeContext.segL;
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
                    hook.OnBoundaryPassed(ctx);
                    continue;
                }

                if (!hook.OnBSDF(ctx, si.value(), isect, bsdf))
                {
                    return;
                }
                // Sample the BSDF at the current vertex
                Vector3f wo = isect.wo;
                Float uc = ctx.sampler.Get1D();
                bool pathPolarised = false;
                PolarisedLightSystem.Attenuation pathMatrix = default;
                Point2f u2 = ctx.sampler.Get2D();
                pstd.optional<BSDFSample> bs = ctx.st.polarised
                    ? bsdf.Sample_f(wo, uc, u2, ctx.mode, BxDFReflTransFlags.All, out pathPolarised, out pathMatrix)
                    : bsdf.Sample_f(wo, uc, u2, ctx.mode, BxDFReflTransFlags.All);
                if (!bs)
                {
                    return;
                }
                ctx.st.ScatterSurface(bsdf, isect, wo, bs.value(), ctx.mode, pathPolarised, pathMatrix);
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
