// (not in pbrt-v4) curvedpatches.cs
//
// Curved triangles and quads ray traced directly with Bezier clipping (Tejima et al. 2015):
//   - PN triangles (Vlachos et al. 2001), cubic Bezier triangles from the vertex positions and normals
//   - Phong tessellation triangles and quads (Boubekeur and Alexa 2008)
//   - Gregory quads (20 control points) and Gregory triangles (18 control points) with the OpenSubdiv bases
// A triangle is split into three quads (vertex, edge midpoint, centroid, edge midpoint) with a bilinear map of the barycentric coordinates, which turns
// a polynomial of degree d of the barycentric coordinates into a patch of bidegree (d, d). The PN triangles (d = 3) and the Phong triangles (d = 2)
// are therefore represented exactly by bicubic Bezier patches, which are found by interpolation of the surface on a 4 x 4 grid. The Gregory patches
// are rational: their Bezier patches (the averaged inner control points) bound the surface for the intersection and the exact basis is evaluated at the
// hit.

using System;
using System.Collections.Generic;
using OpenSubdiv.Far;
using OpenSubdiv.Far.Internal;

namespace pbrt
{
    // The interpolation of a function on the 4 x 4 grid at u, v = 0, 1/3, 2/3, 1 by a bicubic Bezier patch
    public static class BezierFit
    {
        public static BezierPatch4 FromFunction(Func<double, double, SdfVector> function)
        {
            SdfVector[,] samples = new SdfVector[4, 4];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    samples[i, j] = function(i / 3.0, j / 3.0);
                }
            }
            SdfVector[,] rows = new SdfVector[4, 4];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    SdfVector sum = new SdfVector(0, 0, 0);
                    for (int k = 0; k < 4; ++k)
                    {
                        sum += inverse[j, k] * samples[i, k];
                    }
                    rows[i, j] = sum;
                }
            }
            BezierPatch4 patch = default;
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    SdfVector sum = new SdfVector(0, 0, 0);
                    for (int k = 0; k < 4; ++k)
                    {
                        sum += inverse[i, k] * rows[k, j];
                    }
                    patch.Set(i, j, sum);
                }
            }
            return patch;
        }

        private static double[,] BuildInverse()
        {
            double[,] matrix = new double[4, 8];
            for (int r = 0; r < 4; ++r)
            {
                double t = r / 3.0;
                double s = 1 - t;
                matrix[r, 0] = s * s * s;
                matrix[r, 1] = 3 * s * s * t;
                matrix[r, 2] = 3 * s * t * t;
                matrix[r, 3] = t * t * t;
                matrix[r, 4 + r] = 1;
            }
            for (int c = 0; c < 4; ++c)
            {
                int pivot = c;
                for (int r = c + 1; r < 4; ++r)
                {
                    if (Math.Abs(matrix[r, c]) > Math.Abs(matrix[pivot, c]))
                    {
                        pivot = r;
                    }
                }
                for (int k = 0; k < 8; ++k)
                {
                    double swap = matrix[c, k];
                    matrix[c, k] = matrix[pivot, k];
                    matrix[pivot, k] = swap;
                }
                double scale = 1 / matrix[c, c];
                for (int k = 0; k < 8; ++k)
                {
                    matrix[c, k] *= scale;
                }
                for (int r = 0; r < 4; ++r)
                {
                    if (r == c)
                    {
                        continue;
                    }
                    double factor = matrix[r, c];
                    for (int k = 0; k < 8; ++k)
                    {
                        matrix[r, k] -= factor * matrix[c, k];
                    }
                }
            }
            double[,] result = new double[4, 4];
            for (int r = 0; r < 4; ++r)
            {
                for (int c = 0; c < 4; ++c)
                {
                    result[r, c] = matrix[r, 4 + c];
                }
            }
            return result;
        }

        private static readonly double[,] inverse = BuildInverse();
    }

    // The bilinear map from the (u, v) of one of the three quads of a triangle to the barycentric coordinates of the triangle
    public sealed class TriangleQuadMap
    {
        // quad: 0, 1 or 2, the vertex of the triangle at (u, v) = (0, 0)
        public TriangleQuadMap(int quad)
        {
            int next = (quad + 1) % 3;
            int previous = (quad + 2) % 3;
            corner[0, quad] = 1;
            corner[1, quad] = 0.5;
            corner[1, next] = 0.5;
            corner[2, quad] = 0.5;
            corner[2, previous] = 0.5;
            for (int k = 0; k < 3; ++k)
            {
                corner[3, k] = 1.0 / 3.0;
            }
        }

        // s and t are the barycentric coordinates of the second and the third vertex
        public void Evaluate(double u, double v, out double s, out double t)
        {
            s = Mix(1, u, v);
            t = Mix(2, u, v);
        }

        public void Derivatives(double u, double v, out double dsdu, out double dsdv, out double dtdu, out double dtdv)
        {
            dsdu = (1 - v) * (corner[1, 1] - corner[0, 1]) + v * (corner[3, 1] - corner[2, 1]);
            dsdv = (1 - u) * (corner[2, 1] - corner[0, 1]) + u * (corner[3, 1] - corner[1, 1]);
            dtdu = (1 - v) * (corner[1, 2] - corner[0, 2]) + v * (corner[3, 2] - corner[2, 2]);
            dtdv = (1 - u) * (corner[2, 2] - corner[0, 2]) + u * (corner[3, 2] - corner[1, 2]);
        }

        // The barycentric coordinates at the corner c (0: (0, 0), 1: (1, 0), 2: (0, 1), 3: (1, 1))
        public double Corner(int c, int k)
        {
            return corner[c, k];
        }

        private double Mix(int k, double u, double v)
        {
            return (1 - u) * (1 - v) * corner[0, k] + u * (1 - v) * corner[1, k] + (1 - u) * v * corner[2, k] + u * v * corner[3, k];
        }

        private readonly double[,] corner = new double[4, 3];
    }

    // The texture coordinates of a patch: bilinear in (u, v) with the values at the corners (0, 0), (1, 0), (0, 1), (1, 1)
    public sealed class PatchTextureMap
    {
        public PatchTextureMap(double[] cornerUv)
        {
            this.cornerUv = cornerUv;
        }

        public static PatchTextureMap Default()
        {
            return new PatchTextureMap(new double[] { 0, 0, 1, 0, 0, 1, 1, 1 });
        }

        // The texture coordinates of a triangle quad from the coordinates of the three vertices
        public static PatchTextureMap FromTriangle(TriangleQuadMap map, double[] vertexUv)
        {
            double[] cornerUv = new double[8];
            for (int c = 0; c < 4; ++c)
            {
                for (int k = 0; k < 3; ++k)
                {
                    cornerUv[2 * c] += map.Corner(c, k) * vertexUv[2 * k];
                    cornerUv[2 * c + 1] += map.Corner(c, k) * vertexUv[2 * k + 1];
                }
            }
            return new PatchTextureMap(cornerUv);
        }

        // The texture coordinates and the derivatives of the position with respect to them, from the derivatives with respect to (u, v)
        public void Convert(double u, double v, SdfVector dpdu, SdfVector dpdv, out Point2f uv, out SdfVector dpdTextureU, out SdfVector dpdTextureV)
        {
            double tu = Mix(0, u, v);
            double tv = Mix(1, u, v);
            uv = new Point2f((Float)tu, (Float)tv);
            double a = (1 - v) * (cornerUv[2] - cornerUv[0]) + v * (cornerUv[6] - cornerUv[4]);
            double c = (1 - v) * (cornerUv[3] - cornerUv[1]) + v * (cornerUv[7] - cornerUv[5]);
            double b = (1 - u) * (cornerUv[4] - cornerUv[0]) + u * (cornerUv[6] - cornerUv[2]);
            double d = (1 - u) * (cornerUv[5] - cornerUv[1]) + u * (cornerUv[7] - cornerUv[3]);
            double determinant = a * d - b * c;
            if (Math.Abs(determinant) < 1e-12)
            {
                dpdTextureU = dpdu;
                dpdTextureV = dpdv;
                return;
            }
            dpdTextureU = (d * dpdu - c * dpdv) / determinant;
            dpdTextureV = (a * dpdv - b * dpdu) / determinant;
        }

        private double Mix(int k, double u, double v)
        {
            return (1 - u) * (1 - v) * cornerUv[k] + u * (1 - v) * cornerUv[2 + k] + (1 - u) * v * cornerUv[4 + k] + u * v * cornerUv[6 + k];
        }

        private readonly double[] cornerUv;
    }

    // The shading normal of a patch
    public abstract class PatchNormalField
    {
        public abstract SdfVector Evaluate(double u, double v);
    }

    // The normalized bilinear interpolation of the normals at the corners (0, 0), (1, 0), (0, 1), (1, 1)
    public sealed class BilinearNormalField : PatchNormalField
    {
        public BilinearNormalField(SdfVector n00, SdfVector n10, SdfVector n01, SdfVector n11)
        {
            this.n00 = n00;
            this.n10 = n10;
            this.n01 = n01;
            this.n11 = n11;
        }

        public override SdfVector Evaluate(double u, double v)
        {
            SdfVector n = (1 - u) * (1 - v) * n00 + u * (1 - v) * n10 + (1 - u) * v * n01 + u * v * n11;
            return n.LengthSquared() > 0 ? n.Normalized() : n;
        }

        private readonly SdfVector n00, n10, n01, n11;
    }

    // The shading normal of a curved triangle at the barycentric coordinates of a quad
    public sealed class TriangleNormalField : PatchNormalField
    {
        public TriangleNormalField(CurvedTriangle triangle, TriangleQuadMap map)
        {
            this.triangle = triangle;
            this.map = map;
        }

        public override SdfVector Evaluate(double u, double v)
        {
            map.Evaluate(u, v, out double s, out double t);
            return triangle.Normal(s, t);
        }

        private readonly CurvedTriangle triangle;
        private readonly TriangleQuadMap map;
    }

    // A surface over the barycentric coordinates (1 - s - t, s, t) of a triangle that is a polynomial of s and t
    public abstract class CurvedTriangle
    {
        public abstract SdfVector Position(double s, double t);

        // The unit shading normal
        public abstract SdfVector Normal(double s, double t);
    }

    // The cubic Bezier triangle of a PN triangle
    public sealed class PnTriangle : CurvedTriangle
    {
        public PnTriangle(SdfVector[] p, SdfVector[] n)
        {
            this.n = n;
            // exponents of the barycentric coordinates (b0, b1, b2) of the ten control points
            control[0] = p[0];
            control[1] = p[1];
            control[2] = p[2];
            SdfVector near01 = EdgePoint(p[0], p[1], n[0]);
            SdfVector near10 = EdgePoint(p[1], p[0], n[1]);
            SdfVector near12 = EdgePoint(p[1], p[2], n[1]);
            SdfVector near21 = EdgePoint(p[2], p[1], n[2]);
            SdfVector near20 = EdgePoint(p[2], p[0], n[2]);
            SdfVector near02 = EdgePoint(p[0], p[2], n[0]);
            control[3] = near01;
            control[4] = near10;
            control[5] = near12;
            control[6] = near21;
            control[7] = near20;
            control[8] = near02;
            SdfVector e = (near01 + near10 + near12 + near21 + near20 + near02) * (1.0 / 6.0);
            SdfVector v = (p[0] + p[1] + p[2]) * (1.0 / 3.0);
            control[9] = e + (e - v) * 0.5;
            normal01 = MidNormal(p[0], p[1], n[0], n[1]);
            normal12 = MidNormal(p[1], p[2], n[1], n[2]);
            normal20 = MidNormal(p[2], p[0], n[2], n[0]);
        }

        public override SdfVector Position(double s, double t)
        {
            double b0 = 1 - s - t, b1 = s, b2 = t;
            double b00 = b0 * b0, b11 = b1 * b1, b22 = b2 * b2;
            return b00 * b0 * control[0] + b11 * b1 * control[1] + b22 * b2 * control[2]
                 + 3 * b00 * b1 * control[3] + 3 * b0 * b11 * control[4]
                 + 3 * b11 * b2 * control[5] + 3 * b1 * b22 * control[6]
                 + 3 * b22 * b0 * control[7] + 3 * b2 * b00 * control[8]
                 + 6 * b0 * b1 * b2 * control[9];
        }

        public override SdfVector Normal(double s, double t)
        {
            double b0 = 1 - s - t, b1 = s, b2 = t;
            SdfVector result = b0 * b0 * n[0] + b1 * b1 * n[1] + b2 * b2 * n[2] + b0 * b1 * normal01 + b1 * b2 * normal12 + b2 * b0 * normal20;
            return result.LengthSquared() > 0 ? result.Normalized() : result;
        }

        private static SdfVector EdgePoint(SdfVector from, SdfVector to, SdfVector normal)
        {
            double w = SdfVector.Dot(to - from, normal);
            return (2 * from + to - w * normal) * (1.0 / 3.0);
        }

        private static SdfVector MidNormal(SdfVector pi, SdfVector pj, SdfVector ni, SdfVector nj)
        {
            SdfVector edge = pj - pi;
            double length2 = edge.LengthSquared();
            double v = length2 > 0 ? 2 * SdfVector.Dot(edge, ni + nj) / length2 : 0;
            SdfVector h = ni + nj - v * edge;
            return h.LengthSquared() > 0 ? h.Normalized() : ni;
        }

        private readonly SdfVector[] control = new SdfVector[10];
        private readonly SdfVector[] n;
        private readonly SdfVector normal01, normal12, normal20;
    }

    // The Phong tessellation of a triangle: the linear position blended with its projections on the tangent planes of the vertices
    public sealed class PhongTriangle : CurvedTriangle
    {
        public PhongTriangle(SdfVector[] p, SdfVector[] n, double alpha)
        {
            this.p = p;
            this.n = n;
            this.alpha = alpha;
        }

        public override SdfVector Position(double s, double t)
        {
            double[] b = { 1 - s - t, s, t };
            SdfVector q = b[0] * p[0] + b[1] * p[1] + b[2] * p[2];
            SdfVector sum = new SdfVector(0, 0, 0);
            for (int i = 0; i < 3; ++i)
            {
                sum += b[i] * SdfVector.Dot(q - p[i], n[i]) * n[i];
            }
            return q - alpha * sum;
        }

        public override SdfVector Normal(double s, double t)
        {
            SdfVector result = (1 - s - t) * n[0] + s * n[1] + t * n[2];
            return result.LengthSquared() > 0 ? result.Normalized() : result;
        }

        private readonly SdfVector[] p;
        private readonly SdfVector[] n;
        private readonly double alpha;
    }

    // The patch surface of a bicubic Bezier patch with exact derivatives
    public sealed class FittedPatchSurface : PatchSurface
    {
        public FittedPatchSurface(BezierPatch4 patch, PatchTextureMap texture, PatchNormalField normals, int faceIndex)
        {
            this.patch = patch;
            this.texture = texture;
            this.normals = normals;
            this.faceIndex = faceIndex;
        }

        public override void Evaluate(double u, double v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector shadingNormal)
        {
            p = patch.Evaluate(u, v);
            SdfVector du = patch.EvaluateDu(u, v);
            SdfVector dv = patch.EvaluateDv(u, v);
            texture.Convert(u, v, du, dv, out uv, out dpdu, out dpdv);
            shadingNormal = normals != null ? normals.Evaluate(u, v) : new SdfVector(0, 0, 0);
        }

        public override int FaceIndex()
        {
            return faceIndex;
        }

        private readonly BezierPatch4 patch;
        private readonly PatchTextureMap texture;
        private readonly PatchNormalField normals;
        private readonly int faceIndex;
    }

    // A Gregory quad: 20 control points, 5 per corner (P, Ep, Em, Fp, Fm), evaluated with the Gregory basis of OpenSubdiv
    public sealed class GregoryQuadSurface : PatchSurface
    {
        public GregoryQuadSurface(SdfVector[] control, PatchTextureMap texture, int faceIndex)
        {
            this.control = control;
            this.texture = texture;
            this.faceIndex = faceIndex;
        }

        public override void Evaluate(double u, double v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector shadingNormal)
        {
            double[] wp = new double[20];
            double[] wu = new double[20];
            double[] wv = new double[20];
            PatchParam param = default;
            PatchBasis.EvaluatePatchBasisNormalized((int)PatchDescriptor.Type.GREGORY_BASIS, param, u, v, wp, wu, wv);
            p = new SdfVector(0, 0, 0);
            SdfVector du = new SdfVector(0, 0, 0);
            SdfVector dv = new SdfVector(0, 0, 0);
            for (int i = 0; i < 20; ++i)
            {
                p += wp[i] * control[i];
                du += wu[i] * control[i];
                dv += wv[i] * control[i];
            }
            texture.Convert(u, v, du, dv, out uv, out dpdu, out dpdv);
            shadingNormal = new SdfVector(0, 0, 0);
        }

        public override int FaceIndex()
        {
            return faceIndex;
        }

        private readonly SdfVector[] control;
        private readonly PatchTextureMap texture;
        private readonly int faceIndex;
    }

    // One of the three quads of a Gregory triangle (18 control points)
    public sealed class GregoryTriangleSurface : PatchSurface
    {
        public GregoryTriangleSurface(SdfVector[] control, TriangleQuadMap map, PatchTextureMap texture, int faceIndex)
        {
            this.control = control;
            this.map = map;
            this.texture = texture;
            this.faceIndex = faceIndex;
        }

        // The Bezier triangle (15 control points) with the averaged inner points that bounds a Gregory triangle
        public static SdfVector[] ToBezierTriangle(SdfVector[] g)
        {
            SdfVector[] b = new SdfVector[15];
            b[0] = g[0];
            b[1] = g[1];
            b[5] = g[2];
            b[6] = (g[3] + g[4]) * 0.5;
            b[4] = g[5];
            b[8] = g[6];
            b[3] = g[7];
            b[7] = (g[8] + g[9]) * 0.5;
            b[14] = g[10];
            b[12] = g[11];
            b[13] = g[12];
            b[10] = (g[13] + g[14]) * 0.5;
            b[2] = g[15];
            b[11] = g[16];
            b[9] = g[17];
            return b;
        }

        public static SdfVector BezierTrianglePosition(SdfVector[] bezier, double s, double t)
        {
            double[] w = new double[15];
            PatchBasis.evalBezierTriDerivWeights(s, t, 0, 0, w);
            SdfVector p = new SdfVector(0, 0, 0);
            for (int i = 0; i < 15; ++i)
            {
                p += w[i] * bezier[i];
            }
            return p;
        }

        public override void Evaluate(double u, double v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector shadingNormal)
        {
            map.Evaluate(u, v, out double s, out double t);
            map.Derivatives(u, v, out double dsdu, out double dsdv, out double dtdu, out double dtdv);
            double[] wp = new double[18];
            double[] ws = new double[18];
            double[] wt = new double[18];
            PatchBasis.EvalBasisGregoryTri(s, t, wp, ws, wt);
            p = new SdfVector(0, 0, 0);
            SdfVector ps = new SdfVector(0, 0, 0);
            SdfVector pt = new SdfVector(0, 0, 0);
            for (int i = 0; i < 18; ++i)
            {
                p += wp[i] * control[i];
                ps += ws[i] * control[i];
                pt += wt[i] * control[i];
            }
            SdfVector du = dsdu * ps + dtdu * pt;
            SdfVector dv = dsdv * ps + dtdv * pt;
            texture.Convert(u, v, du, dv, out uv, out dpdu, out dpdv);
            shadingNormal = new SdfVector(0, 0, 0);
        }

        public override int FaceIndex()
        {
            return faceIndex;
        }

        private readonly SdfVector[] control;
        private readonly TriangleQuadMap map;
        private readonly PatchTextureMap texture;
        private readonly int faceIndex;
    }

    // The shapes of the curved patches
    public static class CurvedPatches
    {
        public static void AddPnTriangle(pstd.vector<Shape> shapes, BezierSurfaceSettings settings, SdfVector[] p, SdfVector[] n, double[] vertexUv,
                                         int faceIndex)
        {
            PnTriangle triangle = new PnTriangle(p, n);
            AddTriangle(shapes, settings, triangle, vertexUv, faceIndex, true);
        }

        public static void AddPhongTriangle(pstd.vector<Shape> shapes, BezierSurfaceSettings settings, SdfVector[] p, SdfVector[] n, double alpha,
                                            double[] vertexUv, int faceIndex)
        {
            PhongTriangle triangle = new PhongTriangle(p, n, alpha);
            AddTriangle(shapes, settings, triangle, vertexUv, faceIndex, true);
        }

        // p, n and uv in the order p00, p10, p01, p11 of a bilinear patch
        public static void AddPhongQuad(pstd.vector<Shape> shapes, BezierSurfaceSettings settings, SdfVector[] p, SdfVector[] n, double alpha,
                                        double[] cornerUv, int faceIndex)
        {
            BezierPatch4 patch = BezierFit.FromFunction((double u, double v) =>
            {
                double[] w = { (1 - u) * (1 - v), u * (1 - v), (1 - u) * v, u * v };
                SdfVector q = w[0] * p[0] + w[1] * p[1] + w[2] * p[2] + w[3] * p[3];
                SdfVector sum = new SdfVector(0, 0, 0);
                for (int i = 0; i < 4; ++i)
                {
                    sum += w[i] * SdfVector.Dot(q - p[i], n[i]) * n[i];
                }
                return q - alpha * sum;
            });
            PatchTextureMap texture = cornerUv != null ? new PatchTextureMap(cornerUv) : PatchTextureMap.Default();
            PatchSurface surface = new FittedPatchSurface(patch, texture, new BilinearNormalField(n[0], n[1], n[2], n[3]), faceIndex);
            shapes.push_back(new BezierClipPatchShape(settings, patch, surface, false));
        }

        public static void AddGregoryQuad(pstd.vector<Shape> shapes, BezierSurfaceSettings settings, SdfVector[] control, double[] cornerUv, int faceIndex)
        {
            BezierPatch4 patch = SubdivisionSurface.GregoryToBezier(control);
            PatchTextureMap texture = cornerUv != null ? new PatchTextureMap(cornerUv) : PatchTextureMap.Default();
            shapes.push_back(new BezierClipPatchShape(settings, patch, new GregoryQuadSurface(control, texture, faceIndex), false));
        }

        public static void AddGregoryTriangle(pstd.vector<Shape> shapes, BezierSurfaceSettings settings, SdfVector[] control, double[] vertexUv,
                                              int faceIndex)
        {
            SdfVector[] bezier = GregoryTriangleSurface.ToBezierTriangle(control);
            double[] uv = vertexUv != null ? vertexUv : new double[] { 0, 0, 1, 0, 1, 1 };
            for (int quad = 0; quad < 3; ++quad)
            {
                TriangleQuadMap map = new TriangleQuadMap(quad);
                BezierPatch4 patch = BezierFit.FromFunction((double u, double v) =>
                {
                    map.Evaluate(u, v, out double s, out double t);
                    return GregoryTriangleSurface.BezierTrianglePosition(bezier, s, t);
                });
                PatchSurface surface = new GregoryTriangleSurface(control, map, PatchTextureMap.FromTriangle(map, uv), faceIndex);
                shapes.push_back(new BezierClipPatchShape(settings, patch, surface, false));
            }
        }

        private static void AddTriangle(pstd.vector<Shape> shapes, BezierSurfaceSettings settings, CurvedTriangle triangle, double[] vertexUv,
                                        int faceIndex, bool shadingNormals)
        {
            double[] uv = vertexUv != null ? vertexUv : new double[] { 0, 0, 1, 0, 1, 1 };
            for (int quad = 0; quad < 3; ++quad)
            {
                TriangleQuadMap map = new TriangleQuadMap(quad);
                BezierPatch4 patch = BezierFit.FromFunction((double u, double v) =>
                {
                    map.Evaluate(u, v, out double s, out double t);
                    return triangle.Position(s, t);
                });
                PatchNormalField normals = shadingNormals ? new TriangleNormalField(triangle, map) : null;
                PatchSurface surface = new FittedPatchSurface(patch, PatchTextureMap.FromTriangle(map, uv), normals, faceIndex);
                shapes.push_back(new BezierClipPatchShape(settings, patch, surface, false));
            }
        }

        // The shapes of a "curvedpatches" shape:
        //   "type": "pn" (triangles), "phong" (triangles and quads) or "gregory" (18 points per triangle, 20 per quad, in "P")
        //   "P", "indices" (not for gregory), "faceVertexCounts" (3 or 4, default all triangles, gregory: default all quads), "N", "uv", "alpha"
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                                ParameterDictionary parameters, FileLoc loc)
        {
            string type = parameters.GetOneString("type", "pn");
            List<Point3f> pointList = parameters.GetPoint3fArray("P");
            List<int> indexList = parameters.GetIntArray("indices");
            List<int> countList = parameters.GetIntArray("faceVertexCounts");
            List<Normal3f> normalList = parameters.GetNormal3fArray("N");
            List<Point2f> uvList = parameters.GetPoint2fArray("uv");
            double alpha = parameters.GetOneFloat("alpha", 0.75f);

            SdfVector[] points = new SdfVector[pointList.Count];
            SdfVector minimum = new SdfVector(double.MaxValue, double.MaxValue, double.MaxValue);
            SdfVector maximum = new SdfVector(-double.MaxValue, -double.MaxValue, -double.MaxValue);
            for (int i = 0; i < points.Length; ++i)
            {
                points[i] = new SdfVector(pointList[i].x, pointList[i].y, pointList[i].z);
                minimum = new SdfVector(Math.Min(minimum.x, points[i].x), Math.Min(minimum.y, points[i].y), Math.Min(minimum.z, points[i].z));
                maximum = new SdfVector(Math.Max(maximum.x, points[i].x), Math.Max(maximum.y, points[i].y), Math.Max(maximum.z, points[i].z));
            }
            double diagonal = points.Length != 0 ? (maximum - minimum).Length() : 1;

            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = renderFromObject;
            settings.objectFromRender = objectFromRender;
            settings.reverseOrientation = reverseOrientation;
            settings.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            settings.epsilon = parameters.GetOneFloat("epsilon", 1e-5f) * diagonal;
            settings.pixelSpread = parameters.GetOneFloat("pixelspread", 0);
            settings.clip.maxLevel = parameters.GetOneInt("maxlevel", 10);
            settings.clip.uvMargin = parameters.GetOneFloat("uvmargin", 1);
            settings.clip.useBezierClip = parameters.GetOneBool("bezierclip", true);
            settings.clip.conservative = parameters.GetOneBool("conservative", false);

            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            if (type == "gregory")
            {
                int offset = 0;
                int face = 0;
                int faceCount = countList.Count != 0 ? countList.Count : points.Length / 20;
                for (; face < faceCount; ++face)
                {
                    int count = countList.Count != 0 ? countList[face] : 4;
                    int size = count == 3 ? 18 : 20;
                    if (count != 3 && count != 4 || offset + size > points.Length)
                    {
                        ErrorExit(loc, "curvedpatches: gregory patches need 18 (triangle) or 20 (quad) points each");
                    }
                    SdfVector[] control = new SdfVector[size];
                    Array.Copy(points, offset, control, 0, size);
                    offset += size;
                    double[] uv = null;
                    if (uvList.Count >= 3 * (face + 1) && count == 3)
                    {
                        uv = new double[6];
                        for (int k = 0; k < 3; ++k)
                        {
                            uv[2 * k] = uvList[3 * face + k].x;
                            uv[2 * k + 1] = uvList[3 * face + k].y;
                        }
                    }
                    if (count == 3)
                    {
                        AddGregoryTriangle(shapes, settings, control, uv, face);
                    }
                    else
                    {
                        AddGregoryQuad(shapes, settings, control, null, face);
                    }
                }
                return shapes;
            }

            if (type != "pn" && type != "phong")
            {
                ErrorExit(loc, "curvedpatches: unknown type \"{0}\"", type);
            }
            int[] indices = indexList.ToArray();
            int[] counts = countList.Count != 0 ? countList.ToArray() : null;
            if (counts == null)
            {
                counts = new int[indices.Length / 3];
                for (int i = 0; i < counts.Length; ++i)
                {
                    counts[i] = 3;
                }
            }
            SdfVector[] normals = new SdfVector[points.Length];
            if (normalList.Count == points.Length)
            {
                for (int i = 0; i < normals.Length; ++i)
                {
                    normals[i] = new SdfVector(normalList[i].x, normalList[i].y, normalList[i].z).Normalized();
                }
            }
            else
            {
                int at = 0;
                for (int f = 0; f < counts.Length; ++f)
                {
                    SdfVector a = points[indices[at]], b = points[indices[at + 1]], c = points[indices[at + counts[f] - 1]];
                    SdfVector faceNormal = SdfVector.Cross(b - a, c - a);
                    for (int k = 0; k < counts[f]; ++k)
                    {
                        normals[indices[at + k]] += faceNormal;
                    }
                    at += counts[f];
                }
                for (int i = 0; i < normals.Length; ++i)
                {
                    if (normals[i].LengthSquared() > 0)
                    {
                        normals[i] = normals[i].Normalized();
                    }
                }
            }
            CurvedMesh mesh = new CurvedMesh();
            mesh.settings = settings;
            mesh.positions = points;
            mesh.normals = normals;
            mesh.indices = indices;
            mesh.alpha = alpha;
            if (uvList.Count == points.Length)
            {
                mesh.uv = new double[2 * points.Length];
                for (int i = 0; i < points.Length; ++i)
                {
                    mesh.uv[2 * i] = uvList[i].x;
                    mesh.uv[2 * i + 1] = uvList[i].y;
                }
            }
            bool useClip = parameters.GetOneBool("phongclip", false);
            int cursor = 0;
            for (int f = 0; f < counts.Length; ++f)
            {
                int count = counts[f];
                if (count == 3)
                {
                    SdfVector[] p = { points[indices[cursor]], points[indices[cursor + 1]], points[indices[cursor + 2]] };
                    SdfVector[] n = { normals[indices[cursor]], normals[indices[cursor + 1]], normals[indices[cursor + 2]] };
                    double[] uv = null;
                    if (uvList.Count == points.Length)
                    {
                        uv = new double[6];
                        for (int k = 0; k < 3; ++k)
                        {
                            uv[2 * k] = uvList[indices[cursor + k]].x;
                            uv[2 * k + 1] = uvList[indices[cursor + k]].y;
                        }
                    }
                    if (type == "pn")
                    {
                        AddPnTriangle(shapes, settings, p, n, uv, f);
                    }
                    else if (useClip)
                    {
                        AddPhongTriangle(shapes, settings, p, n, alpha, uv, f);
                    }
                    else
                    {
                        shapes.push_back(new PhongTriangleShape(mesh, cursor, f));
                    }
                }
                else if (count == 4 && type == "phong")
                {
                    // the vertices of a quad are in the order of a bilinear patch: p00, p10, p01, p11
                    int[] order = { 0, 1, 2, 3 };
                    SdfVector[] p = new SdfVector[4];
                    SdfVector[] n = new SdfVector[4];
                    double[] uv = uvList.Count == points.Length ? new double[8] : null;
                    for (int k = 0; k < 4; ++k)
                    {
                        int vertex = indices[cursor + order[k]];
                        p[k] = points[vertex];
                        n[k] = normals[vertex];
                        if (uv != null)
                        {
                            uv[2 * k] = uvList[vertex].x;
                            uv[2 * k + 1] = uvList[vertex].y;
                        }
                    }
                    AddPhongQuad(shapes, settings, p, n, alpha, uv, f);
                }
                else
                {
                    ErrorExit(loc, "curvedpatches: unsupported face with {0} vertices for type \"{1}\"", count, type);
                }
                cursor += count;
            }
            return shapes;
        }

        private static void ErrorExit(FileLoc loc, string message, params object[] arguments)
        {
            pbrt.ErrorExit(loc, string.Format(message, arguments));
        }
    }
}
