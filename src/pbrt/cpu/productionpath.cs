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
// Not here: polarisation (the path is scalar), regularisation, and the equiangular sampling of volpath.

using System;
using System.Collections.Generic;

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
        }

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
                beamTransmittance = integrator.ShadowTransmittance(this, ctx, point, ls.value().pLight, stack, out beamR);
                return (bool)beamTransmittance;
            }
        }

        // The transmittance from a point of the path to a point of a light, through the volumes and the surfaces that only
        // bound them. The volume of each segment answers for it. The stack is a copy.
        private SampledSpectrum ShadowTransmittance(Services rs, VolumeContext ctx, Interaction origin, Interaction target, BoundaryStack stack, out SampledSpectrum beamR)
        {
            beamR = new SampledSpectrum(1f);
            SampledSpectrum T = new SampledSpectrum(1f);
            Ray ray = origin.SpawnRayTo(target);
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
            beamR = ctx.r;
            ctx.r = savedR;
            ctx.SetP(savedP);
            ctx.medium = savedMedium;
            return T;
        }

        // Next event estimation at a surface or at a point of a volume: f L T / p, with the weight of the heuristic, divided by
        // the mean density of the path over the wavelengths
        private SampledSpectrum SampleLd(Services rs, VolumeContext ctx, Interaction intr, bool surface, BSDF bsdf, PhaseFunction phase, Sampler sampler)
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
                f = new SampledSpectrum(phase.p(wo, wi));
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
            return w * f * ls.value().L * T / p_l / r.Average();
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler, ScratchBuffer scratchBuffer, VisibleSurface visibleSurf)
        {
            SampledSpectrum L = new SampledSpectrum(0f), beta = new SampledSpectrum(1f);
            int depth = 0;
            Float p_b = 0, etaScale = 1;
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

            while (true)
            {
                // The next point of the path: the nearest surface, or what the volume that the ray is in says
                Float length = Length(ray.d);
                Ray unit = new Ray(ray.o, ray.d / length, ray.time);
                Point3f P;
                VolumeGeometry g;
                bool hit;
                Medium medium = MediumTracking.Current(rs.stack);
                if (medium != null)
                {
                    ctx.SetP(ray.o);
                    ctx.medium = medium;
                    ctx.lambda = lambda;
                    ctx.mayScatter = depth < maxDepth;
                    Volume volume = Volume.Create(ctx, medium);
                    SampledSpectrum Lv, transmittance, weight;
                    Ray wo;
                    hit = volume.Integrate(rs, unit, out Lv, out transmittance, out weight, out P, out wo, out g);
                    L += beta * Lv;
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
                            L += beta * Le0 / ctx.r.Average();
                        }
                        else
                        {
                            Float p_l = lightSampler.PMF(prevIntrCtx, light) * light.PDF_Li(prevIntrCtx, ray.d, true);
                            L += beta * PowerHeuristic(1, p_b, 1, p_l) * Le0 / ctx.r.Average();
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
                    L += beta * SampleLd(rs, ctx, point, false, default, g.phase, sampler);
                    pstd.optional<PhaseFunctionSample> ps = g.phase.Sample_p(point.wo, sampler.Get2D());
                    if (!ps || ps.value().pdf == 0)
                    {
                        break;
                    }
                    beta *= ps.value().p / ps.value().pdf;
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
                        if (depth == 0 || specularBounce || isect.areaLight == null)
                        {
                            L += beta * Le / ctx.r.Average();
                        }
                        else
                        {
                            Light areaLight = isect.areaLight;
                            Float p_l = lightSampler.PMF(prevIntrCtx, areaLight) * areaLight.PDF_Li(prevIntrCtx, ray.d, true);
                            L += beta * PowerHeuristic(1, p_b, 1, p_l) * Le / ctx.r.Average();
                        }
                    }
                    BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                    ctx.lambda = lambda;
                    if (!bsdf)
                    {
                        // a surface that only bounds a volume
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
                        L += beta * SampleLd(rs, ctx, isect, true, bsdf, null, sampler);
                    }
                    Vector3f wo = -ray.d;
                    Float u = sampler.Get1D();
                    pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, u, sampler.Get2D());
                    if (!bs)
                    {
                        break;
                    }
                    beta *= bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                    p_b = bs.value().pdfIsProportional ? bsdf.PDF(wo, bs.value().wi) : bs.value().pdf;
                    specularBounce = bs.value().IsSpecular();
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
            return L;
        }

        public override string ToString() { return StringPrintf("[ ProductionPathIntegrator maxDepth: %d lightSampler: %s ]", maxDepth, lightSampler); }

        public static ProductionPathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights,
                                                      FileLoc loc)
        {
            return new ProductionPathIntegrator(parameters.GetOneInt("maxdepth", 5), camera, sampler, aggregate, lights, parameters.GetOneString("lightsampler", "bvh"));
        }

        private int maxDepth;
        private LightSampler lightSampler;
    }
}
