// (not in pbrt-v4) unified_tracer.cs
//
// The shared walk of the volume interface, after the UPBP / LuxCore tracer: one object that finds the next scattering event of a
// path, whether it is on a surface or in a volume. It replaces PathWalk and its PathWalkHook: there are no callbacks. The volume
// decides where its collisions are (Volume.Integrate); the tracer replays the null ones into the path state and returns the real
// one as an event with a BSDF (the phase function of a medium point is a BSDF), so a renderer does not know which it got.
// Volpath (TracerVolPathIntegrator) and a bidirectional integrator are clients of the same Trace, Continue and Transmittance.
//
// Not in this first version: polarisation, subsurface scattering, curved (refractive) edges, OSL ray types.

using System;
using System.Collections.Generic;
using static pbrt.pbrt;

namespace pbrt
{

    // What UnifiedTracer.SampleLight returns
    public struct LightArrival
    {
        // The direction the path leaves the vertex along, the radiance (or intensity) towards the vertex, and the density per solid
        // angle of the direction (without the choice of the light)
        public Vector3f wi;
        public SampledSpectrum L;
        public Float pdf;
        public bool delta;
        // Only this strategy finds the light from this vertex (a curved path): the scattering of the vertex has no part in the weight
        public bool neeOnly;
        public bool curved;
        // The transmittance and the spectral factors of the path to the light
        public SampledSpectrum T;
        public SampledSpectrum r_u;
        public SampledSpectrum r_l;
        // A straight sample whose shadow is not yet found
        public bool needsShadow;
        public Interaction target;
    }

    // UnifiedTracer Definition
    public sealed unsafe class UnifiedTracer : SceneTracer
    {
        public UnifiedTracer(Integrator integrator, Camera camera, int maxDepth)
        {
            this.integrator = integrator;
            this.camera = camera;
            this.maxDepth = maxDepth;
        }

        public override ScatterEvent Trace(ref RayDifferential ray, ref PathState state, ref BoundaryStack stack, Sampler sampler,
                                           ref SampledWavelengths lambda, ScratchBuffer scratchBuffer, TraceBuffers buffers)
        {
            buffers.emission.Clear();
            int depthLimit = state.depthLimit > 0 ? state.depthLimit : maxDepth;
            ScatterEvent result = default;
            result.emission = buffers.emission;
            result.kind = EventKind.Terminated;
            // The path came along a curve of a refractive volume since its last scattering
            bool curved = false;
            RefractiveMedium curvedMedium = null;
            while (true)
            {
                Medium medium = MediumTracking.Current(stack);
                Volume volume = medium != null ? VolumeFactory.For(medium) : null;
                // The scene is not intersected along a straight ray in a refractive volume, where the paths are curves
                pstd.optional<ShapeIntersection> si = volume != null && volume.IsRefractive() ? default : integrator.Intersect(new Ray(ray));
                if (medium != null)
                {
                    if (volume.IsRefractive())
                    {
                        curvedMedium = ((RefractiveVolume)volume).Medium();
                    }
                    Float tMax = si ? si.value().tHit : Infinity;
                    RNG rng = new RNG(Hash(ray.o, tMax), Hash(ray.d));
                    Float u = sampler.Get1D();
                    buffers.passed.Clear();
                    // A curved volume cannot be bounded by a straight intersection: its path meets the surfaces of the scene along its
                    // own chords
                    Func<Point3f, Point3f, Float> sceneChord = null;
                    if (volume.IsRefractive())
                    {
                        Float rayTime = ray.time;
                        sceneChord = (Point3f a, Point3f b) =>
                        {
                            Vector3f chord = b - a;
                            pstd.optional<ShapeIntersection> hit = integrator.Intersect(new Ray(a, chord, rayTime), 1);
                            return hit && hit.value().tHit > 1e-4f ? hit.value().tHit : -1;
                        };
                    }
                    volume.Integrate(new Ray(ray), tMax, u, ref rng, lambda, state.scatteredIn, buffers.passed,
                                     out VolumeIntegration integration, sceneChord);
                    // The null collisions, in order
                    for (int i = 0; i < buffers.passed.Count; ++i)
                    {
                        VolumeCollision collision = buffers.passed[i];
                        if (!state.beta)
                        {
                            return result;
                        }
                        GatherMediumEmission(collision, ray, state, buffers, medium, stack, curvedMedium);
                        SampledSpectrum sigma_n = ClampZero(collision.sigma_maj - collision.mp.sigma_a - collision.mp.sigma_s);
                        if (!state.NullCollision(collision.T_maj, collision.sigma_maj, sigma_n))
                        {
                            return result;
                        }
                    }
                    if (integration.end != VolumeEnd.Escaped)
                    {
                        VolumeCollision collision = integration.terminal;
                        if (!state.beta)
                        {
                            return result;
                        }
                        GatherMediumEmission(collision, ray, state, buffers, medium, stack, curvedMedium);
                        if (integration.end == VolumeEnd.Absorbed)
                        {
                            return result;
                        }
                        if (state.depth >= depthLimit)
                        {
                            return result;
                        }
                        ++state.depth;
                        state.RealScatter(collision.T_maj, collision.mp.sigma_s);
                        if (!((bool)state.beta && (bool)state.r_u))
                        {
                            return result;
                        }
                        state.scatteredIn |= integration.scatterBit;
                        MediumInteraction interaction = new MediumInteraction(collision.p, -collision.incoming, ray.time, medium, collision.mp.phase);
                        result.kind = EventKind.Scatter;
                        result.interaction = interaction;
                        result.bsdf = interaction.GetBSDF();
                        result.inMedium = true;
                        result.curvedMedium = curvedMedium;
                        result.curved = curvedMedium != null;
                        result.sigma_s = collision.mp.sigma_s;
                        result.edge = StraightEdge();
                        return result;
                    }
                    if (!state.beta || !state.r_u)
                    {
                        return result;
                    }
                    state.EndSegment(integration.T_maj);
                    if (!integration.exitsStraight)
                    {
                        // The curve left the volume: the boundary surface is found from a little before the end of the curve
                        if (integration.popsMedium)
                        {
                            stack.RemoveMedium(medium);
                        }
                        ray.o = integration.exitRay.o;
                        ray.d = integration.exitRay.d;
                        si = integrator.Intersect(new Ray(ray));
                        curved = true;
                    }
                }

                // The ray left the scene
                if (!si)
                {
                    result.kind = EventKind.Escaped;
                    result.curved = curved;
                    return result;
                }

                // A surface: its emission, then its BSDF (a surface without one only marks a boundary)
                SurfaceInteraction isect = si.value().intr;
                SampledSpectrum Le = isect.Le(-ray.d, lambda);
                if ((bool)Le)
                {
                    EmissionSample emission = default;
                    emission.isMedium = false;
                    emission.p = isect.p();
                    emission.direction = ray.d;
                    emission.light = isect.areaLight;
                    emission.depth = state.depth;
                    emission.curved = curved;
                    emission.radiance = Le;
                    emission.beta = state.beta;
                    emission.r_u = state.r_u;
                    emission.r_l = state.r_l;
                    buffers.emission.Add(emission);
                }
                BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                if (!bsdf)
                {
                    isect.SkipIntersection(ref ray, si.value().tHit);
                    MediumTracking.CrossSurface(ref stack, isect, ray.d);
                    continue;
                }
                if (state.depth >= depthLimit)
                {
                    return result;
                }
                ++state.depth;
                result.kind = EventKind.Scatter;
                result.interaction = isect;
                result.bsdf = bsdf;
                result.inMedium = false;
                result.curvedMedium = curvedMedium;
                result.curved = curved;
                result.edge = StraightEdge();
                return result;
            }
        }

        public override bool Continue(ScatterEvent scatter, ref RayDifferential ray, ref PathState state, ref BoundaryStack stack,
                                      Sampler sampler, out BSDFSample sample)
        {
            sample = default;
            TransportMode mode = state.fromCamera ? TransportMode.Radiance : TransportMode.Importance;
            Vector3f wo = scatter.interaction.wo;
            if (scatter.inMedium)
            {
                MediumInteraction interaction = scatter.interaction.AsMedium();
                pstd.optional<BSDFSample> ps = scatter.bsdf.Sample_f(wo, 0.0f, sampler.Get2D());
                if (!ps || ps.value().pdf == 0)
                {
                    return false;
                }
                sample = ps.value();
                state.ScatterPhase(interaction.phase, wo, sample);
                if (state.rescaled)
                {
                    state.r_l = state.r_u / sample.pdf;
                }
                ray.o = interaction.p();
                ray.d = sample.wi;
                return true;
            }
            SurfaceInteraction isect = scatter.interaction.AsSurface();
            Float uc = sampler.Get1D();
            pstd.optional<BSDFSample> bs = scatter.bsdf.Sample_f(wo, uc, sampler.Get2D(), mode, BxDFReflTransFlags.All);
            if (!bs)
            {
                return false;
            }
            sample = bs.value();
            state.ScatterSurface(scatter.bsdf, isect, wo, sample, mode);
            if (state.rescaled)
            {
                if (sample.pdfIsProportional)
                {
                    state.r_l = state.r_u / scatter.bsdf.PDF(wo, sample.wi);
                }
                else
                {
                    state.r_l = state.r_u / sample.pdf;
                }
            }
            ray = isect.SpawnRay(ray, scatter.bsdf, sample.wi, sample.flags, sample.eta);
            MediumTracking.CrossSurface(ref stack, isect, ray.d);
            return true;
        }

        public override SampledSpectrum Transmittance(Interaction a, Interaction b, ref BoundaryStack stack, SampledWavelengths lambda,
                                                      out SampledSpectrum r_u, out SampledSpectrum r_l)
        {
            Ray lightRay = a.SpawnRayTo(b);
            RNG rng = new RNG(Hash(lightRay.o), Hash(lightRay.d));
            MediumTracking.CrossSurface(ref stack, a, lightRay.d);
            return ShadowRayFrom(lightRay, b, ref stack, lambda, rng, out r_u, out r_l);
        }

        // The shadow ray from where lightRay starts to b, through the surfaces and volumes between, with the stack of its start
        SampledSpectrum ShadowRayFrom(Ray lightRay, Interaction b, ref BoundaryStack stack, SampledWavelengths lambda, RNG rng,
                                      out SampledSpectrum r_u, out SampledSpectrum r_l)
        {
            SampledSpectrum T_ray = new SampledSpectrum(1.0f);
            SampledSpectrum rl = new SampledSpectrum(1.0f);
            SampledSpectrum ru = new SampledSpectrum(1.0f);
            while (lightRay.d != new Vector3f(0, 0, 0))
            {
                pstd.optional<ShapeIntersection> si = integrator.Intersect(lightRay, 1 - ShadowEpsilon);
                if (si && ThinCoatMaterial.Blocks(si.value().intr, lightRay.d))
                {
                    T_ray = new SampledSpectrum(0.0f);
                    break;
                }
                Medium medium = MediumTracking.Current(stack);
                if (medium != null)
                {
                    Float tMax = si ? si.value().tHit : (1 - ShadowEpsilon);
                    VolumeFactory.For(medium).SegmentTransmittance(lightRay, tMax, lambda, ref rng, true, ref T_ray, ref rl, ref ru);
                }
                if (!T_ray)
                {
                    break;
                }
                if (!si)
                {
                    break;
                }
                lightRay = si.value().intr.SpawnRayTo(b);
                MediumTracking.CrossSurface(ref stack, si.value().intr, lightRay.d);
            }
            r_l = rl;
            r_u = ru;
            return T_ray;
        }

        // A curved connection from a vertex inside a refractive volume to a point of a light outside it: the curve (Volume.Connect), then
        // the straight shadow ray from the surface that bounds the volume. The transmittance and the spectral factors of the result
        // are those of both parts.
        public override bool Connect(Interaction vertex, Interaction target, ref BoundaryStack stack, SampledWavelengths lambda,
                                     ref RNG rng, out VolumeConnection connection)
        {
            connection = default;
            Medium medium = MediumTracking.Current(stack);
            if (!(medium is RefractiveMedium))
            {
                return false;
            }
            RefractiveMedium refractive = (RefractiveMedium)medium;
            Volume volume = VolumeFactory.For(medium);
            Point3f x = vertex.p();
            Point3f y = target.p();
            if (DistanceSquared(x, y) == 0)
            {
                return false;
            }
            bool targetInside = refractive.Contains(y);
            Func<Point3f, Point3f, Float> sceneChord = ChordOcclusion.SceneChord(integrator, refractive, vertex.time, y);
            if (!volume.Connect(x, y, lambda, vertex.time, ref rng, out VolumeConnection curve, sceneChord))
            {
                return false;
            }
            if (targetInside)
            {
                // The whole path is the curve
                connection = curve;
                return true;
            }
            Bounds3f bounds = volume.Bounds();
            Float diagonal = Length(bounds.pMax - bounds.pMin);
            Ray lightRay;
            if (curve.exitLength < 1e-4f * diagonal)
            {
                // Within a few rounding errors of the vertex: there is no surface to find from a distance, the medium is left here
                stack.RemoveMedium(medium);
                lightRay = new Ray(curve.exitPoint, y - curve.exitPoint, vertex.time);
            }
            else
            {
                Float back = Math.Min(1e-3f * diagonal, 0.5f * curve.exitLength);
                pstd.optional<ShapeIntersection> boundary =
                    integrator.Intersect(new Ray(curve.exitPoint - back * curve.exitDirection, curve.exitDirection, vertex.time), 4 * back);
                if (!boundary)
                {
                    return false;
                }
                Interaction boundaryIntr = boundary.value().intr;
                lightRay = boundaryIntr.SpawnRayTo(target);
                MediumTracking.CrossSurface(ref stack, boundaryIntr, lightRay.d);
            }
            RNG shadowRng = new RNG(Hash(lightRay.o), Hash(lightRay.d));
            SampledSpectrum T_out = ShadowRayFrom(lightRay, target, ref stack, lambda, shadowRng, out SampledSpectrum ruOut,
                                                  out SampledSpectrum rlOut);
            if (!T_out)
            {
                return false;
            }
            connection = curve;
            connection.transmittance = curve.transmittance * T_out;
            connection.r_l = curve.r_l * rlOut;
            connection.r_u = curve.r_u * ruOut;
            return true;
        }

        // A sample of a light as seen from a vertex, whatever volume the vertex is in: a straight sample for a vertex outside the refractive
        // volumes, and for a vertex inside one the light reached along a curved path (Pediredla et al., section 5). The caller applies one
        // formula to both: the density is per solid angle of the direction the path leaves the vertex along, and for a curved path it is the
        // density of the point on the light times the Jacobian of the path, over the cosine at the light and the count of the search
        // (an estimate of one over the probability of the path).
        public bool SampleLight(Interaction vertex, LightSampleContext context, Light light, Point2f u, BoundaryStack stack,
                                SampledWavelengths lambda, out LightArrival arrival)
        {
            arrival = default;
            RefractiveMedium refractive = MediumTracking.Current(stack) as RefractiveMedium;
            if (refractive == null)
            {
                pstd.optional<LightLiSample> straight = light.SampleLi(context, u, lambda, true);
                if (!straight || !straight.value().L || straight.value().pdf == 0)
                {
                    return false;
                }
                arrival.wi = straight.value().wi;
                arrival.L = straight.value().L;
                arrival.pdf = straight.value().pdf;
                arrival.delta = IsDeltaLight(light.Type());
                arrival.target = straight.value().pLight;
                arrival.needsShadow = true;
                return true;
            }

            // The point the path ends at, the density of that point, and what the light emits towards the vertex (known once the path is)
            DiffuseAreaLight areaLight = light as DiffuseAreaLight;
            PointLight pointLight = light as PointLight;
            SpotLight spotLight = light as SpotLight;
            bool infinite = light.Type() == LightType.Infinite;
            Interaction target;
            Float pdfPoint;
            Point3f x = vertex.p();
            if (areaLight != null)
            {
                if (!areaLight.SampleArea(u, out target, out pdfPoint))
                {
                    return false;
                }
            }
            else if (pointLight != null || spotLight != null)
            {
                pstd.optional<LightLiSample> sample = light.SampleLi(context, u, lambda);
                if (!sample)
                {
                    return false;
                }
                target = sample.value().pLight;
                pdfPoint = 1;
            }
            else if (infinite)
            {
                // A point far away along the direction sampled: the density per area there is the density per direction over the squared
                // distance, and the Jacobian over it is that of the exit direction against the direction at the vertex
                pstd.optional<LightLiSample> sample = light.SampleLi(context, u, lambda, true);
                if (!sample || !sample.value().L || sample.value().pdf == 0)
                {
                    return false;
                }
                Float distance = 300 * Length(refractive.Bounds().pMax - refractive.Bounds().pMin);
                target = new Interaction(x + distance * sample.value().wi, vertex.time, (Medium)null);
                pdfPoint = sample.value().pdf / (distance * distance);
            }
            else
            {
                return false;
            }
            RNG rng = new RNG(Hash(x), Hash(target.p()));
            BoundaryStack connectionStack = stack;
            if (!Connect(vertex, target, ref connectionStack, lambda, ref rng, out VolumeConnection connection))
            {
                return false;
            }
            Float cosineEnd = areaLight != null ? AbsDot(target.n, connection.arrivingDirection) : 1;
            if (cosineEnd == 0 || connection.areaJacobian == 0)
            {
                return false;
            }
            if (areaLight != null)
            {
                arrival.L = areaLight.L(target.p(), target.n, target.uv, -connection.arrivingDirection, lambda, target.time);
            }
            else if (pointLight != null)
            {
                arrival.L = pointLight.SampleLi(context, new Point2f(0.5f, 0.5f), lambda).value().L * DistanceSquared(x, target.p());
            }
            else if (spotLight != null)
            {
                arrival.L = spotLight.IAlong(-connection.arrivingDirection, lambda);
            }
            else
            {
                arrival.L = light.Le(new Ray(x, connection.arrivingDirection), lambda);
            }
            if (!arrival.L)
            {
                return false;
            }
            arrival.wi = connection.leavingDirection;
            arrival.pdf = pdfPoint * connection.areaJacobian / cosineEnd / connection.pathCount;
            arrival.delta = IsDeltaLight(light.Type());
            arrival.neeOnly = true;
            arrival.curved = true;
            arrival.T = connection.transmittance;
            arrival.r_u = connection.r_u;
            arrival.r_l = connection.r_l;
            return true;
        }

        // The shadow of a straight sample, which is found after the scattering at the vertex is known to be worth it
        public bool ResolveShadow(Interaction vertex, ref LightArrival arrival, BoundaryStack stack, SampledWavelengths lambda)
        {
            if (!arrival.needsShadow)
            {
                return (bool)arrival.T;
            }
            arrival.T = Transmittance(vertex, arrival.target, ref stack, lambda, out arrival.r_u, out arrival.r_l);
            arrival.needsShadow = false;
            return (bool)arrival.T;
        }

        // The emission of a medium at a collision, with the weights volpath gives it when no other strategy finds the point
        void GatherMediumEmission(VolumeCollision collision, RayDifferential ray, PathState state, TraceBuffers buffers, Medium medium,
                                  BoundaryStack stack, RefractiveMedium curvedMedium)
        {
            if (state.depth >= maxDepth || !(bool)collision.mp.Le)
            {
                return;
            }
            Float pdf = collision.sigma_maj[0] * collision.T_maj[0];
            SampledSpectrum r_e = state.r_u * collision.sigma_maj * collision.T_maj / pdf;
            if (!(bool)r_e)
            {
                return;
            }
            EmissionSample emission = default;
            emission.isMedium = true;
            emission.p = collision.p;
            emission.direction = ray.d;
            emission.depth = state.depth;
            emission.radiance = collision.T_maj / pdf * collision.mp.sigma_a * collision.mp.Le;
            emission.beta = state.beta;
            emission.r_u = state.r_u;
            emission.r_l = state.r_l;
            emission.r_e = r_e;
            emission.medium = medium;
            emission.T_maj = collision.T_maj;
            emission.sigma_maj = collision.sigma_maj;
            emission.coefficient = collision.mp.sigma_a * collision.mp.Le;
            emission.segU = state.segU;
            emission.segL = state.segL;
            emission.stack = stack;
            emission.curvedMedium = curvedMedium;
            buffers.emission.Add(emission);
        }

        static ScatterEdge StraightEdge()
        {
            ScatterEdge edge = default;
            edge.curved = false;
            return edge;
        }

        readonly Integrator integrator;
        readonly Camera camera;
        readonly int maxDepth;
    }

}  // namespace pbrt
