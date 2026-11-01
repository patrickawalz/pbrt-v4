// (not in pbrt-v4) refractiveconnection_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class RefractiveConnectionTests
    {
        static RefractiveConnectionOptions Options(Float step)
        {
            RefractiveConnectionOptions options = new RefractiveConnectionOptions();
            options.stepSize = step;
            options.tolerance = 1e-3f;
            options.maxLength = 20.0f;
            return options;
        }

        // The error of the closest approach and its derivative with respect to w match a finite difference of the error
        [TEST("RefractiveConnection", "ErrorDerivativeMatchesFiniteDifferences")]
        public static void RefractiveConnection_ErrorDerivativeMatchesFiniteDifferences()
        {
            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), 1.5f, 2.0f);
            Point3f start = new Point3f(-1.2f, 0.1f, -0.9f);
            Point3f target = new Point3f(0.5f, 0.3f, 1.1f);
            RefractiveConnectionOptions options = Options(0.03f);
            Vector3f[] directions = new Vector3f[] { target - start, new Vector3f(1.6f, 0.1f, 2.1f), new Vector3f(1.9f, 0.5f, 1.8f) };
            for (int d = 0; d < directions.Length; ++d)
            {
                Vector3f w = pbrt.Normalize(directions[d]);
                EXPECT_TRUE(RefractiveConnection.TraceToClosestApproach(lens, start, w, target, options, out SymplecticRayJacobian end, out Float length));
                RefractiveConnection.ErrorAndDerivative(end, target, out Vector3f error, out SquareMatrix3 derivative);
                // At the closest approach the error is orthogonal to the optical velocity
                EXPECT_NEAR(0.0f, pbrt.Dot(error, end.v), 2e-5f);
                Float delta = 0.01f;
                for (int j = 0; j < 3; ++j)
                {
                    Vector3f wPlus = w, wMinus = w;
                    wPlus[j] += delta;
                    wMinus[j] -= delta;
                    EXPECT_TRUE(RefractiveConnection.TraceToClosestApproach(lens, start, wPlus, target, options, out SymplecticRayJacobian endPlus, out Float lengthPlus));
                    EXPECT_TRUE(RefractiveConnection.TraceToClosestApproach(lens, start, wMinus, target, options, out SymplecticRayJacobian endMinus, out Float lengthMinus));
                    for (int i = 0; i < 3; ++i)
                    {
                        Float finiteDifference = ((endPlus.p[i] - target[i]) - (endMinus.p[i] - target[i])) / (2 * delta);
                        EXPECT_NEAR(finiteDifference, derivative[i][j], 4e-4f);
                    }
                }
            }
        }

        // In a constant index the path is the straight line
        [TEST("RefractiveConnection", "ConstantIndexGivesTheStraightLine")]
        public static void RefractiveConnection_ConstantIndexGivesTheStraightLine()
        {
            LinearRefractiveIndexField field = new LinearRefractiveIndexField(1.3f, new Vector3f(0, 0, 0));
            Point3f start = new Point3f(0.2f, -0.4f, 0.1f);
            Point3f target = new Point3f(1.5f, 0.9f, 2.0f);
            Vector3f straight = target - start;
            RefractiveConnectionOptions options = Options(0.5f);
            EXPECT_TRUE(RefractiveConnection.Solve(field, start, target, new Vector3f(0.3f, 0.2f, 1.0f), options, out RefractiveConnectionPath path));
            Vector3f expected = pbrt.Normalize(straight);
            EXPECT_NEAR(expected.x, path.startDirection.x, 4e-4f);
            EXPECT_NEAR(expected.y, path.startDirection.y, 4e-4f);
            EXPECT_NEAR(expected.z, path.startDirection.z, 4e-4f);
            EXPECT_NEAR(expected.x, path.endDirection.x, 4e-4f);
            EXPECT_NEAR(expected.y, path.endDirection.y, 4e-4f);
            EXPECT_NEAR(expected.z, path.endDirection.z, 4e-4f);
            EXPECT_NEAR(pbrt.Length(straight), path.length, 1e-3f);
            EXPECT_LT(path.distance, 1e-3f);
        }

        // A connection through the Luneburg lens is a path that, traced again by the plain integrator, ends at the target
        [TEST("RefractiveConnection", "LuneburgConnectionReachesTheTarget")]
        public static void RefractiveConnection_LuneburgConnectionReachesTheTarget()
        {
            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), 1.5f, 2.0f);
            Point3f start = new Point3f(-1.2f, 0.1f, -0.9f);
            Point3f target = new Point3f(0.5f, 0.3f, 1.1f);
            RefractiveConnectionOptions options = Options(0.03f);
            EXPECT_TRUE(RefractiveConnection.Solve(lens, start, target, target - start, options, out RefractiveConnectionPath path));
            EXPECT_LT(path.distance, 1e-3f);
            SymplecticRay ray = new SymplecticRay(lens, start, path.startDirection);
            ray.Advance(lens, path.length, 0.03f);
            EXPECT_NEAR(target.x, ray.p.x, 6e-4f);
            EXPECT_NEAR(target.y, ray.p.y, 6e-4f);
            EXPECT_NEAR(target.z, ray.p.z, 6e-4f);
            Vector3f direction = ray.Direction();
            EXPECT_NEAR(path.endDirection.x, direction.x, 2e-6f);
            EXPECT_NEAR(path.endDirection.y, direction.y, 2e-6f);
            EXPECT_NEAR(path.endDirection.z, direction.z, 2e-6f);
            // The lens bends the path, so it is longer than the straight line and leaves a different direction
            EXPECT_GT(path.length, pbrt.Length(target - start));
        }

        // A path that leaves the region is a failure
        [TEST("RefractiveConnection", "RejectsPathsLeavingTheRegion")]
        public static void RefractiveConnection_RejectsPathsLeavingTheRegion()
        {
            LinearRefractiveIndexField field = new LinearRefractiveIndexField(1.3f, new Vector3f(0, 0, 0));
            Point3f start = new Point3f(0, 0, 0);
            Point3f target = new Point3f(0, 0, 3);
            RefractiveConnectionOptions options = Options(0.25f);
            EXPECT_TRUE(RefractiveConnection.Solve(field, start, target, new Vector3f(0.1f, 0, 1), options, out RefractiveConnectionPath path));
            options.inside = (Point3f p) => p.z < 2.0f;
            EXPECT_FALSE(RefractiveConnection.Solve(field, start, target, new Vector3f(0.1f, 0, 1), options, out path));
        }

        // With one path the estimate is unbiased: the mean of (found ? count : 0) is one, although about half of the searches
        // point away from the target and fail
        [TEST("RefractiveConnection", "SinglePathEstimateIsUnbiased")]
        public static void RefractiveConnection_SinglePathEstimateIsUnbiased()
        {
            LinearRefractiveIndexField field = new LinearRefractiveIndexField(1.3f, new Vector3f(0, 0, 0));
            Point3f start = new Point3f(0, 0, 0);
            Point3f target = new Point3f(0.5f, 0.2f, 1.5f);
            RefractiveConnectionOptions options = Options(0.5f);
            options.maxLength = 8.0f;
            RNG rng = new RNG(3);
            int trials = 1500;
            double sum = 0;
            int found = 0;
            for (int i = 0; i < trials; ++i)
            {
                if (RefractiveConnection.SolveMultiple(field, start, target, options, ref rng, out RefractiveConnectionPath path, out int count))
                {
                    EXPECT_FALSE(path.truncated);
                    sum += count;
                    ++found;
                }
            }
            // Measured 0.977 with 689 of 1500 found; the standard deviation of the mean is about 0.04
            EXPECT_NEAR(1.0, sum / trials, 0.2);
            EXPECT_GT(found, trials / 4);
            EXPECT_LT(found, 3 * trials / 4);
        }

        // The area Jacobian of a connection is that of the integrator: the end positions of paths whose start directions differ by a
        // small angle span a parallelogram whose area, perpendicular to the end direction, is dA / d(omega). In a constant index it
        // is the squared distance.
        [TEST("RefractiveConnection", "AreaJacobianMatchesFiniteDifferences")]
        public static void RefractiveConnection_AreaJacobianMatchesFiniteDifferences()
        {
            LinearRefractiveIndexField constant = new LinearRefractiveIndexField(1.3f, new Vector3f(0, 0, 0));
            Point3f start = new Point3f(0.2f, -0.4f, 0.1f);
            Point3f target = new Point3f(1.5f, 0.9f, 2.0f);
            RefractiveConnectionOptions options = Options(0.5f);
            EXPECT_TRUE(RefractiveConnection.Solve(constant, start, target, new Vector3f(0.3f, 0.2f, 1.0f), options, out RefractiveConnectionPath straight));
            // Measured: 1e-6
            EXPECT_NEAR(pbrt.LengthSquared(target - start), straight.areaJacobian, 1e-5f);

            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), 1.5f, 2.0f);
            Point3f x = new Point3f(-0.3f, 0.1f, 0.2f);
            Point3f[] targets = new Point3f[] { new Point3f(3.0f, 0.4f, -0.2f), new Point3f(2.5f, -1.0f, 1.2f) };
            for (int t = 0; t < targets.Length; ++t)
            {
                RefractiveConnectionOptions lensOptions = Options(0.03f);
                EXPECT_TRUE(RefractiveConnection.Solve(lens, x, targets[t], targets[t] - x, lensOptions, out RefractiveConnectionPath path));
                Vector3f axis = Math.Abs(path.startDirection.x) < 0.6f ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0);
                Vector3f e1 = pbrt.Normalize(pbrt.Cross(path.startDirection, axis));
                Vector3f e2 = pbrt.Cross(path.startDirection, e1);
                Float delta = 0.002f;
                Vector3f[] columns = new Vector3f[2];
                Vector3f[] axes = new Vector3f[] { e1, e2 };
                for (int k = 0; k < 2; ++k)
                {
                    SymplecticRay plus = new SymplecticRay(lens, x, path.startDirection + delta * axes[k]);
                    SymplecticRay minus = new SymplecticRay(lens, x, path.startDirection - delta * axes[k]);
                    plus.Advance(lens, path.length, 0.03f);
                    minus.Advance(lens, path.length, 0.03f);
                    columns[k] = (1 / (2 * delta)) * (plus.p - minus.p);
                }
                Float expected = Math.Abs(pbrt.Dot(pbrt.Cross(columns[0], columns[1]), path.endDirection));
                // The finite difference differs by 4e-4 at most (8.69 and 8.33 against 11.1 and 10.0 for the straight line)
                EXPECT_NEAR(expected, path.areaJacobian, 2e-3f);
            }
        }
    }
}
