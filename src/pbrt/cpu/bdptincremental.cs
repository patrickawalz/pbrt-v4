// (not in pbrt-v4) cpu/bdptincremental.cs
//
// "bdptinc": bidirectional path tracing whose multiple importance sampling weights come from partial sums stored at the vertices
// of the subpaths (dVCM and dVC, after Georgiev et al.'s vertex connection and merging, SmallVCM/SmallUPBP and LuxCore's BiDir
// engines, without the merging), and not from the backward walk over the path that BDPT.MISSum makes for every connection. The
// weight of a connection is read from the two end vertices of the subpaths in O(1).
//
// What the sums are. For a path with a given set of vertices the balance heuristic weight of the strategy that made it is 1 / sum,
// with sum the probabilities of the path under all strategies relative to the one that made it. bdpt's MISSum evaluates that sum
// as 1 + the camera side + the light side, each a sum over the vertices of the subpath of products of the ratios
// rho_j = pdfRev_j / pdfFwd_j (the densities per area, or per volume in a medium, of pbrt's Vertex) of the vertices between the
// vertex and the end, and of the spectral factors of the media. Written as a recurrence along the subpath:
//
//     T_k = rho_k (a_k cumU_(k-1) segL_k + segU_k T_(k-1))          rho_k = pdfRev_k / pdfFwd_k
//     T_k = pdfRev_k (dVCM_k + dVC_k)
//     dVCM_k = a_k cumU_(k-1) segL_k / pdfFwd_k                      (the strategy that connects at vertex k)
//     dVC_k = segU_k pdfRev_(k-1) (dVCM_(k-1) + dVC_(k-1)) / pdfFwd_k  (the strategies at the vertices before k)
//
// a_k is 1 if the strategy that connects there is a strategy that exists (neither of the vertices is specular). dVCM_k is known
// when the vertex is made, and dVC_k when the walk has scattered at the vertex, which gives pdfRev_(k-1). The reverse densities
// of the last two vertices of a subpath depend on the vertex that the connection joins them to (they are not those of the walk),
// so a connection computes those four itself and combines them with the stored dVCM and dVC of the vertex before the last: this
// is the O(1) of the connection. segL, segU, cumU and vtx are bdpt's spectral factors for the media (chromatic majorants,
// hero wavelength): the sums are SampledSpectrum, and with a medium that is grey, or without a medium, all the factors are one.
// There is one engine for surfaces, media and polarised light; the recurrences have no branch on any of them.

using System;
using System.Collections.Generic;
using PLight = pbrt.PolarisedLightSystem.Light;
using PAttenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    using static pbrt;

    // VertexMIS Definition: the partial sums of a vertex of a subpath, and the prefix products that the connections of the media
    // and the equiangular strategy read from the end vertices
    public struct VertexMIS
    {
        public SampledSpectrum dVCM, dVC;
        // The product of the scattering coefficient ratios of the vertices 1 to k
        public SampledSpectrum vtxProduct;
        // (light subpaths, for the equiangular strategy) the same from vertex 2, the product of segU from vertex 3, and the product
        // of the ratios rho_j of the vertices 1 to k - 1
        public SampledSpectrum vtxProductFrom2, segUProductFrom3;
        public Float rhoProductBefore;

        public static VertexMIS Root()
        {
            VertexMIS m = new VertexMIS();
            m.dVCM = new SampledSpectrum(0.0f);
            m.dVC = new SampledSpectrum(0.0f);
            m.vtxProduct = new SampledSpectrum(1.0f);
            m.vtxProductFrom2 = new SampledSpectrum(1.0f);
            m.segUProductFrom3 = new SampledSpectrum(1.0f);
            m.rhoProductBefore = 1;
            return m;
        }
    }

    public static unsafe class BDPTIncremental
    {
        static Float Remap0(Float f)
        {
            return f != 0 ? f : 1;
        }

        // ------------------------------------------------------------------------------------------
        // The sums at the vertices of the subpaths

        // The prefix products of a new vertex k of a subpath, once its segment is stored
        public static void BeginVertex(Vertex[] path, VertexMIS[] state, int k, bool lightSubpath)
        {
            state[k].vtxProduct = state[k - 1].vtxProduct * path[k].vtx;
            if (lightSubpath)
            {
                state[k].vtxProductFrom2 = k >= 2 ? state[k - 1].vtxProductFrom2 * path[k].vtx : new SampledSpectrum(1.0f);
                state[k].segUProductFrom3 = k >= 3 ? state[k - 1].segUProductFrom3 * path[k].segU : new SampledSpectrum(1.0f);
            }
        }

        // The vertex k of a camera subpath after the walk has scattered at it (the reverse density of vertex k - 1 is known)
        public static void FinishCameraVertex(Integrator integrator, Vertex[] path, VertexMIS[] state, int k, Float splatScale)
        {
            Vertex v = path[k];
            Vertex before = path[k - 1];
            Float inverseForward = 1 / Remap0(v.pdfFwd);
            SampledSpectrum dVCM = new SampledSpectrum(0.0f);
            if (!integrator.unidirectionalStrategies && !v.delta && !before.delta)
            {
                dVCM = before.cumU * v.segL * inverseForward;
                if (k == 1)
                {
                    // See https://github.com/mmp/pbrt-v4/issues/347
                    dVCM *= 1 / splatScale;
                }
            }
            state[k].dVCM = dVCM;
            state[k].dVC = v.segU * (state[k - 1].dVCM + state[k - 1].dVC) * (Remap0(before.pdfRev) * inverseForward);
        }

        // The first vertex of a light subpath: the strategy with no light vertex (the camera subpath hits the light)
        public static void BeginLightRoot(Integrator integrator, Vertex root, VertexMIS[] state)
        {
            state[0] = VertexMIS.Root();
            bool volumeEmission = BDPT.IsVolumeEmission(root);
            bool allowed = !root.delta && !root.IsDeltaLight() && !(volumeEmission && !integrator.gatherMediumEmission);
            if (allowed)
            {
                Float inverseForward = 1 / Remap0(root.pdfFwd);
                state[0].dVCM = volumeEmission ? root.emissionMaj * (root.sigmaT0 * inverseForward)
                                               : new SampledSpectrum(inverseForward);
            }
        }

        // The vertex k >= 1 of a light subpath after the walk has scattered at it
        public static void FinishLightVertex(Vertex[] path, VertexMIS[] state, int k)
        {
            Vertex v = path[k];
            Vertex before = path[k - 1];
            Float inverseForward = 1 / Remap0(v.pdfFwd);
            SampledSpectrum dVCM = new SampledSpectrum(0.0f);
            if (!v.delta && !before.delta)
            {
                dVCM = before.cumU * v.segL * inverseForward;
            }
            state[k].dVCM = dVCM;
            state[k].dVC = v.segU * (state[k - 1].dVCM + state[k - 1].dVC) * (Remap0(before.pdfRev) * inverseForward);
            state[k].rhoProductBefore = k == 1 ? 1 : state[k - 1].rhoProductBefore * (Remap0(before.pdfRev) / Remap0(before.pdfFwd));
        }

        // ------------------------------------------------------------------------------------------
        // Subpaths

        public static int GenerateCameraSubpath(Integrator integrator, RayDifferential ray, ref SampledWavelengths lambda,
                                                Sampler sampler, ScratchBuffer scratchBuffer, int maxDepth, Camera camera,
                                                Vertex[] path, VertexMIS[] state, bool regularize, Float splatScale,
                                                bool polarised, List<MediumEmissionEnd> emissionEnds,
                                                BDPTEquiangularSink equiangularSink)
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
            state[0] = VertexMIS.Root();
            camera.PDF_We(new Ray(ray), &pdfPos, &pdfDir);
            return RandomWalk(integrator, ref lambda, ray, stack, sampler, camera, scratchBuffer, beta, pdfDir, maxDepth - 1,
                              TransportMode.Radiance, path, state, 1, regularize, splatScale, polarised, emissionEnds,
                              equiangularSink, -1, default) + 1;
        }

        public static int GenerateLightSubpath(Integrator integrator, ref SampledWavelengths lambda, Sampler sampler, Camera camera,
                                               ScratchBuffer scratchBuffer, int maxDepth, Float time, LightSampler lightSampler,
                                               Vertex[] path, VertexMIS[] state, bool regularize, Float splatScale,
                                               bool polarised)
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
            VolumeEmissionLight volumeLight = light as VolumeEmissionLight;
            pstd.optional<LightLeSample> les = volumeLight != null
                ? volumeLight.SampleLeVolume(ul0, ul1, sampler.Get1D(), lambda, time)
                : light.SampleLe(ul0, ul1, ref lambda, time);
            BoundaryStack stack = default;
            if (volumeLight != null)
            {
                bool valid = les && les.value().pdfPos != 0 && les.value().pdfDir != 0 && (bool)les.value().L;
                if (valid)
                {
                    stack = BDPT.StackAtPoint(integrator, camera, les.value().ray.o, time);
                    valid = MediumTracking.Current(stack) == volumeLight.Medium();
                }
                if (!valid)
                {
                    path[0] = Vertex.CreateLight(light, new Ray(new Point3f(0, 0, 0), new Vector3f(0, 0, 1), time),
                                                 new SampledSpectrum(0.0f), lightSamplePDF);
                    path[0].stack = stack;
                    BeginLightRoot(integrator, path[0], state);
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

            if (path[0].ei.light is VolumeEmissionLight emissiveMedium)
            {
                SampledSpectrum majorant = emissiveMedium.Medium().MajorantAt(ray.o, lambda, ray.time);
                path[0].sigmaT0 = majorant[0];
                path[0].emissionMaj = majorant / majorant[0];
            }

            // The densities of an infinite area light are known before the walk (bdpt corrects them after it), so that the sums of
            // the vertices are made with the final densities
            Float firstVertexDensity = -1;
            if (path[0].IsInfiniteLight())
            {
                firstVertexDensity = les.value().pdfPos;
                path[0].pdfFwd = BDPT.InfiniteLightDensity(integrator.infiniteLights, lightSampler, ray.d);
            }
            path[0].stack = stack;
            BeginLightRoot(integrator, path[0], state);

            SampledSpectrum beta = les.value().L * les.value().AbsCosTheta(ray.d) / (p_l * les.value().pdfDir);
            int nVertices = RandomWalk(integrator, ref lambda, ray, stack, sampler, camera, scratchBuffer, beta,
                                       les.value().pdfDir, maxDepth - 1, TransportMode.Importance, path, state, 1, regularize,
                                       splatScale, polarised, null, null, firstVertexDensity, ray.d);
            return nVertices + 1;
        }

        public static int RandomWalk(Integrator integrator, ref SampledWavelengths lambdaRef, RayDifferential ray,
                                     BoundaryStack stack, Sampler sampler, Camera camera, ScratchBuffer scratchBuffer,
                                     SampledSpectrum beta, Float pdf, int maxDepth, TransportMode mode, Vertex[] path,
                                     VertexMIS[] state, int off, bool regularize, Float splatScale, bool polarised,
                                     List<MediumEmissionEnd> emissionEnds, BDPTEquiangularSink equiangularSink,
                                     Float firstVertexDensity, Vector3f firstDirection)
        {
            if (maxDepth == 0)
            {
                return 0;
            }
            bool fromCamera = mode == TransportMode.Radiance;
            WalkContext ctx = new WalkContext();
            ctx.integrator = integrator;
            ctx.lambda = lambdaRef;
            ctx.ray = ray;
            ctx.stack = stack;
            ctx.sampler = sampler;
            ctx.camera = camera;
            ctx.scratchBuffer = scratchBuffer;
            ctx.mode = mode;
            ctx.st = new PathState(beta, new PAttenuation(beta), new PLight(beta), fromCamera, polarised, false);
            ctx.st.trackSegment = true;
            IncrementalWalkHook hook = new IncrementalWalkHook(path, state, off, maxDepth, pdf, regularize, splatScale,
                                                               emissionEnds, equiangularSink, firstVertexDensity, firstDirection);
            try
            {
                PathWalk.Run(ctx, hook);
                return hook.bounces;
            }
            finally
            {
                lambdaRef = ctx.lambda;
            }
        }

        // The vertices of a subpath with their densities and sums
        sealed class IncrementalWalkHook : PathWalkHook
        {
            public IncrementalWalkHook(Vertex[] path, VertexMIS[] state, int off, int maxDepth, Float pdfFwd, bool regularize,
                                       Float splatScale, List<MediumEmissionEnd> emissionEnds,
                                       BDPTEquiangularSink equiangularSink, Float firstVertexDensity, Vector3f firstDirection)
            {
                this.path = path;
                this.state = state;
                this.off = off;
                this.maxDepth = maxDepth;
                this.pdfFwd = pdfFwd;
                this.regularize = regularize;
                this.splatScale = splatScale;
                this.emissionEnds = emissionEnds;
                this.equiangularSink = equiangularSink;
                this.firstVertexDensity = firstVertexDensity;
                this.firstDirection = firstDirection;
            }

            public int bounces = 0;
            Vertex[] path;
            VertexMIS[] state;
            int off, maxDepth, vIdx;
            Float pdfFwd;
            bool regularize;
            Float splatScale;
            bool anyNonSpecularBounces = false;
            List<MediumEmissionEnd> emissionEnds;
            BDPTEquiangularSink equiangularSink;
            int segmentPieces = 0;
            Float firstVertexDensity;
            Vector3f firstDirection;

            // The collisions of the segment that arrives at the new vertex go to the vertex, and the next segment starts empty
            void StoreSegment(WalkContext ctx, int index)
            {
                path[index].segU = ctx.st.segU;
                path[index].segL = ctx.st.segL;
                path[index].vtx = ctx.st.vtx;
                path[index].cumU = path[index - 1].cumU * ctx.st.segU;
                ctx.st.ResetSegment();
                segmentPieces = 0;
                // The first vertex of the subpath of an infinite area light has the density of its position
                if (index == off && firstVertexDensity >= 0)
                {
                    path[index].pdfFwd = firstVertexDensity;
                    if (path[index].IsOnSurface())
                    {
                        path[index].pdfFwd *= AbsDot(firstDirection, path[index].ng());
                    }
                }
                BeginVertex(path, state, index, ctx.mode == TransportMode.Importance);
            }

            void FinishVertex(WalkContext ctx, int index)
            {
                if (ctx.mode == TransportMode.Radiance)
                {
                    FinishCameraVertex(ctx.integrator, path, state, index, splatScale);
                }
                else
                {
                    FinishLightVertex(path, state, index);
                }
            }

            public override void BeginSegment(WalkContext ctx, Medium medium, Float tMax)
            {
                ++segmentPieces;
                if (equiangularSink == null || ctx.mode != TransportMode.Radiance || segmentPieces != 1 || !(bool)ctx.st.beta ||
                    vIdx > equiangularSink.maxDepth)
                {
                    return;
                }
                int count = ctx.integrator.EquiangularCount(vIdx);
                for (int i = 0; i < count; ++i)
                {
                    equiangularSink.L += SampleEquiangularVertex(ctx, medium, tMax, count);
                }
            }

            // The contribution of the path that has a scattering point on the segment that the walk is about to sample, found by
            // choosing a light, a point on it and then the distance along the segment by the angle seen from that point (Fong et
            // al., 4.2.2 and 4.2.3), and connected to the point. The weight is that of bdpt (see BDPT.EquiangularTerm), with the sum
            // of the strategies from the sums of the vertices.
            SampledSpectrum SampleEquiangularVertex(WalkContext ctx, Medium medium, Float tMax, int count)
            {
                SampledSpectrum zero = new SampledSpectrum(0.0f);
                BDPTIncrementalIntegrator bdpt = (BDPTIncrementalIntegrator)ctx.integrator;
                LightSampler lightSampler = bdpt.BDPTLightSampler;
                SampledWavelengths lambda = ctx.lambda;
                Sampler sampler = ctx.sampler;
                Float uLightChoice = sampler.Get1D();
                Float uDistance = sampler.Get1D();
                Point2f uLight = sampler.Get2D();
                pstd.optional<SampledLight> sampledLight = ctx.integrator.bdptEquiangularLightSampler.Sample(uLightChoice);
                if (!sampledLight)
                {
                    return zero;
                }
                Light light = sampledLight.value().light;
                DiffuseAreaLight areaLight = light as DiffuseAreaLight;
                Float length = Length(ctx.ray.d);
                Point3f origin = ctx.ray.o;
                Vector3f direction = ctx.ray.d / length;
                Float segmentLength = tMax * length;
                LightSampleContext segmentContext = new LightSampleContext(new Point3fi(origin), new Normal3f(0, 0, 0),
                                                                            new Normal3f(0, 0, 0), ctx.ray.time);
                Point3f pLight;
                Interaction yIntr = default;
                Float pdfArea = 1;
                if (areaLight != null)
                {
                    if (!areaLight.SampleArea(uLight, out yIntr, out pdfArea))
                    {
                        return zero;
                    }
                    pLight = yIntr.p();
                }
                else
                {
                    pstd.optional<LightLiSample> lsOrigin = light.SampleLi(segmentContext, uLight, lambda, true);
                    if (!lsOrigin)
                    {
                        return zero;
                    }
                    pLight = lsOrigin.value().pLight.p();
                }
                if (!EquiangularSampling.Sample(origin, direction, segmentLength, pLight, uDistance, out Float t, out Float pdfDistance))
                {
                    return zero;
                }
                Point3f p = origin + t * direction;

                SampledSpectrum T_seg = new SampledSpectrum(1.0f), rl_seg = new SampledSpectrum(1.0f), ru_seg = new SampledSpectrum(1.0f);
                RNG rng = new RNG(Hash(origin, t), Hash(direction));
                Float uTrack = rng.Uniform<Float>();
                SampledSpectrum T_maj = medium.volumeIntegrator.tracker.SampleT_maj(
                    new Ray(origin, direction, ctx.ray.time), medium, t, uTrack, ref rng, lambda,
                    (Point3f pp, MediumProperties mpp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                    {
                        SampledSpectrum sigma_n = ClampZero(sigma_maj - mpp.sigma_a - mpp.sigma_s);
                        MediumEvents.RatioTrackingStep(ref T_seg, ref rl_seg, ref ru_seg, T_maj_, sigma_maj, sigma_n);
                        return (bool)T_seg;
                    });
                if (!T_seg)
                {
                    return zero;
                }
                MediumEvents.FinishSegment(ref T_seg, ref rl_seg, ref ru_seg, T_maj);
                if (!VolPathIntegrator.MajorantCovers(medium, new Ray(origin, direction, ctx.ray.time), segmentLength, t, lambda))
                {
                    return zero;
                }

                MediumProperties mp = medium.SamplePoint(p, lambda);
                if (!mp.sigma_s || !(mp.sigma_s[0] > 0))
                {
                    return zero;
                }
                MediumInteraction intr = new MediumInteraction(p, -direction, ctx.ray.time, medium, mp.phase);
                Vertex x = Vertex.CreateMedium(intr, ctx.st.beta, pdfFwd, path[vIdx - 1]);
                x.sigmaS0 = mp.sigma_s[0];
                x.segU = ru_seg;
                x.segL = rl_seg;
                x.vtx = mp.sigma_s / mp.sigma_s[0];
                x.cumU = path[vIdx - 1].cumU * ru_seg;
                x.stack = ctx.stack;

                LightSampleContext lightContext = Integrator.LightContextAt(intr, false, default);
                SampledSpectrum radiance;
                Interaction target;
                if (areaLight != null)
                {
                    Vector3f d = yIntr.p() - p;
                    Float r2 = LengthSquared(d);
                    if (r2 == 0)
                    {
                        return zero;
                    }
                    Vector3f wi = d / MathF.Sqrt(r2);
                    Float cosY = AbsDot(yIntr.n, wi);
                    if (cosY == 0)
                    {
                        return zero;
                    }
                    SampledSpectrum Lemit = areaLight.L(yIntr.p(), yIntr.n, yIntr.uv, -wi, lambda, ctx.ray.time);
                    if (!Lemit)
                    {
                        return zero;
                    }
                    radiance = Lemit * (cosY / r2);
                    target = yIntr;
                }
                else
                {
                    pstd.optional<LightLiSample> ls = light.SampleLi(lightContext, uLight, lambda, true);
                    if (!ls || !ls.value().L || ls.value().pdf == 0)
                    {
                        return zero;
                    }
                    radiance = ls.value().L;
                    target = ls.value().pLight;
                    pdfArea = 1;
                }
                EndpointInteraction ei = new EndpointInteraction(light, target);
                Vertex yVertex = Vertex.CreateLight(ei, new SampledSpectrum(1.0f), 0);
                yVertex.pdfFwd = yVertex.PDFLightOrigin(ctx.integrator.infiniteLights, x, lightSampler);
                SampledSpectrum f_hat = x.f(yVertex, TransportMode.Radiance);
                if (!f_hat)
                {
                    return zero;
                }
                SampledSpectrum T_conn = ctx.integrator.TrSegments(intr, target, ctx.stack, lambda, out SampledSpectrum ru_conn,
                                                                    out SampledSpectrum rl_conn, out Medium endMedium);
                if (!T_conn)
                {
                    return zero;
                }

                // The weight: the ways to find the path, relative to the hero wavelength's connection. The scattering point is the
                // last vertex of the camera subpath (the sums of the vertices before it are those of the walk).
                path[vIdx] = x;
                state[vIdx].vtxProduct = state[vIdx - 1].vtxProduct * x.vtx;
                Vertex[] lightVertices = new Vertex[] { new Vertex() };
                if (!MISSum(ctx.integrator, ctx.camera, lightVertices, dummyLightState, path, state, yVertex, 1, vIdx + 1,
                            lightSampler, ru_conn, rl_conn, false, out SampledSpectrum sum))
                {
                    return zero;
                }
                SampledSpectrum r_u0 = state[vIdx - 1].vtxProduct * path[vIdx - 1].cumU;
                SampledSpectrum equiangular = r_u0 * rl_seg * rl_conn * (count * pdfDistance);
                Float pmfConnection = lightSampler.PMF(light);
                SampledSpectrum denominator = (equiangular * sampledLight.value().p + sum * (mp.sigma_s[0] * pmfConnection)) * pdfArea;
                Float average = denominator.Average();
                if (!(average > 0))
                {
                    return zero;
                }
                return ctx.st.beta * T_seg * mp.sigma_s * f_hat * T_conn * radiance / average;
            }

            public override bool BeginIteration(WalkContext ctx)
            {
                if (!ctx.st.beta)
                {
                    return false;
                }
                if (ctx.integrator.russianRoulette && bounces > 1 && ctx.st.beta.MaxComponentValue() < 1)
                {
                    Float q = std.max<Float>(0, 1 - ctx.st.beta.MaxComponentValue());
                    if (ctx.sampler.Get1D() < q)
                    {
                        return false;
                    }
                    ctx.st.SurviveRoulette(q);
                }
                vIdx = off + bounces;
                return true;
            }

            public override bool BeginCollision(WalkContext ctx, Medium medium, Point3f p, MediumProperties mp,
                                                SampledSpectrum sigma_maj, SampledSpectrum T_maj)
            {
                if (emissionEnds != null && ctx.mode == TransportMode.Radiance && (bool)mp.Le && (bool)ctx.st.beta)
                {
                    VolumeEmissionLight emissionLight = (medium as TextureMedium)?.EmissionLight();
                    if (emissionLight != null)
                    {
                        Float pdfCollision = sigma_maj[0] * T_maj[0];
                        SampledSpectrum w = T_maj / pdfCollision;
                        Vertex end = Vertex.CreateLight(emissionLight, new Interaction(p, ctx.ray.time, medium),
                                                        ctx.st.beta * w, 0);
                        end.emission = mp.sigma_a * mp.Le;
                        end.sigmaT0 = sigma_maj[0];
                        end.emissionMaj = sigma_maj / sigma_maj[0];
                        end.segU = ctx.st.segU * T_maj / T_maj[0];
                        end.segL = ctx.st.segL * T_maj / T_maj[0];
                        end.cumU = path[vIdx - 1].cumU * end.segU;
                        end.pdfFwd = path[vIdx - 1].ConvertDensity(pdfFwd, end) * end.sigmaT0;
                        end.pbeta = ctx.st.pa;
                        end.pbeta *= w;
                        end.stack = ctx.stack;
                        emissionEnds.Add(new MediumEmissionEnd(end, vIdx));
                    }
                }
                return true;
            }

            public override bool BeginScatter(WalkContext ctx)
            {
                return true;
            }

            public override bool OnScatterVertex(WalkContext ctx, Medium medium, Point3f p, MediumProperties mp,
                                                 out MediumInteraction intr, out BSDF phaseBsdf)
            {
                intr = new MediumInteraction(p, -ctx.ray.d, ctx.ray.time, medium, mp.phase);
                path[vIdx] = Vertex.CreateMedium(intr, ctx.st.beta, pdfFwd, path[vIdx - 1]);
                path[vIdx].sigmaS0 = mp.sigma_s[0];
                path[vIdx].stack = ctx.stack;
                StoreSegment(ctx, vIdx);
                path[vIdx].pbeta = ctx.st.pa;
                path[vIdx].plight = ctx.st.pl;
                phaseBsdf = path[vIdx].bsdf;
                return ++bounces < maxDepth;
            }

            public override void OnPhaseSampled(WalkContext ctx, MediumInteraction intr, Point3f p, BSDFSample ps)
            {
                pdfFwd = ps.pdf;
                ctx.ray = intr.SpawnRay(ps.wi);
                anyNonSpecularBounces = true;
                path[vIdx - 1].pdfRev = path[vIdx].ConvertDensity(ps.pdf, path[vIdx - 1]);
                FinishVertex(ctx, vIdx);
            }

            public override bool EndAfterMedium(WalkContext ctx, bool terminated)
            {
                return terminated;
            }

            public override void OnEscape(WalkContext ctx)
            {
                if (ctx.mode == TransportMode.Radiance)
                {
                    path[vIdx] = Vertex.CreateLight(new EndpointInteraction(new Ray(ctx.ray)), ctx.st.beta, pdfFwd);
                    StoreSegment(ctx, vIdx);
                    path[vIdx].pbeta = ctx.st.pa;
                    ++bounces;
                }
            }

            public override void OnSurfaceHit(WalkContext ctx, SurfaceInteraction isect)
            {
            }

            public override bool OnBSDF(WalkContext ctx, ShapeIntersection si, SurfaceInteraction isect, BSDF bsdf)
            {
                if (regularize && anyNonSpecularBounces)
                {
                    ++PathIntegrator.regularizedBSDFs;
                    bsdf.Regularize();
                }

                ++PathIntegrator.totalBSDFs;
                path[vIdx] = Vertex.CreateSurface(isect, bsdf, ctx.st.beta, pdfFwd, path[vIdx - 1]);
                path[vIdx].stack = ctx.stack;
                StoreSegment(ctx, vIdx);
                path[vIdx].pbeta = ctx.st.pa;
                path[vIdx].plight = ctx.st.pl;
                return ++bounces < maxDepth;
            }

            public override bool OnSurfaceScattered(WalkContext ctx, ShapeIntersection si, SurfaceInteraction isect, BSDF bsdf,
                                                    BSDFSample bs)
            {
                Vector3f wo = isect.wo;
                pdfFwd = bs.pdfIsProportional ? bsdf.PDF(wo, bs.wi, ctx.mode, BxDFReflTransFlags.All) : bs.pdf;
                anyNonSpecularBounces |= !bs.IsSpecular();
                Float pdfRev = bsdf.PDF(bs.wi, wo, !ctx.mode, BxDFReflTransFlags.All);
                if (bs.IsSpecular())
                {
                    path[vIdx].delta = true;
                    pdfRev = pdfFwd = 0;
                }
                path[vIdx - 1].pdfRev = path[vIdx].ConvertDensity(pdfRev, path[vIdx - 1]);
                FinishVertex(ctx, vIdx);
                return true;
            }
        }

        // The light state of the equiangular strategy's connection, which has no light subpath
        static readonly VertexMIS[] dummyLightState = new VertexMIS[] { VertexMIS.Root() };

        // ------------------------------------------------------------------------------------------
        // The weight of a connection

        static Light LightOfVertex(Vertex v)
        {
            return v.type == VertexType.Light ? v.ei.light : (v.type == VertexType.Surface ? v.si.areaLight : null);
        }

        static bool FirstPiece(Integrator integrator, Vertex z, Vertex x, out Point3f origin, out Vector3f direction,
                               out Float tMax, out Float tX)
        {
            origin = default;
            direction = default;
            tMax = 0;
            tX = 0;
            Ray ray = z.GetInteraction().SpawnRayTo(x.GetInteraction());
            Float length = Length(ray.d);
            if (length == 0)
            {
                return false;
            }
            pstd.optional<ShapeIntersection> si = integrator.Intersect(ray);
            if (si && si.value().tHit < 1 - ShadowEpsilon)
            {
                return false;
            }
            origin = ray.o;
            direction = ray.d / length;
            tMax = si ? si.value().tHit * length : Infinity;
            tX = Dot(x.p() - origin, direction);
            return tX > 0 && tX <= tMax;
        }

        public static Float MISWeight(Integrator integrator, Camera camera, Vertex[] lightVertices, VertexMIS[] lightState,
                                      Vertex[] cameraVertices, VertexMIS[] cameraState, Vertex sampled, int s, int t,
                                      LightSampler lightSampler, SampledSpectrum connRu, SampledSpectrum connRl)
        {
            if (!MISSum(integrator, camera, lightVertices, lightState, cameraVertices, cameraState, sampled, s, t, lightSampler,
                        connRu, connRl, true, out SampledSpectrum sum))
            {
                return 1;
            }
            Float average = sum.Average();
            return average > 0 ? 1 / average : 0;
        }

        // The sum, per wavelength, of the probabilities of the path under all the strategies, relative to the sample's strategy
        // (bdpt's BDPT.MISSum), from the sums at the end vertices of the subpaths; false if the weight is one
        public static bool MISSum(Integrator integrator, Camera camera, Vertex[] lightVertices, VertexMIS[] lightState,
                                  Vertex[] cameraVertices, VertexMIS[] cameraState, Vertex sampled, int s, int t,
                                  LightSampler lightSampler, SampledSpectrum connRu, SampledSpectrum connRl,
                                  bool withEquiangular, out SampledSpectrum sumOut)
        {
            sumOut = default;
            bool mediumEmissionPair = integrator.gatherMediumEmission &&
                ((s == 0 && t == 2 && BDPT.IsVolumeEmission(cameraVertices[1])) ||
                 (s == 1 && t == 1 && BDPT.IsVolumeEmission(lightVertices[0])));
            if (s + t == 2 && !mediumEmissionPair)
            {
                return false;
            }

            // The end vertices of the two subpaths and the vertices before them; the sampled vertex of an s = 1 or t = 1 strategy
            // takes the place of the vertex of the subpath
            Vertex pt = t == 1 ? sampled : cameraVertices[t - 1];
            Vertex qs = s == 0 ? null : (s == 1 && t > 1 ? sampled : lightVertices[s - 1]);
            Vertex ptMinus = t > 1 ? cameraVertices[t - 2] : null;
            Vertex qsMinus = s > 1 ? lightVertices[s - 2] : null;

            // The reverse densities of the end vertices and of the vertices before them, for the direction of the connection
            Float ptRev = s > 0 ? qs.PDF(integrator, qsMinus, pt) : pt.PDFLightOrigin(integrator.infiniteLights, ptMinus, lightSampler);
            // (the camera vertex of a subpath of two vertices is not a term of the sum)
            Float ptMinusRev = 0;
            if (t >= 3)
            {
                ptMinusRev = s > 0 ? pt.PDF(integrator, qs, ptMinus) : pt.PDFLight(integrator, ptMinus);
            }
            Float qsRev = 0, qsMinusRev = 0;
            if (qs != null)
            {
                qsRev = pt.PDF(integrator, ptMinus, qs);
            }
            if (qsMinus != null)
            {
                qsMinusRev = qs.PDF(integrator, pt, qsMinus);
            }

            Film film = camera.GetFilm();
            Float splatScale = (Float)film.FullResolution().x * (Float)film.FullResolution().y / (Float)film.PixelBounds().Area();

            SampledSpectrum zero = new SampledSpectrum(0.0f);
            SampledSpectrum one = new SampledSpectrum(1.0f);
            Float riOneLight = s == 1 ? 1 : 0;
            bool oneLightValid = s == 1;

            // The strategies that connect on the camera subpath: the vertex t - 1 (stored sums of the vertex before it)
            SampledSpectrum cameraSum = zero;
            if (t >= 2)
            {
                Float inverseForward = 1 / Remap0(pt.pdfFwd);
                SampledSpectrum dVCM = zero;
                if (!ptMinus.delta && integrator.StrategyAllowed(s + 1, t - 1))
                {
                    dVCM = ptMinus.cumU * pt.segL * inverseForward;
                    if (t - 1 == 1)
                    {
                        dVCM *= 1 / splatScale;
                    }
                    if (s == 0)
                    {
                        riOneLight = Remap0(ptRev) * inverseForward * (t - 1 == 1 ? 1 / splatScale : 1);
                        oneLightValid = true;
                    }
                }
                SampledSpectrum dVC = zero;
                if (t >= 3)
                {
                    dVC = pt.segU * (cameraState[t - 2].dVCM + cameraState[t - 2].dVC) * (Remap0(ptMinusRev) * inverseForward);
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
                    bool volumeEmission = BDPT.IsVolumeEmission(qs);
                    bool allowed = !(volumeEmission && !integrator.gatherMediumEmission) && !qs.IsDeltaLight() &&
                                   integrator.StrategyAllowed(0, t + 1);
                    if (allowed)
                    {
                        dVCM = volumeEmission ? qs.emissionMaj * (qs.sigmaT0 * inverseForward) : new SampledSpectrum(inverseForward);
                    }
                }
                else
                {
                    if (!qsMinus.delta && integrator.StrategyAllowed(s - 1, t + 1))
                    {
                        dVCM = qsMinus.cumU * qs.segL * inverseForward;
                    }
                    dVC = qs.segU * (lightState[s - 2].dVCM + lightState[s - 2].dVC) * (Remap0(qsMinusRev) * inverseForward);

                    // The strategy with one light vertex (the equiangular strategy's neighbour): the product of the ratios from 1
                    if (!(s == 2 ? false : lightVertices[1].delta) && !lightVertices[0].delta &&
                        integrator.StrategyAllowed(1, t + s - 1))
                    {
                        Float rho = Remap0(qsRev) / Remap0(qs.pdfFwd);
                        if (s >= 3)
                        {
                            rho *= Remap0(qsMinusRev) / Remap0(qsMinus.pdfFwd) * lightState[s - 2].rhoProductBefore;
                        }
                        if (t == 1)
                        {
                            rho /= splatScale;
                        }
                        riOneLight = rho;
                        oneLightValid = true;
                    }
                }
                lightSum = (dVCM + dVC) * Remap0(qsRev);
                if (t == 1)
                {
                    lightSum *= 1 / splatScale;
                }
            }

            // The sum: the sample's own strategy (1, with the spectral factors of the media), the camera side and the light side
            SampledSpectrum scatterVertices = (t >= 2 ? cameraState[t - 1].vtxProduct : one) * (s >= 2 ? lightState[s - 1].vtxProduct : one);
            SampledSpectrum cameraWalked = pt.cumU;
            SampledSpectrum lightWalked = qs != null ? qs.cumU : one;
            SampledSpectrum connectionAsConnection = s >= 1 ? connRl : one;
            SampledSpectrum connectionAsWalk = s >= 1 ? connRu : one;
            SampledSpectrum emissionEnd = s == 0 && BDPT.IsVolumeEmission(pt) ? pt.emissionMaj : one;
            SampledSpectrum sum = scatterVertices *
                (cameraWalked * lightWalked * connectionAsConnection * emissionEnd +
                 connectionAsWalk * (lightWalked * cameraSum + cameraWalked * lightSum));

            if (withEquiangular && integrator.bdptEquiangular && oneLightValid)
            {
                sum += EquiangularTerm(integrator, lightVertices, lightState, cameraVertices, cameraState, pt, qs, s, t, riOneLight,
                                       connRu, connRl, lightSampler);
            }
            sumOut = sum;
            return true;
        }

        // The strategy that finds a path y0, x1, ... with a scattering point x1 in a medium next to a point or area light y0 by
        // choosing the light point first and x1 on the segment that arrives at it from the camera side by the angle seen from the
        // light (see BDPT.EquiangularTerm, which this reproduces with the products read from the stored prefix products)
        static SampledSpectrum EquiangularTerm(Integrator integrator, Vertex[] lightVertices, VertexMIS[] lightState,
                                               Vertex[] cameraVertices, VertexMIS[] cameraState, Vertex pt, Vertex qs, int s, int t,
                                               Float riOneLight, SampledSpectrum connRu, SampledSpectrum connRl,
                                               LightSampler lightSampler)
        {
            SampledSpectrum zero = new SampledSpectrum(0.0f);
            SampledSpectrum value = new SampledSpectrum(riOneLight);
            Vertex y0, x1, z;
            if (s == 1)
            {
                if (t < 2)
                {
                    return zero;
                }
                y0 = qs;
                x1 = pt;
                z = cameraVertices[t - 2];
                value *= cameraState[t - 2].vtxProduct;
                value *= z.cumU * x1.segL * connRl;
            }
            else if (s == 0)
            {
                if (t < 3)
                {
                    return zero;
                }
                y0 = pt;
                x1 = cameraVertices[t - 2];
                z = cameraVertices[t - 3];
                value *= cameraState[t - 3].vtxProduct;
                value *= z.cumU * x1.segL * y0.segL;
            }
            else
            {
                y0 = lightVertices[0];
                x1 = lightVertices[1];
                if (t >= 2)
                {
                    value *= cameraState[t - 1].vtxProduct;
                }
                value *= lightState[s - 1].vtxProductFrom2;
                value *= pt.cumU * x1.segL;
                if (s == 2)
                {
                    z = pt;
                    value *= connRl;
                }
                else
                {
                    z = lightVertices[2];
                    value *= z.segL * lightState[s - 1].segUProductFrom3 * connRu;
                }
            }
            if (x1.type != VertexType.Medium || !(x1.sigmaS0 > 0) || !BDPT.EquiangularLight(LightOfVertex(y0)))
            {
                return zero;
            }
            if (!FirstPiece(integrator, z, x1, out Point3f origin, out Vector3f direction, out Float tMax, out Float tX))
            {
                return zero;
            }
            Float pdfDistance = EquiangularSampling.Pdf(origin, direction, tMax, y0.p(), tX);
            if (!(pdfDistance > 0))
            {
                return zero;
            }
            Light light = LightOfVertex(y0);
            Float pmfConnection = lightSampler.PMF(light);
            if (!(pmfConnection > 0))
            {
                return zero;
            }
            Float pmfEquiangular = integrator.bdptEquiangularLightSampler.PMF(light);
            return value * (integrator.EquiangularCount(s + t - 2) * pdfDistance * pmfEquiangular / (x1.sigmaS0 * pmfConnection));
        }

        // ------------------------------------------------------------------------------------------
        // Connections

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

        static SampledSpectrum SafeBeta(SampledSpectrum beta)
        {
            SampledSpectrum safe = beta;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                if (safe[i] == 0)
                {
                    safe[i] = 1;
                }
            }
            return safe;
        }

        static PAttenuation ConnectionMueller(Vertex v, Vector3f wi, bool fromCamera)
        {
            if (v.type == VertexType.Medium &&
                PolarisedPathIntegrator.PhaseAttenuation(v.mi.phase, v.mi.wo, wi, fromCamera, out PAttenuation M))
            {
                return M;
            }
            return PAttenuation.Lambert(new SampledSpectrum(1.0f));
        }

        // The path that a camera subpath makes with the emission that it gathered in a medium: the vertices of the subpath before
        // the collision, then the collision as a light vertex (s = 0). The end takes the place of the vertex of the subpath at its
        // index while the connection is evaluated.
        public static SampledSpectrum ConnectEmissionEnd(Integrator integrator, ref SampledWavelengths lambdaRef,
                                                         Vertex[] cameraVertices, VertexMIS[] cameraState, MediumEmissionEnd end,
                                                         LightSampler lightSampler, Camera camera, Sampler sampler,
                                                         PolarisedPathIntegrator.Sensor pol, Vector3f cameraDirection)
        {
            Vertex savedVertex = cameraVertices[end.index];
            VertexMIS savedState = cameraState[end.index];
            cameraVertices[end.index] = end.vertex;
            cameraState[end.index].vtxProduct = cameraState[end.index - 1].vtxProduct * end.vertex.vtx;
            try
            {
                pstd.optional<Point2f> pRaster = default;
                Float misWeight = 0;
                return ConnectBDPT(integrator, ref lambdaRef, new Vertex[0], dummyLightState, cameraVertices, cameraState, 0,
                                   end.index + 1, lightSampler, camera, sampler, ref pRaster, ref misWeight, pol, cameraDirection);
            }
            finally
            {
                cameraVertices[end.index] = savedVertex;
                cameraState[end.index] = savedState;
            }
        }

        // The contribution of a connection without its weight, and what the weight needs of it
        public struct Connection
        {
            public bool valid;
            public SampledSpectrum L;
            public Vertex sampled;
            public SampledSpectrum connRu, connRl;
            public Vector3f toCamera;
        }

        public static Connection EvaluateConnection(Integrator integrator, ref SampledWavelengths lambdaRef, Vertex[] lightVertices,
                                                    Vertex[] cameraVertices, int s, int t, LightSampler lightSampler, Camera camera,
                                                    Sampler sampler, ref pstd.optional<Point2f> pRaster)
        {
            SampledWavelengths lambda = lambdaRef;
            SampledSpectrum L = new SampledSpectrum(0.0f);
            Vector3f toCamera = default;
            SampledSpectrum connRu = new SampledSpectrum(1.0f), connRl = new SampledSpectrum(1.0f);
            Connection c = new Connection();
            // Ignore invalid connections related to infinite area lights
            if (t > 1 && s != 0 && cameraVertices[t - 1].type == VertexType.Light)
            {
                return c;
            }

            Vertex sampled = new Vertex();
            if (s == 0)
            {
                Vertex pt = cameraVertices[t - 1];
                if (pt.IsLight())
                {
                    L = pt.Le(integrator.infiniteLights, cameraVertices[t - 2], lambda) * pt.beta;
                }
                DCHECK(!L.HasNaNs());
            }
            else if (t == 1)
            {
                Vertex qs = lightVertices[s - 1];
                if (qs.IsConnectible())
                {
                    pstd.optional<CameraWiSample> cs = camera.SampleWi(qs.GetInteraction(), sampler.Get2D(), ref lambda);
                    if (cs)
                    {
                        pRaster = new pstd.optional<Point2f>(cs.value().pRaster);
                        sampled = Vertex.CreateCamera(camera, cs.value().pLens, cs.value().Wi / cs.value().pdf);
                        toCamera = Normalize(cs.value().pLens.p() - qs.p());
                        L = qs.beta * qs.f(sampled, TransportMode.Importance) * sampled.beta;
                        if (BDPT.IsVolumeEmission(qs))
                        {
                            L = qs.pdfFwd > 0 ? L / qs.pdfFwd : new SampledSpectrum(0.0f);
                        }
                        if (qs.IsOnSurface())
                        {
                            L *= AbsDot(cs.value().wi, qs.ns());
                        }
                        DCHECK(!L.HasNaNs());
                        if ((bool)L)
                        {
                            L *= integrator.TrSegments(cs.value().pRef, cs.value().pLens, qs.stack, lambda, out connRu, out connRl,
                                                       out Medium _);
                            Film film = camera.GetFilm();
                            L *= (Float)film.FullResolution().x * (Float)film.FullResolution().y /
                                 (Float)film.PixelBounds().Area();
                        }
                    }
                }
            }
            else if (s == 1)
            {
                Vertex pt = cameraVertices[t - 1];
                if (pt.IsConnectible())
                {
                    pstd.optional<SampledLight> sampledLight = lightSampler.Sample(sampler.Get1D());

                    if (sampledLight)
                    {
                        Light light = sampledLight.value().light;
                        Float p_l = sampledLight.value().p;

                        LightSampleContext ctx = Integrator.LightContextAt(pt.GetInteraction(), pt.IsOnSurface(),
                                                                           pt.IsOnSurface() ? pt.bsdf.Flags() : default);
                        VolumeEmissionLight volumeLight = light as VolumeEmissionLight;
                        pstd.optional<LightLiSample> lightWeight = volumeLight != null
                            ? volumeLight.SampleLiVolume(ctx, sampler.Get2D(), sampler.Get1D(), lambda)
                            : light.SampleLi(ctx, sampler.Get2D(), lambda);
                        if (lightWeight && (bool)lightWeight.value().L && lightWeight.value().pdf > 0)
                        {
                            EndpointInteraction ei = new EndpointInteraction(light, lightWeight.value().pLight);
                            sampled = Vertex.CreateLight(ei, lightWeight.value().L / (lightWeight.value().pdf * p_l), 0);
                            sampled.pdfFwd = sampled.PDFLightOrigin(integrator.infiniteLights, pt, lightSampler);
                            if (volumeLight != null)
                            {
                                SampledSpectrum majorant = volumeLight.Medium().MajorantAt(lightWeight.value().pLight.p(), lambda, ctx.time);
                                sampled.sigmaT0 = majorant[0];
                                sampled.emissionMaj = majorant / majorant[0];
                            }
                            L = pt.beta * pt.f(sampled, TransportMode.Radiance) * sampled.beta;
                            if (pt.IsOnSurface())
                            {
                                L *= AbsDot(lightWeight.value().wi, pt.ns());
                            }
                            if ((bool)L)
                            {
                                L *= integrator.TrSegments(pt.GetInteraction(), lightWeight.value().pLight, pt.stack, lambda,
                                                           out connRu, out connRl, out Medium endMedium);
                                if (volumeLight != null && endMedium != volumeLight.Medium())
                                {
                                    L = new SampledSpectrum(0.0f);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                Vertex qs = lightVertices[s - 1], pt = cameraVertices[t - 1];
                if (qs.IsConnectible() && pt.IsConnectible())
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

            ++PathIntegrator.totalPaths;
            if (!L)
            {
                ++PathIntegrator.zeroRadiancePaths;
            }
            if (PathIntegrator.pathLength == null)
            {
                PathIntegrator.pathLength = new StatIntDistribution();
            }
            PathIntegrator.pathLength.Add(s + t - 2);
            lambdaRef = lambda;
            c.valid = true;
            c.L = L;
            c.sampled = sampled;
            c.connRu = connRu;
            c.connRl = connRl;
            c.toCamera = toCamera;
            return c;
        }

        public static SampledSpectrum ConnectBDPT(Integrator integrator, ref SampledWavelengths lambdaRef, Vertex[] lightVertices,
                                                  VertexMIS[] lightState, Vertex[] cameraVertices, VertexMIS[] cameraState,
                                                  int s, int t, LightSampler lightSampler, Camera camera, Sampler sampler,
                                                  ref pstd.optional<Point2f> pRaster, ref Float misWeightOut,
                                                  PolarisedPathIntegrator.Sensor pol = null, Vector3f cameraDirection = default)
        {
            Connection c = EvaluateConnection(integrator, ref lambdaRef, lightVertices, cameraVertices, s, t, lightSampler, camera,
                                              sampler, ref pRaster);
            if (!c.valid)
            {
                return new SampledSpectrum(0.0f);
            }
            SampledSpectrum L = c.L;
            Vertex sampled = c.sampled;
            Vector3f toCamera = c.toCamera;
            Float misWeight = (bool)L ? MISWeight(integrator, camera, lightVertices, lightState, cameraVertices, cameraState,
                                                  sampled, s, t, lightSampler, c.connRu, c.connRl)
                                      : 0.0f;
            DCHECK(!IsNaN(misWeight));
            L *= misWeight;
            misWeightOut = misWeight;

            if (pol != null && (bool)L)
            {
                PLight lightSide = new PLight(new SampledSpectrum(1.0f));
                PAttenuation cameraSide = PAttenuation.Identity();
                if (s > 0)
                {
                    Vertex qs = s == 1 && t > 1 ? sampled : lightVertices[s - 1];
                    lightSide = qs.plight;
                    lightSide /= SafeBeta(qs.beta);
                    if (s > 1 && t > 0)
                    {
                        Vector3f toVertex = t == 1 ? toCamera : Normalize(cameraVertices[t - 1].p() - qs.p());
                        lightSide = ConnectionMueller(qs, toVertex, false) * lightSide;
                    }
                }
                if (t > 1)
                {
                    Vertex pt = cameraVertices[t - 1];
                    cameraSide = pt.pbeta;
                    cameraSide /= SafeBeta(pt.beta);
                    Vector3f toVertex = s == 0 ? default : Normalize(s == 1 ? sampled.p() - pt.p() : lightVertices[s - 1].p() - pt.p());
                    if (s > 0)
                    {
                        lightSide = ConnectionMueller(pt, toVertex, true) * lightSide;
                    }
                }

                PLight Lp = cameraSide * lightSide;
                Lp *= L;
                Ray cameraRay = t == 1
                    ? new Ray(sampled.p(), -toCamera, sampled.time())
                    : new Ray(cameraVertices[0].p(), Normalize(cameraVertices[1].p() - cameraVertices[0].p()), cameraVertices[0].time());
                return PolarisedPathIntegrator.MeasureCameraRay(camera, cameraRay, lambdaRef[0], Lp, pol.analyser, pol.analyserAngle,
                                                                pol.measurement, t == 1 ? toCamera : cameraDirection, pol.cameraUp);
            }

            return L;
        }
    }

    // BDPTIncrementalIntegrator Definition
    public sealed unsafe class BDPTIncrementalIntegrator : RayIntegrator
    {
        public BDPTIncrementalIntegrator(Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights, int maxDepth,
                                         bool visualizeStrategies, bool visualizeWeights, bool regularize = false,
                                         PolarisedPathIntegrator.Sensor sensor = null, bool russianRoulette = false,
                                         bool gatherMediumEmission = true, bool unidirectionalStrategies = false,
                                         bool equiangular = false, int equiangularSamples = 1,
                                         int equiangularMultipleDepth = int.MaxValue)
            : base(camera, sampler, aggregate, BDPTIntegrator.EmissionLightsForBDPT(lights))
        {
            this.bdptEquiangular = equiangular && sensor == null;
            if (this.bdptEquiangular)
            {
                List<Light> supported = new List<Light>();
                foreach (Light light in TextureMedium.AddEmissionLights(lights))
                {
                    if (BDPT.EquiangularLight(light))
                    {
                        supported.Add(light);
                    }
                }
                if (supported.Count == 0)
                {
                    this.bdptEquiangular = false;
                }
                else
                {
                    this.bdptEquiangularLightSampler = new PowerLightSampler(supported.ToArray(), new Allocator());
                }
            }
            this.bdptMaxDepth = maxDepth;
            this.bdptEquiangularSamples = Math.Max(1, equiangularSamples);
            this.bdptEquiangularMultipleDepth = equiangularMultipleDepth;
            this.gatherMediumEmission = gatherMediumEmission;
            this.unidirectionalStrategies = unidirectionalStrategies;
            this.sensor = sensor;
            this.russianRoulette = russianRoulette;
            if (sensor != null)
            {
                sensor.cameraUp = camera.GetCameraTransform().RenderFromCamera(new Vector3f(0, 1, 0), 0);
            }
            this.maxDepth = maxDepth;
            this.regularize = regularize;
            this.lightSampler = new PowerLightSampler(TextureMedium.AddEmissionLights(lights).ToArray(), new Allocator());
            this.visualizeStrategies = visualizeStrategies;
            this.visualizeWeights = visualizeWeights;
            Film film = camera.GetFilm();
            this.splatScale = (Float)film.FullResolution().x * (Float)film.FullResolution().y / (Float)film.PixelBounds().Area();
        }

        public override void Render()
        {
            if (visualizeStrategies || visualizeWeights)
            {
                int bufferCount = (1 + maxDepth) * (6 + maxDepth) / 2;
                weightFilms = new Film[bufferCount];
                for (int depth = 0; depth <= maxDepth; ++depth)
                {
                    for (int s = 0; s <= depth + 2; ++s)
                    {
                        int t = depth + 2 - s;
                        if (t == 0 || (s == 1 && t == 1))
                        {
                            continue;
                        }

                        string filename = StringPrintf("bdptinc_d%02d_s%02d_t%02d.exr", depth, s, t);

                        FilmBaseParameters p = new FilmBaseParameters(
                            camera.GetFilm().FullResolution(),
                            new Bounds2i(new Point2i(0, 0), camera.GetFilm().FullResolution()),
                            new BoxFilter(),
                            camera.GetFilm().Diagonal() * 1000, PixelSensor.CreateDefault(), filename);
                        weightFilms[BDPT.BufferIndex(s, t)] = new RGBFilm(p, RGBColorSpace.sRGB);
                    }
                }
            }

            base.Render();

            if (visualizeStrategies || visualizeWeights)
            {
                Float invSampleCount = 1.0f / samplerPrototype.SamplesPerPixel();
                for (int i = 0; i < weightFilms.Length; ++i)
                {
                    ImageMetadata metadata = new ImageMetadata();
                    if (weightFilms[i] != null)
                    {
                        weightFilms[i].WriteImage(metadata, invSampleCount);
                    }
                }
                weightFilms = null;
            }
        }

        public LightSampler BDPTLightSampler { get { return lightSampler; } }

        // The vertices and sums of the subpaths of a thread are kept between samples (every entry is written before it is read)
        [ThreadStatic] static VertexMIS[] cameraStateBuffer, lightStateBuffer;

        static VertexMIS[] StateBuffer(ref VertexMIS[] buffer, int size)
        {
            if (buffer == null || buffer.Length < size)
            {
                buffer = new VertexMIS[size];
            }
            return buffer;
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                           ScratchBuffer scratchBuffer, VisibleSurface visibleSurface)
        {
            Vertex[] cameraVertices = new Vertex[maxDepth + 2];
            for (int i = 0; i < cameraVertices.Length; ++i)
            {
                cameraVertices[i] = new Vertex();
            }
            VertexMIS[] cameraState = StateBuffer(ref cameraStateBuffer, maxDepth + 2);
            List<MediumEmissionEnd> emissionEnds = gatherMediumEmission ? new List<MediumEmissionEnd>() : null;
            BDPTEquiangularSink equiangularSink = bdptEquiangular ? new BDPTEquiangularSink(maxDepth) : null;
            int nCamera = BDPTIncremental.GenerateCameraSubpath(this, ray, ref lambda, sampler, scratchBuffer, maxDepth + 2, camera,
                                                                cameraVertices, cameraState, regularize, splatScale, sensor != null,
                                                                emissionEnds, equiangularSink);
            Vertex[] lightVertices = new Vertex[maxDepth + 1];
            for (int i = 0; i < lightVertices.Length; ++i)
            {
                lightVertices[i] = new Vertex();
            }
            VertexMIS[] lightState = StateBuffer(ref lightStateBuffer, maxDepth + 1);
            int nLight = BDPTIncremental.GenerateLightSubpath(this, ref lambda, sampler, camera, scratchBuffer,
                                                              unidirectionalStrategies ? 1 : maxDepth + 1,
                                                              cameraVertices[0].time(), lightSampler, lightVertices, lightState,
                                                              regularize, splatScale, sensor != null);

            SampledSpectrum L = new SampledSpectrum(0.0f);
            for (int t = 1; t <= nCamera; ++t)
            {
                for (int s = 0; s <= nLight; ++s)
                {
                    int depth = t + s - 2;
                    if ((s == 1 && t == 1 && !BDPT.IsVolumeEmission(lightVertices[0])) || depth < 0 || depth > maxDepth)
                    {
                        continue;
                    }
                    if (!StrategyAllowed(s, t))
                    {
                        continue;
                    }
                    pstd.optional<Point2f> pFilmNew = default;
                    Float misWeight = 0.0f;
                    SampledSpectrum Lpath = BDPTIncremental.ConnectBDPT(this, ref lambda, lightVertices, lightState, cameraVertices,
                                                                        cameraState, s, t, lightSampler, camera, sampler,
                                                                        ref pFilmNew, ref misWeight, sensor, -ray.d);
                    if ((bool)Lpath && (visualizeStrategies || visualizeWeights))
                    {
                        SampledSpectrum value = default;
                        if (visualizeStrategies)
                        {
                            value = misWeight == 0 ? new SampledSpectrum(0.0f) : Lpath / misWeight;
                        }
                        if (visualizeWeights)
                        {
                            value = Lpath;
                        }
                        if (pFilmNew)
                        {
                            weightFilms[BDPT.BufferIndex(s, t)].AddSplat(pFilmNew.value(), value, lambda);
                        }
                        else
                        {
                            pstd.optional<CameraWiSample> cs = camera.SampleWi(
                                new Interaction(ray.o + ray.d * 100.0f, (Float)0, (Medium)null), new Point2f(0.5f, 0.5f),
                                ref lambda);
                            if (cs)
                            {
                                weightFilms[BDPT.BufferIndex(s, t)].AddSplat(cs.value().pRaster, value, lambda);
                            }
                        }
                    }
                    if (t != 1)
                    {
                        L += Lpath;
                    }
                    else if ((bool)Lpath)
                    {
                        CHECK(pFilmNew.has_value());
                        AddCameraSplat(camera, pFilmNew.value(), Lpath, lambda, sampler);
                    }
                }
            }

            if (equiangularSink != null)
            {
                L += equiangularSink.L;
            }

            if (emissionEnds != null)
            {
                foreach (MediumEmissionEnd end in emissionEnds)
                {
                    int depth = end.index - 1;
                    if (end.index > nCamera || depth < 0 || depth > maxDepth)
                    {
                        continue;
                    }
                    L += BDPTIncremental.ConnectEmissionEnd(this, ref lambda, cameraVertices, cameraState, end, lightSampler, camera,
                                                            sampler, sensor, -ray.d);
                }
            }

            return L;
        }

        protected override void NoCameraRay(CameraSample cameraSample, ref SampledWavelengths lambda, Sampler sampler,
                                            ScratchBuffer scratchBuffer)
        {
            Vertex[] cameraVertices = new Vertex[maxDepth + 2];
            for (int i = 0; i < cameraVertices.Length; ++i)
            {
                cameraVertices[i] = new Vertex();
            }
            VertexMIS[] cameraState = StateBuffer(ref cameraStateBuffer, maxDepth + 2);
            Vertex[] lightVertices = new Vertex[maxDepth + 1];
            for (int i = 0; i < lightVertices.Length; ++i)
            {
                lightVertices[i] = new Vertex();
            }
            VertexMIS[] lightState = StateBuffer(ref lightStateBuffer, maxDepth + 1);
            int nLight = BDPTIncremental.GenerateLightSubpath(this, ref lambda, sampler, camera, scratchBuffer,
                                                              unidirectionalStrategies ? 1 : maxDepth + 1,
                                                              camera.SampleTime(cameraSample.time), lightSampler, lightVertices,
                                                              lightState, regularize, splatScale, sensor != null);
            for (int s = 0; s <= nLight; ++s)
            {
                int depth = s - 1;
                if ((s == 1 && !BDPT.IsVolumeEmission(lightVertices[0])) || depth < 0 || depth > maxDepth)
                {
                    continue;
                }
                if (!StrategyAllowed(s, 1))
                {
                    continue;
                }
                pstd.optional<Point2f> pFilmNew = default;
                Float misWeight = 0.0f;
                SampledSpectrum Lpath = BDPTIncremental.ConnectBDPT(this, ref lambda, lightVertices, lightState, cameraVertices,
                                                                    cameraState, s, 1, lightSampler, camera, sampler, ref pFilmNew,
                                                                    ref misWeight, sensor, default);
                if ((bool)Lpath)
                {
                    CHECK(pFilmNew.has_value());
                    AddCameraSplat(camera, pFilmNew.value(), Lpath, lambda, sampler);
                }
            }
        }

        public override string ToString()
        {
            return StringPrintf("[ BDPTIncrementalIntegrator maxDepth: %d visualizeStrategies: %s " +
                                "visualizeWeights: %s regularize: %s lightSampler: %s ]",
                                maxDepth, visualizeStrategies, visualizeWeights, regularize, lightSampler);
        }

        public static BDPTIncrementalIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                                       Primitive aggregate, List<Light> lights, FileLoc loc)
        {
            MediumTracking.RequireNoPerEntryVolumes("bdptinc");
            if (!(camera is PerspectiveCamera) && !(camera is RealisticCamera))
            {
                ErrorExit("Only the \"perspective\" and \"realistic\" cameras are currently supported with the " +
                          "\"bdptinc\" integrator.");
            }
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            bool visualizeStrategies = parameters.GetOneBool("visualizestrategies", false);
            bool visualizeWeights = parameters.GetOneBool("visualizeweights", false);

            if ((visualizeStrategies || visualizeWeights) && maxDepth > 5)
            {
                Warning(loc, "visualizestrategies/visualizeweights was enabled, limiting maxdepth to 5");
                maxDepth = 5;
            }

            bool regularize = parameters.GetOneBool("regularize", false);
            bool russianRoulette = parameters.GetOneBool("russianroulette", false);
            bool gatherMediumEmission = parameters.GetOneBool("gathermediumemission", true);
            bool equiangular = parameters.GetOneBool("equiangular", false);
            if (equiangular && parameters.GetOneBool("polarised", false))
            {
                Warning(loc, "\"equiangular\" is not used with \"polarised\" bdptinc.");
            }
            string strategies = parameters.GetOneString("strategies", "all");
            if (strategies != "all" && strategies != "unidirectional")
            {
                ErrorExit(loc, StringPrintf("\"%s\": unknown \"strategies\"; expected \"all\" or \"unidirectional\".", strategies));
            }
            PolarisedPathIntegrator.Sensor sensor = null;
            PolarisedPathIntegrator.ParseSensor(parameters, loc, out bool analyser, out Float analyserAngle,
                                                out PolarisedPathIntegrator.Measurement measurement);
            if (parameters.GetOneBool("polarised", false))
            {
                sensor = new PolarisedPathIntegrator.Sensor();
                sensor.analyser = analyser;
                sensor.analyserAngle = analyserAngle;
                sensor.measurement = measurement;
            }

            return new BDPTIncrementalIntegrator(camera, sampler, aggregate, lights, maxDepth, visualizeStrategies,
                                                 visualizeWeights, regularize, sensor, russianRoulette, gatherMediumEmission,
                                                 strategies == "unidirectional", equiangular,
                                                 parameters.GetOneInt("equiangularsamples", 1),
                                                 parameters.GetOneInt("equiangulardepth", int.MaxValue));
        }

        int maxDepth;
        bool regularize;
        LightSampler lightSampler;
        bool visualizeStrategies, visualizeWeights;
        Film[] weightFilms;
        PolarisedPathIntegrator.Sensor sensor;
        Float splatScale;
    }
}  // namespace pbrt
