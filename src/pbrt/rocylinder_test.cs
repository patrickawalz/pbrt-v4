// (not in pbrt-v4) rocylinder_test.cs
//
// Tests of the swept disks (rocap.cs, RocapSegment.Disks): the hits against fine meshes of the tube just inside and just
// outside the surface, against the cylinder and the cone, and at low angles to the strand.

using System;
using testing;

namespace pbrt
{
    public static unsafe class RocylinderTests
    {
        // RocylinderMesh Definition: the rings of the circles of the disks, with the disks of the two ends, as triangles
        private sealed class RocylinderMesh
        {
            private readonly SdfVector[] corners;
            private readonly int rings, sides;
            private readonly SdfVector startCenter, endCenter;

            public RocylinderMesh(RocapSegment segment, int rings, int sides, double scale)
            {
                this.rings = rings;
                this.sides = sides;
                corners = new SdfVector[(rings + 1) * sides];
                SdfVector across = new SdfVector(0.3, 0.5, 0.8);
                for (int i = 0; i <= rings; ++i)
                {
                    double u = (double)i / rings;
                    SdfVector along = segment.Velocity(u).Normalized();
                    across = (across - along * SdfVector.Dot(across, along)).Normalized();
                    SdfVector other = SdfVector.Cross(along, across);
                    SdfVector center = segment.Position(u);
                    double r = segment.Radius(u) * scale;
                    for (int j = 0; j < sides; ++j)
                    {
                        double angle = 2 * Math.PI * j / sides;
                        corners[i * sides + j] = center + across * (r * Math.Cos(angle)) + other * (r * Math.Sin(angle));
                    }
                }
                startCenter = segment.Position(0);
                endCenter = segment.Position(1);
            }

            private static void Triangle(SdfVector o, SdfVector d, SdfVector a, SdfVector b, SdfVector c, ref double best)
            {
                SdfVector e1 = b - a, e2 = c - a;
                SdfVector p = SdfVector.Cross(d, e2);
                double det = SdfVector.Dot(e1, p);
                if (det == 0)
                {
                    return;
                }
                SdfVector s = o - a;
                double x = SdfVector.Dot(s, p) / det;
                if (x < 0 || x > 1)
                {
                    return;
                }
                SdfVector q = SdfVector.Cross(s, e1);
                double y = SdfVector.Dot(d, q) / det;
                if (y < 0 || x + y > 1)
                {
                    return;
                }
                double t = SdfVector.Dot(e2, q) / det;
                if (t > 0 && t < best)
                {
                    best = t;
                }
            }

            public double Intersect(SdfVector o, SdfVector d)
            {
                double best = double.MaxValue;
                for (int i = 0; i < rings; ++i)
                {
                    for (int j = 0; j < sides; ++j)
                    {
                        int k = (j + 1) % sides;
                        SdfVector a = corners[i * sides + j], b = corners[i * sides + k], c = corners[(i + 1) * sides + j], e = corners[(i + 1) * sides + k];
                        Triangle(o, d, a, b, c, ref best);
                        Triangle(o, d, b, e, c, ref best);
                    }
                }
                for (int j = 0; j < sides; ++j)
                {
                    int k = (j + 1) % sides;
                    Triangle(o, d, startCenter, corners[j], corners[k], ref best);
                    Triangle(o, d, endCenter, corners[rings * sides + j], corners[rings * sides + k], ref best);
                }
                return best;
            }
        }

        private static RocapSegment Hook(double r0, double r1, double r2, double r3)
        {
            SdfVector[] p =
            {
                new SdfVector(-1, 0, 0), new SdfVector(-0.6, 2.2, 0.4), new SdfVector(0.8, -1.8, -0.5), new SdfVector(1.1, 0.3, 0.2)
            };
            return new RocapSegment(p, new double[] { r0, r1, r2, r3 }, 0, 1) { Disks = true, FlatCaps = true, Polynomial = mode > 0 };
        }

        // The intersector under test: 0 the search between samples, 1 the polynomial
        private static int mode;

        private static string Mode()
        {
            return mode == 0 ? "search" : "polynomial";
        }

        // The hit is between the mesh just inside the surface and the mesh just outside it, on the circle of its u, with the
        // normal of the surface there (across the two tangents of the surface)
        private static void Check(RocapSegment segment, SdfVector origin, SdfVector dir, RocylinderMesh inside, RocylinderMesh outside, double size, ref int total,
                                  ref int missed, ref int extra, ref double worstT, ref double worstSurface, ref double worstNormal)
        {
            double inner = inside.Intersect(origin, dir), outer = outside.Intersect(origin, dir);
            bool hit = segment.Intersect(origin, dir, 0, 1e30, out double t, out double u, out SdfVector normal);
            if (inner < double.MaxValue)
            {
                ++total;
                if (!hit)
                {
                    ++missed;
                    return;
                }
            }
            if (!hit)
            {
                return;
            }
            if (outer == double.MaxValue)
            {
                ++extra;
                return;
            }
            if (inner == double.MaxValue)
            {
                // (between the two meshes, at the outline)
                return;
            }
            worstT = Math.Max(worstT, Math.Max(outer - t, t - inner) / size);
            SdfVector radial = origin + dir * t - segment.Position(u);
            SdfVector along = segment.Velocity(u).Normalized();
            if (u > 0 && u < 1)
            {
                worstSurface = Math.Max(worstSurface, Math.Max(Math.Abs(radial.Length() - segment.Radius(u)), Math.Abs(SdfVector.Dot(radial, along))) / size);
                // The tangents of the surface c(u) + r(u) (cos N + sin B): around the circle, and along the strand by differences
                SdfVector around = SdfVector.Cross(along, radial).Normalized();
                worstNormal = Math.Max(worstNormal, Math.Abs(SdfVector.Dot(normal, around)));
                CHECK_GT(SdfVector.Dot(normal, radial), 0);
                CHECK_LT(Math.Abs(normal.Length() - 1), 1e-9);
            }
        }

        private static void Compare(RocapSegment segment, int rays, out int total, out int missed, out int extra, out double worstT, out double worstSurface,
                                    out double worstNormal)
        {
            const int sides = 96;
            double chord = Math.Cos(Math.PI / sides);
            RocylinderMesh inside = new RocylinderMesh(segment, 1500, sides, chord * (1 - 1e-4)), outside = new RocylinderMesh(segment, 1500, sides, (1 + 1e-4) / chord);
            Random random = new Random(11);
            segment.Bounds(out SdfVector low, out SdfVector high);
            SdfVector center = 0.5 * (low + high);
            double size = (high - low).Length();
            total = missed = extra = 0;
            worstT = worstSurface = worstNormal = 0;
            for (int k = 0; k < rays; ++k)
            {
                SdfVector dir = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
                SdfVector target = center + new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5) * size * 0.7;
                if (k % 2 == 1)
                {
                    // (at the strand)
                    double u = random.NextDouble();
                    target = segment.Position(u) + new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5) * (2.4 * segment.Radius(u));
                }
                SdfVector origin = target - dir * (3 * size);
                Check(segment, origin, dir, inside, outside, size, ref total, ref missed, ref extra, ref worstT, ref worstSurface, ref worstNormal);
            }
        }

        // Rays at a low angle to the strand (1 to 10 degrees), and rays across it within a degree of the planes of its disks
        private static void Graze(RocapSegment segment, int rays, bool across, out int total, out int missed, out int extra, out double worstT, out double worstSurface)
        {
            const int sides = 96;
            double chord = Math.Cos(Math.PI / sides);
            RocylinderMesh inside = new RocylinderMesh(segment, 1500, sides, chord * (1 - 1e-4)), outside = new RocylinderMesh(segment, 1500, sides, (1 + 1e-4) / chord);
            Random random = new Random(23);
            segment.Bounds(out SdfVector low, out SdfVector high);
            double size = (high - low).Length();
            total = missed = extra = 0;
            worstT = worstSurface = 0;
            double worstNormal = 0;
            for (int k = 0; k < rays; ++k)
            {
                double u = 0.02 + 0.96 * random.NextDouble();
                SdfVector along = segment.Velocity(u).Normalized();
                SdfVector side = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5);
                side = (side - along * SdfVector.Dot(side, along)).Normalized();
                SdfVector target = segment.Position(u) + side * (segment.Radius(u) * (0.7 + 0.4 * random.NextDouble()));
                SdfVector turn = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5);
                turn = (turn - along * SdfVector.Dot(turn, along)).Normalized();
                SdfVector dir;
                if (across)
                {
                    double tilt = (2 * random.NextDouble() - 1) * Math.PI / 180;
                    dir = (turn * Math.Cos(tilt) + along * Math.Sin(tilt)).Normalized();
                }
                else
                {
                    double tilt = (1 + 9 * random.NextDouble()) * Math.PI / 180 * (random.Next(2) == 0 ? 1 : -1);
                    dir = (along * Math.Cos(tilt) + turn * Math.Sin(tilt)).Normalized();
                }
                if (random.Next(2) == 0)
                {
                    dir = -dir;
                }
                SdfVector origin = target - dir * (2 * size);
                Check(segment, origin, dir, inside, outside, size, ref total, ref missed, ref extra, ref worstT, ref worstSurface, ref worstNormal);
            }
        }

        [TEST("Rocylinder", "MatchesTheMesh")]
        public static void MatchesTheMesh()
        {
            for (mode = 0; mode < 2; ++mode)
            for (int which = 0; which < 4; ++which)
            {
                RocapSegment segment = which == 0 ? Hook(0.12, 0.12, 0.12, 0.12) : which == 1 ? Hook(0.3, 0.2, 0.06, 0.02) : which == 2 ? Hook(0.01, 0.02, 0.01, 0.015) :
                                       Hook(0.02, 0.3, 0.02, 0.2);
                Compare(segment, 1200, out int total, out int missed, out int extra, out double worstT, out double worstSurface, out double worstNormal);
                Console.WriteLine("rocylinder (" + Mode() + ") " + (which == 0 ? "constant" : which == 1 ? "tapered" : which == 2 ? "thin" : "swelling") + ": hits " + total + " missed " + missed +
                                  " extra " + extra + " worst t " + worstT + " surface " + worstSurface + " normal " + worstNormal);
                CHECK_GT(total, 100);
                CHECK_EQ(missed, 0);
                CHECK_EQ(extra, 0);
                CHECK_LT(worstT, 1e-5);
                CHECK_LT(worstSurface, 1e-9);
                CHECK_LT(worstNormal, 1e-9);
            }
        }

        [TEST("Rocylinder", "GrazingAndCrossingRaysMatchTheMesh")]
        public static void GrazingAndCrossingRaysMatchTheMesh()
        {
            for (mode = 0; mode < 2; ++mode)
            for (int across = 0; across < 2; ++across)
            {
                for (int which = 0; which < 3; ++which)
                {
                    RocapSegment segment = which == 0 ? Hook(0.12, 0.12, 0.12, 0.12) : which == 1 ? Hook(0.02, 0.03, 0.02, 0.025) : Hook(0.3, 0.2, 0.06, 0.02);
                    Graze(segment, 800, across == 1, out int total, out int missed, out int extra, out double worstT, out double worstSurface);
                    Console.WriteLine("rocylinder (" + Mode() + ") " + (across == 1 ? "rays across" : "grazing rays") + ", " + (which == 0 ? "constant" : which == 1 ? "thin" : "tapered") + ": hits " +
                                      total + " missed " + missed + " extra " + extra + " worst t " + worstT + " surface " + worstSurface);
                    CHECK_GT(total, 400);
                    CHECK_EQ(missed, 0);
                    CHECK_EQ(extra, 0);
                    CHECK_LT(worstT, 1e-5);
                    CHECK_LT(worstSurface, 1e-9);
                }
            }
        }

        // A straight strand with a radius that changes evenly is a cone cut off square: sides of the radius given (the swept
        // spheres give less), flat ends
        [TEST("Rocylinder", "StraightTaperedStrandIsACone")]
        public static void StraightTaperedStrandIsACone()
        {
            SdfVector[] p = { new SdfVector(0, 0, 0), new SdfVector(1, 0, 0), new SdfVector(2, 0, 0), new SdfVector(3, 0, 0) };
            const double r0 = 0.5, r1 = 0.2;
            double third = (r1 - r0) / 3;
            for (mode = 0; mode < 2; ++mode)
            {
            RocapSegment segment = new RocapSegment(p, new double[] { r0, r0 + third, r0 + 2 * third, r1 }, 0, 1) { Disks = true, FlatCaps = true, Polynomial = mode > 0 };
            Random random = new Random(5);
            int hits = 0, wrong = 0;
            double worst = 0, worstNormal = 0;
            for (int k = 0; k < 20000; ++k)
            {
                SdfVector dir = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
                SdfVector target = new SdfVector(-0.5 + 4 * random.NextDouble(), 1.4 * (random.NextDouble() - 0.5), 1.4 * (random.NextDouble() - 0.5));
                SdfVector o = target - dir * 10;
                // The cone |yz| = r0 + (r1 - r0) x / 3 between x = 0 and 3, and its ends
                double slope = (r1 - r0) / 3;
                double a = dir.y * dir.y + dir.z * dir.z - slope * slope * dir.x * dir.x;
                double radiusAtOrigin = r0 + slope * o.x;
                double b = o.y * dir.y + o.z * dir.z - slope * radiusAtOrigin * dir.x;
                double c = o.y * o.y + o.z * o.z - radiusAtOrigin * radiusAtOrigin;
                double expected = double.MaxValue;
                double discriminant = b * b - a * c;
                if (discriminant >= 0 && a != 0)
                {
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        double t = (-b + sign * Math.Sqrt(discriminant)) / a;
                        double x = o.x + dir.x * t;
                        if (t > 0 && x >= 0 && x <= 3 && r0 + slope * x >= 0 && t < expected)
                        {
                            expected = t;
                        }
                    }
                }
                for (int end = 0; end < 2; ++end)
                {
                    double t = (3 * end - o.x) / dir.x;
                    double y = o.y + dir.y * t, z = o.z + dir.z * t, r = end == 0 ? r0 : r1;
                    if (t > 0 && y * y + z * z <= r * r && t < expected)
                    {
                        expected = t;
                    }
                }
                bool hit = segment.Intersect(o, dir, 0, 1e30, out double found, out double u, out SdfVector normal);
                if (hit != (expected < double.MaxValue))
                {
                    ++wrong;
                    continue;
                }
                if (hit)
                {
                    ++hits;
                    worst = Math.Max(worst, Math.Abs(found - expected));
                    SdfVector point = o + dir * found;
                    if (point.x > 1e-6 && point.x < 3 - 1e-6)
                    {
                        // The normal of the cone: outward, and leaning to the thin end by the slope
                        double across = Math.Sqrt(point.y * point.y + point.z * point.z);
                        SdfVector cone = new SdfVector(-slope, point.y / across, point.z / across).Normalized();
                        worstNormal = Math.Max(worstNormal, (cone - normal).Length());
                    }
                }
            }
            Console.WriteLine("rocylinder (" + Mode() + ") cone: hits " + hits + " wrong " + wrong + " worst t " + worst + " normal " + worstNormal);
            CHECK_LT(worstNormal, 1e-8);
            CHECK_GT(hits, 2000);
            CHECK_EQ(wrong, 0);
            CHECK_LT(worst, 1e-8);
            }
        }
    }
}
