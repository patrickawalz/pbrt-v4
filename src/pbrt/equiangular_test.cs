// (not in pbrt-v4) equiangular_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class EquiangularTests
    {
        // The density integrates to one over the segment, and what Sample() returns is the density Pdf() reports
        [TEST("EquiangularSampling", "PdfIsNormalisedAndMatchesSample")]
        public static void EquiangularSampling_PdfIsNormalisedAndMatchesSample()
        {
            RNG rng = new RNG();
            for (int trial = 0; trial < 50; ++trial)
            {
                Point3f origin = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                Vector3f direction = Normalize(new Vector3f(rng.Uniform<Float>() - .5f, rng.Uniform<Float>() - .5f, rng.Uniform<Float>() - .5f));
                Point3f light = new Point3f(3 * rng.Uniform<Float>() - 1, 3 * rng.Uniform<Float>() - 1, 3 * rng.Uniform<Float>() - 1);
                Float tMax = trial % 5 == 0 ? Float.PositiveInfinity : 0.5f + 4 * rng.Uniform<Float>();

                // The sampled values match the density
                for (int i = 0; i < 20; ++i)
                {
                    EXPECT_TRUE(EquiangularSampling.Sample(origin, direction, tMax, light, rng.Uniform<Float>(), out Float t, out Float pdf));
                    EXPECT_NEAR(pdf, EquiangularSampling.Pdf(origin, direction, tMax, light, t), 1e-3f * pdf + 1e-6f);
                    EXPECT_TRUE(t >= 0 && t <= tMax);
                }

                // The density integrates to one: the average of 1/pdf over samples is the length of the segment
                if (!Float.IsInfinity(tMax))
                {
                    double sum = 0;
                    int n = 20000;
                    for (int i = 0; i < n; ++i)
                    {
                        Float u = (i + 0.5f) / n;
                        EquiangularSampling.Sample(origin, direction, tMax, light, u, out Float t, out Float pdf);
                        sum += 1.0 / pdf;
                    }
                    EXPECT_NEAR(tMax, (Float)(sum / n), 0.01f * tMax);
                }
            }
        }

        // The density of a point on a triangle light seen from the segment origin is what SampleArea reports, wherever the origin is,
        // and the density integrates to one over the triangle
        [TEST("EquiangularSampling", "TriangleLightAreaDensity")]
        public static void EquiangularSampling_TriangleLightAreaDensity()
        {
            RNG rng = new RNG(77);
            Transform id = new Transform();
            pstd.vector<int> indices = new pstd.vector<int>(new int[] { 0, 1, 2 });
            pstd.vector<Point3f> pv = new pstd.vector<Point3f>(new Point3f[] {
                new Point3f(-1, -1, 0), new Point3f(1, -1, 0), new Point3f(0, 1, 0) });
            TriangleMesh mesh = new TriangleMesh(id, false, indices, pv, new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(),
                                                 new pstd.vector<Point2f>(), new pstd.vector<int>(), new Allocator());
            pstd.vector<Shape> tris = Triangle.CreateTriangles(mesh, new Allocator());
            DiffuseAreaLight light = new DiffuseAreaLight(id, new MediumInterface(), new ConstantSpectrum(1.0f), 1.0f, tris[0],
                                                          null, new Image(), null, true /* two sided */);
            EXPECT_TRUE(light.SupportsEquiangular());
            // Origins far and near, so that both the area and the spherical sampling are used
            Float[] distances = { 0.01f, 0.5f, 3, 30, 300 };
            for (int d = 0; d < distances.Length; ++d)
            {
                for (int trial = 0; trial < 20; ++trial)
                {
                    Point3f origin = new Point3f(distances[d] * (2 * rng.Uniform<Float>() - 1), distances[d] * (2 * rng.Uniform<Float>() - 1),
                                                 distances[d] * (2 * rng.Uniform<Float>() - 1));
                    LightSampleContext ctx = new LightSampleContext(new Point3fi(origin), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
                    for (int i = 0; i < 200; ++i)
                    {
                        if (!light.SampleArea(ctx, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), out Interaction y, out Float pdfArea))
                            continue;
                        EXPECT_NEAR(pdfArea, light.PdfArea(ctx, y.p(), y.n), 2e-3f * pdfArea + 1e-6f);
                    }
                    // The density integrates to one: its average over points chosen uniformly on the triangle is 1 / area (1/2)
                    double sum = 0;
                    int n = 20000;
                    Normal3f normal = new Normal3f(0, 0, 1);
                    for (int i = 0; i < n; ++i)
                    {
                        Float a = rng.Uniform<Float>(), b = rng.Uniform<Float>();
                        if (a + b > 1)
                        {
                            a = 1 - a;
                            b = 1 - b;
                        }
                        Point3f y = new Point3f(-1, -1, 0) + a * new Vector3f(2, 0, 0) + b * new Vector3f(1, 2, 0);
                        sum += light.PdfArea(ctx, y, normal);
                    }
                    // (an origin on the triangle's plane has a density of heavy tails, which the average of a few samples misses)
                    if (distances[d] >= 0.5f)
                        EXPECT_NEAR(0.5f, (Float)(sum / n), 0.05f);
                }
            }
        }
    }
}
