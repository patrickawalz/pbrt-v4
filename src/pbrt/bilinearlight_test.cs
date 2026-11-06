// (not in pbrt-v4) Tests of the sampling of a rectangular BilinearPatch (an area light) from a reference point, against independent computations:
// the integral of the PDF over the directions, the agreement of PDF() with the density used by Sample(), the irradiance estimated with Sample()
// against a quadrature over the area of the patch, the same two triangles as a control, and the reference point that is on the patch itself.

using System;
using System.Diagnostics;
using System.Globalization;
using testing;

namespace pbrt
{
    public static unsafe class BilinearLightTests
    {
        private static Shape[] MakeRectangle(Point3f centre, Vector3f axisU, Vector3f axisV, Float halfU, Float halfV, bool asTriangles)
        {
            Point3f[] p = Corners(centre, axisU, axisV, halfU, halfV);
            return MakeShapes(p, asTriangles);
        }

        private static Point3f[] Corners(Point3f centre, Vector3f axisU, Vector3f axisV, Float halfU, Float halfV)
        {
            return new Point3f[]
            {
                centre - halfU * axisU - halfV * axisV, centre + halfU * axisU - halfV * axisV,
                centre - halfU * axisU + halfV * axisV, centre + halfU * axisU + halfV * axisV
            };
        }

        private static Shape[] MakeShapes(Point3f[] p, bool asTriangles)
        {
            if (asTriangles)
            {
                TriangleMesh triangles = new TriangleMesh(new Transform(), false, new pstd.vector<int>(new int[] { 0, 1, 3, 0, 3, 2 }), new pstd.vector<Point3f>(p),
                                                          new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(), new pstd.vector<int>(), new Allocator());
                pstd.vector<Shape> list = Triangle.CreateTriangles(triangles, new Allocator());
                return new Shape[] { list[0], list[1] };
            }
            BilinearPatchMesh mesh = new BilinearPatchMesh(new Transform(), false, new pstd.vector<int>(new int[] { 0, 1, 2, 3 }), new pstd.vector<Point3f>(p),
                                                           new pstd.vector<Normal3f>(), new pstd.vector<Point2f>(), new pstd.vector<int>(), null, new Allocator());
            return new Shape[] { BilinearPatch.CreatePatches(mesh, new Allocator())[0] };
        }

        private static Vector3f RandomDirection(Random random)
        {
            double z = 1 - 2 * random.NextDouble();
            double r = Math.Sqrt(Math.Max(0, 1 - z * z));
            double phi = 2 * Math.PI * random.NextDouble();
            return new Vector3f((Float)(r * Math.Cos(phi)), (Float)(r * Math.Sin(phi)), (Float)z);
        }

        public struct Measurement
        {
            public double pdfIntegral;
            public double sampledOverQuadrature;
            public double mismatchFraction;
            public double quadrature;
        }

        // The shape from the point p (with the position error given) and the normal n: the cosine of the receiver is |dot(n, wi)|.
        private static Measurement Measure(Shape shape, Point3f[] corners, Point3f p, Vector3f error, Normal3f n, bool warp, int count, int seed, int part)
        {
            Random random = new Random(seed);
            Normal3f ns = warp ? n : new Normal3f(0, 0, 0);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p, error), ns, ns, 0);
            Measurement m = new Measurement();

            // (the quadrature and the integral of the PDF over the directions to the points of the patch: the integral over the solid angle is
            // the integral over the area of pdf * cos at the patch / distance^2)
            double estimate = 0;
            int mismatches = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                if (!ss.has_value() || ss.value().pdf == 0)
                {
                    continue;
                }
                Vector3f wi = pbrt.Normalize(ss.value().intr.p() - p);
                estimate += pbrt.AbsDot(n, wi) / ss.value().pdf;
                double other = shape.PDF(ctx, wi);
                if (Math.Abs(other - ss.value().pdf) > 1e-2 * Math.Abs(ss.value().pdf))
                {
                    ++mismatches;
                }
            }
            estimate /= count;
            m.mismatchFraction = (double)mismatches / count;

            // The irradiance by the midpoint rule over the parameters of the patch, from its corners alone (the corners are in the order p00, p10, p01, p11)
            const int cells = 400;
            const int pdfStride = 4;
            double quadrature = 0;
            double pdfSum = 0;
            Vector3f edgeU = corners[1] - corners[0], edgeV = corners[2] - corners[0];
            Vector3f planeNormal = pbrt.Normalize(pbrt.Cross(edgeU, edgeV));
            double cellArea = pbrt.Length(pbrt.Cross(edgeU, edgeV)) / ((double)cells * cells);
            for (int i = 0; i < cells; ++i)
            {
                for (int j = 0; j < cells; ++j)
                {
                    // (the part of the rectangle that is the triangle: -1 is all of it, 0 is the half where v < u, 1 the half where v > u)
                    double weight = part < 0 ? 1 : (i == j ? 0.5 : ((i > j) == (part == 0) ? 1 : 0));
                    if (weight == 0)
                    {
                        continue;
                    }
                    Point3f q = corners[0] + ((i + 0.5f) / cells) * edgeU + ((j + 0.5f) / cells) * edgeV;
                    Vector3f d = q - p;
                    Vector3f wi = pbrt.Normalize(d);
                    double cosines = pbrt.AbsDot(planeNormal, wi) / pbrt.LengthSquared(d) * cellArea * weight;
                    quadrature += pbrt.AbsDot(n, wi) * cosines;
                    if (i % pdfStride == 1 && j % pdfStride == 1)
                    {
                        pdfSum += shape.PDF(ctx, wi) * cosines * pdfStride * pdfStride;
                    }
                }
            }
            m.pdfIntegral = pdfSum;
            m.quadrature = quadrature;
            m.sampledOverQuadrature = quadrature > 0 ? estimate / quadrature : 1;
            return m;
        }

        private static void CheckReceiver(Shape shape, Point3f[] corners, Point3f p, Normal3f n, bool warp, int seed, int part, string what)
        {
            Measurement m = Measure(shape, corners, p, new Vector3f(0, 0, 0), n, warp, 60000, seed, part);
            // The irradiance by Sample() is the one by quadrature over the area, the PDF integrates to 1 (a patch seen from the point) and is the density of Sample()
            EXPECT_NEAR(1.0, m.sampledOverQuadrature, 0.015);
            if (m.pdfIntegral > 0)
            {
                EXPECT_NEAR(1.0, m.pdfIntegral, 0.02);
            }
            EXPECT_LT(m.mismatchFraction, 0.05);
            if (Math.Abs(m.sampledOverQuadrature - 1) > 0.015 || m.mismatchFraction >= 0.05 || (m.pdfIntegral > 0 && Math.Abs(m.pdfIntegral - 1) > 0.02))
            {
                Console.WriteLine("[BilinearLight] failing receiver " + what + " p=" + p + " n=" + n + " warp=" + warp + " s/q=" + m.sampledOverQuadrature.ToString("F4", CultureInfo.InvariantCulture) +
                                  " pdfInt=" + m.pdfIntegral.ToString("F4", CultureInfo.InvariantCulture) + " mismatch=" + m.mismatchFraction.ToString("E2", CultureInfo.InvariantCulture));
            }
        }

        private static void CheckAllReceivers(bool asTriangles)
        {
            // The rectangle of the light of the scenes of the animated lights, tilted a little, and receivers on a floor below it and around it, and random ones
            Vector3f ax = pbrt.Normalize(new Vector3f(0.999128209f, 0.00290597103f, -0.0416458633f));
            Vector3f ay = pbrt.Normalize(new Vector3f(0.00290597103f, 0.99031343f, 0.138819544f));
            Point3f centre = new Point3f(-2.04375f, 2.49375f, 0.127083333f);
            Point3f[] corners = Corners(centre, ax, ay, 0.6f, 0.6f);
            Shape[] shapes = MakeShapes(corners, asTriangles);
            int seed = 0;
            for (int part = 0; part < shapes.Length; ++part)
            {
                Shape shape = shapes[part];
                int partOfRectangle = asTriangles ? part : -1;
                foreach (float z in new float[] { -6, -1, -0.3f, 0.0f, 0.1f, 0.3f, 1, 6 })
                {
                    foreach (float x in new float[] { -5, -2, 3 })
                    {
                        Point3f p = new Point3f(x, 0, z);
                        CheckReceiver(shape, corners, p, new Normal3f(0, 1, 0), true, ++seed, partOfRectangle, "floor");
                        CheckReceiver(shape, corners, p, new Normal3f(0, 1, 0), false, ++seed, partOfRectangle, "floor, no warp");
                    }
                }
                Random random = new Random(3);
                for (int i = 0; i < 16; ++i)
                {
                    Point3f p = centre + new Vector3f((Float)(random.NextDouble() * 6 - 3), (Float)(random.NextDouble() * 6 - 3), (Float)((random.NextDouble() < 0.5 ? -1 : 1) * (0.05 + random.NextDouble() * 3)));
                    CheckReceiver(shape, corners, p, (Normal3f)RandomDirection(random), true, ++seed, partOfRectangle, "random");
                }
            }
        }

        [TEST("BilinearLight", "RectangleSamplingMatchesQuadratureAndPDF")]
        public static void RectangleSamplingMatchesQuadratureAndPDF()
        {
            CheckAllReceivers(false);
        }

        [TEST("BilinearLight", "TrianglePairControlMatchesQuadratureAndPDF")]
        public static void TrianglePairControlMatchesQuadratureAndPDF()
        {
            CheckAllReceivers(true);
        }

        // The estimate of sum |cos| / pdf at a point on the patch (sampled from the point given, with the error bounds of a point where a ray hit the patch)
        private static double SelfIrradiance(Shape shape, Point3f p, Vector3f error, int count)
        {
            Random random = new Random(11);
            Normal3f n = new Normal3f(0, 0, 1);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(p, error), n, n, 0);
            double estimate = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)random.NextDouble(), (Float)random.NextDouble()));
                if (!ss.has_value() || ss.value().pdf == 0)
                {
                    continue;
                }
                estimate += pbrt.AbsDot(n, pbrt.Normalize(ss.value().intr.p() - p)) / ss.value().pdf;
            }
            return estimate / count;
        }

        // A point on the emitter itself (the emitter seen by the camera has a BSDF, and the light sampling is made from its points): the patch sends no light to its own plane.
        // Before the fix the point, a few ulp from the plane, was sampled as one a hair above the patch, which sees the hemisphere: the estimate was pi.
        [TEST("BilinearLight", "ReferencePointOnThePatchReceivesNoLightFromItsOwnPlane")]
        public static void ReferencePointOnThePatchReceivesNoLightFromItsOwnPlane()
        {
            foreach (bool asTriangles in new bool[] { false, true })
            {
                Shape shape = MakeRectangle(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), new Vector3f(0, 1, 0), 0.6f, 0.6f, asTriangles)[0];
                Vector3f error = new Vector3f(1e-6f, 1e-6f, 1e-6f);
                foreach (float height in new float[] { 0, 1e-8f, -1e-8f, 3e-7f, -3e-7f })
                {
                    foreach (float x in new float[] { 0, 0.3f, 0.55f })
                    {
                        EXPECT_LT(SelfIrradiance(shape, new Point3f(x, 0.1f, height), error, 20000), 0.01);
                    }
                }
            }
        }

        // The point a visible distance above the patch is not on it: it sees the patch as a rectangle (the hemisphere when it is close), as before
        [TEST("BilinearLight", "ReferencePointAboveThePatchStillSeesIt")]
        public static void ReferencePointAboveThePatchStillSeesIt()
        {
            Shape shape = MakeRectangle(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), new Vector3f(0, 1, 0), 0.6f, 0.6f, false)[0];
            Vector3f error = new Vector3f(1e-6f, 1e-6f, 1e-6f);
            foreach (float height in new float[] { 1e-3f, -1e-3f, 1e-2f })
            {
                EXPECT_NEAR(Math.PI, SelfIrradiance(shape, new Point3f(0, 0.1f, height), error, 20000), 0.05 * Math.PI);
            }
        }

        // (a measurement, not a check) The time of Sample(ctx, u) and of PDF(ctx, wi) of a rectangular patch, for receivers around it that are not on its plane (the common case)
        [TEST("BilinearLight", "BenchmarkSampleAndPDF")]
        public static void BenchmarkSampleAndPDF()
        {
            Shape shape = MakeRectangle(new Point3f(0, 2, 0), new Vector3f(1, 0, 0), new Vector3f(0, 0, 1), 0.6f, 0.6f, false)[0];
            const int calls = 200000;
            const int runs = 9;
            Random random = new Random(1);
            ShapeSampleContext[] contexts = new ShapeSampleContext[1024];
            Point2f[] us = new Point2f[1024];
            Vector3f[] directions = new Vector3f[1024];
            for (int i = 0; i < 1024; ++i)
            {
                Point3f p = new Point3f((Float)(random.NextDouble() * 8 - 4), (Float)(random.NextDouble() * 0.5), (Float)(random.NextDouble() * 8 - 4));
                contexts[i] = new ShapeSampleContext(new Point3fi(p, new Vector3f(1e-6f, 1e-6f, 1e-6f)), new Normal3f(0, 1, 0), new Normal3f(0, 1, 0), 0);
                us[i] = new Point2f((Float)random.NextDouble(), (Float)random.NextDouble());
                pstd.optional<ShapeSample> ss = shape.Sample(contexts[i], us[i]);
                directions[i] = ss.has_value() ? pbrt.Normalize(ss.value().intr.p() - p) : new Vector3f(0, 1, 0);
            }
            double bestSample = double.MaxValue, bestPdf = double.MaxValue;
            double sink = 0;
            for (int run = 0; run < runs; ++run)
            {
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < calls; ++i)
                {
                    pstd.optional<ShapeSample> ss = shape.Sample(contexts[i & 1023], us[(i * 7) & 1023]);
                    sink += ss.has_value() ? ss.value().pdf : 0;
                }
                bestSample = Math.Min(bestSample, watch.Elapsed.TotalMilliseconds * 1e6 / calls);
                watch.Restart();
                for (int i = 0; i < calls; ++i)
                {
                    sink += shape.PDF(contexts[i & 1023], directions[i & 1023]);
                }
                bestPdf = Math.Min(bestPdf, watch.Elapsed.TotalMilliseconds * 1e6 / calls);
            }
            Console.WriteLine("[BilinearLight] Sample(ctx, u) " + bestSample.ToString("F1", CultureInfo.InvariantCulture) + " ns, PDF(ctx, wi) " + bestPdf.ToString("F1", CultureInfo.InvariantCulture) +
                              " ns (best of " + runs + " runs of " + calls + " calls; checksum " + sink.ToString("E3", CultureInfo.InvariantCulture) + ")");
        }
    }
}
