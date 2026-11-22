// (not in pbrt-v4) sdfdda_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class SdfDdaTests
    {
        // Rays from outside, from inside the solid and from its surface, traced with and without the grid of cells
        private static void Compare(string expression, Point3f lower, Point3f upper)
        {
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = 3;
            SdfNode field = SdfCompiledNode.Compile(SdfParser.Parse(expression, context));
            SdfShapeOptions with = new SdfShapeOptions();
            with.ddaResolution = 64;
            SdfShape plain = new SdfShape(new Transform(), new Transform(), false, field, lower, upper, new SdfShapeOptions());
            SdfShape cells = new SdfShape(new Transform(), new Transform(), false, field, lower, upper, with);
            RNG rng = new RNG(71);
            int rays = 0, onlyPlain = 0, onlyCells = 0, apart = 0, shadows = 0;
            double worst = 0;
            for (int i = 0; i < 300000; ++i)
            {
                Point3f o = new Point3f((Float)(1.2 * (2 * rng.Uniform<double>() - 1)), (Float)(1.2 * (2 * rng.Uniform<double>() - 1)),
                                        (Float)(1.2 * (2 * rng.Uniform<double>() - 1)));
                Vector3f d = Normalize(new Vector3f((Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1),
                                                    (Float)(2 * rng.Uniform<double>() - 1)));
                if (i % 3 == 2)
                {
                    // From a point of the surface: the hit of another ray, as a bounce starts
                    pstd.optional<ShapeIntersection> first = plain.Intersect(new Ray(o, d));
                    if (!first.has_value())
                    {
                        continue;
                    }
                    o = first.value().intr.p();
                    d = Normalize(new Vector3f((Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1),
                                               (Float)(2 * rng.Uniform<double>() - 1)));
                }
                Ray ray = new Ray(o, d);
                ++rays;
                pstd.optional<ShapeIntersection> a = plain.Intersect(ray), b = cells.Intersect(ray);
                shadows += plain.IntersectP(ray) != cells.IntersectP(ray) ? 1 : 0;
                if (a.has_value() != b.has_value())
                {
                    if (a.has_value())
                    {
                        ++onlyPlain;
                        if (onlyPlain <= 3)
                        {
                            double t = a.value().tHit;
                            SdfVector p = new SdfVector(o.x + t * d.x, o.y + t * d.y, o.z + t * d.z);
                            Console.WriteLine("  only without the grid: start value {0:E3}, hit at {1:E6}, value there {2:E3}",
                                              field.Evaluate(new SdfVector(o.x, o.y, o.z)), t, field.Evaluate(p));
                        }
                    }
                    else
                    {
                        ++onlyCells;
                        if (onlyCells <= 3)
                        {
                            double t = b.value().tHit;
                            SdfVector p = new SdfVector(o.x + t * d.x, o.y + t * d.y, o.z + t * d.z);
                            Console.WriteLine("  only with the grid: start value {0:E3}, hit at {1:E6}, value there {2:E3}",
                                              field.Evaluate(new SdfVector(o.x, o.y, o.z)), t, field.Evaluate(p));
                        }
                    }
                }
                else if (a.has_value())
                {
                    double gap = Math.Abs(a.value().tHit - b.value().tHit);
                    worst = Math.Max(worst, gap);
                    if (gap > 1e-5)
                    {
                        ++apart;
                        if (apart <= 3)
                        {
                            Console.WriteLine("  hits apart: start value {0:E3}, without {1:E6}, with {2:E6}", field.Evaluate(new SdfVector(o.x, o.y, o.z)),
                                              a.value().tHit, b.value().tHit);
                        }
                    }
                }
            }
            Console.WriteLine("{0}: {1} rays, hit only without the grid {2}, only with it {3}, hits apart by more than 1e-5: {4} (largest gap {5:E2}), shadow rays that differ {6}",
                              expression, rays, onlyPlain, onlyCells, apart, worst, shadows);
            CHECK_EQ(onlyPlain, 0);
            CHECK_EQ(onlyCells, 0);
            CHECK_EQ(apart, 0);
            CHECK_EQ(shadows, 0);
        }

        // The cut-down fields of the cells give the hits and the normals of the whole field
        private static int ComparePruned(string expression, Point3f lower, Point3f upper)
        {
            SdfParseContext context = new SdfParseContext();
            context.domainRadius = 3;
            SdfNode field = SdfCompiledNode.Compile(SdfParser.Parse(expression, context));
            SdfShapeOptions with = new SdfShapeOptions();
            with.ddaResolution = 48;
            with.prune = true;
            SdfShape plain = new SdfShape(new Transform(), new Transform(), false, field, lower, upper, new SdfShapeOptions());
            SdfShape cells = new SdfShape(new Transform(), new Transform(), false, field, lower, upper, with);
            RNG rng = new RNG(73);
            int rays = 0, hits = 0, different = 0, apart = 0, turned = 0, shadows = 0;
            for (int i = 0; i < 150000; ++i)
            {
                Point3f o = new Point3f((Float)(1.6 * (2 * rng.Uniform<double>() - 1)), (Float)(1.6 * (2 * rng.Uniform<double>() - 1)),
                                        (Float)(1.6 * (2 * rng.Uniform<double>() - 1)));
                Vector3f d = Normalize(new Vector3f((Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1),
                                                    (Float)(2 * rng.Uniform<double>() - 1)));
                Ray ray = new Ray(o, d);
                ++rays;
                pstd.optional<ShapeIntersection> a = plain.Intersect(ray), b = cells.Intersect(ray);
                shadows += plain.IntersectP(ray) != cells.IntersectP(ray) ? 1 : 0;
                if (a.has_value() != b.has_value())
                {
                    ++different;
                    continue;
                }
                if (a.has_value())
                {
                    ++hits;
                    apart += Math.Abs(a.value().tHit - b.value().tHit) > 1e-5 ? 1 : 0;
                    turned += Dot(a.value().intr.n, b.value().intr.n) < 1 - 1e-6 ? 1 : 0;
                }
            }
            Console.WriteLine("{0}...: {1} different fields for the cells; {2} rays, {3} hits, {4} hit on one side only, {5} hits apart, {6} normals turned, {7} shadow rays that differ",
                              expression.Substring(0, Math.Min(60, expression.Length)), cells.PrunedFields(), rays, hits, different, apart, turned, shadows);
            CHECK_GT(hits, 2000);
            CHECK_EQ(different, 0);
            CHECK_EQ(apart, 0);
            CHECK_EQ(turned, 0);
            CHECK_EQ(shadows, 0);
            return cells.PrunedFields();
        }

        [TEST("SdfDda", "PrunedCells")]
        public static void SdfDda_PrunedCells()
        {
            Point3f lower = new Point3f(-1.4f, -1.4f, -1.4f), upper = new Point3f(1.4f, 1.4f, 1.4f);
            foreach (string kind in new string[] { "union(A, B)", "smoothunion(A, B, 0.08)", "smoothunion(A, B, 0.1, \"cubic\")", "smoothunion(A, B, 0.06, \"exp\")" })
            {
                RNG rng = new RNG(75);
                string chain = "";
                for (int i = 0; i < 40; ++i)
                {
                    double x = 2 * rng.Uniform<double>() - 1, y = 2 * rng.Uniform<double>() - 1, z = 2 * rng.Uniform<double>() - 1;
                    string part = i % 3 == 0 ? "torus(0.15, 0.05)" : (i % 3 == 1 ? "box(0.1, 0.06, 0.08)" : "sphere(0.12)");
                    part = "translate(" + part + ", " + x.ToString("F3") + ", " + y.ToString("F3") + ", " + z.ToString("F3") + ")";
                    chain = i == 0 ? part : kind.Replace("A", chain).Replace("B", part);
                }
                int fields = ComparePruned(chain, lower, upper);
                // The exponential blend never lets go of an operand, so every cell keeps the whole field
                CHECK(kind.Contains("exp") ? fields == 1 : fields > 20);
            }
            string cut = "subtract(intersect(smoothunion(sphere(0.9), translate(box(0.5, 0.5, 0.5), 0.6, 0.3, 0), 0.1), box(1.2, 0.8, 1.2)), " +
                         "union(translate(sphere(0.4), -0.6, 0.5, 0.4), translate(cylinder(0.2, 2), 0.3, 0, -0.3)))";
            ComparePruned(cut, lower, upper);
            ComparePruned("rotate(offset(smoothsubtract(" + cut + ", translate(torus(0.5, 0.2), 0, -0.5, 0), 0.05), 0.02), 20, 30, 10)", lower, upper);
            ComparePruned("label(smoothintersect(sphere(1.0), union(translate(box(0.6, 0.6, 0.6), 0.3, 0, 0), translate(sphere(0.5), -0.6, 0, 0)), 0.1), 1)", lower, upper);
        }

        // A field without its local bounds, to see what they are worth
        private sealed class GlobalOnly : SdfNode
        {
            public GlobalOnly(SdfNode source)
            {
                this.source = source;
            }

            public override double Evaluate(SdfVector p)
            {
                return source.Evaluate(p);
            }

            public override double Lipschitz()
            {
                return source.Lipschitz();
            }

            private readonly SdfNode source;
        }

        // The values of the field that marching asks for per ray, for rays aimed at the box from outside
        private static double Evaluations(SdfNode field, SdfShapeOptions options, Point3f lower, Point3f upper, out int hits)
        {
            options.countEvaluations = true;
            SdfShape shape = new SdfShape(new Transform(), new Transform(), false, field, lower, upper, options);
            RNG rng = new RNG(77);
            hits = 0;
            int rays = 60000;
            for (int i = 0; i < rays; ++i)
            {
                Vector3f from = Normalize(new Vector3f((Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1),
                                                       (Float)(2 * rng.Uniform<double>() - 1)));
                Point3f target = new Point3f((Float)(lower.x + (upper.x - lower.x) * rng.Uniform<double>()), (Float)(lower.y + (upper.y - lower.y) * rng.Uniform<double>()),
                                             (Float)(lower.z + (upper.z - lower.z) * rng.Uniform<double>()));
                Point3f o = new Point3f(4 * from.x, 4 * from.y, 4 * from.z);
                hits += shape.Intersect(new Ray(o, Normalize(target - o))).has_value() ? 1 : 0;
            }
            return shape.marchEvaluations / (double)rays;
        }

        // Not a check: the number of values per ray for each way of stepping, printed
        [TEST("SdfDda", "StepCounts")]
        public static void SdfDda_StepCounts()
        {
            RNG rng = new RNG(79);
            string chain = "";
            for (int i = 0; i < 36; ++i)
            {
                double angle = 2 * Math.PI * i / 36;
                string part = (i % 3 == 0 ? "torus(0.16, 0.05)" : "sphere(0.14)");
                part = "translate(" + part + ", " + (1.1 * Math.Cos(angle)).ToString("F3") + ", " + (0.25 * Math.Sin(3 * angle)).ToString("F3") + ", " +
                       (1.1 * Math.Sin(angle)).ToString("F3") + ")";
                chain = i == 0 ? part : "smoothunion(" + chain + ", " + part + ", 0.08)";
            }
            string[] names = { "twist(box(0.5, 1.0, 0.25, 0.05), 1.2)", "bend(box(1.0, 0.2, 0.3), 0.8)", "ring of 36 blended parts",
                               "displace(sphere(0.8), 0.05, 4, 3)", "torus(0.8, 0.25)" };
            string[] expressions = { names[0], names[1], chain, names[3], names[4] };
            for (int e = 0; e < expressions.Length; ++e)
            {
                SdfParseContext context = new SdfParseContext();
                SdfNode parsed = SdfParser.Parse(expressions[e], context);
                parsed.Bounds(out SdfVector lo, out SdfVector up, out bool grows);
                Point3f lower = new Point3f((Float)(lo.x - 0.05), (Float)(lo.y - 0.05), (Float)(lo.z - 0.05));
                Point3f upper = new Point3f((Float)(up.x + 0.05), (Float)(up.y + 0.05), (Float)(up.z + 0.05));
                context = new SdfParseContext();
                context.domainRadius = Math.Max(lo.Length(), up.Length());
                SdfLocalBounds.warps = true;
                parsed = SdfParser.Parse(expressions[e], context);
                SdfLocalBounds.warps = false;
                SdfNode field = SdfCompiledNode.Compile(parsed);
                SdfShapeOptions plain = new SdfShapeOptions();
                SdfShapeOptions none = new SdfShapeOptions();
                none.relaxation = 1;
                SdfShapeOptions fixed16 = new SdfShapeOptions();
                fixed16.relaxation = 1.6;
                SdfShapeOptions slope2 = new SdfShapeOptions();
                slope2.slopeRelaxation = true;
                slope2.relaxation = 2;
                SdfShapeOptions slope3 = new SdfShapeOptions();
                slope3.slopeRelaxation = true;
                slope3.relaxation = 3;
                double global = Evaluations(new GlobalOnly(field), plain, lower, upper, out int h0);
                double standard = Evaluations(field, plain, lower, upper, out int h1);
                double unrelaxed = Evaluations(field, none, lower, upper, out int h2);
                double wide = Evaluations(field, fixed16, lower, upper, out int h3);
                double two = Evaluations(field, slope2, lower, upper, out int h4);
                double three = Evaluations(field, slope3, lower, upper, out int h5);
                SdfShapeOptions along = new SdfShapeOptions();
                along.rayBounds = true;
                double ray = Evaluations(field, along, lower, upper, out int h6);
                SdfShapeOptions alongSlope = new SdfShapeOptions();
                alongSlope.rayBounds = true;
                alongSlope.slopeRelaxation = true;
                alongSlope.relaxation = 2;
                double raySlope = Evaluations(field, alongSlope, lower, upper, out int h7);
                Console.WriteLine("  {0}: with the bound along the ray {1:F1}, with it and the slope relaxation {2:F1}; hits {3} {4}", names[e], ray, raySlope, h6, h7);
                CHECK(h6 == h1 && h7 == h1);
                Console.WriteLine("{0}: values per ray: no relaxation {1:F1}, fixed 1.2 {2:F1} (without local bounds {3:F1}), fixed 1.6 {4:F1}, slope up to 2 {5:F1}, slope up to 3 {6:F1}; hits {7} {8} {9} {10} {11} {12}",
                                  names[e], unrelaxed, standard, global, wide, two, three, h0, h1, h2, h3, h4, h5);
                CHECK(h0 == h1 && h1 == h2 && h2 == h3 && h3 == h4 && h4 == h5);
            }
        }

        // Rays against two shapes of one field: the number that hit both, hit one only, and hit at different distances
        private static void CompareShapes(SdfShape first, SdfShape second, double extent, out int hits, out int different, out int apart)
        {
            RNG rng = new RNG(83);
            hits = 0;
            different = 0;
            apart = 0;
            for (int i = 0; i < 40000; ++i)
            {
                Vector3f from = Normalize(new Vector3f((Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1), (Float)(2 * rng.Uniform<double>() - 1)));
                Point3f target = new Point3f((Float)(extent * (2 * rng.Uniform<double>() - 1)), (Float)(extent * (2 * rng.Uniform<double>() - 1)),
                                             (Float)(extent * (2 * rng.Uniform<double>() - 1)));
                Point3f o = new Point3f(5 * from.x, 5 * from.y, 5 * from.z);
                Ray ray = new Ray(o, Normalize(target - o));
                pstd.optional<ShapeIntersection> ha = first.Intersect(ray), hb = second.Intersect(ray);
                if (ha.has_value() != hb.has_value())
                {
                    ++different;
                }
                else if (ha.has_value())
                {
                    ++hits;
                    apart += Math.Abs(ha.value().tHit - hb.value().tHit) > 1e-5 ? 1 : 0;
                }
            }
        }

        // The fields of the cells cut down level by level against every cell from the whole field: the same hits, and (not a
        // check) what each takes to set up
        [TEST("SdfDda", "BlocksOfCells")]
        public static void SdfDda_BlocksOfCells()
        {
            int[] counts = { 200, 1500 };
            foreach (int count in counts)
            {
                for (int smooth = 0; smooth < (count > 1000 ? 1 : 2); ++smooth)
                {
                    RNG rng = new RNG(85);
                    System.Text.StringBuilder chain = new System.Text.StringBuilder();
                    string field = "";
                    for (int i = 0; i < count; ++i)
                    {
                        double angle = 2 * Math.PI * i / count, turns = 7 * angle;
                        double x = (1.0 + 0.25 * Math.Cos(turns)) * Math.Cos(angle), y = 0.25 * Math.Sin(turns), z = (1.0 + 0.25 * Math.Cos(turns)) * Math.Sin(angle);
                        string part = "translate(" + (i % 3 == 0 ? "torus(0.05, 0.015)" : "sphere(" + (0.02 + 0.03 * rng.Uniform<double>()).ToString("F3") + ")") + ", " +
                                      x.ToString("F3") + ", " + y.ToString("F3") + ", " + z.ToString("F3") + ")";
                        field = i == 0 ? part : (smooth == 1 ? "smoothunion(" + field + ", " + part + ", 0.02)" : "union(" + field + ", " + part + ")");
                    }
                    SdfNode compiled = SdfCompiledNode.Compile(SdfParser.Parse(field, new SdfParseContext()));
                    Point3f lower = new Point3f(-1.4f, -0.4f, -1.4f), upper = new Point3f(1.4f, 0.4f, 1.4f);
                    SdfShapeOptions flat = new SdfShapeOptions();
                    flat.ddaResolution = 64;
                    flat.prune = true;
                    flat.pruneHierarchy = false;
                    SdfShapeOptions blocks = new SdfShapeOptions();
                    blocks.ddaResolution = 64;
                    blocks.prune = true;
                    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                    SdfShape first = new SdfShape(new Transform(), new Transform(), false, compiled, lower, upper, flat);
                    double flatSeconds = watch.Elapsed.TotalSeconds;
                    watch.Restart();
                    SdfShape second = new SdfShape(new Transform(), new Transform(), false, compiled, lower, upper, blocks);
                    double blockSeconds = watch.Elapsed.TotalSeconds;
                    CompareShapes(first, second, 1.2, out int hits, out int different, out int apart);
                    SdfShapeOptions walkedFlat = new SdfShapeOptions(), walkedBlocks = new SdfShapeOptions();
                    walkedFlat.ddaResolution = walkedBlocks.ddaResolution = 64;
                    walkedFlat.prune = walkedBlocks.prune = true;
                    walkedFlat.pruneCompile = walkedBlocks.pruneCompile = false;
                    walkedFlat.pruneHierarchy = false;
                    watch.Restart();
                    new SdfShape(new Transform(), new Transform(), false, compiled, lower, upper, walkedFlat);
                    double flatCut = watch.Elapsed.TotalSeconds;
                    watch.Restart();
                    new SdfShape(new Transform(), new Transform(), false, compiled, lower, upper, walkedBlocks);
                    Console.WriteLine("  of which finding and cutting down the cells, without compiling: {0:F2} s from the whole field, {1:F2} s level by level", flatCut, watch.Elapsed.TotalSeconds);
                    Console.WriteLine("{0} parts in a {1}: cells set up in {2:F2} s from the whole field ({3} fields), {4:F2} s level by level ({5} fields); {6} hits, {7} hit by one only, {8} apart",
                                      count, smooth == 1 ? "blend" : "union", flatSeconds, first.PrunedFields(), blockSeconds, second.PrunedFields(), hits, different, apart);
                    CHECK_GT(hits, 500);
                    CHECK_EQ(different, 0);
                    CHECK_EQ(apart, 0);
                }
            }
        }

        // The list of triangles of a region has the values of the whole mesh in it, and a mesh with lists for its cells is hit
        // like the mesh without
        [TEST("SdfDda", "TrianglesOfCells")]
        public static void SdfDda_TrianglesOfCells()
        {
            // A knotted tube: a torus knot swept by a circle
            int around = 240, across = 12;
            SdfVector[] vertices = new SdfVector[around * across];
            int[] indices = new int[around * across * 6];
            for (int i = 0; i < around; ++i)
            {
                double t = 2 * Math.PI * i / around, h = 1e-4;
                SdfVector center = Knot(t), tangent = (Knot(t + h) - Knot(t - h)).Normalized();
                SdfVector side = SdfVector.Cross(tangent, new SdfVector(0, 1, 0)).Normalized(), up = SdfVector.Cross(side, tangent);
                for (int j = 0; j < across; ++j)
                {
                    double a = 2 * Math.PI * j / across;
                    vertices[i * across + j] = center + side * (0.12 * Math.Cos(a)) + up * (0.12 * Math.Sin(a));
                    int i1 = (i + 1) % around, j1 = (j + 1) % across, at = 6 * (i * across + j);
                    indices[at] = i * across + j;
                    indices[at + 1] = i * across + j1;
                    indices[at + 2] = i1 * across + j1;
                    indices[at + 3] = i * across + j;
                    indices[at + 4] = i1 * across + j1;
                    indices[at + 5] = i1 * across + j;
                }
            }
            SdfMeshSign[] signs = { SdfMeshSign.Pseudonormal, SdfMeshSign.Winding };
            foreach (SdfMeshSign sign in signs)
            {
                SdfMeshDistance mesh = new SdfMeshDistance(vertices, indices, sign);
                RNG rng = new RNG(87);
                int lists = 0, wrong = 0, triedPoints = 0;
                for (int i = 0; i < 3000; ++i)
                {
                    SdfVector center = new SdfVector(1.2 * (2 * rng.Uniform<double>() - 1), 0.5 * (2 * rng.Uniform<double>() - 1), 1.2 * (2 * rng.Uniform<double>() - 1));
                    double radius = 0.01 + 0.05 * rng.Uniform<double>();
                    SdfNode part = mesh.Prune(center, radius, out double least, out double most);
                    // A smaller ball inside it, from the list
                    SdfVector inner = center + new SdfVector(0.3 * radius, -0.2 * radius, 0.1 * radius);
                    SdfNode smaller = part.Prune(inner, 0.5 * radius, out double innerLeast, out double innerMost);
                    lists += part is SdfMeshPart ? 1 : 0;
                    for (int k = 0; k < 20; ++k)
                    {
                        SdfVector offset = new SdfVector(2 * rng.Uniform<double>() - 1, 2 * rng.Uniform<double>() - 1, 2 * rng.Uniform<double>() - 1);
                        if (offset.Length() > 1)
                        {
                            continue;
                        }
                        SdfVector p = center + offset * radius, q = inner + offset * (0.5 * radius);
                        double expected = mesh.Evaluate(p), value = part.Evaluate(p);
                        ++triedPoints;
                        wrong += Math.Abs(value - expected) > 1e-12 || expected < least - 1e-12 || expected > most + 1e-12 ? 1 : 0;
                        wrong += Math.Abs(smaller.Evaluate(q) - mesh.Evaluate(q)) > 1e-12 ? 1 : 0;
                    }
                }
                Point3f lower = new Point3f(-1.3f, -0.6f, -1.3f), upper = new Point3f(1.3f, 0.6f, 1.3f);
                SdfShapeOptions cells = new SdfShapeOptions();
                cells.ddaResolution = 64;
                cells.prune = true;
                SdfShape first = new SdfShape(new Transform(), new Transform(), false, mesh, lower, upper, new SdfShapeOptions());
                SdfShape second = new SdfShape(new Transform(), new Transform(), false, mesh, lower, upper, cells);
                CompareShapes(first, second, 1.1, out int hits, out int different, out int apart);
                Console.WriteLine("mesh of {0} triangles, sign by {1}: {2} of 3000 balls got a list, {3} of {4} values wrong; {5} fields of cells; {6} hits, {7} hit by one only, {8} apart",
                                  indices.Length / 3, sign, lists, wrong, triedPoints, second.PrunedFields(), hits, different, apart);
                CHECK_GT(lists, 1000);
                CHECK_EQ(wrong, 0);
                CHECK_GT(hits, 500);
                CHECK_EQ(different, 0);
                CHECK_EQ(apart, 0);
            }
        }

        private static SdfVector Knot(double t)
        {
            double r = 0.8 + 0.3 * Math.Cos(3 * t);
            return new SdfVector(r * Math.Cos(2 * t), 0.3 * Math.Sin(3 * t), r * Math.Sin(2 * t));
        }

        [TEST("SdfDda", "SameHits")]
        public static void SdfDda_SameHits()
        {
            Compare("subtract(cylinder(0.5, 0.6), translate(cylinder(0.42, 0.6), 0, 0.1, 0))", new Point3f(-0.52f, -0.62f, -0.52f), new Point3f(0.52f, 0.62f, 0.52f));
            Compare("smoothunion(sphere(0.5), translate(torus(0.5, 0.15), 0.3, 0.2, 0), 0.15)", new Point3f(-0.6f, -0.6f, -0.7f), new Point3f(1.0f, 0.6f, 0.7f));
            Compare("twist(box(0.4, 0.5, 0.2, 0.05), 1.2)", new Point3f(-0.6f, -0.6f, -0.6f), new Point3f(0.6f, 0.6f, 0.6f));
        }
    }
}
