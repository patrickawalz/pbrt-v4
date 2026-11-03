// (not in pbrt-v4) texture_shellmap.cs
//
// Float texture "shellmap": a volumetric texture mapped into the shell over a triangle mesh (shell mapping, Porumbescu et al. 2005),
// with the smooth mapping of Jeschke et al. (2007) and Ogaki (2023) instead of the tetrahedra of the original method. The inner texture
// is a function of the texture space position (u, v, w): u and v come from the uv of the mesh (times the tiling "uscale" and "vscale")
// and w in [0, 1] is the height in the shell, from the base mesh to the offset mesh. The shell is the layer between the mesh and the
// mesh moved by "thickness" along its vertex normals; the bilinear sides and the cubic of the inverse map are those of shellprism.cs.
//
// Kuwana and Fujishiro (2024) integrate the inner volume along the nonlinear ray in texture space, and so have no in-scattering. Here the
// shell is a field of the world position, so a "texture" medium driven by it (delta tracking in world space) scatters, absorbs and emits
// like any other medium; the mapping only decides where the inner volume appears. Regions outside every prism have the value "outside".
//
// Parameters: "texture inner" (the volume in (u, v, w), a float texture that is defined with the identity transform, as it reads (u, v, w)
// where a texture reads the position), the mesh ("point3 P", "integer indices", "normal N", "point2 uv", or "string filename" of a PLY
// file), "float thickness" (0.1), "float uscale", "float vscale" (1), "float outside" (0), "bool tightbounds" (false).
//
// Region bounds: by default the range of the inner texture over everything (rigorous). "tightbounds" true maps the box through the
// shell at a lattice of points and uses the inner bounds over their hull plus 25% (the mapping is smooth, but this is a sample, not a
// proof, so a medium may be slightly biased where the shell folds sharply).

using System;
using System.Collections.Generic;

namespace pbrt
{

    // ShellPrismTree Definition: a bounding volume hierarchy of the prisms of a mesh for point queries
    public sealed class ShellPrismTree
    {
        // ShellPrismTree Public Methods
        public ShellPrismTree(SdfVector[] baseP, SdfVector[] offset, int[] indices)
        {
            this.baseP = baseP;
            this.offset = offset;
            this.indices = indices;
            int count = indices.Length / 3;
            lower = new SdfVector[count];
            upper = new SdfVector[count];
            order = new int[count];
            for (int t = 0; t < count; ++t)
            {
                order[t] = t;
                double lx = double.MaxValue, ly = double.MaxValue, lz = double.MaxValue;
                double ux = double.MinValue, uy = double.MinValue, uz = double.MinValue;
                for (int k = 0; k < 3; ++k)
                {
                    int v = indices[3 * t + k];
                    for (int level = 0; level < 2; ++level)
                    {
                        SdfVector q = baseP[v] + offset[v] * level;
                        lx = Math.Min(lx, q.x);
                        ly = Math.Min(ly, q.y);
                        lz = Math.Min(lz, q.z);
                        ux = Math.Max(ux, q.x);
                        uy = Math.Max(uy, q.y);
                        uz = Math.Max(uz, q.z);
                    }
                }
                lower[t] = new SdfVector(lx, ly, lz);
                upper[t] = new SdfVector(ux, uy, uz);
            }
            nodeLower = new List<SdfVector>();
            nodeUpper = new List<SdfVector>();
            nodeLeft = new List<int>();
            nodeRight = new List<int>();
            nodeFirst = new List<int>();
            nodeCount = new List<int>();
            Build(0, count);
        }

        // Calls visit(triangle) for every triangle whose prism box contains the point; stops when visit returns true
        public bool Query(SdfVector point, double tolerance, Func<int, bool> visit)
        {
            int[] stack = new int[64];
            int sp = 0;
            stack[sp++] = 0;
            while (sp > 0)
            {
                int node = stack[--sp];
                if (!Contains(nodeLower[node], nodeUpper[node], point, tolerance))
                {
                    continue;
                }
                if (nodeLeft[node] < 0)
                {
                    for (int i = 0; i < nodeCount[node]; ++i)
                    {
                        int t = order[nodeFirst[node] + i];
                        if (Contains(lower[t], upper[t], point, tolerance) && visit(t))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    stack[sp++] = nodeLeft[node];
                    stack[sp++] = nodeRight[node];
                }
            }
            return false;
        }

        // Calls visit(triangle) for every triangle whose prism box overlaps the box
        public void QueryBox(SdfVector boxLower, SdfVector boxUpper, Action<int> visit)
        {
            int[] stack = new int[64];
            int sp = 0;
            stack[sp++] = 0;
            while (sp > 0)
            {
                int node = stack[--sp];
                if (!Overlaps(nodeLower[node], nodeUpper[node], boxLower, boxUpper))
                {
                    continue;
                }
                if (nodeLeft[node] < 0)
                {
                    for (int i = 0; i < nodeCount[node]; ++i)
                    {
                        int t = order[nodeFirst[node] + i];
                        if (Overlaps(lower[t], upper[t], boxLower, boxUpper))
                        {
                            visit(t);
                        }
                    }
                }
                else
                {
                    stack[sp++] = nodeLeft[node];
                    stack[sp++] = nodeRight[node];
                }
            }
        }

        // ShellPrismTree Private Methods
        private static bool Contains(SdfVector lo, SdfVector hi, SdfVector p, double tolerance)
        {
            return p.x >= lo.x - tolerance && p.x <= hi.x + tolerance && p.y >= lo.y - tolerance && p.y <= hi.y + tolerance &&
                   p.z >= lo.z - tolerance && p.z <= hi.z + tolerance;
        }

        private static bool Overlaps(SdfVector lo, SdfVector hi, SdfVector otherLo, SdfVector otherHi)
        {
            return lo.x <= otherHi.x && hi.x >= otherLo.x && lo.y <= otherHi.y && hi.y >= otherLo.y && lo.z <= otherHi.z && hi.z >= otherLo.z;
        }

        private int Build(int first, int count)
        {
            int node = nodeLower.Count;
            double lx = double.MaxValue, ly = double.MaxValue, lz = double.MaxValue;
            double ux = double.MinValue, uy = double.MinValue, uz = double.MinValue;
            double cx0 = double.MaxValue, cy0 = double.MaxValue, cz0 = double.MaxValue;
            double cx1 = double.MinValue, cy1 = double.MinValue, cz1 = double.MinValue;
            for (int i = first; i < first + count; ++i)
            {
                int t = order[i];
                lx = Math.Min(lx, lower[t].x);
                ly = Math.Min(ly, lower[t].y);
                lz = Math.Min(lz, lower[t].z);
                ux = Math.Max(ux, upper[t].x);
                uy = Math.Max(uy, upper[t].y);
                uz = Math.Max(uz, upper[t].z);
                double mx = 0.5 * (lower[t].x + upper[t].x), my = 0.5 * (lower[t].y + upper[t].y), mz = 0.5 * (lower[t].z + upper[t].z);
                cx0 = Math.Min(cx0, mx);
                cy0 = Math.Min(cy0, my);
                cz0 = Math.Min(cz0, mz);
                cx1 = Math.Max(cx1, mx);
                cy1 = Math.Max(cy1, my);
                cz1 = Math.Max(cz1, mz);
            }
            nodeLower.Add(new SdfVector(lx, ly, lz));
            nodeUpper.Add(new SdfVector(ux, uy, uz));
            nodeLeft.Add(-1);
            nodeRight.Add(-1);
            nodeFirst.Add(first);
            nodeCount.Add(count);
            if (count <= 4)
            {
                return node;
            }
            int axis = 0;
            double extentX = cx1 - cx0, extentY = cy1 - cy0, extentZ = cz1 - cz0;
            if (extentY > extentX && extentY >= extentZ)
            {
                axis = 1;
            }
            else if (extentZ > extentX && extentZ > extentY)
            {
                axis = 2;
            }
            Array.Sort(order, first, count, Comparer<int>.Create((int a, int b) =>
            {
                double ca = axis == 0 ? lower[a].x + upper[a].x : (axis == 1 ? lower[a].y + upper[a].y : lower[a].z + upper[a].z);
                double cb = axis == 0 ? lower[b].x + upper[b].x : (axis == 1 ? lower[b].y + upper[b].y : lower[b].z + upper[b].z);
                return ca.CompareTo(cb);
            }));
            int half = count / 2;
            int left = Build(first, half);
            int right = Build(first + half, count - half);
            nodeLeft[node] = left;
            nodeRight[node] = right;
            return node;
        }

        // ShellPrismTree Private Members
        private readonly SdfVector[] baseP, offset, lower, upper;
        private readonly int[] indices, order;
        private readonly List<SdfVector> nodeLower, nodeUpper;
        private readonly List<int> nodeLeft, nodeRight, nodeFirst, nodeCount;
    }

    // ShellMapTexture Definition
    public sealed unsafe class ShellMapTexture : FloatTexture
    {
        // ShellMapTexture Public Methods
        public ShellMapTexture(Transform renderFromTexture, FloatTexture inner, int[] indices, Point3f[] p, Normal3f[] n, Point2f[] uv,
                               double thickness, double uScale, double vScale, double outside, bool tightBounds)
        {
            this.renderFromTexture = renderFromTexture;
            this.inner = inner;
            this.indices = indices;
            this.uv = uv;
            this.uScale = uScale;
            this.vScale = vScale;
            this.outside = outside;
            this.tightBounds = tightBounds;
            baseP = new SdfVector[p.Length];
            offset = new SdfVector[p.Length];
            for (int i = 0; i < p.Length; ++i)
            {
                Normal3f unit = pbrt.Normalize(n[i]);
                baseP[i] = new SdfVector(p[i].x, p[i].y, p[i].z);
                offset[i] = new SdfVector(unit.x, unit.y, unit.z) * thickness;
            }
            tree = new ShellPrismTree(baseP, offset, indices);
            tolerance = 1e-9 * Math.Max(1, thickness);
        }

        // The texture space position (u, v, w) of a point of the texture's space, or false outside every prism
        public bool Map(SdfVector point, out double u, out double v, out double w)
        {
            double foundU = 0, foundV = 0, foundW = 0;
            bool found = false;
            double[] heights = new double[3], alphas = new double[3], betas = new double[3];
            ShellPrism prism = new ShellPrism(new SdfVector[3], new SdfVector[3]);
            tree.Query(point, tolerance, (int t) =>
            {
                int v0 = indices[3 * t], v1 = indices[3 * t + 1], v2 = indices[3 * t + 2];
                prism.Set(baseP[v0], baseP[v1], baseP[v2], offset[v0], offset[v1], offset[v2]);
                int count = prism.Inverse(point, -1e-9, 1 + 1e-9, heights, alphas, betas);
                for (int k = 0; k < count; ++k)
                {
                    if (alphas[k] >= -1e-9 && betas[k] >= -1e-9 && alphas[k] + betas[k] <= 1 + 1e-9)
                    {
                        Point2f t0 = uv[v0], t1 = uv[v1], t2 = uv[v2];
                        foundU = (t0.x + alphas[k] * (t1.x - t0.x) + betas[k] * (t2.x - t0.x)) * uScale;
                        foundV = (t0.y + alphas[k] * (t1.y - t0.y) + betas[k] * (t2.y - t0.y)) * vScale;
                        foundW = Math.Min(Math.Max(heights[k], 0), 1);
                        found = true;
                        return true;
                    }
                }
                return false;
            });
            u = foundU;
            v = foundV;
            w = foundW;
            return found;
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f local = renderFromTexture.ApplyInverse(ctx.p);
            if (!Map(new SdfVector(local.x, local.y, local.z), out double u, out double v, out double w))
            {
                return (Float)outside;
            }
            TextureEvalContext mapped = ctx;
            mapped.p = new Point3f((Float)u, (Float)v, (Float)w);
            mapped.uv = new Point2f((Float)u, (Float)v);
            return inner.Evaluate(mapped);
        }

        public override Float MinValue()
        {
            return Math.Min(inner.MinValue(), (Float)outside);
        }

        public override Float MaxValue()
        {
            return Math.Max(inner.MaxValue(), (Float)outside);
        }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            if (!tightBounds)
            {
                lo = MinValue();
                hi = MaxValue();
                return;
            }
            Bounds3f box = new Bounds3f();
            for (int i = 0; i < 8; ++i)
            {
                Point3f corner = renderFromTexture.ApplyInverse(region.Corner(i));
                box = i == 0 ? new Bounds3f(corner) : pbrt.Union(box, corner);
            }
            SdfVector boxLower = new SdfVector(box.pMin.x, box.pMin.y, box.pMin.z), boxUpper = new SdfVector(box.pMax.x, box.pMax.y, box.pMax.z);
            double low = double.MaxValue, high = double.MinValue;
            bool anyOutside = false;
            bool overlapped = false;
            // A lattice of points of the box mapped through the shell (the map is extended analytically outside a prism by the lattice
            // of each prism that overlaps the box)
            tree.QueryBox(boxLower, boxUpper, (int t) =>
            {
                overlapped = true;
                int v0 = indices[3 * t], v1 = indices[3 * t + 1], v2 = indices[3 * t + 2];
                ShellPrism prism = new ShellPrism(new SdfVector[3], new SdfVector[3]);
                prism.Set(baseP[v0], baseP[v1], baseP[v2], offset[v0], offset[v1], offset[v2]);
                double[] heights = new double[3], alphas = new double[3], betas = new double[3];
                double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
                double minW = double.MaxValue, maxW = double.MinValue;
                int mapped = 0;
                for (int i = 0; i < 27; ++i)
                {
                    SdfVector q = new SdfVector(boxLower.x + (boxUpper.x - boxLower.x) * (i % 3) / 2.0,
                                                boxLower.y + (boxUpper.y - boxLower.y) * ((i / 3) % 3) / 2.0,
                                                boxLower.z + (boxUpper.z - boxLower.z) * (i / 9) / 2.0);
                    int count = prism.Inverse(q, -0.5, 1.5, heights, alphas, betas);
                    if (count == 0)
                    {
                        continue;
                    }
                    if (alphas[0] >= 0 && betas[0] >= 0 && alphas[0] + betas[0] <= 1 && heights[0] >= 0 && heights[0] <= 1)
                    {
                        ++mapped;
                    }
                    else
                    {
                        anyOutside = true;
                    }
                    Point2f t0 = uv[v0], t1 = uv[v1], t2 = uv[v2];
                    double u = (t0.x + alphas[0] * (t1.x - t0.x) + betas[0] * (t2.x - t0.x)) * uScale;
                    double v = (t0.y + alphas[0] * (t1.y - t0.y) + betas[0] * (t2.y - t0.y)) * vScale;
                    minU = Math.Min(minU, u);
                    maxU = Math.Max(maxU, u);
                    minV = Math.Min(minV, v);
                    maxV = Math.Max(maxV, v);
                    minW = Math.Min(minW, heights[0]);
                    maxW = Math.Max(maxW, heights[0]);
                }
                if (mapped < 27)
                {
                    anyOutside = true;
                }
                if (minU > maxU)
                {
                    return;
                }
                double padU = 0.25 * (maxU - minU), padV = 0.25 * (maxV - minV), padW = 0.25 * (maxW - minW);
                Bounds3f textureBox = new Bounds3f(
                    new Point3f((Float)(minU - padU), (Float)(minV - padV), (Float)Math.Max(minW - padW, 0)),
                    new Point3f((Float)(maxU + padU), (Float)(maxV + padV), (Float)Math.Min(maxW + padW, 1)));
                inner.RegionBounds(textureBox, out Float innerLow, out Float innerHigh);
                low = Math.Min(low, innerLow);
                high = Math.Max(high, innerHigh);
            });
            if (!overlapped || anyOutside)
            {
                low = Math.Min(low, outside);
                high = Math.Max(high, outside);
            }
            lo = (Float)low;
            hi = (Float)high;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ShellMapTexture triangles: %d uscale: %f vscale: %f outside: %f ]", indices.Length / 3,
                                     (Float)uScale, (Float)vScale, (Float)outside);
        }

        public static ShellMapTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                             Allocator alloc)
        {
            FloatTexture inner = parameters.GetFloatTextureOrNull("inner", alloc);
            if (inner == null)
            {
                pbrt.ErrorExit(loc, "shellmap: \"texture inner\" is required.");
            }
            DisplacedTriangle.MeshData(parameters.GetOneString("filename", ""), parameters.GetIntArray("indices"),
                                       parameters.GetPoint3fArray("P"), parameters.GetNormal3fArray("N"), parameters.GetPoint2fArray("uv"),
                                       loc, out int[] indices, out Point3f[] p, out Normal3f[] n, out Point2f[] uv);
            return new ShellMapTexture(renderFromTexture, inner, indices, p, n, uv, parameters.GetOneFloat("thickness", 0.1f),
                                       parameters.GetOneFloat("uscale", 1), parameters.GetOneFloat("vscale", 1),
                                       parameters.GetOneFloat("outside", 0), parameters.GetOneBool("tightbounds", false));
        }

        // ShellMapTexture Private Members
        private readonly Transform renderFromTexture;
        private readonly FloatTexture inner;
        private readonly int[] indices;
        private readonly Point2f[] uv;
        private readonly SdfVector[] baseP, offset;
        private readonly ShellPrismTree tree;
        private readonly double uScale, vScale, outside, tolerance;
        private readonly bool tightBounds;
    }

}  // namespace pbrt
