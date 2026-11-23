// (not in pbrt-v4) ribbon3_test.cs
//
// Tests of the "ribbon3" shape (ribbon3shape.cs): the rays against the triangles of the surface, the normals, and the sampling.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class Ribbon3Tests
    {
        private static BezierSurfaceSettings Settings()
        {
            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = new Transform();
            settings.objectFromRender = new Transform();
            return settings;
        }

        private static List<Ribbon3Curve> One(Ribbon3Curve curve)
        {
            List<Ribbon3Curve> curves = new List<Ribbon3Curve>();
            curves.Add(curve);
            return curves;
        }

        // A curve that bends out of a plane, with a width that changes and a normal that goes round by `twist` degrees
        private static Ribbon3Curve Twisted(double twist, Ribbon3Normals interpolation)
        {
            SdfVector[] points = { new SdfVector(0, 0, 0), new SdfVector(1, 1.2, 0.3), new SdfVector(2, -0.8, 0.6), new SdfVector(3, 0.4, -0.2) };
            double[] widths = { 0.15, 0.3, 0.1, 0.2 };
            double angle = twist * Math.PI / 180;
            return new Ribbon3Curve(points, widths, new SdfVector(0, 0, 1), new SdfVector(0, Math.Sin(angle), Math.Cos(angle)), interpolation, null, null, 0, 1);
        }

        // The distance to the triangle along the ray (Moller and Trumbore), or -1
        private static double Triangle(SdfVector o, SdfVector d, SdfVector a, SdfVector b, SdfVector c)
        {
            SdfVector e1 = b - a, e2 = c - a, p = SdfVector.Cross(d, e2);
            double determinant = SdfVector.Dot(e1, p);
            if (Math.Abs(determinant) < 1e-300)
            {
                return -1;
            }
            SdfVector s = o - a;
            double u = SdfVector.Dot(s, p) / determinant;
            SdfVector q = SdfVector.Cross(s, e1);
            double v = SdfVector.Dot(d, q) / determinant;
            if (u < 0 || v < 0 || u + v > 1)
            {
                return -1;
            }
            double t = SdfVector.Dot(e2, q) / determinant;
            return t > 0 ? t : -1;
        }

        // The rays hit the shape where they hit the surface in fine triangles: for a ribbon that bends, twists by half a turn
        // and changes its width, rays from all sides at points of the surface and beside it
        [TEST("Ribbon3", "RaysHitTheSurface")]
        public static void RaysHitTheSurface()
        {
            for (int roots = 0; roots < 2; ++roots)
            foreach (double twist in new double[] { 0, 60, 170 })
            {
                Ribbon3Curve curve = Twisted(twist, roots == 1 && twist == 60 ? Ribbon3Normals.Linear : Ribbon3Normals.Slerp);
                List<Ribbon3Shape> shapes = Ribbons3.Shapes(Settings(), One(curve), 0, true, roots == 1, false, false, false, 0);
                const int nu = 1200, nv = 24;
                SdfVector[,] grid = new SdfVector[nu + 1, nv + 1];
                for (int i = 0; i <= nu; ++i)
                {
                    for (int j = 0; j <= nv; ++j)
                    {
                        grid[i, j] = curve.Position((double)i / nu, 2.0 * j / nv - 1);
                    }
                }
                Random random = new Random(11);
                int rays = 0, hits = 0, missed = 0, extra = 0;
                double worst = 0, worstOff = 0;
                for (int ray = 0; ray < 3000; ++ray)
                {
                    // At a point of the surface (or up to a tenth beside its edge), from a direction not along it
                    double u = random.NextDouble(), v = (2 * random.NextDouble() - 1) * 1.1;
                    SdfVector target = curve.Position(u, v);
                    curve.Derivatives(u, v, out SdfVector du, out SdfVector dv);
                    SdfVector normal = SdfVector.Cross(du, dv).Normalized();
                    double zeta = 1 - 2 * random.NextDouble(), radius = Math.Sqrt(Math.Max(0, 1 - zeta * zeta)), phi = 2 * Math.PI * random.NextDouble();
                    SdfVector d = new SdfVector(radius * Math.Cos(phi), radius * Math.Sin(phi), zeta);
                    if (Math.Abs(SdfVector.Dot(d, normal)) < 0.05)
                    {
                        continue;
                    }
                    SdfVector o = target - d * 5;
                    double expected = double.MaxValue;
                    for (int i = 0; i < nu; ++i)
                    {
                        // (only the triangles near the ray)
                        SdfVector center = grid[i, nv / 2] - o;
                        if ((center - d * SdfVector.Dot(center, d)).LengthSquared() > 0.25)
                        {
                            continue;
                        }
                        for (int j = 0; j < nv; ++j)
                        {
                            double t1 = Triangle(o, d, grid[i, j], grid[i + 1, j], grid[i + 1, j + 1]);
                            double t2 = Triangle(o, d, grid[i, j], grid[i + 1, j + 1], grid[i, j + 1]);
                            if (t1 > 0)
                            {
                                expected = Math.Min(expected, t1);
                            }
                            if (t2 > 0)
                            {
                                expected = Math.Min(expected, t2);
                            }
                        }
                    }
                    double found = double.MaxValue, offSurface = 0;
                    Ray r = new Ray(new Point3f((Float)o.x, (Float)o.y, (Float)o.z), new Vector3f((Float)d.x, (Float)d.y, (Float)d.z));
                    double length = Math.Sqrt((double)r.d.x * r.d.x + (double)r.d.y * r.d.y + (double)r.d.z * r.d.z);
                    foreach (Ribbon3Shape shape in shapes)
                    {
                        pstd.optional<ShapeIntersection> hit = shape.Intersect(r);
                        if (hit && hit.value().tHit * length < found)
                        {
                            found = hit.value().tHit * length;
                            // The hit against the surface at the parameters that the shape gives for it
                            Point3f at = hit.value().intr.p();
                            Point2f uv = hit.value().intr.uv;
                            SdfVector surface = curve.Position(uv.x, 2 * (double)uv.y - 1);
                            offSurface = (new SdfVector(at.x, at.y, at.z) - surface).Length();
                        }
                    }
                    ++rays;
                    // (a ray within a hundredth of the half width of the edge may fall on either side of it in the triangles)
                    bool edge = Math.Abs(Math.Abs(v) - 1) < 0.01;
                    if (expected < double.MaxValue && found < double.MaxValue)
                    {
                        ++hits;
                        worst = Math.Max(worst, Math.Abs(found - expected));
                        worstOff = Math.Max(worstOff, offSurface);
                    }
                    else if (expected < double.MaxValue && !edge)
                    {
                        Console.WriteLine("  missed: u " + u + " v " + v + " n.d " + SdfVector.Dot(d, normal) + " expected " + expected);
                        ++missed;
                    }
                    else if (found < double.MaxValue && !edge)
                    {
                        ++extra;
                    }
                }
                Console.WriteLine("ribbon3 (" + (roots == 1 ? "roots" : "newton") + ") twisted by " + twist + " degrees: " + shapes.Count + " shapes, " + rays + " rays, " + hits + " hit both, " + missed +
                                  " hit only the triangles, " + extra + " hit only the shape; the distances differ by up to " + worst + ", the hits are up to " + worstOff +
                                  " from the surface at their parameters");
                CHECK_EQ(missed, 0);
                CHECK_EQ(extra, 0);
                CHECK_GT(hits, 1500);
                // The hits are on the surface itself (to the single precision of the rays and of the texture coordinates); the
                // triangles cut the corners of a surface that bends and twists, by more along a ray at a low angle
                CHECK_LT(worstOff, 2e-5);
                CHECK_LT(worst, 1e-3);
            }
        }

        // The surface is across the normal that goes round from one end to the other, at every u and for any twist, and as wide
        // as the width
        [TEST("Ribbon3", "FollowsTheNormals")]
        public static void FollowsTheNormals()
        {
            foreach (Ribbon3Normals interpolation in new Ribbon3Normals[] { Ribbon3Normals.Slerp, Ribbon3Normals.Linear })
            foreach (double twist in new double[] { 0, 60, 170 })
            {
                Ribbon3Curve curve = Twisted(twist, interpolation);
                double worst = 0, worstWidth = 0;
                for (int i = 0; i <= 64; ++i)
                {
                    double u = i / 64.0;
                    SdfVector normal = curve.Normal(u, out SdfVector normalDerivative);
                    SdfVector tangent = curve.Velocity(u).Normalized();
                    SdfVector across = (normal - tangent * SdfVector.Dot(normal, tangent)).Normalized();
                    curve.Derivatives(u, 0, out SdfVector du, out SdfVector dv);
                    SdfVector surface = SdfVector.Cross(du, dv).Normalized();
                    worst = Math.Max(worst, Math.Acos(Math.Min(1, Math.Abs(SdfVector.Dot(surface, across)))) * 180 / Math.PI);
                    SdfVector bitangent = curve.Position(u, 1) - curve.Position(u, -1);
                    worstWidth = Math.Max(worstWidth, Math.Abs((bitangent - tangent * SdfVector.Dot(bitangent, tangent)).Length() / (2 * curve.Width(u)) - 1));
                }
                Console.WriteLine("ribbon3 (" + interpolation + ") twisted by " + twist + " degrees: the surface is up to " + worst + " degrees from the normals, the width off by up to " + worstWidth);
                CHECK_LT(worst, 1e-5);
                CHECK_LT(worstWidth, 1e-12);
            }
        }

        // The three ways for the normal to go from one end of a curve to the other: the same normals at the ends; in a line the
        // normal is that of the round way at the middle too, and ahead of or behind it elsewhere; through the basis it is the
        // normal of the cubic curve of the normals given
        [TEST("Ribbon3", "NormalsBetweenTheEnds")]
        public static void NormalsBetweenTheEnds()
        {
            SdfVector[] points = { new SdfVector(0, 0, 0), new SdfVector(1, 0.3, 0), new SdfVector(2, -0.3, 0), new SdfVector(3, 0, 0) };
            double[] widths = { 0.1, 0.1, 0.1, 0.1 };
            double angle = 150 * Math.PI / 180;
            SdfVector n0 = new SdfVector(0, 0, 1), n1 = new SdfVector(0, Math.Sin(angle), Math.Cos(angle));
            SdfVector[] values = { n0, new SdfVector(0, 0.2, 1), new SdfVector(0, 1, -0.3), n1 };
            Ribbon3Curve round = new Ribbon3Curve(points, widths, n0, n1, Ribbon3Normals.Slerp, values, null, 0, 1);
            Ribbon3Curve line = new Ribbon3Curve(points, widths, n0, n1, Ribbon3Normals.Linear, values, null, 0, 1);
            Ribbon3Curve basis = new Ribbon3Curve(points, widths, n0, n1, Ribbon3Normals.Basis, values, null, 0, 1);
            double ends = 0, middle = 0, apart = 0, basisOff = 0, derivativeOff = 0;
            for (int i = 0; i <= 32; ++i)
            {
                double u = i / 32.0, s = 1 - u;
                SdfVector a = round.Normal(u, out SdfVector da), b = line.Normal(u, out SdfVector db), c = basis.Normal(u, out SdfVector dc);
                double between = Math.Acos(Math.Max(-1, Math.Min(1, SdfVector.Dot(a, b)))) * 180 / Math.PI;
                apart = Math.Max(apart, between);
                if (i == 0 || i == 32)
                {
                    ends = Math.Max(ends, Math.Max(between, Math.Acos(Math.Max(-1, Math.Min(1, SdfVector.Dot(a, c)))) * 180 / Math.PI));
                }
                if (i == 16)
                {
                    middle = between;
                }
                SdfVector expected = (values[0] * (s * s * s) + values[1] * (3 * u * s * s) + values[2] * (3 * u * u * s) + values[3] * (u * u * u)).Normalized();
                basisOff = Math.Max(basisOff, (c - expected).Length());
                // The derivatives against differences
                const double h = 1e-6;
                if (i > 0 && i < 32)
                {
                    SdfVector lineDifference = (line.Normal(u + h, out SdfVector d1) - line.Normal(u - h, out SdfVector d2)) / (2 * h);
                    SdfVector basisDifference = (basis.Normal(u + h, out SdfVector d3) - basis.Normal(u - h, out SdfVector d4)) / (2 * h);
                    derivativeOff = Math.Max(derivativeOff, Math.Max((lineDifference - db).Length(), (basisDifference - dc).Length()));
                }
            }
            Console.WriteLine("ribbon3 normals over a curve that turns them by 150 degrees: in a line they are up to " + apart + " degrees from the round way (" + middle +
                              " at the middle, " + ends + " at the ends); through the basis off the cubic curve of the normals by " + basisOff + "; derivatives off by " +
                              derivativeOff);
            CHECK_LT(ends, 1e-5);
            CHECK_LT(middle, 1e-5);
            CHECK_GT(apart, 5);
            CHECK_LT(basisOff, 1e-14);
            CHECK_LT(derivativeOff, 1e-6);
        }

        // The default basis gives the centerline of "ribbon" and "ribbon2" (the quadratic B-spline, as cubic curves), and over
        // the zigzag with the normals 0 0 1 the surface lies flat with one side up
        [TEST("Ribbon3", "SameCenterlineAsTheRibbon")]
        public static void SameCenterlineAsTheRibbon()
        {
            SdfVector[] points = new SdfVector[10];
            SdfVector[] normals = new SdfVector[10];
            double[] widths = new double[10];
            double[] x = new double[10], y = new double[10], z = new double[10], w = new double[10], nx = new double[10], ny = new double[10], nz = new double[10];
            for (int i = 0; i < 10; ++i)
            {
                points[i] = new SdfVector(-1.6 + 0.36 * i, 0.55 + (i % 2 == 1 ? 0.28 : -0.28), 0.9 + 0.1 * Math.Sin(i));
                normals[i] = new SdfVector(0, 0, 1);
                widths[i] = 0.09;
                x[i] = points[i].x;
                y[i] = points[i].y;
                z[i] = points[i].z;
                w[i] = 0.09;
                nz[i] = 1;
            }
            StrandBasis basis = new StrandBasis("b-spline", 2, 1, "nonperiodic", 0.5, null);
            List<Ribbon3Curve> curves = Ribbons3.Curves(basis.Curves(x, 3), basis.Curves(y, 3), basis.Curves(z, 3), basis.Curves(w, 3), basis.Curves(nx, 3), basis.Curves(ny, 3),
                                                        basis.Curves(nz, 3), Ribbon3Normals.Slerp, null, 1);
            List<RibbonSegment> segments = RibbonBuilder.Build(points, normals, widths);
            CHECK_EQ(curves.Count, segments.Count);
            double worst = 0, lean = 0;
            int up = 0, shapes = Ribbons3.Shapes(Settings(), curves, 0, true, true, false, false, false, 0).Count;
            for (int s = 0; s < curves.Count; ++s)
            {
                for (int i = 0; i <= 16; ++i)
                {
                    double u = i / 16.0;
                    worst = Math.Max(worst, (curves[s].Position(u, 0) - segments[s].Position(u, 0)).Length());
                    curves[s].Derivatives(u, 0, out SdfVector du, out SdfVector dv);
                    SdfVector surface = SdfVector.Cross(du, dv).Normalized();
                    SdfVector tangent = du.Normalized();
                    SdfVector across = (new SdfVector(0, 0, 1) - tangent * tangent.z).Normalized();
                    lean = Math.Max(lean, Math.Acos(Math.Min(1, Math.Abs(SdfVector.Dot(surface, across)))) * 180 / Math.PI);
                    up += surface.z > 0 ? 1 : 0;
                }
            }
            Console.WriteLine("ribbon3 over a zigzag with the normals 0 0 1: " + curves.Count + " curves in " + shapes + " shapes, the centerline differs from that of ribbon by " +
                              worst + ", the surface is up to " + lean + " degrees from the normals, " + (up == 0 || up == 17 * curves.Count ? "one side along the whole strand" : "its sides swap"));
            CHECK_LT(worst, 1e-14);
            CHECK_LT(lean, 1e-5);
            CHECK(up == 0 || up == 17 * curves.Count);
        }

        // "flipturns" for the basis of "ribbon": over the zigzag with the normals 0 0 1 the surface is that of "ribbon"
        [TEST("Ribbon3", "FlipTurnsAsTheRibbon")]
        public static void FlipTurnsAsTheRibbon()
        {
            SdfVector[] points = new SdfVector[10];
            SdfVector[] normals = new SdfVector[10];
            double[] widths = new double[10];
            double[] x = new double[10], y = new double[10], z = new double[10], w = new double[10], nx = new double[10], ny = new double[10], nz = new double[10];
            for (int i = 0; i < 10; ++i)
            {
                points[i] = new SdfVector(-1.6 + 0.36 * i, 0.55 + (i % 2 == 1 ? 0.28 : -0.28), 0.9 + 0.1 * Math.Sin(i));
                normals[i] = new SdfVector(0, 0, 1);
                widths[i] = 0.09;
                x[i] = points[i].x;
                y[i] = points[i].y;
                z[i] = points[i].z;
                w[i] = 0.09;
                nz[i] = 1;
            }
            StrandBasis basis = new StrandBasis("b-spline", 2, 1, "nonperiodic", 0.5, null);
            List<RibbonSegment> segments = RibbonBuilder.Build(points, normals, widths);
            List<Ribbon3Curve> curves = Ribbons3.Curves(basis.Curves(x, 3), basis.Curves(y, 3), basis.Curves(z, 3), basis.Curves(w, 3), basis.Curves(nx, 3), basis.Curves(ny, 3),
                                                        basis.Curves(nz, 3), Ribbon3Normals.Slerp, segments, 1);
            CHECK_EQ(curves.Count, segments.Count);
            double atEnds = 0, within = 0, positions = 0;
            int opposite = 0, flips = 0;
            for (int s = 0; s < curves.Count; ++s)
            {
                for (int i = 0; i <= 16; ++i)
                {
                    double u = i / 16.0;
                    curves[s].Derivatives(u, 0, out SdfVector du, out SdfVector dv);
                    segments[s].Derivatives(u, 0, out SdfVector du2, out SdfVector dv2);
                    SdfVector mine = SdfVector.Cross(du, dv).Normalized(), theirs = SdfVector.Cross(du2, dv2).Normalized();
                    double angle = Math.Acos(Math.Max(-1, Math.Min(1, SdfVector.Dot(mine, theirs)))) * 180 / Math.PI;
                    // (the normal turns over where the ribbon narrows to nothing: compared where both are a quarter wide)
                    SdfVector t1 = du.Normalized(), t2 = du2.Normalized();
                    SdfVector g1 = dv.Normalized(), g2 = dv2.Normalized();
                    if (SdfVector.Cross(t1, g1).Length() > 0.25 && SdfVector.Cross(t2, g2).Length() > 0.25)
                    {
                        within = Math.Max(within, angle);
                    }
                    positions = Math.Max(positions, (curves[s].Position(u, 1) - segments[s].Position(u, 1)).Length());
                    if (i == 0 || i == 16)
                    {
                        atEnds = Math.Max(atEnds, angle);
                        opposite += SdfVector.Dot(mine, theirs) < 0 ? 1 : 0;
                    }
                }
                curves[s].Derivatives(0, 0, out SdfVector a0, out SdfVector b0);
                curves[s].Derivatives(1, 0, out SdfVector a1, out SdfVector b1);
                flips += SdfVector.Cross(a0, b0).z * SdfVector.Cross(a1, b1).z < 0 ? 1 : 0;
            }
            Console.WriteLine("ribbon3 with flipturns over the zigzag: " + flips + " of " + curves.Count + " curves turn their sides; from the surface of ribbon by up to " + atEnds +
                              " degrees at the ends of the curves (" + opposite + " opposite faces) and " + within + " degrees within them; the edges are up to " + positions + " apart (half width 0.09)");
            CHECK_EQ(opposite, 0);
            CHECK_GT(flips, 4);
            CHECK_LT(atEnds, 1e-4);
            CHECK_LT(positions, 1e-12);
            CHECK_LT(within, 1e-4);
        }

        // "flipturns" in a cubic basis (catmull-rom, which "ribbon" does not have): the surface is that of "ribbon2" with
        // "flipturns", but for the centerline, which is the cubic curve here and quadratic pieces there
        [TEST("Ribbon3", "FlipTurnsAsRibbon2")]
        public static void FlipTurnsAsRibbon2()
        {
            double[] x = new double[10], y = new double[10], z = new double[10], nx = new double[10], ny = new double[10], nz = new double[10];
            for (int i = 0; i < 10; ++i)
            {
                x[i] = -1.6 + 0.36 * i;
                y[i] = 0.55 + (i % 2 == 1 ? 0.28 : -0.28);
                z[i] = 0.9 + 0.1 * Math.Sin(i);
                nz[i] = 1;
            }
            StrandBasis basis = new StrandBasis("catmull-rom", 3, 1, "nonperiodic", 0.5, null);
            const int pieces = 4;
            List<RibbonSegment> segments = Ribbons2.Build(Ribbons2.Runs(basis, x, y, z, null, 0.09, nx, ny, nz, pieces), true, out double[] starts, out double[] extents);
            double[] w = new double[basis.Curves(x, 3).Length];
            Array.Fill(w, 0.09);
            List<Ribbon3Curve> curves = Ribbons3.Curves(basis.Curves(x, 3), basis.Curves(y, 3), basis.Curves(z, 3), w, basis.Curves(nx, 3), basis.Curves(ny, 3),
                                                        basis.Curves(nz, 3), Ribbon3Normals.Slerp, segments, pieces);
            CHECK_EQ(segments.Count, pieces * curves.Count);
            double centers = 0, edges = 0, directions = 0;
            int up = 0, places = 0;
            for (int s = 0; s < curves.Count; ++s)
            {
                for (int k = 0; k < pieces; ++k)
                {
                    for (int i = 0; i <= 8; ++i)
                    {
                        double local = i / 8.0, u = (k + local) / pieces;
                        RibbonSegment segment = segments[s * pieces + k];
                        centers = Math.Max(centers, (curves[s].Position(u, 0) - segment.Position(local, 0)).Length());
                        edges = Math.Max(edges, (curves[s].Position(u, 1) - segment.Position(local, 1)).Length());
                        SdfVector mine = (curves[s].Position(u, 1) - curves[s].Position(u, 0)).Normalized();
                        SdfVector theirs = (segment.Position(local, 1) - segment.Position(local, 0)).Normalized();
                        directions = Math.Max(directions, (mine - theirs).Length());
                        curves[s].Derivatives(u, 0, out SdfVector du, out SdfVector dv);
                        up += SdfVector.Cross(du, dv).z > 0 ? 1 : 0;
                        ++places;
                    }
                }
            }
            Console.WriteLine("ribbon3 with flipturns, catmull-rom: from ribbon2 with flipturns the centerline is up to " + centers + ", the edges up to " + edges +
                              " (half width 0.09), the bitangents differ by " + directions + "; one face is up at " + up + " of " + places + " places");
            CHECK_LT(directions, 1e-12);
            CHECK_LT(edges, 2 * centers + 1e-12);
            // (the quadratic pieces of "ribbon2" are that far from the cubic curve over this strand, which turns sharply: 0.13
            // percent of its length)
            CHECK_LT(centers, 0.01);
        }

        // "flipturns" with "flippieces" 1 over catmull-rom: "ribbon2" in 4 pieces takes the bitangents of the whole curves, and is
        // then the surface of "ribbon3" with one piece but for the centerline
        [TEST("Ribbon3", "FlipPiecesAsRibbon2")]
        public static void FlipPiecesAsRibbon2()
        {
            double[] x = new double[10], y = new double[10], z = new double[10], nx = new double[10], ny = new double[10], nz = new double[10];
            for (int i = 0; i < 10; ++i)
            {
                x[i] = -1.6 + 0.36 * i;
                y[i] = 0.55 + (i % 2 == 1 ? 0.28 : -0.28);
                z[i] = 0.9 + 0.1 * Math.Sin(i);
                nz[i] = 1;
            }
            StrandBasis basis = new StrandBasis("catmull-rom", 3, 1, "nonperiodic", 0.5, null);
            const int pieces = 4;
            List<RibbonSegment> coarse = Ribbons2.Build(Ribbons2.Runs(basis, x, y, z, null, 0.09, nx, ny, nz, 1), true, out double[] coarseStarts, out double[] coarseExtents);
            List<RibbonSegment> segments = Ribbons2.Build(Ribbons2.Runs(basis, x, y, z, null, 0.09, nx, ny, nz, pieces), true, out double[] starts, out double[] extents);
            Ribbons2.TakeBitangents(segments, pieces, coarse, 1);
            double[] w = new double[basis.Curves(x, 3).Length];
            Array.Fill(w, 0.09);
            List<Ribbon3Curve> curves = Ribbons3.Curves(basis.Curves(x, 3), basis.Curves(y, 3), basis.Curves(z, 3), w, basis.Curves(nx, 3), basis.Curves(ny, 3),
                                                        basis.Curves(nz, 3), Ribbon3Normals.Slerp, coarse, 1);
            double centers = 0, edges = 0, directions = 0;
            int flips = 0;
            for (int s = 0; s < curves.Count; ++s)
            {
                for (int k = 0; k < pieces; ++k)
                {
                    for (int i = 0; i <= 8; ++i)
                    {
                        double local = i / 8.0, u = (k + local) / pieces;
                        RibbonSegment segment = segments[s * pieces + k];
                        centers = Math.Max(centers, (curves[s].Position(u, 0) - segment.Position(local, 0)).Length());
                        edges = Math.Max(edges, (curves[s].Position(u, 1) - segment.Position(local, 1)).Length());
                        SdfVector mine = (curves[s].Position(u, 1) - curves[s].Position(u, 0)).Normalized();
                        SdfVector theirs = (segment.Position(local, 1) - segment.Position(local, 0)).Normalized();
                        directions = Math.Max(directions, Math.Acos(Math.Max(-1, Math.Min(1, SdfVector.Dot(mine, theirs)))) * 180 / Math.PI);
                    }
                }
                curves[s].Derivatives(0, 0, out SdfVector a0, out SdfVector b0);
                curves[s].Derivatives(1, 0, out SdfVector a1, out SdfVector b1);
                flips += SdfVector.Cross(a0, b0).z * SdfVector.Cross(a1, b1).z < 0 ? 1 : 0;
            }
            Console.WriteLine("flippieces 1, catmull-rom: " + flips + " of " + curves.Count + " curves turn their sides; ribbon2 in " + pieces +
                              " pieces from ribbon3: the centerline is up to " + centers + ", the edges up to " + edges + " (half width 0.09), the bitangents up to " +
                              directions + " degrees apart");
            // (no curve of this strand turns its sides: the two ends of each have nearly one direction, and the bitangent in a
            // line between them does not follow the centerline in between, so that the ribbon is narrower there)
            CHECK_LT(directions, 3);
            CHECK_LT(centers, 0.01);
            CHECK_LT(edges, 0.015);
        }

        // "flipstretch" "bend" over catmull-rom and the cubic b-spline: the ribbon turns its sides at the bends of the zigzag (as
        // "ribbon" does over its basis: at every one of its segments), and "ribbon2" and "ribbon3" have the same bitangents
        [TEST("Ribbon3", "BendStretchesTurnTheSides")]
        public static void BendStretchesTurnTheSides()
        {
            double[] x = new double[10], y = new double[10], z = new double[10], nx = new double[10], ny = new double[10], nz = new double[10];
            for (int i = 0; i < 10; ++i)
            {
                x[i] = -1.6 + 0.36 * i;
                y[i] = 0.55 + (i % 2 == 1 ? 0.28 : -0.28);
                z[i] = 0.9 + 0.1 * Math.Sin(i);
                nz[i] = 1;
            }
            foreach (string name in new string[] { "catmull-rom", "b-spline" })
            {
                StrandBasis basis = new StrandBasis(name, 3, 1, "nonperiodic", 0.5, null);
                const int pieces = 4;
                double[] cx = basis.Curves(x, 3), cy = basis.Curves(y, 3), cz = basis.Curves(z, 3);
                double[] cnx = basis.Curves(nx, 3), cny = basis.Curves(ny, 3), cnz = basis.Curves(nz, 3);
                List<RibbonSegment> stretches = Ribbons2.BendStretches(cx, cy, cz, cnx, cny, cnz);
                List<RibbonSegment> segments = Ribbons2.Build(Ribbons2.Runs(basis, x, y, z, null, 0.09, nx, ny, nz, pieces), true, out double[] starts, out double[] extents);
                Ribbons2.TakeBitangents(segments, pieces, stretches, 2);
                double[] w = new double[cx.Length];
                Array.Fill(w, 0.09);
                List<Ribbon3Curve> curves = Ribbons3.Curves(cx, cy, cz, w, cnx, cny, cnz, Ribbon3Normals.Slerp, stretches, 2);
                CHECK_EQ(stretches.Count, 2 * curves.Count);
                double directions = 0;
                int turns = 0;
                double sideBefore = 0;
                for (int s = 0; s < curves.Count; ++s)
                {
                    for (int k = 0; k < pieces; ++k)
                    {
                        for (int i = 0; i <= 8; ++i)
                        {
                            double local = i / 8.0, u = (k + local) / pieces;
                            RibbonSegment segment = segments[s * pieces + k];
                            SdfVector mine = (curves[s].Position(u, 1) - curves[s].Position(u, 0)).Normalized();
                            SdfVector theirs = (segment.Position(local, 1) - segment.Position(local, 0)).Normalized();
                            directions = Math.Max(directions, Math.Acos(Math.Max(-1, Math.Min(1, SdfVector.Dot(mine, theirs)))) * 180 / Math.PI);
                        }
                    }
                    // The face that is up at the middle of the curve (the end of a stretch)
                    curves[s].Derivatives(0.5, 0, out SdfVector du, out SdfVector dv);
                    double side = SdfVector.Cross(du, dv).z;
                    turns += s > 0 && side * sideBefore < 0 ? 1 : 0;
                    sideBefore = side;
                }
                Console.WriteLine("flipstretch bend, " + name + ": the ribbon turns its sides over " + turns + " of the " + (curves.Count - 1) +
                                  " stretches between the middles of the curves; the bitangents of ribbon2 in " + pieces + " pieces and ribbon3 are up to " + directions +
                                  " degrees apart");
                // (the cubic b-spline of these points is a gentle wave, whose bends turn by less than a right angle)
                if (name == "catmull-rom")
                {
                    CHECK_EQ(turns, curves.Count - 1);
                }
                else
                {
                    CHECK_EQ(turns, 0);
                }
                // (the angle of two directions that are the same is found to a millionth of a degree)
                CHECK_LT(directions, 1e-4);
            }
        }

        // Without normals a strand in a plane lies flat in it, and the normal is one along the strand
        [TEST("Ribbon3", "WithoutNormalsNoTwist")]
        public static void WithoutNormalsNoTwist()
        {
            double[] x = new double[10], y = new double[10], z = new double[10], w = new double[10];
            for (int i = 0; i < 10; ++i)
            {
                x[i] = -1.6 + 0.36 * i;
                y[i] = 0.55 + (i % 2 == 1 ? 0.28 : -0.28);
                w[i] = 0.09;
            }
            StrandBasis basis = new StrandBasis("catmull-rom", 3, 1, "nonperiodic", 0.5, null);
            List<Ribbon3Curve> curves = Ribbons3.Curves(basis.Curves(x, 3), basis.Curves(y, 3), basis.Curves(z, 3), basis.Curves(w, 3), null, null, null, Ribbon3Normals.Slerp, null, 1);
            double worst = 0;
            int up = 0, places = 0;
            foreach (Ribbon3Curve curve in curves)
            {
                for (int i = 0; i <= 16; ++i)
                {
                    curve.Derivatives(i / 16.0, 0, out SdfVector du, out SdfVector dv);
                    SdfVector surface = SdfVector.Cross(du, dv).Normalized();
                    worst = Math.Max(worst, Math.Acos(Math.Min(1, Math.Abs(surface.z))) * 180 / Math.PI);
                    up += surface.z > 0 ? 1 : 0;
                    ++places;
                }
            }
            Console.WriteLine("ribbon3 without normals over a zigzag in a plane: the surface is up to " + worst + " degrees out of the plane, " +
                              (up == 0 || up == places ? "one side along the whole strand" : "its sides swap"));
            CHECK_LT(worst, 1e-6);
            CHECK(up == 0 || up == places);
        }

        private static Ribbon3Shape LightShape(bool solidAngle)
        {
            // A bent and twisted strip of changing width
            SdfVector[] points = { new SdfVector(0, 0, 0), new SdfVector(1, 1, 0.2), new SdfVector(2, 1, 0.4), new SdfVector(3, 0, 0.5) };
            double[] widths = { 0.15, 0.25, 0.3, 0.2 };
            Ribbon3Curve curve = new Ribbon3Curve(points, widths, new SdfVector(0, 0, 1), new SdfVector(0, 0.6, 0.8), Ribbon3Normals.Slerp, null, null, 0, 1);
            return new Ribbon3Shape(Settings(), curve, 0, 1, Ribbons3.Leaves(curve, true), 0, solidAngle, true, false, false, false, null);
        }

        // The samples by area: the density of a sample is the density that the shape gives for its point, and the mean of
        // 1 / pdf is the area
        [TEST("Ribbon3", "AreaDensityMatchesTheSamples")]
        public static void AreaDensityMatchesTheSamples()
        {
            Ribbon3Shape shape = LightShape(true);
            Random random = new Random(21);
            const int count = 200000;
            int mismatched = 0;
            double sum = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                if (!ss)
                {
                    continue;
                }
                sum += 1.0 / ss.value().pdf;
                Float pdf = shape.PDF(ss.value().intr);
                mismatched += Math.Abs(pdf - ss.value().pdf) > 1e-3 * ss.value().pdf ? 1 : 0;
            }
            double area = sum / count;
            Console.WriteLine("ribbon3 area: by samples " + area + ", Area() " + shape.Area() + "; " + mismatched + " densities differ");
            CHECK_LE(mismatched, count / 20000);
            CHECK_LT(Math.Abs(area - shape.Area()) / area, 0.01);
        }

        // From a point: the solid angle of the ribbon by the directions that hit it and by the samples (the mean of 1 / pdf of
        // the ones that are seen) are the same, the density of a direction is that of its sample, and the samples for the point
        // vary less than those by area
        [TEST("Ribbon3", "SamplesGiveTheSolidAngle")]
        public static void SamplesGiveTheSolidAngle()
        {
            Point3f[] references = { new Point3f(0.3f, 0.1f, 0.4f), new Point3f(-1.5f, 0.05f, 0.15f), new Point3f(2.0f, 3.0f, 2.5f), new Point3f(1.0f, 0.2f, 0.25f) };
            foreach (Point3f reference in references)
            {
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(reference, new Vector3f(0, 0, 0)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
                Ribbon3Shape shape = LightShape(true), plain = LightShape(false);
                Random random = new Random(31);
                const int count = 400000;
                int hits = 0;
                for (int i = 0; i < count; ++i)
                {
                    double z = 1 - 2 * random.NextDouble(), radius = Math.Sqrt(Math.Max(0, 1 - z * z)), phi = 2 * Math.PI * random.NextDouble();
                    Vector3f wi = new Vector3f((Float)(radius * Math.Cos(phi)), (Float)(radius * Math.Sin(phi)), (Float)z);
                    hits += shape.IntersectP(ctx.SpawnRay(wi)) ? 1 : 0;
                }
                double byDirections = 4 * Math.PI * hits / count;
                double[] mean = new double[2], square = new double[2];
                int compared = 0, mismatched = 0;
                for (int kind = 0; kind < 2; ++kind)
                {
                    Ribbon3Shape sampled = kind == 0 ? shape : plain;
                    for (int i = 0; i < count; ++i)
                    {
                        pstd.optional<ShapeSample> ss = sampled.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                        if (!ss || !(ss.value().pdf > 0))
                        {
                            continue;
                        }
                        Vector3f wi = pbrt.Normalize(ss.value().intr.p() - reference);
                        pstd.optional<ShapeIntersection> hit = sampled.Intersect(ctx.SpawnRay(wi));
                        if (!hit || pbrt.Distance(hit.value().intr.p(), ss.value().intr.p()) > 1e-3)
                        {
                            continue;
                        }
                        double value = 1.0 / ss.value().pdf;
                        mean[kind] += value / count;
                        square[kind] += value * value / count;
                        if (kind == 0 && i % 50 == 0 && pbrt.AbsDot(ss.value().intr.n, wi) > 0.05)
                        {
                            ++compared;
                            Float pdf = sampled.PDF(ctx, wi);
                            mismatched += Math.Abs(pdf - ss.value().pdf) > 0.02 * ss.value().pdf ? 1 : 0;
                        }
                    }
                }
                double deviation = Math.Sqrt(Math.Max(0, square[0] - mean[0] * mean[0])), deviationPlain = Math.Sqrt(Math.Max(0, square[1] - mean[1] * mean[1]));
                Console.WriteLine("ribbon3 from " + reference + ": solid angle by directions " + byDirections.ToString("0.00000") + ", by samples " + mean[0].ToString("0.00000") +
                                  " (deviation " + deviation.ToString("0.0000") + "), by area samples " + mean[1].ToString("0.00000") + " (deviation " +
                                  deviationPlain.ToString("0.0000") + "); " + mismatched + " of " + compared + " densities differ");
                double tolerance = 3 * Math.Sqrt(byDirections * (4 * Math.PI - byDirections) / count) + 4 * deviation / Math.Sqrt(count) + 0.005 * byDirections;
                CHECK_LT(Math.Abs(mean[0] - byDirections), tolerance);
                CHECK_GT(compared, 500);
                CHECK_LE(mismatched, compared / 100);
                CHECK_LT(deviation, deviationPlain);
            }
        }
    }
}
