// (not in pbrt-v4) tracer_bdpt_online.cs
//
// Bidirectional path tracing after UPBP / LuxCore BIDIRCPU, on the UnifiedTracer. The light subpath is stored (its vertices, as in
// UPBP) with the partial sums dVCM and dVC of the incremental MIS weights (the recurrence of the bdptinc note, in the area and volume
// measure of Vertex.pdfFwd / pdfRev, with the spectral factors segU / segL / vtx of media). The camera subpath is not stored: it is
// walked once, and at each of its vertices the connections to the light subpath are made and weighted from the running sums of that
// vertex and of the one before it. No camera array, no walk hook, no backwards walk over the path for a weight.
// Not in this version: polarisation, equiangular sampling, emissive media, Russian roulette, visualizestrategies.

using System;
using System.Collections.Generic;
using static pbrt.pbrt;
using PLight = pbrt.PolarisedLightSystem.Light;
using PAttenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{

    // PathSums Definition: the partial sums of a vertex of a subpath, and the product of the scattering coefficient ratios of the
    // vertices of the subpath up to it
    public struct PathSums
    {
        public SampledSpectrum dVCM, dVC, vtxProduct;

        public static PathSums Root()
        {
            PathSums sums = new PathSums();
            sums.dVCM = new SampledSpectrum(0.0f);
            sums.dVC = new SampledSpectrum(0.0f);
            sums.vtxProduct = new SampledSpectrum(1.0f);
            return sums;
        }
    }

    // OnlineBDPT Definition
    public static unsafe class OnlineBDPT
    {
        static Float Remap0(Float f)
        {
            return f != 0 ? f : 1;
        }

        // ------------------------------------------------------------------------------------------
        // The sums at the vertices of the subpaths

        // The sums of the vertices of a stored light subpath
        public static PathSums[] LightSums(Vertex[] path, int count)
        {
            PathSums[] sums = new PathSums[Math.Max(count, 1)];
            sums[0] = PathSums.Root();
            if (count == 0)
            {
                return sums;
            }
            Vertex root = path[0];
            if (!root.delta && !root.IsDeltaLight())
            {
                sums[0].dVCM = new SampledSpectrum(1 / Remap0(root.pdfFwd));
            }
            for (int k = 1; k < count; ++k)
            {
                Vertex v = path[k];
                Vertex before = path[k - 1];
                Float inverseForward = 1 / Remap0(v.pdfFwd);
                sums[k].vtxProduct = sums[k - 1].vtxProduct * v.vtx;
                SampledSpectrum dVCM = new SampledSpectrum(0.0f);
                if (!v.delta && !before.delta && BDPT.CurvedConnectionAllowed(v, before, v, 2))
                {
                    dVCM = before.cumU * v.segL * inverseForward;
                }
                sums[k].dVCM = dVCM;
                sums[k].dVC = v.segU * (sums[k - 1].dVCM + sums[k - 1].dVC) * (Remap0(before.pdfRev) * inverseForward);
            }
            return sums;
        }

        // A new vertex of the camera subpath: the part of its sums that its own segment gives
        public static PathSums BeginCamera(Vertex v, PathSums beforeSums)
        {
            PathSums sums = PathSums.Root();
            sums.vtxProduct = beforeSums.vtxProduct * v.vtx;
            return sums;
        }

        // The vertex of the camera subpath after the walk has scattered at it (the reverse density of the vertex before it is known)
        public static void FinishCamera(ref PathSums sums, Vertex v, Vertex before, PathSums beforeSums, Float splatScale, bool first)
        {
            Float inverseForward = 1 / Remap0(v.pdfFwd);
            SampledSpectrum dVCM = new SampledSpectrum(0.0f);
            if (!v.delta && !before.delta && BDPT.CurvedConnectionAllowed(v, before, v, first ? 1 : 2))
            {
                dVCM = before.cumU * v.segL * inverseForward;
                if (first)
                {
                    // See https://github.com/mmp/pbrt-v4/issues/347
                    dVCM *= 1 / splatScale;
                }
            }
            sums.dVCM = dVCM;
            sums.dVC = v.segU * (beforeSums.dVCM + beforeSums.dVC) * (Remap0(before.pdfRev) * inverseForward);
        }

        public static Float SplatScale(Camera camera)
        {
            Film film = camera.GetFilm();
            return (Float)film.FullResolution().x * (Float)film.FullResolution().y / (Float)film.PixelBounds().Area();
        }

        // ------------------------------------------------------------------------------------------
        // The weight of a connection

        // The sum, per wavelength, of the probabilities of the path under all the strategies, relative to the sample's strategy, from
        // the sums at the end vertices of the subpaths. pt is the end of the camera subpath (ignored for t = 1: the sampled camera
        // vertex), ptMinus the vertex before it. False if the weight is one.
        static bool MISSum(Integrator integrator, Camera camera, Vertex[] lightVertices, PathSums[] lightSums, Vertex cameraEnd,
                           Vertex cameraBefore, PathSums cameraBeforeSums, PathSums cameraEndSums, Vertex sampled, int s, int t,
                           LightSampler lightSampler, SampledSpectrum connRu, SampledSpectrum connRl, EdgeGeometry connectionToLight,
                           EdgeGeometry connectionToCamera, out SampledSpectrum sumOut)
        {
            sumOut = default;
            if (s + t == 2)
            {
                return false;
            }
            Vertex pt = t == 1 ? sampled : cameraEnd;
            Vertex qs = s == 0 ? null : (s == 1 && t > 1 ? sampled : lightVertices[s - 1]);
            Vertex ptMinus = t > 1 ? cameraBefore : null;
            Vertex qsMinus = s > 1 ? lightVertices[s - 2] : null;

            // The reverse densities of the end vertices and of the vertices before them, for the direction of the connection
            Float ptRev = s > 0 ? qs.PDF(integrator, qsMinus, pt, qs.EdgeBack(), connectionToCamera)
                            : pt.PDFLightOrigin(integrator.infiniteLights, ptMinus, lightSampler, pt.EdgeBack());
            Float ptMinusRev = 0;
            if (t >= 3)
            {
                ptMinusRev = s > 0 ? pt.PDF(integrator, qs, ptMinus, connectionToLight, pt.EdgeBack()) : pt.PDFLight(integrator, ptMinus, pt.EdgeBack());
            }
            Float qsRev = 0, qsMinusRev = 0;
            if (qs != null)
            {
                qsRev = pt.PDF(integrator, ptMinus, qs, pt.EdgeBack(), connectionToLight);
            }
            if (qsMinus != null)
            {
                qsMinusRev = qs.PDF(integrator, pt, qsMinus, connectionToCamera, qs.EdgeBack());
            }
            Float splatScale = SplatScale(camera);
            SampledSpectrum zero = new SampledSpectrum(0.0f);
            SampledSpectrum one = new SampledSpectrum(1.0f);

            // The strategies that connect on the camera subpath
            SampledSpectrum cameraSum = zero;
            if (t >= 2)
            {
                Float inverseForward = 1 / Remap0(pt.pdfFwd);
                SampledSpectrum dVCM = zero;
                if (!ptMinus.delta && integrator.StrategyAllowed(s + 1, t - 1) && BDPT.CurvedConnectionAllowed(pt, ptMinus, pt, t - 1))
                {
                    dVCM = ptMinus.cumU * pt.segL * inverseForward;
                    if (t - 1 == 1)
                    {
                        dVCM *= 1 / splatScale;
                    }
                }
                SampledSpectrum dVC = zero;
                if (t >= 3)
                {
                    dVC = pt.segU * (cameraBeforeSums.dVCM + cameraBeforeSums.dVC) * (Remap0(ptMinusRev) * inverseForward);
                }
                cameraSum = (dVCM + dVC) * Remap0(ptRev);
            }

            // The strategies that connect on the light subpath
            SampledSpectrum lightSum = zero;
            if (s >= 1)
            {
                Float inverseForward = 1 / Remap0(qs.pdfFwd);
                SampledSpectrum dVCM = zero;
                SampledSpectrum dVC = zero;
                if (s == 1)
                {
                    if (!qs.IsDeltaLight() && integrator.StrategyAllowed(0, t + 1))
                    {
                        dVCM = new SampledSpectrum(inverseForward);
                    }
                }
                else
                {
                    if (!qsMinus.delta && integrator.StrategyAllowed(s - 1, t + 1) && BDPT.CurvedConnectionAllowed(qs, qsMinus, qs, t + 1))
                    {
                        dVCM = qsMinus.cumU * qs.segL * inverseForward;
                    }
                    dVC = qs.segU * (lightSums[s - 2].dVCM + lightSums[s - 2].dVC) * (Remap0(qsMinusRev) * inverseForward);
                }
                lightSum = (dVCM + dVC) * Remap0(qsRev);
                if (t == 1)
                {
                    lightSum *= 1 / splatScale;
                }
            }

            // The sum: the sample's own strategy (1, with the spectral factors of the media), the camera side and the light side
            SampledSpectrum scatterVertices = (t >= 2 ? cameraEndSums.vtxProduct : one) * (s >= 2 ? lightSums[s - 1].vtxProduct : one);
            SampledSpectrum cameraWalked = pt.cumU;
            SampledSpectrum lightWalked = qs != null ? qs.cumU : one;
            SampledSpectrum connectionAsConnection = s >= 1 ? connRl : one;
            SampledSpectrum connectionAsWalk = s >= 1 ? connRu : one;
            sumOut = scatterVertices *
                (cameraWalked * lightWalked * connectionAsConnection +
                 connectionAsWalk * (lightWalked * cameraSum + cameraWalked * lightSum));
            return true;
        }

        static Float MISWeight(Integrator integrator, Camera camera, Vertex[] lightVertices, PathSums[] lightSums, Vertex cameraEnd,
                               Vertex cameraBefore, PathSums cameraBeforeSums, PathSums cameraEndSums, Vertex sampled, int s, int t,
                               LightSampler lightSampler, SampledSpectrum connRu, SampledSpectrum connRl,
                               EdgeGeometry connectionToLight = default, EdgeGeometry connectionToCamera = default)
        {
            if (!MISSum(integrator, camera, lightVertices, lightSums, cameraEnd, cameraBefore, cameraBeforeSums, cameraEndSums, sampled,
                        s, t, lightSampler, connRu, connRl, connectionToLight, connectionToCamera, out SampledSpectrum sum))
            {
                return 1;
            }
            Float average = sum.Average();
            return average > 0 ? 1 / average : 0;
        }

        // ------------------------------------------------------------------------------------------
        // The connections

        static Float GeometryTerm(Vertex v0, Vertex v1)
        {
            Vector3f d = v0.p() - v1.p();
            Float g = 1 / LengthSquared(d);
            d *= std.sqrt(g);
            if (v0.IsOnSurface())
            {
                g *= AbsDot(v0.ns(), d);
            }
            if (v1.IsOnSurface())
            {
                g *= AbsDot(v1.ns(), d);
            }
            return g;
        }

        // The strategy (s, t) with t >= 2 at the end vertex pt of the camera subpath: the contribution, weighted
        public static SampledSpectrum ConnectCameraVertex(Integrator integrator, SampledWavelengths lambda, Vertex[] lightVertices,
                                                          PathSums[] lightSums, Vertex pt, Vertex ptMinus, PathSums ptMinusSums,
                                                          PathSums ptSums, int s, int t, LightSampler lightSampler, Camera camera,
                                                          Sampler sampler)
        {
            SampledSpectrum L = new SampledSpectrum(0.0f);
            SampledSpectrum connRu = new SampledSpectrum(1.0f), connRl = new SampledSpectrum(1.0f);
            // The connection edge when it goes through a refractive medium: curved, in both directions
            EdgeGeometry curvedToLight = default, curvedToCamera = default;
            // Ignore invalid connections related to infinite area lights
            if (s != 0 && pt.type == VertexType.Light)
            {
                return L;
            }
            Vertex sampled = new Vertex();
            if (s == 0)
            {
                // Interpret the camera subpath as a complete path
                if (pt.IsLight())
                {
                    L = pt.Le(integrator.infiniteLights, ptMinus, lambda) * pt.beta;
                }
            }
            else if (s == 1)
            {
                // Sample a point on a light and connect it to the camera subpath
                if (pt.IsConnectible())
                {
                    pstd.optional<SampledLight> sampledLight = lightSampler.Sample(sampler.Get1D());
                    if (sampledLight)
                    {
                        Light light = sampledLight.value().light;
                        Float p_l = sampledLight.value().p;
                        LightSampleContext ctx = Integrator.LightContextAt(pt.GetInteraction(), pt.IsOnSurface(),
                                                                           pt.IsOnSurface() ? pt.bsdf.Flags() : default);
                        RefractiveMedium refractive = BDPT.RefractiveOf(pt);
                        if (refractive != null)
                        {
                            // From inside a refractive medium an area light is reached along a curved path to a point sampled by area
                            DiffuseAreaLight areaLight = light as DiffuseAreaLight;
                            PointLight pointLight = light as PointLight;
                            if (areaLight == null && pointLight == null)
                            {
                                ErrorExit("tracerbdptonline supports diffuse area lights and point lights only from inside a refractive medium.");
                            }
                            Interaction yIntr = default;
                            Float pdfPoint = 1;
                            SampledSpectrum pointIntensity = default;
                            bool sampledPoint;
                            if (pointLight != null)
                            {
                                // The intensity of the point is the radiance of the straight sample times the squared distance
                                pstd.optional<LightLiSample> pointSample = pointLight.SampleLi(ctx, sampler.Get2D(), lambda);
                                sampledPoint = pointSample && (bool)pointSample.value().L;
                                if (sampledPoint)
                                {
                                    yIntr = pointSample.value().pLight;
                                    pointIntensity = pointSample.value().L * DistanceSquared(pt.p(), yIntr.p());
                                }
                            }
                            else
                            {
                                sampledPoint = areaLight.SampleArea(sampler.Get2D(), out yIntr, out pdfPoint);
                            }
                            if (sampledPoint)
                            {
                                Point3f y = yIntr.p();
                                if (DistanceSquared(pt.p(), y) > 0 &&
                                    BDPT.CurvedConnection(integrator, refractive, pt, yIntr, true, lambda, out curvedToLight,
                                                          out curvedToCamera, out int count, out SampledSpectrum curvedT, out connRu,
                                                          out connRl))
                                {
                                    SampledSpectrum Le = pointLight != null ? pointIntensity : light.L(y, yIntr.n, yIntr.uv, (-1) * curvedToLight.arriving, lambda, yIntr.time);
                                    EndpointInteraction ei = new EndpointInteraction(light, yIntr);
                                    sampled = Vertex.CreateLight(ei, Le / (pdfPoint * p_l), 0);
                                    sampled.pdfFwd = sampled.PDFLightOrigin(integrator.infiniteLights, pt, lightSampler, curvedToCamera);
                                    L = pt.beta * pt.f(sampled, TransportMode.Radiance, curvedToLight) * sampled.beta;
                                    L *= BDPT.GeometryTerm(pt, sampled, curvedToLight) * (Float)count * curvedT;
                                }
                            }
                        }
                        else
                        {
                            pstd.optional<LightLiSample> lightWeight = light.SampleLi(ctx, sampler.Get2D(), lambda);
                            if (lightWeight && (bool)lightWeight.value().L && lightWeight.value().pdf > 0)
                            {
                                EndpointInteraction ei = new EndpointInteraction(light, lightWeight.value().pLight);
                                sampled = Vertex.CreateLight(ei, lightWeight.value().L / (lightWeight.value().pdf * p_l), 0);
                                sampled.pdfFwd = sampled.PDFLightOrigin(integrator.infiniteLights, pt, lightSampler);
                                L = pt.beta * pt.f(sampled, TransportMode.Radiance) * sampled.beta;
                                if (pt.IsOnSurface())
                                {
                                    L *= AbsDot(lightWeight.value().wi, pt.ns());
                                }
                                if ((bool)L)
                                {
                                    L *= integrator.TrSegments(pt.GetInteraction(), lightWeight.value().pLight, pt.stack, lambda,
                                                               out connRu, out connRl, out Medium _);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Handle all other bidirectional connection cases
                Vertex qs = lightVertices[s - 1];
                if (qs.IsConnectible() && pt.IsConnectible())
                {
                    // From inside a refractive medium the connection is a curved path
                    bool cameraSideInside = BDPT.RefractiveOf(pt) != null;
                    RefractiveMedium refractive = cameraSideInside ? BDPT.RefractiveOf(pt) : BDPT.RefractiveOf(qs);
                    if (refractive != null)
                    {
                        if (BDPT.CurvedConnection(integrator, refractive, cameraSideInside ? pt : qs,
                                                  (cameraSideInside ? qs : pt).GetInteraction(), cameraSideInside, lambda,
                                                  out curvedToLight, out curvedToCamera, out int count, out SampledSpectrum curvedT,
                                                  out connRu, out connRl))
                        {
                            L = qs.beta * qs.f(pt, TransportMode.Importance, curvedToCamera) *
                                pt.f(qs, TransportMode.Radiance, curvedToLight) * pt.beta;
                            if ((bool)L)
                            {
                                L *= BDPT.GeometryTerm(pt, qs, curvedToLight) * (Float)count * curvedT;
                            }
                        }
                    }
                    else
                    {
                        L = qs.beta * qs.f(pt, TransportMode.Importance) * pt.f(qs, TransportMode.Radiance) * pt.beta;
                        if ((bool)L)
                        {
                            L *= GeometryTerm(qs, pt);
                            L *= integrator.TrSegments(qs.GetInteraction(), pt.GetInteraction(), qs.stack, lambda, out connRu,
                                                       out connRl, out Medium _);
                        }
                    }
                }
            }
            if (!L)
            {
                return L;
            }
            Float misWeight = MISWeight(integrator, camera, lightVertices, lightSums, pt, ptMinus, ptMinusSums, ptSums, sampled, s, t,
                                        lightSampler, connRu, connRl, curvedToLight, curvedToCamera);
            return L * misWeight;
        }

        // The strategy (s, 1): the light vertex s - 1 connected to a point sampled on the camera. The contribution goes to the film at
        // pRaster.
        public static SampledSpectrum ConnectToCamera(Integrator integrator, ref SampledWavelengths lambdaRef, Vertex[] lightVertices,
                                                      PathSums[] lightSums, int s, LightSampler lightSampler, Camera camera,
                                                      Sampler sampler, out pstd.optional<Point2f> pRaster)
        {
            pRaster = default;
            SampledWavelengths lambda = lambdaRef;
            SampledSpectrum L = new SampledSpectrum(0.0f);
            SampledSpectrum connRu = new SampledSpectrum(1.0f), connRl = new SampledSpectrum(1.0f);
            Vertex qs = lightVertices[s - 1];
            Vertex sampled = new Vertex();
            if (!qs.IsConnectible())
            {
                return L;
            }
            pstd.optional<CameraWiSample> cs = camera.SampleWi(qs.GetInteraction(), sampler.Get2D(), ref lambda);
            lambdaRef = lambda;
            if (!cs)
            {
                return L;
            }
            pRaster = new pstd.optional<Point2f>(cs.value().pRaster);
            sampled = Vertex.CreateCamera(camera, cs.value().pLens, cs.value().Wi / cs.value().pdf);
            L = qs.beta * qs.f(sampled, TransportMode.Importance) * sampled.beta;
            if (qs.IsOnSurface())
            {
                L *= AbsDot(cs.value().wi, qs.ns());
            }
            if (!(bool)L)
            {
                return L;
            }
            L *= integrator.TrSegments(cs.value().pRef, cs.value().pLens, qs.stack, lambda, out connRu, out connRl, out Medium _);
            // See https://github.com/mmp/pbrt-v4/issues/347
            L *= SplatScale(camera);
            if (!L)
            {
                return L;
            }
            Float misWeight = MISWeight(integrator, camera, lightVertices, lightSums, null, null, default, default, sampled, s, 1,
                                        lightSampler, connRu, connRl);
            return L * misWeight;
        }
    }

    // TracerBDPTOnlineIntegrator Definition
    public sealed unsafe class TracerBDPTOnlineIntegrator : RayIntegrator
    {
        public TracerBDPTOnlineIntegrator(Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights, int maxDepth,
                                          bool regularize, bool gatherMediumEmission = true)
            : base(camera, sampler, aggregate, BDPTIntegrator.EmissionLightsForBDPT(lights))
        {
            this.gatherMediumEmission = gatherMediumEmission;
            // The weights of the strategies that sample or gather the emission of a medium need the whole camera subpath (see
            // BDPT.MISSum), which this integrator does not store: a scene with emissive media is rendered by tracerbdpt, which has the same
            // strategies and weights from stored subpaths
            foreach (Light light in this.lights)
            {
                if (light is VolumeEmissionLight)
                {
                    emissiveFallback = new TracerBDPTIntegrator(camera, sampler, aggregate, lights, maxDepth, regularize, gatherMediumEmission);
                    break;
                }
            }
            this.maxDepth = maxDepth;
            this.bdptMaxDepth = maxDepth;
            this.regularize = regularize;
            this.lightSampler = new PowerLightSampler(this.lights.ToArray(), new Allocator());
            this.tracer = new UnifiedTracer(this, camera, maxDepth);
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler, ScratchBuffer scratchBuffer,
                                           VisibleSurface visibleSurface)
        {
            if (emissiveFallback != null)
            {
                return emissiveFallback.Li(ray, ref lambda, sampler, scratchBuffer, visibleSurface);
            }
            SampledSpectrum L = new SampledSpectrum(0.0f);

            // The light subpath is stored, with its sums
            Vertex[] lightVertices = NewVertices(maxDepth + 1);
            int nLight = TracerBDPT.GenerateLightSubpath(tracer, this, ref lambda, sampler, camera, scratchBuffer, maxDepth + 1, ray.time,
                                                         lightSampler, lightVertices, regularize);
            PathSums[] lightSums = OnlineBDPT.LightSums(lightVertices, nLight);

            // t = 1: the light vertices connect to the camera
            SplatLightVertices(ref lambda, lightVertices, lightSums, nLight, sampler);

            // The camera subpath is walked once, and connected at each of its vertices
            Float splatScale = OnlineBDPT.SplatScale(camera);
            BoundaryStack stack = MediumTracking.Start(camera.GetMedium());
            Vertex prev = Vertex.CreateCamera(camera, new Ray(ray), new SampledSpectrum(1.0f));
            prev.stack = stack;
            Float pdfPos, pdfDir;
            camera.PDF_We(new Ray(ray), &pdfPos, &pdfDir);
            PathSums prevSums = PathSums.Root();
            SampledSpectrum beta = new SampledSpectrum(1.0f);
            PathState state = new PathState(beta, new PAttenuation(beta), new PLight(beta), true, false, false);
            state.trackSegment = true;
            int walkLimit = maxDepth + 1;
            state.depthLimit = walkLimit;
            TraceBuffers buffers = new TraceBuffers();
            Float pdfFwd = pdfDir;
            int bounces = 0;
            bool anyNonSpecularBounces = false;
            RayDifferential cameraRay = ray;
            while (true)
            {
                if (!state.beta)
                {
                    break;
                }
                // Where and along what the walk leaves the last vertex: a curved edge is solved from there
                Point3f edgeStart = cameraRay.o;
                Vector3f edgeDirection = Normalize(cameraRay.d);
                ScatterEvent scatter = tracer.Trace(ref cameraRay, ref state, ref stack, sampler, ref lambda, scratchBuffer, buffers);
                if (scatter.kind == EventKind.Terminated)
                {
                    break;
                }
                int t = bounces + 2;
                Vertex cur;
                if (scatter.kind == EventKind.Escaped)
                {
                    cur = Vertex.CreateLight(new EndpointInteraction(new Ray(cameraRay)), state.beta, pdfFwd);
                }
                else if (scatter.inMedium)
                {
                    cur = Vertex.CreateMedium(scatter.interaction.AsMedium(), state.beta, pdfFwd, prev);
                    cur.sigmaS0 = scatter.sigma_s[0];
                    TracerBDPT.FinishEdge(TransportMode.Radiance, prev, cur, scatter.curvedMedium, edgeStart, edgeDirection,
                                          (-1) * scatter.interaction.AsMedium().wo, pdfFwd);
                }
                else
                {
                    BSDF bsdf = scatter.bsdf;
                    if (regularize && anyNonSpecularBounces)
                    {
                        bsdf.Regularize();
                        scatter.bsdf = bsdf;
                    }
                    cur = Vertex.CreateSurface(scatter.interaction.AsSurface(), bsdf, state.beta, pdfFwd, prev);
                    TracerBDPT.FinishEdge(TransportMode.Radiance, prev, cur, scatter.curvedMedium, edgeStart, edgeDirection,
                                          Normalize(cameraRay.d), pdfFwd);
                }
                cur.segU = state.segU;
                cur.segL = state.segL;
                cur.vtx = state.vtx;
                cur.cumU = prev.cumU * state.segU;
                state.ResetSegment();
                cur.stack = stack;
                PathSums curSums = OnlineBDPT.BeginCamera(cur, prevSums);

                // All the strategies that end the camera subpath at this vertex
                for (int s = 0; s <= nLight; ++s)
                {
                    int depth = t + s - 2;
                    if (depth < 0 || depth > maxDepth)
                    {
                        continue;
                    }
                    L += OnlineBDPT.ConnectCameraVertex(this, lambda, lightVertices, lightSums, cur, prev, prevSums, curSums, s, t,
                                                        lightSampler, camera, sampler);
                }
                if (scatter.kind == EventKind.Escaped)
                {
                    break;
                }
                if (++bounces >= walkLimit)
                {
                    break;
                }
                if (!tracer.Continue(scatter, ref cameraRay, ref state, ref stack, sampler, out BSDFSample sample))
                {
                    break;
                }
                if (scatter.inMedium)
                {
                    pdfFwd = sample.pdf;
                    anyNonSpecularBounces = true;
                    prev.pdfRev = cur.ConvertDensity(sample.pdf, prev, cur.EdgeBack());
                }
                else
                {
                    SurfaceInteraction isect = scatter.interaction.AsSurface();
                    Vector3f wo = isect.wo;
                    pdfFwd = sample.pdfIsProportional ? scatter.bsdf.PDF(wo, sample.wi, TransportMode.Radiance, BxDFReflTransFlags.All)
                                                      : sample.pdf;
                    anyNonSpecularBounces |= !sample.IsSpecular();
                    Float pdfRev = scatter.bsdf.PDF(sample.wi, wo, TransportMode.Importance, BxDFReflTransFlags.All);
                    if (sample.IsSpecular())
                    {
                        cur.delta = true;
                        pdfRev = pdfFwd = 0;
                    }
                    prev.pdfRev = cur.ConvertDensity(pdfRev, prev, cur.EdgeBack());
                }
                OnlineBDPT.FinishCamera(ref curSums, cur, prev, prevSums, splatScale, t == 2);
                prev = cur;
                prevSums = curSums;
            }
            return L;
        }

        void SplatLightVertices(ref SampledWavelengths lambda, Vertex[] lightVertices, PathSums[] lightSums, int nLight, Sampler sampler)
        {
            for (int s = 2; s <= nLight; ++s)
            {
                int depth = s - 1;
                if (depth > maxDepth)
                {
                    continue;
                }
                SampledSpectrum Lpath = OnlineBDPT.ConnectToCamera(this, ref lambda, lightVertices, lightSums, s, lightSampler, camera,
                                                                   sampler, out pstd.optional<Point2f> pFilmNew);
                if ((bool)Lpath)
                {
                    AddCameraSplat(camera, pFilmNew.value(), Lpath, lambda, sampler);
                }
            }
        }

        protected override void NoCameraRay(CameraSample cameraSample, ref SampledWavelengths lambda, Sampler sampler,
                                            ScratchBuffer scratchBuffer)
        {
            if (emissiveFallback != null)
            {
                emissiveFallback.NoCameraRayOf(cameraSample, ref lambda, sampler, scratchBuffer);
                return;
            }
            Vertex[] lightVertices = NewVertices(maxDepth + 1);
            int nLight = TracerBDPT.GenerateLightSubpath(tracer, this, ref lambda, sampler, camera, scratchBuffer, maxDepth + 1,
                                                         camera.SampleTime(cameraSample.time), lightSampler, lightVertices, regularize);
            PathSums[] lightSums = OnlineBDPT.LightSums(lightVertices, nLight);
            SplatLightVertices(ref lambda, lightVertices, lightSums, nLight, sampler);
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
            return StringPrintf("[ TracerBDPTOnlineIntegrator maxDepth: %d regularize: %s lightSampler: %s ]", maxDepth, regularize,
                                lightSampler);
        }

        public static TracerBDPTOnlineIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                                        Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            if (!(camera is PerspectiveCamera) && !(camera is RealisticCamera))
            {
                ErrorExit("Only the \"perspective\" and \"realistic\" cameras are currently supported with the \"tracerbdptonline\" integrator.");
            }
            return new TracerBDPTOnlineIntegrator(camera, sampler, aggregate, lights, parameters.GetOneInt("maxdepth", 5),
                                                  parameters.GetOneBool("regularize", false),
                                                  parameters.GetOneBool("gathermediumemission", true));
        }

        readonly int maxDepth;
        readonly bool regularize;
        readonly LightSampler lightSampler;
        readonly UnifiedTracer tracer;
        readonly TracerBDPTIntegrator emissiveFallback;
    }

}  // namespace pbrt
