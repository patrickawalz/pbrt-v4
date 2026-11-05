// (not in pbrt-v4) shapes_patches.cs
//
// The patch primitives of the RenderMan Interface: RiPatch and RiPatchMesh, bilinear and bicubic, periodic or not, with the bases bezier, b-spline,
// catmull-rom, hermite and power and any ustep/vstep. The surface is traced directly, not diced. A bilinear patch is a BilinearPatch of pbrt-v4
// (analytic intersection and sampling); a bicubic patch is converted exactly to a bicubic Bezier patch (the change of basis is linear) and
// intersected by Bezier clipping (bezierclip.cs) like the other bicubic patches (bezierpatchshape.cs), which also gives its area, its sampling and its
// bounds. The parameterization is that of RenderMan: (u, v) in [0, 1]^2 runs over the whole mesh, the patches of a mesh take consecutive intervals.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static class PatchBases
    {
        // The basis matrices of RenderMan: P(u) = [u^3 u^2 u 1] M [g0 g1 g2 g3]^T
        public static bool TryGet(string name, out double[,] matrix)
        {
            switch (name)
            {
                case "bezier":
                    matrix = new double[,] { { -1, 3, -3, 1 }, { 3, -6, 3, 0 }, { -3, 3, 0, 0 }, { 1, 0, 0, 0 } };
                    return true;
                case "b-spline":
                    matrix = Scale(new double[,] { { -1, 3, -3, 1 }, { 3, -6, 3, 0 }, { -3, 0, 3, 0 }, { 1, 4, 1, 0 } }, 1.0 / 6.0);
                    return true;
                case "catmull-rom":
                    matrix = Scale(new double[,] { { -1, 3, -3, 1 }, { 2, -5, 4, -1 }, { -1, 0, 1, 0 }, { 0, 2, 0, 0 } }, 0.5);
                    return true;
                case "hermite":
                    matrix = new double[,] { { 2, 1, -2, 1 }, { -3, -2, 3, -1 }, { 0, 1, 0, 0 }, { 1, 0, 0, 0 } };
                    return true;
                case "power":
                    matrix = new double[,] { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } };
                    return true;
            }
            matrix = null;
            return false;
        }

        // The default step of a basis (RiBasis): 3 for bezier and hermite and power, 1 for b-spline and catmull-rom
        public static int DefaultStep(string name)
        {
            return name == "b-spline" || name == "catmull-rom" ? 1 : (name == "hermite" ? 2 : 3);
        }

        private static double[,] Scale(double[,] m, double s)
        {
            double[,] result = new double[4, 4];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    result[i, j] = m[i, j] * s;
                }
            }
            return result;
        }

        public static double[,] Inverse(double[,] m)
        {
            double[,] a = new double[4, 8];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    a[i, j] = m[i, j];
                }
                a[i, 4 + i] = 1;
            }
            for (int c = 0; c < 4; ++c)
            {
                int pivot = c;
                for (int r = c + 1; r < 4; ++r)
                {
                    if (Math.Abs(a[r, c]) > Math.Abs(a[pivot, c]))
                    {
                        pivot = r;
                    }
                }
                for (int j = 0; j < 8; ++j)
                {
                    double tmp = a[c, j];
                    a[c, j] = a[pivot, j];
                    a[pivot, j] = tmp;
                }
                double d = a[c, c];
                for (int j = 0; j < 8; ++j)
                {
                    a[c, j] /= d;
                }
                for (int r = 0; r < 4; ++r)
                {
                    if (r != c)
                    {
                        double f = a[r, c];
                        for (int j = 0; j < 8; ++j)
                        {
                            a[r, j] -= f * a[c, j];
                        }
                    }
                }
            }
            double[,] result = new double[4, 4];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    result[i, j] = a[i, 4 + j];
                }
            }
            return result;
        }

        public static double[,] Multiply(double[,] a, double[,] b)
        {
            double[,] result = new double[4, 4];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    for (int k = 0; k < 4; ++k)
                    {
                        result[i, j] += a[i, k] * b[k, j];
                    }
                }
            }
            return result;
        }

        // The Bezier control net with the same surface as the control net g (g[i, j]: i along u, j along v) in the bases of u and v
        public static BezierPatch4 ToBezier(SdfVector[,] g, double[,] uBasis, double[,] vBasis)
        {
            double[,] bezier;
            TryGet("bezier", out bezier);
            double[,] inverse = Inverse(bezier);
            double[,] t = Multiply(inverse, uBasis);
            double[,] s = Multiply(inverse, vBasis);
            BezierPatch4 patch = new BezierPatch4();
            for (int k = 0; k < 4; ++k)
            {
                for (int l = 0; l < 4; ++l)
                {
                    SdfVector sum = new SdfVector(0, 0, 0);
                    for (int i = 0; i < 4; ++i)
                    {
                        for (int j = 0; j < 4; ++j)
                        {
                            sum += (t[k, i] * s[l, j]) * g[i, j];
                        }
                    }
                    patch.Set(k, l, sum);
                }
            }
            return patch;
        }

        // The surface of the control net evaluated directly from the basis formulas (the reference of the tests)
        public static SdfVector Evaluate(SdfVector[,] g, double[,] uBasis, double[,] vBasis, double u, double v)
        {
            double[] ub = new double[4], vb = new double[4];
            for (int i = 0; i < 4; ++i)
            {
                for (int m = 0; m < 4; ++m)
                {
                    ub[i] += Math.Pow(u, 3 - m) * uBasis[m, i];
                    vb[i] += Math.Pow(v, 3 - m) * vBasis[m, i];
                }
            }
            SdfVector sum = new SdfVector(0, 0, 0);
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    sum += (ub[i] * vb[j]) * g[i, j];
                }
            }
            return sum;
        }
    }

    public static unsafe class Patches
    {
        // The shapes of a patch or a patch mesh. nu, nv: the grid of control points (P[v * nu + u]); varyingUv: the texture coordinates at the corners of
        // the patches, (nuc * nvc) pairs, or null for the parameterization of RenderMan
        public static pstd.vector<Shape> CreateMesh(string name, Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                                     ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            pstd.vector<Shape> none = new pstd.vector<Shape>();
            List<Point3f> points = parameters.GetPoint3fArray("P");
            string type = parameters.GetOneString("type", "bilinear");
            bool bicubic = type == "bicubic";
            if (type != "bilinear" && !bicubic)
            {
                pbrt.Error(loc, name + ": \"type\" must be \"bilinear\" or \"bicubic\".");
                return none;
            }
            int nu, nv;
            if (name == "patch")
            {
                nu = nv = bicubic ? 4 : 2;
            }
            else
            {
                nu = parameters.GetOneInt("nu", 0);
                nv = parameters.GetOneInt("nv", 0);
            }
            if (nu < 2 || nv < 2 || points.Count != nu * nv)
            {
                pbrt.Error(loc, name + ": \"P\" must hold nu * nv = " + nu * nv + " points (" + points.Count + " given).");
                return none;
            }
            bool uPeriodic = parameters.GetOneString("uwrap", "nonperiodic") == "periodic";
            bool vPeriodic = parameters.GetOneString("vwrap", "nonperiodic") == "periodic";
            string uBasisName = parameters.GetOneString("ubasis", parameters.GetOneString("basis", "bezier"));
            string vBasisName = parameters.GetOneString("vbasis", parameters.GetOneString("basis", "bezier"));
            double[,] uBasis, vBasis;
            if (!PatchBases.TryGet(uBasisName, out uBasis) || !PatchBases.TryGet(vBasisName, out vBasis))
            {
                pbrt.Error(loc, name + ": unknown basis \"" + uBasisName + "\" or \"" + vBasisName + "\".");
                return none;
            }
            int uStep = bicubic ? parameters.GetOneInt("ustep", PatchBases.DefaultStep(uBasisName)) : 1;
            int vStep = bicubic ? parameters.GetOneInt("vstep", PatchBases.DefaultStep(vBasisName)) : 1;
            if (uStep < 1 || vStep < 1)
            {
                pbrt.Error(loc, name + ": ustep and vstep must be positive.");
                return none;
            }
            // the number of patches along each direction (RenderMan interface specification, RiPatchMesh)
            int nuPatches, nvPatches;
            if (bicubic)
            {
                nuPatches = uPeriodic ? nu / uStep : (nu - 4) / uStep + 1;
                nvPatches = vPeriodic ? nv / vStep : (nv - 4) / vStep + 1;
                if (!uPeriodic && nu < 4 || !vPeriodic && nv < 4)
                {
                    pbrt.Error(loc, name + ": a bicubic nonperiodic mesh needs at least 4 control points along u and v.");
                    return none;
                }
            }
            else
            {
                nuPatches = uPeriodic ? nu : nu - 1;
                nvPatches = vPeriodic ? nv : nv - 1;
            }
            if (nuPatches < 1 || nvPatches < 1)
            {
                pbrt.Error(loc, name + ": no patch.");
                return none;
            }
            if (parameters.GetFloatArray("Pw").Count != 0)
            {
                pbrt.Error(loc, name + ": rational \"Pw\" patches are not supported (use the nupatch shape).");
                return none;
            }
            // varying texture coordinates: the corners of the patches
            int nuc = uPeriodic ? nuPatches : nuPatches + 1, nvc = vPeriodic ? nvPatches : nvPatches + 1;
            List<Point2f> uvs = parameters.GetPoint2fArray("uv");
            if (uvs.Count == 0)
            {
                uvs = parameters.GetPoint2fArray("st");
            }
            if (uvs.Count != 0 && uvs.Count != nuc * nvc)
            {
                pbrt.Error(loc, name + ": \"uv\" must hold one pair per patch corner (" + nuc * nvc + "). Discarding it.");
                uvs = new List<Point2f>();
            }

            Bounds3f box = new Bounds3f(points[0]);
            foreach (Point3f p in points)
            {
                box = pbrt.Union(box, p);
            }
            double diagonal = pbrt.Length(box.pMax - box.pMin);

            if (!bicubic)
            {
                return CreateBilinear(renderFromObject, reverseOrientation, points, nu, nv, nuPatches, nvPatches, uPeriodic, vPeriodic, nuc, nvc, uvs, alloc);
            }

            BezierSurfaceSettings settings = new BezierSurfaceSettings();
            settings.renderFromObject = renderFromObject;
            settings.objectFromRender = objectFromRender;
            settings.reverseOrientation = reverseOrientation;
            settings.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            settings.epsilon = parameters.GetOneFloat("epsilon", 1e-5f) * diagonal;
            settings.pixelSpread = parameters.GetOneFloat("pixelspread", 0);
            settings.clip.maxLevel = parameters.GetOneInt("maxlevel", 10);
            settings.autoMaxLevel = parameters.GetOneInt("maxlevel", -1) < 0;
            settings.clip.uvMargin = parameters.GetOneFloat("uvmargin", 1);
            settings.clip.useBezierClip = parameters.GetOneBool("bezierclip", true);
            settings.clip.conservative = parameters.GetOneBool("conservative", false);

            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            for (int pv = 0; pv < nvPatches; ++pv)
            {
                for (int pu = 0; pu < nuPatches; ++pu)
                {
                    SdfVector[,] g = new SdfVector[4, 4];
                    for (int j = 0; j < 4; ++j)
                    {
                        for (int i = 0; i < 4; ++i)
                        {
                            int iu = pu * uStep + i, iv = pv * vStep + j;
                            if (uPeriodic)
                            {
                                iu %= nu;
                            }
                            if (vPeriodic)
                            {
                                iv %= nv;
                            }
                            Point3f p = points[iv * nu + iu];
                            g[i, j] = new SdfVector(p.x, p.y, p.z);
                        }
                    }
                    BezierPatch4 bezier = PatchBases.ToBezier(g, uBasis, vBasis);
                    double[] cornerUv = CornerTexture(pu, pv, nuPatches, nvPatches, nuc, nvc, uPeriodic, vPeriodic, uvs);
                    PatchSurface surface = new FittedPatchSurface(bezier, new PatchTextureMap(cornerUv), null, pv * nuPatches + pu);
                    shapes.push_back(new BezierClipPatchShape(settings, bezier, surface, false));
                }
            }
            return shapes;
        }

        // The texture coordinates at (p00, p10, p01, p11) of the patch (pu, pv)
        private static double[] CornerTexture(int pu, int pv, int nuPatches, int nvPatches, int nuc, int nvc, bool uPeriodic, bool vPeriodic,
                                              List<Point2f> uvs)
        {
            double[] result = new double[8];
            for (int c = 0; c < 4; ++c)
            {
                int du = c & 1, dv = c >> 1;
                if (uvs.Count == 0)
                {
                    result[2 * c] = (pu + du) / (double)nuPatches;
                    result[2 * c + 1] = (pv + dv) / (double)nvPatches;
                }
                else
                {
                    int iu = pu + du, iv = pv + dv;
                    if (uPeriodic)
                    {
                        iu %= nuc;
                    }
                    if (vPeriodic)
                    {
                        iv %= nvc;
                    }
                    result[2 * c] = uvs[iv * nuc + iu].x;
                    result[2 * c + 1] = uvs[iv * nuc + iu].y;
                }
            }
            return result;
        }

        private static pstd.vector<Shape> CreateBilinear(Transform renderFromObject, bool reverseOrientation, List<Point3f> points, int nu, int nv,
                                                         int nuPatches, int nvPatches, bool uPeriodic, bool vPeriodic, int nuc, int nvc,
                                                         List<Point2f> uvs, Allocator alloc)
        {
            // four vertices per patch: the texture coordinates of the patches of a periodic mesh differ at the seam
            List<Point3f> vertices = new List<Point3f>();
            List<Point2f> vertexUv = new List<Point2f>();
            List<int> indices = new List<int>();
            for (int pv = 0; pv < nvPatches; ++pv)
            {
                for (int pu = 0; pu < nuPatches; ++pu)
                {
                    double[] corner = CornerTexture(pu, pv, nuPatches, nvPatches, nuc, nvc, uPeriodic, vPeriodic, uvs);
                    for (int c = 0; c < 4; ++c)
                    {
                        int iu = (pu + (c & 1)) % nu, iv = (pv + (c >> 1)) % nv;
                        indices.Add(vertices.Count);
                        vertices.Add(points[iv * nu + iu]);
                        vertexUv.Add(new Point2f((Float)corner[2 * c], (Float)corner[2 * c + 1]));
                    }
                }
            }
            BilinearPatchMesh mesh = new BilinearPatchMesh(renderFromObject, reverseOrientation, new pstd.vector<int>(indices),
                                                           new pstd.vector<Point3f>(vertices), new pstd.vector<Normal3f>(),
                                                           new pstd.vector<Point2f>(vertexUv), new pstd.vector<int>(), null, alloc);
            return BilinearPatch.CreatePatches(mesh, alloc);
        }
    }
}  // namespace pbrt
