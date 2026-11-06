// (not in pbrt-v4) upg_test.cs: the tests of unbiased photon gathering (cpu/upg.cs, docs/upg.md)

using System;
using System.Text;
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
            // The phase functions of the media (phase 2)
            cases.Add(("isotropic phase function", new BSDF(new HGPhaseFunction(0))));
            cases.Add(("Henyey-Greenstein g 0.6", new BSDF(new HGPhaseFunction(0.6f))));
            cases.Add(("Henyey-Greenstein g -0.5", new BSDF(new HGPhaseFunction(-0.5f))));
            cases.Add(("Henyey-Greenstein g 0.9", new BSDF(new HGPhaseFunction(0.9f))));
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
            public int CameraPath(Point2i pixel, int sampleIndex, Vertex[] path, ref SampledWavelengths lambda, bool polarised = false)
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
                                                  path, false, polarised, null, null);
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
        // The media of the scenes of phase 2: the room (x in [-2, 2], y in [0, 2], z in [-4, 2]) filled with a medium, the camera in it.
        // The variant "kind:name" is the room variant "name" with the medium "kind"
        static string MediumDeclaration(string kind)
        {
            switch (kind)
            {
            case "fog":
                // (constant spectra: an RGB triple of three equal values is a spectrum that varies by a few 1e-4)
                return "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"spectrum sigma_a\" [300 0.05 900 0.05] \"spectrum sigma_s\" [300 0.35 900 0.35] \"float g\" 0\n";
            case "fogray":
                return "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0.05 0.05 0.05] \"rgb sigma_s\" [0.5 0.5 0.5] \"float g\" 0 \"float rayleigh\" 1\n";
            case "foghg":
                return "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0.05 0.05 0.05] \"rgb sigma_s\" [0.35 0.35 0.35] \"float g\" 0.6\n";
            case "fogdense":
                return "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0.05 0.05 0.05] \"rgb sigma_s\" [1.2 1.2 1.2] \"float g\" 0.3\n";
            case "foggrid":
            {
                // A smooth density on a grid over the room: the extinction varies by a factor of five
                int nx = 8, ny = 4, nz = 12;
                StringBuilder sb = new StringBuilder();
                sb.Append("MakeNamedMedium \"fog\" \"string type\" \"uniformgrid\" \"rgb sigma_a\" [0.05 0.05 0.05] \"rgb sigma_s\" [0.4 0.4 0.4] \"float g\" 0.3 " +
                          "\"integer nx\" " + nx + " \"integer ny\" " + ny + " \"integer nz\" " + nz + " \"point3 p0\" [-2 0 -4] \"point3 p1\" [2 2 2] \"float density\" [");
                for (int z = 0; z < nz; ++z)
                {
                    for (int y = 0; y < ny; ++y)
                    {
                        for (int x = 0; x < nx; ++x)
                        {
                            double d = 0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(0.9 * x + 0.5) * Math.Cos(1.3 * y + 0.2) * Math.Sin(0.6 * z + 1.0));
                            sb.Append(d.ToString("R", CultureInfo.InvariantCulture)).Append(' ');
                        }
                    }
                }
                sb.Append("]\n");
                return sb.ToString();
            }
            case "fogosl":
            {
                string directory = OslVolumeShaders.Directory();
                return "Translate -2 0 -4\nScale 4 2 6\nMakeNamedMedium \"fog\" \"string type\" \"osl\" \"string shader\" \"" + directory +
                       "/vtrig.osl\" \"float amp\" 0.7 \"point3 p0\" [0 0 0] \"point3 p1\" [1 1 1]\nIdentity\n";
            }
            default:
                return "";
            }
        }

        public static string RoomScene(string variant, string integratorLine, int resolution, int spp)
        {
            string mediaText = "";
            int colon = variant.IndexOf(':');
            if (colon >= 0)
            {
                mediaText = MediumDeclaration(variant.Substring(0, colon)) + "MediumInterface \"\" \"fog\"\n";
                variant = variant.Substring(colon + 1);
            }
            string s = "Option \"rendercoordsys\" \"world\"\n" + mediaText;
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
            if (variant == "polarised")
            {
                // Smooth spheres polarise (the Fresnel reflection of the light of the room): glass and copper; the floor is rough
                s += "AttributeBegin\nMaterial \"dielectric\" \"float eta\" 1.5\nTranslate 0.7 0.5 0.8\nShape \"sphere\" \"float radius\" 0.5\nAttributeEnd\n";
                s += "AttributeBegin\nMaterial \"conductor\" \"float roughness\" 0 \"spectrum eta\" \"metal-Cu-eta\" \"spectrum k\" \"metal-Cu-k\"\n" +
                     "Translate -0.8 0.35 0.5\nShape \"sphere\" \"float radius\" 0.35\nAttributeEnd\n";
            }
            else
            {
                s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.3 0.5 0.8]\nTranslate 0.8 0.35 0.6\nShape \"sphere\" \"float radius\" 0.35\nAttributeEnd\n";
            }
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
                bound = UpgBound.Compute(shooter.bsdf, shooter.type == VertexType.Surface ? shooter.si.wo : shooter.mi.wo, shooter.p(), center, radius);
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
                                     ref double worstRaw, ref int gatheringTechniques, Func<Vertex, Vertex, double> mediumFactor = null, bool strict = true)
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
                // (the density of a vertex in a medium has the factor of the collision, see UpgMis: here from the coefficients of the medium)
                double factor = mediumFactor != null && x[m].type == VertexType.Medium ? mediumFactor(x[m], keepLight ? x[m + 1] : x[m - 1]) : 1.0;
                double pc = Math.Min(mis.Measure(x[m]) * density * factor, 1.0);
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
            if (strict)
            {
                EXPECT_TRUE(Math.Abs(sumWeights - 1) < 1e-4);
            }
            for (int i = 0; i < logDensity.Count; ++i)
            {
                double raw = Math.Exp(beta * (logDensity[i] - maxLog)) / denominator;
                double error = Math.Abs(raw - weights[i]);
                worstRaw = Math.Max(worstRaw, error);
                if (error > 1e-4 + 1e-3 * raw)
                {
                    Console.WriteLine("[Upg] weight mismatch for technique " + names[i] + ": renderer " + weights[i] + ", raw densities " + raw + " (k " + k + ")");
                }
                if (strict)
                {
                    EXPECT_TRUE(error <= 1e-4 + 1e-3 * raw);
                }
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

        // ------------------------------------------------------------------------------------------
        // Polarisation (phase 1b)
        //
        // The connection of a photon and a camera vertex is the connection of two vertices of BDPT: the Mueller matrix of the camera
        // sub-path and the Stokes vector of the light sub-path, each relative to its scalar throughput, with the scattering Mueller
        // matrices of the two connection vertices between them (a surface depolarises), the connection along y -> z.

        // PolarisedUPBP's FresnelReal() (Utils2.hxx, MIT): the reflectances of the s and p components (the reference of the Fresnel
        // equations; ior < 1 for light that comes from the denser medium)
        static void UpbpFresnelReal(Float cosInc, Float ior, out Float fs, out Float fp)
        {
            if (ior < 1.0f && MathF.Acos(cosInc) >= MathF.Asin(ior))
            {
                fs = 1.0f;
                fp = 1.0f;
                return;
            }
            Float cos2 = cosInc * cosInc;
            Float sin2 = 1 - cos2;
            Float n2 = ior * ior;
            Float sqrtNSin = MathF.Sqrt(n2 - sin2);
            fs = Sqr(-Sqr(sqrtNSin - cosInc) / (n2 - 1));
            fp = Sqr((n2 * cosInc - sqrtNSin) / (n2 * cosInc + sqrtNSin));
        }

        static string MirrorScene(string material)
        {
            string s = "Option \"rendercoordsys\" \"world\"\n";
            s += "LookAt 0.8 0 0.6  0 0 0  0 1 0\nCamera \"perspective\" \"float fov\" 0.5\n";
            s += "Film \"rgb\" \"integer xresolution\" 1 \"integer yresolution\" 1 \"string filename\" \"out.exr\"\n";
            s += "Sampler \"independent\" \"integer pixelsamples\" 1\nIntegrator \"upg\" \"integer maxdepth\" 4 \"integer photons\" 1 \"float radius\" 0.5 \"bool polarised\" true\nWorldBegin\n";
            s += "AttributeBegin\n" + material + "\n" + Quad("-50 -50 0", "50 -50 0", "50 50 0", "-50 50 0") + "AttributeEnd\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]\n" + Quad("-50 -50 1", "50 -50 1", "50 50 1", "-50 50 1") + "AttributeEnd\n";
            s += "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [1 1 1]\n" + Quad("40 40 0.9", "41 40 0.9", "41 41 0.9", "40 41 0.9") + "AttributeEnd\n";
            return s;
        }

        // The connection of a camera vertex behind a smooth dielectric reflection (its Mueller matrix is the Fresnel one) and a vertex
        // of a light sub-path whose Stokes vector is strongly polarised: the Stokes vector that the sensor measures is
        // (1, D cos 2chi, D sin 2chi, 0) of the scalar contribution, with D = (Rs - Rp) / (Rs + Rp) of the reflection of PolarisedUPBP's
        // Fresnel equations. The polarisation of the light sub-path does not reach the sensor (the surfaces at the connection depolarise),
        // the invariants of the frame rotation (S1^2 + S2^2 = D^2 S0^2) and the intensity are checked for every choice of the sensor.
        [TEST("Upg", "PolarisedConnectionMatchesTheReferenceFormulas")]
        public static void Upg_PolarisedConnection()
        {
            Fixture fx = Fixture.Build(MirrorScene("Material \"dielectric\" \"float eta\" 1.5"));
            SampledWavelengths lambda = fx.Wavelengths();
            int found = 0;
            for (int i = 0; i < 4000 && found < 20; ++i)
            {
                Vertex[] eye = new Vertex[5];
                int n = fx.CameraPath(new Point2i(0, 0), i, eye, ref lambda, true);
                if (n < 3 || eye[2].type != VertexType.Surface || !(eye[2].bsdf.GetBxDF() is DiffuseBxDF) || !(eye[1].bsdf.GetBxDF() is DielectricBxDF))
                {
                    continue;
                }
                // eye[1]: the smooth reflection (the sampled direction went to the ceiling), eye[2]: the diffuse vertex it reached
                Vertex pt = eye[2];
                // a vertex of a light sub-path: a diffuse vertex of another sub-path, with a light polarised in every component
                Vertex qs = pt.Clone();
                qs.si.pi = new Point3fi(new Point3f(0.3f, -0.4f, 1.0f));
                qs.beta = new SampledSpectrum(0.7f);
                qs.plight = new PolarisedLightSystem.Light(new StokesVector(new SampledSpectrum(0.7f), new SampledSpectrum(0.5f),
                                                                           new SampledSpectrum(-0.3f), new SampledSpectrum(0.2f))).InDirection(Normalize(pt.p() - qs.p()), new Vector3f(0, 0, 1));
                SampledSpectrum scalar = new SampledSpectrum(0.123f);
                Vector3f cameraDirection = Normalize(eye[1].p() - eye[0].p());
                // the reflectance at the mirror
                Vector3f woLocal = eye[1].bsdf.RenderToLocal(eye[1].si.wo);
                UpbpFresnelReal(AbsCosTheta(woLocal), woLocal.z > 0 ? 1.5f : 1 / 1.5f, out Float fs, out Float fp);
                double expectedD = (fs - fp) / (double)(fs + fp);
                double[] measured = new double[4];
                for (int m = 0; m < 4; ++m)
                {
                    PolarisedPathIntegrator.Sensor sensor = new PolarisedPathIntegrator.Sensor();
                    sensor.analyser = false;
                    sensor.measurement = (PolarisedPathIntegrator.Measurement)m;
                    sensor.cameraUp = fx.camera.GetCameraTransform().RenderFromCamera(new Vector3f(0, 1, 0), 0);
                    SampledSpectrum v = BDPT.MeasureConnection(fx.camera, qs, pt, eye, scalar, lambda[0], sensor, cameraDirection);
                    measured[m] = v[0];
                }
                Console.WriteLine("[Upg] polarised connection: S0 " + measured[0].ToString("G5", CultureInfo.InvariantCulture) + " S1 " +
                                  measured[1].ToString("G5", CultureInfo.InvariantCulture) + " S2 " + measured[2].ToString("G5", CultureInfo.InvariantCulture) +
                                  " S3 " + measured[3].ToString("G5", CultureInfo.InvariantCulture) + ", D of the reference " + expectedD.ToString("G5", CultureInfo.InvariantCulture));
                EXPECT_NEAR(measured[0], 0.123, 1e-5);
                EXPECT_NEAR(Math.Sqrt(measured[1] * measured[1] + measured[2] * measured[2]), Math.Abs(expectedD) * 0.123, 2e-4 * 0.123 + 1e-6);
                EXPECT_NEAR(measured[3], 0, 1e-5);
                ++found;
            }
            ASSERT_GE(found, 5);
            Console.WriteLine("[Upg] polarised connection: " + found + " mirror reflections checked");
        }

        static Variant PolarisedVariant(string label, string line, int spp, int seeds, string measure)
        {
            return new Variant { label = label, integratorLine = line + " \"bool polarised\" true \"string measure\" \"" + measure + "\"", spp = spp, seeds = seeds };
        }

        // The image statistic: a random +-1 projection of the pixels (a Stokes channel has both signs, so its plain mean says little)
        static List<double> Projections(List<float[]> images, int pattern)
        {
            Random random = new Random(777 + pattern);
            int length = images[0].Length;
            double[] weights = new double[length];
            for (int i = 0; i < length; ++i)
            {
                weights[i] = random.Next(2) == 0 ? -1.0 : 1.0;
            }
            List<double> values = new List<double>();
            foreach (float[] image in images)
            {
                double sum = 0;
                for (int i = 0; i < length; ++i)
                {
                    sum += weights[i] * image[i];
                }
                values.Add(sum / length);
            }
            return values;
        }

        static double TOfValues(List<double> a, List<double> b)
        {
            double ma = 0, mb = 0, va = 0, vb = 0;
            foreach (double v in a)
            {
                ma += v;
            }
            foreach (double v in b)
            {
                mb += v;
            }
            ma /= a.Count;
            mb /= b.Count;
            foreach (double v in a)
            {
                va += (v - ma) * (v - ma);
            }
            foreach (double v in b)
            {
                vb += (v - mb) * (v - mb);
            }
            va /= (a.Count - 1) * (double)a.Count;
            vb /= (b.Count - 1) * (double)b.Count;
            double denominator = Math.Sqrt(va + vb);
            return denominator > 0 ? (ma - mb) / denominator : 0;
        }

        // (2) Polarised UPG against a long polarised bdpt: the Stokes channels S0, S1 and S2 of the room with a glass and a copper sphere
        [TEST("Upg", "PolarisedUnbiasedness")]
        public static void Upg_PolarisedUnbiasedness()
        {
            PolarisedUnbiasednessCase("polarised", "pol");
        }

        // Rayleigh scattering in a medium polarises the light (the Mueller matrix of the scattering at the connection vertices that are in the medium)
        [TEST("Upg", "PolarisedRayleighFog")]
        public static void Upg_PolarisedRayleighFog()
        {
            PolarisedUnbiasednessCase("fogray:direct", "polray");
        }

        static void PolarisedUnbiasednessCase(string sceneVariant, string tag)
        {
            string common = " \"integer maxdepth\" 5 \"integer photons\" 512";
            string[] measures = new string[] { "intensity", "s1", "s2" };
            foreach (string measure in measures)
            {
                Variant reference = PolarisedVariant("bdpt", "Integrator \"bdpt\" \"integer maxdepth\" 5", 64, 64, measure);
                List<float[]> referenceImages = RenderSeeds(tag + "_" + measure + "_ref", RoomScene(sceneVariant, reference.integratorLine, 12, reference.spp),
                                                            reference.spp, 1000, reference.seeds);
                Variant[] variants = new Variant[]
                {
                    PolarisedVariant("upgonly small-radius", "Integrator \"upg\"" + common + " \"float radius\" 0.06 \"bool upgonly\" true", 16, 24, measure),
                    PolarisedVariant("upg+bdpt large-radius", "Integrator \"upg\"" + common + " \"float radius\" 0.4", 16, 24, measure),
                    PolarisedVariant("upg+bdpt small-radius", "Integrator \"upg\"" + common + " \"float radius\" 0.06", 16, 24, measure),
                };
                for (int v = 0; v < variants.Length; ++v)
                {
                    List<float[]> images = RenderSeeds(tag + "_" + measure + "_v" + v, RoomScene(sceneVariant, variants[v].integratorLine, 12, variants[v].spp),
                                                       variants[v].spp, 1 + 100 * v, variants[v].seeds);
                    double meanA, meanB;
                    double t = TStatistic(images, referenceImages, -1, out meanA, out meanB);
                    string projections = "";
                    for (int pattern = 0; pattern < 3; ++pattern)
                    {
                        double tp = TOfValues(Projections(images, pattern), Projections(referenceImages, pattern));
                        projections += " " + tp.ToString("F2", CultureInfo.InvariantCulture);
                        EXPECT_TRUE(Math.Abs(tp) < 4.0);
                    }
                    Console.WriteLine("[Upg] polarised " + measure + ": " + variants[v].label + " (" + variants[v].seeds + " images of 16 spp): mean " + meanA.ToString("G5", CultureInfo.InvariantCulture) +
                                      " vs " + meanB.ToString("G5", CultureInfo.InvariantCulture) + ", t " + t.ToString("F2", CultureInfo.InvariantCulture) + ", t of three random +-1 projections of the pixels" + projections);
                    if (measure == "intensity" || Math.Abs(meanB) > 1e-4)
                    {
                        EXPECT_TRUE(Math.Abs(t) < 4.0);
                    }
                }
            }
        }

        // (1) With polarised = true and a scene without a polarising element (diffuse and a rough conductor), S0 is the scalar image and S1..S3 are zero
        [TEST("Upg", "PolarisedWithoutPolarisingElements")]
        public static void Upg_PolarisedIdentity()
        {
            string common = "Integrator \"upg\" \"integer maxdepth\" 5 \"integer photons\" 512 \"float radius\" 0.06";
            List<float[]> scalar = RenderSeeds("poli_scalar", RoomScene("direct", common, 12, 16), 16, 1, 32);
            List<float[]> polarised = RenderSeeds("poli_s0", RoomScene("direct", common + " \"bool polarised\" true", 12, 16), 16, 5001, 32);
            double ma, mb;
            double t = TStatistic(polarised, scalar, -1, out ma, out mb);
            Console.WriteLine("[Upg] polarised S0 " + ma.ToString("G6", CultureInfo.InvariantCulture) + " vs scalar " + mb.ToString("G6", CultureInfo.InvariantCulture) + ", t " + t.ToString("F2", CultureInfo.InvariantCulture));
            EXPECT_TRUE(Math.Abs(t) < 4.0);
            // the same seed: the same light, so the images are the same up to the arithmetic of the Mueller matrices
            List<float[]> sameSeedScalar = RenderSeeds("poli_ss", RoomScene("direct", common, 12, 16), 16, 7, 1);
            List<float[]> sameSeedPolarised = RenderSeeds("poli_sp", RoomScene("direct", common + " \"bool polarised\" true", 12, 16), 16, 7, 1);
            double worst = 0;
            for (int i = 0; i < sameSeedScalar[0].Length; ++i)
            {
                worst = Math.Max(worst, Math.Abs(sameSeedScalar[0][i] - sameSeedPolarised[0][i]) / (Math.Abs(sameSeedScalar[0][i]) + 1e-3));
            }
            Console.WriteLine("[Upg] polarised S0 against the scalar render of the same seed: worst relative difference " + worst.ToString("G3", CultureInfo.InvariantCulture));
            EXPECT_TRUE(worst < 1e-3);
            foreach (string measure in new string[] { "s1", "s2", "s3" })
            {
                List<float[]> images = RenderSeeds("poli_" + measure, RoomScene("direct", common + " \"bool polarised\" true \"string measure\" \"" + measure + "\"", 12, 16), 16, 7, 1);
                double largest = 0;
                foreach (float v in images[0])
                {
                    largest = Math.Max(largest, Math.Abs(v));
                }
                Console.WriteLine("[Upg] polarised " + measure + " of the scene without a polarising element: largest |value| " + largest.ToString("G3", CultureInfo.InvariantCulture) + " (the intensity is about " + mb.ToString("G3", CultureInfo.InvariantCulture) + ")");
                EXPECT_TRUE(largest < 1e-5);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Phase 2: media (our own extension: the paper is about surfaces)

        // (a) The reciprocal estimator in an infinite homogeneous medium: the shooter is a scattering event of the camera sub-path, the
        // neighbourhood is a ball, and the probability that the tentative ray (the phase function's direction and the sampled distance to
        // its first scattering event) ends in the ball is the integral over the cone and the chord of the ball:
        //     p_c = int dOmega  phase(wo, w)  (sigma_s / sigma_t) (exp(-sigma_t r_1) - exp(-sigma_t r_2)),
        // r_1,2 = D cos(a) -+ sqrt(d^2 - D^2 sin^2(a)) for the angle a to the axis of the cone (the absorption ends the ray, so
        // the density of a scattering event is sigma_s T).
        static string InfiniteMediumScene(string medium)
        {
            string s = "Option \"rendercoordsys\" \"world\"\n" + medium + "MediumInterface \"\" \"fog\"\n";
            s += "LookAt 0 0 0  0 0 1  0 1 0\nCamera \"perspective\" \"float fov\" 20\n";
            s += "Film \"rgb\" \"integer xresolution\" 1 \"integer yresolution\" 1 \"string filename\" \"out.exr\"\n";
            s += "Sampler \"independent\" \"integer pixelsamples\" 1\nIntegrator \"upg\" \"integer maxdepth\" 3 \"integer photons\" 1 \"float radius\" 0.5\nWorldBegin\n";
            s += "LightSource \"point\" \"rgb I\" [1 1 1] \"point3 from\" [0 0 0]\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]\nTranslate 0 5000 0\nShape \"sphere\" \"float radius\" 1\nAttributeEnd\n";
            return s;
        }

        static double BallProbability(Vertex shooter, Point3f center, Float radius, double sigmaS, double sigmaT, int n, int m)
        {
            Vector3f wo = shooter.mi.wo;
            Point3f origin = shooter.p();
            Vector3f toCenter = center - origin;
            double distance = Length(toCenter);
            Vector3f axis = toCenter / (Float)distance;
            Vector3f e1 = Normalize(Cross(axis, Math.Abs(axis.x) < 0.9f ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0)));
            Vector3f e2 = Cross(axis, e1);
            double halfAngle = Math.Asin(radius / distance);
            double sum = 0;
            for (int i = 0; i < n; ++i)
            {
                double a = (i + 0.5) / n * halfAngle;
                double sinA = Math.Sin(a), cosA = Math.Cos(a);
                double chord = Math.Sqrt(Math.Max((double)radius * radius - distance * distance * sinA * sinA, 0));
                double near = distance * cosA - chord, far = distance * cosA + chord;
                double radial = sigmaS / sigmaT * (Math.Exp(-sigmaT * near) - Math.Exp(-sigmaT * far));
                double around = 0;
                for (int j = 0; j < m; ++j)
                {
                    double phi = (j + 0.5) / m * 2 * Math.PI;
                    Vector3f w = (Float)cosA * axis + (Float)sinA * ((Float)Math.Cos(phi) * e1 + (Float)Math.Sin(phi) * e2);
                    around += shooter.bsdf.PDF(wo, w);
                }
                sum += around / m * 2 * Math.PI * radial * sinA;
            }
            return sum * halfAngle / n;
        }

        static void MediumReciprocalCase(string label, string phaseG, double g, Float radius, Vector3f offset)
        {
            double sigmaA = 0.2, sigmaS = 0.6;
            string medium = "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0.2 0.2 0.2] \"rgb sigma_s\" [0.6 0.6 0.6] \"float g\" " + phaseG + "\n";
            Fixture fx = Fixture.Build(InfiniteMediumScene(medium));
            SampledWavelengths lambda = fx.Wavelengths();
            Vertex shooter = null;
            for (int i = 0; i < 200 && shooter == null; ++i)
            {
                Vertex[] path = new Vertex[4];
                int count = fx.CameraPath(new Point2i(0, 0), i, path, ref lambda);
                if (count >= 2 && path[1].type == VertexType.Medium)
                {
                    shooter = path[1];
                }
            }
            ASSERT_TRUE(shooter != null);
            Point3f center = shooter.p() + offset;
            double pc = BallProbability(shooter, center, radius, sigmaS, sigmaA + sigmaS, 4000, 96);
            double pcCoarse = BallProbability(shooter, center, radius, sigmaS, sigmaA + sigmaS, 2000, 48);
            double quadratureError = Math.Abs(pc - pcCoarse) / pc;
            Console.WriteLine("[Upg] " + label + ": quadrature p_c " + pc.ToString("G6", CultureInfo.InvariantCulture) + " (relative difference to the coarse rule " +
                              quadratureError.ToString("G3", CultureInfo.InvariantCulture) + ")");
            EXPECT_TRUE(quadratureError < 1e-3);
            for (int bounded = 0; bounded < 2; ++bounded)
            {
                int runs = bounded == 1 ? 400000 : 40000;
                EstimatorResult r = RunEstimator(fx, shooter, center, radius, bounded == 1, runs, 51 + bounded);
                double expected = 1.0 / pc;
                double z = (r.mean - expected) / r.standardError;
                Console.WriteLine("[Upg] " + label + (bounded == 1 ? ", with the angular bound" : ", without the bound") + ": p_b " + r.pb.ToString("G4", CultureInfo.InvariantCulture) +
                                  ", mean trials " + r.meanTrials.ToString("G4", CultureInfo.InvariantCulture) + ", estimate " + r.mean.ToString("G6", CultureInfo.InvariantCulture) + " +- " +
                                  r.standardError.ToString("G3", CultureInfo.InvariantCulture) + ", 1/p_c " + expected.ToString("G6", CultureInfo.InvariantCulture) + ", z " +
                                  z.ToString("F2", CultureInfo.InvariantCulture));
                EXPECT_TRUE(Math.Abs(z) < 4.0);
                EXPECT_TRUE(Math.Abs(r.meanTrials - r.pb / pc) < 0.03 * (r.pb / pc) + 0.05);
            }
        }

        [TEST("Upg", "MediumReciprocalEstimator")]
        public static void Upg_MediumReciprocalEstimator()
        {
            MediumReciprocalCase("infinite medium, isotropic phase function, d 0.4 at distance 0.8", "0", 0, 0.4f, new Vector3f(0.7f, 0.2f, 0.3f));
            MediumReciprocalCase("infinite medium, isotropic phase function, d 0.15 at distance 1.5", "0", 0, 0.15f, new Vector3f(-1.1f, 0.6f, 0.8f));
            MediumReciprocalCase("infinite medium, Henyey-Greenstein g 0.6, d 0.3", "0.6", 0.6, 0.3f, new Vector3f(0.5f, -0.4f, 0.6f));
            MediumReciprocalCase("infinite medium, Henyey-Greenstein g -0.5, d 0.3", "-0.5", -0.5, 0.3f, new Vector3f(0.5f, -0.4f, 0.6f));
        }

        // (c) The MIS weights of all the techniques add to one on paths with vertices in a medium (the same function and the same check
        // as the paths of surfaces; the raw-density computation has the factor sigma_s exp(-sigma_t x) of the collision from the
        // coefficients of the medium, not from the code that is tested)
        [TEST("Upg", "MediumMisWeightsSumToOne")]
        public static void Upg_MediumMisWeightsSumToOne()
        {
            Fixture fx = Fixture.Build(RoomScene("fog:direct", "Integrator \"upg\" \"integer maxdepth\" 6 \"integer photons\" 400 \"float radius\" 0.25", 8, 1));
            int maxDepth = fx.integrator.MaxDepth;
            Func<Vertex, Vertex, double> factor = (Vertex x, Vertex neighbour) => 0.35 * Math.Exp(-0.4 * Length(x.p() - neighbour.p()));
            foreach (Float beta in new Float[] { 2, 1 })
            {
                int checkedPaths = 0, mediumPaths = 0, gatheringTechniques = 0;
                double worstSum = 0, worstRaw = 0, worstShort = 0;
                int shortPaths = 0;
                SampledWavelengths lambda = fx.Wavelengths();
                fx.integrator.Mis.SetWavelengths(lambda);
                for (int i = 0; i < 400000 && mediumPaths < 150; ++i)
                {
                    Vertex[] eye = new Vertex[maxDepth + 2];
                    int n = fx.CameraPath(new Point2i(i % 8, (i / 8) % 8), i, eye, ref lambda);
                    for (int T = 3; T <= n; ++T)
                    {
                        Vertex last = eye[T - 1];
                        if (last.type == VertexType.Surface && last.si.areaLight != null)
                        {
                            bool hasMedium = false, wellConditioned = true;
                            for (int j = 1; j < T - 1; ++j)
                            {
                                if (eye[j].type == VertexType.Medium)
                                {
                                    hasMedium = true;
                                    // The density of a vertex that a view of the path recomputes from the geometry differs from the walk's by the
                                    // offset of the origin of the ray, relative to the distance and the cosine (4e-4 at a distance of 0.1 and a
                                    // grazing angle: a property of the float geometry of bdpt's densities, not of the weights)
                                    wellConditioned &= Length(eye[j].p() - eye[j - 1].p()) > 0.25f && Length(eye[j].p() - eye[j + 1].p()) > 0.25f;
                                }
                            }
                            if (hasMedium)
                            {
                                int before = checkedPaths;
                                double worstBefore = worstSum;
                                if (wellConditioned)
                                {
                                    CheckPathWeights(fx, eye, T, beta, false, ref checkedPaths, ref worstSum, ref worstRaw, ref gatheringTechniques, factor);
                                    if (checkedPaths > before)
                                    {
                                        ++mediumPaths;
                                    }
                                }
                                else
                                {
                                    double ignoredRaw = 0;
                                    int ignoredCount = 0;
                                    CheckPathWeights(fx, eye, T, beta, false, ref ignoredCount, ref worstShort, ref ignoredRaw, ref gatheringTechniques, factor, false);
                                    shortPaths += ignoredCount;
                                }
                            }
                            break;
                        }
                    }
                }
                Console.WriteLine("[Upg] MIS sums in a medium: beta " + beta + ": " + mediumPaths + " paths with a vertex in the medium, " + gatheringTechniques +
                                  " gathering techniques, worst |sum - 1| " + worstSum.ToString("G3", CultureInfo.InvariantCulture) + ", worst difference to the raw-density weights " +
                                  worstRaw.ToString("G3", CultureInfo.InvariantCulture) + "; " + shortPaths +
                                  " paths with a medium vertex closer than 0.25 to a neighbour (not asserted, float geometry): worst |sum - 1| " + worstShort.ToString("G3", CultureInfo.InvariantCulture));
                EXPECT_GE(mediumPaths, 100);
                EXPECT_GT(gatheringTechniques, 100);
            }
        }

        // (d) Unbiasedness against bdpt in the room filled with media
        [TEST("Upg", "UnbiasedFogRoom")]
        public static void Upg_UnbiasedFogRoom()
        {
            UnbiasednessOfScene("fog", "fog:direct", BdptReference(64, 96), UnbiasednessVariants(512, 16, 32), 12);
        }

        [TEST("Upg", "UnbiasedForwardScatteringFog")]
        public static void Upg_UnbiasedForwardScatteringFog()
        {
            UnbiasednessOfScene("foghg", "foghg:indirect", BdptReference(64, 96), UnbiasednessVariants(512, 16, 32), 12);
        }

        [TEST("Upg", "UnbiasedDenseFog")]
        public static void Upg_UnbiasedDenseFog()
        {
            UnbiasednessOfScene("fogdense", "fogdense:direct", BdptReference(64, 96), UnbiasednessVariants(512, 16, 32), 12);
        }

        [TEST("Upg", "UnbiasedGridMedium")]
        public static void Upg_UnbiasedGridMedium()
        {
            UnbiasednessOfScene("foggrid", "foggrid:direct", BdptReference(64, 96), UnbiasednessVariants(512, 16, 32), 12);
        }

        [TEST("Upg", "UnbiasedOslMedium")]
        public static void Upg_UnbiasedOslMedium()
        {
            UnbiasednessOfScene("fogosl", "fogosl:direct", BdptReference(64, 96), UnbiasednessVariants(512, 16, 32), 12);
        }

        // glossy surfaces in a medium: the copper floor of the caustic-like room under the small light
        [TEST("Upg", "UnbiasedGlossyInFog")]
        public static void Upg_UnbiasedGlossyInFog()
        {
            UnbiasednessOfScene("fogcaustic", "fog:caustic", BdptReference(128, 96), UnbiasednessVariants(512, 16, 32), 12);
        }

        // (e) The furnace with a medium: the inside of an emitting diffuse sphere filled with a medium that does not absorb has the
        // radiance L_e / (1 - a) in every pixel (the sum of the bounces, a medium that only scatters changes nothing)
        static string MediumFurnaceScene(string integratorLine, int resolution, int spp)
        {
            string s = "Option \"rendercoordsys\" \"world\"\n";
            s += "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0 0 0] \"rgb sigma_s\" [0.25 0.25 0.25] \"float g\" 0.3\nMediumInterface \"\" \"fog\"\n";
            s += "LookAt 0 0 -1  0 0.2 1  0 1 0\nCamera \"perspective\" \"float fov\" 70\n";
            s += "Film \"rgb\" \"integer xresolution\" " + resolution + " \"integer yresolution\" " + resolution + " \"string filename\" \"out.exr\"\n";
            s += "Sampler \"independent\" \"integer pixelsamples\" " + spp + "\n" + integratorLine + "\nWorldBegin\n";
            s += "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [1 1 1] \"bool twosided\" true\nMaterial \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]\n";
            s += "Shape \"sphere\" \"float radius\" 3\nAttributeEnd\n";
            return s;
        }

        [TEST("Upg", "MediumFurnaceSphere")]
        public static void Upg_MediumFurnace()
        {
            int depth = 40;
            string common = " \"integer maxdepth\" " + depth + " \"integer photons\" 256";
            List<float[]> emission = RenderSeeds("mfurnace_le", FurnaceScene("Integrator \"bdpt\" \"integer maxdepth\" 0", 8, 16), 16, 1, 16);
            MeanAndError(emission, -1, out double le, out double leError);
            double expected = le / (1 - 0.5);
            Console.WriteLine("[Upg] medium furnace: the emission seen directly (no medium) " + le.ToString("G6", CultureInfo.InvariantCulture) + ", expected radiance " +
                              expected.ToString("G6", CultureInfo.InvariantCulture));
            string[] lines = new string[]
            {
                "Integrator \"bdpt\" \"integer maxdepth\" " + depth,
                "Integrator \"upg\"" + common + " \"float radius\" 0.5 \"bool upgonly\" true",
                "Integrator \"upg\"" + common + " \"float radius\" 0.5",
                "Integrator \"upg\"" + common + " \"float radius\" 0.08"
            };
            string[] labels = new string[] { "bdpt", "upgonly r 0.5", "upg+bdpt r 0.5", "upg+bdpt r 0.08" };
            for (int i = 0; i < lines.Length; ++i)
            {
                List<float[]> images = RenderSeeds("mfurnace_" + i, MediumFurnaceScene(lines[i], 8, 16), 16, 1 + 100 * i, 32);
                MeanAndError(images, -1, out double mean, out double error);
                double t = (mean - expected) / Math.Sqrt(error * error + leError * leError * 4);
                Console.WriteLine("[Upg] medium furnace " + labels[i] + ": mean " + mean.ToString("G6", CultureInfo.InvariantCulture) + " +- " + error.ToString("G3", CultureInfo.InvariantCulture) +
                                  ", expected " + expected.ToString("G6", CultureInfo.InvariantCulture) + ", t " + t.ToString("F2", CultureInfo.InvariantCulture));
                EXPECT_TRUE(Math.Abs(t) < 4.0);
            }
        }
    }
}
