// (not in pbrt-v4) sdfprimitives_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class SdfPrimitiveTests
    {
        // An exact distance has a gradient of length 1, and the point p - f(p) grad f(p) is on the surface (the closest point):
        // both are checked at random points, with a few of them allowed to fail where the gradient jumps (the medial axis, the
        // edges). `inside` is a point of the solid, which has to be negative.
        private const int Points = 20000;

        private static void CheckExact(string expression, SdfVector inside, double extent)
        {
            CheckSide(expression, inside, extent, 0);
        }

        // side: -1 looks only at the points inside the solid, 1 only at those outside, 0 at all of them
        private static void CheckSide(string expression, SdfVector inside, double extent, int side)
        {
            Measure(expression, inside, extent, side, out int tested, out int slopes, out int landings, out int tooFar);
            if (slopes > tested / 25 || landings > tested / 100 || tooFar > 0)
            {
                Console.WriteLine("{0} (side {5}): {1} of {2} points with a slope that is not 1, {3} that do not land on the surface, {4} farther than a point of the surface",
                                  expression, slopes, tested, landings, tooFar, side);
            }
            CHECK_GT(tested, 200);
            CHECK_LE(slopes, tested / 25);
            CHECK_LE(landings, tested / 100);
            CHECK_EQ(tooFar, 0);
        }

        private static bool Fails(string expression, SdfVector inside, double extent, int side)
        {
            Measure(expression, inside, extent, side, out int tested, out int slopes, out int landings, out int tooFar);
            return slopes > tested / 25 || landings > tested / 100 || tooFar > 0;
        }

        // The sides that a field claims to be exact on are exact
        private static void CheckClaims(string expression, SdfVector inside, double extent, bool exactInside, bool exactOutside)
        {
            SdfNode field = SdfParser.Parse(expression, new SdfParseContext());
            CHECK_EQ(field.IsExactInside(), exactInside);
            CHECK_EQ(field.IsExactOutside(), exactOutside);
            if (exactInside)
            {
                CheckSide(expression, inside, extent, -1);
            }
            if (exactOutside)
            {
                CheckSide(expression, inside, extent, 1);
            }
        }

        private static void Measure(string expression, SdfVector inside, double extent, out int slopes, out int landings, out int tooFar)
        {
            Measure(expression, inside, extent, 0, out int tested, out slopes, out landings, out tooFar);
        }

        private static void Measure(string expression, SdfVector inside, double extent, int side, out int tested, out int slopes,
                                    out int landings, out int tooFar)
        {
            System.Collections.Generic.List<SdfVector> cloud = new System.Collections.Generic.List<SdfVector>();
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = extent * Math.Sqrt(3);
            SdfNode field = SdfParser.Parse(expression, context);
            CHECK_LT(field.Evaluate(inside), 0);
            CHECK_GT(field.Evaluate(new SdfVector(3 * extent, 2.7 * extent, -3.3 * extent)), 0);
            RNG rng = new RNG(11);
            int count = Points;
            tested = 0;
            slopes = 0;
            landings = 0;
            for (int i = 0; i < count; ++i)
            {
                SdfVector p = new SdfVector(extent * (2 * rng.Uniform<double>() - 1), extent * (2 * rng.Uniform<double>() - 1),
                                            extent * (2 * rng.Uniform<double>() - 1));
                double value = field.Evaluate(p);
                bool counted = side == 0 || (side < 0) == (value < 0);
                tested += counted ? 1 : 0;
                SdfVector gradient = field.Gradient(p, 1e-6);
                double slope = gradient.Length();
                if (Math.Abs(slope - 1) > 1e-3)
                {
                    slopes += counted ? 1 : 0;
                    continue;
                }
                SdfVector landed = p - gradient * (value / (slope * slope));
                if (Math.Abs(field.Evaluate(landed)) > 1e-4 * Math.Max(1.0, Math.Abs(value)))
                {
                    landings += counted ? 1 : 0;
                }
                else
                {
                    cloud.Add(landed);
                }
            }

            // The distance is never more than the distance to a point of the surface: against the points that landed on it
            tooFar = 0;
            for (int i = 0; i < 4000; ++i)
            {
                SdfVector p = new SdfVector(extent * (2 * rng.Uniform<double>() - 1), extent * (2 * rng.Uniform<double>() - 1),
                                            extent * (2 * rng.Uniform<double>() - 1));
                double signed = field.Evaluate(p);
                if (side != 0 && (side < 0) != (signed < 0))
                {
                    continue;
                }
                double value = Math.Abs(signed), nearest = double.PositiveInfinity;
                foreach (SdfVector point in cloud)
                {
                    nearest = Math.Min(nearest, (p - point).Length());
                }
                if (value > nearest + 1e-3)
                {
                    ++tooFar;
                }
            }
        }

        // The check tells a bound from an exact distance: the ellipsoid (a first-order estimate) and a smooth union fail it
        [TEST("SdfPrimitives", "CheckSeesABound")]
        public static void SdfPrimitives_CheckSeesABound()
        {
            Measure("ellipsoid(0.8, 0.3, 0.5)", new SdfVector(0, 0, 0), 1.2, out int slopes, out int landings, out int tooFar);
            CHECK_GT(slopes, Points / 25);
            Measure("smoothunion(sphere(0.5), translate(sphere(0.4), 0.6, 0, 0), 0.3)", new SdfVector(0, 0, 0), 1.2, out slopes, out landings, out tooFar);
            CHECK_GT(slopes + landings, Points / 100);
        }

        // The exact-inside and exact-outside flags: every side that a field claims is exact, and the sides that the minimum and
        // the maximum are known to get wrong are not claimed (and are seen to be wrong)
        [TEST("SdfPrimitives", "ExactSides")]
        public static void SdfPrimitives_ExactSides()
        {
            SdfVector origin = new SdfVector(0, 0, 0);
            CheckClaims("sphere(0.6)", origin, 1.2, true, true);
            CheckClaims("box(0.6, 0.4, 0.5, 0.1)", origin, 1.2, true, true);
            CheckClaims("torus(0.6, 0.2)", new SdfVector(0.6, 0, 0), 1.2, true, true);
            CheckClaims("cylinder(0.4, 0.5)", origin, 1.2, true, true);
            CheckClaims("capsule(-0.4,0,0, 0.4,0.2,0, 0.2)", origin, 1.2, true, true);
            CheckClaims("cone(0.5, 0.8)", new SdfVector(0, 0.3, 0), 1.2, true, true);
            CheckClaims("octahedron(0.7)", origin, 1.2, true, true);
            CheckClaims("ellipsoid(0.8, 0.3, 0.5)", origin, 1.2, false, false);

            string two = "sphere(0.5), translate(sphere(0.5), 0.5, 0, 0)";
            CheckClaims("union(" + two + ")", origin, 1.2, false, true);
            CHECK(Fails("union(" + two + ")", origin, 1.2, -1));
            CheckClaims("intersect(" + two + ")", new SdfVector(0.25, 0, 0), 1.2, true, false);
            CHECK(Fails("intersect(" + two + ")", new SdfVector(0.25, 0, 0), 1.2, 1));
            CheckClaims("subtract(box(0.6, 0.6, 0.6), translate(sphere(0.5), 0.6, 0.6, 0))", origin, 1.2, true, false);
            CHECK(Fails("subtract(box(0.6, 0.6, 0.6), translate(sphere(0.5), 0.6, 0.6, 0))", origin, 1.2, 1));

            CheckClaims("offset(box(0.5, 0.4, 0.3), 0.1)", origin, 1.2, false, true);
            CheckClaims("offset(box(0.5, 0.4, 0.3), -0.1)", origin, 1.2, true, false);
            CheckClaims("shell(sphere(0.6), 0.1)", new SdfVector(0.6, 0, 0), 1.2, false, true);
            CheckClaims("rotate(box(0.5, 0.4, 0.3), 20, 30, 40)", origin, 1.2, true, true);
            CheckClaims("translate(scale(box(0.5, 0.4, 0.3), 1.5), 0.1, 0, 0)", origin, 1.2, true, true);
            CheckClaims("scale(box(0.5, 0.4, 0.3), 1, 2, 1)", origin, 1.2, false, false);
            CheckClaims("label(union(" + two + "), 1)", origin, 1.2, false, true);
            CheckClaims("smoothunion(" + two + ", 0.2)", origin, 1.2, false, false);
        }

        // The closed-form gradient is the central difference, but for the few points next to a crease
        private static void CheckGradient(string expression, double extent)
        {
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = extent * Math.Sqrt(3);
            SdfNode field = SdfParser.Parse(expression, context);
            RNG rng = new RNG(5);
            int different = 0;
            double worst = 0;
            for (int i = 0; i < Points; ++i)
            {
                SdfVector p = new SdfVector(extent * (2 * rng.Uniform<double>() - 1), extent * (2 * rng.Uniform<double>() - 1),
                                            extent * (2 * rng.Uniform<double>() - 1));
                double error = (field.AnalyticGradient(p, 1e-6) - field.Gradient(p, 1e-6)).Length();
                if (error > 1e-4)
                {
                    ++different;
                    worst = Math.Max(worst, error);
                }
            }
            if (different > Points / 200)
            {
                Console.WriteLine("{0}: the gradient differs at {1} of {2} points (by up to {3})", expression, different, Points, worst);
            }
            CHECK_LE(different, Points / 200);
        }

        [TEST("SdfPrimitives", "AnalyticGradients")]
        public static void SdfPrimitives_AnalyticGradients()
        {
            string two = "box(0.5, 0.3, 0.4, 0.05), translate(torus(0.4, 0.15), 0.3, 0.2, 0)";
            CheckGradient("sphere(0.6)", 1.2);
            CheckGradient("box(0.6, 0.4, 0.5)", 1.2);
            CheckGradient("box(0.6, 0.4, 0.5, 0.1)", 1.2);
            CheckGradient("torus(0.6, 0.2)", 1.2);
            CheckGradient("cylinder(0.4, 0.5)", 1.2);
            CheckGradient("capsule(-0.4,0,0, 0.4,0.2,0, 0.2)", 1.2);
            CheckGradient("plane(0.3, 1, 0.2, 0.1)", 1.2);
            CheckGradient("ellipsoid(0.8, 0.3, 0.5)", 1.2);
            CheckGradient("octahedron(0.7)", 1.2);
            CheckGradient("link(0.3, 0.4, 0.1)", 1.2);
            CheckGradient("roundedcylinder(0.5, 0.1, 0.4)", 1.2);
            CheckGradient("cappedcone(0.5, 0.6, 0.2)", 1.2);
            CheckGradient("cappedcone(0.5, 0.2, 0.6)", 1.2);
            CheckGradient("roundcone(0.4, 0.15, 0.6)", 1.2);
            CheckGradient("infinitecylinder(0.4)", 1.2);
            CheckGradient("union(" + two + ")", 1.2);
            CheckGradient("intersect(" + two + ")", 1.2);
            CheckGradient("subtract(" + two + ")", 1.2);
            CheckGradient("smoothunion(" + two + ", 0.2)", 1.2);
            CheckGradient("smoothunion(" + two + ", 0.2, \"cubic\")", 1.2);
            CheckGradient("smoothunion(" + two + ", 0.2, \"exp\")", 1.2);
            CheckGradient("smoothintersect(" + two + ", 0.2)", 1.2);
            CheckGradient("smoothsubtract(" + two + ", 0.2)", 1.2);
            CheckGradient("smoothsubtract(" + two + ", 0.2, \"exp\")", 1.2);
            CheckGradient("offset(box(0.5, 0.3, 0.4), 0.1)", 1.2);
            CheckGradient("shell(torus(0.5, 0.3), 0.1)", 1.2);
            CheckGradient("rotate(translate(box(0.5, 0.3, 0.4), 0.1, 0.2, -0.1), 20, 30, 40)", 1.2);
            CheckGradient("scale(torus(0.4, 0.15), 1.5)", 1.2);
            CheckGradient("scale(torus(0.4, 0.15), 1, 2, 1.5)", 1.2);
            CheckGradient("twist(box(0.5, 0.3, 0.4), 1.5)", 1.2);
            CheckGradient("bend(box(0.6, 0.2, 0.3), 0.8)", 1.2);
            CheckGradient("repeat(sphere(0.2), 0.6, 0.6, 0.6)", 1.2);
            CheckGradient("mirror(translate(box(0.3, 0.2, 0.2), 0.1, 0, 0), \"x\")", 1.2);
            CheckGradient("elongate(torus(0.3, 0.1), 0.2, 0.1, 0)", 1.2);
            CheckGradient("label(sphere(0.5), 1)", 1.2);
            // A node without a closed form (the cone) below one with: the difference of its own subtree fills in
            CheckGradient("union(cone(0.4, 0.6), translate(sphere(0.3), 0.4, 0, 0))", 1.2);
        }

        // The box of a field holds every point of its surface (found by walking random points down the gradient), is not much
        // larger than the surface, and where the field says that it grows with the distance to the box, it does
        private static void CheckBounds(string expression, double extent)
        {
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = extent * Math.Sqrt(3);
            SdfNode field = SdfParser.Parse(expression, context);
            bool bounded = field.Bounds(out SdfVector lower, out SdfVector upper, out bool grows);
            CHECK(bounded);
            RNG rng = new RNG(3);
            int found = 0, outside = 0, below = 0;
            SdfVector least = new SdfVector(1e30, 1e30, 1e30), most = new SdfVector(-1e30, -1e30, -1e30);
            for (int i = 0; i < Points; ++i)
            {
                SdfVector p = new SdfVector(extent * (2 * rng.Uniform<double>() - 1), extent * (2 * rng.Uniform<double>() - 1),
                                            extent * (2 * rng.Uniform<double>() - 1));
                double toBox = SdfExtent.Distance(p, lower, upper);
                if (grows && toBox > 0 && field.Evaluate(p) < toBox - 1e-9)
                {
                    ++below;
                }
                for (int step = 0; step < 40 && Math.Abs(field.Evaluate(p)) > 1e-9; ++step)
                {
                    SdfVector gradient = field.Gradient(p, 1e-6);
                    double squared = gradient.LengthSquared();
                    if (squared < 1e-12)
                    {
                        break;
                    }
                    p = p - gradient * (field.Evaluate(p) / squared);
                }
                if (Math.Abs(field.Evaluate(p)) > 1e-9 || Math.Abs(p.x) > 4 * extent || Math.Abs(p.y) > 4 * extent || Math.Abs(p.z) > 4 * extent)
                {
                    continue;
                }
                ++found;
                least = new SdfVector(Math.Min(least.x, p.x), Math.Min(least.y, p.y), Math.Min(least.z, p.z));
                most = new SdfVector(Math.Max(most.x, p.x), Math.Max(most.y, p.y), Math.Max(most.z, p.z));
                if (SdfExtent.Distance(p, lower, upper) > 1e-7)
                {
                    ++outside;
                }
            }
            double slack = 0;
            for (int axis = 0; axis < 3; ++axis)
            {
                slack = Math.Max(slack, Math.Max(least[axis] - lower[axis], upper[axis] - most[axis]));
            }
            if (outside > 0 || below > 0 || slack > 0.5 * extent || found <= Points / 50)
            {
                Console.WriteLine("{0}: {1} of {2} surface points outside the box, {3} points below the distance to it, slack {4}",
                                  expression, outside, found, below, slack);
            }
            // (a field that is flat far from its surface, such as the metaballs, only lets the nearby points walk to it)
            CHECK_GT(found, Points / 50);
            CHECK_EQ(outside, 0);
            CHECK_EQ(below, 0);
            CHECK_LT(slack, 0.5 * extent);
        }

        private static void CheckUnbounded(string expression)
        {
            SdfNode field = SdfParser.Parse(expression, new SdfParseContext());
            CHECK(!field.Bounds(out SdfVector lower, out SdfVector upper, out bool grows));
        }

        [TEST("SdfPrimitives", "Bounds")]
        public static void SdfPrimitives_Bounds()
        {
            string two = "box(0.5, 0.3, 0.4, 0.05), translate(torus(0.4, 0.15), 0.3, 0.2, 0)";
            string[] expressions = {
                "sphere(0.6)", "box(0.6, 0.4, 0.5, 0.1)", "torus(0.6, 0.2)", "cylinder(0.4, 0.5)", "capsule(-0.4,0,0, 0.4,0.2,0, 0.2)",
                "cone(0.5, 0.8)", "ellipsoid(0.8, 0.3, 0.5)", "octahedron(0.7)", "menger(2)",
                "boxframe(0.7, 0.5, 0.6, 0.08)", "cappedtorus(60, 0.6, 0.15)", "link(0.3, 0.4, 0.1)", "hexprism(0.5, 0.3)",
                "triprism(0.5, 0.3)", "roundedcylinder(0.5, 0.1, 0.4)", "cappedcone(0.5, 0.6, 0.2)", "roundcone(0.4, 0.15, 0.6)",
                "roundcone(0.15, 0.4, 0.6)", "solidangle(35, 0.8)", "cutsphere(0.7, 0.2)", "cuthollowsphere(0.6, 0.2, 0.06)",
                "deathstar(0.7, 0.5, 0.8)", "rhombus(0.6, 0.4, 0.2, 0.05)", "pyramid(0.8)",
                "triangle(-0.5,0,0.2, 0.6,0.1,-0.3, 0,0.5,0.4, 0.05)", "quad(-0.5,0,-0.5, 0.5,0,-0.5, 0.5,0,0.5, -0.5,0,0.5, 0.05)",
                "union(" + two + ")", "intersect(" + two + ")", "subtract(" + two + ")", "intersect(infinitecylinder(0.4), box(0.8, 0.6, 0.8))",
                "intersect(plane(0, 1, 0, 0.1), sphere(0.7))",
                "smoothunion(" + two + ", 0.2)", "smoothunion(" + two + ", 0.2, \"cubic\")", "smoothunion(" + two + ", 0.2, \"exp\")",
                "smoothintersect(" + two + ", 0.2)", "smoothsubtract(" + two + ", 0.2)", "mix(" + two + ", 0.4)",
                "offset(box(0.5, 0.3, 0.4), 0.1)", "offset(box(0.5, 0.3, 0.4), -0.1)", "shell(torus(0.5, 0.3), 0.1)",
                "offset(smoothunion(" + two + ", 0.2), 0.1)",
                "rotate(translate(box(0.5, 0.3, 0.4), 0.1, 0.2, -0.1), 20, 30, 40)", "scale(torus(0.4, 0.15), 1.5)",
                "scale(torus(0.4, 0.15), 1, 2, 1.5)", "twist(box(0.5, 0.3, 0.4), 1.5)", "bend(box(0.6, 0.2, 0.3), 0.8)",
                "repeat(sphere(0.2), 0.6, 0.6, 0.6, 1)", "mirror(translate(box(0.3, 0.2, 0.2), 0.2, 0, 0), \"x\")",
                "elongate(torus(0.3, 0.1), 0.2, 0.1, 0)", "displace(sphere(0.6), 0.05, 4, 3)", "label(sphere(0.5), 1)",
                "metaballs(0.3, 0,0,0,0.5,1, 0.4,0.1,0,0.4,1)", "inset(sphere(0.7), box(0.3, 0.3, 2), 0.1)",
                "ink(sphere(0.7), box(0.3, 0.3, 2), 0.05)" };
            foreach (string expression in expressions)
            {
                CheckBounds(expression, 1.2);
            }
            CheckUnbounded("plane(0, 1, 0, 0)");
            CheckUnbounded("infinitecylinder(0.4)");
            CheckUnbounded("repeat(sphere(0.2), 0.6, 0.6, 0.6)");
            CheckUnbounded("union(sphere(0.5), plane(0, 1, 0, 0))");
            CheckUnbounded("gyroid(0.5, 0.05)");
            // A field that is not known to grow with the distance has no box once its surface is moved outward
            CheckUnbounded("offset(ellipsoid(0.8, 0.3, 0.5), 0.1)");
        }

        // The outlines: the sign against an inside test written by hand (on a grid, so that many points are level with a corner
        // of the outline, where a ray-crossing count is easy to get wrong), then the exact distance, the gradient and the box of
        // the solids over them
        private static int WrongSigns(SdfContour contour, Func<double, double, bool> inside)
        {
            int wrong = 0;
            for (int i = -60; i <= 60; ++i)
            {
                for (int j = -60; j <= 60; ++j)
                {
                    double x = 0.02 * i, y = 0.02 * j, distance = contour.Distance(x, y);
                    // A point on the outline may fall to either side
                    if (Math.Abs(distance) > 1e-9 && (distance < 0) != inside(x, y))
                    {
                        ++wrong;
                    }
                }
            }
            return wrong;
        }

        [TEST("SdfPrimitives", "OutlineSigns")]
        public static void SdfPrimitives_OutlineSigns()
        {
            CHECK_EQ(WrongSigns(SdfShapes2D.Circle(0.5), (double x, double y) => x * x + y * y < 0.25), 0);
            CHECK_EQ(WrongSigns(SdfShapes2D.Rectangle(0.6, 0.4, 0), (double x, double y) => Math.Abs(x) < 0.6 && Math.Abs(y) < 0.4), 0);
            CHECK_EQ(WrongSigns(SdfShapes2D.Cross(0.8, 0.2), (double x, double y) => (Math.Abs(x) < 0.8 && Math.Abs(y) < 0.2) ||
                                                                                      (Math.Abs(x) < 0.2 && Math.Abs(y) < 0.8)), 0);
            double a = 50 * Math.PI / 180;
            CHECK_EQ(WrongSigns(SdfShapes2D.Pie(a, 0.8), (double x, double y) => x * x + y * y < 0.64 && Math.Atan2(Math.Abs(x), y) < a), 0);
            CHECK_EQ(WrongSigns(SdfShapes2D.Vesica(0.8, 0.4), (double x, double y) => (x - 0.4) * (x - 0.4) + y * y < 0.64 &&
                                                                                       (x + 0.4) * (x + 0.4) + y * y < 0.64), 0);
            // A thick arc is the set of the points within the half width of the middle arc
            CHECK_EQ(WrongSigns(SdfShapes2D.Arc(a, 0.6, 0.12), (double x, double y) =>
            {
                double angle = Math.Atan2(Math.Abs(x), y), radius = Math.Sqrt(x * x + y * y);
                if (angle <= a)
                {
                    return Math.Abs(radius - 0.6) < 0.12;
                }
                double ex = Math.Abs(x) - 0.6 * Math.Sin(a), ey = y - 0.6 * Math.Cos(a);
                return ex * ex + ey * ey < 0.12 * 0.12;
            }), 0);
            CHECK_EQ(WrongSigns(SdfShapes2D.RegularPolygon(8, 0.6), (double x, double y) =>
            {
                for (int side = 0; side < 8; ++side)
                {
                    if (x * Math.Cos(0.25 * Math.PI * side) + y * Math.Sin(0.25 * Math.PI * side) >= 0.6)
                    {
                        return false;
                    }
                }
                return true;
            }), 0);
            // A square with a round hole, and the same turned and moved
            SdfContour holed = SdfShapes2D.Path("M -0.6 -0.6 L 0.6 -0.6 L 0.6 0.6 L -0.6 0.6 Z M 0.3 0 A -0.3 0 1 A 0.3 0 1", out string error);
            CHECK(holed != null && holed.IsClosed());
            CHECK_EQ(WrongSigns(holed, (double x, double y) => Math.Abs(x) < 0.6 && Math.Abs(y) < 0.6 && x * x + y * y > 0.09), 0);
            SdfContour moved = holed.Transformed(0.5 * Math.PI, 0.2, 0.1);
            CHECK_EQ(WrongSigns(moved, (double x, double y) => Math.Abs(x - 0.2) < 0.6 && Math.Abs(y - 0.1) < 0.6 &&
                                                               (x - 0.2) * (x - 0.2) + (y - 0.1) * (y - 0.1) > 0.09), 0);
            // Under a parabola: the Bezier from (-0.8, 0) to (0.8, 0) with the control point (0, 1.6) is y = 0.8 - 1.25 x^2
            SdfContour parabola = SdfShapes2D.Path("M -0.8 0 Q 0 1.6 0.8 0 Z", out error);
            CHECK_EQ(WrongSigns(parabola, (double x, double y) => y > 0 && y < 0.8 - 1.25 * x * x), 0);
        }

        [TEST("SdfPrimitives", "Outlines")]
        public static void SdfPrimitives_Outlines()
        {
            SdfVector origin = new SdfVector(0, 0, 0);
            string blade = "path(\"M -0.7 -0.2 Q 0 -0.6 0.7 -0.2 A 0.7 0.2 1 Q 0 0.7 -0.7 0.2 L -0.4 0 Z\")";
            string holed = "path(\"M -0.6 -0.5 L 0.6 -0.5 L 0.6 0.5 L -0.6 0.5 Z M 0.25 0 A -0.25 0 1 A 0.25 0 1\")";
            string profile = "path(\"M 0 -0.6 L 0.5 -0.6 L 0.5 -0.4 Q 0.1 -0.2 0.2 0.2 A 0.4 0.5 0.4 L 0 0.7\")";
            string[] solids = {
                "extrude(circle(0.5), 0.3)", "extrude(rect(0.6, 0.4), 0.3)", "extrude(rect(0.6, 0.4, 0.15), 0.3)",
                "extrude(polygon(-0.6,-0.4, 0.6,-0.4, 0.1,0, 0.6,0.5, -0.6,0.5), 0.3)", "extrude(ngon(5, 0.5), 0.3)",
                "extrude(octagon(0.5), 0.3)", "extrude(cross(0.7, 0.2), 0.3)", "extrude(pie(50, 0.8), 0.3)",
                "extrude(arc(60, 0.6, 0.12), 0.3)", "extrude(arc(130, 0.6, 0.12), 0.3)", "extrude(horseshoe(90, 0.45, 0.4, 0.1), 0.3)",
                "extrude(vesica(0.8, 0.4), 0.3)", "extrude(" + blade + ", 0.25)", "extrude(" + holed + ", 0.3)",
                "extrude(rotate2(translate2(cross(0.5, 0.15), 0.1, 0.05), 30), 0.3)",
                "revolve(circle(0.2), 0.6)", "revolve(rect(0.15, 0.3, 0.05), 0.5)", "revolve(translate2(cross(0.25, 0.08), 0.6, 0))",
                "revolve(arc(120, 0.2, 0.05), 0.6)", "lathe(" + profile + ")" };
            SdfVector[] inside = {
                origin, origin, origin, new SdfVector(-0.3, 0, 0), origin, origin, origin, new SdfVector(0, 0.4, 0),
                new SdfVector(0, 0.6, 0), new SdfVector(0, 0.6, 0), new SdfVector(0, 0.45, 0), origin, origin, new SdfVector(0.45, 0, 0),
                new SdfVector(0.1, 0.05, 0),
                new SdfVector(0.6, 0, 0), new SdfVector(0.5, 0, 0), new SdfVector(0.6, 0, 0), new SdfVector(0.6, 0.2, 0), origin };
            for (int i = 0; i < solids.Length; ++i)
            {
                CheckClaims(solids[i], inside[i], 1.2, true, true);
                CheckGradient(solids[i], 1.2);
                CheckBounds(solids[i], 1.2);
            }
            // Grown outlines are exact on the side that they grew toward; one that crosses the axis of a revolution is not exact
            CheckClaims("extrude(offset2(cross(0.5, 0.15), 0.1), 0.3)", origin, 1.2, false, true);
            CheckClaims("extrude(offset2(cross(0.6, 0.25), -0.1), 0.3)", origin, 1.2, true, false);
            CheckClaims("revolve(rect(0.5, 0.3))", origin, 1.2, false, false);
            CheckBounds("extrude(offset2(cross(0.5, 0.15), 0.1), 0.3)", 1.2);

            // The exclusive or is exact on both sides, and a | b ^ c reads from the left
            string two = "sphere(0.5), translate(box(0.4, 0.3, 0.3), 0.4, 0, 0)";
            CheckClaims("xor(" + two + ")", new SdfVector(-0.3, 0, 0), 1.2, true, true);
            CheckGradient("xor(" + two + ")", 1.2);
            CheckBounds("xor(" + two + ")", 1.2);
            CheckClaims("sphere(0.5) ^ translate(box(0.4, 0.3, 0.3), 0.4, 0, 0)", new SdfVector(-0.3, 0, 0), 1.2, true, true);

            // The changed metric: bounds of the distance (a gradient that is never longer than 1), with their boxes
            foreach (string expression in new string[] { "torusnorm(0.6, 0.2, 2, 8)", "torusnorm(0.6, 0.2, 8, 8)", "ballnorm(0.6, 4)" })
            {
                SdfNode field = SdfParser.Parse(expression, new SdfParseContext());
                CHECK(!field.IsExactInside() && !field.IsExactOutside());
                RNG rng = new RNG(9);
                double steepest = 0;
                for (int i = 0; i < Points; ++i)
                {
                    SdfVector p = new SdfVector(1.2 * (2 * rng.Uniform<double>() - 1), 1.2 * (2 * rng.Uniform<double>() - 1),
                                                1.2 * (2 * rng.Uniform<double>() - 1));
                    steepest = Math.Max(steepest, field.Gradient(p, 1e-6).Length());
                }
                CHECK_LE(steepest, 1 + 1e-6);
                CheckBounds(expression, 1.2);
            }
        }

        // A chain of unions compiled as a hierarchy that skips the operands that are too far gives the values of the plain chain,
        // bit for bit (the smooth unions are compiled as before)
        [TEST("SdfPrimitives", "CulledUnions")]
        public static void SdfPrimitives_CulledUnions()
        {
            string[] kinds = { "union(A, B)", "smoothunion(A, B, 0.08)", "smoothunion(A, B, 0.15, \"cubic\")", "smoothunion(A, B, 0.1, \"exp\")" };
            foreach (string kind in kinds)
            {
                RNG rng = new RNG(61);
                string chain = "";
                for (int i = 0; i < (kind == kinds[0] ? 150 : 24); ++i)
                {
                    double x = 2 * rng.Uniform<double>() - 1, y = 2 * rng.Uniform<double>() - 1, z = 2 * rng.Uniform<double>() - 1;
                    string part = i % 4 == 0 ? "torus(0.15, 0.05)" : (i % 4 == 1 ? "box(0.1, 0.06, 0.08)" : (i % 4 == 2 ? "sphere(0.12)" : "ellipsoid(0.2, 0.1, 0.1)"));
                    part = "translate(" + part + ", " + x.ToString("F3") + ", " + y.ToString("F3") + ", " + z.ToString("F3") + ")";
                    // One operand without an end, which is never skipped
                    if (i == 9)
                    {
                        part = "plane(0, 1, 0, 1.4)";
                    }
                    chain = i == 0 ? part : kind.Replace("A", chain).Replace("B", part);
                }
                SdfNode field = SdfParser.Parse(chain, new SdfParseContext());
                SdfNode plain = SdfCompiledNode.Compile(field), culled = SdfCompiledNode.Compile(field, true);
                int different = 0;
                for (int i = 0; i < 200000; ++i)
                {
                    SdfVector p = new SdfVector(1.6 * (2 * rng.Uniform<double>() - 1), 1.6 * (2 * rng.Uniform<double>() - 1),
                                                1.6 * (2 * rng.Uniform<double>() - 1));
                    different += plain.Evaluate(p) != culled.Evaluate(p) ? 1 : 0;
                }
                if (different > 0)
                {
                    Console.WriteLine("{0}: {1} of 200000 values differ", kind, different);
                }
                CHECK_EQ(different, 0);
            }
        }

        [TEST("SdfPrimitives", "ExactDistances")]
        public static void SdfPrimitives_ExactDistances()
        {
            CheckExact("boxframe(0.7, 0.5, 0.6, 0.08)", new SdfVector(0.66, 0.46, 0), 1.2);
            CheckExact("cappedtorus(60, 0.6, 0.15)", new SdfVector(0, 0.6, 0), 1.2);
            CheckExact("link(0.3, 0.4, 0.1)", new SdfVector(0.4, 0, 0), 1.2);
            CheckExact("hexprism(0.5, 0.3)", new SdfVector(0, 0, 0), 1.2);
            CheckExact("triprism(0.5, 0.3)", new SdfVector(0, 0, 0), 1.2);
            CheckExact("roundedcylinder(0.5, 0.1, 0.4)", new SdfVector(0, 0, 0), 1.2);
            CheckExact("cappedcone(0.5, 0.6, 0.2)", new SdfVector(0, 0, 0), 1.2);
            CheckExact("roundcone(0.4, 0.15, 0.6)", new SdfVector(0, 0.3, 0), 1.2);
            CheckExact("solidangle(35, 0.8)", new SdfVector(0, 0.5, 0), 1.2);
            CheckExact("cutsphere(0.7, 0.2)", new SdfVector(0, 0.5, 0), 1.2);
            CheckExact("cuthollowsphere(0.6, 0.2, 0.06)", new SdfVector(0, -0.6, 0), 1.2);
            CheckExact("deathstar(0.7, 0.5, 0.8)", new SdfVector(-0.3, 0, 0), 1.2);
            CheckExact("rhombus(0.6, 0.4, 0.2, 0.05)", new SdfVector(0, 0, 0), 1.2);
            CheckExact("pyramid(0.8)", new SdfVector(0, 0.2, 0), 1.2);
            CheckExact("triangle(-0.5,0,0.2, 0.6,0.1,-0.3, 0,0.5,0.4, 0.05)", new SdfVector(0.03, 0.2, 0.1), 1.2);
            CheckExact("quad(-0.5,0,-0.5, 0.5,0,-0.5, 0.5,0,0.5, -0.5,0,0.5, 0.05)", new SdfVector(0, 0, 0), 1.2);
            CheckExact("intersect(infinitecylinder(0.4), box(3, 3, 3))", new SdfVector(0, 0, 0), 1.2);
            CheckExact("intersect(infinitecone(30), box(3, 3, 3))", new SdfVector(0, -0.5, 0), 1.2);
        }
    }
}
