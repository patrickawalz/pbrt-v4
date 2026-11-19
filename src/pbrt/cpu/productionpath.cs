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
// "bool polarised": the path carries the polarisation of the light (Li, PolarisedGather), and the sensor measures what
// "analyser" and "measure" say, as "polpath" does. Not here: regularisation, and the equiangular sampling of volpath.

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
                f = phase.pSpectral(wo, wi);
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
                // (nothing more is gathered once the ray has left the box of the emission)
                Float t0, t1;
                if (!emissionBounds[index].IntersectP(ray.o, ray.d, Infinity, &t0, &t1))
                {
                    break;
                }
                pstd.optional<ShapeIntersection> si = Intersect(ray);
                Medium medium = MediumTracking.Current(stack);
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
                ray = isect.SpawnRay(wi);
                MediumTracking.CrossSurface(ref stack, isect, ray.d);
            }
            ctx.r = savedR;
            ctx.SetP(savedP);
            ctx.medium = savedMedium;
            SampledSpectrum scattered = w * f * emitted / p_e;
            if (ctx.polar != null && (bool)scattered)
            {
                // The emission of a medium is unpolarised; the scattering towards the path polarises it
                if (surface)
                {
                    ctx.polar.Add(scattered, PolarisedPathIntegrator.ConnectionAttenuation(bsdf, (SurfaceInteraction)intr, wo, wi, TransportMode.Radiance) *
                                             new PLight(scattered));
                }
                else
                {
                    ctx.polar.ScatteredUnpolarised(scattered, phase, wo, wi);
                }
            }
            return scattered;
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

            // The light and the point on it of the light sample that is returned, for the polarisation of what it emits
            // (PolarisedGather.emitter)
            private static void Emitter(VolumeContext ctx, Light light, Interaction point)
            {
                if (ctx.polar != null)
                {
                    ctx.polar.emitter.light = light;
                    ctx.polar.emitter.point = point;
                }
            }

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
                Emitter(ctx, light, ls.value().pLight);
                Float p_l = sampledLight.value().p * ls.value().pdf;
                // (a volume that is scattered in once has no other way to the light: no heuristic)
                L = ls.value().L / p_l;
                beamTransmittance = integrator.ShadowTransmittance(this, ctx, point, ls.value().pLight, stack, out beamR);
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
                Emitter(ctx, sampledLight.value().light, ls.value().pLight);
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
                Emitter(ctx, light, ls.value().pLight);
                lightPdf = sampledLight.value().p * ls.value().pdf;
                delta = IsDeltaLight(light.Type());
                L = ls.value().L / lightPdf;
                beamTransmittance = integrator.ShadowTransmittance(this, ctx, point, ls.value().pLight, stack, out beamR);
                return (bool)beamTransmittance;
            }

            // The direction is followed through the volumes and the surfaces that only bound them, to the first surface that
            // stops it or out of the scene
            public override bool EvaluateLightSample(VolumeContext ctx, MediumInteraction point, Vector3f wi, Float scatterPdf, out SampledSpectrum L,
                                                     out SampledSpectrum beamTransmittance, out SampledSpectrum beamR, out SampledSpectrum emitted)
            {
                L = new SampledSpectrum(0f);
                emitted = new SampledSpectrum(0f);
                Emitter(ctx, null, null);
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
                            Emitter(ctx, isect.areaLight, isect);
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
                    chosen.fixedPoint = GaussianBeamLight.IsPointLight(light);
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
                Emitter(ctx, light.light, ls.value().pLight);
                L = ls.value().L / (light.probability * ls.value().pdf);
                BoundaryStack unused;
                beamTransmittance = integrator.ShadowTransmittance(this, ctx, point, ls.value().pLight, place != null ? place.stack : stack, out beamR, out unused);
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
            beamR = new SampledSpectrum(1f);
            SampledSpectrum T = new SampledSpectrum(1f);
            Ray ray = origin.SpawnRayTo(target);
            end = stack;
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
                    T *= Volume.Create(ctx, medium).Transmittance(rs, ray.o, ray.Apply(tMax));
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
            return T;
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
            LightSampleContext origin = new LightSampleContext(new Point3fi(direct.ray.o), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), direct.ray.time);
            pstd.optional<LightLiSample> pivot = light.SampleLi(origin, sampler.Get2D(), lambda, false);
            return pivot ? direct.For(true, GaussianBeamLight.IsPointLight(light), pivot.value().pLight.p()) : 1;
        }

        private SampledSpectrum SampleLd(Services rs, VolumeContext ctx, Interaction intr, bool surface, BSDF bsdf, PhaseFunction phase, Sampler sampler, VolumeDirectWeight direct)
        {
            LightSampleContext lightContext;
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
            SampledSpectrum f;
            Float scatterPdf;
            if (surface)
            {
                f = bsdf.f(wo, wi) * AbsDot(wi, ((SurfaceInteraction)intr).shading.n);
                scatterPdf = bsdf.PDF(wo, wi);
            }
            else
            {
                f = phase.pSpectral(wo, wi);
                scatterPdf = phase.PDF(wo, wi);
            }
            if (!f)
            {
                return new SampledSpectrum(0f);
            }
            SampledSpectrum beamR;
            SampledSpectrum T = ShadowTransmittance(rs, ctx, intr, ls.value().pLight, rs.stack, out beamR);
            if (!T)
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
            SampledSpectrum lit = DirectWeight(direct, light, sampler, ctx.lambda) * w * f * ls.value().L * T / p_l / r.Average();
            if (ctx.polar != null && (bool)lit)
            {
                // What the light emits towards the point, through the scattering there towards the path
                PLight arriving = PolarisedEmission.AtPoint(light, ls.value().pLight, -wi, ctx.lambda, lit);
                if (surface)
                {
                    ctx.polar.Add(lit, PolarisedPathIntegrator.ConnectionAttenuation(bsdf, (SurfaceInteraction)intr, wo, wi, TransportMode.Radiance) * arriving);
                }
                else if (PolarisedGather.PhaseMatrix(phase, wo, wi, out PAttenuation towardsLight))
                {
                    ctx.polar.Add(lit, towardsLight * arriving);
                }
            }
            return lit;
        }

        // What a point of the path gathered goes into the light of the path, times the throughput: `gathered` as a spectrum,
        // and, for the polarised path, as polarised light through the Mueller matrices of the path so far (`relative`: each
        // over its first element, which the throughput has). What the strategies recorded since the last time is the
        // polarised part of `gathered`, the rest is unpolarised (PolarisedGather).
        private void Gather(ref SampledSpectrum L, ref PLight Lp, SampledSpectrum beta, PAttenuation relative, VolumeContext ctx, SampledSpectrum gathered)
        {
            L += beta * gathered;
            if (ctx.polar != null)
            {
                Lp += relative * (ctx.polar.Take(gathered) * beta);
            }
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler, ScratchBuffer scratchBuffer, VisibleSurface visibleSurf)
        {
            SampledSpectrum L = new SampledSpectrum(0f), beta = new SampledSpectrum(1f);
            // The polarised path: the light of the path as polarised light, and the product of the Mueller matrices of the path
            // so far, each over its first element (Gather)
            PLight Lp = PLight.Zero();
            PAttenuation relative = PAttenuation.Identity();
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
            if (polarised)
            {
                ctx.polar = new PolarisedGather();
            }
            Services rs = new Services();
            rs.integrator = this;
            rs.sampler = sampler;
            rs.stack = MediumTracking.Start(camera.GetMedium());
            rs.rng = new RNG(Hash(ray.o, ray.d), Hash(sampler.Get1D()));

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
                            ctx.emissionWeight = p_b / (p_b + pmf * medium.EmissionLight().PdfDirection(prevIntrCtx.p(), unit.d, ray.time));
                        }
                    }
                    Volume volume = Volume.Create(ctx, medium);
                    SampledSpectrum Lv, transmittance, weight;
                    Ray wo;
                    hit = volume.Integrate(rs, unit, out Lv, out transmittance, out weight, out P, out wo, out g);
                    Gather(ref L, ref Lp, beta, relative, ctx, Lv);
                    beta *= weight * transmittance;
                    if (!beta || !ctx.r)
                    {
                        break;
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
                            Gather(ref L, ref Lp, beta, relative, ctx, Le0 / ctx.r.Average());
                        }
                        else
                        {
                            Float p_l = lightSampler.PMF(prevIntrCtx, light) * light.PDF_Li(prevIntrCtx, ray.d, true);
                            Gather(ref L, ref Lp, beta, relative, ctx, PowerHeuristic(1, p_b, 1, p_l) * Le0 / ctx.r.Average());
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
                    Gather(ref L, ref Lp, beta, relative, ctx, SampleLd(rs, ctx, point, false, default, g.phase, sampler, direct));
                    if (emissionLights.Count > 0)
                    {
                        Gather(ref L, ref Lp, beta, relative, ctx, SampleEmission(rs, ctx, point, false, default, g.phase, sampler, rs.stack, ctx.r, true));
                    }
                    // One more scattering between this collision and a light at a position, where the volume asks for it
                    // (Volume.OnceMoreShare): the light of the next collision, which the path may still reach
                    if (g.joint != null && g.joint.onceMore != VolumeOnceMore.None && depth < maxDepth)
                    {
                        Medium here = MediumTracking.Current(rs.stack);
                        if (here != null)
                        {
                            Volume.OnceMoreStart start = new Volume.OnceMoreStart();
                            start.point = point;
                            start.phase = g.phase;
                            Gather(ref L, ref Lp, beta, relative, ctx, OnceMore(rs, ctx, here, start, g.phase, g.joint));
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
                    // (the value of the phase function for every wavelength, where it depends on the wavelength: the direction
                    // was drawn with the first)
                    beta *= g.phase.pSpectral(point.wo, ps.value().wi) / ps.value().pdf;
                    if (polarised)
                    {
                        // (only the Rayleigh part of a phase function polarises; the others depolarise)
                        relative *= PolarisedGather.PhaseMatrix(g.phase, point.wo, ps.value().wi, out PAttenuation scatteredPhase)
                            ? scatteredPhase : PAttenuation.Lambert(new SampledSpectrum(1f));
                    }
                    p_b = ps.value().pdf;
                    specularBounce = false;
                    prevIntrCtx = new LightSampleContext(point);
                    ray = point.SpawnRay(ps.value().wi);
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
                        SampledSpectrum seen;
                        if (depth == 0 || specularBounce || isect.areaLight == null)
                        {
                            seen = Le / ctx.r.Average();
                        }
                        else
                        {
                            Light areaLight = isect.areaLight;
                            Float p_l = lightSampler.PMF(prevIntrCtx, areaLight) * areaLight.PDF_Li(prevIntrCtx, ray.d, true);
                            seen = DirectWeight(direct, areaLight, sampler, ctx.lambda) * PowerHeuristic(1, p_b, 1, p_l) * Le / ctx.r.Average();
                        }
                        if (polarised)
                        {
                            // (the light of a glowing surface is polarised: PolarisedEmission)
                            ctx.polar.Add(seen, PolarisedEmission.AtHit(isect, -ray.d, lambda, seen));
                        }
                        Gather(ref L, ref Lp, beta, relative, ctx, seen);
                    }
                    BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                    ctx.lambda = lambda;
                    if (!bsdf)
                    {
                        // a surface that only bounds a volume
                        if (polarised && PolarisedPathIntegrator.PassedCoat(isect, -ray.d, TransportMode.Radiance, out PAttenuation coat))
                        {
                            relative *= coat;
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
                        Gather(ref L, ref Lp, beta, relative, ctx, SampleLd(rs, ctx, isect, true, bsdf, null, sampler, null));
                        // One more scattering between this point and a light at a position, in the volume that the path
                        // came through, where that volume asks for it (Volume.OnceMoreShare)
                        Medium around = MediumTracking.Current(rs.stack);
                        if (around != null && around.volumeIntegrator.onceMore != VolumeOnceMore.None && around.volumeIntegrator.scattering == VolumeScattering.Multiple &&
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
                                Gather(ref L, ref Lp, beta, relative, ctx, OnceMore(rs, ctx, around, start, mp.phase, null));
                            }
                        }
                        if (emissionLights.Count > 0)
                        {
                            Gather(ref L, ref Lp, beta, relative, ctx, SampleEmission(rs, ctx, isect, true, bsdf, null, sampler, rs.stack, ctx.r, true));
                        }
                    }
                    Vector3f wo = -ray.d;
                    Float u = sampler.Get1D();
                    // (the polarised path asks the BxDFs that follow one path for the matrix of the path itself)
                    bool pathPolarised = false;
                    PAttenuation pathMatrix = default;
                    Point2f u2 = sampler.Get2D();
                    pstd.optional<BSDFSample> bs = polarised
                        ? bsdf.Sample_f(wo, u, u2, TransportMode.Radiance, BxDFReflTransFlags.All, out pathPolarised, out pathMatrix)
                        : bsdf.Sample_f(wo, u, u2);
                    if (!bs)
                    {
                        break;
                    }
                    beta *= bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                    if (polarised)
                    {
                        // The matrix of the scattering over its first element (the throughput has that)
                        PAttenuation scattered;
                        if (pathPolarised)
                        {
                            relative *= pathMatrix;
                        }
                        else if (PolarisedPathIntegrator.SpecularAttenuation(bsdf, isect, wo, bs.value(), TransportMode.Radiance, out scattered))
                        {
                            relative *= scattered * SafeDiv(new SampledSpectrum(1f), scattered.GetIntensity());
                        }
                        else if (PolarisedPathIntegrator.RoughAttenuation(bsdf, isect, wo, bs.value().wi, new SampledSpectrum(1f), TransportMode.Radiance,
                                                                          out scattered))
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
                return PolarisedPathIntegrator.MeasureCameraRay(camera, cameraRay, lambda[0], Lp, analyser, analyserAngle, measurement, cameraDirection,
                                                                cameraUp);
            }
            return L;
        }

        public override string ToString() { return StringPrintf("[ ProductionPathIntegrator maxDepth: %d lightSampler: %s ]", maxDepth, lightSampler); }

        public static ProductionPathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights,
                                                      FileLoc loc)
        {
            ProductionPathIntegrator integrator = new ProductionPathIntegrator(parameters.GetOneInt("maxdepth", 5), camera, sampler, aggregate, lights,
                                                                               parameters.GetOneString("lightsampler", "bvh"));
            // ("bool polarised": the path carries the polarisation of the light, and the sensor measures what "analyser" and
            // "measure" say, as "polpath" does)
            integrator.polarised = parameters.GetOneBool("polarised", false);
            PolarisedPathIntegrator.ParseSensor(parameters, loc, out integrator.analyser, out integrator.analyserAngle, out integrator.measurement);
            integrator.cameraUp = camera.GetCameraTransform().RenderFromCamera(new Vector3f(0, 1, 0), 0);
            return integrator;
        }

        private bool polarised;
        private bool analyser;
        private Float analyserAngle;
        private PolarisedPathIntegrator.Measurement measurement;
        private Vector3f cameraUp;

        private int maxDepth;
        private LightSampler lightSampler;
        private List<VolumeEmissionLight> emissionLights;
        private Float[] emissionPmf;
        private Bounds3f[] emissionBounds;
    }
}
