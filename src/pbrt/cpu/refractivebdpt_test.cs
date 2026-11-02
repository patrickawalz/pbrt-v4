// (not in pbrt-v4) cpu/refractivebdpt_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class RefractiveBDPTTests
    {
        const Float LensRadius = 1.5f;

        // A bump lens (smooth, index 1.4 at its center) of a scattering medium in a sphere, a camera ray towards it, and an integrator to walk with
        // The centre of the sphere light of the scene
        static Vector3f LightPosition = new Vector3f(-3.2f, 0.3f, 0.2f);
        static Float LightRadius = 1.2f;

        static void MakeScene(out RefractiveMedium medium, out VolPathIntegrator integrator, out Camera camera, int samples)
        {
            MakeScene(out medium, out integrator, out camera, out Light[] lights, samples);
        }

        static void MakeScene(out RefractiveMedium medium, out VolPathIntegrator integrator, out Camera camera, out Light[] lightArray, int samples)
        {
            Transform identity = new Transform();
            BumpRefractiveIndexField lens = new BumpRefractiveIndexField(new Point3f(0, 0, 0), LensRadius, 0.4f);
            Bounds3f box = new Bounds3f(new Point3f(-LensRadius, -LensRadius, -LensRadius), new Point3f(LensRadius, LensRadius, LensRadius));
            Medium scatterer = new HomogeneousMedium(new ConstantSpectrum(0.05f), new ConstantSpectrum(0.8f), 1.0f, new ConstantSpectrum(0f), 0.0f, 0.0f, null);
            medium = new RefractiveMedium(scatterer, lens, box, (SampledWavelengths l) => new SampledSpectrum(0.85f), 0.05f,
                                          (Point3f p) => pbrt.LengthSquared(new Vector3f(p.x, p.y, p.z)) <= LensRadius * LensRadius);
            Shape boundary = new Sphere(identity, identity, false, LensRadius, -LensRadius, LensRadius, 360);
            Transform renderFromLight = pbrt.Translate(LightPosition);
            Transform lightFromRender = pbrt.Inverse(renderFromLight);
            Shape lightShape = new Sphere(renderFromLight, lightFromRender, false, LightRadius, -LightRadius, LightRadius, 360);
            ConstantSpectrum Le = new ConstantSpectrum(1);
            Light areaLight = new DiffuseAreaLight(renderFromLight, new MediumInterface(), Le, 1.0f / SpectrumToPhotometric(Le),
                                                   lightShape, null, new Image(), null, false);
            List<Light> lights = new List<Light>();
            lights.Add(areaLight);
            lightArray = lights.ToArray();
            List<Primitive> prims = new List<Primitive>();
            prims.Add(new GeometricPrimitive(boundary, null, null, new MediumInterface(medium, null)));
            Material black = new DiffuseMaterial(new SpectrumConstantTexture(new ConstantSpectrum(0f)), null, null);
            prims.Add(new GeometricPrimitive(lightShape, black, areaLight, new MediumInterface()));
            Primitive aggregate = new BVHAggregate(prims);

            Point2i resolution = new Point2i(4, 4);
            AnimatedTransform identityA = new AnimatedTransform(identity, 0, identity, 1);
            FilmBaseParameters fp = new FilmBaseParameters(resolution, new Bounds2i(new Point2i(0, 0), resolution),
                                                           new BoxFilter(new Vector2f(0.5f, 0.5f)), 1.0f, PixelSensor.CreateDefault(),
                                                           "refractive_test.exr");
            RGBFilm film = new RGBFilm(fp, RGBColorSpace.sRGB);
            CameraBaseParameters cbp = new CameraBaseParameters(new CameraTransform(identityA), film, null,
                                                                new ParameterDictionary(), new FileLoc());
            camera = new PerspectiveCamera(cbp, 45, new Bounds2f(new Point2f(-1, -1), new Point2f(1, 1)), 0.0f, 10.0f);
            IndependentSampler sampler = new IndependentSampler(samples, 7);
            integrator = new VolPathIntegrator(1, camera, sampler, aggregate, lights, "uniform", false, false, false, 0,
                                               PolarisedPathIntegrator.Measurement.Intensity, false, 1, int.MaxValue, false, true);
        }

        // Whether the straight segment from a to b meets the sphere of the lens
        static bool SegmentMeetsLens(Point3f a, Point3f b)
        {
            Vector3f d = b - a;
            Float t = Math.Max(0.0f, Math.Min(1.0f, -pbrt.Dot(new Vector3f(a.x, a.y, a.z), d) / pbrt.LengthSquared(d)));
            Point3f closest = a + t * d;
            return pbrt.LengthSquared(new Vector3f(closest.x, closest.y, closest.z)) < LensRadius * LensRadius;
        }

        // The edges of camera subpaths: an edge that runs through the lens is recorded as curved, with the directions it leaves and
        // arrives along, and tracing it again from the recorded start reaches the vertex it arrives at (so that the start, the
        // direction and the vertex belong together); the other edges are not curved
        [TEST("RefractiveBDPT", "CameraSubpathEdgesRecordTheCurve")]
        public static void RefractiveBDPT_CameraSubpathEdgesRecordTheCurve()
        {
            int samples = 3000;
            MakeScene(out RefractiveMedium medium, out VolPathIntegrator integrator, out Camera camera, samples);
            IndependentSampler sampler = new IndependentSampler(samples, 11);
            ScratchBuffer buffer = new ScratchBuffer();
            int curved = 0, straight = 0, firstEdges = 0;
            double worstDensity = 0; int densityChecks = 0;
            double worstDistance = 0, worstDirection = 0, worstStart = 0, worstJacobian = 0, worstFineDistance = 0;
            List<double> distances = new List<double>(), directionErrors = new List<double>(), startErrors = new List<double>();
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                Vector3f direction = pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.2f));
                Point2f jitter = sampler.Get2D();
                RayDifferential ray = new RayDifferential(new Point3f(4.0f, 0.2f + 1e-2f * jitter.x, 0.1f + 1e-2f * jitter.y), direction);
                Vertex[] path = new Vertex[8];
                int count = BDPT.GenerateCameraSubpath(integrator, ray, ref lambda, sampler, buffer, 7, camera, path, false);
                for (int v = 1; v < count; ++v)
                {
                    if (path[v].type == VertexType.Light && path[v].IsInfiniteLight())
                    {
                        continue;
                    }
                    bool expectCurved = SegmentMeetsLens(path[v - 1].p(), path[v].p());
                    if (path[v].curvedEdge)
                    {
                        ++curved;
                        EXPECT_TRUE(expectCurved);
                        worstDistance = Math.Max(worstDistance, path[v].edge.distance);
                        distances.Add(path[v].edge.distance);
                        if (v == 1)
                        {
                            ++firstEdges;
                            worstStart = Math.Max(worstStart, pbrt.Length(path[v].edge.startDirection - direction));
                            startErrors.Add(pbrt.Length(path[v].edge.startDirection - direction));
                        }
                        // The edge arrives along the direction opposite to the one the vertex looks back along
                        if (path[v].type == VertexType.Medium)
                        {
                            worstDirection = Math.Max(worstDirection, pbrt.Length(path[v].edge.endDirection + path[v].mi.wo));
                            directionErrors.Add(pbrt.Length(path[v].edge.endDirection + path[v].mi.wo));
                        }
                        EXPECT_GT(path[v].edge.areaJacobian, 0.0f);
                        EXPECT_GT(path[v].edge.areaJacobianReverse, 0.0f);
                        if (v >= 2 && path[v].type == VertexType.Medium && path[v - 1].type == VertexType.Medium)
                        {
                            // The density of the vertex is the density of the phase function sample over the Jacobian of the edge
                            Float phase = path[v - 1].PDF(integrator, path[v - 2], path[v], path[v - 1].EdgeBack(), path[v].EdgeIn());
                            Float direct = path[v - 1].bsdf.PDF(path[v - 1].mi.wo, path[v].edge.startDirection);
                            Float expected = direct / path[v].edge.areaJacobian;
                            EXPECT_GT(expected, 0.0f);
                            worstDensity = Math.Max(worstDensity, Math.Abs(path[v].pdfFwd - expected) / expected);
                            worstDensity = Math.Max(worstDensity, Math.Abs(phase - expected) / expected);
                            ++densityChecks;
                        }
                        // The Jacobian of the edge against one traced with a tenth of the step, from the recorded start and direction
                        RefractiveConnection.TraceEdge(medium.BoundedField(), path[v - 1].p(), path[v].edge.startDirection, path[v].edge.length,
                                                       0.005f, out Point3f fineEnd, out Vector3f fineDirection, out Float fineJacobian,
                                                       out Float fineStartIndex, out Float fineEndIndex);
                        worstJacobian = Math.Max(worstJacobian, Math.Abs(fineJacobian - path[v].edge.areaJacobian) / fineJacobian);
                        worstFineDistance = Math.Max(worstFineDistance, pbrt.Length(fineEnd - path[v].p()));
                    }
                    else
                    {
                        ++straight;
                        // A segment that is not curved misses the lens, but one that grazes it can be curved by the lens: it only has to
                        // be an edge that the walk did not find in the medium
                        EXPECT_TRUE(!expectCurved || path[v].type == VertexType.Surface);
                    }
                }
                buffer.Reset();
            }
            Console.Error.WriteLine("MEASURE edges curved=" + curved + " straight=" + straight + " first=" + firstEdges + " worstDistance=" +
                                    worstDistance + " worstEndDirection=" + worstDirection + " worstStart=" + worstStart);
            distances.Sort(); directionErrors.Sort();
            Console.Error.WriteLine("MEASURE distance median=" + distances[distances.Count / 2] + " p99=" + distances[distances.Count * 99 / 100] + " direction median=" + directionErrors[directionErrors.Count / 2] + " p99=" + directionErrors[directionErrors.Count * 99 / 100]);
            startErrors.Sort();
            int fallbacks = distances.FindAll(d => d > 1e-3).Count;
            Console.Error.WriteLine("MEASURE start direction median=" + startErrors[startErrors.Count / 2] + " p90=" + startErrors[startErrors.Count * 9 / 10] + " p99=" + startErrors[startErrors.Count * 99 / 100] + " fallbacks=" + fallbacks);
            EXPECT_GT(curved, samples);
            EXPECT_GT(firstEdges, samples / 2);
            Console.Error.WriteLine("MEASURE against the fine trace: jacobian " + worstJacobian + " end " + worstFineDistance);
            // Measured: 1.4e-5, 6.8e-6 and 0 (the solver starts from the walk's direction, which already reaches the vertex)
            EXPECT_LT(worstDistance, 1e-4);
            EXPECT_LT(worstDirection, 5e-5);
            EXPECT_LT(worstStart, 1e-6);
            EXPECT_EQ(0, fallbacks);
            // Measured: 9.4e-4 and 5.3e-4
            EXPECT_LT(worstJacobian, 5e-3);
            EXPECT_LT(worstFineDistance, 3e-3);
            EXPECT_GT(densityChecks, 100);
            Console.WriteLine($"medium pdfFwd vs phase pdf / J: {densityChecks} checks, worst relative {worstDensity:E2}");
            EXPECT_LT(worstDensity, 1e-4);
        }

        // The edges of light subpaths, which walk the other way: leaving the previous vertex along the recorded start direction and
        // tracing finely, the curve reaches the vertex; the Jacobian is the one of that direction and the stored reverse one is
        // that of the curve traced back from the vertex along the reversed arrival direction
        [TEST("RefractiveBDPT", "LightSubpathEdgesRecordTheCurve")]
        public static void RefractiveBDPT_LightSubpathEdgesRecordTheCurve()
        {
            int samples = 3000;
            MakeScene(out RefractiveMedium medium, out VolPathIntegrator integrator, out Camera camera, out Light[] lights, samples);
            UniformLightSampler lightSampler = new UniformLightSampler(lights, new Allocator());
            IndependentSampler sampler = new IndependentSampler(samples, 13);
            ScratchBuffer buffer = new ScratchBuffer();
            int curved = 0;
            double worstDistance = 0, worstJacobian = 0, worstReverseJacobian = 0, worstBack = 0;
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                Vertex[] path = new Vertex[8];
                int count = BDPT.GenerateLightSubpath(integrator, ref lambda, sampler, camera, buffer, 7, 0.0f, lightSampler, path, false);
                for (int v = 1; v < count; ++v)
                {
                    if (!path[v].curvedEdge)
                    {
                        continue;
                    }
                    ++curved;
                    RefractiveEdge edge = path[v].edge;
                    RefractiveConnection.TraceEdge(medium.BoundedField(), path[v - 1].p(), edge.startDirection, edge.length, 0.005f,
                                                   out Point3f end, out Vector3f endDirection, out Float jacobian, out Float startIndex,
                                                   out Float endIndex);
                    worstDistance = Math.Max(worstDistance, pbrt.Length(end - path[v].p()));
                    worstJacobian = Math.Max(worstJacobian, Math.Abs(jacobian - edge.areaJacobian) / jacobian);
                    RefractiveConnection.TraceEdge(medium.BoundedField(), path[v].p(), (-1) * edge.endDirection, edge.length, 0.005f,
                                                   out Point3f back, out Vector3f backDirection, out Float reverseJacobian,
                                                   out Float reverseStartIndex, out Float reverseEndIndex);
                    worstBack = Math.Max(worstBack, pbrt.Length(back - path[v - 1].p()));
                    worstReverseJacobian = Math.Max(worstReverseJacobian, Math.Abs(reverseJacobian - edge.areaJacobianReverse) / reverseJacobian);
                }
                buffer.Reset();
            }
            Console.Error.WriteLine("MEASURE light edges curved=" + curved + " end " + worstDistance + " jacobian " + worstJacobian +
                                    " back " + worstBack + " reverse jacobian " + worstReverseJacobian);
            // Measured: 297 curved edges, and 2.6e-4, 3.4e-3, 6.2e-4 and 2.4e-3
            EXPECT_GT(curved, 100);
            EXPECT_LT(worstDistance, 3e-3);
            EXPECT_LT(worstJacobian, 1e-2);
            EXPECT_LT(worstBack, 3e-3);
            EXPECT_LT(worstReverseJacobian, 1e-2);
        }

        // The single scattering radiance of a camera ray through the lens, by volpath (the walk and the curved next event estimate)
        // or by bdpt (the same two strategies, now with weights between them: the hit of the light by the walk through the curved
        // edge, and the light sample connected along a curved path)
        static void LensRadiance(bool useBDPT, int maxDepth, bool unidirectional, int samples, out double mean, out double standardError)
        {
            MakeScene(out RefractiveMedium medium, out VolPathIntegrator volpath, out Camera camera, out Light[] lights, samples);
            List<Light> lightList = new List<Light>(lights);
            IndependentSampler sampler = new IndependentSampler(samples, 7);
            BDPTIntegrator bdpt = new BDPTIntegrator(camera, sampler, volpath.aggregate, lightList, maxDepth, false, false, false, null,
                                                     false, true, unidirectional);
            bdpt.skipCameraSampling = !unidirectional;
            VolPathIntegrator reference = new VolPathIntegrator(maxDepth, camera, sampler, volpath.aggregate, lightList, "uniform", false,
                                                                false, false, 0, PolarisedPathIntegrator.Measurement.Intensity, false, 1,
                                                                int.MaxValue, false, true);
            ScratchBuffer buffer = new ScratchBuffer();
            double sum = 0, sum2 = 0;
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                Vector3f rayDirection = pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.0f));
                Point2f jitter = sampler.Get2D();
                RayDifferential ray = new RayDifferential(new Point3f(4.0f, 0.2f + 1e-4f * jitter.x, 0.1f + 1e-4f * jitter.y), rayDirection);
                SampledSpectrum L = useBDPT ? bdpt.Li(ray, ref lambda, sampler, buffer, null) : reference.Li(ray, ref lambda, sampler, buffer, null);
                double value = L.Average();
                sum += value;
                sum2 += value * value;
                buffer.Reset();
            }
            mean = sum / samples;
            standardError = Math.Sqrt((sum2 / samples - mean * mean) / samples);
        }

        [TEST("RefractiveBDPT", "UnidirectionalStrategiesMatchVolpath")]
        public static void RefractiveBDPT_UnidirectionalStrategiesMatchVolpath()
        {
            LensRadiance(false, 1, false, 60000, out double volpathMean, out double volpathError);
            LensRadiance(true, 1, true, 60000, out double bdptMean, out double bdptError);
            Console.Error.WriteLine("MEASURE volpath=" + volpathMean + "+-" + volpathError + " bdpt(s<=1)=" + bdptMean + "+-" + bdptError);
            EXPECT_GT(volpathMean, 0.0);
            EXPECT_LT(Math.Abs(volpathMean - bdptMean), 4 * Math.Sqrt(volpathError * volpathError + bdptError * bdptError));
        }

        // A path that scatters once in the lens and ends on the light is found by two strategies: the walk that hits the light
        // (s = 0, t = 3) and the light sample connected from the scattering point (s = 1, t = 2). The weights of the two strategies
        // for the same path add up to one, only if both have the same density per area of the vertices and of the curved edge
        [TEST("RefractiveBDPT", "WeightsOfTheTwoStrategiesOfAPathAddUpToOne")]
        public static void RefractiveBDPT_WeightsOfTheTwoStrategiesOfAPathAddUpToOne()
        {
            int samples = 40000;
            MakeScene(out RefractiveMedium medium, out VolPathIntegrator volpath, out Camera camera, out Light[] lights, samples);
            List<Light> lightList = new List<Light>(lights);
            IndependentSampler sampler = new IndependentSampler(samples, 5);
            BDPTIntegrator bdpt = new BDPTIntegrator(camera, sampler, volpath.aggregate, lightList, 3, false, false, false, null,
                                                     false, true, false);
            ScratchBuffer buffer = new ScratchBuffer();
            int paths = 0, curvedPaths = 0;
            double worst = 0, sumWeight0 = 0, sumWeight1 = 0;
            SampledSpectrum one = new SampledSpectrum(1.0f);
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                Vector3f direction = pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.0f));
                Point2f jitter = sampler.Get2D();
                RayDifferential ray = new RayDifferential(new Point3f(4.0f, 0.2f + 1e-4f * jitter.x, 0.1f + 1e-4f * jitter.y), direction);
                Vertex[] cameraVertices = new Vertex[6];
                for (int v = 0; v < cameraVertices.Length; ++v)
                    cameraVertices[v] = new Vertex();
                int count = BDPT.GenerateCameraSubpath(bdpt, ray, ref lambda, sampler, buffer, 5, camera, cameraVertices, false);
                if (count < 3 || cameraVertices[1].type != VertexType.Medium || cameraVertices[2].type != VertexType.Surface ||
                    cameraVertices[2].si.areaLight == null)
                {
                    buffer.Reset();
                    continue;
                }
                ++paths;
                Vertex[] lightVertices = new Vertex[2];
                for (int v = 0; v < lightVertices.Length; ++v)
                    lightVertices[v] = new Vertex();
                Float weight0 = BDPT.MISWeight(bdpt, camera, lightVertices, cameraVertices, new Vertex(), 0, 3, bdpt.BDPTLightSampler, one, one);
                Light light = cameraVertices[2].si.areaLight;
                EndpointInteraction ei = new EndpointInteraction(light, cameraVertices[2].GetInteraction());
                Vertex sampled = Vertex.CreateLight(ei, one, 0);
                EdgeGeometry toLight = cameraVertices[2].EdgeIn(), toCamera = cameraVertices[2].EdgeBack();
                if (toLight.curved)
                    ++curvedPaths;
                sampled.pdfFwd = sampled.PDFLightOrigin(bdpt.infiniteLights, cameraVertices[1], bdpt.BDPTLightSampler, toCamera);
                Float weight1 = BDPT.MISWeight(bdpt, camera, lightVertices, cameraVertices, sampled, 1, 2, bdpt.BDPTLightSampler, one, one,
                                               toLight, toCamera);
                worst = Math.Max(worst, Math.Abs(weight0 + weight1 - 1));
                sumWeight0 += weight0;
                sumWeight1 += weight1;
                buffer.Reset();
            }
            Console.Error.WriteLine("MEASURE paths=" + paths + " curved=" + curvedPaths + " worst |w0 + w1 - 1|=" + worst +
                                    " mean w0=" + sumWeight0 / Math.Max(paths, 1) + " mean w1=" + sumWeight1 / Math.Max(paths, 1));
            EXPECT_GT(curvedPaths, 300);
            EXPECT_LT(worst, 1e-3);
        }

        // The transverse vector that the polarisation follows along a curved path (see PolarisationTransport)
        static void TransportThroughTheLens(Float stepSize, Point3f start, Vector3f direction, Vector3f reference, out Vector3f endDirection,
                                            out Vector3f endVector, out Float length)
        {
            BumpRefractiveIndexField lens = new BumpRefractiveIndexField(new Point3f(0, 0, 0), LensRadius, 0.4f);
            Bounds3f box = new Bounds3f(new Point3f(-LensRadius, -LensRadius, -LensRadius), new Point3f(LensRadius, LensRadius, LensRadius));
            Medium scatterer = new HomogeneousMedium(new ConstantSpectrum(0.0f), new ConstantSpectrum(0.0f), 1.0f, new ConstantSpectrum(0f), 0.0f, 0.0f, null);
            RefractiveMedium medium = new RefractiveMedium(scatterer, lens, box, (SampledWavelengths l) => new SampledSpectrum(0.0f), stepSize,
                                                           (Point3f p) => pbrt.LengthSquared(new Vector3f(p.x, p.y, p.z)) <= LensRadius * LensRadius);
            PolarisationTransport transport = new PolarisationTransport();
            transport.Begin(direction, reference);
            RNG rng = new RNG(1, 2);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            medium.SampleT_majCurved(start, direction, Infinity, 0.5f, ref rng, lambda,
                                     (Point3f p, Vector3f t, MediumProperties mp, SampledSpectrum m, SampledSpectrum T) => true,
                                     out Point3f endPoint, out endDirection, out length, transport);
            endVector = transport.vector;
        }

        [TEST("RefractiveBDPT", "PolarisationIsCarriedAlongTheCurve")]
        public static void RefractiveBDPT_PolarisationIsCarriedAlongTheCurve()
        {
            // In a plane (a ray through the centre of the lens at an angle) the out-of-plane vector does not move, and the in-plane
            // one stays in the plane and perpendicular to the path
            Point3f planarStart = new Point3f(-1.2f, 0.5f, 0);
            Vector3f planarDirection = pbrt.Normalize(new Vector3f(1.0f, 0.1f, 0));
            TransportThroughTheLens(0.01f, planarStart, planarDirection, new Vector3f(0, 0, 1), out Vector3f d1, out Vector3f e1, out Float l1);
            EXPECT_LT(pbrt.Length(e1 - new Vector3f(0, 0, 1)), 1e-5);
            TransportThroughTheLens(0.01f, planarStart, planarDirection, new Vector3f(0, 1, 0), out Vector3f d2, out Vector3f e2, out Float l2);
            EXPECT_LT(Math.Abs(e2.z), 1e-5);
            EXPECT_LT(Math.Abs(pbrt.Dot(e2, d2)), 1e-5);
            // The path did bend, so the test of the in-plane vector has an effect
            EXPECT_GT(pbrt.Length(d1 - planarDirection), 0.02f);
            EXPECT_LT(pbrt.Length(e2 - new Vector3f(0, 1, 0)), 1.5f);
            EXPECT_GT(pbrt.Length(e2 - new Vector3f(0, 1, 0)), 0.02f);

            // Off a plane the vector turns about the path (the path has torsion): it stays perpendicular and of unit length, and the
            // result converges with the step
            Point3f start = new Point3f(-1.2f, 0.3f, 0.4f);
            Vector3f direction = pbrt.Normalize(new Vector3f(1.0f, 0.2f, -0.3f));
            Vector3f reference = new Vector3f(0, 0, 1);
            TransportThroughTheLens(0.04f, start, direction, reference, out Vector3f dA, out Vector3f eA, out Float lA);
            TransportThroughTheLens(0.02f, start, direction, reference, out Vector3f dB, out Vector3f eB, out Float lB);
            TransportThroughTheLens(0.005f, start, direction, reference, out Vector3f dC, out Vector3f eC, out Float lC);
            Console.Error.WriteLine("MEASURE transport: perpendicular " + pbrt.Dot(eC, dC) + " length " + pbrt.Length(eC) +
                                    " error h=0.04 " + pbrt.Length(eA - eC) + " h=0.02 " + pbrt.Length(eB - eC));
            EXPECT_LT(Math.Abs(pbrt.Dot(eC, dC)), 1e-5);
            EXPECT_LT(Math.Abs(pbrt.Length(eC) - 1), 1e-5);
            EXPECT_LT(pbrt.Length(eB - eC), pbrt.Length(eA - eC));
            EXPECT_LT(pbrt.Length(eB - eC), 2e-3);
            // The vector turned about the path, relative to the one that a rotation-free (flat) carry would give
            EXPECT_GT(pbrt.Length(eC - reference), 0.02f);
        }

        // The polarised radiance (a Stokes component) of a camera ray through a lens of a Rayleigh scattering medium, by volpath
        static void PolarisedLensRadiance(PolarisedPathIntegrator.Measurement measurement, bool nee, int maxDepth, int samples,
                                          out double mean, out double standardError)
        {
            HGPhaseFunction.creationRayleigh = 1;
            MakeScene(out RefractiveMedium medium, out VolPathIntegrator volpath, out Camera camera, out Light[] lights, samples);
            HGPhaseFunction.creationRayleigh = 0;
            List<Light> lightList = new List<Light>(lights);
            IndependentSampler sampler = new IndependentSampler(samples, 9);
            VolPathIntegrator integrator = new VolPathIntegrator(maxDepth, camera, sampler, volpath.aggregate, lightList, "uniform", false,
                                                                 true, false, 0, measurement, false, 1, int.MaxValue, false, nee);
            ScratchBuffer buffer = new ScratchBuffer();
            double sum = 0, sum2 = 0;
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                Vector3f rayDirection = pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.2f));
                Point2f jitter = sampler.Get2D();
                RayDifferential ray = new RayDifferential(new Point3f(4.0f, 0.2f + 1e-4f * jitter.x, 0.1f + 1e-4f * jitter.y), rayDirection);
                SampledSpectrum L = integrator.Li(ray, ref lambda, sampler, buffer, null);
                double value = L.Average();
                sum += value;
                sum2 += value * value;
                buffer.Reset();
            }
            mean = sum / samples;
            standardError = Math.Sqrt((sum2 / samples - mean * mean) / samples);
        }

        // Polarised volpath through the lens: the next event estimate and the sampled direction agree for the Stokes components
        // that the frame transport along the first curved edge decides (S1 and S2 of the light scattered twice)
        [TEST("RefractiveBDPT", "PolarisedVolpathThroughTheLens")]
        public static void RefractiveBDPT_PolarisedVolpathThroughTheLens()
        {
            PolarisedPathIntegrator.Measurement[] measurements = { PolarisedPathIntegrator.Measurement.Intensity,
                                                                    PolarisedPathIntegrator.Measurement.S1,
                                                                    PolarisedPathIntegrator.Measurement.S2 };
            foreach (PolarisedPathIntegrator.Measurement measurement in measurements)
            {
                PolarisedLensRadiance(measurement, true, 2, 40000, out double nee, out double neeError);
                PolarisedLensRadiance(measurement, false, 2, 400000, out double direction, out double directionError);
                Console.Error.WriteLine("MEASURE " + measurement + ": nee " + nee + "+-" + neeError + " direction " + direction + "+-" + directionError);
                EXPECT_LT(Math.Abs(nee - direction), 4 * Math.Sqrt(neeError * neeError + directionError * directionError));
            }
        }


        // Two bumps of the index: not symmetric about a point, so the paths in it are not planar
        sealed class TwoBumpField : RefractiveIndexField
        {
            BumpRefractiveIndexField a = new BumpRefractiveIndexField(new Point3f(0.5f, 0.3f, 0.4f), 1.1f, 1.2f);
            BumpRefractiveIndexField b = new BumpRefractiveIndexField(new Point3f(-0.4f, -0.3f, -0.3f), 1.1f, 1.0f);

            public override void ValueAndGradient(Point3f p, out Float value, out Vector3f gradient)
            {
                ValueGradientAndHessian(p, out value, out gradient, out SquareMatrix3 hessian);
            }

            public override void ValueGradientAndHessian(Point3f p, out Float value, out Vector3f gradient, out SquareMatrix3 hessian)
            {
                a.ValueGradientAndHessian(p, out Float va, out Vector3f ga, out SquareMatrix3 ha);
                b.ValueGradientAndHessian(p, out Float vb, out Vector3f gb, out SquareMatrix3 hb);
                value = va + vb - 1;
                gradient = ga + gb;
                hessian = ha + hb;
            }
        }

        // The turning of the transported vector about the path follows Rytov's law: relative to the normal of the path it turns by
        // minus the torsion of the path, whose integral is computed here from the tangents alone (derivatives of the curve by
        // finite differences), independently of the rotations that carry the vector
        [TEST("RefractiveBDPT", "TransportTurnsByTheTorsionOfThePath")]
        public static void RefractiveBDPT_TransportTurnsByTheTorsionOfThePath()
        {
            Float step = 0.02f;
            RefractiveIndexField lens = new TwoBumpField();
            Bounds3f box = new Bounds3f(new Point3f(-LensRadius, -LensRadius, -LensRadius), new Point3f(LensRadius, LensRadius, LensRadius));
            Medium scatterer = new HomogeneousMedium(new ConstantSpectrum(0.0f), new ConstantSpectrum(0.0f), 1.0f, new ConstantSpectrum(0f), 0.0f, 0.0f, null);
            RefractiveMedium medium = new RefractiveMedium(scatterer, lens, box, (SampledWavelengths l) => new SampledSpectrum(0.0f), step,
                                                           (Point3f p) => pbrt.LengthSquared(new Vector3f(p.x, p.y, p.z)) <= LensRadius * LensRadius);
            Point3f[] starts = { new Point3f(1.0f, 0.0f, -0.5f), new Point3f(-1.0f, 0.3f, 0.2f), new Point3f(0.2f, -1.1f, 0.4f),
                                 new Point3f(0.1f, 0.9f, -0.9f), new Point3f(-0.8f, -0.8f, 0.5f), new Point3f(0.9f, 0.5f, 0.6f) };
            Vector3f[] directions = { new Vector3f(0.0f, 1.0f, 0.5f), new Vector3f(1.0f, 0.2f, -0.6f), new Vector3f(0.7f, 0.5f, 0.2f),
                                      new Vector3f(0.4f, -0.5f, 0.8f), new Vector3f(0.6f, 0.4f, -0.7f), new Vector3f(-0.5f, 0.5f, -0.6f) };
            double largest = 0;
            for (int ray = 0; ray < starts.Length; ++ray)
            {
                Point3f start = starts[ray];
                Vector3f direction = pbrt.Normalize(directions[ray]);
                PolarisationTransport transport = new PolarisationTransport();
                transport.tangentHistory = new List<Vector3f>();
                transport.vectorHistory = new List<Vector3f>();
                transport.Begin(direction, new Vector3f(1, 0, 0));
                transport.tangentHistory.Add(transport.tangent);
                transport.vectorHistory.Add(transport.vector);
                RNG rng = new RNG(1, 2);
                medium.SampleT_majCurved(start, direction, Infinity, 0.5f, ref rng, SampledWavelengths.SampleVisible(0.5f),
                                         (Point3f p, Vector3f t, MediumProperties mp, SampledSpectrum m, SampledSpectrum T) => true,
                                         out Point3f endPoint, out Vector3f endDirection, out Float length, transport);
                List<Vector3f> t = transport.tangentHistory;
                List<Vector3f> e = transport.vectorHistory;
                int n = t.Count;
                EXPECT_GT(n, 60);
                EXPECT_TRUE(true);
                // The curvature vector k = dt/ds and its derivative by finite differences, the torsion of the path
                double integral = 0, maxTorsion = 0, maxCurvature = 0;
                double angleStart = 0, angleEnd = 0;
                // Angle of the vector in the plane perpendicular to the path, from the normal (the direction of the curvature)
                Func<int, double> angle = (int i) =>
                {
                    Vector3f k = (t[i + 1] - t[i]) * (1.0f / step);
                    Vector3f normal = pbrt.Normalize(k);
                    Vector3f binormal = pbrt.Cross(t[i], normal);
                    return Math.Atan2(pbrt.Dot(e[i], binormal), pbrt.Dot(e[i], normal));
                };
                // Only the part of the path that is bent has a normal
                int first = 1, last = n - 3;
                while (first < last && pbrt.Length(t[first + 1] - t[first]) / step < 0.08f)
                    ++first;
                while (last > first && pbrt.Length(t[last + 2] - t[last + 1]) / step < 0.08f)
                    --last;
                angleStart = angle(first);
                angleEnd = angle(last);
                for (int i = first; i < last; ++i)
                {
                    Vector3f k0 = (t[i + 1] - t[i]) * (1.0f / step);
                    Vector3f k1 = (t[i + 2] - t[i + 1]) * (1.0f / step);
                    Vector3f dk = (k1 - k0) * (1.0f / step);
                    Vector3f km = (k0 + k1) * 0.5f;
                    Vector3f tm = pbrt.Normalize(t[i] + t[i + 1]);
                    double curvature2 = pbrt.LengthSquared(km);
                    double torsion = pbrt.Dot(tm, pbrt.Cross(km, dk)) / curvature2;
                    integral += torsion * step;
                    maxTorsion = Math.Max(maxTorsion, Math.Abs(torsion));
                    maxCurvature = Math.Max(maxCurvature, Math.Sqrt(curvature2));
                }
                // The angle is of the vector from the normal, which turns by +torsion in the frame that the vector does not turn in
                double turned = angleEnd - angleStart;
                while (turned > Math.PI) turned -= 2 * Math.PI;
                while (turned < -Math.PI) turned += 2 * Math.PI;
                Console.Error.WriteLine("MEASURE torsion: steps " + n + " max curvature " + maxCurvature + " max torsion " + maxTorsion +
                                        " integral " + integral + " vector turned relative to the normal " + turned);
                largest = Math.Max(largest, Math.Abs(integral));
                if (Math.Abs(integral) > 0.01 || Math.Abs(turned) > 0.01)
                    EXPECT_LT(Math.Abs(turned + integral), 0.02);

            }
            EXPECT_GT(largest, 0.05);
        }

        // The polarised radiance of the camera ray through the lens of a Rayleigh medium lit from the side, by volpath or by bdpt with
        // the strategies of a unidirectional path tracer (the scattering is near 90 degrees, so the light is strongly polarised and
        // the frames that the curved edges carry decide S1 and S2)
        static void PolarisedSideLitRadiance(bool useBDPT, PolarisedPathIntegrator.Measurement measurement, int maxDepth, int samples,
                                             out double mean, out double standardError, bool planar = false)
        {
            Vector3f savedLight = LightPosition;
            LightPosition = new Vector3f(0.3f, -3.2f, planar ? 0.0f : 0.4f);
            HGPhaseFunction.creationRayleigh = 1;
            MakeScene(out RefractiveMedium medium, out VolPathIntegrator volpath, out Camera camera, out Light[] lights, samples);
            HGPhaseFunction.creationRayleigh = 0;
            LightPosition = savedLight;
            List<Light> lightList = new List<Light>(lights);
            IndependentSampler sampler = new IndependentSampler(samples, 13);
            PolarisedPathIntegrator.Sensor sensor = new PolarisedPathIntegrator.Sensor();
            sensor.analyser = false;
            sensor.analyserAngle = 0;
            sensor.measurement = measurement;
            BDPTIntegrator bdpt = new BDPTIntegrator(camera, sampler, volpath.aggregate, lightList, maxDepth, false, false, false, sensor,
                                                     false, true, true);
            VolPathIntegrator reference = new VolPathIntegrator(maxDepth, camera, sampler, volpath.aggregate, lightList, "uniform", false,
                                                                true, false, 0, measurement, false, 1, int.MaxValue, false, true);
            ScratchBuffer buffer = new ScratchBuffer();
            double sum = 0, sum2 = 0;
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                Vector3f rayDirection = pbrt.Normalize(new Vector3f(-1.0f, 0.05f, planar ? 0.0f : 0.2f));
                Point2f jitter = sampler.Get2D();
                RayDifferential ray = new RayDifferential(new Point3f(4.0f, 0.2f + 1e-4f * jitter.x, (planar ? 0.0f : 0.1f) + 1e-4f * jitter.y), rayDirection);
                SampledSpectrum L = useBDPT ? bdpt.Li(ray, ref lambda, sampler, buffer, null) : reference.Li(ray, ref lambda, sampler, buffer, null);
                double value = L.Average();
                sum += value;
                sum2 += value * value;
                buffer.Reset();
            }
            mean = sum / samples;
            standardError = Math.Sqrt((sum2 / samples - mean * mean) / samples);
        }

        [TEST("RefractiveBDPT", "PolarisedUnidirectionalStrategiesMatchVolpath")]
        public static void RefractiveBDPT_PolarisedUnidirectionalStrategiesMatchVolpath()
        {
            PolarisedPathIntegrator.Measurement[] measurements = { PolarisedPathIntegrator.Measurement.Intensity,
                                                                    PolarisedPathIntegrator.Measurement.S1,
                                                                    PolarisedPathIntegrator.Measurement.S2 };
            foreach (PolarisedPathIntegrator.Measurement measurement in measurements)
            {
                PolarisedSideLitRadiance(false, measurement, 1, 60000, out double volpathMean, out double volpathError);
                PolarisedSideLitRadiance(true, measurement, 1, 60000, out double bdptMean, out double bdptError);
                Console.Error.WriteLine("MEASURE " + measurement + ": volpath " + volpathMean + "+-" + volpathError + " bdpt " + bdptMean + "+-" + bdptError);
                EXPECT_LT(Math.Abs(volpathMean - bdptMean), 4 * Math.Sqrt(volpathError * volpathError + bdptError * bdptError));
            }
        }

        // A scene that is symmetric about the plane z = 0 (the lens, the light, the camera ray and the camera's up direction in it)
        // measures no S2 (the 45 degree polarisation changes sign under the reflection), while S1 is strong: a carry of the
        // polarisation along the curved edge that turned the frames about the path would break that
        [TEST("RefractiveBDPT", "PolarisedRadianceOfASymmetricSceneHasNoS2")]
        public static void RefractiveBDPT_PolarisedRadianceOfASymmetricSceneHasNoS2()
        {
            PolarisedSideLitRadiance(true, PolarisedPathIntegrator.Measurement.S1, 1, 60000, out double s1, out double s1Error, true);
            PolarisedSideLitRadiance(true, PolarisedPathIntegrator.Measurement.S2, 1, 60000, out double s2, out double s2Error, true);
            Console.Error.WriteLine("MEASURE symmetric scene: S1 " + s1 + "+-" + s1Error + " S2 " + s2 + "+-" + s2Error);
            EXPECT_LT(s1, 0.0);
            EXPECT_LT(Math.Abs(s2), 4 * s2Error);
            EXPECT_LT(s2Error, 0.05 * Math.Abs(s1));
        }



        // The unweighted light sample connection from a scattering point in the lens along the curved path (the contribution of
        // ConnectBDPT with s = 1, before the weight) against the integral over the surface of the light of the same quantity,
        // summed on a grid of points with the solver and the exact transmittance of the grey medium
        [TEST("RefractiveBDPT", "LightSampleMatchesQuadratureOverTheLight")]
        public static void RefractiveBDPT_LightSampleMatchesQuadratureOverTheLight()
        {
            int samples = 400000;
            HGPhaseFunction.creationRayleigh = 0;
            MakeScene(out RefractiveMedium medium, out VolPathIntegrator volpath, out Camera camera, out Light[] lights, samples);
            List<Light> lightList = new List<Light>(lights);
            IndependentSampler sampler = new IndependentSampler(samples, 21);
            BDPTIntegrator bdpt = new BDPTIntegrator(camera, sampler, volpath.aggregate, lightList, 1, false, false, false, null, false, true, true);
            ScratchBuffer buffer = new ScratchBuffer();
            // A camera subpath with a vertex inside the lens
            Vertex[] cameraVertices = null;
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            for (int attempt = 0; attempt < 1000 && cameraVertices == null; ++attempt)
            {
                sampler.StartPixelSample(new Point2i(0, 0), attempt);
                Vertex[] candidate = new Vertex[4];
                for (int v = 0; v < candidate.Length; ++v)
                    candidate[v] = new Vertex();
                SampledWavelengths l = lambda;
                RayDifferential ray = new RayDifferential(new Point3f(4.0f, 0.2f, 0.1f), pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.2f)));
                int count = BDPT.GenerateCameraSubpath(bdpt, ray, ref l, sampler, buffer, 3, camera, candidate, false);
                if (count >= 2 && candidate[1].type == VertexType.Medium && BDPT.RefractiveOf(candidate[1]) != null)
                {
                    cameraVertices = candidate;
                    lambda = l;
                }
                buffer.Reset();
            }
            EXPECT_TRUE(cameraVertices != null);
            Vertex pt = cameraVertices[1];
            Vertex[] lightVertices = { new Vertex(), new Vertex() };
            double sum = 0, sum2 = 0;
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(1, 1), i);
                pstd.optional<Point2f> pRaster = default;
                Float weight = 0;
                SampledWavelengths l = lambda;
                SampledSpectrum L = BDPT.ConnectBDPT(bdpt, ref l, lightVertices, cameraVertices, 1, 2, bdpt.BDPTLightSampler, camera, sampler, ref pRaster, ref weight);
                double value = weight > 0 ? L.Average() / weight : 0;
                sum += value;
                sum2 += value * value;
                buffer.Reset();
            }
            double mean = sum / samples, error = Math.Sqrt((sum2 / samples - mean * mean) / samples);

            // The quadrature over the sphere of the light
            Point3f x = pt.p();
            Vector3f center = LightPosition;
            Float radius = 1.2f;
            Light light = lights[0];
            RNG rng = new RNG(5, 6);
            int points = 200000;
            double integral = 0;
            int failed = 0;
            double sigmaT = 0.85;
            for (int k = 0; k < points; ++k)
            {
                // A uniform point of the sphere
                Float u = 1 - 2 * rng.Uniform<Float>();
                Float phi = 2 * (Float)Math.PI * rng.Uniform<Float>();
                Float r = (Float)Math.Sqrt(Math.Max(0, 1 - u * u));
                Vector3f n = new Vector3f(r * (Float)Math.Cos(phi), r * (Float)Math.Sin(phi), u);
                Point3f y = new Point3f(center.x + radius * n.x, center.y + radius * n.y, center.z + radius * n.z);
                RefractiveConnectionOptions options = medium.ConnectionOptions(pbrt.Distance(x, y));
                options.inside = null;
                RefractiveConnectionPath path = default;
                bool found = RefractiveConnection.Solve(medium.BoundedField(), x, y, pbrt.Normalize(y - x), options, out path);
                if (!found)
                {
                    ++failed;
                    continue;
                }
                Float cosEnd = Math.Abs(pbrt.Dot(n, path.endDirection));
                SampledSpectrum Le = light.L(y, new Normal3f(n.x, n.y, n.z), new Point2f(0, 0), (-1) * path.endDirection, lambda);
                if (!Le)
                    continue;
                // The transmittance of the length in the medium
                RNG scratch = new RNG(1, 2);
                medium.SampleT_majCurved(x, path.startDirection, path.length, 0.5f, ref scratch, lambda,
                                         (Point3f p, Vector3f t, MediumProperties mp, SampledSpectrum m, SampledSpectrum T) => true,
                                         out Point3f exitPoint, out Vector3f exitDirection, out Float inside);
                double transmittance = Math.Exp(-sigmaT * inside);
                SampledSpectrum f = pt.bsdf.f(pt.mi.wo, path.startDirection);
                integral += f.Average() * Le.Average() * cosEnd / path.areaJacobian * transmittance;
            }
            integral *= 4 * Math.PI * radius * radius / points * pt.beta.Average();
            Console.Error.WriteLine("MEASURE light sample " + mean + "+-" + error + " quadrature " + integral + " (failed solves " + failed + "/" + points + ")" +
                                    " ratio " + mean / integral);
            EXPECT_LT(Math.Abs(mean - integral), 4 * error + 0.003 * integral);
        }

        // The strategies that find a path with two scatterings in the lens and then the light (the walk that hits the light, the light
        // sample at the second scattering, and the connection of the first scattering to a light subpath's vertex in the lens) each
        // estimate the same integral before their weights: the unweighted contributions agree in the mean
        [TEST("RefractiveBDPT", "UnweightedStrategiesAgreeAtDepthTwo")]
        public static void RefractiveBDPT_UnweightedStrategiesAgreeAtDepthTwo()
        {
            int samples = 200000;
            LightPosition = new Vector3f(-5.0f, 0.3f, 0.2f);
            LightRadius = 3.0f;
            MakeScene(out RefractiveMedium medium, out VolPathIntegrator volpath, out Camera camera, out Light[] lights, samples);
            LightPosition = new Vector3f(-3.2f, 0.3f, 0.2f);
            LightRadius = 1.2f;
            List<Light> lightList = new List<Light>(lights);
            IndependentSampler sampler = new IndependentSampler(samples, 37);
            BDPTIntegrator bdpt = new BDPTIntegrator(camera, sampler, volpath.aggregate, lightList, 1, false, false, false, null, false, true, true);
            ScratchBuffer buffer = new ScratchBuffer();
            double[] sum = new double[3], sum2 = new double[3];
            double[] weightedSum = new double[3];
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                Point2f jitter = sampler.Get2D();
                RayDifferential ray = new RayDifferential(new Point3f(4.0f, 0.2f + 1e-4f * jitter.x, 0.1f + 1e-4f * jitter.y),
                                                          pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.2f)));
                Vertex[] cameraVertices = new Vertex[6];
                for (int v = 0; v < cameraVertices.Length; ++v)
                    cameraVertices[v] = new Vertex();
                int cameraCount = BDPT.GenerateCameraSubpath(bdpt, ray, ref lambda, sampler, buffer, 4, camera, cameraVertices, false);
                Vertex[] lightVertices = new Vertex[4];
                for (int v = 0; v < lightVertices.Length; ++v)
                    lightVertices[v] = new Vertex();
                int lightCount = BDPT.GenerateLightSubpath(bdpt, ref lambda, sampler, camera, buffer, 3, 0, bdpt.BDPTLightSampler, lightVertices, false);
                double[] value = new double[3];
                bool firstInLens = cameraCount >= 2 && cameraVertices[1].type == VertexType.Medium;
                pstd.optional<Point2f> pRaster = default;
                Float weight;
                SampledWavelengths l;
                if (firstInLens && cameraCount >= 4 && cameraVertices[2].type == VertexType.Medium &&
                    cameraVertices[3].type == VertexType.Surface && cameraVertices[3].si.areaLight != null)
                {
                    weight = 0;
                    l = lambda;
                    SampledSpectrum Lw = BDPT.ConnectBDPT(bdpt, ref l, lightVertices, cameraVertices, 0, 4, bdpt.BDPTLightSampler, camera, sampler, ref pRaster, ref weight);
                    value[0] = weight > 0 ? Lw.Average() / weight : 0;
                    weightedSum[0] += Lw.Average();
                }
                if (firstInLens && cameraCount >= 3 && cameraVertices[2].type == VertexType.Medium)
                {
                    weight = 0;
                    l = lambda;
                    SampledSpectrum Ls = BDPT.ConnectBDPT(bdpt, ref l, lightVertices, cameraVertices, 1, 3, bdpt.BDPTLightSampler, camera, sampler, ref pRaster, ref weight);
                    value[1] = weight > 0 ? Ls.Average() / weight : 0;
                    weightedSum[1] += Ls.Average();
                }
                if (firstInLens && lightCount >= 2 && lightVertices[1].type == VertexType.Medium)
                {
                    weight = 0;
                    l = lambda;
                    SampledSpectrum Lc = BDPT.ConnectBDPT(bdpt, ref l, lightVertices, cameraVertices, 2, 2, bdpt.BDPTLightSampler, camera, sampler, ref pRaster, ref weight);
                    value[2] = weight > 0 ? Lc.Average() / weight : 0;
                    weightedSum[2] += Lc.Average();
                }
                for (int k = 0; k < 3; ++k)
                {
                    sum[k] += value[k];
                    sum2[k] += value[k] * value[k];
                }
                buffer.Reset();
            }
            double[] mean = new double[3], error = new double[3];
            for (int k = 0; k < 3; ++k)
            {
                mean[k] = sum[k] / samples;
                error[k] = Math.Sqrt((sum2[k] / samples - mean[k] * mean[k]) / samples);
            }
            Console.Error.WriteLine("MEASURE depth two unweighted walk(0,4) " + mean[0] + "+-" + error[0] + " light sample(1,3) " + mean[1] + "+-" + error[1] +
                                    " connection(2,2) " + mean[2] + "+-" + error[2] + "; weighted " + weightedSum[0] / samples + " " +
                                    weightedSum[1] / samples + " " + weightedSum[2] / samples + " total " +
                                    (weightedSum[0] + weightedSum[1] + weightedSum[2]) / samples);
        }

        // The two strategies that find the light after one scattering in the lens each estimate the same integral, before their weights:
        // the unweighted contributions of the walk that hits the light (s = 0, t = 3) and of the light sample (s = 1, t = 2) agree in the
        // mean over camera subpaths
        [TEST("RefractiveBDPT", "UnweightedStrategiesAgreeInTheMean")]
        public static void RefractiveBDPT_UnweightedStrategiesAgreeInTheMean()
        {
            int samples = 300000;
            LightPosition = new Vector3f(-5.0f, 0.3f, 0.2f);
            LightRadius = 3.0f;
            MakeScene(out RefractiveMedium medium, out VolPathIntegrator volpath, out Camera camera, out Light[] lights, samples);
            LightPosition = new Vector3f(-3.2f, 0.3f, 0.2f);
            LightRadius = 1.2f;
            List<Light> lightList = new List<Light>(lights);
            IndependentSampler sampler = new IndependentSampler(samples, 31);
            BDPTIntegrator bdpt = new BDPTIntegrator(camera, sampler, volpath.aggregate, lightList, 1, false, false, false, null, false, true, true);
            ScratchBuffer buffer = new ScratchBuffer();
            double sumWalk = 0, sumSample = 0, sumDifference2 = 0, sumDifference = 0;
            double weightedWalk = 0, weightedSample = 0;
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                Point2f jitter = sampler.Get2D();
                RayDifferential ray = new RayDifferential(new Point3f(4.0f, 0.2f + 1e-4f * jitter.x, 0.1f + 1e-4f * jitter.y),
                                                          pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.2f)));
                Vertex[] cameraVertices = new Vertex[5];
                for (int v = 0; v < cameraVertices.Length; ++v)
                    cameraVertices[v] = new Vertex();
                int count = BDPT.GenerateCameraSubpath(bdpt, ray, ref lambda, sampler, buffer, 3, camera, cameraVertices, false);
                double walk = 0, sample = 0;
                if (count >= 2 && cameraVertices[1].type == VertexType.Medium)
                {
                    Vertex[] lightVertices = { new Vertex(), new Vertex() };
                    pstd.optional<Point2f> pRaster = default;
                    Float weight = 0;
                    SampledWavelengths l = lambda;
                    if (count >= 3 && cameraVertices[2].type == VertexType.Surface && cameraVertices[2].si.areaLight != null)
                    {
                        SampledSpectrum Lw = BDPT.ConnectBDPT(bdpt, ref l, lightVertices, cameraVertices, 0, 3, bdpt.BDPTLightSampler, camera, sampler, ref pRaster, ref weight);
                        walk = weight > 0 ? Lw.Average() / weight : 0;
                        weightedWalk += Lw.Average();
                    }
                    weight = 0;
                    l = lambda;
                    SampledSpectrum Ls = BDPT.ConnectBDPT(bdpt, ref l, lightVertices, cameraVertices, 1, 2, bdpt.BDPTLightSampler, camera, sampler, ref pRaster, ref weight);
                    sample = weight > 0 ? Ls.Average() / weight : 0;
                    weightedSample += Ls.Average();
                }
                sumWalk += walk;
                sumSample += sample;
                sumDifference += walk - sample;
                sumDifference2 += (walk - sample) * (walk - sample);
                buffer.Reset();
            }
            double meanDifference = sumDifference / samples;
            double errorDifference = Math.Sqrt((sumDifference2 / samples - meanDifference * meanDifference) / samples);
            Console.Error.WriteLine("MEASURE unweighted walk " + sumWalk / samples + " sample " + sumSample / samples + " difference " + meanDifference +
                                    "+-" + errorDifference + " z=" + meanDifference / errorDifference + "; weighted walk " + weightedWalk / samples +
                                    " sample " + weightedSample / samples + " total " + (weightedWalk + weightedSample) / samples);
            EXPECT_LT(Math.Abs(meanDifference), 4 * errorDifference);
            EXPECT_GT(sumSample, 0.0);
        }
    }
}
