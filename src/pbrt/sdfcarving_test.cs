// (not in pbrt-v4) sdfcarving_test.cs

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class SdfCarvingTests
    {
        // Points of the surface: random points walked down the gradient
        private static List<SdfVector> Surface(SdfNode field, SdfVector center, double extent, int count)
        {
            List<SdfVector> cloud = new List<SdfVector>();
            RNG rng = new RNG(17);
            for (int i = 0; i < count; ++i)
            {
                SdfVector p = center + new SdfVector(extent * (2 * rng.Uniform<double>() - 1), extent * (2 * rng.Uniform<double>() - 1),
                                                     extent * (2 * rng.Uniform<double>() - 1));
                for (int step = 0; step < 60 && Math.Abs(field.Evaluate(p)) > 1e-9; ++step)
                {
                    SdfVector gradient = field.Gradient(p, 1e-6);
                    double squared = gradient.LengthSquared();
                    if (squared < 1e-12)
                    {
                        break;
                    }
                    p = p - gradient * (field.Evaluate(p) / squared);
                }
                if (Math.Abs(field.Evaluate(p)) <= 1e-9 && (p - center).Length() < 3 * extent)
                {
                    cloud.Add(p);
                }
            }
            return cloud;
        }

        // Carves around the box (lower, upper), which holds the object, and checks that the planes hold every point of the
        // surface, that they are not far from it, and that the proxy field is the field on the surface and a lower bound of the
        // distance outside. Returns the number of evaluations of the field.
        private static int Check(string expression, SdfVector lower, SdfVector upper, double slackAllowed, int maxSpheres = 600)
        {
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = Math.Max(lower.Length(), upper.Length());
            SdfNode field = SdfParser.Parse(expression, context);
            SdfVector center = (lower + upper) * 0.5;
            double reach = 0.5 * (upper - lower).Length();
            SdfCarving carving = new SdfCarving(field, center, (field.Lipschitz() > 1.0001 ? 1.5 : 4) * reach, 0.05 * reach, maxSpheres, 64);
            CHECK(carving.IsValid());
            double[] planes = carving.Planes(20);
            List<SdfVector> cloud = Surface(field, center, reach, 6000);
            CHECK_GT(cloud.Count, 1000);
            int outside = 0;
            double[] nearest = new double[planes.Length / 4];
            for (int i = 0; i < nearest.Length; ++i)
            {
                nearest[i] = double.PositiveInfinity;
            }
            foreach (SdfVector p in cloud)
            {
                for (int i = 0; i < planes.Length; i += 4)
                {
                    double beyond = planes[i] * p.x + planes[i + 1] * p.y + planes[i + 2] * p.z - planes[i + 3];
                    outside += beyond > 1e-7 ? 1 : 0;
                    nearest[i / 4] = Math.Min(nearest[i / 4], -beyond);
                }
            }
            // How far the planes stand off the surface, as a share of the size of the object
            double slack = 0;
            for (int i = 0; i < nearest.Length; ++i)
            {
                slack = Math.Max(slack, nearest[i] / reach);
            }

            SdfCarvedField proxy = new SdfCarvedField(field, new double[][] { planes }, 0.01 * reach);
            int changed = 0, tooFar = 0, replaced = 0;
            foreach (SdfVector p in cloud)
            {
                changed += proxy.Evaluate(p) != field.Evaluate(p) ? 1 : 0;
            }
            RNG rng = new RNG(23);
            double lipschitz = field.Lipschitz();
            for (int i = 0; i < 3000; ++i)
            {
                SdfVector p = center + new SdfVector(3 * reach * (2 * rng.Uniform<double>() - 1), 3 * reach * (2 * rng.Uniform<double>() - 1),
                                                     3 * reach * (2 * rng.Uniform<double>() - 1));
                double value = proxy.Evaluate(p);
                if (value == field.Evaluate(p))
                {
                    continue;
                }
                ++replaced;
                double closest = double.PositiveInfinity;
                foreach (SdfVector q in cloud)
                {
                    closest = Math.Min(closest, (p - q).Length());
                }
                tooFar += value / lipschitz > closest + 1e-9 ? 1 : 0;
            }
            Console.WriteLine("{0}: {1} evaluations, {2} rounds, {3} balls, {4} points, {5} planes, stand-off {6:F3}, proxy at {7} of 3000 points",
                              expression, carving.Evaluations(), carving.Iterations(), carving.SphereCount(), carving.PointCount(),
                              planes.Length / 4, slack, replaced);
            CHECK_EQ(outside, 0);
            CHECK_EQ(changed, 0);
            CHECK_EQ(tooFar, 0);
            CHECK_GT(replaced, 1500);
            CHECK_LT(slack, slackAllowed);
            return carving.Evaluations();
        }

        [TEST("SdfCarving", "Bounds")]
        public static void SdfCarving_Bounds()
        {
            SdfVector lower = new SdfVector(-1.5, -1.5, -1.5), upper = new SdfVector(1.5, 1.5, 1.5);
            // Exact distances: the paper needs less than a hundred evaluations for these
            CHECK_LT(Check("sphere(0.8)", lower, upper, 0.1), 400);
            Check("box(0.9, 0.5, 0.3)", lower, upper, 0.15);
            Check("torus(0.8, 0.25)", lower, upper, 0.15);
            Check("rotate(translate(box(0.9, 0.2, 0.3), 0.3, 0.2, 0), 20, 30, 40)", lower, upper, 0.15);
            Check("capsule(-0.9,-0.6,0, 0.8,0.7,0.3, 0.15)", lower, upper, 0.15);
            // Bounds of the distance: a union inside, blends, two parts, a field with no closed-form box
            Check("union(sphere(0.5), translate(box(0.4, 0.3, 0.3), 0.6, 0.2, 0))", lower, upper, 0.15);
            Check("smoothunion(sphere(0.5), translate(torus(0.5, 0.15), 0.4, 0.3, 0), 0.2)", lower, upper, 0.15);
            Check("union(translate(sphere(0.3), -0.9, 0, 0), translate(octahedron(0.4), 0.8, 0.5, 0.3))", lower, upper, 0.15);
            Check("offset(ellipsoid(0.9, 0.4, 0.6), 0.1)", lower, upper, 0.4, 4000);
            Check("ellipsoid(1.0, 0.3, 0.5)", lower, upper, 1.0, 4000);
            // A Lipschitz bound above 1: smaller balls, and the budget of balls ends the carving before the threshold does; the
            // planes are then farther from the surface, and still hold it
            Check("twist(box(0.6, 0.8, 0.3), 1)", lower, upper, 0.4, 4000);
            Check("displace(sphere(0.8), 0.05, 4, 3)", lower, upper, 0.5, 4000);
        }

        // The convex decomposition: every point of the surface is inside one of the pieces, and the pieces together are
        // smaller than the single hull. Returns the share of the volume of the single hull that the pieces take.
        private static double CheckPieces(string expression, int parts)
        {
            SdfVector lower = new SdfVector(-1.5, -1.5, -1.5), upper = new SdfVector(1.5, 1.5, 1.5);
            SdfNode field = SdfParser.Parse(expression, new SdfParseContext());
            SdfVector center = (lower + upper) * 0.5;
            double reach = 0.5 * (upper - lower).Length();
            SdfCarving carving = new SdfCarving(field, center, 4 * reach, 0.03 * reach, 4000, 64);
            CHECK(carving.IsValid());
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            SdfCarving[] pieces = carving.Decompose(parts);
            long milliseconds = watch.ElapsedMilliseconds;
            double[][] hulls = new double[pieces.Length][];
            double volume = 0;
            for (int i = 0; i < pieces.Length; ++i)
            {
                hulls[i] = pieces[i].Planes(20);
                volume += pieces[i].Volume();
            }
            List<SdfVector> cloud = Surface(field, center, reach, 6000);
            int outside = 0;
            foreach (SdfVector p in cloud)
            {
                outside += SdfCarvedHulls.Beyond(hulls, p) > 1e-7 ? 1 : 0;
            }
            // The inside of the solid is covered too (a density is read there)
            RNG rng = new RNG(43);
            int inside = 0, uncovered = 0;
            double deepest = 0;
            for (int i = 0; i < 400000; ++i)
            {
                SdfVector p = new SdfVector(1.5 * (2 * rng.Uniform<double>() - 1), 1.5 * (2 * rng.Uniform<double>() - 1), 1.5 * (2 * rng.Uniform<double>() - 1));
                if (field.Evaluate(p) < 0)
                {
                    ++inside;
                    double beyond = SdfCarvedHulls.Beyond(hulls, p);
                    if (beyond > 0)
                    {
                        ++uncovered;
                        deepest = Math.Max(deepest, beyond);
                    }
                }
            }
            if (uncovered > 0)
            {
                Console.WriteLine("  {0} of {1} points inside the solid are outside the pieces, by up to {2}", uncovered, inside, deepest);
            }
            CHECK_EQ(uncovered, 0);
            double share = volume / carving.Volume();
            Console.WriteLine("{0}: asked for {1} pieces, got {2}, {3:F2} of the volume of one hull, {4} ms, {5} of {6} surface points outside",
                              expression, parts, pieces.Length, share, milliseconds, outside, cloud.Count);
            CHECK_EQ(outside, 0);
            return share;
        }

        [TEST("SdfCarving", "Decomposition")]
        public static void SdfCarving_Decomposition()
        {
            string apart = "union(translate(sphere(0.35), -0.9, -0.6, 0), translate(octahedron(0.4), 0.8, 0.6, 0.3))";
            string angle = "union(translate(box(0.9, 0.15, 0.2), 0, -0.7, 0), translate(box(0.15, 0.9, 0.2), -0.75, 0, 0))";
            CHECK_LT(CheckPieces(apart, 2), 0.7);
            CHECK_LT(CheckPieces(angle, 2), 0.8);
            foreach (int parts in new int[] { 2, 4, 8 })
            {
                CheckPieces("torus(0.9, 0.15)", parts);
                CheckPieces(angle, parts);
                CheckPieces("union(" + apart + ", translate(torus(0.4, 0.1), 0, 0.2, -0.8))", parts);
            }
            CheckPieces("smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(smoothunion(translate(torus(0.146, 0.05), 1.100, 0.000, 0.000), translate(sphere(0.112), 1.083, 0.125, 0.191), 0.08), translate(sphere(0.152), 1.034, 0.217, 0.376), 0.08), translate(torus(0.126, 0.05), 0.953, 0.250, 0.550), 0.08), translate(sphere(0.143), 0.843, 0.217, 0.707), 0.08), translate(sphere(0.129), 0.707, 0.125, 0.843), 0.08), translate(torus(0.125, 0.05), 0.550, 0.000, 0.953), 0.08), translate(sphere(0.141), 0.376, -0.125, 1.034), 0.08), translate(sphere(0.103), 0.191, -0.217, 1.083), 0.08), translate(torus(0.155, 0.05), 0.000, -0.250, 1.100), 0.08), translate(sphere(0.106), -0.191, -0.217, 1.083), 0.08), translate(sphere(0.107), -0.376, -0.125, 1.034), 0.08), translate(torus(0.154, 0.05), -0.550, -0.000, 0.953), 0.08), translate(sphere(0.166), -0.707, 0.125, 0.843), 0.08), translate(sphere(0.110), -0.843, 0.217, 0.707), 0.08), translate(torus(0.138, 0.05), -0.953, 0.250, 0.550), 0.08), translate(sphere(0.150), -1.034, 0.217, 0.376), 0.08), translate(sphere(0.176), -1.083, 0.125, 0.191), 0.08), translate(torus(0.166, 0.05), -1.100, 0.000, 0.000), 0.08), translate(sphere(0.132), -1.083, -0.125, -0.191), 0.08), translate(sphere(0.178), -1.034, -0.217, -0.376), 0.08), translate(torus(0.124, 0.05), -0.953, -0.250, -0.550), 0.08), translate(sphere(0.169), -0.843, -0.217, -0.707), 0.08), translate(sphere(0.123), -0.707, -0.125, -0.843), 0.08), translate(torus(0.132, 0.05), -0.550, -0.000, -0.953), 0.08), translate(sphere(0.109), -0.376, 0.125, -1.034), 0.08), translate(sphere(0.125), -0.191, 0.217, -1.083), 0.08), translate(torus(0.185, 0.05), -0.000, 0.250, -1.100), 0.08), translate(sphere(0.114), 0.191, 0.217, -1.083), 0.08), translate(sphere(0.147), 0.376, 0.125, -1.034), 0.08), translate(torus(0.171, 0.05), 0.550, 0.000, -0.953), 0.08), translate(sphere(0.130), 0.707, -0.125, -0.843), 0.08), translate(sphere(0.144), 0.843, -0.217, -0.707), 0.08), translate(torus(0.125, 0.05), 0.953, -0.250, -0.550), 0.08), translate(sphere(0.105), 1.034, -0.217, -0.376), 0.08), translate(sphere(0.116), 1.083, -0.125, -0.191), 0.08)", 8);
            // A convex object is left whole
            CheckPieces("sphere(0.8)", 4);
        }

        // A closed mesh of a torus about y
        private static void TorusMesh(double major, double minor, int around, int across, out SdfVector[] vertices, out int[] indices)
        {
            vertices = new SdfVector[around * across];
            indices = new int[6 * around * across];
            for (int i = 0; i < around; ++i)
            {
                for (int j = 0; j < across; ++j)
                {
                    double u = 2 * Math.PI * i / around, v = 2 * Math.PI * j / across, ring = major + minor * Math.Cos(v);
                    vertices[i * across + j] = new SdfVector(ring * Math.Cos(u), minor * Math.Sin(v), ring * Math.Sin(u));
                    int a = i * across + j, b = ((i + 1) % around) * across + j, c = ((i + 1) % around) * across + (j + 1) % across,
                        d = i * across + (j + 1) % across;
                    int at = 6 * a;
                    indices[at] = a; indices[at + 1] = d; indices[at + 2] = c;
                    indices[at + 3] = a; indices[at + 4] = c; indices[at + 5] = b;
                }
            }
        }

        // A mesh baked to a grid is a bound of the distance outside the box of its grid too, and its carving holds the mesh
        [TEST("SdfCarving", "Mesh")]
        public static void SdfCarving_Mesh()
        {
            TorusMesh(0.8, 0.25, 64, 32, out SdfVector[] vertices, out int[] indices);
            SdfNode exact = SdfMeshLoader.FromTriangles(vertices, indices, 0, 0.05, SdfMeshSign.Automatic);
            SdfNode grid = SdfMeshLoader.FromTriangles(vertices, indices, 48, 0.05, SdfMeshSign.Automatic);
            CHECK(exact.Evaluate(new SdfVector(0.8, 0, 0)) < 0 && exact.Evaluate(new SdfVector(0, 0, 0)) > 0);
            CHECK(grid.Bounds(out SdfVector lower, out SdfVector upper, out bool grows));
            RNG rng = new RNG(31);
            int above = 0, tested = 0;
            double worst = 0;
            for (int i = 0; i < 20000; ++i)
            {
                SdfVector p = new SdfVector(4 * (2 * rng.Uniform<double>() - 1), 4 * (2 * rng.Uniform<double>() - 1), 4 * (2 * rng.Uniform<double>() - 1));
                if (SdfExtent.Distance(p, lower, upper) == 0)
                {
                    continue;
                }
                ++tested;
                // The grid is off by a share of a cell at the border of its box
                double over = grid.Evaluate(p) / grid.Lipschitz() - exact.Evaluate(p);
                worst = Math.Max(worst, over);
                above += over > 0.01 ? 1 : 0;
            }
            Console.WriteLine("grid outside its box: {0} of {1} points above the distance to the mesh, by up to {2:F4}", above, tested, worst);
            CHECK_EQ(above, 0);

            foreach (SdfNode field in new SdfNode[] { exact, grid })
            {
                field.Bounds(out lower, out upper, out grows);
                SdfVector center = (lower + upper) * 0.5;
                double reach = 0.5 * (upper - lower).Length();
                SdfCarving carving = new SdfCarving(field, center, (field.Lipschitz() > 1.0001 ? 1.5 : 4) * reach, 0.05 * reach, 4000, 64);
                CHECK(carving.IsValid());
                double[][] hulls = SdfCarvedHulls.FromCarving(carving, 8, 20);
                int outside = 0;
                foreach (SdfVector vertex in vertices)
                {
                    outside += SdfCarvedHulls.Beyond(hulls, vertex) > 1e-7 ? 1 : 0;
                }
                Console.WriteLine("mesh carving: {0} evaluations, {1} balls, {2} pieces, {3} of {4} vertices outside", carving.Evaluations(),
                                  carving.SphereCount(), hulls.Length, outside, vertices.Length);
                CHECK_EQ(outside, 0);
            }
        }

        // V0 has to hold the object: a field without end does not start
        [TEST("SdfCarving", "Refuses")]
        public static void SdfCarving_Refuses()
        {
            SdfNode plane = SdfParser.Parse("plane(0, 1, 0, 0)", new SdfParseContext());
            CHECK(!new SdfCarving(plane, new SdfVector(0, 0, 0), 8, 0.1, 600, 20).IsValid());
            SdfNode large = SdfParser.Parse("sphere(9)", new SdfParseContext());
            CHECK(!new SdfCarving(large, new SdfVector(0, 0, 0), 8, 0.1, 600, 20).IsValid());
        }
    }
}
