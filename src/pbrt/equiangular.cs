// (not in pbrt-v4) equiangular.cs
//
// Equiangular sampling (Kulla and Fajardo 2012; Fong et al., "Production Volume Rendering", section 4.2.2): the point that a
// ray scatters at, chosen so that the angle seen from a point light is uniform over the segment, which has the density of the
// 1/r^2 falloff of the light and cancels its singularity. Nothing about it depends on the wavelength.

using System;

namespace pbrt
{

    // EquiangularSegment Definition: what volpath keeps of the medium segment that the walk is in, for the weights of the two ways
    // to find a scattering point on it. r_u0 is r_u at the start of the segment, segL the connection ratios of the null collisions of the
    // walk up to the vertex (B/A of PathState), and t and sigmaS0 the distance and the hero scattering coefficient of the vertex.
    public sealed class EquiangularSegment
    {
        public Point3f origin;
        public Vector3f direction;
        public Float tMax;
        public LightSampleContext context;
        public SampledSpectrum r_u0;
        public SampledSpectrum segL;
        public Float t;
        public Float sigmaS0;
        public bool active;
    }

    public static class EquiangularSampling
    {
        // The angles at the two ends of the segment, seen from the light, and the distance of the light from the ray line.
        // direction has unit length; the segment is [0, tMax] along it (tMax may be infinite).
        static void Setup(Point3f origin, Vector3f direction, Float tMax, Point3f light, out Float delta, out Float distance,
                          out Float thetaA, out Float thetaB)
        {
            Vector3f toLight = light - origin;
            delta = pbrt.Dot(toLight, direction);
            Vector3f perpendicular = toLight - delta * direction;
            Float a = -delta;
            Float b = tMax - delta;
            // A ray through the light has no distance: the density stays a valid density with a small one (Fong 4.2.2 gives a
            // special form for it that does not cover a ray that goes through the light)
            Float minimum = 1e-4f * (1 + MathF.Abs(a) + (float.IsInfinity(b) ? 0 : MathF.Abs(b)));
            distance = Math.Max(pbrt.Length(perpendicular), minimum);
            thetaA = MathF.Atan(a / distance);
            thetaB = float.IsInfinity(b) ? MathF.PI / 2 : MathF.Atan(b / distance);
        }

        // The distance along the ray and its density per unit length. False if the segment has no extent.
        public static bool Sample(Point3f origin, Vector3f direction, Float tMax, Point3f light, Float u, out Float t, out Float pdf)
        {
            Setup(origin, direction, tMax, light, out Float delta, out Float distance, out Float thetaA, out Float thetaB);
            t = 0;
            pdf = 0;
            if (!(thetaB > thetaA))
                return false;
            Float theta = (1 - u) * thetaA + u * thetaB;
            Float tPrime = distance * MathF.Tan(theta);
            t = tPrime + delta;
            if (!(t >= 0) || !(t <= tMax))
                t = Math.Min(Math.Max(t, 0), tMax);
            pdf = distance / ((thetaB - thetaA) * (distance * distance + (t - delta) * (t - delta)));
            return pdf > 0 && !float.IsInfinity(pdf) && !float.IsNaN(pdf);
        }

        public static Float Pdf(Point3f origin, Vector3f direction, Float tMax, Point3f light, Float t)
        {
            Setup(origin, direction, tMax, light, out Float delta, out Float distance, out Float thetaA, out Float thetaB);
            if (!(thetaB > thetaA))
                return 0;
            return distance / ((thetaB - thetaA) * (distance * distance + (t - delta) * (t - delta)));
        }
    }

}  // namespace pbrt
