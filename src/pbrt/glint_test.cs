// (not in pbrt-v4) glint_test.cs
//
// The glint material (glint.cs): the distribution of the normals of a footprint, and the BxDF on it.

using System;
using testing;

namespace pbrt
{
    public static unsafe class GlintTests
    {
        // A normal map of gentle bumps (no flat piece: every triangle of the manifold has an area among the normals)
        private static Image Bumps(int size, double slope)
        {
            Image image = new Image(PixelFormat.Float, new Point2i(size, size), new string[] { "R", "G", "B" });
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    double u = 2 * Math.PI * x / size, v = 2 * Math.PI * y / size;
                    // (periodic over the map, as the map repeats)
                    double sx = slope * (Math.Sin(3 * u + 0.7) * Math.Cos(2 * v) + 0.6 * Math.Cos(7 * u - 5 * v + 0.2) + 0.35 * Math.Sin(11 * v + 4 * u));
                    double sy = slope * (Math.Cos(2 * u - 0.4) * Math.Sin(5 * v + 1.1) + 0.6 * Math.Sin(6 * u + 8 * v) + 0.35 * Math.Cos(13 * u - 3 * v));
                    Set(image, x, y, -sx, -sy, 1);
                }
            }
            return image;
        }

        // A normal map of flat flakes, 4 by 4 texels each, of random normals
        private static Image Flakes(int size, double spread)
        {
            Image image = new Image(PixelFormat.Float, new Point2i(size, size), new string[] { "R", "G", "B" });
            RNG rng = new RNG(77);
            int cells = size / 4;
            double[] fx = new double[cells * cells], fy = new double[cells * cells];
            for (int i = 0; i < cells * cells; ++i)
            {
                fx[i] = spread * (2 * rng.Uniform<Float>() - 1);
                fy[i] = spread * (2 * rng.Uniform<Float>() - 1);
            }
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    int cell = (y / 4) * cells + x / 4;
                    Set(image, x, y, fx[cell], fy[cell], 1);
                }
            }
            return image;
        }

        private static void Set(Image image, int x, int y, double nx, double ny, double nz)
        {
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            image.SetChannel(new Point2i(x, y), 0, (Float)(0.5 * (nx / length + 1)));
            image.SetChannel(new Point2i(x, y), 1, (Float)(0.5 * (ny / length + 1)));
            image.SetChannel(new Point2i(x, y), 2, (Float)(0.5 * (nz / length + 1)));
        }

        // The normals that the manifold gives for points of the footprint have the density that it evaluates: by the share
        // of the samples in each bin of the disc of normals against the integral of the density over the bin, by the whole
        // integral, and by the area of the normals that the footprint has at all
        [TEST("Glint", "ManifoldSamplesWhatItEvaluates")]
        public static void ManifoldSamplesWhatItEvaluates()
        {
            PositionNormalManifold manifold = new PositionNormalManifold(Bumps(64, 0.22));
            GlintFootprint[] footprints =
            {
                new GlintFootprint(20.3, 31.7, 5.5, 1.0, -1.5, 4.0),
                // (over the edge of the map, which repeats)
                new GlintFootprint(62.9, 1.2, 3.0, 0.0, 0.0, 3.0),
                // (smaller than a texel)
                new GlintFootprint(40.4, 12.6, 0.3, 0.05, -0.1, 0.25),
            };
            for (int test = 0; test < 2 * footprints.Length; ++test)
            {
                GlintFootprint footprint = footprints[test % footprints.Length];
                // (then the same with a roughness of the facets)
                double roughness = test < footprints.Length ? 0 : 0.03;
                RNG rng = new RNG(5);
                const int count = 400000, bins = 16;
                // The range of the normals of the footprint
                double minX = 1, minY = 1, maxX = -1, maxY = -1;
                double[] sx = new double[count], sy = new double[count];
                double inverse = 0;
                for (int i = 0; i < count; ++i)
                {
                    manifold.Sample(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), footprint, roughness, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), out sx[i], out sy[i]);
                    minX = Math.Min(minX, sx[i]);
                    maxX = Math.Max(maxX, sx[i]);
                    minY = Math.Min(minY, sy[i]);
                    maxY = Math.Max(maxY, sy[i]);
                    if (i < 20000)
                    {
                        double density = manifold.Evaluate(sx[i], sy[i], footprint, roughness);
                        EXPECT_GT(density, 0);
                        inverse += 1 / density / 20000;
                    }
                }
                double padX = 0.02 * (maxX - minX), padY = 0.02 * (maxY - minY);
                minX -= padX;
                maxX += padX;
                minY -= padY;
                maxY += padY;
                double[] sampled = new double[bins * bins], integral = new double[bins * bins], squares = new double[bins * bins];
                for (int i = 0; i < count; ++i)
                {
                    int bx = Math.Min((int)((sx[i] - minX) / (maxX - minX) * bins), bins - 1), by = Math.Min((int)((sy[i] - minY) / (maxY - minY) * bins), bins - 1);
                    sampled[by * bins + bx] += 1.0 / count;
                }
                const int perBin = 1500;
                double binArea = (maxX - minX) * (maxY - minY) / (bins * bins);
                double total = 0, support = 0;
                for (int bin = 0; bin < bins * bins; ++bin)
                {
                    for (int i = 0; i < perBin; ++i)
                    {
                        double mx = minX + (bin % bins + rng.Uniform<Float>()) / bins * (maxX - minX);
                        double my = minY + (bin / bins + rng.Uniform<Float>()) / bins * (maxY - minY);
                        double value = manifold.Evaluate(mx, my, footprint, roughness) * binArea;
                        integral[bin] += value / perBin;
                        squares[bin] += value * value / perBin;
                        support += value > 0 ? binArea / perBin : 0;
                    }
                    total += integral[bin];
                }
                double worst = 0;
                for (int bin = 0; bin < bins * bins; ++bin)
                {
                    double error = Math.Sqrt(Math.Max(0.0, squares[bin] - integral[bin] * integral[bin]) / perBin + sampled[bin] / count);
                    worst = Math.Max(worst, Math.Abs(sampled[bin] - integral[bin]) / (error + 2e-4));
                }
                Console.WriteLine("footprint at " + footprint.cx + ", " + footprint.cy + ", roughness " + roughness + ": the density integrates to " + total + "; the worst bin is off by " +
                                  worst + " sigma; the area of its normals " + support + " by the density, " + inverse + " by the samples");
                EXPECT_NEAR(1.0, total, 0.02);
                // (without a roughness the density has spikes, and the error of a bin is itself an estimate from few of them)
                EXPECT_LT(worst, roughness > 0 ? 5.0 : 6.0);
                if (roughness == 0)
                {
                    // (with a roughness the density falls to nothing at its edge, and the mean of its inverse does not settle)
                    EXPECT_NEAR(1.0, inverse / support, 0.04);
                }
            }
        }

        // Flat pieces of a map: a flake has one normal, and the manifold gives it the smallest triangle of normals; a sampled
        // normal is always one that the density has, and the density of the flake is the share of the footprint on it over
        // the area of that triangle
        [TEST("Glint", "FlatPieces")]
        public static void FlatPieces()
        {
            PositionNormalManifold manifold = new PositionNormalManifold(Flakes(64, 0.5));
            // (inside one flake: the texels 8 to 11, whose cells 8 to 10 are flat)
            GlintFootprint inside = new GlintFootprint(9.5, 9.5, 1.0, 0.0, 0.0, 1.0);
            RNG rng = new RNG(9);
            double smallest = double.MaxValue, largest = 0;
            for (int i = 0; i < 2000; ++i)
            {
                manifold.Sample(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), inside, 0, new Point2f(0, 0), out double mx, out double my);
                double density = manifold.Evaluate(mx, my, inside, 0);
                smallest = Math.Min(smallest, density);
                largest = Math.Max(largest, density);
            }
            // Each of the 8 triangles of the footprint has an eighth of it and the area MinimumJacobian / 2 of normals, all
            // around the one normal of the flake: where n of them hold a normal the density is n / 8 / (MinimumJacobian / 2)
            double one = 1.0 / 8 / (PositionNormalManifold.MinimumJacobian / 2);
            Console.WriteLine("density inside a flake: " + smallest + " to " + largest + " (one triangle: " + one + ")");
            EXPECT_NEAR(8 * one, largest, 1e-3 * 8 * one);
            EXPECT_NEAR(8 * one, smallest, 1e-3 * 8 * one);

            // Over several flakes every sampled normal is found by the density
            GlintFootprint wide = new GlintFootprint(30.2, 17.9, 9.0, 2.0, -3.0, 7.0);
            for (int i = 0; i < 20000; ++i)
            {
                manifold.Sample(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), wide, 0, new Point2f(0, 0), out double mx, out double my);
                EXPECT_GT(manifold.Evaluate(mx, my, wide, 0), 0);
            }
        }

        // A large footprint is cut into tiles, and the mean of the distributions of the tiles is the distribution of the whole
        // footprint
        [TEST("Glint", "FootprintTiles")]
        public static void FootprintTiles()
        {
            Image map = Bumps(64, 0.22);
            PositionNormalManifold manifold = PositionNormalManifold.Of(map);
            GlintMaterial whole = new GlintMaterial(map, false, null, null, null, null, 1, 1, 1, 1000, 1, null, null);
            GlintMaterial tiled = new GlintMaterial(map, false, null, null, null, null, 1, 1, 1, 3, 1, null, null);
            MaterialEvalContext ctx = new MaterialEvalContext();
            ctx.uv = new Point2f(0.37f, 0.61f);
            // (a pixel of about 20 by 13 texels, askew)
            ctx.dudx = 0.30f;
            ctx.dvdx = 0.06f;
            ctx.dudy = -0.05f;
            ctx.dvdy = 0.21f;
            GlintFootprint full = whole.Footprint(ctx, new Point2f(0.5f, 0.5f));
            EXPECT_GT(Math.Sqrt(full.ax * full.ax + full.ay * full.ay), 9.0);
            RNG rng = new RNG(3);
            foreach (double roughness in new double[] { 0, 0.04 })
            {
                double worst = 0, largest = 0;
                int tiles = 0;
                for (int i = 0; i < 300; ++i)
                {
                    manifold.Sample(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), full, roughness, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                    out double mx, out double my);
                    double expected = manifold.Evaluate(mx, my, full, roughness);
                    // Every tile once: 4 along the longer side, 3 along the other
                    double mean = 0;
                    tiles = 0;
                    for (int a = 0; a < 4; ++a)
                    {
                        for (int b = 0; b < 3; ++b)
                        {
                            GlintFootprint tile = tiled.Footprint(ctx, new Point2f((a + 0.5f) / 4, (b + 0.5f) / 3));
                            EXPECT_LT(Math.Sqrt(tile.ax * tile.ax + tile.ay * tile.ay), 3.0 + 1e-9);
                            EXPECT_LT(Math.Sqrt(tile.bx * tile.bx + tile.by * tile.by), 3.0 + 1e-9);
                            mean += manifold.Evaluate(mx, my, tile, roughness) / 12;
                            ++tiles;
                        }
                    }
                    EXPECT_GT(expected, 0);
                    worst = Math.Max(worst, Math.Abs(mean - expected) / expected);
                    largest = Math.Max(largest, expected);
                }
                Console.WriteLine("roughness " + roughness + ": the mean of " + tiles + " tiles against the whole footprint, off by at most " + worst);
                EXPECT_LT(worst, 1e-9);
            }
        }

        private static GlintBxDF Conductor(PositionNormalManifold manifold, GlintFootprint footprint, Float eta, Float k, int bounces)
        {
            return new GlintBxDF(manifold, footprint, 0, new SampledSpectrum(eta), new SampledSpectrum(k), bounces);
        }

        // One facet alone ("bounces" 1) is a BxDF in closed form: reciprocal, with an albedo below one, and Sample_f, PDF and f
        // in agreement, for a conductor and for a dielectric from both sides
        [TEST("Glint", "OneFacet")]
        public static void OneFacet()
        {
            PositionNormalManifold manifold = new PositionNormalManifold(Bumps(64, 0.22));
            GlintFootprint footprint = new GlintFootprint(20.3, 31.7, 6.5, 1.0, -1.5, 6.0);
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.samples = 600000;
            options.pairs = 20000;
            BxDFCheckResult conductor = BxDFCheck.Run(Conductor(manifold, footprint, 0.2f, 3.5f, 1), "glint conductor, one facet", options);
            BxDFCheck.Expect(conductor, "glint conductor, one facet", 2e-3, 1.005, 5, 0.5);
            // (the density of the normals has steps, at the edges of the triangles and of the footprint; the half vector of a
            // refraction is not the same to the last bit for the two orders of a pair, and a pair at a step shows it)
            BxDFCheckResult dielectric = BxDFCheck.Run(new GlintBxDF(manifold, footprint, 0, 1.5f, 1), "glint dielectric, one facet", options);
            BxDFCheck.Expect(dielectric, "glint dielectric, one facet", 5e-2, 1.005, 5, 0.5);
            // With a roughness of the facets the density has no steps
            options.samples = 150000;
            options.pairs = 6000;
            BxDFCheckResult roughConductor = BxDFCheck.Run(new GlintBxDF(manifold, footprint, 0.05, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f), 1),
                                                           "glint conductor, one rough facet", options);
            BxDFCheck.Expect(roughConductor, "glint conductor, one rough facet", 2e-3, 1.005, 5, 0.5);
            BxDFCheckResult roughDielectric = BxDFCheck.Run(new GlintBxDF(manifold, footprint, 0.05, 1.5f, 1), "glint dielectric, one rough facet", options);
            BxDFCheck.Expect(roughDielectric, "glint dielectric, one rough facet", 2e-3, 1.005, 5, 0.5);
        }

        // The mean weight of the samples from wo, and the shares of them that are glossy (one facet) and that failed
        private static void Albedo(GlintBxDF bxdf, Vector3f wo, int count, ref RNG rng, out double albedo, out double error, out double glossy,
                                   out double lost)
        {
            double sum = 0, squares = 0;
            int glossyCount = 0, lostCount = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<BSDFSample> bs = bxdf.Sample_f(wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                             TransportMode.Importance, BxDFReflTransFlags.All);
                if (!bs || !(bs.value().pdf > 0))
                {
                    ++lostCount;
                    continue;
                }
                BSDFSample s = bs.value();
                double value = s.f.Average() * AbsCosTheta(s.wi) / s.pdf;
                EXPECT_TRUE(value >= 0 && !double.IsInfinity(value));
                sum += value;
                squares += value * value;
                glossyCount += s.IsSpecular() ? 0 : 1;
            }
            albedo = sum / count;
            error = Math.Sqrt(Math.Max(0.0, squares / count - albedo * albedo) / count);
            glossy = (double)glossyCount / count;
            lost = (double)lostCount / count;
        }

        // With the walk between the two facets no light is lost: a conductor that reflects everything, and a dielectric, send
        // out all the light that arrives, from every direction and for a rough map
        [TEST("Glint", "WhiteFurnace")]
        public static void WhiteFurnace()
        {
            RNG rng = new RNG(21);
            foreach (double slope in new double[] { 0.22, 1.0 })
            {
                PositionNormalManifold manifold = new PositionNormalManifold(Bumps(64, slope));
                GlintFootprint footprint = new GlintFootprint(20.3, 31.7, 6.5, 1.0, -1.5, 6.0);
                foreach (Float cosine in new Float[] { 0.95f, 0.5f, 0.15f, -0.6f })
                {
                    Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine) * 0.8f, SafeSqrt(1 - cosine * cosine) * 0.6f, cosine);
                    // (a conductor of this index reflects all but a millionth)
                    Albedo(Conductor(manifold, footprint, 1f, 3000f, 1), wo, 100000, ref rng, out double single, out double singleError, out double g1,
                           out double l1);
                    Albedo(Conductor(manifold, footprint, 1f, 3000f, 64), wo, 100000, ref rng, out double metal, out double metalError, out double g2,
                           out double l2);
                    Albedo(new GlintBxDF(manifold, footprint, 0, 1.5f, 64), wo, 100000, ref rng, out double glass, out double glassError, out double g3,
                           out double l3);
                    Albedo(new GlintBxDF(manifold, footprint, 0.08, new SampledSpectrum(1f), new SampledSpectrum(3000f), 64), wo, 100000, ref rng,
                           out double rough, out double roughError, out double g4, out double l4);
                    if (cosine > 0)
                    {
                        // (rough facets: all but what the facets past the horizon lose)
                        EXPECT_NEAR(1.0, rough, 2e-3 + l4);
                    }
                    Console.WriteLine("slope " + slope + ", cos " + cosine + ": conductor, one facet " + single + "; with the walk " + metal + " +- " +
                                      metalError + " (one facet: " + g2 + " of the samples, lost: " + l2 + "); dielectric " + glass + " +- " + glassError +
                                      " (lost: " + l3 + ")");
                    if (cosine > 0)
                    {
                        EXPECT_LT(single, 1.0 + 1e-3);
                        EXPECT_NEAR(1.0, metal, 2e-3);
                    }
                    EXPECT_NEAR(1.0, glass, 2e-3);
                }
            }
        }

        // Two facets, both reflections, in closed form: the light that the walk sends out after exactly two facets, as a share
        // of what arrives (a conductor that reflects everything), is the integral of the closed form over the directions,
        // and of its density; and the closed form is reciprocal
        [TEST("Glint", "TwoFacets")]
        public static void TwoFacets()
        {
            RNG rng = new RNG(41);
            foreach (double slope in new double[] { 0.35, 1.0 })
            {
                PositionNormalManifold manifold = new PositionNormalManifold(Bumps(64, slope));
                GlintFootprint footprint = new GlintFootprint(20.3, 31.7, 6.5, 1.0, -1.5, 6.0);
                foreach (double roughness in new double[] { 0.06, 0 })
                {
                    GlintBxDF one = new GlintBxDF(manifold, footprint, roughness, new SampledSpectrum(1f), new SampledSpectrum(3000f), 1);
                    GlintBxDF two = new GlintBxDF(manifold, footprint, roughness, new SampledSpectrum(1f), new SampledSpectrum(3000f), 2);
                    foreach (Float cosine in new Float[] { 0.9f, 0.3f })
                    {
                        Vector3f wo = new Vector3f(SafeSqrt(1 - cosine * cosine) * 0.8f, SafeSqrt(1 - cosine * cosine) * 0.6f, cosine);
                        Albedo(one, wo, 200000, ref rng, out double shareOne, out double e1, out double g1, out double l1);
                        Albedo(two, wo, 200000, ref rng, out double shareTwo, out double e2, out double g2, out double l2);
                        // (every sample of these two is of a closed form)
                        EXPECT_NEAR(shareTwo, g2, 1e-3);
                        // The closed form of the two facets alone, over the hemisphere, by directions of a cosine density
                        const int count = 200000;
                        double sum = 0, squares = 0, sumPdf = 0, squaresPdf = 0;
                        for (int i = 0; i < count; ++i)
                        {
                            Vector3f wi = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                            if (!(wi.z > 0))
                            {
                                continue;
                            }
                            double density = CosineHemispherePDF(wi.z);
                            double value = (two.f(wo, wi, TransportMode.Importance)[0] - one.f(wo, wi, TransportMode.Importance)[0]) * wi.z / density;
                            double pdf = (two.PDF(wo, wi, TransportMode.Importance, BxDFReflTransFlags.All) -
                                          one.PDF(wo, wi, TransportMode.Importance, BxDFReflTransFlags.All)) / density;
                            EXPECT_TRUE(value >= 0 && pdf >= 0);
                            sum += value;
                            squares += value * value;
                            sumPdf += pdf;
                            squaresPdf += pdf * pdf;
                        }
                        double integral = sum / count, error = Math.Sqrt(Math.Max(0.0, squares / count - integral * integral) / count);
                        double integralPdf = sumPdf / count, errorPdf = Math.Sqrt(Math.Max(0.0, squaresPdf / count - integralPdf * integralPdf) / count);
                        double walked = shareTwo - shareOne, walkedError = Math.Sqrt((shareOne * (1 - shareOne) + shareTwo * (1 - shareTwo)) / 200000);
                        Console.WriteLine("slope " + slope + ", roughness " + roughness + ", cos " + cosine + ": two facets by the walk " + walked + " +- " +
                                          walkedError + ", by the closed form " + integral + " +- " + error + ", by its density " + integralPdf + " +- " +
                                          errorPdf + " (one facet: " + shareOne + ")");
                        double sigma = Math.Sqrt(walkedError * walkedError + error * error);
                        EXPECT_LT(Math.Abs(walked - integral), 5 * sigma + 0.01 * walked + 2e-4);
                        EXPECT_LT(Math.Abs(walked - integralPdf), 5 * Math.Sqrt(walkedError * walkedError + errorPdf * errorPdf) + 0.01 * walked + 2e-4);
                    }
                    // Reciprocity, with both facets
                    double worst = 0;
                    int pairs = 0;
                    GlintBxDF metal = new GlintBxDF(manifold, footprint, roughness, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f), 2);
                    GlintBxDF metalOne = new GlintBxDF(manifold, footprint, roughness, new SampledSpectrum(0.2f), new SampledSpectrum(3.5f), 1);
                    for (int i = 0; i < 40000; ++i)
                    {
                        Vector3f a = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        Vector3f b = SampleUniformHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        if (a.z < 0.05f || b.z < 0.05f)
                        {
                            continue;
                        }
                        double ab = metal.f(a, b, TransportMode.Radiance)[0] - metalOne.f(a, b, TransportMode.Radiance)[0];
                        double ba = metal.f(b, a, TransportMode.Importance)[0] - metalOne.f(b, a, TransportMode.Importance)[0];
                        if (Math.Max(ab, ba) > 1e-4)
                        {
                            ++pairs;
                            worst = Math.Max(worst, Math.Abs(ab - ba) / Math.Max(ab, ba));
                        }
                    }
                    Console.WriteLine("slope " + slope + ", roughness " + roughness + ": the two facets are reciprocal to " + worst + " over " + pairs + " pairs");
                    EXPECT_GT(pairs, 50);
                    if (roughness > 0)
                    {
                        EXPECT_LT(worst, 2e-2);
                    }
                }
            }
        }

        // Polarisation: one facet has the Mueller matrix of the Fresnel equations at its normal, with a first element of 1; a
        // walk over several facets has a matrix too, when asked for the pair of directions it was sampled with
        [TEST("Glint", "Polarised")]
        public static void Polarised()
        {
            PositionNormalManifold manifold = new PositionNormalManifold(Bumps(64, 0.8));
            GlintFootprint footprint = new GlintFootprint(20.3, 31.7, 6.5, 1.0, -1.5, 6.0);
            RNG rng = new RNG(31);
            foreach (bool dielectric in new bool[] { false, true })
            {
                GlintBxDF glint = dielectric ? new GlintBxDF(manifold, footprint, 0, 1.5f, 32) : Conductor(manifold, footprint, 0.2f, 3.5f, 32);
                // (one facet alone, where the matrix is that of one reflection or refraction)
                GlintBxDF single1 = dielectric ? new GlintBxDF(manifold, footprint, 0, 1.5f, 1) : Conductor(manifold, footprint, 0.2f, 3.5f, 1);
                EXPECT_TRUE(glint.HasMuellerMatrix());
                BSDF bsdf = new BSDF(new Normal3f(0, 0, 1), new Normal3f(0, 0, 1), new Vector3f(1, 0, 0), glint);
                int single = 0, singleMatrix = 0, walks = 0, walkMatrix = 0;
                double worstFirst = 0, largestChain = 0;
                for (int i = 0; i < 20000; ++i)
                {
                    Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (Math.Abs(wo.z) < 0.05f || (!dielectric && wo.z < 0))
                    {
                        continue;
                    }
                    bool polarised;
                    PolarisedLightSystem.Attenuation matrix;
                    pstd.optional<BSDFSample> bs = glint.Sample_f(bsdf, wo, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                                  TransportMode.Radiance, BxDFReflTransFlags.All, out polarised, out matrix);
                    if (!bs)
                    {
                        continue;
                    }
                    if (bs.value().IsSpecular())
                    {
                        ++walks;
                        walkMatrix += polarised ? 1 : 0;
                    }
                    else
                    {
                        ++single;
                        singleMatrix += polarised ? 1 : 0;
                    }
                    if (polarised)
                    {
                        // (unpolarised light keeps its radiance: the first element of the matrix is 1)
                        PolarisedLightSystem.Light light = new PolarisedLightSystem.Light(new SampledSpectrum(1f));
                        Vector3f toward = bsdf.LocalToRender(wo);
                        StokesVector stokes = (matrix * light).RepresentInDirection(toward, Normalize(Cross(toward, new Vector3f(0.3f, 0.5f, 0.8f)))).ToStokesVector();
                        // (with two facets in it the first element is not 1: the second facet reflects the light that the
                        // first one polarised, not unpolarised light)
                        largestChain = Math.Max(largestChain, Math.Abs(stokes.s0[0] - 1));
                    }
                }
                // One facet is polarised as the rough BxDF of pbrt of the same interface is for the same pair of directions (the
                // Fresnel matrix at the half vector, in the frames of its plane): light that arrives unpolarised leaves with
                // the same Stokes vector
                BxDF stock = dielectric ? (BxDF)new DielectricBxDF(1.5f, new TrowbridgeReitzDistribution(0.4f, 0.4f))
                                        : new ConductorBxDF(new TrowbridgeReitzDistribution(0.4f, 0.4f), new SampledSpectrum(0.2f), new SampledSpectrum(3.5f));
                BSDF stockBsdf = bsdf.WithBxDF(stock);
                double worstStokes = 0, largestPolarisation = 0;
                int compared = 0;
                for (int i = 0; i < 40000; ++i)
                {
                    Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    if (Math.Abs(wo.z) < 0.05f || Math.Abs(wi.z) < 0.05f)
                    {
                        continue;
                    }
                    TransportMode mode = i % 2 == 0 ? TransportMode.Radiance : TransportMode.Importance;
                    bool polarised;
                    PolarisedLightSystem.Attenuation matrix, expected;
                    SampledSpectrum value = single1.f(bsdf, wo, wi, mode, out polarised, out matrix);
                    if (!(value[0] > 0))
                    {
                        continue;
                    }
                    EXPECT_TRUE(polarised);
                    // (unpolarised light keeps its radiance through one facet: the first element is 1)
                    {
                        PolarisedLightSystem.Light unpolarised = new PolarisedLightSystem.Light(new SampledSpectrum(1f));
                        Vector3f toward = mode == TransportMode.Radiance ? bsdf.LocalToRender(wo) : bsdf.LocalToRender(wi);
                        StokesVector stokes = (matrix * unpolarised).RepresentInDirection(toward, Normalize(Cross(toward, new Vector3f(0.3f, 0.5f, 0.8f)))).ToStokesVector();
                        worstFirst = Math.Max(worstFirst, Math.Abs(stokes.s0[0] - 1));
                    }
                    Vector3f woRender = bsdf.LocalToRender(wo), wiRender = bsdf.LocalToRender(wi);
                    if (!polarised || !PolarisedPathIntegrator.RoughAttenuation(stockBsdf, null, woRender, wiRender, new SampledSpectrum(1f), mode, out expected))
                    {
                        continue;
                    }
                    // (the light leaves along wo for a path from the camera, along wi for a path from the light)
                    Vector3f leaving = mode == TransportMode.Radiance ? woRender : wiRender;
                    Vector3f reference = Normalize(Cross(leaving, new Vector3f(0.3f, 0.5f, 0.8f)));
                    PolarisedLightSystem.Light light = new PolarisedLightSystem.Light(new SampledSpectrum(1f));
                    StokesVector a = (matrix * light).RepresentInDirection(leaving, reference).ToStokesVector();
                    StokesVector b = (expected * light).RepresentInDirection(leaving, reference).ToStokesVector();
                    worstStokes = Math.Max(worstStokes, Math.Max(Math.Max(Math.Abs(a.s0[0] - b.s0[0]), Math.Abs(a.s1[0] - b.s1[0])),
                                                                 Math.Max(Math.Abs(a.s2[0] - b.s2[0]), Math.Abs(a.s3[0] - b.s3[0]))));
                    largestPolarisation = Math.Max(largestPolarisation, Math.Sqrt(a.s1[0] * a.s1[0] + a.s2[0] * a.s2[0] + a.s3[0] * a.s3[0]));
                    ++compared;
                }
                Console.WriteLine((dielectric ? "dielectric" : "conductor") + ": " + compared + " pairs against pbrt's rough BxDF: Stokes vectors differ by at most " +
                                  worstStokes + "; degree of polarisation up to " + largestPolarisation);
                EXPECT_GT(compared, 1000);
                EXPECT_LT(worstStokes, 2e-3);
                // (a metal of this index polarises little; a dielectric fully at Brewster's angle)
                EXPECT_GT(largestPolarisation, dielectric ? 0.5 : 0.02);
                Console.WriteLine((dielectric ? "dielectric" : "conductor") + ": one facet " + single + " (" + singleMatrix + " with a matrix), walks " + walks +
                                  " (" + walkMatrix + " with a matrix); first element of one facet off by " + worstFirst + ", with more facets by up to " +
                                  largestChain);
                EXPECT_GT(single, 1000);
                // (a conductor has a closed form for two facets: its walks are those of three or more)
                EXPECT_GT(walks, dielectric ? 300 : 10);
                EXPECT_EQ(single, singleMatrix);
                EXPECT_EQ(walks, walkMatrix);
                EXPECT_LT(worstFirst, 1e-3);
            }
        }
    }
}
