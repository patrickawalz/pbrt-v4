// (not in pbrt-v4) volume_test.cs
//
// The volume box (shapes_volume.cs): intersection against an independent test of the six parallelograms in render space, normals, (u, v), the area, exact
// area sampling and the solid angle pdf, under a rotation, a non-uniform scale and a mirror.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class VolumeTests
    {
        private static readonly double[] Low = { -1, 0, -0.5 }, High = { 2, 1, 0.5 };

        private static Shape Make(Transform t, bool reverse)
        {
            return new VolumeBox(Low, High, t, Inverse(t), reverse);
        }

        private static Transform Placement()
        {
            return Translate(new Vector3f(0.5f, -1, 2)) * Rotate(35, new Vector3f(1, 2, 0.5f)) * Scale(1.2f, 0.7f, 2.0f);
        }

        // The six parallelograms in render space: corner, edge a, edge b
        private static void Faces(Transform t, double[][] corner, double[][] ea, double[][] eb)
        {
            for (int face = 0; face < 6; ++face)
            {
                int a = face >> 1, s = face & 1, a1 = (a + 1) % 3, a2 = (a + 2) % 3;
                double[] o = new double[3], e1 = new double[3], e2 = new double[3];
                o[a] = s == 1 ? High[a] : Low[a];
                o[a1] = Low[a1];
                o[a2] = Low[a2];
                e1[a1] = High[a1] - Low[a1];
                e2[a2] = High[a2] - Low[a2];
                Point3f c = t.Apply(new Point3f((Float)o[0], (Float)o[1], (Float)o[2]));
                Vector3f x = t.Apply(new Vector3f((Float)e1[0], (Float)e1[1], (Float)e1[2])), y = t.Apply(new Vector3f((Float)e2[0], (Float)e2[1], (Float)e2[2]));
                corner[face] = new double[] { c.x, c.y, c.z };
                ea[face] = new double[] { x.x, x.y, x.z };
                eb[face] = new double[] { y.x, y.y, y.z };
            }
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new double[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        // The reference: the smallest t > 0 over the six parallelograms
        private static bool Reference(Transform t, Ray ray, out double tHit, out int hitFace)
        {
            double[][] corner = new double[6][], ea = new double[6][], eb = new double[6][];
            Faces(t, corner, ea, eb);
            double[] o = { ray.o.x, ray.o.y, ray.o.z }, d = { ray.d.x, ray.d.y, ray.d.z };
            tHit = double.MaxValue;
            hitFace = -1;
            for (int f = 0; f < 6; ++f)
            {
                double[] n = Cross(ea[f], eb[f]);
                double denominator = Dot(n, d);
                if (denominator == 0)
                {
                    continue;
                }
                double[] diff = { corner[f][0] - o[0], corner[f][1] - o[1], corner[f][2] - o[2] };
                double s = Dot(n, diff) / denominator;
                if (!(s > 0) || s >= tHit)
                {
                    continue;
                }
                double[] p = { o[0] + s * d[0] - corner[f][0], o[1] + s * d[1] - corner[f][1], o[2] + s * d[2] - corner[f][2] };
                double aa = Dot(ea[f], ea[f]), bb = Dot(eb[f], eb[f]), ab = Dot(ea[f], eb[f]);
                double pa = Dot(p, ea[f]), pb = Dot(p, eb[f]);
                double det = aa * bb - ab * ab;
                double u = (pa * bb - pb * ab) / det, v = (pb * aa - pa * ab) / det;
                if (u >= -1e-9 && u <= 1 + 1e-9 && v >= -1e-9 && v <= 1 + 1e-9)
                {
                    tHit = s;
                    hitFace = f;
                }
            }
            return hitFace >= 0;
        }

        [TEST("Volume", "IntersectionAgainstFaces")]
        public static void IntersectionAgainstFaces()
        {
            Transform t = Placement();
            Shape shape = Make(t, false);
            Random rng = new Random(4);
            Point3f center = t.Apply(new Point3f(0.5f, 0.5f, 0));
            int hits = 0, inside = 0;
            for (int i = 0; i < 20000; ++i)
            {
                double z = 1 - 2 * rng.NextDouble(), r = Math.Sqrt(1 - z * z), phi = 2 * Math.PI * rng.NextDouble();
                double far = rng.NextDouble() < 0.2 ? 0.8 * rng.NextDouble() : 8;
                Point3f o = new Point3f((Float)(center.x + far * r * Math.Cos(phi)), (Float)(center.y + far * r * Math.Sin(phi)), (Float)(center.z + far * z));
                double z2 = 1 - 2 * rng.NextDouble(), r2 = Math.Sqrt(1 - z2 * z2), phi2 = 2 * Math.PI * rng.NextDouble();
                Vector3f d = new Vector3f((Float)(r2 * Math.Cos(phi2)), (Float)(r2 * Math.Sin(phi2)), (Float)z2);
                if (i % 4 == 0)
                {
                    d = pbrt.Normalize(new Vector3f((Float)(center.x - o.x + rng.NextDouble() - 0.5), (Float)(center.y - o.y + rng.NextDouble() - 0.5), (Float)(center.z - o.z + rng.NextDouble() - 0.5)));
                }
                Ray ray = new Ray(o, d);
                bool expected = Reference(t, ray, out double tHit, out int face);
                pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                if (expected != si.has_value())
                {
                    // a ray through an edge or a corner
                    EXPECT_TRUE(false, "hit or miss, ray " + i + " reference " + expected);
                    continue;
                }
                EXPECT_EQ(expected, shape.IntersectP(ray), "IntersectP, ray " + i);
                if (expected)
                {
                    ++hits;
                    EXPECT_NEAR(tHit, si.value().tHit, 1e-4 * Math.Max(1, tHit), "t, ray " + i);
                    Normal3f n = si.value().intr.n;
                    double[] dn = { n.x, n.y, n.z };
                    double[] expectedN = Cross(Facesa(t, face), Facesb(t, face));
                    double l = Math.Sqrt(Dot(expectedN, expectedN)) * ((face & 1) == 1 ? 1 : -1);
                    EXPECT_NEAR(1.0, Dot(dn, expectedN) / l, 1e-4, "normal, ray " + i);
                    // outward: the origin is outside of the hit face's half space when it enters, inside when it exits
                    Vector3f toHit = si.value().intr.p() - center;
                    EXPECT_GT(Dot(dn, new double[] { toHit.x, toHit.y, toHit.z }), 0.0, "normal points away from the center, ray " + i);
                    if (far < 1)
                    {
                        ++inside;
                    }
                }
                // tMax cuts the hit
                if (expected)
                {
                    EXPECT_TRUE(!shape.IntersectP(ray, (Float)(tHit * 0.999)), "tMax");
                }
            }
            EXPECT_GT(hits, 3000);
            EXPECT_GT(inside, 200, "rays that start inside and leave through a face");
        }

        private static double[] Facesa(Transform t, int face)
        {
            double[][] c = new double[6][], a = new double[6][], b = new double[6][];
            Faces(t, c, a, b);
            return a[face];
        }

        private static double[] Facesb(Transform t, int face)
        {
            double[][] c = new double[6][], a = new double[6][], b = new double[6][];
            Faces(t, c, a, b);
            return b[face];
        }

        [TEST("Volume", "UvAndAreaAndMirror")]
        public static void UvAndAreaAndMirror()
        {
            Transform t = Placement();
            double[][] c = new double[6][], a = new double[6][], b = new double[6][];
            Faces(t, c, a, b);
            double expectedArea = 0;
            for (int f = 0; f < 6; ++f)
            {
                double[] n = Cross(a[f], b[f]);
                expectedArea += Math.Sqrt(Dot(n, n));
            }
            Shape shape = Make(t, false);
            EXPECT_NEAR(expectedArea, shape.Area(), 1e-4 * expectedArea, "area");
            // P(u, v) = corner + u dP/du + v dP/dv on the face that is hit
            Random rng = new Random(2);
            Point3f center = t.Apply(new Point3f(0.5f, 0.5f, 0));
            for (int i = 0; i < 500; ++i)
            {
                double z = 1 - 2 * rng.NextDouble(), r = Math.Sqrt(1 - z * z), phi = 2 * Math.PI * rng.NextDouble();
                Point3f o = new Point3f((Float)(center.x + 9 * r * Math.Cos(phi)), (Float)(center.y + 9 * r * Math.Sin(phi)), (Float)(center.z + 9 * z));
                Ray ray = new Ray(o, pbrt.Normalize(new Vector3f((Float)(center.x - o.x + rng.NextDouble() - 0.5), (Float)(center.y - o.y + rng.NextDouble() - 0.5),
                                                                (Float)(center.z - o.z + rng.NextDouble() - 0.5))));
                pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                if (!si.has_value())
                {
                    continue;
                }
                SurfaceInteraction intr = si.value().intr;
                EXPECT_TRUE(intr.uv.x >= 0 && intr.uv.x <= 1 && intr.uv.y >= 0 && intr.uv.y <= 1, "uv in the unit square");
                Point3f p = intr.p();
                // find the face through the reference, and rebuild the position
                Reference(t, ray, out double tHit, out int face);
                double[] q = { c[face][0], c[face][1], c[face][2] };
                // the face frame of the box has dP/du x dP/dv outward, which for the minimum sides swaps the edges
                double[] pu = (face & 1) == 1 ? a[face] : b[face], pv = (face & 1) == 1 ? b[face] : a[face];
                for (int k = 0; k < 3; ++k)
                {
                    q[k] += intr.uv.x * pu[k] + intr.uv.y * pv[k];
                }
                EXPECT_NEAR(0.0, Math.Abs(q[0] - p.x) + Math.Abs(q[1] - p.y) + Math.Abs(q[2] - p.z), 2e-4, "position from (u, v)");
            }
            // a mirror and reverse orientation: outward normal flips with reverseOrientation only
            Transform mirror = Translate(new Vector3f(1, 1, 1)) * Scale(-1, 1, 1);
            foreach (bool reverse in new bool[] { false, true })
            {
                Shape m = Make(mirror, reverse);
                Ray ray = new Ray(new Point3f(5, 1.5f, 1), new Vector3f(-1, 0, 0));
                pstd.optional<ShapeIntersection> si = m.Intersect(ray);
                ASSERT_TRUE(si.has_value());
                // mirrored in x: the box spans x in [-1, 2] after the mirror at 1 - [ -1.. 2 ] = [-1, 2]... the face hit is at x = 1 - (-1) = 2
                EXPECT_NEAR(3.0, si.value().tHit, 1e-4);
                EXPECT_TRUE(reverse ? si.value().intr.n.x < -0.99 : si.value().intr.n.x > 0.99, "intersection normal, reverse " + reverse);
                pstd.optional<ShapeSample> ss = m.Sample(new Point2f(0.1f, 0.3f));
                ASSERT_TRUE(ss.has_value());
                Vector3f fromCenter = ss.value().intr.p() - mirror.Apply(new Point3f(0.5f, 0.5f, 0));
                double dot = ss.value().intr.n.x * fromCenter.x + ss.value().intr.n.y * fromCenter.y + ss.value().intr.n.z * fromCenter.z;
                EXPECT_TRUE(reverse ? dot < 0 : dot > 0, "sample normal, reverse " + reverse);
            }
        }

        [TEST("Volume", "AreaSamplingAndSolidAngle")]
        public static void AreaSamplingAndSolidAngle()
        {
            Transform t = Placement();
            Shape shape = Make(t, false);
            double area = shape.Area();
            Random rng = new Random(7);
            // the share of the samples on each face
            double[][] c = new double[6][], a = new double[6][], b = new double[6][];
            Faces(t, c, a, b);
            int count = 120000;
            int[] perFace = new int[6];
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                ASSERT_TRUE(ss.has_value());
                EXPECT_NEAR(1.0 / area, ss.value().pdf, 1e-6);
                Point3f p = ss.value().intr.p();
                double best = double.MaxValue;
                int bestFace = 0;
                for (int f = 0; f < 6; ++f)
                {
                    double[] n = Cross(a[f], b[f]);
                    double l = Math.Sqrt(Dot(n, n));
                    double distance = Math.Abs(Dot(n, new double[] { p.x - c[f][0], p.y - c[f][1], p.z - c[f][2] })) / l;
                    if (distance < best)
                    {
                        best = distance;
                        bestFace = f;
                    }
                }
                EXPECT_LT(best, 1e-4, "on a face");
                ++perFace[bestFace];
            }
            for (int f = 0; f < 6; ++f)
            {
                double[] n = Cross(a[f], b[f]);
                double share = Math.Sqrt(Dot(n, n)) / area, sigma = Math.Sqrt(share * (1 - share) / count);
                EXPECT_LT(Math.Abs(perFace[f] / (double)count - share) / sigma, 4.5, "share of face " + f);
            }
            // the solid angle of the box from a point outside: by random directions, and by the samples of Sample(ctx), which are weighted by 1 / pdf where the sample
            // is the first hit
            Point3f from = new Point3f(6, -3, 5);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(from), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
            int directions = 600000, hit = 0;
            for (int i = 0; i < directions; ++i)
            {
                double z = 1 - 2 * rng.NextDouble(), r = Math.Sqrt(1 - z * z), phi = 2 * Math.PI * rng.NextDouble();
                if (shape.IntersectP(new Ray(from, new Vector3f((Float)(r * Math.Cos(phi)), (Float)(r * Math.Sin(phi)), (Float)z))))
                {
                    ++hit;
                }
            }
            double p0 = hit / (double)directions;
            double byDirections = 4 * Math.PI * p0, errorDirections = 4 * Math.PI * Math.Sqrt(p0 * (1 - p0) / directions);
            double sum = 0, sum2 = 0;
            int samples = 300000, mismatch = 0;
            for (int i = 0; i < samples; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                double w = 0;
                if (ss.has_value())
                {
                    Vector3f wi = pbrt.Normalize(ss.value().intr.p() - from);
                    double dot = ss.value().intr.n.x * wi.x + ss.value().intr.n.y * wi.y + ss.value().intr.n.z * wi.z;
                    if (dot < -1e-3)
                    {
                        // front face: the first hit; pdf of the direction agrees
                        double pdf = shape.PDF(ctx, wi);
                        if (Math.Abs(pdf / ss.value().pdf - 1) > 1e-3)
                        {
                            ++mismatch;
                        }
                        w = 1.0 / ss.value().pdf;
                    }
                }
                sum += w;
                sum2 += w * w;
            }
            double mean = sum / samples, error = Math.Sqrt(Math.Max(0, sum2 / samples - mean * mean) / samples);
            EXPECT_EQ(0, mismatch, "PDF(ctx, wi) against the pdf of the sample");
            double zscore = Math.Abs(mean - byDirections) / Math.Sqrt(error * error + errorDirections * errorDirections);
            EXPECT_LT(zscore, 4.0, "solid angle by samples " + mean + " +- " + error + " vs by directions " + byDirections + " +- " + errorDirections);
        }

        [TEST("Volume", "ParserNames")]
        public static void ParserNames()
        {
            List<ParsedParameter> list = new List<ParsedParameter>();
            ParsedParameter bound = new ParsedParameter(null);
            bound.type = "float";
            bound.name = "bound";
            foreach (float v in new float[] { -1, 1, -2, 2, -3, 3 })
            {
                bound.floats.push_back(v);
            }
            list.Add(bound);
            Transform id = new Transform();
            pstd.vector<Shape> made = Shape.Create("volume", id, id, false, new ParameterDictionary(list, RGBColorSpace.sRGB), new Dictionary<string, FloatTexture>(), null,
                                                  new Allocator());
            ASSERT_EQ(1, (int)made.size());
            EXPECT_NEAR(2 * (2 * 4 + 2 * 6 + 4 * 6), made[0].Area(), 1e-4);
            pstd.optional<ShapeIntersection> si = made[0].Intersect(new Ray(new Point3f(0, 0, 10), new Vector3f(0, 0, -1)));
            ASSERT_TRUE(si.has_value());
            EXPECT_NEAR(7.0, si.value().tHit, 1e-5);
        }
    }
}
