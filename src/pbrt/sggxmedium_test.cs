// (not in pbrt-v4) Tests of sggxmedium.cs: the medium of SGGX microflakes.

using System;
using testing;

namespace pbrt
{
    public static unsafe class SGGXMediumTests
    {
        private static Vector3f OnSphere(RNG rng)
        {
            Float z = 1 - 2 * rng.Uniform<Float>(), phi = 2 * Pi * rng.Uniform<Float>(), r = SafeSqrt(1 - z * z);
            return new Vector3f(r * std.cos(phi), r * std.sin(phi), z);
        }

        private static SGGXMatrix[] Matrices()
        {
            Vector3f axis = Normalize(new Vector3f(0.3f, -0.5f, 0.8f));
            return new SGGXMatrix[]
            {
                SGGXMatrix.Uniaxial(axis, 1, 0.3f * 0.3f), SGGXMatrix.Uniaxial(axis, 0.3f * 0.3f, 1), SGGXMatrix.Uniaxial(axis, 1, 0.05f * 0.05f),
                SGGXMatrix.Uniaxial(new Vector3f(0, 0, 1), 0.6f * 0.6f, 1), new SGGXMatrix(1, 1, 1, 0, 0, 0),
                new SGGXMatrix(0.9f, 0.5f, 0.2f, 0.15f, -0.1f, 0.05f)
            };
        }

        // The phase function integrates to 1 over the directions, for any direction it is asked from, and the projected area is
        // what the distribution of the normals gives: the integral of D(m) <w, m> over the half of the normals that face w
        [TEST("SGGXMedium", "PhaseFunctionIsNormalised")]
        public static void PhaseFunctionIsNormalised()
        {
            RNG rng = new RNG(3);
            double worstPhase = 0, worstArea = 0;
            foreach (SGGXMatrix S in Matrices())
            {
                SGGXPhaseFunction phase = new SGGXPhaseFunction(S, new SampledSpectrum(SGGXPhaseFunction.PerfectMirrorK));
                for (int trial = 0; trial < 3; ++trial)
                {
                    Vector3f wo = OnSphere(rng);
                    // (the sphere by latitude and longitude around wo, where the phase function is smooth in both)
                    Vector3f t1, t2;
                    CoordinateSystem(wo, &t1, &t2);
                    int nTheta = 3000, nPhi = 1200;
                    double sum = 0, area = 0;
                    for (int i = 0; i < nTheta; ++i)
                    {
                        double theta = Math.PI * (i + 0.5) / nTheta;
                        double sine = Math.Sin(theta), cosine = Math.Cos(theta);
                        for (int j = 0; j < nPhi; ++j)
                        {
                            double phi = 2 * Math.PI * (j + 0.5) / nPhi;
                            Vector3f w = (Float)(sine * Math.Cos(phi)) * t1 + (Float)(sine * Math.Sin(phi)) * t2 + (Float)cosine * wo;
                            double weight = sine * (Math.PI / nTheta) * (2 * Math.PI / nPhi);
                            sum += phase.p(wo, w) * weight;
                            if (cosine > 0)
                            {
                                area += phase.D(w) * cosine * weight;
                            }
                        }
                    }
                    worstPhase = Math.Max(worstPhase, Math.Abs(sum - 1));
                    worstArea = Math.Max(worstArea, Math.Abs(area / S.ProjectedArea(wo) - 1));
                }
            }
            Console.WriteLine("SGGX: the phase function integrates to 1 within " + worstPhase + "; the projected area is that of the normals within " + worstArea);
            CHECK_LT(worstPhase, 5e-3);
            CHECK_LT(worstArea, 5e-3);
        }

        // The samples have the density that they say: a function of the direction averages over them, divided by nothing (the
        // weight p / pdf is 1), to its integral against the phase function; and what the medium scatters from one direction to
        // another, projected area times phase function, is the same both ways
        [TEST("SGGXMedium", "SamplingAndReciprocity")]
        public static void SamplingAndReciprocity()
        {
            RNG rng = new RNG(8);
            double worstMoment = 0, worstReciprocity = 0, worstWeight = 0;
            foreach (SGGXMatrix S in Matrices())
            {
                SGGXPhaseFunction phase = new SGGXPhaseFunction(S, new SampledSpectrum(SGGXPhaseFunction.PerfectMirrorK));
                Vector3f wo = OnSphere(rng);
                Vector3f probe = OnSphere(rng);
                int n = 400000;
                double sampled = 0, sampled2 = 0;
                for (int i = 0; i < n; ++i)
                {
                    pstd.optional<PhaseFunctionSample> s = phase.Sample_p(wo, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (!s)
                    {
                        continue;
                    }
                    PhaseFunctionSample sample = s.value();
                    worstWeight = Math.Max(worstWeight, Math.Abs(sample.p / sample.pdf - 1));
                    EXPECT_NEAR(1, Length(sample.wi), 1e-4f);
                    double d = Dot(sample.wi, probe);
                    sampled += d / n;
                    sampled2 += d * d / n;
                }
                // (the same two moments as integrals against the phase function, over latitude and longitude around wo)
                Vector3f t1, t2;
                CoordinateSystem(wo, &t1, &t2);
                int nTheta = 3000, nPhi = 1200;
                double uniform = 0, uniform2 = 0;
                for (int i = 0; i < nTheta; ++i)
                {
                    double theta = Math.PI * (i + 0.5) / nTheta;
                    double sine = Math.Sin(theta), cosine = Math.Cos(theta);
                    for (int j = 0; j < nPhi; ++j)
                    {
                        double phi = 2 * Math.PI * (j + 0.5) / nPhi;
                        Vector3f w = (Float)(sine * Math.Cos(phi)) * t1 + (Float)(sine * Math.Sin(phi)) * t2 + (Float)cosine * wo;
                        double value = phase.p(wo, w) * sine * (Math.PI / nTheta) * (2 * Math.PI / nPhi);
                        double d = Dot(w, probe);
                        uniform += value * d;
                        uniform2 += value * d * d;
                    }
                }
                worstMoment = Math.Max(worstMoment, Math.Max(Math.Abs(sampled - uniform), Math.Abs(sampled2 - uniform2)));
                for (int i = 0; i < 2000; ++i)
                {
                    Vector3f a = OnSphere(rng), b = OnSphere(rng);
                    double forward = S.ProjectedArea(a) * phase.p(a, b), backward = S.ProjectedArea(b) * phase.p(b, a);
                    worstReciprocity = Math.Max(worstReciprocity, Math.Abs(forward - backward) / Math.Max(forward, 1e-12));
                }
            }
            Console.WriteLine("SGGX: moments of the samples against the integral differ by " + worstMoment + "; reciprocity within " + worstReciprocity +
                              "; weights off 1 by " + worstWeight);
            CHECK_LT(worstMoment, 0.004);
            CHECK_LT(worstReciprocity, 1e-4);
            CHECK_LT(worstWeight, 1e-6);
        }

        // The medium: the coefficients of the medium inside times the projected area for the direction, the majorant of a ray
        // never under them, the largest projected area that of the matrix, and no answer without a direction
        [TEST("SGGXMedium", "ExtinctionFollowsTheDirection")]
        public static void ExtinctionFollowsTheDirection()
        {
            RNG rng = new RNG(12);
            Spectrum sigma_a = new ConstantSpectrum(0.5f), sigma_s = new ConstantSpectrum(2);
            HomogeneousMedium inner = new HomogeneousMedium(sigma_a, sigma_s, 1, new ConstantSpectrum(0), 1, 0, Allocator.Default);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.4f);
            foreach (SGGXMatrix S in Matrices())
            {
                SGGXMedium medium = new SGGXMedium(inner, S, null);
                double largest = 0;
                for (int i = 0; i < 20000; ++i)
                {
                    Vector3f w = OnSphere(rng);
                    Float area = S.ProjectedArea(w);
                    largest = Math.Max(largest, area);
                    if (i < 200)
                    {
                        Point3f p = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                        // (a direction of any length, either way)
                        MediumProperties mp = medium.SamplePoint(p, (i % 2 == 0 ? 3.5f : -0.2f) * w, lambda, 0);
                        EXPECT_NEAR(0.5f * area, mp.sigma_a[0], 1e-5f);
                        EXPECT_NEAR(2 * area, mp.sigma_s[0], 1e-5f);
                        CHECK(mp.phase is SGGXPhaseFunction);
                        Ray ray = new Ray(p, 2 * w);
                        RayMajorantIterator iterator = medium.SampleRay(ray, 1, lambda, new ScratchBuffer());
                        pstd.optional<RayMajorantSegment> segment = iterator.Next();
                        CHECK((bool)segment);
                        CHECK_GE(segment.value().sigma_maj[0], mp.sigma_a[0] + mp.sigma_s[0]);
                        CHECK_LE(segment.value().sigma_maj[0], 1.001f * (mp.sigma_a[0] + mp.sigma_s[0]));
                    }
                }
                CHECK_LE(largest, S.LargestProjectedArea() * 1.0001);
                CHECK_GE(largest, S.LargestProjectedArea() * 0.995);
                bool refused = false;
                try
                {
                    medium.SamplePoint(new Point3f(0, 0, 0), lambda, 0);
                }
                catch (InvalidOperationException)
                {
                    refused = true;
                }
                CHECK(refused);
            }
        }

        // Diffuse flakes: the cosine lobe around one drawn normal, averaged over the normals, is the phase function of the
        // paper. For flakes that face every direction alike that is the phase function of a matte sphere,
        // 2 / (3 pi^2) (sin a + (pi - a) cos a) with a the angle between the two directions; for any matrix the mean is the
        // integral of <wi, m> / pi against the normals that wo sees, and what the medium scatters is the same both ways.
        [TEST("SGGXMedium", "DiffuseFlakes")]
        public static void DiffuseFlakes()
        {
            RNG rng = new RNG(21);
            SampledSpectrum mirror = new SampledSpectrum(SGGXPhaseFunction.PerfectMirrorK);
            int n = 400000;

            SGGXPhaseFunction sphere = new SGGXPhaseFunction(new SGGXMatrix(1, 1, 1, 0, 0, 0), mirror);
            double worstSphere = 0;
            for (int trial = 0; trial < 6; ++trial)
            {
                Vector3f wo = OnSphere(rng), wi = OnSphere(rng);
                double mean = 0;
                for (int i = 0; i < n; ++i)
                {
                    mean += new SGGXDiffusePhaseFunction(sphere, wo, (ulong)(trial * n + i)).p(wo, wi) / n;
                }
                double angle = Math.Acos(Math.Clamp((double)Dot(wo, wi), -1.0, 1.0));
                double expected = 2 / (3 * Math.PI * Math.PI) * (Math.Sin(angle) + (Math.PI - angle) * Math.Cos(angle));
                worstSphere = Math.Max(worstSphere, Math.Abs(mean - expected));
            }

            double worstMean = 0, worstReciprocity = 0, worstLobe = 0;
            foreach (SGGXMatrix S in Matrices())
            {
                SGGXPhaseFunction flakes = new SGGXPhaseFunction(S, mirror);
                Vector3f a = OnSphere(rng), b = OnSphere(rng);
                double fromA = 0, fromB = 0;
                for (int i = 0; i < n; ++i)
                {
                    fromA += new SGGXDiffusePhaseFunction(flakes, a, (ulong)(7 * n + i)).p(a, b) / n;
                    fromB += new SGGXDiffusePhaseFunction(flakes, b, (ulong)(9 * n + i)).p(b, a) / n;
                }
                // (the integral over the normals m that a sees: D(m) <a, m> / sigma(a) times <b, m> / pi)
                Vector3f t1, t2;
                CoordinateSystem(a, &t1, &t2);
                int nTheta = 1500, nPhi = 800;
                double integral = 0;
                for (int i = 0; i < nTheta; ++i)
                {
                    double theta = 0.5 * Math.PI * (i + 0.5) / nTheta;
                    double sine = Math.Sin(theta), cosine = Math.Cos(theta);
                    for (int j = 0; j < nPhi; ++j)
                    {
                        double phi = 2 * Math.PI * (j + 0.5) / nPhi;
                        Vector3f m = (Float)(sine * Math.Cos(phi)) * t1 + (Float)(sine * Math.Sin(phi)) * t2 + (Float)cosine * a;
                        integral += flakes.D(m) * cosine / S.ProjectedArea(a) * Math.Max(0.0, (double)Dot(b, m)) / Math.PI * sine *
                                    (0.5 * Math.PI / nTheta) * (2 * Math.PI / nPhi);
                    }
                }
                worstMean = Math.Max(worstMean, Math.Abs(fromA - integral));
                worstReciprocity = Math.Max(worstReciprocity, Math.Abs(S.ProjectedArea(a) * fromA - S.ProjectedArea(b) * fromB));

                // One lobe: its samples have the density that they say, and the normal faces the arriving ray
                SGGXDiffusePhaseFunction lobe = new SGGXDiffusePhaseFunction(flakes, a, 12345);
                CHECK_GT(Dot(lobe.Normal(), a), 0);
                double total = 0;
                for (int i = 0; i < 20000; ++i)
                {
                    pstd.optional<PhaseFunctionSample> sample = lobe.Sample_p(a, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if ((bool)sample)
                    {
                        worstLobe = Math.Max(worstLobe, Math.Abs(sample.value().p / sample.value().pdf - 1));
                        EXPECT_NEAR(std.max<Float>(0, Dot(sample.value().wi, lobe.Normal())) / Pi, sample.value().pdf, 1e-5f);
                    }
                    Vector3f w = OnSphere(rng);
                    total += lobe.p(a, w) * 4 * Math.PI / 20000;
                }
                EXPECT_NEAR(1.0, total, 0.03);
            }
            Console.WriteLine("SGGX diffuse: the mean lobe of flakes without a direction is off the matte sphere by " + worstSphere +
                              "; of the six matrices off the integral by " + worstMean + ", off reciprocity by " + worstReciprocity + "; weights off 1 by " + worstLobe);
            CHECK_LT(worstSphere, 1.5e-3);
            CHECK_LT(worstMean, 1.5e-3);
            CHECK_LT(worstReciprocity, 2e-3);
            CHECK_LT(worstLobe, 1e-5);
        }

        // The fields: with textures for the axis and the roughness the matrix at a point is that of their values there, the
        // extinction follows it, and the majorant of a ray is never under it
        [TEST("SGGXMedium", "OrientationField")]
        public static void OrientationField()
        {
            RNG rng = new RNG(5);
            HomogeneousMedium inner = new HomogeneousMedium(new ConstantSpectrum(0.5f), new ConstantSpectrum(2), 1, new ConstantSpectrum(0), 1, 0, Allocator.Default);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.7f);
            // (the medium turned a quarter about z: its x axis is the y axis of render space)
            Transform renderFromMedium = Rotate(90, new Vector3f(0, 0, 1));
            foreach (bool fibres in new bool[] { true, false })
            {
                SGGXMedium medium = new SGGXMedium(inner, SGGXMatrix.Uniaxial(new Vector3f(0, 0, 1), 1, 0.09f), null);
                medium.SetFields(new SGGXField[] { new SGGXTextureField("ax"), new SGGXTextureField("ay"), new SGGXTextureField("az") }, new SGGXTextureField("rough"),
                                 fibres, new Vector3f(0, 0, 1), 0.3f, renderFromMedium);
                NamedTextures textures = new NamedTextures();
                textures.floatTextures["ax"] = new FloatConstantTexture(2);
                textures.floatTextures["ay"] = new FloatConstantTexture(0);
                textures.floatTextures["az"] = new FloatConstantTexture(0);
                textures.floatTextures["rough"] = new FloatConstantTexture(0.2f);
                medium.ResolveTextures(textures, new FileLoc());
                CHECK(medium.HasFields());
                SGGXMatrix expected = fibres ? SGGXMatrix.Uniaxial(new Vector3f(0, 1, 0), 1, 0.04f) : SGGXMatrix.Uniaxial(new Vector3f(0, 1, 0), 0.04f, 1);
                SGGXMatrix here = medium.MatrixAt(new Point3f(0.3f, 0.1f, -2));
                EXPECT_NEAR(expected.xx, here.xx, 1e-5f);
                EXPECT_NEAR(expected.yy, here.yy, 1e-5f);
                EXPECT_NEAR(expected.zz, here.zz, 1e-5f);
                EXPECT_NEAR(expected.xy, here.xy, 1e-5f);
                for (int i = 0; i < 200; ++i)
                {
                    Vector3f w = OnSphere(rng);
                    Point3f p = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                    MediumProperties mp = medium.SamplePoint(p, w, lambda, 0);
                    EXPECT_NEAR(2 * expected.ProjectedArea(w), mp.sigma_s[0], 1e-4f);
                    RayMajorantIterator iterator = medium.SampleRay(new Ray(p, w), 1, lambda, new ScratchBuffer());
                    CHECK_GE(iterator.Next().value().sigma_maj[0], mp.sigma_a[0] + mp.sigma_s[0]);
                }
            }
        }

        // A homogeneous medium with these parameters ("type name" and a value or [values], as in a scene), turned a quarter
        // about z: x of the medium is y of render space
        static Medium Flakes(string text)
        {
            System.Collections.Generic.List<ParsedParameter> list = new System.Collections.Generic.List<ParsedParameter>();
            text = "\"rgb sigma_a\" [0.5 0.5 0.5] \"rgb sigma_s\" [2 2 2] " + text;
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(text, "\"([a-z0-9]+) ([A-Za-z_0-9]+)\"\\s*(\\[[^\\]]*\\]|\"[^\"]*\"|\\S+)"))
            {
                ParsedParameter parameter = new ParsedParameter(new FileLoc());
                parameter.type = match.Groups[1].Value;
                parameter.name = match.Groups[2].Value;
                string values = match.Groups[3].Value.Trim('[', ']');
                if (parameter.type == "string")
                {
                    foreach (System.Text.RegularExpressions.Match quoted in System.Text.RegularExpressions.Regex.Matches(values, "\"([^\"]*)\""))
                    {
                        parameter.AddString(quoted.Groups[1].Value);
                    }
                }
                else
                {
                    foreach (string value in values.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (parameter.type == "integer")
                        {
                            parameter.AddInt(int.Parse(value));
                        }
                        else
                        {
                            parameter.AddFloat((Float)double.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }
                }
                list.Add(parameter);
            }
            return Medium.Create("homogeneous", new ParameterDictionary(list, RGBColorSpace.sRGB), Rotate(90, new Vector3f(0, 0, 1)), new FileLoc(), Allocator.Default);
        }

        static string SceneFile(string directoryName, string name)
        {
            string directory = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && directory != null; ++i)
            {
                string candidate = System.IO.Path.Combine(directory, "scenes", directoryName, name);
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
                directory = System.IO.Path.GetDirectoryName(directory);
            }
            return null;
        }

        // The fields from the values of a grid: a grid of one value is the uniform medium; a grid of matrices gives the
        // matrix of the voxel (turned with the medium), no flakes where it has none, never more than "sggxmax", and a
        // majorant that holds between the voxels too
        [TEST("SGGXMedium", "GridFields")]
        public static void GridFields()
        {
            RNG rng = new RNG(17);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.3f);
            // (the medium is turned a quarter about z: x of the medium is y of render space)
            SGGXMedium axis = (SGGXMedium)Flakes("\"string microflakes\" \"fibre\" \"float flakeroughness\" 0.2 \"integer flakenx\" 2 \"integer flakeny\" 1 \"integer flakenz\" 1 " +
                                                 "\"float flakeaxisgrid\" [3 0 0  3 0 0] \"float flakeroughnessgrid\" [0.4 0.4]");
            CHECK(axis.HasFields());
            SGGXMatrix expected = SGGXMatrix.Uniaxial(new Vector3f(0, 1, 0), 1, 0.16f);
            // (render space (-y, x) is the medium's (x, y): the point is inside the unit box of the medium)
            Point3f inside = new Point3f(-0.5f, 0.5f, 0.5f);
            SGGXMatrix here = axis.MatrixAt(inside);
            EXPECT_NEAR(expected.xx, here.xx, 1e-5f);
            EXPECT_NEAR(expected.yy, here.yy, 1e-5f);
            EXPECT_NEAR(expected.zz, here.zz, 1e-5f);
            // (outside the grid the values are zero: the axis of "flakeaxis", z, and the least roughness)
            SGGXMatrix outside = axis.MatrixAt(new Point3f(5, 5, 5));
            EXPECT_NEAR(1, outside.xx, 1e-5f);
            EXPECT_NEAR(0.0001f, outside.zz, 1e-6f);

            // Matrices: voxel 0 has none, voxel 1 fibres along the medium's x of projected area up to 2, voxel 2 a matrix with
            // all six coefficients
            string six = "0 0 0 0 0 0   0.04 4 4 0 0 0   0.5 0.3 0.2 0.1 0.05 -0.02";
            SGGXMedium matrices = (SGGXMedium)Flakes("\"string microflakes\" \"matrix\" \"float sggx\" [0.25 0.25 0.25 0 0 0] \"integer flakenx\" 3 \"integer flakeny\" 1 " +
                                                     "\"integer flakenz\" 1 \"point3 flakep0\" [0 0 0] \"point3 flakep1\" [3 1 1] \"float sggxgrid\" [" + six + "]");
            // (the centres of the voxels, medium (0.5, 1.5, 2.5; 0.5; 0.5) = render (-0.5, x, 0.5))
            SGGXMatrix none = matrices.MatrixAt(new Point3f(-0.5f, 0.5f, 0.5f));
            EXPECT_NEAR(0.25f, none.xx, 1e-6f);
            EXPECT_NEAR(0.25f, none.zz, 1e-6f);
            SGGXMatrix fibres = matrices.MatrixAt(new Point3f(-0.5f, 1.5f, 0.5f));
            EXPECT_NEAR(4, fibres.xx, 1e-4f);
            EXPECT_NEAR(0.04f, fibres.yy, 1e-5f);
            EXPECT_NEAR(4, fibres.zz, 1e-4f);
            SGGXMatrix full = matrices.MatrixAt(new Point3f(-0.5f, 2.5f, 0.5f));
            // (R S R^T for the quarter turn: x -> y, y -> -x)
            EXPECT_NEAR(0.3f, full.xx, 1e-5f);
            EXPECT_NEAR(0.5f, full.yy, 1e-5f);
            EXPECT_NEAR(0.2f, full.zz, 1e-5f);
            EXPECT_NEAR(-0.1f, full.xy, 1e-5f);
            EXPECT_NEAR(0.02f, full.xz, 1e-5f);
            EXPECT_NEAR(0.05f, full.yz, 1e-5f);
            double tightest = 0;
            for (int i = 0; i < 4000; ++i)
            {
                Vector3f w = OnSphere(rng);
                Point3f p = new Point3f(-rng.Uniform<Float>(), 3.4f * rng.Uniform<Float>() - 0.2f, rng.Uniform<Float>());
                MediumProperties mp = matrices.SamplePoint(p, w, lambda, 0);
                Float majorant = matrices.SampleRay(new Ray(p, w), 1, lambda, new ScratchBuffer()).Next().value().sigma_maj[0];
                CHECK_GE(majorant, mp.sigma_a[0] + mp.sigma_s[0]);
                tightest = Math.Max(tightest, (mp.sigma_a[0] + mp.sigma_s[0]) / majorant);
                EXPECT_NEAR(2.5f * matrices.MatrixAt(p).ProjectedArea(w), mp.sigma_a[0] + mp.sigma_s[0], 1e-4f);
            }
            Console.WriteLine("SGGX grid of matrices: the extinction reaches " + tightest + " of the majorant");
            CHECK_GT(tightest, 0.9);

            // "sggxmax" under the largest of the grid: the matrices over it are scaled down to it, the others are as given
            SGGXMedium limited = (SGGXMedium)Flakes("\"string microflakes\" \"matrix\" \"float sggx\" [0.25 0.25 0.25 0 0 0] \"integer flakenx\" 3 \"integer flakeny\" 1 " +
                                                    "\"integer flakenz\" 1 \"point3 flakep0\" [0 0 0] \"point3 flakep1\" [3 1 1] \"float sggxmax\" 1 \"float sggxgrid\" [" + six + "]");
            SGGXMatrix scaled = limited.MatrixAt(new Point3f(-0.5f, 1.5f, 0.5f));
            EXPECT_NEAR(1, scaled.LargestProjectedArea(), 1e-5f);
            EXPECT_NEAR(0.01f, scaled.yy, 1e-6f);
            EXPECT_NEAR(0.5f, limited.MatrixAt(new Point3f(-0.5f, 2.5f, 0.5f)).yy, 1e-5f);
        }

        // The fields from the float grids of a NanoVDB file: the level set of a torus as the roughness of fibres
        [TEST("SGGXMedium", "NanoVDBFields")]
        public static void NanoVDBFields()
        {
            string file = SceneFile("sdf", "level_set_torus.nvdb");
            if (file == null)
            {
                Console.WriteLine("SGGX NanoVDB test skipped: scenes/sdf/level_set_torus.nvdb not found");
                return;
            }
            nanovdb.GridHandle<NanoVDBBuffer> first = nanovdb.io.IO.readGrid<NanoVDBBuffer>(file, 0, 0, new NanoVDBBuffer(Allocator.Default));
            string name = first.grid<float>().gridName();
            SGGXMedium medium = (SGGXMedium)Flakes("\"string microflakes\" \"fibre\" \"vector3 flakeaxis\" [0 0 1] \"string flakegridfile\" \"" + file.Replace("\\", "/") +
                                                   "\" \"string flakeroughnessgridname\" \"" + name + "\"");
            CHECK(medium.HasFields());
            // createLevelSetTorus(0.7, 0.25) around the y axis of the grid's space, which is the medium's: 0.1 outside the
            // surface at the medium's (1.05, 0, 0), render (0, 1.05, 0). The roughness there is the distance, 0.1.
            SGGXMatrix at = medium.MatrixAt(new Point3f(0, 1.05f, 0));
            Console.WriteLine("SGGX NanoVDB: grid \"" + name + "\", alpha^2 0.1 outside the torus " + at.zz);
            EXPECT_NEAR(0.01f, at.zz, 0.002f);
            EXPECT_NEAR(1, at.xx, 1e-5f);
            // (inside the torus the distance is negative: the least roughness)
            EXPECT_NEAR(0.0001f, medium.MatrixAt(new Point3f(0, 0.7f, 0)).zz, 1e-6f);
        }
    }
}
