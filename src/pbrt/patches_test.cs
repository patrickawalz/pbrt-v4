// (not in pbrt-v4) Tests of the patch primitives (shapes_patches.cs): the change of basis against the direct evaluation of the basis formulas, ray
// intersection against dense point sets and against the evaluated surface, the patch counts and parameterization of meshes (periodic or not,
// any step), and the area and sampling.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class PatchTests
    {
        private static readonly string[] Bases = { "bezier", "b-spline", "catmull-rom", "hermite", "power" };

        private static SdfVector[,] RandomNet(Random rng, double bend)
        {
            SdfVector[,] g = new SdfVector[4, 4];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    g[i, j] = new SdfVector(i + bend * (rng.NextDouble() - 0.5), j + bend * (rng.NextDouble() - 0.5), bend * (rng.NextDouble() - 0.5));
                }
            }
            return g;
        }

        [TEST("Patch", "ChangeOfBasisIsExact")]
        public static void ChangeOfBasisIsExact()
        {
            Random rng = new Random(3);
            double worst = 0;
            foreach (string ub in Bases)
            {
                foreach (string vb in Bases)
                {
                    double[,] um, vm;
                    PatchBases.TryGet(ub, out um);
                    PatchBases.TryGet(vb, out vm);
                    SdfVector[,] g = RandomNet(rng, 1.5);
                    BezierPatch4 bezier = PatchBases.ToBezier(g, um, vm);
                    for (int k = 0; k < 40; ++k)
                    {
                        double u = rng.NextDouble(), v = rng.NextDouble();
                        SdfVector a = bezier.Evaluate(u, v), b = PatchBases.Evaluate(g, um, vm, u, v);
                        worst = Math.Max(worst, (a - b).Length());
                    }
                }
            }
            EXPECT_LT(worst, 1e-9, "conversion error");
            // the textbook properties: catmull-rom interpolates the middle points, hermite the end points, b-spline averages (1, 4, 1) / 6
            double[,] cr, he, bs;
            PatchBases.TryGet("catmull-rom", out cr);
            PatchBases.TryGet("hermite", out he);
            PatchBases.TryGet("b-spline", out bs);
            SdfVector[,] net = RandomNet(rng, 2);
            EXPECT_LT((PatchBases.Evaluate(net, cr, cr, 0, 0) - net[1, 1]).Length(), 1e-12);
            EXPECT_LT((PatchBases.Evaluate(net, cr, cr, 1, 1) - net[2, 2]).Length(), 1e-12);
            EXPECT_LT((PatchBases.Evaluate(net, he, he, 0, 0) - net[0, 0]).Length(), 1e-12);
            EXPECT_LT((PatchBases.Evaluate(net, he, he, 1, 1) - net[2, 2]).Length(), 1e-12);
            SdfVector expected = new SdfVector(0, 0, 0);
            double[] w = { 1.0 / 6, 4.0 / 6, 1.0 / 6 };
            for (int i = 0; i < 3; ++i)
            {
                for (int j = 0; j < 3; ++j)
                {
                    expected += (w[i] * w[j]) * net[i, j];
                }
            }
            EXPECT_LT((PatchBases.Evaluate(net, bs, bs, 0, 0) - expected).Length(), 1e-12);
        }

        private static ParsedParameter Param(string type, string name, Float[] floats, int[] ints, string[] strings)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = type;
            p.name = name;
            if (floats != null)
            {
                foreach (Float v in floats)
                {
                    p.floats.push_back(v);
                }
            }
            if (ints != null)
            {
                foreach (int v in ints)
                {
                    p.ints.push_back(v);
                }
            }
            if (strings != null)
            {
                foreach (string v in strings)
                {
                    p.strings.push_back(v);
                }
            }
            return p;
        }

        private static pstd.vector<Shape> CreatePatchShapes(string name, Transform t, bool reverse, SdfVector[,] points, int nu, int nv, string type, string ub, string vb,
                                                            int uStep, int vStep, bool uPeriodic, bool vPeriodic, Float[] uv)
        {
            Float[] p = new Float[3 * nu * nv];
            for (int j = 0; j < nv; ++j)
            {
                for (int i = 0; i < nu; ++i)
                {
                    p[3 * (j * nu + i)] = (Float)points[i, j].x;
                    p[3 * (j * nu + i) + 1] = (Float)points[i, j].y;
                    p[3 * (j * nu + i) + 2] = (Float)points[i, j].z;
                }
            }
            List<ParsedParameter> list = new List<ParsedParameter>();
            list.Add(Param("point3", "P", p, null, null));
            list.Add(Param("string", "type", null, null, new string[] { type }));
            list.Add(Param("string", "ubasis", null, null, new string[] { ub }));
            list.Add(Param("string", "vbasis", null, null, new string[] { vb }));
            list.Add(Param("string", "uwrap", null, null, new string[] { uPeriodic ? "periodic" : "nonperiodic" }));
            list.Add(Param("string", "vwrap", null, null, new string[] { vPeriodic ? "periodic" : "nonperiodic" }));
            if (name == "patchmesh")
            {
                list.Add(Param("integer", "nu", null, new int[] { nu }, null));
                list.Add(Param("integer", "nv", null, new int[] { nv }, null));
            }
            list.Add(Param("integer", "ustep", null, new int[] { uStep }, null));
            list.Add(Param("integer", "vstep", null, new int[] { vStep }, null));
            if (uv != null)
            {
                list.Add(Param("point2", "uv", uv, null, null));
            }
            ParameterDictionary dict = new ParameterDictionary(list, RGBColorSpace.sRGB);
            return Shape.Create(name, t, Inverse(t), reverse, dict, new Dictionary<string, FloatTexture>(), null, new Allocator());
        }

        // The control net of the patch (pu, pv) of a mesh, as the patch mesh takes it
        private static SdfVector[,] PatchNet(SdfVector[,] points, int nu, int nv, int pu, int pv, int uStep, int vStep, bool uPeriodic, bool vPeriodic)
        {
            SdfVector[,] g = new SdfVector[4, 4];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    int iu = pu * uStep + i, iv = pv * vStep + j;
                    g[i, j] = points[uPeriodic ? iu % nu : iu, vPeriodic ? iv % nv : iv];
                }
            }
            return g;
        }

        // The surface of the mesh at the global parameters (U, V), from the basis formulas
        private static SdfVector Surface(SdfVector[,] points, int nu, int nv, int nuPatches, int nvPatches, int uStep, int vStep, bool uPeriodic, bool vPeriodic,
                                         double[,] um, double[,] vm, double U, double V)
        {
            int pu = Math.Min(nuPatches - 1, (int)Math.Floor(U * nuPatches)), pv = Math.Min(nvPatches - 1, (int)Math.Floor(V * nvPatches));
            return PatchBases.Evaluate(PatchNet(points, nu, nv, pu, pv, uStep, vStep, uPeriodic, vPeriodic), um, vm, U * nuPatches - pu, V * nvPatches - pv);
        }

        private static double DistanceToRay(SdfVector o, SdfVector d, SdfVector p)
        {
            double t = SdfVector.Dot(p - o, d);
            return t < 0 ? (p - o).Length() : (p - (o + t * d)).Length();
        }

        // Rays against a mesh: those aimed at a point of the surface hit it there (or earlier, on a fold) and the uv of the hit gives the point;
        // those that pass far from a dense set of points of the surface miss
        private static void CheckRays(string label, SdfVector[,] points, int nu, int nv, int nuPatches, int nvPatches, int uStep, int vStep, bool uPeriodic,
                                      bool vPeriodic, double[,] um, double[,] vm, pstd.vector<Shape> shapes, Random rng, bool hitsAtSeams)
        {
            int resolution = 60 * Math.Max(nuPatches, nvPatches);
            List<SdfVector> dense = new List<SdfVector>();
            SdfVector low = new SdfVector(1e30, 1e30, 1e30), high = new SdfVector(-1e30, -1e30, -1e30);
            for (int i = 0; i <= resolution; ++i)
            {
                for (int j = 0; j <= resolution; ++j)
                {
                    SdfVector s = Surface(points, nu, nv, nuPatches, nvPatches, uStep, vStep, uPeriodic, vPeriodic, um, vm,
                                          Math.Min(i / (double)resolution, 1 - 1e-12), Math.Min(j / (double)resolution, 1 - 1e-12));
                    dense.Add(s);
                    low = new SdfVector(Math.Min(low.x, s.x), Math.Min(low.y, s.y), Math.Min(low.z, s.z));
                    high = new SdfVector(Math.Max(high.x, s.x), Math.Max(high.y, s.y), Math.Max(high.z, s.z));
                }
            }
            double size = (high - low).Length();
            double spacing = size / (resolution * 0.5);
            SdfVector center = 0.5 * (low + high);

            int aimedMisses = 0, wrongPoint = 0, wrongUv = 0, aimed = 0;
            for (int k = 0; k < 400; ++k)
            {
                double U = hitsAtSeams ? Math.Min(0.999, ((int)(rng.NextDouble() * nuPatches) + (rng.Next(2) == 0 ? 1e-4 : 1 - 1e-4)) / nuPatches) : rng.NextDouble();
                double V = rng.NextDouble();
                SdfVector target = Surface(points, nu, nv, nuPatches, nvPatches, uStep, vStep, uPeriodic, vPeriodic, um, vm, U, V);
                SdfVector direction = new SdfVector(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
                direction = direction / direction.Length();
                SdfVector origin = target - 2 * size * direction;
                ++aimed;
                Ray ray = new Ray(new Point3f((Float)origin.x, (Float)origin.y, (Float)origin.z), new Vector3f((Float)direction.x, (Float)direction.y, (Float)direction.z));
                pstd.optional<ShapeIntersection> best = default;
                for (int s = 0; s < (int)shapes.size(); ++s)
                {
                    pstd.optional<ShapeIntersection> si = shapes[s].Intersect(ray);
                    if (si.has_value() && (!best.has_value() || si.value().tHit < best.value().tHit))
                    {
                        best = si;
                    }
                }
                if (!best.has_value())
                {
                    ++aimedMisses;
                    continue;
                }
                Point3f hit = best.value().intr.p();
                // the hit point is on the ray, and it is the surface at its uv
                double hu = best.value().intr.uv.x, hv = best.value().intr.uv.y;
                SdfVector atUv = Surface(points, nu, nv, nuPatches, nvPatches, uStep, vStep, uPeriodic, vPeriodic, um, vm,
                                         Math.Min(Math.Max(hu, 0), 1 - 1e-12), Math.Min(Math.Max(hv, 0), 1 - 1e-12));
                if ((atUv - new SdfVector(hit.x, hit.y, hit.z)).Length() > 2e-4 * size)
                {
                    ++wrongUv;
                }
                if (DistanceToRay(origin, direction, new SdfVector(hit.x, hit.y, hit.z)) > 1e-4 * size)
                {
                    ++wrongPoint;
                }
            }
            EXPECT_EQ(0, aimedMisses, label + " rays aimed at the surface that miss (of " + aimed + ")");
            EXPECT_EQ(0, wrongPoint, label + " hit points off the ray");
            EXPECT_EQ(0, wrongUv, label + " hit points that are not the surface at the uv of the hit");

            int violations = 0, misses = 0, hits = 0;
            for (int k = 0; k < 500; ++k)
            {
                SdfVector origin = center + 1.5 * size * new SdfVector(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
                SdfVector target = center + 0.7 * size * new SdfVector(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
                SdfVector direction = (target - origin) / (target - origin).Length();
                double nearest = double.PositiveInfinity;
                foreach (SdfVector s in dense)
                {
                    nearest = Math.Min(nearest, DistanceToRay(origin, direction, s));
                }
                Ray ray = new Ray(new Point3f((Float)origin.x, (Float)origin.y, (Float)origin.z), new Vector3f((Float)direction.x, (Float)direction.y, (Float)direction.z));
                bool hit = false;
                for (int s = 0; s < (int)shapes.size() && !hit; ++s)
                {
                    hit = shapes[s].IntersectP(ray);
                }
                if (hit)
                {
                    ++hits;
                }
                else
                {
                    ++misses;
                }
                // the dense points are within spacing of every point of the surface: a ray that passes farther than that from all of them misses, and a
                // ray that passes within 1e-3 of the size of one that is a sample of the surface cannot miss the surface by more than the spacing
                if (nearest > spacing && hit)
                {
                    ++violations;
                }
                if (nearest < 1e-4 * size && !hit)
                {
                    ++violations;
                }
            }
            EXPECT_EQ(0, violations, label + " rays whose hit or miss contradicts the dense point set (" + hits + " hits, " + misses + " misses)");
            EXPECT_GT(hits, 20, label + " hits");
            EXPECT_GT(misses, 20, label + " misses");
        }

        [TEST("Patch", "BicubicPatchesOfEveryBasis")]
        public static void BicubicPatchesOfEveryBasis()
        {
            Random rng = new Random(21);
            Transform rigid = Translate(new Vector3f(0.3f, -0.2f, 0.5f)) * Rotate(33, Normalize(new Vector3f(1, 0.5f, -1)));
            foreach (string ub in Bases)
            {
                foreach (string vb in new string[] { "bezier", "catmull-rom", "hermite" })
                {
                    SdfVector[,] net = RandomNet(rng, 1.2);
                    pstd.vector<Shape> shapes = CreatePatchShapes("patch", new Transform(), false, net, 4, 4, "bicubic", ub, vb, 3, 3, false, false, null);
                    ASSERT_EQ(1, (int)shapes.size());
                    double[,] um, vm;
                    PatchBases.TryGet(ub, out um);
                    PatchBases.TryGet(vb, out vm);
                    CheckRays("bicubic " + ub + " x " + vb, net, 4, 4, 1, 1, 3, 3, false, false, um, vm, shapes, rng, false);
                }
            }
            // under a transform: the hit points are in render space
            SdfVector[,] net2 = RandomNet(rng, 1.0);
            pstd.vector<Shape> moved = CreatePatchShapes("patch", rigid, false, net2, 4, 4, "bicubic", "b-spline", "b-spline", 1, 1, false, false, null);
            double[,] bs;
            PatchBases.TryGet("b-spline", out bs);
            int wrong = 0;
            for (int k = 0; k < 200; ++k)
            {
                double u = rng.NextDouble(), v = rng.NextDouble();
                SdfVector s = PatchBases.Evaluate(net2, bs, bs, u, v);
                Point3f target = rigid.Apply(new Point3f((Float)s.x, (Float)s.y, (Float)s.z));
                Vector3f d = Normalize(new Vector3f((Float)(rng.NextDouble() - 0.5), (Float)(rng.NextDouble() - 0.5), (Float)(rng.NextDouble() - 0.5)));
                pstd.optional<ShapeIntersection> si = moved[0].Intersect(new Ray(target - 3 * d, d));
                if (!si.has_value())
                {
                    ++wrong;
                    continue;
                }
                // (an earlier hit than the aimed point is possible on a fold): the hit is on the ray and is the surface at its uv, in render space
                SdfVector atUv = PatchBases.Evaluate(net2, bs, bs, Math.Min(Math.Max(si.value().intr.uv.x, 0), 1), Math.Min(Math.Max(si.value().intr.uv.y, 0), 1));
                Point3f expected = rigid.Apply(new Point3f((Float)atUv.x, (Float)atUv.y, (Float)atUv.z));
                if (Distance(si.value().intr.p(), expected) > 1e-3f || si.value().tHit > 3.0001f)
                {
                    ++wrong;
                }
            }
            EXPECT_EQ(0, wrong, "rays at a transformed patch");
        }

        [TEST("Patch", "MeshesCountAndParameterize")]
        public static void MeshesCountAndParameterize()
        {
            Random rng = new Random(8);
            // nonperiodic: bezier (7 points, step 3: 2 patches), b-spline (6 points, step 1: 3 patches), catmull-rom (5 points: 2 patches)
            // periodic along u: b-spline ring of 8 points (8 patches), step 2 (4 patches)
            int[][] configs = new int[][] {
                // basis index into names, nu, nv, ustep, vstep, uPeriodic, vPeriodic, expected nuPatches, expected nvPatches
                new int[] { 0, 7, 4, 3, 3, 0, 0, 2, 1 },
                new int[] { 1, 6, 5, 1, 1, 0, 0, 3, 2 },
                new int[] { 2, 5, 4, 1, 1, 0, 0, 2, 1 },
                new int[] { 1, 8, 4, 1, 1, 1, 0, 8, 1 },
                new int[] { 1, 8, 4, 2, 1, 1, 0, 4, 1 },
                new int[] { 1, 6, 6, 1, 1, 1, 1, 6, 6 },
                new int[] { 2, 6, 7, 3, 3, 1, 0, 2, 2 },
            };
            string[] names = { "bezier", "b-spline", "catmull-rom" };
            foreach (int[] c in configs)
            {
                string basis = names[c[0]];
                int nu = c[1], nv = c[2];
                bool uPeriodic = c[5] != 0, vPeriodic = c[6] != 0;
                SdfVector[,] points = new SdfVector[nu, nv];
                for (int j = 0; j < nv; ++j)
                {
                    for (int i = 0; i < nu; ++i)
                    {
                        // a bumpy sheet, rolled up into a ring along u if periodic along u
                        double angle = 2 * Math.PI * i / nu;
                        double radius = 3 + 0.3 * (rng.NextDouble() - 0.5);
                        points[i, j] = uPeriodic ? new SdfVector(radius * Math.Cos(angle), radius * Math.Sin(angle), j + 0.3 * (rng.NextDouble() - 0.5))
                                                 : new SdfVector(i + 0.3 * (rng.NextDouble() - 0.5), j + 0.3 * (rng.NextDouble() - 0.5), 0.5 * (rng.NextDouble() - 0.5));
                    }
                }
                string label = "mesh " + basis + " nu " + nu + " nv " + nv + " step " + c[3] + "," + c[4] + " periodic " + uPeriodic + "," + vPeriodic;
                pstd.vector<Shape> shapes = CreatePatchShapes("patchmesh", new Transform(), false, points, nu, nv, "bicubic", basis, basis, c[3], c[4], uPeriodic, vPeriodic, null);
                EXPECT_EQ(c[7] * c[8], (int)shapes.size(), label + " patch count");
                double[,] m;
                PatchBases.TryGet(basis, out m);
                CheckRays(label, points, nu, nv, c[7], c[8], c[3], c[4], uPeriodic, vPeriodic, m, m, shapes, rng, true);
                CheckRays(label, points, nu, nv, c[7], c[8], c[3], c[4], uPeriodic, vPeriodic, m, m, shapes, rng, false);
            }
        }

        [TEST("Patch", "BilinearPatches")]
        public static void BilinearPatches()
        {
            Random rng = new Random(2);
            // a mesh of 3 x 2 points: two patches, the second covers u in [0.5, 1]
            SdfVector[,] points = new SdfVector[3, 2];
            for (int j = 0; j < 2; ++j)
            {
                for (int i = 0; i < 3; ++i)
                {
                    points[i, j] = new SdfVector(i + 0.2 * (rng.NextDouble() - 0.5), j + 0.2 * (rng.NextDouble() - 0.5), 0.6 * (rng.NextDouble() - 0.5));
                }
            }
            pstd.vector<Shape> shapes = CreatePatchShapes("patchmesh", new Transform(), false, points, 3, 2, "bilinear", "bezier", "bezier", 1, 1, false, false, null);
            EXPECT_EQ(2, (int)shapes.size());
            int wrong = 0;
            for (int k = 0; k < 300; ++k)
            {
                double U = rng.NextDouble(), V = rng.NextDouble();
                int pu = U < 0.5 ? 0 : 1;
                double u = U * 2 - pu;
                SdfVector a = (1 - u) * (1 - V) * points[pu, 0] + u * (1 - V) * points[pu + 1, 0] + (1 - u) * V * points[pu, 1] + u * V * points[pu + 1, 1];
                SdfVector d = new SdfVector(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, 1);
                d = d / d.Length();
                SdfVector o = a - 2 * d;
                Ray ray = new Ray(new Point3f((Float)o.x, (Float)o.y, (Float)o.z), new Vector3f((Float)d.x, (Float)d.y, (Float)d.z));
                bool found = false;
                for (int s = 0; s < 2; ++s)
                {
                    pstd.optional<ShapeIntersection> si = shapes[s].Intersect(ray);
                    if (si.has_value())
                    {
                        found = true;
                        if (Math.Abs(si.value().intr.uv.x - U) > 2e-3 || Math.Abs(si.value().intr.uv.y - V) > 2e-3)
                        {
                            ++wrong;
                        }
                    }
                }
                if (!found)
                {
                    ++wrong;
                }
            }
            EXPECT_EQ(0, wrong, "bilinear mesh hits with the uv of RenderMan");
            // periodic along u: 3 patches
            pstd.vector<Shape> ring = CreatePatchShapes("patchmesh", new Transform(), false, points, 3, 2, "bilinear", "bezier", "bezier", 1, 1, true, false, null);
            EXPECT_EQ(3, (int)ring.size());
            // a single bilinear patch of 4 points
            SdfVector[,] quad = new SdfVector[2, 2];
            quad[0, 0] = new SdfVector(0, 0, 0);
            quad[1, 0] = new SdfVector(1, 0, 0);
            quad[0, 1] = new SdfVector(0, 1, 0);
            quad[1, 1] = new SdfVector(1, 1, 1);
            pstd.vector<Shape> single = CreatePatchShapes("patch", new Transform(), false, quad, 2, 2, "bilinear", "bezier", "bezier", 1, 1, false, false, null);
            EXPECT_EQ(1, (int)single.size());
            pstd.optional<ShapeIntersection> hit = single[0].Intersect(new Ray(new Point3f(0.5f, 0.5f, 5), new Vector3f(0, 0, -1)));
            ASSERT_TRUE(hit.has_value());
            EXPECT_NEAR(0.25, hit.value().intr.p().z, 1e-5);
            EXPECT_NEAR(0.5, hit.value().intr.uv.x, 1e-4);
            EXPECT_NEAR(0.5, hit.value().intr.uv.y, 1e-4);
        }

        [TEST("Patch", "AreaAndSampling")]
        public static void AreaAndSampling()
        {
            Random rng = new Random(12);
            foreach (string basis in new string[] { "bezier", "b-spline", "catmull-rom" })
            {
                double[,] m;
                PatchBases.TryGet(basis, out m);
                SdfVector[,] net = RandomNet(rng, 1.5);
                pstd.vector<Shape> shapes = CreatePatchShapes("patch", new Transform(), false, net, 4, 4, "bicubic", basis, basis, 1, 1, false, false, null);
                Shape shape = shapes[0];
                // the integral of the area element and of f = x + 2 y^2 + z over the surface, by the midpoint rule on a fine grid
                int n = 400;
                double area = 0, integral = 0;
                double h = 1e-5;
                for (int i = 0; i < n; ++i)
                {
                    for (int j = 0; j < n; ++j)
                    {
                        double u = (i + 0.5) / n, v = (j + 0.5) / n;
                        SdfVector p = PatchBases.Evaluate(net, m, m, u, v);
                        SdfVector du = (PatchBases.Evaluate(net, m, m, u + h, v) - PatchBases.Evaluate(net, m, m, u - h, v)) / (2 * h);
                        SdfVector dv = (PatchBases.Evaluate(net, m, m, u, v + h) - PatchBases.Evaluate(net, m, m, u, v - h)) / (2 * h);
                        SdfVector c = new SdfVector(du.y * dv.z - du.z * dv.y, du.z * dv.x - du.x * dv.z, du.x * dv.y - du.y * dv.x);
                        double dA = c.Length() / ((double)n * n);
                        area += dA;
                        integral += (p.x + 2 * p.y * p.y + p.z) * dA;
                    }
                }
                EXPECT_NEAR(1.0, shape.Area() / area, 2e-3, basis + " Area()");
                // E[f / pdf] over the samples against the integral, with its standard error
                int nSamples = 200000;
                double sum = 0, sumSquares = 0;
                for (int i = 0; i < nSamples; ++i)
                {
                    pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                    ASSERT_TRUE(ss.has_value());
                    Point3f p = ss.value().intr.p();
                    double f = (p.x + 2 * p.y * p.y + p.z) / ss.value().pdf;
                    sum += f;
                    sumSquares += f * f;
                }
                double mean = sum / nSamples, error = Math.Sqrt((sumSquares / nSamples - mean * mean) / nSamples);
                EXPECT_LT(Math.Abs(mean - integral) / error, 4.0, basis + " E[f / pdf] " + mean + " +- " + error + " against " + integral);
                // the solid angle pdf of Sample(ctx, u) is PDF(ctx, wi) for the samples that are the first hit (PDF(Interaction) of the Bezier clip
                // patches is 1 / Area(), which is not the pdf of its table sampler: not used here)
                ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(new Point3f(1.5f, 1.5f, 6)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
                int compared = 0, inconsistent = 0;
                for (int i = 0; i < 3000; ++i)
                {
                    pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                    if (!ss.has_value())
                    {
                        continue;
                    }
                    Vector3f wi = Normalize(ss.value().intr.p() - ctx.p());
                    pstd.optional<ShapeIntersection> first = shape.Intersect(new Ray(ctx.p(), wi));
                    if (!first.has_value() || Math.Abs(first.value().tHit - Distance(ss.value().intr.p(), ctx.p())) > 1e-3f ||
                        AbsDot(first.value().intr.n, -wi) < 0.1f)
                    {
                        continue;
                    }
                    ++compared;
                    if (Math.Abs(shape.PDF(ctx, wi) / ss.value().pdf - 1) > 1e-2)
                    {
                        ++inconsistent;
                    }
                }
                EXPECT_GT(compared, 1000, basis + " samples compared");
                EXPECT_EQ(0, inconsistent, basis + " Sample(ctx).pdf against PDF(ctx, wi) (of " + compared + ")");
            }
        }
    }
}  // namespace pbrt
