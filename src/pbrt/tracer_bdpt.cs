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
                                                ScratchBuffer scratchBuffer, int maxDepth, Camera camera, Vertex[] path, bool regularize)
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
                              TransportMode.Radiance, path, 1, regularize) + 1;
        }

        public static int GenerateLightSubpath(UnifiedTracer tracer, Integrator integrator, ref SampledWavelengths lambda, Sampler sampler,
                                               ScratchBuffer scratchBuffer, int maxDepth, Float time, LightSampler lightSampler,
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
            pstd.optional<LightLeSample> les = light.SampleLe(ul0, ul1, ref lambda, time);
            if (!les || les.value().pdfPos == 0 || les.value().pdfDir == 0 || !les.value().L)
            {
                return 0;
            }
            BoundaryStack stack = MediumTracking.Start(les.value().medium);
            RayDifferential ray = new RayDifferential(les.value().ray);

            Float p_l = lightSamplePDF * les.value().pdfPos;
            path[0] = les.value().intr ? Vertex.CreateLight(light, les.value().intr.value(), les.value().L, p_l)
                                       : Vertex.CreateLight(light, new Ray(ray), les.value().L, p_l);
            SampledSpectrum beta = les.value().L * les.value().AbsCosTheta(ray.d) / (p_l * les.value().pdfDir);
            path[0].stack = stack;
            int nVertices = RandomWalk(tracer, ref lambda, ray, stack, sampler, scratchBuffer, beta, les.value().pdfDir, maxDepth - 1,
                                       TransportMode.Importance, path, 1, regularize);
            // Correct subpath sampling densities for infinite area lights
            if (path[0].IsInfiniteLight())
            {
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
                                     TransportMode mode, Vertex[] path, int off, bool regularize)
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
                if (scatter.kind == EventKind.Terminated)
                {
                    break;
                }
                if (scatter.kind == EventKind.Escaped)
                {
                    // Capture escaped rays when tracing from the camera
                    if (mode == TransportMode.Radiance)
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
        public TracerBDPTIntegrator(Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights, int maxDepth, bool regularize)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
            this.bdptMaxDepth = maxDepth;
            this.regularize = regularize;
            this.lightSampler = new PowerLightSampler(lights.ToArray(), new Allocator());
            this.tracer = new UnifiedTracer(this, camera, maxDepth);
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler, ScratchBuffer scratchBuffer,
                                           VisibleSurface visibleSurface)
        {
            Vertex[] cameraVertices = NewVertices(maxDepth + 2);
            int nCamera = TracerBDPT.GenerateCameraSubpath(tracer, ray, ref lambda, sampler, scratchBuffer, maxDepth + 2, camera,
                                                           cameraVertices, regularize);
            Vertex[] lightVertices = NewVertices(maxDepth + 1);
            int nLight = TracerBDPT.GenerateLightSubpath(tracer, this, ref lambda, sampler, scratchBuffer, maxDepth + 1,
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
            return L;
        }

        protected override void NoCameraRay(CameraSample cameraSample, ref SampledWavelengths lambda, Sampler sampler,
                                            ScratchBuffer scratchBuffer)
        {
            Vertex[] cameraVertices = NewVertices(maxDepth + 2);
            Vertex[] lightVertices = NewVertices(maxDepth + 1);
            int nLight = TracerBDPT.GenerateLightSubpath(tracer, this, ref lambda, sampler, scratchBuffer, maxDepth + 1,
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
                                            parameters.GetOneBool("regularize", false));
        }

        readonly int maxDepth;
        readonly bool regularize;
        readonly LightSampler lightSampler;
        readonly UnifiedTracer tracer;
    }

}  // namespace pbrt
