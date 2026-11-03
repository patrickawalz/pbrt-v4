// (not in pbrt-v4) realisticcameraaspheric_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class RealisticCameraAsphericTests
    {
        // The aspheric terms of the interfaces of the double Gauss lens: the conic constant and A4, A6, ..., A16 in millimetres.
        // The front element and the interface after the stop are aspheric, the others are spheres.
        static double[] AsphericRow(int interfaceIndex)
        {
            if (interfaceIndex == 0)
            {
                return new double[] { -0.4, 2e-6, -1e-9, 0, 0, 0, 0, 0 };
            }

            if (interfaceIndex == 6)
            {
                return new double[] { 0.2, -8e-6, 0, 0, 0, 0, 0, 0 };
            }

            return null;
        }

        static List<Float> AsphericParameters()
        {
            List<Float> parameters = new List<Float>();
            for (int i = 0; i < RealisticCameraPolarisedTests.doubleGauss.Length / 4; ++i)
            {
                double[] row = AsphericRow(i);
                for (int k = 0; k < 8; ++k)
                {
                    parameters.Add(row == null ? 0 : (Float)row[k]);
                }
            }

            return parameters;
        }

        // Spheres to within a picometre of sag: the terms are so small that only the machinery of the aspheric intersection
        // differs from that of the sphere
        static List<Float> NearlySphericalParameters()
        {
            List<Float> parameters = new List<Float>();
            for (int i = 0; i < RealisticCameraPolarisedTests.doubleGauss.Length / 4; ++i)
            {
                for (int k = 0; k < 8; ++k)
                {
                    parameters.Add(k == 1 ? 1e-12f : 0);
                }
            }

            return parameters;
        }

        // The sag of an interface of the double Gauss lens in metres at the radius r metres: the formula of the lens file
        // parameter
        static double Sag(int interfaceIndex, double r, bool aspheric = true)
        {
            double radius = RealisticCameraPolarisedTests.doubleGauss[interfaceIndex * 4] * 0.001;
            double[] row = aspheric ? AsphericRow(interfaceIndex) : null;
            double rMm = r * 1000;
            double c = 1 / (radius * 1000);
            double conic = row == null ? 0 : row[0];
            double sag = c * rMm * rMm / (1 + Math.Sqrt(1 - (1 + conic) * c * c * rMm * rMm));
            if (row != null)
            {
                for (int term = 0; term < 7; ++term)
                {
                    sag += row[1 + term] * Math.Pow(rMm, 4 + 2 * term);
                }
            }

            return sag / 1000;
        }

        // The slope of the sag by central differences
        static double Slope(int interfaceIndex, double r, bool aspheric = true)
        {
            double h = 1e-6;
            double rLow = Math.Max(r - h, 0);
            return (Sag(interfaceIndex, r + h, aspheric) - Sag(interfaceIndex, rLow, aspheric)) / (r + h - rLow);
        }

        // The normal of the front element towards the scene at (x, y) in camera space
        static Vector3f FrontNormal(Float x, Float y, bool aspheric = true)
        {
            double r = Math.Sqrt((double)x * x + (double)y * y);
            double radial = r > 0 ? Slope(0, r, aspheric) / r : 0;
            return pbrt.Normalize(new Vector3f((Float)(radial * x), (Float)(radial * y), 1));
        }

        // The index of refraction of the medium behind interface i of the double Gauss lens at a wavelength in nanometres (0 is the
        // d line): the Cauchy relation through n_d with the n_F - n_C = (n_d - 1) / V_d of the Abbe number
        static double Eta(int interfaceIndex, double wavelength, Float[] abbeNumbers)
        {
            double nd = RealisticCameraPolarisedTests.doubleGauss[interfaceIndex * 4 + 2];
            if (nd == 0)
            {
                return 1;
            }

            if (abbeNumbers == null || abbeNumbers[interfaceIndex] == 0 || wavelength <= 0)
            {
                return nd;
            }

            double b = ((nd - 1) / abbeNumbers[interfaceIndex]) / (1 / (0.4861327 * 0.4861327) - 1 / (0.6562725 * 0.6562725));
            double l = wavelength * 1e-3;
            return nd + b * (1 / (l * l) - 1 / (0.5875618 * 0.5875618));
        }

        // The refraction of light through the lens, found with bisection for the intersections and finite differences for the
        // normals, written without the camera's code. The light starts at the point pCamera of the front element (camera space)
        // with the direction travel. Returns false for total internal reflection or a missed interface (the apertures are not
        // tested).
        internal static bool Trace(Point3f pCamera, Vector3f travelCamera, bool aspheric, Float wavelength, Float[] abbeNumbers,
                                   out Vector3f exitCamera)
        {
            exitCamera = default;
            Float[] lens = RealisticCameraPolarisedTests.doubleGauss;
            // Lens space has the z axis the other way round
            double[] p = { pCamera.x, pCamera.y, -pCamera.z };
            double[] d = { travelCamera.x, travelCamera.y, -travelCamera.z };
            double r0 = Math.Sqrt(p[0] * p[0] + p[1] * p[1]);
            double elementZ = p[2] - Sag(0, r0, aspheric);
            int count = lens.Length / 4;
            for (int i = 0; i < count; ++i)
            {
                double radius = lens[i * 4] * 0.001;
                double thickness = lens[i * 4 + 1] * 0.001;
                if (radius == 0)
                {
                    double tStop = (elementZ - p[2]) / d[2];
                    p = new double[] { p[0] + tStop * d[0], p[1] + tStop * d[1], elementZ };
                    elementZ += thickness;
                    continue;
                }

                if (i > 0)
                {
                    double low = 0;
                    double high = (elementZ + 0.02 - p[2]) / d[2];
                    for (int iteration = 0; iteration < 200; ++iteration)
                    {
                        double middle = 0.5 * (low + high);
                        double x = p[0] + middle * d[0];
                        double y = p[1] + middle * d[1];
                        double f = p[2] + middle * d[2] - elementZ - Sag(i, Math.Sqrt(x * x + y * y), aspheric);
                        if (f < 0)
                        {
                            low = middle;
                        }
                        else
                        {
                            high = middle;
                        }
                    }

                    double t = 0.5 * (low + high);
                    p = new double[] { p[0] + t * d[0], p[1] + t * d[1], p[2] + t * d[2] };
                }

                double r = Math.Sqrt(p[0] * p[0] + p[1] * p[1]);
                double radial = r > 0 ? Slope(i, r, aspheric) / r : 0;
                double nx = -radial * p[0];
                double ny = -radial * p[1];
                double nz = 1;
                double nLength = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                nx /= nLength;
                ny /= nLength;
                nz /= nLength;
                // The normal faces the ray
                double cosIn = -(nx * d[0] + ny * d[1] + nz * d[2]);
                if (cosIn < 0)
                {
                    nx = -nx;
                    ny = -ny;
                    nz = -nz;
                    cosIn = -cosIn;
                }

                double etaIn = i == 0 ? 1 : Eta(i - 1, wavelength, abbeNumbers);
                double etaOut = Eta(i, wavelength, abbeNumbers);
                double eta = etaOut / etaIn;
                double sin2Out = (1 - cosIn * cosIn) / (eta * eta);
                if (sin2Out >= 1)
                {
                    return false;
                }

                double cosOut = Math.Sqrt(1 - sin2Out);
                double scale = cosIn / eta - cosOut;
                double ex = d[0] / eta + nx * scale;
                double ey = d[1] / eta + ny * scale;
                double ez = d[2] / eta + nz * scale;
                double eLength = Math.Sqrt(ex * ex + ey * ey + ez * ez);
                d = new double[] { ex / eLength, ey / eLength, ez / eLength };
                elementZ += thickness;
            }

            exitCamera = pbrt.Normalize(new Vector3f((Float)d[0], (Float)d[1], (Float)(-d[2])));
            return true;
        }

        [TEST("RealisticCameraAspheric", "TraceMatchesBisection")]
        public static void RealisticCameraAspheric_TraceMatchesBisection()
        {
            // The direction at which light reaches the film, through aspheric interfaces: the camera's Newton iteration against
            // an independent bisection
            Point2i resolution = new Point2i(64, 64);
            RealisticCamera camera = RealisticCameraPolarisedTests.MakeCamera(resolution, "realisticcameraaspheric_test.exr",
                                                                              AsphericParameters());
            RNG rng = new RNG(21);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            int tested = 0;
            for (int i = 0; i < 3000; ++i)
            {
                Point3f reference = new Point3f(1.2f * (rng.Uniform<Float>() - 0.5f), 0.9f * (rng.Uniform<Float>() - 0.5f),
                                                1.5f + 3 * rng.Uniform<Float>());
                Interaction referenceInteraction = new Interaction(reference, new Normal3f(0, 0, -1), 0, null);
                pstd.optional<CameraWiSample> sample = camera.SampleWi(referenceInteraction,
                    new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), ref lambda);
                if (!sample.has_value())
                {
                    continue;
                }

                Point3f pLens = sample.value().pLens.p();
                Ray ray = new Ray(pLens, -sample.value().wi);
                if (!camera.PolarisedSensor(ray, 0, out PolarisedLightSystem.Attenuation optics, out Vector3f sensorDirection,
                                            out Vector3f sensorUp))
                {
                    continue;
                }

                if (!Trace(pLens, -ray.d, true, 0, null, out Vector3f expected))
                {
                    continue;
                }

                EXPECT_NEAR(expected.x, sensorDirection.x, 5e-5f, "x, sample " + i);
                EXPECT_NEAR(expected.y, sensorDirection.y, 5e-5f, "y, sample " + i);
                EXPECT_NEAR(expected.z, sensorDirection.z, 5e-5f, "z, sample " + i);
                ++tested;
            }

            EXPECT_GT(tested, 500);
        }

        [TEST("RealisticCameraAspheric", "SphereIsTheLimit")]
        public static void RealisticCameraAspheric_SphereIsTheLimit()
        {
            // Interfaces whose aspheric terms are negligible take the Newton path but must give the rays of the spheres
            Point2i resolution = new Point2i(64, 48);
            RealisticCamera spheres = RealisticCameraPolarisedTests.MakeCamera(resolution);
            RealisticCamera nearlySpheres = RealisticCameraPolarisedTests.MakeCamera(resolution, "realisticcameraaspheric_test.exr",
                                                                                     NearlySphericalParameters());
            RNG rng = new RNG(8);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);

            // The light that reaches the film through the interfaces, which does not depend on the focus
            int sensorTested = 0;
            for (int i = 0; i < 2000; ++i)
            {
                Point3f reference = new Point3f(1.2f * (rng.Uniform<Float>() - 0.5f), 0.9f * (rng.Uniform<Float>() - 0.5f),
                                                1.5f + 3 * rng.Uniform<Float>());
                Interaction referenceInteraction = new Interaction(reference, new Normal3f(0, 0, -1), 0, null);
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<CameraWiSample> sample = spheres.SampleWi(referenceInteraction, u, ref lambda);
                if (!sample.has_value())
                {
                    continue;
                }

                Ray ray = new Ray(sample.value().pLens.p(), -sample.value().wi);
                bool hitSpheres = spheres.PolarisedSensor(ray, 0, out PolarisedLightSystem.Attenuation opticsA, out Vector3f directionA,
                                                          out Vector3f upA);
                bool hitNearly = nearlySpheres.PolarisedSensor(ray, 0, out PolarisedLightSystem.Attenuation opticsB,
                                                               out Vector3f directionB, out Vector3f upB);
                EXPECT_EQ(hitSpheres, hitNearly);
                if (!hitSpheres || !hitNearly)
                {
                    continue;
                }

                EXPECT_NEAR(directionA.x, directionB.x, 1e-5f);
                EXPECT_NEAR(directionA.y, directionB.y, 1e-5f);
                EXPECT_NEAR(directionA.z, directionB.z, 1e-5f);
                ++sensorTested;
            }

            EXPECT_GT(sensorTested, 500);

            // The rays of the cameras: the Newton iteration is in double precision, and the sphere's in single, in the focus too
            // (the paraxial rays that find the cardinal points start a metre from the lens, where single precision gives 1e-7 m),
            // so the positions agree to a few microns
            int tested = 0;
            int mismatched = 0;
            for (int i = 0; i < 4000; ++i)
            {
                CameraSample sample = new CameraSample
                {
                    pFilm = new Point2f(resolution.x * rng.Uniform<Float>(), resolution.y * rng.Uniform<Float>()),
                    pLens = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                    time = 0,
                    filterWeight = 1
                };
                pstd.optional<CameraRay> a = spheres.GenerateRay(sample, ref lambda);
                pstd.optional<CameraRay> b = nearlySpheres.GenerateRay(sample, ref lambda);
                if (a.has_value() != b.has_value())
                {
                    ++mismatched;
                    continue;
                }

                if (!a.has_value())
                {
                    continue;
                }

                EXPECT_NEAR(a.value().ray.o.x, b.value().ray.o.x, 2e-5f);
                EXPECT_NEAR(a.value().ray.o.y, b.value().ray.o.y, 2e-5f);
                EXPECT_NEAR(a.value().ray.o.z, b.value().ray.o.z, 2e-5f);
                EXPECT_NEAR(a.value().ray.d.x, b.value().ray.d.x, 2e-4f);
                EXPECT_NEAR(a.value().ray.d.y, b.value().ray.d.y, 2e-4f);
                EXPECT_NEAR(a.value().ray.d.z, b.value().ray.d.z, 2e-4f);
                EXPECT_NEAR(a.value().weight[0], b.value().weight[0], 2e-3f * a.value().weight[0]);
                ++tested;
            }

            EXPECT_GT(tested, 2000);
            // Rays at the very edge of an aperture may differ
            EXPECT_LT(mismatched, 5);
        }

        [TEST("RealisticCameraAspheric", "FrontElementAreaAndNormal")]
        public static void RealisticCameraAspheric_FrontElementAreaAndNormal()
        {
            // The area density of the points that SampleWi() chooses, against the area of the front element by numerical
            // integration of 2 pi r sqrt(1 + sag'^2), and the normals of the points against finite differences
            Point2i resolution = new Point2i(64, 64);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            for (int variant = 0; variant < 2; ++variant)
            {
                RealisticCamera camera = RealisticCameraPolarisedTests.MakeCamera(
                    resolution, "realisticcameraaspheric_test.exr", variant == 0 ? null : AsphericParameters());
                double frontRadius = RealisticCameraPolarisedTests.doubleGauss[3] * 0.0005;
                int steps = 20000;
                double area = 0;
                for (int i = 0; i < steps; ++i)
                {
                    double r = (i + 0.5) * frontRadius / steps;
                    area += 2 * Math.PI * r * Math.Sqrt(1 + Math.Pow(Slope(0, r, variant == 1), 2)) * frontRadius / steps;
                }

                RNG rng = new RNG(4);
                Point3f reference = new Point3f(0.1f, -0.05f, 3);
                Interaction referenceInteraction = new Interaction(reference, new Normal3f(0, 0, -1), 0, null);
                int tested = 0;
                for (int i = 0; i < 200; ++i)
                {
                    pstd.optional<CameraWiSample> sample = camera.SampleWi(referenceInteraction,
                        new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), ref lambda);
                    if (!sample.has_value())
                    {
                        continue;
                    }

                    Interaction lens = sample.value().pLens;
                    Float dist = pbrt.Length(lens.p() - reference);
                    double sampledArea = (double)dist * dist / (pbrt.AbsDot(lens.n, sample.value().wi) * sample.value().pdf);
                    EXPECT_NEAR(area, sampledArea, 2e-4 * area, "area, variant " + variant);
                    Vector3f expectedNormal = FrontNormal(lens.p().x, lens.p().y, variant == 1);
                    EXPECT_NEAR(1.0f, pbrt.Dot(expectedNormal, new Vector3f(lens.n.x, lens.n.y, lens.n.z)), 1e-4f,
                                "normal, variant " + variant);
                    ++tested;
                }

                EXPECT_GT(tested, 100);
            }
        }

        [TEST("RealisticCameraAspheric", "ImportanceMatchesGenerateRay")]
        public static void RealisticCameraAspheric_ImportanceMatchesGenerateRay()
        {
            // The weight of GenerateRay() is We cos(theta) / (pdfPos pdfDir) with the normal of the aspheric front element
            Point2i resolution = new Point2i(64, 48);
            RealisticCamera camera = RealisticCameraPolarisedTests.MakeCamera(resolution, "realisticcameraaspheric_test.exr",
                                                                              AsphericParameters());
            RNG rng = new RNG(3);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            int tested = 0;
            for (int i = 0; i < 4000; ++i)
            {
                CameraSample sample = new CameraSample
                {
                    pFilm = new Point2f(resolution.x * rng.Uniform<Float>(), resolution.y * rng.Uniform<Float>()),
                    pLens = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                    time = 0,
                    filterWeight = 1
                };
                pstd.optional<CameraRay> cameraRay = camera.GenerateRay(sample, ref lambda);
                if (!cameraRay.has_value())
                {
                    continue;
                }

                Ray ray = cameraRay.value().ray;
                Point2f pRaster;
                SampledSpectrum we = camera.We(ray, ref lambda, &pRaster);
                Float pdfPos, pdfDir;
                camera.PDF_We(ray, &pdfPos, &pdfDir);
                if (we[0] == 0)
                {
                    continue;
                }

                EXPECT_NEAR(sample.pFilm.x, pRaster.x, 2e-3f * resolution.x);
                EXPECT_NEAR(sample.pFilm.y, pRaster.y, 2e-3f * resolution.y);
                Float cosTheta = std.abs(pbrt.Dot(FrontNormal(ray.o.x, ray.o.y), ray.d));
                Float expected = we[0] * cosTheta / (pdfPos * pdfDir);
                EXPECT_NEAR(cameraRay.value().weight[0], expected, 2e-3f * cameraRay.value().weight[0]);
                ++tested;
            }

            EXPECT_GT(tested, 2000);
        }

        [TEST("RealisticCameraAspheric", "ImportanceAndDensityIntegrals")]
        public static void RealisticCameraAspheric_ImportanceAndDensityIntegrals()
        {
            // Integrating over the rays that leave the aspheric front element (the area and the solid angle): the density gives
            // the fraction of the samples that GenerateRay() turns into rays, and the importance gives the mean of its weight
            Point2i resolution = new Point2i(64, 64);
            RealisticCamera camera = RealisticCameraPolarisedTests.MakeCamera(resolution, "realisticcameraaspheric_test.exr",
                                                                              AsphericParameters());
            RNG rng = new RNG(5);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);

            Float vertexZ = 0;
            bool haveVertex = false;
            int nPath = 400000;
            double pathSum = 0;
            double pathSum2 = 0;
            int nSuccess = 0;
            for (int i = 0; i < nPath; ++i)
            {
                CameraSample sample = new CameraSample
                {
                    pFilm = new Point2f(resolution.x * rng.Uniform<Float>(), resolution.y * rng.Uniform<Float>()),
                    pLens = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                    time = 0,
                    filterWeight = 1
                };
                pstd.optional<CameraRay> cameraRay = camera.GenerateRay(sample, ref lambda);
                Float w = 0;
                if (cameraRay.has_value())
                {
                    w = cameraRay.value().weight[0];
                    ++nSuccess;
                    if (!haveVertex)
                    {
                        Point3f o = cameraRay.value().ray.o;
                        // The vertex in lens space, from the hit on the surface
                        vertexZ = (Float)(-o.z - Sag(0, Math.Sqrt((double)o.x * o.x + (double)o.y * o.y)));
                        haveVertex = true;
                    }
                }

                pathSum += w;
                pathSum2 += (double)w * w;
            }

            double pathMean = pathSum / nPath;
            double pathError = Math.Sqrt((pathSum2 / nPath - pathMean * pathMean) / nPath);

            Float frontRadius = RealisticCameraPolarisedTests.doubleGauss[3] * 0.0005f;
            Float diskArea = pbrt.Pi * frontRadius * frontRadius;
            Float cosMax = std.cos(pbrt.Radians(45.0f));
            Float coneSolidAngle = 2 * pbrt.Pi * (1 - cosMax);
            int nRays = 1500000;
            double densitySum = 0;
            double densitySum2 = 0;
            double importanceSum = 0;
            double importanceSum2 = 0;
            for (int i = 0; i < nRays; ++i)
            {
                // Uniform on the disk of the aperture; the area of the surface is the area of the disk times sqrt(1 + sag'^2)
                Float r = frontRadius * std.sqrt(rng.Uniform<Float>());
                Float phiPoint = 2 * pbrt.Pi * rng.Uniform<Float>();
                Float x = r * std.cos(phiPoint);
                Float y = r * std.sin(phiPoint);
                Float z = (Float)(-(vertexZ + Sag(0, r)));
                Vector3f normalUnnormalised = new Vector3f((Float)(r > 0 ? Slope(0, r) / r * x : 0),
                                                           (Float)(r > 0 ? Slope(0, r) / r * y : 0), 1);
                double areaWeight = diskArea * pbrt.Length(normalUnnormalised);
                Vector3f normal = pbrt.Normalize(normalUnnormalised);
                Float cosTheta = 1 - rng.Uniform<Float>() * (1 - cosMax);
                Float sinTheta = std.sqrt(std.max(0.0f, 1 - cosTheta * cosTheta));
                Float phi = 2 * pbrt.Pi * rng.Uniform<Float>();
                Ray ray = new Ray(new Point3f(x, y, z), new Vector3f(sinTheta * std.cos(phi), sinTheta * std.sin(phi), cosTheta));
                Float pdfPos, pdfDir;
                camera.PDF_We(ray, &pdfPos, &pdfDir);
                SampledSpectrum we = camera.We(ray, ref lambda);
                double density = (double)pdfPos * pdfDir * areaWeight * coneSolidAngle;
                double importance = (double)we[0] * pbrt.AbsDot(normal, ray.d) * areaWeight * coneSolidAngle;
                densitySum += density;
                densitySum2 += density * density;
                importanceSum += importance;
                importanceSum2 += importance * importance;
            }

            double successFraction = (double)nSuccess / nPath;
            double successError = Math.Sqrt(successFraction * (1 - successFraction) / nPath);
            double densityMean = densitySum / nRays;
            double densityError = Math.Sqrt((densitySum2 / nRays - densityMean * densityMean) / nRays);
            double importanceMean = importanceSum / nRays;
            double importanceError = Math.Sqrt((importanceSum2 / nRays - importanceMean * importanceMean) / nRays);
            EXPECT_NEAR(successFraction, densityMean, 4 * Math.Sqrt(successError * successError + densityError * densityError),
                        "density integral " + densityMean + " +- " + densityError + ", fraction of samples " + successFraction);
            EXPECT_NEAR(pathMean, importanceMean, 4 * Math.Sqrt(pathError * pathError + importanceError * importanceError),
                        "path " + pathMean + " +- " + pathError + ", importance " + importanceMean + " +- " + importanceError);
        }
    }
}
