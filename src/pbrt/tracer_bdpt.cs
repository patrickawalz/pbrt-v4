// (not in pbrt-v4) tracer_bdpt.cs
//
// Bidirectional path tracing as a client of the UnifiedTracer. The subpaths are walked with Trace and Continue (no PathWalkHook):
// a surface vertex and a medium vertex are one event for the walk. Stage A keeps the stored Vertex arrays and hands them to the
// connection and MIS code of bdpt (BDPT.ConnectBDPT), so the integrator can be checked against "bdpt" with the same strategies and
// weights. Not in this stage: polarisation, equiangular sampling, emissive media (as lights and as camera subpath ends), Russian
// roulette, visualizestrategies, the online (dVCM / dVC) weights of stage B.

using System;
using System.Collections.Generic;
using static pbrt.pbrt;
using PLight = pbrt.PolarisedLightSystem.Light;
using PAttenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{

    // TracerBDPT Definition: the subpaths
    public static unsafe class TracerBDPT
    {
        public static int GenerateCameraSubpath(UnifiedTracer tracer, RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                                ScratchBuffer scratchBuffer, int maxDepth, Camera camera, Vertex[] path, bool regularize,
                                                List<MediumEmissionEnd> emissionEnds = null)
        {
            if (maxDepth == 0)
            {
                return 0;
            }
            SampledSpectrum beta = new SampledSpectrum(1.0f);
            Float pdfPos, pdfDir;
            BoundaryStack stack = MediumTracking.Start(camera.GetMedium());
            path[0] = Vertex.CreateCamera(camera, new Ray(ray), beta);
            path[0].stack = stack;
            camera.PDF_We(new Ray(ray), &pdfPos, &pdfDir);
            return RandomWalk(tracer, ref lambda, ray, stack, sampler, scratchBuffer, beta, pdfDir, maxDepth - 1,
                              TransportMode.Radiance, path, 1, regularize, emissionEnds) + 1;
        }

        public static int GenerateLightSubpath(UnifiedTracer tracer, Integrator integrator, ref SampledWavelengths lambda, Sampler sampler,
                                               Camera camera, ScratchBuffer scratchBuffer, int maxDepth, Float time, LightSampler lightSampler,
                                               Vertex[] path, bool regularize)
        {
            if (maxDepth == 0)
            {
                return 0;
            }
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(sampler.Get1D());
            if (!sampledLight)
            {
                return 0;
            }
            Light light = sampledLight.value().light;
            Float lightSamplePDF = sampledLight.value().p;
            Point2f ul0 = sampler.Get2D();
            Point2f ul1 = sampler.Get2D();
            // The point of an emissive volume has a third coordinate
            VolumeEmissionLight volumeLight = light as VolumeEmissionLight;
            pstd.optional<LightLeSample> les = volumeLight != null
                ? volumeLight.SampleLeVolume(ul0, ul1, sampler.Get1D(), lambda, time)
                : light.SampleLe(ul0, ul1, ref lambda, time);
            BoundaryStack stack = default;
            if (volumeLight != null)
            {
                // The point only emits if the surfaces around it say that it is in the medium. A sample that misses is a light subpath
                // without throughput and not the absence of one: the s = 1 strategies take samples of their own, and only run if the
                // light subpath has a vertex.
                bool valid = les && les.value().pdfPos != 0 && les.value().pdfDir != 0 && (bool)les.value().L;
                if (valid)
                {
                    stack = BDPT.StackAtPoint(integrator, camera, les.value().ray.o, time);
                    valid = BDPT.MediumEmits(MediumTracking.Current(stack), volumeLight);
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
                {
                    return 0;
                }
                stack = MediumTracking.Start(les.value().medium);
            }
            RayDifferential ray = new RayDifferential(les.value().ray);

            Float p_l = lightSamplePDF * les.value().pdfPos;
            path[0] = les.value().intr ? Vertex.CreateLight(light, les.value().intr.value(), les.value().L, p_l)
                                       : Vertex.CreateLight(light, new Ray(ray), les.value().L, p_l);
            SampledSpectrum beta = les.value().L * les.value().AbsCosTheta(ray.d) / (p_l * les.value().pdfDir);
            // The extinction at the point of an emissive medium, for the weight of a camera subpath that gathers it
            if (path[0].ei.light is VolumeEmissionLight emissiveMedium)
            {
                SampledSpectrum majorant = emissiveMedium.Source().MajorantAt(ray.o, lambda, ray.time);
                path[0].sigmaT0 = majorant[0];
                path[0].emissionMaj = majorant / majorant[0];
            }
            path[0].stack = stack;
            int nVertices = RandomWalk(tracer, ref lambda, ray, stack, sampler, scratchBuffer, beta, les.value().pdfDir, maxDepth - 1,
                                       TransportMode.Importance, path, 1, regularize);
            // Correct subpath sampling densities for infinite area lights
            if (path[0].IsInfiniteLight())
            {
                // (not in pbrt-v4) A light at infinity that enters a refractive medium along a curved edge is only reached by the
                // light sampled from the camera subpath (CurvedLightSample), so this subpath stops at the light
                if (nVertices > 0 && path[1].curvedEdge)
                {
                    nVertices = 0;
                }
                if (nVertices > 0)
                {
                    path[1].pdfFwd = les.value().pdfPos;
                    if (path[1].IsOnSurface())
                    {
                        path[1].pdfFwd *= AbsDot(ray.d, path[1].ng());
                    }
                }
                path[0].pdfFwd = BDPT.InfiniteLightDensity(integrator.infiniteLights, lightSampler, ray.d);
            }
            return nVertices + 1;
        }

        // The vertices of a subpath: path[off - 1] is the vertex before the walk, the vertex after is path[off + bounces]
        public static int RandomWalk(UnifiedTracer tracer, ref SampledWavelengths lambda, RayDifferential ray, BoundaryStack stack,
                                     Sampler sampler, ScratchBuffer scratchBuffer, SampledSpectrum beta, Float pdf, int maxDepth,
                                     TransportMode mode, Vertex[] path, int off, bool regularize,
                                     List<MediumEmissionEnd> emissionEnds = null)
        {
            if (maxDepth == 0)
            {
                return 0;
            }
            bool fromCamera = mode == TransportMode.Radiance;
            PathState state = new PathState(beta, new PAttenuation(beta), new PLight(beta), fromCamera, false, false);
            state.trackSegment = true;
            state.depthLimit = maxDepth;
            TraceBuffers buffers = new TraceBuffers();
            Float pdfFwd = pdf;
            int bounces = 0;
            bool anyNonSpecularBounces = false;
            while (true)
            {
                if (!state.beta)
                {
                    break;
                }
                int vIdx = off + bounces;
                // Where and along what the walk leaves the last vertex: the start of the edge, which is curved if it goes through a
                // refractive volume
                Point3f edgeStart = ray.o;
                Vector3f edgeDirection = Normalize(ray.d);
                ScatterEvent scatter = tracer.Trace(ref ray, ref state, ref stack, sampler, ref lambda, scratchBuffer, buffers);
                // A camera subpath gathers the emission of the medium at each of its collisions, like volpath does: the collision is the
                // last vertex of a path with no light subpath, and the strategies that sample the medium as a light share the weight (see
                // MISWeight). The emission end is kept next to the subpath: the walk goes on.
                if (emissionEnds != null && fromCamera)
                {
                    for (int i = 0; i < scatter.emission.Count; ++i)
                    {
                        if (scatter.emission[i].isMedium)
                        {
                            AddEmissionEnd(emissionEnds, scatter.emission[i], path, vIdx, edgeStart, edgeDirection, pdfFwd, ray.time);
                        }
                    }
                }
                if (scatter.kind == EventKind.Terminated)
                {
                    break;
                }
                if (scatter.kind == EventKind.Escaped)
                {
                    // Capture escaped rays when tracing from the camera. (not in pbrt-v4) A ray that escapes along a curved edge is not
                    // used: the connection to the light sampled from the vertex in the refractive medium is the only strategy for it.
                    if (mode == TransportMode.Radiance && !(scatter.curved && BDPT.RefractiveOf(path[vIdx - 1]) != null))
                    {
                        path[vIdx] = Vertex.CreateLight(new EndpointInteraction(new Ray(ray)), state.beta, pdfFwd);
                        StoreSegment(path, vIdx, ref state);
                        ++bounces;
                    }
                    break;
                }

                if (scatter.inMedium)
                {
                    MediumInteraction interaction = scatter.interaction.AsMedium();
                    path[vIdx] = Vertex.CreateMedium(interaction, state.beta, pdfFwd, path[vIdx - 1]);
                    path[vIdx].sigmaS0 = scatter.sigma_s[0];
                    FinishEdge(mode, path[vIdx - 1], path[vIdx], scatter.curvedMedium, edgeStart, edgeDirection,
                               (-1) * interaction.wo, pdfFwd);
                    StoreSegment(path, vIdx, ref state);
                    path[vIdx].stack = stack;
                    if (++bounces >= maxDepth)
                    {
                        break;
                    }
                    if (!tracer.Continue(scatter, ref ray, ref state, ref stack, sampler, out BSDFSample phaseSample))
                    {
                        break;
                    }
                    pdfFwd = phaseSample.pdf;
                    anyNonSpecularBounces = true;
                    path[vIdx - 1].pdfRev = path[vIdx].ConvertDensity(phaseSample.pdf, path[vIdx - 1], path[vIdx].EdgeBack());
                    continue;
                }

                SurfaceInteraction isect = scatter.interaction.AsSurface();
                BSDF bsdf = scatter.bsdf;
                if (regularize && anyNonSpecularBounces)
                {
                    bsdf.Regularize();
                    scatter.bsdf = bsdf;
                }
                path[vIdx] = Vertex.CreateSurface(isect, bsdf, state.beta, pdfFwd, path[vIdx - 1]);
                FinishEdge(mode, path[vIdx - 1], path[vIdx], scatter.curvedMedium, edgeStart, edgeDirection, Normalize(ray.d), pdfFwd);
                StoreSegment(path, vIdx, ref state);
                path[vIdx].stack = stack;
                if (++bounces >= maxDepth)
                {
                    break;
                }
                if (!tracer.Continue(scatter, ref ray, ref state, ref stack, sampler, out BSDFSample bs))
                {
                    break;
                }
                Vector3f wo = isect.wo;
                pdfFwd = bs.pdfIsProportional ? bsdf.PDF(wo, bs.wi, mode, BxDFReflTransFlags.All) : bs.pdf;
                anyNonSpecularBounces |= !bs.IsSpecular();
                Float pdfRev = bsdf.PDF(bs.wi, wo, !mode, BxDFReflTransFlags.All);
                if (bs.IsSpecular())
                {
                    path[vIdx].delta = true;
                    pdfRev = pdfFwd = 0;
                }
                path[vIdx - 1].pdfRev = path[vIdx].ConvertDensity(pdfRev, path[vIdx - 1], path[vIdx].EdgeBack());
            }
            return bounces;
        }

        // The collision of a camera subpath in an emissive medium as the vertex that ends a path: the emission, the null collisions on the
        // way, and the density of the collision (that of the direction at the previous vertex, and the majorant's along the ray)
        static void AddEmissionEnd(List<MediumEmissionEnd> emissionEnds, EmissionSample sample, Vertex[] path, int vIdx, Point3f edgeStart,
                                   Vector3f edgeDirection, Float pdfFwd, Float time)
        {
            VolumeEmissionLight emissionLight = BDPT.EmissionLightOf(sample.medium);
            if (emissionLight == null || !(bool)sample.beta)
            {
                return;
            }
            Float pdfCollision = sample.sigma_maj[0] * sample.T_maj[0];
            SampledSpectrum w = sample.T_maj / pdfCollision;
            Vertex end = Vertex.CreateLight(emissionLight, new Interaction(sample.p, time, sample.medium), sample.beta * w, 0);
            end.emission = sample.coefficient;
            // The density of a collision at the point is the majorant's: every collision gathers, null or not
            end.sigmaT0 = sample.sigma_maj[0];
            end.emissionMaj = sample.sigma_maj / sample.sigma_maj[0];
            end.segU = sample.segU * sample.T_maj / sample.T_maj[0];
            end.segL = sample.segL * sample.T_maj / sample.T_maj[0];
            end.cumU = path[vIdx - 1].cumU * end.segU;
            FinishEdge(TransportMode.Radiance, path[vIdx - 1], end, sample.curvedMedium, edgeStart, edgeDirection, edgeDirection, pdfFwd);
            end.pdfFwd = path[vIdx - 1].ConvertDensity(pdfFwd, end, end.EdgeIn()) * end.sigmaT0;
            end.stack = sample.stack;
            emissionEnds.Add(new MediumEmissionEnd(end, vIdx));
        }

        // The vertex at the end of an edge has been made: when the edge was curved, the curve between the two vertices is solved for its
        // directions and Jacobians, from the camera side vertex to the light side one whichever way the walk went (see
        // RefractiveMedium.SolveEdge), and the edge is stored in the direction of the walk: leaving the previous vertex, arriving at this
        // one, with the Jacobian of this direction and of the reverse. If the solver does not find the curve, the curve that the walk
        // followed is traced again from its start.
        public static void FinishEdge(TransportMode mode, Vertex previousVertex, Vertex currentVertex, RefractiveMedium edgeMedium, Point3f edgeStart,
                               Vector3f edgeDirection, Vector3f arrival, Float pdfFwd)
        {
            if (edgeMedium == null)
            {
                return;
            }
            Point3f previous = previousVertex.p();
            Point3f current = currentVertex.p();
            RefractiveEdge edge;
            bool found;
            if (mode == TransportMode.Radiance)
            {
                // The camera side is the previous vertex
                found = edgeMedium.SolveEdge(previous, current, edgeDirection, out edge);
            }
            else
            {
                // The camera side is this vertex: the solved curve leaves it along the reverse of the direction of arrival, and arrives at the
                // previous vertex against the direction that the walk leaves it along
                found = edgeMedium.SolveEdge(current, previous, (-1) * arrival, out RefractiveEdge solved);
                edge = new RefractiveEdge();
                edge.startDirection = (-1) * solved.endDirection;
                edge.endDirection = (-1) * solved.startDirection;
                edge.length = solved.length;
                edge.distance = solved.distance;
                edge.areaJacobian = solved.areaJacobianReverse;
                edge.areaJacobianReverse = solved.areaJacobian;
            }
            if (!found)
            {
                found = edgeMedium.TraceEdge(edgeStart, edgeDirection, current, out edge);
            }
            if (found)
            {
                currentVertex.curvedEdge = true;
                currentVertex.edge = edge;
                // The density of the vertex, per area perpendicular to the arrival, from the density of the direction it was sampled with
                currentVertex.pdfFwd = previousVertex.ConvertDensity(pdfFwd, currentVertex, currentVertex.EdgeIn());
            }
        }

        // The collisions of the segment that arrives at the new vertex go to the vertex, and the next segment starts empty
        static void StoreSegment(Vertex[] path, int index, ref PathState state)
        {
            path[index].segU = state.segU;
            path[index].segL = state.segL;
            path[index].vtx = state.vtx;
            path[index].cumU = path[index - 1].cumU * state.segU;
            state.ResetSegment();
        }
    }

    // TracerBDPTIntegrator Definition
    public sealed unsafe class TracerBDPTIntegrator : RayIntegrator
    {
        public TracerBDPTIntegrator(Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights, int maxDepth, bool regularize,
                                    bool gatherMediumEmission = true)
            : base(camera, sampler, aggregate, BDPTIntegrator.EmissionLightsForBDPT(lights))
        {
            this.gatherMediumEmission = gatherMediumEmission;
            this.maxDepth = maxDepth;
            this.bdptMaxDepth = maxDepth;
            this.regularize = regularize;
            this.lightSampler = new PowerLightSampler(this.lights.ToArray(), new Allocator());
            this.tracer = new UnifiedTracer(this, camera, maxDepth);
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler, ScratchBuffer scratchBuffer,
                                           VisibleSurface visibleSurface)
        {
            Vertex[] cameraVertices = NewVertices(maxDepth + 2);
            List<MediumEmissionEnd> emissionEnds = gatherMediumEmission ? new List<MediumEmissionEnd>() : null;
            int nCamera = TracerBDPT.GenerateCameraSubpath(tracer, ray, ref lambda, sampler, scratchBuffer, maxDepth + 2, camera,
                                                           cameraVertices, regularize, emissionEnds);
            Vertex[] lightVertices = NewVertices(maxDepth + 1);
            int nLight = TracerBDPT.GenerateLightSubpath(tracer, this, ref lambda, sampler, camera, scratchBuffer, maxDepth + 1,
                                                         cameraVertices[0].time(), lightSampler, lightVertices, regularize);
            SampledSpectrum L = new SampledSpectrum(0.0f);
            for (int t = 1; t <= nCamera; ++t)
            {
                for (int s = 0; s <= nLight; ++s)
                {
                    int depth = t + s - 2;
                    if ((s == 1 && t == 1) || depth < 0 || depth > maxDepth)
                    {
                        continue;
                    }
                    pstd.optional<Point2f> pFilmNew = default;
                    Float misWeight = 0.0f;
                    SampledSpectrum Lpath = BDPT.ConnectBDPT(this, ref lambda, lightVertices, cameraVertices, s, t, lightSampler, camera,
                                                            sampler, ref pFilmNew, ref misWeight, null, -ray.d);
                    if (t != 1)
                    {
                        L += Lpath;
                    }
                    else if ((bool)Lpath)
                    {
                        AddCameraSplat(camera, pFilmNew.value(), Lpath, lambda, sampler);
                    }
                }
            }
            if (emissionEnds != null)
            {
                foreach (MediumEmissionEnd end in emissionEnds)
                {
                    L += BDPT.ConnectEmissionEnd(this, ref lambda, cameraVertices, end, lightSampler, camera, sampler, null, -ray.d);
                }
            }
            return L;
        }

        // For the online integrator, which hands the paths of a scene with emissive media to this one
        public void NoCameraRayOf(CameraSample cameraSample, ref SampledWavelengths lambda, Sampler sampler, ScratchBuffer scratchBuffer)
        {
            NoCameraRay(cameraSample, ref lambda, sampler, scratchBuffer);
        }

        protected override void NoCameraRay(CameraSample cameraSample, ref SampledWavelengths lambda, Sampler sampler,
                                            ScratchBuffer scratchBuffer)
        {
            Vertex[] cameraVertices = NewVertices(maxDepth + 2);
            Vertex[] lightVertices = NewVertices(maxDepth + 1);
            int nLight = TracerBDPT.GenerateLightSubpath(tracer, this, ref lambda, sampler, camera, scratchBuffer, maxDepth + 1,
                                                         camera.SampleTime(cameraSample.time), lightSampler, lightVertices, regularize);
            for (int s = 0; s <= nLight; ++s)
            {
                int depth = s - 1;
                if (s == 1 || depth < 0 || depth > maxDepth)
                {
                    continue;
                }
                pstd.optional<Point2f> pFilmNew = default;
                Float misWeight = 0.0f;
                SampledSpectrum Lpath = BDPT.ConnectBDPT(this, ref lambda, lightVertices, cameraVertices, s, 1, lightSampler, camera,
                                                        sampler, ref pFilmNew, ref misWeight, null, default);
                if ((bool)Lpath)
                {
                    AddCameraSplat(camera, pFilmNew.value(), Lpath, lambda, sampler);
                }
            }
        }

        static Vertex[] NewVertices(int count)
        {
            Vertex[] vertices = new Vertex[count];
            for (int i = 0; i < vertices.Length; ++i)
            {
                vertices[i] = new Vertex();
            }
            return vertices;
        }

        public override string ToString()
        {
            return StringPrintf("[ TracerBDPTIntegrator maxDepth: %d regularize: %s lightSampler: %s ]", maxDepth, regularize, lightSampler);
        }

        public static TracerBDPTIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler, Primitive aggregate,
                                                  List<Light> lights, FileLoc loc)
        {
            if (!(camera is PerspectiveCamera) && !(camera is RealisticCamera))
            {
                ErrorExit("Only the \"perspective\" and \"realistic\" cameras are currently supported with the \"tracerbdpt\" integrator.");
            }
            return new TracerBDPTIntegrator(camera, sampler, aggregate, lights, parameters.GetOneInt("maxdepth", 5),
                                            parameters.GetOneBool("regularize", false),
                                            parameters.GetOneBool("gathermediumemission", true));
        }

        readonly int maxDepth;
        readonly bool regularize;
        readonly LightSampler lightSampler;
        readonly UnifiedTracer tracer;
    }

}  // namespace pbrt
