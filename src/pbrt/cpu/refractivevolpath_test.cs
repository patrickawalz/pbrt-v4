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
                // The walk seeds its random numbers from the ray, so each sample gets a ray that differs by about 1e-4 (a position on
                // the film, in a render)
                Vector3f rayDirection = pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.0f));
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

        // The mean radiance along a ray through a Luneburg lens with one scattering, lit only by an emissive volume (the medium of a
        // sphere) beside the lens. mode 0: the emission is found by the walk alone; 1: by the connection along a curved path to a
        // point of the volume; 2: by the forward estimate along the curved ray (emission_fnee)
        static void VolumeLightRadiance(int mode, int samples, out double mean, out double standardError)
        {
            Transform identity = new Transform();
            Float radius = 1.5f;
            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), radius, 2.0f);
            Bounds3f box = new Bounds3f(new Point3f(-radius, -radius, -radius), new Point3f(radius, radius, radius));
            RefractiveMedium medium = new RefractiveMedium(new VaryingScatterer(), lens, box,
                                                           (SampledWavelengths l) => new SampledSpectrum(1.0f), 0.05f,
                                                           (Point3f p) => pbrt.LengthSquared(new Vector3f(p.x, p.y, p.z)) <= radius * radius);
            Shape boundary = new Sphere(identity, identity, false, radius, -radius, radius, 360);

            Point3f center = new Point3f(-1.0f, 2.3f, 0.2f);
            Float fireRadius = 0.8f;
            Bounds3f fireBounds = new Bounds3f(center - new Vector3f(fireRadius, fireRadius, fireRadius),
                                               center + new Vector3f(fireRadius, fireRadius, fireRadius));
            TextureMedium fire = new TextureMedium(fireBounds, identity, new ConstantSpectrum(2f), new ConstantSpectrum(0f), 1.0f, 0f,
                                                   new FloatConstantTexture(1f), 0f, 1f, 4, 4, 0, 0.5f,
                                                   new SpectrumConstantTexture(new ConstantSpectrum(1f)), 1f, null);
            Transform renderFromFire = pbrt.Translate(new Vector3f(center.x, center.y, center.z));
            Shape fireShape = new Sphere(renderFromFire, pbrt.Inverse(renderFromFire), false, fireRadius, -fireRadius, fireRadius, 360);

            TextureMedium.ClearEmissionLights();
            List<Light> lights = new List<Light>();
            try
            {
                fire.EnableEmissionSampling(8, mode == 2);
                lights = TextureMedium.AddEmissionLights(lights);
            }
            finally
            {
                TextureMedium.ClearEmissionLights();
            }

            List<Primitive> prims = new List<Primitive>();
            prims.Add(new GeometricPrimitive(boundary, null, null, new MediumInterface(medium, null)));
            prims.Add(new GeometricPrimitive(fireShape, null, null, new MediumInterface(fire, null)));
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
            IndependentSampler sampler = new IndependentSampler(samples, 11);
            VolPathIntegrator integrator = new VolPathIntegrator(2, camera, sampler, aggregate, lights, "uniform", false, false,
                                                                 false, 0, PolarisedPathIntegrator.Measurement.Intensity, false, 1,
                                                                 int.MaxValue, false, mode != 0);
            ScratchBuffer buffer = new ScratchBuffer();
            double sum = 0, sum2 = 0;
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                Vector3f rayDirection = pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.0f));
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

        // The emission of a volume beside the lens, found from a vertex inside it: the walk alone, the connection to a point of the
        // volume along a curved path, and the forward estimate along the curved ray estimate the same radiance
        [TEST("RefractiveVolPath", "VolumeLightThroughTheLensAgreesAcrossStrategies")]
        public static void RefractiveVolPath_VolumeLightThroughTheLensAgreesAcrossStrategies()
        {
            VolumeLightRadiance(0, 200000, out double meanWalk, out double seWalk);
            VolumeLightRadiance(1, 30000, out double meanPoint, out double sePoint);
            VolumeLightRadiance(2, 30000, out double meanForward, out double seForward);
            Console.Error.WriteLine("MEASURE volume light walk=" + meanWalk + "+-" + seWalk + " point=" + meanPoint + "+-" + sePoint +
                                    " forward=" + meanForward + "+-" + seForward);
            EXPECT_GT(meanWalk, 0.0);
            EXPECT_LT(Math.Abs(meanPoint - meanWalk), 4 * Math.Sqrt(sePoint * sePoint + seWalk * seWalk));
            EXPECT_LT(Math.Abs(meanForward - meanWalk), 4 * Math.Sqrt(seForward * seForward + seWalk * seWalk));
            EXPECT_LT(sePoint, 0.1 * meanPoint);
            EXPECT_LT(seForward, 0.1 * meanForward);
        }

        // An absorbing and emitting medium whose coefficients vary with the position
        private class VaryingEmitter : Medium
        {
            public static Float Absorption(Point3f p) { return 0.45f + 0.3f * (Float)Math.Sin(3 * p.x + 2 * p.z); }
            public static Float Emission(Point3f p) { return 1.5f + (Float)Math.Cos(2 * p.x + 3 * p.y); }

            public override string ToString() { return "[ VaryingEmitter ]"; }
            public override bool IsEmissive() { return true; }

            public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
            {
                return new MediumProperties { sigma_a = new SampledSpectrum(Absorption(p)), sigma_s = new SampledSpectrum(0f),
                                              phase = new HGPhaseFunction(0f), Le = new SampledSpectrum(Emission(p)) };
            }

            public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
            {
                return buf.Alloc(new HomogeneousMajorantIterator(0, tMax, new SampledSpectrum(1.0f)));
            }
        }

        // The grids of a GridModulatedMedium of the test: the factors on the extinction and the emission of a homogeneous medium
        // (absorption 0.5, emission 1), linear in the position, which trilinear interpolation reproduces exactly
        const int GridResolution = 7;
        // The camera ray of the emission tests: it crosses the lens obliquely, so that it passes through the cells of the grids in z
        static readonly Vector3f EmissionRayDirection = pbrt.Normalize(new Vector3f(-1.0f, 0.05f, 0.2f));
        static Float GridDensity(Point3f p) { return 0.6f + 0.2f * (p.x / 1.5f) + 0.3f * (p.z / 1.5f); }
        static Float GridEmission(Point3f p) { return 1.2f + 0.5f * (p.y / 1.5f) - 0.3f * (p.x / 1.5f) + 0.4f * (p.z / 1.5f); }

        static Float[] SampleGrid(Func<Point3f, Float> function)
        {
            Float[] values = new Float[GridResolution * GridResolution * GridResolution];
            for (int z = 0; z < GridResolution; ++z)
            {
                for (int y = 0; y < GridResolution; ++y)
                {
                    for (int x = 0; x < GridResolution; ++x)
                    {
                        Point3f p = new Point3f(-1.5f + 3.0f * x / (GridResolution - 1), -1.5f + 3.0f * y / (GridResolution - 1),
                                                -1.5f + 3.0f * z / (GridResolution - 1));
                        values[(z * GridResolution + y) * GridResolution + x] = function(p);
                    }
                }
            }
            return values;
        }

        // The radiance along a camera ray through a lens of an emitting, absorbing medium, with nothing behind it, by the
        // integrator
        static void EmissionRadiance(RefractiveIndexField lens, Medium inner, Float majorant, int samples, out double mean,
                                     out double standardError)
        {
            Transform identity = new Transform();
            Float radius = 1.5f;
            Bounds3f box = new Bounds3f(new Point3f(-radius, -radius, -radius), new Point3f(radius, radius, radius));
            RefractiveMedium medium = new RefractiveMedium(inner, lens, box, (SampledWavelengths l) => new SampledSpectrum(majorant),
                                                           0.05f, (Point3f p) => pbrt.LengthSquared(new Vector3f(p.x, p.y, p.z)) <= radius * radius);
            Shape boundary = new Sphere(identity, identity, false, radius, -radius, radius, 360);
            // A light that no ray reaches: the scene needs one
            Transform renderFromLight = pbrt.Translate(new Vector3f(0.0f, 20.0f, 0.0f));
            Transform lightFromRender = pbrt.Inverse(renderFromLight);
            Shape lightShape = new Sphere(renderFromLight, lightFromRender, false, 0.1f, -0.1f, 0.1f, 360);
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
            AnimatedTransform identityA = new AnimatedTransform(identity, 0, identity, 1);
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
                                                                 int.MaxValue, false, true);
            ScratchBuffer buffer = new ScratchBuffer();
            double sum = 0, sum2 = 0;
            for (int i = 0; i < samples; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i);
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(sampler.Get1D());
                // The walk seeds its random numbers from the ray, so each sample gets a ray that differs by about 1e-4 (a position
                // on the film, in a render): with one ray for all of them the samples share their random numbers
                Vector3f rayDirection = EmissionRayDirection;
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

        // The emitted radiance along the curve of the same camera ray: the integral of sigma_a Le exp(-optical depth) over the arc
        // length on a finely traced path (trapezoid rule)
        static double EmissionQuadrature(RefractiveIndexField lens, Func<Point3f, Float> absorptionFunction, Func<Point3f, Float> emissionFunction)
        {
            Float radius = 1.5f;
            Vector3f d = EmissionRayDirection;
            Point3f o = new Point3f(4.0f, 0.2f, 0.1f);
            // The entry point on the sphere
            Float b = pbrt.Dot(new Vector3f(o.x, o.y, o.z), d);
            Float c = pbrt.LengthSquared(new Vector3f(o.x, o.y, o.z)) - radius * radius;
            Float tEntry = -b - (Float)Math.Sqrt(b * b - c);
            Point3f entry = o + tEntry * d;
            SymplecticRay ray = new SymplecticRay(lens, entry, d);
            Float step = 0.001f;
            double radiance = 0, depth = 0;
            Float previousAbsorption = absorptionFunction(ray.p);
            Float previousIntegrand = previousAbsorption * emissionFunction(ray.p);
            double previousTransmittance = 1;
            for (int i = 0; i < 20000; ++i)
            {
                SymplecticRay next = ray;
                next.Step(lens, step);
                Float fraction = 1;
                if (pbrt.LengthSquared(new Vector3f(next.p.x, next.p.y, next.p.z)) > radius * radius)
                {
                    // The last step ends at the sphere
                    Float lo = 0, hi = step;
                    for (int k = 0; k < 40; ++k)
                    {
                        Float mid = 0.5f * (lo + hi);
                        SymplecticRay partial = ray;
                        partial.Step(lens, mid);
                        if (pbrt.LengthSquared(new Vector3f(partial.p.x, partial.p.y, partial.p.z)) > radius * radius) { hi = mid; } else { lo = mid; }
                    }
                    fraction = lo / step;
                    next = ray;
                    next.Step(lens, lo);
                }
                Float h = fraction * step;
                Float absorption = absorptionFunction(next.p);
                Float integrand = absorption * emissionFunction(next.p);
                depth += 0.5 * (absorption + previousAbsorption) * h;
                double transmittance = Math.Exp(-depth);
                radiance += 0.5 * (integrand * transmittance + previousIntegrand * previousTransmittance) * h;
                previousAbsorption = absorption;
                previousIntegrand = integrand;
                previousTransmittance = transmittance;
                ray = next;
                if (fraction < 1)
                {
                    break;
                }
            }
            return radiance;
        }

        // Medium emission along a curved path: the integrator's estimate (emission added at the tentative collisions of the curved
        // walk, with the weights of the straight one) equals the integral of sigma_a Le exp(-optical depth) over the arc length,
        // for a constant medium, for coefficients that vary along the curve, and for the density and emission grids of a scene,
        // in a straight path (index 1) and through the Luneburg lens
        [TEST("RefractiveVolPath", "EmissionAlongTheCurveMatchesQuadrature")]
        public static void RefractiveVolPath_EmissionAlongTheCurveMatchesQuadrature()
        {
            RefractiveIndexField[] fields = new RefractiveIndexField[] { new LinearRefractiveIndexField(1.0f, new Vector3f(0, 0, 0)),
                new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), 1.5f, 2.0f) };
            Medium constant = new HomogeneousMedium(new ConstantSpectrum(0.5f), new ConstantSpectrum(0f), 1.0f, new ConstantSpectrum(1f), 1.0f, 0.0f, null);
            Bounds3f gridBounds = new Bounds3f(new Point3f(-1.5f, -1.5f, -1.5f), new Point3f(1.5f, 1.5f, 1.5f));
            GridModulatedMedium modulated = new GridModulatedMedium(constant, gridBounds, GridResolution, GridResolution, GridResolution,
                                                                    SampleGrid(GridDensity), SampleGrid(GridEmission));
            Medium[] media = new Medium[] { constant, new VaryingEmitter(), modulated };
            Float[] majorants = new Float[] { 0.5f, 1.0f, 0.5f * modulated.MaxDensity() };
            Func<Point3f, Float>[] absorption = new Func<Point3f, Float>[] { (Point3f p) => 0.5f, VaryingEmitter.Absorption,
                                                                             (Point3f p) => 0.5f * GridDensity(p) };
            Func<Point3f, Float>[] emission = new Func<Point3f, Float>[] { (Point3f p) => 1.0f, VaryingEmitter.Emission, GridEmission };
            string[] names = new string[] { "constant", "varying", "grids" };
            for (int m = 0; m < media.Length; ++m)
            {
                for (int f = 0; f < fields.Length; ++f)
                {
                    double radiance = EmissionQuadrature(fields[f], absorption[m], emission[m]);
                    EmissionRadiance(fields[f], media[m], majorants[m], 60000, out double mean, out double standardError);
                    Console.Error.WriteLine("MEASURE emission " + names[m] + " field=" + f + " integrator=" + mean + "+-" + standardError +
                                            " quadrature=" + radiance);
                    EXPECT_GT(radiance, 0.1);
                    EXPECT_LT(Math.Abs(mean - radiance), 4 * standardError);
                    EXPECT_LT(standardError, 0.01 * radiance);
                }
            }
        }
    }
}
