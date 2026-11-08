// (not in pbrt-v4) shapes_cylinder_sampling.cs
//
// Solid angle sampling of a whole (open, 360 degree) Cylinder, seen from a point outside of its infinite cylinder or inside of it (a point above or below the tube, or within it).
//
// The directions from the point o that hit the cylinder are described in meridian coordinates: the azimuth phi of the horizontal part of the direction about the
// line through o parallel to the axis, and h, the sine of the elevation (the solid angle element is dphi dh). In the half plane of a meridian phi the line meets the
// circle of the cylinder at the horizontal distances rhoNear < rhoFar (the tangent meridians have rhoNear = rhoFar), and the directions that hit the wall are those
// whose h lies in the interval of the near wall, [h(rhoNear, Z0), h(rhoNear, Z1)], or of the far wall (seen through the open ends), [h(rhoFar, Z0), h(rhoFar, Z1)],
// with h(rho, Z) = Z / sqrt(rho^2 + Z^2) and Z0, Z1 the heights of the ends relative to o. The solid angle is the integral over phi of the length of the union of the
// two intervals.
//
// For a point inside of the infinite cylinder (|o_xy| < R) every meridian meets the wall once, at rhoFar = -b + sqrt(b^2 + R^2 - |o_xy|^2) (the other root is behind the point), the
// directions are the single interval of that wall, and the integrand is smooth and periodic in phi itself, which is tabulated in place of psi.
//
// The meridians that meet the cylinder (outside) are those within the angle alpha = asin(R / |o_xy|) of the direction to the axis. With sin(phi - phi0) = sin(alpha) sin(psi) the
// square root of the discriminant becomes R cos(psi): the integrand has no square root singularity at the tangent meridians (psi = +-pi/2), which a quadrature (or a
// table) in phi would have. The integrand in psi, G(psi) = (union length) * dphi/dpsi, is piecewise smooth (it has kinks where the two intervals start to overlap)
// and is tabulated at CellCount + 1 values of psi, per reference point. A sample picks psi from the piecewise linear density through the table (exactly, by solving
// the quadratic of a cell), then h uniformly in the union at that meridian. The density in solid angle is
//
//     pdf = G~(psi) / (Z~ * G(psi))
//
// where G~ is the interpolated and G the exact integrand and Z~ the integral of G~: it is 1 / Omega up to the interpolation error of the table (a few per cent
// with the kinks), it is computed exactly, so the estimator is unbiased, and PDF(wi) evaluates it for any direction without a ray test.
//
// Projected solid angle. When the shading normal is (nearly) parallel to the axis of the cylinder, the cosine of a direction is |h| (up to the sine of the tilt, tiltSine), so the
// projected solid angle element is |h| dphi dh, and in a meridian the integral of the weight w(h) = |h| + tiltSine over the union has a closed form (the primitive
// h |h| / 2 + tiltSine h), which is also what Prata (2003, math-ph/0302003) integrates over phi in closed form for the cosine weighted solid angle of a cylinder.
// The same table of the integrand W(psi) dphi/dpsi is used with the weight, h is chosen in the union with the density w(h) / W by inverting the primitive, and the density
// is G~(psi) w(h) / (Z~ G(psi)): about |cos| / (the projected solid angle), which cancels the cosine of a diffuse receiver. With a tilt the weight |h| + tiltSine bounds the
// cosine of the actual normal, so the density is positive wherever the integrand is, and the estimator stays unbiased.

using System;

namespace pbrt
{
    public struct CylinderSolidAngleSampler
    {
        const int CellCount = 64;
        // Points closer to the wall of the infinite cylinder than this fraction of the radius are sampled by area
        const double WallMargin = 1e-6;

        double ox, oy, oz;
        double radius, zMin, zMax;
        double rho0, phi0, sinAlpha;
        double z0, z1;
        double cellWidth;
        double total;
        // The weight of h in the union: 1 (solid angle), or |h| + tiltSine (projected solid angle for a normal near the axis)
        bool projected;
        double tilt;
        // The point is inside of the infinite cylinder: the table is over the azimuth phi in [0, 2 pi) and each meridian has the interval of the far wall only
        bool surrounded;
        double parameterStart;
        double[] density;
        double[] cumulative;

        // The reference point (ox, oy, oz) and the cylinder of the given radius and ends in the object space. False if the point is within 0.1 percent of the radius of the
        // wall of the infinite cylinder (the cylinder is then sampled by area).
        public bool Prepare(double px, double py, double pz, double cylinderRadius, double cylinderZMin, double cylinderZMax, bool projectedSolidAngle = false,
                            double tiltSine = 0.0)
        {
            this = default(CylinderSolidAngleSampler);
            projected = projectedSolidAngle;
            tilt = tiltSine;
            ox = px;
            oy = py;
            oz = pz;
            radius = cylinderRadius;
            zMin = cylinderZMin;
            zMax = cylinderZMax;
            if (!(radius > 0) || !(zMax > zMin))
            {
                return false;
            }
            rho0 = Math.Sqrt(ox * ox + oy * oy);
            if (rho0 > radius * (1.0 + WallMargin))
            {
                surrounded = false;
            }
            else if (rho0 < radius * (1.0 - WallMargin))
            {
                surrounded = true;
            }
            else
            {
                return false;
            }
            phi0 = Math.Atan2(-oy, -ox);
            sinAlpha = surrounded ? 1.0 : radius / rho0;
            z0 = zMin - oz;
            z1 = zMax - oz;
            double span = surrounded ? 2.0 * Math.PI : Math.PI;
            parameterStart = surrounded ? 0.0 : -0.5 * Math.PI;
            cellWidth = span / CellCount;
            density = new double[CellCount + 1];
            cumulative = new double[CellCount + 1];
            for (int i = 0; i <= CellCount; ++i)
            {
                double parameter = parameterStart + i * cellWidth;
                double phi, derivative, measure;
                Evaluate(parameter, out phi, out derivative, out measure);
                // outside: the integrand vanishes at the tangent meridians
                density[i] = !surrounded && (i == 0 || i == CellCount) ? 0.0 : measure * derivative;
                if (i > 0)
                {
                    cumulative[i] = cumulative[i - 1] + 0.5 * cellWidth * (density[i - 1] + density[i]);
                }
            }
            total = cumulative[CellCount];
            return total > 0 && !double.IsNaN(total) && !double.IsInfinity(total);
        }

        // The approximate solid angle, or projected solid angle if projected (the integral of the table)
        public double ApproximateSolidAngle
        {
            get { return total; }
        }

        // The union of two intervals of h, as at most two disjoint intervals; returns the count and the total length
        static int Union(double lo1, double hi1, double lo2, double hi2, out double aLo, out double aHi, out double bLo, out double bHi)
        {
            if (lo2 < lo1)
            {
                double t = lo1;
                lo1 = lo2;
                lo2 = t;
                t = hi1;
                hi1 = hi2;
                hi2 = t;
            }
            if (lo2 <= hi1)
            {
                aLo = lo1;
                aHi = Math.Max(hi1, hi2);
                bLo = 0;
                bHi = 0;
                return 1;
            }
            aLo = lo1;
            aHi = hi1;
            bLo = lo2;
            bHi = hi2;
            return 2;
        }

        static double HeightOf(double rho, double z)
        {
            return z / Math.Sqrt(rho * rho + z * z);
        }

        // The meridian of the table parameter (psi outside, the azimuth itself inside): its azimuth, dphi/dparameter, the near and far distances (the near one is not used inside)
        void Meridian(double parameter, out double phi, out double derivative, out double rhoNear, out double rhoFar)
        {
            if (surrounded)
            {
                phi = parameter;
                derivative = 1.0;
                double b = ox * Math.Cos(phi) + oy * Math.Sin(phi);
                // the root behind the point is -b - sqrt(D) < 0; this one is positive, and stable as b^2 + R^2 - rho0^2 > 0
                double discriminant = b * b + radius * radius - rho0 * rho0;
                rhoFar = discriminant > 0 ? -b + Math.Sqrt(discriminant) : 0.0;
                rhoNear = 0.0;
                return;
            }
            double s = sinAlpha * Math.Sin(parameter);
            double cosDelta = Math.Sqrt(Math.Max(0.0, 1.0 - s * s));
            phi = phi0 + Math.Asin(s);
            double cosPsi = Math.Cos(parameter);
            derivative = sinAlpha * cosPsi / cosDelta;
            rhoFar = rho0 * cosDelta + radius * cosPsi;
            // rhoNear * rhoFar = rho0^2 - R^2: no cancellation near the tangent meridians
            rhoNear = (rho0 * rho0 - radius * radius) / rhoFar;
        }

        // The intervals of h of the walls at the meridian: the near wall's, and the union with the far wall's (inside there is the far wall only)
        int WallIntervals(double rhoNear, double rhoFar, out double nearLo, out double nearHi, out double aLo, out double aHi, out double bLo, out double bHi)
        {
            if (surrounded)
            {
                nearLo = 1.0;
                nearHi = 0.0;
                aLo = HeightOf(rhoFar, z0);
                aHi = HeightOf(rhoFar, z1);
                bLo = 0.0;
                bHi = 0.0;
                return 1;
            }
            nearLo = HeightOf(rhoNear, z0);
            nearHi = HeightOf(rhoNear, z1);
            return Union(nearLo, nearHi, HeightOf(rhoFar, z0), HeightOf(rhoFar, z1), out aLo, out aHi, out bLo, out bHi);
        }

        // The weight of the direction with the height h in the measure that is sampled
        double Weight(double h)
        {
            return projected ? Math.Abs(h) + tilt : 1.0;
        }

        // The primitive of the weight: the measure of [lo, hi] is Primitive(hi) - Primitive(lo)
        double Primitive(double h)
        {
            return projected ? 0.5 * h * Math.Abs(h) + tilt * h : h;
        }

        // The inverse of the primitive
        double InversePrimitive(double y)
        {
            if (!projected)
            {
                return y;
            }
            if (y >= 0)
            {
                return tilt > 0 ? -tilt + Math.Sqrt(tilt * tilt + 2.0 * y) : Math.Sqrt(2.0 * y);
            }
            return tilt > 0 ? tilt - Math.Sqrt(tilt * tilt - 2.0 * y) : -Math.Sqrt(-2.0 * y);
        }

        double Measure(int count, double aLo, double aHi, double bLo, double bHi)
        {
            double measure = Primitive(aHi) - Primitive(aLo);
            if (count == 2)
            {
                measure += Primitive(bHi) - Primitive(bLo);
            }
            return measure;
        }

        // The measure of the union of the intervals of h at the meridian psi (its length, or the integral of the weight), and the meridian
        void Evaluate(double parameter, out double phi, out double derivative, out double measure)
        {
            double rhoNear, rhoFar;
            Meridian(parameter, out phi, out derivative, out rhoNear, out rhoFar);
            double nearLo, nearHi, aLo, aHi, bLo, bHi;
            int count = WallIntervals(rhoNear, rhoFar, out nearLo, out nearHi, out aLo, out aHi, out bLo, out bHi);
            measure = Measure(count, aLo, aHi, bLo, bHi);
        }

        // The sample for (e0, e1): the point on the cylinder (object space), and the density in solid angle from the reference point
        public void Sample(double e0, double e1, out double x, out double y, out double z, out double pdf)
        {
            // The cell, and psi in it, by the linear density
            double target = e0 * total;
            int cell = 0;
            while (cell < CellCount - 1 && cumulative[cell + 1] < target)
            {
                ++cell;
            }
            double g0 = density[cell], g1 = density[cell + 1];
            double slope = g1 - g0;
            double remaining = Math.Max(0.0, target - cumulative[cell]);
            double discriminant = Math.Max(0.0, g0 * g0 + 2.0 * slope * remaining / cellWidth);
            double denominator = g0 + Math.Sqrt(discriminant);
            double offset = denominator > 0 ? 2.0 * remaining / denominator : 0.0;
            offset = Math.Min(cellWidth, Math.Max(0.0, offset));
            double psi = parameterStart + cell * cellWidth + offset;

            double phi, derivative, rhoNear, rhoFar;
            Meridian(psi, out phi, out derivative, out rhoNear, out rhoFar);
            double nearLo, nearHi, aLo, aHi, bLo, bHi;
            int count = WallIntervals(rhoNear, rhoFar, out nearLo, out nearHi, out aLo, out aHi, out bLo, out bHi);
            double measure = Measure(count, aLo, aHi, bLo, bHi);

            // h in the union with the density of the weight
            double t = e1 * measure;
            double firstMeasure = Primitive(aHi) - Primitive(aLo);
            double h;
            if (count == 1 || t < firstMeasure)
            {
                h = InversePrimitive(Primitive(aLo) + t);
            }
            else
            {
                h = InversePrimitive(Primitive(bLo) + (t - firstMeasure));
            }
            h = Math.Min(Math.Max(h, -1.0 + 1e-15), 1.0 - 1e-15);

            // The wall that the direction meets first: the near one if h is in its interval
            bool nearWall = h >= nearLo && h <= nearHi;
            double rho = nearWall ? rhoNear : rhoFar;
            double horizontal = Math.Sqrt(1.0 - h * h);
            double dz = rho * h / horizontal;
            double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);
            x = ox + rho * cosPhi;
            y = oy + rho * sinPhi;
            z = Math.Min(zMax, Math.Max(zMin, oz + dz));
            double hitRadius = Math.Sqrt(x * x + y * y);
            x *= radius / hitRadius;
            y *= radius / hitRadius;

            double interpolated = g0 + slope * offset / cellWidth;
            double exact = measure * derivative;
            pdf = exact > 0 ? interpolated * Weight(h) / (total * exact) : 0.0;
        }

        // The density in solid angle of the direction (dx, dy, dz) (unit, object space) from the reference point; zero for the directions that miss the cylinder
        public double Pdf(double dx, double dy, double dz)
        {
            double horizontal2 = dx * dx + dy * dy;
            if (!(horizontal2 > 0))
            {
                return 0.0;
            }
            double phi = Math.Atan2(dy, dx);
            double parameter;
            if (surrounded)
            {
                parameter = phi - 2.0 * Math.PI * Math.Floor(phi / (2.0 * Math.PI));
            }
            else
            {
                double delta = phi - phi0;
                delta -= 2.0 * Math.PI * Math.Floor((delta + Math.PI) / (2.0 * Math.PI));
                if (Math.Abs(delta) >= 0.5 * Math.PI)
                {
                    return 0.0;
                }
                double sinPsi = Math.Sin(delta) / sinAlpha;
                if (Math.Abs(sinPsi) > 1.0)
                {
                    return 0.0;
                }
                parameter = Math.Asin(sinPsi);
            }
            double meridianPhi, derivative, rhoNear, rhoFar;
            Meridian(parameter, out meridianPhi, out derivative, out rhoNear, out rhoFar);
            double h = dz;
            double nearLo, nearHi, aLo, aHi, bLo, bHi;
            int count = WallIntervals(rhoNear, rhoFar, out nearLo, out nearHi, out aLo, out aHi, out bLo, out bHi);
            bool hits = (h >= aLo && h <= aHi) || (count == 2 && h >= bLo && h <= bHi);
            if (!hits)
            {
                return 0.0;
            }
            double exact = Measure(count, aLo, aHi, bLo, bHi) * derivative;
            if (!(exact > 0))
            {
                return 0.0;
            }
            double position = (parameter - parameterStart) / cellWidth;
            int cell = Math.Min(CellCount - 1, Math.Max(0, (int)position));
            double fraction = position - cell;
            double interpolated = density[cell] + (density[cell + 1] - density[cell]) * fraction;
            return interpolated * Weight(h) / (total * exact);
        }
    }
}
