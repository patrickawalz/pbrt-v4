// (not in pbrt-v4) cpu/refractivevolpath_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class RefractiveVolPathTests
    {
        // A scattering medium whose coefficients vary with the position
        private class VaryingScatterer : Medium
        {
            public override string ToString() { return "[ VaryingScatterer ]"; }
            public override bool IsEmissive() { return false; }

            public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
            {
                return new MediumProperties { sigma_a = new SampledSpectrum(0.1f),
                                              sigma_s = new SampledSpectrum(0.3f + 0.25f * (Float)Math.Sin(3 * p.x + 2 * p.z)),
                                              phase = new HGPhaseFunction(0f), Le = new SampledSpectrum(0f) };
            }

            public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
            {
                return buf.Alloc(new HomogeneousMajorantIterator(0, tMax, new SampledSpectrum(1.0f)));
            }
        }

        // The mean radiance along a ray through a Luneburg lens (a sphere of the medium) next to an emitting sphere, with one
        // scattering: by next event estimation along curved paths (nee) or by the direction of the phase function alone
        static void Radiance(bool nee, int samples, out double mean, out double standardError)
        {
            Transform identity = new Transform();
            Float radius = 1.5f;
            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), radius, 2.0f);
            Bounds3f box = new Bounds3f(new Point3f(-radius, -radius, -radius), new Point3f(radius, radius, radius));
            RefractiveMedium medium = new RefractiveMedium(new VaryingScatterer(), lens, box,
                                                           (SampledWavelengths l) => new SampledSpectrum(1.0f), 0.05f,
                                                           (Point3f p) => pbrt.LengthSquared(new Vector3f(p.x, p.y, p.z)) <= radius * radius);

            Shape boundary = new Sphere(identity, identity, false, radius, -radius, radius, 360);
            Transform renderFromLight = pbrt.Translate(new Vector3f(-3.2f, 0.3f, 0.2f));
            Transform lightFromRender = pbrt.Inverse(renderFromLight);
            Shape lightShape = new Sphere(renderFromLight, lightFromRender, false, 1.2f, -1.2f, 1.2f, 360);
            ConstantSpectrum Le = new ConstantSpectrum(1);
            Light areaLight = new DiffuseAreaLight(renderFromLight, new MediumInterface(), Le, 1.0f / SpectrumToPhotometric(Le),
                                                   lightShape, null, new Image(), null, false);
            List<Light> lights = new List<Light>();
            lights.Add(areaLight);

            List<Primitive> prims = new List<Primitive>();
            prims.Add(new GeometricPrimitive(boundary, null, null, new MediumInterface(medium, null)));
            Material black = new DiffuseMaterial(new SpectrumConstantTexture(new ConstantSpectrum(0f)), null, null);
            prims.Add(new GeometricPrimitive(lightShape, black, areaLight, new MediumInterface()));
            Primitive aggregate = new BVHAggregate(prims);

            Point2i resolution = new Point2i(4, 4);
            Transform id = new Transform();
            AnimatedTransform identityA = new AnimatedTransform(id, 0, id, 1);
            FilmBaseParameters fp = new FilmBaseParameters(resolution, new Bounds2i(new Point2i(0, 0), resolution),
                                                           new BoxFilter(new Vector2f(0.5f, 0.5f)), 1.0f, PixelSensor.CreateDefault(),
                                                           "refractive_test.exr");
            RGBFilm film = new RGBFilm(fp, RGBColorSpace.sRGB);
            CameraBaseParameters cbp = new CameraBaseParameters(new CameraTransform(identityA), film, null,
                                                                new ParameterDictionary(), new FileLoc());
            Camera camera = new PerspectiveCamera(cbp, 45, new Bounds2f(new Point2f(-1, -1), new Point2f(1, 1)), 0.0f, 10.0f);
            IndependentSampler sampler = new IndependentSampler(samples, 7);
            VolPathIntegrator integrator = new VolPathIntegrator(1, camera, sampler, aggregate, lights, "uniform", false, false,
                                                                 false, 0, PolarisedPathIntegrator.Measurement.Intensity, false, 1,
                                                                 int.MaxValue, false, nee);
            ScratchBuffer buffer = new ScratchBuffer();
            double sum = 0, sum2 = 0;
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                RayDifferential ray = new RayDifferential(new Point3f(4.0f, 0.2f, 0.1f), pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.0f)));
                SampledSpectrum L = integrator.Li(ray, ref lambda, sampler, buffer, null);
                double value = L.Average();
                sum += value;
                sum2 += value * value;
                buffer.Reset();
            }
            mean = sum / samples;
            standardError = Math.Sqrt((sum2 / samples - mean * mean) / samples);
        }

        // From inside the lens, the light is found by the connection along a curved path or by the direction of the phase function
        // alone: the two estimators of the single scattering radiance agree
        [TEST("RefractiveVolPath", "NextEventEstimateMatchesDirectionSampling")]
        public static void RefractiveVolPath_NextEventEstimateMatchesDirectionSampling()
        {
            Radiance(true, 30000, out double meanNee, out double seNee);
            Radiance(false, 300000, out double meanDirection, out double seDirection);
            Console.Error.WriteLine("MEASURE nee=" + meanNee + "+-" + seNee + " direction=" + meanDirection + "+-" + seDirection);
            EXPECT_LT(Math.Abs(meanNee - meanDirection), 4 * Math.Sqrt(seNee * seNee + seDirection * seDirection));
            EXPECT_GT(meanDirection, 0.0);
        }
    }
}
