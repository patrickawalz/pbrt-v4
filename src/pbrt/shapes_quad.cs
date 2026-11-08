// (not in pbrt-v4) shapes_quad.cs
//
// Shape "quadmesh": a mesh of planar convex quadrilaterals that are intersected, sampled and shaded as quadrilaterals. Meant for pure quad meshes
// (the output of a Catmull-Clark subdivision, a modeller's quad cage) and for quad lights that are sampled by solid angle and by equiangular
// sampling.
//
//  * Intersection. A quad is the union of two triangles that share a diagonal. The ray is moved to the ray coordinate system once (as the
//    watertight triangle test of Woop et al. does), the five edge functions (four sides and the diagonal) are evaluated in double precision, which
//    makes their signs exact for the single precision vertices, and the diagonal is evaluated once and used with opposite signs for the two
//    triangles. The ray cannot pass between the two halves, and it cannot pass between two quads (or a quad and a triangle) that share an edge
//    and its two vertices. The hit point is computed from the barycentric coordinates of the triangle that was hit, with the error bounds of a
//    triangle, so that the rays spawned from it are offset correctly. The bilinear (u,v) of the hit is solved in closed form from the barycentric
//    coordinates (Lagae and Dutre 2005; LuxRender's MeshQuadrilateral), with the numerically stable form of the quadratic.
//  * Interaction. The (u,v), dpdu, dpdv, dndu, dndv, the shading normal and the texture coordinates are those of BilinearPatch, for the same
//    four corners.
//  * Sampling. By area: one of the two triangles, chosen by area, then a uniform point of it (density 1 / Area). By solid angle, from a point:
//    for a rectangle the spherical rectangle of Urena et al. (as BilinearPatch does), otherwise one of the two spherical triangles of Arvo,
//    chosen by solid angle, so the density is 1 / (solid angle of the quad). Both are warped for the cosine at the reference point.
//    With "string sampling" "projected" the quad is instead sampled proportional to its projected solid angle (the density is |cos| / A, with the cosine
//    of the angle to the shading normal and A the integral of |cos| over the quad: exact, no warp), see polygon_sampling.cs (Peters 2021). It is used
//    for the reference points with a shading normal; the other points are sampled as above.
//  * Quads that are not planar and convex (within a tolerance relative to their size), or that are degenerate, are not made into Quad shapes:
//    each is two ordinary Triangle shapes, split along the shorter diagonal, as LuxRender splits every quad.
//
// The corners of a quad are given in order around it: p0, p1, p2, p3, counterclockwise when seen from the side the normal points to.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class Quad : Shape
    {
        // Corner i (in order around the quad) of a quad is stored at index CornerSlot[i] of the four vertex indices of a BilinearPatchMesh, whose order is
        // p00, p10, p01, p11
        private static readonly int[] CornerSlot = { 0, 1, 3, 2 };

        private const Float MinSphericalSampleArea = 3e-4f;
        private const Float MaxSphericalSampleArea = 6.22f;

        // Tolerances of the test that makes a quad a Quad: relative to the length of the longer diagonal
        private const double PlanarTolerance = 1e-5;
        private const double ConvexTolerance = 1e-6;
        private const double DegenerateTolerance = 1e-6;

        private static List<BilinearPatchMesh> allMeshes;
        private static readonly object allMeshesLock = new object();

        // The corners in the order of the frame this quad is intersected in: frame k starts at corner k
        private int meshIndex;
        private int quadIndex;
        private int rotation;
        private double a11;
        private double b11;
        private Float areaA;
        private Float areaB;
        private bool rectangle;
        private bool projectedSampling;

        // The sampling by projected solid angle falls back to the sampling by area when the projected solid angle of the quad is smaller than this
        private const double MinProjectedSampleArea = 1e-8;

        [ThreadStatic]
        private static ProjectedSolidAnglePolygon upperPolygon;
        [ThreadStatic]
        private static ProjectedSolidAnglePolygon lowerPolygon;

        // An orthonormal basis (double precision) whose z-axis is the shading normal
        private struct Basis
        {
            public double xx, xy, xz;
            public double yx, yy, yz;
            public double zx, zy, zz;

            public Basis(Normal3f normal)
            {
                double length = Math.Sqrt((double)normal.x * normal.x + (double)normal.y * normal.y + (double)normal.z * normal.z);
                zx = normal.x / length;
                zy = normal.y / length;
                zz = normal.z / length;
                double hx = Math.Abs(zx) < 0.9 ? 1.0 : 0.0;
                double hy = Math.Abs(zx) < 0.9 ? 0.0 : 1.0;
                xx = hy * zz;
                xy = -hx * zz;
                xz = hx * zy - hy * zx;
                double xLength = Math.Sqrt(xx * xx + xy * xy + xz * xz);
                xx /= xLength;
                xy /= xLength;
                xz /= xLength;
                yx = zy * xz - zz * xy;
                yy = zz * xx - zx * xz;
                yz = zx * xy - zy * xx;
            }
        }

        // The hit of one of the two triangles: second tells which one; the weights are those of the corners of the triangle in the order
        // (q0, q1, q3) for the first and (q2, q3, q1) for the second
        private struct QuadHit
        {
            public bool second;
            public double w0;
            public double w1;
            public double w2;
            public Float t;
        }

        // The ray coordinate system of the watertight triangle test
        private struct RayFrame
        {
            public int kx;
            public int ky;
            public int kz;
            public Float sx;
            public Float sy;
            public Float sz;
            public Point3f origin;

            public RayFrame(Ray ray)
            {
                origin = ray.o;
                kz = pbrt.MaxComponentIndex(pbrt.Abs(ray.d));
                kx = kz + 1;
                if (kx == 3)
                {
                    kx = 0;
                }
                ky = kx + 1;
                if (ky == 3)
                {
                    ky = 0;
                }
                Vector3f d = pbrt.Permute(ray.d, kx, ky, kz);
                sx = -d.x / d.z;
                sy = -d.y / d.z;
                sz = 1 / d.z;
            }

            public void Project(Point3f p, out Float x, out Float y, out Float z)
            {
                Point3f pt = pbrt.Permute(p - (Vector3f)origin, kx, ky, kz);
                x = pt.x + sx * pt.z;
                y = pt.y + sy * pt.z;
                z = pt.z * sz;
            }
        }

        public Quad(BilinearPatchMesh mesh, int meshIndex, int quadIndex, int rotation, bool projectedSampling)
        {
            this.projectedSampling = projectedSampling;
            this.meshIndex = meshIndex;
            this.quadIndex = quadIndex;
            this.rotation = rotation;

            Point3f q0 = CornerOf(mesh, quadIndex, rotation);
            Point3f q1 = CornerOf(mesh, quadIndex, rotation + 1);
            Point3f q2 = CornerOf(mesh, quadIndex, rotation + 2);
            Point3f q3 = CornerOf(mesh, quadIndex, rotation + 3);
            ComputeV11Coordinates(q0, q1, q2, q3, out a11, out b11);
            areaA = 0.5f * pbrt.Length(pbrt.Cross(q1 - q0, q3 - q0));
            areaB = 0.5f * pbrt.Length(pbrt.Cross(q3 - q2, q1 - q2));
            rectangle = IsRectangle(q0, q1, q2, q3);
        }

        public static void Init(Allocator alloc)
        {
            allMeshes = new List<BilinearPatchMesh>();
        }

        private BilinearPatchMesh GetMesh()
        {
            return allMeshes[meshIndex];
        }

        private static Point3f CornerOf(BilinearPatchMesh mesh, int quadIndex, int corner)
        {
            return mesh.p[mesh.vertexIndices[4 * quadIndex + CornerSlot[corner & 3]]];
        }

        private Point3f Corner(BilinearPatchMesh mesh, int corner)
        {
            return CornerOf(mesh, quadIndex, corner + rotation);
        }

        // The coordinates (a11, b11) of q2 in the frame (q0; q1 - q0, q3 - q0): q2 - q0 = a11 (q1 - q0) + b11 (q3 - q0), for a planar quad
        public static void ComputeV11Coordinates(Point3f q0, Point3f q1, Point3f q2, Point3f q3, out double a11, out double b11)
        {
            double e1x = (double)q1.x - q0.x, e1y = (double)q1.y - q0.y, e1z = (double)q1.z - q0.z;
            double e3x = (double)q3.x - q0.x, e3y = (double)q3.y - q0.y, e3z = (double)q3.z - q0.z;
            double e2x = (double)q2.x - q0.x, e2y = (double)q2.y - q0.y, e2z = (double)q2.z - q0.z;
            double nx = e1y * e3z - e1z * e3y, ny = e1z * e3x - e1x * e3z, nz = e1x * e3y - e1y * e3x;
            double nn = nx * nx + ny * ny + nz * nz;
            if (nn == 0)
            {
                a11 = 1;
                b11 = 1;
                return;
            }
            double cx = e2y * e3z - e2z * e3y, cy = e2z * e3x - e2x * e3z, cz = e2x * e3y - e2y * e3x;
            a11 = (cx * nx + cy * ny + cz * nz) / nn;
            double dx = e1y * e2z - e1z * e2y, dy = e1z * e2x - e1x * e2z, dz = e1x * e2y - e1y * e2x;
            b11 = (dx * nx + dy * ny + dz * nz) / nn;
        }

        // The (u,v) in [0,1]^2 of the bilinear patch (q0 at (0,0), q1 at (1,0), q2 at (1,1), q3 at (0,1)) of the point whose coordinates in the frame
        // (q0; q1 - q0, q3 - q0) are (alpha, beta), for a planar convex quad whose q2 is (a11, b11) in that frame. The point is
        // alpha = u (1 + v (a11 - 1)), beta = v (1 + u (b11 - 1)); eliminating v gives (b11 - 1) u^2 + (1 + (a11 - 1) beta - (b11 - 1) alpha) u - alpha = 0,
        // solved with the stable form of the quadratic formula: neither the case b11 = 1 (a parallelogram or a trapezoid) nor a nearly flat quadratic
        // needs a special case.
        public static void SolveBilinear(double a11, double b11, double alpha, double beta, out double u, out double v)
        {
            double a = a11 - 1;
            double b = b11 - 1;
            double linear = 1 + a * beta - b * alpha;
            double discriminant = linear * linear + 4 * b * alpha;
            if (discriminant < 0)
            {
                discriminant = 0;
            }
            double root = Math.Sqrt(discriminant);
            double q = -0.5 * (linear + (linear < 0 ? -root : root));

            // The roots are q / b and -alpha / q; the one in [0,1] (up to rounding) is the point of a convex quad
            double best = 0;
            double bestOutside = double.MaxValue;
            for (int i = 0; i < 2; ++i)
            {
                double candidate;
                if (i == 0)
                {
                    if (b == 0)
                    {
                        continue;
                    }
                    candidate = q / b;
                }
                else
                {
                    if (q == 0)
                    {
                        continue;
                    }
                    candidate = -alpha / q;
                }
                double outside = candidate < 0 ? -candidate : (candidate > 1 ? candidate - 1 : 0);
                if (outside < bestOutside)
                {
                    bestOutside = outside;
                    best = candidate;
                }
            }
            u = Math.Min(1.0, Math.Max(0.0, best));
            double denominator = 1 + u * b;
            if (Math.Abs(denominator) > 1e-300)
            {
                v = beta / denominator;
            }
            else
            {
                v = 0;
            }
            v = Math.Min(1.0, Math.Max(0.0, v));
        }

        // The (u,v) of the quad as it was given (corner 0 at (0,0)) from the (u,v) of the frame that starts at corner k
        private static Point2f ToGivenFrame(int rotation, double u, double v)
        {
            switch (rotation & 3)
            {
                case 0:
                    return new Point2f((Float)u, (Float)v);
                case 1:
                    return new Point2f((Float)(1 - v), (Float)u);
                case 2:
                    return new Point2f((Float)(1 - u), (Float)(1 - v));
                default:
                    return new Point2f((Float)v, (Float)(1 - u));
            }
        }

        // The (u,v), in the frame of the quad as it was given, of the point with the weights w of the corners of one of the two triangles
        private Point2f UvOfWeights(bool second, double w0, double w1, double w2)
        {
            double alpha;
            double beta;
            if (!second)
            {
                alpha = w1;
                beta = w2;
            }
            else
            {
                alpha = w0 * a11 + w2;
                beta = w0 * b11 + w1;
            }
            double u;
            double v;
            SolveBilinear(a11, b11, alpha, beta, out u, out v);
            return ToGivenFrame(rotation, u, v);
        }

        // Whether the corners (in order around the quad) are those of a rectangle, as BilinearPatch tests it
        private static bool IsRectangle(Point3f q0, Point3f q1, Point3f q2, Point3f q3)
        {
            if (q0 == q1 || q1 == q2 || q2 == q3 || q3 == q0)
            {
                return false;
            }
            Normal3f n = (Normal3f)pbrt.Normalize(pbrt.Cross(q1 - q0, q3 - q0));
            if (pbrt.AbsDot((Vector3f)pbrt.Normalize(q2 - q0), n) > 1e-5f)
            {
                return false;
            }
            Point3f center = (q0 + (Vector3f)q1 + (Vector3f)q2 + (Vector3f)q3) / 4;
            Float d0 = pbrt.DistanceSquared(q0, center);
            Float d1 = pbrt.DistanceSquared(q1, center);
            Float d2 = pbrt.DistanceSquared(q2, center);
            Float d3 = pbrt.DistanceSquared(q3, center);
            return std.abs(d1 - d0) / d0 <= 1e-4f && std.abs(d2 - d0) / d0 <= 1e-4f && std.abs(d3 - d0) / d0 <= 1e-4f;
        }

        // Whether the four points (in order around the quad) are a planar, strictly convex, non-degenerate quadrilateral, and if so, the frame (the
        // corner it starts at, 0 to 3) that conditions the (u,v) solve best: the one whose q2 has the largest smaller coordinate (1 and 1 for a
        // parallelogram, whose frames are all as good; the given frame is kept then).
        public static bool Classify(Point3f p0, Point3f p1, Point3f p2, Point3f p3, out int rotation)
        {
            rotation = 0;
            double[][] c = { ToDouble(p0), ToDouble(p1), ToDouble(p2), ToDouble(p3) };

            // Degenerate: a zero edge, or a diagonal too short to measure against
            double diagonal = Math.Max(Length(Sub(c[2], c[0])), Length(Sub(c[3], c[1])));
            if (diagonal == 0)
            {
                return false;
            }
            for (int i = 0; i < 4; ++i)
            {
                if (Length(Sub(c[(i + 1) & 3], c[i])) <= DegenerateTolerance * diagonal)
                {
                    return false;
                }
            }

            // The normal of the quad (Newell's: the cross product of the diagonals)
            double[] normal = Cross(Sub(c[2], c[0]), Sub(c[3], c[1]));
            double normalLength = Length(normal);
            if (normalLength == 0)
            {
                return false;
            }
            normal = new double[] { normal[0] / normalLength, normal[1] / normalLength, normal[2] / normalLength };

            // Planar: every corner within a tolerance of the plane through the center
            double[] center = new double[3];
            for (int k = 0; k < 3; ++k)
            {
                center[k] = (c[0][k] + c[1][k] + c[2][k] + c[3][k]) / 4;
            }
            for (int i = 0; i < 4; ++i)
            {
                if (Math.Abs(Dot(normal, Sub(c[i], center))) > PlanarTolerance * diagonal)
                {
                    return false;
                }
            }

            // Strictly convex: the turn at every corner is to the same side as the normal
            for (int i = 0; i < 4; ++i)
            {
                double[] incoming = Sub(c[i], c[(i + 3) & 3]);
                double[] outgoing = Sub(c[(i + 1) & 3], c[i]);
                double turn = Dot(normal, Cross(incoming, outgoing));
                if (turn <= ConvexTolerance * Length(incoming) * Length(outgoing))
                {
                    return false;
                }
            }

            // The frame: the one whose (a11, b11) have the largest smaller coordinate
            Point3f[] corners = { p0, p1, p2, p3 };
            double bestScore = -1;
            for (int k = 0; k < 4; ++k)
            {
                double alpha;
                double beta;
                ComputeV11Coordinates(corners[k], corners[(k + 1) & 3], corners[(k + 2) & 3], corners[(k + 3) & 3], out alpha, out beta);
                double score = Math.Min(alpha, beta);
                if (score > bestScore * (1 + 1e-9) + 1e-12)
                {
                    bestScore = score;
                    rotation = k;
                }
            }
            return true;
        }

        private static double[] ToDouble(Point3f p)
        {
            return new double[] { p.x, p.y, p.z };
        }

        private static double[] Sub(double[] a, double[] b)
        {
            return new double[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new double[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double Length(double[] a)
        {
            return Math.Sqrt(Dot(a, a));
        }

        // Edge function of the watertight test, in double precision: the products of single precision numbers are exact in double precision, so the
        // sign of the difference is exact, and the function of (b, a) is exactly the negative of the function of (a, b)
        private static double EdgeFunction(Float ax, Float ay, Float bx, Float by)
        {
            return (double)ax * (double)by - (double)ay * (double)bx;
        }

        // The edge and distance tests of the watertight ray-triangle intersection for the edge functions e0, e1, e2 of a triangle whose vertices
        // (ray coordinate system, z scaled) are given; the bounds of the conservative t are those of pbrt::IntersectTriangle
        private static bool TestTriangle(double e0, double e1, double e2, Float x0, Float y0, Float z0, Float x1, Float y1, Float z1,
                                         Float x2, Float y2, Float z2, Float tMax, out double w0, out double w1, out double w2, out Float t)
        {
            w0 = 0;
            w1 = 0;
            w2 = 0;
            t = 0;
            if ((e0 < 0 || e1 < 0 || e2 < 0) && (e0 > 0 || e1 > 0 || e2 > 0))
            {
                return false;
            }
            double det = e0 + e1 + e2;
            if (det == 0)
            {
                return false;
            }
            double tScaled = e0 * z0 + e1 * z1 + e2 * z2;
            if (det < 0 && (tScaled >= 0 || tScaled < (double)tMax * det))
            {
                return false;
            }
            else if (det > 0 && (tScaled <= 0 || tScaled > (double)tMax * det))
            {
                return false;
            }

            double invDet = 1 / det;
            w0 = e0 * invDet;
            w1 = e1 * invDet;
            w2 = e2 * invDet;
            double tHit = tScaled * invDet;

            // The hit must be conservatively in front of the ray origin
            double maxZt = Math.Max(Math.Abs((double)z0), Math.Max(Math.Abs((double)z1), Math.Abs((double)z2)));
            double deltaZ = pbrt.gamma(3) * maxZt;
            double maxXt = Math.Max(Math.Abs((double)x0), Math.Max(Math.Abs((double)x1), Math.Abs((double)x2)));
            double maxYt = Math.Max(Math.Abs((double)y0), Math.Max(Math.Abs((double)y1), Math.Abs((double)y2)));
            double deltaX = pbrt.gamma(5) * (maxXt + maxZt);
            double deltaY = pbrt.gamma(5) * (maxYt + maxZt);
            double deltaE = 2 * (pbrt.gamma(2) * maxXt * maxYt + deltaY * maxXt + deltaX * maxYt);
            double maxE = Math.Max(Math.Abs(e0), Math.Max(Math.Abs(e1), Math.Abs(e2)));
            double deltaT = 3 * (pbrt.gamma(3) * maxE * maxZt + deltaE * maxZt + deltaZ * maxE) * Math.Abs(invDet);
            if (tHit <= deltaT)
            {
                return false;
            }
            t = (Float)tHit;
            return true;
        }

        private bool IntersectQuad(BilinearPatchMesh mesh, Ray ray, Float tMax, out QuadHit hit)
        {
            hit = default;
            Point3f q0 = Corner(mesh, 0);
            Point3f q1 = Corner(mesh, 1);
            Point3f q2 = Corner(mesh, 2);
            Point3f q3 = Corner(mesh, 3);

            RayFrame frame = new RayFrame(ray);
            Float x0, y0, z0, x1, y1, z1, x2, y2, z2, x3, y3, z3;
            frame.Project(q0, out x0, out y0, out z0);
            frame.Project(q1, out x1, out y1, out z1);
            frame.Project(q2, out x2, out y2, out z2);
            frame.Project(q3, out x3, out y3, out z3);

            // The four sides and the diagonal q1-q3; the first triangle is (q0, q1, q3), the second (q2, q3, q1), and the diagonal is the edge
            // function of the first and, with the opposite sign, of the second
            double diagonal = EdgeFunction(x1, y1, x3, y3);
            double side30 = EdgeFunction(x3, y3, x0, y0);
            double side01 = EdgeFunction(x0, y0, x1, y1);
            double side12 = EdgeFunction(x1, y1, x2, y2);
            double side23 = EdgeFunction(x2, y2, x3, y3);

            double w0, w1, w2;
            Float t;
            if (TestTriangle(diagonal, side30, side01, x0, y0, z0, x1, y1, z1, x3, y3, z3, tMax, out w0, out w1, out w2, out t))
            {
                hit.second = false;
                hit.w0 = w0;
                hit.w1 = w1;
                hit.w2 = w2;
                hit.t = t;
                return true;
            }
            if (TestTriangle(-diagonal, side12, side23, x2, y2, z2, x3, y3, z3, x1, y1, z1, tMax, out w0, out w1, out w2, out t))
            {
                hit.second = true;
                hit.w0 = w0;
                hit.w1 = w1;
                hit.w2 = w2;
                hit.t = t;
                return true;
            }
            return false;
        }

        // The point of the hit, from the weights of the triangle that was hit
        private Point3f PointOfHit(BilinearPatchMesh mesh, bool second, double w0, double w1, double w2, out Vector3f pError)
        {
            Point3f a, b, c;
            if (!second)
            {
                a = Corner(mesh, 0);
                b = Corner(mesh, 1);
                c = Corner(mesh, 3);
            }
            else
            {
                a = Corner(mesh, 2);
                b = Corner(mesh, 3);
                c = Corner(mesh, 1);
            }
            Float f0 = (Float)w0, f1 = (Float)w1, f2 = (Float)w2;
            Point3f pAbsSum = pbrt.Abs(f0 * a) + pbrt.Abs(f1 * b) + pbrt.Abs(f2 * c);
            pError = pbrt.gamma(7) * (Vector3f)pAbsSum;
            return f0 * a + f1 * b + f2 * c;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            BilinearPatchMesh mesh = GetMesh();
            QuadHit hit;
            if (!IntersectQuad(mesh, ray, tMax, out hit))
            {
                return default;
            }
            return new ShapeIntersection(InteractionOfHit(mesh, hit, ray.time, -ray.d), hit.t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            QuadHit hit;
            return IntersectQuad(GetMesh(), ray, tMax, out hit);
        }

        private SurfaceInteraction InteractionOfHit(BilinearPatchMesh mesh, QuadHit hit, Float time, Vector3f wo)
        {
            Point2f uv = UvOfWeights(hit.second, hit.w0, hit.w1, hit.w2);
            SurfaceInteraction isect = BilinearPatch.InteractionFromIntersection(mesh, quadIndex, uv, time, wo);
            // The point of the triangle that was hit, with the error bounds of a triangle, instead of the point of the (u,v) solve
            Vector3f pError;
            Point3f p = PointOfHit(mesh, hit.second, hit.w0, hit.w1, hit.w2, out pError);
            isect.pi = new Point3fi(p, pError);
            return isect;
        }

        public override Float Area()
        {
            return areaA + areaB;
        }

        public override Bounds3f Bounds()
        {
            BilinearPatchMesh mesh = GetMesh();
            return pbrt.Union(new Bounds3f(Corner(mesh, 0), Corner(mesh, 1)), new Bounds3f(Corner(mesh, 2), Corner(mesh, 3)));
        }

        public override DirectionCone NormalBounds()
        {
            BilinearPatchMesh mesh = GetMesh();
            Point3f q0 = Corner(mesh, 0);
            Vector3f n = pbrt.Normalize(pbrt.Cross(Corner(mesh, 1) - q0, Corner(mesh, 3) - q0));
            if (mesh.n != null)
            {
                int vi = 4 * quadIndex;
                Normal3f ns = (mesh.n[mesh.vertexIndices[vi]] + mesh.n[mesh.vertexIndices[vi + 1]] + mesh.n[mesh.vertexIndices[vi + 2]] +
                               mesh.n[mesh.vertexIndices[vi + 3]]) / 4;
                n = pbrt.FaceForward(n, ns);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            {
                n = -n;
            }
            return new DirectionCone(n);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ Quad meshIndex: %d quadIndex: %d rotation: %d area: %f ]", meshIndex, quadIndex, rotation, Area());
        }

        // The point, the normal and the texture coordinates of the point with the weights w of the corners of one of the two triangles
        private ShapeSample SampleOfWeights(BilinearPatchMesh mesh, bool second, double w0, double w1, double w2, Float time, bool hasTime, Float pdf)
        {
            Vector3f pError;
            Point3f p = PointOfHit(mesh, second, w0, w1, w2, out pError);
            Point2f uv = UvOfWeights(second, w0, w1, w2);
            return SampleAt(mesh, p, pError, uv, time, hasTime, pdf);
        }

        private ShapeSample SampleAt(BilinearPatchMesh mesh, Point3f p, Vector3f pError, Point2f uv, Float time, bool hasTime, Float pdf)
        {
            int vi = 4 * quadIndex;
            int v0 = mesh.vertexIndices[vi], v1 = mesh.vertexIndices[vi + 1], v2 = mesh.vertexIndices[vi + 2], v3 = mesh.vertexIndices[vi + 3];
            Point3f q0 = Corner(mesh, 0);
            Normal3f n = (Normal3f)pbrt.Normalize(pbrt.Cross(Corner(mesh, 1) - q0, Corner(mesh, 3) - q0));
            if (mesh.n != null)
            {
                Normal3f ns = pbrt.Lerp(uv[0], pbrt.Lerp(uv[1], mesh.n[v0], mesh.n[v2]), pbrt.Lerp(uv[1], mesh.n[v1], mesh.n[v3]));
                n = pbrt.FaceForward(n, ns);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            {
                n = -n;
            }
            Point2f st = uv;
            if (mesh.uv != null)
            {
                st = pbrt.Lerp(uv[0], pbrt.Lerp(uv[1], mesh.uv[v0], mesh.uv[v2]), pbrt.Lerp(uv[1], mesh.uv[v1], mesh.uv[v3]));
            }
            Interaction intr = hasTime ? new Interaction(new Point3fi(p, pError), n, time, st) : new Interaction(new Point3fi(p, pError), n, st);
            return new ShapeSample(intr, pdf);
        }

        public override Float PDF(Interaction intr)
        {
            return 1 / Area();
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            BilinearPatchMesh mesh = GetMesh();
            Float pFirst = areaA / (areaA + areaB);
            bool second;
            Point2f uTriangle;
            if (u[0] < pFirst)
            {
                second = false;
                uTriangle = new Point2f(u[0] / pFirst, u[1]);
            }
            else
            {
                second = true;
                uTriangle = new Point2f((u[0] - pFirst) / (1 - pFirst), u[1]);
            }
            uTriangle = new Point2f(std.min(uTriangle[0], (Float)pbrt.OneMinusEpsilon), uTriangle[1]);
            pstd.array3<Float> b = pbrt.SampleUniformTriangle(uTriangle);
            return SampleOfWeights(mesh, second, b[0], b[1], b[2], 0, false, 1 / Area());
        }

        // The solid angles of the two triangles seen from p, and whether the quad is sampled by solid angle from the reference point
        private bool SamplesBySolidAngle(BilinearPatchMesh mesh, ShapeSampleContext ctx, out Float omegaA, out Float omegaB)
        {
            Point3f p = ctx.p();
            Vector3f v0 = pbrt.Normalize(Corner(mesh, 0) - p), v1 = pbrt.Normalize(Corner(mesh, 1) - p);
            Vector3f v2 = pbrt.Normalize(Corner(mesh, 2) - p), v3 = pbrt.Normalize(Corner(mesh, 3) - p);
            omegaA = pbrt.SphericalTriangleArea(v0, v1, v3);
            omegaB = pbrt.SphericalTriangleArea(v2, v3, v1);
            if (rectangle)
            {
                return pbrt.SphericalQuadArea(v0, v1, v2, v3) > 1e-4f && !BilinearPatch.ReferencePointOnPlane(ctx, Corner(mesh, 0), Corner(mesh, 1), Corner(mesh, 3));
            }
            if (omegaA < MinSphericalSampleArea || omegaB < MinSphericalSampleArea || omegaA > MaxSphericalSampleArea ||
                omegaB > MaxSphericalSampleArea)
            {
                return false;
            }
            return !BilinearPatch.ReferencePointOnPlane(ctx, Corner(mesh, 0), Corner(mesh, 1), Corner(mesh, 3));
        }

        private static pstd.span<Float> CosineWeights(Normal3f ns, Vector3f a, Vector3f b, Vector3f c, Vector3f d)
        {
            return new pstd.span<Float>(new Float[] { std.max<Float>(0.01f, pbrt.AbsDot(ns, a)), std.max<Float>(0.01f, pbrt.AbsDot(ns, b)),
                                                      std.max<Float>(0.01f, pbrt.AbsDot(ns, c)), std.max<Float>(0.01f, pbrt.AbsDot(ns, d)) });
        }

        // The parts of the quad above and below the horizon of the reference point (the plane through it with the shading normal), as polygons for the sampling
        // by projected solid angle, and their projected solid angles. False if the quad is not sampled this way from the reference point.
        private bool PrepareProjected(BilinearPatchMesh mesh, ShapeSampleContext ctx, out Basis basis, out ProjectedSolidAnglePolygon upper,
                                      out ProjectedSolidAnglePolygon lower, out double upperArea, out double lowerArea)
        {
            basis = default;
            upper = null;
            lower = null;
            upperArea = 0;
            lowerArea = 0;
            if (!projectedSampling || ctx.ns == new Normal3f(0, 0, 0))
            {
                return false;
            }
            if (BilinearPatch.ReferencePointOnPlane(ctx, Corner(mesh, 0), Corner(mesh, 1), Corner(mesh, 3)))
            {
                return false;
            }
            basis = new Basis(ctx.ns);
            Point3f reference = ctx.p();
            double[] x = new double[4];
            double[] y = new double[4];
            double[] z = new double[4];
            for (int k = 0; k < 4; ++k)
            {
                Point3f c = Corner(mesh, k);
                double ox = (double)c.x - reference.x;
                double oy = (double)c.y - reference.y;
                double oz = (double)c.z - reference.z;
                x[k] = ox * basis.xx + oy * basis.xy + oz * basis.xz;
                y[k] = ox * basis.yx + oy * basis.yy + oz * basis.yz;
                z[k] = ox * basis.zx + oy * basis.zy + oz * basis.zz;
            }
            if (upperPolygon == null)
            {
                upperPolygon = new ProjectedSolidAnglePolygon();
                lowerPolygon = new ProjectedSolidAnglePolygon();
            }
            upper = upperPolygon;
            lower = lowerPolygon;
            if (upper.Prepare(4, x, y, z, 1))
            {
                upperArea = upper.ProjectedSolidAngle;
            }
            if (lower.Prepare(4, x, y, z, -1))
            {
                lowerArea = lower.ProjectedSolidAngle;
            }
            return upperArea + lowerArea > MinProjectedSampleArea;
        }

        // The sample of the direction w from the reference point: the point of the quad in that direction, with the density in solid angle
        private pstd.optional<ShapeSample> SampleProjected(BilinearPatchMesh mesh, ShapeSampleContext ctx, Point2f u, Basis basis, ProjectedSolidAnglePolygon upper,
                                                           ProjectedSolidAnglePolygon lower, double upperArea, double lowerArea)
        {
            double total = upperArea + lowerArea;
            double pUpper = upperArea / total;
            double u0 = u[0];
            ProjectedSolidAnglePolygon chosen;
            double sign;
            if (u0 < pUpper)
            {
                chosen = upper;
                sign = 1;
                u0 = u0 / pUpper;
            }
            else
            {
                chosen = lower;
                sign = -1;
                u0 = (u0 - pUpper) / (1 - pUpper);
            }
            u0 = Math.Min(u0, 1 - 1.1102230246251565e-16);
            double lx, ly, lz;
            chosen.Sample(u0, Math.Min((double)u[1], 1 - 1.1102230246251565e-16), out lx, out ly, out lz);
            lz *= sign;
            double wx = lx * basis.xx + ly * basis.yx + lz * basis.zx;
            double wy = lx * basis.xy + ly * basis.yy + lz * basis.zy;
            double wz = lx * basis.xz + ly * basis.yz + lz * basis.zz;

            // The point of the plane of the quad in that direction, and its coordinates (alpha, beta) in the frame (q0; q1 - q0, q3 - q0)
            Point3f reference = ctx.p();
            Point3f q0 = Corner(mesh, 0), q1 = Corner(mesh, 1), q2 = Corner(mesh, 2), q3 = Corner(mesh, 3);
            double e1x = (double)q1.x - q0.x, e1y = (double)q1.y - q0.y, e1z = (double)q1.z - q0.z;
            double e3x = (double)q3.x - q0.x, e3y = (double)q3.y - q0.y, e3z = (double)q3.z - q0.z;
            double nx = e1y * e3z - e1z * e3y, ny = e1z * e3x - e1x * e3z, nz = e1x * e3y - e1y * e3x;
            double denominator = wx * nx + wy * ny + wz * nz;
            if (denominator == 0)
            {
                return default;
            }
            double rx = (double)q0.x - reference.x, ry = (double)q0.y - reference.y, rz = (double)q0.z - reference.z;
            double t = (rx * nx + ry * ny + rz * nz) / denominator;
            if (!(t > 0))
            {
                return default;
            }
            double dx = t * wx - rx, dy = t * wy - ry, dz = t * wz - rz;
            double nn = nx * nx + ny * ny + nz * nz;
            double alpha = ((dy * e3z - dz * e3y) * nx + (dz * e3x - dx * e3z) * ny + (dx * e3y - dy * e3x) * nz) / nn;
            double beta = ((e1y * dz - e1z * dy) * nx + (e1z * dx - e1x * dz) * ny + (e1x * dy - e1y * dx) * nz) / nn;
            double u1, v1;
            SolveBilinear(a11, b11, alpha, beta, out u1, out v1);
            Point2f uv = ToGivenFrame(rotation, u1, v1);
            Point3f point = new Point3f((Float)(reference.x + t * wx), (Float)(reference.y + t * wy), (Float)(reference.z + t * wz));
            Vector3f pError = pbrt.gamma(6) * (Vector3f)(pbrt.Abs(q0) + pbrt.Abs(q1) + pbrt.Abs(q2) + pbrt.Abs(q3));
            return SampleAt(mesh, point, pError, uv, ctx.time, true, (Float)(Math.Abs(lz) / total));
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            BilinearPatchMesh mesh = GetMesh();
            Basis projectedBasis;
            ProjectedSolidAnglePolygon upperPart, lowerPart;
            double upperArea, lowerArea;
            if (PrepareProjected(mesh, ctx, out projectedBasis, out upperPart, out lowerPart, out upperArea, out lowerArea))
            {
                return SampleProjected(mesh, ctx, u, projectedBasis, upperPart, lowerPart, upperArea, lowerArea);
            }
            Float omegaA, omegaB;
            if (!SamplesBySolidAngle(mesh, ctx, out omegaA, out omegaB))
            {
                // Sample by area and convert the density to solid angle
                pstd.optional<ShapeSample> ss = Sample(u);
                ss.Ref().intr.time = ctx.time;
                Vector3f wi = ss.value().intr.p() - ctx.p();
                if (pbrt.LengthSquared(wi) == 0)
                {
                    return default;
                }
                wi = pbrt.Normalize(wi);
                ss.Ref().pdf /= pbrt.AbsDot(ss.value().intr.n, -wi) / pbrt.DistanceSquared(ctx.p(), ss.value().intr.p());
                if (pbrt.IsInf(ss.value().pdf))
                {
                    return default;
                }
                return ss;
            }

            Point3f reference = ctx.p();
            Point3f q0 = Corner(mesh, 0), q1 = Corner(mesh, 1), q2 = Corner(mesh, 2), q3 = Corner(mesh, 3);
            if (rectangle)
            {
                // The spherical rectangle, warped for the cosine at the reference point
                Float pdfRectangle = 1;
                if (ctx.ns != new Normal3f(0, 0, 0))
                {
                    pstd.span<Float> w = CosineWeights(ctx.ns, pbrt.Normalize(q0 - reference), pbrt.Normalize(q1 - reference),
                                                       pbrt.Normalize(q3 - reference), pbrt.Normalize(q2 - reference));
                    u = pbrt.SampleBilinear(u, w);
                    pdfRectangle *= pbrt.BilinearPDF(u, w);
                }
                Vector3f eu = q1 - q0, ev = q3 - q0;
                Float quadPDF;
                Point3f pRect = pbrt.SampleSphericalRectangle(reference, q0, eu, ev, u, &quadPDF);
                pdfRectangle *= quadPDF;
                Point2f local = new Point2f(pbrt.Dot(pRect - q0, eu) / pbrt.DistanceSquared(q1, q0), pbrt.Dot(pRect - q0, ev) / pbrt.DistanceSquared(q3, q0));
                Point2f given = ToGivenFrame(rotation, local[0], local[1]);
                Vector3f rectError = pbrt.gamma(6) * (Vector3f)(pbrt.Abs(q0) + pbrt.Abs(q1) + pbrt.Abs(q2) + pbrt.Abs(q3));
                return SampleAt(mesh, pRect, rectError, given, ctx.time, true, pdfRectangle);
            }

            // One of the two spherical triangles, chosen by solid angle, warped for the cosine at the reference point
            Float omega = omegaA + omegaB;
            Float pFirst = omegaA / omega;
            bool second;
            Point2f uTriangle;
            if (u[0] < pFirst)
            {
                second = false;
                uTriangle = new Point2f(u[0] / pFirst, u[1]);
            }
            else
            {
                second = true;
                uTriangle = new Point2f((u[0] - pFirst) / (1 - pFirst), u[1]);
            }
            uTriangle = new Point2f(std.min(uTriangle[0], (Float)pbrt.OneMinusEpsilon), uTriangle[1]);

            Point3f a = second ? q2 : q0, b = second ? q3 : q1, c = second ? q1 : q3;
            Float pdf = second ? 1 - pFirst : pFirst;
            if (ctx.ns != new Normal3f(0, 0, 0))
            {
                pstd.span<Float> w = TriangleCosineWeights(ctx.ns, reference, a, b, c);
                uTriangle = pbrt.SampleBilinear(uTriangle, w);
                pdf *= pbrt.BilinearPDF(uTriangle, w);
            }
            Float trianglePDF;
            pstd.array3<Float> bary = pbrt.SampleSphericalTriangle(new pstd.array3<Point3f>(a, b, c), reference, uTriangle, &trianglePDF);
            if (trianglePDF == 0)
            {
                return default;
            }
            pdf *= trianglePDF;
            return SampleOfWeights(mesh, second, bary[0], bary[1], bary[2], ctx.time, true, pdf);
        }

        // The weights the spherical triangle sampling of Triangle uses for the cosine at the reference point (including the order of its corners)
        private static pstd.span<Float> TriangleCosineWeights(Normal3f ns, Point3f reference, Point3f a, Point3f b, Point3f c)
        {
            Vector3f wi0 = pbrt.Normalize(a - reference), wi1 = pbrt.Normalize(b - reference), wi2 = pbrt.Normalize(c - reference);
            return new pstd.span<Float>(new Float[] { std.max<Float>(0.01f, pbrt.AbsDot(ns, wi1)), std.max<Float>(0.01f, pbrt.AbsDot(ns, wi1)),
                                                      std.max<Float>(0.01f, pbrt.AbsDot(ns, wi0)), std.max<Float>(0.01f, pbrt.AbsDot(ns, wi2)) });
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            BilinearPatchMesh mesh = GetMesh();
            Ray ray = ctx.SpawnRay(wi);
            QuadHit hit;
            if (!IntersectQuad(mesh, ray, Infinity, out hit))
            {
                return 0;
            }
            Basis projectedBasis;
            ProjectedSolidAnglePolygon upperPart, lowerPart;
            double upperArea, lowerArea;
            if (PrepareProjected(mesh, ctx, out projectedBasis, out upperPart, out lowerPart, out upperArea, out lowerArea))
            {
                double cosine = Math.Abs((double)wi.x * projectedBasis.zx + (double)wi.y * projectedBasis.zy + (double)wi.z * projectedBasis.zz);
                return (Float)(cosine / (upperArea + lowerArea));
            }
            Float omegaA, omegaB;
            Vector3f pError;
            Point3f pHit = PointOfHit(mesh, hit.second, hit.w0, hit.w1, hit.w2, out pError);
            if (!SamplesBySolidAngle(mesh, ctx, out omegaA, out omegaB))
            {
                // The density of area sampling, in solid angle
                Point3f q0 = Corner(mesh, 0);
                Normal3f n = (Normal3f)pbrt.Normalize(pbrt.Cross(Corner(mesh, 1) - q0, Corner(mesh, 3) - q0));
                Float areaPdf = (1 / Area()) / (pbrt.AbsDot(n, -wi) / pbrt.DistanceSquared(ctx.p(), pHit));
                return pbrt.IsInf(areaPdf) ? 0 : areaPdf;
            }

            Point3f reference = ctx.p();
            Point3f p0 = Corner(mesh, 0), p1 = Corner(mesh, 1), p2 = Corner(mesh, 2), p3 = Corner(mesh, 3);
            if (rectangle)
            {
                Vector3f v0 = pbrt.Normalize(p0 - reference), v1 = pbrt.Normalize(p1 - reference);
                Vector3f v2 = pbrt.Normalize(p2 - reference), v3 = pbrt.Normalize(p3 - reference);
                Float pdfRectangle = 1 / pbrt.SphericalQuadArea(v0, v1, v2, v3);
                if (ctx.ns != new Normal3f(0, 0, 0))
                {
                    pstd.span<Float> w = CosineWeights(ctx.ns, v0, v1, v3, v2);
                    Point2f uRect = pbrt.InvertSphericalRectangleSample(reference, p0, p1 - p0, p3 - p0, pHit);
                    return pbrt.BilinearPDF(uRect, w) * pdfRectangle;
                }
                return pdfRectangle;
            }

            Float pdf = 1 / (omegaA + omegaB);
            if (ctx.ns != new Normal3f(0, 0, 0))
            {
                Point3f a = hit.second ? p2 : p0, b = hit.second ? p3 : p1, c = hit.second ? p1 : p3;
                Point2f uTriangle = pbrt.InvertSphericalTriangleSample(new pstd.array3<Point3f>(a, b, c), reference, wi);
                pdf *= pbrt.BilinearPDF(uTriangle, TriangleCosineWeights(ctx.ns, reference, a, b, c));
            }
            return pdf;
        }

        // The Quad shapes (and the Triangle shapes of the quads that are not planar and convex) of a mesh of quadrilaterals. indices holds four per quad,
        // in order around the quad. P is in object space.
        public static pstd.vector<Shape> CreateShapes(Transform renderFromObject, bool reverseOrientation, List<int> indices, List<Point3f> P,
                                                      List<Normal3f> N, List<Point2f> uv, List<int> faceIndices, bool projectedSampling, Allocator alloc)
        {
            int nQuads = indices.Count / 4;
            List<int> goodIndices = new List<int>();
            List<int> goodFaces = new List<int>();
            List<int> goodRotation = new List<int>();
            List<int> triangleIndices = new List<int>();
            List<int> triangleFaces = new List<int>();

            for (int i = 0; i < nQuads; ++i)
            {
                int[] c = { indices[4 * i], indices[4 * i + 1], indices[4 * i + 2], indices[4 * i + 3] };
                Point3f[] r = new Point3f[4];
                for (int k = 0; k < 4; ++k)
                {
                    r[k] = renderFromObject.Apply(P[c[k]]);
                }
                int rotation;
                if (Classify(r[0], r[1], r[2], r[3], out rotation))
                {
                    // Stored as a bilinear patch stores its corners: p00, p10, p01, p11
                    goodIndices.Add(c[0]);
                    goodIndices.Add(c[1]);
                    goodIndices.Add(c[3]);
                    goodIndices.Add(c[2]);
                    goodRotation.Add(rotation);
                    goodFaces.Add(faceIndices.Count != 0 ? faceIndices[i] : 0);
                }
                else
                {
                    // Two triangles, split along the shorter diagonal
                    bool firstDiagonal = pbrt.DistanceSquared(r[0], r[2]) < pbrt.DistanceSquared(r[1], r[3]);
                    int[] split = firstDiagonal ? new int[] { c[0], c[1], c[2], c[0], c[2], c[3] } : new int[] { c[1], c[2], c[3], c[1], c[3], c[0] };
                    triangleIndices.AddRange(split);
                    triangleFaces.Add(faceIndices.Count != 0 ? faceIndices[i] : 0);
                    triangleFaces.Add(faceIndices.Count != 0 ? faceIndices[i] : 0);
                }
            }

            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            if (triangleIndices.Count != 0)
            {
                TriangleMesh triangleMesh = new TriangleMesh(renderFromObject, reverseOrientation, new pstd.vector<int>(triangleIndices),
                                                             new pstd.vector<Point3f>(P), new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(N),
                                                             new pstd.vector<Point2f>(uv), new pstd.vector<int>(faceIndices.Count != 0 ? triangleFaces : new List<int>()),
                                                             alloc);
                pstd.vector<Shape> triangles = Triangle.CreateTriangles(triangleMesh, alloc);
                for (int i = 0; i < (int)triangles.size(); ++i)
                {
                    shapes.push_back(triangles[i]);
                }
            }
            if (goodIndices.Count != 0)
            {
                BilinearPatchMesh mesh = new BilinearPatchMesh(renderFromObject, reverseOrientation, new pstd.vector<int>(goodIndices),
                                                               new pstd.vector<Point3f>(P), new pstd.vector<Normal3f>(N), new pstd.vector<Point2f>(uv),
                                                               new pstd.vector<int>(faceIndices.Count != 0 ? goodFaces : new List<int>()), null, alloc);
                int meshIndex;
                lock (allMeshesLock)
                {
                    meshIndex = allMeshes.Count;
                    allMeshes.Add(mesh);
                }
                for (int i = 0; i < goodRotation.Count; ++i)
                {
                    shapes.push_back(new Quad(mesh, meshIndex, i, goodRotation[i], projectedSampling));
                }
            }
            return shapes;
        }

        // The sampling of the quads from a point: "solidangle" (the default; the spherical rectangle or two spherical triangles, warped for the cosine) or
        // "projected" (proportional to the projected solid angle, see polygon_sampling.cs)
        public static bool ParseSampling(string sampling, FileLoc loc)
        {
            if (sampling != "solidangle" && sampling != "projected")
            {
                pbrt.ErrorExit(loc, "\"sampling\" must be \"solidangle\" or \"projected\".");
            }
            return sampling == "projected";
        }

        // Shape "quadmesh": "integer indices" (four per quad, in order around it; none for a single quad), "point3 P", "point2 uv", "normal N",
        // "integer faceIndices", "string sampling"
        public static pstd.vector<Shape> Create(Transform renderFromObject, bool reverseOrientation, ParameterDictionary parameters, FileLoc loc,
                                                Allocator alloc)
        {
            List<int> indices = parameters.GetIntArray("indices");
            List<Point3f> P = parameters.GetPoint3fArray("P");
            List<Point2f> uv = parameters.GetPoint2fArray("uv");
            if (P.Count == 0)
            {
                pbrt.ErrorExit(loc, "Vertex positions \"P\" must be provided with quadmesh shape.");
            }
            if (indices.Count == 0)
            {
                if (P.Count != 4)
                {
                    pbrt.ErrorExit(loc, "Vertex indices \"indices\" must be provided with quadmesh shape.");
                }
                indices = new List<int> { 0, 1, 2, 3 };
            }
            else if ((indices.Count % 4) != 0)
            {
                pbrt.Error(loc, pbrt.StringPrintf("Number of vertex indices %d not a multiple of 4. Discarding %d excess.", indices.Count, indices.Count % 4));
                while ((indices.Count % 4) != 0)
                {
                    indices.RemoveAt(indices.Count - 1);
                }
            }
            if (uv.Count != 0 && uv.Count != P.Count)
            {
                pbrt.Error(loc, "Number of \"uv\"s for quad mesh must match \"P\"s. Discarding uvs.");
                uv = new List<Point2f>();
            }
            List<Normal3f> N = parameters.GetNormal3fArray("N");
            if (N.Count != 0 && N.Count != P.Count)
            {
                pbrt.Error(loc, "Number of \"N\"s for quad mesh must match \"P\"s. Discarding \"N\"s.");
                N = new List<Normal3f>();
            }
            for (int i = 0; i < indices.Count; ++i)
            {
                if (indices[i] < 0 || indices[i] >= P.Count)
                {
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("quadmesh has out of-bounds vertex index %d (%d \"P\" values were given).", indices[i], P.Count));
                }
            }
            List<int> faceIndices = parameters.GetIntArray("faceIndices");
            if (faceIndices.Count != 0 && faceIndices.Count != indices.Count / 4)
            {
                pbrt.Error(loc, pbrt.StringPrintf("Number of face indices %d does not match number of quads %d. Discarding face indices.",
                                                  faceIndices.Count, indices.Count / 4));
                faceIndices = new List<int>();
            }
            return CreateShapes(renderFromObject, reverseOrientation, indices, P, N, uv, faceIndices, ParseSampling(parameters.GetOneString("sampling", "solidangle"), loc), alloc);
        }
    }
}  // namespace pbrt
