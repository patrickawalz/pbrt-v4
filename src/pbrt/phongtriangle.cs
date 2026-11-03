// (not in pbrt-v4) phongtriangle.cs
//
// Phong tessellation triangles ray traced directly, after
//   Ogaki, Tokuyoshi: "Direct Ray Tracing of Phong Tessellation", Computer Graphics Forum 30(4), 2011 (EGSR).
// The Phong tessellation of a triangle is a quadratic polynomial of the barycentric coordinates. A ray is the intersection of two planes, which turns
// the intersection into the intersection of two conics F(s, t) = 0 and G(s, t) = 0. Instead of the quartic of Kajiya's method, the pencil x F + G is
// used: a real root x of the cubic det(x F + G) = 0 makes the conic of the pencil degenerate (a pair of lines), and the intersections of F with the two
// lines are the roots of two quadratics.
// Differences from the paper: the pair of lines is extracted with the adjugate of the degenerate conic (Richter-Gebert) instead of the branch on
// M11 > M22, the hits are polished with two Newton steps on the two conics, and the bounding box of a triangle is the box of the six control points
// of the quadratic Bezier triangle (a tight box) instead of the offset box that the kd-tree of the paper uses.

using System;

namespace pbrt
{
    // The triangles of a mesh of Phong tessellation triangles (object space)
    public sealed class CurvedMesh
    {
        public BezierSurfaceSettings settings;
        public SdfVector[] positions;
        public SdfVector[] normals;
        // two values per vertex, or null
        public double[] uv;
        public int[] indices;
        public double alpha = 0.75;
    }

    public sealed class PhongTriangleShape : Shape
    {
        // indexOffset: the position of the first of the three vertex indices of the triangle in the index array of the mesh
        public PhongTriangleShape(CurvedMesh mesh, int indexOffset, int faceIndex)
        {
            this.mesh = mesh;
            this.indexOffset = indexOffset;
            this.faceIndex = faceIndex;
            SdfVector[] p = new SdfVector[3];
            SdfVector[] n = new SdfVector[3];
            for (int k = 0; k < 3; ++k)
            {
                int vertex = mesh.indices[indexOffset + k];
                p[k] = mesh.positions[vertex];
                n[k] = mesh.normals[vertex];
            }
            // The coefficients of the position (relative to the centroid) as a polynomial of (s, t): 1, s, t, s^2, t^2, s t
            origin = (p[0] + p[1] + p[2]) * (1.0 / 3.0);
            SdfVector e1 = p[1] - p[0];
            SdfVector e2 = p[2] - p[0];
            coefficient = new SdfVector[6];
            coefficient[0] = p[0] - origin;
            coefficient[1] = e1;
            coefficient[2] = e2;
            double[,] linear = { { 1, -1, -1 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int i = 0; i < 3; ++i)
            {
                double a = SdfVector.Dot(p[0] - p[i], n[i]);
                double c = SdfVector.Dot(e1, n[i]);
                double d = SdfVector.Dot(e2, n[i]);
                double b0 = linear[i, 0], bs = linear[i, 1], bt = linear[i, 2];
                double k0 = b0 * a;
                double ks = b0 * c + bs * a;
                double kt = b0 * d + bt * a;
                double kss = bs * c;
                double ktt = bt * d;
                double kst = bs * d + bt * c;
                coefficient[0] -= mesh.alpha * k0 * n[i];
                coefficient[1] -= mesh.alpha * ks * n[i];
                coefficient[2] -= mesh.alpha * kt * n[i];
                coefficient[3] -= mesh.alpha * kss * n[i];
                coefficient[4] -= mesh.alpha * ktt * n[i];
                coefficient[5] -= mesh.alpha * kst * n[i];
            }
            this.normals = n;

            // The control points of the quadratic Bezier triangle: the corners and 2 X(mid) - (X(a) + X(b)) / 2 for the edges
            SdfVector x00 = Position(0, 0), x10 = Position(1, 0), x01 = Position(0, 1);
            SdfVector[] control =
            {
                x00, x10, x01,
                2 * Position(0.5, 0) - (x00 + x10) * 0.5,
                2 * Position(0.5, 0.5) - (x10 + x01) * 0.5,
                2 * Position(0, 0.5) - (x01 + x00) * 0.5
            };
            SdfVector low = control[0], high = control[0];
            for (int i = 1; i < control.Length; ++i)
            {
                low = new SdfVector(Math.Min(low.x, control[i].x), Math.Min(low.y, control[i].y), Math.Min(low.z, control[i].z));
                high = new SdfVector(Math.Max(high.x, control[i].x), Math.Max(high.y, control[i].y), Math.Max(high.z, control[i].z));
            }
            low += origin;
            high += origin;
            Bounds3f result = new Bounds3f();
            for (int corner = 0; corner < 8; ++corner)
            {
                Point3f point = mesh.settings.renderFromObject.Apply(new Point3f((Float)((corner & 1) != 0 ? high.x : low.x),
                                                                                 (Float)((corner & 2) != 0 ? high.y : low.y),
                                                                                 (Float)((corner & 4) != 0 ? high.z : low.z)));
                result = corner == 0 ? new Bounds3f(point) : pbrt.Union(result, point);
            }
            Vector3f pad = new Vector3f(1e-4f, 1e-4f, 1e-4f) * pbrt.Length(result.pMax - result.pMin);
            bounds = new Bounds3f(result.pMin - pad, result.pMax + pad);
        }

        public override Bounds3f Bounds()
        {
            return bounds;
        }

        public override DirectionCone NormalBounds()
        {
            return DirectionCone.EntireSphere();
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            if (!Trace(ray, tMax, out double s, out double t, out double distance, out double length))
            {
                return default;
            }
            SurfaceInteraction intr = InteractionFromHit(ray, s, t);
            return new ShapeIntersection(intr, (Float)(distance / length));
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Trace(ray, tMax, out double s, out double t, out double distance, out double length);
        }

        public override Float Area()
        {
            int n = 8;
            double area = 0;
            for (int j = 0; j < n; ++j)
            {
                for (int i = 0; i < n - j; ++i)
                {
                    area += TriangleArea((double)i / n, (double)j / n, (double)(i + 1) / n, (double)j / n, (double)i / n, (double)(j + 1) / n);
                    if (i + j < n - 1)
                    {
                        area += TriangleArea((double)(i + 1) / n, (double)j / n, (double)(i + 1) / n, (double)(j + 1) / n, (double)i / n, (double)(j + 1) / n);
                    }
                }
            }
            return (Float)area;
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            pbrt.ErrorExit("Phong tessellation triangles: area lights are not supported yet.");
            return default;
        }

        public override Float PDF(Interaction intr)
        {
            pbrt.ErrorExit("Phong tessellation triangles: area lights are not supported yet.");
            return 0;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            pbrt.ErrorExit("Phong tessellation triangles: area lights are not supported yet.");
            return default;
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            pbrt.ErrorExit("Phong tessellation triangles: area lights are not supported yet.");
            return 0;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ PhongTriangleShape face: %d ]", faceIndex);
        }

        // The position relative to the centroid
        private SdfVector Position(double s, double t)
        {
            return coefficient[0] + s * coefficient[1] + t * coefficient[2] + s * s * coefficient[3] + t * t * coefficient[4] + s * t * coefficient[5];
        }

        private void Derivatives(double s, double t, out SdfVector dpds, out SdfVector dpdt)
        {
            dpds = coefficient[1] + 2 * s * coefficient[3] + t * coefficient[5];
            dpdt = coefficient[2] + 2 * t * coefficient[4] + s * coefficient[5];
        }

        private double TriangleArea(double s0, double t0, double s1, double t1, double s2, double t2)
        {
            Transform renderFromObject = mesh.settings.renderFromObject;
            SdfVector a = ToRender(renderFromObject, Position(s0, t0) + origin);
            SdfVector b = ToRender(renderFromObject, Position(s1, t1) + origin);
            SdfVector c = ToRender(renderFromObject, Position(s2, t2) + origin);
            return 0.5 * SdfVector.Cross(b - a, c - a).Length();
        }

        private static SdfVector ToRender(Transform renderFromObject, SdfVector p)
        {
            Point3f q = renderFromObject.Apply(new Point3f((Float)p.x, (Float)p.y, (Float)p.z));
            return new SdfVector(q.x, q.y, q.z);
        }

        // The ray in object space, then the intersection of the two conics
        private bool Trace(Ray ray, Float tMax, out double hitS, out double hitT, out double distance, out double length)
        {
            hitS = 0;
            hitT = 0;
            distance = 0;
            Point3f o = mesh.settings.objectFromRender.Apply(ray.o);
            Vector3f dv = mesh.settings.objectFromRender.Apply(ray.d);
            length = Math.Sqrt((double)dv.x * dv.x + (double)dv.y * dv.y + (double)dv.z * dv.z);
            if (length == 0 || double.IsNaN(length))
            {
                return false;
            }
            SdfVector d = new SdfVector(dv.x / length, dv.y / length, dv.z / length);
            SdfVector rayOrigin = new SdfVector(o.x, o.y, o.z) - origin;
            double tMaxObject = float.IsPositiveInfinity(tMax) ? 1e30 : tMax * length;

            // The two planes through the ray
            SdfVector axis = Math.Abs(d.x) < 0.6 ? new SdfVector(1, 0, 0) : new SdfVector(0, 1, 0);
            SdfVector d1 = SdfVector.Cross(d, axis).Normalized();
            SdfVector d2 = SdfVector.Cross(d, d1);
            double[] f = new double[6];
            double[] g = new double[6];
            f[0] = SdfVector.Dot(d1, coefficient[0] - rayOrigin);
            g[0] = SdfVector.Dot(d2, coefficient[0] - rayOrigin);
            for (int k = 1; k < 6; ++k)
            {
                f[k] = SdfVector.Dot(d1, coefficient[k]);
                g[k] = SdfVector.Dot(d2, coefficient[k]);
            }
            Normalize(f);
            Normalize(g);

            double[,] mf = ConicMatrix(f);
            double[,] mg = ConicMatrix(g);
            double[] cubic = PencilDeterminant(mf, mg);
            double[] roots = new double[3];
            int rootCount = SolveCubic(cubic[3], cubic[2], cubic[1], cubic[0], roots);

            double bestDistance = tMaxObject;
            bool found = false;
            double[] candidates = new double[8];
            for (int r = 0; r < rootCount; ++r)
            {
                double x = roots[r];
                double[,] m = new double[3, 3];
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = 0; j < 3; ++j)
                    {
                        m[i, j] = x * mf[i, j] + mg[i, j];
                    }
                }
                if (!SplitConic(m, out double[] line1, out double[] line2))
                {
                    continue;
                }
                int count = 0;
                count += IntersectConicLine(f, line1, candidates, count);
                count += IntersectConicLine(f, line2, candidates, count);
                bool any = false;
                for (int c = 0; c < count; c += 2)
                {
                    double s = candidates[c];
                    double t = candidates[c + 1];
                    Polish(f, g, ref s, ref t);
                    const double tolerance = 1e-7;
                    if (s < -tolerance || t < -tolerance || s + t > 1 + tolerance)
                    {
                        continue;
                    }
                    s = Math.Min(Math.Max(s, 0), 1);
                    t = Math.Min(Math.Max(t, 0), 1);
                    double along = SdfVector.Dot(Position(s, t) - rayOrigin, d);
                    if (along > 0 && along < bestDistance)
                    {
                        bestDistance = along;
                        hitS = s;
                        hitT = t;
                        found = true;
                    }
                    any = true;
                }
                // One real root of the cubic is sufficient: the lines of any real degenerate conic of the pencil contain all of the intersections
                if (any || found)
                {
                    break;
                }
            }
            distance = bestDistance;
            return found;
        }

        private static void Normalize(double[] c)
        {
            double scale = 0;
            for (int k = 0; k < 6; ++k)
            {
                scale = Math.Max(scale, Math.Abs(c[k]));
            }
            if (scale > 0)
            {
                for (int k = 0; k < 6; ++k)
                {
                    c[k] /= scale;
                }
            }
        }

        // The symmetric matrix of the conic c[0] + c[1] s + c[2] t + c[3] s^2 + c[4] t^2 + c[5] s t on the vector (s, t, 1)
        private static double[,] ConicMatrix(double[] c)
        {
            return new double[3, 3]
            {
                { c[3], 0.5 * c[5], 0.5 * c[1] },
                { 0.5 * c[5], c[4], 0.5 * c[2] },
                { 0.5 * c[1], 0.5 * c[2], c[0] }
            };
        }

        private static double Determinant(double[,] m)
        {
            return m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
                 - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
                 + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        }

        // The coefficients c0 ... c3 of det(x F + G)
        private static double[] PencilDeterminant(double[,] mf, double[,] mg)
        {
            double d0 = Determinant(Combine(mf, mg, 0));
            double d1 = Determinant(Combine(mf, mg, 1));
            double dm1 = Determinant(Combine(mf, mg, -1));
            double d2 = Determinant(Combine(mf, mg, 2));
            double c0 = d0;
            double c2 = 0.5 * (d1 + dm1) - c0;
            double a = 0.5 * (d1 - dm1);
            double c3 = (d2 - c0 - 4 * c2 - 2 * a) / 6;
            double c1 = a - c3;
            return new double[] { c0, c1, c2, c3 };
        }

        private static double[,] Combine(double[,] mf, double[,] mg, double x)
        {
            double[,] m = new double[3, 3];
            for (int i = 0; i < 3; ++i)
            {
                for (int j = 0; j < 3; ++j)
                {
                    m[i, j] = x * mf[i, j] + mg[i, j];
                }
            }
            return m;
        }

        // The real roots of c3 x^3 + c2 x^2 + c1 x + c0, the dominant ones first
        private static int SolveCubic(double c3, double c2, double c1, double c0, double[] roots)
        {
            double scale = Math.Max(Math.Max(Math.Abs(c3), Math.Abs(c2)), Math.Max(Math.Abs(c1), Math.Abs(c0)));
            if (scale == 0)
            {
                return 0;
            }
            if (Math.Abs(c3) <= 1e-12 * scale)
            {
                if (Math.Abs(c2) <= 1e-12 * scale)
                {
                    if (Math.Abs(c1) <= 1e-12 * scale)
                    {
                        return 0;
                    }
                    roots[0] = -c0 / c1;
                    return 1;
                }
                double discriminant = c1 * c1 - 4 * c2 * c0;
                if (discriminant < 0)
                {
                    return 0;
                }
                double root = Math.Sqrt(discriminant);
                double q = -0.5 * (c1 + (c1 >= 0 ? root : -root));
                roots[0] = q / c2;
                roots[1] = q != 0 ? c0 / q : roots[0];
                return 2;
            }
            double a = c2 / c3, b = c1 / c3, c = c0 / c3;
            double p = b - a * a / 3;
            double qq = 2 * a * a * a / 27 - a * b / 3 + c;
            double shift = -a / 3;
            double h = qq * qq / 4 + p * p * p / 27;
            if (h > 0)
            {
                double sq = Math.Sqrt(h);
                roots[0] = Math.Cbrt(-qq / 2 + sq) + Math.Cbrt(-qq / 2 - sq) + shift;
                return 1;
            }
            if (p == 0)
            {
                roots[0] = shift;
                return 1;
            }
            double m = 2 * Math.Sqrt(-p / 3);
            double argument = Math.Min(Math.Max(3 * qq / (p * m), -1), 1);
            double angle = Math.Acos(argument) / 3;
            roots[0] = m * Math.Cos(angle) + shift;
            roots[1] = m * Math.Cos(angle - 2 * Math.PI / 3) + shift;
            roots[2] = m * Math.Cos(angle - 4 * Math.PI / 3) + shift;
            return 3;
        }

        // The two lines (a, b, c) with a s + b t + c = 0 of a degenerate conic, false when they are complex
        private static bool SplitConic(double[,] c, out double[] line1, out double[] line2)
        {
            line1 = null;
            line2 = null;
            double[,] adjugate = new double[3, 3];
            for (int i = 0; i < 3; ++i)
            {
                for (int j = 0; j < 3; ++j)
                {
                    int i1 = (i + 1) % 3, i2 = (i + 2) % 3, j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                    adjugate[j, i] = c[i1, j1] * c[i2, j2] - c[i1, j2] * c[i2, j1];
                }
            }
            int best = 0;
            for (int i = 1; i < 3; ++i)
            {
                if (adjugate[i, i] < adjugate[best, best])
                {
                    best = i;
                }
            }
            double diagonal = adjugate[best, best];
            if (diagonal >= 0)
            {
                // complex lines: the pencil member is a point, only possible for a tangent configuration
                return false;
            }
            double norm = Math.Sqrt(-diagonal);
            double[] point = { adjugate[0, best] / norm, adjugate[1, best] / norm, adjugate[2, best] / norm };
            double[,] skew =
            {
                { c[0, 0], c[0, 1] + point[2], c[0, 2] - point[1] },
                { c[1, 0] - point[2], c[1, 1], c[1, 2] + point[0] },
                { c[2, 0] + point[1], c[2, 1] - point[0], c[2, 2] }
            };
            // The rows are multiples of one line, the columns of the other
            int row = 0;
            int column = 0;
            double rowNorm = -1;
            double columnNorm = -1;
            for (int i = 0; i < 3; ++i)
            {
                double rn = skew[i, 0] * skew[i, 0] + skew[i, 1] * skew[i, 1] + skew[i, 2] * skew[i, 2];
                double cn = skew[0, i] * skew[0, i] + skew[1, i] * skew[1, i] + skew[2, i] * skew[2, i];
                if (rn > rowNorm)
                {
                    rowNorm = rn;
                    row = i;
                }
                if (cn > columnNorm)
                {
                    columnNorm = cn;
                    column = i;
                }
            }
            line1 = new double[] { skew[row, 0], skew[row, 1], skew[row, 2] };
            line2 = new double[] { skew[0, column], skew[1, column], skew[2, column] };
            return true;
        }

        // The intersections of the conic f with the line; appends (s, t) pairs, returns the number of values written
        private static int IntersectConicLine(double[] f, double[] line, double[] output, int offset)
        {
            double a = line[0], b = line[1], c = line[2];
            double[] quadratic = new double[3];
            bool solveForT = Math.Abs(a) >= Math.Abs(b);
            if (Math.Abs(a) == 0 && Math.Abs(b) == 0)
            {
                return 0;
            }
            if (solveForT)
            {
                // s = (-b t - c) / a = p0 + p1 t
                double p0 = -c / a, p1 = -b / a;
                // f0 + f1 s + f2 t + f3 s^2 + f4 t^2 + f5 s t
                quadratic[0] = f[0] + f[1] * p0 + f[3] * p0 * p0;
                quadratic[1] = f[1] * p1 + f[2] + 2 * f[3] * p0 * p1 + f[5] * p0;
                quadratic[2] = f[3] * p1 * p1 + f[4] + f[5] * p1;
                int count = SolveQuadratic(quadratic, out double r0, out double r1);
                int written = 0;
                if (count >= 1)
                {
                    output[offset] = p0 + p1 * r0;
                    output[offset + 1] = r0;
                    written += 2;
                }
                if (count == 2)
                {
                    output[offset + 2] = p0 + p1 * r1;
                    output[offset + 3] = r1;
                    written += 2;
                }
                return written;
            }
            else
            {
                // t = (-a s - c) / b = p0 + p1 s
                double p0 = -c / b, p1 = -a / b;
                quadratic[0] = f[0] + f[2] * p0 + f[4] * p0 * p0;
                quadratic[1] = f[1] + f[2] * p1 + 2 * f[4] * p0 * p1 + f[5] * p0;
                quadratic[2] = f[3] + f[4] * p1 * p1 + f[5] * p1;
                int count = SolveQuadratic(quadratic, out double r0, out double r1);
                int written = 0;
                if (count >= 1)
                {
                    output[offset] = r0;
                    output[offset + 1] = p0 + p1 * r0;
                    written += 2;
                }
                if (count == 2)
                {
                    output[offset + 2] = r1;
                    output[offset + 3] = p0 + p1 * r1;
                    written += 2;
                }
                return written;
            }
        }

        // The real roots of q[2] x^2 + q[1] x + q[0]
        private static int SolveQuadratic(double[] q, out double r0, out double r1)
        {
            r0 = 0;
            r1 = 0;
            double scale = Math.Max(Math.Abs(q[0]), Math.Max(Math.Abs(q[1]), Math.Abs(q[2])));
            if (scale == 0)
            {
                return 0;
            }
            if (Math.Abs(q[2]) <= 1e-14 * scale)
            {
                if (Math.Abs(q[1]) <= 1e-14 * scale)
                {
                    return 0;
                }
                r0 = -q[0] / q[1];
                return 1;
            }
            double discriminant = q[1] * q[1] - 4 * q[2] * q[0];
            if (discriminant < 0)
            {
                // a grazing hit can have a slightly negative discriminant from rounding
                if (discriminant > -1e-12 * scale * scale)
                {
                    r0 = -0.5 * q[1] / q[2];
                    return 1;
                }
                return 0;
            }
            double root = Math.Sqrt(discriminant);
            double k = -0.5 * (q[1] + (q[1] >= 0 ? root : -root));
            r0 = k / q[2];
            r1 = k != 0 ? q[0] / k : r0;
            return 2;
        }

        // Two Newton steps on the two conics
        private static void Polish(double[] f, double[] g, ref double s, ref double t)
        {
            for (int iteration = 0; iteration < 2; ++iteration)
            {
                double fv = f[0] + f[1] * s + f[2] * t + f[3] * s * s + f[4] * t * t + f[5] * s * t;
                double gv = g[0] + g[1] * s + g[2] * t + g[3] * s * s + g[4] * t * t + g[5] * s * t;
                double fs = f[1] + 2 * f[3] * s + f[5] * t, ft = f[2] + 2 * f[4] * t + f[5] * s;
                double gs = g[1] + 2 * g[3] * s + g[5] * t, gt = g[2] + 2 * g[4] * t + g[5] * s;
                double determinant = fs * gt - ft * gs;
                if (Math.Abs(determinant) < 1e-12)
                {
                    return;
                }
                double ds = (fv * gt - ft * gv) / determinant;
                double dt = (fs * gv - fv * gs) / determinant;
                if (Math.Abs(ds) > 0.1 || Math.Abs(dt) > 0.1)
                {
                    return;
                }
                s -= ds;
                t -= dt;
            }
        }

        private SurfaceInteraction InteractionFromHit(Ray ray, double s, double t)
        {
            SdfVector p = Position(s, t) + origin;
            Derivatives(s, t, out SdfVector dpds, out SdfVector dpdt);
            double[] vertexUv = new double[6] { 0, 0, 1, 0, 1, 1 };
            if (mesh.uv != null)
            {
                for (int k = 0; k < 3; ++k)
                {
                    int vertex = mesh.indices[indexOffset + k];
                    vertexUv[2 * k] = mesh.uv[2 * vertex];
                    vertexUv[2 * k + 1] = mesh.uv[2 * vertex + 1];
                }
            }
            double[] cornerUv =
            {
                vertexUv[0], vertexUv[1],
                vertexUv[2], vertexUv[3],
                vertexUv[4], vertexUv[5],
                vertexUv[2] + vertexUv[4] - vertexUv[0], vertexUv[3] + vertexUv[5] - vertexUv[1]
            };
            new PatchTextureMap(cornerUv).Convert(s, t, dpds, dpdt, out Point2f uv, out SdfVector dpdu, out SdfVector dpdv);

            // The shading normal of the paper: the interpolated normal, or the normal of the surface where the reflected ray would go below it
            SdfVector interpolated = ((1 - s - t) * normals[0] + s * normals[1] + t * normals[2]).Normalized();
            SdfVector surfaceNormal = SdfVector.Cross(dpds, dpdt);
            SdfVector shading = interpolated;
            if (surfaceNormal.LengthSquared() > 0)
            {
                surfaceNormal = surfaceNormal.Normalized();
                if (SdfVector.Dot(surfaceNormal, interpolated) < 0)
                {
                    surfaceNormal = -surfaceNormal;
                }
                Vector3f dr = mesh.settings.objectFromRender.Apply(ray.d);
                SdfVector direction = new SdfVector(dr.x, dr.y, dr.z).Normalized();
                SdfVector reflected = direction - 2 * SdfVector.Dot(direction, interpolated) * interpolated;
                if (SdfVector.Dot(surfaceNormal, reflected) < 0)
                {
                    shading = surfaceNormal;
                }
            }

            Transform renderFromObject = mesh.settings.renderFromObject;
            Point3f pr = renderFromObject.Apply(new Point3f((Float)p.x, (Float)p.y, (Float)p.z));
            Vector3f pu = renderFromObject.Apply(new Vector3f((Float)dpdu.x, (Float)dpdu.y, (Float)dpdu.z));
            Vector3f pv = renderFromObject.Apply(new Vector3f((Float)dpdv.x, (Float)dpdv.y, (Float)dpdv.z));
            bool flipNormal = mesh.settings.reverseOrientation ^ mesh.settings.transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            SurfaceInteraction si = new SurfaceInteraction(new Point3fi(pr, pError), uv, -ray.d, pu, pv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0),
                                                           ray.time, flipNormal, faceIndex);
            Normal3f ns = pbrt.Normalize(renderFromObject.Apply(new Normal3f((Float)shading.x, (Float)shading.y, (Float)shading.z)));
            if (mesh.settings.reverseOrientation)
            {
                ns = -ns;
            }
            si.SetShadingGeometry(ns, pu, pv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), true);
            return si;
        }

        private readonly CurvedMesh mesh;
        private readonly int indexOffset;
        private readonly int faceIndex;
        private readonly SdfVector[] coefficient;
        private readonly SdfVector[] normals;
        private readonly SdfVector origin;
        private readonly Bounds3f bounds;
    }
}
