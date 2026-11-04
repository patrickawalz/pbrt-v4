// (not in pbrt-v4) displacedpatch.cs
//
// Displacement of curved patches (the bicubic Bezier and Gregory patches of the subdivision surfaces, the PN, Phong and Gregory patches) at the finest
// level, without a tessellation of the patch. The displacement field is a grid of vectors (the ShellVectorField of the vector displacement of
// displacedmesh) that is addressed by the texture coordinates of the patch. The domain of a patch is subdivided in a quadtree (the cropped Bezier
// patches bound the base surface); a node is bounded by its Bezier patch grown by the bound of the displacement under its footprint, and a leaf is
// the size of a cell of the grid, which is displaced at its corners (two triangles).
//   scalar: a height map displaces along the unit normal of the limit surface (the vector (0, 0, h) of the tangent space); one bound for the patch
//   vector: a vector map displaces in the tangent space (or the object space) of the limit surface; one bound for the patch
//   shell:  as vector (or scalar) but the bound of every node comes from the min-max pyramid of the grid under its footprint, which skips most of
//           the nodes of a patch with a displacement that varies a lot
// The scalar and the vector modes are the displacement at the leaf of Tejima's clipping, the shell mode is the shell traversal of the displaced
// mesh on a curved patch. The bound of a node is the sphere of the largest displacement for the tangent space (the frame is orthonormal), and the exact
// box for the object space.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public enum PatchDisplacementMode
    {
        Scalar,
        Vector,
        Shell
    }

    public sealed class PatchDisplacement
    {
        public PatchDisplacementMode mode;
        public ShellVectorField field;
        public bool tangentSpace = true;
        public bool smoothNormals = true;
        public int maxDepth = 16;
        // Replaces the displacement along the border that patches share by the average of theirs, fading out over one cell of the grid
        public bool weldSeams = true;

        // The displacement of the parameters "displacement" ("none", "scalar", "vector", "shell"), "displacementmap" (an image) or "functionx",
        // "functiony", "functionz" (expressions of (u, v); "function" for the height of the scalar mode), "displacementscale", "displacementbias",
        // "resolution", "vectorspace" ("tangent", "object"), "smoothnormals", "displacementdepth", "weldseams"; null without displacement
        public static PatchDisplacement Create(ParameterDictionary parameters, FileLoc loc)
        {
            string name = parameters.GetOneString("displacement", "none");
            if (name == "none")
            {
                return null;
            }
            PatchDisplacement result = new PatchDisplacement();
            if (name == "scalar")
            {
                result.mode = PatchDisplacementMode.Scalar;
            }
            else if (name == "vector")
            {
                result.mode = PatchDisplacementMode.Vector;
            }
            else if (name == "shell")
            {
                result.mode = PatchDisplacementMode.Shell;
            }
            else
            {
                pbrt.ErrorExit(loc, "\"displacement\" must be \"none\", \"scalar\", \"vector\" or \"shell\".");
            }
            double scale = parameters.GetOneFloat("displacementscale", 1);
            double bias = parameters.GetOneFloat("displacementbias", 0);
            string space = parameters.GetOneString("vectorspace", "tangent");
            result.tangentSpace = space == "tangent";
            result.smoothNormals = parameters.GetOneBool("smoothnormals", true);
            result.maxDepth = parameters.GetOneInt("displacementdepth", 16);
            result.weldSeams = parameters.GetOneBool("weldseams", true);
            bool scalarField = result.mode == PatchDisplacementMode.Scalar;
            string mapName = parameters.GetOneString("displacementmap", "");
            if (mapName.Length != 0)
            {
                if (scalarField)
                {
                    result.field = new ShellVectorGrid(ReadHeights(pbrt.ResolveFilename(mapName), scale, bias));
                }
                else
                {
                    result.field = new ShellVectorGrid(new VectorMapImage(pbrt.ResolveFilename(mapName), scale, bias));
                }
            }
            else
            {
                int resolution = Math.Max(2, parameters.GetOneInt("resolution", 1024));
                int depth = 1;
                while ((1 << depth) < resolution - 1 && depth < 24)
                {
                    ++depth;
                }
                try
                {
                    if (scalarField)
                    {
                        result.field = new ShellVectorProcedural(ShellExpression.Parse("0"), ShellExpression.Parse("0"),
                                                                 ShellExpression.Parse(parameters.GetOneString("function", "0")), depth, scale);
                    }
                    else
                    {
                        result.field = new ShellVectorProcedural(ShellExpression.Parse(parameters.GetOneString("functionx", "0")),
                                                                 ShellExpression.Parse(parameters.GetOneString("functiony", "0")),
                                                                 ShellExpression.Parse(parameters.GetOneString("functionz", "0")), depth, scale);
                    }
                }
                catch (FormatException e)
                {
                    pbrt.ErrorExit(loc, "displacement: " + e.Message);
                }
            }
            return result;
        }

        // The vectors (0, 0, scale (h - bias)) of the first channel of an image
        private static VectorMapImage ReadHeights(string filename, double scale, double bias)
        {
            ImageAndMetadata loaded = Image.Read(filename, null, ColorEncoding.Linear);
            Image image = loaded.image;
            int width = image.Resolution().x;
            int height = image.Resolution().y;
            float[] values = new float[3 * width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    values[3 * (y * width + x) + 2] = (float)(scale * (image.GetChannel(new Point2i(x, height - 1 - y), 0) - bias));
                }
            }
            return new VectorMapImage(width, height, values);
        }

        // Replaces the clipped patches of a list of shapes by displaced patches
        public pstd.vector<Shape> Apply(pstd.vector<Shape> shapes, BezierSurfaceSettings settings)
        {
            pstd.vector<Shape> result = new pstd.vector<Shape>();
            for (int i = 0; i < (int)shapes.size(); ++i)
            {
                BezierClipPatchShape patch = shapes[i] as BezierClipPatchShape;
                if (patch == null)
                {
                    pbrt.ErrorExit("displacement: the shape cannot be displaced.");
                }
                result.push_back(new DisplacedPatchShape(settings, patch.Patch, patch.Surface, this));
            }
            if (weldSeams)
            {
                DisplacedPatchShape.Weld(result);
            }
            return result;
        }
    }

    public sealed unsafe class DisplacedPatchShape : Shape
    {
        public DisplacedPatchShape(BezierSurfaceSettings settings, in BezierPatch4 patch, PatchSurface surface, PatchDisplacement displacement)
        {
            this.settings = settings;
            this.surface = surface;
            this.patch = patch;
            this.displacement = displacement;
            field = displacement.field;
            // The texture coordinates are bilinear in the parameters of the patch
            double[] corner = new double[8];
            for (int c = 0; c < 4; ++c)
            {
                surface.Evaluate(c & 1, (c >> 1) & 1, out SdfVector p, out SdfVector du, out SdfVector dv, out Point2f uv, out SdfVector ns);
                corner[2 * c] = uv.x;
                corner[2 * c + 1] = uv.y;
            }
            texture = new PatchTextureMap(corner);
            tileU = Math.Floor((corner[0] + corner[2] + corner[4] + corner[6]) / 4);
            tileV = Math.Floor((corner[1] + corner[3] + corner[5] + corner[7]) / 4);
            GridFootprintBound(0, 1, 0, 1, out rootLow, out rootHigh);
            patch.GetMinMax(out SdfVector low, out SdfVector high, 0);
            Grow(ref low, ref high, rootLow, rootHigh);
            Bounds3f result = new Bounds3f();
            for (int c = 0; c < 8; ++c)
            {
                Point3f point = settings.renderFromObject.Apply(new Point3f((Float)((c & 1) != 0 ? high.x : low.x), (Float)((c & 2) != 0 ? high.y : low.y),
                                                                            (Float)((c & 4) != 0 ? high.z : low.z)));
                result = c == 0 ? new Bounds3f(point) : pbrt.Union(result, point);
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
            if (!Trace(ray, tMax, false, out Hit hit, out double length))
            {
                return default;
            }
            SurfaceInteraction intr = InteractionFromHit(ray, hit);
            return new ShapeIntersection(intr, (Float)(hit.t / length));
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Trace(ray, tMax, true, out Hit hit, out double length);
        }

        // The sampler of the area of the displaced surface (built when first needed): the cells of the table are those of the footprint of a grid of 32 x 32
        private PatchAreaSampler Sampler()
        {
            PatchAreaSampler result = sampler;
            if (result == null)
            {
                result = new PatchAreaSampler((double u, double v) =>
                {
                    SdfVector p = Displaced(u, v);
                    Point3f q = settings.renderFromObject.Apply(new Point3f((Float)p.x, (Float)p.y, (Float)p.z));
                    return new SdfVector(q.x, q.y, q.z);
                }, 32);
                sampler = result;
            }
            return result;
        }

        public override Float Area()
        {
            return (Float)Sampler().Area;
        }

        // The point of the displaced surface at the parameters (u, v), with its normal
        private Interaction InteractionAt(double u, double v)
        {
            PatchAreaSampler areaSampler = Sampler();
            SdfVector p = Displaced(u, v);
            areaSampler.Differences(u, v, out SdfVector dpdu, out SdfVector dpdv);
            texture.Convert(u, v, dpdu, dpdv, out Point2f uv, out SdfVector pu, out SdfVector pv);
            Point3f pr = settings.renderFromObject.Apply(new Point3f((Float)p.x, (Float)p.y, (Float)p.z));
            Normal3f n = pbrt.Normalize(new Normal3f((Float)(pu.y * pv.z - pu.z * pv.y), (Float)(pu.z * pv.x - pu.x * pv.z), (Float)(pu.x * pv.y - pu.y * pv.x)));
            if (settings.reverseOrientation ^ settings.transformSwapsHandedness)
            {
                n = -n;
            }
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            return new Interaction(new Point3fi(pr, pError), n, uv);
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            PatchAreaSampler areaSampler = Sampler();
            areaSampler.Sample(u, out double pu, out double pv, out double density);
            double jacobian = areaSampler.Jacobian(pu, pv);
            if (!(jacobian > 0))
            {
                return default;
            }
            return new ShapeSample(InteractionAt(pu, pv), (Float)(density / jacobian));
        }

        public override Float PDF(Interaction intr)
        {
            return 1 / Area();
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return PatchSampling.ToSolidAngle(Sample(u), ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            if (!Trace(ray, Infinity, false, out Hit hit, out double length))
            {
                return 0;
            }
            Interaction at = InteractionAt(hit.u, hit.v);
            return PatchSampling.AreaToSolidAngle(Sampler().AreaDensity(hit.u, hit.v), at.n, at.p(), ctx, wi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ DisplacedPatchShape face: %d ]", surface.FaceIndex());
        }

        private struct Hit
        {
            public double t;
            public double u, v;
            public double u0, u1, v0, v1;
            public bool upper;
        }

        // The displacement vector at a point of the grid, bilinearly interpolated
        private void Vector(double gridX, double gridY, out double vx, out double vy, out double vz)
        {
            double fx = Math.Max(0, Math.Min(field.cellsX, gridX));
            double fy = Math.Max(0, Math.Min(field.cellsY, gridY));
            int ix = Math.Min((int)fx, field.cellsX - 1), iy = Math.Min((int)fy, field.cellsY - 1);
            double tx = fx - ix, ty = fy - iy;
            field.Sample(ix, iy, out double ax, out double ay, out double az);
            field.Sample(ix + 1, iy, out double bx, out double by, out double bz);
            field.Sample(ix, iy + 1, out double cx, out double cy, out double cz);
            field.Sample(ix + 1, iy + 1, out double dx, out double dy, out double dz);
            vx = (1 - ty) * ((1 - tx) * ax + tx * bx) + ty * ((1 - tx) * cx + tx * dx);
            vy = (1 - ty) * ((1 - tx) * ay + tx * by) + ty * ((1 - tx) * cy + tx * dy);
            vz = (1 - ty) * ((1 - tx) * az + tx * bz) + ty * ((1 - tx) * cz + tx * dz);
        }

        // The snapped grid coordinates of the texture coordinates
        private void GridPoint(double textureU, double textureV, out double gridX, out double gridY)
        {
            gridX = (textureU - tileU) * field.cellsX;
            gridY = (textureV - tileV) * field.cellsY;
            double rx = Math.Round(gridX), ry = Math.Round(gridY);
            if (Math.Abs(gridX - rx) < 1e-3)
            {
                gridX = rx;
            }
            if (Math.Abs(gridY - ry) < 1e-3)
            {
                gridY = ry;
            }
        }

        // The displaced point at the parameters (u, v) of the patch, with the correction of the seams
        private SdfVector Displaced(double u, double v)
        {
            SdfVector point = RawDisplaced(u, v);
            return edgeCorrection == null ? point : point + SeamCorrection(u, v);
        }

        // The correction of the offset along the four borders (the welded offset minus the own one at the border), fading out over one cell
        private SdfVector SeamCorrection(double u, double v)
        {
            double cells = edgeCorrection[0].Length - 1;
            SdfVector sum = new SdfVector(0, 0, 0);
            double weights = 0;
            for (int e = 0; e < 4; ++e)
            {
                double along = e == 0 ? u : e == 1 ? v : e == 2 ? u : v;
                double across = e == 0 ? v : e == 1 ? 1 - u : e == 2 ? 1 - v : u;
                double weight = 1 - across * cells;
                if (weight <= 0)
                {
                    continue;
                }
                double position = Math.Max(0, Math.Min(cells, along * cells));
                int index = Math.Min((int)position, (int)cells - 1);
                double fraction = position - index;
                sum = sum + weight * ((1 - fraction) * edgeCorrection[e][index] + fraction * edgeCorrection[e][index + 1]);
                weights += weight;
            }
            return weights > 1 ? sum / weights : sum;
        }

        // Welds the borders that the displaced patches of a surface share: the offset at a point of a border becomes the average of the offsets of the patches
        // that meet there (matched by the position of the base surface; a border between patches of different levels has no matching points and is left alone)
        public static void Weld(pstd.vector<Shape> shapes)
        {
            List<DisplacedPatchShape> patches = new List<DisplacedPatchShape>();
            for (int i = 0; i < (int)shapes.size(); ++i)
            {
                DisplacedPatchShape patch = shapes[i] as DisplacedPatchShape;
                if (patch != null)
                {
                    patches.Add(patch);
                }
            }
            Dictionary<string, List<int[]>> byPosition = new Dictionary<string, List<int[]>>();
            List<SdfVector[][]> offsets = new List<SdfVector[][]>();
            for (int a = 0; a < patches.Count; ++a)
            {
                DisplacedPatchShape patch = patches[a];
                int cells = Math.Max(1, Math.Max(patch.field.cellsX, patch.field.cellsY));
                SdfVector[][] own = new SdfVector[4][];
                for (int e = 0; e < 4; ++e)
                {
                    own[e] = new SdfVector[cells + 1];
                    for (int k = 0; k <= cells; ++k)
                    {
                        double t = (double)k / cells;
                        double u = e == 0 ? t : e == 1 ? 1 : e == 2 ? t : 0, v = e == 0 ? 0 : e == 1 ? t : e == 2 ? 1 : t;
                        patch.surface.Evaluate(u, v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector shading);
                        own[e][k] = patch.RawDisplaced(u, v) - p;
                        string key = Math.Round(p.x * 1e5) + "," + Math.Round(p.y * 1e5) + "," + Math.Round(p.z * 1e5);
                        if (!byPosition.TryGetValue(key, out List<int[]> list))
                        {
                            list = new List<int[]>();
                            byPosition[key] = list;
                        }
                        list.Add(new int[] { a, e, k });
                    }
                }
                offsets.Add(own);
            }
            SdfVector[][][] correction = new SdfVector[patches.Count][][];
            double[] pad = new double[patches.Count];
            for (int a = 0; a < patches.Count; ++a)
            {
                correction[a] = new SdfVector[4][];
                for (int e = 0; e < 4; ++e)
                {
                    correction[a][e] = new SdfVector[offsets[a][e].Length];
                }
            }
            foreach (List<int[]> list in byPosition.Values)
            {
                // One offset per patch, whichever border or corner of it the point is on
                Dictionary<int, SdfVector> perPatch = new Dictionary<int, SdfVector>();
                foreach (int[] entry in list)
                {
                    if (!perPatch.ContainsKey(entry[0]))
                    {
                        perPatch[entry[0]] = offsets[entry[0]][entry[1]][entry[2]];
                    }
                }
                if (perPatch.Count < 2)
                {
                    continue;
                }
                SdfVector average = new SdfVector(0, 0, 0);
                foreach (SdfVector offset in perPatch.Values)
                {
                    average = average + offset;
                }
                average = average / perPatch.Count;
                foreach (int[] entry in list)
                {
                    SdfVector delta = average - offsets[entry[0]][entry[1]][entry[2]];
                    correction[entry[0]][entry[1]][entry[2]] = delta;
                    pad[entry[0]] = Math.Max(pad[entry[0]], delta.Length());
                }
            }
            for (int a = 0; a < patches.Count; ++a)
            {
                patches[a].edgeCorrection = correction[a];
                patches[a].weldPad = pad[a];
                patches[a].GrowBounds(pad[a]);
            }
        }

        // The bounds of the shape grown by the distance that the welding moves the surface
        private void GrowBounds(double distance)
        {
            if (!(distance > 0))
            {
                return;
            }
            Vector3f px = settings.renderFromObject.Apply(new Vector3f(1, 0, 0)), py = settings.renderFromObject.Apply(new Vector3f(0, 1, 0)), pz = settings.renderFromObject.Apply(new Vector3f(0, 0, 1));
            double scale = Math.Max(pbrt.Length(px), Math.Max(pbrt.Length(py), pbrt.Length(pz)));
            Vector3f grow = new Vector3f((Float)(distance * scale * 1.01), (Float)(distance * scale * 1.01), (Float)(distance * scale * 1.01));
            bounds = new Bounds3f(bounds.pMin - grow, bounds.pMax + grow);
        }

        // The displaced point at the parameters (u, v) of the patch, as the field gives it
        private SdfVector RawDisplaced(double u, double v)
        {
            surface.Evaluate(u, v, out SdfVector p, out SdfVector dpdu, out SdfVector dpdv, out Point2f uv, out SdfVector shading);
            // At a degenerate point (a corner at an extraordinary vertex) the frame is that of a point nearby
            for (int attempt = 0; attempt < 4; ++attempt)
            {
                double lu = dpdu.LengthSquared(), lv = dpdv.LengthSquared();
                if (lu > 0 && lv > 0 && SdfVector.Cross(dpdu, dpdv).LengthSquared() > 1e-18 * lu * lv)
                {
                    break;
                }
                double shrink = 1e-3 * (attempt + 1);
                surface.Evaluate(u + (0.5 - u) * shrink, v + (0.5 - v) * shrink, out SdfVector p2, out dpdu, out dpdv, out Point2f uv2, out shading);
            }
            texture.Evaluate(u, v, out double textureU, out double textureV);
            GridPoint(textureU, textureV, out double gridX, out double gridY);
            Vector(gridX, gridY, out double vx, out double vy, out double vz);
            if (!displacement.tangentSpace)
            {
                return p + new SdfVector(vx, vy, vz);
            }
            SdfVector n = shading.LengthSquared() > 0 ? shading.Normalized() : SdfVector.Cross(dpdu, dpdv).Normalized();
            SdfVector t = dpdu - SdfVector.Dot(dpdu, n) * n;
            t = t.LengthSquared() > 0 ? t.Normalized() : SdfVector.Cross(n, Math.Abs(n.x) < 0.9 ? new SdfVector(1, 0, 0) : new SdfVector(0, 1, 0)).Normalized();
            SdfVector b = SdfVector.Cross(n, t);
            return p + vx * t + vy * b + vz * n;
        }

        // The bound of the displacement under a rectangle of the parameters: the box of the vectors of the grid under its footprint
        private void GridFootprintBound(double u0, double u1, double v0, double v1, out SdfVector low, out SdfVector high)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = -double.MaxValue, maxY = -double.MaxValue;
            for (int c = 0; c < 4; ++c)
            {
                texture.Evaluate((c & 1) != 0 ? u1 : u0, (c & 2) != 0 ? v1 : v0, out double textureU, out double textureV);
                GridPoint(textureU, textureV, out double gridX, out double gridY);
                minX = Math.Min(minX, gridX);
                maxX = Math.Max(maxX, gridX);
                minY = Math.Min(minY, gridY);
                maxY = Math.Max(maxY, gridY);
            }
            int cellX0 = Math.Max(0, Math.Min(field.cellsX - 1, (int)Math.Floor(minX + 1e-9)));
            int cellX1 = Math.Max(0, Math.Min(field.cellsX - 1, (int)Math.Ceiling(maxX - 1e-9) - 1));
            int cellY0 = Math.Max(0, Math.Min(field.cellsY - 1, (int)Math.Floor(minY + 1e-9)));
            int cellY1 = Math.Max(0, Math.Min(field.cellsY - 1, (int)Math.Ceiling(maxY - 1e-9) - 1));
            cellX1 = Math.Max(cellX1, cellX0);
            cellY1 = Math.Max(cellY1, cellY0);
            // The smallest level of the pyramid whose nodes cover the footprint with at most two nodes along each axis
            int level = 0;
            while (level < field.levels - 1 && ((cellX1 >> level) - (cellX0 >> level) > 1 || (cellY1 >> level) - (cellY0 >> level) > 1))
            {
                ++level;
            }
            double[] box = boundBox ??= new double[6];
            double[] sum = boundSum ??= new double[6];
            for (int c = 0; c < 3; ++c)
            {
                sum[c] = double.MaxValue;
                sum[3 + c] = -double.MaxValue;
            }
            for (int j = cellY0 >> level; j <= cellY1 >> level; ++j)
            {
                for (int i = cellX0 >> level; i <= cellX1 >> level; ++i)
                {
                    field.NodeBox(level, i, j, box);
                    for (int c = 0; c < 3; ++c)
                    {
                        sum[c] = Math.Min(sum[c], box[c]);
                        sum[3 + c] = Math.Max(sum[3 + c], box[3 + c]);
                    }
                }
            }
            low = new SdfVector(sum[0], sum[1], sum[2]);
            high = new SdfVector(sum[3], sum[4], sum[5]);
        }

        // low and high grown by the displacement bounded by [displacementLow, displacementHigh]
        private void Grow(ref SdfVector low, ref SdfVector high, SdfVector displacementLow, SdfVector displacementHigh)
        {
            if (weldPad > 0)
            {
                low = low - new SdfVector(weldPad, weldPad, weldPad);
                high = high + new SdfVector(weldPad, weldPad, weldPad);
            }
            if (displacement.tangentSpace)
            {
                double x = Math.Max(Math.Abs(displacementLow.x), Math.Abs(displacementHigh.x));
                double y = Math.Max(Math.Abs(displacementLow.y), Math.Abs(displacementHigh.y));
                double z = Math.Max(Math.Abs(displacementLow.z), Math.Abs(displacementHigh.z));
                double radius = Math.Sqrt(x * x + y * y + z * z);
                low = low - new SdfVector(radius, radius, radius);
                high = high + new SdfVector(radius, radius, radius);
            }
            else
            {
                low = low + displacementLow;
                high = high + displacementHigh;
            }
        }

        [ThreadStatic]
        private static double[] boundBox;
        [ThreadStatic]
        private static double[] boundSum;

        private bool Trace(Ray ray, Float tMax, bool anyHit, out Hit hit, out double length)
        {
            hit = default;
            Point3f o = settings.objectFromRender.Apply(ray.o);
            Vector3f d = settings.objectFromRender.Apply(ray.d);
            length = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
            if (length == 0 || double.IsNaN(length))
            {
                return false;
            }
            SdfVector origin = new SdfVector(o.x, o.y, o.z);
            SdfVector direction = new SdfVector(d.x / length, d.y / length, d.z / length);
            hit.t = float.IsPositiveInfinity(tMax) ? 1e30 : tMax * length;
            bool found = Visit(patch, 0, 1, 0, 1, 0, origin, direction, anyHit, ref hit);
            return found;
        }

        private bool Visit(in BezierPatch4 node, double u0, double u1, double v0, double v1, int depth, SdfVector origin, SdfVector direction,
                           bool anyHit, ref Hit hit)
        {
            SdfVector low, high;
            node.GetMinMax(out low, out high, 0);
            SdfVector boundLow, boundHigh;
            GridFootprintBound(u0, u1, v0, v1, out boundLow, out boundHigh);
            Grow(ref low, ref high, boundLow, boundHigh);
            if (!SlabTest(origin, direction, low, high, hit.t, out double entry))
            {
                return false;
            }

            // The size of the footprint on the grid
            double minX = double.MaxValue, minY = double.MaxValue, maxX = -double.MaxValue, maxY = -double.MaxValue;
            for (int c = 0; c < 4; ++c)
            {
                texture.Evaluate((c & 1) != 0 ? u1 : u0, (c & 2) != 0 ? v1 : v0, out double textureU, out double textureV);
                GridPoint(textureU, textureV, out double gridX, out double gridY);
                minX = Math.Min(minX, gridX);
                maxX = Math.Max(maxX, gridX);
                minY = Math.Min(minY, gridY);
                maxY = Math.Max(maxY, gridY);
            }
            if ((maxX - minX <= 1 + 1e-9 && maxY - minY <= 1 + 1e-9) || depth >= displacement.maxDepth)
            {
                return TestLeaf(u0, u1, v0, v1, origin, direction, ref hit);
            }

            double um = 0.5 * (u0 + u1), vm = 0.5 * (v0 + v1);
            BezierPatch4* children = stackalloc BezierPatch4[4];
            node.Split4(out children[0], out children[1], out children[2], out children[3], 0.5, 0.5);
            double* us = stackalloc double[4];
            double* ue = stackalloc double[4];
            double* vs = stackalloc double[4];
            double* ve = stackalloc double[4];
            us[0] = u0; ue[0] = um; vs[0] = v0; ve[0] = vm;
            us[1] = um; ue[1] = u1; vs[1] = v0; ve[1] = vm;
            us[2] = u0; ue[2] = um; vs[2] = vm; ve[2] = v1;
            us[3] = um; ue[3] = u1; vs[3] = vm; ve[3] = v1;
            // The children in the order of the entry of the ray into their boxes
            double* order = stackalloc double[4];
            int* index = stackalloc int[4];
            for (int k = 0; k < 4; ++k)
            {
                children[k].GetMinMax(out SdfVector cl, out SdfVector ch, 0);
                Grow(ref cl, ref ch, boundLow, boundHigh);
                order[k] = SlabTest(origin, direction, cl, ch, hit.t, out double e) ? e : double.MaxValue;
                int position = k;
                while (position > 0 && order[index[position - 1]] > order[k])
                {
                    index[position] = index[position - 1];
                    --position;
                }
                index[position] = k;
            }
            bool found = false;
            for (int k = 0; k < 4; ++k)
            {
                int child = index[k];
                if (order[child] == double.MaxValue || order[child] > hit.t)
                {
                    break;
                }
                if (Visit(in children[child], us[child], ue[child], vs[child], ve[child], depth + 1, origin, direction, anyHit, ref hit))
                {
                    found = true;
                    if (anyHit)
                    {
                        return true;
                    }
                }
            }
            return found;
        }

        private static bool SlabTest(SdfVector origin, SdfVector direction, SdfVector low, SdfVector high, double tMax, out double entry)
        {
            double t0 = 0, t1 = tMax;
            for (int axis = 0; axis < 3; ++axis)
            {
                double inverse = 1 / direction[axis];
                double near = (low[axis] - origin[axis]) * inverse;
                double far = (high[axis] - origin[axis]) * inverse;
                if (near > far)
                {
                    double swap = near;
                    near = far;
                    far = swap;
                }
                far *= 1 + 2 * 1.2e-7;
                t0 = near > t0 ? near : t0;
                t1 = far < t1 ? far : t1;
                if (t0 > t1)
                {
                    entry = 0;
                    return false;
                }
            }
            entry = t0;
            return true;
        }

        // The two triangles of the displaced leaf
        private bool TestLeaf(double u0, double u1, double v0, double v1, SdfVector origin, SdfVector direction, ref Hit hit)
        {
            SdfVector p00 = Displaced(u0, v0), p10 = Displaced(u1, v0), p01 = Displaced(u0, v1), p11 = Displaced(u1, v1);
            bool found = false;
            if (Moeller(origin, direction, p00, p10, p01, hit.t, out double t, out double a, out double b))
            {
                hit.t = t;
                hit.u = u0 + a * (u1 - u0);
                hit.v = v0 + b * (v1 - v0);
                hit.upper = false;
                found = true;
            }
            if (Moeller(origin, direction, p10, p11, p01, hit.t, out t, out a, out b))
            {
                hit.t = t;
                hit.u = u0 + (1 - b) * (u1 - u0);
                hit.v = v0 + (a + b) * (v1 - v0);
                hit.upper = true;
                found = true;
            }
            if (found)
            {
                hit.u0 = u0;
                hit.u1 = u1;
                hit.v0 = v0;
                hit.v1 = v1;
            }
            return found;
        }

        private static bool Moeller(SdfVector o, SdfVector d, SdfVector p0, SdfVector p1, SdfVector p2, double tMax, out double t, out double a, out double b)
        {
            t = 0;
            a = 0;
            b = 0;
            SdfVector e1 = p1 - p0, e2 = p2 - p0;
            SdfVector pv = SdfVector.Cross(d, e2);
            double det = SdfVector.Dot(e1, pv);
            if (Math.Abs(det) < 1e-300)
            {
                return false;
            }
            SdfVector s = o - p0;
            const double tolerance = 1e-9;
            a = SdfVector.Dot(s, pv) / det;
            if (a < -tolerance || a > 1 + tolerance)
            {
                return false;
            }
            SdfVector q = SdfVector.Cross(s, e1);
            b = SdfVector.Dot(d, q) / det;
            if (b < -tolerance || a + b > 1 + tolerance)
            {
                return false;
            }
            t = SdfVector.Dot(e2, q) / det;
            return t > 0 && t < tMax;
        }

        private SurfaceInteraction InteractionFromHit(Ray ray, Hit hit)
        {
            double du = hit.u1 - hit.u0, dv = hit.v1 - hit.v0;
            SdfVector p00 = Displaced(hit.u0, hit.v0), p10 = Displaced(hit.u1, hit.v0), p01 = Displaced(hit.u0, hit.v1), p11 = Displaced(hit.u1, hit.v1);
            SdfVector dpdu, dpdv;
            if (!hit.upper)
            {
                dpdu = (p10 - p00) / du;
                dpdv = (p01 - p00) / dv;
            }
            else
            {
                dpdu = (p11 - p01) / du;
                dpdv = (p11 - p10) / dv;
            }
            SdfVector position = ray_o(ray) + ray_d(ray) * hit.t;
            texture.Evaluate(hit.u, hit.v, out double textureU, out double textureV);
            texture.Convert(hit.u, hit.v, dpdu, dpdv, out Point2f uv, out SdfVector pu, out SdfVector pv);

            SdfVector shading = new SdfVector(0, 0, 0);
            if (displacement.smoothNormals)
            {
                SdfVector[] cornerNormal = new SdfVector[4];
                for (int c = 0; c < 4; ++c)
                {
                    double cu = (c & 1) != 0 ? hit.u1 : hit.u0, cv = (c & 2) != 0 ? hit.v1 : hit.v0;
                    double ua = Math.Max(0, cu - du), ub = Math.Min(1, cu + du), va = Math.Max(0, cv - dv), vb = Math.Min(1, cv + dv);
                    SdfVector tu = Displaced(ub, cv) - Displaced(ua, cv);
                    SdfVector tv = Displaced(cu, vb) - Displaced(cu, va);
                    cornerNormal[c] = SdfVector.Cross(tu, tv);
                }
                double lu = (hit.u - hit.u0) / du, lv = (hit.v - hit.v0) / dv;
                shading = (1 - lu) * (1 - lv) * cornerNormal[0] + lu * (1 - lv) * cornerNormal[1] + (1 - lu) * lv * cornerNormal[2] + lu * lv * cornerNormal[3];
                SdfVector geometric = SdfVector.Cross(pu, pv);
                if (shading.LengthSquared() > 0 && SdfVector.Dot(shading, geometric) < 0)
                {
                    shading = -shading;
                }
            }

            Transform renderFromObject = settings.renderFromObject;
            Point3f pr = renderFromObject.Apply(new Point3f((Float)position.x, (Float)position.y, (Float)position.z));
            Vector3f pur = renderFromObject.Apply(new Vector3f((Float)pu.x, (Float)pu.y, (Float)pu.z));
            Vector3f pvr = renderFromObject.Apply(new Vector3f((Float)pv.x, (Float)pv.y, (Float)pv.z));
            bool flipNormal = settings.reverseOrientation ^ settings.transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            SurfaceInteraction si = new SurfaceInteraction(new Point3fi(pr, pError), uv, -ray.d, pur, pvr, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0),
                                                           ray.time, flipNormal, surface.FaceIndex());
            if (shading.LengthSquared() > 0)
            {
                Normal3f ns = pbrt.Normalize(renderFromObject.Apply(new Normal3f((Float)shading.x, (Float)shading.y, (Float)shading.z)));
                if (settings.reverseOrientation)
                {
                    ns = -ns;
                }
                PatchShading.Apply(si, ns, pur);
            }
            return si;
        }

        private SdfVector ray_o(Ray ray)
        {
            Point3f o = settings.objectFromRender.Apply(ray.o);
            return new SdfVector(o.x, o.y, o.z);
        }

        private SdfVector ray_d(Ray ray)
        {
            Vector3f d = settings.objectFromRender.Apply(ray.d);
            double length = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
            return new SdfVector(d.x / length, d.y / length, d.z / length);
        }

        private PatchAreaSampler sampler;
        private SdfVector[][] edgeCorrection;
        private double weldPad;
        private readonly BezierSurfaceSettings settings;
        private readonly PatchSurface surface;
        private readonly BezierPatch4 patch;
        private readonly PatchDisplacement displacement;
        private readonly ShellVectorField field;
        private readonly PatchTextureMap texture;
        private readonly double tileU, tileV;
        private readonly SdfVector rootLow, rootHigh;
        private Bounds3f bounds;
    }
}
