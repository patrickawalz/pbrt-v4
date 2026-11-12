// (not in pbrt-v4) util/vcavity_test.cs
//
// The groove solver of util/vcavity.cs against rays traced one by one in the cross section of the groove, and the V-cavity
// conductor against the Cook-Torrance BRDF with the V-cavity masking of Torrance and Sparrow, which is its first reflection.

using System;
using testing;

namespace pbrt
{
    public static unsafe class VCavityTests
    {
        // A ray traced in the groove of facet tilts tl (left) and tr (right, negative), entering at the abscissa x of the
        // aperture (height 1) from the elevation thetaI. Returns the number of reflections; the first wall and the exit elevation
        static int TraceRay(double thetaI, double tl, double tr, double x, int maxN, out int first, out double thetaO)
        {
            double psiL = Math.PI - tl, psiR = -tr;
            // The walls, from the apex (the origin) to the aperture
            double lx = Math.Cos(psiL) / Math.Sin(psiL), rx = Math.Cos(psiR) / Math.Sin(psiR);
            double px = x, pz = 1;
            double dx = -Math.Sin(thetaI), dz = -Math.Cos(thetaI);
            first = -1;
            thetaO = 0;
            for (int n = 0; n <= maxN; ++n)
            {
                // Nearest wall: p + t d = s (wall tip), 0 < s < 1
                double best = Double.PositiveInfinity;
                int wall = -1;
                for (int w = 0; w < 2; ++w)
                {
                    double wx = w == 0 ? lx : rx, wz = 1;
                    double det = dx * (-wz) - dz * (-wx);
                    if (Math.Abs(det) < 1e-14)
                    {
                        continue;
                    }
                    // p + t d - s w = 0
                    double t = (-px * (-wz) + pz * (-wx)) / det;
                    double s = (dx * (-pz) - dz * (-px)) / det;
                    if (t > 1e-9 && s > 0 && s < 1 && t < best)
                    {
                        best = t;
                        wall = w;
                    }
                }
                if (wall < 0)
                {
                    // Leaves through the aperture
                    thetaO = Math.Atan2(dx, dz);
                    return n;
                }
                if (n == 0)
                {
                    first = wall;
                }
                px += best * dx;
                pz += best * dz;
                // The normal of the wall, towards the inside of the groove
                double tilt = wall == 0 ? tl : tr;
                double nx = Math.Sin(tilt), nz = Math.Cos(tilt);
                double dn = dx * nx + dz * nz;
                dx -= 2 * dn * nx;
                dz -= 2 * dn * nz;
            }
            return maxN + 1;
        }

        [TEST("VCavity", "SolverAgainstTracedRays")]
        public static void VCavity_SolverAgainstTracedRays()
        {
            RNG rng = new RNG(17);
            double[] tilts = new double[] { 0.3, -0.3, 0.8, -0.8, 1.2, -1.2, 1.45, -1.45, 0.5, -1.0, 1.1, -0.4, 1.3, -1.35 };
            double[] elevations = new double[] { -1.3, -0.6, 0.0, 0.25, 0.9, 1.45 };
            double worstZ = 0, worstAngle = 0, worstTotal = 0;
            int outcomesChecked = 0;
            VCavityOutcome* outcomes = stackalloc VCavityOutcome[VCavity.kMaxOutcomes];
            for (int g = 0; g < tilts.Length; g += 2)
            {
                double tl = tilts[g], tr = tilts[g + 1];
                double psiL = Math.PI - tl, psiR = -tr;
                double lx = Math.Cos(psiL) / Math.Sin(psiL), rx = Math.Cos(psiR) / Math.Sin(psiR);
                foreach (double thetaI in elevations)
                {
                    int maxN = 64;
                    int count = VCavity.Trace(thetaI, tl, tr, maxN, outcomes);
                    double total = 0;
                    for (int k = 0; k < count; ++k)
                    {
                        total += outcomes[k].prob;
                    }
                    worstTotal = Math.Max(worstTotal, Math.Abs(total - 1));

                    int rays = 200000;
                    int[] hits = new int[count];
                    int unmatched = 0;
                    for (int r = 0; r < rays; ++r)
                    {
                        double x = lx + (rx - lx) * rng.Uniform<double>();
                        int first;
                        double thetaO;
                        int n = TraceRay(thetaI, tl, tr, x, maxN, out first, out thetaO);
                        bool found = false;
                        for (int k = 0; k < count; ++k)
                        {
                            if (outcomes[k].first == first && outcomes[k].n == n)
                            {
                                worstAngle = Math.Max(worstAngle, Math.Abs(VCavity.WrapPi(outcomes[k].thetaO - thetaO)));
                                ++hits[k];
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            ++unmatched;
                        }
                    }
                    // A ray in a thousand may graze a tip
                    EXPECT_LT(unmatched, rays / 1000);
                    for (int k = 0; k < count; ++k)
                    {
                        double expected = rays * outcomes[k].prob;
                        double z = Math.Abs(hits[k] - expected) / Math.Sqrt(expected * (1 - outcomes[k].prob) + 1);
                        worstZ = Math.Max(worstZ, z);
                        ++outcomesChecked;
                    }
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE vcavity solver: %d outcomes of 42 grooves and elevations against 200000 traced rays each: worst %.1f sigma, " +
                                                      "exit elevation within %.1e, probabilities sum to 1 within %.1e", outcomesChecked, worstZ, worstAngle, worstTotal));
            EXPECT_LT(worstZ, 4.5);
            EXPECT_LT(worstAngle, 1e-9);
            EXPECT_LT(worstTotal, 1e-9);
        }

        // One reflection: D G F / (4 cos cos) with G = min(1, 2 (n.h)(n.o) / (o.h), 2 (n.h)(n.i) / (i.h))
        [TEST("VCavity", "FirstReflectionIsCookTorrance")]
        public static void VCavity_FirstReflectionIsCookTorrance()
        {
            RNG rng = new RNG(4);
            SampledSpectrum eta = new SampledSpectrum(0.2f), k = new SampledSpectrum(3.9f);
            MicrofacetDistribution[] distributions = new MicrofacetDistribution[]
            {
                new TrowbridgeReitzDistribution(0.4f, 0.4f), new TrowbridgeReitzDistribution(0.5f, 0.15f), new BeckmannDistribution(0.4f, 0.4f),
                new StudentTDistribution(0.4f, 0.4f, 3f)
            };
            foreach (MicrofacetDistribution distribution in distributions)
            {
                BxDF bxdf = new VCavityConductorBxDF(distribution, eta, k, 1);
                double worst = 0;
                int compared = 0;
                for (int i = 0; i < 4000; ++i)
                {
                    Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Vector3f wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    wo.z = Math.Abs(wo.z);
                    wi.z = Math.Abs(wi.z);
                    if (wo.z < 0.05f || wi.z < 0.05f)
                    {
                        continue;
                    }
                    Vector3f wh = Normalize(wo + wi);
                    Float masking = Math.Min(1, Math.Min(2 * wh.z * wo.z / Dot(wo, wh), 2 * wh.z * wi.z / Dot(wi, wh)));
                    double expected = (distribution.D(wh) * masking * FrComplex(Dot(wo, wh), eta, k) / (4 * wo.z * wi.z)).Average();
                    double value = bxdf.f(wo, wi, TransportMode.Radiance).Average();
                    if (Math.Max(expected, value) > 1e-3)
                    {
                        worst = Math.Max(worst, Math.Abs(expected - value) / Math.Max(expected, value));
                        ++compared;
                    }
                }
                Console.Error.WriteLine(pbrt.StringPrintf("MEASURE vcavity first reflection %s: %d pairs against Cook-Torrance, worst relative difference %.1e",
                                                          distribution.ToString(), compared, worst));
                EXPECT_GT(compared, 1000);
                EXPECT_LT(worst, 2e-3);
            }
        }

        // The integral of the density of Sample_f over the directions. The density of a pair grows as 1 / L, L the length of
        // the sum of the projections of the two directions on the surface (it vanishes where wi is wo turned by half a turn
        // about the normal), so directions uniform over the sphere estimate the integral with a variance that has no bound.
        // The directions are taken in polar coordinates about that point of the projected disk instead, where the area
        // element r dr cancels 1 / L.
        static void DensityIntegral(BxDF bxdf, Vector3f wo, ref RNG rng, int samples, out double integral, out double error)
        {
            double sum = 0, sum2 = 0;
            double cx = -wo.x, cy = -wo.y;
            for (int i = 0; i < samples; ++i)
            {
                double psi = 2 * Math.PI * rng.Uniform<double>();
                double ux = Math.Cos(psi), uy = Math.Sin(psi);
                // The distance to the rim of the unit disk along (ux, uy) from (cx, cy)
                double b = cx * ux + cy * uy;
                double reach = -b + Math.Sqrt(Math.Max(0.0, b * b - (cx * cx + cy * cy) + 1));
                double r = reach * rng.Uniform<double>();
                double x = cx + r * ux, y = cy + r * uy;
                double z = Math.Sqrt(Math.Max(0.0, 1 - x * x - y * y));
                double value = 0;
                if (z > 1e-6)
                {
                    Vector3f wi = new Vector3f((Float)x, (Float)y, (Float)z);
                    // d(omega) = dx dy / z, and dx dy = r dr dpsi, with r and psi uniform
                    value = bxdf.PDF(wo, wi, TransportMode.Radiance, BxDFReflTransFlags.All) / z * 2 * Math.PI * reach * r;
                }
                sum += value;
                sum2 += value * value;
            }
            integral = sum / samples;
            error = Math.Sqrt(Math.Max(0.0, sum2 / samples - integral * integral) / samples);
        }

        [TEST("VCavity", "DensityIntegratesToOne")]
        public static void VCavity_DensityIntegratesToOne()
        {
            RNG rng = new RNG(8);
            SampledSpectrum one = new SampledSpectrum(1f), large = new SampledSpectrum(1e4f);
            MicrofacetDistribution[] distributions = new MicrofacetDistribution[]
            {
                new TrowbridgeReitzDistribution(0.5f, 0.5f), new TrowbridgeReitzDistribution(0.5f, 0.15f), new BeckmannDistribution(0.5f, 0.5f),
                new StudentTDistribution(0.5f, 0.5f, 3f)
            };
            foreach (MicrofacetDistribution distribution in distributions)
            {
                BxDF bxdf = new VCavityConductorBxDF(distribution, one, large, 24);
                foreach (Float cosine in new Float[] { 0.9f, 0.5f, 0.2f })
                {
                    Float sine = SafeSqrt(1 - cosine * cosine);
                    Vector3f wo = new Vector3f(sine * (Float)Math.Cos(0.7), sine * (Float)Math.Sin(0.7), cosine);
                    double integral, error;
                    DensityIntegral(bxdf, wo, ref rng, 400000, out integral, out error);
                    Console.Error.WriteLine(pbrt.StringPrintf("MEASURE vcavity density %s, cos %.1f: integral %.4f +- %.4f", distribution.ToString(), cosine,
                                                              integral, error));
                    EXPECT_LT(Math.Abs(integral - 1), Math.Max(4 * error, 0.01));
                    EXPECT_LT(error, 0.01);
                }
            }
        }

        // ---- The reference of a dielectric interface made of grooves -----------------------------------------------------
        // (docs/vcavity.md, section 5: there is no dielectric V-cavity BxDF yet; this is what one has to match)
        static bool Refract(double[] d, double nx, double ny, double nz, double r, double[] o)
        {
            double c = -(d[0] * nx + d[1] * ny + d[2] * nz);
            if (!(c > 1e-12))
            {
                return false;
            }
            double s2 = r * r * Math.Max(0.0, 1 - c * c);
            if (!(s2 < 1))
            {
                return false;
            }
            double ct = Math.Sqrt(1 - s2);
            o[0] = r * (d[0] + c * nx) - ct * nx;
            o[1] = r * (d[1] + c * ny) - ct * ny;
            o[2] = r * (d[2] + c * nz) - ct * nz;
            return true;
        }

        // ---- The periodic profile -----------------------------------------------------------------------------------------
        // The surface that the grooves stand for when they are side by side: a triangle wave of tilt alpha in the plane (t, z),
        // valleys at height 0 and ridges at height 1, air above and the medium below. A ray is followed through it until it
        // leaves upwards (above the ridges) or downwards (below the valleys): what it meets on the way, in the air of a valley
        // or in the medium of a ridge, is the same Fresnel interface. Nothing is dropped.
        static bool ReferencePeriodic(Vector3f wo, double phi, double alpha, double ior, ref RNG rng, out Vector3f wi)
        {
            wi = new Vector3f(0, 0, 0);
            double tx = Math.Cos(phi), ty = Math.Sin(phi);
            double cot = Math.Cos(alpha) / Math.Sin(alpha);
            double period = 2 * cot;
            double sinA = Math.Sin(alpha), cosA = Math.Cos(alpha);
            double[] d = new double[] { -wo.x, -wo.y, -wo.z };
            double[] o = new double[3];
            // The valley of index k has its bottom at t = k period; its left wall goes up to (k - 1/2) period, its right wall to
            // (k + 1/2) period
            double pt = period * rng.Uniform<double>();
            double pz = wo.z > 0 ? 1 : 0;
            bool inAir = wo.z > 0;
            long lastValley = long.MinValue;
            int lastWall = -1;
            for (int step = 0; step < 2000; ++step)
            {
                double dt = d[0] * tx + d[1] * ty, dz = d[2];
                // The walls the ray can meet before it leaves the band of heights [0, 1]
                double best = Double.PositiveInfinity;
                long hitValley = 0;
                int hitWall = -1;
                double reach = Double.PositiveInfinity;
                if (dz > 1e-14)
                {
                    reach = (1 - pz) / dz;
                }
                else if (dz < -1e-14)
                {
                    reach = (0 - pz) / dz;
                }
                double tEnd = Double.IsInfinity(reach) ? pt + 1e6 * Math.Sign(dt) : pt + reach * dt;
                long first = (long)Math.Floor(Math.Min(pt, tEnd) / period) - 1, last = (long)Math.Ceiling(Math.Max(pt, tEnd) / period) + 1;
                if (last - first > 4000)
                {
                    return false;
                }
                for (long valley = first; valley <= last; ++valley)
                {
                    for (int wall = 0; wall < 2; ++wall)
                    {
                        if (valley == lastValley && wall == lastWall)
                        {
                            continue;
                        }
                        // The wall from (valley period, 0) to (valley period -+ cot, 1)
                        double bx = valley * period, ex = wall == 0 ? -cot : cot;
                        double det = dt * (-1.0) - dz * (-ex);
                        if (Math.Abs(det) < 1e-14)
                        {
                            continue;
                        }
                        double rx = bx - pt, rz = 0 - pz;
                        double l = (rx * (-1.0) - rz * (-ex)) / det;
                        double along = (dt * rz - dz * rx) / det;
                        if (l > 1e-12 && along >= 0 && along <= 1 && l < best)
                        {
                            best = l;
                            hitValley = valley;
                            hitWall = wall;
                        }
                    }
                }
                if (hitWall < 0)
                {
                    // Out of the band
                    wi = new Vector3f((Float)d[0], (Float)d[1], (Float)d[2]);
                    return true;
                }
                pt += best * dt;
                pz += best * dz;
                // The normal of the wall towards the air
                double nt = hitWall == 0 ? sinA : -sinA;
                double nx = nt * tx, ny = nt * ty, nz = cosA;
                double dn = d[0] * nx + d[1] * ny + d[2] * nz;
                // The ray is in the air when it travels against that normal
                inAir = dn < 0;
                double cosine = Math.Abs(dn);
                double relative = inAir ? ior : 1 / ior;
                double reflectance = FrDielectric((Float)Math.Min(cosine, 1.0), (Float)relative);
                lastValley = hitValley;
                lastWall = hitWall;
                if (rng.Uniform<double>() < reflectance)
                {
                    d[0] -= 2 * dn * nx;
                    d[1] -= 2 * dn * ny;
                    d[2] -= 2 * dn * nz;
                    continue;
                }
                double side = inAir ? 1 : -1;
                if (!Refract(d, side * nx, side * ny, side * nz, 1 / relative, o))
                {
                    d[0] -= 2 * dn * nx;
                    d[1] -= 2 * dn * ny;
                    d[2] -= 2 * dn * nz;
                    continue;
                }
                d[0] = o[0];
                d[1] = o[1];
                d[2] = o[2];
            }
            return false;
        }

        // The reciprocity of a model from rays alone, with no density. For a patch of directions A above the surface and a
        // patch B below it, both taken with the cosine distribution, an interface that is reciprocal sends from A into B the
        // part that it sends from B into A, times ior^2 and the ratio of the projected solid angles of the patches:
        //     Omega_A P(A to B) / ior^2 = Omega_B P(B to A).
        // (For two patches on the same side there is no ior^2.) The patches are caps about two directions.
        static void PatchTransfer(MicrofacetDistribution distribution, double ior, Vector3f a, Vector3f b, double capCosine, ref RNG rng,
                                  int samples, out double fromA, out double fromB, out double errorA, out double errorB, out double kept)
        {
            double hitsA = 0, hitsB = 0, keptRays = 0;
            for (int i = 0; i < samples; ++i)
            {
                for (int side = 0; side < 2; ++side)
                {
                    // A direction of the cap about a (or b), uniform in solid angle; its cosine is its weight
                    Vector3f axis = side == 0 ? a : b, target = side == 0 ? b : a;
                    double z = 1 - rng.Uniform<double>() * (1 - capCosine), r = Math.Sqrt(Math.Max(0.0, 1 - z * z)), phi = 2 * Math.PI * rng.Uniform<double>();
                    Vector3f wo = Frame.FromZ(axis).FromLocal(new Vector3f((Float)(r * Math.Cos(phi)), (Float)(r * Math.Sin(phi)), (Float)z));
                    if (wo.z * axis.z <= 0)
                    {
                        continue;
                    }
                    Vector3f s = distribution.Sample_wm(new Vector3f(0, 0, 1), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    double sinA = Math.Sqrt((double)s.x * s.x + (double)s.y * s.y);
                    if (!(sinA > 1e-9))
                    {
                        continue;
                    }
                    Vector3f wi;
                    if (!ReferencePeriodic(wo, Math.Atan2(s.y, s.x), Math.Atan2(sinA, s.z), ior, ref rng, out wi))
                    {
                        continue;
                    }
                    keptRays += 0.5;
                    if (Dot(wi, target) > capCosine)
                    {
                        if (side == 0)
                        {
                            hitsA += Math.Abs(wo.z);
                        }
                        else
                        {
                            hitsB += Math.Abs(wo.z);
                        }
                    }
                }
            }
            // The cosine-weighted transfer per unit solid angle of the starting cap (the caps have the same solid angle)
            fromA = hitsA / samples;
            fromB = hitsB / samples;
            errorA = Math.Sqrt(Math.Max(hitsA, 1.0)) / samples;
            errorB = Math.Sqrt(Math.Max(hitsB, 1.0)) / samples;
            kept = keptRays / samples;
        }

        // The periodic profile is reciprocal and keeps every ray: the two properties a dielectric V-cavity BxDF has to have, and
        // that the one-valley model that was tried first did not (docs/vcavity.md)
        [TEST("VCavity", "PeriodicDielectricReferenceIsReciprocal")]
        public static void VCavity_PeriodicDielectricReferenceIsReciprocal()
        {
            RNG rng = new RNG(61);
            MicrofacetDistribution distribution = new TrowbridgeReitzDistribution(0.5f, 0.5f);
            double ior = 1.5, capCosine = Math.Cos(0.35);
            Vector3f[] above = new Vector3f[] { Normalize(new Vector3f(0.3f, 0.1f, 0.9f)), Normalize(new Vector3f(-0.7f, 0.2f, 0.6f)),
                                                Normalize(new Vector3f(0.1f, 0.8f, 0.5f)) };
            Vector3f[] below = new Vector3f[] { Normalize(new Vector3f(-0.2f, -0.1f, -0.95f)), Normalize(new Vector3f(0.5f, -0.3f, -0.7f)),
                                                Normalize(new Vector3f(-0.6f, 0.5f, -0.55f)) };
            double worst = 0, worstSigma = 0, keptLeast = 1;
            for (int i = 0; i < above.Length; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    // Transmission pairs, and one reflection pair inside the medium
                    Vector3f a = j < 3 ? above[i] : below[i], b = j < 3 ? below[j] : below[(i + 1) % 3];
                    double factor = j < 3 ? ior * ior : 1;
                    double fromA, fromB, errorA, errorB, kept;
                    PatchTransfer(distribution, ior, a, b, capCosine, ref rng, 300000, out fromA, out fromB, out errorA, out errorB, out kept);
                    double left = fromA / factor, right = fromB;
                    double error = Math.Sqrt(Sqr(errorA / factor) + Sqr(errorB));
                    if (Math.Max(left, right) > 20 * error)
                    {
                        worst = Math.Max(worst, Math.Abs(left - right) / Math.Max(left, right));
                        worstSigma = Math.Max(worstSigma, Math.Abs(left - right) / error);
                    }
                    keptLeast = Math.Min(keptLeast, kept);
                }
            }
            Console.Error.WriteLine(pbrt.StringPrintf("MEASURE vcavity periodic dielectric reference: reciprocity of 12 pairs of patches, worst relative difference %.3f " +
                                                      "(%.1f sigma); rays kept, at least %.4f", worst, worstSigma, keptLeast));
            EXPECT_LT(worstSigma, 4.5, "the periodic profile is not reciprocal");
            EXPECT_GT(keptLeast, 0.999, "the periodic profile loses rays");
        }
    }
}
