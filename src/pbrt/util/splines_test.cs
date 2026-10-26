// splines_test.cpp

using System;
using testing;

namespace pbrt
{
    public static unsafe class SplinesTests
    {
        [TEST("Spline", "BezierBounds")]
        public static void Spline_BezierBounds()
        {
            // Simple
            Point3f[] cp = new Point3f[4];
            Bounds3f b = BoundCubicBezier(pstd.MakeConstSpan(cp), 0f, 1f);
            b = Expand(b, 1e-3f * Length(b.Diagonal()));
            for (Float u = 0; u <= 1f; u += 1f / 1024f)
            {
                Point3f p = EvaluateCubicBezier(pstd.MakeConstSpan(cp), u);
                EXPECT_TRUE(Inside(p, b), p + " @ u = " + u + " not in " + b);
            }

            // Randoms...
            RNG rng = new RNG();
            for (int i = 0; i < 1000; ++i)
            {
                for (int j = 0; j < 4; ++j)
                    for (int c = 0; c < 3; ++c)
                        cp[j][c] = -5f + 10f * rng.Uniform<Float>();

                Bounds3f b2 = BoundCubicBezier(pstd.MakeConstSpan(cp), 0f, 1f);
                b2 = Expand(b2, 1e-3f * Length(b2.Diagonal()));
                for (Float u = 0; u <= 1f; u += 1f / 1024f)
                {
                    Point3f p = EvaluateCubicBezier(pstd.MakeConstSpan(cp), u);
                    EXPECT_TRUE(Inside(p, b2), p + " @ u = " + u + " not in " + b2);
                }
            }
        }
    }
}
