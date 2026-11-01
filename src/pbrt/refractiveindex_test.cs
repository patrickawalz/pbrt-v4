// (not in pbrt-v4) refractiveindex_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class RefractiveIndexTests
    {
        // A smooth field with a known gradient and Hessian: n = 1.2 + 0.2 exp(-|p - c|^2 / (2 sigma^2))
        static void Bump(Point3f p, out Float value, out Vector3f gradient, out SquareMatrix3 hessian)
        {
            Vector3f offset = p - new Point3f(0.1f, -0.05f, 0.0f);
            Float sigma2 = 0.35f * 0.35f;
            Float bump = 0.2f * (Float)Math.Exp(-pbrt.Dot(offset, offset) / (2 * sigma2));
            value = 1.2f + bump;
            gradient = (-bump / sigma2) * offset;
            hessian = (bump / (sigma2 * sigma2)) * RefractiveIndexField.OuterProduct(offset, offset) +
                      (-bump / sigma2) * new SquareMatrix3();
        }

        internal static GridRefractiveIndexField BumpGrid()
        {
            int resolution = 41;
            Float[] values = new Float[resolution * resolution * resolution];
            for (int z = 0; z < resolution; ++z)
            {
                for (int y = 0; y < resolution; ++y)
                {
                    for (int x = 0; x < resolution; ++x)
                    {
                        Point3f p = new Point3f(-1 + 2 * x / 40.0f, -1 + 2 * y / 40.0f, -1 + 2 * z / 40.0f);
                        Bump(p, out Float value, out Vector3f gradient, out SquareMatrix3 hessian);
                        values[(z * resolution + y) * resolution + x] = value;
                    }
                }
            }
            return new GridRefractiveIndexField(new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)),
                                                resolution, resolution, resolution, values);
        }

        // The spline goes through a smooth field with the value, the gradient and the Hessian of the field
        [TEST("GridRefractiveIndexField", "FollowsASmoothField")]
        public static void GridRefractiveIndexField_FollowsASmoothField()
        {
            GridRefractiveIndexField grid = BumpGrid();
            RNG rng = new RNG(5);
            for (int i = 0; i < 200; ++i)
            {
                Point3f p = new Point3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
                Bump(p, out Float expectedValue, out Vector3f expectedGradient, out SquareMatrix3 expectedHessian);
                grid.ValueGradientAndHessian(p, out Float value, out Vector3f gradient, out SquareMatrix3 hessian);
                // Measured over these points: value 1.5e-6, gradient 3.7e-5, Hessian 6.3e-3 (the Hessian of a cubic spline is of the second order)
                EXPECT_NEAR(expectedValue, value, 1e-5f);
                for (int a = 0; a < 3; ++a)
                {
                    EXPECT_NEAR(expectedGradient[a], gradient[a], 2e-4f);
                    for (int b = 0; b < 3; ++b)
                    {
                        EXPECT_NEAR(expectedHessian[a][b], hessian[a][b], 3e-2f);
                    }
                }
                // The value and the gradient without the Hessian are the same
                grid.ValueAndGradient(p, out Float valueOnly, out Vector3f gradientOnly);
                EXPECT_EQ(value, valueOnly);
                EXPECT_EQ(gradient.x, gradientOnly.x);
                EXPECT_EQ(gradient.y, gradientOnly.y);
                EXPECT_EQ(gradient.z, gradientOnly.z);
            }
        }

        // A linear field is reproduced exactly, with the grid spacing different along each axis, and continues linearly outside it
        [TEST("GridRefractiveIndexField", "LinearFieldIsExactAlsoOutside")]
        public static void GridRefractiveIndexField_LinearFieldIsExactAlsoOutside()
        {
            int resolutionX = 5, resolutionY = 6, resolutionZ = 3;
            Bounds3f bounds = new Bounds3f(new Point3f(0, 0, -1), new Point3f(1, 2, 1));
            Float[] values = new Float[resolutionX * resolutionY * resolutionZ];
            for (int z = 0; z < resolutionZ; ++z)
            {
                for (int y = 0; y < resolutionY; ++y)
                {
                    for (int x = 0; x < resolutionX; ++x)
                    {
                        Float px = x / (Float)(resolutionX - 1);
                        Float py = 2 * y / (Float)(resolutionY - 1);
                        Float pz = -1 + 2 * z / (Float)(resolutionZ - 1);
                        values[(z * resolutionY + y) * resolutionX + x] = 1.2f + 0.1f * px - 0.05f * py + 0.2f * pz;
                    }
                }
            }
            GridRefractiveIndexField grid = new GridRefractiveIndexField(bounds, resolutionX, resolutionY, resolutionZ, values);
            Point3f[] points = new Point3f[] { new Point3f(0.3f, 0.7f, 0.2f), new Point3f(0.99f, 1.9f, -0.9f),
                                               new Point3f(-0.5f, 3, 2), new Point3f(2.5f, -1, -3) };
            for (int i = 0; i < points.Length; ++i)
            {
                Point3f p = points[i];
                grid.ValueGradientAndHessian(p, out Float value, out Vector3f gradient, out SquareMatrix3 hessian);
                EXPECT_NEAR(1.2f + 0.1f * p.x - 0.05f * p.y + 0.2f * p.z, value, 2e-5f);
                EXPECT_NEAR(0.1f, gradient.x, 2e-5f);
                EXPECT_NEAR(-0.05f, gradient.y, 2e-5f);
                EXPECT_NEAR(0.2f, gradient.z, 2e-5f);
                for (int a = 0; a < 3; ++a)
                {
                    for (int b = 0; b < 3; ++b)
                    {
                        EXPECT_NEAR(0.0f, hessian[a][b], 2e-4f);
                    }
                }
            }
        }

        // The gradient of the Luneburg field is the derivative of its value and the Hessian the derivative of the gradient
        // (for the exponents of the paper's figure 7), and the lens is index 1 outside
        [TEST("LuneburgRefractiveIndexField", "DerivativesAreConsistent")]
        public static void LuneburgRefractiveIndexField_DerivativesAreConsistent()
        {
            RNG rng = new RNG(9);
            Point3f center = new Point3f(0.2f, -0.1f, 0.3f);
            Float[] exponents = new Float[] { 1.5f, 2.0f, 3.0f };
            Float delta = 1e-3f;
            for (int e = 0; e < exponents.Length; ++e)
            {
                LuneburgRefractiveIndexField field = new LuneburgRefractiveIndexField(center, 1.5f, exponents[e]);
                for (int i = 0; i < 40; ++i)
                {
                    // A point at the distance 0.3 to 1.3 from the center, in a random direction
                    Vector3f direction = pbrt.Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f,
                                                                      rng.Uniform<Float>() - 0.5f));
                    Point3f p = center + (0.3f + rng.Uniform<Float>()) * direction;
                    field.ValueGradientAndHessian(p, out Float value, out Vector3f gradient, out SquareMatrix3 hessian);
                    for (int a = 0; a < 3; ++a)
                    {
                        Point3f plus = p, minus = p;
                        plus[a] += delta;
                        minus[a] -= delta;
                        field.ValueGradientAndHessian(plus, out Float valuePlus, out Vector3f gradientPlus, out SquareMatrix3 hessianPlus);
                        field.ValueGradientAndHessian(minus, out Float valueMinus, out Vector3f gradientMinus, out SquareMatrix3 hessianMinus);
                        // The finite differences differ by 1.1e-4 (gradient) and 7e-5 (Hessian) at most
                        EXPECT_NEAR((valuePlus - valueMinus) / (2 * delta), gradient[a], 6e-4f);
                        for (int b = 0; b < 3; ++b)
                        {
                            EXPECT_NEAR((gradientPlus[b] - gradientMinus[b]) / (2 * delta), hessian[b][a], 4e-4f);
                        }
                    }
                }
            }

            // The Luneburg lens: n = sqrt(2 - (r / R)^2), with 1 at the surface and outside of it
            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), 2.0f, 2.0f);
            lens.ValueAndGradient(new Point3f(0, 0, 0), out Float atCenter, out Vector3f gradientAtCenter);
            EXPECT_NEAR(Math.Sqrt(2.0), atCenter, 1e-5);
            EXPECT_NEAR(0.0f, pbrt.Length(gradientAtCenter), 1e-5f);
            lens.ValueAndGradient(new Point3f(1, 0, 0), out Float inside, out Vector3f gradientInside);
            EXPECT_NEAR(Math.Sqrt(2 - 0.25), inside, 1e-5);
            // -x / (R^2 n)
            EXPECT_NEAR(-1 / (4 * Math.Sqrt(2 - 0.25)), gradientInside.x, 1e-5);
            lens.ValueAndGradient(new Point3f(0, 3, 0), out Float outside, out Vector3f gradientOutside);
            EXPECT_EQ(1.0f, outside);
            EXPECT_EQ(0.0f, pbrt.Length(gradientOutside));
        }
    }
}
