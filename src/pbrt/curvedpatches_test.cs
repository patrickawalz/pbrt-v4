// (not in pbrt-v4) curvedpatches_test.cs
//
// Tests of the PN, Phong and Gregory patches (curvedpatches.cs, phongtriangle.cs).

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class CurvedPatchTests
    {
        private static BezierSurfaceSettings Settings()
        {
            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = new Transform();
            settings.objectFromRender = new Transform();
            settings.epsilon = 1e-6;
            return settings;
        }

        // An octahedron with the normals of the sphere
        private static CurvedMesh Octahedron(double alpha)
        {
            CurvedMesh mesh = new CurvedMesh();
            mesh.settings = Settings();
            mesh.positions = new SdfVector[]
            {
                new SdfVector(1, 0, 0), new SdfVector(-1, 0, 0), new SdfVector(0, 1, 0),
                new SdfVector(0, -1, 0), new SdfVector(0, 0, 1), new SdfVector(0, 0, -1)
            };
            mesh.normals = new SdfVector[6];
            for (int i = 0; i < 6; ++i)
            {
                mesh.normals[i] = mesh.positions[i];
            }
            mesh.indices = new int[]
            {
                0, 2, 4,   2, 1, 4,   1, 3, 4,   3, 0, 4,
                2, 0, 5,   1, 2, 5,   3, 1, 5,   0, 3, 5
            };
            mesh.alpha = alpha;
            return mesh;
        }

        private static double Closest(pstd.vector<Shape> shapes, SdfVector origin, SdfVector direction)
        {
            Ray ray = new Ray(new Point3f((Float)origin.x, (Float)origin.y, (Float)origin.z),
                              new Vector3f((Float)direction.x, (Float)direction.y, (Float)direction.z));
            double best = double.MaxValue;
            for (int s = 0; s < (int)shapes.size(); ++s)
            {
                pstd.optional<ShapeIntersection> hit = shapes[s].Intersect(ray);
                if (hit && hit.value().tHit < best)
                {
                    best = hit.value().tHit;
                }
            }
            return best;
        }

        [TEST("Curved", "BezierFitReproducesBicubics")]
        public static void BezierFitReproducesBicubics()
        {
            BezierPatch4 patch = BezierFit.FromFunction((double u, double v) =>
                new SdfVector(u * u * u - 2 * u * v, v * v * v + u * u * v * v, 3 * u * v * v - u + 0.5));
            double worst = 0;
            for (int i = 0; i <= 10; ++i)
            {
                for (int j = 0; j <= 10; ++j)
                {
                    double u = i / 10.0, v = j / 10.0;
                    SdfVector expected = new SdfVector(u * u * u - 2 * u * v, v * v * v + u * u * v * v, 3 * u * v * v - u + 0.5);
                    worst = Math.Max(worst, (patch.Evaluate(u, v) - expected).Length());
                }
            }
            CHECK_LT(worst, 1e-12);
        }

        // The distance of a point from the Phong triangle: Gauss-Newton on the squared distance from the best point of a coarse grid
        private static double SurfaceDistance(PhongTriangle surface, SdfVector point)
        {
            double bs = 0, bt = 0, best = double.MaxValue;
            int n = 24;
            for (int j = 0; j <= n; ++j)
            {
                for (int i = 0; i <= n - j; ++i)
                {
                    double d = (surface.Position((double)i / n, (double)j / n) - point).Length();
                    if (d < best)
                    {
                        best = d;
                        bs = (double)i / n;
                        bt = (double)j / n;
                    }
                }
            }
            double h = 1e-6;
            for (int iteration = 0; iteration < 30; ++iteration)
            {
                SdfVector x = surface.Position(bs, bt) - point;
                SdfVector xs = (surface.Position(bs + h, bt) - surface.Position(bs - h, bt)) / (2 * h);
                SdfVector xt = (surface.Position(bs, bt + h) - surface.Position(bs, bt - h)) / (2 * h);
                double a = SdfVector.Dot(xs, xs), b = SdfVector.Dot(xs, xt), c = SdfVector.Dot(xt, xt);
                double r0 = SdfVector.Dot(xs, x), r1 = SdfVector.Dot(xt, x);
                double det = a * c - b * b;
                if (Math.Abs(det) < 1e-18)
                {
                    break;
                }
                bs -= (c * r0 - b * r1) / det;
                bt -= (a * r1 - b * r0) / det;
            }
            return (surface.Position(bs, bt) - point).Length();
        }

        [TEST("Curved", "PhongConicsMatchBezierClipAndTessellation")]
        public static void PhongConicsMatchBezierClipAndTessellation()
        {
            CurvedMesh mesh = Octahedron(0.75);
            pstd.vector<Shape> conic = new pstd.vector<Shape>();
            pstd.vector<Shape> clip = new pstd.vector<Shape>();
            List<SdfVector[]> tessellation = new List<SdfVector[]>();
            int n = 40;
            for (int f = 0; f < 8; ++f)
            {
                conic.push_back(new PhongTriangleShape(mesh, 3 * f, f));
                SdfVector[] p = new SdfVector[3];
                SdfVector[] nn = new SdfVector[3];
                for (int k = 0; k < 3; ++k)
                {
                    p[k] = mesh.positions[mesh.indices[3 * f + k]];
                    nn[k] = mesh.normals[mesh.indices[3 * f + k]];
                }
                CurvedPatches.AddPhongTriangle(clip, mesh.settings, p, nn, mesh.alpha, null, f);
                PhongTriangle surface = new PhongTriangle(p, nn, mesh.alpha);
                for (int j = 0; j < n; ++j)
                {
                    for (int i = 0; i < n - j; ++i)
                    {
                        SdfVector a = surface.Position((double)i / n, (double)j / n);
                        SdfVector b = surface.Position((double)(i + 1) / n, (double)j / n);
                        SdfVector c = surface.Position((double)i / n, (double)(j + 1) / n);
                        tessellation.Add(new SdfVector[] { a, b, c });
                        if (i + j < n - 1)
                        {
                            SdfVector d = surface.Position((double)(i + 1) / n, (double)(j + 1) / n);
                            tessellation.Add(new SdfVector[] { b, d, c });
                        }
                    }
                }
            }
            Random random = new Random(11);
            int total = 0, missed = 0, extra = 0;
            double worstDistance = 0, worstClip = 0, worstTessellation = 0;
            for (int k = 0; k < 2000; ++k)
            {
                SdfVector dir = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
                SdfVector target = new SdfVector(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1) * 0.8;
                SdfVector origin = target - dir * 5;
                double expected = double.MaxValue;
                foreach (SdfVector[] tri in tessellation)
                {
                    SubdivisionSurfaceTests.MoellerHit(origin, dir, tri, out double t);
                    if (t > 0 && t < expected)
                    {
                        expected = t;
                    }
                }
                double fromConic = Closest(conic, origin, dir);
                double fromClip = Closest(clip, origin, dir);
                bool conicHit = fromConic < double.MaxValue, clipHit = fromClip < double.MaxValue;
                if (expected < double.MaxValue)
                {
                    ++total;
                    if (!conicHit)
                    {
                        ++missed;
                    }
                    else
                    {
                        worstTessellation = Math.Max(worstTessellation, Math.Abs(fromConic - expected));
                    }
                }
                else if (conicHit)
                {
                    ++extra;
                }
                if (conicHit && clipHit)
                {
                    double difference = Math.Abs(fromConic - fromClip);
                    worstClip = Math.Max(worstClip, difference);
                }
                if (conicHit)
                {
                    // The hit point lies on the surface: the distance to the closest triangle of the mesh
                    double distance = double.MaxValue;
                    for (int f = 0; f < 8; ++f)
                    {
                        SdfVector[] p = new SdfVector[3];
                        SdfVector[] nn = new SdfVector[3];
                        for (int q = 0; q < 3; ++q)
                        {
                            p[q] = mesh.positions[mesh.indices[3 * f + q]];
                            nn[q] = mesh.normals[mesh.indices[3 * f + q]];
                        }
                        distance = Math.Min(distance, SurfaceDistance(new PhongTriangle(p, nn, mesh.alpha), origin + dir * fromConic));
                    }
                    worstDistance = Math.Max(worstDistance, distance);
                }
            }
            Console.WriteLine("phong: hits " + total + " missed " + missed + " extra " + extra + " worst distance of the hit from the surface " + worstDistance + " worst vs clip " + worstClip + " worst vs tessellation " + worstTessellation);
            CHECK_GT(total, 500);
            CHECK_LT(missed, 4);
            CHECK_LT(extra, 4);
            // The conic hits are on the surface to the precision of the single precision ray; the leaves of the clipping are a little coarser
            CHECK_LT(worstDistance, 1e-5);
            CHECK_LT(worstClip, 1e-3);
            CHECK_LT(worstTessellation, 1e-2);
        }

        [TEST("Curved", "PnQuadsAreExactAndContinuous")]
        public static void PnTriangleQuadsAreExactAndContinuous()
        {
            SdfVector[] p = { new SdfVector(0, 0, 0), new SdfVector(1, 0.1, 0), new SdfVector(0.2, 1, 0.3) };
            SdfVector[] nn = { new SdfVector(0, 0.2, 1).Normalized(), new SdfVector(0.3, 0, 1).Normalized(), new SdfVector(-0.2, -0.1, 1).Normalized() };
            PnTriangle triangle = new PnTriangle(p, nn);
            // The corners are interpolated, and the quads agree where they meet
            CHECK_LT((triangle.Position(0, 0) - p[0]).Length(), 1e-14);
            CHECK_LT((triangle.Position(1, 0) - p[1]).Length(), 1e-14);
            CHECK_LT((triangle.Position(0, 1) - p[2]).Length(), 1e-14);
            CHECK_LT((triangle.Normal(1, 0) - nn[1]).Length(), 1e-14);
            double worst = 0;
            for (int quad = 0; quad < 3; ++quad)
            {
                TriangleQuadMap map = new TriangleQuadMap(quad);
                BezierPatch4 patch = BezierFit.FromFunction((double u, double v) =>
                {
                    map.Evaluate(u, v, out double s, out double t);
                    return triangle.Position(s, t);
                });
                for (int i = 0; i <= 7; ++i)
                {
                    for (int j = 0; j <= 7; ++j)
                    {
                        double u = i / 7.0, v = j / 7.0;
                        map.Evaluate(u, v, out double s, out double t);
                        worst = Math.Max(worst, (patch.Evaluate(u, v) - triangle.Position(s, t)).Length());
                    }
                }
            }
            CHECK_LT(worst, 1e-12);
        }

        [TEST("Curved", "PnAndPhongShapesMatchTessellation")]
        public static void PnAndPhongShapesMatchTessellation()
        {
            CurvedMesh mesh = Octahedron(0.75);
            pstd.vector<Shape> pn = new pstd.vector<Shape>();
            pstd.vector<Shape> quads = new pstd.vector<Shape>();
            List<SdfVector[]> tessellation = new List<SdfVector[]>();
            int n = 32;
            for (int f = 0; f < 8; ++f)
            {
                SdfVector[] p = new SdfVector[3];
                SdfVector[] nn = new SdfVector[3];
                for (int k = 0; k < 3; ++k)
                {
                    p[k] = mesh.positions[mesh.indices[3 * f + k]];
                    nn[k] = mesh.normals[mesh.indices[3 * f + k]];
                }
                CurvedPatches.AddPnTriangle(pn, mesh.settings, p, nn, null, f);
                PnTriangle surface = new PnTriangle(p, nn);
                for (int j = 0; j < n; ++j)
                {
                    for (int i = 0; i < n - j; ++i)
                    {
                        tessellation.Add(new SdfVector[] { surface.Position((double)i / n, (double)j / n), surface.Position((double)(i + 1) / n, (double)j / n),
                                                           surface.Position((double)i / n, (double)(j + 1) / n) });
                        if (i + j < n - 1)
                        {
                            tessellation.Add(new SdfVector[] { surface.Position((double)(i + 1) / n, (double)j / n),
                                                               surface.Position((double)(i + 1) / n, (double)(j + 1) / n),
                                                               surface.Position((double)i / n, (double)(j + 1) / n) });
                        }
                    }
                }
            }
            // A Phong quad (a cube face with the normals of the sphere) against its own tessellation
            SdfVector[] cp = { new SdfVector(-1, -1, 1), new SdfVector(1, -1, 1), new SdfVector(-1, 1, 1), new SdfVector(1, 1, 1) };
            SdfVector[] cn = new SdfVector[4];
            for (int i = 0; i < 4; ++i)
            {
                cn[i] = cp[i].Normalized();
            }
            CurvedPatches.AddPhongQuad(quads, mesh.settings, cp, cn, 0.75, null, 0);
            Random random = new Random(3);
            int total = 0, missed = 0, extra = 0;
            double worst = 0;
            for (int k = 0; k < 2000; ++k)
            {
                SdfVector dir = new SdfVector(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5).Normalized();
                SdfVector target = new SdfVector(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1) * 0.8;
                SdfVector origin = target - dir * 5;
                double expected = double.MaxValue;
                foreach (SdfVector[] tri in tessellation)
                {
                    SubdivisionSurfaceTests.MoellerHit(origin, dir, tri, out double t);
                    if (t > 0 && t < expected)
                    {
                        expected = t;
                    }
                }
                double actual = Closest(pn, origin, dir);
                if (expected < double.MaxValue)
                {
                    ++total;
                    if (actual == double.MaxValue)
                    {
                        ++missed;
                    }
                    else
                    {
                        worst = Math.Max(worst, Math.Abs(actual - expected));
                    }
                }
                else if (actual < double.MaxValue)
                {
                    ++extra;
                }
            }
            Console.WriteLine("pn: hits " + total + " missed " + missed + " extra " + extra + " worst t " + worst);
            CHECK_GT(total, 500);
            CHECK_LT(missed, 4);
            CHECK_LT(extra, 4);
            CHECK_LT(worst, 1e-2);

            // The quad is hit by a ray at its center, in front of the flat quad, and not behind it
            double hit = Closest(quads, new SdfVector(0.1, 0.2, 5), new SdfVector(0, 0, -1));
            CHECK_GT(hit, 3.0);
            CHECK_LT(hit, 4.5);
            CHECK_EQ(Closest(quads, new SdfVector(0.1, 0.2, 1.5), new SdfVector(0, 0, 1)), double.MaxValue);
        }

        [TEST("Curved", "GregoryTriangleInterpolatesItsCorners")]
        public static void GregoryTriangleInterpolatesItsCorners()
        {
            SdfVector[] control = new SdfVector[18];
            Random random = new Random(2);
            for (int i = 0; i < 18; ++i)
            {
                control[i] = new SdfVector(random.NextDouble(), random.NextDouble(), random.NextDouble());
            }
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            CurvedPatches.AddGregoryTriangle(shapes, Settings(), control, null, 0);
            CHECK_EQ((int)shapes.size(), 3);
            GregoryTriangleSurface surface = new GregoryTriangleSurface(control, new TriangleQuadMap(0), PatchTextureMap.Default(), 0);
            surface.Evaluate(0, 0, out SdfVector p0, out SdfVector du, out SdfVector dv, out Point2f uv, out SdfVector ns);
            // vertex 0 of the triangle is the first control point, vertex 1 is control point 5, vertex 2 is control point 10
            CHECK_LT((p0 - control[0]).Length(), 1e-12);
            surface = new GregoryTriangleSurface(control, new TriangleQuadMap(1), PatchTextureMap.Default(), 0);
            surface.Evaluate(0, 0, out SdfVector p1, out du, out dv, out uv, out ns);
            surface = new GregoryTriangleSurface(control, new TriangleQuadMap(2), PatchTextureMap.Default(), 0);
            surface.Evaluate(0, 0, out SdfVector p2, out du, out dv, out uv, out ns);
            Console.WriteLine("gregory triangle corners: " + (p1 - control[5]).Length() + " " + (p2 - control[10]).Length());
            CHECK_LT((p1 - control[5]).Length(), 1e-9);
            CHECK_LT((p2 - control[10]).Length(), 1e-9);
        }
    }
}
