// (not in pbrt-v4) polygrain_walk_test.cs
//
// The multiple scattering of the polydisperse micrograin BSDF (polygrain_walk.cs):
//   - with one scattering the walk is the BSDF of the paper: the same albedo, and the same light into a cone of directions;
//   - grains and a bulk that absorb nothing return all the light, where the model of the paper returns a part of it;
//   - the albedo by the walk (Sample_f) and by its evaluation (f) agree, and the density integrates to one;
//   - what the later scatterings do to reciprocity, measured.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class PolyGrainWalkTests
    {
        internal static PolyGrainBxDF Mirror(mg.PolyGrain d, Float bulkReflectance, int order, int samples = 4)
        {
            PolyGrainBxDF bxdf = new PolyGrainBxDF(d, samples);
            for (int j = 0; j < d.n; ++j)
            {
                bxdf.SetGrainDiffuse(j, new SampledSpectrum(0f));
                // (a reflectance of 1 at every angle, to within 1e-7)
                bxdf.SetGrainConductor(j, new SampledSpectrum(1f), new SampledSpectrum(1e4f));
            }
            bxdf.SetBulk(new DiffuseBxDF(new SampledSpectrum(bulkReflectance)));
            bxdf.maxOrder = order;
            return bxdf;
        }

        internal static PolyGrainBxDF Chalk(mg.PolyGrain d, int order)
        {
            PolyGrainBxDF bxdf = new PolyGrainBxDF(d, 4);
            for (int j = 0; j < d.n; ++j)
            {
                bxdf.SetGrainDiffuse(j, new SampledSpectrum(1f));
            }
            bxdf.SetBulk(new DiffuseBxDF(new SampledSpectrum(1f)));
            bxdf.maxOrder = order;
            return bxdf;
        }

        internal static Vector3f Direction(Float cosine, Float phi)
        {
            Float sine = SafeSqrt(1 - cosine * cosine);
            return new Vector3f(sine * (Float)Math.Cos(phi), sine * (Float)Math.Sin(phi), cosine);
        }

        // The albedo and the light into the cone about `to`, by the samples of the walk or of the BxDF
        static void Sampled(PolyGrainBxDF bxdf, bool walk, Vector3f wo, Vector3f to, Float cosCone, int count, ref RNG rng, out double albedo, out double error,
                            out double cone, out double coneError)
        {
            double sum = 0, squares = 0, inCone = 0, inCone2 = 0;
            for (int i = 0; i < count; ++i)
            {
                Float uc = rng.Uniform<Float>();
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<BSDFSample> bs = walk ? bxdf.WalkSample(wo, uc, u, TransportMode.Importance, BxDFReflTransFlags.All)
                                                    : bxdf.Sample_f(wo, uc, u, TransportMode.Importance, BxDFReflTransFlags.All);
                if (!bs)
                {
                    continue;
                }
                double w = bs.value().f[0] * AbsCosTheta(bs.value().wi) / bs.value().pdf;
                sum += w;
                squares += w * w;
                if (Dot(bs.value().wi, to) > cosCone)
                {
                    inCone += w;
                    inCone2 += w * w;
                }
            }
            albedo = sum / count;
            error = Math.Sqrt(Math.Max(0.0, squares / count - albedo * albedo) / count);
            cone = inCone / count;
            coneError = Math.Sqrt(Math.Max(0.0, inCone2 / count - cone * cone) / count);
        }

        [TEST("PolyGrainWalk", "OneScatteringIsThePaper")]
        public static void OneScatteringIsThePaper()
        {
            RNG rng = new RNG(5);
            foreach (bool metal in new bool[] { true, false })
            {
                PolyGrainBxDF paper, walk;
                if (metal)
                {
                    paper = PolyGrainTests.Gold(PolyGrainTests.Two(), 0.5f, 4);
                    walk = PolyGrainTests.Gold(PolyGrainTests.Two(), 0.5f, 4);
                }
                else
                {
                    paper = Chalk(PolyGrainTests.Two(), 1);
                    walk = Chalk(PolyGrainTests.Two(), 1);
                }
                foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                {
                    Vector3f wo = Direction(cosine, 0.4f), to = Direction(0.7f, 2.9f);
                    Float cosCone = (Float)Math.Cos(0.5);
                    double a0, e0, c0, ce0, a1, e1, c1, ce1;
                    Sampled(paper, false, wo, to, cosCone, 300000, ref rng, out a0, out e0, out c0, out ce0);
                    Sampled(walk, true, wo, to, cosCone, 300000, ref rng, out a1, out e1, out c1, out ce1);
                    Console.WriteLine("MEASURE polygrain walk, one scattering, {0}, cosine {1}: albedo {2:F4} against the paper {3:F4} (+- {4:F4}); into a cone " +
                                      "{5:F5} against {6:F5} (+- {7:F5})", metal ? "conductor grains" : "diffuse grains", cosine, a1, a0, Math.Sqrt(e0 * e0 + e1 * e1), c1,
                                      c0, Math.Sqrt(ce0 * ce0 + ce1 * ce1));
                    EXPECT_LT(Math.Abs(a1 - a0), 5 * Math.Sqrt(e0 * e0 + e1 * e1) + 1e-3);
                    EXPECT_LT(Math.Abs(c1 - c0), 5 * Math.Sqrt(ce0 * ce0 + ce1 * ce1) + 3e-4);
                }
            }
        }

        [TEST("PolyGrainWalk", "NothingIsLost")]
        public static void NothingIsLost()
        {
            RNG rng = new RNG(9);
            mg.PolyGrain[] cases = new mg.PolyGrain[] { PolyGrainTests.One(0.5, 1.0), PolyGrainTests.One(0.8, 0.5), PolyGrainTests.Two() };
            double furthest = 0;
            for (int c = 0; c < cases.Length; ++c)
            {
                foreach (bool metal in new bool[] { true, false })
                {
                    PolyGrainBxDF paper = metal ? Mirror(cases[c], 1, 1) : Chalk(cases[c], 1);
                    PolyGrainBxDF walk = metal ? Mirror(cases[c], 1, 64) : Chalk(cases[c], 64);
                    foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                    {
                        Vector3f wo = Direction(cosine, 1.3f);
                        double a0, e0, a1, e1, unused, unusedError;
                        Sampled(paper, false, wo, wo, 2, 60000, ref rng, out a0, out e0, out unused, out unusedError);
                        walk.reciprocal = false;
                        Sampled(walk, false, wo, wo, 2, 60000, ref rng, out a1, out e1, out unused, out unusedError);
                        walk.reciprocal = true;
                        double a2, e2;
                        Sampled(walk, false, wo, wo, 2, 60000, ref rng, out a2, out e2, out unused, out unusedError);
                        Console.WriteLine("MEASURE polygrain white furnace, case {0}, {1}, cosine {2}: the paper {3:F4}, every scattering {4:F4}, from both " +
                                          "directions {5:F4} +- {6:F4}", c, metal ? "mirror grains" : "white grains", cosine, a0, a1, a2, e2);
                        furthest = Math.Max(furthest, Math.Abs(a2 - 1));
                        EXPECT_GT(a1, 0.99);
                        EXPECT_LT(a1, 1 + 1e-4);
                    }
                }
            }
            Console.WriteLine("MEASURE polygrain white furnace, the walks from both directions: furthest from 1 by {0:F4}", furthest);
        }

        [TEST("PolyGrainWalk", "Checks")]
        public static void Checks()
        {
            BxDFCheckOptions options = new BxDFCheckOptions();
            options.cosines = new Float[] { 0.9f, 0.5f, 0.2f };
            options.samples = 40000;
            options.pairs = 300;
            options.repeats = 24;
            options.bothSides = false;
            PolyGrainBxDF gold = PolyGrainTests.Gold(PolyGrainTests.Two(), 0.5f, 4);
            gold.maxOrder = 32;
            BxDFCheckResult metal = BxDFCheck.Run(gold, "PolyGrain walk, two types of conductor grains on a diffuse bulk", options);
            EXPECT_EQ(0, metal.invalid);
            EXPECT_LT(metal.albedoMax, 1.0);
            EXPECT_TRUE(metal.albedoSigmas < 5 || metal.albedoRelative < 1e-2, "Sample_f and f disagree about the albedo: " + metal.albedoSigmas);
            EXPECT_LT(metal.pdfMax, 1.03);
            EXPECT_GT(metal.pdfMin, 0.95);
            BxDFCheckResult chalk = BxDFCheck.Run(Chalk(PolyGrainTests.Two(), 32), "PolyGrain walk, white diffuse grains on a white bulk", options);
            EXPECT_EQ(0, chalk.invalid);
            EXPECT_TRUE(chalk.albedoSigmas < 5 || chalk.albedoRelative < 1e-2, "Sample_f and f disagree about the albedo: " + chalk.albedoSigmas);
            EXPECT_LT(chalk.pdfMax, 1.03);
            EXPECT_GT(chalk.pdfMin, 0.95);
            Console.WriteLine("MEASURE polygrain walk reciprocity (mean squared difference of a pair over its variance; 1 when reciprocal): conductor grains {0:F2}, " +
                              "white grains {1:F2}", metal.reciprocityChi2, chalk.reciprocityChi2);
        }

        // How far from reciprocal the later scatterings are: f(wo, wi) against f(wi, wo), averaged over many walks
        [TEST("PolyGrainWalk", "ReciprocityOfTheLaterScatterings")]
        public static void ReciprocityOfTheLaterScatterings()
        {
            RNG rng = new RNG(21);
          foreach (bool both in new bool[] { false, true })
          {
            PolyGrainBxDF walk = Mirror(PolyGrainTests.Two(), 1, 32);
            walk.reciprocal = both;
            PolyGrainBxDF paper = Mirror(PolyGrainTests.Two(), 1, 1);
            double worst = 0, mean = 0;
            int pairs = 0;
            Vector3f[] a = new Vector3f[] { Direction(0.95f, 0.2f), Direction(0.8f, 1.0f), Direction(0.9f, 4.0f), Direction(0.3f, 0.5f), Direction(0.6f, 2.0f) };
            Vector3f[] b = new Vector3f[] { Direction(0.25f, 3.0f), Direction(0.5f, 4.2f), Direction(0.15f, 0.9f), Direction(0.35f, 3.5f), Direction(0.2f, 5.0f) };
            for (int p = 0; p < a.Length; ++p)
            {
                double x = 0, y = 0, x2 = 0, y2 = 0;
                int count = 6000;
                for (int i = 0; i < count; ++i)
                {
                    Vector3f d = 1e-5f * new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                    Vector3f o = Normalize(a[p] + d), i2 = Normalize(b[p] - d);
                    double u = walk.f(o, i2, TransportMode.Radiance)[0], v = walk.f(i2, o, TransportMode.Importance)[0];
                    x += u; x2 += u * u; y += v; y2 += v * v;
                }
                x /= count; y /= count;
                double error = Math.Sqrt((Math.Max(0, x2 / count - x * x) + Math.Max(0, y2 / count - y * y)) / count);
                double single = paper.f(a[p], b[p], TransportMode.Radiance)[0];
                double relative = (x - y) / Math.Max(x, y);
                if (both)
                {
                    EXPECT_LT(Math.Abs(x - y), 4 * error);
                }
                Console.WriteLine("MEASURE polygrain walk" + (both ? ", from both directions" : "") + ", pair {0} (cosines {1:F2}, {2:F2}): f {3:F4} one way, {4:F4} the other (+- {5:F4}); the paper {6:F4}; " +
                                  "difference {7:P1} of f", p, a[p].z, b[p].z, x, y, error, single, relative);
                worst = Math.Max(worst, Math.Abs(relative));
                mean += Math.Abs(relative);
                ++pairs;
            }
            Console.WriteLine("MEASURE polygrain walk" + (both ? ", from both directions" : "") + ": f(wo, wi) against f(wi, wo), worst {0:P1}, mean {1:P1} of f " +
                              "over {2} pairs", worst, mean / pairs, pairs);
          }
        }

        // ---- Grains laid out one by one: unit hemispheres with their centres on the plane z = 0, placed at random (they may
        // overlap, as in the model), mirrors or white, on a diffuse base. Rays are traced through them. This is what the model
        // of the paper describes for one scattering, and what the walk approximates for the later ones.
        internal sealed class Layout
        {
            public double size;
            public int cells;
            public double[] cx, cy;
            public List<int>[] grid;

            public Layout(double tau, double size, ulong seed)
            {
                this.size = size;
                cells = (int)size;
                RNG rng = new RNG(seed);
                // tau = 1 - exp(-density pi r^2)
                double density = -Math.Log(1 - tau) / Math.PI;
                int count = (int)Math.Round(density * size * size);
                cx = new double[count];
                cy = new double[count];
                grid = new List<int>[cells * cells];
                for (int c = 0; c < grid.Length; ++c)
                {
                    grid[c] = new List<int>();
                }
                for (int s = 0; s < count; ++s)
                {
                    cx[s] = size * rng.Uniform<double>();
                    cy[s] = size * rng.Uniform<double>();
                    int x0 = (int)Math.Floor(cx[s] - 1), x1 = (int)Math.Floor(cx[s] + 1), y0 = (int)Math.Floor(cy[s] - 1), y1 = (int)Math.Floor(cy[s] + 1);
                    for (int x = x0; x <= x1; ++x)
                    {
                        for (int y = y0; y <= y1; ++y)
                        {
                            grid[((y % cells) + cells) % cells * cells + ((x % cells) + cells) % cells].Add(s);
                        }
                    }
                }
            }

            // The first thing the ray from p along d meets: 1 a grain (its normal in n), 0 the base, -1 nothing (it leaves upwards)
            public int Trace(ref double px, ref double py, ref double pz, double dx, double dy, double dz, out double nx, out double ny, out double nz)
            {
                nx = ny = nz = 0;
                double t = 0;
                // the ray is followed cell by cell while it is below the top of the grains
                double tEnd = dz > 0 ? (1.0000001 - pz) / dz : (dz < 0 ? pz / -dz : 1e30);
                tEnd = Math.Min(tEnd, 4 * size);
                int ix = (int)Math.Floor(px), iy = (int)Math.Floor(py);
                int stepX = dx > 0 ? 1 : -1, stepY = dy > 0 ? 1 : -1;
                double nextX = dx != 0 ? ((dx > 0 ? ix + 1 : ix) - px) / dx : 1e30, nextY = dy != 0 ? ((dy > 0 ? iy + 1 : iy) - py) / dy : 1e30;
                double deltaX = dx != 0 ? Math.Abs(1 / dx) : 1e30, deltaY = dy != 0 ? Math.Abs(1 / dy) : 1e30;
                while (t < tEnd)
                {
                    double tCell = Math.Min(Math.Min(nextX, nextY), tEnd);
                    List<int> list = grid[((iy % cells) + cells) % cells * cells + ((ix % cells) + cells) % cells];
                    double best = 1e30;
                    double bx = 0, by = 0;
                    foreach (int s in list)
                    {
                        // the image of the grain that is nearest to the cell
                        double sx = cx[s] + size * Math.Round((ix + 0.5 - cx[s]) / size), sy = cy[s] + size * Math.Round((iy + 0.5 - cy[s]) / size);
                        double ox = px - sx, oy = py - sy;
                        double b = ox * dx + oy * dy + pz * dz, c = ox * ox + oy * oy + pz * pz - 1;
                        double disc = b * b - c;
                        if (disc <= 0)
                        {
                            continue;
                        }
                        double hit = -b - Math.Sqrt(disc);
                        if (hit > 1e-9 && hit < best && pz + hit * dz >= 0)
                        {
                            best = hit;
                            bx = sx;
                            by = sy;
                        }
                    }
                    if (best <= tCell + 1e-9)
                    {
                        px += best * dx;
                        py += best * dy;
                        pz += best * dz;
                        nx = px - bx;
                        ny = py - by;
                        nz = pz;
                        return 1;
                    }
                    t = tCell;
                    if (t >= tEnd)
                    {
                        break;
                    }
                    if (nextX < nextY)
                    {
                        ix += stepX;
                        nextX += deltaX;
                    }
                    else
                    {
                        iy += stepY;
                        nextY += deltaY;
                    }
                }
                if (dz < 0)
                {
                    px += tEnd * dx;
                    py += tEnd * dy;
                    pz = 0;
                    return 0;
                }
                return -1;
            }
        }

        // The light that leaves, in all and after one scattering only, for light that arrives from wo
        internal static void Simulate(Layout layout, Vector3f wo, bool mirrorGrains, double baseAlbedo, int rays, ref RNG rng, out double all, out double once)
        {
            double sumAll = 0, sumOnce = 0;
            for (int r = 0; r < rays; ++r)
            {
                double px = layout.size * rng.Uniform<double>(), py = layout.size * rng.Uniform<double>(), pz = 1.0000001;
                double dx = -wo.x, dy = -wo.y, dz = -wo.z;
                double weight = 1;
                for (int events = 0; events < 200 && weight > 0; ++events)
                {
                    double nx, ny, nz;
                    int what = layout.Trace(ref px, ref py, ref pz, dx, dy, dz, out nx, out ny, out nz);
                    if (what < 0)
                    {
                        sumAll += weight;
                        if (events == 1)
                        {
                            sumOnce += weight;
                        }
                        break;
                    }
                    if (what == 0)
                    {
                        nx = 0;
                        ny = 0;
                        nz = 1;
                        weight *= baseAlbedo;
                    }
                    if (what == 0 || !mirrorGrains)
                    {
                        // a diffuse reflection about the normal
                        Vector3f local = SampleCosineHemisphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        Vector3f n = new Vector3f((Float)nx, (Float)ny, (Float)nz), tangent, bitangent;
                        CoordinateSystem(n, &tangent, &bitangent);
                        Vector3f w = local.x * tangent + local.y * bitangent + local.z * n;
                        dx = w.x;
                        dy = w.y;
                        dz = w.z;
                    }
                    else
                    {
                        double dot = dx * nx + dy * ny + dz * nz;
                        dx -= 2 * dot * nx;
                        dy -= 2 * dot * ny;
                        dz -= 2 * dot * nz;
                    }
                    double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    dx /= length;
                    dy /= length;
                    dz /= length;
                }
            }
            all = sumAll / rays;
            once = sumOnce / rays;
        }

        [TEST("PolyGrainWalk", "AgainstGrainsLaidOut")]
        public static void AgainstGrainsLaidOut()
        {
            RNG rng = new RNG(77);
            double worst = 0, worstPaper = 0, worstBoth = 0;
            foreach (double tau in new double[] { 0.3, 0.6, 0.85 })
            {
                Layout layout = new Layout(tau, 60, 1234);
                mg.PolyGrain d = PolyGrainTests.One(tau, 1.0);
                foreach (bool mirrorGrains in new bool[] { true, false })
                {
                    foreach (Float baseAlbedo in new Float[] { 0f, 0.8f })
                    {
                        PolyGrainBxDF paper = mirrorGrains ? Mirror(d, baseAlbedo, 1) : Chalk(d, 1);
                        PolyGrainBxDF walk = mirrorGrains ? Mirror(d, baseAlbedo, 64) : Chalk(d, 64);
                        if (!mirrorGrains)
                        {
                            paper.SetBulk(new DiffuseBxDF(new SampledSpectrum(baseAlbedo)));
                            walk.SetBulk(new DiffuseBxDF(new SampledSpectrum(baseAlbedo)));
                        }
                        foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                        {
                            Vector3f wo = Direction(cosine, 0.7f);
                            double all, once, a0, e0, a1, e1, unused, unusedError;
                            Simulate(layout, wo, mirrorGrains, baseAlbedo, 150000, ref rng, out all, out once);
                            Sampled(paper, false, wo, wo, 2, 80000, ref rng, out a0, out e0, out unused, out unusedError);
                            walk.reciprocal = false;
                            Sampled(walk, false, wo, wo, 2, 80000, ref rng, out a1, out e1, out unused, out unusedError);
                            walk.reciprocal = true;
                            double a2, e2;
                            Sampled(walk, false, wo, wo, 2, 80000, ref rng, out a2, out e2, out unused, out unusedError);
                            Console.WriteLine("MEASURE polygrain against grains laid out, tau0 {0}, {1} grains, base {2}, cosine {3}: one scattering {4:F4} (the paper " +
                                              "{5:F4}); every scattering {6:F4} (the walk {7:F4}, from both directions {8:F4} +- {9:F4})", tau,
                                              mirrorGrains ? "mirror" : "white", baseAlbedo, cosine, once, a0, all, a1, a2, e2);
                            EXPECT_LT(Math.Abs(all - a2), 0.03 + 4 * e2);
                            worstBoth = Math.Max(worstBoth, Math.Abs(all - a2));
                            // The model of the paper is that of the grains laid out; the walk is near them
                            EXPECT_LT(Math.Abs(once - a0), 0.015);
                            EXPECT_LT(Math.Abs(all - a1), 0.03);
                            worst = Math.Max(worst, Math.Abs(all - a1));
                            worstPaper = Math.Max(worstPaper, all - a0);
                        }
                    }
                }
            }
            Console.WriteLine("MEASURE polygrain against grains laid out: the walk is within {0:F4} of the light that leaves (from both directions {2:F4}); the model of the " +
                              "paper is short by up to {1:F4}", worst, worstPaper, worstBoth);
        }
    }
}
