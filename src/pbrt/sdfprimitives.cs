// (not in pbrt-v4) sdfprimitives.cs
//
// More primitives of the signed distance fields (sdf.cs), after the list of Inigo Quilez's "distance functions"
// (https://iquilezles.org/articles/distfunctions). All of them are exact distances, inside and outside: most are bodies of
// revolution about y, whose distance is that of their outline in the half plane (the distance from the axis, y), written here
// from the segments and arcs of the outline. The names of the shapes and their parameters follow the article; the solid angle,
// the death star, the capped torus, the link, the box frame, the hexagonal prism and the rhombus follow its formulas, the
// others are worked out from their outlines and faces.

using System;

namespace pbrt
{

    // SdfOutline Definition
    // The pieces that the outlines are made of, in the half plane (q, y) with q the distance from the axis
    public static class SdfOutline
    {
        // The distance from the point (px, py) to the segment from (ax, ay) to (bx, by)
        public static double Segment(double px, double py, double ax, double ay, double bx, double by)
        {
            double ex = bx - ax, ey = by - ay, wx = px - ax, wy = py - ay;
            double squared = ex * ex + ey * ey;
            double t = squared > 0 ? Math.Min(Math.Max((wx * ex + wy * ey) / squared, 0), 1) : 0;
            double dx = wx - ex * t, dy = wy - ey * t;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // The signed distance to the box of half sizes (hx, hy) about the origin
        public static double Box(double px, double py, double hx, double hy)
        {
            double dx = Math.Abs(px) - hx, dy = Math.Abs(py) - hy;
            double ox = Math.Max(dx, 0), oy = Math.Max(dy, 0);
            return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(dx, dy), 0);
        }
    }

    // The twelve edges of a box of the given half sizes, as bars of the thickness e
    public sealed class SdfBoxFrame : SdfExactNode
    {
        public SdfBoxFrame(double hx, double hy, double hz, double thickness)
        {
            halfExtent = new SdfVector(hx, hy, hz);
            this.thickness = thickness;
        }

        private static double Bar(double x, double y, double z)
        {
            double ox = Math.Max(x, 0), oy = Math.Max(y, 0), oz = Math.Max(z, 0);
            return Math.Sqrt(ox * ox + oy * oy + oz * oz) + Math.Min(Math.Max(x, Math.Max(y, z)), 0);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(halfExtent.x + thickness, halfExtent.y + thickness, halfExtent.z + thickness, out lower, out upper, out grows);
        }

        public override double Evaluate(SdfVector p)
        {
            double px = Math.Abs(p.x) - halfExtent.x, py = Math.Abs(p.y) - halfExtent.y, pz = Math.Abs(p.z) - halfExtent.z;
            double qx = Math.Abs(px + thickness) - thickness, qy = Math.Abs(py + thickness) - thickness, qz = Math.Abs(pz + thickness) - thickness;
            return Math.Min(Math.Min(Bar(px, qy, qz), Bar(qx, py, qz)), Bar(qx, qy, pz));
        }

        private readonly SdfVector halfExtent;
        private readonly double thickness;
    }

    // A part of a torus in the plane z = 0: the arc of the ring within the half angle (degrees) of the y axis, with round ends
    public sealed class SdfCappedTorus : SdfExactNode
    {
        public SdfCappedTorus(double halfAngleDegrees, double majorRadius, double minorRadius)
        {
            double angle = halfAngleDegrees * Math.PI / 180;
            sine = Math.Sin(angle);
            cosine = Math.Cos(angle);
            this.majorRadius = majorRadius;
            this.minorRadius = minorRadius;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            // The arc reaches down to the height of its ends, and sideways to them unless it passes the widest point of the ring
            double side = (cosine > 0 ? sine : 1) * majorRadius + minorRadius;
            lower = new SdfVector(-side, majorRadius * cosine - minorRadius, -minorRadius);
            upper = new SdfVector(side, majorRadius + minorRadius, minorRadius);
            grows = true;
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            double x = Math.Abs(p.x);
            double k = cosine * x > sine * p.y ? x * sine + p.y * cosine : Math.Sqrt(x * x + p.y * p.y);
            return Math.Sqrt(Math.Max(x * x + p.y * p.y + p.z * p.z + majorRadius * majorRadius - 2 * majorRadius * k, 0)) - minorRadius;
        }

        private readonly double sine, cosine, majorRadius, minorRadius;
    }

    // A link of a chain in the plane z = 0: two half rings of the radius R joined by straight parts of the half length le
    public sealed class SdfLink : SdfExactNode
    {
        public SdfLink(double halfLength, double majorRadius, double minorRadius)
        {
            this.halfLength = halfLength;
            this.majorRadius = majorRadius;
            this.minorRadius = minorRadius;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double qy = p.y - Math.Min(Math.Max(p.y, -halfLength), halfLength);
            double w = Math.Sqrt(p.x * p.x + qy * qy), ring = w - majorRadius;
            double length = Math.Sqrt(ring * ring + p.z * p.z);
            double scale = w > 0 ? ring / w : 0;
            return new SdfVector(p.x * scale / length, qy * scale / length, p.z / length);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(majorRadius + minorRadius, halfLength + majorRadius + minorRadius, minorRadius, out lower, out upper, out grows);
        }

        public override double Evaluate(SdfVector p)
        {
            double y = Math.Max(Math.Abs(p.y) - halfLength, 0);
            double ring = Math.Sqrt(p.x * p.x + y * y) - majorRadius;
            return Math.Sqrt(ring * ring + p.z * p.z) - minorRadius;
        }

        private readonly double halfLength, majorRadius, minorRadius;
    }

    // A prism along z over a regular hexagon whose sides are at the given distance from the axis
    public sealed class SdfHexPrism : SdfExactNode
    {
        public SdfHexPrism(double apothem, double halfHeight)
        {
            this.apothem = apothem;
            this.halfHeight = halfHeight;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(apothem * 1.1547005383792517, apothem * 1.1547005383792517, halfHeight, out lower, out upper, out grows);
        }

        public override double Evaluate(SdfVector p)
        {
            const double kx = -0.8660254037844386, ky = 0.5, kz = 0.5773502691896258;
            double x = Math.Abs(p.x), y = Math.Abs(p.y), z = Math.Abs(p.z);
            double fold = 2 * Math.Min(kx * x + ky * y, 0);
            x -= fold * kx;
            y -= fold * ky;
            double cx = Math.Min(Math.Max(x, -kz * apothem), kz * apothem);
            double dx = x - cx, dy = y - apothem;
            double outline = Math.Sqrt(dx * dx + dy * dy) * (y - apothem > 0 ? 1 : -1);
            double cap = z - halfHeight;
            double ox = Math.Max(outline, 0), oy = Math.Max(cap, 0);
            return Math.Min(Math.Max(outline, cap), 0) + Math.Sqrt(ox * ox + oy * oy);
        }

        private readonly double apothem, halfHeight;
    }

    // A prism along z over an equilateral triangle with a corner up, whose side is twice the given size
    public sealed class SdfTriPrism : SdfExactNode
    {
        public SdfTriPrism(double size, double halfHeight)
        {
            this.size = size;
            this.halfHeight = halfHeight;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(size, size * 1.1547005383792517, halfHeight, out lower, out upper, out grows);
        }

        public override double Evaluate(SdfVector p)
        {
            const double k = 1.7320508075688772;
            double x = Math.Abs(p.x) - size, y = p.y + size / k;
            if (x + k * y > 0)
            {
                double rx = (x - k * y) / 2, ry = (-k * x - y) / 2;
                x = rx;
                y = ry;
            }
            x -= Math.Min(Math.Max(x, -2 * size), 0);
            double outline = -Math.Sqrt(x * x + y * y) * (y > 0 ? 1 : -1);
            double cap = Math.Abs(p.z) - halfHeight;
            double ox = Math.Max(outline, 0), oy = Math.Max(cap, 0);
            return Math.Min(Math.Max(outline, cap), 0) + Math.Sqrt(ox * ox + oy * oy);
        }

        private readonly double size, halfHeight;
    }

    // A cylinder along y of the given radius and half height whose rims are rounded with the given radius
    public sealed class SdfRoundedCylinder : SdfExactNode
    {
        public SdfRoundedCylinder(double radius, double rounding, double halfHeight)
        {
            this.radius = radius;
            this.rounding = rounding;
            this.halfHeight = halfHeight;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double length = Math.Sqrt(p.x * p.x + p.z * p.z);
            double ex = length - (radius - rounding), ey = Math.Abs(p.y) - (halfHeight - rounding);
            double ux = length > 0 ? p.x / length : 1, uz = length > 0 ? p.z / length : 0, vy = p.y < 0 ? -1 : 1;
            if (Math.Max(ex, ey) <= 0)
            {
                return ex > ey ? new SdfVector(ux, 0, uz) : new SdfVector(0, vy, 0);
            }
            double hx = Math.Max(ex, 0), hy = Math.Max(ey, 0), f = Math.Sqrt(hx * hx + hy * hy);
            return new SdfVector(hx * ux / f, hy * vy / f, hx * uz / f);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(radius, halfHeight, radius, out lower, out upper, out grows);
        }

        public override double Evaluate(SdfVector p)
        {
            double q = Math.Sqrt(p.x * p.x + p.z * p.z);
            return SdfOutline.Box(q, p.y, radius - rounding, halfHeight - rounding) - rounding;
        }

        private readonly double radius, rounding, halfHeight;
    }

    // A cone along y cut at both ends: the radius r1 at y = -halfHeight and r2 at y = halfHeight
    public sealed class SdfCappedCone : SdfExactNode
    {
        public SdfCappedCone(double halfHeight, double r1, double r2)
        {
            this.halfHeight = halfHeight;
            this.r1 = r1;
            this.r2 = r2;
        }

        // The closest point of the segment from (ax, ay) to (bx, by) to (px, py), kept if it is closer than the best so far
        private static void Closest(double px, double py, double ax, double ay, double bx, double by, ref double best,
                                    ref double cx, ref double cy)
        {
            double ex = bx - ax, ey = by - ay, squared = ex * ex + ey * ey;
            double t = squared > 0 ? Math.Min(Math.Max(((px - ax) * ex + (py - ay) * ey) / squared, 0), 1) : 0;
            double x = ax + ex * t, y = ay + ey * t;
            double distance = (px - x) * (px - x) + (py - y) * (py - y);
            if (distance < best)
            {
                best = distance;
                cx = x;
                cy = y;
            }
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            // Away from the closest point of the outline, in the half plane of the point and the axis
            double q = Math.Sqrt(p.x * p.x + p.z * p.z), y = p.y;
            double best = double.PositiveInfinity, cx = 0, cy = 0;
            Closest(q, y, 0, -halfHeight, r1, -halfHeight, ref best, ref cx, ref cy);
            Closest(q, y, r1, -halfHeight, r2, halfHeight, ref best, ref cx, ref cy);
            Closest(q, y, 0, halfHeight, r2, halfHeight, ref best, ref cx, ref cy);
            bool inside = Math.Abs(y) < halfHeight && q < r1 + (r2 - r1) * (y + halfHeight) / (2 * halfHeight);
            double distance = Math.Sqrt(best) * (inside ? -1 : 1);
            if (distance == 0)
            {
                return new SdfVector(0, 1, 0);
            }
            double gq = (q - cx) / distance, gy = (y - cy) / distance;
            return q > 0 ? new SdfVector(gq * p.x / q, gy, gq * p.z / q) : new SdfVector(gq, gy, 0);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(Math.Max(r1, r2), halfHeight, Math.Max(r1, r2), out lower, out upper, out grows);
        }

        public override double Evaluate(SdfVector p)
        {
            double q = Math.Sqrt(p.x * p.x + p.z * p.z), y = p.y;
            double distance = Math.Min(SdfOutline.Segment(q, y, 0, -halfHeight, r1, -halfHeight),
                                       Math.Min(SdfOutline.Segment(q, y, r1, -halfHeight, r2, halfHeight),
                                                SdfOutline.Segment(q, y, 0, halfHeight, r2, halfHeight)));
            bool inside = Math.Abs(y) < halfHeight && q < r1 + (r2 - r1) * (y + halfHeight) / (2 * halfHeight);
            return inside ? -distance : distance;
        }

        private readonly double halfHeight, r1, r2;
    }

    // Two balls, of the radius r1 at the origin and r2 at the height h on y, and the cone that touches both
    public sealed class SdfRoundCone : SdfExactNode
    {
        public SdfRoundCone(double r1, double r2, double height)
        {
            this.r1 = r1;
            this.r2 = r2;
            this.height = height;
            b = (r1 - r2) / height;
            a = Math.Sqrt(Math.Max(1 - b * b, 0));
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double q = Math.Sqrt(p.x * p.x + p.z * p.z), y = p.y;
            double k = -q * b + y * a;
            if (k < 0)
            {
                return p.Normalized();
            }
            if (k > a * height)
            {
                return new SdfVector(p.x, y - height, p.z).Normalized();
            }
            return q > 0 ? new SdfVector(a * p.x / q, b, a * p.z / q) : new SdfVector(a, b, 0);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            lower = new SdfVector(-Math.Max(r1, r2), Math.Min(-r1, height - r2), -Math.Max(r1, r2));
            upper = new SdfVector(Math.Max(r1, r2), Math.Max(r1, height + r2), Math.Max(r1, r2));
            grows = true;
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            double q = Math.Sqrt(p.x * p.x + p.z * p.z), y = p.y;
            double k = -q * b + y * a;
            if (k < 0)
            {
                return Math.Sqrt(q * q + y * y) - r1;
            }
            if (k > a * height)
            {
                return Math.Sqrt(q * q + (y - height) * (y - height)) - r2;
            }
            return q * a + y * b - r1;
        }

        private readonly double r1, r2, height, a, b;
    }

    // The part of a ball of the given radius within the half angle (degrees) of the y axis: a cone with a round cap
    public sealed class SdfSolidAngle : SdfExactNode
    {
        public SdfSolidAngle(double halfAngleDegrees, double radius)
        {
            double angle = halfAngleDegrees * Math.PI / 180;
            sine = Math.Sin(angle);
            cosine = Math.Cos(angle);
            this.radius = radius;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            double side = (cosine > 0 ? sine : 1) * radius;
            lower = new SdfVector(-side, Math.Min(radius * cosine, 0), -side);
            upper = new SdfVector(side, radius, side);
            grows = true;
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            double q = Math.Sqrt(p.x * p.x + p.z * p.z), y = p.y;
            double ball = Math.Sqrt(q * q + y * y) - radius;
            double along = Math.Min(Math.Max(q * sine + y * cosine, 0), radius);
            double dx = q - sine * along, dy = y - cosine * along;
            double side = Math.Sqrt(dx * dx + dy * dy) * (cosine * q - sine * y > 0 ? 1 : -1);
            return Math.Max(ball, side);
        }

        private readonly double sine, cosine, radius;
    }

    // The part of a ball of the radius r above the plane y = h (-r < h < r)
    public sealed class SdfCutSphere : SdfExactNode
    {
        public SdfCutSphere(double radius, double height)
        {
            this.radius = radius;
            this.height = height;
            rim = Math.Sqrt(Math.Max(radius * radius - height * height, 0));
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            double side = height > 0 ? Math.Sqrt(Math.Max(radius * radius - height * height, 0)) : radius;
            lower = new SdfVector(-side, height, -side);
            upper = new SdfVector(side, radius, side);
            grows = true;
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            double q = Math.Sqrt(p.x * p.x + p.z * p.z), y = p.y;
            double length = Math.Sqrt(q * q + y * y);
            double toRim = Math.Sqrt((q - rim) * (q - rim) + (y - height) * (y - height));
            // (the arc: the point of the sphere along the direction of p, if that is above the plane, or the rim)
            double arc = length > 0 ? (radius * y / length >= height ? Math.Abs(length - radius) : toRim) : radius;
            double disk = q <= rim ? Math.Abs(y - height) : toRim;
            double distance = Math.Min(arc, disk);
            return length < radius && y > height ? -distance : distance;
        }

        private readonly double radius, height, rim;
    }

    // A bowl: the part of a sphere of the radius r under the plane y = h, as a shell of the half thickness t with a round rim
    public sealed class SdfCutHollowSphere : SdfExactNode
    {
        public SdfCutHollowSphere(double radius, double height, double thickness)
        {
            this.radius = radius;
            this.height = height;
            this.thickness = thickness;
            rim = Math.Sqrt(Math.Max(radius * radius - height * height, 0));
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            double reach = radius + thickness;
            lower = new SdfVector(-reach, -reach, -reach);
            upper = new SdfVector(reach, Math.Min(height + thickness, reach), reach);
            grows = true;
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            double q = Math.Sqrt(p.x * p.x + p.z * p.z), y = p.y;
            double distance = height * q < rim * y ? Math.Sqrt((q - rim) * (q - rim) + (y - height) * (y - height))
                                                   : Math.Abs(Math.Sqrt(q * q + y * y) - radius);
            return distance - thickness;
        }

        private readonly double radius, height, thickness, rim;
    }

    // A ball of the radius ra at the origin less a ball of the radius rb at the distance d along x
    public sealed class SdfDeathStar : SdfExactNode
    {
        public SdfDeathStar(double ra, double rb, double distance)
        {
            this.ra = ra;
            this.rb = rb;
            this.distance = distance;
            a = (ra * ra - rb * rb + distance * distance) / (2 * distance);
            b = Math.Sqrt(Math.Max(ra * ra - a * a, 0));
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(ra, ra, ra, out lower, out upper, out grows);
        }

        public override double Evaluate(SdfVector p)
        {
            double x = p.x, y = Math.Sqrt(p.y * p.y + p.z * p.z);
            if (x * b - y * a > distance * Math.Max(b - y, 0))
            {
                return Math.Sqrt((x - a) * (x - a) + (y - b) * (y - b));
            }
            return Math.Max(Math.Sqrt(x * x + y * y) - ra, -(Math.Sqrt((x - distance) * (x - distance) + y * y) - rb));
        }

        private readonly double ra, rb, distance, a, b;
    }

    // A prism along y of the half height h over a rhombus with the half diagonals la along x and lb along z, its sides rounded
    public sealed class SdfRhombus : SdfExactNode
    {
        public SdfRhombus(double la, double lb, double halfHeight, double rounding)
        {
            this.la = la;
            this.lb = lb;
            this.halfHeight = halfHeight;
            this.rounding = rounding;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(la + rounding, halfHeight, lb + rounding, out lower, out upper, out grows);
        }

        public override double Evaluate(SdfVector p)
        {
            double x = Math.Abs(p.x), y = Math.Abs(p.y), z = Math.Abs(p.z);
            double f = Math.Min(Math.Max((la * (la - 2 * x) - lb * (lb - 2 * z)) / (la * la + lb * lb), -1), 1);
            double dx = x - 0.5 * la * (1 - f), dz = z - 0.5 * lb * (1 + f);
            double outline = Math.Sqrt(dx * dx + dz * dz) * (x * lb + z * la - la * lb > 0 ? 1 : -1) - rounding;
            double cap = y - halfHeight;
            double ox = Math.Max(outline, 0), oy = Math.Max(cap, 0);
            return Math.Min(Math.Max(outline, cap), 0) + Math.Sqrt(ox * ox + oy * oy);
        }

        private readonly double la, lb, halfHeight, rounding;
    }

    // A pyramid of the given height over the square of side 1 on y = 0: the distance to its four faces and its base, negative
    // inside (the formula of the article was not used: as recalled it is too large under the base)
    public sealed class SdfPyramid : SdfExactNode
    {
        public SdfPyramid(double height)
        {
            this.height = height;
            tip = new SdfVector(0, height, 0);
            // (the slope of a face: inside is under the plane through an edge of the base and the tip)
            double length = Math.Sqrt(height * height + 0.25);
            faceY = 0.5 / length;
            faceX = height / length;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            lower = new SdfVector(-0.5, 0, -0.5);
            upper = new SdfVector(0.5, height, 0.5);
            grows = true;
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            // (by the symmetries, the point is taken to the wedge x >= |z|: its nearest face is the one over the edge x = 0.5)
            double x = Math.Abs(p.x), z = Math.Abs(p.z);
            if (z > x)
            {
                double swap = x;
                x = z;
                z = swap;
            }
            SdfVector q = new SdfVector(x, p.y, z);
            SdfVector a = new SdfVector(0.5, 0, -0.5), b = new SdfVector(0.5, 0, 0.5), c = new SdfVector(-0.5, 0, 0.5),
                      d = new SdfVector(-0.5, 0, -0.5);
            double distance = Math.Min(Math.Min(SdfTriangle.Distance(q, a, b, tip), SdfTriangle.Distance(q, b, c, tip)),
                                       Math.Min(SdfTriangle.Distance(q, a, b, c), SdfTriangle.Distance(q, a, c, d)));
            bool inside = p.y > 0 && faceX * (x - 0.5) + faceY * p.y < 0;
            return inside ? -distance : distance;
        }

        private readonly double height, faceX, faceY;
        private readonly SdfVector tip;
    }

    // A triangle as a plate of the given half thickness with round edges (the distance to the triangle less the thickness)
    public sealed class SdfTriangle : SdfExactNode
    {
        public SdfTriangle(SdfVector a, SdfVector b, SdfVector c, double thickness)
        {
            this.a = a;
            this.b = b;
            this.c = c;
            this.thickness = thickness;
        }

        // The distance from p to the triangle a b c (the closest point by the regions of its corners, edges and face)
        public static double Distance(SdfVector p, SdfVector a, SdfVector b, SdfVector c)
        {
            SdfVector ab = b - a, ac = c - a, ap = p - a;
            double d1 = SdfVector.Dot(ab, ap), d2 = SdfVector.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0)
            {
                return ap.Length();
            }
            SdfVector bp = p - b;
            double d3 = SdfVector.Dot(ab, bp), d4 = SdfVector.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3)
            {
                return bp.Length();
            }
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
            {
                return (ap - ab * (d1 / (d1 - d3))).Length();
            }
            SdfVector cp = p - c;
            double d5 = SdfVector.Dot(ab, cp), d6 = SdfVector.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6)
            {
                return cp.Length();
            }
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            {
                return (ap - ac * (d2 / (d2 - d6))).Length();
            }
            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            {
                double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return (bp - (c - b) * w).Length();
            }
            double denominator = 1 / (va + vb + vc);
            double v = vb * denominator, u = vc * denominator;
            return (ap - ab * v - ac * u).Length();
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            lower = new SdfVector(Math.Min(a.x, Math.Min(b.x, c.x)) - thickness, Math.Min(a.y, Math.Min(b.y, c.y)) - thickness, Math.Min(a.z, Math.Min(b.z, c.z)) - thickness);
            upper = new SdfVector(Math.Max(a.x, Math.Max(b.x, c.x)) + thickness, Math.Max(a.y, Math.Max(b.y, c.y)) + thickness, Math.Max(a.z, Math.Max(b.z, c.z)) + thickness);
            grows = true;
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            return Distance(p, a, b, c) - thickness;
        }

        private readonly SdfVector a, b, c;
        private readonly double thickness;
    }

    // A flat quadrilateral with its corners in order around it, as a plate of the given half thickness with round edges
    public sealed class SdfQuad : SdfExactNode
    {
        public SdfQuad(SdfVector a, SdfVector b, SdfVector c, SdfVector d, double thickness)
        {
            this.a = a;
            this.b = b;
            this.c = c;
            this.d = d;
            this.thickness = thickness;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            lower = new SdfVector(Math.Min(Math.Min(a.x, b.x), Math.Min(c.x, d.x)) - thickness, Math.Min(Math.Min(a.y, b.y), Math.Min(c.y, d.y)) - thickness, Math.Min(Math.Min(a.z, b.z), Math.Min(c.z, d.z)) - thickness);
            upper = new SdfVector(Math.Max(Math.Max(a.x, b.x), Math.Max(c.x, d.x)) + thickness, Math.Max(Math.Max(a.y, b.y), Math.Max(c.y, d.y)) + thickness, Math.Max(Math.Max(a.z, b.z), Math.Max(c.z, d.z)) + thickness);
            grows = true;
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            return Math.Min(SdfTriangle.Distance(p, a, b, c), SdfTriangle.Distance(p, a, c, d)) - thickness;
        }

        private readonly SdfVector a, b, c, d;
        private readonly double thickness;
    }

    // A cylinder along y without ends
    public sealed class SdfInfiniteCylinder : SdfExactNode
    {
        public SdfInfiniteCylinder(double radius)
        {
            this.radius = radius;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double length = Math.Sqrt(p.x * p.x + p.z * p.z);
            return length > 0 ? new SdfVector(p.x / length, 0, p.z / length) : new SdfVector(1, 0, 0);
        }

        public override double Evaluate(SdfVector p)
        {
            return Math.Sqrt(p.x * p.x + p.z * p.z) - radius;
        }

        private readonly double radius;
    }

    // A cone without end: its tip at the origin, opening downward along y with the given half angle (degrees)
    public sealed class SdfInfiniteCone : SdfExactNode
    {
        public SdfInfiniteCone(double halfAngleDegrees)
        {
            double angle = halfAngleDegrees * Math.PI / 180;
            sine = Math.Sin(angle);
            cosine = Math.Cos(angle);
        }

        public override double Evaluate(SdfVector p)
        {
            double q = Math.Sqrt(p.x * p.x + p.z * p.z), y = -p.y;
            double along = Math.Max(q * sine + y * cosine, 0);
            double dx = q - sine * along, dy = y - cosine * along;
            return Math.Sqrt(dx * dx + dy * dy) * (q * cosine - y * sine < 0 ? -1 : 1);
        }

        private readonly double sine, cosine;
    }

    // A change of metric (Quilez, "distance functions": the length8 of his torus82 and torus88): the Euclidean length replaced by
    // (|x|^n + |y|^n + ...)^(1/n), which squares the round shapes off as n grows. For n >= 2 this length is never above the
    // Euclidean one and its gradient is never longer than 1, so the fields are safe bounds of the distance, not exact ones.
    public static class SdfNorm
    {
        public static double Length(double x, double y, double power)
        {
            if (power == 2)
            {
                return Math.Sqrt(x * x + y * y);
            }
            return Math.Pow(Math.Pow(Math.Abs(x), power) + Math.Pow(Math.Abs(y), power), 1 / power);
        }

        public static double Length(double x, double y, double z, double power)
        {
            if (power == 2)
            {
                return Math.Sqrt(x * x + y * y + z * z);
            }
            return Math.Pow(Math.Pow(Math.Abs(x), power) + Math.Pow(Math.Abs(y), power) + Math.Pow(Math.Abs(z), power), 1 / power);
        }
    }

    // A torus about y whose ring is measured with one power and whose tube with another (2 and 8 make a ring with a square tube,
    // 8 and 8 a square ring with a square tube)
    public sealed class SdfNormTorus : SdfNode
    {
        public SdfNormTorus(double majorRadius, double minorRadius, double ringPower, double tubePower)
        {
            this.majorRadius = majorRadius;
            this.minorRadius = minorRadius;
            this.ringPower = ringPower;
            this.tubePower = tubePower;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            SdfExtent.Symmetric(majorRadius + minorRadius, minorRadius, majorRadius + minorRadius, out lower, out upper, out grows);
            grows = false;
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            return SdfNorm.Length(SdfNorm.Length(p.x, p.z, ringPower) - majorRadius, p.y, tubePower) - minorRadius;
        }

        private readonly double majorRadius, minorRadius, ringPower, tubePower;
    }

    // A ball in the changed metric: a cube with round edges, the rounder the smaller the power
    public sealed class SdfNormBall : SdfNode
    {
        public SdfNormBall(double radius, double power)
        {
            this.radius = radius;
            this.power = power;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            SdfExtent.Symmetric(radius, radius, radius, out lower, out upper, out grows);
            grows = false;
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            return SdfNorm.Length(p.x, p.y, p.z, power) - radius;
        }

        private readonly double radius, power;
    }

}  // namespace pbrt
