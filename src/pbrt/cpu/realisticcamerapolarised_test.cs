// (not in pbrt-v4) realisticcamerapolarised_test.cs

using System;
using System.Collections.Generic;
using testing;
using PLight = pbrt.PolarisedLightSystem.Light;

namespace pbrt
{
    public static unsafe class RealisticCameraPolarisedTests
    {
        // The double Gauss lens of pbrt's dgauss.50mm.dat: radius, thickness, index of refraction and aperture diameter in
        // millimetres of each interface from the scene to the film (radius 0 is the aperture stop)
        internal static readonly Float[] doubleGauss =
        {
            29.475f, 3.76f, 1.67f, 25.2f,
            84.83f, 0.12f, 1.0f, 25.2f,
            19.275f, 4.025f, 1.67f, 23.0f,
            40.77f, 3.275f, 1.699f, 23.0f,
            12.75f, 5.705f, 1.0f, 18.0f,
            0.0f, 4.5f, 0.0f, 17.1f,
            -14.495f, 1.18f, 1.603f, 17.0f,
            40.77f, 6.065f, 1.658f, 20.0f,
            -20.385f, 0.19f, 1.0f, 20.0f,
            437.065f, 4.375f, 1.658f, 20.0f,
            -39.73f, 0.0f, 1.0f, 20.0f
        };

        internal static RealisticCamera MakeCamera(Point2i resolution, string filename = "realisticcamerapolarised_test.exr",
                                                   List<Float> asphericParameters = null)
        {
            Transform id = new Transform();
            AnimatedTransform identityA = new AnimatedTransform(id, 0, id, 1);
            Filter filter = new BoxFilter(new Vector2f(0.5f, 0.5f));
            FilmBaseParameters fp = new FilmBaseParameters(resolution, new Bounds2i(new Point2i(0, 0), resolution), filter, 35.0f,
                                                           PixelSensor.CreateDefault(), filename);
            RGBFilm rgbFilm = new RGBFilm(fp, RGBColorSpace.sRGB);
            CameraBaseParameters cbp = new CameraBaseParameters(new CameraTransform(identityA), rgbFilm, null,
                                                                new ParameterDictionary(), new FileLoc());
            List<Float> lensParameters = new List<Float>(doubleGauss);
            return new RealisticCamera(cbp, lensParameters, 5.0f, 17.1f, new Image((Allocator)null), null, asphericParameters);
        }

        // The refraction of a ray at a spherical interface of a lens in lens space, written without the camera's code: the
        // interface of radius radius and centre z centreZ, the media indices etaIn and etaOut. Returns false for total
        // internal reflection or a miss.
        static bool Interface(ref Point3f o, ref Vector3f d, Float radius, Float centreZ, Float etaIn, Float etaOut,
                              out Vector3f normal, out Vector3f directionIn, out Vector3f directionOut)
        {
            normal = default;
            directionIn = d;
            directionOut = default;
            Vector3f oc = new Vector3f(o.x, o.y, o.z - centreZ);
            Float b = pbrt.Dot(oc, d);
            Float c = pbrt.Dot(oc, oc) - radius * radius;
            Float discriminant = b * b - c;
            if (discriminant < 0)
            {
                return false;
            }

            Float root = std.sqrt(discriminant);
            Float t0 = -b - root;
            Float t1 = -b + root;
            bool closer = (d.z > 0) ^ (radius < 0);
            Float t = closer ? std.min(t0, t1) : std.max(t0, t1);
            if (t < 0)
            {
                return false;
            }

            Point3f hit = new Point3f(o.x + t * d.x, o.y + t * d.y, o.z + t * d.z);
            normal = pbrt.Normalize(new Vector3f(hit.x, hit.y, hit.z - centreZ));
            if (pbrt.Dot(normal, d) > 0)
            {
                normal = -normal;
            }

            Float eta = etaOut / etaIn;
            Float cosIn = pbrt.Dot(normal, -d);
            Float sin2Out = (1 - cosIn * cosIn) / (eta * eta);
            if (sin2Out >= 1)
            {
                return false;
            }

            Float cosOut = std.sqrt(1 - sin2Out);
            directionOut = pbrt.Normalize(d * (1 / eta) + normal * (cosIn / eta - cosOut));
            o = hit;
            d = directionOut;
            return true;
        }

        // Light of the given Stokes vector in the frame of the first interface through the lens, as a Jones calculus of the
        // electric field in three dimensions. The field is real because the dielectric transmission has no phase shift. The
        // result is the Stokes vector in the frame of the last interface.
        static bool JonesChain(Vector3f lightDirection, Point3f origin, Float frontZ, Float stokesS1, Float stokesS2,
                               out Float outS0, out Float outS1, out Float outS2, out PolarisationFrame firstFrame)
        {
            outS0 = 0;
            outS1 = 0;
            outS2 = 0;
            firstFrame = default;
            // Lens space has the z axis the other way round
            Point3f o = new Point3f(origin.x, origin.y, -origin.z);
            Vector3f d = new Vector3f(lightDirection.x, lightDirection.y, -lightDirection.z);
            Float elementZ = frontZ;
            bool haveField = false;
            Vector3f field = default;
            Vector3f lastS = default;
            Vector3f lastPOut = default;
            for (int i = 0; i < doubleGauss.Length; i += 4)
            {
                Float radius = doubleGauss[i] * 0.001f;
                Float thickness = doubleGauss[i + 1] * 0.001f;
                if (radius == 0)
                {
                    Float tStop = (elementZ - o.z) / d.z;
                    o = new Point3f(o.x + tStop * d.x, o.y + tStop * d.y, elementZ);
                    elementZ += thickness;
                    continue;
                }

                Float etaIn = i == 0 || doubleGauss[i - 4 + 2] == 0 ? 1 : doubleGauss[i - 4 + 2];
                Float etaOut = doubleGauss[i + 2] == 0 ? 1 : doubleGauss[i + 2];
                if (!Interface(ref o, ref d, radius, elementZ + radius, etaIn, etaOut, out Vector3f normal,
                               out Vector3f directionIn, out Vector3f directionOut))
                {
                    return false;
                }

                Float cosIn = pbrt.Dot(normal, -directionIn);
                Float eta = etaOut / etaIn;
                Float cosOut = -pbrt.Dot(normal, directionOut);
                Float rs = (cosIn - eta * cosOut) / (cosIn + eta * cosOut);
                Float rp = (eta * cosIn - cosOut) / (eta * cosIn + cosOut);
                Float transmittanceS = 1 - rs * rs;
                Float transmittanceP = 1 - rp * rp;
                // s is perpendicular to the plane of incidence, p completes the right handed frame with the direction
                Vector3f s = pbrt.Normalize(pbrt.Cross(normal, directionIn));
                Vector3f pIn = pbrt.Cross(directionIn, s);
                Vector3f pOut = pbrt.Cross(directionOut, s);
                if (!haveField)
                {
                    haveField = true;
                    // The Stokes frame of the first interface, in render space (z flipped back)
                    Vector3f x = new Vector3f(s.x, s.y, -s.z);
                    Vector3f y = new Vector3f(pIn.x, pIn.y, -pIn.z);
                    Vector3f z = new Vector3f(directionIn.x, directionIn.y, -directionIn.z);
                    firstFrame = new PolarisationFrame(x, y, z);
                    // Linear polarisation at angle chi to the x axis: S1 = cos 2chi. The Stokes vectors follow Mitsuba 3 and PolarisedUPBP, whose
                    // handedness is the opposite of the textbook one: S2 = -sin 2chi
                    Float chi2 = std.atan2(-stokesS2, stokesS1);
                    field = s * std.cos(0.5f * chi2) + pIn * std.sin(0.5f * chi2);
                }

                Float fieldS = pbrt.Dot(field, s);
                Float fieldP = pbrt.Dot(field, pIn);
                field = s * (std.sqrt(transmittanceS) * fieldS) + pOut * (std.sqrt(transmittanceP) * fieldP);
                lastS = s;
                lastPOut = pOut;
                elementZ += thickness;
            }

            Float ex = pbrt.Dot(field, lastS);
            Float ey = pbrt.Dot(field, lastPOut);
            outS0 = ex * ex + ey * ey;
            outS1 = ex * ex - ey * ey;
            outS2 = -2 * ex * ey;
            return true;
        }

        static Float FrontVertexZ(Point3f hit)
        {
            // The first interface of the lens: the hit is on its sphere
            Float radius = doubleGauss[0] * 0.001f;
            Float r2 = hit.x * hit.x + hit.y * hit.y;
            Float centreZ = -hit.z + (radius > 0 ? 1.0f : -1.0f) * std.sqrt(radius * radius - r2);
            return centreZ - radius;
        }

        // The normal of the front element, towards the scene, at a point of it in camera space, which is lens space with z flipped
        static Vector3f FrontNormal(Point3f p, Float vertexZ)
        {
            Float radius = doubleGauss[0] * 0.001f;
            Vector3f fromCentre = new Vector3f(p.x, p.y, -p.z - (vertexZ + radius));
            Vector3f nLens = fromCentre * (1 / radius);
            return pbrt.Normalize(new Vector3f(nLens.x, nLens.y, -nLens.z));
        }

        [TEST("RealisticCameraPolarised", "LensMuellerMatchesJones")]
        public static void RealisticCameraPolarised_LensMuellerMatchesJones()
        {
            Point2i resolution = new Point2i(64, 64);
            RealisticCamera camera = MakeCamera(resolution);
            RNG rng = new RNG(11);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            int tested = 0;
            for (int i = 0; i < 400; ++i)
            {
                CameraSample sample = new CameraSample
                {
                    pFilm = new Point2f(64 * rng.Uniform<Float>(), 64 * rng.Uniform<Float>()),
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
                if (!camera.PolarisedSensor(ray, out PolarisedLightSystem.Attenuation optics, out Vector3f sensorDirection,
                                            out Vector3f sensorUp))
                {
                    continue;
                }

                // The start of the independent trace: as PolarisedSensor() backs the origin up against the light direction
                Vector3f lightDirection = pbrt.Normalize(-ray.d);
                Float vertexZ = FrontVertexZ(ray.o);
                Point3f origin = new Point3f(ray.o.x + 1e-3f * ray.d.x, ray.o.y + 1e-3f * ray.d.y, ray.o.z + 1e-3f * ray.d.z);

                Float[,] states = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
                for (int k = 0; k < 4; ++k)
                {
                    if (!JonesChain(lightDirection, origin, vertexZ, states[k, 0], states[k, 1], out Float s0, out Float s1,
                                    out Float s2, out PolarisationFrame firstFrame))
                    {
                        continue;
                    }

                    PLight light = new PLight(new StokesVector(new SampledSpectrum(1.0f), new SampledSpectrum(states[k, 0]),
                                                               new SampledSpectrum(states[k, 1]), new SampledSpectrum(0.0f)));
                    light = light.InDirection(firstFrame.z, firstFrame.x);
                    StokesVector measured = (optics * light).ToStokesVector();
                    EXPECT_NEAR(s0, measured.s0[0], 2e-4f, "S0, sample " + i + " state " + k);
                    EXPECT_NEAR(s1, measured.s1[0], 2e-4f, "S1, sample " + i + " state " + k);
                    EXPECT_NEAR(s2, measured.s2[0], 2e-4f, "S2, sample " + i + " state " + k);
                    EXPECT_TRUE(measured.s0[0] <= 1.0f);
                    ++tested;
                }

                EXPECT_NEAR(1.0f, pbrt.Length(sensorDirection), 1e-4f);
                EXPECT_TRUE(sensorDirection.z < 0);
                EXPECT_NEAR(0.0f, sensorUp.x, 1e-6f);
                EXPECT_NEAR(-1.0f, sensorUp.y, 1e-6f);
                EXPECT_NEAR(0.0f, sensorUp.z, 1e-6f);
            }

            EXPECT_GT(tested, 400);
        }

        [TEST("RealisticCameraPolarised", "ImportanceMatchesGenerateRay")]
        public static void RealisticCameraPolarised_ImportanceMatchesGenerateRay()
        {
            // The weight of GenerateRay() is We cos(theta) / (pdfPos pdfDir) of the ray, and the film position of the ray is the
            // sample's
            Point2i resolution = new Point2i(64, 48);
            RealisticCamera camera = MakeCamera(resolution);
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
                    // A ray on the very edge of an aperture may miss on the way back
                    continue;
                }

                EXPECT_NEAR(sample.pFilm.x, pRaster.x, 2e-3f * resolution.x);
                EXPECT_NEAR(sample.pFilm.y, pRaster.y, 2e-3f * resolution.y);
                Float cosTheta = std.abs(pbrt.Dot(FrontNormal(ray.o, FrontVertexZ(ray.o)), ray.d));
                Float expected = we[0] * cosTheta / (pdfPos * pdfDir);
                EXPECT_NEAR(cameraRay.value().weight[0], expected, 2e-3f * cameraRay.value().weight[0]);
                ++tested;
            }

            EXPECT_GT(tested, 2000);
        }

        [TEST("RealisticCameraPolarised", "ImportanceAndDensityIntegrals")]
        public static void RealisticCameraPolarised_ImportanceAndDensityIntegrals()
        {
            // Integrating over the rays that leave the front element (the projected area and the solid angle): the density
            // gives one, and the importance gives the mean of the weight of GenerateRay(), the irradiance of an environment of
            // radiance one averaged over the film
            Point2i resolution = new Point2i(64, 64);
            RealisticCamera camera = MakeCamera(resolution);
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
                        vertexZ = FrontVertexZ(cameraRay.value().ray.o);
                        haveVertex = true;
                    }
                }

                pathSum += w;
                pathSum2 += (double)w * w;
            }

            double pathMean = pathSum / nPath;
            double pathError = Math.Sqrt((pathSum2 / nPath - pathMean * pathMean) / nPath);

            Float radius = doubleGauss[0] * 0.001f;
            Float frontRadius = doubleGauss[3] * 0.0005f;
            Float cosAlphaMax = std.sqrt(1 - pbrt.Sqr(frontRadius / radius));
            Float capArea = 2 * pbrt.Pi * radius * radius * (1 - cosAlphaMax);
            Float cosMax = std.cos(pbrt.Radians(45.0f));
            Float coneSolidAngle = 2 * pbrt.Pi * (1 - cosMax);
            int nRays = 1500000;
            double densitySum = 0;
            double densitySum2 = 0;
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
                Float pdfPos, pdfDir;
                camera.PDF_We(ray, &pdfPos, &pdfDir);
                SampledSpectrum we = camera.We(ray, ref lambda);
                double density = (double)pdfPos * pdfDir * capArea * coneSolidAngle;
                double importance = (double)we[0] * pbrt.AbsDot(normal, ray.d) * capArea * coneSolidAngle;
                densitySum += density;
                densitySum2 += density * density;
                importanceSum += importance;
                importanceSum2 += importance * importance;
            }

            // The density is that of the rays that GenerateRay() produces, which is a part of all samples
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

        // The mean of the image that an integrator renders of the unit sphere with diffuse walls (Kd = 0.5) that emit (Le = 0.5),
        // seen from its centre through the lens
        static double RenderMean(bool bidirectional)
        {
            Transform identity = new Transform();
            Shape sphere = new Sphere(identity, identity, true, 1, -1, 1, 360);
            Material material = new DiffuseMaterial(new SpectrumConstantTexture(new ConstantSpectrum(0.5f)), null, null);
            ConstantSpectrum Le = new ConstantSpectrum(1);
            Float scale = 0.5f / pbrt.SpectrumToPhotometric(Le);
            Light areaLight = new DiffuseAreaLight(identity, new MediumInterface(), Le, scale, sphere, null, new Image(), null, false);
            List<Light> lights = new List<Light>();
            lights.Add(areaLight);
            List<Primitive> prims = new List<Primitive>();
            prims.Add(new GeometricPrimitive(sphere, material, areaLight, new MediumInterface()));
            Primitive aggregate = new BVHAggregate(prims);

            Point2i resolution = new Point2i(8, 8);
            string filename = bidirectional ? "realisticcamera_bdpt_test.exr" : "realisticcamera_volpath_test.exr";
            RealisticCamera camera = MakeCamera(resolution, filename);
            Sampler sampler = new ZSobolSampler(256, resolution, RandomizeStrategy.PermuteDigits);
            Integrator integrator = bidirectional
                ? new BDPTIntegrator(camera, sampler, aggregate, lights, 6, false, false, false)
                : (Integrator)new VolPathIntegrator(8, camera, sampler, aggregate, lights);
            integrator.Render();

            ImageAndMetadata im = Image.Read(filename);
            double sum = 0;
            for (int y = 0; y < im.image.Resolution().y; ++y)
            {
                for (int x = 0; x < im.image.Resolution().x; ++x)
                {
                    for (int c = 0; c < 3; ++c)
                    {
                        sum += im.image.GetChannel(new Point2i(x, y), c);
                    }
                }
            }

            System.IO.File.Delete(filename);
            return sum / (im.image.Resolution().x * im.image.Resolution().y * 3);
        }

        [TEST("RealisticCameraPolarised", "BDPTMatchesVolPath")]
        public static void RealisticCameraPolarised_BDPTMatchesVolPath()
        {
            // The lens system loses part of the samples of the film: bdpt still has to connect the light subpaths of those
            // samples to the camera. In the closed emissive sphere the strategies that sample the camera carry a large part of
            // the image, so a bdpt without them would be dark by a lot more than the tolerance
            double volpath = RenderMean(false);
            double bdpt = RenderMean(true);
            EXPECT_GT(volpath, 0.0);
            EXPECT_NEAR(volpath, bdpt, 0.015 * volpath, "volpath " + volpath + ", bdpt " + bdpt);
        }
    }
}
