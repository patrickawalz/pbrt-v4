// (not in pbrt-v4) furtable.cs
//
// The fiber of a cuticle, a cortex and a medulla by the five lobes of
//
//   Ling-Qi Yan, Henrik Wann Jensen, Ravi Ramamoorthi. 2017. An Efficient and Practical Near and Far Field Fur Reflectance
//   Model. ACM Trans. Graph. 36, 4, Article 67.
//
// beside the random walk of FurBxDF: R, TT and TRT as those of HairBxDF, with the medulla on their way (their equations 13
// to 16: the attenuation of the cortex over 2 p sc and of the medulla over 2 p sm), and the scattered lobes TTs and TRTs
// (equations 9, 10, 20, 21) from tables of what a medulla scatters. THIS IS A TABLE (the exception to closed forms that was
// asked for, to compare with the walk): the paper's own tables are not at hand, so they are made here, by the walk of a
// cylinder that scatters, once for each pair (scattering across the medulla, g):
//
//   CN(h, phi)        how much of the light that meets the medulla at the offset h (of its radius) is scattered, and to which
//                     angle phi around the axis from the direction it came in (16 offsets, 72 angles)
//   CM(theta, theta') to which inclination, for the light scattered on (|phi| < 90 degrees) and back, each of unit integral
//                     over the sine of the inclination (16 inclinations in, 32 out)
//
// As in the paper the scattered light leaves without a refraction at the cuticle, from the axis: through the medulla over
// kappa and the cortex over 1 - kappa.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // FurScatterTable Definition
    public sealed class FurScatterTable
    {
        public const int Offsets = 16, Angles = 72, Inclinations = 16, Sines = 32;

        // (offset, angle): the part of the light scattered to the angle, for each radian
        public readonly float[] around = new float[Offsets * Angles];
        // (offset): the part of the light that is scattered at all
        public readonly float[] scattered = new float[Offsets];
        // (on or back, inclination in, sine out): for each unit of the sine
        public readonly float[] along = new float[2 * Inclinations * Sines];

        // The table of a medulla with `thickness` (its scattering coefficient times its radius) and the mean cosine g
        public static FurScatterTable Get(double thickness, double g)
        {
            // (to two digits of the thickness, and to 0.05 of g)
            double scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(thickness, 1e-6))) - 1);
            long key = (long)Math.Round(thickness / scale) * 1000 + (long)Math.Floor(Math.Log10(Math.Max(thickness, 1e-6)) + 50) * 100000000L + (long)Math.Round(g * 20 + 20);
            lock (tables)
            {
                if (!tables.TryGetValue(key, out FurScatterTable table))
                {
                    table = new FurScatterTable(Math.Round(thickness / scale) * scale, Math.Round(g * 20) / 20.0);
                    tables[key] = table;
                }
                return table;
            }
        }

        private static readonly Dictionary<long, FurScatterTable> tables = new Dictionary<long, FurScatterTable>();

        private FurScatterTable(double thickness, double g)
        {
            const int walks = 40000;
            Random random = new Random(7);
            for (int i = 0; i < Offsets; ++i)
            {
                double offset = (i + 0.5) / Offsets;
                int count = 0;
                for (int k = 0; k < walks; ++k)
                {
                    if (Walk(random, thickness, g, offset, 0, out double dx, out double dy, out double dz))
                    {
                        ++count;
                        double angle = Math.Atan2(dz, dy);
                        int bin = Math.Min(Angles - 1, (int)((angle + Math.PI) / (2 * Math.PI) * Angles));
                        around[i * Angles + bin] += (float)(1.0 / (walks * 2 * Math.PI / Angles));
                    }
                }
                scattered[i] = (float)count / walks;
            }
            for (int i = 0; i < Inclinations; ++i)
            {
                double sine = (i + 0.5) / Inclinations;
                int[] counts = new int[2];
                for (int k = 0; k < walks; ++k)
                {
                    if (Walk(random, thickness, g, 2 * random.NextDouble() - 1, Math.Asin(sine), out double dx, out double dy, out double dz))
                    {
                        int side = dy > 0 ? 0 : 1;
                        int bin = Math.Min(Sines - 1, (int)((dx + 1) / 2 * Sines));
                        along[(side * Inclinations + i) * Sines + bin] += 1;
                        ++counts[side];
                    }
                }
                for (int side = 0; side < 2; ++side)
                {
                    for (int bin = 0; bin < Sines; ++bin)
                    {
                        int at = (side * Inclinations + i) * Sines + bin;
                        along[at] = counts[side] > 0 ? along[at] / (float)(counts[side] * 2.0 / Sines) : 0.5f;
                    }
                }
            }
        }

        // The walk through a cylinder of unit radius of light that comes along (sin theta, cos theta, 0) at the offset in z:
        // false where it is not scattered; the direction it leaves in
        private static bool Walk(Random random, double thickness, double g, double offset, double theta, out double dx, out double dy, out double dz)
        {
            dx = Math.Sin(theta);
            dy = Math.Cos(theta);
            dz = 0;
            double y = -Math.Sqrt(Math.Max(0, 1 - offset * offset)), z = offset;
            bool scattered = false;
            for (int step = 0; step < 4096; ++step)
            {
                double a = dy * dy + dz * dz, b = y * dy + z * dz;
                if (!(a > 1e-14))
                {
                    return false;
                }
                double distance = (-b + Math.Sqrt(Math.Max(0, b * b - a * (y * y + z * z - 1)))) / a;
                double flight = -Math.Log(1 - random.NextDouble()) / thickness;
                if (flight >= distance)
                {
                    return scattered;
                }
                y += flight * dy;
                z += flight * dz;
                scattered = true;
                // (Henyey and Greenstein)
                double u = random.NextDouble(), cosine;
                if (Math.Abs(g) < 1e-3)
                {
                    cosine = 1 - 2 * u;
                }
                else
                {
                    double term = (1 - g * g) / (1 + g - 2 * g * u);
                    cosine = (1 + g * g - term * term) / (2 * g);
                }
                double sine = Math.Sqrt(Math.Max(0, 1 - cosine * cosine)), phi = 2 * Math.PI * random.NextDouble();
                double ax, ay, az;
                if (Math.Abs(dx) < 0.9)
                {
                    ax = 0;
                    ay = dz;
                    az = -dy;
                }
                else
                {
                    ax = -dz;
                    ay = 0;
                    az = dx;
                }
                double length = Math.Sqrt(ax * ax + ay * ay + az * az);
                ax /= length;
                ay /= length;
                az /= length;
                double bx = dy * az - dz * ay, by = dz * ax - dx * az, bz = dx * ay - dy * ax;
                double c = sine * Math.Cos(phi), s = sine * Math.Sin(phi);
                double nx = cosine * dx + c * ax + s * bx, ny = cosine * dy + c * ay + s * by, nz = cosine * dz + c * az + s * bz;
                double unit = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                dx = nx / unit;
                dy = ny / unit;
                dz = nz / unit;
            }
            return false;
        }
    }

    // FurTableBxDF Definition
    public sealed unsafe class FurTableBxDF : BxDF
    {
        // FurTableBxDF Public Methods
        public FurTableBxDF(Float h, Float eta, SampledSpectrum sigmaCortex, Float kappa, Float sigmaScatter, SampledSpectrum sigmaMedulla, Float g, Float beta_m, Float beta_n,
                            Float alpha)
        {
            this.h = h;
            this.eta = eta;
            this.sigmaCortex = sigmaCortex;
            this.kappa = kappa;
            this.sigmaScatter = sigmaScatter;
            this.sigmaMedulla = sigmaMedulla;
            table = kappa > 0 && sigmaScatter > 0 ? FurScatterTable.Get(kappa * sigmaScatter, g) : null;
            v0 = pbrt.Sqr(0.726f * beta_m + 0.812f * pbrt.Sqr(beta_m) + 3.7f * pbrt.Pow20(beta_m));
            s = 0.626657069f * (0.265f * beta_n + 1.194f * pbrt.Sqr(beta_n) + 5.372f * pbrt.Pow22(beta_n));
            sinAlpha = (Float)Math.Sin(alpha * Math.PI / 180);
            cosAlpha = (Float)Math.Cos(alpha * Math.PI / 180);
        }

        public static string Name()
        {
            return "FurTableBxDF";
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FurTableBxDF h: %f eta: %f kappa: %f ]", h, eta, kappa);
        }

        public override void Regularize()
        {
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.GlossyReflection;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            Lobes(wo, out Geometry geometry);
            SampledSpectrum sum = new SampledSpectrum(0f);
            for (int lobe = 0; lobe < 5; ++lobe)
            {
                Float density = LobeDensity(lobe, wo, wi, geometry);
                if (lobe == 0)
                {
                    sum += geometry.a0 * density;
                }
                else if (lobe == 1)
                {
                    sum += geometry.a1 * density;
                }
                else if (lobe == 2)
                {
                    sum += geometry.a2 * density;
                }
                else if (lobe == 3)
                {
                    sum += geometry.s1 * density;
                }
                else
                {
                    sum += geometry.s2 * density;
                }
            }
            return pbrt.AbsCosTheta(wi) > 0 ? sum / pbrt.AbsCosTheta(wi) : sum;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Lobes(wo, out Geometry geometry);
            Float pdf = 0;
            for (int lobe = 0; lobe < 5; ++lobe)
            {
                pdf += geometry.Chance(lobe) * LobeDensity(lobe, wo, wi, geometry);
            }
            return pdf;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Lobes(wo, out Geometry geometry);
            int lobe = 0;
            Float chance = geometry.Chance(0);
            while (lobe < 4 && uc >= chance)
            {
                uc -= chance;
                chance = geometry.Chance(++lobe);
            }
            uc = chance > 0 ? Math.Min(uc / chance, pbrt.OneMinusEpsilon) : 0;
            Float sinTheta_o = wo.x, cosTheta_o = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_o)), phi_o = (Float)Math.Atan2(wo.z, wo.y);
            Float sinTheta_i, phi_i;
            if (lobe < 3)
            {
                Tilted(lobe, sinTheta_o, cosTheta_o, out Float sinTilted, out Float cosTilted);
                Float variance = lobe == 0 ? v0 : lobe == 1 ? 0.25f * v0 : 4 * v0;
                Float cosTheta = 1 + variance * (Float)Math.Log(Math.Max(u[0], 1e-5f) + (1 - u[0]) * Math.Exp(-2 / variance));
                Float sinTheta = pbrt.SafeSqrt(1 - pbrt.Sqr(cosTheta));
                sinTheta_i = -cosTheta * sinTilted + sinTheta * (Float)Math.Cos(2 * Math.PI * u[1]) * cosTilted;
                phi_i = phi_o + geometry.Center(lobe) + pbrt.SampleTrimmedLogistic(uc, s, -pbrt.Pi, pbrt.Pi);
            }
            else
            {
                // The angle around the axis from the row of the offset, and the inclination from the rows on and back
                int row = geometry.Row(lobe);
                Float mirror = geometry.Offset(lobe) < 0 ? -1 : 1;
                Float target = uc * table.scattered[row], sum = 0;
                int bin = 0;
                Float width = 2 * pbrt.Pi / FurScatterTable.Angles;
                while (bin < FurScatterTable.Angles - 1 && sum + table.around[row * FurScatterTable.Angles + bin] * width < target)
                {
                    sum += table.around[row * FurScatterTable.Angles + bin] * width;
                    ++bin;
                }
                Float inside = table.around[row * FurScatterTable.Angles + bin] > 0 ? (target - sum) / (table.around[row * FurScatterTable.Angles + bin] * width) : 0.5f;
                Float angle = -pbrt.Pi + (bin + Math.Min(Math.Max(inside, 0), pbrt.OneMinusEpsilon)) * width;
                phi_i = phi_o + geometry.Center(lobe) + mirror * angle;
                Float back = Math.Abs(angle) / pbrt.Pi;
                int side = u[1] < back ? 1 : 0;
                int inclination = InclinationRow(-sinTheta_o);
                Float flip = -sinTheta_o < 0 ? -1 : 1;
                Float goal = u[0], total = 0;
                Float step = 2f / FurScatterTable.Sines;
                int sineBin = 0;
                int start = (side * FurScatterTable.Inclinations + inclination) * FurScatterTable.Sines;
                while (sineBin < FurScatterTable.Sines - 1 && total + table.along[start + sineBin] * step < goal)
                {
                    total += table.along[start + sineBin] * step;
                    ++sineBin;
                }
                Float within = table.along[start + sineBin] > 0 ? (goal - total) / (table.along[start + sineBin] * step) : 0.5f;
                sinTheta_i = flip * (-1 + (sineBin + Math.Min(Math.Max(within, 0), pbrt.OneMinusEpsilon)) * step);
            }
            Float cosTheta_i = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_i));
            Vector3f wi = new Vector3f(sinTheta_i, cosTheta_i * (Float)Math.Cos(phi_i), cosTheta_i * (Float)Math.Sin(phi_i));
            Float pdf = PDF(wo, wi, mode, sampleFlags);
            if (!(pdf > 0))
            {
                return default;
            }
            return new BSDFSample(f(wo, wi, mode), wi, pdf, Flags());
        }

        // FurTableBxDF Private Methods

        // What does not depend on wi: the angles of the refraction, the attenuation of each lobe, and where the scattered
        // lobes meet the medulla
        private struct Geometry
        {
            public Float gammaO, gammaT;
            public SampledSpectrum a0, a1, a2, s1, s2;
            public Float chance0, chance1, chance2, chance3, chance4;
            public Float offset1, offset2;

            public Float Chance(int lobe)
            {
                return lobe == 0 ? chance0 : lobe == 1 ? chance1 : lobe == 2 ? chance2 : lobe == 3 ? chance3 : chance4;
            }

            // The middle of a lobe around the axis, from the direction of wo: those of HairBxDF (2 p gammaT - 2 gammaO + p pi), and
            // for a scattered lobe the direction of the light where it meets the medulla, after one refraction and p - 1
            // reflections inside
            public Float Center(int lobe)
            {
                if (lobe < 3)
                {
                    return 2 * lobe * gammaT - 2 * gammaO + lobe * pbrt.Pi;
                }
                int p = lobe - 2;
                return pbrt.Pi + (gammaT - gammaO) + (p - 1) * (pbrt.Pi + 2 * gammaT);
            }

            public Float Offset(int lobe)
            {
                return lobe == 3 ? offset1 : offset2;
            }

            public int Row(int lobe)
            {
                return Math.Min(FurScatterTable.Offsets - 1, (int)(Math.Abs(Offset(lobe)) * FurScatterTable.Offsets));
            }
        }

        private static int InclinationRow(Float sine)
        {
            return Math.Min(FurScatterTable.Inclinations - 1, (int)(Math.Abs(sine) * FurScatterTable.Inclinations));
        }

        private void Lobes(Vector3f wo, out Geometry geometry)
        {
            geometry = default;
            Float sinTheta_o = wo.x, cosTheta_o = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_o));
            Float sinTheta_t = sinTheta_o / eta, cosTheta_t = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_t));
            Float etap = pbrt.SafeSqrt(pbrt.Sqr(eta) - pbrt.Sqr(sinTheta_o)) / cosTheta_o;
            Float sinGamma_t = h / etap, cosGamma_t = pbrt.SafeSqrt(1 - pbrt.Sqr(sinGamma_t));
            geometry.gammaO = pbrt.SafeASin(h);
            geometry.gammaT = pbrt.SafeASin(sinGamma_t);
            // Half the way through the medulla and through the cortex of one crossing of the fiber
            Float medulla = Math.Abs(sinGamma_t) < kappa ? pbrt.SafeSqrt(kappa * kappa - sinGamma_t * sinGamma_t) : 0;
            Float cortex = cosGamma_t - medulla;
            SampledSpectrum extinction = sigmaMedulla + new SampledSpectrum(sigmaScatter);
            // (one crossing: of the cortex over 2 sc and of the medulla over 2 sm)
            SampledSpectrum crossing = pbrt.Exp((sigmaCortex * (2 * cortex) + extinction * (2 * medulla)) * (-1 / cosTheta_t));
            Float fresnel = pbrt.FrDielectric(cosTheta_o * pbrt.SafeSqrt(1 - pbrt.Sqr(h)), eta);
            geometry.a0 = new SampledSpectrum(fresnel);
            geometry.a1 = crossing * pbrt.Sqr(1 - fresnel);
            geometry.a2 = geometry.a1 * crossing * fresnel;
            geometry.s1 = new SampledSpectrum(0f);
            geometry.s2 = new SampledSpectrum(0f);
            if (table != null && medulla > 0)
            {
                // To the medulla: Ta over (2 p - 1) sc and 2 (p - 1) sm; from its axis out: Tb over kappa and 1 - kappa
                SampledSpectrum leaving = pbrt.Exp((sigmaMedulla * kappa + sigmaCortex * (1 - kappa)) * (-1 / cosTheta_t));
                SampledSpectrum first = pbrt.Exp(sigmaCortex * (-cortex / cosTheta_t));
                SampledSpectrum second = pbrt.Exp((sigmaCortex * (3 * cortex) + extinction * (2 * medulla)) * (-1 / cosTheta_t));
                // The offset at which each segment meets the medulla, of its radius: by the side of the axis that it passes
                double phiO = Math.Atan2(wo.z, wo.y), normal = phiO - geometry.gammaO;
                double direction = phiO + geometry.Center(3);
                geometry.offset1 = (Float)(Math.Sin(normal - direction) / kappa);
                // (the second segment leaves from the far end of the first, turned by pi + 2 gammaT)
                double py = Math.Cos(normal) + 2 * cosGamma_t * Math.Cos(direction), pz = Math.Sin(normal) + 2 * cosGamma_t * Math.Sin(direction);
                double next = phiO + geometry.Center(4);
                geometry.offset2 = (Float)((Math.Cos(next) * pz - Math.Sin(next) * py) / kappa);
                geometry.offset1 = Math.Max(-1, Math.Min(1, geometry.offset1));
                geometry.offset2 = Math.Max(-1, Math.Min(1, geometry.offset2));
                geometry.s1 = first * leaving * ((1 - fresnel) * table.scattered[geometry.Row(3)]);
                geometry.s2 = second * leaving * ((1 - fresnel) * fresnel * table.scattered[geometry.Row(4)]);
            }
            Float total = geometry.a0.Average() + geometry.a1.Average() + geometry.a2.Average() + geometry.s1.Average() + geometry.s2.Average();
            if (total > 0)
            {
                geometry.chance0 = geometry.a0.Average() / total;
                geometry.chance1 = geometry.a1.Average() / total;
                geometry.chance2 = geometry.a2.Average() / total;
                geometry.chance3 = geometry.s1.Average() / total;
                geometry.chance4 = geometry.s2.Average() / total;
            }
        }

        // The inclination of wo turned for the tilt of the scales, for each of the lobes that are not scattered (HairBxDF's)
        private void Tilted(int lobe, Float sinTheta_o, Float cosTheta_o, out Float sine, out Float cosine)
        {
            Float sin2 = 2 * cosAlpha * sinAlpha, cos2 = pbrt.Sqr(cosAlpha) - pbrt.Sqr(sinAlpha);
            Float sin4 = 2 * cos2 * sin2, cos4 = pbrt.Sqr(cos2) - pbrt.Sqr(sin2);
            if (lobe == 0)
            {
                sine = sinTheta_o * cos2 - cosTheta_o * sin2;
                cosine = cosTheta_o * cos2 + sinTheta_o * sin2;
            }
            else if (lobe == 1)
            {
                sine = sinTheta_o * cosAlpha + cosTheta_o * sinAlpha;
                cosine = cosTheta_o * cosAlpha - sinTheta_o * sinAlpha;
            }
            else
            {
                sine = sinTheta_o * cos4 + cosTheta_o * sin4;
                cosine = cosTheta_o * cos4 - sinTheta_o * sin4;
            }
            cosine = Math.Abs(cosine);
        }

        // The density of a lobe at wi for each solid angle (its attenuation apart): Mp N of the lobes that are not scattered, and
        // the tables of those that are (with the part scattered divided out: it is in the attenuation)
        private Float LobeDensity(int lobe, Vector3f wo, Vector3f wi, Geometry geometry)
        {
            Float sinTheta_o = wo.x, cosTheta_o = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_o));
            Float sinTheta_i = wi.x, cosTheta_i = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_i));
            Float dphi = (Float)(Math.Atan2(wi.z, wi.y) - Math.Atan2(wo.z, wo.y)) - geometry.Center(lobe);
            while (dphi > pbrt.Pi)
            {
                dphi -= 2 * pbrt.Pi;
            }
            while (dphi < -pbrt.Pi)
            {
                dphi += 2 * pbrt.Pi;
            }
            if (lobe < 3)
            {
                Tilted(lobe, sinTheta_o, cosTheta_o, out Float sine, out Float cosine);
                Float variance = lobe == 0 ? v0 : lobe == 1 ? 0.25f * v0 : 4 * v0;
                return HairBxDF.Mp(cosTheta_i, cosine, sinTheta_i, sine, variance) * pbrt.TrimmedLogistic(dphi, s, -pbrt.Pi, pbrt.Pi);
            }
            if (table == null || geometry.Chance(lobe) == 0)
            {
                return 0;
            }
            int row = geometry.Row(lobe);
            if (!(table.scattered[row] > 0))
            {
                return 0;
            }
            Float angle = geometry.Offset(lobe) < 0 ? -dphi : dphi;
            int bin = Math.Min(FurScatterTable.Angles - 1, (int)((angle + pbrt.Pi) / (2 * pbrt.Pi) * FurScatterTable.Angles));
            Float around = table.around[row * FurScatterTable.Angles + bin] / table.scattered[row];
            Float back = Math.Abs(angle) / pbrt.Pi;
            int inclination = InclinationRow(-sinTheta_o);
            Float sineOut = -sinTheta_o < 0 ? -sinTheta_i : sinTheta_i;
            int sineBin = Math.Min(FurScatterTable.Sines - 1, (int)((sineOut + 1) / 2 * FurScatterTable.Sines));
            Float on = table.along[inclination * FurScatterTable.Sines + sineBin];
            Float behind = table.along[(FurScatterTable.Inclinations + inclination) * FurScatterTable.Sines + sineBin];
            return around * ((1 - back) * on + back * behind);
        }

        // FurTableBxDF Private Members
        private readonly Float h, eta, kappa, sigmaScatter, v0, s, sinAlpha, cosAlpha;
        private readonly SampledSpectrum sigmaCortex, sigmaMedulla;
        private readonly FurScatterTable table;
    }
}
