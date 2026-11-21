// (not in pbrt-v4) cpu/pathspace.cs
//
// Markov chains in path space, with media (see docs/path-space-mcmc.md). The state of a chain is a path: its vertices from the
// light (index 0) to the camera (index k). Every vertex keeps the local terms of its scattering for the directions it has in
// the path (Vertex.connect, step, connectI, stepI), and the null collisions of the segment toward the camera side, as their
// sufficient statistics (Vertex.segU, segL; vtx for the scattering coefficient of a vertex in a medium).
//
// PathSpaceMLTIntegrator is two integrators:
//     "volbidirmlt"   the bidirectional mutation of Veach and Guibas ("Metropolis Light Transport", 1997, section 5.2): the
//                     vertices strictly between two of the path (a, b) are deleted, and n new ones are made, l of them by a walk
//                     from the light side and n - l by a walk from the camera side, joined by a connection;
//     "volpathmlt"    the same with the strategies of a path tracer only: everything on the light side of a vertex is deleted
//                     (a = -1), and the new vertices are a walk from the camera side that ends on a light (l = 0) or is
//                     connected to a point sampled on a light (l = 1).
// Both add the lens perturbation (section 5.3.1). The acceptance is that of Metropolis and Hastings with the density of the
// mixture of the splits l:
//     a = min(1, [c(y) / Q(y | x)] / [c(x) / Q(x | y)]),   Q(y | x) = P(a, b | x) P(n | ...) sum_l P(l | n) q_l(y),
// where q_l is the density of the new vertices under the split l and c is the luminance of the contribution. For the hero
// wavelength a segment that a walk samples has value / density = 1, and one that a connection samples 1 / segL, with the null
// collisions that are stored: no estimate of a transmittance enters the ratio.
//
// The local terms of a vertex, with wCam the direction to its camera-side neighbour and wLight to its light-side one:
//     connect  = f(wCam, wLight, Radiance) |ns . wLight|      (a phase function: its value; a specular lobe: the factor of its delta)
//     step     = connect / pdf(wCam -> wLight, Radiance)       the light-side direction sampled from the camera side
//     connectI = connect |ng . wCam| / |ng . wLight|            (in a medium: connect)
//     stepI    = connectI / pdf(wLight -> wCam, Importance)     the camera-side direction sampled from the light side
//     light:   emitted = Le(wCam), connectI = Le |n . wCam|, stepI = connectI / pdfDir, pdfLight = P(light) pdfArea
//     camera:  step = the weight of the camera ray, connect = We cos (times the squared distance, by which a connection divides)
//
// Not yet: lights other than diffuse area lights; emissive media; the polarised option; BxDFs whose value is a stochastic
// estimate (the walks of the layered and microsurface materials) are not handled exactly; the closed forms of the
// transmittance (a connection through a homogeneous medium is a ratio tracking estimate).

using System;
using System.Collections.Generic;
using PLight = pbrt.PolarisedLightSystem.Light;
using PAttenuation = pbrt.PolarisedLightSystem.Attenuation;
using System.Threading;

namespace pbrt
{
    using static pbrt;

    // PathSpacePath Definition
    public sealed unsafe class PathSpacePath
    {
        // The vertices, from the light (0) to the camera (k)
        public Vertex[] v;
        public int k;
        public SampledWavelengths lambda;
        public Point2f pRaster;
        // The rest of the camera sample, and the time of the path
        public Point2f pLens;
        public Float timeSample;
        public Float time;
        // What the sensor measures of the path (its contribution without any density; with the polarised option, of its
        // polarised light), and the luminance of that over the luminance of the scalar contribution
        public SampledSpectrum measured;
        public double polarisation = 1;
    }

    // PathSpace Definition
    public static unsafe class PathSpace
    {
        // The vertex that a ray reaches: a scattering in a medium or a surface with a BSDF, with the stack of media on arrival
        // (Vertex.stack) and the null collisions of the segment. False if the walk ends first (an absorption, or the ray leaves
        // the scene).
        public static bool TraceSegment(Integrator integrator, Camera camera, RayDifferential ray, BoundaryStack stack,
                                        ref SampledWavelengths lambda, Sampler sampler, ScratchBuffer scratchBuffer, out Vertex next,
                                        bool emissionEnds = false)
        {
            next = null;
            SampledSpectrum one = new SampledSpectrum(1.0f);
            PathState st = new PathState(one, default, default, true, false, false);
            st.trackSegment = true;
            SampledWavelengths lam = lambda;
            while (true)
            {
                pstd.optional<ShapeIntersection> si = integrator.Intersect(new Ray(ray));
                Medium medium = MediumTracking.Current(stack);
                if (medium != null)
                {
                    bool scattered = false, terminated = false;
                    Vertex mediumVertex = null;
                    Float tMax = si ? si.value().tHit : Infinity;
                    RNG rng = new RNG((ulong)(sampler.Get1D() * 4294967296.0), (ulong)(sampler.Get1D() * 4294967296.0));
                    Float u = sampler.Get1D();
                    Ray segment = new Ray(ray);
                    SampledSpectrum T_maj = medium.volumeIntegrator.tracker.SampleT_maj(
                        segment, medium, tMax, u, ref rng, lam,
                        (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                        {
                            // Randomly sample the medium event
                            Float um = sampler.Get1D();
                            MediumEventType evt = MediumEvents.SampleEvent(mp, sigma_maj, um);
                            if (evt == MediumEventType.Absorb)
                            {
                                if (emissionEnds && (bool)mp.Le && mp.sigma_a[0] > 0)
                                {
                                    // The walk ends where the medium emits: the vertex of the emission (a light vertex with no
                                    // light), with the absorption coefficient where a scattering has the scattering coefficient
                                    st.RealScatter(T_maj_, mp.sigma_a);
                                    mediumVertex = Vertex.CreateLight(new EndpointInteraction((Light)null, new Ray(p, -Normalize(segment.d), segment.time)),
                                                                      new SampledSpectrum(1.0f), 0);
                                    mediumVertex.emission = mp.Le;
                                    scattered = true;
                                    return false;
                                }
                                terminated = true;
                                return false;
                            }
                            else if (evt == MediumEventType.Scatter)
                            {
                                st.RealScatter(T_maj_, mp.sigma_s);
                                MediumInteraction intr = new MediumInteraction(p, -Normalize(segment.d), segment.time, medium, mp.phase);
                                mediumVertex = new Vertex(intr, new SampledSpectrum(1.0f));
                                scattered = true;
                                return false;
                            }
                            else
                            {
                                // Null collision
                                SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                                return st.NullCollision(T_maj_, sigma_maj, sigma_n);
                            }
                        });
                    if (terminated || !st.beta)
                    {
                        return false;
                    }
                    if (scattered)
                    {
                        mediumVertex.stack = stack;
                        mediumVertex.segU = st.segU;
                        mediumVertex.segL = st.segL;
                        mediumVertex.vtx = st.vtx;
                        next = mediumVertex;
                        lambda = lam;
                        return true;
                    }
                    st.EndSegment(T_maj);
                }

                // Handle escaped rays after no medium scattering event
                if (!si)
                {
                    return false;
                }

                // Get BSDF and skip over medium boundaries
                SurfaceInteraction isect = si.value().intr;
                BSDF bsdf = isect.GetBSDF(ray, ref lam, camera, scratchBuffer, sampler);
                if (!bsdf)
                {
                    isect.SkipIntersection(ref ray, si.value().tHit);
                    MediumTracking.CrossSurface(ref stack, isect, ray.d);
                    continue;
                }
                Vertex surfaceVertex = new Vertex(isect, bsdf, one);
                surfaceVertex.stack = stack;
                surfaceVertex.segU = st.segU;
                surfaceVertex.segL = st.segL;
                surfaceVertex.vtx = st.vtx;
                next = surfaceVertex;
                lambda = lam;
                return true;
            }
        }

        // The segment between two points as a connection samples it: ratio tracking, every collision of the majorant a null
        // collision. T is the value of the segment over the density of the connection for the hero wavelength, and walkOverConnection
        // the density of the segment as a walk samples it over that of the connection (hero wavelength). False if the segment is
        // blocked or its value is zero. stack is the stack of media of the segment where it leaves origin, and arrival that on
        // arrival at target.
        public static bool ConnectSegment(Integrator integrator, Interaction origin, Interaction target, BoundaryStack stack,
                                          SampledWavelengths lambda, Sampler sampler, out SampledSpectrum T, out Float walkOverConnection,
                                          out BoundaryStack arrival)
        {
            arrival = stack;
            T = new SampledSpectrum(1.0f);
            walkOverConnection = 1;
            SampledSpectrum T_ray = new SampledSpectrum(1.0f), r_l = new SampledSpectrum(1.0f), r_u = new SampledSpectrum(1.0f);
            RNG rng = new RNG((ulong)(sampler.Get1D() * 4294967296.0), (ulong)(sampler.Get1D() * 4294967296.0));
            Ray ray = origin.SpawnRayTo(target);
            while (ray.d != new Vector3f(0, 0, 0))
            {
                // Trace ray through media to estimate transmittance
                pstd.optional<ShapeIntersection> si = integrator.Intersect(ray, 1 - ShadowEpsilon);
                // Handle opaque surface along ray's path
                if (si && ThinCoatMaterial.Blocks(si.value().intr, ray.d))
                {
                    return false;
                }

                // Update transmittance for current ray segment
                Medium medium = MediumTracking.Current(stack);
                if (medium != null)
                {
                    Float tMax = si ? si.value().tHit : (1 - ShadowEpsilon);
                    Float uT = rng.Uniform<Float>();
                    SampledSpectrum T_maj = medium.volumeIntegrator.tracker.ForTransmittance().SampleT_maj(
                        ray, medium, tMax, uT, ref rng, lambda,
                        (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj_) =>
                        {
                            // Update _T_ray_ and PDFs using ratio-tracking estimator
                            SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                            MediumEvents.RatioTrackingStep(ref T_ray, ref r_l, ref r_u, T_maj_, sigma_maj, sigma_n);
                            return (bool)T_ray;
                        });
                    // Update transmittance estimate for final segment
                    MediumEvents.FinishSegment(ref T_ray, ref r_l, ref r_u, T_maj);
                }

                // Generate next ray segment or return final transmittance
                if (!T_ray)
                {
                    return false;
                }
                if (!si)
                {
                    break;
                }
                ray = si.value().intr.SpawnRayTo(target);
                MediumTracking.CrossSurface(ref stack, si.value().intr, ray.d);
            }
            if (!(T_ray[0] > 0) || !(r_u[0] > 0))
            {
                return false;
            }
            T = T_ray;
            walkOverConnection = r_u[0];
            arrival = stack;
            return true;
        }

        // The stack of media on the light side of a vertex from the one on its camera side (Vertex.stack), for its directions
        // wCam and wLight. (CrossSurface() takes the side of arrival from the interaction's wo, which is the side of the walk
        // that made the vertex: here the side is the camera's, whichever walk made it.)
        public static BoundaryStack LightSideStack(Vertex vertex)
        {
            BoundaryStack stack = vertex.stack;
            if (vertex.type == VertexType.Surface)
            {
                Vector3f wo = vertex.si.wo;
                vertex.si.wo = vertex.wCam;
                MediumTracking.CrossSurface(ref stack, vertex.si, vertex.wLight);
                vertex.si.wo = wo;
            }
            return stack;
        }

        // The stack of media on the camera side of a vertex from the one on its light side (Vertex.stackLight)
        public static BoundaryStack CameraSideStack(Vertex vertex)
        {
            BoundaryStack stack = vertex.stackLight;
            if (vertex.type == VertexType.Surface)
            {
                Vector3f wo = vertex.si.wo;
                vertex.si.wo = vertex.wLight;
                MediumTracking.CrossSurface(ref stack, vertex.si, vertex.wCam);
                vertex.si.wo = wo;
            }
            return stack;
        }

        // The cosine of a direction with the geometric normal of a vertex (1 in a medium)
        public static Float GeometricCosine(Vertex vertex, Vector3f w)
        {
            if (vertex.type == VertexType.Medium || (vertex.type == VertexType.Light && !OnAreaLight(vertex)))
            {
                return 1;
            }
            return AbsDot(vertex.ng(), w);
        }

        // Whether a light vertex is a point of the surface of an area light (and not the point of a light without an area: a
        // point, spot, goniometric or projection light, which has no cosine and which no walk arrives at)
        public static bool OnAreaLight(Vertex vertex)
        {
            return vertex.ei.light is DiffuseAreaLight;
        }

        // Whether a light vertex is a point of a medium that emits: only a walk from the camera ends there (the port's bdpt
        // gathers the emission of the media in the same way), and it emits the same in every direction
        public static bool InEmittingMedium(Vertex vertex)
        {
            return vertex.type == VertexType.Light && vertex.ei.light == null;
        }

        // The local terms of a vertex at a surface or in a medium, for its directions wCam and wLight, when neither was sampled
        // from a specular lobe. False if the vertex does not scatter between them.
        public static bool SetTerms(Vertex vertex)
        {
            vertex.delta = false;
            SampledSpectrum value;
            Float pdfR, pdfI;
            if (vertex.type == VertexType.Surface)
            {
                value = vertex.bsdf.f(vertex.wCam, vertex.wLight, TransportMode.Radiance) * AbsDot(vertex.wLight, vertex.si.shading.n);
                pdfR = vertex.bsdf.PDF(vertex.wCam, vertex.wLight, TransportMode.Radiance, BxDFReflTransFlags.All);
                pdfI = vertex.bsdf.PDF(vertex.wLight, vertex.wCam, TransportMode.Importance, BxDFReflTransFlags.All);
                Float cosLight = AbsDot(vertex.si.n, vertex.wLight);
                vertex.connectI = cosLight > 0 ? value * (AbsDot(vertex.si.n, vertex.wCam) / cosLight) : new SampledSpectrum(0.0f);
            }
            else
            {
                value = vertex.bsdf.f(vertex.wCam, vertex.wLight);
                pdfR = vertex.bsdf.PDF(vertex.wCam, vertex.wLight);
                pdfI = vertex.bsdf.PDF(vertex.wLight, vertex.wCam);
                vertex.connectI = value;
            }
            vertex.connect = value;
            vertex.step = pdfR > 0 ? value / pdfR : new SampledSpectrum(0.0f);
            vertex.stepI = pdfI > 0 ? vertex.connectI / pdfI : new SampledSpectrum(0.0f);
            return (bool)value;
        }

        // The specular lobe of a surface vertex that scatters from wo to wi in a mode: its sample, found by sampling the BSDF
        // until that lobe comes up, so that its density is the one a walk has for it. False if the BSDF has no such lobe.
        public static bool SpecularSample(Vertex vertex, Vector3f wo, Vector3f wi, TransportMode mode, Sampler sampler, out BSDFSample sample)
        {
            sample = default;
            for (int attempt = 0; attempt < 512; ++attempt)
            {
                pstd.optional<BSDFSample> bs = vertex.bsdf.Sample_f(wo, sampler.Get1D(), sampler.Get2D(), mode, BxDFReflTransFlags.All);
                if (!bs || bs.value().pdf == 0 || !bs.value().f)
                {
                    continue;
                }
                if (!bs.value().IsSpecular())
                {
                    // (a BSDF with lobes that are not specular: after a few of them the vertex is not taken for a specular one)
                    if (attempt >= 32)
                    {
                        return false;
                    }
                    continue;
                }
                if (Dot(bs.value().wi, wi) < 0.9999f)
                {
                    continue;
                }
                sample = bs.value();
                return true;
            }
            return false;
        }

        // The local terms of a surface vertex whose directions wCam and wLight are joined by a specular lobe
        public static bool SetSpecularTerms(Vertex vertex, Sampler sampler)
        {
            if (vertex.type != VertexType.Surface)
            {
                return false;
            }
            if (!SpecularSample(vertex, vertex.wCam, vertex.wLight, TransportMode.Radiance, sampler, out BSDFSample toLight) ||
                !SpecularSample(vertex, vertex.wLight, vertex.wCam, TransportMode.Importance, sampler, out BSDFSample toCamera))
            {
                return false;
            }
            Normal3f ns = vertex.si.shading.n, ng = vertex.si.n;
            vertex.delta = true;
            vertex.reflected = toLight.IsReflection();
            vertex.eta = vertex.reflected ? 1 : toLight.eta;
            vertex.connect = toLight.f * AbsDot(vertex.wLight, ns);
            vertex.step = vertex.connect / toLight.pdf;
            // (the weight of pbrt's light subpaths: f |wi . ns| / pdf with the correction of the shading normal for importance)
            Float cosLight = AbsDot(vertex.wLight, ng), cosCamShading = AbsDot(vertex.wCam, ns);
            if (cosLight == 0 || cosCamShading == 0)
            {
                return false;
            }
            vertex.connectI = toCamera.f * (AbsDot(vertex.wLight, ns) * AbsDot(vertex.wCam, ng) / cosLight);
            vertex.stepI = vertex.connectI / toCamera.pdf;
            return true;
        }

        // The terms of the vertex on a light for its direction wCam. False if it does not emit that way. A light without an area
        // emits an intensity: it is what the light gives at the distance 1 (Light.SampleLi()), and it has no cosine.
        public static bool SetLightTerms(Vertex vertex, LightSampler lightSampler, SampledWavelengths lambda, Float time)
        {
            Float pdfPos, pdfDir;
            if (InEmittingMedium(vertex))
            {
                // (no strategy starts from the emission of a medium: its densities from the light side are zero)
                vertex.emitted = vertex.emission;
                vertex.connectI = vertex.emitted;
                vertex.stepI = new SampledSpectrum(0.0f);
                vertex.pdfLight = 0;
                return (bool)vertex.emitted;
            }
            DiffuseAreaLight area = vertex.ei.light as DiffuseAreaLight;
            if (area == null)
            {
                Light light = vertex.ei.light;
                LightSampleContext ctx = new LightSampleContext(new Point3fi(vertex.ei.p() + vertex.wCam), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0),
                                                                time);
                pstd.optional<LightLiSample> ls = light.SampleLi(ctx, new Point2f(0.5f, 0.5f), lambda);
                if (!ls || !ls.value().L)
                {
                    return false;
                }
                vertex.emitted = ls.value().L;
                light.PDF_Le(new Ray(vertex.ei.p(), vertex.wCam, time), &pdfPos, &pdfDir);
                vertex.connectI = vertex.emitted;
                vertex.stepI = pdfDir > 0 ? vertex.connectI / pdfDir : new SampledSpectrum(0.0f);
                vertex.pdfLight = lightSampler.PMF(light);
                return true;
            }
            vertex.emitted = area.L(vertex.ei.p(), vertex.ei.n, vertex.ei.uv, vertex.wCam, lambda, time);
            if (!vertex.emitted)
            {
                return false;
            }
            area.PDF_Le(vertex.ei, vertex.wCam, &pdfPos, &pdfDir);
            vertex.connectI = vertex.emitted * AbsDot(vertex.ei.n, vertex.wCam);
            vertex.stepI = pdfDir > 0 ? vertex.connectI / pdfDir : new SampledSpectrum(0.0f);
            vertex.pdfLight = lightSampler.PMF(area) * area.PdfArea();
            return true;
        }

        // What a guided step of the regeneration of a path did (for the second stage of a delayed rejection; not in pbrt-v4):
        // its kind (0 a scattering, 1 the direction that leaves an area light, 2 the point on the film), the vertex made and
        // the vertex of the guide. The steps are kept in stepRecord when it is not null; stepScale (when positive) shrinks
        // the step of the regeneration.
        public sealed class StepRecord
        {
            public StepRecord(int kind, Vertex made, Vertex guide, bool fromCamera)
            {
                this.kind = kind;
                this.made = made;
                this.guide = guide;
                this.fromCamera = fromCamera;
            }

            public int kind;
            public Vertex made, guide;
            public bool fromCamera;
        }

        [ThreadStatic] public static List<StepRecord> stepRecord;
        [ThreadStatic] public static Float stepScale;

        // The density of the move of PerturbNumber() by a difference: a Gaussian around the circle
        public static double StepDensity(double difference, double sigma)
        {
            double sum = 0;
            for (int wrap = -1; wrap <= 1; ++wrap)
            {
                double z = (difference + wrap) / sigma;
                sum += Math.Exp(-0.5 * z * z);
            }
            return sum / (sigma * Math.Sqrt(2 * Math.PI));
        }

        // The probability that a number drawn uniformly in [a, b) and moved by PerturbNumber() lands in [c, d)
        public static double StepProbability(double a, double b, double c, double d, double sigma)
        {
            if (!(b > a))
            {
                return 0;
            }
            double sum = 0;
            for (int wrap = -1; wrap <= 1; ++wrap)
            {
                sum += StepIntegral((d + wrap - a) / sigma) - StepIntegral((c + wrap - a) / sigma) - StepIntegral((d + wrap - b) / sigma) +
                       StepIntegral((c + wrap - b) / sigma);
            }
            return Math.Max(0.0, sum * sigma / (b - a));
        }

        // The integral of the normal distribution function: z Phi(z) + phi(z)
        static double StepIntegral(double z)
        {
            return z * 0.5 * (1 + std.erf(z / Math.Sqrt(2.0))) + Math.Exp(-0.5 * z * z) / Math.Sqrt(2 * Math.PI);
        }

        // A number of [0, 1) moved by a small Gaussian step, around the circle (Kelemen et al. 2002; the small step of pbrt's
        // MLTSampler): the same density both ways
        public static Float PerturbNumber(Float value, Float sigma, Sampler sampler)
        {
            Float radius = sigma * std.sqrt(-2 * std.log(std.max<Float>(1e-12f, 1 - sampler.Get1D())));
            Float moved = value + radius * std.cos(2 * Pi * sampler.Get1D());
            moved -= std.floor(moved);
            return std.min<Float>(moved, OneMinusEpsilon);
        }

        // The random numbers of a scattering at a vertex: new ones, or, with a guide (a vertex of the current path, with its
        // directions), the numbers that give the guide's scattering (BSDF.InvertSample()), moved a little: the small step of
        // primary sample space, in path space (Otsu et al. 2017). `guided` tells whether the numbers are the guide's.
        static void ScatteringNumbers(Vertex guide, bool fromCamera, Float sigma, Sampler sampler, out Float uc, out Point2f u, out bool guided)
        {
            uc = sampler.Get1D();
            u = sampler.Get2D();
            guided = false;
            if (guide == null || guide.type == VertexType.Light || guide.type == VertexType.Camera)
            {
                return;
            }
            pstd.optional<BxDFSampleInverse> inverse = fromCamera
                ? guide.bsdf.InvertSample(guide.wCam, guide.wLight, TransportMode.Radiance)
                : guide.bsdf.InvertSample(guide.wLight, guide.wCam, TransportMode.Importance);
            if (!inverse)
            {
                return;
            }
            guided = true;
            // (any number of the interval of the guide's lobe gives the guide's scattering)
            uc = PerturbNumber(Lerp(uc, inverse.value().ucMin, inverse.value().ucMax), sigma, sampler);
            if (!inverse.value().anyU)
            {
                u = new Point2f(PerturbNumber(inverse.value().u.x, sigma, sampler), PerturbNumber(inverse.value().u.y, sigma, sampler));
            }
        }

        // Samples the direction that leaves a vertex toward the light side (wLight) from its direction wCam, sets its terms and
        // the stack of media on its light side. False if there is none. With a guide the direction is the guide's, moved a
        // little; the move has a way back only if the scattering sampled here has an inverse exactly when the guide's has.
        public static bool StepFromCamera(Vertex vertex, Sampler sampler, out RayDifferential ray, Vertex guide = null, Float sigma = 0)
        {
            ray = default;
            ScatteringNumbers(guide, true, sigma, sampler, out Float uc, out Point2f u2, out bool guided);
            pstd.optional<BSDFSample> bs = vertex.type == VertexType.Surface
                ? vertex.bsdf.Sample_f(vertex.wCam, uc, u2, TransportMode.Radiance, BxDFReflTransFlags.All)
                : vertex.bsdf.Sample_f(vertex.wCam, 0.0f, u2);
            if (!bs || bs.value().pdf == 0 || !bs.value().f)
            {
                return false;
            }
            vertex.wLight = bs.value().wi;
            if (guide != null && (bool)vertex.bsdf.InvertSample(vertex.wCam, vertex.wLight, TransportMode.Radiance) != guided)
            {
                return false;
            }
            if (bs.value().IsSpecular() ? !SetSpecularTerms(vertex, sampler) : !SetTerms(vertex))
            {
                return false;
            }
            vertex.stackLight = LightSideStack(vertex);
            ray = vertex.GetInteraction().SpawnRay(vertex.wLight);
            if (guide != null)
            {
                stepRecord?.Add(new StepRecord(0, vertex, guide, true));
            }
            return true;
        }

        // The same from the light side: samples wCam from wLight (at the light: from the cosine about its normal), and sets
        // the stack of media on the camera side
        public static bool StepFromLight(Vertex vertex, Sampler sampler, out RayDifferential ray, Vertex guide = null, Float sigma = 0,
                                         SampledWavelengths lambda = default, Float time = 0)
        {
            ray = default;
            if (InEmittingMedium(vertex))
            {
                return false;
            }
            if (vertex.type == VertexType.Light && !OnAreaLight(vertex))
            {
                // A light without an area samples its own directions (always anew: its sampling has no inverse here)
                pstd.optional<LightLeSample> les = vertex.ei.light.SampleLe(sampler.Get2D(), sampler.Get2D(), ref lambda, time);
                if (!les || les.value().pdfDir == 0 || !les.value().L)
                {
                    return false;
                }
                vertex.wCam = Normalize(les.value().ray.d);
                ray = vertex.ei.SpawnRay(vertex.wCam);
                return true;
            }
            if (vertex.type == VertexType.Light)
            {
                DiffuseAreaLight area = (DiffuseAreaLight)vertex.ei.light;
                Vector3f local;
                if (guide != null)
                {
                    // (the direction of the guide, on its side of the light, moved a little)
                    Vector3f old = Frame.FromZ(guide.ei.n).ToLocal(guide.wCam);
                    Point2f u = InvertCosineHemisphereSample(new Vector3f(old.x, old.y, std.abs(old.z)));
                    local = SampleCosineHemisphere(new Point2f(PerturbNumber(u.x, sigma, sampler), PerturbNumber(u.y, sigma, sampler)));
                    if (old.z < 0)
                    {
                        local = -local;
                    }
                }
                else
                {
                    local = SampleCosineHemisphere(sampler.Get2D());
                    if (area.TwoSided() && sampler.Get1D() < 0.5f)
                    {
                        local = -local;
                    }
                }
                vertex.wCam = Normalize(Frame.FromZ(vertex.ei.n).FromLocal(local));
                ray = vertex.ei.SpawnRay(vertex.wCam);
                if (guide != null)
                {
                    stepRecord?.Add(new StepRecord(1, vertex, guide, false));
                }
                return true;
            }
            ScatteringNumbers(guide, false, sigma, sampler, out Float uc, out Point2f u2, out bool guided);
            pstd.optional<BSDFSample> bs = vertex.type == VertexType.Surface
                ? vertex.bsdf.Sample_f(vertex.wLight, uc, u2, TransportMode.Importance, BxDFReflTransFlags.All)
                : vertex.bsdf.Sample_f(vertex.wLight, 0.0f, u2);
            if (!bs || bs.value().pdf == 0 || !bs.value().f)
            {
                return false;
            }
            vertex.wCam = bs.value().wi;
            if (guide != null && (bool)vertex.bsdf.InvertSample(vertex.wLight, vertex.wCam, TransportMode.Importance) != guided)
            {
                return false;
            }
            if (bs.value().IsSpecular() ? !SetSpecularTerms(vertex, sampler) : !SetTerms(vertex))
            {
                return false;
            }
            vertex.stack = CameraSideStack(vertex);
            ray = vertex.GetInteraction().SpawnRay(vertex.wCam);
            if (guide != null)
            {
                stepRecord?.Add(new StepRecord(0, vertex, guide, false));
            }
            return true;
        }

        // The spectrum of the contribution of a path: the factor of every scattering, the emission, the weight of the camera ray
        // and the value of every segment over the density a walk has for it. No density of a vertex is in it.
        public static SampledSpectrum UnsampledValue(PathSpacePath path)
        {
            Vertex[] v = path.v;
            SampledSpectrum W = v[0].emitted * v[path.k].step;
            for (int i = 1; i < path.k; ++i)
            {
                W *= v[i].connect;
            }
            for (int i = 0; i < path.k; ++i)
            {
                W *= v[i].segU * v[i].vtx;
            }
            return W;
        }

        // The value over the density, for the hero wavelength's densities, of the n vertices after index a of a path (a = -1: from
        // the light vertex) and of the directions that their two neighbours a and a + n + 1 have toward them, when l of them
        // are a walk from the light side and the others a walk from the camera side. Zero if the split does not make them.
        public static SampledSpectrum PieceWeight(PathSpacePath path, int a, int n, int l)
        {
            Vertex[] v = path.v;
            int B = a + n + 1;
            SampledSpectrum W = new SampledSpectrum(1.0f);
            int first = Math.Max(a, 0);
            // (a light without an area is not seen by the camera, as in volpath and bdpt: it would be one bright point)
            if (path.k == 1 && !OnAreaLight(v[0]) && !InEmittingMedium(v[0]))
            {
                return new SampledSpectrum(0.0f);
            }
            if (a == -1 && l == 0)
            {
                // A walk from the camera side that ends on the light
                if (!OnAreaLight(v[0]) && !InEmittingMedium(v[0]))
                {
                    return new SampledSpectrum(0.0f);
                }
                W = v[0].emitted;
                for (int i = 1; i <= B; ++i)
                {
                    W *= v[i].step;
                }
                for (int i = 0; i < B; ++i)
                {
                    W *= v[i].segU;
                }
            }
            else
            {
                int c = a + l;
                if (c + 1 > path.k || v[c].delta || v[c + 1].delta || !(v[c].segL[0] > 0))
                {
                    return new SampledSpectrum(0.0f);
                }
                if (a == -1)
                {
                    if (!(v[0].pdfLight > 0))
                    {
                        return new SampledSpectrum(0.0f);
                    }
                    W /= v[0].pdfLight;
                }
                for (int i = first; i < c; ++i)
                {
                    W *= v[i].stepI;
                }
                W *= v[c].connectI;
                for (int i = c + 2; i <= B; ++i)
                {
                    W *= v[i].step;
                }
                W *= v[c + 1].connect;
                Float distanceSquared = DistanceSquared(v[c].p(), v[c + 1].p());
                if (distanceSquared == 0)
                {
                    return new SampledSpectrum(0.0f);
                }
                for (int i = first; i < B; ++i)
                {
                    W *= v[i].segU;
                }
                W /= v[c].segL[0] * distanceSquared;
            }
            for (int i = a + 1; i < B; ++i)
            {
                W *= v[i].vtx;
            }
            return W;
        }

        // The spectrum of everything that a mutation of the n vertices after index a keeps
        public static SampledSpectrum KeptWeight(PathSpacePath path, int a, int n)
        {
            Vertex[] v = path.v;
            int B = a + n + 1;
            SampledSpectrum W = new SampledSpectrum(1.0f);
            if (a >= 1)
            {
                W *= v[0].emitted;
            }
            if (B < path.k)
            {
                W *= v[path.k].step;
            }
            for (int i = 1; i < path.k; ++i)
            {
                if (i < a || i > B)
                {
                    W *= v[i].connect;
                }
                if (i <= a || i >= B)
                {
                    W *= v[i].vtx;
                }
            }
            for (int i = 0; i < path.k; ++i)
            {
                if (i < a || i >= B)
                {
                    W *= v[i].segU;
                }
            }
            return W;
        }
    }

    // ChainTracer Definition: the nearest surface with a BSDF along a ray of a specular chain (the boundaries between media
    // are passed through), with the BSDFs of the surfaces that it has found
    public sealed unsafe class ChainTracer
    {
        public ChainTracer(Integrator integrator, Camera camera, SampledWavelengths lambda, ScratchBuffer scratchBuffer, Sampler sampler,
                           Interaction anchor)
        {
            this.integrator = integrator;
            this.camera = camera;
            this.lambda = lambda;
            this.scratchBuffer = scratchBuffer;
            this.sampler = sampler;
            this.anchor = anchor;
        }

        public SurfaceInteraction Trace(SurfaceInteraction from, Point3f origin, Vector3f direction)
        {
            RayDifferential r = from != null ? from.SpawnRay(direction) : anchor.SpawnRay(direction);
            while (true)
            {
                pstd.optional<ShapeIntersection> si = integrator.Intersect(new Ray(r));
                if (!si)
                {
                    return null;
                }
                SurfaceInteraction isect = si.value().intr;
                BSDF bsdf = isect.GetBSDF(r, ref lambda, camera, scratchBuffer, sampler);
                if (!bsdf)
                {
                    isect.SkipIntersection(ref r, si.value().tHit);
                    continue;
                }
                surfaces.Add(isect);
                bsdfs.Add(bsdf);
                return isect;
            }
        }

        public BSDF BsdfOf(SurfaceInteraction surface)
        {
            for (int i = surfaces.Count - 1; i >= 0; --i)
            {
                if (ReferenceEquals(surfaces[i], surface))
                {
                    return bsdfs[i];
                }
            }
            return default;
        }

        public SampledWavelengths lambda;
        Integrator integrator;
        Camera camera;
        ScratchBuffer scratchBuffer;
        Sampler sampler;
        Interaction anchor;
        List<SurfaceInteraction> surfaces = new List<SurfaceInteraction>();
        List<BSDF> bsdfs = new List<BSDF>();
    }

    // PathSpaceMLTIntegrator Definition
    public sealed unsafe class PathSpaceMLTIntegrator : Integrator
    {
        // PathSpaceMLTIntegrator Public Methods
        public PathSpaceMLTIntegrator(Camera camera, Primitive aggregate, List<Light> lights, bool bidirectional, int maxDepth,
                                      int nBootstrap, int nChains, int mutationsPerPixel, Float largeStepProbability, int seed,
                                      bool weightedSeeds, Float lensPerturbationProbability, Float lensPerturbationRadius, int diagnose,
                                      int integralSamples, Float causticPerturbationProbability, Float causticPerturbationAngle,
                                      Float manifoldPerturbationProbability, Float halfVectorPerturbationProbability,
                                      Float halfVectorPerturbationAngle, Float sampleSpacePerturbationProbability, Float sampleSpaceSigma,
                                      PolarisedPathIntegrator.Sensor sensor, Float multiChainPerturbationProbability,
                                      Float halfVectorPerturbationPixels, bool specularNee, int sneeTrials, bool monteCarlo, int sneeSolver, bool jumpRestore,
                                      Float tourLength, Float sneeTuples)
            : base(aggregate, lights)
        {
            this.camera = camera;
            this.bidirectional = bidirectional;
            this.maxDepth = maxDepth;
            this.nBootstrap = nBootstrap;
            this.nChains = nChains;
            this.mutationsPerPixel = mutationsPerPixel;
            this.largeStepProbability = largeStepProbability;
            this.seed = seed;
            this.weightedSeeds = weightedSeeds;
            this.lensPerturbationProbability = lensPerturbationProbability;
            this.lensPerturbationRadius = lensPerturbationRadius;
            this.diagnose = diagnose;
            this.integralSamples = integralSamples;
            this.causticPerturbationProbability = causticPerturbationProbability;
            this.causticPerturbationAngle = causticPerturbationAngle;
            this.manifoldPerturbationProbability = manifoldPerturbationProbability;
            this.halfVectorPerturbationProbability = halfVectorPerturbationProbability;
            this.halfVectorPerturbationAngle = halfVectorPerturbationAngle;
            this.sampleSpacePerturbationProbability = sampleSpacePerturbationProbability;
            this.sampleSpaceSigma = sampleSpaceSigma;
            this.multiChainPerturbationProbability = multiChainPerturbationProbability;
            this.halfVectorPerturbationPixels = halfVectorPerturbationPixels;
            this.specularNee = specularNee;
            this.sneeTrials = sneeTrials;
            this.monteCarlo = monteCarlo;
            this.sneeSolver = sneeSolver;
            this.jumpRestore = jumpRestore;
            this.tourLength = tourLength;
            this.sneeTuples = sneeTuples;
            // (not in pbrt-v4) The polarised option: the sensor measures the Stokes vector of every path
            this.sensor = sensor;
            if (sensor != null)
            {
                sensor.cameraUp = camera.GetCameraTransform().RenderFromCamera(new Vector3f(0, 1, 0), 0);
            }
            lightSampler = new PowerLightSampler(lights.ToArray(), new Allocator());
        }

        public static PathSpaceMLTIntegrator Create(ParameterDictionary parameters, Camera camera, Primitive aggregate,
                                                    List<Light> lights, FileLoc loc, bool bidirectional, bool jumpRestore = false)
        {
            string name = jumpRestore ? (bidirectional ? "volbidirjrlt" : "volpathjrlt") : (bidirectional ? "volbidirmlt" : "volpathmlt");
            if (!(camera is PerspectiveCamera))
            {
                ErrorExit(StringPrintf("Only the \"perspective\" camera is currently supported with the \"%s\" integrator.", name));
            }
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            // (the bootstrap paths are cheap and their weights have a long tail: few of them seed the chains unevenly)
            int nBootstrap = parameters.GetOneInt("bootstrapsamples", 1000000);
            int nChains = parameters.GetOneInt("chains", 1000);
            int mutationsPerPixel = parameters.GetOneInt("mutationsperpixel", 100);
            if (Options.pixelSamples)
            {
                mutationsPerPixel = Options.pixelSamples.value();
            }
            Float largeStepProbability = parameters.GetOneFloat("largestepprobability", 0.3f);
            foreach (Light light in lights)
            {
                DiffuseAreaLight area = light as DiffuseAreaLight;
                if ((area == null && light.Type() != LightType.DeltaPosition) || (area != null && area.thermal != null))
                {
                    Warning(loc, StringPrintf("\"%s\" does not follow distant, infinite and thermal lights yet: their light is not in the image.", name));
                    break;
                }
            }
            if (jumpRestore && parameters.GetOneBool("specularnee", false))
            {
                ErrorExit(loc, StringPrintf("\"%s\": a tour cannot start from specular next-event estimation (its density is not known); " +
                                            "use \"specularnee\" with \"%s\".", name, bidirectional ? "volbidirmlt" : "volpathmlt"));
            }
            string solver = parameters.GetOneString("sneesolver", "newton");
            int sneeSolver = solver == "newton" ? 0 : solver == "neldermead" ? 1 : solver == "slopespace" ? 2 : solver == "polynomial" ? 3 : solver == "bernstein" ? 4 : -1;
            if (sneeSolver < 0)
            {
                ErrorExit(loc, StringPrintf("\"%s\": unknown \"sneesolver\"; expected \"newton\", \"neldermead\", \"slopespace\", \"polynomial\" or \"bernstein\".", solver));
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
            PathSpaceMLTIntegrator integrator = new PathSpaceMLTIntegrator(camera, aggregate, lights, bidirectional, maxDepth, nBootstrap, nChains, mutationsPerPixel,
                                              largeStepProbability, parameters.GetOneInt("seed", 0), parameters.GetOneBool("weightedseeds", false),
                                              parameters.GetOneBool("lensperturbation", true) ? parameters.GetOneFloat("lensperturbationprobability", 0.3f) : 0,
                                              parameters.GetOneFloat("lensperturbationradius", 0.05f), parameters.GetOneInt("diagnose", 0),
                                              parameters.GetOneInt("integralsamples", 1000000),
                                              parameters.GetOneBool("causticperturbation", true) ? parameters.GetOneFloat("causticperturbationprobability", 0.2f) : 0,
                                              parameters.GetOneFloat("causticperturbationangle", 0.1f),
                                              // (not on unless asked for: it takes the place of the other two perturbations)
                                              parameters.GetOneBool("manifoldperturbation", false) ? parameters.GetOneFloat("manifoldperturbationprobability", 0.3f) : 0,
                                              parameters.GetOneBool("halfvectorperturbation", false) ? parameters.GetOneFloat("halfvectorperturbationprobability", 0.3f) : 0,
                                              parameters.GetOneFloat("halfvectorperturbationangle", 0.02f),
                                              // (the small step of pbrt's "mlt", with its "sigma")
                                              parameters.GetOneBool("samplespaceperturbation", false) ? parameters.GetOneFloat("samplespaceperturbationprobability", 0.3f) : 0,
                                              parameters.GetOneFloat("sigma", 0.01f), sensor,
                                              parameters.GetOneBool("multichainperturbation", false) ? parameters.GetOneFloat("multichainperturbationprobability", 0.2f) : 0,
                                              // (0: the fixed angle for every half vector)
                                              parameters.GetOneFloat("halfvectorperturbationpixels", 2),
                                              parameters.GetOneBool("specularnee", false), parameters.GetOneInt("sneetrials", 100000),
                                              // (the image by the bootstrap paths alone, no chain: plain Monte Carlo)
                                              parameters.GetOneBool("montecarlo", false), sneeSolver, jumpRestore,
                                              parameters.GetOneFloat("tourlength", 30), parameters.GetOneFloat("sneetuples", 8));
            // (a second, smaller or renewed, proposal after a rejection: Tierney and Mira 1999, Rioux-Lavoie et al. 2020)
            integrator.delayedRejection = parameters.GetOneBool("delayedrejection", false);
            integrator.delayedRejectionScale = parameters.GetOneFloat("delayedrejectionscale", 0.125f);
            return integrator;
        }

        public override string ToString()
        {
            return StringPrintf("[ PathSpaceMLTIntegrator camera: %s bidirectional: %s maxDepth: %d nBootstrap: %d nChains: %d " +
                                "mutationsPerPixel: %d largeStepProbability: %f ]", camera, bidirectional, maxDepth, nBootstrap, nChains,
                                mutationsPerPixel, largeStepProbability);
        }

        // The longest path, in segments
        int MaxLength()
        {
            return maxDepth + 1;
        }

        // The weight of deleting d vertices: few rather than many. A path tracer's strategies delete at least the light vertex.
        double DeleteWeight(int d)
        {
            if (!bidirectional)
            {
                return d == 0 ? 0 : Math.Pow(0.5, d - 1);
            }
            return d == 0 ? 0.1 : d == 1 ? 0.3 : d == 2 ? 0.25 : d == 3 ? 0.15 : 0.2 * Math.Pow(0.5, d - 3);
        }

        // The number of places in a path of k segments where d vertices can be deleted: the camera vertex stays, and so does the
        // light vertex when nothing is deleted (new vertices do not go before it)
        int DeletePlaces(int d, int k)
        {
            if (!bidirectional)
            {
                return 1;
            }
            return d == 0 ? k : k - d + 1;
        }

        // The probability of deleting the d vertices after index a of a path of k segments. All the vertices but the camera's
        // (a new path) have the probability of a large step on top.
        double DeleteProbability(int a, int d, int k)
        {
            double sum = 0;
            for (int i = 0; i <= k; ++i)
            {
                sum += DeleteWeight(i);
            }
            double p = (1 - largeStepProbability) * DeleteWeight(d) / (DeletePlaces(d, k) * sum);
            if (a == -1 && d == k)
            {
                p += largeStepProbability;
            }
            return p;
        }

        void SampleDelete(int k, Sampler sampler, out int a, out int d)
        {
            Float u = sampler.Get1D();
            Float uPlace = sampler.Get1D();
            if (u < largeStepProbability)
            {
                a = -1;
                d = k;
                return;
            }
            u = (u - largeStepProbability) / (1 - largeStepProbability);
            double sum = 0;
            for (int i = 0; i <= k; ++i)
            {
                sum += DeleteWeight(i);
            }
            double cumulative = 0;
            d = k;
            for (int i = 0; i <= k; ++i)
            {
                cumulative += DeleteWeight(i) / sum;
                if (u < cumulative)
                {
                    d = i;
                    break;
                }
            }
            int places = DeletePlaces(d, k);
            int place = Math.Min((int)(uPlace * places), places - 1);
            a = !bidirectional ? -1 : d == 0 ? place : place - 1;
        }

        // The weight of making n vertices in place of d: as many, or one or two more or fewer (not none in place of none)
        static double AddWeight(int n, int d)
        {
            if (n == 0 && d == 0)
            {
                return 0;
            }
            int difference = Math.Abs(n - d);
            return difference == 0 ? 0.6 : difference == 1 ? 0.15 : difference == 2 ? 0.05 : 0;
        }

        // The probability of making n vertices in place of d after index a, in a path that keeps `kept` segments
        double AddProbability(int n, int d, int a, int kept)
        {
            int least = a == -1 ? 1 : 0, most = MaxLength() - kept;
            if (n < least || n > most)
            {
                return 0;
            }
            double sum = 0;
            for (int i = least; i <= most; ++i)
            {
                sum += AddWeight(i, d);
            }
            return sum > 0 ? AddWeight(n, d) / sum : 0;
        }

        int SampleAdd(int d, int a, int kept, Float u)
        {
            int least = a == -1 ? 1 : 0, most = MaxLength() - kept;
            double cumulative = 0;
            for (int n = least; n <= most; ++n)
            {
                cumulative += AddProbability(n, d, a, kept);
                if (u < cumulative)
                {
                    return n;
                }
            }
            return -1;
        }

        // The splits of n new vertices: every number of them from the light side, or the two strategies of a path tracer
        static int SplitCount(int n, bool allSplits)
        {
            return allSplits ? n + 1 : Math.Min(n + 1, 2);
        }

        // Q / c for the n vertices after index a of a path, without the discrete probabilities of the deletion: the density of
        // the mixture of the splits over the luminance of the path (zero if no split samples it)
        static double InverseWeight(PathSpacePath path, int a, int n, bool allSplits)
        {
            SampledSpectrum kept = PathSpace.KeptWeight(path, a, n);
            int splits = SplitCount(n, allSplits);
            double inverse = 0;
            bool any = false;
            for (int l = 0; l < splits; ++l)
            {
                Float y = (kept * PathSpace.PieceWeight(path, a, n, l)).y(path.lambda);
                if (y > 0 && !Float.IsInfinity(y))
                {
                    inverse += 1.0 / (splits * y);
                    any = true;
                }
            }
            return any ? inverse : 0;
        }

        // The vertex on a light at a point of its surface
        static Vertex LightVertex(DiffuseAreaLight area, Interaction point)
        {
            Vertex light = Vertex.CreateLight(new EndpointInteraction(area, point), new SampledSpectrum(1.0f), 0);
            light.step = new SampledSpectrum(1.0f);
            return light;
        }

        // The camera vertex of a path, with the camera ray through its point of the film
        bool CameraVertex(PathSpacePath path, out Vertex cameraVertex, out RayDifferential ray)
        {
            cameraVertex = null;
            ray = default;
            CameraSample cameraSample = new CameraSample();
            cameraSample.pFilm = path.pRaster;
            cameraSample.time = path.timeSample;
            cameraSample.pLens = path.pLens;
            pstd.optional<CameraRayDifferential> crd = camera.GenerateRayDifferential(cameraSample, ref path.lambda);
            if (!crd || !crd.value().weight)
            {
                return false;
            }
            ray = crd.value().ray;
            path.time = ray.time;
            cameraVertex = Vertex.CreateCamera(camera, new Ray(ray), new SampledSpectrum(1.0f));
            cameraVertex.step = crd.value().weight;
            cameraVertex.stack = MediumTracking.Start(camera.GetMedium());
            cameraVertex.stackLight = cameraVertex.stack;
            cameraVertex.wLight = Normalize(ray.d);
            return true;
        }

        // The camera's importance is normalised over the pixels of the image, and the camera rays are sampled over the larger
        // area that the pixel filter reaches from, with the weight of one: a connection to the camera is on the scale of the
        // camera rays when its importance is over that larger area.
        Float FilmOverSamples()
        {
            Film film = camera.GetFilm();
            return (Float)film.PixelBounds().Area() / film.SampleBounds().Area();
        }

        // Joins the vertex at the end of the light side (u) and the one at the end of the camera side (z) of a path: their
        // directions toward each other, their terms and the segment between them. With z the camera vertex the point on the film
        // is the one that sees u. False if the connection has no value.
        bool Connect(PathSpacePath path, Vertex u, Vertex z, Sampler sampler)
        {
            Vector3f d = z.p() - u.p();
            Float distanceSquared = LengthSquared(d);
            if (distanceSquared == 0)
            {
                return false;
            }
            Vector3f w = d / std.sqrt(distanceSquared);
            u.wCam = w;
            if (u.type == VertexType.Light ? !PathSpace.SetLightTerms(u, lightSampler, path.lambda, path.time) : !PathSpace.SetTerms(u))
            {
                return false;
            }
            z.wLight = -w;
            if (z.type == VertexType.Camera)
            {
                pstd.optional<CameraWiSample> cs = camera.SampleWi(u.GetInteraction(), sampler.Get2D(), ref path.lambda);
                if (!cs || cs.value().pdf == 0 || !cs.value().Wi)
                {
                    return false;
                }
                path.pRaster = cs.value().pRaster;
                z.connect = cs.value().Wi * (distanceSquared * FilmOverSamples() / cs.value().pdf);
                // (the weight that the ray through that point of the film has, for the mutations that take it as sampled)
                if (!CameraVertex(path, out Vertex sampled, out RayDifferential unused))
                {
                    return false;
                }
                z.step = sampled.step;
            }
            else
            {
                if (!PathSpace.SetTerms(z))
                {
                    return false;
                }
                z.stackLight = PathSpace.LightSideStack(z);
            }
            if (!PathSpace.ConnectSegment(this, z.GetInteraction(), u.GetInteraction(), z.stackLight, path.lambda, sampler, out SampledSpectrum T,
                                          out Float walkOverConnection, out BoundaryStack unusedArrival))
            {
                return false;
            }
            // (the value of the segment over the density of a walk, and the density of a connection over that of a walk)
            u.segU = T / walkOverConnection;
            u.segL = new SampledSpectrum(1 / walkOverConnection);
            if (u.type != VertexType.Light)
            {
                u.stack = PathSpace.CameraSideStack(u);
            }
            return true;
        }

        // The connection weight of the camera vertex of a path for the vertex next to it (Vertex.connect of the camera): every
        // path has it, whichever way it was made
        bool SetCameraConnection(PathSpacePath path)
        {
            Vertex cameraVertex = path.v[path.k], first = path.v[path.k - 1];
            pstd.optional<CameraWiSample> cs = camera.SampleWi(first.GetInteraction(), new Point2f(0.5f, 0.5f), ref path.lambda);
            if (!cs || cs.value().pdf == 0 || !cs.value().Wi)
            {
                return false;
            }
            cameraVertex.connect = cs.value().Wi * (DistanceSquared(cameraVertex.p(), first.p()) * FilmOverSamples() / cs.value().pdf);
            return true;
        }

        // A path that keeps the vertices of `current` up to index a and from index a + d + 1, and has n new vertices between
        // them, l made by a walk from the light side and n - l by a walk from the camera side. With a = -1 the light vertex is new,
        // with a + d + 1 = k and a walk from the camera side the point on the film is, and with both the wavelengths and the
        // time are. Null if the split makes no path.
        // With a guide (a path of the same length, for a whole new path of the same split), nothing is sampled anew that has an
        // inverse: the wavelengths, the time, the point on the lens and the point on the light are the guide's, and the point on
        // the film and every scattering direction are the guide's moved by a small step of their random numbers.
        PathSpacePath Regenerate(PathSpacePath current, int a, int d, int n, int l, Sampler sampler, ScratchBuffer scratchBuffer,
                                 PathSpacePath guide = null)
        {
            int k = current == null ? d : current.k;
            int b = a + d + 1, B = a + n + 1, m = n - l;
            bool whole = a == -1 && b == k;
            PathSpacePath path = new PathSpacePath();
            path.k = k - d + n;
            path.v = new Vertex[path.k + 1];
            if (guide != null)
            {
                path.lambda = guide.lambda;
                path.pRaster = guide.pRaster;
                path.pLens = guide.pLens;
                path.timeSample = guide.timeSample;
                path.time = guide.time;
                if (m >= 1)
                {
                    // (the camera ray is sampled: the point on the film moves by a small step of its two numbers)
                    Bounds2f sampleBounds = camera.GetFilm().SampleBounds();
                    Vector2f offset = sampleBounds.Offset(guide.pRaster);
                    path.pRaster = sampleBounds.Lerp(new Point2f(PathSpace.PerturbNumber(offset.x, StepSigma(), sampler),
                                                                 PathSpace.PerturbNumber(offset.y, StepSigma(), sampler)));
                    PathSpace.stepRecord?.Add(new PathSpace.StepRecord(2, null, null, true));
                }
            }
            else if (whole)
            {
                // Sample wavelengths and the camera sample of the path
                path.lambda = Options.disableWavelengthJitter ? camera.GetFilm().SampleWavelengths(0.5f)
                                                              : camera.GetFilm().SampleWavelengths(sampler.Get1D());
                path.pRaster = camera.GetFilm().SampleBounds().Lerp(sampler.Get2D());
                path.timeSample = sampler.Get1D();
                path.pLens = sampler.Get2D();
                path.time = camera.SampleTime(path.timeSample);
            }
            else
            {
                path.lambda = current.lambda;
                path.pRaster = current.pRaster;
                path.pLens = current.pLens;
                path.timeSample = current.timeSample;
                path.time = current.time;
                for (int i = 0; i < a; ++i)
                {
                    path.v[i] = current.v[i];
                }
                for (int i = b + 1; i <= k; ++i)
                {
                    path.v[i - b + B] = current.v[i];
                }
                // (the two vertices next to the new ones get new directions and terms)
                if (a >= 0)
                {
                    path.v[a] = current.v[a].Clone();
                }
                path.v[B] = current.v[b].Clone();
            }
            bool secondaryTerminated = path.lambda.SecondaryTerminated();
            RayDifferential ray = default;

            // The camera vertex of a path whose first segment is new
            if (b == k)
            {
                if (m >= 1 && !whole)
                {
                    path.pRaster = camera.GetFilm().SampleBounds().Lerp(sampler.Get2D());
                }
                if (!CameraVertex(path, out Vertex cameraVertex, out ray))
                {
                    return null;
                }
                path.v[B] = cameraVertex;
            }

            // Follow the walk from the camera side
            Vertex last = path.v[B];
            for (int j = 0; j < m; ++j)
            {
                if (!(j == 0 && b == k) && !PathSpace.StepFromCamera(last, sampler, out ray, guide != null ? guide.v[B - j] : null, StepSigma()))
                {
                    return null;
                }
                if (!PathSpace.TraceSegment(this, camera, ray, last.stackLight, ref path.lambda, sampler, scratchBuffer, out Vertex next,
                                            a == -1 && l == 0 && j == m - 1))
                {
                    return null;
                }
                // (a path that loses its other wavelengths at a new vertex has no way back to the one that had them)
                if ((!whole || guide != null) && path.lambda.SecondaryTerminated() != secondaryTerminated)
                {
                    return null;
                }
                next.wCam = -Normalize(ray.d);
                int index = B - 1 - j;
                if (index == 0 && PathSpace.InEmittingMedium(next))
                {
                    // The walk ended where a medium emits
                    if (!PathSpace.SetLightTerms(next, lightSampler, path.lambda, path.time))
                    {
                        return null;
                    }
                }
                else if (index == 0)
                {
                    // The walk has to end on a light: the vertex is the light's
                    if (next.type != VertexType.Surface)
                    {
                        return null;
                    }
                    DiffuseAreaLight area = next.si.areaLight as DiffuseAreaLight;
                    if (area == null || area.thermal != null)
                    {
                        return null;
                    }
                    Vertex light = LightVertex(area, next.si);
                    light.stack = next.stack;
                    light.segU = next.segU;
                    light.segL = next.segL;
                    light.wCam = next.wCam;
                    if (!PathSpace.SetLightTerms(light, lightSampler, path.lambda, path.time))
                    {
                        return null;
                    }
                    next = light;
                }
                path.v[index] = next;
                last = next;
            }
            if (a == -1 && l == 0)
            {
                // (volpath gathers the emission of a medium up to maxdepth segments, one fewer than the light of a surface)
                if (PathSpace.InEmittingMedium(path.v[0]) && path.k > maxDepth)
                {
                    return null;
                }
                return SetCameraConnection(path) ? path : null;
            }

            // Follow the walk from the light side
            Vertex lightEnd;
            int traces = l;
            if (a == -1 && guide != null)
            {
                // (the point on the light stays)
                lightEnd = guide.v[0].Clone();
                path.v[0] = lightEnd;
                traces = l - 1;
            }
            else if (a == -1)
            {
                lightEnd = SampleLightVertex(path, sampler);
                if (lightEnd == null)
                {
                    return null;
                }
                path.v[0] = lightEnd;
                traces = l - 1;
            }
            else
            {
                lightEnd = path.v[a];
            }
            for (int j = 0; j < traces; ++j)
            {
                if (!PathSpace.StepFromLight(lightEnd, sampler, out ray, guide != null ? guide.v[j] : null, StepSigma(), path.lambda, path.time))
                {
                    return null;
                }
                if (lightEnd.type == VertexType.Light)
                {
                    if (!PathSpace.SetLightTerms(lightEnd, lightSampler, path.lambda, path.time))
                    {
                        return null;
                    }
                    // (the media on the side of the light that the walk leaves from, from the geometry: an intersection does
                    // not record a medium that is the same on both of its sides)
                    lightEnd.stack = BDPT.StackAtPoint(this, camera, ray.o, path.time);
                }
                if (!PathSpace.TraceSegment(this, camera, ray, lightEnd.stack, ref path.lambda, sampler, scratchBuffer, out Vertex next))
                {
                    return null;
                }
                if ((!whole || guide != null) && path.lambda.SecondaryTerminated() != secondaryTerminated)
                {
                    return null;
                }
                // (the segment is kept at its light-side vertex)
                lightEnd.segU = next.segU;
                lightEnd.segL = next.segL;
                next.segU = new SampledSpectrum(1.0f);
                next.segL = new SampledSpectrum(1.0f);
                next.wLight = -Normalize(ray.d);
                next.stackLight = next.stack;
                int index = (a == -1 ? 1 : a + 1) + j;
                path.v[index] = next;
                lightEnd = next;
            }

            // Connect the ends of the two walks
            if (!Connect(path, lightEnd, last, sampler))
            {
                return null;
            }
            if (PathSpace.InEmittingMedium(path.v[0]) && path.k > maxDepth)
            {
                return null;
            }
            return SetCameraConnection(path) ? path : null;
        }

        // The direction of the specular lobe of a kind (reflection or transmission) of a surface vertex, from its camera side
        // (it sets wLight) or from its light side (it sets wCam)
        static bool SpecularDirection(Vertex vertex, bool reflection, bool fromCamera, Sampler sampler)
        {
            for (int attempt = 0; attempt < 512; ++attempt)
            {
                pstd.optional<BSDFSample> bs = vertex.bsdf.Sample_f(fromCamera ? vertex.wCam : vertex.wLight, sampler.Get1D(), sampler.Get2D(),
                                                                    fromCamera ? TransportMode.Radiance : TransportMode.Importance,
                                                                    BxDFReflTransFlags.All);
                if (!bs || bs.value().pdf == 0 || !bs.value().f)
                {
                    continue;
                }
                if (!bs.value().IsSpecular())
                {
                    if (attempt >= 32)
                    {
                        return false;
                    }
                    continue;
                }
                if (bs.value().IsReflection() == reflection)
                {
                    if (fromCamera)
                    {
                        vertex.wLight = bs.value().wi;
                    }
                    else
                    {
                        vertex.wCam = bs.value().wi;
                    }
                    return true;
                }
            }
            return false;
        }

        // The spectrum of the contribution of a path with the terms of a walk from the light from vertex i on, and the camera's
        // connection: what the caustic perturbation changes, without the density of any vertex
        static SampledSpectrum LightFormValue(PathSpacePath path, int i)
        {
            Vertex[] v = path.v;
            SampledSpectrum W = v[path.k].connect;
            if (i > 0)
            {
                W *= v[0].emitted;
            }
            for (int j = 1; j < i; ++j)
            {
                W *= v[j].connect;
            }
            for (int j = i; j < path.k; ++j)
            {
                W *= v[j].connectI;
            }
            for (int j = 0; j < path.k; ++j)
            {
                W *= v[j].segU * v[j].vtx;
            }
            return W;
        }

        // The caustic perturbation (Veach and Guibas 1997, section 5.3.2): the vertex that the camera sees (k - 1) is not
        // specular and is reached, through a chain of specular vertices (possibly none), from a vertex that is not specular or
        // from the light (index i). The direction that leaves vertex i turns a little, the chain is traced again from there to a
        // new vertex k - 1, and that vertex is connected to the camera, at a new point of the film. The weights are as for the
        // lens perturbation: the contribution over what the move samples, up to the symmetric density of the turn.
        // `scale` shrinks the turn; `placed` tells whether the path has a place for the perturbation, and `was` and `turned`
        // are the direction of the current path and the new one (for the second stage of a delayed rejection).
        PathSpacePath CausticPerturbation(PathSpacePath current, Sampler sampler, ScratchBuffer scratchBuffer, out double weightProposed,
                                          out double weightCurrent, Float scale, out bool placed, out Vector3f was, out Vector3f turned)
        {
            weightProposed = 0;
            weightCurrent = 0;
            placed = false;
            was = default;
            turned = default;
            int k = current.k;
            Vertex[] v = current.v;
            if (k < 2 || v[k - 1].delta)
            {
                return null;
            }
            int i = k - 2;
            while (i > 0 && v[i].delta)
            {
                --i;
            }

            // Turn the direction: an angle between theta1 and theta2 with a density that falls as 1 / theta, about the old one
            Float theta2 = causticPerturbationAngle * scale, theta1 = std.min<Float>(0.0001f, theta2);
            Float theta = theta2 * std.exp(-std.log(theta2 / theta1) * sampler.Get1D());
            Float phi = 2 * Pi * sampler.Get1D();
            Vector3f local = new Vector3f(std.sin(theta) * std.cos(phi), std.sin(theta) * std.sin(phi), std.cos(theta));
            Vector3f direction = Normalize(Frame.FromZ(v[i].wCam).FromLocal(local));
            placed = true;
            was = v[i].wCam;
            turned = direction;

            PathSpacePath path = new PathSpacePath();
            path.k = k;
            path.v = new Vertex[k + 1];
            path.lambda = current.lambda;
            path.pRaster = current.pRaster;
            path.pLens = current.pLens;
            path.timeSample = current.timeSample;
            path.time = current.time;
            for (int j = 0; j < i; ++j)
            {
                path.v[j] = v[j];
            }
            Vertex from = v[i].Clone();
            path.v[i] = from;
            from.wCam = direction;
            RayDifferential ray;
            if (from.type == VertexType.Light)
            {
                if (!PathSpace.SetLightTerms(from, lightSampler, path.lambda, path.time))
                {
                    return null;
                }
                ray = from.ei.SpawnRay(direction);
                from.stack = BDPT.StackAtPoint(this, camera, ray.o, path.time);
            }
            else
            {
                // (the direction stays on its side of the surface: the stack of media of the vertex is that of the side)
                if (from.type == VertexType.Surface && (Dot(from.si.n, v[i].wCam) > 0) != (Dot(from.si.n, direction) > 0))
                {
                    return null;
                }
                if (!PathSpace.SetTerms(from))
                {
                    return null;
                }
                ray = from.GetInteraction().SpawnRay(direction);
            }

            // Trace the chain of specular vertices from the light side
            bool secondaryTerminated = current.lambda.SecondaryTerminated();
            Vertex last = from;
            for (int j = i + 1; j <= k - 1; ++j)
            {
                if (!PathSpace.TraceSegment(this, camera, ray, last.stack, ref path.lambda, sampler, scratchBuffer, out Vertex next))
                {
                    return null;
                }
                if (path.lambda.SecondaryTerminated() != secondaryTerminated)
                {
                    return null;
                }
                last.segU = next.segU;
                last.segL = next.segL;
                next.segU = new SampledSpectrum(1.0f);
                next.segL = new SampledSpectrum(1.0f);
                next.wLight = -Normalize(ray.d);
                next.stackLight = next.stack;
                path.v[j] = next;
                last = next;
                if (j < k - 1)
                {
                    if (next.type != VertexType.Surface || !SpecularDirection(next, v[j].reflected, false, sampler) ||
                        !PathSpace.SetSpecularTerms(next, sampler))
                    {
                        return null;
                    }
                    next.stack = PathSpace.CameraSideStack(next);
                    ray = next.si.SpawnRay(next.wCam);
                }
            }

            // Connect the new vertex k - 1 to the camera
            if (!CameraVertex(path, out Vertex cameraVertex, out RayDifferential unused))
            {
                return null;
            }
            path.v[k] = cameraVertex;
            if (!Connect(path, last, cameraVertex, sampler) || !(v[k - 1].segL[0] > 0))
            {
                return null;
            }
            weightProposed = LightFormValue(path, i).y(path.lambda) / (DistanceSquared(last.p(), cameraVertex.p()) * last.segL[0]);
            weightCurrent = LightFormValue(current, i).y(current.lambda) / (DistanceSquared(v[k - 1].p(), v[k].p()) * v[k - 1].segL[0]);
            return path;
        }

        // The lens perturbation (Veach and Guibas 1997, section 5.3.1): the point on the film moves a little, the camera ray is
        // traced again through the same chain of specular vertices to the first vertex that is not specular (index j), and that
        // vertex is connected to the vertex after it of the current path (j - 1), which keeps its position. Null if the path has
        // no such vertices or the perturbed path has no value. The weights are the contribution over the densities of what the
        // perturbation samples (up to the symmetric density of the move on the film), for the proposal and for the current path.
        // `scale` shrinks the move; `placed` tells whether the path has a place for the perturbation, and `moved` is the new
        // point on the film (for the second stage of a delayed rejection).
        PathSpacePath LensPerturbation(PathSpacePath current, Sampler sampler, ScratchBuffer scratchBuffer, out double weightProposed,
                                       out double weightCurrent, Float scale, out bool placed, out Point2f moved)
        {
            weightProposed = 0;
            weightCurrent = 0;
            placed = false;
            moved = default;
            int k = current.k;
            Vertex[] v = current.v;
            int j = k - 1;
            while (j >= 1 && v[j].delta)
            {
                --j;
            }
            if (j < 1 || (j - 1 > 0 && v[j - 1].delta))
            {
                return null;
            }

            // Move the point on the film: a distance between r1 and r2 with a density that falls as 1 / r, any direction
            Bounds2f sampleBounds = camera.GetFilm().SampleBounds();
            LensRadii(scale, out Float r1, out Float r2);
            Float radius = r2 * std.exp(-std.log(r2 / r1) * sampler.Get1D());
            Float phi = 2 * Pi * sampler.Get1D();
            Point2f pRaster = new Point2f(current.pRaster.x + radius * std.cos(phi), current.pRaster.y + radius * std.sin(phi));
            placed = true;
            moved = pRaster;
            if (pRaster.x < sampleBounds.pMin.x || pRaster.x >= sampleBounds.pMax.x || pRaster.y < sampleBounds.pMin.y ||
                pRaster.y >= sampleBounds.pMax.y)
            {
                return null;
            }
            PathSpacePath path = new PathSpacePath();
            path.k = k;
            path.v = new Vertex[k + 1];
            path.lambda = current.lambda;
            path.pRaster = pRaster;
            path.pLens = current.pLens;
            path.timeSample = current.timeSample;
            path.time = current.time;
            if (!CameraVertex(path, out Vertex cameraVertex, out RayDifferential ray))
            {
                return null;
            }
            path.v[k] = cameraVertex;

            // Trace the camera ray through the specular chain
            bool secondaryTerminated = current.lambda.SecondaryTerminated();
            Vertex last = cameraVertex;
            for (int i = k - 1; i >= j; --i)
            {
                if (!PathSpace.TraceSegment(this, camera, ray, last.stackLight, ref path.lambda, sampler, scratchBuffer, out Vertex next))
                {
                    return null;
                }
                if (path.lambda.SecondaryTerminated() != secondaryTerminated)
                {
                    return null;
                }
                next.wCam = -Normalize(ray.d);
                path.v[i] = next;
                last = next;
                if (i > j)
                {
                    // The specular lobe of the kind that the current path has there
                    if (next.type != VertexType.Surface || !SpecularDirection(next, v[i].reflected, true, sampler) ||
                        !PathSpace.SetSpecularTerms(next, sampler))
                    {
                        return null;
                    }
                    next.stackLight = PathSpace.LightSideStack(next);
                    ray = next.si.SpawnRay(next.wLight);
                }
            }

            // Connect the new vertex j to the vertex j - 1, which now sees a new direction on its camera side
            Vertex target = v[j - 1].Clone();
            if (target.type == VertexType.Surface)
            {
                // (the vertex has to be reached from the same side of its surface: its stacks of media are those of the side)
                Vector3f toNew = Normalize(last.p() - target.p());
                if ((Dot(target.si.n, target.wCam) > 0) != (Dot(target.si.n, toNew) > 0))
                {
                    return null;
                }
            }
            if (!Connect(path, target, last, sampler))
            {
                return null;
            }
            path.v[j - 1] = target;
            for (int i = 0; i < j - 1; ++i)
            {
                path.v[i] = v[i];
            }
            if (!SetCameraConnection(path) || !(v[j - 1].segL[0] > 0))
            {
                return null;
            }

            // The segment between j - 1 and j is a connection in both directions of the move; everything else that the move
            // changes is traced from the film
            weightProposed = PathSpace.UnsampledValue(path).y(path.lambda) * PathSpace.GeometricCosine(target, target.wCam) /
                             (DistanceSquared(target.p(), last.p()) * target.segL[0]);
            weightCurrent = PathSpace.UnsampledValue(current).y(current.lambda) * PathSpace.GeometricCosine(v[j - 1], v[j - 1].wCam) /
                            (DistanceSquared(v[j - 1].p(), v[j].p()) * v[j - 1].segL[0]);
            return path;
        }

        // The least and the greatest move on the film of the lens perturbation
        void LensRadii(Float scale, out Float r1, out Float r2)
        {
            Bounds2f sampleBounds = camera.GetFilm().SampleBounds();
            r2 = scale * lensPerturbationRadius *
                 std.max<Float>(sampleBounds.pMax.x - sampleBounds.pMin.x, sampleBounds.pMax.y - sampleBounds.pMin.y);
            r1 = std.min<Float>(0.1f, r2);
        }

        // The derivative of the point on the film that sees a vertex with respect to the (u, v) of the vertex, by two small moves
        bool RasterDerivative(PathSpacePath path, ManifoldVertex vertex, out ManifoldMatrix P)
        {
            P = default;
            SampledWavelengths lambda = path.lambda;
            Point2f[] raster = new Point2f[3];
            Float distance = Distance(vertex.p, path.v[path.k].p());
            Float du = 1e-3f * distance / std.max<Float>(Length(vertex.dpdu), 1e-12f), dv = 1e-3f * distance / std.max<Float>(Length(vertex.dpdv), 1e-12f);
            for (int i = 0; i < 3; ++i)
            {
                Point3f p = vertex.p + (i == 1 ? du : 0) * vertex.dpdu + (i == 2 ? dv : 0) * vertex.dpdv;
                pstd.optional<CameraWiSample> cs = camera.SampleWi(new Interaction(p, path.time, (Medium)null), new Point2f(0.5f, 0.5f), ref lambda);
                if (!cs)
                {
                    return false;
                }
                raster[i] = cs.value().pRaster;
            }
            P = new ManifoldMatrix((raster[1].x - raster[0].x) / du, (raster[2].x - raster[0].x) / dv,
                                   (raster[1].y - raster[0].y) / du, (raster[2].y - raster[0].y) / dv);
            return true;
        }

        // The coordinates of a direction on the plane that touches the sphere at h, along the two axes across h
        static Point2f PlaneCoordinates(Vector3f h, Vector3f direction)
        {
            Vector3f b1, b2;
            CoordinateSystem(h, &b1, &b2);
            Float along = Dot(direction, h);
            return new Point2f(Dot(direction, b1) / along, Dot(direction, b2) / along);
        }

        // The multi-chain perturbation (Veach and Guibas 1997, section 5.3.3): a lens perturbation that goes on where the lens
        // perturbation has to stop. The point on the film moves and the camera ray is traced through the specular vertices to
        // the first vertex that is not specular; if the vertex after it is specular, the direction that leaves it turns a
        // little from the old one and the walk goes on through that chain to the next vertex that is not specular, and so on
        // until the vertex after is not specular either: it stays, and the last new vertex is connected to it. Every turn has
        // the same density both ways, and every traced vertex has its radiance terms, so the weight is the lens perturbation's.
        PathSpacePath MultiChainPerturbation(PathSpacePath current, Sampler sampler, ScratchBuffer scratchBuffer, out double weightProposed,
                                             out double weightCurrent)
        {
            weightProposed = 0;
            weightCurrent = 0;
            int k = current.k;
            Vertex[] v = current.v;
            // The vertex that is connected: the first one that is not specular whose light-side neighbour is not specular
            int j = k - 1;
            while (j >= 1 && (v[j].delta || (j - 1 > 0 && v[j - 1].delta)))
            {
                --j;
            }
            if (j < 1)
            {
                return null;
            }
            Bounds2f sampleBounds = camera.GetFilm().SampleBounds();
            Float r2 = lensPerturbationRadius * std.max<Float>(sampleBounds.pMax.x - sampleBounds.pMin.x, sampleBounds.pMax.y - sampleBounds.pMin.y);
            Float r1 = std.min<Float>(0.1f, r2);
            Float radius = r2 * std.exp(-std.log(r2 / r1) * sampler.Get1D());
            Float phi = 2 * Pi * sampler.Get1D();
            Point2f pRaster = new Point2f(current.pRaster.x + radius * std.cos(phi), current.pRaster.y + radius * std.sin(phi));
            if (pRaster.x < sampleBounds.pMin.x || pRaster.x >= sampleBounds.pMax.x || pRaster.y < sampleBounds.pMin.y ||
                pRaster.y >= sampleBounds.pMax.y)
            {
                return null;
            }
            PathSpacePath path = new PathSpacePath();
            path.k = k;
            path.v = new Vertex[k + 1];
            path.lambda = current.lambda;
            path.pRaster = pRaster;
            path.pLens = current.pLens;
            path.timeSample = current.timeSample;
            path.time = current.time;
            if (!CameraVertex(path, out Vertex cameraVertex, out RayDifferential ray))
            {
                return null;
            }
            path.v[k] = cameraVertex;
            bool secondaryTerminated = current.lambda.SecondaryTerminated();
            Vertex last = cameraVertex;
            for (int i = k - 1; i >= j; --i)
            {
                if (!PathSpace.TraceSegment(this, camera, ray, last.stackLight, ref path.lambda, sampler, scratchBuffer, out Vertex next))
                {
                    return null;
                }
                if (path.lambda.SecondaryTerminated() != secondaryTerminated)
                {
                    return null;
                }
                next.wCam = -Normalize(ray.d);
                path.v[i] = next;
                last = next;
                if (i == j)
                {
                    break;
                }
                if (v[i].delta)
                {
                    // The specular lobe of the kind that the current path has there
                    if (next.type != VertexType.Surface || !SpecularDirection(next, v[i].reflected, true, sampler) ||
                        !PathSpace.SetSpecularTerms(next, sampler))
                    {
                        return null;
                    }
                }
                else
                {
                    // A vertex that is not specular, before a specular chain: the old direction, turned a little
                    Float theta2 = causticPerturbationAngle, theta1 = std.min<Float>(0.0001f, theta2);
                    Float theta = theta2 * std.exp(-std.log(theta2 / theta1) * sampler.Get1D());
                    Float turn = 2 * Pi * sampler.Get1D();
                    Vector3f local = new Vector3f(std.sin(theta) * std.cos(turn), std.sin(theta) * std.sin(turn), std.cos(theta));
                    next.wLight = Normalize(Frame.FromZ(v[i].wLight).FromLocal(local));
                    if (!PathSpace.SetTerms(next))
                    {
                        return null;
                    }
                }
                next.stackLight = PathSpace.LightSideStack(next);
                ray = next.GetInteraction().SpawnRay(next.wLight);
            }

            // Connect the new vertex j to the vertex j - 1, which now sees a new direction on its camera side
            Vertex target = v[j - 1].Clone();
            if (target.type == VertexType.Surface)
            {
                Vector3f toNew = Normalize(last.p() - target.p());
                if ((Dot(target.si.n, target.wCam) > 0) != (Dot(target.si.n, toNew) > 0))
                {
                    return null;
                }
            }
            if (!Connect(path, target, last, sampler))
            {
                return null;
            }
            path.v[j - 1] = target;
            for (int i = 0; i < j - 1; ++i)
            {
                path.v[i] = v[i];
            }
            if (!SetCameraConnection(path) || !(v[j - 1].segL[0] > 0))
            {
                return null;
            }
            weightProposed = PathSpace.UnsampledValue(path).y(path.lambda) * PathSpace.GeometricCosine(target, target.wCam) /
                             (DistanceSquared(target.p(), last.p()) * target.segL[0]);
            weightCurrent = PathSpace.UnsampledValue(current).y(current.lambda) * PathSpace.GeometricCosine(v[j - 1], v[j - 1].wCam) /
                            (DistanceSquared(v[j - 1].p(), v[j].p()) * v[j - 1].segL[0]);
            return path;
        }

        // The chain of surface vertices between two vertices of a path, as the vertices of a manifold: from index `start` to
        // index `end`. The specular ones hold their half vectors to their normals; the others, which have to reflect, have free
        // half vectors. The tangents of the ends are across the first and the last segment. Null if a vertex between them is not
        // on a surface, or scatters through its surface without being specular.
        static ManifoldVertex[] ChainOf(PathSpacePath path, int start, int end)
        {
            Vertex[] v = path.v;
            int step = end > start ? 1 : -1, n = Math.Abs(end - start) - 1;
            ManifoldVertex[] x = new ManifoldVertex[n + 2];
            x[0] = ManifoldVertex.Free(v[start].p(), v[start + step].p() - v[start].p());
            for (int i = 1; i <= n; ++i)
            {
                Vertex vertex = v[start + step * i];
                if (vertex.type != VertexType.Surface)
                {
                    return null;
                }
                x[i] = ManifoldVertex.Of(vertex.si);
                x[i].specular = vertex.delta;
                if (vertex.delta)
                {
                    x[i].reflection = vertex.reflected;
                    // (Vertex.eta is the index of the light side over that of the camera side)
                    x[i].eta = step < 0 ? vertex.eta : 1 / vertex.eta;
                }
                else if ((Dot(vertex.si.n, vertex.wCam) > 0) != (Dot(vertex.si.n, vertex.wLight) > 0))
                {
                    return null;
                }
            }
            x[n + 1] = ManifoldVertex.Free(v[end].p(), v[end].p() - v[end - step].p());
            return x;
        }

        // The solid angle at the vertex hi (the camera side) over the area, across the direction of arrival, at the vertex lo
        // (the light side), through the specular chain between them: 1 / r^2 with no chain. With the cosine at lo it is what a
        // path's contribution has for the two vertices when their measures are areas.
        static Float ChainMeasure(PathSpacePath path, int lo, int hi)
        {
            Vertex[] v = path.v;
            if (hi - lo == 1)
            {
                Float distanceSquared = DistanceSquared(v[lo].p(), v[hi].p());
                return distanceSquared > 0 ? 1 / distanceSquared : 0;
            }
            ManifoldVertex[] x = ChainOf(path, hi, lo);
            if (x == null)
            {
                return 0;
            }
            Vector3f d = x[1].p - x[0].p;
            Float first = LengthSquared(d);
            if (first == 0)
            {
                return 0;
            }
            return AbsDot(v[hi - 1].si.n, d / std.sqrt(first)) / first * SpecularManifold.AreaRatio(x);
        }

        // The vertices that a chain perturbation can end at, from the vertex b in a direction (step -1: toward the light): every
        // vertex that is not specular, as long as the vertices before it are on surfaces
        static void ChainEnds(PathSpacePath path, int b, int step, List<int> ends)
        {
            Vertex[] v = path.v;
            ends.Clear();
            for (int j = b + step; j >= 0 && j <= path.k; j += step)
            {
                if (j == 0 || j == path.k)
                {
                    ends.Add(j);
                    return;
                }
                if (!v[j].delta)
                {
                    ends.Add(j);
                }
                if (v[j].type != VertexType.Surface)
                {
                    return;
                }
            }
        }

        // The vertices of a chain perturbation that starts at a toward the light or toward the camera: b, the vertex that moves
        // (the next one that is not specular; a itself when nothing is traced), and the ends that the chain after b can have.
        // False if the path has none.
        static bool PerturbationPlaces(PathSpacePath path, int a, bool towardLight, bool traced, out int b, List<int> ends)
        {
            Vertex[] v = path.v;
            int k = path.k, step = towardLight ? -1 : 1;
            b = a;
            ends.Clear();
            if (a < 0 || a > k || (a > 0 && a < k && v[a].delta))
            {
                return false;
            }
            if (traced)
            {
                b = a + step;
                while (b > 0 && b < k && v[b].delta)
                {
                    b += step;
                }
                if (b <= 0 || b >= k)
                {
                    return false;
                }
            }
            ChainEnds(path, b, step, ends);
            // (with nothing traced, a chain with no free half vector has nothing to perturb)
            return ends.Count > (traced ? 0 : 1);
        }

        // The spectrum of the contribution of a path with the terms of a walk from the light for the vertices first .. last (none
        // if first > last) and the terms that the camera sees for the others; the camera's own as a connection or as a ray
        static SampledSpectrum PerturbationValue(PathSpacePath path, int first, int last, bool cameraConnection)
        {
            Vertex[] v = path.v;
            SampledSpectrum W = cameraConnection ? v[path.k].connect : v[path.k].step;
            if (first > 0)
            {
                W *= v[0].emitted;
            }
            for (int j = 1; j < path.k; ++j)
            {
                if (j < first || j > last)
                {
                    W *= v[j].connect;
                }
            }
            for (int j = first; j <= last; ++j)
            {
                W *= v[j].connectI;
            }
            for (int j = 0; j < path.k; ++j)
            {
                W *= v[j].segU * v[j].vtx;
            }
            return W;
        }

        // The weight of a path for a chain perturbation of its vertices a, b and c: its contribution over the densities of
        // what the perturbation samples, up to the symmetric densities of the turn at a and of the moves of the half vectors
        static double ChainWeight(PathSpacePath path, int a, int b, int c, bool towardLight, bool traced)
        {
            Vertex[] v = path.v;
            int lo = Math.Min(b, c), hi = Math.Max(b, c);
            bool lightForm = traced && !towardLight;
            double weight = (lightForm ? PerturbationValue(path, a, b, hi == path.k) : PerturbationValue(path, path.k + 1, path.k, hi == path.k))
                .y(path.lambda);

            // The vertices of the chain that are not specular have areas: between each two of them, the solid angle over the
            // area through the specular vertices
            int free = 0, p = lo;
            for (int q = lo + 1; q <= hi; ++q)
            {
                if (q < hi && v[q].delta)
                {
                    continue;
                }
                weight *= ChainMeasure(path, p, q);
                if (!(lightForm && p == b))
                {
                    weight *= PathSpace.GeometricCosine(v[p], v[p].wCam);
                }
                if (q < hi)
                {
                    ++free;
                }
                p = q;
            }
            if (free > 0)
            {
                // The half vectors of those vertices are what is sampled
                ManifoldVertex[] x = ChainOf(path, c, b);
                double measure = x != null ? SpecularManifold.HalfVectorMeasure(x) : 0;
                if (!(measure > 0))
                {
                    return 0;
                }
                weight /= measure;
            }
            for (int j = lo; j < hi; ++j)
            {
                if (!(v[j].segL[0] > 0))
                {
                    return 0;
                }
                weight /= v[j].segL[0];
            }
            return weight;
        }

        // The perturbations of a chain of surface vertices, on one core. Three vertices that are not specular: a, where the path
        // is perturbed; b, the next one, which moves; c, which stays. The direction that leaves a turns a little (at the camera:
        // the point on the film moves), the specular chain is traced again from a to a new b, and the chain between b and c is
        // solved for.
        //   mode 0, the manifold perturbation (Jakob and Marschner 2012, section 6): c is the next vertex that is not specular,
        //     and the specular chain between b and c is found by a walk on its manifold. Toward the light from the camera with no
        //     chain between b and c it is the lens perturbation, and toward the camera with b seen by the camera the caustic
        //     perturbation.
        //   mode 1, half vector space with a breakup (Hanika et al. 2015, section 5): c is any later vertex, and the chain between
        //     b and c keeps its half vectors but for a small move of each (Kaplanyan et al. 2014): the positions follow by
        //     Newton's method. b is the breakup vertex.
        //   mode 2, half vector space alone: nothing is traced, a stays (b = a), and the half vectors of the chain move.
        PathSpacePath ChainPerturbation(PathSpacePath current, Sampler sampler, ScratchBuffer scratchBuffer, int mode, out double weightProposed,
                                        out double weightCurrent)
        {
            weightProposed = 0;
            weightCurrent = 0;
            int k = current.k;
            Vertex[] v = current.v;
            bool traced = mode != 2;

            // Choose a and the side, then c: the perturbed path has the same choices
            List<int> ends = new List<int>();
            int places = 0, a = -1, b = 0;
            bool towardLight = false;
            for (int i = 0; i <= k; ++i)
            {
                places += (PerturbationPlaces(current, i, true, traced, out b, ends) ? 1 : 0) +
                          (PerturbationPlaces(current, i, false, traced, out b, ends) ? 1 : 0);
            }
            if (places == 0)
            {
                return null;
            }
            int chosen = Math.Min((int)(sampler.Get1D() * places), places - 1);
            for (int i = 0; i <= k && a < 0; ++i)
            {
                for (int side = 0; side < 2; ++side)
                {
                    if (PerturbationPlaces(current, i, side == 0, traced, out b, ends) && chosen-- == 0)
                    {
                        a = i;
                        towardLight = side == 0;
                        break;
                    }
                }
            }
            int step = towardLight ? -1 : 1;
            int c = ends[0];
            if (mode == 1)
            {
                c = ends[Math.Min((int)(sampler.Get1D() * ends.Count), ends.Count - 1)];
            }
            else if (mode == 2)
            {
                c = ends[1 + Math.Min((int)(sampler.Get1D() * (ends.Count - 1)), ends.Count - 2)];
            }

            PathSpacePath path = new PathSpacePath();
            path.k = k;
            path.v = new Vertex[k + 1];
            path.lambda = current.lambda;
            path.pRaster = current.pRaster;
            path.pLens = current.pLens;
            path.timeSample = current.timeSample;
            path.time = current.time;
            for (int j = 0; j <= k; ++j)
            {
                path.v[j] = v[j];
            }
            bool secondaryTerminated = current.lambda.SecondaryTerminated();
            Vertex last;
            if (!traced)
            {
                // b stays where it is, and sees a new direction
                last = v[b].Clone();
                path.v[b] = last;
            }
            else
            {
                // Perturb at a
                Vertex from;
                RayDifferential ray;
                if (a == k)
                {
                    // Move the point on the film: a distance between r1 and r2 with a density that falls as 1 / r, any direction
                    Bounds2f sampleBounds = camera.GetFilm().SampleBounds();
                    Float r2 = lensPerturbationRadius * std.max<Float>(sampleBounds.pMax.x - sampleBounds.pMin.x, sampleBounds.pMax.y - sampleBounds.pMin.y);
                    Float r1 = std.min<Float>(0.1f, r2);
                    Float radius = r2 * std.exp(-std.log(r2 / r1) * sampler.Get1D());
                    Float phi = 2 * Pi * sampler.Get1D();
                    path.pRaster = new Point2f(current.pRaster.x + radius * std.cos(phi), current.pRaster.y + radius * std.sin(phi));
                    if (path.pRaster.x < sampleBounds.pMin.x || path.pRaster.x >= sampleBounds.pMax.x || path.pRaster.y < sampleBounds.pMin.y ||
                        path.pRaster.y >= sampleBounds.pMax.y)
                    {
                        return null;
                    }
                    if (!CameraVertex(path, out from, out ray))
                    {
                        return null;
                    }
                }
                else
                {
                    // Turn the direction: an angle between theta1 and theta2 with a density that falls as 1 / theta, about the old one
                    Float theta2 = causticPerturbationAngle, theta1 = std.min<Float>(0.0001f, theta2);
                    Float theta = theta2 * std.exp(-std.log(theta2 / theta1) * sampler.Get1D());
                    Float phi = 2 * Pi * sampler.Get1D();
                    Vector3f local = new Vector3f(std.sin(theta) * std.cos(phi), std.sin(theta) * std.sin(phi), std.cos(theta));
                    Vector3f old = towardLight ? v[a].wLight : v[a].wCam;
                    Vector3f direction = Normalize(Frame.FromZ(old).FromLocal(local));
                    from = v[a].Clone();
                    if (towardLight)
                    {
                        from.wLight = direction;
                    }
                    else
                    {
                        from.wCam = direction;
                    }
                    if (from.type == VertexType.Light)
                    {
                        if (!PathSpace.SetLightTerms(from, lightSampler, path.lambda, path.time))
                        {
                            return null;
                        }
                        ray = from.ei.SpawnRay(direction);
                        from.stack = BDPT.StackAtPoint(this, camera, ray.o, path.time);
                    }
                    else
                    {
                        // (the direction stays on its side of the surface: the stack of media of the vertex is that of the side)
                        if (from.type == VertexType.Surface && (Dot(from.si.n, old) > 0) != (Dot(from.si.n, direction) > 0))
                        {
                            return null;
                        }
                        if (!PathSpace.SetTerms(from))
                        {
                            return null;
                        }
                        ray = from.GetInteraction().SpawnRay(direction);
                    }
                }
                path.v[a] = from;

                // Trace the chain of specular vertices from a to the new b
                last = from;
                for (int j = a + step; j != b + step; j += step)
                {
                    if (!PathSpace.TraceSegment(this, camera, ray, towardLight ? last.stackLight : last.stack, ref path.lambda, sampler,
                                                scratchBuffer, out Vertex next))
                    {
                        return null;
                    }
                    if (path.lambda.SecondaryTerminated() != secondaryTerminated)
                    {
                        return null;
                    }
                    if (towardLight)
                    {
                        next.wCam = -Normalize(ray.d);
                    }
                    else
                    {
                        // (the null collisions of a segment are kept at its vertex on the light side)
                        last.segU = next.segU;
                        last.segL = next.segL;
                        next.segU = new SampledSpectrum(1.0f);
                        next.segL = new SampledSpectrum(1.0f);
                        next.wLight = -Normalize(ray.d);
                        next.stackLight = next.stack;
                    }
                    path.v[j] = next;
                    last = next;
                    if (j != b)
                    {
                        if (next.type != VertexType.Surface || !SpecularDirection(next, v[j].reflected, towardLight, sampler) ||
                            !PathSpace.SetSpecularTerms(next, sampler))
                        {
                            return null;
                        }
                        if (towardLight)
                        {
                            next.stackLight = PathSpace.LightSideStack(next);
                            ray = next.si.SpawnRay(next.wLight);
                        }
                        else
                        {
                            next.stack = PathSpace.CameraSideStack(next);
                            ray = next.si.SpawnRay(next.wCam);
                        }
                    }
                }
            }

            double proposalRatio = 1;
            int n = Math.Abs(b - c) - 1;
            Vertex kept = null;
            if (c != k)
            {
                // (c has to be reached from the same side of its surface: its stacks of media are those of the side)
                kept = v[c].Clone();
                path.v[c] = kept;
            }
            if (n == 0)
            {
                // No chain between b and c: a connection
                if (kept != null && kept.type == VertexType.Surface)
                {
                    Vector3f toNew = Normalize(last.p() - kept.p());
                    if ((Dot(kept.si.n, towardLight ? kept.wCam : kept.wLight) > 0) != (Dot(kept.si.n, toNew) > 0))
                    {
                        return null;
                    }
                }
                if (towardLight)
                {
                    if (!Connect(path, kept, last, sampler))
                    {
                        return null;
                    }
                }
                else if (c == k)
                {
                    if (!CameraVertex(path, out Vertex cameraVertex, out RayDifferential unused))
                    {
                        return null;
                    }
                    path.v[k] = cameraVertex;
                    if (!Connect(path, last, cameraVertex, sampler))
                    {
                        return null;
                    }
                }
                else if (!Connect(path, last, kept, sampler))
                {
                    return null;
                }
            }
            else
            {
                // The chain between c and b, from the chain of the current path
                ManifoldVertex[] seed = ChainOf(current, c, b);
                if (seed == null)
                {
                    return null;
                }
                List<SurfaceInteraction> surfaces = new List<SurfaceInteraction>();
                List<BSDF> bsdfs = new List<BSDF>();
                SampledWavelengths lam = path.lambda;
                Interaction anchor = v[c].GetInteraction();
                SpecularManifold.Tracer tracer = (SurfaceInteraction fromSurface, Point3f origin, Vector3f direction) =>
                {
                    RayDifferential r = fromSurface != null ? fromSurface.SpawnRay(direction) : anchor.SpawnRay(direction);
                    while (true)
                    {
                        pstd.optional<ShapeIntersection> si = Intersect(new Ray(r));
                        if (!si)
                        {
                            return null;
                        }
                        SurfaceInteraction isect = si.value().intr;
                        BSDF bsdf = isect.GetBSDF(r, ref lam, camera, scratchBuffer, sampler);
                        if (!bsdf)
                        {
                            // (a boundary between media only)
                            isect.SkipIntersection(ref r, si.value().tHit);
                            continue;
                        }
                        surfaces.Add(isect);
                        bsdfs.Add(bsdf);
                        return isect;
                    }
                };
                Float scale = 1e-3f;
                for (int i = 0; i < n + 2; ++i)
                {
                    scale = std.max<Float>(scale, Length((Vector3f)seed[i].p));
                }
                SurfaceInteraction[] hits = new SurfaceInteraction[n + 2];
                ManifoldVertex[] found, back;
                Vector3f[] oldHalf = null;
                ManifoldMatrix[] stepsOld = null;
                bool differential = false;
                int adjacent = 1;
                if (mode == 0)
                {
                    // A walk on the specular manifold to the new b, and back: the move has to find the old chain again
                    found = SpecularManifold.Walk(seed, last.p(), tracer, hits, ManifoldVertex.Free(last.p(), (Vector3f)seed[n + 1].n));
                    back = found != null
                        ? SpecularManifold.Walk(found, v[b].p(), tracer, null, ManifoldVertex.Free(v[b].p(), (Vector3f)found[n + 1].n))
                        : null;
                }
                else
                {
                    // The half vectors of the chain: those of the current path, each moved by a small Gaussian step across it.
                    // With the camera at an end of the chain and nothing traced, the steps are those that move the point on
                    // the film evenly (Hanika et al. 2015, section 3); otherwise a fixed angle, the same both ways.
                    Vector3f[] targets = new Vector3f[n + 2];
                    oldHalf = new Vector3f[n + 2];
                    Float sigma = traced ? 0.1f * halfVectorPerturbationAngle : halfVectorPerturbationAngle;
                    adjacent = b == k ? n : 1;
                    ManifoldMatrix P = default;
                    differential = !traced && halfVectorPerturbationPixels > 0 && (b == k || c == k) &&
                                   RasterDerivative(current, seed[adjacent], out P);
                    if (differential)
                    {
                        stepsOld = new ManifoldMatrix[n + 2];
                        differential = SpecularManifold.HalfVectorSteps(seed, adjacent, P, halfVectorPerturbationPixels, 1e-5f,
                                                                        10 * halfVectorPerturbationAngle, stepsOld);
                    }
                    for (int i = 1; i <= n; ++i)
                    {
                        oldHalf[i] = SpecularManifold.HalfVector(seed[i - 1], seed[i], seed[i + 1]);
                        targets[i] = oldHalf[i];
                        if (!seed[i].specular)
                        {
                            Float radius = std.sqrt(-2 * std.log(std.max<Float>(1e-12f, 1 - sampler.Get1D())));
                            Float phi = 2 * Pi * sampler.Get1D();
                            Point2f t = new Point2f(radius * std.cos(phi), radius * std.sin(phi));
                            t = differential ? stepsOld[i] * t : new Point2f(sigma * t.x, sigma * t.y);
                            Vector3f b1, b2;
                            CoordinateSystem(oldHalf[i], &b1, &b2);
                            targets[i] = Normalize(oldHalf[i] + t.x * b1 + t.y * b2);
                        }
                    }
                    ManifoldVertex[] moved = (ManifoldVertex[])seed.Clone();
                    moved[n + 1] = ManifoldVertex.Free(last.p(), (Vector3f)seed[n + 1].n);
                    found = SpecularManifold.SolveHalfVectors(moved, targets, tracer, hits);
                    back = null;
                    if (found != null)
                    {
                        ManifoldVertex[] returned = (ManifoldVertex[])found.Clone();
                        returned[n + 1] = seed[n + 1];
                        back = SpecularManifold.SolveHalfVectors(returned, oldHalf, tracer, null);
                    }
                }
                if (found == null || back == null || lam.SecondaryTerminated() != secondaryTerminated)
                {
                    return null;
                }
                for (int i = 1; i <= n; ++i)
                {
                    if (Length(back[i].p - seed[i].p) > 2e-3f * scale)
                    {
                        return null;
                    }
                }
                if (differential)
                {
                    // The steps depend on the path: the density of the move back over that of the move, for the half vectors
                    // that the new chain has
                    ManifoldMatrix[] stepsNew = new ManifoldMatrix[n + 2];
                    if (!RasterDerivative(path, found[adjacent], out ManifoldMatrix PNew) ||
                        !SpecularManifold.HalfVectorSteps(found, adjacent, PNew, halfVectorPerturbationPixels, 1e-5f,
                                                          10 * halfVectorPerturbationAngle, stepsNew))
                    {
                        return null;
                    }
                    double logRatio = 0;
                    for (int i = 1; i <= n; ++i)
                    {
                        if (seed[i].specular)
                        {
                            continue;
                        }
                        Vector3f newHalf = SpecularManifold.HalfVector(found[i - 1], found[i], found[i + 1]);
                        if (!(Dot(newHalf, oldHalf[i]) > 0))
                        {
                            return null;
                        }
                        logRatio += SpecularManifold.LogStepDensity(stepsNew[i], PlaneCoordinates(newHalf, oldHalf[i])) -
                                    SpecularManifold.LogStepDensity(stepsOld[i], PlaneCoordinates(oldHalf[i], newHalf));
                    }
                    proposalRatio = Math.Exp(logRatio);
                    if (double.IsNaN(proposalRatio) || double.IsInfinity(proposalRatio))
                    {
                        return null;
                    }
                }

                // The vertices of the new chain
                int chainStep = b > c ? 1 : -1;
                SampledSpectrum one = new SampledSpectrum(1.0f);
                for (int i = 1; i <= n; ++i)
                {
                    int index = surfaces.FindIndex((SurfaceInteraction surface) => ReferenceEquals(surface, hits[i]));
                    if (index < 0)
                    {
                        return null;
                    }
                    path.v[c + chainStep * i] = new Vertex(hits[i], bsdfs[index], one);
                }
                if (c == k || b == k)
                {
                    // The point on the film that sees the chain
                    pstd.optional<CameraWiSample> cs = camera.SampleWi(path.v[k - 1].GetInteraction(), new Point2f(0.5f, 0.5f), ref path.lambda);
                    if (!cs || cs.value().pdf == 0 || !cs.value().Wi)
                    {
                        return null;
                    }
                    path.pRaster = cs.value().pRaster;
                    if (!CameraVertex(path, out Vertex cameraVertex, out RayDifferential unused))
                    {
                        return null;
                    }
                    path.v[k] = cameraVertex;
                    if (b == k)
                    {
                        last = cameraVertex;
                    }
                }

                // The directions, the terms and the segments of the chain, from b to c
                for (int j = b; j != c; j += step)
                {
                    Vertex origin = path.v[j], target = path.v[j + step];
                    Vector3f w = Normalize(target.p() - origin.p());
                    if (towardLight)
                    {
                        // (here the chain goes on toward the light)
                        origin.wLight = w;
                        target.wCam = -w;
                    }
                    else
                    {
                        origin.wCam = w;
                        target.wLight = -w;
                    }
                    if (origin.type == VertexType.Light)
                    {
                        if (!PathSpace.SetLightTerms(origin, lightSampler, path.lambda, path.time))
                        {
                            return null;
                        }
                        origin.stack = BDPT.StackAtPoint(this, camera, origin.ei.SpawnRay(w).o, path.time);
                    }
                    else if (origin.type != VertexType.Camera)
                    {
                        if (!traced && j == b && origin.type == VertexType.Surface)
                        {
                            // (b stays, and its new direction on the side of the old one: its stacks of media are those of the side)
                            if ((Dot(origin.si.n, towardLight ? v[b].wLight : v[b].wCam) > 0) != (Dot(origin.si.n, w) > 0))
                            {
                                return null;
                            }
                        }
                        if (j == b || !v[j].delta)
                        {
                            // (a vertex of the chain with a free half vector reflects, as the one it replaces)
                            if (j != b && (Dot(origin.si.n, origin.wCam) > 0) != (Dot(origin.si.n, origin.wLight) > 0))
                            {
                                return null;
                            }
                            if (!PathSpace.SetTerms(origin))
                            {
                                return null;
                            }
                        }
                        else if (!PathSpace.SetSpecularTerms(origin, sampler) || origin.reflected != v[j].reflected)
                        {
                            return null;
                        }
                        if (towardLight)
                        {
                            origin.stackLight = PathSpace.LightSideStack(origin);
                        }
                        else
                        {
                            origin.stack = PathSpace.CameraSideStack(origin);
                        }
                    }
                    if (!PathSpace.ConnectSegment(this, origin.GetInteraction(), target.GetInteraction(), towardLight ? origin.stackLight : origin.stack,
                                                  path.lambda, sampler, out SampledSpectrum T, out Float walkOverConnection,
                                                  out BoundaryStack arrival))
                    {
                        return null;
                    }
                    Vertex lightSide = towardLight ? target : origin;
                    lightSide.segU = T / walkOverConnection;
                    lightSide.segL = new SampledSpectrum(1 / walkOverConnection);
                    if (j + step != c)
                    {
                        if (towardLight)
                        {
                            target.stack = arrival;
                        }
                        else
                        {
                            target.stackLight = arrival;
                        }
                    }
                }
                if (kept != null)
                {
                    // c sees a new direction
                    Vector3f before = towardLight ? v[c].wCam : v[c].wLight, turned = towardLight ? kept.wCam : kept.wLight;
                    if (kept.type == VertexType.Surface && (Dot(kept.si.n, before) > 0) != (Dot(kept.si.n, turned) > 0))
                    {
                        return null;
                    }
                    if (kept.type == VertexType.Light ? !PathSpace.SetLightTerms(kept, lightSampler, path.lambda, path.time) : !PathSpace.SetTerms(kept))
                    {
                        return null;
                    }
                }
            }
            if (!ReferenceEquals(path.v[k], v[k]) && !SetCameraConnection(path))
            {
                return null;
            }
            weightProposed = ChainWeight(path, a, b, c, towardLight, traced) * proposalRatio;
            weightCurrent = ChainWeight(current, a, b, c, towardLight, traced);
            return path;
        }

        // The small step of primary sample space, in path space (Otsu et al. 2017, "Fusing State Spaces for Markov Chain Monte
        // Carlo Rendering"; the mutation of multiplexed MLT, Hachisuka et al. 2014). A split is chosen; the path is taken for a
        // sample of that split, its random numbers are found by the inverses of the sampling (BxDF.InvertSample()), moved a
        // little, and the path of the moved numbers is made by the same split. Whatever has no inverse here (the collisions of
        // a segment, a BxDF that samples by a random walk) is sampled anew. The density of the move is the density of the
        // split times a symmetric one, so the weight of a path is its contribution over the density that the split has for it.
        // `split` is the split to use (negative: one is chosen), returned in `used`.
        PathSpacePath SampleSpacePerturbation(PathSpacePath current, Sampler sampler, ScratchBuffer scratchBuffer, out double weightProposed,
                                              out double weightCurrent, int split, out int used)
        {
            weightProposed = 0;
            int k = current.k;
            int splits = SplitCount(k, bidirectional);
            int l = split >= 0 ? split : Math.Min((int)(sampler.Get1D() * splits), splits - 1);
            used = l;
            weightCurrent = (PathSpace.KeptWeight(current, -1, k) * PathSpace.PieceWeight(current, -1, k, l)).y(current.lambda);
            if (!(weightCurrent > 0))
            {
                return null;
            }
            PathSpacePath path = Regenerate(null, -1, k, k, l, sampler, scratchBuffer, current);
            if (path == null)
            {
                return null;
            }
            weightProposed = (PathSpace.KeptWeight(path, -1, k) * PathSpace.PieceWeight(path, -1, k, l)).y(path.lambda);
            return path;
        }

        // The step of the regeneration with a guide
        Float StepSigma()
        {
            return PathSpace.stepScale > 0 ? sampleSpaceSigma * PathSpace.stepScale : sampleSpaceSigma;
        }

        // The density with which the small step of primary sample space, guided by the path `from`, makes the path `to`, up
        // to what depends on `to` alone (for the second stage of a delayed rejection). `steps` are the guided steps of the
        // regeneration that made `to` from the path `guide`, of which `from` has the same vertices in the same places. A
        // scattering number that chooses a lobe is drawn anywhere in the interval of the lobe of `from` and has to land in the
        // interval of the lobe of `to`; the other numbers move from those of `from` to those of `to`. Negative if the paths do
        // not line up.
        double StepKernel(List<PathSpace.StepRecord> steps, PathSpacePath guide, PathSpacePath from, PathSpacePath to)
        {
            double sigma = sampleSpaceSigma;
            double kernel = 1;
            foreach (PathSpace.StepRecord step in steps)
            {
                if (step.kind == 2)
                {
                    Bounds2f sampleBounds = camera.GetFilm().SampleBounds();
                    Vector2f offsetFrom = sampleBounds.Offset(from.pRaster), offsetTo = sampleBounds.Offset(to.pRaster);
                    kernel *= PathSpace.StepDensity(offsetTo.x - offsetFrom.x, sigma) * PathSpace.StepDensity(offsetTo.y - offsetFrom.y, sigma);
                    continue;
                }
                int index = Array.IndexOf(guide.v, step.guide);
                if (index < 0 || index > from.k || Array.IndexOf(to.v, step.made) != index)
                {
                    return -1;
                }
                Vertex vFrom = from.v[index], vTo = step.made;
                if (step.kind == 1)
                {
                    Vector3f localFrom = Frame.FromZ(vFrom.ei.n).ToLocal(vFrom.wCam), localTo = Frame.FromZ(vTo.ei.n).ToLocal(vTo.wCam);
                    if ((localFrom.z < 0) != (localTo.z < 0))
                    {
                        return 0;
                    }
                    Point2f uFrom = InvertCosineHemisphereSample(new Vector3f(localFrom.x, localFrom.y, std.abs(localFrom.z)));
                    Point2f uTo = InvertCosineHemisphereSample(new Vector3f(localTo.x, localTo.y, std.abs(localTo.z)));
                    kernel *= PathSpace.StepDensity(uTo.x - uFrom.x, sigma) * PathSpace.StepDensity(uTo.y - uFrom.y, sigma);
                    continue;
                }
                if (vFrom.type == VertexType.Light || vFrom.type == VertexType.Camera)
                {
                    continue;
                }
                pstd.optional<BxDFSampleInverse> inverseFrom = step.fromCamera
                    ? vFrom.bsdf.InvertSample(vFrom.wCam, vFrom.wLight, TransportMode.Radiance)
                    : vFrom.bsdf.InvertSample(vFrom.wLight, vFrom.wCam, TransportMode.Importance);
                pstd.optional<BxDFSampleInverse> inverseTo = step.fromCamera
                    ? vTo.bsdf.InvertSample(vTo.wCam, vTo.wLight, TransportMode.Radiance)
                    : vTo.bsdf.InvertSample(vTo.wLight, vTo.wCam, TransportMode.Importance);
                if ((bool)inverseFrom != (bool)inverseTo)
                {
                    return -1;
                }
                if (!inverseFrom)
                {
                    continue;
                }
                BxDFSampleInverse f = inverseFrom.value(), t = inverseTo.value();
                kernel *= PathSpace.StepProbability(f.ucMin, f.ucMax, t.ucMin, t.ucMax, sigma);
                if (!f.anyU && !t.anyU)
                {
                    kernel *= PathSpace.StepDensity(t.u.x - f.u.x, sigma) * PathSpace.StepDensity(t.u.y - f.u.y, sigma);
                }
            }
            return kernel;
        }

        // What the sensor measures of a path. With the polarised option the Stokes vector of the emission goes through the
        // Mueller matrix of every vertex, from the light to the camera, each with its scalar value divided out (the scalar
        // contribution has those), and the sensor measures the result; the chain then follows the luminance of that, which is
        // the scalar luminance times PathSpacePath.polarisation: every weight of a mutation is a contribution over a density,
        // so the acceptance takes the ratio of the two paths' factors and nothing else changes.
        void SetMeasurement(PathSpacePath path, Sampler sampler)
        {
            SampledSpectrum value = PathSpace.UnsampledValue(path);
            path.measured = value;
            path.polarisation = 1;
            if (sensor == null)
            {
                return;
            }
            Vertex[] v = path.v;
            SampledSpectrum one = new SampledSpectrum(1.0f);
            PLight L = BDPT.EmissionState(v[0], v[0].wCam, path.lambda, one);
            for (int i = 1; i < path.k; ++i)
            {
                Vertex vertex = v[i];
                PAttenuation M;
                if (vertex.type == VertexType.Medium)
                {
                    if (!PolarisedPathIntegrator.PhaseAttenuation(vertex.mi.phase, vertex.wCam, vertex.wLight, true, out M))
                    {
                        M = PAttenuation.Lambert(one);
                    }
                }
                else if (vertex.delta)
                {
                    // (the matrix of a specular lobe comes with the weight of its sample: divided out here)
                    if (PathSpace.SpecularSample(vertex, vertex.wCam, vertex.wLight, TransportMode.Radiance, sampler, out BSDFSample bs) &&
                        PolarisedPathIntegrator.SpecularAttenuation(vertex.bsdf, vertex.si, vertex.wCam, bs, TransportMode.Radiance, out M))
                    {
                        SampledSpectrum weight = M.GetIntensity();
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            if (!(weight[c] > 0))
                            {
                                weight[c] = 1;
                            }
                        }
                        M /= weight;
                    }
                    else
                    {
                        M = PAttenuation.Lambert(one);
                    }
                }
                else
                {
                    M = PolarisedPathIntegrator.ConnectionAttenuation(vertex.bsdf, vertex.si, vertex.wCam, vertex.wLight, TransportMode.Radiance);
                }
                L = M * L;
            }
            L *= value;
            Ray cameraRay = new Ray(v[path.k].p(), v[path.k].wLight, path.time);
            path.measured = PolarisedPathIntegrator.MeasureCameraRay(camera, cameraRay, path.lambda[0], L, sensor.analyser, sensor.analyserAngle,
                                                                     sensor.measurement, -v[path.k].wLight, sensor.cameraUp);
            double scalar = value.y(path.lambda);
            // (a Stokes parameter can be negative: the chain follows its size, and the film gets its sign)
            path.polarisation = scalar > 0 ? Math.Abs(path.measured.y(path.lambda)) / scalar : 0;
        }

        // A point on a light, by the light sampler: on the surface of an area light, or the point of a light without an area.
        // Null for the lights that are not followed.
        Vertex SampleLightVertex(PathSpacePath path, Sampler sampler)
        {
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(sampler.Get1D());
            Point2f uLight = sampler.Get2D();
            if (!sampledLight)
            {
                return null;
            }
            Light sampled = sampledLight.value().light;
            DiffuseAreaLight area = sampled as DiffuseAreaLight;
            if (area != null && area.thermal == null)
            {
                if (!area.SampleArea(uLight, out Interaction y, out Float pdfArea))
                {
                    return null;
                }
                y.time = path.time;
                return LightVertex(area, y);
            }
            else if (sampled.Type() == LightType.DeltaPosition)
            {
                // A light without an area: its point (the origin of any ray that it emits)
                SampledWavelengths lam = path.lambda;
                pstd.optional<LightLeSample> les = sampled.SampleLe(uLight, new Point2f(0.5f, 0.5f), ref lam, path.time);
                if (!les)
                {
                    return null;
                }
                Vertex point = Vertex.CreateLight(new EndpointInteraction(sampled, new Ray(les.value().ray.o, les.value().ray.d, path.time)),
                                                  new SampledSpectrum(1.0f), 0);
                point.step = new SampledSpectrum(1.0f);
                return point;
            }
            return null;
        }

        // Specular next-event estimation (Zeltner et al. 2020, "Specular Manifold Sampling"; the walk of Hanika et al. 2015,
        // "Manifold Next Event Estimation"). One trial: a direction is sampled at the vertex x by its BSDF, the ray is followed
        // through the specular surfaces that it meets (each lobe taken with its probability) to a chain, and a walk on the
        // manifold of that chain moves its end to the point on the light. Null if there is no chain or the walk fails.
        const int SneeLongestChain = 6;

        ManifoldVertex[] SneeTrial(Vertex x, Point3f target, ChainTracer tracer, Sampler sampler, SurfaceInteraction[] hits)
        {
            Float uc = sampler.Get1D();
            Point2f u2 = sampler.Get2D();
            pstd.optional<BSDFSample> bs = x.type == VertexType.Surface
                ? x.bsdf.Sample_f(x.wCam, uc, u2, TransportMode.Radiance, BxDFReflTransFlags.All)
                : x.bsdf.Sample_f(x.wCam, 0.0f, u2);
            if (!bs || bs.value().pdf == 0 || bs.value().IsSpecular())
            {
                return null;
            }
            Vector3f direction = bs.value().wi;
            List<ManifoldVertex> chain = new List<ManifoldVertex>();
            chain.Add(ManifoldVertex.Free(x.p(), direction));
            SurfaceInteraction from = null;
            Point3f origin = x.p();
            while (chain.Count <= SneeLongestChain + 1)
            {
                SurfaceInteraction hit = tracer.Trace(from, origin, direction);
                if (hit == null)
                {
                    break;
                }
                pstd.optional<BSDFSample> lobe = tracer.BsdfOf(hit).Sample_f(-direction, sampler.Get1D(), sampler.Get2D(), TransportMode.Radiance,
                                                                           BxDFReflTransFlags.All);
                if (!lobe || !lobe.value().IsSpecular())
                {
                    break;
                }
                ManifoldVertex vertex = ManifoldVertex.Of(hit);
                vertex.reflection = lobe.value().IsReflection();
                // (the chain goes toward the light: the index of the light side over that of the camera side)
                vertex.eta = vertex.reflection ? 1 : lobe.value().eta;
                chain.Add(vertex);
                direction = lobe.value().wi;
                from = hit;
                origin = hit.p();
            }
            if (chain.Count < 2 || chain.Count > SneeLongestChain + 1)
            {
                return null;
            }
            ManifoldVertex end = ManifoldVertex.Free(target, direction);
            chain.Add(end);
            return SpecularManifold.Walk(chain.ToArray(), target, tracer.Trace, hits, end);
        }

        // A chain between x and the point on the light, and the inverse of the probability of having it, by the solver asked for:
        //   newton       Zeltner et al. 2020: a trial of the manifold walk, and the number of further trials up to the first that
        //                finds the same chain (an estimate without bias of the inverse probability);
        //   neldermead   Granizo-Hidalgo and Holzschuch 2024: the same with the trial of the simplex search, which needs no
        //                derivative of the surface (one specular vertex);
        //   slopespace   Loubet et al. 2020: every solution on the triangles of the scene is found, one is taken uniformly, and
        //                the inverse probability is their number (one specular vertex, on a triangle mesh);
        //   polynomial   Fan et al. 2024: the same, with every solution of a triangle from the roots of its specular
        //                polynomials (SpecularPolynomial), without a walk.
        ManifoldVertex[] SneeChain(Vertex x, Vertex light, ChainTracer tracer, Sampler sampler, SurfaceInteraction[] hits, out double inverse)
        {
            inverse = 1;
            Point3f target = light.p();
            if (sneeSolver == 4)
            {
                return BoundedChain(x, light, tracer, sampler, hits, out inverse);
            }
            if (sneeSolver >= 2)
            {
                ManifoldVertex[] one = SlopeSpaceChain(x, target, tracer, sampler, hits, out int count);
                inverse = count;
                return one;
            }
            ManifoldVertex[] chain = sneeSolver == 1 ? NelderMeadTrial(x, target, tracer, sampler, hits) : SneeTrial(x, target, tracer, sampler, hits);
            if (chain == null)
            {
                return null;
            }
            while (!SameChain(chain, sneeSolver == 1 ? NelderMeadTrial(x, target, tracer, sampler, null) : SneeTrial(x, target, tracer, sampler, null)))
            {
                if (++inverse >= sneeTrials)
                {
                    // (the only bias of the estimator: counted, and reported after the render)
                    Interlocked.Increment(ref sneeCapped);
                    break;
                }
            }
            return chain;
        }

        // The specular lobe of a kind at a surface that a chain arrives at along `direction`: its relative index (the side the
        // chain goes to over the side it comes from). False if the BSDF has no such lobe.
        static bool SpecularLobe(BSDF bsdf, Vector3f direction, bool reflection, Sampler sampler, out Float eta)
        {
            eta = 1;
            for (int attempt = 0; attempt < 64; ++attempt)
            {
                pstd.optional<BSDFSample> lobe = bsdf.Sample_f(-direction, sampler.Get1D(), sampler.Get2D(), TransportMode.Radiance,
                                                               BxDFReflTransFlags.All);
                if (!lobe || !lobe.value().IsSpecular())
                {
                    if (attempt >= 8)
                    {
                        return false;
                    }
                    continue;
                }
                if (lobe.value().IsReflection() == reflection)
                {
                    eta = reflection ? 1 : lobe.value().eta;
                    return true;
                }
            }
            return false;
        }

        // What is left of the law of reflection or refraction at a point of a surface between x and the target: the length of
        // the half vector less the normal (the objective of Granizo-Hidalgo and Holzschuch 2024, equation 8)
        static Float SpecularObjective(Point3f x, SurfaceInteraction hit, Point3f target, Float eta)
        {
            Vector3f wi = Normalize(x - hit.p()), wo = Normalize(target - hit.p());
            Vector3f h = wi + eta * wo;
            if (LengthSquared(h) == 0)
            {
                return 2;
            }
            h = Normalize(h);
            Vector3f n = (Vector3f)hit.shading.n;
            return Length(h - (Dot(h, n) < 0 ? -n : n));
        }

        // One trial of the search without derivatives (Granizo-Hidalgo and Holzschuch 2024): three points of a specular
        // surface (here: where three directions sampled at x land) are the first triangle of the Nelder-Mead method; every
        // new point of the method is put back on the surface by the ray from x through it. The search stops when the
        // objective at the best point is below 1e-4, or after 50 steps (the values of the paper). Null if it does not get there.
        ManifoldVertex[] NelderMeadTrial(Vertex x, Point3f target, ChainTracer tracer, Sampler sampler, SurfaceInteraction[] hits)
        {
            SurfaceInteraction[] point = new SurfaceInteraction[3];
            Float[] value = new Float[3];
            bool reflection = true;
            Float eta = 1;
            for (int i = 0; i < 3; ++i)
            {
                Float uc = sampler.Get1D();
                Point2f u2 = sampler.Get2D();
                pstd.optional<BSDFSample> bs = x.type == VertexType.Surface
                    ? x.bsdf.Sample_f(x.wCam, uc, u2, TransportMode.Radiance, BxDFReflTransFlags.All)
                    : x.bsdf.Sample_f(x.wCam, 0.0f, u2);
                if (!bs || bs.value().pdf == 0 || bs.value().IsSpecular())
                {
                    return null;
                }
                point[i] = tracer.Trace(null, x.p(), bs.value().wi);
                if (point[i] == null)
                {
                    return null;
                }
                if (i == 0)
                {
                    // The kind of the chain: the lobe that the first point's surface takes for its ray
                    pstd.optional<BSDFSample> lobe = tracer.BsdfOf(point[0]).Sample_f(-bs.value().wi, sampler.Get1D(), sampler.Get2D(),
                                                                                    TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (!lobe || !lobe.value().IsSpecular())
                    {
                        return null;
                    }
                    reflection = lobe.value().IsReflection();
                    eta = reflection ? 1 : lobe.value().eta;
                }
                value[i] = SpecularObjective(x.p(), point[i], target, eta);
            }
            for (int step = 0; step < 50; ++step)
            {
                // Order the triangle: best, middle, worst
                for (int i = 0; i < 2; ++i)
                {
                    for (int j = i + 1; j < 3; ++j)
                    {
                        if (value[j] < value[i])
                        {
                            Float swappedValue = value[i];
                            value[i] = value[j];
                            value[j] = swappedValue;
                            SurfaceInteraction swappedPoint = point[i];
                            point[i] = point[j];
                            point[j] = swappedPoint;
                        }
                    }
                }
                if (value[0] < 1e-4f)
                {
                    break;
                }
                Point3f centre = point[0].p() + 0.5f * (point[1].p() - point[0].p());
                Vector3f away = centre - point[2].p();
                SurfaceInteraction reflected = tracer.Trace(null, x.p(), Normalize(centre + away - x.p()));
                Float reflectedValue = reflected != null ? SpecularObjective(x.p(), reflected, target, eta) : 10;
                if (reflectedValue < value[0])
                {
                    SurfaceInteraction expanded = tracer.Trace(null, x.p(), Normalize(centre + 2 * away - x.p()));
                    Float expandedValue = expanded != null ? SpecularObjective(x.p(), expanded, target, eta) : 10;
                    if (expandedValue < reflectedValue)
                    {
                        point[2] = expanded;
                        value[2] = expandedValue;
                    }
                    else
                    {
                        point[2] = reflected;
                        value[2] = reflectedValue;
                    }
                }
                else if (reflectedValue < value[1])
                {
                    point[2] = reflected;
                    value[2] = reflectedValue;
                }
                else
                {
                    SurfaceInteraction contracted = tracer.Trace(null, x.p(), Normalize(centre - 0.5f * away - x.p()));
                    Float contractedValue = contracted != null ? SpecularObjective(x.p(), contracted, target, eta) : 10;
                    if (contractedValue < value[2])
                    {
                        point[2] = contracted;
                        value[2] = contractedValue;
                    }
                    else
                    {
                        // Shrink toward the best point
                        for (int i = 1; i < 3; ++i)
                        {
                            Point3f half = point[0].p() + 0.5f * (point[i].p() - point[0].p());
                            SurfaceInteraction shrunk = tracer.Trace(null, x.p(), Normalize(half - x.p()));
                            if (shrunk == null)
                            {
                                return null;
                            }
                            point[i] = shrunk;
                            value[i] = SpecularObjective(x.p(), shrunk, target, eta);
                        }
                    }
                }
            }
            int best = value[1] < value[0] ? (value[2] < value[1] ? 2 : 1) : (value[2] < value[0] ? 2 : 0);
            if (!(value[best] < 1e-4f))
            {
                return null;
            }
            // (the surface has to have the lobe at the point found)
            Vector3f arriving = Normalize(point[best].p() - x.p());
            if (!SpecularLobe(tracer.BsdfOf(point[best]), arriving, reflection, sampler, out Float etaFound) || std.abs(etaFound - eta) > 1e-4f * eta)
            {
                return null;
            }
            ManifoldVertex vertex = ManifoldVertex.Of(point[best]);
            vertex.reflection = reflection;
            vertex.eta = eta;
            if (hits != null)
            {
                hits[1] = point[best];
            }
            return new ManifoldVertex[] { ManifoldVertex.Free(x.p(), arriving), vertex, ManifoldVertex.Free(target, target - point[best].p()) };
        }

        // Every chain of one specular vertex on the triangles of the scene between x and the target (Loubet et al. 2020,
        // "Slope-Space Integrals for Specular Next Event Estimation", the case of a specular surface): in a triangle, the slopes
        // of the normal and of the half vector are taken to vary linearly between their values at its corners, and the point
        // where the two are equal follows from two linear equations (the paper's equation 15), one point or none a triangle.
        // That point is put on the surface by a ray from x and brought to the exact solution by the manifold walk; the
        // solutions that several triangles lead to are counted once. One of them is returned, taken uniformly, and their
        // number. The triangles are all gone over: the paper's hierarchy that prunes them is not built.
        ManifoldVertex[] SlopeSpaceChain(Vertex x, Point3f target, ChainTracer tracer, Sampler sampler, SurfaceInteraction[] hits, out int count)
        {
            count = 0;
            List<ManifoldVertex[]> solutions = new List<ManifoldVertex[]>();
            List<SurfaceInteraction> solutionHits = new List<SurfaceInteraction>();
            Point3f px = x.p();
            SurfaceInteraction[] walkHits = new SurfaceInteraction[3];
            List<Point3f> candidates = new List<Point3f>();
            Vector3f span = target - px;
            foreach (TriangleMesh mesh in Triangle.AllMeshes())
            {
                // (the index of a mesh that refracts, for the first guess: where a ray onto its first triangle arrives)
                Float meshEta = 0;
                bool probed = false;
                for (int triangle = 0; triangle < mesh.nTriangles; ++triangle)
                {
                    int i0 = mesh.vertexIndices[3 * triangle], i1 = mesh.vertexIndices[3 * triangle + 1], i2 = mesh.vertexIndices[3 * triangle + 2];
                    Point3f p0 = mesh.p[i0], p1 = mesh.p[i1], p2 = mesh.p[i2];
                    Vector3f ng = Cross(p1 - p0, p2 - p0);
                    if (LengthSquared(ng) == 0)
                    {
                        continue;
                    }
                    ng = Normalize(ng);
                    Vector3f n0 = mesh.n != null ? (Vector3f)mesh.n[i0] : ng, n1 = mesh.n != null ? (Vector3f)mesh.n[i1] : ng,
                             n2 = mesh.n != null ? (Vector3f)mesh.n[i2] : ng;
                    if (Dot(ng, n0) < 0)
                    {
                        ng = -ng;
                    }
                    Float sideX = Dot(px - p0, ng), sideTarget = Dot(target - p0, ng);
                    bool reflection = (sideX > 0) == (sideTarget > 0);
                    Float eta = 1;
                    if (!reflection)
                    {
                        if (!probed)
                        {
                            probed = true;
                            Point3f centroid = p0 + ((p1 - p0) + (p2 - p0)) / 3;
                            Vector3f toward = sideX > 0 ? -ng : ng;
                            pstd.optional<ShapeIntersection> si = Intersect(new Ray(centroid - 1e-3f * toward, toward, x.time()), 1);
                            if (si)
                            {
                                SurfaceInteraction probe = si.value().intr;
                                SampledWavelengths lambda = tracer.lambda;
                                BSDF bsdf = probe.GetBSDF(new RayDifferential(new Ray(centroid - 1e-3f * toward, toward, x.time())), ref lambda, camera,
                                                          new ScratchBuffer(), sampler);
                                if ((bool)bsdf && SpecularLobe(bsdf, toward, false, sampler, out Float found))
                                {
                                    // (kept as the index of the side under the normal over the side above it)
                                    meshEta = sideX > 0 ? found : 1 / found;
                                }
                            }
                        }
                        if (meshEta == 0)
                        {
                            continue;
                        }
                        eta = sideX > 0 ? meshEta : 1 / meshEta;
                    }

                    // The points of the triangle where a chain can be: one guess by the slopes, or every root of the polynomials
                    candidates.Clear();
                    if (sneeSolver == 3)
                    {
                        // The Bernstein coefficients of the coplanarity polynomial over the triangle, from one vector a corner
                        // (((p_i - x) x (target - x)) . n_j, which is bilinear): if they have one sign the triangle has no chain
                        Vector3f c0 = Cross(p0 - px, span), c1 = Cross(p1 - px, span), c2 = Cross(p2 - px, span);
                        Float b00 = Dot(c0, n0), b11 = Dot(c1, n1), b22 = Dot(c2, n2);
                        Float b01 = 0.5f * (Dot(c0, n1) + Dot(c1, n0)), b02 = 0.5f * (Dot(c0, n2) + Dot(c2, n0)), b12 = 0.5f * (Dot(c1, n2) + Dot(c2, n1));
                        if ((b00 > 0 && b11 > 0 && b22 > 0 && b01 > 0 && b02 > 0 && b12 > 0) ||
                            (b00 < 0 && b11 < 0 && b22 < 0 && b01 < 0 && b02 < 0 && b12 < 0))
                        {
                            continue;
                        }
                        // The cone of the half vectors of the triangle against the cone of its normals. The direction from a point of
                        // the triangle to x is a positive combination of the directions from its corners, and so for the target:
                        // the half vector is one of the nine sums of a corner's direction to x and eta times a corner's direction
                        // to the target, up to the lengths of the two combinations, which are between the cosine of the spread of
                        // the corners' directions and 1. A normal is a positive combination of the corners' normals. If the two
                        // cones (and the mirror of the first) do not meet, the triangle has no chain.
                        Vector3f a0 = Normalize(px - p0), a1 = Normalize(px - p1), a2 = Normalize(px - p2);
                        Vector3f t0 = Normalize(target - p0), t1 = Normalize(target - p1), t2 = Normalize(target - p2);
                        Vector3f hAxis = Normalize((a0 + a1 + a2) + eta * (t0 + t1 + t2)), nAxis = Normalize(n0 + n1 + n2);
                        Float hCosine = 1, hShortest = Infinity;
                        for (int ia = 0; ia < 3; ++ia)
                        {
                            Vector3f a = ia == 0 ? a0 : ia == 1 ? a1 : a2;
                            for (int it = 0; it < 3; ++it)
                            {
                                Vector3f sum = a + eta * (it == 0 ? t0 : it == 1 ? t1 : t2);
                                Float along = Dot(sum, hAxis);
                                hShortest = std.min<Float>(hShortest, along);
                                hCosine = std.min<Float>(hCosine, along / std.max<Float>(Length(sum), 1e-20f));
                            }
                        }
                        Float nCosine = std.min<Float>(Dot(Normalize(n0), nAxis), std.min<Float>(Dot(Normalize(n1), nAxis), Dot(Normalize(n2), nAxis)));
                        Float spreadA = std.min<Float>(Dot(a0, a1), std.min<Float>(Dot(a0, a2), Dot(a1, a2)));
                        Float spreadT = std.min<Float>(Dot(t0, t1), std.min<Float>(Dot(t0, t2), Dot(t1, t2)));
                        if (hShortest > 0 && spreadA > 0 && spreadT > 0 && hCosine > 0 && nCosine > 0)
                        {
                            Float slack = SafeASin(std.min<Float>(1, ((1 / spreadA - 1) + eta * (1 / spreadT - 1)) / hShortest));
                            Float reach = SafeACos(hCosine) + SafeACos(nCosine) + slack + 1e-4f;
                            Float between = SafeACos(AbsDot(hAxis, nAxis));
                            if (between > reach)
                            {
                                continue;
                            }
                        }
                        SpecularPolynomial.Roots(px, target, p0, p1, p2, n0, n1, n2, reflection, eta, candidates);
                    }
                    else
                    {
                        // The slopes of the normal and of the half vector at the corners, in the frame of the triangle
                        Vector3f e1 = Normalize(p1 - p0), e2 = Cross(ng, e1);
                        Point3f centre = p0 + ((p1 - p0) + (p2 - p0)) / 3;
                        Vector3f hCentre = Normalize(px - centre) + eta * Normalize(target - centre);
                        Float sign = Dot(hCentre, ng) < 0 ? -1 : 1;
                        Vector3f h0 = sign * (Normalize(px - p0) + eta * Normalize(target - p0)), h1 = sign * (Normalize(px - p1) + eta * Normalize(target - p1)),
                                 h2 = sign * (Normalize(px - p2) + eta * Normalize(target - p2));
                        Float hz0 = Dot(h0, ng), hz1 = Dot(h1, ng), hz2 = Dot(h2, ng), nz0 = Dot(n0, ng), nz1 = Dot(n1, ng), nz2 = Dot(n2, ng);
                        if (!(hz0 > 1e-6f && hz1 > 1e-6f && hz2 > 1e-6f && nz0 > 1e-6f && nz1 > 1e-6f && nz2 > 1e-6f))
                        {
                            continue;
                        }
                        Float d0x = Dot(n0, e1) / nz0 - Dot(h0, e1) / hz0, d0y = Dot(n0, e2) / nz0 - Dot(h0, e2) / hz0;
                        Float d1x = Dot(n1, e1) / nz1 - Dot(h1, e1) / hz1, d1y = Dot(n1, e2) / nz1 - Dot(h1, e2) / hz1;
                        Float d2x = Dot(n2, e1) / nz2 - Dot(h2, e1) / hz2, d2y = Dot(n2, e2) / nz2 - Dot(h2, e2) / hz2;
                        // d0 + b1 (d1 - d0) + b2 (d2 - d0) = 0
                        Float a00 = d1x - d0x, a01 = d2x - d0x, a10 = d1y - d0y, a11 = d2y - d0y;
                        Float determinant = a00 * a11 - a01 * a10;
                        if (determinant == 0)
                        {
                            continue;
                        }
                        Float b1 = (-d0x * a11 + d0y * a01) / determinant, b2 = (-d0y * a00 + d0x * a10) / determinant;
                        if (!(b1 >= -0.05f && b2 >= -0.05f && b1 + b2 <= 1.05f))
                        {
                            continue;
                        }
                        candidates.Add(p0 + b1 * (p1 - p0) + b2 * (p2 - p0));
                    }

                    foreach (Point3f guess in candidates)
                    {
                        // The point on the surface, as x sees it
                        Vector3f direction = Normalize(guess - px);
                        SurfaceInteraction hit = tracer.Trace(null, px, direction);
                        if (hit == null || DistanceSquared(hit.p(), guess) > 1e-4f * DistanceSquared(px, guess))
                        {
                            continue;
                        }
                        if (!SpecularLobe(tracer.BsdfOf(hit), direction, reflection, sampler, out Float etaExact))
                        {
                            continue;
                        }
                        ManifoldVertex vertex = ManifoldVertex.Of(hit);
                        vertex.reflection = reflection;
                        vertex.eta = etaExact;
                        ManifoldVertex end = ManifoldVertex.Free(target, target - hit.p());
                        ManifoldVertex[] exact;
                        SurfaceInteraction exactHit = hit;
                        if (sneeSolver == 3)
                        {
                            // (a root of the polynomials is the chain itself: no walk)
                            if (std.abs(etaExact - eta) > 1e-4f * eta || SpecularObjective(px, hit, target, etaExact) > 2e-3f)
                            {
                                continue;
                            }
                            exact = new ManifoldVertex[] { ManifoldVertex.Free(px, direction), vertex, end };
                        }
                        else
                        {
                            // (the guess is brought to the exact chain by the manifold walk)
                            exact = SpecularManifold.Walk(new ManifoldVertex[] { ManifoldVertex.Free(px, direction), vertex, end }, target, tracer.Trace,
                                                          walkHits, end);
                            exactHit = walkHits[1];
                        }
                        if (exact == null)
                        {
                            continue;
                        }
                        bool known = false;
                        foreach (ManifoldVertex[] solution in solutions)
                        {
                            known |= SameChain(solution, exact);
                        }
                        if (!known)
                        {
                            solutions.Add(exact);
                            solutionHits.Add(exactHit);
                        }
                    }
                }
            }
            count = solutions.Count;
            if (count == 0)
            {
                return null;
            }
            int chosen = Math.Min((int)(sampler.Get1D() * count), count - 1);
            if (hits != null)
            {
                hits[1] = solutionHits[chosen];
            }
            return solutions[chosen];
        }

        // Whether two chains are the same solution: the same kinds of vertices at the same places
        static bool SameChain(ManifoldVertex[] a, ManifoldVertex[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            Float scale = 1e-3f;
            for (int i = 0; i < a.Length; ++i)
            {
                scale = std.max<Float>(scale, Length((Vector3f)a[i].p));
            }
            for (int i = 1; i < a.Length - 1; ++i)
            {
                if (a[i].reflection != b[i].reflection || Length(a[i].p - b[i].p) > 2e-3f * scale)
                {
                    return false;
                }
            }
            return true;
        }

        // The paths that specular next-event estimation makes, and which no other strategy is then counted for: a specular
        // chain of at most SneeLongestChain vertices from the light to a vertex that is not specular and is not the camera
        bool InSneeClass(PathSpacePath path)
        {
            int j = 1;
            while (j < path.k && path.v[j].delta)
            {
                ++j;
            }
            if (!(j >= 2 && j < path.k))
            {
                return false;
            }
            // (the two other solvers find chains of one vertex; the triangles' solver, on a triangle mesh)
            if (sneeSolver == 0)
            {
                return j - 1 <= SneeLongestChain;
            }
            if (sneeSolver == 4)
            {
                // (the table of bounds: a point light, a specular triangle, a point of a receiving triangle)
                return j == 2 && path.v[1].si.onTriangle && path.v[2].type == VertexType.Surface && path.v[2].si.onTriangle &&
                       !PathSpace.OnAreaLight(path.v[0]) && !PathSpace.InEmittingMedium(path.v[0]);
            }
            return j == 2 && (sneeSolver == 1 || path.v[1].si.onTriangle);
        }

        // The tables of caustic bounds, one a point light (CausticBounds; Fan et al. 2025), built before the render. The kind of
        // every mesh comes from a ray onto its first triangle: whether its BSDF has a specular reflection, the index of a
        // specular transmission, or neither (then it receives).
        void BuildCausticBounds()
        {
            Dictionary<TriangleMesh, CausticBounds.MeshKind> kinds = new Dictionary<TriangleMesh, CausticBounds.MeshKind>();
            IndependentSampler sampler = SamplerOf(0, 9);
            SampledWavelengths lambda = camera.GetFilm().SampleWavelengths(0.5f);
            foreach (TriangleMesh mesh in Triangle.AllMeshes())
            {
                CausticBounds.MeshKind kind = new CausticBounds.MeshKind();
                kinds[mesh] = kind;
                if (mesh.nTriangles == 0)
                {
                    continue;
                }
                Point3f p0 = mesh.p[mesh.vertexIndices[0]], p1 = mesh.p[mesh.vertexIndices[1]], p2 = mesh.p[mesh.vertexIndices[2]];
                Vector3f ng = Cross(p1 - p0, p2 - p0);
                if (LengthSquared(ng) == 0)
                {
                    continue;
                }
                ng = Normalize(ng);
                if (mesh.n != null && Dot(ng, (Vector3f)mesh.n[mesh.vertexIndices[0]]) < 0)
                {
                    ng = -ng;
                }
                Point3f centroid = p0 + ((p1 - p0) + (p2 - p0)) / 3;
                Ray ray = new Ray(centroid + 1e-3f * ng, -ng, 0);
                pstd.optional<ShapeIntersection> si = Intersect(ray, 1);
                if (!si)
                {
                    continue;
                }
                BSDF bsdf = si.value().intr.GetBSDF(new RayDifferential(ray), ref lambda, camera, new ScratchBuffer(), sampler);
                if (!bsdf)
                {
                    continue;
                }
                kind.reflects = SpecularLobe(bsdf, -ng, true, sampler, out Float unused);
                // (the ray goes from above the normal to under it: the index of the side under over the side above)
                kind.eta = SpecularLobe(bsdf, -ng, false, sampler, out Float eta) ? eta : 0;
                kind.receives = !kind.reflects && kind.eta == 0;
            }
            causticBounds = new List<CausticBounds>();
            Timer timer = new Timer();
            foreach (Light light in lights)
            {
                if (light.Type() != LightType.DeltaPosition)
                {
                    continue;
                }
                pstd.optional<LightLeSample> les = light.SampleLe(new Point2f(0.5f, 0.5f), new Point2f(0.5f, 0.5f), ref lambda, 0);
                if (!les)
                {
                    continue;
                }
                CausticBounds bounds = new CausticBounds(les.value().ray.o, Triangle.AllMeshes(), kinds);
                causticBounds.Add(bounds);
                if (!Options.quiet)
                {
                    Console.WriteLine("caustic bounds: {0} boxes for the light at {1}, {2:F1} s", bounds.Count(), bounds.light, timer.ElapsedSeconds());
                }
            }
        }

        // A chain of one specular vertex between a point of a receiving triangle and a point light, by the table of bounds
        // (Fan et al. 2025) and the specular polynomials (Fan et al. 2024): the specular triangles whose boxes hold the point
        // are each taken with a probability that follows their weight (about `sneetuples` of them), the chains in those taken
        // are found, and one is returned, taken uniformly; the inverse of its probability is the number found over the
        // probability of its triangle.
        ManifoldVertex[] BoundedChain(Vertex x, Vertex light, ChainTracer tracer, Sampler sampler, SurfaceInteraction[] hits, out double inverse)
        {
            inverse = 0;
            if (causticBounds == null || x.type != VertexType.Surface || !x.si.onTriangle || x.si.triangleMesh == null ||
                PathSpace.OnAreaLight(light) || PathSpace.InEmittingMedium(light))
            {
                return null;
            }
            Point3f px = x.p(), target = light.p();
            CausticBounds bounds = null;
            foreach (CausticBounds candidate in causticBounds)
            {
                if (DistanceSquared(candidate.light, target) < 1e-10f * (1 + DistanceSquared(target, new Point3f(0, 0, 0))))
                {
                    bounds = candidate;
                }
            }
            if (bounds == null)
            {
                return null;
            }
            List<CausticBounds.Caster> casters = new List<CausticBounds.Caster>();
            bounds.Query(x.si.triangleMesh, x.si.triangleIndex, x.si.triangleCoordinates, casters);
            double total = 0;
            foreach (CausticBounds.Caster caster in casters)
            {
                total += caster.weight;
            }
            List<ManifoldVertex[]> solutions = new List<ManifoldVertex[]>();
            List<SurfaceInteraction> solutionHits = new List<SurfaceInteraction>();
            List<double> probabilities = new List<double>();
            List<Point3f> candidates = new List<Point3f>();
            foreach (CausticBounds.Caster caster in casters)
            {
                double probability = Math.Min(1.0, sneeTuples * caster.weight / total);
                if (sampler.Get1D() >= probability)
                {
                    continue;
                }
                TriangleMesh mesh = caster.mesh;
                int i0 = mesh.vertexIndices[3 * caster.triangle], i1 = mesh.vertexIndices[3 * caster.triangle + 1], i2 = mesh.vertexIndices[3 * caster.triangle + 2];
                Point3f p0 = mesh.p[i0], p1 = mesh.p[i1], p2 = mesh.p[i2];
                Vector3f ng = Normalize(Cross(p1 - p0, p2 - p0));
                Vector3f n0 = mesh.n != null ? (Vector3f)mesh.n[i0] : ng, n1 = mesh.n != null ? (Vector3f)mesh.n[i1] : ng,
                         n2 = mesh.n != null ? (Vector3f)mesh.n[i2] : ng;
                if (Dot(ng, n0) < 0)
                {
                    ng = -ng;
                }
                // (the index of the light's side over the side of x, from the index under the normal over above it)
                Float meshEta = bounds.KindOf(mesh).eta;
                Float eta = caster.reflection ? 1 : (Dot(px - p0, ng) > 0 ? meshEta : 1 / meshEta);
                candidates.Clear();
                SpecularPolynomial.Roots(px, target, p0, p1, p2, n0, n1, n2, caster.reflection, eta, candidates, 1e-7);
                foreach (Point3f guess in candidates)
                {
                    Vector3f direction = Normalize(guess - px);
                    SurfaceInteraction hit = tracer.Trace(null, px, direction);
                    if (hit == null || DistanceSquared(hit.p(), guess) > 1e-4f * DistanceSquared(px, guess))
                    {
                        continue;
                    }
                    if (!SpecularLobe(tracer.BsdfOf(hit), direction, caster.reflection, sampler, out Float etaExact) ||
                        std.abs(etaExact - eta) > 1e-4f * eta || SpecularObjective(px, hit, target, etaExact) > 2e-3f)
                    {
                        continue;
                    }
                    ManifoldVertex vertex = ManifoldVertex.Of(hit);
                    vertex.reflection = caster.reflection;
                    vertex.eta = etaExact;
                    solutions.Add(new ManifoldVertex[] { ManifoldVertex.Free(px, direction), vertex, ManifoldVertex.Free(target, target - hit.p()) });
                    solutionHits.Add(hit);
                    probabilities.Add(probability);
                }
            }
            if (solutions.Count == 0)
            {
                return null;
            }
            int chosen = Math.Min((int)(sampler.Get1D() * solutions.Count), solutions.Count - 1);
            if (hits != null)
            {
                hits[1] = solutionHits[chosen];
            }
            inverse = solutions.Count / probabilities[chosen];
            return solutions[chosen];
        }

        // A path by specular next-event estimation, and its weight: a walk from the camera of a random number of segments to a
        // vertex x, a point on a light, and a chain between them found by a trial. A trial finds a given chain with a
        // probability that is not known: its inverse is estimated without bias by the number of further trials up to the first
        // that finds the same chain again (the paper's algorithm 3). The other factors are the contribution over the density of
        // the walk and of the point on the light, with the solid angle at x over the area at the light through the chain.
        PathSpacePath SneePath(Sampler sampler, ScratchBuffer scratchBuffer, out double weight)
        {
            weight = 0;
            int most = MaxLength();
            if (most < 3)
            {
                return null;
            }
            int depth = Math.Min((int)(sampler.Get1D() * (most - 2)), most - 3) + 1;
            PathSpacePath path = new PathSpacePath();
            path.lambda = Options.disableWavelengthJitter ? camera.GetFilm().SampleWavelengths(0.5f)
                                                          : camera.GetFilm().SampleWavelengths(sampler.Get1D());
            path.pRaster = camera.GetFilm().SampleBounds().Lerp(sampler.Get2D());
            path.timeSample = sampler.Get1D();
            path.pLens = sampler.Get2D();
            path.time = camera.SampleTime(path.timeSample);
            if (!CameraVertex(path, out Vertex cameraVertex, out RayDifferential ray))
            {
                return null;
            }
            List<Vertex> walk = new List<Vertex>();
            walk.Add(cameraVertex);
            Vertex x = cameraVertex;
            for (int j = 0; j < depth; ++j)
            {
                if (j > 0 && !PathSpace.StepFromCamera(x, sampler, out ray))
                {
                    return null;
                }
                if (!PathSpace.TraceSegment(this, camera, ray, x.stackLight, ref path.lambda, sampler, scratchBuffer, out Vertex next))
                {
                    return null;
                }
                next.wCam = -Normalize(ray.d);
                walk.Add(next);
                x = next;
            }
            Vertex light = SampleLightVertex(path, sampler);
            if (light == null)
            {
                return null;
            }

            // The chain, and the inverse of the probability of finding it
            ChainTracer tracer = new ChainTracer(this, camera, path.lambda, scratchBuffer, sampler, x.GetInteraction());
            SurfaceInteraction[] hits = new SurfaceInteraction[SneeLongestChain + 2];
            ManifoldVertex[] chain = SneeChain(x, light, tracer, sampler, hits, out double trials);
            if (chain == null)
            {
                return null;
            }
            int m = chain.Length - 2, k = depth + m + 1;
            if (k > most)
            {
                return null;
            }
            if (tracer.lambda.SecondaryTerminated() != path.lambda.SecondaryTerminated())
            {
                return null;
            }

            // The path: the light, the chain, x, the walk back to the camera
            path.k = k;
            path.v = new Vertex[k + 1];
            for (int j = 0; j <= depth; ++j)
            {
                path.v[k - j] = walk[j];
            }
            SampledSpectrum one = new SampledSpectrum(1.0f);
            for (int i = 1; i <= m; ++i)
            {
                path.v[m + 1 - i] = new Vertex(hits[i], tracer.BsdfOf(hits[i]), one);
            }
            path.v[0] = light;
            for (int j = m + 1; j >= 1; --j)
            {
                Vertex origin = path.v[j], target = path.v[j - 1];
                Vector3f w = Normalize(target.p() - origin.p());
                origin.wLight = w;
                target.wCam = -w;
                if (j == m + 1)
                {
                    if (!PathSpace.SetTerms(origin))
                    {
                        return null;
                    }
                }
                else if (!PathSpace.SetSpecularTerms(origin, sampler) || origin.reflected != chain[m + 1 - j].reflection)
                {
                    return null;
                }
                origin.stackLight = PathSpace.LightSideStack(origin);
                if (!PathSpace.ConnectSegment(this, origin.GetInteraction(), target.GetInteraction(), origin.stackLight, path.lambda, sampler,
                                              out SampledSpectrum T, out Float walkOverConnection, out BoundaryStack arrival))
                {
                    return null;
                }
                target.segU = T / walkOverConnection;
                target.segL = new SampledSpectrum(1 / walkOverConnection);
                if (j - 1 >= 1)
                {
                    target.stack = arrival;
                }
            }
            if (!PathSpace.SetLightTerms(light, lightSampler, path.lambda, path.time) || !SetCameraConnection(path) || !(light.pdfLight > 0))
            {
                return null;
            }
            Vertex[] v = path.v;
            SampledSpectrum W = v[0].emitted;
            for (int i = 1; i <= m + 1; ++i)
            {
                W *= v[i].connect;
            }
            for (int i = m + 2; i <= k; ++i)
            {
                W *= v[i].step;
            }
            for (int i = 0; i < k; ++i)
            {
                W *= v[i].segU * v[i].vtx;
            }
            weight = W.y(path.lambda) * PathSpace.GeometricCosine(v[0], v[0].wCam) * ChainMeasure(path, 0, m + 1) / v[0].pdfLight * trials * (most - 2);
            for (int i = 0; i <= m; ++i)
            {
                if (!(v[i].segL[0] > 0))
                {
                    return null;
                }
                weight /= v[i].segL[0];
            }
            return weight > 0 && !double.IsInfinity(weight) ? path : null;
        }

        IndependentSampler SamplerOf(long index, int stream)
        {
            IndependentSampler sampler = new IndependentSampler(1, stream + 4 * seed);
            sampler.StartPixelSample(new Point2i((int)(index & 0xffff), (int)(index >> 16)), 0, 0);
            return sampler;
        }

        // The bootstrap sample of an index: a new path of a random length and split, and its weight c / Q
        PathSpacePath BootstrapPath(long index, ScratchBuffer scratchBuffer, out double weight, bool allSplits, int stream = 1)
        {
            IndependentSampler sampler = SamplerOf(index, stream);
            int most = MaxLength();
            int n = Math.Min((int)(sampler.Get1D() * most), most - 1) + 1;
            int splits = SplitCount(n, allSplits);
            int l = Math.Min((int)(sampler.Get1D() * splits), splits - 1);
            weight = 0;
            PathSpacePath path;
            if (specularNee)
            {
                // Half of the bootstrap paths are made by specular next-event estimation, and the paths that it makes are then
                // counted for it alone: its density is not known, so it cannot share them with the other strategies
                if (sampler.Get1D() < 0.5f)
                {
                    path = SneePath(sampler, scratchBuffer, out weight);
                    if (path == null)
                    {
                        weight = 0;
                        return null;
                    }
                    SetMeasurement(path, sampler);
                    weight *= 2 * path.polarisation;
                    if (!(weight > 0))
                    {
                        return null;
                    }
                    return path;
                }
            }
            path = Regenerate(null, -1, n, n, l, sampler, scratchBuffer);
            if (path == null || (specularNee && InSneeClass(path)))
            {
                return null;
            }
            double inverse = InverseWeight(path, -1, path.k, allSplits);
            if (!(inverse > 0))
            {
                return null;
            }
            SetMeasurement(path, sampler);
            weight = (specularNee ? 2 : 1) * most / inverse * path.polarisation;
            if (!(weight > 0))
            {
                return null;
            }
            return path;
        }

        // One mutation of a chain: the next state. The proposal and its acceptance probability are returned for the splats.
        PathSpacePath Mutate(PathSpacePath current, Sampler sampler, ScratchBuffer buf, out PathSpacePath proposed, out Float accept)
        {
            int k = current.k;
            accept = 0;
            proposed = null;
            double ratio = 0;
            // What the second stage of a delayed rejection needs of the first: its kind (0 none, 1 lens, 2 caustic,
            // 3 regeneration), the weight of its proposal (zero if it has no value), and what it drew
            int stage = 0;
            double weightFirst = 0;
            Point2f movedFirst = default;
            Vector3f wasFirst = default, turnedFirst = default;
            int aFirst = 0, dFirst = 0, nFirst = 0, splitFirst = 0;
            List<PathSpace.StepRecord> stepsFirst = null;
            Float uMutation = sampler.Get1D();
            if (uMutation < lensPerturbationProbability + causticPerturbationProbability + manifoldPerturbationProbability +
                            halfVectorPerturbationProbability + sampleSpacePerturbationProbability + multiChainPerturbationProbability)
            {
                // A perturbation: its move has the same density both ways
                double weightProposed, weightCurrent;
                Float uPerturbation = uMutation;
                if (uPerturbation < lensPerturbationProbability)
                {
                    proposed = LensPerturbation(current, sampler, buf, out weightProposed, out weightCurrent, 1, out bool placed, out movedFirst);
                    stage = placed ? 1 : 0;
                }
                else if ((uPerturbation -= lensPerturbationProbability) < causticPerturbationProbability)
                {
                    proposed = CausticPerturbation(current, sampler, buf, out weightProposed, out weightCurrent, 1, out bool placed, out wasFirst,
                                                   out turnedFirst);
                    stage = placed ? 2 : 0;
                }
                else if ((uPerturbation -= causticPerturbationProbability) < manifoldPerturbationProbability)
                {
                    proposed = ChainPerturbation(current, sampler, buf, 0, out weightProposed, out weightCurrent);
                }
                else if ((uPerturbation -= manifoldPerturbationProbability) < halfVectorPerturbationProbability)
                {
                    proposed = ChainPerturbation(current, sampler, buf, sampler.Get1D() < 0.5f ? 1 : 2, out weightProposed, out weightCurrent);
                }
                else if ((uPerturbation -= halfVectorPerturbationProbability) < multiChainPerturbationProbability)
                {
                    proposed = MultiChainPerturbation(current, sampler, buf, out weightProposed, out weightCurrent);
                }
                else
                {
                    if (delayedRejection)
                    {
                        PathSpace.stepRecord = new List<PathSpace.StepRecord>();
                    }
                    proposed = SampleSpacePerturbation(current, sampler, buf, out weightProposed, out weightCurrent, -1, out splitFirst);
                    stepsFirst = PathSpace.stepRecord;
                    PathSpace.stepRecord = null;
                    // (a second stage only after a whole first proposal: what a broken one drew is not all known)
                    stage = proposed != null ? 4 : 0;
                }
                if (proposed != null && weightProposed > 0 && weightCurrent > 0 && !double.IsInfinity(weightProposed))
                {
                    ratio = weightProposed / weightCurrent;
                    weightFirst = weightProposed;
                }
            }
            else
            {
                SampleDelete(k, sampler, out int a, out int d);
                int kept = k - d;
                int n = SampleAdd(d, a, kept, sampler.Get1D());
                if (n >= 0)
                {
                    stage = 3;
                    aFirst = a;
                    dFirst = d;
                    nFirst = n;
                    int splits = SplitCount(n, bidirectional);
                    int l = Math.Min((int)(sampler.Get1D() * splits), splits - 1);
                    proposed = Regenerate(current, a, d, n, l, sampler, buf);
                    if (proposed != null)
                    {
                        double inverseProposed = InverseWeight(proposed, a, n, bidirectional);
                        double inverseCurrent = InverseWeight(current, a, d, bidirectional);
                        if (inverseProposed > 0 && inverseCurrent > 0)
                        {
                            double forward = DeleteProbability(a, d, k) * AddProbability(n, d, a, kept);
                            double reverse = DeleteProbability(a, n, proposed.k) * AddProbability(d, n, a, kept);
                            ratio = (inverseCurrent * reverse) / (inverseProposed * forward);
                            // (the inverse weight of the proposal, for the second stage)
                            weightFirst = inverseProposed;
                        }
                    }
                }
            }
            bool valued = false;
            if (ratio > 0 && PathSpace.UnsampledValue(proposed).y(proposed.lambda) > 0)
            {
                // (the sensor's share of the two paths: 1 without the polarised option)
                SetMeasurement(proposed, sampler);
                accept = (Float)Math.Min(1.0, ratio * proposed.polarisation / current.polarisation);
                if (!(accept > 0))
                {
                    accept = 0;
                }
                valued = proposed.polarisation > 0;
            }
            if (!delayedRejection)
            {
                return sampler.Get1D() < accept ? proposed : current;
            }

            // Delayed rejection (Tierney and Mira 1999; in rendering, Rioux-Lavoie et al. 2020; not in pbrt-v4). The splats of
            // the caller get what the step does given the outcome of the first stage: the first proposal if it is accepted,
            // and otherwise the second proposal with its own acceptance.
            if (sampler.Get1D() < accept)
            {
                accept = 1;
                return proposed;
            }
            Float acceptFirst = accept;
            PathSpacePath first = valued ? proposed : null;
            PathSpacePath whole = proposed;
            accept = 0;
            proposed = null;
            if (stage == 0)
            {
                return current;
            }
            double ratioSecond = 0;
            PathSpacePath second = null;
            if (stage == 1)
            {
                // A smaller move on the film. The first proposal is the lens perturbation of the second one as well, with the
                // move from the second point to the first: the densities of that move, as 1 / r^2 between the radii.
                second = LensPerturbation(current, sampler, buf, out double weightSecond, out double weightCurrent, delayedRejectionScale,
                                          out bool placed, out Point2f movedSecond);
                if (second != null && weightSecond > 0 && weightCurrent > 0 && !double.IsInfinity(weightSecond) &&
                    PathSpace.UnsampledValue(second).y(second.lambda) > 0)
                {
                    SetMeasurement(second, sampler);
                    LensRadii(1, out Float r1, out Float r2);
                    Float there = Distance(current.pRaster, movedFirst), back = Distance(movedSecond, movedFirst);
                    if (second.polarisation > 0 && back >= r1 && back <= r2)
                    {
                        double target = weightSecond * second.polarisation;
                        double firstFromSecond = first != null ? Math.Min(1.0, weightFirst * first.polarisation / target) : 0;
                        ratioSecond = target / (weightCurrent * current.polarisation) * Sqr((double)there / back) * (1 - firstFromSecond) /
                                      (1 - acceptFirst);
                    }
                }
            }
            else if (stage == 2)
            {
                // A smaller turn, likewise: the density of a turn by theta is as 1 / (theta sin(theta)) between the angles
                second = CausticPerturbation(current, sampler, buf, out double weightSecond, out double weightCurrent, delayedRejectionScale,
                                             out bool placed, out Vector3f was, out Vector3f turnedSecond);
                if (second != null && weightSecond > 0 && weightCurrent > 0 && !double.IsInfinity(weightSecond) &&
                    PathSpace.UnsampledValue(second).y(second.lambda) > 0)
                {
                    SetMeasurement(second, sampler);
                    Float theta2 = causticPerturbationAngle, theta1 = std.min<Float>(0.0001f, theta2);
                    Float there = AngleBetween(wasFirst, turnedFirst), back = AngleBetween(turnedSecond, turnedFirst);
                    if (second.polarisation > 0 && back >= theta1 && back <= theta2)
                    {
                        double target = weightSecond * second.polarisation;
                        double firstFromSecond = first != null ? Math.Min(1.0, weightFirst * first.polarisation / target) : 0;
                        ratioSecond = target / (weightCurrent * current.polarisation) * ((double)there * std.sin(there)) /
                                      ((double)back * std.sin(back)) * (1 - firstFromSecond) / (1 - acceptFirst);
                    }
                }
            }
            else if (stage == 4)
            {
                // A smaller step of the numbers, by the same split. The first proposal is the step from the second one as well,
                // by the numbers of the second one: the densities of the two steps to it, of the first size.
                PathSpace.stepScale = delayedRejectionScale;
                second = SampleSpacePerturbation(current, sampler, buf, out double weightSecond, out double weightCurrent, splitFirst, out int used);
                PathSpace.stepScale = 0;
                if (second != null && weightSecond > 0 && weightCurrent > 0 && !double.IsInfinity(weightSecond) &&
                    PathSpace.UnsampledValue(second).y(second.lambda) > 0)
                {
                    SetMeasurement(second, sampler);
                    double there = StepKernel(stepsFirst, current, current, whole), back = StepKernel(stepsFirst, current, second, whole);
                    if (second.polarisation > 0 && there > 0 && back > 0)
                    {
                        double target = weightSecond * second.polarisation;
                        double firstFromSecond = first != null ? Math.Min(1.0, weightFirst * first.polarisation / target) : 0;
                        ratioSecond = target / (weightCurrent * current.polarisation) * back / there * (1 - firstFromSecond) / (1 - acceptFirst);
                    }
                }
            }
            else
            {
                // The same vertices are made again. The first proposal is a regeneration of the second one as well (they keep
                // the same vertices), and its density cancels but for the discrete choices.
                int a = aFirst, d = dFirst, kept = k - d;
                int n = SampleAdd(d, a, kept, sampler.Get1D());
                if (n >= 0)
                {
                    int splits = SplitCount(n, bidirectional);
                    int l = Math.Min((int)(sampler.Get1D() * splits), splits - 1);
                    second = Regenerate(current, a, d, n, l, sampler, buf);
                    if (second != null && PathSpace.UnsampledValue(second).y(second.lambda) > 0)
                    {
                        double inverseSecond = InverseWeight(second, a, n, bidirectional);
                        double inverseCurrent = InverseWeight(current, a, d, bidirectional);
                        SetMeasurement(second, sampler);
                        if (inverseSecond > 0 && inverseCurrent > 0 && second.polarisation > 0)
                        {
                            inverseSecond /= second.polarisation;
                            inverseCurrent /= current.polarisation;
                            double deleteCurrent = DeleteProbability(a, d, k), deleteFirst = DeleteProbability(a, nFirst, kept + nFirst);
                            double deleteSecond = DeleteProbability(a, n, kept + n);
                            double firstFromSecond = 0;
                            if (first != null)
                            {
                                double inverseFirst = weightFirst / first.polarisation;
                                double denominator = inverseFirst * deleteSecond * AddProbability(nFirst, n, a, kept);
                                firstFromSecond = denominator > 0
                                    ? Math.Min(1.0, inverseSecond * deleteFirst * AddProbability(n, nFirst, a, kept) / denominator) : 1;
                            }
                            double forward = deleteCurrent * AddProbability(nFirst, d, a, kept) * AddProbability(n, d, a, kept);
                            double reverse = deleteSecond * AddProbability(nFirst, n, a, kept) * AddProbability(d, n, a, kept);
                            ratioSecond = forward > 0 ? inverseCurrent * reverse / (inverseSecond * forward) * (1 - firstFromSecond) / (1 - acceptFirst)
                                                      : 0;
                        }
                    }
                }
            }
            if (ratioSecond > 0 && !double.IsNaN(ratioSecond))
            {
                proposed = second;
                accept = (Float)Math.Min(1.0, ratioSecond);
            }
            return sampler.Get1D() < accept ? proposed : current;
        }

        // The classes of the test of the chain: the length of a path, the number of its vertices in media, and where it falls
        // on the film
        const int DiagnosisClasses = 7 * 3 + 12;

        static int LengthClass(PathSpacePath path)
        {
            int inMedia = 0;
            for (int i = 1; i < path.k; ++i)
            {
                if (path.v[i].type == VertexType.Medium)
                {
                    ++inMedia;
                }
            }
            return Math.Min(path.k, 6) * 3 + Math.Min(inMedia, 2);
        }

        int FilmClass(PathSpacePath path)
        {
            Bounds2f bounds = camera.GetFilm().SampleBounds();
            int x = Math.Min(3, Math.Max(0, (int)(4 * (path.pRaster.x - bounds.pMin.x) / (bounds.pMax.x - bounds.pMin.x))));
            int y = Math.Min(2, Math.Max(0, (int)(3 * (path.pRaster.y - bounds.pMin.y) / (bounds.pMax.y - bounds.pMin.y))));
            return 21 + 4 * y + x;
        }

        // "diagnose" n: a test of the chain alone. Paths drawn with a known density and weighted by c / density have the
        // distribution of the chain's target, and a chain that keeps that target leaves the weighted share of every class of
        // paths unchanged after any number of mutations. The shares before and after n mutations are printed with the standard
        // error of their difference (the same paths, so the weights cancel in it).
        void Diagnose(int steps)
        {
            ThreadLocal<ScratchBuffer> threadScratchBuffers = new ThreadLocal<ScratchBuffer>(() => new ScratchBuffer());
            double[] before = new double[DiagnosisClasses], after = new double[DiagnosisClasses];
            double[] difference = new double[DiagnosisClasses], differenceSquared = new double[DiagnosisClasses];
            double total = 0, totalSquared = 0;
            object guard = new object();
            long count = nBootstrap;
            ProgressReporter progress = new ProgressReporter(count, "Testing the chain", Options.quiet);
            ParallelFor(0, count, (long start, long end) =>
            {
                ScratchBuffer buf = threadScratchBuffers.Get();
                double[] localBefore = new double[DiagnosisClasses], localAfter = new double[DiagnosisClasses];
                double[] localDifference = new double[DiagnosisClasses], localSquared = new double[DiagnosisClasses];
                double localTotal = 0, localTotalSquared = 0;
                for (long i = start; i < end; ++i)
                {
                    PathSpacePath path = BootstrapPath(i, buf, out double weight, bidirectional);
                    if (path == null || !(weight > 0))
                    {
                        continue;
                    }
                    IndependentSampler sampler = SamplerOf(i, 3);
                    int length0 = LengthClass(path), film0 = FilmClass(path);
                    for (int j = 0; j < steps; ++j)
                    {
                        path = Mutate(path, sampler, buf, out PathSpacePath proposed, out Float accept);
                    }
                    int length1 = LengthClass(path), film1 = FilmClass(path);
                    localTotal += weight;
                    localTotalSquared += weight * weight;
                    localBefore[length0] += weight;
                    localBefore[film0] += weight;
                    localAfter[length1] += weight;
                    localAfter[film1] += weight;
                    if (length0 != length1)
                    {
                        localDifference[length1] += weight;
                        localDifference[length0] -= weight;
                        localSquared[length1] += weight * weight;
                        localSquared[length0] += weight * weight;
                    }
                    if (film0 != film1)
                    {
                        localDifference[film1] += weight;
                        localDifference[film0] -= weight;
                        localSquared[film1] += weight * weight;
                        localSquared[film0] += weight * weight;
                    }
                }
                lock (guard)
                {
                    total += localTotal;
                    totalSquared += localTotalSquared;
                    for (int c = 0; c < DiagnosisClasses; ++c)
                    {
                        before[c] += localBefore[c];
                        after[c] += localAfter[c];
                        difference[c] += localDifference[c];
                        differenceSquared[c] += localSquared[c];
                    }
                }
                progress.Update(end - start);
            });
            progress.Done();
            Console.WriteLine("diagnose: {0} paths, {1} mutations each; integral of the luminance {2:G6}; effective paths {3:F0}", count, steps,
                              total / count, total * total / totalSquared);
            Console.WriteLine("class                          share before   share after   change (%)   standard errors");
            for (int c = 0; c < DiagnosisClasses; ++c)
            {
                if (!(before[c] > 0) && !(after[c] > 0))
                {
                    continue;
                }
                string name = c < 21 ? string.Format("length {0}, in media {1}{2}", c / 3, c % 3, c % 3 == 2 ? "+" : "")
                                     : string.Format("film row {0} column {1}", (c - 21) / 4, (c - 21) % 4);
                double error = Math.Sqrt(differenceSquared[c]);
                Console.WriteLine("{0,-30} {1,12:F5} {2,13:F5} {3,12:F2} {4,17:F1}", name, before[c] / total, after[c] / total,
                                  100 * difference[c] / Math.Max(before[c], 1e-300), error > 0 ? difference[c] / error : 0);
            }
        }

        // "diagnose" -1: a test of the weights of the splits. A split that can make every path of a length is an estimator of
        // the light that the paths of that length carry on its own, so the mean of its weight is the same for every split of a
        // length (up to the paths that a split cannot make: a specular vertex at its connection). The means are printed with
        // their standard errors.
        void DiagnoseSplits()
        {
            ThreadLocal<ScratchBuffer> threadScratchBuffers = new ThreadLocal<ScratchBuffer>(() => new ScratchBuffer());
            int most = MaxLength();
            double[,] sums = new double[most + 1, most + 1], squares = new double[most + 1, most + 1];
            long[,] counts = new long[most + 1, most + 1];
            object guard = new object();
            ParallelFor(0, nBootstrap, (long start, long end) =>
            {
                ScratchBuffer buf = threadScratchBuffers.Get();
                double[,] localSums = new double[most + 1, most + 1], localSquares = new double[most + 1, most + 1];
                long[,] localCounts = new long[most + 1, most + 1];
                for (long i = start; i < end; ++i)
                {
                    IndependentSampler sampler = SamplerOf(i, 1);
                    int n = Math.Min((int)(sampler.Get1D() * most), most - 1) + 1;
                    int splits = SplitCount(n, bidirectional);
                    int l = Math.Min((int)(sampler.Get1D() * splits), splits - 1);
                    ++localCounts[n, l];
                    PathSpacePath path = Regenerate(null, -1, n, n, l, sampler, buf);
                    if (path == null)
                    {
                        continue;
                    }
                    Float y = PathSpace.PieceWeight(path, -1, n, l).y(path.lambda);
                    if (y > 0 && !Float.IsInfinity(y))
                    {
                        localSums[n, l] += y;
                        localSquares[n, l] += (double)y * y;
                    }
                }
                lock (guard)
                {
                    for (int n = 0; n <= most; ++n)
                    {
                        for (int l = 0; l <= most; ++l)
                        {
                            sums[n, l] += localSums[n, l];
                            squares[n, l] += localSquares[n, l];
                            counts[n, l] += localCounts[n, l];
                        }
                    }
                }
            });
            Console.WriteLine("diagnose splits: mean weight of every split (vertices from the light side) of every length, +- standard error");
            for (int n = 1; n <= most; ++n)
            {
                string line = string.Format("length {0}:", n);
                for (int l = 0; l < SplitCount(n, bidirectional); ++l)
                {
                    double mean = sums[n, l] / Math.Max(1, counts[n, l]);
                    double error = Math.Sqrt(Math.Max(0.0, squares[n, l] / Math.Max(1, counts[n, l]) - mean * mean) / Math.Max(1, counts[n, l]));
                    line += string.Format("  l={0} {1:E4} +- {2:E1}", l, mean, error);
                }
                Console.WriteLine(line);
            }
        }

        public override void Render()
        {
            if (diagnose < 0)
            {
                DiagnoseSplits();
                return;
            }
            if (diagnose > 0)
            {
                Diagnose(diagnose);
                return;
            }
            Film film = camera.GetFilm();
            Timer timer = new Timer();
            if (specularNee && sneeSolver == 4)
            {
                BuildCausticBounds();
            }
            ThreadLocal<ScratchBuffer> threadScratchBuffers = new ThreadLocal<ScratchBuffer>(() => new ScratchBuffer());

            // Generate bootstrap samples
            Float[] bootstrapWeights = new Float[nBootstrap];
            ProgressReporter progress = new ProgressReporter(nBootstrap, "Generating bootstrap paths", Options.quiet);
            ParallelFor(0, nBootstrap, (long start, long end) =>
            {
                ScratchBuffer buf = threadScratchBuffers.Get();
                for (long i = start; i < end; ++i)
                {
                    BootstrapPath(i, buf, out double weight, bidirectional);
                    bootstrapWeights[i] = (Float)weight;
                }
                progress.Update(end - start);
            });
            progress.Done();
            double sum = 0;
            for (int i = 0; i < nBootstrap; ++i)
            {
                sum += bootstrapWeights[i];
            }
            Float ownIntegral = (Float)(sum / nBootstrap);
            if (!(ownIntegral > 0))
            {
                ErrorExit("No light carrying paths found during bootstrap sampling! Are you trying to render a black image?");
            }
            AliasTable bootstrapTable = new AliasTable(new pstd.span<Float>(bootstrapWeights));

            // The integral of the luminance over the paths, b: the mean weight of bootstrap paths made with every split, which
            // has far less noise than the weights of the two strategies of a path tracer. (The estimator of "mlt" is not used:
            // its connections to the camera are on the scale of the image's pixels and its camera rays on that of the area the
            // pixel filter samples from.)
            int nIntegral = integralSamples;
            double[] integralWeights = new double[nIntegral];
            ProgressReporter progressIntegral = new ProgressReporter(nIntegral, "Estimating the integral of the luminance", Options.quiet);
            ParallelFor(0, nIntegral, (long start, long end) =>
            {
                ScratchBuffer buf = threadScratchBuffers.Get();
                for (long i = start; i < end; ++i)
                {
                    BootstrapPath(i, buf, out double weight, true, 0);
                    integralWeights[i] = weight;
                }
                progressIntegral.Update(end - start);
            });
            progressIntegral.Done();
            double integral = 0;
            for (int i = 0; i < nIntegral; ++i)
            {
                integral += integralWeights[i];
            }
            Float b = (Float)(integral / nIntegral);

            long nTotalMutations = (long)mutationsPerPixel * (long)film.SampleBounds().Area();
            if (jumpRestore)
            {
                // Jump Restore in path space (Holl, Singh and Seidel 2025, "Jump Restore Light Transport"). A tour starts from a
                // new path of the bootstrap sampler (the global dynamics, of density u) and follows the chain of the mutations
                // (the local dynamics, which keep the target p); a state is held for an exponential time of rate 1, and the tour
                // is killed at the rate k = c u / p, which is c over the weight that the bootstrap sampler has for the state
                // (the paper's equation 26). The image is c / (number of tours) times the integral of the measurement over the
                // tours (equation 32): no seeds are chosen and no integral b scales the image; b only sets c so that a tour
                // lasts `tourlength` steps on average. The time a state is held before either clock rings is taken at its
                // expectation, 1 / (1 + k), which is exact.
                long nTours = Math.Max(1, (long)(nTotalMutations / tourLength));
                double rate = b / tourLength;
                long steps = 0, accepted = 0, longest = 0;
                ProgressReporter progressTours = new ProgressReporter(nTours, "Rendering", Options.quiet);
                ParallelFor(0, nTours, (long start, long end) =>
                {
                    ScratchBuffer buf = threadScratchBuffers.Get();
                    long localSteps = 0, localAccepted = 0, localLongest = 0;
                    for (long tour = start; tour < end; ++tour)
                    {
                        PathSpacePath current = BootstrapPath(tour, buf, out double weight, bidirectional, 6);
                        if (current == null)
                        {
                            // (a sample of the global dynamics that is no path: a tour of no length, counted)
                            continue;
                        }
                        IndependentSampler sampler = SamplerOf(tour, 7);
                        long length = 0;
                        while (true)
                        {
                            // The killing rate of the state: c over its weight as a sample of the global dynamics
                            double inverse = InverseWeight(current, -1, current.k, bidirectional);
                            double stateWeight = inverse > 0 ? MaxLength() / inverse * current.polarisation : double.PositiveInfinity;
                            double killing = rate / stateWeight;
                            SampledSpectrum L = current.measured;
                            L /= std.abs(L.y(current.lambda));
                            film.AddSplat(current.pRaster, L * (Float)(rate / (1 + killing) * nTotalMutations / nTours), current.lambda);
                            if (sampler.Get1D() * (1 + killing) < killing)
                            {
                                break;
                            }
                            PathSpacePath next = Mutate(current, sampler, buf, out PathSpacePath proposed, out Float accept);
                            ++length;
                            if (next != current)
                            {
                                current = next;
                                ++localAccepted;
                            }
                        }
                        localSteps += length;
                        localLongest = Math.Max(localLongest, length);
                    }
                    Interlocked.Add(ref steps, localSteps);
                    Interlocked.Add(ref accepted, localAccepted);
                    lock (threadScratchBuffers)
                    {
                        longest = Math.Max(longest, localLongest);
                    }
                    progressTours.Update(end - start);
                });
                progressTours.Done();
                if (!Options.quiet)
                {
                    Console.WriteLine("{0}: {1} tours, {2:F1} steps a tour on average, the longest {3}, acceptance {4:F3}",
                                      bidirectional ? "volbidirjrlt" : "volpathjrlt", nTours, (double)steps / nTours, longest,
                                      (double)accepted / Math.Max(1, steps));
                }
                ImageMetadata restored = new ImageMetadata();
                restored.renderTimeSeconds = (float)timer.ElapsedSeconds();
                camera.InitMetadata(restored);
                film.WriteImage(restored, (Float)1 / mutationsPerPixel);
                return;
            }
            if (monteCarlo)
            {
                // (not a chain: every bootstrap path is put on the film with its weight)
                ProgressReporter progressDirect = new ProgressReporter(nTotalMutations, "Rendering", Options.quiet);
                ParallelFor(0, nTotalMutations, (long start, long end) =>
                {
                    ScratchBuffer buf = threadScratchBuffers.Get();
                    for (long i = start; i < end; ++i)
                    {
                        PathSpacePath path = BootstrapPath(i, buf, out double weight, bidirectional, 5);
                        if (path != null)
                        {
                            SampledSpectrum L = path.measured;
                            L /= std.abs(L.y(path.lambda));
                            film.AddSplat(path.pRaster, L * (Float)weight, path.lambda);
                        }
                    }
                    progressDirect.Update(end - start);
                });
                progressDirect.Done();
                if (!Options.quiet && specularNee)
                {
                    Console.WriteLine("specular next-event estimation: {0} estimates stopped at {1} trials", sneeCapped, sneeTrials);
                }
                ImageMetadata direct = new ImageMetadata();
                direct.renderTimeSeconds = (float)timer.ElapsedSeconds();
                camera.InitMetadata(direct);
                film.WriteImage(direct, (Float)1 / mutationsPerPixel);
                return;
            }

            // Follow _nChains_ Markov chains to render image
            long acceptedMutations = 0, totalMutations = 0;
            ProgressReporter progressRender = new ProgressReporter(nChains, "Rendering", Options.quiet);
            ParallelFor(0, nChains, (long start, long end) =>
            {
                ScratchBuffer buf = threadScratchBuffers.Get();
                for (long chain = start; chain < end; ++chain)
                {
                    // Compute number of mutations to apply in current Markov chain
                    long nChainMutations = Math.Min((chain + 1) * nTotalMutations / nChains, nTotalMutations) - chain * nTotalMutations / nChains;

                    // Select initial state from the set of bootstrap samples
                    // ("weightedseeds": a chain starts from any bootstrap path and carries its weight, in place of a path
                    // chosen by its weight and the integral b. The image is then unbiased for any number of bootstrap paths, at
                    // the price of chains of very different weights.)
                    IndependentSampler sampler = SamplerOf(chain, 2);
                    int bootstrapIndex = weightedSeeds ? Math.Min((int)(sampler.Get1D() * nBootstrap), nBootstrap - 1)
                                                       : bootstrapTable.Sample(sampler.Get1D());
                    PathSpacePath current = BootstrapPath(bootstrapIndex, buf, out double bootstrapWeight, bidirectional);
                    if (current == null)
                    {
                        continue;
                    }
                    Float chainWeight = weightedSeeds ? (Float)bootstrapWeight : b;
                    SampledSpectrum LCurrent = current.measured;
                    LCurrent /= std.abs(LCurrent.y(current.lambda));
                    long accepted = 0;

                    // Run the Markov chain for _nChainMutations_ steps
                    for (long j = 0; j < nChainMutations; ++j)
                    {
                        PathSpacePath next = Mutate(current, sampler, buf, out PathSpacePath proposed, out Float accept);
                        SampledSpectrum LProposed = default;
                        if (accept > 0)
                        {
                            LProposed = proposed.measured;
                            LProposed /= std.abs(LProposed.y(proposed.lambda));
                        }

                        // Splat both current and proposed samples to _film_
                        if (accept > 0)
                        {
                            film.AddSplat(proposed.pRaster, LProposed * (accept * chainWeight), proposed.lambda);
                        }
                        film.AddSplat(current.pRaster, LCurrent * ((1 - accept) * chainWeight), current.lambda);

                        // Accept or reject the proposal
                        if (next != current)
                        {
                            current = next;
                            LCurrent = LProposed;
                            ++accepted;
                        }
                    }
                    Interlocked.Add(ref acceptedMutations, accepted);
                    Interlocked.Add(ref totalMutations, nChainMutations);
                }
                progressRender.Update(end - start);
            });
            progressRender.Done();
            if (!Options.quiet)
            {
                Console.WriteLine("{0}: b = {1:G6} (from the bootstrap paths {2:G6}), acceptance {3:F3}", bidirectional ? "volbidirmlt" : "volpathmlt",
                                  b, ownIntegral, (double)acceptedMutations / Math.Max(1, totalMutations));
            }

            // Store final image computed with MLT
            ImageMetadata metadata = new ImageMetadata();
            metadata.renderTimeSeconds = (float)timer.ElapsedSeconds();
            camera.InitMetadata(metadata);
            film.WriteImage(metadata, (Float)1 / mutationsPerPixel);
        }

        // PathSpaceMLTIntegrator Private Members
        private Camera camera;
        private bool bidirectional;
        private int maxDepth, nBootstrap, nChains, mutationsPerPixel, seed;
        private Float largeStepProbability;
        private bool weightedSeeds;
        private Float lensPerturbationProbability, lensPerturbationRadius, causticPerturbationProbability, causticPerturbationAngle;
        private Float manifoldPerturbationProbability, halfVectorPerturbationProbability, halfVectorPerturbationAngle;
        private Float sampleSpacePerturbationProbability, sampleSpaceSigma;
        private PolarisedPathIntegrator.Sensor sensor;
        private Float multiChainPerturbationProbability, halfVectorPerturbationPixels;
        private bool specularNee, monteCarlo, jumpRestore;
        private bool delayedRejection;
        private Float delayedRejectionScale;
        private Float tourLength, sneeTuples;
        private List<CausticBounds> causticBounds;
        private int sneeTrials, sneeSolver;
        private long sneeCapped;
        private int diagnose, integralSamples;
        private LightSampler lightSampler;
    }

}  // namespace pbrt
