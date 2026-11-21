// (not in pbrt-v4) manifold_test.cs
//
// The specular manifold (cpu/manifold.cs), on spheres, whose derivatives are exact:
//   - the Jacobian of the constraint of a refracting vertex against finite differences, for each of its three blocks;
//   - the walk: from a chain through a refracting sphere (two specular vertices) and from a chain off a mirror sphere (one), to
//     the end of another chain traced from the same point; the walk gets there, keeps the constraints, and finds that chain;
//   - the area ratio |det(d x_1 / d x_n+1)| against the areas that two small moves of the end span at both vertices.

using System;
using testing;

namespace pbrt
{
    public static unsafe class ManifoldTests
    {
        static Sphere Ball(Float radius)
        {
            Transform identity = new Transform();
            return new Sphere(identity, identity, false, radius, -radius, radius, 360);
        }

        // The nearest hit among a glass ball of radius 1 and the inside of a screen of radius 6, both about the origin
        static SurfaceInteraction Trace(SurfaceInteraction from, Point3f origin, Vector3f direction)
        {
            Sphere[] shapes = new Sphere[] { Ball(1), Ball(6) };
            Ray ray = new Ray(origin + (Float)2e-4 * direction, direction);
            SurfaceInteraction nearest = null;
            Float distance = Infinity;
            foreach (Sphere shape in shapes)
            {
                pstd.optional<ShapeIntersection> si = shape.Intersect(ray, distance);
                if (si)
                {
                    distance = si.value().tHit;
                    nearest = si.value().intr;
                }
            }
            return nearest;
        }

        // The vertex of a ball at a point of its surface, from a ray that comes straight down on it
        static ManifoldVertex OnBall(Float radius, Point3f p)
        {
            Vector3f outward = Normalize((Vector3f)p);
            pstd.optional<ShapeIntersection> si = Ball(radius).Intersect(new Ray(new Point3f(0, 0, 0) + 2 * radius * outward, -outward));
            return ManifoldVertex.Of(si.value().intr);
        }

        // A chain from a point through the glass ball (in and out) to the screen
        static ManifoldVertex[] Through(Point3f from, Point3f toward)
        {
            ManifoldVertex[] pattern = new ManifoldVertex[4];
            pattern[0] = ManifoldVertex.Free(from, toward - from);
            pattern[1] = new ManifoldVertex();
            pattern[1].reflection = false;
            pattern[1].eta = 1.5f;
            pattern[2] = new ManifoldVertex();
            pattern[2].reflection = false;
            pattern[2].eta = 1 / 1.5f;
            pattern[3] = new ManifoldVertex();
            return SpecularManifold.TraceChain(pattern, toward, Trace, null, null);
        }

        // A chain from a point off the ball, as a mirror, to the screen
        static ManifoldVertex[] Off(Point3f from, Point3f toward)
        {
            ManifoldVertex[] pattern = new ManifoldVertex[3];
            pattern[0] = ManifoldVertex.Free(from, toward - from);
            pattern[1] = new ManifoldVertex();
            pattern[1].reflection = true;
            pattern[2] = new ManifoldVertex();
            return SpecularManifold.TraceChain(pattern, toward, Trace, null, null);
        }

        static ManifoldVertex Moved(ManifoldVertex vertex, Float radius, Float du, Float dv)
        {
            if (radius == 0)
            {
                ManifoldVertex moved = new ManifoldVertex();
                moved.p = vertex.p + du * vertex.dpdu + dv * vertex.dpdv;
                moved.dpdu = vertex.dpdu;
                moved.dpdv = vertex.dpdv;
                moved.n = vertex.n;
                return moved;
            }
            ManifoldVertex onBall = OnBall(radius, vertex.p + du * vertex.dpdu + dv * vertex.dpdv);
            onBall.eta = vertex.eta;
            onBall.reflection = vertex.reflection;
            onBall.specular = vertex.specular;
            return onBall;
        }

        [TEST("Manifold", "JacobianAgainstFiniteDifferences")]
        public static void JacobianAgainstFiniteDifferences()
        {
            ManifoldVertex[] x = Through(new Point3f(-3, 0.2f, 0.1f), new Point3f(-0.9f, 0.3f, 0.25f));
            EXPECT_TRUE(x != null);
            ManifoldMatrix[] A = new ManifoldMatrix[2], B = new ManifoldMatrix[2], C = new ManifoldMatrix[2];
            SpecularManifold.Jacobian(x, A, B, C);
            Float[] radii = new Float[] { 0, 1, 1, 6 };
            Float h = 2e-3f;
            double worst = 0;
            for (int i = 1; i <= 2; ++i)
            {
                for (int which = -1; which <= 1; ++which)
                {
                    ManifoldMatrix block = which == -1 ? A[i - 1] : which == 0 ? B[i - 1] : C[i - 1];
                    for (int axis = 0; axis < 2; ++axis)
                    {
                        ManifoldVertex[] plus = (ManifoldVertex[])x.Clone(), minus = (ManifoldVertex[])x.Clone();
                        int moved = i + which;
                        plus[moved] = Moved(x[moved], radii[moved], axis == 0 ? h : 0, axis == 1 ? h : 0);
                        minus[moved] = Moved(x[moved], radii[moved], axis == 0 ? -h : 0, axis == 1 ? -h : 0);
                        Point2f cPlus = SpecularManifold.Constraint(plus[i - 1], plus[i], plus[i + 1]);
                        Point2f cMinus = SpecularManifold.Constraint(minus[i - 1], minus[i], minus[i + 1]);
                        double d0 = (cPlus.x - cMinus.x) / (2 * h), d1 = (cPlus.y - cMinus.y) / (2 * h);
                        double a0 = axis == 0 ? block.m00 : block.m01, a1 = axis == 0 ? block.m10 : block.m11;
                        double scale = Math.Max(1e-2, Math.Max(Math.Abs(a0), Math.Abs(a1)));
                        worst = Math.Max(worst, Math.Max(Math.Abs(d0 - a0), Math.Abs(d1 - a1)) / scale);
                    }
                }
            }
            Console.WriteLine("MEASURE manifold: Jacobian of the constraints of a chain through a glass ball against finite differences {0:E2}", worst);
            EXPECT_LT(worst, 3e-2);
        }

        static void CheckWalk(ManifoldVertex[] seed, ManifoldVertex[] goal, string name)
        {
            EXPECT_TRUE(seed != null && goal != null, name + ": the chains exist");
            int n = seed.Length - 2;
            ManifoldVertex[] found = SpecularManifold.Walk(seed, goal[n + 1].p, Trace);
            EXPECT_TRUE(found != null, name + ": the walk arrives");
            if (found == null)
            {
                return;
            }
            double end = Length(found[n + 1].p - goal[n + 1].p), first = Length(found[1].p - goal[1].p), constraint = 0;
            for (int i = 1; i <= n; ++i)
            {
                Point2f c = SpecularManifold.Constraint(found[i - 1], found[i], found[i + 1]);
                constraint = Math.Max(constraint, Math.Max(Math.Abs(c.x), Math.Abs(c.y)));
            }
            Console.WriteLine("MEASURE manifold walk, {0}: end off the target by {1:E2}, first vertex off the other chain's by {2:E2}, constraint {3:E2}",
                              name, end, first, constraint);
            EXPECT_LT(end, 2e-3);
            EXPECT_LT(first, 2e-3);
            EXPECT_LT(constraint, 1e-3);
        }

        [TEST("Manifold", "Walk")]
        public static void Walk()
        {
            Point3f from = new Point3f(-3, 0.2f, 0.1f);
            CheckWalk(Through(from, new Point3f(-0.9f, 0.3f, 0.25f)), Through(from, new Point3f(-0.9f, 0.1f, -0.2f)), "through a glass ball");
            CheckWalk(Through(from, new Point3f(-0.9f, 0.3f, 0.25f)), Through(from, new Point3f(-0.9f, -0.45f, 0.3f)), "through a glass ball, far");
            CheckWalk(Off(from, new Point3f(-0.8f, 0.5f, 0.2f)), Off(from, new Point3f(-0.8f, 0.3f, -0.4f)), "off a mirror ball");
        }

        [TEST("Manifold", "AreaRatio")]
        public static void AreaRatio()
        {
            Point3f from = new Point3f(-3, 0.2f, 0.1f);
            foreach (bool mirror in new bool[] { false, true })
            {
                ManifoldVertex[] x = mirror ? Off(from, new Point3f(-0.8f, 0.5f, 0.2f)) : Through(from, new Point3f(-0.9f, 0.3f, 0.25f));
                int n = x.Length - 2;
                Float ratio = SpecularManifold.AreaRatio(x);
                // Two small moves of the end, along its tangents and against them, and the moves of the first vertex that follow
                Float h = 0.01f;
                ManifoldVertex end = x[n + 1];
                ManifoldVertex[] plusU = SpecularManifold.Walk(x, OnBall(6, end.p + h * Normalize(end.dpdu)).p, Trace);
                ManifoldVertex[] minusU = SpecularManifold.Walk(x, OnBall(6, end.p - h * Normalize(end.dpdu)).p, Trace);
                ManifoldVertex[] plusV = SpecularManifold.Walk(x, OnBall(6, end.p + h * Normalize(end.dpdv)).p, Trace);
                ManifoldVertex[] minusV = SpecularManifold.Walk(x, OnBall(6, end.p - h * Normalize(end.dpdv)).p, Trace);
                EXPECT_TRUE(plusU != null && minusU != null && plusV != null && minusV != null);
                if (plusU == null || minusU == null || plusV == null || minusV == null)
                {
                    continue;
                }
                double endArea = Length(Cross(plusU[n + 1].p - minusU[n + 1].p, plusV[n + 1].p - minusV[n + 1].p));
                double firstArea = Length(Cross(plusU[1].p - minusU[1].p, plusV[1].p - minusV[1].p));
                double measured = firstArea / endArea;
                Console.WriteLine("MEASURE manifold area ratio, {0}: {1:E4} against the areas of two moves {2:E4}", mirror ? "mirror ball" : "glass ball",
                                  ratio, measured);
                EXPECT_LT(Math.Abs(ratio - measured) / measured, 0.01);
            }
        }
        // A chain for half vector space: from a point to the screen (taken as a rough reflector: its half vector is free), on
        // to the ball, off the ball as a mirror, to wherever that ray lands (the end)
        static ManifoldVertex[] RoughThenMirror(Point3f from, Vector3f toScreen, Point3f onBall)
        {
            ManifoldVertex[] x = new ManifoldVertex[4];
            x[0] = ManifoldVertex.Free(from, toScreen);
            x[1] = ManifoldVertex.Of(Trace(null, from, Normalize(toScreen)));
            x[1].specular = false;
            x[2] = ManifoldVertex.Of(Trace(null, x[1].p, Normalize(onBall - x[1].p)));
            Vector3f arriving = Normalize(x[2].p - x[1].p);
            Vector3f mirrored = arriving - 2 * Dot(arriving, (Vector3f)x[2].n) * (Vector3f)x[2].n;
            SurfaceInteraction end = Trace(null, x[2].p, mirrored);
            x[3] = ManifoldVertex.Free(end.p(), mirrored);
            return x;
        }

        static Vector3f[] HalfVectorsOf(ManifoldVertex[] x)
        {
            Vector3f[] h = new Vector3f[x.Length];
            for (int i = 1; i < x.Length - 1; ++i)
            {
                h[i] = SpecularManifold.HalfVector(x[i - 1], x[i], x[i + 1]);
            }
            return h;
        }

        [TEST("Manifold", "HalfVectorJacobian")]
        public static void HalfVectorJacobian()
        {
            ManifoldVertex[] x = RoughThenMirror(new Point3f(-3, 0.2f, 0.1f), new Vector3f(-1, 0.9f, 0.5f), new Point3f(-0.7f, 0.5f, 0.5f));
            Vector3f[] targets = HalfVectorsOf(x);
            double[] J = new double[16], plus = new double[4], minus = new double[4];
            SpecularManifold.HalfVectorJacobian(x, J);
            Float[] radii = new Float[] { 0, 6, 1, 0 };
            Float h = 2e-3f;
            double worst = 0;
            for (int moved = 1; moved <= 2; ++moved)
            {
                for (int axis = 0; axis < 2; ++axis)
                {
                    ManifoldVertex[] forward = (ManifoldVertex[])x.Clone(), backward = (ManifoldVertex[])x.Clone();
                    forward[moved] = Moved(x[moved], radii[moved], axis == 0 ? h : 0, axis == 1 ? h : 0);
                    backward[moved] = Moved(x[moved], radii[moved], axis == 0 ? -h : 0, axis == 1 ? -h : 0);
                    SpecularManifold.HalfVectorResidual(forward, targets, plus);
                    SpecularManifold.HalfVectorResidual(backward, targets, minus);
                    for (int row = 0; row < 4; ++row)
                    {
                        // (the residual is what the half vector is held to less the half vector: the Jacobian's negative)
                        double measured = -(plus[row] - minus[row]) / (2 * h), analytic = J[row * 4 + 2 * (moved - 1) + axis];
                        worst = Math.Max(worst, Math.Abs(measured - analytic) / Math.Max(1e-2, Math.Abs(analytic)));
                    }
                }
            }
            Console.WriteLine("MEASURE half vector space: Jacobian of a rough vertex and a mirror against finite differences {0:E2}", worst);
            EXPECT_LT(worst, 3e-2);
        }

        [TEST("Manifold", "HalfVectorMeasure")]
        public static void HalfVectorMeasure()
        {
            ManifoldVertex[] x = RoughThenMirror(new Point3f(-3, 0.2f, 0.1f), new Vector3f(-1, 0.9f, 0.5f), new Point3f(-0.7f, 0.5f, 0.5f));
            foreach (bool withMirror in new bool[] { false, true })
            {
                // Without the mirror: the chain point, rough vertex, point on the screen
                ManifoldVertex[] chain = withMirror ? x : new ManifoldVertex[] { x[0], x[1], x[2] };
                double measure = SpecularManifold.HalfVectorMeasure(chain);
                // Four moves of the rough vertex; with the mirror, the mirror vertex follows each (solved on its own chain)
                Float h = 0.003f;
                Point3f[] p = new Point3f[4];
                Vector3f[] half = new Vector3f[4];
                bool solved = true;
                for (int move = 0; move < 4; ++move)
                {
                    ManifoldVertex moved = Moved(x[1], 6, move == 0 ? h : move == 1 ? -h : 0, move == 2 ? h : move == 3 ? -h : 0);
                    ManifoldVertex after = x[2];
                    if (withMirror)
                    {
                        ManifoldVertex[] tail = SpecularManifold.SolveHalfVectors(new ManifoldVertex[] { moved, x[2], x[3] }, new Vector3f[3], Trace, null);
                        solved &= tail != null;
                        if (tail == null)
                        {
                            break;
                        }
                        after = tail[1];
                    }
                    p[move] = moved.p;
                    half[move] = SpecularManifold.HalfVector(x[0], moved, after);
                }
                EXPECT_TRUE(solved);
                if (!solved)
                {
                    continue;
                }
                double measured = Length(Cross(half[0] - half[1], half[2] - half[3])) / Length(Cross(p[0] - p[1], p[2] - p[3]));
                Console.WriteLine("MEASURE half vector space: solid angle of the half vector over the area of a rough vertex{0}: {1:E4} against four moves {2:E4}",
                                  withMirror ? ", a mirror after it" : "", measure, measured);
                EXPECT_LT(Math.Abs(measure - measured) / measured, 0.01);
            }
        }

        [TEST("Manifold", "SolveHalfVectors")]
        public static void SolveHalfVectors()
        {
            Point3f from = new Point3f(-3, 0.2f, 0.1f);
            ManifoldVertex[] start = RoughThenMirror(from, new Vector3f(-1, 0.9f, 0.5f), new Point3f(-0.7f, 0.5f, 0.5f));
            // Another chain with the same ends: the rough vertex elsewhere, the mirror solved for
            ManifoldVertex[] moved = (ManifoldVertex[])start.Clone();
            moved[1] = Moved(start[1], 6, 0.01f, -0.008f);
            ManifoldVertex[] tail = SpecularManifold.SolveHalfVectors(new ManifoldVertex[] { moved[1], start[2], start[3] }, new Vector3f[3], Trace, null);
            EXPECT_TRUE(tail != null);
            if (tail == null)
            {
                return;
            }
            moved[2] = tail[1];
            ManifoldVertex[] found = SpecularManifold.SolveHalfVectors(start, HalfVectorsOf(moved), Trace, null);
            EXPECT_TRUE(found != null);
            if (found == null)
            {
                return;
            }
            double rough = Length(found[1].p - moved[1].p), mirror = Length(found[2].p - moved[2].p);
            Console.WriteLine("MEASURE half vector space: solve for the half vectors of another chain: rough vertex off by {0:E2}, mirror vertex off by {1:E2}",
                              rough, mirror);
            EXPECT_LT(rough, 1e-3);
            EXPECT_LT(mirror, 5e-3);
        }
    }
}
