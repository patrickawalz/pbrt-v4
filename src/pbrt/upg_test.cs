// (not in pbrt-v4) upg_test.cs: the tests of unbiased photon gathering (cpu/upg.cs, docs/upg.md)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using testing;

namespace pbrt
{
    public static unsafe class UpgTests
    {
        static BSDF DiffuseBSDF()
        {
            return new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), new DiffuseBxDF(new SampledSpectrum(0.5f)));
        }

        static BSDF ConductorBSDF(Float alphaX, Float alphaY)
        {
            return new BSDF(new Normal3f(0, 0, 1), new Vector3f(1, 0, 0),
                            new ConductorBxDF(new TrowbridgeReitzDistribution(alphaX, alphaY), new SampledSpectrum(0.2f),
                                              new SampledSpectrum(3.9f)));
        }

        // The u numbers inside the bound (u1 wraps)
        static bool InBound(in UpgBound bound, Point2f u)
        {
            if (u.x < bound.u0Lo || u.x > bound.u0Hi)
            {
                return false;
            }
            Float d = u.y - bound.u1Lo;
            if (d < 0)
            {
                d += 1;
            }
            return d <= bound.u1Len;
        }

        // The bound contains every random number whose sample is in the cone, and its area is the probability that a random
        // number is in it. Returns the number of numbers in the cone that the bound misses, and the tightness P(cone) / p_b.
        static void CheckBound(BSDF bsdf, Vector3f wo, Point3f origin, Point3f center, Float radius, int n, string label,
                               out int missed, out Float tightness, out Float pb)
        {
            UpgBound bound = UpgBound.Compute(bsdf, wo, origin, center, radius);
            Vector3f axis = Normalize(center - origin);
            Float distance = Length(center - origin);
            Float cosA = MathF.Sqrt(1 - Sqr(radius / distance));
            RNG rng = new RNG(7, 11);
            missed = 0;
            int inCone = 0, inBound = 0;
            for (int i = 0; i < n; ++i)
            {
                Float uc = rng.Uniform<Float>();
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                if (InBound(bound, u))
                {
                    ++inBound;
                }
                pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                if (bs && Dot(bs.value().wi, axis) >= cosA)
                {
                    ++inCone;
                    if (!InBound(bound, u))
                    {
                        ++missed;
                    }
                }
            }
            pb = bound.Area();
            // The area is the probability that a uniform pair is in the bound: the empirical frequency agrees within 4 standard errors
            Float frequency = (Float)inBound / n;
            Float standardError = MathF.Sqrt(Math.Max(pb * (1 - pb), 1e-12f) / n);
            EXPECT_TRUE(MathF.Abs(frequency - pb) <= 4 * standardError + 1e-6f);
            tightness = inBound > 0 ? (Float)inCone / inBound : 0;
            Console.WriteLine("[Upg] bound " + label + ": p_b " + pb.ToString("G5", CultureInfo.InvariantCulture) + ", empirical frequency of the numbers in the bound " +
                              ((Float)inBound / n).ToString("G5", CultureInfo.InvariantCulture) + ", in the cone " + inCone + ", missed by the bound " + missed +
                              ", tightness " + tightness.ToString("G4", CultureInfo.InvariantCulture));
        }

        static List<(string, BSDF)> BoundCases()
        {
            List<(string, BSDF)> cases = new List<(string, BSDF)>();
            cases.Add(("diffuse", DiffuseBSDF()));
            cases.Add(("conductor alpha 0.05", ConductorBSDF(0.05f, 0.05f)));
            cases.Add(("conductor alpha 0.3", ConductorBSDF(0.3f, 0.3f)));
            cases.Add(("conductor alpha 0.3 x 0.1", ConductorBSDF(0.3f, 0.1f)));
            cases.Add(("conductor alpha 0.8", ConductorBSDF(0.8f, 0.8f)));
            return cases;
        }

        [TEST("Upg", "AngularBoundContainsTheCone")]
        public static void Upg_AngularBoundContainsTheCone()
        {
            Point3f origin = new Point3f(0, 0, 0);
            Vector3f[] wos = new Vector3f[] { Normalize(new Vector3f(0.3f, 0.2f, 0.8f)), Normalize(new Vector3f(-0.7f, 0.1f, 0.3f)),
                                              Normalize(new Vector3f(0, 0, 1)), Normalize(new Vector3f(0.4f, -0.3f, -0.8f)) };
            Point3f[] centers = new Point3f[] { new Point3f(0.5f, 0.2f, 1.2f), new Point3f(-0.9f, 0.6f, 0.4f), new Point3f(0.1f, -0.2f, 2.0f),
                                                new Point3f(0.0f, 0.0f, 3.0f), new Point3f(1.5f, 1.5f, 0.3f), new Point3f(0.9f, -0.9f, 0.05f), new Point3f(0.3f, 0.2f, -1.5f) };
            Float[] radii = new Float[] { 0.05f, 0.2f, 0.5f };
            int totalMissed = 0, cases = 0;
            foreach ((string name, BSDF bsdf) in BoundCases())
            {
                foreach (Vector3f wo in wos)
                {
                    foreach (Point3f center in centers)
                    {
                        foreach (Float radius in radii)
                        {
                            CheckBound(bsdf, wo, origin, center, radius, 200000, name + " wo " + wo + " center " + center + " r " + radius,
                                       out int missed, out Float tightness, out Float pb);
                            totalMissed += missed;
                            ++cases;
                        }
                    }
                }
            }
            Console.WriteLine("[Upg] angular bound: " + cases + " configurations, numbers in the cone that the bound misses: " + totalMissed);
            EXPECT_EQ(totalMissed, 0);
        }

        // ------------------------------------------------------------------------------------------
        // A scene in the process: the integrator, the camera and a sampler, for the tests that look inside the technique

        sealed class Fixture
        {
            public UpgIntegrator integrator;
            public Camera camera;
            public Sampler sampler;
            public ScratchBuffer scratch = new ScratchBuffer(65536);

            public static Fixture Build(string text)
            {
                Fixture fixture = new Fixture();
                RenderingCoordinateSystem saved = Options.renderingSpace;
                try
                {
                    BasicScene scene = new BasicScene();
                    BasicSceneBuilder builder = new BasicSceneBuilder(scene);
                    pbrt.ParseString(builder, text);
                    SortedDictionary<string, Medium> media = scene.CreateMedia();
                    NamedTextures textures = scene.CreateTextures();
                    Dictionary<int, List<Light>> shapeIndexToAreaLights = new Dictionary<int, List<Light>>();
                    List<Light> lights = scene.CreateLights(textures, shapeIndexToAreaLights);
                    SortedDictionary<string, Material> namedMaterials = new SortedDictionary<string, Material>(StringComparer.Ordinal);
                    List<Material> materials = new List<Material>();
                    scene.CreateMaterials(textures, namedMaterials, materials);
                    Primitive accel = scene.CreateAggregate(textures, shapeIndexToAreaLights, media, namedMaterials, materials);
                    fixture.camera = scene.GetCamera();
                    fixture.sampler = scene.GetSampler();
                    fixture.integrator = (UpgIntegrator)scene.CreateIntegrator(fixture.camera, fixture.sampler, accel, lights);
                }
                finally
                {
                    Options.renderingSpace = saved;
                }
                return fixture;
            }

            public SampledWavelengths Wavelengths()
            {
                return camera.GetFilm().SampleWavelengths(0.37f);
            }

            // The camera sub-path of the pixel sample
            public int CameraPath(Point2i pixel, int sampleIndex, Vertex[] path, ref SampledWavelengths lambda)
            {
                for (int i = 0; i < path.Length; ++i)
                {
                    path[i] = new Vertex();
                }
                sampler.StartPixelSample(pixel, sampleIndex);
                CameraSample cameraSample = GetCameraSample(sampler, pixel, camera.GetFilm().GetFilter());
                pstd.optional<CameraRayDifferential> cameraRay = camera.GenerateRayDifferential(cameraSample, ref lambda);
                if (!cameraRay)
                {
                    return 0;
                }
                scratch.Reset();
                return BDPT.GenerateCameraSubpath(integrator, cameraRay.value().ray, ref lambda, sampler, scratch, path.Length, camera,
                                                  path, false, false, null, null);
            }
        }

        // ------------------------------------------------------------------------------------------
        // The scenes of the tests

        const string UpgTestDirectory = "/tmp/upg_test_renders";

        static string Quad(string a, string b, string c, string d)
        {
            return "Shape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [" + a + " " + b + " " + c + " " + d + "]\n";
        }

        // The room of the paper's kind of scene: diffuse walls, a red wall, a glossy (copper) floor, a diffuse sphere and an area
        // light. Variants: "direct" (the light shines down), "indirect" (the light shines up: everything the camera sees is lit by
        // bounces), "caustic" (a small light over a nearly mirror-like floor, so a bright patch of reflected light on the ceiling)
        public static string RoomScene(string variant, string integratorLine, int resolution, int spp)
        {
            string s = "Option \"rendercoordsys\" \"world\"\n";
            s += "LookAt 0 1.0 -3.6  0 1.0 0  0 1 0\nCamera \"perspective\" \"float fov\" 55\n";
            s += "Film \"rgb\" \"integer xresolution\" " + resolution + " \"integer yresolution\" " + resolution + " \"string filename\" \"out.exr\"\n";
            s += "Sampler \"independent\" \"integer pixelsamples\" " + spp + "\n" + integratorLine + "\nWorldBegin\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.7 0.7 0.7]\n";
            s += Quad("-2 2 -4", "2 2 -4", "2 2 2", "-2 2 2");
            s += Quad("-2 0 2", "2 0 2", "2 2 2", "-2 2 2");
            s += Quad("2 0 2", "2 0 -4", "2 2 -4", "2 2 2");
            s += Quad("2 0 -4", "-2 0 -4", "-2 2 -4", "2 2 -4");
            s += "AttributeEnd\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.75 0.2 0.2]\n" + Quad("-2 0 -4", "-2 0 2", "-2 2 2", "-2 2 -4") + "AttributeEnd\n";
            if (variant == "caustic")
            {
                s += "AttributeBegin\nMaterial \"conductor\" \"float roughness\" 0.06 \"bool remaproughness\" false " +
                     "\"spectrum eta\" \"metal-Cu-eta\" \"spectrum k\" \"metal-Cu-k\"\n";
            }
            else
            {
                s += "AttributeBegin\nMaterial \"conductor\" \"float roughness\" 0.12 \"spectrum eta\" \"metal-Cu-eta\" \"spectrum k\" \"metal-Cu-k\"\n";
            }
            s += Quad("-2 0 -4", "2 0 -4", "2 0 2", "-2 0 2") + "AttributeEnd\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.3 0.5 0.8]\nTranslate 0.8 0.35 0.6\nShape \"sphere\" \"float radius\" 0.35\nAttributeEnd\n";
            if (variant == "indirect")
            {
                // The light faces up, at the height of 1.2: the ceiling above it is lit, everything else only by bounces
                s += "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [40 40 40] \"bool twosided\" false\n";
                s += "Shape \"trianglemesh\" \"integer indices\" [0 2 1 0 3 2] \"point3 P\" [-0.3 1.2 0.4 0.3 1.2 0.4 0.3 1.2 -0.2 -0.3 1.2 -0.2]\nAttributeEnd\n";
            }
            else if (variant == "caustic")
            {
                s += "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [60 60 60] \"bool twosided\" false\n";
                s += "Shape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-0.15 1.98 0.3 0.15 1.98 0.3 0.15 1.98 -0.1 -0.15 1.98 -0.1]\nAttributeEnd\n";
            }
            else
            {
                s += "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [25 25 25] \"bool twosided\" false\n";
                s += "Shape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [-0.3 1.98 0.4 0.3 1.98 0.4 0.3 1.98 -0.2 -0.3 1.98 -0.2]\nAttributeEnd\n";
            }
            return s;
        }

        // The inside of a diffuse sphere that emits: every pixel sees the same radiance
        public static string FurnaceScene(string integratorLine, int resolution, int spp)
        {
            string s = "Option \"rendercoordsys\" \"world\"\n";
            s += "LookAt 0 0 -1  0 0.2 1  0 1 0\nCamera \"perspective\" \"float fov\" 70\n";
            s += "Film \"rgb\" \"integer xresolution\" " + resolution + " \"integer yresolution\" " + resolution + " \"string filename\" \"out.exr\"\n";
            s += "Sampler \"independent\" \"integer pixelsamples\" " + spp + "\n" + integratorLine + "\nWorldBegin\n";
            s += "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [1 1 1] \"bool twosided\" true\nMaterial \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]\n";
            s += "Shape \"sphere\" \"float radius\" 3\nAttributeEnd\n";
            return s;
        }

        // ------------------------------------------------------------------------------------------
        // (a) The reciprocal estimator: E[N_b] / p_b = 1 / p_c for a probability p_c that is known independently. The shooter is the
        // floor, which a camera sub-path reaches; the tentative rays hit a ceiling above, and the neighbourhood is a disc of it.
        // The probability is the integral of the density of the direction sampler over the disc (converted to area), by quadrature
        // with the density function of the BSDF (and in closed form for the diffuse floor under the center of the disc).

        static string PlaneScene(string floorMaterial, bool reverse, string cameraPosition)
        {
            string s = "Option \"rendercoordsys\" \"world\"\n";
            s += "LookAt " + cameraPosition + "  0 0 0  0 1 0\nCamera \"perspective\" \"float fov\" 0.5\n";
            s += "Film \"rgb\" \"integer xresolution\" 1 \"integer yresolution\" 1 \"string filename\" \"out.exr\"\n";
            s += "Sampler \"independent\" \"integer pixelsamples\" 1\nIntegrator \"upg\" \"integer maxdepth\" 3 \"integer photons\" 1 \"float radius\" 0.5\nWorldBegin\n";
            s += "AttributeBegin\n" + floorMaterial + "\n" + (reverse ? "ReverseOrientation\n" : "") +
                 Quad("-50 -50 0", "50 -50 0", "50 50 0", "-50 50 0") + "AttributeEnd\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]\n" + Quad("-50 -50 1", "50 -50 1", "50 50 1", "-50 50 1") + "AttributeEnd\n";
            s += "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [1 1 1]\n" + Quad("40 40 0.9", "41 40 0.9", "41 41 0.9", "40 41 0.9") + "AttributeEnd\n";
            return s;
        }

        // The probability that the sampler of the floor sends the direction to a point of the disc of radius d around the center on
        // the plane z = 1 (the integral over the disc of density(direction) cos / r^2), by a polar midpoint rule with n x 2n points
        static double DiscProbability(Vertex shooter, Point3f center, Float radius, int n)
        {
            Vector3f wo = shooter.si.wo;
            Point3f origin = shooter.p();
            double sum = 0;
            for (int i = 0; i < n; ++i)
            {
                double rho = (i + 0.5) / n * radius;
                for (int j = 0; j < 2 * n; ++j)
                {
                    double phi = (j + 0.5) / (2 * n) * 2 * Math.PI;
                    Point3f p = new Point3f(center.x + (Float)(rho * Math.Cos(phi)), center.y + (Float)(rho * Math.Sin(phi)), center.z);
                    Vector3f d = p - origin;
                    double r2 = LengthSquared(d);
                    Vector3f wi = d / MathF.Sqrt((Float)r2);
                    double pdf = shooter.bsdf.PDF(wo, wi);
                    double cosReceiver = Math.Abs(wi.z);
                    sum += pdf * cosReceiver / r2 * rho;
                }
            }
            return sum * (radius / n) * (2 * Math.PI / (2 * n));
        }

        struct EstimatorResult
        {
            public double mean, standardError, meanTrials, pb;
        }

        static EstimatorResult RunEstimator(Fixture fx, Vertex shooter, Point3f center, Float radius, bool useBound, int runs, int streamBase)
        {
            UpgBound bound = UpgBound.Full;
            if (useBound)
            {
                bound = UpgBound.Compute(shooter.bsdf, shooter.si.wo, shooter.p(), center, radius);
            }
            UpgWalker walker = new UpgWalker(fx.integrator, fx.camera, new IndependentSampler(1, 1234 + streamBase));
            SampledWavelengths lambda = fx.Wavelengths();
            double sum = 0, sumSquares = 0, trials = 0;
            for (int r = 0; r < runs; ++r)
            {
                walker.sampler.StartPixelSample(new Point2i(r, streamBase), 0);
                Float estimate = walker.ReciprocalEstimate(shooter, TransportMode.Radiance, bound, center, radius, lambda, 1 << 24, out int n);
                sum += estimate;
                sumSquares += (double)estimate * estimate;
                trials += n;
            }
            EstimatorResult result = new EstimatorResult();
            result.mean = sum / runs;
            double variance = Math.Max(sumSquares / runs - result.mean * result.mean, 0);
            result.standardError = Math.Sqrt(variance / runs);
            result.meanTrials = trials / runs;
            result.pb = bound.Area();
            return result;
        }

        static void ReciprocalCase(string label, string floorMaterial, bool reverse, string cameraPosition, Float centerX, Float centerY, Float radius,
                                   bool closedForm)
        {
            Fixture fx = Fixture.Build(PlaneScene(floorMaterial, reverse, cameraPosition));
            SampledWavelengths lambda = fx.Wavelengths();
            Vertex[] path = new Vertex[4];
            int count = fx.CameraPath(new Point2i(0, 0), 0, path, ref lambda);
            ASSERT_GE(count, 2);
            Vertex shooter = path[1];
            ASSERT_TRUE(shooter.type == VertexType.Surface);
            Point3f center = new Point3f(centerX, centerY, 1);
            double pc = DiscProbability(shooter, center, radius, 400);
            double pcCoarse = DiscProbability(shooter, center, radius, 200);
            double quadratureError = Math.Abs(pc - pcCoarse) / pc;
            if (closedForm)
            {
                // under the center of a diffuse floor: the probability of the disc is d^2 / (d^2 + h^2)
                double exact = (double)radius * radius / ((double)radius * radius + 1.0);
                EXPECT_NEAR(pc, exact, 2e-4 * exact);
                Console.WriteLine("[Upg] " + label + ": closed form p_c " + exact.ToString("G6", CultureInfo.InvariantCulture) + ", quadrature " + pc.ToString("G6", CultureInfo.InvariantCulture));
            }
            Console.WriteLine("[Upg] " + label + ": quadrature p_c " + pc.ToString("G6", CultureInfo.InvariantCulture) + " (n=400 vs n=200: relative difference " +
                              quadratureError.ToString("G3", CultureInfo.InvariantCulture) + ")");
            EXPECT_TRUE(quadratureError < 1e-3);
            for (int bounded = 0; bounded < 2; ++bounded)
            {
                int runs = 40000;
                EstimatorResult r = RunEstimator(fx, shooter, center, radius, bounded == 1, runs, 31 + bounded);
                double expected = 1.0 / pc;
                double z = (r.mean - expected) / r.standardError;
                Console.WriteLine("[Upg] " + label + (bounded == 1 ? ", with the angular bound" : ", without the bound") + ": p_b " +
                                  r.pb.ToString("G4", CultureInfo.InvariantCulture) + ", mean trials " + r.meanTrials.ToString("G4", CultureInfo.InvariantCulture) +
                                  ", estimate " + r.mean.ToString("G6", CultureInfo.InvariantCulture) + " +- " + r.standardError.ToString("G3", CultureInfo.InvariantCulture) +
                                  " (relative error " + (r.standardError / r.mean).ToString("G3", CultureInfo.InvariantCulture) + "), 1/p_c " +
                                  expected.ToString("G6", CultureInfo.InvariantCulture) + ", z " + z.ToString("F2", CultureInfo.InvariantCulture));
                EXPECT_TRUE(Math.Abs(z) < 4.0);
                // the expected number of trials is p_b / p_c
                EXPECT_TRUE(Math.Abs(r.meanTrials - r.pb / pc) < 0.03 * (r.pb / pc) + 0.05);
            }
        }

        [TEST("Upg", "ReciprocalEstimatorDiffuse")]
        public static void Upg_ReciprocalEstimatorDiffuse()
        {
            string diffuse = "Material \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]";
            ReciprocalCase("diffuse, under the center, d 0.3", diffuse, false, "0 0 0.5", 0, 0, 0.3f, true);
            ReciprocalCase("diffuse, under the center, d 0.05 (small p_c)", diffuse, false, "0 0 0.5", 0, 0, 0.05f, true);
            ReciprocalCase("diffuse, off center 0.7, d 0.25", diffuse, false, "0 0 0.5", 0.7f, 0.2f, 0.25f, false);
            ReciprocalCase("diffuse, the floor faces away from the camera, off center 0.7, d 0.25", diffuse, true, "0.8 0 0.6", 0.7f, 0.2f, 0.25f, false);
        }

        [TEST("Upg", "ReciprocalEstimatorGlossy")]
        public static void Upg_ReciprocalEstimatorGlossy()
        {
            string conductor = "Material \"conductor\" \"float roughness\" 0.3 \"spectrum eta\" \"metal-Cu-eta\" \"spectrum k\" \"metal-Cu-k\"";
            // the mirror direction of the camera at (0, 0, 0.5) is straight up: the lobe is around the point (0, 0, 1)
            ReciprocalCase("glossy (roughness 0.3), at the lobe, d 0.2", conductor, false, "0 0 0.5", 0.1f, 0, 0.2f, false);
            ReciprocalCase("glossy (roughness 0.3), on the tail of the lobe, d 0.2", conductor, false, "0 0 0.5", 0.9f, 0.3f, 0.2f, false);
            // the camera at (0.8, 0, 0.6): the mirror direction hits the ceiling at x = -1.33
            ReciprocalCase("glossy (roughness 0.3), oblique, the floor faces away from the camera, d 0.25", conductor, true, "0.8 0 0.6", -1.2f, 0.1f, 0.25f, false);
            string conductorStretched = "Material \"conductor\" \"float uroughness\" 0.4 \"float vroughness\" 0.1 \"spectrum eta\" \"metal-Cu-eta\" \"spectrum k\" \"metal-Cu-k\"";
            ReciprocalCase("anisotropic glossy (0.4 x 0.1), oblique, d 0.25", conductorStretched, false, "0.8 0 0.6", -1.2f, 0.1f, 0.25f, false);
        }

        // ------------------------------------------------------------------------------------------
        // (c) The MIS weights of all the techniques that can generate a path add to one.
        //
        // A path of the camera sub-paths that ends on the light (so that every split exists) is presented to BDPT.MISWeight, the
        // function the renderer calls, as every split (s light vertices, t camera vertices) and as every gathering technique, with
        // the densities of the vertices recomputed for the side that samples them. The sum of the weights of all the techniques is one.
        // A second computation from the raw densities (doubles, logarithms) gives the same weight for every technique.

        static void CheckPathWeights(Fixture fx, Vertex[] eye, int T, Float beta, bool gatheringPreferred, ref int checkedPaths, ref double worstSum,
                                     ref double worstRaw, ref int gatheringTechniques)
        {
            UpgIntegrator integrator = fx.integrator;
            UpgMis mis = integrator.Mis;
            int k = T - 1;
            Vertex emitter = eye[T - 1];
            LightSampler lightSampler = integrator.UpgLightSampler;
            // The densities of sampling x_i with the walk from the light (dL) and with the walk from the camera (dE), per area
            Float[] dL = new Float[k + 1];
            Float[] dE = new Float[k + 1];
            for (int i = 0; i < k; ++i)
            {
                dE[i] = eye[k - i].pdfFwd;
            }
            dL[0] = emitter.PDFLightOrigin(integrator.infiniteLights, eye[T - 2], lightSampler);
            dL[1] = emitter.PDFLight(integrator, eye[T - 2]);
            for (int i = 2; i < k; ++i)
            {
                dL[i] = eye[k - i + 1].PDF(integrator, eye[k - i + 2], eye[k - i]);
            }
            for (int i = 0; i < k; ++i)
            {
                if (!(dL[i] > 0) || !(dE[i] > 0) || eye[k - i].delta)
                {
                    return;
                }
            }
            // The walk's own reverse densities are the same numbers as the recomputed ones (where the walk has sampled the direction
            // that the density is conditioned on, which is toward the vertex two further along the camera sub-path)
            for (int j = 1; j <= T - 3; ++j)
            {
                EXPECT_TRUE(Math.Abs(eye[j].pdfRev - dL[k - j]) <= 2e-3 * dL[k - j]);
            }

            Vertex lightEnd = Vertex.CreateLight(emitter.si.areaLight, emitter.GetInteraction(), new SampledSpectrum(1.0f), dL[0]);
            SampledSpectrum ones = new SampledSpectrum(1.0f);
            Func<int, Vertex[]> makeLight = (int s) =>
            {
                Vertex[] v = new Vertex[s];
                for (int i = 0; i < s; ++i)
                {
                    v[i] = i == 0 ? lightEnd.Clone() : eye[k - i].Clone();
                    v[i].pdfFwd = dL[i];
                    v[i].pdfRev = dE[i];
                }
                return v;
            };
            Func<int, Vertex[]> makeCamera = (int t) =>
            {
                Vertex[] v = new Vertex[t];
                for (int j = 0; j < t; ++j)
                {
                    v[j] = eye[j].Clone();
                    if (j > 0)
                    {
                        v[j].pdfFwd = dE[k - j];
                        v[j].pdfRev = k - j >= 1 ? dL[k - j] : 0;
                    }
                }
                return v;
            };
            Func<int, int, UpgMisQuery, Float> weight = (int s, int t, UpgMisQuery query) =>
            {
                Vertex[] l = makeLight(s);
                Vertex[] c = makeCamera(t);
                Vertex sampled = new Vertex();
                if (s == 1)
                {
                    sampled = l[0].Clone();
                }
                else if (t == 1)
                {
                    sampled = c[0].Clone();
                }
                return BDPT.MISWeight(integrator, fx.camera, l, c, sampled, s, t, lightSampler, ones, ones, query);
            };

            mis.beta = beta;
            mis.gatheringPreferred = gatheringPreferred;
            // The path as the techniques see it, to find which gathering techniques exist
            Vertex[] x = new Vertex[k + 1];
            x[0] = lightEnd.Clone();
            for (int i = 1; i <= k; ++i)
            {
                x[i] = eye[k - i].Clone();
            }
            List<double> logDensity = new List<double>();
            List<Float> weights = new List<Float>();
            List<string> names = new List<string>();
            // Densities of the sub-path products, in logarithms: P(s) = prod_{i < s} dL(i) prod_{s <= i < k} dE(i)
            double[] logP = new double[k + 1];
            for (int s = 0; s <= k; ++s)
            {
                double lp = 0;
                for (int i = 0; i < s; ++i)
                {
                    lp += Math.Log(dL[i]);
                }
                for (int i = s; i < k; ++i)
                {
                    lp += Math.Log(dE[i]);
                }
                logP[s] = lp;
            }
            int[] variants = new int[k + 1];
            bool anyGathering = false;
            for (int m = 1; m <= k - 1; ++m)
            {
                variants[m] = mis.Variant(k, m, m >= 2 ? x[m - 2] : null, x[m - 1], x[m], x[m + 1], m + 2 <= k ? x[m + 2] : null, false);
                anyGathering |= variants[m] != UpgMis.None;
            }
            double lightPaths = mis.lightPaths;
            for (int s = 0; s <= k; ++s)
            {
                Float w = weight(s, k + 1 - s, new UpgMisQuery(mis, UpgMis.None, 0));
                weights.Add(w);
                names.Add("bdpt s=" + s);
                bool present = !(gatheringPreferred && anyGathering);
                logDensity.Add(present ? logP[s] : double.NegativeInfinity);
            }
            for (int m = 1; m <= k - 1; ++m)
            {
                if (variants[m] == UpgMis.None)
                {
                    continue;
                }
                ++gatheringTechniques;
                bool keepLight = variants[m] == UpgMis.KeepLightVertex;
                int s = keepLight ? m + 1 : m;
                int t = k + 1 - s;
                Float w = weight(s, t, new UpgMisQuery(mis, variants[m], m));
                double density = keepLight ? dE[m] : dL[m];
                double pc = Math.Min(mis.Measure(x[m]) * density, 1.0);
                weights.Add(w);
                names.Add("gather m=" + m + (keepLight ? " keep light vertex" : " keep camera vertex"));
                logDensity.Add(Math.Log(lightPaths * pc) + logP[s]);
            }
            double sumWeights = 0;
            double maxLog = double.NegativeInfinity;
            foreach (double l in logDensity)
            {
                maxLog = Math.Max(maxLog, l);
            }
            double denominator = 0;
            for (int i = 0; i < logDensity.Count; ++i)
            {
                sumWeights += weights[i];
                denominator += Math.Exp(beta * (logDensity[i] - maxLog));
            }
            worstSum = Math.Max(worstSum, Math.Abs(sumWeights - 1));
            EXPECT_TRUE(Math.Abs(sumWeights - 1) < 1e-4);
            for (int i = 0; i < logDensity.Count; ++i)
            {
                double raw = Math.Exp(beta * (logDensity[i] - maxLog)) / denominator;
                double error = Math.Abs(raw - weights[i]);
                worstRaw = Math.Max(worstRaw, error);
                if (error > 1e-4 + 1e-3 * raw)
                {
                    Console.WriteLine("[Upg] weight mismatch for technique " + names[i] + ": renderer " + weights[i] + ", raw densities " + raw + " (k " + k + ")");
                }
                EXPECT_TRUE(error <= 1e-4 + 1e-3 * raw);
            }
            ++checkedPaths;
        }

        [TEST("Upg", "MisWeightsOfAllTechniquesSumToOne")]
        public static void Upg_MisWeightsSumToOne()
        {
            Fixture fx = Fixture.Build(RoomScene("direct", "Integrator \"upg\" \"integer maxdepth\" 6 \"integer photons\" 400 \"float radius\" 0.25", 8, 1));
            int maxDepth = fx.integrator.MaxDepth;
            (Float, bool)[] settings = new (Float, bool)[] { (2, false), (1, false), (2, true), (3, false) };
            foreach ((Float beta, bool gatheringPreferred) in settings)
            {
                int checkedPaths = 0, gatheringTechniques = 0;
                double worstSum = 0, worstRaw = 0;
                int[] byLength = new int[10];
                SampledWavelengths lambda = fx.Wavelengths();
                for (int i = 0; i < 60000 && checkedPaths < 400; ++i)
                {
                    Vertex[] eye = new Vertex[maxDepth + 2];
                    int n = fx.CameraPath(new Point2i(i % 8, (i / 8) % 8), i, eye, ref lambda);
                    for (int T = 3; T <= n; ++T)
                    {
                        Vertex last = eye[T - 1];
                        if (last.type == VertexType.Surface && last.si.areaLight != null)
                        {
                            int before = checkedPaths;
                            CheckPathWeights(fx, eye, T, beta, gatheringPreferred, ref checkedPaths, ref worstSum, ref worstRaw, ref gatheringTechniques);
                            if (checkedPaths > before)
                            {
                                ++byLength[Math.Min(T - 1, 9)];
                            }
                            break;
                        }
                    }
                }
                string lengths = "";
                for (int k = 2; k < 10; ++k)
                {
                    lengths += " k" + k + ":" + byLength[k];
                }
                Console.WriteLine("[Upg] MIS sums: beta " + beta + (gatheringPreferred ? ", gathering preferred" : "") + ": " + checkedPaths + " paths (" + lengths +
                                  " ), " + gatheringTechniques + " gathering techniques, worst |sum - 1| " + worstSum.ToString("G3", CultureInfo.InvariantCulture) +
                                  ", worst difference to the raw-density weights " + worstRaw.ToString("G3", CultureInfo.InvariantCulture));
                EXPECT_GE(checkedPaths, 150);
                EXPECT_GT(gatheringTechniques, 100);
            }
        }

        // ------------------------------------------------------------------------------------------
        // (d), (e) The renderer's expectation: images over independent seeds. The scenes are small; each image is a pbrt process.

        static List<float[]> RenderSeeds(string name, string sceneText, int spp, int firstSeed, int count)
        {
            Directory.CreateDirectory(UpgTestDirectory);
            List<float[]> images = new List<float[]>();
            for (int i = 0; i < count; ++i)
            {
                OslRender r = OslRenderTools.Render(name, sceneText, spp, firstSeed + i, "", UpgTestDirectory);
                ASSERT_TRUE(r != null);
                images.Add(r.rgb);
                string basePath = Path.Combine(UpgTestDirectory, name + "_" + spp + "_" + (firstSeed + i));
                File.Delete(basePath + ".pbrt");
                File.Delete(basePath + ".exr");
            }
            return images;
        }

        static double ImageMean(float[] image, int channel)
        {
            double sum = 0;
            int n = 0;
            for (int i = channel < 0 ? 0 : channel; i < image.Length; i += channel < 0 ? 1 : 3)
            {
                sum += image[i];
                ++n;
            }
            return sum / n;
        }

        static void MeanAndError(List<float[]> images, int channel, out double mean, out double standardError)
        {
            double sum = 0, sumSquares = 0;
            foreach (float[] image in images)
            {
                double m = ImageMean(image, channel);
                sum += m;
                sumSquares += m * m;
            }
            int n = images.Count;
            mean = sum / n;
            standardError = Math.Sqrt(Math.Max(sumSquares / n - mean * mean, 0) / (n - 1));
        }

        // The t statistic of the difference of the mean of the images (all pixels, all channels or one channel)
        static double TStatistic(List<float[]> a, List<float[]> b, int channel, out double meanA, out double meanB)
        {
            MeanAndError(a, channel, out meanA, out double seA);
            MeanAndError(b, channel, out meanB, out double seB);
            return (meanA - meanB) / Math.Sqrt(seA * seA + seB * seB);
        }

        // The per-pixel means and variances of the mean
        static void PixelStatistics(List<float[]> images, out double[] mean, out double[] varianceOfMean)
        {
            int length = images[0].Length;
            mean = new double[length];
            varianceOfMean = new double[length];
            int n = images.Count;
            for (int i = 0; i < length; ++i)
            {
                double sum = 0, sumSquares = 0;
                foreach (float[] image in images)
                {
                    sum += image[i];
                    sumSquares += (double)image[i] * image[i];
                }
                mean[i] = sum / n;
                varianceOfMean[i] = Math.Max(sumSquares / n - mean[i] * mean[i], 0) * n / (n - 1.0) / n;
            }
        }

        // The mean square z-score of the pixel differences. Reported, not asserted: the pixel distributions of the gathering are heavy-tailed
        // (rare large values), the sample variance of 32 images then underestimates and the z-scores are skewed negative; with 160 images of
        // the caustic scene the same statistic is 1.04 (mean z -0.09)
        static double MeanSquareZ(List<float[]> a, List<float[]> b)
        {
            PixelStatistics(a, out double[] meanA, out double[] varA);
            PixelStatistics(b, out double[] meanB, out double[] varB);
            double sum = 0;
            int n = 0;
            for (int i = 0; i < meanA.Length; ++i)
            {
                double v = varA[i] + varB[i];
                if (v > 0)
                {
                    double z = (meanA[i] - meanB[i]) / Math.Sqrt(v);
                    sum += z * z;
                    ++n;
                }
            }
            return sum / n;
        }

        // The root mean square error of the average of G images against the reference, with the noise of the reference removed
        // (its per-pixel variance of the mean is subtracted), for G = 1, 2, 4, ...; and the slope of its logarithm against log G
        static double ErrorSlope(List<float[]> images, List<float[]> reference, string label, out string table)
        {
            PixelStatistics(reference, out double[] refMean, out double[] refVarOfMean);
            double refNoise = 0;
            for (int i = 0; i < refMean.Length; ++i)
            {
                refNoise += refVarOfMean[i];
            }
            refNoise /= refMean.Length;
            List<double> logG = new List<double>();
            List<double> logError = new List<double>();
            table = "";
            // The groups are random partitions of the images (40 of them, a fixed sequence): the squared error of the average of G images
            // against the reference has expectation sigma^2 / G (+ the square of a bias) under random partitions, with less noise than one
            // partition
            Random random = new Random(12345);
            const int partitions = 40;
            int[][] orders = new int[partitions][];
            for (int p = 0; p < partitions; ++p)
            {
                int[] order = new int[images.Count];
                for (int i = 0; i < order.Length; ++i)
                {
                    order[i] = i;
                }
                for (int i = order.Length - 1; i > 0; --i)
                {
                    int j = random.Next(i + 1);
                    int swap = order[i];
                    order[i] = order[j];
                    order[j] = swap;
                }
                orders[p] = order;
            }
            for (int g = 1; g * 2 <= images.Count; g *= 2)
            {
                int groups = images.Count / g;
                double mse = 0;
                for (int p = 0; p < partitions; ++p)
                {
                    for (int group = 0; group < groups; ++group)
                    {
                        for (int i = 0; i < refMean.Length; ++i)
                        {
                            double sum = 0;
                            for (int j = 0; j < g; ++j)
                            {
                                sum += images[orders[p][group * g + j]][i];
                            }
                            double d = sum / g - refMean[i];
                            mse += d * d;
                        }
                    }
                }
                mse /= partitions * groups * refMean.Length;
                double corrected = mse - refNoise;
                table += " G=" + g + ":" + Math.Sqrt(Math.Max(corrected, 1e-30)).ToString("G3", CultureInfo.InvariantCulture);
                logG.Add(Math.Log(g));
                logError.Add(0.5 * Math.Log(Math.Max(corrected, 1e-30)));
            }
            double mx = 0, my = 0;
            for (int i = 0; i < logG.Count; ++i)
            {
                mx += logG[i];
                my += logError[i];
            }
            mx /= logG.Count;
            my /= logG.Count;
            double sxy = 0, sxx = 0;
            for (int i = 0; i < logG.Count; ++i)
            {
                sxy += (logG[i] - mx) * (logError[i] - my);
                sxx += (logG[i] - mx) * (logG[i] - mx);
            }
            return sxy / sxx;
        }

        sealed class Variant
        {
            public string label;
            public string integratorLine;
            public int spp;
            public int seeds;
        }

        static void UnbiasednessOfScene(string sceneName, string variantName, Variant reference, Variant[] variants, int resolution)
        {
            string referenceScene = RoomScene(variantName, reference.integratorLine, resolution, reference.spp);
            string sceneDump = Environment.GetEnvironmentVariable("UPG_SCENEDIR");
            if (sceneDump != null)
            {
                Directory.CreateDirectory(sceneDump);
                File.WriteAllText(Path.Combine(sceneDump, sceneName + "_ref.pbrt"), referenceScene);
                for (int v = 0; v < variants.Length; ++v)
                {
                    File.WriteAllText(Path.Combine(sceneDump, sceneName + "_v" + v + ".pbrt"), RoomScene(variantName, variants[v].integratorLine, resolution, variants[v].spp));
                }
            }
            List<float[]> referenceImages = RenderSeeds(sceneName + "_ref", referenceScene, reference.spp, 1000, reference.seeds);
            double referenceMean = 0, referenceError = 0;
            MeanAndError(referenceImages, -1, out referenceMean, out referenceError);
            Console.WriteLine("[Upg] " + sceneName + ": reference " + reference.label + ", " + reference.seeds + " images of " + reference.spp + " spp, mean " +
                              referenceMean.ToString("G6", CultureInfo.InvariantCulture) + " +- " + referenceError.ToString("G3", CultureInfo.InvariantCulture));
            List<List<float[]>> results = new List<List<float[]>>();
            for (int v = 0; v < variants.Length; ++v)
            {
                Variant variant = variants[v];
                string scene = RoomScene(variantName, variant.integratorLine, resolution, variant.spp);
                List<float[]> images = RenderSeeds(sceneName + "_v" + v, scene, variant.spp, 1 + 100 * v, variant.seeds);
                results.Add(images);
                double mean, meanReference;
                double t = TStatistic(images, referenceImages, -1, out mean, out meanReference);
                string channelStatistics = "";
                for (int c = 0; c < 3; ++c)
                {
                    double tc = TStatistic(images, referenceImages, c, out double mc, out double rc);
                    channelStatistics += " t" + "RGB"[c] + "=" + tc.ToString("F2", CultureInfo.InvariantCulture);
                    EXPECT_TRUE(Math.Abs(tc) < 4.0);
                }
                double z2 = MeanSquareZ(images, referenceImages);
                if (Environment.GetEnvironmentVariable("UPG_DUMP") != null)
                {
                    PixelStatistics(images, out double[] dm, out double[] dv);
                    PixelStatistics(referenceImages, out double[] rm, out double[] rv);
                    List<string> rows = new List<string>();
                    for (int i = 0; i < dm.Length; ++i)
                    {
                        rows.Add(i + "," + dm[i].ToString("G6", CultureInfo.InvariantCulture) + "," + Math.Sqrt(dv[i]).ToString("G6", CultureInfo.InvariantCulture) + "," +
                                 rm[i].ToString("G6", CultureInfo.InvariantCulture) + "," + Math.Sqrt(rv[i]).ToString("G6", CultureInfo.InvariantCulture));
                    }
                    File.WriteAllLines(Path.Combine(UpgTestDirectory, "dump_" + sceneName + "_" + v + ".csv"), rows);
                }
                double slope = ErrorSlope(images, referenceImages, variant.label, out string table);
                Console.WriteLine("[Upg] " + sceneName + ": " + variant.label + " (" + variant.seeds + " images of " + variant.spp + " spp): mean " +
                                  mean.ToString("G6", CultureInfo.InvariantCulture) + " vs " + meanReference.ToString("G6", CultureInfo.InvariantCulture) + ", t " +
                                  t.ToString("F2", CultureInfo.InvariantCulture) + " (" + channelStatistics + " ), mean z^2 of the pixels " + z2.ToString("F3", CultureInfo.InvariantCulture) +
                                  ", RMSE of G averaged images" + table + ", log-log slope " + slope.ToString("F3", CultureInfo.InvariantCulture));
                EXPECT_TRUE(Math.Abs(t) < 4.0);
                EXPECT_TRUE(slope > -0.6 && slope < -0.4);
            }
            // The radius is not part of the expectation: pairs of variants that differ only in it have the same mean
            for (int a = 0; a < variants.Length; ++a)
            {
                for (int b = a + 1; b < variants.Length; ++b)
                {
                    if (variants[a].label.Substring(0, variants[a].label.IndexOf(' ')) == variants[b].label.Substring(0, variants[b].label.IndexOf(' ')))
                    {
                        double ma, mb;
                        double t = TStatistic(results[a], results[b], -1, out ma, out mb);
                        Console.WriteLine("[Upg] " + sceneName + ": " + variants[a].label + " vs " + variants[b].label + ": means " + ma.ToString("G6", CultureInfo.InvariantCulture) +
                                          " and " + mb.ToString("G6", CultureInfo.InvariantCulture) + ", t " + t.ToString("F2", CultureInfo.InvariantCulture));
                        EXPECT_TRUE(Math.Abs(t) < 4.0);
                    }
                }
            }
        }

        static Variant[] UnbiasednessVariants(int photons, int spp, int seeds)
        {
            string common = " \"integer maxdepth\" 5 \"integer photons\" " + photons;
            return new Variant[]
            {
                new Variant { label = "upgonly large-radius", integratorLine = "Integrator \"upg\"" + common + " \"float radius\" 0.4 \"bool upgonly\" true", spp = spp, seeds = seeds },
                new Variant { label = "upgonly small-radius", integratorLine = "Integrator \"upg\"" + common + " \"float radius\" 0.06 \"bool upgonly\" true", spp = spp, seeds = seeds },
                new Variant { label = "upg+bdpt large-radius", integratorLine = "Integrator \"upg\"" + common + " \"float radius\" 0.4", spp = spp, seeds = seeds },
                new Variant { label = "upg+bdpt small-radius", integratorLine = "Integrator \"upg\"" + common + " \"float radius\" 0.06", spp = spp, seeds = seeds },
            };
        }

        static Variant BdptReference(int spp, int seeds)
        {
            return new Variant { label = "bdpt", integratorLine = "Integrator \"bdpt\" \"integer maxdepth\" 5", spp = spp, seeds = seeds };
        }

        [TEST("Upg", "UnbiasedRoomWithGlossyFloor")]
        public static void Upg_UnbiasedRoom()
        {
            UnbiasednessOfScene("room", "direct", BdptReference(64, 96), UnbiasednessVariants(512, 16, 32), 12);
        }

        [TEST("Upg", "UnbiasedIndirectlyLitRoom")]
        public static void Upg_UnbiasedIndirect()
        {
            UnbiasednessOfScene("indirect", "indirect", BdptReference(64, 96), UnbiasednessVariants(512, 16, 32), 12);
        }

        [TEST("Upg", "UnbiasedCausticLikeRoom")]
        public static void Upg_UnbiasedCaustic()
        {
            UnbiasednessOfScene("caustic", "caustic", BdptReference(128, 96), UnbiasednessVariants(512, 16, 32), 12);
        }

        // (e) The furnace: the inside of an emitting diffuse sphere of albedo a has the radiance L_e (1 + a + ... + a^D) for paths of
        // up to D bounces, in every pixel, exactly
        [TEST("Upg", "FurnaceSphere")]
        public static void Upg_Furnace()
        {
            int depth = 5;
            string common = " \"integer maxdepth\" " + depth + " \"integer photons\" 256";
            List<float[]> emission = RenderSeeds("furnace_le", FurnaceScene("Integrator \"bdpt\" \"integer maxdepth\" 0", 8, 16), 16, 1, 16);
            MeanAndError(emission, -1, out double le, out double leError);
            double expected = 0, series = 0;
            for (int i = 0; i <= depth; ++i)
            {
                series += Math.Pow(0.5, i);
            }
            expected = le * series;
            Console.WriteLine("[Upg] furnace: the emission seen directly " + le.ToString("G6", CultureInfo.InvariantCulture) + ", expected radiance for " + depth +
                              " bounces " + expected.ToString("G6", CultureInfo.InvariantCulture));
            string[] lines = new string[]
            {
                "Integrator \"bdpt\" \"integer maxdepth\" " + depth,
                "Integrator \"upg\"" + common + " \"float radius\" 0.5 \"bool upgonly\" true",
                "Integrator \"upg\"" + common + " \"float radius\" 0.08 \"bool upgonly\" true",
                "Integrator \"upg\"" + common + " \"float radius\" 0.5",
                "Integrator \"upg\"" + common + " \"float radius\" 0.08"
            };
            string[] labels = new string[] { "bdpt", "upgonly r 0.5", "upgonly r 0.08", "upg+bdpt r 0.5", "upg+bdpt r 0.08" };
            int furnaceSeeds = 48;
            for (int i = 0; i < lines.Length; ++i)
            {
                List<float[]> images = RenderSeeds("furnace_" + i, FurnaceScene(lines[i], 8, 16), 16, 1 + 100 * i, furnaceSeeds);
                MeanAndError(images, -1, out double mean, out double error);
                double t = (mean - expected) / Math.Sqrt(error * error + leError * leError * series * series);
                Console.WriteLine("[Upg] furnace " + labels[i] + ": mean " + mean.ToString("G6", CultureInfo.InvariantCulture) + " +- " + error.ToString("G3", CultureInfo.InvariantCulture) +
                                  ", expected " + expected.ToString("G6", CultureInfo.InvariantCulture) + ", t " + t.ToString("F2", CultureInfo.InvariantCulture));
                EXPECT_TRUE(Math.Abs(t) < 4.0);
            }
        }
    }
}
