// (not in pbrt-v4) realisticcameradispersion_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class RealisticCameraDispersionTests
    {
        // The Abbe numbers of the glasses of the double Gauss lens, behind each interface of the lens file (0 for air and the stop)
        static readonly Float[] abbeNumbers = { 32, 0, 32, 30, 0, 0, 38, 33, 0, 33, 0 };

        static RealisticCamera MakeDispersiveCamera(Point2i resolution)
        {
            return RealisticCameraPolarisedTests.MakeCamera(resolution, "realisticcameradispersion_test.exr", null,
                                                            new List<Float>(abbeNumbers));
        }

        static SampledWavelengths HeroWavelength(Float wavelength)
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            lambda[0] = wavelength;
            return lambda;
        }

        [TEST("RealisticCameraDispersion", "TraceMatchesCauchy")]
        public static void RealisticCameraDispersion_TraceMatchesCauchy()
        {
            // The direction at which light of a wavelength reaches the film, against an independent trace with the Cauchy
            // relation of the Abbe numbers
            Point2i resolution = new Point2i(64, 64);
            RealisticCamera dispersive = MakeDispersiveCamera(resolution);
            RealisticCamera plain = RealisticCameraPolarisedTests.MakeCamera(resolution);
            RNG rng = new RNG(31);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            int tested = 0;
            int separated = 0;
            Float[] wavelengths = { 450, 550, 650 };
            for (int i = 0; i < 2000; ++i)
            {
                Point3f reference = new Point3f(1.2f * (rng.Uniform<Float>() - 0.5f), 0.9f * (rng.Uniform<Float>() - 0.5f),
                                                1.5f + 3 * rng.Uniform<Float>());
                Interaction referenceInteraction = new Interaction(reference, new Normal3f(0, 0, -1), 0, null);
                pstd.optional<CameraWiSample> sample = dispersive.SampleWi(referenceInteraction,
                    new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), ref lambda);
                if (!sample.has_value())
                {
                    continue;
                }

                Point3f pLens = sample.value().pLens.p();
                Ray ray = new Ray(pLens, -sample.value().wi);
                Vector3f[] directions = new Vector3f[3];
                bool all = true;
                for (int k = 0; k < 3; ++k)
                {
                    if (!dispersive.PolarisedSensor(ray, wavelengths[k], out PolarisedLightSystem.Attenuation optics,
                                                    out directions[k], out Vector3f sensorUp) ||
                        !RealisticCameraAsphericTests.Trace(pLens, -ray.d, false, wavelengths[k], abbeNumbers,
                                                            out Vector3f expected))
                    {
                        all = false;
                        break;
                    }

                    EXPECT_NEAR(expected.x, directions[k].x, 5e-5f, "x, sample " + i + " wavelength " + wavelengths[k]);
                    EXPECT_NEAR(expected.y, directions[k].y, 5e-5f, "y, sample " + i + " wavelength " + wavelengths[k]);
                    EXPECT_NEAR(expected.z, directions[k].z, 5e-5f, "z, sample " + i + " wavelength " + wavelengths[k]);
                }

                if (!all)
                {
                    continue;
                }

                // The wavelength 0 is the d line, which is that of a lens without dispersion
                if (dispersive.PolarisedSensor(ray, 0, out PolarisedLightSystem.Attenuation opticsD, out Vector3f directionD,
                                               out Vector3f upD) &&
                    plain.PolarisedSensor(ray, 0, out PolarisedLightSystem.Attenuation opticsPlain, out Vector3f directionPlain,
                                          out Vector3f upPlain))
                {
                    EXPECT_NEAR(directionD.x, directionPlain.x, 1e-5f);
                    EXPECT_NEAR(directionD.y, directionPlain.y, 1e-5f);
                }

                // Blue and red leave the lens in different directions
                if (pbrt.Length(directions[0] - directions[2]) > 1e-4f)
                {
                    ++separated;
                }

                ++tested;
            }

            EXPECT_GT(tested, 400);
            EXPECT_GT(separated, tested * 8 / 10);
        }

        [TEST("RealisticCameraDispersion", "HeroWavelength")]
        public static void RealisticCameraDispersion_HeroWavelength()
        {
            // A dispersive camera traces the first wavelength and terminates the others; a plain one leaves the sample alone
            Point2i resolution = new Point2i(64, 48);
            RealisticCamera dispersive = MakeDispersiveCamera(resolution);
            RealisticCamera plain = RealisticCameraPolarisedTests.MakeCamera(resolution);
            CameraSample sample = new CameraSample
            {
                pFilm = new Point2f(10.5f, 40.5f),
                pLens = new Point2f(0.45f, 0.55f),
                time = 0,
                filterWeight = 1
            };
            SampledWavelengths lambdaPlain = HeroWavelength(550);
            SampledWavelengths lambdaDispersive = HeroWavelength(550);
            plain.GenerateRay(sample, ref lambdaPlain);
            dispersive.GenerateRay(sample, ref lambdaDispersive);
            EXPECT_TRUE(!lambdaPlain.SecondaryTerminated());
            EXPECT_TRUE(lambdaDispersive.SecondaryTerminated());

            // The ray of a wavelength is the ray of that wavelength
            SampledWavelengths blue = HeroWavelength(450);
            SampledWavelengths red = HeroWavelength(650);
            pstd.optional<CameraRay> blueRay = dispersive.GenerateRay(sample, ref blue);
            pstd.optional<CameraRay> redRay = dispersive.GenerateRay(sample, ref red);
            EXPECT_TRUE(blueRay.has_value() && redRay.has_value());
            // Lateral and axial colour: the rays that start at the same film point and pupil point leave in different directions
            EXPECT_GT(pbrt.Length(blueRay.value().ray.d - redRay.value().ray.d), 1e-4f);

            // The termination happens when no ray comes out too
            CameraSample far = sample;
            far.pLens = new Point2f(0.0f, 0.0f);
            SampledWavelengths lambdaMiss = HeroWavelength(550);
            dispersive.GenerateRay(far, ref lambdaMiss);
            EXPECT_TRUE(lambdaMiss.SecondaryTerminated());
        }

        [TEST("RealisticCameraDispersion", "ImportanceMatchesGenerateRay")]
        public static void RealisticCameraDispersion_ImportanceMatchesGenerateRay()
        {
            // The weight of GenerateRay() is We cos(theta) / (pdfPos pdfDir). The density of PDF_We() has no wavelength: it is that
            // of the d line, so the two agree to the precision of the test at the d line, and to a few per cent at the ends of
            // the visible range (which only changes the weights of bdpt's strategies of multiple importance sampling, which sum
            // to one for any density, and not the expectation)
            Point2i resolution = new Point2i(64, 48);
            RealisticCamera camera = MakeDispersiveCamera(resolution);
            RNG rng = new RNG(3);
            Float[] wavelengths = { 587.5618f, 420, 680 };
            for (int w = 0; w < wavelengths.Length; ++w)
            {
                SampledWavelengths lambda = HeroWavelength(wavelengths[w]);
                int tested = 0;
                for (int i = 0; i < 3000; ++i)
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

                    // The film position of the ray is that of the sample, to the lateral colour of the lens
                    Float tolerance = w == 0 ? 2e-3f : 6e-2f;
                    EXPECT_NEAR(sample.pFilm.x, pRaster.x, (w == 0 ? 2e-3f : 2e-2f) * resolution.x);
                    EXPECT_NEAR(sample.pFilm.y, pRaster.y, (w == 0 ? 2e-3f : 2e-2f) * resolution.y);
                    Float cosTheta = std.abs(pbrt.Dot(RealisticCameraAsphericTestsFrontNormal(ray), ray.d));
                    Float expected = we[0] * cosTheta / (pdfPos * pdfDir);
                    EXPECT_NEAR(cameraRay.value().weight[0], expected, tolerance * cameraRay.value().weight[0],
                                "wavelength " + wavelengths[w]);
                    ++tested;
                }

                EXPECT_GT(tested, 1500);
            }
        }

        // The normal of the spherical front element of the double Gauss lens at the origin of a ray
        static Vector3f RealisticCameraAsphericTestsFrontNormal(Ray ray)
        {
            // The sphere of radius R of the lens file, with the centre on the axis at the side of the film
            Float radius = RealisticCameraPolarisedTests.doubleGauss[0] * 0.001f;
            Float rXY2 = ray.o.x * ray.o.x + ray.o.y * ray.o.y;
            Float zFromCentre = std.sqrt(radius * radius - rXY2);
            return pbrt.Normalize(new Vector3f(ray.o.x, ray.o.y, zFromCentre));
        }

        [TEST("RealisticCameraDispersion", "ImportanceIntegral")]
        public static void RealisticCameraDispersion_ImportanceIntegral()
        {
            // At a wavelength, the integral of the importance over the rays that leave the front element is the mean of the weight
            // of GenerateRay() (the irradiance of an environment of radiance one averaged over the film)
            Point2i resolution = new Point2i(64, 64);
            RealisticCamera camera = MakeDispersiveCamera(resolution);
            Float[] wavelengths = { 430, 680 };
            for (int w = 0; w < wavelengths.Length; ++w)
            {
                RNG rng = new RNG((ulong)(5 + w));
                SampledWavelengths lambda = HeroWavelength(wavelengths[w]);
                int nPath = 250000;
                double pathSum = 0;
                double pathSum2 = 0;
                Float vertexZ = 0;
                bool haveVertex = false;
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
                    Float weight = 0;
                    if (cameraRay.has_value())
                    {
                        weight = cameraRay.value().weight[0];
                        if (!haveVertex)
                        {
                            Point3f o = cameraRay.value().ray.o;
                            vertexZ = -o.z - (Float)RealisticCameraAsphericTestsSag(Math.Sqrt((double)o.x * o.x + (double)o.y * o.y));
                            haveVertex = true;
                        }
                    }

                    pathSum += weight;
                    pathSum2 += (double)weight * weight;
                }

                double pathMean = pathSum / nPath;
                double pathError = Math.Sqrt((pathSum2 / nPath - pathMean * pathMean) / nPath);

                Float radius = RealisticCameraPolarisedTests.doubleGauss[0] * 0.001f;
                Float frontRadius = RealisticCameraPolarisedTests.doubleGauss[3] * 0.0005f;
                Float cosAlphaMax = std.sqrt(1 - pbrt.Sqr(frontRadius / radius));
                Float capArea = 2 * pbrt.Pi * radius * radius * (1 - cosAlphaMax);
                Float cosMax = std.cos(pbrt.Radians(45.0f));
                Float coneSolidAngle = 2 * pbrt.Pi * (1 - cosMax);
                int nRays = 800000;
                double importanceSum = 0;
                double importanceSum2 = 0;
                for (int i = 0; i < nRays; ++i)
                {
                    Float cosAlpha = 1 - rng.Uniform<Float>() * (1 - cosAlphaMax);
                    Float sinAlpha = std.sqrt(1 - cosAlpha * cosAlpha);
                    Float phiPoint = 2 * pbrt.Pi * rng.Uniform<Float>();
                    Point3f origin = new Point3f(radius * sinAlpha * std.cos(phiPoint), radius * sinAlpha * std.sin(phiPoint),
                                                 -(vertexZ + radius - radius * cosAlpha));
                    Vector3f normal = new Vector3f(sinAlpha * std.cos(phiPoint), sinAlpha * std.sin(phiPoint), cosAlpha);
                    Float cosTheta = 1 - rng.Uniform<Float>() * (1 - cosMax);
                    Float sinTheta = std.sqrt(std.max(0.0f, 1 - cosTheta * cosTheta));
                    Float phi = 2 * pbrt.Pi * rng.Uniform<Float>();
                    Ray ray = new Ray(origin, new Vector3f(sinTheta * std.cos(phi), sinTheta * std.sin(phi), cosTheta));
                    SampledSpectrum we = camera.We(ray, ref lambda);
                    double importance = (double)we[0] * pbrt.AbsDot(normal, ray.d) * capArea * coneSolidAngle;
                    importanceSum += importance;
                    importanceSum2 += importance * importance;
                }

                double importanceMean = importanceSum / nRays;
                double importanceError = Math.Sqrt((importanceSum2 / nRays - importanceMean * importanceMean) / nRays);
                EXPECT_NEAR(pathMean, importanceMean, 4 * Math.Sqrt(pathError * pathError + importanceError * importanceError),
                            "wavelength " + wavelengths[w] + ": path " + pathMean + " +- " + pathError + ", importance " +
                            importanceMean + " +- " + importanceError);
            }
        }

        // The sag of the spherical front interface of the double Gauss lens (metres)
        static double RealisticCameraAsphericTestsSag(double r)
        {
            double radius = RealisticCameraPolarisedTests.doubleGauss[0] * 0.001;
            return radius - Math.Sign(radius) * Math.Sqrt(radius * radius - r * r);
        }

        [TEST("RealisticCameraDispersion", "BDPTMatchesVolPath")]
        public static void RealisticCameraDispersion_BDPTMatchesVolPath()
        {
            // The hero wavelength of the sample goes through the camera and the light subpaths of bdpt: the mean of the image of the
            // closed emissive sphere is that of volpath
            List<Float> abbe = new List<Float>(abbeNumbers);
            double volpath = RealisticCameraPolarisedTests.RenderMean(false, abbe);
            double bdpt = RealisticCameraPolarisedTests.RenderMean(true, abbe);
            EXPECT_GT(volpath, 0.0);
            EXPECT_NEAR(volpath, bdpt, 0.015 * volpath, "volpath " + volpath + ", bdpt " + bdpt);
        }
    }
}
