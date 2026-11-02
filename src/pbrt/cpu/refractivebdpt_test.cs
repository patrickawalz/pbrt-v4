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
            Transform renderFromLight = pbrt.Translate(new Vector3f(-3.2f, 0.3f, 0.2f));
            Transform lightFromRender = pbrt.Inverse(renderFromLight);
            Shape lightShape = new Sphere(renderFromLight, lightFromRender, false, 1.2f, -1.2f, 1.2f, 360);
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
    }
}
