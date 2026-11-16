// (not in pbrt-v4) cpu/productionpath.cs
//
// Integrator "production": the path tracer of Fong et al., "Production Volume Rendering" (SIGGRAPH 2017 course), section 3.
// It is a surface path tracer (next event estimation and BSDF sampling, combined by the power heuristic) that knows nothing
// of how a volume is integrated: when the ray is inside a surface whose material has a volume, it asks for that volume and
// calls Volume.Integrate(), which returns the next point of the path (a surface, or a point of the volume with its phase
// function), what was gathered on the way and the weight of the step; its shadow rays call Volume.Transmittance(). Each
// volume is integrated the way its medium asks ("string scattering": every scattering, one, or absorption alone), so one
// image can hold all three (productionvolume.cs).
//
// The volumes come from the materials, as on the rest of this branch (Material.GetVolume, "string volume" on a material, the
// BoundaryStack with its priorities and overlaps); a MediumInterface of pbrt-v4 is read as one.
//
//   integer maxdepth       scatterings at surfaces and in volumes (5)
//   string lightsampler    as "path"
//
// A medium whose emission is sampled as a light ("bool emission_nee" on the medium, media_emission_light.cs) is sampled here
// along lines, whatever "emission_fnee" says: from every point of the path a direction towards the emission is drawn and the
// emission is gathered along the whole ray (Volume.Emission), and the emission that the path gathers along its own ray
// (Volume.Integrate) is the other strategy for the same integral; the two share it by the densities of the direction
// (SampleEmission, VolumeContext.emissionWeight).
//
// Not here: polarisation (the path is scalar), regularisation, and the equiangular sampling of volpath.

using System;
using System.Collections.Generic;
using PLight = pbrt.PolarisedLightSystem.Light;
using PAttenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    using static pbrt;

    public sealed unsafe class ProductionPathIntegrator : RayIntegrator
    {
        public ProductionPathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights, string lightSampleStrategy)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
            lightSampler = LightSampler.Create(lightSampleStrategy, lights.ToArray(), new Allocator());
            // The media whose emission is sampled, each chosen by its power
            emissionLights = Medium.EmissionLights();
            emissionPmf = new Float[emissionLights.Count];
            emissionBounds = new Bounds3f[emissionLights.Count];
            Float total = 0;
            for (int i = 0; i < emissionLights.Count; ++i)
            {
                emissionBounds[i] = emissionLights[i].RenderBounds();
                emissionPmf[i] = emissionLights[i].Phi(SampledWavelengths.SampleUniform(0.5f)).Average();
                total += emissionPmf[i];
            }
            for (int i = 0; i < emissionLights.Count; ++i)
            {
                emissionPmf[i] = total > 0 ? emissionPmf[i] / total : 1f / emissionLights.Count;
            }
        }

        // The probability of choosing the light of the emission of a medium; 0 for a medium that is not sampled
        private Float EmissionPmf(VolumeEmissionLight light)
        {
            for (int i = 0; i < emissionLights.Count; ++i)
            {
                if (emissionLights[i] == light)
                {
                    return emissionPmf[i];
                }
            }
            return 0;
        }

        // Forward next event estimation of the emission of a medium (Simon et al. 2017; Huang et al. 2021), at a surface or at
        // a point of a volume: f times the emission along the ray of a direction drawn towards the emission, over the density
        // of the direction, over the densities of the path and of the beam; `weighted`: with the weight against the direction that the BSDF or
        // the phase function samples. The volume of each segment answers for its emission and its transmittance. The stack is
        // a copy.
        private SampledSpectrum SampleEmission(Services rs, VolumeContext ctx, Interaction intr, bool surface, BSDF bsdf, PhaseFunction phase, Sampler sampler,
                                               BoundaryStack stack, SampledSpectrum pathR, bool weighted)
        {
            SampledSpectrum emitted = new SampledSpectrum(0f);
            if (emissionLights.Count == 0)
            {
                return emitted;
            }
            Float uChoice = sampler.Get1D();
            Point2f uPoint = sampler.Get2D();
            Float uz = sampler.Get1D();
            int index = 0;
            Float cumulative = emissionPmf[0];
            while (index + 1 < emissionLights.Count && uChoice >= cumulative)
            {
                ++index;
                cumulative += emissionPmf[index];
            }
            VolumeEmissionLight light = emissionLights[index];
            Float pmf = emissionPmf[index];
            Vector3f wi;
            Float pdfDirection;
            if (!(pmf > 0) || !light.SampleDirection(intr.p(), uPoint, uz, ctx.time, out wi, out pdfDirection))
            {
                return emitted;
            }
            Vector3f wo = intr.wo;
            SampledSpectrum f;
            Float scatterPdf;
            if (surface)
            {
                f = bsdf.f(wo, wi) * AbsDot(wi, ((SurfaceInteraction)intr).shading.n);
                scatterPdf = bsdf.PDF(wo, wi);
            }
            else
            {
                f = new SampledSpectrum(phase.p(wo, wi));
                scatterPdf = phase.PDF(wo, wi);
            }
            if (!f)
            {
                return emitted;
            }
            Float p_e = pmf * pdfDirection;
            Float w = weighted ? p_e / (p_e + scatterPdf) : 1;
            Ray ray = intr.SpawnRay(wi);
            MediumTracking.CrossSurface(ref stack, intr, ray.d);
            SampledSpectrum savedR = ctx.r;
            Point3f savedP = ctx.P;
            Medium savedMedium = ctx.medium;
            ctx.r = new SampledSpectrum(1f);
            SampledSpectrum T = new SampledSpectrum(1f);
            while (true)
            {
                Medium medium = MediumTracking.Current(stack);
                if (medium != null && medium.BendsRays())
                {
                    // A volume that may bend the ray: the emission and the transmittance along the ray as it goes, to
                    // the surface that it reaches
                    pstd.optional<ShapeIntersection> ahead = Intersect(ray);
                    ctx.SetP(ray.o);
                    ctx.medium = medium;
                    SampledSpectrum reaching = pathR * ctx.r;
                    SampledSpectrum part;
                    bool bent, hit;
                    Point3f P;
                    VolumeGeometry g;
                    Ray arrival;
                    SampledSpectrum through = Volume.Create(ctx, medium).Gather(rs, ray, ahead ? ahead.value().tHit : Unbounded, medium.EmissionLight() == light && (bool)reaching,
                                                                                reaching, emissionBounds[index], out part, out bent, out hit, out P, out g, out arrival);
                    emitted += T * part;
                    T *= through;
                    if (!bent)
                    {
                        hit = (bool)ahead;
                        arrival = ray;
                    }
                    if (!hit || !T)
                    {
                        break;
                    }
                    SurfaceInteraction reached = bent ? g.surface.intr : ahead.value().intr;
                    if (ThinCoatMaterial.Blocks(reached, arrival.d))
                    {
                        break;
                    }
                    ray = reached.SpawnRay(arrival.d);
                    ray.d = Normalize(ray.d);
                    MediumTracking.CrossSurface(ref stack, reached, ray.d);
                    continue;
                }
                // (nothing more is gathered once the ray has left the box of the emission)
                Float t0, t1;
                if (!emissionBounds[index].IntersectP(ray.o, ray.d, Infinity, &t0, &t1))
                {
                    break;
                }
                pstd.optional<ShapeIntersection> si = Intersect(ray);
                if (medium != null)
                {
                    Point3f end = ray.Apply(si ? si.value().tHit : Unbounded);
                    ctx.SetP(ray.o);
                    ctx.medium = medium;
                    Volume volume = Volume.Create(ctx, medium);
                    SampledSpectrum density = pathR * ctx.r;
                    if (medium == light.Medium() && (bool)density)
                    {
                        emitted += T * volume.EmissionOver(rs, ray.o, end, density);
                    }
                    if (!si || !emissionBounds[index].IntersectP(end, ray.d, Infinity, &t0, &t1))
                    {
                        break;
                    }
                    T *= volume.Transmittance(rs, ray.o, end);
                    if (!T)
                    {
                        break;
                    }
                }
                if (!si)
                {
                    break;
                }
                SurfaceInteraction isect = si.value().intr;
                if (ThinCoatMaterial.Blocks(isect, ray.d))
                {
                    break;
                }
                ray = isect.SpawnRay(ray.d);
                MediumTracking.CrossSurface(ref stack, isect, ray.d);
            }
            ctx.r = savedR;
            ctx.SetP(savedP);
            ctx.medium = savedMedium;
            return w * f * emitted / p_e;
        }

        // Once-more scattered next-event estimation from a point of the path, in the volume there, by the implementation that
        // the volume asks for
        private SampledSpectrum OnceMore(Services rs, VolumeContext ctx, Medium medium, Volume.OnceMoreStart start, PhaseFunction proposal, VolumeJoint at)
        {
            bool pathWeights = !(medium.volumeIntegrator.tracker is DecompositionTracker);
            Medium savedMedium = ctx.medium;
            ctx.medium = medium;
            Volume volume = Volume.Create(ctx, medium);
            SampledSpectrum result = medium.volumeIntegrator.onceMore == VolumeOnceMore.OnceMore ? volume.OnceMoreShare(rs, start, proposal, at, pathWeights)
                                                                                                 : volume.OnceMoreShare2(rs, start, proposal, at, pathWeights);
            ctx.medium = savedMedium;
            return result;
        }

        // The length of a ray that leaves the scene, for the volumes that are asked between two points
        private const Float Unbounded = 1e18f;

        // The services that a volume is given: the nearest hit, random numbers, and a sample of the lights
        private sealed class Services : VolumeServices
        {
            public ProductionPathIntegrator integrator;
            public Sampler sampler;
            public BoundaryStack stack;

            public override bool GetNearestHit(Ray ray, out Point3f P, out Float distance, out VolumeGeometry g)
            {
                g = null;
                pstd.optional<ShapeIntersection> si = integrator.Intersect(ray);
                if (!si)
                {
                    P = ray.o;
                    distance = Infinity;
                    return false;
                }
                distance = si.value().tHit;
                P = ray.Apply(distance);
                g = new VolumeGeometry();
                g.isSurface = true;
                g.surface = si.value();
                return true;
            }

            public override Float GenerateRandomNumber() { return sampler.Get1D(); }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, out Vector3f wi, out SampledSpectrum L,
                                                     out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                wi = default;
                L = default;
                beamTransmittance = default;
                beamR = new SampledSpectrum(1f);
                LightSampleContext lightContext = new LightSampleContext(point);
                pstd.optional<SampledLight> sampledLight = integrator.lightSampler.Sample(lightContext, sampler.Get1D());
                Point2f uLight = sampler.Get2D();
                if (!sampledLight)
                {
                    return false;
                }
                Light light = sampledLight.value().light;
                // (the whole light by this sample alone: no part of it is left to another strategy)
                pstd.optional<LightLiSample> ls = light.SampleLi(lightContext, uLight, ctx.lambda, false);
                if (!ls || !(bool)ls.value().L || ls.value().pdf == 0)
                {
                    return false;
                }
                wi = ls.value().wi;
                Float p_l = sampledLight.value().p * ls.value().pdf;
                // (a volume that is scattered in once has no other way to the light: no heuristic)
                L = ls.value().L / p_l;
                beamTransmittance = integrator.ShadowTransmittance(this, ctx, point, ls.value().pLight, stack, out beamR, sampledLight.value().light, ls.value().L);
                if (ctx.shadowBent && IsDeltaLight(sampledLight.value().light.Type()))
                {
                    // (a light at a point or from one direction behind a volume that bends the way to it)
                    SampledSpectrum arriving;
                    if (!integrator.BentDeltaArrival(this, ctx, point, stack, sampler, sampledLight.value().light, ls.value(), out wi, out arriving, out beamTransmittance,
                                                     out beamR))
                    {
                        return false;
                    }
                    L = arriving / p_l;
                }
                return (bool)beamTransmittance;
            }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L)
            {
                wi = default;
                L = default;
                LightSampleContext lightContext = new LightSampleContext(point);
                pstd.optional<SampledLight> sampledLight = integrator.lightSampler.Sample(lightContext, sampler.Get1D());
                Point2f uLight = sampler.Get2D();
                if (!sampledLight)
                {
                    return false;
                }
                pstd.optional<LightLiSample> ls = sampledLight.value().light.SampleLi(lightContext, uLight, ctx.lambda, false);
                if (!ls || !(bool)ls.value().L || ls.value().pdf == 0)
                {
                    return false;
                }
                wi = ls.value().wi;
                L = ls.value().L / (sampledLight.value().p * ls.value().pdf);
                return true;
            }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L, out Float lightPdf,
                                                     out bool delta, out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                wi = default;
                L = default;
                lightPdf = 0;
                delta = false;
                beamTransmittance = default;
                beamR = new SampledSpectrum(1f);
                LightSampleContext lightContext = new LightSampleContext(point);
                pstd.optional<SampledLight> sampledLight = integrator.lightSampler.Sample(lightContext, sampler.Get1D());
                Point2f uLight = sampler.Get2D();
                if (!sampledLight)
                {
                    return false;
                }
                Light light = sampledLight.value().light;
                // (the whole light where the light alone is sampled; with the heuristic, the part that the other strategy may find too)
                pstd.optional<LightLiSample> ls = light.SampleLi(lightContext, uLight, ctx.lambda, ctx.medium != null && ctx.medium.volumeIntegrator.phaseSampling);
                if (!ls || !(bool)ls.value().L || ls.value().pdf == 0)
                {
                    return false;
                }
                wi = ls.value().wi;
                lightPdf = sampledLight.value().p * ls.value().pdf;
                delta = IsDeltaLight(light.Type());
                L = ls.value().L / lightPdf;
                beamTransmittance = integrator.ShadowTransmittance(this, ctx, point, ls.value().pLight, stack, out beamR, light, ls.value().L);
                if (ctx.shadowBent && delta)
                {
                    SampledSpectrum arriving;
                    if (!integrator.BentDeltaArrival(this, ctx, point, stack, sampler, light, ls.value(), out wi, out arriving, out beamTransmittance, out beamR))
                    {
                        return false;
                    }
                    L = arriving / lightPdf;
                }
                return (bool)beamTransmittance;
            }

            // The direction is followed through the volumes and the surfaces that only bound them, to the first surface that
            // stops it or out of the scene
            public override bool EvaluateLightSample(VolumeContext ctx, MediumInteraction point, Vector3f wi, Float scatterPdf, out SampledSpectrum L,
                                                     out SampledSpectrum beamTransmittance, out SampledSpectrum beamR, out SampledSpectrum emitted)
            {
                L = new SampledSpectrum(0f);
                emitted = new SampledSpectrum(0f);
                SampledSpectrum T = new SampledSpectrum(1f);
                BoundaryStack local = stack;
                LightSampleContext lightContext = new LightSampleContext(point);
                Ray ray = point.SpawnRay(wi);
                SampledSpectrum savedR = ctx.r;
                Point3f savedP = ctx.P;
                Medium savedMedium = ctx.medium;
                ctx.r = new SampledSpectrum(1f);
                bool reached = false;
                while (true)
                {
                    pstd.optional<ShapeIntersection> si = integrator.Intersect(ray);
                    Medium medium = MediumTracking.Current(local);
                    if (medium != null)
                    {
                        Point3f end = ray.Apply(si ? si.value().tHit : Unbounded);
                        ctx.SetP(ray.o);
                        ctx.medium = medium;
                        Volume volume = Volume.Create(ctx, medium);
                        if (medium.IsEmissive() && (bool)ctx.r)
                        {
                            // (a medium whose emission is also sampled from the point, SampleEmission: the share of this direction)
                            Float share = 1;
                            Float pmf = integrator.EmissionPmf(medium.EmissionLight());
                            if (pmf > 0)
                            {
                                share = scatterPdf / (scatterPdf + pmf * medium.EmissionLight().PdfDirection(point.p(), wi, ctx.time));
                            }
                            emitted += share * T * volume.Emission(this, ray.o, end) / ctx.r.Average();
                        }
                        T *= volume.Transmittance(this, ray.o, end);
                        if (!T)
                        {
                            break;
                        }
                    }
                    if (!si)
                    {
                        foreach (Light light in integrator.infiniteLights)
                        {
                            SampledSpectrum Le = light.Le(ray, ctx.lambda);
                            if ((bool)Le)
                            {
                                Float p_l = integrator.lightSampler.PMF(lightContext, light) * light.PDF_Li(lightContext, wi, true);
                                L += PowerHeuristic(1, scatterPdf, 1, p_l) * Le;
                                reached = true;
                            }
                        }
                        break;
                    }
                    SurfaceInteraction isect = si.value().intr;
                    if (ThinCoatMaterial.Blocks(isect, ray.d))
                    {
                        SampledSpectrum Le = isect.Le(-ray.d, ctx.lambda);
                        if ((bool)Le)
                        {
                            Float p_l = isect.areaLight == null ? 0 : integrator.lightSampler.PMF(lightContext, isect.areaLight) * isect.areaLight.PDF_Li(lightContext, wi, true);
                            L = PowerHeuristic(1, scatterPdf, 1, p_l) * Le;
                            reached = true;
                        }
                        break;
                    }
                    ray = isect.SpawnRay(wi);
                    MediumTracking.CrossSurface(ref local, isect, ray.d);
                }
                beamTransmittance = T;
                beamR = ctx.r;
                ctx.r = savedR;
                ctx.SetP(savedP);
                ctx.medium = savedMedium;
                return reached && (bool)T;
            }

            public override bool SampleEmission(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, SampledSpectrum r, bool weighted,
                                                out SampledSpectrum emitted)
            {
                emitted = new SampledSpectrum(0f);
                if (integrator.emissionLights.Count == 0)
                {
                    return false;
                }
                emitted = integrator.SampleEmission(this, ctx, point, false, default, phase, sampler, stack, r, weighted);
                return (bool)emitted;
            }

            // The light that the sampler of the lights chooses for where the ray starts, with a point of it
            public override bool ChooseLight(VolumeContext ctx, out VolumeLight chosen)
            {
                chosen = null;
                LightSampleContext lightContext = new LightSampleContext(new Point3fi(ctx.P), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ctx.time);
                // Half of the time by the sampler of the lights, half of the time any light alike. The sampler chooses for where
                // the ray starts, and the light serves points all along the ray: one that matters little at the start may be
                // the one that the ray passes, and what is gathered about it is divided by the probability of having chosen it.
                int count = integrator.lights.Count;
                if (count == 0)
                {
                    return false;
                }
                Float uChoice = sampler.Get1D();
                Point2f uLight = sampler.Get2D();
                Light light;
                if (uChoice < 0.5f)
                {
                    pstd.optional<SampledLight> sampledLight = integrator.lightSampler.Sample(lightContext, 2 * uChoice);
                    if (!sampledLight)
                    {
                        return false;
                    }
                    light = sampledLight.value().light;
                }
                else
                {
                    light = integrator.lights[Math.Min((int)((2 * uChoice - 1) * count), count - 1)];
                }
                chosen = new VolumeLight();
                chosen.light = light;
                chosen.probability = 0.5f * integrator.lightSampler.PMF(lightContext, light) + 0.5f / count;
                LightType type = light.Type();
                if (type != LightType.DeltaPosition && type != LightType.Area)
                {
                    return true;
                }
                pstd.optional<LightLiSample> ls = light.SampleLi(lightContext, uLight, ctx.lambda, false);
                if (ls)
                {
                    chosen.hasPoint = true;
                    chosen.fixedPoint = light.Type() == LightType.DeltaPosition;
                    chosen.point = ls.value().pLight.p();
                }
                return true;
            }

            public override bool Connect(VolumeContext ctx, MediumInteraction from, Point3f to, out SampledSpectrum beamTransmittance, out SampledSpectrum beamR,
                                         out VolumePlace place)
            {
                place = new VolumePlace();
                MediumInteraction target = new MediumInteraction(to, new Vector3f(0, 0, 1), from.time, null, null);
                beamTransmittance = integrator.ShadowTransmittance(this, ctx, from, target, stack, out beamR, out place.stack);
                place.medium = MediumTracking.Current(place.stack);
                return (bool)beamTransmittance;
            }

            public override bool ConnectBent(VolumeContext ctx, Interaction from, VolumePlace place, Point3f to, out Vector3f direction, out Float searches,
                                             out Float spread)
            {
                return integrator.FindBent(this, ctx, from, place != null ? place.stack : stack, sampler, true, to, Normalize(to - from.p()), out direction, out spread,
                                           out searches);
            }

            public override bool EvaluateLight(VolumeContext ctx, VolumeLight light, VolumePlace place, MediumInteraction point, out Vector3f wi, out SampledSpectrum L,
                                               out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                wi = default;
                L = default;
                beamTransmittance = default;
                beamR = new SampledSpectrum(1f);
                LightSampleContext lightContext = new LightSampleContext(point);
                pstd.optional<LightLiSample> ls = light.light.SampleLi(lightContext, sampler.Get2D(), ctx.lambda, false);
                if (!ls || !(bool)ls.value().L || ls.value().pdf == 0)
                {
                    return false;
                }
                wi = ls.value().wi;
                L = ls.value().L / (light.probability * ls.value().pdf);
                BoundaryStack unused;
                beamTransmittance = integrator.ShadowTransmittance(this, ctx, point, ls.value().pLight, place != null ? place.stack : stack, out beamR, out unused,
                                                                   light.light, ls.value().L);
                if (ctx.shadowBent && IsDeltaLight(light.light.Type()))
                {
                    SampledSpectrum arriving;
                    if (!integrator.BentDeltaArrival(this, ctx, point, place != null ? place.stack : stack, sampler, light.light, ls.value(), out wi, out arriving,
                                                     out beamTransmittance, out beamR))
                    {
                        return false;
                    }
                    L = arriving / light.probability;
                }
                return (bool)beamTransmittance;
            }
        }

        // The transmittance from a point of the path to a point of a light, through the volumes and the surfaces that only
        // bound them. The volume of each segment answers for it. The stack is a copy.
        private SampledSpectrum ShadowTransmittance(Services rs, VolumeContext ctx, Interaction origin, Interaction target, BoundaryStack stack, out SampledSpectrum beamR)
        {
            BoundaryStack end;
            return ShadowTransmittance(rs, ctx, origin, target, stack, out beamR, out end);
        }

        // (`end`: the stack of the volumes around the target)
        private SampledSpectrum ShadowTransmittance(Services rs, VolumeContext ctx, Interaction origin, Interaction target, BoundaryStack stack, out SampledSpectrum beamR,
                                                    out BoundaryStack end)
        {
            return ShadowTransmittance(rs, ctx, origin, target, stack, out beamR, out end, null, new SampledSpectrum(0f));
        }

        // The same for the sample of a light, where a volume on the way may bend the ray (Volume.Transmit). The ray is then
        // followed through the volumes and the surfaces that only bound them to the first surface that stops it, or out of
        // the scene, and the sample counts if that is the light it was drawn for: what is returned is the transmittance
        // along the way times what the light emits there towards the ray, over `sampled`, the radiance that the sample
        // was given (so that the caller's product with `sampled` is right). Nothing for a light at a point or from one
        // direction, which no ray lands on.
        private SampledSpectrum ShadowTransmittance(Services rs, VolumeContext ctx, Interaction origin, Interaction target, BoundaryStack stack, out SampledSpectrum beamR,
                                                    Light light, SampledSpectrum sampled)
        {
            BoundaryStack end;
            return ShadowTransmittance(rs, ctx, origin, target, stack, out beamR, out end, light, sampled);
        }

        private SampledSpectrum ShadowTransmittance(Services rs, VolumeContext ctx, Interaction origin, Interaction target, BoundaryStack stack, out SampledSpectrum beamR,
                                                    out BoundaryStack end, Light light, SampledSpectrum sampled)
        {
            beamR = new SampledSpectrum(1f);
            SampledSpectrum T = new SampledSpectrum(1f);
            Ray ray = origin.SpawnRayTo(target);
            end = stack;
            bool shadowBent = false;
            if (LengthSquared(ray.d) == 0)
            {
                return T;
            }
            MediumTracking.CrossSurface(ref stack, origin, ray.d);
            SampledSpectrum savedR = ctx.r;
            Point3f savedP = ctx.P;
            Medium savedMedium = ctx.medium;
            ctx.r = new SampledSpectrum(1f);
            while (true)
            {
                pstd.optional<ShapeIntersection> si = Intersect(ray, 1 - ShadowEpsilon);
                if (si && ThinCoatMaterial.Blocks(si.value().intr, ray.d))
                {
                    T = new SampledSpectrum(0f);
                    break;
                }
                Medium medium = MediumTracking.Current(stack);
                if (medium != null)
                {
                    Float tMax = si ? si.value().tHit : (1 - ShadowEpsilon);
                    ctx.SetP(ray.o);
                    ctx.medium = medium;
                    Float length = Length(ray.d);
                    bool bent, hit;
                    Point3f P;
                    VolumeGeometry g;
                    Ray arrival;
                    T *= Volume.Create(ctx, medium).Transmit(rs, new Ray(ray.o, ray.d / length, ray.time), tMax * length, out bent, out hit, out P, out g, out arrival);
                    if (bent)
                    {
                        shadowBent = true;
                        T = Landed(rs, ctx, stack, hit, g, arrival, T, light, sampled);
                        break;
                    }
                    if (!T)
                    {
                        break;
                    }
                }
                if (!si)
                {
                    break;
                }
                ray = si.value().intr.SpawnRayTo(target);
                MediumTracking.CrossSurface(ref stack, si.value().intr, ray.d);
            }
            end = stack;
            beamR = ctx.r;
            ctx.r = savedR;
            ctx.SetP(savedP);
            ctx.medium = savedMedium;
            ctx.shadowBent = shadowBent;
            return T;
        }

        // Where a ray that is followed through the scene ends (Follow)
        private enum LandingKind
        {
            // on a surface that stops it
            Surface,
            // out of the scene
            Escaped,
            // on the plane that it was to be stopped at
            Plane,
            // nowhere (nothing gets through, or too many surfaces were crossed)
            Lost
        }

        private struct Landing
        {
            public LandingKind kind;
            public SampledSpectrum T;
            public Point3f P;
            public Ray arrival;
            public SurfaceInteraction isect;
        }

        // A ray followed through the volumes and the surfaces that only bound them, as the volumes bend it
        // (Volume.Transmit), to the first surface that stops it, to a plane if one is given (coming from behind it), or out
        // of the scene; with the transmittance along the way if it is wanted. `pending`: the ray has been carried through a
        // first volume already, which bent it, and it is where that volume left it (hit, g, arrival, with the transmittance T).
        private Landing Follow(Services rs, VolumeContext ctx, BoundaryStack stack, Ray ray, bool plane, Point3f planePoint, Vector3f planeNormal, bool wantT,
                               bool pending, bool hit, VolumeGeometry g, Ray arrival, SampledSpectrum T)
        {
            Landing landing = new Landing();
            landing.kind = LandingKind.Lost;
            landing.T = new SampledSpectrum(0f);
            bool savedStop = ctx.stopAtPlane, savedOnly = ctx.geometryOnly;
            Point3f savedP = ctx.P;
            Medium savedMedium = ctx.medium;
            ctx.stopAtPlane = plane;
            ctx.stopPoint = planePoint;
            ctx.stopNormal = planeNormal;
            ctx.geometryOnly = !wantT;
            ctx.atPlane = false;
            bool atPlane = false;
            Point3f P = default;
            for (int crossings = 0; crossings < 256; ++crossings)
            {
                if (!pending)
                {
                    // The next stretch of the ray: straight to the nearest surface or to the plane, or as the volume there bends it
                    pstd.optional<ShapeIntersection> si = Intersect(ray);
                    Float toPlane = Infinity;
                    if (plane)
                    {
                        Float rate = Dot(ray.d, planeNormal), behind = Dot(planePoint - ray.o, planeNormal);
                        if (rate > 0 && behind >= 0)
                        {
                            toPlane = behind / rate;
                        }
                    }
                    hit = (bool)si && si.value().tHit <= toPlane;
                    atPlane = !hit && !IsInf(toPlane);
                    arrival = ray;
                    P = atPlane ? ray.Apply(toPlane) : default;
                    g = null;
                    if (hit)
                    {
                        g = new VolumeGeometry();
                        g.isSurface = true;
                        g.surface = si.value();
                    }
                    Medium medium = MediumTracking.Current(stack);
                    bool carried = false;
                    if (medium != null)
                    {
                        ctx.SetP(ray.o);
                        ctx.medium = medium;
                        bool bent, hitBent;
                        Point3f PBent;
                        VolumeGeometry gBent;
                        Ray arrivalBent;
                        Float tMax = hit ? si.value().tHit : atPlane ? toPlane : Unbounded;
                        T *= Volume.Create(ctx, medium).Transmit(rs, ray, tMax, out bent, out hitBent, out PBent, out gBent, out arrivalBent);
                        if (bent)
                        {
                            hit = hitBent;
                            g = gBent;
                            arrival = arrivalBent;
                            atPlane = ctx.atPlane;
                            P = PBent;
                            carried = true;
                        }
                        if (!T)
                        {
                            break;
                        }
                    }
                    if (ctx.differentials && !carried && (hit || atPlane))
                    {
                        // (a straight stretch)
                        Float length = hit ? si.value().tHit : toPlane;
                        ctx.offset1 += length * ctx.turn1;
                        ctx.offset2 += length * ctx.turn2;
                    }
                }
                pending = false;
                if (atPlane)
                {
                    landing.kind = LandingKind.Plane;
                    landing.T = T;
                    landing.P = P;
                    landing.arrival = arrival;
                    break;
                }
                if (!hit)
                {
                    landing.kind = LandingKind.Escaped;
                    landing.T = T;
                    landing.arrival = arrival;
                    break;
                }
                SurfaceInteraction isect = g.surface.intr;
                if (ThinCoatMaterial.Blocks(isect, arrival.d))
                {
                    landing.kind = LandingKind.Surface;
                    landing.T = T;
                    landing.arrival = arrival;
                    landing.isect = isect;
                    landing.P = isect.p();
                    break;
                }
                // A surface that only bounds a volume: through it, straight on
                ray = isect.SpawnRay(arrival.d);
                ray.d = Normalize(ray.d);
                MediumTracking.CrossSurface(ref stack, isect, ray.d);
            }
            ctx.stopAtPlane = savedStop;
            ctx.geometryOnly = savedOnly;
            ctx.atPlane = false;
            ctx.SetP(savedP);
            ctx.medium = savedMedium;
            return landing;
        }

        // A shadow ray that a volume bent, followed from where that volume left it (a surface, or none) to the first surface
        // that stops it or out of the scene: the transmittance of the whole way times what `light` emits there towards the
        // ray, over `sampled` (ShadowTransmittance)
        private SampledSpectrum Landed(Services rs, VolumeContext ctx, BoundaryStack stack, bool hit, VolumeGeometry g, Ray arrival, SampledSpectrum T, Light light,
                                       SampledSpectrum sampled)
        {
            if (light == null)
            {
                return new SampledSpectrum(0f);
            }
            Landing landing = Follow(rs, ctx, stack, arrival, false, default, default, true, true, hit, g, arrival, T);
            if (landing.kind == LandingKind.Escaped && light.Type() == LightType.Infinite)
            {
                return landing.T * SafeDiv(light.Le(landing.arrival, ctx.lambda), sampled);
            }
            if (landing.kind == LandingKind.Surface && landing.isect.areaLight == light)
            {
                return landing.T * SafeDiv(landing.isect.Le(-landing.arrival.d, ctx.lambda), sampled);
            }
            return new SampledSpectrum(0f);
        }

        // The ray from a point of the path in a direction, with the stack of the volumes that it starts in
        private Ray Leaving(Interaction origin, Vector3f direction, BoundaryStack stack, out BoundaryStack entered)
        {
            Ray ray = origin.SpawnRay(direction);
            ray.d = Normalize(ray.d);
            entered = stack;
            MediumTracking.CrossSurface(ref entered, origin, ray.d);
            return ray;
        }

        // How far the ray that leaves a point in a direction ends from a target, through the volumes as they bend it: for a
        // target at a point, where the ray meets the plane through the point (across the straight line to it), in the two
        // coordinates of the plane; for a target that is a direction, the ray as it leaves the scene, across that direction.
        // False where the ray is stopped on the way.
        // With the derivative of the miss by the two coordinates of the direction (along a1 and a2, across it), carried
        // along the ray in closed form (VolumeContext.differentials).
        private bool Miss(Services rs, VolumeContext ctx, Interaction origin, BoundaryStack stack, Vector3f direction, bool toPoint, Point3f target, Vector3f towards,
                          Vector3f e1, Vector3f e2, Vector3f a1, Vector3f a2, out Float m1, out Float m2, out Float j11, out Float j21, out Float j12, out Float j22,
                          out Float across)
        {
            m1 = 0;
            m2 = 0;
            across = 1;
            j11 = j21 = j12 = j22 = 0;
            BoundaryStack entered;
            Ray ray = Leaving(origin, direction, stack, out entered);
            ctx.differentials = true;
            ctx.offset1 = ctx.offset2 = new Vector3f(0, 0, 0);
            ctx.turn1 = a1;
            ctx.turn2 = a2;
            Landing landing = Follow(rs, ctx, entered, ray, toPoint, target, towards, false, false, false, null, default, new SampledSpectrum(1f));
            ctx.differentials = false;
            if (toPoint)
            {
                if (landing.kind != LandingKind.Plane)
                {
                    return false;
                }
                m1 = Dot(landing.P - target, e1);
                m2 = Dot(landing.P - target, e2);
                // (the changes of the point, taken along the ray onto the plane)
                Float rate = Dot(landing.arrival.d, towards);
                // (the plane is not across the ray as it arrives: the area across it is less by this)
                across = std.abs(rate);
                Vector3f p1 = ctx.offset1 - landing.arrival.d * (Dot(ctx.offset1, towards) / rate), p2 = ctx.offset2 - landing.arrival.d * (Dot(ctx.offset2, towards) / rate);
                j11 = Dot(p1, e1);
                j21 = Dot(p1, e2);
                j12 = Dot(p2, e1);
                j22 = Dot(p2, e2);
                return true;
            }
            if (landing.kind != LandingKind.Escaped || !(Dot(landing.arrival.d, towards) > 0))
            {
                return false;
            }
            m1 = Dot(landing.arrival.d, e1);
            m2 = Dot(landing.arrival.d, e2);
            j11 = Dot(ctx.turn1, e1);
            j21 = Dot(ctx.turn1, e2);
            j12 = Dot(ctx.turn2, e1);
            j22 = Dot(ctx.turn2, e2);
            return true;
        }

        // The direction in which a ray from a point of the path, bent by the volumes on its way, reaches a point (or leaves
        // the scene in a direction): Newton's method on the two coordinates of the direction, from a first direction, with
        // the derivative carried along the ray, and steps that are halved until the miss falls. `spread`: the size of the derivative's determinant at the solution -- the area
        // across the ray at the target per solid angle at the start (the squared distance, where nothing bends the ray), or
        // the solid angle of the directions of leaving per solid angle at the start (one).
        private bool SolveConnection(Services rs, VolumeContext ctx, Interaction origin, BoundaryStack stack, bool toPoint, Point3f target, Vector3f towards,
                                     Vector3f first, out Vector3f direction, out Float spread)
        {
            direction = Normalize(first);
            spread = 0;
            Vector3f e1, e2;
            CoordinateSystem(towards, &e1, &e2);
            Float scale = toPoint ? Distance(origin.p(), target) : 1;
            // (as near as the single precision of the points of the scene allows)
            Float tolerance = toPoint ? 2e-5f * scale + 4e-6f * (1 + Length((Vector3f)target)) : 2e-5f;
            Vector3f a1, a2;
            CoordinateSystem(direction, &a1, &a2);
            Float m1, m2, j11, j21, j12, j22, across;
            if (!Miss(rs, ctx, origin, stack, direction, toPoint, target, towards, e1, e2, a1, a2, out m1, out m2, out j11, out j21, out j12, out j22, out across))
            {
                return false;
            }
            for (int iteration = 0; iteration < 16; ++iteration)
            {
                Float determinant = j11 * j22 - j12 * j21;
                if (!(std.abs(determinant) > 0) || IsNaN(determinant))
                {
                    return false;
                }
                if (m1 * m1 + m2 * m2 < tolerance * tolerance)
                {
                    spread = std.abs(determinant) * across;
                    return true;
                }
                Float du = -(j22 * m1 - j12 * m2) / determinant, dv = -(-j21 * m1 + j11 * m2) / determinant;
                // (no further than a quarter of a radian at a step)
                Float step = std.sqrt(du * du + dv * dv);
                if (step > 0.25f)
                {
                    du *= 0.25f / step;
                    dv *= 0.25f / step;
                }
                // The step, halved until the ray gets through and misses by less
                bool stepped = false;
                for (int halving = 0; halving < 6; ++halving)
                {
                    Vector3f trial = Normalize(direction + du * a1 + dv * a2);
                    Vector3f b1, b2;
                    CoordinateSystem(trial, &b1, &b2);
                    Float n1, n2, k11, k21, k12, k22, slant;
                    if (Miss(rs, ctx, origin, stack, trial, toPoint, target, towards, e1, e2, b1, b2, out n1, out n2, out k11, out k21, out k12, out k22, out slant) &&
                        n1 * n1 + n2 * n2 < m1 * m1 + m2 * m2)
                    {
                        direction = trial;
                        a1 = b1;
                        a2 = b2;
                        m1 = n1;
                        m2 = n2;
                        j11 = k11;
                        j21 = k21;
                        j12 = k12;
                        j22 = k22;
                        across = slant;
                        stepped = true;
                        break;
                    }
                    du *= 0.5f;
                    dv *= 0.5f;
                }
                if (!stepped)
                {
                    // (no step misses by less: the miss is as small as the single precision of the walk lets it be, where
                    // it is within a few times the tolerance; the search has failed otherwise)
                    if (m1 * m1 + m2 * m2 < 16 * tolerance * tolerance)
                    {
                        spread = std.abs(determinant) * across;
                        return true;
                    }
                    return false;
                }
            }
            return false;
        }

        // The light of a light at a point or from one direction at a point of the path, where a volume bends the way to
        // it: the direction that reaches the light is searched (SolveConnection) from a first direction drawn about the
        // straight one, half the time within a fiftieth of a radian and half within half a radian. There may be more than
        // one such direction; the one found is weighed by one over the probability of finding it, estimated by the number
        // of further searches until it is found again (Pediredla et al. 2020, algorithm 3; Booth 2007). The light arrives as
        //     I T (n_start / n_light)^2 / spread          (a light at a point, of intensity I towards the ray)
        //     L T (n_start / n_end)^2 / spread            (a light from one direction, of radiance L)
        // with the transmittance and the indices along the path (the volumes give them together).
        private SampledSpectrum BentDeltaLight(Services rs, VolumeContext ctx, Interaction intr, bool surface, BSDF bsdf, PhaseFunction phase, Sampler sampler,
                                               Light light, LightLiSample ls, Float lightProbability, out Vector3f found)
        {
            SampledSpectrum arriving, T, beamR;
            if (!BentDeltaArrival(rs, ctx, intr, rs.stack, sampler, light, ls, out found, out arriving, out T, out beamR))
            {
                return new SampledSpectrum(0f);
            }
            Vector3f wo = intr.wo;
            SampledSpectrum f = surface ? bsdf.f(wo, found) * AbsDot(found, ((SurfaceInteraction)intr).shading.n) : new SampledSpectrum(phase.p(wo, found));
            SampledSpectrum r = ctx.r * beamR;
            if (!f || !r)
            {
                return new SampledSpectrum(0f);
            }
            return f * arriving * T / (lightProbability * r.Average());
        }

        // The same without the scattering at the point: the direction found, what arrives along it before the transmittance
        // (the light's intensity or radiance, over `spread`, times the number of searches), the transmittance and the
        // densities of the way
        private bool BentDeltaArrival(Services rs, VolumeContext ctx, Interaction intr, BoundaryStack stack, Sampler sampler, Light light, LightLiSample ls,
                                      out Vector3f found, out SampledSpectrum arriving, out SampledSpectrum T, out SampledSpectrum beamR)
        {
            arriving = new SampledSpectrum(0f);
            T = new SampledSpectrum(0f);
            beamR = new SampledSpectrum(1f);
            found = default;
            bool toPoint = light.Type() == LightType.DeltaPosition;
            Point3f target = ls.pLight.p();
            Vector3f towards = Normalize(ls.wi);
            Float spread, searches;
            if (!FindBent(rs, ctx, intr, stack, sampler, toPoint, target, towards, out found, out spread, out searches))
            {
                return false;
            }
            return BentArrival(rs, ctx, intr, stack, light, ls, toPoint, target, towards, found, spread, searches, out arriving, out T, out beamR);
        }

        // The search of BentDeltaArrival: a direction that reaches the target, with the area (or solid angle) that the
        // directions about it spread to there and the number of searches until it was found again
        private bool FindBent(Services rs, VolumeContext ctx, Interaction intr, BoundaryStack stack, Sampler sampler, bool toPoint, Point3f target, Vector3f towards,
                              out Vector3f found, out Float spread, out Float searches)
        {
            // (the searches go on until the direction is found again; past `SureSearches` each further one is made with
            // the probability `Further` only and counts for as many more, so that the count stays right in the mean and
            // the work is bounded)
            const int SureSearches = 24;
            const Float Further = 0.75f;
            found = default;
            spread = 0;
            searches = 0;
            bool any = false;
            Float worth = 1;
            for (int search = 0; ; ++search)
            {
                if (search > SureSearches)
                {
                    if (sampler.Get1D() >= Further)
                    {
                        break;
                    }
                    worth /= Further;
                }
                // (the straight direction itself, or about it: closely, widely, or anywhere)
                Float choice = sampler.Get1D();
                Float cone = choice < 0.4f ? 0 : choice < 0.7f ? 0.1f : choice < 0.9f ? 0.5f : Pi;
                Vector3f offset = cone > 0 ? SampleUniformCone(sampler.Get2D(), std.cos(cone)) : new Vector3f(0, 0, 1);
                Frame frame = Frame.FromZ(towards);
                Vector3f first = frame.FromLocal(offset);
                Vector3f direction;
                Float s;
                bool solved = SolveConnection(rs, ctx, intr, stack, toPoint, target, towards, first, out direction, out s);
                if (!any)
                {
                    if (!solved)
                    {
                        return false;
                    }
                    any = true;
                    found = direction;
                    spread = s;
                    continue;
                }
                searches += worth;
                if (solved && Dot(direction, found) > 1 - 1e-6f)
                {
                    break;
                }
            }
            return any && searches > 0;
        }

        private bool BentArrival(Services rs, VolumeContext ctx, Interaction intr, BoundaryStack stack, Light light, LightLiSample ls, bool toPoint, Point3f target,
                                 Vector3f towards, Vector3f found, Float spread, Float searches, out SampledSpectrum arriving, out SampledSpectrum T,
                                 out SampledSpectrum beamR)
        {
            arriving = new SampledSpectrum(0f);
            T = new SampledSpectrum(0f);
            beamR = new SampledSpectrum(1f);
            // Along the path that was found, with the transmittance
            BoundaryStack entered;
            Ray ray = Leaving(intr, found, stack, out entered);
            SampledSpectrum savedR = ctx.r;
            ctx.r = new SampledSpectrum(1f);
            Landing landing = Follow(rs, ctx, entered, ray, toPoint, target, towards, true, false, false, null, default, new SampledSpectrum(1f));
            beamR = ctx.r;
            ctx.r = savedR;
            if ((toPoint && landing.kind != LandingKind.Plane) || (!toPoint && landing.kind != LandingKind.Escaped) || !landing.T)
            {
                return false;
            }
            SampledSpectrum emitted;
            if (toPoint)
            {
                // (the intensity towards the ray: what the light gives a point at the distance one back along the ray)
                LightSampleContext before = new LightSampleContext(new Point3fi(target - landing.arrival.d), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ray.time);
                pstd.optional<LightLiSample> towardsRay = light.SampleLi(before, new Point2f(0.5f, 0.5f), ctx.lambda, false);
                if (!towardsRay)
                {
                    return false;
                }
                emitted = towardsRay.value().L;
            }
            else
            {
                emitted = ls.L;
            }
            arriving = emitted * (searches / spread);
            T = landing.T;
            return true;
        }

        // Next event estimation at a surface or at a point of a volume: f L T / p, with the weight of the heuristic, divided by
        // the mean density of the path over the wavelengths
        // The weight of the light of one light at the collision of a volume that lit a point of its own from the lights
        // (VolumeDirectWeight): by a point of the light, drawn as the volume draws the point that it samples about
        private Float DirectWeight(VolumeDirectWeight direct, Light light, Sampler sampler, SampledWavelengths lambda)
        {
            if (direct == null)
            {
                return 1;
            }
            LightType type = light.Type();
            if (type != LightType.DeltaPosition && type != LightType.Area)
            {
                return 1;
            }
            LightSampleContext origin = new LightSampleContext(new Point3fi(direct.curve != null ? direct.worldOrigin : direct.ray.o), new Normal3f(0, 0, 0),
                                                                new Normal3f(0, 0, 0), direct.ray.time);
            pstd.optional<LightLiSample> pivot = light.SampleLi(origin, sampler.Get2D(), lambda, false);
            return pivot ? direct.For(true, type == LightType.DeltaPosition, pivot.value().pLight.p()) : 1;
        }

        private SampledSpectrum SampleLd(Services rs, VolumeContext ctx, Interaction intr, bool surface, BSDF bsdf, PhaseFunction phase, Sampler sampler, VolumeDirectWeight direct,
                                         out Vector3f wiLight)
        {
            LightSampleContext lightContext;
            wiLight = new Vector3f(0, 0, 1);
            if (!lightSampling)
            {
                return new SampledSpectrum(0f);
            }
            if (surface)
            {
                SurfaceInteraction isect = (SurfaceInteraction)intr;
                lightContext = new LightSampleContext(isect);
                BxDFFlags flags = bsdf.Flags();
                if (IsReflective(flags) && !IsTransmissive(flags))
                {
                    lightContext.pi = isect.OffsetRayOrigin(isect.wo);
                }
                else if (IsTransmissive(flags) && !IsReflective(flags))
                {
                    lightContext.pi = isect.OffsetRayOrigin(-isect.wo);
                }
            }
            else
            {
                lightContext = new LightSampleContext(intr);
            }
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(lightContext, sampler.Get1D());
            Point2f uLight = sampler.Get2D();
            if (!sampledLight)
            {
                return new SampledSpectrum(0f);
            }
            Light light = sampledLight.value().light;
            pstd.optional<LightLiSample> ls = light.SampleLi(lightContext, uLight, ctx.lambda, true);
            if (!ls || !(bool)ls.value().L || ls.value().pdf == 0)
            {
                return new SampledSpectrum(0f);
            }
            Vector3f wo = intr.wo, wi = ls.value().wi;
            wiLight = wi;
            SampledSpectrum f;
            Float scatterPdf;
            if (surface)
            {
                f = bsdf.f(wo, wi) * AbsDot(wi, ((SurfaceInteraction)intr).shading.n);
                scatterPdf = bsdf.PDF(wo, wi);
            }
            else
            {
                f = new SampledSpectrum(phase.p(wo, wi));
                scatterPdf = phase.PDF(wo, wi);
            }
            bool delta = IsDeltaLight(light.Type());
            if (!f && !delta)
            {
                return new SampledSpectrum(0f);
            }
            SampledSpectrum beamR;
            SampledSpectrum T = ShadowTransmittance(rs, ctx, intr, ls.value().pLight, rs.stack, out beamR, light, ls.value().L);
            if (delta && ctx.shadowBent)
            {
                // A light at a point or from one direction behind a volume that bends the way to it
                return DirectWeight(direct, light, sampler, ctx.lambda) *
                       BentDeltaLight(rs, ctx, intr, surface, bsdf, phase, sampler, light, ls.value(), sampledLight.value().p, out wiLight);
            }
            if (!T || !f)
            {
                return new SampledSpectrum(0f);
            }
            Float p_l = sampledLight.value().p * ls.value().pdf;
            Float w = IsDeltaLight(light.Type()) ? 1 : PowerHeuristic(1, p_l, 1, scatterPdf);
            SampledSpectrum r = ctx.r * beamR;
            if (!r)
            {
                return new SampledSpectrum(0f);
            }
            return DirectWeight(direct, light, sampler, ctx.lambda) * w * f * ls.value().L * T / p_l / r.Average();
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler, ScratchBuffer scratchBuffer, VisibleSurface visibleSurf)
        {
            SampledSpectrum L = new SampledSpectrum(0f), beta = new SampledSpectrum(1f);
            // The polarised path. `relative` is the product of the Mueller matrices of the path so far, each over its first
            // element (beta has those); what has been added to L since the last change of it is unpolarised light, or light
            // through one more matrix (next-event estimation), and goes into `Lp` through `relative` before it changes
            // (Polarise).
            PAttenuation relative = PAttenuation.Identity();
            PLight Lp = PLight.Zero();
            SampledSpectrum polarisedUpTo = new SampledSpectrum(0f);
            Vector3f cameraDirection = -ray.d;
            Ray cameraRay = new Ray(ray);
            int depth = 0;
            Float p_b = 0, etaScale = 1;
            // What weighs the light that arrives straight from a light at the last point of a volume, where the volume gathered
            // a share of that light by itself (VolumeGeometry.direct); null where it did not
            VolumeDirectWeight direct = null;
            // The collision of a volume that the next ray leaves from, where the volume draws two points jointly
            // (VolumeGeometry.joint); null otherwise
            VolumeJoint joint = null;
            bool specularBounce = true;
            LightSampleContext prevIntrCtx = default;
            VolumeContext ctx = new VolumeContext();
            ctx.lambda = lambda;
            ctx.time = ray.time;
            Services rs = new Services();
            rs.integrator = this;
            rs.sampler = sampler;
            rs.stack = MediumTracking.Start(camera.GetMedium());
            rs.rng = new RNG(Hash(ray.o, ray.d), Hash(sampler.Get1D()));
            // The direction that the path left its last point in: what the densities of the light sampling are of, where a
            // volume bends the ray on its way (it stays through the surfaces that only bound volumes)
            Vector3f departure = ray.d;

            while (true)
            {
                // The next point of the path: the nearest surface, or what the volume that the ray is in says
                Float length = Length(ray.d);
                Ray unit = new Ray(ray.o, ray.d / length, ray.time);

                Point3f P;
                VolumeGeometry g;
                bool hit;
                VolumeJoint pending = joint;
                VolumeJoint surfaceJoint = null;
                joint = null;
                Medium medium = MediumTracking.Current(rs.stack);
                if (medium != null)
                {
                    ctx.SetP(ray.o);
                    ctx.medium = medium;
                    ctx.lambda = lambda;
                    ctx.mayScatter = depth < maxDepth;
                    ctx.mayScatterTwice = depth + 1 < maxDepth;
                    ctx.depth = depth;
                    ctx.throughput = beta;
                    // (the point that this ray leaves from, where a volume draws two points jointly or puts one more scattering
                    // before a light: for this ray only)
                    ctx.joint = pending;
                    // The emission that the ray gathers in a medium that the last point of the path also sampled
                    ctx.emissionWeight = 1;
                    if (depth > 0 && !specularBounce && p_b > 0)
                    {
                        Float pmf = EmissionPmf(medium.EmissionLight());
                        if (pmf > 0)
                        {
                            // (by the direction that the path left its last point in, which a volume may have bent since)
                            ctx.emissionWeight = p_b / (p_b + pmf * medium.EmissionLight().PdfDirection(prevIntrCtx.p(), Normalize(departure), ray.time));
                        }
                    }
                    Volume volume = Volume.Create(ctx, medium);
                    SampledSpectrum Lv, transmittance, weight;
                    Ray wo;
                    ctx.bent = false;
                    ctx.polarised = polarised;
                    hit = volume.Integrate(rs, unit, out Lv, out transmittance, out weight, out P, out wo, out g);
                    L += beta * Lv;
                    beta *= weight * transmittance;
                    if (!beta || !ctx.r)
                    {
                        break;
                    }
                    if (ctx.bent)
                    {
                        // A volume that bent the ray: the path goes on with the ray as it arrives, and the polarisation
                        // of the light is carried along the curve (it comes the other way: it arrives along the reverse of
                        // the direction of arrival and leaves along the reverse of the direction that the ray started in)
                        ray = new RayDifferential(ctx.arrival);
                        length = 1;
                        if (polarised)
                        {
                            Polarise(ref Lp, ref polarisedUpTo, L, relative, false, default);
                            relative *= PAttenuation.Transport(-ctx.startDirection, ctx.startAxis, -ctx.arrivalDirection, ctx.arrivalAxis);
                        }
                    }
                }
                else
                {
                    Float distance;
                    hit = rs.GetNearestHit(unit, out P, out distance, out g);
                }

                if (!hit)
                {
                    foreach (Light light in infiniteLights)
                    {
                        SampledSpectrum Le0 = light.Le(new Ray(ray), lambda);
                        if (depth == 0 || specularBounce)
                        {
                            L += beta * Le0 / ctx.r.Average();
                        }
                        else
                        {
                            Float p_l = lightSampler.PMF(prevIntrCtx, light) * light.PDF_Li(prevIntrCtx, departure, true);
                            L += beta * (lightSampling ? PowerHeuristic(1, p_b, 1, p_l) : 1) * Le0 / ctx.r.Average();
                        }
                    }
                    break;
                }

                if (!g.isSurface)
                {
                    // A point of a volume: its "BSDF" is the phase function
                    if (depth++ >= maxDepth)
                    {
                        break;
                    }
                    MediumInteraction point = g.scatter;
                    direct = g.direct;
                    Vector3f wiLight;
                    if (polarised)
                    {
                        Polarise(ref Lp, ref polarisedUpTo, L, relative, false, default);
                    }
                    L += beta * SampleLd(rs, ctx, point, false, default, g.phase, sampler, direct, out wiLight);
                    if (polarised)
                    {
                        // (the light of the lights is scattered here with the matrix of the phase function for its direction)
                        PAttenuation towardsLight;
                        bool known = PolarisedPathIntegrator.PhaseAttenuation(g.phase, point.wo, wiLight, true, out towardsLight);
                        Polarise(ref Lp, ref polarisedUpTo, L, relative, known, towardsLight);
                    }
                    if (emissionLights.Count > 0)
                    {
                        L += beta * SampleEmission(rs, ctx, point, false, default, g.phase, sampler, rs.stack, ctx.r, true);
                    }
                    // One more scattering between this collision and a light at a position, where the volume asks for it
                    // (Volume.OnceMoreShare): the light of the next collision, which the path may still reach
                    if (g.joint != null && g.joint.onceMore != VolumeOnceMore.None && depth < maxDepth && !polarised)
                    {
                        Medium here = MediumTracking.Current(rs.stack);
                        if (here != null)
                        {
                            Volume.OnceMoreStart start = new Volume.OnceMoreStart();
                            start.point = point;
                            start.phase = g.phase;
                            L += beta * OnceMore(rs, ctx, here, start, g.phase, g.joint);
                        }
                    }
                    pstd.optional<PhaseFunctionSample> ps = g.phase.Sample_p(point.wo, sampler.Get2D());
                    if (!ps || ps.value().pdf == 0)
                    {
                        break;
                    }
                    joint = g.joint;
                    if (joint != null)
                    {
                        joint.phasePdf = ps.value().pdf;
                    }
                    if (polarised)
                    {
                        Polarise(ref Lp, ref polarisedUpTo, L, relative, false, default);
                        PAttenuation scattered;
                        if (PolarisedPathIntegrator.PhaseAttenuation(g.phase, point.wo, ps.value().wi, true, out scattered))
                        {
                            relative *= scattered;
                        }
                        else
                        {
                            relative *= PAttenuation.Lambert(new SampledSpectrum(1f));
                        }
                    }
                    beta *= ps.value().p / ps.value().pdf;
                    p_b = ps.value().pdf;
                    specularBounce = false;
                    prevIntrCtx = new LightSampleContext(point);
                    ray = point.SpawnRay(ps.value().wi);
                    departure = ray.d;
                }
                else
                {
                    ShapeIntersection si = g.surface;
                    SurfaceInteraction isect = si.intr;
                    // (the distance of the hit in the units of the ray that the interaction is given)
                    Float tHit = si.tHit / length;
                    SampledSpectrum Le = isect.Le(-ray.d, lambda);
                    if ((bool)Le)
                    {
                        if (depth == 0 || specularBounce || isect.areaLight == null)
                        {
                            L += beta * Le / ctx.r.Average();
                        }
                        else
                        {
                            Light areaLight = isect.areaLight;
                            Float p_l = lightSampler.PMF(prevIntrCtx, areaLight) * areaLight.PDF_Li(prevIntrCtx, departure, true);
                            L += beta * (lightSampling ? DirectWeight(direct, areaLight, sampler, ctx.lambda) * PowerHeuristic(1, p_b, 1, p_l) : 1) * Le / ctx.r.Average();
                        }
                    }
                    BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                    ctx.lambda = lambda;
                    if (!bsdf)
                    {
                        // a surface that only bounds a volume
                        if (polarised)
                        {
                            PAttenuation coat;
                            if (PolarisedPathIntegrator.PassedCoat(isect, -ray.d, TransportMode.Radiance, out coat))
                            {
                                Polarise(ref Lp, ref polarisedUpTo, L, relative, false, default);
                                relative *= coat;
                            }
                        }
                        isect.SkipIntersection(ref ray, tHit);
                        MediumTracking.CrossSurface(ref rs.stack, isect, ray.d);
                        continue;
                    }
                    if (depth++ >= maxDepth)
                    {
                        break;
                    }
                    if (IsNonSpecular(bsdf.Flags()))
                    {
                        Vector3f wiLight;
                        if (polarised)
                        {
                            Polarise(ref Lp, ref polarisedUpTo, L, relative, false, default);
                        }
                        L += beta * SampleLd(rs, ctx, isect, true, bsdf, null, sampler, null, out wiLight);
                        if (polarised)
                        {
                            PAttenuation towardsLight = default;
                            bool known = PolarisedPathIntegrator.StockRough(bsdf) &&
                                         PolarisedPathIntegrator.RoughAttenuation(bsdf, isect, -ray.d, wiLight, new SampledSpectrum(1f), TransportMode.Radiance, out towardsLight);
                            Polarise(ref Lp, ref polarisedUpTo, L, relative, known, known ? towardsLight : default);
                        }
                        // One more scattering between this point and a light at a position, in the volume that the path
                        // came through, where that volume asks for it (Volume.OnceMoreShare)
                        Medium around = MediumTracking.Current(rs.stack);
                        if (!polarised && around != null && around.volumeIntegrator.onceMore != VolumeOnceMore.None && around.volumeIntegrator.scattering == VolumeScattering.Multiple &&
                            !(around is AggregateMedium) && depth < maxDepth)
                        {
                            MediumProperties mp = around.SamplePoint(isect.p(), lambda, ray.time);
                            if (mp.phase != null)
                            {
                                surfaceJoint = new VolumeJoint();
                                surfaceJoint.side = FaceForward(isect.n, isect.wo);
                                surfaceJoint.ray = new Ray(isect.p(), -isect.wo, ray.time);
                                surfaceJoint.t = 0;
                                surfaceJoint.product = 1;
                                surfaceJoint.phase = mp.phase;
                                surfaceJoint.onceMore = around.volumeIntegrator.onceMore;
                                surfaceJoint.sided = true;
                                Volume.OnceMoreStart start = new Volume.OnceMoreStart();
                                start.point = isect;
                                start.bsdf = bsdf;
                                start.surface = true;
                                start.side = surfaceJoint.side;
                                start.place = new VolumePlace();
                                start.place.medium = around;
                                start.place.stack = rs.stack;
                                L += beta * OnceMore(rs, ctx, around, start, mp.phase, null);
                            }
                        }
                        if (emissionLights.Count > 0)
                        {
                            L += beta * SampleEmission(rs, ctx, isect, true, bsdf, null, sampler, rs.stack, ctx.r, true);
                        }
                    }
                    Vector3f wo = -ray.d;
                    Float u = sampler.Get1D();
                    pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, u, sampler.Get2D());
                    if (!bs)
                    {
                        break;
                    }
                    beta *= bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                    if (polarised)
                    {
                        // The matrix of the scattering over its first element (the weight of the path has that)
                        Polarise(ref Lp, ref polarisedUpTo, L, relative, false, default);
                        PAttenuation scattered;
                        if (PolarisedPathIntegrator.SpecularAttenuation(bsdf, isect, wo, bs.value(), TransportMode.Radiance, out scattered))
                        {
                            relative *= scattered * SafeDiv(new SampledSpectrum(1f), scattered.GetIntensity());
                        }
                        else if (PolarisedPathIntegrator.RoughAttenuation(bsdf, isect, wo, bs.value().wi, new SampledSpectrum(1f), TransportMode.Radiance, out scattered))
                        {
                            relative *= scattered;
                        }
                        else
                        {
                            relative *= PAttenuation.Lambert(new SampledSpectrum(1f));
                        }
                    }
                    p_b = bs.value().pdfIsProportional ? bsdf.PDF(wo, bs.value().wi) : bs.value().pdf;
                    specularBounce = bs.value().IsSpecular();
                    direct = null;
                    // (the ray that leaves from here is a rival of the scattering that was put between here and a light)
                    if (surfaceJoint != null && !specularBounce)
                    {
                        joint = surfaceJoint;
                        joint.phasePdf = p_b;
                    }
                    if (bs.value().IsTransmission())
                    {
                        etaScale *= Sqr(bs.value().eta);
                    }
                    prevIntrCtx = new LightSampleContext(isect);
                    ray = isect.SpawnRay(ray, bsdf, bs.value().wi, bs.value().flags, bs.value().eta);
                    departure = ray.d;
                    MediumTracking.CrossSurface(ref rs.stack, isect, ray.d);
                }

                if (!beta)
                {
                    break;
                }
                SampledSpectrum rrBeta = beta * etaScale / ctx.r.Average();
                if (rrBeta.MaxComponentValue() < 1 && depth > 1)
                {
                    Float q = std.max<Float>(0, 1 - rrBeta.MaxComponentValue());
                    if (sampler.Get1D() < q)
                    {
                        break;
                    }
                    beta /= 1 - q;
                }
            }
            if (polarised)
            {
                Polarise(ref Lp, ref polarisedUpTo, L, relative, false, default);
                return PolarisedPathIntegrator.MeasureCameraRay(camera, cameraRay, lambda[0], Lp, analyser, analyserAngle, measurement, cameraDirection, cameraUp);
            }
            return L;
        }

        // What has been added to L since `upTo` goes into the polarised light: through the matrices of the path, and first
        // through `last` if there is one (the matrix, over its first element, of the scattering that sent it along the path)
        private static void Polarise(ref PLight Lp, ref SampledSpectrum upTo, SampledSpectrum L, PAttenuation relative, bool hasLast, PAttenuation last)
        {
            SampledSpectrum added = L - upTo;
            upTo = L;
            if (!added)
            {
                return;
            }
            PLight light = new PLight(added);
            if (hasLast)
            {
                light = last * light;
            }
            Lp += relative * light;
        }

        public override string ToString() { return StringPrintf("[ ProductionPathIntegrator maxDepth: %d lightSampler: %s ]", maxDepth, lightSampler); }

        public static ProductionPathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights,
                                                      FileLoc loc)
        {
            ProductionPathIntegrator integrator = new ProductionPathIntegrator(parameters.GetOneInt("maxdepth", 5), camera, sampler, aggregate, lights,
                                                                               parameters.GetOneString("lightsampler", "bvh"));
            // ("bool lightsampling" false: no light is sampled, and a path finds the lights only by reaching them: the
            // reference that the estimators with light sampling are tested against)
            integrator.lightSampling = parameters.GetOneBool("lightsampling", true);
            // ("bool polarised": the path carries the polarisation of the light, and the sensor measures what "analyser"
            // and "measure" say, as "polarisedpath" does)
            integrator.polarised = parameters.GetOneBool("polarised", false);
            PolarisedPathIntegrator.ParseSensor(parameters, loc, out integrator.analyser, out integrator.analyserAngle, out integrator.measurement);
            integrator.cameraUp = camera.GetCameraTransform().RenderFromCamera(new Vector3f(0, 1, 0), 0);
            return integrator;
        }

        private int maxDepth;
        private bool lightSampling = true;
        // The polarised path (Li): the Mueller matrices of the path from the camera, each over its first element (the weight
        // of the path has those), and the light with its Stokes vector; what the sensor measures of it
        private bool polarised;
        private bool analyser;
        private Float analyserAngle;
        private PolarisedPathIntegrator.Measurement measurement;
        private Vector3f cameraUp;
        private LightSampler lightSampler;
        private List<VolumeEmissionLight> emissionLights;
        private Float[] emissionPmf;
        private Bounds3f[] emissionBounds;
    }
}
