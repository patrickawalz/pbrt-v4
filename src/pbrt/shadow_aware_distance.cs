// (not in pbrt-v4) shadow_aware_distance.cs
//
// Planetary shadow-aware distance sampling (after Breyer and Zirr, "Planetary Shadow-Aware Distance Sampling", EGSR 2022): the
// scattering point of a segment of a ray in the atmosphere, chosen for a connection to the sun, is only sampled where the sun can
// be seen. For a direction toward the sun the planet shadows a half cylinder behind it (the cylinder of the radius of the planet
// whose axis is the direction, the part of it on the night side); the points of the ray outside of it are the lit ones, and there
// are at most two intervals of them. Their distances are sampled in proportion to the density of the collisions of the majorant,
// sigma_maj(t) exp(-tau_maj(t)), as the paper maps uniform samples to opacities with the intervals of shadow cut out: the
// cumulative of the density in a piece of a shell (a constant majorant) is inverted in closed form.
//
// Every wavelength has its own majorants, so the density of a distance is a spectrum, the density that each wavelength would have
// if it were the hero wavelength; the first one samples. The densities go into the rescaled path probabilities of the strategies
// as the density of uniform distance sampling does, only per wavelength (see VolPathIntegrator.SampleShadowAware).
//
// The strategy is a function of the direction toward the light. It has no density (it does not exist) for a direction for which
// the ray is lit all along or shadowed all along, and every strategy that finds the same path asks for that same density.

using System;

namespace pbrt
{
    using static pbrt;

    public sealed class ShadowAwareDistance
    {
        // The pieces of the ray that are lit, one for every piece of a shell the lit intervals cover, in the order of the ray
        const int Capacity = 512;
        readonly double[] start = new double[Capacity];
        readonly double[] end = new double[Capacity];
        readonly double[] sigma = new double[Capacity * NSpectrumSamples];
        readonly double[] tauStart = new double[Capacity * NSpectrumSamples];
        // The probability that the first collision of the majorant is in the piece, for each wavelength
        readonly double[] mass = new double[Capacity * NSpectrumSamples];
        readonly double[] total = new double[NSpectrumSamples];
        readonly double[] tau = new double[NSpectrumSamples];
        int count;

        [ThreadStatic]
        static ShadowAwareDistance scratch;

        // A buffer of the calling thread, for a density that is asked for and used at once
        public static ShadowAwareDistance Scratch()
        {
            if (scratch == null)
            {
                scratch = new ShadowAwareDistance();
            }
            return scratch;
        }

        public int Pieces() { return count; }

        // The ends of the interval of the ray [0, tMax] that is in the planet's shadow, for the direction toward the light, in
        // double precision relative to the centre of the planet. False if the ray is lit all along or shadowed all along.
        public static bool ShadowInterval(double cx, double cy, double cz, double radius, Point3f origin, Vector3f direction,
                                          double tMax, Vector3f toLight, out double shadowStart, out double shadowEnd)
        {
            shadowStart = 0;
            shadowEnd = 0;
            double lx = toLight.x, ly = toLight.y, lz = toLight.z;
            double ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            lx /= ll;
            ly /= ll;
            lz /= ll;
            double qx = origin.x - cx, qy = origin.y - cy, qz = origin.z - cz;
            double dx = direction.x, dy = direction.y, dz = direction.z;
            double s0 = qx * lx + qy * ly + qz * lz;
            double ds = dx * lx + dy * ly + dz * lz;
            // The components perpendicular to the axis of the cylinder
            double px = qx - s0 * lx, py = qy - s0 * ly, pz = qz - s0 * lz;
            double ex = dx - ds * lx, ey = dy - ds * ly, ez = dz - ds * lz;
            double a = ex * ex + ey * ey + ez * ez;
            double b = px * ex + py * ey + pz * ez;
            double c = px * px + py * py + pz * pz - radius * radius;
            double lo, hi;
            if (a > 1e-14)
            {
                double discriminant = b * b - a * c;
                if (discriminant <= 0)
                {
                    return false;
                }
                // The stable form of the two roots
                double root = Math.Sqrt(discriminant);
                double q = -(b + (b >= 0 ? root : -root));
                double t1 = q / a;
                double t2 = q != 0 ? c / q : t1;
                lo = Math.Min(t1, t2);
                hi = Math.Max(t1, t2);
            }
            else
            {
                // A ray along the axis is inside the cylinder or outside of it
                if (c >= 0)
                {
                    return false;
                }
                lo = double.NegativeInfinity;
                hi = double.PositiveInfinity;
            }
            // Only the half of the cylinder behind the planet shadows (s < 0)
            if (ds > 0)
            {
                hi = Math.Min(hi, -s0 / ds);
            }
            else if (ds < 0)
            {
                lo = Math.Max(lo, -s0 / ds);
            }
            else if (s0 >= 0)
            {
                return false;
            }
            if (!(lo < hi) || hi <= 0 || lo >= tMax)
            {
                return false;
            }
            shadowStart = Math.Max(lo, 0.0);
            shadowEnd = Math.Min(hi, tMax);
            // No lit interval at all: shadowed all along
            if (shadowStart <= 0 && shadowEnd >= tMax)
            {
                return false;
            }
            return true;
        }

        // The lit pieces of the segment with the majorants of the medium. False if the strategy does not exist for this ray and
        // direction (the ray is lit all along, or shadowed all along, or the lit part has no collisions of the first wavelength).
        public bool Build(AtmosphereMedium atmosphere, Point3f origin, Vector3f direction, Float tMax, Vector3f toLight,
                          SampledWavelengths lambda)
        {
            count = 0;
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                total[c] = 0;
                tau[c] = 0;
            }
            if (!(tMax > 0))
            {
                return false;
            }
            double extent = Float.IsInfinity(tMax) ? double.MaxValue : tMax;
            if (!ShadowInterval(atmosphere.CenterX(), atmosphere.CenterY(), atmosphere.CenterZ(), atmosphere.PlanetRadiusUnits(), origin,
                                direction, extent, toLight, out double shadowStart, out double shadowEnd))
            {
                return false;
            }
            AtmosphereMajorantIterator iterator = atmosphere.SampleRay(new Ray(origin, direction), tMax, lambda);
            while (true)
            {
                pstd.optional<RayMajorantSegment> next = iterator.Next();
                if (!next)
                {
                    break;
                }
                RayMajorantSegment piece = next.value();
                double m0 = piece.tMin, m1 = piece.tMax;
                // The lit part before the shadow and the one after it
                for (int k = 0; k < 2 && count < Capacity; ++k)
                {
                    double a = Math.Max(m0, k == 0 ? 0.0 : shadowEnd);
                    double b = Math.Min(m1, k == 0 ? shadowStart : extent);
                    if (!(b > a))
                    {
                        continue;
                    }
                    start[count] = a;
                    end[count] = b;
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        int index = count * NSpectrumSamples + c;
                        sigma[index] = piece.sigma_maj[c];
                        tauStart[index] = tau[c] + piece.sigma_maj[c] * (a - m0);
                        mass[index] = Math.Exp(-tauStart[index]) * -ExpM1(-sigma[index] * (b - a));
                        total[c] += mass[index];
                    }
                    ++count;
                }
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    tau[c] += piece.sigma_maj[c] * (m1 - m0);
                }
            }
            return count > 0 && total[0] > 0;
        }

        // A distance with the density of the first wavelength's collisions of the majorant, restricted to the lit pieces, and the
        // density of every wavelength at it, per unit length
        public bool Sample(Float u, out Float t, out SampledSpectrum density)
        {
            t = 0;
            density = new SampledSpectrum(0.0f);
            double target = u * total[0];
            double accumulated = 0;
            int i = 0;
            while (i < count - 1 && accumulated + mass[i * NSpectrumSamples] <= target)
            {
                accumulated += mass[i * NSpectrumSamples];
                ++i;
            }
            double s = sigma[i * NSpectrumSamples];
            double length = end[i] - start[i];
            if (!(s > 0) || !(mass[i * NSpectrumSamples] > 0))
            {
                return false;
            }
            // The opacity of the piece covered up to the point, as a fraction of the piece's own
            double y = Math.Min(Math.Max(target - accumulated, 0.0) * Math.Exp(tauStart[i * NSpectrumSamples]), -ExpM1(-s * length));
            double distance = Math.Min(-double.LogP1(-y) / s, length);
            double point = start[i] + distance;
            t = (Float)point;
            density = Density(i, point);
            return density[0] > 0 && !Float.IsInfinity(density[0]);
        }

        // The density of every wavelength at a distance; zero outside the lit pieces
        public SampledSpectrum Density(Float t)
        {
            for (int i = 0; i < count; ++i)
            {
                if (t >= start[i] && t <= end[i])
                {
                    return Density(i, t);
                }
            }
            return new SampledSpectrum(0.0f);
        }

        SampledSpectrum Density(int i, double t)
        {
            SampledSpectrum density = new SampledSpectrum(0.0f);
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                int index = i * NSpectrumSamples + c;
                if (total[c] > 0)
                {
                    density[c] = (Float)(sigma[index] * Math.Exp(-(tauStart[index] + sigma[index] * (t - start[i]))) / total[c]);
                }
            }
            return density;
        }

        // exp(x) - 1 without the cancellation of small x
        static double ExpM1(double x)
        {
            if (Math.Abs(x) < 1e-5)
            {
                return x + 0.5 * x * x + x * x * x / 6;
            }
            return Math.Exp(x) - 1;
        }
    }

}  // namespace pbrt
