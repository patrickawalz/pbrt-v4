// (not in pbrt-v4) refractiveray_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class RefractiveRayTests
    {
        // Without a gradient the path is a straight line, at the speed n
        [TEST("SymplecticRay", "StraightInAConstantIndex")]
        public static void SymplecticRay_StraightInAConstantIndex()
        {
            LinearRefractiveIndexField field = new LinearRefractiveIndexField(1.5f, new Vector3f(0, 0, 0));
            Vector3f direction = pbrt.Normalize(new Vector3f(1, 2, -0.5f));
            SymplecticRay ray = new SymplecticRay(field, new Point3f(0.5f, 0, 1), direction);
            ray.Advance(field, 10.0f, 0.5f);
            Point3f expected = new Point3f(0.5f, 0, 1) + 10.0f * direction;
            EXPECT_NEAR(expected.x, ray.p.x, 1e-4f);
            EXPECT_NEAR(expected.y, ray.p.y, 1e-4f);
            EXPECT_NEAR(expected.z, ray.p.z, 1e-4f);
            EXPECT_NEAR(1.5f, pbrt.Length(ray.v), 1e-5f);
            Vector3f travelled = ray.Direction();
            EXPECT_NEAR(direction.x, travelled.x, 1e-5f);
            EXPECT_NEAR(direction.y, travelled.y, 1e-5f);
            EXPECT_NEAR(direction.z, travelled.z, 1e-5f);
        }

        // In n = n0 + g z the path has the closed form n(x) = C cosh(g x / C + a), with C = n0 sin(theta) the invariant of the
        // path and a = acosh(n0 / C). The error of the integrator falls with the step, and the optical velocity keeps the
        // length of the index.
        [TEST("SymplecticRay", "ConstantGradientFollowsTheCatenary")]
        public static void SymplecticRay_ConstantGradientFollowsTheCatenary()
        {
            Float n0 = 1.2f, slope = 0.3f;
            double theta = 40.0 * Math.PI / 180.0;
            LinearRefractiveIndexField field = new LinearRefractiveIndexField(n0, new Vector3f(0, 0, slope));
            double invariant = n0 * Math.Sin(theta);
            double a = Math.Acosh(n0 / invariant);
            Float[] stepSizes = new Float[] { 0.04f, 0.01f };
            Float[] pathErrors = new Float[2];
            for (int s = 0; s < 2; ++s)
            {
                SymplecticRay ray = new SymplecticRay(field, new Point3f(0, 0, 0),
                                                      new Vector3f((Float)Math.Sin(theta), 0, (Float)Math.Cos(theta)));
                int steps = (int)(2.0f / stepSizes[s]);
                Float maxPathError = 0, maxVelocityError = 0, maxInvariantError = 0;
                for (int i = 0; i < steps; ++i)
                {
                    ray.Step(field, stepSizes[s]);
                    double exactZ = (invariant * Math.Cosh(slope * ray.p.x / invariant + a) - n0) / slope;
                    maxPathError = Math.Max(maxPathError, (Float)Math.Abs(ray.p.z - exactZ));
                    maxVelocityError = Math.Max(maxVelocityError, Math.Abs(pbrt.Length(ray.v) - (n0 + slope * ray.p.z)));
                    // The gradient is along z, so the transverse part of v does not change
                    maxInvariantError = Math.Max(maxInvariantError, Math.Abs(ray.v.x - (Float)invariant) + Math.Abs(ray.v.y));
                }
                pathErrors[s] = maxPathError;
                // The error of the step is of the first order: 0.036 s measured, for the path and for |v| - n
                EXPECT_LT(maxPathError, 0.05f * stepSizes[s]);
                EXPECT_LT(maxVelocityError, 0.05f * stepSizes[s]);
                EXPECT_LT(maxInvariantError, 1e-6f);
            }
            // A step a quarter as long has an error less than half as large
            EXPECT_LT(pathErrors[1], 0.5f * pathErrors[0]);
        }

        // The Luneburg lens focuses a parallel beam to the point of its surface on the other side
        [TEST("SymplecticRay", "LuneburgLensFocusesAParallelBeam")]
        public static void SymplecticRay_LuneburgLensFocusesAParallelBeam()
        {
            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), 1.0f, 2.0f);
            Float[] heights = new Float[] { 0.0f, 0.3f, 0.6f, 0.9f };
            for (int h = 0; h < heights.Length; ++h)
            {
                // The ray enters the lens on its surface, where the index is 1, travelling along +z
                SymplecticRay ray = new SymplecticRay(lens, new Point3f(heights[h], 0, -(Float)Math.Sqrt(1 - heights[h] * heights[h])),
                                                      new Vector3f(0, 0, 1));
                SymplecticRay previous = ray;
                ray.Step(lens, 0.005f);
                int steps = 1;
                while (pbrt.Length(new Vector3f(ray.p.x, ray.p.y, ray.p.z)) < 1.0f && steps < 4000)
                {
                    previous = ray;
                    ray.Step(lens, 0.005f);
                    ++steps;
                }
                EXPECT_LT(steps, 4000);
                // Where the path leaves the sphere: the interpolation between the two steps
                Float r0 = pbrt.Length(new Vector3f(previous.p.x, previous.p.y, previous.p.z));
                Float r1 = pbrt.Length(new Vector3f(ray.p.x, ray.p.y, ray.p.z));
                Float fraction = (1.0f - r0) / (r1 - r0);
                Point3f exit = previous.p + fraction * (ray.p - previous.p);
                // 2.6e-5 measured with this step
                EXPECT_NEAR(0.0f, exit.x, 2e-4f);
                EXPECT_NEAR(0.0f, exit.y, 2e-4f);
                EXPECT_NEAR(1.0f, exit.z, 2e-4f);
            }
        }

        // The derivatives of the end point of a path with respect to its initial direction are those of the integrator: a finite
        // difference of the integrated position and optical velocity matches them, for the analytic field and for the spline
        static void CheckJacobian(RefractiveIndexField field, Point3f start, Vector3f w, int steps, Float stepSize, Float tolerance)
        {
            SymplecticRayJacobian jacobian = new SymplecticRayJacobian(field, start, w);
            for (int i = 0; i < steps; ++i)
            {
                jacobian.Step(field, stepSize);
            }
            // The Jacobian walks the same path as the ray of the same initial direction
            SymplecticRay reference = new SymplecticRay(field, start, w);
            reference.Advance(field, steps * stepSize, stepSize);
            EXPECT_NEAR(reference.p.x, jacobian.p.x, 1e-4f);
            EXPECT_NEAR(reference.p.y, jacobian.p.y, 1e-4f);
            EXPECT_NEAR(reference.p.z, jacobian.p.z, 1e-4f);

            Float delta = 0.01f;
            for (int j = 0; j < 3; ++j)
            {
                Vector3f wPlus = w, wMinus = w;
                wPlus[j] += delta;
                wMinus[j] -= delta;
                SymplecticRay plus = new SymplecticRay(field, start, wPlus);
                SymplecticRay minus = new SymplecticRay(field, start, wMinus);
                for (int i = 0; i < steps; ++i)
                {
                    plus.Step(field, stepSize);
                    minus.Step(field, stepSize);
                }
                for (int i = 0; i < 3; ++i)
                {
                    EXPECT_NEAR((plus.p[i] - minus.p[i]) / (2 * delta), jacobian.dpdw[i][j], tolerance);
                    EXPECT_NEAR((plus.v[i] - minus.v[i]) / (2 * delta), jacobian.dvdw[i][j], tolerance);
                }
            }
        }

        [TEST("SymplecticRayJacobian", "MatchesFiniteDifferences")]
        public static void SymplecticRayJacobian_MatchesFiniteDifferences()
        {
            LuneburgRefractiveIndexField lens = new LuneburgRefractiveIndexField(new Point3f(0, 0, 0), 1.5f, 2.0f);
            // The largest entries of the derivatives are about 0.8, and the finite difference differs by 6e-5 at most
            CheckJacobian(lens, new Point3f(0.3f, -0.2f, -0.6f), new Vector3f(0.2f, 0.1f, 1.0f), 30, 0.03f, 3e-4f);
            CheckJacobian(RefractiveIndexTests.BumpGrid(), new Point3f(-0.3f, 0.1f, -0.4f), new Vector3f(1.0f, 0.2f, 0.3f), 30, 0.03f, 3e-4f);
        }
    }
}
