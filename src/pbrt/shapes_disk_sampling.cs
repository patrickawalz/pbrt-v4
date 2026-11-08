// (not in pbrt-v4) shapes_disk_sampling.cs
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Solid angle sampling of a disk: a C# port of the sampling code of
//
//     Ibon Guillen, Carlos Ureña, Alan King, Marcos Fajardo, Iliyan Georgiev, Jorge Lopez-Moreno, Adrian Jarabo.
//     "Area-Preserving Parameterizations for Spherical Ellipses". Computer Graphics Forum (EGSR) 36(4), 2017.
//     https://github.com/iguillens/mitsuba-disk-sampling (GPL-3.0), arXiv 1805.09048.
//
// from the files src/shapes/disk/DiskSampler.hpp and SphericalEllipseSampler.hpp of that repository (the polar, the concentric and the cylindrical variants;
// the tabulated variants and the rejection sampling variant are not ported). The code in this file is therefore under the GNU General Public License, version 3
// or later, and it is kept apart from the rest of Magi so that it can be separated until the license of Magi is decided.
//
// A disk seen from a point o subtends a spherical ellipse of semi-axes a (along the chord direction x_e, perpendicular to the plane that holds o and the
// axis of the disk) and b (in that plane). A point of the unit sphere around o with the coordinates (x, y, z), z along the axis of the ellipse, is mapped back to the
// disk by the central projection. The quadrant of the ellipse between the angles 0 and phi (about z, from x_e) has the solid angle
//
//     Omega_r(phi) = phi - c Pi(n; t | k),   tan t = tan(phi) a_t / b_t,
//
// (Booth's expression, Pi the elliptic integral of the third kind) which the polar and concentric variants invert by Newton iterations; the cylindrical
// variant inverts Ureña's expression for the part of the ellipse between two parallel chords. Differences to the C++ code:
//  * everything is in double precision, the elliptic integrals are Carlson's symmetric forms (Numerical Recipes, 3rd ed., 6.12) instead of Boost's;
//  * the Newton iterations are safeguarded with a bracket and are iterated to a tolerance relative to the area, not 1e-5 absolute;
//  * the derivative of Omega_r is 1 - h(phi) (h the height of the point of the ellipse boundary at the angle phi), which is exact and cheaper than the
//    derivative written with the elliptic functions of the original;
//  * the thresholds against degenerate configurations are relative to the size of the configuration, not absolute.

using System;

namespace pbrt
{
    // Carlson's symmetric elliptic integrals, and the incomplete Legendre integrals F and Pi of an amplitude in [-pi/2, pi/2] made from them
    public static class CarlsonElliptic
    {
        // RF(x, y, z) = 1/2 integral_0^inf dt / sqrt((t + x)(t + y)(t + z)), x, y, z >= 0 and at most one of them zero
        public static double RF(double x, double y, double z)
        {
            double xt = x, yt = y, zt = z;
            double ave = 0, delx = 0, dely = 0, delz = 0;
            for (int iteration = 0; iteration < 100; ++iteration)
            {
                double sqrtx = Math.Sqrt(xt), sqrty = Math.Sqrt(yt), sqrtz = Math.Sqrt(zt);
                double alamb = sqrtx * (sqrty + sqrtz) + sqrty * sqrtz;
                xt = 0.25 * (xt + alamb);
                yt = 0.25 * (yt + alamb);
                zt = 0.25 * (zt + alamb);
                ave = (xt + yt + zt) / 3.0;
                delx = (ave - xt) / ave;
                dely = (ave - yt) / ave;
                delz = (ave - zt) / ave;
                if (Math.Max(Math.Abs(delx), Math.Max(Math.Abs(dely), Math.Abs(delz))) < 0.0025)
                {
                    break;
                }
            }
            double e2 = delx * dely - delz * delz;
            double e3 = delx * dely * delz;
            return (1.0 + (e2 / 24.0 - 0.1 - 3.0 * e3 / 44.0) * e2 + e3 / 14.0) / Math.Sqrt(ave);
        }

        // RC(x, y) = RF(x, y, y), for y > 0 (the only case that RJ needs here)
        static double RC(double x, double y)
        {
            double xt = x, yt = y, ave = 0, s = 0;
            for (int iteration = 0; iteration < 100; ++iteration)
            {
                double alamb = 2.0 * Math.Sqrt(xt) * Math.Sqrt(yt) + yt;
                xt = 0.25 * (xt + alamb);
                yt = 0.25 * (yt + alamb);
                ave = (xt + yt + yt) / 3.0;
                s = (yt - ave) / ave;
                if (Math.Abs(s) < 0.0012)
                {
                    break;
                }
            }
            return (1.0 + s * s * (0.3 + s * (1.0 / 7.0 + s * (0.375 + s * 9.0 / 22.0)))) / Math.Sqrt(ave);
        }

        // RJ(x, y, z, p) = 3/2 integral_0^inf dt / ((t + p) sqrt((t + x)(t + y)(t + z))), x, y, z >= 0 (at most one zero) and p > 0
        public static double RJ(double x, double y, double z, double p)
        {
            const double C1 = 3.0 / 14.0;
            const double C2 = 1.0 / 3.0;
            const double C3 = 3.0 / 22.0;
            const double C4 = 3.0 / 26.0;
            const double C5 = 0.75 * C3;
            const double C6 = 1.5 * C4;
            const double C7 = 0.5 * C2;
            const double C8 = C3 + C3;
            double sum = 0.0, fac = 1.0;
            double xt = x, yt = y, zt = z, pt = p;
            double ave = 0, delx = 0, dely = 0, delz = 0, delp = 0;
            for (int iteration = 0; iteration < 100; ++iteration)
            {
                double sqrtx = Math.Sqrt(xt), sqrty = Math.Sqrt(yt), sqrtz = Math.Sqrt(zt);
                double alamb = sqrtx * (sqrty + sqrtz) + sqrty * sqrtz;
                double alpha = pt * (sqrtx + sqrty + sqrtz) + sqrtx * sqrty * sqrtz;
                alpha *= alpha;
                double beta = pt * (pt + alamb) * (pt + alamb);
                sum += fac * RC(alpha, beta);
                fac *= 0.25;
                xt = 0.25 * (xt + alamb);
                yt = 0.25 * (yt + alamb);
                zt = 0.25 * (zt + alamb);
                pt = 0.25 * (pt + alamb);
                ave = 0.2 * (xt + yt + zt + pt + pt);
                delx = (ave - xt) / ave;
                dely = (ave - yt) / ave;
                delz = (ave - zt) / ave;
                delp = (ave - pt) / ave;
                if (Math.Max(Math.Max(Math.Abs(delx), Math.Abs(dely)), Math.Max(Math.Abs(delz), Math.Abs(delp))) < 0.0015)
                {
                    break;
                }
            }
            double ea = delx * (dely + delz) + dely * delz;
            double eb = delx * dely * delz;
            double ec = delp * delp;
            double ed = ea - 3.0 * ec;
            double ee = eb + 2.0 * delp * (ea - ec);
            return 3.0 * sum + fac * (1.0 + ed * (-C1 + C5 * ed - C6 * ee) + eb * (C7 + delp * (-C8 + delp * C4)) + delp * ea * (C2 - delp * C3) -
                                      C2 * delp * ec) / (ave * Math.Sqrt(ave));
        }

        // F(phi | k) = integral_0^phi dtheta / sqrt(1 - k^2 sin^2 theta), odd in phi, |phi| <= pi/2
        public static double LegendreF(double phi, double k)
        {
            double s = Math.Sin(phi), c = Math.Cos(phi);
            return s * RF(c * c, 1.0 - k * k * s * s, 1.0);
        }

        // Pi(n; phi | k) = integral_0^phi dtheta / ((1 - n sin^2 theta) sqrt(1 - k^2 sin^2 theta)), odd in phi, |phi| <= pi/2, n sin^2 phi < 1
        public static double LegendrePi(double phi, double n, double k)
        {
            double s = Math.Sin(phi), c = Math.Cos(phi);
            double y = 1.0 - k * k * s * s;
            return s * RF(c * c, y, 1.0) + (n / 3.0) * s * s * s * RJ(c * c, y, 1.0, 1.0 - n * s * s);
        }
    }

    public enum DiskSolidAngleMapping
    {
        Polar,
        Concentric,
        Cylindrical
    }

    // The sampling of the spherical ellipse that a disk subtends from a point, set up by Prepare
    public struct DiskSolidAngleSampler
    {
        const double Eps = 1e-6;

        DiskSolidAngleMapping mapping;
        // The frame of the disk seen from the point: the point, the chord direction, the direction in the plane of the point and the axis, the axis; the distance
        // from the point to the plane of the disk
        double ox, oy, oz;
        double xdx, xdy, xdz;
        double ydx, ydy, ydz;
        double zdx, zdy, zdz;
        double d;
        // The reprojection of the ellipse axis onto the disk plane
        double yhpNorm, dhpNorm;
        // The semi-axes of the spherical ellipse
        double a, b;
        // Booth's constants (the polar and concentric mappings): c, n, k, a_t/b_t, asqrt(1-b^2), bsqrt(1-a^2), the solid angle of a quadrant
        double boothC, boothN, boothK, boothTCoeff, boothPhiCoeff, asqrtb, bsqrta, quadrant;
        // Ureña's constants (the cylindrical mapping): the arcs, the tangent ellipse, the integral constants, the half solid angle
        double beta, tangentA, tangentB, urenaN, urenaK, urenaM, urenaP, urenaC1, halfArea;
        // The solid angle of the ellipse, by the quadrature for every mapping (so that the density 1 / Omega is accurate even where Ureña's closed form is not)
        double totalSolidAngle;

        // The solid angle of the disk seen from the point (the sampling density is its inverse)
        public double SolidAngle
        {
            get { return totalSolidAngle; }
        }

        public double MajorAxis { get { return a; } }

        public double MinorAxis { get { return b; } }

        // o: the point, c: the center of the disk, n: its normal, r: its radius. Returns false if the disk cannot be sampled by this method from o (a point in the
        // plane of the disk, a degenerate or a nearly vanishing ellipse).
        public bool Prepare(DiskSolidAngleMapping mappingType, double px, double py, double pz, double cx, double cy, double cz, double nx, double ny, double nz,
                            double r)
        {
            this = default(DiskSolidAngleSampler);
            mapping = mappingType;
            if (!(r > 0))
            {
                return false;
            }
            double lx = cx - px, ly = cy - py, lz = cz - pz;
            double lengthL = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            // The distance from the point to the plane of the disk, and the axis of the disk frame, pointing from the disk to the point's side
            d = -(lx * nx + ly * ny + lz * nz);
            if (d < 0)
            {
                zdx = nx;
                zdy = ny;
                zdz = nz;
                d = -d;
            }
            else
            {
                zdx = -nx;
                zdy = -ny;
                zdz = -nz;
            }
            if (d < 1e-7 * (lengthL + r))
            {
                return false;
            }

            // The chord direction is perpendicular to the plane of the point and the axis; any perpendicular if the point is on the axis
            xdx = ly * zdz - lz * zdy;
            xdy = lz * zdx - lx * zdz;
            xdz = lx * zdy - ly * zdx;
            double lengthX2 = xdx * xdx + xdy * xdy + xdz * xdz;
            if (lengthX2 < 1e-24 * lengthL * lengthL)
            {
                if (Math.Abs(zdx) > Math.Abs(zdz))
                {
                    double invLength = 1.0 / Math.Sqrt(zdx * zdx + zdy * zdy);
                    xdx = -zdy * invLength;
                    xdy = zdx * invLength;
                    xdz = 0;
                }
                else
                {
                    double invLength = 1.0 / Math.Sqrt(zdy * zdy + zdz * zdz);
                    xdx = 0;
                    xdy = -zdz * invLength;
                    xdz = zdy * invLength;
                }
            }
            else
            {
                double invLength = 1.0 / Math.Sqrt(lengthX2);
                xdx *= invLength;
                xdy *= invLength;
                xdz *= invLength;
            }
            ydx = zdy * xdz - zdz * xdy;
            ydy = zdz * xdx - zdx * xdz;
            ydz = zdx * xdy - zdy * xdx;
            ox = px;
            oy = py;
            oz = pz;

            // The point projected onto the plane of the disk, and its distance to the center
            double opx = px + d * zdx - cx, opy = py + d * zdy - cy, opz = pz + d * zdz - cz;
            double h = Math.Sqrt(opx * opx + opy * opy + opz * opz);

            // The extreme points of the disk in the plane of the point and the axis (y0 nearer, y1 farther), and their projections onto the sphere
            double y0 = h - r;
            double y1 = h + r;
            double invSqrt0 = 1.0 / Math.Sqrt(d * d + y0 * y0);
            double invSqrt1 = 1.0 / Math.Sqrt(d * d + y1 * y1);
            double y0p = y0 * invSqrt0;
            double y1p = y1 * invSqrt1;
            double d0p = d * invSqrt0;
            double d1p = d * invSqrt1;

            // The center of the ellipse, the chord of the disk through its projection, the half chord on the sphere
            double yhp = 0.5 * (y0p + y1p);
            double dhp = 0.5 * (d0p + d1p);
            double yh = d * yhp / dhp;
            double chordSquared = r * r - (h - yh) * (h - yh);
            if (!(chordSquared > 0))
            {
                return false;
            }
            double rp = Math.Sqrt(chordSquared);
            double x1p = rp / Math.Sqrt(rp * rp + yh * yh + d * d);
            a = x1p;
            b = 0.5 * Math.Sqrt((y1p - y0p) * (y1p - y0p) + (d1p - d0p) * (d1p - d0p));
            if (a < Eps || b < Eps || !(a < 1.0) || !(b < 1.0) || b > a * (1.0 + 1e-9))
            {
                return false;
            }
            // a point on the axis has a == b, to rounding
            b = Math.Min(b, a);

            double quadrantByQuadrature = QuadrantSolidAngleByPsi(0.5 * Math.PI);
            totalSolidAngle = 4.0 * quadrantByQuadrature;
            if (mapping == DiskSolidAngleMapping.Cylindrical)
            {
                tangentA = a / Math.Sqrt(1.0 - a * a);
                tangentB = b / Math.Sqrt(1.0 - b * b);
                beta = Math.Asin(b);
                urenaP = 1.0 / (tangentB * tangentB);
                urenaM = (tangentA * tangentA - tangentB * tangentB) / (tangentA * tangentA + 1.0);
                urenaK = Math.Sqrt(urenaM);
                urenaN = -tangentB * tangentB;
                urenaC1 = 2.0 * tangentA / Math.Sqrt(1.0 + tangentA * tangentA);
                halfArea = UrenaIntegral(-0.5 * Math.PI);
                if (!(Math.Abs(2.0 * halfArea - 4.0 * quadrantByQuadrature) <= 1e-6 * 4.0 * quadrantByQuadrature))
                {
                    // the closed form is not accurate for this thin ellipse: the polar mapping, which has the same density, is used
                    mapping = DiskSolidAngleMapping.Polar;
                }
            }
            if (mapping != DiskSolidAngleMapping.Cylindrical)
            {
                boothC = b * (1.0 - a * a) / (a * Math.Sqrt(1.0 - b * b));
                boothK = Math.Sqrt((a * a - b * b) / (1.0 - b * b));
                boothN = (a * a - b * b) / (a * a * (1.0 - b * b));
                quadrant = quadrantByQuadrature;
                asqrtb = a * Math.Sqrt(1.0 - b * b);
                bsqrta = b * Math.Sqrt(1.0 - a * a);
                boothPhiCoeff = bsqrta / asqrtb;
                boothTCoeff = 1.0 / boothPhiCoeff;
            }
            if (!(SolidAngle > 0) || double.IsNaN(SolidAngle) || double.IsInfinity(SolidAngle))
            {
                return false;
            }

            double axisNorm = 1.0 / Math.Sqrt(yhp * yhp + dhp * dhp);
            yhpNorm = yhp * axisNorm;
            dhpNorm = dhp * axisNorm;
            return true;
        }

        // The solid angle of the part of the quadrant of the ellipse swept by the azimuth phi in [0, pi/2] from the major axis: Booth's closed form
        // (phi - c Pi(n; t | k)); inaccurate for a thin small ellipse
        public double QuadrantSolidAngleBooth(double phi)
        {
            double t = Math.Min(0.5 * Math.PI, Math.Max(0.0, Math.Atan(boothTCoeff * Math.Tan(phi))));
            return phi - boothC * CarlsonElliptic.LegendrePi(t, boothN, boothK);
        }

        static readonly double[] QuadratureNodes = new double[16];
        static readonly double[] QuadratureWeights = new double[16];

        static DiskSolidAngleSampler()
        {
            // 16 point Gauss-Legendre rule on [-1, 1]
            int count = QuadratureNodes.Length;
            for (int i = 0; i < count; ++i)
            {
                double x = Math.Cos(Math.PI * (i + 0.75) / (count + 0.5));
                double derivative = 1.0;
                for (int iteration = 0; iteration < 100; ++iteration)
                {
                    double p0 = 1.0, p1 = x;
                    for (int j = 2; j <= count; ++j)
                    {
                        double p2 = ((2 * j - 1) * x * p1 - (j - 1) * p0) / j;
                        p0 = p1;
                        p1 = p2;
                    }
                    derivative = count * (x * p1 - p0) / (x * x - 1.0);
                    double step = p1 / derivative;
                    x -= step;
                    if (Math.Abs(step) < 1e-15)
                    {
                        break;
                    }
                }
                QuadratureNodes[i] = x;
                QuadratureWeights[i] = 2.0 / ((1.0 - x * x) * derivative * derivative);
            }
        }

        // integral_lo^hi dpsi / (1 + h(psi))
        double PanelIntegral(double lo, double hi)
        {
            double sum = 0.0;
            double middle = 0.5 * (lo + hi), half = 0.5 * (hi - lo);
            double oneMinusA2 = 1.0 - a * a, a2MinusB2 = a * a - b * b;
            for (int i = 0; i < QuadratureNodes.Length; ++i)
            {
                double sinPsi = Math.Sin(middle + half * QuadratureNodes[i]);
                double h = Math.Sqrt(oneMinusA2 + a2MinusB2 * sinPsi * sinPsi);
                sum += QuadratureWeights[i] / (1.0 + h);
            }
            return sum * half;
        }

        // The solid angle of the part of the quadrant of the ellipse up to the parametric angle psi (tan psi = (a/b) tan phi), by quadrature
        double QuadrantSolidAngleByPsi(double psi)
        {
            if (!(psi > 0.0))
            {
                return 0.0;
            }
            // panels [psi / 2^(k+1), psi / 2^k], more of them the closer the near singularity (at the distance of about sqrt(1 - a^2) / sqrt(a^2 - b^2)) is to 0
            double distance = Math.Sqrt((1.0 - a * a) / Math.Max(a * a - b * b, 1e-300));
            int panels = distance < 1.0 ? Math.Min(40, (int)Math.Ceiling(Math.Log(1.0 / Math.Max(distance, 1e-12), 2.0)) + 1) : 0;
            double sum = 0.0;
            double hi = psi;
            for (int k = 0; k < panels; ++k)
            {
                double lo = 0.5 * hi;
                sum += PanelIntegral(lo, hi);
                hi = lo;
            }
            sum += PanelIntegral(0.0, hi);
            return a * b * sum;
        }

        // The solid angle of the part of the quadrant of the ellipse swept by the azimuth phi in [0, pi/2] from the major axis
        public double QuadrantSolidAngle(double phi)
        {
            return QuadrantSolidAngleByPsi(Math.Atan2(a * Math.Sin(phi), b * Math.Cos(phi)));
        }

        // The height (z) of the boundary of the spherical ellipse at the azimuth phi
        double BoundaryHeight(double phi)
        {
            double sinPhi = Math.Sin(phi), cosPhi = Math.Cos(phi);
            double radius = a * b / Math.Sqrt(a * a * sinPhi * sinPhi + b * b * cosPhi * cosPhi);
            return Math.Sqrt(Math.Max(0.0, 1.0 - radius * radius));
        }

        // The azimuth in [0, pi/2] that sweeps the fraction u of the solid angle of a quadrant. Safeguarded Newton iterations: Omega_r is increasing, with the
        // derivative 1 - h(phi).
        public double InvertQuadrant(double u, out int iterations)
        {
            double phiMax = u * 0.5 * Math.PI;
            double lo = 0.0, hi = phiMax;
            double phi = Math.Min(phiMax, Math.Max(0.0, Math.Atan(boothPhiCoeff * Math.Tan(phiMax))));
            double target = u * quadrant;
            double tolerance = Math.Max(1e-13 * quadrant, 1e-15);
            iterations = 0;
            for (int iteration = 0; iteration < 60; ++iteration)
            {
                ++iterations;
                double difference = QuadrantSolidAngle(phi) - target;
                if (Math.Abs(difference) <= tolerance || hi - lo <= 1e-15)
                {
                    break;
                }
                if (difference > 0)
                {
                    hi = phi;
                }
                else
                {
                    lo = phi;
                }
                double derivative = 1.0 - BoundaryHeight(phi);
                double next = derivative > 1e-12 ? phi - difference / derivative : -1.0;
                if (next > lo && next < hi && Math.Abs(next - phi) < 1e-11)
                {
                    // converged in the angle; the area cannot be evaluated more accurately than this for a small ellipse
                    phi = next;
                    break;
                }
                phi = (next > lo && next < hi) ? next : 0.5 * (lo + hi);
            }
            return phi;
        }

        // Ureña's integral between the amplitude (negative) ampl and zero, of the half area as a function of the chord
        double UrenaIntegral(double amplitude)
        {
            return urenaC1 * tangentB * (urenaP * CarlsonElliptic.LegendreF(amplitude, urenaK) -
                                         (urenaP + 1.0) * CarlsonElliptic.LegendrePi(amplitude, urenaN, urenaK));
        }

        // The solid angle between the chords at -beta and beta_t (t in [0, 1] is the fraction of the parameter), as a function of t
        double UrenaOmega(double t)
        {
            if (t <= 0.0)
            {
                return 0.0;
            }
            if (t >= 1.0)
            {
                return 2.0 * halfArea;
            }
            if (t == 0.5)
            {
                return halfArea;
            }
            double betaT = Math.Abs(beta * (2.0 * t - 1.0));
            double amplitude = -Math.Asin(Math.Min(1.0, Math.Tan(betaT) / tangentB));
            double integral = UrenaIntegral(amplitude);
            return halfArea + (t > 0.5 ? 1.0 : -1.0) * integral;
        }

        // The chord parameter t in [0, 1] that sweeps the fraction u of the solid angle of the ellipse
        double UrenaInvert(double u, out int iterations)
        {
            double target = 2.0 * halfArea * u;
            double tolerance = Math.Max(1e-13 * 2.0 * halfArea, 1e-15);
            double lo = 0.0, hi = 1.0;
            double t = u;
            iterations = 0;
            for (int iteration = 0; iteration < 80; ++iteration)
            {
                ++iterations;
                double difference = UrenaOmega(t) - target;
                if (Math.Abs(difference) <= tolerance || hi - lo <= 1e-15)
                {
                    break;
                }
                if (difference > 0)
                {
                    hi = t;
                }
                else
                {
                    lo = t;
                }
                double tanBeta = Math.Tan(Math.Abs(beta * (2.0 * t - 1.0)));
                double ratio = (1.0 - urenaP * tanBeta * tanBeta) / (1.0 - urenaM * urenaP * tanBeta * tanBeta);
                double derivative = 2.0 * beta * urenaC1 * Math.Sqrt(Math.Max(0.0, ratio));
                double next = derivative > 1e-12 ? t - difference / derivative : -1.0;
                if (next > lo && next < hi && Math.Abs(next - t) < 1e-11)
                {
                    t = next;
                    break;
                }
                t = (next > lo && next < hi) ? next : 0.5 * (lo + hi);
            }
            return t;
        }

        // The point of the disk for the random numbers (e0, e1) in [0,1)^2, with the density 1 / SolidAngle in solid angle from the point. iterations is the number of
        // iterations of the inversion.
        public void Sample(double e0, double e1, out double x, out double y, out double z, out int iterations)
        {
            double xu, yu, zu;
            iterations = 0;
            if (mapping == DiskSolidAngleMapping.Polar)
            {
                // The continuous variation of the polar mapping, with its single discontinuity: the quadrant of e0, folded to the first
                double u;
                if (e0 >= 0.75)
                {
                    u = 1.0 - 4.0 * (e0 - 0.75);
                }
                else if (e0 >= 0.5)
                {
                    u = 4.0 * (e0 - 0.5);
                }
                else if (e0 >= 0.25)
                {
                    u = 1.0 - 4.0 * (e0 - 0.25);
                }
                else
                {
                    u = 4.0 * e0;
                }
                double phi = InvertQuadrant(u, out iterations);
                if (e0 >= 0.75)
                {
                    phi = 2.0 * Math.PI - phi;
                }
                else if (e0 >= 0.5)
                {
                    phi = Math.PI + phi;
                }
                else if (e0 >= 0.25)
                {
                    phi = Math.PI - phi;
                }
                LocalFromAzimuth(phi, e1, out xu, out yu, out zu);
            }
            else if (mapping == DiskSolidAngleMapping.Concentric)
            {
                // Shirley and Chiu's mapping of the square to the disk, with the angle folded to the first quadrant of the ellipse
                double u = 2.0 * e0 - 1.0;
                double v = 2.0 * e1 - 1.0;
                double radius, theta;
                if (u == 0.0 && v == 0.0)
                {
                    radius = 0.0;
                    theta = 0.0;
                }
                else if (Math.Abs(u) > Math.Abs(v))
                {
                    radius = u;
                    if (u < 0.0)
                    {
                        theta = Math.PI + 0.25 * Math.PI * v / u;
                    }
                    else if (v >= 0.0)
                    {
                        theta = 0.25 * Math.PI * v / u;
                    }
                    else
                    {
                        theta = 2.0 * Math.PI + 0.25 * Math.PI * v / u;
                    }
                }
                else
                {
                    radius = v;
                    if (v >= 0.0)
                    {
                        theta = 0.5 * Math.PI - 0.25 * Math.PI * u / v;
                    }
                    else
                    {
                        theta = 1.5 * Math.PI - 0.25 * Math.PI * u / v;
                    }
                }
                double uFolded;
                if (theta > 1.5 * Math.PI)
                {
                    uFolded = 1.0 - (theta - 1.5 * Math.PI) / (0.5 * Math.PI);
                }
                else if (theta > Math.PI)
                {
                    uFolded = (theta - Math.PI) / (0.5 * Math.PI);
                }
                else if (theta > 0.5 * Math.PI)
                {
                    uFolded = 1.0 - (theta - 0.5 * Math.PI) / (0.5 * Math.PI);
                }
                else
                {
                    uFolded = theta / (0.5 * Math.PI);
                }
                double phi = InvertQuadrant(Math.Min(1.0, Math.Max(0.0, uFolded)), out iterations);
                if (theta > 1.5 * Math.PI)
                {
                    phi = 2.0 * Math.PI - phi;
                }
                else if (theta > Math.PI)
                {
                    phi = Math.PI + phi;
                }
                else if (theta > 0.5 * Math.PI)
                {
                    phi = Math.PI - phi;
                }
                LocalFromAzimuth(phi, radius * radius, out xu, out yu, out zu);
            }
            else
            {
                // Parallel chords: the chord by the area below it, then a point of the chord uniformly in the angle (on the great circle arc)
                double t = UrenaInvert(e0, out iterations);
                double betaT = (2.0 * t - 1.0) * beta;
                double y1 = Math.Tan(betaT);
                double x1 = tangentA * Math.Sqrt(Math.Max(0.0, 1.0 - y1 * y1 / (tangentB * tangentB)));
                double norm = 1.0 / Math.Sqrt(x1 * x1 + y1 * y1 + 1.0);
                double sx = x1 * norm, sy = y1 * norm, sz = norm;
                double w = 2.0 * e1 - 1.0;
                double f = Math.Sqrt(Math.Max(0.0, (1.0 - w * w * sx * sx) / (1.0 - sx * sx)));
                xu = sx * w;
                yu = sy * f;
                zu = sz * f;
            }

            // Back to the plane of the disk: the direction of the sphere in the frame of the ellipse, then the frame of the disk, then the central projection
            double yuP = yu * dhpNorm + zu * yhpNorm;
            double zuP = -yu * yhpNorm + zu * dhpNorm;
            double proportion = d / zuP;
            double lx = xu * proportion, ly = yuP * proportion;
            x = ox + lx * xdx + ly * ydx + d * zdx;
            y = oy + lx * xdy + ly * ydy + d * zdy;
            z = oz + lx * xdz + ly * ydz + d * zdz;
        }

        // The direction of the sphere at the azimuth phi (from the major axis), at the fraction v of the arc from the pole to the boundary of the ellipse
        void LocalFromAzimuth(double phi, double v, out double xu, out double yu, out double zu)
        {
            double sinPhi = Math.Sin(phi), cosPhi = Math.Cos(phi);
            double radius = a * b / Math.Sqrt(a * a * sinPhi * sinPhi + b * b * cosPhi * cosPhi);
            double boundaryHeight = Math.Sqrt(Math.Max(0.0, 1.0 - radius * radius));
            double height = 1.0 - (1.0 - boundaryHeight) * v;
            double sphereRadius = Math.Sqrt(Math.Max(0.0, 1.0 - height * height));
            xu = sphereRadius * cosPhi;
            yu = sphereRadius * sinPhi;
            zu = height;
        }
    }
}
