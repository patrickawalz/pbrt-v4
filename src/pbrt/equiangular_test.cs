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
    }
}
