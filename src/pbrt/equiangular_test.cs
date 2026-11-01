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

        // The point on a triangle light is chosen uniformly by area, with the density 1 / area wherever the segment starts
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
            EXPECT_NEAR(0.5f, light.PdfArea(), 1e-6f);
            Vector3f mean = new Vector3f(0, 0, 0);
            int n = 20000;
            for (int i = 0; i < n; ++i)
            {
                EXPECT_TRUE(light.SampleArea(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), out Interaction y, out Float pdfArea));
                EXPECT_NEAR(light.PdfArea(), pdfArea, 1e-5f);
                mean += new Vector3f(y.p().x, y.p().y, y.p().z) / n;
            }
            // Uniform by area: the mean of the points is the centroid (0, -1/3, 0)
            EXPECT_NEAR(0.0f, mean.x, 0.02f);
            EXPECT_NEAR(-1.0f / 3, mean.y, 0.02f);
            EXPECT_NEAR(0.0f, mean.z, 0.02f);
        }
    }
}
