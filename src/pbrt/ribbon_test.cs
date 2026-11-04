// (not in pbrt-v4) ribbon_test.cs
//
// Tests of the ribbons (ribbon.cs, ribbonshape.cs).

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class RibbonTests
    {
        private static SdfVector[] Spiral(int count, double radius, double rise)
        {
            SdfVector[] points = new SdfVector[count];
            for (int i = 0; i < count; ++i)
            {
                double angle = i * 0.7;
                points[i] = new SdfVector(radius * Math.Cos(angle), radius * Math.Sin(angle), rise * i);
            }
            return points;
        }

        private static double[] Widths(int count, double width)
        {
            double[] widths = new double[count];
            for (int i = 0; i < count; ++i)
            {
                widths[i] = width * (1 + 0.5 * Math.Sin(i));
            }
            return widths;
        }

        private static void AgainstTessellation(List<RibbonSegment> segments, int rayCount, out int total, out int missed, out int extra, out double worstT)
        {
            int nu = 160, nv = 24;
            List<SdfVector[]> triangles = new List<SdfVector[]>();
            foreach (RibbonSegment segment in segments)
            {
                SdfVector[,] grid = new SdfVector[nu + 1, nv + 1];
                for (int i = 0; i <= nu; ++i)
                {
                    for (int j = 0; j <= nv; ++j)
                    {
                        grid[i, j] = segment.Position((double)i / nu, 2.0 * j / nv - 1);
                    }
                }
                for (int i = 0; i < nu; ++i)
                {
                    for (int j = 0; j < nv; ++j)
                    {
                        triangles.Add(new SdfVector[] { grid[i, j], grid[i + 1, j], grid[i, j + 1] });
                        triangles.Add(new SdfVector[] { grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1] });
                    }
                }
            }
            Random random = new Random(7);
            total = 0;
            missed = 0;
            extra = 0;
            worstT = 0;
            for (int k = 0; k < rayCount; ++k)
            {
                SdfVector dir = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
                SdfVector target = new SdfVector(random.NextDouble() * 2.4 - 1.2, random.NextDouble() * 2.4 - 1.2, random.NextDouble() * 4);
                SdfVector origin = target - dir * 8;
                double expected = double.MaxValue;
                foreach (SdfVector[] triangle in triangles)
                {
                    if (SubdivisionSurfaceTests.MoellerHit(origin, dir, triangle, out double t) && t < expected)
                    {
                        expected = t;
                    }
                }
                double actual = double.MaxValue;
                foreach (RibbonSegment segment in segments)
                {
                    if (segment.Intersect(origin, dir, 0, 1e30, 0, out double t, out double u, out double v) && t < actual)
                    {
                        actual = t;
                    }
                }
                if (expected < double.MaxValue)
                {
                    ++total;
                    if (actual == double.MaxValue)
                    {
                        ++missed;
                    }
                    else
                    {
                        worstT = Math.Max(worstT, Math.Abs(actual - expected));
                    }
                }
                else if (actual < double.MaxValue)
                {
                    ++extra;
                }
            }
        }

        [TEST("Ribbon", "Ribbon3MatchesTheTessellation")]
        public static void Ribbon3MatchesTheTessellation()
        {
            SdfVector[] points = Spiral(9, 1.0, 0.35);
            List<RibbonSegment> segments = RibbonBuilder.Build(points, null, Widths(points.Length, 0.18));
            CHECK_EQ(segments.Count, 7);
            AgainstTessellation(segments, 3000, out int total, out int missed, out int extra, out double worstT);
            Console.WriteLine("ribbon3: hits " + total + " missed " + missed + " extra " + extra + " worst t " + worstT);
            CHECK_GT(total, 150);
            CHECK_LT(missed, 3);
            CHECK_LT(extra, 3);
            CHECK_LT(worstT, 5e-3);
        }

        [TEST("Ribbon", "Ribbon3WithNormalsMatchesTheTessellation")]
        public static void Ribbon3WithNormalsMatchesTheTessellation()
        {
            SdfVector[] points = Spiral(9, 1.0, 0.35);
            SdfVector[] normals = new SdfVector[points.Length];
            for (int i = 0; i < normals.Length; ++i)
            {
                // the normals of a cone around the axis of the spiral
                normals[i] = new SdfVector(points[i].x, points[i].y, 0.6).Normalized();
            }
            List<RibbonSegment> segments = RibbonBuilder.Build(points, normals, Widths(points.Length, 0.18));
            AgainstTessellation(segments, 3000, out int total, out int missed, out int extra, out double worstT);
            Console.WriteLine("ribbon3 with normals: hits " + total + " missed " + missed + " extra " + extra + " worst t " + worstT);
            CHECK_GT(total, 150);
            CHECK_LT(missed, 3);
            CHECK_LT(extra, 3);
            CHECK_LT(worstT, 5e-3);
        }

        [TEST("Ribbon", "SegmentsAreConnectedSmoothly")]
        public static void SegmentsAreConnectedSmoothly()
        {
            SdfVector[] points = Spiral(9, 1.0, 0.35);
            List<RibbonSegment> segments = RibbonBuilder.Build(points, null, Widths(points.Length, 0.18));
            double worstPosition = 0, worstAngle = 0, worstUnconnected = 0;
            for (int i = 0; i + 1 < segments.Count; ++i)
            {
                for (int k = 0; k <= 4; ++k)
                {
                    double v = -1 + 0.5 * k;
                    SdfVector end = segments[i].Position(1, v), start = segments[i + 1].Position(0, v);
                    worstPosition = Math.Max(worstPosition, (end - start).Length());
                    segments[i].Derivatives(1, v, out SdfVector dpdu0, out SdfVector dpdv0);
                    segments[i + 1].Derivatives(0, v, out SdfVector dpdu1, out SdfVector dpdv1);
                    SdfVector n0 = SdfVector.Cross(dpdu0, dpdv0).Normalized(), n1 = SdfVector.Cross(dpdu1, dpdv1).Normalized();
                    worstAngle = Math.Max(worstAngle, Math.Acos(Math.Min(1, Math.Abs(SdfVector.Dot(n0, n1)))));
                    // The same joint without the terms of the connection
                    RibbonSegment plain = new RibbonSegment();
                    plain.b0 = segments[i + 1].b0;
                    plain.b1 = segments[i + 1].b1;
                    plain.b2 = segments[i + 1].b2;
                    plain.bitangent0 = segments[i + 1].bitangent0;
                    plain.bitangent1 = segments[i + 1].bitangent1.Normalized();
                    plain.width0 = segments[i + 1].width0;
                    plain.width1 = segments[i + 1].width1;
                    plain.width2 = segments[i + 1].width2;
                    plain.Derivatives(0, v, out SdfVector dpdu2, out SdfVector dpdv2);
                    SdfVector n2 = SdfVector.Cross(dpdu2, dpdv2).Normalized();
                    worstUnconnected = Math.Max(worstUnconnected, Math.Acos(Math.Min(1, Math.Abs(SdfVector.Dot(n0, n2)))));
                }
            }
            Console.WriteLine("joints: worst gap " + worstPosition + " worst angle between the normals " + worstAngle + " (without the connection terms " + worstUnconnected + ")");
            CHECK_LT(worstPosition, 1e-9);
            CHECK_LT(worstAngle, 1e-3);
        }
    }
}
