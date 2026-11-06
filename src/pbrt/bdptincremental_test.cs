// (not in pbrt-v4) bdptincremental_test.cs: the tests of the "bdptinc" integrator (cpu/bdptincremental.cs, docs/bdpt-incremental.md)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using testing;

namespace pbrt
{
    public static unsafe class BdptIncrementalTests
    {
        // ------------------------------------------------------------------------------------------
        // Scenes

        static string Quad(string a, string b, string c, string d)
        {
            return "Shape \"trianglemesh\" \"integer indices\" [0 1 2 0 2 3] \"point3 P\" [" + a + " " + b + " " + c + " " + d + "]\n";
        }

        public static string MediumText(string kind)
        {
            switch (kind)
            {
            case "fog":
                return "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"spectrum sigma_a\" [300 0.05 900 0.05] \"spectrum sigma_s\" [300 0.35 900 0.35] \"float g\" 0\n";
            case "foghg":
                return "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0.05 0.05 0.05] \"rgb sigma_s\" [0.35 0.35 0.35] \"float g\" 0.6\n";
            case "fogchrom":
                return "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0.02 0.05 0.12] \"rgb sigma_s\" [0.15 0.4 0.9] \"float g\" 0.3\n";
            case "fograyleigh":
                return "MakeNamedMedium \"fog\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0.05 0.05 0.05] \"rgb sigma_s\" [0.5 0.5 0.5] \"float g\" 0 \"float rayleigh\" 1\n";
            case "fogemit":
                return MediumText("foghg");
            case "fognested":
                return MediumText("foghg") + "MakeNamedMedium \"ball\" \"string type\" \"homogeneous\" \"rgb sigma_a\" [0.1 0.2 0.3] \"rgb sigma_s\" [1.5 1.0 0.5] \"float g\" -0.3\n";
            case "foggrid":
            {
                int nx = 8, ny = 4, nz = 12;
                StringBuilder sb = new StringBuilder();
                sb.Append("MakeNamedMedium \"fog\" \"string type\" \"uniformgrid\" \"rgb sigma_a\" [0.05 0.04 0.03] \"rgb sigma_s\" [0.4 0.45 0.5] \"float g\" 0.3 " +
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
            default:
                return "";
            }
        }

        // The room (x in [-2, 2], y in [0, 2], z in [-4, 2]). variant: "room" (area light, glossy floor), "specular" (glass and mirror
        // spheres), "point", "spot", "distant", "infinite" (uniform), "polarised" (dielectric and conductor spheres). media: "" or
        // the name of a MediumText kind (the room is filled with it and the camera is in it)
        public static string RoomScene(string variant, string media, string integratorLine, int res, int spp)
        {
            string s = "Option \"rendercoordsys\" \"world\"\n";
            if (media.Length > 0)
            {
                s += MediumText(media) + "MediumInterface \"\" \"fog\"\n";
            }
            s += "LookAt 0 1.0 -3.6  0 1.0 0  0 1 0\nCamera \"perspective\" \"float fov\" 55\n";
            s += "Film \"rgb\" \"integer xresolution\" " + res + " \"integer yresolution\" " + res + " \"string filename\" \"out.exr\"\n";
            s += "Sampler \"independent\" \"integer pixelsamples\" " + spp + "\n" + integratorLine + "\nWorldBegin\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.7 0.7 0.7]\n";
            // (the uniform sky comes in through the missing ceiling)
            s += (variant == "infinite" || variant == "distant" ? "" : Quad("-2 2 -4", "2 2 -4", "2 2 2", "-2 2 2")) + Quad("-2 0 2", "2 0 2", "2 2 2", "-2 2 2") + Quad("2 0 2", "2 0 -4", "2 2 -4", "2 2 2") +
                 Quad("2 0 -4", "-2 0 -4", "-2 2 -4", "2 2 -4") + "AttributeEnd\n";
            s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.75 0.2 0.2]\n" + Quad("-2 0 -4", "-2 0 2", "-2 2 2", "-2 2 -4") + "AttributeEnd\n";
            if (variant == "cornell")
            {
                s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.5 0.5 0.5]\n" + Quad("-2 0 -4", "2 0 -4", "2 0 2", "-2 0 2") + "AttributeEnd\n";
            }
            else
            {
                s += "AttributeBegin\nMaterial \"conductor\" \"float roughness\" " + (variant == "glossy" ? "0.06 \"bool remaproughness\" false" : "0.12") +
                     " \"spectrum eta\" \"metal-Cu-eta\" \"spectrum k\" \"metal-Cu-k\"\n" + Quad("-2 0 -4", "2 0 -4", "2 0 2", "-2 0 2") + "AttributeEnd\n";
            }
            if (variant == "specular")
            {
                s += "AttributeBegin\nMaterial \"dielectric\" \"float eta\" 1.5\nTranslate 0.7 0.5 0.8\nShape \"sphere\" \"float radius\" 0.5\nAttributeEnd\n";
                s += "AttributeBegin\nMaterial \"conductor\" \"float roughness\" 0 \"spectrum eta\" \"metal-Cu-eta\" \"spectrum k\" \"metal-Cu-k\"\nTranslate -0.8 0.35 0.5\nShape \"sphere\" \"float radius\" 0.35\nAttributeEnd\n";
            }
            else if (variant == "polarised")
            {
                s += "AttributeBegin\nMaterial \"dielectric\" \"float eta\" 1.5\nTranslate 0.7 0.5 0.8\nShape \"sphere\" \"float radius\" 0.5\nAttributeEnd\n";
                s += "AttributeBegin\nMaterial \"conductor\" \"float roughness\" 0.15 \"spectrum eta\" \"metal-Cu-eta\" \"spectrum k\" \"metal-Cu-k\"\nTranslate -0.8 0.35 0.5\nShape \"sphere\" \"float radius\" 0.35\nAttributeEnd\n";
            }
            else
            {
                s += "AttributeBegin\nMaterial \"diffuse\" \"rgb reflectance\" [0.3 0.5 0.8]\nTranslate 0.8 0.35 0.6\nShape \"sphere\" \"float radius\" 0.35\nAttributeEnd\n";
            }
            if (media == "fogemit")
            {
                // a glowing ball (textured medium with emission) in the fog
                s += "AttributeBegin\nAttributeBegin\nTranslate -0.4 0.4 -1.0\nScale 1.2 1.2 1.2\nTexture \"dens\" \"float\" \"densitygrid\" \"integer nx\" 2 \"integer ny\" 2 \"integer nz\" 2 \"float density\" [ 1 0.6 0.8 1 0.7 1 0.9 0.5 ]\nTexture \"em\" \"spectrum\" \"constant\" \"rgb value\" [1 0.5 0.15]\n" +
                     "MakeNamedMedium \"glow\" \"string type\" \"texture\" \"string density\" \"dens\" \"rgb sigma_a\" [0.6 0.7 0.8] \"rgb sigma_s\" [0.8 0.8 0.8] \"float g\" 0.2 " +
                     "\"integer majorantres\" 4 \"string emission_texture\" \"em\" \"float Lescale\" 3 \"bool emission_nee\" true\nAttributeEnd\n" +
                     "MediumInterface \"glow\" \"fog\"\nMaterial \"interface\"\nTranslate 0.2 1.0 -0.4\nShape \"sphere\" \"float radius\" 0.6\nAttributeEnd\n";
            }
            if (media == "fognested")
            {
                s += "AttributeBegin\nMediumInterface \"ball\" \"fog\"\nMaterial \"interface\"\nTranslate 0.2 1.0 -0.4\nShape \"sphere\" \"float radius\" 0.6\nAttributeEnd\n";
            }
            switch (variant)
            {
            case "point":
                s += "LightSource \"point\" \"rgb I\" [8 8 8] \"point3 from\" [0 1.8 0.1]\n";
                break;
            case "spot":
                s += "LightSource \"spot\" \"rgb I\" [20 20 20] \"point3 from\" [0 1.9 0.1] \"point3 to\" [0 0 0.3] \"float coneangle\" 50\n";
                break;
            case "distant":
                s += "LightSource \"distant\" \"rgb L\" [1.5 1.5 1.5] \"point3 from\" [0.3 1 -0.2] \"point3 to\" [0 0 0]\n";
                break;
            case "infinite":
                s += "LightSource \"infinite\" \"rgb L\" [0.4 0.45 0.5]\n";
                break;
            case "glossy":
                s += "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [60 60 60] \"bool twosided\" false\n" +
                     Quad("-0.15 1.98 0.3", "0.15 1.98 0.3", "0.15 1.98 -0.1", "-0.15 1.98 -0.1") + "AttributeEnd\n";
                break;
            default:
                s += "AttributeBegin\nAreaLightSource \"diffuse\" \"rgb L\" [25 25 25] \"bool twosided\" false\n" +
                     Quad("-0.3 1.98 0.4", "0.3 1.98 0.4", "0.3 1.98 -0.2", "-0.3 1.98 -0.2") + "AttributeEnd\n";
                break;
            }
            return s;
        }

        // ------------------------------------------------------------------------------------------
        // Fixtures

        public sealed class Fixture
        {
            public Integrator integrator;
            public Camera camera;
            public Sampler sampler;
            public ScratchBuffer scratch = new ScratchBuffer(65536);

            public static Fixture Build(string text)
            {
                Fixture fixture = new Fixture();
                RenderingCoordinateSystem saved = Options.renderingSpace;
                try
                {
                    // (the emission lights of the media are collected in static lists: forget those of the earlier scenes)
                    TextureMedium.ClearEmissionLights();
                    BasicScene scene = new BasicScene();
                    BasicSceneBuilder builder = new BasicSceneBuilder(scene);
                    pbrt.ParseString(builder, text);
                    SortedDictionary<string, Medium> media = scene.CreateMedia();
                    NamedTextures textures = scene.CreateTextures();
                    foreach (KeyValuePair<string, Medium> m in media)
                    {
                        if (m.Value is TextureMedium textureMedium)
                        {
                            textureMedium.ResolveTextures(textures, new FileLoc());
                        }
                    }
                    Dictionary<int, List<Light>> shapeIndexToAreaLights = new Dictionary<int, List<Light>>();
                    List<Light> lights = scene.CreateLights(textures, shapeIndexToAreaLights);
                    SortedDictionary<string, Material> namedMaterials = new SortedDictionary<string, Material>(StringComparer.Ordinal);
                    List<Material> materials = new List<Material>();
                    scene.CreateMaterials(textures, namedMaterials, materials);
                    Primitive accel = scene.CreateAggregate(textures, shapeIndexToAreaLights, media, namedMaterials, materials);
                    fixture.camera = scene.GetCamera();
                    fixture.sampler = scene.GetSampler();
                    fixture.integrator = scene.CreateIntegrator(fixture.camera, fixture.sampler, accel, lights);
                }
                finally
                {
                    Options.renderingSpace = saved;
                }
                return fixture;
            }
        }

        // One strategy of a sample: its connection, the radiance with the weight, and the weight
        struct Entry
        {
            public int s, t;
            public Float weight;
            public SampledSpectrum L;
        }

        // The strategies of one pixel sample, as the Li of the integrator forms them: every (s, t) with its weight and its contribution,
        // the equiangular contributions and the contributions of the emission ends. The sampler is restarted before every connection, so
        // that two integrators draw the same numbers in the connections.
        static List<Entry> StrategiesOfSample(Fixture fx, bool incremental, Point2i pixel, int sampleIndex, bool polarised, int maxDepth,
                                              bool regularize, out SampledSpectrum extra, out int ends)
        {
            Camera camera = fx.camera;
            Sampler sampler = fx.sampler;
            List<Entry> entries = new List<Entry>();
            extra = new SampledSpectrum(0.0f);
            ends = 0;
            sampler.StartPixelSample(pixel, sampleIndex);
            Float lu = sampler.Get1D();
            SampledWavelengths lambda = camera.GetFilm().SampleWavelengths(lu);
            CameraSample cameraSample = GetCameraSample(sampler, pixel, camera.GetFilm().GetFilter());
            pstd.optional<CameraRayDifferential> cameraRay = camera.GenerateRayDifferential(cameraSample, ref lambda);
            if (!cameraRay)
            {
                return entries;
            }
            RayDifferential ray = cameraRay.value().ray;
            Integrator integrator = fx.integrator;
            fx.scratch.Reset();
            PolarisedPathIntegrator.Sensor sensor = null;
            if (polarised)
            {
                sensor = new PolarisedPathIntegrator.Sensor();
                sensor.cameraUp = camera.GetCameraTransform().RenderFromCamera(new Vector3f(0, 1, 0), 0);
            }
            LightSampler lightSampler = incremental ? ((BDPTIncrementalIntegrator)integrator).BDPTLightSampler
                                                    : ((BDPTIntegrator)integrator).BDPTLightSampler;
            Vertex[] cameraVertices = new Vertex[maxDepth + 2];
            for (int i = 0; i < cameraVertices.Length; ++i)
            {
                cameraVertices[i] = new Vertex();
            }
            Vertex[] lightVertices = new Vertex[maxDepth + 1];
            for (int i = 0; i < lightVertices.Length; ++i)
            {
                lightVertices[i] = new Vertex();
            }
            List<MediumEmissionEnd> emissionEnds = integrator.gatherMediumEmission ? new List<MediumEmissionEnd>() : null;
            BDPTEquiangularSink sink = integrator.bdptEquiangular ? new BDPTEquiangularSink(maxDepth) : null;
            VertexMIS[] cameraState = new VertexMIS[maxDepth + 2];
            VertexMIS[] lightState = new VertexMIS[maxDepth + 1];
            Film film = camera.GetFilm();
            Float splatScale = (Float)film.FullResolution().x * (Float)film.FullResolution().y / (Float)film.PixelBounds().Area();
            int nCamera, nLight;
            if (incremental)
            {
                nCamera = BDPTIncremental.GenerateCameraSubpath(integrator, ray, ref lambda, sampler, fx.scratch, maxDepth + 2, camera,
                                                                cameraVertices, cameraState, regularize, splatScale, polarised,
                                                                emissionEnds, sink);
                nLight = BDPTIncremental.GenerateLightSubpath(integrator, ref lambda, sampler, camera, fx.scratch,
                                                              integrator.unidirectionalStrategies ? 1 : maxDepth + 1,
                                                              cameraVertices[0].time(), lightSampler, lightVertices, lightState,
                                                              regularize, splatScale, polarised);
            }
            else
            {
                nCamera = BDPT.GenerateCameraSubpath(integrator, ray, ref lambda, sampler, fx.scratch, maxDepth + 2, camera, cameraVertices,
                                                     regularize, polarised, emissionEnds, sink);
                nLight = BDPT.GenerateLightSubpath(integrator, ref lambda, sampler, camera, fx.scratch,
                                                   integrator.unidirectionalStrategies ? 1 : maxDepth + 1, cameraVertices[0].time(),
                                                   lightSampler, lightVertices, regularize, polarised);
            }
            if (sink != null)
            {
                extra += sink.L;
            }
            for (int t = 1; t <= nCamera; ++t)
            {
                for (int s = 0; s <= nLight; ++s)
                {
                    int depth = t + s - 2;
                    if ((s == 1 && t == 1 && !BDPT.IsVolumeEmission(lightVertices[0])) || depth < 0 || depth > maxDepth ||
                        !integrator.StrategyAllowed(s, t))
                    {
                        continue;
                    }
                    sampler.StartPixelSample(pixel, sampleIndex + 100000);
                    pstd.optional<Point2f> pFilm = default;
                    Float weight = 0;
                    SampledSpectrum L = incremental
                        ? BDPTIncremental.ConnectBDPT(integrator, ref lambda, lightVertices, lightState, cameraVertices, cameraState, s, t,
                                                      lightSampler, camera, sampler, ref pFilm, ref weight, sensor, -ray.d)
                        : BDPT.ConnectBDPT(integrator, ref lambda, lightVertices, cameraVertices, s, t, lightSampler, camera, sampler,
                                           ref pFilm, ref weight, sensor, -ray.d);
                    Entry e = new Entry();
                    e.s = s;
                    e.t = t;
                    e.weight = weight;
                    e.L = L;
                    entries.Add(e);
                }
            }
            if (emissionEnds != null)
            {
                ends = emissionEnds.Count;
                foreach (MediumEmissionEnd end in emissionEnds)
                {
                    int depth = end.index - 1;
                    if (end.index > nCamera || depth < 0 || depth > maxDepth)
                    {
                        continue;
                    }
                    sampler.StartPixelSample(pixel, sampleIndex + 100000);
                    extra += incremental
                        ? BDPTIncremental.ConnectEmissionEnd(integrator, ref lambda, cameraVertices, cameraState, end, lightSampler, camera,
                                                             sampler, sensor, -ray.d)
                        : BDPT.ConnectEmissionEnd(integrator, ref lambda, cameraVertices, end, lightSampler, camera, sampler, sensor, -ray.d);
                }
            }
            return entries;
        }

        sealed class WeightStats
        {
            public int emissionEnds, samples, strategies, weightsBelowOne, mismatches;
            public double worstWeight, worstRadiance;
            public Float worstExtra;
            public double largestExtra;
            public string worstCase = "";
        }

        // The weights (and the contributions) of the strategies of the same pixel samples under the two integrators
        static WeightStats CompareWeights(string label, string media, string variant, string integratorExtra, int maxDepth, int pixels,
                                          int samplesPerPixel, bool polarised, double tolerance)
        {
            string line = "Integrator \"INT\" \"integer maxdepth\" " + maxDepth + " " + integratorExtra;
            Fixture oldFx = Fixture.Build(RoomScene(variant, media, line.Replace("INT", "bdpt"), 16, 1));
            Fixture newFx = Fixture.Build(RoomScene(variant, media, line.Replace("INT", "bdptinc"), 16, 1));
            WeightStats stats = new WeightStats();
            for (int p = 0; p < pixels; ++p)
            {
                Point2i pixel = new Point2i((p * 5 + 3) % 16, (p * 7 + 1) % 16);
                for (int k = 0; k < samplesPerPixel; ++k)
                {
                    List<Entry> a = StrategiesOfSample(oldFx, false, pixel, k, polarised, maxDepth, false, out SampledSpectrum extraOld, out int endsOld);
                    List<Entry> b = StrategiesOfSample(newFx, true, pixel, k, polarised, maxDepth, false, out SampledSpectrum extraNew, out int endsNew);
                    stats.emissionEnds += endsNew;
                    EXPECT_EQ(endsOld, endsNew);
                    ++stats.samples;
                    EXPECT_EQ(a.Count, b.Count);
                    int n = Math.Min(a.Count, b.Count);
                    for (int i = 0; i < n; ++i)
                    {
                        ++stats.strategies;
                        EXPECT_EQ(a[i].s, b[i].s);
                        EXPECT_EQ(a[i].t, b[i].t);
                        double wa = a[i].weight, wb = b[i].weight;
                        double rel = Math.Abs(wa - wb) / Math.Max(Math.Max(Math.Abs(wa), Math.Abs(wb)), 1e-30);
                        if ((bool)a[i].L || (bool)b[i].L)
                        {
                            if (wa < 0.999)
                            {
                                ++stats.weightsBelowOne;
                            }
                            if (rel > stats.worstWeight)
                            {
                                stats.worstWeight = rel;
                                stats.worstCase = "pixel " + pixel + " sample " + k + " (s=" + a[i].s + ", t=" + a[i].t + ") old " + wa.ToString("G9", CultureInfo.InvariantCulture) +
                                                  " new " + wb.ToString("G9", CultureInfo.InvariantCulture);
                            }
                            if (rel > tolerance)
                            {
                                ++stats.mismatches;
                            }
                            double ra = a[i].L.Average(), rb = b[i].L.Average();
                            double rrel = Math.Abs(ra - rb) / Math.Max(Math.Max(Math.Abs(ra), Math.Abs(rb)), 1e-30);
                            stats.worstRadiance = Math.Max(stats.worstRadiance, rrel);
                        }
                    }
                    double ea = extraOld.Average(), eb = extraNew.Average();
                    stats.largestExtra = Math.Max(stats.largestExtra, Math.Max(Math.Abs(ea), Math.Abs(eb)));
                    double erel = Math.Abs(ea - eb) / Math.Max(Math.Max(Math.Abs(ea), Math.Abs(eb)), 1e-30);
                    stats.worstExtra = Math.Max(stats.worstExtra, (Float)erel);
                    if (erel > tolerance)
                    {
                        ++stats.mismatches;
                    }
                }
            }
            Console.WriteLine("[BdptInc] weights " + label + ": " + stats.samples + " samples, " + stats.strategies + " strategies, " + stats.weightsBelowOne +
                              " with weight < 1, worst relative weight difference " + stats.worstWeight.ToString("G3", CultureInfo.InvariantCulture) +
                              " (" + stats.worstCase + "), worst contribution difference " + stats.worstRadiance.ToString("G3", CultureInfo.InvariantCulture) +
                              ", " + stats.emissionEnds + " emission ends, worst equiangular/emission-end difference " + stats.worstExtra.ToString("G3", CultureInfo.InvariantCulture) + " (largest such contribution " + stats.largestExtra.ToString("G3", CultureInfo.InvariantCulture) + ")" +
                              ", beyond " + tolerance.ToString("G2", CultureInfo.InvariantCulture) + ": " + stats.mismatches);
            return stats;
        }

        const double WeightTolerance = 2e-4;

        [TEST("BdptInc", "WeightsEqualBdptSurfaces")]
        public static void BdptInc_WeightsEqualBdptSurfaces()
        {
            WeightStats a = CompareWeights("room, area light", "", "room", "", 8, 30, 3, false, WeightTolerance);
            EXPECT_EQ(a.mismatches, 0);
            EXPECT_GT(a.weightsBelowOne, 100);
        }

        [TEST("BdptInc", "WeightsEqualBdptSpecularAndLightTypes")]
        public static void BdptInc_WeightsEqualBdptSpecularAndLightTypes()
        {
            foreach (string variant in new string[] { "specular", "point", "spot", "distant", "infinite" })
            {
                WeightStats a = CompareWeights(variant, "", variant, "", 8, 30, 3, false, WeightTolerance);
                EXPECT_EQ(a.mismatches, 0);
                EXPECT_GT(a.strategies, 100);
            }
        }

        [TEST("BdptInc", "WeightsEqualBdptMedia")]
        public static void BdptInc_WeightsEqualBdptMedia()
        {
            foreach (string media in new string[] { "fog", "foghg", "fogchrom", "foggrid", "fograyleigh", "fognested" })
            {
                WeightStats a = CompareWeights(media, media, "room", "", 8, 24, 3, false, WeightTolerance);
                EXPECT_EQ(a.mismatches, 0);
                EXPECT_GT(a.weightsBelowOne, 50);
            }
        }

        [TEST("BdptInc", "WeightsEqualBdptEmissiveMedium")]
        public static void BdptInc_WeightsEqualBdptEmissiveMedium()
        {
            WeightStats a = CompareWeights("emissive medium", "fogemit", "room", "", 6, 200, 3, false, WeightTolerance);
            EXPECT_EQ(a.mismatches, 0);
            WeightStats b = CompareWeights("emissive medium, no gathering", "fogemit", "room", "\"bool gathermediumemission\" false", 6, 20, 3, false, WeightTolerance);
            EXPECT_EQ(b.mismatches, 0);
        }

        [TEST("BdptInc", "WeightsEqualBdptEquiangular")]
        public static void BdptInc_WeightsEqualBdptEquiangular()
        {
            WeightStats a = CompareWeights("equiangular, point light", "foghg", "point", "\"bool equiangular\" true", 6, 30, 3, false, WeightTolerance);
            EXPECT_EQ(a.mismatches, 0);
            WeightStats b = CompareWeights("equiangular, area light, 3 samples", "fogchrom", "room", "\"bool equiangular\" true \"integer equiangularsamples\" 3", 6, 30, 3, false, WeightTolerance);
            EXPECT_EQ(b.mismatches, 0);
        }

        [TEST("BdptInc", "WeightsEqualBdptUnidirectional")]
        public static void BdptInc_WeightsEqualBdptUnidirectional()
        {
            WeightStats a = CompareWeights("unidirectional strategies", "", "room", "\"string strategies\" \"unidirectional\"", 6, 30, 3, false, WeightTolerance);
            EXPECT_EQ(a.mismatches, 0);
            WeightStats b = CompareWeights("unidirectional strategies, fog", "fog", "room", "\"string strategies\" \"unidirectional\"", 6, 20, 3, false, WeightTolerance);
            EXPECT_EQ(b.mismatches, 0);
        }

        [TEST("BdptInc", "WeightsEqualBdptPolarised")]
        public static void BdptInc_WeightsEqualBdptPolarised()
        {
            WeightStats a = CompareWeights("polarised room", "", "polarised", "\"bool polarised\" true", 6, 30, 3, true, WeightTolerance);
            EXPECT_EQ(a.mismatches, 0);
            WeightStats b = CompareWeights("polarised fog, Rayleigh", "fograyleigh", "polarised", "\"bool polarised\" true \"string measure\" \"s1\"", 6, 20, 3, true, WeightTolerance);
            EXPECT_EQ(b.mismatches, 0);
        }
    }
}
