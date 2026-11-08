// (not in pbrt-v4) shapes_quad2.cs
//
// Shape "quadmesh2": the planar convex quadrilateral of shapes_quad.cs, written in the form of Triangle (shapes.cs) so that the two can be compared. It does
// the same as Quad (the same intersection test with the diagonal shared by the two halves, the same bilinear (u,v), the same sampling by area, by solid
// angle and by projected solid angle, the same classification), with
//
//  * a QuadMesh as TriangleMesh is one (the vertex indices, four per quad in order around it, the positions in render space, the normals, uv, face
//    indices, and per quad the frame of the (u,v) solve and whether it is a rectangle), instead of a BilinearPatchMesh whose corner order is permuted,
//  * a Quad2 that is only (meshIndex, quadIndex), as Triangle is: no a11, b11, rotation, areas or flags of its own,
//  * a free function IntersectQuad(ray, tMax, q0, q1, q2, q3) that returns an optional QuadIntersection, as IntersectTriangle does, with the ray coordinate
//    system computed in line instead of in a RayFrame, and no QuadHit,
//  * a static InteractionFromIntersection(mesh, quadIndex, qi, time, wo) that does what BilinearPatch.InteractionFromIntersection does for the hit,
//  * Area, SolidAngle, Bounds, NormalBounds, Sample, PDF in the order of Triangle.
//
// What is shared with Quad: Classify, ComputeV11Coordinates, SolveBilinear, IsRectangle, ToGivenFrame, EdgeFunction, TestTriangle and Basis. What is shared with
// Ngon2 and Rectangle2: the functions of PolygonSampling (shapes_polygon_common.cs): the sampling by projected solid angle and the conversion of the density
// of a sample by area. The intersection is its own (the quad is split along the diagonal of its frame, which Ngon2 and Rectangle2 do not need).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace pbrt
{
    // QuadMesh Definition
    public unsafe class QuadMesh
    {
        // QuadMesh Public Methods
        public QuadMesh(Transform renderFromObject, bool reverseOrientation, pstd.vector<int> indices, pstd.vector<Point3f> P, pstd.vector<Normal3f> N,
                        pstd.vector<Point2f> UV, pstd.vector<int> fIndices, List<int> rotations, bool projectedSampling, Allocator alloc)
        {
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            this.projectedSampling = projectedSampling;
            nQuads = (int)(indices.size() / 4);
            nVertices = (int)P.size();
            pbrt.CHECK_EQ((indices.size() % 4), (ulong)0);
            pbrt.CHECK_EQ(rotations.Count, nQuads);
            vertexIndices = pbrt.intBufferCache.LookupOrAdd(new pstd.span<int>(indices.data(), 0, (int)indices.size()), alloc);

            // Transform mesh vertices to rendering space
            Point3f[] pts = P.ToArray();
            for (int i = 0; i < pts.Length; ++i)
            {
                pts[i] = renderFromObject.Apply(pts[i]);
            }
            p = pbrt.point3BufferCache.LookupOrAdd(new pstd.span<Point3f>(pts), alloc);

            if (!UV.empty())
            {
                pbrt.CHECK_EQ((ulong)nVertices, UV.size());
                uv = pbrt.point2BufferCache.LookupOrAdd(new pstd.span<Point2f>(UV.data(), 0, (int)UV.size()), alloc);
            }
            if (!N.empty())
            {
                pbrt.CHECK_EQ((ulong)nVertices, N.size());
                Normal3f[] nns = N.ToArray();
                for (int i = 0; i < nns.Length; ++i)
                {
                    nns[i] = renderFromObject.Apply(nns[i]);
                    if (reverseOrientation)
                    {
                        nns[i] = -nns[i];
                    }
                }
                n = pbrt.normal3BufferCache.LookupOrAdd(new pstd.span<Normal3f>(nns), alloc);
            }
            if (!fIndices.empty())
            {
                pbrt.CHECK_EQ((ulong)nQuads, fIndices.size());
                faceIndices = pbrt.intBufferCache.LookupOrAdd(new pstd.span<int>(fIndices.data(), 0, (int)fIndices.size()), alloc);
            }

            // The frame of every quad and whether it is a rectangle
            rotation = rotations.ToArray();
            rectangle = new bool[nQuads];
            for (int i = 0; i < nQuads; ++i)
            {
                FrameCorners(i, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
                rectangle[i] = Quad.IsRectangle(q0, q1, q2, q3);
            }
        }

        // Corner k (in order around the quad) of a quad
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point3f Corner(int quadIndex, int corner)
        {
            return p[vertexIndices[4 * quadIndex + (corner & 3)]];
        }

        // The corners in the order of the frame the quad is intersected in: frame r starts at corner r
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FrameCorners(int quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3)
        {
            int r = rotation[quadIndex];
            q0 = Corner(quadIndex, r);
            q1 = Corner(quadIndex, r + 1);
            q2 = Corner(quadIndex, r + 2);
            q3 = Corner(quadIndex, r + 3);
        }

        // QuadMesh Public Members
        public bool reverseOrientation, transformSwapsHandedness, projectedSampling;
        public int nQuads, nVertices;
        public int[] vertexIndices = null;
        public Point3f[] p = null;
        public Normal3f[] n = null;
        public Point2f[] uv = null;
        public int[] faceIndices = null;
        public int[] rotation = null;
        public bool[] rectangle = null;
    }

    // QuadIntersection Definition: the weights of the corners of the triangle that was hit, (q0, q1, q3) for the first and (q2, q3, q1) for the second
    public struct QuadIntersection
    {
        public QuadIntersection(bool second, double b0, double b1, double b2, Float t)
        {
            this.second = second;
            this.b0 = b0;
            this.b1 = b1;
            this.b2 = b2;
            this.t = t;
        }

        public bool second;
        public double b0, b1, b2;
        public Float t;
    }

    public sealed unsafe class Quad2 : Shape
    {
        // Quad2 Public Methods
        public Quad2() { }
        public Quad2(int meshIndex, int quadIndex)
        {
            this.meshIndex = meshIndex;
            this.quadIndex = quadIndex;
        }

        static List<QuadMesh> allMeshes;
        static readonly object allMeshesLock = new object();
        const Float MinSphericalSampleArea = 3e-4f;
        const Float MaxSphericalSampleArea = 6.22f;

        [ThreadStatic]
        static Point3f[] cornerScratch;

        public static void Init(Allocator alloc)
        {
            allMeshes = new List<QuadMesh>();
        }

        QuadMesh GetMesh()
        {
            return allMeshes[meshIndex];
        }

        public override Float Area()
        {
            QuadMesh mesh = GetMesh();
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            return 0.5f * pbrt.Length(pbrt.Cross(q1 - q0, q3 - q0)) + 0.5f * pbrt.Length(pbrt.Cross(q3 - q2, q1 - q2));
        }

        public Float SolidAngle(Point3f p)
        {
            QuadMesh mesh = GetMesh();
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            return pbrt.SphericalTriangleArea(pbrt.Normalize(q0 - p), pbrt.Normalize(q1 - p), pbrt.Normalize(q3 - p)) +
                   pbrt.SphericalTriangleArea(pbrt.Normalize(q2 - p), pbrt.Normalize(q3 - p), pbrt.Normalize(q1 - p));
        }

        // QuadIntersection of a ray with the quad whose corners are q0 to q3 (in order around it, the diagonal is q1-q3): the ray is moved to the ray
        // coordinate system once, the five edge functions are those of IntersectTriangle (in double precision), and the diagonal is evaluated once and used with
        // opposite signs for the two triangles
        public static pstd.optional<QuadIntersection> IntersectQuad(Ray ray, Float tMax, Point3f q0, Point3f q1, Point3f q2, Point3f q3)
        {
            if (IntersectQuad(ray, tMax, q0, q1, q2, q3, out QuadIntersection found))
            {
                return found;
            }
            return default;
        }

        public static bool IntersectQuad(in Ray ray, Float tMax, in Point3f q0, in Point3f q1, in Point3f q2, in Point3f q3, out QuadIntersection result)
        {
            result = default;
            // Transform quad vertices to ray coordinate space
            int kz = pbrt.MaxComponentIndex(pbrt.Abs(ray.d));
            int kx = kz + 1;
            if (kx == 3)
            {
                kx = 0;
            }
            int ky = kx + 1;
            if (ky == 3)
            {
                ky = 0;
            }
            Vector3f d = pbrt.Permute(ray.d, kx, ky, kz);
            Float Sx = -d.x / d.z;
            Float Sy = -d.y / d.z;
            Float Sz = 1 / d.z;
            Point3f p0t = pbrt.Permute(q0 - (Vector3f)ray.o, kx, ky, kz);
            Point3f p1t = pbrt.Permute(q1 - (Vector3f)ray.o, kx, ky, kz);
            Point3f p2t = pbrt.Permute(q2 - (Vector3f)ray.o, kx, ky, kz);
            Point3f p3t = pbrt.Permute(q3 - (Vector3f)ray.o, kx, ky, kz);
            p0t.x += Sx * p0t.z;
            p0t.y += Sy * p0t.z;
            p1t.x += Sx * p1t.z;
            p1t.y += Sy * p1t.z;
            p2t.x += Sx * p2t.z;
            p2t.y += Sy * p2t.z;
            p3t.x += Sx * p3t.z;
            p3t.y += Sy * p3t.z;
            p0t.z *= Sz;
            p1t.z *= Sz;
            p2t.z *= Sz;
            p3t.z *= Sz;

            // The four sides and the diagonal
            double diagonal = Quad.EdgeFunction(p1t.x, p1t.y, p3t.x, p3t.y);
            double side30 = Quad.EdgeFunction(p3t.x, p3t.y, p0t.x, p0t.y);
            double side01 = Quad.EdgeFunction(p0t.x, p0t.y, p1t.x, p1t.y);
            double side12 = Quad.EdgeFunction(p1t.x, p1t.y, p2t.x, p2t.y);
            double side23 = Quad.EdgeFunction(p2t.x, p2t.y, p3t.x, p3t.y);

            double b0, b1, b2;
            Float t;
            if (Quad.TestTriangle(diagonal, side30, side01, p0t.x, p0t.y, p0t.z, p1t.x, p1t.y, p1t.z, p3t.x, p3t.y, p3t.z, tMax, out b0, out b1, out b2, out t))
            {
                result = new QuadIntersection(false, b0, b1, b2, t);
                return true;
            }
            if (Quad.TestTriangle(-diagonal, side12, side23, p2t.x, p2t.y, p2t.z, p3t.x, p3t.y, p3t.z, p1t.x, p1t.y, p1t.z, tMax, out b0, out b1, out b2, out t))
            {
                result = new QuadIntersection(true, b0, b1, b2, t);
                return true;
            }
            return false;
        }

        // The point of the hit, from the weights of the triangle that was hit, with the error bounds of a triangle
        static Point3f PointOfHit(in Point3f q0, in Point3f q1, in Point3f q2, in Point3f q3, QuadIntersection qi, out Vector3f pError)
        {
            Point3f a = qi.second ? q2 : q0;
            Point3f b = qi.second ? q3 : q1;
            Point3f c = qi.second ? q1 : q3;
            Float f0 = (Float)qi.b0, f1 = (Float)qi.b1, f2 = (Float)qi.b2;
            Point3f pAbsSum = pbrt.Abs(f0 * a) + pbrt.Abs(f1 * b) + pbrt.Abs(f2 * c);
            pError = pbrt.gamma(7) * (Vector3f)pAbsSum;
            return f0 * a + f1 * b + f2 * c;
        }

        // The (u,v), in the frame of the quad as it was given, of the point with the weights w of the corners of one of the two triangles
        static Point2f UvOfWeights(int rotation, in Point3f q0, in Point3f q1, in Point3f q2, in Point3f q3, bool second, double w0, double w1, double w2)
        {
            Quad.ComputeV11Coordinates(q0, q1, q2, q3, out double a11, out double b11);
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
            Quad.SolveBilinear(a11, b11, alpha, beta, out double u, out double v);
            return Quad.ToGivenFrame(rotation, u, v);
        }

        public static SurfaceInteraction InteractionFromIntersection(QuadMesh mesh, int quadIndex, QuadIntersection qi, Float time, Vector3f wo)
        {
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            Point2f uv = UvOfWeights(mesh.rotation[quadIndex], q0, q1, q2, q3, qi.second, qi.b0, qi.b1, qi.b2);
            return InteractionFromUv(mesh, quadIndex, q0, q1, q2, q3, uv, time, wo, qi);
        }

        // The interaction at the (u,v) of the bilinear patch of the corners as they were given (corner 0 at (0,0), 1 at (1,0), 3 at (0,1), 2 at (1,1));
        // the point is that of the hit, with the error bounds of a triangle
        static SurfaceInteraction InteractionFromUv(QuadMesh mesh, int quadIndex, in Point3f q0, in Point3f q1, in Point3f q2, in Point3f q3, Point2f uv, Float time, Vector3f wo, QuadIntersection qi)
        {
            // Compute bilinear patch point, dpdu and dpdv for (u,v)
            int vi = 4 * quadIndex;
            int[] v = { mesh.vertexIndices[vi], mesh.vertexIndices[vi + 1], mesh.vertexIndices[vi + 3], mesh.vertexIndices[vi + 2] };
            Point3f p00 = mesh.p[v[0]], p10 = mesh.p[v[1]];
            Point3f p01 = mesh.p[v[2]], p11 = mesh.p[v[3]];

            Point3f p = pbrt.Lerp(uv[0], pbrt.Lerp(uv[1], p00, p01), pbrt.Lerp(uv[1], p10, p11));
            Vector3f dpdu = pbrt.Lerp(uv[1], p10, p11) - pbrt.Lerp(uv[1], p00, p01);
            Vector3f dpdv = pbrt.Lerp(uv[0], p01, p11) - pbrt.Lerp(uv[0], p00, p10);

            // Compute (s,t) texture coordinates at the (u,v)
            Point2f st = uv;
            Float duds = 1, dudt = 0, dvds = 0, dvdt = 1;
            if (mesh.uv != null)
            {
                Point2f uv00 = mesh.uv[v[0]], uv10 = mesh.uv[v[1]];
                Point2f uv01 = mesh.uv[v[2]], uv11 = mesh.uv[v[3]];
                st = pbrt.Lerp(uv[0], pbrt.Lerp(uv[1], uv00, uv01), pbrt.Lerp(uv[1], uv10, uv11));

                // Update dpdu and dpdv accounting for (s,t)
                Vector2f dstdu = pbrt.Lerp(uv[1], uv10, uv11) - pbrt.Lerp(uv[1], uv00, uv01);
                Vector2f dstdv = pbrt.Lerp(uv[0], uv01, uv11) - pbrt.Lerp(uv[0], uv00, uv10);

                duds = std.abs(dstdu[0]) < 1e-8f ? 0 : 1 / dstdu[0];
                dvds = std.abs(dstdv[0]) < 1e-8f ? 0 : 1 / dstdv[0];
                dudt = std.abs(dstdu[1]) < 1e-8f ? 0 : 1 / dstdu[1];
                dvdt = std.abs(dstdv[1]) < 1e-8f ? 0 : 1 / dstdv[1];

                Vector3f dpds = dpdu * duds + dpdv * dvds;
                Vector3f dpdt = dpdu * dudt + dpdv * dvdt;

                if (pbrt.Cross(dpds, dpdt) != new Vector3f(0, 0, 0))
                {
                    if (pbrt.Dot(pbrt.Cross(dpdu, dpdv), pbrt.Cross(dpds, dpdt)) < 0)
                    {
                        dpdt = -dpdt;
                    }
                    dpdu = dpds;
                    dpdv = dpdt;
                }
            }

            // Find partial derivatives dndu and dndv
            Vector3f d2Pduu = new Vector3f(0, 0, 0), d2Pdvv = new Vector3f(0, 0, 0);
            Vector3f d2Pduv = (p00 - p01) + (p11 - p10);
            Float E = pbrt.Dot(dpdu, dpdu), F = pbrt.Dot(dpdu, dpdv), G = pbrt.Dot(dpdv, dpdv);
            Vector3f n = pbrt.Normalize(pbrt.Cross(dpdu, dpdv));
            Float e = pbrt.Dot(n, d2Pduu), f = pbrt.Dot(n, d2Pduv), g = pbrt.Dot(n, d2Pdvv);

            Float EGF2 = pbrt.DifferenceOfProducts(E, G, F, F);
            Float invEGF2 = (EGF2 == 0) ? (Float)0 : 1 / EGF2;
            Normal3f dndu = (Normal3f)((f * F - e * G) * invEGF2 * dpdu + (e * F - f * E) * invEGF2 * dpdv);
            Normal3f dndv = (Normal3f)((g * F - f * G) * invEGF2 * dpdu + (f * F - g * E) * invEGF2 * dpdv);

            Normal3f dnds = dndu * duds + dndv * dvds;
            Normal3f dndt = dndu * dudt + dndv * dvdt;
            dndu = dnds;
            dndv = dndt;

            // The point of the triangle that was hit, with the error bounds of a triangle, instead of the point of the (u,v) solve
            Point3f pHit = PointOfHit(q0, q1, q2, q3, qi, out Vector3f pError);

            int faceIndex = mesh.faceIndices != null ? mesh.faceIndices[quadIndex] : 0;
            bool flipNormal = mesh.reverseOrientation ^ mesh.transformSwapsHandedness;
            SurfaceInteraction isect = new SurfaceInteraction(new Point3fi(pHit, pError), st, wo, dpdu, dpdv, dndu, dndv, time, flipNormal, faceIndex);

            // Compute the shading normal if necessary
            if (mesh.n != null)
            {
                Normal3f n00 = mesh.n[v[0]], n10 = mesh.n[v[1]];
                Normal3f n01 = mesh.n[v[2]], n11 = mesh.n[v[3]];
                Normal3f ns = pbrt.Lerp(uv[0], pbrt.Lerp(uv[1], n00, n01), pbrt.Lerp(uv[1], n10, n11));
                if (pbrt.LengthSquared(ns) > 0)
                {
                    ns = pbrt.Normalize(ns);
                    Normal3f dnduS = pbrt.Lerp(uv[1], n10, n11) - pbrt.Lerp(uv[1], n00, n01);
                    Normal3f dndvS = pbrt.Lerp(uv[0], n01, n11) - pbrt.Lerp(uv[0], n00, n10);
                    Normal3f dndsS = dnduS * duds + dndvS * dvds;
                    Normal3f dndtS = dnduS * dudt + dndvS * dvdt;
                    dnduS = dndsS;
                    dndvS = dndtS;

                    Transform r = pbrt.RotateFromTo((Vector3f)pbrt.Normalize(isect.n), (Vector3f)ns);
                    isect.SetShadingGeometry(ns, r.Apply(dpdu), r.Apply(dpdv), dnduS, dndvS, true);
                }
            }
            return isect;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            QuadMesh mesh = GetMesh();
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            pstd.optional<QuadIntersection> qi = IntersectQuad(ray, tMax, q0, q1, q2, q3);
            if (!qi)
            {
                return default;
            }
            SurfaceInteraction intr = InteractionFromIntersection(mesh, quadIndex, qi.value(), ray.time, -ray.d);
            return new ShapeIntersection(intr, qi.value().t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            QuadMesh mesh = GetMesh();
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            return IntersectQuad(ray, tMax, q0, q1, q2, q3, out QuadIntersection qi);
        }

        public override Bounds3f Bounds()
        {
            QuadMesh mesh = GetMesh();
            return pbrt.Union(new Bounds3f(mesh.Corner(quadIndex, 0), mesh.Corner(quadIndex, 1)), new Bounds3f(mesh.Corner(quadIndex, 2), mesh.Corner(quadIndex, 3)));
        }

        public override DirectionCone NormalBounds()
        {
            QuadMesh mesh = GetMesh();
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            Vector3f n = pbrt.Normalize(pbrt.Cross(q1 - q0, q3 - q0));
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
            return pbrt.StringPrintf("[ Quad2 meshIndex: %d quadIndex: %d area: %f ]", meshIndex, quadIndex, Area());
        }

        // The point, the normal and the texture coordinates of the sample with the (u,v) of the bilinear patch of the corners as they were given
        ShapeSample SampleAt(QuadMesh mesh, Point3f p, Vector3f pError, Point2f uv, Float time, bool hasTime, Float pdf)
        {
            int vi = 4 * quadIndex;
            int v0 = mesh.vertexIndices[vi], v1 = mesh.vertexIndices[vi + 1], v2 = mesh.vertexIndices[vi + 3], v3 = mesh.vertexIndices[vi + 2];
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            Normal3f n = (Normal3f)pbrt.Normalize(pbrt.Cross(q1 - q0, q3 - q0));
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

        ShapeSample SampleOfWeights(QuadMesh mesh, bool second, double w0, double w1, double w2, Float time, bool hasTime, Float pdf)
        {
            QuadIntersection qi = new QuadIntersection(second, w0, w1, w2, 0);
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            Point3f p = PointOfHit(q0, q1, q2, q3, qi, out Vector3f pError);
            return SampleAt(mesh, p, pError, UvOfWeights(mesh.rotation[quadIndex], q0, q1, q2, q3, second, w0, w1, w2), time, hasTime, pdf);
        }

        public override Float PDF(Interaction intr)
        {
            return 1 / Area();
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            QuadMesh mesh = GetMesh();
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            Float areaA = 0.5f * pbrt.Length(pbrt.Cross(q1 - q0, q3 - q0));
            Float areaB = 0.5f * pbrt.Length(pbrt.Cross(q3 - q2, q1 - q2));
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
            return SampleOfWeights(mesh, second, b[0], b[1], b[2], 0, false, 1 / (areaA + areaB));
        }

        // The solid angles of the two triangles seen from the reference point, and whether the quad is sampled by solid angle from it
        bool SamplesBySolidAngle(QuadMesh mesh, ShapeSampleContext ctx, out Float omegaA, out Float omegaB)
        {
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            Point3f p = ctx.p();
            Vector3f v0 = pbrt.Normalize(q0 - p), v1 = pbrt.Normalize(q1 - p);
            Vector3f v2 = pbrt.Normalize(q2 - p), v3 = pbrt.Normalize(q3 - p);
            omegaA = pbrt.SphericalTriangleArea(v0, v1, v3);
            omegaB = pbrt.SphericalTriangleArea(v2, v3, v1);
            if (mesh.rectangle[quadIndex])
            {
                return pbrt.SphericalQuadArea(v0, v1, v2, v3) > 1e-4f && !BilinearPatch.ReferencePointOnPlane(ctx, q0, q1, q3);
            }
            if (omegaA < MinSphericalSampleArea || omegaB < MinSphericalSampleArea || omegaA > MaxSphericalSampleArea || omegaB > MaxSphericalSampleArea)
            {
                return false;
            }
            return !BilinearPatch.ReferencePointOnPlane(ctx, q0, q1, q3);
        }

        static pstd.span<Float> CosineWeights(Normal3f ns, Vector3f a, Vector3f b, Vector3f c, Vector3f d)
        {
            return new pstd.span<Float>(new Float[] { std.max<Float>(0.01f, pbrt.AbsDot(ns, a)), std.max<Float>(0.01f, pbrt.AbsDot(ns, b)),
                                                      std.max<Float>(0.01f, pbrt.AbsDot(ns, c)), std.max<Float>(0.01f, pbrt.AbsDot(ns, d)) });
        }

        // The weights the spherical triangle sampling of Triangle uses for the cosine at the reference point (including the order of its corners)
        static pstd.span<Float> TriangleCosineWeights(Normal3f ns, Point3f reference, Point3f a, Point3f b, Point3f c)
        {
            Vector3f wi0 = pbrt.Normalize(a - reference), wi1 = pbrt.Normalize(b - reference), wi2 = pbrt.Normalize(c - reference);
            return new pstd.span<Float>(new Float[] { std.max<Float>(0.01f, pbrt.AbsDot(ns, wi1)), std.max<Float>(0.01f, pbrt.AbsDot(ns, wi1)),
                                                      std.max<Float>(0.01f, pbrt.AbsDot(ns, wi0)), std.max<Float>(0.01f, pbrt.AbsDot(ns, wi2)) });
        }

        // The parts of the quad above and below the horizon of the reference point (the plane through it with the shading normal), as polygons for the sampling
        // by projected solid angle, and their projected solid angles. False if the quad is not sampled this way from the reference point.
        bool PrepareProjected(QuadMesh mesh, ShapeSampleContext ctx, out Quad.Basis basis, out ProjectedSolidAnglePolygon upper,
                              out ProjectedSolidAnglePolygon lower, out double upperArea, out double lowerArea)
        {
            basis = default;
            upper = null;
            lower = null;
            upperArea = 0;
            lowerArea = 0;
            if (!mesh.projectedSampling || ctx.ns == new Normal3f(0, 0, 0))
            {
                return false;
            }
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            if (BilinearPatch.ReferencePointOnPlane(ctx, q0, q1, q3))
            {
                return false;
            }
            if (cornerScratch == null)
            {
                cornerScratch = new Point3f[4];
            }
            cornerScratch[0] = q0;
            cornerScratch[1] = q1;
            cornerScratch[2] = q2;
            cornerScratch[3] = q3;
            return PolygonSampling.PrepareProjected(ctx, cornerScratch, 4, out basis, out upper, out lower, out upperArea, out lowerArea);
        }

        // The sample of the direction w from the reference point: the point of the quad in that direction, with the density in solid angle
        pstd.optional<ShapeSample> SampleProjected(QuadMesh mesh, ShapeSampleContext ctx, Point2f u, Quad.Basis basis, ProjectedSolidAnglePolygon upper,
                                                   ProjectedSolidAnglePolygon lower, double upperArea, double lowerArea)
        {
            // The point of the plane of the quad in that direction, and its coordinates (alpha, beta) in the frame (q0; q1 - q0, q3 - q0)
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            double e1x = (double)q1.x - q0.x, e1y = (double)q1.y - q0.y, e1z = (double)q1.z - q0.z;
            double e3x = (double)q3.x - q0.x, e3y = (double)q3.y - q0.y, e3z = (double)q3.z - q0.z;
            double nx = e1y * e3z - e1z * e3y, ny = e1z * e3x - e1x * e3z, nz = e1x * e3y - e1y * e3x;
            if (!PolygonSampling.SampleProjected(ctx, u, basis, upper, lower, upperArea, lowerArea, q0, nx, ny, nz, out double px, out double py, out double pz,
                                                 out Float pdf))
            {
                return default;
            }
            double dx = px - q0.x, dy = py - q0.y, dz = pz - q0.z;
            double nn = nx * nx + ny * ny + nz * nz;
            double alpha = ((dy * e3z - dz * e3y) * nx + (dz * e3x - dx * e3z) * ny + (dx * e3y - dy * e3x) * nz) / nn;
            double beta = ((e1y * dz - e1z * dy) * nx + (e1z * dx - e1x * dz) * ny + (e1x * dy - e1y * dx) * nz) / nn;
            Quad.ComputeV11Coordinates(q0, q1, q2, q3, out double a11, out double b11);
            Quad.SolveBilinear(a11, b11, alpha, beta, out double u1, out double v1);
            Point2f uv = Quad.ToGivenFrame(mesh.rotation[quadIndex], u1, v1);
            Point3f point = new Point3f((Float)px, (Float)py, (Float)pz);
            Vector3f pError = pbrt.gamma(6) * (Vector3f)(pbrt.Abs(q0) + pbrt.Abs(q1) + pbrt.Abs(q2) + pbrt.Abs(q3));
            return SampleAt(mesh, point, pError, uv, ctx.time, true, pdf);
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            QuadMesh mesh = GetMesh();
            if (PrepareProjected(mesh, ctx, out Quad.Basis projectedBasis, out ProjectedSolidAnglePolygon upperPart, out ProjectedSolidAnglePolygon lowerPart,
                                 out double upperArea, out double lowerArea))
            {
                return SampleProjected(mesh, ctx, u, projectedBasis, upperPart, lowerPart, upperArea, lowerArea);
            }
            if (!SamplesBySolidAngle(mesh, ctx, out Float omegaA, out Float omegaB))
            {
                // Sample by area and convert the density to solid angle
                return PolygonSampling.AreaSampleToSolidAngle(ctx, Sample(u));
            }

            Point3f reference = ctx.p();
            mesh.FrameCorners(quadIndex, out Point3f q0, out Point3f q1, out Point3f q2, out Point3f q3);
            if (mesh.rectangle[quadIndex])
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
                Point2f given = Quad.ToGivenFrame(mesh.rotation[quadIndex], local[0], local[1]);
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

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            QuadMesh mesh = GetMesh();
            Ray ray = ctx.SpawnRay(wi);
            mesh.FrameCorners(quadIndex, out Point3f p0, out Point3f p1, out Point3f p2, out Point3f p3);
            pstd.optional<QuadIntersection> hit = IntersectQuad(ray, Infinity, p0, p1, p2, p3);
            if (!hit)
            {
                return 0;
            }
            if (PrepareProjected(mesh, ctx, out Quad.Basis projectedBasis, out ProjectedSolidAnglePolygon upperPart, out ProjectedSolidAnglePolygon lowerPart,
                                 out double upperArea, out double lowerArea))
            {
                return PolygonSampling.ProjectedPdf(projectedBasis, wi, upperArea, lowerArea);
            }
            Point3f pHit = PointOfHit(p0, p1, p2, p3, hit.value(), out Vector3f pError);
            if (!SamplesBySolidAngle(mesh, ctx, out Float omegaA, out Float omegaB))
            {
                // The density of area sampling, in solid angle
                Normal3f n = (Normal3f)pbrt.Normalize(pbrt.Cross(p1 - p0, p3 - p0));
                return PolygonSampling.AreaPdfToSolidAngle(ctx, Area(), n, wi, pHit);
            }

            Point3f reference = ctx.p();
            if (mesh.rectangle[quadIndex])
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
                Point3f a = hit.value().second ? p2 : p0, b = hit.value().second ? p3 : p1, c = hit.value().second ? p1 : p3;
                Point2f uTriangle = pbrt.InvertSphericalTriangleSample(new pstd.array3<Point3f>(a, b, c), reference, wi);
                pdf *= pbrt.BilinearPDF(uTriangle, TriangleCosineWeights(ctx.ns, reference, a, b, c));
            }
            return pdf;
        }

        // The Quad2 shapes (and the Triangle shapes of the quads that are not planar and convex) of a mesh of quadrilaterals. indices holds four per quad,
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
                if (Quad.Classify(r[0], r[1], r[2], r[3], out int rotation))
                {
                    goodIndices.AddRange(c);
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
                QuadMesh mesh = new QuadMesh(renderFromObject, reverseOrientation, new pstd.vector<int>(goodIndices), new pstd.vector<Point3f>(P),
                                             new pstd.vector<Normal3f>(N), new pstd.vector<Point2f>(uv),
                                             new pstd.vector<int>(faceIndices.Count != 0 ? goodFaces : new List<int>()), goodRotation, projectedSampling, alloc);
                int meshIndex;
                lock (allMeshesLock)
                {
                    meshIndex = allMeshes.Count;
                    allMeshes.Add(mesh);
                }
                for (int i = 0; i < goodRotation.Count; ++i)
                {
                    shapes.push_back(new Quad2(meshIndex, i));
                }
            }
            return shapes;
        }

        int meshIndex, quadIndex;
    }
}  // namespace pbrt
